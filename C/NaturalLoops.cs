using System.Collections.Generic;

namespace Cnidaria.C;

internal sealed class NaturalLoop
{
    public readonly ControlFlowBlock Header;
    public readonly HashSet<ControlFlowBlock> Blocks = new();
    public ControlFlowBlock? Preheader;

    public NaturalLoop(ControlFlowBlock header)
    {
        Header = header;
        Blocks.Add(header);
    }
}

internal sealed class NaturalLoopAnalysis
{
    private readonly int[] _start;
    private readonly int[] _end;
    public readonly List<NaturalLoop> Loops = new();
    public int Remaining;

    public NaturalLoopAnalysis(ControlFlowFunction function, int budget)
    {
        Remaining = budget;
        _start = new int[function.Blocks.Length];
        _end = new int[function.Blocks.Length];
        var walk = new Stack<(ControlFlowBlock Block, bool Exit)>();
        walk.Push((function.Entry, false));
        int clock = 0;
        while (walk.Count != 0)
        {
            if (!Spend())
                return;
            var (block, exit) = walk.Pop();
            if (exit)
            {
                _end[block.Ordinal] = clock;
                continue;
            }
            _start[block.Ordinal] = ++clock;
            walk.Push((block, true));
            foreach (var child in block.DominatorChildren)
                walk.Push((child, false));
        }

        var byHeader = new Dictionary<ControlFlowBlock, NaturalLoop>();
        foreach (var block in function.ReversePostOrder)
        {
            foreach (var successor in block.UniqueSuccessors)
            {
                if (!Spend())
                    return;
                if (successor.IsExit || !Dominates(successor, block))
                    continue;
                if (!byHeader.TryGetValue(successor, out var loop))
                {
                    loop = new NaturalLoop(successor);
                    byHeader.Add(successor, loop);
                }
                loop.Blocks.Add(block);
            }
        }

        var pending = new Stack<ControlFlowBlock>();
        foreach (var loop in byHeader.Values)
        {
            foreach (var latch in loop.Blocks)
            {
                if (!ReferenceEquals(latch, loop.Header))
                    pending.Push(latch);
            }
            bool reducible = true;
            while (pending.Count != 0)
            {
                var block = pending.Pop();
                foreach (var predecessor in block.UniquePredecessors)
                {
                    if (!Spend())
                        return;
                    if (!predecessor.IsReachable)
                        continue;
                    if (!Dominates(loop.Header, predecessor))
                    {
                        reducible = false;
                        continue;
                    }
                    if (loop.Blocks.Add(predecessor))
                        pending.Push(predecessor);
                }
            }
            if (!reducible || ReferenceEquals(loop.Header, function.Entry))
                continue;
            ControlFlowBlock? entry = null;
            int entries = 0;
            foreach (var predecessor in loop.Header.UniquePredecessors)
            {
                if (!Spend())
                    return;
                if (!loop.Blocks.Contains(predecessor))
                {
                    entry = predecessor;
                    entries++;
                }
            }
            if (entries == 0)
                continue;
            if (entries == 1 && entry!.IsReachable && entry.UniqueSuccessors.Length == 1 &&
                entry.Terminator is GimpleGotoStatement)
                loop.Preheader = entry;
            Loops.Add(loop);
        }
        Loops.Sort(static (left, right) =>
        {
            int size = left.Blocks.Count.CompareTo(right.Blocks.Count);
            return size != 0 ? size : left.Header.Ordinal.CompareTo(right.Header.Ordinal);
        });
    }

    public bool Spend() => Remaining-- > 0;

    public bool Dominates(ControlFlowBlock definition, ControlFlowBlock use)
        => _start[definition.Ordinal] != 0 &&
           _start[definition.Ordinal] <= _start[use.Ordinal] &&
           _end[use.Ordinal] <= _end[definition.Ordinal];
}
