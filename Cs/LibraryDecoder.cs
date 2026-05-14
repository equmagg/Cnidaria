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
        private readonly ImmutableArray<IMetadataView> _refs;
        private readonly Dictionary<NamedTypeSymbol, string> _asmByType =
            new(ReferenceEqualityComparer<NamedTypeSymbol>.Instance);

        private readonly HashSet<string> _seenModules = new(StringComparer.Ordinal);

        public MetadataReferenceSet(IEnumerable<IMetadataView> references)
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
                foreach (SpecialType st in Enum.GetValues(typeof(SpecialType)))
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

                var members = t.GetMembers();
                for (int i = 0; i < members.Length; i++)
                    if (members[i] is NamedTypeSymbol nt)
                        AddTypeAndNested(nt);
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
        public ByRefTypeSymbol CreateByRefType(TypeSymbol elementType)
            => _types.GetByRefType(elementType);
        public ArrayTypeSymbol CreateArrayType(TypeSymbol elementType, int rank)
            => _types.GetArrayType(elementType, rank);
        public NamedTypeSymbol ConstructNamedType(NamedTypeSymbol type, ImmutableArray<TypeSymbol> typeArguments)
            => _types.ConstructNamedType(type, typeArguments);
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
            bool isFromMetadata)
        {
            var t = new SourceNamedTypeSymbol(
                name,
                containing,
                kind,
                arity: arity,
                declaredAccessibility: declaredAccessibility,
                isFromMetadata: isFromMetadata);

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
            bool isFromMetadata = false)
        {
            var ns = (SyntheticNamespaceSymbol)EnsureNamespace(@namespace);
            return CreateTypeCore(
                ns,
                name,
                kind,
                arity,
                declaredAccessibility,
                isFromMetadata);
        }

        public NamedTypeSymbol AddNestedType(
            NamedTypeSymbol containingType,
            string name,
            TypeKind kind,
            int arity = 0,
            Accessibility declaredAccessibility = Accessibility.Public,
            bool isFromMetadata = false)
        {
            if (containingType is null) throw new ArgumentNullException(nameof(containingType));

            return CreateTypeCore(
                containingType,
                name,
                kind,
                arity,
                declaredAccessibility,
                isFromMetadata);
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
            ImmutableArray<TypeParameterSymbol> typeParameters)
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
                isExtensionMethod: isExtensionMethod);

            if (!typeParameters.IsDefaultOrEmpty && m is ExternalMethodSymbol em)
                em.SetTypeParameters(typeParameters);

            AddMemberToType(containingType, m);
            return m;
        }
    }

    public sealed class MetadataCoreLibProvider : ICoreLibraryProvider
    {
        private readonly IMetadataView _md;
        internal MetadataCoreLibProvider(MetadataImage md)
        : this(new MetadataImageView(md))
        {  }
        internal MetadataCoreLibProvider(IMetadataView md)
            => _md = md ?? throw new ArgumentNullException(nameof(md));
        public void Populate(CoreLibraryBuilder core)
        {
            var typeCount = _md.GetRowCount(MetadataTableKind.TypeDef);
            var typeByRid = new NamedTypeSymbol[typeCount + 1]; // 1 based

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

                if (TryMapSpecialType(ns, name, out var st) && !enclosingByRid.ContainsKey(rid))
                {
                    var special = core.GetSpecialType(st);
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
                        isFromMetadata: true);
                }
                else
                {
                    created = core.AddType(
                        ns,
                        name,
                        kind,
                        arity: arity,
                        declaredAccessibility: DecodeTypeAccessibility(td.Flags),
                        isFromMetadata: true);
                }

                typeByRid[rid] = created;
                return created;
            }

            for (int rid = 1; rid <= typeCount; rid++)
                _ = EnsureType(rid);

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
            var ifaceSets = new Dictionary<SourceNamedTypeSymbol, HashSet<TypeSymbol>>(
                ReferenceEqualityComparer<SourceNamedTypeSymbol>.Instance);

            for (int rid = 1; rid <= _md.GetRowCount(MetadataTableKind.InterfaceImpl); rid++)
            {
                var row = _md.GetInterfaceImpl(rid);

                if ((uint)row.ClassTypeDefRid >= (uint)typeByRid.Length)
                    continue;

                if (typeByRid[row.ClassTypeDefRid] is not SourceNamedTypeSymbol owner)
                    continue;

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
                    set = new HashSet<TypeSymbol>(ReferenceEqualityComparer<TypeSymbol>.Instance);
                    ifaceSets.Add(owner, set);
                }

                set.Add(iface.OriginalDefinition);
            }

            foreach (var kv in ifaceSets)
            {
                var b = ImmutableArray.CreateBuilder<TypeSymbol>(kv.Value.Count);
                foreach (var iface in kv.Value)
                    b.Add(iface);

                kv.Key.SetDeclaredInterfaces(b.ToImmutable());
            }

            var methodByRid = AddMethods(core, typeByRid);
            var fieldByRid = AddFields(core, typeByRid);
            var propertyByRid = AddPropertiesFromTable(core, typeByRid, methodByRid);
            AddPropertiesFromAccessors(core, typeByRid);
            ApplyMethodImpls(core, typeByRid, methodByRid);

            var paramByRid = BuildParamMap(methodByRid);
            ApplyCustomAttributes(core, typeByRid, fieldByRid, methodByRid, paramByRid, propertyByRid);
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
        private void ApplyParamRefKinds(MethodSymbol method, int paramListRid, int paramCount)
        {
            if (method is null || paramCount <= 0)
                return;

            int totalParams = _md.GetRowCount(MetadataTableKind.Param);
            if (totalParams == 0)
                return;
            if (paramListRid <= 0 || paramListRid > totalParams)
                return;

            var ps = method.Parameters;
            for (int i = 0; i < paramCount && i < ps.Length; i++)
            {
                int rid = paramListRid + i;
                if (rid > totalParams)
                    break;

                var row = _md.GetParam(rid);
                if (row.Sequence != (ushort)(i + 1))
                    continue;

                var p = ps[i];
                if (p.Type is not ByRefTypeSymbol)
                    continue;
                var attrs = (System.Reflection.ParameterAttributes)row.Flags;
                bool isOut = (attrs & System.Reflection.ParameterAttributes.Out) != 0;
                bool isIn = (attrs & System.Reflection.ParameterAttributes.In) != 0;

                if (isOut)
                {
                    p.RefKind = ParameterRefKind.Out;
                    p.IsReadOnlyRef = false;
                }
                else if (isIn)
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
        private void ApplyParamDefaultValues(
            MethodSymbol method, int paramListRid, int paramCount, Dictionary<int, ConstantRow> constByParent)
        {
            if (method is null || paramCount <= 0)
                return;
            if (constByParent is null || constByParent.Count == 0)
                return;

            int totalParams = _md.GetRowCount(MetadataTableKind.Param);
            if (totalParams == 0)
                return;
            if (paramListRid <= 0 || paramListRid > totalParams)
                return;

            var ps = method.Parameters;
            for (int i = 0; i < paramCount && i < ps.Length; i++)
            {
                int rid = paramListRid + i;
                if (rid > totalParams)
                    break;

                var row = _md.GetParam(rid);
                if (row.Sequence != (ushort)(i + 1))
                    continue;

                var p = ps[i];
                if (p.Type is ByRefTypeSymbol)
                    continue;

                int parentTok = MetadataToken.Make(MetadataToken.ParamDef, rid);
                if (!constByParent.TryGetValue(parentTok, out var crow))
                    continue;

                var cval = DecodeConstant(p.Type, crow);
                if (cval.HasValue)
                    p.SetDefaultValue(cval);
            }
        }
        private void ApplyMethodImpls(
            CoreLibraryBuilder core,
            NamedTypeSymbol[] typeByRid,
            Dictionary<int, MethodSymbol> methodByRid)
        {
            int count = _md.GetRowCount(MetadataTableKind.MethodImpl);
            if (count == 0)
                return;

            var accessorToProperty = new Dictionary<MethodSymbol, PropertySymbol>(
                ReferenceEqualityComparer<MethodSymbol>.Instance);

            for (int i = 1; i < typeByRid.Length; i++)
            {
                var type = typeByRid[i];
                if (type is null)
                    continue;

                var members = type.GetMembers();
                for (int m = 0; m < members.Length; m++)
                {
                    if (members[m] is not PropertySymbol p)
                        continue;
                    if (p.GetMethod is not null)
                        accessorToProperty[p.GetMethod] = p;
                    if (p.SetMethod is not null)
                        accessorToProperty[p.SetMethod] = p;
                }
            }

            for (int rid = 1; rid <= count; rid++)
            {
                var row = _md.GetMethodImpl(rid);

                if ((uint)row.ClassTypeDefRid >= (uint)typeByRid.Length)
                    continue;

                var body = ResolveMethodToken(core, typeByRid, methodByRid, row.BodyMethodToken);
                var decl = ResolveMethodToken(core, typeByRid, methodByRid, row.DeclarationMethodToken);

                if (body is null || decl is null)
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

                if (accessorToProperty.TryGetValue(body, out var bodyProp) &&
                    accessorToProperty.TryGetValue(decl, out var declProp))
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
        private MethodSymbol? ResolveMethodToken(
            CoreLibraryBuilder core,
            NamedTypeSymbol[] typeByRid,
            Dictionary<int, MethodSymbol> methodByRid,
            int token)
        {
            int table = MetadataToken.Table(token);
            int rid = MetadataToken.Rid(token);
            return table switch
            {
                MetadataToken.MethodDef => methodByRid.TryGetValue(rid, out var m) ? m : null,
                MetadataToken.MemberRef => ResolveMemberRefMethod(core, typeByRid, rid),
                _ => null
            };
        }
        private MethodSymbol? ResolveMemberRefMethod(CoreLibraryBuilder core, NamedTypeSymbol[] typeByRid, int memberRefRid)
        {
            if (memberRefRid <= 0 || memberRefRid > _md.GetRowCount(MetadataTableKind.MemberRef))
                return null;

            var row = _md.GetMemberRef(memberRefRid);
            string name = _md.GetString(row.Name);
            if (string.IsNullOrEmpty(name))
                return null;

            NamedTypeSymbol? ownerType = ResolveTypeToken(core, typeByRid, row.ClassToken) as NamedTypeSymbol;
            if (ownerType is null)
                return null;

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

                var members = type.GetMembers();
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
                        return m;
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
        private static bool AreEquivalentSignatureType(TypeSymbol a, TypeSymbol b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (a.SpecialType != SpecialType.None || b.SpecialType != SpecialType.None)
                return a.SpecialType == b.SpecialType;

            if (a is ArrayTypeSymbol aa && b is ArrayTypeSymbol ab)
                return aa.Rank == ab.Rank && AreEquivalentSignatureType(aa.ElementType, ab.ElementType);

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
        private void ApplyCustomAttributes(
            CoreLibraryBuilder core,
            NamedTypeSymbol[] typeByRid,
            Dictionary<int, FieldSymbol> fieldByRid,
            Dictionary<int, MethodSymbol> methodByRid,
            Dictionary<int, ParameterSymbol> paramByRid,
            Dictionary<int, PropertySymbol> propertyByRid)
        {
            int count = _md.GetRowCount(MetadataTableKind.CustomAttribute);
            if (count == 0)
                return;

            for (int rid = 1; rid <= count; rid++)
            {
                var row = _md.GetCustomAttribute(rid);

                var owner = ResolveAttributeOwner(
                    row.ParentToken,
                    typeByRid,
                    fieldByRid,
                    methodByRid,
                    paramByRid,
                    propertyByRid);

                if (owner is null)
                    continue;

                var data = DecodeCustomAttribute(core, typeByRid, row);
                if (data is null)
                    continue;

                AddImportedAttribute(owner, data);
            }
        }
        private Symbol? ResolveAttributeOwner(
            int parentToken,
            NamedTypeSymbol[] typeByRid,
            Dictionary<int, FieldSymbol> fieldByRid,
            Dictionary<int, MethodSymbol> methodByRid,
            Dictionary<int, ParameterSymbol> paramByRid,
            Dictionary<int, PropertySymbol> propertyByRid)
        {
            int table = MetadataToken.Table(parentToken);
            int rid = MetadataToken.Rid(parentToken);

            return table switch
            {
                MetadataToken.TypeDef => (rid > 0 && rid < typeByRid.Length) ? typeByRid[rid] : null,
                MetadataToken.FieldDef => fieldByRid.TryGetValue(rid, out var f) ? f : null,
                MetadataToken.MethodDef => methodByRid.TryGetValue(rid, out var m) ? m : null,
                MetadataToken.ParamDef => paramByRid.TryGetValue(rid, out var p) ? p : null,
                MetadataToken.PropertyDef => propertyByRid.TryGetValue(rid, out var prop) ? prop : null,
                _ => null
            };
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

                case ParameterSymbol p: p.AddAttribute(data); break;
                case TypeParameterSymbol tp: tp.AddAttribute(data); break;
            }
        }
        private AttributeData? DecodeCustomAttribute(
            CoreLibraryBuilder core,
            NamedTypeSymbol[] typeByRid,
            CustomAttributeRow row)
        {
            var attrTypeSym = ResolveTypeToken(core, typeByRid, row.AttributeTypeToken) as NamedTypeSymbol;
            if (attrTypeSym is null)
                return null;

            var blob = _md.GetBlob(row.Value);
            var r = new AttrBlobReader(blob);

            int ctorParamCount = r.ReadInt32();
            var ctorParamTypes = ImmutableArray.CreateBuilder<TypeSymbol>(ctorParamCount);
            for (int i = 0; i < ctorParamCount; i++)
                ctorParamTypes.Add(ResolveTypeToken(core, typeByRid, r.ReadInt32()));

            var ctor = FindMatchingAttributeConstructor(attrTypeSym, ctorParamTypes.ToImmutable());
            if (ctor is null)
                return null;

            int ctorArgCount = r.ReadInt32();
            var ctorArgs = ImmutableArray.CreateBuilder<TypedConstant>(ctorArgCount);
            for (int i = 0; i < ctorArgCount; i++)
                ctorArgs.Add(ReadTypedConstant(core, typeByRid, ref r));

            int namedCount = r.ReadInt32();
            var namedArgs = ImmutableArray.CreateBuilder<AttributeNamedArgumentData>(namedCount);

            for (int i = 0; i < namedCount; i++)
            {
                byte memberKind = r.ReadByte(); // 1 = field, 2 = property
                string memberName = _md.GetString(r.ReadInt32());
                var value = ReadTypedConstant(core, typeByRid, ref r);

                var member = FindAttributeNamedMember(attrTypeSym, memberKind, memberName);
                if (member is null)
                    continue;

                namedArgs.Add(new AttributeNamedArgumentData(memberName, member, value));
            }

            return new AttributeData(
                attributeClass: attrTypeSym,
                constructor: ctor,
                constructorArguments: ctorArgs.ToImmutable(),
                namedArguments: namedArgs.ToImmutable(),
                target: (AttributeApplicationTarget)row.Target);
        }
        private TypeSymbol ResolveTypeToken(CoreLibraryBuilder core, NamedTypeSymbol[] typeByRid, int token)
        {
            int table = MetadataToken.Table(token);
            int rid = MetadataToken.Rid(token);

            return table switch
            {
                MetadataToken.TypeDef => (rid > 0 && rid < typeByRid.Length && typeByRid[rid] is not null)
                    ? typeByRid[rid]
                    : new ErrorTypeSymbol($"typedef:{rid}", null, ImmutableArray<Location>.Empty),

                MetadataToken.TypeRef => ResolveTypeRef(rid, core),
                MetadataToken.TypeSpec => ResolveTypeSpec(rid, typeByRid, core, null, ImmutableArray<TypeParameterSymbol>.Empty),

                _ => new ErrorTypeSymbol($"bad-type-token:0x{token:X8}", null, ImmutableArray<Location>.Empty)
            };
        }

        private MethodSymbol? FindMatchingAttributeConstructor(
            NamedTypeSymbol attributeType,
            ImmutableArray<TypeSymbol> parameterTypes)
        {
            for (NamedTypeSymbol? t = attributeType; t is not null; t = t.BaseType as NamedTypeSymbol)
            {
                var members = t.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is not MethodSymbol m || !m.IsConstructor)
                        continue;

                    var ps = m.Parameters;
                    if (ps.Length != parameterTypes.Length)
                        continue;

                    bool same = true;
                    for (int p = 0; p < ps.Length; p++)
                    {
                        if (!AreSameType(ps[p].Type, parameterTypes[p]))
                        {
                            same = false;
                            break;
                        }
                    }

                    if (same)
                        return m;
                }
            }

            return null;
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

        private TypedConstant ReadTypedConstant(
            CoreLibraryBuilder core,
            NamedTypeSymbol[] typeByRid,
            ref AttrBlobReader r)
        {
            var type = ResolveTypeToken(core, typeByRid, r.ReadInt32());
            byte kind = r.ReadByte();

            object? value = kind switch
            {
                0 => null,
                1 => r.ReadByte() != 0,
                2 => (char)r.ReadUInt16(),
                3 => r.ReadSByte(),
                4 => r.ReadByte(),
                5 => r.ReadInt16(),
                6 => r.ReadUInt16(),
                7 => r.ReadInt32(),
                8 => r.ReadUInt32(),
                9 => r.ReadInt64(),
                10 => r.ReadUInt64(),
                11 => r.ReadSingle(),
                12 => r.ReadDouble(),
                13 => _md.GetString(r.ReadInt32()),
                14 => ResolveTypeToken(core, typeByRid, r.ReadInt32()),
                _ => throw new InvalidOperationException($"Unsupported attribute constant kind: {kind}")
            };

            return new TypedConstant(type, value);
        }

        private static bool AreSameType(TypeSymbol a, TypeSymbol b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (a.SpecialType != SpecialType.None || b.SpecialType != SpecialType.None)
                return a.SpecialType == b.SpecialType;

            if (a is ArrayTypeSymbol aa && b is ArrayTypeSymbol ab)
                return aa.Rank == ab.Rank && AreSameType(aa.ElementType, ab.ElementType);

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
        private Dictionary<int, MethodSymbol> AddMethods(CoreLibraryBuilder core, NamedTypeSymbol[] typeByRid)
        {
            var methodByRid = new Dictionary<int, MethodSymbol>();
            var constByParent = new Dictionary<int, ConstantRow>();
            for (int i = 0; i < _md.GetRowCount(MetadataTableKind.Constant); i++)
                constByParent[_md.GetConstant(i + 1).ParentToken] = _md.GetConstant(i + 1);
            for (int rid = 1; rid <= _md.GetRowCount(MetadataTableKind.TypeDef); rid++)
            {
                var declaringType = typeByRid[rid];
                if (declaringType is null)
                    continue;

                var td = _md.GetTypeDef(rid);

                int start = td.MethodList; // 1 based
                int end = (rid == _md.GetRowCount(MetadataTableKind.TypeDef))
                    ? (_md.GetRowCount(MetadataTableKind.MethodDef) + 1)
                    : _md.GetTypeDef(rid + 1).MethodList;

                if (start <= 0 || end < start)
                    continue;

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
                    var retType = ReadType(core, typeByRid, ref reader, declaringType, mtps);

                    var ps = ImmutableArray.CreateBuilder<(string name, TypeSymbol type)>((int)paramCount);
                    int totalParams = _md.GetRowCount(MetadataTableKind.Param);
                    for (int i = 0; i < paramCount; i++)
                    {
                        var pt = ReadType(core, typeByRid, ref reader, declaringType, mtps);
                        string paramName = $"arg{i}";
                        int prid = mdRow.ParamList + i;
                        if (mdRow.ParamList > 0 && prid > 0 && prid <= totalParams)
                        {
                            var prow = _md.GetParam(prid);
                            if (prow.Sequence == (ushort)(i + 1) && prow.Name != 0)
                            {
                                var decodedName = _md.GetString(prow.Name);
                                if (!string.IsNullOrEmpty(decodedName))
                                    paramName = decodedName;
                            }
                        }
                        ps.Add((paramName, pt));
                    }

                    bool isCtor =
                        string.Equals(mname, declaringType.Name, StringComparison.Ordinal) &&
                        retType.SpecialType == SpecialType.System_Void;

                    bool isVirtual = (mdRow.Flags & (ushort)System.Reflection.MethodAttributes.Virtual) != 0;
                    bool isAbstract = (mdRow.Flags & (ushort)System.Reflection.MethodAttributes.Abstract) != 0;
                    bool isNewSlot = (mdRow.Flags & (ushort)System.Reflection.MethodAttributes.NewSlot) != 0;
                    bool isFinal = (mdRow.Flags & (ushort)System.Reflection.MethodAttributes.Final) != 0;
                    bool isOverride = isVirtual && !isNewSlot;
                    bool isSealed = isFinal;
                    bool isExtensionMethod = (mdRow.Flags & MetadataFlagBits.Extension) != 0;

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
                        typeParameters: mtps);

                    ApplyParamRefKinds(ms, mdRow.ParamList, (int)paramCount);
                    ApplyParamDefaultValues(ms, mdRow.ParamList, (int)paramCount, constByParent);
                    methodByRid[mrid] = ms;
                }
            }

            return methodByRid;
        }

        private Dictionary<int, FieldSymbol> AddFields(CoreLibraryBuilder core, NamedTypeSymbol[] typeByRid)
        {
            var fieldByRid = new Dictionary<int, FieldSymbol>();
            var constByParent = new Dictionary<int, ConstantRow>();
            for (int i = 0; i < _md.GetRowCount(MetadataTableKind.Constant); i++)
                constByParent[_md.GetConstant(i+1).ParentToken] = _md.GetConstant(i+1);
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

                    bool isStatic = (frow.Flags & 0x0010) != 0; // FieldAttributes.Static
                    bool isConst = (frow.Flags & 0x0040) != 0; // FieldAttributes.Literal
                    Optional<object> cval = default;
                    if (isConst)
                    {
                        int parentTok = MetadataToken.Make(MetadataToken.FieldDef, frid);
                        if (constByParent.TryGetValue(parentTok, out var crow))
                            cval = DecodeConstant(ftype, crow);
                    }
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
            if (row.TypeCode == 0 && blob.Length == 0)
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
                case 0x0E: return new Optional<object>(Encoding.UTF8.GetString(blob));// String

                default: return Optional<object>.None;
            }
        }
        private Dictionary<int, PropertySymbol> AddPropertiesFromTable(
            CoreLibraryBuilder core,
            NamedTypeSymbol[] typeByRid,
            Dictionary<int, MethodSymbol> methodByRid)
        {
            var propertyByRid = new Dictionary<int, PropertySymbol>();

            if (_md.GetRowCount(MetadataTableKind.Property) == 0)
                return propertyByRid;

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

            for (int prid = 1; prid <= _md.GetRowCount(MetadataTableKind.Property); prid++)
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

            return propertyByRid;
        }
        private Dictionary<int, ParameterSymbol> BuildParamMap(Dictionary<int, MethodSymbol> methodByRid)
        {
            var map = new Dictionary<int, ParameterSymbol>();
            int totalParams = _md.GetRowCount(MetadataTableKind.Param);

            if (totalParams == 0)
                return map;

            for (int mrid = 1; mrid <= _md.GetRowCount(MetadataTableKind.MethodDef); mrid++)
            {
                if (!methodByRid.TryGetValue(mrid, out var method))
                    continue;

                var mrow = _md.GetMethodDef(mrid);
                if (mrow.ParamList <= 0 || mrow.ParamList > totalParams)
                    continue;

                var ps = method.Parameters;
                for (int i = 0; i < ps.Length; i++)
                {
                    int prid = mrow.ParamList + i;
                    if (prid > totalParams)
                        break;

                    var prow = _md.GetParam(prid);
                    if (prow.Sequence == (ushort)(i + 1))
                        map[prid] = ps[i];
                }
            }

            return map;
        }
        private void AddPropertiesFromAccessors(CoreLibraryBuilder core, NamedTypeSymbol[] typeByRid)
        {
            for (int rid = 1; rid <= _md.GetRowCount(MetadataTableKind.TypeDef); rid++)
            {
                var t = typeByRid[rid];
                if (t is null)
                    continue;

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

                SigElementType.SZARRAY
                    => core.CreateArrayType(ReadType(core, typeByRid, ref reader, declaringType, methodTypeParameters), rank: 1),

                SigElementType.ARRAY
                => ReadMdArray(core, typeByRid, ref reader, declaringType, methodTypeParameters),

                _ => new ErrorTypeSymbol($"sig:{et}", containing: null, locations: ImmutableArray<Location>.Empty)
            };
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
            return core.ConstructNamedType(def, args.ToImmutable());
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
            return core.CreateArrayType(elem, checked((int)rank));
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
