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
            var desiredBase = new Dictionary<SourceNamedTypeSymbol, BaseClassInfo>(
                ReferenceEqualityComparer<SourceNamedTypeSymbol>.Instance);

            var desiredInterfaces = new Dictionary<SourceNamedTypeSymbol, HashSet<NamedTypeSymbol>>(
                ReferenceEqualityComparer<SourceNamedTypeSymbol>.Instance);

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

                    var binder = HasModifier(typeSyntax.Modifiers, SyntaxKind.UnsafeKeyword) ? unsafeTypeBinder : safeTypeBinder;
                    var ctx = new BindingContext(
                        compilation: compilation,
                        semanticModel: stubModel,
                        containingSymbol: typeSymbol,
                        recorder: NullRecorder.Instance);

                    // Interfaces for class / struct / interface
                    var ifaces = ResolveDeclaredInterfaces(typeSymbol, baseList, binder, ctx, diagnostics);
                    if (!ifaces.IsDefaultOrEmpty)
                    {
                        if (!desiredInterfaces.TryGetValue(typeSymbol, out var set))
                        {
                            set = new HashSet<NamedTypeSymbol>(ReferenceEqualityComparer<NamedTypeSymbol>.Instance);
                            desiredInterfaces.Add(typeSymbol, set);
                        }

                        for (int i = 0; i < ifaces.Length; i++)
                            set.Add(ifaces[i]);
                    }

                    // Base class only for class
                    if (typeSymbol.TypeKind == TypeKind.Class)
                    {
                        if (!TryResolveDeclaredBaseClass(typeSymbol, baseList, binder, ctx, diagnostics, out var baseClass))
                            continue;

                        if (baseClass is null)
                            continue;

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
                    else
                    {
                        ValidateOnlyInterfacesInBaseList(typeSymbol, baseList, binder, ctx, diagnostics);
                    }
                }
            }

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

            foreach (var kv in desiredBase)
                kv.Key.SetDeclaredBaseType(kv.Value.BaseType);

            foreach (var kv in desiredInterfaces)
            {
                var b = ImmutableArray.CreateBuilder<TypeSymbol>(kv.Value.Count);
                foreach (var iface in kv.Value)
                    b.Add(iface);

                kv.Key.SetDeclaredInterfaces(b.ToImmutable());
            }

            BindOverrides(compilation, trees, diagnostics);
        }

        private static void ValidateOnlyInterfacesInBaseList(
            SourceNamedTypeSymbol declaringType,
            BaseListSyntax baseList,
            TypeBinder binder,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            for (int i = 0; i < baseList.Types.Count; i++)
            {
                var baseTypeSyntax = baseList.Types[i].Type;
                var t = binder.BindType(baseTypeSyntax, context, diagnostics);

                if (t is not NamedTypeSymbol nt || nt is ErrorTypeSymbol)
                    continue;

                if (nt.TypeKind == TypeKind.Interface)
                    continue;

                diagnostics.Add(new Diagnostic(
                    "CN_BASE006",
                    DiagnosticSeverity.Error,
                    declaringType.TypeKind == TypeKind.Struct
                        ? $"Struct '{declaringType.Name}' cannot have base class '{nt.Name}'; only interfaces are allowed."
                        : $"Interface '{declaringType.Name}' cannot inherit from non-interface type '{nt.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, baseTypeSyntax.Span)));
            }
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
                    var overridden = FindOverridableInBaseChain(bt, m, out var sealedCandidate);
                    if (sealedCandidate is not null)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_OVR_SEALED001",
                            DiagnosticSeverity.Error,
                            $"Cannot override inherited member '{sealedCandidate.Name}' because it is sealed.",
                            new Location(tree, md.Span)));
                        continue;
                    }
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
        private static MethodSymbol? FindOverridableInBaseChain(NamedTypeSymbol baseType, MethodSymbol overriding, out MethodSymbol? sealedCandidate)
        {
            sealedCandidate = null;
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

                    if (bm.IsSealed)
                    {
                        sealedCandidate = bm;
                        return null;
                    }

                    if (bm.IsVirtual || bm.IsAbstract || bm.IsOverride)
                        return bm;
                }
            }
            return null;
        }
        private static bool SignatureEquals(MethodSymbol a, MethodSymbol b)
        {
            if (!LocalScopeBinder.AreSameType(a.ReturnType, b.ReturnType))
                return false;

            var ap = a.Parameters;
            var bp = b.Parameters;

            if (ap.Length != bp.Length)
                return false;

            for (int i = 0; i < ap.Length; i++)
            {
                if (ap[i].RefKind != bp[i].RefKind)
                    return false;

                if (!LocalScopeBinder.AreSameType(ap[i].Type, bp[i].Type))
                    return false;
            }

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

                if (nt.IsSealed)
                {
                    diagnostics.Add(new Diagnostic(
                         "CN_BASE_SEALED001",
                         DiagnosticSeverity.Error,
                         $"'{nt.Name}' is sealed and cannot be used as a base class.",
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
        private static ImmutableArray<NamedTypeSymbol> ResolveDeclaredInterfaces(
            SourceNamedTypeSymbol declaringType,
            BaseListSyntax? baseList,
            TypeBinder binder,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (baseList is null || baseList.Types.Count == 0)
                return ImmutableArray<NamedTypeSymbol>.Empty;

            var seen = new HashSet<NamedTypeSymbol>(ReferenceEqualityComparer<NamedTypeSymbol>.Instance);
            var result = ImmutableArray.CreateBuilder<NamedTypeSymbol>();

            for (int i = 0; i < baseList.Types.Count; i++)
            {
                var baseTypeSyntax = baseList.Types[i].Type;
                var t = binder.BindType(baseTypeSyntax, context, diagnostics);

                if (t is not NamedTypeSymbol nt || nt is ErrorTypeSymbol)
                    continue;

                if (ReferenceEquals(nt, declaringType))
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_IFACE_SELF001",
                        DiagnosticSeverity.Error,
                        $"Type '{declaringType.Name}' cannot implement itself.",
                        new Location(context.SemanticModel.SyntaxTree, baseTypeSyntax.Span)));
                    continue;
                }

                if (nt.TypeKind != TypeKind.Interface)
                    continue;

                if (seen.Add(nt))
                    result.Add(nt);
            }

            return result.ToImmutable();
        }

        private static bool CreatesBaseTypeCycle(
             SourceNamedTypeSymbol start,
             NamedTypeSymbol baseCandidate,
             Dictionary<SourceNamedTypeSymbol, BaseClassInfo> desiredBase)
        {
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
}
