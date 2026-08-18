using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal sealed class SsaLoopHoistingResult
    {
        public SsaMethod Method { get; }
        public bool Changed { get; }
        public int NextSyntheticTreeId { get; }

        public SsaLoopHoistingResult(SsaMethod method, bool changed, int nextSyntheticTreeId)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Changed = changed;
            NextSyntheticTreeId = nextSyntheticTreeId;
        }
    }

    internal static class SsaLoopHoister
    {
        public static SsaLoopHoistingResult OptimizeMethod(
            SsaMethod method,
            SsaOptimizationOptions options,
            int nextSyntheticTreeId)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (options is null)
                throw new ArgumentNullException(nameof(options));
            if (method.ValueNumbers is null || method.Blocks.Length == 0 || method.Cfg.NaturalLoops.IsDefaultOrEmpty)
                return new SsaLoopHoistingResult(method, changed: false, nextSyntheticTreeId);
            if (options.MaxLoopHoistsPerLoop <= 0)
                return new SsaLoopHoistingResult(method, changed: false, nextSyntheticTreeId);

            var canonicalized = CanonicalizeLoopPreheaders(method, options.Validate, nextSyntheticTreeId);
            if (canonicalized.Method.Cfg.NaturalLoops.IsDefaultOrEmpty)
                return canonicalized;

            var optimized = new Optimizer(canonicalized.Method, options, canonicalized.NextSyntheticTreeId).Run();
            return new SsaLoopHoistingResult(
                optimized.Method,
                canonicalized.Changed || optimized.Changed,
                optimized.NextSyntheticTreeId);
        }

        private static SsaLoopHoistingResult CanonicalizeLoopPreheaders(
            SsaMethod method,
            bool validate,
            int nextSyntheticTreeId)
        {
            var current = method;
            bool changed = false;
            int nextTreeId = Math.Max(nextSyntheticTreeId, MaxTreeId(method) + 1);
            int remaining = Math.Max(8, method.Cfg.NaturalLoops.Length * 2 + 4);
            var rejectedHeaders = new HashSet<int>();

            while (remaining-- > 0)
            {
                CfgLoop candidate = default;
                bool found = false;
                var loops = current.Cfg.NaturalLoops;
                for (int i = 0; i < loops.Length; i++)
                {
                    var loop = loops[i];
                    if (!loop.IsReducible || loop.IsCanonicalPreheader || rejectedHeaders.Contains(loop.Header))
                        continue;
                    if (loop.Entries.Length != 1 || loop.Entries[0] != loop.Header)
                        continue;

                    candidate = loop;
                    found = true;
                    break;
                }

                if (!found)
                    break;

                var rewritten = GenTreeCriticalEdgeSplitter.CreateLoopPreheader(
                    current.GenTreeMethod,
                    current.Cfg,
                    candidate);
                if (ReferenceEquals(rewritten, current.GenTreeMethod))
                {
                    rejectedHeaders.Add(candidate.Header);
                    continue;
                }

                current = RebuildSsa(current, rewritten, validate);
                nextTreeId = Math.Max(nextTreeId, MaxTreeId(current) + 1);
                rejectedHeaders.Clear();
                changed = true;
            }

            return new SsaLoopHoistingResult(current, changed, nextTreeId);
        }

        private readonly struct LoopOrder
        {
            public readonly int Header;
            public readonly int Depth;
            public readonly int Size;

            public LoopOrder(int header, int depth, int size)
            {
                Header = header;
                Depth = depth;
                Size = size;
            }
        }

        private readonly struct HoistKey : IEquatable<HoistKey>
        {
            public readonly ValueNumber Value;
            public readonly ValueNumberType Type;

            public HoistKey(ValueNumber value, GenStackKind stackKind, RuntimeType? type)
            {
                Value = value;
                Type = ValueNumberType.For(stackKind, type);
            }

            public bool Equals(HoistKey other)
                => Value == other.Value && Type.Equals(other.Type);

            public override bool Equals(object? obj)
                => obj is HoistKey other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(Value, Type);
        }

        private sealed class HoistCandidate
        {
            public readonly GenTree Source;
            public int CloneTreeId;

            public HoistCandidate(GenTree source)
            {
                Source = source;
            }
        }

        private sealed class Optimizer
        {
            private readonly SsaOptimizationOptions _options;
            private SsaMethod _current;
            private int _nextSyntheticTreeId;
            private bool _changed;

            public Optimizer(SsaMethod method, SsaOptimizationOptions options, int nextSyntheticTreeId)
            {
                _current = method;
                _options = options;
                _nextSyntheticTreeId = Math.Max(nextSyntheticTreeId, MaxTreeId(method) + 1);
            }

            public SsaLoopHoistingResult Run()
            {
                var order = BuildLoopOrder(_current.Cfg);
                for (int i = 0; i < order.Count; i++)
                {
                    if (!TryFindLoop(_current.Cfg, order[i].Header, out var loop))
                        continue;
                    if (!CanHoistLoop(_current, loop))
                        continue;

                    var pass = new LoopPass(_current, loop, _options, _nextSyntheticTreeId);
                    var result = pass.Run();
                    _nextSyntheticTreeId = Math.Max(_nextSyntheticTreeId, result.NextSyntheticTreeId);
                    if (!result.Changed)
                        continue;

                    _current = result.Method;
                    _changed = true;
                }

                return new SsaLoopHoistingResult(_current, _changed, _nextSyntheticTreeId);
            }

            private static List<LoopOrder> BuildLoopOrder(ControlFlowGraph cfg)
            {
                var result = new List<LoopOrder>(cfg.NaturalLoops.Length);
                for (int i = 0; i < cfg.NaturalLoops.Length; i++)
                {
                    var loop = cfg.NaturalLoops[i];
                    result.Add(new LoopOrder(loop.Header, loop.Depth, loop.Blocks.Length));
                }

                result.Sort(static (left, right) =>
                {
                    int c = right.Depth.CompareTo(left.Depth);
                    if (c != 0)
                        return c;
                    c = left.Size.CompareTo(right.Size);
                    if (c != 0)
                        return c;
                    return left.Header.CompareTo(right.Header);
                });
                return result;
            }

            private static bool TryFindLoop(ControlFlowGraph cfg, int header, out CfgLoop loop)
            {
                for (int i = 0; i < cfg.NaturalLoops.Length; i++)
                {
                    if (cfg.NaturalLoops[i].Header == header)
                    {
                        loop = cfg.NaturalLoops[i];
                        return true;
                    }
                }

                loop = default;
                return false;
            }

            private static bool CanHoistLoop(SsaMethod method, CfgLoop loop)
            {
                if (!loop.IsReducible || !loop.IsCanonicalPreheader)
                    return false;
                if (loop.Preheader < 0 || (uint)loop.Preheader >= (uint)method.Blocks.Length)
                    return false;
                if (loop.Contains(loop.Preheader))
                    return false;
                if (loop.Entries.Length != 1 || loop.Entries[0] != loop.Header)
                    return false;
                if ((uint)loop.Header >= (uint)method.Blocks.Length)
                    return false;

                var predecessors = method.Cfg.Blocks[loop.Header].Predecessors;
                for (int i = 0; i < predecessors.Length; i++)
                {
                    var predecessor = predecessors[i];
                    if (loop.Contains(predecessor.FromBlockId))
                        continue;
                    if (predecessor.Kind == CfgEdgeKind.Exception || predecessor.FromBlockId != loop.Preheader)
                        return false;
                }

                return true;
            }
        }

        private sealed class LoopPass
        {
            private readonly SsaMethod _method;
            private readonly CfgLoop _loop;
            private readonly SsaOptimizationOptions _options;
            private readonly SsaValueNumberingResult _vn;
            private readonly Dictionary<SsaValueName, SsaValueDefinition> _definitions = new();
            private readonly SsaLoopInvariantValueNumberAnalysis _invariance;
            private readonly HashSet<HoistKey> _hoistedValues = new();
            private readonly HashSet<int> _definitelyExecuted = new();
            private readonly List<HoistCandidate> _candidates = new();
            private readonly int _preheaderInsertionIndex;
            private readonly bool _loopContainsCall;
            private readonly int _loopGeneralVarCount;
            private readonly int _loopFloatVarCount;
            private readonly int _loopGeneralVarInOutCount;
            private readonly int _loopFloatVarInOutCount;
            private int _hoistedGeneralCount;
            private int _hoistedFloatCount;
            private int _nextSyntheticTreeId;

            private sealed class NodeState
            {
                public readonly GenTree Node;
                public readonly ImmutableArray<NodeState> Children;
                public readonly bool Invariant;
                public readonly bool Hoistable;

                public NodeState(GenTree node, ImmutableArray<NodeState> children, bool invariant, bool hoistable)
                {
                    Node = node;
                    Children = children;
                    Invariant = invariant;
                    Hoistable = hoistable;
                }
            }

            public LoopPass(SsaMethod method, CfgLoop loop, SsaOptimizationOptions options, int nextSyntheticTreeId)
            {
                _method = method;
                _loop = loop;
                _options = options;
                _vn = method.ValueNumbers!;
                _invariance = new SsaLoopInvariantValueNumberAnalysis(method, loop);
                _nextSyntheticTreeId = Math.Max(nextSyntheticTreeId, MaxTreeId(method) + 1);
                _preheaderInsertionIndex = FindTerminatorStart(method.GenTreeMethod.Blocks[loop.Preheader].Statements);

                for (int i = 0; i < method.ValueDefinitions.Length; i++)
                {
                    var definition = method.ValueDefinitions[i];
                    _definitions[definition.Name] = definition;
                }

                BuildDefinitelyExecutedSet();
                CollectLoopPressure(
                    out bool loopContainsCall,
                    out int loopGeneralVarCount,
                    out int loopFloatVarCount,
                    out int loopGeneralVarInOutCount,
                    out int loopFloatVarInOutCount);
                _loopContainsCall = loopContainsCall;
                _loopGeneralVarCount = loopGeneralVarCount;
                _loopFloatVarCount = loopFloatVarCount;
                _loopGeneralVarInOutCount = loopGeneralVarInOutCount;
                _loopFloatVarInOutCount = loopFloatVarInOutCount;
            }

            public SsaLoopHoistingResult Run()
            {
                CollectCandidates();
                if (_candidates.Count == 0)
                    return new SsaLoopHoistingResult(_method, changed: false, _nextSyntheticTreeId);

                var rewritten = InsertHoistedExpressions();
                var rebuilt = RebuildSsa(_method, rewritten, _options.Validate);
                if (!ValidateHoists(rebuilt))
                    return new SsaLoopHoistingResult(_method, changed: false, _nextSyntheticTreeId);

                return new SsaLoopHoistingResult(rebuilt, changed: true, _nextSyntheticTreeId);
            }

            private void CollectCandidates()
            {
                var order = BuildBlockOrder();
                bool canHoistSideEffects = true;
                for (int i = 0; i < order.Count && _candidates.Count < _options.MaxLoopHoistsPerLoop; i++)
                {
                    int blockId = order[i];
                    if (!SameTryRegion(_method.Cfg.Blocks[_loop.Preheader], _method.Cfg.Blocks[blockId]))
                        continue;

                    if (blockId != _loop.Header)
                        canHoistSideEffects = false;

                    var block = _method.Blocks[blockId];
                    for (int s = 0; s < block.Statements.Length && _candidates.Count < _options.MaxLoopHoistsPerLoop; s++)
                    {
                        var state = AnalyzeTree(block.Statements[s].Source, blockId, ref canHoistSideEffects);
                        SelectCandidates(state, blockId);
                    }

                    canHoistSideEffects = false;
                }
            }

            private List<int> BuildBlockOrder()
            {
                var result = new List<int>(_loop.Blocks.Length);
                result.Add(_loop.Header);

                var added = new HashSet<int> { _loop.Header };
                var rpo = _method.Cfg.ReversePostOrder;
                for (int i = 0; i < rpo.Length; i++)
                {
                    int blockId = rpo[i];
                    if (_loop.Contains(blockId) && added.Add(blockId))
                        result.Add(blockId);
                }

                for (int i = 0; i < _loop.Blocks.Length; i++)
                {
                    int blockId = _loop.Blocks[i];
                    if (added.Add(blockId))
                        result.Add(blockId);
                }

                return result;
            }

            private NodeState AnalyzeTree(GenTree node, int blockId, ref bool canHoistSideEffects)
            {
                ImmutableArray<NodeState> children = ImmutableArray<NodeState>.Empty;
                bool childrenInvariant = true;
                if (!node.Operands.IsDefaultOrEmpty)
                {
                    var builder = ImmutableArray.CreateBuilder<NodeState>(node.Operands.Length);
                    for (int i = 0; i < node.Operands.Length; i++)
                    {
                        var child = AnalyzeTree(node.Operands[i], blockId, ref canHoistSideEffects);
                        builder.Add(child);
                        childrenInvariant &= child.Invariant;
                    }
                    children = builder.ToImmutable();
                }

                bool invariant = IsTreeInvariant(node, blockId, childrenInvariant);
                bool hoistable = invariant &&
                    node.Parent is not null &&
                    SsaCseCandidatePolicy.CanConsider(node) &&
                    UsesAvailableValues(node);

                if (hoistable && (node.Flags & GenTreeFlags.CanThrow) != 0 && !canHoistSideEffects)
                    hoistable = false;

                if (canHoistSideEffects)
                {
                    if (!invariant && (node.Flags & GenTreeFlags.CanThrow) != 0)
                        canHoistSideEffects = false;
                    if (HasGloballyVisibleSideEffect(node))
                        canHoistSideEffects = false;
                }

                return new NodeState(node, children, invariant, hoistable);
            }

            private bool IsTreeInvariant(GenTree node, int blockId, bool childrenInvariant)
            {
                if (node.Kind is GenTreeKind.Local or GenTreeKind.Arg or GenTreeKind.Temp)
                {
                    if (!node.SsaValueName.HasValue || !DefinitionAvailableAtPreheader(node.SsaValueName.Value))
                        return false;
                    if (!_vn.TryGetTreeValue(node, out var localPair))
                        return false;
                    return _invariance.IsInvariant(localPair.Liberal);
                }

                if (node.Kind is GenTreeKind.LocalAddr or GenTreeKind.ArgAddr or GenTreeKind.TempAddr)
                    return false;
                if (!childrenInvariant)
                    return false;
                if (!_vn.TryGetTreeValue(node, out var pair))
                    return false;
                if (!_invariance.IsInvariant(pair.Liberal))
                    return false;
                return _invariance.AreTreeMemoryDependenciesInvariant(node, blockId);
            }

            private void SelectCandidates(NodeState state, int blockId)
            {
                if (_candidates.Count >= _options.MaxLoopHoistsPerLoop)
                    return;

                if (state.Hoistable)
                {
                    TrySelectCandidate(state.Node, blockId);
                    return;
                }

                for (int i = 0; i < state.Children.Length && _candidates.Count < _options.MaxLoopHoistsPerLoop; i++)
                    SelectCandidates(state.Children[i], blockId);
            }

            private CandidateDisposition TrySelectCandidate(GenTree node, int blockId)
            {
                if (!_vn.TryGetTreeValue(node, out var pair))
                    return CandidateDisposition.NotSelected;

                ValueNumber normal = _vn.Store.VNNormalValue(pair.Liberal);
                if (!normal.IsValid || _vn.Store.TryGetConstant(normal, out _))
                    return CandidateDisposition.NotSelected;

                var key = new HoistKey(normal, node.StackKind, node.Type);
                if (_hoistedValues.Contains(key))
                    return CandidateDisposition.Covered;
                if (!IsProfitableToHoist(node, blockId))
                    return CandidateDisposition.NotSelected;

                _hoistedValues.Add(key);
                _candidates.Add(new HoistCandidate(node));
                if (IsFloatStackKind(node.StackKind))
                {
                    _hoistedFloatCount++;
                }
                else
                {
                    _hoistedGeneralCount +=
                        node.StackKind == GenStackKind.I8 && _method.GenTreeMethod.Target.PointerSize == 4
                            ? 2
                            : 1;
                }
                return CandidateDisposition.Selected;
            }

            private bool IsProfitableToHoist(GenTree node, int blockId)
            {
                int cost = SsaCseCandidatePolicy.EstimateCost(node);
                int minimumCost = Math.Max(1, _options.LoopHoistMinCost);
                if (cost < minimumCost)
                    return false;

                _ = _definitelyExecuted.Contains(blockId);

                bool isFloat = IsFloatStackKind(node.StackKind);
                int loopVarCount = isFloat ? _loopFloatVarCount : _loopGeneralVarCount;
                int varInOutCount = isFloat ? _loopFloatVarInOutCount : _loopGeneralVarInOutCount;
                int hoistedCount = isFloat ? _hoistedFloatCount : _hoistedGeneralCount;
                int availableRegisters = AvailableRegisterCount(isFloat);
                if (!isFloat && _method.GenTreeMethod.Target.PointerSize == 4 && node.StackKind == GenStackKind.I8)
                    availableRegisters = (availableRegisters + 1) / 2;
                availableRegisters -= hoistedCount;

                if (loopVarCount >= availableRegisters && cost < minimumCost * 2)
                    return false;
                if (varInOutCount > availableRegisters && cost <= minimumCost + 1)
                    return false;

                return true;
            }

            private int AvailableRegisterCount(bool isFloat)
            {
                var registers = isFloat
                    ? RegisterInfo.AllocatableFloatingRegisters(_method.GenTreeMethod.Target)
                    : RegisterInfo.AllocatableGeneralRegisters(_method.GenTreeMethod.Target);

                int calleeSaved = 0;
                int callerSaved = 0;
                for (int i = 0; i < registers.Length; i++)
                {
                    if (RegisterInfo.IsCalleeSaved(_method.GenTreeMethod.Target, registers[i]))
                        calleeSaved++;
                    else if (RegisterInfo.IsCallerSaved(_method.GenTreeMethod.Target, registers[i]))
                        callerSaved++;
                }

                int available = isFloat
                    ? calleeSaved
                    : calleeSaved - 1;
                if (!_loopContainsCall)
                    available += callerSaved - 1;
                return available;
            }

            private void CollectLoopPressure(
                out bool containsCall,
                out int generalCount,
                out int floatCount,
                out int generalInOutCount,
                out int floatInOutCount)
            {
                containsCall = false;
                for (int i = 0; i < _loop.Blocks.Length; i++)
                {
                    int blockId = _loop.Blocks[i];
                    var treeList = _method.Blocks[blockId].TreeList;
                    for (int t = 0; t < treeList.Length; t++)
                        containsCall |= (treeList[t].Tree.Source.Flags & GenTreeFlags.ContainsCall) != 0;
                }

                var liveness = SsaLocalLiveness.Build(_method);
                generalCount = 0;
                floatCount = 0;
                generalInOutCount = 0;
                floatInOutCount = 0;

                for (int i = 0; i < liveness.Table.Slots.Length; i++)
                {
                    var slot = liveness.Table.Slots[i];
                    bool usedOrDefined = false;
                    bool liveInOrOut = false;
                    for (int b = 0; b < _loop.Blocks.Length; b++)
                    {
                        int blockId = _loop.Blocks[b];
                        usedOrDefined |= liveness.UseBits[blockId].Contains(slot) || liveness.DefBits[blockId].Contains(slot);
                        liveInOrOut |= liveness.LiveInBits[blockId].Contains(slot) || liveness.LiveOutBits[blockId].Contains(slot);
                    }

                    if (!liveInOrOut)
                        continue;
                    if (!_method.TryGetSsaLocalDescriptor(slot, out var descriptor))
                        continue;

                    bool isFloat = IsFloatStackKind(descriptor.StackKind);
                    int weight = !isFloat &&
                                 descriptor.StackKind == GenStackKind.I8 &&
                                 _method.GenTreeMethod.Target.PointerSize == 4
                        ? 2
                        : 1;
                    if (isFloat)
                    {
                        floatInOutCount += weight;
                        if (usedOrDefined)
                            floatCount += weight;
                    }
                    else if (IsGeneralRegisterStackKind(descriptor.StackKind))
                    {
                        generalInOutCount += weight;
                        if (usedOrDefined)
                            generalCount += weight;
                    }
                }
            }

            private static bool IsGeneralRegisterStackKind(GenStackKind stackKind)
                => stackKind is
                    GenStackKind.I4 or
                    GenStackKind.I8 or
                    GenStackKind.NativeInt or
                    GenStackKind.NativeUInt or
                    GenStackKind.Ref or
                    GenStackKind.ByRef or
                    GenStackKind.Ptr;

            private static bool IsFloatStackKind(GenStackKind stackKind)
                => stackKind is GenStackKind.R4 or GenStackKind.R8;

            private static bool HasGloballyVisibleSideEffect(GenTree node)
            {
                if (node.Kind is GenTreeKind.StoreLocal or GenTreeKind.StoreArg or GenTreeKind.StoreTemp)
                    return node.LocalDescriptor is null || node.LocalDescriptor.HasMemoryAlias;

                if (node.Kind is GenTreeKind.Intrinsic or
                    GenTreeKind.Call or
                    GenTreeKind.IndirectCall or
                    GenTreeKind.VirtualCall or
                    GenTreeKind.NewObject or
                    GenTreeKind.NewDelegate or
                    GenTreeKind.DelegateCombine or
                    GenTreeKind.DelegateRemove or
                    GenTreeKind.DelegateInvoke or
                    GenTreeKind.GcPoll)
                {
                    return true;
                }

                return (node.Flags & (GenTreeFlags.MemoryWrite |
                                      GenTreeFlags.AddressExposed |
                                      GenTreeFlags.Allocation |
                                      GenTreeFlags.ControlFlow |
                                      GenTreeFlags.ExceptionFlow |
                                      GenTreeFlags.Ordered)) != 0;
            }

            private bool UsesAvailableValues(GenTree node)
            {
                if (node.Kind is GenTreeKind.Local or GenTreeKind.Arg or GenTreeKind.Temp)
                {
                    if (!node.SsaValueName.HasValue || !DefinitionAvailableAtPreheader(node.SsaValueName.Value))
                        return false;
                }
                else if (node.Kind is GenTreeKind.LocalAddr or GenTreeKind.ArgAddr or GenTreeKind.TempAddr)
                {
                    return false;
                }

                if (node.SsaLocalFieldBaseValue.HasValue && !DefinitionAvailableAtPreheader(node.SsaLocalFieldBaseValue.Value))
                    return false;

                for (int i = 0; i < node.Operands.Length; i++)
                {
                    if (!UsesAvailableValues(node.Operands[i]))
                        return false;
                }

                return true;
            }

            private bool DefinitionAvailableAtPreheader(SsaValueName name)
            {
                if (!_definitions.TryGetValue(name, out var definition))
                    return false;
                if (definition.IsInitial)
                    return true;
                if (definition.DefBlockId < 0 || _loop.Contains(definition.DefBlockId))
                    return false;
                if (definition.DefBlockId == _loop.Preheader)
                {
                    if (definition.IsPhi)
                        return true;
                    return definition.DefStatementIndex >= 0 && definition.DefStatementIndex < _preheaderInsertionIndex;
                }
                return _method.Cfg.Dominates(definition.DefBlockId, _loop.Preheader);
            }

            private void BuildDefinitelyExecutedSet()
            {
                _definitelyExecuted.Add(_loop.Header);

                var loops = _method.Cfg.NaturalLoops;
                for (int i = 0; i < loops.Length; i++)
                {
                    var child = loops[i];
                    if (child.Parent == _loop.Index && child.Preheader >= 0 && _loop.Contains(child.Preheader))
                        _definitelyExecuted.Add(child.Preheader);
                }

                int exitSource = -1;
                int exitEdgeCount = 0;
                for (int i = 0; i < _loop.Blocks.Length; i++)
                {
                    int blockId = _loop.Blocks[i];
                    var successors = _method.Cfg.Blocks[blockId].Successors;
                    for (int s = 0; s < successors.Length; s++)
                    {
                        var successor = successors[s];
                        if (successor.Kind == CfgEdgeKind.Exception || _loop.Contains(successor.ToBlockId))
                            continue;

                        exitEdgeCount++;
                        exitSource = blockId;
                        if (exitEdgeCount > 1)
                            return;
                    }
                }

                if (exitEdgeCount != 1)
                    return;

                int current = exitSource;
                while ((uint)current < (uint)_method.Cfg.ImmediateDominators.Length && _loop.Contains(current))
                {
                    _definitelyExecuted.Add(current);
                    if (current == _loop.Header)
                        return;

                    int parent = _method.Cfg.ImmediateDominators[current];
                    if (parent < 0 || parent == current)
                        return;
                    current = parent;
                }
            }

            private GenTreeMethod InsertHoistedExpressions()
            {
                var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(_method.GenTreeMethod.Blocks.Length);
                for (int i = 0; i < _method.GenTreeMethod.Blocks.Length; i++)
                {
                    var oldBlock = _method.GenTreeMethod.Blocks[i];
                    int extraStatements = oldBlock.Id == _loop.Preheader ? _candidates.Count : 0;
                    var statements = ImmutableArray.CreateBuilder<GenTree>(oldBlock.Statements.Length + extraStatements);
                    int insertionIndex = oldBlock.Id == _loop.Preheader
                        ? FindTerminatorStart(oldBlock.Statements)
                        : oldBlock.Statements.Length;

                    for (int s = 0; s < insertionIndex; s++)
                        statements.Add(CloneExistingTree(oldBlock.Statements[s]));

                    if (oldBlock.Id == _loop.Preheader)
                    {
                        for (int c = 0; c < _candidates.Count; c++)
                        {
                            var candidate = _candidates[c];
                            var clone = CloneTree(candidate.Source, isRoot: true);
                            candidate.CloneTreeId = clone.Id;
                            var eval = new GenTree(
                                _nextSyntheticTreeId++,
                                GenTreeKind.Eval,
                                clone.Pc,
                                BytecodeOp.Pop,
                                null,
                                GenStackKind.Void,
                                clone.Flags & ~(GenTreeFlags.AssertionProperties | GenTreeFlags.MakeCse | GenTreeFlags.ExplicitInit),
                                ImmutableArray.Create(clone));
                            statements.Add(eval);
                        }
                    }

                    for (int s = insertionIndex; s < oldBlock.Statements.Length; s++)
                        statements.Add(CloneExistingTree(oldBlock.Statements[s]));

                    blocks.Add(new GenTreeBlock(
                        oldBlock.Id,
                        oldBlock.StartPc,
                        oldBlock.EndPcExclusive,
                        oldBlock.EntryStackDepth,
                        oldBlock.ExitStackDepth,
                        oldBlock.JumpKind,
                        oldBlock.Flags,
                        statements.ToImmutable(),
                        oldBlock.SuccessorBlockIds,
                        oldBlock.SuccessorPcs,
                        oldBlock.RegionPc));
                }

                return _method.GenTreeMethod.CloneWithBlocks(blocks.ToImmutable());
            }

            private static GenTree CloneExistingTree(GenTree node)
            {
                ImmutableArray<GenTree> operands = ImmutableArray<GenTree>.Empty;
                if (!node.Operands.IsDefaultOrEmpty)
                {
                    var builder = ImmutableArray.CreateBuilder<GenTree>(node.Operands.Length);
                    for (int i = 0; i < node.Operands.Length; i++)
                        builder.Add(CloneExistingTree(node.Operands[i]));
                    operands = builder.ToImmutable();
                }

                var clone = new GenTree(
                    node.Id,
                    node.Kind,
                    node.Pc,
                    node.SourceOp,
                    node.Type,
                    node.StackKind,
                    node.Flags,
                    operands,
                    int32: node.Int32,
                    int64: node.Int64,
                    text: node.Text,
                    runtimeType: node.RuntimeType,
                    field: node.Field,
                    method: node.Method,
                    convKind: node.ConvKind,
                    convFlags: node.ConvFlags,
                    targetPc: node.TargetPc,
                    targetBlockId: node.TargetBlockId,
                    boundsCheckIndexOverride: node.BoundsCheckIndexOverride);
                clone.LocalDescriptor = node.LocalDescriptor;
                clone.CseNumber = node.CseNumber;
                return clone;
            }

            private GenTree CloneTree(GenTree node, bool isRoot)
            {
                ImmutableArray<GenTree> operands = ImmutableArray<GenTree>.Empty;
                if (!node.Operands.IsDefaultOrEmpty)
                {
                    var builder = ImmutableArray.CreateBuilder<GenTree>(node.Operands.Length);
                    for (int i = 0; i < node.Operands.Length; i++)
                        builder.Add(CloneTree(node.Operands[i], isRoot: false));
                    operands = builder.ToImmutable();
                }

                var flags = node.Flags & ~(
                    GenTreeFlags.AssertionProperties |
                    GenTreeFlags.VarDef |
                    GenTreeFlags.VarUseAsg |
                    GenTreeFlags.VarDeath |
                    GenTreeFlags.Prolog |
                    GenTreeFlags.MakeCse |
                    GenTreeFlags.ExplicitInit);
                if (isRoot)
                    flags |= GenTreeFlags.MakeCse;

                var clone = new GenTree(
                    _nextSyntheticTreeId++,
                    node.Kind,
                    node.Pc,
                    node.SourceOp,
                    node.Type,
                    node.StackKind,
                    flags,
                    operands,
                    int32: node.Int32,
                    int64: node.Int64,
                    text: node.Text,
                    runtimeType: node.RuntimeType,
                    field: node.Field,
                    method: node.Method,
                    convKind: node.ConvKind,
                    convFlags: node.ConvFlags,
                    targetPc: node.TargetPc,
                    targetBlockId: node.TargetBlockId,
                    boundsCheckIndexOverride: node.BoundsCheckIndexOverride);
                clone.LocalDescriptor = node.LocalDescriptor;
                return clone;
            }

            private bool ValidateHoists(SsaMethod rebuilt)
            {
                if (rebuilt.ValueNumbers is null)
                    return false;
                if (!TryFindLoop(rebuilt.Cfg, _loop.Header, out var rebuiltLoop))
                    return false;
                if (rebuiltLoop.Preheader != _loop.Preheader)
                    return false;

                var nodeById = new Dictionary<int, GenTree>();
                for (int b = 0; b < rebuilt.Blocks.Length; b++)
                {
                    var treeList = rebuilt.Blocks[b].TreeList;
                    for (int i = 0; i < treeList.Length; i++)
                        nodeById[treeList[i].Tree.Source.Id] = treeList[i].Tree.Source;
                }

                for (int i = 0; i < _candidates.Count; i++)
                {
                    var candidate = _candidates[i];
                    if (!nodeById.TryGetValue(candidate.Source.Id, out var source))
                        return false;
                    if (!nodeById.TryGetValue(candidate.CloneTreeId, out var clone))
                        return false;
                    if (clone.Parent is null || clone.Parent.Kind != GenTreeKind.Eval)
                        return false;
                    if ((clone.Flags & GenTreeFlags.MakeCse) == 0)
                        return false;
                    if (!rebuilt.ValueNumbers.TryGetTreeValue(source, out var sourcePair) ||
                        !rebuilt.ValueNumbers.TryGetTreeValue(clone, out var clonePair))
                    {
                        return false;
                    }

                    ValueNumber sourceNormal = rebuilt.ValueNumbers.Store.VNNormalValue(sourcePair.Liberal);
                    ValueNumber cloneNormal = rebuilt.ValueNumbers.Store.VNNormalValue(clonePair.Liberal);
                    if (!sourceNormal.IsValid || sourceNormal != cloneNormal)
                        return false;
                    if (rebuilt.ValueNumbers.Store.TryGetConstant(sourceNormal, out _))
                        return false;
                    if (!ValueNumberType.For(source.StackKind, source.Type).Equals(ValueNumberType.For(clone.StackKind, clone.Type)))
                        return false;
                    if (rebuilt.ValueNumbers.Store.VNExceptionSet(sourcePair.Conservative) !=
                        rebuilt.ValueNumbers.Store.VNExceptionSet(clonePair.Conservative))
                    {
                        return false;
                    }
                }

                return true;
            }

            private static bool SameTryRegion(CfgBlock left, CfgBlock right)
                => SequenceEqual(left.TryRegionIndexes, right.TryRegionIndexes);

            private static bool SequenceEqual(ImmutableArray<int> left, ImmutableArray<int> right)
            {
                if (left.Length != right.Length)
                    return false;
                for (int i = 0; i < left.Length; i++)
                {
                    if (left[i] != right[i])
                        return false;
                }
                return true;
            }

            private enum CandidateDisposition : byte
            {
                NotSelected,
                Selected,
                Covered,
            }
        }

        private static int FindTerminatorStart(ImmutableArray<GenTree> statements)
        {
            if (statements.IsDefaultOrEmpty)
                return 0;

            int lastIndex = statements.Length - 1;
            var last = statements[lastIndex];
            if (last.Kind == GenTreeKind.Branch && lastIndex > 0 &&
                statements[lastIndex - 1].Kind is GenTreeKind.BranchTrue or GenTreeKind.BranchFalse)
            {
                return lastIndex - 1;
            }

            return last.Kind is
                GenTreeKind.Branch or
                GenTreeKind.BranchTrue or
                GenTreeKind.BranchFalse or
                GenTreeKind.Return or
                GenTreeKind.Throw or
                GenTreeKind.Rethrow or
                GenTreeKind.EndFinally
                    ? lastIndex
                    : statements.Length;
        }

        private static SsaMethod RebuildSsa(SsaMethod previous, GenTreeMethod rewritten, bool validate)
        {
            bool includeExceptionEdges = HasExceptionEdges(previous.Cfg);
            var cfg = ControlFlowGraph.Build(rewritten, includeExceptionEdges);
            rewritten.AttachFlowGraph(cfg);
            var liveness = GenTreeLocalLiveness.Build(rewritten, cfg);
            rewritten.AttachHirLiveness(liveness);
            var rebuilt = GenTreeSsaBuilder.BuildMethod(rewritten, cfg, liveness, validate);
            return SsaValueNumbering.BuildMethod(rebuilt, validate);
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

        private static bool TryFindLoop(ControlFlowGraph cfg, int header, out CfgLoop loop)
        {
            for (int i = 0; i < cfg.NaturalLoops.Length; i++)
            {
                if (cfg.NaturalLoops[i].Header == header)
                {
                    loop = cfg.NaturalLoops[i];
                    return true;
                }
            }

            loop = default;
            return false;
        }

        private static int MaxTreeId(SsaMethod method)
        {
            int max = 0;
            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var block = method.Blocks[b];
                for (int s = 0; s < block.Statements.Length; s++)
                    Visit(block.Statements[s].Source);
            }
            return max;

            void Visit(GenTree node)
            {
                if (node.Id > max)
                    max = node.Id;
                for (int i = 0; i < node.Operands.Length; i++)
                    Visit(node.Operands[i]);
            }
        }
    }


    internal sealed class SsaLoopInvariantValueNumberAnalysis
    {
        private readonly SsaMethod _method;
        private readonly CfgLoop _loop;
        private readonly SsaValueNumberingResult _valueNumbers;
        private readonly Dictionary<ValueNumber, bool> _phiInvariant = new();
        private readonly Dictionary<ValueNumber, bool> _memoryPhiInvariant = new();
        private readonly Dictionary<ValueNumber, bool> _cache = new();
        private readonly HashSet<ValueNumber> _active = new();

        public SsaLoopInvariantValueNumberAnalysis(SsaMethod method, CfgLoop loop)
        {
            _method = method ?? throw new ArgumentNullException(nameof(method));
            _loop = loop;
            _valueNumbers = method.ValueNumbers ?? throw new ArgumentException("SSA value numbers are required.", nameof(method));

            for (int i = 0; i < method.ValueDefinitions.Length; i++)
            {
                var definition = method.ValueDefinitions[i];
                if (!definition.IsPhi || !_valueNumbers.TryGetSsaValue(definition.Name, out var value))
                    continue;

                bool invariant = !_loop.Contains(definition.DefBlockId);
                RegisterPhiInvariant(_phiInvariant, value.Liberal, invariant);
                RegisterPhiInvariant(_phiInvariant, value.Conservative, invariant);
            }

            for (int i = 0; i < method.MemoryDefinitions.Length; i++)
            {
                var definition = method.MemoryDefinitions[i];
                if (definition.IsPhi && _valueNumbers.TryGetMemoryValue(definition.Name, out var value))
                    RegisterPhiInvariant(_memoryPhiInvariant, value, !_loop.Contains(definition.DefBlockId));
            }
        }

        public bool IsInvariant(ValueNumber value)
        {
            if (!value.IsValid)
                return false;
            if (_cache.TryGetValue(value, out bool cached))
                return cached;
            if (!_active.Add(value))
                return false;

            bool invariant = false;
            if (_valueNumbers.Store.TryGetEntry(value, out var entry))
            {
                if (entry.Function == ValueNumberFunction.MemOpaque)
                {
                    invariant = entry.Args.Length == 1 &&
                        TryGetLoopIndex(entry.Args[0], out int loopIndex) &&
                        LoopIdentityIsInvariant(loopIndex);
                }
                else
                {
                    switch (entry.Kind)
                    {
                        case ValueNumberKind.Constant:
                            invariant = true;
                            break;

                        case ValueNumberKind.Function:
                            if (entry.Function == ValueNumberFunction.MapStore && entry.Args.Length == 4)
                            {
                                invariant =
                                    IsInvariant(entry.Args[0]) &&
                                    IsInvariant(entry.Args[1]) &&
                                    IsInvariant(entry.Args[2]) &&
                                    TryGetLoopIndex(entry.Args[3], out int loopIndex) &&
                                    LoopIdentityIsInvariant(loopIndex);
                            }
                            else
                            {
                                invariant = AllArgumentsInvariant(entry.Args);
                            }
                            break;

                        case ValueNumberKind.Phi:
                            invariant = _phiInvariant.TryGetValue(value, out bool phiInvariant) && phiInvariant;
                            break;

                        case ValueNumberKind.MemoryPhi:
                            invariant = _memoryPhiInvariant.TryGetValue(value, out bool memoryPhiInvariant) && memoryPhiInvariant;
                            break;
                    }
                }
            }

            _active.Remove(value);
            _cache[value] = invariant;
            return invariant;
        }

        public bool AreTreeMemoryDependenciesInvariant(GenTree node, int blockId)
        {
            var dependencies = _valueNumbers.LoopMemoryDependencies;
            for (int i = 0; i < dependencies.Length; i++)
            {
                var dependency = dependencies[i];
                if (dependency.BlockId != blockId || dependency.TreeId != node.Id)
                    continue;

                if (!IsInvariant(dependency.Memory))
                    return false;
            }

            for (int i = 0; i < node.Operands.Length; i++)
            {
                if (!AreTreeMemoryDependenciesInvariant(node.Operands[i], blockId))
                    return false;
            }

            return true;
        }

        private bool AllArgumentsInvariant(ImmutableArray<ValueNumber> arguments)
        {
            for (int i = 0; i < arguments.Length; i++)
            {
                if (!IsInvariant(arguments[i]))
                    return false;
            }
            return true;
        }

        private bool TryGetLoopIndex(ValueNumber value, out int loopIndex)
        {
            if (_valueNumbers.Store.TryGetConstant(value, out var constant) &&
                constant.Kind == ValueNumberConstantKind.Int32 &&
                constant.A >= int.MinValue &&
                constant.A <= int.MaxValue)
            {
                loopIndex = (int)constant.A;
                return true;
            }

            loopIndex = -1;
            return false;
        }

        private bool LoopIdentityIsInvariant(int loopIndex)
        {
            if (loopIndex < 0)
                return true;
            if (loopIndex == _loop.Index)
                return false;

            var loops = _method.Cfg.NaturalLoops;
            for (int i = 0; i < loops.Length; i++)
            {
                if (loops[i].Index != loopIndex)
                    continue;
                return !_loop.Contains(loops[i].Header);
            }

            return false;
        }

        private static void RegisterPhiInvariant(Dictionary<ValueNumber, bool> map, ValueNumber value, bool invariant)
        {
            if (!value.IsValid)
                return;

            if (map.TryGetValue(value, out bool existing))
                map[value] = existing && invariant;
            else
                map.Add(value, invariant);
        }
    }
}
