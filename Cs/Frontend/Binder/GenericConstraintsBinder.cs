using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    /// <summary>Provides reusable predicates for generic constraint evaluation</summary>
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
        /// <summary>Returns whether a type argument satisfies a substituted type constraint (identity, implicit reference, boxing, or type parameter conversion)</summary>
        public static bool SatisfiesTypeConstraint(TypeSymbol argument, TypeSymbol constraint)
        {
            if (LocalScopeBinder.AreSameType(argument, constraint) || constraint.SpecialType == SpecialType.System_Object)
                return true;

            if (argument is TypeParameterSymbol typeParameter)
                return IsSatisfiedByTypeParameter(typeParameter, constraint, new HashSet<TypeParameterSymbol>(ReferenceEqualityComparer<TypeParameterSymbol>.Instance));

            // Nullable<T> boxes to ValueType and object only; it satisfies no interface constraint
            if (IsSystemNullableValueType(argument))
                return constraint.SpecialType == SpecialType.System_ValueType;

            return ConvertsByReferenceOrBoxing(argument, constraint);
        }
        private static bool IsSatisfiedByTypeParameter(TypeParameterSymbol typeParameter, TypeSymbol constraint, HashSet<TypeParameterSymbol> visited)
        {
            if (!visited.Add(typeParameter))
                return false;

            if (constraint.SpecialType == SpecialType.System_ValueType &&
                (typeParameter.GenericConstraint & GenericConstraintsFlags.StructConstraint) != 0)
            {
                return true;
            }

            foreach (var bound in typeParameter.ConstraintTypes)
            {
                if (LocalScopeBinder.AreSameType(bound, constraint))
                    return true;
                if (bound is TypeParameterSymbol other
                    ? IsSatisfiedByTypeParameter(other, constraint, visited)
                    : ConvertsByReferenceOrBoxing(bound, constraint))
                {
                    return true;
                }
            }
            return false;
        }
        private static bool ConvertsByReferenceOrBoxing(TypeSymbol source, TypeSymbol target)
        {
            if (target is NamedTypeSymbol { TypeKind: TypeKind.Interface })
                return ImplementsInterface(source, target, new HashSet<TypeSymbol>(ReferenceEqualityComparer<TypeSymbol>.Instance));

            for (var current = source.BaseType; current is not null; current = current.BaseType)
            {
                if (LocalScopeBinder.AreSameType(current, target))
                    return true;
            }

            // Array covariance between reference element types
            return source is ArrayTypeSymbol sourceArray &&
                   target is ArrayTypeSymbol targetArray &&
                   sourceArray.Rank == targetArray.Rank &&
                   sourceArray.IsSZArray == targetArray.IsSZArray &&
                   sourceArray.ElementType.IsReferenceType &&
                   targetArray.ElementType.IsReferenceType &&
                   SatisfiesTypeConstraint(sourceArray.ElementType, targetArray.ElementType);
        }
        private static bool ImplementsInterface(TypeSymbol type, TypeSymbol iface, HashSet<TypeSymbol> visited)
        {
            if (!visited.Add(type))
                return false;

            foreach (var implemented in type.Interfaces)
            {
                if (LocalScopeBinder.AreSameType(implemented, iface) || ImplementsInterface(implemented, iface, visited))
                    return true;
            }
            return type.BaseType is TypeSymbol baseType && ImplementsInterface(baseType, iface, visited);
        }
        /// <summary>Returns whether 'new T()' can construct a type argument</summary>
        public static bool HasPublicParameterlessConstructor(TypeSymbol type)
        {
            if (type is TypeParameterSymbol typeParameter)
            {
                return (typeParameter.GenericConstraint &
                    (GenericConstraintsFlags.ConstructorConstraint | GenericConstraintsFlags.StructConstraint)) != 0;
            }
            if (type.IsValueType)
                return true;
            if (type is not NamedTypeSymbol { TypeKind: TypeKind.Class } named || named.OriginalDefinition.IsAbstract)
                return false;

            foreach (var member in named.GetMembers())
            {
                if (member is MethodSymbol { IsConstructor: true, IsStatic: false } constructor && constructor.Parameters.Length == 0)
                    return constructor.DeclaredAccessibility == Accessibility.Public;
            }
            return false;
        }
        /// <summary>Returns whether a type has recursively unmanaged storage</summary>
        public static bool IsUnmanagedType(TypeSymbol t)
        {
            var visiting = new HashSet<TypeSymbol>(ReferenceEqualityComparer<TypeSymbol>.Instance);
            return IsUnmanagedTypeCore(t, visiting);
        }
        private static bool IsUnmanagedTypeCore(TypeSymbol t, HashSet<TypeSymbol> visiting)
        {
            if (t is TypeParameterSymbol tp)
                return (tp.GenericConstraint & GenericConstraintsFlags.UnmanagedConstraint) != 0;
            if (t is PointerTypeSymbol or FunctionPointerTypeSymbol)
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

                // A repeated value type is already being validated on this path
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
    /// <summary>Validates constructed type and method arguments against declared constraints</summary>
    internal static class GenericConstraintChecker
    {
        /// <summary>Checks type arguments for a constructed named type</summary>
        public static bool CheckNamedTypeInstantiation(
            NamedTypeSymbol constructedType,
            ImmutableArray<TypeSymbol> typeArguments,
            Func<int, TextSpan> getArgSpan,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (context.DefersGenericConstraintChecks)
                return true;

            var tree = context.SemanticModel.SyntaxTree;
            return CheckNamedType(constructedType, typeArguments, i => new Location(tree, getArgSpan(i)), context.Compilation.TypeManager, diagnostics);
        }
        /// <summary>Checks type arguments for a constructed method</summary>
        public static bool CheckMethodInstantiation(
            MethodSymbol methodDefinition,
            ImmutableArray<TypeSymbol> typeArguments,
            Func<int, TextSpan> getArgSpan,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (context.DefersGenericConstraintChecks)
                return true;

            var typeParameters = methodDefinition.TypeParameters;
            // A method of a constructed type sees that type's arguments in its constraints
            var map = methodDefinition.ContainingSymbol is SubstitutedNamedTypeSymbol containing
                ? containing.SubstitutionMap
                : ImmutableDictionary<TypeParameterSymbol, TypeSymbol>.Empty;
            var tree = context.SemanticModel.SyntaxTree;
            return CheckCore(
                methodDefinition.Name,
                typeParameters,
                typeArguments,
                AddArguments(map, typeParameters, typeArguments),
                i => new Location(tree, getArgSpan(i)),
                context.Compilation.TypeManager,
                diagnostics);
        }
        /// <summary>Checks every constructed type that a declared signature mentions</summary>
        public static void CheckDeclarations(Compilation compilation, ImmutableArray<SyntaxTree> trees, DiagnosticBag diagnostics)
        {
            var types = compilation.TypeManager;
            var seen = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
            foreach (var tree in trees)
            {
                if (!compilation.DeclaredSymbolsByTree.TryGetValue(tree, out var declMap))
                    continue;

                foreach (var kv in declMap)
                {
                    var symbol = kv.Value;
                    if (!seen.Add(symbol))
                        continue;

                    var location = symbol.Locations.IsDefaultOrEmpty ? new Location(tree, kv.Key.Span) : symbol.Locations[0];
                    switch (symbol)
                    {
                        case NamedTypeSymbol type:
                            if (type.BaseType is TypeSymbol baseType)
                                CheckTypeDeep(baseType, location, types, diagnostics);
                            foreach (var iface in type.Interfaces)
                                CheckTypeDeep(iface, location, types, diagnostics);
                            CheckConstraintTypes(type.TypeParameters, location, types, diagnostics);
                            break;
                        case MethodSymbol method:
                            CheckTypeDeep(method.ReturnType, location, types, diagnostics);
                            foreach (var parameter in method.Parameters)
                                CheckTypeDeep(parameter.Type, location, types, diagnostics);
                            CheckConstraintTypes(method.TypeParameters, location, types, diagnostics);
                            break;
                        case FieldSymbol field:
                            CheckTypeDeep(field.Type, location, types, diagnostics);
                            break;
                        case PropertySymbol property:
                            CheckTypeDeep(property.Type, location, types, diagnostics);
                            foreach (var parameter in property.Parameters)
                                CheckTypeDeep(parameter.Type, location, types, diagnostics);
                            break;
                    }
                }
            }
        }
        internal static void CheckConstraintTypes(ImmutableArray<TypeParameterSymbol> typeParameters, Location location, TypeManager types, DiagnosticBag diagnostics)
        {
            foreach (var typeParameter in typeParameters)
            {
                foreach (var constraint in typeParameter.ConstraintTypes)
                    CheckTypeDeep(constraint, location, types, diagnostics);
            }
        }
        /// <summary>Checks a type and every constructed type nested in it</summary>
        internal static void CheckTypeDeep(TypeSymbol type, Location location, TypeManager types, DiagnosticBag diagnostics)
        {
            switch (type)
            {
                case ArrayTypeSymbol array:
                    CheckTypeDeep(array.ElementType, location, types, diagnostics);
                    break;
                case PointerTypeSymbol pointer:
                    CheckTypeDeep(pointer.PointedAtType, location, types, diagnostics);
                    break;
                case ByRefTypeSymbol byRef:
                    CheckTypeDeep(byRef.ElementType, location, types, diagnostics);
                    break;
                case TupleTypeSymbol tuple:
                    foreach (var element in tuple.ElementTypes)
                        CheckTypeDeep(element, location, types, diagnostics);
                    break;
                case FunctionPointerTypeSymbol functionPointer:
                    CheckTypeDeep(functionPointer.ReturnType, location, types, diagnostics);
                    foreach (var parameter in functionPointer.Parameters)
                        CheckTypeDeep(parameter.Type, location, types, diagnostics);
                    break;
                case SubstitutedNamedTypeSymbol named:
                    if (named.ContainingTypeOpt is NamedTypeSymbol containing)
                        CheckTypeDeep(containing, location, types, diagnostics);
                    foreach (var argument in named.TypeArguments)
                        CheckTypeDeep(argument, location, types, diagnostics);
                    CheckNamedType(named, named.TypeArguments, _ => location, types, diagnostics);
                    break;
            }
        }
        private static bool CheckNamedType(
            NamedTypeSymbol constructedType,
            ImmutableArray<TypeSymbol> typeArguments,
            Func<int, Location> getArgLocation,
            TypeManager types,
            DiagnosticBag diagnostics)
        {
            var typeParameters = constructedType.TypeParameters;
            var map = constructedType is SubstitutedNamedTypeSymbol substituted
                ? substituted.SubstitutionMap
                : ImmutableDictionary<TypeParameterSymbol, TypeSymbol>.Empty;
            return CheckCore(
                constructedType.OriginalDefinition.Name,
                typeParameters,
                typeArguments,
                AddArguments(map, typeParameters, typeArguments),
                getArgLocation,
                types,
                diagnostics);
        }
        private static ImmutableDictionary<TypeParameterSymbol, TypeSymbol> AddArguments(
            ImmutableDictionary<TypeParameterSymbol, TypeSymbol> map,
            ImmutableArray<TypeParameterSymbol> typeParameters,
            ImmutableArray<TypeSymbol> typeArguments)
        {
            if (typeParameters.IsDefaultOrEmpty || typeArguments.IsDefaultOrEmpty)
                return map;

            var builder = map.ToBuilder();
            int n = Math.Min(typeParameters.Length, typeArguments.Length);
            for (int i = 0; i < n; i++)
                builder[typeParameters[i]] = typeArguments[i];
            return builder.ToImmutable();
        }
        private static bool CheckCore(
            string ownerDisplayName,
            ImmutableArray<TypeParameterSymbol> typeParameters,
            ImmutableArray<TypeSymbol> typeArguments,
            ImmutableDictionary<TypeParameterSymbol, TypeSymbol> map,
            Func<int, Location> getArgLocation,
            TypeManager types,
            DiagnosticBag diagnostics)
        {
            if (typeParameters.IsDefaultOrEmpty || typeArguments.IsDefaultOrEmpty)
                return true;
            int n = Math.Min(typeParameters.Length, typeArguments.Length);
            bool ok = true;

            void Report(string id, int index, string message, DiagnosticSeverity severity = DiagnosticSeverity.Error)
            {
                if (severity == DiagnosticSeverity.Error)
                    ok = false;
                diagnostics.Add(new Diagnostic(id, severity, message, getArgLocation(index)));
            }

            for (int i = 0; i < n; i++)
            {
                var tp = typeParameters[i];
                var arg = typeArguments[i];
                if (arg.Kind == SymbolKind.Error)
                    continue;
                var constraints = tp.GenericConstraint;
                // Allows ref-like type arguments
                if ((constraints & GenericConstraintsFlags.AllowsRefStruct) == 0
                    && RefLikeRestrictionFacts.ContainsRefLike(arg))
                {
                    Report("CN_GENCONSTR_BYREFLIKE", i,
                        $"Ref-like type '{arg.Name}' cannot be used as a type argument for '{tp.Name}' " +
                        $"in '{ownerDisplayName}' unless '{tp.Name}' has 'allows ref struct'.");
                }
                // Requires unmanaged storage
                if ((constraints & GenericConstraintsFlags.UnmanagedConstraint) != 0
                    && !GenericConstraintFacts.IsUnmanagedType(arg))
                {
                    Report("CN_GENCONSTR_UNMANAGED", i,
                        $"The type '{arg.Name}' must be unmanaged to satisfy " +
                        $"the 'unmanaged' constraint on '{tp.Name}' in '{ownerDisplayName}'.");
                }
                if ((constraints & GenericConstraintsFlags.ClassConstraint) != 0
                    && !arg.IsReferenceType)
                {
                    Report("CN_GENCONSTR_CLASS", i,
                        $"The type '{arg.Name}' must be a reference type to satisfy " +
                        $"the 'class' constraint on '{tp.Name}' in '{ownerDisplayName}'.");
                }
                // Requires a non-nullable value type
                if ((constraints & GenericConstraintsFlags.UnmanagedConstraint) == 0
                    && (constraints & GenericConstraintsFlags.StructConstraint) != 0
                    && !GenericConstraintFacts.IsNonNullableValueType(arg))
                {
                    Report("CN_GENCONSTR_STRUCT", i,
                        $"The type '{arg.Name}' must be a non-nullable value type to satisfy " +
                        $"the 'struct' constraint on '{tp.Name}' in '{ownerDisplayName}'.");
                }
                // 'notnull' is a nullability contract, so a violation only warns
                if ((constraints & (GenericConstraintsFlags.StructConstraint
                    | GenericConstraintsFlags.UnmanagedConstraint)) == 0
                    && (constraints & GenericConstraintsFlags.NotNullConstraint) != 0
                    && !GenericConstraintFacts.IsNotNullType(arg))
                {
                    Report("CN_GENCONSTR_NOTNULL", i,
                        $"The type '{arg.Name}' should be non-nullable to satisfy " +
                        $"the 'notnull' constraint on '{tp.Name}' in '{ownerDisplayName}'.",
                        DiagnosticSeverity.Warning);
                }
                if ((constraints & GenericConstraintsFlags.ConstructorConstraint) != 0
                    && !GenericConstraintFacts.HasPublicParameterlessConstructor(arg))
                {
                    Report("CN_GENCONSTR_NEW", i,
                        $"The type '{arg.Name}' must be a non-abstract type with a public parameterless constructor " +
                        $"to satisfy the 'new()' constraint on '{tp.Name}' in '{ownerDisplayName}'.");
                }
                foreach (var constraintType in tp.ConstraintTypes)
                {
                    if (constraintType.Kind == SymbolKind.Error)
                        continue;
                    var required = TypeSubstituter.Substitute(constraintType, types, map);
                    if (!GenericConstraintFacts.SatisfiesTypeConstraint(arg, required))
                    {
                        Report("CN_GENCONSTR_TYPE", i,
                            $"The type '{arg.Name}' cannot be used as type argument '{tp.Name}' in '{ownerDisplayName}': " +
                            $"there is no implicit reference, boxing, or type parameter conversion to '{required.Name}'.");
                    }
                }
            }
            return ok;
        }
    }
    /// <summary>Detects ref-like storage through composite types</summary>
    internal static class RefLikeRestrictionFacts
    {
        /// <summary>Returns whether a type directly or transitively contains ref-like storage</summary>
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
                case FunctionPointerTypeSymbol:
                    return false;
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
    /// <summary>Binds generic constraint clauses to source type parameters</summary>
    internal static class GenericConstraintBinder
    {
        /// <summary>Binds constraint clauses for every declared generic owner</summary>
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

                        case ExtensionBlockDeclarationSyntax eb when kv.Value is NamedTypeSymbol grouping:
                            BindOwnerConstraintClauses(tree, eb.ConstraintClauses, grouping.TypeParameters, grouping, diagnostics);
                            break;
                    }
                }
                // Implementation methods repeat the block's clauses on their copies; the grouping type reports their errors
                foreach (var implementation in EnumerateExtensionImplementations(declMap))
                {
                    if (implementation.ExtensionMember!.BlockSyntax?.Node is ExtensionBlockDeclarationSyntax blockSyntax)
                        BindOwnerConstraintClauses(tree, blockSyntax.ConstraintClauses, implementation.TypeParameters, implementation, new DiagnosticBag());
                }
            }
        }
        internal static IEnumerable<SourceMethodSymbol> EnumerateExtensionImplementations(ImmutableDictionary<SyntaxNode, Symbol> declMap)
        {
            var seen = new HashSet<SourceMethodSymbol>(ReferenceEqualityComparer<SourceMethodSymbol>.Instance);
            foreach (var symbol in declMap.Values)
            {
                if (symbol is SourceMethodSymbol { ExtensionMember: not null } method)
                {
                    if (seen.Add(method))
                        yield return method;
                }
                else if (symbol is SourcePropertySymbol property)
                {
                    if (property.GetMethod is SourceMethodSymbol { ExtensionMember: not null } getter && seen.Add(getter))
                        yield return getter;
                    if (property.SetMethod is SourceMethodSymbol { ExtensionMember: not null } setter && seen.Add(setter))
                        yield return setter;
                }
            }
        }
        internal static void BindOwnerTypeConstraints(
            SyntaxTree tree,
            SyntaxList<TypeParameterConstraintClauseSyntax> clauses,
            ImmutableArray<TypeParameterSymbol> typeParameters,
            Symbol owner,
            Binder typeBinder,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (clauses.Count == 0 || typeParameters.IsDefaultOrEmpty)
                return;

            // A clause may name a type whose constraints mention these parameters, so check only once all are bound
            bool checkAfterBinding = !context.DefersGenericConstraintChecks;
            context.SuppressGenericConstraintChecks = true;
            for (int c = 0; c < clauses.Count; c++)
            {
                var clause = clauses[c];
                var tpName = clause.Name.ValueText ?? string.Empty;
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
                    continue;

                var constraints = clause.Constraints;
                for (int i = 0; i < constraints.Count; i++)
                {
                    if (constraints[i] is not TypeConstraintSyntax typeConstraint)
                        continue;

                    if (typeConstraint.Type is IdentifierNameSyntax id)
                    {
                        var text = id.Identifier.ValueText ?? string.Empty;
                        if (string.Equals(text, "unmanaged", StringComparison.Ordinal) ||
                            string.Equals(text, "notnull", StringComparison.Ordinal))
                        {
                            continue;
                        }
                    }

                    var type = typeBinder.BindType(typeConstraint.Type, context, diagnostics);
                    if (type is not ErrorTypeSymbol)
                        tp.AddConstraintType(type);
                }
            }
            context.SuppressGenericConstraintChecks = false;
            if (checkAfterBinding)
                GenericConstraintChecker.CheckConstraintTypes(typeParameters, new Location(tree, clauses[0].Span), context.Compilation.TypeManager, diagnostics);
        }

        /// <summary>Binds and validates constraint clauses for one generic owner</summary>
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
                        case ClassOrStructConstraintSyntax s when s.Kind == SyntaxKind.ClassConstraint:
                            if (!tp.TrySetConstraint(GenericConstraintsFlags.ClassConstraint))
                            {
                                diagnostics.Add(new Diagnostic(
                                    id: "CN_GENCONSTR_DUP005",
                                    severity: DiagnosticSeverity.Error,
                                    message: $"Duplicate 'class' constraint for type parameter '{tp.Name}'.",
                                    location: new Location(tree, s.Span)));
                            }
                            break;
                        case ConstructorConstraintSyntax ctor:
                            if (!tp.TrySetConstraint(GenericConstraintsFlags.ConstructorConstraint))
                            {
                                diagnostics.Add(new Diagnostic(
                                    id: "CN_GENCONSTR_DUP006",
                                    severity: DiagnosticSeverity.Error,
                                    message: $"Duplicate 'new()' constraint for type parameter '{tp.Name}'.",
                                    location: new Location(tree, ctor.Span)));
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
