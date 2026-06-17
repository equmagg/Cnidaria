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
        internal static bool IsUnsafeContext(Symbol? symbol, BinderFlags flags = BinderFlags.None)
        {
            if ((flags & BinderFlags.UnsafeRegion) != 0)
                return true;
            for (Symbol? cur = symbol; cur is not null; cur = cur.ContainingSymbol)
            {
                switch (cur)
                {
                    case SourceNamedTypeSymbol type when type.IsUnsafe:
                        return true;

                    case SourceMethodSymbol method when method.IsUnsafe:
                        return true;

                    case SourceFieldSymbol field when field.IsUnsafe:
                        return true;
                }
            }
            return false;
        }
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

            if (element is TypeParameterSymbol tp)
            {
                if ((tp.GenericConstraint & GenericConstraintsFlags.StructConstraint) == 0)
                    return element;
            }

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
        internal static NamedTypeSymbol GetSystemNullableDefinitionOrReport(
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
            if (!IsUnsafeContext(context.ContainingSymbol, Flags))
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
}
