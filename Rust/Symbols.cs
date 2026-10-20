using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.Rust;

public enum SymbolKind : byte { Module, NamedType, TypeParameter, TypeAlias, Function, Field, Variant, Parameter, Local, Constant, Static, Lifetime, Label, Import }
public enum SymbolNamespace : byte { Type, Value, Lifetime, Label }
public enum TypeKind : byte { Error, Never, Unit, Boolean, Character, Integer, Float, String, Tuple, Array, Slice, Reference, Pointer, Function, Struct, Enum, TypeParameter, Inference, Range }
public enum AggregateShape : byte { Unit, Tuple, Named }
public enum RangeKind : byte { Exclusive, Inclusive, From, To, ToInclusive, Full }

///<summary>Represents a declaration with stable identity within a compilation</summary>
public abstract class Symbol
{
    public SymbolKind Kind { get; }
    public string Name { get; }
    public Symbol? ContainingSymbol { get; }
    public SyntaxNode? Declaration { get; }
    public SyntaxTree? SyntaxTree { get; }
    public ModuleSymbol? DeclaringModule => this is ModuleSymbol module ? module.Parent : ContainingSymbol as ModuleSymbol ?? ContainingSymbol?.DeclaringModule;
    public bool IsPublic { get; internal set; }
    public ModuleSymbol? AccessibleWithin { get; internal set; }
    public string QualifiedName => ContainingSymbol is null ? Name : ContainingSymbol.QualifiedName + "::" + Name;
    protected Symbol(SymbolKind kind, string name, Symbol? containing = null, SyntaxNode? syntax = null, SyntaxTree? tree = null)
    { Kind = kind; Name = name; ContainingSymbol = containing; Declaration = syntax; SyntaxTree = tree; }
    public override string ToString() => QualifiedName;
}

public sealed class ModuleSymbol : Symbol
{
    private readonly List<Symbol> _members = new();
    public ModuleSymbol? Parent => ContainingSymbol as ModuleSymbol;
    public ImmutableArray<Symbol> Members => _members.ToImmutableArray();
    internal BindingScope Scope { get; }
    internal ModuleSymbol(string name, ModuleSymbol? parent, SyntaxNode syntax, SyntaxTree tree)
        : base(SymbolKind.Module, name, parent, syntax, tree) => Scope = new BindingScope(null, this, tree, syntax.Span);
    internal void Add(Symbol symbol) => _members.Add(symbol);
    public bool Contains(ModuleSymbol module)
    {
        for (ModuleSymbol? current = module; current is not null; current = current.Parent)
            if (ReferenceEquals(current, this)) return true;
        return false;
    }
}

///<summary>Describes a Rust type independently of its source spelling</summary>
public abstract class TypeSymbol
{
    public abstract TypeKind Kind { get; }
    public abstract string Name { get; }
    public bool IsError => Kind == TypeKind.Error;
    public bool IsInteger => Kind == TypeKind.Integer;
    public bool IsNumeric => Kind is TypeKind.Integer or TypeKind.Float;
    public virtual TypeSymbol Resolve() => this;
    public override string ToString() => Name;
}

public sealed class PrimitiveTypeSymbol : TypeSymbol
{
    public override TypeKind Kind { get; }
    public override string Name { get; }
    public int BitWidth { get; }
    public bool IsSigned { get; }
    internal PrimitiveTypeSymbol(string name, TypeKind kind, int width = 0, bool signed = false)
    { Name = name; Kind = kind; BitWidth = width; IsSigned = signed; }
}

public sealed class TypeParameterSymbol : Symbol
{
    public new GenericParameterSyntax Declaration { get; }
    public int Ordinal { get; }
    public TypeSymbol Type { get; }
    public TypeSymbol? DefaultType { get; internal set; }
    internal TypeParameterSymbol(string name, Symbol owner, GenericParameterSyntax syntax, SyntaxTree tree, int ordinal)
        : base(SymbolKind.TypeParameter, name, owner, syntax, tree) { Declaration = syntax; Ordinal = ordinal; Type = new ParameterTypeSymbol(this); }
}

public sealed class ParameterTypeSymbol : TypeSymbol
{
    public TypeParameterSymbol Parameter { get; }
    public override TypeKind Kind => TypeKind.TypeParameter;
    public override string Name => Parameter.Name;
    internal ParameterTypeSymbol(TypeParameterSymbol parameter) => Parameter = parameter;
}

public sealed class LifetimeSymbol : Symbol
{
    public bool IsElided { get; }
    internal LifetimeSymbol(string name, Symbol? owner = null, SyntaxNode? syntax = null, SyntaxTree? tree = null, bool elided = false)
        : base(SymbolKind.Lifetime, name, owner, syntax, tree) => IsElided = elided;
}

public sealed class AggregateSymbol : Symbol
{
    public TypeKind TypeKind { get; }
    public AggregateShape Shape { get; }
    public ImmutableArray<TypeParameterSymbol> TypeParameters { get; internal set; } = [];
    public ImmutableArray<FieldSymbol> Fields { get; internal set; } = [];
    public ImmutableArray<VariantSymbol> Variants { get; internal set; } = [];
    public ImmutableArray<FunctionSymbol> Methods { get; internal set; } = [];
    public NamedTypeSymbol Type { get; internal set; }
    internal BindingScope Scope { get; }
    internal int SignatureState;
    internal AggregateSymbol(string name, TypeKind kind, AggregateShape shape, ModuleSymbol module, ItemSyntax syntax, SyntaxTree tree)
        : base(SymbolKind.NamedType, name, module, syntax, tree)
    { TypeKind = kind; Shape = shape; Type = new NamedTypeSymbol(this, []); Scope = new BindingScope(module.Scope, this, tree, syntax.Span); }
}

public sealed class NamedTypeSymbol : TypeSymbol
{
    public AggregateSymbol Definition { get; }
    public ImmutableArray<TypeSymbol> TypeArguments { get; }
    public override TypeKind Kind => Definition.TypeKind;
    public override string Name => Definition.QualifiedName + (TypeArguments.IsEmpty ? "" : "<" + string.Join(", ", TypeArguments) + ">");
    internal NamedTypeSymbol(AggregateSymbol definition, ImmutableArray<TypeSymbol> arguments) { Definition = definition; TypeArguments = arguments; }
}

public sealed class TupleTypeSymbol : TypeSymbol
{
    public ImmutableArray<TypeSymbol> Elements { get; }
    public override TypeKind Kind => Elements.IsEmpty ? TypeKind.Unit : TypeKind.Tuple;
    public override string Name => "(" + string.Join(", ", Elements) + (Elements.Length == 1 ? "," : "") + ")";
    internal TupleTypeSymbol(ImmutableArray<TypeSymbol> elements) => Elements = elements;
}

public sealed class SequenceTypeSymbol : TypeSymbol
{
    public TypeSymbol ElementType { get; }
    public ulong? Length { get; }
    public RangeKind RangeForm { get; }
    public override TypeKind Kind { get; }
    public override string Name => Kind switch { TypeKind.Array => $"[{ElementType}; {Length}]", TypeKind.Slice => $"[{ElementType}]", _ => $"range{RangeForm}<{ElementType}>" };
    internal SequenceTypeSymbol(TypeKind kind, TypeSymbol element, ulong? length = null, RangeKind rangeForm = RangeKind.Exclusive) { Kind = kind; ElementType = element; Length = length; RangeForm = rangeForm; }
}

public sealed class IndirectTypeSymbol : TypeSymbol
{
    public TypeSymbol ElementType { get; }
    public bool IsMutable { get; }
    public LifetimeSymbol? Lifetime { get; }
    public override TypeKind Kind { get; }
    public override string Name => Kind == TypeKind.Reference
        ? "&" + (Lifetime is { IsElided: false } ? Lifetime.Name + " " : "") + (IsMutable ? "mut " : "") + ElementType
        : (IsMutable ? "*mut " : "*const ") + ElementType;
    internal IndirectTypeSymbol(TypeKind kind, TypeSymbol element, bool mutable, LifetimeSymbol? lifetime = null)
    { Kind = kind; ElementType = element; IsMutable = mutable; Lifetime = lifetime; }
}

public sealed class FunctionTypeSymbol : TypeSymbol
{
    public ImmutableArray<TypeSymbol> ParameterTypes { get; }
    public TypeSymbol ReturnType { get; }
    public bool IsUnsafe { get; }
    public string Abi { get; }
    public override TypeKind Kind => TypeKind.Function;
    public override string Name => (IsUnsafe ? "unsafe " : "") + "fn(" + string.Join(", ", ParameterTypes) + ") -> " + ReturnType;
    internal FunctionTypeSymbol(ImmutableArray<TypeSymbol> parameters, TypeSymbol result, bool isUnsafe = false, string abi = "Rust")
    { ParameterTypes = parameters; ReturnType = result; IsUnsafe = isUnsafe; Abi = abi; }
}

public sealed class TypeAliasSymbol : Symbol
{
    public TypeSymbol Target { get; internal set; }
    public ImmutableArray<TypeParameterSymbol> TypeParameters { get; internal set; } = [];
    internal BindingScope Scope { get; }
    internal int SignatureState;
    internal TypeAliasSymbol(string name, ModuleSymbol module, TypeAliasItemSyntax syntax, SyntaxTree tree, TypeSymbol error)
        : base(SymbolKind.TypeAlias, name, module, syntax, tree) { Target = error; Scope = new BindingScope(module.Scope, this, tree, syntax.Span); }
}

public sealed class FunctionSymbol : Symbol
{
    public new FunctionItemSyntax Declaration { get; }
    public ImmutableArray<TypeParameterSymbol> TypeParameters { get; internal set; } = [];
    public ImmutableArray<ParameterSymbol> Parameters { get; internal set; } = [];
    public TypeSymbol ReturnType { get; internal set; }
    public bool IsUnsafe { get; internal set; }
    public bool IsExtern { get; internal set; }
    public bool IsForeign { get; internal set; }
    public string? ExternalName { get; internal set; }
    public string Abi { get; internal set; } = "Rust";
    public bool HasSelfParameter => !Parameters.IsEmpty && Parameters[0].IsSelf;
    public NamedTypeSymbol? SelfType { get; internal set; }
    internal BindingScope Scope { get; }
    internal int SignatureState;
    internal FunctionSymbol(string name, Symbol owner, FunctionItemSyntax syntax, SyntaxTree tree, BindingScope parent, TypeSymbol error)
        : base(SymbolKind.Function, name, owner, syntax, tree) { Declaration = syntax; ReturnType = error; Scope = new BindingScope(parent, this, tree, syntax.Span); }
}

public abstract class VariableSymbol : Symbol
{
    private TypeSymbol _type;
    public TypeSymbol Type { get => _type.Resolve(); internal set => _type = value; }
    public bool IsMutable { get; internal set; }
    protected VariableSymbol(SymbolKind kind, string name, Symbol owner, SyntaxNode syntax, SyntaxTree tree, TypeSymbol type, bool mutable = false)
        : base(kind, name, owner, syntax, tree) { _type = type; IsMutable = mutable; }
}

public sealed class ParameterSymbol : VariableSymbol
{
    public new ParameterSyntax Declaration { get; }
    public int Ordinal { get; }
    public bool IsSelf { get; }
    internal ParameterSymbol(string name, FunctionSymbol owner, ParameterSyntax syntax, SyntaxTree tree, TypeSymbol type, int ordinal, bool mutable)
        : base(SymbolKind.Parameter, name, owner, syntax, tree, type, mutable) { Declaration = syntax; Ordinal = ordinal; IsSelf = syntax.IsSelf; }
}

public sealed class LocalSymbol : VariableSymbol
{
    internal LocalSymbol(string name, Symbol owner, SyntaxNode syntax, SyntaxTree tree, TypeSymbol type, bool mutable)
        : base(SymbolKind.Local, name, owner, syntax, tree, type, mutable) { }
}

public sealed class GlobalSymbol : VariableSymbol
{
    public new ItemSyntax Declaration { get; }
    public ConstantValue ConstantValue { get; internal set; }
    internal int BindingState;
    internal BindingScope Scope { get; }
    internal GlobalSymbol(string name, bool isStatic, ModuleSymbol module, ItemSyntax syntax, SyntaxTree tree, TypeSymbol error, bool mutable)
        : base(isStatic ? SymbolKind.Static : SymbolKind.Constant, name, module, syntax, tree, error, mutable) { Declaration = syntax; Scope = module.Scope; }
}

public sealed class FieldSymbol : VariableSymbol
{
    public int Ordinal { get; }
    internal FieldSymbol(string name, Symbol owner, FieldSyntax syntax, SyntaxTree tree, TypeSymbol type, int ordinal)
        : base(SymbolKind.Field, name, owner, syntax, tree, type) => Ordinal = ordinal;
}

public sealed class VariantSymbol : Symbol
{
    public AggregateSymbol Enum { get; }
    public AggregateShape Shape { get; }
    public ImmutableArray<FieldSymbol> Fields { get; internal set; } = [];
    public System.Numerics.BigInteger Discriminant { get; internal set; }
    internal VariantSymbol(string name, AggregateSymbol owner, EnumVariantSyntax syntax, SyntaxTree tree, AggregateShape shape)
        : base(SymbolKind.Variant, name, owner, syntax, tree) { Enum = owner; Shape = shape; IsPublic = true; }
}

public sealed class LabelSymbol : Symbol
{
    public bool IsLoop { get; }
    internal LabelSymbol(string name, Symbol owner, SyntaxNode syntax, SyntaxTree tree, bool isLoop)
        : base(SymbolKind.Label, name, owner, syntax, tree) => IsLoop = isLoop;
}

public sealed class ImportSymbol : Symbol
{
    public Symbol Target { get; }
    public SymbolNamespace Namespace { get; }
    public bool IsGlob { get; }
    internal ImportSymbol(string name, Symbol target, ModuleSymbol module, UseTreeSyntax syntax, SyntaxTree tree, SymbolNamespace space, bool glob)
        : base(SymbolKind.Import, name, module, syntax, tree) { Target = target; Namespace = space; IsGlob = glob; }
}

internal enum InferenceKind : byte { Any, Integer, Float }
internal sealed class InferenceTypeSymbol : TypeSymbol
{
    internal TypeSymbol? Binding;
    internal TypeSymbol? DefaultType;
    internal bool HasNeverConstraint;
    internal InferenceKind Constraint;
    internal readonly SyntaxNode Origin;
    internal InferenceTypeSymbol(InferenceKind constraint, SyntaxNode origin) { Constraint = constraint; Origin = origin; }
    public override TypeKind Kind => Binding?.Resolve().Kind ?? TypeKind.Inference;
    public override string Name => Binding?.Resolve().Name ?? (Constraint == InferenceKind.Any ? "_" : "{" + Constraint.ToString().ToLowerInvariant() + "}");
    public override TypeSymbol Resolve() { if (Binding is null) return this; return Binding = Binding.Resolve(); }
}

internal readonly record struct ScopeEntry(string Name, SymbolNamespace Namespace, Symbol Symbol, int VisibleFrom);
internal sealed class BindingScope
{
    internal readonly BindingScope? Parent;
    internal readonly Symbol Owner;
    internal readonly SyntaxTree Tree;
    internal readonly TextSpan Span;
    internal readonly List<ScopeEntry> Entries = new();
    internal BindingScope(BindingScope? parent, Symbol owner, SyntaxTree tree, TextSpan span) { Parent = parent; Owner = owner; Tree = tree; Span = span; }
    internal void Add(string name, SymbolNamespace space, Symbol symbol, int visibleFrom = 0) => Entries.Add(new ScopeEntry(name, space, symbol, visibleFrom));
    internal ImmutableArray<Symbol> Find(string name, SymbolNamespace space, int position = int.MaxValue)
    {
        var matches = Entries.Where(e => e.Name == name && e.Namespace == space && e.VisibleFrom <= position).Select(e => e.Symbol).ToArray();
        var explicitMatches = matches.Where(s => s is not ImportSymbol { IsGlob: true }).ToArray();
        if (explicitMatches.Length != 0) return [explicitMatches[^1]];
        if (matches.Length != 0) return matches.DistinctBy(s => s is ImportSymbol import ? import.Target : s).ToImmutableArray();
        return Parent?.Find(name, space, position) ?? [];
    }
}

internal sealed class TypeSystem
{
    internal readonly Dictionary<string, PrimitiveTypeSymbol> Primitives = new(StringComparer.Ordinal);
    internal readonly PrimitiveTypeSymbol Error = new("<error>", TypeKind.Error);
    internal readonly PrimitiveTypeSymbol Never = new("!", TypeKind.Never);
    internal readonly TupleTypeSymbol Unit = new([]);
    internal readonly LifetimeSymbol StaticLifetime = new("'static");
    internal PrimitiveTypeSymbol Bool => Primitives["bool"];
    internal PrimitiveTypeSymbol I32 => Primitives["i32"];
    internal PrimitiveTypeSymbol Usize => Primitives["usize"];
    internal PrimitiveTypeSymbol F64 => Primitives["f64"];
    internal TypeSystem(int pointerWidth)
    {
        foreach (int bits in new[] { 8, 16, 32, 64, 128 })
        {
            Primitives.Add("i" + bits, new("i" + bits, TypeKind.Integer, bits, true));
            Primitives.Add("u" + bits, new("u" + bits, TypeKind.Integer, bits));
        }
        Primitives.Add("isize", new("isize", TypeKind.Integer, pointerWidth, true));
        Primitives.Add("usize", new("usize", TypeKind.Integer, pointerWidth));
        Primitives.Add("f32", new("f32", TypeKind.Float, 32)); Primitives.Add("f64", new("f64", TypeKind.Float, 64));
        Primitives.Add("bool", new("bool", TypeKind.Boolean)); Primitives.Add("char", new("char", TypeKind.Character)); Primitives.Add("str", new("str", TypeKind.String));
    }

    internal TypeSymbol Substitute(TypeSymbol type, IReadOnlyDictionary<TypeParameterSymbol, TypeSymbol> substitution)
    {
        type = type.Resolve();
        return type switch
        {
            ParameterTypeSymbol p when substitution.TryGetValue(p.Parameter, out TypeSymbol? value) => value,
            NamedTypeSymbol n => new NamedTypeSymbol(n.Definition, n.TypeArguments.Select(t => Substitute(t, substitution)).ToImmutableArray()),
            TupleTypeSymbol t => new TupleTypeSymbol(t.Elements.Select(e => Substitute(e, substitution)).ToImmutableArray()),
            SequenceTypeSymbol s => new SequenceTypeSymbol(s.Kind, Substitute(s.ElementType, substitution), s.Length, s.RangeForm),
            IndirectTypeSymbol r => new IndirectTypeSymbol(r.Kind, Substitute(r.ElementType, substitution), r.IsMutable, r.Lifetime),
            FunctionTypeSymbol f => new FunctionTypeSymbol(f.ParameterTypes.Select(t => Substitute(t, substitution)).ToImmutableArray(), Substitute(f.ReturnType, substitution), f.IsUnsafe, f.Abi),
            _ => type
        };
    }
    internal TypeSymbol FieldType(FieldSymbol field, NamedTypeSymbol receiver)
        => Substitute(field.Type, receiver.Definition.TypeParameters.Zip(receiver.TypeArguments).ToDictionary(p => p.First, p => p.Second));

    internal static bool Same(TypeSymbol left, TypeSymbol right)
    {
        left = left.Resolve(); right = right.Resolve();
        if (ReferenceEquals(left, right)) return true;
        if (left.Kind != right.Kind) return false;
        return (left, right) switch
        {
            (NamedTypeSymbol a, NamedTypeSymbol b) => ReferenceEquals(a.Definition, b.Definition) && SameList(a.TypeArguments, b.TypeArguments),
            (TupleTypeSymbol a, TupleTypeSymbol b) => SameList(a.Elements, b.Elements),
            (SequenceTypeSymbol a, SequenceTypeSymbol b) => a.Length == b.Length && a.RangeForm == b.RangeForm && Same(a.ElementType, b.ElementType),
            (IndirectTypeSymbol a, IndirectTypeSymbol b) => a.IsMutable == b.IsMutable && Same(a.ElementType, b.ElementType),
            (FunctionTypeSymbol a, FunctionTypeSymbol b) => a.IsUnsafe == b.IsUnsafe && a.Abi == b.Abi && SameList(a.ParameterTypes, b.ParameterTypes) && Same(a.ReturnType, b.ReturnType),
            _ => false
        };
    }
    private static bool SameList(ImmutableArray<TypeSymbol> left, ImmutableArray<TypeSymbol> right)
        => left.Length == right.Length && left.Zip(right).All(p => Same(p.First, p.Second));
}
