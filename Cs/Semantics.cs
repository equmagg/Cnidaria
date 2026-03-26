using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace Cnidaria.Cs
{
    public enum SpecialType : byte
    {
        None,
        System_Object,
        System_Void,
        System_ValueType,
        System_Enum,
        System_Array,
        System_Boolean,
        System_Char,
        System_Int8,
        System_UInt8,
        System_Int16,
        System_UInt16,
        System_Int32,
        System_UInt32,
        System_Int64,
        System_UInt64,
        System_String,
        System_Single,
        System_Double,
        System_Decimal, 
        System_IntPtr,
        System_UIntPtr,
        System_Exception,
    }
    public enum ConversionKind : byte
    {
        None,
        Identity,
        ImplicitNumeric,
        ImplicitConstant,
        ExplicitNumeric,
        ImplicitReference,
        ExplicitReference,
        Boxing,
        Unboxing,
        UserDefined,
        NullLiteral,
        ImplicitTuple,
        ExplicitTuple,
        ImplicitNullable,
        ExplicitNullable,
        ImplicitStackAlloc,
    }
    public enum TypeKind : byte { Class, Struct, Interface, Enum, Delegate, Error, Dynamic, Unknown }
    public enum DiagnosticSeverity : byte { Hidden, Info, Warning, Error }
    public enum SymbolKind : byte
    {
        Assembly, Module,
        Namespace,
        NamedType, TypeParameter,
        Method, Property, Field, Event,
        Parameter, Local, Label,
        Alias,
        ArrayType,
        PointerType,
        ByRefType,
        Error
    }
    internal enum BoundBinaryOperatorKind : byte
    {
        StringConcatenation,
        Add,
        Subtract,
        Multiply,
        Divide,
        Modulo,

        BitwiseAnd,
        BitwiseOr,
        ExclusiveOr,

        LogicalAnd,
        LogicalOr,

        Equals,
        NotEquals,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,

        LeftShift,
        RightShift,
        UnsignedRightShift,
    }
    internal enum BoundUnaryOperatorKind : byte
    {
        UnaryPlus,
        UnaryMinus,
        LogicalNot,
        BitwiseNot,
    }
    public enum CandidateReason : byte
    {
        None,
        NotAValue,
        NotInvocable,
        OverloadResolutionFailure,
        Inaccessible,
        Ambiguous,

    }
    public readonly struct Conversion
    {
        public readonly ConversionKind Kind;
        public readonly MethodSymbol? UserDefinedMethod;
        public readonly bool UserDefinedIsImplicit;
        public bool Exists => Kind != ConversionKind.None;
        public bool IsImplicit =>
            Kind is ConversionKind.Identity
            or ConversionKind.ImplicitNumeric
            or ConversionKind.ImplicitConstant
            or ConversionKind.ImplicitReference
            or ConversionKind.Boxing
            or ConversionKind.ImplicitTuple
            or ConversionKind.ImplicitNullable
            or ConversionKind.ImplicitStackAlloc
            or ConversionKind.NullLiteral
            || (Kind == ConversionKind.UserDefined && UserDefinedIsImplicit);

        public Conversion(ConversionKind kind) 
        {
            Kind = kind;
            UserDefinedMethod = null;
            UserDefinedIsImplicit = false;
        }
        public Conversion(ConversionKind kind, MethodSymbol userDefinedMethod, bool isImplicit)
        {
            Kind = kind;
            UserDefinedMethod = userDefinedMethod;
            UserDefinedIsImplicit = isImplicit;
        }
    }
    public readonly struct Location
    {
        public readonly SyntaxTree SyntaxTree;
        public readonly TextSpan Span;

        public Location(SyntaxTree syntaxTree, TextSpan span)
        {
            SyntaxTree = syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree));
            Span = span;
        }
        public override string ToString() => $"{Span}";
        public string ToString(string sorce) => Span.ToString(sorce);
    }
    public interface IDiagnostic
    {
        public string GetMessage();
        public string GetMessage(string source);
        public DiagnosticSeverity GetSeverity();
    }
    public readonly struct Diagnostic : IDiagnostic
    {
        public readonly string Id;
        public readonly DiagnosticSeverity Severity;
        public readonly string Message;
        public readonly Location Location;

        public Diagnostic(string id, DiagnosticSeverity severity, string message, Location location)
        {
            Id = id;
            Severity = severity;
            Message = message;
            Location = location;
        }
        public override string ToString() => $"{Id} {Severity}: {Message} {(Location.Span==default(TextSpan) ? "" : $"[{Location}])")}";
        public string GetMessage() => this.ToString();
        public string GetMessage(string souce) 
            => $"{Id} {Severity}: {Message} {(Location.Span == default(TextSpan) ? "" : $"[{Location.ToString(souce)}])")}";
        public DiagnosticSeverity GetSeverity() => this.Severity;
        
    }
    public readonly struct Optional<T>
    {
        public readonly bool HasValue;
        public readonly T Value;

        public Optional(T value)
        {
            HasValue = true;
            Value = value;
        }

        public static Optional<T> None => default;
    }
    public sealed class DiagnosticBag
    {
        private readonly List<Diagnostic> _items = new();
        public void Add(Diagnostic d) => _items.Add(d);
        public ImmutableArray<Diagnostic> ToImmutable() => _items.ToImmutableArray();
        public bool IsEmpty => _items.Count == 0;
    }
    public sealed class SyntaxTree
    {
        public CompilationUnitSyntax Root { get; }
        public string FilePath { get; }

        public SyntaxTree(CompilationUnitSyntax root, string filePath)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            FilePath = filePath ?? "";
        }
    }
    public sealed class TypeManager
    {
        private readonly struct TupleTypeKey : IEquatable<TupleTypeKey>
        {
            public readonly ImmutableArray<TypeSymbol> Types;
            public readonly ImmutableArray<string?> Names;

            public TupleTypeKey(ImmutableArray<TypeSymbol> types, ImmutableArray<string?> names)
            {
                Types = types;
                Names = names;
            }

            public bool Equals(TupleTypeKey other)
            {
                if (Types.Length != other.Types.Length)
                    return false;
                if (Names.Length != other.Names.Length)
                    return false;

                for (int i = 0; i < Types.Length; i++)
                    if (!ReferenceEquals(Types[i], other.Types[i]))
                        return false;

                for (int i = 0; i < Names.Length; i++)
                    if (!StringComparer.Ordinal.Equals(Names[i], other.Names[i]))
                        return false;

                return true;
            }

            public override bool Equals(object? obj)
                => obj is TupleTypeKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = 17;
                    for (int i = 0; i < Types.Length; i++)
                        h = (h * 31) + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Types[i]);
                    for (int i = 0; i < Names.Length; i++)
                        h = (h * 31) + (Names[i] is null ? 0 : StringComparer.Ordinal.GetHashCode(Names[i]!));
                    return h;
                }
            }
        }
        private readonly struct SubstitutedNamedTypeKey : IEquatable<SubstitutedNamedTypeKey>
        {
            public readonly NamedTypeSymbol Definition;
            public readonly NamedTypeSymbol? Containing;
            public readonly ImmutableArray<TypeSymbol> Args;

            public SubstitutedNamedTypeKey(NamedTypeSymbol def, NamedTypeSymbol? containing, ImmutableArray<TypeSymbol> args)
            {
                Definition = def;
                Containing = containing;
                Args = args.IsDefault ? ImmutableArray<TypeSymbol>.Empty : args;
            }
            public bool Equals(SubstitutedNamedTypeKey other)
            {
                if (!ReferenceEquals(Definition, other.Definition))
                    return false;
                if (!ReferenceEquals(Containing, other.Containing))
                    return false;
                if (Args.Length != other.Args.Length)
                    return false;

                for (int i = 0; i < Args.Length; i++)
                    if (!ReferenceEquals(Args[i], other.Args[i]))
                        return false;

                return true;
            }
            public override bool Equals(object? obj) => obj is SubstitutedNamedTypeKey o && Equals(o);
            public override int GetHashCode()
            {
                unchecked
                {
                    int h = 17;
                    h = (h * 31) + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Definition);
                    h = (h * 31) + (Containing is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Containing));
                    for (int i = 0; i < Args.Length; i++)
                        h = (h * 31) + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Args[i]);
                    return h;
                }
            }
        }
        private readonly NamedTypeSymbol[] _specialTypes;
        private readonly Dictionary<(TypeSymbol elem, int rank), ArrayTypeSymbol> _arrayTypes = new();
        private readonly Dictionary<TypeSymbol, PointerTypeSymbol> _pointerTypes = new();
        private readonly Dictionary<TupleTypeKey, TupleTypeSymbol> _tupleTypes = new();
        private readonly Dictionary<TypeSymbol, ByRefTypeSymbol> _byRefTypes = new();
        private readonly Dictionary<SubstitutedNamedTypeKey, SubstitutedNamedTypeSymbol> _namedTypeInstantiations = new();
        private NamedTypeSymbol? _arrayIEnumerable;
        private NamedTypeSymbol? _arrayICollection;
        private NamedTypeSymbol? _arrayIList;
        private NamedTypeSymbol? _arrayGenericIEnumerableDef;
        private NamedTypeSymbol? _arrayGenericICollectionDef;
        private NamedTypeSymbol? _arrayGenericIReadOnlyCollectionDef;
        private NamedTypeSymbol? _arrayGenericIReadOnlyListDef;
        private NamedTypeSymbol? _arrayGenericIListDef;

        private bool _arrayInterfacesBound;
        public NamespaceSymbol CoreGlobalNamespace { get; }

        public TypeManager(ICoreLibraryProvider? provider = null)
        {
            CoreGlobalNamespace = BuildCoreLibrary(out _specialTypes);
            var builder = new CoreLibraryBuilder(this, (SyntheticNamespaceSymbol)CoreGlobalNamespace);
            provider?.Populate(builder);
        }
        internal NamedTypeSymbol SubstituteNamedType(NamedTypeSymbol originalDefinition, NamedTypeSymbol containingType)
            => GetOrCreateNamedType(originalDefinition, containingType, ImmutableArray<TypeSymbol>.Empty);
        internal NamedTypeSymbol ConstructNamedType(
            NamedTypeSymbol definition,
            NamedTypeSymbol? containingTypeOpt,
            ImmutableArray<TypeSymbol> typeArguments)
        {
            if (typeArguments.IsDefault)
                typeArguments = ImmutableArray<TypeSymbol>.Empty;

            if (definition is SubstitutedNamedTypeSymbol snt)
                definition = snt.OriginalDefinition;

            return GetOrCreateNamedType(definition, containingTypeOpt, typeArguments);
        }
        internal NamedTypeSymbol ConstructNamedType(NamedTypeSymbol type, ImmutableArray<TypeSymbol> typeArguments)
        {
            if (typeArguments.IsDefault)
                typeArguments = ImmutableArray<TypeSymbol>.Empty;

            if (type is SubstitutedNamedTypeSymbol snt)
                return GetOrCreateNamedType(snt.OriginalDefinition, snt.ContainingTypeOpt, typeArguments);

            return GetOrCreateNamedType(type, containingTypeOpt: null, typeArguments);
        }
        private NamedTypeSymbol GetOrCreateNamedType(
            NamedTypeSymbol definition, NamedTypeSymbol? containingTypeOpt, ImmutableArray<TypeSymbol> typeArguments)
        {
            if (containingTypeOpt is null && typeArguments.IsDefaultOrEmpty)
                return definition;

            if (!typeArguments.IsDefaultOrEmpty && typeArguments.Length != definition.Arity)
                throw new ArgumentException("Type argument count must match arity.");

            var key = new SubstitutedNamedTypeKey(definition, containingTypeOpt, typeArguments);
            if (_namedTypeInstantiations.TryGetValue(key, out var existing))
                return existing;

            var map = ImmutableDictionary<TypeParameterSymbol, TypeSymbol>.Empty;

            if (containingTypeOpt is SubstitutedNamedTypeSymbol parent)
                map = parent.SubstitutionMap;

            if (!typeArguments.IsDefaultOrEmpty && definition.Arity != 0)
            {
                var b = map.ToBuilder();
                var tps = definition.TypeParameters;
                for (int i = 0; i < tps.Length; i++)
                    b[tps[i]] = typeArguments[i];
                map = b.ToImmutable();
            }

            var created = new SubstitutedNamedTypeSymbol(this, definition, containingTypeOpt, typeArguments, map);
            _namedTypeInstantiations[key] = created;
            return created;
        }
        public NamedTypeSymbol GetSpecialType(SpecialType st)
        {
            var i = (int)st;
            if ((uint)i >= (uint)_specialTypes.Length || _specialTypes[i] is null)
                return new ErrorTypeSymbol(st.ToString(), containing: null, ImmutableArray<Location>.Empty);

            return _specialTypes[i];
        }
        public ByRefTypeSymbol GetByRefType(TypeSymbol elementType)
        {
            if (!_byRefTypes.TryGetValue(elementType, out var br))
                _byRefTypes[elementType] = br = new ByRefTypeSymbol(elementType);
            return br;
        }
        public PointerTypeSymbol GetPointerType(TypeSymbol pointedAtType)
        {
            if (!_pointerTypes.TryGetValue(pointedAtType, out var pt))
                _pointerTypes[pointedAtType] = pt = new PointerTypeSymbol(pointedAtType);
            return pt;
        }
        public TupleTypeSymbol GetTupleType(ImmutableArray<TypeSymbol> elementTypes, ImmutableArray<string?> elementNames)
        {
            if (elementTypes.IsDefault)
                elementTypes = ImmutableArray<TypeSymbol>.Empty;

            if (elementNames.IsDefault || elementNames.Length != elementTypes.Length)
                elementNames = ImmutableArray.CreateRange(elementTypes, _ => (string?)null);

            var key = new TupleTypeKey(elementTypes, elementNames);
            if (!_tupleTypes.TryGetValue(key, out var tt))
            {
                var valueType = GetSpecialType(SpecialType.System_ValueType);
                _tupleTypes[key] = tt = new TupleTypeSymbol(elementTypes, elementNames, valueType);
            }
            return tt;
        }
        public ArrayTypeSymbol GetArrayType(TypeSymbol elementType, int rank)
        {
            if (rank <= 0) rank = 1;

            if (!_arrayTypes.TryGetValue((elementType, rank), out var at))
            {
                var arrayBase = GetSpecialType(SpecialType.System_Array);
                var ifaces = _arrayInterfacesBound
                    ? BuildArrayInterfaces(elementType, rank)
                    : ImmutableArray<TypeSymbol>.Empty;

                _arrayTypes[(elementType, rank)] = at =
                    new ArrayTypeSymbol(elementType, rank, arrayBase, ifaces);
            }

            return at;
        }
        private static NamespaceSymbol BuildCoreLibrary(out NamedTypeSymbol[] specialTypes)
        {
            specialTypes = new NamedTypeSymbol[(int)SpecialType.System_Exception + 1];

            var global = new SyntheticNamespaceSymbol("", containing: null, isGlobal: true);
            var system = global.GetOrAddNamespace("System");

            var objectType = new SpecialNamedTypeSymbol(
                "Object", system, TypeKind.Class,
                SpecialType.System_Object, isRef: true, isVal: false, baseType: null);
            specialTypes[(int)objectType.SpecialType] = objectType;
            system.AddType(objectType);

            var valueType = new SpecialNamedTypeSymbol(
                "ValueType", system, TypeKind.Class,
                SpecialType.System_ValueType, isRef: true, isVal: false, baseType: objectType);
            specialTypes[(int)valueType.SpecialType] = valueType;
            system.AddType(valueType);

            var enumType = new SpecialNamedTypeSymbol(
                "Enum", system, TypeKind.Class,
                SpecialType.System_Enum, isRef: true, isVal: false, baseType: valueType);
            specialTypes[(int)enumType.SpecialType] = enumType;
            system.AddType(enumType);

            var arrayType = new SpecialNamedTypeSymbol(
                "Array", system, TypeKind.Class,
                SpecialType.System_Array, isRef: true, isVal: false, baseType: objectType);
            specialTypes[(int)arrayType.SpecialType] = arrayType;
            system.AddType(arrayType);

            var voidType = new SpecialNamedTypeSymbol(
                "Void", system, TypeKind.Struct,
                SpecialType.System_Void, isRef: false, isVal: false, baseType: null);
            specialTypes[(int)voidType.SpecialType] = voidType;
            system.AddType(voidType);

            var stringType = new SpecialNamedTypeSymbol(
                "String", system, TypeKind.Class,
                SpecialType.System_String, isRef: true, isVal: false, baseType: objectType);
            specialTypes[(int)stringType.SpecialType] = stringType;
            system.AddType(stringType);

            var boolType = new SpecialNamedTypeSymbol(
                "Boolean", system, TypeKind.Struct,
                SpecialType.System_Boolean, isRef: false, isVal: true, baseType: valueType);
            specialTypes[(int)boolType.SpecialType] = boolType;
            system.AddType(boolType);

            var charType = new SpecialNamedTypeSymbol(
                "Char", system, TypeKind.Struct,
                SpecialType.System_Char, isRef: false, isVal: true, baseType: valueType);
            specialTypes[(int)charType.SpecialType] = charType;
            system.AddType(charType);

            var sbyteType = new SpecialNamedTypeSymbol(
                "SByte", system, TypeKind.Struct,
                SpecialType.System_Int8, isRef: false, isVal: true, baseType: valueType);
            specialTypes[(int)sbyteType.SpecialType] = sbyteType;
            system.AddType(sbyteType);

            var shortType = new SpecialNamedTypeSymbol(
                "Int16", system, TypeKind.Struct,
                SpecialType.System_Int16, isRef: false, isVal: true, baseType: valueType);
            specialTypes[(int)shortType.SpecialType] = shortType;
            system.AddType(shortType);

            var intType = new SpecialNamedTypeSymbol(
                "Int32", system, TypeKind.Struct,
                SpecialType.System_Int32, isRef: false, isVal: true, baseType: valueType);
            specialTypes[(int)intType.SpecialType] = intType;
            system.AddType(intType);

            var longType = new SpecialNamedTypeSymbol(
                "Int64", system, TypeKind.Struct,
                SpecialType.System_Int64, isRef: false, isVal: true, baseType: valueType);
            specialTypes[(int)longType.SpecialType] = longType;
            system.AddType(longType);

            var byteType = new SpecialNamedTypeSymbol(
                "Byte", system, TypeKind.Struct,
                SpecialType.System_UInt8, isRef: false, isVal: true, baseType: valueType);
            specialTypes[(int)byteType.SpecialType] = byteType;
            system.AddType(byteType);

            var ushortType = new SpecialNamedTypeSymbol(
                "UInt16", system, TypeKind.Struct,
                SpecialType.System_UInt16, isRef: false, isVal: true, baseType: valueType);
            specialTypes[(int)ushortType.SpecialType] = ushortType;
            system.AddType(ushortType);

            var uintType = new SpecialNamedTypeSymbol(
                "UInt32", system, TypeKind.Struct,
                SpecialType.System_UInt32, isRef: false, isVal: true, baseType: valueType);
            specialTypes[(int)uintType.SpecialType] = uintType;
            system.AddType(uintType);

            var ulongType = new SpecialNamedTypeSymbol(
                "UInt64", system, TypeKind.Struct,
                SpecialType.System_UInt64, isRef: false, isVal: true, baseType: valueType);
            specialTypes[(int)ulongType.SpecialType] = ulongType;
            system.AddType(ulongType);

            var floatType = new SpecialNamedTypeSymbol(
                "Single", system, TypeKind.Struct,
                SpecialType.System_Single, isRef: false, isVal: true, baseType: valueType);
            specialTypes[(int)floatType.SpecialType] = floatType;
            system.AddType(floatType);

            var doubleType = new SpecialNamedTypeSymbol(
                "Double", system, TypeKind.Struct,
                SpecialType.System_Double, isRef: false, isVal: true, baseType: valueType);
            specialTypes[(int)doubleType.SpecialType] = doubleType;
            system.AddType(doubleType);

            var decimalType = new SpecialNamedTypeSymbol(
                "Decimal", system, TypeKind.Struct,
                SpecialType.System_Decimal, isRef: false, isVal: true, baseType: valueType);
            specialTypes[(int)decimalType.SpecialType] = decimalType;
            system.AddType(decimalType);

            var intPtrType = new SpecialNamedTypeSymbol(
                "IntPtr", system, TypeKind.Struct,
                SpecialType.System_IntPtr, isRef: false, isVal: true, baseType: valueType);
            specialTypes[(int)intPtrType.SpecialType] = intPtrType;
            system.AddType(intPtrType);

            var uintPtrType = new SpecialNamedTypeSymbol(
                "UIntPtr", system, TypeKind.Struct,
                SpecialType.System_UIntPtr, isRef: false, isVal: true, baseType: valueType);
            specialTypes[(int)uintPtrType.SpecialType] = uintPtrType;
            system.AddType(uintPtrType);

            var exceptionType = new SpecialNamedTypeSymbol(
                "Exception", system, TypeKind.Class,
                SpecialType.System_Exception, isRef: true, isVal: false, baseType: objectType);
            specialTypes[(int)exceptionType.SpecialType] = exceptionType;
            system.AddType(exceptionType);

            return global;
        }
        internal void BindWellKnownArrayInterfaces(NamespaceSymbol globalNamespace)
        {
            _arrayIEnumerable = FindType(globalNamespace, "System.Collections", "IEnumerable", 0);
            _arrayICollection = FindType(globalNamespace, "System.Collections", "ICollection", 0);
            _arrayIList = FindType(globalNamespace, "System.Collections", "IList", 0);

            _arrayGenericIEnumerableDef = FindType(globalNamespace, "System.Collections.Generic", "IEnumerable", 1);
            _arrayGenericICollectionDef = FindType(globalNamespace, "System.Collections.Generic", "ICollection", 1);
            _arrayGenericIReadOnlyCollectionDef = FindType(globalNamespace, "System.Collections.Generic", "IReadOnlyCollection", 1);
            _arrayGenericIReadOnlyListDef = FindType(globalNamespace, "System.Collections.Generic", "IReadOnlyList", 1);
            _arrayGenericIListDef = FindType(globalNamespace, "System.Collections.Generic", "IList", 1);

            _arrayInterfacesBound = true;

            foreach (var arr in _arrayTypes.Values)
                arr.SetInterfaces(BuildArrayInterfaces(arr.ElementType, arr.Rank));
        }
        private ImmutableArray<TypeSymbol> BuildArrayInterfaces(TypeSymbol elementType, int rank)
        {
            var b = ImmutableArray.CreateBuilder<TypeSymbol>(8);

            AddIfPresent(_arrayIEnumerable);
            AddIfPresent(_arrayICollection);
            AddIfPresent(_arrayIList);

            if (rank == 1)
            {
                AddConstructedIfPresent(_arrayGenericIEnumerableDef, elementType);
                AddConstructedIfPresent(_arrayGenericICollectionDef, elementType);
                AddConstructedIfPresent(_arrayGenericIReadOnlyCollectionDef, elementType);
                AddConstructedIfPresent(_arrayGenericIReadOnlyListDef, elementType);
                AddConstructedIfPresent(_arrayGenericIListDef, elementType);
            }

            return b.Count == 0 ? ImmutableArray<TypeSymbol>.Empty : b.ToImmutable();

            void AddIfPresent(NamedTypeSymbol? t)
            {
                if (t is null)
                    return;

                for (int i = 0; i < b.Count; i++)
                    if (ReferenceEquals(b[i], t))
                        return;

                b.Add(t);
            }

            void AddConstructedIfPresent(NamedTypeSymbol? def, TypeSymbol arg)
            {
                if (def is null)
                    return;

                var constructed = ConstructNamedType(def, ImmutableArray.Create(arg));

                for (int i = 0; i < b.Count; i++)
                    if (ReferenceEquals(b[i], constructed))
                        return;

                b.Add(constructed);
            }
        }
        private static NamedTypeSymbol? FindType(
    NamespaceSymbol root,
    string fullNamespace,
    string name,
    int arity)
        {
            var ns = FindNamespace(root, fullNamespace);
            if (ns is null)
                return null;

            var types = ns.GetTypeMembers(name, arity);
            if (types.IsDefaultOrEmpty)
                return null;

            for (int i = 0; i < types.Length; i++)
            {
                if (types[i].TypeKind == TypeKind.Interface)
                    return types[i];
            }

            return null;
        }

        private static NamespaceSymbol? FindNamespace(NamespaceSymbol root, string fullNamespace)
        {
            if (string.IsNullOrEmpty(fullNamespace))
                return root;

            NamespaceSymbol current = root;
            var parts = fullNamespace.Split('.', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < parts.Length; i++)
            {
                var members = current.GetNamespaceMembers();
                NamespaceSymbol? next = null;

                for (int j = 0; j < members.Length; j++)
                {
                    if (StringComparer.Ordinal.Equals(members[j].Name, parts[i]))
                    {
                        next = members[j];
                        break;
                    }
                }

                if (next is null)
                    return null;

                current = next;
            }

            return current;
        }
    }
    public static class CompilationFactory
    {
        public static Compilation Create(ImmutableArray<SyntaxTree> trees, out ImmutableArray<Diagnostic> diagnostics)
            => Create(trees, coreLib: null, out diagnostics);
        public static Compilation Create(
            ImmutableArray<SyntaxTree> trees,
            ICoreLibraryProvider? coreLib,
            out ImmutableArray<Diagnostic> diagnostics)
        {
            var bag = new DiagnosticBag();
            var types = new TypeManager(coreLib);

            var decl = new DeclarationBuilder(bag, types, isCoreLibrary: false);
            var declResult = decl.Build(trees);

            var compilation = new Compilation(
                syntaxTrees: trees,
                sourceGlobalNamespace: declResult.GlobalNamespace,
                entryPoint: decl.SynthesizedEntryPoint,
                declaredSymbolsByTree: declResult.DeclaredSymbolsByTree,
                types: types);

            GenericConstraintBinder.BindAll(compilation, trees, bag);
            MemberSignatureBinder.BindAll(compilation, trees, bag);
            BaseTypeBinder.BindAll(compilation, trees, bag);
            ExplicitInterfaceImplementationBinder.BindAll(compilation, trees, bag);
            AttributeBinder.BindAll(compilation, trees, bag);

            diagnostics = bag.ToImmutable();
            return compilation;
        }
        public static Compilation CreateCoreLibrary(ImmutableArray<SyntaxTree> trees, out ImmutableArray<Diagnostic> diagnostics)
        {
            var bag = new DiagnosticBag();
            var types = new TypeManager(provider: null);
            var decl = new DeclarationBuilder(bag, types, isCoreLibrary: true);
            var declResult = decl.Build(trees);

            var compilation = new Compilation(
                trees,
                declResult.GlobalNamespace,
                entryPoint: decl.SynthesizedEntryPoint,
                declResult.DeclaredSymbolsByTree,
                types);

            GenericConstraintBinder.BindAll(compilation, trees, bag);
            MemberSignatureBinder.BindAll(compilation, trees, bag);
            BaseTypeBinder.BindAll(compilation, trees, bag);
            AttributeBinder.BindAll(compilation, trees, bag);

            diagnostics = bag.ToImmutable();
            return compilation;
        }
    }
    public sealed class Compilation
    {
        private readonly ImmutableArray<SyntaxTree> _syntaxTrees;
        private readonly TypeManager _types;
        public MethodSymbol? EntryPoint { get; }
        public NamespaceSymbol SourceGlobalNamespace { get; }
        public NamespaceSymbol GlobalNamespace { get; }
        public ImmutableArray<SyntaxTree> SyntaxTrees => _syntaxTrees;
        internal TypeManager TypeManager => _types;
        internal ImmutableDictionary<SyntaxTree, ImmutableDictionary<SyntaxNode, Symbol>> DeclaredSymbolsByTree { get; }
            = ImmutableDictionary<SyntaxTree, ImmutableDictionary<SyntaxNode, Symbol>>.Empty;
        private readonly Dictionary<(SyntaxTree Tree, bool IgnoreAccessibility), SemanticModel> _semanticModelCache = new();
        public Compilation(
            ImmutableArray<SyntaxTree> syntaxTrees,
            NamespaceSymbol sourceGlobalNamespace,
            MethodSymbol? entryPoint,
            ImmutableDictionary<SyntaxTree, ImmutableDictionary<SyntaxNode, Symbol>> declaredSymbolsByTree,
            TypeManager types)
        {
            _syntaxTrees = syntaxTrees;
            _types = types;
            SourceGlobalNamespace = sourceGlobalNamespace;
            EntryPoint = entryPoint;
            DeclaredSymbolsByTree = declaredSymbolsByTree;

            GlobalNamespace = new MergedNamespaceSymbol((NamespaceSymbol)SourceGlobalNamespace, types.CoreGlobalNamespace);
            _types.BindWellKnownArrayInterfaces(GlobalNamespace);
        }
        internal NamedTypeSymbol ConstructNamedType(NamedTypeSymbol type, ImmutableArray<TypeSymbol> typeArguments)
            => _types.ConstructNamedType(type, typeArguments);
        public NamedTypeSymbol GetSpecialType(SpecialType st) => _types.GetSpecialType(st);
        public PointerTypeSymbol CreatePointerType(TypeSymbol pointedAtType)
            => _types.GetPointerType(pointedAtType);
        public ArrayTypeSymbol CreateArrayType(TypeSymbol elementType, int rank)
            => _types.GetArrayType(elementType, rank);
        public TupleTypeSymbol CreateTupleType(ImmutableArray<TypeSymbol> elementTypes, ImmutableArray<string?> elementNames)
            => _types.GetTupleType(elementTypes, elementNames);
        public ByRefTypeSymbol CreateByRefType(TypeSymbol elementType)
            => TypeManager.GetByRefType(elementType);
        public SemanticModel GetSemanticModel(SyntaxTree tree, bool ignoreAccessibility = false)
        {
            var key = (tree, ignoreAccessibility);

            if (_semanticModelCache.TryGetValue(key, out var model))
                return model;

            model = new SourceSemanticModel(this, tree, ignoreAccessibility);
            _semanticModelCache.Add(key, model);
            return model;
        }
        public IEnumerable<SyntaxNode> EnumerateMethodBodyOwners(SyntaxTree tree)
        {
            Compilation c = this;
            if (!c.DeclaredSymbolsByTree.TryGetValue(tree, out var declMap))
                yield break;
            foreach (var kv in declMap)
            {
                switch (kv.Key)
                {
                    case MethodDeclarationSyntax md when md.Body != null || md.ExpressionBody != null:
                        yield return md;
                        break;

                    case ConstructorDeclarationSyntax cd when cd.Body != null || cd.ExpressionBody != null:
                        yield return cd;
                        break;

                    case PropertyDeclarationSyntax pd:
                        {
                            if (pd.ExpressionBody != null)
                            {
                                yield return pd;
                                break;
                            }

                            var al = pd.AccessorList;
                            if (al == null) break;

                            foreach (var acc in al.Accessors)
                            {
                                if (acc.Body != null || acc.ExpressionBody != null)
                                {
                                    yield return acc;
                                    continue;
                                }
                                if (acc.Kind is SyntaxKind.GetAccessorDeclaration or SyntaxKind.SetAccessorDeclaration)
                                    yield return acc;
                            }

                        }
                        break;
                    case IndexerDeclarationSyntax ids:
                        {
                            if (ids.ExpressionBody != null)
                            {
                                yield return ids;
                                break;
                            }

                            var ial = ids.AccessorList;
                            if (ial == null) break;
                            foreach (var acc in ial.Accessors)
                            {
                                if (acc.Body != null || acc.ExpressionBody != null)
                                    yield return acc;
                            }
                        }
                        break;
                    case OperatorDeclarationSyntax od when od.Body != null || od.ExpressionBody != null:
                        yield return od;
                        break;

                    case ConversionOperatorDeclarationSyntax cod when cod.Body != null || cod.ExpressionBody != null:
                        yield return cod;
                        break;
                }
            }
        }

        public (MetadataImage md, Dictionary<int, BytecodeFunction> funcs, ImmutableArray<Diagnostic> diags, Exception? exception) BuildModule(
            string moduleName,
            SyntaxTree tree,
            bool includeCoreTypesInTypeDefs,
            string defaultExternalAssemblyName,
            Func<NamedTypeSymbol, string?>? externalAssemblyResolver = null,
            bool allowInlining = true,
            bool print = false)
        {
            Compilation compilation = this;
            var model = compilation.GetSemanticModel(tree);
            var diagnostics = model.GetDiagnostics();
            if (print && diagnostics.Length > 0)
            {
                foreach (var diagnostic in diagnostics)
                    Console.WriteLine(diagnostic);
            }

            var rootNs = includeCoreTypesInTypeDefs
                ? compilation.GlobalNamespace
                : compilation.SourceGlobalNamespace;
            var systemObject = compilation.GetSpecialType(SpecialType.System_Object);
            var tokens = new MetadataTokenProvider(
                moduleName,
                rootNs,
                systemObject,
                defaultExternalAssemblyName,
                externalAssemblyResolver);

            var functions = new Dictionary<int, BytecodeFunction>();

            try
            {
                if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                    return (tokens.Image, functions, diagnostics, null);
                void AddFn(BytecodeFunction fn)
                {
                    if (!functions.TryAdd(fn.MethodToken, fn))
                        throw new InvalidOperationException($"Duplicate bytecode for token 0x{fn.MethodToken:X8}");
                }
                var sharedInlineBodyCache =
                    new Dictionary<MethodSymbol, BoundMethodBody>(ReferenceEqualityComparer<MethodSymbol>.Instance);
                void EmitBody(BoundMethodBody body)
                {
                    if (print)
                    {
                        string printedFull = Cnidaria.Cs.BoundTreePrinter.Print(body);
                        Console.WriteLine(printedFull);
                    }

                    var lowered = IRLowering.Rewrite(compilation, body, allowInlining, sharedInlineBodyCache);
                    var emit = BytecodeEmitter.Emit(lowered, tokens);

                    AddFn(emit.Entry);
                    foreach (var lf in emit.AdditionalMethods)
                        AddFn(lf);
                }
                if (model.GetBoundNode(tree.Root) is BoundCompilationUnit cu &&
                    cu.TopLevelMethodBodyOpt is BoundMethodBody topLevelBody)
                {
                    EmitBody(topLevelBody);
                }
                foreach (var owner in compilation.EnumerateMethodBodyOwners(tree))
                {
                    var body = (BoundMethodBody)model.GetBoundNode(owner);
                    EmitBody(body);
                }
                foreach (var ctor in EnumerateSynthesizedInstanceCtorsInTree(compilation, tree))
                {
                    int ctorTok = tokens.GetMethodToken(ctor);
                    if (functions.ContainsKey(ctorTok))
                        continue;
                    var ret = new BoundReturnStatement(tree.Root, expression: null);
                    var block = new BoundBlockStatement(tree.Root, ImmutableArray.Create<BoundStatement>(ret));
                    var body = new BoundMethodBody(tree.Root, ctor, block);
                    EmitBody(body);
                }
                foreach (var cctor in EnumerateSynthesizedStaticCctorsInTree(compilation, tree))
                {
                    int cctorTok = tokens.GetMethodToken(cctor);
                    if (functions.ContainsKey(cctorTok))
                        continue;

                    var body = BuildSynthesizedTypeInitializerBody(compilation, tree, model, cctor);
                    EmitBody(body);
                }
                return (tokens.Image, functions, diagnostics, null);
            }
            catch (Exception ex)
            {
                if (print)
                    Console.WriteLine(ex.Message);
                return (tokens.Image, functions, diagnostics, ex);
            }

        }
        private static BoundMethodBody BuildSynthesizedTypeInitializerBody(
            Compilation compilation,
            SyntaxTree tree,
            SemanticModel model,
            MethodSymbol cctor)
        {
            var bag = new DiagnosticBag();

            IBindingRecorder recorder =
                model is IBindingRecorder r ? r : NullRecorder.Instance;

            var importScopeMap = ImportsBuilder.BuildImportScopeMap(compilation, tree, recorder, bag);
            var typeBinder = new TypeBinder(parent: null, flags: BinderFlags.None, compilation: compilation, importScopeMap: importScopeMap);
            var exprBinder = new LocalScopeBinder(parent: typeBinder, flags: BinderFlags.InMethod, containing: cctor);

            var ctx = new BindingContext(compilation, model, cctor, recorder);

            var stmts = ImmutableArray.CreateBuilder<BoundStatement>();

            var ownerType = (NamedTypeSymbol)cctor.ContainingSymbol!;
            var members = ownerType.GetMembers();

            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is not SourceFieldSymbol fs)
                    continue;
                if (!fs.IsStatic || fs.IsConst)
                    continue;

                var declRefs = fs.DeclaringSyntaxReferences;
                if (declRefs.IsDefaultOrEmpty)
                    continue;

                if (declRefs[0].Node is not VariableDeclaratorSyntax vd)
                    continue;
                if (vd.Initializer is null)
                    continue;

                var rhsSyntax = vd.Initializer.Value;
                var rhsBound = exprBinder.BindExpressionWithTargetType(
                    exprSyntax: rhsSyntax,
                    targetType: fs.Type,
                    diagnosticNode: vd.Initializer,
                    context: ctx,
                    diagnostics: bag,
                    requireImplicit: true);

                var lhs = new BoundMemberAccessExpression(
                    syntax: rhsSyntax,
                    receiverOpt: null,
                    member: fs,
                    type: fs.Type,
                    isLValue: true);

                var ass = new BoundAssignmentExpression(vd, lhs, rhsBound);
                stmts.Add(new BoundExpressionStatement(vd, ass));
            }

            stmts.Add(new BoundReturnStatement(tree.Root, expression: null));
            var block = new BoundBlockStatement(tree.Root, stmts.ToImmutable());
            return new BoundMethodBody(tree.Root, cctor, block);
        }
        private static IEnumerable<SynthesizedConstructorSymbol> EnumerateSynthesizedInstanceCtorsInTree(
            Compilation compilation, SyntaxTree tree)
        {
            static bool IsDeclaredInTree(SourceNamedTypeSymbol t, SyntaxTree tree)
            {
                var refs = t.DeclaringSyntaxReferences;
                if (refs.IsDefaultOrEmpty)
                    return false;
                for (int i = 0; i < refs.Length; i++)
                {
                    if (ReferenceEquals(refs[i].SyntaxTree, tree))
                        return true;
                }
                return false;
            }
            static void AddTypeAndNested(
                NamedTypeSymbol t,
                SyntaxTree tree,
                List<SourceNamedTypeSymbol> dst)
            {
                if (t is SourceNamedTypeSymbol st && IsDeclaredInTree(st, tree))
                    dst.Add(st);

                var members = t.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is NamedTypeSymbol nested)
                        AddTypeAndNested(nested, tree, dst);
                }
            }
            static void VisitNs(
                NamespaceSymbol ns,
                SyntaxTree tree,
                List<SourceNamedTypeSymbol> dst)
            {
                var types = ns.GetTypeMembers();
                for (int i = 0; i < types.Length; i++)
                    AddTypeAndNested(types[i], tree, dst);
                var nss = ns.GetNamespaceMembers();
                for (int i = 0; i < nss.Length; i++)
                    VisitNs(nss[i], tree, dst);
            }
            var list = new List<SourceNamedTypeSymbol>();
            VisitNs(compilation.SourceGlobalNamespace, tree, list);
            for (int ti = 0; ti < list.Count; ti++)
            {
                var members = list[ti].GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is SynthesizedConstructorSymbol ctor && !ctor.IsStatic)
                        yield return ctor;
                }
            }
        }
        private static IEnumerable<MethodSymbol> EnumerateSynthesizedStaticCctorsInTree(Compilation compilation, SyntaxTree tree)
        {
            static bool IsDeclaredInTree(SourceNamedTypeSymbol t, SyntaxTree tree)
            {
                var refs = t.DeclaringSyntaxReferences;
                if (refs.IsDefaultOrEmpty) return false;
                for (int i = 0; i < refs.Length; i++)
                    if (ReferenceEquals(refs[i].SyntaxTree, tree))
                        return true;
                return false;
            }

            static void AddTypeAndNested(NamedTypeSymbol t, SyntaxTree tree, List<SourceNamedTypeSymbol> dst)
            {
                if (t is SourceNamedTypeSymbol st && IsDeclaredInTree(st, tree))
                    dst.Add(st);

                var mem = t.GetMembers();
                for (int i = 0; i < mem.Length; i++)
                    if (mem[i] is NamedTypeSymbol nested)
                        AddTypeAndNested(nested, tree, dst);
            }

            static void VisitNs(NamespaceSymbol ns, SyntaxTree tree, List<SourceNamedTypeSymbol> dst)
            {
                var types = ns.GetTypeMembers();
                for (int i = 0; i < types.Length; i++)
                    AddTypeAndNested(types[i], tree, dst);

                var nss = ns.GetNamespaceMembers();
                for (int i = 0; i < nss.Length; i++)
                    VisitNs(nss[i], tree, dst);
            }

            var list = new List<SourceNamedTypeSymbol>();
            VisitNs(compilation.SourceGlobalNamespace, tree, list);

            for (int ti = 0; ti < list.Count; ti++)
            {
                var members = list[ti].GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is MethodSymbol ms &&
                        ms.IsStatic &&
                        ms.Parameters.Length == 0 &&
                        StringComparer.Ordinal.Equals(ms.Name, ".cctor") &&
                        (ms.DeclaringSyntaxReferences.IsDefaultOrEmpty)) 
                    {
                        yield return ms;
                    }
                }
            }
        }
    }
    internal sealed class Imports
    {
        public static readonly Imports Empty = new Imports(
         containers: ImmutableArray<Symbol>.Empty,
         aliases: ImmutableDictionary<string, AliasSymbol>.Empty.WithComparers(StringComparer.Ordinal),
         staticTypes: ImmutableArray<NamedTypeSymbol>.Empty);
        public ImmutableArray<Symbol> Containers { get; } // NamespaceSymbol or NamedTypeSymbol
        public ImmutableDictionary<string, AliasSymbol> Aliases { get; }
        public ImmutableArray<NamedTypeSymbol> StaticTypes { get; }

        public Imports(
            ImmutableArray<Symbol> containers,
            ImmutableDictionary<string, AliasSymbol> aliases,
            ImmutableArray<NamedTypeSymbol> staticTypes)
        {
            Containers = containers.IsDefault ? ImmutableArray<Symbol>.Empty : containers;
            Aliases = aliases ?? ImmutableDictionary<string, AliasSymbol>.Empty.WithComparers(StringComparer.Ordinal);
            StaticTypes = staticTypes.IsDefault ? ImmutableArray<NamedTypeSymbol>.Empty : staticTypes;
        }

        public bool TryGetAlias(string name, out AliasSymbol? alias)
            => Aliases.TryGetValue(name, out alias);
    }
    internal sealed class ImportScopeMap
    {
        private readonly ImmutableArray<(TextSpan Span, Imports Imports)> _scopes;

        public Imports RootImports { get; }

        public ImportScopeMap(
            Imports rootImports,
            ImmutableArray<(TextSpan Span, Imports Imports)> scopes)
        {
            RootImports = rootImports ?? throw new ArgumentNullException(nameof(rootImports));
            _scopes = scopes.IsDefault ? ImmutableArray<(TextSpan Span, Imports Imports)>.Empty : scopes;
        }

        public Imports GetImportsForPosition(int position)
        {
            // Select the smallest scope span containing the position
            Imports best = RootImports;
            int bestLen = int.MaxValue;

            for (int i = 0; i < _scopes.Length; i++)
            {
                var (span, imports) = _scopes[i];

                if (position < span.Start || position >= span.End)
                    continue;

                int len = span.Length;
                if (len < bestLen)
                {
                    bestLen = len;
                    best = imports;
                }
            }

            return best;
        }

        public Imports GetImportsForSymbol(Symbol containingSymbol)
        {
            if (containingSymbol is null)
                return RootImports;

            var refs = containingSymbol.DeclaringSyntaxReferences;
            if (refs.IsDefaultOrEmpty)
                return RootImports;

            var node = refs[0].Node;
            return GetImportsForPosition(node.Span.Start);
        }

        public Imports GetImportsForNode(SyntaxNode node)
        {
            if (node is null)
                return RootImports;

            return GetImportsForPosition(node.Span.Start);
        }
    }
    public readonly struct SyntaxReference
    {
        public readonly SyntaxTree SyntaxTree;
        public readonly SyntaxNode Node;

        public SyntaxReference(SyntaxTree syntaxTree, SyntaxNode node)
        {
            SyntaxTree = syntaxTree;
            Node = node;
        }
    }
    public readonly struct SymbolInfo
    {
        public readonly Symbol? Symbol; // chosen best symbol
        public readonly ImmutableArray<Symbol> CandidateSymbols;
        public readonly CandidateReason CandidateReason;

        public SymbolInfo(Symbol? symbol, ImmutableArray<Symbol> candidates, CandidateReason reason)
        {
            Symbol = symbol;
            CandidateSymbols = candidates;
            CandidateReason = reason;
        }

        public bool IsEmpty => Symbol is null && CandidateSymbols.IsDefaultOrEmpty;
        public static SymbolInfo None => new(null, ImmutableArray<Symbol>.Empty, CandidateReason.None);
    }
    public readonly struct TypeInfo
    {
        public readonly TypeSymbol? Type;
        public readonly TypeSymbol? ConvertedType;
        public TypeInfo(TypeSymbol? type, TypeSymbol? convertedType)
        {
            Type = type;
            ConvertedType = convertedType;
        }
    }
    



    public abstract class SemanticModel
    {
        public Compilation Compilation { get; }
        public SyntaxTree SyntaxTree { get; }
        public bool IgnoresAccessibility { get; }

        protected SemanticModel(Compilation compilation, SyntaxTree tree, bool ignoresAccessibility)
        {
            Compilation = compilation;
            SyntaxTree = tree;
            IgnoresAccessibility = ignoresAccessibility;
        }

        public abstract ImmutableArray<Diagnostic> GetDiagnostics(TextSpan? span = null, CancellationToken cancellationToken = default);

        public abstract Symbol? GetDeclaredSymbol(SyntaxNode declaration, CancellationToken cancellationToken = default);

        public abstract SymbolInfo GetSymbolInfo(SyntaxNode node, CancellationToken cancellationToken = default);

        public abstract Symbol? GetAliasInfo(NameSyntax nameSyntax, CancellationToken cancellationToken = default);

        public abstract TypeInfo GetTypeInfo(ExpressionSyntax expr, CancellationToken cancellationToken = default);

        public abstract Optional<object> GetConstantValue(ExpressionSyntax expr, CancellationToken cancellationToken = default);

        public abstract Conversion GetConversion(ExpressionSyntax expr, CancellationToken cancellationToken = default);

        public abstract Symbol? GetEnclosingSymbol(int position, CancellationToken cancellationToken = default);

        public abstract ImmutableArray<Symbol> LookupSymbols(int position, string? name = null, CancellationToken cancellationToken = default);

        internal abstract BoundNode GetBoundNode(SyntaxNode node, CancellationToken cancellationToken = default);
    }
    public interface IBindingRecorder
    {
        void RecordBound(SyntaxNode syntax, BoundNode bound);
        void RecordDeclared(SyntaxNode syntax, Symbol symbol);
    }
    internal sealed class SourceSemanticModel : SemanticModel, IBindingRecorder
    {
        private readonly Dictionary<SyntaxNode, BoundNode> _boundNodeCache = new();
        private readonly Dictionary<SyntaxNode, Symbol> _declaredSymbolCache = new();
        private ImmutableArray<Diagnostic>? _diagnosticsCache;
        void IBindingRecorder.RecordBound(SyntaxNode syntax, BoundNode bound)
        => _boundNodeCache[syntax] = bound;

        void IBindingRecorder.RecordDeclared(SyntaxNode syntax, Symbol symbol)
            => _declaredSymbolCache[syntax] = symbol;
        public SourceSemanticModel(Compilation compilation, SyntaxTree tree, bool ignoresAccessibility)
            : base(compilation, tree, ignoresAccessibility)
        {
        }
        public override ImmutableArray<Diagnostic> GetDiagnostics(TextSpan? span = null, CancellationToken cancellationToken = default)
        {
            EnsureBoundTreeFor(SyntaxTree.Root, cancellationToken);
            return _diagnosticsCache ?? ImmutableArray<Diagnostic>.Empty;
        }
        public override Symbol? GetDeclaredSymbol(SyntaxNode declaration, CancellationToken cancellationToken = default)
        {
            if (_declaredSymbolCache.TryGetValue(declaration, out var s))
                return s;

            if (Compilation.DeclaredSymbolsByTree.TryGetValue(SyntaxTree, out var map) &&
                map.TryGetValue(declaration, out var declSym))
            {
                return declSym;
            }

            return null;
        }
        public override SymbolInfo GetSymbolInfo(SyntaxNode node, CancellationToken cancellationToken = default)
        {
            if (node is ExpressionSyntax es)
            {
                var b = GetBoundNode(es, cancellationToken);

                while (b is BoundConversionExpression conv)
                    b = conv.Operand;

                switch (b)
                {
                    case BoundLocalExpression le:
                        return new SymbolInfo(le.Local, ImmutableArray<Symbol>.Empty, CandidateReason.None);

                    case BoundParameterExpression pe:
                        return new SymbolInfo(pe.Parameter, ImmutableArray<Symbol>.Empty, CandidateReason.None);

                    case BoundCallExpression call:
                        return new SymbolInfo(call.Method, ImmutableArray<Symbol>.Empty, CandidateReason.None);

                    case BoundMemberAccessExpression ma:
                        return new SymbolInfo(ma.Member, ImmutableArray<Symbol>.Empty, CandidateReason.None);

                    case BoundLabelExpression l:
                        return new SymbolInfo(l.Label, ImmutableArray<Symbol>.Empty, CandidateReason.None);

                    case BoundObjectCreationExpression oc when oc.ConstructorOpt is not null:
                        return new SymbolInfo(oc.ConstructorOpt, ImmutableArray.Create<Symbol>(oc.ConstructorOpt), CandidateReason.None);
                }

            }

            return SymbolInfo.None;
        }

        public override Symbol? GetAliasInfo(NameSyntax nameSyntax, CancellationToken cancellationToken = default)
        {
            return null;
        }

        public override TypeInfo GetTypeInfo(ExpressionSyntax expr, CancellationToken cancellationToken = default)
        {
            var b = GetBoundNode(expr, cancellationToken);
            if (b is not BoundExpression be)
                return new TypeInfo(type: null, convertedType: null);

            if (be is BoundConversionExpression conv)
                return new TypeInfo(type: conv.Operand.Type, convertedType: conv.Type);

            if (be is BoundFixedInitializerExpression fixedInit)
                return new TypeInfo(type: fixedInit.Expression.Type, convertedType: fixedInit.Type);

            return new TypeInfo(type: be.Type, convertedType: be.Type);
        }

        public override Optional<object> GetConstantValue(ExpressionSyntax expr, CancellationToken cancellationToken = default)
        {
            var b = GetBoundNode(expr, cancellationToken);
            if (b is BoundConversionExpression conv)
            {
                if (expr is CastExpressionSyntax)
                    return conv.ConstantValueOpt;

                return conv.Operand.ConstantValueOpt;
            }

            return b is BoundExpression be ? be.ConstantValueOpt : Optional<object>.None;
        }

        public override Conversion GetConversion(ExpressionSyntax expr, CancellationToken cancellationToken = default)
        {
            var b = GetBoundNode(expr, cancellationToken);
            if (b is BoundConversionExpression conv)
                return conv.Conversion;

            if (b is BoundFixedInitializerExpression fixedInit)
                return fixedInit.ElementPointerConversion;

            if (b is BoundExpression be && be.Type is not null)
                return new Conversion(ConversionKind.Identity);

            return new Conversion(ConversionKind.None);
        }

        public override Symbol? GetEnclosingSymbol(int position, CancellationToken cancellationToken = default)
        {
            return null;
        }

        public override ImmutableArray<Symbol> LookupSymbols(int position, string? name = null, CancellationToken cancellationToken = default)
        {
            return ImmutableArray<Symbol>.Empty;
        }

        internal override BoundNode GetBoundNode(SyntaxNode node, CancellationToken cancellationToken = default)
        {
            if (_boundNodeCache.TryGetValue(node, out var b))
                return b;

            EnsureBoundTreeFor(node, cancellationToken);

            if (_boundNodeCache.TryGetValue(node, out b))
                return b;

            throw new KeyNotFoundException($"No bound node for syntax: {node.Kind}");

        }
        private void EnsureBoundTreeFor(SyntaxNode node, CancellationToken ct)
        {
            if (_diagnosticsCache != null) return;

            var bag = new DiagnosticBag();
            var recorder = (IBindingRecorder)this;

            var importScopeMap = ImportsBuilder.BuildImportScopeMap(Compilation, SyntaxTree, recorder, bag);
            var typeBinder = new TypeBinder(parent: null, flags: BinderFlags.None, compilation: Compilation, importScopeMap: importScopeMap);

            // Bind top level statements if present
            var entryPoint = Compilation.EntryPoint;
            var hasTopLevel = SyntaxTree.Root.Members.Any(m => m is GlobalStatementSyntax);

            if (entryPoint != null && hasTopLevel)
            {
                var binder = new LocalScopeBinder(
                    parent: typeBinder,
                    flags: BinderFlags.InMethod,
                    containing: entryPoint);

                var ctx = new BindingContext(Compilation, this, entryPoint, recorder);
                var stmtSyntaxes = ImmutableArray.CreateBuilder<StatementSyntax>();
                foreach (var m in SyntaxTree.Root.Members)
                {
                    if (m is GlobalStatementSyntax gs)
                        stmtSyntaxes.Add(gs.Statement);
                }
                binder.PredeclareLocalFunctionsInStatementList(stmtSyntaxes.ToImmutable(), ctx, bag);
                var stmts = ImmutableArray.CreateBuilder<BoundStatement>();
                foreach (var m in SyntaxTree.Root.Members)
                {
                    if (m is GlobalStatementSyntax gs)
                    {
                        var bound = binder.BindStatement(gs.Statement, ctx, bag);
                        recorder.RecordBound(gs, bound);
                        stmts.Add(bound);
                    }
                }
                binder.ReportControlFlowDiagnostics(ctx, bag);
                var bodyStmt = new BoundStatementList(SyntaxTree.Root, stmts.ToImmutable());
                var topLevelBody = new BoundMethodBody(SyntaxTree.Root, entryPoint, bodyStmt);

                var root = new BoundCompilationUnit(SyntaxTree.Root, ImmutableArray<BoundStatement>.Empty, topLevelBody);
                recorder.RecordBound(SyntaxTree.Root, root);
            }
            else
            {
                var root = new BoundCompilationUnit(SyntaxTree.Root, ImmutableArray<BoundStatement>.Empty);
                recorder.RecordBound(SyntaxTree.Root, root);
            }
            // Bind bodies of declared methods/ctors
            if (Compilation.DeclaredSymbolsByTree.TryGetValue(SyntaxTree, out var declMap))
            {
                foreach (var kv in declMap)
                {
                    if (kv.Key is MethodDeclarationSyntax md && kv.Value is MethodSymbol ms)
                    {
                        BindMethodLikeBody(md, md.Body, md.ExpressionBody, ms, typeBinder, recorder, bag);
                    }
                    else if (kv.Key is ConstructorDeclarationSyntax cd && kv.Value is MethodSymbol ctor)
                    {
                        BindMethodLikeBody(cd, cd.Body, cd.ExpressionBody, ctor, typeBinder, recorder, bag);
                    }
                    else if (kv.Key is PropertyDeclarationSyntax pd && kv.Value is PropertySymbol prop)
                    {
                        BindPropertyBodies(pd, prop, typeBinder, recorder, bag);
                    }
                    else if (kv.Key is OperatorDeclarationSyntax od && kv.Value is MethodSymbol opMethod)
                    {
                        BindMethodLikeBody(od, od.Body, od.ExpressionBody, opMethod, typeBinder, recorder, bag);
                    }
                    else if (kv.Key is ConversionOperatorDeclarationSyntax cod && kv.Value is MethodSymbol convMethod)
                    {
                        BindMethodLikeBody(cod, cod.Body, cod.ExpressionBody, convMethod, typeBinder, recorder, bag);
                    }
                    else if (kv.Key is IndexerDeclarationSyntax ids && kv.Value is PropertySymbol idxProp)
                    {
                        BindIndexerBodies(ids, idxProp, typeBinder, recorder, bag);
                    }
                }
            }
            _diagnosticsCache = bag.ToImmutable();
        }
        private static bool HasModifier(SyntaxTokenList modifiers, SyntaxKind kind)
        {
            foreach (var t in modifiers)
                if (t.Kind == kind)
                    return true;
            return false;
        }
        private void BindPropertyBodies(
            PropertyDeclarationSyntax propertySyntax,
            PropertySymbol property,
            TypeBinder typeBinder,
            IBindingRecorder recorder,
            DiagnosticBag diagnostics)
        {
            if (propertySyntax.ExpressionBody is not null)
            {
                if (property.GetMethod is MethodSymbol getMethod)
                {
                    BindAccessorBody(
                        ownerSyntax: propertySyntax,
                        isUnsafeRegion: HasModifier(propertySyntax.Modifiers, SyntaxKind.UnsafeKeyword),
                        body: null,
                        exprBody: propertySyntax.ExpressionBody,
                        method: getMethod,
                        typeBinder: typeBinder,
                        recorder: recorder,
                        diagnostics: diagnostics);
                }
                return;
            }

            var accessorList = propertySyntax.AccessorList;
            if (accessorList is null)
                return;

            var isUnsafe = HasModifier(propertySyntax.Modifiers, SyntaxKind.UnsafeKeyword);
            var backingField = (property is SourcePropertySymbol sp) ? sp.BackingFieldOpt : null;
            for (int i = 0; i < accessorList.Accessors.Count; i++)
            {
                var acc = accessorList.Accessors[i];

                switch (acc.Kind)
                {
                    case SyntaxKind.GetAccessorDeclaration:
                        if (property.GetMethod is MethodSymbol getMethod)
                        {
                            if (acc.Body != null || acc.ExpressionBody != null)
                            {
                                BindAccessorBody(
                                    ownerSyntax: acc,
                                    isUnsafeRegion: isUnsafe,
                                    body: acc.Body,
                                    exprBody: acc.ExpressionBody,
                                    method: getMethod,
                                    typeBinder: typeBinder,
                                    recorder: recorder,
                                    diagnostics: diagnostics);
                            }
                            else if (backingField is not null)
                            {
                                BindAutoPropertyAccessorBody(
                                    propertySyntax: propertySyntax,
                                    ownerSyntax: acc,
                                    isUnsafeRegion: isUnsafe,
                                    method: getMethod,
                                    typeBinder: typeBinder,
                                    recorder: recorder,
                                    diagnostics: diagnostics,
                                    backingField: backingField,
                                    isGetter: true);
                            }
                        }
                        break;

                    case SyntaxKind.SetAccessorDeclaration:
                        if (property.SetMethod is MethodSymbol setMethod)
                        {
                            if (acc.Body != null || acc.ExpressionBody != null)
                            {
                                BindAccessorBody(
                                    ownerSyntax: acc,
                                    isUnsafeRegion: isUnsafe,
                                    body: acc.Body,
                                    exprBody: acc.ExpressionBody,
                                    method: setMethod,
                                    typeBinder: typeBinder,
                                    recorder: recorder,
                                    diagnostics: diagnostics);
                            }
                            else if (backingField is not null)
                            {
                                BindAutoPropertyAccessorBody(
                                    propertySyntax: propertySyntax,
                                    ownerSyntax: acc,
                                    isUnsafeRegion: isUnsafe,
                                    method: setMethod,
                                    typeBinder: typeBinder,
                                    recorder: recorder,
                                    diagnostics: diagnostics,
                                    backingField: backingField,
                                    isGetter: false);
                            }
                        }
                        break;
                }
            }
        }
        private void BindAutoPropertyAccessorBody(
            PropertyDeclarationSyntax propertySyntax,
            SyntaxNode ownerSyntax,
            bool isUnsafeRegion,
            MethodSymbol method,
            TypeBinder typeBinder,
            IBindingRecorder recorder,
            DiagnosticBag diagnostics,
            FieldSymbol backingField,
            bool isGetter)
        {
            var ctx = new BindingContext(Compilation, this, method, recorder);
            var flags = BinderFlags.InMethod;
            if (isUnsafeRegion)
                flags |= BinderFlags.UnsafeRegion;
            var binder = new LocalScopeBinder(parent: typeBinder, flags: flags, containing: method);
            BoundExpression? receiver = null;
            if (!backingField.IsStatic)
            {
                if (method.ContainingSymbol is not NamedTypeSymbol containingType)
                    return; // should not happen
                receiver = new BoundThisExpression(new ThisExpressionSyntax(default), containingType);
            }
            BoundMemberAccessExpression FieldAccess(bool isLValue)
                => new BoundMemberAccessExpression(
                    syntax: new ThisExpressionSyntax(default),
                    receiverOpt: receiver,
                    member: backingField,
                    type: backingField.Type,
                    isLValue: isLValue,
                    constantValueOpt: Optional<object>.None);
            if (isGetter)
            {
                var stmts = ImmutableArray.CreateBuilder<BoundStatement>(initialCapacity: 4);
                if (propertySyntax.Initializer is not null && backingField.Type.IsReferenceType)
                {
                    // if (field != null) goto done;
                    var boolType = Compilation.GetSpecialType(SpecialType.System_Boolean);
                    var fieldRead1 = FieldAccess(isLValue: false);
                    var nullLit = new BoundLiteralExpression(ownerSyntax, backingField.Type, null);
                    var notNull = new BoundBinaryExpression(
                        ownerSyntax,
                        BoundBinaryOperatorKind.NotEquals,
                        boolType,
                        fieldRead1,
                        nullLit,
                        Optional<object>.None);
                    var done = LabelSymbol.CreateGenerated("<auto_prop_get_done>", method);
                    stmts.Add(new BoundConditionalGotoStatement(ownerSyntax, notNull, done, jumpIfTrue: true));

                    // field = <init>;
                    var initExpr = binder.BindExpression(propertySyntax.Initializer.Value, ctx, diagnostics);
                    initExpr = binder.ApplyConversion(
                        exprSyntax: propertySyntax.Initializer.Value,
                        expr: initExpr,
                        targetType: backingField.Type,
                        diagnosticNode: propertySyntax.Initializer,
                        context: ctx,
                        diagnostics: diagnostics,
                        requireImplicit: true);

                    stmts.Add(new BoundExpressionStatement(
                        ownerSyntax,
                        new BoundAssignmentExpression(ownerSyntax, FieldAccess(isLValue: true), initExpr)));
                    stmts.Add(new BoundLabelStatement(ownerSyntax, done));
                }
                var fieldRead2 = FieldAccess(isLValue: false);
                stmts.Add(new BoundReturnStatement(ownerSyntax, fieldRead2));

                var body = new BoundBlockStatement(ownerSyntax, stmts.ToImmutable());
                recorder.RecordBound(ownerSyntax, new BoundMethodBody(ownerSyntax, method, body));
                return;
            }
            // field = value;
            if (method.Parameters.Length != 1)
                return;

            var valueExpr = new BoundParameterExpression(ownerSyntax, method.Parameters[0]);
            var assignExpr = new BoundAssignmentExpression(ownerSyntax, FieldAccess(isLValue: true), valueExpr);
            var setBody = new BoundBlockStatement(ownerSyntax, 
                ImmutableArray.Create<BoundStatement>(new BoundExpressionStatement(ownerSyntax, assignExpr)));
            recorder.RecordBound(ownerSyntax, new BoundMethodBody(ownerSyntax, method, setBody));
        }
        private void BindAccessorBody(
            SyntaxNode ownerSyntax,
            bool isUnsafeRegion,
            BlockSyntax? body,
            ArrowExpressionClauseSyntax? exprBody,
            MethodSymbol method,
            TypeBinder typeBinder,
            IBindingRecorder recorder,
            DiagnosticBag diagnostics)
        {
            if (body == null && exprBody == null)
                return;

            var ctx = new BindingContext(Compilation, this, method, recorder);
            var flags = BinderFlags.InMethod;
            if (isUnsafeRegion)
                flags |= BinderFlags.UnsafeRegion;

            var binder = new LocalScopeBinder(parent: typeBinder, flags: flags, containing: method);

            BoundStatement boundBody;

            if (body != null)
            {
                boundBody = binder.BindStatement(body, ctx, diagnostics);
                binder.ReportControlFlowDiagnostics(ctx, diagnostics);
            }
            else
            {
                var expr = binder.BindExpression(exprBody!.Expression, ctx, diagnostics);

                if (method.ReturnType.SpecialType == SpecialType.System_Void)
                {
                    boundBody = new BoundExpressionStatement(exprBody, expr);
                    recorder.RecordBound(exprBody, boundBody);
                }
                else
                {
                    expr = binder.ApplyConversion(
                        exprSyntax: exprBody.Expression,
                        expr: expr,
                        targetType: method.ReturnType,
                        diagnosticNode: exprBody,
                        context: ctx,
                        diagnostics: diagnostics,
                        requireImplicit: true);

                    var ret = new BoundReturnStatement(exprBody, expr);
                    recorder.RecordBound(exprBody, ret);
                    boundBody = ret;
                }
            }
            var methodBody = new BoundMethodBody(ownerSyntax, method, boundBody);
            recorder.RecordBound(ownerSyntax, methodBody);
        }
        private void BindIndexerBodies(
            IndexerDeclarationSyntax indexerSyntax,
            PropertySymbol indexer,
            TypeBinder typeBinder,
            IBindingRecorder recorder,
            DiagnosticBag diagnostics)
        {
            if (indexerSyntax.ExpressionBody is not null)
            {
                if (indexer.GetMethod is MethodSymbol getMethod)
                {
                    BindAccessorBody(
                        ownerSyntax: indexerSyntax,
                        isUnsafeRegion: HasModifier(indexerSyntax.Modifiers, SyntaxKind.UnsafeKeyword),
                        body: null,
                        exprBody: indexerSyntax.ExpressionBody,
                        method: getMethod,
                        typeBinder: typeBinder,
                        recorder: recorder,
                        diagnostics: diagnostics);
                }
                return;
            }
            var accessorList = indexerSyntax.AccessorList;
            if (accessorList is null)
                return;

            var isUnsafe = HasModifier(indexerSyntax.Modifiers, SyntaxKind.UnsafeKeyword);
            for (int i = 0; i < accessorList.Accessors.Count; i++)
            {
                var acc = accessorList.Accessors[i];
                switch (acc.Kind)
                {
                    case SyntaxKind.GetAccessorDeclaration:
                        if (indexer.GetMethod is MethodSymbol getMethod && (acc.Body != null || acc.ExpressionBody != null))
                        {
                            BindAccessorBody(
                                ownerSyntax: acc,
                                isUnsafeRegion: isUnsafe,
                                body: acc.Body,
                                exprBody: acc.ExpressionBody,
                                method: getMethod,
                                typeBinder: typeBinder,
                                recorder: recorder,
                                diagnostics: diagnostics);
                        }
                        break;

                    case SyntaxKind.SetAccessorDeclaration:
                        if (indexer.SetMethod is MethodSymbol setMethod && (acc.Body != null || acc.ExpressionBody != null))
                        {
                            BindAccessorBody(
                                ownerSyntax: acc,
                                isUnsafeRegion: isUnsafe,
                                body: acc.Body,
                                exprBody: acc.ExpressionBody,
                                method: setMethod,
                                typeBinder: typeBinder,
                                recorder: recorder,
                                diagnostics: diagnostics);
                        }
                        break;
                }
            }
        }
        private void BindMethodLikeBody(
            SyntaxNode ownerSyntax,
            BlockSyntax? body,
            ArrowExpressionClauseSyntax? exprBody,
            MethodSymbol method,
            TypeBinder typeBinder,
            IBindingRecorder recorder,
            DiagnosticBag diagnostics)
        {
            if (body == null && exprBody == null)
                return;

            var ctx = new BindingContext(Compilation, this, method, recorder);
            var flags = BinderFlags.InMethod;
            if (ownerSyntax is ConstructorDeclarationSyntax)
                flags |= BinderFlags.InConstructor;
            if (HasUnsafeModifier(ownerSyntax))
                flags |= BinderFlags.UnsafeRegion;
            var binder = new LocalScopeBinder(parent: typeBinder, flags: flags, containing: method);
            BoundStatement? ctorInitializerStmt = null;
            if (ownerSyntax is ConstructorDeclarationSyntax ctorSyntax)
                ctorInitializerStmt = binder.BindConstructorInitializer(ctorSyntax, ctx, diagnostics);
            BoundStatement boundBody;

            if (body != null)
            {
                boundBody = binder.BindStatement(body, ctx, diagnostics);
                binder.ReportControlFlowDiagnostics(ctx, diagnostics);
            }
            else
            {
                var expr = binder.BindExpression(exprBody!.Expression, ctx, diagnostics);

                if (method.ReturnType.SpecialType == SpecialType.System_Void)
                {
                    boundBody = new BoundExpressionStatement(exprBody, expr);
                    recorder.RecordBound(exprBody, boundBody);
                }
                else
                {
                    expr = binder.ApplyConversion(
                        exprSyntax: exprBody.Expression,
                        expr: expr,
                        targetType: method.ReturnType,
                        diagnosticNode: exprBody,
                        context: ctx,
                        diagnostics: diagnostics,
                        requireImplicit: true);

                    var ret = new BoundReturnStatement(exprBody, expr);
                    recorder.RecordBound(exprBody, ret);
                    boundBody = ret;
                }
            }
            if (ctorInitializerStmt != null)
                boundBody = PrependStatement(boundBody, ctorInitializerStmt);
            var methodBody = new BoundMethodBody(ownerSyntax, method, boundBody);
            recorder.RecordBound(ownerSyntax, methodBody);
            static BoundStatement PrependStatement(BoundStatement body, BoundStatement statement)
            {
                if (body is BoundBlockStatement block)
                {
                    var b = ImmutableArray.CreateBuilder<BoundStatement>(block.Statements.Length + 1);
                    b.Add(statement);
                    b.AddRange(block.Statements);
                    return new BoundBlockStatement(block.Syntax, b.ToImmutable());
                }
                return new BoundBlockStatement(body.Syntax, ImmutableArray.Create(statement, body));
            }
            static bool HasUnsafeModifier(SyntaxNode node)
            {
                SyntaxTokenList mods;
                switch (node)
                {
                    case MethodDeclarationSyntax m:
                        mods = m.Modifiers;
                        break;
                    case ConstructorDeclarationSyntax c:
                        mods = c.Modifiers;
                        break;
                    case OperatorDeclarationSyntax o:
                        mods = o.Modifiers;
                        break;
                    case ConversionOperatorDeclarationSyntax co:
                        mods = co.Modifiers;
                        break;
                    default:
                        return false;
                }

                foreach (var t in mods)
                    if (t.Kind == SyntaxKind.UnsafeKeyword)
                        return true;

                return false;
            }
        }
    }
    internal static class ImportsBuilder
    {
        public static Imports Build(Compilation compilation, SyntaxTree tree, IBindingRecorder recorder, DiagnosticBag diagnostics)
        {
            var containers = ImmutableArray.CreateBuilder<Symbol>();
            var aliases = ImmutableDictionary.CreateBuilder<string, AliasSymbol>(StringComparer.Ordinal);
            var staticTypes = ImmutableArray.CreateBuilder<NamedTypeSymbol>();

            AddImplicitUsing(compilation, containers, "System");
            AddImplicitUsing(compilation, containers, "System.Collections");
            AddImplicitUsing(compilation, containers, "System.Collections.Generic");
            AddImplicitUsing(compilation, containers, "System.Runtime.CompilerServices");

            foreach (var u in tree.Root.Usings)
            {
                if (u.StaticKeyword.Span.Length != 0)
                {
                    if (u.Alias != null)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_USING_STATIC_ALIAS001",
                            DiagnosticSeverity.Error,
                            "A 'using static' directive cannot declare an alias.",
                            new Location(tree, u.Span)));
                        continue;
                    }
                    var target = ResolveNamespaceOrType(compilation, u.Name, containers.ToImmutable(), aliases.ToImmutable(), diagnostics, tree);
                    if (target is null)
                        continue;

                    if (target is not NamedTypeSymbol nt)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_USING_STATIC001",
                            DiagnosticSeverity.Error,
                            "A 'using static' directive must reference a type.",
                            new Location(tree, u.Span)));
                        continue;
                    }
                    bool dup = false;
                    for (int i = 0; i < staticTypes.Count; i++)
                    {
                        if (ReferenceEquals(staticTypes[i], nt))
                        {
                            dup = true;
                            break;
                        }
                    }
                    if (!dup)
                        staticTypes.Add(nt);

                    continue;
                }
                {
                    var target = ResolveNamespaceOrType(compilation, u.Name, containers.ToImmutable(), aliases.ToImmutable(), diagnostics, tree);
                    if (target is null)
                        continue;

                    if (u.Alias != null)
                    {
                        var aliasName = u.Alias.Name.Identifier.ValueText ?? "";
                        if (aliasName.Length == 0)
                            continue;

                        var aliasSym = new AliasSymbol(
                            name: aliasName,
                            containing: null,
                            target: target,
                            locations: ImmutableArray.Create(new Location(tree, u.Alias.Span)));

                        aliases[aliasName] = aliasSym;

                        recorder.RecordDeclared(u, aliasSym);
                    }
                    else
                    {
                        containers.Add(target);
                    }
                }
                
            }

            return new Imports(containers.ToImmutable(), aliases.ToImmutable(), staticTypes.ToImmutable());
        }
        public static ImportScopeMap BuildImportScopeMap(
            Compilation compilation,
            SyntaxTree tree,
            IBindingRecorder recorder,
            DiagnosticBag diagnostics)
        {
            var compilationLevel = BuildCompilationLevelImports(compilation, tree, recorder, diagnostics);

            var rootImports = ApplyUsingDirectives(
                compilation: compilation,
                tree: tree,
                usings: tree.Root.Usings,
                baseImports: compilationLevel,
                recorder: recorder,
                diagnostics: diagnostics,
                allowGlobalUsing: true,
                includeGlobalDirectives: false);

            var scopes = new List<(TextSpan Span, Imports Imports)>();

            var members = tree.Root.Members;
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i] is BaseNamespaceDeclarationSyntax ns)
                    BuildNamespaceScopes(compilation, tree, ns, rootImports, recorder, diagnostics, scopes);
            }

            var b = ImmutableArray.CreateBuilder<(TextSpan Span, Imports Imports)>(scopes.Count);
            for (int i = 0; i < scopes.Count; i++)
                b.Add(scopes[i]);

            return new ImportScopeMap(rootImports, b.ToImmutable());
        }
        private static void BuildNamespaceScopes(
            Compilation compilation,
            SyntaxTree tree,
            BaseNamespaceDeclarationSyntax ns,
            Imports parentImports,
            IBindingRecorder recorder,
            DiagnosticBag diagnostics,
            List<(TextSpan Span, Imports Imports)> scopes)
        {
            var nsImports = ApplyUsingDirectives(
                compilation: compilation,
                tree: tree,
                usings: ns.Usings,
                baseImports: parentImports,
                recorder: recorder,
                diagnostics: diagnostics,
                allowGlobalUsing: false,
                includeGlobalDirectives: false);

            scopes.Add((ns.Span, nsImports));

            var members = ns.Members;
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i] is BaseNamespaceDeclarationSyntax inner)
                    BuildNamespaceScopes(compilation, tree, inner, nsImports, recorder, diagnostics, scopes);
            }
        }
        private static Imports BuildCompilationLevelImports(
            Compilation compilation,
            SyntaxTree requestingTree,
            IBindingRecorder recorder,
            DiagnosticBag diagnostics)
        {
            var baseImports = CreateImplicitImports(compilation);

            var containers = baseImports.Containers.ToBuilder();
            var aliases = baseImports.Aliases.ToBuilder();
            var staticTypes = baseImports.StaticTypes.ToBuilder();

            var seenGlobalAliases = new HashSet<string>(StringComparer.Ordinal);

            var trees = compilation.SyntaxTrees;
            for (int ti = 0; ti < trees.Length; ti++)
            {
                var tree = trees[ti];
                var usings = tree.Root.Usings;

                for (int ui = 0; ui < usings.Count; ui++)
                {
                    var u = usings[ui];
                    bool isGlobal = u.GlobalKeyword.Span.Length != 0;
                    if (!isGlobal)
                        continue;

                    ApplyOneUsingDirective(
                        compilation: compilation,
                        tree: tree,
                        usingDirective: u,
                        containers: containers,
                        aliases: aliases,
                        staticTypes: staticTypes,
                        recorder: ReferenceEquals(tree, requestingTree) ? recorder : null,
                        diagnostics: diagnostics,
                        allowGlobalUsing: true,
                        includeGlobalDirectives: true,
                        seenAliasesInCurrentScope: seenGlobalAliases);
                }
            }

            return new Imports(containers.ToImmutable(), aliases.ToImmutable(), staticTypes.ToImmutable());
        }
        private static Imports CreateImplicitImports(Compilation compilation)
        {
            var containers = ImmutableArray.CreateBuilder<Symbol>();
            var aliases = ImmutableDictionary.CreateBuilder<string, AliasSymbol>(StringComparer.Ordinal);
            var staticTypes = ImmutableArray.CreateBuilder<NamedTypeSymbol>();

            AddImplicitUsing(compilation, containers, "System");
            AddImplicitUsing(compilation, containers, "System.Collections");
            AddImplicitUsing(compilation, containers, "System.Collections.Generic");
            AddImplicitUsing(compilation, containers, "System.Runtime.CompilerServices");

            return new Imports(containers.ToImmutable(), aliases.ToImmutable(), staticTypes.ToImmutable());
        }
        private static Imports ApplyUsingDirectives(
            Compilation compilation,
            SyntaxTree tree,
            SyntaxList<UsingDirectiveSyntax> usings,
            Imports baseImports,
            IBindingRecorder recorder,
            DiagnosticBag diagnostics,
            bool allowGlobalUsing,
            bool includeGlobalDirectives)
        {
            var containers = baseImports.Containers.ToBuilder();
            var aliases = baseImports.Aliases.ToBuilder();
            var staticTypes = baseImports.StaticTypes.ToBuilder();

            var seenAliasesInThisScope = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < usings.Count; i++)
            {
                ApplyOneUsingDirective(
                    compilation: compilation,
                    tree: tree,
                    usingDirective: usings[i],
                    containers: containers,
                    aliases: aliases,
                    staticTypes: staticTypes,
                    recorder: recorder,
                    diagnostics: diagnostics,
                    allowGlobalUsing: allowGlobalUsing,
                    includeGlobalDirectives: includeGlobalDirectives,
                    seenAliasesInCurrentScope: seenAliasesInThisScope);
            }

            return new Imports(containers.ToImmutable(), aliases.ToImmutable(), staticTypes.ToImmutable());
        }
        private static void ApplyOneUsingDirective(
            Compilation compilation,
            SyntaxTree tree,
            UsingDirectiveSyntax usingDirective,
            ImmutableArray<Symbol>.Builder containers,
            ImmutableDictionary<string, AliasSymbol>.Builder aliases,
            ImmutableArray<NamedTypeSymbol>.Builder staticTypes,
            IBindingRecorder? recorder,
            DiagnosticBag diagnostics,
            bool allowGlobalUsing,
            bool includeGlobalDirectives,
            HashSet<string> seenAliasesInCurrentScope)
        {
            bool isGlobal = usingDirective.GlobalKeyword.Span.Length != 0;

            if (isGlobal && !allowGlobalUsing)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_USING_GLOBAL001",
                    DiagnosticSeverity.Error,
                    "The 'global using' directive is only permitted at the compilation unit level.",
                    new Location(tree, usingDirective.Span)));
                return;
            }

            if (isGlobal && !includeGlobalDirectives)
            {
                // Already processed at compilation level
                return;
            }

            bool isStatic = usingDirective.StaticKeyword.Span.Length != 0;

            if (isStatic && usingDirective.Alias != null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_USING_STATIC_ALIAS001",
                    DiagnosticSeverity.Error,
                    "A 'using static' directive cannot declare an alias.",
                    new Location(tree, usingDirective.Span)));
                return;
            }

            var importedContainersSnapshot = containers.ToImmutable();
            var aliasesSnapshot = aliases.ToImmutable();

            var target = ResolveNamespaceOrType(
                compilation,
                usingDirective.Name,
                importedContainersSnapshot,
                aliasesSnapshot,
                diagnostics,
                tree);

            if (target is null)
                return;

            if (isStatic)
            {
                if (target is not NamedTypeSymbol nt)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_USING_STATIC001",
                        DiagnosticSeverity.Error,
                        "A 'using static' directive must reference a type.",
                        new Location(tree, usingDirective.Span)));
                    return;
                }

                AddStaticTypeUnique(staticTypes, nt);
                return;
            }

            if (usingDirective.Alias != null)
            {
                var aliasName = usingDirective.Alias.Name.Identifier.ValueText ?? "";
                if (aliasName.Length == 0)
                    return;

                if (!seenAliasesInCurrentScope.Add(aliasName))
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_USING_ALIAS_DUP001",
                        DiagnosticSeverity.Error,
                        $"The using alias '{aliasName}' is declared more than once in this scope.",
                        new Location(tree, usingDirective.Alias.Span)));
                    return;
                }

                var aliasSym = new AliasSymbol(
                    name: aliasName,
                    containing: null,
                    target: target,
                    locations: ImmutableArray.Create(new Location(tree, usingDirective.Alias.Span)));

                // Inner alias hides outer alias of the same name
                aliases[aliasName] = aliasSym;

                recorder?.RecordDeclared(usingDirective, aliasSym);
                return;
            }

            AddContainerUnique(containers, target);
        }
        private static void AddStaticTypeUnique(ImmutableArray<NamedTypeSymbol>.Builder staticTypes, NamedTypeSymbol nt)
        {
            for (int i = 0; i < staticTypes.Count; i++)
            {
                if (ReferenceEquals(staticTypes[i], nt))
                    return;
            }
            staticTypes.Add(nt);
        }
        private static Symbol? ResolveNamespaceOrType(
            Compilation compilation,
            NameSyntax name,
            ImmutableArray<Symbol> importedContainers,
            ImmutableDictionary<string, AliasSymbol> aliases,
            DiagnosticBag diagnostics,
            SyntaxTree tree)
        {
            var parts = CollectParts(name);
            if (parts.Count == 0)
                return null;

            var current = new List<Symbol>(capacity: 8);
            if (aliases.TryGetValue(parts[0].Name, out var alias))
            {
                current.Add(alias.Target);
            }
            else
            {
                current.Add(compilation.GlobalNamespace);
                for (int i = 0; i < importedContainers.Length; i++)
                    current.Add(importedContainers[i]);
            }

            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                var next = new List<Symbol>();

                foreach (var c in current)
                {
                    if (c is NamespaceSymbol ns)
                    {
                        foreach (var childNs in ns.GetNamespaceMembers())
                            if (StringComparer.Ordinal.Equals(childNs.Name, part.Name))
                                next.Add(childNs);

                        foreach (var t in ns.GetTypeMembers(part.Name, part.Arity))
                            next.Add(t);
                    }
                    else if (c is NamedTypeSymbol nt)
                    {
                        foreach (var t in nt.GetTypeMembers(part.Name, part.Arity))
                            next.Add(t);
                    }
                }

                if (next.Count == 0)
                {
                    diagnostics.Add(new Diagnostic("CN_USING002", DiagnosticSeverity.Error,
                        $"Using target '{part.Name}' not found.",
                        new Location(tree, part.Syntax.Span)));
                    return null;
                }

                current = next;
            }

            if (current.Count == 1)
                return current[0];

            diagnostics.Add(new Diagnostic("CN_USING003", DiagnosticSeverity.Error,
                $"Using target '{name}' is ambiguous.",
                new Location(tree, name.Span)));

            return null;
        }
        private static void AddImplicitUsing(Compilation compilation, ImmutableArray<Symbol>.Builder containers, string nsName)
        {
            var ns = TryGetNamespace(compilation.GlobalNamespace, nsName);
            if (ns != null)
                AddContainerUnique(containers, ns);
        }
        private static NamespaceSymbol? TryGetNamespace(NamespaceSymbol global, string dottedName)
        {
            if (string.IsNullOrEmpty(dottedName))
                return null;

            NamespaceSymbol? cur = global;
            int pos = 0;

            while (pos < dottedName.Length)
            {
                int nextDot = dottedName.IndexOf('.', pos);
                string part = nextDot < 0
                    ? dottedName.Substring(pos)
                    : dottedName.Substring(pos, nextDot - pos);

                NamespaceSymbol? next = null;
                foreach (var ns in cur!.GetNamespaceMembers())
                {
                    if (StringComparer.Ordinal.Equals(ns.Name, part))
                    {
                        next = ns;
                        break;
                    }
                }

                if (next is null)
                    return null;

                cur = next;
                if (nextDot < 0)
                    break;

                pos = nextDot + 1;
            }

            return cur;
        }
        private static void AddContainerUnique(ImmutableArray<Symbol>.Builder containers, Symbol sym)
        {
            for (int i = 0; i < containers.Count; i++)
            {
                if (ReferenceEquals(containers[i], sym))
                    return;
            }
            containers.Add(sym);
        }
        private readonly struct NamePart
        {
            public readonly string Name;
            public readonly int Arity;
            public readonly SyntaxNode Syntax;

            public NamePart(string name, int arity, SyntaxNode syntax)
            {
                Name = name;
                Arity = arity;
                Syntax = syntax;
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
                        dst.Add(new NamePart(id.Identifier.ValueText ?? "", 0, id));
                        return;

                    case GenericNameSyntax g:
                        dst.Add(new NamePart(g.Identifier.ValueText ?? "", g.TypeArgumentList.Arguments.Count, g));
                        return;

                    case QualifiedNameSyntax q:
                        Collect(q.Left, dst);
                        Collect(q.Right, dst);
                        return;

                    default:
                        dst.Add(new NamePart("", 0, n));
                        return;
                }
            }
        }
    }






    [Flags]
    public enum BinderFlags
    {
        None = 0,
        InType = 1 << 0,
        InMethod = 1 << 1,
        InConstructor = 1 << 2,
        InLambda = 1 << 3,
        InQuery = 1 << 4,
        UnsafeRegion = 1 << 5,
        CheckedContext = 1 << 6,
        UncheckedContext = 1 << 7,
    }
    public enum BoundNodeKind
    {
        CompilationUnit,
        MethodBody,
        // Expressions
        BadExpression,
        Literal,
        Local,
        Parameter,
        This,
        Base,
        Call,
        MemberAccess,
        IndexerAccess,
        Conversion,
        AsExpression,
        Unary,
        Binary,
        Assignment,
        CompoundAssignment,
        NullCoalescingAssignment,
        IncrementDecrement,
        Conditional,
        UnboundImplicitObjectCreation,
        ObjectCreation,
        LabelExpression,
        ArrayInitializer,
        ArrayCreation,
        ArrayElementAccess,
        StackAllocArrayCreation,
        AddressOf,
        RefExpression,
        PointerIndirection,
        PointerElementAccess,
        TupleExpression,
        Sequence,
        SizeOfExpression,
        CheckedExpression,
        UncheckedExpression,
        ThrowExpression,
        IsPatternExpression, 
        // Statements
        BadStatement,
        Block,
        StatementList,
        ExpressionStatement,
        LocalDeclaration,
        LocalFunctionStatement,
        EmptyStatement,
        Throw,
        Return,
        Break,
        Continue,
        If,
        While,
        DoWhile,
        For,
        ForEach,
        Goto,
        ConditionalGoto,
        LabelStatement,
        TryStatement,
        CatchBlock,
        CheckedStatement,
        UncheckedStatement, 
        FixedStatement,
        FixedInitializer,
    }
    public sealed class BindingContext
    {
        public Compilation Compilation { get; }
        public SemanticModel SemanticModel { get; }
        public Symbol ContainingSymbol { get; }
        public IBindingRecorder Recorder { get; }
        public BindingContext(
            Compilation compilation, 
            SemanticModel semanticModel, 
            Symbol containingSymbol,
            IBindingRecorder recorder)
        {
            Compilation = compilation;
            SemanticModel = semanticModel;
            ContainingSymbol = containingSymbol;
            Recorder = recorder;
        }
    }
    
}
