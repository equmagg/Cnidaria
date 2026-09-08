using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;

namespace Cnidaria.C
{
    /// <summary>Selects how much of the GIMPLE invariant set is checked</summary>
    public enum GimpleVerificationLevel : byte
    {
        /// <summary>Runs no checks</summary>
        None,
        /// <summary>Checks statement shapes and operand forms</summary>
        Statements,
        /// <summary>Also checks the renamed operand forms and phi arity</summary>
        Full,
    }

    /// <summary>Checks that lowered functions satisfy the GIMPLE invariants</summary>
    /// <remarks>
    /// Before renaming, computation operands may still be declarations. Afterwards they must be
    /// renamed values or compile-time invariants, and every phi carries exactly one operand per
    /// incoming edge.
    /// </remarks>
    public static class GimpleVerifier
    {
        /// <summary>Checks the statement shapes of a lowered function that has not been renamed</summary>
        public static ImmutableArray<GimpleProblem> Verify(GimpleFunctionDefinition function)
        {
            if (function is null)
                throw new ArgumentNullException(nameof(function));

            var problems = ImmutableArray.CreateBuilder<GimpleProblem>();
            var context = new Context(problems, block: null, renamed: false);

            foreach (var block in function.Blocks)
            {
                foreach (var statement in block.Statements)
                    VerifyStatement(statement, context);
            }

            return problems.ToImmutable();
        }

        /// <summary>Checks the statement shapes and phi arity of a renamed function</summary>
        public static ImmutableArray<GimpleProblem> Verify(
            GimpleFunctionAnnotations function,
            GimpleVerificationLevel level = GimpleVerificationLevel.Full)
        {
            if (function is null)
                throw new ArgumentNullException(nameof(function));

            var problems = ImmutableArray.CreateBuilder<GimpleProblem>();
            if (level == GimpleVerificationLevel.None)
                return problems.ToImmutable();

            var renamed = level == GimpleVerificationLevel.Full;
            var promoted = renamed ? CollectPromotedDeclarations(function) : null;
            foreach (var block in function.Blocks)
            {
                if (!block.IsReachable)
                    continue;

                var context = new Context(problems, block.ControlFlowBlock, renamed, promoted);
                if (renamed)
                    VerifyPhis(block, context);

                foreach (var instruction in block.Statements)
                    VerifyStatement(instruction.Statement, context);
            }

            return problems.ToImmutable();
        }

        // Only promoted declarations are required to appear renamed; the rest stay memory operands
        private static HashSet<object> CollectPromotedDeclarations(GimpleFunctionAnnotations function)
        {
            var promoted = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (var variable in function.Variables)
            {
                if (variable.Symbol is not null)
                    promoted.Add(variable.Symbol);
                else if (variable.Temporary is not null)
                    promoted.Add(variable.Temporary);
            }

            return promoted;
        }

        private static void VerifyPhis(GimpleBlockAnnotations block, in Context context)
        {
            var predecessors = 0;
            foreach (var edge in block.ControlFlowBlock.Predecessors)
            {
                if (edge.Source.IsReachable)
                    predecessors++;
            }

            foreach (var phi in block.Phis)
            {
                if (phi.Operands.Length != predecessors)
                {
                    context.Report(
                        GimpleProblemKind.InvalidPhi,
                        $"Phi '{phi.Result}' carries {phi.Operands.Length.ToString(CultureInfo.InvariantCulture)} operands for " +
                        $"{predecessors.ToString(CultureInfo.InvariantCulture)} incoming edges.");
                }

                foreach (var operand in phi.Operands)
                {
                    if (!SameType(operand.Value.Type, phi.Result.Type))
                    {
                        context.Report(
                            GimpleProblemKind.InvalidPhi,
                            $"Phi '{phi.Result}' takes operand '{operand.Value}' of incompatible type " +
                            $"'{operand.Value.Type.ToDisplayString()}'.");
                    }
                }
            }
        }

        private static void VerifyStatement(GimpleStatement statement, in Context context)
        {
            switch (statement)
            {
                case GimpleAssignStatement assign:
                    VerifyAssign(assign, context);
                    break;

                case GimpleCallStatement call:
                    VerifyCall(call, context);
                    break;

                case GimpleCondStatement conditional:
                    VerifyCond(conditional, context);
                    break;

                case GimpleSwitchStatement switchStatement:
                    context.RequireValue(switchStatement.Expression, "switch");
                    break;

                case GimpleReturnStatement { Expression: not null } returnStatement:
                    context.RequireOperand(returnStatement.Expression, "return");
                    break;

                case GimpleAsmStatement asmStatement:
                    VerifyAsm(asmStatement, context);
                    break;
            }
        }

        private static void VerifyAssign(GimpleAssignStatement assign, in Context context)
        {
            context.RequirePlace(assign.Lhs, "assignment target");

            switch (assign.RhsClass)
            {
                case GimpleRhsClass.Single:
                    if (assign.IsConstructor)
                        break;

                    if (assign.Op1 is { } single && !GimpleOperandRules.IsSingleRhs(single))
                    {
                        context.Report(
                            GimpleProblemKind.InvalidOperand,
                            $"Assignment right-hand side '{single.Kind}' is not a GIMPLE single operand.");
                    }
                    break;

                case GimpleRhsClass.Unary:
                case GimpleRhsClass.Binary:
                case GimpleRhsClass.Ternary:
                    foreach (var operand in assign.Operands)
                        context.RequireValue(operand, GimpleOperators.Name(assign.Subcode));
                    break;

                default:
                    context.Report(
                        GimpleProblemKind.InvalidStatement,
                        $"Assignment carries the non-assignable tree code '{GimpleOperators.Name(assign.Subcode)}'.");
                    break;
            }

            if (assign.Operands.Length != (assign.IsConstructor ? 0 : GimpleOperators.Arity(assign.Subcode)))
            {
                context.Report(
                    GimpleProblemKind.InvalidStatement,
                    $"Assignment with tree code '{GimpleOperators.Name(assign.Subcode)}' carries " +
                    $"{assign.Operands.Length.ToString(CultureInfo.InvariantCulture)} operands.");
            }
        }

        private static void VerifyCall(GimpleCallStatement call, in Context context)
        {
            if (call.Lhs is not null)
                context.RequirePlace(call.Lhs, "call result");

            if (call.Function is not GimpleSymbolValue { Symbol: FunctionSymbol })
                context.RequireValue(call.Function, "callee");

            foreach (var argument in call.Arguments)
                context.RequireOperand(argument, "call argument");
        }

        private static void VerifyCond(GimpleCondStatement conditional, in Context context)
        {
            if (!GimpleOperators.IsComparison(conditional.Code))
            {
                context.Report(
                    GimpleProblemKind.InvalidStatement,
                    $"Branch carries the non-comparison tree code '{GimpleOperators.Name(conditional.Code)}'.");
            }

            context.RequireValue(conditional.Lhs, "branch");
            context.RequireValue(conditional.Rhs, "branch");
        }

        private static void VerifyAsm(GimpleAsmStatement asmStatement, in Context context)
        {
            foreach (var output in asmStatement.Outputs)
            {
                if (output.Target is not null)
                    context.RequirePlace(output.Target, "assembly output");
            }

            foreach (var input in asmStatement.Inputs)
            {
                if (input.Value is not null)
                    context.RequireOperand(input.Value, "assembly input");
            }
        }

        private static bool SameType(QualifiedType left, QualifiedType right)
            => string.Equals(left.Type.ToDisplayString(), right.Type.ToDisplayString(), StringComparison.Ordinal);

        private readonly struct Context
        {
            private readonly ImmutableArray<GimpleProblem>.Builder _problems;
            private readonly ControlFlowBlock? _block;
            private readonly bool _renamed;
            private readonly HashSet<object>? _promoted;

            public Context(
                ImmutableArray<GimpleProblem>.Builder problems,
                ControlFlowBlock? block,
                bool renamed,
                HashSet<object>? promoted = null)
            {
                _problems = problems;
                _block = block;
                _renamed = renamed;
                _promoted = promoted;
            }

            public void Report(GimpleProblemKind kind, string message)
                => _problems.Add(new GimpleProblem(kind, _block, message));

            /// <summary>Requires an operand a computation may consume directly</summary>
            public void RequireValue(GimpleValue value, string context)
            {
                if (GimpleOperandRules.IsValue(value))
                    return;

                if (!_renamed && GimpleOperandRules.IsRegisterOperand(value))
                    return;

                // A declaration that promotion left in memory keeps standing for its storage
                if (_renamed && IsUnpromotedDeclaration(value))
                    return;

                Report(
                    GimpleProblemKind.InvalidOperand,
                    $"Operand '{value.Kind}' of {context} is not a GIMPLE value.");
            }

            private bool IsUnpromotedDeclaration(GimpleValue value)
            {
                switch (value)
                {
                    case GimpleSymbolValue symbolValue:
                        return _promoted is null || !_promoted.Contains(symbolValue.Symbol);
                    case GimpleTemporaryValue temporary:
                        return _promoted is null || !_promoted.Contains(temporary);
                    default:
                        return false;
                }
            }

            /// <summary>Requires an operand a statement may carry, which may reference memory</summary>
            public void RequireOperand(GimpleValue value, string context)
            {
                if (GimpleOperandRules.IsSingleRhs(value))
                    return;

                Report(
                    GimpleProblemKind.InvalidOperand,
                    $"Operand '{value.Kind}' of {context} is not a GIMPLE operand.");
            }

            public void RequirePlace(GimpleValue value, string context)
            {
                if (GimpleOperandRules.IsPlace(value))
                    return;

                Report(
                    GimpleProblemKind.InvalidOperand,
                    $"Operand '{value.Kind}' of {context} does not denote storage.");
            }
        }
    }
}
