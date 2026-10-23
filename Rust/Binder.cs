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
    private readonly Dictionary<AggregateSymbol, bool> _discriminants = new();
    private readonly List<BoundFunction> _functions = new();
    private readonly MacroExpander _expander = new(RustEdition.Rust2024);
    private readonly Dictionary<SyntaxNode, MacroScope?> _itemMacros = new();
    private readonly List<ModuleSymbol> _expansionCrates = new();
    private readonly Dictionary<string, ModuleSymbol> _externCrates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _features = new(StringComparer.Ordinal);
    // Searched after the module: the extern and macro_use preludes, then the standard library prelude
    private readonly BindingScope _externPrelude, _stdPrelude;
    internal readonly Dictionary<string, Symbol> LangItems = new(StringComparer.Ordinal);
    internal readonly List<ImplSymbol> LocalImpls = new();
    private readonly Dictionary<ImplSymbol, (ImplItemSyntax Syntax, ImmutableArray<ItemSyntax> Items)> _implItems = new();
    private readonly Dictionary<Symbol, ParamEnv> _environments = new();
    private List<(InferenceTypeSymbol Variable, TypeSymbol? Binding, InferenceKind Constraint, bool Never, TypeSymbol? Default)>? _undo;
    private MacroScope? _macroScope;
    internal int ExpansionDepth;
    private int _typeDepth;
    private int _implOrdinal;

    internal Binder(Compilation compilation)
    {
        _compilation = compilation;
        Types = TypeSystem.For(compilation.Options.PointerWidth);
        SyntaxTree root = compilation.RootSyntaxTree;
        Crate = new ModuleSymbol(compilation.Options.CrateName, null, root.Root, root, null) { IsPublic = true };
        _stdPrelude = new BindingScope(null, Crate, root, root.Root.Span);
        _externPrelude = new BindingScope(_stdPrelude, Crate, root, root.Root.Span);
        Crate.Scope.Parent = _externPrelude;
        foreach (Compilation reference in compilation.References) Inject(reference.Binding);
    }
    // The implicit #[macro_use] extern crate and #[prelude_import] use of the injected library crate
    private void Inject(Binder library)
    {
        ModuleSymbol crate = library.Crate;
        foreach (ImplSymbol impl in library.LocalImpls) AddImpl(impl);
        _externCrates.Add(crate.Name, crate);
        _externPrelude.Add(crate.Name, SymbolNamespace.Type, crate);
        foreach (ScopeEntry entry in crate.Scope.Entries)
            if (entry.Namespace == SymbolNamespace.Macro && entry.Symbol is MacroSymbol { IsPublic: true }) _externPrelude.Add(entry.Name, entry.Namespace, entry.Symbol);
        ModuleSymbol? prelude = Submodule(Submodule(crate, "prelude"), "rust_2024");
        foreach (ScopeEntry entry in prelude?.Scope.Entries ?? [])
            if (IsAccessible(entry.Symbol, Crate)) _stdPrelude.Add(entry.Name, entry.Namespace, entry.Symbol);
    }
    private static ModuleSymbol? Submodule(ModuleSymbol? module, string name)
        => module?.Scope.Named(name).Select(e => e.Namespace == SymbolNamespace.Type ? Unwrap(e.Symbol) : null).OfType<ModuleSymbol>().FirstOrDefault();
    private static ModuleSymbol? CrateOf(Symbol symbol)
    {
        Symbol current = symbol;
        while (current.ContainingSymbol is Symbol containing) current = containing;
        return current as ModuleSymbol;
    }
    internal bool IsLocal(Symbol symbol) => ReferenceEquals(CrateOf(symbol), Crate);
    internal Symbol? FindLangItem(string name)
        => LangItems.GetValueOrDefault(name) ?? _compilation.References.Select(r => r.Binding.FindLangItem(name)).FirstOrDefault(s => s is not null);

    // Runs a speculative unification; every inference binding it makes is undone afterwards
    internal T Probe<T>(Func<T> action)
    {
        var outer = _undo;
        _undo = new();
        try { return action(); }
        finally
        {
            for (int i = _undo.Count - 1; i >= 0; i--)
            {
                var (variable, binding, constraint, never, fallback) = _undo[i];
                variable.Binding = binding; variable.Constraint = constraint; variable.HasNeverConstraint = never; variable.DefaultType = fallback;
            }
            _undo = outer;
        }
    }
    private void Change(InferenceTypeSymbol variable) => _undo?.Add((variable, variable.Binding, variable.Constraint, variable.HasNeverConstraint, variable.DefaultType));
    internal TypeSymbol Zonk(TypeSymbol type) => type.Resolve() switch
    {
        NamedTypeSymbol n when !n.TypeArguments.IsEmpty => new NamedTypeSymbol(n.Definition, n.TypeArguments.Select(Zonk).ToImmutableArray(), n.LifetimeArguments),
        TupleTypeSymbol t => new TupleTypeSymbol(t.Elements.Select(Zonk).ToImmutableArray()),
        SequenceTypeSymbol s => new SequenceTypeSymbol(s.Kind, Zonk(s.ElementType), s.Length, s.RangeForm),
        IndirectTypeSymbol r => new IndirectTypeSymbol(r.Kind, Zonk(r.ElementType), r.IsMutable, r.Lifetime),
        FunctionTypeSymbol f => new FunctionTypeSymbol(f.ParameterTypes.Select(Zonk).ToImmutableArray(), Zonk(f.ReturnType), f.IsUnsafe, f.Abi),
        ProjectionTypeSymbol p => new ProjectionTypeSymbol(p.Item, Zonk(p.SelfType), p.TraitArguments.Select(Zonk).ToImmutableArray()),
        var resolved => resolved
    };

    // Trait solving: trait and projection predicates are proved against where-clauses, the impls of every linked crate and builtin rules
    private abstract record Candidate;
    private sealed record EnvironmentCandidate(TraitReference Bound) : Candidate;
    private sealed record ImplCandidate(ImplSymbol Impl) : Candidate;
    private sealed record BuiltinCandidate(ImmutableArray<TraitReference> Nested) : Candidate;
    // Impls of one trait, grouped by the head of their self type; blanket impls match every head
    private sealed class ImplIndex
    {
        internal readonly Dictionary<ImplSymbol, int> Order = new();
        internal readonly Dictionary<object, List<ImplSymbol>> ByHead = new();
        internal readonly List<ImplSymbol> Blanket = new();
    }
    private const int ObligationDepthLimit = 64;
    private static readonly object ReferenceHead = new(), MutableReferenceHead = new(), PointerHead = new(), MutablePointerHead = new(), FunctionHead = new();
    private readonly Dictionary<TraitSymbol, ImplIndex> _traitImpls = new();
    private readonly List<Obligation> _obligations = new();
    internal TraitSymbol? SizedTrait, CopyTrait, CloneTrait;

    private void AddImpl(ImplSymbol impl)
    {
        if (impl.Trait is not TraitReference reference) return;
        if (!_traitImpls.TryGetValue(reference.Trait, out ImplIndex? index)) _traitImpls.Add(reference.Trait, index = new());
        index.Order.Add(impl, index.Order.Count);
        if (TypeHead(reference.Self) is not object head) index.Blanket.Add(impl);
        else if (index.ByHead.TryGetValue(head, out List<ImplSymbol>? list)) list.Add(impl);
        else index.ByHead.Add(head, [impl]);
    }
    private IEnumerable<ImplSymbol> ImplCandidates(TraitSymbol trait, object? head)
    {
        if (!_traitImpls.TryGetValue(trait, out ImplIndex? index)) return [];
        if (head is null) return index.Order.Keys;
        return index.ByHead.TryGetValue(head, out List<ImplSymbol>? list) ? list.Concat(index.Blanket) : index.Blanket;
    }
    // Impls registered before this one that could share a self type with it
    private IEnumerable<ImplSymbol> EarlierImpls(ImplSymbol impl)
    {
        if (impl.Trait is not TraitReference reference || !_traitImpls.TryGetValue(reference.Trait, out ImplIndex? index)) return [];
        int order = index.Order[impl];
        return ImplCandidates(reference.Trait, TypeHead(reference.Self)).Where(other => index.Order[other] < order);
    }
    // Fast rejection by the outermost type constructor; null matches every type
    private static object? TypeHead(TypeSymbol type) => type.Resolve() switch
    {
        NamedTypeSymbol named => named.Definition,
        PrimitiveTypeSymbol primitive => primitive,
        IndirectTypeSymbol { Kind: TypeKind.Reference } reference => reference.IsMutable ? MutableReferenceHead : ReferenceHead,
        IndirectTypeSymbol pointer => pointer.IsMutable ? MutablePointerHead : PointerHead,
        TupleTypeSymbol tuple => tuple.Elements.Length,
        SequenceTypeSymbol sequence => sequence.Kind,
        FunctionTypeSymbol => FunctionHead,
        _ => null
    };

    internal static Dictionary<TypeParameterSymbol, TypeSymbol> Instantiate(ImplSymbol impl, Func<TypeParameterSymbol, TypeSymbol> fresh)
        => impl.TypeParameters.ToDictionary(p => p, fresh);
    internal static Dictionary<TypeParameterSymbol, TypeSymbol> Substitution(TraitReference reference)
    {
        var substitution = new Dictionary<TypeParameterSymbol, TypeSymbol> { [reference.Trait.SelfParameter] = reference.Self };
        for (int i = 0; i < reference.Trait.TypeParameters.Length && i < reference.Arguments.Length; i++) substitution[reference.Trait.TypeParameters[i]] = reference.Arguments[i];
        return substitution;
    }
    private bool Unify(TraitReference left, TraitReference right)
        => ReferenceEquals(left.Trait, right.Trait) && left.Arguments.Length == right.Arguments.Length && Unify(left.Self, right.Self) && left.Arguments.Zip(right.Arguments).All(p => Unify(p.First, p.Second));
    // A variable for speculative instantiation; it is never defaulted or reported
    internal TypeSymbol Probing(TypeParameterSymbol parameter) => new InferenceTypeSymbol(InferenceKind.Any, parameter.Declaration ?? (SyntaxNode?)parameter.ContainingSymbol?.Declaration ?? Crate.Declaration!);

    internal ImmutableArray<Predicate> Elaborate(IEnumerable<Predicate> predicates)
    {
        var result = ImmutableArray.CreateBuilder<Predicate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<Predicate>(predicates);
        while (queue.Count != 0 && result.Count < 4096)
        {
            Predicate predicate = queue.Dequeue();
            if (!seen.Add(predicate.ToString()!)) continue;
            result.Add(predicate);
            if (predicate is not TraitPredicate { Trait: TraitReference reference }) continue;
            Dictionary<TypeParameterSymbol, TypeSymbol> substitution = Substitution(reference);
            foreach (Predicate inherited in reference.Trait.Predicates)
                if (IsSuper(inherited, reference.Trait)) queue.Enqueue(Types.Substitute(inherited, substitution));
            foreach (AssociatedTypeSymbol item in reference.Trait.AssociatedTypes)
                foreach (Predicate bound in item.Bounds) queue.Enqueue(Types.Substitute(bound, substitution));
        }
        return result.ToImmutable();
    }
    private static bool IsSuper(Predicate predicate, TraitSymbol trait) => predicate switch
    {
        TraitPredicate p => p.Trait.Self is ParameterTypeSymbol { Parameter: var self } && ReferenceEquals(self, trait.SelfParameter),
        ProjectionPredicate p => p.Projection.SelfType is ParameterTypeSymbol { Parameter: var self } && ReferenceEquals(self, trait.SelfParameter),
        _ => false
    };

    private List<Candidate> AssembleCandidates(TraitReference goal, ParamEnv env, out bool ambiguous)
    {
        var candidates = new List<Candidate>();
        TypeSymbol self = goal.Self.Resolve();
        ambiguous = self is InferenceTypeSymbol { Constraint: InferenceKind.Any };
        if (ambiguous) return candidates;
        foreach (Predicate predicate in env.Predicates)
        {
            if (predicate is not TraitPredicate { Trait: TraitReference bound } || !ReferenceEquals(bound.Trait, goal.Trait)) continue;
            if (!Probe(() => Unify(goal, bound))) continue;
            if (candidates.OfType<EnvironmentCandidate>().Any(c => TypeSystem.Same(c.Bound.Self, bound.Self) && TypeSystem.SameList(c.Bound.Arguments, bound.Arguments))) continue;
            candidates.Add(new EnvironmentCandidate(bound));
        }
        if (candidates.Count != 0) return candidates;
        if (Builtin(goal, self, out BuiltinCandidate? builtin))
        {
            if (builtin is not null) candidates.Add(builtin);
            return candidates;
        }
        foreach (ImplSymbol impl in ImplCandidates(goal.Trait, TypeHead(self)))
            if (Probe(() => Unify(goal, Types.Substitute(impl.Trait!, Instantiate(impl, Probing))))) candidates.Add(new ImplCandidate(impl));
        return candidates;
    }
    // Sized has no impls; Copy and Clone of tuples, arrays, function pointers and ! are structural as in rustc
    private bool Builtin(TraitReference goal, TypeSymbol self, out BuiltinCandidate? candidate)
    {
        candidate = null;
        if (ReferenceEquals(goal.Trait, SizedTrait))
        {
            if (IsSized(self) && self is not ParameterTypeSymbol { Parameter.IsMaybeSized: true }) candidate = new BuiltinCandidate([]);
            return true;
        }
        if (!ReferenceEquals(goal.Trait, CopyTrait) && !ReferenceEquals(goal.Trait, CloneTrait)) return false;
        switch (self)
        {
            case TupleTypeSymbol tuple: candidate = new BuiltinCandidate(tuple.Elements.Select(e => goal with { Self = e }).ToImmutableArray()); return true;
            case SequenceTypeSymbol { Kind: TypeKind.Array } array: candidate = new BuiltinCandidate([goal with { Self = array.ElementType }]); return true;
            case FunctionTypeSymbol or PrimitiveTypeSymbol { Kind: TypeKind.Never }: candidate = new BuiltinCandidate([]); return true;
        }
        return false;
    }

    private List<Predicate>? ConfirmCandidate(Candidate candidate, TraitReference goal, Func<TypeParameterSymbol, TypeSymbol> fresh)
    {
        switch (candidate)
        {
            case EnvironmentCandidate environment: return Unify(goal, environment.Bound) ? [] : null;
            case BuiltinCandidate builtin: return builtin.Nested.Select(r => (Predicate)new TraitPredicate(r)).ToList();
            case ImplCandidate { Impl: var impl }:
            {
                Dictionary<TypeParameterSymbol, TypeSymbol> substitution = Instantiate(impl, fresh);
                if (!Unify(goal, Types.Substitute(impl.Trait!, substitution))) return null;
                var nested = impl.Predicates.Select(p => Types.Substitute(p, substitution)).ToList();
                if (SizedTrait is not null)
                    foreach (TypeParameterSymbol parameter in impl.TypeParameters)
                        if (!parameter.IsMaybeSized) nested.Add(new TraitPredicate(new TraitReference(SizedTrait, substitution[parameter], [])));
                return nested;
            }
        }
        return null;
    }
    private Candidate? ChooseCandidate(List<Candidate> candidates, TraitReference goal, ParamEnv env, int depth, out Evaluation result)
    {
        result = Evaluation.Holds;
        if (candidates.Count == 1) return candidates[0];
        if (candidates.Count == 0) { result = Evaluation.Fails; return null; }
        var viable = candidates.Where(c => Probe(() => EvaluateCandidate(c, goal, env, depth)) != Evaluation.Fails).ToList();
        if (viable.Count == 1) return viable[0];
        result = viable.Count == 0 ? Evaluation.Fails : Evaluation.Ambiguous;
        return null;
    }

    internal Evaluation Evaluate(Predicate predicate, ParamEnv env) => Probe(() => EvaluateInner(predicate, env, 0));
    private Evaluation EvaluateInner(Predicate predicate, ParamEnv env, int depth)
    {
        if (depth > ObligationDepthLimit) return Evaluation.Ambiguous;
        switch (predicate)
        {
            case TraitPredicate { Trait: var goal }:
            {
                if (Contains(goal, t => t.IsError)) return Evaluation.Holds;
                List<Candidate> candidates = AssembleCandidates(goal, env, out bool ambiguous);
                if (ambiguous) return Evaluation.Ambiguous;
                Candidate? chosen = ChooseCandidate(candidates, goal, env, depth, out Evaluation result);
                return chosen is null ? result : EvaluateCandidate(chosen, goal, env, depth);
            }
            case ProjectionPredicate projection:
            {
                var nested = new List<Predicate>();
                TypeSymbol? value = Project(projection.Projection, env, nested, Probing, depth);
                if (value is null) return Evaluation.Ambiguous;
                if (!Unify(value, projection.Type)) return Evaluation.Fails;
                return EvaluateNested(nested, env, depth);
            }
        }
        return Evaluation.Holds;
    }
    private Evaluation EvaluateCandidate(Candidate candidate, TraitReference goal, ParamEnv env, int depth)
        => ConfirmCandidate(candidate, goal, Probing) is List<Predicate> nested ? EvaluateNested(nested, env, depth) : Evaluation.Fails;
    private Evaluation EvaluateNested(List<Predicate> nested, ParamEnv env, int depth)
    {
        Evaluation result = Evaluation.Holds;
        foreach (Predicate predicate in nested)
        {
            Evaluation evaluation = EvaluateInner(predicate, env, depth + 1);
            if (evaluation == Evaluation.Fails) return Evaluation.Fails;
            if (evaluation == Evaluation.Ambiguous) result = Evaluation.Ambiguous;
        }
        return result;
    }

    // The value of a projection, or null while selecting its impl must wait for inference
    internal TypeSymbol? Project(ProjectionTypeSymbol projection, ParamEnv env, List<Predicate> nested, Func<TypeParameterSymbol, TypeSymbol> fresh, int depth = 0)
    {
        TraitReference goal = projection.Trait;
        if (Contains(goal, t => t.IsError)) return Types.Error;
        List<Candidate> candidates = AssembleCandidates(goal, env, out bool ambiguous);
        if (ambiguous) return null;
        Candidate? chosen = ChooseCandidate(candidates, goal, env, depth, out Evaluation result);
        if (chosen is null)
        {
            if (result == Evaluation.Ambiguous) return null;
            nested.Add(new TraitPredicate(goal));
            return Types.Error;
        }
        switch (chosen)
        {
            case ImplCandidate { Impl: var impl }:
            {
                Dictionary<TypeParameterSymbol, TypeSymbol> substitution = Instantiate(impl, fresh);
                if (!Unify(goal, Types.Substitute(impl.Trait!, substitution))) return Types.Error;
                nested.AddRange(impl.Predicates.Select(p => Types.Substitute(p, substitution)));
                return impl.AssociatedTypes.TryGetValue(projection.Item, out TypeSymbol? value) ? Types.Substitute(value, substitution) : Types.Error;
            }
            case EnvironmentCandidate environment:
                Unify(goal, environment.Bound);
                foreach (ProjectionPredicate equality in env.Predicates.OfType<ProjectionPredicate>())
                {
                    if (!ReferenceEquals(equality.Projection.Item, projection.Item) || !Probe(() => Unify(equality.Projection, projection))) continue;
                    Unify(equality.Projection, projection);
                    return equality.Type;
                }
                return projection;
        }
        return projection;
    }

    internal void RegisterObligation(Predicate predicate, ParamEnv env, SyntaxNode syntax, SyntaxTree tree) => _obligations.Add(new Obligation(predicate, env, syntax, tree, 0));
    internal void SelectWherePossible()
    {
        bool progress;
        do
        {
            progress = false;
            for (int i = 0; i < _obligations.Count; i++)
            {
                Obligation obligation = _obligations[i];
                Evaluation result = ProcessObligation(obligation, out List<Predicate>? nested);
                if (result == Evaluation.Ambiguous) continue;
                _obligations.RemoveAt(i--);
                progress = true;
                if (result == Evaluation.Fails) { ReportUnsatisfied(obligation); continue; }
                foreach (Predicate predicate in nested!)
                {
                    if (obligation.Depth >= ObligationDepthLimit)
                    {
                        Error(obligation.Tree, obligation.Syntax, $"Overflow evaluating the requirement '{predicate}'.", "RTRA0275");
                        break;
                    }
                    _obligations.Add(obligation with { Predicate = predicate, Depth = obligation.Depth + 1 });
                }
            }
        } while (progress);
    }
    private Evaluation ProcessObligation(Obligation obligation, out List<Predicate>? nested)
    {
        nested = null;
        TypeSymbol Fill(TypeParameterSymbol _) => Fresh(obligation.Syntax);
        switch (obligation.Predicate)
        {
            case TraitPredicate { Trait: var goal }:
            {
                if (Contains(goal, t => t.IsError)) { nested = []; return Evaluation.Holds; }
                List<Candidate> candidates = AssembleCandidates(goal, obligation.Env, out bool ambiguous);
                if (ambiguous) return Evaluation.Ambiguous;
                Candidate? chosen = ChooseCandidate(candidates, goal, obligation.Env, obligation.Depth, out Evaluation result);
                if (chosen is null) return result;
                nested = ConfirmCandidate(chosen, goal, Fill);
                return nested is null ? Evaluation.Fails : Evaluation.Holds;
            }
            case ProjectionPredicate projection:
            {
                nested = new List<Predicate>();
                TypeSymbol? value = Project(projection.Projection, obligation.Env, nested, Fill, obligation.Depth);
                if (value is null) return Evaluation.Ambiguous;
                return Unify(value, projection.Type) ? Evaluation.Holds : Evaluation.Fails;
            }
        }
        nested = [];
        return Evaluation.Holds;
    }
    private void ReportUnsatisfied(Obligation obligation)
    {
        if (obligation.Predicate is TraitPredicate { Trait: var goal })
            Error(obligation.Tree, obligation.Syntax, $"The trait bound '{Zonk(goal)}' is not satisfied.", "RTRA0277");
        else if (obligation.Predicate is ProjectionPredicate projection)
            Error(obligation.Tree, obligation.Syntax, $"Type mismatch resolving '{Zonk(projection.Projection)} == {Zonk(projection.Type)}'.", "RTRA0271");
    }
    private void ReportAmbiguities()
    {
        foreach (Obligation obligation in _obligations)
        {
            bool erroneous = obligation.Predicate switch
            {
                TraitPredicate p => Contains(p.Trait, t => t.IsError),
                ProjectionPredicate p => Contains(p.Projection, t => t.IsError) || Contains(p.Type, t => t.IsError),
                _ => true
            };
            if (!erroneous) Error(obligation.Tree, obligation.Syntax, $"Type annotations needed: cannot satisfy '{obligation.Predicate}'.", "RTRA0283");
        }
        _obligations.Clear();
    }
    private TraitReference Zonk(TraitReference reference) => new(reference.Trait, Zonk(reference.Self), reference.Arguments.Select(Zonk).ToImmutableArray());
    private static bool Contains(TraitReference reference, Func<TypeSymbol, bool> predicate)
        => Contains(reference.Self, predicate) || reference.Arguments.Any(a => Contains(a, predicate));

    internal bool Holds(TraitSymbol? trait, TypeSymbol type, ParamEnv env)
        => trait is not null && Evaluate(new TraitPredicate(new TraitReference(trait, type, [])), env) == Evaluation.Holds;
    // Primitive, shared reference and raw pointer Copy comes from core's impls; this mirrors them without selection
    internal bool IsCopy(TypeSymbol type, ParamEnv env)
    {
        type = type.Resolve();
        switch (type)
        {
            case PrimitiveTypeSymbol primitive: return primitive.Kind != TypeKind.String;
            case IndirectTypeSymbol indirect: return indirect.Kind == TypeKind.Pointer || !indirect.IsMutable;
            case FunctionTypeSymbol or InferenceTypeSymbol: return true;
            case TupleTypeSymbol tuple when CopyTrait is null: return tuple.Elements.All(e => IsCopy(e, env));
            case SequenceTypeSymbol { Kind: TypeKind.Array } array when CopyTrait is null: return IsCopy(array.ElementType, env);
        }
        return Holds(CopyTrait, type, env);
    }

    // Resolves a trait method applied to concrete types to the impl method that codegen calls
    internal (FunctionSymbol Function, ImmutableArray<TypeSymbol> Arguments) ResolveInstance(FunctionSymbol function, ImmutableArray<TypeSymbol> arguments)
    {
        if (function.Trait is not TraitSymbol trait || arguments.Length <= trait.TypeParameters.Length) return (function, arguments);
        int parents = trait.TypeParameters.Length + 1;
        var goal = new TraitReference(trait, arguments[0], arguments[1..parents]);
        return Probe(() =>
        {
            List<Candidate> candidates = AssembleCandidates(goal, ParamEnv.Empty, out _);
            if (candidates is not [ImplCandidate { Impl: var impl }]) return (function, arguments);
            Dictionary<TypeParameterSymbol, TypeSymbol> substitution = Instantiate(impl, Probing);
            if (!Unify(goal, Types.Substitute(impl.Trait!, substitution)) || impl.Methods.FirstOrDefault(m => m.Name == function.Name) is not FunctionSymbol method)
                return (function, arguments);
            return (method, impl.TypeParameters.Select(p => Zonk(substitution[p])).Concat(arguments[parents..]).ToImmutableArray());
        });
    }

    private bool Overlap(ImplSymbol first, ImplSymbol second) => Probe(() =>
    {
        Dictionary<TypeParameterSymbol, TypeSymbol> left = Instantiate(first, Probing), right = Instantiate(second, Probing);
        if (!Unify(Types.Substitute(first.Trait!, left), Types.Substitute(second.Trait!, right))) return false;
        return first.Predicates.Select(p => Types.Substitute(p, left)).Concat(second.Predicates.Select(p => Types.Substitute(p, right)))
            .All(p => EvaluateInner(p, ParamEnv.Empty, 0) != Evaluation.Fails);
    });
    private bool SameInherentSelf(ImplSymbol first, ImplSymbol second)
        => Probe(() => Unify(Types.Substitute(first.SelfType, Instantiate(first, Probing)), Types.Substitute(second.SelfType, Instantiate(second, Probing))));
    internal SemanticData Data(SyntaxTree tree) => _compilation.GetSemanticModel(tree).Data;
    internal void Error(SyntaxTree tree, SyntaxNode syntax, string message, string code = "RSEM0001")
    {
        SemanticData data = Data(tree);
        data.Diagnostics.Add(new SemanticDiagnostic(code, message, tree, syntax.Span));
        data.ErrorNodes.Add(syntax);
    }
    internal void Unsupported(SyntaxTree tree, SyntaxNode syntax, string feature)
        => Error(tree, syntax, $"{feature} is not supported by semantic binding yet.", "RSEM0002");
    private static string AttributeName(AttributeSyntax attribute) => attribute.Path?.Segments.FirstOrDefault()?.Identifier.ValueText ?? "";

    internal MacroScope? MacrosFor(SyntaxNode? item) => item is null ? _macroScope : _itemMacros.TryGetValue(item, out MacroScope? scope) ? scope : _macroScope;
    internal MacroScope? DefineMacro(MacroDefinitionItemSyntax syntax, SyntaxTree tree, MacroScope? scope)
    {
        if (syntax.Body is not TokenTreeSyntax body) return scope;
        string name = Name(syntax.Identifier);
        BuiltinMacro builtin = BuiltinMacro.None;
        if (syntax.Attributes.FirstOrDefault(a => AttributeName(a) == "rustc_builtin_macro") is AttributeSyntax marker)
        {
            string expander = marker.Input?.ChildTokens().Where(t => t.Kind == SyntaxKind.IdentifierToken).Select(Name).FirstOrDefault() ?? name;
            if (!MacroExpander.Builtins.TryGetValue(expander, out builtin)) { Error(tree, marker, $"Cannot find a built-in macro with name '{expander}'.", "RMAC0001"); return scope; }
        }
        MacroDefinition definition;
        try { definition = new MacroDefinition(name, MacroExpander.ParseRules(body, tree.Text), Crate, builtin); }
        catch (MacroException exception) { Error(tree, syntax, exception.Message, "RMAC0001"); return scope; }
        if (syntax.Attributes.Any(a => AttributeName(a) == "macro_export"))
        {
            var exported = new MacroSymbol(definition, Crate, syntax, tree) { IsPublic = true };
            AddMember(Crate, exported, SymbolNamespace.Macro);
            Data(tree).Declarations[syntax] = exported;
        }
        return new MacroScope(definition, scope);
    }
    private MacroDefinition? ResolveMacro(MacroExpressionSyntax invocation, MacroScope? scope, SyntaxTree tree, ModuleSymbol module)
    {
        PathSegmentSyntax[] segments = invocation.Path?.Segments.ToArray() ?? [];
        Symbol? symbol = null;
        if (segments.Length == 1 && !IsGlobal(invocation.Path!))
        {
            string name = Name(segments[0].Identifier);
            if (MacroScope.Find(scope, name) is MacroDefinition local) return local;
            ImmutableArray<Symbol> found = module.Scope.Find(name, SymbolNamespace.Macro);
            if (found.Length == 1) symbol = Unwrap(found[0]);
        }
        else if (segments.Length > 1 && ResolvePath(invocation.Path, module.Scope, tree, SymbolNamespace.Macro, diagnose: false) is not null)
            symbol = ResolvePath(invocation.Path, module.Scope, tree, SymbolNamespace.Macro);
        if (symbol is MacroSymbol macro)
        {
            RecordSymbol(tree, invocation, macro);
            return macro.Definition;
        }
        Error(tree, invocation, $"Cannot find macro '{string.Join("::", segments.Select(s => Name(s.Identifier)))}' in this scope.", "RMAC0002");
        return null;
    }
    private static bool IsGlobal(PathSyntax path) => path.Children.FirstOrDefault().IsToken && path.Children[0].Token.Kind == SyntaxKind.ColonColonToken;
    // Expansion marks index the crate of the macro that produced the tokens, which is where their $crate refers
    private const string DollarCrate = "$crate\u0001";
    private ModuleSymbol? CrateOfMark(int mark) => mark > 0 && mark <= _expansionCrates.Count ? _expansionCrates[mark - 1] : null;
    internal MacroExpansion? Expand(MacroExpressionSyntax invocation, MacroScope? scope, SyntaxTree tree, ModuleSymbol module)
    {
        MacroDefinition? definition = ResolveMacro(invocation, scope, tree, module);
        if (definition is null || invocation.Body is not TokenTreeSyntax body) return null;
        if (ExpansionDepth >= 128) { Error(tree, invocation, $"Recursion limit reached while expanding '{definition.Name}!'.", "RMAC0003"); return null; }
        var input = new MacroInput(MacroExpander.Tokens(body, tree.Text, false).ToImmutableArray());
        _expansionCrates.Add(definition.Crate);
        int mark = _expansionCrates.Count;
        try
        {
            if (definition.Builtin != BuiltinMacro.None)
                return new MacroExpansion(definition, _expander.ExpandBuiltin(definition.Builtin, input, new BuiltinContext(tree, invocation.Span, mark, module.QualifiedName, _compilation.Options.PointerWidth)));
            var match = _expander.Match(definition, input);
            if (match is null) { Error(tree, invocation, $"No rules of macro '{definition.Name}!' match this invocation.", "RMAC0004"); return null; }
            return new MacroExpansion(definition, MacroExpander.Transcribe(match.Value.Rule, match.Value.Bindings, invocation.Span, mark));
        }
        catch (MacroException exception) { Error(tree, invocation, exception.Message, "RMAC0001"); return null; }
    }
    private Parser? Parsed(Parser parser, SyntaxNode? result, SyntaxNode invocation, SyntaxTree tree, bool complete = true)
    {
        foreach (SyntaxDiagnostic diagnostic in parser.Diagnostics) Error(tree, invocation, "Invalid macro expansion: " + diagnostic.Message, "RMAC0005");
        if (parser.Diagnostics.Count == 0 && complete && !parser.AtEnd) Error(tree, invocation, "The macro expansion has trailing tokens.", "RMAC0005");
        if (parser.Diagnostics.Count != 0 || complete && !parser.AtEnd) return null;
        if (result is not null) Register(result, invocation, tree);
        return parser;
    }
    private void Register(SyntaxNode expansion, SyntaxNode invocation, SyntaxTree tree)
    {
        SemanticData data = Data(tree);
        data.Parents[expansion] = invocation;
        foreach (SyntaxNode node in expansion.DescendantNodes().Prepend(expansion))
            foreach (SyntaxNode child in node.ChildNodes()) data.Parents[child] = node;
    }
    internal ExpressionSyntax? ExpandExpression(MacroExpressionSyntax invocation, MacroScope? scope, SyntaxTree tree, ModuleSymbol module)
    {
        if (Expand(invocation, scope, tree, module) is not MacroExpansion value) return null;
        var parser = new Parser(value.Tokens, RustEdition.Rust2024);
        ExpressionSyntax expression = parser.ParseExpression();
        return Parsed(parser, expression, invocation, tree) is null ? null : expression;
    }
    internal BlockExpressionSyntax? ExpandStatements(MacroExpressionSyntax invocation, MacroScope? scope, SyntaxTree tree, ModuleSymbol module)
    {
        if (Expand(invocation, scope, tree, module) is not MacroExpansion value) return null;
        ImmutableArray<SyntaxToken> tokens = value.Tokens.Insert(0, MacroExpander.Punctuation(SyntaxKind.OpenBraceToken, "{", invocation.Span, 0))
            .Add(MacroExpander.Punctuation(SyntaxKind.CloseBraceToken, "}", invocation.Span, 0));
        var parser = new Parser(tokens, RustEdition.Rust2024);
        BlockExpressionSyntax block = parser.ParseBlockExpression();
        return Parsed(parser, block, invocation, tree) is null ? null : block;
    }
    private ImmutableArray<ItemSyntax> ExpandItems(MacroExpressionSyntax invocation, MacroScope? scope, SyntaxTree tree, ModuleSymbol module)
    {
        if (Expand(invocation, scope, tree, module) is not MacroExpansion expansion) return [];
        var (definition, tokens) = expansion;
        if (definition.Builtin != BuiltinMacro.None) { Error(tree, invocation, $"Macro '{definition.Name}!' cannot be used in item position.", "RMAC0006"); return []; }
        var parser = new Parser(tokens, RustEdition.Rust2024);
        ImmutableArray<ItemSyntax> items = parser.ParseItems();
        if (Parsed(parser, null, invocation, tree) is null) return [];
        foreach (ItemSyntax item in items) Register(item, invocation, tree);
        return items;
    }
    internal TypeSyntax? ExpandType(MacroExpressionSyntax invocation, MacroScope? scope, SyntaxTree tree, ModuleSymbol module)
    {
        if (Expand(invocation, scope, tree, module) is not MacroExpansion expansion) return null;
        var parser = new Parser(expansion.Tokens, RustEdition.Rust2024);
        TypeSyntax type = parser.ParseType();
        return Parsed(parser, type, invocation, tree) is null ? null : type;
    }
    internal PatternSyntax? ExpandPattern(MacroExpressionSyntax invocation, MacroScope? scope, SyntaxTree tree, ModuleSymbol module)
    {
        if (Expand(invocation, scope, tree, module) is not MacroExpansion expansion) return null;
        var parser = new Parser(expansion.Tokens, RustEdition.Rust2024);
        PatternSyntax pattern = parser.ParsePatternFragment(true);
        return Parsed(parser, pattern, invocation, tree) is null ? null : pattern;
    }
    internal SyntaxTree TreeOf(SyntaxNode node) => _compilation.FindTree(node) ?? throw new InvalidOperationException("Syntax node belongs to no compilation.");
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
        BindCrateAttributes(_compilation.RootSyntaxTree);
        Declare(Crate, _compilation.RootSyntaxTree.Root, _compilation.RootSyntaxTree);
        AddScope(Crate.Scope);
        DeclareItems(_compilation.RootSyntaxTree.Root.Items, Crate, _compilation.RootSyntaxTree, "");
        foreach (var entry in _visibility.ToArray()) BindVisibility(entry.Symbol, entry.Visibility);
        foreach (TraitSymbol trait in _declarations.OfType<TraitSymbol>())
            foreach (Symbol item in trait.Methods.Cast<Symbol>().Concat(trait.AssociatedTypes)) { item.IsPublic = trait.IsPublic; item.AccessibleWithin = trait.AccessibleWithin; }
        BindImports();
        SizedTrait = FindLangItem("sized") as TraitSymbol;
        CopyTrait = FindLangItem("copy") as TraitSymbol;
        CloneTrait = FindLangItem("clone") as TraitSymbol;
        foreach (var entry in _impls) BindImplHeader(entry.Syntax, entry.Module, entry.Tree);
        foreach (Symbol symbol in _declarations.ToArray()) BindSignature(symbol);
        foreach (ImplSymbol impl in _implItems.Keys.ToArray()) BindImplItems(impl);
        CheckImpls();
        foreach (GlobalSymbol global in _declarations.OfType<GlobalSymbol>().ToArray()) BindGlobal(global);
        BindDiscriminants();
        foreach (FunctionSymbol function in _declarations.OfType<FunctionSymbol>().ToArray())
            _functions.Add(new BodyBinder(this, function.Scope, function).BindFunction());
        // Integer and float variables fall back to their defaults only after the obligations had their chance to constrain them
        SelectWherePossible();
        foreach (InferenceTypeSymbol inference in _inferences)
        {
            if (inference.Resolve() is not InferenceTypeSymbol unresolved) continue;
            if (unresolved.Constraint == InferenceKind.Any && unresolved.DefaultType is TypeSymbol defaultType) unresolved.Binding = defaultType;
            else if (unresolved.Constraint == InferenceKind.Integer) unresolved.Binding = Types.I32;
            else if (unresolved.Constraint == InferenceKind.Float) unresolved.Binding = Types.F64;
            else if (unresolved.HasNeverConstraint) unresolved.Binding = Types.Never;
        }
        SelectWherePossible();
        foreach (InferenceTypeSymbol inference in _inferences)
        {
            if (inference.Resolve() is not InferenceTypeSymbol unresolved) continue;
            Error(TreeOf(unresolved.Origin), unresolved.Origin, "Type cannot be inferred; add a type annotation.");
            unresolved.Binding = Types.Error;
        }
        SelectWherePossible();
        ReportAmbiguities();
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
        }
        BorrowChecker.Check(this, _functions);
        foreach (SyntaxTree tree in _compilation.SyntaxTrees)
        {
            SemanticData data = Data(tree);
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
            CheckAttributes(item.Attributes, tree, item is FunctionItemSyntax, item is FunctionItemSyntax or TraitItemSyntax);
            _itemMacros[item] = _macroScope;
            string name = Name(item.Identifier);
            Symbol? symbol = null;
            switch (item)
            {
                case TraitItemSyntax syntax:
                {
                    if (Has(syntax, SyntaxKind.UnsafeKeyword)) Unsupported(tree, syntax, "Unsafe traits");
                    if (Has(syntax, SyntaxKind.EqualsToken)) { Unsupported(tree, syntax, "Trait aliases"); break; }
                    var trait = new TraitSymbol(name, module, syntax, tree);
                    symbol = trait;
                    AddMember(module, trait, SymbolNamespace.Type);
                    if (syntax.Attributes.FirstOrDefault(a => AttributeName(a) == "lang") is AttributeSyntax traitLang && LangItem(traitLang, tree) is string traitItem && !LangItems.TryAdd(traitItem, trait))
                        Error(tree, traitLang, $"Duplicate lang item '{traitItem}'.");
                    trait.TypeParameters = DeclareGenerics(syntax, trait, trait.Scope, tree, out ImmutableArray<LifetimeSymbol> traitLifetimes);
                    trait.LifetimeParameters = traitLifetimes;
                    DeclareTraitItems(trait, syntax, module, tree);
                    break;
                }
                case ModuleItemSyntax syntax:
                {
                    var nested = new ModuleSymbol(name, module, syntax, tree, _externPrelude);
                    symbol = nested;
                    AddMember(module, nested, SymbolNamespace.Type);
                    string path = modulePath.Length == 0 ? name : modulePath + "::" + name;
                    MacroScope? macros = _macroScope;
                    if (syntax.Body is ItemListSyntax body)
                    {
                        AddScope(nested.Scope);
                        DeclareItems(body.Items, nested, tree, path);
                    }
                    else if (_compilation.ModuleSources.TryGetValue(path, out SyntaxTree? source) && _usedTrees.Add(source))
                    {
                        var sourceScope = new BindingScope(_externPrelude, nested, source, source.Root.Span);
                        // Both files declare the same module; entries are copied after imports are resolved.
                        AddScope(sourceScope);
                        DeclareItems(source.Root.Items, nested, source, path);
                        Data(source).Declarations[source.Root] = nested;
                    }
                    else Error(tree, syntax, $"No unique source supplied for module '{path}'.");
                    if (!syntax.Attributes.Any(a => AttributeName(a) == "macro_use")) _macroScope = macros;
                    break;
                }
                case FunctionItemSyntax syntax:
                    symbol = new FunctionSymbol(name, module, syntax, tree, module.Scope, Types.Error) { IsIntrinsic = syntax.Attributes.Any(a => AttributeName(a) == "rustc_intrinsic") };
                    AddMember(module, symbol, SymbolNamespace.Value);
                    if (syntax.Attributes.FirstOrDefault(a => AttributeName(a) == "lang") is AttributeSyntax lang && LangItem(lang, tree) is string langItem && !LangItems.TryAdd(langItem, (FunctionSymbol)symbol))
                        Error(tree, lang, $"Duplicate lang item '{langItem}'.");
                    break;
                case ExternCrateItemSyntax external:
                {
                    string crateName = Name(external.Identifier);
                    ModuleSymbol? target = crateName == "self" ? Crate : _externCrates.GetValueOrDefault(crateName);
                    string alias = external.Alias.Kind == SyntaxKind.None ? crateName : Name(external.Alias);
                    if (target is null) Error(tree, external, $"Cannot find crate '{crateName}'.");
                    else if (alias == "self") Error(tree, external, "'extern crate self' requires an alias.");
                    else if (alias != "_")
                    {
                        symbol = new ImportSymbol(alias, target, module, external, tree, SymbolNamespace.Type, false);
                        AddUnique(module.Scope, alias, SymbolNamespace.Type, symbol, external);
                        module.Add(symbol); RecordSymbol(tree, external, target);
                    }
                    break;
                }
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
                case MacroDefinitionItemSyntax definition: _macroScope = DefineMacro(definition, tree, _macroScope); break;
                case MacroInvocationItemSyntax { Invocation: MacroExpressionSyntax invocation }:
                {
                    ImmutableArray<ItemSyntax> expanded = ExpandItems(invocation, _macroScope, tree, module);
                    ExpansionDepth++;
                    DeclareItems(expanded, module, tree, modulePath);
                    ExpansionDepth--;
                    break;
                }
                case MissingItemSyntax: break;
                default: Unsupported(tree, item, item.Kind.ToString()); break;
            }
            if (symbol is null) continue;
            Declare(symbol, item, tree);
            symbol.AccessibleWithin = module;
            _visibility.Add((symbol, item.Visibility));
            switch (symbol)
            {
                case AggregateSymbol a:
                    a.TypeParameters = DeclareGenerics(item, a, a.Scope, tree, out ImmutableArray<LifetimeSymbol> lifetimes);
                    a.LifetimeParameters = lifetimes;
                    a.Type = new NamedTypeSymbol(a, a.TypeParameters.Select(p => p.Type).ToImmutableArray(), lifetimes);
                    break;
                case FunctionSymbol f: f.TypeParameters = DeclareGenerics(item, f, f.Scope, tree, out _); break;
                case TypeAliasSymbol a: a.TypeParameters = DeclareGenerics(item, a, a.Scope, tree, out _); break;
            }
        }
    }
    private IEnumerable<ItemSyntax> ExpandMembers(IEnumerable<ItemSyntax> items, MacroScope? macros, ModuleSymbol module, SyntaxTree tree)
    {
        foreach (ItemSyntax item in items)
        {
            if (item is not MacroInvocationItemSyntax { Invocation: MacroExpressionSyntax invocation }) { yield return item; continue; }
            ImmutableArray<ItemSyntax> expanded = ExpandItems(invocation, macros, tree, module);
            ExpansionDepth++;
            foreach (ItemSyntax member in ExpandMembers(expanded, macros, module, tree)) yield return member;
            ExpansionDepth--;
        }
    }
    private void DeclareTraitItems(TraitSymbol trait, TraitItemSyntax syntax, ModuleSymbol module, SyntaxTree tree)
    {
        var methods = ImmutableArray.CreateBuilder<FunctionSymbol>();
        var types = ImmutableArray.CreateBuilder<AssociatedTypeSymbol>();
        foreach (ItemSyntax item in ExpandMembers(syntax.Body?.Items ?? [], _macroScope, module, tree).ToArray())
        {
            CheckAttributes(item.Attributes, tree, item is FunctionItemSyntax);
            if (item.Visibility is not null) Error(tree, item.Visibility, "Visibility qualifiers are not permitted on trait items.");
            string name = Name(item.Identifier);
            switch (item)
            {
                case FunctionItemSyntax method:
                {
                    _itemMacros[method] = _macroScope;
                    var function = new FunctionSymbol(name, trait, method, tree, trait.Scope, Types.Error)
                    { SelfType = trait.SelfParameter.Type, AccessibleWithin = module, ParentTypeParameters = trait.Generics };
                    if (methods.Any(m => m.Name == name)) Error(tree, method, $"'{name}' is already defined in this trait.");
                    Declare(function, method, tree);
                    function.TypeParameters = DeclareGenerics(method, function, function.Scope, tree, out _);
                    methods.Add(function);
                    break;
                }
                case TypeAliasItemSyntax alias:
                    if (alias.GenericParameters is not null || alias.WhereClause is not null) Unsupported(tree, alias, "Generic associated types");
                    if (alias.Value is not null) Unsupported(tree, alias.Value, "Associated type defaults");
                    if (types.Any(t => t.Name == name)) Error(tree, alias, $"'{name}' is already defined in this trait.");
                    var associated = new AssociatedTypeSymbol(name, trait, alias, tree) { AccessibleWithin = module };
                    Declare(associated, alias, tree);
                    types.Add(associated);
                    break;
                case ConstItemSyntax: Unsupported(tree, item, "Associated constants"); break;
                case MissingItemSyntax: break;
                default: Unsupported(tree, item, item.Kind.ToString()); break;
            }
        }
        trait.Methods = methods.ToImmutable();
        trait.AssociatedTypes = types.ToImmutable();
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
        if (scope.Named(name).Any(e => e.Namespace == space && e.Symbol is not ImportSymbol { IsGlob: true }))
            Error(symbol.SyntaxTree ?? scope.Tree, syntax, $"'{name}' is already defined in the {space.ToString().ToLowerInvariant()} namespace.");
        scope.Add(name, space, symbol);
    }
    internal void CheckAttributes(IEnumerable<AttributeSyntax> attributes, SyntaxTree tree, bool function = false, bool lang = false)
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
            if (GatedAttributes.TryGetValue(name, out string? feature))
            {
                if (!_features.Contains(feature)) Error(tree, attribute, $"The '{name}' attribute requires #![feature({feature})].");
                if (name == "rustc_intrinsic" && !function || name == "lang" && !lang) Unsupported(tree, attribute, $"Attribute '{name}' here");
                continue;
            }
            if (name is not ("doc" or "allow" or "warn" or "deny" or "forbid" or "inline" or "cold" or "must_use" or "macro_export" or "macro_use"))
                Unsupported(tree, attribute, $"Attribute '{name}'");
        }
    }
    private static readonly ImmutableDictionary<string, string> GatedAttributes = new Dictionary<string, string>
    {
        ["lang"] = "lang_items", ["prelude_import"] = "prelude_import", ["rustc_builtin_macro"] = "rustc_attrs", ["rustc_intrinsic"] = "intrinsics"
    }.ToImmutableDictionary(StringComparer.Ordinal);
    private static readonly ImmutableHashSet<string> KnownFeatures = ImmutableHashSet.Create(StringComparer.Ordinal, "intrinsics", "lang_items", "no_core", "prelude_import", "rustc_attrs");
    private void BindCrateAttributes(SyntaxTree tree)
    {
        AttributeSyntax[] attributes = tree.Root.Attributes.ToArray();
        foreach (AttributeSyntax attribute in attributes.Where(a => AttributeName(a) == "feature"))
            foreach (SyntaxToken token in attribute.Input?.ChildTokens().Where(t => t.Kind == SyntaxKind.IdentifierToken) ?? [])
                if (KnownFeatures.Contains(Name(token))) _features.Add(Name(token));
                else Unsupported(tree, attribute, $"Feature '{Name(token)}'");
        foreach (AttributeSyntax attribute in attributes)
        {
            string name = AttributeName(attribute);
            if (name == "no_core" && !_features.Contains(name)) Error(tree, attribute, "The 'no_core' attribute requires #![feature(no_core)].");
            else if (name is not ("feature" or "no_core" or "no_std" or "doc" or "allow" or "warn" or "deny" or "forbid")) Unsupported(tree, attribute, $"Crate attribute '{name}'");
        }
    }
    private string? LangItem(AttributeSyntax attribute, SyntaxTree tree)
    {
        if (attribute.Input is null && (attribute.Value?.Value as LiteralExpressionSyntax)?.Literal.Value is string { Length: > 0 } item) return item;
        Error(tree, attribute, "The lang attribute requires a string value.");
        return null;
    }
    private ImmutableArray<TypeParameterSymbol> DeclareGenerics(ItemSyntax syntax, Symbol symbol, BindingScope scope, SyntaxTree tree, out ImmutableArray<LifetimeSymbol> lifetimes)
    {
        AddScope(scope);
        var parameters = ImmutableArray.CreateBuilder<TypeParameterSymbol>();
        var declared = ImmutableArray.CreateBuilder<LifetimeSymbol>();
        foreach (GenericParameterSyntax parameter in syntax.GenericParameters?.Parameters ?? [])
        {
            string name = Name(parameter.Identifier);
            if (parameter.IsConst) { Unsupported(tree, parameter, "Const generic parameters"); continue; }
            if (parameter.Identifier.Kind is SyntaxKind.LifetimeToken or SyntaxKind.RawLifetimeToken)
            {
                var lifetime = new LifetimeSymbol(name, symbol, parameter, tree);
                Declare(lifetime, parameter, tree); declared.Add(lifetime);
                AddUnique(scope, name, SymbolNamespace.Lifetime, lifetime, parameter);
            }
            else
            {
                var type = new TypeParameterSymbol(name, symbol, parameter, tree, parameters.Count);
                parameters.Add(type); Declare(type, parameter, tree);
                AddUnique(scope, name, SymbolNamespace.Type, type, parameter);
            }
        }
        lifetimes = declared.ToImmutable();
        return parameters.ToImmutable();
    }
    // Bounds on parameters and supertraits come first so that where-clauses over T::Assoc can find the trait declaring Assoc
    private ImmutableArray<Predicate> BindPredicates(ItemSyntax syntax, BindingScope scope, SyntaxTree tree, TypeSymbol? traitSelf, Action<ImmutableArray<Predicate>> publish)
    {
        var predicates = new List<Predicate>();
        foreach (GenericParameterSyntax parameter in syntax.GenericParameters?.Parameters ?? [])
        {
            if (parameter.Bounds is not TypeBoundListSyntax bounds) continue;
            switch (Data(tree).Declarations.GetValueOrDefault(parameter))
            {
                case LifetimeSymbol lifetime: BindLifetimeBounds(lifetime, bounds, scope, tree, predicates); break;
                case TypeParameterSymbol type: BindBounds(type.Type, bounds, scope, tree, predicates, type); break;
            }
        }
        if (traitSelf is not null && syntax is TraitItemSyntax { Bounds: TypeBoundListSyntax supertraits }) BindBounds(traitSelf, supertraits, scope, tree, predicates, null);
        WherePredicateSyntax[] clauses = syntax.WhereClause?.Predicates.ToArray() ?? [];
        bool Simple(WherePredicateSyntax clause) => clause.Type is null || clause.Type is PathTypeSyntax { Path: PathSyntax { Qualification: null } path } && path.Segments.Count() == 1;
        foreach (WherePredicateSyntax clause in clauses.Where(Simple)) BindWhere(clause, scope, tree, predicates);
        publish(predicates.ToImmutableArray());
        foreach (WherePredicateSyntax clause in clauses.Where(c => !Simple(c))) BindWhere(clause, scope, tree, predicates);
        ImmutableArray<Predicate> result = predicates.ToImmutableArray();
        publish(result);
        return result;
    }
    private void BindWhere(WherePredicateSyntax clause, BindingScope scope, SyntaxTree tree, List<Predicate> output)
    {
        if (clause.Binder is not null) { Unsupported(tree, clause.Binder, "Higher-ranked where clauses"); return; }
        if (clause.Type is null)
        {
            SyntaxToken token = clause.ChildTokens().FirstOrDefault(t => t.Kind is SyntaxKind.LifetimeToken or SyntaxKind.RawLifetimeToken);
            if (BindLifetime(token, clause, scope, tree) is LifetimeSymbol longer && clause.Bounds is TypeBoundListSyntax lifetimeBounds)
                BindLifetimeBounds(longer, lifetimeBounds, scope, tree, output);
            return;
        }
        TypeSymbol type = BindType(clause.Type, scope, tree);
        TypeParameterSymbol? parameter = type is ParameterTypeSymbol { Parameter: var declared } && ReferenceEquals(declared.ContainingSymbol, scope.Owner) ? declared : null;
        if (clause.Bounds is TypeBoundListSyntax bounds) BindBounds(type, bounds, scope, tree, output, parameter);
    }
    private void BindLifetimeBounds(LifetimeSymbol longer, TypeBoundListSyntax bounds, BindingScope scope, SyntaxTree tree, List<Predicate> output)
    {
        foreach (TypeBoundSyntax bound in bounds.Bounds)
        {
            if (bound.Lifetime.Kind == SyntaxKind.None) Error(tree, bound, "A lifetime can only be bounded by lifetimes.");
            else if (BindLifetime(bound.Lifetime, bound, scope, tree) is LifetimeSymbol shorter) output.Add(new OutlivesPredicate(longer, shorter));
        }
    }
    private void BindBounds(TypeSymbol bounded, TypeBoundListSyntax bounds, BindingScope scope, SyntaxTree tree, List<Predicate> output, TypeParameterSymbol? parameter)
    {
        foreach (TypeBoundSyntax syntax in bounds.Bounds)
        {
            TypeBoundSyntax bound = syntax;
            while (bound.Path is null && bound.Inner is TypeBoundSyntax inner) bound = inner;
            if (bound.Lifetime.Kind != SyntaxKind.None) continue;
            if (bound.Binder is not null) { Unsupported(tree, bound, "Higher-ranked trait bounds"); continue; }
            if (bound.Path is not PathSyntax path || Has(bound, SyntaxKind.TildeToken) || Has(bound, SyntaxKind.UseKeyword)) { Unsupported(tree, bound, "This kind of bound"); continue; }
            if (BindTraitReference(path, bounded, scope, tree, output) is not TraitReference reference) continue;
            if (!Has(bound, SyntaxKind.QuestionToken)) { output.Add(new TraitPredicate(reference)); continue; }
            if (!ReferenceEquals(reference.Trait, SizedTrait)) Error(tree, bound, "Only 'Sized' can be relaxed with '?'.");
            else if (parameter is null) Error(tree, bound, "'?Sized' is only allowed on type parameters declared by this item.");
            else parameter.IsMaybeSized = true;
        }
    }
    internal TraitReference? BindTraitReference(PathSyntax path, TypeSymbol self, BindingScope scope, SyntaxTree tree, List<Predicate>? projections)
    {
        Symbol? symbol = ResolvePath(path, scope, tree, SymbolNamespace.Type);
        if (symbol is null) return null;
        if (symbol is not TraitSymbol trait) { Error(tree, path, $"'{symbol.Name}' is not a trait."); return null; }
        BindSignature(trait);
        PathSegmentSyntax last = path.Segments.Last();
        if (last.FunctionParameters is not null) Unsupported(tree, last, "Parenthesized trait arguments");
        var substitution = new Dictionary<TypeParameterSymbol, TypeSymbol> { [trait.SelfParameter] = self };
        var arguments = new List<TypeSymbol>();
        var bindings = new List<(GenericArgumentSyntax Syntax, string Name, TypeSyntax Value)>();
        int supplied = 0;
        foreach (GenericArgumentSyntax argument in last.Arguments?.Arguments ?? [])
        {
            if (argument.Binding is EqualsValueClauseSyntax { Value: TypeSyntax value } && argument.Value is PathTypeSyntax { Path: PathSyntax name } && name.Segments.Count() == 1)
                bindings.Add((argument, Name(name.Segments.First().Identifier), value));
            else if (argument.Bounds is not null || argument.Binding is not null) Unsupported(tree, argument, "Associated type constraints");
            else if (argument.Value is TypeSyntax type)
            {
                TypeSymbol bound = BindType(type, scope, tree);
                if (supplied < trait.TypeParameters.Length) { arguments.Add(bound); substitution[trait.TypeParameters[supplied]] = bound; }
                supplied++;
            }
            else if (argument.Value is not null) Unsupported(tree, argument, "Const generic arguments");
        }
        if (supplied > trait.TypeParameters.Length) Error(tree, last, $"Expected {trait.TypeParameters.Length} type arguments, got {supplied}.");
        for (int i = arguments.Count; i < trait.TypeParameters.Length; i++)
        {
            TypeParameterSymbol parameter = trait.TypeParameters[i];
            TypeSymbol value = parameter.DefaultType is TypeSymbol fallback ? Types.Substitute(fallback, substitution) : Types.Error;
            if (parameter.DefaultType is null) Error(tree, last, $"Missing type argument '{parameter.Name}' for trait '{trait.Name}'.");
            arguments.Add(value); substitution[parameter] = value;
        }
        var reference = new TraitReference(trait, self, arguments.ToImmutableArray());
        foreach (var (syntax, name, value) in bindings)
        {
            if (trait.AssociatedTypes.FirstOrDefault(a => a.Name == name) is not AssociatedTypeSymbol item) { Error(tree, syntax, $"Trait '{trait.Name}' has no associated type '{name}'.", "RTRA0220"); continue; }
            if (projections is null) { Error(tree, syntax, "Associated type bindings are not allowed here."); continue; }
            projections.Add(new ProjectionPredicate(new ProjectionTypeSymbol(item, self, reference.Arguments), BindType(value, scope, tree)));
        }
        RecordSymbol(tree, path, trait);
        return reference;
    }
    internal ParamEnv EnvironmentOf(Symbol owner)
    {
        if (_environments.TryGetValue(owner, out ParamEnv? environment)) return environment;
        environment = new ParamEnv(Elaborate(PredicatesOf(owner)));
        _environments[owner] = environment;
        return environment;
    }
    private static IEnumerable<Predicate> PredicatesOf(Symbol? owner) => owner switch
    {
        FunctionSymbol function => function.Predicates.Concat(PredicatesOf(function.ContainingSymbol)),
        ImplSymbol impl => impl.Predicates,
        TraitSymbol trait => trait.Predicates.Prepend(new TraitPredicate(trait.Identity)),
        AggregateSymbol aggregate => aggregate.Predicates,
        _ => []
    };
    private IEnumerable<Predicate> PredicatesInScope(BindingScope scope)
    {
        for (BindingScope? current = scope; current is not null; current = current.Parent)
            if (current.Owner is FunctionSymbol or ImplSymbol or TraitSymbol or AggregateSymbol) return Elaborate(PredicatesOf(current.Owner));
        return [];
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
        var pending = new List<(UseTreeSyntax Syntax, ModuleSymbol Module, SyntaxTree Tree, ImmutableArray<string> Path, bool Public, bool Prelude)>();
        void Flatten(UseTreeSyntax use, ModuleSymbol module, SyntaxTree tree, ImmutableArray<string> prefix, bool isPublic, bool prelude)
        {
            ImmutableArray<string> path = prefix.AddRange(use.Path?.Segments.Select(s => s.Identifier.Text == "$crate" ? DollarCrate + s.Identifier.Context.ToString(CultureInfo.InvariantCulture) : Name(s.Identifier)) ?? []);
            if (use.ChildrenTrees.Any())
                foreach (UseTreeSyntax child in use.ChildrenTrees) Flatten(child, module, tree, path, isPublic, prelude);
            else pending.Add((use, module, tree, path, isPublic, prelude));
        }
        foreach (var use in _uses)
        {
            if (use.Syntax.Tree is UseTreeSyntax syntax)
            {
                if (use.Syntax.Visibility?.Restriction is not null) Unsupported(use.Tree, use.Syntax.Visibility, "Restricted visibility on imports");
                bool prelude = use.Syntax.Attributes.Any(a => AttributeName(a) == "prelude_import");
                if (prelude && (!syntax.IsGlob || !ReferenceEquals(use.Module, Crate))) { Error(use.Tree, use.Syntax, "A prelude import must be a glob import in the crate root."); continue; }
                Flatten(syntax, use.Module, use.Tree, [], use.Syntax.Visibility is { Restriction: null }, prelude);
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
                foreach (SymbolNamespace space in new[] { SymbolNamespace.Type, SymbolNamespace.Value, SymbolNamespace.Macro })
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
                        progress |= AddImport(use.Syntax, use.Module, use.Tree, member.Name, member.Symbol, member.Namespace, true, use.Public, use.Prelude);
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
                    scope.AddRange(module.Scope.Entries);
    }
    private bool AddImport(UseTreeSyntax syntax, ModuleSymbol module, SyntaxTree tree, string name, Symbol target, SymbolNamespace space, bool glob, bool isPublic, bool prelude = false)
    {
        Symbol original = Unwrap(target);
        BindingScope scope = prelude ? _stdPrelude : module.Scope;
        if (name == "_") return false;
        if (glob && scope.Named(name).Any(e => e.Namespace == space && e.Symbol is ImportSymbol { IsGlob: true } i && ReferenceEquals(Unwrap(i), original))) return false;
        if (!IsAccessible(target, module)) { Error(tree, syntax, $"'{target.Name}' is private."); return false; }
        if (isPublic && !original.IsPublic) Error(tree, syntax, $"Cannot publicly re-export private '{target.Name}'.");
        var import = new ImportSymbol(name, original, module, syntax, tree, space, glob) { IsPublic = isPublic, AccessibleWithin = isPublic ? null : module };
        if (glob) scope.Add(name, space, import);
        else AddUnique(scope, name, space, import, syntax);
        if (!prelude) module.Add(import);
        Data(tree).Declarations.TryAdd(syntax, import); RecordSymbol(tree, syntax, original);
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
        else if (first.StartsWith(DollarCrate, StringComparison.Ordinal)) current = CrateOfMark(int.Parse(first.AsSpan(DollarCrate.Length), CultureInfo.InvariantCulture));
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
            ImmutableArray<Symbol> matches = members?.Named(name).Where(e => e.Namespace == (index == names.Length ? space : SymbolNamespace.Type)).Select(e => e.Symbol).ToImmutableArray() ?? [];
            Symbol[] explicitMatches = matches.Where(s => s is not ImportSymbol { IsGlob: true }).ToArray();
            current = explicitMatches.Length == 1 ? explicitMatches[0] : matches.DistinctBy(Unwrap).Count() == 1 ? matches[0] : null;
        }
        return current is not null && IsAccessible(current, module) && !(space == SymbolNamespace.Value && Unwrap(current) is ModuleSymbol) ? current : null;
    }
    internal Symbol? ResolvePath(PathSyntax? path, BindingScope scope, SyntaxTree tree, SymbolNamespace space, bool diagnose = true, int position = int.MaxValue, bool hygienic = false, int length = -1)
    {
        if (path is null) return null;
        if (path.Qualification is not null) { if (diagnose) Error(tree, path, "A qualified path names an associated item, not a module item."); return null; }
        bool global = IsGlobal(path);
        PathSegmentSyntax[] segments = path.Segments.ToArray();
        if (length >= 0 && length < segments.Length) segments = segments[..length];
        if (segments.Length == 0) return null;
        Symbol? result = null;
        for (int i = 0; i < segments.Length; i++)
        {
            string name = Name(segments[i].Identifier);
            SymbolNamespace ns = i == segments.Length - 1 ? space : SymbolNamespace.Type;
            ImmutableArray<Symbol> matches = [];
            if (i == 0)
            {
                if (global) result = _externCrates.GetValueOrDefault(name);
                else if (name == "crate") result = Crate;
                else if (name == "$crate") result = CrateOfMark(segments[i].Identifier.Context);
                else if (name == "self" && (segments.Length > 1 || space == SymbolNamespace.Type)) result = ModuleOf(scope);
                else if (name == "super") result = ModuleOf(scope).Parent;
                else if (name == "Self") result = (FindSelf(scope) as NamedTypeSymbol)?.Definition;
                else
                {
                    matches = scope.Find(name, ns, position, hygienic);
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
        if (result is not null && segments.Length == path.Segments.Count()) RecordSymbol(tree, path, result);
        return result;
    }
    private static ImmutableArray<Symbol> FindMember(BindingScope scope, string name, SymbolNamespace space)
    {
        var all = scope.Named(name).Where(e => e.Namespace == space).Select(e => e.Symbol).ToArray();
        var explicitMatches = all.Where(s => s is not ImportSymbol { IsGlob: true }).ToArray();
        return (explicitMatches.Length == 0 ? all.DistinctBy(Unwrap) : explicitMatches).ToImmutableArray();
    }
    private static TypeSymbol? FindSelf(BindingScope scope)
    {
        for (BindingScope? current = scope; current is not null; current = current.Parent)
        {
            switch (current.Owner)
            {
                case FunctionSymbol { SelfType: TypeSymbol self }: return self;
                case ImplSymbol impl: return impl.SelfType;
                case TraitSymbol trait: return trait.SelfParameter.Type;
                case AggregateSymbol aggregate: return aggregate.Type;
            }
        }
        return null;
    }
    private static ImplSymbol? FindImpl(BindingScope scope)
    {
        for (BindingScope? current = scope; current is not null; current = current.Parent)
            if (current.Owner is ImplSymbol impl) return impl;
        return null;
    }
    // T::Name or Self::Name resolves through the bounds in scope; inside a trait impl, Self::Name is the impl's definition
    private TypeSymbol AssociatedType(TypeSymbol self, TraitReference? qualified, PathSegmentSyntax segment, BindingScope scope, SyntaxTree tree)
    {
        string name = Name(segment.Identifier);
        if (self.IsError) return Types.Error;
        if (segment.Arguments is not null) Unsupported(tree, segment, "Generic associated types");
        if (qualified is not null)
        {
            if (qualified.Trait.AssociatedTypes.FirstOrDefault(a => a.Name == name) is AssociatedTypeSymbol direct)
                return new ProjectionTypeSymbol(direct, self, qualified.Arguments);
            Error(tree, segment, $"Trait '{qualified.Trait.Name}' has no associated type '{name}'.", "RTRA0220");
            return Types.Error;
        }
        if (FindImpl(scope) is ImplSymbol { Trait: TraitReference implemented } impl && TypeSystem.Same(self, impl.SelfType) &&
            implemented.Trait.AssociatedTypes.FirstOrDefault(a => a.Name == name) is AssociatedTypeSymbol own)
            return impl.AssociatedTypes.TryGetValue(own, out TypeSymbol? value) ? value : new ProjectionTypeSymbol(own, self, implemented.Arguments);
        var matches = new List<(AssociatedTypeSymbol Item, TraitReference Trait)>();
        foreach (TraitPredicate bound in PredicatesInScope(scope).OfType<TraitPredicate>())
        {
            if (!TypeSystem.Same(bound.Trait.Self, self) || bound.Trait.Trait.AssociatedTypes.FirstOrDefault(a => a.Name == name) is not AssociatedTypeSymbol item) continue;
            if (!matches.Any(m => ReferenceEquals(m.Item, item) && TypeSystem.SameList(m.Trait.Arguments, bound.Trait.Arguments))) matches.Add((item, bound.Trait));
        }
        if (matches.Count == 1) return new ProjectionTypeSymbol(matches[0].Item, self, matches[0].Trait.Arguments);
        if (matches.Count > 1) Error(tree, segment, $"Ambiguous associated type '{name}' in bounds of '{self}'.", "RTRA0221");
        else if (self is ParameterTypeSymbol) Error(tree, segment, $"Associated type '{name}' not found for '{self}'.", "RTRA0220");
        else Error(tree, segment, $"Ambiguous associated type '{name}' on '{self}'; use a fully qualified path such as <{self} as Trait>::{name}.", "RTRA0223");
        return Types.Error;
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
                if (aggregate.Declaration is ItemSyntax item) aggregate.Predicates = BindPredicates(item, aggregate.Scope, tree, null, p => aggregate.Predicates = p);
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
            case TraitSymbol trait when trait.SignatureState == 0:
            {
                trait.SignatureState = 1;
                SyntaxTree tree = trait.Scope.Tree;
                BindDefaults(trait.TypeParameters, trait.Scope, tree);
                trait.Predicates = BindPredicates((TraitItemSyntax)trait.Declaration!, trait.Scope, tree, trait.SelfParameter.Type, p => trait.Predicates = p);
                foreach (AssociatedTypeSymbol item in trait.AssociatedTypes)
                {
                    if (item.Declaration is not TypeAliasItemSyntax { Bounds: TypeBoundListSyntax bounds }) continue;
                    var predicates = new List<Predicate>();
                    BindBounds(new ProjectionTypeSymbol(item, trait.SelfParameter.Type, trait.TypeParameters.Select(p => p.Type).ToImmutableArray()), bounds, trait.Scope, tree, predicates, null);
                    item.Bounds = predicates.ToImmutableArray();
                }
                trait.SignatureState = 2;
                break;
            }
            case FunctionSymbol function when function.SignatureState == 0:
            {
                function.SignatureState = 1;
                FunctionItemSyntax syntax = function.Declaration;
                SyntaxTree tree = function.Scope.Tree;
                if (function.ContainingSymbol is TraitSymbol owner) BindSignature(owner);
                function.Predicates = BindPredicates(syntax, function.Scope, tree, null, p => function.Predicates = p);
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
                if (function.ExternalName is not null && !function.AllTypeParameters.IsEmpty)
                    Error(tree, syntax, "A function with a fixed external name cannot have type parameters.");
                ParamEnv environment = EnvironmentOf(function);
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
                        if (parameter.Type is not null)
                        {
                            type = BindType(parameter.Type, function.Scope, tree);
                            TypeSymbol receiverType = type is IndirectTypeSymbol { Kind: TypeKind.Reference } selfReference ? selfReference.ElementType : type;
                            if (function.SelfType is not null && !TypeSystem.Same(type, function.SelfType) && !TypeSystem.Same(receiverType, function.SelfType))
                                Unsupported(tree, parameter, "Arbitrary self parameter types");
                        }
                        else if (Has(parameter, SyntaxKind.AmpersandToken)) type = new IndirectTypeSymbol(TypeKind.Reference, type, Has(parameter, SyntaxKind.MutKeyword),
                            BindLifetime(parameter.ChildTokens().FirstOrDefault(t => t.Kind is SyntaxKind.LifetimeToken or SyntaxKind.RawLifetimeToken), parameter, function.Scope, tree));
                    }
                    else type = BindType(parameter.Type, function.Scope, tree);
                    type = NormalizeIn(type, environment);
                    string name = parameter.IsSelf ? "self" : parameter.Pattern is IdentifierPatternSyntax id ? Name(id.Identifier) : "$" + parameters.Count;
                    var p = new ParameterSymbol(name, function, parameter, tree, type, parameters.Count,
                        parameter.IsSelf ? !Has(parameter, SyntaxKind.AmpersandToken) && Has(parameter, SyntaxKind.MutKeyword) : parameter.Pattern is IdentifierPatternSyntax binding && Has(binding, SyntaxKind.MutKeyword));
                    Declare(p, parameter, tree); RecordType(tree, parameter, type); parameters.Add(p);
                }
                function.Parameters = parameters.ToImmutable();
                function.ReturnType = syntax.ReturnType is null ? Types.Unit : NormalizeIn(BindType(syntax.ReturnType.Type, function.Scope, tree), environment);
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
            if (parameter.Declaration is not GenericParameterSyntax syntax) continue;
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
    // Headers and associated types of every impl are bound before signatures, so projections in signatures can normalize
    private void BindImplHeader(ImplItemSyntax syntax, ModuleSymbol module, SyntaxTree tree)
    {
        if (Has(syntax, SyntaxKind.BangToken) || Has(syntax, SyntaxKind.UnsafeKeyword)) { Unsupported(tree, syntax, "Negative or unsafe impls"); return; }
        var impl = new ImplSymbol(_implOrdinal++, module, syntax, tree, Types.Error) { AccessibleWithin = module, IsPublic = true };
        Declare(impl, syntax, tree);
        impl.TypeParameters = DeclareGenerics(syntax, impl, impl.Scope, tree, out ImmutableArray<LifetimeSymbol> lifetimes);
        impl.LifetimeParameters = lifetimes;
        foreach (TypeParameterSymbol parameter in impl.TypeParameters)
            if (parameter.Declaration?.Default is not null) Error(tree, parameter.Declaration, "Impl type parameters cannot have defaults.");
        impl.SelfType = BindType(syntax.SelfType, impl.Scope, tree);
        impl.Predicates = BindPredicates(syntax, impl.Scope, tree, null, p => impl.Predicates = p);
        ImmutableArray<ItemSyntax> items = ExpandMembers(syntax.Body?.Items ?? [], MacrosFor(syntax), module, tree).ToImmutableArray();
        _implItems[impl] = (syntax, items);
        if (syntax.Trait is TypeSyntax traitSyntax)
        {
            if (traitSyntax is not PathTypeSyntax { Path: PathSyntax path }) { Error(tree, traitSyntax, "Expected a trait path."); return; }
            var projections = new List<Predicate>();
            impl.Trait = BindTraitReference(path, impl.SelfType, impl.Scope, tree, projections);
            if (projections.Count != 0) Error(tree, traitSyntax, "Associated type bindings are not allowed in impl headers.");
            if (impl.Trait is not TraitReference reference) return;
            foreach (TypeAliasItemSyntax alias in items.OfType<TypeAliasItemSyntax>())
            {
                string name = Name(alias.Identifier);
                if (reference.Trait.AssociatedTypes.FirstOrDefault(a => a.Name == name) is not AssociatedTypeSymbol item)
                { Error(tree, alias, $"Type '{name}' is not a member of trait '{reference.Trait.Name}'.", "RTRA0437"); continue; }
                if (alias.GenericParameters is not null || alias.WhereClause is not null || alias.Bounds is not null) Unsupported(tree, alias, "Generics or bounds on associated type definitions");
                if (impl.AssociatedTypes.ContainsKey(item)) { Error(tree, alias, $"Duplicate definition of associated type '{name}'.", "RTRA0201"); continue; }
                if (alias.Value?.Value is not TypeSyntax value) { Error(tree, alias, "An associated type in an impl requires a definition."); continue; }
                impl.AssociatedTypes[item] = BindType(value, impl.Scope, tree);
            }
            AddImpl(impl);
            LocalImpls.Add(impl);
        }
        else if (impl.SelfType is NamedTypeSymbol self)
        {
            if (!IsLocal(self.Definition)) Error(tree, syntax, $"Cannot define an inherent impl for '{self}' outside the crate that defines it.", "RTRA0116");
            else self.Definition.InherentImpls = self.Definition.InherentImpls.Add(impl);
        }
        else if (!impl.SelfType.IsError) Error(tree, syntax, $"An inherent impl requires a nominal type defined in this crate, not '{impl.SelfType}'.", "RTRA0118");
    }
    private void BindImplItems(ImplSymbol impl)
    {
        var (syntax, items) = _implItems[impl];
        SyntaxTree tree = impl.SyntaxTree!;
        var module = (ModuleSymbol)impl.ContainingSymbol!;
        MacroScope? macros = MacrosFor(syntax);
        var methods = ImmutableArray.CreateBuilder<FunctionSymbol>();
        foreach (ItemSyntax item in items)
        {
            switch (item)
            {
                case FunctionItemSyntax method:
                {
                    CheckAttributes(method.Attributes, tree, function: true);
                    _itemMacros[method] = macros;
                    string name = Name(method.Identifier);
                    var function = new FunctionSymbol(name, impl, method, tree, impl.Scope, Types.Error)
                    {
                        SelfType = impl.SelfType, AccessibleWithin = module, ParentTypeParameters = impl.TypeParameters,
                        IsIntrinsic = method.Attributes.Any(a => AttributeName(a) == "rustc_intrinsic")
                    };
                    if (methods.Any(m => m.Name == name)) Error(tree, method, $"Duplicate definition of '{name}'.", "RTRA0201");
                    Declare(function, method, tree);
                    function.TypeParameters = DeclareGenerics(method, function, function.Scope, tree, out _);
                    if (impl.Trait is null) BindVisibility(function, method.Visibility);
                    else
                    {
                        if (method.Visibility is not null) Error(tree, method.Visibility, "Visibility qualifiers are not permitted on trait impl items.");
                        function.IsPublic = true; function.AccessibleWithin = null;
                    }
                    methods.Add(function);
                    break;
                }
                case TypeAliasItemSyntax alias when impl.Trait is null && !impl.SelfType.IsError: Unsupported(tree, alias, "Inherent associated types"); break;
                case TypeAliasItemSyntax or MissingItemSyntax: break;
                case ConstItemSyntax: Unsupported(tree, item, "Associated constants"); break;
                default: Unsupported(tree, item, item.Kind.ToString()); break;
            }
        }
        impl.Methods = methods.ToImmutable();
        foreach (FunctionSymbol method in impl.Methods) BindSignature(method);
    }
    private void CheckImpls()
    {
        foreach (ImplSymbol impl in _implItems.Keys)
        {
            SyntaxTree tree = impl.SyntaxTree!;
            var syntax = (ImplItemSyntax)impl.Declaration!;
            foreach (TypeParameterSymbol parameter in impl.TypeParameters)
            {
                bool Mentions(TypeSymbol type) => Contains(type, t => t is ParameterTypeSymbol p && ReferenceEquals(p.Parameter, parameter));
                if (!Mentions(impl.SelfType) && impl.Trait?.Arguments.Any(Mentions) != true)
                    Error(tree, parameter.Declaration!, $"The type parameter '{parameter.Name}' is not constrained by the impl trait or self type.", "RTRA0207");
            }
            if (impl.Trait is not TraitReference reference)
            {
                if (impl.SelfType is not NamedTypeSymbol self) continue;
                foreach (ImplSymbol other in self.Definition.InherentImpls.TakeWhile(i => !ReferenceEquals(i, impl)))
                {
                    if (!SameInherentSelf(impl, other)) continue;
                    foreach (FunctionSymbol method in impl.Methods.Where(m => other.Methods.Any(o => o.Name == m.Name)))
                        Error(tree, method.Declaration, $"Duplicate definitions with name '{method.Name}'.", "RTRA0592");
                }
                continue;
            }
            TraitSymbol trait = reference.Trait;
            ParamEnv environment = EnvironmentOf(impl);
            if (CheckOrphan(reference, syntax, tree) && EarlierImpls(impl).Any(other => Overlap(impl, other)))
                Error(tree, syntax, $"Conflicting implementations of trait '{trait.Name}' for type '{impl.SelfType}'.", "RTRA0119");
            Dictionary<TypeParameterSymbol, TypeSymbol> substitution = Substitution(reference);
            foreach (TraitPredicate required in trait.Predicates.OfType<TraitPredicate>())
                if (Evaluate(Types.Substitute(required, substitution), environment) == Evaluation.Fails)
                    Error(tree, syntax, $"The trait bound '{Types.Substitute(required.Trait, substitution)}' is not satisfied.", "RTRA0277");
            var missing = new List<string>();
            foreach (AssociatedTypeSymbol item in trait.AssociatedTypes)
            {
                if (!impl.AssociatedTypes.ContainsKey(item)) { missing.Add(item.Name); continue; }
                foreach (TraitPredicate bound in item.Bounds.OfType<TraitPredicate>())
                {
                    TraitReference expected = Types.Substitute(bound.Trait, substitution);
                    expected = expected with { Self = NormalizeIn(expected.Self, environment) };
                    if (Evaluate(new TraitPredicate(expected), environment) == Evaluation.Fails)
                        Error(tree, syntax, $"The trait bound '{expected}' required by associated type '{item.Name}' is not satisfied.", "RTRA0277");
                }
            }
            foreach (FunctionSymbol required in trait.Methods)
            {
                if (impl.Methods.FirstOrDefault(m => m.Name == required.Name) is FunctionSymbol method) CompareSignatures(reference, required, method, tree);
                else if (required.Declaration.Body is null) missing.Add(required.Name);
            }
            if (missing.Count != 0) Error(tree, syntax, $"Not all trait items are implemented; missing: {string.Join(", ", missing.Select(m => "'" + m + "'"))}.", "RTRA0046");
            foreach (FunctionSymbol method in impl.Methods.Where(m => !trait.Methods.Any(t => t.Name == m.Name)))
                Error(tree, method.Declaration, $"Method '{method.Name}' is not a member of trait '{trait.Name}'.", "RTRA0407");
            if (ReferenceEquals(trait, CopyTrait))
            {
                if (impl.SelfType is not NamedTypeSymbol copied) { if (!impl.SelfType.IsError && !IsLocal(trait)) Error(tree, syntax, $"The trait 'Copy' cannot be implemented for '{impl.SelfType}'.", "RTRA0206"); continue; }
                FieldSymbol? field = copied.Definition.Fields.Concat(copied.Definition.Variants.SelectMany(v => v.Fields))
                    .FirstOrDefault(f => !IsCopy(NormalizeIn(Types.FieldType(f, copied), environment), environment));
                if (field is not null) Error(tree, syntax, $"The trait 'Copy' cannot be implemented for '{copied}': field '{field.Name}' does not implement 'Copy'.", "RTRA0204");
            }
        }
    }
    private void CompareSignatures(TraitReference reference, FunctionSymbol required, FunctionSymbol method, SyntaxTree tree)
    {
        if (required.TypeParameters.Length != method.TypeParameters.Length)
        { Error(tree, method.Declaration, $"Method '{method.Name}' has {method.TypeParameters.Length} type parameters but its trait declaration has {required.TypeParameters.Length}.", "RTRA0049"); return; }
        if (required.HasSelfParameter != method.HasSelfParameter)
        { Error(tree, method.Declaration, $"Method '{method.Name}' must {(required.HasSelfParameter ? "" : "not ")}take 'self' like its trait declaration.", "RTRA0185"); return; }
        if (required.Parameters.Length != method.Parameters.Length)
        { Error(tree, method.Declaration, $"Method '{method.Name}' has {method.Parameters.Length} parameters but its trait declaration has {required.Parameters.Length}.", "RTRA0050"); return; }
        Dictionary<TypeParameterSymbol, TypeSymbol> substitution = Substitution(reference);
        for (int i = 0; i < required.TypeParameters.Length; i++) substitution[required.TypeParameters[i]] = method.TypeParameters[i].Type;
        ParamEnv environment = EnvironmentOf(method);
        void Compare(TypeSymbol declared, TypeSymbol actual, SyntaxNode syntax)
        {
            TypeSymbol expected = NormalizeIn(Types.Substitute(declared, substitution), environment);
            if (!TypeSystem.Same(expected, actual) && !expected.IsError && !actual.IsError)
                Error(tree, syntax, $"Method '{method.Name}' has an incompatible type for its trait: expected '{expected}', found '{actual}'.", "RTRA0053");
        }
        for (int i = 0; i < method.Parameters.Length; i++) Compare(required.Parameters[i].Type, method.Parameters[i].Type, method.Parameters[i].Declaration);
        Compare(required.ReturnType, method.ReturnType, method.Declaration.ReturnType ?? (SyntaxNode)method.Declaration);
    }
    private bool CheckOrphan(TraitReference reference, SyntaxNode syntax, SyntaxTree tree)
    {
        if (IsLocal(reference.Trait)) return true;
        foreach (TypeSymbol type in reference.Arguments.Prepend(reference.Self))
        {
            TypeSymbol inner = type.Resolve() is IndirectTypeSymbol { Kind: TypeKind.Reference } fundamental ? fundamental.ElementType.Resolve() : type.Resolve();
            if (inner is NamedTypeSymbol named && IsLocal(named.Definition)) return true;
            if (inner is ParameterTypeSymbol)
            {
                Error(tree, syntax, $"Type parameter '{inner}' must be covered by a local type when implementing the foreign trait '{reference.Trait.Name}'.", "RTRA0210");
                return false;
            }
        }
        Error(tree, syntax, $"Only traits defined in this crate can be implemented for types defined outside it ('{reference}').", "RTRA0117");
        return false;
    }
    internal TypeSymbol NormalizeIn(TypeSymbol type, ParamEnv environment)
    {
        if (!Contains(type, t => t is ProjectionTypeSymbol)) return type;
        return type.Resolve() switch
        {
            ProjectionTypeSymbol p => NormalizedProjection(p, environment),
            NamedTypeSymbol n => new NamedTypeSymbol(n.Definition, n.TypeArguments.Select(t => NormalizeIn(t, environment)).ToImmutableArray(), n.LifetimeArguments),
            TupleTypeSymbol t => new TupleTypeSymbol(t.Elements.Select(e => NormalizeIn(e, environment)).ToImmutableArray()),
            SequenceTypeSymbol s => new SequenceTypeSymbol(s.Kind, NormalizeIn(s.ElementType, environment), s.Length, s.RangeForm),
            IndirectTypeSymbol r => new IndirectTypeSymbol(r.Kind, NormalizeIn(r.ElementType, environment), r.IsMutable, r.Lifetime),
            FunctionTypeSymbol f => new FunctionTypeSymbol(f.ParameterTypes.Select(t => NormalizeIn(t, environment)).ToImmutableArray(), NormalizeIn(f.ReturnType, environment), f.IsUnsafe, f.Abi),
            var other => other
        };
    }
    private TypeSymbol NormalizedProjection(ProjectionTypeSymbol projection, ParamEnv environment)
    {
        var inner = new ProjectionTypeSymbol(projection.Item, NormalizeIn(projection.SelfType, environment), projection.TraitArguments.Select(t => NormalizeIn(t, environment)).ToImmutableArray());
        TypeSymbol? value = Probe(() => Project(inner, environment, new List<Predicate>(), Probing) is TypeSymbol result ? Zonk(result) : null);
        if (value is null || value is ProjectionTypeSymbol rigid && TypeSystem.Same(rigid, inner)) return inner;
        return NormalizeIn(value, environment);
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
                MacroTypeSyntax macro => BindMacroType(macro, scope, tree, infer),
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
            if (sized.Any(t => !IsSized(t)) && sized.Any(t => !IsSized(t, new ParamEnv(PredicatesInScope(scope).ToImmutableArray()))))
                Error(tree, syntax, "This type requires sized element or generic argument types.");
        });
        return type;
    }
    private TypeSymbol UnsupportedType(TypeSyntax syntax, SyntaxTree tree) { Unsupported(tree, syntax, syntax.Kind.ToString()); return Types.Error; }
    private TypeSymbol BindMacroType(MacroTypeSyntax syntax, BindingScope scope, SyntaxTree tree, bool infer)
    {
        if (syntax.Invocation is not MacroExpressionSyntax invocation || ExpandType(invocation, MacrosFor(scope.Owner.Declaration), tree, ModuleOf(scope)) is not TypeSyntax type)
            return Types.Error;
        ExpansionDepth++;
        try { return BindType(type, scope, tree, infer); }
        finally { ExpansionDepth--; }
    }
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
    internal TypeSymbol BindPathType(PathSyntax? path, BindingScope scope, SyntaxTree tree, bool infer, int count = -1)
    {
        if (path is null) return Types.Error;
        PathSegmentSyntax[] segments = path.Segments.ToArray();
        if (count < 0) count = segments.Length;
        if (count == 0) return Types.Error;
        TypeSymbol type;
        if (path.Qualification is QualifiedPathSyntax qualification)
        {
            type = BindType(qualification.Type, scope, tree, infer);
            TraitReference? trait = qualification.Trait is PathSyntax traitPath ? BindTraitReference(traitPath, type, scope, tree, null) : null;
            if (qualification.Trait is not null && trait is null) return Types.Error;
            for (int i = 0; i < count; i++) type = AssociatedType(type, i == 0 ? trait : null, segments[i], scope, tree);
            if (count == segments.Length) RecordType(tree, path, type);
            return type;
        }
        if (count == 1)
        {
            string name = Name(segments[0].Identifier);
            if (name == "Self" && FindSelf(scope) is TypeSymbol self)
            {
                if (self is NamedTypeSymbol named) RecordSymbol(tree, path, named.Definition);
                RecordType(tree, path, self);
                return self;
            }
            if (scope.Find(name, SymbolNamespace.Type).IsEmpty && Types.Primitives.TryGetValue(name, out PrimitiveTypeSymbol? primitive))
            {
                if (segments[0].Arguments is not null) Error(tree, path, "Primitive types do not take generic arguments.");
                RecordType(tree, path, primitive); return primitive;
            }
        }
        else if (ResolvePath(path, scope, tree, SymbolNamespace.Type, diagnose: false, length: count) is null)
        {
            type = AssociatedType(BindPathType(path, scope, tree, infer, count - 1), null, segments[count - 1], scope, tree);
            if (count == segments.Length) RecordType(tree, path, type);
            return type;
        }
        Symbol? symbol = ResolvePath(path, scope, tree, SymbolNamespace.Type, length: count);
        PathSegmentSyntax last = segments[count - 1];
        switch (symbol)
        {
            case TypeParameterSymbol parameter:
                if (last.Arguments is not null) Error(tree, path, "Type parameters do not take generic arguments.");
                type = parameter.Type; break;
            case AggregateSymbol aggregate:
            {
                BindSignature(aggregate);
                ImmutableArray<TypeSymbol> typeArguments = BindArguments(aggregate.TypeParameters, last.Arguments, path, scope, tree, infer, out ImmutableArray<LifetimeSymbol?> lifetimes);
                if (!lifetimes.IsEmpty && lifetimes.Length != aggregate.LifetimeParameters.Length)
                    Error(tree, path, $"Expected {aggregate.LifetimeParameters.Length} lifetime arguments, got {lifetimes.Length}.");
                type = new NamedTypeSymbol(aggregate, typeArguments, aggregate.LifetimeParameters
                    .Select((_, i) => (i < lifetimes.Length ? lifetimes[i] : null) ?? new LifetimeSymbol("'_", elided: true)).ToImmutableArray());
                break;
            }
            case TypeAliasSymbol alias:
                BindSignature(alias);
                ImmutableArray<TypeSymbol> arguments = BindArguments(alias.TypeParameters, last.Arguments, path, scope, tree, infer);
                type = Types.Substitute(alias.Target, alias.TypeParameters.Zip(arguments).ToDictionary(p => p.First, p => p.Second)); break;
            default:
                if (symbol is not null) Error(tree, path, $"'{symbol.Name}' is not a type.");
                type = Types.Error; break;
        }
        if (count == segments.Length) RecordType(tree, path, type);
        return type;
    }
    internal ImmutableArray<TypeSymbol> BindArguments(ImmutableArray<TypeParameterSymbol> parameters, GenericArgumentListSyntax? syntax, SyntaxNode origin, BindingScope scope, SyntaxTree tree, bool infer)
        => BindArguments(parameters, syntax, origin, scope, tree, infer, out _);
    private ImmutableArray<TypeSymbol> BindArguments(ImmutableArray<TypeParameterSymbol> parameters, GenericArgumentListSyntax? syntax, SyntaxNode origin, BindingScope scope, SyntaxTree tree, bool infer,
        out ImmutableArray<LifetimeSymbol?> lifetimes)
    {
        var supplied = (syntax?.Arguments ?? []).Where(a => a.Value is not null || !a.ChildTokens().Any(t => t.Kind is SyntaxKind.LifetimeToken or SyntaxKind.RawLifetimeToken)).ToArray();
        lifetimes = (syntax?.Arguments ?? []).Except(supplied).Select(l => BindLifetime(l.ChildTokens().First(), l, scope, tree)).ToImmutableArray();
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
        BoundExpression? value = initializer is null ? null : new BodyBinder(this, global.Scope, null, tree, syntax).Expression(initializer, global.Type);
        if (value is null) Error(tree, syntax, "An initializer is required.");
        else if (!IsConstant(value, initializer: true) && !value.HasErrors) Unsupported(tree, initializer ?? (SyntaxNode)syntax, "This constant initializer");
        if (global.Kind == SymbolKind.Constant) global.ConstantValue = value?.ConstantValue ?? default;
        bound = new BoundGlobal(syntax, global, value);
        _globals.Add(global, bound); global.BindingState = 2; Data(tree).BoundNodes[syntax] = bound;
        return bound;
    }

    // Consts and statics hold constant expressions, and shared borrows of them get the static lifetime; initializers may also borrow statics
    internal static bool IsConstant(BoundExpression expression, bool initializer = false) => expression switch
    {
        _ when expression.ConstantValue.HasValue => true,
        BoundFunctionReference or BoundVariableExpression { Variable: GlobalSymbol { Kind: SymbolKind.Constant } } => true,
        BoundTupleExpression tuple => tuple.Elements.All(e => IsConstant(e, initializer)),
        BoundArrayExpression array => array.Elements.All(e => IsConstant(e, initializer)),
        BoundConstructionExpression construction => construction.Fields.All(f => IsConstant(f.Value, initializer)) && (construction.Base is null || IsConstant(construction.Base, initializer)),
        BoundConversionExpression { Conversion: not (ConversionKind.NumericCast or ConversionKind.PointerCast) } conversion => IsConstant(conversion.Operand, initializer),
        BoundUnaryExpression { Operator: SyntaxKind.AmpersandToken, IsRawBorrow: false, Type: IndirectTypeSymbol { IsMutable: false } } borrow =>
            IsConstant(borrow.Operand, initializer) || initializer && borrow.Operand is BoundVariableExpression { Variable: GlobalSymbol { Kind: SymbolKind.Static } },
        BoundFieldExpression field => IsConstant(field.Receiver, initializer),
        BoundIndexExpression { Index.ConstantValue.HasValue: true } element => initializer && IsConstant(element.Receiver, initializer),
        _ => false
    };
    internal BoundExpression? Initializer(GlobalSymbol global) => (IsLocal(global) ? BindGlobal(global) : _compilation.BoundGlobal(global)).Initializer;
    // A constant reduces to the expression that built it, through other constants and the fields and elements of constants
    internal BoundExpression? Reduce(BoundExpression expression) => expression switch
    {
        BoundVariableExpression { Variable: GlobalSymbol { Kind: SymbolKind.Constant } constant } => Initializer(constant) is BoundExpression value ? Reduce(value) : null,
        BoundConversionExpression { Conversion: ConversionKind.Identity } conversion => Reduce(conversion.Operand),
        BoundFieldExpression field => Reduce(field.Receiver) is BoundExpression receiver && Field(receiver, field.Name) is BoundExpression value ? Reduce(value) : null,
        BoundIndexExpression { Index.ConstantValue.Value: BigInteger index } element => Reduce(element.Receiver) is BoundArrayExpression array && index >= 0 &&
            index < (array.RepeatCount is null ? array.Elements.Length : ((SequenceTypeSymbol)array.Type).Length ?? 0) ? Reduce(array.Elements[array.RepeatCount is null ? (int)index : 0]) : null,
        BoundIndexExpression => null,
        _ => expression
    };
    internal BoundExpression? Field(BoundExpression aggregate, string name) => aggregate switch
    {
        BoundTupleExpression tuple => int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index < tuple.Elements.Length ? tuple.Elements[index] : null,
        BoundConstructionExpression construction when construction.Fields.FirstOrDefault(f => f.Field.Name == name) is { Value: BoundExpression given } => given,
        BoundConstructionExpression { Base: BoundExpression baseValue } => Reduce(baseValue) is BoundExpression reduced ? Field(reduced, name) : null,
        _ => null
    };
    private void BindDiscriminants()
    {
        foreach (AggregateSymbol aggregate in _declarations.OfType<AggregateSymbol>().Where(a => a.TypeKind == TypeKind.Enum)) BindDiscriminants(aggregate);
    }
    // Discriminants bind on demand, since constants may name unit variants and discriminants may name constants
    internal BigInteger Discriminant(VariantSymbol variant)
    {
        if (IsLocal(variant)) BindDiscriminants(variant.Enum);
        return variant.Discriminant;
    }
    private void BindDiscriminants(AggregateSymbol aggregate)
    {
        if (!_discriminants.TryAdd(aggregate, false))
        {
            if (!_discriminants[aggregate]) Error(aggregate.Scope.Tree, aggregate.Declaration, $"Cyclic discriminant of '{aggregate.Name}'.");
            return;
        }
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
        _discriminants[aggregate] = true;
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
        if (left.IsError || right.IsError)
        {
            if (left is InferenceTypeSymbol leftError) { Change(leftError); leftError.Binding = Types.Error; }
            if (right is InferenceTypeSymbol rightError) { Change(rightError); rightError.Binding = Types.Error; }
            return true;
        }
        if (TypeSystem.Same(left, right)) return true;
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
            (ProjectionTypeSymbol a, ProjectionTypeSymbol b) => ReferenceEquals(a.Item, b.Item) && Unify(a.SelfType, b.SelfType) && UnifyList(a.TraitArguments, b.TraitArguments),
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
            Change(other);
            if (other.Constraint == InferenceKind.Any) other.Constraint = inference.Constraint;
            other.HasNeverConstraint |= inference.HasNeverConstraint;
            other.DefaultType ??= inference.DefaultType;
        }
        else if (inference.Constraint == InferenceKind.Integer && value.Kind != TypeKind.Integer || inference.Constraint == InferenceKind.Float && value.Kind != TypeKind.Float) return false;
        if (Contains(value, t => ReferenceEquals(t, inference))) return false;
        Change(inference);
        inference.Binding = value;
        return true;
    }
    private static bool ContainsParameter(TypeSymbol type, Func<TypeParameterSymbol, bool> predicate) => Contains(type, t => t is ParameterTypeSymbol p && predicate(p.Parameter));
    internal static bool Contains(TypeSymbol type, Func<TypeSymbol, bool> predicate)
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
            ProjectionTypeSymbol p => Contains(p.SelfType, predicate) || p.TraitArguments.Any(t => Contains(t, predicate)),
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
            if (!Visit(type.Type, new List<NamedTypeSymbol>())) Error(type.Scope.Tree, type.Declaration, $"'{type.Name}' has infinite size or exceeds the layout nesting limit; introduce indirection.");
    }

    private static IEnumerable<LifetimeSymbol> Lifetimes(TypeSymbol type) => type.Resolve() switch
    {
        IndirectTypeSymbol { Kind: TypeKind.Reference, Lifetime: LifetimeSymbol lifetime } r => Lifetimes(r.ElementType).Prepend(lifetime),
        TupleTypeSymbol t => t.Elements.SelectMany(Lifetimes),
        SequenceTypeSymbol s => Lifetimes(s.ElementType),
        NamedTypeSymbol n => n.LifetimeArguments.Concat(n.TypeArguments.SelectMany(Lifetimes)),
        _ => []
    };
    private static TypeSymbol ReplaceElidedLifetimes(TypeSymbol type, LifetimeSymbol lifetime) => type.Resolve() switch
    {
        IndirectTypeSymbol r => new IndirectTypeSymbol(r.Kind, ReplaceElidedLifetimes(r.ElementType, lifetime), r.IsMutable, r.Lifetime is { IsElided: true } ? lifetime : r.Lifetime),
        TupleTypeSymbol t => new TupleTypeSymbol(t.Elements.Select(e => ReplaceElidedLifetimes(e, lifetime)).ToImmutableArray()),
        SequenceTypeSymbol s => new SequenceTypeSymbol(s.Kind, ReplaceElidedLifetimes(s.ElementType, lifetime), s.Length, s.RangeForm),
        NamedTypeSymbol n => new NamedTypeSymbol(n.Definition, n.TypeArguments.Select(t => ReplaceElidedLifetimes(t, lifetime)).ToImmutableArray(),
            n.LifetimeArguments.Select(l => l.IsElided ? lifetime : l).ToImmutableArray()),
        _ => type
    };
    internal bool IsSized(TypeSymbol type, ParamEnv? environment = null, HashSet<AggregateSymbol>? active = null)
    {
        type = type.Resolve();
        if (type.Kind is TypeKind.String or TypeKind.Slice) return false;
        if (type is ParameterTypeSymbol { Parameter.IsMaybeSized: true })
            return environment?.Predicates.Any(p => p is TraitPredicate { Trait: var bound } && ReferenceEquals(bound.Trait, SizedTrait) && TypeSystem.Same(bound.Self, type)) == true;
        if (type is TupleTypeSymbol tuple) return tuple.Elements.All(t => IsSized(t, environment, active));
        if (type is SequenceTypeSymbol { Kind: TypeKind.Array } array) return IsSized(array.ElementType, environment, active);
        if (type is NamedTypeSymbol named)
        {
            active ??= new HashSet<AggregateSymbol>();
            if (!active.Add(named.Definition)) return true;
            bool result = named.Definition.Fields.Concat(named.Definition.Variants.SelectMany(v => v.Fields)).All(f => IsSized(Types.FieldType(f, named), environment, active));
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
            // Bodiless trait methods are never called by value, so only definitions need sized parameters
            if (symbol is ParameterSymbol { ContainingSymbol: FunctionSymbol { Declaration.Body: null, Trait: not null } }) continue;
            ParamEnv? environment = symbol.ContainingSymbol switch
            {
                FunctionSymbol function => EnvironmentOf(function),
                AggregateSymbol aggregate => EnvironmentOf(aggregate),
                VariantSymbol variant => EnvironmentOf(variant.Enum),
                _ => null
            };
            if (symbol is VariableSymbol variable && !unsizedTail && !IsSized(variable.Type, environment)) Error(tree, syntax, $"'{variable.Type}' cannot be used by value in this declaration.");
            if (symbol is FieldSymbol field && Lifetimes(field.Type).Any(l => l.IsElided)) Error(tree, syntax, "References in fields require an explicit lifetime.");
            if (symbol is FunctionSymbol returning && !(returning.Trait is not null && returning.Declaration.Body is null) && !IsSized(returning.ReturnType, EnvironmentOf(returning)))
                Error(tree, syntax, $"Function return type '{returning.ReturnType}' must be sized.");
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
    private MacroScope? _macros;
    private sealed class LoopContext(LabelSymbol label, string key, TypeSymbol type, bool values, HashSet<VariableSymbol> outer)
    {
        internal readonly LabelSymbol Label = label;
        internal readonly string Key = key;
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
    private readonly ParamEnv _environment;
    internal BodyBinder(Binder binder, BindingScope scope, FunctionSymbol? function, SyntaxTree? tree = null, SyntaxNode? item = null)
    {
        _binder = binder; _scope = scope; _function = function; _tree = tree ?? scope.Tree;
        _macros = binder.MacrosFor(item ?? function?.Declaration ?? scope.Owner.Declaration);
        _environment = function is not null ? binder.EnvironmentOf(function) : ParamEnv.Empty;
    }
    private void Error(SyntaxNode syntax, string message, string code = "RSEM0001") => _binder.Error(_tree, syntax, message, code);
    private void Require(Predicate predicate, SyntaxNode syntax) => _binder.RegisterObligation(predicate, _environment, syntax, _tree);
    private void RequireAll(IEnumerable<Predicate> predicates, SyntaxNode syntax)
    {
        foreach (Predicate predicate in predicates) if (predicate is not OutlivesPredicate) Require(predicate, syntax);
    }
    private TypeSymbol Normalize(TypeSymbol type, SyntaxNode syntax)
    {
        if (!Binder.Contains(type, t => t is ProjectionTypeSymbol)) return type;
        return type.Resolve() switch
        {
            ProjectionTypeSymbol projection => NormalizeProjection(projection, syntax),
            NamedTypeSymbol n => new NamedTypeSymbol(n.Definition, n.TypeArguments.Select(t => Normalize(t, syntax)).ToImmutableArray(), n.LifetimeArguments),
            TupleTypeSymbol t => new TupleTypeSymbol(t.Elements.Select(e => Normalize(e, syntax)).ToImmutableArray()),
            SequenceTypeSymbol s => new SequenceTypeSymbol(s.Kind, Normalize(s.ElementType, syntax), s.Length, s.RangeForm),
            IndirectTypeSymbol r => new IndirectTypeSymbol(r.Kind, Normalize(r.ElementType, syntax), r.IsMutable, r.Lifetime),
            FunctionTypeSymbol f => new FunctionTypeSymbol(f.ParameterTypes.Select(t => Normalize(t, syntax)).ToImmutableArray(), Normalize(f.ReturnType, syntax), f.IsUnsafe, f.Abi),
            var other => other
        };
    }
    // A projection whose impl cannot be selected yet becomes a fresh variable that a deferred obligation will bind
    private TypeSymbol NormalizeProjection(ProjectionTypeSymbol projection, SyntaxNode syntax)
    {
        var inner = new ProjectionTypeSymbol(projection.Item, Normalize(projection.SelfType, syntax), projection.TraitArguments.Select(t => Normalize(t, syntax)).ToImmutableArray());
        var nested = new List<Predicate>();
        TypeSymbol? value = _binder.Project(inner, _environment, nested, _ => _binder.Fresh(syntax));
        if (value is null)
        {
            InferenceTypeSymbol variable = _binder.Fresh(syntax);
            Require(new ProjectionPredicate(inner, variable), syntax);
            return variable;
        }
        RequireAll(nested, syntax);
        return value is ProjectionTypeSymbol rigid && TypeSystem.Same(rigid, inner) ? value : Normalize(value, syntax);
    }
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
                AddVariable(parameter, declaration.Span.Start, names, declaration, declaration.ChildTokens().First(t => t.Kind == SyntaxKind.SelfValueKeyword));
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
        if (body is null && !function.IsForeign && !function.IsIntrinsic && function.Trait is null) Error(syntax, "A function declaration requires a body outside an extern block.");
        _binder.SelectWherePossible();
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
                MacroExpressionSyntax macro => MacroExpression(macro, expected, place),
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
        Symbol? symbol;
        if (syntax.Path is PathSyntax relative && IsTypeRelative(relative, syntax.Span.Start)) return AssociatedValue(relative, syntax);
        if (syntax.Path is { Qualification: null } path && path.Segments.ToArray() is [{ Arguments: null } segment] && segment.Identifier.Context != 0)
        {
            ImmutableArray<Symbol> local = _scope.Find(MacroExpander.Key(segment.Identifier), SymbolNamespace.Value, syntax.Span.Start);
            symbol = local.Length == 1 ? local[0] : _binder.ResolvePath(path, _scope, _tree, SymbolNamespace.Value, position: syntax.Span.Start, hygienic: true);
        }
        else symbol = _binder.ResolvePath(syntax.Path, _scope, _tree, SymbolNamespace.Value, position: syntax.Span.Start);
        if (symbol is null) return Bad(syntax);
        _binder.RecordSymbol(_tree, syntax, symbol);
        return Value(symbol, syntax, syntax.Path, place);
    }
    private ImmutableArray<Symbol> FindValue(SyntaxToken name, int position)
    {
        if (name.Context == 0) return _scope.Find(Binder.Name(name), SymbolNamespace.Value, position);
        ImmutableArray<Symbol> local = _scope.Find(MacroExpander.Key(name), SymbolNamespace.Value, position);
        return local.IsEmpty ? _scope.Find(Binder.Name(name), SymbolNamespace.Value, position, skipLocals: true) : local;
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
                    if (_binder.IsLocal(global)) _binder.BindGlobal(global);
                    if (global.Kind == SymbolKind.Static && global.IsMutable && !_unsafe) Error(syntax, "Access to a mutable static requires an unsafe block.");
                }
                var bound = new BoundVariableExpression(syntax, variable);
                if (variable is GlobalSymbol { Kind: SymbolKind.Constant } constant) bound.ConstantValue = constant.ConstantValue;
                return bound;
            }
            case FunctionSymbol function: return FunctionReference(function, syntax, [], path?.Segments.LastOrDefault()?.Arguments);
            case AggregateSymbol { Shape: AggregateShape.Unit } aggregate:
                return new BoundConstructionExpression(syntax, Instantiate(aggregate, path, syntax), null, []);
            case VariantSymbol { Shape: AggregateShape.Unit } variant:
                return UnitVariant(syntax, Instantiate(variant.Enum, path, syntax), variant);
            default: return Bad(syntax, $"'{symbol.Name}' cannot be used as a value here.");
        }
    }
    // A unit variant of an enum without fields is a constant: its discriminant
    private BoundConstructionExpression UnitVariant(SyntaxNode syntax, NamedTypeSymbol type, VariantSymbol variant) => new(syntax, type, variant, [])
    {
        ConstantValue = variant.Enum.Variants.All(v => v.Fields.IsEmpty) ? ConstantValue.From(_binder.Discriminant(variant)) : default
    };
    private NamedTypeSymbol Instantiate(AggregateSymbol aggregate, PathSyntax? path, SyntaxNode syntax)
    {
        if (path?.Segments.Count(s => s.Arguments is not null) > 1) Error(syntax, "Generic arguments may only be supplied once for a constructor.");
        GenericArgumentListSyntax? arguments = path?.Segments.FirstOrDefault(s => Binder.Name(s.Identifier) == aggregate.Name)?.Arguments;
        arguments ??= path?.Segments.LastOrDefault()?.Arguments;
        var type = new NamedTypeSymbol(aggregate, _binder.BindArguments(aggregate.TypeParameters, arguments, syntax, _scope, _tree, true));
        if (!aggregate.Predicates.IsEmpty)
            RequireAll(aggregate.Predicates.Select(p => Types.Substitute(p, aggregate.TypeParameters.Zip(type.TypeArguments).ToDictionary(x => x.First, x => x.Second))), syntax);
        return type;
    }
    // Instantiates the function's own and enclosing generics and requires its where-clauses for those arguments
    private BoundFunctionReference FunctionReference(FunctionSymbol function, SyntaxNode syntax, ImmutableArray<TypeSymbol> parents, GenericArgumentListSyntax? supplied)
    {
        if (parents.Length != function.ParentTypeParameters.Length) parents = function.ParentTypeParameters.Select(_ => (TypeSymbol)_binder.Fresh(syntax)).ToImmutableArray();
        ImmutableArray<TypeSymbol> arguments = parents.AddRange(_binder.BindArguments(function.TypeParameters, supplied, syntax, _scope, _tree, true));
        Dictionary<TypeParameterSymbol, TypeSymbol> substitution = function.AllTypeParameters.Zip(arguments).ToDictionary(p => p.First, p => p.Second);
        IEnumerable<Predicate> predicates = function.Predicates.Concat(function.ContainingSymbol switch { ImplSymbol impl => impl.Predicates, TraitSymbol trait => trait.Predicates, _ => [] });
        RequireAll(predicates.Select(p => Types.Substitute(p, substitution)), syntax);
        if (_binder.SizedTrait is TraitSymbol sized)
            foreach (var (parameter, argument) in function.AllTypeParameters.Zip(arguments))
                if (!parameter.IsMaybeSized) Require(new TraitPredicate(new TraitReference(sized, argument, [])), syntax);
        var type = new FunctionTypeSymbol(function.Parameters.Select(p => Normalize(Types.Substitute(p.Type, substitution), syntax)).ToImmutableArray(),
            Normalize(Types.Substitute(function.ReturnType, substitution), syntax), function.IsUnsafe, function.Abi);
        _binder.RecordSymbol(_tree, syntax, function);
        return new BoundFunctionReference(syntax, function, type, arguments);
    }
    private BoundFunctionReference TraitMethodReference(FunctionSymbol method, TypeSymbol self, ImmutableArray<TypeSymbol>? traitArguments, GenericArgumentListSyntax? supplied, SyntaxNode syntax)
    {
        TraitSymbol trait = method.Trait!;
        ImmutableArray<TypeSymbol> arguments = traitArguments ?? trait.TypeParameters.Select(_ => (TypeSymbol)_binder.Fresh(syntax)).ToImmutableArray();
        Require(new TraitPredicate(new TraitReference(trait, self, arguments)), syntax);
        return FunctionReference(method, syntax, arguments.Insert(0, self), supplied);
    }
    private ImmutableArray<TraitSymbol> _traitsInScope;
    // Imported and prelude traits, plus the traits bounding generics in scope, contribute methods and associated functions
    private ImmutableArray<TraitSymbol> TraitsInScope()
    {
        if (!_traitsInScope.IsDefault) return _traitsInScope;
        var traits = new List<TraitSymbol>();
        for (BindingScope? scope = _scope; scope is not null; scope = scope.Parent)
            foreach (ScopeEntry entry in scope.Entries)
                if (entry.Namespace == SymbolNamespace.Type && Binder.Unwrap(entry.Symbol) is TraitSymbol trait && !traits.Contains(trait)) traits.Add(trait);
        foreach (TraitPredicate bound in _environment.Predicates.OfType<TraitPredicate>())
            if (!traits.Contains(bound.Trait.Trait)) traits.Add(bound.Trait.Trait);
        return _traitsInScope = traits.ToImmutableArray();
    }
    private bool IsTypeRelative(PathSyntax path, int position)
    {
        if (path.Qualification is not null) return true;
        int count = path.Segments.Count();
        if (count < 2 || _binder.ResolvePath(path, _scope, _tree, SymbolNamespace.Value, false, position) is not null) return false;
        string first = Binder.Name(path.Segments.First().Identifier);
        if (count == 2 && (first == "Self" || _scope.Find(first, SymbolNamespace.Type).IsEmpty && Types.Primitives.ContainsKey(first))) return true;
        return _binder.ResolvePath(path, _scope, _tree, SymbolNamespace.Type, false, position, length: count - 1) is TypeParameterSymbol or AggregateSymbol or TypeAliasSymbol or TraitSymbol;
    }
    // Type::item, Trait::item and <T as Trait>::item: inherent impls first, then traits the type may implement
    private BoundExpression AssociatedValue(PathSyntax path, SyntaxNode syntax)
    {
        PathSegmentSyntax[] segments = path.Segments.ToArray();
        PathSegmentSyntax last = segments[^1];
        string name = Binder.Name(last.Identifier);
        TypeSymbol self;
        if (path.Qualification is QualifiedPathSyntax qualification && segments.Length == 1)
        {
            self = Normalize(_binder.BindType(qualification.Type, _scope, _tree, true), syntax);
            if (qualification.Trait is PathSyntax traitPath)
            {
                if (_binder.BindTraitReference(traitPath, self, _scope, _tree, null) is not TraitReference qualified) return Bad(syntax);
                if (qualified.Trait.Methods.FirstOrDefault(m => m.Name == name) is FunctionSymbol qualifiedMethod)
                    return TraitMethodReference(qualifiedMethod, self, qualified.Arguments, last.Arguments, syntax);
                return Bad(syntax, $"Trait '{qualified.Trait.Name}' has no associated function '{name}'.");
            }
        }
        else if (path.Qualification is null && _binder.ResolvePath(path, _scope, _tree, SymbolNamespace.Type, false, length: segments.Length - 1) is TraitSymbol trait)
        {
            if (trait.Methods.FirstOrDefault(m => m.Name == name) is not FunctionSymbol traitMethod) return Bad(syntax, $"Trait '{trait.Name}' has no associated function '{name}'.");
            ImmutableArray<TypeSymbol>? supplied = segments[^2].Arguments is GenericArgumentListSyntax traitArguments
                ? _binder.BindArguments(trait.TypeParameters, traitArguments, syntax, _scope, _tree, true) : null;
            return TraitMethodReference(traitMethod, _binder.Fresh(syntax), supplied, last.Arguments, syntax);
        }
        else self = Normalize(_binder.BindPathType(path, _scope, _tree, true, segments.Length - 1), syntax);
        if (self.IsError) return Bad(syntax);
        _binder.SelectWherePossible();
        if (self.Resolve() is NamedTypeSymbol named)
        {
            if (named.Definition.Variants.FirstOrDefault(v => v.Name == name) is VariantSymbol variant)
                return variant.Shape == AggregateShape.Unit ? UnitVariant(syntax, named, variant) : Bad(syntax, $"Variant '{name}' requires constructor arguments.");
            foreach (ImplSymbol impl in named.Definition.InherentImpls)
            {
                if (impl.Methods.FirstOrDefault(m => m.Name == name) is not FunctionSymbol method) continue;
                if (!_binder.Probe(() => _binder.Unify(Types.Substitute(impl.SelfType, Binder.Instantiate(impl, _binder.Probing)), self))) continue;
                ImmutableArray<TypeSymbol> arguments = impl.TypeParameters.Select(_ => (TypeSymbol)_binder.Fresh(syntax)).ToImmutableArray();
                _binder.Unify(Types.Substitute(impl.SelfType, impl.TypeParameters.Zip(arguments).ToDictionary(p => p.First, p => p.Second)), self);
                if (!_binder.IsAccessible(method, Binder.ModuleOf(_scope))) Error(syntax, $"Associated function '{name}' is private.");
                return FunctionReference(method, syntax, arguments, last.Arguments);
            }
        }
        var candidates = new List<FunctionSymbol>();
        foreach (TraitSymbol candidate in TraitsInScope())
        {
            if (candidate.Methods.FirstOrDefault(m => m.Name == name) is not FunctionSymbol method) continue;
            var goal = new TraitPredicate(new TraitReference(candidate, self, candidate.TypeParameters.Select(_binder.Probing).ToImmutableArray()));
            if (_binder.Evaluate(goal, _environment) != Evaluation.Fails) candidates.Add(method);
        }
        if (candidates.Count == 1) return TraitMethodReference(candidates[0], self, null, last.Arguments, syntax);
        if (candidates.Count > 1) return Fail(syntax, $"Multiple applicable items named '{name}' for '{self}': {string.Join(", ", candidates.Select(c => c.Trait!.Name))}.", "RTRA0034");
        return Fail(syntax, $"No function or associated item named '{name}' found for '{_binder.Zonk(self)}' in the current scope.", "RTRA0599");
    }
    private BoundErrorExpression Fail(SyntaxNode syntax, string message, string code, IEnumerable<BoundNode>? children = null)
    {
        Error(syntax, message, code);
        return Bad(syntax, children: children);
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
        if (length > 1 && element is not BoundVariableExpression { Variable: GlobalSymbol { Kind: SymbolKind.Constant } })
        {
            if (_binder.CopyTrait is TraitSymbol copy) Require(new TraitPredicate(new TraitReference(copy, element.Type, [])), syntax.Element ?? (SyntaxNode)syntax);
            else _binder.CheckLater(() =>
            {
                if (!_binder.IsCopy(element.Type, _environment) && !element.HasErrors) Error(syntax, $"The repeated array element '{element.Type}' must be Copy.");
            });
        }
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
        if (!IsBuiltinOperand(bound.Type) && !bound.Type.IsError && op is SyntaxKind.MinusToken or SyntaxKind.BangToken &&
            _binder.FindLangItem(op == SyntaxKind.MinusToken ? "neg" : "not") is TraitSymbol { TypeParameters.IsEmpty: true } trait &&
            trait.Methods.FirstOrDefault(m => m.Name == (op == SyntaxKind.MinusToken ? "neg" : "not")) is FunctionSymbol method)
        {
            BoundFunctionReference target = TraitMethodReference(method, bound.Type, [], null, syntax);
            var signature = (FunctionTypeSymbol)target.Type;
            return new BoundCallExpression(syntax, signature.ReturnType, target, [Coerce(bound, signature.ParameterTypes[0], syntax.Operand ?? (SyntaxNode)syntax)]);
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
    // Scalars keep the builtin operators; everything else goes through the operator traits like rustc's lookup_op_method
    private static bool IsBuiltinOperand(TypeSymbol type) => type.Resolve() is PrimitiveTypeSymbol { Kind: not TypeKind.String } or InferenceTypeSymbol or IndirectTypeSymbol { Kind: TypeKind.Pointer };
    private static (string Item, string Method)? OperatorTrait(SyntaxKind op) => op switch
    {
        SyntaxKind.PlusToken => ("add", "add"), SyntaxKind.MinusToken => ("sub", "sub"), SyntaxKind.StarToken => ("mul", "mul"),
        SyntaxKind.SlashToken => ("div", "div"), SyntaxKind.PercentToken => ("rem", "rem"), SyntaxKind.AmpersandToken => ("bitand", "bitand"),
        SyntaxKind.PipeToken => ("bitor", "bitor"), SyntaxKind.CaretToken => ("bitxor", "bitxor"),
        SyntaxKind.LessThanLessThanToken => ("shl", "shl"), SyntaxKind.GreaterThanGreaterThanToken => ("shr", "shr"),
        SyntaxKind.EqualsEqualsToken => ("eq", "eq"), SyntaxKind.BangEqualsToken => ("eq", "ne"),
        SyntaxKind.LessThanToken => ("partial_ord", "lt"), SyntaxKind.LessThanEqualsToken => ("partial_ord", "le"),
        SyntaxKind.GreaterThanToken => ("partial_ord", "gt"), SyntaxKind.GreaterThanEqualsToken => ("partial_ord", "ge"),
        _ => null
    };
    private BoundExpression? OverloadedOperator(SyntaxNode syntax, SyntaxKind op, BoundExpression left, BoundExpression right, bool assignment)
    {
        if (OperatorTrait(op) is not var (item, name)) return null;
        bool comparison = Comparison(op);
        if (assignment) { item += "_assign"; name += "_assign"; }
        if (_binder.FindLangItem(item) is not TraitSymbol trait || trait.TypeParameters.Length != 1 || trait.Methods.FirstOrDefault(m => m.Name == name) is not FunctionSymbol method) return null;
        BoundFunctionReference target = TraitMethodReference(method, left.Type, [right.Type], null, syntax);
        var signature = (FunctionTypeSymbol)target.Type;
        if (signature.ParameterTypes.Length != 2) return Bad(syntax);
        BoundExpression first = comparison || assignment ? AutoRef(left, assignment) : left, second = comparison ? AutoRef(right, false) : right;
        if (assignment && left.ValueCategory != ValueCategory.MutablePlace) Error(syntax, "Assignment requires a mutable place.");
        return new BoundCallExpression(syntax, signature.ReturnType, target, [Coerce(first, signature.ParameterTypes[0], left.Syntax), Coerce(second, signature.ParameterTypes[1], right.Syntax)]);
    }
    private static BoundExpression AutoRef(BoundExpression operand, bool mutable)
        => new BoundUnaryExpression(operand.Syntax, new IndirectTypeSymbol(TypeKind.Reference, operand.Type, mutable), SyntaxKind.AmpersandToken, operand);
    private BoundExpression BinaryOperation(SyntaxNode syntax, SyntaxKind op, BoundExpression left, BoundExpression right)
    {
        if (op is not (SyntaxKind.AmpersandAmpersandToken or SyntaxKind.PipePipeToken) && (!IsBuiltinOperand(left.Type) || !IsBuiltinOperand(right.Type)) &&
            !left.Type.IsError && !right.Type.IsError && OverloadedOperator(syntax, op, left, right, false) is BoundExpression overloaded)
            return overloaded;
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
        if (compound && !IsBuiltinOperand(target.Type) && !target.Type.IsError)
        {
            BoundExpression operand = Expression(syntax.Right);
            if (OverloadedOperator(syntax, CompoundOperator(syntax.OperatorToken.Kind), target, operand, true) is BoundExpression call) return call;
            return Bad(syntax, $"Compound assignment is not defined for '{target.Type}'.", [target, operand]);
        }
        bool shift = syntax.OperatorToken.Kind is SyntaxKind.LessThanLessThanEqualsToken or SyntaxKind.GreaterThanGreaterThanEqualsToken;
        BoundExpression value = Expression(syntax.Right, shift ? null : target.Type);
        if (target is BoundVariableExpression { Variable: LocalSymbol or ParameterSymbol } variable)
        {
            if (!variable.Variable.IsMutable && _assigned.Contains(variable.Variable))
                Error(syntax, $"Cannot assign to immutable '{variable.Variable.Name}'.");
            _initialized.Add(variable.Variable); _assigned.Add(variable.Variable);
        }
        else if (target.ValueCategory != ValueCategory.MutablePlace) Error(syntax, "Assignment requires a mutable place.");
        if (compound) _ = BinaryOperation(syntax, CompoundOperator(syntax.OperatorToken.Kind), target, value);
        return new BoundAssignmentExpression(syntax, Types.Unit, syntax.OperatorToken.Kind, target, value);
    }
    private static SyntaxKind CompoundOperator(SyntaxKind kind) => kind switch
    {
        SyntaxKind.PlusEqualsToken => SyntaxKind.PlusToken, SyntaxKind.MinusEqualsToken => SyntaxKind.MinusToken,
        SyntaxKind.StarEqualsToken => SyntaxKind.StarToken, SyntaxKind.SlashEqualsToken => SyntaxKind.SlashToken,
        SyntaxKind.PercentEqualsToken => SyntaxKind.PercentToken, SyntaxKind.CaretEqualsToken => SyntaxKind.CaretToken,
        SyntaxKind.PipeEqualsToken => SyntaxKind.PipeToken, SyntaxKind.AmpersandEqualsToken => SyntaxKind.AmpersandToken,
        SyntaxKind.LessThanLessThanEqualsToken => SyntaxKind.LessThanLessThanToken, _ => SyntaxKind.GreaterThanGreaterThanToken
    };
    private BoundExpression Cast(CastExpressionSyntax syntax)
    {
        BoundExpression operand = Expression(syntax.Expression);
        TypeSymbol type = Normalize(_binder.BindType(syntax.Type, _scope, _tree), syntax);
        ConversionKind kind = TypeSystem.Same(operand.Type, type) ? ConversionKind.Identity : type.Kind == TypeKind.Pointer ? ConversionKind.PointerCast : ConversionKind.NumericCast;
        var cast = new BoundConversionExpression(syntax, type, kind, operand);
        _binder.CheckLater(() =>
        {
            TypeSymbol source = operand.Type;
            bool valid = TypeSystem.Same(source, type) || source.IsNumeric && type.IsNumeric || source.Kind is TypeKind.Boolean or TypeKind.Character && type.IsInteger ||
                source is NamedTypeSymbol { Kind: TypeKind.Enum } fieldless && fieldless.Definition.Variants.All(v => v.Fields.IsEmpty) && type.IsInteger ||
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
                var values = arguments.Select((a, i) => Expression(a, i < fields.Length ? Normalize(Types.FieldType(fields[i], type), syntax) : null)).ToArray();
                _binder.RecordSymbol(_tree, path, symbol);
                return new BoundConstructionExpression(syntax, type, symbol as VariantSymbol, fields.Zip(values).Select(p => new BoundFieldInitializer(p.First, p.Second)).ToImmutableArray());
            }
        }
        BoundExpression target;
        BoundExpression? receiverArgument = null;
        if (syntax.Target is MemberAccessExpressionSyntax member) target = Method(member, out receiverArgument);
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
    private enum Adjustment : byte { None, Shared, Mutable }
    private sealed record MethodPick(FunctionSymbol Method, ImplSymbol? Impl, int Step, Adjustment Adjustment);
    // Probes like rustc: for each autoderef step try the receiver by value, then &, then &mut; inherent methods win over trait methods at each one
    private BoundExpression Method(MemberAccessExpressionSyntax member, out BoundExpression? receiverArgument)
    {
        receiverArgument = null;
        BoundExpression receiver = Expression(member.Receiver);
        SyntaxNode receiverSyntax = member.Receiver ?? (SyntaxNode)member;
        string name = Binder.Name(member.Member);
        _binder.SelectWherePossible();
        if (receiver.Type is InferenceTypeSymbol { Constraint: InferenceKind.Any })
            return Fail(member, $"Type annotations needed before calling method '{name}'.", "RTRA0282", [receiver]);
        if (receiver.Type.IsError) return Bad(member, children: [receiver]);
        var steps = new List<TypeSymbol> { receiver.Type };
        while (steps.Count < 32 && steps[^1] is IndirectTypeSymbol { Kind: TypeKind.Reference } reference) steps.Add(reference.ElementType.Resolve());
        var inherent = new List<(FunctionSymbol Method, ImplSymbol Impl)>();
        foreach (TypeSymbol step in steps)
            if (step is NamedTypeSymbol named)
                foreach (ImplSymbol impl in named.Definition.InherentImpls)
                    if (impl.Methods.FirstOrDefault(m => m.Name == name && m.HasSelfParameter) is FunctionSymbol method && !inherent.Contains((method, impl))) inherent.Add((method, impl));
        var traits = TraitsInScope().Select(t => t.Methods.FirstOrDefault(m => m.Name == name && m.HasSelfParameter)).OfType<FunctionSymbol>().ToList();
        MethodPick? pick = null;
        for (int i = 0; i < steps.Count && pick is null; i++)
        {
            foreach (Adjustment adjustment in new[] { Adjustment.None, Adjustment.Shared, Adjustment.Mutable })
            {
                TypeSymbol adjusted = adjustment == Adjustment.None ? steps[i] : new IndirectTypeSymbol(TypeKind.Reference, steps[i], adjustment == Adjustment.Mutable);
                var applicable = inherent.Where(c => _binder.Probe(() => InherentApplies(c.Method, c.Impl, adjusted))).ToList();
                if (applicable.Count == 0)
                {
                    var traitApplicable = traits.Where(m => _binder.Probe(() => TraitApplies(m, adjusted))).ToList();
                    if (traitApplicable.Count > 1) return Fail(member, $"Multiple applicable methods named '{name}' for '{adjusted}': {string.Join(", ", traitApplicable.Select(m => m.Trait!.Name))}.", "RTRA0034", [receiver]);
                    if (traitApplicable.Count == 1) pick = new MethodPick(traitApplicable[0], null, i, adjustment);
                }
                else if (applicable.Count > 1) return Fail(member, $"Multiple applicable methods named '{name}' for '{adjusted}'.", "RTRA0034", [receiver]);
                else pick = new MethodPick(applicable[0].Method, applicable[0].Impl, i, adjustment);
                if (pick is not null) break;
            }
        }
        if (pick is null)
            return Fail(member, $"No method named '{name}' found for '{_binder.Zonk(receiver.Type)}' in the current scope.", "RTRA0599", [receiver]);
        // A reference picked by value is reborrowed rather than moved
        if (pick.Adjustment == Adjustment.None && steps[pick.Step] is IndirectTypeSymbol { Kind: TypeKind.Reference } picked)
            pick = pick with { Step = pick.Step + 1, Adjustment = picked.IsMutable ? Adjustment.Mutable : Adjustment.Shared };
        BoundExpression self = receiver;
        for (int i = 0; i < pick.Step; i++)
        {
            var reference = (IndirectTypeSymbol)self.Type;
            self = new BoundUnaryExpression(receiverSyntax, reference.ElementType, SyntaxKind.StarToken, self, reference.IsMutable ? ValueCategory.MutablePlace : ValueCategory.Place);
        }
        if (pick.Adjustment != Adjustment.None)
        {
            if (pick.Adjustment == Adjustment.Mutable && self.ValueCategory == ValueCategory.Place) Error(member, "Method requires a mutable receiver.");
            self = new BoundUnaryExpression(receiverSyntax, new IndirectTypeSymbol(TypeKind.Reference, self.Type, pick.Adjustment == Adjustment.Mutable), SyntaxKind.AmpersandToken, self);
        }
        BoundFunctionReference target;
        if (pick.Impl is ImplSymbol owner)
        {
            if (!_binder.IsAccessible(pick.Method, Binder.ModuleOf(_scope))) Error(member, $"Method '{name}' is private.");
            ImmutableArray<TypeSymbol> arguments = owner.TypeParameters.Select(_ => (TypeSymbol)_binder.Fresh(member)).ToImmutableArray();
            _binder.Unify(Types.Substitute(pick.Method.Parameters[0].Type, owner.TypeParameters.Zip(arguments).ToDictionary(p => p.First, p => p.Second)), self.Type);
            target = FunctionReference(pick.Method, member, arguments, member.GenericArguments);
        }
        else
        {
            TraitSymbol trait = pick.Method.Trait!;
            InferenceTypeSymbol selfType = _binder.Fresh(member);
            ImmutableArray<TypeSymbol> arguments = trait.TypeParameters.Select(_ => (TypeSymbol)_binder.Fresh(member)).ToImmutableArray();
            Dictionary<TypeParameterSymbol, TypeSymbol> substitution = Binder.Substitution(new TraitReference(trait, selfType, arguments));
            _binder.Unify(Types.Substitute(pick.Method.Parameters[0].Type, substitution), self.Type);
            target = TraitMethodReference(pick.Method, selfType, arguments, member.GenericArguments, member);
        }
        Record(member, target);
        var signature = (FunctionTypeSymbol)target.Type;
        receiverArgument = Coerce(self, signature.ParameterTypes[0], receiverSyntax);
        return target;
    }
    private bool InherentApplies(FunctionSymbol method, ImplSymbol impl, TypeSymbol adjusted)
    {
        Dictionary<TypeParameterSymbol, TypeSymbol> substitution = Binder.Instantiate(impl, _binder.Probing);
        if (!_binder.Unify(Types.Substitute(method.Parameters[0].Type, substitution), adjusted)) return false;
        return impl.Predicates.All(p => _binder.Evaluate(Types.Substitute(p, substitution), _environment) != Evaluation.Fails);
    }
    private bool TraitApplies(FunctionSymbol method, TypeSymbol adjusted)
    {
        TraitSymbol trait = method.Trait!;
        var reference = new TraitReference(trait, new InferenceTypeSymbol(InferenceKind.Any, method.Declaration), trait.TypeParameters.Select(_binder.Probing).ToImmutableArray());
        if (!_binder.Unify(Types.Substitute(method.Parameters[0].Type, Binder.Substitution(reference)), adjusted)) return false;
        return _binder.Evaluate(new TraitPredicate(reference), _environment) != Evaluation.Fails;
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
            return Folded(new BoundFieldExpression(syntax, tuple.Elements[index], receiver, null, index, receiver.ValueCategory), receiver);
        if (receiver.Type is NamedTypeSymbol { Kind: TypeKind.Struct } named && named.Definition.Fields.FirstOrDefault(f => f.Name == name) is FieldSymbol field)
        {
            if (!_binder.IsAccessible(field, Binder.ModuleOf(_scope))) Error(syntax, $"Field '{name}' is private.");
            _binder.RecordSymbol(_tree, syntax, field);
            return Folded(new BoundFieldExpression(syntax, Normalize(Types.FieldType(field, named), syntax), receiver, field, field.Ordinal, receiver.ValueCategory), receiver);
        }
        return Bad(syntax, receiver.Type.IsError ? null : $"No field '{name}' on '{receiver.Type}'.", [receiver]);
    }
    // Fields and constant elements of constants fold to the value that initialized them
    private BoundExpression Folded(BoundExpression access, BoundExpression receiver)
    {
        if (Binder.IsConstant(receiver, initializer: true)) access.ConstantValue = _binder.Reduce(access)?.ConstantValue ?? default;
        return access;
    }
    private BoundExpression Index(IndexExpressionSyntax syntax, bool place)
    {
        BoundExpression receiver = AutoDeref(Expression(syntax.Receiver));
        BoundExpression index = Expression(syntax.Index, Types.Usize);
        if (receiver.Type is not SequenceTypeSymbol { Kind: TypeKind.Array or TypeKind.Slice } sequence)
            return Bad(syntax, receiver.Type.IsError ? null : $"Cannot index '{receiver.Type}' without trait resolution.", [receiver, index]);
        if (sequence.Length is ulong length && index.ConstantValue.Value is BigInteger n && (n < 0 || n >= length)) Error(syntax, "Constant index is outside the array.");
        return Folded(new BoundIndexExpression(syntax, sequence.ElementType, receiver, index, receiver.ValueCategory), receiver);
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
            TypeSymbol fieldType = Normalize(Types.FieldType(field, type), fieldSyntax);
            BoundExpression value;
            if (fieldSyntax.Value is not null) value = Expression(fieldSyntax.Value, fieldType);
            else
            {
                ImmutableArray<Symbol> candidates = FindValue(fieldSyntax.Identifier, fieldSyntax.Span.Start);
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
            context = new LoopContext(label, MacroExpander.Key(labelToken), expected ?? _binder.Fresh(syntax), true, VariablesInScope());
            _loops.Add(context); Data.Declarations[syntax] = label; _scope.Add(label.Name, SymbolNamespace.Label, label);
        }
        var statements = ImmutableArray.CreateBuilder<BoundStatement>();
        MacroScope? macros = _macros;
        foreach (StatementSyntax statement in syntax.Statements) Statement(statement, statements);
        BoundExpression? tail = syntax.TailExpression is null ? null : Tail(syntax.TailExpression, statements, expected ?? context?.Type);
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
        _scope = previous; _unsafe = wasUnsafe; _macros = macros;
        return Record(syntax, new BoundBlockExpression(syntax, result, statements.ToImmutable(), tail, locals, context?.Label));
    }
    private void Statement(StatementSyntax statement, ImmutableArray<BoundStatement>.Builder statements)
    {
        BoundStatement bound;
        switch (statement)
        {
            case LetStatementSyntax let:
            {
                TypeSymbol type = let.Type is null ? _binder.Fresh(let.Pattern ?? (SyntaxNode)let) : Normalize(_binder.BindType(let.Type, _scope, _tree, true), let.Type);
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
                if (expression.Expression is MacroExpressionSyntax macro)
                {
                    if (ExpandStatements(macro) is BlockExpressionSyntax expansion) MacroStatements(expansion, statements, expression.Semicolon.Kind != SyntaxKind.None);
                    else statements.Add(Record(statement, new BoundExpressionStatement(statement, Bad(macro))));
                    return;
                }
                BoundExpression value = Expression(expression.Expression);
                if (expression.Semicolon.Kind == SyntaxKind.None && value.Type.Kind != TypeKind.Never) value = Coerce(value, Types.Unit, expression.Expression ?? (SyntaxNode)expression);
                bound = new BoundExpressionStatement(statement, value);
                break;
            }
            case EmptyStatementSyntax: bound = new BoundExpressionStatement(statement, null); break;
            case ItemStatementSyntax { Item: MacroDefinitionItemSyntax definition }:
                _macros = _binder.DefineMacro(definition, _tree, _macros);
                return;
            default:
                _binder.Unsupported(_tree, statement, "Block-local items");
                bound = new BoundExpressionStatement(statement, Bad(statement));
                break;
        }
        statements.Add(Record(statement, bound));
    }
    private BlockExpressionSyntax? ExpandStatements(MacroExpressionSyntax macro) => _binder.ExpandStatements(macro, _macros, _tree, Binder.ModuleOf(_scope));
    private void MacroStatements(BlockExpressionSyntax expansion, ImmutableArray<BoundStatement>.Builder statements, bool semicolon)
    {
        _binder.ExpansionDepth++;
        try
        {
            foreach (StatementSyntax statement in expansion.Statements) Statement(statement, statements);
            if (expansion.TailExpression is not ExpressionSyntax tail) return;
            BoundExpression value = Expression(tail);
            if (!semicolon && value.Type.Kind != TypeKind.Never) value = Coerce(value, Types.Unit, tail);
            statements.Add(new BoundExpressionStatement(tail, value));
        }
        finally { _binder.ExpansionDepth--; }
    }
    private BoundExpression? Tail(ExpressionSyntax syntax, ImmutableArray<BoundStatement>.Builder statements, TypeSymbol? expected)
    {
        if (syntax is not MacroExpressionSyntax macro) return Expression(syntax, expected);
        if (ExpandStatements(macro) is not BlockExpressionSyntax expansion) return Bad(macro);
        _binder.ExpansionDepth++;
        try
        {
            foreach (StatementSyntax statement in expansion.Statements) Statement(statement, statements);
            return expansion.TailExpression is null ? null : Tail(expansion.TailExpression, statements, expected);
        }
        finally { _binder.ExpansionDepth--; }
    }
    private BoundExpression MacroExpression(MacroExpressionSyntax syntax, TypeSymbol? expected, bool place)
    {
        if (_binder.ExpandExpression(syntax, _macros, _tree, Binder.ModuleOf(_scope)) is not ExpressionSyntax expansion) return Bad(syntax);
        _binder.ExpansionDepth++;
        try { return Expression(expansion, expected, place); }
        finally { _binder.ExpansionDepth--; }
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
        string key = name.Length == 0 ? "" : MacroExpander.Key(syntax.Label);
        var context = new LoopContext(label, key, isFor || isWhile ? Types.Unit : expected ?? _binder.Fresh(syntax), !isFor && !isWhile, VariablesInScope());
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
        string key = name.Length == 0 ? "" : MacroExpander.Key(syntax.Label);
        LoopContext? context = name.Length == 0 ? _loops.LastOrDefault(l => l.Label.IsLoop) : _loops.LastOrDefault(l => l.Key == key);
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

    private void AddVariable(VariableSymbol symbol, int visibleFrom, HashSet<string> names, SyntaxNode syntax, SyntaxToken name)
    {
        // Expanded tokens share the invocation span, so their bindings become visible in binding order
        if (name.Text is not null) visibleFrom = 0;
        string key = MacroExpander.Key(name);
        if (!names.Add(key)) Error(syntax, $"'{symbol.Name}' is bound more than once in the pattern.");
        _scope.Add(key, SymbolNamespace.Value, symbol, visibleFrom);
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
        if (syntax is MacroPatternSyntax macro)
        {
            PatternSyntax? expansion = macro.Invocation is MacroExpressionSyntax invocation ? _binder.ExpandPattern(invocation, _macros, _tree, Binder.ModuleOf(_scope)) : null;
            if (expansion is null) return new BoundPattern(syntax, input, BoundPatternKind.Error);
            _binder.ExpansionDepth++;
            try { return Pattern(expansion, input, syntax, visibleFrom, names, parameter, mode); }
            finally { _binder.ExpansionDepth--; }
        }
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
                AddVariable(variable, visibleFrom, names, syntax, identifier.Identifier);
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
            BoundExpression? initializer = _binder.Initializer(constant);
            BoundExpression value = Coerce(Value(constant, syntax), input, syntax);
            _binder.RecordSymbol(_tree, syntax, constant);
            if (!value.ConstantValue.HasValue && !value.HasErrors && initializer is not null) return Destructure(initializer, input, syntax);
            return new BoundPattern(syntax, input, BoundPatternKind.Literal, matched: constant, value: value);
        }
        AggregateSymbol? aggregate = symbol switch { AggregateSymbol { Shape: AggregateShape.Unit } a => a, VariantSymbol { Shape: AggregateShape.Unit } v => v.Enum, _ => null };
        if (aggregate is null) { Error(syntax, "Expected a constant or unit constructor."); return new BoundPattern(syntax, input, BoundPatternKind.Error); }
        NamedTypeSymbol type = Instantiate(aggregate, path, syntax);
        if (!_binder.Unify(input, type)) Error(syntax, $"Pattern type '{type}' does not match '{input}'.");
        _binder.RecordSymbol(_tree, syntax, symbol);
        return new BoundPattern(syntax, input, BoundPatternKind.Aggregate, matched: symbol, irrefutable: aggregate.TypeKind == TypeKind.Struct || aggregate.Variants.Length == 1);
    }
    // Constants without a scalar value match by their structure, as rustc turns them into patterns
    private BoundPattern Destructure(BoundExpression value, TypeSymbol input, SyntaxNode syntax)
    {
        switch (value)
        {
            case BoundExpression { ConstantValue.HasValue: true }: return new BoundPattern(syntax, input, BoundPatternKind.Literal, value: value);
            case BoundVariableExpression { Variable: GlobalSymbol { Kind: SymbolKind.Constant } constant } when _binder.Initializer(constant) is BoundExpression initializer:
                return Destructure(initializer, input, syntax);
            case BoundConversionExpression { Conversion: ConversionKind.Identity } conversion: return Destructure(conversion.Operand, input, syntax);
            case BoundTupleExpression tuple:
                ImmutableArray<BoundPattern> elements = tuple.Elements.Select(e => Destructure(e, e.Type, syntax)).ToImmutableArray();
                return new BoundPattern(syntax, input, BoundPatternKind.Tuple, elements, irrefutable: elements.All(p => p.IsIrrefutable));
            case BoundConstructionExpression { Base: null } construction:
                ImmutableArray<BoundPattern> fields = construction.Fields.Select(f => Destructure(f.Value, f.Value.Type, syntax)).ToImmutableArray();
                AggregateSymbol definition = ((NamedTypeSymbol)construction.Type).Definition;
                return new BoundPattern(syntax, input, BoundPatternKind.Aggregate, fields, matched: (Symbol?)construction.Variant ?? definition, fields: construction.Fields.Select(f => f.Field).ToImmutableArray(),
                    irrefutable: (definition.TypeKind == TypeKind.Struct || definition.Variants.Length == 1) && fields.All(p => p.IsIrrefutable));
            case BoundUnaryExpression { Operator: SyntaxKind.AmpersandToken } borrow:
                BoundPattern referenced = Destructure(borrow.Operand, borrow.Operand.Type, syntax);
                return new BoundPattern(syntax, input, BoundPatternKind.Reference, [referenced], irrefutable: referenced.IsIrrefutable);
            default:
                _binder.Unsupported(_tree, syntax, "Constant patterns of this type");
                return new BoundPattern(syntax, input, BoundPatternKind.Error);
        }
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
            List<BoundPattern?[]> rows = arms.Where(a => a.Guard is null).Select(a => new BoundPattern?[] { a.Pattern }).ToList();
            if (!scrutinee.Type.IsError && Useful(rows, [scrutinee.Type]))
            { Error(syntax, "Match is not exhaustive; add the missing cases or a wildcard arm."); match.OwnErrors = true; }
        });
        return match;
    }
    // A match is exhaustive when a wildcard is useless after its unguarded arms (Maranget, "Warnings for pattern matching", 2007)
    private bool Useful(List<BoundPattern?[]> rows, TypeSymbol[] types)
    {
        while (types.Length != 0)
        {
            rows = rows.SelectMany(Expand).ToList();
            TypeSymbol type = types[0].Resolve();
            TypeSymbol[] rest = types[1..];
            BoundPattern[] heads = rows.Select(r => r[0]).OfType<BoundPattern>().ToArray();
            if (Constructors(type, heads) is List<(object Key, TypeSymbol[] Fields)> constructors && constructors.All(c => heads.Any(h => Covers(h, c.Key, type))))
                return constructors.Any(c => Useful(Specialize(rows, c.Key, type, c.Fields.Length), [.. c.Fields, .. rest]));
            rows = rows.Where(r => r[0] is null).Select(r => r[1..]).ToList();
            types = rest;
        }
        return rows.Count == 0;
    }
    private static IEnumerable<BoundPattern?[]> Expand(BoundPattern?[] row)
    {
        BoundPattern? head = row[0];
        while (head is BoundPattern { PatternKind: BoundPatternKind.Binding } binding) head = binding.Subpatterns.FirstOrDefault();
        if (head?.PatternKind is BoundPatternKind.Wildcard or BoundPatternKind.Rest or BoundPatternKind.Error) head = null;
        if (head?.PatternKind != BoundPatternKind.Or) return [[head, .. row[1..]]];
        return head.Subpatterns.SelectMany(alternative => Expand([alternative, .. row[1..]]));
    }
    private static readonly object SingleConstructor = new();
    // Slices longer than every fixed pattern and every prefix plus suffix match alike, and arrays need columns only for the elements patterns name
    private List<(object Key, TypeSymbol[] Fields)>? Constructors(TypeSymbol type, BoundPattern[] heads)
    {
        BoundPattern[] slices = heads.Where(h => h.PatternKind == BoundPatternKind.Slice).ToArray();
        int longest = slices.Where(h => Rest(h) < 0).Select(h => h.Subpatterns.Length).DefaultIfEmpty(-1).Max();
        int ends = slices.Select(Rest).Where(r => r >= 0).DefaultIfEmpty(0).Max() + slices.Where(h => Rest(h) >= 0).Select(h => h.Subpatterns.Length - 1 - Rest(h)).DefaultIfEmpty(0).Max();
        return type switch
        {
            { Kind: TypeKind.Boolean } => [(true, []), (false, [])],
            { Kind: TypeKind.Never } => [],
            NamedTypeSymbol { Kind: TypeKind.Enum } named => named.Definition.Variants.Select(v => ((object)v, v.Fields.Select(f => Types.FieldType(f, named)).ToArray())).ToList(),
            NamedTypeSymbol { Kind: TypeKind.Struct } named => [(SingleConstructor, named.Definition.Fields.Select(f => Types.FieldType(f, named)).ToArray())],
            TupleTypeSymbol tuple => [(SingleConstructor, tuple.Elements.ToArray())],
            IndirectTypeSymbol { Kind: TypeKind.Reference } reference => [(SingleConstructor, [reference.ElementType])],
            SequenceTypeSymbol { Kind: TypeKind.Array, Length: ulong length } array =>
                [(SingleConstructor, Enumerable.Repeat(array.ElementType, (int)Math.Min(length, (ulong)Math.Max(longest, ends))).ToArray())],
            SequenceTypeSymbol { Kind: TypeKind.Slice } slice => Enumerable.Range(0, Math.Max(longest + 1, ends) + 1).Select(n => ((object)n, Enumerable.Repeat(slice.ElementType, n).ToArray())).ToList(),
            _ => null
        };
    }
    private static int Rest(BoundPattern slice) => slice.Subpatterns.Select((s, i) => s.PatternKind == BoundPatternKind.Rest ? i : -1).DefaultIfEmpty(-1).Max();
    private static bool Covers(BoundPattern head, object constructor, TypeSymbol type)
    {
        if (constructor == SingleConstructor) return true;
        if (constructor is int length) return head.PatternKind == BoundPatternKind.Slice && (Rest(head) >= 0 ? head.Subpatterns.Length - 1 <= length : head.Subpatterns.Length == length);
        object? key = head.MatchedSymbol as VariantSymbol ?? head.Value?.ConstantValue.Value switch
        {
            BigInteger discriminant when type is NamedTypeSymbol { Kind: TypeKind.Enum } named => named.Definition.Variants.FirstOrDefault(v => v.Discriminant == discriminant),
            var value => value
        };
        return constructor.Equals(key);
    }
    private static List<BoundPattern?[]> Specialize(List<BoundPattern?[]> rows, object constructor, TypeSymbol type, int arity)
    {
        var result = new List<BoundPattern?[]>();
        foreach (BoundPattern?[] row in rows)
        {
            if (row[0] is not BoundPattern head) result.Add([.. new BoundPattern?[arity], .. row[1..]]);
            else if (Covers(head, constructor, type)) result.Add([.. Fields(head, type, arity), .. row[1..]]);
        }
        return result;
    }
    // A reference column also takes patterns that match through the reference, as slice patterns on slice references do
    private static BoundPattern?[] Fields(BoundPattern head, TypeSymbol type, int arity)
    {
        var fields = new BoundPattern?[arity];
        void Set(int index, BoundPattern pattern) { if ((uint)index < (uint)arity) fields[index] = pattern; }
        if (type is IndirectTypeSymbol) Set(0, head.PatternKind == BoundPatternKind.Reference ? head.Subpatterns[0] : head);
        else if (head.PatternKind == BoundPatternKind.Aggregate)
            foreach (var (field, pattern) in head.Fields.Zip(head.Subpatterns)) Set(field.Ordinal, pattern);
        else if (head.PatternKind == BoundPatternKind.Tuple)
            for (int i = 0; i < head.Subpatterns.Length; i++) Set(head.Subpatterns[i].InputIndex >= 0 ? head.Subpatterns[i].InputIndex : i, head.Subpatterns[i]);
        else if (head.PatternKind == BoundPatternKind.Slice)
            for (int i = 0, index = 0; i < head.Subpatterns.Length; i++)
                if (head.Subpatterns[i].PatternKind == BoundPatternKind.Rest) index = arity - (head.Subpatterns.Length - i - 1);
                else Set(index++, head.Subpatterns[i]);
        return fields;
    }
}
