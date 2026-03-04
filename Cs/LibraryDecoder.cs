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
        public NamedTypeSymbol AddType(
            string @namespace,
            string name,
            TypeKind kind,
            int arity = 0,
            Accessibility declaredAccessibility = Accessibility.Public,
            bool isFromMetadata = false)
        {
            var ns = (SyntheticNamespaceSymbol)EnsureNamespace(@namespace);

            var t = new SourceNamedTypeSymbol(
                name,
                ns,
                kind,
                arity: arity,
                declaredAccessibility: declaredAccessibility,
                isFromMetadata: isFromMetadata);

            TypeSymbol? defaultBase = kind switch
            {
                TypeKind.Class => _types.GetSpecialType(SpecialType.System_Object),
                TypeKind.Struct => _types.GetSpecialType(SpecialType.System_ValueType),
                TypeKind.Enum => _types.GetSpecialType(SpecialType.System_Enum),
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

            ns.AddType(t);
            return t;
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
                isSealed: isSealed);

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
        { }
        internal MetadataCoreLibProvider(IMetadataView md)
            => _md = md ?? throw new ArgumentNullException(nameof(md));
        public void Populate(CoreLibraryBuilder core)
        {
            // map types by RID
            var typeByRid = new NamedTypeSymbol[_md.GetRowCount(MetadataTableKind.TypeDef) + 1]; // 1 based
            // create/map types
            for (int rid = 1; rid <= _md.GetRowCount(MetadataTableKind.TypeDef); rid++)
            {
                var td = _md.GetTypeDef(rid);
                var ns = _md.GetString(td.Namespace);
                var mdName = _md.GetString(td.Name);
                var (name, arity) = SplitArity(mdName);

                if (string.IsNullOrEmpty(name))
                    continue;

                // Special types
                if (TryMapSpecialType(ns, name, out var st))
                {
                    typeByRid[rid] = core.GetSpecialType(st);
                    continue;
                }
                var kind = InferKindFromExtends(td.ExtendsEncoded);
                var t = core.AddType(
                    ns, name, kind,
                    arity: arity,
                    declaredAccessibility: DecodeTypeAccessibility(td.Flags),
                    isFromMetadata: true);
                typeByRid[rid] = t;
            }
            // apply declared base types from ExtendsEncoded
            for (int rid = 1; rid <= _md.GetRowCount(MetadataTableKind.TypeDef); rid++)
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
                    declaringType: null,
                    methodTypeParameters: ImmutableArray<TypeParameterSymbol>.Empty);
                if (baseType is NamedTypeSymbol bt)
                {
                    src.SetDeclaredBaseType(bt);
                }
            }

            // Import
            var methodByRid = AddMethods(core, typeByRid);
            AddFields(core, typeByRid);
            AddPropertiesFromTable(core, typeByRid, methodByRid);
            AddPropertiesFromAccessors(core, typeByRid);
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
        private Dictionary<int, MethodSymbol> AddMethods(CoreLibraryBuilder core, NamedTypeSymbol[] typeByRid)
        {
            var methodByRid = new Dictionary<int, MethodSymbol>();

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
                    var retType = ReadType(core, typeByRid, ref reader, declaringType, ImmutableArray<TypeParameterSymbol>.Empty);

                    var ps = ImmutableArray.CreateBuilder<(string name, TypeSymbol type)>((int)paramCount);
                    for (int i = 0; i < paramCount; i++)
                    {
                        var pt = ReadType(core, typeByRid, ref reader, declaringType, ImmutableArray<TypeParameterSymbol>.Empty);
                        ps.Add(($"arg{i}", pt));
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
                        typeParameters: mtps);

                    ApplyParamRefKinds(ms, mdRow.ParamList, (int)paramCount);

                    methodByRid[mrid] = ms;
                }
            }

            return methodByRid;
        }

        private void AddFields(CoreLibraryBuilder core, NamedTypeSymbol[] typeByRid)
        {
            var constByParent = new Dictionary<int, ConstantRow>();
            for (int i = 0; i < _md.GetRowCount(MetadataTableKind.Constant); i++)
                constByParent[_md.GetConstant(i + 1).ParentToken] = _md.GetConstant(i + 1);
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
                    core.AddExternalField(
                        declaringType,
                        fname,
                        ftype,
                        isStatic,
                        isConst,
                        declaredAccessibility: DecodeFieldAccessibility(frow.Flags),
                        cval);
                }
            }
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
        private void AddPropertiesFromTable(
            CoreLibraryBuilder core,
            NamedTypeSymbol[] typeByRid,
            Dictionary<int, MethodSymbol> methodByRid)
        {
            if (_md.GetRowCount(MetadataTableKind.Property) == 0)
                return;

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

                core.AddExternalProperty(
                    containingType: declaring,
                    name: pname,
                    type: propType,
                    isStatic: isStatic,
                    declaredAccessibility: DerivePropertyAccessibility(get, set),
                    getMethod: get,
                    setMethod: set,
                    parameters: propParameters);

                existing.Add(pname);
            }
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
        private TypeKind InferKindFromExtends(int extendsEncoded)
        {
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

                        var td = _md.GetTypeDef(rid);
                        baseNs = _md.GetString(td.Namespace);
                        baseName = _md.GetString(td.Name);
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
                var tps = declaringType.TypeParameters;
                if ((uint)tps.Length > ordinal)
                    return tps[(int)ordinal];
            }
            return new ErrorTypeSymbol($"var:{ordinal}", containing: null, locations: ImmutableArray<Location>.Empty);
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
    public sealed class MinimalCoreLibProvider : ICoreLibraryProvider
    {
        public void Populate(CoreLibraryBuilder core)
        {
            core.EnsureNamespace("System");

            core.AddParameterlessInstanceConstructor(core.GetSpecialType(SpecialType.System_Object));

            var console = core.AddClass("System", "Console");

            var @void = core.GetSpecialType(SpecialType.System_Void);
            var str = core.GetSpecialType(SpecialType.System_String);
            var @int = core.GetSpecialType(SpecialType.System_Int32);

            core.AddIntrinsicStaticMethod(
                containingType: console,
                name: "WriteLine",
                returnType: @void,
                parameters: ImmutableArray.Create<(string, TypeSymbol)>(("value", str)),
                intrinsicName: "System.Console.WriteLine(string)");

            core.AddIntrinsicStaticMethod(
                containingType: console,
                name: "WriteLine",
                returnType: @void,
                parameters: ImmutableArray.Create<(string, TypeSymbol)>(("value", @int)),
                intrinsicName: "System.Console.WriteLine(int)");

        }
    }

}
