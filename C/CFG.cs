using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Cnidaria.C
{
    public enum ControlFlowEdgeKind : byte
    {
        FallThrough,
        Goto,
        ConditionalTrue,
        ConditionalFalse,
        SwitchCase,
        SwitchDefault,
        Return,
        ImplicitExit,
    }

    public enum ControlFlowProblemKind : byte
    {
        DuplicateLabel,
        MissingTarget,
        UnknownTerminator,
    }

    public sealed class ControlFlowGraph
    {
        public SemanticModel SemanticModel { get; }
        public GimpleTree GimpleTree { get; }
        public ImmutableArray<ControlFlowFunction> Functions { get; }
        public ImmutableArray<ControlFlowProblem> Problems { get; }

        private ControlFlowGraph(
            GimpleTree gimpleTree,
            ImmutableArray<ControlFlowFunction> functions)
        {
            GimpleTree = gimpleTree ?? throw new ArgumentNullException(nameof(gimpleTree));
            SemanticModel = gimpleTree.SemanticModel;
            Functions = functions.IsDefault ? ImmutableArray<ControlFlowFunction>.Empty : functions;
            Problems = Functions.SelectMany(static function => function.Problems).ToImmutableArray();
        }

        public static ControlFlowGraph Build(SemanticModel semanticModel)
        {
            if (semanticModel is null)
                throw new ArgumentNullException(nameof(semanticModel));

            return Build(semanticModel.GetGimpleTree());
        }

        public static ControlFlowGraph Build(GimpleTree gimpleTree)
        {
            if (gimpleTree is null)
                throw new ArgumentNullException(nameof(gimpleTree));

            var functions = ImmutableArray.CreateBuilder<ControlFlowFunction>();
            foreach (var member in gimpleTree.Members)
            {
                if (member is GimpleFunctionDefinition function)
                    functions.Add(ControlFlowFunction.Build(function));
            }

            return new ControlFlowGraph(gimpleTree, functions.ToImmutable());
        }
    }

    public sealed class ControlFlowFunction
    {
        private readonly Dictionary<GimpleLabel, ControlFlowBlock> _blocksByLabel;

        public GimpleFunctionDefinition Function { get; }
        public FunctionSymbol? Symbol => Function.Symbol;
        public ControlFlowBlock Entry { get; }
        public ControlFlowBlock Exit { get; }
        public ImmutableArray<ControlFlowBlock> Blocks { get; }
        public ImmutableArray<ControlFlowBlock> RealBlocks { get; }
        public ImmutableArray<ControlFlowEdge> Edges { get; }
        public ImmutableArray<ControlFlowBlock> PostOrder { get; }
        public ImmutableArray<ControlFlowBlock> ReversePostOrder { get; }
        public ImmutableArray<ControlFlowProblem> Problems { get; }

        private ControlFlowFunction(
            GimpleFunctionDefinition function,
            ControlFlowBlock entry,
            ControlFlowBlock exit,
            ImmutableArray<ControlFlowBlock> realBlocks,
            ImmutableArray<ControlFlowBlock> blocks,
            ImmutableArray<ControlFlowEdge> edges,
            ImmutableArray<ControlFlowBlock> postOrder,
            ImmutableArray<ControlFlowBlock> reversePostOrder,
            ImmutableArray<ControlFlowProblem> problems,
            Dictionary<GimpleLabel, ControlFlowBlock> blocksByLabel)
        {
            Function = function ?? throw new ArgumentNullException(nameof(function));
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
            Exit = exit ?? throw new ArgumentNullException(nameof(exit));
            RealBlocks = realBlocks.IsDefault ? ImmutableArray<ControlFlowBlock>.Empty : realBlocks;
            Blocks = blocks.IsDefault ? ImmutableArray<ControlFlowBlock>.Empty : blocks;
            Edges = edges.IsDefault ? ImmutableArray<ControlFlowEdge>.Empty : edges;
            PostOrder = postOrder.IsDefault ? ImmutableArray<ControlFlowBlock>.Empty : postOrder;
            ReversePostOrder = reversePostOrder.IsDefault ? ImmutableArray<ControlFlowBlock>.Empty : reversePostOrder;
            Problems = problems.IsDefault ? ImmutableArray<ControlFlowProblem>.Empty : problems;
            _blocksByLabel = blocksByLabel is null
                ? new Dictionary<GimpleLabel, ControlFlowBlock>()
                : new Dictionary<GimpleLabel, ControlFlowBlock>(blocksByLabel);
        }

        public static ControlFlowFunction Build(GimpleFunctionDefinition function)
        {
            if (function is null)
                throw new ArgumentNullException(nameof(function));

            return new Builder(function).Build();
        }

        public bool TryGetBlock(GimpleLabel label, out ControlFlowBlock? block)
        {
            if (label is null)
                throw new ArgumentNullException(nameof(label));

            return _blocksByLabel.TryGetValue(label, out block);
        }

        public override string ToString()
            => Symbol?.Name ?? "<anonymous-function>";

        private sealed class Builder
        {
            private readonly GimpleFunctionDefinition _function;
            private readonly List<ControlFlowBlock> _blocks = new();
            private readonly List<ControlFlowBlock> _realBlocks = new();
            private readonly List<ControlFlowEdge> _edges = new();
            private readonly List<ControlFlowProblem> _problems = new();
            private readonly Dictionary<GimpleLabel, ControlFlowBlock> _blocksByLabel = new();
            private readonly Dictionary<ControlFlowBlock, List<ControlFlowEdge>> _predecessors = new();
            private readonly Dictionary<ControlFlowBlock, List<ControlFlowEdge>> _successors = new();

            private ControlFlowBlock? _exit;

            public Builder(GimpleFunctionDefinition function)
            {
                _function = function;
            }

            public ControlFlowFunction Build()
            {
                CreateBlocks();
                CreateEdges();
                SealConnectivity();

                var entry = _realBlocks[0];
                var exit = _exit!;
                var postOrder = ComputeReachabilityAndPostOrder(entry, _blocks);
                var reversePostOrder = postOrder.Reverse().ToImmutableArray();
                ComputeDominators(entry, _blocks, reversePostOrder);

                return new ControlFlowFunction(
                    _function,
                    entry,
                    exit,
                    _realBlocks.ToImmutableArray(),
                    _blocks.ToImmutableArray(),
                    _edges.ToImmutableArray(),
                    postOrder,
                    reversePostOrder,
                    _problems.ToImmutableArray(),
                    _blocksByLabel);
            }

            private void CreateBlocks()
            {
                for (var i = 0; i < _function.Blocks.Length; i++)
                {
                    var block = new ControlFlowBlock(i, _function.Blocks[i], isExit: false);
                    _blocks.Add(block);
                    _realBlocks.Add(block);
                    _predecessors.Add(block, new List<ControlFlowEdge>());
                    _successors.Add(block, new List<ControlFlowEdge>());

                    if (_blocksByLabel.ContainsKey(block.Label!))
                    {
                        _problems.Add(new ControlFlowProblem(
                            ControlFlowProblemKind.DuplicateLabel,
                            block,
                            statement: null,
                            target: block.Label,
                            $"Duplicate GIMPLE label '{block.Label}'. The first block with this label remains the jump target."));
                    }
                    else
                    {
                        _blocksByLabel.Add(block.Label!, block);
                    }
                }

                _exit = new ControlFlowBlock(_function.Blocks.Length, gimpleBlock: null, isExit: true);
                _blocks.Add(_exit);
                _predecessors.Add(_exit, new List<ControlFlowEdge>());
                _successors.Add(_exit, new List<ControlFlowEdge>());
            }

            private void CreateEdges()
            {
                for (var i = 0; i < _realBlocks.Count; i++)
                {
                    var block = _realBlocks[i];
                    var next = i + 1 < _realBlocks.Count ? _realBlocks[i + 1] : null;
                    var terminator = block.Terminator;

                    switch (terminator)
                    {
                        case null:
                            if (next is not null)
                                AddEdge(block, next, ControlFlowEdgeKind.FallThrough, statement: null, switchValue: null);
                            else
                                AddEdge(block, _exit!, ControlFlowEdgeKind.ImplicitExit, statement: null, switchValue: null);
                            break;

                        case GimpleGotoStatement @goto:
                            AddLabelEdge(block, @goto.Target, ControlFlowEdgeKind.Goto, @goto, switchValue: null);
                            break;

                        case GimpleConditionalGotoStatement conditional:
                            AddLabelEdge(block, conditional.WhenTrue, ControlFlowEdgeKind.ConditionalTrue, conditional, switchValue: null);
                            AddLabelEdge(block, conditional.WhenFalse, ControlFlowEdgeKind.ConditionalFalse, conditional, switchValue: null);
                            break;

                        case GimpleSwitchStatement @switch:
                            foreach (var @case in @switch.Cases)
                                AddLabelEdge(block, @case.Target, ControlFlowEdgeKind.SwitchCase, @switch, @case.Value);

                            AddLabelEdge(block, @switch.DefaultLabel, ControlFlowEdgeKind.SwitchDefault, @switch, switchValue: null);
                            break;

                        case GimpleReturnStatement @return:
                            AddEdge(block, _exit!, ControlFlowEdgeKind.Return, @return, switchValue: null);
                            break;

                        default:
                            if (terminator.IsTerminator)
                            {
                                _problems.Add(new ControlFlowProblem(
                                    ControlFlowProblemKind.UnknownTerminator,
                                    block,
                                    terminator,
                                    target: null,
                                    $"Unknown GIMPLE terminator '{terminator.GetType().Name}'."));
                            }
                            else if (next is not null)
                            {
                                AddEdge(block, next, ControlFlowEdgeKind.FallThrough, statement: null, switchValue: null);
                            }
                            else
                            {
                                AddEdge(block, _exit!, ControlFlowEdgeKind.ImplicitExit, statement: null, switchValue: null);
                            }
                            break;
                    }
                }
            }

            private void AddLabelEdge(
                ControlFlowBlock source,
                GimpleLabel targetLabel,
                ControlFlowEdgeKind kind,
                GimpleStatement statement,
                GimpleConstantValue? switchValue)
            {
                if (!_blocksByLabel.TryGetValue(targetLabel, out var target))
                {
                    _problems.Add(new ControlFlowProblem(
                        ControlFlowProblemKind.MissingTarget,
                        source,
                        statement,
                        targetLabel,
                        $"Missing GIMPLE target block for label '{targetLabel}'."));
                    return;
                }

                AddEdge(source, target, kind, statement, switchValue);
            }

            private void AddEdge(
                ControlFlowBlock source,
                ControlFlowBlock target,
                ControlFlowEdgeKind kind,
                GimpleStatement? statement,
                GimpleConstantValue? switchValue)
            {
                var edge = new ControlFlowEdge(_edges.Count, source, target, kind, statement, switchValue);
                _edges.Add(edge);
                _successors[source].Add(edge);
                _predecessors[target].Add(edge);
            }

            private void SealConnectivity()
            {
                foreach (var block in _blocks)
                {
                    var predecessors = _predecessors[block].ToImmutableArray();
                    var successors = _successors[block].ToImmutableArray();
                    block.SetConnectivity(
                        predecessors,
                        successors,
                        DistinctBlocks(predecessors.Select(static edge => edge.Source)),
                        DistinctBlocks(successors.Select(static edge => edge.Target)));
                }
            }

            private static ImmutableArray<ControlFlowBlock> ComputeReachabilityAndPostOrder(
                ControlFlowBlock entry,
                IEnumerable<ControlFlowBlock> blocks)
            {
                var visited = new HashSet<ControlFlowBlock>();
                var postOrder = new List<ControlFlowBlock>();
                var stack = new Stack<SearchFrame>();

                visited.Add(entry);
                stack.Push(new SearchFrame(entry, nextSuccessorIndex: 0));

                while (stack.Count != 0)
                {
                    var frame = stack.Pop();
                    var successors = frame.Block.UniqueSuccessors;

                    if (frame.NextSuccessorIndex < successors.Length)
                    {
                        stack.Push(new SearchFrame(frame.Block, frame.NextSuccessorIndex + 1));

                        var successor = successors[frame.NextSuccessorIndex];
                        if (visited.Add(successor))
                            stack.Push(new SearchFrame(successor, nextSuccessorIndex: 0));

                        continue;
                    }

                    postOrder.Add(frame.Block);
                }

                foreach (var block in blocks)
                    block.SetReachable(visited.Contains(block));

                return postOrder.ToImmutableArray();
            }

            private static void ComputeDominators(
                ControlFlowBlock entry,
                IEnumerable<ControlFlowBlock> blocks,
                ImmutableArray<ControlFlowBlock> reversePostOrder)
            {
                var immediateDominators = new Dictionary<ControlFlowBlock, ControlFlowBlock?>();
                foreach (var block in blocks)
                    immediateDominators.Add(block, null);

                immediateDominators[entry] = entry;

                var changed = true;
                while (changed)
                {
                    changed = false;

                    for (var i = 1; i < reversePostOrder.Length; i++)
                    {
                        var block = reversePostOrder[i];
                        if (!block.IsReachable)
                            continue;

                        ControlFlowBlock? newImmediateDominator = null;
                        foreach (var predecessor in block.UniquePredecessors)
                        {
                            if (!predecessor.IsReachable || immediateDominators[predecessor] is null)
                                continue;

                            newImmediateDominator = newImmediateDominator is null
                                ? predecessor
                                : Intersect(predecessor, newImmediateDominator, immediateDominators);
                        }

                        if (!ReferenceEquals(immediateDominators[block], newImmediateDominator))
                        {
                            immediateDominators[block] = newImmediateDominator;
                            changed = true;
                        }
                    }
                }

                var dominatorChildren = new Dictionary<ControlFlowBlock, List<ControlFlowBlock>>();
                var dominanceFrontier = new Dictionary<ControlFlowBlock, HashSet<ControlFlowBlock>>();
                foreach (var block in blocks)
                {
                    dominatorChildren.Add(block, new List<ControlFlowBlock>());
                    dominanceFrontier.Add(block, new HashSet<ControlFlowBlock>());
                }

                foreach (var block in reversePostOrder)
                {
                    var immediateDominator = ReferenceEquals(block, entry)
                        ? null
                        : immediateDominators[block];

                    block.SetImmediateDominator(immediateDominator);

                    if (immediateDominator is not null)
                        dominatorChildren[immediateDominator].Add(block);
                }

                foreach (var block in reversePostOrder)
                {
                    if (block.UniquePredecessors.Length < 2)
                        continue;

                    var stop = immediateDominators[block];
                    if (stop is null)
                        continue;

                    foreach (var predecessor in block.UniquePredecessors)
                    {
                        if (!predecessor.IsReachable)
                            continue;

                        var runner = predecessor;
                        while (!ReferenceEquals(runner, stop))
                        {
                            if (!block.IsExit)
                                dominanceFrontier[runner].Add(block);

                            var next = immediateDominators[runner];
                            if (next is null || ReferenceEquals(next, runner))
                                break;

                            runner = next;
                        }
                    }
                }

                foreach (var block in blocks)
                {
                    block.SetDominatorChildren(dominatorChildren[block]
                        .OrderBy(static child => child.Ordinal)
                        .ToImmutableArray());

                    block.SetDominanceFrontier(dominanceFrontier[block]
                        .OrderBy(static frontierBlock => frontierBlock.Ordinal)
                        .ToImmutableArray());
                }
            }

            private static ControlFlowBlock Intersect(
                ControlFlowBlock first,
                ControlFlowBlock second,
                Dictionary<ControlFlowBlock, ControlFlowBlock?> immediateDominators)
            {
                var path = new HashSet<ControlFlowBlock>();
                ControlFlowBlock? firstCurrent = first;
                while (firstCurrent is not null)
                {
                    path.Add(firstCurrent);

                    var next = immediateDominators[firstCurrent];
                    if (next is null || ReferenceEquals(next, firstCurrent))
                        break;

                    firstCurrent = next;
                }

                ControlFlowBlock? secondCurrent = second;
                while (secondCurrent is not null)
                {
                    if (path.Contains(secondCurrent))
                        return secondCurrent;

                    var next = immediateDominators[secondCurrent];
                    if (next is null || ReferenceEquals(next, secondCurrent))
                        return secondCurrent;

                    secondCurrent = next;
                }

                return first;
            }

            private static ImmutableArray<ControlFlowBlock> DistinctBlocks(IEnumerable<ControlFlowBlock> blocks)
            {
                var builder = ImmutableArray.CreateBuilder<ControlFlowBlock>();
                var seen = new HashSet<ControlFlowBlock>();

                foreach (var block in blocks)
                {
                    if (seen.Add(block))
                        builder.Add(block);
                }

                return builder.ToImmutable();
            }

            private readonly struct SearchFrame
            {
                public ControlFlowBlock Block { get; }
                public int NextSuccessorIndex { get; }

                public SearchFrame(ControlFlowBlock block, int nextSuccessorIndex)
                {
                    Block = block;
                    NextSuccessorIndex = nextSuccessorIndex;
                }
            }
        }
    }

    public sealed class ControlFlowBlock
    {
        private ImmutableArray<ControlFlowEdge> _predecessors;
        private ImmutableArray<ControlFlowEdge> _successors;
        private ImmutableArray<ControlFlowBlock> _uniquePredecessors;
        private ImmutableArray<ControlFlowBlock> _uniqueSuccessors;
        private ImmutableArray<ControlFlowBlock> _dominatorChildren;
        private ImmutableArray<ControlFlowBlock> _dominanceFrontier;

        public int Ordinal { get; }
        public GimpleBasicBlock? GimpleBlock { get; }
        public bool IsExit { get; }
        public bool IsReachable { get; private set; }
        public GimpleLabel? Label => GimpleBlock?.Label;
        public ImmutableArray<GimpleStatement> Statements => GimpleBlock?.Statements ?? ImmutableArray<GimpleStatement>.Empty;
        public GimpleStatement? Terminator => Statements.Length != 0 && Statements[^1].IsTerminator ? Statements[^1] : null;
        public ImmutableArray<ControlFlowEdge> Predecessors => _predecessors;
        public ImmutableArray<ControlFlowEdge> Successors => _successors;
        public ImmutableArray<ControlFlowBlock> UniquePredecessors => _uniquePredecessors;
        public ImmutableArray<ControlFlowBlock> UniqueSuccessors => _uniqueSuccessors;
        public ControlFlowBlock? ImmediateDominator { get; private set; }
        public ImmutableArray<ControlFlowBlock> DominatorChildren => _dominatorChildren;
        public ImmutableArray<ControlFlowBlock> DominanceFrontier => _dominanceFrontier;

        internal ControlFlowBlock(int ordinal, GimpleBasicBlock? gimpleBlock, bool isExit)
        {
            if (ordinal < 0)
                throw new ArgumentOutOfRangeException(nameof(ordinal));

            Ordinal = ordinal;
            GimpleBlock = gimpleBlock;
            IsExit = isExit;
            _predecessors = ImmutableArray<ControlFlowEdge>.Empty;
            _successors = ImmutableArray<ControlFlowEdge>.Empty;
            _uniquePredecessors = ImmutableArray<ControlFlowBlock>.Empty;
            _uniqueSuccessors = ImmutableArray<ControlFlowBlock>.Empty;
            _dominatorChildren = ImmutableArray<ControlFlowBlock>.Empty;
            _dominanceFrontier = ImmutableArray<ControlFlowBlock>.Empty;
        }

        public bool Dominates(ControlFlowBlock other)
        {
            if (other is null)
                throw new ArgumentNullException(nameof(other));

            ControlFlowBlock? current = other;
            while (current is not null)
            {
                if (ReferenceEquals(current, this))
                    return true;

                current = current.ImmediateDominator;
            }

            return false;
        }

        public override string ToString()
        {
            if (IsExit)
                return "<exit>";

            return Label?.Name ?? $"block_{Ordinal.ToString(CultureInfo.InvariantCulture)}";
        }

        internal void SetConnectivity(
            ImmutableArray<ControlFlowEdge> predecessors,
            ImmutableArray<ControlFlowEdge> successors,
            ImmutableArray<ControlFlowBlock> uniquePredecessors,
            ImmutableArray<ControlFlowBlock> uniqueSuccessors)
        {
            _predecessors = predecessors.IsDefault ? ImmutableArray<ControlFlowEdge>.Empty : predecessors;
            _successors = successors.IsDefault ? ImmutableArray<ControlFlowEdge>.Empty : successors;
            _uniquePredecessors = uniquePredecessors.IsDefault ? ImmutableArray<ControlFlowBlock>.Empty : uniquePredecessors;
            _uniqueSuccessors = uniqueSuccessors.IsDefault ? ImmutableArray<ControlFlowBlock>.Empty : uniqueSuccessors;
        }

        internal void SetReachable(bool isReachable)
            => IsReachable = isReachable;

        internal void SetImmediateDominator(ControlFlowBlock? immediateDominator)
            => ImmediateDominator = immediateDominator;

        internal void SetDominatorChildren(ImmutableArray<ControlFlowBlock> children)
            => _dominatorChildren = children.IsDefault ? ImmutableArray<ControlFlowBlock>.Empty : children;

        internal void SetDominanceFrontier(ImmutableArray<ControlFlowBlock> frontier)
            => _dominanceFrontier = frontier.IsDefault ? ImmutableArray<ControlFlowBlock>.Empty : frontier;
    }

    public sealed class ControlFlowEdge
    {
        public int Ordinal { get; }
        public ControlFlowBlock Source { get; }
        public ControlFlowBlock Target { get; }
        public ControlFlowEdgeKind Kind { get; }
        public GimpleStatement? Statement { get; }
        public GimpleConstantValue? SwitchValue { get; }

        internal ControlFlowEdge(
            int ordinal,
            ControlFlowBlock source,
            ControlFlowBlock target,
            ControlFlowEdgeKind kind,
            GimpleStatement? statement,
            GimpleConstantValue? switchValue)
        {
            if (ordinal < 0)
                throw new ArgumentOutOfRangeException(nameof(ordinal));

            Ordinal = ordinal;
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Kind = kind;
            Statement = statement;
            SwitchValue = switchValue;
        }

        public override string ToString()
        {
            return SwitchValue is null
                ? $"{Source} -> {Target} ({Kind})"
                : $"{Source} -> {Target} ({Kind}: {SwitchValue})";
        }
    }

    public sealed class ControlFlowProblem
    {
        public ControlFlowProblemKind Kind { get; }
        public ControlFlowBlock? Block { get; }
        public GimpleStatement? Statement { get; }
        public GimpleLabel? Target { get; }
        public string Message { get; }

        internal ControlFlowProblem(
            ControlFlowProblemKind kind,
            ControlFlowBlock? block,
            GimpleStatement? statement,
            GimpleLabel? target,
            string message)
        {
            Kind = kind;
            Block = block;
            Statement = statement;
            Target = target;
            Message = message ?? string.Empty;
        }

        public override string ToString() => Message;
    }

    public static class ControlFlowExtensions
    {
        public static ControlFlowGraph GetControlFlowGraph(this SemanticModel semanticModel)
            => ControlFlowGraph.Build(semanticModel);

        public static ControlFlowGraph GetControlFlowGraph(this GimpleTree gimpleTree)
            => ControlFlowGraph.Build(gimpleTree);

        public static ControlFlowFunction GetControlFlowGraph(this GimpleFunctionDefinition function)
            => ControlFlowFunction.Build(function);
    }
}
