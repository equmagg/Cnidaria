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
    internal static class GenericConstraintFacts
    {
        public static bool IsSystemNullableValueType(TypeSymbol t)
        {
            if (t is not NamedTypeSymbol nt || !nt.IsValueType)
                return false;

            var def = nt.OriginalDefinition;
            if (def.Arity != 1 || !string.Equals(def.Name, "Nullable", StringComparison.Ordinal))
                return false;

            return def.ContainingSymbol is NamespaceSymbol ns
                && string.Equals(ns.Name, "System", StringComparison.Ordinal);
        }
        public static bool IsNonNullableValueType(TypeSymbol t)
        {
            if (t is TypeParameterSymbol tp)
                return (tp.GenericConstraint & GenericConstraintsFlags.StructConstraint) != 0;

            if (!t.IsValueType)
                return false;

            return !IsSystemNullableValueType(t);
        }
        public static bool IsNotNullType(TypeSymbol t)
        {
            if (t is TypeParameterSymbol tp)
            {
                var c = tp.GenericConstraint;
                return (c & (GenericConstraintsFlags.StructConstraint
                    | GenericConstraintsFlags.UnmanagedConstraint
                    | GenericConstraintsFlags.NotNullConstraint)) != 0;
            }
            if (t is NullTypeSymbol)
                return false;
            if (t.IsValueType)
                return IsNonNullableValueType(t);
            return t.IsReferenceType;
        }
        public static bool IsUnmanagedType(TypeSymbol t)
        {
            var visiting = new HashSet<TypeSymbol>(ReferenceEqualityComparer<TypeSymbol>.Instance);
            return IsUnmanagedTypeCore(t, visiting);
        }
        private static bool IsUnmanagedTypeCore(TypeSymbol t, HashSet<TypeSymbol> visiting)
        {
            if (t is TypeParameterSymbol tp)
                return (tp.GenericConstraint & GenericConstraintsFlags.UnmanagedConstraint) != 0;
            if (t is PointerTypeSymbol)
                return true;
            if (t is ByRefTypeSymbol)
                return false;
            if (t is ArrayTypeSymbol)
                return false;
            if (t is TupleTypeSymbol tuple)
            {
                for (int i = 0; i < tuple.ElementTypes.Length; i++)
                    if (!IsUnmanagedTypeCore(tuple.ElementTypes[i], visiting))
                        return false;
                return true;
            }
            if (t is NamedTypeSymbol nt)
            {
                if (nt.IsReferenceType)
                    return false;

                if (nt.IsRefLikeType)
                    return false;

                if (!nt.IsValueType)
                    return false;

                if (!visiting.Add(nt))
                    return true;

                if (nt.TypeKind == TypeKind.Enum)
                {
                    var u = nt.EnumUnderlyingType;
                    visiting.Remove(nt);
                    return u != null && IsUnmanagedTypeCore(u, visiting);
                }

                var members = nt.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is FieldSymbol f && !f.IsStatic)
                    {
                        if (!IsUnmanagedTypeCore(f.Type, visiting))
                        {
                            visiting.Remove(nt);
                            return false;
                        }
                    }
                }

                visiting.Remove(nt);
                return true;
            }
            return false;
        }
    }
    internal static class GenericConstraintChecker
    {
        public static bool CheckNamedTypeInstantiation(
            NamedTypeSymbol constructedType,
            ImmutableArray<TypeSymbol> typeArguments,
            Func<int, TextSpan> getArgSpan,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var tps = constructedType.TypeParameters;
            return CheckCore(
                ownerDisplayName: constructedType.OriginalDefinition.Name,
                typeParameters: tps,
                typeArguments: typeArguments,
                getArgSpan: getArgSpan,
                context: context,
                diagnostics: diagnostics);
        }
        public static bool CheckMethodInstantiation(
            MethodSymbol methodDefinition,
            ImmutableArray<TypeSymbol> typeArguments,
            Func<int, TextSpan> getArgSpan,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var tps = methodDefinition.TypeParameters;
            return CheckCore(
                ownerDisplayName: methodDefinition.Name,
                typeParameters: tps,
                typeArguments: typeArguments,
                getArgSpan: getArgSpan,
                context: context,
                diagnostics: diagnostics);
        }
        private static bool CheckCore(
            string ownerDisplayName,
            ImmutableArray<TypeParameterSymbol> typeParameters,
            ImmutableArray<TypeSymbol> typeArguments,
            Func<int, TextSpan> getArgSpan,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (typeParameters.IsDefaultOrEmpty || typeArguments.IsDefaultOrEmpty)
                return true;
            int n = Math.Min(typeParameters.Length, typeArguments.Length);
            bool ok = true;

            for (int i = 0; i < n; i++)
            {
                var tp = typeParameters[i];
                var arg = typeArguments[i];
                if (arg.Kind == SymbolKind.Error)
                    continue;
                var constraints = tp.GenericConstraint;
                // where T : allows ref struct
                if ((constraints & GenericConstraintsFlags.AllowsRefStruct) == 0
                    && RefLikeRestrictionFacts.ContainsRefLike(arg))
                {
                    ok = false;
                    diagnostics.Add(new Diagnostic(
                        id: "CN_GENCONSTR_BYREFLIKE",
                        severity: DiagnosticSeverity.Error,
                        message: $"Ref-like type '{arg.Name}' cannot be used as a type argument for '{tp.Name}' " +
                        $"in '{ownerDisplayName}' unless '{tp.Name}' has 'allows ref struct'.",
                        location: new Location(context.SemanticModel.SyntaxTree, getArgSpan(i))));
                }
                // where T : unmanaged
                if ((constraints & GenericConstraintsFlags.UnmanagedConstraint) != 0
                    && !GenericConstraintFacts.IsUnmanagedType(arg))
                {
                    ok = false;
                    diagnostics.Add(new Diagnostic(
                        id: "CN_GENCONSTR_UNMANAGED",
                        severity: DiagnosticSeverity.Error,
                        message: $"The type '{arg.Name}' must be unmanaged to satisfy " +
                        $"the 'unmanaged' constraint on '{tp.Name}' in '{ownerDisplayName}'.",
                        location: new Location(context.SemanticModel.SyntaxTree, getArgSpan(i))));
                }
                // where T : struct
                if ((constraints & GenericConstraintsFlags.UnmanagedConstraint) == 0
                    && (constraints & GenericConstraintsFlags.StructConstraint) != 0
                    && !GenericConstraintFacts.IsNonNullableValueType(arg))
                {
                    ok = false;
                    diagnostics.Add(new Diagnostic(
                        id: "CN_GENCONSTR_STRUCT",
                        severity: DiagnosticSeverity.Error,
                        message: $"The type '{arg.Name}' must be a non-nullable value type to satisfy " +
                        $"the 'struct' constraint on '{tp.Name}' in '{ownerDisplayName}'.",
                        location: new Location(context.SemanticModel.SyntaxTree, getArgSpan(i))));
                }
                // where T : notnull
                if ((constraints & (GenericConstraintsFlags.StructConstraint
                    | GenericConstraintsFlags.UnmanagedConstraint)) == 0
                    && (constraints & GenericConstraintsFlags.NotNullConstraint) != 0
                    && !GenericConstraintFacts.IsNotNullType(arg))
                {
                    ok = false;
                    diagnostics.Add(new Diagnostic(
                        id: "CN_GENCONSTR_NOTNULL",
                        severity: DiagnosticSeverity.Error,
                        message: $"The type '{arg.Name}' must be non-nullable to satisfy " +
                        $"the 'notnull' constraint on '{tp.Name}' in '{ownerDisplayName}'.",
                        location: new Location(context.SemanticModel.SyntaxTree, getArgSpan(i))));
                }
            }
            return ok;
        }
    }
    internal static class RefLikeRestrictionFacts
    {
        public static bool ContainsRefLike(TypeSymbol type)
        {
            switch (type)
            {
                case null:
                    return false;
                case ByRefTypeSymbol br:
                    return ContainsRefLike(br.ElementType);
                case PointerTypeSymbol ptr:
                    return ContainsRefLike(ptr.PointedAtType);
                case ArrayTypeSymbol arr:
                    return ContainsRefLike(arr.ElementType);
                case TupleTypeSymbol tuple:
                    for (int i = 0; i < tuple.ElementTypes.Length; i++)
                        if (ContainsRefLike(tuple.ElementTypes[i]))
                            return true;
                    return false;
                case NamedTypeSymbol nt:
                    if (nt.IsRefLikeType)
                        return true;
                    var typeArgs = nt.TypeArguments;
                    for (int i = 0; i < typeArgs.Length; i++)
                        if (ContainsRefLike(typeArgs[i]))
                            return true;
                    return false;
                default:
                    return false;
            }
        }
    }
    internal static class GenericConstraintBinder
    {
        public static void BindAll(Compilation compilation, ImmutableArray<SyntaxTree> trees, DiagnosticBag diagnostics)
        {
            foreach (var tree in trees)
            {
                if (!compilation.DeclaredSymbolsByTree.TryGetValue(tree, out var declMap))
                    continue;
                foreach (var kv in declMap)
                {
                    switch (kv.Key)
                    {
                        case ClassDeclarationSyntax cd when kv.Value is NamedTypeSymbol nt:
                            BindOwnerConstraintClauses(tree, cd.ConstraintClauses, nt.TypeParameters, nt, diagnostics);
                            break;

                        case StructDeclarationSyntax sd when kv.Value is NamedTypeSymbol nt:
                            BindOwnerConstraintClauses(tree, sd.ConstraintClauses, nt.TypeParameters, nt, diagnostics);
                            break;

                        case InterfaceDeclarationSyntax id when kv.Value is NamedTypeSymbol nt:
                            BindOwnerConstraintClauses(tree, id.ConstraintClauses, nt.TypeParameters, nt, diagnostics);
                            break;

                        case DelegateDeclarationSyntax dd when kv.Value is NamedTypeSymbol nt:
                            BindOwnerConstraintClauses(tree, dd.ConstraintClauses, nt.TypeParameters, nt, diagnostics);
                            break;

                        case MethodDeclarationSyntax md when kv.Value is MethodSymbol ms:
                            BindOwnerConstraintClauses(tree, md.ConstraintClauses, ms.TypeParameters, ms, diagnostics);
                            break;
                    }
                }
            }
        }
        internal static void BindOwnerConstraintClauses(
            SyntaxTree tree,
            SyntaxList<TypeParameterConstraintClauseSyntax> clauses,
            ImmutableArray<TypeParameterSymbol> typeParameters,
            Symbol owner,
            DiagnosticBag diagnostics)
        {
            if (clauses.Count == 0)
                return;
            if (typeParameters.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic(
                    id: "CN_GENCONSTR_CLAUSE001",
                    severity: DiagnosticSeverity.Error,
                    message: $"'{owner.Name}' has no type parameters but has constraint clauses.",
                    location: new Location(tree, clauses[0].Span)));
                return;
            }

            for (int c = 0; c < clauses.Count; c++)
            {
                var clause = clauses[c];
                var tpName = clause.Name.ValueText ?? "";
                if (tpName.Length == 0)
                    continue;

                TypeParameterSymbol? tp = null;
                for (int i = 0; i < typeParameters.Length; i++)
                {
                    if (StringComparer.Ordinal.Equals(typeParameters[i].Name, tpName))
                    {
                        tp = typeParameters[i];
                        break;
                    }
                }
                if (tp is null)
                {
                    diagnostics.Add(new Diagnostic(
                        id: "CN_GENCONSTR_CLAUSE002",
                        severity: DiagnosticSeverity.Error,
                        message: $"Type parameter '{tpName}' is not declared on '{owner.Name}'.",
                        location: new Location(tree, clause.Name.Span)));
                    continue;
                }

                var cs = clause.Constraints;
                for (int i = 0; i < cs.Count; i++)
                {
                    var constraint = cs[i];
                    switch (constraint)
                    {
                        case ClassOrStructConstraintSyntax s when s.Kind == SyntaxKind.StructConstraint:
                            if (!tp.TrySetConstraint(GenericConstraintsFlags.StructConstraint))
                            {
                                diagnostics.Add(new Diagnostic(
                                    id: "CN_GENCONSTR_DUP001",
                                    severity: DiagnosticSeverity.Error,
                                    message: $"Duplicate 'struct' constraint for type parameter '{tp.Name}'.",
                                    location: new Location(tree, s.Span)));
                            }
                            break;
                        case AllowsConstraintClauseSyntax allows:
                            {
                                var allowsItems = allows.Constraints;
                                for (int a = 0; a < allowsItems.Count; a++)
                                {
                                    if (allowsItems[a] is RefStructConstraintSyntax rs)
                                    {
                                        if (!tp.TrySetConstraint(GenericConstraintsFlags.AllowsRefStruct))
                                        {
                                            diagnostics.Add(new Diagnostic(
                                                id: "CN_GENCONSTR_DUP002",
                                                severity: DiagnosticSeverity.Error,
                                                message: $"Duplicate 'allows ref struct' " +
                                                $"constraint for type parameter '{tp.Name}'.",
                                                location: new Location(tree, rs.Span)));
                                        }
                                    }
                                }
                            }
                            break;
                        case TypeConstraintSyntax tc:
                            if (tc.Type is IdentifierNameSyntax id)
                            {
                                var text = id.Identifier.ValueText ?? string.Empty;
                                if (string.Equals(text, "unmanaged", StringComparison.Ordinal))
                                {
                                    if (!tp.TrySetConstraint(GenericConstraintsFlags.UnmanagedConstraint))
                                    {
                                        diagnostics.Add(new Diagnostic(
                                            id: "CN_GENCONSTR_DUP003",
                                            severity: DiagnosticSeverity.Error,
                                            message: $"Duplicate 'unmanaged' constraint for type parameter '{tp.Name}'.",
                                            location: new Location(tree, tc.Span)));
                                    }
                                    tp.TrySetConstraint(GenericConstraintsFlags.StructConstraint);
                                }
                                else if (string.Equals(text, "notnull", StringComparison.Ordinal))
                                {
                                    if (!tp.TrySetConstraint(GenericConstraintsFlags.NotNullConstraint))
                                    {
                                        diagnostics.Add(new Diagnostic(
                                            id: "CN_GENCONSTR_DUP004",
                                            severity: DiagnosticSeverity.Error,
                                            message: $"Duplicate 'notnull' constraint for type parameter '{tp.Name}'.",
                                            location: new Location(tree, tc.Span)));
                                    }
                                }
                            }
                            break;
                    }
                }
            }
        }
    }
}
