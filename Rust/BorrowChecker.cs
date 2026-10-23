using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;

namespace Cnidaria.Rust;

internal sealed class BitSet
{
    private readonly ulong[] _words;
    internal int Count { get; }
    internal BitSet(int count) { Count = count; _words = new ulong[(count + 63) >> 6]; }
    internal bool this[int index] => (_words[index >> 6] & (1UL << index)) != 0;
    internal void Add(int index) => _words[index >> 6] |= 1UL << index;
    internal void Remove(int index) => _words[index >> 6] &= ~(1UL << index);
    internal void Clear() => Array.Clear(_words);
    internal BitSet Clone()
    {
        var copy = new BitSet(Count);
        Array.Copy(_words, copy._words, _words.Length);
        return copy;
    }
    internal bool UnionWith(BitSet other)
    {
        bool changed = false;
        for (int i = 0; i < _words.Length; i++)
        {
            ulong merged = _words[i] | other._words[i];
            changed |= merged != _words[i];
            _words[i] = merged;
        }
        return changed;
    }
    internal IEnumerable<int> Indices()
    {
        for (int i = 0; i < _words.Length; i++)
            for (ulong word = _words[i]; word != 0; word &= word - 1) yield return i * 64 + BitOperations.TrailingZeroCount(word);
    }
}

///<summary>Sorted disjoint inclusive ranges of control flow points</summary>
internal sealed class IntervalSet
{
    private readonly List<(int Start, int End)> _ranges = new();
    private int Find(int point)
    {
        int low = 0, high = _ranges.Count - 1;
        while (low <= high)
        {
            int middle = (low + high) >> 1;
            if (_ranges[middle].Start <= point) low = middle + 1;
            else high = middle - 1;
        }
        return high;
    }
    internal bool Contains(int point)
    {
        int index = Find(point);
        return index >= 0 && _ranges[index].End >= point;
    }
    internal bool Add(int start, int end)
    {
        int index = Find(start);
        if (index >= 0 && _ranges[index].End >= end) return false;
        int first = index >= 0 && _ranges[index].End >= start - 1 ? index : index + 1, last = first;
        for (; last < _ranges.Count && _ranges[last].Start <= end + 1; last++)
        {
            start = Math.Min(start, _ranges[last].Start);
            end = Math.Max(end, _ranges[last].End);
        }
        _ranges.RemoveRange(first, last - first);
        _ranges.Insert(first, (start, end));
        return true;
    }
    internal bool UnionWith(IntervalSet other)
    {
        bool changed = false;
        foreach (var (start, end) in other._ranges) changed |= Add(start, end);
        return changed;
    }
}

internal enum Variance : byte { Bivariant, Covariant, Contravariant, Invariant }

///<summary>A type whose references and lifetime arguments are replaced by region variables</summary>
internal sealed class RegionType(TypeSymbol type, int region, ImmutableArray<RegionType> elements, ImmutableArray<int> lifetimes)
{
    internal TypeSymbol Type { get; } = type;
    internal int Region { get; } = region;
    internal ImmutableArray<RegionType> Elements { get; } = elements;
    internal ImmutableArray<int> Lifetimes { get; } = lifetimes;
    internal RegionType Element(int index = 0) => index < Elements.Length ? Elements[index] : new RegionType(Type, -1, [], []);
    internal int[] Regions()
    {
        if (Region < 0 && Lifetimes.IsEmpty && Elements.IsEmpty) return [];
        var regions = new List<int>();
        Collect(regions);
        return regions.Distinct().ToArray();
    }
    private void Collect(List<int> regions)
    {
        if (Region >= 0) regions.Add(Region);
        regions.AddRange(Lifetimes);
        foreach (RegionType element in Elements) element.Collect(regions);
    }
}

///<summary>Checks moves, borrows and lifetimes of one function on its control flow graph with liveness-based regions</summary>
internal sealed class BorrowChecker
{
    private enum Access : byte { Copy, ShallowRead, SharedBorrow, MutableBorrow, Reservation, Mutate, Move, StorageDead }
    private sealed record Constraint(int Longer, int Shorter, SyntaxNode Syntax, bool IntoReturn);
    private sealed class Loan(int index, int point, FlowPlace place, int region, bool mutable, bool twoPhase, int temp, SyntaxNode syntax)
    {
        internal int Index { get; } = index;
        internal int Point { get; } = point;
        internal FlowPlace Place { get; } = place;
        internal int Region { get; } = region;
        internal bool Mutable { get; } = mutable;
        internal bool TwoPhase { get; } = twoPhase;
        internal int Temp { get; } = temp;
        internal SyntaxNode Syntax { get; } = syntax;
        internal int Activation { get; set; } = -1;
        internal IntervalSet? Reserved { get; set; }
    }
    private sealed class MovePath(FlowPlace place)
    {
        internal FlowPlace Place { get; } = place;
        internal readonly List<int> Children = new();
        internal int[] Descendants = [];
    }

    private readonly Binder _binder;
    private readonly Dictionary<AggregateSymbol, (Variance[] Lifetimes, Variance[] Types)> _variances;
    private readonly FlowGraph _graph;
    private readonly SyntaxTree _tree;
    private readonly int[] _blockStart;
    private readonly int[] _pointBlock;
    private readonly int _pointCount;
    private readonly Dictionary<LifetimeSymbol, int> _universals = new();
    private readonly List<LifetimeSymbol?> _universalNames = [null];
    private readonly List<IntervalSet> _values = new();
    private readonly List<BitSet> _ends = new();
    private readonly List<Constraint> _constraints = new();
    private readonly List<Loan> _loans = new();
    private readonly Dictionary<int, Loan> _loanAt = new();
    private readonly List<MovePath> _paths = new();
    private readonly Dictionary<int, int> _roots = new();
    private readonly Dictionary<(int Parent, Projection Projection), int> _children = new();
    private readonly HashSet<(int Start, int Length, string Code)> _reported = new();
    private readonly HashSet<int> _movesReported = new();
    private RegionType[] _locals = [];
    private int[][] _localRegions = [];
    private bool[,] _known = new bool[0, 0];
    private List<int>?[] _killAt = [];
    private List<Loan>?[] _activations = [];

    private BorrowChecker(Binder binder, FlowGraph graph, Dictionary<AggregateSymbol, (Variance[], Variance[])> variances)
    {
        _binder = binder; _graph = graph; _variances = variances;
        _tree = graph.Function.SyntaxTree!;
        _blockStart = new int[graph.Blocks.Count];
        for (int b = 0; b < graph.Blocks.Count; b++) { _blockStart[b] = _pointCount; _pointCount += graph.Blocks[b].Statements.Count + 1; }
        _pointBlock = new int[_pointCount];
        for (int b = 0; b < graph.Blocks.Count; b++) Array.Fill(_pointBlock, b, _blockStart[b], graph.Blocks[b].Statements.Count + 1);
    }

    internal static void Check(Binder binder, IEnumerable<BoundFunction> functions)
    {
        var variances = new Dictionary<AggregateSymbol, (Variance[], Variance[])>();
        foreach (BoundFunction function in functions)
        {
            if (function.Body is null || function.HasErrors) continue;
            ParamEnv environment = binder.EnvironmentOf(function.Symbol);
            var copies = new Dictionary<string, bool>(StringComparer.Ordinal);
            bool IsCopy(TypeSymbol type)
            {
                string key = type.Name;
                if (!copies.TryGetValue(key, out bool copy)) copies.Add(key, copy = binder.IsCopy(type, environment));
                return copy;
            }
            new BorrowChecker(binder, FlowGraphBuilder.Build(binder.Types, function, IsCopy), variances).Run();
        }
    }

    private void Run()
    {
        FunctionSymbol function = _graph.Function;
        foreach (ParameterSymbol parameter in function.Parameters) CollectUniversals(parameter.Type);
        CollectUniversals(function.ReturnType);
        for (int u = 0; u < _universalNames.Count; u++)
        {
            int region = NewRegion();
            _values[region].Add(0, _pointCount - 1); _ends[region].Add(u);
        }
        KnownRelations();
        _locals = _graph.Locals.Select(l => l.Kind switch
        {
            LocalKind.Return or LocalKind.Parameter => Build(l.Type, Universal),
            LocalKind.Static => Build(l.Type, _ => 0),
            _ => Build(l.Type, _ => NewRegion())
        }).ToArray();
        for (int i = 0; i < _locals.Length; i++)
            if (_graph.Locals[i].IsAnnotated) Annotate(_graph.Locals[i].Type, _locals[i], _graph.Locals[i].Syntax);
        for (int b = 0; b < _graph.Blocks.Count; b++)
        {
            List<FlowStatement> statements = _graph.Blocks[b].Statements;
            for (int i = 0; i < statements.Count; i++)
                if (statements[i].Kind == StatementKind.Assign) Constraints(statements[i], _blockStart[b] + i);
        }
        _localRegions = _locals.Select(t => t.Regions()).ToArray();
        Liveness();
        Solve();
        CheckUniversals();
        PrepareLoans();
        BuildMovePaths();
        BitSet[] loans = Forward(_loans.Count, (point, statement, state) => { BeforeLoans(point, state); if (statement is not null) LoanEffect(point, statement, state); });
        BitSet[] moves = Forward(_paths.Count, (_, statement, state) => { if (statement is not null) MoveEffect(statement, state); });
        for (int b = 0; b < _graph.Blocks.Count; b++)
        {
            BitSet loanState = loans[b].Clone(), moveState = moves[b].Clone();
            FlowBlock block = _graph.Blocks[b];
            for (int i = 0; i < block.Statements.Count; i++)
            {
                int point = _blockStart[b] + i;
                FlowStatement statement = block.Statements[i];
                BeforeLoans(point, loanState);
                CheckStatement(point, statement, loanState, moveState);
                LoanEffect(point, statement, loanState);
                MoveEffect(statement, moveState);
            }
            int end = _blockStart[b] + block.Statements.Count;
            BeforeLoans(end, loanState);
            CheckTerminator(end, block.Terminator, loanState, moveState);
        }
    }

    private int NewRegion()
    {
        _values.Add(new IntervalSet());
        _ends.Add(new BitSet(_universalNames.Count));
        return _values.Count - 1;
    }
    private void CollectUniversals(TypeSymbol type)
    {
        foreach (LifetimeSymbol lifetime in Lifetimes(type))
        {
            if (ReferenceEquals(lifetime, _binder.Types.StaticLifetime) || _universals.ContainsKey(lifetime)) continue;
            _universals.Add(lifetime, _universalNames.Count);
            _universalNames.Add(lifetime);
        }
    }
    private static IEnumerable<LifetimeSymbol> Lifetimes(TypeSymbol type) => type.Resolve() switch
    {
        IndirectTypeSymbol { Kind: TypeKind.Reference, Lifetime: LifetimeSymbol lifetime } reference => Lifetimes(reference.ElementType).Prepend(lifetime),
        IndirectTypeSymbol pointer => Lifetimes(pointer.ElementType),
        TupleTypeSymbol tuple => tuple.Elements.SelectMany(Lifetimes),
        SequenceTypeSymbol sequence => Lifetimes(sequence.ElementType),
        NamedTypeSymbol named => named.LifetimeArguments.Concat(named.TypeArguments.SelectMany(Lifetimes)),
        _ => []
    };
    private int Universal(LifetimeSymbol? lifetime)
    {
        if (lifetime is null) return NewRegion();
        if (ReferenceEquals(lifetime, _binder.Types.StaticLifetime)) return 0;
        return _universals.TryGetValue(lifetime, out int universal) ? universal : NewRegion();
    }
    private int UniversalOf(LifetimeSymbol? lifetime)
        => lifetime is null ? -1 : ReferenceEquals(lifetime, _binder.Types.StaticLifetime) ? 0 : _universals.GetValueOrDefault(lifetime, -1);

    private void KnownRelations()
    {
        int count = _universalNames.Count;
        _known = new bool[count, count];
        for (int u = 0; u < count; u++) { _known[u, u] = true; _known[0, u] = true; }
        foreach (ParameterSymbol parameter in _graph.Function.Parameters) Implied(parameter.Type);
        foreach (OutlivesPredicate bound in _binder.EnvironmentOf(_graph.Function).Predicates.OfType<OutlivesPredicate>())
            if (UniversalOf(bound.Longer) is int longer and >= 0 && UniversalOf(bound.Shorter) is int shorter and >= 0) _known[longer, shorter] = true;
        for (int k = 0; k < count; k++)
            for (int i = 0; i < count; i++)
                if (_known[i, k])
                    for (int j = 0; j < count; j++) _known[i, j] |= _known[k, j];
    }
    private void Implied(TypeSymbol type)
    {
        switch (type.Resolve())
        {
            case IndirectTypeSymbol { Kind: TypeKind.Reference } reference:
                int outer = UniversalOf(reference.Lifetime);
                if (outer >= 0)
                    foreach (LifetimeSymbol inner in Lifetimes(reference.ElementType))
                        if (UniversalOf(inner) is int u and >= 0) _known[u, outer] = true;
                Implied(reference.ElementType);
                break;
            case IndirectTypeSymbol pointer: Implied(pointer.ElementType); break;
            case TupleTypeSymbol tuple: foreach (TypeSymbol element in tuple.Elements) Implied(element); break;
            case SequenceTypeSymbol sequence: Implied(sequence.ElementType); break;
            case NamedTypeSymbol named: foreach (TypeSymbol argument in named.TypeArguments) Implied(argument); break;
        }
    }

    private RegionType Build(TypeSymbol type, Func<LifetimeSymbol?, int> lifetime, Func<TypeParameterSymbol, RegionType?>? parameter = null)
    {
        type = type.Resolve();
        switch (type)
        {
            case IndirectTypeSymbol indirect:
                return new RegionType(type, indirect.Kind == TypeKind.Reference ? lifetime(indirect.Lifetime) : -1, [Build(indirect.ElementType, lifetime, parameter)], []);
            case TupleTypeSymbol tuple when !tuple.Elements.IsEmpty:
                return new RegionType(type, -1, tuple.Elements.Select(e => Build(e, lifetime, parameter)).ToImmutableArray(), []);
            case SequenceTypeSymbol sequence:
                return new RegionType(type, -1, [Build(sequence.ElementType, lifetime, parameter)], []);
            case NamedTypeSymbol named:
                return new RegionType(type, -1, named.TypeArguments.Select(t => Build(t, lifetime, parameter)).ToImmutableArray(),
                    named.Definition.LifetimeParameters.Select((_, i) => lifetime(i < named.LifetimeArguments.Length ? named.LifetimeArguments[i] : null)).ToImmutableArray());
            case ParameterTypeSymbol generic when parameter?.Invoke(generic.Parameter) is RegionType bound:
                return bound;
        }
        return new RegionType(type, -1, [], []);
    }
    private RegionType Field(RegionType owner, VariantSymbol? variant, int index)
    {
        if (owner.Type is not NamedTypeSymbol named) return owner.Element(index);
        AggregateSymbol definition = named.Definition;
        ImmutableArray<FieldSymbol> fields = variant?.Fields ?? definition.Fields;
        if (index >= fields.Length) return new RegionType(owner.Type, -1, [], []);
        return Build(fields[index].Type, l =>
        {
            int slot = l is null ? -1 : definition.LifetimeParameters.IndexOf(l);
            return slot >= 0 && slot < owner.Lifetimes.Length ? owner.Lifetimes[slot] : 0;
        }, p => ReferenceEquals(p.ContainingSymbol, definition) ? owner.Element(p.Ordinal) : null);
    }
    private RegionType TypeOf(FlowPlace place, int length = -1)
    {
        RegionType type = _locals[place.Local];
        VariantSymbol? variant = null;
        if (length < 0) length = place.Projections.Length;
        for (int i = 0; i < length; i++)
        {
            Projection projection = place.Projections[i];
            switch (projection.Kind)
            {
                case ProjectionKind.Deref: case ProjectionKind.Index: case ProjectionKind.ConstantIndex: type = type.Element(); variant = null; break;
                case ProjectionKind.Field: type = Field(type, variant, projection.Value); variant = null; break;
                case ProjectionKind.Downcast: variant = (type.Type as NamedTypeSymbol)?.Definition.Variants[projection.Value]; break;
            }
        }
        return type;
    }
    private RegionType OperandType(FlowOperand operand) => operand.Kind == OperandKind.Constant ? Build(operand.Type, _ => 0) : TypeOf(operand.Place);

    private (Variance[] Lifetimes, Variance[] Types) Variances(AggregateSymbol definition)
    {
        if (_variances.TryGetValue(definition, out var cached)) return cached;
        var lifetimes = new Variance[definition.LifetimeParameters.Length];
        var types = new Variance[definition.TypeParameters.Length];
        _variances.Add(definition, (lifetimes, types));
        bool changed;
        void Mark(Variance[] array, int index, Variance variance)
        {
            Variance merged = Meet(array[index], variance);
            if (merged != array[index]) { array[index] = merged; changed = true; }
        }
        void Walk(TypeSymbol type, Variance variance)
        {
            switch (type.Resolve())
            {
                case IndirectTypeSymbol indirect:
                    if (indirect.Kind == TypeKind.Reference && indirect.Lifetime is LifetimeSymbol lifetime && definition.LifetimeParameters.IndexOf(lifetime) is int slot and >= 0)
                        Mark(lifetimes, slot, variance);
                    Walk(indirect.ElementType, Compose(variance, indirect.IsMutable ? Variance.Invariant : Variance.Covariant));
                    break;
                case TupleTypeSymbol tuple: foreach (TypeSymbol element in tuple.Elements) Walk(element, variance); break;
                case SequenceTypeSymbol sequence: Walk(sequence.ElementType, variance); break;
                case FunctionTypeSymbol signature:
                    foreach (TypeSymbol parameter in signature.ParameterTypes) Walk(parameter, Compose(variance, Variance.Contravariant));
                    Walk(signature.ReturnType, variance);
                    break;
                case NamedTypeSymbol named:
                    var (innerLifetimes, innerTypes) = Variances(named.Definition);
                    for (int i = 0; i < named.LifetimeArguments.Length && i < innerLifetimes.Length; i++)
                        if (definition.LifetimeParameters.IndexOf(named.LifetimeArguments[i]) is int index and >= 0) Mark(lifetimes, index, Compose(variance, innerLifetimes[i]));
                    for (int i = 0; i < named.TypeArguments.Length && i < innerTypes.Length; i++) Walk(named.TypeArguments[i], Compose(variance, innerTypes[i]));
                    break;
                case ParameterTypeSymbol generic when ReferenceEquals(generic.Parameter.ContainingSymbol, definition):
                    Mark(types, generic.Parameter.Ordinal, variance);
                    break;
            }
        }
        do
        {
            changed = false;
            foreach (FieldSymbol field in definition.Fields.Concat(definition.Variants.SelectMany(v => v.Fields))) Walk(field.Type, Variance.Covariant);
        } while (changed);
        return (lifetimes, types);
    }
    private static Variance Meet(Variance left, Variance right)
        => left == right || right == Variance.Bivariant ? left : left == Variance.Bivariant ? right : Variance.Invariant;
    private static Variance Compose(Variance outer, Variance inner) => outer switch
    {
        Variance.Covariant => inner,
        Variance.Contravariant => inner switch { Variance.Covariant => Variance.Contravariant, Variance.Contravariant => Variance.Covariant, _ => inner },
        _ => outer
    };

    private void Outlives(int longer, int shorter, SyntaxNode syntax, bool intoReturn)
    {
        if (longer != shorter && longer >= 0 && shorter >= 0) _constraints.Add(new Constraint(longer, shorter, syntax, intoReturn));
    }
    private void Relate(RegionType sub, RegionType super, Variance variance, SyntaxNode syntax, bool intoReturn)
    {
        void Regions(int left, int right, Variance relation)
        {
            if (relation is Variance.Covariant or Variance.Invariant) Outlives(left, right, syntax, intoReturn);
            if (relation is Variance.Contravariant or Variance.Invariant) Outlives(right, left, syntax, intoReturn);
        }
        if (variance == Variance.Bivariant) return;
        Regions(sub.Region, super.Region, variance);
        switch (sub.Type, super.Type)
        {
            case (IndirectTypeSymbol left, IndirectTypeSymbol right):
                Relate(sub.Element(), super.Element(), Compose(variance, left.IsMutable && right.IsMutable ? Variance.Invariant : Variance.Covariant), syntax, intoReturn);
                break;
            case (NamedTypeSymbol left, NamedTypeSymbol right) when ReferenceEquals(left.Definition, right.Definition):
                var (lifetimes, types) = Variances(left.Definition);
                for (int i = 0; i < sub.Lifetimes.Length && i < super.Lifetimes.Length && i < lifetimes.Length; i++)
                    Regions(sub.Lifetimes[i], super.Lifetimes[i], Compose(variance, lifetimes[i]));
                for (int i = 0; i < sub.Elements.Length && i < super.Elements.Length && i < types.Length; i++)
                    Relate(sub.Elements[i], super.Elements[i], Compose(variance, types[i]), syntax, intoReturn);
                break;
            case (TupleTypeSymbol, TupleTypeSymbol) or (SequenceTypeSymbol, SequenceTypeSymbol):
                for (int i = 0; i < sub.Elements.Length && i < super.Elements.Length; i++) Relate(sub.Elements[i], super.Elements[i], variance, syntax, intoReturn);
                break;
        }
    }
    private void Annotate(TypeSymbol type, RegionType local, SyntaxNode syntax)
    {
        void Equate(LifetimeSymbol? lifetime, int region)
        {
            if (lifetime is null or { IsElided: true } || UniversalOf(lifetime) is not (int universal and >= 0)) return;
            Outlives(universal, region, syntax, false); Outlives(region, universal, syntax, false);
        }
        switch (type.Resolve())
        {
            case IndirectTypeSymbol indirect:
                if (indirect.Kind == TypeKind.Reference) Equate(indirect.Lifetime, local.Region);
                Annotate(indirect.ElementType, local.Element(), syntax);
                break;
            case TupleTypeSymbol tuple: for (int i = 0; i < tuple.Elements.Length; i++) Annotate(tuple.Elements[i], local.Element(i), syntax); break;
            case SequenceTypeSymbol sequence: Annotate(sequence.ElementType, local.Element(), syntax); break;
            case NamedTypeSymbol named:
                for (int i = 0; i < named.LifetimeArguments.Length && i < local.Lifetimes.Length; i++) Equate(named.LifetimeArguments[i], local.Lifetimes[i]);
                for (int i = 0; i < named.TypeArguments.Length; i++) Annotate(named.TypeArguments[i], local.Element(i), syntax);
                break;
        }
    }

    private void Constraints(FlowStatement statement, int point)
    {
        FlowRvalue value = statement.Value!;
        RegionType target = TypeOf(statement.Place);
        SyntaxNode syntax = statement.Syntax;
        bool intoReturn = statement.Place.Local == 0;
        switch (value.Kind)
        {
            case RvalueKind.Use:
                Relate(OperandType(value.Operands[0]), target, Variance.Covariant, syntax, intoReturn);
                break;
            case RvalueKind.Ref:
            {
                int region = 0;
                if (!value.IsPromoted)
                {
                    region = NewRegion();
                    _values[region].Add(point, point);
                    Reborrow(value.Place, region, syntax);
                    if (!IsUntracked(value.Place))
                    {
                        int temp = statement.Place.Projections.IsEmpty ? statement.Place.Local : -1;
                        var loan = new Loan(_loans.Count, point, value.Place, region, value.IsMutable, value.IsTwoPhase && temp >= 0, temp, syntax);
                        _loans.Add(loan); _loanAt.Add(point, loan);
                    }
                }
                Relate(new RegionType(value.Type.Resolve(), region, [TypeOf(value.Place)], []), target, Variance.Covariant, syntax, intoReturn);
                break;
            }
            case RvalueKind.Aggregate:
                for (int i = 0; i < value.Operands.Length; i++)
                {
                    RegionType element = target.Type switch
                    {
                        TupleTypeSymbol => target.Element(i),
                        NamedTypeSymbol => Field(target, value.Variant, value.Fields[i]),
                        _ => target.Element()
                    };
                    Relate(OperandType(value.Operands[i]), element, Variance.Covariant, syntax, intoReturn);
                }
                break;
            case RvalueKind.Cast when value.Conversion == ConversionKind.ArrayToSliceReference:
                Relate(OperandType(value.Operands[0]), target, Variance.Covariant, syntax, intoReturn);
                break;
            case RvalueKind.Call: Call(value, target, syntax, intoReturn); break;
            case RvalueKind.Iterate:
            {
                RegionType iterator = TypeOf(value.Place);
                if (iterator.Type is IndirectTypeSymbol { Kind: TypeKind.Reference } reference)
                {
                    Outlives(iterator.Region, target.Region, syntax, intoReturn);
                    Relate(iterator.Element().Element(), target.Element(), reference.IsMutable ? Variance.Invariant : Variance.Covariant, syntax, intoReturn);
                }
                else if (iterator.Type is SequenceTypeSymbol { Kind: TypeKind.Array }) Relate(iterator.Element(), target, Variance.Covariant, syntax, intoReturn);
                break;
            }
        }
    }
    private void Reborrow(FlowPlace place, int region, SyntaxNode syntax)
    {
        for (int i = place.Projections.Length - 1; i >= 0; i--)
        {
            if (place.Projections[i].Kind != ProjectionKind.Deref) continue;
            RegionType referent = TypeOf(place, i);
            if (referent.Type is not IndirectTypeSymbol { Kind: TypeKind.Reference } reference) break;
            Outlives(referent.Region, region, syntax, false);
            if (!reference.IsMutable) break;
        }
    }
    private bool IsUntracked(FlowPlace place)
    {
        if (_graph.Locals[place.Local].Kind == LocalKind.Static) return true;
        for (int i = 0; i < place.Projections.Length; i++)
            if (place.Projections[i].Kind == ProjectionKind.Deref && TypeOf(place, i).Type is IndirectTypeSymbol { IsMutable: false } or IndirectTypeSymbol { Kind: TypeKind.Pointer }) return true;
        return false;
    }
    private void Call(FlowRvalue call, RegionType target, SyntaxNode syntax, bool intoReturn)
    {
        var instances = new Dictionary<LifetimeSymbol, int>();
        int Instance(LifetimeSymbol? lifetime)
        {
            if (lifetime is null) return NewRegion();
            if (ReferenceEquals(lifetime, _binder.Types.StaticLifetime)) return 0;
            if (!instances.TryGetValue(lifetime, out int region)) instances.Add(lifetime, region = NewRegion());
            return region;
        }
        ImmutableArray<RegionType> parameters;
        RegionType result;
        if (call.Callee is FunctionSymbol callee)
        {
            var arguments = new Dictionary<TypeParameterSymbol, RegionType>();
            ImmutableArray<TypeParameterSymbol> generics = callee.AllTypeParameters;
            for (int i = 0; i < generics.Length && i < call.TypeArguments.Length; i++)
                arguments.Add(generics[i], Build(call.TypeArguments[i], _ => NewRegion()));
            RegionType? Argument(TypeParameterSymbol parameter) => arguments.GetValueOrDefault(parameter);
            parameters = callee.Parameters.Select(p => Build(p.Type, Instance, Argument)).ToImmutableArray();
            result = Build(callee.ReturnType, Instance, Argument);
        }
        else if (call.Operands[0].Type.Resolve() is FunctionTypeSymbol signature)
        {
            parameters = signature.ParameterTypes.Select(t => Build(t, Instance)).ToImmutableArray();
            int[] inputs = parameters.SelectMany(p => p.Regions()).Distinct().ToArray();
            result = Build(signature.ReturnType, l => l is { IsElided: true } && inputs.Length == 1 ? inputs[0] : Instance(l));
        }
        else return;
        for (int i = 0; i + 1 < call.Operands.Length && i < parameters.Length; i++)
            Relate(OperandType(call.Operands[i + 1]), parameters[i], Variance.Covariant, syntax, false);
        Relate(result, target, Variance.Covariant, syntax, intoReturn);
    }

    private static IEnumerable<int> Uses(FlowPlace place, bool includeBase = true)
    {
        if (includeBase) yield return place.Local;
        foreach (Projection projection in place.Projections)
            if (projection.Kind == ProjectionKind.Index) yield return projection.Value;
    }
    private static IEnumerable<int> Uses(FlowRvalue value)
    {
        foreach (FlowOperand operand in value.Operands)
            if (operand.Kind != OperandKind.Constant) foreach (int local in Uses(operand.Place)) yield return local;
        if (value.Kind is RvalueKind.Ref or RvalueKind.AddressOf or RvalueKind.Discriminant or RvalueKind.Length or RvalueKind.Iterate)
            foreach (int local in Uses(value.Place)) yield return local;
    }
    // Live points are traced backward from each use, only for locals whose live ranges constrain regions
    private void Liveness()
    {
        int locals = _graph.Locals.Count;
        var reservations = _loans.Where(l => l.TwoPhase).ToDictionary(l => l.Temp);
        var relevant = new bool[locals];
        for (int local = 0; local < locals; local++)
            relevant[local] = reservations.ContainsKey(local) || _localRegions[local].Any(r => r >= _universalNames.Count);
        var uses = new List<int>?[locals];
        var defined = new int[_pointCount];
        Array.Fill(defined, -1);
        void Use(IEnumerable<int> used, int point) { foreach (int local in used) if (relevant[local]) (uses[local] ??= new()).Add(point); }
        var predecessors = new List<int>?[_graph.Blocks.Count];
        for (int b = 0; b < _graph.Blocks.Count; b++)
        {
            FlowBlock block = _graph.Blocks[b];
            for (int i = 0; i < block.Statements.Count; i++)
            {
                FlowStatement statement = block.Statements[i];
                int point = _blockStart[b] + i;
                switch (statement.Kind)
                {
                    case StatementKind.Assign:
                        if (statement.Place.Projections.IsEmpty) defined[point] = statement.Place.Local;
                        else Use(Uses(statement.Place, statement.Place.IsIndirect), point);
                        Use(Uses(statement.Value!), point);
                        break;
                    case StatementKind.StorageDead: defined[point] = statement.Place.Local; break;
                    case StatementKind.FakeRead: Use(Uses(statement.Place), point); break;
                }
            }
            int end = _blockStart[b] + block.Statements.Count;
            if (block.Terminator.Kind == TerminatorKind.Branch && block.Terminator.Condition.Kind != OperandKind.Constant) Use(Uses(block.Terminator.Condition.Place), end);
            if (block.Terminator.Kind == TerminatorKind.Return) Use([0], end);
            foreach (int successor in block.Terminator.Successors) (predecessors[successor] ??= new()).Add(b);
        }
        var visited = new int[_pointCount];
        var used = new int[_pointCount];
        var pending = new Stack<int>();
        for (int local = 0; local < locals; local++)
        {
            if (uses[local] is not List<int> points) continue;
            int mark = local + 1;
            var range = new IntervalSet();
            foreach (int point in points) { used[point] = mark; pending.Push(point); }
            while (pending.Count != 0)
            {
                int point = pending.Pop();
                if (visited[point] == mark || defined[point] == local && used[point] != mark) continue;
                visited[point] = mark;
                range.Add(point, point);
                int block = _pointBlock[point];
                if (point > _blockStart[block]) pending.Push(point - 1);
                else foreach (int predecessor in predecessors[block] ?? []) pending.Push(_blockStart[predecessor] + _graph.Blocks[predecessor].Statements.Count);
            }
            foreach (int region in _localRegions[local]) _values[region].UnionWith(range);
            if (reservations.TryGetValue(local, out Loan? loan)) loan.Reserved = range;
        }
    }

    private void Solve()
    {
        var dependents = new List<Constraint>?[_values.Count];
        foreach (Constraint constraint in _constraints) (dependents[constraint.Shorter] ??= new()).Add(constraint);
        var queue = new Queue<int>(Enumerable.Range(0, _values.Count));
        var queued = Enumerable.Repeat(true, _values.Count).ToArray();
        while (queue.Count != 0)
        {
            int region = queue.Dequeue();
            queued[region] = false;
            foreach (Constraint constraint in dependents[region] ?? [])
            {
                bool changed = _values[constraint.Longer].UnionWith(_values[region]);
                changed |= _ends[constraint.Longer].UnionWith(_ends[region]);
                if (changed && !queued[constraint.Longer]) { queued[constraint.Longer] = true; queue.Enqueue(constraint.Longer); }
            }
        }
    }

    private void CheckUniversals()
    {
        for (int u = 1; u < _universalNames.Count; u++)
            foreach (int v in _ends[u].Indices())
            {
                if (_known[u, v]) continue;
                Report(Blame(u, v), "RBCK0001", $"Lifetime {LifetimeName(u)} may not live long enough; it must outlive {LifetimeName(v)}.");
                break;
            }
    }
    private string LifetimeName(int universal)
        => universal == 0 ? "'static" : _universalNames[universal] is { IsElided: false } named ? named.Name : "'" + universal;
    private SyntaxNode Blame(int from, int to)
    {
        var outgoing = _constraints.ToLookup(c => c.Longer);
        var reached = new Dictionary<int, Constraint>();
        var queue = new Queue<int>([from]);
        while (queue.Count != 0)
        {
            int region = queue.Dequeue();
            if (region == to && reached.TryGetValue(region, out Constraint? last)) return last.Syntax;
            foreach (Constraint constraint in outgoing[region])
                if (constraint.Shorter != from && reached.TryAdd(constraint.Shorter, constraint)) queue.Enqueue(constraint.Shorter);
        }
        return _graph.Function.Declaration;
    }
    private bool FlowsIntoReturn(int region)
    {
        var outgoing = _constraints.ToLookup(c => c.Longer);
        var visited = new HashSet<int> { region };
        var queue = new Queue<int>([region]);
        while (queue.Count != 0)
            foreach (Constraint constraint in outgoing[queue.Dequeue()])
            {
                if (constraint.IntoReturn) return true;
                if (visited.Add(constraint.Shorter)) queue.Enqueue(constraint.Shorter);
            }
        return false;
    }

    private IEnumerable<int> Successors(int point)
    {
        int block = _pointBlock[point];
        if (point - _blockStart[block] < _graph.Blocks[block].Statements.Count) return [point + 1];
        return _graph.Blocks[block].Terminator.Successors.Select(s => _blockStart[s]);
    }
    private void PrepareLoans()
    {
        _killAt = new List<int>?[_pointCount];
        _activations = new List<Loan>?[_pointCount];
        var temps = _loans.Where(l => l.TwoPhase).ToDictionary(l => l.Temp);
        if (temps.Count != 0)
            for (int b = 0; b < _graph.Blocks.Count; b++)
                for (int i = 0; i < _graph.Blocks[b].Statements.Count; i++)
                {
                    FlowRvalue? value = _graph.Blocks[b].Statements[i].Value;
                    if (value is null) continue;
                    foreach (FlowOperand operand in value.Operands)
                        if (operand.Kind == OperandKind.Move && operand.Place.Projections.IsEmpty && temps.TryGetValue(operand.Place.Local, out Loan? loan) && loan.Activation < 0)
                        {
                            loan.Activation = _blockStart[b] + i;
                            (_activations[loan.Activation] ??= new()).Add(loan);
                        }
                }
        var visited = new int[_pointCount];
        var pending = new Stack<int>();
        for (int index = 0; index < _loans.Count; index++)
        {
            Loan loan = _loans[index];
            IntervalSet region = _values[loan.Region];
            foreach (int successor in Successors(loan.Point)) pending.Push(successor);
            while (pending.Count != 0)
            {
                int point = pending.Pop();
                if (visited[point] == index + 1) continue;
                visited[point] = index + 1;
                if (!region.Contains(point)) { (_killAt[point] ??= new()).Add(index); continue; }
                foreach (int successor in Successors(point)) pending.Push(successor);
            }
        }
    }

    private BitSet[] Forward(int size, Action<int, FlowStatement?, BitSet> transfer)
    {
        int blocks = _graph.Blocks.Count;
        var entry = new BitSet[blocks];
        for (int b = 0; b < blocks; b++) entry[b] = new BitSet(size);
        var visited = new bool[blocks];
        var queued = new bool[blocks];
        var queue = new Queue<int>([0]);
        visited[0] = queued[0] = true;
        while (queue.Count != 0)
        {
            int b = queue.Dequeue();
            queued[b] = false;
            BitSet state = entry[b].Clone();
            FlowBlock block = _graph.Blocks[b];
            for (int i = 0; i < block.Statements.Count; i++) transfer(_blockStart[b] + i, block.Statements[i], state);
            transfer(_blockStart[b] + block.Statements.Count, null, state);
            foreach (int successor in block.Terminator.Successors)
            {
                bool changed = entry[successor].UnionWith(state);
                if ((changed || !visited[successor]) && !queued[successor]) { visited[successor] = queued[successor] = true; queue.Enqueue(successor); }
            }
        }
        return entry;
    }
    private void BeforeLoans(int point, BitSet state)
    {
        if (_killAt[point] is List<int> killed) foreach (int loan in killed) state.Remove(loan);
    }
    private void LoanEffect(int point, FlowStatement statement, BitSet state)
    {
        switch (statement.Kind)
        {
            case StatementKind.Assign:
                // Overwriting a place also ends a reborrow through its old value created by this statement
                if (_loanAt.TryGetValue(point, out Loan? loan)) state.Add(loan.Index);
                foreach (int index in state.Indices().ToArray())
                {
                    FlowPlace borrowed = _loans[index].Place;
                    if (borrowed.Local == statement.Place.Local && (statement.Place.Projections.IsEmpty || Conflicts(borrowed, statement.Place, true, true))) state.Remove(index);
                }
                break;
            case StatementKind.StorageDead:
                foreach (int index in state.Indices().ToArray())
                    if (_loans[index].Place.Local == statement.Place.Local) state.Remove(index);
                break;
        }
    }

    private static bool Conflicts(FlowPlace borrowed, FlowPlace accessed, bool deep, bool disjointIndices = false)
    {
        if (borrowed.Local != accessed.Local) return false;
        int common = Math.Min(borrowed.Projections.Length, accessed.Projections.Length);
        for (int i = 0; i < common; i++)
        {
            Projection left = borrowed.Projections[i], right = accessed.Projections[i];
            switch (left.Kind, right.Kind)
            {
                case (ProjectionKind.Field, ProjectionKind.Field) or (ProjectionKind.Downcast, ProjectionKind.Downcast):
                    if (left.Value != right.Value) return false;
                    break;
                case (ProjectionKind.ConstantIndex, ProjectionKind.ConstantIndex) when left.FromEnd == right.FromEnd:
                    if (left.Value != right.Value) return false;
                    break;
                case (ProjectionKind.Index or ProjectionKind.ConstantIndex, ProjectionKind.Index or ProjectionKind.ConstantIndex):
                    if (disjointIndices) return false;
                    break;
            }
        }
        if (borrowed.Projections.Length <= accessed.Projections.Length || deep) return true;
        // A shallow access cannot reach data behind a pointer stored in the accessed place
        for (int i = common; i < borrowed.Projections.Length; i++)
            if (borrowed.Projections[i].Kind == ProjectionKind.Deref) return false;
        return true;
    }

    private int PathOf(FlowPlace place, bool create)
    {
        if (_graph.Locals[place.Local].Kind == LocalKind.Static || place.Projections.Any(p => p.Kind is ProjectionKind.Deref or ProjectionKind.Index)) return -1;
        if (!_roots.TryGetValue(place.Local, out int path))
        {
            if (!create) return -1;
            _roots.Add(place.Local, path = _paths.Count);
            _paths.Add(new MovePath(new FlowPlace(place.Local)));
        }
        for (int i = 0; i < place.Projections.Length; i++)
        {
            if (_children.TryGetValue((path, place.Projections[i]), out int child)) { path = child; continue; }
            if (!create) return -1;
            child = _paths.Count;
            _paths.Add(new MovePath(place.Prefix(i + 1)));
            _paths[path].Children.Add(child);
            _children.Add((path, place.Projections[i]), child);
            path = child;
        }
        return path;
    }
    private void BuildMovePaths()
    {
        foreach (FlowStatement statement in _graph.Blocks.SelectMany(b => b.Statements))
        {
            if (statement.Kind != StatementKind.Assign) continue;
            foreach (FlowOperand operand in statement.Value!.Operands)
                if (operand.Kind == OperandKind.Move) PathOf(operand.Place, true);
            PathOf(statement.Place, true);
        }
        for (int i = _paths.Count - 1; i >= 0; i--)
            _paths[i].Descendants = _paths[i].Children.SelectMany(c => _paths[c].Descendants).Prepend(i).ToArray();
    }
    private void MoveEffect(FlowStatement statement, BitSet state)
    {
        switch (statement.Kind)
        {
            case StatementKind.Assign:
                foreach (FlowOperand operand in statement.Value!.Operands)
                    if (operand.Kind == OperandKind.Move && PathOf(operand.Place, false) is int moved and >= 0)
                        foreach (int path in _paths[moved].Descendants) state.Add(path);
                if (PathOf(statement.Place, false) is int assigned and >= 0)
                    foreach (int path in _paths[assigned].Descendants) state.Remove(path);
                break;
            case StatementKind.StorageDead:
                if (_roots.TryGetValue(statement.Place.Local, out int root))
                    foreach (int path in _paths[root].Descendants) state.Remove(path);
                break;
        }
    }

    private void CheckStatement(int point, FlowStatement statement, BitSet loans, BitSet moved)
    {
        SyntaxNode syntax = statement.Syntax;
        if (_activations[point] is List<Loan> activated)
            foreach (Loan loan in activated) Conflict(point, loan.Place, true, Access.MutableBorrow, syntax, loans, loan);
        switch (statement.Kind)
        {
            case StatementKind.Assign:
            {
                FlowRvalue value = statement.Value!;
                switch (value.Kind)
                {
                    case RvalueKind.Ref when value.IsPromoted: break;
                    case RvalueKind.Ref or RvalueKind.AddressOf:
                        Initialized(value.Place, moved, syntax, "Borrow");
                        Conflict(point, value.Place, true, value.IsTwoPhase ? Access.Reservation : value.IsMutable ? Access.MutableBorrow : Access.SharedBorrow, syntax, loans);
                        break;
                    case RvalueKind.Discriminant or RvalueKind.Length:
                        Initialized(value.Place, moved, syntax, "Use");
                        Conflict(point, value.Place, false, Access.ShallowRead, syntax, loans);
                        break;
                    case RvalueKind.Iterate:
                        Initialized(value.Place, moved, syntax, "Use");
                        Conflict(point, value.Place, true, Access.Copy, syntax, loans);
                        break;
                    default:
                        foreach (FlowOperand operand in value.Operands) Operand(point, operand, syntax, loans, moved);
                        break;
                }
                Assigned(statement.Place, moved, syntax);
                Conflict(point, statement.Place, false, Access.Mutate, syntax, loans);
                break;
            }
            case StatementKind.FakeRead: Initialized(statement.Place, moved, syntax, "Use"); break;
            case StatementKind.StorageDead: Conflict(point, statement.Place, false, Access.StorageDead, syntax, loans); break;
        }
    }
    private void CheckTerminator(int point, FlowTerminator terminator, BitSet loans, BitSet moved)
    {
        if (terminator.Kind == TerminatorKind.Branch) Operand(point, terminator.Condition, terminator.Syntax, loans, moved);
        if (terminator.Kind != TerminatorKind.Return) return;
        foreach (int index in loans.Indices())
        {
            Loan loan = _loans[index];
            if (!loan.Place.IsIndirect) Dropped(loan);
        }
    }
    private void Operand(int point, FlowOperand operand, SyntaxNode syntax, BitSet loans, BitSet moved)
    {
        if (operand.Kind == OperandKind.Constant) return;
        Initialized(operand.Place, moved, syntax, "Use");
        if (operand.Kind == OperandKind.Move) Movable(operand.Place, syntax);
        Conflict(point, operand.Place, true, operand.Kind == OperandKind.Move ? Access.Move : Access.Copy, syntax, loans);
    }

    private void Conflict(int point, FlowPlace place, bool deep, Access access, SyntaxNode syntax, BitSet loans, Loan? except = null)
    {
        if (_graph.Locals[place.Local].Kind == LocalKind.Static) return;
        foreach (int index in loans.Indices())
        {
            Loan loan = _loans[index];
            if (ReferenceEquals(loan, except) || !Conflicts(loan.Place, place, deep)) continue;
            // A reserved two-phase borrow behaves as a shared borrow until the call that activates it
            bool active = loan.Mutable && !(loan.TwoPhase && point != loan.Activation && loan.Reserved?.Contains(point) == true);
            if (access is Access.Copy or Access.ShallowRead or Access.SharedBorrow && !active || access == Access.Reservation && !loan.Mutable) continue;
            (string Code, string Message) error = access switch
            {
                Access.Copy or Access.ShallowRead => ("RBCK0503", $"Cannot use '{Describe(place)}' because it was mutably borrowed."),
                Access.SharedBorrow => ("RBCK0502", $"Cannot borrow '{Describe(place)}' as immutable because it is also borrowed as mutable."),
                Access.MutableBorrow or Access.Reservation when loan.Mutable => ("RBCK0499", $"Cannot borrow '{Describe(place)}' as mutable more than once at a time."),
                Access.MutableBorrow => ("RBCK0502", $"Cannot borrow '{Describe(place)}' as mutable because it is also borrowed as immutable."),
                Access.Mutate => ("RBCK0506", $"Cannot assign to '{Describe(place)}' because it is borrowed."),
                Access.Move => ("RBCK0505", $"Cannot move out of '{Describe(place)}' because it is borrowed."),
                _ => ("", "")
            };
            if (access == Access.StorageDead) Dropped(loan);
            else Report(syntax, error.Code, error.Message);
            return;
        }
    }
    private void Dropped(Loan loan)
    {
        FlowLocal local = _graph.Locals[loan.Place.Local];
        string name = local.Symbol?.Name ?? "value";
        if (FlowsIntoReturn(loan.Region))
            Report(loan.Syntax, "RBCK0515", local.Kind == LocalKind.Temporary ? "Cannot return a value referencing a temporary value."
                : $"Cannot return a value referencing {(local.Kind == LocalKind.Parameter ? "parameter" : "local")} '{name}'.");
        else if (local.Kind == LocalKind.Temporary) Report(loan.Syntax, "RBCK0716", "Temporary value is dropped while still borrowed.");
        else Report(loan.Syntax, "RBCK0597", $"'{name}' does not live long enough.");
    }

    private void Initialized(FlowPlace place, BitSet moved, SyntaxNode syntax, string action)
    {
        if (!_roots.TryGetValue(place.Local, out int path)) return;
        int depth = 0;
        while (true)
        {
            if (moved[path])
            {
                if (_movesReported.Add(path)) Report(syntax, "RBCK0382", $"{action} of moved value '{Describe(_paths[path].Place)}'.");
                return;
            }
            if (depth == place.Projections.Length || place.Projections[depth].Kind is ProjectionKind.Deref or ProjectionKind.Index) break;
            if (!_children.TryGetValue((path, place.Projections[depth]), out int child)) return;
            path = child; depth++;
        }
        if (depth != place.Projections.Length) return;
        foreach (int descendant in _paths[path].Descendants)
            if (moved[descendant])
            {
                if (_movesReported.Add(descendant)) Report(syntax, "RBCK0382", $"{action} of partially moved value '{Describe(place)}'.");
                return;
            }
    }
    private void Assigned(FlowPlace place, BitSet moved, SyntaxNode syntax)
    {
        int length = place.Projections.Length;
        for (int i = 0; i < place.Projections.Length; i++)
        {
            if (place.Projections[i].Kind == ProjectionKind.Deref) { Initialized(place.Prefix(i), moved, syntax, "Use"); return; }
            if (place.Projections[i].Kind == ProjectionKind.Index) { length = i + 1; break; }
        }
        if (length == 0 || !_roots.TryGetValue(place.Local, out int path)) return;
        for (int depth = 0; ; depth++)
        {
            if (moved[path])
            {
                if (_movesReported.Add(path)) Report(syntax, "RBCK0382", $"Assignment to part of moved value '{Describe(_paths[path].Place)}'.");
                return;
            }
            if (depth + 1 >= length || !_children.TryGetValue((path, place.Projections[depth]), out int child)) return;
            path = child;
        }
    }
    private void Movable(FlowPlace place, SyntaxNode syntax)
    {
        FlowLocal local = _graph.Locals[place.Local];
        if (local.Kind == LocalKind.Static) { Report(syntax, "RBCK0507", $"Cannot move out of static '{local.Symbol?.Name}'."); return; }
        for (int i = 0; i < place.Projections.Length; i++)
        {
            ProjectionKind kind = place.Projections[i].Kind;
            TypeSymbol container = TypeOf(place, i).Type;
            if (kind == ProjectionKind.Deref)
            {
                string behind = container is IndirectTypeSymbol { Kind: TypeKind.Pointer } ? "a raw pointer" : ((IndirectTypeSymbol)container).IsMutable ? "a mutable reference" : "a shared reference";
                Report(syntax, "RBCK0507", $"Cannot move out of '{Describe(place)}', which is behind {behind}.");
                return;
            }
            if (kind == ProjectionKind.Index || kind == ProjectionKind.ConstantIndex && container.Kind == TypeKind.Slice)
            {
                Report(syntax, "RBCK0508", $"Cannot move out of '{Describe(place)}', a non-copy {(container.Kind == TypeKind.Slice ? "slice" : "array")} element.");
                return;
            }
        }
    }

    private string Describe(FlowPlace place)
    {
        FlowLocal local = _graph.Locals[place.Local];
        string text = local.Kind switch { LocalKind.Temporary => "temporary value", LocalKind.Return => "return value", _ => local.Symbol?.Name ?? "value" };
        VariantSymbol? variant = null;
        for (int i = 0; i < place.Projections.Length; i++)
        {
            Projection projection = place.Projections[i];
            switch (projection.Kind)
            {
                case ProjectionKind.Deref:
                    if (i + 1 == place.Projections.Length || place.Projections[i + 1].Kind != ProjectionKind.Field) text = "*" + text;
                    break;
                case ProjectionKind.Field:
                    TypeSymbol owner = TypeOf(place, i).Type;
                    text += "." + (owner is NamedTypeSymbol named && projection.Value < (variant?.Fields ?? named.Definition.Fields).Length
                        ? (variant?.Fields ?? named.Definition.Fields)[projection.Value].Name : projection.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case ProjectionKind.Index: text += "[..]"; break;
                case ProjectionKind.ConstantIndex: text += projection.FromEnd ? $"[len - {projection.Value}]" : $"[{projection.Value}]"; break;
            }
            variant = projection.Kind == ProjectionKind.Downcast ? (TypeOf(place, i).Type as NamedTypeSymbol)?.Definition.Variants[projection.Value] : null;
        }
        return text;
    }
    private void Report(SyntaxNode syntax, string code, string message)
    {
        if (_reported.Add((syntax.Span.Start, syntax.Span.Length, code))) _binder.Error(_tree, syntax, message, code);
    }
}
