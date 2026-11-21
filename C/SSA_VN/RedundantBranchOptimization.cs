using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.C;

internal static class RedundantBranchOptimization
{
    private const int MaxDominatingCompares = 128;
    private const int MaxDominatingMatches = 4;
    private const int MaxJoinJumps = 4;

    public static GimpleFunctionAnnotations Optimize(GimpleFunctionAnnotations function, TargetInfo target, SsaOptimizationOptions options,
        ValueNumberingOptions valueNumberingOptions)
    {
        if (!options.EnableBranchFolding || function.Problems.Length != 0)
            return function;

        var optimizer = new Optimizer(function, target, valueNumberingOptions);
        if (optimizer.HasStaleEdges())
            function = optimizer.Rebuild();

        for (var round = 0; round < options.MaxIterations; round++)
        {
            optimizer = new Optimizer(function, target, valueNumberingOptions);
            if (!optimizer.Run())
                break;
            function = optimizer.Rebuild();
        }

        return function;
    }

    private sealed class Optimizer
    {
        private readonly GimpleFunctionAnnotations _function;
        private readonly ValueNumberingOptions _valueNumberingOptions;
        private readonly ValueRangeTable _constants;
        private readonly Dictionary<ControlFlowBlock, GimpleBlockAnnotations> _blocks = new();
        private readonly Dictionary<ControlFlowBlock, GimpleStatementAnnotations> _terminators = new();
        private readonly Dictionary<GimplePhi, Dictionary<ControlFlowBlock, GimpleName>> _addedInputs = new();
        private readonly HashSet<ControlFlowBlock> _gainedPredecessors = new();
        private readonly HashSet<ControlFlowBlock> _bypassed = new();

        public Optimizer(GimpleFunctionAnnotations function, TargetInfo target, ValueNumberingOptions valueNumberingOptions)
        {
            _function = function;
            _valueNumberingOptions = valueNumberingOptions;
            _constants = new ValueRangeTable(target, assertions: null);
            foreach (GimpleBlockAnnotations block in function.Blocks)
                _blocks[block.ControlFlowBlock] = block;
        }

        public bool Run()
        {
            foreach (ControlFlowBlock block in DominatorPostOrder())
            {
                if (TryGetBranch(block, out GimpleCondStatement branch) && !TryResolveFromDominators(block, branch))
                    ThreadThroughPhis(block, branch);
            }

            return _terminators.Count != 0;
        }

        public bool HasStaleEdges()
        {
            foreach (GimpleBlockAnnotations block in _function.Blocks)
            {
                if (!block.IsReachable || block.Statements.Length == 0 || block.Statements[^1].Statement is not GimpleGotoStatement jump)
                    continue;
                ImmutableArray<ControlFlowBlock> successors = block.ControlFlowBlock.UniqueSuccessors;
                if (successors.Length != 1 || !Targets(jump.Target, successors[0]))
                    return true;
            }

            return false;
        }

        private List<ControlFlowBlock> DominatorPostOrder()
        {
            var order = new List<ControlFlowBlock>();
            var pending = new Stack<(ControlFlowBlock Block, bool Visited)>();
            pending.Push((_function.ControlFlowFunction.Entry, false));
            while (pending.Count != 0)
            {
                (ControlFlowBlock block, bool visited) = pending.Pop();
                if (visited)
                {
                    order.Add(block);
                    continue;
                }

                pending.Push((block, true));
                foreach (ControlFlowBlock child in block.DominatorChildren)
                    pending.Push((child, false));
            }

            return order;
        }

        private bool TryResolveFromDominators(ControlFlowBlock block, GimpleCondStatement branch)
        {
            var compares = 0;
            var matches = 0;
            for (ControlFlowBlock? dominator = block.ImmediateDominator; dominator is not null; dominator = dominator.ImmediateDominator)
            {
                if (_terminators.ContainsKey(dominator) || _bypassed.Contains(dominator) || !TryGetBranch(dominator, out GimpleCondStatement dominating))
                    continue;
                if (++compares > MaxDominatingCompares)
                    return false;

                bool? whenTrue = Implied(dominating, onTrue: true, branch);
                bool? whenFalse = Implied(dominating, onTrue: false, branch);
                if (whenTrue is null && whenFalse is null)
                    continue;
                if (++matches > MaxDominatingMatches || !TryGetSuccessor(dominating.WhenTrue, out ControlFlowBlock trueSuccessor) ||
                    !TryGetSuccessor(dominating.WhenFalse, out ControlFlowBlock falseSuccessor))
                    return false;

                bool trueReaches = Reaches(trueSuccessor, block, dominator);
                bool falseReaches = Reaches(falseSuccessor, block, dominator);
                if (!trueReaches && !falseReaches)
                    return false;
                if (trueReaches && falseReaches)
                {
                    if (whenTrue is bool takenOnTrue && whenFalse is bool takenOnFalse &&
                        ThreadThroughDominator(block, branch, dominator, trueSuccessor, falseSuccessor, takenOnTrue, takenOnFalse))
                        return true;
                }
                else if ((trueReaches ? whenTrue : whenFalse) is bool truth && CurrentTerminator(block) is GimpleStatementAnnotations test)
                {
                    Replace(block, test, new GimpleGotoStatement(truth ? branch.WhenTrue : branch.WhenFalse, branch.Syntax));
                    return true;
                }
            }

            return false;
        }

        private static bool? Implied(GimpleCondStatement dominating, bool onTrue, GimpleCondStatement branch)
        {
            GimpleTreeCode code = onTrue ? dominating.Code : ScalarEvolution.Invert(dominating.Code);
            var scope = new AssertionScope(new Assertion(dominating.Lhs, code, dominating.Rhs), null);
            return AssertionTable.Implication(scope, branch.Lhs, branch.Code, branch.Rhs);
        }

        private bool ThreadThroughDominator(ControlFlowBlock block, GimpleCondStatement branch, ControlFlowBlock dominator,
            ControlFlowBlock trueSuccessor, ControlFlowBlock falseSuccessor, bool takenOnTrue, bool takenOnFalse)
        {
            // A branch between them would need its side of the flow duplicated
            for (ControlFlowBlock? between = block.ImmediateDominator; !ReferenceEquals(between, dominator); between = between.ImmediateDominator)
            {
                if (between is null || CurrentTerminator(between)?.Statement is GimpleCondStatement)
                    return false;
            }

            List<ControlFlowBlock> chain = ChainOf(block);
            if (!IsBareBranch(block, branch, chain))
                return false;

            ControlFlowBlock join = chain[^1];
            var threaded = false;
            foreach (ControlFlowBlock predecessor in join.UniquePredecessors)
            {
                if (!predecessor.IsReachable)
                    continue;
                bool isTrue = ReferenceEquals(predecessor, dominator) ? ReferenceEquals(trueSuccessor, join) : Reaches(trueSuccessor, predecessor, dominator);
                bool isFalse = ReferenceEquals(predecessor, dominator) ? ReferenceEquals(falseSuccessor, join) : Reaches(falseSuccessor, predecessor, dominator);
                if (isTrue != isFalse)
                    threaded |= Redirect(predecessor, join, block, (isTrue ? takenOnTrue : takenOnFalse) ? branch.WhenTrue : branch.WhenFalse);
            }

            if (threaded)
                _bypassed.Add(block);
            return threaded;
        }

        private void ThreadThroughPhis(ControlFlowBlock block, GimpleCondStatement branch)
        {
            List<ControlFlowBlock> chain = ChainOf(block);
            ControlFlowBlock join = chain[^1];
            if (_blocks[join].Phis.Length == 0 || !IsBareBranch(block, branch, chain))
                return;

            foreach (ControlFlowBlock predecessor in join.UniquePredecessors)
            {
                if (predecessor.IsReachable && !ReferenceEquals(predecessor, join) && TryDecide(chain, branch, predecessor, out bool taken) &&
                    Redirect(predecessor, join, block, taken ? branch.WhenTrue : branch.WhenFalse))
                    _bypassed.Add(block);
            }
        }

        // Inlining leaves the phis a branch tests a few jumps above it, each block on the way entered from the one before
        private List<ControlFlowBlock> ChainOf(ControlFlowBlock block)
        {
            var chain = new List<ControlFlowBlock> { block };
            ControlFlowBlock join = block;
            while (chain.Count <= MaxJoinJumps && join.Predecessors.Length == 1)
            {
                ControlFlowBlock predecessor = join.Predecessors[0].Source;
                if (!predecessor.IsReachable || _terminators.ContainsKey(predecessor) || _gainedPredecessors.Contains(predecessor) ||
                    !_blocks.TryGetValue(predecessor, out GimpleBlockAnnotations? jump) || !IsBare(jump) ||
                    jump.Statements[^1].Statement is not GimpleGotoStatement)
                    break;
                join = predecessor;
                chain.Add(join);
            }

            return chain;
        }

        // Flow that bypasses the chain skips its phis, they may feed nothing but the branch and the phis below them
        private bool IsBareBranch(ControlFlowBlock block, GimpleCondStatement branch, List<ControlFlowBlock> chain)
        {
            ControlFlowBlock join = chain[^1];
            if (_terminators.ContainsKey(block) || _gainedPredecessors.Contains(block) || !IsBare(_blocks[block]) || IsLoopHeader(join))
                return false;

            foreach (ControlFlowBlock link in chain)
            {
                foreach (GimplePhi phi in _blocks[link].Phis)
                {
                    foreach (GimpleUse use in _function.GetImmediateUses(phi.Result))
                    {
                        if (use.Kind == GimpleUseKind.Phi
                                ? ReferenceEquals(use.Block, join) || !chain.Contains(use.Block)
                                : !ReferenceEquals(use.Statement, branch))
                            return false;
                    }
                }
            }

            return true;
        }

        private static bool IsBare(GimpleBlockAnnotations block)
        {
            if (block.Statements.Length == 0)
                return false;
            for (var i = 0; i < block.Statements.Length - 1; i++)
            {
                if (block.Statements[i].Statement is not GimpleNopStatement && !DeclaresRegister(block.Statements[i].Statement))
                    return false;
            }

            return true;
        }

        // A variable kept in registers takes no code to declare, while a stack slot is laid out from its declaration
        private static bool DeclaresRegister(GimpleStatement statement)
            => statement is GimpleDeclarationStatement declaration && declaration.Symbol is VariableSymbol variable && !variable.IsAddressTaken &&
               declaration.StorageClass is StorageClass.None or StorageClass.Auto or StorageClass.Register &&
               !GimpleTypes.IsAggregate(variable.Type) && !GimpleTypes.IsVolatileOrAtomic(variable.Type);

        private static bool IsLoopHeader(ControlFlowBlock block)
        {
            foreach (ControlFlowBlock predecessor in block.UniquePredecessors)
            {
                if (predecessor.IsReachable && block.Dominates(predecessor))
                    return true;
            }

            return false;
        }

        private bool TryDecide(List<ControlFlowBlock> chain, GimpleCondStatement branch, ControlFlowBlock predecessor, out bool taken)
        {
            taken = false;
            if (!TryGetConstant(Incoming(chain, branch.Lhs, predecessor), out GimpleConstantValue left) ||
                !TryGetConstant(Incoming(chain, branch.Rhs, predecessor), out GimpleConstantValue right) ||
                _constants.Evaluate(null, left, branch.Code, right) is not bool truth)
                return false;

            taken = truth;
            return true;
        }

        private GimpleValue Incoming(List<ControlFlowBlock> chain, GimpleValue value, ControlFlowBlock predecessor)
        {
            for (var i = 0; i < chain.Count; i++)
            {
                ControlFlowBlock from = i + 1 < chain.Count ? chain[i + 1] : predecessor;
                foreach (GimplePhi phi in _blocks[chain[i]].Phis)
                {
                    if (!ReferenceEquals(phi.Result, value))
                        continue;
                    foreach (GimplePhiOperand operand in phi.Operands)
                    {
                        if (ReferenceEquals(operand.Predecessor, from))
                            value = operand.Value;
                    }
                }
            }

            return value;
        }

        private bool TryGetConstant(GimpleValue value, out GimpleConstantValue constant)
        {
            switch (value)
            {
                case GimpleConstantValue known:
                    constant = known;
                    return true;
                case GimpleName name when _function.ValueNumbering.TryGetConstantValue(name, out object? number):
                    constant = new GimpleConstantValue(number, name.Type);
                    return true;
                default:
                    constant = null!;
                    return false;
            }
        }

        private bool Redirect(ControlFlowBlock predecessor, ControlFlowBlock join, ControlFlowBlock branching, GimpleLabel label)
        {
            if (!TryGetSuccessor(label, out ControlFlowBlock target) || ReferenceEquals(target, join) ||
                CurrentTerminator(predecessor) is not GimpleStatementAnnotations current)
                return false;

            GimpleStatement? redirected = current.Statement switch
            {
                GimpleGotoStatement jump when Targets(jump.Target, join) => new GimpleGotoStatement(label, jump.Syntax),
                GimpleCondStatement condition when Targets(condition.WhenTrue, join) || Targets(condition.WhenFalse, join) =>
                    new GimpleCondStatement(condition.Code, condition.Lhs, condition.Rhs,
                        Targets(condition.WhenTrue, join) ? label : condition.WhenTrue,
                        Targets(condition.WhenFalse, join) ? label : condition.WhenFalse, condition.Syntax),
                _ => null,
            };
            if (redirected is null)
                return false;

            // A second edge from the same block could not give the phis a value of its own
            ImmutableArray<GimplePhi> phis = _blocks[target].Phis;
            if (phis.Length != 0 && current.Statement is GimpleCondStatement other && (Targets(other.WhenTrue, target) || Targets(other.WhenFalse, target)))
                return false;

            foreach (GimplePhi phi in phis)
            {
                if (!_addedInputs.TryGetValue(phi, out Dictionary<ControlFlowBlock, GimpleName>? inputs))
                    _addedInputs.Add(phi, inputs = new Dictionary<ControlFlowBlock, GimpleName>());
                inputs[predecessor] = Input(phi, branching);
            }

            Replace(predecessor, current, redirected);
            _gainedPredecessors.Add(target);
            _bypassed.Add(join);
            return true;
        }

        private void Replace(ControlFlowBlock block, GimpleStatementAnnotations current, GimpleStatement statement)
        {
            ImmutableArray<GimpleOperandInfo> operands = statement is GimpleCondStatement ? current.Operands : ImmutableArray<GimpleOperandInfo>.Empty;
            _terminators[block] = new GimpleStatementAnnotations(current.Ordinal, block, statement, current.InputStatement, operands,
                SsaRewriting.CollectUses(operands, block, statement), ImmutableArray<GimpleDefinition>.Empty, null, null, GimpleStatementFlags.None);
        }

        private GimpleStatementAnnotations? CurrentTerminator(ControlFlowBlock block)
        {
            if (_terminators.TryGetValue(block, out GimpleStatementAnnotations? replaced))
                return replaced;

            return _blocks.TryGetValue(block, out GimpleBlockAnnotations? annotations) && annotations.Statements.Length != 0 &&
                   annotations.Statements[^1].Statement.IsTerminator
                ? annotations.Statements[^1]
                : null;
        }

        private bool TryGetBranch(ControlFlowBlock block, out GimpleCondStatement branch)
        {
            branch = null!;
            if (CurrentTerminator(block)?.Statement is not GimpleCondStatement condition ||
                condition.Code is not (GimpleTreeCode.LtExpr or GimpleTreeCode.LeExpr or GimpleTreeCode.GtExpr or GimpleTreeCode.GeExpr or
                    GimpleTreeCode.EqExpr or GimpleTreeCode.NeExpr) ||
                !AssertionTable.IsTracked(condition.Lhs.Type) || !AssertionTable.IsTracked(condition.Rhs.Type))
                return false;

            branch = condition;
            return true;
        }

        private bool TryGetSuccessor(GimpleLabel label, out ControlFlowBlock successor)
        {
            successor = null!;
            if (!_function.ControlFlowFunction.TryGetBlock(label, out ControlFlowBlock? block) || block is null)
                return false;

            successor = block;
            return true;
        }

        private bool Targets(GimpleLabel label, ControlFlowBlock block)
            => _function.ControlFlowFunction.TryGetBlock(label, out ControlFlowBlock? target) && ReferenceEquals(target, block);

        private static bool Reaches(ControlFlowBlock from, ControlFlowBlock to, ControlFlowBlock excluded)
        {
            if (ReferenceEquals(from, to))
                return true;

            var visited = new HashSet<ControlFlowBlock> { from };
            var pending = new Stack<ControlFlowBlock>();
            pending.Push(from);
            while (pending.Count != 0)
            {
                ControlFlowBlock current = pending.Pop();
                if (ReferenceEquals(current, excluded))
                    continue;
                foreach (ControlFlowBlock successor in current.UniqueSuccessors)
                {
                    if (ReferenceEquals(successor, to))
                        return true;
                    if (visited.Add(successor))
                        pending.Push(successor);
                }
            }

            return false;
        }

        private GimpleName Input(GimplePhi phi, ControlFlowBlock predecessor)
        {
            if (_addedInputs.TryGetValue(phi, out Dictionary<ControlFlowBlock, GimpleName>? inputs) && inputs.TryGetValue(predecessor, out GimpleName? added))
                return added;

            foreach (GimplePhiOperand operand in phi.Operands)
            {
                if (ReferenceEquals(operand.Predecessor, predecessor))
                    return operand.Value;
            }

            throw new InvalidOperationException($"Phi '{phi.Result}' has no input from '{predecessor}'.");
        }

        public GimpleFunctionAnnotations Rebuild()
        {
            ControlFlowFunction previous = _function.ControlFlowFunction;
            var basicBlocks = ImmutableArray.CreateBuilder<GimpleBasicBlock>(previous.RealBlocks.Length);
            foreach (ControlFlowBlock block in previous.RealBlocks)
                basicBlocks.Add(Synchronize(block));
            ControlFlowFunction flow = previous.Rebuild(basicBlocks.MoveToImmutable());

            var redefinitions = new Dictionary<GimpleName, GimpleDefinition?>();
            var blocks = ImmutableArray.CreateBuilder<GimpleBlockAnnotations>(_function.Blocks.Length);
            var uses = ImmutableArray.CreateBuilder<GimpleUse>();
            foreach (GimpleBlockAnnotations block in _function.Blocks)
            {
                ControlFlowBlock moved = flow.Blocks[block.ControlFlowBlock.Ordinal];
                if (!moved.IsReachable)
                {
                    foreach (GimplePhi phi in block.Phis)
                        redefinitions[phi.Result] = null;
                    foreach (GimpleStatementAnnotations instruction in block.Statements)
                    {
                        foreach (GimpleDefinition definition in instruction.Definitions)
                            redefinitions[definition.Name] = null;
                    }

                    blocks.Add(new GimpleBlockAnnotations(moved, ImmutableArray<GimplePhi>.Empty, ImmutableArray<GimpleStatementAnnotations>.Empty));
                    continue;
                }

                var phis = ImmutableArray.CreateBuilder<GimplePhi>(block.Phis.Length);
                foreach (GimplePhi phi in block.Phis)
                {
                    var operands = ImmutableArray.CreateBuilder<GimplePhiOperand>(moved.Predecessors.Length);
                    foreach (ControlFlowEdge edge in moved.Predecessors)
                    {
                        if (edge.Source.IsReachable)
                            operands.Add(new GimplePhiOperand(edge, Input(phi, previous.Blocks[edge.Source.Ordinal])));
                    }

                    redefinitions[phi.Result] = new GimpleDefinition(phi.Result, GimpleDefinitionKind.Phi, moved, statement: null, phi.Result, parameter: null);
                    phis.Add(new GimplePhi(phi.Ordinal, moved, phi.Variable, phi.Result, operands.ToImmutable()));
                }

                var statements = ImmutableArray.CreateBuilder<GimpleStatementAnnotations>(block.Statements.Length);
                for (var i = 0; i < block.Statements.Length; i++)
                {
                    GimpleStatementAnnotations instruction = i == block.Statements.Length - 1 &&
                        _terminators.TryGetValue(block.ControlFlowBlock, out GimpleStatementAnnotations? replaced)
                        ? replaced
                        : block.Statements[i];
                    GimpleStatementAnnotations relocated = Relocate(instruction, moved, redefinitions);
                    statements.Add(relocated);
                    uses.AddRange(relocated.Uses);
                }

                blocks.Add(new GimpleBlockAnnotations(moved, phis.MoveToImmutable(), statements.MoveToImmutable()));
            }

            var definitions = ImmutableArray.CreateBuilder<GimpleDefinition>(_function.Definitions.Length);
            foreach (GimpleDefinition definition in _function.Definitions)
            {
                if (redefinitions.TryGetValue(definition.Name, out GimpleDefinition? redefined))
                {
                    if (redefined is not null)
                        definitions.Add(redefined);
                }
                else
                {
                    definitions.Add(definition.Block is null
                        ? definition
                        : new GimpleDefinition(definition.Name, definition.Kind, flow.Blocks[definition.Block.Ordinal], definition.Statement,
                            definition.Target, definition.Parameter));
                }
            }

            var undefined = new Dictionary<GimpleVariable, GimpleName>();
            foreach (GimpleVariable variable in _function.Variables)
                undefined.Add(variable, _function.GetUndefinedName(variable));

            return new GimpleFunctionAnnotations(flow, _function.MemoryVariable, _function.Variables, blocks.MoveToImmutable(),
                definitions.ToImmutable(), uses.ToImmutable(), _function.Problems, undefined, _valueNumberingOptions, _function.Target);
        }

        // The edges follow the terminators the optimizer left, the statements stay those the function was built from
        private GimpleBasicBlock Synchronize(ControlFlowBlock block)
        {
            GimpleBasicBlock basic = block.GimpleBlock!;
            if (!block.IsReachable || !basic.HasTerminator || CurrentTerminator(block)?.Statement is not GimpleStatement current)
                return basic;

            GimpleStatement original = basic.Statements[^1];
            GimpleStatement? synchronized = (current, original) switch
            {
                (GimpleGotoStatement jump, GimpleGotoStatement before) when ReferenceEquals(jump.Target, before.Target) => null,
                (GimpleGotoStatement jump, _) => new GimpleGotoStatement(jump.Target, original.Syntax),
                (GimpleCondStatement branch, GimpleCondStatement before)
                    when !ReferenceEquals(branch.WhenTrue, before.WhenTrue) || !ReferenceEquals(branch.WhenFalse, before.WhenFalse) =>
                    new GimpleCondStatement(before.Code, before.Lhs, before.Rhs, branch.WhenTrue, branch.WhenFalse, before.Syntax),
                _ => null,
            };

            return synchronized is null
                ? basic
                : new GimpleBasicBlock(basic.Label, basic.Statements.SetItem(basic.Statements.Length - 1, synchronized));
        }

        private static GimpleStatementAnnotations Relocate(GimpleStatementAnnotations instruction, ControlFlowBlock block,
            Dictionary<GimpleName, GimpleDefinition?> redefinitions)
        {
            var definitions = ImmutableArray.CreateBuilder<GimpleDefinition>(instruction.Definitions.Length);
            foreach (GimpleDefinition definition in instruction.Definitions)
            {
                var redefined = new GimpleDefinition(definition.Name, definition.Kind, block, definition.Statement, definition.Target, definition.Parameter);
                redefinitions[definition.Name] = redefined;
                definitions.Add(redefined);
            }

            var uses = ImmutableArray.CreateBuilder<GimpleUse>(instruction.Uses.Length);
            foreach (GimpleUse use in instruction.Uses)
                uses.Add(new GimpleUse(use.Name, use.Kind, block, use.Statement, use.Value));

            return new GimpleStatementAnnotations(instruction.Ordinal, block, instruction.Statement, instruction.InputStatement, instruction.Operands,
                uses.MoveToImmutable(), definitions.MoveToImmutable(), instruction.MemoryInput, instruction.MemoryOutput, instruction.Flags);
        }
    }
}
