using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace Cnidaria.Rust;

internal sealed class Binder
{
    private readonly Compilation _compilation;
    internal readonly TypeSystem Types;
    internal ModuleSymbol Crate { get; }
    private readonly List<Symbol> _declarations = new();
    private readonly List<(UseItemSyntax Syntax, ModuleSymbol Module, SyntaxTree Tree)> _uses = new();
    private readonly List<(ImplItemSyntax Syntax, ModuleSymbol Module, SyntaxTree Tree)> _impls = new();
    private readonly List<(Symbol Symbol, VisibilitySyntax? Visibility)> _visibility = new();
    private readonly HashSet<SyntaxTree> _usedTrees = new();
    private readonly List<InferenceTypeSymbol> _inferences = new();
    private readonly List<Action> _checks = new();
    private readonly Dictionary<GlobalSymbol, BoundGlobal> _globals = new();
    private readonly List<BoundFunction> _functions = new();
    private int _typeDepth;

    internal Binder(Compilation compilation)
    {
        _compilation = compilation;
        Types = new TypeSystem(compilation.Options.PointerWidth);
        Crate = new ModuleSymbol(compilation.Options.CrateName, null, compilation.RootSyntaxTree.Root, compilation.RootSyntaxTree) { IsPublic = true };
    }
    internal SemanticData Data(SyntaxTree tree) => _compilation.GetSemanticModel(tree).Data;
    internal void Error(SyntaxTree tree, SyntaxNode syntax, string message, string code = "RSEM0001")
    {
        SemanticData data = Data(tree);
        data.Diagnostics.Add(new SemanticDiagnostic(code, message, tree, syntax.Span));
        data.ErrorNodes.Add(syntax);
    }
    internal void Unsupported(SyntaxTree tree, SyntaxNode syntax, string feature)
        => Error(tree, syntax, $"{feature} is not supported by semantic binding yet.", "RSEM0002");
    private void Declare(Symbol symbol, SyntaxNode syntax, SyntaxTree tree)
    {
        _declarations.Add(symbol);
        Data(tree).Declarations[syntax] = symbol;
    }
    internal void RecordSymbol(SyntaxTree tree, SyntaxNode syntax, Symbol symbol) => Data(tree).Symbols[syntax] = new SymbolInfo(symbol, []);
    internal void RecordType(SyntaxTree tree, SyntaxNode syntax, TypeSymbol type) => Data(tree).Types[syntax] = new TypeInfo(type, type);
    internal void AddScope(BindingScope scope) => Data(scope.Tree).Scopes.Add(scope);
    internal static Symbol Unwrap(Symbol symbol) => symbol is ImportSymbol import ? Unwrap(import.Target) : symbol;
    internal static string Name(SyntaxToken token) => token.Kind is SyntaxKind.LifetimeToken or SyntaxKind.RawLifetimeToken
        ? "'" + token.ValueText : token.ValueText ?? SyntaxFacts.GetText(token.Kind) ?? (token.Value is IFormattable value ? value.ToString(null, CultureInfo.InvariantCulture) : "");
    internal static bool Has(SyntaxNode node, SyntaxKind kind) => node.ChildTokens().Any(t => t.Kind == kind);

    internal void Bind()
    {
        _usedTrees.Add(_compilation.RootSyntaxTree);
        Declare(Crate, _compilation.RootSyntaxTree.Root, _compilation.RootSyntaxTree);
        AddScope(Crate.Scope);
        DeclareItems(_compilation.RootSyntaxTree.Root.Items, Crate, _compilation.RootSyntaxTree, "");
        foreach (var entry in _visibility.ToArray()) BindVisibility(entry.Symbol, entry.Visibility);
        BindImports();
        foreach (Symbol symbol in _declarations.ToArray()) BindSignature(symbol);
        foreach (var entry in _impls) BindImpl(entry.Syntax, entry.Module, entry.Tree);
        foreach (GlobalSymbol global in _declarations.OfType<GlobalSymbol>().ToArray()) BindGlobal(global);
        BindDiscriminants();
        foreach (FunctionSymbol function in _declarations.OfType<FunctionSymbol>().ToArray())
            _functions.Add(new BodyBinder(this, function.Scope, function).BindFunction());
        foreach (InferenceTypeSymbol inference in _inferences)
        {
            if (inference.Resolve() is not InferenceTypeSymbol unresolved) continue;
            if (unresolved.Constraint == InferenceKind.Any && unresolved.DefaultType is TypeSymbol defaultType) unresolved.Binding = defaultType;
            else if (unresolved.Constraint == InferenceKind.Integer) unresolved.Binding = Types.I32;
            else if (unresolved.Constraint == InferenceKind.Float) unresolved.Binding = Types.F64;
            else if (unresolved.HasNeverConstraint) unresolved.Binding = Types.Never;
            else
            {
                SyntaxTree tree = _compilation.SyntaxTrees.First(t => Data(t).Types.ContainsKey(unresolved.Origin) || t.Root.DescendantNodes().Contains(unresolved.Origin));
                Error(tree, unresolved.Origin, "Type cannot be inferred; add a type annotation.");
                unresolved.Binding = Types.Error;
            }
        }
        foreach (Action check in _checks) check();
        CheckLayouts();
        CheckSizedTypes();
        foreach (SyntaxTree tree in _compilation.SyntaxTrees)
        {
            if (!_usedTrees.Contains(tree)) Error(tree, tree.Root, "Module source is not referenced by a module declaration.");
            if (tree.Edition != RustEdition.Rust2024) Unsupported(tree, tree.Root, "Editions before Rust 2024");
            SemanticData data = Data(tree);
            foreach (SyntaxNode error in data.ErrorNodes)
            {
                for (SyntaxNode? current = error; current is not null; current = data.Parents.GetValueOrDefault(current))
                    if (data.BoundNodes.TryGetValue(current, out BoundNode? bound)) { bound.OwnErrors = true; break; }
            }
            data.BoundNodes[tree.Root] = new BoundCompilationUnit(tree.Root, Crate,
                _functions.Where(f => ReferenceEquals(f.Symbol.SyntaxTree, tree)).ToImmutableArray(),
                _globals.Values.Where(g => ReferenceEquals(g.Symbol.SyntaxTree, tree)).ToImmutableArray(),
                data.Diagnostics.Count != 0 || tree.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
        }
    }

    private void DeclareItems(IEnumerable<ItemSyntax> items, ModuleSymbol module, SyntaxTree tree, string modulePath)
    {
        foreach (ItemSyntax item in items)
        {
            CheckAttributes(item.Attributes, tree, item is FunctionItemSyntax);
            string name = Name(item.Identifier);
            Symbol? symbol = null;
            switch (item)
            {
                case ModuleItemSyntax syntax:
                {
                    var nested = new ModuleSymbol(name, module, syntax, tree);
                    symbol = nested;
                    AddMember(module, nested, SymbolNamespace.Type);
                    string path = modulePath.Length == 0 ? name : modulePath + "::" + name;
                    if (syntax.Body is ItemListSyntax body)
                    {
                        AddScope(nested.Scope);
                        DeclareItems(body.Items, nested, tree, path);
                    }
                    else if (_compilation.ModuleSources.TryGetValue(path, out SyntaxTree? source) && _usedTrees.Add(source))
                    {
                        var sourceScope = new BindingScope(null, nested, source, source.Root.Span);
                        // Both files declare the same module; entries are copied after imports are resolved.
                        AddScope(sourceScope);
                        DeclareItems(source.Root.Items, nested, source, path);
                        Data(source).Declarations[source.Root] = nested;
                    }
                    else Error(tree, syntax, $"No unique source supplied for module '{path}'.");
                    break;
                }
                case FunctionItemSyntax syntax:
                    symbol = new FunctionSymbol(name, module, syntax, tree, module.Scope, Types.Error);
                    AddMember(module, symbol, SymbolNamespace.Value);
                    break;
                case ExternBlockItemSyntax foreign:
                    if (!Has(foreign, SyntaxKind.UnsafeKeyword)) Error(tree, foreign, "Extern blocks must be unsafe in Rust 2024.");
                    string abi = foreign.ChildTokens().FirstOrDefault(t => t.Kind == SyntaxKind.StringLiteralToken).Value as string ?? "C";
                    foreach (ItemSyntax member in foreign.Body?.Items ?? [])
                    {
                        if (member is not FunctionItemSyntax declaration) { Unsupported(tree, member, "Non-function foreign items"); continue; }
                        DeclareItems(new[] { member }, module, tree, modulePath);
                        if (Data(tree).Declarations.GetValueOrDefault(member) is FunctionSymbol function)
                        {
                            function.IsForeign = true;
                            function.IsExtern = true;
                            function.IsUnsafe = !Has(member, SyntaxKind.SafeKeyword);
                            function.Abi = abi;
                            if (declaration.Body is not null) Error(tree, declaration, "Foreign functions cannot have a body.");
                            if (!function.TypeParameters.IsEmpty) Error(tree, declaration, "Foreign functions cannot have type parameters.");
                        }
                    }
                    break;
                case StructItemSyntax syntax:
                    symbol = new AggregateSymbol(name, TypeKind.Struct, Shape(syntax.Fields), module, item, tree);
                    AddMember(module, symbol, SymbolNamespace.Type);
                    if (((AggregateSymbol)symbol).Shape != AggregateShape.Named) AddMember(module, symbol, SymbolNamespace.Value, false);
                    break;
                case EnumItemSyntax syntax:
                {
                    var aggregate = new AggregateSymbol(name, TypeKind.Enum, AggregateShape.Named, module, item, tree);
                    symbol = aggregate;
                    AddMember(module, symbol, SymbolNamespace.Type);
                    aggregate.Variants = syntax.Variants.Select(v =>
                    {
                        var variant = new VariantSymbol(Name(v.Identifier), aggregate, v, tree, Shape(v.Fields));
                        Declare(variant, v, tree);
                        AddUnique(aggregate.Scope, variant.Name, SymbolNamespace.Type, variant, v);
                        if (variant.Shape != AggregateShape.Named) AddUnique(aggregate.Scope, variant.Name, SymbolNamespace.Value, variant, v);
                        return variant;
                    }).ToImmutableArray();
                    break;
                }
                case TypeAliasItemSyntax syntax:
                    symbol = new TypeAliasSymbol(name, module, syntax, tree, Types.Error);
                    AddMember(module, symbol, SymbolNamespace.Type);
                    break;
                case ConstItemSyntax:
                case StaticItemSyntax:
                    symbol = new GlobalSymbol(name, item is StaticItemSyntax, module, item, tree, Types.Error, Has(item, SyntaxKind.MutKeyword));
                    if (name != "_") AddMember(module, symbol, SymbolNamespace.Value);
                    break;
                case UseItemSyntax use: _uses.Add((use, module, tree)); break;
                case ImplItemSyntax impl: _impls.Add((impl, module, tree)); break;
                case MissingItemSyntax: break;
                default: Unsupported(tree, item, item.Kind.ToString()); break;
            }
            if (symbol is null) continue;
            Declare(symbol, item, tree);
            symbol.AccessibleWithin = module;
            _visibility.Add((symbol, item.Visibility));
            switch (symbol)
            {
                case AggregateSymbol a: a.TypeParameters = DeclareGenerics(item, a, a.Scope, tree); a.Type = new NamedTypeSymbol(a, a.TypeParameters.Select(p => p.Type).ToImmutableArray()); break;
                case FunctionSymbol f: f.TypeParameters = DeclareGenerics(item, f, f.Scope, tree); break;
                case TypeAliasSymbol a: a.TypeParameters = DeclareGenerics(item, a, a.Scope, tree); break;
            }
        }
    }
    private static AggregateShape Shape(FieldListSyntax? fields) => fields is null ? AggregateShape.Unit
        : Has(fields, SyntaxKind.OpenParenToken) ? AggregateShape.Tuple : AggregateShape.Named;
    private void AddMember(ModuleSymbol module, Symbol symbol, SymbolNamespace space, bool add = true)
    {
        if (add) module.Add(symbol);
        if (symbol.Declaration is SyntaxNode syntax) AddUnique(module.Scope, symbol.Name, space, symbol, syntax);
    }
    private void AddUnique(BindingScope scope, string name, SymbolNamespace space, Symbol symbol, SyntaxNode syntax)
    {
        if (scope.Entries.Any(e => e.Name == name && e.Namespace == space && e.Symbol is not ImportSymbol { IsGlob: true }))
            Error(symbol.SyntaxTree ?? scope.Tree, syntax, $"'{name}' is already defined in the {space.ToString().ToLowerInvariant()} namespace.");
        scope.Add(name, space, symbol);
    }
    internal void CheckAttributes(IEnumerable<AttributeSyntax> attributes, SyntaxTree tree, bool function = false)
    {
        foreach (AttributeSyntax attribute in attributes)
        {
            string name = attribute.Path?.Segments.FirstOrDefault()?.Identifier.ValueText ?? "";
            if (name is "no_mangle" or "export_name" or "link_name")
            {
                if (!function) Unsupported(tree, attribute, $"Attribute '{name}' outside a function");
                if (name != "link_name" && !attribute.IsUnsafe) Error(tree, attribute, $"Attribute '{name}' requires unsafe in Rust 2024.");
                if (name == "no_mangle")
                {
                    if (attribute.Input is not null || attribute.Value is not null) Error(tree, attribute, "The no_mangle attribute takes no arguments.");
                }
                else if (attribute.Input is not null || (attribute.Value?.Value as LiteralExpressionSyntax)?.Literal.Value is not string value || value.Length == 0 || value.Contains('\0'))
                    Error(tree, attribute, $"Attribute '{name}' requires a nonempty string without NUL.");
                continue;
            }
            if (name is not ("doc" or "allow" or "warn" or "deny" or "forbid" or "inline" or "cold" or "must_use"))
                Unsupported(tree, attribute, $"Attribute '{name}'");
        }
    }
    private ImmutableArray<TypeParameterSymbol> DeclareGenerics(ItemSyntax syntax, Symbol symbol, BindingScope scope, SyntaxTree tree)
    {
        AddScope(scope);
        var parameters = ImmutableArray.CreateBuilder<TypeParameterSymbol>();
        foreach (GenericParameterSyntax parameter in syntax.GenericParameters?.Parameters ?? [])
        {
            string name = Name(parameter.Identifier);
            if (parameter.IsConst) { Unsupported(tree, parameter, "Const generic parameters"); continue; }
            if (parameter.Identifier.Kind is SyntaxKind.LifetimeToken or SyntaxKind.RawLifetimeToken)
            {
                var lifetime = new LifetimeSymbol(name, symbol, parameter, tree);
                Declare(lifetime, parameter, tree);
                AddUnique(scope, name, SymbolNamespace.Lifetime, lifetime, parameter);
            }
            else
            {
                var type = new TypeParameterSymbol(name, symbol, parameter, tree, parameters.Count);
                parameters.Add(type); Declare(type, parameter, tree);
                AddUnique(scope, name, SymbolNamespace.Type, type, parameter);
            }
            if (parameter.Bounds is not null) Unsupported(tree, parameter.Bounds, "Generic bounds");
        }
        if (syntax.WhereClause is not null) Unsupported(tree, syntax.WhereClause, "Where clauses");
        return parameters.ToImmutable();
    }
    private void BindVisibility(Symbol symbol, VisibilitySyntax? visibility)
    {
        if (visibility is null || symbol.SyntaxTree is not SyntaxTree tree || symbol.DeclaringModule is not ModuleSymbol module) return;
        if (visibility.Restriction is null) { symbol.IsPublic = true; symbol.AccessibleWithin = null; return; }
        Symbol? restriction = ResolvePath(visibility.Restriction, module.Scope, tree, SymbolNamespace.Type);
        if (restriction is ModuleSymbol ancestor && ancestor.Contains(module)) symbol.AccessibleWithin = ancestor;
        else Error(tree, visibility, "Visibility must be restricted to an ancestor module.");
    }
    internal bool IsAccessible(Symbol symbol, ModuleSymbol from) => symbol.IsPublic || symbol.AccessibleWithin is null || symbol.AccessibleWithin.Contains(from);
    internal static ModuleSymbol ModuleOf(BindingScope scope)
    {
        if (scope.Owner is ModuleSymbol module) return module;
        return scope.Owner.DeclaringModule ?? throw new InvalidOperationException("Scope has no module.");
    }

    private void BindImports()
    {
        var pending = new List<(UseTreeSyntax Syntax, ModuleSymbol Module, SyntaxTree Tree, ImmutableArray<string> Path, bool Public)>();
        void Flatten(UseTreeSyntax use, ModuleSymbol module, SyntaxTree tree, ImmutableArray<string> prefix, bool isPublic)
        {
            ImmutableArray<string> path = prefix.AddRange(use.Path?.Segments.Select(s => Name(s.Identifier)) ?? []);
            if (use.ChildrenTrees.Any())
                foreach (UseTreeSyntax child in use.ChildrenTrees) Flatten(child, module, tree, path, isPublic);
            else pending.Add((use, module, tree, path, isPublic));
        }
        foreach (var use in _uses)
        {
            if (use.Syntax.Tree is UseTreeSyntax syntax)
            {
                if (use.Syntax.Visibility?.Restriction is not null) Unsupported(use.Tree, use.Syntax.Visibility, "Restricted visibility on imports");
                Flatten(syntax, use.Module, use.Tree, [], use.Syntax.Visibility is { Restriction: null });
            }
        }
        bool progress;
        do
        {
            progress = false;
            foreach (var use in pending.ToArray())
            {
                ImmutableArray<string> path = use.Path;
                bool self = path.Length > 1 && path[^1] == "self";
                if (self) path = path.RemoveAt(path.Length - 1);
                var targets = new List<(Symbol Symbol, SymbolNamespace Space)>();
                foreach (SymbolNamespace space in new[] { SymbolNamespace.Type, SymbolNamespace.Value })
                {
                    Symbol? target = ResolveNames(path, use.Module.Scope, space);
                    if (target is not null) targets.Add((target, space));
                }
                if (targets.Count == 0) continue;
                if (use.Syntax.IsGlob)
                {
                    Symbol source = Unwrap(targets[0].Symbol);
                    if (ReferenceEquals(source, use.Module)) { Error(use.Tree, use.Syntax, "Cannot glob-import a module into itself."); pending.Remove(use); progress = true; continue; }
                    BindingScope? members = source switch { ModuleSymbol m => m.Scope, AggregateSymbol { TypeKind: TypeKind.Enum } a => a.Scope, _ => null };
                    if (members is null) { Error(use.Tree, use.Syntax, "Glob imports require a module or enum."); pending.Remove(use); progress = true; continue; }
                    foreach (ScopeEntry member in members.Entries.ToArray())
                    {
                        if (source is AggregateSymbol && Unwrap(member.Symbol) is not VariantSymbol) continue;
                        if (!IsAccessible(member.Symbol, use.Module)) continue;
                        progress |= AddImport(use.Syntax, use.Module, use.Tree, member.Name, member.Symbol, member.Namespace, true, use.Public);
                    }
                    continue;
                }
                foreach (var target in targets)
                {
                    if (self && target.Space != SymbolNamespace.Type) continue;
                    string alias = use.Syntax.Alias.Kind == SyntaxKind.None ? path.LastOrDefault() ?? "" : Name(use.Syntax.Alias);
                    AddImport(use.Syntax, use.Module, use.Tree, alias, target.Symbol, target.Space, false, use.Public);
                }
                pending.Remove(use); progress = true;
            }
        } while (progress);
        foreach (var use in pending)
            if (!use.Syntax.IsGlob || ResolveNames(use.Path, use.Module.Scope, SymbolNamespace.Type) is null)
                Error(use.Tree, use.Syntax, $"Cannot resolve import '{string.Join("::", use.Path)}'.");
        foreach (SyntaxTree tree in _compilation.SyntaxTrees)
            foreach (BindingScope scope in Data(tree).Scopes)
                if (scope.Owner is ModuleSymbol module && !ReferenceEquals(scope, module.Scope))
                    scope.Entries.AddRange(module.Scope.Entries);
    }
    private bool AddImport(UseTreeSyntax syntax, ModuleSymbol module, SyntaxTree tree, string name, Symbol target, SymbolNamespace space, bool glob, bool isPublic)
    {
        Symbol original = Unwrap(target);
        if (name == "_") return false;
        if (glob && module.Scope.Entries.Any(e => e.Name == name && e.Namespace == space && e.Symbol is ImportSymbol { IsGlob: true } i && ReferenceEquals(Unwrap(i), original))) return false;
        if (!IsAccessible(target, module)) { Error(tree, syntax, $"'{target.Name}' is private."); return false; }
        if (isPublic && !original.IsPublic) Error(tree, syntax, $"Cannot publicly re-export private '{target.Name}'.");
        var import = new ImportSymbol(name, original, module, syntax, tree, space, glob) { IsPublic = isPublic, AccessibleWithin = isPublic ? null : module };
        if (glob) module.Scope.Add(name, space, import);
        else AddUnique(module.Scope, name, space, import, syntax);
        module.Add(import); Data(tree).Declarations.TryAdd(syntax, import); RecordSymbol(tree, syntax, original);
        return true;
    }

    private Symbol? ResolveNames(ImmutableArray<string> names, BindingScope scope, SymbolNamespace space)
    {
        if (names.IsEmpty) return null;
        ModuleSymbol module = ModuleOf(scope);
        Symbol? current;
        int index = 1;
        string first = names[0];
        if (first == "crate") current = Crate;
        else if (first == "self") current = module;
        else if (first == "super") current = module.Parent;
        else
        {
            ImmutableArray<Symbol> matches = scope.Find(first, names.Length == 1 ? space : SymbolNamespace.Type);
            current = matches.Length == 1 ? matches[0] : null;
        }
        while (current is not null && index < names.Length)
        {
            if (!IsAccessible(current, module)) return null;
            current = Unwrap(current);
            string name = names[index++];
            if (name == "super" && current is ModuleSymbol parent) { current = parent.Parent; continue; }
            BindingScope? members = current switch { ModuleSymbol m => m.Scope, AggregateSymbol a => a.Scope, _ => null };
            ImmutableArray<Symbol> matches = members?.Entries.Where(e => e.Name == name && e.Namespace == (index == names.Length ? space : SymbolNamespace.Type)).Select(e => e.Symbol).ToImmutableArray() ?? [];
            Symbol[] explicitMatches = matches.Where(s => s is not ImportSymbol { IsGlob: true }).ToArray();
            current = explicitMatches.Length == 1 ? explicitMatches[0] : matches.DistinctBy(Unwrap).Count() == 1 ? matches[0] : null;
        }
        return current is not null && IsAccessible(current, module) && !(space == SymbolNamespace.Value && Unwrap(current) is ModuleSymbol) ? current : null;
    }
    internal Symbol? ResolvePath(PathSyntax? path, BindingScope scope, SyntaxTree tree, SymbolNamespace space, bool diagnose = true, int position = int.MaxValue)
    {
        if (path is null) return null;
        if (path.Qualification is not null) { if (diagnose) Unsupported(tree, path, "Qualified associated paths"); return null; }
        if (path.Children.FirstOrDefault().IsToken && path.Children[0].Token.Kind == SyntaxKind.ColonColonToken)
        { if (diagnose) Unsupported(tree, path, "External crate paths"); return null; }
        PathSegmentSyntax[] segments = path.Segments.ToArray();
        if (segments.Length == 0) return null;
        Symbol? result = null;
        for (int i = 0; i < segments.Length; i++)
        {
            string name = Name(segments[i].Identifier);
            SymbolNamespace ns = i == segments.Length - 1 ? space : SymbolNamespace.Type;
            ImmutableArray<Symbol> matches = [];
            if (i == 0)
            {
                if (name == "crate") result = Crate;
                else if (name == "self" && (segments.Length > 1 || space == SymbolNamespace.Type)) result = ModuleOf(scope);
                else if (name == "super") result = ModuleOf(scope).Parent;
                else if (name == "Self") result = FindSelf(scope)?.Definition;
                else
                {
                    matches = scope.Find(name, ns, position);
                    result = matches.Length == 1 ? matches[0] : null;
                }
            }
            else
            {
                Symbol? owner = result is null ? null : Unwrap(result);
                if (name == "super" && owner is ModuleSymbol module) result = module.Parent;
                else
                {
                    BindingScope? members = owner switch { ModuleSymbol m => m.Scope, AggregateSymbol a => a.Scope, _ => null };
                    matches = members is null ? [] : FindMember(members, name, ns);
                    result = matches.Length == 1 ? matches[0] : null;
                }
            }
            if (result is null)
            {
                Data(tree).Symbols[segments[i]] = new SymbolInfo(null, matches);
                Data(tree).Symbols[path] = new SymbolInfo(null, matches);
                if (diagnose) Error(tree, segments[i], matches.Length > 1 ? $"'{name}' is ambiguous." : $"Cannot resolve '{name}' in the {ns.ToString().ToLowerInvariant()} namespace.");
                return null;
            }
            if (diagnose && !IsAccessible(result, ModuleOf(scope))) Error(tree, segments[i], $"'{name}' is private.");
            result = Unwrap(result);
            if (diagnose && result is ModuleSymbol && segments[i].Arguments is not null) Error(tree, segments[i], "Modules do not take generic arguments.");
            RecordSymbol(tree, segments[i], result);
        }
        if (result is not null) RecordSymbol(tree, path, result);
        return result;
    }
    private static ImmutableArray<Symbol> FindMember(BindingScope scope, string name, SymbolNamespace space)
    {
        var all = scope.Entries.Where(e => e.Name == name && e.Namespace == space).Select(e => e.Symbol).ToArray();
        var explicitMatches = all.Where(s => s is not ImportSymbol { IsGlob: true }).ToArray();
        return (explicitMatches.Length == 0 ? all.DistinctBy(Unwrap) : explicitMatches).ToImmutableArray();
    }
    private static NamedTypeSymbol? FindSelf(BindingScope scope)
    {
        for (BindingScope? current = scope; current is not null; current = current.Parent)
        {
            if (current.Owner is FunctionSymbol { SelfType: NamedTypeSymbol self }) return self;
            if (current.Owner is AggregateSymbol aggregate) return aggregate.Type;
        }
        return null;
    }

    private void BindSignature(Symbol symbol)
    {
        switch (symbol)
        {
            case AggregateSymbol aggregate when aggregate.SignatureState == 0:
            {
                aggregate.SignatureState = 1;
                var tree = aggregate.Scope.Tree;
                BindDefaults(aggregate.TypeParameters, aggregate.Scope, tree);
                if (aggregate.Declaration is StructItemSyntax structure) aggregate.Fields = BindFields(structure.Fields, aggregate, aggregate.Scope, tree);
                foreach (VariantSymbol variant in aggregate.Variants)
                    if (variant.Declaration is EnumVariantSyntax syntax) variant.Fields = BindFields(syntax.Fields, variant, aggregate.Scope, tree);
                aggregate.SignatureState = 2;
                break;
            }
            case TypeAliasSymbol alias:
            {
                if (alias.SignatureState == 2) break;
                if (alias.SignatureState == 1) { Error(alias.Scope.Tree, alias.Scope.Owner.Declaration ?? alias.Scope.Tree.Root, $"Cyclic type alias '{alias.Name}'."); alias.Target = Types.Error; return; }
                alias.SignatureState = 1;
                BindDefaults(alias.TypeParameters, alias.Scope, alias.Scope.Tree);
                alias.Target = BindType((alias.Declaration as TypeAliasItemSyntax)?.Value?.Value as TypeSyntax, alias.Scope, alias.Scope.Tree);
                alias.SignatureState = 2;
                break;
            }
            case FunctionSymbol function when function.SignatureState == 0:
            {
                function.SignatureState = 1;
                FunctionItemSyntax syntax = function.Declaration;
                SyntaxTree tree = function.Scope.Tree;
                if (!function.IsForeign)
                {
                    function.IsUnsafe = Has(syntax, SyntaxKind.UnsafeKeyword);
                    function.IsExtern = Has(syntax, SyntaxKind.ExternKeyword);
                    function.Abi = function.IsExtern ? syntax.ChildTokens().FirstOrDefault(t => t.Kind == SyntaxKind.StringLiteralToken).Value as string ?? "C" : "Rust";
                    if (Has(syntax, SyntaxKind.SafeKeyword)) Error(tree, syntax, "The safe qualifier is only valid on foreign functions.");
                }
                foreach (AttributeSyntax attribute in syntax.Attributes)
                {
                    string name = attribute.Path?.Segments.FirstOrDefault()?.Identifier.ValueText ?? "";
                    if (name is not ("no_mangle" or "export_name" or "link_name")) continue;
                    if (function.IsForeign != (name == "link_name"))
                        Error(tree, attribute, "Export attributes require a function body; link_name requires a foreign function.");
                    if (name == "no_mangle")
                    {
                        if (function.Name.Any(c => c > 127)) Error(tree, attribute, "An unmangled identifier must contain only ASCII characters.");
                        function.ExternalName ??= function.Name;
                    }
                    else if ((attribute.Value?.Value as LiteralExpressionSyntax)?.Literal.Value is string value)
                        function.ExternalName = value;
                }
                if (function.ExternalName is not null && !function.TypeParameters.IsEmpty)
                    Error(tree, syntax, "A function with a fixed external name cannot have type parameters.");
                if (Has(syntax, SyntaxKind.AsyncKeyword)) Unsupported(tree, syntax, "Async functions");
                if (Has(syntax, SyntaxKind.ConstKeyword)) Unsupported(tree, syntax, "Const functions");
                BindDefaults(function.TypeParameters, function.Scope, tree, true);
                var parameters = ImmutableArray.CreateBuilder<ParameterSymbol>();
                foreach (ParameterSyntax parameter in syntax.Parameters?.Parameters ?? [])
                {
                    if (parameter.IsVariadic) Unsupported(tree, parameter, "Variadic parameters");
                    TypeSymbol type;
                    if (parameter.IsSelf)
                    {
                        type = function.SelfType ?? (TypeSymbol)Types.Error;
                        if (function.SelfType is null || parameters.Count != 0) Error(tree, parameter, "'self' must be the first parameter of an associated function.");
                        if (parameter.Type is not null) type = BindType(parameter.Type, function.Scope, tree);
                        else if (Has(parameter, SyntaxKind.AmpersandToken)) type = new IndirectTypeSymbol(TypeKind.Reference, type, Has(parameter, SyntaxKind.MutKeyword),
                            BindLifetime(parameter.ChildTokens().FirstOrDefault(t => t.Kind is SyntaxKind.LifetimeToken or SyntaxKind.RawLifetimeToken), parameter, function.Scope, tree));
                        TypeSymbol receiverType = type is IndirectTypeSymbol { Kind: TypeKind.Reference } selfReference ? selfReference.ElementType : type;
                        if (function.SelfType is not null && !TypeSystem.Same(receiverType, function.SelfType))
                            Unsupported(tree, parameter, "Arbitrary self parameter types");
                    }
                    else type = BindType(parameter.Type, function.Scope, tree);
                    string name = parameter.IsSelf ? "self" : parameter.Pattern is IdentifierPatternSyntax id ? Name(id.Identifier) : "$" + parameters.Count;
                    var p = new ParameterSymbol(name, function, parameter, tree, type, parameters.Count,
                        parameter.IsSelf ? !Has(parameter, SyntaxKind.AmpersandToken) && Has(parameter, SyntaxKind.MutKeyword) : parameter.Pattern is IdentifierPatternSyntax binding && Has(binding, SyntaxKind.MutKeyword));
                    Declare(p, parameter, tree); RecordType(tree, parameter, type); parameters.Add(p);
                }
                function.Parameters = parameters.ToImmutable();
                function.ReturnType = syntax.ReturnType is null ? Types.Unit : BindType(syntax.ReturnType.Type, function.Scope, tree);
                var inputLifetimes = function.Parameters.SelectMany(p => Lifetimes(p.Type)).Distinct().ToArray();
                LifetimeSymbol? outputLifetime = function.Parameters.FirstOrDefault(p => p.IsSelf)?.Type is IndirectTypeSymbol { Kind: TypeKind.Reference } receiver
                    ? receiver.Lifetime : inputLifetimes.Length == 1 ? inputLifetimes[0] : null;
                if (Lifetimes(function.ReturnType).Any(l => l.IsElided))
                {
                    if (outputLifetime is null) Error(tree, syntax.ReturnType ?? (SyntaxNode)syntax, "Return reference requires an explicit lifetime; input lifetimes do not determine it.");
                    else function.ReturnType = ReplaceElidedLifetimes(function.ReturnType, outputLifetime);
                }
                function.SignatureState = 2;
                break;
            }
            case GlobalSymbol global when global.Type.IsError:
                global.Type = BindType(global.Declaration switch { ConstItemSyntax c => c.Type, StaticItemSyntax s => s.Type, _ => null }, global.Scope, global.SyntaxTree ?? global.Scope.Tree);
                global.Type = ReplaceElidedLifetimes(global.Type, Types.StaticLifetime);
                break;
        }
    }
    private void BindDefaults(ImmutableArray<TypeParameterSymbol> parameters, BindingScope scope, SyntaxTree tree, bool function = false)
    {
        bool optional = false;
        foreach (TypeParameterSymbol parameter in parameters)
        {
            GenericParameterSyntax syntax = parameter.Declaration;
            if (syntax.Default?.Value is TypeSyntax type)
            {
                if (function) Error(tree, syntax, "Function type parameters cannot have defaults.");
                parameter.DefaultType = BindType(type, scope, tree);
                if (ContainsParameter(parameter.DefaultType, p => p.Ordinal >= parameter.Ordinal && ReferenceEquals(p.ContainingSymbol, parameter.ContainingSymbol)))
                    Error(tree, type, "A generic default may only reference earlier type parameters.");
                optional = true;
            }
            else if (optional) Error(tree, syntax, "Required type parameters cannot follow defaulted parameters.");
        }
    }
    private ImmutableArray<FieldSymbol> BindFields(FieldListSyntax? fields, Symbol owner, BindingScope scope, SyntaxTree tree)
    {
        var result = ImmutableArray.CreateBuilder<FieldSymbol>();
        foreach (FieldSyntax syntax in fields?.Fields ?? [])
        {
            CheckAttributes(syntax.Attributes, tree);
            string name = Shape(fields) == AggregateShape.Tuple ? result.Count.ToString(CultureInfo.InvariantCulture) : Name(syntax.Identifier);
            if (result.Any(f => f.Name == name)) Error(tree, syntax, $"Duplicate field '{name}'.");
            var field = new FieldSymbol(name, owner, syntax, tree, BindType(syntax.Type, scope, tree), result.Count)
            { IsPublic = owner is VariantSymbol, AccessibleWithin = ModuleOf(scope) };
            if (owner is not VariantSymbol) BindVisibility(field, syntax.Visibility);
            Declare(field, syntax, tree); RecordType(tree, syntax, field.Type); result.Add(field);
        }
        return result.ToImmutable();
    }
    private void BindImpl(ImplItemSyntax syntax, ModuleSymbol module, SyntaxTree tree)
    {
        if (syntax.Trait is not null || syntax.GenericParameters is not null || syntax.WhereClause is not null)
        { Unsupported(tree, syntax, "Trait or generic impl blocks"); return; }
        if (BindType(syntax.SelfType, module.Scope, tree) is not NamedTypeSymbol self)
        { Error(tree, syntax, "An inherent impl requires a named type."); return; }
        if (!self.TypeArguments.IsEmpty) { Unsupported(tree, syntax, "Generic inherent impls"); return; }
        foreach (ItemSyntax item in syntax.Body?.Items ?? [])
        {
            if (item is not FunctionItemSyntax method) { Unsupported(tree, item, "Non-function associated items"); continue; }
            CheckAttributes(method.Attributes, tree, function: true);
            var function = new FunctionSymbol(Name(method.Identifier), self.Definition, method, tree, module.Scope, Types.Error)
            { SelfType = self, AccessibleWithin = module };
            Declare(function, method, tree);
            function.TypeParameters = DeclareGenerics(method, function, function.Scope, tree);
            BindVisibility(function, method.Visibility);
            AddUnique(self.Definition.Scope, function.Name, SymbolNamespace.Value, function, method);
            self.Definition.Methods = self.Definition.Methods.Add(function);
            BindSignature(function);
        }
    }

    internal TypeSymbol BindType(TypeSyntax? syntax, BindingScope scope, SyntaxTree tree, bool infer = false)
    {
        if (syntax is null) return Types.Error;
        if (++_typeDepth > 256) { _typeDepth--; Error(tree, syntax, "Type nesting exceeds the semantic binding limit."); return Types.Error; }
        TypeSymbol type;
        try
        {
            type = syntax switch
            {
                PathTypeSyntax path => BindPathType(path.Path, scope, tree, infer),
                ParenthesizedTypeSyntax p => BindType(p.Type, scope, tree, infer),
                TupleTypeSyntax tuple => new TupleTypeSymbol(tuple.Elements.Select(t => BindType(t, scope, tree, infer)).ToImmutableArray()),
                ReferenceTypeSyntax r => new IndirectTypeSymbol(TypeKind.Reference, BindType(r.ElementType, scope, tree, infer), r.IsMutable, BindLifetime(r.Lifetime, r, scope, tree)),
                PointerTypeSyntax p => new IndirectTypeSymbol(TypeKind.Pointer, BindType(p.ElementType, scope, tree, infer), p.IsMutable),
                SliceTypeSyntax s => new SequenceTypeSymbol(TypeKind.Slice, BindType(s.ElementType, scope, tree, infer)),
                ArrayTypeSyntax a => new SequenceTypeSymbol(TypeKind.Array, BindType(a.ElementType, scope, tree, infer), BindLength(a.Length, scope, tree)),
                NeverTypeSyntax => Types.Never,
                InferredTypeSyntax when infer => Fresh(syntax),
                MissingTypeSyntax => Types.Error,
                FunctionPointerTypeSyntax f => BindFunctionType(f, scope, tree),
                _ => UnsupportedType(syntax, tree)
            };
        }
        finally { _typeDepth--; }
        RecordType(tree, syntax, type);
        if (syntax is PathTypeSyntax { Path: PathSyntax typePath } && Data(tree).Symbols.TryGetValue(typePath, out SymbolInfo info)) Data(tree).Symbols[syntax] = info;
        CheckLater(() =>
        {
            TypeSymbol resolved = type.Resolve();
            IEnumerable<TypeSymbol> sized = resolved switch
            {
                SequenceTypeSymbol s => new[] { s.ElementType }, TupleTypeSymbol t => t.Elements,
                NamedTypeSymbol n => n.TypeArguments, FunctionTypeSymbol f => f.ParameterTypes.Append(f.ReturnType), _ => []
            };
            if (sized.Any(t => !IsSized(t))) Error(tree, syntax, "This type requires sized element or generic argument types.");
        });
        return type;
    }
    private TypeSymbol UnsupportedType(TypeSyntax syntax, SyntaxTree tree) { Unsupported(tree, syntax, syntax.Kind.ToString()); return Types.Error; }
    private TypeSymbol BindFunctionType(FunctionPointerTypeSyntax syntax, BindingScope scope, SyntaxTree tree)
    {
        if (syntax.Binder is not null) Unsupported(tree, syntax.Binder, "Higher-ranked function types");
        if (syntax.DescendantTokens().Any(t => t.Kind == SyntaxKind.DotDotDotToken)) Unsupported(tree, syntax, "Variadic function types");
        return new FunctionTypeSymbol((syntax.Parameters?.Types ?? []).Select(t => BindType(t, scope, tree)).ToImmutableArray(),
            syntax.ReturnType is null ? Types.Unit : BindType(syntax.ReturnType.Type, scope, tree),
            Has(syntax, SyntaxKind.UnsafeKeyword), Has(syntax, SyntaxKind.ExternKeyword) ? syntax.ChildTokens().FirstOrDefault(t => t.Kind == SyntaxKind.StringLiteralToken).Value as string ?? "C" : "Rust");
    }
    private LifetimeSymbol? BindLifetime(SyntaxToken token, SyntaxNode syntax, BindingScope scope, SyntaxTree tree)
    {
        string name = Name(token);
        if (name.Length == 0 || name == "'_") return new LifetimeSymbol("'_", elided: true);
        if (name == "'static") return Types.StaticLifetime;
        Symbol? symbol = scope.Find(name, SymbolNamespace.Lifetime).FirstOrDefault();
        if (symbol is LifetimeSymbol lifetime) return lifetime;
        Error(tree, syntax, $"Undeclared lifetime '{name}'.");
        return null;
    }
    internal TypeSymbol BindPathType(PathSyntax? path, BindingScope scope, SyntaxTree tree, bool infer)
    {
        if (path is null) return Types.Error;
        PathSegmentSyntax[] segments = path.Segments.ToArray();
        if (segments.Length == 1)
        {
            string name = Name(segments[0].Identifier);
            if (name == "Self" && FindSelf(scope) is NamedTypeSymbol self)
            {
                RecordSymbol(tree, path, self.Definition); RecordType(tree, path, self);
                return self;
            }
            if (scope.Find(name, SymbolNamespace.Type).IsEmpty && Types.Primitives.TryGetValue(name, out PrimitiveTypeSymbol? primitive))
            {
                if (segments[0].Arguments is not null) Error(tree, path, "Primitive types do not take generic arguments.");
                RecordType(tree, path, primitive); return primitive;
            }
        }
        Symbol? symbol = ResolvePath(path, scope, tree, SymbolNamespace.Type);
        TypeSymbol type;
        switch (symbol)
        {
            case TypeParameterSymbol parameter:
                if (segments.LastOrDefault()?.Arguments is not null) Error(tree, path, "Type parameters do not take generic arguments.");
                type = parameter.Type; break;
            case AggregateSymbol aggregate:
                BindSignature(aggregate);
                type = new NamedTypeSymbol(aggregate, BindArguments(aggregate.TypeParameters, segments.LastOrDefault()?.Arguments, path, scope, tree, infer)); break;
            case TypeAliasSymbol alias:
                BindSignature(alias);
                ImmutableArray<TypeSymbol> arguments = BindArguments(alias.TypeParameters, segments.LastOrDefault()?.Arguments, path, scope, tree, infer);
                type = Types.Substitute(alias.Target, alias.TypeParameters.Zip(arguments).ToDictionary(p => p.First, p => p.Second)); break;
            default:
                if (symbol is not null) Error(tree, path, $"'{symbol.Name}' is not a type.");
                type = Types.Error; break;
        }
        RecordType(tree, path, type);
        return type;
    }
    internal ImmutableArray<TypeSymbol> BindArguments(ImmutableArray<TypeParameterSymbol> parameters, GenericArgumentListSyntax? syntax, SyntaxNode origin, BindingScope scope, SyntaxTree tree, bool infer)
    {
        var supplied = (syntax?.Arguments ?? []).Where(a => a.Value is not null || !a.ChildTokens().Any(t => t.Kind is SyntaxKind.LifetimeToken or SyntaxKind.RawLifetimeToken)).ToArray();
        foreach (GenericArgumentSyntax lifetime in (syntax?.Arguments ?? []).Except(supplied))
            BindLifetime(lifetime.ChildTokens().First(), lifetime, scope, tree);
        if (supplied.Length > parameters.Length) Error(tree, syntax ?? origin, $"Expected {parameters.Length} type arguments, got {supplied.Length}.");
        var result = ImmutableArray.CreateBuilder<TypeSymbol>();
        var substitution = new Dictionary<TypeParameterSymbol, TypeSymbol>();
        for (int i = 0; i < parameters.Length; i++)
        {
            TypeSymbol value;
            if (i < supplied.Length && supplied[i].Value is TypeSyntax type && supplied[i].Binding is null && supplied[i].Bounds is null)
                value = BindType(type, scope, tree, infer);
            else if (i < supplied.Length) { Unsupported(tree, supplied[i], "Non-type generic arguments"); value = Types.Error; }
            else if (infer && syntax is null) value = new InferenceTypeSymbol(InferenceKind.Any, origin)
            {
                DefaultType = parameters[i].DefaultType is TypeSymbol defaultType ? Types.Substitute(defaultType, substitution) : null
            };
            else if (parameters[i].DefaultType is TypeSymbol fallback) value = Types.Substitute(fallback, substitution);
            else if (infer) value = Fresh(origin);
            else { Error(tree, syntax ?? origin, $"Missing type argument '{parameters[i].Name}'."); value = Types.Error; }
            if (infer && syntax is null && value is InferenceTypeSymbol implicitArgument) _inferences.Add(implicitArgument);
            result.Add(value); substitution.Add(parameters[i], value);
        }
        return result.ToImmutable();
    }
    internal ulong? BindLength(ExpressionSyntax? syntax, BindingScope scope, SyntaxTree tree)
    {
        if (syntax is null) return null;
        BoundExpression expression = new BodyBinder(this, scope, null, tree).Expression(syntax, Types.Usize);
        if (expression.ConstantValue.Value is BigInteger value && value >= 0 && value <= (BigInteger.One << _compilation.Options.PointerWidth) - 1) return (ulong)value;
        Error(tree, syntax, "Array length must be a constant usize.");
        return null;
    }
    internal BoundGlobal BindGlobal(GlobalSymbol global)
    {
        if (_globals.TryGetValue(global, out BoundGlobal? bound)) return bound;
        ItemSyntax syntax = global.Declaration;
        SyntaxTree tree = global.SyntaxTree ?? global.Scope.Tree;
        if (global.BindingState == 1)
        {
            Error(tree, syntax, $"Cyclic constant initializer '{global.Name}'.");
            return new BoundGlobal(syntax, global, new BoundErrorExpression(syntax, Types.Error));
        }
        global.BindingState = 1;
        BindSignature(global);
        ExpressionSyntax? initializer = (syntax switch { ConstItemSyntax c => c.Value, StaticItemSyntax s => s.Value, _ => null })?.Value as ExpressionSyntax;
        BoundExpression? value = initializer is null ? null : new BodyBinder(this, global.Scope, null, tree).Expression(initializer, global.Type);
        if (value is null) Error(tree, syntax, "An initializer is required.");
        else if (!value.ConstantValue.HasValue && !value.HasErrors) Unsupported(tree, initializer ?? (SyntaxNode)syntax, "This constant initializer");
        if (global.Kind == SymbolKind.Constant) global.ConstantValue = value?.ConstantValue ?? default;
        bound = new BoundGlobal(syntax, global, value);
        _globals.Add(global, bound); global.BindingState = 2; Data(tree).BoundNodes[syntax] = bound;
        return bound;
    }

    private void BindDiscriminants()
    {
        foreach (AggregateSymbol aggregate in _declarations.OfType<AggregateSymbol>().Where(a => a.TypeKind == TypeKind.Enum))
        {
            BigInteger next = BigInteger.Zero;
            var used = new HashSet<BigInteger>();
            foreach (VariantSymbol variant in aggregate.Variants)
            {
                if (variant.Declaration is not EnumVariantSyntax syntax) continue;
                if (syntax.Discriminant?.Value is ExpressionSyntax expression)
                {
                    if (aggregate.Variants.Any(v => !v.Fields.IsEmpty)) Error(aggregate.Scope.Tree, syntax, "Explicit discriminants on data-carrying enums require an integer repr.");
                    BoundExpression value = new BodyBinder(this, aggregate.Scope, null).Expression(expression, Types.Primitives["isize"]);
                    if (value.ConstantValue.Value is BigInteger integer) next = integer;
                    else Error(aggregate.Scope.Tree, expression, "Enum discriminant must be an integer constant.");
                }
                BigInteger limit = BigInteger.One << (_compilation.Options.PointerWidth - 1);
                if (next < -limit || next >= limit) Error(aggregate.Scope.Tree, syntax, "Enum discriminant overflows isize.");
                if (!used.Add(next)) Error(aggregate.Scope.Tree, syntax, $"Discriminant '{next}' is assigned more than once.");
                variant.Discriminant = next++;
            }
        }
    }

    internal InferenceTypeSymbol Fresh(SyntaxNode origin, InferenceKind constraint = InferenceKind.Any)
    {
        var type = new InferenceTypeSymbol(constraint, origin); _inferences.Add(type);
        return type;
    }
    internal void CheckLater(Action check) => _checks.Add(check);
    internal bool Unify(TypeSymbol left, TypeSymbol right)
    {
        left = left.Resolve(); right = right.Resolve();
        if (left.IsError || right.IsError || TypeSystem.Same(left, right)) return true;
        if (left is InferenceTypeSymbol leftInference) return BindInference(leftInference, right);
        if (right is InferenceTypeSymbol rightInference) return BindInference(rightInference, left);
        if (left.Kind != right.Kind) return false;
        return (left, right) switch
        {
            (TupleTypeSymbol a, TupleTypeSymbol b) => UnifyList(a.Elements, b.Elements),
            (NamedTypeSymbol a, NamedTypeSymbol b) => ReferenceEquals(a.Definition, b.Definition) && UnifyList(a.TypeArguments, b.TypeArguments),
            (SequenceTypeSymbol a, SequenceTypeSymbol b) => a.Length == b.Length && a.RangeForm == b.RangeForm && Unify(a.ElementType, b.ElementType),
            (IndirectTypeSymbol a, IndirectTypeSymbol b) => a.IsMutable == b.IsMutable && Unify(a.ElementType, b.ElementType),
            (FunctionTypeSymbol a, FunctionTypeSymbol b) => a.Abi == b.Abi && a.IsUnsafe == b.IsUnsafe && UnifyList(a.ParameterTypes, b.ParameterTypes) && Unify(a.ReturnType, b.ReturnType),
            _ => false
        };
    }
    private bool UnifyList(ImmutableArray<TypeSymbol> a, ImmutableArray<TypeSymbol> b) => a.Length == b.Length && a.Zip(b).All(p => Unify(p.First, p.Second));
    private bool BindInference(InferenceTypeSymbol inference, TypeSymbol value)
    {
        if (ReferenceEquals(inference, value)) return true;
        if (value is InferenceTypeSymbol other)
        {
            if (inference.Constraint != InferenceKind.Any && other.Constraint != InferenceKind.Any && inference.Constraint != other.Constraint) return false;
            if (other.Constraint == InferenceKind.Any) other.Constraint = inference.Constraint;
            other.HasNeverConstraint |= inference.HasNeverConstraint;
            other.DefaultType ??= inference.DefaultType;
        }
        else if (inference.Constraint == InferenceKind.Integer && value.Kind != TypeKind.Integer || inference.Constraint == InferenceKind.Float && value.Kind != TypeKind.Float) return false;
        if (Contains(value, t => ReferenceEquals(t, inference))) return false;
        inference.Binding = value;
        return true;
    }
    private static bool ContainsParameter(TypeSymbol type, Func<TypeParameterSymbol, bool> predicate) => Contains(type, t => t is ParameterTypeSymbol p && predicate(p.Parameter));
    private static bool Contains(TypeSymbol type, Func<TypeSymbol, bool> predicate)
    {
        type = type.Resolve();
        if (predicate(type)) return true;
        return type switch
        {
            NamedTypeSymbol n => n.TypeArguments.Any(t => Contains(t, predicate)),
            TupleTypeSymbol t => t.Elements.Any(e => Contains(e, predicate)),
            SequenceTypeSymbol s => Contains(s.ElementType, predicate),
            IndirectTypeSymbol r => Contains(r.ElementType, predicate),
            FunctionTypeSymbol f => Contains(f.ReturnType, predicate) || f.ParameterTypes.Any(t => Contains(t, predicate)),
            _ => false
        };
    }
    private void CheckLayouts()
    {
        var complete = new Dictionary<AggregateSymbol, List<NamedTypeSymbol>>();
        var depths = new Dictionary<TypeSymbol, int>();
        int Depth(TypeSymbol type)
        {
            if (depths.TryGetValue(type, out int depth)) return depth;
            IEnumerable<TypeSymbol> children = type switch { NamedTypeSymbol n => n.TypeArguments, TupleTypeSymbol t => t.Elements, SequenceTypeSymbol s => new[] { s.ElementType }, IndirectTypeSymbol r => new[] { r.ElementType }, _ => [] };
            depth = 1 + children.Select(Depth).DefaultIfEmpty().Max();
            depths[type] = depth;
            return depth;
        }
        bool Visit(TypeSymbol type, List<NamedTypeSymbol> active)
        {
            type = type.Resolve();
            if (type is NamedTypeSymbol named)
            {
                if (complete.TryGetValue(named.Definition, out List<NamedTypeSymbol>? known) && known.Any(n => TypeSystem.Same(n, named))) return true;
                if (active.Any(a => ReferenceEquals(a.Definition, named.Definition) && Depth(named) >= Depth(a)) || active.Count >= 128) return false;
                active.Add(named);
                bool finite = named.Definition.Fields.Concat(named.Definition.Variants.SelectMany(v => v.Fields)).All(f => Visit(Types.FieldType(f, named), active));
                active.RemoveAt(active.Count - 1);
                if (finite)
                {
                    if (!complete.TryGetValue(named.Definition, out known)) complete.Add(named.Definition, known = new());
                    known.Add(named);
                }
                return finite;
            }
            return type switch
            {
                TupleTypeSymbol tuple => tuple.Elements.All(t => Visit(t, active)),
                SequenceTypeSymbol { Kind: TypeKind.Array } array => Visit(array.ElementType, active),
                _ => true
            };
        }
        foreach (AggregateSymbol type in _declarations.OfType<AggregateSymbol>())
            if (!Visit(type.Type, new List<NamedTypeSymbol>())) Error(type.Scope.Tree, type.Declaration ?? type.Scope.Tree.Root, $"'{type.Name}' has infinite size or exceeds the layout nesting limit; introduce indirection.");
    }

    private static IEnumerable<LifetimeSymbol> Lifetimes(TypeSymbol type) => type.Resolve() switch
    {
        IndirectTypeSymbol { Kind: TypeKind.Reference, Lifetime: LifetimeSymbol lifetime } r => Lifetimes(r.ElementType).Prepend(lifetime),
        TupleTypeSymbol t => t.Elements.SelectMany(Lifetimes),
        SequenceTypeSymbol s => Lifetimes(s.ElementType),
        NamedTypeSymbol n => n.TypeArguments.SelectMany(Lifetimes),
        _ => []
    };
    private static TypeSymbol ReplaceElidedLifetimes(TypeSymbol type, LifetimeSymbol lifetime) => type.Resolve() switch
    {
        IndirectTypeSymbol r => new IndirectTypeSymbol(r.Kind, ReplaceElidedLifetimes(r.ElementType, lifetime), r.IsMutable, r.Lifetime is { IsElided: true } ? lifetime : r.Lifetime),
        TupleTypeSymbol t => new TupleTypeSymbol(t.Elements.Select(e => ReplaceElidedLifetimes(e, lifetime)).ToImmutableArray()),
        SequenceTypeSymbol s => new SequenceTypeSymbol(s.Kind, ReplaceElidedLifetimes(s.ElementType, lifetime), s.Length, s.RangeForm),
        NamedTypeSymbol n => new NamedTypeSymbol(n.Definition, n.TypeArguments.Select(t => ReplaceElidedLifetimes(t, lifetime)).ToImmutableArray()),
        _ => type
    };
    internal bool IsSized(TypeSymbol type, HashSet<AggregateSymbol>? active = null)
    {
        type = type.Resolve();
        if (type.Kind is TypeKind.String or TypeKind.Slice) return false;
        if (type is TupleTypeSymbol tuple) return tuple.Elements.All(t => IsSized(t, active));
        if (type is SequenceTypeSymbol { Kind: TypeKind.Array } array) return IsSized(array.ElementType, active);
        if (type is NamedTypeSymbol named)
        {
            active ??= new HashSet<AggregateSymbol>();
            if (!active.Add(named.Definition)) return true;
            bool result = named.Definition.Fields.Concat(named.Definition.Variants.SelectMany(v => v.Fields)).All(f => IsSized(Types.FieldType(f, named), active));
            active.Remove(named.Definition);
            return result;
        }
        return true;
    }
    private void CheckSizedTypes()
    {
        foreach (Symbol symbol in _declarations)
        {
            if (symbol.SyntaxTree is not SyntaxTree tree || symbol.Declaration is not SyntaxNode syntax) continue;
            bool unsizedTail = symbol is FieldSymbol { ContainingSymbol: AggregateSymbol { TypeKind: TypeKind.Struct } owner } tail && ReferenceEquals(owner.Fields.LastOrDefault(), tail);
            if (symbol is VariableSymbol variable && !unsizedTail && !IsSized(variable.Type)) Error(tree, syntax, $"'{variable.Type}' cannot be used by value in this declaration.");
            if (symbol is FieldSymbol field && Lifetimes(field.Type).Any(l => l.IsElided)) Error(tree, syntax, "References in fields require an explicit lifetime.");
            if (symbol is FunctionSymbol function && !IsSized(function.ReturnType)) Error(tree, syntax, $"Function return type '{function.ReturnType}' must be sized.");
        }
    }
}

internal sealed class BodyBinder
{
    private readonly Binder _binder;
    private readonly SyntaxTree _tree;
    private readonly FunctionSymbol? _function;
    private BindingScope _scope;
    private TypeSystem Types => _binder.Types;
    private SemanticData Data => _binder.Data(_tree);
    private HashSet<VariableSymbol> _initialized = new();
    private HashSet<VariableSymbol> _assigned = new();
    private bool _reachable = true;
    private bool _unsafe;
    private int _depth;
    private readonly List<LoopContext> _loops = new();
    private sealed class LoopContext(LabelSymbol label, TypeSymbol type, bool values, HashSet<VariableSymbol> outer)
    {
        internal readonly LabelSymbol Label = label;
        internal readonly TypeSymbol Type = type;
        internal readonly bool AllowsValue = values;
        internal readonly HashSet<VariableSymbol> Outer = outer;
        internal readonly List<FlowState> Breaks = new();
        internal readonly List<FlowState> Continues = new();
    }
    private sealed record FlowState(HashSet<VariableSymbol> Initialized, HashSet<VariableSymbol> Assigned, bool Reachable);
    private FlowState Save() => new(new(_initialized), new(_assigned), _reachable);
    private void Restore(FlowState state) { _initialized = new(state.Initialized); _assigned = new(state.Assigned); _reachable = state.Reachable; }
    private void Merge(IEnumerable<FlowState> states)
    {
        FlowState[] reachable = states.Where(s => s.Reachable).ToArray();
        if (reachable.Length == 0) { _reachable = false; return; }
        Restore(reachable[0]);
        foreach (FlowState state in reachable.Skip(1)) { _initialized.IntersectWith(state.Initialized); _assigned.UnionWith(state.Assigned); }
    }
    internal BodyBinder(Binder binder, BindingScope scope, FunctionSymbol? function, SyntaxTree? tree = null)
    { _binder = binder; _scope = scope; _function = function; _tree = tree ?? scope.Tree; }
    private void Error(SyntaxNode syntax, string message) => _binder.Error(_tree, syntax, message);
    private BoundErrorExpression Bad(SyntaxNode syntax, string? message = null, IEnumerable<BoundNode>? children = null)
    {
        if (message is not null) Error(syntax, message);
        return new BoundErrorExpression(syntax, Types.Error, children);
    }
    private BindingScope Enter(SyntaxNode syntax)
    {
        BindingScope previous = _scope;
        _scope = new BindingScope(previous, previous.Owner, _tree, syntax.Span);
        _binder.AddScope(_scope);
        return previous;
    }
    private T Record<T>(SyntaxNode syntax, T node) where T : BoundNode
    {
        Data.BoundNodes[syntax] = node;
        if (node is BoundExpression expression) _binder.RecordType(_tree, syntax, expression.Type);
        if (node is BoundCallExpression { Target: BoundFunctionReference function }) _binder.RecordSymbol(_tree, syntax, function.Function);
        return node;
    }

    internal BoundFunction BindFunction()
    {
        FunctionSymbol function = _function ?? throw new InvalidOperationException("No function to bind.");
        FunctionItemSyntax syntax = function.Declaration;
        int errors = Data.Diagnostics.Count;
        var patterns = ImmutableArray.CreateBuilder<BoundPattern>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (ParameterSymbol parameter in function.Parameters)
        {
            ParameterSyntax declaration = parameter.Declaration;
            if (parameter.IsSelf)
            {
                AddVariable(parameter, declaration.Span.Start, names, declaration);
                patterns.Add(new BoundPattern(declaration, parameter.Type, BoundPatternKind.Binding, variable: parameter, irrefutable: true));
            }
            else
            {
                BoundPattern pattern = Pattern(declaration.Pattern, parameter.Type, declaration, declaration.Span.Start, names, parameter);
                if (!pattern.IsIrrefutable) Error(declaration, "Function parameters require irrefutable patterns.");
                patterns.Add(pattern);
            }
        }
        _unsafe = function.IsUnsafe;
        BoundBlockExpression? body = syntax.Body is null ? null : Block(syntax.Body, function.ReturnType);
        if (body is null && !function.IsForeign) Error(syntax, "A function declaration requires a body outside an extern block.");
        var bound = new BoundFunction(syntax, function, patterns.ToImmutable(), body) { OwnErrors = Data.Diagnostics.Count > errors };
        return Record(syntax, bound);
    }

    internal BoundExpression Expression(ExpressionSyntax? syntax, TypeSymbol? expected = null, bool place = false)
    {
        if (syntax is null) return Bad(_scope.Owner.Declaration ?? _tree.Root);
        if (++_depth > 256) { _depth--; return Bad(syntax, "Expression nesting exceeds the semantic binding limit."); }
        int errors = Data.Diagnostics.Count;
        BoundExpression result;
        try
        {
            result = syntax switch
            {
                LiteralExpressionSyntax literal => Literal(literal.Literal, literal),
                PathExpressionSyntax path => Path(path, place),
                ParenthesizedExpressionSyntax p => Expression(p.Expression, expected, place),
                TupleExpressionSyntax tuple => Tuple(tuple, expected),
                ArrayExpressionSyntax array => Array(array, expected),
                RepeatArrayExpressionSyntax array => RepeatArray(array, expected),
                UnaryExpressionSyntax unary => Unary(unary),
                BinaryExpressionSyntax binary => Binary(binary),
                AssignmentExpressionSyntax assignment => Assignment(assignment),
                CastExpressionSyntax cast => Cast(cast),
                CallExpressionSyntax call => Call(call, expected),
                MemberAccessExpressionSyntax member => Member(member, place),
                IndexExpressionSyntax index => Index(index, place),
                StructExpressionSyntax construction => Construct(construction, expected),
                BlockExpressionSyntax block => Block(block, expected),
                IfExpressionSyntax conditional => If(conditional, expected),
                LoopExpressionSyntax loop => Loop(loop, expected),
                ControlFlowExpressionSyntax control => Control(control),
                MatchExpressionSyntax match => Match(match, expected),
                RangeExpressionSyntax range => Range(range),
                AttributedExpressionSyntax attributed => Attributed(attributed, expected, place),
                MissingExpressionSyntax => Bad(syntax),
                _ => Unsupported(syntax)
            };
        }
        finally { _depth--; }
        if (Data.Diagnostics.Count > errors) result.OwnErrors = true;
        Record(syntax, result);
        if (result.Type.Kind == TypeKind.Never) _reachable = false;
        if (expected is not null) result = Coerce(result, expected, syntax);
        EvaluateConstant(result);
        _binder.CheckLater(() => EvaluateConstant(result));
        return result;
    }
    private BoundExpression Unsupported(SyntaxNode syntax)
    { _binder.Unsupported(_tree, syntax, syntax.Kind.ToString()); return Bad(syntax); }
    private BoundExpression Attributed(AttributedExpressionSyntax syntax, TypeSymbol? expected, bool place)
    { _binder.CheckAttributes(syntax.Attributes, _tree); return Expression(syntax.Expression, expected, place); }
    internal BoundExpression Coerce(BoundExpression expression, TypeSymbol target, SyntaxNode syntax)
    {
        TypeSymbol source = expression.Type;
        target = target.Resolve();
        ConversionKind kind;
        if (source.Kind == TypeKind.Never)
        {
            kind = ConversionKind.Never;
            if (target is InferenceTypeSymbol inference) inference.HasNeverConstraint = true;
        }
        else if (_binder.Unify(source, target)) kind = ConversionKind.Identity;
        else if (source is IndirectTypeSymbol a && target is IndirectTypeSymbol b && (a.IsMutable || !b.IsMutable))
        {
            if (a.Kind == TypeKind.Reference && b.Kind == TypeKind.Reference && a.ElementType.Resolve() is SequenceTypeSymbol { Kind: TypeKind.Array } array &&
                b.ElementType.Resolve() is SequenceTypeSymbol { Kind: TypeKind.Slice } slice && _binder.Unify(array.ElementType, slice.ElementType))
                kind = ConversionKind.ArrayToSliceReference;
            else if (_binder.Unify(a.ElementType, b.ElementType))
                kind = (a.Kind, b.Kind) switch
                {
                    (TypeKind.Reference, TypeKind.Reference) => ConversionKind.MutableToSharedReference,
                    (TypeKind.Reference, TypeKind.Pointer) => ConversionKind.ReferenceToPointer,
                    (TypeKind.Pointer, TypeKind.Pointer) => ConversionKind.MutableToConstPointer,
                    _ => ConversionKind.None
                };
            else kind = ConversionKind.None;
        }
        else kind = ConversionKind.None;
        Data.Types[syntax] = new TypeInfo(source, target);
        Data.Conversions[syntax] = new Conversion(kind);
        if (kind == ConversionKind.None)
        {
            Error(syntax, $"Expected '{target}', found '{source}'.");
            var invalid = Bad(syntax, children: new[] { expression });
            Data.BoundNodes[syntax] = invalid;
            return invalid;
        }
        if (kind == ConversionKind.Identity) return expression;
        var converted = new BoundConversionExpression(syntax, target, kind, expression) { ConstantValue = expression.ConstantValue };
        Data.BoundNodes[syntax] = converted;
        return converted;
    }
    private BoundExpression Literal(SyntaxToken token, SyntaxNode syntax, bool negative = false, bool checkRange = true)
    {
        TypeSymbol type;
        object? value = token.Value;
        string suffix = token.GetSuffix(_tree.Text).ToString();
        switch (token.Kind)
        {
            case SyntaxKind.IntegerLiteralToken:
                type = suffix.Length == 0 ? _binder.Fresh(syntax, InferenceKind.Integer)
                    : Types.Primitives.TryGetValue(suffix, out PrimitiveTypeSymbol? integer) && integer.IsNumeric ? integer : Types.Error;
                value = value switch { ulong u => new BigInteger(u), UInt128 u => BigInteger.Parse(u.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture), float or double => value, _ => null };
                if (negative && value is BigInteger signed) value = -signed;
                else if (negative) value = value switch { float f => -f, double d => -d, _ => value };
                if (type.Kind == TypeKind.Float && value is BigInteger floating) value = type is PrimitiveTypeSymbol { BitWidth: 32 } ? (object)(float)floating : (double)floating;
                break;
            case SyntaxKind.FloatingLiteralToken:
                type = suffix.Length == 0 ? _binder.Fresh(syntax, InferenceKind.Float)
                    : Types.Primitives.TryGetValue(suffix, out PrimitiveTypeSymbol? floatingType) && floatingType.Kind == TypeKind.Float ? floatingType : Types.Error;
                if (negative) value = value switch { float f => -f, double d => -d, _ => value };
                break;
            case SyntaxKind.TrueKeyword: type = Types.Bool; value = true; break;
            case SyntaxKind.FalseKeyword: type = Types.Bool; value = false; break;
            case SyntaxKind.CharacterLiteralToken: type = Types.Primitives["char"]; break;
            case SyntaxKind.ByteLiteralToken: type = Types.Primitives["u8"]; value = value is byte b ? new BigInteger(b) : value; break;
            case SyntaxKind.StringLiteralToken:
            case SyntaxKind.RawStringLiteralToken:
                type = new IndirectTypeSymbol(TypeKind.Reference, Types.Primitives["str"], false, Types.StaticLifetime); break;
            case SyntaxKind.ByteStringLiteralToken:
            case SyntaxKind.RawByteStringLiteralToken:
                type = new IndirectTypeSymbol(TypeKind.Reference, new SequenceTypeSymbol(TypeKind.Array, Types.Primitives["u8"], (ulong)(value as byte[] ?? []).Length), false, Types.StaticLifetime); break;
            default: return Unsupported(syntax);
        }
        if (type.IsError) Error(syntax, $"Invalid numeric suffix '{suffix}'.");
        if (value is null) return Bad(syntax, "Invalid literal value.");
        var result = new BoundLiteralExpression(syntax, type, value);
        if (checkRange) CheckInteger(result);
        return result;
    }
    private void CheckInteger(BoundExpression expression)
    {
        _binder.CheckLater(() =>
        {
            EvaluateConstant(expression);
            if (expression.Type is not PrimitiveTypeSymbol { Kind: TypeKind.Integer } type || expression.ConstantValue.Value is not BigInteger value) return;
            BigInteger min = type.IsSigned ? -(BigInteger.One << (type.BitWidth - 1)) : BigInteger.Zero;
            BigInteger max = (BigInteger.One << (type.BitWidth - (type.IsSigned ? 1 : 0))) - 1;
            if (value < min || value > max) { Error(expression.Syntax, $"Constant value '{value}' overflows '{type}'."); expression.OwnErrors = true; }
        });
    }

    private BoundExpression Path(PathExpressionSyntax syntax, bool place)
    {
        Symbol? symbol = _binder.ResolvePath(syntax.Path, _scope, _tree, SymbolNamespace.Value, position: syntax.Span.Start);
        if (symbol is null) return Bad(syntax);
        _binder.RecordSymbol(_tree, syntax, symbol);
        return Value(symbol, syntax, syntax.Path, place);
    }
    private BoundExpression Value(Symbol symbol, SyntaxNode syntax, PathSyntax? path = null, bool place = false)
    {
        switch (symbol)
        {
            case VariableSymbol variable:
            {
                if (!place && _reachable && variable is LocalSymbol or ParameterSymbol && !_initialized.Contains(variable))
                    Error(syntax, $"'{variable.Name}' may be uninitialized.");
                if (variable is GlobalSymbol global)
                {
                    _binder.BindGlobal(global);
                    if (global.Kind == SymbolKind.Static && global.IsMutable && !_unsafe) Error(syntax, "Access to a mutable static requires an unsafe block.");
                }
                var bound = new BoundVariableExpression(syntax, variable);
                if (variable is GlobalSymbol { Kind: SymbolKind.Constant } constant) bound.ConstantValue = constant.ConstantValue;
                return bound;
            }
            case FunctionSymbol function: return FunctionReference(function, syntax, path?.Segments.LastOrDefault()?.Arguments);
            case AggregateSymbol { Shape: AggregateShape.Unit } aggregate:
                return new BoundConstructionExpression(syntax, Instantiate(aggregate, path, syntax), null, []);
            case VariantSymbol { Shape: AggregateShape.Unit } variant:
                return new BoundConstructionExpression(syntax, Instantiate(variant.Enum, path, syntax), variant, []);
            default: return Bad(syntax, $"'{symbol.Name}' cannot be used as a value here.");
        }
    }
    private NamedTypeSymbol Instantiate(AggregateSymbol aggregate, PathSyntax? path, SyntaxNode syntax)
    {
        if (path?.Segments.Count(s => s.Arguments is not null) > 1) Error(syntax, "Generic arguments may only be supplied once for a constructor.");
        GenericArgumentListSyntax? arguments = path?.Segments.FirstOrDefault(s => Binder.Name(s.Identifier) == aggregate.Name)?.Arguments;
        arguments ??= path?.Segments.LastOrDefault()?.Arguments;
        return new NamedTypeSymbol(aggregate, _binder.BindArguments(aggregate.TypeParameters, arguments, syntax, _scope, _tree, true));
    }
    private BoundFunctionReference FunctionReference(FunctionSymbol function, SyntaxNode syntax, GenericArgumentListSyntax? supplied)
    {
        ImmutableArray<TypeSymbol> arguments = _binder.BindArguments(function.TypeParameters, supplied, syntax, _scope, _tree, true);
        var substitution = function.TypeParameters.Zip(arguments).ToDictionary(p => p.First, p => p.Second);
        var type = new FunctionTypeSymbol(function.Parameters.Select(p => Types.Substitute(p.Type, substitution)).ToImmutableArray(),
            Types.Substitute(function.ReturnType, substitution), function.IsUnsafe, function.Abi);
        _binder.RecordSymbol(_tree, syntax, function);
        return new BoundFunctionReference(syntax, function, type, arguments);
    }
    private BoundExpression Tuple(TupleExpressionSyntax syntax, TypeSymbol? expected)
    {
        ImmutableArray<TypeSymbol> contextual = expected?.Resolve() is TupleTypeSymbol tuple ? tuple.Elements : [];
        var elements = syntax.Elements.Select((e, i) => Expression(e, i < contextual.Length ? contextual[i] : null)).ToImmutableArray();
        return new BoundTupleExpression(syntax, new TupleTypeSymbol(elements.Select(e => e.Type).ToImmutableArray()), elements);
    }
    private BoundExpression Array(ArrayExpressionSyntax syntax, TypeSymbol? expected)
    {
        TypeSymbol element = expected?.Resolve() is SequenceTypeSymbol { Kind: TypeKind.Array } array ? array.ElementType : _binder.Fresh(syntax);
        ImmutableArray<BoundExpression> elements = syntax.Elements.Select(e => Expression(e, element)).ToImmutableArray();
        return new BoundArrayExpression(syntax, new SequenceTypeSymbol(TypeKind.Array, element, (ulong)elements.Length), elements);
    }
    private BoundExpression RepeatArray(RepeatArrayExpressionSyntax syntax, TypeSymbol? expected)
    {
        TypeSymbol? contextual = expected?.Resolve() is SequenceTypeSymbol { Kind: TypeKind.Array } array ? array.ElementType : null;
        BoundExpression element = Expression(syntax.Element, contextual);
        BoundExpression count = Expression(syntax.Length, Types.Usize);
        ulong? length = count.ConstantValue.Value is BigInteger value && value >= 0 && value <= ulong.MaxValue ? (ulong)value : null;
        if (length is null) Error(syntax, "Array repeat count must be a constant usize.");
        _binder.CheckLater(() =>
        {
            if (length > 1 && element.Type.Kind is not (TypeKind.Boolean or TypeKind.Integer or TypeKind.Float or TypeKind.Character or TypeKind.Pointer or TypeKind.Unit) &&
                element.Type is not IndirectTypeSymbol { Kind: TypeKind.Reference, IsMutable: false } && !element.HasErrors)
                _binder.Unsupported(_tree, syntax, "Copy checking for this array repeat element");
        });
        return new BoundArrayExpression(syntax, new SequenceTypeSymbol(TypeKind.Array, element.Type, length), [element], count);
    }

    private BoundExpression Unary(UnaryExpressionSyntax syntax)
    {
        SyntaxKind op = syntax.OperatorToken.Kind;
        if (op == SyntaxKind.MinusToken && syntax.Operand is LiteralExpressionSyntax literal)
        {
            BoundExpression value = Literal(literal.Literal, syntax, true, false);
            var operand = new BoundLiteralExpression(literal, value.Type, value.ConstantValue.Value switch { BigInteger n => -n, float f => -f, double d => -d, var other => other });
            Record(literal, operand);
            var result = new BoundUnaryExpression(syntax, value.Type, op, operand) { ConstantValue = value.ConstantValue };
            CheckUnary(result); CheckInteger(result);
            return result;
        }
        BoundExpression bound = Expression(syntax.Operand);
        if (op is SyntaxKind.AmpersandToken or SyntaxKind.AmpersandAmpersandToken)
        {
            bool mutable = Binder.Has(syntax, SyntaxKind.MutKeyword);
            bool raw = syntax.ChildTokens().Any(t => t.ValueText == "raw");
            if (mutable && bound.ValueCategory == ValueCategory.Place) Error(syntax, "Cannot mutably borrow an immutable place.");
            TypeSymbol type = new IndirectTypeSymbol(raw ? TypeKind.Pointer : TypeKind.Reference, bound.Type, mutable);
            if (op == SyntaxKind.AmpersandAmpersandToken) type = new IndirectTypeSymbol(TypeKind.Reference, type, false);
            return new BoundUnaryExpression(syntax, type, op, bound, rawBorrow: raw);
        }
        if (op == SyntaxKind.StarToken)
        {
            if (bound.Type is not IndirectTypeSymbol reference) return Bad(syntax, $"Cannot dereference '{bound.Type}'.", [bound]);
            if (reference.Kind == TypeKind.Pointer && !_unsafe) Error(syntax, "Dereferencing a raw pointer requires an unsafe block.");
            return new BoundUnaryExpression(syntax, reference.ElementType, op, bound, reference.IsMutable ? ValueCategory.MutablePlace : ValueCategory.Place);
        }
        var unary = new BoundUnaryExpression(syntax, bound.Type, op, bound);
        unary.ConstantValue = bound.ConstantValue.Value switch
        {
            BigInteger n when op == SyntaxKind.MinusToken => ConstantValue.From(-n),
            BigInteger n when op == SyntaxKind.BangToken => default,
            float f when op == SyntaxKind.MinusToken => ConstantValue.From(-f),
            double d when op == SyntaxKind.MinusToken => ConstantValue.From(-d),
            bool b when op == SyntaxKind.BangToken => ConstantValue.From(!b),
            _ => default
        };
        CheckUnary(unary); CheckInteger(unary);
        return unary;
    }
    private void CheckUnary(BoundUnaryExpression expression)
    {
        _binder.CheckLater(() =>
        {
            TypeSymbol type = expression.Operand.Type;
            if (type.IsError) return;
            bool valid = expression.Operator switch
            {
                SyntaxKind.MinusToken => type.Kind == TypeKind.Float || type is PrimitiveTypeSymbol { Kind: TypeKind.Integer, IsSigned: true },
                SyntaxKind.BangToken => type.Kind is TypeKind.Integer or TypeKind.Boolean,
                _ => false
            };
            if (!valid) { Error(expression.Syntax, $"Unary operator is not defined for '{type}'."); expression.OwnErrors = true; }
            if (expression.Operator == SyntaxKind.BangToken && type is PrimitiveTypeSymbol { Kind: TypeKind.Integer } integer && expression.Operand.ConstantValue.Value is BigInteger n)
                expression.ConstantValue = ConstantValue.From(integer.IsSigned ? ~n : ((BigInteger.One << integer.BitWidth) - 1) ^ n);
        });
    }
    private static bool Comparison(SyntaxKind op) => op is SyntaxKind.EqualsEqualsToken or SyntaxKind.BangEqualsToken or SyntaxKind.LessThanToken or SyntaxKind.LessThanEqualsToken or SyntaxKind.GreaterThanToken or SyntaxKind.GreaterThanEqualsToken;
    private BoundExpression Binary(BinaryExpressionSyntax syntax)
    {
        SyntaxKind op = syntax.OperatorToken.Kind;
        bool logical = op is SyntaxKind.AmpersandAmpersandToken or SyntaxKind.PipePipeToken;
        BoundExpression left = Expression(syntax.Left, logical ? Types.Bool : null);
        FlowState beforeRight = Save();
        BoundExpression right = Expression(syntax.Right, logical ? Types.Bool : null);
        if (logical) Merge(new[] { beforeRight, Save() });
        return BinaryOperation(syntax, op, left, right);
    }
    private BoundExpression BinaryOperation(SyntaxNode syntax, SyntaxKind op, BoundExpression left, BoundExpression right)
    {
        if (left.Type.Kind == TypeKind.Never && right.Type.Kind != TypeKind.Never) left = Coerce(left, right.Type, left.Syntax);
        if (right.Type.Kind == TypeKind.Never && left.Type.Kind != TypeKind.Never) right = Coerce(right, left.Type, right.Syntax);
        bool shift = op is SyntaxKind.LessThanLessThanToken or SyntaxKind.GreaterThanGreaterThanToken;
        bool logical = op is SyntaxKind.AmpersandAmpersandToken or SyntaxKind.PipePipeToken;
        if (!shift && !_binder.Unify(left.Type, right.Type)) Error(syntax, $"Operator operands have incompatible types '{left.Type}' and '{right.Type}'.");
        var result = new BoundBinaryExpression(syntax, Comparison(op) || logical ? Types.Bool : left.Type, op, left, right);
        result.ConstantValue = FoldBinary(op, left.ConstantValue, right.ConstantValue, syntax);
        _binder.CheckLater(() =>
        {
            TypeSymbol type = left.Type;
            if (type.IsError || right.Type.IsError) return;
            bool valid = op switch
            {
                SyntaxKind.AmpersandAmpersandToken or SyntaxKind.PipePipeToken => type.Kind == TypeKind.Boolean,
                SyntaxKind.AmpersandToken or SyntaxKind.PipeToken or SyntaxKind.CaretToken => type.Kind is TypeKind.Boolean or TypeKind.Integer,
                SyntaxKind.LessThanLessThanToken or SyntaxKind.GreaterThanGreaterThanToken => type.IsInteger && right.Type.IsInteger,
                _ when Comparison(op) => type.Kind is TypeKind.Boolean or TypeKind.Character or TypeKind.Integer or TypeKind.Float or TypeKind.Pointer,
                _ => type.IsNumeric
            };
            if (!valid) { Error(syntax, $"Operator is not defined for '{type}' and '{right.Type}' without trait resolution."); result.OwnErrors = true; }
            if (shift && type is PrimitiveTypeSymbol integer && right.ConstantValue.Value is BigInteger count && (count < 0 || count >= integer.BitWidth))
            { Error(syntax, "Shift amount is outside the operand width."); result.OwnErrors = true; }
            if (op is SyntaxKind.SlashToken or SyntaxKind.PercentToken && right.ConstantValue.Value is BigInteger divisor)
            {
                if (divisor.IsZero) { Error(syntax, "Division by zero in a constant expression."); result.OwnErrors = true; }
                else if (divisor == -1 && type is PrimitiveTypeSymbol { IsSigned: true } signed && left.ConstantValue.Value is BigInteger dividend && dividend == -(BigInteger.One << (signed.BitWidth - 1)))
                { Error(syntax, $"Constant division overflows '{type}'."); result.OwnErrors = true; }
            }
        });
        CheckInteger(result);
        return result;
    }
    private ConstantValue FoldBinary(SyntaxKind op, ConstantValue left, ConstantValue right, SyntaxNode syntax)
    {
        if (!left.HasValue || !right.HasValue) return default;
        if (left.Value is BigInteger a && right.Value is BigInteger b)
        {
            if (op is SyntaxKind.SlashToken or SyntaxKind.PercentToken && b.IsZero) return default;
            object? value = op switch
            {
                SyntaxKind.PlusToken => a + b, SyntaxKind.MinusToken => a - b, SyntaxKind.StarToken => a * b,
                SyntaxKind.SlashToken => a / b, SyntaxKind.PercentToken => a % b,
                SyntaxKind.AmpersandToken => a & b, SyntaxKind.PipeToken => a | b, SyntaxKind.CaretToken => a ^ b,
                SyntaxKind.LessThanLessThanToken when b >= 0 && b < 128 => a << (int)b,
                SyntaxKind.GreaterThanGreaterThanToken when b >= 0 && b < 128 => a >> (int)b,
                SyntaxKind.EqualsEqualsToken => a == b, SyntaxKind.BangEqualsToken => a != b,
                SyntaxKind.LessThanToken => a < b, SyntaxKind.LessThanEqualsToken => a <= b,
                SyntaxKind.GreaterThanToken => a > b, SyntaxKind.GreaterThanEqualsToken => a >= b, _ => null
            };
            return value is null ? default : ConstantValue.From(value);
        }
        if (left.Value is bool x && right.Value is bool y)
        {
            object? value = op switch
            {
                SyntaxKind.AmpersandAmpersandToken or SyntaxKind.AmpersandToken => x & y,
                SyntaxKind.PipePipeToken or SyntaxKind.PipeToken => x | y, SyntaxKind.CaretToken => x ^ y,
                SyntaxKind.EqualsEqualsToken => x == y, SyntaxKind.BangEqualsToken => x != y,
                SyntaxKind.LessThanToken => !x && y, SyntaxKind.LessThanEqualsToken => !x || y,
                SyntaxKind.GreaterThanToken => x && !y, SyntaxKind.GreaterThanEqualsToken => x || !y, _ => null
            };
            return value is null ? default : ConstantValue.From(value);
        }
        return default;
    }

    private void EvaluateConstant(BoundExpression expression)
    {
        switch (expression)
        {
            case BoundLiteralExpression literal when literal.Type is PrimitiveTypeSymbol { Name: "f32" } && literal.ConstantValue.Value is double d:
                literal.ConstantValue = ConstantValue.From((float)d);
                break;
            case BoundUnaryExpression unary:
                EvaluateConstant(unary.Operand);
                if (unary.Operator == SyntaxKind.BangToken && unary.Type is PrimitiveTypeSymbol { Kind: TypeKind.Integer } integer && unary.Operand.ConstantValue.Value is BigInteger bits)
                    unary.ConstantValue = ConstantValue.From(integer.IsSigned ? ~bits : ((BigInteger.One << integer.BitWidth) - 1) ^ bits);
                break;
            case BoundBinaryExpression binary:
                EvaluateConstant(binary.Left); EvaluateConstant(binary.Right);
                binary.ConstantValue = FoldBinary(binary.Operator, binary.Left.ConstantValue, binary.Right.ConstantValue, binary.Syntax);
                if (binary.Operator is SyntaxKind.LessThanLessThanToken or SyntaxKind.GreaterThanGreaterThanToken && binary.Type is PrimitiveTypeSymbol { Kind: TypeKind.Integer } shifted && binary.ConstantValue.Value is BigInteger value)
                    binary.ConstantValue = ConstantValue.From(Truncate(value, shifted));
                break;
            case BoundConversionExpression conversion:
                EvaluateConstant(conversion.Operand);
                if (conversion.Conversion == ConversionKind.NumericCast && conversion.Type is PrimitiveTypeSymbol { Kind: TypeKind.Integer } destination && conversion.Operand.ConstantValue.Value is BigInteger source)
                    conversion.ConstantValue = ConstantValue.From(Truncate(source, destination));
                else if (conversion.Conversion is ConversionKind.Identity or ConversionKind.Never) conversion.ConstantValue = conversion.Operand.ConstantValue;
                break;
            case BoundVariableExpression { Variable: GlobalSymbol { Kind: SymbolKind.Constant } global } variable:
                variable.ConstantValue = global.ConstantValue;
                break;
        }
    }
    private static BigInteger Truncate(BigInteger value, PrimitiveTypeSymbol type)
    {
        BigInteger modulus = BigInteger.One << type.BitWidth;
        value = ((value % modulus) + modulus) % modulus;
        if (type.IsSigned && value >= modulus / 2) value -= modulus;
        return value;
    }
    private BoundExpression Assignment(AssignmentExpressionSyntax syntax)
    {
        bool compound = syntax.OperatorToken.Kind != SyntaxKind.EqualsToken;
        BoundExpression target = Expression(syntax.Left, place: !compound);
        BoundExpression value = Expression(syntax.Right, target.Type);
        if (target is BoundVariableExpression { Variable: LocalSymbol or ParameterSymbol } variable)
        {
            if (!variable.Variable.IsMutable && _assigned.Contains(variable.Variable))
                Error(syntax, $"Cannot assign to immutable '{variable.Variable.Name}'.");
            _initialized.Add(variable.Variable); _assigned.Add(variable.Variable);
        }
        else if (target.ValueCategory != ValueCategory.MutablePlace) Error(syntax, "Assignment requires a mutable place.");
        if (compound)
        {
            SyntaxKind op = syntax.OperatorToken.Kind switch
            {
                SyntaxKind.PlusEqualsToken => SyntaxKind.PlusToken, SyntaxKind.MinusEqualsToken => SyntaxKind.MinusToken,
                SyntaxKind.StarEqualsToken => SyntaxKind.StarToken, SyntaxKind.SlashEqualsToken => SyntaxKind.SlashToken,
                SyntaxKind.PercentEqualsToken => SyntaxKind.PercentToken, SyntaxKind.CaretEqualsToken => SyntaxKind.CaretToken,
                SyntaxKind.PipeEqualsToken => SyntaxKind.PipeToken, SyntaxKind.AmpersandEqualsToken => SyntaxKind.AmpersandToken,
                SyntaxKind.LessThanLessThanEqualsToken => SyntaxKind.LessThanLessThanToken, _ => SyntaxKind.GreaterThanGreaterThanToken
            };
            _ = BinaryOperation(syntax, op, target, value);
        }
        return new BoundAssignmentExpression(syntax, Types.Unit, syntax.OperatorToken.Kind, target, value);
    }
    private BoundExpression Cast(CastExpressionSyntax syntax)
    {
        BoundExpression operand = Expression(syntax.Expression);
        TypeSymbol type = _binder.BindType(syntax.Type, _scope, _tree);
        var cast = new BoundConversionExpression(syntax, type, type.Kind == TypeKind.Pointer ? ConversionKind.PointerCast : ConversionKind.NumericCast, operand);
        _binder.CheckLater(() =>
        {
            TypeSymbol source = operand.Type;
            bool valid = source.IsNumeric && type.IsNumeric || source.Kind is TypeKind.Boolean or TypeKind.Character && type.IsInteger ||
                source is PrimitiveTypeSymbol { Name: "u8" } && type.Kind == TypeKind.Character ||
                source is IndirectTypeSymbol { Kind: TypeKind.Pointer } pointer && (type is IndirectTypeSymbol { Kind: TypeKind.Pointer } destinationPointer && (_binder.IsSized(destinationPointer.ElementType) || !_binder.IsSized(pointer.ElementType)) || type.IsInteger && _binder.IsSized(pointer.ElementType)) ||
                source.IsInteger && type is IndirectTypeSymbol { Kind: TypeKind.Pointer } fromInteger && _binder.IsSized(fromInteger.ElementType) ||
                source.Kind == TypeKind.Function && (type.IsInteger || type is IndirectTypeSymbol { Kind: TypeKind.Pointer } fromFunction && _binder.IsSized(fromFunction.ElementType)) ||
                source is IndirectTypeSymbol { Kind: TypeKind.Reference } reference && type is IndirectTypeSymbol { Kind: TypeKind.Pointer } raw && (reference.IsMutable || !raw.IsMutable) && TypeSystem.Same(reference.ElementType, raw.ElementType);
            if (!valid && !source.IsError && !type.IsError) { Error(syntax, $"Invalid cast from '{source}' to '{type}'."); cast.OwnErrors = true; }
        });
        if (type is PrimitiveTypeSymbol { Kind: TypeKind.Integer } integer && operand.ConstantValue.Value is BigInteger n)
        {
            BigInteger modulus = BigInteger.One << integer.BitWidth;
            n = ((n % modulus) + modulus) % modulus;
            if (integer.IsSigned && n >= modulus / 2) n -= modulus;
            cast.ConstantValue = ConstantValue.From(n);
        }
        Data.Conversions[syntax] = new Conversion(cast.Conversion);
        return cast;
    }

    private BoundExpression Call(CallExpressionSyntax syntax, TypeSymbol? expected)
    {
        ExpressionSyntax[] arguments = (syntax.Arguments?.Arguments ?? []).ToArray();
        if (syntax.Target is PathExpressionSyntax path)
        {
            Symbol? symbol = _binder.ResolvePath(path.Path, _scope, _tree, SymbolNamespace.Value, false, path.Span.Start);
            if (symbol is AggregateSymbol { Shape: AggregateShape.Tuple } or VariantSymbol { Shape: AggregateShape.Tuple })
            {
                _binder.ResolvePath(path.Path, _scope, _tree, SymbolNamespace.Value, position: path.Span.Start);
                AggregateSymbol definition = symbol is VariantSymbol variant ? variant.Enum : (AggregateSymbol)symbol;
                NamedTypeSymbol type = Instantiate(definition, path.Path, syntax);
                if (expected is not null) _binder.Unify(type, expected);
                ImmutableArray<FieldSymbol> fields = symbol is VariantSymbol v ? v.Fields : definition.Fields;
                foreach (FieldSymbol field in fields)
                    if (!_binder.IsAccessible(field, Binder.ModuleOf(_scope))) Error(syntax, $"Constructor field '{field.Name}' is private.");
                if (arguments.Length != fields.Length) Error(syntax, $"Expected {fields.Length} constructor arguments, got {arguments.Length}.");
                var values = arguments.Select((a, i) => Expression(a, i < fields.Length ? Types.FieldType(fields[i], type) : null)).ToArray();
                _binder.RecordSymbol(_tree, path, symbol);
                return new BoundConstructionExpression(syntax, type, symbol as VariantSymbol, fields.Zip(values).Select(p => new BoundFieldInitializer(p.First, p.Second)).ToImmutableArray());
            }
        }
        BoundExpression target;
        BoundExpression? receiverArgument = null;
        if (syntax.Target is MemberAccessExpressionSyntax member)
        {
            BoundExpression receiver = AutoDeref(Expression(member.Receiver));
            if (receiver.Type is NamedTypeSymbol named && named.Definition.Methods.FirstOrDefault(f => f.Name == Binder.Name(member.Member) && f.HasSelfParameter) is FunctionSymbol method)
            {
                if (!_binder.IsAccessible(method, Binder.ModuleOf(_scope))) Error(member, $"Method '{method.Name}' is private.");
                TypeSymbol parameter = method.Parameters[0].Type;
                BoundExpression self = receiver;
                if (parameter is IndirectTypeSymbol reference)
                {
                    if (reference.IsMutable && receiver.ValueCategory == ValueCategory.Place) Error(member, "Method requires a mutable receiver.");
                    self = new BoundUnaryExpression(member.Receiver ?? member, new IndirectTypeSymbol(TypeKind.Reference, receiver.Type, reference.IsMutable), SyntaxKind.AmpersandToken, receiver);
                }
                self = Coerce(self, parameter, member.Receiver ?? member);
                receiverArgument = self;
                target = Record(member, FunctionReference(method, member, member.GenericArguments));
            }
            else
            {
                Error(member, $"No inherent method '{Binder.Name(member.Member)}' on '{receiver.Type}'.");
                target = Bad(member, children: [receiver]);
            }
        }
        else target = Expression(syntax.Target);
        if (target.Type is not FunctionTypeSymbol signature)
        {
            var invalid = arguments.Select(a => Expression(a)).Prepend(target).ToArray();
            return Bad(syntax, target.Type.IsError ? null : $"'{target.Type}' is not callable.", invalid);
        }
        if (signature.IsUnsafe && !_unsafe) Error(syntax, "Calling an unsafe function requires an unsafe block.");
        if (expected is not null) _binder.Unify(signature.ReturnType, expected);
        int offset = receiverArgument is null ? 0 : 1;
        if (arguments.Length + offset != signature.ParameterTypes.Length) Error(syntax, $"Expected {signature.ParameterTypes.Length - offset} arguments, got {arguments.Length}.");
        var boundArguments = ImmutableArray.CreateBuilder<BoundExpression>();
        if (receiverArgument is not null) boundArguments.Add(receiverArgument);
        for (int i = 0; i < arguments.Length; i++)
            boundArguments.Add(Expression(arguments[i], i + offset < signature.ParameterTypes.Length ? signature.ParameterTypes[i + offset] : null));
        return new BoundCallExpression(syntax, signature.ReturnType, target, boundArguments.ToImmutable());
    }
    private BoundExpression AutoDeref(BoundExpression expression)
    {
        while (expression.Type is IndirectTypeSymbol { Kind: TypeKind.Reference } reference)
            expression = new BoundUnaryExpression(expression.Syntax, reference.ElementType, SyntaxKind.StarToken, expression,
                reference.IsMutable ? ValueCategory.MutablePlace : ValueCategory.Place);
        return expression;
    }
    private BoundExpression Member(MemberAccessExpressionSyntax syntax, bool place)
    {
        BoundExpression receiver = AutoDeref(Expression(syntax.Receiver));
        string name = Binder.Name(syntax.Member);
        if (syntax.GenericArguments is not null) Error(syntax, "Field access cannot take generic arguments.");
        if (receiver.Type is TupleTypeSymbol tuple && int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index < tuple.Elements.Length)
            return new BoundFieldExpression(syntax, tuple.Elements[index], receiver, null, index, receiver.ValueCategory);
        if (receiver.Type is NamedTypeSymbol { Kind: TypeKind.Struct } named && named.Definition.Fields.FirstOrDefault(f => f.Name == name) is FieldSymbol field)
        {
            if (!_binder.IsAccessible(field, Binder.ModuleOf(_scope))) Error(syntax, $"Field '{name}' is private.");
            _binder.RecordSymbol(_tree, syntax, field);
            return new BoundFieldExpression(syntax, Types.FieldType(field, named), receiver, field, field.Ordinal, receiver.ValueCategory);
        }
        return Bad(syntax, receiver.Type.IsError ? null : $"No field '{name}' on '{receiver.Type}'.", [receiver]);
    }
    private BoundExpression Index(IndexExpressionSyntax syntax, bool place)
    {
        BoundExpression receiver = AutoDeref(Expression(syntax.Receiver));
        BoundExpression index = Expression(syntax.Index, Types.Usize);
        if (receiver.Type is not SequenceTypeSymbol { Kind: TypeKind.Array or TypeKind.Slice } sequence)
            return Bad(syntax, receiver.Type.IsError ? null : $"Cannot index '{receiver.Type}' without trait resolution.", [receiver, index]);
        if (sequence.Length is ulong length && index.ConstantValue.Value is BigInteger n && (n < 0 || n >= length)) Error(syntax, "Constant index is outside the array.");
        return new BoundIndexExpression(syntax, sequence.ElementType, receiver, index, receiver.ValueCategory);
    }
    private BoundExpression Construct(StructExpressionSyntax syntax, TypeSymbol? expected)
    {
        Symbol? symbol = _binder.ResolvePath(syntax.Path, _scope, _tree, SymbolNamespace.Type);
        NamedTypeSymbol? aliasType = symbol is TypeAliasSymbol ? _binder.BindPathType(syntax.Path, _scope, _tree, true) as NamedTypeSymbol : null;
        AggregateSymbol? definition = symbol switch { AggregateSymbol a => a, VariantSymbol v => v.Enum, _ => aliasType?.Definition };
        if (symbol is null || definition is null || symbol is AggregateSymbol { TypeKind: TypeKind.Enum }) return Bad(syntax, "A struct expression requires a struct or enum variant.");
        NamedTypeSymbol type = aliasType ?? Instantiate(definition, syntax.Path, syntax);
        if (expected is not null) _binder.Unify(type, expected);
        ImmutableArray<FieldSymbol> fields = symbol is VariantSymbol variant ? variant.Fields : definition.Fields;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var values = ImmutableArray.CreateBuilder<BoundFieldInitializer>();
        foreach (StructExpressionFieldSyntax fieldSyntax in syntax.Fields)
        {
            string name = Binder.Name(fieldSyntax.Identifier);
            if (!seen.Add(name)) Error(fieldSyntax, $"Field '{name}' is specified more than once.");
            FieldSymbol? field = fields.FirstOrDefault(f => f.Name == name);
            if (field is null) { Error(fieldSyntax, $"Unknown field '{name}'."); if (fieldSyntax.Value is not null) Expression(fieldSyntax.Value); continue; }
            if (!_binder.IsAccessible(field, Binder.ModuleOf(_scope))) Error(fieldSyntax, $"Field '{name}' is private.");
            TypeSymbol fieldType = Types.FieldType(field, type);
            BoundExpression value;
            if (fieldSyntax.Value is not null) value = Expression(fieldSyntax.Value, fieldType);
            else
            {
                ImmutableArray<Symbol> candidates = _scope.Find(name, SymbolNamespace.Value, fieldSyntax.Span.Start);
                value = candidates.Length == 1 ? Coerce(Value(Binder.Unwrap(candidates[0]), fieldSyntax), fieldType, fieldSyntax)
                    : Bad(fieldSyntax, $"Cannot resolve shorthand field value '{name}'.");
            }
            _binder.RecordSymbol(_tree, fieldSyntax, field); Record(fieldSyntax, value);
            values.Add(new BoundFieldInitializer(field, value));
        }
        BoundExpression? baseValue = syntax.Base is null ? null : Expression(syntax.Base, type);
        if (baseValue is not null && symbol is VariantSymbol) Error(syntax, "Functional update syntax is not valid for enums.");
        foreach (FieldSymbol field in fields.Where(f => !seen.Contains(f.Name)))
        {
            if (baseValue is null) Error(syntax, $"Missing field '{field.Name}'.");
            else if (!_binder.IsAccessible(field, Binder.ModuleOf(_scope))) Error(syntax, $"Field '{field.Name}' is private.");
        }
        _binder.RecordSymbol(_tree, syntax, symbol);
        return new BoundConstructionExpression(syntax, type, symbol as VariantSymbol, values.ToImmutable(), baseValue);
    }

    private BoundBlockExpression Block(BlockExpressionSyntax syntax, TypeSymbol? expected = null)
    {
        BindingScope previous = Enter(syntax);
        bool wasUnsafe = _unsafe;
        _unsafe |= Binder.Has(syntax, SyntaxKind.UnsafeKeyword);
        _binder.CheckAttributes(syntax.Attributes, _tree);
        if (syntax.ChildTokens().Any(t => t.Kind is SyntaxKind.AsyncKeyword or SyntaxKind.ConstKeyword or SyntaxKind.TryKeyword or SyntaxKind.GenKeyword))
            _binder.Unsupported(_tree, syntax, "Async, const, try or generator blocks");
        SyntaxToken labelToken = syntax.ChildTokens().FirstOrDefault(t => t.Kind is SyntaxKind.LifetimeToken or SyntaxKind.RawLifetimeToken);
        LoopContext? context = null;
        if (labelToken.Kind != SyntaxKind.None)
        {
            var label = new LabelSymbol(Binder.Name(labelToken), _scope.Owner, syntax, _tree, false);
            context = new LoopContext(label, expected ?? _binder.Fresh(syntax), true, VariablesInScope());
            _loops.Add(context); Data.Declarations[syntax] = label; _scope.Add(label.Name, SymbolNamespace.Label, label);
        }
        var statements = ImmutableArray.CreateBuilder<BoundStatement>();
        foreach (StatementSyntax statement in syntax.Statements)
        {
            BoundStatement bound;
            switch (statement)
            {
                case LetStatementSyntax let:
                {
                    TypeSymbol type = let.Type is null ? _binder.Fresh(let.Pattern ?? (SyntaxNode)let) : _binder.BindType(let.Type, _scope, _tree, true);
                    BoundExpression? initializer = let.Initializer?.Value is ExpressionSyntax value ? Expression(value, type) : null;
                    FlowState afterInitializer = Save();
                    BoundBlockExpression? otherwise = let.Else is null ? null : Block(let.Else);
                    if (otherwise is not null && otherwise.Type.Kind != TypeKind.Never && !otherwise.HasErrors) Error(let.Else ?? (SyntaxNode)let, "The else branch of let must diverge.");
                    Restore(afterInitializer);
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    BoundPattern pattern = Pattern(let.Pattern, type, let, let.Span.End, names);
                    if (!pattern.IsIrrefutable && otherwise is null) Error(let, "Refutable pattern requires let-else, if-let or match.");
                    if (initializer is null)
                    {
                        if (otherwise is not null) Error(let, "Let-else requires an initializer.");
                        foreach (VariableSymbol variable in PatternVariables(pattern)) { _initialized.Remove(variable); _assigned.Remove(variable); }
                    }
                    CheckMutablePattern(pattern, initializer);
                    bound = new BoundLetStatement(let, pattern, initializer, otherwise);
                    break;
                }
                case ExpressionStatementSyntax expression:
                {
                    BoundExpression value = Expression(expression.Expression);
                    if (expression.Semicolon.Kind == SyntaxKind.None && value.Type.Kind != TypeKind.Never) value = Coerce(value, Types.Unit, expression.Expression ?? (SyntaxNode)expression);
                    bound = new BoundExpressionStatement(statement, value);
                    break;
                }
                case EmptyStatementSyntax: bound = new BoundExpressionStatement(statement, null); break;
                default:
                    _binder.Unsupported(_tree, statement, "Block-local items");
                    bound = new BoundExpressionStatement(statement, Bad(statement));
                    break;
            }
            statements.Add(Record(statement, bound));
        }
        BoundExpression? tail = syntax.TailExpression is null ? null : Expression(syntax.TailExpression, expected ?? context?.Type);
        TypeSymbol result = !_reachable ? Types.Never : tail?.Type ?? Types.Unit;
        if (expected is not null && tail is null && _reachable && !_binder.Unify(Types.Unit, expected)) Error(syntax, $"Expected '{expected}', found '()'.");
        if (context is not null)
        {
            _loops.RemoveAt(_loops.Count - 1);
            if (_reachable)
            {
                if (!_binder.Unify(result, context.Type)) Error(syntax, "Block result and break values have incompatible types.");
                context.Breaks.Add(Save());
            }
            Merge(context.Breaks);
            result = context.Breaks.Count == 0 ? Types.Never : context.Type;
        }
        ImmutableArray<LocalSymbol> locals = _scope.Entries.Select(e => e.Symbol).OfType<LocalSymbol>().Distinct().ToImmutableArray();
        _scope = previous; _unsafe = wasUnsafe;
        return Record(syntax, new BoundBlockExpression(syntax, result, statements.ToImmutable(), tail, locals, context?.Label));
    }
    private BoundExpression Condition(ExpressionSyntax? syntax, out FlowState whenFalse)
    {
        if (syntax is LetExpressionSyntax let)
        {
            BoundExpression value = Expression(let.Value);
            whenFalse = Save();
            BoundPattern pattern = Pattern(let.Pattern, value.Type, let, let.Span.End, new HashSet<string>(StringComparer.Ordinal));
            CheckMutablePattern(pattern, value);
            return Record(let, new BoundPatternTestExpression(let, Types.Bool, pattern, value));
        }
        if (syntax is BinaryExpressionSyntax { OperatorToken.Kind: SyntaxKind.AmpersandAmpersandToken } and)
        {
            BoundExpression left = Condition(and.Left, out FlowState leftFalse);
            BoundExpression right = Condition(and.Right, out FlowState rightFalse);
            FlowState success = Save();
            Merge(new[] { leftFalse, rightFalse });
            whenFalse = Save(); Restore(success);
            return Record(and, new BoundBinaryExpression(and, Types.Bool, SyntaxKind.AmpersandAmpersandToken, left, right));
        }
        BoundExpression condition = Expression(syntax, Types.Bool);
        whenFalse = Save();
        return condition;
    }
    private BoundExpression If(IfExpressionSyntax syntax, TypeSymbol? expected)
    {
        BindingScope previous = Enter(syntax);
        BoundExpression condition = Condition(syntax.Condition, out FlowState whenFalse);
        BoundBlockExpression then = syntax.Then is null
            ? new BoundBlockExpression(syntax, Types.Error, [], null, []) : Block(syntax.Then, syntax.Else is null ? Types.Unit : expected);
        FlowState afterThen = Save();
        _scope = previous;
        Restore(whenFalse);
        BoundExpression? otherwise = syntax.Else is null ? null : Expression(syntax.Else, expected);
        FlowState afterElse = Save();
        Merge(new[] { afterThen, afterElse });
        TypeSymbol type;
        if (otherwise is null) type = Types.Unit;
        else if (then.Type.Kind == TypeKind.Never) type = otherwise.Type;
        else if (otherwise.Type.Kind == TypeKind.Never) type = then.Type;
        else
        {
            type = expected ?? then.Type;
            otherwise = Coerce(otherwise, type, syntax.Else ?? syntax);
        }
        return new BoundIfExpression(syntax, type, condition, then, otherwise);
    }
    private HashSet<VariableSymbol> VariablesInScope()
    {
        var result = new HashSet<VariableSymbol>();
        for (BindingScope? scope = _scope; scope is not null; scope = scope.Parent)
            result.UnionWith(scope.Entries.Select(e => e.Symbol).OfType<VariableSymbol>().Where(v => v is LocalSymbol or ParameterSymbol));
        return result;
    }
    private BoundExpression Loop(LoopExpressionSyntax syntax, TypeSymbol? expected)
    {
        bool isFor = Binder.Has(syntax, SyntaxKind.ForKeyword);
        bool isWhile = Binder.Has(syntax, SyntaxKind.WhileKeyword);
        string name = Binder.Name(syntax.Label);
        var label = new LabelSymbol(name, _scope.Owner, syntax, _tree, true);
        var context = new LoopContext(label, isFor || isWhile ? Types.Unit : expected ?? _binder.Fresh(syntax), !isFor && !isWhile, VariablesInScope());
        FlowState before = Save();
        BindingScope previous = Enter(syntax);
        if (name.Length != 0) _scope.Add(name, SymbolNamespace.Label, label);
        Data.Declarations[syntax] = label;
        BoundExpression? condition = null;
        BoundExpression? iterator = null;
        BoundPattern? pattern = null;
        if (isFor)
        {
            iterator = Expression(syntax.ConditionOrIterator);
            before = Save();
            TypeSymbol element;
            if (iterator.Type is SequenceTypeSymbol { Kind: TypeKind.Array or TypeKind.Range } sequence) element = sequence.ElementType;
            else if (iterator.Type is IndirectTypeSymbol { Kind: TypeKind.Reference, ElementType: SequenceTypeSymbol { Kind: TypeKind.Array or TypeKind.Slice } seq } reference)
                element = new IndirectTypeSymbol(TypeKind.Reference, seq.ElementType, reference.IsMutable);
            else { element = Types.Error; _binder.Unsupported(_tree, syntax, "IntoIterator resolution for this type"); }
            if (iterator is BoundRangeExpression range)
            {
                if (range.Start is null || range.End is null) _binder.Unsupported(_tree, syntax, "Unbounded range iteration");
                _binder.CheckLater(() =>
                {
                    if (!element.IsError && element.Kind is not (TypeKind.Integer or TypeKind.Character)) Error(syntax, "Range iteration requires an integer or character element type.");
                });
            }
            pattern = Pattern(syntax.Pattern, element, syntax, syntax.ConditionOrIterator?.Span.End ?? syntax.Span.Start, new HashSet<string>(StringComparer.Ordinal));
            if (!pattern.IsIrrefutable) Error(syntax, "A for loop requires an irrefutable pattern.");
        }
        _loops.Add(context);
        if (isWhile) condition = Condition(syntax.ConditionOrIterator, out before);
        BoundBlockExpression body = syntax.Body is null ? new BoundBlockExpression(syntax, Types.Error, [], null, []) : Block(syntax.Body, Types.Unit);
        FlowState afterBody = Save();
        foreach (FlowState backedge in context.Continues.Append(afterBody).Where(s => s.Reachable))
            foreach (VariableSymbol variable in context.Outer.Where(v => !v.IsMutable && !before.Assigned.Contains(v) && backedge.Assigned.Contains(v)))
                Error(syntax, $"Loop may assign to immutable '{variable.Name}' more than once.");
        _loops.RemoveAt(_loops.Count - 1); _scope = previous;
        if (isFor || isWhile)
        {
            Merge(context.Breaks.Append(before));
            _assigned.UnionWith(afterBody.Assigned);
        }
        else Merge(context.Breaks);
        TypeSymbol type = isFor || isWhile ? Types.Unit : context.Breaks.Count == 0 ? Types.Never : context.Type;
        if (!isFor && !isWhile && context.Breaks.Count == 0) _binder.Unify(context.Type, Types.Never);
        if (isFor && iterator is not null && pattern is not null) return new BoundForExpression(syntax, Types.Unit, label, pattern, iterator, body);
        return new BoundLoopExpression(syntax, type, label, condition, body);
    }
    private BoundExpression Control(ControlFlowExpressionSyntax syntax)
    {
        SyntaxKind keyword = syntax.Keyword.Kind;
        if (keyword == SyntaxKind.ReturnKeyword)
        {
            if (_function is null) return Bad(syntax, "'return' is only valid inside a function.");
            BoundExpression? value = syntax.Value is null ? null : Expression(syntax.Value, _function.ReturnType);
            if (value is null && !_binder.Unify(Types.Unit, _function.ReturnType)) Error(syntax, $"Return requires a value of type '{_function.ReturnType}'.");
            return new BoundControlFlowExpression(BoundNodeKind.Return, syntax, Types.Never, null, value);
        }
        bool isContinue = keyword == SyntaxKind.ContinueKeyword;
        if (!isContinue && keyword != SyntaxKind.BreakKeyword) return Unsupported(syntax);
        string name = Binder.Name(syntax.Label);
        LoopContext? context = name.Length == 0 ? _loops.LastOrDefault(l => l.Label.IsLoop) : _loops.LastOrDefault(l => l.Label.Name == name);
        if (context is null) return Bad(syntax, name.Length == 0 ? "No enclosing loop." : $"Unknown label '{name}'.");
        if (isContinue && !context.Label.IsLoop) Error(syntax, "Continue must target a loop.");
        if (name.Length != 0) _binder.RecordSymbol(_tree, syntax, context.Label);
        BoundExpression? result = syntax.Value is null ? null : Expression(syntax.Value, context.AllowsValue ? context.Type : null);
        if (result is not null && (isContinue || !context.AllowsValue)) Error(syntax, "Only loop and labelled block breaks can carry values.");
        if (!isContinue)
        {
            if (result is null && !_binder.Unify(context.Type, Types.Unit)) Error(syntax, "Break values have incompatible types.");
            if (_reachable) context.Breaks.Add(Save());
        }
        else if (_reachable) context.Continues.Add(Save());
        return new BoundControlFlowExpression(isContinue ? BoundNodeKind.Continue : BoundNodeKind.Break, syntax, Types.Never, context.Label, result);
    }
    private BoundExpression Range(RangeExpressionSyntax syntax)
    {
        BoundExpression? start = syntax.Start is null ? null : Expression(syntax.Start);
        BoundExpression? end = syntax.End is null ? null : Expression(syntax.End);
        TypeSymbol element = start?.Type ?? end?.Type ?? Types.Unit;
        if (start is not null && end is not null && !_binder.Unify(start.Type, end.Type)) Error(syntax, "Range endpoints have incompatible types.");
        bool inclusive = syntax.OperatorToken.Kind == SyntaxKind.DotDotEqualsToken;
        RangeKind form = start is null ? end is null ? RangeKind.Full : inclusive ? RangeKind.ToInclusive : RangeKind.To : end is null ? RangeKind.From : inclusive ? RangeKind.Inclusive : RangeKind.Exclusive;
        return new BoundRangeExpression(syntax, new SequenceTypeSymbol(TypeKind.Range, element, rangeForm: form), start, end, inclusive);
    }

    private void AddVariable(VariableSymbol symbol, int visibleFrom, HashSet<string> names, SyntaxNode syntax)
    {
        if (!names.Add(symbol.Name)) Error(syntax, $"'{symbol.Name}' is bound more than once in the pattern.");
        _scope.Add(symbol.Name, SymbolNamespace.Value, symbol, visibleFrom);
        _initialized.Add(symbol); _assigned.Add(symbol);
        Data.Declarations[syntax] = symbol;
        if (symbol is LocalSymbol) _binder.CheckLater(() =>
        {
            if (!_binder.IsSized(symbol.Type)) Error(syntax, $"Local '{symbol.Name}' must have a sized type.");
        });
    }
    private static IEnumerable<VariableSymbol> PatternVariables(BoundPattern pattern)
        => pattern.Descendants().Prepend(pattern).OfType<BoundPattern>().Select(p => p.Variable).OfType<VariableSymbol>().Distinct();
    private void CheckMutablePattern(BoundPattern pattern, BoundExpression? value)
    {
        if (value?.ValueCategory == ValueCategory.Place && pattern.Descendants().Prepend(pattern).OfType<BoundPattern>().Any(p => p.BindingMode == BindingMode.MutableReference))
            Error(pattern.Syntax, "Cannot mutably borrow an immutable place in a pattern.");
    }
    private BoundPattern Pattern(PatternSyntax? syntax, TypeSymbol input, SyntaxNode fallback, int visibleFrom, HashSet<string> names, ParameterSymbol? parameter = null, BindingMode mode = BindingMode.Value)
    {
        if (syntax is null) return new BoundPattern(fallback, Types.Error, BoundPatternKind.Error);
        if (++_depth > 256) { _depth--; Error(syntax, "Pattern nesting exceeds the semantic binding limit."); return new BoundPattern(syntax, Types.Error, BoundPatternKind.Error); }
        BoundPattern result;
        try { result = BindPattern(syntax, input.Resolve(), visibleFrom, names, parameter, mode); }
        finally { _depth--; }
        _binder.RecordType(_tree, syntax, input);
        return Record(syntax, result);
    }
    private BoundPattern BindPattern(PatternSyntax syntax, TypeSymbol input, int visibleFrom, HashSet<string> names, ParameterSymbol? parameter, BindingMode mode)
    {
        if (syntax is not (IdentifierPatternSyntax or ReferencePatternSyntax or WildcardPatternSyntax) && input is IndirectTypeSymbol { Kind: TypeKind.Reference } reference)
        {
            BindingMode inherited = mode == BindingMode.Reference || !reference.IsMutable ? BindingMode.Reference : BindingMode.MutableReference;
            BoundPattern inner = Pattern(syntax, reference.ElementType, syntax, visibleFrom, names, parameter, inherited);
            return new BoundPattern(syntax, input, BoundPatternKind.Reference, [inner], irrefutable: inner.IsIrrefutable);
        }
        switch (syntax)
        {
            case WildcardPatternSyntax: return new BoundPattern(syntax, input, BoundPatternKind.Wildcard, irrefutable: true);
            case ParenthesizedPatternSyntax p: return Pattern(p.Pattern, input, syntax, visibleFrom, names, parameter, mode);
            case IdentifierPatternSyntax identifier:
            {
                string name = Binder.Name(identifier.Identifier);
                bool byRef = Binder.Has(identifier, SyntaxKind.RefKeyword);
                bool mutable = Binder.Has(identifier, SyntaxKind.MutKeyword);
                Symbol? existing = _scope.Find(name, SymbolNamespace.Value, identifier.Span.Start).FirstOrDefault();
                existing = existing is null ? null : Binder.Unwrap(existing);
                if (!byRef && !mutable && identifier.Subpattern is null && existing is GlobalSymbol { Kind: SymbolKind.Constant } or VariantSymbol { Shape: AggregateShape.Unit } or AggregateSymbol { Shape: AggregateShape.Unit })
                    return ConstantPattern(existing, syntax, input);
                if (existing is GlobalSymbol or VariantSymbol { Shape: AggregateShape.Unit } or AggregateSymbol { Shape: AggregateShape.Unit })
                    Error(syntax, $"Pattern binding cannot shadow '{name}'.");
                if (mode != BindingMode.Value && (byRef || mutable)) Error(syntax, "Explicit ref or mut bindings require the default move binding mode in Rust 2024.");
                BindingMode bindingMode = byRef ? mutable ? BindingMode.MutableReference : BindingMode.Reference : mode;
                TypeSymbol type = bindingMode == BindingMode.Value ? input : new IndirectTypeSymbol(TypeKind.Reference, input, bindingMode == BindingMode.MutableReference);
                VariableSymbol variable = parameter is not null && bindingMode == BindingMode.Value ? parameter
                    : new LocalSymbol(name, _scope.Owner, syntax, _tree, type, !byRef && mutable);
                if (variable is ParameterSymbol) variable.IsMutable = mutable;
                AddVariable(variable, visibleFrom, names, syntax);
                BoundPattern? subpattern = identifier.Subpattern is null ? null : Pattern(identifier.Subpattern, input, syntax, visibleFrom, names, mode: mode);
                return new BoundPattern(syntax, input, BoundPatternKind.Binding, subpattern is null ? [] : [subpattern], variable, mode: bindingMode, irrefutable: subpattern?.IsIrrefutable ?? true);
            }
            case LiteralPatternSyntax literal:
            {
                BoundExpression value = Coerce(Literal(literal.Literal, literal, Binder.Has(literal, SyntaxKind.MinusToken)), input, syntax);
                return new BoundPattern(syntax, input, BoundPatternKind.Literal, value: value);
            }
            case PathPatternSyntax path:
            {
                Symbol? symbol = _binder.ResolvePath(path.Path, _scope, _tree, SymbolNamespace.Value);
                return symbol is null ? new BoundPattern(syntax, Types.Error, BoundPatternKind.Error) : ConstantPattern(symbol, syntax, input, path.Path);
            }
            case TuplePatternSyntax tuple:
            {
                PatternSyntax[] elements = tuple.Elements.ToArray();
                int rest = System.Array.FindIndex(elements, p => p is RestPatternSyntax);
                int count = elements.Count(p => p is not RestPatternSyntax);
                if (input is InferenceTypeSymbol && rest < 0)
                {
                    _binder.Unify(input, new TupleTypeSymbol(elements.Select(p => (TypeSymbol)_binder.Fresh(p)).ToImmutableArray()));
                    input = input.Resolve();
                }
                if (input is not TupleTypeSymbol type) { Error(syntax, $"Tuple pattern cannot match '{input}'."); return new BoundPattern(syntax, input, BoundPatternKind.Error); }
                if (rest < 0 && count != type.Elements.Length || rest >= 0 && count > type.Elements.Length) Error(syntax, "Tuple pattern has the wrong number of elements.");
                ImmutableArray<BoundPattern> bound = SequencePatterns(elements, type.Elements, visibleFrom, names, mode);
                return new BoundPattern(syntax, input, BoundPatternKind.Tuple, bound, irrefutable: bound.All(p => p.IsIrrefutable));
            }
            case ReferencePatternSyntax referencePattern:
            {
                bool mutable = Binder.Has(syntax, SyntaxKind.MutKeyword);
                if (mode != BindingMode.Value) Error(syntax, "Reference patterns require the default move binding mode in Rust 2024.");
                if (input is InferenceTypeSymbol) _binder.Unify(input, new IndirectTypeSymbol(TypeKind.Reference, _binder.Fresh(syntax), mutable));
                if (input.Resolve() is not IndirectTypeSymbol { Kind: TypeKind.Reference } refType || refType.IsMutable != mutable)
                { Error(syntax, "Reference pattern has an incompatible reference type."); return new BoundPattern(syntax, input, BoundPatternKind.Error); }
                TypeSymbol element = refType.ElementType;
                if (Binder.Has(syntax, SyntaxKind.AmpersandAmpersandToken))
                {
                    if (element.Resolve() is IndirectTypeSymbol { Kind: TypeKind.Reference, IsMutable: false } inner) element = inner.ElementType;
                    else Error(syntax, "Double-reference pattern requires two references.");
                }
                BoundPattern pattern = Pattern(referencePattern.Pattern, element, syntax, visibleFrom, names);
                return new BoundPattern(syntax, input, BoundPatternKind.Reference, [pattern], irrefutable: pattern.IsIrrefutable);
            }
            case TupleStructPatternSyntax tupleStruct:
                return AggregatePattern(tupleStruct.Path, syntax, input, tupleStruct.Elements.ToArray(), null, visibleFrom, names, mode);
            case StructPatternSyntax structure:
                return AggregatePattern(structure.Path, syntax, input, null, structure.Fields.ToArray(), visibleFrom, names, mode);
            case OrPatternSyntax alternatives:
            {
                var patterns = ImmutableArray.CreateBuilder<BoundPattern>();
                foreach (PatternSyntax alternative in alternatives.Alternatives)
                {
                    BindingScope saved = Enter(alternative);
                    BoundPattern pattern = Pattern(alternative, input, syntax, visibleFrom, new HashSet<string>(StringComparer.Ordinal), mode: mode);
                    if (PatternVariables(pattern).Any()) _binder.Unsupported(_tree, alternative, "Bindings in or-patterns");
                    patterns.Add(pattern); _scope = saved;
                }
                return new BoundPattern(syntax, input, BoundPatternKind.Or, patterns.ToImmutable(), irrefutable: patterns.Any(p => p.IsIrrefutable));
            }
            case RangePatternSyntax range:
            {
                BoundExpression? Endpoint(PatternSyntax? endpoint)
                {
                    if (endpoint is null) return null;
                    BoundPattern pattern = Pattern(endpoint, input, syntax, visibleFrom, new HashSet<string>(StringComparer.Ordinal));
                    if (pattern.PatternKind == BoundPatternKind.Literal && pattern.Value is BoundExpression value) return value;
                    Error(endpoint, "Range pattern endpoints must be constants.");
                    return new BoundErrorExpression(endpoint, Types.Error);
                }
                BoundExpression? start = Endpoint(range.Start), end = Endpoint(range.End);
                bool inclusive = range.OperatorToken.Kind != SyntaxKind.DotDotToken;
                _binder.CheckLater(() =>
                {
                    if (input.Resolve().Kind is not (TypeKind.Integer or TypeKind.Character)) Error(syntax, "Range patterns require an integer or character type.");
                    static BigInteger? Ordinal(object? value) => value switch
                    {
                        BigInteger integer => integer,
                        string character when character.Length != 0 => char.ConvertToUtf32(character, 0),
                        _ => null
                    };
                    if (Ordinal(start?.ConstantValue.Value) is BigInteger lower && Ordinal(end?.ConstantValue.Value) is BigInteger upper && (inclusive ? lower > upper : lower >= upper))
                        Error(syntax, "Range pattern is empty.");
                });
                return new BoundPattern(syntax, input, BoundPatternKind.Range, value: new BoundRangeExpression(syntax,
                    new SequenceTypeSymbol(TypeKind.Range, input, rangeForm: inclusive ? RangeKind.Inclusive : RangeKind.Exclusive), start, end, inclusive));
            }
            case SlicePatternSyntax slice:
            {
                if (input is not SequenceTypeSymbol { Kind: TypeKind.Array or TypeKind.Slice } sequence)
                { Error(syntax, $"Slice pattern cannot match '{input}'."); return new BoundPattern(syntax, input, BoundPatternKind.Error); }
                var patterns = ImmutableArray.CreateBuilder<BoundPattern>();
                bool rest = false;
                foreach (PatternSyntax element in slice.Elements)
                {
                    if (element is RestPatternSyntax)
                    {
                        if (rest) Error(element, "Only one rest pattern is allowed.");
                        rest = true; patterns.Add(new BoundPattern(element, input, BoundPatternKind.Rest, irrefutable: true));
                    }
                    else patterns.Add(Pattern(element, sequence.ElementType, syntax, visibleFrom, names, mode: mode));
                }
                int count = patterns.Count(p => p.PatternKind != BoundPatternKind.Rest);
                if (sequence.Length is ulong length && (rest ? (ulong)count > length : (ulong)count != length)) Error(syntax, "Slice pattern has an incompatible array length.");
                bool irrefutable = patterns.All(p => p.IsIrrefutable) && (sequence.Length is not null || rest && count == 0);
                return new BoundPattern(syntax, input, BoundPatternKind.Slice, patterns.ToImmutable(), irrefutable: irrefutable);
            }
            default: _binder.Unsupported(_tree, syntax, syntax.Kind.ToString()); return new BoundPattern(syntax, input, BoundPatternKind.Error);
        }
    }
    private ImmutableArray<BoundPattern> SequencePatterns(PatternSyntax[] syntax, ImmutableArray<TypeSymbol> types, int visibleFrom, HashSet<string> names, BindingMode mode)
    {
        var result = ImmutableArray.CreateBuilder<BoundPattern>();
        int slot = 0;
        bool rest = false;
        for (int i = 0; i < syntax.Length; i++)
        {
            if (syntax[i] is RestPatternSyntax)
            {
                if (rest) Error(syntax[i], "Only one rest pattern is allowed.");
                rest = true; slot = types.Length - (syntax.Length - i - 1);
                continue;
            }
            BoundPattern pattern = Pattern(syntax[i], slot >= 0 && slot < types.Length ? types[slot] : Types.Error, syntax[i], visibleFrom, names, mode: mode);
            pattern.InputIndex = slot;
            result.Add(pattern);
            slot++;
        }
        return result.ToImmutable();
    }
    private BoundPattern ConstantPattern(Symbol symbol, SyntaxNode syntax, TypeSymbol input, PathSyntax? path = null)
    {
        if (symbol is GlobalSymbol { Kind: SymbolKind.Constant } constant)
        {
            _binder.BindGlobal(constant);
            BoundExpression value = Coerce(Value(constant, syntax), input, syntax);
            _binder.RecordSymbol(_tree, syntax, constant);
            return new BoundPattern(syntax, input, BoundPatternKind.Literal, matched: constant, value: value);
        }
        AggregateSymbol? aggregate = symbol switch { AggregateSymbol { Shape: AggregateShape.Unit } a => a, VariantSymbol { Shape: AggregateShape.Unit } v => v.Enum, _ => null };
        if (aggregate is null) { Error(syntax, "Expected a constant or unit constructor."); return new BoundPattern(syntax, input, BoundPatternKind.Error); }
        NamedTypeSymbol type = Instantiate(aggregate, path, syntax);
        if (!_binder.Unify(input, type)) Error(syntax, $"Pattern type '{type}' does not match '{input}'.");
        _binder.RecordSymbol(_tree, syntax, symbol);
        return new BoundPattern(syntax, input, BoundPatternKind.Aggregate, matched: symbol, irrefutable: aggregate.TypeKind == TypeKind.Struct || aggregate.Variants.Length == 1);
    }
    private BoundPattern AggregatePattern(PathSyntax? path, PatternSyntax syntax, TypeSymbol input, PatternSyntax[]? elements, StructPatternFieldSyntax[]? namedFields,
        int visibleFrom, HashSet<string> names, BindingMode mode)
    {
        Symbol? symbol = _binder.ResolvePath(path, _scope, _tree, elements is null ? SymbolNamespace.Type : SymbolNamespace.Value);
        NamedTypeSymbol? aliasType = symbol is TypeAliasSymbol ? _binder.BindPathType(path, _scope, _tree, true) as NamedTypeSymbol : null;
        AggregateSymbol? aggregate = symbol switch { AggregateSymbol a => a, VariantSymbol v => v.Enum, _ => aliasType?.Definition };
        if (symbol is null || aggregate is null || symbol is AggregateSymbol { TypeKind: TypeKind.Enum })
        { Error(syntax, "Pattern requires a struct or enum variant."); return new BoundPattern(syntax, input, BoundPatternKind.Error); }
        AggregateShape shape = symbol is VariantSymbol shapedVariant ? shapedVariant.Shape : aggregate.Shape;
        if (elements is not null && shape != AggregateShape.Tuple) Error(syntax, "Tuple-struct pattern requires a tuple constructor.");
        NamedTypeSymbol type = aliasType ?? Instantiate(aggregate, path, syntax);
        if (!_binder.Unify(input, type)) Error(syntax, $"Pattern type '{type}' does not match '{input}'.");
        ImmutableArray<FieldSymbol> fields = symbol is VariantSymbol variant ? variant.Fields : aggregate.Fields;
        var bound = ImmutableArray.CreateBuilder<BoundPattern>();
        var matchedFields = ImmutableArray.CreateBuilder<FieldSymbol>();
        if (elements is not null)
        {
            bool rest = elements.Any(p => p is RestPatternSyntax);
            int count = elements.Count(p => p is not RestPatternSyntax);
            if (rest ? count > fields.Length : count != fields.Length) Error(syntax, "Constructor pattern has the wrong number of fields.");
            bound.AddRange(SequencePatterns(elements, fields.Select(f => Types.FieldType(f, type)).ToImmutableArray(), visibleFrom, names, mode));
            int skip = System.Array.FindIndex(elements, p => p is RestPatternSyntax);
            matchedFields.AddRange(skip < 0 ? fields : fields.Take(skip).Concat(fields.Skip(fields.Length - (elements.Length - skip - 1))));
        }
        else
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (StructPatternFieldSyntax fieldSyntax in namedFields ?? [])
            {
                string name = Binder.Name(fieldSyntax.Identifier);
                FieldSymbol? field = fields.FirstOrDefault(f => f.Name == name);
                if (!seen.Add(name)) Error(fieldSyntax, $"Field '{name}' occurs more than once.");
                if (field is null) { Error(fieldSyntax, $"Unknown field '{name}'."); continue; }
                if (!_binder.IsAccessible(field, Binder.ModuleOf(_scope))) Error(fieldSyntax, $"Field '{name}' is private.");
                bound.Add(Pattern(fieldSyntax.Pattern, Types.FieldType(field, type), fieldSyntax, visibleFrom, names, mode: mode));
                matchedFields.Add(field); _binder.RecordSymbol(_tree, fieldSyntax, field);
            }
            if (!syntax.ChildNodes().OfType<RestPatternSyntax>().Any() && fields.Any(f => !seen.Contains(f.Name))) Error(syntax, "Pattern must mention every field or include '..'.");
        }
        _binder.RecordSymbol(_tree, syntax, symbol);
        foreach (FieldSymbol field in matchedFields)
            if (!_binder.IsAccessible(field, Binder.ModuleOf(_scope))) Error(syntax, $"Field '{field.Name}' is private.");
        bool irrefutable = (aggregate.TypeKind == TypeKind.Struct || aggregate.Variants.Length == 1) && bound.All(p => p.IsIrrefutable);
        return new BoundPattern(syntax, input, BoundPatternKind.Aggregate, bound.ToImmutable(), matched: symbol, fields: matchedFields.ToImmutable(), irrefutable: irrefutable);
    }
    private BoundExpression Match(MatchExpressionSyntax syntax, TypeSymbol? expected)
    {
        BoundExpression scrutinee = Expression(syntax.Expression);
        FlowState before = Save();
        var exits = new List<FlowState>();
        var arms = ImmutableArray.CreateBuilder<BoundMatchArm>();
        TypeSymbol? result = expected;
        foreach (MatchArmSyntax arm in syntax.Arms)
        {
            Restore(before);
            BindingScope previous = Enter(arm);
            BoundPattern pattern = Pattern(arm.Pattern, scrutinee.Type, arm, arm.Pattern?.Span.End ?? arm.Span.Start, new HashSet<string>(StringComparer.Ordinal));
            CheckMutablePattern(pattern, scrutinee);
            BoundExpression? guard = arm.Guard is null ? null : Expression(arm.Guard, Types.Bool);
            BoundExpression value = Expression(arm.Expression, result);
            if (value.Type.Kind != TypeKind.Never && result is null) result = value.Type;
            arms.Add(Record(arm, new BoundMatchArm(arm, pattern, guard, value)));
            exits.Add(Save()); _scope = previous;
        }
        Merge(exits);
        var match = new BoundMatchExpression(syntax, result ?? Types.Never, scrutinee, arms.ToImmutable());
        _binder.CheckLater(() =>
        {
            IEnumerable<BoundPattern> alternatives = arms.Where(a => a.Guard is null).SelectMany(a => Alternatives(a.Pattern)).ToArray();
            TypeSymbol type = scrutinee.Type;
            while (type is IndirectTypeSymbol { Kind: TypeKind.Reference } reference) type = reference.ElementType.Resolve();
            bool exhaustive = alternatives.Any(p => p.IsIrrefutable);
            if (!exhaustive && type.Kind == TypeKind.Boolean)
                exhaustive = alternatives.Any(p => p.Value?.ConstantValue.Value is true) && alternatives.Any(p => p.Value?.ConstantValue.Value is false);
            if (!exhaustive && type is NamedTypeSymbol { Kind: TypeKind.Enum } named)
                exhaustive = named.Definition.Variants.All(v => alternatives.Any(p => ReferenceEquals(p.MatchedSymbol, v) && p.Subpatterns.All(s => s.IsIrrefutable)));
            if (!exhaustive && !type.IsError) { Error(syntax, "Match is not exhaustive; add the missing cases or a wildcard arm."); match.OwnErrors = true; }
        });
        return match;
    }
    private static IEnumerable<BoundPattern> Alternatives(BoundPattern pattern)
        => pattern.PatternKind is BoundPatternKind.Or or BoundPatternKind.Reference ? pattern.Subpatterns.SelectMany(Alternatives) : new[] { pattern };
}
