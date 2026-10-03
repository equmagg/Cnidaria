using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Cnidaria.Cs
{
    public interface ICoreLibraryProvider
    {
        void Populate(CoreLibraryBuilder core);
    }
    public sealed class MetadataReferenceSet : ICoreLibraryProvider
    {
        private readonly ImmutableArray<EcmaMetadata> _refs;
        private readonly Dictionary<NamedTypeSymbol, string> _asmByType =
            new(ReferenceEqualityComparer<NamedTypeSymbol>.Instance);

        private readonly HashSet<string> _seenModules = new(StringComparer.Ordinal);

        public MetadataReferenceSet(IEnumerable<EcmaMetadata> references)
        {
            if (references is null) throw new ArgumentNullException(nameof(references));
            _refs = references.ToImmutableArray();

            for (int i = 0; i < _refs.Length; i++)
            {
                var name = _refs[i].ModuleName;
                if (!_seenModules.Add(name))
                    throw new InvalidOperationException($"Duplicate metadata reference: '{name}'");
            }
        }

        public void Populate(CoreLibraryBuilder core)
        {
            for (int i = 0; i < _refs.Length; i++)
            {
                var md = _refs[i];
                var before = CollectAllTypes(core.GlobalNamespace);

                new MetadataCoreLibProvider(md).Populate(core);

                var after = CollectAllTypes(core.GlobalNamespace);
                RegisterNewTypes(md.ModuleName, before, after);
            }
            if (_refs.Length > 0)
            {
                string stdName = _refs[0].ModuleName;
                foreach (SpecialType st in Enum.GetValues<SpecialType>())
                {
                    if (st == SpecialType.None) continue;
                    var t = core.GetSpecialType(st);
                    _asmByType[t] = stdName;
                }
            }
        }
        public string? ResolveAssemblyName(NamedTypeSymbol type)
        {
            if (type is null) return null;
            var def = type.OriginalDefinition;
            return _asmByType.TryGetValue(def, out var asm) ? asm : null;
        }

        private void RegisterNewTypes(string asmName, ImmutableArray<NamedTypeSymbol> before, ImmutableArray<NamedTypeSymbol> after)
        {
            var beforeSet = new HashSet<NamedTypeSymbol>(before, ReferenceEqualityComparer<NamedTypeSymbol>.Instance);
            for (int i = 0; i < after.Length; i++)
            {
                var t = after[i];
                if (beforeSet.Contains(t))
                    continue;

                _asmByType[t.OriginalDefinition] = asmName;
            }
        }
        private static ImmutableArray<NamedTypeSymbol> CollectAllTypes(NamespaceSymbol root)
        {
            var set = new HashSet<NamedTypeSymbol>(ReferenceEqualityComparer<NamedTypeSymbol>.Instance);
            var list = new List<NamedTypeSymbol>();

            void AddTypeAndNested(NamedTypeSymbol t)
            {
                if (!set.Add(t)) return;
                list.Add(t);

                foreach (var nested in t.GetNestedTypes())
                    AddTypeAndNested(nested);
            }

            void VisitNs(NamespaceSymbol ns)
            {
                var types = ns.GetTypeMembers();
                for (int i = 0; i < types.Length; i++)
                    AddTypeAndNested(types[i]);

                var nss = ns.GetNamespaceMembers();
                for (int i = 0; i < nss.Length; i++)
                    VisitNs(nss[i]);
            }

            VisitNs(root);
            return list.ToImmutableArray();
        }
    }
    public sealed class CoreLibraryBuilder
    {
        private readonly TypeManager _types;
        private readonly SyntheticNamespaceSymbol _global;
        public NamespaceSymbol GlobalNamespace => _global;
        internal TypeManager Types => _types;
        internal CoreLibraryBuilder(TypeManager types, SyntheticNamespaceSymbol global)
        {
            _types = types;
            _global = global;
        }

        public NamedTypeSymbol GetSpecialType(SpecialType st) => _types.GetSpecialType(st);
        public NamespaceSymbol EnsureNamespace(string fullName)
        {
            var ns = _global;

            if (!string.IsNullOrWhiteSpace(fullName))
            {
                foreach (var part in fullName.Split('.', StringSplitOptions.RemoveEmptyEntries))
                    ns = ns.GetOrAddNamespace(part);
            }

            return ns;
        }
        public PointerTypeSymbol CreatePointerType(TypeSymbol pointedAtType)
            => _types.GetPointerType(pointedAtType);
        public FunctionPointerTypeSymbol CreateFunctionPointerType(
            FunctionPointerCallingConvention callingConvention,
            TypeSymbol returnType,
            FunctionPointerRefKind returnRefKind,
            ImmutableArray<FunctionPointerParameter> parameters)
            => _types.GetFunctionPointerType(callingConvention, returnType, returnRefKind, parameters);
        public ByRefTypeSymbol CreateByRefType(TypeSymbol elementType)
            => _types.GetByRefType(elementType);
        public ArrayTypeSymbol CreateArrayType(TypeSymbol elementType, int rank)
            => _types.GetArrayType(elementType, rank);
        public ArrayTypeSymbol CreateArrayType(TypeSymbol elementType, int rank, bool isSZArray)
            => _types.GetArrayType(elementType, rank, isSZArray);
        public NamedTypeSymbol ConstructNamedType(NamedTypeSymbol type, ImmutableArray<TypeSymbol> typeArguments)
            => _types.ConstructNamedType(type, typeArguments);
        public TupleTypeSymbol GetTupleType(ImmutableArray<TypeSymbol> elementTypes, ImmutableArray<string?> elementNames)
            => _types.GetTupleType(elementTypes, elementNames);
        public NamedTypeSymbol ConstructNestedType(NamedTypeSymbol definition, NamedTypeSymbol containingType, ImmutableArray<TypeSymbol> typeArguments)
            => _types.ConstructNamedType(definition, containingType, typeArguments);
        public FieldSymbol AddExternalField(
            NamedTypeSymbol containingType,
            string name,
            TypeSymbol type,
            bool isStatic,
            bool isConst,
            Accessibility declaredAccessibility,
            Optional<object> constantValueOpt = default)
        {
            var f = new ExternalFieldSymbol(
                name: name,
                containing: containingType,
                type: type,
                isStatic: isStatic,
                isConst: isConst,
                declaredAccessibility: declaredAccessibility,
                constantValueOpt: constantValueOpt);

            AddMemberToType(containingType, f);
            return f;
        }
        public PropertySymbol AddExternalProperty(
            NamedTypeSymbol containingType,
            string name,
            TypeSymbol type,
            bool isStatic,
            Accessibility declaredAccessibility,
            MethodSymbol? getMethod,
            MethodSymbol? setMethod,
            ImmutableArray<ParameterSymbol> parameters)
        {
            var p = new ExternalPropertySymbol(
                name,
                containingType,
                type,
                isStatic,
                declaredAccessibility: declaredAccessibility,
                getMethod,
                setMethod,
                parameters);

            AddMemberToType(containingType, p);
            return p;
        }
        private static void AddMemberToType(NamedTypeSymbol containingType, Symbol member)
        {
            switch (containingType)
            {
                case SourceNamedTypeSymbol s:
                    s.AddMember(member);
                    return;
                case SpecialNamedTypeSymbol sp:
                    sp.AddMember(member);
                    return;
                default:
                    throw new InvalidOperationException("Containing type must be a mutable core type.");
            }
        }
        private static NamedTypeSymbol? FindSystemType(NamespaceSymbol global, string name, int arity)
        {
            NamespaceSymbol? systemNs = null;
            var namespaces = global.GetNamespaceMembers();
            for (int i = 0; i < namespaces.Length; i++)
            {
                if (StringComparer.Ordinal.Equals(namespaces[i].Name, "System"))
                {
                    systemNs = namespaces[i];
                    break;
                }
            }
            if (systemNs is null)
                return null;
            var candidates = systemNs.GetTypeMembers(name, arity);
            return candidates.IsDefaultOrEmpty ? null : candidates[0];
        }
        private static void AddNestedTypeToType(NamedTypeSymbol containingType, NamedTypeSymbol nested)
        {
            switch (containingType)
            {
                case SourceNamedTypeSymbol s:
                    s.AddNestedType(nested);
                    return;

                case SpecialNamedTypeSymbol sp:
                    sp.AddNestedType(nested);
                    return;

                default:
                    throw new InvalidOperationException("Containing type must be a mutable core type.");
            }
        }
        private NamedTypeSymbol CreateTypeCore(
            Symbol containing,
            string name,
            TypeKind kind,
            int arity,
            Accessibility declaredAccessibility,
            bool isFromMetadata,
            bool isSealed)
        {
            var t = new SourceNamedTypeSymbol(
                name,
                containing,
                kind,
                arity: arity,
                declaredAccessibility: declaredAccessibility,
                isFromMetadata: isFromMetadata,
                isSealed: isSealed);

            TypeSymbol? defaultBase = kind switch
            {
                TypeKind.Class => _types.GetSpecialType(SpecialType.System_Object),
                TypeKind.Struct => _types.GetSpecialType(SpecialType.System_ValueType),
                TypeKind.Enum => _types.GetSpecialType(SpecialType.System_Enum),
                TypeKind.Interface => null,
                TypeKind.Delegate => FindSystemType(_global, "MulticastDelegate", 0)
                    ?? FindSystemType(_global, "Delegate", 0)
                    ?? _types.GetSpecialType(SpecialType.System_Object),
                _ => _types.GetSpecialType(SpecialType.System_Object),
            };

            t.SetDefaultBaseType(defaultBase);

            if (arity == 0)
            {
                t.SetTypeParameters(ImmutableArray<TypeParameterSymbol>.Empty);
            }
            else
            {
                var tps = ImmutableArray.CreateBuilder<TypeParameterSymbol>(arity);
                for (int i = 0; i < arity; i++)
                {
                    var tpName = i == 0 ? "T" : $"T{i}";
                    tps.Add(new TypeParameterSymbol(tpName, t, ordinal: i, locations: ImmutableArray<Location>.Empty));
                }
                t.SetTypeParameters(tps.ToImmutable());
            }

            switch (containing)
            {
                case SyntheticNamespaceSymbol ns:
                    ns.AddType(t);
                    break;

                case NamedTypeSymbol nt:
                    AddNestedTypeToType(nt, t);
                    break;

                default:
                    throw new InvalidOperationException("Unsupported containing symbol for imported type.");
            }

            return t;
        }
        public NamedTypeSymbol AddType(
            string @namespace,
            string name,
            TypeKind kind,
            int arity = 0,
            Accessibility declaredAccessibility = Accessibility.Public,
            bool isFromMetadata = false,
            bool isSealed = false)
        {
            var ns = (SyntheticNamespaceSymbol)EnsureNamespace(@namespace);
            return CreateTypeCore(
                ns,
                name,
                kind,
                arity,
                declaredAccessibility,
                isFromMetadata,
                isSealed);
        }

        public NamedTypeSymbol AddNestedType(
            NamedTypeSymbol containingType,
            string name,
            TypeKind kind,
            int arity = 0,
            Accessibility declaredAccessibility = Accessibility.Public,
            bool isFromMetadata = false,
            bool isSealed = false)
        {
            if (containingType is null) throw new ArgumentNullException(nameof(containingType));

            return CreateTypeCore(
                containingType,
                name,
                kind,
                arity,
                declaredAccessibility,
                isFromMetadata,
                isSealed);
        }
        public NamedTypeSymbol AddClass(string @namespace, string name)
            => AddType(@namespace, name, TypeKind.Class);
        public NamedTypeSymbol AddStruct(string @namespace, string name)
        => AddType(@namespace, name, TypeKind.Struct);
        public NamedTypeSymbol AddEnum(string @namespace, string name)
            => AddType(@namespace, name, TypeKind.Enum);
        public MethodSymbol AddIntrinsicStaticMethod(
            NamedTypeSymbol containingType,
            string name,
            TypeSymbol returnType,
            ImmutableArray<(string name, TypeSymbol type)> parameters,
            string intrinsicName)
        {

            var m = new IntrinsicMethodSymbol(
                name: name,
                containing: containingType,
                returnType: returnType,
                parameters: parameters,
                intrinsicName: intrinsicName);

            AddMemberToType(containingType, m);
            return m;
        }
        public MethodSymbol AddParameterlessInstanceConstructor(NamedTypeSymbol containingType)
        {
            var voidType = _types.GetSpecialType(SpecialType.System_Void);
            var ctor = new SynthesizedConstructorSymbol(
                containing: containingType,
                voidType: voidType,
                isStatic: false,
                parameters: ImmutableArray<ParameterSymbol>.Empty);
            AddMemberToType(containingType, ctor);
            return ctor;
        }
        public MethodSymbol AddExternalMethod(
            NamedTypeSymbol containingType,
            string name,
            TypeSymbol returnType,
            bool isStatic,
            bool isConstructor,
            ImmutableArray<(string name, TypeSymbol type)> parameters,
            Accessibility declaredAccessibility,
            bool isVirtual,
            bool isAbstract,
            bool isOverride,
            bool isSealed,
            bool isExtensionMethod,
            ImmutableArray<TypeParameterSymbol> typeParameters,
            bool isExtern = false,
            bool returnsByRefReadonly = false)
        {
            var m = new ExternalMethodSymbol(
                name: name,
                containing: containingType,
                returnType: returnType,
                isStatic: isStatic,
                isConstructor: isConstructor,
                parameters: parameters,
                declaredAccessibility: declaredAccessibility,
                isVirtual: isVirtual,
                isAbstract: isAbstract,
                isOverride: isOverride,
                isSealed: isSealed,
                isExtensionMethod: isExtensionMethod,
                isExtern: isExtern,
                returnsByRefReadonly: returnsByRefReadonly);

            if (!typeParameters.IsDefaultOrEmpty && m is ExternalMethodSymbol em)
                em.SetTypeParameters(typeParameters);

            AddMemberToType(containingType, m);
            return m;
        }
    }

    public sealed class MetadataCoreLibProvider : ICoreLibraryProvider
    {
        private readonly EcmaMetadata _md;
        private CoreLibraryBuilder _core = null!;
        private NamedTypeSymbol[] _typeByRid = Array.Empty<NamedTypeSymbol>();
        private readonly Dictionary<int, MethodSymbol> _methodByRid = new();
        private readonly Dictionary<int, PropertySymbol> _propertyByRid = new();
        private readonly Dictionary<int, ParameterSymbol> _paramByRid = new();
        private Dictionary<int, FieldSymbol> _fieldByRid = new();
        private readonly Dictionary<int, ConstantRow> _constByParent = new();
        private readonly HashSet<int> _extensionMethodRids = new();
        private readonly Dictionary<int, List<int>> _methodImplRowsByType = new();
        private readonly Dictionary<int, List<int>> _propertyRowsByType = new();
        private readonly Dictionary<int, List<int>> _genericParamRowsByMethod = new();
        private readonly Dictionary<int, List<int>> _constraintRowsByGenericParam = new();
        internal MetadataCoreLibProvider(EcmaMetadata md)
            => _md = md ?? throw new ArgumentNullException(nameof(md));
        public void Populate(CoreLibraryBuilder core)
        {
            var typeCount = _md.GetRowCount(MetadataTableKind.TypeDef);
            var typeByRid = new NamedTypeSymbol[typeCount + 1]; // 1 based
            _core = core;
            _typeByRid = typeByRid;

            var enclosingByRid = new Dictionary<int, int>();
            for (int i = 1; i <= _md.GetRowCount(MetadataTableKind.NestedClass); i++)
            {
                var row = _md.GetNestedClass(i);
                enclosingByRid[row.NestedTypeRid] = row.EnclosingTypeRid;
            }

            NamedTypeSymbol EnsureType(int rid)
            {
                if ((uint)rid >= (uint)typeByRid.Length || rid <= 0)
                    throw new BadImageFormatException($"Invalid TypeDef rid: {rid}");

                if (typeByRid[rid] is { } existing)
                    return existing;

                var td = _md.GetTypeDef(rid);
                var ns = _md.GetString(td.Namespace);
                var mdName = _md.GetString(td.Name);
                var (name, arity) = SplitArity(mdName);

                if (string.IsNullOrEmpty(name))
                    throw new BadImageFormatException($"TypeDef #{rid} has empty name.");

                bool isSealed = ((System.Reflection.TypeAttributes)td.Flags & System.Reflection.TypeAttributes.Sealed) != 0;

                if (TryMapSpecialType(ns, name, out var st) && !enclosingByRid.ContainsKey(rid))
                {
                    var special = core.GetSpecialType(st);
                    if (isSealed && special is SpecialNamedTypeSymbol specialNamed)
                        specialNamed.MarkSealed();
                    typeByRid[rid] = special;
                    return special;
                }

                var kind = InferKind(td);
                NamedTypeSymbol created;
                if (enclosingByRid.TryGetValue(rid, out int enclosingRid))
                {
                    var enclosing = EnsureType(enclosingRid);
                    created = core.AddNestedType(
                        enclosing,
                        name,
                        kind,
                        arity: arity,
                        declaredAccessibility: DecodeTypeAccessibility(td.Flags),
                        isFromMetadata: true,
                        isSealed: isSealed);
                }
                else
                {
                    created = core.AddType(
                        ns,
                        name,
                        kind,
                        arity: arity,
                        declaredAccessibility: DecodeTypeAccessibility(td.Flags),
                        isFromMetadata: true,
                        isSealed: isSealed);
                }

                typeByRid[rid] = created;
                return created;
            }

            for (int rid = 1; rid <= typeCount; rid++)
            {
                if (!_md.IsModuleTypeDef(rid))
                    _ = EnsureType(rid);
            }

            // apply declared base types from ExtendsEncoded
            for (int rid = 1; rid <= typeCount; rid++)
            {
                var td = _md.GetTypeDef(rid);
                var declaring = typeByRid[rid];

                if (td.ExtendsEncoded == 0)
                    continue;

                if (declaring is not SourceNamedTypeSymbol src)
                    continue; // special types already have BaseType fixed

                var baseType = ResolveTypeDefOrRef(
                    unchecked((uint)td.ExtendsEncoded),
                    typeByRid,
                    core,
                    declaringType: declaring,
                    methodTypeParameters: ImmutableArray<TypeParameterSymbol>.Empty);

                if (baseType is NamedTypeSymbol bt)
                    src.SetDeclaredBaseType(bt);
            }

            // Import interfaces
            var ifaceSets = new Dictionary<NamedTypeSymbol, List<TypeSymbol>>(
                ReferenceEqualityComparer<NamedTypeSymbol>.Instance);

            for (int rid = 1; rid <= _md.GetRowCount(MetadataTableKind.InterfaceImpl); rid++)
            {
                var row = _md.GetInterfaceImpl(rid);

                if ((uint)row.ClassTypeDefRid >= (uint)typeByRid.Length)
                    continue;

                if (typeByRid[row.ClassTypeDefRid] is not (SourceNamedTypeSymbol or SpecialNamedTypeSymbol))
                    continue;
                var owner = (NamedTypeSymbol)typeByRid[row.ClassTypeDefRid]!;

                var ifaceType = ResolveTypeDefOrRef(
                    unchecked((uint)row.InterfaceEncoded),
                    typeByRid,
                    core,
                    declaringType: owner,
                    methodTypeParameters: ImmutableArray<TypeParameterSymbol>.Empty);

                if (ifaceType is not NamedTypeSymbol iface || iface.TypeKind != TypeKind.Interface)
                    continue;

                if (!ifaceSets.TryGetValue(owner, out var set))
                {
                    set = new List<TypeSymbol>();
                    ifaceSets.Add(owner, set);
                }

                if (!set.Exists(existing => LocalScopeBinder.AreSameType(existing, iface)))
                    set.Add(iface);
            }

            foreach (var kv in ifaceSets)
            {
                switch (kv.Key)
                {
                    case SourceNamedTypeSymbol source:
                        source.SetDeclaredInterfaces(kv.Value.ToImmutableArray());
                        break;
                    case SpecialNamedTypeSymbol special:
                        special.SetDeclaredInterfaces(kv.Value.ToImmutableArray());
                        break;
                }
            }

            for (int rid = 1; rid <= _md.GetRowCount(MetadataTableKind.Constant); rid++)
                _constByParent[_md.GetConstant(rid).ParentToken] = _md.GetConstant(rid);
            CollectExtensionMethods(typeByRid);
            _fieldByRid = AddFields(core, typeByRid);
            IndexMemberRows();
            for (int rid = 1; rid <= typeCount; rid++)
            {
                int typeRid = rid;
                switch (typeByRid[rid])
                {
                    case SourceNamedTypeSymbol source:
                        source.SetMemberLoader(() => LoadMembers(typeRid));
                        break;
                    case SpecialNamedTypeSymbol special:
                        special.SetMemberLoader(() => LoadMembers(typeRid));
                        break;
                }
            }

            ApplyTypeLevelCustomAttributes(core, typeByRid);
            for (int rid = 1; rid <= _md.GetRowCount(MetadataTableKind.GenericParam); rid++)
            {
                if (MetadataToken.Table(_md.GetGenericParam(rid).OwnerToken) == MetadataToken.TypeDef)
                    ApplyGenericParameterConstraints(core, typeByRid, rid);
            }
        }

        // A compilation looks into few library types, so a type decodes its methods and properties on its first member lookup
        private void LoadMembers(int typeRid)
        {
            var core = _core;
            var typeByRid = _typeByRid;
            var (methodStart, methodEnd) = _md.GetMethodRange(typeRid);
            AddMethodsOfType(core, typeByRid, typeRid);
            AddPropertiesFromTableOfType(core, typeByRid, typeRid);
            AddPropertiesFromAccessorsOfType(core, typeByRid[typeRid]);
            ApplyMethodImplsOfType(core, typeByRid, typeRid);

            for (int mrid = methodStart; mrid < methodEnd; mrid++)
                MapParameters(mrid);
            for (int mrid = methodStart; mrid < methodEnd; mrid++)
            {
                ApplyCustomAttributesOf(core, typeByRid, MetadataToken.Make(MetadataToken.MethodDef, mrid));
                var (paramStart, paramEnd) = _md.GetParamRange(mrid);
                for (int prid = paramStart; prid < paramEnd; prid++)
                    ApplyCustomAttributesOf(core, typeByRid, MetadataToken.Make(MetadataToken.ParamDef, prid));
                if (_genericParamRowsByMethod.TryGetValue(mrid, out var genericParams))
                {
                    foreach (int gprid in genericParams)
                        ApplyCustomAttributesOf(core, typeByRid, MetadataToken.Make(MetadataToken.GenericParam, gprid));
                }
            }
            if (_propertyRowsByType.TryGetValue(typeRid, out var properties))
            {
                foreach (int prid in properties)
                    ApplyCustomAttributesOf(core, typeByRid, MetadataToken.Make(MetadataToken.PropertyDef, prid));
            }

            for (int mrid = methodStart; mrid < methodEnd; mrid++)
            {
                if (_genericParamRowsByMethod.TryGetValue(mrid, out var genericParams))
                {
                    foreach (int gprid in genericParams)
                        ApplyGenericParameterConstraints(core, typeByRid, gprid);
                }
            }
            ApplyExtensionMembers(core, methodStart, methodEnd);
        }

        private void IndexMemberRows()
        {
            for (int rid = 1; rid <= _md.GetRowCount(MetadataTableKind.MethodImpl); rid++)
                AddRow(_methodImplRowsByType, _md.GetMethodImpl(rid).ClassTypeDefRid, rid);

            for (int rid = 1; rid <= _md.GetRowCount(MetadataTableKind.Property); rid++)
            {
                var row = _md.GetProperty(rid);
                int accessor = row.GetMethod != 0 && MetadataToken.Table(row.GetMethod) == MetadataToken.MethodDef ? row.GetMethod
                    : row.SetMethod != 0 && MetadataToken.Table(row.SetMethod) == MetadataToken.MethodDef ? row.SetMethod
                    : 0;
                if (accessor != 0)
                    AddRow(_propertyRowsByType, _md.GetMethodOwnerTypeDefRid(MetadataToken.Rid(accessor)), rid);
            }

            for (int rid = 1; rid <= _md.GetRowCount(MetadataTableKind.GenericParam); rid++)
            {
                int owner = _md.GetGenericParam(rid).OwnerToken;
                if (MetadataToken.Table(owner) == MetadataToken.MethodDef)
                    AddRow(_genericParamRowsByMethod, MetadataToken.Rid(owner), rid);
            }

            for (int rid = 1; rid <= _md.GetRowCount(MetadataTableKind.GenericParamConstraint); rid++)
                AddRow(_constraintRowsByGenericParam, _md.GetGenericParamConstraint(rid).OwnerRid, rid);

            static void AddRow(Dictionary<int, List<int>> rows, int key, int rid)
            {
                if (!rows.TryGetValue(key, out var list))
                    rows[key] = list = new List<int>();
                list.Add(rid);
            }
        }
        // Rebuilds C# 14 extension members from [ExtensionMarker("<G>$N")] on their implementation methods
        private void ApplyExtensionMembers(CoreLibraryBuilder core, int methodStart, int methodEnd)
        {
            for (int mrid = methodStart; mrid < methodEnd; mrid++)
            {
                if (!_methodByRid.TryGetValue(mrid, out var method) ||
                    method is not ExternalMethodSymbol implementation || implementation.ContainingSymbol is not NamedTypeSymbol container)
                {
                    continue;
                }

                string? groupingName = null;
                foreach (var attribute in implementation.GetAttributes())
                {
                    if (MethodAttributeFacts.IsAttribute(attribute, "System.Runtime.CompilerServices", "ExtensionMarkerAttribute") &&
                        attribute.ConstructorArguments is [{ Value: string name }])
                    {
                        groupingName = name;
                    }
                }
                if (groupingName is null)
                    continue;

                NamedTypeSymbol? grouping = null;
                foreach (var member in container.GetMembers())
                {
                    if (member is NamedTypeSymbol nested && StringComparer.Ordinal.Equals(nested.Name, groupingName))
                        grouping = nested;
                }
                if (grouping is null)
                    continue;

                string methodName = implementation.Name;
                var parameterCount = implementation.Parameters.Length;
                ExtensionMemberInfo info;
                if (methodName.StartsWith("get_", StringComparison.Ordinal))
                    info = new(ExtensionMemberKind.Property, methodName.Substring(4), isStatic: parameterCount == 0, isSetter: false, grouping.Arity, groupingName);
                else if (methodName.StartsWith("set_", StringComparison.Ordinal))
                    info = new(ExtensionMemberKind.Property, methodName.Substring(4), isStatic: parameterCount == 1, isSetter: true, grouping.Arity, groupingName);
                else if (methodName.StartsWith("op_", StringComparison.Ordinal))
                    info = new(ExtensionMemberKind.Operator, methodName, isStatic: true, isSetter: false, grouping.Arity, groupingName);
                else
                    info = new(ExtensionMemberKind.Method, methodName, isStatic: !implementation.IsExtensionMethod, isSetter: false, grouping.Arity, groupingName);

                foreach (var member in grouping.GetMembers())
                {
                    if (member is not MethodSymbol { Name: "<Extension>$", Parameters.Length: 1 } marker)
                        continue;
                    var map = ImmutableDictionary.CreateBuilder<TypeParameterSymbol, TypeSymbol>();
                    for (int i = 0; i < grouping.TypeParameters.Length && i < implementation.TypeParameters.Length; i++)
                        map[grouping.TypeParameters[i]] = implementation.TypeParameters[i];
                    var receiverType = marker.Parameters[0].Type is ByRefTypeSymbol byRef ? byRef.ElementType : marker.Parameters[0].Type;
                    info.ExtendedType = TypeSubstituter.Substitute(receiverType, core.Types, map.ToImmutable());
                }
                implementation.SetExtensionMember(info);
            }
        }
        private void ApplyGenericParameterConstraints(CoreLibraryBuilder core, NamedTypeSymbol[] typeByRid, int genericParamRid)
        {
            if (ResolveGenericParameter(genericParamRid, typeByRid, _methodByRid) is not TypeParameterSymbol tp)
                return;

            var flags = (System.Reflection.GenericParameterAttributes)_md.GetGenericParam(genericParamRid).Flags;
            if ((flags & System.Reflection.GenericParameterAttributes.ReferenceTypeConstraint) != 0)
                tp.TrySetConstraint(GenericConstraintsFlags.ClassConstraint);
            if ((flags & System.Reflection.GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
                tp.TrySetConstraint(GenericConstraintsFlags.StructConstraint);
            else if ((flags & System.Reflection.GenericParameterAttributes.DefaultConstructorConstraint) != 0)
                tp.TrySetConstraint(GenericConstraintsFlags.ConstructorConstraint);
            if (((int)flags & 0x0020) != 0)
                tp.TrySetConstraint(GenericConstraintsFlags.AllowsRefStruct);

            var attributes = tp.GetAttributes();
            for (int i = 0; i < attributes.Length; i++)
            {
                if (MethodAttributeFacts.IsAttribute(attributes[i], "System.Runtime.CompilerServices", "IsUnmanagedAttribute"))
                    tp.TrySetConstraint(GenericConstraintsFlags.UnmanagedConstraint);
            }

            if (!_constraintRowsByGenericParam.TryGetValue(genericParamRid, out var constraintRows))
                return;
            foreach (int rid in constraintRows)
            {
                var row = _md.GetGenericParamConstraint(rid);
                int ownerToken = _md.GetGenericParam(row.OwnerRid).OwnerToken;
                int ownerRid = MetadataToken.Rid(ownerToken);
                NamedTypeSymbol? declaringType;
                ImmutableArray<TypeParameterSymbol> methodTypeParameters = ImmutableArray<TypeParameterSymbol>.Empty;
                if (MetadataToken.Table(ownerToken) == MetadataToken.MethodDef)
                {
                    var method = _methodByRid[ownerRid];
                    declaringType = method.ContainingSymbol as NamedTypeSymbol;
                    methodTypeParameters = method.TypeParameters;
                }
                else
                {
                    declaringType = typeByRid[ownerRid];
                }

                var constraint = ResolveTypeDefOrRef(unchecked((uint)row.ConstraintEncoded), typeByRid, core, declaringType, methodTypeParameters);
                // The struct flag already carries what a System.ValueType constraint says
                if (constraint is ErrorTypeSymbol ||
                    constraint.SpecialType == SpecialType.System_ValueType && (tp.GenericConstraint & GenericConstraintsFlags.StructConstraint) != 0)
                {
                    continue;
                }
                tp.AddConstraintType(constraint);
            }
        }
        private static Accessibility DecodeMethodAccessibility(ushort flags)
        {
            var a = (System.Reflection.MethodAttributes)(flags & (ushort)System.Reflection.MethodAttributes.MemberAccessMask);
            return a switch
            {
                System.Reflection.MethodAttributes.Private => Accessibility.Private,
                System.Reflection.MethodAttributes.FamANDAssem => Accessibility.ProtectedAndInternal,
                System.Reflection.MethodAttributes.Assembly => Accessibility.Internal,
                System.Reflection.MethodAttributes.Family => Accessibility.Protected,
                System.Reflection.MethodAttributes.FamORAssem => Accessibility.ProtectedOrInternal,
                System.Reflection.MethodAttributes.Public => Accessibility.Public,
                _ => Accessibility.Private
            };
        }

        private static Accessibility DecodeFieldAccessibility(ushort flags)
        {
            var a = (System.Reflection.FieldAttributes)(flags & (ushort)System.Reflection.FieldAttributes.FieldAccessMask);
            return a switch
            {
                System.Reflection.FieldAttributes.Private => Accessibility.Private,
                System.Reflection.FieldAttributes.FamANDAssem => Accessibility.ProtectedAndInternal,
                System.Reflection.FieldAttributes.Assembly => Accessibility.Internal,
                System.Reflection.FieldAttributes.Family => Accessibility.Protected,
                System.Reflection.FieldAttributes.FamORAssem => Accessibility.ProtectedOrInternal,
                System.Reflection.FieldAttributes.Public => Accessibility.Public,
                _ => Accessibility.Private
            };
        }

        private static Accessibility DecodeTypeAccessibility(int flags)
        {
            var vis = (System.Reflection.TypeAttributes)(flags & (int)System.Reflection.TypeAttributes.VisibilityMask);
            return vis switch
            {
                System.Reflection.TypeAttributes.Public => Accessibility.Public,
                System.Reflection.TypeAttributes.NotPublic => Accessibility.Internal,
                System.Reflection.TypeAttributes.NestedPublic => Accessibility.Public,
                System.Reflection.TypeAttributes.NestedPrivate => Accessibility.Private,
                System.Reflection.TypeAttributes.NestedFamily => Accessibility.Protected,
                System.Reflection.TypeAttributes.NestedAssembly => Accessibility.Internal,
                System.Reflection.TypeAttributes.NestedFamORAssem => Accessibility.ProtectedOrInternal,
                System.Reflection.TypeAttributes.NestedFamANDAssem => Accessibility.ProtectedAndInternal,
                _ => Accessibility.Internal
            };
        }

        private static Accessibility DerivePropertyAccessibility(MethodSymbol? get, MethodSymbol? set)
        {
            if (get is not null) return get.DeclaredAccessibility;
            if (set is not null) return set.DeclaredAccessibility;
            return Accessibility.Public;
        }
        private void ApplyParamRefKinds(MethodSymbol method, int methodRid)
        {
            var ps = method.Parameters;
            for (int i = 0; i < ps.Length; i++)
            {
                var p = ps[i];
                if (p.Type is not ByRefTypeSymbol)
                    continue;
                int rid = _md.FindParamRid(methodRid, i + 1);
                if (rid == 0)
                    continue;

                var attrs = (System.Reflection.ParameterAttributes)_md.GetParam(rid).Flags;
                if ((attrs & System.Reflection.ParameterAttributes.Out) != 0)
                {
                    p.RefKind = ParameterRefKind.Out;
                    p.IsReadOnlyRef = false;
                }
                else if ((attrs & System.Reflection.ParameterAttributes.In) != 0)
                {
                    p.RefKind = ParameterRefKind.In;
                    p.IsReadOnlyRef = true;
                }
                else
                {
                    p.RefKind = ParameterRefKind.Ref;
                    p.IsReadOnlyRef = false;
                }
            }
        }
        private void ApplyParamDefaultValues(MethodSymbol method, int methodRid, Dictionary<int, ConstantRow> constByParent)
        {
            if (constByParent.Count == 0)
                return;

            var ps = method.Parameters;
            for (int i = 0; i < ps.Length; i++)
            {
                var p = ps[i];
                if (p.Type is ByRefTypeSymbol)
                    continue;
                int rid = _md.FindParamRid(methodRid, i + 1);
                if (rid == 0 || !constByParent.TryGetValue(MetadataToken.Make(MetadataToken.ParamDef, rid), out var crow))
                    continue;

                var cval = DecodeConstant(p.Type, crow);
                if (cval.HasValue)
                    p.SetDefaultValue(cval);
            }
        }
        private void ApplyMethodImplsOfType(CoreLibraryBuilder core, NamedTypeSymbol[] typeByRid, int typeRid)
        {
            if (!_methodImplRowsByType.TryGetValue(typeRid, out var rows))
                return;

            foreach (int rid in rows)
            {
                var row = _md.GetMethodImpl(rid);

                // !n in the tokens of a MethodImpl names the generic parameters of its class (ECMA-335 II.22.27)
                var genericContext = typeByRid[typeRid];
                var body = ResolveMethodToken(core, typeByRid, _methodByRid, row.BodyMethodToken, genericContext);
                var decl = ResolveMethodToken(core, typeByRid, _methodByRid, row.DeclarationMethodToken, genericContext);

                if (body is null || decl is null)
                    continue;
                // A static member implementing a static abstract or virtual member by name keeps its name; only explicit implementations are renamed.
                if (body.IsStatic && StringComparer.Ordinal.Equals(body.Name, decl.Name))
                    continue;
                switch (body)
                {
                    case ExternalMethodSymbol em:
                        em.SetExplicitInterfaceImplementation(decl);
                        break;
                    case SourceMethodSymbol sm:
                        sm.SetExplicitInterfaceImplementation(decl);
                        break;
                }

                if (FindPropertyOfAccessor(body) is PropertySymbol bodyProp && FindPropertyOfAccessor(decl) is PropertySymbol declProp)
                {
                    switch (bodyProp)
                    {
                        case ExternalPropertySymbol ep:
                            ep.SetExplicitInterfaceImplementation(declProp);
                            break;
                        case SourcePropertySymbol sp:
                            sp.SetExplicitInterfaceImplementation(declProp);
                            break;
                    }
                }
            }
        }

        private static PropertySymbol? FindPropertyOfAccessor(MethodSymbol accessor)
        {
            if (accessor.ContainingSymbol is not NamedTypeSymbol type)
                return null;
            var definition = accessor.OriginalDefinition;
            foreach (var member in type.GetMembers())
            {
                if (member is PropertySymbol property &&
                    (ReferenceEquals(property.GetMethod?.OriginalDefinition, definition) ||
                     ReferenceEquals(property.SetMethod?.OriginalDefinition, definition)))
                {
                    return property;
                }
            }
            return null;
        }
        private MethodSymbol? ResolveMethodToken(
            CoreLibraryBuilder core,
            NamedTypeSymbol[] typeByRid,
            Dictionary<int, MethodSymbol> methodByRid,
            int token,
            NamedTypeSymbol? genericContext = null)
        {
            int table = MetadataToken.Table(token);
            int rid = MetadataToken.Rid(token);
            return table switch
            {
                MetadataToken.MethodDef => ResolveMethodDef(methodByRid, rid),
                MetadataToken.MemberRef => ResolveMemberRefMethod(core, typeByRid, rid, genericContext),
                _ => null
            };
        }
        private MethodSymbol? ResolveMethodDef(Dictionary<int, MethodSymbol> methodByRid, int methodRid)
        {
            if (!methodByRid.TryGetValue(methodRid, out var method) &&
                _typeByRid.Length > _md.GetMethodOwnerTypeDefRid(methodRid) &&
                _typeByRid[_md.GetMethodOwnerTypeDefRid(methodRid)] is NamedTypeSymbol owner)
            {
                _ = owner.GetMembers();
                methodByRid.TryGetValue(methodRid, out method);
            }
            return method;
        }
        private MethodSymbol? ResolveMemberRefMethod(
            CoreLibraryBuilder core, NamedTypeSymbol[] typeByRid, int memberRefRid, NamedTypeSymbol? genericContext)
        {
            if (memberRefRid <= 0 || memberRefRid > _md.GetRowCount(MetadataTableKind.MemberRef))
                return null;

            var row = _md.GetMemberRef(memberRefRid);
            string name = _md.GetString(row.Name);
            if (string.IsNullOrEmpty(name))
                return null;

            NamedTypeSymbol? ownerType = ResolveTypeToken(core, typeByRid, row.ClassToken, genericContext) as NamedTypeSymbol;
            if (ownerType is null)
                return null;
            if (name == ".ctor")
                name = ownerType.OriginalDefinition.Name;

            var sig = _md.GetBlob(row.Signature);
            if (sig.Length == 0)
                return null;

            var reader = new SigReader(sig);

            byte cc = reader.ReadByte();

            uint genArity = 0;
            if ((cc & 0x10) != 0)
                genArity = reader.ReadCompressedUInt();

            uint paramCount = reader.ReadCompressedUInt();

            bool hasThis = (cc & 0x20) != 0;
            bool isStatic = !hasThis;

            ImmutableArray<TypeParameterSymbol> methodTypeParameters = ImmutableArray<TypeParameterSymbol>.Empty;
            if (genArity != 0)
            {
                var tb = ImmutableArray.CreateBuilder<TypeParameterSymbol>((int)genArity);
                for (int i = 0; i < (int)genArity; i++)
                {
                    string tpName = (i == 0) ? "T" : $"T{i}";
                    tb.Add(new TypeParameterSymbol(
                        tpName,
                        containing: null,
                        ordinal: i,
                        locations: ImmutableArray<Location>.Empty));
                }
                methodTypeParameters = tb.ToImmutable();
            }

            var returnType = ReadType(core, typeByRid, ref reader, ownerType, methodTypeParameters);

            var parameterTypes = ImmutableArray.CreateBuilder<TypeSymbol>((int)paramCount);
            for (int i = 0; i < (int)paramCount; i++)
                parameterTypes.Add(ReadType(core, typeByRid, ref reader, ownerType, methodTypeParameters));

            return FindMethodBySignature(
                ownerType,
                name,
                returnType,
                parameterTypes.ToImmutable(),
                checked((int)genArity),
                isStatic);
        }
        private MethodSymbol? FindMethodBySignature(
            NamedTypeSymbol ownerType,
            string name,
            TypeSymbol returnType,
            ImmutableArray<TypeSymbol> parameterTypes,
            int genericArity,
            bool isStatic)
        {
            var seen = new HashSet<NamedTypeSymbol>(ReferenceEqualityComparer<NamedTypeSymbol>.Instance);
            var stack = new Stack<NamedTypeSymbol>();
            stack.Push(ownerType);

            while (stack.Count != 0)
            {
                var type = stack.Pop();
                if (!seen.Add(type))
                    continue;

                // A MemberRef carries its member's definition signature, so it matches against the definition, not the instantiation
                var members = type.OriginalDefinition.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is not MethodSymbol m)
                        continue;

                    if (!StringComparer.Ordinal.Equals(m.Name, name))
                        continue;

                    if (m.IsStatic != isStatic)
                        continue;

                    if (m.TypeParameters.Length != genericArity)
                        continue;

                    var ps = m.Parameters;
                    if (ps.Length != parameterTypes.Length)
                        continue;

                    bool same = AreEquivalentSignatureType(m.ReturnType, returnType);
                    if (!same)
                        continue;

                    for (int p = 0; p < ps.Length; p++)
                    {
                        if (!AreEquivalentSignatureType(ps[p].Type, parameterTypes[p]))
                        {
                            same = false;
                            break;
                        }
                    }

                    if (same)
                        return ReferenceEquals(type, type.OriginalDefinition) ? m : FindConstructedMember(type, m);
                }

                if (type.TypeKind == TypeKind.Interface)
                {
                    var ifaces = type.Interfaces;
                    for (int i = 0; i < ifaces.Length; i++)
                    {
                        if (ifaces[i] is NamedTypeSymbol iface)
                            stack.Push(iface);
                    }
                }
                else if (type.BaseType is NamedTypeSymbol bt)
                {
                    stack.Push(bt);
                }
            }

            return null;
        }
        private static MethodSymbol? FindConstructedMember(NamedTypeSymbol constructedType, MethodSymbol definition)
        {
            var members = constructedType.GetMembers();
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is MethodSymbol m && ReferenceEquals(m.OriginalDefinition, definition))
                    return m;
            }
            return null;
        }
        private static bool AreEquivalentSignatureType(TypeSymbol a, TypeSymbol b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (a.SpecialType != SpecialType.None || b.SpecialType != SpecialType.None)
                return a.SpecialType == b.SpecialType;

            if (a is ArrayTypeSymbol aa && b is ArrayTypeSymbol ab)
                return aa.Rank == ab.Rank && aa.IsSZArray == ab.IsSZArray && AreEquivalentSignatureType(aa.ElementType, ab.ElementType);

            if (a is PointerTypeSymbol pa && b is PointerTypeSymbol pb)
                return AreEquivalentSignatureType(pa.PointedAtType, pb.PointedAtType);

            if (a is ByRefTypeSymbol ra && b is ByRefTypeSymbol rb)
                return AreEquivalentSignatureType(ra.ElementType, rb.ElementType);

            if (a is NamedTypeSymbol na && b is NamedTypeSymbol nb)
            {
                if (!ReferenceEquals(na.OriginalDefinition, nb.OriginalDefinition))
                    return false;

                var aa2 = na.TypeArguments;
                var bb2 = nb.TypeArguments;
                if (aa2.Length != bb2.Length)
                    return false;

                for (int i = 0; i < aa2.Length; i++)
                {
                    if (!AreEquivalentSignatureType(aa2[i], bb2[i]))
                        return false;
                }

                return true;
            }

            if (a is TypeParameterSymbol ta && b is TypeParameterSymbol tb)
            {
                if (ta.Ordinal != tb.Ordinal)
                    return false;

                static int OwnerKind(TypeParameterSymbol tp) => tp.ContainingSymbol switch
                {
                    MethodSymbol => 2,
                    NamedTypeSymbol => 1,
                    _ => 0
                };

                int ak = OwnerKind(ta);
                int bk = OwnerKind(tb);
                return ak == bk || ak == 0 || bk == 0;
            }

            return false;
        }
        private void ApplyTypeLevelCustomAttributes(CoreLibraryBuilder core, NamedTypeSymbol[] typeByRid)
        {
            int count = _md.GetRowCount(MetadataTableKind.CustomAttribute);
            for (int rid = 1; rid <= count; rid++)
            {
                var row = _md.GetCustomAttribute(rid);
                int table = MetadataToken.Table(row.ParentToken);
                if (table is MetadataToken.TypeDef or MetadataToken.FieldDef ||
                    table == MetadataToken.GenericParam &&
                    MetadataToken.Table(_md.GetGenericParam(MetadataToken.Rid(row.ParentToken)).OwnerToken) == MetadataToken.TypeDef)
                {
                    ApplyCustomAttribute(core, typeByRid, row);
                }
            }
        }
        private void ApplyCustomAttributesOf(CoreLibraryBuilder core, NamedTypeSymbol[] typeByRid, int parentToken)
        {
            var (start, end) = _md.GetCustomAttributeRange(parentToken);
            for (int rid = start; rid < end; rid++)
                ApplyCustomAttribute(core, typeByRid, _md.GetCustomAttribute(rid));
        }
        private void ApplyCustomAttribute(CoreLibraryBuilder core, NamedTypeSymbol[] typeByRid, CustomAttributeRow row)
        {
            var (owner, target) = ResolveAttributeOwner(row.ParentToken, typeByRid, _fieldByRid, _methodByRid, _paramByRid, _propertyByRid);
            if (owner is null)
                return;

            var data = DecodeCustomAttribute(core, typeByRid, _methodByRid, row, target);
            if (data is not null)
                AddImportedAttribute(owner, data);
        }
        private (Symbol? Owner, AttributeApplicationTarget Target) ResolveAttributeOwner(
            int parentToken,
            NamedTypeSymbol[] typeByRid,
            Dictionary<int, FieldSymbol> fieldByRid,
            Dictionary<int, MethodSymbol> methodByRid,
            Dictionary<int, ParameterSymbol> paramByRid,
            Dictionary<int, PropertySymbol> propertyByRid)
        {
            int table = MetadataToken.Table(parentToken);
            int rid = MetadataToken.Rid(parentToken);

            switch (table)
            {
                case MetadataToken.TypeDef:
                    return ((rid > 0 && rid < typeByRid.Length) ? typeByRid[rid] : null, AttributeApplicationTarget.Default);
                case MetadataToken.FieldDef:
                    return (fieldByRid.TryGetValue(rid, out var f) ? f : null, AttributeApplicationTarget.Default);
                case MetadataToken.MethodDef:
                    return (methodByRid.TryGetValue(rid, out var m) ? m : null, AttributeApplicationTarget.Default);
                case MetadataToken.PropertyDef:
                    return (propertyByRid.TryGetValue(rid, out var prop) ? prop : null, AttributeApplicationTarget.Default);
                case MetadataToken.ParamDef:
                    if (paramByRid.TryGetValue(rid, out var p))
                        return (p, AttributeApplicationTarget.Default);
                    if (_md.GetParam(rid).Sequence == 0 &&
                        methodByRid.TryGetValue(_md.GetParamOwnerMethodRid(rid), out var returnOwner))
                    {
                        return (returnOwner, AttributeApplicationTarget.ReturnValue);
                    }
                    return (null, AttributeApplicationTarget.Default);
                case MetadataToken.GenericParam:
                    return (ResolveGenericParameter(rid, typeByRid, methodByRid), AttributeApplicationTarget.Default);
                default:
                    return (null, AttributeApplicationTarget.Default);
            }
        }
        private TypeParameterSymbol? ResolveGenericParameter(int rid, NamedTypeSymbol[] typeByRid, Dictionary<int, MethodSymbol> methodByRid)
        {
            var row = _md.GetGenericParam(rid);
            int ownerRid = MetadataToken.Rid(row.OwnerToken);
            ImmutableArray<TypeParameterSymbol> parameters = MetadataToken.Table(row.OwnerToken) switch
            {
                MetadataToken.TypeDef when ownerRid < typeByRid.Length && typeByRid[ownerRid] is { } type => GetTypeParametersInMetadataOrder(type),
                MetadataToken.MethodDef when methodByRid.TryGetValue(ownerRid, out var method) => method.TypeParameters,
                _ => ImmutableArray<TypeParameterSymbol>.Empty,
            };
            return row.Number < parameters.Length ? parameters[row.Number] : null;
        }

        private static void AddImportedAttribute(Symbol owner, AttributeData data)
        {
            switch (owner)
            {
                case SourceNamedTypeSymbol t: t.AddAttribute(data); break;
                case SourceMethodSymbol m: m.AddAttribute(data); break;
                case SourceFieldSymbol f: f.AddAttribute(data); break;
                case SourcePropertySymbol p: p.AddAttribute(data); break;

                case ExternalMethodSymbol m: m.AddAttribute(data); break;
                case ExternalFieldSymbol f: f.AddAttribute(data); break;
                case ExternalPropertySymbol p: p.AddAttribute(data); break;

                case ParameterSymbol p:
                    if (MethodAttributeFacts.IsAttribute(data, "System", "ParamArrayAttribute") ||
                        MethodAttributeFacts.IsAttribute(data, "System.Runtime.CompilerServices", "ParamCollectionAttribute"))
                    {
                        p.IsParams = true;
                    }
                    else
                    {
                        p.AddAttribute(data);
                    }
                    break;
                case TypeParameterSymbol tp: tp.AddAttribute(data); break;
            }
        }
        private AttributeData? DecodeCustomAttribute(
            CoreLibraryBuilder core,
            NamedTypeSymbol[] typeByRid,
            Dictionary<int, MethodSymbol> methodByRid,
            CustomAttributeRow row,
            AttributeApplicationTarget target)
        {
            var ctor = ResolveMethodToken(core, typeByRid, methodByRid, row.ConstructorToken);
            if (ctor is null || ctor.ContainingSymbol is not NamedTypeSymbol attrTypeSym)
                return null;

            var r = new CustomAttributeBlobReader(_md.GetBlob(row.Value));
            var ctorParams = ctor.Parameters;
            var ctorArgs = ImmutableArray.CreateBuilder<TypedConstant>(ctorParams.Length);
            for (int i = 0; i < ctorParams.Length; i++)
                ctorArgs.Add(ReadAttributeValue(core, ref r, ctorParams[i].Type));

            int namedCount = r.ReadUInt16();
            var namedArgs = ImmutableArray.CreateBuilder<AttributeNamedArgumentData>(namedCount);
            for (int i = 0; i < namedCount; i++)
            {
                byte memberKind = r.ReadByte();
                var memberType = ReadAttributeMemberType(core, ref r);
                string memberName = r.ReadSerString() ?? string.Empty;
                var value = ReadAttributeValue(core, ref r, memberType);

                var member = FindAttributeNamedMember(attrTypeSym, memberKind == CustomAttributeBlob.NamedField ? (byte)1 : (byte)2, memberName);
                if (member is not null)
                    namedArgs.Add(new AttributeNamedArgumentData(memberName, member, value));
            }

            return new AttributeData(
                attributeClass: attrTypeSym,
                constructor: ctor,
                constructorArguments: ctorArgs.ToImmutable(),
                namedArguments: namedArgs.ToImmutable(),
                target: target);
        }
        private TypedConstant ReadAttributeValue(CoreLibraryBuilder core, ref CustomAttributeBlobReader r, TypeSymbol type)
        {
            if (type is ArrayTypeSymbol array)
            {
                uint length = r.ReadUInt32();
                if (length == uint.MaxValue)
                    return new TypedConstant(type, null);
                var elements = ImmutableArray.CreateBuilder<TypedConstant>((int)length);
                for (uint i = 0; i < length; i++)
                    elements.Add(ReadAttributeValue(core, ref r, array.ElementType));
                return new TypedConstant(type, elements.ToImmutable());
            }
            if (type.SpecialType == SpecialType.System_Object)
                return ReadAttributeValue(core, ref r, ReadAttributeMemberType(core, ref r));
            if (IsSystemType(type))
            {
                string? name = r.ReadSerString();
                return new TypedConstant(type, name is null ? null : ResolveSerializedTypeName(core, name));
            }

            var valueType = type is NamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
                ? enumType.EnumUnderlyingType ?? core.GetSpecialType(SpecialType.System_Int32)
                : type;
            if (valueType.SpecialType == SpecialType.System_String)
                return new TypedConstant(type, r.ReadSerString());

            SigElementType element = valueType.SpecialType switch
            {
                SpecialType.System_Boolean => SigElementType.BOOLEAN,
                SpecialType.System_Char => SigElementType.CHAR,
                SpecialType.System_Int8 => SigElementType.I1,
                SpecialType.System_UInt8 => SigElementType.U1,
                SpecialType.System_Int16 => SigElementType.I2,
                SpecialType.System_UInt16 => SigElementType.U2,
                SpecialType.System_Int32 => SigElementType.I4,
                SpecialType.System_UInt32 => SigElementType.U4,
                SpecialType.System_Int64 => SigElementType.I8,
                SpecialType.System_UInt64 => SigElementType.U8,
                SpecialType.System_Single => SigElementType.R4,
                SpecialType.System_Double => SigElementType.R8,
                _ => throw new BadImageFormatException($"Attribute argument type '{type.Name}' cannot be decoded."),
            };
            return new TypedConstant(type, r.ReadPrimitive(element));
        }
        private TypeSymbol ReadAttributeMemberType(CoreLibraryBuilder core, ref CustomAttributeBlobReader r)
        {
            byte tag = r.ReadByte();
            switch (tag)
            {
                case (byte)SigElementType.SZARRAY:
                    return core.CreateArrayType(ReadAttributeMemberType(core, ref r), rank: 1, isSZArray: true);
                case CustomAttributeBlob.TypeTag:
                    return ResolveSerializedTypeName(core, "System.Type");
                case CustomAttributeBlob.BoxedTag:
                    return core.GetSpecialType(SpecialType.System_Object);
                case CustomAttributeBlob.EnumTag:
                    return ResolveSerializedTypeName(core, r.ReadSerString() ?? string.Empty);
            }
            SpecialType special = (SigElementType)tag switch
            {
                SigElementType.BOOLEAN => SpecialType.System_Boolean,
                SigElementType.CHAR => SpecialType.System_Char,
                SigElementType.I1 => SpecialType.System_Int8,
                SigElementType.U1 => SpecialType.System_UInt8,
                SigElementType.I2 => SpecialType.System_Int16,
                SigElementType.U2 => SpecialType.System_UInt16,
                SigElementType.I4 => SpecialType.System_Int32,
                SigElementType.U4 => SpecialType.System_UInt32,
                SigElementType.I8 => SpecialType.System_Int64,
                SigElementType.U8 => SpecialType.System_UInt64,
                SigElementType.R4 => SpecialType.System_Single,
                SigElementType.R8 => SpecialType.System_Double,
                SigElementType.STRING => SpecialType.System_String,
                _ => throw new BadImageFormatException($"Invalid custom attribute element type 0x{tag:X2}."),
            };
            return core.GetSpecialType(special);
        }
        private static bool IsSystemType(TypeSymbol type)
            => type is NamedTypeSymbol { Name: "Type", Arity: 0 } named &&
               named.ContainingSymbol is NamespaceSymbol { Name: "System" } ns &&
               ns.ContainingSymbol is NamespaceSymbol { IsGlobalNamespace: true };
        private TypeSymbol ResolveSerializedTypeName(CoreLibraryBuilder core, string serialized)
        {
            int position = 0;
            var type = ParseSerializedTypeName(core, serialized, ref position);
            return type;
        }
        private TypeSymbol ParseSerializedTypeName(CoreLibraryBuilder core, string text, ref int position)
        {
            int start = position;
            while (position < text.Length && text[position] is not ('[' or ']' or ','))
                position++;
            string fullName = text[start..position];

            string[] nestedParts = fullName.Split('+');
            string outer = nestedParts[0];
            int dot = outer.LastIndexOf('.');
            string ns = dot < 0 ? string.Empty : outer[..dot];
            var (outerName, outerArity) = SplitArity(dot < 0 ? outer : outer[(dot + 1)..]);

            NamedTypeSymbol? current = null;
            var nsSym = TryGetNamespace(core.GlobalNamespace, ns);
            if (nsSym is not null)
            {
                var found = nsSym.GetTypeMembers(outerName, outerArity);
                if (found.Length != 0)
                    current = found[0];
            }
            for (int i = 1; i < nestedParts.Length && current is not null; i++)
            {
                var (nestedName, nestedArity) = SplitArity(nestedParts[i]);
                var nested = current.GetTypeMembers(nestedName, nestedArity);
                current = nested.Length != 0 ? nested[0] : null;
            }
            TypeSymbol result = current ?? (TypeSymbol)new ErrorTypeSymbol($"missing-type:{fullName}", null, ImmutableArray<Location>.Empty);

            if (position + 1 < text.Length && text[position] == '[' && text[position + 1] == '[')
            {
                var args = ImmutableArray.CreateBuilder<TypeSymbol>();
                position++;
                while (position < text.Length && text[position] == '[')
                {
                    position++;
                    args.Add(ParseSerializedTypeName(core, text, ref position));
                    SkipAssemblyQualifier(text, ref position);
                    position++;
                    if (position < text.Length && text[position] == ',')
                        position++;
                }
                position++;
                if (current is not null)
                    result = core.ConstructNamedType(current, args.ToImmutable());
            }
            while (position < text.Length && text[position] == '[')
            {
                int rank = 1;
                position++;
                while (position < text.Length && text[position] == ',')
                {
                    rank++;
                    position++;
                }
                position++;
                result = core.CreateArrayType(result, rank, isSZArray: rank == 1);
            }
            return result;
        }
        private static void SkipAssemblyQualifier(string text, ref int position)
        {
            int depth = 0;
            while (position < text.Length)
            {
                char c = text[position];
                if (c == '[')
                    depth++;
                else if (c == ']')
                {
                    if (depth == 0)
                        return;
                    depth--;
                }
                position++;
            }
        }
        private TypeSymbol ResolveTypeToken(CoreLibraryBuilder core, NamedTypeSymbol[] typeByRid, int token, NamedTypeSymbol? genericContext = null)
        {
            int table = MetadataToken.Table(token);
            int rid = MetadataToken.Rid(token);

            return table switch
            {
                MetadataToken.TypeDef => (rid > 0 && rid < typeByRid.Length && typeByRid[rid] is not null)
                    ? typeByRid[rid]
                    : new ErrorTypeSymbol($"typedef:{rid}", null, ImmutableArray<Location>.Empty),

                MetadataToken.TypeRef => ResolveTypeRef(rid, core),
                MetadataToken.TypeSpec => ResolveTypeSpec(rid, typeByRid, core, genericContext, ImmutableArray<TypeParameterSymbol>.Empty),

                _ => new ErrorTypeSymbol($"bad-type-token:0x{token:X8}", null, ImmutableArray<Location>.Empty)
            };
        }

        private Symbol? FindAttributeNamedMember(NamedTypeSymbol attrType, byte memberKind, string name)
        {
            for (NamedTypeSymbol? t = attrType; t is not null; t = t.BaseType as NamedTypeSymbol)
            {
                var members = t.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    var m = members[i];
                    if (!StringComparer.Ordinal.Equals(m.Name, name))
                        continue;

                    if (memberKind == 1 && m is FieldSymbol f && !f.IsStatic && !f.IsConst)
                        return f;

                    if (memberKind == 2 && m is PropertySymbol p && !p.IsStatic && p.Parameters.Length == 0)
                        return p;
                }
            }

            return null;
        }

        private static bool AreSameType(TypeSymbol a, TypeSymbol b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (a.SpecialType != SpecialType.None || b.SpecialType != SpecialType.None)
                return a.SpecialType == b.SpecialType;

            if (a is ArrayTypeSymbol aa && b is ArrayTypeSymbol ab)
                return aa.Rank == ab.Rank && aa.IsSZArray == ab.IsSZArray && AreSameType(aa.ElementType, ab.ElementType);

            if (a is PointerTypeSymbol pa && b is PointerTypeSymbol pb)
                return AreSameType(pa.PointedAtType, pb.PointedAtType);

            if (a is ByRefTypeSymbol ra && b is ByRefTypeSymbol rb)
                return AreSameType(ra.ElementType, rb.ElementType);

            if (a is NamedTypeSymbol na && b is NamedTypeSymbol nb)
            {
                if (!ReferenceEquals(na.OriginalDefinition, nb.OriginalDefinition))
                    return false;

                var aa2 = na.TypeArguments;
                var bb2 = nb.TypeArguments;
                if (aa2.Length != bb2.Length)
                    return false;

                for (int i = 0; i < aa2.Length; i++)
                    if (!AreSameType(aa2[i], bb2[i]))
                        return false;

                return true;
            }

            if (a is TypeParameterSymbol ta && b is TypeParameterSymbol tb)
                return ta.Ordinal == tb.Ordinal && ReferenceEquals(ta.ContainingSymbol, tb.ContainingSymbol);

            return false;
        }
        private Dictionary<int, string?[]>? _tupleElementNames;

        // TupleElementNamesAttribute arguments by parent token.
        private Dictionary<int, string?[]> CollectTupleElementNames()
        {
            var result = new Dictionary<int, string?[]>();
            for (int rid = 1; rid <= _md.GetRowCount(MetadataTableKind.CustomAttribute); rid++)
            {
                var row = _md.GetCustomAttribute(rid);
                if (!_md.IsAttribute(row.ConstructorToken, "System.Runtime.CompilerServices", "TupleElementNamesAttribute"))
                    continue;
                var reader = new CustomAttributeBlobReader(_md.GetBlob(row.Value));
                uint count = reader.ReadUInt32();
                if (count == uint.MaxValue)
                    continue;
                var names = new string?[count];
                for (int i = 0; i < names.Length; i++)
                    names[i] = reader.ReadSerString();
                result[row.ParentToken] = names;
            }
            return result;
        }
        private TypeSymbol ApplyTupleElementNames(CoreLibraryBuilder core, TypeSymbol type, int parentToken)
        {
            _tupleElementNames ??= CollectTupleElementNames();
            if (!_tupleElementNames.TryGetValue(parentToken, out var names))
                return type;
            int index = 0;
            return Apply(type, ref index);

            TypeSymbol Apply(TypeSymbol type, ref int index)
            {
                switch (type)
                {
                    case ArrayTypeSymbol array:
                        {
                            var element = Apply(array.ElementType, ref index);
                            return ReferenceEquals(element, array.ElementType) ? type : core.CreateArrayType(element, array.Rank, array.IsSZArray);
                        }
                    case ByRefTypeSymbol byRef:
                        {
                            var element = Apply(byRef.ElementType, ref index);
                            return ReferenceEquals(element, byRef.ElementType) ? type : core.CreateByRefType(element);
                        }
                    case PointerTypeSymbol pointer:
                        {
                            var element = Apply(pointer.PointedAtType, ref index);
                            return ReferenceEquals(element, pointer.PointedAtType) ? type : core.CreatePointerType(element);
                        }
                    case NamedTypeSymbol named when named.ContainingSymbol is not NamedTypeSymbol && !named.TypeArguments.IsDefaultOrEmpty:
                        {
                            bool isTuple = MetadataTokenProvider.IsValueTupleType(named) && named.TypeArguments.Length <= 7;
                            var elementNames = ImmutableArray<string?>.Empty;
                            if (isTuple)
                            {
                                var nb = ImmutableArray.CreateBuilder<string?>(named.TypeArguments.Length);
                                for (int i = 0; i < named.TypeArguments.Length; i++)
                                    nb.Add(index < names.Length ? names[index++] : null);
                                elementNames = nb.MoveToImmutable();
                            }
                            var args = ImmutableArray.CreateBuilder<TypeSymbol>(named.TypeArguments.Length);
                            bool changed = false;
                            foreach (var argument in named.TypeArguments)
                            {
                                var applied = Apply(argument, ref index);
                                changed |= !ReferenceEquals(applied, argument);
                                args.Add(applied);
                            }
                            if (isTuple)
                                return core.GetTupleType(args.MoveToImmutable(), elementNames);
                            return changed ? core.ConstructNamedType(named.OriginalDefinition, args.MoveToImmutable()) : type;
                        }
                    default:
                        return type;
                }
            }
        }
        private void CollectExtensionMethods(NamedTypeSymbol[] typeByRid)
        {
            for (int rid = 1; rid <= _md.GetRowCount(MetadataTableKind.CustomAttribute); rid++)
            {
                var row = _md.GetCustomAttribute(rid);
                if (MetadataToken.Table(row.ParentToken) != MetadataToken.MethodDef)
                    continue;
                bool isExtension = _md.IsAttribute(row.ConstructorToken, "System.Runtime.CompilerServices", "ExtensionAttribute");
                if (!isExtension && !_md.IsAttribute(row.ConstructorToken, "System.Runtime.CompilerServices", "ExtensionMarkerAttribute"))
                    continue;

                int methodRid = MetadataToken.Rid(row.ParentToken);
                if (isExtension)
                    _extensionMethodRids.Add(methodRid);
                if (typeByRid[_md.GetMethodOwnerTypeDefRid(methodRid)] is SourceNamedTypeSymbol container)
                    container.MarkMayContainExtensionMembers(inBlock: !isExtension);
            }
        }
        private void AddMethodsOfType(CoreLibraryBuilder core, NamedTypeSymbol[] typeByRid, int rid)
        {
            var declaringType = typeByRid[rid];
            if (declaringType is null)
                return;

            var methodByRid = _methodByRid;
            var constByParent = _constByParent;
            var extensionMethodRids = _extensionMethodRids;
            var (start, end) = _md.GetMethodRange(rid);
            if (start <= 0 || end < start)
                return;

            for (int mrid = start; mrid < end; mrid++)
            {
                var mdRow = _md.GetMethodDef(mrid);
                var mname = _md.GetString(mdRow.Name);
                var sig = _md.GetBlob(mdRow.Signature);

                var reader = new SigReader(sig);

                byte cc = reader.ReadByte();

                uint genArity = 0;
                if ((cc & 0x10) != 0)
                    genArity = reader.ReadCompressedUInt();
                uint paramCount = reader.ReadCompressedUInt();

                bool hasThis = (cc & 0x20) != 0;
                bool isStatic = !hasThis;
                ImmutableArray<TypeParameterSymbol> mtps = ImmutableArray<TypeParameterSymbol>.Empty;
                if (genArity != 0)
                {
                    var b = ImmutableArray.CreateBuilder<TypeParameterSymbol>((int)genArity);
                    for (int i = 0; i < (int)genArity; i++)
                    {
                        string tpName = (i == 0) ? "T" : $"T{i}";
                        b.Add(new TypeParameterSymbol(tpName, containing: null, ordinal: i, locations: ImmutableArray<Location>.Empty));
                    }
                    mtps = b.ToImmutable();
                }
                var retType = ReadMethodReturnType(
                    core,
                    typeByRid,
                    ref reader,
                    declaringType,
                    mtps,
                    out bool returnsByRefReadonly);
                int returnParamRid = _md.FindParamRid(mrid, 0);
                if (returnParamRid != 0)
                    retType = ApplyTupleElementNames(core, retType, MetadataToken.Make(MetadataToken.ParamDef, returnParamRid));

                var ps = ImmutableArray.CreateBuilder<(string name, TypeSymbol type)>((int)paramCount);
                for (int i = 0; i < paramCount; i++)
                {
                    var pt = ReadType(core, typeByRid, ref reader, declaringType, mtps);
                    string paramName = $"arg{i}";
                    int prid = _md.FindParamRid(mrid, i + 1);
                    if (prid != 0)
                    {
                        var decodedName = _md.GetString(_md.GetParam(prid).Name);
                        if (!string.IsNullOrEmpty(decodedName))
                            paramName = decodedName;
                        pt = ApplyTupleElementNames(core, pt, MetadataToken.Make(MetadataToken.ParamDef, prid));
                    }
                    ps.Add((paramName, pt));
                }

                bool isCtor = mname == ".ctor";
                if (isCtor)
                    mname = declaringType.Name;

                bool isVirtual = (mdRow.Flags & (ushort)System.Reflection.MethodAttributes.Virtual) != 0;
                bool isAbstract = (mdRow.Flags & (ushort)System.Reflection.MethodAttributes.Abstract) != 0;
                bool isNewSlot = (mdRow.Flags & (ushort)System.Reflection.MethodAttributes.NewSlot) != 0;
                bool isFinal = (mdRow.Flags & (ushort)System.Reflection.MethodAttributes.Final) != 0;
                // virtual final newslot is how a non-virtual C# method implementing an interface is encoded.
                if (isVirtual && isNewSlot && isFinal && !isAbstract)
                    isVirtual = isFinal = false;
                bool isOverride = isVirtual && !isNewSlot;
                bool isSealed = isFinal;
                bool isExtensionMethod = extensionMethodRids.Contains(mrid);

                var ms = core.AddExternalMethod(
                    containingType: declaringType,
                    name: mname,
                    returnType: retType,
                    isStatic: isStatic,
                    isConstructor: isCtor,
                    parameters: ps.ToImmutable(),
                    declaredAccessibility: DecodeMethodAccessibility(mdRow.Flags),
                    isVirtual: isVirtual,
                    isAbstract: isAbstract,
                    isOverride: isOverride,
                    isSealed: isSealed,
                    isExtensionMethod: isExtensionMethod,
                    typeParameters: mtps,
                    isExtern: (mdRow.ImplFlags & MetadataFlagBits.Extern) != 0 ||
                              (mdRow.Flags & (ushort)System.Reflection.MethodAttributes.PinvokeImpl) != 0,
                    returnsByRefReadonly: returnsByRefReadonly);

                ApplyParamRefKinds(ms, mrid);
                ApplyParamDefaultValues(ms, mrid, constByParent);
                methodByRid[mrid] = ms;
            }
        }

        private Dictionary<int, FieldSymbol> AddFields(CoreLibraryBuilder core, NamedTypeSymbol[] typeByRid)
        {
            var fieldByRid = new Dictionary<int, FieldSymbol>();
            var constByParent = _constByParent;
            for (int rid = 1; rid <= _md.GetRowCount(MetadataTableKind.TypeDef); rid++)
            {
                var declaringType = typeByRid[rid];
                if (declaringType is null)
                    continue;

                var td = _md.GetTypeDef(rid);

                int start = td.FieldList; // 1-based
                int end = (rid == _md.GetRowCount(MetadataTableKind.TypeDef))
                    ? (_md.GetRowCount(MetadataTableKind.Field) + 1)
                    : _md.GetTypeDef(rid + 1).FieldList;

                if (start <= 0 || end < start)
                    continue;

                for (int frid = start; frid < end; frid++)
                {
                    var frow = _md.GetField(frid);
                    var fname = _md.GetString(frow.Name);
                    var sig = _md.GetBlob(frow.Signature);

                    var reader = new SigReader(sig);
                    byte kind = reader.ReadByte(); // 0x06 FIELD
                    if (kind != 0x06)
                        continue;

                    var ftype = ReadType(core, typeByRid, ref reader, declaringType, ImmutableArray<TypeParameterSymbol>.Empty);
                    if ((frow.Flags & (ushort)System.Reflection.FieldAttributes.RTSpecialName) != 0 && fname == "value__")
                    {
                        if (declaringType is SourceNamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
                            enumType.SetEnumUnderlyingType(ftype);
                        continue;
                    }

                    bool isStatic = (frow.Flags & 0x0010) != 0; // FieldAttributes.Static
                    bool isConst = (frow.Flags & 0x0040) != 0; // FieldAttributes.Literal
                    Optional<object> cval = default;
                    if (isConst)
                    {
                        int parentTok = MetadataToken.Make(MetadataToken.FieldDef, frid);
                        if (constByParent.TryGetValue(parentTok, out var crow))
                            cval = DecodeConstant(ftype, crow);
                    }
                    ftype = ApplyTupleElementNames(core, ftype, MetadataToken.Make(MetadataToken.FieldDef, frid));
                    var field = core.AddExternalField(
                        declaringType,
                        fname,
                        ftype,
                        isStatic,
                        isConst,
                        declaredAccessibility: DecodeFieldAccessibility(frow.Flags),
                        cval);

                    fieldByRid[frid] = field;
                }
            }
            return fieldByRid;
        }
        private Optional<object> DecodeConstant(TypeSymbol fieldType, ConstantRow row)
        {
            var blob = _md.GetBlob(row.Value);
            if (row.TypeCode == (byte)SigElementType.CLASS)
                return new Optional<object>(null!);

            switch (row.TypeCode)
            {
                case 0x02: return new Optional<object>(blob[0] != 0);                // Boolean
                case 0x03: return new Optional<object>(BitConverter.ToChar(blob));    // Char
                case 0x04: return new Optional<object>(unchecked((sbyte)blob[0]));    // I1
                case 0x05: return new Optional<object>(blob[0]);                      // U1
                case 0x06: return new Optional<object>(BitConverter.ToInt16(blob));   // I2
                case 0x07: return new Optional<object>(BitConverter.ToUInt16(blob));  // U2
                case 0x08: return new Optional<object>(BitConverter.ToInt32(blob));   // I4
                case 0x09: return new Optional<object>(BitConverter.ToUInt32(blob));  // U4
                case 0x0A: return new Optional<object>(BitConverter.ToInt64(blob));   // I8
                case 0x0B: return new Optional<object>(BitConverter.ToUInt64(blob));  // U8
                case 0x0C: return new Optional<object>(BitConverter.ToSingle(blob));  // R4
                case 0x0D: return new Optional<object>(BitConverter.ToDouble(blob));  // R8
                case 0x0E: return new Optional<object>(Encoding.Unicode.GetString(blob)); // String

                default: return Optional<object>.None;
            }
        }
        private void AddPropertiesFromTableOfType(CoreLibraryBuilder core, NamedTypeSymbol[] typeByRid, int typeRid)
        {
            if (!_propertyRowsByType.TryGetValue(typeRid, out var rows))
                return;

            var methodByRid = _methodByRid;
            var propertyByRid = _propertyByRid;

            // Avoid duplicates
            var existingNames = new Dictionary<NamedTypeSymbol, HashSet<string>>();

            HashSet<string> GetOrCreateExisting(NamedTypeSymbol t)
            {
                if (existingNames.TryGetValue(t, out var set))
                    return set;

                set = new HashSet<string>(StringComparer.Ordinal);
                var ms = t.GetMembers();
                for (int i = 0; i < ms.Length; i++)
                {
                    if (ms[i] is PropertySymbol p)
                        set.Add(p.Name);
                }

                existingNames.Add(t, set);
                return set;
            }

            foreach (int prid in rows)
            {
                var prow = _md.GetProperty(prid);
                var pname = _md.GetString(prow.Name);
                if (string.IsNullOrEmpty(pname))
                    continue;

                MethodSymbol? get = null;
                MethodSymbol? set = null;

                if (prow.GetMethod != 0 && MetadataToken.Table(prow.GetMethod) == MetadataToken.MethodDef)
                {
                    int mrid = MetadataToken.Rid(prow.GetMethod);
                    methodByRid.TryGetValue(mrid, out get);
                }
                if (prow.SetMethod != 0 && MetadataToken.Table(prow.SetMethod) == MetadataToken.MethodDef)
                {
                    int mrid = MetadataToken.Rid(prow.SetMethod);
                    methodByRid.TryGetValue(mrid, out set);
                }

                if (get is null && set is null)
                    continue;

                var declaring = (get?.ContainingSymbol ?? set?.ContainingSymbol) as NamedTypeSymbol;
                if (declaring is null)
                    continue;

                if (get is not null && set is not null)
                {
                    if (!ReferenceEquals(get.ContainingSymbol, set.ContainingSymbol))
                        continue;
                    if (get.IsStatic != set.IsStatic)
                        continue;
                }

                var existing = GetOrCreateExisting(declaring);
                if (existing.Contains(pname))
                    continue;

                bool isStatic = get?.IsStatic ?? set!.IsStatic;

                TypeSymbol propType;
                ImmutableArray<ParameterSymbol> propParameters = ImmutableArray<ParameterSymbol>.Empty;

                var sig = _md.GetBlob(prow.Signature);
                if (sig.Length != 0) // indexer
                {
                    var r = new SigReader(sig);
                    _ = r.ReadByte(); // calling convention
                    uint paramCount = r.ReadCompressedUInt();

                    propType = ReadType(core, typeByRid, ref r, declaring, ImmutableArray<TypeParameterSymbol>.Empty);

                    if (paramCount != 0)
                    {
                        var pb = ImmutableArray.CreateBuilder<ParameterSymbol>((int)paramCount);
                        for (int pi = 0; pi < paramCount; pi++)
                        {
                            var pType = ReadType(core, typeByRid, ref r, declaring, ImmutableArray<TypeParameterSymbol>.Empty);
                            pb.Add(new ParameterSymbol(
                                name: $"p{pi}",
                                containing: declaring,
                                type: pType,
                                locations: ImmutableArray<Location>.Empty));
                        }
                        propParameters = pb.ToImmutable();
                    }
                }
                else
                {
                    propType = get?.ReturnType ?? set!.Parameters[0].Type;
                }

                var prop = core.AddExternalProperty(
                    containingType: declaring,
                    name: pname,
                    type: propType,
                    isStatic: isStatic,
                    declaredAccessibility: DerivePropertyAccessibility(get, set),
                    getMethod: get,
                    setMethod: set,
                    parameters: propParameters);

                propertyByRid[prid] = prop;
                existing.Add(pname);
            }
        }
        private void MapParameters(int methodRid)
        {
            if (!_methodByRid.TryGetValue(methodRid, out var method))
                return;
            var ps = method.Parameters;
            for (int i = 0; i < ps.Length; i++)
            {
                int prid = _md.FindParamRid(methodRid, i + 1);
                if (prid != 0)
                    _paramByRid[prid] = ps[i];
            }
        }
        private void AddPropertiesFromAccessorsOfType(CoreLibraryBuilder core, NamedTypeSymbol? t)
        {
            if (t is null)
                return;

            // avoid duplicates if already present
            var existingProps = t.GetMembers().OfType<PropertySymbol>().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

            var dict = new Dictionary<string, (MethodSymbol? get, MethodSymbol? set)>(StringComparer.Ordinal);

            foreach (var m in t.GetMembers().OfType<MethodSymbol>())
            {
                if (m.Name.StartsWith("get_", StringComparison.Ordinal) &&
                    m.Parameters.Length == 0 &&
                    m.ReturnType.SpecialType != SpecialType.System_Void)
                {
                    var pn = m.Name.Substring(4);
                    dict.TryGetValue(pn, out var pair);
                    pair.get = m;
                    dict[pn] = pair;
                }
                else if (m.Name.StartsWith("set_", StringComparison.Ordinal) &&
                         m.Parameters.Length == 1 &&
                         m.ReturnType.SpecialType == SpecialType.System_Void)
                {
                    var pn = m.Name.Substring(4);
                    dict.TryGetValue(pn, out var pair);
                    pair.set = m;
                    dict[pn] = pair;
                }
            }

            foreach (var kv in dict)
            {
                var name = kv.Key;
                if (existingProps.Contains(name))
                    continue;

                var (get, set) = kv.Value;
                if (get is null && set is null)
                    continue;

                var propType = get?.ReturnType ?? set!.Parameters[0].Type;
                bool isStatic = get?.IsStatic ?? set!.IsStatic;

                // if both exist, ensure static matches
                if (get is not null && set is not null && get.IsStatic != set.IsStatic)
                    continue;

                // no indexers, so parameters empty
                core.AddExternalProperty(
                    containingType: t,
                    name: name,
                    type: propType,
                    isStatic: isStatic,
                    declaredAccessibility: DerivePropertyAccessibility(get, set),
                    getMethod: get,
                    setMethod: set,
                    parameters: ImmutableArray<ParameterSymbol>.Empty);
            }
        }
        private TypeKind InferKind(TypeDefRow td)
        {
            var attrs = (System.Reflection.TypeAttributes)td.Flags;
            if ((attrs & System.Reflection.TypeAttributes.Interface) != 0)
                return TypeKind.Interface;

            int extendsEncoded = td.ExtendsEncoded;

            if (extendsEncoded == 0)
                return TypeKind.Class;

            uint coded = unchecked((uint)extendsEncoded);
            int tag = (int)(coded & 0x3u);
            int rid = (int)(coded >> 2);

            if (rid <= 0)
                return TypeKind.Class;

            string baseNs;
            string baseName;

            switch (tag)
            {
                case 0: // TypeDef
                    {
                        if (rid > _md.GetRowCount(MetadataTableKind.TypeDef))
                            return TypeKind.Class;

                        var typedef = _md.GetTypeDef(rid);
                        baseNs = _md.GetString(typedef.Namespace);
                        baseName = _md.GetString(typedef.Name);
                        break;
                    }

                case 1: // TypeRef
                    {
                        if (rid > _md.GetRowCount(MetadataTableKind.TypeRef))
                            return TypeKind.Class;

                        var tr = _md.GetTypeRef(rid);
                        baseNs = _md.GetString(tr.Namespace);
                        baseName = _md.GetString(tr.Name);
                        break;
                    }

                default: // TypeSpec etc.
                    return TypeKind.Class;
            }

            int tick = baseName.IndexOf('`');
            if (tick >= 0)
                baseName = baseName.Substring(0, tick);

            if (baseNs == "System" && baseName == "ValueType") return TypeKind.Struct;
            if (baseNs == "System" && baseName == "Enum") return TypeKind.Enum;
            if (baseNs == "System" && baseName == "MulticastDelegate") return TypeKind.Delegate;

            return TypeKind.Class;
        }
        private static bool TryMapSpecialType(string ns, string name, out SpecialType st)
        {
            st = SpecialType.None;
            if (!string.Equals(ns, "System", StringComparison.Ordinal))
                return false;

            st = name switch
            {
                "Object" => SpecialType.System_Object,
                "Void" => SpecialType.System_Void,
                "ValueType" => SpecialType.System_ValueType,
                "Enum" => SpecialType.System_Enum,
                "Array" => SpecialType.System_Array,
                "String" => SpecialType.System_String,
                "Exception" => SpecialType.System_Exception,

                "Boolean" => SpecialType.System_Boolean,
                "Char" => SpecialType.System_Char,
                "SByte" => SpecialType.System_Int8,
                "Byte" => SpecialType.System_UInt8,
                "Int16" => SpecialType.System_Int16,
                "UInt16" => SpecialType.System_UInt16,
                "Int32" => SpecialType.System_Int32,
                "UInt32" => SpecialType.System_UInt32,
                "Int64" => SpecialType.System_Int64,
                "UInt64" => SpecialType.System_UInt64,
                "Single" => SpecialType.System_Single,
                "Double" => SpecialType.System_Double,
                "Decimal" => SpecialType.System_Decimal,
                "IntPtr" => SpecialType.System_IntPtr,
                "UIntPtr" => SpecialType.System_UIntPtr,
                _ => SpecialType.None
            };

            return st != SpecialType.None;
        }
        private TypeSymbol ReadType(
            CoreLibraryBuilder core,
            NamedTypeSymbol[] typeByRid,
            ref SigReader reader,
            NamedTypeSymbol? declaringType,
            ImmutableArray<TypeParameterSymbol> methodTypeParameters)
        {
            var et = (SigElementType)reader.ReadByte();

            return et switch
            {
                SigElementType.VOID => core.GetSpecialType(SpecialType.System_Void),
                SigElementType.BOOLEAN => core.GetSpecialType(SpecialType.System_Boolean),
                SigElementType.CHAR => core.GetSpecialType(SpecialType.System_Char),
                SigElementType.I1 => core.GetSpecialType(SpecialType.System_Int8),
                SigElementType.U1 => core.GetSpecialType(SpecialType.System_UInt8),
                SigElementType.I2 => core.GetSpecialType(SpecialType.System_Int16),
                SigElementType.U2 => core.GetSpecialType(SpecialType.System_UInt16),
                SigElementType.I4 => core.GetSpecialType(SpecialType.System_Int32),
                SigElementType.U4 => core.GetSpecialType(SpecialType.System_UInt32),
                SigElementType.I8 => core.GetSpecialType(SpecialType.System_Int64),
                SigElementType.U8 => core.GetSpecialType(SpecialType.System_UInt64),
                SigElementType.I => core.GetSpecialType(SpecialType.System_IntPtr),
                SigElementType.U => core.GetSpecialType(SpecialType.System_UIntPtr),
                SigElementType.R4 => core.GetSpecialType(SpecialType.System_Single),
                SigElementType.R8 => core.GetSpecialType(SpecialType.System_Double),
                SigElementType.STRING => core.GetSpecialType(SpecialType.System_String),
                SigElementType.OBJECT => core.GetSpecialType(SpecialType.System_Object),

                SigElementType.VAR => ReadVar(declaringType, ref reader),
                SigElementType.MVAR => ReadMVar(methodTypeParameters, ref reader),

                SigElementType.CLASS or SigElementType.VALUETYPE
                    => ResolveTypeDefOrRef(reader.ReadCompressedUInt(), typeByRid, core, declaringType, methodTypeParameters),

                SigElementType.GENERICINST
                => ReadGenericInst(core, typeByRid, ref reader, declaringType, methodTypeParameters),

                SigElementType.PTR
                    => core.CreatePointerType(ReadType(core, typeByRid, ref reader, declaringType, methodTypeParameters)),

                SigElementType.BYREF
                    => core.CreateByRefType(ReadType(core, typeByRid, ref reader, declaringType, methodTypeParameters)),

                SigElementType.FNPTR
                    => ReadFunctionPointerType(core, typeByRid, ref reader, declaringType, methodTypeParameters),

                SigElementType.CMOD_REQD or SigElementType.CMOD_OPT
                    => ReadModifiedType(core, typeByRid, ref reader, declaringType, methodTypeParameters),

                SigElementType.SZARRAY
                    => core.CreateArrayType(ReadType(core, typeByRid, ref reader, declaringType, methodTypeParameters), rank: 1, isSZArray: true),

                SigElementType.ARRAY
                => ReadMdArray(core, typeByRid, ref reader, declaringType, methodTypeParameters),

                _ => new ErrorTypeSymbol($"sig:{et}", containing: null, locations: ImmutableArray<Location>.Empty)
            };
        }
        private TypeSymbol ReadMethodReturnType(
            CoreLibraryBuilder core,
            NamedTypeSymbol[] typeByRid,
            ref SigReader reader,
            NamedTypeSymbol? declaringType,
            ImmutableArray<TypeParameterSymbol> methodTypeParameters,
            out bool returnsByRefReadonly)
        {
            returnsByRefReadonly = false;
            if (reader.PeekByte() != (byte)SigElementType.BYREF)
                return ReadType(core, typeByRid, ref reader, declaringType, methodTypeParameters);

            _ = reader.ReadByte();
            while (reader.PeekByte() == (byte)SigElementType.CMOD_REQD ||
                   reader.PeekByte() == (byte)SigElementType.CMOD_OPT)
            {
                var modifierKind = (SigElementType)reader.ReadByte();
                uint modifierType = reader.ReadCompressedUInt();
                if (modifierKind == SigElementType.CMOD_REQD &&
                    GetModifierTypeName(modifierType, typeByRid) ==
                    ("System.Runtime.InteropServices", "InAttribute"))
                {
                    returnsByRefReadonly = true;
                }
            }

            var elementType = ReadType(core, typeByRid, ref reader, declaringType, methodTypeParameters);
            return core.CreateByRefType(elementType);
        }

        private TypeSymbol ReadFunctionPointerType(
            CoreLibraryBuilder core,
            NamedTypeSymbol[] typeByRid,
            ref SigReader reader,
            NamedTypeSymbol? declaringType,
            ImmutableArray<TypeParameterSymbol> methodTypeParameters)
        {
            byte rawCallingConvention = reader.ReadByte();
            var callingConvention = (rawCallingConvention & 0x0F) switch
            {
                0x00 => FunctionPointerCallingConvention.Managed,
                0x01 => FunctionPointerCallingConvention.Cdecl,
                0x02 => FunctionPointerCallingConvention.Stdcall,
                0x03 => FunctionPointerCallingConvention.Thiscall,
                0x04 => FunctionPointerCallingConvention.Fastcall,
                _ => FunctionPointerCallingConvention.Unmanaged
            };
            int parameterCount = checked((int)reader.ReadCompressedUInt());
            var returnType = ReadFunctionPointerSignatureType(
                core,
                typeByRid,
                ref reader,
                declaringType,
                methodTypeParameters,
                out var returnRefKind);
            var parameters = ImmutableArray.CreateBuilder<FunctionPointerParameter>(parameterCount);
            for (int i = 0; i < parameterCount; i++)
            {
                var parameterType = ReadFunctionPointerSignatureType(
                    core,
                    typeByRid,
                    ref reader,
                    declaringType,
                    methodTypeParameters,
                    out var parameterRefKind);
                parameters.Add(new FunctionPointerParameter(parameterType, parameterRefKind));
            }
            return core.CreateFunctionPointerType(callingConvention, returnType, returnRefKind, parameters.ToImmutable());
        }

        private TypeSymbol ReadFunctionPointerSignatureType(
            CoreLibraryBuilder core,
            NamedTypeSymbol[] typeByRid,
            ref SigReader reader,
            NamedTypeSymbol? declaringType,
            ImmutableArray<TypeParameterSymbol> methodTypeParameters,
            out FunctionPointerRefKind refKind)
        {
            bool isByRef = reader.PeekByte() == (byte)SigElementType.BYREF;
            if (isByRef)
                _ = reader.ReadByte();

            refKind = isByRef ? FunctionPointerRefKind.Ref : FunctionPointerRefKind.None;
            while (reader.PeekByte() == (byte)SigElementType.CMOD_REQD ||
                   reader.PeekByte() == (byte)SigElementType.CMOD_OPT)
            {
                var modifierKind = (SigElementType)reader.ReadByte();
                uint modifierType = reader.ReadCompressedUInt();
                var modifierName = GetModifierTypeName(modifierType, typeByRid);
                if (modifierKind == SigElementType.CMOD_REQD &&
                    modifierName == ("System.Runtime.InteropServices", "OutAttribute"))
                {
                    refKind = FunctionPointerRefKind.Out;
                }
                else if (modifierKind == SigElementType.CMOD_REQD &&
                    modifierName == ("System.Runtime.InteropServices", "InAttribute"))
                {
                    refKind = FunctionPointerRefKind.In;
                }
                else if (modifierKind == SigElementType.CMOD_OPT &&
                    modifierName == ("System.Runtime.CompilerServices", "RequiresLocationAttribute"))
                {
                    refKind = FunctionPointerRefKind.RefReadOnly;
                }
            }

            return ReadType(core, typeByRid, ref reader, declaringType, methodTypeParameters);
        }

        private TypeSymbol ReadModifiedType(
            CoreLibraryBuilder core,
            NamedTypeSymbol[] typeByRid,
            ref SigReader reader,
            NamedTypeSymbol? declaringType,
            ImmutableArray<TypeParameterSymbol> methodTypeParameters)
        {
            _ = reader.ReadCompressedUInt();
            return ReadType(core, typeByRid, ref reader, declaringType, methodTypeParameters);
        }

        private (string Namespace, string Name) GetModifierTypeName(
            uint encoded,
            NamedTypeSymbol[] typeByRid)
        {
            int tag = (int)(encoded & 0x3u);
            int rid = checked((int)(encoded >> 2));
            if (tag == 0 && (uint)rid < (uint)typeByRid.Length && typeByRid[rid] is { } type)
                return (GetNamespaceName(type.ContainingSymbol), type.Name);

            if (tag == 1 && rid > 0 && rid <= _md.GetRowCount(MetadataTableKind.TypeRef))
            {
                var row = _md.GetTypeRef(rid);
                return (_md.GetString(row.Namespace), SplitArity(_md.GetString(row.Name)).name);
            }

            return (string.Empty, string.Empty);
        }

        private static string GetNamespaceName(Symbol? symbol)
        {
            var names = new Stack<string>();
            for (var current = symbol; current is not null; current = current.ContainingSymbol)
            {
                if (current is NamespaceSymbol ns && !string.IsNullOrEmpty(ns.Name))
                    names.Push(ns.Name);
            }
            return string.Join(".", names);
        }
        private static TypeSymbol ReadVar(NamedTypeSymbol? declaringType, ref SigReader reader)
        {
            uint ordinal = reader.ReadCompressedUInt();

            if (declaringType is not null)
            {
                var allTypeParameters = GetTypeParametersInMetadataOrder(declaringType);
                if ((uint)allTypeParameters.Length > ordinal)
                    return allTypeParameters[(int)ordinal];
            }

            return new ErrorTypeSymbol($"var:{ordinal}", containing: null, locations: ImmutableArray<Location>.Empty);
        }
        private static ImmutableArray<TypeParameterSymbol> GetTypeParametersInMetadataOrder(NamedTypeSymbol type)
        {
            var chain = new List<NamedTypeSymbol>();

            for (Symbol? cur = type; cur is NamedTypeSymbol nt; cur = nt.ContainingSymbol)
                chain.Add(nt);

            var b = ImmutableArray.CreateBuilder<TypeParameterSymbol>();

            for (int i = chain.Count - 1; i >= 0; i--)
            {
                var tps = chain[i].TypeParameters;
                for (int j = 0; j < tps.Length; j++)
                    b.Add(tps[j]);
            }

            return b.ToImmutable();
        }
        private static TypeSymbol ReadMVar(ImmutableArray<TypeParameterSymbol> methodTypeParameters, ref SigReader reader)
        {
            uint ordinal = reader.ReadCompressedUInt();
            if (!methodTypeParameters.IsDefaultOrEmpty && (uint)methodTypeParameters.Length > ordinal)
                return methodTypeParameters[(int)ordinal];
            return new ErrorTypeSymbol($"mvar:{ordinal}", containing: null, locations: ImmutableArray<Location>.Empty);
        }
        private TypeSymbol ReadGenericInst(
            CoreLibraryBuilder core,
            NamedTypeSymbol[] typeByRid,
            ref SigReader reader,
            NamedTypeSymbol? declaringType,
            ImmutableArray<TypeParameterSymbol> methodTypeParameters)
        {
            var kindEt = (SigElementType)reader.ReadByte();
            if (kindEt != SigElementType.CLASS && kindEt != SigElementType.VALUETYPE)
                return new ErrorTypeSymbol($"genericinst-kind:{kindEt}", containing: null, locations: ImmutableArray<Location>.Empty);
            var def = ResolveTypeDefOrRef(reader.ReadCompressedUInt(), typeByRid, core, declaringType, methodTypeParameters) as NamedTypeSymbol;
            if (def is null)
                return new ErrorTypeSymbol("genericinst-def", containing: null, locations: ImmutableArray<Location>.Empty);
            uint argc = reader.ReadCompressedUInt();
            if (argc == 0)
                return def;
            var args = ImmutableArray.CreateBuilder<TypeSymbol>((int)argc);
            for (int i = 0; i < (int)argc; i++)
                args.Add(ReadType(core, typeByRid, ref reader, declaringType, methodTypeParameters));
            return ConstructFromMetadataArguments(core, def, args.ToImmutable());
        }
        private static TypeSymbol ConstructFromMetadataArguments(CoreLibraryBuilder core, NamedTypeSymbol definition, ImmutableArray<TypeSymbol> arguments)
        {
            if (definition.ContainingSymbol is not NamedTypeSymbol)
                return core.ConstructNamedType(definition, arguments);

            var chain = new List<NamedTypeSymbol>();
            for (Symbol? current = definition; current is NamedTypeSymbol type; current = type.ContainingSymbol)
                chain.Add(type);
            chain.Reverse();

            int offset = 0;
            NamedTypeSymbol? constructed = null;
            foreach (var type in chain)
            {
                int arity = type.Arity;
                if (offset + arity > arguments.Length)
                    return new ErrorTypeSymbol($"genericinst-arity:{definition.Name}", containing: null, locations: ImmutableArray<Location>.Empty);
                var own = arguments.Slice(offset, arity);
                offset += arity;
                constructed = constructed is null
                    ? core.ConstructNamedType(type, own)
                    : core.ConstructNestedType(type, constructed, own);
            }
            return offset == arguments.Length
                ? constructed!
                : new ErrorTypeSymbol($"genericinst-arity:{definition.Name}", containing: null, locations: ImmutableArray<Location>.Empty);
        }
        private TypeSymbol ReadMdArray(
            CoreLibraryBuilder core,
            NamedTypeSymbol[] typeByRid,
            ref SigReader reader,
            NamedTypeSymbol? declaringType,
            ImmutableArray<TypeParameterSymbol> methodTypeParameters)
        {
            var elem = ReadType(core, typeByRid, ref reader, declaringType, methodTypeParameters);

            uint rank = reader.ReadCompressedUInt();
            uint numSizes = reader.ReadCompressedUInt();
            for (int i = 0; i < numSizes; i++)
                _ = reader.ReadCompressedUInt();

            uint numLoBounds = reader.ReadCompressedUInt();
            for (int i = 0; i < numLoBounds; i++)
                _ = reader.ReadCompressedUInt();
            if (rank == 0)
                return new ErrorTypeSymbol("array-rank-0", containing: null, locations: ImmutableArray<Location>.Empty);
            return core.CreateArrayType(elem, checked((int)rank), isSZArray: false);
        }
        private TypeSymbol ResolveTypeDefOrRef(
            uint encoded,
            NamedTypeSymbol[] typeByRid,
            CoreLibraryBuilder core,
            NamedTypeSymbol? declaringType,
            ImmutableArray<TypeParameterSymbol> methodTypeParameters)
        {
            int tag = (int)(encoded & 0x3u);
            int rid = (int)(encoded >> 2);

            // TypeDef
            if (tag == 0)
            {
                if ((uint)rid < (uint)typeByRid.Length && typeByRid[rid] is { } td)
                    return td;

                return new ErrorTypeSymbol($"typedef:{rid}", containing: null, locations: ImmutableArray<Location>.Empty);
            }
            // TypeDef
            if (tag == 1)
                return ResolveTypeRef(rid, core);

            // TypeSpec
            if (tag == 2)
                return ResolveTypeSpec(rid, typeByRid, core, declaringType, methodTypeParameters);

            return new ErrorTypeSymbol($"typeref:{tag}:{rid}", containing: null, locations: ImmutableArray<Location>.Empty);
        }
        private TypeSymbol ResolveTypeRef(int typeRefRid, CoreLibraryBuilder core)
        {
            if (typeRefRid <= 0 || typeRefRid > _md.GetRowCount(MetadataTableKind.TypeRef))
                return new ErrorTypeSymbol("bad-typeref", null, ImmutableArray<Location>.Empty);

            var tr = _md.GetTypeRef(typeRefRid);

            // Nested TypeRef is possible
            int scopeTable = MetadataToken.Table(tr.ResolutionScopeToken);
            if (scopeTable == MetadataToken.TypeRef)
            {
                var enclosing = ResolveTypeRef(MetadataToken.Rid(tr.ResolutionScopeToken), core) as NamedTypeSymbol;
                if (enclosing is null)
                    return new ErrorTypeSymbol("bad-nested-typeref", null, ImmutableArray<Location>.Empty);

                var mdName = _md.GetString(tr.Name);
                var (name, arity) = SplitArity(mdName);

                var nested = enclosing.GetTypeMembers(name, arity);
                return nested.Length != 0
                    ? nested[0]
                    : new ErrorTypeSymbol($"missing-nested:{name}", null, ImmutableArray<Location>.Empty);
            }

            // Top level TypeRef
            var ns = _md.GetString(tr.Namespace);
            var mdTypeName = _md.GetString(tr.Name);
            var (typeName, arity2) = SplitArity(mdTypeName);

            var nsSym = TryGetNamespace(core.GlobalNamespace, ns);
            if (nsSym is null)
                return new ErrorTypeSymbol($"missing-ns:{ns}", null, ImmutableArray<Location>.Empty);

            var types = nsSym.GetTypeMembers(typeName, arity2);
            return types.Length != 0
                ? types[0]
                : new ErrorTypeSymbol($"missing-type:{ns}.{typeName}", null, ImmutableArray<Location>.Empty);
        }
        private TypeSymbol ResolveTypeSpec(
            int typeSpecRid,
            NamedTypeSymbol[] typeByRid,
            CoreLibraryBuilder core,
            NamedTypeSymbol? declaringType,
            ImmutableArray<TypeParameterSymbol> methodTypeParameters)
        {
            if (typeSpecRid <= 0 || typeSpecRid > _md.GetRowCount(MetadataTableKind.TypeSpec))
                return new ErrorTypeSymbol("bad-typespec", containing: null, locations: ImmutableArray<Location>.Empty);
            var ts = _md.GetTypeSpec(typeSpecRid);
            var sig = _md.GetBlob(ts.Signature);
            var r = new SigReader(sig);
            return ReadType(core, typeByRid, ref r, declaringType, methodTypeParameters);
        }
        private static (string name, int arity) SplitArity(string mdName)
        {
            int tick = mdName.IndexOf('`');
            if (tick < 0)
                return (mdName, 0);

            var name = mdName.Substring(0, tick);

            if (tick + 1 < mdName.Length && int.TryParse(mdName.Substring(tick + 1), out int arity))
                return (name, arity);

            return (name, 0);
        }
        private static NamespaceSymbol? TryGetNamespace(NamespaceSymbol root, string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return root;

            var parts = fullName.Split('.', StringSplitOptions.RemoveEmptyEntries);
            NamespaceSymbol cur = root;

            for (int i = 0; i < parts.Length; i++)
            {
                var members = cur.GetNamespaceMembers();
                NamespaceSymbol? next = null;
                for (int j = 0; j < members.Length; j++)
                {
                    if (members[j].Name == parts[i])
                    {
                        next = members[j];
                        break;
                    }
                }
                if (next is null)
                    return null;

                cur = next;
            }
            return cur;
        }
        private ref struct SigReader
        {
            private readonly ReadOnlySpan<byte> _s;
            private int _i;

            public SigReader(ReadOnlySpan<byte> s)
            {
                _s = s;
                _i = 0;
            }
            public byte ReadByte()
            {
                if ((uint)_i >= (uint)_s.Length) throw new InvalidOperationException("Signature underflow.");
                return _s[_i++];
            }
            public byte PeekByte()
            {
                if ((uint)_i >= (uint)_s.Length) throw new InvalidOperationException("Signature underflow.");
                return _s[_i];
            }

            public uint ReadCompressedUInt()
            {
                byte b0 = ReadByte();
                if ((b0 & 0x80) == 0)
                    return b0;

                if ((b0 & 0xC0) == 0x80)
                {
                    byte b1 = ReadByte();
                    return (uint)(((b0 & 0x3F) << 8) | b1);
                }

                if ((b0 & 0xE0) == 0xC0)
                {
                    byte b1 = ReadByte();
                    byte b2 = ReadByte();
                    byte b3 = ReadByte();
                    return (uint)(((b0 & 0x1F) << 24) | (b1 << 16) | (b2 << 8) | b3);
                }

                throw new InvalidOperationException("Bad compressed uint.");
            }
        }
    }

}
