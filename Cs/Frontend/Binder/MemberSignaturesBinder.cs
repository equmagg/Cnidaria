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
                    if (kv.Key is DelegateDeclarationSyntax dd && kv.Value is SourceNamedTypeSymbol delegateType)
                    {
                        BindDelegateSignature(compilation, tree, dd, delegateType, stubModel, safeTypeBinder, unsafeTypeBinder, diagnostics, pendingOptionalParameters);
                    }
                    else if (kv.Key is MethodDeclarationSyntax md && kv.Value is SourceMethodSymbol sm)
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


        private static void BindDelegateSignature(
            Compilation compilation,
            SyntaxTree tree,
            DelegateDeclarationSyntax syntax,
            SourceNamedTypeSymbol delegateType,
            SemanticModel stubModel,
            TypeBinder safeTypeBinder,
            TypeBinder unsafeTypeBinder,
            DiagnosticBag diagnostics,
            List<PendingOptionalParameter> pendingOptionalParameters)
        {
            var typeBinder = HasModifier(syntax.Modifiers, SyntaxKind.UnsafeKeyword)
                ? unsafeTypeBinder
                : safeTypeBinder;

            var invoke = FindSourceMethod(delegateType, "Invoke");
            if (invoke is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_DELEGATE001",
                    DiagnosticSeverity.Error,
                    $"Delegate type '{delegateType.Name}' is missing synthesized Invoke method.",
                    new Location(tree, syntax.Span)));
                return;
            }

            var ctx = new BindingContext(compilation, stubModel, invoke, NullRecorder.Instance);
            var returnType = typeBinder.BindType(syntax.ReturnType, ctx, diagnostics);
            invoke.SetReturnType(returnType);

            var pars = syntax.ParameterList.Parameters;
            for (int i = 0; i < pars.Count && i < invoke.Parameters.Length; i++)
            {
                var pt = BindParameterType(typeBinder, pars[i], ctx, diagnostics, out var isReadOnlyRef);
                invoke.Parameters[i].Type = pt;
                invoke.Parameters[i].IsReadOnlyRef = isReadOnlyRef;

                if (pars[i].Default is not null)
                {
                    pendingOptionalParameters.Add(
                        new PendingOptionalParameter(tree, pars[i], invoke.Parameters[i], invoke, typeBinder, stubModel));
                }
            }
        }

        private static SourceMethodSymbol? FindSourceMethod(NamedTypeSymbol type, string name)
        {
            var members = type.GetMembers();
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is SourceMethodSymbol m && StringComparer.Ordinal.Equals(m.Name, name))
                    return m;
            }

            return null;
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
}
