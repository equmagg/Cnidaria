using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Cnidaria.Cs
{
    internal sealed class SsaOptimizationOptions
    {
        public static SsaOptimizationOptions Default => new SsaOptimizationOptions();
        public static SsaOptimizationOptions DefaultWithoutValidation => new SsaOptimizationOptions { Validate = false };

        public bool Validate { get; set; } = true;
        public bool PropagateConstants { get; set; } = true;
        public bool FoldConstants { get; set; } = true;
        public bool SimplifyAlgebraicIdentities { get; set; } = true;
        public bool RemoveDeadDefinitions { get; set; } = true;
        public bool CopyPropagate { get; set; } = true;
        public bool FoldRedundantBranches { get; set; } = true;
        public bool JumpThreadBranches { get; set; } = true;
        public bool RemoveUnreachableBlocks { get; set; } = true;
        public int MaxBranchOptimizationPasses { get; set; } = 4;
        public int MaxIterations { get; set; } = 8;
    }
    internal static class SsaOptimizer
    {
        public static SsaMethod OptimizeMethod(SsaMethod method, SsaOptimizationOptions? options = null)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            options ??= SsaOptimizationOptions.Default;
            if (options.MaxIterations <= 0)
                return method;

            var optimizer = new MethodOptimizer(method, options);
            var result = optimizer.Run();

            if (options.Validate)
                SsaVerifier.Verify(result);

            return result;
        }

        private enum ConstKind : byte
        {
            I4,
            I8,
            Null,
        }

        private readonly struct ConstValue : IEquatable<ConstValue>
        {
            public readonly ConstKind Kind;
            public readonly int I4;
            public readonly long I8;

            private ConstValue(ConstKind kind, int i4, long i8)
            {
                Kind = kind;
                I4 = i4;
                I8 = i8;
            }

            public static ConstValue ForI4(int value) => new ConstValue(ConstKind.I4, value, value);
            public static ConstValue ForI8(long value) => new ConstValue(ConstKind.I8, unchecked((int)value), value);
            public static ConstValue Null => new ConstValue(ConstKind.Null, 0, 0);

            public bool Equals(ConstValue other) => Kind == other.Kind && I4 == other.I4 && I8 == other.I8;
            public override bool Equals(object? obj) => obj is ConstValue other && Equals(other);
            public override int GetHashCode() => ((int)Kind * 397) ^ I4 ^ I8.GetHashCode();
        }

        private enum ValueFactKind : byte
        {
            Unknown,
            Constant,
        }

        private readonly struct ValueFact : IEquatable<ValueFact>
        {
            public readonly ValueFactKind Kind;
            public readonly ConstValue Constant;

            private ValueFact(ValueFactKind kind, ConstValue constant)
            {
                Kind = kind;
                Constant = constant;
            }

            public static ValueFact Unknown => default;
            public static ValueFact ForConstant(ConstValue constant) => new ValueFact(ValueFactKind.Constant, constant);

            public bool Equals(ValueFact other)
            {
                if (Kind != other.Kind)
                    return false;

                return Kind switch
                {
                    ValueFactKind.Constant => Constant.Equals(other.Constant),
                    _ => true,
                };
            }

            public override bool Equals(object? obj) => obj is ValueFact other && Equals(other);
            public override int GetHashCode() => Kind switch
            {
                ValueFactKind.Constant => ((int)Kind * 397) ^ Constant.GetHashCode(),
                _ => 0,
            };
        }

        private sealed class OptimizationResult
        {
            public ImmutableArray<SsaBlock> Blocks { get; }
            public bool Changed { get; }

            public OptimizationResult(ImmutableArray<SsaBlock> blocks, bool changed)
            {
                Blocks = blocks;
                Changed = changed;
            }
        }

        private sealed class MethodOptimizer
        {
            private readonly SsaMethod _original;
            private readonly SsaOptimizationOptions _options;
            private readonly Dictionary<SsaSlot, SsaSlotInfo> _slotInfos = new();
            private int _nextSyntheticTreeId;

            public MethodOptimizer(SsaMethod method, SsaOptimizationOptions options)
            {
                _original = method;
                _options = options;
                for (int i = 0; i < method.Slots.Length; i++)
                    _slotInfos[method.Slots[i].Slot] = method.Slots[i];
                _nextSyntheticTreeId = MaxTreeId(method) + 1;
            }

            public SsaMethod Run()
            {
                var current = EnsureValueNumbers(_original);

                for (int iteration = 0; iteration < _options.MaxIterations; iteration++)
                {
                    current = EnsureValueNumbers(current);

                    var pointLiveness = SsaLocalLiveness.Build(current);
                    OptimizationResult copyProp = _options.CopyPropagate
                        ? CopyPropagate(current, pointLiveness)
                        : new OptimizationResult(current.Blocks, changed: false);

                    var afterCopyProp = copyProp.Changed
                        ? EnsureValueNumbers(WithBlocks(current, copyProp.Blocks))
                        : current;

                    pointLiveness = SsaLocalLiveness.Build(afterCopyProp);
                    var facts = ComputeFacts(afterCopyProp);
                    var rewrite = Rewrite(afterCopyProp, facts, pointLiveness);
                    var afterRewrite = WithBlocks(afterCopyProp, rewrite.Blocks);

                    OptimizationResult dce = _options.RemoveDeadDefinitions
                        ? EliminateDeadDefinitions(afterRewrite)
                        : new OptimizationResult(afterRewrite.Blocks, changed: false);

                    var next = WithBlocks(afterRewrite, dce.Blocks);
                    bool changed = copyProp.Changed || rewrite.Changed || dce.Changed;

                    if ((_options.FoldRedundantBranches || _options.JumpThreadBranches || _options.RemoveUnreachableBlocks) &&
                        _options.MaxBranchOptimizationPasses > 0)
                    {
                        next = EnsureValueNumbers(next);
                        var flow = OptimizeRedundantBranches(next);
                        _nextSyntheticTreeId = Math.Max(_nextSyntheticTreeId, flow.NextSyntheticTreeId);
                        if (flow.Changed)
                        {
                            current = flow.Method;
                            continue;
                        }

                        next = flow.Method;
                    }

                    if (!changed)
                    {
                        current = EnsureValueNumbers(next);
                        break;
                    }

                    current = EnsureValueNumbers(next);
                }

                var useDefLinks = ExtractUseDefLinks(current.ValueDefinitions);
                var definitions = BuildValueDefinitions(current.Slots, current.InitialValues, current.Blocks, useDefLinks);
                var memoryDefinitions = GenTreeSsaBuilder.BuildMemoryDefinitions(current.InitialMemoryValues, current.Blocks);
                GenTreeSsaBuilder.AnnotateSsaUses(definitions, memoryDefinitions, current.Blocks);
                var localDescriptors = BuildSsaLocalDescriptors(current.Slots, definitions, current.SsaLocalDescriptors);
                var finalWithoutVn = new SsaMethod(
                    current.GenTreeMethod,
                    current.Cfg,
                    current.Slots,
                    current.InitialValues,
                    definitions,
                    current.Blocks,
                    valueNumbers: null,
                    ssaLocalDescriptors: localDescriptors,
                    initialMemoryValues: current.InitialMemoryValues,
                    memoryDefinitions: memoryDefinitions);
                return SsaValueNumbering.BuildMethod(finalWithoutVn);
            }




            private readonly struct FlowOptimizationResult
            {
                public readonly SsaMethod Method;
                public readonly bool Changed;
                public readonly int NextSyntheticTreeId;

                public FlowOptimizationResult(SsaMethod method, bool changed, int nextSyntheticTreeId)
                {
                    Method = method ?? throw new ArgumentNullException(nameof(method));
                    Changed = changed;
                    NextSyntheticTreeId = nextSyntheticTreeId;
                }
            }

            private FlowOptimizationResult OptimizeRedundantBranches(SsaMethod method)
            {
                method = EnsureValueNumbers(method);

                if (method.ValueNumbers is null || method.Blocks.Length == 0)
                    return new FlowOptimizationResult(method, false, _nextSyntheticTreeId);

                var current = method;
                bool anyChange = false;
                int nextTreeId = _nextSyntheticTreeId;
                int maxPasses = Math.Max(1, _options.MaxBranchOptimizationPasses);

                for (int pass = 0; pass < maxPasses; pass++)
                {
                    var passOptimizer = new RedundantBranchOptimizer(current, _options, nextTreeId);
                    var passResult = passOptimizer.Run();
                    nextTreeId = passResult.NextSyntheticTreeId;

                    if (!passResult.Changed)
                        break;

                    anyChange = true;
                    current = RebuildSsaAfterFlowRewrite(current, passResult.Method);
                    current = EnsureValueNumbers(current);
                }

                return new FlowOptimizationResult(current, anyChange, nextTreeId);
            }

            private SsaMethod RebuildSsaAfterFlowRewrite(SsaMethod previous, GenTreeMethod rewritten)
            {
                bool includeExceptionEdges = HasExceptionEdges(previous.Cfg);
                var cfg = ControlFlowGraph.Build(rewritten, includeExceptionEdges);
                rewritten.AttachFlowGraph(cfg);

                var liveness = GenTreeLocalLiveness.Build(rewritten, cfg);
                rewritten.AttachHirLiveness(liveness);

                var rebuilt = GenTreeSsaBuilder.BuildMethod(rewritten, cfg, liveness, validate: _options.Validate);
                return EnsureValueNumbers(rebuilt);
            }

            private static bool HasExceptionEdges(ControlFlowGraph cfg)
            {
                for (int b = 0; b < cfg.Blocks.Length; b++)
                {
                    var successors = cfg.Blocks[b].Successors;
                    for (int s = 0; s < successors.Length; s++)
                    {
                        if (successors[s].Kind == CfgEdgeKind.Exception)
                            return true;
                    }
                }

                return false;
            }

            private sealed class RedundantBranchOptimizer
            {
                private readonly SsaMethod _method;
                private readonly SsaOptimizationOptions _options;
                private readonly Dictionary<int, BranchInfo> _branches = new();
                private readonly Dictionary<int, MutableBlock> _edits = new();
                private int _nextTreeId;
                private bool _changed;

                public RedundantBranchOptimizer(SsaMethod method, SsaOptimizationOptions options, int nextTreeId)
                {
                    _method = method ?? throw new ArgumentNullException(nameof(method));
                    _options = options ?? throw new ArgumentNullException(nameof(options));
                    _nextTreeId = nextTreeId;
                }

                public FlowRewriteResult Run()
                {
                    BuildBranchIndex();

                    if (_branches.Count == 0)
                        return new FlowRewriteResult(_method.GenTreeMethod, false, _nextTreeId);

                    if (_options.FoldRedundantBranches)
                        FoldBranches();

                    if (_options.JumpThreadBranches)
                        ThreadBranches();

                    if (!_changed && !_options.RemoveUnreachableBlocks)
                        return new FlowRewriteResult(_method.GenTreeMethod, false, _nextTreeId);

                    var blocks = FreezeBlocks();
                    if (_options.RemoveUnreachableBlocks)
                    {
                        var compacted = RemoveUnreachableBlocks(blocks, out bool removed);
                        if (removed)
                        {
                            _changed = true;
                            blocks = compacted;
                        }
                    }

                    if (!_changed)
                        return new FlowRewriteResult(_method.GenTreeMethod, false, _nextTreeId);

                    return new FlowRewriteResult(_method.GenTreeMethod.CloneWithBlocks(blocks), true, _nextTreeId);
                }

                private void BuildBranchIndex()
                {
                    for (int i = 0; i < _method.Blocks.Length; i++)
                    {
                        if (TryBuildBranchInfo(_method.Blocks[i], out var branch))
                            _branches[branch.BlockId] = branch;
                    }
                }

                private void FoldBranches()
                {
                    var order = _method.Cfg.ReversePostOrder;
                    if (order.IsDefaultOrEmpty)
                    {
                        for (int blockId = 0; blockId < _method.Blocks.Length; blockId++)
                            TryFoldBranch(blockId);
                        return;
                    }

                    for (int i = 0; i < order.Length; i++)
                        TryFoldBranch(order[i]);
                }

                private void TryFoldBranch(int blockId)
                {
                    if (!_branches.TryGetValue(blockId, out var branch))
                        return;
                    if (!BranchIsStillConditional(branch))
                        return;

                    if (TryGetConstantBranchValue(branch, out bool constantValue))
                    {
                        ReplaceWithUnconditionalBranch(branch, branch.TargetFor(constantValue));
                        return;
                    }

                    if (branch.CompareNormalValueNumber.IsValid &&
                        TryInferBranchValueFromDominators(branch, out bool inferredValue))
                    {
                        ReplaceWithUnconditionalBranch(branch, branch.TargetFor(inferredValue));
                    }
                }

                private void ThreadBranches()
                {
                    var branches = new List<BranchInfo>(_branches.Values);
                    branches.Sort(static (left, right) => left.BlockId.CompareTo(right.BlockId));

                    for (int i = 0; i < branches.Count; i++)
                    {
                        var branch = branches[i];
                        if (!BranchIsStillConditional(branch))
                            continue;
                        if (!branch.CompareNormalValueNumber.IsValid)
                            continue;
                        if (!CanThreadThrough(branch))
                            continue;

                        var predecessors = _method.Cfg.Blocks[branch.BlockId].Predecessors;
                        for (int p = 0; p < predecessors.Length; p++)
                        {
                            var predEdge = predecessors[p];
                            if (predEdge.Kind == CfgEdgeKind.Exception)
                                continue;

                            int predId = predEdge.FromBlockId;
                            if (predId == branch.BlockId)
                                continue;

                            if (!TryInferBranchValueForIncomingEdge(branch, predId, out bool value))
                                continue;

                            int target = branch.TargetFor(value);
                            if (target == branch.BlockId)
                                continue;

                            RedirectNormalEdge(predId, branch.BlockId, target);
                        }
                    }
                }

                private bool TryBuildBranchInfo(SsaBlock ssaBlock, out BranchInfo branch)
                {
                    branch = default;

                    var block = ssaBlock.CfgBlock.SourceBlock;
                    if (block.Statements.IsDefaultOrEmpty)
                        return false;

                    if (!TryGetConditionalTransfer(
                            block.Statements,
                            out var terminator,
                            out _,
                            out _,
                            out _))
                    {
                        return false;
                    }

                    if (terminator.Operands.Length != 1)
                        return false;

                    var condition = terminator.Operands[0];
                    if (!IsPureCondition(condition))
                        return false;

                    if (!TryGetNormalBranchTargets(block, terminator, out int trueTarget, out int falseTarget))
                        return false;
                    if ((uint)trueTarget >= (uint)_method.Blocks.Length || (uint)falseTarget >= (uint)_method.Blocks.Length)
                        return false;

                    ValueNumber compareNormalVN = ValueNumberStore.NoVN;
                    _ = TryGetLiberalNormalRelopValueNumber(condition, out compareNormalVN);

                    branch = new BranchInfo(
                        ssaBlock.Id,
                        block,
                        ssaBlock,
                        terminator,
                        condition,
                        compareNormalVN,
                        trueTarget,
                        falseTarget);
                    return true;
                }

                private bool TryGetConditionalTransfer(
                    ImmutableArray<GenTree> statements,
                    out GenTree conditional,
                    out GenTree? appendedFallThrough,
                    out int conditionalIndex,
                    out int appendedIndex)
                {
                    conditional = null!;
                    appendedFallThrough = null;
                    conditionalIndex = -1;
                    appendedIndex = -1;

                    if (statements.IsDefaultOrEmpty)
                        return false;

                    var last = statements[statements.Length - 1];
                    if (last.Kind is GenTreeKind.BranchTrue or GenTreeKind.BranchFalse)
                    {
                        conditional = last;
                        conditionalIndex = statements.Length - 1;
                        return true;
                    }

                    if (last.Kind == GenTreeKind.Branch && statements.Length >= 2)
                    {
                        var previous = statements[statements.Length - 2];
                        if (previous.Kind is GenTreeKind.BranchTrue or GenTreeKind.BranchFalse)
                        {
                            conditional = previous;
                            appendedFallThrough = last;
                            conditionalIndex = statements.Length - 2;
                            appendedIndex = statements.Length - 1;
                            return true;
                        }
                    }

                    return false;
                }

                private bool TryGetConservativeNormalValueNumber(GenTree tree, out ValueNumber vn)
                {
                    vn = ValueNumberStore.NoVN;

                    if (_method.ValueNumbers is null)
                        return false;

                    if (!_method.ValueNumbers.TryGetTreeValue(tree, out var pair))
                        return false;

                    vn = NormalValueNumber(_method, pair.Conservative);
                    return vn.IsValid;
                }

                private bool TryGetLiberalNormalRelopValueNumber(GenTree tree, out ValueNumber vn)
                {
                    vn = ValueNumberStore.NoVN;

                    if (_method.ValueNumbers is null)
                        return false;

                    if (!_method.ValueNumbers.TryGetTreeValue(tree, out var pair))
                        return false;

                    vn = NormalValueNumber(_method, pair.Liberal);
                    return IsRelopValueNumber(vn);
                }

                private bool IsRelopValueNumber(ValueNumber vn)
                {
                    if (_method.ValueNumbers is null || !vn.IsValid)
                        return false;

                    if (!_method.ValueNumbers.Store.TryGetEntry(vn, out var entry))
                        return false;

                    return entry.Kind == ValueNumberKind.Function && IsComparisonFunction(entry.Function);
                }

                private static bool IsComparisonFunction(ValueNumberFunction function)
                    => function is ValueNumberFunction.Ceq
                        or ValueNumberFunction.Clt
                        or ValueNumberFunction.CltUn
                        or ValueNumberFunction.Cgt
                        or ValueNumberFunction.CgtUn;

                private bool TryGetNormalBranchTargets(GenTreeBlock block, GenTree terminator, out int trueTarget, out int falseTarget)
                {
                    trueTarget = -1;
                    falseTarget = -1;

                    int branchTarget = terminator.TargetBlockId;
                    if ((uint)branchTarget >= (uint)_method.Blocks.Length)
                        return false;

                    int otherTarget = -1;
                    var successors = GetSuccessors(block.Id);
                    for (int i = 0; i < successors.Length; i++)
                    {
                        int succ = successors[i];
                        if ((uint)succ >= (uint)_method.Blocks.Length)
                            return false;

                        if (succ == branchTarget)
                            continue;

                        if (otherTarget >= 0 && otherTarget != succ)
                            return false;

                        otherTarget = succ;
                    }

                    if (otherTarget < 0)
                    {
                        int fallThrough = block.Id + 1;
                        if ((uint)fallThrough < (uint)_method.Blocks.Length && fallThrough != branchTarget)
                            otherTarget = fallThrough;
                    }

                    if (otherTarget < 0)
                        return false;

                    if (terminator.Kind == GenTreeKind.BranchTrue)
                    {
                        trueTarget = branchTarget;
                        falseTarget = otherTarget;
                    }
                    else
                    {
                        trueTarget = otherTarget;
                        falseTarget = branchTarget;
                    }

                    return true;
                }

                private bool TryGetConstantBranchValue(BranchInfo branch, out bool value)
                {
                    value = false;

                    if (_method.ValueNumbers is not null &&
                        TryGetConservativeNormalValueNumber(branch.Condition, out var conditionVN) &&
                        _method.ValueNumbers.Store.TryGetConstant(conditionVN, out var key))
                    {
                        switch (key.Kind)
                        {
                            case ValueNumberConstantKind.Int32:
                                value = key.A != 0;
                                return true;
                            case ValueNumberConstantKind.Int64:
                                value = key.A != 0;
                                return true;
                            case ValueNumberConstantKind.Null:
                                value = false;
                                return true;
                        }
                    }

                    switch (branch.Condition.Kind)
                    {
                        case GenTreeKind.ConstI4:
                            value = branch.Condition.Int32 != 0;
                            return true;
                        case GenTreeKind.ConstI8:
                            value = branch.Condition.Int64 != 0;
                            return true;
                        case GenTreeKind.ConstNull:
                            value = false;
                            return true;
                        default:
                            return false;
                    }
                }

                private bool TryInferBranchValueFromDominators(BranchInfo branch, out bool value)
                {
                    value = false;

                    var idoms = _method.Cfg.ImmediateDominators;
                    if ((uint)branch.BlockId >= (uint)idoms.Length)
                        return false;

                    int dom = idoms[branch.BlockId];
                    int limit = _method.Blocks.Length;
                    while ((uint)dom < (uint)_method.Blocks.Length && limit-- > 0)
                    {
                        if (_branches.TryGetValue(dom, out var dominating) &&
                            SamePredicate(dominating, branch) &&
                            TryInferValueOnPathFromDominator(dominating, branch.BlockId, out value))
                        {
                            return true;
                        }

                        dom = idoms[dom];
                    }

                    return false;
                }

                private bool TryInferValueOnPathFromDominator(BranchInfo dominating, int blockId, out bool value)
                {
                    value = false;

                    bool onTruePath = Dominates(dominating.TrueTarget, blockId);
                    bool onFalsePath = Dominates(dominating.FalseTarget, blockId);

                    if (onTruePath == onFalsePath)
                        return false;

                    value = onTruePath;
                    return true;
                }

                private bool TryInferBranchValueForIncomingEdge(BranchInfo branch, int predId, out bool value)
                {
                    value = false;

                    if (TryInferDirectPredecessorValue(branch, predId, branch.BlockId, out value))
                        return true;

                    int child = branch.BlockId;
                    int probe = predId;
                    int limit = Math.Min(_method.Blocks.Length, 16);

                    while (limit-- > 0)
                    {
                        if (TryInferDirectPredecessorValue(branch, probe, child, out value))
                            return true;

                        if (!IsTransparentUnconditionalBlock(probe))
                            break;

                        int solePred = SoleNormalPredecessor(probe);
                        if (solePred < 0 || solePred == probe)
                            break;

                        child = probe;
                        probe = solePred;
                    }

                    return false;
                }

                private bool TryInferDirectPredecessorValue(BranchInfo branch, int predId, int edgeTarget, out bool value)
                {
                    value = false;

                    if (!_branches.TryGetValue(predId, out var predBranch))
                        return false;
                    if (!BranchIsStillConditional(predBranch))
                        return false;
                    if (!SamePredicate(predBranch, branch))
                        return false;

                    bool reachesViaTrue = predBranch.TrueTarget == edgeTarget;
                    bool reachesViaFalse = predBranch.FalseTarget == edgeTarget;

                    if (reachesViaTrue == reachesViaFalse)
                        return false;

                    value = reachesViaTrue;
                    return true;
                }

                private bool SamePredicate(BranchInfo left, BranchInfo right)
                    => left.CompareNormalValueNumber.IsValid &&
                       right.CompareNormalValueNumber.IsValid &&
                       left.CompareNormalValueNumber == right.CompareNormalValueNumber;

                private bool Dominates(int dominator, int blockId)
                {
                    if (dominator == blockId)
                        return true;
                    if ((uint)dominator >= (uint)_method.Blocks.Length || (uint)blockId >= (uint)_method.Blocks.Length)
                        return false;

                    var idoms = _method.Cfg.ImmediateDominators;
                    int cur = blockId;
                    int limit = _method.Blocks.Length;
                    while ((uint)cur < (uint)idoms.Length && limit-- > 0)
                    {
                        cur = idoms[cur];
                        if (cur == dominator)
                            return true;
                    }

                    return false;
                }

                private bool CanThreadThrough(BranchInfo branch)
                {
                    if (branch.SsaBlock.Phis.Length != 0 || branch.SsaBlock.MemoryPhis.Length != 0)
                        return false;

                    var statements = GetStatements(branch.BlockId);
                    if (!TryGetConditionalTransfer(statements, out var conditional, out var appendedFallThrough, out _, out _))
                        return false;

                    if (conditional.Id != branch.Terminator.Id)
                        return false;

                    return statements.Length == 1 || (statements.Length == 2 && appendedFallThrough is not null);
                }

                private bool IsTransparentUnconditionalBlock(int blockId)
                {
                    if ((uint)blockId >= (uint)_method.Blocks.Length)
                        return false;

                    var ssaBlock = _method.Blocks[blockId];
                    if (ssaBlock.Phis.Length != 0 || ssaBlock.MemoryPhis.Length != 0)
                        return false;

                    var statements = GetStatements(blockId);
                    if (statements.Length == 0)
                        return GetSuccessors(blockId).Length == 1;

                    if (statements.Length != 1)
                        return false;

                    var last = statements[0];
                    return last.Kind == GenTreeKind.Branch && GetSuccessors(blockId).Length == 1;
                }

                private int SoleNormalPredecessor(int blockId)
                {
                    if ((uint)blockId >= (uint)_method.Cfg.Blocks.Length)
                        return -1;

                    int result = -1;
                    var predecessors = _method.Cfg.Blocks[blockId].Predecessors;
                    for (int i = 0; i < predecessors.Length; i++)
                    {
                        var edge = predecessors[i];
                        if (edge.Kind == CfgEdgeKind.Exception)
                            continue;

                        if (result >= 0 && result != edge.FromBlockId)
                            return -1;

                        result = edge.FromBlockId;
                    }

                    return result;
                }

                private bool BranchIsStillConditional(BranchInfo branch)
                {
                    var statements = GetStatements(branch.BlockId);
                    return TryGetConditionalTransfer(statements, out var conditional, out _, out _, out _) &&
                           conditional.Id == branch.Terminator.Id;
                }

                private void ReplaceWithUnconditionalBranch(BranchInfo branch, int targetBlockId)
                {
                    if ((uint)targetBlockId >= (uint)_method.Blocks.Length)
                        return;

                    var statements = GetStatements(branch.BlockId);
                    if (!TryGetConditionalTransfer(statements, out var conditional, out _, out int conditionalIndex, out int appendedIndex))
                        return;

                    var builder = ImmutableArray.CreateBuilder<GenTree>(statements.Length);
                    for (int i = 0; i < statements.Length; i++)
                    {
                        if (i == conditionalIndex || i == appendedIndex)
                            continue;

                        builder.Add(statements[i]);
                    }
                    builder.Add(NewUnconditionalBranch(conditional.Pc, targetBlockId));

                    SetBlock(
                        branch.BlockId,
                        builder.ToImmutable(),
                        ImmutableArray.Create(targetBlockId),
                        ImmutableArray.Create(TargetPc(targetBlockId)),
                        GenTreeBlockJumpKind.Always);
                }

                private void RedirectNormalEdge(int fromBlockId, int oldTargetId, int newTargetId)
                {
                    if ((uint)fromBlockId >= (uint)_method.Blocks.Length ||
                        (uint)oldTargetId >= (uint)_method.Blocks.Length ||
                        (uint)newTargetId >= (uint)_method.Blocks.Length ||
                        oldTargetId == newTargetId)
                    {
                        return;
                    }

                    var successors = GetSuccessors(fromBlockId);
                    bool hasOld = false;
                    var succBuilder = ImmutableArray.CreateBuilder<int>(successors.Length);
                    for (int i = 0; i < successors.Length; i++)
                    {
                        int succ = successors[i];
                        if (succ == oldTargetId)
                        {
                            hasOld = true;
                            succ = newTargetId;
                        }

                        if (!Contains(succBuilder, succ))
                            succBuilder.Add(succ);
                    }

                    if (!hasOld)
                        return;

                    var statements = GetStatements(fromBlockId);
                    if (statements.Length == 0)
                    {
                        if (successors.Length != 1)
                            return;

                        SetBlock(
                            fromBlockId,
                            ImmutableArray.Create(NewUnconditionalBranch(_method.GenTreeMethod.Blocks[fromBlockId].StartPc, newTargetId)),
                            ImmutableArray.Create(newTargetId),
                            ImmutableArray.Create(TargetPc(newTargetId)),
                            GenTreeBlockJumpKind.Always);
                        return;
                    }

                    if (TryGetConditionalTransfer(
                            statements,
                            out var conditional,
                            out var appendedFallThrough,
                            out int conditionalIndex,
                            out int appendedIndex))
                    {
                        var rewrittenSuccs = succBuilder.ToImmutable();
                        if (rewrittenSuccs.Length == 1)
                        {
                            var collapsed = ImmutableArray.CreateBuilder<GenTree>(statements.Length);
                            for (int i = 0; i < statements.Length; i++)
                            {
                                if (i == conditionalIndex || i == appendedIndex)
                                    continue;

                                collapsed.Add(statements[i]);
                            }
                            collapsed.Add(NewUnconditionalBranch(conditional.Pc, rewrittenSuccs[0]));

                            SetBlock(
                                fromBlockId,
                                collapsed.ToImmutable(),
                                rewrittenSuccs,
                                SuccessorPcsFor(rewrittenSuccs),
                                GenTreeBlockJumpKind.Always);
                            return;
                        }

                        var builder = statements.ToBuilder();
                        if (conditional.TargetBlockId == oldTargetId)
                        {
                            builder[conditionalIndex] = CloneTreeWithTarget(conditional, newTargetId);
                        }
                        else if (appendedFallThrough is not null && appendedFallThrough.TargetBlockId == oldTargetId)
                        {
                            builder[appendedIndex] = CloneTreeWithTarget(appendedFallThrough, newTargetId);
                        }
                        else
                        {
                            builder.Add(NewUnconditionalBranch(conditional.Pc, newTargetId));
                        }

                        SetBlock(
                            fromBlockId,
                            builder.ToImmutable(),
                            rewrittenSuccs,
                            SuccessorPcsFor(rewrittenSuccs),
                            GenTreeBlockJumpKind.Conditional);
                        return;
                    }

                    var last = statements[statements.Length - 1];
                    if (last.Kind == GenTreeKind.Branch)
                    {
                        if (last.TargetBlockId != oldTargetId)
                            return;

                        var builder = statements.ToBuilder();
                        builder[builder.Count - 1] = CloneTreeWithTarget(last, newTargetId);

                        SetBlock(
                            fromBlockId,
                            builder.ToImmutable(),
                            ImmutableArray.Create(newTargetId),
                            ImmutableArray.Create(TargetPc(newTargetId)),
                            GenTreeBlockJumpKind.Always);
                    }
                }

                private void SetBlock(
                    int blockId,
                    ImmutableArray<GenTree> statements,
                    ImmutableArray<int> successors,
                    ImmutableArray<int> successorPcs,
                    GenTreeBlockJumpKind jumpKind)
                {
                    var original = _method.GenTreeMethod.Blocks[blockId];
                    _edits[blockId] = new MutableBlock(
                        original,
                        statements,
                        successors,
                        successorPcs,
                        jumpKind);
                    _changed = true;
                }

                private ImmutableArray<GenTreeBlock> FreezeBlocks()
                {
                    if (_edits.Count == 0)
                        return _method.GenTreeMethod.Blocks;

                    var builder = ImmutableArray.CreateBuilder<GenTreeBlock>(_method.GenTreeMethod.Blocks.Length);
                    for (int i = 0; i < _method.GenTreeMethod.Blocks.Length; i++)
                    {
                        if (!_edits.TryGetValue(i, out var edit))
                        {
                            builder.Add(_method.GenTreeMethod.Blocks[i]);
                            continue;
                        }

                        builder.Add(new GenTreeBlock(
                            edit.Original.Id,
                            edit.Original.StartPc,
                            edit.Original.EndPcExclusive,
                            edit.Original.EntryStackDepth,
                            edit.Original.ExitStackDepth,
                            edit.JumpKind,
                            edit.Original.Flags,
                            edit.Statements,
                            edit.Successors,
                            edit.SuccessorPcs));
                    }

                    return builder.ToImmutable();
                }

                private ImmutableArray<GenTreeBlock> RemoveUnreachableBlocks(ImmutableArray<GenTreeBlock> blocks, out bool removed)
                {
                    removed = false;

                    if (blocks.Length == 0)
                        return blocks;

                    bool hasEh = _method.GenTreeMethod.Function.ExceptionHandlers.Length != 0 ||
                                 _method.Cfg.ExceptionRegions.Length != 0;
                    var exceptionEdges = hasEh
                        ? EhFuncletLayout.BuildImplicitExceptionEdges(_method.Cfg)
                        : ImmutableArray<CfgEdge>.Empty;

                    var reachable = new bool[blocks.Length];
                    var stack = new Stack<int>();
                    MarkReachable(0, reachable, stack);

                    if (hasEh)
                    {
                        for (int i = 0; i < blocks.Length; i++)
                        {
                            if (IsProtectedEhBlock(i))
                                MarkReachable(i, reachable, stack);
                        }
                    }

                    while (stack.Count != 0)
                    {
                        int blockId = stack.Pop();
                        var successors = blocks[blockId].SuccessorBlockIds;
                        for (int i = 0; i < successors.Length; i++)
                            MarkReachable(successors[i], reachable, stack);

                        for (int i = 0; i < exceptionEdges.Length; i++)
                        {
                            var edge = exceptionEdges[i];
                            if (edge.FromBlockId == blockId)
                                MarkReachable(edge.ToBlockId, reachable, stack);
                        }
                    }

                    int reachableCount = 0;
                    for (int i = 0; i < reachable.Length; i++)
                    {
                        if (reachable[i])
                            reachableCount++;
                    }

                    if (reachableCount == blocks.Length)
                        return blocks;

                    removed = true;

                    var map = new int[blocks.Length];
                    for (int i = 0; i < map.Length; i++)
                        map[i] = -1;

                    int next = 0;
                    for (int i = 0; i < blocks.Length; i++)
                    {
                        if (reachable[i])
                            map[i] = next++;
                    }

                    var builder = ImmutableArray.CreateBuilder<GenTreeBlock>(reachableCount);
                    for (int oldId = 0; oldId < blocks.Length; oldId++)
                    {
                        if (!reachable[oldId])
                            continue;

                        var old = blocks[oldId];
                        int newId = map[oldId];
                        var successors = RemapSuccessors(old.SuccessorBlockIds, map);
                        var statements = RemapStatementTargets(old.Statements, map, blocks);
                        builder.Add(new GenTreeBlock(
                            newId,
                            old.StartPc,
                            old.EndPcExclusive,
                            old.EntryStackDepth,
                            old.ExitStackDepth,
                            old.JumpKind,
                            RemapBlockFlags(old.Flags, oldId, newId),
                            statements,
                            successors,
                            SuccessorPcsFor(successors, blocks, map)));
                    }

                    return builder.ToImmutable();
                }

                private static void MarkReachable(int blockId, bool[] reachable, Stack<int> stack)
                {
                    if ((uint)blockId >= (uint)reachable.Length || reachable[blockId])
                        return;

                    reachable[blockId] = true;
                    stack.Push(blockId);
                }

                private bool IsProtectedEhBlock(int blockId)
                {
                    if ((uint)blockId >= (uint)_method.Cfg.Blocks.Length)
                        return true;

                    var cfgBlock = _method.Cfg.Blocks[blockId];
                    return cfgBlock.IsInTryRegion || cfgBlock.IsInHandlerRegion || cfgBlock.IsHandlerEntry;
                }

                private ImmutableArray<int> RemapSuccessors(ImmutableArray<int> successors, int[] map)
                {
                    if (successors.IsDefaultOrEmpty)
                        return ImmutableArray<int>.Empty;

                    var builder = ImmutableArray.CreateBuilder<int>(successors.Length);
                    for (int i = 0; i < successors.Length; i++)
                    {
                        int succ = successors[i];
                        if ((uint)succ >= (uint)map.Length || map[succ] < 0)
                            continue;

                        int mapped = map[succ];
                        if (!Contains(builder, mapped))
                            builder.Add(mapped);
                    }

                    return builder.ToImmutable();
                }

                private ImmutableArray<GenTree> RemapStatementTargets(ImmutableArray<GenTree> statements, int[] map, ImmutableArray<GenTreeBlock> oldBlocks)
                {
                    if (statements.IsDefaultOrEmpty)
                        return ImmutableArray<GenTree>.Empty;

                    ImmutableArray<GenTree>.Builder? builder = null;
                    for (int i = 0; i < statements.Length; i++)
                    {
                        var rewritten = RemapTreeTarget(statements[i], map, oldBlocks);
                        if (!ReferenceEquals(rewritten, statements[i]) && builder is null)
                        {
                            builder = ImmutableArray.CreateBuilder<GenTree>(statements.Length);
                            for (int j = 0; j < i; j++)
                                builder.Add(statements[j]);
                        }

                        builder?.Add(rewritten);
                    }

                    return builder is null ? statements : builder.ToImmutable();
                }

                private GenTree RemapTreeTarget(GenTree tree, int[] map, ImmutableArray<GenTreeBlock> oldBlocks)
                {
                    ImmutableArray<GenTree>.Builder? operands = null;
                    for (int i = 0; i < tree.Operands.Length; i++)
                    {
                        var operand = RemapTreeTarget(tree.Operands[i], map, oldBlocks);
                        if (!ReferenceEquals(operand, tree.Operands[i]) && operands is null)
                        {
                            operands = ImmutableArray.CreateBuilder<GenTree>(tree.Operands.Length);
                            for (int j = 0; j < i; j++)
                                operands.Add(tree.Operands[j]);
                        }

                        operands?.Add(operand);
                    }

                    int targetBlock = tree.TargetBlockId;
                    int mappedTarget = targetBlock;
                    int mappedTargetPc = tree.TargetPc;
                    bool targetChanged = false;

                    if ((uint)targetBlock < (uint)map.Length)
                    {
                        mappedTarget = map[targetBlock];
                        if (mappedTarget < 0)
                            mappedTarget = targetBlock;
                        else if (mappedTarget != targetBlock)
                            targetChanged = true;

                        if (targetChanged && (uint)targetBlock < (uint)oldBlocks.Length)
                            mappedTargetPc = oldBlocks[targetBlock].StartPc;
                    }

                    if (operands is null && !targetChanged)
                        return tree;

                    return new GenTree(
                        tree.Id,
                        tree.Kind,
                        tree.Pc,
                        tree.SourceOp,
                        tree.Type,
                        tree.StackKind,
                        tree.Flags,
                        operands?.ToImmutable() ?? tree.Operands,
                        int32: tree.Int32,
                        int64: tree.Int64,
                        text: tree.Text,
                        runtimeType: tree.RuntimeType,
                        field: tree.Field,
                        method: tree.Method,
                        convKind: tree.ConvKind,
                        convFlags: tree.ConvFlags,
                        targetPc: mappedTargetPc,
                        targetBlockId: mappedTarget);
                }

                private static GenTreeBlockFlags RemapBlockFlags(GenTreeBlockFlags flags, int oldId, int newId)
                {
                    if (oldId == newId)
                        return flags;

                    if ((flags & GenTreeBlockFlags.Entry) != 0 && newId != 0)
                        return flags & ~GenTreeBlockFlags.Entry;

                    if (newId == 0)
                        return (flags | GenTreeBlockFlags.Entry);

                    return flags;
                }

                private GenTree NewUnconditionalBranch(int pc, int targetBlockId)
                    => new GenTree(
                        _nextTreeId++,
                        GenTreeKind.Branch,
                        pc,
                        BytecodeOp.Br,
                        null,
                        GenStackKind.Void,
                        GenTreeFlags.ControlFlow | GenTreeFlags.Ordered,
                        ImmutableArray<GenTree>.Empty,
                        targetPc: TargetPc(targetBlockId),
                        targetBlockId: targetBlockId);

                private GenTree CloneTreeWithTarget(GenTree tree, int targetBlockId)
                    => new GenTree(
                        tree.Id,
                        tree.Kind,
                        tree.Pc,
                        tree.SourceOp,
                        tree.Type,
                        tree.StackKind,
                        tree.Flags,
                        tree.Operands,
                        int32: tree.Int32,
                        int64: tree.Int64,
                        text: tree.Text,
                        runtimeType: tree.RuntimeType,
                        field: tree.Field,
                        method: tree.Method,
                        convKind: tree.ConvKind,
                        convFlags: tree.ConvFlags,
                        targetPc: TargetPc(targetBlockId),
                        targetBlockId: targetBlockId);

                private ImmutableArray<GenTree> GetStatements(int blockId)
                    => _edits.TryGetValue(blockId, out var edit)
                        ? edit.Statements
                        : _method.GenTreeMethod.Blocks[blockId].Statements;

                private ImmutableArray<int> GetSuccessors(int blockId)
                    => _edits.TryGetValue(blockId, out var edit)
                        ? edit.Successors
                        : _method.GenTreeMethod.Blocks[blockId].SuccessorBlockIds;

                private int TargetPc(int blockId)
                    => _method.GenTreeMethod.Blocks[blockId].StartPc;

                private ImmutableArray<int> SuccessorPcsFor(ImmutableArray<int> successors)
                    => SuccessorPcsFor(successors, _method.GenTreeMethod.Blocks, oldToNewMap: null);

                private ImmutableArray<int> SuccessorPcsFor(ImmutableArray<int> successors, ImmutableArray<GenTreeBlock> blocks, int[]? oldToNewMap)
                {
                    if (successors.IsDefaultOrEmpty)
                        return ImmutableArray<int>.Empty;

                    var builder = ImmutableArray.CreateBuilder<int>(successors.Length);
                    for (int i = 0; i < successors.Length; i++)
                    {
                        int succ = successors[i];
                        int oldSucc = succ;

                        if (oldToNewMap is not null)
                        {
                            oldSucc = -1;
                            for (int old = 0; old < oldToNewMap.Length; old++)
                            {
                                if (oldToNewMap[old] == succ)
                                {
                                    oldSucc = old;
                                    break;
                                }
                            }
                        }

                        if ((uint)oldSucc < (uint)blocks.Length)
                            builder.Add(blocks[oldSucc].StartPc);
                    }

                    return builder.ToImmutable();
                }

                private static bool Contains(ImmutableArray<int>.Builder builder, int value)
                {
                    for (int i = 0; i < builder.Count; i++)
                    {
                        if (builder[i] == value)
                            return true;
                    }

                    return false;
                }

                private static bool IsPureCondition(GenTree tree)
                {
                    if ((tree.Flags & (GenTreeFlags.SideEffect | GenTreeFlags.MemoryWrite | GenTreeFlags.CanThrow | GenTreeFlags.ExceptionFlow | GenTreeFlags.Ordered)) != 0)
                        return false;

                    for (int i = 0; i < tree.Operands.Length; i++)
                    {
                        if (!IsPureCondition(tree.Operands[i]))
                            return false;
                    }

                    return true;
                }

                private readonly struct BranchInfo
                {
                    public readonly int BlockId;
                    public readonly GenTreeBlock Block;
                    public readonly SsaBlock SsaBlock;
                    public readonly GenTree Terminator;
                    public readonly GenTree Condition;
                    public readonly ValueNumber CompareNormalValueNumber;
                    public readonly int TrueTarget;
                    public readonly int FalseTarget;

                    public BranchInfo(
                        int blockId,
                        GenTreeBlock block,
                        SsaBlock ssaBlock,
                        GenTree terminator,
                        GenTree condition,
                        ValueNumber compareNormalValueNumber,
                        int trueTarget,
                        int falseTarget)
                    {
                        BlockId = blockId;
                        Block = block;
                        SsaBlock = ssaBlock;
                        Terminator = terminator;
                        Condition = condition;
                        CompareNormalValueNumber = compareNormalValueNumber;
                        TrueTarget = trueTarget;
                        FalseTarget = falseTarget;
                    }

                    public int TargetFor(bool conditionValue) => conditionValue ? TrueTarget : FalseTarget;
                }

                private readonly struct MutableBlock
                {
                    public readonly GenTreeBlock Original;
                    public readonly ImmutableArray<GenTree> Statements;
                    public readonly ImmutableArray<int> Successors;
                    public readonly ImmutableArray<int> SuccessorPcs;
                    public readonly GenTreeBlockJumpKind JumpKind;

                    public MutableBlock(
                        GenTreeBlock original,
                        ImmutableArray<GenTree> statements,
                        ImmutableArray<int> successors,
                        ImmutableArray<int> successorPcs,
                        GenTreeBlockJumpKind jumpKind)
                    {
                        Original = original ?? throw new ArgumentNullException(nameof(original));
                        Statements = statements.IsDefault ? ImmutableArray<GenTree>.Empty : statements;
                        Successors = successors.IsDefault ? ImmutableArray<int>.Empty : successors;
                        SuccessorPcs = successorPcs.IsDefault ? ImmutableArray<int>.Empty : successorPcs;
                        JumpKind = jumpKind;
                    }
                }
            }

            private readonly struct FlowRewriteResult
            {
                public readonly GenTreeMethod Method;
                public readonly bool Changed;
                public readonly int NextSyntheticTreeId;

                public FlowRewriteResult(GenTreeMethod method, bool changed, int nextSyntheticTreeId)
                {
                    Method = method ?? throw new ArgumentNullException(nameof(method));
                    Changed = changed;
                    NextSyntheticTreeId = nextSyntheticTreeId;
                }
            }

            private readonly struct CopyPropSsaDef
            {
                public readonly SsaValueName Name;
                public readonly SsaDescriptor Descriptor;
                public readonly SsaTree? DefTree;
                public readonly ValueNumber ConservativeValueNumber;

                public CopyPropSsaDef(SsaValueName name, SsaDescriptor descriptor, SsaTree? defTree, ValueNumber conservativeValueNumber)
                {
                    Name = name;
                    Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
                    DefTree = defTree;
                    ConservativeValueNumber = conservativeValueNumber;
                }

                public bool HasValueNumber => ConservativeValueNumber.IsValid;
            }

            private sealed class CopyPropState
            {
                private readonly SsaMethod _method;
                private readonly Dictionary<SsaSlot, Stack<CopyPropSsaDef>> _defsBySlot = new();
                private readonly Dictionary<ValueNumber, HashSet<SsaSlot>> _slotsByValueNumber = new();
                private readonly HashSet<SsaValueName> _pushedInitialDefinitions = new();

                public CopyPropState(SsaMethod method)
                {
                    _method = method ?? throw new ArgumentNullException(nameof(method));
                }

                public bool HasCurrentDefinition(SsaSlot slot)
                    => _defsBySlot.TryGetValue(slot, out var stack) && stack.Count != 0;

                public bool TryPeek(SsaSlot slot, out CopyPropSsaDef definition)
                {
                    if (_defsBySlot.TryGetValue(slot, out var stack) && stack.Count != 0)
                    {
                        definition = stack.Peek();
                        return true;
                    }

                    definition = default;
                    return false;
                }

                public ImmutableArray<SsaSlot> GetCandidateSlots(ValueNumber conservativeValueNumber)
                {
                    conservativeValueNumber = NormalValueNumber(_method, conservativeValueNumber);
                    if (!conservativeValueNumber.IsValid)
                        return ImmutableArray<SsaSlot>.Empty;

                    if (!_slotsByValueNumber.TryGetValue(conservativeValueNumber, out var set) || set.Count == 0)
                        return ImmutableArray<SsaSlot>.Empty;

                    var slots = new List<SsaSlot>(set);
                    slots.Sort();
                    return slots.ToImmutableArray();
                }

                public bool TryPushInitialDefinition(SsaValueName name)
                {
                    if (!_pushedInitialDefinitions.Add(name))
                        return false;

                    if (!_method.TryGetSsaDescriptor(name, out var descriptor) || !descriptor.IsInitial)
                        return false;

                    PushCore(name, descriptor, defTree: null);
                    return true;
                }

                public void PushDefinition(SsaValueName name, SsaTree? defTree, List<SsaSlot> pushedInBlock)
                {
                    if (!_method.TryGetSsaDescriptor(name, out var descriptor))
                        return;

                    PushCore(name, descriptor, defTree);
                    pushedInBlock.Add(name.Slot);
                }

                public void PopDefinition(SsaSlot slot)
                {
                    if (!_defsBySlot.TryGetValue(slot, out var stack) || stack.Count == 0)
                        throw new InvalidOperationException($"Copy-prop SSA stack underflow for {slot}.");

                    var removed = stack.Pop();
                    if (stack.Count == 0)
                        _defsBySlot.Remove(slot);

                    _ = removed;
                }

                private void PushCore(SsaValueName name, SsaDescriptor descriptor, SsaTree? defTree)
                {
                    ValueNumber conservativeValueNumber = NormalValueNumber(_method, descriptor.ValueNumbers.Conservative);
                    var definition = new CopyPropSsaDef(name, descriptor, defTree, conservativeValueNumber);

                    if (!_defsBySlot.TryGetValue(name.Slot, out var stack))
                    {
                        stack = new Stack<CopyPropSsaDef>();
                        _defsBySlot.Add(name.Slot, stack);
                    }

                    stack.Push(definition);

                    if (conservativeValueNumber.IsValid)
                    {
                        if (!_slotsByValueNumber.TryGetValue(conservativeValueNumber, out var slots))
                        {
                            slots = new HashSet<SsaSlot>();
                            _slotsByValueNumber.Add(conservativeValueNumber, slots);
                        }
                        slots.Add(name.Slot);
                    }
                }
            }

            private sealed class SsaAvailability
            {
                private readonly SsaMethod _method;
                private readonly Dictionary<SsaValueName, SsaValueDefinition> _definitions = new();
                private readonly Dictionary<(int blockId, int statementIndex, int treeId), int> _treeOrder = new();

                public SsaAvailability(SsaMethod method)
                {
                    _method = method ?? throw new ArgumentNullException(nameof(method));
                    for (int i = 0; i < method.ValueDefinitions.Length; i++)
                        _definitions[method.ValueDefinitions[i].Name] = method.ValueDefinitions[i];
                    BuildTreeOrder();
                }

                public bool IsAvailableAt(SsaValueName name, int useBlockId, int useStatementIndex, int useTreeId)
                {
                    if (!_definitions.TryGetValue(name, out var definition))
                        return false;

                    if (definition.IsInitial)
                        return true;

                    if (definition.DefBlockId < 0)
                        return false;

                    if (definition.DefBlockId == useBlockId)
                    {
                        if (definition.IsPhi)
                            return true;

                        if (definition.DefStatementIndex < 0)
                            return false;

                        if (definition.DefStatementIndex < useStatementIndex)
                            return true;

                        if (definition.DefStatementIndex > useStatementIndex)
                            return false;

                        int defOrder = GetTreeOrder(definition.DefBlockId, definition.DefStatementIndex, definition.DefTreeId);
                        int useOrder = GetTreeOrder(useBlockId, useStatementIndex, useTreeId);
                        return defOrder >= 0 && useOrder >= 0 && defOrder < useOrder;
                    }

                    return Dominates(definition.DefBlockId, useBlockId);
                }

                private void BuildTreeOrder()
                {
                    for (int b = 0; b < _method.Blocks.Length; b++)
                    {
                        var block = _method.Blocks[b];
                        for (int i = 0; i < block.TreeList.Length; i++)
                        {
                            var item = block.TreeList[i];
                            _treeOrder[(block.Id, item.StatementIndex, item.Tree.Source.Id)] = item.TreeIndex;
                        }
                    }
                }

                private int GetTreeOrder(int blockId, int statementIndex, int treeId)
                    => _treeOrder.TryGetValue((blockId, statementIndex, treeId), out int order) ? order : -1;

                private bool Dominates(int definitionBlockId, int useBlockId)
                {
                    if (definitionBlockId == useBlockId)
                        return true;

                    if ((uint)definitionBlockId >= (uint)_method.Cfg.Blocks.Length || (uint)useBlockId >= (uint)_method.Cfg.Blocks.Length)
                        return false;

                    int current = useBlockId;
                    while (current >= 0)
                    {
                        current = _method.Cfg.ImmediateDominators[current];
                        if (current == definitionBlockId)
                            return true;
                    }

                    return false;
                }
            }

            private OptimizationResult CopyPropagate(SsaMethod method, SsaLocalLiveness pointLiveness)
            {
                if (method.ValueNumbers is null)
                    return new OptimizationResult(method.Blocks, changed: false);

                var availability = new SsaAvailability(method);
                var blocks = new SsaBlock[method.Blocks.Length];
                for (int i = 0; i < method.Blocks.Length; i++)
                    blocks[method.Blocks[i].Id] = method.Blocks[i];

                var state = new CopyPropState(method);

                var visited = new bool[method.Blocks.Length];
                bool changed = false;

                var roots = method.Cfg.DominatorRoots.IsDefaultOrEmpty
                    ? ImmutableArray.Create(method.Blocks.Length == 0 ? -1 : method.Blocks[0].Id)
                    : method.Cfg.DominatorRoots;

                for (int i = 0; i < roots.Length; i++)
                {
                    int root = roots[i];
                    if ((uint)root < (uint)blocks.Length)
                        VisitBlock(root);
                }

                for (int i = 0; i < blocks.Length; i++)
                {
                    if (blocks[i] is null)
                        blocks[i] = method.Blocks[i];
                }

                return new OptimizationResult(ImmutableArray.Create(blocks), changed);

                void VisitBlock(int blockId)
                {
                    if ((uint)blockId >= (uint)blocks.Length || visited[blockId])
                        return;

                    visited[blockId] = true;
                    var originalBlock = blocks[blockId];
                    var pushedInBlock = new List<SsaSlot>();

                    for (int p = 0; p < originalBlock.Phis.Length; p++)
                        state.PushDefinition(originalBlock.Phis[p].Target, defTree: null, pushedInBlock);

                    bool blockChanged = false;
                    var statements = ImmutableArray.CreateBuilder<SsaTree>(originalBlock.Statements.Length);
                    var statementTreeLists = ImmutableArray.CreateBuilder<ImmutableArray<SsaTree>>(originalBlock.Statements.Length);
                    bool skipCopyProp = BlockIsFinallyOrFaultHandler(method, originalBlock);

                    for (int s = 0; s < originalBlock.Statements.Length; s++)
                    {
                        var rewritten = RewriteCopyPropStatement(
                            method,
                            originalBlock.Statements[s],
                            originalBlock.StatementTreeLists[s],
                            availability,
                            pointLiveness,
                            state,
                            pushedInBlock,
                            blockId,
                            s,
                            skipCopyProp,
                            ref changed);

                        if (!ReferenceEquals(rewritten.Root, originalBlock.Statements[s]))
                            blockChanged = true;

                        statements.Add(rewritten.Root);
                        statementTreeLists.Add(rewritten.TreeList);
                    }

                    if (blockChanged)
                        blocks[blockId] = new SsaBlock(
                            originalBlock.CfgBlock,
                            originalBlock.Phis,
                            statements.ToImmutable(),
                            originalBlock.MemoryPhis,
                            originalBlock.MemoryIn,
                            originalBlock.MemoryOut,
                            statementTreeLists: statementTreeLists.ToImmutable());

                    var children = method.Cfg.DominatorTreeChildren[blockId];
                    for (int i = 0; i < children.Length; i++)
                        VisitBlock(children[i]);

                    for (int i = pushedInBlock.Count - 1; i >= 0; i--)
                        state.PopDefinition(pushedInBlock[i]);
                }
            }


            private (SsaTree Root, ImmutableArray<SsaTree> TreeList) RewriteCopyPropStatement(
                SsaMethod method,
                SsaTree root,
                ImmutableArray<SsaTree> treeList,
                SsaAvailability availability,
                SsaLocalLiveness pointLiveness,
                CopyPropState state,
                List<SsaSlot> pushedInBlock,
                int blockId,
                int statementIndex,
                bool skipCopyProp,
                ref bool changed)
            {
                if (treeList.IsDefaultOrEmpty)
                    return (root, treeList);

                var rewrittenByTree = new Dictionary<SsaTree, SsaTree>(ReferenceEqualityComparer<SsaTree>.Instance);
                var rewrittenTreeList = ImmutableArray.CreateBuilder<SsaTree>(treeList.Length);
                bool statementChanged = false;

                for (int i = 0; i < treeList.Length; i++)
                {
                    var tree = treeList[i];
                    var candidate = RebuildSsaTreeWithRewrittenOperands(tree, rewrittenByTree, ref statementChanged);

                    SsaTree rewritten = candidate;
                    if (candidate.StoreTarget.HasValue)
                    {
                        state.PushDefinition(candidate.StoreTarget.Value, candidate, pushedInBlock);
                    }
                    else if (!skipCopyProp && candidate.Value.HasValue && IsPureLocalSsaUse(candidate))
                    {
                        state.TryPushInitialDefinition(candidate.Value.Value);
                        if (TryCopyPropagateUse(method, candidate, availability, pointLiveness, state, blockId, statementIndex, out var replacement))
                        {
                            rewritten = replacement;
                            statementChanged = true;
                            changed = true;
                        }
                    }

                    rewrittenByTree[tree] = rewritten;
                    rewrittenTreeList.Add(rewritten);
                }

                if (statementChanged)
                    changed = true;

                var rewrittenRoot = rewrittenByTree.TryGetValue(root, out var foundRoot) ? foundRoot : root;
                var resultTreeList = rewrittenTreeList.ToImmutable();
                if (!ReferenceEquals(resultTreeList[resultTreeList.Length - 1], rewrittenRoot))
                    throw new InvalidOperationException($"Copy propagation changed SSA statement tree-list root ordering for node {root.Source.Id}.");

                return (rewrittenRoot, resultTreeList);
            }

            private static SsaTree RebuildSsaTreeWithRewrittenOperands(
                SsaTree tree,
                Dictionary<SsaTree, SsaTree> rewrittenByTree,
                ref bool changed)
            {
                if (tree.Operands.Length == 0)
                    return tree;

                ImmutableArray<SsaTree>.Builder? operands = null;
                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    if (!rewrittenByTree.TryGetValue(tree.Operands[i], out var rewrittenOperand))
                        throw new InvalidOperationException($"SSA tree list is not in execution order: operand appears after parent at node {tree.Source.Id}.");

                    if (!ReferenceEquals(rewrittenOperand, tree.Operands[i]) && operands is null)
                    {
                        operands = ImmutableArray.CreateBuilder<SsaTree>(tree.Operands.Length);
                        for (int j = 0; j < i; j++)
                            operands.Add(tree.Operands[j]);
                    }

                    operands?.Add(rewrittenOperand);
                }

                if (operands is null)
                    return tree;

                changed = true;
                return new SsaTree(
                    tree.Source,
                    operands.ToImmutable(),
                    tree.Value,
                    tree.StoreTarget,
                    tree.LocalFieldBaseValue,
                    tree.LocalField,
                    tree.MemoryUses,
                    tree.MemoryDefinitions);
            }

            private static ValueNumber NormalValueNumber(SsaMethod method, ValueNumber value)
            {
                if (!value.IsValid || method.ValueNumbers is null)
                    return value;
                return method.ValueNumbers.Store.VNNormalValue(value);
            }

            private bool TryCopyPropagateUse(
                SsaMethod method,
                SsaTree useSite,
                SsaAvailability availability,
                SsaLocalLiveness pointLiveness,
                CopyPropState state,
                int blockId,
                int statementIndex,
                out SsaTree replacement)
            {
                replacement = null!;
                if (!useSite.Value.HasValue)
                    return false;

                var useName = useSite.Value.Value;
                if (!method.TryGetSsaDescriptor(useName, out var useDescriptor))
                    return false;

                var useVN = NormalValueNumber(method, useDescriptor.ValueNumbers.Conservative);
                if (!useVN.IsValid)
                    return false;

                var candidateSlots = state.GetCandidateSlots(useVN);
                for (int i = 0; i < candidateSlots.Length; i++)
                {
                    var candidateSlot = candidateSlots[i];
                    if (candidateSlot.Equals(useName.Slot))
                        continue;

                    if (!state.TryPeek(candidateSlot, out var candidate))
                        continue;

                    if (!candidate.HasValueNumber || candidate.ConservativeValueNumber != useVN)
                        continue;

                    if (!availability.IsAvailableAt(candidate.Name, blockId, statementIndex, useSite.Source.Id))
                        continue;

                    if (!pointLiveness.IsLiveBeforeTree(blockId, statementIndex, useSite.Source.Id, candidateSlot))
                        continue;

                    if (!CanSubstituteLocal(method, useDescriptor, candidate.Descriptor, useSite.Source))
                        continue;

                    replacement = CreateSsaLocalUse(useSite.Source, candidate.Name, candidate.Descriptor);
                    return true;
                }

                return false;
            }

            private bool CanSubstituteLocal(SsaMethod method, SsaDescriptor useDescriptor, SsaDescriptor candidateDescriptor, GenTree useSource)
            {
                if (useDescriptor.BaseLocal.Equals(candidateDescriptor.BaseLocal))
                    return false;

                if (!SameStorageShape(useDescriptor.Type, useDescriptor.StackKind, candidateDescriptor.Type, candidateDescriptor.StackKind))
                    return false;

                if (_slotInfos.TryGetValue(useDescriptor.BaseLocal, out var useInfo) &&
                    _slotInfos.TryGetValue(candidateDescriptor.BaseLocal, out var candidateInfo))
                {
                    if (IsPromotedStructFieldSlot(useInfo) || IsPromotedStructFieldSlot(candidateInfo))
                        return false;

                    if (useInfo.LocalDescriptor is not null && candidateInfo.LocalDescriptor is not null &&
                        useInfo.LocalDescriptor.DoNotEnregister != candidateInfo.LocalDescriptor.DoNotEnregister)
                    {
                        return false;
                    }

                    if (CopyPropLocalScore(useInfo, candidateInfo, preferCandidate: true) <= 0)
                        return false;
                }

                return true;
            }

            private static bool IsPromotedStructFieldSlot(SsaSlotInfo info)
                => info.LocalDescriptor is { Category: GenLocalCategory.PromotedStructField };

            private static int CopyPropLocalScore(SsaSlotInfo useInfo, SsaSlotInfo candidateInfo, bool preferCandidate)
            {
                int score = preferCandidate ? 1 : -1;

                if (useInfo.LocalDescriptor is { IsCompilerTemp: true })
                    score += 2;
                if (candidateInfo.LocalDescriptor is { IsCompilerTemp: true })
                    score -= 2;

                if (useInfo.LocalDescriptor is { DoNotEnregister: true })
                    score += 1;
                if (candidateInfo.LocalDescriptor is { DoNotEnregister: true })
                    score -= 1;

                return score;
            }

            private SsaTree CreateSsaLocalUse(GenTree template, SsaValueName value, SsaDescriptor descriptor)
            {
                var kind = value.Slot.Kind switch
                {
                    SsaSlotKind.Arg => GenTreeKind.Arg,
                    SsaSlotKind.Local => GenTreeKind.Local,
                    SsaSlotKind.Temp => GenTreeKind.Temp,
                    _ => template.Kind,
                };

                var flags = (template.Flags &
                    ~(GenTreeFlags.MemoryRead | GenTreeFlags.MemoryWrite | GenTreeFlags.SideEffect
                    | GenTreeFlags.CanThrow | GenTreeFlags.ContainsCall | GenTreeFlags.ControlFlow
                    | GenTreeFlags.ExceptionFlow | GenTreeFlags.AddressExposed | GenTreeFlags.VarDef
                    | GenTreeFlags.VarUseAsg | GenTreeFlags.VarDeath)) | GenTreeFlags.LocalUse;
                var source = new GenTree(
                    _nextSyntheticTreeId++,
                    kind,
                    template.Pc,
                    template.SourceOp,
                    descriptor.Type,
                    descriptor.StackKind,
                    flags,
                    ImmutableArray<GenTree>.Empty,
                    int32: value.Slot.Index);

                source.AttachSsaUse(value);
                if (_slotInfos.TryGetValue(value.Slot, out var info))
                    source.LocalDescriptor = info.LocalDescriptor;

                return new SsaTree(source, ImmutableArray<SsaTree>.Empty, value: value);
            }

            private static bool IsPureLocalSsaUse(SsaTree tree)
            {
                if (!tree.Value.HasValue || tree.StoreTarget.HasValue || tree.LocalFieldBaseValue.HasValue || tree.HasMemoryEffects)
                    return false;

                if (tree.Operands.Length != 0)
                    return false;

                return tree.Source.Kind is GenTreeKind.Arg or GenTreeKind.Local or GenTreeKind.Temp or GenTreeKind.Field;
            }

            private static bool SameStorageShape(RuntimeType? leftType, GenStackKind leftStackKind, RuntimeType? rightType, GenStackKind rightStackKind)
            {
                if (leftStackKind != rightStackKind)
                    return false;

                if (ReferenceEquals(leftType, rightType))
                    return true;

                if (leftType is null || rightType is null)
                    return leftType is null && rightType is null;

                return leftType.TypeId == rightType.TypeId;
            }

            private static bool BlockIsFinallyOrFaultHandler(SsaMethod method, SsaBlock block)
            {
                var handlers = block.CfgBlock.HandlerRegionIndexes;
                for (int i = 0; i < handlers.Length; i++)
                {
                    int regionIndex = handlers[i];
                    if ((uint)regionIndex >= (uint)method.Cfg.ExceptionRegions.Length)
                        continue;

                    var region = method.Cfg.ExceptionRegions[regionIndex];
                    if (region.Kind is CfgExceptionRegionKind.Finally or CfgExceptionRegionKind.Fault)
                        return true;
                }

                return false;
            }

            private Dictionary<SsaValueName, ValueFact> ComputeFacts(SsaMethod method)
            {
                method = EnsureValueNumbers(method);

                var facts = new Dictionary<SsaValueName, ValueFact>();
                for (int i = 0; i < method.ValueDefinitions.Length; i++)
                    facts[method.ValueDefinitions[i].Name] = ValueFact.Unknown;

                if (method.ValueNumbers is null)
                    return facts;

                if (_options.PropagateConstants)
                {
                    for (int i = 0; i < method.ValueDefinitions.Length; i++)
                    {
                        var name = method.ValueDefinitions[i].Name;
                        if (TryGetSsaConstant(method, name, out var constant))
                            SetFact(facts, name, ValueFact.ForConstant(constant));
                    }
                }

                return facts;
            }

            private bool SetFact(Dictionary<SsaValueName, ValueFact> facts, SsaValueName name, ValueFact fact)
            {
                if (!facts.TryGetValue(name, out var current))
                {
                    facts.Add(name, fact);
                    return true;
                }

                if (current.Equals(fact))
                    return false;

                facts[name] = fact;
                return true;
            }

            private ValueFact EvaluateTree(SsaMethod method, SsaTree tree, Dictionary<SsaValueName, ValueFact> facts)
            {
                if (tree.Value.HasValue)
                    return NormalizeValue(tree.Value.Value, facts);

                if (TryGetSourceConstant(tree.Source, out var sourceConstant))
                    return ValueFact.ForConstant(sourceConstant);

                if (_options.FoldConstants && TryGetTreeConstant(method, tree, out var vnConstant))
                    return ValueFact.ForConstant(vnConstant);

                if (!_options.FoldConstants)
                    return ValueFact.Unknown;

                if (tree.Kind == GenTreeKind.Unary && tree.Operands.Length == 1)
                {
                    var operand = EvaluateTree(method, tree.Operands[0], facts);
                    if (operand.Kind == ValueFactKind.Constant && TryFoldUnary(tree.Source, operand.Constant, out var folded))
                        return ValueFact.ForConstant(folded);
                }

                if (tree.Kind == GenTreeKind.Binary && tree.Operands.Length == 2)
                {
                    var left = EvaluateTree(method, tree.Operands[0], facts);
                    var right = EvaluateTree(method, tree.Operands[1], facts);
                    if (left.Kind == ValueFactKind.Constant
                        && right.Kind == ValueFactKind.Constant && TryFoldBinary(tree.Source, left.Constant, right.Constant, out var folded))
                        return ValueFact.ForConstant(folded);
                }

                if (tree.Kind == GenTreeKind.Conv && tree.Operands.Length == 1)
                {
                    var operand = EvaluateTree(method, tree.Operands[0], facts);
                    if (operand.Kind == ValueFactKind.Constant && TryFoldConversion(tree.Source, operand.Constant, out var folded))
                        return ValueFact.ForConstant(folded);
                }

                return ValueFact.Unknown;
            }

            private ValueFact NormalizeValue(SsaValueName name, Dictionary<SsaValueName, ValueFact> facts)
            {
                return facts.TryGetValue(name, out var fact) && fact.Kind == ValueFactKind.Constant
                    ? fact
                    : ValueFact.Unknown;
            }

            private SsaMethod EnsureValueNumbers(SsaMethod method)
                => method.ValueNumbers is null ? SsaValueNumbering.BuildMethod(method, validate: _options.Validate) : method;

            private bool TryGetSsaConstant(SsaMethod method, SsaValueName name, out ConstValue constant)
            {
                constant = default;
                if (!TryGetSsaValueNumber(method, name, out var vn))
                    return false;
                return TryGetConstantFromValueNumber(method, vn, out constant);
            }

            private bool TryGetTreeConstant(SsaMethod method, SsaTree tree, out ConstValue constant)
            {
                constant = default;

                if (tree.Value.HasValue)
                    return TryGetSsaConstant(method, tree.Value.Value, out constant);

                if (tree.Source is null || method.ValueNumbers is null)
                    return false;

                if (tree.Source.CanThrow)
                    return false;

                if (!method.ValueNumbers.TryGetTreeValue(tree.Source, out var pair))
                    return false;

                return TryGetConstantFromValueNumber(method, NormalValueNumber(method, pair.Conservative), out constant);
            }

            private static bool TryGetSsaValueNumber(SsaMethod method, SsaValueName name, out ValueNumber vn)
            {
                vn = ValueNumberStore.NoVN;
                if (!method.TryGetSsaDescriptor(name, out var descriptor))
                    return false;

                vn = NormalValueNumber(method, descriptor.ValueNumbers.Conservative);
                return vn.IsValid;
            }

            private bool TryGetTreeValueNumber(SsaMethod method, SsaTree tree, out ValueNumber vn)
            {
                vn = ValueNumberStore.NoVN;

                if (tree.Value.HasValue)
                    return TryGetSsaValueNumber(method, tree.Value.Value, out vn);

                if (method.ValueNumbers is null || tree.Source is null)
                    return false;

                if (!method.ValueNumbers.TryGetTreeValue(tree.Source, out var pair))
                    return false;

                vn = NormalValueNumber(method, pair.Conservative);
                return vn.IsValid;
            }

            private bool TryGetConstantFromValueNumber(SsaMethod method, ValueNumber vn, out ConstValue constant)
            {
                constant = default;
                if (method.ValueNumbers is null || !vn.IsValid)
                    return false;

                vn = NormalValueNumber(method, vn);
                if (!vn.IsValid)
                    return false;

                if (!method.ValueNumbers.Store.TryGetConstant(vn, out var key))
                    return false;

                switch (key.Kind)
                {
                    case ValueNumberConstantKind.Int32:
                        constant = ConstValue.ForI4((int)key.A);
                        return true;
                    case ValueNumberConstantKind.Int64:
                        constant = ConstValue.ForI8(key.A);
                        return true;
                    case ValueNumberConstantKind.Null:
                        constant = ConstValue.Null;
                        return true;
                    default:
                        return false;
                }
            }

            private bool SameValueNumber(SsaMethod method, SsaTree left, SsaTree right)
            {
                return TryGetTreeValueNumber(method, left, out var leftVn) &&
                       TryGetTreeValueNumber(method, right, out var rightVn) &&
                       leftVn.IsValid &&
                       leftVn == rightVn;
            }

            private static bool IsGcOrManagedPointerKind(GenStackKind stackKind)
                => stackKind is GenStackKind.Ref or GenStackKind.ByRef or GenStackKind.Null;

            private static bool CanReplaceWithConstant(GenTree template, ConstValue constant)
            {
                if (template.CanThrow)
                    return false;
                if (constant.Kind == ConstKind.Null)
                {
                    return template.Kind == GenTreeKind.ConstNull;
                }

                return IsIntegerLike(template.StackKind);
            }

            private OptimizationResult Rewrite(SsaMethod method, Dictionary<SsaValueName, ValueFact> facts, SsaLocalLiveness pointLiveness)
            {
                bool changed = false;
                var blocks = ImmutableArray.CreateBuilder<SsaBlock>(method.Blocks.Length);

                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    var block = method.Blocks[b];
                    var phis = ImmutableArray.CreateBuilder<SsaPhi>(block.Phis.Length);
                    for (int p = 0; p < block.Phis.Length; p++)
                    {
                        var phi = block.Phis[p];
                        var newInputs = ImmutableArray.CreateBuilder<SsaPhiInput>(phi.Inputs.Length);
                        bool phiChanged = false;

                        for (int i = 0; i < phi.Inputs.Length; i++)
                        {
                            var input = phi.Inputs[i];
                            _ = NormalizeValue(input.Value, facts);
                            newInputs.Add(input);
                        }

                        var rewrittenPhi = phiChanged
                            ? new SsaPhi(phi.BlockId, phi.Slot, phi.Target, newInputs.ToImmutable())
                            : phi;

                        if (PhiIsTrivial(rewrittenPhi, facts))
                        {
                            changed = true;
                            continue;
                        }

                        if (phiChanged)
                            changed = true;

                        phis.Add(rewrittenPhi);
                    }

                    var statements = ImmutableArray.CreateBuilder<SsaTree>(block.Statements.Length);
                    var statementTreeLists = ImmutableArray.CreateBuilder<ImmutableArray<SsaTree>>(block.Statements.Length);
                    for (int s = 0; s < block.Statements.Length; s++)
                    {
                        var rewritten = RewriteStatement(method, block.Statements[s], block.StatementTreeLists[s], facts, pointLiveness, block.Id, s, ref changed);
                        statements.Add(rewritten.Root);
                        statementTreeLists.Add(rewritten.TreeList);
                    }

                    blocks.Add(new SsaBlock(
                        block.CfgBlock,
                        phis.ToImmutable(),
                        statements.ToImmutable(),
                        block.MemoryPhis,
                        block.MemoryIn,
                        block.MemoryOut,
                        statementTreeLists: statementTreeLists.ToImmutable()));
                }

                return new OptimizationResult(blocks.ToImmutable(), changed);
            }

            private bool PhiIsTrivial(SsaPhi phi, Dictionary<SsaValueName, ValueFact> facts)
            {
                if (!_options.PropagateConstants)
                    return false;

                var fact = NormalizeValue(phi.Target, facts);
                return fact.Kind == ValueFactKind.Constant;
            }

            private (SsaTree Root, ImmutableArray<SsaTree> TreeList) RewriteStatement(
                SsaMethod method,
                SsaTree root,
                ImmutableArray<SsaTree> treeList,
                Dictionary<SsaValueName, ValueFact> facts,
                SsaLocalLiveness pointLiveness,
                int blockId,
                int statementIndex,
                ref bool changed)
            {
                if (treeList.IsDefaultOrEmpty)
                {
                    var recursivelyRewritten = RewriteTreeRecursive(method, root, facts, pointLiveness, blockId, statementIndex, ref changed);
                    return (recursivelyRewritten, SsaTreeLinearOrder.BuildStatement(recursivelyRewritten));
                }

                var rewrittenByTree = new Dictionary<SsaTree, SsaTree>(ReferenceEqualityComparer<SsaTree>.Instance);
                var draftTreeList = ImmutableArray.CreateBuilder<SsaTree>(treeList.Length + 4);
                var appended = new HashSet<SsaTree>(ReferenceEqualityComparer<SsaTree>.Instance);
                bool statementChanged = false;

                for (int i = 0; i < treeList.Length; i++)
                {
                    var tree = treeList[i];
                    var candidate = RebuildSsaTreeWithRewrittenOperands(tree, rewrittenByTree, ref statementChanged);
                    var rewritten = RewriteTreeNode(method, candidate, facts, pointLiveness, blockId, statementIndex, ref statementChanged);
                    rewrittenByTree[tree] = rewritten;
                    AppendTreePreservingExistingOrder(rewritten, appended, draftTreeList);
                }

                if (statementChanged)
                    changed = true;

                var rewrittenRoot = rewrittenByTree.TryGetValue(root, out var foundRoot) ? foundRoot : root;
                var finalTreeList = ProjectReachableTreeList(rewrittenRoot, draftTreeList.ToImmutable());
                return (rewrittenRoot, finalTreeList);
            }

            private SsaTree RewriteTreeRecursive(
                SsaMethod method,
                SsaTree tree,
                Dictionary<SsaValueName, ValueFact> facts,
                SsaLocalLiveness pointLiveness,
                int blockId,
                int statementIndex,
                ref bool changed)
            {
                var operands = ImmutableArray.CreateBuilder<SsaTree>(tree.Operands.Length);
                bool operandChanged = false;
                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    var rewritten = RewriteTreeRecursive(method, tree.Operands[i], facts, pointLiveness, blockId, statementIndex, ref changed);
                    if (!ReferenceEquals(rewritten, tree.Operands[i]))
                        operandChanged = true;
                    operands.Add(rewritten);
                }

                var candidate = operandChanged
                    ? new SsaTree(
                        tree.Source,
                        operands.ToImmutable(),
                        tree.Value,
                        tree.StoreTarget,
                        tree.LocalFieldBaseValue,
                        tree.LocalField,
                        tree.MemoryUses,
                        tree.MemoryDefinitions)
                    : tree;

                return RewriteTreeNode(method, candidate, facts, pointLiveness, blockId, statementIndex, ref changed);
            }

            private SsaTree RewriteTreeNode(
                SsaMethod method,
                SsaTree candidate,
                Dictionary<SsaValueName, ValueFact> facts,
                SsaLocalLiveness pointLiveness,
                int blockId,
                int statementIndex,
                ref bool changed)
            {
                if (candidate.Value.HasValue)
                {
                    var fact = NormalizeValue(candidate.Value.Value, facts);
                    if (_options.PropagateConstants && fact.Kind == ValueFactKind.Constant && CanReplaceWithConstant(candidate.Source, fact.Constant))
                    {
                        changed = true;
                        return new SsaTree(CreateConstantTree(candidate.Source, fact.Constant), ImmutableArray<SsaTree>.Empty);
                    }

                    return candidate;
                }

                if (_options.SimplifyAlgebraicIdentities && TrySimplifyTree(method, candidate, facts, out var simplified))
                {
                    if (CanUseReplacementTreeAt(method, pointLiveness, candidate, simplified, blockId, statementIndex))
                    {
                        changed = true;
                        return simplified;
                    }
                }

                if (_options.FoldConstants && !candidate.StoreTarget.HasValue && ProducesValue(candidate.Source) && !HasObservableEffect(candidate))
                {
                    var fact = EvaluateTree(method, candidate, facts);
                    if (fact.Kind == ValueFactKind.Constant &&
                        !TryGetSourceConstant(candidate.Source, out _) &&
                        CanReplaceWithConstant(candidate.Source, fact.Constant))
                    {
                        changed = true;
                        return new SsaTree(CreateConstantTree(candidate.Source, fact.Constant), ImmutableArray<SsaTree>.Empty);
                    }
                }

                return candidate;
            }

            private static void AppendTreePreservingExistingOrder(
                SsaTree tree,
                HashSet<SsaTree> appended,
                ImmutableArray<SsaTree>.Builder builder)
            {
                for (int i = 0; i < tree.Operands.Length; i++)
                    AppendTreePreservingExistingOrder(tree.Operands[i], appended, builder);

                if (appended.Add(tree))
                    builder.Add(tree);
            }

            private static ImmutableArray<SsaTree> ProjectReachableTreeList(SsaTree root, ImmutableArray<SsaTree> candidateOrder)
            {
                var reachable = new HashSet<SsaTree>(ReferenceEqualityComparer<SsaTree>.Instance);
                MarkReachable(root, reachable);

                var builder = ImmutableArray.CreateBuilder<SsaTree>(reachable.Count);
                var appended = new HashSet<SsaTree>(ReferenceEqualityComparer<SsaTree>.Instance);

                for (int i = 0; i < candidateOrder.Length; i++)
                {
                    var tree = candidateOrder[i];
                    if (reachable.Contains(tree) && appended.Add(tree))
                        builder.Add(tree);
                }

                AppendMissingReachable(root, reachable, appended, builder);

                if (builder.Count == 0 || !ReferenceEquals(builder[builder.Count - 1], root))
                {
                    if (!appended.Contains(root))
                    {
                        appended.Add(root);
                        builder.Add(root);
                    }
                    else
                    {
                        throw new InvalidOperationException($"SSA rewritten statement tree-list root is not last for node {root.Source.Id}.");
                    }
                }

                return builder.ToImmutable();
            }

            private static void MarkReachable(SsaTree tree, HashSet<SsaTree> reachable)
            {
                if (!reachable.Add(tree))
                    return;

                for (int i = 0; i < tree.Operands.Length; i++)
                    MarkReachable(tree.Operands[i], reachable);
            }

            private static void AppendMissingReachable(
                SsaTree tree,
                HashSet<SsaTree> reachable,
                HashSet<SsaTree> appended,
                ImmutableArray<SsaTree>.Builder builder)
            {
                if (!reachable.Contains(tree) || appended.Contains(tree))
                    return;

                for (int i = 0; i < tree.Operands.Length; i++)
                    AppendMissingReachable(tree.Operands[i], reachable, appended, builder);

                if (appended.Add(tree))
                    builder.Add(tree);
            }

            private bool CanUseReplacementTreeAt(
                SsaMethod method,
                SsaLocalLiveness pointLiveness,
                SsaTree useSite,
                SsaTree replacement,
                int blockId,
                int statementIndex)
            {
                if (ReferenceEquals(useSite, replacement))
                    return true;

                var requiredSlots = new HashSet<SsaSlot>();
                CollectReplacementLocalUses(replacement, requiredSlots);
                if (requiredSlots.Count == 0)
                    return true;

                foreach (var slot in requiredSlots)
                {
                    if (!pointLiveness.IsLiveBeforeTree(blockId, statementIndex, useSite.Source.Id, slot))
                        return false;
                }

                return true;
            }

            private static void CollectReplacementLocalUses(SsaTree tree, HashSet<SsaSlot> slots)
            {
                if (tree.Value.HasValue)
                    slots.Add(tree.Value.Value.Slot);

                if (tree.LocalFieldBaseValue.HasValue)
                    slots.Add(tree.LocalFieldBaseValue.Value.Slot);

                for (int i = 0; i < tree.Operands.Length; i++)
                    CollectReplacementLocalUses(tree.Operands[i], slots);
            }

            private OptimizationResult EliminateDeadDefinitions(SsaMethod method)
            {
                var live = ComputeLiveValues(method);
                bool changed = false;
                var blocks = ImmutableArray.CreateBuilder<SsaBlock>(method.Blocks.Length);

                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    var block = method.Blocks[b];
                    var phis = ImmutableArray.CreateBuilder<SsaPhi>(block.Phis.Length);
                    for (int p = 0; p < block.Phis.Length; p++)
                    {
                        var phi = block.Phis[p];
                        if (live.Contains(phi.Target))
                            phis.Add(phi);
                        else
                            changed = true;
                    }

                    var statements = ImmutableArray.CreateBuilder<SsaTree>(block.Statements.Length);
                    var statementTreeLists = ImmutableArray.CreateBuilder<ImmutableArray<SsaTree>>(block.Statements.Length);
                    for (int s = 0; s < block.Statements.Length; s++)
                    {
                        var statement = block.Statements[s];
                        var oldTreeList = block.StatementTreeLists[s];
                        if (statement.StoreTarget.HasValue && !live.Contains(statement.StoreTarget.Value))
                        {
                            if (MustPreserveDeadStore(statement.StoreTarget.Value))
                            {
                                statements.Add(statement);
                                statementTreeLists.Add(oldTreeList);
                                continue;
                            }

                            var sideEffects = ExtractSideEffects(statement);
                            if (sideEffects is not null)
                            {
                                statements.Add(sideEffects);
                                statementTreeLists.Add(ProjectReachableTreeList(sideEffects, oldTreeList.Add(sideEffects)));
                            }
                            changed = true;
                            continue;
                        }

                        if (!statement.StoreTarget.HasValue && !HasObservableEffect(statement))
                        {
                            changed = true;
                            continue;
                        }

                        statements.Add(statement);
                        statementTreeLists.Add(oldTreeList);
                    }

                    blocks.Add(new SsaBlock(
                        block.CfgBlock,
                        phis.ToImmutable(),
                        statements.ToImmutable(),
                        block.MemoryPhis,
                        block.MemoryIn,
                        block.MemoryOut,
                        statementTreeLists: statementTreeLists.ToImmutable()));
                }

                return new OptimizationResult(blocks.ToImmutable(), changed);
            }

            private bool MustPreserveDeadStore(SsaValueName target)
            {
                if (!_slotInfos.TryGetValue(target.Slot, out var info))
                    return true;

                if (IsGcOrManagedPointerKind(info.StackKind))
                    return true;

                var abi = MachineAbi.ClassifyStorageValue(info.Type, info.StackKind);
                return abi.ContainsGcPointers;
            }

            private HashSet<SsaValueName> ComputeLiveValues(SsaMethod method)
            {
                var phiDefs = new Dictionary<SsaValueName, SsaPhi>();
                var storeDefs = new Dictionary<SsaValueName, SsaTree>();

                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    var block = method.Blocks[b];
                    for (int p = 0; p < block.Phis.Length; p++)
                        phiDefs[block.Phis[p].Target] = block.Phis[p];
                    for (int s = 0; s < block.Statements.Length; s++)
                        CollectStoreDefinitions(block.Statements[s], storeDefs);
                }

                var useDefByDef = new Dictionary<SsaValueName, SsaValueName>();
                for (int i = 0; i < method.ValueDefinitions.Length; i++)
                {
                    var descriptor = method.ValueDefinitions[i].Descriptor;
                    if (descriptor.HasUseDefSsaNum)
                        useDefByDef[descriptor.Name] = new SsaValueName(descriptor.BaseLocal, descriptor.UseDefSsaNumber);
                }

                var live = new HashSet<SsaValueName>();
                var work = new Queue<SsaValueName>();

                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    var statements = method.Blocks[b].Statements;
                    for (int s = 0; s < statements.Length; s++)
                    {
                        var statement = statements[s];
                        if (statement.StoreTarget.HasValue)
                        {
                            if (StoreRhsHasObservableEffect(statement))
                                MarkUses(statement, live, work, includeStoreTarget: false);
                        }
                        else if (HasObservableEffect(statement))
                        {
                            MarkUses(statement, live, work, includeStoreTarget: false);
                        }
                    }
                }

                while (work.Count != 0)
                {
                    var value = work.Dequeue();
                    if (storeDefs.TryGetValue(value, out var store))
                    {
                        if (useDefByDef.TryGetValue(value, out var useDef))
                            MarkValue(useDef, live, work);
                        MarkUses(store, live, work, includeStoreTarget: false);
                        continue;
                    }

                    if (phiDefs.TryGetValue(value, out var phi))
                    {
                        for (int i = 0; i < phi.Inputs.Length; i++)
                            MarkValue(phi.Inputs[i].Value, live, work);
                    }
                }

                return live;
            }

            private void CollectStoreDefinitions(SsaTree tree, Dictionary<SsaValueName, SsaTree> storeDefs)
            {
                for (int i = 0; i < tree.Operands.Length; i++)
                    CollectStoreDefinitions(tree.Operands[i], storeDefs);

                if (tree.StoreTarget.HasValue)
                    storeDefs[tree.StoreTarget.Value] = tree;
            }

            private void MarkUses(SsaTree tree, HashSet<SsaValueName> live, Queue<SsaValueName> work, bool includeStoreTarget)
            {
                if (tree.Value.HasValue)
                    MarkValue(tree.Value.Value, live, work);

                if (tree.LocalFieldBaseValue.HasValue)
                    MarkValue(tree.LocalFieldBaseValue.Value, live, work);

                if (includeStoreTarget && tree.StoreTarget.HasValue)
                    MarkValue(tree.StoreTarget.Value, live, work);

                for (int i = 0; i < tree.Operands.Length; i++)
                    MarkUses(tree.Operands[i], live, work, includeStoreTarget: true);
            }

            private static void MarkValue(SsaValueName value, HashSet<SsaValueName> live, Queue<SsaValueName> work)
            {
                if (live.Add(value))
                    work.Enqueue(value);
            }

            private SsaTree? ExtractSideEffects(SsaTree deadStore)
            {
                if (deadStore.Operands.Length == 0)
                    return null;

                if (deadStore.Operands.Length == 1)
                {
                    var operand = deadStore.Operands[0];
                    if (!HasObservableEffect(operand))
                        return null;

                    var evalSource = CreateEvalTree(deadStore.Source, ImmutableArray.Create(operand));
                    return new SsaTree(evalSource, ImmutableArray.Create(operand));
                }

                var sideEffects = ImmutableArray.CreateBuilder<SsaTree>(deadStore.Operands.Length);
                for (int i = 0; i < deadStore.Operands.Length; i++)
                {
                    if (HasObservableEffect(deadStore.Operands[i]))
                        sideEffects.Add(deadStore.Operands[i]);
                }

                if (sideEffects.Count == 0)
                    return null;

                var operands = sideEffects.ToImmutable();
                return new SsaTree(CreateEvalTree(deadStore.Source, operands), operands);
            }

            private bool StoreRhsHasObservableEffect(SsaTree tree)
            {
                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    if (HasObservableEffect(tree.Operands[i]))
                        return true;
                }
                return false;
            }

            private bool HasObservableEffect(SsaTree tree)
            {
                if (tree.HasMemoryEffects)
                    return true;

                if (tree.Value.HasValue)
                    return false;

                if (tree.StoreTarget.HasValue)
                    return StoreRhsHasObservableEffect(tree);

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

                var flags = tree.Source.Flags;
                if ((flags & (GenTreeFlags.ContainsCall |
                              GenTreeFlags.CanThrow |
                              GenTreeFlags.SideEffect |
                              GenTreeFlags.MemoryWrite |
                              GenTreeFlags.ControlFlow |
                              GenTreeFlags.ExceptionFlow |
                              GenTreeFlags.Ordered)) != 0)
                    return true;

                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    if (HasObservableEffect(tree.Operands[i]))
                        return true;
                }

                return false;
            }

            private bool TrySimplifyTree(SsaMethod method, SsaTree tree, Dictionary<SsaValueName, ValueFact> facts, out SsaTree simplified)
            {
                simplified = null!;

                if (tree.StoreTarget.HasValue || !ProducesValue(tree.Source))
                    return false;

                if (tree.Kind == GenTreeKind.Unary && tree.Operands.Length == 1)
                    return TrySimplifyUnary(method, tree, facts, out simplified);

                if (tree.Kind == GenTreeKind.Binary && tree.Operands.Length == 2)
                    return TrySimplifyBinary(method, tree, facts, out simplified);

                if (tree.Kind == GenTreeKind.Conv && tree.Operands.Length == 1)
                    return TrySimplifyConversion(method, tree, facts, out simplified);

                return false;
            }

            private bool TrySimplifyUnary(SsaMethod method, SsaTree tree, Dictionary<SsaValueName, ValueFact> facts, out SsaTree simplified)
            {
                simplified = null!;
                var operand = tree.Operands[0];
                var fact = EvaluateTree(method, operand, facts);

                bool integerLike = IsIntegerLike(tree.Source.StackKind);

                if (integerLike && tree.Source.SourceOp == BytecodeOp.Neg && IsZero(fact))
                {
                    simplified = CreateConstantSsaTree(tree.Source, ZeroFor(tree.Source));
                    return true;
                }

                if (integerLike && tree.Source.SourceOp == BytecodeOp.Not && IsAllBitsSet(fact))
                {
                    simplified = CreateConstantSsaTree(tree.Source, ZeroFor(tree.Source));
                    return true;
                }

                if (integerLike && operand.Kind == GenTreeKind.Unary && operand.Operands.Length == 1 &&
                    operand.Source.SourceOp == tree.Source.SourceOp &&
                    (tree.Source.SourceOp == BytecodeOp.Neg || tree.Source.SourceOp == BytecodeOp.Not))
                {
                    simplified = operand.Operands[0];
                    return true;
                }

                return false;
            }

            private bool TrySimplifyConversion(SsaMethod method, SsaTree tree, Dictionary<SsaValueName, ValueFact> facts, out SsaTree simplified)
            {
                simplified = null!;

                if ((tree.Source.ConvFlags & NumericConvFlags.Checked) != 0)
                    return false;

                var operand = tree.Operands[0];
                if (!operand.Value.HasValue)
                    return false;

                if (!_slotInfos.TryGetValue(operand.Value.Value.Slot, out var operandInfo))
                    return false;

                if (!IsSemanticallyNoOpConversion(tree.Source.ConvKind, tree.Source.ConvFlags, operandInfo.StackKind))
                    return false;

                var sourceAbi = MachineAbi.ClassifyStorageValue(operandInfo.Type, operandInfo.StackKind);
                var destinationAbi = MachineAbi.ClassifyStorageValue(tree.Source.Type, tree.Source.StackKind);
                if (sourceAbi.PassingKind == destinationAbi.PassingKind &&
                    sourceAbi.RegisterClass == destinationAbi.RegisterClass &&
                    sourceAbi.Size == destinationAbi.Size &&
                    sourceAbi.ContainsGcPointers == destinationAbi.ContainsGcPointers)
                {
                    simplified = operand;
                    return true;
                }

                return false;
            }

            private static bool IsSemanticallyNoOpConversion(NumericConvKind targetKind, NumericConvFlags flags, GenStackKind sourceStackKind)
            {
                if ((flags & NumericConvFlags.Checked) != 0)
                    return false;

                if (targetKind is NumericConvKind.Bool or
                    NumericConvKind.I1 or NumericConvKind.U1 or
                    NumericConvKind.I2 or NumericConvKind.U2 or NumericConvKind.Char)
                {
                    return false;
                }

                if (targetKind is NumericConvKind.I4 or NumericConvKind.U4)
                    return sourceStackKind == GenStackKind.I4;

                if (targetKind is NumericConvKind.I8 or NumericConvKind.U8)
                    return sourceStackKind == GenStackKind.I8;

                if (targetKind == NumericConvKind.R4)
                    return sourceStackKind == GenStackKind.R4;

                if (targetKind == NumericConvKind.R8)
                    return sourceStackKind == GenStackKind.R8;

                if (targetKind == NumericConvKind.NativeInt)
                    return sourceStackKind == GenStackKind.NativeInt ||
                           (TargetArchitecture.PointerSize == 4 && sourceStackKind == GenStackKind.I4);

                if (targetKind == NumericConvKind.NativeUInt)
                    return sourceStackKind == GenStackKind.NativeUInt ||
                           sourceStackKind == GenStackKind.Ptr ||
                           (TargetArchitecture.PointerSize == 4 && sourceStackKind == GenStackKind.I4);

                return false;
            }

            private bool TrySimplifyBinary(SsaMethod method, SsaTree tree, Dictionary<SsaValueName, ValueFact> facts, out SsaTree simplified)
            {
                simplified = null!;

                var left = tree.Operands[0];
                var right = tree.Operands[1];
                var leftFact = EvaluateTree(method, left, facts);
                var rightFact = EvaluateTree(method, right, facts);
                var op = tree.Source.SourceOp;

                bool integerResult = IsIntegerLike(tree.Source.StackKind);
                bool comparableOperands = CanFoldComparisonOperands(left.Source.StackKind, right.Source.StackKind);
                bool orderedOperands = IsIntegerLike(left.Source.StackKind) && IsIntegerLike(right.Source.StackKind);

                switch (op)
                {
                    case BytecodeOp.Add:
                        if (!integerResult) break;
                        if (IsZero(rightFact)) { simplified = left; return true; }
                        if (IsZero(leftFact)) { simplified = right; return true; }
                        break;

                    case BytecodeOp.Sub:
                        if (!integerResult) break;
                        if (IsZero(rightFact)) { simplified = left; return true; }
                        if (SameValue(method, left, right, facts)) { simplified = CreateConstantSsaTree(tree.Source, ZeroFor(tree.Source)); return true; }
                        break;

                    case BytecodeOp.Mul:
                        if (!integerResult) break;
                        if (IsOne(rightFact)) { simplified = left; return true; }
                        if (IsOne(leftFact)) { simplified = right; return true; }
                        if (IsZero(rightFact) && !HasObservableEffect(left))
                        { simplified = CreateConstantSsaTree(tree.Source, ZeroFor(tree.Source)); return true; }
                        if (IsZero(leftFact) && !HasObservableEffect(right))
                        { simplified = CreateConstantSsaTree(tree.Source, ZeroFor(tree.Source)); return true; }
                        break;

                    case BytecodeOp.Div:
                        if (!integerResult) break;
                        if (IsOne(rightFact)) { simplified = left; return true; }
                        break;

                    case BytecodeOp.Div_Un:
                        if (!integerResult) break;
                        if (IsOne(rightFact)) { simplified = left; return true; }
                        break;

                    case BytecodeOp.Rem:
                        if (!integerResult) break;
                        if (IsOne(rightFact) && !HasObservableEffect(left))
                        { simplified = CreateConstantSsaTree(tree.Source, ZeroFor(tree.Source)); return true; }
                        break;

                    case BytecodeOp.Rem_Un:
                        if (!integerResult) break;
                        if (IsOne(rightFact) && !HasObservableEffect(left))
                        { simplified = CreateConstantSsaTree(tree.Source, ZeroFor(tree.Source)); return true; }
                        break;

                    case BytecodeOp.And:
                        if (!integerResult) break;
                        if (IsZero(rightFact) && !HasObservableEffect(left))
                        { simplified = CreateConstantSsaTree(tree.Source, ZeroFor(tree.Source)); return true; }
                        if (IsZero(leftFact) && !HasObservableEffect(right))
                        { simplified = CreateConstantSsaTree(tree.Source, ZeroFor(tree.Source)); return true; }
                        if (IsAllBitsSet(rightFact)) { simplified = left; return true; }
                        if (IsAllBitsSet(leftFact)) { simplified = right; return true; }
                        if (SameValue(method, left, right, facts)) { simplified = left; return true; }
                        break;

                    case BytecodeOp.Or:
                        if (!integerResult) break;
                        if (IsZero(rightFact)) { simplified = left; return true; }
                        if (IsZero(leftFact)) { simplified = right; return true; }
                        if (IsAllBitsSet(rightFact) && !HasObservableEffect(left))
                        { simplified = CreateConstantSsaTree(tree.Source, AllBitsSetFor(tree.Source)); return true; }
                        if (IsAllBitsSet(leftFact) && !HasObservableEffect(right))
                        { simplified = CreateConstantSsaTree(tree.Source, AllBitsSetFor(tree.Source)); return true; }
                        if (SameValue(method, left, right, facts)) { simplified = left; return true; }
                        break;

                    case BytecodeOp.Xor:
                        if (!integerResult) break;
                        if (IsZero(rightFact)) { simplified = left; return true; }
                        if (IsZero(leftFact)) { simplified = right; return true; }
                        if (SameValue(method, left, right, facts))
                        { simplified = CreateConstantSsaTree(tree.Source, ZeroFor(tree.Source)); return true; }
                        break;

                    case BytecodeOp.Shl:
                    case BytecodeOp.Shr:
                    case BytecodeOp.Shr_Un:
                        if (!integerResult) break;
                        if (IsEffectiveZeroShift(rightFact, tree.Source.StackKind)) { simplified = left; return true; }
                        break;

                    case BytecodeOp.Ceq:
                        if (comparableOperands && SameValue(method, left, right, facts))
                        { simplified = CreateConstantSsaTree(tree.Source, ConstValue.ForI4(1)); return true; }
                        break;

                    case BytecodeOp.Clt:
                    case BytecodeOp.Clt_Un:
                    case BytecodeOp.Cgt:
                    case BytecodeOp.Cgt_Un:
                        if (orderedOperands && SameValue(method, left, right, facts))
                        { simplified = CreateConstantSsaTree(tree.Source, ConstValue.ForI4(0)); return true; }
                        break;
                }

                return false;
            }

            private SsaTree CreateConstantSsaTree(GenTree template, ConstValue value)
                => new SsaTree(CreateConstantTree(template, value), ImmutableArray<SsaTree>.Empty);

            private bool SameValue(SsaMethod method, SsaTree left, SsaTree right, Dictionary<SsaValueName, ValueFact> facts)
            {
                if (SameValueNumber(method, left, right))
                    return true;

                var leftFact = EvaluateTree(method, left, facts);
                var rightFact = EvaluateTree(method, right, facts);

                if (leftFact.Kind == ValueFactKind.Constant && rightFact.Kind == ValueFactKind.Constant)
                    return leftFact.Constant.Equals(rightFact.Constant);

                if (left.Value.HasValue && right.Value.HasValue)
                    return left.Value.Value.Equals(right.Value.Value);

                return false;
            }

            private static bool IsZero(ValueFact fact)
                => fact.Kind == ValueFactKind.Constant &&
                   (fact.Constant.Kind == ConstKind.I4 && fact.Constant.I4 == 0 ||
                    fact.Constant.Kind == ConstKind.I8 && fact.Constant.I8 == 0);

            private static bool IsOne(ValueFact fact)
                => fact.Kind == ValueFactKind.Constant &&
                   (fact.Constant.Kind == ConstKind.I4 && fact.Constant.I4 == 1 ||
                    fact.Constant.Kind == ConstKind.I8 && fact.Constant.I8 == 1);

            private static bool IsAllBitsSet(ValueFact fact)
                => fact.Kind == ValueFactKind.Constant &&
                   (fact.Constant.Kind == ConstKind.I4 && fact.Constant.I4 == -1 ||
                    fact.Constant.Kind == ConstKind.I8 && fact.Constant.I8 == -1);

            private static bool UsesEightByteInteger(GenStackKind stackKind)
                => stackKind == GenStackKind.I8 ||
                   ((stackKind is GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ptr) &&
                    TargetArchitecture.PointerSize == 8);

            private static bool IsEffectiveZeroShift(ValueFact fact, GenStackKind stackKind)
            {
                if (fact.Kind != ValueFactKind.Constant || fact.Constant.Kind == ConstKind.Null)
                    return false;

                int mask = UsesEightByteInteger(stackKind) ? 0x3f : 0x1f;
                int amount = fact.Constant.Kind == ConstKind.I8
                    ? unchecked((int)fact.Constant.I8)
                    : fact.Constant.I4;

                return (amount & mask) == 0;
            }

            private static ConstValue ZeroFor(GenTree template)
                => template.StackKind == GenStackKind.I8 ? ConstValue.ForI8(0) : ConstValue.ForI4(0);

            private static ConstValue AllBitsSetFor(GenTree template)
                => template.StackKind == GenStackKind.I8 ? ConstValue.ForI8(-1) : ConstValue.ForI4(-1);

            private GenTree CreateConstantTree(GenTree template, ConstValue constant)
            {
                return constant.Kind switch
                {
                    ConstKind.I4 => new GenTree(
                        _nextSyntheticTreeId++,
                        GenTreeKind.ConstI4,
                        template.Pc,
                        BytecodeOp.Ldc_I4,
                        type: null,
                        stackKind: GenStackKind.I4,
                        flags: GenTreeFlags.None,
                        operands: ImmutableArray<GenTree>.Empty,
                        int32: constant.I4),
                    ConstKind.I8 => new GenTree(
                        _nextSyntheticTreeId++,
                        GenTreeKind.ConstI8,
                        template.Pc,
                        BytecodeOp.Ldc_I8,
                        type: null,
                        stackKind: GenStackKind.I8,
                        flags: GenTreeFlags.None,
                        operands: ImmutableArray<GenTree>.Empty,
                        int64: constant.I8),
                    ConstKind.Null => new GenTree(
                        _nextSyntheticTreeId++,
                        GenTreeKind.ConstNull,
                        template.Pc,
                        BytecodeOp.Ldnull,
                        type: template.Type,
                        stackKind: GenStackKind.Null,
                        flags: GenTreeFlags.None,
                        operands: ImmutableArray<GenTree>.Empty),
                    _ => throw new InvalidOperationException("Unknown SSA constant kind."),
                };
            }

            private GenTree CreateEvalTree(GenTree template, ImmutableArray<SsaTree> operands)
            {
                var genOperands = ImmutableArray.CreateBuilder<GenTree>(operands.Length);
                GenTreeFlags flags = GenTreeFlags.None;
                for (int i = 0; i < operands.Length; i++)
                {
                    genOperands.Add(operands[i].Source);
                    flags |= operands[i].Source.Flags;
                }

                return new GenTree(
                    _nextSyntheticTreeId++,
                    GenTreeKind.Eval,
                    template.Pc,
                    BytecodeOp.Pop,
                    type: null,
                    stackKind: GenStackKind.Void,
                    flags: flags,
                    operands: genOperands.ToImmutable());
            }

            private static bool TryGetSourceConstant(GenTree source, out ConstValue constant)
            {
                switch (source.Kind)
                {
                    case GenTreeKind.ConstI4:
                        constant = ConstValue.ForI4(source.Int32);
                        return true;
                    case GenTreeKind.ConstI8:
                        constant = ConstValue.ForI8(source.Int64);
                        return true;
                    case GenTreeKind.ConstNull:
                        constant = ConstValue.Null;
                        return true;
                    default:
                        constant = default;
                        return false;
                }
            }

            private static bool CanFoldComparisonOperands(GenStackKind left, GenStackKind right)
            {
                if (left is GenStackKind.R4 or GenStackKind.R8 || right is GenStackKind.R4 or GenStackKind.R8)
                    return false;

                if (IsIntegerLike(left) && IsIntegerLike(right))
                    return true;

                return IsReferenceLike(left) && IsReferenceLike(right);
            }

            private static bool IsReferenceLike(GenStackKind stackKind)
                => stackKind is GenStackKind.Ref or GenStackKind.ByRef or GenStackKind.Null;

            private static bool IsIntegerLike(GenStackKind stackKind)
                => stackKind is GenStackKind.I4 or GenStackKind.I8 or GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ptr;

            private static bool TryFoldUnary(GenTree source, ConstValue operand, out ConstValue result)
            {
                result = default;
                if (source.SourceOp == BytecodeOp.Neg)
                {
                    if (operand.Kind == ConstKind.I4)
                    {
                        result = ConstValue.ForI4(unchecked(-operand.I4));
                        return true;
                    }
                    if (operand.Kind == ConstKind.I8)
                    {
                        result = ConstValue.ForI8(unchecked(-operand.I8));
                        return true;
                    }
                }

                if (source.SourceOp == BytecodeOp.Not)
                {
                    if (operand.Kind == ConstKind.I4)
                    {
                        result = ConstValue.ForI4(~operand.I4);
                        return true;
                    }
                    if (operand.Kind == ConstKind.I8)
                    {
                        result = ConstValue.ForI8(~operand.I8);
                        return true;
                    }
                }

                return false;
            }

            private static bool TryFoldBinary(GenTree source, ConstValue left, ConstValue right, out ConstValue result)
            {
                result = default;

                if (left.Kind == ConstKind.Null || right.Kind == ConstKind.Null)
                {
                    if (source.SourceOp == BytecodeOp.Ceq)
                    {
                        result = ConstValue.ForI4(left.Kind == ConstKind.Null && right.Kind == ConstKind.Null ? 1 : 0);
                        return true;
                    }
                    return false;
                }

                if (left.Kind == ConstKind.I8 || right.Kind == ConstKind.I8 || source.StackKind == GenStackKind.I8)
                    return TryFoldBinaryI8(source.SourceOp, left.Kind == ConstKind.I8 ? left.I8 : left.I4, right.Kind == ConstKind.I8 ? right.I8 : right.I4, out result);

                return TryFoldBinaryI4(source.SourceOp, left.I4, right.I4, out result);
            }

            private static bool TryFoldBinaryI4(BytecodeOp op, int left, int right, out ConstValue result)
            {
                result = default;
                switch (op)
                {
                    case BytecodeOp.Add_Ovf:
                        try
                        {
                            result = ConstValue.ForI4(checked(left + right));
                            return true;
                        }
                        catch (OverflowException)
                        {
                            return false;
                        }
                    case BytecodeOp.Sub_Ovf:
                        try
                        {
                            result = ConstValue.ForI4(checked(left - right));
                            return true;
                        }
                        catch (OverflowException)
                        {
                            return false;
                        }
                    case BytecodeOp.Mul_Ovf:
                        try
                        {
                            result = ConstValue.ForI4(checked(left * right));
                            return true;
                        }
                        catch (OverflowException)
                        {
                            return false;
                        }
                    case BytecodeOp.Add_Ovf_Un:
                        try
                        {
                            result = ConstValue.ForI4(unchecked((int)checked((uint)left + (uint)right)));
                            return true;
                        }
                        catch (OverflowException)
                        {
                            return false;
                        }
                    case BytecodeOp.Sub_Ovf_Un:
                        try
                        {
                            result = ConstValue.ForI4(unchecked((int)checked((uint)left - (uint)right)));
                            return true;
                        }
                        catch (OverflowException)
                        {
                            return false;
                        }
                    case BytecodeOp.Mul_Ovf_Un:
                        try
                        {
                            result = ConstValue.ForI4(unchecked((int)checked((uint)left * (uint)right)));
                            return true;
                        }
                        catch (OverflowException)
                        {
                            return false;
                        }
                    case BytecodeOp.Add:
                        result = ConstValue.ForI4(unchecked(left + right));
                        return true;
                    case BytecodeOp.Sub:
                        result = ConstValue.ForI4(unchecked(left - right));
                        return true;
                    case BytecodeOp.Mul:
                        result = ConstValue.ForI4(unchecked(left * right));
                        return true;
                    case BytecodeOp.Div:
                        if (right == 0 || (left == int.MinValue && right == -1)) return false;
                        result = ConstValue.ForI4(left / right);
                        return true;
                    case BytecodeOp.Div_Un:
                        if (right == 0) return false;
                        result = ConstValue.ForI4(unchecked((int)((uint)left / (uint)right)));
                        return true;
                    case BytecodeOp.Rem:
                        if (right == 0 || (left == int.MinValue && right == -1)) return false;
                        result = ConstValue.ForI4(left % right);
                        return true;
                    case BytecodeOp.Rem_Un:
                        if (right == 0) return false;
                        result = ConstValue.ForI4(unchecked((int)((uint)left % (uint)right)));
                        return true;
                    case BytecodeOp.And:
                        result = ConstValue.ForI4(left & right);
                        return true;
                    case BytecodeOp.Or:
                        result = ConstValue.ForI4(left | right);
                        return true;
                    case BytecodeOp.Xor:
                        result = ConstValue.ForI4(left ^ right);
                        return true;
                    case BytecodeOp.Shl:
                        result = ConstValue.ForI4(left << (right & 31));
                        return true;
                    case BytecodeOp.Shr:
                        result = ConstValue.ForI4(left >> (right & 31));
                        return true;
                    case BytecodeOp.Shr_Un:
                        result = ConstValue.ForI4(unchecked((int)((uint)left >> (right & 31))));
                        return true;
                    case BytecodeOp.Ceq:
                        result = ConstValue.ForI4(left == right ? 1 : 0);
                        return true;
                    case BytecodeOp.Clt:
                        result = ConstValue.ForI4(left < right ? 1 : 0);
                        return true;
                    case BytecodeOp.Clt_Un:
                        result = ConstValue.ForI4((uint)left < (uint)right ? 1 : 0);
                        return true;
                    case BytecodeOp.Cgt:
                        result = ConstValue.ForI4(left > right ? 1 : 0);
                        return true;
                    case BytecodeOp.Cgt_Un:
                        result = ConstValue.ForI4((uint)left > (uint)right ? 1 : 0);
                        return true;
                    default:
                        return false;
                }
            }
            private static bool TryFoldBinaryI8(BytecodeOp op, long left, long right, out ConstValue result)
            {
                result = default;
                switch (op)
                {
                    case BytecodeOp.Add_Ovf:
                        try
                        {
                            result = ConstValue.ForI8(checked(left + right));
                            return true;
                        }
                        catch (OverflowException)
                        {
                            return false;
                        }
                    case BytecodeOp.Sub_Ovf:
                        try
                        {
                            result = ConstValue.ForI8(checked(left - right));
                            return true;
                        }
                        catch (OverflowException)
                        {
                            return false;
                        }
                    case BytecodeOp.Mul_Ovf:
                        try
                        {
                            result = ConstValue.ForI8(checked(left * right));
                            return true;
                        }
                        catch (OverflowException)
                        {
                            return false;
                        }
                    case BytecodeOp.Add_Ovf_Un:
                        try
                        {
                            result = ConstValue.ForI8(unchecked((long)checked((ulong)left + (ulong)right)));
                            return true;
                        }
                        catch (OverflowException)
                        {
                            return false;
                        }
                    case BytecodeOp.Sub_Ovf_Un:
                        try
                        {
                            result = ConstValue.ForI8(unchecked((long)checked((ulong)left - (ulong)right)));
                            return true;
                        }
                        catch (OverflowException)
                        {
                            return false;
                        }
                    case BytecodeOp.Mul_Ovf_Un:
                        try
                        {
                            result = ConstValue.ForI8(unchecked((long)checked((ulong)left * (ulong)right)));
                            return true;
                        }
                        catch (OverflowException)
                        {
                            return false;
                        }
                    case BytecodeOp.Add:
                        result = ConstValue.ForI8(unchecked(left + right));
                        return true;
                    case BytecodeOp.Sub:
                        result = ConstValue.ForI8(unchecked(left - right));
                        return true;
                    case BytecodeOp.Mul:
                        result = ConstValue.ForI8(unchecked(left * right));
                        return true;
                    case BytecodeOp.Div:
                        if (right == 0 || (left == long.MinValue && right == -1)) return false;
                        result = ConstValue.ForI8(left / right);
                        return true;
                    case BytecodeOp.Div_Un:
                        if (right == 0) return false;
                        result = ConstValue.ForI8(unchecked((long)((ulong)left / (ulong)right)));
                        return true;
                    case BytecodeOp.Rem:
                        if (right == 0 || (left == long.MinValue && right == -1)) return false;
                        result = ConstValue.ForI8(left % right);
                        return true;
                    case BytecodeOp.Rem_Un:
                        if (right == 0) return false;
                        result = ConstValue.ForI8(unchecked((long)((ulong)left % (ulong)right)));
                        return true;
                    case BytecodeOp.And:
                        result = ConstValue.ForI8(left & right);
                        return true;
                    case BytecodeOp.Or:
                        result = ConstValue.ForI8(left | right);
                        return true;
                    case BytecodeOp.Xor:
                        result = ConstValue.ForI8(left ^ right);
                        return true;
                    case BytecodeOp.Shl:
                        result = ConstValue.ForI8(left << ((int)right & 63));
                        return true;
                    case BytecodeOp.Shr:
                        result = ConstValue.ForI8(left >> ((int)right & 63));
                        return true;
                    case BytecodeOp.Shr_Un:
                        result = ConstValue.ForI8(unchecked((long)((ulong)left >> ((int)right & 63))));
                        return true;
                    case BytecodeOp.Ceq:
                        result = ConstValue.ForI4(left == right ? 1 : 0);
                        return true;
                    case BytecodeOp.Clt:
                        result = ConstValue.ForI4(left < right ? 1 : 0);
                        return true;
                    case BytecodeOp.Clt_Un:
                        result = ConstValue.ForI4((ulong)left < (ulong)right ? 1 : 0);
                        return true;
                    case BytecodeOp.Cgt:
                        result = ConstValue.ForI4(left > right ? 1 : 0);
                        return true;
                    case BytecodeOp.Cgt_Un:
                        result = ConstValue.ForI4((ulong)left > (ulong)right ? 1 : 0);
                        return true;
                    default:
                        return false;
                }
            }

            private static bool TryFoldConversion(GenTree source, ConstValue operand, out ConstValue result)
            {
                result = default;
                if (operand.Kind == ConstKind.Null)
                    return false;

                bool isChecked = (source.ConvFlags & NumericConvFlags.Checked) != 0;
                bool sourceUnsigned = (source.ConvFlags & NumericConvFlags.SourceUnsigned) != 0;

                try
                {
                    switch (source.ConvKind)
                    {
                        case NumericConvKind.I1:
                            result = ConstValue.ForI4(isChecked
                                ? checked((sbyte)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4))
                                : unchecked((sbyte)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4)));
                            return true;
                        case NumericConvKind.U1:
                            result = ConstValue.ForI4(isChecked
                                ? checked((byte)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4))
                                : unchecked((byte)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4)));
                            return true;
                        case NumericConvKind.I2:
                            result = ConstValue.ForI4(isChecked
                                ? checked((short)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4))
                                : unchecked((short)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4)));
                            return true;
                        case NumericConvKind.U2:
                        case NumericConvKind.Char:
                            result = ConstValue.ForI4(isChecked
                                ? checked((ushort)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4))
                                : unchecked((ushort)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4)));
                            return true;
                        case NumericConvKind.I4:
                        case NumericConvKind.Bool:
                            result = ConstValue.ForI4(isChecked && operand.Kind == ConstKind.I8
                                ? checked((int)operand.I8)
                                : unchecked((int)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4)));
                            return true;
                        case NumericConvKind.U4:
                            result = ConstValue.ForI4(isChecked
                                ? unchecked((int)checked((uint)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4)))
                                : unchecked((int)(uint)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4)));
                            return true;
                        case NumericConvKind.I8:
                        case NumericConvKind.NativeInt:
                            result = ConstValue.ForI8(operand.Kind == ConstKind.I8
                                ? operand.I8
                                : sourceUnsigned ? (long)(uint)operand.I4 : operand.I4);
                            return true;
                        case NumericConvKind.U8:
                        case NumericConvKind.NativeUInt:
                            if (sourceUnsigned)
                            {
                                result = ConstValue.ForI8(operand.Kind == ConstKind.I8
                                    ? operand.I8
                                    : unchecked((long)(uint)operand.I4));
                            }
                            else
                            {
                                long signed = operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4;
                                result = ConstValue.ForI8(isChecked
                                    ? unchecked((long)checked((ulong)signed))
                                    : unchecked((long)(ulong)signed));
                            }
                            return true;
                        default:
                            return false;
                    }
                }
                catch (OverflowException)
                {
                    return false;
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

            private static SsaMethod WithBlocks(SsaMethod method, ImmutableArray<SsaBlock> blocks)
            {
                var compacted = CompactSsaNumbers(method.InitialValues, method.ValueDefinitions, blocks);
                var definitions = BuildValueDefinitions(method.Slots, compacted.InitialValues, compacted.Blocks, compacted.UseDefLinks);
                var memoryDefinitions = GenTreeSsaBuilder.BuildMemoryDefinitions(method.InitialMemoryValues, compacted.Blocks);
                GenTreeSsaBuilder.AnnotateSsaUses(definitions, memoryDefinitions, compacted.Blocks);
                var localDescriptors = BuildSsaLocalDescriptors(method.Slots, definitions, method.SsaLocalDescriptors);
                return new SsaMethod(
                    method.GenTreeMethod,
                    method.Cfg,
                    method.Slots,
                    compacted.InitialValues,
                    definitions,
                    compacted.Blocks,
                    valueNumbers: null,
                    ssaLocalDescriptors: localDescriptors,
                    initialMemoryValues: method.InitialMemoryValues,
                    memoryDefinitions: memoryDefinitions);
            }

            private readonly struct SsaCompactionResult
            {
                public readonly ImmutableArray<SsaValueName> InitialValues;
                public readonly ImmutableArray<SsaBlock> Blocks;
                public readonly ImmutableArray<SsaUseDefLink> UseDefLinks;

                public SsaCompactionResult(ImmutableArray<SsaValueName> initialValues, ImmutableArray<SsaBlock> blocks, ImmutableArray<SsaUseDefLink> useDefLinks)
                {
                    InitialValues = initialValues;
                    Blocks = blocks;
                    UseDefLinks = useDefLinks.IsDefault ? ImmutableArray<SsaUseDefLink>.Empty : useDefLinks;
                }
            }

            private static SsaCompactionResult CompactSsaNumbers(
                ImmutableArray<SsaValueName> initialValues, ImmutableArray<SsaValueDefinition> previousDefinitions, ImmutableArray<SsaBlock> blocks)
            {
                var map = new Dictionary<SsaValueName, SsaValueName>();
                var nextBySlot = new Dictionary<SsaSlot, int>();
                var compactedInitialValues = ImmutableArray.CreateBuilder<SsaValueName>(initialValues.Length);

                for (int i = 0; i < initialValues.Length; i++)
                {
                    var oldInitial = initialValues[i];
                    var newInitial = new SsaValueName(oldInitial.Slot, SsaConfig.FirstSsaNumber);
                    if (!map.TryAdd(oldInitial, newInitial))
                        throw new InvalidOperationException($"Duplicate initial SSA value {oldInitial}.");
                    nextBySlot[oldInitial.Slot] = SsaConfig.FirstSsaNumber;
                    compactedInitialValues.Add(newInitial);
                }

                for (int b = 0; b < blocks.Length; b++)
                {
                    var block = blocks[b];
                    for (int p = 0; p < block.Phis.Length; p++)
                        AssignDefinition(block.Phis[p].Target);

                    for (int n = 0; n < block.TreeList.Length; n++)
                    {
                        var tree = block.TreeList[n].Tree;
                        if (tree.StoreTarget.HasValue)
                            AssignDefinition(tree.StoreTarget.Value);
                    }
                }

                var compactedBlocks = ImmutableArray.CreateBuilder<SsaBlock>(blocks.Length);
                for (int b = 0; b < blocks.Length; b++)
                {
                    var block = blocks[b];
                    var phis = ImmutableArray.CreateBuilder<SsaPhi>(block.Phis.Length);
                    for (int p = 0; p < block.Phis.Length; p++)
                    {
                        var phi = block.Phis[p];
                        var inputs = ImmutableArray.CreateBuilder<SsaPhiInput>(phi.Inputs.Length);
                        for (int i = 0; i < phi.Inputs.Length; i++)
                        {
                            var input = phi.Inputs[i];
                            inputs.Add(new SsaPhiInput(input.PredecessorBlockId, MapUse(input.Value, "phi input")));
                        }

                        phis.Add(new SsaPhi(block.Id, phi.Slot, MapDefinition(phi.Target), inputs.ToImmutable()));
                    }

                    var statements = ImmutableArray.CreateBuilder<SsaTree>(block.Statements.Length);
                    var statementTreeLists = ImmutableArray.CreateBuilder<ImmutableArray<SsaTree>>(block.Statements.Length);
                    for (int s = 0; s < block.Statements.Length; s++)
                    {
                        var rewritten = RewriteStatement(block.Statements[s], block.StatementTreeLists[s]);
                        statements.Add(rewritten.Root);
                        statementTreeLists.Add(rewritten.TreeList);
                    }

                    compactedBlocks.Add(new SsaBlock(
                        block.CfgBlock,
                        phis.ToImmutable(),
                        statements.ToImmutable(),
                        block.MemoryPhis,
                        block.MemoryIn,
                        block.MemoryOut,
                        statementTreeLists: statementTreeLists.ToImmutable()));
                }

                var initialList = new List<SsaValueName>(compactedInitialValues.Count);
                for (int i = 0; i < compactedInitialValues.Count; i++)
                    initialList.Add(compactedInitialValues[i]);
                initialList.Sort();
                var compactedUseDefs = ImmutableArray.CreateBuilder<SsaUseDefLink>();
                for (int i = 0; i < previousDefinitions.Length; i++)
                {
                    var descriptor = previousDefinitions[i].Descriptor;
                    if (!descriptor.HasUseDefSsaNum)
                        continue;

                    var oldDef = descriptor.Name;
                    var oldUse = new SsaValueName(descriptor.BaseLocal, descriptor.UseDefSsaNumber);
                    if (map.TryGetValue(oldDef, out var compactedDef) && map.TryGetValue(oldUse, out var compactedUse))
                        compactedUseDefs.Add(new SsaUseDefLink(compactedDef, compactedUse));
                }

                return new SsaCompactionResult(initialList.ToImmutableArray(), compactedBlocks.ToImmutable(), compactedUseDefs.ToImmutable());

                void AssignDefinitions(SsaTree tree)
                {
                    for (int i = 0; i < tree.Operands.Length; i++)
                        AssignDefinitions(tree.Operands[i]);

                    if (tree.StoreTarget.HasValue)
                        AssignDefinition(tree.StoreTarget.Value);
                }

                void AssignDefinition(SsaValueName oldName)
                {
                    if (oldName.Version <= SsaConfig.ReservedSsaNumber)
                        throw new InvalidOperationException($"Non-initial SSA definition reused the reserved SSA number: {oldName}.");
                    if (map.ContainsKey(oldName))
                        throw new InvalidOperationException($"Duplicate active SSA definition {oldName}.");

                    int next = nextBySlot.TryGetValue(oldName.Slot, out int current) ? current + 1 : SsaConfig.FirstSsaNumber;
                    var newName = new SsaValueName(oldName.Slot, next);
                    nextBySlot[oldName.Slot] = next;
                    map.Add(oldName, newName);
                }

                SsaValueName MapDefinition(SsaValueName oldName)
                {
                    if (map.TryGetValue(oldName, out var newName))
                        return newName;
                    throw new InvalidOperationException($"Active SSA definition {oldName} was not assigned a compact SSA number.");
                }

                SsaValueName MapUse(SsaValueName oldName, string context)
                {
                    if (map.TryGetValue(oldName, out var newName))
                        return newName;
                    throw new InvalidOperationException($"SSA {context} uses removed or never-defined value {oldName}.");
                }

                (SsaTree Root, ImmutableArray<SsaTree> TreeList) RewriteStatement(SsaTree root, ImmutableArray<SsaTree> treeList)
                {
                    var rewrittenByTree = new Dictionary<SsaTree, SsaTree>(ReferenceEqualityComparer<SsaTree>.Instance);
                    var rewrittenTreeList = ImmutableArray.CreateBuilder<SsaTree>(treeList.Length);

                    for (int i = 0; i < treeList.Length; i++)
                    {
                        var rewritten = RewriteNode(treeList[i], rewrittenByTree);
                        rewrittenByTree[treeList[i]] = rewritten;
                        rewrittenTreeList.Add(rewritten);
                    }

                    var rewrittenRoot = rewrittenByTree.TryGetValue(root, out var foundRoot) ? foundRoot : RewriteNode(root, rewrittenByTree);
                    return (rewrittenRoot, ProjectReachableTreeList(rewrittenRoot, rewrittenTreeList.ToImmutable()));
                }

                SsaTree RewriteNode(SsaTree tree, Dictionary<SsaTree, SsaTree> rewrittenByTree)
                {
                    var operands = ImmutableArray.CreateBuilder<SsaTree>(tree.Operands.Length);
                    for (int i = 0; i < tree.Operands.Length; i++)
                    {
                        if (!rewrittenByTree.TryGetValue(tree.Operands[i], out var rewrittenOperand))
                            rewrittenOperand = RewriteNode(tree.Operands[i], rewrittenByTree);
                        operands.Add(rewrittenOperand);
                    }

                    SsaValueName? value = tree.Value.HasValue
                        ? MapUse(tree.Value.Value, "tree use")
                        : null;
                    SsaValueName? target = tree.StoreTarget.HasValue
                        ? MapDefinition(tree.StoreTarget.Value)
                        : null;
                    SsaValueName? localFieldBase = tree.LocalFieldBaseValue.HasValue
                        ? MapUse(tree.LocalFieldBaseValue.Value, "local field base")
                        : null;
                    return new SsaTree(
                        tree.Source, operands.ToImmutable(), value, target, localFieldBase, tree.LocalField, tree.MemoryUses, tree.MemoryDefinitions);
                }
            }

            private static ImmutableArray<SsaUseDefLink> ExtractUseDefLinks(ImmutableArray<SsaValueDefinition> definitions)
            {
                var result = ImmutableArray.CreateBuilder<SsaUseDefLink>();
                for (int i = 0; i < definitions.Length; i++)
                {
                    var descriptor = definitions[i].Descriptor;
                    if (!descriptor.HasUseDefSsaNum)
                        continue;

                    var definition = descriptor.Name;
                    var use = new SsaValueName(descriptor.BaseLocal, descriptor.UseDefSsaNumber);
                    result.Add(new SsaUseDefLink(definition, use));
                }

                return result.ToImmutable();
            }

            private static ImmutableArray<SsaValueDefinition> BuildValueDefinitions(
                ImmutableArray<SsaSlotInfo> slots,
                ImmutableArray<SsaValueName> initialValues,
                ImmutableArray<SsaBlock> blocks,
                ImmutableArray<SsaUseDefLink> useDefLinks)
            {
                var infoBySlot = new Dictionary<SsaSlot, SsaSlotInfo>();
                for (int i = 0; i < slots.Length; i++)
                    infoBySlot[slots[i].Slot] = slots[i];

                var definitions = new List<SsaValueDefinition>();
                var seen = new HashSet<SsaValueName>();

                for (int i = 0; i < initialValues.Length; i++)
                {
                    var name = initialValues[i];
                    var info = GetSlotInfo(infoBySlot, name.Slot);
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
                        var info = GetSlotInfo(infoBySlot, phi.Target.Slot);
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
                        CollectDefinition(block.TreeList[i].Tree, block, block.TreeList[i].StatementIndex);
                }

                definitions.Sort(static (a, b) => a.Name.CompareTo(b.Name));
                return definitions.ToImmutableArray();

                void CollectDefinition(SsaTree tree, SsaBlock block, int statementIndex)
                {
                    int blockId = block.Id;

                    if (!tree.StoreTarget.HasValue)
                        return;

                    var name = tree.StoreTarget.Value;
                    var info = GetSlotInfo(infoBySlot, name.Slot);
                    int useDefSsaNum = SsaConfig.ReservedSsaNumber;
                    for (int i = 0; i < useDefLinks.Length; i++)
                    {
                        if (useDefLinks[i].Definition.Equals(name))
                        {
                            useDefSsaNum = useDefLinks[i].Use.Version;
                            break;
                        }
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

                void Add(SsaValueDefinition definition)
                {
                    if (!seen.Add(definition.Name))
                        throw new InvalidOperationException($"Duplicate SSA definition {definition.Name}.");
                    definitions.Add(definition);
                }
            }

            private static ImmutableArray<SsaLocalDescriptor> BuildSsaLocalDescriptors(
                ImmutableArray<SsaSlotInfo> slots,
                ImmutableArray<SsaValueDefinition> valueDefinitions,
                ImmutableArray<SsaLocalDescriptor> previousDescriptors)
            {
                var previousBySlot = new Dictionary<SsaSlot, SsaLocalDescriptor>();
                for (int i = 0; i < previousDescriptors.Length; i++)
                    previousBySlot[previousDescriptors[i].Slot] = previousDescriptors[i];

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

                var result = ImmutableArray.CreateBuilder<SsaLocalDescriptor>(slots.Length);
                for (int i = 0; i < slots.Length; i++)
                {
                    var info = slots[i];
                    descriptorsBySlot.TryGetValue(info.Slot, out var list);
                    var perSsaData = DensePerSsaData(info.Slot, list);
                    previousBySlot.TryGetValue(info.Slot, out var previous);
                    var local = new SsaLocalDescriptor(
                        info.Slot,
                        info.Type,
                        info.StackKind,
                        info.AddressExposed,
                        previous?.IsSsaPromoted ?? perSsaData.Length != 0,
                        previous?.LocalDescriptor,
                        perSsaData);
                    previous?.LocalDescriptor?.SetSsaDescriptors(perSsaData);
                    result.Add(local);
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
                            throw new InvalidOperationException($"Duplicate SSA descriptor {descriptor.Name}.");
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
                            throw new InvalidOperationException($"Missing SSA descriptor {slot}_{i}.");
                        builder.Add(table[i]!);
                    }
                    return builder.ToImmutable();
                }
            }

            private static SsaSlotInfo GetSlotInfo(Dictionary<SsaSlot, SsaSlotInfo> infoBySlot, SsaSlot slot)
            {
                if (infoBySlot.TryGetValue(slot, out var info))
                    return info;
                return new SsaSlotInfo(
                    slot, null, GenStackKind.Unknown, addressExposed: true, memoryAliased: true, category: GenLocalCategory.AddressExposedLocal);
            }

            private static int MaxTreeId(SsaMethod method)
            {
                int max = 0;
                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    var block = method.Blocks[b];
                    for (int s = 0; s < block.Statements.Length; s++)
                        Visit(block.Statements[s]);
                }
                return max;

                void Visit(SsaTree tree)
                {
                    if (tree.Source.Id > max)
                        max = tree.Source.Id;
                    for (int i = 0; i < tree.Operands.Length; i++)
                        Visit(tree.Operands[i]);
                }
            }
        }
    }
}
