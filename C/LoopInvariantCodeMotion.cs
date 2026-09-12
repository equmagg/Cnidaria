using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.C
{
    internal static class LoopInvariantCodeMotion
    {
        private sealed class Loop
        {
            public readonly ControlFlowBlock Header;
            public readonly HashSet<ControlFlowBlock> Blocks = new();
            public ControlFlowBlock? Preheader;

            public Loop(ControlFlowBlock header)
            {
                Header = header;
                Blocks.Add(header);
            }
        }

        private sealed class Analysis
        {
            private readonly int[] _start;
            private readonly int[] _end;
            public readonly List<Loop> Loops = new();
            public int Remaining;

            public Analysis(ControlFlowFunction function, int budget)
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

                var byHeader = new Dictionary<ControlFlowBlock, Loop>();
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
                            loop = new Loop(successor);
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

        private static bool Enabled(SsaOptimizationOptions options)
            => options.EnableLoopInvariantCodeMotion && options.MaxLoopHoistsPerLoop > 0 && options.MaxLoopAnalysisWork > 0;

        public static ControlFlowGraph CreatePreheaders(ControlFlowGraph graph, SsaOptimizationOptions options)
        {
            if (!Enabled(options))
                return graph;
            var replacements = new Dictionary<GimpleFunctionDefinition, GimpleFunctionDefinition>();
            var functions = ImmutableArray.CreateBuilder<ControlFlowFunction>(graph.Functions.Length);
            foreach (var flow in graph.Functions)
            {
                var rewritten = CreatePreheaders(flow, options.MaxLoopAnalysisWork);
                functions.Add(ReferenceEquals(rewritten, flow.Function) ? flow : ControlFlowFunction.Build(rewritten));
                replacements.Add(flow.Function, rewritten);
            }
            bool changed = false;
            var members = ImmutableArray.CreateBuilder<GimpleNode>(graph.GimpleTree.Members.Length);
            foreach (var member in graph.GimpleTree.Members)
            {
                if (member is GimpleFunctionDefinition function)
                {
                    var replacement = replacements[function];
                    changed |= !ReferenceEquals(function, replacement);
                    members.Add(replacement);
                }
                else
                    members.Add(member);
            }
            if (!changed)
                return graph;
            var tree = graph.GimpleTree;
            return graph.WithTrimmedMembers(
                new GimpleTree(tree.SemanticModel, members.ToImmutable(), tree.Diagnostics, tree.HasInliningApplied),
                functions.ToImmutable());
        }

        private static GimpleFunctionDefinition CreatePreheaders(ControlFlowFunction flow, int budget)
        {
            var analysis = new Analysis(flow, budget);
            if (analysis.Remaining <= 0 || flow.Problems.Length != 0)
                return flow.Function;
            var preheaders = new Dictionary<GimpleLabel, (Loop Loop, GimpleLabel Label)>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var block in flow.RealBlocks)
                names.Add(block.Label!.Name);
            foreach (var loop in analysis.Loops)
            {
                if (loop.Preheader is not null)
                    continue;
                string name = loop.Header.Label!.Name + "_licm";
                while (!names.Add(name))
                    name += "_";
                preheaders.Add(loop.Header.Label, (loop, new GimpleLabel(name)));
            }
            if (preheaders.Count == 0)
                return flow.Function;

            // Split entries before SSA construction so scalar and memory phis are built once.
            var blocks = ImmutableArray.CreateBuilder<GimpleBasicBlock>();
            foreach (var block in flow.RealBlocks)
            {
                if (preheaders.TryGetValue(block.Label!, out var preheader))
                    blocks.Add(new GimpleBasicBlock(preheader.Label,
                        ImmutableArray.Create<GimpleStatement>(new GimpleGotoStatement(block.Label!))));
                var statements = block.Statements;
                if (statements.Length != 0)
                {
                    var terminator = statements[^1];
                    GimpleStatement replacement = terminator switch
                    {
                        GimpleGotoStatement jump => new GimpleGotoStatement(Redirect(jump.Target), jump.Syntax),
                        GimpleCondStatement branch => new GimpleCondStatement(branch.Code, branch.Lhs, branch.Rhs,
                            Redirect(branch.WhenTrue), Redirect(branch.WhenFalse), branch.Syntax),
                        GimpleSwitchStatement dispatch => RewriteSwitch(dispatch),
                        _ => terminator,
                    };
                    statements = statements.SetItem(statements.Length - 1, replacement);
                }
                blocks.Add(new GimpleBasicBlock(block.Label!, statements));

                GimpleLabel Redirect(GimpleLabel target)
                    => preheaders.TryGetValue(target, out var destination) && !destination.Loop.Blocks.Contains(block)
                        ? destination.Label : target;

                GimpleSwitchStatement RewriteSwitch(GimpleSwitchStatement dispatch)
                {
                    var cases = ImmutableArray.CreateBuilder<GimpleSwitchCase>(dispatch.Cases.Length);
                    foreach (var item in dispatch.Cases)
                        cases.Add(new GimpleSwitchCase(item.Value, Redirect(item.Target)));
                    return new GimpleSwitchStatement(dispatch.Expression, cases.ToImmutable(), Redirect(dispatch.DefaultLabel), dispatch.Syntax);
                }
            }
            var function = flow.Function;
            return new GimpleFunctionDefinition(function.Syntax, function.Symbol, function.Temporaries,
                blocks.ToImmutable(), function.EntryLabel, function.HasScalarReplacementApplied);
        }

        public static GimpleFunctionAnnotations Optimize(GimpleFunctionAnnotations function, TargetInfo target,
            SsaOptimizationOptions options, ValueNumberingOptions valueNumberingOptions)
        {
            if (!Enabled(options) || function.Problems.Length != 0)
                return function;
            var analysis = new Analysis(function.ControlFlowFunction, options.MaxLoopAnalysisWork);
            if (analysis.Remaining <= 0 || analysis.Loops.Count == 0)
                return function;
            return new Hoister(function, target, options, valueNumberingOptions, analysis).Run();
        }

        private sealed class Hoister
        {
            private const int MinimumHoistCost = 4;

            private readonly GimpleFunctionAnnotations _function;
            private readonly SsaOptimizationOptions _options;
            private readonly ValueNumberingOptions _valueNumberingOptions;
            private readonly Analysis _analysis;
            private readonly int _registers;
            private readonly Dictionary<ControlFlowBlock, List<GimpleStatementAnnotations>> _statements = new();
            private readonly Dictionary<GimpleName, ControlFlowBlock?> _locations = new();
            private readonly Dictionary<GimpleName, GimpleStatementAnnotations> _instructions = new();
            private readonly Dictionary<GimpleStatement, ControlFlowBlock> _instructionBlocks = new();
            private bool _changed;

            public Hoister(GimpleFunctionAnnotations function, TargetInfo target, SsaOptimizationOptions options,
                ValueNumberingOptions valueNumberingOptions, Analysis analysis)
            {
                _function = function;
                _options = options;
                _valueNumberingOptions = valueNumberingOptions;
                _analysis = analysis;
                _registers = TargetRegisterInfo.AllocatableGeneralRegisters(target).Length;
                foreach (var block in function.Blocks)
                {
                    _statements.Add(block.ControlFlowBlock, new List<GimpleStatementAnnotations>(block.Statements));
                    foreach (var instruction in block.Statements)
                    {
                        _instructionBlocks[instruction.Statement] = block.ControlFlowBlock;
                        if (instruction.Definitions.Length == 1)
                            _instructions[instruction.Definitions[0].Name] = instruction;
                    }
                }
                foreach (var definition in function.Definitions)
                    _locations.Add(definition.Name, definition.Block);
            }

            public GimpleFunctionAnnotations Run()
            {
                foreach (var loop in _analysis.Loops)
                {
                    if (_analysis.Remaining <= 0)
                        break;
                    if (loop.Preheader is not null && _statements.ContainsKey(loop.Preheader))
                        Hoist(loop);
                }
                return _changed ? Rebuild() : _function;
            }

            private void Hoist(Loop loop)
            {
                bool writesMemory = false;
                bool containsCall = false;
                var live = new HashSet<GimpleName>();
                var useCounts = new Dictionary<GimpleName, int>();
                foreach (var block in loop.Blocks)
                {
                    if (!_analysis.Spend() || !_statements.TryGetValue(block, out var statements))
                        return;
                    if (_function.TryGetBlock(block, out var annotations) && annotations is not null)
                    {
                        foreach (var phi in annotations.Phis)
                        {
                            if (!_analysis.Spend())
                                return;
                            if (IsScalar(phi.Result.Type))
                                live.Add(phi.Result);
                            foreach (var operand in phi.Operands)
                            {
                                if (!_analysis.Spend())
                                    return;
                                if (loop.Blocks.Contains(operand.Predecessor))
                                    CountUse(operand.Value, useCounts);
                            }
                        }
                    }
                    foreach (var instruction in statements)
                    {
                        if (!_analysis.Spend())
                            return;
                        writesMemory |= (instruction.Flags & GimpleStatementFlags.WritesMemory) != 0;
                        containsCall |= (instruction.Flags & GimpleStatementFlags.ContainsCall) != 0;
                        foreach (var use in instruction.Uses)
                        {
                            if (!_analysis.Spend())
                                return;
                            CountUse(use.Name, useCounts);
                            if (IsScalar(use.Name.Type) && _locations.TryGetValue(use.Name, out var definition) &&
                                (definition is null || !loop.Blocks.Contains(definition)))
                                live.Add(use.Name);
                        }
                    }
                }

                int hoists = 0;
                int livePressure = RegisterPressure(live);
                int temporaryPressure = EstimateTemporaryPressure(loop, live);
                var moved = new HashSet<GimpleStatementAnnotations>();
                var destination = _statements[loop.Preheader!];
                try
                {
                    foreach (var block in _function.ControlFlowFunction.ReversePostOrder)
                    {
                        if (!_analysis.Spend())
                            return;
                        if (!loop.Blocks.Contains(block))
                            continue;
                        bool guaranteed = ReferenceEquals(block, loop.Header);
                        foreach (var instruction in _statements[block])
                        {
                            if (_analysis.Remaining <= 0 || hoists >= _options.MaxLoopHoistsPerLoop)
                                return;
                            if (moved.Contains(instruction))
                                continue;
                            bool movable = Classify(instruction, out bool speculative, out _);
                            if (movable && (speculative || guaranteed) &&
                                TrySelectPlan(instruction, loop, writesMemory, _options.MaxLoopHoistsPerLoop - hoists,
                                    out var plan, out int cost) &&
                                IsProfitable(plan, cost, loop, live, useCounts, livePressure, temporaryPressure, containsCall,
                                    out var released, out var carried))
                            {
                                // Commit the dependency group only after its total cost and pressure pass.
                                foreach (var candidate in plan)
                                {
                                    int insertion = destination.Count > 0 && destination[^1].Statement.IsTerminator
                                        ? destination.Count - 1 : destination.Count;
                                    destination.Insert(insertion, candidate);
                                    _locations[candidate.Definitions[0].Name] = loop.Preheader;
                                    _instructionBlocks[candidate.Statement] = loop.Preheader!;
                                    moved.Add(candidate);
                                    foreach (var use in candidate.Uses)
                                    {
                                        if (useCounts.TryGetValue(use.Name, out int count))
                                            useCounts[use.Name] = count - 1;
                                    }
                                }
                                live.ExceptWith(released);
                                live.UnionWith(carried);
                                livePressure += RegisterPressure(carried) - RegisterPressure(released);
                                hoists += plan.Count;
                                _changed = true;
                            }
                            else if (!movable || !speculative)
                            {
                                // Retained effects and traps fence later non-speculative moves.
                                guaranteed = false;
                            }
                        }
                    }
                }
                finally
                {
                    if (moved.Count != 0)
                    {
                        foreach (var block in loop.Blocks)
                            _statements[block].RemoveAll(moved.Contains);
                    }
                }
            }

            private static void CountUse(GimpleName name, Dictionary<GimpleName, int> counts)
            {
                if (!IsScalar(name.Type))
                    return;
                counts.TryGetValue(name, out int count);
                counts[name] = count + 1;
            }

            private ControlFlowBlock UseBlock(GimpleUse use)
                => use.Edge?.Source ?? (use.Statement is not null && _instructionBlocks.TryGetValue(use.Statement, out var block)
                    ? block : use.Block);

            private int RegisterWeight(GimpleName name)
                => Math.Max(1, (_function.Target.SizeOf(name.Type) + _function.Target.RegisterSize - 1) /
                    _function.Target.RegisterSize);

            private int RegisterPressure(HashSet<GimpleName> names)
            {
                int pressure = 0;
                foreach (var name in names)
                    pressure += RegisterWeight(name);
                return pressure;
            }

            private int EstimateTemporaryPressure(Loop loop, HashSet<GimpleName> live)
            {
                int peak = 0;
                foreach (var block in loop.Blocks)
                {
                    var active = new HashSet<GimpleName>();
                    int pressure = 0;
                    var statements = _statements[block];
                    foreach (var instruction in statements)
                    {
                        foreach (var definition in instruction.Definitions)
                        {
                            if (!IsScalar(definition.Name.Type) || live.Contains(definition.Name))
                                continue;
                            foreach (var use in _function.GetImmediateUses(definition.Name))
                            {
                                if (!_analysis.Spend())
                                    return _registers;
                                if (!ReferenceEquals(UseBlock(use), block))
                                {
                                    if (active.Add(definition.Name))
                                        pressure += RegisterWeight(definition.Name);
                                    break;
                                }
                            }
                        }
                    }
                    peak = Math.Max(peak, pressure);
                    for (int i = statements.Count - 1; i >= 0; i--)
                    {
                        if (!_analysis.Spend())
                            return _registers;
                        foreach (var definition in statements[i].Definitions)
                        {
                            if (active.Remove(definition.Name))
                                pressure -= RegisterWeight(definition.Name);
                        }
                        foreach (var use in statements[i].Uses)
                        {
                            if (!_analysis.Spend())
                                return _registers;
                            if (IsScalar(use.Name.Type) && !live.Contains(use.Name) && active.Add(use.Name))
                                pressure += RegisterWeight(use.Name);
                        }
                        peak = Math.Max(peak, pressure);
                    }
                }
                return peak;
            }

            private bool TrySelectPlan(GimpleStatementAnnotations root, Loop loop, bool writesMemory, int limit,
                out List<GimpleStatementAnnotations> plan, out int cost)
            {
                plan = new List<GimpleStatementAnnotations>();
                if (!TryBuildPlan(root, loop, writesMemory, limit, plan, out cost))
                    return false;
                for (int depth = 0; depth < Math.Min(64, limit) && _analysis.Spend(); depth++)
                {
                    if (!_function.TryGetSingleUse(root.Definitions[0].Name, out var use) || use is null ||
                        !loop.Blocks.Contains(UseBlock(use)) ||
                        use.Statement is not GimpleAssignStatement { Lhs: GimpleName name } ||
                        !_instructions.TryGetValue(name, out var consumer))
                        break;
                    var extended = new List<GimpleStatementAnnotations>();
                    if (!Classify(consumer, out bool speculative, out _) || !speculative ||
                        !TryBuildPlan(consumer, loop, writesMemory, limit, extended, out int extendedCost))
                        break;
                    plan = extended;
                    cost = extendedCost;
                    root = consumer;
                }
                return true;
            }

            private bool TryBuildPlan(GimpleStatementAnnotations root, Loop loop, bool writesMemory, int limit,
                List<GimpleStatementAnnotations> plan, out int cost)
            {
                int totalCost = 0;
                var planned = new HashSet<GimpleName>();
                var visiting = new HashSet<GimpleName>();
                bool success = Collect(root, 0);
                cost = totalCost;
                return success;

                bool Collect(GimpleStatementAnnotations instruction, int depth)
                {
                    if (!_analysis.Spend() || depth >= Math.Min(64, limit) ||
                        !Classify(instruction, out bool speculative, out int instructionCost) ||
                        depth != 0 && !speculative ||
                        (instruction.Flags & GimpleStatementFlags.ReadsMemory) != 0 &&
                            (writesMemory || instruction.MemoryInput is null))
                        return false;
                    var name = instruction.Definitions[0].Name;
                    if (planned.Contains(name))
                        return true;
                    if (!visiting.Add(name))
                        return false;
                    foreach (var use in instruction.Uses)
                    {
                        if (!_analysis.Spend() || use.Name.IsUndefined || !_locations.TryGetValue(use.Name, out var definition))
                            return false;
                        if (definition is null || _analysis.Dominates(definition, loop.Preheader!))
                            continue;
                        if (!loop.Blocks.Contains(definition) || !_instructions.TryGetValue(use.Name, out var dependency) ||
                            !Collect(dependency, depth + 1))
                            return false;
                    }
                    if (plan.Count >= limit)
                        return false;
                    visiting.Remove(name);
                    planned.Add(name);
                    plan.Add(instruction);
                    totalCost += instructionCost;
                    return true;
                }
            }

            private bool IsProfitable(List<GimpleStatementAnnotations> plan, int cost, Loop loop,
                HashSet<GimpleName> live, Dictionary<GimpleName, int> useCounts, int livePressure, int temporaryPressure, bool containsCall,
                out HashSet<GimpleName> released, out HashSet<GimpleName> carried)
            {
                released = new HashSet<GimpleName>();
                carried = new HashSet<GimpleName>();
                var root = plan[^1];
                if (_function.HasZeroUses(root.Definitions[0].Name))
                    return false;
                var removedUses = new Dictionary<GimpleName, int>();
                foreach (var instruction in plan)
                {
                    foreach (var use in instruction.Uses)
                    {
                        if (!_analysis.Spend())
                            return false;
                        CountUse(use.Name, removedUses);
                    }
                }
                foreach (var pair in removedUses)
                {
                    if (live.Contains(pair.Key) && useCounts.TryGetValue(pair.Key, out int count) && count == pair.Value &&
                        _locations.TryGetValue(pair.Key, out var definition) &&
                        (definition is null || _analysis.Dominates(definition, loop.Preheader!)) &&
                        !UsedBeyondLoop(pair.Key, loop))
                        released.Add(pair.Key);
                }
                foreach (var instruction in plan)
                {
                    var name = instruction.Definitions[0].Name;
                    removedUses.TryGetValue(name, out int removed);
                    if (useCounts.TryGetValue(name, out int count) && count > removed || UsedBeyondLoop(name, loop))
                        carried.Add(name);
                }
                if (_analysis.Remaining <= 0)
                    return false;
                int growth = RegisterPressure(carried) - RegisterPressure(released);
                if (cost < MinimumHoistCost)
                    return (root.Flags & GimpleStatementFlags.ReadsMemory) != 0 && growth <= 0;
                return growth <= 0 || !containsCall &&
                    livePressure + temporaryPressure + growth <= Math.Max(1, _registers - 2);
            }

            private bool UsedBeyondLoop(GimpleName name, Loop loop)
            {
                foreach (var use in _function.GetImmediateUses(name))
                {
                    if (!_analysis.Spend())
                        return true;
                    var block = UseBlock(use);
                    if (use.Kind == GimpleUseKind.Phi && loop.Blocks.Contains(block) && !loop.Blocks.Contains(use.Block))
                        return true;
                    if (!loop.Blocks.Contains(block) && !_analysis.Dominates(block, loop.Preheader!))
                        return true;
                }
                return false;
            }

            private bool Classify(GimpleStatementAnnotations instruction, out bool speculative, out int cost)
            {
                speculative = false;
                cost = 0;
                if (instruction.Statement is not GimpleAssignStatement assign || instruction.Definitions.Length != 1 ||
                    assign.Lhs is not GimpleName result || !IsScalar(result.Type) || IsOrdered(result.Type) ||
                    IsFixed(result) || instruction.MemoryOutput is not null ||
                    (instruction.Flags & (GimpleStatementFlags.WritesMemory | GimpleStatementFlags.ContainsCall)) != 0)
                    return false;
                if (!ClassifyCode(assign.Subcode, out speculative))
                    return false;
                foreach (var operand in instruction.Operands)
                {
                    if (!ClassifyOperand(operand, ref speculative))
                        return false;
                }
                bool load = (instruction.Flags & GimpleStatementFlags.ReadsMemory) != 0;
                speculative &= !load;
                cost = load ? 1 : assign.Subcode switch
                {
                    GimpleTreeCode.TruncDivExpr or GimpleTreeCode.TruncModExpr or GimpleTreeCode.ExactDivExpr => 12,
                    GimpleTreeCode.MultExpr => 4,
                    GimpleTreeCode.SsaName or GimpleTreeCode.IntegerCst => 0,
                    _ => 1,
                };
                return true;
            }

            private bool ClassifyOperand(GimpleOperandInfo operand, ref bool speculative)
            {
                if (!_analysis.Spend() || operand.WritesMemory || operand.ContainsCall ||
                    !operand.IsAddress && (!IsScalar(operand.Original.Type) || IsOrdered(operand.Original.Type)) ||
                    operand.Name is not null && (operand.Name.IsUndefined || IsFixed(operand.Name)))
                    return false;
                if (operand.Name is null && operand.Original is not GimpleConstantValue)
                {
                    if (!ClassifyCode(GimpleOperators.CodeOf(operand.Original), out bool safe))
                        return false;
                    if (!operand.IsAddress)
                        speculative &= safe;
                }
                speculative &= !operand.ReadsMemory;
                foreach (var child in operand.Children)
                {
                    if (!ClassifyOperand(child, ref speculative))
                        return false;
                }
                return true;
            }

            private static bool ClassifyCode(GimpleTreeCode code, out bool speculative)
            {
                speculative = true;
                switch (code)
                {
                    case GimpleTreeCode.MemRef:
                    case GimpleTreeCode.ArrayRef:
                    case GimpleTreeCode.ComponentRef:
                    case GimpleTreeCode.TruncDivExpr:
                    case GimpleTreeCode.TruncModExpr:
                    case GimpleTreeCode.ExactDivExpr:
                        speculative = false;
                        return true;
                    case GimpleTreeCode.SsaName:
                    case GimpleTreeCode.VarDecl:
                    case GimpleTreeCode.ParmDecl:
                    case GimpleTreeCode.FunctionDecl:
                    case GimpleTreeCode.IntegerCst:
                    case GimpleTreeCode.StringCst:
                    case GimpleTreeCode.AddrExpr:
                    case GimpleTreeCode.NegateExpr:
                    case GimpleTreeCode.BitNotExpr:
                    case GimpleTreeCode.AbsExpr:
                    case GimpleTreeCode.TruthNotExpr:
                    case GimpleTreeCode.NopExpr:
                    case GimpleTreeCode.ConvertExpr:
                    case GimpleTreeCode.PlusExpr:
                    case GimpleTreeCode.MinusExpr:
                    case GimpleTreeCode.MultExpr:
                    case GimpleTreeCode.PointerPlusExpr:
                    case GimpleTreeCode.PointerDiffExpr:
                    case GimpleTreeCode.BitAndExpr:
                    case GimpleTreeCode.BitIorExpr:
                    case GimpleTreeCode.BitXorExpr:
                    case GimpleTreeCode.LshiftExpr:
                    case GimpleTreeCode.RshiftExpr:
                    case GimpleTreeCode.LrotateExpr:
                    case GimpleTreeCode.RrotateExpr:
                    case GimpleTreeCode.MinExpr:
                    case GimpleTreeCode.MaxExpr:
                    case GimpleTreeCode.TruthAndExpr:
                    case GimpleTreeCode.TruthOrExpr:
                    case GimpleTreeCode.TruthXorExpr:
                    case GimpleTreeCode.LtExpr:
                    case GimpleTreeCode.LeExpr:
                    case GimpleTreeCode.GtExpr:
                    case GimpleTreeCode.GeExpr:
                    case GimpleTreeCode.EqExpr:
                    case GimpleTreeCode.NeExpr:
                    case GimpleTreeCode.CondExpr:
                        return true;
                    default:
                        return false;
                }
            }

            private static bool IsFixed(GimpleName name)
                => name.Variable.Symbol is VariableSymbol { ExplicitRegisterName: not null };

            private static bool IsOrdered(QualifiedType type)
                => (type.Qualifiers & (TypeQualifiers.Volatile | TypeQualifiers.Atomic)) != 0;

            private static bool IsScalar(QualifiedType type)
                => type.Type.Kind is TypeKind.Pointer or TypeKind.Enum || type.Type is BuiltinType
                {
                    BuiltinKind: BuiltinTypeKind.Bool or BuiltinTypeKind.Char or BuiltinTypeKind.SignedChar or
                        BuiltinTypeKind.UnsignedChar or BuiltinTypeKind.Short or BuiltinTypeKind.UnsignedShort or
                        BuiltinTypeKind.Int or BuiltinTypeKind.UnsignedInt or BuiltinTypeKind.Long or
                        BuiltinTypeKind.UnsignedLong or BuiltinTypeKind.LongLong or BuiltinTypeKind.UnsignedLongLong
                };

            private GimpleFunctionAnnotations Rebuild()
            {
                var blocks = ImmutableArray.CreateBuilder<GimpleBlockAnnotations>(_function.Blocks.Length);
                var uses = ImmutableArray.CreateBuilder<GimpleUse>();
                var replacements = new Dictionary<GimpleName, GimpleDefinition>();
                foreach (var block in _function.Blocks)
                {
                    var statements = ImmutableArray.CreateBuilder<GimpleStatementAnnotations>();
                    foreach (var instruction in _statements[block.ControlFlowBlock])
                    {
                        var rewritten = instruction;
                        if (!ReferenceEquals(instruction.Block, block.ControlFlowBlock))
                        {
                            var definition = instruction.Definitions[0];
                            var moved = new GimpleDefinition(definition.Name, definition.Kind, block.ControlFlowBlock,
                                definition.Statement, definition.Target, definition.Parameter);
                            replacements[definition.Name] = moved;
                            var movedUses = ImmutableArray.CreateBuilder<GimpleUse>(instruction.Uses.Length);
                            foreach (var use in instruction.Uses)
                                movedUses.Add(new GimpleUse(use.Name, use.Kind, block.ControlFlowBlock,
                                    use.Statement, use.Value, use.Edge));
                            rewritten = new GimpleStatementAnnotations(instruction.Ordinal, block.ControlFlowBlock,
                                instruction.Statement, instruction.InputStatement, instruction.Operands, movedUses.ToImmutable(),
                                ImmutableArray.Create(moved), instruction.MemoryInput, instruction.MemoryOutput, instruction.Flags);
                        }
                        statements.Add(rewritten);
                        uses.AddRange(rewritten.Uses);
                    }
                    blocks.Add(new GimpleBlockAnnotations(block.ControlFlowBlock, block.Phis, statements.ToImmutable()));
                }
                var definitions = ImmutableArray.CreateBuilder<GimpleDefinition>(_function.Definitions.Length);
                foreach (var definition in _function.Definitions)
                    definitions.Add(replacements.TryGetValue(definition.Name, out var replacement) ? replacement : definition);
                var undefined = new Dictionary<GimpleVariable, GimpleName>();
                foreach (var variable in _function.Variables)
                    undefined.Add(variable, _function.GetUndefinedName(variable));
                return new GimpleFunctionAnnotations(_function.ControlFlowFunction, _function.MemoryVariable,
                    _function.Variables, blocks.ToImmutable(), definitions.ToImmutable(), uses.ToImmutable(),
                    _function.Problems, undefined, _valueNumberingOptions, _function.Target);
            }
        }
    }
}
