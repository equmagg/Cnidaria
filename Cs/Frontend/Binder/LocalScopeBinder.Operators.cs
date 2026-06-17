using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace Cnidaria.Cs
{
    internal sealed partial class LocalScopeBinder : Binder
    {
        private static ImmutableArray<string> GetUnaryOperatorMetadataNames(BoundUnaryOperatorKind op, bool isChecked)
        {
            return op switch
            {
                BoundUnaryOperatorKind.UnaryPlus => ImmutableArray.Create($"{OperatorPrefix}UnaryPlus"),
                BoundUnaryOperatorKind.UnaryMinus => isChecked
                    ? ImmutableArray.Create($"{OperatorPrefix}CheckedUnaryNegation", $"{OperatorPrefix}UnaryNegation")
                    : ImmutableArray.Create($"{OperatorPrefix}UnaryNegation"),
                BoundUnaryOperatorKind.LogicalNot => ImmutableArray.Create($"{OperatorPrefix}LogicalNot"),
                BoundUnaryOperatorKind.BitwiseNot => ImmutableArray.Create($"{OperatorPrefix}OnesComplement"),
                _ => ImmutableArray<string>.Empty
            };
        }

        private static ImmutableArray<string> GetBinaryOperatorMetadataNames(BoundBinaryOperatorKind op, bool isChecked)
        {
            return op switch
            {
                BoundBinaryOperatorKind.Add => isChecked
                        ? ImmutableArray.Create($"{OperatorPrefix}CheckedAddition", $"{OperatorPrefix}Addition")
                        : ImmutableArray.Create($"{OperatorPrefix}Addition"),
                BoundBinaryOperatorKind.Subtract => isChecked
                    ? ImmutableArray.Create($"{OperatorPrefix}CheckedSubtraction", $"{OperatorPrefix}Subtraction")
                    : ImmutableArray.Create($"{OperatorPrefix}Subtraction"),
                BoundBinaryOperatorKind.Multiply => isChecked
                    ? ImmutableArray.Create($"{OperatorPrefix}CheckedMultiply", $"{OperatorPrefix}Multiply")
                    : ImmutableArray.Create($"{OperatorPrefix}Multiply"),
                BoundBinaryOperatorKind.Divide => isChecked
                    ? ImmutableArray.Create($"{OperatorPrefix}CheckedDivision", $"{OperatorPrefix}Division")
                    : ImmutableArray.Create($"{OperatorPrefix}Division"),
                BoundBinaryOperatorKind.Modulo => ImmutableArray.Create($"{OperatorPrefix}Modulus"),
                BoundBinaryOperatorKind.BitwiseAnd => ImmutableArray.Create($"{OperatorPrefix}BitwiseAnd"),
                BoundBinaryOperatorKind.BitwiseOr => ImmutableArray.Create($"{OperatorPrefix}BitwiseOr"),
                BoundBinaryOperatorKind.ExclusiveOr => ImmutableArray.Create($"{OperatorPrefix}ExclusiveOr"),
                BoundBinaryOperatorKind.Equals => ImmutableArray.Create($"{OperatorPrefix}Equality"),
                BoundBinaryOperatorKind.NotEquals => ImmutableArray.Create($"{OperatorPrefix}Inequality"),
                BoundBinaryOperatorKind.LessThan => ImmutableArray.Create($"{OperatorPrefix}LessThan"),
                BoundBinaryOperatorKind.LessThanOrEqual => ImmutableArray.Create($"{OperatorPrefix}LessThanOrEqual"),
                BoundBinaryOperatorKind.GreaterThan => ImmutableArray.Create($"{OperatorPrefix}GreaterThan"),
                BoundBinaryOperatorKind.GreaterThanOrEqual => ImmutableArray.Create($"{OperatorPrefix}GreaterThanOrEqual"),
                BoundBinaryOperatorKind.LeftShift => ImmutableArray.Create($"{OperatorPrefix}LeftShift"),
                BoundBinaryOperatorKind.RightShift => ImmutableArray.Create($"{OperatorPrefix}RightShift"),
                BoundBinaryOperatorKind.UnsignedRightShift => ImmutableArray.Create($"{OperatorPrefix}UnsignedRightShift"),
                _ => ImmutableArray<string>.Empty
            };
        }

        private static ImmutableArray<string> GetCompoundAssignmentOperatorMetadataNames(BoundBinaryOperatorKind op, bool isChecked)
        {
            return op switch
            {
                BoundBinaryOperatorKind.Add => isChecked
                    ? ImmutableArray.Create($"{OperatorPrefix}CheckedAdditionAssignment", $"{OperatorPrefix}AdditionAssignment")
                    : ImmutableArray.Create($"{OperatorPrefix}AdditionAssignment"),
                BoundBinaryOperatorKind.Subtract => isChecked
                    ? ImmutableArray.Create($"{OperatorPrefix}CheckedSubtractionAssignment", $"{OperatorPrefix}SubtractionAssignment")
                    : ImmutableArray.Create($"{OperatorPrefix}SubtractionAssignment"),
                BoundBinaryOperatorKind.Multiply => isChecked
                    ? ImmutableArray.Create($"{OperatorPrefix}CheckedMultiplyAssignment", $"{OperatorPrefix}MultiplyAssignment")
                    : ImmutableArray.Create($"{OperatorPrefix}MultiplyAssignment"),
                BoundBinaryOperatorKind.Divide => isChecked
                    ? ImmutableArray.Create($"{OperatorPrefix}CheckedDivisionAssignment", $"{OperatorPrefix}DivisionAssignment")
                    : ImmutableArray.Create($"{OperatorPrefix}DivisionAssignment"),
                BoundBinaryOperatorKind.Modulo => ImmutableArray.Create($"{OperatorPrefix}ModulusAssignment"),
                BoundBinaryOperatorKind.BitwiseAnd => ImmutableArray.Create($"{OperatorPrefix}BitwiseAndAssignment"),
                BoundBinaryOperatorKind.BitwiseOr => ImmutableArray.Create($"{OperatorPrefix}BitwiseOrAssignment"),
                BoundBinaryOperatorKind.ExclusiveOr => ImmutableArray.Create($"{OperatorPrefix}ExclusiveOrAssignment"),
                BoundBinaryOperatorKind.LeftShift => ImmutableArray.Create($"{OperatorPrefix}LeftShiftAssignment"),
                BoundBinaryOperatorKind.RightShift => ImmutableArray.Create($"{OperatorPrefix}RightShiftAssignment"),
                BoundBinaryOperatorKind.UnsignedRightShift => ImmutableArray.Create($"{OperatorPrefix}UnsignedRightShiftAssignment"),
                _ => ImmutableArray<string>.Empty
            };
        }
        private BoundExpression BindPrefixUnary(PrefixUnaryExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (node.Kind == SyntaxKind.PreIncrementExpression)
                return BindPrefixIncrementOrDecrement(node, isIncrement: true, context, diagnostics);

            if (node.Kind == SyntaxKind.PreDecrementExpression)
                return BindPrefixIncrementOrDecrement(node, isIncrement: false, context, diagnostics);


            var operand = BindExpression(node.Operand, context, diagnostics);
            if (operand.HasErrors)
                return new BoundBadExpression(node);

            BoundUnaryOperatorKind opKind;
            switch (node.Kind)
            {
                case SyntaxKind.UnaryPlusExpression:
                    opKind = BoundUnaryOperatorKind.UnaryPlus;
                    break;
                case SyntaxKind.UnaryMinusExpression:
                    opKind = BoundUnaryOperatorKind.UnaryMinus;
                    break;
                case SyntaxKind.LogicalNotExpression:
                    opKind = BoundUnaryOperatorKind.LogicalNot;
                    break;
                case SyntaxKind.BitwiseNotExpression:
                    opKind = BoundUnaryOperatorKind.BitwiseNot;
                    break;
                case SyntaxKind.AddressOfExpression:
                    return BindAddressOf(node, operand, context, diagnostics);
                case SyntaxKind.PointerIndirectionExpression:
                    return BindPointerIndirection(node, operand, context, diagnostics);

                default:
                    diagnostics.Add(new Diagnostic("CN_UNARY000", DiagnosticSeverity.Error,
                        $"Unary operator not supported: {node.Kind}",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    return new BoundBadExpression(node);
            }
            if (TryBindUserDefinedUnaryOperator(
                operatorSyntax: node,
                operandSyntax: node.Operand,
                op: opKind,
                operand: operand,
                context: context,
                diagnostics: diagnostics,
                out var userDefinedUnary))
            {
                return userDefinedUnary;
            }
            if (opKind == BoundUnaryOperatorKind.LogicalNot)
            {
                var boolType = context.Compilation.GetSpecialType(SpecialType.System_Boolean);
                operand = ApplyConversion(node.Operand, operand, boolType, node, context, diagnostics, requireImplicit: true);
                if (operand.HasErrors) return new BoundBadExpression(node);

                var cv = FoldUnaryConstant(opKind, boolType, operand, isChecked: false);
                return new BoundUnaryExpression(node, opKind, boolType, operand, cv, isChecked: false);
            }

            // numeric / integral
            var promotedType = GetUnaryPromotionType(
                context.Compilation, context.SemanticModel.SyntaxTree, opKind, operand.Type, node, diagnostics);
            if (promotedType is null)
                return new BoundBadExpression(node);

            operand = ApplyConversion(node.Operand, operand, promotedType, node, context, diagnostics, requireImplicit: true);
            if (operand.HasErrors) return new BoundBadExpression(node);

            bool isChecked = IsCheckedOverflowContext && IsOverflowCheckedUnaryOperator(opKind, promotedType);
            var constValue = FoldUnaryConstant(opKind, promotedType, operand, isChecked);
            return new BoundUnaryExpression(node, opKind, promotedType, operand, constValue, isChecked);
        }
        private BoundExpression BindPostfixUnary(PostfixUnaryExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            switch (node.Kind)
            {
                case SyntaxKind.PostIncrementExpression:
                    return BindPostfixIncrementOrDecrement(node, isIncrement: true, resultUsed: true, context, diagnostics);

                case SyntaxKind.PostDecrementExpression:
                    return BindPostfixIncrementOrDecrement(node, isIncrement: false, resultUsed: true, context, diagnostics);

                default:
                    diagnostics.Add(new Diagnostic("CN_UNARY001", DiagnosticSeverity.Error,
                        $"Postfix unary operator not supported: {node.Kind}",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    return new BoundBadExpression(node);
            }
        }
        private BoundExpression BindPrefixIncrementOrDecrement(
            PrefixUnaryExpressionSyntax node,
            bool isIncrement,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            return BindIncrementOrDecrementCore(
                operatorSyntax: node,
                operandSyntax: node.Operand,
                isIncrement: isIncrement,
                isPostfix: false,
                resultUsed: true,
                context: context,
                diagnostics: diagnostics);
        }
        private BoundExpression BindPostfixIncrementOrDecrement(
            PostfixUnaryExpressionSyntax node,
            bool isIncrement,
            bool resultUsed,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            return BindIncrementOrDecrementCore(
                operatorSyntax: node,
                operandSyntax: node.Operand,
                isIncrement: isIncrement,
                isPostfix: true,
                resultUsed: resultUsed,
                context: context,
                diagnostics: diagnostics);
        }
        private BoundExpression BindIncrementOrDecrementCore(
            ExpressionSyntax operatorSyntax,
            ExpressionSyntax operandSyntax,
            bool isIncrement,
            bool isPostfix,
            bool resultUsed,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var lv = BindLValue(operandSyntax, context, diagnostics, requireReadable: true);
            var leftTarget = lv.Target;

            if (leftTarget.HasErrors || lv.Read.HasErrors)
                return new BoundBadExpression(operatorSyntax);

            if (leftTarget is BoundLocalExpression le && le.Local.IsConst)
            {
                diagnostics.Add(new Diagnostic("CN_ASG001", DiagnosticSeverity.Error,
                    "Cannot assign to a const local.",
                    new Location(context.SemanticModel.SyntaxTree, operandSyntax.Span)));
                return new BoundBadExpression(operatorSyntax);
            }
            if (IsReadOnlyLocalTarget(leftTarget))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ASG_READONLYLOCAL_INCDEC",
                    DiagnosticSeverity.Error,
                    "Cannot modify a readonly local.",
                    new Location(context.SemanticModel.SyntaxTree, operandSyntax.Span)));
                return new BoundBadExpression(operatorSyntax);
            }
            if (IsReadOnlyByRefParameterTarget(leftTarget))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ASG_READONLYREF002",
                    DiagnosticSeverity.Error,
                    "Cannot modify a readonly by-ref parameter.",
                    new Location(context.SemanticModel.SyntaxTree, operandSyntax.Span)));
                return new BoundBadExpression(operatorSyntax);
            }

            if (IsDirectOperatorTarget(leftTarget) &&
                (!isPostfix || !resultUsed) &&
                TryBindDirectIncrementDecrementOperator(
                    operatorSyntax,
                    leftTarget,
                    isIncrement,
                    context,
                    diagnostics,
                    out var directCall,
                    out var directMethod))
            {
                if (directCall.HasErrors)
                    return new BoundBadExpression(operatorSyntax);

                return new BoundIncrementDecrementExpression(
                    operatorSyntax,
                    target: leftTarget,
                    read: lv.Read,
                    value: directCall,
                    isIncrement: isIncrement,
                    isPostfix: isPostfix,
                    operatorMethodOpt: directMethod,
                    usesDirectOperator: true,
                    isChecked: directMethod is not null &&
                               directMethod.Name.StartsWith("op_Checked", StringComparison.Ordinal));
            }

            var opKind = isIncrement ? BoundBinaryOperatorKind.Add : BoundBinaryOperatorKind.Subtract;

            var opResult = BindIncrementDecrementOperatorBinary(
                operatorSyntax,
                operandSyntax,
                lv.Read,
                opKind,
                context,
                diagnostics);

            if (opResult.HasErrors)
                return new BoundBadExpression(operatorSyntax);

            var valueToAssign = ApplyCompoundAssignmentConversion(
                syntaxForBoundNode: operatorSyntax,
                expr: opResult,
                targetType: leftTarget.Type,
                diagnosticNode: operatorSyntax,
                context: context,
                diagnostics: diagnostics);

            if (valueToAssign.HasErrors)
                return new BoundBadExpression(operatorSyntax);

            bool isChecked = opResult is BoundBinaryExpression be && be.IsChecked;
            return new BoundIncrementDecrementExpression(
                operatorSyntax,
                target: leftTarget,
                read: lv.Read,
                value: valueToAssign,
                isIncrement: isIncrement,
                isPostfix: isPostfix,
                isChecked: isChecked);
        }
        private BoundExpression BindIncrementDecrementOperatorBinary(
            ExpressionSyntax operatorSyntax,
            ExpressionSyntax operandSyntax,
            BoundExpression left,
            BoundBinaryOperatorKind op,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            if (TryBindUserDefinedIncrementDecrementOperator(
                operatorSyntax: operatorSyntax,
                operandSyntax: operandSyntax,
                operand: left,
                isIncrement: op == BoundBinaryOperatorKind.Add,
                context: ctx,
                diagnostics: diagnostics,
                out var userDefined))
            {
                return userDefined;
            }
            var intType = ctx.Compilation.GetSpecialType(SpecialType.System_Int32);
            BoundExpression one = new BoundLiteralExpression(operatorSyntax, intType, 1);

            if (TryBindPointerArithmeticBinary(
                diagnosticNode: operatorSyntax,
                leftSyntax: operandSyntax,
                left: left,
                rightSyntax: operatorSyntax,
                right: one,
                op: op,
                ctx: ctx,
                diagnostics: diagnostics,
                out var ptrArith))
            {
                return ptrArith;
            }

            var promoted = GetBinaryNumericPromotionType(ctx.Compilation, ctx.SemanticModel.SyntaxTree, left, one, operatorSyntax, diagnostics);

            if (promoted is null || promoted is ErrorTypeSymbol)
                return new BoundBadExpression(operatorSyntax);

            var leftConv = ApplyConversion(operandSyntax, left, promoted, operatorSyntax, ctx, diagnostics, requireImplicit: true);
            var rightConv = ApplyConversion(operatorSyntax, one, promoted, operatorSyntax, ctx, diagnostics, requireImplicit: true);

            if (leftConv.HasErrors || rightConv.HasErrors)
                return new BoundBadExpression(operatorSyntax);

            bool isChecked = IsCheckedOverflowContext && IsOverflowCheckedBinaryOperator(op, promoted);
            var constValue = FoldBinaryConstant(op, promoted, leftConv, rightConv, isChecked);

            return new BoundBinaryExpression(operatorSyntax, op, promoted, leftConv, rightConv, constValue, isChecked);
        }
        private bool TryBindPointerArithmeticBinary(
            SyntaxNode diagnosticNode,
            ExpressionSyntax leftSyntax,
            BoundExpression left,
            ExpressionSyntax rightSyntax,
            BoundExpression right,
            BoundBinaryOperatorKind op,
            BindingContext ctx,
            DiagnosticBag diagnostics,
            out BoundExpression result)
        {
            result = null!;

            bool leftPtr = left.Type is PointerTypeSymbol;
            bool rightPtr = right.Type is PointerTypeSymbol;
            if (!leftPtr && !rightPtr)
                return false;

            EnsureUnsafe(diagnosticNode, ctx, diagnostics);

            if (op is not (BoundBinaryOperatorKind.Add or BoundBinaryOperatorKind.Subtract))
            {
                diagnostics.Add(new Diagnostic("CN_PTRARITH000", DiagnosticSeverity.Error,
                    $"Pointer arithmetic supports only '+' and '-', got '{op}'.",
                    new Location(ctx.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                result = new BoundBadExpression((ExpressionSyntax)diagnosticNode);
                return true;
            }

            if (!leftPtr)
            {
                diagnostics.Add(new Diagnostic("CN_PTRARITH001", DiagnosticSeverity.Error,
                    "Pointer arithmetic currently requires the pointer operand on the left.",
                    new Location(ctx.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                result = new BoundBadExpression((ExpressionSyntax)diagnosticNode);
                return true;
            }

            if (rightPtr)
            {
                if (op != BoundBinaryOperatorKind.Subtract)
                {
                    diagnostics.Add(new Diagnostic("CN_PTRARITH002", DiagnosticSeverity.Error,
                        "Pointer-pointer arithmetic supports only subtraction.",
                        new Location(ctx.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                    result = new BoundBadExpression((ExpressionSyntax)diagnosticNode);
                    return true;
                }
                if (!ReferenceEquals(left.Type, right.Type))
                {
                    diagnostics.Add(new Diagnostic("CN_PTRARITH004", DiagnosticSeverity.Error,
                        $"Pointer subtraction requires both operands to have the same pointer type, " +
                        $"got '{left.Type.Name}' and '{right.Type.Name}'.",
                        new Location(ctx.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                    result = new BoundBadExpression((ExpressionSyntax)diagnosticNode);
                    return true;
                }
                var diffType = ctx.Compilation.GetSpecialType(SpecialType.System_IntPtr);
                result = new BoundBinaryExpression(
                    diagnosticNode,
                    BoundBinaryOperatorKind.Subtract,
                    diffType,
                    left,
                    right,
                    Optional<object>.None);
                return true;
            }

            if (!IsIntegral(right.Type.SpecialType))
            {
                diagnostics.Add(new Diagnostic("CN_PTRARITH003", DiagnosticSeverity.Error,
                    $"Pointer arithmetic requires an integral offset, got '{right.Type.Name}'.",
                    new Location(ctx.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                result = new BoundBadExpression((ExpressionSyntax)diagnosticNode);
                return true;
            }

            var offsetType = right.Type.SpecialType switch
            {
                SpecialType.System_IntPtr or SpecialType.System_UIntPtr => right.Type,
                _ => ctx.Compilation.GetSpecialType(SpecialType.System_Int32)
            };
            var rightConv = ApplyConversion(rightSyntax, right, offsetType, diagnosticNode, ctx, diagnostics, requireImplicit: true);

            if (rightConv.HasErrors)
            {
                result = new BoundBadExpression((ExpressionSyntax)diagnosticNode);
                return true;
            }

            result = new BoundBinaryExpression(
                diagnosticNode,
                op,
                left.Type,
                left,
                rightConv,
                Optional<object>.None);

            return true;
        }
        private static TypeSymbol? GetUnaryPromotionType(
            Compilation compilation,
            SyntaxTree tree,
            BoundUnaryOperatorKind op,
            TypeSymbol operandType,
            SyntaxNode diagnosticNode,
            DiagnosticBag diagnostics)
        {
            var st = operandType.SpecialType;

            bool IsSmallIntegral(SpecialType t) => t is
                SpecialType.System_Int8 or SpecialType.System_UInt8 or
                SpecialType.System_Int16 or SpecialType.System_UInt16 or
                SpecialType.System_Char;


            switch (op)
            {
                case BoundUnaryOperatorKind.UnaryPlus:
                case BoundUnaryOperatorKind.UnaryMinus:
                    if (!IsNumeric(st))
                    {
                        diagnostics.Add(new Diagnostic("CN_UNARY_NUM000", DiagnosticSeverity.Error,
                            $"Operator requires numeric operand, got '{operandType.Name}'.",
                            new Location(tree, diagnosticNode.Span)));
                        return null;
                    }
                    if (IsSmallIntegral(st))
                        return compilation.GetSpecialType(SpecialType.System_Int32);
                    if (op == BoundUnaryOperatorKind.UnaryMinus)
                    {
                        if (st == SpecialType.System_UInt32)
                            return compilation.GetSpecialType(SpecialType.System_Int64);

                        if (st == SpecialType.System_UInt64 || st == SpecialType.System_UIntPtr)
                        {
                            var badType = st == SpecialType.System_UIntPtr ? "nuint" : "ulong";
                            diagnostics.Add(new Diagnostic("CN_UNARY_NUM001", DiagnosticSeverity.Error,
                                $"The operand of unary '-' cannot be of type '{badType}'.",
                                new Location(tree, diagnosticNode.Span)));
                            return null;
                        }
                    }
                    return operandType;
                case BoundUnaryOperatorKind.BitwiseNot:
                    if (IsEnumType(operandType))
                        return operandType;
                    if (!IsIntegral(st))
                    {
                        diagnostics.Add(new Diagnostic("CN_UNARY_INT000", DiagnosticSeverity.Error,
                            $"Operator requires integral operand, got '{operandType.Name}'.",
                            new Location(tree, diagnosticNode.Span)));
                        return null;
                    }

                    if (IsSmallIntegral(st))
                        return compilation.GetSpecialType(SpecialType.System_Int32);

                    return operandType;

                default:
                    diagnostics.Add(new Diagnostic("CN_UNARY_UNKNOWN", DiagnosticSeverity.Error,
                        $"Unexpected unary operator '{op}'.",
                        new Location(tree, diagnosticNode.Span)));
                    return null;
            }
        }
        private static Optional<object> FoldUnaryConstant(BoundUnaryOperatorKind op, TypeSymbol type, BoundExpression operand, bool isChecked)
        {
            if (!operand.ConstantValueOpt.HasValue)
                return Optional<object>.None;

            var v = operand.ConstantValueOpt.Value;

            switch (op)
            {
                case BoundUnaryOperatorKind.UnaryPlus:
                    return operand.ConstantValueOpt;

                case BoundUnaryOperatorKind.LogicalNot:
                    if (v is bool b) return new Optional<object>(!b);
                    break;

                case BoundUnaryOperatorKind.UnaryMinus:
                    switch (type.SpecialType)
                    {
                        case SpecialType.System_Int32:
                            if (v is int i)
                            {
                                try
                                {
                                    return new Optional<object>(isChecked ? checked(-i) : unchecked(-i));
                                }
                                catch (OverflowException)
                                {
                                    return Optional<object>.None;
                                }
                            }
                            break;
                        case SpecialType.System_Int64:
                            if (v is long l)
                            {
                                try
                                {
                                    return new Optional<object>(isChecked ? checked(-l) : unchecked(-l));
                                }
                                catch (OverflowException)
                                {
                                    return Optional<object>.None;
                                }
                            }
                            break;
                        case SpecialType.System_Single:
                            if (v is float f) return new Optional<object>(-f);
                            break;
                        case SpecialType.System_Double:
                            if (v is double d) return new Optional<object>(-d);
                            break;
                        case SpecialType.System_Decimal:
                            if (v is decimal m) return new Optional<object>(-m);
                            break;
                    }
                    break;
                case BoundUnaryOperatorKind.BitwiseNot:
                    switch (GetEffectiveNumericSpecialType(type))
                    {
                        case SpecialType.System_Int8:
                            if (v is sbyte sb) return new Optional<object>((sbyte)~sb);
                            break;
                        case SpecialType.System_UInt8:
                            if (v is byte bt) return new Optional<object>((byte)~bt);
                            break;
                        case SpecialType.System_Int16:
                            if (v is short s) return new Optional<object>((short)~s);
                            break;
                        case SpecialType.System_UInt16:
                            if (v is ushort us) return new Optional<object>((ushort)~us);
                            break;
                        case SpecialType.System_Int32:
                            if (v is int i) return new Optional<object>(~i);
                            break;
                        case SpecialType.System_UInt32:
                            if (v is uint ui) return new Optional<object>(~ui);
                            break;
                        case SpecialType.System_Int64:
                            if (v is long l) return new Optional<object>(~l);
                            break;
                        case SpecialType.System_UInt64:
                            if (v is ulong ul) return new Optional<object>(~ul);
                            break;
                    }
                    break;
            }
            return Optional<object>.None;
        }
        private BoundExpression BindBinary(BinaryExpressionSyntax bin, BindingContext context, DiagnosticBag diagnostics)
        {
            if (bin.Kind == SyntaxKind.AsExpression)
                return BindAsExpression(bin, context, diagnostics);
            if (bin.Kind == SyntaxKind.IsExpression)
                return BindIsExpression(bin, context, diagnostics);
            if (bin.Kind == SyntaxKind.LogicalAndExpression || bin.Kind == SyntaxKind.LogicalOrExpression)
                return BindConditionalLogicalBinary(bin, context, diagnostics);
            var left = BindExpression(bin.Left, context, diagnostics);
            var right = BindExpression(bin.Right, context, diagnostics);

            if (left.HasErrors || right.HasErrors)
                return new BoundBadExpression(bin);

            switch (bin.Kind)
            {
                // arithmetic numeric
                case SyntaxKind.AddExpression:
                case SyntaxKind.SubtractExpression:
                case SyntaxKind.MultiplyExpression:
                case SyntaxKind.DivideExpression:
                case SyntaxKind.ModuloExpression:
                    return BindNumericBinary(bin, left, right, context, diagnostics);

                // bitwise & | ^ (bool or integral)
                case SyntaxKind.BitwiseAndExpression:
                case SyntaxKind.BitwiseOrExpression:
                case SyntaxKind.ExclusiveOrExpression:
                    return BindBitwiseBinary(bin, left, right, context, diagnostics);

                // conditional logical && ||
                case SyntaxKind.LogicalAndExpression:
                case SyntaxKind.LogicalOrExpression:
                    return BindConditionalLogicalBinary(bin, context, diagnostics);

                // equality
                case SyntaxKind.EqualsExpression:
                case SyntaxKind.NotEqualsExpression:
                    return BindEqualityBinary(bin, left, right, context, diagnostics);

                // relational
                case SyntaxKind.LessThanExpression:
                case SyntaxKind.LessThanOrEqualExpression:
                case SyntaxKind.GreaterThanExpression:
                case SyntaxKind.GreaterThanOrEqualExpression:
                    return BindRelationalBinary(bin, left, right, context, diagnostics);

                // shifts
                case SyntaxKind.LeftShiftExpression:
                case SyntaxKind.RightShiftExpression:
                case SyntaxKind.UnsignedRightShiftExpression:
                    return BindShiftBinary(bin, left, right, context, diagnostics);

                // coalesing
                case SyntaxKind.CoalesceExpression:
                    return BindNullCoalescing(bin, left, right, context, diagnostics);

                default:
                    diagnostics.Add(new Diagnostic("CN_BIN000", DiagnosticSeverity.Error,
                        $"Binary operator not supported: {bin.Kind}",
                        new Location(context.SemanticModel.SyntaxTree, bin.Span)));
                    return new BoundBadExpression(bin);
            }
        }
        private BoundExpression BindAsExpression(BinaryExpressionSyntax bin, BindingContext ctx, DiagnosticBag diagnostics)
        {
            var operand = BindExpression(bin.Left, ctx, diagnostics);
            if (operand.HasErrors)
                return new BoundBadExpression(bin);
            if (bin.Right is not TypeSyntax typeSyntax)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_AS000",
                    DiagnosticSeverity.Error,
                    "The right hand side of the 'as' operator must be a type.",
                    new Location(ctx.SemanticModel.SyntaxTree, bin.Right.Span)));
                return new BoundBadExpression(bin);
            }
            var targetType = BindType(typeSyntax, ctx, diagnostics);
            if (targetType is ErrorTypeSymbol)
                return new BoundBadExpression(bin);
            if (!targetType.IsReferenceType && !TryGetSystemNullableInfo(targetType, out _, out _))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_AS001",
                    DiagnosticSeverity.Error,
                    "The 'as' operator may only be used with reference types or nullable value types.",
                    new Location(ctx.SemanticModel.SyntaxTree, typeSyntax.Span)));
                return new BoundBadExpression(bin);
            }

            var conv = ClassifyConversion(operand, targetType);
            if (!conv.Exists)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_AS002",
                    DiagnosticSeverity.Error,
                    $"Cannot convert type '{operand.Type.Name}' to '{targetType.Name}' via the 'as' operator.",
                    new Location(ctx.SemanticModel.SyntaxTree, bin.Span)));
                return new BoundBadExpression(bin);
            }

            if (conv.Kind is ConversionKind.ImplicitNumeric
                or ConversionKind.ImplicitConstant
                or ConversionKind.ExplicitNumeric
                or ConversionKind.ImplicitTuple
                or ConversionKind.ExplicitTuple
                or ConversionKind.ImplicitStackAlloc
                or ConversionKind.UserDefined)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_AS003",
                    DiagnosticSeverity.Error,
                    $"Cannot convert type '{operand.Type.Name}' to '{targetType.Name}' via the 'as' operator.",
                    new Location(ctx.SemanticModel.SyntaxTree, bin.Span)));
                return new BoundBadExpression(bin);
            }

            return new BoundAsExpression(bin, targetType, operand, conv);
        }
        private BoundExpression BindIsExpression(BinaryExpressionSyntax bin, BindingContext ctx, DiagnosticBag diagnostics)
        {
            var operand = BindExpression(bin.Left, ctx, diagnostics);
            if (operand.HasErrors)
                return new BoundBadExpression(bin);
            if (bin.Right is not TypeSyntax)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_IS000",
                    DiagnosticSeverity.Error,
                    "The right hand side of the 'is' operator must be a type.",
                    new Location(ctx.SemanticModel.SyntaxTree, bin.Right.Span)));
                return new BoundBadExpression(bin);
            }


            var asToken = new SyntaxToken(
                SyntaxKind.AsKeyword,
                bin.OperatorToken.Span,
                valueText: null,
                value: null,
                leadingTrivia: Array.Empty<SyntaxTrivia>(),
                trailingTrivia: Array.Empty<SyntaxTrivia>());
            var asSyntax = new BinaryExpressionSyntax(SyntaxKind.AsExpression, bin.Left, asToken, bin.Right);
            var asBound = BindAsExpression(asSyntax, ctx, diagnostics);
            if (asBound.HasErrors)
                return new BoundBadExpression(bin);

            var nullToken = new SyntaxToken(
                SyntaxKind.NullKeyword,
                bin.OperatorToken.Span,
                valueText: null,
                value: null,
                leadingTrivia: Array.Empty<SyntaxTrivia>(),
                trailingTrivia: Array.Empty<SyntaxTrivia>());
            var nullLit = new LiteralExpressionSyntax(SyntaxKind.NullLiteralExpression, nullToken);
            var nullBound = BindLiteral(nullLit, ctx, diagnostics);

            var notEqToken = new SyntaxToken(
                SyntaxKind.ExclamationEqualsToken,
                bin.OperatorToken.Span,
                valueText: null,
                value: null,
                leadingTrivia: Array.Empty<SyntaxTrivia>(),
                trailingTrivia: Array.Empty<SyntaxTrivia>());
            var fakeNotEq = new BinaryExpressionSyntax(SyntaxKind.NotEqualsExpression, bin.Left, notEqToken, nullLit);

            return BindEqualityBinary(fakeNotEq, asBound, nullBound, ctx, diagnostics);
        }
        private BoundExpression BindNumericBinary(
            BinaryExpressionSyntax bin,
            BoundExpression left,
            BoundExpression right,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            var op = bin.Kind switch
            {
                SyntaxKind.AddExpression => BoundBinaryOperatorKind.Add,
                SyntaxKind.SubtractExpression => BoundBinaryOperatorKind.Subtract,
                SyntaxKind.MultiplyExpression => BoundBinaryOperatorKind.Multiply,
                SyntaxKind.DivideExpression => BoundBinaryOperatorKind.Divide,
                SyntaxKind.ModuloExpression => BoundBinaryOperatorKind.Modulo,
                _ => throw new InvalidOperationException()
            };
            if (TryBindUserDefinedBinaryOperator(
                operatorSyntax: bin,
                leftSyntax: bin.Left,
                left: left,
                rightSyntax: bin.Right,
                right: right,
                op: op,
                context: ctx,
                diagnostics: diagnostics,
                out var userDefined))
            {
                return userDefined;
            }
            if (op == BoundBinaryOperatorKind.Add &&
                (left.Type.SpecialType == SpecialType.System_String
                || right.Type.SpecialType == SpecialType.System_String))
            {
                return BindStringConcatenation(bin, left, right, ctx, diagnostics);
            }
            if (TryBindPointerArithmeticBinary(
                diagnosticNode: bin,
                leftSyntax: bin.Left,
                left: left,
                rightSyntax: bin.Right,
                right: right,
                op: op,
                ctx: ctx,
                diagnostics: diagnostics,
                out var ptrArith))
            {
                return ptrArith;
            }
            // enum subtraction
            if (op == BoundBinaryOperatorKind.Subtract &&
                IsEnumType(left.Type) &&
                ReferenceEquals(left.Type, right.Type))
            {
                var underlying = GetEnumUnderlyingTypeOrDefault(ctx.Compilation, left.Type);
                var promoted = GetBinaryNumericPromotionType(
                    ctx.Compilation,
                    ctx.SemanticModel.SyntaxTree,
                    underlying,
                    underlying,
                    bin,
                    diagnostics);

                if (promoted is null || promoted is ErrorTypeSymbol)
                    return new BoundBadExpression(bin);

                var leftConv = ApplyConversion(bin.Left, left, promoted, bin, ctx, diagnostics, requireImplicit: false);
                var rightConv = ApplyConversion(bin.Right, right, promoted, bin, ctx, diagnostics, requireImplicit: false);

                if (leftConv.HasErrors || rightConv.HasErrors)
                    return new BoundBadExpression(bin);

                bool isChecked = IsCheckedOverflowContext && IsOverflowCheckedBinaryOperator(op, promoted);
                var constValue = FoldBinaryConstant(op, promoted, leftConv, rightConv, isChecked);

                return new BoundBinaryExpression(bin, op, promoted, leftConv, rightConv, constValue, isChecked);

            }

            {
                var promoted = GetBinaryNumericPromotionType(ctx.Compilation, ctx.SemanticModel.SyntaxTree, left, right, bin, diagnostics);
                if (promoted is null || promoted is ErrorTypeSymbol)
                    return new BoundBadExpression(bin);

                left = ApplyConversion(bin.Left, left, promoted, bin, ctx, diagnostics, requireImplicit: true);
                right = ApplyConversion(bin.Right, right, promoted, bin, ctx, diagnostics, requireImplicit: true);
                if (left.HasErrors || right.HasErrors)
                    return new BoundBadExpression(bin);

                bool isChecked = IsCheckedOverflowContext && IsOverflowCheckedBinaryOperator(op, promoted);
                var constValue = FoldBinaryConstant(op, promoted, left, right, isChecked);
                return new BoundBinaryExpression(bin, op, promoted, left, right, constValue, isChecked);
            }
        }
        private BoundExpression BindBitwiseBinary(
            BinaryExpressionSyntax bin,
            BoundExpression left,
            BoundExpression right,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            var op = bin.Kind switch
            {
                SyntaxKind.BitwiseAndExpression => BoundBinaryOperatorKind.BitwiseAnd,
                SyntaxKind.BitwiseOrExpression => BoundBinaryOperatorKind.BitwiseOr,
                SyntaxKind.ExclusiveOrExpression => BoundBinaryOperatorKind.ExclusiveOr,
                _ => throw new InvalidOperationException()
            };
            if (TryBindUserDefinedBinaryOperator(
                operatorSyntax: bin,
                leftSyntax: bin.Left,
                left: left,
                rightSyntax: bin.Right,
                right: right,
                op: op,
                context: ctx,
                diagnostics: diagnostics,
                out var userDefined))
            {
                return userDefined;
            }
            var boolType = ctx.Compilation.GetSpecialType(SpecialType.System_Boolean);
            if (left.Type.SpecialType == SpecialType.System_Boolean && right.Type.SpecialType == SpecialType.System_Boolean)
            {
                var cv = FoldBooleanBinaryConstant(op, left, right);
                return new BoundBinaryExpression(bin, op, boolType, left, right, cv);
            }

            // enum op enum
            if (IsEnumType(left.Type) && ReferenceEquals(left.Type, right.Type))
            {
                var underlying = GetEnumUnderlyingTypeOrDefault(ctx.Compilation, left.Type);
                var constValue = FoldBitwiseConstant(op, underlying.SpecialType, left, right);
                return new BoundBinaryExpression(bin, op, left.Type, left, right, constValue);
            }

            // integral only
            if (!IsIntegral(left.Type.SpecialType) || !IsIntegral(right.Type.SpecialType))
            {
                diagnostics.Add(new Diagnostic("CN_BIT000", DiagnosticSeverity.Error,
                    $"Bitwise operator requires integral operands (or bool/bool, or same enum type), got '{left.Type.Name}' and '{right.Type.Name}'.",
                    new Location(ctx.SemanticModel.SyntaxTree, bin.Span)));
                return new BoundBadExpression(bin);
            }

            var promoted = GetBinaryNumericPromotionType(ctx.Compilation, ctx.SemanticModel.SyntaxTree, left, right, bin, diagnostics);
            if (promoted is null || promoted is ErrorTypeSymbol)
                return new BoundBadExpression(bin);

            left = ApplyConversion(bin.Left, left, promoted, bin, ctx, diagnostics, requireImplicit: true);
            right = ApplyConversion(bin.Right, right, promoted, bin, ctx, diagnostics, requireImplicit: true);

            if (left.HasErrors || right.HasErrors)
                return new BoundBadExpression(bin);

            var folded = FoldBitwiseConstant(op, promoted.SpecialType, left, right);
            return new BoundBinaryExpression(bin, op, promoted, left, right, folded);
        }
        private BoundExpression BindConditionalLogicalBinary(
            BinaryExpressionSyntax bin,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            var op = bin.Kind switch
            {
                SyntaxKind.LogicalAndExpression => BoundBinaryOperatorKind.LogicalAnd,
                SyntaxKind.LogicalOrExpression => BoundBinaryOperatorKind.LogicalOr,
                _ => throw new InvalidOperationException()
            };

            var boolType = ctx.Compilation.GetSpecialType(SpecialType.System_Boolean);

            var left = BindExpression(bin.Left, ctx, diagnostics);
            left = ApplyConversion(bin.Left, left, boolType, bin, ctx, diagnostics, requireImplicit: true);
            var rightBinder = op == BoundBinaryOperatorKind.LogicalAnd
                ? CreateFlowScopeBinderForTrue(left) : this;

            var right = rightBinder.BindExpression(bin.Right, ctx, diagnostics);
            right = rightBinder.ApplyConversion(bin.Right, right, boolType, bin, ctx, diagnostics, requireImplicit: true);

            if (left.HasErrors || right.HasErrors)
                return new BoundBadExpression(bin);

            return new BoundBinaryExpression(bin, op, boolType, left, right, Optional<object>.None);
        }
        private BoundExpression BindEqualityBinary(
            BinaryExpressionSyntax bin,
            BoundExpression left,
            BoundExpression right,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            var op = bin.Kind switch
            {
                SyntaxKind.EqualsExpression => BoundBinaryOperatorKind.Equals,
                SyntaxKind.NotEqualsExpression => BoundBinaryOperatorKind.NotEquals,
                _ => throw new InvalidOperationException()
            };
            if (TryBindUserDefinedBinaryOperator(
                operatorSyntax: bin,
                leftSyntax: bin.Left,
                left: left,
                rightSyntax: bin.Right,
                right: right,
                op: op,
                context: ctx,
                diagnostics: diagnostics,
                out var userDefined,
                requireBooleanReturn: true))
            {
                return userDefined;
            }
            var boolType = ctx.Compilation.GetSpecialType(SpecialType.System_Boolean);
            if (IsNumeric(left.Type.SpecialType) && IsNumeric(right.Type.SpecialType))
            {
                var promoted = GetBinaryNumericPromotionType(ctx.Compilation, ctx.SemanticModel.SyntaxTree, left, right, bin, diagnostics);
                if (promoted is null || promoted is ErrorTypeSymbol) return new BoundBadExpression(bin);

                left = ApplyConversion(bin.Left, left, promoted, bin, ctx, diagnostics, requireImplicit: true);
                right = ApplyConversion(bin.Right, right, promoted, bin, ctx, diagnostics, requireImplicit: true);

                if (left.HasErrors || right.HasErrors) return new BoundBadExpression(bin);
                var cv = FoldBooleanBinaryConstant(op, left, right);
                return new BoundBinaryExpression(bin, op, boolType, left, right, cv);
            }
            // enum == 0 / enum != 0
            if (IsEnumType(left.Type) && !IsEnumType(right.Type))
            {
                var conv = ClassifyConversion(right, left.Type);
                if (conv.Exists && conv.IsImplicit)
                {
                    var r2 = ApplyConversion(
                        exprSyntax: bin.Right,
                        expr: right,
                        targetType: left.Type,
                        diagnosticNode: bin,
                        context: ctx,
                        diagnostics: diagnostics,
                        requireImplicit: true);

                    if (!r2.HasErrors)
                    {
                        var cv = FoldBooleanBinaryConstant(op, left, r2);
                        return new BoundBinaryExpression(bin, op, boolType, left, r2, cv);
                    }
                }
            }
            if (IsEnumType(right.Type) && !IsEnumType(left.Type))
            {
                var conv = ClassifyConversion(left, right.Type);
                if (conv.Exists && conv.IsImplicit)
                {
                    var l2 = ApplyConversion(
                        exprSyntax: bin.Left,
                        expr: left,
                        targetType: right.Type,
                        diagnosticNode: bin,
                        context: ctx,
                        diagnostics: diagnostics,
                        requireImplicit: true);

                    if (!l2.HasErrors)
                    {
                        var cv = FoldBooleanBinaryConstant(op, l2, right);
                        return new BoundBinaryExpression(bin, op, boolType, l2, right, cv);
                    }
                }
            }
            // enum == enum
            if (IsEnumType(left.Type) && ReferenceEquals(left.Type, right.Type))
            {
                var cv = FoldBooleanBinaryConstant(op, left, right);
                return new BoundBinaryExpression(bin, op, boolType, left, right, cv);
            }

            // bool == bool
            if (left.Type.SpecialType == SpecialType.System_Boolean && right.Type.SpecialType == SpecialType.System_Boolean)
            {
                var cv = FoldBooleanBinaryConstant(op, left, right);
                return new BoundBinaryExpression(bin, op, boolType, left, right, Optional<object>.None);
            }

            if (TryBindPointerEquality(left, right, bin, ctx, diagnostics, out var lp, out var rp))
                return new BoundBinaryExpression(bin, op, boolType, lp, rp, Optional<object>.None);

            if (TryBindTypeParameterNullEquality(left, right, bin, ctx, diagnostics, out var tl, out var tr))
                return new BoundBinaryExpression(bin, op, boolType, tl, tr, Optional<object>.None);

            // reference equality
            {
                if (TryBindReferenceEquality(left, right, bin, ctx, diagnostics, out var l2, out var r2))
                    return new BoundBinaryExpression(bin, op, boolType, l2, r2, Optional<object>.None);
            }


            diagnostics.Add(new Diagnostic("CN_EQ000", DiagnosticSeverity.Error,
                $"Operator '{bin.OperatorToken.Kind}' cannot be applied to operands of type '{left.Type.Name}' and '{right.Type.Name}'.",
                new Location(ctx.SemanticModel.SyntaxTree, bin.Span)));
            return new BoundBadExpression(bin);
        }
        private bool TryBindPointerEquality(
            BoundExpression left,
            BoundExpression right,
            SyntaxNode diagnosticNode,
            BindingContext ctx,
            DiagnosticBag diagnostics,
            out BoundExpression leftOut,
            out BoundExpression rightOut)
        {
            leftOut = left;
            rightOut = right;

            bool IsNullLiteral(BoundExpression e) =>
                e.Type is NullTypeSymbol || (e.ConstantValueOpt.HasValue && e.ConstantValueOpt.Value is null);

            if (IsNullLiteral(left) && right.Type is PointerTypeSymbol)
            {
                EnsureUnsafe(diagnosticNode, ctx, diagnostics);
                leftOut = ApplyConversion((ExpressionSyntax)left.Syntax, left, right.Type, diagnosticNode, ctx, diagnostics, requireImplicit: true);
                return !leftOut.HasErrors;
            }

            if (IsNullLiteral(right) && left.Type is PointerTypeSymbol)
            {
                EnsureUnsafe(diagnosticNode, ctx, diagnostics);
                rightOut = ApplyConversion((ExpressionSyntax)right.Syntax, right, left.Type, diagnosticNode, ctx, diagnostics, requireImplicit: true);
                return !rightOut.HasErrors;
            }

            if (left.Type is PointerTypeSymbol && right.Type is PointerTypeSymbol)
            {
                EnsureUnsafe(diagnosticNode, ctx, diagnostics);
                return ReferenceEquals(left.Type, right.Type);
            }

            return false;
        }
        private bool TryBindTypeParameterNullEquality(
            BoundExpression left,
            BoundExpression right,
            SyntaxNode diagnosticNode,
            BindingContext ctx,
            DiagnosticBag diagnostics,
            out BoundExpression leftOut,
            out BoundExpression rightOut)
        {
            leftOut = left;
            rightOut = right;

            bool leftNull = left.Type is NullTypeSymbol || (left.ConstantValueOpt.HasValue && left.ConstantValueOpt.Value is null);
            bool rightNull = right.Type is NullTypeSymbol || (right.ConstantValueOpt.HasValue && right.ConstantValueOpt.Value is null);

            if (leftNull == rightNull)
                return false;

            BoundExpression value = leftNull ? right : left;

            if (value.Type is not TypeParameterSymbol tp)
                return false;

            if ((tp.GenericConstraint & GenericConstraintsFlags.AllowsRefStruct) != 0)
                return false;

            var objectType = ctx.Compilation.GetSpecialType(SpecialType.System_Object);

            BoundExpression boxedValue = new BoundConversionExpression(
                syntax: value.Syntax,
                type: objectType,
                operand: value,
                conversion: new Conversion(ConversionKind.Boxing),
                isChecked: false);

            BoundExpression nullObject = new BoundConversionExpression(
                syntax: leftNull ? left.Syntax : right.Syntax,
                type: objectType,
                operand: leftNull ? left : right,
                conversion: new Conversion(ConversionKind.NullLiteral),
                isChecked: false);

            if (leftNull)
            {
                leftOut = nullObject;
                rightOut = boxedValue;
            }
            else
            {
                leftOut = boxedValue;
                rightOut = nullObject;
            }

            return true;
        }
        private bool TryBindReferenceEquality(
            BoundExpression left,
            BoundExpression right,
            SyntaxNode diagnosticNode,
            BindingContext ctx,
            DiagnosticBag diagnostics,
            out BoundExpression leftOut,
            out BoundExpression rightOut)
        {
            leftOut = left;
            rightOut = right;

            bool IsNullLiteral(BoundExpression e) => e.Type is NullTypeSymbol ||
                (e.ConstantValueOpt.HasValue && e.ConstantValueOpt.Value is null);

            // null == ref
            if (IsNullLiteral(left) && right.Type.IsReferenceType)
            {
                leftOut = ApplyConversion((ExpressionSyntax)
                    left.Syntax, left, right.Type, diagnosticNode, ctx, diagnostics, requireImplicit: true);
                return !leftOut.HasErrors;
            }
            if (IsNullLiteral(right) && left.Type.IsReferenceType)
            {
                rightOut = ApplyConversion((ExpressionSyntax)
                    right.Syntax, right, left.Type, diagnosticNode, ctx, diagnostics, requireImplicit: true);
                return !rightOut.HasErrors;
            }

            if (!left.Type.IsReferenceType || !right.Type.IsReferenceType)
                return false;

            if (ReferenceEquals(left.Type, right.Type))
                return true;

            // allow implicit reference conversion either way
            if (HasImplicitReferenceConversion(left.Type, right.Type))
            {
                leftOut = ApplyConversion((ExpressionSyntax)
                    left.Syntax, left, right.Type, diagnosticNode, ctx, diagnostics, requireImplicit: true);
                return !leftOut.HasErrors;
            }
            if (HasImplicitReferenceConversion(right.Type, left.Type))
            {
                rightOut = ApplyConversion((ExpressionSyntax)
                    right.Syntax, right, left.Type, diagnosticNode, ctx, diagnostics, requireImplicit: true);
                return !rightOut.HasErrors;
            }

            return false;
        }
        private BoundExpression BindRelationalBinary(
            BinaryExpressionSyntax bin,
            BoundExpression left,
            BoundExpression right,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            var op = bin.Kind switch
            {
                SyntaxKind.LessThanExpression => BoundBinaryOperatorKind.LessThan,
                SyntaxKind.LessThanOrEqualExpression => BoundBinaryOperatorKind.LessThanOrEqual,
                SyntaxKind.GreaterThanExpression => BoundBinaryOperatorKind.GreaterThan,
                SyntaxKind.GreaterThanOrEqualExpression => BoundBinaryOperatorKind.GreaterThanOrEqual,
                _ => throw new InvalidOperationException()
            };
            if (TryBindUserDefinedBinaryOperator(
                operatorSyntax: bin,
                leftSyntax: bin.Left,
                left: left,
                rightSyntax: bin.Right,
                right: right,
                op: op,
                context: ctx,
                diagnostics: diagnostics,
                out var userDefined,
                requireBooleanReturn: true))
            {
                return userDefined;
            }
            var boolType = ctx.Compilation.GetSpecialType(SpecialType.System_Boolean);

            if (IsEnumType(left.Type) && ReferenceEquals(left.Type, right.Type))
            {
                var constValue = FoldBooleanBinaryConstant(op, left, right);
                return new BoundBinaryExpression(bin, op, boolType, left, right, constValue);
            }

            if (!IsNumeric(left.Type.SpecialType) || !IsNumeric(right.Type.SpecialType))
            {
                diagnostics.Add(new Diagnostic("CN_REL000", DiagnosticSeverity.Error,
                    $"Relational operator requires numeric operands, got '{left.Type.Name}' and '{right.Type.Name}'.",
                    new Location(ctx.SemanticModel.SyntaxTree, bin.Span)));
                return new BoundBadExpression(bin);
            }

            var promoted = GetBinaryNumericPromotionType(ctx.Compilation, ctx.SemanticModel.SyntaxTree, left, right, bin, diagnostics);
            if (promoted is null || promoted is ErrorTypeSymbol) return new BoundBadExpression(bin);

            left = ApplyConversion(bin.Left, left, promoted, bin, ctx, diagnostics, requireImplicit: true);
            right = ApplyConversion(bin.Right, right, promoted, bin, ctx, diagnostics, requireImplicit: true);

            if (left.HasErrors || right.HasErrors) return new BoundBadExpression(bin);

            return new BoundBinaryExpression(bin, op, boolType, left, right, Optional<object>.None);
        }
        private BoundExpression BindShiftBinary(
            BinaryExpressionSyntax bin,
            BoundExpression left,
            BoundExpression right,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            var op = bin.Kind switch
            {
                SyntaxKind.LeftShiftExpression => BoundBinaryOperatorKind.LeftShift,
                SyntaxKind.RightShiftExpression => BoundBinaryOperatorKind.RightShift,
                SyntaxKind.UnsignedRightShiftExpression => BoundBinaryOperatorKind.UnsignedRightShift,
                _ => throw new InvalidOperationException()
            };
            if (TryBindUserDefinedBinaryOperator(
                operatorSyntax: bin,
                leftSyntax: bin.Left,
                left: left,
                rightSyntax: bin.Right,
                right: right,
                op: op,
                context: ctx,
                diagnostics: diagnostics,
                out var userDefined))
            {
                return userDefined;
            }
            if (!IsIntegral(left.Type.SpecialType) || !IsIntegral(right.Type.SpecialType))
            {
                diagnostics.Add(new Diagnostic("CN_SHIFT000", DiagnosticSeverity.Error,
                    $"Shift operator requires integral operands, got '{left.Type.Name}' and '{right.Type.Name}'.",
                    new Location(ctx.SemanticModel.SyntaxTree, bin.Span)));
                return new BoundBadExpression(bin);
            }
            var leftPromoted = GetUnaryPromotionType(ctx.Compilation, ctx.SemanticModel.SyntaxTree,
                BoundUnaryOperatorKind.UnaryPlus, left.Type, bin, diagnostics);
            if (leftPromoted is null) return new BoundBadExpression(bin);

            left = ApplyConversion(bin.Left, left, leftPromoted, bin, ctx, diagnostics, requireImplicit: true);

            // right converted to int
            var intType = ctx.Compilation.GetSpecialType(SpecialType.System_Int32);
            right = ApplyConversion(bin.Right, right, intType, bin, ctx, diagnostics, requireImplicit: true);

            if (left.HasErrors || right.HasErrors) return new BoundBadExpression(bin);

            var constValue = FoldShiftConstant(op, leftPromoted, left, right);
            return new BoundBinaryExpression(bin, op, leftPromoted, left, right, constValue);
        }
        private static Optional<object> FoldShiftConstant(
            BoundBinaryOperatorKind op,
            TypeSymbol leftType,
            BoundExpression left,
            BoundExpression right)
        {
            if (!left.ConstantValueOpt.HasValue || !right.ConstantValueOpt.HasValue)
                return Optional<object>.None;

            if (right.ConstantValueOpt.Value is not int shift)
                return Optional<object>.None;

            int mask = leftType.SpecialType switch
            {
                SpecialType.System_Int32 or SpecialType.System_UInt32 => 0x1F,
                SpecialType.System_Int64 or SpecialType.System_UInt64 => 0x3F,
                SpecialType.System_IntPtr or SpecialType.System_UIntPtr => Cnidaria.Cs.TargetArchitecture.PointerSize == 4 ? 0x1F : 0x3F,
                _ => 0x1F
            };
            shift &= mask;

            object lv = left.ConstantValueOpt.Value!;
            switch (leftType.SpecialType)
            {
                case SpecialType.System_Int32:
                    if (lv is int i32)
                        return new Optional<object>(op switch
                        {
                            BoundBinaryOperatorKind.LeftShift => unchecked(i32 << shift),
                            BoundBinaryOperatorKind.RightShift => i32 >> shift,
                            BoundBinaryOperatorKind.UnsignedRightShift => (int)((uint)i32 >> shift),
                            _ => 0
                        });
                    break;

                case SpecialType.System_UInt32:
                    if (lv is uint u32)
                        return new Optional<object>(op switch
                        {
                            BoundBinaryOperatorKind.LeftShift => unchecked(u32 << shift),
                            BoundBinaryOperatorKind.RightShift or BoundBinaryOperatorKind.UnsignedRightShift => u32 >> shift,
                            _ => 0u
                        });
                    break;

                case SpecialType.System_Int64:
                    if (lv is long i64)
                        return new Optional<object>(op switch
                        {
                            BoundBinaryOperatorKind.LeftShift => unchecked(i64 << shift),
                            BoundBinaryOperatorKind.RightShift => i64 >> shift,
                            BoundBinaryOperatorKind.UnsignedRightShift => (long)((ulong)i64 >> shift),
                            _ => 0L
                        });
                    break;

                case SpecialType.System_UInt64:
                    if (lv is ulong u64)
                        return new Optional<object>(op switch
                        {
                            BoundBinaryOperatorKind.LeftShift => unchecked(u64 << shift),
                            BoundBinaryOperatorKind.RightShift or BoundBinaryOperatorKind.UnsignedRightShift => u64 >> shift,
                            _ => 0UL
                        });
                    break;

                case SpecialType.System_IntPtr:
                    if (Cnidaria.Cs.TargetArchitecture.PointerSize == 4 && lv is int ni32)
                        return new Optional<object>(op switch
                        {
                            BoundBinaryOperatorKind.LeftShift => unchecked(ni32 << shift),
                            BoundBinaryOperatorKind.RightShift => ni32 >> shift,
                            BoundBinaryOperatorKind.UnsignedRightShift => (int)((uint)ni32 >> shift),
                            _ => 0
                        });
                    if (Cnidaria.Cs.TargetArchitecture.PointerSize == 8 && (lv is long or int))
                    {
                        long ni64 = lv is long l ? l : (int)lv;
                        return new Optional<object>(op switch
                        {
                            BoundBinaryOperatorKind.LeftShift => unchecked(ni64 << shift),
                            BoundBinaryOperatorKind.RightShift => ni64 >> shift,
                            BoundBinaryOperatorKind.UnsignedRightShift => (long)((ulong)ni64 >> shift),
                            _ => 0L
                        });
                    }
                    break;

                case SpecialType.System_UIntPtr:
                    if (Cnidaria.Cs.TargetArchitecture.PointerSize == 4 && lv is uint nu32)
                        return new Optional<object>(op switch
                        {
                            BoundBinaryOperatorKind.LeftShift => unchecked(nu32 << shift),
                            BoundBinaryOperatorKind.RightShift or BoundBinaryOperatorKind.UnsignedRightShift => nu32 >> shift,
                            _ => 0u
                        });
                    if (Cnidaria.Cs.TargetArchitecture.PointerSize == 8 && (lv is ulong or uint))
                    {
                        ulong nu64 = lv is ulong ul ? ul : (uint)lv;
                        return new Optional<object>(op switch
                        {
                            BoundBinaryOperatorKind.LeftShift => unchecked(nu64 << shift),
                            BoundBinaryOperatorKind.RightShift or BoundBinaryOperatorKind.UnsignedRightShift => nu64 >> shift,
                            _ => 0UL
                        });
                    }
                    break;
            }
            return Optional<object>.None;
        }
        private static TypeSymbol? GetBinaryIntegralPromotionType(
            Compilation compilation,
            SyntaxTree tree,
            TypeSymbol left,
            TypeSymbol right,
            SyntaxNode diagnosticNode,
            DiagnosticBag diagnostics)
        {
            if (!IsIntegral(left.SpecialType) || !IsIntegral(right.SpecialType))
                return null;
            return GetBinaryNumericPromotionType(compilation, tree, left, right, diagnosticNode, diagnostics);
        }
        private static bool IsNumeric(SpecialType t) => t is
            SpecialType.System_Int8 or SpecialType.System_UInt8 or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_IntPtr or SpecialType.System_UIntPtr or
            SpecialType.System_Char or
            SpecialType.System_Single or SpecialType.System_Double or
            SpecialType.System_Decimal;

        private static bool IsIntegral(SpecialType t) => t is
            SpecialType.System_Int8 or SpecialType.System_UInt8 or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_IntPtr or SpecialType.System_UIntPtr or
            SpecialType.System_Char;
        private static bool IsEnumType(TypeSymbol t)
            => t is NamedTypeSymbol nt && nt.TypeKind == TypeKind.Enum;

        private static TypeSymbol GetEnumUnderlyingTypeOrDefault(Compilation compilation, TypeSymbol t)
        {
            if (t is NamedTypeSymbol nt && nt.TypeKind == TypeKind.Enum)
                return nt.EnumUnderlyingType ?? compilation.GetSpecialType(SpecialType.System_Int32);

            return t;
        }

        private static SpecialType GetEffectiveNumericSpecialType(TypeSymbol t)
        {
            if (t is NamedTypeSymbol nt && nt.TypeKind == TypeKind.Enum)
                return nt.EnumUnderlyingType?.SpecialType ?? SpecialType.System_Int32;

            return t.SpecialType;
        }
        private static Optional<object> FoldBitwiseConstant(
            BoundBinaryOperatorKind op,
            SpecialType type,
            BoundExpression left,
            BoundExpression right)
        {
            if (!left.ConstantValueOpt.HasValue || !right.ConstantValueOpt.HasValue)
                return Optional<object>.None;

            object lv = left.ConstantValueOpt.Value!;
            object rv = right.ConstantValueOpt.Value!;

            return type switch
            {
                SpecialType.System_Int8 when lv is sbyte a && rv is sbyte b => op switch
                {
                    BoundBinaryOperatorKind.BitwiseAnd => new Optional<object>((sbyte)(a & b)),
                    BoundBinaryOperatorKind.BitwiseOr => new Optional<object>((sbyte)(a | b)),
                    BoundBinaryOperatorKind.ExclusiveOr => new Optional<object>((sbyte)(a ^ b)),
                    _ => Optional<object>.None
                },

                SpecialType.System_UInt8 when lv is byte a && rv is byte b => op switch
                {
                    BoundBinaryOperatorKind.BitwiseAnd => new Optional<object>((byte)(a & b)),
                    BoundBinaryOperatorKind.BitwiseOr => new Optional<object>((byte)(a | b)),
                    BoundBinaryOperatorKind.ExclusiveOr => new Optional<object>((byte)(a ^ b)),
                    _ => Optional<object>.None
                },

                SpecialType.System_Int16 when lv is short a && rv is short b => op switch
                {
                    BoundBinaryOperatorKind.BitwiseAnd => new Optional<object>((short)(a & b)),
                    BoundBinaryOperatorKind.BitwiseOr => new Optional<object>((short)(a | b)),
                    BoundBinaryOperatorKind.ExclusiveOr => new Optional<object>((short)(a ^ b)),
                    _ => Optional<object>.None
                },

                SpecialType.System_UInt16 when lv is ushort a && rv is ushort b => op switch
                {
                    BoundBinaryOperatorKind.BitwiseAnd => new Optional<object>((ushort)(a & b)),
                    BoundBinaryOperatorKind.BitwiseOr => new Optional<object>((ushort)(a | b)),
                    BoundBinaryOperatorKind.ExclusiveOr => new Optional<object>((ushort)(a ^ b)),
                    _ => Optional<object>.None
                },

                SpecialType.System_Char when lv is char a && rv is char b => op switch
                {
                    BoundBinaryOperatorKind.BitwiseAnd => new Optional<object>((char)(a & b)),
                    BoundBinaryOperatorKind.BitwiseOr => new Optional<object>((char)(a | b)),
                    BoundBinaryOperatorKind.ExclusiveOr => new Optional<object>((char)(a ^ b)),
                    _ => Optional<object>.None
                },

                SpecialType.System_Int32 when lv is int a && rv is int b => op switch
                {
                    BoundBinaryOperatorKind.BitwiseAnd => new Optional<object>(a & b),
                    BoundBinaryOperatorKind.BitwiseOr => new Optional<object>(a | b),
                    BoundBinaryOperatorKind.ExclusiveOr => new Optional<object>(a ^ b),
                    _ => Optional<object>.None
                },

                SpecialType.System_UInt32 when lv is uint a && rv is uint b => op switch
                {
                    BoundBinaryOperatorKind.BitwiseAnd => new Optional<object>(a & b),
                    BoundBinaryOperatorKind.BitwiseOr => new Optional<object>(a | b),
                    BoundBinaryOperatorKind.ExclusiveOr => new Optional<object>(a ^ b),
                    _ => Optional<object>.None
                },

                SpecialType.System_Int64 when lv is long a && rv is long b => op switch
                {
                    BoundBinaryOperatorKind.BitwiseAnd => new Optional<object>(a & b),
                    BoundBinaryOperatorKind.BitwiseOr => new Optional<object>(a | b),
                    BoundBinaryOperatorKind.ExclusiveOr => new Optional<object>(a ^ b),
                    _ => Optional<object>.None
                },

                SpecialType.System_UInt64 when lv is ulong a && rv is ulong b => op switch
                {
                    BoundBinaryOperatorKind.BitwiseAnd => new Optional<object>(a & b),
                    BoundBinaryOperatorKind.BitwiseOr => new Optional<object>(a | b),
                    BoundBinaryOperatorKind.ExclusiveOr => new Optional<object>(a ^ b),
                    _ => Optional<object>.None
                },

                _ => Optional<object>.None
            };
        }
        private static bool IsSignedIntegral(SpecialType t) => t is
                SpecialType.System_Int8 or SpecialType.System_Int16 or
                SpecialType.System_Int32 or SpecialType.System_Int64;
        private static TypeSymbol? GetBinaryNumericPromotionType(
            Compilation compilation,
            SyntaxTree tree,
            BoundExpression left,
            BoundExpression right,
            SyntaxNode diagnosticNode,
            DiagnosticBag diagnostics)
        {
            var ls = left.Type.SpecialType;
            var rs = right.Type.SpecialType;

            bool IsNativeInt(SpecialType t) =>
                t is SpecialType.System_IntPtr or SpecialType.System_UIntPtr;

            if (IsNativeInt(ls) || IsNativeInt(rs))
            {
                var nativeExpr = IsNativeInt(ls) ? left : right;
                var otherExpr = ReferenceEquals(nativeExpr, left) ? right : left;
                var nativeType = nativeExpr.Type;

                var conv = ClassifyConversion(otherExpr, nativeType);

                if (conv.Kind == ConversionKind.ImplicitConstant)
                    return nativeType;

                if (nativeType.SpecialType == SpecialType.System_UIntPtr &&
                    conv.Kind == ConversionKind.ImplicitNumeric)
                {
                    return nativeType;
                }
            }

            if (ls == SpecialType.System_UInt64 || rs == SpecialType.System_UInt64)
            {
                var otherExpr = (ls == SpecialType.System_UInt64) ? right : left;

                if (IsSignedIntegral(otherExpr.Type.SpecialType))
                {
                    var u64 = compilation.GetSpecialType(SpecialType.System_UInt64);
                    var conv = ClassifyConversion(otherExpr, u64);

                    if (conv.Kind == ConversionKind.ImplicitConstant)
                        return u64;
                }
            }
            if (ls == SpecialType.System_UInt32 || rs == SpecialType.System_UInt32)
            {
                var otherExpr = (ls == SpecialType.System_UInt32) ? right : left;
                var otherSt = otherExpr.Type.SpecialType;

                if (otherSt is SpecialType.System_Int8 or SpecialType.System_Int16 or SpecialType.System_Int32)
                {
                    var u32 = compilation.GetSpecialType(SpecialType.System_UInt32);
                    var conv = ClassifyConversion(otherExpr, u32);

                    if (conv.Kind == ConversionKind.ImplicitConstant)
                        return u32;
                }
            }

            return GetBinaryNumericPromotionType(compilation, tree, left.Type, right.Type, diagnosticNode, diagnostics);
        }
        private static TypeSymbol? GetBinaryNumericPromotionType(
            Compilation compilation,
            SyntaxTree tree,
            TypeSymbol left,
            TypeSymbol right,
            SyntaxNode diagnosticNode,
            DiagnosticBag diagnostics)
        {
            var ls = left.SpecialType;
            var rs = right.SpecialType;

            bool IsNativeInt(SpecialType t) => t is SpecialType.System_IntPtr or SpecialType.System_UIntPtr;

            if (!IsNumeric(ls) || !IsNumeric(rs))
            {
                diagnostics.Add(new Diagnostic("CN_BIN001", DiagnosticSeverity.Error,
                    $"Operator requires numeric operands, got '{left.Name}' and '{right.Name}'.",
                    new Location(tree, diagnosticNode.Span)));
                return null;
            }
            if (IsNativeInt(ls) || IsNativeInt(rs))
            {
                if (ls is SpecialType.System_Decimal or SpecialType.System_Single or SpecialType.System_Double ||
                    rs is SpecialType.System_Decimal or SpecialType.System_Single or SpecialType.System_Double)
                {
                    diagnostics.Add(new Diagnostic("CN_BIN_PROMO_NATIVE001", DiagnosticSeverity.Error,
                        "Native-sized integers cannot be mixed with floating-point or decimal types without an explicit cast.",
                        new Location(tree, diagnosticNode.Span)));
                    return null;
                }

                if (ls == SpecialType.System_IntPtr || rs == SpecialType.System_IntPtr)
                {
                    var other = (ls == SpecialType.System_IntPtr) ? rs : ls;
                    if (other == SpecialType.System_IntPtr ||
                        other is SpecialType.System_Int8 or SpecialType.System_UInt8 or
                                 SpecialType.System_Int16 or SpecialType.System_UInt16 or
                                 SpecialType.System_Char or
                                 SpecialType.System_Int32)
                    {
                        return compilation.GetSpecialType(SpecialType.System_IntPtr);
                    }
                    if (other is SpecialType.System_Int64 or SpecialType.System_UInt32)
                        return compilation.GetSpecialType(SpecialType.System_Int64);
                }

                if (ls == SpecialType.System_UIntPtr || rs == SpecialType.System_UIntPtr)
                {
                    var other = (ls == SpecialType.System_UIntPtr) ? rs : ls;
                    if (other == SpecialType.System_UIntPtr ||
                        other is SpecialType.System_UInt8 or
                                 SpecialType.System_UInt16 or
                                 SpecialType.System_Char or
                                 SpecialType.System_UInt32)
                    {
                        return compilation.GetSpecialType(SpecialType.System_UIntPtr);
                    }
                }

                diagnostics.Add(new Diagnostic("CN_BIN_PROMO_NATIVE002", DiagnosticSeverity.Error,
                    "Native-sized integer operands require an explicit cast to a common type.",
                    new Location(tree, diagnosticNode.Span)));
                return null;
            }
            if (ls == SpecialType.System_Decimal || rs == SpecialType.System_Decimal)
            {
                if (ls is SpecialType.System_Single or SpecialType.System_Double ||
                    rs is SpecialType.System_Single or SpecialType.System_Double)
                {
                    diagnostics.Add(new Diagnostic("CN_BIN_PROMO_DEC001", DiagnosticSeverity.Error,
                        "decimal cannot be mixed with float/double in binary numeric promotion.",
                        new Location(tree, diagnosticNode.Span)));
                    return null;
                }
                return compilation.GetSpecialType(SpecialType.System_Decimal);
            }

            if (ls == SpecialType.System_Double || rs == SpecialType.System_Double)
                return compilation.GetSpecialType(SpecialType.System_Double);

            if (ls == SpecialType.System_Single || rs == SpecialType.System_Single)
                return compilation.GetSpecialType(SpecialType.System_Single);

            if (ls == SpecialType.System_UInt64 || rs == SpecialType.System_UInt64)
            {
                var other = (ls == SpecialType.System_UInt64) ? rs : ls;
                if (IsSignedIntegral(other))
                {
                    diagnostics.Add(new Diagnostic("CN_BIN_PROMO_ULONG001", DiagnosticSeverity.Error,
                        "ulong cannot be mixed with signed integral types in binary numeric promotion.",
                        new Location(tree, diagnosticNode.Span)));
                    return new ErrorTypeSymbol("ulong-mix", null, ImmutableArray<Location>.Empty);
                }
                return compilation.GetSpecialType(SpecialType.System_UInt64);
            }

            if (ls == SpecialType.System_Int64 || rs == SpecialType.System_Int64)
                return compilation.GetSpecialType(SpecialType.System_Int64);

            if (ls == SpecialType.System_UInt32 || rs == SpecialType.System_UInt32)
            {
                var other = (ls == SpecialType.System_UInt32) ? rs : ls;
                if (other is SpecialType.System_Int8 or SpecialType.System_Int16 or SpecialType.System_Int32)
                    return compilation.GetSpecialType(SpecialType.System_Int64);
                return compilation.GetSpecialType(SpecialType.System_UInt32);
            }

            return compilation.GetSpecialType(SpecialType.System_Int32);
        }
        private BoundExpression BindNullCoalescing(
           BinaryExpressionSyntax bin,
           BoundExpression left,
           BoundExpression right,
           BindingContext ctx,
           DiagnosticBag diagnostics)
        {
            if (left.HasErrors || right.HasErrors)
                return new BoundBadExpression(bin);

            var tree = ctx.SemanticModel.SyntaxTree;
            var compilation = ctx.Compilation;
            var boolType = compilation.GetSpecialType(SpecialType.System_Boolean);

            // Evaluate lhs once
            var tmp = NewTemp("$coalesce_tmp", left.Type);
            var tmpDecl = new BoundLocalDeclarationStatement(bin, tmp, left);
            var tmpExpr = new BoundLocalExpression(bin, tmp);

            // Nullable<T> case
            if (TryGetSystemNullableInfo(left.Type, out var leftNullable, out var underlying))
            {
                var hasValueGet = FindNullableHasValueGetter(leftNullable);
                var gv = FindNullableGetValueOrDefault(leftNullable, underlying);

                if (hasValueGet is null || gv is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_COALESCE_NULLABLE000",
                        DiagnosticSeverity.Error,
                        "Missing Nullable<T> members (HasValue / GetValueOrDefault).",
                        new Location(tree, bin.Span)));
                    return new BoundBadExpression(bin);
                }

                var cond = new BoundCallExpression(bin, tmpExpr, hasValueGet, ImmutableArray<BoundExpression>.Empty);
                if (TryGetSystemNullableInfo(right.Type, out var rightNullable, out var rightUnderlying)
                    && ReferenceEquals(rightUnderlying, underlying)
                    && ReferenceEquals(rightNullable.OriginalDefinition, leftNullable.OriginalDefinition))
                {
                    var resultType = left.Type;

                    var whenTrue = tmpExpr;
                    var whenFalse = ApplyConversion(
                        exprSyntax: bin.Right,
                        expr: right,
                        targetType: resultType,
                        diagnosticNode: bin,
                        context: ctx,
                        diagnostics: diagnostics,
                        requireImplicit: true);

                    if (whenFalse.HasErrors)
                        return new BoundBadExpression(bin);

                    var condSyntax = new ConditionalExpressionSyntax(
                        condition: bin.Left,
                        questionToken: default,
                        whenTrue: bin.Left,
                        colonToken: default,
                        whenFalse: bin.Right);

                    var value = new BoundConditionalExpression(condSyntax, resultType, cond, whenTrue, whenFalse, Optional<object>.None);

                    return new BoundSequenceExpression(
                        bin,
                        locals: ImmutableArray.Create(tmp),
                        sideEffects: ImmutableArray.Create<BoundStatement>(tmpDecl),
                        value: value);
                }
                var lhsValue = new BoundCallExpression(bin, tmpExpr, gv, ImmutableArray<BoundExpression>.Empty); // underlying

                var resultType2 = ClassifyConditionalResultType(
                    compilation,
                    tree,
                    underlying,
                    right.Type,
                    bin,
                    diagnostics);

                if (resultType2 is null || resultType2 is ErrorTypeSymbol || resultType2.SpecialType == SpecialType.System_Void)
                    return new BoundBadExpression(bin);

                var whenTrue2 = ApplyConversion(
                    exprSyntax: bin.Left,
                    expr: lhsValue,
                    targetType: resultType2,
                    diagnosticNode: bin,
                    context: ctx,
                    diagnostics: diagnostics,
                    requireImplicit: true);

                var whenFalse2 = ApplyConversion(
                    exprSyntax: bin.Right,
                    expr: right,
                    targetType: resultType2,
                    diagnosticNode: bin,
                    context: ctx,
                    diagnostics: diagnostics,
                    requireImplicit: true);

                if (whenTrue2.HasErrors || whenFalse2.HasErrors)
                    return new BoundBadExpression(bin);

                var condSyntax2 = new ConditionalExpressionSyntax(
                    condition: bin.Left,
                    questionToken: default,
                    whenTrue: bin.Left,
                    colonToken: default,
                    whenFalse: bin.Right);

                var value2 = new BoundConditionalExpression(condSyntax2, resultType2, cond, whenTrue2, whenFalse2, Optional<object>.None);

                return new BoundSequenceExpression(
                    bin,
                    locals: ImmutableArray.Create(tmp),
                    sideEffects: ImmutableArray.Create<BoundStatement>(tmpDecl),
                    value: value2);
            }
            // Reference type case
            if (!left.Type.IsReferenceType && left.Type is not NullTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_COALESCE000",
                    DiagnosticSeverity.Error,
                    "The left-hand side of '??' must be a reference type or a nullable value type.",
                    new Location(tree, bin.Left.Span)));
                return new BoundBadExpression(bin);
            }

            var resultTypeRef = ClassifyConditionalResultType(
                compilation,
                tree,
                left.Type,
                right.Type,
                bin,
                diagnostics);

            if (resultTypeRef is null || resultTypeRef is ErrorTypeSymbol || resultTypeRef.SpecialType == SpecialType.System_Void)
                return new BoundBadExpression(bin);

            var nullLit = new BoundLiteralExpression(bin, NullTypeSymbol.Instance, null);
            var condRef = new BoundBinaryExpression(
                bin,
                BoundBinaryOperatorKind.NotEquals,
                boolType,
                tmpExpr,
                nullLit,
                constantValueOpt: Optional<object>.None);

            var whenTrueRef = ApplyConversion(
                exprSyntax: bin.Left,
                expr: tmpExpr,
                targetType: resultTypeRef,
                diagnosticNode: bin,
                context: ctx,
                diagnostics: diagnostics,
                requireImplicit: true);

            var whenFalseRef = ApplyConversion(
                exprSyntax: bin.Right,
                expr: right,
                targetType: resultTypeRef,
                diagnosticNode: bin,
                context: ctx,
                diagnostics: diagnostics,
                requireImplicit: true);

            if (whenTrueRef.HasErrors || whenFalseRef.HasErrors)
                return new BoundBadExpression(bin);

            var condSyntaxRef = new ConditionalExpressionSyntax(
                condition: bin.Left,
                questionToken: default,
                whenTrue: bin.Left,
                colonToken: default,
                whenFalse: bin.Right);

            var valueRef = new BoundConditionalExpression(condSyntaxRef, resultTypeRef, condRef, whenTrueRef, whenFalseRef, Optional<object>.None);

            return new BoundSequenceExpression(
                bin,
                locals: ImmutableArray.Create(tmp),
                sideEffects: ImmutableArray.Create<BoundStatement>(tmpDecl),
                value: valueRef);
        }
        private LocalSymbol NewTemp(string prefix, TypeSymbol type)
        {
            var name = $"{prefix}{_tempId++}";
            return new LocalSymbol(name, _containing, type, ImmutableArray<Location>.Empty);
        }
        private void CollectPatternLocalsWhenTrue(BoundExpression condition, ImmutableArray<LocalSymbol>.Builder builder)
        {
            switch (condition)
            {
                case BoundIsPatternExpression isPattern
                    when isPattern.DeclaredLocalOpt is not null && !isPattern.IsNegated:
                    builder.Add(isPattern.DeclaredLocalOpt);
                    return;

                case BoundBinaryExpression bin
                    when bin.OperatorKind == BoundBinaryOperatorKind.LogicalAnd:
                    CollectPatternLocalsWhenTrue(bin.Left, builder);
                    CollectPatternLocalsWhenTrue(bin.Right, builder);
                    return;

                case BoundCheckedExpression chk:
                    CollectPatternLocalsWhenTrue(chk.Expression, builder);
                    return;

                case BoundUncheckedExpression unchk:
                    CollectPatternLocalsWhenTrue(unchk.Expression, builder);
                    return;

                case BoundConversionExpression conv
                    when conv.Conversion.Kind == ConversionKind.Identity &&
                         conv.Type.SpecialType == SpecialType.System_Boolean:
                    CollectPatternLocalsWhenTrue(conv.Operand, builder);
                    return;
            }
        }
        private void CollectPatternLocalsWhenFalse(BoundExpression condition, ImmutableArray<LocalSymbol>.Builder builder)
        {
            switch (condition)
            {
                case BoundIsPatternExpression isPattern
                    when isPattern.DeclaredLocalOpt is not null && isPattern.IsNegated:
                    builder.Add(isPattern.DeclaredLocalOpt);
                    return;

                case BoundCheckedExpression chk:
                    CollectPatternLocalsWhenFalse(chk.Expression, builder);
                    return;

                case BoundUncheckedExpression unchk:
                    CollectPatternLocalsWhenFalse(unchk.Expression, builder);
                    return;

                case BoundConversionExpression conv
                    when conv.Conversion.Kind == ConversionKind.Identity &&
                         conv.Type.SpecialType == SpecialType.System_Boolean:
                    CollectPatternLocalsWhenFalse(conv.Operand, builder);
                    return;
            }
        }
        private ImmutableArray<LocalSymbol> GetPatternLocalsWhenTrue(BoundExpression condition)
        {
            var builder = ImmutableArray.CreateBuilder<LocalSymbol>();
            CollectPatternLocalsWhenTrue(condition, builder);
            return builder.ToImmutable();
        }
        private ImmutableArray<LocalSymbol> GetPatternLocalsWhenFalse(BoundExpression condition)
        {
            var builder = ImmutableArray.CreateBuilder<LocalSymbol>();
            CollectPatternLocalsWhenFalse(condition, builder);
            return builder.ToImmutable();
        }
        private LocalScopeBinder CreateFlowScopeBinderForTrue(BoundExpression condition)
        {
            var locals = GetPatternLocalsWhenTrue(condition);
            if (locals.IsDefaultOrEmpty)
                return this;

            var scope = new LocalScopeBinder(parent: this, flags: Flags, containing: _containing);
            for (int i = 0; i < locals.Length; i++)
                scope.ImportFlowingLocal(locals[i]);
            return scope;
        }
        private LocalScopeBinder CreateFlowScopeBinderForFalse(BoundExpression condition)
        {
            var locals = GetPatternLocalsWhenFalse(condition);
            if (locals.IsDefaultOrEmpty)
                return this;

            var scope = new LocalScopeBinder(parent: this, flags: Flags, containing: _containing);
            for (int i = 0; i < locals.Length; i++)
                scope.ImportFlowingLocal(locals[i]);
            return scope;
        }
        private static bool StatementDefinitelyDoesNotComplete(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundReturnStatement:
                case BoundThrowStatement:
                case BoundGotoStatement:
                    return true;

                case BoundBlockStatement block:
                    return block.Statements.Length != 0 &&
                           StatementDefinitelyDoesNotComplete(block.Statements[block.Statements.Length - 1]);

                case BoundIfStatement @if when @if.ElseOpt is not null:
                    return StatementDefinitelyDoesNotComplete(@if.Then) &&
                           StatementDefinitelyDoesNotComplete(@if.ElseOpt);

                case BoundCheckedStatement chk:
                    return StatementDefinitelyDoesNotComplete(chk.Statement);

                case BoundUncheckedStatement unchk:
                    return StatementDefinitelyDoesNotComplete(unchk.Statement);

                default:
                    return false;
            }
        }
        private LocalScopeBinder GetFlowScopeBinderForFollowingStatements(BoundStatement statement)
        {
            if (statement is BoundIfStatement @if &&
                @if.ElseOpt is null &&
                StatementDefinitelyDoesNotComplete(@if.Then))
            {
                return CreateFlowScopeBinderForFalse(@if.Condition);
            }

            return this;
        }
        private BoundExpression BindConditional(ConditionalExpressionSyntax node, BindingContext ctx, DiagnosticBag diagnostics)
        {
            var boolType = ctx.Compilation.GetSpecialType(SpecialType.System_Boolean);

            var condition = BindExpression(node.Condition, ctx, diagnostics);
            condition = ApplyConversion(node.Condition, condition, boolType, node, ctx, diagnostics, requireImplicit: true);

            var whenTrueBinder = CreateFlowScopeBinderForTrue(condition);
            var whenTrue = whenTrueBinder.BindExpression(node.WhenTrue, ctx, diagnostics);
            var whenFalse = BindExpression(node.WhenFalse, ctx, diagnostics);

            if (condition.HasErrors || whenTrue.HasErrors || whenFalse.HasErrors)
                return new BoundBadExpression(node);

            var resultType = ClassifyConditionalResultType(
                ctx.Compilation, ctx.SemanticModel.SyntaxTree, whenTrue.Type, whenFalse.Type, node, diagnostics);
            if (resultType is null || resultType is ErrorTypeSymbol || resultType.SpecialType == SpecialType.System_Void)
                return new BoundBadExpression(node);

            whenTrue = ApplyConversion(node.WhenTrue, whenTrue, resultType, node, ctx, diagnostics, requireImplicit: true);
            whenFalse = ApplyConversion(node.WhenFalse, whenFalse, resultType, node, ctx, diagnostics, requireImplicit: true);

            if (whenTrue.HasErrors || whenFalse.HasErrors)
                return new BoundBadExpression(node);

            var cv = FoldConditionalConstant(condition, whenTrue, whenFalse);
            return new BoundConditionalExpression(node, resultType, condition, whenTrue, whenFalse, cv);
        }
        private static TypeSymbol? ClassifyConditionalResultType(
            Compilation compilation,
            SyntaxTree tree,
            TypeSymbol t1,
            TypeSymbol t2,
            SyntaxNode diagNode,
            DiagnosticBag diagnostics)
        {
            if (ReferenceEquals(t1, t2))
                return t1;

            if (t1 is ThrowTypeSymbol) return t2;
            if (t2 is ThrowTypeSymbol) return t1;

            // null + ref => ref
            if (t1 is NullTypeSymbol && t2.IsReferenceType) return t2;
            if (t2 is NullTypeSymbol && t1.IsReferenceType) return t1;

            // numeric => use binary numeric promotions
            if (IsNumeric(t1.SpecialType) && IsNumeric(t2.SpecialType))
                return GetBinaryNumericPromotionType(compilation, tree, t1, t2, diagNode, diagnostics);

            // reference
            if (t1.IsReferenceType && t2.IsReferenceType)
            {
                if (HasImplicitReferenceConversion(t2, t1)) return t1;
                if (HasImplicitReferenceConversion(t1, t2)) return t2;

                diagnostics.Add(new Diagnostic("CN_COND_REF000", DiagnosticSeverity.Error,
                    $"Cannot determine common type for '{t1.Name}' and '{t2.Name}'.",
                    new Location(tree, diagNode.Span)));
                return null;
            }

            diagnostics.Add(new Diagnostic("CN_COND000", DiagnosticSeverity.Error,
                $"Cannot determine type of conditional expression for '{t1.Name}' and '{t2.Name}'.",
                new Location(tree, diagNode.Span)));
            return null;


        }
        private static Optional<object> FoldConditionalConstant(BoundExpression condition, BoundExpression whenTrue, BoundExpression whenFalse)
        {
            if (condition.ConstantValueOpt.HasValue && condition.ConstantValueOpt.Value is bool b)
            {
                var chosen = b ? whenTrue : whenFalse;
                if (chosen.ConstantValueOpt.HasValue)
                    return chosen.ConstantValueOpt;
            }
            return Optional<object>.None;
        }
        private static bool IsBaseTypeOf(TypeSymbol baseType, TypeSymbol derived)
        {
            for (var t = derived; t != null; t = t.BaseType)
                if (ReferenceEquals(t, baseType)) return true;
            return false;
        }
        private static Optional<object> FoldBooleanBinaryConstant(
            BoundBinaryOperatorKind op,
            BoundExpression left,
            BoundExpression right)
        {
            if (!left.ConstantValueOpt.HasValue || !right.ConstantValueOpt.HasValue)
                return Optional<object>.None;

            object? lv = left.ConstantValueOpt.Value;
            object? rv = right.ConstantValueOpt.Value;

            if (op == BoundBinaryOperatorKind.LogicalAnd && lv is bool lb1 && rv is bool rb1)
                return new Optional<object>(lb1 && rb1);

            if (op == BoundBinaryOperatorKind.LogicalOr && lv is bool lb2 && rv is bool rb2)
                return new Optional<object>(lb2 || rb2);

            if (TryFoldComparisonOrEquality(op, lv, rv, out bool result))
                return new Optional<object>(result);

            return Optional<object>.None;
        }
        private static bool TryFoldComparisonOrEquality(
            BoundBinaryOperatorKind op,
            object? lv,
            object? rv,
            out bool result)
        {
            // null == null / null != null
            if (lv is null || rv is null)
            {
                result = op switch
                {
                    BoundBinaryOperatorKind.Equals => Equals(lv, rv),
                    BoundBinaryOperatorKind.NotEquals => !Equals(lv, rv),
                    _ => default
                };

                return op is BoundBinaryOperatorKind.Equals or BoundBinaryOperatorKind.NotEquals;
            }

            // bool
            if (lv is bool b1 && rv is bool b2)
            {
                result = op switch
                {
                    BoundBinaryOperatorKind.Equals => b1 == b2,
                    BoundBinaryOperatorKind.NotEquals => b1 != b2,
                    _ => default
                };
                return op is BoundBinaryOperatorKind.Equals or BoundBinaryOperatorKind.NotEquals;
            }

            // string
            if (lv is string s1 && rv is string s2)
            {
                result = op switch
                {
                    BoundBinaryOperatorKind.Equals => s1 == s2,
                    BoundBinaryOperatorKind.NotEquals => s1 != s2,
                    _ => default
                };
                return op is BoundBinaryOperatorKind.Equals or BoundBinaryOperatorKind.NotEquals;
            }

            // float/double
            if (lv is float f1 && rv is float f2)
            {
                result = op switch
                {
                    BoundBinaryOperatorKind.Equals => f1 == f2,
                    BoundBinaryOperatorKind.NotEquals => f1 != f2,
                    BoundBinaryOperatorKind.LessThan => f1 < f2,
                    BoundBinaryOperatorKind.LessThanOrEqual => f1 <= f2,
                    BoundBinaryOperatorKind.GreaterThan => f1 > f2,
                    BoundBinaryOperatorKind.GreaterThanOrEqual => f1 >= f2,
                    _ => default
                };
                return op is BoundBinaryOperatorKind.Equals
                    or BoundBinaryOperatorKind.NotEquals
                    or BoundBinaryOperatorKind.LessThan
                    or BoundBinaryOperatorKind.LessThanOrEqual
                    or BoundBinaryOperatorKind.GreaterThan
                    or BoundBinaryOperatorKind.GreaterThanOrEqual;
            }
            if (lv is double d1 && rv is double d2)
            {
                result = op switch
                {
                    BoundBinaryOperatorKind.Equals => d1 == d2,
                    BoundBinaryOperatorKind.NotEquals => d1 != d2,
                    BoundBinaryOperatorKind.LessThan => d1 < d2,
                    BoundBinaryOperatorKind.LessThanOrEqual => d1 <= d2,
                    BoundBinaryOperatorKind.GreaterThan => d1 > d2,
                    BoundBinaryOperatorKind.GreaterThanOrEqual => d1 >= d2,
                    _ => default
                };
                return op is BoundBinaryOperatorKind.Equals
                    or BoundBinaryOperatorKind.NotEquals
                    or BoundBinaryOperatorKind.LessThan
                    or BoundBinaryOperatorKind.LessThanOrEqual
                    or BoundBinaryOperatorKind.GreaterThan
                    or BoundBinaryOperatorKind.GreaterThanOrEqual;
            }

            // Integral / decimal / char
            if (lv is sbyte i8a && rv is sbyte i8b) return TryFoldOrdered(op, i8a.CompareTo(i8b), out result);
            if (lv is byte u8a && rv is byte u8b) return TryFoldOrdered(op, u8a.CompareTo(u8b), out result);
            if (lv is short i16a && rv is short i16b) return TryFoldOrdered(op, i16a.CompareTo(i16b), out result);
            if (lv is ushort u16a && rv is ushort u16b) return TryFoldOrdered(op, u16a.CompareTo(u16b), out result);
            if (lv is char ca && rv is char cb) return TryFoldOrdered(op, ca.CompareTo(cb), out result);
            if (lv is int i32a && rv is int i32b) return TryFoldOrdered(op, i32a.CompareTo(i32b), out result);
            if (lv is uint u32a && rv is uint u32b) return TryFoldOrdered(op, u32a.CompareTo(u32b), out result);
            if (lv is long i64a && rv is long i64b) return TryFoldOrdered(op, i64a.CompareTo(i64b), out result);
            if (lv is ulong u64a && rv is ulong u64b) return TryFoldOrdered(op, u64a.CompareTo(u64b), out result);
            if (lv is decimal ma && rv is decimal mb) return TryFoldOrdered(op, ma.CompareTo(mb), out result);

            result = default;
            return false;
        }
        private static bool TryFoldOrdered(BoundBinaryOperatorKind op, int cmp, out bool result)
        {
            result = op switch
            {
                BoundBinaryOperatorKind.Equals => cmp == 0,
                BoundBinaryOperatorKind.NotEquals => cmp != 0,
                BoundBinaryOperatorKind.LessThan => cmp < 0,
                BoundBinaryOperatorKind.LessThanOrEqual => cmp <= 0,
                BoundBinaryOperatorKind.GreaterThan => cmp > 0,
                BoundBinaryOperatorKind.GreaterThanOrEqual => cmp >= 0,
                _ => default
            };

            return op is BoundBinaryOperatorKind.Equals
                or BoundBinaryOperatorKind.NotEquals
                or BoundBinaryOperatorKind.LessThan
                or BoundBinaryOperatorKind.LessThanOrEqual
                or BoundBinaryOperatorKind.GreaterThan
                or BoundBinaryOperatorKind.GreaterThanOrEqual;
        }
        private static Optional<object> FoldBinaryConstant(
            BoundBinaryOperatorKind op,
            TypeSymbol type,
            BoundExpression left,
            BoundExpression right,
            bool isChecked)
        {
            if (!left.ConstantValueOpt.HasValue || !right.ConstantValueOpt.HasValue)
                return Optional<object>.None;

            try
            {
                switch (type.SpecialType)
                {
                    case SpecialType.System_Int32:
                        if (left.ConstantValueOpt.Value is int li && right.ConstantValueOpt.Value is int ri)
                        {
                            return op switch
                            {
                                BoundBinaryOperatorKind.Add => new Optional<object>(isChecked ? checked(li + ri) : unchecked(li + ri)),
                                BoundBinaryOperatorKind.Subtract => new Optional<object>(isChecked ? checked(li - ri) : unchecked(li - ri)),
                                BoundBinaryOperatorKind.Multiply => new Optional<object>(isChecked ? checked(li * ri) : unchecked(li * ri)),
                                BoundBinaryOperatorKind.Divide => new Optional<object>(isChecked ? checked(li / ri) : unchecked(li / ri)),
                                BoundBinaryOperatorKind.Modulo => new Optional<object>(isChecked ? checked(li % ri) : unchecked(li % ri)),
                                _ => Optional<object>.None
                            };
                        }
                        break;

                    case SpecialType.System_UInt32:
                        if (left.ConstantValueOpt.Value is uint lui && right.ConstantValueOpt.Value is uint rui)
                        {
                            return op switch
                            {
                                BoundBinaryOperatorKind.Add => new Optional<object>(isChecked ? checked(lui + rui) : unchecked(lui + rui)),
                                BoundBinaryOperatorKind.Subtract => new Optional<object>(isChecked ? checked(lui - rui) : unchecked(lui - rui)),
                                BoundBinaryOperatorKind.Multiply => new Optional<object>(isChecked ? checked(lui * rui) : unchecked(lui * rui)),
                                BoundBinaryOperatorKind.Divide => new Optional<object>(lui / rui),
                                BoundBinaryOperatorKind.Modulo => new Optional<object>(lui % rui),
                                _ => Optional<object>.None
                            };
                        }
                        break;

                    case SpecialType.System_Int64:
                        if (left.ConstantValueOpt.Value is long ll && right.ConstantValueOpt.Value is long rl)
                        {
                            return op switch
                            {
                                BoundBinaryOperatorKind.Add => new Optional<object>(isChecked ? checked(ll + rl) : unchecked(ll + rl)),
                                BoundBinaryOperatorKind.Subtract => new Optional<object>(isChecked ? checked(ll - rl) : unchecked(ll - rl)),
                                BoundBinaryOperatorKind.Multiply => new Optional<object>(isChecked ? checked(ll * rl) : unchecked(ll * rl)),
                                BoundBinaryOperatorKind.Divide => new Optional<object>(isChecked ? checked(ll / rl) : unchecked(ll / rl)),
                                BoundBinaryOperatorKind.Modulo => new Optional<object>(isChecked ? checked(ll % rl) : unchecked(ll % rl)),
                                _ => Optional<object>.None
                            };
                        }
                        break;

                    case SpecialType.System_UInt64:
                        if (left.ConstantValueOpt.Value is ulong lul && right.ConstantValueOpt.Value is ulong rul)
                        {
                            return op switch
                            {
                                BoundBinaryOperatorKind.Add => new Optional<object>(isChecked ? checked(lul + rul) : unchecked(lul + rul)),
                                BoundBinaryOperatorKind.Subtract => new Optional<object>(isChecked ? checked(lul - rul) : unchecked(lul - rul)),
                                BoundBinaryOperatorKind.Multiply => new Optional<object>(isChecked ? checked(lul * rul) : unchecked(lul * rul)),
                                BoundBinaryOperatorKind.Divide => new Optional<object>(lul / rul),
                                BoundBinaryOperatorKind.Modulo => new Optional<object>(lul % rul),
                                _ => Optional<object>.None
                            };
                        }
                        break;

                    case SpecialType.System_Single:
                        if (left.ConstantValueOpt.Value is float lf && right.ConstantValueOpt.Value is float rf)
                        {
                            return op switch
                            {
                                BoundBinaryOperatorKind.Add => new Optional<object>(lf + rf),
                                BoundBinaryOperatorKind.Subtract => new Optional<object>(lf - rf),
                                BoundBinaryOperatorKind.Multiply => new Optional<object>(lf * rf),
                                BoundBinaryOperatorKind.Divide => new Optional<object>(lf / rf),
                                BoundBinaryOperatorKind.Modulo => new Optional<object>(lf % rf),
                                _ => Optional<object>.None
                            };
                        }
                        break;

                    case SpecialType.System_Double:
                        if (left.ConstantValueOpt.Value is double ld && right.ConstantValueOpt.Value is double rd)
                        {
                            return op switch
                            {
                                BoundBinaryOperatorKind.Add => new Optional<object>(ld + rd),
                                BoundBinaryOperatorKind.Subtract => new Optional<object>(ld - rd),
                                BoundBinaryOperatorKind.Multiply => new Optional<object>(ld * rd),
                                BoundBinaryOperatorKind.Divide => new Optional<object>(ld / rd),
                                BoundBinaryOperatorKind.Modulo => new Optional<object>(ld % rd),
                                _ => Optional<object>.None
                            };
                        }
                        break;
                }
            }
            catch (OverflowException)
            {
                return Optional<object>.None;
            }
            catch (DivideByZeroException)
            {
                return Optional<object>.None;
            }

            return Optional<object>.None;
        }
        private BoundExpression BindLiteral(LiteralExpressionSyntax lit, BindingContext context, DiagnosticBag diagnostics)
        {
            var token = lit.LiteralToken;
            var value = token.Value;

            if (token.Kind == SyntaxKind.NullKeyword)
            {
                return new BoundLiteralExpression(lit, NullTypeSymbol.Instance, null);
            }
            if (lit.Kind == SyntaxKind.DefaultLiteralExpression || token.Kind == SyntaxKind.DefaultKeyword)
            {
                return new BoundLiteralExpression(lit, DefaultLiteralTypeSymbol.Instance, null);
            }
            if (value is null)
            {
                diagnostics.Add(new Diagnostic("CN_BIND004", DiagnosticSeverity.Error,
                    "Invalid literal.",
                    new Location(context.SemanticModel.SyntaxTree, lit.Span)));
                return new BoundBadExpression(lit);
            }

            if (value is int)
            {
                var t = context.Compilation.GetSpecialType(SpecialType.System_Int32);
                return new BoundLiteralExpression(lit, t, value);
            }

            if (value is uint)
            {
                var t = context.Compilation.GetSpecialType(SpecialType.System_UInt32);
                return new BoundLiteralExpression(lit, t, value);
            }

            if (value is long)
            {
                var t = context.Compilation.GetSpecialType(SpecialType.System_Int64);
                return new BoundLiteralExpression(lit, t, value);
            }

            if (value is ulong)
            {
                var t = context.Compilation.GetSpecialType(SpecialType.System_UInt64);
                return new BoundLiteralExpression(lit, t, value);
            }

            if (value is float)
            {
                var t = context.Compilation.GetSpecialType(SpecialType.System_Single);
                return new BoundLiteralExpression(lit, t, value);
            }

            if (value is double)
            {
                var t = context.Compilation.GetSpecialType(SpecialType.System_Double);
                return new BoundLiteralExpression(lit, t, value);
            }

            if (value is decimal)
            {
                var t = context.Compilation.GetSpecialType(SpecialType.System_Decimal);
                return new BoundLiteralExpression(lit, t, value);
            }

            if (value is string)
            {
                var t = context.Compilation.GetSpecialType(SpecialType.System_String);
                return new BoundLiteralExpression(lit, t, value);
            }

            if (value is bool)
            {
                var t = context.Compilation.GetSpecialType(SpecialType.System_Boolean);
                return new BoundLiteralExpression(lit, t, value);
            }

            if (value is char)
            {
                var t = context.Compilation.GetSpecialType(SpecialType.System_Char);
                return new BoundLiteralExpression(lit, t, value);
            }

            diagnostics.Add(new Diagnostic("CN_BIND004", DiagnosticSeverity.Error,
                $"Unsupported literal: {value.GetType().Name}",
                new Location(context.SemanticModel.SyntaxTree, lit.Span)));

            return new BoundBadExpression(lit);
        }
        private static bool HasModifier(SyntaxTokenList mods, SyntaxKind kind)
        {
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (m.Kind == kind)
                    return true;
                if (m.Kind == SyntaxKind.IdentifierToken && m.ContextualKind == kind)
                    return true;
            }
            return false;
        }
    }
}
