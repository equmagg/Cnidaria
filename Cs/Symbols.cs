using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Cnidaria.Cs
{
    public enum AttributeApplicationTarget : byte
    {
        Default = 0,
        Assembly,
        Module,
        Class,
        Struct,
        Enum,
        Constructor,
        Method,
        Property,
        Field,
        Event,
        Interface,
        Parameter,
        Delegate,
        ReturnValue,
        GenericParameter,
        Unknown
    }
    public enum Accessibility : byte
    {
        NotApplicable = 0,
        Private,
        Protected,
        Internal,
        ProtectedOrInternal,   // protected internal
        ProtectedAndInternal,  // private protected
        Public
    }
    public readonly struct TypedConstant
    {
        public TypeSymbol Type { get; }
        public object? Value { get; }

        public TypedConstant(TypeSymbol type, object? value)
        {
            Type = type;
            Value = value;
        }
    }
    public readonly struct AttributeNamedArgumentData
    {
        public string Name { get; }
        public Symbol Member { get; } // FieldSymbol or PropertySymbol
        public TypedConstant Value { get; }

        public AttributeNamedArgumentData(string name, Symbol member, TypedConstant value)
        {
            Name = name;
            Member = member;
            Value = value;
        }
    }
    public sealed class AttributeData
    {
        public NamedTypeSymbol AttributeClass { get; }
        public MethodSymbol Constructor { get; }
        public ImmutableArray<TypedConstant> ConstructorArguments { get; }
        public ImmutableArray<AttributeNamedArgumentData> NamedArguments { get; }
        public AttributeApplicationTarget Target { get; }

        public AttributeData(
            NamedTypeSymbol attributeClass,
            MethodSymbol constructor,
            ImmutableArray<TypedConstant> constructorArguments,
            ImmutableArray<AttributeNamedArgumentData> namedArguments,
            AttributeApplicationTarget target)
        {
            AttributeClass = attributeClass;
            Constructor = constructor;
            ConstructorArguments = constructorArguments.IsDefault ? ImmutableArray<TypedConstant>.Empty : constructorArguments;
            NamedArguments = namedArguments.IsDefault ? ImmutableArray<AttributeNamedArgumentData>.Empty : namedArguments;
            Target = target;
        }
    }
    public abstract class Symbol
    {
        public abstract SymbolKind Kind { get; }
        public abstract string Name { get; }
        public abstract Symbol? ContainingSymbol { get; }
        public abstract ImmutableArray<Location> Locations { get; }
        public virtual Accessibility DeclaredAccessibility => Accessibility.NotApplicable;
        public virtual ImmutableArray<SyntaxReference> DeclaringSyntaxReferences => ImmutableArray<SyntaxReference>.Empty;
        public virtual ImmutableArray<AttributeData> GetAttributes() => ImmutableArray<AttributeData>.Empty;
        public virtual bool IsFromMetadata => false;
        public override string ToString() => $"{Kind} {Name}";
    }
    public abstract class NamespaceSymbol : Symbol
    {
        public sealed override SymbolKind Kind => SymbolKind.Namespace;

        public abstract bool IsGlobalNamespace { get; }
        public abstract ImmutableArray<NamespaceSymbol> GetNamespaceMembers();
        public abstract ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name, int arity);
        public abstract ImmutableArray<NamedTypeSymbol> GetTypeMembers();
        public ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name)
            => GetTypeMembers(name, arity: -1);
    }

    public abstract class TypeSymbol : Symbol
    {
        public override SymbolKind Kind => SymbolKind.NamedType;
        public virtual bool IsReferenceType => false;
        public virtual bool IsValueType => false;
        public virtual bool IsRefLikeType => false;
        public virtual SpecialType SpecialType => SpecialType.None;
        public virtual TypeSymbol? BaseType => null;
        public virtual ImmutableArray<TypeSymbol> Interfaces => ImmutableArray<TypeSymbol>.Empty;
    }
    internal sealed class NullTypeSymbol : TypeSymbol
    {
        public static readonly NullTypeSymbol Instance = new();

        public override SymbolKind Kind => SymbolKind.Error;
        public override string Name => "<null>";
        public override Symbol? ContainingSymbol => null;
        public override ImmutableArray<Location> Locations => ImmutableArray<Location>.Empty;

        public override bool IsReferenceType => false;
        public override bool IsValueType => false;

        private NullTypeSymbol() { }
    }
    internal sealed class ThrowTypeSymbol : TypeSymbol
    {
        public static readonly ThrowTypeSymbol Instance = new();
        public override SymbolKind Kind => SymbolKind.Error;
        public override string Name => "<throw>";
        public override Symbol? ContainingSymbol => null;
        public override ImmutableArray<Location> Locations => ImmutableArray<Location>.Empty;
        public override bool IsReferenceType => false;
        public override bool IsValueType => false;
        private ThrowTypeSymbol() { }
    }
    public abstract class NamedTypeSymbol : TypeSymbol
    {
        public abstract TypeKind TypeKind { get; }
        public abstract int Arity { get; }
        public virtual bool IsReadOnlyStruct => false;
        public abstract ImmutableArray<TypeParameterSymbol> TypeParameters { get; }
        public virtual ImmutableArray<TypeSymbol> TypeArguments
        {
            get
            {
                var tps = TypeParameters;
                if (tps.IsDefaultOrEmpty)
                    return ImmutableArray<TypeSymbol>.Empty;

                var b = ImmutableArray.CreateBuilder<TypeSymbol>(tps.Length);
                for (int i = 0; i < tps.Length; i++)
                    b.Add(tps[i]);
                return b.ToImmutable();
            }
        }
        public virtual NamedTypeSymbol OriginalDefinition => this;
        public virtual TypeSymbol? EnumUnderlyingType => null;

        public abstract ImmutableArray<Symbol> GetMembers();
        public abstract ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name, int arity);
        public ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name)
            => GetTypeMembers(name, arity: -1);
    }
    [Flags]
    public enum GenericConstraintsFlags : byte
    {
        None = 0,
        UnmanagedConstraint = 1 << 0,
        NotNullConstraint = 1 << 1,
        StructConstraint = 1 << 2,
        AllowsRefStruct = 1 << 3,
    }

    public sealed class TypeParameterSymbol : TypeSymbol
    {
        public override SymbolKind Kind => SymbolKind.TypeParameter;
        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations { get; }
        public int Ordinal { get; }
        public GenericConstraintsFlags GenericConstraint { get; private set; }
        private List<AttributeData>? _attributes;
        public override bool IsValueType => (GenericConstraint & GenericConstraintsFlags.StructConstraint) != 0;
        public TypeParameterSymbol(string name, Symbol? containing, int ordinal, ImmutableArray<Location> locations)
        {
            Name = name;
            ContainingSymbol = containing;
            Ordinal = ordinal;
            Locations = locations;
        }
        public override ImmutableArray<AttributeData> GetAttributes()
            => _attributes is null ? ImmutableArray<AttributeData>.Empty : _attributes.ToImmutableArray();
        internal void AddAttribute(AttributeData a)
        {
            (_attributes ??= new List<AttributeData>()).Add(a);
        }
        internal bool TrySetConstraint(GenericConstraintsFlags constraint)
        {
            if ((GenericConstraint & constraint) != 0)
                return false;
            GenericConstraint |= constraint;
            return true;
        }
    }
    public sealed class ArrayTypeSymbol : TypeSymbol
    {
        private readonly NamedTypeSymbol _arrayBase;

        public override SymbolKind Kind => SymbolKind.ArrayType;
        public override string Name
            => Rank == 1
                ? $"{ElementType.Name}[]"
                : $"{ElementType.Name}[{new string(',', Rank - 1)}]";

        public override Symbol? ContainingSymbol => null;
        public override ImmutableArray<Location> Locations => ImmutableArray<Location>.Empty;

        public override bool IsReferenceType => true;
        public override TypeSymbol? BaseType => _arrayBase;

        public TypeSymbol ElementType { get; }
        public int Rank { get; }

        public ArrayTypeSymbol(TypeSymbol elementType, int rank, NamedTypeSymbol arrayBase)
        {
            ElementType = elementType;
            Rank = rank;
            _arrayBase = arrayBase;
        }
    }
    public sealed class PointerTypeSymbol : TypeSymbol
    {
        public override SymbolKind Kind => SymbolKind.PointerType;
        public override string Name => $"{PointedAtType.Name}*";
        public override Symbol? ContainingSymbol => null;
        public override ImmutableArray<Location> Locations => ImmutableArray<Location>.Empty;

        public TypeSymbol PointedAtType { get; }

        public PointerTypeSymbol(TypeSymbol pointedAtType)
            => PointedAtType = pointedAtType;
    }
    public sealed class ByRefTypeSymbol : TypeSymbol
    {
        public override SymbolKind Kind => SymbolKind.ByRefType;
        public override string Name => $"{ElementType.Name}&";
        public override Symbol? ContainingSymbol => null;
        public override ImmutableArray<Location> Locations => ImmutableArray<Location>.Empty;
        public TypeSymbol ElementType { get; }
        public ByRefTypeSymbol(TypeSymbol elementType)
            => ElementType = elementType;
    }
    public sealed class TupleTypeSymbol : NamedTypeSymbol
    {
        private readonly TypeSymbol _baseType;

        private ImmutableArray<Symbol> _members;
        private bool _membersInitialized;
        public override Symbol? ContainingSymbol => null;
        public override ImmutableArray<Location> Locations => ImmutableArray<Location>.Empty;

        public override TypeKind TypeKind => TypeKind.Struct;
        public override int Arity => 0;
        public override ImmutableArray<TypeParameterSymbol> TypeParameters => ImmutableArray<TypeParameterSymbol>.Empty;

        public override bool IsReferenceType => false;
        public override bool IsValueType => true;
        public override TypeSymbol? BaseType => _baseType;

        public ImmutableArray<TypeSymbol> ElementTypes { get; }
        public ImmutableArray<string?> ElementNames { get; }
        public int Cardinality => ElementTypes.Length;

        public override string Name
        {
            get
            {
                if (ElementTypes.IsDefaultOrEmpty)
                    return "()";

                var sb = new StringBuilder();
                sb.Append('(');
                for (int i = 0; i < ElementTypes.Length; i++)
                {
                    if (i != 0) sb.Append(", ");
                    sb.Append(ElementTypes[i].Name);

                    var n = ElementNames.IsDefault ? null : ElementNames[i];
                    if (!string.IsNullOrEmpty(n))
                    {
                        sb.Append(' ');
                        sb.Append(n);
                    }
                }
                sb.Append(')');
                return sb.ToString();
            }
        }

        internal TupleTypeSymbol(
            ImmutableArray<TypeSymbol> elementTypes,
            ImmutableArray<string?> elementNames,
            TypeSymbol baseType)
        {
            ElementTypes = elementTypes;
            ElementNames = elementNames;
            _baseType = baseType;
        }

        public override ImmutableArray<Symbol> GetMembers()
        {
            if (_membersInitialized)
                return _members;

            _membersInitialized = true;

            if (ElementTypes.IsDefaultOrEmpty)
            {
                _members = ImmutableArray<Symbol>.Empty;
                return _members;
            }

            var b = ImmutableArray.CreateBuilder<Symbol>(ElementTypes.Length * 2);

            for (int i = 0; i < ElementTypes.Length; i++)
            {
                string itemName = "Item" + (i + 1).ToString();
                b.Add(new TupleElementFieldSymbol(itemName, this, i, ElementTypes[i]));

                var n = ElementNames.IsDefault ? null : ElementNames[i];
                if (!string.IsNullOrEmpty(n) && !StringComparer.Ordinal.Equals(n, itemName))
                    b.Add(new TupleElementFieldSymbol(n!, this, i, ElementTypes[i]));
            }

            _members = b.ToImmutable();
            return _members;
        }

        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name, int arity)
            => ImmutableArray<NamedTypeSymbol>.Empty;
    }
    internal sealed class TupleElementFieldSymbol : FieldSymbol
    {
        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations => ImmutableArray<Location>.Empty;

        public override TypeSymbol Type { get; }
        public override bool IsStatic => false;
        public override bool IsConst => false;
        public override Optional<object> ConstantValueOpt => Optional<object>.None;

        public int ElementIndex { get; }

        public TupleElementFieldSymbol(string name, TupleTypeSymbol containingTuple, int elementIndex, TypeSymbol elementType)
        {
            Name = name;
            ContainingSymbol = containingTuple;
            ElementIndex = elementIndex;
            Type = elementType;
        }
    }
    public sealed class ErrorTypeSymbol : NamedTypeSymbol
    {
        public override SymbolKind Kind => SymbolKind.Error;
        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations { get; }
        public override TypeKind TypeKind => TypeKind.Error;
        public override int Arity => 0;
        public ErrorTypeSymbol(string name, Symbol? containing, ImmutableArray<Location> locations)
        {
            Name = name;
            ContainingSymbol = containing;
            Locations = locations;
        }

        public override ImmutableArray<TypeParameterSymbol> TypeParameters => ImmutableArray<TypeParameterSymbol>.Empty;
        public override ImmutableArray<Symbol> GetMembers() => ImmutableArray<Symbol>.Empty;
        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name, int arity)
            => ImmutableArray<NamedTypeSymbol>.Empty;
    }
    public abstract class FieldSymbol : Symbol
    {
        public sealed override SymbolKind Kind => SymbolKind.Field;
        public abstract TypeSymbol Type { get; }
        public abstract bool IsStatic { get; }
        public abstract bool IsConst { get; }
        public virtual bool IsReadOnly => false;
        public abstract Optional<object> ConstantValueOpt { get; }
    }
    public abstract class PropertySymbol : Symbol
    {
        public sealed override SymbolKind Kind => SymbolKind.Property;
        public abstract TypeSymbol Type { get; }
        public abstract bool IsStatic { get; }
        public abstract bool HasGet { get; }
        public abstract bool HasSet { get; }
        public abstract MethodSymbol? GetMethod { get; }
        public abstract MethodSymbol? SetMethod { get; }
        public virtual ImmutableArray<ParameterSymbol> Parameters => ImmutableArray<ParameterSymbol>.Empty;
    }
    public abstract class MethodSymbol : Symbol
    {
        public sealed override SymbolKind Kind => SymbolKind.Method;
        public abstract TypeSymbol ReturnType { get; }
        public abstract ImmutableArray<ParameterSymbol> Parameters { get; }
        public abstract ImmutableArray<TypeParameterSymbol> TypeParameters { get; }
        public abstract bool IsStatic { get; }
        public abstract bool IsConstructor { get; }
        public abstract bool IsAsync { get; }
        public virtual bool IsVirtual => false;
        public virtual bool IsAbstract => false;
        public virtual bool IsOverride => false;
        public virtual bool IsSealed => false;
        public virtual MethodSymbol? OverriddenMethod => null;
        public virtual MethodSymbol OriginalDefinition => this;
        public virtual ImmutableArray<TypeSymbol> TypeArguments
        {
            get
            {
                var tps = TypeParameters;
                if (tps.IsDefaultOrEmpty)
                    return ImmutableArray<TypeSymbol>.Empty;

                var b = ImmutableArray.CreateBuilder<TypeSymbol>(tps.Length);
                for (int i = 0; i < tps.Length; i++)
                    b.Add(tps[i]);
                return b.ToImmutable();
            }
        }
        public virtual bool IsSpecialName
            => IsConstructor
            || Name.StartsWith("get_", StringComparison.Ordinal)
            || Name.StartsWith("set_", StringComparison.Ordinal)
            || Name.StartsWith("op_", StringComparison.Ordinal);

        public virtual bool IsRuntimeSpecialName => IsConstructor;
    }
    internal sealed class SynthesizedConstructorSymbol : MethodSymbol
    {
        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations => ImmutableArray<Location>.Empty;

        public override TypeSymbol ReturnType { get; }
        public override ImmutableArray<ParameterSymbol> Parameters { get; }
        public override ImmutableArray<TypeParameterSymbol> TypeParameters => ImmutableArray<TypeParameterSymbol>.Empty;

        public override bool IsStatic { get; }
        public override bool IsConstructor => true;
        public override bool IsAsync => false;
        public SynthesizedConstructorSymbol(
            NamedTypeSymbol containing,
            TypeSymbol voidType,
            bool isStatic,
            ImmutableArray<ParameterSymbol> parameters)
        {
            ContainingSymbol = containing;
            Name = containing.Name;
            ReturnType = voidType;
            IsStatic = isStatic;
            Parameters = parameters.IsDefault ? ImmutableArray<ParameterSymbol>.Empty : parameters;
        }
    }
    internal sealed class SynthesizedBackingFieldSymbol : FieldSymbol
    {
        private TypeSymbol _type;

        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations => ImmutableArray<Location>.Empty;

        public override bool IsStatic { get; }
        public override bool IsConst => false;
        public override bool IsReadOnly { get; }
        public override Optional<object> ConstantValueOpt => Optional<object>.None;
        public override TypeSymbol Type => _type;

        public SynthesizedBackingFieldSymbol(
            string name,
            Symbol containing,
            TypeSymbol placeholderType,
            bool isStatic,
            bool isReadOnly)
        {
            Name = name;
            ContainingSymbol = containing;
            _type = placeholderType;
            IsStatic = isStatic;
            IsReadOnly = isReadOnly;
        }

        internal void SetType(TypeSymbol type) => _type = type;
    }
    public enum ParameterRefKind : byte
    {
        None = 0,
        Ref,
        Out,
        In
    }
    public sealed class ParameterSymbol : Symbol
    {
        public override SymbolKind Kind => SymbolKind.Parameter;
        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations { get; }
        public TypeSymbol Type { get; internal set; }
        private List<AttributeData>? _attributes;
        public bool IsReadOnlyRef { get; internal set; }
        public bool IsScoped { get; internal set; }
        public bool IsParams { get; internal set; }
        public ParameterRefKind RefKind { get; internal set; }
        public ParameterSymbol(
            string name,
            Symbol containing,
            TypeSymbol type,
            ImmutableArray<Location> locations,
            bool isReadOnlyRef = false,
            ParameterRefKind refKind = ParameterRefKind.None,
            bool isScoped = false,
            bool isParams = false)
        {
            Name = name;
            ContainingSymbol = containing;
            Type = type;
            Locations = locations;
            IsReadOnlyRef = isReadOnlyRef;
            RefKind = refKind;
            IsScoped = isScoped;
            IsParams = isParams;
        }
        public override ImmutableArray<AttributeData> GetAttributes()
        => _attributes is null ? ImmutableArray<AttributeData>.Empty : _attributes.ToImmutableArray();
        internal void AddAttribute(AttributeData a)
        {
            (_attributes ??= new List<AttributeData>()).Add(a);
        }
    }
    public sealed class LocalSymbol : Symbol
    {
        public override SymbolKind Kind => SymbolKind.Local;
        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations { get; }
        public TypeSymbol Type { get; }
        public bool IsConst { get; }
        public bool IsByRef { get; }
        public Optional<object> ConstantValueOpt { get; }
        public LocalSymbol(
            string name,
            Symbol containing,
            TypeSymbol type,
            ImmutableArray<Location> locations,
            bool isByRef = false,
            bool isConst = false,
            Optional<object> constantValueOpt = default)
        {
            Name = name;
            ContainingSymbol = containing;
            Type = type;
            Locations = locations;

            IsByRef = isByRef;
            IsConst = isConst;
            ConstantValueOpt = isConst ? constantValueOpt : Optional<object>.None;
        }
    }
    public sealed class AliasSymbol : Symbol
    {
        public override SymbolKind Kind => SymbolKind.Alias;
        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations { get; }
        public Symbol Target { get; }

        public AliasSymbol(string name, Symbol? containing, Symbol target, ImmutableArray<Location> locations)
        {
            Name = name;
            ContainingSymbol = containing;
            Target = target;
            Locations = locations;
        }
    }



    internal sealed class SourceNamespaceSymbol : NamespaceSymbol
    {
        private readonly Dictionary<string, SourceNamespaceSymbol> _namespaces = new(StringComparer.Ordinal);
        private readonly Dictionary<(string name, int arity), List<NamedTypeSymbol>> _typesByName = new();

        private readonly List<SyntaxReference> _declRefs = new();
        private readonly List<Location> _locations = new();

        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override bool IsGlobalNamespace { get; }
        public override ImmutableArray<Location> Locations => _locations.ToImmutableArray();
        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences => _declRefs.ToImmutableArray();

        public SourceNamespaceSymbol(string name, Symbol? containing, bool isGlobal)
        {
            Name = name;
            ContainingSymbol = containing;
            IsGlobalNamespace = isGlobal;
        }

        public void AddDeclaration(SyntaxTree tree, SyntaxNode node)
        {
            _declRefs.Add(new SyntaxReference(tree, node));
            _locations.Add(new Location(tree, node.Span));
        }

        public SourceNamespaceSymbol GetOrAddNamespace(string name, Func<SourceNamespaceSymbol> factory)
        {
            if (!_namespaces.TryGetValue(name, out var ns))
                _namespaces[name] = ns = factory();
            return ns;
        }

        public void AddType(NamedTypeSymbol type)
        {
            var key = (type.Name, type.Arity);
            if (!_typesByName.TryGetValue(key, out var list))
                _typesByName[key] = list = new List<NamedTypeSymbol>();
            list.Add(type);
        }

        public override ImmutableArray<NamespaceSymbol> GetNamespaceMembers()
        {
            return _namespaces.Values.Cast<NamespaceSymbol>().ToImmutableArray();
        }

        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name, int arity)
        {
            if (arity >= 0)
                return _typesByName.TryGetValue((name, arity), out var list)
                    ? list.ToImmutableArray()
                    : ImmutableArray<NamedTypeSymbol>.Empty;

            // arity == -1 => any
            return _typesByName
                .Where(kv => StringComparer.Ordinal.Equals(kv.Key.name, name))
                .SelectMany(kv => kv.Value)
                .ToImmutableArray();
        }
        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers()
        {
            if (_typesByName.Count == 0)
                return ImmutableArray<NamedTypeSymbol>.Empty;
            var b = ImmutableArray.CreateBuilder<NamedTypeSymbol>();
            foreach (var kv in _typesByName)
            {
                var list = kv.Value;
                for (int i = 0; i < list.Count; i++)
                    b.Add(list[i]);
            }
            return b.ToImmutable();
        }
    }
    internal sealed class SourceFieldSymbol : FieldSymbol
    {
        private TypeSymbol _type;
        private Optional<object> _constantValueOpt;

        private readonly List<Location> _locations = new();
        private readonly List<SyntaxReference> _declRefs = new();
        private readonly List<AttributeData> _attributes = new();
        internal SyntaxReference AttributeOwnerDeclarationRef { get; }
        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations => _locations.ToImmutableArray();
        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences => _declRefs.ToImmutableArray();
        public override bool IsStatic { get; }
        public override bool IsConst { get; }
        public override Optional<object> ConstantValueOpt => _constantValueOpt;
        public override TypeSymbol Type => _type;
        internal TypeSyntax? DeclaredTypeSyntax { get; }
        internal bool IsUnsafe { get; }
        public override Accessibility DeclaredAccessibility { get; }
        public override bool IsReadOnly { get; }
        public SourceFieldSymbol(
            string name,
            Symbol containing,
            TypeSyntax? declaredTypeSyntax,
            TypeSymbol placeholderType,
            bool isStatic,
            bool isConst,
            bool isReadOnly,
            bool isUnsafe,
            Accessibility declaredAccessibility,
            Location location,
            SyntaxReference declarationRef,
            SyntaxReference attributeOwnerDeclarationRef)
        {
            Name = name;
            ContainingSymbol = containing;

            DeclaredTypeSyntax = declaredTypeSyntax;
            _type = placeholderType;

            IsStatic = isStatic;
            IsConst = isConst;
            IsReadOnly = isReadOnly;
            IsUnsafe = isUnsafe;

            _constantValueOpt = Optional<object>.None;
            DeclaredAccessibility = declaredAccessibility;
            _locations.Add(location);
            _declRefs.Add(declarationRef);
            AttributeOwnerDeclarationRef = attributeOwnerDeclarationRef;
        }
        public override ImmutableArray<AttributeData> GetAttributes() => _attributes.ToImmutableArray();
        internal void AddAttribute(AttributeData a) => _attributes.Add(a);
        internal void SetType(TypeSymbol type) => _type = type;
        internal void SetConstantValue(Optional<object> constantValueOpt) => _constantValueOpt = constantValueOpt;

        internal void AddDeclaration(Location location, SyntaxReference declarationRef)
        {
            _locations.Add(location);
            _declRefs.Add(declarationRef);
        }
    }
    internal sealed class SourcePropertySymbol : PropertySymbol
    {
        private TypeSymbol _type;
        private readonly List<AttributeData> _attributes = new();
        private readonly List<Location> _locations = new();
        private readonly List<SyntaxReference> _declRefs = new();
        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations => _locations.ToImmutableArray();
        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences => _declRefs.ToImmutableArray();
        public override TypeSymbol Type => _type;
        public override bool IsStatic { get; }
        public override bool HasGet { get; }
        public override bool HasSet { get; }
        public override MethodSymbol? GetMethod { get; }
        public override MethodSymbol? SetMethod { get; }
        private ImmutableArray<ParameterSymbol> _parameters;
        public override ImmutableArray<ParameterSymbol> Parameters => _parameters;
        internal TypeSyntax DeclaredTypeSyntax { get; }
        internal FieldSymbol? BackingFieldOpt { get; private set; }
        public override Accessibility DeclaredAccessibility { get; }
        public SourcePropertySymbol(
            string name,
            Symbol containing,
            TypeSyntax declaredTypeSyntax,
            TypeSymbol placeholderType,
            bool isStatic,
            Accessibility declaredAccessibility,
            bool hasGet,
            bool hasSet,
            MethodSymbol? getMethod,
            MethodSymbol? setMethod,
            Location location,
            SyntaxReference declarationRef)
        {
            Name = name;
            ContainingSymbol = containing;

            DeclaredTypeSyntax = declaredTypeSyntax;
            _type = placeholderType;

            IsStatic = isStatic;
            HasGet = hasGet;
            HasSet = hasSet;
            GetMethod = getMethod;
            SetMethod = setMethod;
            DeclaredAccessibility = declaredAccessibility;

            _parameters = ImmutableArray<ParameterSymbol>.Empty;

            _locations.Add(location);
            _declRefs.Add(declarationRef);
        }
        internal void SetType(TypeSymbol type) => _type = type;
        internal void SetParameters(ImmutableArray<ParameterSymbol> parameters)
            => _parameters = parameters.IsDefault ? ImmutableArray<ParameterSymbol>.Empty : parameters;
        internal void AddDeclaration(Location location, SyntaxReference declarationRef)
        {
            _locations.Add(location);
            _declRefs.Add(declarationRef);
        }
        internal void SetBackingField(FieldSymbol field)
        {
            BackingFieldOpt ??= field;
        }
        public override ImmutableArray<AttributeData> GetAttributes() => _attributes.ToImmutableArray();

        internal void AddAttribute(AttributeData a) => _attributes.Add(a);
    }
    internal sealed class SourceNamedTypeSymbol : NamedTypeSymbol
    {
        private readonly bool _isReadOnlyStruct;
        private readonly bool _isRefLikeType;
        private readonly int _arity;
        private readonly List<SyntaxReference> _declRefs = new();
        private readonly List<Location> _locations = new();
        private readonly List<Symbol> _members = new();
        private readonly Dictionary<(string name, int arity), List<NamedTypeSymbol>> _nestedTypesByName = new();

        private TypeSymbol? _defaultBaseType;
        private bool _defaultBaseTypeSet;

        private TypeSymbol? _declaredBaseType;
        private bool _declaredBaseTypeSet;

        private TypeSymbol? _enumUnderlyingType;
        private bool _enumUnderlyingTypeSet;

        private ImmutableArray<TypeParameterSymbol> _typeParameters;
        private readonly List<AttributeData> _attributes = new();
        public override bool IsRefLikeType => _isRefLikeType;
        public override bool IsReadOnlyStruct => _isReadOnlyStruct;
        public override int Arity => _arity;
        public override Accessibility DeclaredAccessibility { get; }
        public override bool IsFromMetadata { get; }
        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations => _locations.ToImmutableArray();
        public override TypeKind TypeKind { get; }

        public override TypeSymbol? BaseType
            => _declaredBaseTypeSet ? _declaredBaseType
                : _defaultBaseTypeSet ? _defaultBaseType
                : null;

        public override bool IsReferenceType =>
            TypeKind is TypeKind.Class or TypeKind.Interface or TypeKind.Delegate;

        public override bool IsValueType => TypeKind is TypeKind.Struct or TypeKind.Enum;

        public override ImmutableArray<TypeParameterSymbol> TypeParameters
            => _typeParameters.IsDefault ? ImmutableArray<TypeParameterSymbol>.Empty : _typeParameters;

        public override TypeSymbol? EnumUnderlyingType => _enumUnderlyingTypeSet ? _enumUnderlyingType : null;

        internal void SetEnumUnderlyingType(TypeSymbol underlyingType)
        {
            if (TypeKind != TypeKind.Enum)
                throw new InvalidOperationException("Enum underlying type can only be set on enum types.");

            if (_enumUnderlyingTypeSet)
                throw new InvalidOperationException($"Enum underlying type already set for '{Name}'.");

            _enumUnderlyingType = underlyingType;
            _enumUnderlyingTypeSet = true;
        }
        internal void SetDefaultBaseType(TypeSymbol? baseType)
        {
            if (_defaultBaseTypeSet) throw new InvalidOperationException("Default base type already set.");
            _defaultBaseTypeSet = true;
            _defaultBaseType = baseType;
        }

        internal void SetDeclaredBaseType(TypeSymbol? baseType)
        {
            if (_declaredBaseTypeSet) throw new InvalidOperationException("Declared base type already set.");
            _declaredBaseTypeSet = true;
            _declaredBaseType = baseType;
        }
        public void SetTypeParameters(ImmutableArray<TypeParameterSymbol> tps)
        {
            if (!_typeParameters.IsDefault) throw new InvalidOperationException("TypeParameters already set.");

            if (!tps.IsDefaultOrEmpty && tps.Length != _arity)
                throw new ArgumentException("TypeParameters length must match Arity.");

            if (tps.IsDefault && _arity != 0)
                throw new ArgumentException("TypeParameters must be set for generic types.");

            _typeParameters = tps;
        }
        public SourceNamedTypeSymbol(
            string name,
            Symbol? containing,
            TypeKind typeKind,
            int arity,
            Accessibility declaredAccessibility,
            bool isFromMetadata = false,
            bool isReadOnlyStruct = false,
            bool isRefLikeType = false)
        {
            Name = name;
            ContainingSymbol = containing;
            TypeKind = typeKind;

            _arity = arity < 0 ? throw new ArgumentOutOfRangeException(nameof(arity)) : arity;
            DeclaredAccessibility = declaredAccessibility;
            IsFromMetadata = isFromMetadata;
            _typeParameters = default;
            _isReadOnlyStruct = isReadOnlyStruct;
            _isRefLikeType = isRefLikeType;
        }

        public void AddDeclaration(SyntaxTree tree, SyntaxNode node)
        {
            _declRefs.Add(new SyntaxReference(tree, node));
            _locations.Add(new Location(tree, node.Span));
        }

        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences => _declRefs.ToImmutableArray();

        public void AddMember(Symbol member) => _members.Add(member);

        public void AddNestedType(NamedTypeSymbol type)
        {
            var key = (type.Name, type.Arity);
            if (!_nestedTypesByName.TryGetValue(key, out var list))
                _nestedTypesByName[key] = list = new List<NamedTypeSymbol>();
            list.Add(type);
            _members.Add(type);
        }

        public override ImmutableArray<Symbol> GetMembers() => _members.ToImmutableArray();

        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name, int arity)
        {
            if (arity >= 0)
                return _nestedTypesByName.TryGetValue((name, arity), out var list)
                    ? list.ToImmutableArray()
                    : ImmutableArray<NamedTypeSymbol>.Empty;

            return _nestedTypesByName
                .Where(kv => StringComparer.Ordinal.Equals(kv.Key.name, name))
                .SelectMany(kv => kv.Value)
                .ToImmutableArray();
        }
        public override ImmutableArray<AttributeData> GetAttributes() => _attributes.ToImmutableArray();

        internal void AddAttribute(AttributeData a) => _attributes.Add(a);
    }
    internal sealed class LocalFunctionSymbol : MethodSymbol
    {
        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations { get; }

        public LocalFunctionStatementSyntax Declaration { get; }

        private TypeSymbol _returnType;
        private ImmutableArray<ParameterSymbol> _parameters;

        public override TypeSymbol ReturnType => _returnType;
        public override ImmutableArray<ParameterSymbol> Parameters => _parameters;
        public override ImmutableArray<TypeParameterSymbol> TypeParameters => ImmutableArray<TypeParameterSymbol>.Empty;

        public override bool IsStatic { get; }
        public override bool IsConstructor => false;
        public override bool IsAsync { get; }

        private readonly SyntaxReference _declRef;
        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences => ImmutableArray.Create(_declRef);

        public LocalFunctionSymbol(
            string name,
            Symbol containing,
            LocalFunctionStatementSyntax declaration,
            SyntaxTree tree,
            ImmutableArray<Location> locations,
            bool isStatic,
            bool isAsync)
        {
            Name = name;
            ContainingSymbol = containing;
            Declaration = declaration;

            _declRef = new SyntaxReference(tree, declaration);
            Locations = locations.IsDefault ? ImmutableArray<Location>.Empty : locations;

            IsStatic = isStatic;
            IsAsync = isAsync;

            _returnType = new ErrorTypeSymbol("error", containing: null, locations: ImmutableArray<Location>.Empty);
            _parameters = ImmutableArray<ParameterSymbol>.Empty;
        }

        internal void SetSignature(TypeSymbol returnType, ImmutableArray<ParameterSymbol> parameters)
        {
            _returnType = returnType;
            _parameters = parameters.IsDefault ? ImmutableArray<ParameterSymbol>.Empty : parameters;
        }
    }
    internal sealed class SourceMethodSymbol : MethodSymbol
    {
        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations { get; }
        private TypeSymbol _returnType;
        public override TypeSymbol ReturnType => _returnType;
        private ImmutableArray<ParameterSymbol> _parameters;
        public override ImmutableArray<ParameterSymbol> Parameters => _parameters;
        private ImmutableArray<TypeParameterSymbol> _typeParameters;
        public override ImmutableArray<TypeParameterSymbol> TypeParameters
            => _typeParameters.IsDefault ? ImmutableArray<TypeParameterSymbol>.Empty : _typeParameters;
        public override Accessibility DeclaredAccessibility { get; }
        public override bool IsStatic { get; }
        public override bool IsConstructor { get; }
        public override bool IsAsync { get; }
        private bool _isVirtual;
        private bool _isAbstract;
        private bool _isOverride;
        private bool _isSealed;
        private MethodSymbol? _overridden;
        public override bool IsVirtual => _isVirtual;
        public override bool IsAbstract => _isAbstract;
        public override bool IsOverride => _isOverride;
        public override bool IsSealed => _isSealed;
        public override MethodSymbol? OverriddenMethod => _overridden;
        private readonly List<SyntaxReference> _declRefs = new();
        private readonly List<AttributeData> _attributes = new();
        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences => _declRefs.ToImmutableArray();

        public SourceMethodSymbol(
            string name,
            Symbol containing,
            TypeSymbol returnType,
            ImmutableArray<TypeParameterSymbol> typeParameters,
            bool isStatic,
            bool isConstructor,
            bool isAsync,
            ImmutableArray<Location> locations,
            Accessibility declaredAccessibility = Accessibility.Public)
        {
            Name = name;
            ContainingSymbol = containing;
            _returnType = returnType;
            _typeParameters = typeParameters;
            IsStatic = isStatic;
            IsConstructor = isConstructor;
            IsAsync = isAsync;
            Locations = locations;
            DeclaredAccessibility = declaredAccessibility;
            _parameters = default;
        }
        internal void SetDispatchFlags(bool isVirtual, bool isAbstract, bool isOverride, bool isSealed)
        {
            _isVirtual = isVirtual;
            _isAbstract = isAbstract;
            _isOverride = isOverride;
            _isSealed = isSealed;
        }
        internal void SetOverriddenMethod(MethodSymbol overridden) => _overridden = overridden;
        internal void SetTypeParameters(ImmutableArray<TypeParameterSymbol> typeParameters)
        {
            if (!_typeParameters.IsDefault) throw new InvalidOperationException("TypeParameters already set.");
            _typeParameters = typeParameters;
        }
        internal void SetReturnType(TypeSymbol type) => _returnType = type;
        public void SetParameters(ImmutableArray<ParameterSymbol> parameters)
        {
            if (!_parameters.IsDefault) throw new InvalidOperationException("Parameters already set.");
            _parameters = parameters;
        }
        public override ImmutableArray<AttributeData> GetAttributes() => _attributes.ToImmutableArray();
        internal void AddAttribute(AttributeData a) => _attributes.Add(a);
        public void AddDeclaration(SyntaxReference decl) => _declRefs.Add(decl);
    }
    internal static class TypeSubstituter
    {
        internal static TypeSymbol Substitute(TypeSymbol type, TypeManager types, ImmutableDictionary<TypeParameterSymbol, TypeSymbol> map)
        {
            if (map.IsEmpty)
                return type;

            if (type is TypeParameterSymbol tp && map.TryGetValue(tp, out var repl))
                return repl;

            switch (type)
            {
                case ArrayTypeSymbol at:
                    {
                        var elem = Substitute(at.ElementType, types, map);
                        return ReferenceEquals(elem, at.ElementType) ? at : types.GetArrayType(elem, at.Rank);
                    }
                case PointerTypeSymbol pt:
                    {
                        var p = Substitute(pt.PointedAtType, types, map);
                        return ReferenceEquals(p, pt.PointedAtType) ? pt : types.GetPointerType(p);
                    }
                case TupleTypeSymbol tt:
                    {
                        var elems = tt.ElementTypes;
                        bool changed = false;
                        var b = ImmutableArray.CreateBuilder<TypeSymbol>(elems.Length);
                        for (int i = 0; i < elems.Length; i++)
                        {
                            var e = Substitute(elems[i], types, map);
                            if (!ReferenceEquals(e, elems[i])) changed = true;
                            b.Add(e);
                        }
                        return changed ? types.GetTupleType(b.ToImmutable(), tt.ElementNames) : tt;
                    }
                case ByRefTypeSymbol br:
                    {
                        var e = Substitute(br.ElementType, types, map);
                        return ReferenceEquals(e, br.ElementType) ? br : types.GetByRefType(e);
                    }
                case SubstitutedNamedTypeSymbol snt:
                    {
                        if (snt.TypeArguments.IsDefaultOrEmpty)
                            return snt;

                        bool changed = false;
                        var b = ImmutableArray.CreateBuilder<TypeSymbol>(snt.TypeArguments.Length);
                        for (int i = 0; i < snt.TypeArguments.Length; i++)
                        {
                            var a = Substitute(snt.TypeArguments[i], types, map);
                            if (!ReferenceEquals(a, snt.TypeArguments[i])) changed = true;
                            b.Add(a);
                        }
                        return changed ? types.ConstructNamedType(snt, b.ToImmutable()) : snt;
                    }


                default:
                    return type;
            }
        }
    }
    internal sealed class SubstitutedNamedTypeSymbol : NamedTypeSymbol
    {
        private readonly TypeManager _types;

        private readonly NamedTypeSymbol _originalDefinition;
        private readonly ImmutableArray<TypeSymbol> _typeArguments;

        public override NamedTypeSymbol OriginalDefinition => _originalDefinition;
        public override ImmutableArray<TypeSymbol> TypeArguments => _typeArguments;

        internal NamedTypeSymbol? ContainingTypeOpt { get; }
        internal ImmutableDictionary<TypeParameterSymbol, TypeSymbol> SubstitutionMap { get; }

        private bool _membersInitialized;
        private ImmutableArray<Symbol> _members;

        private bool _baseInitialized;
        private TypeSymbol? _baseType;
        public override Accessibility DeclaredAccessibility => _originalDefinition.DeclaredAccessibility;
        public override bool IsFromMetadata => _originalDefinition.IsFromMetadata;
        public override bool IsRefLikeType => _originalDefinition.IsRefLikeType;
        public override bool IsReadOnlyStruct => _originalDefinition.IsReadOnlyStruct;
        public SubstitutedNamedTypeSymbol(
            TypeManager types,
            NamedTypeSymbol originalDefinition,
            NamedTypeSymbol? containingTypeOpt,
            ImmutableArray<TypeSymbol> typeArguments,
            ImmutableDictionary<TypeParameterSymbol, TypeSymbol> substitutionMap)
        {
            _types = types;
            _originalDefinition = originalDefinition;
            ContainingTypeOpt = containingTypeOpt;
            _typeArguments = typeArguments.IsDefault ? ImmutableArray<TypeSymbol>.Empty : typeArguments;
            SubstitutionMap = substitutionMap;
            _members = default;
        }
        public override string Name
        {
            get
            {
                if (_originalDefinition.Arity == 0 || _typeArguments.Length != _originalDefinition.Arity)
                    return _originalDefinition.Name;

                var sb = new StringBuilder();
                sb.Append(_originalDefinition.Name);
                sb.Append('<');
                for (int i = 0; i < _typeArguments.Length; i++)
                {
                    if (i != 0) sb.Append(", ");
                    sb.Append(_typeArguments[i].Name);
                }
                sb.Append('>');
                return sb.ToString();
            }
        }
        public override TypeSymbol? EnumUnderlyingType
        {
            get
            {
                var ut = _originalDefinition.EnumUnderlyingType;
                return ut is null ? null : TypeSubstituter.Substitute(ut, _types, SubstitutionMap);
            }
        }
        public override Symbol? ContainingSymbol => ContainingTypeOpt ?? _originalDefinition.ContainingSymbol;
        public override ImmutableArray<Location> Locations => _originalDefinition.Locations;
        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences => _originalDefinition.DeclaringSyntaxReferences;

        public override TypeKind TypeKind => _originalDefinition.TypeKind;
        public override SpecialType SpecialType => _originalDefinition.SpecialType;
        public override bool IsReferenceType => _originalDefinition.IsReferenceType;
        public override bool IsValueType => _originalDefinition.IsValueType;

        public override int Arity => _originalDefinition.Arity;
        public override ImmutableArray<TypeParameterSymbol> TypeParameters => _originalDefinition.TypeParameters;

        public override TypeSymbol? BaseType
        {
            get
            {
                if (_baseInitialized) return _baseType;
                _baseInitialized = true;
                var bt = _originalDefinition.BaseType;
                _baseType = bt is null ? null : TypeSubstituter.Substitute(bt, _types, SubstitutionMap);
                return _baseType;
            }
        }

        public override ImmutableArray<Symbol> GetMembers()
        {
            if (_membersInitialized) return _members;
            _membersInitialized = true;

            var orig = _originalDefinition.GetMembers();
            var b = ImmutableArray.CreateBuilder<Symbol>(orig.Length);

            for (int i = 0; i < orig.Length; i++)
            {
                var m = orig[i];
                switch (m)
                {
                    case NamedTypeSymbol nt:
                        b.Add(_types.SubstituteNamedType(nt, this));
                        break;

                    case MethodSymbol ms:
                        b.Add(new SubstitutedMethodSymbol(ms, this, _types, SubstitutionMap));
                        break;

                    case FieldSymbol fs:
                        b.Add(new SubstitutedFieldSymbol(fs, this, _types, SubstitutionMap));
                        break;

                    case PropertySymbol ps:
                        b.Add(new SubstitutedPropertySymbol(ps, this, _types, SubstitutionMap));
                        break;

                    default:
                        b.Add(m);
                        break;
                }
            }

            _members = b.ToImmutable();
            return _members;
        }

        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name, int arity)
        {
            var nested = _originalDefinition.GetTypeMembers(name, arity);
            if (nested.IsDefaultOrEmpty)
                return nested;

            var b = ImmutableArray.CreateBuilder<NamedTypeSymbol>(nested.Length);
            for (int i = 0; i < nested.Length; i++)
                b.Add(_types.SubstituteNamedType(nested[i], this));

            return b.ToImmutable();
        }
    }
    internal sealed class SubstitutedFieldSymbol : FieldSymbol
    {
        private readonly FieldSymbol _original;
        private readonly TypeSymbol _type;

        public override string Name => _original.Name;
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations => _original.Locations;

        public override bool IsStatic => _original.IsStatic;
        public override bool IsConst => _original.IsConst;
        public override Optional<object> ConstantValueOpt => _original.ConstantValueOpt;

        public override TypeSymbol Type => _type;
        public override Accessibility DeclaredAccessibility => _original.DeclaredAccessibility;
        public override bool IsFromMetadata => _original.IsFromMetadata;
        public SubstitutedFieldSymbol(FieldSymbol original, NamedTypeSymbol containing, TypeManager types, ImmutableDictionary<TypeParameterSymbol, TypeSymbol> map)
        {
            _original = original;
            ContainingSymbol = containing;
            _type = TypeSubstituter.Substitute(original.Type, types, map);
        }
    }
    internal sealed class SubstitutedPropertySymbol : PropertySymbol
    {
        private readonly PropertySymbol _original;
        private readonly TypeSymbol _type;
        private readonly MethodSymbol? _getMethod;
        private readonly MethodSymbol? _setMethod;
        private readonly ImmutableArray<ParameterSymbol> _parameters;

        public override string Name => _original.Name;
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations => _original.Locations;
        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences => _original.DeclaringSyntaxReferences;
        public override Accessibility DeclaredAccessibility => _original.DeclaredAccessibility;
        public override bool IsFromMetadata => _original.IsFromMetadata;
        public override TypeSymbol Type => _type;
        public override bool IsStatic => _original.IsStatic;
        public override bool HasGet => _getMethod is not null;
        public override bool HasSet => _setMethod is not null;
        public override MethodSymbol? GetMethod => _getMethod;
        public override MethodSymbol? SetMethod => _setMethod;
        public override ImmutableArray<ParameterSymbol> Parameters => _parameters;
        public override ImmutableArray<AttributeData> GetAttributes() => _original.GetAttributes();
        public SubstitutedPropertySymbol(
            PropertySymbol original,
            NamedTypeSymbol containing,
            TypeManager types,
            ImmutableDictionary<TypeParameterSymbol, TypeSymbol> map)
        {
            _original = original;
            ContainingSymbol = containing;

            _type = TypeSubstituter.Substitute(original.Type, types, map);

            if (original.GetMethod is MethodSymbol gm)
                _getMethod = new SubstitutedMethodSymbol(gm, containing, types, map);

            if (original.SetMethod is MethodSymbol sm)
                _setMethod = new SubstitutedMethodSymbol(sm, containing, types, map);

            var ps = original.Parameters;
            if (ps.IsDefaultOrEmpty)
            {
                _parameters = ImmutableArray<ParameterSymbol>.Empty;
            }
            else
            {
                var b = ImmutableArray.CreateBuilder<ParameterSymbol>(ps.Length);
                for (int i = 0; i < ps.Length; i++)
                {
                    var p = ps[i];
                    var pt = TypeSubstituter.Substitute(p.Type, types, map);
                    b.Add(new ParameterSymbol(
                        p.Name, this, pt, p.Locations,
                        isReadOnlyRef: p.IsReadOnlyRef,
                        refKind: p.RefKind,
                        isScoped: p.IsScoped));
                }
                _parameters = b.ToImmutable();
            }
        }
    }
    internal sealed class SubstitutedMethodSymbol : MethodSymbol
    {
        private readonly MethodSymbol _original;
        private readonly TypeSymbol _returnType;
        private readonly ImmutableArray<ParameterSymbol> _parameters;

        public override string Name => _original.Name;
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations => _original.Locations;
        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences => _original.DeclaringSyntaxReferences;
        public override Accessibility DeclaredAccessibility => _original.DeclaredAccessibility;
        public override bool IsFromMetadata => _original.IsFromMetadata;
        public override bool IsStatic => _original.IsStatic;
        public override bool IsConstructor => _original.IsConstructor;
        public override bool IsAsync => _original.IsAsync;
        public override ImmutableArray<TypeParameterSymbol> TypeParameters => _original.TypeParameters;
        public override TypeSymbol ReturnType => _returnType;
        public override ImmutableArray<ParameterSymbol> Parameters => _parameters;
        public override MethodSymbol OriginalDefinition => _original.OriginalDefinition;
        public SubstitutedMethodSymbol(MethodSymbol original, NamedTypeSymbol containing, TypeManager types, ImmutableDictionary<TypeParameterSymbol, TypeSymbol> map)
        {
            _original = original;
            ContainingSymbol = containing;

            _returnType = TypeSubstituter.Substitute(original.ReturnType, types, map);

            var ps = original.Parameters;
            var b = ImmutableArray.CreateBuilder<ParameterSymbol>(ps.Length);
            for (int i = 0; i < ps.Length; i++)
            {
                var p = ps[i];
                var pt = TypeSubstituter.Substitute(p.Type, types, map);
                b.Add(new ParameterSymbol(
                    p.Name, this, pt, p.Locations,
                    isReadOnlyRef: p.IsReadOnlyRef,
                    refKind: p.RefKind,
                    isScoped: p.IsScoped));
            }
            _parameters = b.ToImmutable();
        }
    }
    internal sealed class ConstructedMethodSymbol : MethodSymbol
    {
        private readonly MethodSymbol _definition;
        private readonly TypeSymbol _returnType;
        private readonly ImmutableArray<ParameterSymbol> _parameters;
        private readonly ImmutableArray<TypeSymbol> _typeArguments;
        public override MethodSymbol OriginalDefinition => _definition.OriginalDefinition;
        public override ImmutableArray<TypeSymbol> TypeArguments => _typeArguments;
        public override string Name => _definition.Name;
        public override Symbol? ContainingSymbol => _definition.ContainingSymbol;
        public override ImmutableArray<Location> Locations => _definition.Locations;
        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences => _definition.DeclaringSyntaxReferences;
        public override Accessibility DeclaredAccessibility => _definition.DeclaredAccessibility;
        public override bool IsFromMetadata => _definition.IsFromMetadata;
        public override bool IsStatic => _definition.IsStatic;
        public override bool IsConstructor => _definition.IsConstructor;
        public override bool IsAsync => _definition.IsAsync;
        public override ImmutableArray<TypeParameterSymbol> TypeParameters => _definition.TypeParameters;

        public override TypeSymbol ReturnType => _returnType;
        public override ImmutableArray<ParameterSymbol> Parameters => _parameters;
        public ConstructedMethodSymbol(MethodSymbol definition, ImmutableArray<TypeSymbol> typeArguments, TypeManager types)
        {
            _definition = definition;
            _typeArguments = typeArguments.IsDefault ? ImmutableArray<TypeSymbol>.Empty : typeArguments;

            var tps = definition.TypeParameters;
            var map = ImmutableDictionary<TypeParameterSymbol, TypeSymbol>.Empty;

            if (!tps.IsDefaultOrEmpty && _typeArguments.Length == tps.Length)
            {
                var mb = map.ToBuilder();
                for (int i = 0; i < tps.Length; i++)
                    mb[tps[i]] = _typeArguments[i];
                map = mb.ToImmutable();
            }

            _returnType = TypeSubstituter.Substitute(definition.ReturnType, types, map);

            var ps = definition.Parameters;
            var b = ImmutableArray.CreateBuilder<ParameterSymbol>(ps.Length);
            for (int i = 0; i < ps.Length; i++)
            {
                var p = ps[i];
                var pt = TypeSubstituter.Substitute(p.Type, types, map);
                b.Add(new ParameterSymbol(p.Name, this, pt, p.Locations, isReadOnlyRef: p.IsReadOnlyRef));
            }
            _parameters = b.ToImmutable();
        }
    }
    internal sealed class SyntheticNamespaceSymbol : NamespaceSymbol
    {
        private readonly Dictionary<string, SyntheticNamespaceSymbol> _namespaces = new(StringComparer.Ordinal);
        private readonly Dictionary<(string name, int arity), List<NamedTypeSymbol>> _types = new();

        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override bool IsGlobalNamespace { get; }
        public override ImmutableArray<Location> Locations => ImmutableArray<Location>.Empty;

        public SyntheticNamespaceSymbol(string name, Symbol? containing, bool isGlobal)
        {
            Name = name;
            ContainingSymbol = containing;
            IsGlobalNamespace = isGlobal;
        }

        public SyntheticNamespaceSymbol GetOrAddNamespace(string name)
        {
            if (!_namespaces.TryGetValue(name, out var ns))
                _namespaces[name] = ns = new SyntheticNamespaceSymbol(name, this, isGlobal: false);
            return ns;
        }

        public void AddType(NamedTypeSymbol type)
        {
            var key = (type.Name, type.Arity);
            if (!_types.TryGetValue(key, out var list))
                _types[key] = list = new List<NamedTypeSymbol>();
            list.Add(type);
        }

        public override ImmutableArray<NamespaceSymbol> GetNamespaceMembers()
            => _namespaces.Values.Cast<NamespaceSymbol>().ToImmutableArray();

        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name, int arity)
        {
            if (arity >= 0)
                return _types.TryGetValue((name, arity), out var list)
                    ? list.ToImmutableArray()
                    : ImmutableArray<NamedTypeSymbol>.Empty;

            return _types
                .Where(kv => StringComparer.Ordinal.Equals(kv.Key.name, name))
                .SelectMany(kv => kv.Value)
                .ToImmutableArray();
        }
        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers()
        {
            if (_types.Count == 0)
                return ImmutableArray<NamedTypeSymbol>.Empty;
            var b = ImmutableArray.CreateBuilder<NamedTypeSymbol>();
            foreach (var kv in _types)
            {
                var list = kv.Value;
                for (int i = 0; i < list.Count; i++)
                    b.Add(list[i]);
            }
            return b.ToImmutable();
        }
    }
    internal sealed class SpecialNamedTypeSymbol : NamedTypeSymbol
    {
        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations => ImmutableArray<Location>.Empty;
        public override TypeKind TypeKind { get; }
        public override SpecialType SpecialType { get; }
        public override bool IsReferenceType { get; }
        public override bool IsValueType { get; }
        public override TypeSymbol? BaseType { get; }
        public override int Arity => 0;
        private readonly List<Symbol> _members = new();
        private readonly Dictionary<(string name, int arity), List<NamedTypeSymbol>> _nestedTypesByName = new();
        public override Accessibility DeclaredAccessibility { get; }
        public override bool IsFromMetadata => true;
        public SpecialNamedTypeSymbol(
            string name,
            Symbol containing,
            TypeKind typeKind,
            SpecialType specialType,
            bool isRef,
            bool isVal,
            TypeSymbol? baseType)
        {
            Name = name;
            ContainingSymbol = containing;
            TypeKind = typeKind;
            SpecialType = specialType;
            IsReferenceType = isRef;
            IsValueType = isVal;
            BaseType = baseType;
        }

        public override ImmutableArray<TypeParameterSymbol> TypeParameters => ImmutableArray<TypeParameterSymbol>.Empty;
        internal void AddMember(Symbol member) => _members.Add(member);
        internal void AddNestedType(NamedTypeSymbol type)
        {
            var key = (type.Name, type.Arity);
            if (!_nestedTypesByName.TryGetValue(key, out var list))
                _nestedTypesByName[key] = list = new List<NamedTypeSymbol>();
            list.Add(type);
            _members.Add(type);
        }
        public override ImmutableArray<Symbol> GetMembers() => _members.ToImmutableArray();
        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name, int arity)
        {
            if (arity >= 0)
                return _nestedTypesByName.TryGetValue((name, arity), out var list)
                    ? list.ToImmutableArray()
                    : ImmutableArray<NamedTypeSymbol>.Empty;
            return _nestedTypesByName
                .Where(kv => StringComparer.Ordinal.Equals(kv.Key.name, name))
                .SelectMany(kv => kv.Value)
                .ToImmutableArray();
        }
    }
    internal sealed class MergedNamespaceSymbol : NamespaceSymbol
    {
        private readonly NamespaceSymbol _a;
        private readonly NamespaceSymbol _b;

        public MergedNamespaceSymbol(NamespaceSymbol a, NamespaceSymbol b)
        {
            _a = a;
            _b = b;
        }

        public override string Name => _a.Name; //assume same
        public override Symbol? ContainingSymbol => _a.ContainingSymbol;
        public override bool IsGlobalNamespace => _a.IsGlobalNamespace || _b.IsGlobalNamespace;
        public override ImmutableArray<Location> Locations => ImmutableArray<Location>.Empty;

        public override ImmutableArray<NamespaceSymbol> GetNamespaceMembers()
        {
            var all = _a.GetNamespaceMembers().Concat(_b.GetNamespaceMembers());

            return all
                .GroupBy(ns => ns.Name, StringComparer.Ordinal)
                .Select(g =>
                {
                    NamespaceSymbol merged = g.First();
                    foreach (var next in g.Skip(1))
                        merged = new MergedNamespaceSymbol(merged, next);
                    return merged;
                })
                .ToImmutableArray();
        }
        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name, int arity)
            => _a.GetTypeMembers(name, arity).AddRange(_b.GetTypeMembers(name, arity));
        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers()
        {
            var a = _a.GetTypeMembers();
            var b = _b.GetTypeMembers();
            if (a.IsDefaultOrEmpty) return b;
            if (b.IsDefaultOrEmpty) return a;
            return a.AddRange(b);
        }
    }
    public sealed class LabelSymbol : Symbol
    {
        public override SymbolKind Kind => SymbolKind.Label;
        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations => _locations.ToImmutableArray();
        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences => _declRefs.ToImmutableArray();

        private readonly List<Location> _locations = new();
        private readonly List<SyntaxReference> _declRefs = new();

        internal bool IsDefined { get; private set; }

        internal LabelSymbol(string name, Symbol containing)
        {
            Name = name;
            ContainingSymbol = containing;
        }

        internal static LabelSymbol CreateGenerated(string debugName, MethodSymbol containing)
        {
            var label = new LabelSymbol(debugName, containing);
            label.IsDefined = true;
            return label;
        }

        internal bool TryDefine(SyntaxTree tree, SyntaxNode declarationNode)
        {
            if (IsDefined)
                return false;

            IsDefined = true;
            _locations.Add(new Location(tree, declarationNode.Span));
            _declRefs.Add(new SyntaxReference(tree, declarationNode));
            return true;
        }
    }
    internal sealed class ExternalMethodSymbol : MethodSymbol
    {
        private readonly bool _isVirtual;
        private readonly bool _isAbstract;
        private readonly bool _isOverride;
        private readonly bool _isSealed;
        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations => ImmutableArray<Location>.Empty;

        public override TypeSymbol ReturnType { get; }
        public override ImmutableArray<ParameterSymbol> Parameters { get; }
        public override ImmutableArray<TypeParameterSymbol> TypeParameters => ImmutableArray<TypeParameterSymbol>.Empty;
        public override Accessibility DeclaredAccessibility { get; }
        public override bool IsFromMetadata => true;
        public override bool IsStatic { get; }
        public override bool IsConstructor { get; }
        public override bool IsAsync => false;
        public override bool IsVirtual => _isVirtual;
        public override bool IsAbstract => _isAbstract;
        public override bool IsOverride => _isOverride;
        public override bool IsSealed => _isSealed;
        public ExternalMethodSymbol(
            string name,
            Symbol containing,
            TypeSymbol returnType,
            bool isStatic,
            bool isConstructor,
            ImmutableArray<(string name, TypeSymbol type)> parameters,
            Accessibility declaredAccessibility,
            bool isVirtual,
            bool isAbstract,
            bool isOverride,
            bool isSealed)
        {
            Name = name;
            ContainingSymbol = containing;
            ReturnType = returnType;
            IsStatic = isStatic;
            IsConstructor = isConstructor;
            DeclaredAccessibility = declaredAccessibility;
            _isVirtual = isVirtual;
            _isAbstract = isAbstract;
            _isOverride = isOverride;
            _isSealed = isSealed;
            var ps = ImmutableArray.CreateBuilder<ParameterSymbol>(parameters.Length);
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                ps.Add(new ParameterSymbol(p.name, this, p.type, ImmutableArray<Location>.Empty));
            }
            Parameters = ps.ToImmutable();
        }
    }
    internal sealed class ExternalFieldSymbol : FieldSymbol
    {
        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations => ImmutableArray<Location>.Empty;
        public override Accessibility DeclaredAccessibility { get; }
        public override bool IsFromMetadata => true;
        public override TypeSymbol Type { get; }
        public override bool IsStatic { get; }
        public override bool IsConst { get; }
        public override Optional<object> ConstantValueOpt { get; }

        public ExternalFieldSymbol(
            string name,
            Symbol containing,
            TypeSymbol type,
            bool isStatic,
            bool isConst,
            Accessibility declaredAccessibility,
            Optional<object> constantValueOpt)
        {
            Name = name;
            ContainingSymbol = containing;
            Type = type;
            IsStatic = isStatic;
            IsConst = isConst;
            DeclaredAccessibility = declaredAccessibility;
            ConstantValueOpt = isConst ? constantValueOpt : Optional<object>.None;
        }
    }
    internal sealed class ExternalPropertySymbol : PropertySymbol
    {
        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations => ImmutableArray<Location>.Empty;
        public override Accessibility DeclaredAccessibility { get; }
        public override bool IsFromMetadata => true;
        public override TypeSymbol Type { get; }
        public override bool IsStatic { get; }

        public override MethodSymbol? GetMethod { get; }
        public override MethodSymbol? SetMethod { get; }

        public override bool HasGet => GetMethod is not null;
        public override bool HasSet => SetMethod is not null;

        private readonly ImmutableArray<ParameterSymbol> _parameters;
        public override ImmutableArray<ParameterSymbol> Parameters => _parameters;

        public ExternalPropertySymbol(
            string name,
            Symbol containing,
            TypeSymbol type,
            bool isStatic,
            Accessibility declaredAccessibility,
            MethodSymbol? getMethod,
            MethodSymbol? setMethod,
            ImmutableArray<ParameterSymbol> parameters)
        {
            Name = name;
            ContainingSymbol = containing;
            Type = type;
            IsStatic = isStatic;
            DeclaredAccessibility = declaredAccessibility;
            GetMethod = getMethod;
            SetMethod = setMethod;
            _parameters = parameters;
        }
    }
    internal sealed class IntrinsicMethodSymbol : MethodSymbol
    {
        public override string Name { get; }
        public override Symbol? ContainingSymbol { get; }
        public override ImmutableArray<Location> Locations => ImmutableArray<Location>.Empty;

        public override TypeSymbol ReturnType { get; }
        public override ImmutableArray<ParameterSymbol> Parameters { get; }
        public override ImmutableArray<TypeParameterSymbol> TypeParameters => ImmutableArray<TypeParameterSymbol>.Empty;

        public override bool IsStatic => true;
        public override bool IsConstructor => false;
        public override bool IsAsync => false;

        public string IntrinsicName { get; }

        public IntrinsicMethodSymbol(
            string name,
            Symbol containing,
            TypeSymbol returnType,
            ImmutableArray<(string name, TypeSymbol type)> parameters,
            string intrinsicName)
        {
            Name = name;
            ContainingSymbol = containing;
            ReturnType = returnType;
            IntrinsicName = intrinsicName;

            var ps = ImmutableArray.CreateBuilder<ParameterSymbol>(parameters.Length);
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                ps.Add(new ParameterSymbol(p.name, this, p.type, ImmutableArray<Location>.Empty));
            }
            Parameters = ps.ToImmutable();
        }
    }
}
