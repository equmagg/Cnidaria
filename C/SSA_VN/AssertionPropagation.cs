using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.C;

internal readonly struct Assertion
{
    public Assertion(GimpleValue left, GimpleTreeCode code, GimpleValue right)
    {
        Left = left;
        Code = code;
        Right = right;
    }

    public GimpleValue Left { get; }
    public GimpleTreeCode Code { get; }
    public GimpleValue Right { get; }
}

internal sealed class AssertionScope
{
    public AssertionScope(Assertion assertion, AssertionScope? parent)
    {
        Assertion = assertion;
        Parent = parent;
    }

    public Assertion Assertion { get; }
    public AssertionScope? Parent { get; }
}

/// <summary>Holds the relations known to be true on entry to each block</summary>
internal sealed class AssertionTable
{
    private const int MaxScopeWalk = 64;

    private readonly Dictionary<ControlFlowBlock, AssertionScope?> _scopes = new();

    internal void Record(ControlFlowBlock block, AssertionScope? scope) => _scopes[block] = scope;

    public AssertionScope? ScopeOf(ControlFlowBlock block) => _scopes.TryGetValue(block, out AssertionScope? scope) ? scope : null;

    public static bool? Implication(AssertionScope? scope, GimpleValue left, GimpleTreeCode code, GimpleValue right)
    {
        var steps = 0;
        for (var current = scope; current is not null && steps++ < MaxScopeWalk; current = current.Parent)
        {
            if (Implied(current.Assertion, left, code, right) is bool truth)
                return truth;
        }

        return null;
    }

    public static AssertionScope? Enter(AssertionScope? scope, GimpleCondStatement condition, bool onTrue)
    {
        foreach (Assertion assertion in Assertions(condition))
        {
            GimpleTreeCode code = onTrue ? assertion.Code : ScalarEvolution.Invert(assertion.Code);
            scope = new AssertionScope(new Assertion(assertion.Left, code, assertion.Right), scope);
        }
        return scope;
    }

    // A test of a flag against zero stands for the comparison that computed the flag
    public static Assertion? Describe(GimpleCondStatement condition)
    {
        if (!IsComparison(condition.Code) || !IsTracked(condition.Lhs.Type) || !IsTracked(condition.Rhs.Type))
            return null;
        if (condition.Code is GimpleTreeCode.NeExpr or GimpleTreeCode.EqExpr &&
            condition.Lhs is GimpleName flag && condition.Rhs is GimpleConstantValue zero && Same(zero, new GimpleConstantValue(0, zero.Type)) &&
            flag.Definition?.Statement is GimpleAssignStatement comparison && comparison.Operands.Length == 2 && IsComparison(comparison.Subcode) &&
            IsTracked(comparison.Operands[0].Type) && IsTracked(comparison.Operands[1].Type))
        {
            GimpleTreeCode code = condition.Code == GimpleTreeCode.NeExpr ? comparison.Subcode : ScalarEvolution.Invert(comparison.Subcode);
            return new Assertion(comparison.Operands[0], code, comparison.Operands[1]);
        }
        return new Assertion(condition.Lhs, condition.Code, condition.Rhs);
    }

    public static bool IsTracked(QualifiedType type)
        => type.Type.Kind == TypeKind.Pointer || GimpleTypes.IsIntegerLike(type);

    public static GimpleTreeCode Swap(GimpleTreeCode code) => code switch
    {
        GimpleTreeCode.LtExpr => GimpleTreeCode.GtExpr,
        GimpleTreeCode.LeExpr => GimpleTreeCode.GeExpr,
        GimpleTreeCode.GtExpr => GimpleTreeCode.LtExpr,
        GimpleTreeCode.GeExpr => GimpleTreeCode.LeExpr,
        _ => code,
    };

    public static bool Same(GimpleValue left, GimpleValue right)
        => ReferenceEquals(left, right) ||
           left is GimpleConstantValue a && right is GimpleConstantValue b &&
           TryGetConstant(a, out var x) && TryGetConstant(b, out var y) && x == y;

    internal static bool TryGetConstant(GimpleConstantValue constant, out Int128 value)
    {
        value = 0;
        if (constant.Value is null)
            return true;
        if (!ScalarEvolution.TryGetInteger(constant.Value, out var number))
            return false;
        value = constant.Value is ulong unsignedNumber ? unsignedNumber : number;
        return true;
    }

    private static IEnumerable<Assertion> Assertions(GimpleCondStatement condition)
    {
        if (!IsComparison(condition.Code) || !IsTracked(condition.Lhs.Type) || !IsTracked(condition.Rhs.Type))
            yield break;
        yield return new Assertion(condition.Lhs, condition.Code, condition.Rhs);
        if (Describe(condition) is Assertion described && !ReferenceEquals(described.Left, condition.Lhs))
            yield return described;
    }

    private static bool IsComparison(GimpleTreeCode code)
        => code is GimpleTreeCode.LtExpr or GimpleTreeCode.LeExpr or GimpleTreeCode.GtExpr or GimpleTreeCode.GeExpr
            or GimpleTreeCode.EqExpr or GimpleTreeCode.NeExpr;

    private static bool? Implied(Assertion known, GimpleValue left, GimpleTreeCode code, GimpleValue right)
    {
        if (Same(known.Left, left) && Same(known.Right, right))
            return Implies(known.Code, code);
        if (Same(known.Left, right) && Same(known.Right, left))
            return Implies(Swap(known.Code), code);
        return null;
    }

    private static bool? Implies(GimpleTreeCode known, GimpleTreeCode query)
    {
        if (known == query)
            return true;
        return (known, query) switch
        {
            (GimpleTreeCode.LtExpr, GimpleTreeCode.LeExpr or GimpleTreeCode.NeExpr) => true,
            (GimpleTreeCode.LtExpr, GimpleTreeCode.GtExpr or GimpleTreeCode.GeExpr or GimpleTreeCode.EqExpr) => false,
            (GimpleTreeCode.GtExpr, GimpleTreeCode.GeExpr or GimpleTreeCode.NeExpr) => true,
            (GimpleTreeCode.GtExpr, GimpleTreeCode.LtExpr or GimpleTreeCode.LeExpr or GimpleTreeCode.EqExpr) => false,
            (GimpleTreeCode.LeExpr, GimpleTreeCode.GtExpr) => false,
            (GimpleTreeCode.GeExpr, GimpleTreeCode.LtExpr) => false,
            (GimpleTreeCode.EqExpr, GimpleTreeCode.LeExpr or GimpleTreeCode.GeExpr) => true,
            (GimpleTreeCode.EqExpr, GimpleTreeCode.NeExpr or GimpleTreeCode.LtExpr or GimpleTreeCode.GtExpr) => false,
            (GimpleTreeCode.NeExpr, GimpleTreeCode.EqExpr) => false,
            _ => null,
        };
    }
}

internal static class AssertionPropagation
{
    public static GimpleFunctionAnnotations Optimize(GimpleFunctionAnnotations function, SsaOptimizationOptions options,
        ValueNumberingOptions valueNumberingOptions, out AssertionTable? assertions)
    {
        assertions = null;
        if (!options.EnableAssertionPropagation || function.Problems.Length != 0)
            return function;
        return new Propagator(function, valueNumberingOptions, options.MaxLoopAnalysisWork).Run(out assertions);
    }

    private sealed class Propagator
    {
        private readonly GimpleFunctionAnnotations _function;
        private readonly ValueNumberingOptions _valueNumberingOptions;
        private readonly Dictionary<ControlFlowBlock, GimpleBlockAnnotations> _blocks = new();
        private readonly Dictionary<GimpleStatementAnnotations, GimpleStatementAnnotations> _replacements = new();
        private readonly Dictionary<GimpleDefinition, GimpleDefinition> _redefinitions = new();
        private readonly AssertionTable _table = new();
        private int _budget;

        public Propagator(GimpleFunctionAnnotations function, ValueNumberingOptions valueNumberingOptions, int budget)
        {
            _function = function;
            _valueNumberingOptions = valueNumberingOptions;
            _budget = budget;
            foreach (var block in function.Blocks)
                _blocks[block.ControlFlowBlock] = block;
        }

        public GimpleFunctionAnnotations Run(out AssertionTable assertions)
        {
            assertions = _table;
            var entry = _function.ControlFlowFunction.Entry;
            var pending = new Stack<(ControlFlowBlock Block, AssertionScope? Scope)>();
            pending.Push((entry, null));
            while (pending.Count != 0 && _budget-- > 0)
            {
                var (block, scope) = pending.Pop();
                _table.Record(block, scope);
                if (!_blocks.TryGetValue(block, out var annotations))
                    continue;

                PropagateConstants(annotations, scope);
                var test = annotations.Statements.Length != 0 ? annotations.Statements[^1] : null;
                var condition = test?.Statement as GimpleCondStatement;
                if (condition is not null && AssertionTable.Describe(condition) is Assertion described &&
                    AssertionTable.Implication(scope, described.Left, described.Code, described.Right) is bool truth)
                {
                    var target = truth ? condition.WhenTrue : condition.WhenFalse;
                    var jump = new GimpleGotoStatement(target, condition.Syntax);
                    _replacements[test!] = new GimpleStatementAnnotations(test!.Ordinal, test.Block, jump, test.InputStatement,
                        ImmutableArray<GimpleOperandInfo>.Empty, ImmutableArray<GimpleUse>.Empty, ImmutableArray<GimpleDefinition>.Empty,
                        null, null, GimpleStatementFlags.None);
                }

                foreach (var child in block.DominatorChildren)
                    pending.Push((child, Enter(block, child, condition, scope)));
            }

            return _replacements.Count == 0 ? _function : Rebuild();
        }

        private AssertionScope? Enter(ControlFlowBlock from, ControlFlowBlock to, GimpleCondStatement? condition, AssertionScope? scope)
        {
            if (condition is null || to.Predecessors.Length != 1 || !ReferenceEquals(to.Predecessors[0].Source, from))
                return scope;
            var onTrue = Targets(condition.WhenTrue, to);
            if (onTrue == Targets(condition.WhenFalse, to))
                return scope;
            return AssertionTable.Enter(scope, condition, onTrue);
        }

        private bool Targets(GimpleLabel label, ControlFlowBlock block)
            => _function.ControlFlowFunction.TryGetBlock(label, out var target) && ReferenceEquals(target, block);

        private void PropagateConstants(GimpleBlockAnnotations block, AssertionScope? scope)
        {
            Dictionary<GimpleName, GimpleConstantValue>? constants = null;
            for (var current = scope; current is not null; current = current.Parent)
            {
                var known = current.Assertion;
                if (known.Code != GimpleTreeCode.EqExpr)
                    continue;
                if (known.Left is GimpleName name && known.Right is GimpleConstantValue constant && GimpleTypes.IsIntegerLike(name.Type))
                    (constants ??= new()).TryAdd(name, constant);
                else if (known.Right is GimpleName swapped && known.Left is GimpleConstantValue swappedConstant && GimpleTypes.IsIntegerLike(swapped.Type))
                    (constants ??= new()).TryAdd(swapped, swappedConstant);
            }
            if (constants is null)
                return;

            foreach (var instruction in block.Statements)
            {
                if (instruction.Statement is GimpleAsmStatement or GimpleDeclarationStatement)
                    continue;
                var touches = false;
                foreach (var use in instruction.Uses)
                    touches |= use.Kind == GimpleUseKind.Value && constants.ContainsKey(use.Name);
                if (!touches)
                    continue;

                var current = _replacements.TryGetValue(instruction, out var replaced) ? replaced : instruction;
                _replacements[instruction] = SsaRewriting.Rewrite(current, node =>
                    node.Name is { } name && !node.IsAddress && constants.TryGetValue(name, out var constant)
                        ? new GimpleOperandInfo(new GimpleConstantValue(constant.Value, name.Type, constant.Syntax), null,
                            ImmutableArray<GimpleOperandInfo>.Empty, false, false, false)
                        : null, _redefinitions);
            }
        }

        private GimpleFunctionAnnotations Rebuild()
        {
            var blocks = ImmutableArray.CreateBuilder<GimpleBlockAnnotations>(_function.Blocks.Length);
            var uses = ImmutableArray.CreateBuilder<GimpleUse>();
            foreach (var block in _function.Blocks)
            {
                var statements = ImmutableArray.CreateBuilder<GimpleStatementAnnotations>(block.Statements.Length);
                foreach (var instruction in block.Statements)
                {
                    var rewritten = _replacements.TryGetValue(instruction, out var replacement) ? replacement : instruction;
                    statements.Add(rewritten);
                    uses.AddRange(rewritten.Uses);
                }
                blocks.Add(new GimpleBlockAnnotations(block.ControlFlowBlock, block.Phis, statements.MoveToImmutable()));
            }

            var definitions = ImmutableArray.CreateBuilder<GimpleDefinition>(_function.Definitions.Length);
            foreach (var definition in _function.Definitions)
                definitions.Add(_redefinitions.TryGetValue(definition, out var redefined) ? redefined : definition);

            var undefined = new Dictionary<GimpleVariable, GimpleName>();
            foreach (var variable in _function.Variables)
                undefined.Add(variable, _function.GetUndefinedName(variable));

            return new GimpleFunctionAnnotations(_function.ControlFlowFunction, _function.MemoryVariable, _function.Variables,
                blocks.ToImmutable(), definitions.ToImmutable(), uses.ToImmutable(), _function.Problems, undefined,
                _valueNumberingOptions, _function.Target);
        }
    }
}
