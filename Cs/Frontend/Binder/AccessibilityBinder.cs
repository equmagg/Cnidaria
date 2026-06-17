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
    internal static class AccessibilityHelper
    {
        public static bool IsAccessible(Symbol symbol, BindingContext context)
        {
            if (context.SemanticModel.IgnoresAccessibility)
                return true;

            var acc = symbol.DeclaredAccessibility;
            if (acc == Accessibility.NotApplicable || acc == Accessibility.Public)
                return true;

            var declaringType = GetDeclaringType(symbol);
            var accessingType = GetEnclosingType(context.ContainingSymbol);
            bool sameAssembly = IsSameAssembly(symbol, context.ContainingSymbol);

            bool protectedOk = declaringType is not null &&
                               accessingType is not null &&
                               IsSameOrDerived(accessingType, declaringType);

            bool privateOk = declaringType is not null &&
                             accessingType is not null &&
                             IsSameOrNestedRelation(accessingType, declaringType);

            return acc switch
            {
                Accessibility.Private => privateOk,
                Accessibility.Internal => sameAssembly,
                Accessibility.Protected => protectedOk,
                Accessibility.ProtectedOrInternal => protectedOk || sameAssembly,
                Accessibility.ProtectedAndInternal => protectedOk && sameAssembly,
                _ => true
            };
        }
        private static bool IsSameAssembly(Symbol a, Symbol b)
        => a.IsFromMetadata == b.IsFromMetadata;

        private static NamedTypeSymbol? GetDeclaringType(Symbol symbol)
        {
            return symbol switch
            {
                NamedTypeSymbol nt when nt.ContainingSymbol is NamedTypeSymbol owner => owner,
                FieldSymbol f => f.ContainingSymbol as NamedTypeSymbol,
                PropertySymbol p => p.ContainingSymbol as NamedTypeSymbol,
                MethodSymbol m => m.ContainingSymbol as NamedTypeSymbol,
                _ => null
            };
        }

        private static NamedTypeSymbol? GetEnclosingType(Symbol? s)
        {
            for (; s is not null; s = s.ContainingSymbol)
                if (s is NamedTypeSymbol nt)
                    return nt;
            return null;
        }
        private static bool IsSameTypeDefinition(NamedTypeSymbol a, NamedTypeSymbol b)
        {
            if (ReferenceEquals(a, b))
                return true;

            return ReferenceEquals(a.OriginalDefinition, b.OriginalDefinition);
        }
        private static bool IsSameOrNestedRelation(NamedTypeSymbol a, NamedTypeSymbol b)
            => IsSameTypeDefinition(a, b) || IsNestedWithin(a, b) || IsNestedWithin(b, a);

        private static bool IsNestedWithin(NamedTypeSymbol maybeInner, NamedTypeSymbol maybeOuter)
        {
            for (var cur = maybeInner.ContainingSymbol; cur is not null; cur = cur.ContainingSymbol)
            {
                if (cur is NamedTypeSymbol nt && IsSameTypeDefinition(nt, maybeOuter))
                    return true;
            }

            return false;
        }

        private static bool IsSameOrDerived(NamedTypeSymbol type, NamedTypeSymbol baseType)
        {
            for (TypeSymbol? cur = type; cur is not null; cur = cur.BaseType)
            {
                if (cur is NamedTypeSymbol nt && IsSameTypeDefinition(nt, baseType))
                    return true;
            }

            return false;
        }
    }
}
