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

                        case DelegateDeclarationSyntax dds when symbol is Symbol:
                            BindAttributeListsOnOwner(
                                compilation, tree, dds.AttributeLists, ownerSyntax: dds, ownerSymbol: symbol,
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

                        case AccessorDeclarationSyntax ads when symbol is MethodSymbol:
                            BindAttributeListsOnOwner(
                                compilation, tree, ads.AttributeLists, ownerSyntax: ads, ownerSymbol: symbol,
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
            ValidateInlineArrayTypes(compilation, trees, diagnostics);
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
        private static void ValidateInlineArrayTypes(
            Compilation compilation,
            ImmutableArray<SyntaxTree> trees,
            DiagnosticBag diagnostics)
        {
            var seen = new HashSet<NamedTypeSymbol>(ReferenceEqualityComparer<NamedTypeSymbol>.Instance);

            for (int ti = 0; ti < trees.Length; ti++)
            {
                var tree = trees[ti];
                if (!compilation.DeclaredSymbolsByTree.TryGetValue(tree, out var declMap))
                    continue;

                foreach (var kv in declMap)
                {
                    if (kv.Value is not NamedTypeSymbol type || kv.Key is not TypeDeclarationSyntax syntax)
                        continue;

                    if (!seen.Add(type))
                        continue;

                    if (!InlineArrayFacts.TryGetLength(type, out int length))
                        continue;

                    var typeLocation = new Location(tree, syntax.Identifier.Span);
                    if (type.TypeKind != TypeKind.Struct)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_INLINEARRAY001",
                            DiagnosticSeverity.Error,
                            "Inline array attribute can only be applied to a struct type.",
                            typeLocation));
                        continue;
                    }

                    if (length <= 0)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_INLINEARRAY002",
                            DiagnosticSeverity.Error,
                            "Inline array length must be greater than zero.",
                            typeLocation));
                    }

                    int instanceFieldCount = 0;
                    FieldSymbol? elementField = null;
                    var members = type.GetMembers();
                    for (int i = 0; i < members.Length; i++)
                    {
                        if (members[i] is FieldSymbol f && !f.IsStatic && !f.IsConst)
                        {
                            instanceFieldCount++;
                            elementField = f;
                        }
                    }

                    if (instanceFieldCount != 1)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_INLINEARRAY003",
                            DiagnosticSeverity.Error,
                            "Inline array struct must declare exactly one instance field.",
                            typeLocation));
                    }
                    else if (elementField!.Type is ByRefTypeSymbol)
                    {
                        var loc = elementField.Locations.IsDefaultOrEmpty ? typeLocation : elementField.Locations[0];
                        diagnostics.Add(new Diagnostic(
                            "CN_INLINEARRAY004",
                            DiagnosticSeverity.Error,
                            "Inline array element field cannot be a ref field.",
                            loc));
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
}
