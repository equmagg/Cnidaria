using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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

                        case MethodDeclarationSyntax md when kv.Value is MethodSymbol ms:
                            BindOwnerConstraintClauses(tree, md.ConstraintClauses, ms.TypeParameters, ms, diagnostics);
                            break;
                    }
                }
            }
        }
        private static void BindOwnerConstraintClauses(
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
    internal static class MemberSignatureBinder
    {
        private sealed class NullRecorder : IBindingRecorder
        {
            public static readonly NullRecorder Instance = new();
            public void RecordBound(SyntaxNode syntax, BoundNode bound) { }
            public void RecordDeclared(SyntaxNode syntax, Symbol symbol) { }
        }
        private sealed class SemanticModelStub : SemanticModel
        {
            public SemanticModelStub(Compilation c, SyntaxTree t)
            : base(c, t, ignoresAccessibility: true) { }

            public override ImmutableArray<Diagnostic> GetDiagnostics(TextSpan? span = null, CancellationToken cancellationToken = default)
                => ImmutableArray<Diagnostic>.Empty;

            public override Symbol? GetDeclaredSymbol(SyntaxNode declaration, CancellationToken cancellationToken = default) => null;
            public override SymbolInfo GetSymbolInfo(SyntaxNode node, CancellationToken cancellationToken = default) => SymbolInfo.None;
            public override Symbol? GetAliasInfo(NameSyntax nameSyntax, CancellationToken cancellationToken = default) => null;
            public override TypeInfo GetTypeInfo(ExpressionSyntax expr, CancellationToken cancellationToken = default)
                => new TypeInfo(null, null);
            public override Optional<object> GetConstantValue(ExpressionSyntax expr, CancellationToken cancellationToken = default)
                => Optional<object>.None;
            public override Conversion GetConversion(ExpressionSyntax expr, CancellationToken cancellationToken = default)
                => new Conversion(ConversionKind.None);
            public override Symbol? GetEnclosingSymbol(int position, CancellationToken cancellationToken = default) => null;
            public override ImmutableArray<Symbol> LookupSymbols(int position, string? name = null, CancellationToken cancellationToken = default)
                => ImmutableArray<Symbol>.Empty;

            internal override BoundNode GetBoundNode(SyntaxNode node, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();
        }
        public static void BindAll(Compilation compilation, ImmutableArray<SyntaxTree> trees, DiagnosticBag diagnostics)
        {
            var pendingConstFields = new List<PendingConstField>();
            var pendingOptionalParameters = new List<PendingOptionalParameter>();
            foreach (var tree in trees)
            {
                var stubModel = new SemanticModelStub(compilation, tree);
                var importScopeMap = ImportsBuilder.BuildImportScopeMap(compilation, tree, NullRecorder.Instance, diagnostics);
                if (!compilation.DeclaredSymbolsByTree.TryGetValue(tree, out var declMap))
                    continue;
                var safeTypeBinder = new TypeBinder(
                    parent: null, flags: BinderFlags.None, compilation: compilation, importScopeMap: importScopeMap);
                var unsafeTypeBinder = new TypeBinder(
                    parent: null, flags: BinderFlags.UnsafeRegion, compilation: compilation, importScopeMap: importScopeMap);
                BindEnumUnderlyingTypesForTree(
                    compilation, tree, declMap, stubModel, safeTypeBinder, diagnostics);
                foreach (var kv in declMap)
                {
                    if (kv.Key is MethodDeclarationSyntax md && kv.Value is SourceMethodSymbol sm)
                    {
                        var ctx = new BindingContext(compilation, stubModel, sm, NullRecorder.Instance);

                        var typeBinder = HasModifier(md.Modifiers, SyntaxKind.UnsafeKeyword) ? unsafeTypeBinder : safeTypeBinder;
                        var rt = typeBinder.BindType(md.ReturnType, ctx, diagnostics);
                        sm.SetReturnType(rt);

                        var pars = md.ParameterList.Parameters;
                        for (int i = 0; i < pars.Count && i < sm.Parameters.Length; i++)
                        {
                            var pt = BindParameterType(typeBinder, pars[i], ctx, diagnostics, out var isReadOnlyRef);
                            sm.Parameters[i].Type = pt;
                            sm.Parameters[i].IsReadOnlyRef = isReadOnlyRef;
                            if (pars[i].Default is not null)
                                pendingOptionalParameters.Add(
                                    new PendingOptionalParameter(tree, pars[i], sm.Parameters[i], sm, typeBinder, stubModel));
                        }
                    }
                    else if (kv.Key is ConstructorDeclarationSyntax cd && kv.Value is SourceMethodSymbol ctor)
                    {
                        var ctx = new BindingContext(compilation, stubModel, ctor, NullRecorder.Instance);

                        var typeBinder = HasModifier(cd.Modifiers, SyntaxKind.UnsafeKeyword) ? unsafeTypeBinder : safeTypeBinder;

                        var pars = cd.ParameterList.Parameters;
                        for (int i = 0; i < pars.Count && i < ctor.Parameters.Length; i++)
                        {
                            var pt = BindParameterType(typeBinder, pars[i], ctx, diagnostics, out var isReadOnlyRef);
                            ctor.Parameters[i].Type = pt;
                            ctor.Parameters[i].IsReadOnlyRef = isReadOnlyRef;
                            if (pars[i].Default is not null)
                                pendingOptionalParameters.Add(
                                    new PendingOptionalParameter(tree, pars[i], ctor.Parameters[i], ctor, typeBinder, stubModel));
                        }
                    }
                    else if (kv.Key is OperatorDeclarationSyntax od && kv.Value is SourceMethodSymbol opMethod)
                    {
                        var ctx = new BindingContext(compilation, stubModel, opMethod, NullRecorder.Instance);

                        var typeBinder = HasModifier(od.Modifiers, SyntaxKind.UnsafeKeyword) ? unsafeTypeBinder : safeTypeBinder;
                        var rt = typeBinder.BindType(od.ReturnType, ctx, diagnostics);
                        opMethod.SetReturnType(rt);

                        var pars = od.ParameterList.Parameters;
                        for (int i = 0; i < pars.Count && i < opMethod.Parameters.Length; i++)
                        {
                            var pt = BindParameterType(typeBinder, pars[i], ctx, diagnostics, out var isReadOnlyRef);
                            opMethod.Parameters[i].Type = pt;
                            opMethod.Parameters[i].IsReadOnlyRef = isReadOnlyRef;
                        }
                    }
                    else if (kv.Key is ConversionOperatorDeclarationSyntax cod && kv.Value is SourceMethodSymbol convMethod)
                    {
                        var ctx = new BindingContext(compilation, stubModel, convMethod, NullRecorder.Instance);

                        var typeBinder = HasModifier(cod.Modifiers, SyntaxKind.UnsafeKeyword) ? unsafeTypeBinder : safeTypeBinder;
                        var rt = typeBinder.BindType(cod.Type, ctx, diagnostics);
                        convMethod.SetReturnType(rt);

                        var pars = cod.ParameterList.Parameters;
                        for (int i = 0; i < pars.Count && i < convMethod.Parameters.Length; i++)
                        {
                            var pt = BindParameterType(typeBinder, pars[i], ctx, diagnostics, out var isReadOnlyRef);
                            convMethod.Parameters[i].Type = pt;
                            convMethod.Parameters[i].IsReadOnlyRef = isReadOnlyRef;
                        }
                    }
                    else if (kv.Key is VariableDeclaratorSyntax vd && kv.Value is SourceFieldSymbol field)
                    {
                        if (field.DeclaredTypeSyntax is null)
                            continue;

                        var ctx = new BindingContext(compilation, stubModel, field, NullRecorder.Instance);
                        var typeBinder = field.IsUnsafe ? unsafeTypeBinder : safeTypeBinder;
                        var ft = typeBinder.BindType(field.DeclaredTypeSyntax, ctx, diagnostics);
                        field.SetType(ft);
                        ValidateFieldTypeRestrictions(field, ft, tree, vd, diagnostics);
                        if (field.IsConst)
                        {
                            if (vd.Initializer is null)
                            {
                                diagnostics.Add(new Diagnostic(
                                    "CN_CONSTFIELD001",
                                    DiagnosticSeverity.Error,
                                    "Const field must have an initializer.",
                                    new Location(ctx.SemanticModel.SyntaxTree, vd.Span)));
                            }
                            else
                            {
                                pendingConstFields.Add(new PendingConstField(
                                     tree,
                                     vd,
                                     field,
                                     typeBinder,
                                     stubModel));
                            }
                        }
                    }
                    else if (kv.Key is PropertyDeclarationSyntax pd && kv.Value is SourcePropertySymbol prop)
                    {
                        var ctx = new BindingContext(compilation, stubModel, prop, NullRecorder.Instance);

                        var typeBinder = HasModifier(pd.Modifiers, SyntaxKind.UnsafeKeyword) ? unsafeTypeBinder : safeTypeBinder;
                        var pt = typeBinder.BindType(pd.Type, ctx, diagnostics);
                        prop.SetType(pt);

                        if (prop.GetMethod is SourceMethodSymbol get)
                        {
                            get.SetReturnType(pt);
                        }
                        if (prop.SetMethod is SourceMethodSymbol set && set.Parameters.Length == 1)
                        {
                            set.Parameters[0].Type = pt;
                        }
                        if (IsAutoProperty(pd) &&
                            prop.BackingFieldOpt is null &&
                            prop.ContainingSymbol is NamedTypeSymbol ownerType)
                        {
                            string backingName = $"<{prop.Name}>k__BackingField";
                            var backing = new SynthesizedBackingFieldSymbol(
                                name: backingName,
                                containing: ownerType,
                                placeholderType: pt,
                                isStatic: prop.IsStatic,
                                isReadOnly: false);
                            bool added = ownerType switch
                            {
                                SourceNamedTypeSymbol srcType => AddToSource(srcType, backing),
                                SpecialNamedTypeSymbol specialType => AddToSpecial(specialType, backing),
                                _ => false
                            };

                            if (added)
                                prop.SetBackingField(backing);

                            static bool AddToSource(SourceNamedTypeSymbol t, Symbol m)
                            {
                                t.AddMember(m);
                                return true;
                            }

                            static bool AddToSpecial(SpecialNamedTypeSymbol t, Symbol m)
                            {
                                t.AddMember(m);
                                return true;
                            }
                        }
                    }
                    else if (kv.Key is IndexerDeclarationSyntax idx && kv.Value is SourcePropertySymbol prop2)
                    {
                        var ctx = new BindingContext(compilation, stubModel, prop2, NullRecorder.Instance);

                        var typeBinder = HasModifier(idx.Modifiers, SyntaxKind.UnsafeKeyword) ? unsafeTypeBinder : safeTypeBinder;
                        var pt = typeBinder.BindType(idx.Type, ctx, diagnostics);
                        prop2.SetType(pt);

                        var propParams = prop2.Parameters;
                        var idxPars = idx.ParameterList.Parameters;

                        for (int i = 0; i < idxPars.Count && i < propParams.Length; i++)
                        {
                            var pType = BindParameterType(typeBinder, idxPars[i], ctx, diagnostics, out var isReadOnlyRef);

                            propParams[i].Type = pType;
                            propParams[i].IsReadOnlyRef = isReadOnlyRef;

                            if (prop2.GetMethod is SourceMethodSymbol get && i < get.Parameters.Length)
                            {
                                get.Parameters[i].Type = pType;
                                get.Parameters[i].IsReadOnlyRef = isReadOnlyRef;
                            }

                            if (prop2.SetMethod is SourceMethodSymbol set && i < set.Parameters.Length - 1)
                            {
                                set.Parameters[i].Type = pType;
                                set.Parameters[i].IsReadOnlyRef = isReadOnlyRef;
                            }
                        }

                        if (prop2.GetMethod is SourceMethodSymbol getMethod)
                            getMethod.SetReturnType(pt);

                        if (prop2.SetMethod is SourceMethodSymbol setMethod && setMethod.Parameters.Length > 0)
                            setMethod.Parameters[setMethod.Parameters.Length - 1].Type = pt;

                    }
                }
                BindEnumMembersForTree(
                    compilation, tree, declMap, stubModel, safeTypeBinder, diagnostics);
            }
            BindPendingConstFields(compilation, pendingConstFields, diagnostics);
            BindPendingOptionalParameters(compilation, pendingOptionalParameters, diagnostics);
        }
        private readonly struct PendingConstField
        {
            public readonly SyntaxTree Tree;
            public readonly VariableDeclaratorSyntax Declarator;
            public readonly SourceFieldSymbol Field;
            public readonly TypeBinder TypeBinder;
            public readonly SemanticModel StubModel;

            public PendingConstField(
                SyntaxTree tree,
                VariableDeclaratorSyntax declarator,
                SourceFieldSymbol field,
                TypeBinder typeBinder,
                SemanticModel stubModel)
            {
                Tree = tree;
                Declarator = declarator;
                Field = field;
                TypeBinder = typeBinder;
                StubModel = stubModel;
            }
        }
        private readonly struct PendingOptionalParameter
        {
            public readonly SyntaxTree Tree;
            public readonly ParameterSyntax ParameterSyntax;
            public readonly ParameterSymbol Parameter;
            public readonly MethodSymbol ContainingMethod;
            public readonly TypeBinder TypeBinder;
            public readonly SemanticModel StubModel;

            public PendingOptionalParameter(
                SyntaxTree tree,
                ParameterSyntax parameterSyntax,
                ParameterSymbol parameter,
                MethodSymbol containingMethod,
                TypeBinder typeBinder,
                SemanticModel stubModel)
            {
                Tree = tree;
                ParameterSyntax = parameterSyntax;
                Parameter = parameter;
                ContainingMethod = containingMethod;
                TypeBinder = typeBinder;
                StubModel = stubModel;
            }
        }
        private static void BindPendingConstFields(
            Compilation compilation,
            List<PendingConstField> pendingConstFields,
            DiagnosticBag diagnostics)
        {
            if (pendingConstFields.Count == 0)
                return;

            bool madeProgress;
            do
            {
                madeProgress = false;

                for (int i = 0; i < pendingConstFields.Count; i++)
                {
                    var pending = pendingConstFields[i];
                    if (pending.Field.ConstantValueOpt.HasValue)
                        continue;

                    if (TryBindConstFieldInitializer(
                        compilation: compilation,
                        pending: pending,
                        reportDiagnostics: false,
                        diagnostics: diagnostics,
                        constantValueOpt: out var constantValueOpt))
                    {
                        pending.Field.SetConstantValue(constantValueOpt);
                        madeProgress = true;
                    }
                }
            }
            while (madeProgress);

            for (int i = 0; i < pendingConstFields.Count; i++)
            {
                var pending = pendingConstFields[i];
                if (pending.Field.ConstantValueOpt.HasValue)
                    continue;

                if (!TryBindConstFieldInitializer(
                    compilation: compilation,
                    pending: pending,
                    reportDiagnostics: true,
                    diagnostics: diagnostics,
                    constantValueOpt: out _))
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_CONSTFIELD002",
                        DiagnosticSeverity.Error,
                        "Const field initializer is not a compile-time constant.",
                        new Location(pending.Tree, pending.Declarator.Initializer!.Span)));
                }
            }
        }
        private static bool TryBindConstFieldInitializer(
            Compilation compilation,
            in PendingConstField pending,
            bool reportDiagnostics,
            DiagnosticBag diagnostics,
            out Optional<object> constantValueOpt)
        {
            constantValueOpt = Optional<object>.None;

            var ctx = new BindingContext(compilation, pending.StubModel, pending.Field, NullRecorder.Instance);
            var exprBinder = new LocalScopeBinder(
                parent: pending.TypeBinder,
                flags: pending.Field.IsUnsafe ? BinderFlags.UnsafeRegion : BinderFlags.None,
                containing: pending.Field,
                inheritFlowFromParent: false);
            var sink = reportDiagnostics ? diagnostics : new DiagnosticBag();
            var init = exprBinder.BindExpression(pending.Declarator.Initializer!.Value, ctx, sink);

            init = exprBinder.ApplyConversion(
                exprSyntax: pending.Declarator.Initializer.Value,
                expr: init,
                targetType: pending.Field.Type,
                diagnosticNode: pending.Declarator,
                context: ctx,
                diagnostics: sink,
                requireImplicit: true);

            if (!init.ConstantValueOpt.HasValue)
                return false;

            constantValueOpt = init.ConstantValueOpt;
            return true;
        }
        private static void BindPendingOptionalParameters(
    Compilation compilation,
    List<PendingOptionalParameter> pending,
    DiagnosticBag diagnostics)
        {
            for (int i = 0; i < pending.Count; i++)
            {
                var p = pending[i];
                if (p.Parameter.HasExplicitDefault)
                    continue;

                if (!TryBindOptionalParameterDefault(compilation, p, diagnostics, out var c))
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_PARAMDEFAULT001",
                        DiagnosticSeverity.Error,
                        "Optional parameter default value must be a compile-time constant.",
                        new Location(p.Tree, p.ParameterSyntax.Default!.Span)));
                    continue;
                }

                p.Parameter.SetDefaultValue(c);
            }
        }

        private static bool TryBindOptionalParameterDefault(
            Compilation compilation,
            in PendingOptionalParameter pending,
            DiagnosticBag diagnostics,
            out Optional<object> constantValueOpt)
        {
            constantValueOpt = Optional<object>.None;
            var def = pending.ParameterSyntax.Default;
            if (def is null)
                return false;

            if (pending.Parameter.RefKind != ParameterRefKind.None)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_PARAMDEFAULT002",
                    DiagnosticSeverity.Error,
                    "Optional parameters cannot be ref/out/in.",
                    new Location(pending.Tree, def.Span)));
                return false;
            }

            if (pending.Parameter.IsParams)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_PARAMDEFAULT003",
                    DiagnosticSeverity.Error,
                    "'params' parameters cannot have a default value.",
                    new Location(pending.Tree, def.Span)));
                return false;
            }

            var ctx = new BindingContext(compilation, pending.StubModel, pending.ContainingMethod, NullRecorder.Instance);
            var exprBinder = new LocalScopeBinder(
                parent: pending.TypeBinder,
                flags: pending.TypeBinder.Flags,
                containing: pending.ContainingMethod,
                inheritFlowFromParent: false);

            var init = exprBinder.BindExpression(def.Value, ctx, diagnostics);
            init = exprBinder.ApplyConversion(
                exprSyntax: def.Value,
                expr: init,
                targetType: pending.Parameter.Type,
                diagnosticNode: pending.ParameterSyntax,
                context: ctx,
                diagnostics: diagnostics,
                requireImplicit: true);

            if (!init.ConstantValueOpt.HasValue)
                return false;

            constantValueOpt = init.ConstantValueOpt;
            return true;
        }
        private static bool CanContainRefLikeField(FieldSymbol field)
        {
            // Allowed only as instance field of a ref struct
            return field.ContainingSymbol is NamedTypeSymbol owner &&
                   owner.IsRefLikeType &&
                   !field.IsStatic;
        }
        private static void ValidateFieldTypeRestrictions(
            FieldSymbol field,
            TypeSymbol fieldType,
            SyntaxTree tree,
            SyntaxNode diagnosticNode,
            DiagnosticBag diagnostics)
        {
            if (fieldType is ErrorTypeSymbol)
                return;

            // ref fields are only valid in instance fields of ref struct
            if (fieldType is ByRefTypeSymbol && !CanContainRefLikeField(field))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REFLIKE_FIELD001",
                    DiagnosticSeverity.Error,
                    "Ref fields are only allowed as instance fields of a ref struct.",
                    new Location(tree, diagnosticNode.Span)));
                return;
            }

            // ref fields are only valid in instance fields of ref struct
            if (RefLikeRestrictionFacts.ContainsRefLike(fieldType) && !CanContainRefLikeField(field))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REFLIKE_FIELD002",
                    DiagnosticSeverity.Error,
                    "Fields of ref-like type are only allowed as instance fields of a ref struct.",
                    new Location(tree, diagnosticNode.Span)));
            }

            // instance fields of a readonly struct must be readonly
            if (field.ContainingSymbol is NamedTypeSymbol ownerType &&
                ownerType.IsReadOnlyStruct &&
                !field.IsStatic &&
                !field.IsReadOnly)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_READONLY_STRUCT001",
                    DiagnosticSeverity.Error,
                    "Instance fields of a readonly struct must be readonly.",
                    new Location(tree, diagnosticNode.Span)));
            }
        }
        private static bool IsByRefParameter(ParameterSyntax p)
        {
            return HasModifier(p.Modifiers, SyntaxKind.RefKeyword)
                || HasModifier(p.Modifiers, SyntaxKind.OutKeyword)
                || HasModifier(p.Modifiers, SyntaxKind.InKeyword);
        }

        private static TypeSymbol BindParameterType(
            TypeBinder typeBinder,
            ParameterSyntax parameter,
            BindingContext ctx,
            DiagnosticBag diagnostics,
            out bool isReadOnlyRef)
        {
            var baseType = typeBinder.BindType(parameter.Type, ctx, diagnostics);
            bool isByRef = IsByRefParameter(parameter);
            isReadOnlyRef = DeclarationBuilder.IsReadOnlyByRefParameter(parameter);

            if (!isByRef)
                return baseType;

            if (baseType is ByRefTypeSymbol)
                return baseType;

            return ctx.Compilation.CreateByRefType(baseType);
        }
        private static void BindEnumUnderlyingTypesForTree(
            Compilation compilation,
            SyntaxTree tree,
            ImmutableDictionary<SyntaxNode, Symbol> declMap,
            SemanticModel stubModel,
            TypeBinder safeTypeBinder,
            DiagnosticBag diagnostics)
        {
            var intType = compilation.GetSpecialType(SpecialType.System_Int32);

            foreach (var kv in declMap)
            {
                if (kv.Key is not EnumDeclarationSyntax eds)
                    continue;

                if (kv.Value is not SourceNamedTypeSymbol enumType || enumType.TypeKind != TypeKind.Enum)
                    continue;

                if (enumType.EnumUnderlyingType is not null)
                    continue;

                TypeSymbol underlying = intType;

                if (eds.BaseList is not null && eds.BaseList.Types.Count > 0)
                {
                    if (eds.BaseList.Types.Count > 1)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_ENUM001",
                            DiagnosticSeverity.Error,
                            "Enum can only specify one underlying type.",
                            new Location(tree, eds.BaseList.Span)));
                    }

                    var baseTypeSyntax = eds.BaseList.Types[0].Type;
                    var ctx = new BindingContext(compilation, stubModel, enumType, NullRecorder.Instance);
                    var boundUnderlying = safeTypeBinder.BindType(baseTypeSyntax, ctx, diagnostics);

                    if (!IsValidEnumUnderlyingType(boundUnderlying))
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_ENUM002",
                            DiagnosticSeverity.Error,
                            $"Type '{boundUnderlying.Name}' is not a valid enum underlying type.",
                            new Location(tree, baseTypeSyntax.Span)));
                    }
                    else
                    {
                        underlying = boundUnderlying;
                    }
                }

                enumType.SetEnumUnderlyingType(underlying);
            }
        }
        private static void BindEnumMembersForTree(
            Compilation compilation,
            SyntaxTree tree,
            ImmutableDictionary<SyntaxNode, Symbol> declMap,
            SemanticModel stubModel,
            TypeBinder safeTypeBinder,
            DiagnosticBag diagnostics)
        {
            foreach (var kv in declMap)
            {
                if (kv.Key is not EnumDeclarationSyntax eds)
                    continue;

                if (kv.Value is not SourceNamedTypeSymbol enumType || enumType.TypeKind != TypeKind.Enum)
                    continue;

                var underlyingType = enumType.EnumUnderlyingType ?? compilation.GetSpecialType(SpecialType.System_Int32);
                object? previousValue = null;
                bool hasPrevious = false;

                for (int i = 0; i < eds.Members.Count; i++)
                {
                    var em = eds.Members[i];
                    if (!declMap.TryGetValue(em, out var sym) || sym is not SourceFieldSymbol field)
                        continue;

                    object value;

                    if (em.EqualsValue is not null)
                    {
                        var ctx = new BindingContext(compilation, stubModel, field, NullRecorder.Instance);
                        var exprBinder = new LocalScopeBinder(parent: safeTypeBinder, flags: BinderFlags.None, containing: field);
                        var init = exprBinder.BindExpression(em.EqualsValue.Value, ctx, diagnostics);

                        if (!ReferenceEquals(init.Type, enumType))
                        {
                            init = exprBinder.ApplyConversion(
                                exprSyntax: em.EqualsValue.Value,
                                expr: init,
                                targetType: underlyingType,
                                diagnosticNode: em,
                                context: ctx,
                                diagnostics: diagnostics,
                                requireImplicit: true);
                        }

                        if (!init.ConstantValueOpt.HasValue)
                        {
                            diagnostics.Add(new Diagnostic(
                                "CN_ENUM003",
                                DiagnosticSeverity.Error,
                                $"Enum member '{field.Name}' must have a constant value.",
                                new Location(tree, em.Span)));

                            value = GetDefaultEnumConstantValue(underlyingType);
                        }
                        else
                        {
                            value = init.ConstantValueOpt.Value!;
                        }
                    }
                    else
                    {
                        if (!hasPrevious)
                        {
                            value = GetDefaultEnumConstantValue(underlyingType);
                        }
                        else if (!TryIncrementEnumConstantValue(previousValue!, underlyingType, out value))
                        {
                            diagnostics.Add(new Diagnostic(
                                "CN_ENUM004",
                                DiagnosticSeverity.Error,
                                $"Enum member '{field.Name}' value overflows underlying type '{underlyingType.Name}'.",
                                new Location(tree, em.Span)));

                            value = previousValue!;
                        }
                    }
                    field.SetConstantValue(new Optional<object>(value));
                    hasPrevious = true;
                    previousValue = value;
                }
            }
        }
        private static bool IsAutoProperty(PropertyDeclarationSyntax pd)
        {
            if (pd is null)
                return false;

            if (pd.ExpressionBody is not null)
                return false;

            if (pd.AccessorList is null)
                return false;

            bool hasAccessor = false;
            var accessors = pd.AccessorList.Accessors;
            for (int i = 0; i < accessors.Count; i++)
            {
                var a = accessors[i];

                if (a.Kind is not SyntaxKind.GetAccessorDeclaration and not SyntaxKind.SetAccessorDeclaration)
                    return false;

                hasAccessor = true;

                if (a.Body is not null || a.ExpressionBody is not null)
                    return false;
            }

            return hasAccessor;
        }
        private static bool IsValidEnumUnderlyingType(TypeSymbol t)
        {
            return t.SpecialType is
                SpecialType.System_Int8 or SpecialType.System_UInt8 or
                SpecialType.System_Int16 or SpecialType.System_UInt16 or
                SpecialType.System_Int32 or SpecialType.System_UInt32 or
                SpecialType.System_Int64 or SpecialType.System_UInt64;
        }

        private static object GetDefaultEnumConstantValue(TypeSymbol underlyingType)
        {
            return underlyingType.SpecialType switch
            {
                SpecialType.System_Int8 => (sbyte)0,
                SpecialType.System_UInt8 => (byte)0,
                SpecialType.System_Int16 => (short)0,
                SpecialType.System_UInt16 => (ushort)0,
                SpecialType.System_Int32 => 0,
                SpecialType.System_UInt32 => 0u,
                SpecialType.System_Int64 => 0L,
                SpecialType.System_UInt64 => 0UL,
                _ => 0
            };
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
        private static bool TryIncrementEnumConstantValue(object current, TypeSymbol underlyingType, out object next)
        {
            next = current;

            switch (underlyingType.SpecialType)
            {
                case SpecialType.System_Int8:
                    {
                        var v = (sbyte)current;
                        if (v == sbyte.MaxValue) return false;
                        next = (sbyte)(v + 1);
                        return true;
                    }
                case SpecialType.System_UInt8:
                    {
                        var v = (byte)current;
                        if (v == byte.MaxValue) return false;
                        next = (byte)(v + 1);
                        return true;
                    }
                case SpecialType.System_Int16:
                    {
                        var v = (short)current;
                        if (v == short.MaxValue) return false;
                        next = (short)(v + 1);
                        return true;
                    }
                case SpecialType.System_UInt16:
                    {
                        var v = (ushort)current;
                        if (v == ushort.MaxValue) return false;
                        next = (ushort)(v + 1);
                        return true;
                    }
                case SpecialType.System_Int32:
                    {
                        var v = (int)current;
                        if (v == int.MaxValue) return false;
                        next = v + 1;
                        return true;
                    }
                case SpecialType.System_UInt32:
                    {
                        var v = (uint)current;
                        if (v == uint.MaxValue) return false;
                        next = v + 1;
                        return true;
                    }
                case SpecialType.System_Int64:
                    {
                        var v = (long)current;
                        if (v == long.MaxValue) return false;
                        next = v + 1;
                        return true;
                    }
                case SpecialType.System_UInt64:
                    {
                        var v = (ulong)current;
                        if (v == ulong.MaxValue) return false;
                        next = v + 1;
                        return true;
                    }
                default:
                    return false;
            }
        }
    }
    internal sealed class NullRecorder : IBindingRecorder
    {
        public static readonly NullRecorder Instance = new();
        public void RecordBound(SyntaxNode syntax, BoundNode bound) { }
        public void RecordDeclared(SyntaxNode syntax, Symbol symbol) { }
    }
    internal static class AttributeBinder
    {
        private sealed class SemanticModelStub : SemanticModel
        {
            public SemanticModelStub(Compilation c, SyntaxTree t)
                : base(c, t, ignoresAccessibility: true) { }

            public override ImmutableArray<Diagnostic> GetDiagnostics(TextSpan? span = null, CancellationToken cancellationToken = default)
                => ImmutableArray<Diagnostic>.Empty;

            public override Symbol? GetDeclaredSymbol(SyntaxNode declaration, CancellationToken cancellationToken = default) => null;
            public override SymbolInfo GetSymbolInfo(SyntaxNode node, CancellationToken cancellationToken = default) => SymbolInfo.None;
            public override Symbol? GetAliasInfo(NameSyntax nameSyntax, CancellationToken cancellationToken = default) => null;
            public override TypeInfo GetTypeInfo(
                ExpressionSyntax expr, CancellationToken cancellationToken = default) => new TypeInfo(null, null);
            public override Optional<object> GetConstantValue(
                ExpressionSyntax expr, CancellationToken cancellationToken = default) => Optional<object>.None;
            public override Conversion GetConversion(
                ExpressionSyntax expr, CancellationToken cancellationToken = default) => new Conversion(ConversionKind.None);
            public override Symbol? GetEnclosingSymbol(int position, CancellationToken cancellationToken = default) => null;
            public override ImmutableArray<Symbol> LookupSymbols(
                int position, string? name = null, CancellationToken cancellationToken = default)
                => ImmutableArray<Symbol>.Empty;

            internal override BoundNode GetBoundNode(SyntaxNode node, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();
        }
        private readonly struct BoundAttributeApplication
        {
            public readonly SyntaxTree Tree;
            public readonly AttributeSyntax Syntax;
            public readonly Symbol Owner;
            public readonly AttributeData Data;

            public BoundAttributeApplication(SyntaxTree tree, AttributeSyntax syntax, Symbol owner, AttributeData data)
            {
                Tree = tree;
                Syntax = syntax;
                Owner = owner;
                Data = data;
            }
        }

        private readonly struct AttributeUsageSpec
        {
            public readonly ulong ValidOnMask;
            public readonly bool AllowMultiple;
            public readonly bool Inherited;

            public AttributeUsageSpec(ulong validOnMask, bool allowMultiple, bool inherited)
            {
                ValidOnMask = validOnMask;
                AllowMultiple = allowMultiple;
                Inherited = inherited;
            }
        }
        private readonly struct AppliedAttrKey : IEquatable<AppliedAttrKey>
        {
            public readonly Symbol Owner;
            public readonly NamedTypeSymbol AttrClass;
            public readonly AttributeApplicationTarget Target;

            public AppliedAttrKey(Symbol owner, NamedTypeSymbol attrClass, AttributeApplicationTarget target)
            {
                Owner = owner;
                AttrClass = attrClass.OriginalDefinition;
                Target = target;
            }

            public bool Equals(AppliedAttrKey other)
                => ReferenceEquals(Owner, other.Owner)
                && ReferenceEquals(AttrClass, other.AttrClass)
                && Target == other.Target;

            public override bool Equals(object? obj) => obj is AppliedAttrKey other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(
                    RuntimeHelpers.GetHashCode(Owner),
                    RuntimeHelpers.GetHashCode(AttrClass),
                    (int)Target);
        }
        private readonly struct BoundAttrArg
        {
            public readonly AttributeArgumentSyntax Syntax;
            public readonly string? Name; // NameColon -> ctor named arg; NameEquals handled separately
            public readonly BoundExpression Expression;

            public BoundAttrArg(AttributeArgumentSyntax syntax, string? name, BoundExpression expression)
            {
                Syntax = syntax;
                Name = name;
                Expression = expression;
            }
        }

        private readonly struct NamedAttrAssign
        {
            public readonly AttributeArgumentSyntax Syntax;
            public readonly string Name;
            public readonly BoundExpression Expression;

            public NamedAttrAssign(AttributeArgumentSyntax syntax, string name, BoundExpression expression)
            {
                Syntax = syntax;
                Name = name;
                Expression = expression;
            }
        }

        public static void BindAll(Compilation compilation, ImmutableArray<SyntaxTree> trees, DiagnosticBag diagnostics)
        {
            var applications = new List<BoundAttributeApplication>(capacity: 64);
            for (int ti = 0; ti < trees.Length; ti++)
            {
                var tree = trees[ti];
                var stubModel = new SemanticModelStub(compilation, tree);
                var importScopeMap = ImportsBuilder.BuildImportScopeMap(compilation, tree, NullRecorder.Instance, diagnostics);

                var safeTypeBinder = new TypeBinder(
                    parent: null, flags: BinderFlags.None, compilation: compilation, importScopeMap: importScopeMap);
                var unsafeTypeBinder = new TypeBinder(
                    parent: null, flags: BinderFlags.UnsafeRegion, compilation: compilation, importScopeMap: importScopeMap);

                if (!compilation.DeclaredSymbolsByTree.TryGetValue(tree, out var declMap))
                    continue;

                foreach (var kv in declMap)
                {
                    var syntax = kv.Key;
                    var symbol = kv.Value;

                    switch (syntax)
                    {
                        case TypeDeclarationSyntax tds when symbol is Symbol:
                            BindAttributeListsOnOwner(
                                compilation, tree, tds.AttributeLists, ownerSyntax: tds, ownerSymbol: symbol,
                                stubModel, safeTypeBinder, unsafeTypeBinder, diagnostics, applications);
                            break;

                        case EnumDeclarationSyntax eds when symbol is Symbol:
                            BindAttributeListsOnOwner(
                                compilation, tree, eds.AttributeLists, ownerSyntax: eds, ownerSymbol: symbol,
                                stubModel, safeTypeBinder, unsafeTypeBinder, diagnostics, applications);
                            break;

                        case MethodDeclarationSyntax mds when symbol is MethodSymbol:
                            BindAttributeListsOnOwner(
                                compilation, tree, mds.AttributeLists, ownerSyntax: mds, ownerSymbol: symbol,
                                stubModel, safeTypeBinder, unsafeTypeBinder, diagnostics, applications);
                            break;

                        case ConstructorDeclarationSyntax cds when symbol is MethodSymbol:
                            BindAttributeListsOnOwner(
                                compilation, tree, cds.AttributeLists, ownerSyntax: cds, ownerSymbol: symbol,
                                stubModel, safeTypeBinder, unsafeTypeBinder, diagnostics, applications);
                            break;

                        case PropertyDeclarationSyntax pds when symbol is PropertySymbol:
                            BindAttributeListsOnOwner(
                                compilation, tree, pds.AttributeLists, ownerSyntax: pds, ownerSymbol: symbol,
                                stubModel, safeTypeBinder, unsafeTypeBinder, diagnostics, applications);
                            break;

                        case ParameterSyntax ps when symbol is ParameterSymbol:
                            BindAttributeListsOnOwner(
                                compilation, tree, ps.AttributeLists, ownerSyntax: ps, ownerSymbol: symbol,
                                stubModel, safeTypeBinder, unsafeTypeBinder, diagnostics, applications);
                            break;

                        case TypeParameterSyntax tps when symbol is TypeParameterSymbol:
                            BindAttributeListsOnOwner(
                                compilation, tree, tps.AttributeLists, ownerSyntax: tps, ownerSymbol: symbol,
                                stubModel, safeTypeBinder, unsafeTypeBinder, diagnostics, applications);
                            break;

                        case VariableDeclaratorSyntax _ when symbol is SourceFieldSymbol sf:
                            if (sf.AttributeOwnerDeclarationRef.Node is FieldDeclarationSyntax fds)
                            {
                                BindAttributeListsOnOwner(
                                    compilation, tree, fds.AttributeLists, ownerSyntax: fds, ownerSymbol: sf,
                                    stubModel, safeTypeBinder, unsafeTypeBinder, diagnostics, applications);
                            }
                            break;

                        case EnumMemberDeclarationSyntax ems when symbol is SourceFieldSymbol esf:
                            BindAttributeListsOnOwner(
                                compilation, tree, ems.AttributeLists, ownerSyntax: ems, ownerSymbol: esf,
                                stubModel, safeTypeBinder, unsafeTypeBinder, diagnostics, applications);
                            break;
                        case OperatorDeclarationSyntax ods when symbol is MethodSymbol:
                            BindAttributeListsOnOwner(
                                compilation, tree, ods.AttributeLists, ownerSyntax: ods, ownerSymbol: symbol,
                                stubModel, safeTypeBinder, unsafeTypeBinder, diagnostics, applications);
                            break;

                        case ConversionOperatorDeclarationSyntax cods when symbol is MethodSymbol:
                            BindAttributeListsOnOwner(
                                compilation, tree, cods.AttributeLists, ownerSyntax: cods, ownerSymbol: symbol,
                                stubModel, safeTypeBinder, unsafeTypeBinder, diagnostics, applications);
                            break;

                        case IndexerDeclarationSyntax ids when symbol is PropertySymbol:
                            BindAttributeListsOnOwner(
                                compilation, tree, ids.AttributeLists, ownerSyntax: ids, ownerSymbol: symbol,
                                stubModel, safeTypeBinder, unsafeTypeBinder, diagnostics, applications);
                            break;
                    }
                }

                if (tree.Root.AttributeLists.Count > 0)
                {
                    for (int i = 0; i < tree.Root.AttributeLists.Count; i++)
                    {
                        var al = tree.Root.AttributeLists[i];
                        diagnostics.Add(new Diagnostic(
                            "CN_ATTR900",
                            DiagnosticSeverity.Error,
                            "Assembly/module attributes are parsed, but assembly/module symbols are not implemented.",
                            new Location(tree, al.Span)));
                    }
                }
            }
            ValidateAttributeUsageApplications(applications, diagnostics);
        }
        private static void BindAttributeListsOnOwner(
            Compilation compilation,
                SyntaxTree tree,
                SyntaxList<AttributeListSyntax> lists,
                SyntaxNode ownerSyntax,
                Symbol ownerSymbol,
                SemanticModel stubModel,
                TypeBinder safeTypeBinder,
                TypeBinder unsafeTypeBinder,
                DiagnosticBag diagnostics,
                List<BoundAttributeApplication> applications)
        {
            if (lists.Count == 0)
                return;

            var defaultTarget = GetDefaultTarget(ownerSymbol);
            if (defaultTarget == AttributeApplicationTarget.Unknown)
                return;

            var flags = GetAttributeExprFlags(ownerSyntax);
            var typeBinder = (flags & BinderFlags.UnsafeRegion) != 0 ? unsafeTypeBinder : safeTypeBinder;

            var exprBindingContainer = GetAttributeExpressionContainer(ownerSymbol);
            var localBinder = new LocalScopeBinder(
                parent: typeBinder,
                flags: flags,
                containing: exprBindingContainer,
                inheritFlowFromParent: false);

            var ctx = new BindingContext(compilation, stubModel, ownerSymbol, NullRecorder.Instance);

            for (int li = 0; li < lists.Count; li++)
            {
                var list = lists[li];

                var target = ResolveAttributeTarget(list.Target, defaultTarget, ownerSymbol, ownerSyntax, tree, diagnostics);
                if (target == AttributeApplicationTarget.Unknown)
                    continue;

                for (int ai = 0; ai < list.Attributes.Count; ai++)
                {
                    var attrSyntax = list.Attributes[ai];
                    if (TryBindSingleAttribute(
                        tree, attrSyntax, target, ownerSymbol,
                        typeBinder, localBinder, ctx, diagnostics,
                        out var data))
                    {
                        AddAttribute(ownerSymbol, data!);

                        applications.Add(new BoundAttributeApplication(tree, attrSyntax, ownerSymbol, data!));
                    }
                }
            }

        }
        private static void ValidateAttributeUsageApplications(
            List<BoundAttributeApplication> applications,
            DiagnosticBag diagnostics)
        {
            var seenNoMultiple = new HashSet<AppliedAttrKey>();

            for (int i = 0; i < applications.Count; i++)
            {
                var app = applications[i];
                var spec = GetAttributeUsageSpec(app.Data.AttributeClass);

                ulong targetBit = ToAttributeTargetsBit(app.Data.Target);
                if (targetBit == 0 || (spec.ValidOnMask & targetBit) == 0)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ATTR012",
                        DiagnosticSeverity.Error,
                        $"Attribute '{app.Data.AttributeClass.Name}' is not valid on target '{app.Data.Target}'.",
                        new Location(app.Tree, app.Syntax.Span)));
                }

                if (!spec.AllowMultiple)
                {
                    var key = new AppliedAttrKey(app.Owner, app.Data.AttributeClass, app.Data.Target);
                    if (!seenNoMultiple.Add(key))
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_ATTR013",
                            DiagnosticSeverity.Error,
                            $"Attribute '{app.Data.AttributeClass.Name}' cannot be applied multiple times to the same target.",
                            new Location(app.Tree, app.Syntax.Span)));
                    }
                }
            }
        }
        private static AttributeUsageSpec GetAttributeUsageSpec(NamedTypeSymbol attributeClass)
        {
            // [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
            var spec = new AttributeUsageSpec(validOnMask: 0x7FFF, allowMultiple: false, inherited: true);

            if (IsSystemAttributeUsageAttribute(attributeClass))
                return new AttributeUsageSpec(validOnMask: 0x0004, allowMultiple: false, inherited: true); // Class

            var attrs = attributeClass.GetAttributes();
            for (int i = 0; i < attrs.Length; i++)
            {
                var a = attrs[i];
                if (!IsSystemAttributeUsageAttribute(a.AttributeClass))
                    continue;

                ulong validOn = spec.ValidOnMask;
                bool allowMultiple = spec.AllowMultiple;
                bool inherited = spec.Inherited;

                if (a.ConstructorArguments.Length >= 1 &&
                    TryConvertToUInt64(a.ConstructorArguments[0].Value, out var m))
                {
                    validOn = m;
                }

                var named = a.NamedArguments;
                for (int ni = 0; ni < named.Length; ni++)
                {
                    if (StringComparer.Ordinal.Equals(named[ni].Name, "AllowMultiple") &&
                        named[ni].Value.Value is bool am)
                    {
                        allowMultiple = am;
                        continue;
                    }

                    if (StringComparer.Ordinal.Equals(named[ni].Name, "Inherited") &&
                        named[ni].Value.Value is bool inh)
                    {
                        inherited = inh;
                        continue;
                    }
                }

                return new AttributeUsageSpec(validOn, allowMultiple, inherited);
            }

            return spec;
        }

        private static bool IsSystemAttributeUsageAttribute(NamedTypeSymbol t)
        {
            var def = t.OriginalDefinition;
            return StringComparer.Ordinal.Equals(def.Name, "AttributeUsageAttribute")
                && IsNamespace(def.ContainingSymbol, "System");
        }
        private static bool TryConvertToUInt64(object? value, out ulong result)
        {
            switch (value)
            {
                case byte v: result = v; return true;
                case sbyte v: result = unchecked((ulong)v); return true;
                case short v: result = unchecked((ulong)v); return true;
                case ushort v: result = v; return true;
                case int v: result = unchecked((ulong)v); return true;
                case uint v: result = v; return true;
                case long v: result = unchecked((ulong)v); return true;
                case ulong v: result = v; return true;
                default:
                    result = 0;
                    return false;
            }
        }
        private static ulong ToAttributeTargetsBit(AttributeApplicationTarget target)
        {
            return target switch
            {
                AttributeApplicationTarget.Assembly => 0x0001,
                AttributeApplicationTarget.Module => 0x0002,
                AttributeApplicationTarget.Class => 0x0004,
                AttributeApplicationTarget.Struct => 0x0008,
                AttributeApplicationTarget.Enum => 0x0010,
                AttributeApplicationTarget.Constructor => 0x0020,
                AttributeApplicationTarget.Method => 0x0040,
                AttributeApplicationTarget.Property => 0x0080,
                AttributeApplicationTarget.Field => 0x0100,
                AttributeApplicationTarget.Event => 0x0200,
                AttributeApplicationTarget.Interface => 0x0400,
                AttributeApplicationTarget.Parameter => 0x0800,
                AttributeApplicationTarget.Delegate => 0x1000,
                AttributeApplicationTarget.ReturnValue => 0x2000,
                AttributeApplicationTarget.GenericParameter => 0x4000,
                _ => 0
            };
        }
        private static AttributeApplicationTarget ResolveAttributeTarget(
                AttributeTargetSpecifierSyntax? targetSyntax,
                AttributeApplicationTarget defaultTarget,
                Symbol ownerSymbol,
                SyntaxNode ownerSyntax,
                SyntaxTree tree,
                DiagnosticBag diagnostics)
        {
            if (targetSyntax is null)
                return defaultTarget;

            var text = targetSyntax.Identifier.ValueText ?? "";
            var parsed = ParseTarget(text, defaultTarget);

            if (parsed == AttributeApplicationTarget.Unknown)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ATTR001",
                    DiagnosticSeverity.Error,
                    $"Unknown attribute target '{text}'.",
                    new Location(tree, targetSyntax.Span)));
                return AttributeApplicationTarget.Unknown;
            }

            if (!IsTargetValidForOwner(parsed, ownerSymbol))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ATTR002",
                    DiagnosticSeverity.Error,
                    $"Attribute target '{text}' is not valid for this declaration.",
                    new Location(tree, targetSyntax.Span)));
                return AttributeApplicationTarget.Unknown;
            }

            return parsed;
        }
        private static AttributeApplicationTarget ParseTarget(string text, AttributeApplicationTarget defaultForOwner)
        {
            switch (text)
            {
                case "assembly": return AttributeApplicationTarget.Assembly;
                case "module": return AttributeApplicationTarget.Module;
                case "class": return AttributeApplicationTarget.Class;
                case "struct": return AttributeApplicationTarget.Struct;
                case "enum": return AttributeApplicationTarget.Enum;
                case "constructor": return AttributeApplicationTarget.Constructor;
                case "method": return AttributeApplicationTarget.Method;
                case "property": return AttributeApplicationTarget.Property;
                case "field": return AttributeApplicationTarget.Field;
                case "event": return AttributeApplicationTarget.Event;
                case "interface": return AttributeApplicationTarget.Interface;
                case "param":
                case "parameter": return AttributeApplicationTarget.Parameter;
                case "delegate": return AttributeApplicationTarget.Delegate;
                case "return": return AttributeApplicationTarget.ReturnValue;
                case "type":
                    return defaultForOwner is AttributeApplicationTarget.Class
                        or AttributeApplicationTarget.Struct
                        or AttributeApplicationTarget.Interface
                        or AttributeApplicationTarget.Enum
                        or AttributeApplicationTarget.Delegate
                        ? defaultForOwner
                        : AttributeApplicationTarget.Unknown;
                default:
                    return AttributeApplicationTarget.Unknown;
            }
        }
        private static bool IsTargetValidForOwner(AttributeApplicationTarget target, Symbol owner)
        {
            switch (owner)
            {
                case NamedTypeSymbol nt:
                    return nt.TypeKind switch
                    {
                        TypeKind.Class => target == AttributeApplicationTarget.Class,
                        TypeKind.Struct => target == AttributeApplicationTarget.Struct,
                        TypeKind.Interface => target == AttributeApplicationTarget.Interface,
                        TypeKind.Enum => target == AttributeApplicationTarget.Enum,
                        TypeKind.Delegate => target == AttributeApplicationTarget.Delegate,
                        _ => false
                    };

                case MethodSymbol m:
                    if (m.IsConstructor)
                        return target == AttributeApplicationTarget.Constructor;
                    return target == AttributeApplicationTarget.Method || target == AttributeApplicationTarget.ReturnValue;

                case PropertySymbol:
                    return target == AttributeApplicationTarget.Property || target == AttributeApplicationTarget.ReturnValue;

                case FieldSymbol:
                    return target == AttributeApplicationTarget.Field;

                case ParameterSymbol:
                    return target == AttributeApplicationTarget.Parameter;

                case TypeParameterSymbol:
                    return target == AttributeApplicationTarget.GenericParameter;

                default:
                    return false;
            }
        }
        private static AttributeApplicationTarget GetDefaultTarget(Symbol owner)
        {
            return owner switch
            {
                NamedTypeSymbol nt => nt.TypeKind switch
                {
                    TypeKind.Class => AttributeApplicationTarget.Class,
                    TypeKind.Struct => AttributeApplicationTarget.Struct,
                    TypeKind.Interface => AttributeApplicationTarget.Interface,
                    TypeKind.Enum => AttributeApplicationTarget.Enum,
                    TypeKind.Delegate => AttributeApplicationTarget.Delegate,
                    _ => AttributeApplicationTarget.Unknown
                },
                MethodSymbol m => m.IsConstructor ? AttributeApplicationTarget.Constructor : AttributeApplicationTarget.Method,
                PropertySymbol => AttributeApplicationTarget.Property,
                FieldSymbol => AttributeApplicationTarget.Field,
                ParameterSymbol => AttributeApplicationTarget.Parameter,
                TypeParameterSymbol => AttributeApplicationTarget.GenericParameter,
                _ => AttributeApplicationTarget.Unknown
            };
        }
        private static BinderFlags GetAttributeExprFlags(SyntaxNode ownerSyntax)
        {
            static bool HasModifier(SyntaxTokenList mods, SyntaxKind kind)
            {
                for (int i = 0; i < mods.Count; i++)
                    if (mods[i].Kind == kind) return true;
                return false;
            }

            return ownerSyntax switch
            {
                MethodDeclarationSyntax md when HasModifier(md.Modifiers, SyntaxKind.UnsafeKeyword) => BinderFlags.UnsafeRegion,
                ConstructorDeclarationSyntax cd when HasModifier(cd.Modifiers, SyntaxKind.UnsafeKeyword) => BinderFlags.UnsafeRegion,
                PropertyDeclarationSyntax pd when HasModifier(pd.Modifiers, SyntaxKind.UnsafeKeyword) => BinderFlags.UnsafeRegion,
                FieldDeclarationSyntax fd when HasModifier(fd.Modifiers, SyntaxKind.UnsafeKeyword) => BinderFlags.UnsafeRegion,
                TypeDeclarationSyntax td when HasModifier(td.Modifiers, SyntaxKind.UnsafeKeyword) => BinderFlags.UnsafeRegion,
                OperatorDeclarationSyntax od when HasModifier(od.Modifiers, SyntaxKind.UnsafeKeyword) => BinderFlags.UnsafeRegion,
                ConversionOperatorDeclarationSyntax cod when HasModifier(cod.Modifiers, SyntaxKind.UnsafeKeyword) => BinderFlags.UnsafeRegion,
                _ => BinderFlags.None
            };
        }

        private static Symbol GetAttributeExpressionContainer(Symbol owner)
        {
            return owner switch
            {
                ParameterSymbol p when p.ContainingSymbol is MethodSymbol m && m.ContainingSymbol is Symbol s => s,
                TypeParameterSymbol tp when tp.ContainingSymbol is MethodSymbol m && m.ContainingSymbol is Symbol s => s,
                MethodSymbol m when m.ContainingSymbol is Symbol s => s,
                _ => owner
            };
        }
        private static bool TryBindSingleAttribute(
                SyntaxTree tree,
                AttributeSyntax attrSyntax,
                AttributeApplicationTarget target,
                Symbol ownerSymbol,
                TypeBinder typeBinder,
             LocalScopeBinder exprBinder,
                BindingContext ctx,
                DiagnosticBag diagnostics,
                out AttributeData? data)
        {
            data = null;

            var attrTypeSym = typeBinder.BindAttributeType(attrSyntax.Name, ctx, diagnostics);
            if (attrTypeSym is not NamedTypeSymbol attrType)
                return false;

            if (!IsAttributeType(attrType))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ATTR003",
                    DiagnosticSeverity.Error,
                    $"Type '{attrType.Name}' is not an attribute type (must derive from System.Attribute).",
                    new Location(tree, attrSyntax.Name.Span)));
                return false;
            }

            var ctorArgsBuilder = ImmutableArray.CreateBuilder<BoundAttrArg>();
            var namedAssignsBuilder = ImmutableArray.CreateBuilder<NamedAttrAssign>();

            var args = attrSyntax.ArgumentList?.Arguments ?? SeparatedSyntaxList<AttributeArgumentSyntax>.Empty;
            for (int i = 0; i < args.Count; i++)
            {
                var a = args[i];
                var boundExpr = exprBinder.BindExpression(a.Expression, ctx, diagnostics);

                if (a.NameEquals is not null)
                {
                    var name = a.NameEquals.Name.Identifier.ValueText ?? "";
                    namedAssignsBuilder.Add(new NamedAttrAssign(a, name, boundExpr));
                }
                else
                {
                    string? ctorArgName = a.NameColon?.Name.Identifier.ValueText;
                    ctorArgsBuilder.Add(new BoundAttrArg(a, ctorArgName, boundExpr));
                }
            }

            var ctorArgs = ctorArgsBuilder.ToImmutable();
            var namedAssigns = namedAssignsBuilder.ToImmutable();

            if (!TryResolveAttributeConstructor(
                tree, attrSyntax, attrType, ctorArgs, exprBinder, ctx, diagnostics,
                out var ctor, out var convertedCtorArgs))
            {
                return false;
            }

            var ctorTyped = ImmutableArray.CreateBuilder<TypedConstant>(convertedCtorArgs.Length);
            for (int i = 0; i < convertedCtorArgs.Length; i++)
            {
                if (!TryCreateTypedConstant(
                    tree,
                    argSpanNode: ctorArgs[i].Syntax,
                    convertedExpression: convertedCtorArgs[i],
                    declaredType: ctor!.Parameters[i].Type,
                    diagnostics,
                    out var tc))
                {
                    return false;
                }

                ctorTyped.Add(tc);
            }

            var namedTyped = ImmutableArray.CreateBuilder<AttributeNamedArgumentData>(namedAssigns.Length);
            for (int i = 0; i < namedAssigns.Length; i++)
            {
                if (!TryBindNamedAttributeAssignment(
                    tree, attrType, namedAssigns[i], exprBinder, ctx, diagnostics, out var namedData))
                {
                    return false;
                }

                namedTyped.Add(namedData);
            }

            data = new AttributeData(
                attributeClass: attrType,
                constructor: ctor!,
                constructorArguments: ctorTyped.ToImmutable(),
                namedArguments: namedTyped.ToImmutable(),
                target: target);

            return true;
        }
        private static bool TryResolveAttributeConstructor(
                SyntaxTree tree,
                AttributeSyntax attrSyntax,
                NamedTypeSymbol attributeType,
                ImmutableArray<BoundAttrArg> ctorArgs,
                LocalScopeBinder exprBinder,
                BindingContext ctx,
                DiagnosticBag diagnostics,
                out MethodSymbol? chosen,
                out ImmutableArray<BoundExpression> convertedArgsInParameterOrder)
        {
            chosen = null;
            convertedArgsInParameterOrder = default;

            var candidatesBuilder = ImmutableArray.CreateBuilder<MethodSymbol>();
            var members = attributeType.GetMembers();
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is MethodSymbol ms && ms.IsConstructor && !ms.IsStatic)
                    candidatesBuilder.Add(ms);
            }
            var candidates = candidatesBuilder.ToImmutable();

            if (candidates.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ATTR004",
                    DiagnosticSeverity.Error,
                    $"Attribute type '{attributeType.Name}' has no instance constructor.",
                    new Location(tree, attrSyntax.Span)));
                return false;
            }

            MethodSymbol? best = null;
            int bestScore = int.MaxValue;
            bool ambiguous = false;
            int[]? bestParamMap = null;

            for (int ci = 0; ci < candidates.Length; ci++)
            {
                var m = candidates[ci];
                if (m.Parameters.Length != ctorArgs.Length)
                    continue;

                var paramMap = new int[ctorArgs.Length];
                for (int i = 0; i < paramMap.Length; i++) paramMap[i] = -1;

                var assigned = new bool[m.Parameters.Length];
                int nextPositional = 0;
                bool bad = false;

                for (int ai = 0; ai < ctorArgs.Length; ai++)
                {
                    int paramIndex = -1;

                    if (!string.IsNullOrEmpty(ctorArgs[ai].Name))
                    {
                        for (int pi = 0; pi < m.Parameters.Length; pi++)
                        {
                            if (StringComparer.Ordinal.Equals(m.Parameters[pi].Name, ctorArgs[ai].Name))
                            {
                                paramIndex = pi;
                                break;
                            }
                        }
                    }
                    else
                    {
                        while (nextPositional < assigned.Length && assigned[nextPositional])
                            nextPositional++;

                        if (nextPositional < assigned.Length)
                            paramIndex = nextPositional++;
                    }

                    if (paramIndex < 0 || paramIndex >= assigned.Length || assigned[paramIndex])
                    {
                        bad = true;
                        break;
                    }

                    assigned[paramIndex] = true;
                    paramMap[ai] = paramIndex;
                }

                if (bad)
                    continue;

                int score = 0;
                for (int ai = 0; ai < ctorArgs.Length; ai++)
                {
                    var expr = ctorArgs[ai].Expression;
                    var ptype = m.Parameters[paramMap[ai]].Type;

                    var conv = LocalScopeBinder.ClassifyConversion(expr, ptype);
                    if (!conv.Exists || !conv.IsImplicit)
                    {
                        bad = true;
                        break;
                    }

                    score += conv.Kind switch
                    {
                        ConversionKind.Identity => 0,
                        ConversionKind.ImplicitNumeric => 1,
                        ConversionKind.ImplicitConstant => 1,
                        ConversionKind.ImplicitReference => 1,
                        ConversionKind.ImplicitTuple => 1,
                        ConversionKind.NullLiteral => 1,
                        ConversionKind.Boxing => 2,
                        _ => 10
                    };
                }

                if (bad)
                    continue;

                if (score < bestScore)
                {
                    best = m;
                    bestScore = score;
                    ambiguous = false;
                    bestParamMap = paramMap;
                }
                else if (score == bestScore)
                {
                    ambiguous = true;
                }
            }

            if (best is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ATTR005",
                    DiagnosticSeverity.Error,
                    "No attribute constructor overload matches the supplied arguments.",
                    new Location(tree, attrSyntax.Span)));
                return false;
            }

            if (ambiguous)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ATTR006",
                    DiagnosticSeverity.Error,
                    "Attribute constructor overload resolution is ambiguous.",
                    new Location(tree, attrSyntax.Span)));
                return false;
            }

            var converted = new BoundExpression[best.Parameters.Length];
            for (int ai = 0; ai < ctorArgs.Length; ai++)
            {
                int pi = bestParamMap![ai];
                converted[pi] = exprBinder.ApplyConversion(
                    exprSyntax: ctorArgs[ai].Syntax.Expression,
                    expr: ctorArgs[ai].Expression,
                    targetType: best.Parameters[pi].Type,
                    diagnosticNode: ctorArgs[ai].Syntax,
                    context: ctx,
                    diagnostics: diagnostics,
                    requireImplicit: true);
            }

            chosen = best;
            convertedArgsInParameterOrder = ImmutableArray.Create(converted);
            return true;
        }
        private static bool TryBindNamedAttributeAssignment(
            SyntaxTree tree,
            NamedTypeSymbol attributeType,
            NamedAttrAssign assign,
            LocalScopeBinder exprBinder,
            BindingContext ctx,
            DiagnosticBag diagnostics,
            out AttributeNamedArgumentData data)
        {
            data = default;

            if (!TryFindWritableAttributeNamedMember(attributeType, assign.Name, out var member, out var memberType))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ATTR007",
                    DiagnosticSeverity.Error,
                    $"No writable instance property/field named '{assign.Name}' found on attribute type '{attributeType.Name}'.",
                    new Location(tree, assign.Syntax.Span)));
                return false;
            }

            var converted = exprBinder.ApplyConversion(
                exprSyntax: assign.Syntax.Expression,
                expr: assign.Expression,
                targetType: memberType!,
                diagnosticNode: assign.Syntax,
                context: ctx,
                diagnostics: diagnostics,
                requireImplicit: true);

            if (!TryCreateTypedConstant(
                tree,
                argSpanNode: assign.Syntax,
                convertedExpression: converted,
                declaredType: memberType!,
                diagnostics,
                out var tc))
            {
                return false;
            }

            data = new AttributeNamedArgumentData(assign.Name, member!, tc);
            return true;
        }
        private static bool TryFindWritableAttributeNamedMember(
            NamedTypeSymbol attrType,
            string name,
            out Symbol? member,
            out TypeSymbol? memberType)
        {
            for (NamedTypeSymbol? t = attrType; t is not null; t = t.BaseType as NamedTypeSymbol)
            {
                var members = t.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (!StringComparer.Ordinal.Equals(members[i].Name, name))
                        continue;

                    if (members[i] is FieldSymbol f)
                    {
                        if (!f.IsStatic && !f.IsConst)
                        {
                            member = f;
                            memberType = f.Type;
                            return true;
                        }
                    }
                    else if (members[i] is PropertySymbol p)
                    {
                        if (!p.IsStatic && p.HasSet && p.Parameters.Length == 0)
                        {
                            member = p;
                            memberType = p.Type;
                            return true;
                        }
                    }
                }
            }

            member = null;
            memberType = null;
            return false;
        }
        private static bool TryCreateTypedConstant(
            SyntaxTree tree,
            SyntaxNode argSpanNode,
            BoundExpression convertedExpression,
            TypeSymbol declaredType,
            DiagnosticBag diagnostics,
            out TypedConstant constant)
        {
            constant = default;

            if (!IsValidAttributeParameterType(declaredType))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ATTR008",
                    DiagnosticSeverity.Error,
                    $"Type '{declaredType.Name}' is not a valid attribute parameter type.",
                    new Location(tree, argSpanNode.Span)));
                return false;
            }

            if (declaredType is ArrayTypeSymbol arr)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ATTR009",
                    DiagnosticSeverity.Error,
                    "Array-valued attribute arguments are not implemented.",
                    new Location(tree, argSpanNode.Span)));
                return false;
            }

            if (declaredType.SpecialType == SpecialType.System_Object &&
            convertedExpression is BoundConversionExpression conv &&
            conv.Conversion.Kind == ConversionKind.Boxing &&
            conv.Operand.ConstantValueOpt.HasValue)
            {
                var innerType = conv.Operand.Type;
                if (innerType is null || !IsValidAttributeParameterType(innerType))
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ATTR010",
                        DiagnosticSeverity.Error,
                        "Attribute argument is not a valid compile-time constant.",
                        new Location(tree, argSpanNode.Span)));
                    return false;
                }

                constant = new TypedConstant(innerType, conv.Operand.ConstantValueOpt.Value);
                return true;
            }

            if (!convertedExpression.ConstantValueOpt.HasValue)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ATTR011",
                    DiagnosticSeverity.Error,
                    "Attribute argument must be a compile-time constant.",
                    new Location(tree, argSpanNode.Span)));
                return false;
            }

            constant = new TypedConstant(declaredType, convertedExpression.ConstantValueOpt.Value);
            return true;
        }
        private static bool IsValidAttributeParameterType(TypeSymbol t)
        {
            if (t is ArrayTypeSymbol arr)
                return arr.Rank == 1 && IsValidNonArrayAttributeParameterType(arr.ElementType);

            return IsValidNonArrayAttributeParameterType(t);
        }

        private static bool IsValidNonArrayAttributeParameterType(TypeSymbol t)
        {
            if (t is NamedTypeSymbol nt && nt.TypeKind == TypeKind.Enum)
                return true;

            switch (t.SpecialType)
            {
                case SpecialType.System_Object:
                case SpecialType.System_String:
                case SpecialType.System_Boolean:
                case SpecialType.System_Char:
                case SpecialType.System_Int8:
                case SpecialType.System_UInt8:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                    return true;

                default:
                    break;
            }

            return t is NamedTypeSymbol n && n.Name == "Type" && IsNamespace(n.ContainingSymbol, "System");
        }
        private static bool IsAttributeType(NamedTypeSymbol t)
        {
            for (TypeSymbol? cur = t; cur is not null; cur = cur.BaseType)
            {
                if (cur is NamedTypeSymbol nt &&
                    nt.Name == "Attribute" &&
                    IsNamespace(nt.ContainingSymbol, "System"))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsNamespace(Symbol? s, string fullName)
        {
            if (s is not NamespaceSymbol ns)
                return false;

            if (ns.IsGlobalNamespace)
                return fullName.Length == 0;

            var parts = new Stack<string>();
            Symbol? cur = ns;
            while (cur is NamespaceSymbol n && !n.IsGlobalNamespace)
            {
                parts.Push(n.Name);
                cur = n.ContainingSymbol;
            }

            var sb = new StringBuilder();
            bool first = true;
            foreach (var p in parts)
            {
                if (!first) sb.Append('.');
                sb.Append(p);
                first = false;
            }

            return StringComparer.Ordinal.Equals(sb.ToString(), fullName);
        }
        private static void AddAttribute(Symbol owner, AttributeData data)
        {
            switch (owner)
            {
                case SourceNamedTypeSymbol t:
                    t.AddAttribute(data);
                    break;
                case SourceMethodSymbol m:
                    m.AddAttribute(data);
                    break;
                case SourcePropertySymbol p:
                    p.AddAttribute(data);
                    break;
                case SourceFieldSymbol f:
                    f.AddAttribute(data);
                    break;
                case ParameterSymbol p:
                    p.AddAttribute(data);
                    break;
                case TypeParameterSymbol tp:
                    tp.AddAttribute(data);
                    break;
            }
        }
    }
    internal static class BaseTypeBinder
    {
        private sealed class NullRecorder : IBindingRecorder
        {
            public static readonly NullRecorder Instance = new();
            public void RecordBound(SyntaxNode syntax, BoundNode bound) { }
            public void RecordDeclared(SyntaxNode syntax, Symbol symbol) { }
        }
        private sealed class SemanticModelStub : SemanticModel
        {
            public SemanticModelStub(Compilation c, SyntaxTree t)
                : base(c, t, ignoresAccessibility: true) { }
            public override ImmutableArray<Diagnostic> GetDiagnostics(TextSpan? span = null, CancellationToken cancellationToken = default)
                => ImmutableArray<Diagnostic>.Empty;
            public override Symbol? GetDeclaredSymbol(SyntaxNode declaration, CancellationToken cancellationToken = default) => null;
            public override SymbolInfo GetSymbolInfo(SyntaxNode node, CancellationToken cancellationToken = default) => SymbolInfo.None;
            public override Symbol? GetAliasInfo(NameSyntax nameSyntax, CancellationToken cancellationToken = default) => null;
            public override TypeInfo GetTypeInfo(ExpressionSyntax expr, CancellationToken cancellationToken = default) => new TypeInfo(null, null);
            public override Optional<object> GetConstantValue(ExpressionSyntax expr, CancellationToken cancellationToken = default) => Optional<object>.None;
            public override Conversion GetConversion(ExpressionSyntax expr, CancellationToken cancellationToken = default) => new Conversion(ConversionKind.None);
            public override Symbol? GetEnclosingSymbol(int position, CancellationToken cancellationToken = default) => null;
            public override ImmutableArray<Symbol> LookupSymbols(int position, string? name = null, CancellationToken cancellationToken = default)
                => ImmutableArray<Symbol>.Empty;

            internal override BoundNode GetBoundNode(SyntaxNode node, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();
        }
        private readonly struct BaseClassInfo
        {
            public readonly NamedTypeSymbol BaseType;
            public readonly Location Location;
            public BaseClassInfo(NamedTypeSymbol baseType, Location location)
            {
                BaseType = baseType;
                Location = location;
            }
        }
        public static void BindAll(Compilation compilation, ImmutableArray<SyntaxTree> trees, DiagnosticBag diagnostics)
        {
            var desiredBase = new Dictionary<SourceNamedTypeSymbol, BaseClassInfo>();
            for (int ti = 0; ti < trees.Length; ti++)
            {
                var tree = trees[ti];
                var stubModel = new SemanticModelStub(compilation, tree);
                var importScopeMap = ImportsBuilder.BuildImportScopeMap(compilation, tree, NullRecorder.Instance, diagnostics);
                var safeTypeBinder = new TypeBinder(parent: null, flags: BinderFlags.None, compilation: compilation, importScopeMap: importScopeMap);
                var unsafeTypeBinder = new TypeBinder(parent: null, flags: BinderFlags.UnsafeRegion, compilation: compilation, importScopeMap: importScopeMap);
                if (!compilation.DeclaredSymbolsByTree.TryGetValue(tree, out var declMap))
                    continue;

                foreach (var kv in declMap)
                {
                    if (kv.Key is not TypeDeclarationSyntax typeSyntax)
                        continue;
                    if (kv.Value is not SourceNamedTypeSymbol typeSymbol)
                        continue;
                    var baseList = GetBaseList(typeSyntax);
                    if (baseList is null || baseList.Types.Count == 0)
                        continue;

                    if (typeSymbol.TypeKind != TypeKind.Class)
                        continue;

                    var binder = HasModifier(typeSyntax.Modifiers, SyntaxKind.UnsafeKeyword) ? unsafeTypeBinder : safeTypeBinder;
                    var ctx = new BindingContext(
                         compilation: compilation,
                         semanticModel: stubModel,
                         containingSymbol: typeSymbol,
                         recorder: NullRecorder.Instance);

                    if (!TryResolveDeclaredBaseClass(typeSymbol, baseList, binder, ctx, diagnostics, out var baseClass))
                        continue;

                    if (baseClass is null)
                        continue; // only interfaces in the base list 

                    var loc = new Location(tree, baseList.Span);

                    if (!desiredBase.TryGetValue(typeSymbol, out var existing))
                    {
                        desiredBase[typeSymbol] = new BaseClassInfo(baseClass, loc);
                    }
                    else if (!ReferenceEquals(existing.BaseType, baseClass))
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_BASE001",
                            DiagnosticSeverity.Error,
                            $"Partial declarations of '{typeSymbol.Name}' specify different base classes ('{existing.BaseType.Name}' vs '{baseClass.Name}').",
                            loc));
                    }
                }
            }
            // Break cycles before applying
            var keys = new List<SourceNamedTypeSymbol>(desiredBase.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                var t = keys[i];
                if (!desiredBase.TryGetValue(t, out var info))
                    continue;

                if (CreatesBaseTypeCycle(t, info.BaseType, desiredBase))
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_BASE_CYCLE",
                        DiagnosticSeverity.Error,
                        $"Base type cycle detected for '{t.Name}'.",
                        info.Location));

                    desiredBase.Remove(t);
                }
            }
            // apply
            foreach (var kv in desiredBase)
            {
                kv.Key.SetDeclaredBaseType(kv.Value.BaseType);
            }
            BindOverrides(compilation, trees, diagnostics);
        }

        private static BaseListSyntax? GetBaseList(TypeDeclarationSyntax syntax) => syntax switch
        {
            ClassDeclarationSyntax c => c.BaseList,
            StructDeclarationSyntax s => s.BaseList,
            InterfaceDeclarationSyntax i => i.BaseList,
            _ => null
        };
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
        private static void BindOverrides(Compilation compilation, ImmutableArray<SyntaxTree> trees, DiagnosticBag diagnostics)
        {
            foreach (var tree in trees)
            {
                if (!compilation.DeclaredSymbolsByTree.TryGetValue(tree, out var declMap))
                    continue;
                foreach (var kv in declMap)
                {
                    if (kv.Key is not MethodDeclarationSyntax md)
                        continue;
                    if (kv.Value is not SourceMethodSymbol m)
                        continue;
                    if (!m.IsOverride)
                        continue;
                    if (m.IsStatic || m.IsConstructor)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_OVR001",
                            DiagnosticSeverity.Error,
                            $"Method '{m.Name}' cannot be 'override' because it is static/constructor.",
                            new Location(tree, md.Span)));
                        continue;
                    }
                    if (m.ContainingSymbol is not NamedTypeSymbol ct || ct.BaseType is not NamedTypeSymbol bt)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_OVR002",
                            DiagnosticSeverity.Error,
                            $"Method '{m.Name}' is marked 'override' but containing type has no base class.",
                            new Location(tree, md.Span)));
                        continue;
                    }
                    var overridden = FindOverridableInBaseChain(bt, m);
                    if (overridden is null)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_OVR003",
                            DiagnosticSeverity.Error,
                            $"No suitable virtual method found to override: '{m.Name}'.",
                            new Location(tree, md.Span)));
                        continue;
                    }
                    m.SetOverriddenMethod(overridden);
                }
            }
        }
        private static MethodSymbol? FindOverridableInBaseChain(NamedTypeSymbol baseType, MethodSymbol overriding)
        {
            for (NamedTypeSymbol? t = baseType; t is not null; t = t.BaseType as NamedTypeSymbol)
            {
                foreach (var mem in t.GetMembers())
                {
                    if (mem is not MethodSymbol bm)
                        continue;
                    if (!string.Equals(bm.Name, overriding.Name, StringComparison.Ordinal))
                        continue;
                    if (!SignatureEquals(bm, overriding))
                        continue;
                    if (bm.IsVirtual || bm.IsAbstract || bm.IsOverride)
                        return bm;
                }
            }
            return null;
        }
        private static bool SignatureEquals(MethodSymbol a, MethodSymbol b)
        {
            if (!ReferenceEquals(a.ReturnType, b.ReturnType))
                return false;
            var ap = a.Parameters;
            var bp = b.Parameters;
            if (ap.Length != bp.Length)
                return false;
            for (int i = 0; i < ap.Length; i++)
                if (!ReferenceEquals(ap[i].Type, bp[i].Type))
                    return false;
            return true;
        }
        private static bool TryResolveDeclaredBaseClass(
             SourceNamedTypeSymbol declaringType,
             BaseListSyntax baseList,
             TypeBinder binder,
             BindingContext context,
             DiagnosticBag diagnostics,
             out NamedTypeSymbol? baseClass)
        {
            baseClass = null;

            for (int i = 0; i < baseList.Types.Count; i++)
            {
                var bt = baseList.Types[i];
                if (bt is not SimpleBaseTypeSyntax sbt)
                    continue;

                var t = binder.BindType(sbt.Type, context, diagnostics);
                if (t is not NamedTypeSymbol nt)
                {
                    diagnostics.Add(new Diagnostic(
                         "CN_BASE002",
                         DiagnosticSeverity.Error,
                         $"'{t.Name}' is not a valid base class type.",
                         new Location(context.SemanticModel.SyntaxTree, sbt.Type.Span)));
                    continue;
                }

                if (nt.TypeKind == TypeKind.Interface)
                {
                    continue;
                }

                if (nt.TypeKind != TypeKind.Class && nt.TypeKind != TypeKind.Error)
                {
                    diagnostics.Add(new Diagnostic(
                         "CN_BASE003",
                         DiagnosticSeverity.Error,
                         $"'{nt.Name}' is not a class type and cannot be used as a base class.",
                         new Location(context.SemanticModel.SyntaxTree, sbt.Type.Span)));
                    continue;
                }

                if (ReferenceEquals(nt, declaringType))
                {
                    diagnostics.Add(new Diagnostic(
                         "CN_BASE004",
                         DiagnosticSeverity.Error,
                         $"Type '{declaringType.Name}' cannot derive from itself.",
                         new Location(context.SemanticModel.SyntaxTree, sbt.Type.Span)));
                    continue;
                }

                if (baseClass is null)
                {
                    baseClass = nt;
                }
                else
                {
                    diagnostics.Add(new Diagnostic(
                         "CN_BASE005",
                         DiagnosticSeverity.Error,
                         $"Type '{declaringType.Name}' has more than one base class ('{baseClass.Name}' and '{nt.Name}').",
                         new Location(context.SemanticModel.SyntaxTree, sbt.Type.Span)));
                }
            }
            return true;
        }
        private static bool CreatesBaseTypeCycle(
             SourceNamedTypeSymbol start,
             NamedTypeSymbol baseCandidate,
             Dictionary<SourceNamedTypeSymbol, BaseClassInfo> desiredBase)
        {
            // Only SourceNamedTypeSymbol nodes can participate in a source cycle
            var seen = new HashSet<SourceNamedTypeSymbol>();

            NamedTypeSymbol? cur = baseCandidate;
            while (cur is SourceNamedTypeSymbol curSource)
            {
                if (ReferenceEquals(curSource, start))
                    return true;

                if (!seen.Add(curSource))
                    break;

                if (desiredBase.TryGetValue(curSource, out var next))
                    cur = next.BaseType;
                else
                    cur = curSource.BaseType as NamedTypeSymbol;
            }
            return false;
        }
    }
    internal sealed class DeclarationResult
    {
        public NamespaceSymbol GlobalNamespace { get; }
        public ImmutableDictionary<SyntaxTree, ImmutableDictionary<SyntaxNode, Symbol>> DeclaredSymbolsByTree { get; }

        public DeclarationResult(
            NamespaceSymbol globalNamespace,
            ImmutableDictionary<SyntaxTree, ImmutableDictionary<SyntaxNode, Symbol>> declaredSymbolsByTree)
        {
            GlobalNamespace = globalNamespace;
            DeclaredSymbolsByTree = declaredSymbolsByTree;
        }
    }
    internal sealed class DeclarationBuilder
    {
        private const string OpetatorPrefix = "op_";
        private readonly DiagnosticBag _diagnostics;
        private readonly TypeManager _types;
        private readonly bool _isCoreLibrary;
        private readonly SourceNamespaceSymbol _global;
        private readonly Dictionary<SyntaxTree, Dictionary<SyntaxNode, Symbol>> _declByTree = new();

        private readonly Dictionary<(Symbol container, string name, int arity), NamedTypeSymbol> _typeCache = new();

        private SyntaxTree? _topLevelTree;
        private SourceNamedTypeSymbol? _topLevelProgram;
        private MethodSymbol? _topLevelMain;

        public MethodSymbol? SynthesizedEntryPoint => _topLevelMain;

        public DeclarationBuilder(DiagnosticBag diagnostics, TypeManager types, bool isCoreLibrary)
        {
            _diagnostics = diagnostics;
            _types = types;
            _isCoreLibrary = isCoreLibrary;
            _global = new SourceNamespaceSymbol(name: "", containing: null, isGlobal: true);
        }
        public DeclarationResult Build(ImmutableArray<SyntaxTree> trees)
        {
            foreach (var tree in trees)
            {
                _declByTree[tree] = new Dictionary<SyntaxNode, Symbol>();
                DeclareCompilationUnit(tree, tree.Root, _global);
            }
            SynthesizeDefaultConstructors();
            SynthesizeTypeInitializers();
            var immutable = _declByTree.ToImmutableDictionary(
                kv => kv.Key,
                kv => kv.Value.ToImmutableDictionary());

            return new DeclarationResult(_global, immutable);
        }
        private void SynthesizeDefaultConstructors()
        {
            foreach (var kv in _typeCache)
                if (kv.Value is SourceNamedTypeSymbol s)
                    EnsureDefaultConstructor(s);
        }
        private void SynthesizeTypeInitializers()
        {
            foreach (var kv in _typeCache)
                if (kv.Value is SourceNamedTypeSymbol s)
                    EnsureTypeInitializer(s);
        }
        private void EnsureTypeInitializer(SourceNamedTypeSymbol type)
        {
            var members = type.GetMembers();
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is MethodSymbol ms &&
                    ms.IsStatic &&
                    ms.Parameters.Length == 0 &&
                    StringComparer.Ordinal.Equals(ms.Name, ".cctor"))
                {
                    return;
                }
            }

            bool needsCctor = false;
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is not SourceFieldSymbol fs)
                    continue;
                if (!fs.IsStatic || fs.IsConst)
                    continue;

                var declRefs = fs.DeclaringSyntaxReferences;
                if (declRefs.IsDefaultOrEmpty)
                    continue;

                if (declRefs[0].Node is VariableDeclaratorSyntax vd && vd.Initializer is not null)
                {
                    needsCctor = true;
                    break;
                }
            }

            if (!needsCctor)
                return;

            var voidType = _types.GetSpecialType(SpecialType.System_Void);

            var cctor = new SourceMethodSymbol(
                name: ".cctor",
                containing: type,
                returnType: voidType,
                typeParameters: default,
                isStatic: true,
                isConstructor: true,
                isAsync: false,
                locations: ImmutableArray<Location>.Empty,
                declaredAccessibility: Accessibility.Private);

            cctor.SetTypeParameters(ImmutableArray<TypeParameterSymbol>.Empty);
            cctor.SetParameters(ImmutableArray<ParameterSymbol>.Empty);

            type.AddMember(cctor);
        }
        private void EnsureDefaultConstructor(SourceNamedTypeSymbol type)
        {
            if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
                return;
            bool hasAnyInstanceCtor = false;
            bool hasParameterlessInstanceCtor = false;
            var members = type.GetMembers();
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is MethodSymbol ms && ms.IsConstructor && !ms.IsStatic)
                {
                    hasAnyInstanceCtor = true;
                    if (ms.Parameters.Length == 0)
                        hasParameterlessInstanceCtor = true;
                }
            }
            var voidType = _types.GetSpecialType(SpecialType.System_Void);
            if (type.TypeKind == TypeKind.Class)
            {
                if (!hasAnyInstanceCtor)
                    type.AddMember(new SynthesizedConstructorSymbol(
                        containing: type,
                        voidType: voidType,
                        isStatic: false,
                        parameters: ImmutableArray<ParameterSymbol>.Empty));
                return;
            }
            // struct
            if (!hasParameterlessInstanceCtor)
                type.AddMember(new SynthesizedConstructorSymbol(
                    containing: type,
                    voidType: voidType,
                    isStatic: false,
                    parameters: ImmutableArray<ParameterSymbol>.Empty));
        }

        private static void AddMemberToType(NamedTypeSymbol containingType, Symbol member)
        {
            switch (containingType)
            {
                case SourceNamedTypeSymbol s: s.AddMember(member); return;
                case SpecialNamedTypeSymbol sp: sp.AddMember(member); return;
                default: throw new InvalidOperationException("Type must be mutable.");
            }
        }
        private static void AddNestedTypeToType(NamedTypeSymbol containingType, NamedTypeSymbol nested)
        {
            switch (containingType)
            {
                case SourceNamedTypeSymbol s: s.AddNestedType(nested); return;
                case SpecialNamedTypeSymbol sp: sp.AddNestedType(nested); return;
                default: throw new InvalidOperationException("Type must be mutable.");
            }
        }
        private static string GetFullNamespaceName(SourceNamespaceSymbol ns)
        {
            if (ns.IsGlobalNamespace) return "";
            var parts = new Stack<string>();
            Symbol? cur = ns;
            while (cur is SourceNamespaceSymbol sns && !sns.IsGlobalNamespace)
            {
                parts.Push(sns.Name);
                cur = sns.ContainingSymbol;
            }
            return string.Join(".", parts);
        }
        private bool TryMapSpecialType(Symbol container, string name, int arity, TypeKind declaredKind, out NamedTypeSymbol special)
        {
            special = null!;
            if (!_isCoreLibrary) return false;
            if (arity != 0) return false;
            if (container is not SourceNamespaceSymbol ns) return false;
            if (!StringComparer.Ordinal.Equals(GetFullNamespaceName(ns), "System")) return false;

            var st = name switch
            {
                "Object" => SpecialType.System_Object,
                "Void" => SpecialType.System_Void,
                "ValueType" => SpecialType.System_ValueType,
                "Enum" => SpecialType.System_Enum,
                "Array" => SpecialType.System_Array,
                "String" => SpecialType.System_String,
                "Exception" => SpecialType.System_Exception,

                "Boolean" => SpecialType.System_Boolean,
                "Char" => SpecialType.System_Char,
                "SByte" => SpecialType.System_Int8,
                "Byte" => SpecialType.System_UInt8,
                "Int16" => SpecialType.System_Int16,
                "UInt16" => SpecialType.System_UInt16,
                "Int32" => SpecialType.System_Int32,
                "UInt32" => SpecialType.System_UInt32,
                "Int64" => SpecialType.System_Int64,
                "UInt64" => SpecialType.System_UInt64,
                "Single" => SpecialType.System_Single,
                "Double" => SpecialType.System_Double,
                "Decimal" => SpecialType.System_Decimal,
                "IntPtr" => SpecialType.System_IntPtr,
                "UIntPtr" => SpecialType.System_UIntPtr,
                _ => SpecialType.None
            };
            if (st == SpecialType.None) return false;
            special = _types.GetSpecialType(st);
            if (special.TypeKind != declaredKind)
            {
                _diagnostics.Add(new Diagnostic(
                    id: "CN_CORELIB_0001",
                    severity: DiagnosticSeverity.Error,
                    message: $"Special type System.{name} must be declared as {special.TypeKind}, not {declaredKind}.",
                    location: new Location(_topLevelTree ?? new SyntaxTree(default!, ""), default)));
            }
            return true;
        }
        private void RecordDeclared(SyntaxTree tree, SyntaxNode node, Symbol symbol)
            => _declByTree[tree][node] = symbol;
        private void DeclareCompilationUnit(SyntaxTree tree, CompilationUnitSyntax root, SourceNamespaceSymbol global)
        {
            foreach (var member in root.Members)
                DeclareMember(tree, member, global);
        }
        private TypeSymbol? GetDefaultBaseType(TypeKind kind)
        {
            return kind switch
            {
                TypeKind.Class => _types.GetSpecialType(SpecialType.System_Object),
                TypeKind.Struct => _types.GetSpecialType(SpecialType.System_ValueType),
                TypeKind.Enum => _types.GetSpecialType(SpecialType.System_Enum),
                _ => null
            };
        }
        private void DeclareMember(SyntaxTree tree, MemberDeclarationSyntax member, Symbol container)
        {
            switch (member)
            {
                case BaseNamespaceDeclarationSyntax ns:
                    DeclareNamespaceDeclaration(tree, ns, (SourceNamespaceSymbol)container);
                    return;

                case ClassDeclarationSyntax c:
                    DeclareTypeDeclaration(tree, c, container, TypeKind.Class, c.TypeParameterList);
                    return;

                case StructDeclarationSyntax s:
                    DeclareTypeDeclaration(tree, s, container, TypeKind.Struct, s.TypeParameterList);
                    return;

                case InterfaceDeclarationSyntax i:
                    DeclareTypeDeclaration(tree, i, container, TypeKind.Interface, i.TypeParameterList);
                    return;

                case EnumDeclarationSyntax e:
                    DeclareEnumDeclaration(tree, e, container);
                    return;

                case GlobalStatementSyntax gs:
                    DeclareTopLevelStatement(tree, gs, container);
                    return;

                default:
                    // unknown member kind
                    return;
            }
        }
        private void DeclareTopLevelStatement(SyntaxTree tree, GlobalStatementSyntax gs, Symbol container)
        {
            if (container is not SourceNamespaceSymbol global || !global.IsGlobalNamespace)
            {
                _diagnostics.Add(new Diagnostic(
                    id: "CN_TL001",
                    severity: DiagnosticSeverity.Error,
                    message: "Top-level statements must be in the global namespace.",
                    location: new Location(tree, gs.Span)));
                return;
            }

            if (_topLevelTree is null)
                _topLevelTree = tree;
            else if (!ReferenceEquals(_topLevelTree, tree))
            {
                _diagnostics.Add(new Diagnostic(
                    id: "CN_TL002",
                    severity: DiagnosticSeverity.Error,
                    message: "Top-level statements are only allowed in one source file.",
                    location: new Location(tree, gs.Span)));
                return;
            }

            // Ensure Program exists and is the container for top level statements
            _topLevelProgram ??= GetOrCreateProgramType(tree, global, gs);

            // Ensure synthesized entry method exists
            _topLevelMain ??= GetOrCreateTopLevelMain(tree, _topLevelProgram, gs);
        }
        private SourceNamedTypeSymbol GetOrCreateProgramType(
            SyntaxTree tree, SourceNamespaceSymbol global, SyntaxNode locationNode)
        {
            var key = ((Symbol)global, "Program", 0);

            if (!_typeCache.TryGetValue(key, out var existing))
            {
                var type = new SourceNamedTypeSymbol("Program", global, TypeKind.Class, arity: 0, Accessibility.Internal);
                type.SetDefaultBaseType(GetDefaultBaseType(TypeKind.Class));
                type.SetTypeParameters(ImmutableArray<TypeParameterSymbol>.Empty);
                global.AddType(type);

                type.AddDeclaration(tree, locationNode);

                _typeCache.Add(key, type);
                return type;
            }

            if (existing is SourceNamedTypeSymbol s)
                return s;

            throw new InvalidOperationException("Cached Program type is not a SourceNamedTypeSymbol.");
        }
        private MethodSymbol GetOrCreateTopLevelMain(
            SyntaxTree tree, SourceNamedTypeSymbol programType, SyntaxNode locationNode)
        {
            var voidType = _types.GetSpecialType(SpecialType.System_Void);
            var stringType = _types.GetSpecialType(SpecialType.System_String);


            var main = new SourceMethodSymbol(
                name: "<Main>$",
                containing: programType,
                returnType: voidType,
                typeParameters: default,
                isStatic: true,
                isConstructor: false,
                isAsync: false,
                locations: ImmutableArray.Create(new Location(tree, locationNode.Span)),
                declaredAccessibility: Accessibility.Private);
            main.SetTypeParameters(ImmutableArray<TypeParameterSymbol>.Empty);

            var argsType = _types.GetArrayType(stringType, rank: 1);
            var argsParam = new ParameterSymbol(
                name: "args",
                containing: main,
                type: argsType,
                locations: ImmutableArray.Create(new Location(tree, locationNode.Span)));

            main.SetParameters(ImmutableArray.Create(argsParam));
            programType.AddMember(main);
            return main;
        }
        private void DeclareNamespaceDeclaration(SyntaxTree tree, BaseNamespaceDeclarationSyntax syntax, SourceNamespaceSymbol container)
        {
            var ns = GetOrCreateNamespacePath(tree, container, syntax.Name, syntax);
            RecordDeclared(tree, syntax, ns);

            // declare nested members under this namespace
            foreach (var member in syntax.Members)
                DeclareMember(tree, member, ns);
        }
        private SourceNamespaceSymbol GetOrCreateNamespacePath(
            SyntaxTree tree,
            SourceNamespaceSymbol container,
            NameSyntax name,
            SyntaxNode ownerForDiagnostics)
        {
            var current = container;
            foreach (var part in EnumerateDottedNameParts(name))
            {
                if (part.Length == 0)
                    continue;

                current = current.GetOrAddNamespace(
                    part,
                    () => new SourceNamespaceSymbol(part, current, isGlobal: false));

            }

            current.AddDeclaration(tree, ownerForDiagnostics);
            return current;
        }
        private void DeclareTypeDeclaration(
            SyntaxTree tree,
            TypeDeclarationSyntax syntax,
            Symbol container,
            TypeKind kind,
            TypeParameterListSyntax? typeParameterList)
        {
            var name = syntax.Identifier.ValueText ?? "";
            var arity = typeParameterList?.Parameters.Count ?? 0;
            var typeDefaultAcc = container is NamedTypeSymbol
                ? Accessibility.Private
                : Accessibility.Internal;
            var isReadOnlyStruct = HasModifier(syntax.Modifiers, SyntaxKind.ReadOnlyKeyword);
            var isRefStruct = HasModifier(syntax.Modifiers, SyntaxKind.RefKeyword);
            var declaredAcc = DecodeDeclaredAccessibility(
                syntax.Modifiers,
                typeDefaultAcc,
                allowProtected: container is NamedTypeSymbol,
                allowInternal: true,
                diagLocation: new Location(tree, syntax.Span));
            var key = (container, name, arity);
            if (kind != TypeKind.Struct && (isReadOnlyStruct || isRefStruct))
            {
                _diagnostics.Add(new Diagnostic(
                    "CN_TYPEMOD001",
                    DiagnosticSeverity.Error,
                    "Only structs may be declared with 'readonly' or 'ref'.",
                    new Location(tree, syntax.Span)));
            }
            if (!_typeCache.TryGetValue(key, out var type))
            {
                if (TryMapSpecialType(container, name, arity, kind, out var special))
                {
                    type = special;
                    _typeCache.Add(key, type);
                }
                else
                {
                    var s = new SourceNamedTypeSymbol(
                        name,
                        container,
                        kind,
                        arity,
                        declaredAcc,
                        isFromMetadata: false,
                        isReadOnlyStruct: kind == TypeKind.Struct && isReadOnlyStruct,
                        isRefLikeType: kind == TypeKind.Struct && isRefStruct);
                    s.SetDefaultBaseType(GetDefaultBaseType(kind));
                    s.SetTypeParameters(DeclareTypeParameters(tree, s, typeParameterList));
                    type = s;
                    _typeCache.Add(key, type);
                    if (container is SourceNamespaceSymbol ns) ns.AddType(type);
                    else if (container is NamedTypeSymbol nt) AddNestedTypeToType(nt, type);
                    else throw new InvalidOperationException($"Invalid container for type: {container.Kind}");
                }
            }
            else
            {
                if (type.TypeKind != kind)
                {
                    _diagnostics.Add(new Diagnostic(
                        id: "CN0001",
                        severity: DiagnosticSeverity.Error,
                        message: $"Partial type '{name}' has conflicting kinds ({type.TypeKind} vs {kind}).",
                        location: new Location(tree, syntax.Span)));
                }
                else if (type is SourceNamedTypeSymbol srcExisting && kind == TypeKind.Struct)
                {
                    if (srcExisting.IsReadOnlyStruct != isReadOnlyStruct || srcExisting.IsRefLikeType != isRefStruct)
                    {
                        _diagnostics.Add(new Diagnostic(
                            "CN_TYPEMOD002",
                            DiagnosticSeverity.Error,
                            $"Partial struct '{name}' has conflicting 'readonly'/'ref' modifiers.",
                            new Location(tree, syntax.Span)));
                    }
                }

            }
            if (type is SourceNamedTypeSymbol srcType)
                srcType.AddDeclaration(tree, syntax);
            RecordDeclared(tree, syntax, type);

            // type parameters are declarations too
            if (typeParameterList != null)
            {
                for (int i = 0; i < typeParameterList.Parameters.Count; i++)
                {
                    var tpSyntax = typeParameterList.Parameters[i];
                }
            }

            // Declare nested types inside this type
            foreach (var m in syntax.Members)
            {
                switch (m)
                {
                    case ClassDeclarationSyntax c:
                        DeclareTypeDeclaration(tree, c, type, TypeKind.Class, c.TypeParameterList);
                        break;
                    case StructDeclarationSyntax s:
                        DeclareTypeDeclaration(tree, s, type, TypeKind.Struct, s.TypeParameterList);
                        break;
                    case InterfaceDeclarationSyntax i:
                        DeclareTypeDeclaration(tree, i, type, TypeKind.Interface, i.TypeParameterList);
                        break;
                    case EnumDeclarationSyntax e:
                        DeclareEnumDeclaration(tree, e, type);
                        break;
                    case MethodDeclarationSyntax md:
                        DeclareMethodDeclaration(tree, md, type);
                        break;
                    case FieldDeclarationSyntax fd:
                        DeclareFieldDeclaration(tree, fd, type);
                        break;
                    case PropertyDeclarationSyntax pd:
                        DeclarePropertyDeclaration(tree, pd, type);
                        break;
                    case IndexerDeclarationSyntax id:
                        DeclareIndexerDeclaration(tree, id, type);
                        break;
                    case ConstructorDeclarationSyntax cd:
                        DeclareConstructorDeclaration(tree, cd, type);
                        break;
                    case OperatorDeclarationSyntax ods:
                        DeclareOperatorDeclaration(tree, ods, type);
                        break;

                    case ConversionOperatorDeclarationSyntax cods:
                        DeclareConversionOperatorDeclaration(tree, cods, type);
                        break;
                    default:

                        break;
                }
            }
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
        private Accessibility DecodeDeclaredAccessibility(
            SyntaxTokenList mods,
            Accessibility @default,
            bool allowProtected,
            bool allowInternal,
            Location diagLocation)
        {
            bool hasPublic = HasModifier(mods, SyntaxKind.PublicKeyword);
            bool hasPrivate = HasModifier(mods, SyntaxKind.PrivateKeyword);
            bool hasProtected = HasModifier(mods, SyntaxKind.ProtectedKeyword);
            bool hasInternal = HasModifier(mods, SyntaxKind.InternalKeyword);

            int cnt = (hasPublic ? 1 : 0) + (hasPrivate ? 1 : 0) + (hasProtected ? 1 : 0) + (hasInternal ? 1 : 0);
            if (cnt == 0)
                return @default;

            if (hasPublic && cnt > 1)
            {
                _diagnostics.Add(new Diagnostic("CN_ACC001", DiagnosticSeverity.Error,
                    "Invalid access modifier combination.", diagLocation));
                return @default;
            }

            if (hasPrivate && hasInternal && !hasProtected)
            {
                _diagnostics.Add(new Diagnostic("CN_ACC001", DiagnosticSeverity.Error,
                    "Invalid access modifier combination.", diagLocation));
                return @default;
            }

            if (hasProtected && hasInternal)
            {
                if (!allowProtected || !allowInternal)
                {
                    _diagnostics.Add(new Diagnostic("CN_ACC002", DiagnosticSeverity.Error,
                        "Protected/internal is not valid here.", diagLocation));
                    return @default;
                }
                return Accessibility.ProtectedOrInternal;
            }

            if (hasPrivate && hasProtected)
            {
                if (!allowProtected)
                {
                    _diagnostics.Add(new Diagnostic("CN_ACC002", DiagnosticSeverity.Error,
                        "Private protected is not valid here.", diagLocation));
                    return @default;
                }
                return Accessibility.ProtectedAndInternal;
            }

            if (hasPublic) return Accessibility.Public;
            if (hasPrivate) return Accessibility.Private;

            if (hasProtected)
            {
                if (!allowProtected)
                {
                    _diagnostics.Add(new Diagnostic("CN_ACC002", DiagnosticSeverity.Error,
                        "Protected is not valid here.", diagLocation));
                    return @default;
                }
                return Accessibility.Protected;
            }

            if (hasInternal)
            {
                if (!allowInternal)
                {
                    _diagnostics.Add(new Diagnostic("CN_ACC002", DiagnosticSeverity.Error,
                        "Internal is not valid here.", diagLocation));
                    return @default;
                }
                return Accessibility.Internal;
            }

            return @default;
        }
        private void DeclareMethodDeclaration(SyntaxTree tree, MethodDeclarationSyntax syntax, NamedTypeSymbol container)
        {
            var name = syntax.Identifier.ValueText ?? "";
            var isStatic = HasModifier(syntax.Modifiers, SyntaxKind.StaticKeyword);
            var isAsync = HasModifier(syntax.Modifiers, SyntaxKind.AsyncKeyword);
            var isOverride = HasModifier(syntax.Modifiers, SyntaxKind.OverrideKeyword);
            var isAbstract = HasModifier(syntax.Modifiers, SyntaxKind.AbstractKeyword);
            var isVirtualKeyword = HasModifier(syntax.Modifiers, SyntaxKind.VirtualKeyword);
            var isSealed = HasModifier(syntax.Modifiers, SyntaxKind.SealedKeyword);
            var typeDefaultAcc = container is NamedTypeSymbol
                ? Accessibility.Private
                : Accessibility.Internal;
            var declaredAcc = DecodeDeclaredAccessibility(
                syntax.Modifiers,
                typeDefaultAcc,
                allowProtected: container is NamedTypeSymbol,
                allowInternal: true,
                diagLocation: new Location(tree, syntax.Span));
            bool isVirtual = isVirtualKeyword || isOverride || isAbstract;
            // placeholder types
            var placeholderReturn = new ErrorTypeSymbol("unbound-return", containing: null, ImmutableArray<Location>.Empty);

            var method = new SourceMethodSymbol(
                name: name,
                containing: container,
                returnType: placeholderReturn,
                typeParameters: default,
                isStatic: isStatic,
                isConstructor: false,
                isAsync: isAsync,
                locations: ImmutableArray.Create(new Location(tree, syntax.Span)),
                declaredAccessibility: declaredAcc,
                isExtensionMethod: syntax.ParameterList.Parameters.Count != 0
                    && HasModifier(syntax.ParameterList.Parameters[0].Modifiers, SyntaxKind.ThisKeyword));
            method.SetDispatchFlags(isVirtual, isAbstract, isOverride, isSealed);
            var tps = DeclareTypeParameters(tree, method, syntax.TypeParameterList);
            method.SetTypeParameters(tps);
            // placeholder
            var ps = ImmutableArray.CreateBuilder<ParameterSymbol>(syntax.ParameterList.Parameters.Count);
            for (int i = 0; i < syntax.ParameterList.Parameters.Count; i++)
            {
                var p = syntax.ParameterList.Parameters[i];
                var pName = p.Identifier.ValueText ?? "";
                var pType = new ErrorTypeSymbol("unbound-param", containing: null, ImmutableArray<Location>.Empty);
                bool isParams = IsParamsParameter(p);

                var param = new ParameterSymbol(
                    name: pName,
                    containing: method,
                    type: pType,
                    locations: ImmutableArray.Create(new Location(tree, p.Span)),
                    isReadOnlyRef: IsReadOnlyByRefParameter(p),
                    refKind: GetParameterRefKind(p),
                    isScoped: IsScopedParameter(p),
                    isParams: isParams);

                ps.Add(param);
                RecordDeclared(tree, p, param);
            }

            method.SetParameters(ps.ToImmutable());
            method.AddDeclaration(new SyntaxReference(tree, syntax));

            AddMemberToType(container, method);
            RecordDeclared(tree, syntax, method);
        }
        internal static bool IsParamsParameter(ParameterSyntax p)
            => HasModifier(p.Modifiers, SyntaxKind.ParamsKeyword);
        internal static bool IsReadOnlyByRefParameter(ParameterSyntax p)
        {
            return HasModifier(p.Modifiers, SyntaxKind.InKeyword)
                || (HasModifier(p.Modifiers, SyntaxKind.RefKeyword)
                && HasModifier(p.Modifiers, SyntaxKind.ReadOnlyKeyword));
        }
        internal static ParameterRefKind GetParameterRefKind(ParameterSyntax p)
        {
            if (HasModifier(p.Modifiers, SyntaxKind.RefKeyword)) return ParameterRefKind.Ref;
            if (HasModifier(p.Modifiers, SyntaxKind.OutKeyword)) return ParameterRefKind.Out;
            if (HasModifier(p.Modifiers, SyntaxKind.InKeyword)) return ParameterRefKind.In;
            return ParameterRefKind.None;
        }
        internal static bool IsScopedParameter(ParameterSyntax p)
            => HasModifier(p.Modifiers, SyntaxKind.ScopedKeyword);
        private void DeclareOperatorDeclaration(SyntaxTree tree, OperatorDeclarationSyntax syntax, NamedTypeSymbol container)
        {
            if (!TryGetOperatorMetadataName(syntax, out var metadataName))
            {
                _diagnostics.Add(new Diagnostic(
                    "CN_OPDECL001",
                    DiagnosticSeverity.Error,
                    $"Unsupported operator declaration '{metadataName}'.",
                    new Location(tree, syntax.Span)));
                return;
            }

            var isStatic = HasModifier(syntax.Modifiers, SyntaxKind.StaticKeyword);
            var isAsync = HasModifier(syntax.Modifiers, SyntaxKind.AsyncKeyword);
            var isOverride = HasModifier(syntax.Modifiers, SyntaxKind.OverrideKeyword);
            var isAbstract = HasModifier(syntax.Modifiers, SyntaxKind.AbstractKeyword);
            var isVirtualKeyword = HasModifier(syntax.Modifiers, SyntaxKind.VirtualKeyword);
            var isSealed = HasModifier(syntax.Modifiers, SyntaxKind.SealedKeyword);

            var declaredAcc = DecodeDeclaredAccessibility(
                syntax.Modifiers,
                Accessibility.Private,
                allowProtected: true,
                allowInternal: true,
                diagLocation: new Location(tree, syntax.Span));

            bool isVirtual = isVirtualKeyword || isOverride || isAbstract;

            var placeholderReturn = new ErrorTypeSymbol("unbound-return", containing: null, ImmutableArray<Location>.Empty);

            var method = new SourceMethodSymbol(
                name: metadataName,
                containing: container,
                returnType: placeholderReturn,
                typeParameters: default,
                isStatic: isStatic,
                isConstructor: false,
                isAsync: isAsync,
                locations: ImmutableArray.Create(new Location(tree, syntax.Span)),
                declaredAccessibility: declaredAcc);

            method.SetDispatchFlags(isVirtual, isAbstract, isOverride, isSealed);
            method.SetTypeParameters(ImmutableArray<TypeParameterSymbol>.Empty);

            var ps = ImmutableArray.CreateBuilder<ParameterSymbol>(syntax.ParameterList.Parameters.Count);
            for (int i = 0; i < syntax.ParameterList.Parameters.Count; i++)
            {
                var p = syntax.ParameterList.Parameters[i];
                var pName = p.Identifier.ValueText ?? "";
                var pType = new ErrorTypeSymbol("unbound-param", containing: null, ImmutableArray<Location>.Empty);

                var param = new ParameterSymbol(
                    name: pName,
                    containing: method,
                    type: pType,
                    locations: ImmutableArray.Create(new Location(tree, p.Span)),
                    isReadOnlyRef: IsReadOnlyByRefParameter(p),
                    refKind: GetParameterRefKind(p),
                    isScoped: IsScopedParameter(p));

                ps.Add(param);
                RecordDeclared(tree, p, param);
            }

            method.SetParameters(ps.ToImmutable());
            method.AddDeclaration(new SyntaxReference(tree, syntax));

            AddMemberToType(container, method);
            RecordDeclared(tree, syntax, method);
        }
        private void DeclareConversionOperatorDeclaration(
            SyntaxTree tree, ConversionOperatorDeclarationSyntax syntax, NamedTypeSymbol container)
        {
            bool isExplicit = syntax.ImplicitOrExplicitKeyword.Kind == SyntaxKind.ExplicitKeyword;
            bool isChecked = syntax.CheckedKeyword.Span.Length != 0;

            if (isChecked && !isExplicit)
            {
                _diagnostics.Add(new Diagnostic(
                    "CN_OPDECL002",
                    DiagnosticSeverity.Error,
                    "Checked conversion operator can only be explicit.",
                    new Location(tree, syntax.Span)));
            }

            string metadataName = isChecked
                ? $"{OpetatorPrefix}CheckedExplicit"
                : (isExplicit ? $"{OpetatorPrefix}Explicit" : $"{OpetatorPrefix}Implicit");

            var isStatic = HasModifier(syntax.Modifiers, SyntaxKind.StaticKeyword);
            var isAsync = HasModifier(syntax.Modifiers, SyntaxKind.AsyncKeyword);

            var declaredAcc = DecodeDeclaredAccessibility(
                syntax.Modifiers,
                Accessibility.Private,
                allowProtected: true,
                allowInternal: true,
                diagLocation: new Location(tree, syntax.Span));

            var placeholderReturn = new ErrorTypeSymbol("unbound-return", containing: null, ImmutableArray<Location>.Empty);

            var method = new SourceMethodSymbol(
                name: metadataName,
                containing: container,
                returnType: placeholderReturn,
                typeParameters: default,
                isStatic: isStatic,
                isConstructor: false,
                isAsync: isAsync,
                locations: ImmutableArray.Create(new Location(tree, syntax.Span)),
                declaredAccessibility: declaredAcc);

            method.SetDispatchFlags(isVirtual: false, isAbstract: false, isOverride: false, isSealed: false);
            method.SetTypeParameters(ImmutableArray<TypeParameterSymbol>.Empty);

            var ps = ImmutableArray.CreateBuilder<ParameterSymbol>(syntax.ParameterList.Parameters.Count);
            for (int i = 0; i < syntax.ParameterList.Parameters.Count; i++)
            {
                var p = syntax.ParameterList.Parameters[i];
                var pName = p.Identifier.ValueText ?? "";
                var pType = new ErrorTypeSymbol("unbound-param", containing: null, ImmutableArray<Location>.Empty);

                var param = new ParameterSymbol(
                    name: pName,
                    containing: method,
                    type: pType,
                    locations: ImmutableArray.Create(new Location(tree, p.Span)),
                    isReadOnlyRef: IsReadOnlyByRefParameter(p),
                    refKind: GetParameterRefKind(p),
                    isScoped: IsScopedParameter(p));

                ps.Add(param);
                RecordDeclared(tree, p, param);
            }

            method.SetParameters(ps.ToImmutable());
            method.AddDeclaration(new SyntaxReference(tree, syntax));

            AddMemberToType(container, method);
            RecordDeclared(tree, syntax, method);
        }
        private static bool TryGetOperatorMetadataName(OperatorDeclarationSyntax syntax, out string name)
        {
            bool isChecked = syntax.CheckedKeyword.Span.Length != 0;
            string op = syntax.OperatorToken.ValueText ?? string.Empty;
            int arity = syntax.ParameterList.Parameters.Count;

            switch (op)
            {
                case "+":
                    if (arity == 1) { name = $"{OpetatorPrefix}UnaryPlus"; return true; }
                    if (arity == 2)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedAddition" : $"{OpetatorPrefix}Addition"; return true;
                    }
                    break;

                case "-":
                    if (arity == 1)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedUnaryNegation" : $"{OpetatorPrefix}UnaryNegation"; return true;
                    }
                    if (arity == 2)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedSubtraction" : $"{OpetatorPrefix}Subtraction"; return true;
                    }
                    break;

                case "*":
                    if (arity == 2)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedMultiply" : $"{OpetatorPrefix}Multiply"; return true;
                    }
                    break;
                case "/":
                    if (arity == 2)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedDivision" : $"{OpetatorPrefix}Division"; return true;
                    }
                    break;
                case "%":
                    if (arity == 2) { name = $"{OpetatorPrefix}Modulus"; return true; }
                    break;

                case "&":
                    if (arity == 2) { name = $"{OpetatorPrefix}BitwiseAnd"; return true; }
                    break;
                case "|":
                    if (arity == 2) { name = $"{OpetatorPrefix}BitwiseOr"; return true; }
                    break;
                case "^":
                    if (arity == 2) { name = $"{OpetatorPrefix}ExclusiveOr"; return true; }
                    break;

                case "<<":
                    if (arity == 2) { name = $"{OpetatorPrefix}LeftShift"; return true; }
                    break;
                case ">>":
                    if (arity == 2) { name = $"{OpetatorPrefix}RightShift"; return true; }
                    break;
                case ">>>":
                    if (arity == 2) { name = $"{OpetatorPrefix}UnsignedRightShift"; return true; }
                    break;

                case "==":
                    if (arity == 2) { name = $"{OpetatorPrefix}Equality"; return true; }
                    break;
                case "!=":
                    if (arity == 2) { name = $"{OpetatorPrefix}Inequality"; return true; }
                    break;
                case "<":
                    if (arity == 2) { name = $"{OpetatorPrefix}LessThan"; return true; }
                    break;
                case "<=":
                    if (arity == 2) { name = $"{OpetatorPrefix}LessThanOrEqual"; return true; }
                    break;
                case ">":
                    if (arity == 2) { name = $"{OpetatorPrefix}GreaterThan"; return true; }
                    break;
                case ">=":
                    if (arity == 2) { name = $"{OpetatorPrefix}GreaterThanOrEqual"; return true; }
                    break;

                case "!":
                    if (arity == 1) { name = $"{OpetatorPrefix}LogicalNot"; return true; }
                    break;
                case "~":
                    if (arity == 1) { name = $"{OpetatorPrefix}OnesComplement"; return true; }
                    break;

                case "++":
                    if (arity == 1)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedIncrement"
                            : $"{OpetatorPrefix}Increment";
                        return true;
                    }
                    if (arity == 0)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedIncrementAssignment"
                            : $"{OpetatorPrefix}IncrementAssignment";
                        return true;
                    }
                    break;

                case "--":
                    if (arity == 1)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedDecrement"
                            : $"{OpetatorPrefix}Decrement";
                        return true;
                    }
                    if (arity == 0)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedDecrementAssignment"
                            : $"{OpetatorPrefix}DecrementAssignment";
                        return true;
                    }
                    break;

                case "true":
                    if (arity == 1) { name = $"{OpetatorPrefix}True"; return true; }
                    break;
                case "false":
                    if (arity == 1) { name = $"{OpetatorPrefix}False"; return true; }
                    break;

                case "+=":
                    if (arity == 1 || arity == 2)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedAdditionAssignment" : $"{OpetatorPrefix}AdditionAssignment"; return true;
                    }
                    break;
                case "-=":
                    if (arity == 1 || arity == 2)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedSubtractionAssignment" : $"{OpetatorPrefix}SubtractionAssignment"; return true;
                    }
                    break;
                case "*=":
                    if (arity == 1 || arity == 2)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedMultiplyAssignment" : $"{OpetatorPrefix}MultiplyAssignment"; return true;
                    }
                    break;
                case "/=":
                    if (arity == 1 || arity == 2)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedDivisionAssignment" : $"{OpetatorPrefix}DivisionAssignment"; return true;
                    }
                    break;

                case "%=":
                    if (arity == 1 || arity == 2) { name = $"{OpetatorPrefix}ModulusAssignment"; return true; }
                    break;
                case "&=":
                    if (arity == 1 || arity == 2) { name = $"{OpetatorPrefix}BitwiseAndAssignment"; return true; }
                    break;
                case "|=":
                    if (arity == 1 || arity == 2) { name = $"{OpetatorPrefix}BitwiseOrAssignment"; return true; }
                    break;
                case "^=":
                    if (arity == 1 || arity == 2) { name = $"{OpetatorPrefix}ExclusiveOrAssignment"; return true; }
                    break;
                case "<<=":
                    if (arity == 1 || arity == 2) { name = $"{OpetatorPrefix}LeftShiftAssignment"; return true; }
                    break;
                case ">>=":
                    if (arity == 1 || arity == 2) { name = $"{OpetatorPrefix}RightShiftAssignment"; return true; }
                    break;
                case ">>>=":
                    if (arity == 1 || arity == 2) { name = $"{OpetatorPrefix}UnsignedRightShiftAssignment"; return true; }
                    break;
            }

            name = op;
            return false;
        }
        private void DeclareConstructorDeclaration(SyntaxTree tree, ConstructorDeclarationSyntax syntax, NamedTypeSymbol container)
        {
            var isStatic = HasModifier(syntax.Modifiers, SyntaxKind.StaticKeyword);
            var isAsync = HasModifier(syntax.Modifiers, SyntaxKind.AsyncKeyword);
            var name = isStatic ? ".cctor" : (syntax.Identifier.ValueText ?? container.Name);
            var typeDefaultAcc = container is NamedTypeSymbol
                ? Accessibility.Private
                : Accessibility.Internal;

            var declaredAcc = DecodeDeclaredAccessibility(
                syntax.Modifiers,
                typeDefaultAcc,
                allowProtected: container is NamedTypeSymbol,
                allowInternal: true,
                diagLocation: new Location(tree, syntax.Span));
            var voidType = _types.GetSpecialType(SpecialType.System_Void);

            var ctor = new SourceMethodSymbol(
                name: name,
                containing: container,
                returnType: voidType,
                typeParameters: default,
                isStatic: isStatic,
                isConstructor: true,
                isAsync: isAsync,
                locations: ImmutableArray.Create(new Location(tree, syntax.Span)),
                declaredAccessibility: declaredAcc);
            ctor.SetTypeParameters(ImmutableArray<TypeParameterSymbol>.Empty);
            var ps = ImmutableArray.CreateBuilder<ParameterSymbol>(syntax.ParameterList.Parameters.Count);
            for (int i = 0; i < syntax.ParameterList.Parameters.Count; i++)
            {
                var p = syntax.ParameterList.Parameters[i];
                var pName = p.Identifier.ValueText ?? "";
                var pType = new ErrorTypeSymbol("unbound-param", containing: null, ImmutableArray<Location>.Empty);
                bool isParams = IsParamsParameter(p);

                var param = new ParameterSymbol(
                    name: pName,
                    containing: ctor,
                    type: pType,
                    locations: ImmutableArray.Create(new Location(tree, p.Span)),
                    isReadOnlyRef: IsReadOnlyByRefParameter(p),
                    refKind: GetParameterRefKind(p),
                    isScoped: IsScopedParameter(p),
                    isParams: isParams);

                ps.Add(param);
                RecordDeclared(tree, p, param);
            }

            ctor.SetParameters(ps.ToImmutable());
            ctor.AddDeclaration(new SyntaxReference(tree, syntax));

            AddMemberToType(container, ctor);
            RecordDeclared(tree, syntax, ctor);
        }
        private void DeclareIndexerDeclaration(SyntaxTree tree, IndexerDeclarationSyntax syntax, NamedTypeSymbol container)
        {
            const string metadataName = "Item";

            bool isStatic = HasModifier(syntax.Modifiers, SyntaxKind.StaticKeyword);
            var typeDefaultAcc = container is NamedTypeSymbol
                ? Accessibility.Private
                : Accessibility.Internal;

            var declaredAcc = DecodeDeclaredAccessibility(
                syntax.Modifiers,
                typeDefaultAcc,
                allowProtected: container is NamedTypeSymbol,
                allowInternal: true,
                diagLocation: new Location(tree, syntax.Span));

            bool hasGet = false;
            bool hasSet = false;
            AccessorDeclarationSyntax? getAccessorSyntax = null;
            AccessorDeclarationSyntax? setAccessorSyntax = null;

            if (syntax.ExpressionBody is not null)
            {
                hasGet = true;
            }
            else if (syntax.AccessorList is not null)
            {
                var accessors = syntax.AccessorList.Accessors;
                for (int i = 0; i < accessors.Count; i++)
                {
                    var a = accessors[i];
                    switch (a.Kind)
                    {
                        case SyntaxKind.GetAccessorDeclaration:
                            hasGet = true;
                            getAccessorSyntax ??= a;
                            break;
                        case SyntaxKind.SetAccessorDeclaration:
                            hasSet = true;
                            setAccessorSyntax ??= a;
                            break;
                    }
                }
            }

            if (!hasGet && !hasSet)
            {
                _diagnostics.Add(new Diagnostic(
                    id: "CN_IDX000",
                    severity: DiagnosticSeverity.Error,
                    message: "Indexer must declare at least a get or set accessor.",
                    location: new Location(tree, syntax.Span)));
                return;
            }

            var placeholderType = new ErrorTypeSymbol("unbound-indexer", containing: null, ImmutableArray<Location>.Empty);

            SourceMethodSymbol? getMethod = null;
            SourceMethodSymbol? setMethod = null;

            if (hasGet)
            {
                var loc = new Location(tree, (getAccessorSyntax ?? (SyntaxNode)syntax).Span);
                getMethod = new SourceMethodSymbol(
                    name: $"get_{metadataName}",
                    containing: container,
                    returnType: placeholderType,
                    typeParameters: ImmutableArray<TypeParameterSymbol>.Empty,
                    isStatic: isStatic,
                    isConstructor: false,
                    isAsync: false,
                    locations: ImmutableArray.Create(loc),
                    declaredAccessibility: declaredAcc);

                getMethod.AddDeclaration(new SyntaxReference(tree, (getAccessorSyntax ?? (SyntaxNode)syntax)));

                AddMemberToType(container, getMethod);
                if (getAccessorSyntax is not null)
                    RecordDeclared(tree, getAccessorSyntax, getMethod);
            }

            if (hasSet)
            {
                var voidType = _types.GetSpecialType(SpecialType.System_Void);
                var loc = new Location(tree, (setAccessorSyntax ?? (SyntaxNode)syntax).Span);

                setMethod = new SourceMethodSymbol(
                    name: $"set_{metadataName}",
                    containing: container,
                    returnType: voidType,
                    typeParameters: ImmutableArray<TypeParameterSymbol>.Empty,
                    isStatic: isStatic,
                    isConstructor: false,
                    isAsync: false,
                    locations: ImmutableArray.Create(loc),
                    declaredAccessibility: declaredAcc);

                setMethod.AddDeclaration(new SyntaxReference(tree, (setAccessorSyntax ?? (SyntaxNode)syntax)));

                AddMemberToType(container, setMethod);
                if (setAccessorSyntax is not null)
                    RecordDeclared(tree, setAccessorSyntax, setMethod);
            }

            var prop = new SourcePropertySymbol(
                name: metadataName,
                containing: container,
                declaredTypeSyntax: syntax.Type,
                placeholderType: placeholderType,
                isStatic: isStatic,
                declaredAccessibility: declaredAcc,
                hasGet: hasGet,
                hasSet: hasSet,
                getMethod: getMethod,
                setMethod: setMethod,
                location: new Location(tree, syntax.Span),
                declarationRef: new SyntaxReference(tree, syntax));

            // Property parameters (for property signature / metadata).
            var propParams = ImmutableArray.CreateBuilder<ParameterSymbol>(syntax.ParameterList.Parameters.Count);
            for (int i = 0; i < syntax.ParameterList.Parameters.Count; i++)
            {
                var p = syntax.ParameterList.Parameters[i];
                var pName = p.Identifier.ValueText ?? "";
                var pType = new ErrorTypeSymbol("unbound-param", containing: null, ImmutableArray<Location>.Empty);

                var param = new ParameterSymbol(
                    name: pName,
                    containing: prop,
                    type: pType,
                    locations: ImmutableArray.Create(new Location(tree, p.Span)),
                    isReadOnlyRef: IsReadOnlyByRefParameter(p),
                    refKind: GetParameterRefKind(p),
                    isScoped: IsScopedParameter(p));

                propParams.Add(param);
                RecordDeclared(tree, p, param);
            }

            prop.SetParameters(propParams.ToImmutable());

            // Getter parameters mirror indexer parameters.
            if (getMethod is not null)
            {
                var getParams = ImmutableArray.CreateBuilder<ParameterSymbol>(propParams.Count);
                for (int i = 0; i < propParams.Count; i++)
                {
                    var src = propParams[i];
                    getParams.Add(new ParameterSymbol(
                        name: src.Name,
                        containing: getMethod,
                        type: src.Type,
                        locations: src.Locations,
                        isReadOnlyRef: src.IsReadOnlyRef));
                }
                getMethod.SetParameters(getParams.ToImmutable());
            }

            // Setter parameters: indexer parameters + implicit value.
            if (setMethod is not null)
            {
                var setParams = ImmutableArray.CreateBuilder<ParameterSymbol>(propParams.Count + 1);
                for (int i = 0; i < propParams.Count; i++)
                {
                    var src = propParams[i];
                    setParams.Add(new ParameterSymbol(
                        name: src.Name,
                        containing: setMethod,
                        type: src.Type,
                        locations: src.Locations,
                        isReadOnlyRef: src.IsReadOnlyRef));
                }

                setParams.Add(new ParameterSymbol(
                    name: "value",
                    containing: setMethod,
                    type: placeholderType,
                    locations: ImmutableArray.Create(new Location(tree, syntax.Span))));

                setMethod.SetParameters(setParams.ToImmutable());
            }

            AddMemberToType(container, prop);
            RecordDeclared(tree, syntax, prop);
        }
        private void DeclarePropertyDeclaration(SyntaxTree tree, PropertyDeclarationSyntax syntax, NamedTypeSymbol container)
        {
            var name = syntax.Identifier.ValueText ?? "";
            if (name.Length == 0)
                return;

            bool isStatic = HasModifier(syntax.Modifiers, SyntaxKind.StaticKeyword);
            var typeDefaultAcc = container is NamedTypeSymbol
                ? Accessibility.Private
                : Accessibility.Internal;

            var declaredAcc = DecodeDeclaredAccessibility(
                syntax.Modifiers,
                typeDefaultAcc,
                allowProtected: container is NamedTypeSymbol,
                allowInternal: true,
                diagLocation: new Location(tree, syntax.Span));
            bool hasGet = false;
            bool hasSet = false;

            AccessorDeclarationSyntax? getAccessorSyntax = null;
            AccessorDeclarationSyntax? setAccessorSyntax = null;

            if (syntax.ExpressionBody is not null)
            {
                // Expression bodied property
                hasGet = true;
            }
            else if (syntax.AccessorList is not null)
            {
                var accessors = syntax.AccessorList.Accessors;
                for (int i = 0; i < accessors.Count; i++)
                {
                    var a = accessors[i];
                    switch (a.Kind)
                    {
                        case SyntaxKind.GetAccessorDeclaration:
                            hasGet = true;
                            getAccessorSyntax ??= a;
                            break;
                        case SyntaxKind.SetAccessorDeclaration:
                            hasSet = true;
                            setAccessorSyntax ??= a;
                            break;
                    }
                }
            }
            if (!hasGet && !hasSet)
            {
                _diagnostics.Add(new Diagnostic(
                    id: "CN_PROP000",
                    severity: DiagnosticSeverity.Error,
                    message: $"Property '{name}' must declare at least a get or set accessor.",
                    location: new Location(tree, syntax.Span)));
                return;
            }
            var placeholderType = new ErrorTypeSymbol("unbound-property", containing: null, ImmutableArray<Location>.Empty);

            SourceMethodSymbol? getMethod = null;
            SourceMethodSymbol? setMethod = null;

            if (hasGet)
            {
                var loc = new Location(tree, (getAccessorSyntax ?? syntax as SyntaxNode).Span);
                getMethod = new SourceMethodSymbol(
                    name: $"get_{name}",
                    containing: container,
                    returnType: placeholderType,
                    typeParameters: ImmutableArray<TypeParameterSymbol>.Empty,
                    isStatic: isStatic,
                    isConstructor: false,
                    isAsync: false,
                    locations: ImmutableArray.Create(loc),
                    declaredAccessibility: declaredAcc);

                getMethod.SetParameters(ImmutableArray<ParameterSymbol>.Empty);
                getMethod.AddDeclaration(new SyntaxReference(tree, (getAccessorSyntax ?? syntax as SyntaxNode)));

                AddMemberToType(container, getMethod);
                if (getAccessorSyntax is not null)
                    RecordDeclared(tree, getAccessorSyntax, getMethod);
            }

            if (hasSet)
            {
                var voidType = _types.GetSpecialType(SpecialType.System_Void);
                var loc = new Location(tree, (setAccessorSyntax ?? syntax as SyntaxNode).Span);

                setMethod = new SourceMethodSymbol(
                    name: $"set_{name}",
                    containing: container,
                    returnType: voidType,
                    typeParameters: ImmutableArray<TypeParameterSymbol>.Empty,
                    isStatic: isStatic,
                    isConstructor: false,
                    isAsync: false,
                    locations: ImmutableArray.Create(loc),
                    declaredAccessibility: declaredAcc);

                // Implicit value parameter.
                var valueParam = new ParameterSymbol(
                    name: "value",
                    containing: setMethod,
                    type: placeholderType,
                    locations: ImmutableArray.Create(loc));

                setMethod.SetParameters(ImmutableArray.Create(valueParam));
                setMethod.AddDeclaration(new SyntaxReference(tree, (setAccessorSyntax ?? syntax as SyntaxNode)));

                AddMemberToType(container, setMethod);
                if (setAccessorSyntax is not null)
                    RecordDeclared(tree, setAccessorSyntax, setMethod);
            }
            var prop = new SourcePropertySymbol(
                name: name,
                containing: container,
                declaredTypeSyntax: syntax.Type,
                placeholderType: placeholderType,
                isStatic: isStatic,
                declaredAccessibility: declaredAcc,
                hasGet: hasGet,
                hasSet: hasSet,
                getMethod: getMethod,
                setMethod: setMethod,
                location: new Location(tree, syntax.Span),
                declarationRef: new SyntaxReference(tree, syntax));

            AddMemberToType(container, prop);
            RecordDeclared(tree, syntax, prop);
        }
        private void DeclareFieldDeclaration(SyntaxTree tree, FieldDeclarationSyntax syntax, NamedTypeSymbol container)
        {
            bool isConst = HasModifier(syntax.Modifiers, SyntaxKind.ConstKeyword);
            bool isStatic = isConst || HasModifier(syntax.Modifiers, SyntaxKind.StaticKeyword);
            bool isReadOnly = !isConst && HasModifier(syntax.Modifiers, SyntaxKind.ReadOnlyKeyword);
            bool isUnsafe = HasModifier(syntax.Modifiers, SyntaxKind.UnsafeKeyword);
            var typeDefaultAcc = container is NamedTypeSymbol
                ? Accessibility.Private
                : Accessibility.Internal;

            var declaredAcc = DecodeDeclaredAccessibility(
                syntax.Modifiers,
                typeDefaultAcc,
                allowProtected: container is NamedTypeSymbol,
                allowInternal: true,
                diagLocation: new Location(tree, syntax.Span));
            var placeholderType = new ErrorTypeSymbol("unbound-field", containing: null, ImmutableArray<Location>.Empty);
            var decl = syntax.Declaration;

            for (int i = 0; i < decl.Variables.Count; i++)
            {
                var v = decl.Variables[i];
                var name = v.Identifier.ValueText ?? "";
                if (name.Length == 0)
                    continue;

                var field = new SourceFieldSymbol(
                    name: name,
                    containing: container,
                    declaredTypeSyntax: decl.Type,
                    placeholderType: placeholderType,
                    isStatic: isStatic,
                    isConst: isConst,
                    isReadOnly: isReadOnly,
                    isUnsafe: isUnsafe,
                    declaredAccessibility: declaredAcc,
                    location: new Location(tree, v.Span),
                    declarationRef: new SyntaxReference(tree, v),
                    attributeOwnerDeclarationRef: new SyntaxReference(tree, syntax));

                AddMemberToType(container, field);

                RecordDeclared(tree, v, field);
            }
        }
        private void DeclareEnumDeclaration(SyntaxTree tree, EnumDeclarationSyntax syntax, Symbol container)
        {
            var name = syntax.Identifier.ValueText ?? "";
            var key = (container, name, arity: 0);
            var typeDefaultAcc = container is NamedTypeSymbol
                ? Accessibility.Private
                : Accessibility.Internal;
            NamedTypeSymbol type;
            var declaredAcc = DecodeDeclaredAccessibility(
                syntax.Modifiers,
                typeDefaultAcc,
                allowProtected: container is NamedTypeSymbol,
                allowInternal: true,
                diagLocation: new Location(tree, syntax.Span));
            if (!_typeCache.TryGetValue(key, out var existing))
            {
                var s = new SourceNamedTypeSymbol(name, container, TypeKind.Enum, arity: 0, declaredAcc);
                s.SetDefaultBaseType(GetDefaultBaseType(TypeKind.Enum));
                s.SetTypeParameters(ImmutableArray<TypeParameterSymbol>.Empty);

                type = s;
                _typeCache.Add(key, type);

                if (container is SourceNamespaceSymbol ns)
                    ns.AddType(type);
                else if (container is NamedTypeSymbol nt)
                    AddNestedTypeToType(nt, type);
                else
                    throw new InvalidOperationException($"Invalid container for enum: {container.Kind}");
            }
            else
            {
                type = existing;

                if (type.TypeKind != TypeKind.Enum)
                {
                    _diagnostics.Add(new Diagnostic(
                        id: "CN0002",
                        severity: DiagnosticSeverity.Error,
                        message: $"Type '{name}' conflicts with enum declaration.",
                        location: new Location(tree, syntax.Span)));
                }
            }

            if (type is SourceNamedTypeSymbol src)
                src.AddDeclaration(tree, syntax);

            RecordDeclared(tree, syntax, type);

            if (type.TypeKind != TypeKind.Enum || type is not NamedTypeSymbol enumType)
                return;

            for (int i = 0; i < syntax.Members.Count; i++)
            {
                var em = syntax.Members[i];
                var memberName = em.Identifier.ValueText ?? "";
                if (memberName.Length == 0)
                    continue;

                var field = new SourceFieldSymbol(
                    name: memberName,
                    containing: enumType,
                    declaredTypeSyntax: null,
                    placeholderType: enumType,
                    isStatic: true,
                    isConst: true,
                    isReadOnly: false,
                    isUnsafe: false,
                    declaredAccessibility: Accessibility.Public,
                    location: new Location(tree, em.Span),
                    declarationRef: new SyntaxReference(tree, em),
                    attributeOwnerDeclarationRef: new SyntaxReference(tree, em));

                AddMemberToType(enumType, field);
                RecordDeclared(tree, em, field);
            }
        }
        private ImmutableArray<TypeParameterSymbol> DeclareTypeParameters(
            SyntaxTree tree, Symbol owner, TypeParameterListSyntax? list)
        {
            if (list == null || list.Parameters.Count == 0)
                return ImmutableArray<TypeParameterSymbol>.Empty;

            var b = ImmutableArray.CreateBuilder<TypeParameterSymbol>(list.Parameters.Count);
            for (int i = 0; i < list.Parameters.Count; i++)
            {
                var p = list.Parameters[i];
                var name = p.Identifier.ValueText ?? "";
                var tp = new TypeParameterSymbol(
                    name: name,
                    containing: owner,
                    ordinal: i,
                    locations: ImmutableArray.Create(new Location(tree, p.Span)));

                b.Add(tp);
                RecordDeclared(tree, p, tp);
            }
            return b.ToImmutable();
        }
        private static IEnumerable<string> EnumerateDottedNameParts(NameSyntax name)
        {
            switch (name)
            {
                case IdentifierNameSyntax id:
                    yield return id.Identifier.ValueText ?? "";
                    yield break;

                case QualifiedNameSyntax q:
                    foreach (var left in EnumerateDottedNameParts(q.Left))
                        yield return left;

                    yield return q.Right switch
                    {
                        IdentifierNameSyntax rid => rid.Identifier.ValueText ?? "",
                        GenericNameSyntax rg => rg.Identifier.ValueText ?? "",
                        _ => ""
                    };
                    yield break;

                default:
                    yield return "";
                    yield break;
            }
        }
    }

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

        private static bool IsSameOrNestedRelation(NamedTypeSymbol a, NamedTypeSymbol b)
            => ReferenceEquals(a, b) || IsNestedWithin(a, b) || IsNestedWithin(b, a);

        private static bool IsNestedWithin(NamedTypeSymbol maybeInner, NamedTypeSymbol maybeOuter)
        {
            for (var cur = maybeInner.ContainingSymbol; cur is not null; cur = cur.ContainingSymbol)
                if (ReferenceEquals(cur, maybeOuter))
                    return true;
            return false;
        }

        private static bool IsSameOrDerived(NamedTypeSymbol type, NamedTypeSymbol baseType)
        {
            for (TypeSymbol? cur = type; cur is not null; cur = cur.BaseType)
            {
                if (ReferenceEquals(cur, baseType))
                    return true;
            }
            return false;
        }
    }


    public abstract class Binder
    {
        public Binder? Parent { get; }
        public BinderFlags Flags { get; }

        protected Binder(Binder? parent, BinderFlags flags)
        {
            Parent = parent;
            Flags = flags;
        }

        public abstract ImmutableArray<Symbol> LookupSymbols(int position, string? name = null);
        public abstract TypeSymbol BindType(TypeSyntax syntax, BindingContext context, DiagnosticBag diagnostics);
        // Bind layer
        public abstract BoundExpression BindExpression(ExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics);
        public abstract BoundStatement BindStatement(StatementSyntax node, BindingContext context, DiagnosticBag diagnostics);

        public abstract Symbol? GetDeclaredSymbol(SyntaxNode declaration);
        public virtual Symbol? BindNamespaceOrType(ExpressionSyntax expr, BindingContext context, DiagnosticBag diagnostics)
            => Parent?.BindNamespaceOrType(expr, context, diagnostics);
        internal virtual Imports GetImports(BindingContext context)
            => Parent?.GetImports(context) ?? Imports.Empty;
    }
    internal sealed class TypeBinder : Binder
    {
        private readonly Compilation _compilation;
        private readonly ImportScopeMap _importScopeMap;
        public TypeBinder(Binder? parent, BinderFlags flags, Compilation compilation, ImportScopeMap importScopeMap)
            : base(parent, flags)
        {
            _compilation = compilation;
            _importScopeMap = importScopeMap;
        }

        internal override Imports GetImports(BindingContext context)
            => _importScopeMap.GetImportsForSymbol(context.ContainingSymbol);
        public override ImmutableArray<Symbol> LookupSymbols(int position, string? name = null)
        {
            return Parent?.LookupSymbols(position, name) ?? ImmutableArray<Symbol>.Empty;
        }
        public override Symbol? GetDeclaredSymbol(SyntaxNode declaration)
            => Parent?.GetDeclaredSymbol(declaration);
        internal TypeSymbol BindAttributeType(NameSyntax name, BindingContext context, DiagnosticBag diagnostics)
            => BindNameType(name, context, diagnostics, allowAttributeSuffix: true, allowTypeParameterLookup: false);
        public override BoundExpression BindExpression(ExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            diagnostics.Add(new Diagnostic("CN_BIND_T001", DiagnosticSeverity.Error,
                $"Expression not supported in TypeBinder: {node.Kind}",
                new Location(context.SemanticModel.SyntaxTree, node.Span)));
            return new BoundBadExpression(node);
        }

        public override BoundStatement BindStatement(StatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            diagnostics.Add(new Diagnostic("CN_BIND_T002", DiagnosticSeverity.Error,
                $"Statement not supported in TypeBinder: {node.Kind}",
                new Location(context.SemanticModel.SyntaxTree, node.Span)));
            return new BoundBadStatement(node);
        }
        public override TypeSymbol BindType(TypeSyntax? syntax, BindingContext context, DiagnosticBag diagnostics)
        {

            switch (syntax)
            {
                case PredefinedTypeSyntax p:
                    return BindPredefinedType(p, context, diagnostics);

                case ArrayTypeSyntax a:
                    return BindArrayType(a, context, diagnostics);

                case NameSyntax n:
                    return BindNameType(n, context, diagnostics);

                case RefTypeSyntax rt:
                    return BindRefType(rt, context, diagnostics);

                case PointerTypeSyntax pt:
                    return BindPointerType(pt, context, diagnostics);

                case TupleTypeSyntax tt:
                    return BindTupleType(tt, context, diagnostics);

                case NullableTypeSyntax nt:
                    return BindNullableType(nt, context, diagnostics);

                default:
                    diagnostics.Add(new Diagnostic("CN_TYPE003", DiagnosticSeverity.Error,
                        $"Type syntax not supported: {syntax?.Kind}",
                        new Location(context.SemanticModel.SyntaxTree, syntax == null ? default(TextSpan) : syntax.Span)));
                    return new ErrorTypeSymbol("type", containing: null, ImmutableArray<Location>.Empty);
            }
        }
        private TypeSymbol BindPredefinedType(PredefinedTypeSyntax p, BindingContext context, DiagnosticBag diagnostics)
        {
            switch (p.Keyword.Kind)
            {
                case SyntaxKind.BoolKeyword: return _compilation.GetSpecialType(SpecialType.System_Boolean);
                case SyntaxKind.CharKeyword: return _compilation.GetSpecialType(SpecialType.System_Char);
                case SyntaxKind.StringKeyword: return _compilation.GetSpecialType(SpecialType.System_String);
                case SyntaxKind.IntKeyword: return _compilation.GetSpecialType(SpecialType.System_Int32);
                case SyntaxKind.LongKeyword: return _compilation.GetSpecialType(SpecialType.System_Int64);

                case SyntaxKind.SByteKeyword: return _compilation.GetSpecialType(SpecialType.System_Int8);
                case SyntaxKind.ByteKeyword: return _compilation.GetSpecialType(SpecialType.System_UInt8);
                case SyntaxKind.ShortKeyword: return _compilation.GetSpecialType(SpecialType.System_Int16);
                case SyntaxKind.UShortKeyword: return _compilation.GetSpecialType(SpecialType.System_UInt16);
                case SyntaxKind.UIntKeyword: return _compilation.GetSpecialType(SpecialType.System_UInt32);
                case SyntaxKind.ULongKeyword: return _compilation.GetSpecialType(SpecialType.System_UInt64);
                case SyntaxKind.FloatKeyword: return _compilation.GetSpecialType(SpecialType.System_Single);
                case SyntaxKind.DoubleKeyword: return _compilation.GetSpecialType(SpecialType.System_Double);
                case SyntaxKind.DecimalKeyword: return _compilation.GetSpecialType(SpecialType.System_Decimal);

                case SyntaxKind.ObjectKeyword: return _compilation.GetSpecialType(SpecialType.System_Object);
                case SyntaxKind.VoidKeyword: return _compilation.GetSpecialType(SpecialType.System_Void);

                case SyntaxKind.IdentifierToken:
                    if (p.Keyword.ValueText == "nint") return _compilation.GetSpecialType(SpecialType.System_IntPtr);
                    if (p.Keyword.ValueText == "nuint") return _compilation.GetSpecialType(SpecialType.System_UIntPtr);
                    break;
            }
            diagnostics.Add(new Diagnostic("CN_TYPE001", DiagnosticSeverity.Error,
                $"PredefinedType mapping not implemented: {p.Keyword.Kind}",
                new Location(context.SemanticModel.SyntaxTree, p.Span)));
            return new ErrorTypeSymbol("type", containing: null, ImmutableArray<Location>.Empty);
        }
        private TypeSymbol BindNullableType(NullableTypeSyntax nt, BindingContext context, DiagnosticBag diagnostics)
        {
            if (nt.ElementType is NullableTypeSyntax)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TYPE_NULL003",
                    DiagnosticSeverity.Error,
                    "Cannot apply '?' to a nullable type.",
                    new Location(context.SemanticModel.SyntaxTree, nt.Span)));

                return new ErrorTypeSymbol("nullable", containing: null, ImmutableArray<Location>.Empty);
            }

            var element = BindType(nt.ElementType, context, diagnostics);
            if (element.Kind == SymbolKind.Error)
                return element;

            if (element is PointerTypeSymbol or ByRefTypeSymbol || element.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TYPE_NULL002",
                    DiagnosticSeverity.Error,
                    "The '?' type modifier cannot be applied to this type.",
                    new Location(context.SemanticModel.SyntaxTree, nt.Span)));

                return new ErrorTypeSymbol("nullable", containing: null, ImmutableArray<Location>.Empty);
            }

            if (element.IsReferenceType)
                return element;

            if (!element.IsValueType)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TYPE_NULL002",
                    DiagnosticSeverity.Error,
                    "The '?' type modifier can only be applied to reference types or non-nullable value types.",
                    new Location(context.SemanticModel.SyntaxTree, nt.Span)));

                return new ErrorTypeSymbol("nullable", containing: null, ImmutableArray<Location>.Empty);
            }

            if (IsSystemNullableValueType(element))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TYPE_NULL003",
                    DiagnosticSeverity.Error,
                    "Cannot apply '?' to a nullable type.",
                    new Location(context.SemanticModel.SyntaxTree, nt.Span)));

                return new ErrorTypeSymbol("nullable", containing: null, ImmutableArray<Location>.Empty);
            }

            var nullableDef = GetSystemNullableDefinitionOrReport(context, diagnostics, nt);
            if (nullableDef.Kind == SymbolKind.Error)
                return nullableDef;

            var typeArgs = ImmutableArray.Create(element);
            var constructed = _compilation.ConstructNamedType(nullableDef, typeArgs);

            GenericConstraintChecker.CheckNamedTypeInstantiation(
                constructedType: constructed,
                typeArguments: typeArgs,
                getArgSpan: _ => nt.ElementType.Span,
                context: context,
                diagnostics: diagnostics);

            return constructed;
        }
        private static bool IsSystemNullableValueType(TypeSymbol t)
        {
            if (t is not NamedTypeSymbol nt || !nt.IsValueType)
                return false;

            var def = nt.OriginalDefinition;
            if (def.Arity != 1 || !string.Equals(def.Name, "Nullable", StringComparison.Ordinal))
                return false;

            return def.ContainingSymbol is NamespaceSymbol ns
                && string.Equals(ns.Name, "System", StringComparison.Ordinal);
        }
        private NamedTypeSymbol GetSystemNullableDefinitionOrReport(
            BindingContext context, DiagnosticBag diagnostics, SyntaxNode diagnosticNode)
        {
            var global = context.Compilation.GlobalNamespace;

            NamespaceSymbol? systemNs = null;
            foreach (var ns in global.GetNamespaceMembers())
            {
                if (string.Equals(ns.Name, "System", StringComparison.Ordinal))
                {
                    systemNs = ns;
                    break;
                }
            }

            if (systemNs is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TYPE_NULL004",
                    DiagnosticSeverity.Error,
                    "System namespace is required for nullable types.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                return new ErrorTypeSymbol("System.Nullable`1", containing: null, ImmutableArray<Location>.Empty);
            }

            var candidates = systemNs.GetTypeMembers("Nullable", 1);
            if (candidates.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TYPE_NULL004",
                    DiagnosticSeverity.Error,
                    "Core library type 'System.Nullable<T>' is required for nullable types.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                return new ErrorTypeSymbol("System.Nullable`1", containing: null, ImmutableArray<Location>.Empty);
            }

            return candidates[0];
        }
        private TypeSymbol BindTupleType(TupleTypeSyntax tt, BindingContext context, DiagnosticBag diagnostics)
        {
            var elems = tt.Elements;
            if (elems.Count < 2)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TUPTYPE000",
                    DiagnosticSeverity.Error,
                    "Tuple types must contain at least two elements.",
                    new Location(context.SemanticModel.SyntaxTree, tt.Span)));

                return new ErrorTypeSymbol("tuple", containing: null, ImmutableArray<Location>.Empty);
            }

            var types = ImmutableArray.CreateBuilder<TypeSymbol>(elems.Count);
            var names = ImmutableArray.CreateBuilder<string?>(elems.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < elems.Count; i++)
            {
                var e = elems[i];
                var t = BindType(e.Type, context, diagnostics);

                if (t.SpecialType == SpecialType.System_Void)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_TUPTYPE001",
                        DiagnosticSeverity.Error,
                        "Tuple element type cannot be void.",
                        new Location(context.SemanticModel.SyntaxTree, e.Type.Span)));
                    t = new ErrorTypeSymbol("void", containing: null, ImmutableArray<Location>.Empty);
                }

                types.Add(t);

                string? name = null;
                if (e.Identifier.Span.Length != 0)
                    name = e.Identifier.ValueText;

                if (!string.IsNullOrEmpty(name) && !seen.Add(name!))
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_TUPNAME000",
                        DiagnosticSeverity.Error,
                        $"Tuple element name '{name}' is a duplicate.",
                        new Location(context.SemanticModel.SyntaxTree, e.Identifier.Span)));
                    name = null;
                }
                names.Add(name);
            }
            return context.Compilation.CreateTupleType(types.ToImmutable(), names.ToImmutable());
        }
        private TypeSymbol BindRefType(RefTypeSyntax rt, BindingContext context, DiagnosticBag diagnostics)
        {
            var elem = BindType(rt.Type, context, diagnostics);

            if (elem.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REFTYPE001",
                    DiagnosticSeverity.Error,
                    "A by-ref type cannot reference 'void'.",
                    new Location(context.SemanticModel.SyntaxTree, rt.Type.Span)));

                return new ErrorTypeSymbol("ref", containing: null, ImmutableArray<Location>.Empty);
            }

            if (elem is ByRefTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REFTYPE002",
                    DiagnosticSeverity.Error,
                    "A by-ref type cannot reference another by-ref type.",
                    new Location(context.SemanticModel.SyntaxTree, rt.Type.Span)));

                return new ErrorTypeSymbol("ref", containing: null, ImmutableArray<Location>.Empty);
            }

            return context.Compilation.CreateByRefType(elem);
        }
        private TypeSymbol BindPointerType(PointerTypeSyntax p, BindingContext context, DiagnosticBag diagnostics)
        {
            if ((Flags & BinderFlags.UnsafeRegion) == 0)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_UNSAFE_TYPE001",
                    DiagnosticSeverity.Warning,
                    "Pointer types may only be used in an unsafe context.",
                    new Location(context.SemanticModel.SyntaxTree, p.Span)));
            }

            var elem = BindType(p.ElementType, context, diagnostics);

            if (elem.IsReferenceType || elem is ArrayTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_PTRTYPE001",
                    DiagnosticSeverity.Error,
                    $"Cannot take a pointer to managed type '{elem.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, p.ElementType.Span)));
                return new ErrorTypeSymbol("ptr", containing: null, ImmutableArray<Location>.Empty);
            }

            return _compilation.CreatePointerType(elem);
        }
        private TypeSymbol BindArrayType(ArrayTypeSyntax a, BindingContext context, DiagnosticBag diagnostics)
        {
            TypeSymbol t = BindType(a.ElementType, context, diagnostics);
            if (RefLikeRestrictionFacts.ContainsRefLike(t))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REFLIKE_ARR001",
                    DiagnosticSeverity.Error,
                    "Arrays cannot have ref-like element types.",
                    new Location(context.SemanticModel.SyntaxTree, a.ElementType.Span)));
            }
            for (int i = a.RankSpecifiers.Count - 1; i >= 0; i--)
            {
                var rs = a.RankSpecifiers[i];

                int rank = Math.Max(1, rs.Sizes.SeparatorCount + 1);
                t = _compilation.CreateArrayType(t, rank);
            }

            return t;
        }
        private static TypeParameterSymbol? LookupTypeParameter(string name, BindingContext context)
        {
            for (Symbol? s = context.ContainingSymbol; s != null; s = s.ContainingSymbol)
            {
                if (s is MethodSymbol ms)
                {
                    var tps = ms.TypeParameters;
                    for (int i = 0; i < tps.Length; i++)
                        if (StringComparer.Ordinal.Equals(tps[i].Name, name))
                            return tps[i];
                }
                else if (s is NamedTypeSymbol nt)
                {
                    var tps = nt.TypeParameters;
                    for (int i = 0; i < tps.Length; i++)
                        if (StringComparer.Ordinal.Equals(tps[i].Name, name))
                            return tps[i];
                }
            }

            return null;
        }
        private ImmutableArray<TypeSymbol> BindTypeArguments(
            SeparatedSyntaxList<TypeSyntax> args,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var b = ImmutableArray.CreateBuilder<TypeSymbol>(args.Count);
            for (int i = 0; i < args.Count; i++)
            {
                var ta = BindType(args[i], context, diagnostics);
                b.Add(ta);
            }
            return b.ToImmutable();
        }
        private TypeSymbol BindNameType(
            NameSyntax name,
            BindingContext context,
            DiagnosticBag diagnostics,
            bool allowAttributeSuffix = false,
            bool allowTypeParameterLookup = true)
        {
            var imports = GetImports(context);
            var parts = CollectParts(name);
            if (parts.Count == 0)
                return new ErrorTypeSymbol("type", containing: null, ImmutableArray<Location>.Empty);

            if (parts.Count == 1 && parts[0].Arity == 0 && parts[0].TypeArgListOpt is null)
            {
                if (StringComparer.Ordinal.Equals(parts[0].Name, "nint"))
                    return _compilation.GetSpecialType(SpecialType.System_IntPtr);
                if (StringComparer.Ordinal.Equals(parts[0].Name, "nuint"))
                    return _compilation.GetSpecialType(SpecialType.System_UIntPtr);
            }

            List<Symbol> CollectNextWithAttributeSuffix(
                List<Symbol> currentSet,
                NamePart part,
                bool hasTypeArgs,
                ImmutableArray<TypeSymbol> boundTypeArgs)
            {
                var nextSet = CollectNext(currentSet, part.Name, part.Arity, hasTypeArgs, boundTypeArgs, context);

                // Attribute lookup
                if (nextSet.Count == 0 &&
                    allowAttributeSuffix &&
                    !hasTypeArgs &&
                    part.Arity == 0 &&
                    !part.Name.EndsWith("Attribute", StringComparison.Ordinal))
                {
                    nextSet = CollectNext(
                        currentSet,
                        part.Name + "Attribute",
                        0,
                        hasTypeArgs: false,
                        boundTypeArgs: default,
                        context);
                }
                if (hasTypeArgs && part.TypeArgListOpt is not null)
                {
                    for (int i = 0; i < nextSet.Count; i++)
                    {
                        if (nextSet[i] is NamedTypeSymbol nt)
                        {
                            GenericConstraintChecker.CheckNamedTypeInstantiation(
                                constructedType: nt,
                                typeArguments: boundTypeArgs,
                                getArgSpan: a => part.TypeArgListOpt!.Arguments[a].Span,
                                context: context,
                                diagnostics: diagnostics);
                        }
                    }
                }

                return nextSet;
            }

            // Type parameter wins for an unqualified identifier
            if (allowTypeParameterLookup && parts.Count == 1 && parts[0].Arity == 0 && parts[0].TypeArgListOpt is null)
            {
                var tp = LookupTypeParameter(parts[0].Name, context);
                if (tp != null)
                    return tp;
            }

            var current = new List<Symbol>(capacity: 8);
            int startIndex = 0;

            if (imports.TryGetAlias(parts[0].Name, out var alias))
            {
                current.Add(alias!.Target);
                startIndex = 1;
            }
            else
            {
                var layer0 = new List<Symbol>(8);
                var layer1 = new List<Symbol>(8);
                var layer2 = new List<Symbol>(1);
                BuildRootLayers(context, imports, layer0, layer1, layer2);

                var first = parts[0];
                var hasTypeArgs = first.TypeArgListOpt != null;
                var boundTypeArgs = hasTypeArgs
                    ? BindTypeArguments(first.TypeArgListOpt!.Arguments, context, diagnostics)
                    : default;

                var next = CollectNextWithAttributeSuffix(layer0, first, hasTypeArgs, boundTypeArgs);
                if (next.Count == 0)
                    next = CollectNextWithAttributeSuffix(layer1, first, hasTypeArgs, boundTypeArgs);
                if (next.Count == 0)
                    next = CollectNextWithAttributeSuffix(layer2, first, hasTypeArgs, boundTypeArgs);

                if (next.Count == 0)
                {
                    diagnostics.Add(new Diagnostic("CN_TYPE_NAME001", DiagnosticSeverity.Error,
                        $"Type or namespace '{first.Name}' not found.",
                        new Location(context.SemanticModel.SyntaxTree, first.Syntax.Span)));

                    return new ErrorTypeSymbol(first.Name, containing: null, ImmutableArray<Location>.Empty);
                }

                current = next;
                startIndex = 1;
            }

            for (int i = startIndex; i < parts.Count; i++)
            {
                var part = parts[i];

                var hasTypeArgs = part.TypeArgListOpt != null;
                var boundTypeArgs = hasTypeArgs
                    ? BindTypeArguments(part.TypeArgListOpt!.Arguments, context, diagnostics)
                    : default;

                var next = CollectNextWithAttributeSuffix(current, part, hasTypeArgs, boundTypeArgs);

                if (next.Count == 0)
                {
                    diagnostics.Add(new Diagnostic("CN_TYPE_NAME001", DiagnosticSeverity.Error,
                        $"Type or namespace '{part.Name}' not found.",
                        new Location(context.SemanticModel.SyntaxTree, part.Syntax.Span)));

                    return new ErrorTypeSymbol(part.Name, containing: null, ImmutableArray<Location>.Empty);
                }

                current = next;
            }

            var typeCandidates = current.OfType<NamedTypeSymbol>().ToArray();
            if (typeCandidates.Length == 1)
                return typeCandidates[0];

            if (typeCandidates.Length > 1)
            {
                diagnostics.Add(new Diagnostic("CN_TYPE_NAME002", DiagnosticSeverity.Error,
                    $"Type name '{name}' is ambiguous.",
                    new Location(context.SemanticModel.SyntaxTree, name.Span)));

                return new ErrorTypeSymbol("ambiguous", containing: null, ImmutableArray<Location>.Empty);
            }

            diagnostics.Add(new Diagnostic("CN_TYPE_NAME003", DiagnosticSeverity.Error,
                $"'{name}' does not name a type.",
                new Location(context.SemanticModel.SyntaxTree, name.Span)));

            return new ErrorTypeSymbol("not-a-type", containing: null, ImmutableArray<Location>.Empty);
        }

        public override Symbol? BindNamespaceOrType(ExpressionSyntax expr, BindingContext context, DiagnosticBag diagnostics)
        {
            var imports = GetImports(context);
            var parts = CollectExprParts(expr);
            if (parts.Count == 0)
                return null;

            List<Symbol> CollectNextChecked(
                List<Symbol> currentSet,
                ExprPart part,
                bool hasTypeArgs,
                ImmutableArray<TypeSymbol> boundTypeArgs)
            {
                var nextSet = CollectNext(currentSet, part.Name, part.Arity, hasTypeArgs, boundTypeArgs, context);

                if (hasTypeArgs && part.TypeArgListOpt is not null)
                {
                    for (int i = 0; i < nextSet.Count; i++)
                    {
                        if (nextSet[i] is NamedTypeSymbol nt)
                        {
                            GenericConstraintChecker.CheckNamedTypeInstantiation(
                                constructedType: nt,
                                typeArguments: boundTypeArgs,
                                getArgSpan: a => part.TypeArgListOpt!.Arguments[a].Span,
                                context: context,
                                diagnostics: diagnostics);
                        }
                    }
                }

                return nextSet;
            }

            List<Symbol> current = new(capacity: 8);
            int startIndex = 0;

            if (imports.TryGetAlias(parts[0].Name, out var alias))
            {
                current.Add(alias!.Target);
                startIndex = 1;
            }
            else
            {
                var layer0 = new List<Symbol>(8);
                var layer1 = new List<Symbol>(8);
                var layer2 = new List<Symbol>(1);
                BuildRootLayers(context, imports, layer0, layer1, layer2);

                var first = parts[0];
                var hasTypeArgs = first.TypeArgListOpt != null;
                var boundTypeArgs = hasTypeArgs
                    ? BindTypeArguments(first.TypeArgListOpt!.Arguments, context, diagnostics)
                    : default;
                var next = CollectNextChecked(layer0, first, hasTypeArgs, boundTypeArgs);
                if (next.Count == 0)
                    next = CollectNextChecked(layer1, first, hasTypeArgs, boundTypeArgs);
                if (next.Count == 0)
                    next = CollectNextChecked(layer2, first, hasTypeArgs, boundTypeArgs);
                if (next.Count == 0)
                {
                    diagnostics.Add(new Diagnostic("CN_NSORTYPE001", DiagnosticSeverity.Error,
                        $"Name '{first.Name}' not found.",
                        new Location(context.SemanticModel.SyntaxTree, first.Syntax.Span)));
                }
                current = next;
                startIndex = 1;
            }

            for (int i = startIndex; i < parts.Count; i++)
            {
                var part = parts[i];

                var hasTypeArgs = part.TypeArgListOpt != null;
                var boundTypeArgs = hasTypeArgs
                    ? BindTypeArguments(part.TypeArgListOpt!.Arguments, context, diagnostics)
                    : default;

                var next = CollectNextChecked(current, part, hasTypeArgs, boundTypeArgs);

                if (next.Count == 0)
                {
                    diagnostics.Add(new Diagnostic("CN_NSORTYPE001", DiagnosticSeverity.Error,
                        $"Name '{part.Name}' not found.",
                        new Location(context.SemanticModel.SyntaxTree, part.Syntax.Span)));

                    return null;
                }

                current = next;
            }

            if (current.Count == 1)
                return current[0];

            diagnostics.Add(new Diagnostic("CN_NSORTYPE002", DiagnosticSeverity.Error,
                $"Name '{expr}' is ambiguous.",
                new Location(context.SemanticModel.SyntaxTree, expr.Span)));

            return null;
        }

        private static List<ExprPart> CollectExprParts(ExpressionSyntax expr)
        {
            var parts = new List<ExprPart>(capacity: 4);
            Collect(expr, parts);
            return parts;

            static void Collect(ExpressionSyntax e, List<ExprPart> dst)
            {
                switch (e)
                {
                    case IdentifierNameSyntax id:
                        dst.Add(new ExprPart(id.Identifier.ValueText ?? "", 0, id, typeArgListOpt: null));
                        return;

                    case GenericNameSyntax g:
                        dst.Add(new ExprPart(g.Identifier.ValueText ?? "", g.TypeArgumentList.Arguments.Count, g, g.TypeArgumentList));
                        return;

                    case MemberAccessExpressionSyntax ma:
                        Collect(ma.Expression, dst);
                        switch (ma.Name)
                        {
                            case IdentifierNameSyntax rid:
                                dst.Add(new ExprPart(rid.Identifier.ValueText ?? "", 0, rid, typeArgListOpt: null));
                                return;
                            case GenericNameSyntax rg:
                                dst.Add(new ExprPart(
                                    rg.Identifier.ValueText ?? "", rg.TypeArgumentList.Arguments.Count, rg, rg.TypeArgumentList));
                                return;
                            default:
                                dst.Add(new ExprPart("", 0, ma.Name, typeArgListOpt: null));
                                return;
                        }

                    default:
                        return;
                }
            }
        }
        private static List<NamePart> CollectParts(NameSyntax name)
        {
            var parts = new List<NamePart>(capacity: 4);
            Collect(name, parts);
            return parts;

            static void Collect(NameSyntax n, List<NamePart> dst)
            {
                switch (n)
                {
                    case IdentifierNameSyntax id:
                        dst.Add(new NamePart(id.Identifier.ValueText ?? "", 0, id, typeArgListOpt: null));
                        return;

                    case GenericNameSyntax g:
                        dst.Add(new NamePart(g.Identifier.ValueText ?? "", g.TypeArgumentList.Arguments.Count, g, g.TypeArgumentList));
                        return;

                    case QualifiedNameSyntax q:
                        Collect(q.Left, dst);
                        Collect(q.Right, dst);
                        return;

                    default:
                        dst.Add(new NamePart("", 0, n, typeArgListOpt: null));
                        return;
                }
            }
        }
        private static NamespaceSymbol? GetEnclosingNamespace(Symbol? s)
        {
            for (; s != null; s = s.ContainingSymbol)
                if (s is NamespaceSymbol ns)
                    return ns;
            return null;
        }

        private static void AddUnique(List<Symbol> list, Symbol sym)
        {
            for (int i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], sym))
                    return;
            list.Add(sym);
        }
        private void BuildRootLayers(BindingContext context, Imports imports, List<Symbol> layer0, List<Symbol> layer1, List<Symbol> layer2)
        {
            // Enclosing types
            for (Symbol? s = context.ContainingSymbol; s != null; s = s.ContainingSymbol)
            {
                if (s is NamedTypeSymbol nt)
                    AddUnique(layer0, nt);
            }

            if (!TryAddMergedEnclosingNamespaces(context.ContainingSymbol, layer0))
            {
                var ns = GetEnclosingNamespace(context.ContainingSymbol);
                for (NamespaceSymbol? n = ns; n != null; n = n.ContainingSymbol as NamespaceSymbol)
                    AddUnique(layer0, n);
            }

            // Imported containers
            for (int i = 0; i < imports.Containers.Length; i++)
                AddUnique(layer1, imports.Containers[i]);

            // using static
            for (int i = 0; i < imports.StaticTypes.Length; i++)
                AddUnique(layer1, imports.StaticTypes[i]);

            // Global
            AddUnique(layer2, _compilation.GlobalNamespace);
        }
        private bool TryAddMergedEnclosingNamespaces(Symbol? containing, List<Symbol> layer0)
        {
            var parts = new List<string>();
            for (var n = GetEnclosingNamespace(containing); n != null && !n.IsGlobalNamespace; n = n.ContainingSymbol as NamespaceSymbol)
                parts.Add(n.Name);

            // Reconstruct the same path starting from merged global namespace
            NamespaceSymbol cur = _compilation.GlobalNamespace;
            var chainOuterToInner = new List<NamespaceSymbol>(parts.Count);

            for (int i = parts.Count - 1; i >= 0; i--)
            {
                var part = parts[i];
                NamespaceSymbol? next = null;

                foreach (var child in cur.GetNamespaceMembers())
                {
                    if (StringComparer.Ordinal.Equals(child.Name, part))
                    {
                        next = child;
                        break;
                    }
                }

                if (next is null)
                    return false;

                chainOuterToInner.Add(next);
                cur = next;
            }

            for (int i = chainOuterToInner.Count - 1; i >= 0; i--)
                AddUnique(layer0, chainOuterToInner[i]);

            AddUnique(layer0, _compilation.GlobalNamespace);
            return true;
        }
        private List<Symbol> CollectNext(
            List<Symbol> current,
            string name,
            int arity,
            bool hasTypeArgs,
            ImmutableArray<TypeSymbol> boundTypeArgs,
            BindingContext context)
        {
            var next = new List<Symbol>();

            for (int i = 0; i < current.Count; i++)
            {
                var c = current[i];

                if (c is NamespaceSymbol ns)
                {
                    if (!hasTypeArgs)
                    {
                        foreach (var childNs in ns.GetNamespaceMembers())
                            if (StringComparer.Ordinal.Equals(childNs.Name, name))
                                next.Add(childNs);
                    }

                    foreach (var t in ns.GetTypeMembers(name, arity))
                    {
                        var inst = hasTypeArgs ? _compilation.ConstructNamedType(t, boundTypeArgs) : t;
                        if (AccessibilityHelper.IsAccessible(inst, context))
                            next.Add(inst);
                    }
                }
                else if (c is NamedTypeSymbol nt)
                {
                    foreach (var t in nt.GetTypeMembers(name, arity))
                    {
                        var inst = hasTypeArgs ? _compilation.ConstructNamedType(t, boundTypeArgs) : t;
                        if (AccessibilityHelper.IsAccessible(inst, context))
                            next.Add(inst);
                    }
                }
            }
            return next;
        }
        private readonly struct NamePart
        {
            public readonly string Name;
            public readonly int Arity;
            public readonly SyntaxNode Syntax;
            public readonly TypeArgumentListSyntax? TypeArgListOpt;
            public NamePart(string name, int arity, SyntaxNode syntax, TypeArgumentListSyntax? typeArgListOpt)
            {
                Name = name;
                Arity = arity;
                Syntax = syntax;
                TypeArgListOpt = typeArgListOpt;
            }
        }
        private readonly struct ExprPart
        {
            public readonly string Name;
            public readonly int Arity;
            public readonly SyntaxNode Syntax;
            public readonly TypeArgumentListSyntax? TypeArgListOpt;
            public ExprPart(string name, int arity, SyntaxNode syntax, TypeArgumentListSyntax? typeArgListOpt)
            {
                Name = name;
                Arity = arity;
                Syntax = syntax;
                TypeArgListOpt = typeArgListOpt;
            }
        }
    }
    internal sealed class LocalScopeBinder : Binder
    {
        private readonly struct LValue
        {
            public BoundExpression Target { get; }
            public BoundExpression Read { get; }
            public LValue(BoundExpression target, BoundExpression read)
            {
                Target = target;
                Read = read;
            }
        }
        private sealed class ControlFlowScope
        {
            private enum ExceptionRegionKind : byte
            {
                Try, Catch, Finally
            }
            private readonly struct ExceptionRegionFrame
            {
                public int Id { get; }
                public ExceptionRegionKind Kind { get; }
                public ExceptionRegionFrame(int id, ExceptionRegionKind kind)
                {
                    Id = id;
                    Kind = kind;
                }
            }
            private readonly Symbol _containing;
            private readonly MethodSymbol? _method;

            private readonly Dictionary<string, LabelSymbol> _labelsByName = new(StringComparer.Ordinal);
            private readonly List<(GotoStatementSyntax Syntax, LabelSymbol Label)> _gotos = new();
            private readonly List<ExceptionRegionFrame> _exceptionRegionStack = new();
            private readonly Dictionary<LabelSymbol, ImmutableArray<ExceptionRegionFrame>> _labelRegions = new();
            private readonly List<(GotoStatementSyntax Syntax, LabelSymbol Label,
                ImmutableArray<ExceptionRegionFrame> SourceRegions)> _gotoRegions = new();
            private readonly Stack<(LabelSymbol BreakLabel, LabelSymbol ContinueLabel)> _loopStack = new();
            private int _nextGeneratedId;
            private bool _diagnosticsEmitted;
            private int _nextExceptionRegionId;
            public ControlFlowScope(Symbol containing)
            {
                _containing = containing;
                _method = containing as MethodSymbol;
            }

            public LabelSymbol NewGeneratedLabel(string prefix)
            {
                var id = ++_nextGeneratedId;
                var m = _method;
                if (m is not null)
                    return LabelSymbol.CreateGenerated($"<{prefix}#{id}>", m);

                return new LabelSymbol($"<{prefix}#{id}>", _containing);
            }

            public LabelSymbol GetOrCreateSourceLabel(string name)
            {
                if (!_labelsByName.TryGetValue(name, out var label))
                {
                    label = new LabelSymbol(name, _containing);
                    _labelsByName.Add(name, label);
                }
                return label;
            }
            public void RegisterGoto(GotoStatementSyntax syntax, LabelSymbol label)
            {
                var snapshot = SnapshotExceptionRegions();
                _gotos.Add((syntax, label));
                _gotoRegions.Add((syntax, label, snapshot));
            }

            public void PushLoop(LabelSymbol breakLabel, LabelSymbol continueLabel)
                => _loopStack.Push((breakLabel, continueLabel));

            public void PopLoop()
            {
                if (_loopStack.Count != 0)
                    _loopStack.Pop();
            }
            public void PushTryRegion() => PushExceptionRegion(ExceptionRegionKind.Try);
            public void PushCatchRegion() => PushExceptionRegion(ExceptionRegionKind.Catch);
            public void PushFinallyRegion() => PushExceptionRegion(ExceptionRegionKind.Finally);
            public void PopExceptionRegion()
            {
                if (_exceptionRegionStack.Count != 0)
                    _exceptionRegionStack.RemoveAt(_exceptionRegionStack.Count - 1);
            }
            public bool IsInsideCatchRegion
            {
                get
                {
                    for (int i = _exceptionRegionStack.Count - 1; i >= 0; i--)
                        if (_exceptionRegionStack[i].Kind == ExceptionRegionKind.Catch)
                            return true;
                    return false;
                }
            }
            public void RegisterLabelDefinition(LabelSymbol label)
            {
                if (!_labelRegions.ContainsKey(label))
                    _labelRegions[label] = SnapshotExceptionRegions();
            }
            public void ValidateBranchTransfer(
                SyntaxNode syntax,
                LabelSymbol targetLabel,
                BindingContext context,
                DiagnosticBag diagnostics)
            {
                var src = SnapshotExceptionRegions();
                if (!_labelRegions.TryGetValue(targetLabel, out var dst))
                    return;
                AddTransferDiagnosticIfInvalid(src, dst, syntax, context, diagnostics);
            }
            public void ValidateMethodExitTransfer(
                SyntaxNode syntax,
                BindingContext context,
                DiagnosticBag diagnostics)
            {
                var src = SnapshotExceptionRegions();
                var dst = ImmutableArray<ExceptionRegionFrame>.Empty; // method exit
                AddTransferDiagnosticIfInvalid(src, dst, syntax, context, diagnostics);
            }
            public bool TryGetCurrentLoop(out LabelSymbol breakLabel, out LabelSymbol continueLabel)
            {
                if (_loopStack.Count == 0)
                {
                    breakLabel = null!;
                    continueLabel = null!;
                    return false;
                }

                var top = _loopStack.Peek();
                breakLabel = top.BreakLabel;
                continueLabel = top.ContinueLabel;
                return true;
            }

            public void ReportUndefinedLabels(BindingContext context, DiagnosticBag diagnostics)
            {
                if (_diagnosticsEmitted)
                    return;

                _diagnosticsEmitted = true;

                for (int i = 0; i < _gotos.Count; i++)
                {
                    var (syntax, label) = _gotos[i];
                    if (!label.IsDefined)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_FLOW004",
                            DiagnosticSeverity.Error,
                            $"Label '{label.Name}' does not exist in the current context.",
                            new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                    }
                }
                foreach (var (syntax, label, srcRegions) in _gotoRegions)
                {
                    if (!label.IsDefined)
                        continue;
                    if (!_labelRegions.TryGetValue(label, out var dstRegions))
                        continue;
                    AddTransferDiagnosticIfInvalid(srcRegions, dstRegions, syntax, context, diagnostics);
                }
            }
            private void PushExceptionRegion(ExceptionRegionKind kind)
                => _exceptionRegionStack.Add(new ExceptionRegionFrame(_nextExceptionRegionId++, kind));
            private ImmutableArray<ExceptionRegionFrame> SnapshotExceptionRegions()
            {
                if (_exceptionRegionStack.Count == 0)
                    return ImmutableArray<ExceptionRegionFrame>.Empty;
                return ImmutableArray.CreateRange(_exceptionRegionStack);
            }
            private static void AddTransferDiagnosticIfInvalid(
                ImmutableArray<ExceptionRegionFrame> src,
                ImmutableArray<ExceptionRegionFrame> dst,
                SyntaxNode syntax,
                BindingContext context,
                DiagnosticBag diagnostics)
            {
                if (TryClassifyIllegalTransfer(src, dst, out var id, out var message))
                {
                    diagnostics.Add(new Diagnostic(
                        id,
                        DiagnosticSeverity.Error,
                        message,
                        new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                }
            }
            private static bool TryClassifyIllegalTransfer(
                ImmutableArray<ExceptionRegionFrame> src,
                ImmutableArray<ExceptionRegionFrame> dst,
                out string diagnosticId,
                out string diagnosticMessage)
            {
                int common = 0;
                int max = Math.Min(src.Length, dst.Length);
                while (common < max &&
                    src[common].Id == dst[common].Id &&
                    src[common].Kind == dst[common].Kind)
                {
                    common++;
                }
                if (dst.Length > common)
                {
                    diagnosticId = "CN_FLOW009";
                    diagnosticMessage = "Control cannot enter a try, catch, or finally block.";
                    return true;
                }
                for (int i = common; i < src.Length; i++)
                {
                    if (src[i].Kind == ExceptionRegionKind.Finally)
                    {
                        diagnosticId = "CN_FLOW008";
                        diagnosticMessage = "Control cannot leave a finally clause.";
                        return true;
                    }
                }
                diagnosticId = string.Empty;
                diagnosticMessage = string.Empty;
                return false;
            }
        }
        private sealed class BoundTypeOnlyExpression : BoundExpression
        {
            public override BoundNodeKind Kind => BoundNodeKind.BadExpression;

            public BoundTypeOnlyExpression(ExpressionSyntax syntax, TypeSymbol type)
                : base(syntax)
            {
                Type = type;
                ConstantValueOpt = Optional<object>.None;
            }
        }
        private enum BindValueKind : byte
        {
            RValue, LValue
        }
        private sealed class NullBindingRecorder : IBindingRecorder
        {
            public static readonly NullBindingRecorder Instance = new();
            private NullBindingRecorder() { }

            public void RecordDeclared(SyntaxNode syntax, Symbol symbol) { }
            public void RecordBound(SyntaxNode syntax, BoundNode node) { }
        }
        private const string OperatorPrefix = "op_";
        private static BindingContext WithRecorder(BindingContext context, IBindingRecorder recorder)
            => new BindingContext(context.Compilation, context.SemanticModel, context.ContainingSymbol, recorder);
        private static readonly SyntaxTrivia[] s_noTrivia = Array.Empty<SyntaxTrivia>();
        private readonly Symbol _containing;
        private readonly ControlFlowScope _flow;
        private readonly Dictionary<string, LocalSymbol> _locals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ParameterSymbol> _parameters = new(StringComparer.Ordinal);
        private readonly Dictionary<string, LocalFunctionSymbol> _localFunctions = new(StringComparer.Ordinal);
        private readonly LocalScopeBinder? _nameConflictStop;

        private int _tempId;
        public LocalScopeBinder(
            Binder? parent,
            BinderFlags flags,
            Symbol containing,
            bool inheritFlowFromParent = true)
            : base(parent, flags)
        {
            _containing = containing;

            _flow =
                inheritFlowFromParent && parent is LocalScopeBinder ls
                ? ls._flow
                : new ControlFlowScope(containing);

            _nameConflictStop = (parent as LocalScopeBinder)?._nameConflictStop;

            if (containing is MethodSymbol m)
            {
                for (int i = 0; i < m.Parameters.Length; i++)
                    _parameters[m.Parameters[i].Name] = m.Parameters[i];
            }
        }
        private LocalScopeBinder(LocalScopeBinder template, BinderFlags flags)
            : base(template.Parent, flags)
        {
            _containing = template._containing;
            _flow = template._flow;
            _locals = template._locals;
            _parameters = template._parameters;
            _localFunctions = template._localFunctions;
            _nameConflictStop = template._nameConflictStop;
        }
        private LocalScopeBinder WithFlags(BinderFlags flags) => new LocalScopeBinder(this, flags);
        private bool IsCheckedOverflowContext
        {
            get
            {
                if ((Flags & BinderFlags.CheckedContext) != 0) return true;
                if ((Flags & BinderFlags.UncheckedContext) != 0) return false;
                return false; // default unchecked
            }
        }
        private static BinderFlags ApplyCheckedContext(BinderFlags flags, bool isChecked)
        {
            flags &= ~(BinderFlags.CheckedContext | BinderFlags.UncheckedContext);
            flags |= isChecked ? BinderFlags.CheckedContext : BinderFlags.UncheckedContext;
            return flags;
        }
        private static bool IsOverflowCheckedUnaryOperator(BoundUnaryOperatorKind op, TypeSymbol type)
        {
            if (op != BoundUnaryOperatorKind.UnaryMinus)
                return false;

            return type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64;
        }
        private static bool IsOverflowCheckedBinaryOperator(BoundBinaryOperatorKind op, TypeSymbol type)
        {
            if (op is not (
                BoundBinaryOperatorKind.Add or
                BoundBinaryOperatorKind.Subtract or
                BoundBinaryOperatorKind.Multiply or
                BoundBinaryOperatorKind.Divide or
                BoundBinaryOperatorKind.Modulo))
            {
                return false;
            }
            return type.SpecialType is
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64;
        }
        private void ImportFlowingLocal(LocalSymbol local)
            => _locals[local.Name] = local;
        private static void AddUniqueLocal(ImmutableArray<LocalSymbol>.Builder builder, LocalSymbol local)
        {
            for (int i = 0; i < builder.Count; i++)
            {
                if (ReferenceEquals(builder[i], local))
                    return;
            }
            builder.Add(local);
        }
        internal void ReportControlFlowDiagnostics(BindingContext context, DiagnosticBag diagnostics)
            => _flow.ReportUndefinedLabels(context, diagnostics);
        private static IdentifierNameSyntax MakeIdentifierName(string name, TextSpan span)
            => new IdentifierNameSyntax(
                new SyntaxToken(SyntaxKind.IdentifierToken, span, valueText: name, value: name, leadingTrivia: s_noTrivia, trailingTrivia: s_noTrivia));
        private static SyntaxToken MakeToken(SyntaxKind kind, TextSpan span)
            => new SyntaxToken(kind, span, valueText: null, value: null, leadingTrivia: s_noTrivia, trailingTrivia: s_noTrivia);
        private BoundExpression MakeBoolLiteral(SyntaxNode syntax, BindingContext ctx, bool value)
            => new BoundLiteralExpression(syntax, ctx.Compilation.GetSpecialType(SpecialType.System_Boolean), value);
        private static bool TryGetBoolConstant(BoundExpression expr, out bool value)
        {
            if (expr.ConstantValueOpt.HasValue && expr.ConstantValueOpt.Value is bool b)
            {
                value = b;
                return true;
            }
            value = false;
            return false;
        }
        private static bool IsTrueErrorType(TypeSymbol type)
        {
            if (type is not NamedTypeSymbol nt)
                return false;

            if (nt.TypeKind != TypeKind.Error)
                return false;

            return !string.Equals(nt.Name, "<unbound>", StringComparison.Ordinal);
        }

        private static bool ShouldSuppressCascade(BoundExpression expr)
            => expr.HasErrors || IsTrueErrorType(expr.Type);


        private static BoundExpression CreateErrorConversion(ExpressionSyntax exprSyntax, BoundExpression expr, TypeSymbol targetType)
        {
            if (ReferenceEquals(expr.Type, targetType))
                return expr;

            if (expr is BoundThrowExpression te)
            {
                te.SetType(targetType);
                return te;
            }

            var converted = new BoundConversionExpression(
                exprSyntax,
                targetType,
                expr,
                new Conversion(ConversionKind.None),
                isChecked: false);

            converted.SetHasErrors();
            return converted;
        }
        private BoundExpression MakeLogicalAnd(SyntaxNode syntax, BoundExpression left, BoundExpression right, BindingContext ctx, DiagnosticBag diagnostics)
        {
            var boolType = ctx.Compilation.GetSpecialType(SpecialType.System_Boolean);
            if (left.HasErrors || right.HasErrors)
                return new BoundBadExpression(syntax);
            if (left.Type.SpecialType != SpecialType.System_Boolean || right.Type.SpecialType != SpecialType.System_Boolean)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_SWITCH_BOOL000",
                    DiagnosticSeverity.Error,
                    "Internal error: synthesized switch condition is not boolean.",
                    new Location(ctx.SemanticModel.SyntaxTree, syntax.Span)));
                return new BoundBadExpression(syntax);
            }
            return new BoundBinaryExpression(syntax, BoundBinaryOperatorKind.LogicalAnd, boolType, left, right, Optional<object>.None);
        }

        private BoundExpression MakeLogicalOr(SyntaxNode syntax, BoundExpression left, BoundExpression right, BindingContext ctx, DiagnosticBag diagnostics)
        {
            var boolType = ctx.Compilation.GetSpecialType(SpecialType.System_Boolean);
            if (left.HasErrors || right.HasErrors)
                return new BoundBadExpression(syntax);
            if (left.Type.SpecialType != SpecialType.System_Boolean || right.Type.SpecialType != SpecialType.System_Boolean)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_SWITCH_BOOL000",
                    DiagnosticSeverity.Error,
                    "Internal error: synthesized switch condition is not boolean.",
                    new Location(ctx.SemanticModel.SyntaxTree, syntax.Span)));
                return new BoundBadExpression(syntax);
            }
            return new BoundBinaryExpression(syntax, BoundBinaryOperatorKind.LogicalOr, boolType, left, right, Optional<object>.None);
        }
        private bool TryGetLocalFromEnclosingScopes(string name, out LocalSymbol? local)
        {
            for (Binder? b = this; b is LocalScopeBinder ls; b = ls.Parent)
            {
                if (ls._locals.TryGetValue(name, out local))
                    return true;
            }
            local = null!;
            return false;
        }
        private bool TryGetParameterFromEnclosingScopes(string name, out ParameterSymbol? param)
        {
            for (Binder? b = this; b is LocalScopeBinder ls; b = ls.Parent)
            {
                if (ls._parameters.TryGetValue(name, out param))
                    return true;
            }
            param = null!;
            return false;
        }
        private bool IsNameDeclaredInEnclosingScopes(string name)
        {
            for (Binder? b = this; b is LocalScopeBinder ls && !ReferenceEquals(ls, _nameConflictStop); b = ls.Parent)
            {
                if (ls._locals.ContainsKey(name) || ls._parameters.ContainsKey(name) || ls._localFunctions.ContainsKey(name))
                    return true;
            }
            return false;
        }
        private bool TryGetLocalFunctionFromEnclosingScopes(string name, out LocalFunctionSymbol? localFunc)
        {
            for (Binder? b = this; b is LocalScopeBinder ls; b = ls.Parent)
            {
                if (ls._localFunctions.TryGetValue(name, out var f))
                {
                    localFunc = f;
                    return true;
                }
            }
            localFunc = null;
            return false;
        }
        private bool EnsureUnsafe(SyntaxNode diagnosticNode, BindingContext context, DiagnosticBag diagnostics)
        {
            if ((Flags & BinderFlags.UnsafeRegion) != 0)
                return true;

            diagnostics.Add(new Diagnostic(
                "CN_UNSAFE000",
                DiagnosticSeverity.Warning,
                "Unsafe code may only appear in an unsafe context.",
                new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

            return false;
        }
        private static bool IsVar(TypeSyntax typeSyntax)
        {
            return typeSyntax is IdentifierNameSyntax id &&
                   StringComparer.Ordinal.Equals(id.Identifier.ValueText, "var");
        }
        private BoundExpression Record(ExpressionSyntax s, BoundExpression b, BindingContext ctx)
        {
            ctx.Recorder.RecordBound(s, b);
            return b;
        }
        private BoundStatement Record(StatementSyntax s, BoundStatement b, BindingContext ctx)
        {
            ctx.Recorder.RecordBound(s, b);
            return b;
        }
        private bool TryBindImportedStaticMember(
            IdentifierNameSyntax id,
            string name,
            BindValueKind valueKind,
            BindingContext context,
            DiagnosticBag diagnostics,
            out BoundExpression result)
        {
            result = null!;

            var imports = GetImports(context);
            if (imports.StaticTypes.IsDefaultOrEmpty)
                return false;

            FieldSymbol? field = null;
            PropertySymbol? prop = null;
            bool ambiguous = false;

            for (int i = 0; i < imports.StaticTypes.Length; i++)
            {
                var t = imports.StaticTypes[i];
                var members = LookupMembers(t, name);
                if (members.IsDefaultOrEmpty)
                    continue;

                members = FilterAccessibleMembers(members, context);
                if (members.IsDefaultOrEmpty)
                    continue;

                for (int m = 0; m < members.Length; m++)
                {
                    switch (members[m])
                    {
                        case FieldSymbol fs when fs.IsStatic:
                            if (field is not null || prop is not null)
                                ambiguous = true;
                            else
                                field = fs;
                            break;

                        case PropertySymbol ps when ps.IsStatic:
                            if (prop is not null || field is not null)
                                ambiguous = true;
                            else
                                prop = ps;
                            break;
                    }
                }

                if (ambiguous)
                    break;
            }
            if (ambiguous)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_BIND_USINGSTATIC001",
                    DiagnosticSeverity.Error,
                    $"Member name '{name}' is ambiguous due to multiple 'using static' imports.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));
                result = new BoundBadExpression(id);
                return true;
            }
            if (field is null && prop is null)
                return false;

            if (field is not null)
            {
                bool isRefField = field.Type is ByRefTypeSymbol;
                TypeSymbol fieldValueType = isRefField ? ((ByRefTypeSymbol)field.Type).ElementType : field.Type;

                bool canWriteField = !field.IsConst && (isRefField || !field.IsReadOnly);
                var cv = field.IsConst ? field.ConstantValueOpt : Optional<object>.None;

                result = new BoundMemberAccessExpression(
                    id,
                    receiverOpt: null,
                    member: field,
                    type: fieldValueType,
                    isLValue: canWriteField,
                    constantValueOpt: cv);
                return true;
            }

            bool canReadProperty =
                prop!.GetMethod is not null &&
                AccessibilityHelper.IsAccessible(prop.GetMethod, context);

            bool canWriteProperty =
                prop.SetMethod is not null &&
                AccessibilityHelper.IsAccessible(prop.SetMethod, context);

            if (valueKind == BindValueKind.RValue && !canReadProperty)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_MEMACC_USINGSTATIC001",
                    DiagnosticSeverity.Error,
                    $"Property '{prop.Name}' has no accessible getter.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));
                result = new BoundBadExpression(id);
                return true;
            }

            if (valueKind == BindValueKind.LValue && !canWriteProperty)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_MEMACC_USINGSTATIC002",
                    DiagnosticSeverity.Error,
                    $"Property '{prop.Name}' has no accessible setter.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));
                result = new BoundBadExpression(id);
                return true;
            }

            result = new BoundMemberAccessExpression(
                id,
                receiverOpt: null,
                member: prop,
                type: prop.Type,
                isLValue: canWriteProperty);
            return true;
        }
        private ImmutableArray<MethodSymbol> LookupImportedStaticMethods(string name, BindingContext context)
        {
            var imports = GetImports(context);
            if (imports.StaticTypes.IsDefaultOrEmpty)
                return ImmutableArray<MethodSymbol>.Empty;

            var b = ImmutableArray.CreateBuilder<MethodSymbol>();

            for (int i = 0; i < imports.StaticTypes.Length; i++)
            {
                var t = imports.StaticTypes[i];
                var methods = LookupMethods(t, name);
                if (methods.IsDefaultOrEmpty)
                    continue;

                for (int m = 0; m < methods.Length; m++)
                {
                    var ms = methods[m];
                    if (!ms.IsStatic)
                        continue;
                    if (!AccessibilityHelper.IsAccessible(ms, context))
                        continue;
                    b.Add(ms);
                }
            }

            return b.Count == 0 ? ImmutableArray<MethodSymbol>.Empty : b.ToImmutable();
        }
        public override ImmutableArray<Symbol> LookupSymbols(int position, string? name = null)
        {
            var builder = ImmutableArray.CreateBuilder<Symbol>();

            if (name == null)
            {
                foreach (var p in _parameters.Values) builder.Add(p);
                foreach (var l in _locals.Values) builder.Add(l);
                foreach (var f in _localFunctions.Values) builder.Add(f);
            }
            else
            {
                if (_parameters.TryGetValue(name, out var p)) builder.Add(p);
                if (_locals.TryGetValue(name, out var l)) builder.Add(l);
                if (_localFunctions.TryGetValue(name, out var f)) builder.Add(f);
            }

            if (Parent != null)
                builder.AddRange(Parent.LookupSymbols(position, name));

            return builder.ToImmutable();
        }
        internal void PredeclareLocalFunctionsInStatementList(
            ImmutableArray<StatementSyntax> statements,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            for (int i = 0; i < statements.Length; i++)
            {
                if (statements[i] is LocalFunctionStatementSyntax lf)
                    DeclareLocalFunction(lf, context, diagnostics);
            }
        }

        private void PredeclareLocalFunctionsInBlock(BlockSyntax block, BindingContext context, DiagnosticBag diagnostics)
        {
            var stmts = block.Statements;
            for (int i = 0; i < stmts.Count; i++)
            {
                if (stmts[i] is LocalFunctionStatementSyntax lf)
                    DeclareLocalFunction(lf, context, diagnostics);
            }
        }
        private BoundExpression LowerStackAllocToSpanCreation(
            ExpressionSyntax exprSyntax,
            BoundStackAllocArrayCreationExpression sa,
            NamedTypeSymbol spanLikeType,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (!TryFindSpanPointerCtor(spanLikeType, context.Compilation, out var ctor))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_STACKALLOC_SPAN001",
                    DiagnosticSeverity.Error,
                    $"Missing '{spanLikeType.Name}' constructor (void*, int).",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                var bad = new BoundBadExpression(exprSyntax);
                bad.SetType(spanLikeType);
                return bad;
            }

            var int32 = context.Compilation.GetSpecialType(SpecialType.System_Int32);

            var countTmp = NewTemp("$stackalloc_len", int32);
            var ptrTmp = NewTemp("$stackalloc_ptr", sa.Type);

            var countDecl = new BoundLocalDeclarationStatement(diagnosticNode, countTmp, sa.Count);
            var countExpr = new BoundLocalExpression(diagnosticNode, countTmp);

            var sa2 = new BoundStackAllocArrayCreationExpression(
                sa.Syntax,
                (PointerTypeSymbol)sa.Type,
                sa.ElementType,
                countExpr,
                sa.InitializerOpt);

            var ptrDecl = new BoundLocalDeclarationStatement(diagnosticNode, ptrTmp, sa2);
            var ptrExpr = new BoundLocalExpression(diagnosticNode, ptrTmp);

            var arg0 = ApplyConversion(exprSyntax, ptrExpr, ctor.Parameters[0].Type, diagnosticNode, context, diagnostics, requireImplicit: true);
            var arg1 = ApplyConversion(exprSyntax, countExpr, ctor.Parameters[1].Type, diagnosticNode, context, diagnostics, requireImplicit: true);

            var created = new BoundObjectCreationExpression(exprSyntax, spanLikeType, ctor, ImmutableArray.Create(arg0, arg1));

            return new BoundSequenceExpression(
                diagnosticNode,
                locals: ImmutableArray.Create(countTmp, ptrTmp),
                sideEffects: ImmutableArray.Create<BoundStatement>(countDecl, ptrDecl),
                value: created);
        }
        private static bool TryGetSystemSpanDefinition(Compilation compilation, out NamedTypeSymbol spanDef)
        {
            spanDef = null!;

            NamespaceSymbol? system = null;
            var nss = compilation.GlobalNamespace.GetNamespaceMembers();
            for (int i = 0; i < nss.Length; i++)
            {
                if (string.Equals(nss[i].Name, "System", StringComparison.Ordinal))
                {
                    system = nss[i];
                    break;
                }
            }
            if (system is null)
                return false;

            var spans = system.GetTypeMembers("Span", arity: 1);
            if (spans.IsDefaultOrEmpty)
                return false;

            spanDef = spans[0].OriginalDefinition;
            return true;
        }
        private static bool TryGetSpanLikeElementType(TypeSymbol type, out NamedTypeSymbol spanLikeType, out TypeSymbol elementType)
        {
            spanLikeType = null!;
            elementType = null!;

            if (type is not NamedTypeSymbol nt) return false;

            var def = nt.OriginalDefinition;
            if (def.Arity != 1) return false;

            bool isSpan = string.Equals(def.Name, "Span", StringComparison.Ordinal);
            bool isReadOnlySpan = string.Equals(def.Name, "ReadOnlySpan", StringComparison.Ordinal);
            if (!isSpan && !isReadOnlySpan) return false;

            if (def.ContainingSymbol is not NamespaceSymbol ns || !string.Equals(ns.Name, "System", StringComparison.Ordinal))
                return false;

            spanLikeType = nt;
            var args = nt.TypeArguments;
            elementType = args.Length == 1 ? args[0] : def.TypeParameters[0];
            return true;
        }
        private static bool TryFindSpanPointerCtor(NamedTypeSymbol spanLikeType, Compilation compilation, out MethodSymbol ctor)
        {
            ctor = null!;
            var int32 = compilation.GetSpecialType(SpecialType.System_Int32);
            var voidType = compilation.GetSpecialType(SpecialType.System_Void);
            var voidPtr = compilation.CreatePointerType(voidType);

            foreach (var m in spanLikeType.GetMembers())
            {
                if (m is not MethodSymbol ms || !ms.IsConstructor || ms.IsStatic) continue;
                if (ms.Parameters.Length != 2) continue;
                if (ReferenceEquals(ms.Parameters[0].Type, voidPtr) && ReferenceEquals(ms.Parameters[1].Type, int32))
                {
                    ctor = ms;
                    return true;
                }
            }
            return false;
        }
        private LocalFunctionSymbol DeclareLocalFunction(
            LocalFunctionStatementSyntax lf,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var tree = context.SemanticModel.SyntaxTree;
            var name = lf.Identifier.ValueText ?? "";

            if (name.Length == 0)
            {
                diagnostics.Add(new Diagnostic("CN_LFUNC000", DiagnosticSeverity.Error,
                    "Local function name is missing.",
                    new Location(tree, lf.Span)));
                name = "error";
            }
            if (_localFunctions.ContainsKey(name))
            {
                diagnostics.Add(new Diagnostic("CN_LFUNC001", DiagnosticSeverity.Error,
                    $"A local function named '{name}' is already defined in this scope.",
                    new Location(tree, lf.Identifier.Span)));
                return _localFunctions[name];
            }
            if (IsNameDeclaredInEnclosingScopes(name))
            {
                diagnostics.Add(new Diagnostic("CN_LFUNC002", DiagnosticSeverity.Error,
                    $"Cannot declare local function '{name}' because that name is used in an enclosing local scope.",
                    new Location(tree, lf.Identifier.Span)));
            }
            for (int i = 0; i < lf.Modifiers.Count; i++)
            {
                var k = lf.Modifiers[i].Kind;
                if (k != SyntaxKind.StaticKeyword && k != SyntaxKind.AsyncKeyword && k != SyntaxKind.UnsafeKeyword)
                {
                    diagnostics.Add(new Diagnostic("CN_LFUNC003", DiagnosticSeverity.Error,
                        $"Modifier '{lf.Modifiers[i].ValueText}' is not valid on a local function.",
                        new Location(tree, lf.Modifiers[i].Span)));
                }
            }

            if (lf.TypeParameterList != null)
            {
                diagnostics.Add(new Diagnostic("CN_LFUNC004", DiagnosticSeverity.Error,
                    "Generic local functions are not supported.",
                    new Location(tree, lf.TypeParameterList.Span)));
            }

            var isStatic =
                HasModifier(lf.Modifiers, SyntaxKind.StaticKeyword) ||
                (_containing is MethodSymbol containingMethod && containingMethod.IsStatic);
            var isAsync = HasModifier(lf.Modifiers, SyntaxKind.AsyncKeyword);
            var isUnsafe = HasModifier(lf.Modifiers, SyntaxKind.UnsafeKeyword);

            var locations = ImmutableArray.Create(new Location(tree, lf.Identifier.Span));
            var sym = new LocalFunctionSymbol(name, _containing, lf, tree, locations, isStatic, isAsync);

            _localFunctions.Add(name, sym);
            context.Recorder.RecordDeclared(lf, sym);
            var sigBinder =
                isUnsafe && (Flags & BinderFlags.UnsafeRegion) == 0
                ? new LocalScopeBinder(parent: this, flags: Flags | BinderFlags.UnsafeRegion, containing: _containing)
                : this;

            var returnType = sigBinder.BindType(lf.ReturnType, context, diagnostics);

            var pars = lf.ParameterList.Parameters;
            var pb = ImmutableArray.CreateBuilder<ParameterSymbol>(pars.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < pars.Count; i++)
            {
                var p = pars[i];
                var pn = p.Identifier.ValueText ?? "";
                if (!seen.Add(pn))
                {
                    diagnostics.Add(new Diagnostic("CN_LFUNC005", DiagnosticSeverity.Error,
                        $"Duplicate parameter name '{pn}'.",
                        new Location(tree, p.Identifier.Span)));
                }

                TypeSymbol pt;
                if (p.Type != null)
                    pt = sigBinder.BindType(p.Type, context, diagnostics);
                else
                {
                    diagnostics.Add(new Diagnostic("CN_LFUNC006", DiagnosticSeverity.Error,
                        "Parameter type is required for local functions.",
                        new Location(tree, p.Span)));
                    pt = new ErrorTypeSymbol("error", containing: null, locations: ImmutableArray<Location>.Empty);
                }
                var pRefKind = DeclarationBuilder.GetParameterRefKind(p);
                if (pRefKind != ParameterRefKind.None && pt is not ByRefTypeSymbol)
                    pt = context.Compilation.CreateByRefType(pt);
                pb.Add(new ParameterSymbol(
                    pn,
                    sym,
                    pt,
                    ImmutableArray.Create(new Location(tree, p.Identifier.Span)),
                    isReadOnlyRef: DeclarationBuilder.IsReadOnlyByRefParameter(p),
                    refKind: pRefKind,
                    isScoped: DeclarationBuilder.IsScopedParameter(p),
                    isParams: DeclarationBuilder.IsParamsParameter(p)));
            }

            sym.SetSignature(returnType, pb.ToImmutable());
            return sym;
        }
        public override Symbol? GetDeclaredSymbol(SyntaxNode declaration)
            => Parent?.GetDeclaredSymbol(declaration);

        public override BoundStatement BindStatement(StatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            BoundStatement result;
            switch (node)
            {
                case BlockSyntax block:
                    result = BindBlock(block, context, diagnostics);
                    break;

                case ExpressionStatementSyntax es:
                    {
                        BoundExpression expr;
                        if (es.Expression is ImplicitObjectCreationExpressionSyntax ioc)
                            expr = BindImplicitObjectCreation(ioc, context, diagnostics);
                        else
                            expr = BindDiscardedExpression(es.Expression, context, diagnostics);
                        result = new BoundExpressionStatement(es, expr);
                    }
                    break;

                case UnsafeStatementSyntax us:
                    {
                        var unsafeBinder = (Flags & BinderFlags.UnsafeRegion) != 0
                            ? this
                            : WithFlags(Flags | BinderFlags.UnsafeRegion);

                        var inner = unsafeBinder.BindStatement(us.Block, context, diagnostics);

                        if (inner is BoundBlockStatement b)
                            result = new BoundBlockStatement(us, b.Statements);
                        else
                            result = inner;
                    }
                    break;

                case LocalDeclarationStatementSyntax ld:
                    result = BindLocalDeclaration(ld, context, diagnostics);
                    break;

                case ReturnStatementSyntax rs:
                    result = BindReturn(rs, context, diagnostics);
                    break;

                case ThrowStatementSyntax th:
                    result = BindThrow(th, context, diagnostics);
                    break;

                case BreakStatementSyntax @break:
                    result = BindBreak(@break, context, diagnostics);
                    break;

                case ContinueStatementSyntax @continue:
                    result = BindContinue(@continue, context, diagnostics);
                    break;

                case EmptyStatementSyntax empty:
                    result = new BoundEmptyStatement(empty);
                    break;
                case IfStatementSyntax @if:
                    result = BindIf(@if, context, diagnostics);
                    break;
                case WhileStatementSyntax @while:
                    result = BindWhile(@while, context, diagnostics);
                    break;
                case DoStatementSyntax @do:
                    result = BindDoWhile(@do, context, diagnostics);
                    break;
                case TryStatementSyntax @try:
                    result = BindTry(@try, context, diagnostics);
                    break;
                case ForStatementSyntax @for:
                    result = BindFor(@for, context, diagnostics);
                    break;
                case GotoStatementSyntax @goto:
                    result = BindGoto(@goto, context, diagnostics);
                    break;
                case LabeledStatementSyntax labeled:
                    result = BindLabeledStatement(labeled, context, diagnostics);
                    break;
                case LocalFunctionStatementSyntax lf:
                    result = BindLocalFunctionStatement(lf, context, diagnostics);
                    break;
                case CheckedStatementSyntax chk:
                    result = BindCheckedStatement(chk, context, diagnostics);
                    break;
                case SwitchStatementSyntax sw:
                    result = BindSwitchStatement(sw, context, diagnostics);
                    break;

                default:
                    if (Parent != null)
                        result = Parent.BindStatement(node, context, diagnostics);
                    else
                    {
                        diagnostics.Add(new Diagnostic("CN_BIND001", DiagnosticSeverity.Error,
                            $"Statement not supported: {node.Kind}",
                            new Location(context.SemanticModel.SyntaxTree, node.Span)));
                        result = new BoundBadStatement(node);
                    }
                    break;
            }

            return Record(node, result, context);
        }
        public override BoundExpression BindExpression(ExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            BoundExpression result;

            switch (node)
            {
                case LiteralExpressionSyntax lit:
                    result = BindLiteral(lit, context, diagnostics);
                    break;
                case InterpolatedStringExpressionSyntax isx:
                    result = BindInterpolatedString(isx, context, diagnostics);
                    break;
                case IdentifierNameSyntax id:
                    result = BindIdentifier(id, context, diagnostics);
                    break;
                case MemberAccessExpressionSyntax ma:
                    result = BindMemberAccess(ma, BindValueKind.RValue, context, diagnostics);
                    break;
                case CastExpressionSyntax cast:
                    {
                        var targetType = BindType(cast.Type, context, diagnostics);
                        var operand = BindExpression(cast.Expression, context, diagnostics);
                        result = ApplyConversion(
                            exprSyntax: cast,
                            expr: operand,
                            targetType: targetType,
                            diagnosticNode: cast,
                            context: context,
                            diagnostics: diagnostics,
                            requireImplicit: false);

                        break;
                    }
                case TupleExpressionSyntax te:
                    result = BindTupleExpression(te, context, diagnostics);
                    break;
                case ThisExpressionSyntax @this:
                    result = BindThis(@this, context, diagnostics);
                    break;
                case ParenthesizedExpressionSyntax paren:
                    result = BindExpression(paren.Expression, context, diagnostics);
                    break;
                case PrefixUnaryExpressionSyntax pre:
                    result = BindPrefixUnary(pre, context, diagnostics);
                    break;
                case PostfixUnaryExpressionSyntax post:
                    result = BindPostfixUnary(post, context, diagnostics);
                    break;
                case BinaryExpressionSyntax bin:
                    result = BindBinary(bin, context, diagnostics);
                    break;
                case ConditionalExpressionSyntax cond:
                    result = BindConditional(cond, context, diagnostics);
                    break;
                case AssignmentExpressionSyntax assign:
                    result = BindAssignment(assign, context, diagnostics);
                    break;
                case InvocationExpressionSyntax inv:
                    result = BindInvocation(inv, context, diagnostics);
                    break;
                case ImplicitObjectCreationExpressionSyntax ioc:
                    result = BindUnboundImplicitObjectCreation(ioc, context, diagnostics);
                    break;
                case ObjectCreationExpressionSyntax oc:
                    result = BindObjectCreation(oc, context, diagnostics);
                    break;
                case ArrayCreationExpressionSyntax ac:
                    result = BindArrayCreation(ac, context, diagnostics);
                    break;
                case ImplicitArrayCreationExpressionSyntax iac:
                    result = BindImplicitArrayCreation(iac, context, diagnostics);
                    break;
                case StackAllocArrayCreationExpressionSyntax sa:
                    result = BindStackAlloc(sa, context, diagnostics);
                    break;
                case ImplicitStackAllocArrayCreationExpressionSyntax isa:
                    result = BindImplicitStackAlloc(isa, context, diagnostics);
                    break;
                case ElementAccessExpressionSyntax ea:
                    result = BindElementAccess(ea, BindValueKind.RValue, context, diagnostics);
                    break;
                case CheckedExpressionSyntax chk:
                    result = BindCheckedExpression(chk, context, diagnostics);
                    break;
                case SizeOfExpressionSyntax sz:
                    result = BindSizeOf(sz, context, diagnostics);
                    break;
                case DefaultExpressionSyntax def:
                    result = BindDefaultExpression(def, context, diagnostics);
                    break;
                case ThrowExpressionSyntax te:
                    result = BindThrowExpression(te, context, diagnostics);
                    break;
                case IsPatternExpressionSyntax ip:
                    result = BindIsPatternExpression(ip, context, diagnostics);
                    break;
                case SwitchExpressionSyntax sw:
                    result = BindSwitchExpression(sw, context, diagnostics);
                    break;
                case RefExpressionSyntax re:
                    result = BindRefExpression(re, context, diagnostics);
                    break;

                case RangeExpressionSyntax r:
                    diagnostics.Add(new Diagnostic(
                        "CN_SLICE012",
                        DiagnosticSeverity.Error,
                        "Range expressions are only supported inside element access expressions.",
                        new Location(context.SemanticModel.SyntaxTree, r.Span)));
                    result = new BoundBadExpression(r);
                    break;
                default:
                    if (Parent != null)
                        result = Parent.BindExpression(node, context, diagnostics);
                    else
                    {
                        diagnostics.Add(new Diagnostic("CN_BIND002", DiagnosticSeverity.Error,
                            $"Expression not supported: {node.Kind}",
                            new Location(context.SemanticModel.SyntaxTree, node.Span)));
                        result = new BoundBadExpression(node);
                    }
                    break;
            }

            return Record(node, result, context);
        }
        private BoundExpression BindDefaultExpression(
            DefaultExpressionSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var type = BindType(node.Type, context, diagnostics);
            if (type is ErrorTypeSymbol)
                return new BoundBadExpression(node);

            if (type.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_DEF001",
                    DiagnosticSeverity.Error,
                    "The default value of 'void' is not valid.",
                    new Location(context.SemanticModel.SyntaxTree, node.Type.Span)));
                return new BoundBadExpression(node);
            }

            if (type is ByRefTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_DEF002",
                    DiagnosticSeverity.Error,
                    "The default value of a byref type is not valid.",
                    new Location(context.SemanticModel.SyntaxTree, node.Type.Span)));
                return new BoundBadExpression(node);
            }

            return MakeDefaultValue(node, type);
        }
        private BoundExpression BindSizeOf(SizeOfExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var operandType = BindType(node.Type, context, diagnostics);
            if (operandType is ErrorTypeSymbol)
                return new BoundBadExpression(node);

            if (operandType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_SIZEOF000",
                    DiagnosticSeverity.Error,
                    "The sizeof operator cannot be applied to 'void'.",
                    new Location(context.SemanticModel.SyntaxTree, node.Type.Span)));

                return new BoundBadExpression(node);
            }

            if (operandType is ByRefTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_SIZEOF001",
                    DiagnosticSeverity.Error,
                    "The sizeof operator cannot be applied to a by-ref type.",
                    new Location(context.SemanticModel.SyntaxTree, node.Type.Span)));

                return new BoundBadExpression(node);
            }

            var intType = context.Compilation.GetSpecialType(SpecialType.System_Int32);

            if (TryGetCompileTimeSizeOf(operandType, out int size))
                return new BoundLiteralExpression(node, intType, size);

            return new BoundSizeOfExpression(node, intType, operandType);
        }
        private static bool TryGetCompileTimeSizeOf(TypeSymbol type, out int size)
        {
            var visiting = new HashSet<TypeSymbol>();
            return TryGetStorageSizeAlign(type, visiting, out size, out _);
        }
        private static bool TryGetStorageSizeAlign(
            TypeSymbol type,
            HashSet<TypeSymbol> visiting,
            out int size,
            out int align)
        {
            size = 0;
            align = 1;

            if (type is null || type is ErrorTypeSymbol)
                return false;

            if (type.IsReferenceType || type is ArrayTypeSymbol || type is PointerTypeSymbol || type is ByRefTypeSymbol)
            {
                size = RuntimeTypeSystem.PointerSize;
                align = RuntimeTypeSystem.PointerSize;
                return true;
            }

            if (type is TypeParameterSymbol)
                return false;

            if (TryGetKnownPrimitiveOrBuiltinSizeAlign(type, out size, out align))
                return true;

            if (type is NamedTypeSymbol nt)
            {
                if (nt.TypeKind == TypeKind.Enum)
                {
                    var ut = nt.EnumUnderlyingType;
                    if (ut is null)
                        return false;

                    return TryGetStorageSizeAlign(ut, visiting, out size, out align);
                }

                if (nt.TypeKind == TypeKind.Struct)
                {
                    if (!visiting.Add(nt))
                        return false; // recursive cycle guard

                    try
                    {
                        int offset = 0;
                        int maxAlign = 1;

                        var members = nt.GetMembers();
                        for (int i = 0; i < members.Length; i++)
                        {
                            if (members[i] is not FieldSymbol f || f.IsStatic)
                                continue;

                            if (!TryGetStorageSizeAlign(f.Type, visiting, out int fs, out int fa))
                                return false;

                            offset = AlignUp(offset, fa);
                            offset += fs;
                            if (fa > maxAlign)
                                maxAlign = fa;
                        }

                        size = AlignUp(offset, maxAlign);
                        if (size == 0)
                            size = 1; // empty struct behavior

                        align = maxAlign;
                        return true;
                    }
                    finally
                    {
                        visiting.Remove(nt);
                    }
                }
            }
            return false;
        }
        private static bool TryGetKnownPrimitiveOrBuiltinSizeAlign(TypeSymbol type, out int size, out int align)
        {
            size = 0;
            align = 1;

            switch (type.SpecialType)
            {
                case SpecialType.System_Void:
                    size = 0; align = 1; return true;

                case SpecialType.System_Boolean:
                case SpecialType.System_Int8:
                case SpecialType.System_UInt8:
                    size = 1; align = 1; return true;

                case SpecialType.System_Char:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                    size = 2; align = 2; return true;

                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Single:
                    size = 4; align = 4; return true;

                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Double:
                    size = 8; align = 8; return true;

                case SpecialType.System_Decimal:
                    size = 16; align = 8; return true;
                case SpecialType.System_IntPtr:
                case SpecialType.System_UIntPtr:
                    size = RuntimeTypeSystem.PointerSize;
                    align = RuntimeTypeSystem.PointerSize;
                    return true;
            }

            return false;
        }
        private static int AlignUp(int value, int align)
        {
            int mask = align - 1;
            return (value + mask) & ~mask;
        }
        private BoundExpression BindThrowExpression(ThrowExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var exType = GetSystemExceptionTypeOrReport(node, context, diagnostics);
            if (exType.Kind == SymbolKind.Error)
                return new BoundBadExpression(node);

            var expr = BindExpression(node.Expression, context, diagnostics);
            expr = ApplyConversion(
                exprSyntax: node.Expression,
                expr: expr,
                targetType: exType,
                diagnosticNode: node,
                context: context,
                diagnostics: diagnostics,
                requireImplicit: true);

            return new BoundThrowExpression(node, expr);
        }
        private BoundExpression BindCheckedExpression(CheckedExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            bool isChecked = node.Keyword.Kind == SyntaxKind.CheckedKeyword;
            var flags = ApplyCheckedContext(Flags, isChecked);
            var binder = WithFlags(flags);

            var expr = binder.BindExpression(node.Expression, context, diagnostics);
            return isChecked
                ? new BoundCheckedExpression(node, expr)
                : new BoundUncheckedExpression(node, expr);
        }
        private BoundStatement BindCheckedStatement(CheckedStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            bool isChecked = node.Keyword.Kind == SyntaxKind.CheckedKeyword;
            var flags = ApplyCheckedContext(Flags, isChecked);
            var binder = WithFlags(flags);

            var statement = binder.BindStatement(node.Block, context, diagnostics);
            return isChecked
                ? new BoundCheckedStatement(node, statement)
                : new BoundUncheckedStatement(node, statement);
        }
        private BoundExpression BindIsPatternExpression(IsPatternExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var operand = BindExpression(node.Expression, context, diagnostics);
            if (operand.HasErrors)
                return new BoundBadExpression(node);
            return BindIsPatternCore(node, operand, node.Pattern, context, diagnostics);
        }
        private BoundExpression BindIsPatternCore(
            SyntaxNode wholeSyntax,
            BoundExpression operand,
            PatternSyntax pattern,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            switch (pattern)
            {
                case ParenthesizedPatternSyntax paren:
                    return BindIsPatternCore(wholeSyntax, operand, paren.Pattern, context, diagnostics);

                case TypePatternSyntax typePattern:
                    {
                        var patternType = BindType(typePattern.Type, context, diagnostics);
                        return BindIsTypePattern(
                            wholeSyntax,
                            operand,
                            patternType,
                            declaredLocalOpt: null,
                            isDiscard: false,
                            diagnosticNode: typePattern,
                            context,
                            diagnostics);
                    }

                case DeclarationPatternSyntax declPattern:
                    {
                        var patternType = BindType(declPattern.Type, context, diagnostics);
                        if (patternType is ErrorTypeSymbol)
                            return new BoundBadExpression(wholeSyntax);

                        if (!TryBindPatternDesignation(
                            declPattern.Designation,
                            patternType,
                            context,
                            diagnostics,
                            out var local,
                            out var isDiscard))
                        {
                            return new BoundBadExpression(wholeSyntax);
                        }

                        return BindIsTypePattern(
                            wholeSyntax,
                            operand,
                            patternType,
                            local,
                            isDiscard,
                            declPattern,
                            context,
                            diagnostics);
                    }

                case ConstantPatternSyntax:
                    diagnostics.Add(new Diagnostic(
                    "CN_PAT_IS001",
                    DiagnosticSeverity.Error,
                    "Constant patterns in 'is' expressions are not supported yet.",
                    new Location(context.SemanticModel.SyntaxTree, pattern.Span)));
                    return new BoundBadExpression(wholeSyntax);

                case VarPatternSyntax:
                    diagnostics.Add(new Diagnostic(
                    "CN_PAT_IS002",
                    DiagnosticSeverity.Error,
                    "'var' patterns are not supported yet.",
                    new Location(context.SemanticModel.SyntaxTree, pattern.Span)));
                    return new BoundBadExpression(wholeSyntax);

                case DiscardPatternSyntax:
                    diagnostics.Add(new Diagnostic(
                    "CN_PAT_IS003",
                    DiagnosticSeverity.Error,
                    "Discard-only patterns in 'is' expressions are not supported yet.",
                    new Location(context.SemanticModel.SyntaxTree, pattern.Span)));
                    return new BoundBadExpression(wholeSyntax);

                default:
                    diagnostics.Add(new Diagnostic(
                    "CN_PAT_IS004",
                    DiagnosticSeverity.Error,
                    $"Pattern '{pattern.Kind}' is not supported in 'is' expressions.",
                    new Location(context.SemanticModel.SyntaxTree, pattern.Span)));
                    return new BoundBadExpression(wholeSyntax);
            }
        }
        private bool TryBindPatternDesignation(
            VariableDesignationSyntax designation,
            TypeSymbol localType,
            BindingContext context,
            DiagnosticBag diagnostics,
            out LocalSymbol? local,
            out bool isDiscard)
        {
            switch (designation)
            {
                case DiscardDesignationSyntax:
                    local = null;
                    isDiscard = true;
                    return true;

                case SingleVariableDesignationSyntax single:
                    {
                        var name = single.Identifier.ValueText ?? string.Empty;
                        if (name.Length == 0)
                        {
                            local = null;
                            isDiscard = false;
                            return false;
                        }

                        if (IsNameDeclaredInEnclosingScopes(name))
                        {
                            diagnostics.Add(new Diagnostic(
                                "CN_PAT_IS005",
                                DiagnosticSeverity.Error,
                                $"A local named '{name}' is already declared in this scope.",
                                new Location(context.SemanticModel.SyntaxTree, single.Span)));
                        }

                        local = new LocalSymbol(
                            name: name,
                            containing: _containing,
                            type: localType,
                            locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, single.Span)),
                            isConst: false,
                            constantValueOpt: Optional<object>.None,
                            isByRef: false);

                        context.Recorder.RecordDeclared(single, local);
                        isDiscard = false;
                        return true;
                    }

                default:
                    diagnostics.Add(new Diagnostic(
                        "CN_PAT_IS006",
                        DiagnosticSeverity.Error,
                        "Only single-variable and discard designations are supported in patterns.",
                        new Location(context.SemanticModel.SyntaxTree, designation.Span)));
                    local = null;
                    isDiscard = false;
                    return false;
            }
        }
        private BoundExpression BindIsTypePattern(
            SyntaxNode wholeSyntax,
            BoundExpression operand,
            TypeSymbol patternType,
            LocalSymbol? declaredLocalOpt,
            bool isDiscard,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (patternType is ErrorTypeSymbol)
                return new BoundBadExpression(wholeSyntax);

            if (patternType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_PAT_IS007",
                    DiagnosticSeverity.Error,
                    "Pattern type cannot be 'void'.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                return new BoundBadExpression(wholeSyntax);
            }

            if (patternType is PointerTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_PAT_IS008",
                    DiagnosticSeverity.Error,
                    "Pattern type cannot be a pointer type.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                return new BoundBadExpression(wholeSyntax);
            }

            if (patternType.IsRefLikeType)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_PAT_IS009",
                    DiagnosticSeverity.Error,
                    "Pattern matching against ref-like types is not supported.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                return new BoundBadExpression(wholeSyntax);
            }

            var conversion = ClassifyConversion(operand, patternType, context);
            if (!IsConversionOfIsTypePattern(conversion))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_PAT_IS010",
                    DiagnosticSeverity.Error,
                    $"Cannot test expression of type '{operand.Type.Name}' against pattern type '{patternType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                return new BoundBadExpression(wholeSyntax);
            }

            var boolType = context.Compilation.GetSpecialType(SpecialType.System_Boolean);
            return new BoundIsPatternExpression(wholeSyntax, operand, patternType, declaredLocalOpt, boolType, isDiscard);

        }
        private static bool IsConversionOfIsTypePattern(Conversion conversion)
        {
            return conversion.Kind is ConversionKind.Identity
                or ConversionKind.ImplicitReference
                or ConversionKind.ExplicitReference
                or ConversionKind.Boxing
                or ConversionKind.Unboxing
                or ConversionKind.NullLiteral;
        }
        private BoundExpression BindTupleExpression(TupleExpressionSyntax te, BindingContext context, DiagnosticBag diagnostics)
        {
            var args = te.Arguments;
            if (args.Count < 2)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TUPEXPR000",
                    DiagnosticSeverity.Error,
                    "A tuple expression must contain at least two elements.",
                    new Location(context.SemanticModel.SyntaxTree, te.Span)));
                return new BoundBadExpression(te);
            }

            var elements = ImmutableArray.CreateBuilder<BoundExpression>(args.Count);
            var names = ImmutableArray.CreateBuilder<string?>(args.Count);
            var types = ImmutableArray.CreateBuilder<TypeSymbol>(args.Count);

            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            bool hasErrors = false;

            for (int i = 0; i < args.Count; i++)
            {
                var a = args[i];
                var e = BindExpression(a.Expression, context, diagnostics);

                elements.Add(e);
                types.Add(e.Type);

                if (e.Type.SpecialType == SpecialType.System_Void)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_TUPEXPR001",
                        DiagnosticSeverity.Error,
                        "Tuple elements may not be of type 'void'.",
                        new Location(context.SemanticModel.SyntaxTree, a.Expression.Span)));
                    hasErrors = true;
                }

                string? name = null;
                if (a.NameColon != null)
                    name = a.NameColon.Name.Identifier.ValueText;
                else
                    name = TryInferTupleElementName(a.Expression);

                if (!string.IsNullOrEmpty(name))
                {
                    if (!seenNames.Add(name!))
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_TUPNAME000",
                            DiagnosticSeverity.Error,
                            $"Tuple element name '{name}' is a duplicate.",
                            new Location(context.SemanticModel.SyntaxTree, (a.NameColon?.Span ?? a.Expression.Span))));
                        name = null;
                        hasErrors = true;
                    }
                }

                names.Add(name);
            }
            var tupleType = context.Compilation.CreateTupleType(types.ToImmutable(), names.ToImmutable());
            return new BoundTupleExpression(te, tupleType, elements.ToImmutable(), names.ToImmutable(), hasErrors);
            static string? TryInferTupleElementName(ExpressionSyntax expr)
            {
                return expr switch
                {
                    IdentifierNameSyntax id => id.Identifier.ValueText,
                    MemberAccessExpressionSyntax ma => GetSimpleName(ma.Name),
                    _ => null
                };
            }

        }
        private static bool TupleHasUninferableElements(TupleTypeSymbol tupleType)
        {
            var elems = tupleType.ElementTypes;
            for (int i = 0; i < elems.Length; i++)
            {
                var t = elems[i];
                if (t is NullTypeSymbol)
                    return true;
                if (t.SpecialType == SpecialType.System_Void)
                    return true;
                if (t is ErrorTypeSymbol)
                    return true;
            }
            return false;
        }
        private LValue BindLValue(
            ExpressionSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics,
            bool requireReadable = false)
        {
            var target = BindAssignableValue(node, context, diagnostics);

            if (!requireReadable || target.HasErrors)
                return new LValue(target, target);

            if (node is MemberAccessExpressionSyntax ma &&
                target is BoundMemberAccessExpression { Member: PropertySymbol })
            {
                var read = BindMemberAccess(ma, BindValueKind.RValue, context, diagnostics);
                return new LValue(target, read);
            }

            if (node is ElementAccessExpressionSyntax ea &&
                target is BoundIndexerAccessExpression)
            {
                var read = BindElementAccess(ea, BindValueKind.RValue, context, diagnostics);
                return new LValue(target, read);
            }

            return new LValue(target, target);
        }
        private bool TryBindSimpleValue(IdentifierNameSyntax id, out BoundExpression value)
        {
            var name = id.Identifier.ValueText ?? "";

            if (TryGetLocalFromEnclosingScopes(name, out var local))
            {
                value = new BoundLocalExpression(id, local!);
                return true;
            }

            if (TryGetParameterFromEnclosingScopes(name, out var param))
            {
                value = new BoundParameterExpression(id, param!);
                return true;
            }

            value = null!;
            return false;
        }

        private static string GetSimpleName(SimpleNameSyntax name)
        {
            return name switch
            {
                IdentifierNameSyntax id => id.Identifier.ValueText ?? "",
                GenericNameSyntax g => g.Identifier.ValueText ?? "",
                _ => ""
            };
        }
        private BoundExpression BindIdentifier(IdentifierNameSyntax id, BindingContext context, DiagnosticBag diagnostics)
        {
            var name = id.Identifier.ValueText ?? "";

            if (TryGetLocalFromEnclosingScopes(name, out var local))
                return new BoundLocalExpression(id, local!);

            if (TryGetParameterFromEnclosingScopes(name, out var param))
                return new BoundParameterExpression(id, param!);

            if (TryBindUnqualifiedMember(id, name, BindValueKind.RValue, context, diagnostics, out var memberExpr))
                return memberExpr;

            if (TryBindImportedStaticMember(id, name, BindValueKind.RValue, context, diagnostics, out var staticMemberExpr))
                return staticMemberExpr;

            diagnostics.Add(new Diagnostic("CN_BIND003", DiagnosticSeverity.Error,
                $"Use of undeclared identifier '{name}'.",
                new Location(context.SemanticModel.SyntaxTree, id.Span)));

            return new BoundBadExpression(id);
        }
        private static bool TryBindUnqualifiedMember(
            IdentifierNameSyntax id,
            string name,
            BindValueKind valueKind,
            BindingContext context,
            DiagnosticBag diagnostics,
            out BoundExpression result)
        {
            result = null!;

            NamedTypeSymbol? containingType = null;
            for (Symbol? s = context.ContainingSymbol; s != null; s = s.ContainingSymbol)
            {
                if (s is NamedTypeSymbol nt)
                {
                    containingType = nt;
                    break;
                }
            }
            if (containingType is null)
                return false;

            bool inStaticContext = context.ContainingSymbol switch
            {
                MethodSymbol m => m.IsStatic,
                FieldSymbol f => f.IsStatic,
                PropertySymbol p => p.IsStatic,
                _ => false
            };

            var members = LookupMembers(containingType, name);
            if (members.IsDefaultOrEmpty)
                return false;

            var accessibleMembers = FilterAccessibleMembers(members, context);
            if (accessibleMembers.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ACC001",
                    DiagnosticSeverity.Error,
                    $"Member '{name}' is inaccessible due to its protection level.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));

                result = new BoundBadExpression(id);
                return true;
            }

            members = accessibleMembers;

            FieldSymbol? field = null;
            PropertySymbol? prop = null;

            for (int i = 0; i < members.Length; i++)
            {
                switch (members[i])
                {
                    case FieldSymbol fs:
                        if (field is not null || prop is not null)
                        {
                            diagnostics.Add(new Diagnostic("CN_BIND_MEM001", DiagnosticSeverity.Error,
                                $"Member name '{name}' is ambiguous.",
                                new Location(context.SemanticModel.SyntaxTree, id.Span)));
                            result = new BoundBadExpression(id);
                            return true;
                        }
                        field = fs;
                        break;

                    case PropertySymbol ps:
                        if (prop is not null || field is not null)
                        {
                            diagnostics.Add(new Diagnostic("CN_BIND_MEM001", DiagnosticSeverity.Error,
                                $"Member name '{name}' is ambiguous.",
                                new Location(context.SemanticModel.SyntaxTree, id.Span)));
                            result = new BoundBadExpression(id);
                            return true;
                        }
                        prop = ps;
                        break;
                }
            }
            if (field is null && prop is null)
                return false;

            // field
            if (field is not null)
            {

                if (!field.IsStatic && inStaticContext)
                {
                    diagnostics.Add(new Diagnostic("CN_MEMACC011", DiagnosticSeverity.Error,
                        $"An object reference is required for the non-static field '{field.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, id.Span)));
                    result = new BoundBadExpression(id);
                    return true;
                }

                BoundExpression? receiver = field.IsStatic ? null : new BoundThisExpression(id, containingType);

                bool isRefField = field.Type is ByRefTypeSymbol;
                TypeSymbol fieldValueType = isRefField ? ((ByRefTypeSymbol)field.Type).ElementType : field.Type;
                if (valueKind == BindValueKind.LValue &&
                    !field.IsStatic &&
                    IsReadOnlyStructThisReceiver(receiver, context) &&
                    !isRefField)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_READONLY_THIS001",
                        DiagnosticSeverity.Error,
                        "Cannot assign to instance members of 'this' in a readonly struct instance member.",
                        new Location(context.SemanticModel.SyntaxTree, id.Span)));

                    result = new BoundBadExpression(id);
                    return true;
                }
                bool allowCtorReadonlyWrite =
                    valueKind == BindValueKind.LValue &&
                    field.IsReadOnly &&
                    CanAssignReadOnlyFieldInConstructor(field, receiver, context);

                bool canWriteField = !field.IsConst && (isRefField || !field.IsReadOnly || allowCtorReadonlyWrite);

                var cv = field.IsConst ? field.ConstantValueOpt : Optional<object>.None;
                result = new BoundMemberAccessExpression(id, receiver, field, fieldValueType, isLValue: canWriteField, constantValueOpt: cv);
                return true;
            }
            // property
            if (!prop!.IsStatic && inStaticContext)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC021", DiagnosticSeverity.Error,
                    $"An object reference is required for the non-static property '{prop.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));
                result = new BoundBadExpression(id);
                return true;
            }
            BoundExpression? propReceiver = prop.IsStatic ? null : new BoundThisExpression(id, containingType);
            bool canReadProperty =
                prop.GetMethod is not null &&
                AccessibilityHelper.IsAccessible(prop.GetMethod, context);

            bool canWriteProperty =
                prop.SetMethod is not null &&
                AccessibilityHelper.IsAccessible(prop.SetMethod, context);
            if (valueKind == BindValueKind.LValue &&
                !prop.IsStatic &&
                IsReadOnlyStructThisReceiver(propReceiver, context))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_READONLY_THIS002",
                    DiagnosticSeverity.Error,
                    "Cannot assign to instance properties of 'this' in a readonly struct instance member.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));

                result = new BoundBadExpression(id);
                return true;
            }
            if (valueKind == BindValueKind.RValue && !canReadProperty)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC020", DiagnosticSeverity.Error,
                    $"Property '{prop.Name}' has no accessible getter.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));
                result = new BoundBadExpression(id);
                return true;
            }

            bool allowCtorAutoPropWrite =
                valueKind == BindValueKind.LValue &&
                !canWriteProperty &&
                CanAssignReadOnlyAutoPropertyInConstructor(prop, propReceiver, context);

            if (valueKind == BindValueKind.LValue && !canWriteProperty && !allowCtorAutoPropWrite)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC021", DiagnosticSeverity.Error,
                    $"Property '{prop.Name}' has no accessible setter.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));
                result = new BoundBadExpression(id);
                return true;
            }

            result = new BoundMemberAccessExpression(
                id,
                propReceiver,
                prop,
                prop.Type,
                isLValue: canWriteProperty || allowCtorAutoPropWrite);

            return true;
        }
        private BoundExpression BindRefExpression(
            RefExpressionSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var operand = BindAssignableValue(node.Expression, context, diagnostics);
            if (operand.HasErrors)
                return operand;

            if (!operand.IsLValue)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REF000",
                    DiagnosticSeverity.Error,
                    "The operand of 'ref' must be an assignable variable.",
                    new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));

                return new BoundBadExpression(node);
            }

            if (operand is BoundLocalExpression bl && bl.Local.IsConst)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REF001",
                    DiagnosticSeverity.Error,
                    "Cannot create a ref to a const local.",
                    new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));

                return new BoundBadExpression(node);
            }

            if (operand is BoundMemberAccessExpression { Member: PropertySymbol } ||
                operand is BoundIndexerAccessExpression)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REF002",
                    DiagnosticSeverity.Error,
                    "A property cannot be used as a ref value here.",
                    new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));

                return new BoundBadExpression(node);
            }

            if (operand is BoundParameterExpression pe &&
                pe.Parameter.IsReadOnlyRef &&
                !IsInsideIntrinsicMethod(context))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REF003",
                    DiagnosticSeverity.Error,
                    "Cannot use a readonly by-ref parameter as a writable ref.",
                    new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));

                return new BoundBadExpression(node);
            }

            var refElementType = operand.Type is ByRefTypeSymbol br ? br.ElementType : operand.Type;
            var byRefType = context.Compilation.CreateByRefType(refElementType);
            return new BoundRefExpression(node, byRefType, operand);
        }

        private BoundExpression BindAddressOf(
            PrefixUnaryExpressionSyntax node, BoundExpression operand, BindingContext context, DiagnosticBag diagnostics)
        {
            EnsureUnsafe(node, context, diagnostics);

            if (!operand.IsLValue)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ADDR000",
                    DiagnosticSeverity.Error,
                    "The operand of '&' must be an assignable variable.",
                    new Location(context.SemanticModel.SyntaxTree, node.Operand.Span)));

                return new BoundBadExpression(node);
            }

            if (operand is BoundLocalExpression bl && bl.Local.IsConst)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ADDR001",
                    DiagnosticSeverity.Error,
                    "Cannot take the address of a const local.",
                    new Location(context.SemanticModel.SyntaxTree, node.Operand.Span)));

                var bad = new BoundBadExpression(node);
                bad.SetType(context.Compilation.CreatePointerType(operand.Type));
                return bad;
            }

            if (operand.Type.IsReferenceType || operand.Type is ArrayTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ADDR002",
                    DiagnosticSeverity.Error,
                    $"Cannot take the address of managed type '{operand.Type.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, node.Operand.Span)));

                var bad = new BoundBadExpression(node);
                bad.SetType(context.Compilation.CreatePointerType(operand.Type));
                return bad;
            }

            var ptrType = context.Compilation.CreatePointerType(operand.Type);
            return new BoundAddressOfExpression(node, ptrType, operand);
        }

        private BoundExpression BindPointerIndirection(
            PrefixUnaryExpressionSyntax node, BoundExpression operand, BindingContext context, DiagnosticBag diagnostics)
        {
            EnsureUnsafe(node, context, diagnostics);

            if (operand.Type is not PointerTypeSymbol pt)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_DEREF000",
                    DiagnosticSeverity.Error,
                    "The operand of '*' must be a pointer.",
                    new Location(context.SemanticModel.SyntaxTree, node.Operand.Span)));

                return new BoundBadExpression(node);
            }

            return new BoundPointerIndirectionExpression(node, pt.PointedAtType, operand);
        }
        private BoundExpression BindAssignment(AssignmentExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var lv = BindLValue(node.Left, context, diagnostics, requireReadable: node.Kind != SyntaxKind.SimpleAssignmentExpression);
            var leftTarget = lv.Target;

            var right = BindExpression(node.Right, context, diagnostics);

            bool hasErrors = leftTarget.HasErrors || right.HasErrors;

            // const local assignment check
            if (!leftTarget.HasErrors &&
                leftTarget is BoundLocalExpression le &&
                le.Local.IsConst)
            {
                diagnostics.Add(new Diagnostic("CN_ASG001", DiagnosticSeverity.Error,
                    "Cannot assign to a const local.",
                    new Location(context.SemanticModel.SyntaxTree, node.Left.Span)));
                hasErrors = true;
            }
            if (!leftTarget.HasErrors && IsReadOnlyByRefParameterTarget(leftTarget))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ASG_READONLYREF001",
                    DiagnosticSeverity.Error,
                    "Cannot assign to a readonly by-ref parameter.",
                    new Location(context.SemanticModel.SyntaxTree, node.Left.Span)));
                hasErrors = true;
            }
            // Simple assignment
            if (node.Kind == SyntaxKind.SimpleAssignmentExpression)
            {
                if (!leftTarget.HasErrors)
                {
                    TypeSymbol assignmentTargetType = leftTarget.Type;
                    // ref reassignment for ref
                    if (right is BoundRefExpression)
                    {
                        if (leftTarget is BoundMemberAccessExpression { Member: FieldSymbol field } &&
                            field.Type is ByRefTypeSymbol refFieldType)
                        {
                            assignmentTargetType = refFieldType;
                        }
                        else if (leftTarget is BoundLocalExpression refLocalExpr && refLocalExpr.Local.IsByRef)
                        {
                            assignmentTargetType = context.Compilation.CreateByRefType(refLocalExpr.Local.Type);
                        }
                    }
                    else
                    {
                        // value assignment through ref
                        if (leftTarget is BoundMemberAccessExpression { Member: FieldSymbol field } &&
                            field.Type is ByRefTypeSymbol refFieldType)
                        {
                            assignmentTargetType = refFieldType.ElementType;
                        }
                    }

                    right = ApplyConversion(
                        exprSyntax: node.Right,
                        expr: right,
                        targetType: assignmentTargetType,
                        diagnosticNode: node,
                        context: context,
                        diagnostics: diagnostics,
                        requireImplicit: true);

                    hasErrors |= right.HasErrors;
                }

                var asg = new BoundAssignmentExpression(node, leftTarget, right);
                if (hasErrors) asg.SetHasErrors();
                return asg;
            }
            // ??=
            if (node.Kind == SyntaxKind.CoalesceAssignmentExpression)
            {
                if (leftTarget.HasErrors || right.HasErrors)
                    return new BoundBadExpression(node);

                if (!leftTarget.Type.IsReferenceType && !TryGetSystemNullableInfo(leftTarget.Type, out _, out _))
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_COALESCEASG000",
                        DiagnosticSeverity.Error,
                        "The lhs of '??=' must be a reference type or a nullable value type.",
                        new Location(context.SemanticModel.SyntaxTree, node.Left.Span)));
                    return new BoundBadExpression(node);
                }
                // Convert rhs to lhs type
                var converted = ApplyConversion(
                    exprSyntax: node.Right,
                    expr: right,
                    targetType: leftTarget.Type,
                    diagnosticNode: node,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);

                if (converted.HasErrors)
                    return new BoundBadExpression(node);

                var asg = new BoundNullCoalescingAssignmentExpression(node, leftTarget, converted);
                if (hasErrors) asg.SetHasErrors();
                return asg;
            }
            // Compound assignment
            if (!TryMapCompoundAssignmentOperator(node.Kind, out var opKind))
            {
                diagnostics.Add(new Diagnostic("CN_ASG000", DiagnosticSeverity.Error,
                    $"Assignment form not supported: {node.Kind}",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            if (leftTarget.HasErrors || right.HasErrors)
                return new BoundBadExpression(node);

            var leftRead = lv.Read;

            if (IsDirectOperatorTarget(leftTarget) &&
                TryBindDirectCompoundAssignmentOperator(
                    node,
                    opKind,
                    leftTarget,
                    right,
                    context,
                    diagnostics,
                    out var directCall,
                    out var directMethod))
            {
                if (directCall.HasErrors)
                    return new BoundBadExpression(node);

                return new BoundCompoundAssignmentExpression(
                    node,
                    leftTarget,
                    opKind,
                    directCall,
                    operatorMethodOpt: directMethod,
                    usesDirectOperator: true,
                    isChecked: directMethod is not null &&
                               directMethod.Name.StartsWith("op_Checked", StringComparison.Ordinal));
            }

            var opResult = BindCompoundOperatorBinary(node, opKind, leftRead, right, context, diagnostics);
            if (opResult.HasErrors)
                return new BoundBadExpression(node);

            var valueToAssign = ApplyCompoundAssignmentConversion(
                syntaxForBoundNode: node,
                expr: opResult,
                targetType: leftTarget.Type,
                diagnosticNode: node,
                context: context,
                diagnostics: diagnostics);

            bool isChecked = opResult is BoundBinaryExpression compoundOp && compoundOp.IsChecked;
            return new BoundCompoundAssignmentExpression(
                node,
                leftTarget,
                opKind,
                valueToAssign,
                operatorMethodOpt: null,
                usesDirectOperator: false,
                isChecked: isChecked);
        }

        private static bool IsDirectOperatorTarget(BoundExpression target)
        {
            return target switch
            {
                BoundLocalExpression => true,
                BoundParameterExpression => true,
                BoundArrayElementAccessExpression => true,
                BoundPointerIndirectionExpression => true,
                BoundPointerElementAccessExpression => true,
                BoundMemberAccessExpression { Member: FieldSymbol } => true,
                BoundCallExpression call when call.IsLValue => true,
                _ => false
            };
        }
        private BoundExpression BindInterpolatedString(
            InterpolatedStringExpressionSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var stringType = context.Compilation.GetSpecialType(SpecialType.System_String);
            bool canBeConst = true;
            var constBuilder = new StringBuilder();

            var parts = new List<BoundExpression>(capacity: Math.Max(1, node.Contents.Count));

            for (int i = 0; i < node.Contents.Count; i++)
            {
                var c = node.Contents[i];

                if (c is InterpolatedStringTextSyntax text)
                {
                    var tok = text.TextToken;
                    var s = tok.Value as string ?? tok.ValueText ?? string.Empty;

                    if (canBeConst)
                        constBuilder.Append(s);

                    if (s.Length != 0)
                        parts.Add(new BoundLiteralExpression(text, stringType, s));

                    continue;
                }

                if (c is InterpolationSyntax interp)
                {
                    if (interp.AlignmentClause is not null)
                    {
                        diagnostics.Add(new Diagnostic(
                            id: "CN_INTERP_ALIGN000",
                            severity: DiagnosticSeverity.Error,
                            message: "Interpolation alignment is not supported.",
                            location: new Location(context.SemanticModel.SyntaxTree, interp.AlignmentClause.Span)));
                    }

                    if (interp.FormatClause is not null)
                    {
                        diagnostics.Add(new Diagnostic(
                            id: "CN_INTERP_FMT000",
                            severity: DiagnosticSeverity.Error,
                            message: "Interpolation format specifiers are not supported.",
                            location: new Location(context.SemanticModel.SyntaxTree, interp.FormatClause.Span)));
                    }

                    var expr = BindExpression(interp.Expression, context, diagnostics);

                    if (expr.Type.SpecialType == SpecialType.System_Void)
                    {
                        diagnostics.Add(new Diagnostic(
                            id: "CN_INTERP_VOID000",
                            severity: DiagnosticSeverity.Error,
                            message: "Cannot use an expression of type 'void' in an interpolated string.",
                            location: new Location(context.SemanticModel.SyntaxTree, interp.Expression.Span)));

                        expr = new BoundBadExpression(interp);
                    }

                    parts.Add(expr);

                    if (canBeConst)
                    {
                        if (interp.AlignmentClause is not null || interp.FormatClause is not null)
                        {
                            canBeConst = false;
                        }
                        else if (!expr.ConstantValueOpt.HasValue || expr.Type.SpecialType != SpecialType.System_String)
                        {
                            canBeConst = false;
                        }
                        else
                        {
                            constBuilder.Append((string?)expr.ConstantValueOpt.Value ?? string.Empty);
                        }
                    }

                    continue;
                }

                diagnostics.Add(new Diagnostic(
                    id: "CN_INTERP_UNK000",
                    severity: DiagnosticSeverity.Error,
                    message: $"Unsupported interpolated string content: {c.Kind}",
                    location: new Location(context.SemanticModel.SyntaxTree, c.Span)));
            }

            if (canBeConst)
                return new BoundLiteralExpression(node, stringType, constBuilder.ToString());

            if (parts.Count == 0)
                return new BoundLiteralExpression(node, stringType, string.Empty);

            BoundExpression acc = parts[0];
            for (int i = 1; i < parts.Count; i++)
            {
                acc = new BoundBinaryExpression(
                    node,
                    BoundBinaryOperatorKind.StringConcatenation,
                    stringType,
                    acc,
                    parts[i],
                    Optional<object>.None);
            }

            return acc;
        }
        private BoundExpression BindStringConcatenation(
            ExpressionSyntax syntax,
            BoundExpression left,
            BoundExpression right,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            var stringType = ctx.Compilation.GetSpecialType(SpecialType.System_String);

            if (left.Type.SpecialType == SpecialType.System_Void || right.Type.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_STRCONCAT001",
                    DiagnosticSeverity.Error,
                    "Operator '+' cannot be applied to operands of type 'void'.",
                    new Location(ctx.SemanticModel.SyntaxTree, syntax.Span)));
                return new BoundBadExpression(syntax);
            }

            if (left.Type is NullTypeSymbol || (left.ConstantValueOpt.HasValue && left.ConstantValueOpt.Value is null))
                left = ApplyConversion((ExpressionSyntax)left.Syntax, left, stringType, syntax, ctx, diagnostics, requireImplicit: true);

            if (right.Type is NullTypeSymbol || (right.ConstantValueOpt.HasValue && right.ConstantValueOpt.Value is null))
                right = ApplyConversion((ExpressionSyntax)right.Syntax, right, stringType, syntax, ctx, diagnostics, requireImplicit: true);

            if (left.HasErrors || right.HasErrors)
                return new BoundBadExpression(syntax);

            var cv = FoldStringConcatConstant(left, right);

            return new BoundBinaryExpression(
                syntax,
                BoundBinaryOperatorKind.StringConcatenation,
                stringType,
                left,
                right,
                cv);
        }
        private static Optional<object> FoldStringConcatConstant(BoundExpression left, BoundExpression right)
        {
            if (!left.ConstantValueOpt.HasValue || !right.ConstantValueOpt.HasValue)
                return Optional<object>.None;

            if (!TryConstToString(left, out var ls) || !TryConstToString(right, out var rs))
                return Optional<object>.None;

            return new Optional<object>(string.Concat(ls, rs));
            static bool TryConstToString(BoundExpression e, out string s)
            {
                var v = e.ConstantValueOpt.Value;

                if (v is null)
                {
                    s = "";
                    return true;
                }
                if (v is string str)
                {
                    s = str;
                    return true;
                }

                s = "";
                return false;
            }
        }
        private static bool TryMapCompoundAssignmentOperator(SyntaxKind assignmentKind, out BoundBinaryOperatorKind opKind)
        {
            switch (assignmentKind)
            {
                case SyntaxKind.AddAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.Add; return true;
                case SyntaxKind.SubtractAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.Subtract; return true;
                case SyntaxKind.MultiplyAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.Multiply; return true;
                case SyntaxKind.DivideAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.Divide; return true;
                case SyntaxKind.ModuloAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.Modulo; return true;

                case SyntaxKind.AndAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.BitwiseAnd; return true;
                case SyntaxKind.OrAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.BitwiseOr; return true;
                case SyntaxKind.ExclusiveOrAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.ExclusiveOr; return true;

                case SyntaxKind.LeftShiftAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.LeftShift; return true;
                case SyntaxKind.RightShiftAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.RightShift; return true;

                case SyntaxKind.UnsignedRightShiftAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.UnsignedRightShift; return true;

                default:
                    opKind = default;
                    return false;
            }
        }
        private BoundExpression BindCompoundOperatorBinary(
            AssignmentExpressionSyntax node,
            BoundBinaryOperatorKind op,
            BoundExpression left,
            BoundExpression right,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            if (TryBindUserDefinedCompoundAssignmentOperator(
                node: node,
                op: op,
                left: left,
                right: right,
                context: ctx,
                diagnostics: diagnostics,
                out var userDefined))
            {
                return userDefined;
            }
            switch (op)
            {
                case BoundBinaryOperatorKind.Add:
                case BoundBinaryOperatorKind.Subtract:
                case BoundBinaryOperatorKind.Multiply:
                case BoundBinaryOperatorKind.Divide:
                case BoundBinaryOperatorKind.Modulo:
                    return BindCompoundNumericBinary(node, op, left, right, ctx, diagnostics);

                case BoundBinaryOperatorKind.BitwiseAnd:
                case BoundBinaryOperatorKind.BitwiseOr:
                case BoundBinaryOperatorKind.ExclusiveOr:
                    return BindCompoundBitwiseBinary(node, op, left, right, ctx, diagnostics);

                case BoundBinaryOperatorKind.LeftShift:
                case BoundBinaryOperatorKind.RightShift:
                case BoundBinaryOperatorKind.UnsignedRightShift:
                    return BindCompoundShiftBinary(node, op, left, right, ctx, diagnostics);

                default:
                    diagnostics.Add(new Diagnostic("CN_ASG_OP000", DiagnosticSeverity.Error,
                        $"Compound assignment operator not supported: {op}",
                        new Location(ctx.SemanticModel.SyntaxTree, node.Span)));
                    return new BoundBadExpression(node);
            }
        }
        private BoundExpression BindCompoundNumericBinary(
            AssignmentExpressionSyntax node,
            BoundBinaryOperatorKind op,
            BoundExpression left,
            BoundExpression right,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            if (TryBindUserDefinedBinaryOperator(
                operatorSyntax: node,
                leftSyntax: node.Left,
                left: left,
                rightSyntax: node.Right,
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
                return BindStringConcatenation(node, left, right, ctx, diagnostics);
            }
            if (TryBindPointerArithmeticBinary(
                diagnosticNode: node,
                leftSyntax: node.Left,
                left: left,
                rightSyntax: node.Right,
                right: right,
                op: op,
                ctx: ctx,
                diagnostics: diagnostics,
                out var ptrArith))
            {
                return ptrArith;
            }

            var promoted = GetBinaryNumericPromotionType(ctx.Compilation, ctx.SemanticModel.SyntaxTree, left, right, node, diagnostics);
            if (promoted is null || promoted is ErrorTypeSymbol)
                return new BoundBadExpression(node);

            var leftConv = ApplyConversion(node.Left, left, promoted, node, ctx, diagnostics, requireImplicit: true);
            var rightConv = ApplyConversion(node.Right, right, promoted, node, ctx, diagnostics, requireImplicit: true);

            if (leftConv.HasErrors || rightConv.HasErrors)
                return new BoundBadExpression(node);

            bool isChecked = IsCheckedOverflowContext && IsOverflowCheckedBinaryOperator(op, promoted);
            var constValue = FoldBinaryConstant(op, promoted, leftConv, rightConv, isChecked);
            return new BoundBinaryExpression(node, op, promoted, leftConv, rightConv, constValue, isChecked);
        }
        private BoundExpression BindCompoundBitwiseBinary(
            AssignmentExpressionSyntax node,
            BoundBinaryOperatorKind op,
            BoundExpression left,
            BoundExpression right,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            if (TryBindUserDefinedBinaryOperator(
                operatorSyntax: node,
                leftSyntax: node.Left,
                left: left,
                rightSyntax: node.Right,
                right: right,
                op: op,
                context: ctx,
                diagnostics: diagnostics,
                out var userDefined))
            {
                return userDefined;
            }

            var boolType = ctx.Compilation.GetSpecialType(SpecialType.System_Boolean);

            // bool
            if (left.Type.SpecialType == SpecialType.System_Boolean &&
                right.Type.SpecialType == SpecialType.System_Boolean)
            {
                return new BoundBinaryExpression(node, op, boolType, left, right, Optional<object>.None);
            }
            // enum op enum
            if (IsEnumType(left.Type) && ReferenceEquals(left.Type, right.Type))
            {
                var underlying = GetEnumUnderlyingTypeOrDefault(ctx.Compilation, left.Type);
                var constValue = FoldBitwiseConstant(op, underlying.SpecialType, left, right);
                return new BoundBinaryExpression(node, op, left.Type, left, right, constValue);
            }
            // integral only
            if (!IsIntegral(left.Type.SpecialType) || !IsIntegral(right.Type.SpecialType))
            {
                diagnostics.Add(new Diagnostic("CN_BIT000", DiagnosticSeverity.Error,
                    $"Bitwise operator requires integral operands (or bool/bool), got '{left.Type.Name}' and '{right.Type.Name}'.",
                    new Location(ctx.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            var promoted = GetBinaryNumericPromotionType(ctx.Compilation, ctx.SemanticModel.SyntaxTree, left, right, node, diagnostics);
            if (promoted is null || promoted is ErrorTypeSymbol)
                return new BoundBadExpression(node);

            var leftConv = ApplyConversion(node.Left, left, promoted, node, ctx, diagnostics, requireImplicit: true);
            var rightConv = ApplyConversion(node.Right, right, promoted, node, ctx, diagnostics, requireImplicit: true);

            if (leftConv.HasErrors || rightConv.HasErrors)
                return new BoundBadExpression(node);

            return new BoundBinaryExpression(node, op, promoted, leftConv, rightConv, Optional<object>.None);
        }

        private BoundExpression BindCompoundShiftBinary(
            AssignmentExpressionSyntax node,
            BoundBinaryOperatorKind op,
            BoundExpression left,
            BoundExpression right,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            if (TryBindUserDefinedBinaryOperator(
                operatorSyntax: node,
                leftSyntax: node.Left,
                left: left,
                rightSyntax: node.Right,
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
                    new Location(ctx.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            var leftPromoted = GetUnaryPromotionType(
                ctx.Compilation,
                ctx.SemanticModel.SyntaxTree,
                BoundUnaryOperatorKind.UnaryPlus,
                left.Type,
                node,
                diagnostics);

            if (leftPromoted is null || leftPromoted is ErrorTypeSymbol)
                return new BoundBadExpression(node);

            var leftConv = ApplyConversion(node.Left, left, leftPromoted, node, ctx, diagnostics, requireImplicit: true);

            var intType = ctx.Compilation.GetSpecialType(SpecialType.System_Int32);
            var rightConv = ApplyConversion(node.Right, right, intType, node, ctx, diagnostics, requireImplicit: true);

            if (leftConv.HasErrors || rightConv.HasErrors)
                return new BoundBadExpression(node);

            // shift result type is leftPromoted
            return new BoundBinaryExpression(node, op, leftPromoted, leftConv, rightConv, Optional<object>.None);
        }
        private BoundExpression ApplyCompoundAssignmentConversion(
            ExpressionSyntax syntaxForBoundNode,
            BoundExpression expr,
            TypeSymbol targetType,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (ReferenceEquals(expr.Type, targetType))
                return expr;

            var conv = ClassifyConversion(expr, targetType, context);

            bool ok =
                conv.Exists &&
                (conv.IsImplicit ||
                conv.Kind
                is ConversionKind.ExplicitNumeric
                or ConversionKind.ExplicitReference
                or ConversionKind.Unboxing
                or ConversionKind.UserDefined);

            if (!ok)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CASG001",
                    DiagnosticSeverity.Error,
                    $"No conversion from '{expr.Type.Name}' to '{targetType.Name}' in compound assignment.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                var bad = new BoundBadExpression(syntaxForBoundNode);
                bad.SetType(targetType);
                return bad;
            }
            if (conv.Kind == ConversionKind.UserDefined)
            {
                return ApplyConversion(
                    exprSyntax: syntaxForBoundNode,
                    expr: expr,
                    targetType: targetType,
                    diagnosticNode: diagnosticNode,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: false);
            }
            bool isChecked = IsCheckedOverflowContext && conv.Kind == ConversionKind.ExplicitNumeric;
            return new BoundConversionExpression(syntaxForBoundNode, targetType, expr, conv, isChecked);
        }
        private BoundExpression BindAssignableValue(ExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            BoundExpression expr;

            if (node is DeclarationExpressionSyntax de)
            {
                expr = BindOutDeclarationExpressionAsLValue(de, context, diagnostics);
                expr = Record(de, expr, context);
            }
            else if (node is MemberAccessExpressionSyntax ma)
            {
                expr = BindMemberAccess(ma, BindValueKind.LValue, context, diagnostics);
                expr = Record(ma, expr, context);
            }
            else if (node is ElementAccessExpressionSyntax ea)
            {
                expr = BindElementAccess(ea, BindValueKind.LValue, context, diagnostics);
                expr = Record(ea, expr, context);
            }
            else if (node is IdentifierNameSyntax id)
            {
                var name = id.Identifier.ValueText ?? "";
                if (TryGetLocalFromEnclosingScopes(name, out var local))
                {
                    expr = new BoundLocalExpression(id, local!);
                    expr = Record(id, expr, context);
                }
                else if (TryGetParameterFromEnclosingScopes(name, out var param))
                {
                    expr = new BoundParameterExpression(id, param!);
                    expr = Record(id, expr, context);
                }
                else if (TryBindUnqualifiedMember(id, name, BindValueKind.LValue, context, diagnostics, out var memberExpr))
                {
                    expr = Record(id, memberExpr, context);
                }
                else if (TryBindImportedStaticMember(id, name, BindValueKind.LValue, context, diagnostics, out var staticMemberExpr))
                {
                    expr = Record(id, staticMemberExpr, context);
                }
                else
                {
                    diagnostics.Add(new Diagnostic("CN_BIND003", DiagnosticSeverity.Error,
                        $"Use of undeclared identifier '{name}' during assignment.",
                        new Location(context.SemanticModel.SyntaxTree, id.Span)));

                    expr = new BoundBadExpression(id);
                    expr = Record(id, expr, context);
                }
            }
            else
            {
                expr = BindExpression(node, context, diagnostics);
            }

            if (expr.HasErrors)
                return expr;

            if (expr.IsLValue)
                return expr;


            diagnostics.Add(new Diagnostic("CN_ASG002", DiagnosticSeverity.Error,
                "The left-hand side of an assignment must be a variable, property or indexer.",
                new Location(context.SemanticModel.SyntaxTree, node.Span)));

            return new BoundBadExpression(node);
        }
        private BoundExpression BindOutDeclarationExpressionAsLValue(
            DeclarationExpressionSyntax de,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (de.Designation is DiscardDesignationSyntax)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_OUTDECL000",
                    DiagnosticSeverity.Error,
                    "Discard designation in out-argument is not implemented.",
                    new Location(context.SemanticModel.SyntaxTree, de.Span)));

                return new BoundBadExpression(de);
            }

            if (de.Designation is not SingleVariableDesignationSyntax sv)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_OUTDECL001",
                    DiagnosticSeverity.Error,
                    "Unsupported variable designation in out argument.",
                    new Location(context.SemanticModel.SyntaxTree, de.Span)));

                return new BoundBadExpression(de);
            }

            var name = sv.Identifier.ValueText ?? "";

            if (IsNameDeclaredInEnclosingScopes(name))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_OUTDECL002",
                    DiagnosticSeverity.Error,
                    $"A local named '{name}' is already declared in this scope.",
                    new Location(context.SemanticModel.SyntaxTree, sv.Span)));
            }

            var isVar = IsVar(de.Type);

            TypeSymbol localType;
            if (isVar)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_OUTDECL003",
                    DiagnosticSeverity.Error,
                    "'out var' type inference is not implemented. Use an explicit type.",
                    new Location(context.SemanticModel.SyntaxTree, de.Type.Span)));

                localType = new ErrorTypeSymbol("out-var", containing: null, ImmutableArray<Location>.Empty);
            }
            else
            {
                localType = BindType(de.Type, context, diagnostics);
            }

            var local = new LocalSymbol(
                name: name,
                containing: _containing,
                type: localType,
                locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, sv.Span)),
                isConst: false,
                constantValueOpt: Optional<object>.None,
                isByRef: false);

            _locals[name] = local;
            context.Recorder.RecordDeclared(sv, local);

            var idSyntax = new IdentifierNameSyntax(sv.Identifier);
            return new BoundLocalExpression(idSyntax, local);
        }
        private static bool IsReadOnlyValueReceiver(BoundExpression? receiver, BindingContext context)
        {
            if (IsReadOnlyStructThisReceiver(receiver, context))
                return true;

            if (receiver is BoundParameterExpression p &&
                p.Parameter.IsReadOnlyRef &&
                p.Parameter.Type.IsValueType)
            {
                return true;
            }

            return false;
        }
        private static bool IsReadOnlyStructThisReceiver(BoundExpression? receiver, BindingContext context)
        {
            if (receiver is not BoundThisExpression)
                return false;

            if (context.ContainingSymbol is not MethodSymbol method)
                return false;

            // Constructors may assign fields
            if (method.IsConstructor || method.IsStatic)
                return false;

            return method.ContainingSymbol is NamedTypeSymbol nt && nt.IsReadOnlyStruct;
        }
        private static bool IsReadOnlyByRefParameterTarget(BoundExpression expr)
        {
            return expr is BoundParameterExpression p && p.Parameter.IsReadOnlyRef;
        }
        private static bool IsInsideIntrinsicMethod(BindingContext context)
        {
            if (context.ContainingSymbol is not MethodSymbol method)
                return false;

            var attrs = method.GetAttributes();
            for (int i = 0; i < attrs.Length; i++)
            {
                var attrClass = attrs[i].AttributeClass;
                if (attrClass is not null &&
                    string.Equals(attrClass.Name, "IntrinsicAttribute", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
        private static bool CanAssignReadOnlyFieldInConstructor(
            FieldSymbol field,
            BoundExpression? receiver,
            BindingContext context)
        {
            if (!field.IsReadOnly)
                return false;

            // Must be inside a constructor of the same containing type
            if (context.ContainingSymbol is not MethodSymbol method || !method.IsConstructor)
                return false;

            if (!ReferenceEquals(method.ContainingSymbol, field.ContainingSymbol))
                return false;

            // Static/instance must match
            if (method.IsStatic != field.IsStatic)
                return false;

            if (!field.IsStatic)
                return receiver is null || receiver is BoundThisExpression;

            // Static readonly field write in static ctor
            return receiver is null;
        }
        private static bool CanAssignReadOnlyAutoPropertyInConstructor(
            PropertySymbol prop,
            BoundExpression? receiver,
            BindingContext context)
        {
            // Only source auto properties with synthesized backing field
            if (prop is not SourcePropertySymbol sp || sp.BackingFieldOpt is null)
                return false;

            // Property already has a setter
            if (prop.HasSet)
                return false;

            // Must be inside a constructor of the same containing type
            if (context.ContainingSymbol is not MethodSymbol method || !method.IsConstructor)
                return false;

            if (!ReferenceEquals(method.ContainingSymbol, prop.ContainingSymbol))
                return false;

            // Static/instance must match
            if (method.IsStatic != prop.IsStatic)
                return false;

            // For instance property only 'this' is allowed
            if (!prop.IsStatic)
            {
                return receiver is null || receiver is BoundThisExpression;
            }

            // Static property write in static ctor
            return receiver is null;
        }
        private BoundExpression BindInvocation(InvocationExpressionSyntax inv, BindingContext context, DiagnosticBag diagnostics)
        {
            var argSyntaxes = inv.ArgumentList.Arguments;
            var args = ImmutableArray.CreateBuilder<BoundExpression>(argSyntaxes.Count);
            for (int i = 0; i < argSyntaxes.Count; i++)
                args.Add(BindCallArgument(argSyntaxes[i], context, diagnostics));

            var boundArgs = args.ToImmutable();

            return inv.Expression switch
            {
                MemberAccessExpressionSyntax ma => BindMemberAccessInvocation(inv, ma, argSyntaxes, boundArgs, context, diagnostics),
                IdentifierNameSyntax id => BindSimpleInvocation(inv, id, argSyntaxes, boundArgs, context, diagnostics),
                GenericNameSyntax g => BindGenericSimpleInvocation(inv, g, argSyntaxes, boundArgs, context, diagnostics),
                _ => BindUnsupportedInvocation(inv, context, diagnostics)
            };
        }
        private BoundExpression BindCallArgument(ArgumentSyntax argSyntax, BindingContext context, DiagnosticBag diagnostics)
        {
            if (argSyntax.RefKindKeyword is null)
                return BindExpression(argSyntax.Expression, context, diagnostics);

            var rk = argSyntax.RefKindKeyword.Value;

            if (rk.Kind == SyntaxKind.IdentifierToken && rk.ContextualKind == SyntaxKind.ScopedKeyword)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ARGMOD001",
                    DiagnosticSeverity.Error,
                    "The 'scoped' modifier is not valid on an argument.",
                    new Location(context.SemanticModel.SyntaxTree, rk.Span)));

                return BindExpression(argSyntax.Expression, context, diagnostics);
            }

            if (rk.Kind == SyntaxKind.RefKeyword || rk.Kind == SyntaxKind.OutKeyword || rk.Kind == SyntaxKind.InKeyword)
                return BindByRefCallArgument(argSyntax, isReadOnlyPass: rk.Kind == SyntaxKind.InKeyword, context, diagnostics);

            return BindExpression(argSyntax.Expression, context, diagnostics);
        }

        private BoundExpression BindByRefCallArgument(
            ArgumentSyntax argSyntax,
            bool isReadOnlyPass,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var operand = BindAssignableValue(argSyntax.Expression, context, diagnostics);
            if (operand.HasErrors)
                return operand;

            if (operand is BoundLocalExpression bl && bl.Local.IsConst)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REF001",
                    DiagnosticSeverity.Error,
                    "Cannot create a ref to a const local.",
                    new Location(context.SemanticModel.SyntaxTree, argSyntax.Expression.Span)));
                return new BoundBadExpression(argSyntax);
            }

            if (operand is BoundMemberAccessExpression { Member: PropertySymbol } ||
                operand is BoundIndexerAccessExpression)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REF002",
                    DiagnosticSeverity.Error,
                    "A property cannot be used as a ref value here.",
                    new Location(context.SemanticModel.SyntaxTree, argSyntax.Expression.Span)));
                return new BoundBadExpression(argSyntax);
            }

            if (!isReadOnlyPass &&
                operand is BoundParameterExpression pe &&
                pe.Parameter.IsReadOnlyRef &&
                !IsInsideIntrinsicMethod(context))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REF003",
                    DiagnosticSeverity.Error,
                    "Cannot use a readonly by-ref parameter as a writable ref.",
                    new Location(context.SemanticModel.SyntaxTree, argSyntax.Expression.Span)));
                return new BoundBadExpression(argSyntax);
            }

            var refElementType = operand.Type is ByRefTypeSymbol br ? br.ElementType : operand.Type;
            var byRefType = context.Compilation.CreateByRefType(refElementType);
            return new BoundRefExpression(argSyntax, byRefType, operand);
        }

        private static ParameterRefKind GetArgRefKind(SyntaxToken? tok)
        {
            if (tok is null)
                return ParameterRefKind.None;

            var t = tok.Value;
            return t.Kind switch
            {
                SyntaxKind.RefKeyword => ParameterRefKind.Ref,
                SyntaxKind.OutKeyword => ParameterRefKind.Out,
                SyntaxKind.InKeyword => ParameterRefKind.In,
                _ => ParameterRefKind.None
            };
        }

        private static bool ArgumentRefKindMatchesParameter(SyntaxToken? argRefKindKeyword, ParameterSymbol parameter)
        {
            var argKind = GetArgRefKind(argRefKindKeyword);

            var paramKind = parameter.RefKind;
            if (paramKind == ParameterRefKind.None && parameter.Type is ByRefTypeSymbol)
                paramKind = parameter.IsReadOnlyRef ? ParameterRefKind.In : ParameterRefKind.Ref;

            if (argKind == ParameterRefKind.Ref && paramKind == ParameterRefKind.In)
                return true;
            if (argKind == ParameterRefKind.In && paramKind == ParameterRefKind.Ref && parameter.IsReadOnlyRef)
                return true;
            return argKind == paramKind;
        }
        private BoundExpression BindUnsupportedInvocation(InvocationExpressionSyntax inv, BindingContext context, DiagnosticBag diagnostics)
        {
            diagnostics.Add(new Diagnostic("CN_CALL000", DiagnosticSeverity.Error,
                $"Invocation target not supported: {inv.Expression.Kind}",
                new Location(context.SemanticModel.SyntaxTree, inv.Expression.Span)));
            return new BoundBadExpression(inv);
        }

        private BoundExpression BindSimpleInvocation(
            InvocationExpressionSyntax inv,
            IdentifierNameSyntax id,
            SeparatedSyntaxList<ArgumentSyntax> argSyntaxes,
            ImmutableArray<BoundExpression> args,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var name = id.Identifier.ValueText ?? "";

            if (TryBindSimpleValue(id, out _))
            {
                diagnostics.Add(new Diagnostic("CN_CALL010", DiagnosticSeverity.Error,
                    "Delegate invocation is not implemented.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));
                return new BoundBadExpression(inv);
            }
            // Local function invocation
            if (TryGetLocalFunctionFromEnclosingScopes(name, out var localFunc) && localFunc != null)
            {
                var candidates = ImmutableArray.Create<MethodSymbol>(localFunc);
                if (!TryResolveOverload(
                    candidates: candidates,
                    args: args,
                    getArgExprSyntax: i => argSyntaxes[i].Expression,
                    getArgRefKindKeyword: i => argSyntaxes[i].RefKindKeyword,
                    getArgName: i => argSyntaxes[i].NameColon?.Name.Identifier.ValueText,
                    chosen: out var chosen,
                    convertedArgs: out var convertedArgs,
                    context: context,
                    diagnostics: diagnostics,
                    diagnosticNode: inv))
                {
                    return new BoundBadExpression(inv);
                }

                return new BoundCallExpression(inv, receiverOpt: null, method: chosen!, arguments: convertedArgs);
            }
            {
                var methodCtx = context.ContainingSymbol as MethodSymbol;
                var containingType = methodCtx?.ContainingSymbol as NamedTypeSymbol;
                if (containingType is null)
                {
                    diagnostics.Add(new Diagnostic("CN_CALL011", DiagnosticSeverity.Error,
                        "Cannot bind an unqualified invocation without an enclosing type.",
                        new Location(context.SemanticModel.SyntaxTree, id.Span)));
                    return new BoundBadExpression(inv);
                }

                var candidates = LookupMethods(containingType, name);
                if (methodCtx != null && methodCtx.IsStatic)
                    candidates = candidates.Where(m => m.IsStatic).ToImmutableArray();
                bool fromUsingStatic = false;
                if (candidates.IsDefaultOrEmpty)
                {
                    candidates = LookupImportedStaticMethods(name, context);
                    fromUsingStatic = !candidates.IsDefaultOrEmpty;
                }
                if (candidates.IsDefaultOrEmpty)
                {
                    diagnostics.Add(new Diagnostic("CN_CALL012", DiagnosticSeverity.Error,
                        $"No method '{name}' found in type '{containingType.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, id.Span)));
                    return new BoundBadExpression(inv);
                }
                if (!fromUsingStatic)
                {
                    candidates = candidates
                    .Where(m => AccessibilityHelper.IsAccessible(m, context))
                    .ToImmutableArray();
                }
                if (candidates.IsDefaultOrEmpty)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_CALL_ACC001",
                        DiagnosticSeverity.Error,
                        $"No accessible overload of '{name}' found in type '{containingType.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, id.Span)));
                    return new BoundBadExpression(inv);
                }
                var nonGeneric = candidates.Where(m => m.TypeParameters.IsDefaultOrEmpty).ToImmutableArray();
                if (!nonGeneric.IsDefaultOrEmpty)
                {
                    candidates = nonGeneric;
                }
                else
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_CALL014",
                        DiagnosticSeverity.Error,
                        $"Generic method '{name}' requires explicit type arguments (type inference is not implemented).",
                        new Location(context.SemanticModel.SyntaxTree, id.Span)));
                    return new BoundBadExpression(inv);
                }
                if (!TryResolveOverload(
                    candidates: candidates,
                    args: args,
                    getArgExprSyntax: i => argSyntaxes[i].Expression,
                    getArgRefKindKeyword: i => argSyntaxes[i].RefKindKeyword,
                    getArgName: i => argSyntaxes[i].NameColon?.Name.Identifier.ValueText,
                    chosen: out var chosen,
                    convertedArgs: out var convertedArgs,
                    context: context,
                    diagnostics: diagnostics,
                    diagnosticNode: inv))
                {
                    return new BoundBadExpression(inv);
                }

                if (methodCtx != null && methodCtx.IsStatic && chosen is { IsStatic: false })
                {
                    diagnostics.Add(new Diagnostic("CN_CALL013", DiagnosticSeverity.Error,
                        "An object reference is required for the non-static method.",
                        new Location(context.SemanticModel.SyntaxTree, id.Span)));
                    return new BoundBadExpression(inv);
                }

                // For instance methods, ReceiverOpt == null => implicit this 
                return new BoundCallExpression(inv, receiverOpt: null, method: chosen!, arguments: convertedArgs);
            }
        }
        private BoundExpression BindMemberAccessInvocation(
            InvocationExpressionSyntax inv,
            MemberAccessExpressionSyntax ma,
            SeparatedSyntaxList<ArgumentSyntax> argSyntaxes,
            ImmutableArray<BoundExpression> args,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var name = GetSimpleName(ma.Name);
            if (string.IsNullOrEmpty(name))
            {
                diagnostics.Add(new Diagnostic("CN_CALL020", DiagnosticSeverity.Error,
                    "Invalid member name in invocation.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Name.Span)));
                return new BoundBadExpression(inv);
            }

            bool isPointerAccess = ma.Kind == SyntaxKind.PointerMemberAccessExpression;

            BoundExpression? receiverValue;
            NamedTypeSymbol? receiverType;

            if (!TryBindReceiverForMemberAccess(ma.Expression, isPointerAccess, out receiverValue, out receiverType, context, diagnostics))
            {
                var sym = BindNamespaceOrType(ma.Expression, context, diagnostics);
                receiverType = sym as NamedTypeSymbol;
                receiverValue = null;
            }

            if (receiverType is null)
            {
                diagnostics.Add(new Diagnostic("CN_CALL021", DiagnosticSeverity.Error,
                    "Receiver is not a type or a value with members.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Expression.Span)));
                return new BoundBadExpression(inv);
            }

            var candidates = LookupMethods(receiverType, name);
            candidates = (receiverValue is null)
                ? candidates.Where(m => m.IsStatic).ToImmutableArray()
                : candidates.Where(m => !m.IsStatic).ToImmutableArray();

            bool methodFound = !candidates.IsDefaultOrEmpty;

            candidates = candidates
                .Where(m => AccessibilityHelper.IsAccessible(m, context))
                .ToImmutableArray();

            if (methodFound && candidates.IsDefaultOrEmpty) // method exists but not accessible
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CALL_ACC002",
                    DiagnosticSeverity.Error,
                    $"No accessible overload of '{name}' found on type '{receiverType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Name.Span)));
                return new BoundBadExpression(inv);
            }
            if (!candidates.IsDefaultOrEmpty)
            {
                if (ma.Name is GenericNameSyntax gName)
                {
                    var explicitTypeArgs = BindTypeArguments(gName.TypeArgumentList.Arguments, context, diagnostics);
                    var arity = explicitTypeArgs.Length;

                    var arityMatches = candidates.Where(m => m.TypeParameters.Length == arity).ToImmutableArray();
                    if (arityMatches.IsDefaultOrEmpty)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_CALLG010",
                            DiagnosticSeverity.Error,
                            $"No overload of '{name}' has {arity} type parameter(s).",
                            new Location(context.SemanticModel.SyntaxTree, gName.Span)));
                        return new BoundBadExpression(inv);
                    }

                    var constructed = ImmutableArray.CreateBuilder<MethodSymbol>(arityMatches.Length);
                    for (int i = 0; i < arityMatches.Length; i++)
                    {
                        var def = arityMatches[i];
                        if (!GenericConstraintChecker.CheckMethodInstantiation(
                            methodDefinition: def,
                            typeArguments: explicitTypeArgs,
                            getArgSpan: a => gName.TypeArgumentList.Arguments[a].Span,
                            context: context,
                            diagnostics: diagnostics))
                        {
                            continue;
                        }

                        constructed.Add(new ConstructedMethodSymbol(def, explicitTypeArgs, context.Compilation.TypeManager));
                    }

                    candidates = constructed.ToImmutable();
                }
                else
                {
                    var nonGeneric = candidates.Where(m => m.TypeParameters.IsDefaultOrEmpty).ToImmutableArray();
                    if (!nonGeneric.IsDefaultOrEmpty)
                    {
                        candidates = nonGeneric;
                    }
                    else
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_CALLG011",
                            DiagnosticSeverity.Error,
                            $"Generic method '{name}' requires explicit type arguments (type inference is not implemented).",
                            new Location(context.SemanticModel.SyntaxTree, ma.Name.Span)));
                        return new BoundBadExpression(inv);
                    }
                }
                if (TryResolveOverload(
                    candidates: candidates,
                    args: args,
                    getArgExprSyntax: i => argSyntaxes[i].Expression,
                    getArgRefKindKeyword: i => argSyntaxes[i].RefKindKeyword,
                    getArgName: i => argSyntaxes[i].NameColon?.Name.Identifier.ValueText,
                    chosen: out var chosen,
                    convertedArgs: out var convertedArgs,
                    context: context,
                    diagnostics: diagnostics,
                    diagnosticNode: inv))
                {
                    return new BoundCallExpression(inv, receiverOpt: receiverValue, method: chosen!, arguments: convertedArgs);
                }
                return new BoundBadExpression(inv);
            }
            // extentions
            if (receiverValue is not null)
            {
                var extensionCandidates = LookupExtensionMethods(name, receiverValue, context);

                if (!extensionCandidates.IsDefaultOrEmpty)
                {
                    var extArgsBuilder = ImmutableArray.CreateBuilder<BoundExpression>(args.Length + 1);
                    extArgsBuilder.Add(receiverValue);
                    extArgsBuilder.AddRange(args);
                    var extArgs = extArgsBuilder.ToImmutable();

                    if (TryResolveOverload(
                        candidates: extensionCandidates,
                        args: extArgs,
                        getArgExprSyntax: i => i == 0 ? ma.Expression : argSyntaxes[i - 1].Expression,
                        getArgRefKindKeyword: i => i == 0 ? null : argSyntaxes[i - 1].RefKindKeyword,
                        getArgName: i => i == 0 ? null : argSyntaxes[i - 1].NameColon?.Name.Identifier.ValueText,
                        chosen: out var chosen,
                        convertedArgs: out var convertedArgs,
                        context: context,
                        diagnostics: diagnostics,
                        diagnosticNode: inv))
                    {
                        return new BoundCallExpression(inv, receiverOpt: null, method: chosen!, arguments: convertedArgs);
                    }

                    return new BoundBadExpression(inv);
                }
            }

            diagnostics.Add(new Diagnostic("CN_CALL022", DiagnosticSeverity.Error,
                $"No method '{name}' found on type '{receiverType.Name}'.",
                new Location(context.SemanticModel.SyntaxTree, ma.Name.Span)));

            return new BoundBadExpression(inv);
        }
        private BoundExpression BindGenericSimpleInvocation(
            InvocationExpressionSyntax inv,
            GenericNameSyntax nameSyntax,
            SeparatedSyntaxList<ArgumentSyntax> argSyntaxes,
            ImmutableArray<BoundExpression> args,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var name = nameSyntax.Identifier.ValueText ?? "";
            var explicitTypeArgs = BindTypeArguments(nameSyntax.TypeArgumentList.Arguments, context, diagnostics);

            if (TryGetLocalFunctionFromEnclosingScopes(name, out var localFunc) && localFunc != null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CALLG001",
                    DiagnosticSeverity.Error,
                    "Generic local function invocation is not implemented.",
                    new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));
                return new BoundBadExpression(inv);
            }

            var methodCtx = context.ContainingSymbol as MethodSymbol;
            var containingType = methodCtx?.ContainingSymbol as NamedTypeSymbol;
            if (containingType is null)
            {
                diagnostics.Add(new Diagnostic("CN_CALLG002", DiagnosticSeverity.Error,
                    "Cannot bind an unqualified invocation without an enclosing type.",
                    new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));
                return new BoundBadExpression(inv);
            }

            var candidates = LookupMethods(containingType, name);
            if (methodCtx != null && methodCtx.IsStatic)
                candidates = candidates.Where(m => m.IsStatic).ToImmutableArray();
            bool fromUsingStatic = false;
            if (candidates.IsDefaultOrEmpty)
            {
                candidates = LookupImportedStaticMethods(name, context);
                fromUsingStatic = !candidates.IsDefaultOrEmpty;
            }
            if (candidates.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic("CN_CALLG003", DiagnosticSeverity.Error,
                    $"No method '{name}' found in type '{containingType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));
                return new BoundBadExpression(inv);
            }
            if (!fromUsingStatic)
            {
                candidates = candidates
                    .Where(m => AccessibilityHelper.IsAccessible(m, context))
                    .ToImmutableArray();
            }

            if (candidates.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CALL_ACC003",
                    DiagnosticSeverity.Error,
                    $"No accessible overload of '{name}' found in type '{containingType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));
                return new BoundBadExpression(inv);
            }
            var arity = explicitTypeArgs.Length;
            var arityMatches = candidates.Where(m => m.TypeParameters.Length == arity).ToImmutableArray();
            if (arityMatches.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic("CN_CALLG004", DiagnosticSeverity.Error,
                    $"No overload of '{name}' has {arity} type parameter(s).",
                    new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));
                return new BoundBadExpression(inv);
            }

            var constructed = ImmutableArray.CreateBuilder<MethodSymbol>(arityMatches.Length);
            for (int i = 0; i < arityMatches.Length; i++)
                constructed.Add(new ConstructedMethodSymbol(arityMatches[i], explicitTypeArgs, context.Compilation.TypeManager));

            if (!TryResolveOverload(
                candidates: constructed.ToImmutable(),
                args: args,
                getArgExprSyntax: i => argSyntaxes[i].Expression,
                getArgRefKindKeyword: i => argSyntaxes[i].RefKindKeyword,
                getArgName: i => argSyntaxes[i].NameColon?.Name.Identifier.ValueText,
                chosen: out var chosen,
                convertedArgs: out var convertedArgs,
                context: context,
                diagnostics: diagnostics,
                diagnosticNode: inv))
            {
                return new BoundBadExpression(inv);
            }

            if (methodCtx != null && methodCtx.IsStatic && chosen is { IsStatic: false })
            {
                diagnostics.Add(new Diagnostic("CN_CALLG005", DiagnosticSeverity.Error,
                    "An object reference is required for the non-static method.",
                    new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));
                return new BoundBadExpression(inv);
            }

            // For instance methods, ReceiverOpt == null => implicit this
            return new BoundCallExpression(inv, receiverOpt: null, method: chosen!, arguments: convertedArgs);
        }
        private bool TryBindReceiverForMemberAccess(
            ExpressionSyntax receiverSyntax,
            bool isPointerAccess,
            out BoundExpression? receiverValue,
            out NamedTypeSymbol? receiverType,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            receiverValue = null;
            receiverType = null;

            if (isPointerAccess)
            {
                EnsureUnsafe(receiverSyntax, context, diagnostics);

                // Pointer member access is always a value receiver; treat it as (*ptr).Member. 
                var ptrValue = receiverSyntax is IdentifierNameSyntax id && TryBindSimpleValue(id, out var v)
                    ? v
                    : BindExpression(receiverSyntax, context, diagnostics);

                if (ptrValue.Type is not PointerTypeSymbol pt)
                {
                    diagnostics.Add(new Diagnostic("CN_CALL023", DiagnosticSeverity.Error,
                        "The receiver of '->' must be a pointer.",
                        new Location(context.SemanticModel.SyntaxTree, receiverSyntax.Span)));
                    receiverValue = new BoundBadExpression(receiverSyntax);
                    return true;
                }

                var pointedAt = pt.PointedAtType;
                receiverType = GetReceiverTypeForMemberLookup(pointedAt);
                if (receiverType is null)
                {
                    diagnostics.Add(new Diagnostic("CN_CALL024", DiagnosticSeverity.Error,
                        $"The receiver type '{pointedAt.Name}' does not support member lookup.",
                        new Location(context.SemanticModel.SyntaxTree, receiverSyntax.Span)));
                    receiverValue = new BoundBadExpression(receiverSyntax);
                    return true;
                }

                receiverValue = new BoundPointerIndirectionExpression(receiverSyntax, pointedAt, ptrValue);
                return true;
            }
            if (receiverSyntax is IdentifierNameSyntax rid)
            {
                if (TryBindSimpleValue(rid, out var simple))
                {
                    receiverValue = simple;
                    receiverType = GetReceiverTypeForMemberLookup(simple.Type);
                    return true;
                }
                var name = rid.Identifier.ValueText ?? "";
                if (!string.IsNullOrEmpty(name) &&
                    TryBindUnqualifiedMember(rid, name, BindValueKind.RValue, context, diagnostics, out var memberExpr))
                {
                    receiverValue = memberExpr;
                    receiverType = GetReceiverTypeForMemberLookup(memberExpr.Type);
                    return true;
                }
                if (!string.IsNullOrEmpty(name) &&
                    TryBindImportedStaticMember(rid, name, BindValueKind.RValue, context, diagnostics, out var staticMemberExpr))
                {
                    receiverValue = staticMemberExpr;
                    receiverType = GetReceiverTypeForMemberLookup(staticMemberExpr.Type);
                    return true;
                }
                return false;
            }
            if (receiverSyntax is GenericNameSyntax)
                return false;
            if (receiverSyntax is MemberAccessExpressionSyntax)
            {
                var tmpDiagnostics = new DiagnosticBag();
                var tmpContext = WithRecorder(context, NullBindingRecorder.Instance);

                var tmpValue = BindExpression(receiverSyntax, tmpContext, tmpDiagnostics);
                if (!tmpValue.HasErrors)
                {
                    var tmpReceiverType = GetReceiverTypeForMemberLookup(tmpValue.Type);
                    if (tmpReceiverType is not null)
                    {
                        receiverValue = tmpValue;
                        receiverType = tmpReceiverType;
                        return true;
                    }
                }
                return false;
            }
            if (receiverSyntax is PredefinedTypeSyntax pts)
            {
                receiverValue = null;
                receiverType = BindType(pts, context, diagnostics) as NamedTypeSymbol;
                return true;
            }

            receiverValue = BindExpression(receiverSyntax, context, diagnostics);
            receiverType = GetReceiverTypeForMemberLookup(receiverValue.Type);
            return true;
        }

        private BoundExpression BindMemberAccess(
            MemberAccessExpressionSyntax ma,
            BindValueKind valueKind,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var name = GetSimpleName(ma.Name);
            if (string.IsNullOrEmpty(name))
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC000", DiagnosticSeverity.Error,
                    "Invalid member name in member access.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Name.Span)));
                return new BoundBadExpression(ma);
            }
            bool isPointerAccess = ma.Kind == SyntaxKind.PointerMemberAccessExpression;
            BoundExpression? receiverValue;
            NamedTypeSymbol? receiverType;
            if (!TryBindReceiverForMemberAccess(
                ma.Expression,
                isPointerAccess,
                out receiverValue,
                out receiverType,
                context,
                diagnostics))
            {
                var sym = BindNamespaceOrType(ma.Expression, context, diagnostics);
                receiverType = sym as NamedTypeSymbol;
                receiverValue = null;
            }
            if (receiverType is null)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC001", DiagnosticSeverity.Error,
                    "Receiver is not a type or a value with members.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Expression.Span)));
                return new BoundBadExpression(ma);
            }
            var members = LookupMembers(receiverType, name);
            if (members.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC002", DiagnosticSeverity.Error,
                    $"No member '{name}' found on type '{receiverType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Name.Span)));
                return new BoundBadExpression(ma);
            }
            var accessibleMembers = FilterAccessibleMembers(members, context);
            if (accessibleMembers.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ACC002",
                    DiagnosticSeverity.Error,
                    $"Member '{name}' is inaccessible due to its protection level.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Name.Span)));

                return new BoundBadExpression(ma);
            }

            members = accessibleMembers;
            FieldSymbol? field = null;
            PropertySymbol? prop = null;
            bool hasMethod = false;
            bool hasType = false;
            for (int i = 0; i < members.Length; i++)
            {
                switch (members[i])
                {
                    case FieldSymbol f:
                        field ??= f;
                        break;
                    case PropertySymbol p:
                        prop ??= p;
                        break;
                    case MethodSymbol:
                        hasMethod = true;
                        break;
                    case NamedTypeSymbol:
                        hasType = true;
                        break;
                }
            }

            if (field is null && prop is null)
            {
                if (hasMethod)
                {
                    diagnostics.Add(new Diagnostic("CN_MEMACC003", DiagnosticSeverity.Error,
                        "Method groups are not supported.",
                        new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                    return new BoundBadExpression(ma);
                }
                if (hasType)
                {
                    diagnostics.Add(new Diagnostic("CN_MEMACC004", DiagnosticSeverity.Error,
                        "A type name is not a value.",
                        new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                    return new BoundBadExpression(ma);
                }
                diagnostics.Add(new Diagnostic("CN_MEMACC005", DiagnosticSeverity.Error,
                    "Member access does not resolve to a value member.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                return new BoundBadExpression(ma);
            }
            if (field is not null && prop is not null)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC006", DiagnosticSeverity.Error,
                    $"Member name '{name}' is ambiguous between a field and a property.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Name.Span)));
                return new BoundBadExpression(ma);
            }
            // field
            if (field is not null)
            {
                if (valueKind == BindValueKind.LValue && field.IsConst)
                {
                    diagnostics.Add(new Diagnostic("CN_MEMACC010", DiagnosticSeverity.Error,
                        "Cannot assign to a const field.",
                        new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                    return new BoundBadExpression(ma);
                }
                bool isStatic = field.IsStatic;
                if (receiverValue is null)
                {
                    if (!isStatic)
                    {
                        diagnostics.Add(new Diagnostic("CN_MEMACC011", DiagnosticSeverity.Error,
                            "An object reference is required for the non-static field.",
                            new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                        return new BoundBadExpression(ma);
                    }
                }
                else
                {
                    if (isStatic)
                    {
                        diagnostics.Add(new Diagnostic("CN_MEMACC012", DiagnosticSeverity.Error,
                            "A static field cannot be accessed with an instance reference.",
                            new Location(context.SemanticModel.SyntaxTree, ma.Expression.Span)));
                        receiverValue = null;
                    }
                }
                bool isRefField = field.Type is ByRefTypeSymbol;
                TypeSymbol fieldValueType = isRefField ? ((ByRefTypeSymbol)field.Type).ElementType : field.Type;

                if (valueKind == BindValueKind.LValue &&
                    !field.IsStatic &&
                    IsReadOnlyValueReceiver(receiverValue, context) &&
                    !isRefField)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_READONLY_THIS001",
                        DiagnosticSeverity.Error,
                        "Cannot assign to instance members of 'this' in a readonly struct instance member.",
                        new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                    return new BoundBadExpression(ma);
                }
                bool allowCtorReadonlyWrite =
                    valueKind == BindValueKind.LValue &&
                    field.IsReadOnly &&
                    CanAssignReadOnlyFieldInConstructor(field, receiverValue, context);

                if (valueKind == BindValueKind.LValue && field.IsReadOnly && !allowCtorReadonlyWrite && !isRefField)
                {
                    diagnostics.Add(new Diagnostic("CN_MEMACC013", DiagnosticSeverity.Error,
                        "Cannot assign to a readonly field except in a constructor of the same type.",
                        new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                    return new BoundBadExpression(ma);
                }
                bool canWriteField = !field.IsConst && (isRefField || !field.IsReadOnly || allowCtorReadonlyWrite);

                var cv = field.IsConst ? field.ConstantValueOpt : Optional<object>.None;
                return new BoundMemberAccessExpression(
                    ma,
                    receiverValue,
                    field,
                    fieldValueType,
                    isLValue: canWriteField,
                    constantValueOpt: cv);
            }
            // property
            if (prop is null)
                return new BoundBadExpression(ma);
            bool canReadProperty =
                prop.GetMethod is not null &&
                AccessibilityHelper.IsAccessible(prop.GetMethod, context);

            bool canWriteProperty =
                prop.SetMethod is not null &&
                AccessibilityHelper.IsAccessible(prop.SetMethod, context);
            if (valueKind == BindValueKind.RValue && !canReadProperty)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC020", DiagnosticSeverity.Error,
                    "Property has no accessible getter.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                return new BoundBadExpression(ma);
            }

            bool allowCtorAutoPropWrite =
                valueKind == BindValueKind.LValue &&
                !canWriteProperty &&
                CanAssignReadOnlyAutoPropertyInConstructor(prop, receiverValue, context);

            if (valueKind == BindValueKind.LValue && !canWriteProperty && !allowCtorAutoPropWrite)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC021", DiagnosticSeverity.Error,
                    "Property has no accessible setter.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                return new BoundBadExpression(ma);
            }
            bool propIsStatic = prop.IsStatic;
            if (receiverValue is null)
            {
                if (!propIsStatic)
                {
                    diagnostics.Add(new Diagnostic("CN_MEMACC022", DiagnosticSeverity.Error,
                        "An object reference is required for the non-static property.",
                        new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                    return new BoundBadExpression(ma);
                }
            }
            else
            {
                if (propIsStatic)
                {
                    diagnostics.Add(new Diagnostic("CN_MEMACC023", DiagnosticSeverity.Error,
                        "A static property cannot be accessed with an instance reference.",
                        new Location(context.SemanticModel.SyntaxTree, ma.Expression.Span)));
                    receiverValue = null;
                }
            }
            if (valueKind == BindValueKind.LValue &&
                !prop.IsStatic &&
                IsReadOnlyValueReceiver(receiverValue, context))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_READONLY_THIS002",
                    DiagnosticSeverity.Error,
                    "Cannot assign to instance properties of 'this' in a readonly struct instance member.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                return new BoundBadExpression(ma);
            }
            return new BoundMemberAccessExpression(
                ma,
                receiverValue,
                prop,
                prop.Type,
                isLValue: canWriteProperty || allowCtorAutoPropWrite);
        }
        private static NamedTypeSymbol? GetReceiverTypeForMemberLookup(TypeSymbol type)
        {
            if (type is NamedTypeSymbol nt)
                return nt;

            if (type is ArrayTypeSymbol at)
                return at.BaseType as NamedTypeSymbol;

            return null;
        }
        private static ImmutableArray<Symbol> FilterAccessibleMembers(
            ImmutableArray<Symbol> members, BindingContext context)
        {
            if (members.IsDefaultOrEmpty)
                return members;

            var b = ImmutableArray.CreateBuilder<Symbol>(members.Length);
            for (int i = 0; i < members.Length; i++)
            {
                var m = members[i];
                if (AccessibilityHelper.IsAccessible(m, context))
                    b.Add(m);
            }

            return b.Count == members.Length ? members : b.ToImmutable();
        }
        private IEnumerable<NamedTypeSymbol> EnumerateExtensionContainerTypes(BindingContext context)
        {
            var imports = GetImports(context);
            var seen = new HashSet<NamedTypeSymbol>(ReferenceEqualityComparer<NamedTypeSymbol>.Instance);

            // using static X;
            for (int i = 0; i < imports.StaticTypes.Length; i++)
            {
                var t = imports.StaticTypes[i];
                if (seen.Add(t))
                    yield return t;
            }

            // regular namespace/type imports
            for (int i = 0; i < imports.Containers.Length; i++)
            {
                switch (imports.Containers[i])
                {
                    case NamedTypeSymbol nt:
                        if (seen.Add(nt))
                            yield return nt;
                        break;

                    case NamespaceSymbol ns:
                        {
                            var types = ns.GetTypeMembers();
                            for (int j = 0; j < types.Length; j++)
                                if (seen.Add(types[j]))
                                    yield return types[j];
                            break;
                        }
                }
            }

            // current enclosing type namespace
            for (Symbol? s = context.ContainingSymbol; s is not null; s = s.ContainingSymbol)
            {
                if (s is NamespaceSymbol curNs)
                {
                    var types = curNs.GetTypeMembers();
                    for (int j = 0; j < types.Length; j++)
                        if (seen.Add(types[j]))
                            yield return types[j];
                    break;
                }
            }
        }
        private ImmutableArray<MethodSymbol> LookupExtensionMethods(
            string name,
            BoundExpression receiver,
            BindingContext context)
        {
            var b = ImmutableArray.CreateBuilder<MethodSymbol>();

            foreach (var containerType in EnumerateExtensionContainerTypes(context))
            {
                var methods = LookupMethods(containerType, name);
                if (methods.IsDefaultOrEmpty)
                    continue;

                for (int i = 0; i < methods.Length; i++)
                {
                    var m = methods[i];
                    if (!IsExtensionMethod(m))
                        continue;

                    if (!AccessibilityHelper.IsAccessible(m, context))
                        continue;

                    if (m.Parameters.Length == 0)
                        continue;

                    var firstParamType = m.Parameters[0].Type;
                    var conv = ClassifyConversion(receiver, firstParamType, context);
                    if (!conv.Exists || !conv.IsImplicit)
                        continue;

                    b.Add(m);
                }
            }

            return b.Count == 0 ? ImmutableArray<MethodSymbol>.Empty : b.ToImmutable();
        }
        private static ImmutableArray<Symbol> LookupMembers(NamedTypeSymbol type, string name)
        {
            for (NamedTypeSymbol? t = type; t != null; t = t.BaseType as NamedTypeSymbol)
            {
                var all = t.GetMembers();
                var b = ImmutableArray.CreateBuilder<Symbol>();
                for (int i = 0; i < all.Length; i++)
                {
                    var m = all[i];
                    if (StringComparer.Ordinal.Equals(m.Name, name))
                        b.Add(m);
                }
                if (b.Count != 0)
                    return b.ToImmutable();
            }
            return ImmutableArray<Symbol>.Empty;
        }
        private static ImmutableArray<MethodSymbol> LookupMethods(NamedTypeSymbol type, string name)
        {
            var b = ImmutableArray.CreateBuilder<MethodSymbol>();
            for (NamedTypeSymbol? t = type; t != null; t = t.BaseType as NamedTypeSymbol)
            {
                var members = t.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is MethodSymbol ms &&
                        !ms.IsConstructor &&
                        StringComparer.Ordinal.Equals(ms.Name, name))
                    {
                        bool dup = false;
                        for (int j = 0; j < b.Count; j++)
                        {
                            if (SameSignature(b[j], ms))
                            {
                                dup = true;
                                break;
                            }
                        }
                        if (!dup)
                            b.Add(ms);
                    }
                }
            }
            return b.ToImmutable();
        }
        private static bool SameSignature(MethodSymbol a, MethodSymbol b)
        {
            if (a.IsStatic != b.IsStatic) return false;
            if (a.TypeParameters.Length != b.TypeParameters.Length) return false;

            var ap = a.Parameters;
            var bp = b.Parameters;
            if (ap.Length != bp.Length) return false;

            for (int i = 0; i < ap.Length; i++)
            {
                if (ap[i].RefKind != bp[i].RefKind) return false;
                if (ap[i].IsReadOnlyRef != bp[i].IsReadOnlyRef) return false;
                if (!ReferenceEquals(ap[i].Type, bp[i].Type)) return false;
            }
            return true;
        }
        private static ImmutableArray<PropertySymbol> LookupIndexers(NamedTypeSymbol type)
        {
            var builder = ImmutableArray.CreateBuilder<PropertySymbol>();

            for (NamedTypeSymbol? t = type; t is not null; t = t.BaseType as NamedTypeSymbol)
            {
                var members = t.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is PropertySymbol p && p.Parameters.Length != 0)
                        builder.Add(p);
                }

                if (builder.Count != 0)
                    return builder.ToImmutable();
            }

            return ImmutableArray<PropertySymbol>.Empty;
        }
        private static ImmutableArray<MethodSymbol> LookupConstructors(NamedTypeSymbol type)
        {
            if (type is null)
                return ImmutableArray<MethodSymbol>.Empty;

            var b = ImmutableArray.CreateBuilder<MethodSymbol>();
            var members = type.GetMembers();
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is MethodSymbol ms && ms.IsConstructor && !ms.IsStatic)
                    b.Add(ms);
            }
            return b.ToImmutable();
        }
        private ImmutableArray<TypeSymbol> BindTypeArguments(
            SeparatedSyntaxList<TypeSyntax> args,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var b = ImmutableArray.CreateBuilder<TypeSymbol>(args.Count);
            for (int i = 0; i < args.Count; i++)
                b.Add(BindType(args[i], context, diagnostics));
            return b.ToImmutable();
        }
        private BoundExpression BindImplicitObjectCreation(
            ImplicitObjectCreationExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            diagnostics.Add(new Diagnostic("CN_NEW003", DiagnosticSeverity.Error,
                "Cannot infer the type of a target-typed object creation expression. Provide an explicit target type.",
                new Location(context.SemanticModel.SyntaxTree, node.Span)));
            var bad = new BoundBadExpression(node);
            bad.SetType(new ErrorTypeSymbol("new", containing: null, ImmutableArray<Location>.Empty));
            return bad;
        }
        private BoundExpression BindImplicitObjectCreation(
            ImplicitObjectCreationExpressionSyntax node, TypeSymbol targetType, BindingContext context, DiagnosticBag diagnostics)
        {
            var argSyntaxes = node.ArgumentList.Arguments;
            var args = ImmutableArray.CreateBuilder<BoundExpression>(argSyntaxes.Count);
            for (int i = 0; i < argSyntaxes.Count; i++)
                args.Add(BindCallArgument(argSyntaxes[i], context, diagnostics));
            return BindImplicitObjectCreation(node, targetType, args.ToImmutable(), context, diagnostics);
        }
        private BoundExpression BindImplicitObjectCreation(
            ImplicitObjectCreationExpressionSyntax node,
            TypeSymbol targetType,
            ImmutableArray<BoundExpression> boundArgs,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (node.Initializer != null)
            {
                diagnostics.Add(new Diagnostic("CN_NEW000", DiagnosticSeverity.Error,
                    "Object/collection initializers are not supported.",
                    new Location(context.SemanticModel.SyntaxTree, node.Initializer.Span)));
            }
            if (targetType is not NamedTypeSymbol nt)
            {
                diagnostics.Add(new Diagnostic("CN_NEW001", DiagnosticSeverity.Error,
                    $"'{targetType.Name}' is not a constructible type.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                var bad = new BoundBadExpression(node);
                bad.SetType(targetType);
                return bad;
            }
            return BindObjectCreationCoreFromBoundArgs(
                syntax: node,
                type: nt,
                argSyntaxes: node.ArgumentList.Arguments,
                boundArgs: boundArgs,
                diagnosticSpan: node.Span,
                context: context,
                diagnostics: diagnostics);
        }
        private BoundExpression BindUnboundImplicitObjectCreation(
            ImplicitObjectCreationExpressionSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var argSyntaxes = node.ArgumentList.Arguments;
            var args = ImmutableArray.CreateBuilder<BoundExpression>(argSyntaxes.Count);
            for (int i = 0; i < argSyntaxes.Count; i++)
                args.Add(BindCallArgument(argSyntaxes[i], context, diagnostics));
            return new BoundUnboundImplicitObjectCreationExpression(node, args.ToImmutable());
        }
        private BoundExpression BindObjectCreation(ObjectCreationExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (node.Initializer is not null)
            {
                diagnostics.Add(new Diagnostic("CN_NEW000", DiagnosticSeverity.Error,
                    "Object/collection initializers are not supported.",
                    new Location(context.SemanticModel.SyntaxTree, node.Initializer.Span)));
            }
            var createdType = BindType(node.Type, context, diagnostics);
            if (createdType is not NamedTypeSymbol nt)
            {
                diagnostics.Add(new Diagnostic("CN_NEW001", DiagnosticSeverity.Error,
                    $"'{createdType.Name}' is not a constructible type.",
                    new Location(context.SemanticModel.SyntaxTree, node.Type.Span)));
                var bad = new BoundBadExpression(node);
                bad.SetType(createdType);
                return bad;
            }
            var argSyntaxes = node.ArgumentList?.Arguments ?? default;

            var args = ImmutableArray.CreateBuilder<BoundExpression>(argSyntaxes.Count);
            for (int i = 0; i < argSyntaxes.Count; i++)
                args.Add(BindCallArgument(argSyntaxes[i], context, diagnostics));
            return BindObjectCreationCoreFromBoundArgs(
                syntax: node,
                type: nt,
                argSyntaxes: argSyntaxes,
                boundArgs: args.ToImmutable(),
                diagnosticSpan: node.Type.Span,
                context: context,
                diagnostics: diagnostics);
        }
        private BoundExpression BindObjectCreationCoreFromBoundArgs(
            SyntaxNode syntax,
            NamedTypeSymbol type,
            SeparatedSyntaxList<ArgumentSyntax> argSyntaxes,
            ImmutableArray<BoundExpression> boundArgs,
            TextSpan diagnosticSpan,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var allCtorCandidates = LookupConstructors(type);
            var ctorCandidates = allCtorCandidates
                .Where(c => AccessibilityHelper.IsAccessible(c, context))
                .ToImmutableArray();

            if (type.TypeKind == TypeKind.Struct && boundArgs.Length == 0)
            {
                bool hasDeclaredParameterlessCtor = false;
                for (int i = 0; i < allCtorCandidates.Length; i++)
                {
                    if (allCtorCandidates[i].Parameters.Length == 0)
                    {
                        hasDeclaredParameterlessCtor = true;
                        break;
                    }
                }
                if (!hasDeclaredParameterlessCtor)
                    return new BoundObjectCreationExpression(syntax, type, constructorOpt: null, arguments: boundArgs);
            }
            if (ctorCandidates.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic("CN_NEW002", DiagnosticSeverity.Error,
                    $"No accessible constructor found for '{type.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticSpan)));
                var bad = new BoundBadExpression(syntax);
                bad.SetType(type);
                return bad;
            }
            if (!TryResolveOverload(
                candidates: ctorCandidates,
                args: boundArgs,
                getArgExprSyntax: i => argSyntaxes[i].Expression,
                getArgRefKindKeyword: i => argSyntaxes[i].RefKindKeyword,
                getArgName: i => argSyntaxes[i].NameColon?.Name.Identifier.ValueText,
                chosen: out var chosen,
                convertedArgs: out var convertedArgs,
                context: context,
                diagnostics: diagnostics,
                diagnosticNode: syntax))
            {
                var bad = new BoundBadExpression(syntax);
                bad.SetType(type);
                return bad;
            }
            return new BoundObjectCreationExpression(syntax, type, chosen!, convertedArgs);
        }
        private bool TryResolveOverload(
            ImmutableArray<MethodSymbol> candidates,
            ImmutableArray<BoundExpression> args,
            Func<int, ExpressionSyntax> getArgExprSyntax,
            out MethodSymbol? chosen,
            out ImmutableArray<BoundExpression> convertedArgs,
            BindingContext context,
            DiagnosticBag diagnostics,
            SyntaxNode diagnosticNode,
            Func<int, SyntaxToken?>? getArgRefKindKeyword = null,
            Func<int, string?>? getArgName = null)
        {
            chosen = null;
            convertedArgs = default;

            if (getArgName is not null)
            {
                bool sawNamed = false;
                var seenNames = new HashSet<string>(StringComparer.Ordinal);

                for (int i = 0; i < args.Length; i++)
                {
                    var n = getArgName(i);
                    if (!string.IsNullOrEmpty(n))
                    {
                        sawNamed = true;
                        if (!seenNames.Add(n!))
                        {
                            diagnostics.Add(new Diagnostic(
                                "CN_NAMEDARG002",
                                DiagnosticSeverity.Error,
                                $"Named argument '{n}' is specified multiple times.",
                                new Location(context.SemanticModel.SyntaxTree, getArgExprSyntax(i).Span)));
                            return false;
                        }
                    }
                    else if (sawNamed)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_NAMEDARG001",
                            DiagnosticSeverity.Error,
                            "Positional arguments cannot appear after named arguments.",
                            new Location(context.SemanticModel.SyntaxTree, getArgExprSyntax(i).Span)));
                        return false;
                    }
                }

                foreach (var name in seenNames)
                {
                    bool found = false;
                    for (int c = 0; c < candidates.Length && !found; c++)
                    {
                        var ps = candidates[c].Parameters;
                        for (int p = 0; p < ps.Length; p++)
                        {
                            if (string.Equals(ps[p].Name, name, StringComparison.Ordinal))
                            {
                                found = true;
                                break;
                            }
                        }
                    }

                    if (!found)
                    {
                        // Pick the first occurrence for location
                        for (int i = 0; i < args.Length; i++)
                        {
                            var n = getArgName(i);
                            if (string.Equals(n, name, StringComparison.Ordinal))
                            {
                                diagnostics.Add(new Diagnostic(
                                    "CN_NAMEDARG003",
                                    DiagnosticSeverity.Error,
                                    $"No parameter named '{name}' exists in any candidate overload.",
                                    new Location(context.SemanticModel.SyntaxTree, getArgExprSyntax(i).Span)));
                                break;
                            }
                        }
                        return false;
                    }
                }
            }

            MethodSymbol? best = null;
            bool bestUsesParamsExpansion = false;
            int bestScore = int.MaxValue;
            int[]? bestArgToParamMap = null;
            int[]? bestParamsElementArgIndices = null;
            bool ambiguous = false;

            bool allowErrorRecovery = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (ShouldSuppressCascade(args[i]))
                {
                    allowErrorRecovery = true;
                    break;
                }
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                var m = candidates[i];
                var ps = m.Parameters;

                // Regular form
                if (args.Length <= ps.Length)
                {
                    if (TryScoreRegular(m, args, getArgRefKindKeyword, getArgName, context, out int score, out var map))
                        ConsiderCandidate(m, usesParamsExpansion: false, score, map, paramsElementArgIndices: null);
                }

                // Params expansion form
                if (ps.Length > 0 && ps[^1].IsParams && ps[^1].Type is ArrayTypeSymbol at && at.Rank == 1)
                {
                    int fixedCount = ps.Length - 1;

                    if (TryScoreParamsExpanded(m, args, fixedCount, at.ElementType, getArgRefKindKeyword, getArgName,
                        context, out int score, out var map, out var paramsElementArgIndices))
                    {
                        // Penalize params-expansion so that non-expanded matches win when both are viable.
                        score += 5;
                        ConsiderCandidate(m, usesParamsExpansion: true, score, map, paramsElementArgIndices);
                    }
                }
            }

            if (best is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_OVL001",
                    DiagnosticSeverity.Error,
                    "No overload matches the argument list.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                return false;
            }

            if (ambiguous)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_OVL002",
                    DiagnosticSeverity.Error,
                    "Overload resolution is ambiguous.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                return false;
            }

            var chosenMap = bestArgToParamMap;
            if (chosenMap is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_OVL_INTERNAL001",
                    DiagnosticSeverity.Error,
                    "Internal error: overload resolution map is missing.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                return false;
            }

            var converted = new BoundExpression[best.Parameters.Length];
            if (!bestUsesParamsExpansion)
            {
                var ps = best.Parameters;
                var assigned = new bool[ps.Length];

                for (int a = 0; a < args.Length; a++)
                {
                    int p = chosenMap[a];
                    assigned[p] = true;

                    converted[p] = ApplyConversion(
                        exprSyntax: getArgExprSyntax(a),
                        expr: args[a],
                        targetType: ps[p].Type,
                        diagnosticNode: diagnosticNode,
                        context: context,
                        diagnostics: diagnostics,
                        requireImplicit: true);
                }

                for (int p = 0; p < ps.Length; p++)
                    if (!assigned[p])
                        converted[p] = CreateOmittedArgument(ps[p]);
            }
            else
            {
                var ps = best.Parameters;
                int fixedCount = ps.Length - 1;
                int paramsIndex = ps.Length - 1;

                var paramsParam = ps[paramsIndex];
                var paramsArrayType = (ArrayTypeSymbol)paramsParam.Type;
                var elementType = paramsArrayType.ElementType;

                // Figure out which arg supplies each fixed parameter
                var fixedArgIndex = new int[fixedCount];
                for (int p = 0; p < fixedCount; p++)
                    fixedArgIndex[p] = -1;

                for (int a = 0; a < args.Length; a++)
                {
                    int p = chosenMap[a];
                    if ((uint)p < (uint)fixedCount)
                        fixedArgIndex[p] = a;
                }

                // Fixed parameters
                for (int p = 0; p < fixedCount; p++)
                {
                    int a = fixedArgIndex[p];
                    if (a >= 0)
                    {
                        converted[p] = ApplyConversion(
                            exprSyntax: getArgExprSyntax(a),
                            expr: args[a],
                            targetType: ps[p].Type,
                            diagnosticNode: diagnosticNode,
                            context: context,
                            diagnostics: diagnostics,
                            requireImplicit: true);
                    }
                    else
                    {
                        converted[p] = CreateOmittedArgument(ps[p]);
                    }
                }

                var paramElems = bestParamsElementArgIndices ?? Array.Empty<int>();
                int elemCount = paramElems.Length;
                var int32Type = context.Compilation.GetSpecialType(SpecialType.System_Int32);

                var arrLocal = NewTemp("<params$>", paramsArrayType);
                var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>(2 + elemCount);

                // arr = new T[elemCount]
                var countLit = new BoundLiteralExpression(diagnosticNode, int32Type, elemCount);
                var newArr = new BoundArrayCreationExpression(diagnosticNode, paramsArrayType, elementType, countLit, initializerOpt: null);

                sideEffects.Add(new BoundExpressionStatement(
                    diagnosticNode,
                    new BoundAssignmentExpression(
                        diagnosticNode,
                        new BoundLocalExpression(diagnosticNode, arrLocal),
                        newArr)));

                // arr[i] = (T)arg
                for (int i = 0; i < elemCount; i++)
                {
                    int argIndex = paramElems[i];

                    var idxLit = new BoundLiteralExpression(diagnosticNode, int32Type, i);
                    var elemAccess = new BoundArrayElementAccessExpression(
                        diagnosticNode,
                        elementType,
                        new BoundLocalExpression(diagnosticNode, arrLocal),
                        idxLit);

                    var elemValue = ApplyConversion(
                        exprSyntax: getArgExprSyntax(argIndex),
                        expr: args[argIndex],
                        targetType: elementType,
                        diagnosticNode: diagnosticNode,
                        context: context,
                        diagnostics: diagnostics,
                        requireImplicit: true);

                    sideEffects.Add(new BoundExpressionStatement(
                        diagnosticNode,
                        new BoundAssignmentExpression(diagnosticNode, elemAccess, elemValue)));
                }

                var packedArray = new BoundSequenceExpression(
                    diagnosticNode,
                    locals: ImmutableArray.Create(arrLocal),
                    sideEffects: sideEffects.ToImmutable(),
                    value: new BoundLocalExpression(diagnosticNode, arrLocal));

                converted[paramsIndex] = packedArray;
            }

            chosen = best;
            convertedArgs = ImmutableArray.Create(converted);
            return true;

            void ConsiderCandidate(MethodSymbol m, bool usesParamsExpansion, int score, int[] argToParamMap, int[]? paramsElementArgIndices)
            {
                if (score < bestScore)
                {
                    bestScore = score;
                    best = m;
                    bestUsesParamsExpansion = usesParamsExpansion;
                    bestArgToParamMap = argToParamMap;
                    bestParamsElementArgIndices = paramsElementArgIndices;
                    ambiguous = false;
                    return;
                }

                if (score != bestScore)
                    return;

                if (ReferenceEquals(best, m))
                {
                    if (bestUsesParamsExpansion && !usesParamsExpansion)
                    {
                        bestUsesParamsExpansion = false;
                        bestArgToParamMap = argToParamMap;
                        bestParamsElementArgIndices = paramsElementArgIndices;
                        ambiguous = false;
                    }
                    return;
                }

                ambiguous = true;
            }

            bool TryScoreRegular(
                MethodSymbol m,
                ImmutableArray<BoundExpression> args,
                Func<int, SyntaxToken?>? getArgRefKindKeyword,
                Func<int, string?>? getArgName,
                BindingContext context,
                out int score,
                out int[] argToParamMap)
            {
                score = 0;
                argToParamMap = Array.Empty<int>();

                var ps = m.Parameters;
                if (!TryBuildRegularArgMap(ps, args.Length, getArgName, out var map, out var assigned))
                    return false;

                for (int p = 0; p < ps.Length; p++)
                {
                    if (!assigned[p] && !IsOmittable(ps[p]))
                        return false;
                }

                for (int a = 0; a < args.Length; a++)
                {
                    if (allowErrorRecovery && ShouldSuppressCascade(args[a]))
                        continue;

                    int p = map[a];

                    if (getArgRefKindKeyword is not null &&
                        !ArgumentRefKindMatchesParameter(getArgRefKindKeyword(a), ps[p]))
                    {
                        return false;
                    }

                    var conv = ClassifyConversion(args[a], ps[p].Type, context);
                    if (!conv.Exists || !conv.IsImplicit)
                        return false;

                    score += ConversionScore(conv.Kind);
                }

                argToParamMap = map;
                return true;
            }
            bool TryScoreParamsExpanded(
        MethodSymbol m,
        ImmutableArray<BoundExpression> args,
        int fixedCount,
        TypeSymbol elementType,
        Func<int, SyntaxToken?>? getArgRefKindKeyword,
        Func<int, string?>? getArgName,
        BindingContext context,
        out int score,
        out int[] argToParamMap,
        out int[] paramsElementArgIndices)
            {
                score = 0;
                argToParamMap = Array.Empty<int>();
                paramsElementArgIndices = Array.Empty<int>();

                var ps = m.Parameters;
                if (ps.Length == 0)
                    return false;

                if (!TryBuildParamsExpandedArgMap(ps, args.Length, fixedCount, getArgName,
                    out var map, out var fixedAssigned, out var elems))
                {
                    return false;
                }

                // Any fixed parameter not assigned by an argument must have a default.
                for (int p = 0; p < fixedCount; p++)
                {
                    if (!fixedAssigned[p] && !ps[p].HasExplicitDefault)
                        return false;
                }

                int paramsIndex = ps.Length - 1;

                for (int a = 0; a < args.Length; a++)
                {
                    if (allowErrorRecovery && ShouldSuppressCascade(args[a]))
                        continue;

                    int p = map[a];

                    if (p != paramsIndex)
                    {
                        if (getArgRefKindKeyword is not null &&
                            !ArgumentRefKindMatchesParameter(getArgRefKindKeyword(a), ps[p]))
                        {
                            return false;
                        }

                        var conv = ClassifyConversion(args[a], ps[p].Type, context);
                        if (!conv.Exists || !conv.IsImplicit)
                            return false;

                        score += ConversionScore(conv.Kind);
                    }
                    else
                    {
                        if (getArgRefKindKeyword is not null &&
                            GetArgRefKind(getArgRefKindKeyword(a)) != ParameterRefKind.None)
                        {
                            return false;
                        }

                        var conv = ClassifyConversion(args[a], elementType, context);
                        if (!conv.Exists || !conv.IsImplicit)
                            return false;

                        score += ConversionScore(conv.Kind);
                    }
                }

                argToParamMap = map;
                paramsElementArgIndices = elems;
                return true;
            }

            static bool TryBuildRegularArgMap(
                ImmutableArray<ParameterSymbol> parameters,
                int argCount,
                Func<int, string?>? getArgName,
                out int[] map,
                out bool[] assigned)
            {
                map = new int[argCount];
                assigned = new bool[parameters.Length];

                // Fast path for purely positional
                if (getArgName is null)
                {
                    for (int i = 0; i < argCount; i++)
                    {
                        map[i] = i;
                        if ((uint)i < (uint)assigned.Length)
                            assigned[i] = true;
                    }
                    return true;
                }

                int nextPositional = 0;

                for (int a = 0; a < argCount; a++)
                {
                    var name = getArgName(a);
                    if (!string.IsNullOrEmpty(name))
                    {
                        int p = IndexOfParameter(parameters, name!);
                        if (p < 0) return false;
                        if (assigned[p]) return false;

                        assigned[p] = true;
                        map[a] = p;
                        continue;
                    }

                    while (nextPositional < parameters.Length && assigned[nextPositional])
                        nextPositional++;

                    if (nextPositional >= parameters.Length)
                        return false;

                    assigned[nextPositional] = true;
                    map[a] = nextPositional;
                    nextPositional++;
                }

                return true;
            }

            static bool TryBuildParamsExpandedArgMap(
                ImmutableArray<ParameterSymbol> parameters,
                int argCount,
                int fixedCount,
                Func<int, string?>? getArgName,
                out int[] map,
                out bool[] fixedAssigned,
                out int[] paramsElementArgIndices)
            {
                map = new int[argCount];
                fixedAssigned = new bool[Math.Max(0, fixedCount)];
                paramsElementArgIndices = Array.Empty<int>();
                int paramsIndex = parameters.Length - 1;

                // Fast path for positional
                if (getArgName is null)
                {
                    var elems = new int[Math.Max(0, argCount - fixedCount)];
                    int e = 0;

                    for (int a = 0; a < argCount; a++)
                    {
                        if (a < fixedCount)
                        {
                            map[a] = a;
                            fixedAssigned[a] = true;
                        }
                        else
                        {
                            map[a] = paramsIndex;
                            elems[e++] = a;
                        }
                    }

                    paramsElementArgIndices = elems;
                    return true;
                }

                var elemList = new List<int>();
                int nextPositional = 0;

                for (int a = 0; a < argCount; a++)
                {
                    var name = getArgName(a);
                    if (!string.IsNullOrEmpty(name))
                    {
                        int p = IndexOfParameter(parameters, name!);
                        if (p < 0) return false;

                        if (p != paramsIndex)
                        {
                            if ((uint)p >= (uint)fixedCount) return false;
                            if (fixedAssigned[p]) return false;

                            fixedAssigned[p] = true;
                            map[a] = p;
                        }
                        else
                        {
                            map[a] = paramsIndex;
                            elemList.Add(a);
                        }

                        continue;
                    }

                    while (nextPositional < fixedCount && fixedAssigned[nextPositional])
                        nextPositional++;

                    if (nextPositional < fixedCount)
                    {
                        fixedAssigned[nextPositional] = true;
                        map[a] = nextPositional;
                        nextPositional++;
                    }
                    else
                    {
                        map[a] = paramsIndex;
                        elemList.Add(a);
                    }
                }

                paramsElementArgIndices = elemList.Count == 0 ? Array.Empty<int>() : elemList.ToArray();
                return true;
            }

            static int IndexOfParameter(ImmutableArray<ParameterSymbol> parameters, string name)
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (string.Equals(parameters[i].Name, name, StringComparison.Ordinal))
                        return i;
                }
                return -1;
            }

            static bool IsOmittable(ParameterSymbol p)
                => p.IsParams || p.HasExplicitDefault;

            BoundExpression CreateOmittedArgument(ParameterSymbol p)
            {
                if (p.IsParams)
                {
                    if (p.Type is not ArrayTypeSymbol at || at.Rank != 1)
                    {
                        var bad = new BoundBadExpression(diagnosticNode);
                        bad.SetType(p.Type);
                        return bad;
                    }

                    var int32Type = context.Compilation.GetSpecialType(SpecialType.System_Int32);
                    var zero = new BoundLiteralExpression(diagnosticNode, int32Type, 0);
                    return new BoundArrayCreationExpression(diagnosticNode, at, at.ElementType, zero, initializerOpt: null);
                }

                if (!p.HasExplicitDefault || !p.DefaultValueOpt.HasValue)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_OVL003",
                        DiagnosticSeverity.Error,
                        $"Missing argument for parameter '{p.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                    var bad = new BoundBadExpression(diagnosticNode);
                    bad.SetType(p.Type);
                    return bad;
                }

                return new BoundLiteralExpression(diagnosticNode, p.Type, p.DefaultValueOpt.Value);
            }
            static int ConversionScore(ConversionKind k) => k switch
            {
                ConversionKind.Identity => 0,
                ConversionKind.ImplicitStackAlloc => 1,
                ConversionKind.ImplicitNumeric => 1,
                ConversionKind.ImplicitConstant => 1,
                ConversionKind.ImplicitReference => 1,
                ConversionKind.ImplicitTuple => 1,
                ConversionKind.ImplicitNullable => 1,
                ConversionKind.NullLiteral => 1,
                ConversionKind.Boxing => 2,
                ConversionKind.UserDefined => 3,
                _ => 10
            };
        }
        private bool TryBindUserDefinedUnaryOperator(
            ExpressionSyntax operatorSyntax,
            ExpressionSyntax operandSyntax,
            BoundUnaryOperatorKind op,
            BoundExpression operand,
            BindingContext context,
            DiagnosticBag diagnostics,
            out BoundExpression result)
        {
            result = new BoundBadExpression(operatorSyntax);

            var names = GetUnaryOperatorMetadataNames(op, IsCheckedOverflowContext);
            if (names.IsDefaultOrEmpty)
                return false;

            var candidates = LookupUserDefinedOperatorMethods(
                leftType: operand.Type,
                rightType: null,
                metadataNames: names,
                parameterCount: 1,
                context: context);

            if (candidates.IsDefaultOrEmpty)
                return false;

            var args = ImmutableArray.Create(operand);
            if (!TryResolveOverload(
                    candidates: candidates,
                    args: args,
                    getArgExprSyntax: _ => operandSyntax,
                    chosen: out var chosen,
                    convertedArgs: out var convertedArgs,
                    context: context,
                    diagnostics: diagnostics,
                    diagnosticNode: operatorSyntax))
            {
                return true;
            }

            if (chosen!.ReturnType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_UOP001",
                    DiagnosticSeverity.Error,
                    $"Operator method '{chosen.Name}' cannot return void.",
                    new Location(context.SemanticModel.SyntaxTree, operatorSyntax.Span)));
                return true;
            }

            result = new BoundCallExpression(operatorSyntax, receiverOpt: null, chosen, convertedArgs);
            return true;
        }
        private bool TryBindUserDefinedBinaryOperator(
            ExpressionSyntax operatorSyntax,
            ExpressionSyntax leftSyntax,
            BoundExpression left,
            ExpressionSyntax rightSyntax,
            BoundExpression right,
            BoundBinaryOperatorKind op,
            BindingContext context,
            DiagnosticBag diagnostics,
            out BoundExpression result,
            bool requireBooleanReturn = false)
        {
            result = new BoundBadExpression(operatorSyntax);

            var names = GetBinaryOperatorMetadataNames(op, IsCheckedOverflowContext);
            if (names.IsDefaultOrEmpty)
                return false;

            var candidates = LookupUserDefinedOperatorMethods(
                leftType: left.Type,
                rightType: right.Type,
                metadataNames: names,
                parameterCount: 2,
                context: context);

            if (candidates.IsDefaultOrEmpty)
                return false;

            var args = ImmutableArray.Create(left, right);
            if (!TryResolveOverload(
                candidates: candidates,
                args: args,
                getArgExprSyntax: i => i == 0 ? leftSyntax : rightSyntax,
                chosen: out var chosen,
                convertedArgs: out var convertedArgs,
                context: context,
                diagnostics: diagnostics,
                diagnosticNode: operatorSyntax))
            {
                return true;
            }

            if (chosen!.ReturnType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_BOP001",
                    DiagnosticSeverity.Error,
                    $"Operator method '{chosen.Name}' cannot return void.",
                    new Location(context.SemanticModel.SyntaxTree, operatorSyntax.Span)));
                return true;
            }

            if (requireBooleanReturn && chosen.ReturnType.SpecialType != SpecialType.System_Boolean)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_BOP002",
                    DiagnosticSeverity.Error,
                    $"Operator method '{chosen.Name}' must return 'bool' for this operator.",
                    new Location(context.SemanticModel.SyntaxTree, operatorSyntax.Span)));
                return true;
            }

            result = new BoundCallExpression(operatorSyntax, receiverOpt: null, chosen, convertedArgs);
            return true;
        }
        private bool TryBindDirectCompoundAssignmentOperator(
            AssignmentExpressionSyntax node,
            BoundBinaryOperatorKind op,
            BoundExpression leftTarget,
            BoundExpression right,
            BindingContext context,
            DiagnosticBag diagnostics,
            out BoundExpression result,
            out MethodSymbol? method)
        {
            result = new BoundBadExpression(node);
            method = null;

            var names = GetCompoundAssignmentOperatorMetadataNames(op, IsCheckedOverflowContext);
            if (names.IsDefaultOrEmpty)
                return false;

            var candidates = LookupInstanceUserDefinedOperatorMethods(
                receiverType: leftTarget.Type,
                metadataNames: names,
                parameterCount: 1,
                context: context);

            if (candidates.IsDefaultOrEmpty)
                return false;

            var overloadDiags = new DiagnosticBag();
            if (!TryResolveOverload(
                candidates: candidates,
                args: ImmutableArray.Create(right),
                getArgExprSyntax: _ => node.Right,
                chosen: out var chosen,
                convertedArgs: out var convertedArgs,
                context: context,
                diagnostics: overloadDiags,
                diagnosticNode: node))
            {
                if (IsOnlyNoApplicableOverload(overloadDiags))
                    return false;

                foreach (var d in overloadDiags.ToImmutable())
                    diagnostics.Add(d);
                return true;
            }

            if (chosen!.ReturnType.SpecialType != SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CASG_OP002",
                    DiagnosticSeverity.Error,
                    $"Direct compound operator '{chosen.Name}' must return void.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return true;
            }

            method = chosen;
            result = new BoundCallExpression(node, receiverOpt: leftTarget, chosen, convertedArgs);
            return true;

        }
        private static bool IsOnlyNoApplicableOverload(DiagnosticBag diagnostics)
        {
            var items = diagnostics.ToImmutable();
            if (items.IsDefaultOrEmpty)
                return false;

            for (int i = 0; i < items.Length; i++)
                if (!string.Equals(items[i].Id, "CN_OVL001", StringComparison.Ordinal))
                    return false;

            return true;
        }
        private bool TryBindUserDefinedCompoundAssignmentOperator(
            AssignmentExpressionSyntax node,
            BoundBinaryOperatorKind op,
            BoundExpression left,
            BoundExpression right,
            BindingContext context,
            DiagnosticBag diagnostics,
            out BoundExpression result)
        {
            result = new BoundBadExpression(node);

            var names = GetCompoundAssignmentOperatorMetadataNames(op, IsCheckedOverflowContext);
            if (names.IsDefaultOrEmpty)
                return false;

            var candidates = LookupUserDefinedOperatorMethods(
                leftType: left.Type,
                rightType: right.Type,
                metadataNames: names,
                parameterCount: 2,
                context: context);

            if (candidates.IsDefaultOrEmpty)
                return false;

            var args = ImmutableArray.Create(left, right);
            if (!TryResolveOverload(
                candidates: candidates,
                args: args,
                getArgExprSyntax: i => i == 0 ? node.Left : node.Right,
                chosen: out var chosen,
                convertedArgs: out var convertedArgs,
                context: context,
                diagnostics: diagnostics,
                diagnosticNode: node))
            {
                return true;
            }

            if (chosen!.ReturnType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CASG_OP001",
                    DiagnosticSeverity.Error,
                    $"Operator method '{chosen.Name}' cannot return void in compound assignment.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return true;
            }

            result = new BoundCallExpression(node, receiverOpt: null, chosen, convertedArgs);
            return true;
        }
        private bool TryBindDirectIncrementDecrementOperator(
            ExpressionSyntax operatorSyntax,
            BoundExpression target,
            bool isIncrement,
            BindingContext context,
            DiagnosticBag diagnostics,
            out BoundExpression result,
            out MethodSymbol? method)
        {
            result = new BoundBadExpression(operatorSyntax);
            method = null;

            var names = IsCheckedOverflowContext
                ? ImmutableArray.Create(
                    isIncrement ? "op_CheckedIncrementAssignment" : "op_CheckedDecrementAssignment",
                    isIncrement ? "op_IncrementAssignment" : "op_DecrementAssignment")
                : ImmutableArray.Create(
                    isIncrement ? "op_IncrementAssignment" : "op_DecrementAssignment");

            var candidates = LookupInstanceUserDefinedOperatorMethods(
                receiverType: target.Type,
                metadataNames: names,
                parameterCount: 0,
                context: context);

            if (candidates.IsDefaultOrEmpty)
                return false;

            var overloadDiags = new DiagnosticBag();
            if (!TryResolveOverload(
                candidates: candidates,
                args: ImmutableArray<BoundExpression>.Empty,
                getArgExprSyntax: _ => operatorSyntax,
                chosen: out var chosen,
                convertedArgs: out var convertedArgs,
                context: context,
                diagnostics: overloadDiags,
                diagnosticNode: operatorSyntax))
            {
                if (IsOnlyNoApplicableOverload(overloadDiags))
                    return false;

                foreach (var d in overloadDiags.ToImmutable())
                    diagnostics.Add(d);
                return true;
            }

            if (chosen!.ReturnType.SpecialType != SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_INCDEC002",
                    DiagnosticSeverity.Error,
                    $"Direct increment/decrement operator '{chosen.Name}' must return void.",
                    new Location(context.SemanticModel.SyntaxTree, operatorSyntax.Span)));
                return true;
            }

            method = chosen;
            result = new BoundCallExpression(operatorSyntax, receiverOpt: target, chosen, convertedArgs);
            return true;
        }
        private bool TryBindUserDefinedIncrementDecrementOperator(
            ExpressionSyntax operatorSyntax,
            ExpressionSyntax operandSyntax,
            BoundExpression operand,
            bool isIncrement,
            BindingContext context,
            DiagnosticBag diagnostics,
            out BoundExpression result)
        {
            result = new BoundBadExpression(operatorSyntax);

            var names = IsCheckedOverflowContext
                ? ImmutableArray.Create(
                    isIncrement ? "op_CheckedIncrement" : "op_CheckedDecrement",
                    isIncrement ? "op_Increment" : "op_Decrement")
                : ImmutableArray.Create(
                    isIncrement ? "op_Increment" : "op_Decrement");

            var candidates = LookupUserDefinedOperatorMethods(
                leftType: operand.Type,
                rightType: null,
                metadataNames: names,
                parameterCount: 1,
                context: context);

            if (candidates.IsDefaultOrEmpty)
                return false;

            var args = ImmutableArray.Create(operand);
            if (!TryResolveOverload(
                candidates: candidates,
                args: args,
                getArgExprSyntax: _ => operandSyntax,
                chosen: out var chosen,
                convertedArgs: out var convertedArgs,
                context: context,
                diagnostics: diagnostics,
                diagnosticNode: operatorSyntax))
            {
                return true;
            }

            if (chosen!.ReturnType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_INCDEC001",
                    DiagnosticSeverity.Error,
                    $"Operator method '{chosen.Name}' cannot return void.",
                    new Location(context.SemanticModel.SyntaxTree, operatorSyntax.Span)));
                return true;
            }

            result = new BoundCallExpression(operatorSyntax, receiverOpt: null, chosen, convertedArgs);
            return true;
        }
        private ImmutableArray<MethodSymbol> LookupInstanceUserDefinedOperatorMethods(
    TypeSymbol receiverType,
    ImmutableArray<string> metadataNames,
    int parameterCount,
    BindingContext context)
        {
            var types = new List<NamedTypeSymbol>();
            var seenTypes = new HashSet<NamedTypeSymbol>();

            if (receiverType is NamedTypeSymbol nt)
            {
                for (NamedTypeSymbol? cur = nt; cur is not null; cur = cur.BaseType as NamedTypeSymbol)
                {
                    if (seenTypes.Add(cur))
                        types.Add(cur);
                }
            }

            var methods = ImmutableArray.CreateBuilder<MethodSymbol>();
            foreach (var t in types)
            {
                foreach (var m in t.GetMembers())
                {
                    if (m is not MethodSymbol ms)
                        continue;
                    if (ms.IsStatic || ms.IsConstructor)
                        continue;
                    if (ms.Parameters.Length != parameterCount)
                        continue;
                    if (!ContainsName(ms.Name, metadataNames))
                        continue;
                    if (!AccessibilityHelper.IsAccessible(ms, context))
                        continue;

                    methods.Add(ms);
                }
            }

            return methods.ToImmutable();

            static bool ContainsName(string name, ImmutableArray<string> names)
            {
                for (int i = 0; i < names.Length; i++)
                    if (string.Equals(name, names[i], StringComparison.Ordinal))
                        return true;
                return false;
            }
        }
        private ImmutableArray<MethodSymbol> LookupUserDefinedOperatorMethods(
            TypeSymbol leftType,
            TypeSymbol? rightType,
            ImmutableArray<string> metadataNames,
            int parameterCount,
            BindingContext context)
        {
            var types = new List<NamedTypeSymbol>();
            var seenTypes = new HashSet<NamedTypeSymbol>();

            AddTypeAndBases(leftType);
            if (rightType is not null)
                AddTypeAndBases(rightType);

            var methods = ImmutableArray.CreateBuilder<MethodSymbol>();
            foreach (var t in types)
            {
                foreach (var m in t.GetMembers())
                {
                    if (m is not MethodSymbol ms)
                        continue;
                    if (!ms.IsStatic || ms.IsConstructor)
                        continue;
                    if (ms.Parameters.Length != parameterCount)
                        continue;
                    if (!ContainsName(ms.Name, metadataNames))
                        continue;
                    if (!AccessibilityHelper.IsAccessible(ms, context))
                        continue;

                    methods.Add(ms);
                }
            }

            return methods.ToImmutable();

            void AddTypeAndBases(TypeSymbol type)
            {
                if (type is not NamedTypeSymbol nt)
                    return;

                for (NamedTypeSymbol? cur = nt; cur is not null; cur = cur.BaseType as NamedTypeSymbol)
                {
                    if (seenTypes.Add(cur))
                        types.Add(cur);
                }
            }

            static bool ContainsName(string name, ImmutableArray<string> names)
            {
                for (int i = 0; i < names.Length; i++)
                    if (string.Equals(name, names[i], StringComparison.Ordinal))
                        return true;
                return false;
            }
        }

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

            static bool HasImplicitReferenceConversion(TypeSymbol from, TypeSymbol to)
            {
                if (!from.IsReferenceType || !to.IsReferenceType) return false;
                for (var t = from; t != null; t = t.BaseType)
                    if (ReferenceEquals(t, to)) return true;
                return false;
            }
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
                SpecialType.System_IntPtr or SpecialType.System_UIntPtr => RuntimeTypeSystem.PointerSize == 4 ? 0x1F : 0x3F,
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
                    if (RuntimeTypeSystem.PointerSize == 4 && lv is int ni32)
                        return new Optional<object>(op switch
                        {
                            BoundBinaryOperatorKind.LeftShift => unchecked(ni32 << shift),
                            BoundBinaryOperatorKind.RightShift => ni32 >> shift,
                            BoundBinaryOperatorKind.UnsignedRightShift => (int)((uint)ni32 >> shift),
                            _ => 0
                        });
                    if (RuntimeTypeSystem.PointerSize == 8 && (lv is long or int))
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
                    if (RuntimeTypeSystem.PointerSize == 4 && lv is uint nu32)
                        return new Optional<object>(op switch
                        {
                            BoundBinaryOperatorKind.LeftShift => unchecked(nu32 << shift),
                            BoundBinaryOperatorKind.RightShift or BoundBinaryOperatorKind.UnsignedRightShift => nu32 >> shift,
                            _ => 0u
                        });
                    if (RuntimeTypeSystem.PointerSize == 8 && (lv is ulong or uint))
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
                case BoundIsPatternExpression isPattern when isPattern.DeclaredLocalOpt is not null && !isPattern.IsDiscard:
                    AddUniqueLocal(builder, isPattern.DeclaredLocalOpt);
                    return;

                case BoundBinaryExpression bin when bin.OperatorKind == BoundBinaryOperatorKind.LogicalAnd:
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

        private ImmutableArray<LocalSymbol> GetPatternLocalsWhenTrue(BoundExpression condition)
        {
            var builder = ImmutableArray.CreateBuilder<LocalSymbol>();
            CollectPatternLocalsWhenTrue(condition, builder);
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
        private static bool IsExtensionMethod(MethodSymbol method)
        {
            if (method is null || !method.IsStatic || method.Parameters.Length == 0)
                return false;

            return method.IsExtensionMethod;
        }
        private BoundStatement BindLocalFunctionStatement(
            LocalFunctionStatementSyntax lf,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var name = lf.Identifier.ValueText ?? "";

            if (!_localFunctions.TryGetValue(name, out var sym))
                sym = DeclareLocalFunction(lf, context, diagnostics);

            var bodyFlags = Flags;
            if (HasModifier(lf.Modifiers, SyntaxKind.UnsafeKeyword))
                bodyFlags |= BinderFlags.UnsafeRegion;

            var bodyBinder = new LocalScopeBinder(
                parent: this,
                flags: bodyFlags,
                containing: sym,
                inheritFlowFromParent: false);

            var bodyCtx = new BindingContext(context.Compilation, context.SemanticModel, sym, context.Recorder);

            BoundStatement body;
            if (lf.Body != null)
            {
                body = bodyBinder.BindStatement(lf.Body, bodyCtx, diagnostics);
            }
            else if (lf.ExpressionBody != null)
            {
                var expr = bodyBinder.BindExpression(lf.ExpressionBody.Expression, bodyCtx, diagnostics);

                if (sym.ReturnType.SpecialType == SpecialType.System_Void)
                {
                    body = new BoundBlockStatement(lf,
                        ImmutableArray.Create<BoundStatement>(
                            new BoundExpressionStatement(lf.ExpressionBody, expr)));
                }
                else
                {
                    expr = bodyBinder.ApplyConversion(
                        exprSyntax: lf.ExpressionBody.Expression,
                        expr: expr,
                        targetType: sym.ReturnType,
                        diagnosticNode: lf,
                        context: bodyCtx,
                        diagnostics: diagnostics,
                        requireImplicit: true);

                    body = new BoundBlockStatement(lf,
                        ImmutableArray.Create<BoundStatement>(
                            new BoundReturnStatement(lf.ExpressionBody, expr)));
                }
            }
            else
            {
                diagnostics.Add(new Diagnostic("CN_LFUNC007", DiagnosticSeverity.Error,
                    "Local function must have a body.",
                    new Location(context.SemanticModel.SyntaxTree, lf.Span)));
                body = new BoundBadStatement(lf);
            }

            return new BoundLocalFunctionStatement(lf, sym, body);
        }
        private BoundStatement BindLocalDeclaration(LocalDeclarationStatementSyntax ld, BindingContext context, DiagnosticBag diagnostics)
        {
            var decl = ld.Declaration;

            var isConst = HasModifier(ld.Modifiers, SyntaxKind.ConstKeyword);
            var isRefLocal = HasModifier(ld.Modifiers, SyntaxKind.RefKeyword);
            var isVar = IsVar(decl.Type);

            if (decl.Variables.Count == 0)
                return new BoundBadStatement(ld);

            if (isConst && isVar)
            {
                diagnostics.Add(new Diagnostic("CN_CONST000", DiagnosticSeverity.Error,
                    "A const local must have an explicit type (const var is not allowed).",
                    new Location(context.SemanticModel.SyntaxTree, decl.Type.Span)));
            }
            if (isConst && isRefLocal)
            {
                diagnostics.Add(new Diagnostic("CN_REFLOC000", DiagnosticSeverity.Error,
                    "A ref local cannot be const.",
                    new Location(context.SemanticModel.SyntaxTree, ld.Span)));
            }

            if (isRefLocal && decl.Variables.Count != 1)
            {
                diagnostics.Add(new Diagnostic("CN_REFLOC001", DiagnosticSeverity.Error,
                    "A ref local declaration must declare exactly one variable.",
                    new Location(context.SemanticModel.SyntaxTree, decl.Span)));
            }

            TypeSymbol? explicitType = null;
            if (!isVar)
                explicitType = BindType(decl.Type, context, diagnostics);

            if (decl.Variables.Count == 1)
                return BindSingleDeclarator(ld, decl.Variables[0], isVar, explicitType, isRefLocal, isConst, context, diagnostics);

            var list = ImmutableArray.CreateBuilder<BoundStatement>(decl.Variables.Count);
            for (int i = 0; i < decl.Variables.Count; i++)
                list.Add(BindSingleDeclarator(ld, decl.Variables[i], isVar, explicitType, isRefLocal, isConst, context, diagnostics));

            return new BoundStatementList(ld, list.ToImmutable());
        }
        private BoundStatement BindSingleDeclarator(
            SyntaxNode ownerSyntax,
            VariableDeclaratorSyntax v,
            bool isVar,
            TypeSymbol? explicitType,
            bool isRefLocal,
            bool isConst,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var name = v.Identifier.ValueText ?? "";

            if (IsNameDeclaredInEnclosingScopes(name))
            {
                diagnostics.Add(new Diagnostic("CN_LOCAL001", DiagnosticSeverity.Error,
                    $"A local named '{name}' is already declared in this scope.",
                    new Location(context.SemanticModel.SyntaxTree, v.Span)));
            }

            if (isConst && v.Initializer is null)
            {
                diagnostics.Add(new Diagnostic("CN_CONST001", DiagnosticSeverity.Error,
                    "A const local must be initialized.",
                    new Location(context.SemanticModel.SyntaxTree, v.Span)));
            }

            BoundExpression? init = null;
            TypeSymbol localType;
            if (isRefLocal)
            {
                if (v.Initializer is null)
                {
                    diagnostics.Add(new Diagnostic("CN_REFLOC002", DiagnosticSeverity.Error,
                        "A ref local must be initialized.",
                        new Location(context.SemanticModel.SyntaxTree, v.Span)));

                    localType = explicitType ?? new ErrorTypeSymbol("ref", containing: null, ImmutableArray<Location>.Empty);
                    init = null;
                }
                else
                {
                    var rhs = BindExpression(v.Initializer.Value, context, diagnostics);

                    if (rhs is not BoundRefExpression br || br.Type is not ByRefTypeSymbol brType)
                    {
                        diagnostics.Add(new Diagnostic("CN_REFLOC003", DiagnosticSeverity.Error,
                            "A ref local initializer must be a ref expression.",
                            new Location(context.SemanticModel.SyntaxTree, v.Initializer.Value.Span)));

                        localType = explicitType ?? new ErrorTypeSymbol("ref", containing: null, ImmutableArray<Location>.Empty);
                        init = rhs;
                    }
                    else if (isVar)
                    {
                        localType = brType.ElementType;
                        init = rhs;
                    }
                    else
                    {
                        localType = explicitType ?? new ErrorTypeSymbol("type", containing: null, ImmutableArray<Location>.Empty);

                        if (!ReferenceEquals(brType.ElementType, localType))
                        {
                            diagnostics.Add(new Diagnostic("CN_REFLOC004", DiagnosticSeverity.Error,
                                $"Cannot initialize ref local of type '{localType.Name}' with ref value of type '{brType.ElementType.Name}'.",
                                new Location(context.SemanticModel.SyntaxTree, v.Initializer.Value.Span)));
                        }

                        init = rhs;
                    }
                }
                var refLocal = new LocalSymbol(
                    name: name,
                    containing: _containing,
                    type: localType,
                    locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, v.Span)),
                    isConst: false,
                    constantValueOpt: Optional<object>.None,
                    isByRef: true);

                _locals[name] = refLocal;
                context.Recorder.RecordDeclared(v, refLocal);

                return new BoundLocalDeclarationStatement(ownerSyntax, refLocal, init);
            }
            if (isVar)
            {
                if (v.Initializer is null)
                {
                    diagnostics.Add(new Diagnostic("CN_VAR001", DiagnosticSeverity.Error,
                        "Implicitly-typed local variable must be initialized.",
                        new Location(context.SemanticModel.SyntaxTree, v.Span)));

                    localType = new ErrorTypeSymbol("var", containing: null, ImmutableArray<Location>.Empty);
                }
                else
                {
                    var rhs = BindExpression(v.Initializer.Value, context, diagnostics);

                    if (rhs.Type is NullTypeSymbol ||
                        (rhs.ConstantValueOpt.HasValue && rhs.ConstantValueOpt.Value is null))
                    {
                        diagnostics.Add(new Diagnostic("CN_VAR002", DiagnosticSeverity.Error,
                            "Cannot assign <null> to an implicitly-typed local variable.",
                            new Location(context.SemanticModel.SyntaxTree, v.Initializer.Value.Span)));

                        localType = new ErrorTypeSymbol("var", containing: null, ImmutableArray<Location>.Empty);
                        init = rhs;
                    }
                    else if (rhs.Type.SpecialType == SpecialType.System_Void)
                    {
                        diagnostics.Add(new Diagnostic("CN_VAR003", DiagnosticSeverity.Error,
                            "Cannot assign void to an implicitly-typed local variable.",
                            new Location(context.SemanticModel.SyntaxTree, v.Initializer.Value.Span)));

                        localType = new ErrorTypeSymbol("var", containing: null, ImmutableArray<Location>.Empty);
                        init = rhs;
                    }
                    else if (rhs.Type is TupleTypeSymbol tupleType && TupleHasUninferableElements(tupleType))
                    {
                        diagnostics.Add(new Diagnostic("CN_VAR_TUPLE000", DiagnosticSeverity.Error,
                            "Cannot infer the type of an implicitly-typed local variable from a tuple " +
                            "containing elements with no type. Add explicit casts.",
                            new Location(context.SemanticModel.SyntaxTree, v.Initializer.Value.Span)));

                        localType = new ErrorTypeSymbol("var", containing: null, ImmutableArray<Location>.Empty);
                        init = rhs;
                    }
                    else
                    {

                        if (rhs is BoundStackAllocArrayCreationExpression sa && (Flags & BinderFlags.UnsafeRegion) == 0 &&
                            TryGetSystemSpanDefinition(context.Compilation, out var spanDef))
                        {
                            localType = context.Compilation.ConstructNamedType(
                                spanDef,
                                ImmutableArray.Create<TypeSymbol>(sa.ElementType));
                            init = ApplyConversion(
                                exprSyntax: v.Initializer.Value,
                                expr: rhs,
                                targetType: localType,
                                diagnosticNode: v.Initializer,
                                context: context,
                                diagnostics: diagnostics,
                                requireImplicit: true);
                        }
                        else
                        {
                            if (rhs is BoundStackAllocArrayCreationExpression)
                                EnsureUnsafe(v.Initializer.Value, context, diagnostics);
                            localType = rhs.Type;
                            init = rhs;
                        }
                    }
                }
            }
            else
            {
                localType = explicitType ?? new ErrorTypeSymbol("type", containing: null, ImmutableArray<Location>.Empty);

                if (v.Initializer != null)
                {
                    init = BindExpressionWithTargetType(
                        exprSyntax: v.Initializer.Value,
                        targetType: localType,
                        diagnosticNode: v.Initializer,
                        context: context,
                        diagnostics: diagnostics,
                        requireImplicit: true);
                }
            }

            Optional<object> constValueOpt = Optional<object>.None;
            if (isConst)
            {
                if (init is null || !init.ConstantValueOpt.HasValue)
                {
                    diagnostics.Add(new Diagnostic("CN_CONST002", DiagnosticSeverity.Error,
                        "The expression assigned to a const local must be a constant expression.",
                        new Location(context.SemanticModel.SyntaxTree, v.Span)));
                }
                else
                {
                    constValueOpt = init.ConstantValueOpt;
                }
            }

            var local = new LocalSymbol(
                name: name,
                containing: _containing,
                type: localType,
                locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, v.Span)),
                isConst: isConst,
                constantValueOpt: constValueOpt);

            _locals[name] = local;
            context.Recorder.RecordDeclared(v, local);

            return new BoundLocalDeclarationStatement(ownerSyntax, local, init);
        }
        private BoundExpression BindElementAccess(
            ElementAccessExpressionSyntax node,
            BindValueKind bindValueKind,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var expr = BindExpression(node.Expression, context, diagnostics);
            if (expr.HasErrors)
                return new BoundBadExpression(node);
            if (node.ArgumentList.Arguments.Count == 1 &&
                node.ArgumentList.Arguments[0].Expression is RangeExpressionSyntax range)
            {
                if (bindValueKind == BindValueKind.LValue)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_SLICE000",
                        DiagnosticSeverity.Error,
                        "Cannot assign to a slice.",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    return new BoundBadExpression(node);
                }

                return BindSliceElementAccess(node, expr, range, context, diagnostics);
            }
            if (expr.Type is ArrayTypeSymbol at)
            {
                if (node.ArgumentList.Arguments.Count != at.Rank)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ELEM002",
                        DiagnosticSeverity.Error,
                        at.Rank == 1
                            ? "Array element access expects exactly one index argument."
                            : $"Array element access expects exactly {at.Rank} index arguments.",
                        new Location(context.SemanticModel.SyntaxTree, node.ArgumentList.Span)));
                    return new BoundBadExpression(node);
                }

                var intType = context.Compilation.GetSpecialType(SpecialType.System_Int32);

                // a[^x] => a[a.Length - x]
                bool hasFromEndIndex = false;
                for (int i = 0; i < node.ArgumentList.Arguments.Count; i++)
                {
                    if (node.ArgumentList.Arguments[i].Expression is PrefixUnaryExpressionSyntax pre &&
                        pre.Kind == SyntaxKind.IndexExpression)
                    {
                        hasFromEndIndex = true;
                        break;
                    }
                }

                if (!hasFromEndIndex)
                {
                    var indices = ImmutableArray.CreateBuilder<BoundExpression>(node.ArgumentList.Arguments.Count);

                    for (int i = 0; i < node.ArgumentList.Arguments.Count; i++)
                    {
                        var arg = node.ArgumentList.Arguments[i];
                        var indexExpr = BindExpression(arg.Expression, context, diagnostics);
                        indexExpr = ApplyConversion(
                            exprSyntax: arg.Expression,
                            expr: indexExpr,
                            targetType: intType,
                            diagnosticNode: node,
                            context: context,
                            diagnostics: diagnostics,
                            requireImplicit: true);
                        indices.Add(indexExpr);
                    }

                    return new BoundArrayElementAccessExpression(node, at.ElementType, expr, indices.ToImmutable());
                }

                if (at.Rank != 1)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ELEM004",
                        DiagnosticSeverity.Error,
                        "Index-from-end (^) is only supported for single-dimensional arrays.",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    return new BoundBadExpression(node);
                }

                var recvTmp = NewTemp("$idx_recv", expr.Type);
                var recvDecl = new BoundLocalDeclarationStatement(node, recvTmp, expr);
                var recvExpr = new BoundLocalExpression(node, recvTmp);

                var receiverTypeForLookup = GetReceiverTypeForMemberLookup(expr.Type);
                if (receiverTypeForLookup is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ELEM005",
                        DiagnosticSeverity.Error,
                        "Index-from-end (^) is not supported for this receiver type.",
                        new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));
                    return new BoundBadExpression(node);
                }

                Symbol? lengthMember = null;
                {
                    var members = LookupMembers(receiverTypeForLookup, "Length");
                    for (int i = 0; i < members.Length; i++)
                    {
                        var m = members[i];
                        if (m is PropertySymbol p && !p.IsStatic && ReferenceEquals(p.Type, intType))
                        {
                            lengthMember = p;
                            break;
                        }
                        if (m is FieldSymbol f && !f.IsStatic && ReferenceEquals(f.Type, intType))
                        {
                            lengthMember = f;
                            break;
                        }
                    }
                }
                if (lengthMember is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ELEM006",
                        DiagnosticSeverity.Error,
                        "Receiver type does not have an accessible 'Length' member required for index-from-end (^).",
                        new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));
                    return new BoundBadExpression(node);
                }

                var lenTmp = NewTemp("$idx_len", intType);
                var lenAccess = new BoundMemberAccessExpression(
                    (ExpressionSyntax)node.Expression,
                    receiverOpt: recvExpr,
                    member: lengthMember,
                    type: intType,
                    isLValue: false);
                var lenDecl = new BoundLocalDeclarationStatement(node, lenTmp, lenAccess);
                var lenExpr = new BoundLocalExpression(node, lenTmp);

                var locals = ImmutableArray.CreateBuilder<LocalSymbol>(2 + node.ArgumentList.Arguments.Count);
                var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>(2 + node.ArgumentList.Arguments.Count);
                var indices2 = ImmutableArray.CreateBuilder<BoundExpression>(node.ArgumentList.Arguments.Count);

                locals.Add(recvTmp);
                sideEffects.Add(recvDecl);

                // Capture each argument expression
                for (int i = 0; i < node.ArgumentList.Arguments.Count; i++)
                {
                    var argSyntax = node.ArgumentList.Arguments[i].Expression;

                    bool fromEnd = argSyntax is PrefixUnaryExpressionSyntax pre && pre.Kind == SyntaxKind.IndexExpression;
                    var valueSyntax = fromEnd ? ((PrefixUnaryExpressionSyntax)argSyntax).Operand : argSyntax;

                    var boundValue = BindExpression(valueSyntax, context, diagnostics);
                    boundValue = ApplyConversion(
                        exprSyntax: valueSyntax,
                        expr: boundValue,
                        targetType: intType,
                        diagnosticNode: node,
                        context: context,
                        diagnostics: diagnostics,
                        requireImplicit: true);

                    if (boundValue.HasErrors)
                        return new BoundBadExpression(node);

                    var valTmp = NewTemp($"$idx_arg{i}", intType);
                    locals.Add(valTmp);
                    sideEffects.Add(new BoundLocalDeclarationStatement(node, valTmp, boundValue));
                    var valExpr = new BoundLocalExpression(node, valTmp);

                    BoundExpression finalIndex = fromEnd
                        ? (BoundExpression)new BoundBinaryExpression(
                            node,
                            BoundBinaryOperatorKind.Subtract,
                            intType,
                            lenExpr,
                            valExpr,
                            Optional<object>.None,
                            isChecked: false)
                        : (BoundExpression)valExpr;

                    indices2.Add(finalIndex);
                }

                locals.Add(lenTmp);
                sideEffects.Add(lenDecl);

                var access = new BoundArrayElementAccessExpression(node, at.ElementType, recvExpr, indices2.ToImmutable());
                return new BoundSequenceExpression(node, locals.ToImmutable(), sideEffects.ToImmutable(), access);
            }
            if (expr.Type is PointerTypeSymbol pt)
            {
                EnsureUnsafe(node, context, diagnostics);

                if (node.ArgumentList.Arguments.Count != 1)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_PTRIDX000",
                        DiagnosticSeverity.Error,
                        "Pointer element access expects exactly one index argument.",
                        new Location(context.SemanticModel.SyntaxTree, node.ArgumentList.Span)));
                    return new BoundBadExpression(node);
                }

                var indexExpr = BindExpression(node.ArgumentList.Arguments[0].Expression, context, diagnostics);
                var intType = context.Compilation.GetSpecialType(SpecialType.System_Int32);
                indexExpr = ApplyConversion(
                    exprSyntax: node.ArgumentList.Arguments[0].Expression,
                    expr: indexExpr,
                    targetType: intType,
                    diagnosticNode: node,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);

                return new BoundPointerElementAccessExpression(node, pt.PointedAtType, expr, indexExpr);
            }

            var receiverType = GetReceiverTypeForMemberLookup(expr.Type);
            if (receiverType is not null)
            {
                var allIndexers = LookupIndexers(receiverType);
                if (!allIndexers.IsDefaultOrEmpty)
                {
                    var accessibleIndexers = FilterAccessibleIndexers(allIndexers, context);
                    if (accessibleIndexers.IsDefaultOrEmpty)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_ACC003",
                            DiagnosticSeverity.Error,
                            "Indexer is inaccessible due to its protection level.",
                            new Location(context.SemanticModel.SyntaxTree, node.Span)));
                        return new BoundBadExpression(node);
                    }

                    var instanceIndexers = ImmutableArray.CreateBuilder<PropertySymbol>(accessibleIndexers.Length);
                    for (int i = 0; i < accessibleIndexers.Length; i++)
                    {
                        if (!accessibleIndexers[i].IsStatic)
                            instanceIndexers.Add(accessibleIndexers[i]);
                    }

                    if (instanceIndexers.Count == 0)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_ELEM003",
                            DiagnosticSeverity.Error,
                            "A static indexer cannot be accessed with an instance reference.",
                            new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));
                        return new BoundBadExpression(node);
                    }

                    var rawArgs = ImmutableArray.CreateBuilder<BoundExpression>(node.ArgumentList.Arguments.Count);
                    bool hasArgErrors = false;
                    bool hasFromEndIndex = false;
                    for (int i = 0; i < node.ArgumentList.Arguments.Count; i++)
                    {
                        if (node.ArgumentList.Arguments[i].Expression is PrefixUnaryExpressionSyntax pre &&
                            pre.Kind == SyntaxKind.IndexExpression)
                        {
                            hasFromEndIndex = true;
                            break;
                        }
                    }
                    BoundExpression indexerReceiver = expr;
                    ImmutableArray<LocalSymbol> seqLocals = default;
                    ImmutableArray<BoundStatement> seqSideEffects = default;

                    if (hasFromEndIndex)
                    {
                        var intType = context.Compilation.GetSpecialType(SpecialType.System_Int32);

                        Symbol? lengthMember = null;
                        {
                            var members = LookupMembers(receiverType, "Length");
                            for (int i = 0; i < members.Length; i++)
                            {
                                var m = members[i];
                                if (m is PropertySymbol p && !p.IsStatic && ReferenceEquals(p.Type, intType))
                                {
                                    lengthMember = p;
                                    break;
                                }
                                if (m is FieldSymbol f && !f.IsStatic && ReferenceEquals(f.Type, intType))
                                {
                                    lengthMember = f;
                                    break;
                                }
                            }
                        }

                        if (lengthMember is null)
                        {
                            diagnostics.Add(new Diagnostic(
                                "CN_ELEM006",
                                DiagnosticSeverity.Error,
                                "Receiver type does not have an accessible 'Length' member required for index-from-end (^).",
                                new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));
                            return new BoundBadExpression(node);
                        }

                        // Capture receiver
                        var recvTmp = NewTemp("$idx_recv", expr.Type);
                        var recvDecl = new BoundLocalDeclarationStatement(node, recvTmp, expr);
                        var recvExpr = new BoundLocalExpression(node, recvTmp);

                        // Capture length
                        var lenTmp = NewTemp("$idx_len", intType);
                        var lenAccess = new BoundMemberAccessExpression(
                            (ExpressionSyntax)node.Expression,
                            receiverOpt: recvExpr,
                            member: lengthMember,
                            type: intType,
                            isLValue: false);
                        var lenDecl = new BoundLocalDeclarationStatement(node, lenTmp, lenAccess);
                        var lenExpr = new BoundLocalExpression(node, lenTmp);

                        var locals = ImmutableArray.CreateBuilder<LocalSymbol>(2);
                        var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>(2);

                        locals.Add(recvTmp);
                        sideEffects.Add(recvDecl);

                        locals.Add(lenTmp);
                        sideEffects.Add(lenDecl);

                        indexerReceiver = recvExpr;

                        for (int i = 0; i < node.ArgumentList.Arguments.Count; i++)
                        {
                            var argSyntax = node.ArgumentList.Arguments[i].Expression;

                            if (argSyntax is PrefixUnaryExpressionSyntax pre &&
                                pre.Kind == SyntaxKind.IndexExpression)
                            {
                                var valueSyntax = pre.Operand;
                                var valueExpr = BindExpression(valueSyntax, context, diagnostics);
                                valueExpr = ApplyConversion(
                                    exprSyntax: valueSyntax,
                                    expr: valueExpr,
                                    targetType: intType,
                                    diagnosticNode: node,
                                    context: context,
                                    diagnostics: diagnostics,
                                    requireImplicit: true);

                                if (valueExpr.HasErrors)
                                {
                                    hasArgErrors = true;
                                }
                                else
                                {
                                    rawArgs.Add(new BoundBinaryExpression(
                                        node,
                                        BoundBinaryOperatorKind.Subtract,
                                        intType,
                                        lenExpr,
                                        valueExpr,
                                        Optional<object>.None,
                                        isChecked: false));
                                }
                            }
                            else
                            {
                                var arg = BindExpression(argSyntax, context, diagnostics);
                                rawArgs.Add(arg);
                                if (arg.HasErrors)
                                    hasArgErrors = true;
                            }
                        }

                        seqLocals = locals.ToImmutable();
                        seqSideEffects = sideEffects.ToImmutable();
                    }
                    else
                    {
                        for (int i = 0; i < node.ArgumentList.Arguments.Count; i++)
                        {
                            var arg = BindExpression(node.ArgumentList.Arguments[i].Expression, context, diagnostics);
                            rawArgs.Add(arg);
                            if (arg.HasErrors)
                                hasArgErrors = true;
                        }
                    }

                    if (hasArgErrors)
                        return new BoundBadExpression(node);

                    if (!TryResolveIndexerOverload(
                        candidates: instanceIndexers.ToImmutable(),
                        syntax: node,
                        rawArgs: rawArgs.ToImmutable(),
                        context: context,
                        diagnostics: diagnostics,
                        chosen: out var chosenIndexer,
                        convertedArgs: out var convertedArgs))
                    {
                        return new BoundBadExpression(node);
                    }

                    bool canReadIndexer =
                        chosenIndexer!.GetMethod is not null &&
                        AccessibilityHelper.IsAccessible(chosenIndexer.GetMethod, context);

                    bool canWriteIndexer =
                        chosenIndexer.SetMethod is not null &&
                        AccessibilityHelper.IsAccessible(chosenIndexer.SetMethod, context);

                    if (bindValueKind == BindValueKind.RValue && !canReadIndexer)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_MEMACC024",
                            DiagnosticSeverity.Error,
                            "Indexer has no accessible getter.",
                            new Location(context.SemanticModel.SyntaxTree, node.Span)));
                        return new BoundBadExpression(node);
                    }
                    bool isRefReturnIndexer =
                        chosenIndexer.GetMethod is MethodSymbol getMethodSymbol &&
                        getMethodSymbol.ReturnType is ByRefTypeSymbol;

                    if (bindValueKind == BindValueKind.LValue && isRefReturnIndexer && !canReadIndexer)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_MEMACC024",
                            DiagnosticSeverity.Error,
                            "Indexer has no accessible getter.",
                            new Location(context.SemanticModel.SyntaxTree, node.Span)));
                        return new BoundBadExpression(node);
                    }
                    if (bindValueKind == BindValueKind.LValue &&
                        !chosenIndexer.IsStatic &&
                        IsReadOnlyValueReceiver(expr, context))
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_READONLY_THIS002",
                            DiagnosticSeverity.Error,
                            "Cannot assign to instance properties of 'this' in a readonly struct instance member.",
                            new Location(context.SemanticModel.SyntaxTree, node.Span)));
                        return new BoundBadExpression(node);
                    }

                    bool allowCtorAutoPropWrite =
                        bindValueKind == BindValueKind.LValue &&
                        !canWriteIndexer &&
                        CanAssignReadOnlyAutoPropertyInConstructor(chosenIndexer, expr, context);

                    if (bindValueKind == BindValueKind.LValue &&
                        !canWriteIndexer &&
                        !allowCtorAutoPropWrite &&
                        !(isRefReturnIndexer && canReadIndexer))
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_MEMACC025",
                            DiagnosticSeverity.Error,
                            "Indexer has no accessible setter.",
                            new Location(context.SemanticModel.SyntaxTree, node.Span)));
                        return new BoundBadExpression(node);
                    }

                    bool hasErrors = expr.HasErrors;
                    for (int i = 0; i < convertedArgs.Length; i++)
                    {
                        if (convertedArgs[i].HasErrors)
                        {
                            hasErrors = true;
                            break;
                        }
                    }

                    var result = new BoundIndexerAccessExpression(
                        node,
                        indexerReceiver,
                        chosenIndexer,
                        convertedArgs,
                        isLValue: canWriteIndexer || allowCtorAutoPropWrite || (isRefReturnIndexer && canReadIndexer),
                        hasErrors: hasErrors);

                    if (hasFromEndIndex)
                        return new BoundSequenceExpression(node, seqLocals, seqSideEffects, result);

                    return result;
                }
            }

            diagnostics.Add(new Diagnostic(
                "CN_ELEM000",
                DiagnosticSeverity.Error,
                "Element access is not implemented for this type.",
                new Location(context.SemanticModel.SyntaxTree, node.Span)));

            return new BoundBadExpression(node);
        }
        private BoundExpression BindSliceElementAccess(
            ElementAccessExpressionSyntax node,
            BoundExpression receiver,
            RangeExpressionSyntax range,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            static bool IsFromEndIndex(ExpressionSyntax expr, out ExpressionSyntax valueExpression)
            {
                if (expr is PrefixUnaryExpressionSyntax pre && pre.Kind == SyntaxKind.IndexExpression)
                {
                    valueExpression = pre.Operand;
                    return true;
                }

                valueExpression = expr;
                return false;
            }

            var int32 = context.Compilation.GetSpecialType(SpecialType.System_Int32);

            // Capture receiver
            var recvTmp = NewTemp("$slice_recv", receiver.Type);
            var recvDecl = new BoundLocalDeclarationStatement(node, recvTmp, receiver);
            var recvExpr = new BoundLocalExpression(node, recvTmp);

            BoundExpression? leftValueExpr = null;
            bool leftFromEnd = false;
            LocalSymbol? leftTmp = null;
            BoundStatement? leftDecl = null;

            if (range.LeftOperand is ExpressionSyntax leftSyntax)
            {
                leftFromEnd = IsFromEndIndex(leftSyntax, out var leftValueSyntax);
                var boundLeft = BindExpression(leftValueSyntax, context, diagnostics);
                boundLeft = ApplyConversion(
                    exprSyntax: leftValueSyntax,
                    expr: boundLeft,
                    targetType: int32,
                    diagnosticNode: node,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);

                if (boundLeft.HasErrors)
                    return new BoundBadExpression(node);

                leftTmp = NewTemp("$slice_left", int32);
                leftDecl = new BoundLocalDeclarationStatement(node, leftTmp, boundLeft);
                leftValueExpr = new BoundLocalExpression(node, leftTmp);
            }

            BoundExpression? rightValueExpr = null;
            bool rightFromEnd = false;
            LocalSymbol? rightTmp = null;
            BoundStatement? rightDecl = null;

            if (range.RightOperand is ExpressionSyntax rightSyntax)
            {
                rightFromEnd = IsFromEndIndex(rightSyntax, out var rightValueSyntax);
                var boundRight = BindExpression(rightValueSyntax, context, diagnostics);
                boundRight = ApplyConversion(
                    exprSyntax: rightValueSyntax,
                    expr: boundRight,
                    targetType: int32,
                    diagnosticNode: node,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);

                if (boundRight.HasErrors)
                    return new BoundBadExpression(node);

                rightTmp = NewTemp("$slice_right", int32);
                rightDecl = new BoundLocalDeclarationStatement(node, rightTmp, boundRight);
                rightValueExpr = new BoundLocalExpression(node, rightTmp);
            }

            // Determine slicing strategy
            var receiverTypeForLookup = GetReceiverTypeForMemberLookup(receiver.Type);
            if (receiverTypeForLookup is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_SLICE001",
                    DiagnosticSeverity.Error,
                    "Slicing is not supported for this receiver type.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            // Find instance Length
            Symbol? lengthMember = null;
            {
                var members = LookupMembers(receiverTypeForLookup, "Length");
                for (int i = 0; i < members.Length; i++)
                {
                    var m = members[i];
                    if (m is PropertySymbol p && !p.IsStatic && ReferenceEquals(p.Type, int32))
                    {
                        lengthMember = p;
                        break;
                    }
                    if (m is FieldSymbol f && !f.IsStatic && ReferenceEquals(f.Type, int32))
                    {
                        lengthMember = f;
                        break;
                    }
                }
            }
            if (lengthMember is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_SLICE002",
                    DiagnosticSeverity.Error,
                    "Receiver type does not have an accessible 'Length' member required for slicing.",
                    new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));
                return new BoundBadExpression(node);
            }

            // lenTmp = recv.Length
            var lenTmp = NewTemp("$slice_len", int32);
            var lenAccess = new BoundMemberAccessExpression(
                (ExpressionSyntax)node.Expression,
                receiverOpt: recvExpr,
                member: lengthMember,
                type: int32,
                isLValue: false);
            var lenDecl = new BoundLocalDeclarationStatement(node, lenTmp, lenAccess);
            var lenExpr = new BoundLocalExpression(node, lenTmp);

            // startTmp
            var startTmp = NewTemp("$slice_start", int32);
            BoundExpression startInit;
            if (leftValueExpr is null)
            {
                startInit = new BoundLiteralExpression(node, int32, 0);
            }
            else if (leftFromEnd)
            {
                startInit = new BoundBinaryExpression(
                    node,
                    BoundBinaryOperatorKind.Subtract,
                    int32,
                    lenExpr,
                    leftValueExpr,
                    Optional<object>.None,
                    isChecked: false);
            }
            else
            {
                startInit = leftValueExpr;
            }
            var startDecl = new BoundLocalDeclarationStatement(node, startTmp, startInit);
            var startExpr = new BoundLocalExpression(node, startTmp);

            // sliceLenTmp
            var sliceLenTmp = NewTemp("$slice_count", int32);
            BoundExpression sliceLenInit;
            if (rightValueExpr is null)
            {
                // len - start
                sliceLenInit = new BoundBinaryExpression(
                    node,
                    BoundBinaryOperatorKind.Subtract,
                    int32,
                    lenExpr,
                    startExpr,
                    Optional<object>.None,
                    isChecked: false);
            }
            else
            {
                BoundExpression endExpr = rightFromEnd
                    ? new BoundBinaryExpression(
                        node,
                        BoundBinaryOperatorKind.Subtract,
                        int32,
                        lenExpr,
                        rightValueExpr,
                        Optional<object>.None,
                        isChecked: false)
                    : rightValueExpr;

                sliceLenInit = new BoundBinaryExpression(
                    node,
                    BoundBinaryOperatorKind.Subtract,
                    int32,
                    endExpr,
                    startExpr,
                    Optional<object>.None,
                    isChecked: false);
            }
            var sliceLenDecl = new BoundLocalDeclarationStatement(node, sliceLenTmp, sliceLenInit);
            var sliceLenExpr = new BoundLocalExpression(node, sliceLenTmp);

            var locals = ImmutableArray.CreateBuilder<LocalSymbol>(8);
            var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>(8);

            locals.Add(recvTmp);
            sideEffects.Add(recvDecl);

            if (leftTmp is not null)
            {
                locals.Add(leftTmp);
                sideEffects.Add(leftDecl!);
            }
            if (rightTmp is not null)
            {
                locals.Add(rightTmp);
                sideEffects.Add(rightDecl!);
            }

            locals.Add(lenTmp);
            sideEffects.Add(lenDecl);
            locals.Add(startTmp);
            sideEffects.Add(startDecl);
            locals.Add(sliceLenTmp);
            sideEffects.Add(sliceLenDecl);

            // string slicing => string.Substring
            if (receiver.Type.SpecialType == SpecialType.System_String)
            {
                var stringType = (NamedTypeSymbol)context.Compilation.GetSpecialType(SpecialType.System_String);

                MethodSymbol? substring = null;
                bool useTwoArgs = rightValueExpr is not null;
                int desiredParamCount = useTwoArgs ? 2 : 1;

                var members = stringType.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is not MethodSymbol m) continue;
                    if (m.IsStatic || m.IsConstructor) continue;
                    if (!string.Equals(m.Name, "Substring", StringComparison.Ordinal)) continue;
                    if (m.Parameters.Length != desiredParamCount) continue;
                    if (!ReferenceEquals(m.ReturnType, stringType)) continue;
                    if (!ReferenceEquals(m.Parameters[0].Type, int32)) continue;
                    if (desiredParamCount == 2 && !ReferenceEquals(m.Parameters[1].Type, int32)) continue;
                    substring = m;
                    break;
                }

                if (substring is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_SLICE010",
                        DiagnosticSeverity.Error,
                        "Missing required 'string.Substring' overload for slicing.",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    return new BoundBadExpression(node);
                }

                var args = useTwoArgs
                    ? ImmutableArray.Create<BoundExpression>(startExpr, sliceLenExpr)
                    : ImmutableArray.Create<BoundExpression>(startExpr);

                var call = new BoundCallExpression(node, recvExpr, substring, args);
                return new BoundSequenceExpression(node, locals.ToImmutable(), sideEffects.ToImmutable(), call);
            }

            // span slicing => Slice(start) / Slice(start, length)
            if (TryGetSpanLikeElementType(receiver.Type, out var spanLikeType, out _))
            {
                bool useTwoArgs = rightValueExpr is not null;
                int desiredParamCount = useTwoArgs ? 2 : 1;

                MethodSymbol? slice = null;
                var members = spanLikeType.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is not MethodSymbol m) continue;
                    if (m.IsStatic || m.IsConstructor) continue;
                    if (!string.Equals(m.Name, "Slice", StringComparison.Ordinal)) continue;
                    if (m.Parameters.Length != desiredParamCount) continue;
                    if (!ReferenceEquals(m.Parameters[0].Type, int32)) continue;
                    if (desiredParamCount == 2 && !ReferenceEquals(m.Parameters[1].Type, int32)) continue;
                    if (!ReferenceEquals(m.ReturnType, spanLikeType)) continue;
                    slice = m;
                    break;
                }

                if (slice is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_SLICE011",
                        DiagnosticSeverity.Error,
                        "Missing required 'Slice' method overload for span slicing.",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    return new BoundBadExpression(node);
                }

                var args = useTwoArgs
                    ? ImmutableArray.Create<BoundExpression>(startExpr, sliceLenExpr)
                    : ImmutableArray.Create<BoundExpression>(startExpr);

                var call = new BoundCallExpression(node, recvExpr, slice, args);
                return new BoundSequenceExpression(node, locals.ToImmutable(), sideEffects.ToImmutable(), call);
            }

            // array slicing => allocate + Array.Copy
            if (receiver.Type is ArrayTypeSymbol at)
            {
                if (at.Rank != 1)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_SLICE020",
                        DiagnosticSeverity.Error,
                        "Array slicing is only supported for single-dimensional arrays.",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    return new BoundBadExpression(node);
                }

                var resultArrTmp = NewTemp("$slice_arr", receiver.Type);
                locals.Add(resultArrTmp);

                var created = new BoundArrayCreationExpression(
                    syntax: node,
                    type: (ArrayTypeSymbol)receiver.Type,
                    elementType: at.ElementType,
                    count: sliceLenExpr,
                    initializerOpt: null);

                var resultArrDecl = new BoundLocalDeclarationStatement(node, resultArrTmp, created);
                sideEffects.Add(resultArrDecl);

                var resultArrExpr = new BoundLocalExpression(node, resultArrTmp);

                // Find Array.Copy(Array, int, Array, int, int)
                var arrayBase = context.Compilation.GetSpecialType(SpecialType.System_Array);
                if (arrayBase is not NamedTypeSymbol arrayType)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_SLICE021",
                        DiagnosticSeverity.Error,
                        "Missing special type System.Array required for array slicing.",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    return new BoundBadExpression(node);
                }

                MethodSymbol? copy = null;
                var arrayMembers = arrayType.GetMembers();
                for (int i = 0; i < arrayMembers.Length; i++)
                {
                    if (arrayMembers[i] is not MethodSymbol m) continue;
                    if (!m.IsStatic || m.IsConstructor) continue;
                    if (!string.Equals(m.Name, "Copy", StringComparison.Ordinal)) continue;
                    if (m.Parameters.Length != 5) continue;
                    if (!ReferenceEquals(m.Parameters[0].Type, arrayType)) continue;
                    if (!ReferenceEquals(m.Parameters[1].Type, int32)) continue;
                    if (!ReferenceEquals(m.Parameters[2].Type, arrayType)) continue;
                    if (!ReferenceEquals(m.Parameters[3].Type, int32)) continue;
                    if (!ReferenceEquals(m.Parameters[4].Type, int32)) continue;
                    copy = m;
                    break;
                }

                if (copy is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_SLICE022",
                        DiagnosticSeverity.Error,
                        "Missing required 'Array.Copy(Array, int, Array, int, int)' overload for array slicing.",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    return new BoundBadExpression(node);
                }

                var zero = new BoundLiteralExpression(node, int32, 0);

                var srcAsArray = ApplyConversion(
                    exprSyntax: node.Expression,
                    expr: recvExpr,
                    targetType: arrayType,
                    diagnosticNode: node,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);

                var dstAsArray = ApplyConversion(
                    exprSyntax: node.Expression,
                    expr: resultArrExpr,
                    targetType: arrayType,
                    diagnosticNode: node,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);

                if (srcAsArray.HasErrors || dstAsArray.HasErrors)
                    return new BoundBadExpression(node);

                var copyArgs = ImmutableArray.Create<BoundExpression>(
                    srcAsArray,
                    startExpr,
                    dstAsArray,
                    zero,
                    sliceLenExpr);

                var copyCall = new BoundCallExpression(node, receiverOpt: null, copy, copyArgs);
                sideEffects.Add(new BoundExpressionStatement(node, copyCall));

                return new BoundSequenceExpression(node, locals.ToImmutable(), sideEffects.ToImmutable(), resultArrExpr);
            }

            diagnostics.Add(new Diagnostic(
                "CN_SLICE003",
                DiagnosticSeverity.Error,
                "Slicing is not supported for this receiver type.",
                new Location(context.SemanticModel.SyntaxTree, node.Span)));

            return new BoundBadExpression(node);
        }
        private static ImmutableArray<PropertySymbol> FilterAccessibleIndexers(
            ImmutableArray<PropertySymbol> candidates, BindingContext context)
        {
            if (candidates.IsDefaultOrEmpty)
                return candidates;

            var builder = ImmutableArray.CreateBuilder<PropertySymbol>(candidates.Length);
            for (int i = 0; i < candidates.Length; i++)
            {
                var p = candidates[i];
                if (AccessibilityHelper.IsAccessible(p, context))
                    builder.Add(p);
            }

            return builder.Count == candidates.Length ? candidates : builder.ToImmutable();
        }
        private bool TryResolveIndexerOverload(
            ImmutableArray<PropertySymbol> candidates,
            ElementAccessExpressionSyntax syntax,
            ImmutableArray<BoundExpression> rawArgs,
            BindingContext context,
            DiagnosticBag diagnostics,
            out PropertySymbol? chosen,
            out ImmutableArray<BoundExpression> convertedArgs)
        {
            chosen = null;
            convertedArgs = default;

            PropertySymbol? best = null;
            int bestScore = int.MaxValue;
            bool ambiguous = false;

            for (int i = 0; i < candidates.Length; i++)
            {
                var p = candidates[i];
                var parameters = p.Parameters;

                if (parameters.Length != rawArgs.Length)
                    continue;

                int score = 0;
                bool ok = true;

                for (int a = 0; a < rawArgs.Length; a++)
                {
                    var conv = ClassifyConversion(rawArgs[a], parameters[a].Type, context);
                    if (!conv.Exists || !conv.IsImplicit)
                    {
                        ok = false;
                        break;
                    }

                    score += conv.Kind switch
                    {
                        ConversionKind.Identity => 0,
                        ConversionKind.ImplicitNumeric => 1,
                        ConversionKind.ImplicitConstant => 1,
                        ConversionKind.ImplicitReference => 1,
                        ConversionKind.ImplicitTuple => 1,
                        ConversionKind.NullLiteral => 1,
                        ConversionKind.Boxing => 2,
                        ConversionKind.UserDefined => 3,
                        _ => 10
                    };
                }

                if (!ok)
                    continue;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = p;
                    ambiguous = false;
                }
                else if (score == bestScore)
                {
                    ambiguous = true;
                }
            }

            if (best is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ELEM001",
                    DiagnosticSeverity.Error,
                    "No indexer overload matches the argument list.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.ArgumentList.Span)));
                return false;
            }

            if (ambiguous)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ELEM004",
                    DiagnosticSeverity.Error,
                    "Indexer overload resolution is ambiguous.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.ArgumentList.Span)));
                return false;
            }

            var converted = ImmutableArray.CreateBuilder<BoundExpression>(rawArgs.Length);
            var bestParams = best.Parameters;
            for (int i = 0; i < rawArgs.Length; i++)
            {
                converted.Add(ApplyConversion(
                    exprSyntax: syntax.ArgumentList.Arguments[i].Expression,
                    expr: rawArgs[i],
                    targetType: bestParams[i].Type,
                    diagnosticNode: syntax,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true));
            }

            chosen = best;
            convertedArgs = converted.ToImmutable();
            return true;
        }
        private BoundExpression BindArrayCreation(
            ArrayCreationExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var arrayTypeBound = BindType(node.Type, context, diagnostics);
            if (arrayTypeBound is not ArrayTypeSymbol arrayType)
                return new BoundBadExpression(node);

            var elemType = arrayType.ElementType;
            if (elemType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ARRNEW001",
                    DiagnosticSeverity.Error,
                    "Array element type cannot be void.",
                    new Location(context.SemanticModel.SyntaxTree, node.Type.ElementType.Span)));
                return new BoundBadExpression(node);
            }

            if (node.Type.RankSpecifiers.Count == 0)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ARRNEW000",
                    DiagnosticSeverity.Error,
                    "Array creation requires at least one rank specifier.",
                    new Location(context.SemanticModel.SyntaxTree, node.Type.Span)));
                return new BoundBadExpression(node);
            }

            var outerRank = node.Type.RankSpecifiers[0];

            for (int i = 1; i < node.Type.RankSpecifiers.Count; i++)
            {
                var rs = node.Type.RankSpecifiers[i];
                for (int j = 0; j < rs.Sizes.Count; j++)
                {
                    if (rs.Sizes[j] is OmittedArraySizeExpressionSyntax)
                        continue;

                    diagnostics.Add(new Diagnostic(
                        "CN_ARRNEW005",
                        DiagnosticSeverity.Error,
                        "Only the outermost array dimension may specify sizes in a jagged array creation.",
                        new Location(context.SemanticModel.SyntaxTree, rs.Span)));
                    return new BoundBadExpression(node);
                }
            }

            var initializerOpt = BindManagedArrayInitializer(node.Initializer, elemType, arrayType.Rank, context, diagnostics);
            var inferredInitShape = initializerOpt is null
                ? ImmutableArray<int>.Empty
                : InferRectangularInitializerShape(initializerOpt, arrayType.Rank, context, diagnostics);

            var int32 = context.Compilation.GetSpecialType(SpecialType.System_Int32);
            var dimensionSizes = ImmutableArray.CreateBuilder<BoundExpression>(outerRank.Sizes.Count);

            for (int i = 0; i < outerRank.Sizes.Count; i++)
            {
                var sizeExprSyntax = outerRank.Sizes[i];

                if (sizeExprSyntax is not OmittedArraySizeExpressionSyntax)
                {
                    var sizeExpr = BindExpression(sizeExprSyntax, context, diagnostics);
                    sizeExpr = ApplyConversion(
                        exprSyntax: sizeExprSyntax,
                        expr: sizeExpr,
                        targetType: int32,
                        diagnosticNode: node,
                        context: context,
                        diagnostics: diagnostics,
                        requireImplicit: true);
                    dimensionSizes.Add(sizeExpr);
                    continue;
                }

                if (initializerOpt is not null && i < inferredInitShape.Length)
                {
                    dimensionSizes.Add(new BoundLiteralExpression(sizeExprSyntax, int32, inferredInitShape[i]));
                    continue;
                }

                diagnostics.Add(new Diagnostic(
                    "CN_ARRNEW003",
                    DiagnosticSeverity.Error,
                    arrayType.Rank == 1
                        ? "Array creation requires a size expression or an initializer."
                        : "Cannot infer all array dimensions from the initializer.",
                    new Location(context.SemanticModel.SyntaxTree, outerRank.Span)));
                return new BoundBadExpression(node);
            }

            if (initializerOpt != null)
            {
                for (int i = 0; i < dimensionSizes.Count && i < inferredInitShape.Length; i++)
                {
                    var dimExpr = dimensionSizes[i];
                    if (dimExpr.ConstantValueOpt.HasValue &&
                        dimExpr.ConstantValueOpt.Value is int n &&
                        n != inferredInitShape[i])
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_ARRNEW004",
                            DiagnosticSeverity.Error,
                            $"Initializer dimension {i + 1} has length '{inferredInitShape[i]}', but '{n}' was specified.",
                            new Location(context.SemanticModel.SyntaxTree, node.Initializer!.Span)));
                    }
                }
            }

            return new BoundArrayCreationExpression(node, arrayType, elemType, dimensionSizes.ToImmutable(), initializerOpt);
        }
        private BoundExpression BindImplicitArrayCreation(ImplicitArrayCreationExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (node.Commas.Count != 0)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ARRIMP000",
                    DiagnosticSeverity.Error,
                    "Only single-dimensional implicit array creation is supported.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            var exprs = node.Initializer.Expressions;
            if (exprs.Count == 0)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ARRIMP001",
                    DiagnosticSeverity.Error,
                    "Cannot infer array element type from an empty initializer.",
                    new Location(context.SemanticModel.SyntaxTree, node.Initializer.Span)));
                return new BoundBadExpression(node);
            }

            var raw = ImmutableArray.CreateBuilder<BoundExpression>(exprs.Count);
            for (int i = 0; i < exprs.Count; i++)
                raw.Add(BindExpression(exprs[i], context, diagnostics));

            TypeSymbol? elemType = null;
            for (int i = 0; i < raw.Count; i++)
            {
                var t = raw[i].Type;
                if (t is ErrorTypeSymbol)
                    continue;

                if (elemType is null)
                {
                    elemType = t;
                    continue;
                }

                elemType = ClassifyImplicitArrayElementCommonType(elemType, t, node, context, diagnostics);
                if (elemType is null || elemType is ErrorTypeSymbol)
                    return new BoundBadExpression(node);
            }

            if (elemType is null || elemType is NullTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ARRIMP002",
                    DiagnosticSeverity.Error,
                    "Cannot infer array element type.",
                    new Location(context.SemanticModel.SyntaxTree, node.Initializer.Span)));
                return new BoundBadExpression(node);
            }

            if (elemType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ARRIMP003",
                    DiagnosticSeverity.Error,
                    "Array element type cannot be void.",
                    new Location(context.SemanticModel.SyntaxTree, node.Initializer.Span)));
                return new BoundBadExpression(node);
            }

            var converted = ImmutableArray.CreateBuilder<BoundExpression>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                converted.Add(ApplyConversion(
                    exprSyntax: exprs[i],
                    expr: raw[i],
                    targetType: elemType,
                    diagnosticNode: node,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true));
            }

            var init = new BoundArrayInitializerExpression(node.Initializer, elemType, converted.ToImmutable());
            context.Recorder.RecordBound(node.Initializer, init);

            var int32 = context.Compilation.GetSpecialType(SpecialType.System_Int32);
            var count = new BoundLiteralExpression(node, int32, converted.Count);
            var arrayType = context.Compilation.CreateArrayType(elemType, rank: 1);

            return new BoundArrayCreationExpression(node, arrayType, elemType, count, init);
        }
        internal BoundExpression BindExpressionWithTargetType(
            ExpressionSyntax exprSyntax,
            TypeSymbol targetType,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics,
            bool requireImplicit = true)
        {
            if (exprSyntax is InitializerExpressionSyntax initSyntax &&
                initSyntax.Kind == SyntaxKind.ArrayInitializerExpression &&
                targetType is ArrayTypeSymbol arrayType)
            {
                var boundInit = BindManagedArrayInitializer(
                    initSyntax,
                    arrayType.ElementType,
                    arrayType.Rank,
                    context,
                    diagnostics);

                if (boundInit is null)
                {
                    var bad = new BoundBadExpression(exprSyntax);
                    bad.SetType(targetType);
                    context.Recorder.RecordBound(exprSyntax, bad);
                    return bad;
                }

                var inferredShape = InferRectangularInitializerShape(
                    boundInit,
                    arrayType.Rank,
                    context,
                    diagnostics);

                if (arrayType.Rank > 1 && inferredShape.Length < arrayType.Rank)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ARRNEW003",
                        DiagnosticSeverity.Error,
                        "Cannot infer all array dimensions from the initializer.",
                        new Location(context.SemanticModel.SyntaxTree, initSyntax.Span)));

                    var bad = new BoundBadExpression(exprSyntax);
                    bad.SetType(targetType);
                    context.Recorder.RecordBound(exprSyntax, bad);
                    return bad;
                }

                var int32 = context.Compilation.GetSpecialType(SpecialType.System_Int32);
                var sizes = ImmutableArray.CreateBuilder<BoundExpression>(arrayType.Rank);

                for (int i = 0; i < arrayType.Rank; i++)
                    sizes.Add(new BoundLiteralExpression(initSyntax, int32, inferredShape[i]));

                var arrayCreation = new BoundArrayCreationExpression(
                    exprSyntax,
                    arrayType,
                    arrayType.ElementType,
                    sizes.ToImmutable(),
                    boundInit);

                context.Recorder.RecordBound(exprSyntax, arrayCreation);
                return arrayCreation;
            }

            var bound = BindExpression(exprSyntax, context, diagnostics);
            return ApplyConversion(
                exprSyntax,
                bound,
                targetType,
                diagnosticNode,
                context,
                diagnostics,
                requireImplicit);
        }
        private BoundArrayInitializerExpression? BindManagedArrayInitializer(
            InitializerExpressionSyntax? init,
            TypeSymbol elemType,
            int rank,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (init is null)
                return null;

            var exprs = init.Expressions;
            var b = ImmutableArray.CreateBuilder<BoundExpression>(exprs.Count);

            for (int i = 0; i < exprs.Count; i++)
            {
                var exprSyntax = exprs[i];

                if (rank > 1)
                {
                    if (exprSyntax is not InitializerExpressionSyntax nested)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_ARRNEW006",
                            DiagnosticSeverity.Error,
                            "A nested initializer is required for a multidimensional array.",
                            new Location(context.SemanticModel.SyntaxTree, exprSyntax.Span)));

                        b.Add(new BoundBadExpression(exprSyntax));
                        continue;
                    }

                    var nestedInit = BindManagedArrayInitializer(nested, elemType, rank - 1, context, diagnostics)
                        ?? new BoundArrayInitializerExpression(nested, elemType, ImmutableArray<BoundExpression>.Empty);

                    b.Add(nestedInit);
                    continue;
                }

                if (exprSyntax is InitializerExpressionSyntax nestedArrayInit && elemType is ArrayTypeSymbol nestedArrayType)
                {
                    var nestedBoundInit = BindManagedArrayInitializer(
                        nestedArrayInit,
                        nestedArrayType.ElementType,
                        nestedArrayType.Rank,
                        context,
                        diagnostics);

                    if (nestedBoundInit is null)
                    {
                        b.Add(new BoundBadExpression(exprSyntax));
                        continue;
                    }

                    var shape = InferRectangularInitializerShape(nestedBoundInit, nestedArrayType.Rank, context, diagnostics);
                    if (shape.Length < nestedArrayType.Rank)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_ARRNEW007",
                            DiagnosticSeverity.Error,
                            "Cannot infer all nested array dimensions from the initializer.",
                            new Location(context.SemanticModel.SyntaxTree, nestedArrayInit.Span)));

                        b.Add(new BoundBadExpression(exprSyntax));
                        continue;
                    }

                    var nestedSizes = ImmutableArray.CreateBuilder<BoundExpression>(nestedArrayType.Rank);
                    var int32 = context.Compilation.GetSpecialType(SpecialType.System_Int32);
                    for (int d = 0; d < nestedArrayType.Rank; d++)
                        nestedSizes.Add(new BoundLiteralExpression(nestedArrayInit, int32, shape[d]));

                    b.Add(new BoundArrayCreationExpression(
                        nestedArrayInit,
                        nestedArrayType,
                        nestedArrayType.ElementType,
                        nestedSizes.ToImmutable(),
                        nestedBoundInit));
                    continue;
                }

                var e = BindExpression(exprSyntax, context, diagnostics);
                e = ApplyConversion(exprSyntax, e, elemType, init, context, diagnostics, requireImplicit: true);
                b.Add(e);
            }

            var boundInit = new BoundArrayInitializerExpression(init, elemType, b.ToImmutable());
            context.Recorder.RecordBound(init, boundInit);
            return boundInit;
        }
        private ImmutableArray<int> InferRectangularInitializerShape(
            BoundArrayInitializerExpression init,
            int rank,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (rank <= 1)
                return ImmutableArray.Create(init.Elements.Length);

            var topLen = init.Elements.Length;
            if (topLen == 0)
                return ImmutableArray.Create(0);

            ImmutableArray<int> subShape = default;
            bool haveSubShape = false;

            for (int i = 0; i < init.Elements.Length; i++)
            {
                if (init.Elements[i] is not BoundArrayInitializerExpression nested)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ARRNEW006",
                        DiagnosticSeverity.Error,
                        "A nested initializer is required for a multidimensional array.",
                        new Location(context.SemanticModel.SyntaxTree, init.Elements[i].Syntax.Span)));
                    return ImmutableArray.Create(topLen);
                }

                var current = InferRectangularInitializerShape(nested, rank - 1, context, diagnostics);
                if (!haveSubShape)
                {
                    subShape = current;
                    haveSubShape = true;
                    continue;
                }

                if (current.Length != subShape.Length)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ARRNEW008",
                        DiagnosticSeverity.Error,
                        "Multidimensional array initializer must be rectangular.",
                        new Location(context.SemanticModel.SyntaxTree, nested.Syntax.Span)));
                    continue;
                }

                for (int d = 0; d < subShape.Length; d++)
                {
                    if (current[d] != subShape[d])
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_ARRNEW008",
                            DiagnosticSeverity.Error,
                            "Multidimensional array initializer must be rectangular.",
                            new Location(context.SemanticModel.SyntaxTree, nested.Syntax.Span)));
                        break;
                    }
                }
            }

            var result = ImmutableArray.CreateBuilder<int>(1 + subShape.Length);
            result.Add(topLen);
            result.AddRange(subShape);
            return result.ToImmutable();
        }
        private TypeSymbol? ClassifyImplicitArrayElementCommonType(
            TypeSymbol t1,
            TypeSymbol t2,
            SyntaxNode diagNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (ReferenceEquals(t1, t2))
                return t1;

            if (t1 is NullTypeSymbol && t2.IsReferenceType)
                return t2;
            if (t2 is NullTypeSymbol && t1.IsReferenceType)
                return t1;

            if (IsNumeric(t1.SpecialType) && IsNumeric(t2.SpecialType))
                return GetBinaryNumericPromotionType(
                    context.Compilation,
                    context.SemanticModel.SyntaxTree,
                    t1,
                    t2,
                    diagNode,
                    diagnostics);

            if (t1.IsReferenceType && t2.IsReferenceType)
            {
                if (HasImplicitReferenceConversion(t2, t1)) return t1;
                if (HasImplicitReferenceConversion(t1, t2)) return t2;
            }

            diagnostics.Add(new Diagnostic(
                "CN_ARRIMP004",
                DiagnosticSeverity.Error,
                $"Cannot determine common array element type for '{t1.Name}' and '{t2.Name}'.",
                new Location(context.SemanticModel.SyntaxTree, diagNode.Span)));
            return null;
        }
        private BoundExpression BindStackAlloc(
            StackAllocArrayCreationExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (node.Type is not ArrayTypeSyntax at)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_STACKALLOC000",
                    DiagnosticSeverity.Error,
                    "stackalloc requires an array type.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            if (at.RankSpecifiers.Count != 1)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_STACKALLOC001",
                    DiagnosticSeverity.Error,
                    "stackalloc only supports single-dimensional arrays.",
                    new Location(context.SemanticModel.SyntaxTree, at.Span)));
                return new BoundBadExpression(node);
            }

            var elemType = BindType(at.ElementType, context, diagnostics);
            if (elemType.IsReferenceType || elemType is ArrayTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_STACKALLOC002",
                    DiagnosticSeverity.Error,
                    $"Cannot stackalloc managed type '{elemType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, at.ElementType.Span)));
            }

            var rs = at.RankSpecifiers[0];
            BoundExpression? countExpr = null;

            if (rs.Sizes.Count == 1 && rs.Sizes[0] is not OmittedArraySizeExpressionSyntax)
                countExpr = BindExpression(rs.Sizes[0], context, diagnostics);

            var initializerOpt = BindStackAllocInitializer(node.Initializer, elemType, context, diagnostics);

            if (countExpr == null)
            {
                if (initializerOpt != null)
                {
                    var intType = context.Compilation.GetSpecialType(SpecialType.System_Int32);
                    countExpr = new BoundLiteralExpression(node, intType, initializerOpt.Elements.Length);
                }
                else
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_STACKALLOC003",
                        DiagnosticSeverity.Error,
                        "stackalloc requires a size expression or an initializer.",
                        new Location(context.SemanticModel.SyntaxTree, rs.Span)));
                    countExpr = new BoundBadExpression(node);
                }
            }

            var int32 = context.Compilation.GetSpecialType(SpecialType.System_Int32);
            countExpr = ApplyConversion(
                exprSyntax: rs.Sizes.Count > 0 ? rs.Sizes[0] : node,
                expr: countExpr,
                targetType: int32,
                diagnosticNode: node,
                context: context,
                diagnostics: diagnostics,
                requireImplicit: true);

            if (initializerOpt != null && countExpr.ConstantValueOpt.HasValue && countExpr.ConstantValueOpt.Value is int n)
            {
                if (n != initializerOpt.Elements.Length)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_STACKALLOC004",
                        DiagnosticSeverity.Error,
                        $"An initializer of length '{initializerOpt.Elements.Length}' is expected.",
                        new Location(context.SemanticModel.SyntaxTree, node.Initializer!.Span)));
                }
            }

            var ptrType = context.Compilation.CreatePointerType(elemType);
            return new BoundStackAllocArrayCreationExpression(node, ptrType, elemType, countExpr, initializerOpt);
        }
        private BoundExpression BindImplicitStackAlloc(ImplicitStackAllocArrayCreationExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (node.Initializer is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_STACKALLOC_IMP000",
                    DiagnosticSeverity.Error,
                    "Implicit stackalloc requires an initializer.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            var elems = node.Initializer.Expressions;
            if (elems.Count == 0)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_STACKALLOC_IMP001",
                    DiagnosticSeverity.Error,
                    "Cannot infer stackalloc element type from an empty initializer.",
                    new Location(context.SemanticModel.SyntaxTree, node.Initializer.Span)));
                return new BoundBadExpression(node);
            }

            var first = BindExpression(elems[0], context, diagnostics);
            var elemType = first.Type;

            var builder = ImmutableArray.CreateBuilder<BoundExpression>(elems.Count);
            builder.Add(first);
            for (int i = 1; i < elems.Count; i++)
                builder.Add(BindExpression(elems[i], context, diagnostics));

            for (int i = 0; i < builder.Count; i++)
            {
                builder[i] = ApplyConversion(
                    exprSyntax: elems[i],
                    expr: builder[i],
                    targetType: elemType,
                    diagnosticNode: node,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);
            }

            if (elemType.IsReferenceType || elemType is ArrayTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_STACKALLOC_IMP002",
                    DiagnosticSeverity.Error,
                    $"Cannot stackalloc managed type '{elemType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
            }

            var initializerOpt = new BoundArrayInitializerExpression(node.Initializer, elemType, builder.ToImmutable());
            context.Recorder.RecordBound(node.Initializer, initializerOpt);

            var intType = context.Compilation.GetSpecialType(SpecialType.System_Int32);
            var countExpr = new BoundLiteralExpression(node, intType, initializerOpt.Elements.Length);

            var ptrType = context.Compilation.CreatePointerType(elemType);
            return new BoundStackAllocArrayCreationExpression(node, ptrType, elemType, countExpr, initializerOpt);
        }
        private BoundArrayInitializerExpression? BindStackAllocInitializer(
            InitializerExpressionSyntax? init,
            TypeSymbol elemType,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (init is null)
                return null;

            var exprs = init.Expressions;
            var b = ImmutableArray.CreateBuilder<BoundExpression>(exprs.Count);
            for (int i = 0; i < exprs.Count; i++)
            {
                var e = BindExpression(exprs[i], context, diagnostics);
                e = ApplyConversion(exprs[i], e, elemType, init, context, diagnostics, requireImplicit: true);
                b.Add(e);
            }

            var boundInit = new BoundArrayInitializerExpression(init, elemType, b.ToImmutable());
            context.Recorder.RecordBound(init, boundInit);
            return boundInit;
        }
        private BoundStatement BindReturn(ReturnStatementSyntax rs, BindingContext context, DiagnosticBag diagnostics)
        {
            _flow.ValidateMethodExitTransfer(rs, context, diagnostics);
            var containingMethod = context.ContainingSymbol as MethodSymbol;
            var returnType = containingMethod?.ReturnType
                ?? context.Compilation.GetSpecialType(SpecialType.System_Void);

            if (rs.Expression is null)
                return new BoundReturnStatement(rs, null);

            var expr = BindExpression(rs.Expression, context, diagnostics);
            if (returnType is ByRefTypeSymbol byRefReturnType)
            {
                expr = BindRefReturningExpression(
                    rs.Expression,
                    expr,
                    byRefReturnType,
                    rs,
                    context,
                    diagnostics);

                return new BoundReturnStatement(rs, expr);
            }
            expr = ApplyConversion(
                exprSyntax: rs.Expression,
                expr: expr,
                targetType: returnType,
                diagnosticNode: rs,
                context: context,
                diagnostics: diagnostics,
                requireImplicit: true);
            return new BoundReturnStatement(rs, expr);
        }
        private BoundExpression BindRefReturningExpression(
            ExpressionSyntax syntax,
            BoundExpression expr,
            ByRefTypeSymbol targetType,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (expr is not BoundRefExpression br || br.Type is not ByRefTypeSymbol sourceByRef)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REFRET001",
                    DiagnosticSeverity.Error,
                    "A ref return expression must be a ref expression.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));

                return new BoundBadExpression((ExpressionSyntax)diagnosticNode);
            }

            if (!ReferenceEquals(sourceByRef.ElementType, targetType.ElementType))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REFRET001",
                    DiagnosticSeverity.Error,
                    $"Ref type mismatch: expected ref '{targetType.ElementType.Name}', got ref '{sourceByRef.ElementType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));

                return new BoundBadExpression((ExpressionSyntax)diagnosticNode);
            }

            return br;
        }
        private BoundStatement BindThrow(ThrowStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            _flow.ValidateMethodExitTransfer(node, context, diagnostics);

            if (node.Expression is null)
            {
                if (!_flow.IsInsideCatchRegion)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_THROW000",
                        DiagnosticSeverity.Error,
                        "A rethrow statement can only be used inside a catch block.",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    return new BoundBadStatement(node);
                }

                return new BoundThrowStatement(node, expressionOpt: null);
            }

            var exType = GetSystemExceptionTypeOrReport(node, context, diagnostics);
            if (exType.Kind == SymbolKind.Error)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_THROW001",
                    DiagnosticSeverity.Error,
                    "Core library type 'System.Exception' is required for throw.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadStatement(node);
            }

            var expr = BindExpression(node.Expression, context, diagnostics);
            expr = ApplyConversion(
                exprSyntax: node.Expression,
                expr: expr,
                targetType: exType,
                diagnosticNode: node,
                context: context,
                diagnostics: diagnostics,
                requireImplicit: true);

            return new BoundThrowStatement(node, expr);
        }
        private BoundStatement BindBreak(BreakStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (!_flow.TryGetCurrentLoop(out var breakLabel, out _))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FLOW001",
                    DiagnosticSeverity.Error,
                    "The break statement is not within a loop or switch.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));

                return new BoundBadStatement(node);
            }
            _flow.ValidateBranchTransfer(node, breakLabel, context, diagnostics);
            return new BoundBreakStatement(node, breakLabel);
        }

        private BoundStatement BindContinue(ContinueStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (!_flow.TryGetCurrentLoop(out var breakLabel, out var continueLabel)
                || ReferenceEquals(breakLabel, continueLabel))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FLOW002",
                    DiagnosticSeverity.Error,
                    "The continue statement is not within a loop.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));

                return new BoundBadStatement(node);
            }
            _flow.ValidateBranchTransfer(node, continueLabel, context, diagnostics);
            return new BoundContinueStatement(node, continueLabel);
        }
        private BoundExpression BindThis(ThisExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (context.ContainingSymbol is not MethodSymbol method)
            {
                diagnostics.Add(new Diagnostic("CN_THIS000", DiagnosticSeverity.Error,
                    "The 'this' keyword is only valid in instance members.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            if (method.IsStatic)
            {
                diagnostics.Add(new Diagnostic("CN_THIS001", DiagnosticSeverity.Error,
                    "Cannot use 'this' in a static method.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            if (method.ContainingSymbol is not NamedTypeSymbol containingType)
            {
                diagnostics.Add(new Diagnostic("CN_THIS002", DiagnosticSeverity.Error,
                    "Cannot resolve containing type for 'this'.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            return new BoundThisExpression(node, containingType);
        }
        public override TypeSymbol BindType(TypeSyntax syntax, BindingContext context, DiagnosticBag diagnostics)
        {
            if (syntax is PointerTypeSyntax p)
            {
                if ((Flags & BinderFlags.UnsafeRegion) == 0)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_UNSAFE_TYPE001",
                        DiagnosticSeverity.Warning,
                        "Pointer types may only be used in an unsafe context.",
                        new Location(context.SemanticModel.SyntaxTree, p.Span)));
                }

                var elem = BindType(p.ElementType, context, diagnostics);

                if (elem.IsReferenceType || elem is ArrayTypeSymbol)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_PTRTYPE001",
                        DiagnosticSeverity.Error,
                        $"Cannot take a pointer to managed type '{elem.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, p.ElementType.Span)));
                    return new ErrorTypeSymbol("ptr", containing: null, ImmutableArray<Location>.Empty);
                }

                return context.Compilation.CreatePointerType(elem);
            }

            if (Parent != null)
                return Parent.BindType(syntax, context, diagnostics);

            diagnostics.Add(new Diagnostic("CN_TYPE000", DiagnosticSeverity.Error,
                $"No parent binder to bind type: {syntax.Kind}",
                new Location(context.SemanticModel.SyntaxTree, syntax.Span)));

            return new ErrorTypeSymbol("type", containing: null, ImmutableArray<Location>.Empty);
        }
        private BoundStatement BindBlock(BlockSyntax block, BindingContext context, DiagnosticBag diagnostics)
        {
            var scope = new LocalScopeBinder(parent: this, flags: Flags, containing: _containing);

            scope.PredeclareLocalFunctionsInBlock(block, context, diagnostics);

            var statements = ImmutableArray.CreateBuilder<BoundStatement>();

            foreach (var statement in block.Statements)
                statements.Add(scope.BindStatement(statement, context, diagnostics));

            return new BoundBlockStatement(block, statements.ToImmutable());
        }
        internal BoundStatement? BindConstructorInitializer(
            ConstructorDeclarationSyntax ctorSyntax,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var initSyntax = ctorSyntax.Initializer;
            if (initSyntax is null)
                return null;
            if (context.ContainingSymbol is not MethodSymbol method || !method.IsConstructor)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CTORINIT000",
                    DiagnosticSeverity.Error,
                    "Constructor initializer can only appear in a constructor.",
                    new Location(context.SemanticModel.SyntaxTree, initSyntax.Span)));
                var empty = new BoundEmptyStatement(initSyntax);
                context.Recorder.RecordBound(initSyntax, empty);
                return empty;
            }
            if (method.IsStatic)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CTORINIT001",
                    DiagnosticSeverity.Error,
                    "Static constructors cannot have a constructor initializer.",
                    new Location(context.SemanticModel.SyntaxTree, initSyntax.Span)));
                var empty = new BoundEmptyStatement(initSyntax);
                context.Recorder.RecordBound(initSyntax, empty);
                return empty;
            }
            if (method.ContainingSymbol is not NamedTypeSymbol containingType)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CTORINIT002",
                    DiagnosticSeverity.Error,
                    "Cannot resolve containing type for constructor initializer.",
                    new Location(context.SemanticModel.SyntaxTree, initSyntax.Span)));
                var empty = new BoundEmptyStatement(initSyntax);
                context.Recorder.RecordBound(initSyntax, empty);
                return empty;
            }
            bool isThisInitializer = initSyntax.ThisOrBaseKeyword.Kind == SyntaxKind.ThisKeyword;
            bool isBaseInitializer = initSyntax.ThisOrBaseKeyword.Kind == SyntaxKind.BaseKeyword;
            if (!isThisInitializer && !isBaseInitializer)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CTORINIT003",
                    DiagnosticSeverity.Error,
                    "Constructor initializer must be this or base.",
                    new Location(context.SemanticModel.SyntaxTree, initSyntax.Span)));
                var empty = new BoundEmptyStatement(initSyntax);
                context.Recorder.RecordBound(initSyntax, empty);
                return empty;
            }

            NamedTypeSymbol targetType;
            if (isThisInitializer)
            {
                targetType = containingType;
            }
            else
            {
                if (containingType.TypeKind != TypeKind.Class)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_CTORINIT004",
                        DiagnosticSeverity.Error,
                        "Only class constructors can use base constructor inializers.",
                        new Location(context.SemanticModel.SyntaxTree, initSyntax.Span)));
                    var empty = new BoundEmptyStatement(initSyntax);
                    context.Recorder.RecordBound(initSyntax, empty);
                    return empty;
                }
                var baseType = containingType.BaseType as NamedTypeSymbol;
                if (baseType is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_CTORINIT004",
                        DiagnosticSeverity.Error,
                        "Only class constructors can use base constructor inializers.",
                        new Location(context.SemanticModel.SyntaxTree, initSyntax.Span)));
                    var empty = new BoundEmptyStatement(initSyntax);
                    context.Recorder.RecordBound(initSyntax, empty);
                    return empty;
                }
                targetType = baseType;
            }
            var argSyntaxes = initSyntax.ArgumentList.Arguments;
            var argsBuilder = ImmutableArray.CreateBuilder<BoundExpression>(argSyntaxes.Count);
            for (int i = 0; i < argSyntaxes.Count; i++)
                argsBuilder.Add(BindExpression(argSyntaxes[i].Expression, context, diagnostics));
            var boundArgs = argsBuilder.ToImmutable();
            var ctorCandidates = LookupConstructors(targetType)
                .Where(c => AccessibilityHelper.IsAccessible(c, context))
                .ToImmutableArray();
            if (ctorCandidates.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CTORINIT005",
                    DiagnosticSeverity.Error,
                    $"No accessible constructor found for '{targetType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, initSyntax.Span)));
                var empty = new BoundEmptyStatement(initSyntax);
                context.Recorder.RecordBound(initSyntax, empty);
                return empty;
            }
            if (!TryResolveOverload(
                candidates: ctorCandidates,
                args: boundArgs,
                getArgExprSyntax: i => argSyntaxes[i].Expression,
                getArgRefKindKeyword: i => argSyntaxes[i].RefKindKeyword,
                getArgName: i => argSyntaxes[i].NameColon?.Name.Identifier.ValueText,
                chosen: out var chosen,
                convertedArgs: out var convertedArgs,
                context: context,
                diagnostics: diagnostics,
                diagnosticNode: initSyntax))
            {
                var empty = new BoundEmptyStatement(initSyntax);
                context.Recorder.RecordBound(initSyntax, empty);
                return empty;
            }

            if (isThisInitializer && ReferenceEquals(chosen, method))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CTORINIT006",
                    DiagnosticSeverity.Error,
                    "Constructor cannot delegate to itself.",
                    new Location(context.SemanticModel.SyntaxTree, initSyntax.Span)));

                var empty = new BoundEmptyStatement(initSyntax);
                context.Recorder.RecordBound(initSyntax, empty);
                return empty;
            }

            ExpressionSyntax receiverSyntax = isBaseInitializer
                ? new BaseExpressionSyntax(initSyntax.ThisOrBaseKeyword)
                : new ThisExpressionSyntax(initSyntax.ThisOrBaseKeyword);

            var receiver = new BoundThisExpression(receiverSyntax, containingType);
            var call = new BoundCallExpression(initSyntax, receiver, chosen!, convertedArgs);
            var stmt = new BoundExpressionStatement(initSyntax, call);

            context.Recorder.RecordBound(initSyntax, stmt);
            return stmt;
        }
        internal BoundExpression ApplyConversion(
            ExpressionSyntax exprSyntax,
            BoundExpression expr,
            TypeSymbol targetType,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics,
            bool requireImplicit)
        {
            if (IsTrueErrorType(targetType))
            {
                var converted = CreateErrorConversion(exprSyntax, expr, targetType);
                context.Recorder.RecordBound(exprSyntax, converted);
                return converted;
            }
            // Target typed new expression
            if (exprSyntax is ImplicitObjectCreationExpressionSyntax ioc)
            {
                BoundExpression bound;
                if (expr is BoundUnboundImplicitObjectCreationExpression unbound)
                    bound = BindImplicitObjectCreation(ioc, targetType, unbound.Arguments, context, diagnostics);
                else
                    bound = BindImplicitObjectCreation(ioc, targetType, context, diagnostics);
                context.Recorder.RecordBound(exprSyntax, bound);
                return bound;
            }
            // Target typed default literal
            if (expr.Type is DefaultLiteralTypeSymbol)
            {
                if (targetType.SpecialType == SpecialType.System_Void || targetType is ByRefTypeSymbol)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_CONV001",
                        DiagnosticSeverity.Error,
                        $"No {(requireImplicit ? "implicit " : "")}conversion from '{expr.Type.Name}' to '{targetType.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                    var bad = new BoundBadExpression(exprSyntax);
                    bad.SetType(targetType);
                    context.Recorder.RecordBound(exprSyntax, bad);
                    return bad;
                }

                var lowered = MakeDefaultValue(exprSyntax, targetType);
                context.Recorder.RecordBound(exprSyntax, lowered);
                return lowered;
            }

            if (ReferenceEquals(expr.Type, targetType))
                return expr;

            if (expr is BoundThrowExpression te)
            {
                te.SetType(targetType);
                return te;
            }

            if (ShouldSuppressCascade(expr))
            {
                var converted = CreateErrorConversion(exprSyntax, expr, targetType);
                context.Recorder.RecordBound(exprSyntax, converted);
                return converted;
            }
            var conv = ClassifyConversion(expr, targetType, context);

            if (expr is BoundStackAllocArrayCreationExpression && conv.Kind != ConversionKind.ImplicitStackAlloc)
                EnsureUnsafe(exprSyntax, context, diagnostics);

            if (conv.Kind == ConversionKind.ImplicitStackAlloc)
            {
                if (expr is not BoundStackAllocArrayCreationExpression sa)
                    throw new InvalidOperationException("ImplicitStackAlloc conversion requires a stackalloc expression.");
                if (!TryGetSpanLikeElementType(targetType, out var spanLikeType, out var spanElemType) ||
                    !ReferenceEquals(sa.ElementType, spanElemType))
                {
                    diagnostics.Add(new Diagnostic(
                    "CN_CONV001",
                    DiagnosticSeverity.Error,
                    requireImplicit
                        ? $"No implicit conversion from '{expr.Type.Name}' to '{targetType.Name}'."
                        : $"No conversion from '{expr.Type.Name}' to '{targetType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                    var bad = new BoundBadExpression(exprSyntax);
                    bad.SetType(targetType);

                    context.Recorder.RecordBound(exprSyntax, bad);
                    return bad;
                }
                var lowered = LowerStackAllocToSpanCreation(exprSyntax, sa, spanLikeType, diagnosticNode, context, diagnostics);
                context.Recorder.RecordBound(exprSyntax, lowered);
                return lowered;
            }
            if (!conv.Exists || (requireImplicit && !conv.IsImplicit))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CONV001",
                    DiagnosticSeverity.Error,
                    requireImplicit
                        ? $"No implicit conversion from '{expr.Type.Name}' to '{targetType.Name}'."
                        : $"No conversion from '{expr.Type.Name}' to '{targetType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                var bad = new BoundBadExpression(exprSyntax);
                bad.SetType(targetType);

                context.Recorder.RecordBound(exprSyntax, bad);
                return bad;
            }
            if (conv.Kind is ConversionKind.ImplicitNullable or ConversionKind.ExplicitNullable)
            {
                var lowered = LowerNullableConversion(
                    exprSyntax, expr, targetType, diagnosticNode, context, diagnostics, requireImplicit);
                context.Recorder.RecordBound(exprSyntax, lowered);
                return lowered;
            }
            if (conv.Kind == ConversionKind.UserDefined && conv.UserDefinedMethod is MethodSymbol userConv)
            {
                BoundExpression arg = expr;
                var paramType = userConv.Parameters[0].Type;

                if (!ReferenceEquals(arg.Type, paramType))
                {
                    var pre = ClassifyConversion(arg, paramType); // static overload
                    if (!pre.Exists || (requireImplicit && !pre.IsImplicit))
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_CONV001",
                            DiagnosticSeverity.Error,
                            requireImplicit
                                ? $"No implicit conversion from '{expr.Type.Name}' to '{targetType.Name}'."
                                : $"No conversion from '{expr.Type.Name}' to '{targetType.Name}'.",
                            new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                        var bad = new BoundBadExpression(exprSyntax);
                        bad.SetType(targetType);
                        context.Recorder.RecordBound(exprSyntax, bad);
                        return bad;
                    }

                    bool preChecked = IsCheckedOverflowContext && pre.Kind == ConversionKind.ExplicitNumeric;
                    arg = new BoundConversionExpression(exprSyntax, paramType, arg, pre, preChecked);
                }
                BoundExpression converted = new BoundCallExpression(
                    exprSyntax,
                    receiverOpt: null,
                    userConv,
                    ImmutableArray.Create(arg));

                if (!ReferenceEquals(converted.Type, targetType))
                {
                    var post = ClassifyConversion(converted, targetType); // static overload
                    if (!post.Exists || (requireImplicit && !post.IsImplicit))
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_CONV001",
                            DiagnosticSeverity.Error,
                            requireImplicit
                                ? $"No implicit conversion from '{expr.Type.Name}' to '{targetType.Name}'."
                                : $"No conversion from '{expr.Type.Name}' to '{targetType.Name}'.",
                            new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                        var bad = new BoundBadExpression(exprSyntax);
                        bad.SetType(targetType);
                        context.Recorder.RecordBound(exprSyntax, bad);
                        return bad;
                    }

                    bool postChecked = IsCheckedOverflowContext && post.Kind == ConversionKind.ExplicitNumeric;
                    converted = new BoundConversionExpression(exprSyntax, targetType, converted, post, postChecked);
                }

                context.Recorder.RecordBound(exprSyntax, converted);
                return converted;
            }
            {
                bool isChecked = IsCheckedOverflowContext && conv.Kind == ConversionKind.ExplicitNumeric;
                var converted = new BoundConversionExpression(exprSyntax, targetType, expr, conv, isChecked);

                context.Recorder.RecordBound(exprSyntax, converted);
                return converted;
            }
        }
        private BoundExpression LowerNullableConversion(
            ExpressionSyntax exprSyntax,
            BoundExpression expr,
            TypeSymbol targetType,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics,
            bool requireImplicit)
        {
            if (!TryGetSystemNullableInfo(targetType, out var targetNullable, out var targetUnderlying))
                return new BoundBadExpression(exprSyntax);

            // Nullable<S> to Nullable<T>
            if (TryGetSystemNullableInfo(expr.Type, out var fromNullable, out var fromUnderlying))
            {
                var hasValueGet = FindNullableHasValueGetter(fromNullable);
                var gv = FindNullableGetValueOrDefault(fromNullable, fromUnderlying);

                if (hasValueGet is null || gv is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_NULLCONV000",
                        DiagnosticSeverity.Error,
                        "Missing Nullable<T> members (HasValue / GetValueOrDefault).",
                        new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                    return new BoundBadExpression(exprSyntax);
                }

                var srcTmp = NewNullableTemp(expr.Type);
                var srcDecl = new BoundLocalDeclarationStatement(diagnosticNode, srcTmp, expr);
                var src = new BoundLocalExpression(diagnosticNode, srcTmp);

                var cond = new BoundCallExpression(diagnosticNode, src, hasValueGet, ImmutableArray<BoundExpression>.Empty);

                var srcVal = new BoundCallExpression(diagnosticNode, src, gv, ImmutableArray<BoundExpression>.Empty);

                // convert underlying
                var convertedUnderlying = ApplyConversion(
                    exprSyntax: exprSyntax,
                    expr: srcVal,
                    targetType: targetUnderlying,
                    diagnosticNode: diagnosticNode,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: requireImplicit);

                var whenTrue = WrapIntoNullable(diagnosticNode, targetNullable, targetUnderlying, convertedUnderlying);
                var whenFalse = MakeDefaultValue(diagnosticNode, targetType);

                var condSyntax = new ConditionalExpressionSyntax(
                    condition: exprSyntax,
                    questionToken: default,
                    whenTrue: exprSyntax,
                    colonToken: default,
                    whenFalse: exprSyntax);

                var value = new BoundConditionalExpression(condSyntax, targetType, cond, whenTrue, whenFalse, Optional<object>.None);

                return new BoundSequenceExpression(
                    diagnosticNode,
                    locals: ImmutableArray.Create(srcTmp),
                    sideEffects: ImmutableArray.Create<BoundStatement>(srcDecl),
                    value: value);
            }

            // S to Nullable<T>
            {
                var srcTmp = NewNullableTemp(expr.Type);
                var srcDecl = new BoundLocalDeclarationStatement(diagnosticNode, srcTmp, expr);
                var src = new BoundLocalExpression(diagnosticNode, srcTmp);

                var convertedUnderlying = ApplyConversion(
                    exprSyntax: exprSyntax,
                    expr: src,
                    targetType: targetUnderlying,
                    diagnosticNode: diagnosticNode,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: requireImplicit);

                var wrapped = WrapIntoNullable(diagnosticNode, targetNullable, targetUnderlying, convertedUnderlying);

                return new BoundSequenceExpression(
                    diagnosticNode,
                    locals: ImmutableArray.Create(srcTmp),
                    sideEffects: ImmutableArray.Create<BoundStatement>(srcDecl),
                    value: wrapped);
            }
        }
        private BoundExpression WrapIntoNullable(SyntaxNode syntax, NamedTypeSymbol nullableType, TypeSymbol underlying, BoundExpression value)
        {
            var ctor = FindNullableCtor(nullableType, underlying);
            if (ctor is null)
                return new BoundBadExpression(syntax);

            var tmp = NewNullableTemp(nullableType);
            var decl = new BoundLocalDeclarationStatement(syntax, tmp, initializer: null);

            var recv = new BoundLocalExpression(syntax, tmp);
            var call = new BoundCallExpression(syntax, recv, ctor, ImmutableArray.Create(value));
            var callStmt = new BoundExpressionStatement(syntax, call);

            return new BoundSequenceExpression(
                syntax,
                locals: ImmutableArray.Create(tmp),
                sideEffects: ImmutableArray.Create<BoundStatement>(decl, callStmt),
                value: new BoundLocalExpression(syntax, tmp));
        }
        private static MethodSymbol? FindNullableHasValueGetter(NamedTypeSymbol nullableType)
        {
            foreach (var m in nullableType.GetMembers())
            {
                if (m is PropertySymbol ps && string.Equals(ps.Name, "HasValue", StringComparison.Ordinal))
                    return ps.GetMethod;
            }
            return null;
        }
        private BoundExpression MakeDefaultValue(SyntaxNode syntax, TypeSymbol type)
        {
            var tmp = NewNullableTemp(type);
            var decl = new BoundLocalDeclarationStatement(syntax, tmp, initializer: null);
            var value = new BoundLocalExpression(syntax, tmp);
            return new BoundSequenceExpression(
                syntax,
                locals: ImmutableArray.Create(tmp),
                sideEffects: ImmutableArray.Create<BoundStatement>(decl),
                value: value);
        }
        private LocalSymbol NewNullableTemp(TypeSymbol type)
        {
            var name = $"$nullable_tmp{_tempId++}";
            return new LocalSymbol(name, _containing, type, ImmutableArray<Location>.Empty);
        }

        private static MethodSymbol? FindNullableCtor(NamedTypeSymbol nullableType, TypeSymbol underlying)
        {
            foreach (var m in nullableType.GetMembers())
            {
                if (m is MethodSymbol ms && ms.IsConstructor && !ms.IsStatic && ms.Parameters.Length == 1)
                {
                    if (ReferenceEquals(ms.Parameters[0].Type, underlying))
                        return ms;
                }
            }
            return null;
        }
        private static MethodSymbol? FindNullableGetValueOrDefault(NamedTypeSymbol nullableType, TypeSymbol underlying)
        {
            foreach (var m in nullableType.GetMembers())
            {
                if (m is MethodSymbol ms && !ms.IsStatic
                    && string.Equals(ms.Name, "GetValueOrDefault", StringComparison.Ordinal)
                    && ms.Parameters.Length == 0
                    && ReferenceEquals(ms.ReturnType, underlying))
                {
                    return ms;
                }
            }
            return null;
        }
        private bool CanBindTargetTypedObjectCreation(
            BoundUnboundImplicitObjectCreationExpression unbound,
            TypeSymbol targetType,
            BindingContext context)
        {
            if (targetType is not NamedTypeSymbol nt)
                return false;
            var ioc = (ImplicitObjectCreationExpressionSyntax)unbound.Syntax;
            var argSyntaxes = ioc.ArgumentList.Arguments;
            var boundArgs = unbound.Arguments;
            if (argSyntaxes.Count != boundArgs.Length)
                return false;
            var allCtorCandidates = LookupConstructors(nt);
            var ctorCandidates = allCtorCandidates
                .Where(c => AccessibilityHelper.IsAccessible(c, context))
                .ToImmutableArray();

            if (nt.TypeKind == TypeKind.Struct && boundArgs.Length == 0)
            {
                bool hasDeclaredParameterlessCtor = false;
                for (int i = 0; i < allCtorCandidates.Length; i++)
                    if (allCtorCandidates[i].Parameters.Length == 0)
                    {
                        hasDeclaredParameterlessCtor = true;
                        break;
                    }
                if (!hasDeclaredParameterlessCtor)
                    return true;
            }
            if (ctorCandidates.IsDefaultOrEmpty)
                return false;

            for (int i = 0; i < ctorCandidates.Length; i++)
            {
                var m = ctorCandidates[i];
                if (m.Parameters.Length != boundArgs.Length)
                    continue;
                bool ok = true;
                for (int a = 0; a < boundArgs.Length; a++)
                {
                    if (!ArgumentRefKindMatchesParameter(argSyntaxes[a].RefKindKeyword, m.Parameters[a]))
                    {
                        ok = false;
                        break;
                    }
                    var conv = ClassifyConversion(boundArgs[a], m.Parameters[a].Type, context);
                    if (!conv.Exists || !conv.IsImplicit)
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) return true;
            }
            return false;
        }
        private Conversion ClassifyConversion(BoundExpression expr, TypeSymbol target, BindingContext context)
        {
            if (expr is BoundUnboundImplicitObjectCreationExpression unbound)
                return CanBindTargetTypedObjectCreation(unbound, target, context)
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
        internal static Conversion ClassifyConversion(BoundExpression expr, TypeSymbol target)
        {
            if (ReferenceEquals(expr.Type, target))
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
            if (expr.Type is TypeParameterSymbol tp)
            {
                if ((tp.GenericConstraint & GenericConstraintsFlags.AllowsRefStruct) != 0)
                    return new Conversion(ConversionKind.None);

                // T -> object
                if (target.SpecialType == SpecialType.System_Object)
                    return new Conversion(ConversionKind.Boxing);
            }

            // boxing
            if (expr.Type.IsValueType && target.SpecialType == SpecialType.System_Object)
            {
                if (exprHasRefLike)
                    return new Conversion(ConversionKind.None);

                return new Conversion(ConversionKind.Boxing);
            }

            // unboxing explicit
            if (expr.Type.SpecialType == SpecialType.System_Object && target.IsValueType)
            {
                if (targetHasRefLike)
                    return new Conversion(ConversionKind.None);

                return new Conversion(ConversionKind.Unboxing);
            }

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
                    (RuntimeTypeSystem.PointerSize == 4 ? int.MinValue : long.MinValue,
                    RuntimeTypeSystem.PointerSize == 4 ? int.MaxValue : long.MaxValue, true),
                SpecialType.System_UIntPtr =>
                    (RuntimeTypeSystem.PointerSize == 4 ? uint.MinValue : ulong.MinValue,
                    RuntimeTypeSystem.PointerSize == 4 ? uint.MaxValue : ulong.MaxValue, true),
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
        private static bool HasImplicitReferenceConversion(TypeSymbol source, TypeSymbol destination)
        {
            if (!source.IsReferenceType || !destination.IsReferenceType)
                return false;

            if (ReferenceEquals(source, destination))
                return true;

            // Normal upcast
            if (IsBaseTypeOf(destination, source))
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

            if (ReferenceEquals(source, destination))
                return true;

            // Normal downcast
            if (IsBaseTypeOf(source, destination))
                return true;

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
        private BoundStatement BindTry(TryStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (node.Catches.Count == 0 && node.Finally == null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TRY000",
                    DiagnosticSeverity.Error,
                    "A try statement must have at least one catch clause or a finally clause.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
            }
            _flow.PushTryRegion();
            BoundStatement tryStmt;
            try
            {
                tryStmt = BindStatement(node.Block, context, diagnostics);
            }
            finally
            {
                _flow.PopExceptionRegion();
            }
            var tryBlock = tryStmt as BoundBlockStatement
                ?? new BoundBlockStatement(node.Block, ImmutableArray<BoundStatement>.Empty);
            if (tryStmt is not BoundBlockStatement)
                tryBlock.SetHasErrors();

            var catchBlocks = ImmutableArray.CreateBuilder<BoundCatchBlock>(node.Catches.Count);
            var prevUnconditionalCatchTypes = new List<TypeSymbol>(Math.Max(0, node.Catches.Count));

            for (int i = 0; i < node.Catches.Count; i++)
            {
                var c = node.Catches[i];
                if (c.Declaration == null && c.Filter == null && i != node.Catches.Count - 1)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_TRY001",
                        DiagnosticSeverity.Error,
                        "A catch clause without an exception type must be the last catch clause.",
                        new Location(context.SemanticModel.SyntaxTree, c.Span)));
                }

                var boundCatch = BindCatchClause(c, context, diagnostics);
                var currentType = boundCatch.ExceptionType;
                if (currentType.Kind != SymbolKind.Error)
                {
                    for (int j = 0; j < prevUnconditionalCatchTypes.Count; j++)
                    {
                        var prev = prevUnconditionalCatchTypes[j];
                        if (prev.Kind != SymbolKind.Error)
                            continue;

                        if (IsSameOrDerivedFrom(currentType, prev))
                        {
                            diagnostics.Add(new Diagnostic(
                                "CN_TRY002",
                                DiagnosticSeverity.Error,
                                "A previous catch clause already catches all exceptions of this or of a super type.",
                                new Location(context.SemanticModel.SyntaxTree, c.Span)));
                            break;
                        }
                    }
                }

                if (c.Filter == null)
                    prevUnconditionalCatchTypes.Add(currentType);

                catchBlocks.Add(boundCatch);
            }
            BoundBlockStatement? finallyBlockOpt = null;
            if (node.Finally != null)
            {
                _flow.PushFinallyRegion();
                BoundStatement finallyStmt;
                try
                {
                    finallyStmt = BindStatement(node.Finally.Block, context, diagnostics);
                }
                finally
                {
                    _flow.PopExceptionRegion();
                }
                finallyBlockOpt = finallyStmt as BoundBlockStatement
                    ?? new BoundBlockStatement(node.Finally.Block, ImmutableArray<BoundStatement>.Empty);
                if (finallyStmt is not BoundBlockStatement)
                    finallyBlockOpt.SetHasErrors();
            }

            return new BoundTryStatement(node, tryBlock, catchBlocks.ToImmutable(), finallyBlockOpt);
        }
        private BoundCatchBlock BindCatchClause(CatchClauseSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var scope = new LocalScopeBinder(parent: this, flags: Flags, containing: _containing);

            TypeSymbol exceptionType;
            LocalSymbol? exceptionLocalOpt = null;

            if (node.Declaration != null)
            {
                exceptionType = scope.BindType(node.Declaration.Type, context, diagnostics);
                exceptionType = scope.ValidateCatchExceptionType(exceptionType, node.Declaration.Type, context, diagnostics);
                exceptionLocalOpt = scope.DeclareCatchExceptionLocal(node.Declaration, exceptionType, context, diagnostics);
            }
            else
            {
                exceptionType = context.Compilation.GetSpecialType(SpecialType.System_Object);
            }

            BoundExpression? filterOpt = null;
            if (node.Filter != null)
            {
                var boolType = context.Compilation.GetSpecialType(SpecialType.System_Boolean);
                var raw = scope.BindExpression(node.Filter.FilterExpression, context, diagnostics);
                filterOpt = scope.ApplyConversion(
                    exprSyntax: node.Filter.FilterExpression,
                    expr: raw,
                    targetType: boolType,
                    diagnosticNode: node.Filter.FilterExpression,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);
            }

            _flow.PushCatchRegion();
            BoundStatement bodyStmt;
            try
            {
                bodyStmt = scope.BindStatement(node.Block, context, diagnostics);
            }
            finally
            {
                _flow.PopExceptionRegion();
            }
            var body = bodyStmt as BoundBlockStatement
                ?? new BoundBlockStatement(node.Block, ImmutableArray<BoundStatement>.Empty);
            if (bodyStmt is not BoundBlockStatement)
                body.SetHasErrors();

            var bound = new BoundCatchBlock(node, exceptionType, exceptionLocalOpt, filterOpt, body);

            context.Recorder.RecordBound(node, bound);

            return bound;
        }
        private LocalSymbol? DeclareCatchExceptionLocal(
            CatchDeclarationSyntax decl,
            TypeSymbol exceptionType,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (decl.Identifier.Span.Length == 0)
                return null;

            var name = decl.Identifier.ValueText ?? string.Empty;
            if (name.Length == 0)
                return null;

            if (IsNameDeclaredInEnclosingScopes(name))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CATCHVAR000",
                    DiagnosticSeverity.Error,
                    $"A local named '{name}' is already declared in this scope.",
                    new Location(context.SemanticModel.SyntaxTree, decl.Identifier.Span)));
            }

            var local = new LocalSymbol(
                name: name,
                containing: _containing,
                type: exceptionType,
                locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, decl.Identifier.Span)));

            _locals[name] = local;
            context.Recorder.RecordDeclared(decl, local);

            return local;
        }
        private TypeSymbol ValidateCatchExceptionType(
            TypeSymbol exceptionType,
            TypeSyntax typeSyntax,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (exceptionType.Kind == SymbolKind.Error)
                return exceptionType;
            if (exceptionType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TRYTYPE000",
                    DiagnosticSeverity.Error,
                    "Cannot catch 'void'.",
                    new Location(context.SemanticModel.SyntaxTree, typeSyntax.Span)));
                return new ErrorTypeSymbol("void", containing: null, ImmutableArray<Location>.Empty);
            }

            if (exceptionType is PointerTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TRYTYPE001",
                    DiagnosticSeverity.Error,
                    "Cannot catch a pointer type.",
                    new Location(context.SemanticModel.SyntaxTree, typeSyntax.Span)));
                return new ErrorTypeSymbol("ptr", containing: null, ImmutableArray<Location>.Empty);
            }

            if (exceptionType is ArrayTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TRYTYPE002",
                    DiagnosticSeverity.Error,
                    "Cannot catch an array type.",
                    new Location(context.SemanticModel.SyntaxTree, typeSyntax.Span)));
                return new ErrorTypeSymbol("array", containing: null, ImmutableArray<Location>.Empty);
            }

            if (exceptionType is TypeParameterSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TRYTYPE003",
                    DiagnosticSeverity.Error,
                    "Cannot catch a type parameter.",
                    new Location(context.SemanticModel.SyntaxTree, typeSyntax.Span)));
                return new ErrorTypeSymbol("typeparam", containing: null, ImmutableArray<Location>.Empty);
            }

            if (exceptionType is NamedTypeSymbol nt)
            {
                if (nt.TypeKind != TypeKind.Class)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_TRYTYPE004",
                        DiagnosticSeverity.Error,
                        "The type caught must be a class type.",
                        new Location(context.SemanticModel.SyntaxTree, typeSyntax.Span)));
                }
            }
            else
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TRYTYPE004",
                    DiagnosticSeverity.Error,
                    "The type caught must be a class type.",
                    new Location(context.SemanticModel.SyntaxTree, typeSyntax.Span)));
            }

            var sysException = GetSystemExceptionTypeOrReport(typeSyntax, context, diagnostics);
            if (sysException.Kind != SymbolKind.Error &&
                !IsSameOrDerivedFrom(exceptionType, sysException))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TRYTYPE005",
                    DiagnosticSeverity.Error,
                    "The type caught must be derived from 'System.Exception'.",
                    new Location(context.SemanticModel.SyntaxTree, typeSyntax.Span)));
            }

            return exceptionType;
        }
        private static bool IsSameOrDerivedFrom(TypeSymbol type, TypeSymbol baseType)
        {
            for (TypeSymbol? cur = type; cur != null; cur = cur.BaseType)
                if (ReferenceEquals(cur, baseType))
                    return true;

            return false;
        }

        private static NamedTypeSymbol GetSystemExceptionTypeOrReport(
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var exceptionType = context.Compilation.GetSpecialType(SpecialType.System_Exception);
            if (exceptionType.Kind == SymbolKind.Error)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_EXC000",
                    DiagnosticSeverity.Error,
                    "Core library type 'System.Exception' is required for try/catch/throw.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
            }
            return exceptionType;
        }
        private BoundStatement BindIf(IfStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var boolType = context.Compilation.GetSpecialType(SpecialType.System_Boolean);

            var condition = BindExpression(node.Condition, context, diagnostics);
            condition = ApplyConversion(
                exprSyntax: node.Condition,
                expr: condition,
                targetType: boolType,
                diagnosticNode: node.Condition,
                context: context,
                diagnostics: diagnostics,
                requireImplicit: true);

            var thenBinder = CreateFlowScopeBinderForTrue(condition);
            var thenStmt = thenBinder.BindStatement(node.Statement, context, diagnostics);

            BoundStatement? elseStmt = null;
            if (node.Else != null)
                elseStmt = BindStatement(node.Else.Statement, context, diagnostics);

            return new BoundIfStatement(node, condition, thenStmt, elseStmt);
        }
        private BoundExpression BindSwitchLabelCondition(
    SyntaxNode diagnosticNode,
    ExpressionSyntax tmpExprSyntax,
    BoundExpression tmpExpr,
    TypeSymbol governingType,
    SwitchLabelSyntax label,
    BindingContext ctx,
    DiagnosticBag diagnostics,
    out bool isFallbackLabel)
        {
            isFallbackLabel = false;
            var boolType = ctx.Compilation.GetSpecialType(SpecialType.System_Boolean);

            if (label is DefaultSwitchLabelSyntax)
            {
                isFallbackLabel = true;
                return MakeBoolLiteral(label, ctx, value: true);
            }

            if (label is CaseSwitchLabelSyntax cs)
            {
                var value = BindExpression(cs.Value, ctx, diagnostics);
                value = ApplyConversion(cs.Value, value, governingType, diagnosticNode, ctx, diagnostics, requireImplicit: true);
                if (value.HasErrors) return new BoundBadExpression(label);
                if (!value.ConstantValueOpt.HasValue)
                {
                    diagnostics.Add(new Diagnostic("CN_SWITCH000", DiagnosticSeverity.Error,
                        "A switch case label must be a constant expression.",
                        new Location(ctx.SemanticModel.SyntaxTree, cs.Value.Span)));
                    return MakeBoolLiteral(label, ctx, value: false);
                }

                var eqTok = MakeToken(SyntaxKind.EqualsEqualsToken, cs.CaseKeyword.Span);
                var eqSyntax = new BinaryExpressionSyntax(SyntaxKind.EqualsExpression, tmpExprSyntax, eqTok, cs.Value);
                return BindEqualityBinary(eqSyntax, tmpExpr, value, ctx, diagnostics);
            }

            if (label is CasePatternSwitchLabelSyntax cps)
            {
                BoundExpression match;
                switch (cps.Pattern)
                {
                    case ConstantPatternSyntax cp:
                        {
                            var value = BindExpression(cp.Expression, ctx, diagnostics);
                            value = ApplyConversion(cp.Expression, value, governingType, diagnosticNode, ctx, diagnostics, requireImplicit: true);
                            if (value.HasErrors) return new BoundBadExpression(label);
                            if (!value.ConstantValueOpt.HasValue)
                            {
                                diagnostics.Add(new Diagnostic("CN_SWITCH000", DiagnosticSeverity.Error,
                                    "A switch case label must be a constant expression.",
                                    new Location(ctx.SemanticModel.SyntaxTree, cp.Expression.Span)));
                                match = MakeBoolLiteral(label, ctx, value: false);
                                break;
                            }

                            var eqTok = MakeToken(SyntaxKind.EqualsEqualsToken, cps.CaseKeyword.Span);
                            var eqSyntax = new BinaryExpressionSyntax(SyntaxKind.EqualsExpression, tmpExprSyntax, eqTok, cp.Expression);
                            match = BindEqualityBinary(eqSyntax, tmpExpr, value, ctx, diagnostics);
                            break;
                        }
                    case DiscardPatternSyntax:
                        match = MakeBoolLiteral(label, ctx, value: true);
                        isFallbackLabel = cps.WhenClause is null;
                        break;
                    default:
                        diagnostics.Add(new Diagnostic("CN_SWITCH001", DiagnosticSeverity.Error,
                            $"Switch pattern '{cps.Pattern.Kind}' is not supported.",
                            new Location(ctx.SemanticModel.SyntaxTree, cps.Pattern.Span)));
                        match = new BoundBadExpression(label);
                        break;
                }

                if (cps.WhenClause is null) return match;

                var whenCond = BindExpression(cps.WhenClause.Condition, ctx, diagnostics);
                whenCond = ApplyConversion(cps.WhenClause.Condition, whenCond, boolType, cps.WhenClause, ctx, diagnostics, requireImplicit: true);
                if (match.HasErrors || whenCond.HasErrors) return new BoundBadExpression(label);
                return MakeLogicalAnd(label, match, whenCond, ctx, diagnostics);
            }

            diagnostics.Add(new Diagnostic("CN_SWITCH002", DiagnosticSeverity.Error,
                $"Switch label '{label.Kind}' is not supported.",
                new Location(ctx.SemanticModel.SyntaxTree, label.Span)));
            return new BoundBadExpression(label);
        }

        private BoundExpression BindSwitchExpressionArmCondition(
            SwitchExpressionArmSyntax arm,
            ExpressionSyntax tmpExprSyntax,
            BoundExpression tmpExpr,
            TypeSymbol governingType,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            var boolType = ctx.Compilation.GetSpecialType(SpecialType.System_Boolean);
            BoundExpression match;
            switch (arm.Pattern)
            {
                case ConstantPatternSyntax cp:
                    {
                        var value = BindExpression(cp.Expression, ctx, diagnostics);
                        value = ApplyConversion(cp.Expression, value, governingType, arm, ctx, diagnostics, requireImplicit: true);
                        if (value.HasErrors) return new BoundBadExpression(arm);
                        if (!value.ConstantValueOpt.HasValue)
                        {
                            diagnostics.Add(new Diagnostic("CN_SWITCHEXPR000", DiagnosticSeverity.Error,
                                "A switch expression arm pattern must be a constant expression.",
                                new Location(ctx.SemanticModel.SyntaxTree, cp.Expression.Span)));
                            match = MakeBoolLiteral(arm, ctx, value: false);
                            break;
                        }

                        var eqTok = MakeToken(SyntaxKind.EqualsEqualsToken, arm.EqualsGreaterThanToken.Span);
                        var eqSyntax = new BinaryExpressionSyntax(SyntaxKind.EqualsExpression, tmpExprSyntax, eqTok, cp.Expression);
                        match = BindEqualityBinary(eqSyntax, tmpExpr, value, ctx, diagnostics);
                        break;
                    }
                case DiscardPatternSyntax:
                    match = MakeBoolLiteral(arm, ctx, value: true);
                    break;
                default:
                    diagnostics.Add(new Diagnostic("CN_SWITCHEXPR001", DiagnosticSeverity.Error,
                        $"Switch expression pattern '{arm.Pattern.Kind}' is not supported.",
                        new Location(ctx.SemanticModel.SyntaxTree, arm.Pattern.Span)));
                    match = new BoundBadExpression(arm);
                    break;
            }

            if (arm.WhenClause is null) return match;
            var whenCond = BindExpression(arm.WhenClause.Condition, ctx, diagnostics);
            whenCond = ApplyConversion(arm.WhenClause.Condition, whenCond, boolType, arm.WhenClause, ctx, diagnostics, requireImplicit: true);
            if (match.HasErrors || whenCond.HasErrors) return new BoundBadExpression(arm);
            return MakeLogicalAnd(arm, match, whenCond, ctx, diagnostics);
        }

        private BoundStatement BindSwitchStatement(SwitchStatementSyntax node, BindingContext ctx, DiagnosticBag diagnostics)
        {
            var governing = BindExpression(node.Expression, ctx, diagnostics);
            if (governing.HasErrors) return new BoundBadStatement(node);
            if (governing.Type.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic("CN_SWITCH003", DiagnosticSeverity.Error,
                    "The switch expression must not be of type 'void'.",
                    new Location(ctx.SemanticModel.SyntaxTree, node.Expression.Span)));
                return new BoundBadStatement(node);
            }

            var tmp = NewTemp("$switch_tmp", governing.Type);
            var tmpSyntax = MakeIdentifierName(tmp.Name, node.Expression.Span);
            var tmpExpr = new BoundLocalExpression(tmpSyntax, tmp);
            var tmpDecl = new BoundLocalDeclarationStatement(node, tmp, governing);

            var breakLabel = _flow.NewGeneratedLabel("switch_break");
            int sectionCount = node.Sections.Count;
            var sectionLabels = new LabelSymbol[sectionCount];
            for (int i = 0; i < sectionCount; i++)
                sectionLabels[i] = _flow.NewGeneratedLabel("switch_section");

            var sectionConds = new BoundExpression[sectionCount];
            int fallbackSection = -1;

            for (int i = 0; i < sectionCount; i++)
            {
                var sec = node.Sections[i];
                BoundExpression? cond = null;
                bool secHasFallback = false;

                for (int l = 0; l < sec.Labels.Count; l++)
                {
                    bool isFallback;
                    var labelCond = BindSwitchLabelCondition(node, tmpSyntax, tmpExpr, governing.Type, sec.Labels[l], ctx, diagnostics, out isFallback);
                    if (isFallback) secHasFallback = true;
                    else cond = cond is null ? labelCond : MakeLogicalOr(sec, cond, labelCond, ctx, diagnostics);
                }

                sectionConds[i] = cond ?? MakeBoolLiteral(sec, ctx, value: false);
                if (secHasFallback)
                {
                    if (fallbackSection < 0) fallbackSection = i;
                    else
                        diagnostics.Add(new Diagnostic("CN_SWITCH004", DiagnosticSeverity.Error,
                            "Multiple default labels in switch statement.",
                            new Location(ctx.SemanticModel.SyntaxTree, sec.Span)));
                }
            }

            var stmts = ImmutableArray.CreateBuilder<BoundStatement>();
            stmts.Add(tmpDecl);

            for (int i = 0; i < sectionCount; i++)
            {
                var cond = sectionConds[i];
                if (TryGetBoolConstant(cond, out var bc) && bc == false) continue;
                if (TryGetBoolConstant(cond, out bc) && bc == true)
                {
                    stmts.Add(new BoundGotoStatement(node, sectionLabels[i]));
                    continue;
                }
                stmts.Add(new BoundConditionalGotoStatement(node, cond, sectionLabels[i], jumpIfTrue: true));
            }

            if (fallbackSection >= 0) stmts.Add(new BoundGotoStatement(node, sectionLabels[fallbackSection]));
            else stmts.Add(new BoundGotoStatement(node, breakLabel));

            _flow.PushLoop(breakLabel, breakLabel); // break only loop marker
            try
            {
                for (int i = 0; i < sectionCount; i++)
                {
                    var sec = node.Sections[i];
                    stmts.Add(new BoundLabelStatement(sec, sectionLabels[i]));

                    var sectionBinder = new LocalScopeBinder(parent: this, flags: Flags, containing: _containing);
                    var secStatements = sec.Statements.ToArray();
                    sectionBinder.PredeclareLocalFunctionsInStatementList(ImmutableArray.CreateRange(secStatements), ctx, diagnostics);

                    var boundSecStmts = ImmutableArray.CreateBuilder<BoundStatement>(secStatements.Length);
                    for (int s = 0; s < secStatements.Length; s++)
                        boundSecStmts.Add(sectionBinder.BindStatement(secStatements[s], ctx, diagnostics));

                    stmts.Add(new BoundBlockStatement(sec, boundSecStmts.ToImmutable()));
                    stmts.Add(new BoundGotoStatement(sec, breakLabel)); // implicit break
                }
            }
            finally { _flow.PopLoop(); }

            stmts.Add(new BoundLabelStatement(node, breakLabel));
            return new BoundBlockStatement(node, stmts.ToImmutable());
        }

        private BoundExpression BindSwitchExpression(SwitchExpressionSyntax node, BindingContext ctx, DiagnosticBag diagnostics)
        {
            var governing = BindExpression(node.GoverningExpression, ctx, diagnostics);
            if (governing.HasErrors) return new BoundBadExpression(node);
            if (governing.Type.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic("CN_SWITCHEXPR002", DiagnosticSeverity.Error,
                    "The switch governing expression must not be of type 'void'.",
                    new Location(ctx.SemanticModel.SyntaxTree, node.GoverningExpression.Span)));
                return new BoundBadExpression(node);
            }

            var tmp = NewTemp("$switch_expr_tmp", governing.Type);
            var tmpSyntax = MakeIdentifierName(tmp.Name, node.GoverningExpression.Span);
            var tmpExpr = new BoundLocalExpression(tmpSyntax, tmp);
            var tmpDecl = new BoundLocalDeclarationStatement(node, tmp, governing);

            var arms = node.Arms;
            int armCount = arms.Count;
            if (armCount == 0)
            {
                diagnostics.Add(new Diagnostic("CN_SWITCHEXPR003", DiagnosticSeverity.Error,
                    "A switch expression must have at least one arm.",
                    new Location(ctx.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            var boundArmExprs = new BoundExpression[armCount];
            var armIsThrow = new bool[armCount];
            for (int i = 0; i < armCount; i++)
            {
                armIsThrow[i] = arms[i].Expression is ThrowExpressionSyntax;
                boundArmExprs[i] = BindExpression(arms[i].Expression, ctx, diagnostics);
            }

            TypeSymbol? resultType = null;
            for (int i = 0; i < armCount; i++)
            {
                if (armIsThrow[i])
                    continue;
                var t = boundArmExprs[i].Type;
                if (t is ErrorTypeSymbol)
                    continue;
                if (resultType is null)
                {
                    resultType = t;
                    continue;
                }
                var probe = new DiagnosticBag();
                var merged = ClassifyConditionalResultType(
                    ctx.Compilation,
                    ctx.SemanticModel.SyntaxTree,
                    resultType,
                    t,
                    node,
                    probe);
                if (merged is null || merged is ErrorTypeSymbol)
                {
                    resultType = null;
                    break;
                }
                resultType = merged;
            }
            if (resultType is null || resultType is ErrorTypeSymbol)
            {
                var objType = ctx.Compilation.GetSpecialType(SpecialType.System_Object);
                bool ok = true;
                for (int i = 0; i < armCount; i++)
                {
                    if (armIsThrow[i])
                        continue;
                    var e = boundArmExprs[i];
                    if (e.HasErrors)
                        continue;
                    var conv = ClassifyConversion(e, objType, ctx);
                    if (!conv.Exists || !conv.IsImplicit)
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok)
                    resultType = objType;
            }
            if (resultType is null || resultType is ErrorTypeSymbol || resultType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic("CN_SWITCHEXPR004", DiagnosticSeverity.Error,
                    "Cannot determine a common type for switch expression arms.",
                    new Location(ctx.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            var resultLocal = NewTemp("$switch_expr_result", resultType);
            var resultSyntax = MakeIdentifierName(resultLocal.Name, node.SwitchKeyword.Span);
            var resultLocalExpr = new BoundLocalExpression(resultSyntax, resultLocal);
            var resultDecl = new BoundLocalDeclarationStatement(node, resultLocal, initializer: null);

            var endLabel = _flow.NewGeneratedLabel("switch_expr_end");
            var armLabels = new LabelSymbol[armCount];
            for (int i = 0; i < armCount; i++)
                armLabels[i] = _flow.NewGeneratedLabel("switch_expr_arm");

            var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>();
            sideEffects.Add(tmpDecl);
            sideEffects.Add(resultDecl);

            for (int i = 0; i < armCount; i++)
            {
                var arm = arms[i];
                var cond = BindSwitchExpressionArmCondition(arm, tmpSyntax, tmpExpr, governing.Type, ctx, diagnostics);
                if (TryGetBoolConstant(cond, out var bc) && bc == false) continue;
                if (TryGetBoolConstant(cond, out bc) && bc == true)
                {
                    sideEffects.Add(new BoundGotoStatement(arm, armLabels[i]));
                    continue;
                }
                sideEffects.Add(new BoundConditionalGotoStatement(arm, cond, armLabels[i], jumpIfTrue: true));
            }

            sideEffects.Add(new BoundGotoStatement(node, endLabel)); // no match => default(resultLocal)

            for (int i = 0; i < armCount; i++)
            {
                var arm = arms[i];
                sideEffects.Add(new BoundLabelStatement(arm, armLabels[i]));
                var armValue = boundArmExprs[i];
                if (!armIsThrow[i] && !armValue.HasErrors)
                {
                    armValue = ApplyConversion(arm.Expression, armValue, resultType, arm, ctx, diagnostics, requireImplicit: true);
                }
                else if (armValue is BoundBadExpression bad)
                {
                    bad.SetType(resultType);
                }
                var assign = new BoundAssignmentExpression(arm, resultLocalExpr, armValue);
                sideEffects.Add(new BoundExpressionStatement(arm, assign));
                sideEffects.Add(new BoundGotoStatement(arm, endLabel));
            }

            sideEffects.Add(new BoundLabelStatement(node, endLabel));

            return new BoundSequenceExpression(
                node,
                locals: ImmutableArray.Create(tmp, resultLocal),
                sideEffects: sideEffects.ToImmutable(),
                value: resultLocalExpr);
        }
        private BoundStatement BindWhile(WhileStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var boolType = context.Compilation.GetSpecialType(SpecialType.System_Boolean);

            var breakLabel = _flow.NewGeneratedLabel("break");
            var continueLabel = _flow.NewGeneratedLabel("continue");

            var condition = BindExpression(node.Condition, context, diagnostics);
            condition = ApplyConversion(
                exprSyntax: node.Condition,
                expr: condition,
                targetType: boolType,
                diagnosticNode: node.Condition,
                context: context,
                diagnostics: diagnostics,
                requireImplicit: true);

            var bodyBinder = CreateFlowScopeBinderForTrue(condition);
            _flow.PushLoop(breakLabel, continueLabel);
            BoundStatement body;
            try
            {
                body = bodyBinder.BindStatement(node.Statement, context, diagnostics);
            }
            finally
            {
                _flow.PopLoop();
            }
            return new BoundWhileStatement(node, condition, body, breakLabel, continueLabel);
        }
        private BoundStatement BindDoWhile(DoStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var breakLabel = _flow.NewGeneratedLabel("break");
            var continueLabel = _flow.NewGeneratedLabel("continue");

            _flow.PushLoop(breakLabel, continueLabel);
            BoundStatement body;
            try
            {
                body = BindStatement(node.Statement, context, diagnostics);
            }
            finally
            {
                _flow.PopLoop();
            }

            var boolType = context.Compilation.GetSpecialType(SpecialType.System_Boolean);
            var condition = BindExpression(node.Condition, context, diagnostics);
            condition = ApplyConversion(
                exprSyntax: node.Condition,
                expr: condition,
                targetType: boolType,
                diagnosticNode: node.Condition,
                context: context,
                diagnostics: diagnostics,
                requireImplicit: true);

            return new BoundDoWhileStatement(node, body, condition, breakLabel, continueLabel);
        }
        private BoundStatement BindFor(ForStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var scope = new LocalScopeBinder(parent: this, flags: Flags, containing: _containing);
            var initializers = ImmutableArray.CreateBuilder<BoundStatement>();

            var breakLabel = _flow.NewGeneratedLabel("break");
            var continueLabel = _flow.NewGeneratedLabel("continue");

            if (node.Declaration != null)
                initializers.AddRange(scope.BindForVariableDeclaration(node.Declaration, node.Declaration, context, diagnostics));

            for (int i = 0; i < node.Initializers.Count; i++)
            {
                var initSyntax = node.Initializers[i];
                var initExpr = scope.BindDiscardedExpression(initSyntax, context, diagnostics);
                initializers.Add(new BoundExpressionStatement(initSyntax, initExpr));
            }

            BoundExpression? conditionOpt = null;
            if (node.Condition != null)
            {
                var boolType = context.Compilation.GetSpecialType(SpecialType.System_Boolean);
                var cond = scope.BindExpression(node.Condition, context, diagnostics);
                conditionOpt = scope.ApplyConversion(
                    exprSyntax: node.Condition,
                    expr: cond,
                    targetType: boolType,
                    diagnosticNode: node.Condition,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);
            }

            var incrementors = ImmutableArray.CreateBuilder<BoundStatement>();
            for (int i = 0; i < node.Incrementors.Count; i++)
            {
                var incSyntax = node.Incrementors[i];
                var incExpr = scope.BindDiscardedExpression(incSyntax, context, diagnostics);
                incrementors.Add(new BoundExpressionStatement(incSyntax, incExpr));
            }
            _flow.PushLoop(breakLabel, continueLabel);
            BoundStatement body;
            try
            {
                body = scope.BindStatement(node.Statement, context, diagnostics);
            }
            finally
            {
                _flow.PopLoop();
            }

            return new BoundForStatement(node, initializers.ToImmutable(), conditionOpt, incrementors.ToImmutable(), body, breakLabel, continueLabel);
        }
        private BoundExpression BindDiscardedExpression(
            ExpressionSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (node is PostfixUnaryExpressionSyntax post)
            {
                if (post.Kind == SyntaxKind.PostIncrementExpression)
                    return BindPostfixIncrementOrDecrement(post, isIncrement: true, resultUsed: false, context, diagnostics);

                if (post.Kind == SyntaxKind.PostDecrementExpression)
                    return BindPostfixIncrementOrDecrement(post, isIncrement: false, resultUsed: false, context, diagnostics);
            }

            if (node is ImplicitObjectCreationExpressionSyntax ioc)
                return BindImplicitObjectCreation(ioc, context, diagnostics);

            return BindExpression(node, context, diagnostics);
        }
        private BoundStatement BindGoto(GotoStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (node.Kind != SyntaxKind.GotoStatement)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FLOW005",
                    DiagnosticSeverity.Error,
                    "Only 'goto identifier;' is supported (goto case/default is not implemented).",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadStatement(node);
            }

            if (node.Expression is not IdentifierNameSyntax id)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FLOW006",
                    DiagnosticSeverity.Error,
                    "Expected an identifier after 'goto'.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadStatement(node);
            }

            var name = id.Identifier.ValueText ?? string.Empty;
            if (name.Length == 0)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FLOW006",
                    DiagnosticSeverity.Error,
                    "Expected an identifier after 'goto'.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));
                return new BoundBadStatement(node);
            }

            var label = _flow.GetOrCreateSourceLabel(name);
            _flow.RegisterGoto(node, label);

            Record(id, new BoundLabelExpression(id, label), context);

            return new BoundGotoStatement(node, label);
        }

        private BoundStatement BindLabeledStatement(LabeledStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var name = node.Identifier.ValueText ?? string.Empty;
            if (name.Length == 0)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FLOW007",
                    DiagnosticSeverity.Error,
                    "Invalid label name.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadStatement(node);
            }

            var label = _flow.GetOrCreateSourceLabel(name);
            if (!label.TryDefine(context.SemanticModel.SyntaxTree, node))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FLOW003",
                    DiagnosticSeverity.Error,
                    $"Label '{name}' is a duplicate.",
                    new Location(context.SemanticModel.SyntaxTree, node.Identifier.Span)));
            }
            else
            {
                _flow.RegisterLabelDefinition(label);
            }

            context.Recorder.RecordDeclared(node, label);

            var inner = BindStatement(node.Statement, context, diagnostics);
            var list = ImmutableArray.Create<BoundStatement>(
                new BoundLabelStatement(node, label),
                inner);

            return new BoundStatementList(node, list);
        }
        private ImmutableArray<BoundStatement> BindForVariableDeclaration(
            SyntaxNode ownerSyntax,
            VariableDeclarationSyntax decl,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (decl.Variables.Count == 0)
                return ImmutableArray<BoundStatement>.Empty;

            var isVar = IsVar(decl.Type);

            TypeSymbol? explicitType = null;
            if (!isVar)
                explicitType = BindType(decl.Type, context, diagnostics);

            if (decl.Variables.Count == 1)
            {
                var s = BindSingleDeclarator(
                    ownerSyntax, decl.Variables[0], isVar, explicitType, isRefLocal: false, isConst: false, context, diagnostics);
                return ImmutableArray.Create(s);
            }

            var list = ImmutableArray.CreateBuilder<BoundStatement>(decl.Variables.Count);
            for (int i = 0; i < decl.Variables.Count; i++)
                list.Add(BindSingleDeclarator(
                    ownerSyntax, decl.Variables[i], isVar, explicitType, isRefLocal: false, isConst: false, context, diagnostics));

            return list.ToImmutable();
        }
    }
}
