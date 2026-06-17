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
        private Conversion ClassifyConversion(BoundExpression expr, TypeSymbol target, BindingContext context)
        {
            if (expr is BoundOutVarPendingExpression)
                return ClassifyOutVarPendingConversion(target);
            if (expr is BoundOutDiscardExpression discard)
                return ClassifyOutDiscardConversion(discard, target);
            if (expr is BoundUnboundImplicitObjectCreationExpression unbound)
                return CanBindTargetTypedObjectCreation(unbound, target, context)
                    ? new Conversion(ConversionKind.Identity)
                    : new Conversion(ConversionKind.None);
            if (expr is BoundUnboundCollectionExpression unboundCollection)
                return CanBindTargetTypedCollectionExpression(unboundCollection, target, context)
                    ? new Conversion(ConversionKind.Identity)
                    : new Conversion(ConversionKind.None);
            if (expr is BoundUnboundLambdaExpression unboundLambda)
                return CanConvertLambdaToDelegate(unboundLambda, target, context)
                    ? new Conversion(ConversionKind.Identity)
                    : new Conversion(ConversionKind.None);
            if (expr is BoundMethodGroupExpression methodGroup)
                return CanConvertMethodGroupToDelegate(methodGroup, target, context)
                    ? new Conversion(ConversionKind.Identity)
                    : new Conversion(ConversionKind.None);
            if (expr is BoundStackAllocArrayCreationExpression sa &&
                TryGetSpanLikeElementType(target, out _, out var spanElemType) &&
                ReferenceEquals(sa.ElementType, spanElemType))
            {
                return new Conversion(ConversionKind.ImplicitStackAlloc);
            }
            var standard = ClassifyConversion(expr, target);
            if (standard.Exists)
                return standard;
            return ClassifyUserDefinedConversion(expr, target, context);
        }
        private Conversion ClassifyUserDefinedConversion(BoundExpression expr, TypeSymbol target, BindingContext context)
        {
            if (expr.Type is not NamedTypeSymbol && target is not NamedTypeSymbol)
                return new Conversion(ConversionKind.None);
            if (expr.Syntax is not ExpressionSyntax exprSyntax)
                return new Conversion(ConversionKind.None);

            var metadataNames = GetConversionOperatorMetadataNames(IsCheckedOverflowContext);
            var candidates = LookupUserDefinedOperatorMethods(
                leftType: expr.Type,
                rightType: target,
                metadataNames: metadataNames,
                parameterCount: 1,
                context: context);

            if (candidates.IsDefaultOrEmpty)
                return new Conversion(ConversionKind.None);

            MethodSymbol? best = null;
            bool bestImplicit = false;
            int bestScore = int.MaxValue;
            bool ambiguous = false;

            for (int i = 0; i < candidates.Length; i++)
            {
                var m = candidates[i];
                if (m.Parameters.Length != 1)
                    continue;
                if (m.ReturnType.SpecialType == SpecialType.System_Void)
                    continue;

                bool opIsImplicit = string.Equals(m.Name, $"{OperatorPrefix}Implicit", StringComparison.Ordinal);
                bool opIsExplicit =
                    string.Equals(m.Name, $"{OperatorPrefix}Explicit", StringComparison.Ordinal) ||
                    string.Equals(m.Name, $"{OperatorPrefix}CheckedExplicit", StringComparison.Ordinal);

                if (!opIsImplicit && !opIsExplicit)
                    continue;

                var srcToParam = ClassifyConversion(expr, m.Parameters[0].Type);
                if (!srcToParam.Exists)
                    continue;

                var dummyRet = new BoundTypeOnlyExpression(exprSyntax, m.ReturnType);
                var retToTarget = ClassifyConversion(dummyRet, target);
                if (!retToTarget.Exists)
                    continue;

                bool overallImplicit = opIsImplicit && srcToParam.IsImplicit && retToTarget.IsImplicit;

                int score = 0;
                score += ConversionScore(srcToParam.Kind);
                score += ConversionScore(retToTarget.Kind);
                score += overallImplicit ? 0 : 10; // prefer implicit applicability

                if (IsCheckedOverflowContext &&
                    string.Equals(m.Name, $"{OperatorPrefix}CheckedExplicit", StringComparison.Ordinal))
                {
                    score -= 1; // prefer checked explicit in checked context
                }

                if (score < bestScore)
                {
                    best = m;
                    bestImplicit = overallImplicit;
                    bestScore = score;
                    ambiguous = false;
                }
                else if (score == bestScore)
                {
                    ambiguous = true;
                }
            }

            if (best is null || ambiguous)
                return new Conversion(ConversionKind.None);

            return new Conversion(ConversionKind.UserDefined, best, bestImplicit);

            static int ConversionScore(ConversionKind k) => k switch
            {
                ConversionKind.Identity => 0,
                ConversionKind.ImplicitNumeric => 1,
                ConversionKind.ImplicitConstant => 1,
                ConversionKind.ImplicitReference => 1,
                ConversionKind.ImplicitTuple => 1,
                ConversionKind.NullLiteral => 1,
                ConversionKind.Boxing => 2,
                ConversionKind.ExplicitNumeric => 3,
                ConversionKind.ExplicitReference => 3,
                ConversionKind.Unboxing => 3,
                _ => 10
            };
        }
        private static ImmutableArray<string> GetConversionOperatorMetadataNames(bool isCheckedContext)
        {
            return isCheckedContext
                ? ImmutableArray.Create(
                    $"{OperatorPrefix}Implicit",
                    $"{OperatorPrefix}CheckedExplicit",
                    $"{OperatorPrefix}Explicit")
                : ImmutableArray.Create(
                    $"{OperatorPrefix}Implicit",
                    $"{OperatorPrefix}Explicit");
        }
        public static bool AreSameType(TypeSymbol? a, TypeSymbol? b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (a is null || b is null)
                return false;

            if (a.Kind != b.Kind)
                return false;

            switch (a, b)
            {
                case (ArrayTypeSymbol aa, ArrayTypeSymbol bb):
                    return aa.Rank == bb.Rank &&
                           AreSameType(aa.ElementType, bb.ElementType);

                case (PointerTypeSymbol aa, PointerTypeSymbol bb):
                    return AreSameType(aa.PointedAtType, bb.PointedAtType);

                case (ByRefTypeSymbol aa, ByRefTypeSymbol bb):
                    return AreSameType(aa.ElementType, bb.ElementType);

                case (TupleTypeSymbol aa, TupleTypeSymbol bb):
                    if (aa.ElementTypes.Length != bb.ElementTypes.Length)
                        return false;
                    for (int i = 0; i < aa.ElementTypes.Length; i++)
                    {
                        if (!AreSameType(aa.ElementTypes[i], bb.ElementTypes[i]))
                            return false;
                    }
                    return true;

                case (NamedTypeSymbol aa, NamedTypeSymbol bb):
                    if (!ReferenceEquals(aa.OriginalDefinition, bb.OriginalDefinition))
                        return false;

                    var aaArgs = aa.TypeArguments;
                    var bbArgs = bb.TypeArguments;

                    if (aaArgs.Length != bbArgs.Length)
                        return false;

                    for (int i = 0; i < aaArgs.Length; i++)
                    {
                        if (!AreSameType(aaArgs[i], bbArgs[i]))
                            return false;
                    }

                    return true;
                case (TypeParameterSymbol ta, TypeParameterSymbol tb):
                    {
                        if (ta.Ordinal != tb.Ordinal)
                            return false;

                        static int OwnerKind(TypeParameterSymbol tp) => tp.ContainingSymbol switch
                        {
                            MethodSymbol => 2,
                            NamedTypeSymbol => 1,
                            _ => 0
                        };

                        int ak = OwnerKind(ta);
                        int bk = OwnerKind(tb);

                        return ak == bk || ak == 0 || bk == 0;
                    }

                default:
                    return false;
            }
        }
        internal static Conversion ClassifyConversion(BoundExpression expr, TypeSymbol target)
        {
            if (expr is BoundOutVarPendingExpression)
                return ClassifyOutVarPendingConversion(target);

            if (expr is BoundOutDiscardExpression discard)
                return ClassifyOutDiscardConversion(discard, target);

            if (expr is BoundUnboundLambdaExpression || expr is BoundMethodGroupExpression)
                return new Conversion(ConversionKind.None);

            if (AreSameType(expr.Type, target))
                return new Conversion(ConversionKind.Identity);

            if (expr is BoundThrowExpression || expr.Type is ThrowTypeSymbol)
                return new Conversion(ConversionKind.Identity);

            // default literal
            if (expr.Type is DefaultLiteralTypeSymbol)
            {
                if (target.SpecialType == SpecialType.System_Void || target is ByRefTypeSymbol)
                    return new Conversion(ConversionKind.None);

                return new Conversion(ConversionKind.Identity);
            }
            // null literal
            if (expr.Type is NullTypeSymbol)
            {
                if (target.IsReferenceType || target is PointerTypeSymbol)
                    return new Conversion(ConversionKind.NullLiteral);
                if (TryGetSystemNullableInfo(target, out _, out _))
                    return new Conversion(ConversionKind.NullLiteral);
                return new Conversion(ConversionKind.None);
            }

            if (IsTrueErrorType(expr.Type) || IsTrueErrorType(target))
                return new Conversion(ConversionKind.Identity);

            if (TryGetSystemNullableInfo(target, out _, out var targetUnderlying))
            {
                // Nullable<S> to Nullable<T>
                if (TryGetSystemNullableInfo(expr.Type, out _, out var fromUnderlying))
                {
                    var dummy = new BoundTypeOnlyExpression((ExpressionSyntax)expr.Syntax, fromUnderlying);
                    var underlyingConv = ClassifyConversion(dummy, targetUnderlying);
                    if (!underlyingConv.Exists)
                        return new Conversion(ConversionKind.None);
                    return new Conversion(underlyingConv.IsImplicit ? ConversionKind.ImplicitNullable : ConversionKind.ExplicitNullable);
                }
                // S to Nullable<T>
                var underlyingConv2 = ClassifyConversion(expr, targetUnderlying);
                if (!underlyingConv2.Exists)
                    return new Conversion(ConversionKind.None);
                return new Conversion(underlyingConv2.IsImplicit ? ConversionKind.ImplicitNullable : ConversionKind.ExplicitNullable);
            }
            if (expr.ConstantValueOpt.HasValue && expr.ConstantValueOpt.Value is null
                && (target.IsReferenceType || target is PointerTypeSymbol))
                return new Conversion(ConversionKind.NullLiteral);

            // pointer conversions
            if (target is PointerTypeSymbol && TryImplicitConstantZeroPointerConversion(expr))
                return new Conversion(ConversionKind.ImplicitConstant);
            if (expr.Type is PointerTypeSymbol fromPtr && target is PointerTypeSymbol toPtr)
            {
                bool toVoid = toPtr.PointedAtType.SpecialType == SpecialType.System_Void;

                // implicit
                if (toVoid)
                    return new Conversion(ConversionKind.ImplicitNumeric);

                // explicit
                return new Conversion(ConversionKind.ExplicitNumeric);
            }
            if (target is PointerTypeSymbol &&
                (IsIntegral(expr.Type.SpecialType) || IsEnumType(expr.Type)))
            {
                return new Conversion(ConversionKind.ExplicitNumeric);
            }
            if (expr.Type is PointerTypeSymbol &&
                (IsIntegral(target.SpecialType) || IsEnumType(target)))
            {
                return new Conversion(ConversionKind.ExplicitNumeric);
            }
            if (expr.Type is PointerTypeSymbol &&
                (target.SpecialType == SpecialType.System_IntPtr || target.SpecialType == SpecialType.System_UIntPtr))
            {
                return new Conversion(ConversionKind.ExplicitNumeric);
            }
            if ((expr.Type.SpecialType == SpecialType.System_IntPtr || expr.Type.SpecialType == SpecialType.System_UIntPtr) &&
                target is PointerTypeSymbol)
            {
                return new Conversion(ConversionKind.ExplicitNumeric);
            }

            // tuple conversions
            if (expr.Type is TupleTypeSymbol fromTuple && target is TupleTypeSymbol toTuple)
            {
                if (fromTuple.ElementTypes.Length != toTuple.ElementTypes.Length)
                    return new Conversion(ConversionKind.None);
                bool allImplicit = true;

                if (expr is BoundTupleExpression tupleExpr)
                {
                    for (int i = 0; i < toTuple.ElementTypes.Length; i++)
                    {
                        var ec = ClassifyConversion(tupleExpr.Elements[i], toTuple.ElementTypes[i]);
                        if (!ec.Exists)
                            return new Conversion(ConversionKind.None);
                        if (!ec.IsImplicit)
                            allImplicit = false;
                    }
                }
                else
                {
                    for (int i = 0; i < toTuple.ElementTypes.Length; i++)
                    {
                        var dummy = new BoundTypeOnlyExpression((ExpressionSyntax)expr.Syntax, fromTuple.ElementTypes[i]);
                        var ec = ClassifyConversion(dummy, toTuple.ElementTypes[i]);
                        if (!ec.Exists)
                            return new Conversion(ConversionKind.None);
                        if (!ec.IsImplicit)
                            allImplicit = false;
                    }
                }
                return new Conversion(allImplicit ? ConversionKind.ImplicitTuple : ConversionKind.ExplicitTuple);
            }
            bool exprHasRefLike = RefLikeRestrictionFacts.ContainsRefLike(expr.Type);
            bool targetHasRefLike = RefLikeRestrictionFacts.ContainsRefLike(target);

            // type parameter conversions
            if (target is TypeParameterSymbol targetTp)
            {
                if ((targetTp.GenericConstraint & GenericConstraintsFlags.AllowsRefStruct) != 0)
                    return new Conversion(ConversionKind.None);

                if (expr.Type.SpecialType is SpecialType.System_Object
                    or SpecialType.System_ValueType
                    or SpecialType.System_Enum
                    || IsInterfaceType(expr.Type))
                {
                    return new Conversion(ConversionKind.Unboxing);
                }
            }

            if (expr.Type is TypeParameterSymbol tp)
            {
                if ((tp.GenericConstraint & GenericConstraintsFlags.AllowsRefStruct) != 0)
                    return new Conversion(ConversionKind.None);

                if (target.SpecialType == SpecialType.System_Object)
                    return new Conversion(ConversionKind.Boxing);
            }

            if (HasImplicitBoxingConversion(expr.Type, target))
                return new Conversion(ConversionKind.Boxing);

            if (HasExplicitUnboxingConversion(expr.Type, target))
                return new Conversion(ConversionKind.Unboxing);

            // enum conversions
            bool exprIsEnum = IsEnumType(expr.Type);
            bool targetIsEnum = IsEnumType(target);

            if (exprIsEnum || targetIsEnum)
            {
                if (exprIsEnum && targetIsEnum)
                    return new Conversion(ConversionKind.ExplicitNumeric);

                if (targetIsEnum)
                {
                    if (TryImplicitConstantZeroEnumConversion(expr))
                        return new Conversion(ConversionKind.ImplicitConstant);

                    var fromSt = GetEnumOrSelfNumericSpecialType(expr.Type);
                    var toSt = GetEnumOrSelfNumericSpecialType(target);

                    if (IsNumeric(fromSt) && IsNumeric(toSt))
                        return new Conversion(ConversionKind.ExplicitNumeric);

                    return new Conversion(ConversionKind.None);
                }
                if (exprIsEnum)
                {
                    var fromSt = GetEnumOrSelfNumericSpecialType(expr.Type);
                    var toSt = GetEnumOrSelfNumericSpecialType(target);

                    if (IsNumeric(fromSt) && IsNumeric(toSt))
                        return new Conversion(ConversionKind.ExplicitNumeric);
                }
            }

            // numeric conversions
            if (IsNumeric(expr.Type.SpecialType) && IsNumeric(target.SpecialType))
            {
                if (IsImplicitNumeric(expr.Type.SpecialType, target.SpecialType))
                    return new Conversion(ConversionKind.ImplicitNumeric);

                if (TryImplicitConstantNumericConversion(expr, target))
                    return new Conversion(ConversionKind.ImplicitConstant);

                return new Conversion(ConversionKind.ExplicitNumeric);
            }

            // reference conversions
            if (HasImplicitReferenceConversion(expr.Type, target))
                return new Conversion(ConversionKind.ImplicitReference);

            if (HasExplicitReferenceConversion(expr.Type, target))
                return new Conversion(ConversionKind.ExplicitReference);

            return new Conversion(ConversionKind.None);

            static bool IsIntegralConstantSource(SpecialType t) => t is
                SpecialType.System_Int8 or SpecialType.System_UInt8 or
                SpecialType.System_Int16 or SpecialType.System_UInt16 or
                SpecialType.System_Char or
                SpecialType.System_Int32 or SpecialType.System_UInt32 or
                SpecialType.System_Int64 or SpecialType.System_UInt64;

            static SpecialType GetEnumOrSelfNumericSpecialType(TypeSymbol t)
            {
                if (t is NamedTypeSymbol nt && nt.TypeKind == TypeKind.Enum)
                    return nt.EnumUnderlyingType?.SpecialType ?? SpecialType.System_Int32;

                return t.SpecialType;
            }

            static bool TryImplicitConstantZeroEnumConversion(BoundExpression expr)
            {
                if (!expr.ConstantValueOpt.HasValue || expr.ConstantValueOpt.Value is null)
                    return false;

                if (!IsNumeric(expr.Type.SpecialType))
                    return false;

                return TryToDecimal(expr.ConstantValueOpt.Value, out var d) && d == 0m;
            }

            static bool TryToDecimal(object v, out decimal d)
            {
                switch (v)
                {
                    case sbyte x: d = x; return true;
                    case byte x: d = x; return true;
                    case short x: d = x; return true;
                    case ushort x: d = x; return true;
                    case int x: d = x; return true;
                    case uint x: d = x; return true;
                    case long x: d = x; return true;
                    case ulong x: d = x; return true;
                    case char x: d = x; return true;
                    default:
                        d = default;
                        return false;
                }
            }
            static bool TryImplicitConstantZeroPointerConversion(BoundExpression expr)
            {
                if (!expr.ConstantValueOpt.HasValue || expr.ConstantValueOpt.Value is null)
                    return false;

                if (!(IsIntegral(expr.Type.SpecialType) || IsEnumType(expr.Type)))
                    return false;

                return TryToDecimal(expr.ConstantValueOpt.Value, out var d) && d == 0m;
            }
            static (decimal min, decimal max, bool ok) GetIntegralRange(SpecialType t) => t switch
            {
                SpecialType.System_Int8 => (sbyte.MinValue, sbyte.MaxValue, true),
                SpecialType.System_UInt8 => (byte.MinValue, byte.MaxValue, true),
                SpecialType.System_Int16 => (short.MinValue, short.MaxValue, true),
                SpecialType.System_UInt16 => (ushort.MinValue, ushort.MaxValue, true),
                SpecialType.System_Char => (char.MinValue, char.MaxValue, true),
                SpecialType.System_Int32 => (int.MinValue, int.MaxValue, true),
                SpecialType.System_UInt32 => (uint.MinValue, uint.MaxValue, true),
                SpecialType.System_Int64 => (long.MinValue, long.MaxValue, true),
                SpecialType.System_UInt64 => (0m, (decimal)ulong.MaxValue, true),
                SpecialType.System_IntPtr =>
                    (Cnidaria.Cs.TargetArchitecture.PointerSize == 4 ? int.MinValue : long.MinValue,
                    Cnidaria.Cs.TargetArchitecture.PointerSize == 4 ? int.MaxValue : long.MaxValue, true),
                SpecialType.System_UIntPtr =>
                    (Cnidaria.Cs.TargetArchitecture.PointerSize == 4 ? uint.MinValue : ulong.MinValue,
                    Cnidaria.Cs.TargetArchitecture.PointerSize == 4 ? uint.MaxValue : ulong.MaxValue, true),
                _ => (0m, 0m, false)
            };

            static bool TryImplicitConstantNumericConversion(BoundExpression expr, TypeSymbol target)
            {
                if (!expr.ConstantValueOpt.HasValue)
                    return false;

                if (!IsIntegralConstantSource(expr.Type.SpecialType))
                    return false;

                if (!IsIntegral(target.SpecialType))
                    return false;

                if (!TryToDecimal(expr.ConstantValueOpt.Value!, out var value))
                    return false;

                // must be integral
                if (value != decimal.Truncate(value))
                    return false;

                var (min, max, ok) = GetIntegralRange(target.SpecialType);
                if (!ok) return false;

                return value >= min && value <= max;
            }
            static bool IsImplicitNumeric(SpecialType from, SpecialType to)
            {
                return from switch
                {
                    SpecialType.System_Int8 => to is SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64
                        or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal or SpecialType.System_IntPtr,

                    SpecialType.System_UInt8 => to is SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32
                        or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64
                        or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal
                        or SpecialType.System_IntPtr or SpecialType.System_UIntPtr,

                    SpecialType.System_Int16 => to is SpecialType.System_Int32 or SpecialType.System_Int64
                        or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal or SpecialType.System_IntPtr,

                    SpecialType.System_UInt16 => to is SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64
                        or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal
                        or SpecialType.System_IntPtr or SpecialType.System_UIntPtr,

                    SpecialType.System_Int32 => to is SpecialType.System_Int64 or SpecialType.System_Single or SpecialType.System_Double
                        or SpecialType.System_Decimal or SpecialType.System_IntPtr,
                    SpecialType.System_UInt32 => to is SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single
                        or SpecialType.System_Double or SpecialType.System_Decimal or SpecialType.System_UIntPtr,
                    SpecialType.System_Int64 => to is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal,
                    SpecialType.System_UInt64 => to is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal,

                    SpecialType.System_Char => to is SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
                        or SpecialType.System_Int64 or SpecialType.System_UInt64
                        or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal
                        or SpecialType.System_IntPtr or SpecialType.System_UIntPtr,

                    SpecialType.System_Single => to is SpecialType.System_Double,

                    SpecialType.System_IntPtr => to is
                        SpecialType.System_Int64 or
                        SpecialType.System_Single or
                        SpecialType.System_Double or
                        SpecialType.System_Decimal,

                    SpecialType.System_UIntPtr => to is
                        SpecialType.System_UInt64 or
                        SpecialType.System_Single or
                        SpecialType.System_Double or
                        SpecialType.System_Decimal,

                    _ => false
                };
            }
        }
        private static Conversion ClassifyOutVarPendingConversion(TypeSymbol target)
        {
            return target is ByRefTypeSymbol
                ? new Conversion(ConversionKind.Identity)
                : new Conversion(ConversionKind.None);
        }
        private static Conversion ClassifyOutDiscardConversion(BoundOutDiscardExpression discard, TypeSymbol target)
        {
            if (target is not ByRefTypeSymbol targetByRef)
                return new Conversion(ConversionKind.None);

            if (discard.ExplicitElementTypeOpt is null)
                return new Conversion(ConversionKind.Identity);

            return AreSameType(discard.ExplicitElementTypeOpt, targetByRef.ElementType)
                ? new Conversion(ConversionKind.Identity)
                : new Conversion(ConversionKind.None);
        }
        private static bool ImplementsInterface(TypeSymbol source, TypeSymbol destinationInterface)
        {
            var seen = new HashSet<TypeSymbol>(ReferenceEqualityComparer<TypeSymbol>.Instance);
            return ImplementsInterfaceCore(source, destinationInterface, seen);

            static bool ImplementsInterfaceCore(
                TypeSymbol current,
                TypeSymbol destinationInterface,
                HashSet<TypeSymbol> seen)
            {
                if (!seen.Add(current))
                    return false;

                var interfaces = current.Interfaces;
                for (int i = 0; i < interfaces.Length; i++)
                {
                    var iface = interfaces[i];
                    if (ReferenceEquals(iface, destinationInterface))
                        return true;

                    if (ImplementsInterfaceCore(iface, destinationInterface, seen))
                        return true;
                }

                if (current.BaseType is TypeSymbol bt)
                    return ImplementsInterfaceCore(bt, destinationInterface, seen);

                return false;
            }
        }
        private static bool IsInterfaceType(TypeSymbol t)
            => t is NamedTypeSymbol nt && nt.TypeKind == TypeKind.Interface;
        private static bool HasImplicitBoxingConversion(TypeSymbol source, TypeSymbol destination)
        {
            if (!source.IsValueType)
                return false;

            if (RefLikeRestrictionFacts.ContainsRefLike(source))
                return false;

            if (!destination.IsReferenceType)
                return false;

            // to object
            if (destination.SpecialType == SpecialType.System_Object)
                return true;

            // struct to ValueType
            if (destination.SpecialType == SpecialType.System_ValueType)
                return !IsEnumType(source);

            // enum to System.Enum
            if (destination.SpecialType == SpecialType.System_Enum)
                return IsEnumType(source);

            // to any implemented interface
            if (IsInterfaceType(destination) && ImplementsInterface(source, destination))
                return true;

            return false;
        }
        private static bool HasExplicitUnboxingConversion(TypeSymbol source, TypeSymbol destination)
        {
            if (!destination.IsValueType)
                return false;

            if (RefLikeRestrictionFacts.ContainsRefLike(destination))
                return false;

            if (!source.IsReferenceType)
                return false;

            // object/valueType/enum/interface to valuetype
            if (source.SpecialType is SpecialType.System_Object
                or SpecialType.System_ValueType
                or SpecialType.System_Enum)
                return true;

            if (IsInterfaceType(source))
                return true;

            return false;
        }
        private static bool HasImplicitReferenceConversion(TypeSymbol source, TypeSymbol destination)
        {
            if (!source.IsReferenceType || !destination.IsReferenceType)
                return false;

            if (AreSameType(source, destination))
                return true;

            // class/interface/delegate to base class
            if (IsBaseTypeOf(destination, source))
                return true;

            // reference type to implemented interface
            if (IsInterfaceType(destination) && ImplementsInterface(source, destination))
                return true;

            // interface to object
            if (IsInterfaceType(source) && destination.SpecialType == SpecialType.System_Object)
                return true;

            // Array covariance
            if (source is ArrayTypeSymbol srcArr && destination is ArrayTypeSymbol dstArr)
            {
                if (srcArr.Rank != dstArr.Rank)
                    return false;

                if (!srcArr.ElementType.IsReferenceType || !dstArr.ElementType.IsReferenceType)
                    return false;

                return HasImplicitReferenceConversion(srcArr.ElementType, dstArr.ElementType);
            }

            return false;

        }
        private static bool HasExplicitReferenceConversion(TypeSymbol source, TypeSymbol destination)
        {
            if (!source.IsReferenceType || !destination.IsReferenceType)
                return false;

            if (AreSameType(source, destination))
                return true;

            if (HasImplicitReferenceConversion(source, destination))
                return true;

            // Normal downcast
            if (IsBaseTypeOf(source, destination))
                return true;

            bool srcIface = IsInterfaceType(source);
            bool dstIface = IsInterfaceType(destination);

            if (!srcIface && dstIface)
            {
                if (ImplementsInterface(source, destination))
                    return true;

                return false;
            }
            if (srcIface && !dstIface)
            {
                if (destination is NamedTypeSymbol ntDest && ImplementsInterface(ntDest, source))
                    return true;

                return false;
            }

            // Array covariance
            if (source is ArrayTypeSymbol srcArr && destination is ArrayTypeSymbol dstArr)
            {
                if (srcArr.Rank != dstArr.Rank)
                    return false;

                if (!srcArr.ElementType.IsReferenceType || !dstArr.ElementType.IsReferenceType)
                    return false;

                return HasExplicitReferenceConversion(srcArr.ElementType, dstArr.ElementType);
            }

            return false;
        }
        private static bool TryGetSystemNullableInfo(TypeSymbol t, out NamedTypeSymbol nullableType, out TypeSymbol underlying)
        {
            if (t is NamedTypeSymbol nt && nt.IsValueType)
            {
                var def = nt.OriginalDefinition;
                if (def.Arity == 1
                    && string.Equals(def.Name, "Nullable", StringComparison.Ordinal)
                    && def.ContainingSymbol is NamespaceSymbol ns
                    && string.Equals(ns.Name, "System", StringComparison.Ordinal))
                {
                    nullableType = nt;
                    underlying = nt.TypeArguments[0];
                    return true;
                }
            }

            nullableType = null!;
            underlying = null!;
            return false;
        }
    }
}
