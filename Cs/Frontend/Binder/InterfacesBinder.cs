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
    internal static class ExplicitInterfaceImplementationBinder
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
        public static void BindAll(
            Compilation compilation,
            ImmutableArray<SyntaxTree> trees,
            DiagnosticBag diagnostics)
        {
            BindExplicitInterfaceImplementations(compilation, trees, diagnostics);
            ValidateInterfaceImplementations(compilation, trees, diagnostics);
        }
        private static void BindExplicitInterfaceImplementations(
            Compilation compilation,
            ImmutableArray<SyntaxTree> trees,
            DiagnosticBag diagnostics)
        {
            foreach (var tree in trees)
            {
                if (!compilation.DeclaredSymbolsByTree.TryGetValue(tree, out var declMap))
                    continue;

                var stubModel = new SemanticModelStub(compilation, tree);
                var importScopeMap = ImportsBuilder.BuildImportScopeMap(compilation, tree, NullRecorder.Instance, diagnostics);

                var safeTypeBinder = new TypeBinder(
                    parent: null,
                    flags: BinderFlags.None,
                    compilation: compilation,
                    importScopeMap: importScopeMap);

                var unsafeTypeBinder = new TypeBinder(
                    parent: null,
                    flags: BinderFlags.UnsafeRegion,
                    compilation: compilation,
                    importScopeMap: importScopeMap);

                foreach (var kv in declMap)
                {
                    switch (kv.Key)
                    {
                        case MethodDeclarationSyntax md
                            when kv.Value is SourceMethodSymbol sm && md.ExplicitInterfaceSpecifier is not null:
                            {
                                var binder = HasModifier(md.Modifiers, SyntaxKind.UnsafeKeyword) ? unsafeTypeBinder : safeTypeBinder;
                                BindExplicitInterfaceMethod(compilation, tree, stubModel, binder, md, sm, diagnostics);
                                break;
                            }

                        case PropertyDeclarationSyntax pd
                            when kv.Value is SourcePropertySymbol sp && pd.ExplicitInterfaceSpecifier is not null:
                            {
                                var binder = HasModifier(pd.Modifiers, SyntaxKind.UnsafeKeyword) ? unsafeTypeBinder : safeTypeBinder;
                                BindExplicitInterfaceProperty(compilation, tree, stubModel, binder, pd, sp, diagnostics, isIndexer: false);
                                break;
                            }

                        case IndexerDeclarationSyntax id
                            when kv.Value is SourcePropertySymbol sp && id.ExplicitInterfaceSpecifier is not null:
                            {
                                var binder = HasModifier(id.Modifiers, SyntaxKind.UnsafeKeyword) ? unsafeTypeBinder : safeTypeBinder;
                                BindExplicitInterfaceIndexer(compilation, tree, stubModel, binder, id, sp, diagnostics);
                                break;
                            }
                    }
                }
            }
        }
        private static void ValidateInterfaceImplementations(
            Compilation compilation,
            ImmutableArray<SyntaxTree> trees,
            DiagnosticBag diagnostics)
        {
            var seenTypes = new HashSet<NamedTypeSymbol>(ReferenceEqualityComparer<NamedTypeSymbol>.Instance);

            foreach (var tree in trees)
            {
                if (!compilation.DeclaredSymbolsByTree.TryGetValue(tree, out var declMap))
                    continue;

                foreach (var kv in declMap)
                {
                    if (kv.Key is not TypeDeclarationSyntax typeSyntax)
                        continue;

                    if (kv.Value is not SourceNamedTypeSymbol type)
                        continue;

                    if (!seenTypes.Add(type))
                        continue;

                    if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
                        continue;

                    ValidateInterfaceImplementationsForType(type, tree, typeSyntax.Span, diagnostics);
                }
            }
        }
        private static void ValidateInterfaceImplementationsForType(
            SourceNamedTypeSymbol type,
            SyntaxTree tree,
            TextSpan diagnosticSpan,
            DiagnosticBag diagnostics)
        {
            var interfaces = GetEffectiveInterfaceSet(type);
            if (interfaces.IsDefaultOrEmpty)
                return;

            var seenMembers = new List<Symbol>();

            for (int i = 0; i < interfaces.Length; i++)
            {
                foreach (var iface in EnumerateInterfaceClosure(interfaces[i]))
                {
                    var members = iface.GetMembers();
                    for (int m = 0; m < members.Length; m++)
                    {
                        var member = members[m];
                        if (ContainsInterfaceMember(seenMembers, member))
                            continue;

                        seenMembers.Add(member);

                        switch (member)
                        {
                            case MethodSymbol method when ShouldValidateInterfaceMethod(method):
                                if (!HasMethodImplementation(type, method))
                                {
                                    diagnostics.Add(new Diagnostic(
                                        "CN_IFACEIMPL001",
                                        DiagnosticSeverity.Error,
                                        $"'{type.Name}' does not implement interface member '{FormatInterfaceMethod(method)}'.",
                                        new Location(tree, diagnosticSpan)));
                                }
                                break;

                            case PropertySymbol property:
                                if (!HasPropertyImplementation(type, property))
                                {
                                    diagnostics.Add(new Diagnostic(
                                        "CN_IFACEIMPL002",
                                        DiagnosticSeverity.Error,
                                        $"'{type.Name}' does not implement interface member '{FormatInterfaceProperty(property)}'.",
                                        new Location(tree, diagnosticSpan)));
                                }
                                break;
                        }
                    }
                }
            }

        }

        private static ImmutableArray<NamedTypeSymbol> GetEffectiveInterfaceSet(NamedTypeSymbol type)
        {
            var seen = new List<NamedTypeSymbol>();
            var queue = new Queue<NamedTypeSymbol>();

            for (NamedTypeSymbol? cur = type; cur is not null; cur = cur.BaseType as NamedTypeSymbol)
            {
                var ifaces = cur.Interfaces;
                for (int i = 0; i < ifaces.Length; i++)
                {
                    if (ifaces[i] is NamedTypeSymbol nt && nt.TypeKind == TypeKind.Interface)
                        queue.Enqueue(nt);
                }
            }

            var builder = ImmutableArray.CreateBuilder<NamedTypeSymbol>();
            while (queue.Count != 0)
            {
                var cur = queue.Dequeue();
                if (ContainsInterfaceType(seen, cur))
                    continue;

                seen.Add(cur);
                builder.Add(cur);

                var nested = cur.Interfaces;
                for (int i = 0; i < nested.Length; i++)
                {
                    if (nested[i] is NamedTypeSymbol nt && nt.TypeKind == TypeKind.Interface)
                        queue.Enqueue(nt);
                }
            }

            return builder.ToImmutable();
        }
        private static bool ContainsInterfaceType(List<NamedTypeSymbol> seen, NamedTypeSymbol candidate)
        {
            for (int i = 0; i < seen.Count; i++)
            {
                if (LocalScopeBinder.AreSameType(seen[i], candidate))
                    return true;
            }

            return false;
        }

        private static bool ContainsInterfaceMember(List<Symbol> seen, Symbol candidate)
        {
            for (int i = 0; i < seen.Count; i++)
            {
                if (SameInterfaceMemberIdentity(seen[i], candidate))
                    return true;
            }

            return false;
        }
        private static bool SameInterfaceMemberIdentity(Symbol? a, Symbol b)
        {
            if (a is null)
                return false;

            if (ReferenceEquals(a, b))
                return true;

            return (a, b) switch
            {
                (MethodSymbol am, MethodSymbol bm) => SameInterfaceMethodIdentity(am, bm),
                (PropertySymbol ap, PropertySymbol bp) => SameInterfacePropertyIdentity(ap, bp),
                _ => false
            };
        }

        private static bool ShouldValidateInterfaceMethod(MethodSymbol method)
        {
            if (method.IsConstructor || method.IsStatic)
                return false;

            // Property accessors are validated via PropertySymbol
            if (method.Name.StartsWith("get_", StringComparison.Ordinal) ||
                method.Name.StartsWith("set_", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }
        private static bool HasMethodImplementation(NamedTypeSymbol type, MethodSymbol ifaceMethod)
        {
            return FindExplicitMethodImplementation(type, ifaceMethod) is not null
                || FindImplicitMethodImplementation(type, ifaceMethod) is not null;
        }

        private static MethodSymbol? FindExplicitMethodImplementation(NamedTypeSymbol type, MethodSymbol ifaceMethod)
        {
            for (NamedTypeSymbol? cur = type; cur is not null; cur = cur.BaseType as NamedTypeSymbol)
            {
                var members = cur.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is MethodSymbol candidate &&
                        SameInterfaceMethodIdentity(candidate.ExplicitInterfaceImplementation, ifaceMethod))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }


        private static bool SameInterfaceMethodIdentity(MethodSymbol? a, MethodSymbol b)
        {
            if (a is null)
                return false;

            if (ReferenceEquals(a, b))
                return true;

            if (a.ContainingSymbol is TypeSymbol at && b.ContainingSymbol is TypeSymbol bt)
            {
                if (!LocalScopeBinder.AreSameType(at, bt))
                    return false;
            }
            else if (!ReferenceEquals(a.ContainingSymbol, b.ContainingSymbol))
            {
                return false;
            }

            if (ReferenceEquals(a.OriginalDefinition, b.OriginalDefinition))
                return true;

            if (!StringComparer.Ordinal.Equals(a.Name, b.Name))
                return false;

            if (!LocalScopeBinder.AreSameType(a.ReturnType, b.ReturnType))
                return false;

            return SameExplicitMethodSignature(a, b);
        }
        private static bool SameInterfacePropertyIdentity(PropertySymbol? a, PropertySymbol b)
        {
            if (a is null)
                return false;

            if (ReferenceEquals(a, b))
                return true;

            if (!StringComparer.Ordinal.Equals(a.Name, b.Name))
                return false;

            if (a.ContainingSymbol is TypeSymbol at && b.ContainingSymbol is TypeSymbol bt)
            {
                if (!LocalScopeBinder.AreSameType(at, bt))
                    return false;
            }
            else if (!ReferenceEquals(a.ContainingSymbol, b.ContainingSymbol))
            {
                return false;
            }

            if (!LocalScopeBinder.AreSameType(a.Type, b.Type))
                return false;

            if (a.HasGet != b.HasGet || a.HasSet != b.HasSet)
                return false;

            return SameExplicitPropertySignature(a, b);
        }
        private static MethodSymbol? FindImplicitMethodImplementation(NamedTypeSymbol type, MethodSymbol ifaceMethod)
        {
            for (NamedTypeSymbol? cur = type; cur is not null; cur = cur.BaseType as NamedTypeSymbol)
            {
                var members = cur.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is not MethodSymbol candidate)
                        continue;

                    if (candidate.IsStatic || candidate.IsConstructor)
                        continue;

                    if (candidate.ExplicitInterfaceImplementation is not null)
                        continue;

                    if (candidate.DeclaredAccessibility != Accessibility.Public)
                        continue;

                    if (!StringComparer.Ordinal.Equals(candidate.Name, ifaceMethod.Name))
                        continue;

                    if (!SameExplicitMethodSignature(candidate, ifaceMethod))
                        continue;

                    if (!LocalScopeBinder.AreSameType(candidate.ReturnType, ifaceMethod.ReturnType))
                        continue;

                    return candidate;
                }
            }

            return null;
        }

        private static bool HasPropertyImplementation(NamedTypeSymbol type, PropertySymbol ifaceProperty)
        {
            return FindExplicitPropertyImplementation(type, ifaceProperty) is not null
                || FindImplicitPropertyImplementation(type, ifaceProperty) is not null;
        }

        private static PropertySymbol? FindExplicitPropertyImplementation(NamedTypeSymbol type, PropertySymbol ifaceProperty)
        {
            for (NamedTypeSymbol? cur = type; cur is not null; cur = cur.BaseType as NamedTypeSymbol)
            {
                var members = cur.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is PropertySymbol candidate &&
                        SameInterfacePropertyIdentity(candidate.ExplicitInterfaceImplementation, ifaceProperty))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static PropertySymbol? FindImplicitPropertyImplementation(NamedTypeSymbol type, PropertySymbol ifaceProperty)
        {
            for (NamedTypeSymbol? cur = type; cur is not null; cur = cur.BaseType as NamedTypeSymbol)
            {
                var members = cur.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is not PropertySymbol candidate)
                        continue;

                    if (candidate.IsStatic)
                        continue;

                    if (candidate.ExplicitInterfaceImplementation is not null)
                        continue;

                    if (candidate.DeclaredAccessibility != Accessibility.Public)
                        continue;

                    if (!StringComparer.Ordinal.Equals(candidate.Name, ifaceProperty.Name))
                        continue;

                    if (!SameExplicitPropertySignature(candidate, ifaceProperty))
                        continue;

                    if (!LocalScopeBinder.AreSameType(candidate.Type, ifaceProperty.Type))
                        continue;

                    if (ifaceProperty.HasGet && !candidate.HasGet)
                        continue;

                    if (ifaceProperty.HasSet && !candidate.HasSet)
                        continue;

                    return candidate;
                }
            }

            return null;
        }

        private static string FormatInterfaceMethod(MethodSymbol method)
        {
            var sb = new StringBuilder();
            var iface = method.ContainingSymbol as NamedTypeSymbol;

            sb.Append(iface?.Name ?? "?");
            sb.Append('.');
            sb.Append(method.Name);
            sb.Append('(');

            var ps = method.Parameters;
            for (int i = 0; i < ps.Length; i++)
            {
                if (i != 0)
                    sb.Append(", ");

                switch (ps[i].RefKind)
                {
                    case ParameterRefKind.Ref: sb.Append("ref "); break;
                    case ParameterRefKind.Out: sb.Append("out "); break;
                    case ParameterRefKind.In: sb.Append("in "); break;
                }

                sb.Append(ps[i].Type.Name);
            }

            sb.Append(')');
            return sb.ToString();
        }

        private static string FormatInterfaceProperty(PropertySymbol property)
        {
            var iface = property.ContainingSymbol as NamedTypeSymbol;
            if (property.Parameters.Length == 0)
                return $"{iface?.Name ?? "?"}.{property.Name}";

            var sb = new StringBuilder();
            sb.Append(iface?.Name ?? "?");
            sb.Append(".this[");

            var ps = property.Parameters;
            for (int i = 0; i < ps.Length; i++)
            {
                if (i != 0)
                    sb.Append(", ");

                switch (ps[i].RefKind)
                {
                    case ParameterRefKind.Ref: sb.Append("ref "); break;
                    case ParameterRefKind.Out: sb.Append("out "); break;
                    case ParameterRefKind.In: sb.Append("in "); break;
                }

                sb.Append(ps[i].Type.Name);
            }

            sb.Append(']');
            return sb.ToString();
        }
        private static void BindExplicitInterfaceMethod(
            Compilation compilation,
            SyntaxTree tree,
            SemanticModel stubModel,
            TypeBinder typeBinder,
            MethodDeclarationSyntax syntax,
            SourceMethodSymbol method,
            DiagnosticBag diagnostics)
        {
            if (method.ContainingSymbol is not NamedTypeSymbol containingType)
                return;

            ValidateExplicitInterfaceMemberModifiers(tree, syntax.Modifiers, syntax, diagnostics);

            if (containingType.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_EXPLIFACE001",
                    DiagnosticSeverity.Error,
                    "Explicit interface implementations are only valid in classes or structs.",
                    new Location(tree, syntax.ExplicitInterfaceSpecifier!.Span)));
                return;
            }

            if (method.IsStatic)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_EXPLIFACE002",
                    DiagnosticSeverity.Error,
                    "Explicit interface implementation cannot be static.",
                    new Location(tree, syntax.Modifiers.Count > 0 ? syntax.Modifiers[0].Span : syntax.Span)));
                return;
            }

            var iface = BindExplicitInterfaceType(
                compilation, tree, stubModel, typeBinder, method,
                syntax.ExplicitInterfaceSpecifier!.Name, diagnostics);

            if (iface is null)
                return;

            if (!ImplementsInterface(containingType, iface))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_EXPLIFACE003",
                    DiagnosticSeverity.Error,
                    $"Type '{containingType.Name}' does not implement interface '{iface.Name}'.",
                    new Location(tree, syntax.ExplicitInterfaceSpecifier.Span)));
                return;
            }

            MethodSymbol? match = null;
            bool ambiguous = false;
            string sourceName = syntax.Identifier.ValueText ?? string.Empty;

            foreach (var curIface in EnumerateInterfaceClosure(iface))
            {
                var members = curIface.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is not MethodSymbol candidate)
                        continue;

                    if (candidate.IsConstructor)
                        continue;

                    if (!StringComparer.Ordinal.Equals(candidate.Name, sourceName))
                        continue;

                    if (!SameExplicitMethodSignature(method, candidate))
                        continue;

                    if (!LocalScopeBinder.AreSameType(method.ReturnType, candidate.ReturnType))
                        continue;

                    if (match is null)
                        match = candidate;
                    else if (!SameInterfaceMethodIdentity(match, candidate))
                        ambiguous = true;
                }
            }

            if (ambiguous)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_EXPLIFACE004",
                    DiagnosticSeverity.Error,
                    $"Explicit interface implementation '{iface.Name}.{sourceName}' is ambiguous.",
                    new Location(tree, syntax.Span)));
                return;
            }

            if (match is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_EXPLIFACE005",
                    DiagnosticSeverity.Error,
                    $"No interface method '{iface.Name}.{sourceName}' matches the implementing method signature.",
                    new Location(tree, syntax.Span)));
                return;
            }

            method.SetExplicitInterfaceImplementation(match);
        }

        private static void BindExplicitInterfaceProperty(
            Compilation compilation,
            SyntaxTree tree,
            SemanticModel stubModel,
            TypeBinder typeBinder,
            PropertyDeclarationSyntax syntax,
            SourcePropertySymbol property,
            DiagnosticBag diagnostics,
            bool isIndexer)
        {
            if (property.ContainingSymbol is not NamedTypeSymbol containingType)
                return;

            ValidateExplicitInterfaceMemberModifiers(tree, syntax.Modifiers, syntax, diagnostics);

            if (containingType.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_EXPLIFACE001",
                    DiagnosticSeverity.Error,
                    "Explicit interface implementations are only valid in classes or structs.",
                    new Location(tree, syntax.ExplicitInterfaceSpecifier!.Span)));
                return;
            }

            if (property.IsStatic)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_EXPLIFACE002",
                    DiagnosticSeverity.Error,
                    "Explicit interface implementation cannot be static.",
                    new Location(tree, syntax.Modifiers.Count > 0 ? syntax.Modifiers[0].Span : syntax.Span)));
                return;
            }

            var iface = BindExplicitInterfaceType(
                compilation, tree, stubModel, typeBinder, property,
                syntax.ExplicitInterfaceSpecifier!.Name, diagnostics);

            if (iface is null)
                return;

            if (!ImplementsInterface(containingType, iface))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_EXPLIFACE003",
                    DiagnosticSeverity.Error,
                    $"Type '{containingType.Name}' does not implement interface '{iface.Name}'.",
                    new Location(tree, syntax.ExplicitInterfaceSpecifier.Span)));
                return;
            }

            var sourceName = syntax.Identifier.ValueText ?? string.Empty;
            var match = FindMatchingInterfaceProperty(iface, sourceName, property, diagnostics, tree, syntax.Span);

            if (match is null)
                return;

            property.SetExplicitInterfaceImplementation(match);

            if (property.GetMethod is SourceMethodSymbol getImpl && match.GetMethod is MethodSymbol getIface)
                getImpl.SetExplicitInterfaceImplementation(getIface);

            if (property.SetMethod is SourceMethodSymbol setImpl && match.SetMethod is MethodSymbol setIface)
                setImpl.SetExplicitInterfaceImplementation(setIface);
        }

        private static void BindExplicitInterfaceIndexer(
            Compilation compilation,
            SyntaxTree tree,
            SemanticModel stubModel,
            TypeBinder typeBinder,
            IndexerDeclarationSyntax syntax,
            SourcePropertySymbol property,
            DiagnosticBag diagnostics)
        {
            if (property.ContainingSymbol is not NamedTypeSymbol containingType)
                return;

            ValidateExplicitInterfaceMemberModifiers(tree, syntax.Modifiers, syntax, diagnostics);

            if (containingType.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_EXPLIFACE001",
                    DiagnosticSeverity.Error,
                    "Explicit interface implementations are only valid in classes or structs.",
                    new Location(tree, syntax.ExplicitInterfaceSpecifier!.Span)));
                return;
            }

            if (property.IsStatic)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_EXPLIFACE002",
                    DiagnosticSeverity.Error,
                    "Explicit interface implementation cannot be static.",
                    new Location(tree, syntax.Modifiers.Count > 0 ? syntax.Modifiers[0].Span : syntax.Span)));
                return;
            }

            var iface = BindExplicitInterfaceType(
                compilation, tree, stubModel, typeBinder, property,
                syntax.ExplicitInterfaceSpecifier!.Name, diagnostics);

            if (iface is null)
                return;

            if (!ImplementsInterface(containingType, iface))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_EXPLIFACE003",
                    DiagnosticSeverity.Error,
                    $"Type '{containingType.Name}' does not implement interface '{iface.Name}'.",
                    new Location(tree, syntax.ExplicitInterfaceSpecifier.Span)));
                return;
            }

            var match = FindMatchingInterfaceProperty(iface, "Item", property, diagnostics, tree, syntax.Span);

            if (match is null)
                return;

            property.SetExplicitInterfaceImplementation(match);

            if (property.GetMethod is SourceMethodSymbol getImpl && match.GetMethod is MethodSymbol getIface)
                getImpl.SetExplicitInterfaceImplementation(getIface);

            if (property.SetMethod is SourceMethodSymbol setImpl && match.SetMethod is MethodSymbol setIface)
                setImpl.SetExplicitInterfaceImplementation(setIface);
        }

        private static NamedTypeSymbol? BindExplicitInterfaceType(
            Compilation compilation,
            SyntaxTree tree,
            SemanticModel stubModel,
            TypeBinder typeBinder,
            Symbol containingSymbol,
            NameSyntax ifaceName,
            DiagnosticBag diagnostics)
        {
            var ctx = new BindingContext(compilation, stubModel, containingSymbol, NullRecorder.Instance);
            var t = typeBinder.BindType(ifaceName, ctx, diagnostics);

            if (t is NamedTypeSymbol nt && nt.TypeKind == TypeKind.Interface)
                return nt;

            diagnostics.Add(new Diagnostic(
                "CN_EXPLIFACE006",
                DiagnosticSeverity.Error,
                $"'{t.Name}' is not an interface type.",
                new Location(tree, ifaceName.Span)));

            return null;
        }

        private static PropertySymbol? FindMatchingInterfaceProperty(
            NamedTypeSymbol iface,
            string name,
            SourcePropertySymbol property,
            DiagnosticBag diagnostics,
            SyntaxTree tree,
            TextSpan diagnosticSpan)
        {
            PropertySymbol? match = null;
            bool ambiguous = false;

            foreach (var curIface in EnumerateInterfaceClosure(iface))
            {
                var members = curIface.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is not PropertySymbol candidate)
                        continue;

                    if (!StringComparer.Ordinal.Equals(candidate.Name, name))
                        continue;

                    if (!SameExplicitPropertySignature(property, candidate))
                        continue;

                    if (!LocalScopeBinder.AreSameType(property.Type, candidate.Type))
                        continue;

                    if (candidate.HasGet && !property.HasGet)
                        continue;

                    if (candidate.HasSet && !property.HasSet)
                        continue;

                    if (match is null)
                        match = candidate;
                    else if (!SameInterfacePropertyIdentity(match, candidate))
                        ambiguous = true;
                }
            }

            if (ambiguous)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_EXPLIFACE007",
                    DiagnosticSeverity.Error,
                    $"Explicit interface property implementation '{iface.Name}.{name}' is ambiguous.",
                    new Location(tree, diagnosticSpan)));
                return null;
            }

            if (match is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_EXPLIFACE008",
                    DiagnosticSeverity.Error,
                    $"No interface property '{iface.Name}.{name}' matches the implementing member signature.",
                    new Location(tree, diagnosticSpan)));
                return null;
            }

            return match;
        }

        private static bool SameExplicitMethodSignature(MethodSymbol impl, MethodSymbol candidate)
        {
            if (impl.TypeParameters.Length != candidate.TypeParameters.Length)
                return false;

            var ap = impl.Parameters;
            var bp = candidate.Parameters;

            if (ap.Length != bp.Length)
                return false;

            for (int i = 0; i < ap.Length; i++)
            {
                if (ap[i].RefKind != bp[i].RefKind)
                    return false;

                if (ap[i].IsReadOnlyRef != bp[i].IsReadOnlyRef)
                    return false;

                if (!LocalScopeBinder.AreSameType(ap[i].Type, bp[i].Type))
                    return false;
            }

            return true;
        }

        private static bool SameExplicitPropertySignature(PropertySymbol impl, PropertySymbol candidate)
        {
            var ap = impl.Parameters;
            var bp = candidate.Parameters;

            if (ap.Length != bp.Length)
                return false;

            for (int i = 0; i < ap.Length; i++)
            {
                if (ap[i].RefKind != bp[i].RefKind)
                    return false;

                if (ap[i].IsReadOnlyRef != bp[i].IsReadOnlyRef)
                    return false;

                if (!LocalScopeBinder.AreSameType(ap[i].Type, bp[i].Type))
                    return false;
            }

            return true;
        }

        private static IEnumerable<NamedTypeSymbol> EnumerateInterfaceClosure(NamedTypeSymbol root)
        {
            var seen = new List<NamedTypeSymbol>();
            var queue = new Queue<NamedTypeSymbol>();
            queue.Enqueue(root);

            while (queue.Count != 0)
            {
                var cur = queue.Dequeue();
                if (ContainsInterfaceType(seen, cur))
                    continue;

                seen.Add(cur);
                yield return cur;

                var ifaces = cur.Interfaces;
                for (int i = 0; i < ifaces.Length; i++)
                {
                    if (ifaces[i] is NamedTypeSymbol nt && nt.TypeKind == TypeKind.Interface)
                        queue.Enqueue(nt);
                }
            }
        }
        private static bool ImplementsInterface(NamedTypeSymbol type, NamedTypeSymbol target)
        {
            var seen = new List<NamedTypeSymbol>();
            var queue = new Queue<NamedTypeSymbol>();

            for (NamedTypeSymbol? cur = type; cur is not null; cur = cur.BaseType as NamedTypeSymbol)
            {
                var ifaces = cur.Interfaces;
                for (int i = 0; i < ifaces.Length; i++)
                {
                    if (ifaces[i] is NamedTypeSymbol nt && nt.TypeKind == TypeKind.Interface)
                        queue.Enqueue(nt);
                }
            }

            while (queue.Count != 0)
            {
                var cur = queue.Dequeue();
                if (ContainsInterfaceType(seen, cur))
                    continue;

                seen.Add(cur);

                if (LocalScopeBinder.AreSameType(cur, target))
                    return true;

                var ifaces = cur.Interfaces;
                for (int i = 0; i < ifaces.Length; i++)
                {
                    if (ifaces[i] is NamedTypeSymbol nt && nt.TypeKind == TypeKind.Interface)
                        queue.Enqueue(nt);
                }
            }

            return false;
        }

        private static void ValidateExplicitInterfaceMemberModifiers(
            SyntaxTree tree,
            SyntaxTokenList modifiers,
            SyntaxNode syntax,
            DiagnosticBag diagnostics)
        {
            if (HasModifier(modifiers, SyntaxKind.PublicKeyword) ||
                HasModifier(modifiers, SyntaxKind.PrivateKeyword) ||
                HasModifier(modifiers, SyntaxKind.ProtectedKeyword) ||
                HasModifier(modifiers, SyntaxKind.InternalKeyword))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_EXPLIFACE009",
                    DiagnosticSeverity.Error,
                    "Explicit interface implementation cannot declare an accessibility modifier.",
                    new Location(tree, syntax.Span)));
            }

            if (HasModifier(modifiers, SyntaxKind.AbstractKeyword) ||
                HasModifier(modifiers, SyntaxKind.VirtualKeyword) ||
                HasModifier(modifiers, SyntaxKind.OverrideKeyword))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_EXPLIFACE010",
                    DiagnosticSeverity.Error,
                    "Explicit interface implementation cannot be abstract, virtual, or override.",
                    new Location(tree, syntax.Span)));
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
    }
}
