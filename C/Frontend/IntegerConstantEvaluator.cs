using System;

namespace Cnidaria.C;

/// <summary>Evaluates integer constant expressions in the type C gives each operand</summary>
internal static class IntegerConstantEvaluator
{
    public static bool TryEvaluate(BoundExpression expression, TargetInfo target, out long value)
    {
        value = 0;
        if (!TryEvaluateExact(expression, target, out var exact))
            return false;

        var type = expression.Type;
        if (!GimpleTypes.IsIntegerLike(type))
        {
            if (exact < long.MinValue || exact > ulong.MaxValue)
                return false;
            value = unchecked((long)(ulong)(exact & ulong.MaxValue));
            return true;
        }

        if (type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Bool })
        {
            value = exact != 0 ? 1 : 0;
            return true;
        }

        var bits = Math.Min(64, target.SizeOf(type) * 8);
        var signed = GimpleTypes.IsSigned(type, target);
        var low = (ulong)(exact & ulong.MaxValue);
        if (bits < 64)
        {
            var mask = (1UL << bits) - 1;
            low &= mask;
            if (signed && (low >> (bits - 1)) != 0)
                low |= ~mask;
        }

        value = unchecked((long)low);
        return true;
    }

    public static bool? TryGetTruth(BoundExpression expression, TargetInfo target)
    {
        if (TryEvaluate(expression, target, out var integer))
            return integer != 0;

        while (expression is BoundConversionExpression or BoundParenthesizedExpression or BoundCastExpression)
        {
            expression = expression switch
            {
                BoundConversionExpression conversion => conversion.Expression,
                BoundParenthesizedExpression parenthesized => parenthesized.Expression,
                _ => ((BoundCastExpression)expression).Expression,
            };
        }

        return expression.ConstantValue switch
        {
            bool boolean => boolean,
            float single => single != 0,
            double dbl => dbl != 0,
            decimal dec => dec != 0,
            _ => null,
        };
    }

    public static bool TryConvertConstantToLong(object? constantValue, out long value)
    {
        switch (constantValue)
        {
            case byte byteValue:
                value = byteValue;
                return true;
            case sbyte signedByteValue:
                value = signedByteValue;
                return true;
            case short shortValue:
                value = shortValue;
                return true;
            case ushort unsignedShortValue:
                value = unsignedShortValue;
                return true;
            case int intValue:
                value = intValue;
                return true;
            case uint unsignedIntValue:
                value = unsignedIntValue;
                return true;
            case long longValue:
                value = longValue;
                return true;
            case ulong unsignedLongValue when unsignedLongValue <= long.MaxValue:
                value = (long)unsignedLongValue;
                return true;
            case char charValue:
                value = charValue;
                return true;
            case bool boolValue:
                value = boolValue ? 1 : 0;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static bool TryEvaluateExact(BoundExpression expression, TargetInfo target, out Int128 value)
    {
        value = 0;
        switch (expression)
        {
            case BoundConversionExpression conversion:
                return TryEvaluateOperand(conversion.Expression, target, out value);

            case BoundParenthesizedExpression parenthesized:
                return TryEvaluateOperand(parenthesized.Expression, target, out value);

            case BoundCastExpression cast:
                return TryEvaluateOperand(cast.Expression, target, out value);

            case BoundUnaryExpression unary:
                return TryEvaluateUnary(unary, target, out value);

            case BoundBinaryExpression binary:
                return TryEvaluateBinary(binary, target, out value);

            case BoundConditionalExpression conditional:
                return TryGetTruth(conditional.Condition, target) is { } condition &&
                    TryEvaluateOperand(condition ? conditional.WhenTrue : conditional.WhenFalse, target, out value);

            default:
                if (expression.ConstantValue is ulong unsignedLong)
                {
                    value = unsignedLong;
                    return true;
                }

                if (!TryConvertConstantToLong(expression.ConstantValue, out var constant))
                    return false;
                value = constant;
                return true;
        }
    }

    private static bool TryEvaluateOperand(BoundExpression expression, TargetInfo target, out Int128 value)
    {
        value = 0;
        if (!TryEvaluate(expression, target, out var bits))
            return false;

        var type = expression.Type;
        value = GimpleTypes.IsIntegerLike(type) && !GimpleTypes.IsSigned(type, target) && target.SizeOf(type) >= 8
            ? unchecked((ulong)bits)
            : bits;
        return true;
    }

    private static bool TryEvaluateUnary(BoundUnaryExpression expression, TargetInfo target, out Int128 value)
    {
        value = 0;
        if (!TryEvaluateOperand(expression.Operand, target, out var operand))
            return false;

        switch (expression.OperatorToken.Kind)
        {
            case SyntaxKind.PlusToken:
                value = operand;
                return true;
            case SyntaxKind.MinusToken:
                value = -operand;
                return true;
            case SyntaxKind.TildeToken:
                value = ~operand;
                return true;
            case SyntaxKind.BangToken:
                value = operand == 0 ? 1 : 0;
                return true;
            default:
                return false;
        }
    }

    private static bool TryEvaluateBinary(BoundBinaryExpression expression, TargetInfo target, out Int128 value)
    {
        value = 0;
        var kind = expression.OperatorToken.Kind;
        if (!TryEvaluateOperand(expression.Left, target, out var left))
            return false;

        if (kind is SyntaxKind.AmpersandAmpersandToken or SyntaxKind.PipePipeToken &&
            (left != 0) == (kind == SyntaxKind.PipePipeToken))
        {
            value = left != 0 ? 1 : 0;
            return true;
        }

        if (!TryEvaluateOperand(expression.Right, target, out var right))
            return false;

        switch (kind)
        {
            case SyntaxKind.StarToken:
                value = left * right;
                return true;
            case SyntaxKind.SlashToken:
                if (right == 0)
                    return false;
                value = left / right;
                return true;
            case SyntaxKind.PercentToken:
                if (right == 0)
                    return false;
                value = left % right;
                return true;
            case SyntaxKind.PlusToken:
                value = left + right;
                return true;
            case SyntaxKind.MinusToken:
                value = left - right;
                return true;
            case SyntaxKind.LessThanLessThanToken:
            case SyntaxKind.GreaterThanGreaterThanToken:
                if (right < 0 || right >= 64)
                    return false;
                value = kind == SyntaxKind.LessThanLessThanToken ? left << (int)right : left >> (int)right;
                return true;
            case SyntaxKind.LessThanToken:
                value = left < right ? 1 : 0;
                return true;
            case SyntaxKind.LessThanEqualsToken:
                value = left <= right ? 1 : 0;
                return true;
            case SyntaxKind.GreaterThanToken:
                value = left > right ? 1 : 0;
                return true;
            case SyntaxKind.GreaterThanEqualsToken:
                value = left >= right ? 1 : 0;
                return true;
            case SyntaxKind.EqualsEqualsToken:
                value = left == right ? 1 : 0;
                return true;
            case SyntaxKind.BangEqualsToken:
                value = left != right ? 1 : 0;
                return true;
            case SyntaxKind.AmpersandToken:
                value = left & right;
                return true;
            case SyntaxKind.PipeToken:
                value = left | right;
                return true;
            case SyntaxKind.HatToken:
                value = left ^ right;
                return true;
            case SyntaxKind.AmpersandAmpersandToken:
            case SyntaxKind.PipePipeToken:
                value = right != 0 ? 1 : 0;
                return true;
            case SyntaxKind.CommaToken:
                value = right;
                return true;
            default:
                return false;
        }
    }
}
