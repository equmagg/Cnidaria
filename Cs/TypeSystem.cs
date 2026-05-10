using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace Cnidaria.Cs
{
    sealed class RuntimeModule : Cnidaria.Cs.IRuntimeMetadataModule
    {
        public string Name { get; }
        public IMetadataView Md { get; }
        public Dictionary<int, Cnidaria.Cs.BytecodeFunction> MethodsByDefToken { get; }

        public Dictionary<(string ns, string name), int> TypeDefByFullName { get; } = new();

        public Dictionary<(int typeDefToken, string methodName, string sigKey), int> MethodDefIndex { get; } = new();
        private readonly Dictionary<int, string> _sigKeyCache = new();
        private readonly Dictionary<int, int> _enclosingByNestedRid = new();
        private readonly Dictionary<int, (string ns, string name)> _fullTypeNameCache = new();
        public RuntimeModule(string name, IMetadataView md, Dictionary<int, Cnidaria.Cs.BytecodeFunction> methodsByDefToken)
        {
            Name = name;
            Md = md ?? throw new ArgumentNullException(nameof(md));
            MethodsByDefToken = methodsByDefToken;

            BuildTypeIndex();
            BuildMethodIndex();
        }

        private string GetSigKey(int sigBlobIdx)
        {
            if (sigBlobIdx == 0) return "";

            if (_sigKeyCache.TryGetValue(sigBlobIdx, out var k))
                return k;

            var sig = Md.GetBlob(sigBlobIdx);
            var r = new SigReader(sig);
            var sb = new StringBuilder(sig.Length * 2);

            byte cc = r.ReadByte();
            sb.Append("cc=").Append(cc).Append(';');

            if ((cc & 0x10) != 0) // GENERIC
            {
                uint genArity = r.ReadCompressedUInt();
                sb.Append("ga=").Append(genArity).Append(';');
            }

            uint paramCount = r.ReadCompressedUInt();
            sb.Append("pc=").Append(paramCount).Append(';');

            AppendTypeKey(sb, ref r); // ret
            for (int i = 0; i < paramCount; i++)
                AppendTypeKey(sb, ref r);

            k = sb.ToString();
            _sigKeyCache[sigBlobIdx] = k;
            return k;
        }
        private void AppendTypeKey(StringBuilder sb, ref SigReader r)
        {
            var et = (SigElementType)r.ReadByte();
            sb.Append((byte)et);

            switch (et)
            {
                case SigElementType.CLASS:
                case SigElementType.VALUETYPE:
                    {
                        uint coded = r.ReadCompressedUInt();
                        AppendTypeDefOrRefKey(sb, coded);
                        break;
                    }

                case SigElementType.SZARRAY:
                    sb.Append("[]<");
                    AppendTypeKey(sb, ref r);
                    sb.Append('>');
                    break;

                case SigElementType.ARRAY:
                    {
                        sb.Append("[,]<");
                        AppendTypeKey(sb, ref r); // elem
                        uint rank = r.ReadCompressedUInt();
                        uint nsizes = r.ReadCompressedUInt();
                        for (int i = 0; i < nsizes; i++) _ = r.ReadCompressedUInt(); // sizes
                        uint nlb = r.ReadCompressedUInt();
                        for (int i = 0; i < nlb; i++) _ = r.ReadCompressedUInt(); // low bounds
                        sb.Append(":r=").Append(rank).Append('>');
                        break;
                    }

                case SigElementType.PTR:
                    sb.Append("*<");
                    AppendTypeKey(sb, ref r);
                    sb.Append('>');
                    break;

                case SigElementType.BYREF:
                    sb.Append("&<");
                    AppendTypeKey(sb, ref r);
                    sb.Append('>');
                    break;

                case SigElementType.VAR:
                case SigElementType.MVAR:
                    {
                        uint ord = r.ReadCompressedUInt();
                        sb.Append("#").Append(ord);
                        break;
                    }

                case SigElementType.GENERICINST:
                    {
                        var kind = (SigElementType)r.ReadByte();
                        sb.Append("{k=").Append((byte)kind);

                        uint coded = r.ReadCompressedUInt();
                        AppendTypeDefOrRefKey(sb, coded);

                        uint argc = r.ReadCompressedUInt();
                        sb.Append(",a=").Append(argc).Append(",args=[");
                        for (int i = 0; i < argc; i++)
                            AppendTypeKey(sb, ref r);
                        sb.Append("]}");
                        break;
                    }
            }

            sb.Append(';');
        }
        private void AppendTypeDefOrRefKey(StringBuilder sb, uint encoded)
        {
            int tag = (int)(encoded & 0x3u);
            int rid = (int)(encoded >> 2);

            // tag: 0=TypeDef, 1=TypeRef, 2=TypeSpec
            if (tag == 0)
            {
                var (ns, name) = GetTypeDefFullNameByRid(rid); // nested aware
                sb.Append(":").Append(Name).Append(':').Append(ns).Append('.').Append(name);
                return;
            }

            if (tag == 1)
            {
                var (asm, ns, name) = Domain.ResolveTypeRefFullName(this, rid); // nested aware
                sb.Append(":").Append(asm).Append(':').Append(ns).Append('.').Append(name);
                return;
            }

            if (tag == 2)
            {
                var ts = Md.GetTypeSpec(rid);
                var sig = Md.GetBlob(ts.Signature);
                var r2 = new SigReader(sig);
                sb.Append(":").Append(Name).Append(":typespec<");
                AppendTypeKey(sb, ref r2);
                sb.Append('>');
                return;
            }

            throw new NotSupportedException("Bad TypeDefOrRef tag.");
        }
        public (string ns, string name) GetTypeDefFullNameByRid(int rid)
        {
            if (_fullTypeNameCache.TryGetValue(rid, out var v))
                return v;

            var td = Md.GetTypeDef(rid);
            string name = Md.GetString(td.Name);
            string ns = Md.GetString(td.Namespace);

            if (_enclosingByNestedRid.TryGetValue(rid, out int encRid))
            {
                var enc = GetTypeDefFullNameByRid(encRid);
                ns = enc.ns;
                name = enc.name + "+" + name; // stable nesting separator
            }

            v = (ns, name);
            _fullTypeNameCache[rid] = v;
            return v;
        }
        void BuildTypeIndex()
        {
            _enclosingByNestedRid.Clear();
            for (int i = 0; i < Md.GetRowCount(MetadataTableKind.NestedClass); i++)
                _enclosingByNestedRid[Md.GetNestedClass(i + 1).NestedTypeRid] = Md.GetNestedClass(i + 1).EnclosingTypeRid;

            for (int i = 0; i < Md.GetRowCount(MetadataTableKind.TypeDef); i++)
            {
                int rid = i + 1;
                int typeTok = MetadataToken.Make(MetadataToken.TypeDef, rid);
                var (ns, name) = GetTypeDefFullNameByRid(rid);
                TypeDefByFullName[(ns, name)] = typeTok;
            }
        }

        void BuildMethodIndex()
        {
            for (int td = 0; td < Md.GetRowCount(MetadataTableKind.TypeDef); td++)
            {
                int typeTok = MetadataToken.Make(MetadataToken.TypeDef, td + 1);
                int startRid = Md.GetTypeDef(td + 1).MethodList;
                int endRid = (td + 1 < Md.GetRowCount(MetadataTableKind.TypeDef))
                    ? Md.GetTypeDef(td + 2).MethodList
                    : (Md.GetRowCount(MetadataTableKind.MethodDef) + 1);

                for (int rid = startRid; rid < endRid; rid++)
                {
                    int methodTok = MetadataToken.Make(MetadataToken.MethodDef, rid);
                    var mrow = Md.GetMethodDef(rid);
                    var name = Md.GetString(mrow.Name);

                    string sigKey = GetSigKey(mrow.Signature);

                    MethodDefIndex[(typeTok, name, sigKey)] = methodTok;
                }
            }
        }

        public string GetSignatureKeyFromThisModule(int sigBlobIdx) => GetSigKey(sigBlobIdx);
    }
    internal ref struct SigReader
    {
        private readonly ReadOnlySpan<byte> _s;
        private int _i;
        public SigReader(ReadOnlySpan<byte> s) { _s = s; _i = 0; }

        public byte ReadByte()
        {
            if ((uint)_i >= (uint)_s.Length) throw new InvalidOperationException("Signature underflow.");
            return _s[_i++];
        }

        public uint ReadCompressedUInt()
        {
            byte b0 = ReadByte();
            if ((b0 & 0x80) == 0) return b0;

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
    sealed class Domain
    {
        private readonly Dictionary<string, RuntimeModule> _modulesByName = new(StringComparer.Ordinal);

        public void Add(RuntimeModule m) => _modulesByName[m.Name] = m;

        public (RuntimeModule? module, Cnidaria.Cs.BytecodeFunction fn) ResolveCall(RuntimeModule caller, int methodToken)
        {
            int table = MetadataToken.Table(methodToken);
            int rid = MetadataToken.Rid(methodToken);

            if (table == MetadataToken.MethodSpec)
            {
                var ms = caller.Md.GetMethodSpec(rid);
                return ResolveCall(caller, ms.Method);
            }
            if (table == MetadataToken.MethodDef)
            {
                if (!caller.MethodsByDefToken.TryGetValue(methodToken, out var fn))
                    throw new MissingMethodException($"No body for MethodDef 0x{methodToken:X8} in {caller.Name}");
                return (default, fn);
            }

            if (table != MetadataToken.MemberRef)
                throw new NotSupportedException($"Call token table not supported: 0x{methodToken:X8}");

            var mr = caller.Md.GetMemberRef(rid);
            var methodName = caller.Md.GetString(mr.Name);

            string sigKey = caller.GetSignatureKeyFromThisModule(mr.Signature);

            var (asmName, ns, typeName) = ResolveMemberRefClass(caller, mr.ClassToken);

            if (!_modulesByName.TryGetValue(asmName, out var targetModule))
                throw new TypeLoadException($"Assembly '{asmName}' not loaded");

            if (!targetModule.TypeDefByFullName.TryGetValue((ns, typeName), out var typeDefTok))
                throw new TypeLoadException($"Type '{ns}.{typeName}' not found in '{asmName}'");

            if (!targetModule.MethodDefIndex.TryGetValue((typeDefTok, methodName, sigKey), out var methodDefTok))
            {
                if (!TryResolveMethodDefByCompatibleSignature(
                    memberRefModule: caller,
                    targetModule: targetModule,
                    ownerTypeDefToken: typeDefTok,
                    methodName: methodName,
                    memberRefSigBlob: mr.Signature,
                    methodDefToken: out methodDefTok))
                {
                    throw new MissingMethodException(
                        $"{ns}.{typeName}.{methodName} (sigKey={sigKey}) not found in '{asmName}'");
                }
                targetModule.MethodDefIndex[(typeDefTok, methodName, sigKey)] = methodDefTok;
            }

            if (!targetModule.MethodsByDefToken.TryGetValue(methodDefTok, out var fn2))
                throw new MissingMethodException($"No body for resolved MethodDef 0x{methodDefTok:X8} in '{asmName}'");

            return (targetModule, fn2);
        }
        private static bool TryResolveMethodDefByCompatibleSignature(
            RuntimeModule memberRefModule,
            RuntimeModule targetModule,
            int ownerTypeDefToken,
            string methodName,
            int memberRefSigBlob,
            out int methodDefToken)
        {
            methodDefToken = 0;
            if (MetadataToken.Table(ownerTypeDefToken) != MetadataToken.TypeDef)
                return false;

            int ownerRid = MetadataToken.Rid(ownerTypeDefToken);
            var md = targetModule.Md;

            int startRid = md.GetTypeDef(ownerRid).MethodList;
            int endRid = (ownerRid < md.GetRowCount(MetadataTableKind.TypeDef))
                ? md.GetTypeDef(ownerRid + 1).MethodList
                : (md.GetRowCount(MetadataTableKind.MethodDef) + 1);

            for (int rid = startRid; rid < endRid; rid++)
            {
                var row = md.GetMethodDef(rid);
                if (!StringComparer.Ordinal.Equals(md.GetString(row.Name), methodName))
                    continue;

                if (IsSignatureCompatible(targetModule, row.Signature, memberRefModule, memberRefSigBlob))
                {
                    methodDefToken = MetadataToken.Make(MetadataToken.MethodDef, rid);
                    return true;
                }
            }

            return false;
        }
        private static bool IsSignatureCompatible(
            RuntimeModule defModule,
            int defSigBlob,
            RuntimeModule memberRefModule,
            int memberRefSigBlob)
        {
            var defSig = defModule.Md.GetBlob(defSigBlob);
            var mrSig = memberRefModule.Md.GetBlob(memberRefSigBlob);

            var d = new SigReader(defSig);
            var m = new SigReader(mrSig);

            byte dCc = d.ReadByte();
            byte mCc = m.ReadByte();
            if (dCc != mCc)
                return false;

            if (((dCc | mCc) & 0x10) != 0)
            {
                if ((dCc & 0x10) == 0 || (mCc & 0x10) == 0)
                    return false;
                if (d.ReadCompressedUInt() != m.ReadCompressedUInt())
                    return false;
            }

            uint dPc = d.ReadCompressedUInt();
            uint mPc = m.ReadCompressedUInt();
            if (dPc != mPc)
                return false;

            if (!MatchType(defModule, ref d, memberRefModule, ref m))
                return false;

            for (int i = 0; i < dPc; i++)
            {
                if (!MatchType(defModule, ref d, memberRefModule, ref m))
                    return false;
            }

            return true;
        }
        private static bool MatchType(
            RuntimeModule defModule,
            ref SigReader def,
            RuntimeModule memberRefModule,
            ref SigReader mr)
        {
            var dEt = (SigElementType)def.ReadByte();
            var mEt = (SigElementType)mr.ReadByte();

            if (dEt == SigElementType.VAR || dEt == SigElementType.MVAR)
            {
                _ = def.ReadCompressedUInt();
                SkipType(mEt, ref mr);
                return true;
            }

            if ((dEt == SigElementType.CLASS || dEt == SigElementType.VALUETYPE) &&
                mEt == SigElementType.GENERICINST)
            {
                var mKind = (SigElementType)mr.ReadByte();
                if (mKind != dEt)
                    return false;

                int dTok = DecodeTypeDefOrRefEncodedToToken((int)def.ReadCompressedUInt());
                int mOwnerTok = DecodeTypeDefOrRefEncodedToToken((int)mr.ReadCompressedUInt());

                var dName = ResolveTypeTokenFullName(defModule, dTok);
                var mOwner = ResolveTypeTokenFullName(memberRefModule, mOwnerTok);
                if (dName != mOwner)
                    return false;

                uint mArgc = mr.ReadCompressedUInt();
                for (int i = 0; i < mArgc; i++)
                    SkipType((SigElementType)mr.ReadByte(), ref mr);

                return true;
            }

            if (dEt != mEt)
                return false;

            switch (dEt)

            {
                case SigElementType.CLASS:
                case SigElementType.VALUETYPE:
                    {
                        int dTok = DecodeTypeDefOrRefEncodedToToken((int)def.ReadCompressedUInt());
                        int mTok = DecodeTypeDefOrRefEncodedToToken((int)mr.ReadCompressedUInt());
                        var dName = ResolveTypeTokenFullName(defModule, dTok);
                        var mName = ResolveTypeTokenFullName(memberRefModule, mTok);
                        return dName == mName;
                    }

                case SigElementType.SZARRAY:
                case SigElementType.PTR:
                case SigElementType.BYREF:
                    return MatchType(defModule, ref def, memberRefModule, ref mr);

                case SigElementType.ARRAY:
                    {
                        if (!MatchType(defModule, ref def, memberRefModule, ref mr))
                            return false;

                        uint dRank = def.ReadCompressedUInt();
                        uint mRank = mr.ReadCompressedUInt();
                        if (dRank != mRank)
                            return false;

                        uint dNsizes = def.ReadCompressedUInt();
                        uint mNsizes = mr.ReadCompressedUInt();
                        if (dNsizes != mNsizes)
                            return false;
                        for (int i = 0; i < dNsizes; i++)
                            if (def.ReadCompressedUInt() != mr.ReadCompressedUInt())
                                return false;

                        uint dNlb = def.ReadCompressedUInt();
                        uint mNlb = mr.ReadCompressedUInt();
                        if (dNlb != mNlb)
                            return false;
                        for (int i = 0; i < dNlb; i++)
                            if (def.ReadCompressedUInt() != mr.ReadCompressedUInt())
                                return false;

                        return true;
                    }

                case SigElementType.GENERICINST:
                    {
                        var dKind = (SigElementType)def.ReadByte();
                        var mKind = (SigElementType)mr.ReadByte();
                        if (dKind != mKind)
                            return false;

                        int dOwnerTok = DecodeTypeDefOrRefEncodedToToken((int)def.ReadCompressedUInt());
                        int mOwnerTok = DecodeTypeDefOrRefEncodedToToken((int)mr.ReadCompressedUInt());
                        var dOwner = ResolveTypeTokenFullName(defModule, dOwnerTok);
                        var mOwner = ResolveTypeTokenFullName(memberRefModule, mOwnerTok);
                        if (dOwner != mOwner)
                            return false;

                        uint dArgc = def.ReadCompressedUInt();
                        uint mArgc = mr.ReadCompressedUInt();
                        if (dArgc != mArgc)
                            return false;

                        for (int i = 0; i < dArgc; i++)
                            if (!MatchType(defModule, ref def, memberRefModule, ref mr))
                                return false;

                        return true;
                    }
            }

            return true;
        }
        private static void SkipType(SigElementType et, ref SigReader r)
        {
            switch (et)
            {
                case SigElementType.CLASS:
                case SigElementType.VALUETYPE:
                    _ = r.ReadCompressedUInt();
                    return;

                case SigElementType.SZARRAY:
                case SigElementType.PTR:
                case SigElementType.BYREF:
                    SkipType((SigElementType)r.ReadByte(), ref r);
                    return;

                case SigElementType.ARRAY:
                    SkipType((SigElementType)r.ReadByte(), ref r);
                    _ = r.ReadCompressedUInt();
                    uint nsizes = r.ReadCompressedUInt();
                    for (int i = 0; i < nsizes; i++) _ = r.ReadCompressedUInt();
                    uint nlb = r.ReadCompressedUInt();
                    for (int i = 0; i < nlb; i++) _ = r.ReadCompressedUInt();
                    return;

                case SigElementType.GENERICINST:
                    _ = r.ReadByte();
                    _ = r.ReadCompressedUInt();
                    uint argc = r.ReadCompressedUInt();
                    for (int i = 0; i < argc; i++)
                        SkipType((SigElementType)r.ReadByte(), ref r);
                    return;

                case SigElementType.VAR:
                case SigElementType.MVAR:
                    _ = r.ReadCompressedUInt();
                    return;

                default:
                    return;
            }
        }
        public static (string asm, string ns, string name) ResolveTypeRefFullName(RuntimeModule caller, int typeRefRid)
        {
            var tr = caller.Md.GetTypeRef(typeRefRid);
            string name = caller.Md.GetString(tr.Name);
            string ns = caller.Md.GetString(tr.Namespace);

            int scopeTok = tr.ResolutionScopeToken;
            int scopeTable = MetadataToken.Table(scopeTok);
            int scopeRid = MetadataToken.Rid(scopeTok);

            if (scopeTable == MetadataToken.AssemblyRef)
            {
                var ar = caller.Md.GetAssemblyRef(scopeRid);
                string asm = caller.Md.GetString(ar.Name);
                return (asm, ns, name);
            }

            if (scopeTable == MetadataToken.TypeRef)
            {
                var enc = ResolveTypeRefFullName(caller, scopeRid);
                return (enc.asm, enc.ns, enc.name + "+" + name);
            }

            if (scopeTable == MetadataToken.TypeDef)
            {
                var (encNs, encName) = GetTypeDefFullNameByRid(caller, scopeRid);
                return (caller.Name, encNs, encName + "+" + name);
            }

            if (scopeTable == MetadataToken.TypeSpec)
            {
                var ts = caller.Md.GetTypeSpec(scopeRid);
                var sig = caller.Md.GetBlob(ts.Signature);
                var sr = new SigReader(sig);
                var enc = ResolveTypeSpecOwner(caller, ref sr);
                return (enc.asm, enc.ns, enc.name + "+" + name);
            }

            throw new NotSupportedException($"Unsupported TypeRef scope token: 0x{scopeTok:X8}");
        }
        public static (string ns, string name) GetTypeDefFullNameByRid(RuntimeModule m, int rid)
        {
            var enclosingByNestedRid = new Dictionary<int, int>();
            for (int i = 0; i < m.Md.GetRowCount(MetadataTableKind.NestedClass); i++)
                enclosingByNestedRid[m.Md.GetNestedClass(i + 1).NestedTypeRid] = m.Md.GetNestedClass(i + 1).EnclosingTypeRid;

            var td = m.Md.GetTypeDef(rid);
            string name = m.Md.GetString(td.Name);
            string ns = m.Md.GetString(td.Namespace);

            if (enclosingByNestedRid.TryGetValue(rid, out int encRid))
            {
                var enc = GetTypeDefFullNameByRid(m, encRid);
                return (enc.ns, enc.name + "+" + name);
            }

            return (ns, name);
        }
        static (string asm, string ns, string name) ResolveMemberRefClass(RuntimeModule caller, int classToken)
        {
            int table = MetadataToken.Table(classToken);
            int rid = MetadataToken.Rid(classToken);

            if (table == MetadataToken.TypeRef)
            {
                return ResolveTypeRefFullName(caller, rid);
            }

            if (table == MetadataToken.TypeDef)
            {
                var (ns, name) = GetTypeDefFullNameByRid(caller, rid);
                return (caller.Name, ns, name);
            }

            if (table == MetadataToken.TypeSpec)
            {
                var ts = caller.Md.GetTypeSpec(rid);
                var sig = caller.Md.GetBlob(ts.Signature);
                var sr = new SigReader(sig);
                return ResolveTypeSpecOwner(caller, ref sr);
            }

            throw new NotSupportedException($"MemberRef.Class token not supported: 0x{classToken:X8}");
        }
        private static (string asm, string ns, string name) ResolveTypeSpecOwner(RuntimeModule caller, ref SigReader sr)
        {
            var et = (SigElementType)sr.ReadByte();

            if (et == SigElementType.GENERICINST)
            {
                var kind = (SigElementType)sr.ReadByte();
                if (kind != SigElementType.CLASS && kind != SigElementType.VALUETYPE)
                    throw new BadImageFormatException($"GENERICINST owner has invalid kind '{kind}'.");

                uint coded = sr.ReadCompressedUInt();
                int defTok = DecodeTypeDefOrRefEncodedToToken((int)coded);
                return ResolveTypeTokenFullName(caller, defTok);
            }

            if (et == SigElementType.CLASS || et == SigElementType.VALUETYPE)
            {
                uint coded = sr.ReadCompressedUInt();
                int tok = DecodeTypeDefOrRefEncodedToToken((int)coded);
                return ResolveTypeTokenFullName(caller, tok);
            }

            throw new NotSupportedException($"Unsupported TypeSpec as MemberRef owner: {et}");
        }
        private static (string asm, string ns, string name) ResolveTypeTokenFullName(RuntimeModule caller, int tok)
        {
            int table = MetadataToken.Table(tok);
            int rid = MetadataToken.Rid(tok);

            if (table == MetadataToken.TypeRef)
                return ResolveTypeRefFullName(caller, rid);

            if (table == MetadataToken.TypeDef)
            {
                var (ns, name) = GetTypeDefFullNameByRid(caller, rid);
                return (caller.Name, ns, name);
            }

            if (table == MetadataToken.TypeSpec)
            {
                var ts = caller.Md.GetTypeSpec(rid);
                var sig = caller.Md.GetBlob(ts.Signature);
                var sr = new SigReader(sig);
                return ResolveTypeSpecOwner(caller, ref sr);
            }

            throw new NotSupportedException($"Unsupported type token in TypeSpec owner: 0x{tok:X8}");
        }
        private static int DecodeTypeDefOrRefEncodedToToken(int encoded)
        {
            int tag = encoded & 0x3;
            int rid = encoded >> 2;
            return tag switch
            {
                0 => MetadataToken.Make(MetadataToken.TypeDef, rid),
                1 => MetadataToken.Make(MetadataToken.TypeRef, rid),
                2 => MetadataToken.Make(MetadataToken.TypeSpec, rid),
                _ => throw new InvalidOperationException("Bad TypeDefOrRef coded index")
            };
        }
    }

    internal static class TargetArchitecture
    {
        public const int PointerSize = 4;
        public const int NativeIntSize = PointerSize;
        public const int GeneralRegisterSize = 8;
        public const int FloatingRegisterSize = 8;
        public const int StackSlotSize = 8;
        public const int StackAlignment = 8;
        public const int CallFrameAlignment = 16;
    }
    internal sealed class RuntimeTypeSystem
    {
        private readonly IReadOnlyDictionary<string, RuntimeModule> _modules;

        private readonly Dictionary<(string mod, int tok), RuntimeType> _typeCache = new();
        private readonly Dictionary<(string mod, int tok), RuntimeField> _fieldCache = new();
        private readonly Dictionary<(string mod, int tok), RuntimeMethod> _methodCache = new();

        private readonly Dictionary<(string asm, string ns, string name), RuntimeType> _namedTypes =
            new Dictionary<(string asm, string ns, string name), RuntimeType>();

        private readonly Dictionary<int, RuntimeType> _typeById = new();
        private readonly Dictionary<int, RuntimeMethod> _methodById = new();

        private int _nextTypeId = 1;
        private int _nextFieldId = 1;
        private int _nextMethodId = 1;

        

        private const int ObjectHeaderSize = TargetArchitecture.PointerSize * 2;
        private readonly HashSet<int> _layoutDone = new();
        public RuntimeType SystemObject { get; private set; } = null!;
        public RuntimeType SystemString { get; private set; } = null!;
        public RuntimeType SystemArray { get; private set; } = null!;
        public RuntimeType SystemValueType { get; private set; } = null!;
        public RuntimeType SystemEnum { get; private set; } = null!;
        private readonly Dictionary<string, RuntimeType> _constructedTypes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RuntimeMethod> _constructedMethods = new(StringComparer.Ordinal);
        public RuntimeTypeSystem(IReadOnlyDictionary<string, RuntimeModule> modules)
        {
            _modules = modules ?? throw new ArgumentNullException(nameof(modules));

            PrecreateAllTypeDefs();
            BindBaseTypes();
            BindInterfaces();
            IndexWellKnownCoreTypes();
            BuildAllMembers();
            BindMethodImpls();
            foreach (var t in _typeCache.Values)
                EnsureLayout(t);
            BuildAllVTables();
        }
        public RuntimeMethod ResolveMethod(RuntimeModule module, int methodToken)
        {
            if (_methodCache.TryGetValue((module.Name, methodToken), out var cached))
                return cached;

            int table = MetadataToken.Table(methodToken);
            int rid = MetadataToken.Rid(methodToken);

            if (table == MetadataToken.MethodDef)
                return _methodCache[(module.Name, methodToken)];

            if (table == MetadataToken.MethodSpec)
            {
                var resolved = ResolveMethodSpec(module, rid);
                _methodCache[(module.Name, methodToken)] = resolved;
                return resolved;
            }

            if (table != MetadataToken.MemberRef)
                throw new NotSupportedException($"Method token table not supported: 0x{methodToken:X8}");

            var mr = module.Md.GetMemberRef(rid);
            string methodName = module.Md.GetString(mr.Name);

            RuntimeType owner = ResolveMemberRefOwnerType(module, mr.ClassToken);
            EnsureConstructedMembers(owner);

            var sig = module.Md.GetBlob(mr.Signature);
            var sr = new SigReader(sig);
            byte cc = sr.ReadByte();
            bool hasThis = (cc & 0x20) != 0;

            int genericArity = 0;
            if ((cc & 0x10) != 0)
            {
                genericArity = checked((int)sr.ReadCompressedUInt());
            }

            uint paramCount = sr.ReadCompressedUInt();
            RuntimeType ret = ReadTypeSig(module, ref sr);

            var ps = new RuntimeType[paramCount];
            for (int i = 0; i < ps.Length; i++)
                ps[i] = ReadTypeSig(module, ref sr);

            var ownerTypeArgs = owner.GenericTypeArguments ?? Array.Empty<RuntimeType>();
            if (ownerTypeArgs.Length != 0)
            {
                ret = SubstituteRuntimeType(ret, ownerTypeArgs);
                for (int i = 0; i < ps.Length; i++)
                    ps[i] = SubstituteRuntimeType(ps[i], ownerTypeArgs);
            }



            RuntimeMethod? wildcardMatch = null;

            for (int i = 0; i < owner.Methods.Length; i++)
            {
                var m = owner.Methods[i];
                if (!StringComparer.Ordinal.Equals(m.Name, methodName))
                    continue;
                if (m.HasThis != hasThis)
                    continue;
                if (m.GenericArity != genericArity)
                    continue;
                if (m.ParameterTypes.Length != ps.Length)
                    continue;

                bool strict = ReferenceEquals(m.ReturnType, ret);
                if (strict)
                {
                    for (int p = 0; p < ps.Length; p++)
                    {
                        if (!ReferenceEquals(m.ParameterTypes[p], ps[p]))
                        {
                            strict = false;
                            break;
                        }
                    }
                }

                if (strict)
                {
                    _methodCache[(module.Name, methodToken)] = m;
                    return m;
                }

                if (wildcardMatch is null && CompatibleType(m.ReturnType, ret))
                {
                    bool ok = true;
                    for (int p = 0; p < ps.Length; p++)
                    {
                        if (!CompatibleType(m.ParameterTypes[p], ps[p]))
                        {
                            ok = false;
                            break;
                        }
                    }
                    if (ok)
                        wildcardMatch = m;
                }
            }

            if (wildcardMatch is not null)
            {
                _methodCache[(module.Name, methodToken)] = wildcardMatch;
                return wildcardMatch;
            }

            throw new MissingMethodException($"{owner.Namespace}.{owner.Name}.{methodName} not found (memberref in {module.Name})");

            static bool CompatibleType(RuntimeType def, RuntimeType actual)
            {
                if (def.Kind == RuntimeTypeKind.TypeParam)
                    return true;

                if (ReferenceEquals(def, actual) || def.TypeId == actual.TypeId)
                    return true;

                if (def.Kind != actual.Kind)
                    return false;

                if (def.Kind == RuntimeTypeKind.Array)
                {
                    if (def.ArrayRank != actual.ArrayRank)
                        return false;
                    if (def.ElementType is null || actual.ElementType is null)
                        return false;
                    return CompatibleType(def.ElementType, actual.ElementType);
                }

                if (def.Kind == RuntimeTypeKind.Pointer || def.Kind == RuntimeTypeKind.ByRef)
                {
                    if (def.ElementType is null || actual.ElementType is null)
                        return false;
                    return CompatibleType(def.ElementType, actual.ElementType);
                }

                if (def.GenericTypeDefinition is not null)
                {
                    if (actual.GenericTypeDefinition is null)
                        return false;
                    if (!ReferenceEquals(def.GenericTypeDefinition, actual.GenericTypeDefinition))
                        return false;
                    var da = def.GenericTypeArguments;
                    var aa = actual.GenericTypeArguments;
                    if (da.Length != aa.Length)
                        return false;
                    for (int i = 0; i < da.Length; i++)
                        if (!CompatibleType(da[i], aa[i]))
                            return false;
                    return true;
                }

                return false;
            }
        }
        public RuntimeMethod ResolveMethodInMethodContext(RuntimeModule module, int methodToken, RuntimeMethod? methodContext)
        {
            if (methodContext is null)
                return ResolveMethod(module, methodToken);

            int table = MetadataToken.Table(methodToken);
            int rid = MetadataToken.Rid(methodToken);

            if (table == MetadataToken.MethodDef)
            {
                var m = ResolveMethod(module, methodToken);

                return TryProjectMethodDefFromContext(m, methodContext);
            }

            if (table == MetadataToken.MethodSpec)
            {
                var ms = module.Md.GetMethodSpec(rid);

                var genericMethod = ResolveMethodInMethodContext(module, ms.Method, methodContext);

                var sig = module.Md.GetBlob(ms.Instantiation);
                var sr = new SigReader(sig);
                byte kind = sr.ReadByte();
                if (kind != 0x0A) // METHODSPEC
                    throw new InvalidOperationException($"Bad MethodSpec signature kind: 0x{kind:X2}");

                uint argcU = sr.ReadCompressedUInt();
                int argc = checked((int)argcU);
                var methodArgs = new RuntimeType[argc];
                for (int i = 0; i < methodArgs.Length; i++)
                    methodArgs[i] = ReadTypeSig(module, ref sr);

                var ctxOwnerArgs = methodContext.DeclaringType.GenericTypeArguments;
                var ctxMethodArgs = methodContext.MethodGenericArguments;
                if (ctxOwnerArgs.Length != 0 || ctxMethodArgs.Length != 0)
                {
                    for (int i = 0; i < methodArgs.Length; i++)
                        methodArgs[i] = SubstituteRuntimeType(methodArgs[i], ctxOwnerArgs, ctxMethodArgs);
                }

                if (methodArgs.Length != 0 &&
                    LooksLikeOpenGenericDefinition(genericMethod.DeclaringType) &&
                    UsesOwnerTypeParameters(genericMethod) &&
                    !UsesMethodTypeParameters(genericMethod) &&
                    MethodSpecArgsMatchDeclaringTypeArity(genericMethod.DeclaringType, methodArgs.Length))
                {
                    var constructedOwner = GetOrCreateGenericInstanceType(genericMethod.DeclaringType, methodArgs);
                    EnsureConstructedMembers(constructedOwner);

                    var expectedRet = SubstituteRuntimeType(genericMethod.ReturnType, methodArgs);
                    var expectedPs = new RuntimeType[genericMethod.ParameterTypes.Length];
                    for (int i = 0; i < expectedPs.Length; i++)
                        expectedPs[i] = SubstituteRuntimeType(genericMethod.ParameterTypes[i], methodArgs);

                    for (int i = 0; i < constructedOwner.Methods.Length; i++)
                    {
                        var cand = constructedOwner.Methods[i];
                        if (!StringComparer.Ordinal.Equals(cand.Name, genericMethod.Name))
                            continue;
                        if (cand.HasThis != genericMethod.HasThis) continue;
                        if (cand.IsStatic != genericMethod.IsStatic) continue;
                        if (cand.IsVirtual != genericMethod.IsVirtual) continue;
                        if (cand.IsNewSlot != genericMethod.IsNewSlot) continue;
                        if (cand.IsFinal != genericMethod.IsFinal) continue;
                        if (!ReferenceEquals(cand.ReturnType, expectedRet))
                            continue;
                        if (cand.ParameterTypes.Length != expectedPs.Length)
                            continue;

                        bool same = true;
                        for (int p = 0; p < expectedPs.Length; p++)
                        {
                            if (!ReferenceEquals(cand.ParameterTypes[p], expectedPs[p]))
                            {
                                same = false;
                                break;
                            }
                        }

                        if (same)
                            return cand;
                    }
                }

                return GetOrCreateConstructedMethod(genericMethod, methodArgs);
            }

            if (table != MetadataToken.MemberRef)
                return ResolveMethod(module, methodToken);

            {
                var resolved = ResolveMethod(module, methodToken);

                var ctxOwnerArgs = methodContext.DeclaringType.GenericTypeArguments;
                var ctxMethodArgs = methodContext.MethodGenericArguments;

                resolved = BindMethodToReceiver(resolved, methodContext.DeclaringType);

                if (ctxOwnerArgs.Length != 0 &&
                    resolved.DeclaringType.GenericTypeDefinition is null &&
                    TypeUsesOwnerTypeParameters(resolved.DeclaringType))
                {
                    var constructedOwner = GetOrCreateGenericInstanceType(resolved.DeclaringType, ctxOwnerArgs);
                    EnsureConstructedMembers(constructedOwner);
                    resolved = BindMethodToReceiver(resolved, constructedOwner);
                }

                if (ctxMethodArgs.Length != 0 &&
                    (resolved.GenericArity != 0 || UsesMethodTypeParameters(resolved)))
                {
                    return GetOrCreateConstructedMethod(resolved, ctxMethodArgs);
                }

                return resolved;
            }




            static bool CompatibleType(RuntimeType def, RuntimeType actual)
            {
                if (def.Kind == RuntimeTypeKind.TypeParam)
                    return true;

                if (ReferenceEquals(def, actual) || def.TypeId == actual.TypeId)
                    return true;

                if (def.Kind != actual.Kind)
                    return false;

                if (def.Kind == RuntimeTypeKind.Array)
                {
                    if (def.ArrayRank != actual.ArrayRank)
                        return false;
                    if (def.ElementType is null || actual.ElementType is null)
                        return false;
                    return CompatibleType(def.ElementType, actual.ElementType);
                }

                if (def.Kind == RuntimeTypeKind.Pointer || def.Kind == RuntimeTypeKind.ByRef)
                {
                    if (def.ElementType is null || actual.ElementType is null)
                        return false;
                    return CompatibleType(def.ElementType, actual.ElementType);
                }

                if (def.GenericTypeDefinition is not null)
                {
                    if (actual.GenericTypeDefinition is null)
                        return false;
                    if (!ReferenceEquals(def.GenericTypeDefinition, actual.GenericTypeDefinition))
                        return false;
                    var da = def.GenericTypeArguments;
                    var aa = actual.GenericTypeArguments;
                    if (da.Length != aa.Length)
                        return false;
                    for (int i = 0; i < da.Length; i++)
                        if (!CompatibleType(da[i], aa[i]))
                            return false;
                    return true;
                }

                return false;
            }
        }
        private RuntimeMethod TryProjectMethodDefFromContext(RuntimeMethod method, RuntimeMethod methodContext)
        {
            var ctxOwner = methodContext.DeclaringType;
            var ctxOwnerDef = ctxOwner.GenericTypeDefinition ?? ctxOwner;
            var ownerArgs = ctxOwner.GenericTypeArguments;

            if (ownerArgs.Length == 0)
                return method;

            var targetOwnerDef = method.DeclaringType;

            if (targetOwnerDef.GenericTypeDefinition is not null)
                return method;

            if (!TypeUsesOwnerTypeParameters(targetOwnerDef))
                return method;

            if (!StringComparer.Ordinal.Equals(targetOwnerDef.AssemblyName, ctxOwnerDef.AssemblyName) ||
                !StringComparer.Ordinal.Equals(targetOwnerDef.Namespace, ctxOwnerDef.Namespace))
            {
                return method;
            }

            bool sameOrNested =
                StringComparer.Ordinal.Equals(targetOwnerDef.Name, ctxOwnerDef.Name) ||
                targetOwnerDef.Name.StartsWith(ctxOwnerDef.Name + "+", StringComparison.Ordinal);

            if (!sameOrNested)
                return method;

            var constructedOwner = GetOrCreateGenericInstanceType(targetOwnerDef, ownerArgs);
            EnsureConstructedMembers(constructedOwner);

            // fast path
            var defMethods = targetOwnerDef.Methods;
            for (int i = 0; i < defMethods.Length; i++)
            {
                if (ReferenceEquals(defMethods[i], method))
                    return constructedOwner.Methods[i];
            }

            // fallback
            var expectedRet = SubstituteRuntimeType(method.ReturnType, ownerArgs);
            var expectedPs = new RuntimeType[method.ParameterTypes.Length];
            for (int i = 0; i < expectedPs.Length; i++)
                expectedPs[i] = SubstituteRuntimeType(method.ParameterTypes[i], ownerArgs);

            for (int i = 0; i < constructedOwner.Methods.Length; i++)
            {
                var cand = constructedOwner.Methods[i];
                if (!StringComparer.Ordinal.Equals(cand.Name, method.Name))
                    continue;
                if (cand.HasThis != method.HasThis) continue;
                if (cand.IsStatic != method.IsStatic) continue;
                if (cand.IsVirtual != method.IsVirtual) continue;
                if (cand.IsNewSlot != method.IsNewSlot) continue;
                if (cand.IsFinal != method.IsFinal) continue;
                if (cand.GenericArity != method.GenericArity) continue;
                if (!ReferenceEquals(cand.ReturnType, expectedRet))
                    continue;
                if (cand.ParameterTypes.Length != expectedPs.Length)
                    continue;

                bool same = true;
                for (int p = 0; p < expectedPs.Length; p++)
                {
                    if (!ReferenceEquals(cand.ParameterTypes[p], expectedPs[p]))
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                    return cand;
            }

            return method;
        }
        private void LayoutStaticFields(RuntimeType t)
        {
            if (t.StaticFields.Length == 0)
            {
                t.StaticSize = 0;
                t.StaticAlign = 1;
                return;
            }

            int offset = 0;
            int maxAlign = 1;

            for (int i = 0; i < t.StaticFields.Length; i++)
            {
                var f = t.StaticFields[i];
                var (fs, fa) = GetStorageSizeAlign(f.FieldType);
                offset = AlignUp(offset, fa);
                f.Offset = offset;
                offset += fs;
                if (fa > maxAlign) maxAlign = fa;
            }

            t.StaticAlign = maxAlign;
            t.StaticSize = AlignUp(offset, maxAlign);
        }
        public RuntimeMethod GetMethodById(int methodId)
        {
            if (_methodById.TryGetValue(methodId, out var m))
                return m;
            throw new MissingMethodException($"RuntimeMethod id not found: {methodId}");
        }
        private RuntimeMethod ResolveMethodSpec(RuntimeModule module, int methodSpecRid)
        {
            var ms = module.Md.GetMethodSpec(methodSpecRid);
            var genericMethod = ResolveMethod(module, ms.Method);

            var sig = module.Md.GetBlob(ms.Instantiation);
            var sr = new SigReader(sig);
            byte kind = sr.ReadByte();
            if (kind != 0x0A) // METHODSPEC
                throw new InvalidOperationException($"Bad MethodSpec signature kind: 0x{kind:X2}");

            uint argcU = sr.ReadCompressedUInt();
            int argc = checked((int)argcU);
            var methodArgs = new RuntimeType[argc];
            for (int i = 0; i < argc; i++)
                methodArgs[i] = ReadTypeSig(module, ref sr);

            if (methodArgs.Length != 0 &&
                LooksLikeOpenGenericDefinition(genericMethod.DeclaringType) &&
                UsesOwnerTypeParameters(genericMethod) &&
                !UsesMethodTypeParameters(genericMethod) &&
                MethodSpecArgsMatchDeclaringTypeArity(genericMethod.DeclaringType, methodArgs.Length))
            {
                var constructedOwner = GetOrCreateGenericInstanceType(genericMethod.DeclaringType, methodArgs);
                EnsureConstructedMembers(constructedOwner);

                // Find the corresponding method on the constructed owner type.
                var expectedRet = SubstituteRuntimeType(genericMethod.ReturnType, methodArgs);
                var expectedPs = new RuntimeType[genericMethod.ParameterTypes.Length];
                for (int i = 0; i < expectedPs.Length; i++)
                    expectedPs[i] = SubstituteRuntimeType(genericMethod.ParameterTypes[i], methodArgs);

                for (int i = 0; i < constructedOwner.Methods.Length; i++)
                {
                    var cand = constructedOwner.Methods[i];
                    if (!StringComparer.Ordinal.Equals(cand.Name, genericMethod.Name))
                        continue;
                    if (cand.HasThis != genericMethod.HasThis) continue;
                    if (cand.IsStatic != genericMethod.IsStatic) continue;
                    if (cand.IsVirtual != genericMethod.IsVirtual) continue;
                    if (cand.IsNewSlot != genericMethod.IsNewSlot) continue;
                    if (cand.IsFinal != genericMethod.IsFinal) continue;
                    if (!ReferenceEquals(cand.ReturnType, expectedRet))
                        continue;
                    if (cand.ParameterTypes.Length != expectedPs.Length)
                        continue;

                    bool same = true;
                    for (int p = 0; p < expectedPs.Length; p++)
                    {
                        if (!ReferenceEquals(cand.ParameterTypes[p], expectedPs[p]))
                        {
                            same = false;
                            break;
                        }
                    }

                    if (!same)
                        continue;

                    return cand;
                }
            }

            return GetOrCreateConstructedMethod(genericMethod, methodArgs);
        }
        private static bool LooksLikeOpenGenericDefinition(RuntimeType t)
        {
            if (t is null) return false;
            if (t.GenericTypeDefinition is not null) return false;
            if (t.GenericTypeArguments.Length != 0) return false;
            return ParseGenericArityFromName(t.Name) > 0;
        }
        private static int ParseGenericArityFromName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return 0;
            int tick = name.LastIndexOf('`');
            if (tick < 0 || tick + 1 >= name.Length)
                return 0;
            if (int.TryParse(name.AsSpan(tick + 1), out int arity))
                return arity;
            return 0;
        }
        private static bool MethodSpecArgsMatchDeclaringTypeArity(RuntimeType declaringType, int argCount)
        {
            int arity = ParseGenericArityFromName(declaringType.Name);
            return arity != 0 && arity == argCount;
        }
        private static bool UsesOwnerTypeParameters(RuntimeMethod m)
        {
            if (UsesTypeParameters(m.ReturnType, wantMethod: false))
                return true;
            for (int i = 0; i < m.ParameterTypes.Length; i++)
                if (UsesTypeParameters(m.ParameterTypes[i], wantMethod: false))
                    return true;
            return false;
        }
        private static bool UsesMethodTypeParameters(RuntimeMethod m)
        {
            if (m.GenericArity != 0)
                return true;
            if (UsesTypeParameters(m.ReturnType, wantMethod: true))
                return true;
            for (int i = 0; i < m.ParameterTypes.Length; i++)
                if (UsesTypeParameters(m.ParameterTypes[i], wantMethod: true))
                    return true;
            return false;
        }
        private static bool UsesTypeParameters(RuntimeType t, bool wantMethod)
        {
            if (t is null) return false;
            if (t.Kind == RuntimeTypeKind.TypeParam)
                return t.IsMethodGenericParameter == wantMethod;
            if (t.ElementType is not null)
                return UsesTypeParameters(t.ElementType, wantMethod);
            if (t.GenericTypeArguments.Length != 0)
            {
                for (int i = 0; i < t.GenericTypeArguments.Length; i++)
                    if (UsesTypeParameters(t.GenericTypeArguments[i], wantMethod))
                        return true;
            }
            return false;
        }
        private RuntimeMethod GetOrCreateConstructedMethod(RuntimeMethod genericMethod, RuntimeType[] methodArgs)
        {
            if (methodArgs.Length == 0)
                return genericMethod;

            string key = MakeConstructedMethodKey(genericMethod, methodArgs);
            if (_constructedMethods.TryGetValue(key, out var cached))
                return cached;

            var ownerTypeArgs = genericMethod.DeclaringType.GenericTypeArguments ?? Array.Empty<RuntimeType>();

            var declType = SubstituteRuntimeType(genericMethod.DeclaringType, ownerTypeArgs, methodArgs);
            var ret = SubstituteRuntimeType(genericMethod.ReturnType, ownerTypeArgs, methodArgs);

            var ps = new RuntimeType[genericMethod.ParameterTypes.Length];
            for (int i = 0; i < ps.Length; i++)
                ps[i] = SubstituteRuntimeType(genericMethod.ParameterTypes[i], ownerTypeArgs, methodArgs);

            var m = new RuntimeMethod(
                methodId: _nextMethodId++,
                declType: declType,
                name: genericMethod.Name,
                ret: ret,
                ps: ps,
                hasThis: genericMethod.HasThis,
                isVirtual: genericMethod.IsVirtual,
                isStatic: genericMethod.IsStatic,
                isNewSlot: genericMethod.IsNewSlot,
                isFinal: genericMethod.IsFinal,
                flags: genericMethod.Flags,
                implFlags: genericMethod.ImplFlags);

            m.BodyModule = genericMethod.BodyModule;
            m.Body = genericMethod.Body;
            m.GenericMethodDefinition = genericMethod;
            m.GenericArity = genericMethod.GenericArity;
            m.MethodGenericArguments = methodArgs;
            _constructedMethods[key] = m;
            _methodById[m.MethodId] = m;
            return m;
        }
        private static string MakeConstructedMethodKey(RuntimeMethod genericMethod, RuntimeType[] methodArgs)
        {
            var sb = new StringBuilder(32);
            sb.Append("gm:").Append(genericMethod.MethodId).Append('<');
            for (int i = 0; i < methodArgs.Length; i++)
            {
                if (i != 0) sb.Append(',');
                sb.Append(methodArgs[i].TypeId);
            }
            sb.Append('>');
            return sb.ToString();
        }
        public RuntimeType GetTypeById(int typeId)
        {
            if (!_typeById.TryGetValue(typeId, out var t))
                throw new TypeLoadException($"RuntimeType id {typeId} not found.");
            return t;
        }
        internal RuntimeType[] SnapshotKnownTypes()
        {
            var result = new RuntimeType[_typeById.Count];
            int index = 0;
            foreach (var pair in _typeById)
                result[index++] = pair.Value;
            return result;
        }
        public RuntimeType GetByRefType(RuntimeType elementType)
            => GetOrCreateByRefType(elementType);
        public RuntimeType GetArrayType(RuntimeType elementType)
        {
            if (elementType is null) throw new ArgumentNullException(nameof(elementType));

            var t = GetOrCreateArrayType(elementType);
            EnsureLayout(t);
            return t;
        }
        private void PrecreateAllTypeDefs()
        {
            foreach (var kv in _modules)
            {
                var m = kv.Value;
                for (int i = 0; i < m.Md.GetRowCount(MetadataTableKind.TypeDef); i++)
                {
                    int rid = i + 1;
                    int tok = MetadataToken.Make(MetadataToken.TypeDef, rid);

                    var (ns, name) = Domain.GetTypeDefFullNameByRid(m, rid);
                    var td = m.Md.GetTypeDef(i + 1);

                    RuntimeTypeKind kind = InferKindFromTypeDef(m, td);

                    var rt = new RuntimeType(_nextTypeId++, kind, asm: m.Name, ns: ns, name: name);
                    _typeCache[(m.Name, tok)] = rt;
                    _namedTypes[(m.Name, ns, name)] = rt;
                    _typeById[rt.TypeId] = rt;
                }
            }
        }
        private void EnsureLayout(RuntimeType? t)
        {
            if (t is null) return;
            EnsureConstructedMembers(t);
            if (_layoutDone.Contains(t.TypeId)) return;

            _layoutDone.Add(t.TypeId);
            if (TryGetPrimitiveLayout(t, out int primSize, out int primAlign))
            {
                t.SizeOf = primSize;
                t.AlignOf = primAlign;
                t.InstanceSize = t.IsReferenceType ? TargetArchitecture.PointerSize : primSize;
                t.ContainsGcPointers = false;
                t.GcPointerOffsets = Array.Empty<int>();
                LayoutStaticFields(t);
                if (!t.IsReferenceType && IsScalarPrimitiveWrapper(t))
                    BindPrimitiveWrapperBackingField(t);

                return;
            }

            switch (t.Kind)
            {
                case RuntimeTypeKind.TypeParam:
                    t.SizeOf = TargetArchitecture.PointerSize;
                    t.AlignOf = TargetArchitecture.PointerSize;
                    t.InstanceSize = TargetArchitecture.PointerSize;
                    t.ContainsGcPointers = true;
                    t.GcPointerOffsets = new[] { 0 };
                    LayoutStaticFields(t);
                    return;
                case RuntimeTypeKind.Pointer:
                    t.SizeOf = TargetArchitecture.PointerSize;
                    t.AlignOf = TargetArchitecture.PointerSize;
                    t.InstanceSize = TargetArchitecture.PointerSize;
                    t.ContainsGcPointers = false;
                    t.GcPointerOffsets = Array.Empty<int>();
                    LayoutStaticFields(t);
                    return;
                case RuntimeTypeKind.ByRef:
                    t.SizeOf = TargetArchitecture.PointerSize;
                    t.AlignOf = TargetArchitecture.PointerSize;
                    t.InstanceSize = TargetArchitecture.PointerSize;
                    t.ContainsGcPointers = true;
                    t.GcPointerOffsets = new[] { 0 };
                    LayoutStaticFields(t);
                    return;

                case RuntimeTypeKind.Array:
                case RuntimeTypeKind.Interface:
                case RuntimeTypeKind.Class:
                    {
                        t.SizeOf = TargetArchitecture.PointerSize;
                        t.AlignOf = TargetArchitecture.PointerSize;
                        t.ContainsGcPointers = true;
                        t.GcPointerOffsets = new[] { 0 };

                        EnsureLayout(t.BaseType);

                        int offset = (t.BaseType != null)
                            ? t.BaseType.InstanceSize
                            : ObjectHeaderSize;

                        int maxAlign = TargetArchitecture.PointerSize;

                        for (int i = 0; i < t.InstanceFields.Length; i++)
                        {
                            var f = t.InstanceFields[i];
                            var (fs, fa) = GetStorageSizeAlign(f.FieldType);
                            offset = AlignUp(offset, fa);
                            f.Offset = offset;
                            offset += fs;
                            if (fa > maxAlign) maxAlign = fa;
                        }
                        t.InstanceSize = AlignUp(offset, maxAlign);
                        LayoutStaticFields(t);
                        return;
                    }
                case RuntimeTypeKind.Struct:
                case RuntimeTypeKind.Enum:
                    {
                        int offset = 0;
                        int maxAlign = 1;

                        EnsureLayout(t.BaseType);

                        var gcOffsets = new List<int>();

                        for (int i = 0; i < t.InstanceFields.Length; i++)
                        {
                            var f = t.InstanceFields[i];
                            var (fs, fa) = GetStorageSizeAlign(f.FieldType);
                            offset = AlignUp(offset, fa);
                            f.Offset = offset;
                            AppendGcPointerOffsets(gcOffsets, offset, f.FieldType);
                            offset += fs;
                            if (fa > maxAlign) maxAlign = fa;
                        }

                        int size = AlignUp(offset, maxAlign);
                        if (size == 0) size = 1;
                        t.SizeOf = size;
                        t.AlignOf = maxAlign;
                        t.InstanceSize = size;
                        gcOffsets.Sort();
                        t.GcPointerOffsets = gcOffsets.ToArray();
                        t.ContainsGcPointers = t.GcPointerOffsets.Length != 0;
                        LayoutStaticFields(t);
                        return;
                    }
                default:
                    throw new NotSupportedException($"Layout for kind {t.Kind} not implemented");
            }
        }

        private void AppendGcPointerOffsets(List<int> offsets, int baseOffset, RuntimeType fieldType)
        {
            EnsureLayout(fieldType);

            if (fieldType.Kind == RuntimeTypeKind.Pointer)
                return;

            if (fieldType.IsReferenceType || fieldType.Kind is RuntimeTypeKind.ByRef or RuntimeTypeKind.TypeParam)
            {
                offsets.Add(baseOffset);
                return;
            }

            if (!fieldType.ContainsGcPointers)
                return;

            var nested = fieldType.GcPointerOffsets;
            for (int i = 0; i < nested.Length; i++)
                offsets.Add(baseOffset + nested[i]);
        }

        private static bool IsScalarPrimitiveWrapper(RuntimeType t)
        {
            if (t.Namespace != "System")
                return false;

            return t.Name is
                "Boolean" or "Char" or
                "SByte" or "Byte" or
                "Int16" or "UInt16" or
                "Int32" or "UInt32" or
                "Int64" or "UInt64" or
                "IntPtr" or "UIntPtr" or
                "Single" or "Double";
        }
        private void BindPrimitiveWrapperBackingField(RuntimeType t)
        {
            if (t.InstanceFields.Length == 0)
                return;

            if (t.InstanceFields.Length != 1)
                throw new TypeLoadException(
                    $"Primitive wrapper '{t.Namespace}.{t.Name}' may contain only one instance field.");

            var f = t.InstanceFields[0];

            bool validName =
                t.Name switch
                {
                    "IntPtr" or "UIntPtr" => f.Name is "_value" or "m_value",
                    _ => string.Equals(f.Name, "m_value", StringComparison.Ordinal)
                };

            if (!validName)
            {
                string expected = t.Name is "IntPtr" or "UIntPtr" ? "'_value' (or 'm_value')" : "'m_value'";
                throw new TypeLoadException(
                    $"Primitive wrapper '{t.Namespace}.{t.Name}' has unsupported instance field '{f.Name}'. Expected {expected}.");
            }

            if (f.FieldType.TypeId != t.TypeId)
                throw new TypeLoadException(
                    $"Primitive wrapper '{t.Namespace}.{t.Name}.{f.Name}' must have the same primitive type.");

            // Primitive wrappers are canonical scalars
            f.Offset = 0;
        }
        internal RuntimeField BindFieldToReceiver(RuntimeField field, RuntimeType receiverType)
        {
            if (field is null) throw new ArgumentNullException(nameof(field));
            if (receiverType is null) throw new ArgumentNullException(nameof(receiverType));

            for (RuntimeType? cur = receiverType; cur is not null; cur = cur.BaseType)
            {
                if (ReferenceEquals(field.DeclaringType, cur))
                    return field;

                var curGenericDef = cur.GenericTypeDefinition;
                if (curGenericDef is null)
                    continue;

                var fieldOwner = field.DeclaringType;

                bool sameGenericFamily =
                    ReferenceEquals(fieldOwner, curGenericDef) ||
                    (fieldOwner.GenericTypeDefinition is not null &&
                     ReferenceEquals(fieldOwner.GenericTypeDefinition, curGenericDef));

                if (!sameGenericFamily)
                    continue;

                EnsureConstructedMembers(cur);

                var sourceFields = field.IsStatic
                    ? fieldOwner.StaticFields
                    : fieldOwner.InstanceFields;

                var actualFields = field.IsStatic
                    ? cur.StaticFields
                    : cur.InstanceFields;

                for (int i = 0; i < sourceFields.Length && i < actualFields.Length; i++)
                {
                    if (ReferenceEquals(sourceFields[i], field))
                        return actualFields[i];
                }

                var expectedFieldType = SubstituteRuntimeType(field.FieldType, cur.GenericTypeArguments);

                for (int i = 0; i < actualFields.Length; i++)
                {
                    var af = actualFields[i];
                    if (!StringComparer.Ordinal.Equals(af.Name, field.Name))
                        continue;
                    if (!ReferenceEquals(af.FieldType, expectedFieldType))
                        continue;

                    return af;
                }
            }

            throw new InvalidOperationException(
                $"Field '{field.DeclaringType.Namespace}.{field.DeclaringType.Name}.{field.Name}' " +
                $"is not valid for object of type '{receiverType.Namespace}.{receiverType.Name}'.");
        }
        private RuntimeMethod BindMethodToReceiver(RuntimeMethod method, RuntimeType receiverType)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));
            if (receiverType is null) throw new ArgumentNullException(nameof(receiverType));

            for (RuntimeType? cur = receiverType; cur is not null; cur = cur.BaseType)
            {
                if (ReferenceEquals(method.DeclaringType, cur))
                    return method;

                var curGenericDef = cur.GenericTypeDefinition;
                if (curGenericDef is null)
                    continue;

                var methodOwner = method.DeclaringType;

                bool sameGenericFamily =
                    ReferenceEquals(methodOwner, curGenericDef) ||
                    (methodOwner.GenericTypeDefinition is not null &&
                     ReferenceEquals(methodOwner.GenericTypeDefinition, curGenericDef));

                if (!sameGenericFamily)
                    continue;

                EnsureConstructedMembers(cur);

                var sourceMethods = methodOwner.Methods;
                var actualMethods = cur.Methods;

                // fast path
                for (int i = 0; i < sourceMethods.Length && i < actualMethods.Length; i++)
                {
                    if (ReferenceEquals(sourceMethods[i], method))
                        return actualMethods[i];
                }

                // fallback
                var ownerArgs = cur.GenericTypeArguments ?? Array.Empty<RuntimeType>();
                var expectedRet = SubstituteRuntimeType(method.ReturnType, ownerArgs);

                var expectedPs = new RuntimeType[method.ParameterTypes.Length];
                for (int i = 0; i < expectedPs.Length; i++)
                    expectedPs[i] = SubstituteRuntimeType(method.ParameterTypes[i], ownerArgs);

                for (int i = 0; i < actualMethods.Length; i++)
                {
                    var cand = actualMethods[i];

                    if (!StringComparer.Ordinal.Equals(cand.Name, method.Name)) continue;
                    if (cand.HasThis != method.HasThis) continue;
                    if (cand.IsStatic != method.IsStatic) continue;
                    if (cand.IsVirtual != method.IsVirtual) continue;
                    if (cand.IsNewSlot != method.IsNewSlot) continue;
                    if (cand.IsFinal != method.IsFinal) continue;
                    if (cand.GenericArity != method.GenericArity) continue;
                    if (!ReferenceEquals(cand.ReturnType, expectedRet)) continue;
                    if (cand.ParameterTypes.Length != expectedPs.Length) continue;

                    bool same = true;
                    for (int p = 0; p < expectedPs.Length; p++)
                    {
                        if (!ReferenceEquals(cand.ParameterTypes[p], expectedPs[p]))
                        {
                            same = false;
                            break;
                        }
                    }

                    if (same)
                        return cand;
                }
            }

            return method;
        }
        private static int AlignUp(int value, int align)
        {
            int mask = align - 1;
            return (value + mask) & ~mask;
        }

        public (int size, int align) GetStorageSizeAlign(RuntimeType fieldType)
        {
            EnsureLayout(fieldType);

            if (fieldType.Kind == RuntimeTypeKind.TypeParam)
                return (TargetArchitecture.PointerSize, TargetArchitecture.PointerSize);

            // reference types stored as pointers
            if (fieldType.IsReferenceType)
                return (TargetArchitecture.PointerSize, TargetArchitecture.PointerSize);

            if (fieldType.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef)
                return (TargetArchitecture.PointerSize, TargetArchitecture.PointerSize);

            return (fieldType.SizeOf, fieldType.AlignOf);
        }
        private static bool TryGetPrimitiveLayout(RuntimeType t, out int size, out int align)
        {
            size = 0; align = 0;
            if (t.Namespace != "System") return false;

            switch (t.Name)
            {
                case "Void": size = 0; align = 1; return true;
                case "Boolean": size = 1; align = 1; return true;
                case "Char": size = 2; align = 2; return true;
                case "SByte":
                case "Byte": size = 1; align = 1; return true;
                case "Int16":
                case "UInt16": size = 2; align = 2; return true;
                case "Int32":
                case "UInt32":
                case "Single": size = 4; align = 4; return true;
                case "Int64":
                case "UInt64":
                case "Double": size = 8; align = 8; return true;
                case "Decimal": size = 16; align = 8; return true;
                case "IntPtr":
                case "UIntPtr":
                    size = TargetArchitecture.PointerSize;
                    align = TargetArchitecture.PointerSize;
                    return true;
                default:
                    return false;
            }
        }
        public RuntimeField ResolveFieldInMethodContext(RuntimeModule contextModule, int fieldToken, RuntimeMethod? methodContext)
        {
            if (methodContext is null)
                return ResolveField(contextModule, fieldToken);

            int table = MetadataToken.Table(fieldToken);
            int rid = MetadataToken.Rid(fieldToken);

            var ctxOwner = methodContext.DeclaringType;
            var ownerTypeArgs = ctxOwner.GenericTypeArguments;
            var methodTypeArgs = methodContext.MethodGenericArguments;

            if (table == MetadataToken.FieldDef)
            {
                var field = ResolveField(contextModule, fieldToken);

                if (ctxOwner.GenericTypeDefinition is null)
                    return field;

                if (!ReferenceEquals(field.DeclaringType, ctxOwner.GenericTypeDefinition))
                    return field;

                EnsureConstructedMembers(ctxOwner);
                EnsureLayout(ctxOwner);

                var defFields = field.IsStatic
                    ? ctxOwner.GenericTypeDefinition.StaticFields
                    : ctxOwner.GenericTypeDefinition.InstanceFields;

                var actualFields = field.IsStatic
                    ? ctxOwner.StaticFields
                    : ctxOwner.InstanceFields;

                for (int i = 0; i < defFields.Length && i < actualFields.Length; i++)
                {
                    if (ReferenceEquals(defFields[i], field))
                        return actualFields[i];
                }

                var expectedFieldType = SubstituteRuntimeType(field.FieldType, ownerTypeArgs, methodTypeArgs);

                for (int i = 0; i < actualFields.Length; i++)
                {
                    var cand = actualFields[i];
                    if (!StringComparer.Ordinal.Equals(cand.Name, field.Name))
                        continue;
                    if (!ReferenceEquals(cand.FieldType, expectedFieldType))
                        continue;
                    return cand;
                }

                return field;
            }

            if (table != MetadataToken.MemberRef)
                return ResolveField(contextModule, fieldToken);

            var mr = contextModule.Md.GetMemberRef(rid);
            string fieldName = contextModule.Md.GetString(mr.Name);

            var sig = contextModule.Md.GetBlob(mr.Signature);
            var sr = new SigReader(sig);
            byte prolog = sr.ReadByte();
            if (prolog != 0x06)
                throw new InvalidOperationException("MemberRef is not a field signature.");

            RuntimeType fieldType = ReadTypeSig(contextModule, ref sr);
            RuntimeType owner = ResolveMemberRefOwnerType(contextModule, mr.ClassToken);

            if (ownerTypeArgs.Length != 0 || methodTypeArgs.Length != 0)
            {
                owner = SubstituteRuntimeType(owner, ownerTypeArgs, methodTypeArgs);
                fieldType = SubstituteRuntimeType(fieldType, ownerTypeArgs, methodTypeArgs);
            }

            EnsureConstructedMembers(owner);
            EnsureLayout(owner);

            for (int i = 0; i < owner.InstanceFields.Length; i++)
            {
                var f = owner.InstanceFields[i];
                if (StringComparer.Ordinal.Equals(f.Name, fieldName) &&
                    ReferenceEquals(f.FieldType, fieldType))
                    return f;
            }

            for (int i = 0; i < owner.StaticFields.Length; i++)
            {
                var f = owner.StaticFields[i];
                if (StringComparer.Ordinal.Equals(f.Name, fieldName) &&
                    ReferenceEquals(f.FieldType, fieldType))
                    return f;
            }

            throw new MissingFieldException($"{owner.Namespace}.{owner.Name}.{fieldName} not found.");
        }
        public RuntimeField ResolveField(RuntimeModule contextModule, int fieldToken)
        {
            int table = MetadataToken.Table(fieldToken);
            int rid = MetadataToken.Rid(fieldToken);

            if (table == MetadataToken.FieldDef)
            {
                if (_fieldCache.TryGetValue((contextModule.Name, fieldToken), out var fd))
                    return fd;

                throw new MissingFieldException($"FieldDef 0x{fieldToken:X8} not found in {contextModule.Name}");
            }

            if (table != MetadataToken.MemberRef)
                throw new NotSupportedException($"Field token table not supported: 0x{fieldToken:X8}");

            var mr = contextModule.Md.GetMemberRef(rid);
            string fieldName = contextModule.Md.GetString(mr.Name);

            var sig = contextModule.Md.GetBlob(mr.Signature);
            var sr = new SigReader(sig);
            byte prolog = sr.ReadByte();
            if (prolog != 0x06)// MemberRef signature
                throw new InvalidOperationException("MemberRef is not a field signature.");

            RuntimeType fieldType = ReadTypeSig(contextModule, ref sr);

            RuntimeType owner = ResolveMemberRefOwnerType(contextModule, mr.ClassToken);

            EnsureConstructedMembers(owner);
            EnsureLayout(owner);

            // Search both instance/static fields
            for (int i = 0; i < owner.InstanceFields.Length; i++)
            {
                var f = owner.InstanceFields[i];
                if (f.Name == fieldName && f.FieldType.TypeId == fieldType.TypeId)
                    return f;
            }

            for (int i = 0; i < owner.StaticFields.Length; i++)
            {
                var f = owner.StaticFields[i];
                if (f.Name == fieldName && f.FieldType.TypeId == fieldType.TypeId)
                    return f;
            }

            throw new MissingFieldException($"{owner.Namespace}.{owner.Name}.{fieldName} not found.");
        }

        private RuntimeType ResolveMemberRefOwnerType(RuntimeModule caller, int classToken)
        {
            int table = MetadataToken.Table(classToken);
            int rid = MetadataToken.Rid(classToken);

            if (table == MetadataToken.TypeSpec)
                return ResolveType(caller, classToken);
            string asm, ns, name;
            if (table == MetadataToken.TypeRef)
            {
                (asm, ns, name) = Domain.ResolveTypeRefFullName(caller, rid);
            }
            else if (table == MetadataToken.TypeDef)
            {
                var full = Domain.GetTypeDefFullNameByRid(caller, rid);
                asm = caller.Name;
                ns = full.ns;
                name = full.name;
            }
            else
            {
                throw new NotSupportedException($"MemberRef.Class token not supported: 0x{classToken:X8}");
            }

            if (!_namedTypes.TryGetValue((asm, ns, name), out var owner))
                throw new TypeLoadException($"Type '{asm}:{ns}.{name}' not found.");

            return owner;
        }
        private static RuntimeTypeKind InferKindFromTypeDef(RuntimeModule m, TypeDefRow td)
        {
            if (((System.Reflection.TypeAttributes)td.Flags & System.Reflection.TypeAttributes.Interface) != 0)
                return RuntimeTypeKind.Interface;

            if (td.ExtendsEncoded == 0)
            {
                return RuntimeTypeKind.Class;
            }

            var (asm, ns, name) = ResolveTypeDefOrRefName(m, td.ExtendsEncoded);

            if (ns == "System" && name == "ValueType")
                return RuntimeTypeKind.Struct;
            if (ns == "System" && name == "Enum")
                return RuntimeTypeKind.Enum;

            return RuntimeTypeKind.Class;
        }
        private void BindBaseTypes()
        {
            foreach (var kv in _modules)
            {
                var m = kv.Value;
                for (int i = 0; i < m.Md.GetRowCount(MetadataTableKind.TypeDef); i++)
                {
                    int rid = i + 1;
                    int tok = MetadataToken.Make(MetadataToken.TypeDef, rid);
                    var rt = _typeCache[(m.Name, tok)];

                    var td = m.Md.GetTypeDef(i + 1);
                    if (td.ExtendsEncoded == 0)
                        continue;

                    int baseTok = DecodeTypeDefOrRefEncodedToToken(td.ExtendsEncoded);
                    rt.BaseType = ResolveType(m, baseTok);
                }
            }
        }
        private void BindInterfaces()
        {
            foreach (var kv in _modules)
            {
                var m = kv.Value;
                int count = m.Md.GetRowCount(MetadataTableKind.InterfaceImpl);
                if (count == 0)
                    continue;

                var byType = new Dictionary<int, List<RuntimeType>>();

                for (int rid = 1; rid <= count; rid++)
                {
                    var row = m.Md.GetInterfaceImpl(rid);
                    int classTok = MetadataToken.Make(MetadataToken.TypeDef, row.ClassTypeDefRid);

                    if (!_typeCache.TryGetValue((m.Name, classTok), out var type))
                        continue;

                    int ifaceTok = DecodeTypeDefOrRefEncodedToToken(row.InterfaceEncoded);
                    var iface = ResolveType(m, ifaceTok);

                    if (iface.Kind != RuntimeTypeKind.Interface)
                        continue;

                    if (!byType.TryGetValue(type.TypeId, out var list))
                        byType[type.TypeId] = list = new List<RuntimeType>();

                    bool exists = false;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (ReferenceEquals(list[i], iface))
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (!exists)
                        list.Add(iface);
                }

                foreach (var pair in byType)
                    _typeById[pair.Key].Interfaces = pair.Value.ToArray();
            }
        }
        private void BindMethodImpls()
        {
            foreach (var kv in _modules)
            {
                var m = kv.Value;
                int count = m.Md.GetRowCount(MetadataTableKind.MethodImpl);
                for (int rid = 1; rid <= count; rid++)
                {
                    var row = m.Md.GetMethodImpl(rid);
                    int classTok = MetadataToken.Make(MetadataToken.TypeDef, row.ClassTypeDefRid);
                    if (!_typeCache.TryGetValue((m.Name, classTok), out var owner))
                        continue;

                    var body = ResolveMethod(m, row.BodyMethodToken);
                    var decl = ResolveMethod(m, row.DeclarationMethodToken);

                    owner.ExplicitInterfaceMethodImpls ??= new Dictionary<int, RuntimeMethod>();
                    owner.ExplicitInterfaceMethodImpls[decl.MethodId] = body;
                }
            }
        }
        private void IndexWellKnownCoreTypes()
        {
            SystemObject = FindRequired("std", "System", "Object");
            SystemString = FindRequired("std", "System", "String");
            SystemArray = FindRequired("std", "System", "Array");
            SystemValueType = FindRequired("std", "System", "ValueType");
            SystemEnum = FindRequired("std", "System", "Enum");
        }

        private RuntimeType FindRequired(string asm, string ns, string name)
        {
            if (!_namedTypes.TryGetValue((asm, ns, name), out var t))
                throw new TypeLoadException($"Core type not found: {asm}:{ns}.{name}");
            return t;
        }
        private void BuildAllMembers()
        {
            foreach (var kv in _modules)
            {
                var m = kv.Value;
                for (int tdIndex = 0; tdIndex < m.Md.GetRowCount(MetadataTableKind.TypeDef); tdIndex++)
                {
                    int typeRid = tdIndex + 1;
                    int typeTok = MetadataToken.Make(MetadataToken.TypeDef, typeRid);
                    var declaringType = _typeCache[(m.Name, typeTok)];
                    var td = m.Md.GetTypeDef(tdIndex + 1);

                    BuildFieldsForType(m, declaringType, tdIndex, td);
                    BuildMethodsForType(m, declaringType, tdIndex, td);
                }
            }
        }
        private void BuildFieldsForType(RuntimeModule m, RuntimeType declaringType, int tdIndex, TypeDefRow td)
        {
            int startRid = td.FieldList;
            int endRid = (tdIndex + 1 < m.Md.GetRowCount(MetadataTableKind.TypeDef))
                ? m.Md.GetTypeDef(tdIndex + 2).FieldList
                : (m.Md.GetRowCount(MetadataTableKind.Field) + 1);

            var inst = new List<RuntimeField>();
            var stat = new List<RuntimeField>();

            for (int rid = startRid; rid < endRid; rid++)
            {
                int fieldTok = MetadataToken.Make(MetadataToken.FieldDef, rid);
                var fr = m.Md.GetField(rid);

                string name = m.Md.GetString(fr.Name);

                var attrs = (System.Reflection.FieldAttributes)fr.Flags;
                bool isStatic = (attrs & System.Reflection.FieldAttributes.Static) != 0;
                bool isLiteral = (attrs & System.Reflection.FieldAttributes.Literal) != 0;

                if (isLiteral)
                    continue;

                var sig = m.Md.GetBlob(fr.Signature);
                var r = new SigReader(sig);
                byte prolog = r.ReadByte();
                if (prolog != 0x06) throw new InvalidOperationException("Bad field sig");

                RuntimeType fieldType = ReadTypeSig(m, ref r);

                var rf = new RuntimeField(_nextFieldId++, declaringType, name, fieldType, isStatic);
                _fieldCache[(m.Name, fieldTok)] = rf;

                if (declaringType.Kind == RuntimeTypeKind.Enum && !isStatic && name == "value__")
                    declaringType.ElementType = fieldType;

                if (isStatic) stat.Add(rf);
                else inst.Add(rf);
            }

            declaringType.InstanceFields = inst.ToArray();
            declaringType.StaticFields = stat.ToArray();
        }
        private void BuildMethodsForType(RuntimeModule m, RuntimeType declaringType, int tdIndex, TypeDefRow td)
        {
            int startRid = td.MethodList;
            int endRid = (tdIndex + 1 < m.Md.GetRowCount(MetadataTableKind.TypeDef))
                ? m.Md.GetTypeDef(tdIndex + 2).MethodList
                : (m.Md.GetRowCount(MetadataTableKind.MethodDef) + 1);

            var methods = new List<RuntimeMethod>();

            for (int rid = startRid; rid < endRid; rid++)
            {
                int methodTok = MetadataToken.Make(MetadataToken.MethodDef, rid);
                var mr = m.Md.GetMethodDef(rid);

                string name = m.Md.GetString(mr.Name);

                // Decode method signature
                var sig = m.Md.GetBlob(mr.Signature);
                var sr = new SigReader(sig);
                byte cc = sr.ReadByte();

                var attrs = (System.Reflection.MethodAttributes)mr.Flags;
                bool hasThis = (cc & 0x20) != 0;
                bool isStatic = !hasThis;
                bool isVirtual = (attrs & System.Reflection.MethodAttributes.Virtual) != 0;
                bool isNewSlot = (attrs & System.Reflection.MethodAttributes.NewSlot) != 0;
                bool isFinal = (attrs & System.Reflection.MethodAttributes.Final) != 0;

                int genericArity = 0;
                if ((cc & 0x10) != 0)
                {
                    genericArity = checked((int)sr.ReadCompressedUInt());
                }

                uint paramCount = sr.ReadCompressedUInt();
                RuntimeType ret = ReadTypeSig(m, ref sr);

                var ps = new RuntimeType[paramCount];
                for (int i = 0; i < paramCount; i++)
                    ps[i] = ReadTypeSig(m, ref sr);

                var rm = new RuntimeMethod(
                    _nextMethodId++,
                    declaringType,
                    name,
                    ret,
                    ps,
                    hasThis,
                    isVirtual,
                    isStatic,
                    isNewSlot,
                    isFinal,
                    mr.Flags,
                    mr.ImplFlags);
                rm.BodyModule = m;
                rm.GenericArity = genericArity;
                if (m.MethodsByDefToken.TryGetValue(methodTok, out var bodyFn))
                    rm.Body = bodyFn;
                _methodById[rm.MethodId] = rm;
                _methodCache[(m.Name, methodTok)] = rm;
                methods.Add(rm);
            }

            declaringType.Methods = methods.ToArray();
        }
        private void BuildAllVTables()
        {
            var types = _typeCache.Values.ToArray();
            Array.Sort(types, (a, b) => GetDepth(a).CompareTo(GetDepth(b)));

            foreach (var t in types)
                BuildVTableForType(t);
        }

        private static int GetDepth(RuntimeType t)
        {
            int d = 0;
            for (var cur = t.BaseType; cur != null; cur = cur.BaseType) d++;
            return d;
        }

        private void BuildVTableForType(RuntimeType t)
        {
            var baseVt = t.BaseType?.VTable ?? Array.Empty<RuntimeMethod>();
            var vt = new List<RuntimeMethod>(baseVt.Length + 8);
            vt.AddRange(baseVt);

            // assign base slots already exist
            for (int i = 0; i < t.Methods.Length; i++)
            {
                var m = t.Methods[i];

                if (m.IsStatic) continue;
                if (!m.IsVirtual) continue;

                int slot = -1;

                if (t.BaseType != null && !m.IsNewSlot)
                {
                    slot = FindOverrideSlot(baseVt, m);
                }

                if (slot >= 0)
                {
                    m.VTableSlot = slot;
                    vt[slot] = m;
                }
                else
                {
                    m.VTableSlot = vt.Count;
                    vt.Add(m);
                }
            }
            t.VTable = vt.ToArray();
        }
        private static int FindOverrideSlot(RuntimeMethod[] baseVt, RuntimeMethod candidate)
        {
            for (int i = 0; i < baseVt.Length; i++)
            {
                var bm = baseVt[i];
                if (bm.Name != candidate.Name) continue;
                if (!SameSig(bm, candidate)) continue;
                return i;
            }
            return -1;
        }
        private static bool SameSig(RuntimeMethod a, RuntimeMethod b)
        {
            if (!ReferenceEquals(a.ReturnType, b.ReturnType)) return false;
            if (a.ParameterTypes.Length != b.ParameterTypes.Length) return false;
            if (a.GenericArity != b.GenericArity) return false;
            for (int i = 0; i < a.ParameterTypes.Length; i++)
                if (!ReferenceEquals(a.ParameterTypes[i], b.ParameterTypes[i])) return false;
            return true;
        }
        public RuntimeType ResolveType(RuntimeModule contextModule, int typeToken)
        {
            int table = MetadataToken.Table(typeToken);
            int rid = MetadataToken.Rid(typeToken);

            if (table == MetadataToken.TypeDef)
                return _typeCache[(contextModule.Name, typeToken)];

            if (table == MetadataToken.TypeRef)
                return ResolveTypeRef(contextModule, rid);

            if (table == MetadataToken.TypeSpec)
            {
                var ts = contextModule.Md.GetTypeSpec(rid);
                var sig = contextModule.Md.GetBlob(ts.Signature);
                // Stable key to intern constructed types
                string key = contextModule.Name + ":ts:" + Convert.ToHexString(sig);

                if (_constructedTypes.TryGetValue(key, out var cached))
                    return cached;

                var sr = new SigReader(sig);
                RuntimeType t = ReadTypeSig(contextModule, ref sr);
                _constructedTypes[key] = t;
                return t;
            }

            throw new NotSupportedException($"ResolveType: unsupported token 0x{typeToken:X8}");
        }
        private RuntimeType ResolveTypeRef(RuntimeModule contextModule, int typeRefRid)
        {
            var tr = contextModule.Md.GetTypeRef(typeRefRid);

            int scopeTok = tr.ResolutionScopeToken;
            int scopeTable = MetadataToken.Table(scopeTok);

            if (scopeTable == MetadataToken.TypeSpec)
            {
                var enclosing = ResolveType(contextModule, scopeTok);
                var enclosingDef = enclosing.GenericTypeDefinition ?? enclosing;

                string mdName = contextModule.Md.GetString(tr.Name);
                var (simpleName, _) = SplitMetadataArity(mdName);

                if (!_namedTypes.TryGetValue(
                    (enclosingDef.AssemblyName, enclosingDef.Namespace, enclosingDef.Name + "+" + simpleName),
                    out var nestedDef))
                {
                    throw new TypeLoadException(
                        $"Nested TypeRef not resolved: {enclosingDef.AssemblyName}:{enclosingDef.Namespace}.{enclosingDef.Name}+{simpleName}");
                }

                var ownerArgs = enclosing.GenericTypeArguments;

                if (ownerArgs.Length != 0 && TypeUsesOwnerTypeParameters(nestedDef))
                {
                    var closedNested = GetOrCreateGenericInstanceType(nestedDef, ownerArgs);
                    EnsureConstructedMembers(closedNested);
                    return closedNested;
                }

                return nestedDef;
            }

            var (asm, ns, name) = Domain.ResolveTypeRefFullName(contextModule, typeRefRid);
            if (_namedTypes.TryGetValue((asm, ns, name), out var t))
                return t;

            throw new TypeLoadException($"TypeRef not resolved: {asm}:{ns}.{name}");
        }
        private static (string name, int arity) SplitMetadataArity(string mdName)
        {
            int tick = mdName.IndexOf('`');
            if (tick < 0)
                return (mdName, 0);

            var name = mdName.Substring(0, tick);
            if (tick + 1 < mdName.Length && int.TryParse(mdName.AsSpan(tick + 1), out int arity))
                return (name, arity);

            return (name, 0);
        }

        private static bool TypeUsesOwnerTypeParameters(RuntimeType t)
        {
            if (t is null)
                return false;

            if (UsesTypeParameters(t.BaseType!, wantMethod: false))
                return true;

            for (int i = 0; i < t.InstanceFields.Length; i++)
                if (UsesTypeParameters(t.InstanceFields[i].FieldType, wantMethod: false))
                    return true;

            for (int i = 0; i < t.StaticFields.Length; i++)
                if (UsesTypeParameters(t.StaticFields[i].FieldType, wantMethod: false))
                    return true;

            for (int i = 0; i < t.Methods.Length; i++)
            {
                var m = t.Methods[i];
                if (UsesTypeParameters(m.ReturnType, wantMethod: false))
                    return true;

                for (int p = 0; p < m.ParameterTypes.Length; p++)
                    if (UsesTypeParameters(m.ParameterTypes[p], wantMethod: false))
                        return true;
            }

            return false;
        }
        private RuntimeType ReadTypeSig(RuntimeModule contextModule, ref SigReader r)
        {
            var et = (SigElementType)r.ReadByte();

            // Map ELEMENT_TYPE_* to your core types for primitives.
            switch (et)
            {
                case SigElementType.VOID: return FindRequired("std", "System", "Void");
                case SigElementType.BOOLEAN: return FindRequired("std", "System", "Boolean");
                case SigElementType.CHAR: return FindRequired("std", "System", "Char");
                case SigElementType.I1: return FindRequired("std", "System", "SByte");
                case SigElementType.U1: return FindRequired("std", "System", "Byte");
                case SigElementType.I2: return FindRequired("std", "System", "Int16");
                case SigElementType.U2: return FindRequired("std", "System", "UInt16");
                case SigElementType.I4: return FindRequired("std", "System", "Int32");
                case SigElementType.U4: return FindRequired("std", "System", "UInt32");
                case SigElementType.I8: return FindRequired("std", "System", "Int64");
                case SigElementType.U8: return FindRequired("std", "System", "UInt64");
                case SigElementType.I: return FindRequired("std", "System", "IntPtr");
                case SigElementType.U: return FindRequired("std", "System", "UIntPtr");
                case SigElementType.R4: return FindRequired("std", "System", "Single");
                case SigElementType.R8: return FindRequired("std", "System", "Double");
                case SigElementType.STRING: return SystemString;
                case SigElementType.OBJECT: return SystemObject;

                case SigElementType.CLASS:
                case SigElementType.VALUETYPE:
                    {
                        uint coded = r.ReadCompressedUInt();
                        int tok = DecodeTypeDefOrRefEncodedToToken((int)coded);
                        return ResolveType(contextModule, tok);
                    }

                case SigElementType.SZARRAY:
                    {
                        var elem = ReadTypeSig(contextModule, ref r);
                        return GetOrCreateArrayType(elem, rank: 1);
                    }

                case SigElementType.ARRAY:
                    {
                        var elem = ReadTypeSig(contextModule, ref r);

                        uint rank = r.ReadCompressedUInt();
                        uint nsizes = r.ReadCompressedUInt();
                        for (int i = 0; i < nsizes; i++)
                            _ = r.ReadCompressedUInt(); // sizes

                        uint nlb = r.ReadCompressedUInt();
                        for (int i = 0; i < nlb; i++)
                            _ = r.ReadCompressedUInt(); // low bounds

                        if (rank == 0)
                            throw new BadImageFormatException("ARRAY signature with rank=0 is invalid.");

                        return GetOrCreateArrayType(elem, checked((int)rank));
                    }

                case SigElementType.PTR:
                    {
                        var elem = ReadTypeSig(contextModule, ref r);
                        return GetOrCreatePointerType(elem);
                    }

                case SigElementType.BYREF:
                    {
                        var elem = ReadTypeSig(contextModule, ref r);
                        return GetOrCreateByRefType(elem);
                    }

                case SigElementType.VAR:
                    {
                        uint ord = r.ReadCompressedUInt();
                        return GetOrCreateGenericParamType(isMethodParam: false, checked((int)ord));
                    }

                case SigElementType.MVAR:
                    {
                        uint ord = r.ReadCompressedUInt();
                        return GetOrCreateGenericParamType(isMethodParam: true, checked((int)ord));
                    }

                case SigElementType.GENERICINST:
                    {
                        var kindEt = (SigElementType)r.ReadByte();
                        if (kindEt != SigElementType.CLASS && kindEt != SigElementType.VALUETYPE)
                            throw new BadImageFormatException($"GENERICINST with invalid kind: {kindEt}");

                        uint coded = r.ReadCompressedUInt();
                        int defTok = DecodeTypeDefOrRefEncodedToToken((int)coded);
                        RuntimeType genericDef = ResolveType(contextModule, defTok);

                        uint argc = r.ReadCompressedUInt();
                        var args = new RuntimeType[argc];
                        for (int i = 0; i < args.Length; i++)
                            args[i] = ReadTypeSig(contextModule, ref r);

                        return GetOrCreateGenericInstanceType(genericDef, args);
                    }

                default:
                    throw new NotSupportedException($"TypeSig element not supported: {et}");
            }
        }

        private RuntimeType GetOrCreateArrayType(RuntimeType elem)
            => GetOrCreateArrayType(elem, rank: 1);
        private RuntimeType GetOrCreateArrayType(RuntimeType elem, int rank)
        {
            if (rank <= 0) throw new ArgumentOutOfRangeException(nameof(rank));

            string key = "arr:" + rank + ":" + elem.TypeId;
            if (_constructedTypes.TryGetValue(key, out var t))
                return t;

            string suffix = rank == 1 ? "[]" : "[" + new string(',', rank - 1) + "]";
            t = new RuntimeType(
                _nextTypeId++,
                RuntimeTypeKind.Array,
                asm: SystemArray.AssemblyName,
                ns: "System",
                name: elem.Name + suffix);

            t.BaseType = SystemArray;
            t.ElementType = elem;
            t.ArrayRank = rank;

            _constructedTypes[key] = t;
            _typeById[t.TypeId] = t;
            return t;
        }
        private RuntimeType GetOrCreateGenericParamType(bool isMethodParam, int ordinal)
        {
            string key = (isMethodParam ? "mvar:" : "var:") + ordinal;
            if (_constructedTypes.TryGetValue(key, out var t))
                return t;

            string name = (isMethodParam ? "!!" : "!") + ordinal;

            t = new RuntimeType(
                _nextTypeId++,
                RuntimeTypeKind.TypeParam,
                asm: "<sig>",
                ns: "",
                name: name);
            t.IsMethodGenericParameter = isMethodParam;
            t.GenericParameterOrdinal = ordinal;
            _constructedTypes[key] = t;
            _typeById[t.TypeId] = t;
            return t;
        }
        private RuntimeType GetOrCreateGenericInstanceType(RuntimeType genericDef, RuntimeType[] args)
        {
            if (genericDef is null) throw new ArgumentNullException(nameof(genericDef));
            if (args is null) throw new ArgumentNullException(nameof(args));

            var keySb = new StringBuilder();
            keySb.Append("ginst:").Append(genericDef.TypeId).Append('<');
            for (int i = 0; i < args.Length; i++)
            {
                if (i != 0) keySb.Append(',');
                keySb.Append(args[i].TypeId);
            }
            keySb.Append('>');

            string key = keySb.ToString();
            if (_constructedTypes.TryGetValue(key, out var cached))
                return cached;

            var nameSb = new StringBuilder();
            nameSb.Append(genericDef.Name).Append('<');
            for (int i = 0; i < args.Length; i++)
            {
                if (i != 0) nameSb.Append(", ");
                nameSb.Append(args[i].Name);
            }
            nameSb.Append('>');

            var t = new RuntimeType(
                _nextTypeId++,
                genericDef.Kind,
                asm: genericDef.AssemblyName,
                ns: genericDef.Namespace,
                name: nameSb.ToString());

            t.GenericTypeDefinition = genericDef;
            t.GenericTypeArguments = args;

            _constructedTypes[key] = t;
            _typeById[t.TypeId] = t;
            return t;
        }
        internal void EnsureConstructedMembers(RuntimeType t)
        {
            if (t.GenericTypeDefinition is null)
                return;

            var genericDef = t.GenericTypeDefinition;

            if (t.ConstructedMembersInitialized)
            {
                bool staleInstanceFields = genericDef.InstanceFields.Length != 0 && t.InstanceFields.Length == 0;
                bool staleStaticFields = genericDef.StaticFields.Length != 0 && t.StaticFields.Length == 0;
                bool staleMethods = genericDef.Methods.Length != 0 && t.Methods.Length == 0;

                if (!staleInstanceFields && !staleStaticFields && !staleMethods)
                    return;
            }
            t.ConstructedMembersInitialized = true;

            var typeArgs = t.GenericTypeArguments;

            t.BaseType = genericDef.BaseType is null
                ? null
                : SubstituteRuntimeType(genericDef.BaseType, typeArgs);

            if (genericDef.InstanceFields.Length != 0 || genericDef.StaticFields.Length != 0)
            {
                var inst = new RuntimeField[genericDef.InstanceFields.Length];
                var stat = new RuntimeField[genericDef.StaticFields.Length];

                for (int i = 0; i < genericDef.InstanceFields.Length; i++)
                {
                    var src = genericDef.InstanceFields[i];
                    var ft = SubstituteRuntimeType(src.FieldType, typeArgs);
                    inst[i] = new RuntimeField(_nextFieldId++, t, src.Name, ft, isStatic: false);
                }

                for (int i = 0; i < genericDef.StaticFields.Length; i++)
                {
                    var src = genericDef.StaticFields[i];
                    var ft = SubstituteRuntimeType(src.FieldType, typeArgs);
                    stat[i] = new RuntimeField(_nextFieldId++, t, src.Name, ft, isStatic: true);
                }

                t.InstanceFields = inst;
                t.StaticFields = stat;
            }

            if (genericDef.Methods.Length != 0)
            {
                var methods = new RuntimeMethod[genericDef.Methods.Length];

                for (int i = 0; i < genericDef.Methods.Length; i++)
                {
                    var src = genericDef.Methods[i];
                    var ret = SubstituteRuntimeType(src.ReturnType, typeArgs);
                    var ps = new RuntimeType[src.ParameterTypes.Length];
                    for (int p = 0; p < ps.Length; p++)
                        ps[p] = SubstituteRuntimeType(src.ParameterTypes[p], typeArgs);

                    var dst = new RuntimeMethod(
                        _nextMethodId++,
                        t,
                        src.Name,
                        ret,
                        ps,
                        src.HasThis,
                        src.IsVirtual,
                        src.IsStatic,
                        src.IsNewSlot,
                        src.IsFinal,
                        src.Flags,
                        src.ImplFlags);

                    dst.BodyModule = src.BodyModule;
                    dst.Body = src.Body;
                    dst.GenericArity = src.GenericArity;
                    _methodById[dst.MethodId] = dst;
                    methods[i] = dst;
                }

                t.Methods = methods;
            }

            if (t.BaseType is not null)
                EnsureConstructedMembers(t.BaseType);

            if (t.Kind is RuntimeTypeKind.Class or RuntimeTypeKind.Interface)
                BuildVTableForType(t);
        }
        public RuntimeType ResolveTypeInMethodContext(RuntimeModule contextModule, int typeToken, RuntimeMethod? methodContext)
        {
            var t = ResolveType(contextModule, typeToken);
            if (methodContext is null)
                return t;

            return SubstituteRuntimeType(
                t,
                methodContext.DeclaringType.GenericTypeArguments,
                methodContext.MethodGenericArguments);
        }
        private RuntimeType SubstituteRuntimeType(RuntimeType type, RuntimeType[] ownerTypeArgs)
            => SubstituteRuntimeType(type, ownerTypeArgs, Array.Empty<RuntimeType>());
        private RuntimeType SubstituteRuntimeType(RuntimeType type, RuntimeType[] ownerTypeArgs, RuntimeType[] methodTypeArgs)
        {
            if (type.Kind == RuntimeTypeKind.TypeParam)
            {
                if (type.IsMethodGenericParameter)
                {
                    int ord = type.GenericParameterOrdinal;
                    if ((uint)ord < (uint)methodTypeArgs.Length)
                        return methodTypeArgs[ord];
                    return type;
                }
                else
                {
                    int ord = type.GenericParameterOrdinal;
                    if ((uint)ord < (uint)ownerTypeArgs.Length)
                        return ownerTypeArgs[ord];
                }
                return type;
            }

            if (type.Kind == RuntimeTypeKind.Array && type.ElementType is not null)
            {
                var elem = SubstituteRuntimeType(type.ElementType, ownerTypeArgs, methodTypeArgs);
                if (!ReferenceEquals(elem, type.ElementType))
                    return GetOrCreateArrayType(elem, type.ArrayRank <= 0 ? 1 : type.ArrayRank);
                return type;
            }

            if (type.Kind == RuntimeTypeKind.Pointer && type.ElementType is not null)
            {
                var elem = SubstituteRuntimeType(type.ElementType, ownerTypeArgs, methodTypeArgs);
                if (!ReferenceEquals(elem, type.ElementType))
                    return GetOrCreatePointerType(elem);
                return type;
            }

            if (type.Kind == RuntimeTypeKind.ByRef && type.ElementType is not null)
            {
                var elem = SubstituteRuntimeType(type.ElementType, ownerTypeArgs, methodTypeArgs);
                if (!ReferenceEquals(elem, type.ElementType))
                    return GetOrCreateByRefType(elem);
                return type;
            }

            if (type.GenericTypeDefinition is null &&
                ownerTypeArgs.Length != 0 &&
                type.Kind is RuntimeTypeKind.Class or RuntimeTypeKind.Struct or RuntimeTypeKind.Interface &&
                TypeUsesOwnerTypeParameters(type))
            {
                return GetOrCreateGenericInstanceType(type, ownerTypeArgs);
            }

            if (type.GenericTypeDefinition is not null)
            {
                var oldArgs = type.GenericTypeArguments;
                if (oldArgs.Length == 0)
                    return type;

                RuntimeType[]? newArgs = null;
                for (int i = 0; i < oldArgs.Length; i++)
                {
                    var a2 = SubstituteRuntimeType(oldArgs[i], ownerTypeArgs, methodTypeArgs);
                    if (!ReferenceEquals(a2, oldArgs[i]))
                    {
                        newArgs ??= (RuntimeType[])oldArgs.Clone();
                        newArgs[i] = a2;
                    }
                }

                if (newArgs is not null)
                    return GetOrCreateGenericInstanceType(type.GenericTypeDefinition, newArgs);

                return type;
            }

            return type;
        }
        private RuntimeType GetOrCreatePointerType(RuntimeType elem)
        {
            string key = "ptr:" + elem.TypeId;
            if (_constructedTypes.TryGetValue(key, out var t))
                return t;

            t = new RuntimeType(_nextTypeId++, RuntimeTypeKind.Pointer, asm: elem.AssemblyName, ns: elem.Namespace, name: elem.Name + "*");
            t.ElementType = elem;
            _constructedTypes[key] = t;
            _typeById[t.TypeId] = t;
            return t;
        }

        private RuntimeType GetOrCreateByRefType(RuntimeType elem)
        {
            string key = "byref:" + elem.TypeId;
            if (_constructedTypes.TryGetValue(key, out var t))
                return t;

            t = new RuntimeType(_nextTypeId++, RuntimeTypeKind.ByRef, asm: elem.AssemblyName, ns: elem.Namespace, name: elem.Name + "&");
            t.ElementType = elem;
            _constructedTypes[key] = t;
            _typeById[t.TypeId] = t;
            return t;
        }

        private static int DecodeTypeDefOrRefEncodedToToken(int encoded)
        {
            int tag = encoded & 0x3;
            int rid = (int)((uint)encoded >> 2);

            return tag switch
            {
                0 => MetadataToken.Make(MetadataToken.TypeDef, rid),
                1 => MetadataToken.Make(MetadataToken.TypeRef, rid),
                2 => MetadataToken.Make(MetadataToken.TypeSpec, rid),
                _ => throw new InvalidOperationException("Bad TypeDefOrRef coded index")
            };
        }

        private static (string asm, string ns, string name) ResolveTypeDefOrRefName(RuntimeModule contextModule, int extendsEncoded)
        {
            int tok = DecodeTypeDefOrRefEncodedToToken(extendsEncoded);
            int table = MetadataToken.Table(tok);
            int rid = MetadataToken.Rid(tok);

            if (table == MetadataToken.TypeDef)
            {
                var (ns, name) = Domain.GetTypeDefFullNameByRid(contextModule, rid);
                return (contextModule.Name, ns, name);
            }

            if (table == MetadataToken.TypeRef)
            {
                return Domain.ResolveTypeRefFullName(contextModule, rid);
            }

            if (table == MetadataToken.TypeSpec)
            {
                // Not expected for base type in your current emitter.
                return (contextModule.Name, "", "typespec");
            }

            throw new NotSupportedException();
        }

        private void ComputeFieldLayout(RuntimeType t)
        {

        }
    }

    internal enum RuntimeTypeKind : byte
    {
        Class,
        Struct,
        Interface,
        Enum,
        Array,      // SZARRAY / ARRAY
        Pointer,    // PTR
        ByRef,      // BYREF
        TypeParam,  // VAR/MVAR
    }
    internal sealed class RuntimeType
    {
        public int TypeId { get; }
        public RuntimeTypeKind Kind { get; }
        public string AssemblyName { get; }
        public string Namespace { get; }
        public string Name { get; }

        public bool IsValueType => Kind is RuntimeTypeKind.Struct or RuntimeTypeKind.Enum;
        public bool IsReferenceType => !IsValueType && Kind is not (RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef);
        public RuntimeType? BaseType { get; internal set; }
        public RuntimeType? ElementType { get; internal set; }
        public int ArrayRank { get; internal set; }
        public bool IsMethodGenericParameter { get; internal set; }
        public int GenericParameterOrdinal { get; internal set; } = -1;
        public RuntimeType? GenericTypeDefinition { get; internal set; }
        public RuntimeType[] GenericTypeArguments { get; internal set; } = Array.Empty<RuntimeType>();
        internal bool ConstructedMembersInitialized { get; set; }
        // Layout
        public int SizeOf { get; internal set; }
        public int AlignOf { get; internal set; }
        public int StaticSize { get; internal set; }
        public int StaticAlign { get; internal set; } = 1;
        public int InstanceSize { get; internal set; }
        public bool ContainsGcPointers { get; internal set; }
        public int[] GcPointerOffsets { get; internal set; } = Array.Empty<int>();
        public RuntimeType[] Interfaces { get; internal set; } = Array.Empty<RuntimeType>();
        public RuntimeField[] InstanceFields { get; internal set; } = Array.Empty<RuntimeField>();
        public RuntimeField[] StaticFields { get; internal set; } = Array.Empty<RuntimeField>();
        public RuntimeMethod[] Methods { get; internal set; } = Array.Empty<RuntimeMethod>();
        public RuntimeMethod[] VTable { get; internal set; } = Array.Empty<RuntimeMethod>();
        public Dictionary<int, RuntimeMethod>? ExplicitInterfaceMethodImpls { get; internal set; }
        public RuntimeType(int typeId, RuntimeTypeKind kind, string asm, string ns, string name)
        {
            TypeId = typeId;
            Kind = kind;
            AssemblyName = asm;
            Namespace = ns;
            Name = name;
        }
    }
    internal sealed class RuntimeField
    {
        public int FieldId { get; }
        public RuntimeType DeclaringType { get; }
        public string Name { get; }
        public RuntimeType FieldType { get; }
        public bool IsStatic { get; }
        public int Offset { get; internal set; }
        public RuntimeField(int fieldId, RuntimeType declType, string name, RuntimeType fieldType, bool isStatic)
        {
            FieldId = fieldId;
            DeclaringType = declType;
            Name = name;
            FieldType = fieldType;
            IsStatic = isStatic;
        }
    }
    internal sealed class RuntimeMethod
    {
        public int MethodId { get; }
        public RuntimeType DeclaringType { get; }
        public string Name { get; }
        public RuntimeType ReturnType { get; }
        public RuntimeType[] ParameterTypes { get; }
        public bool HasThis { get; }
        public bool IsVirtual { get; }
        public bool IsStatic { get; }
        public bool IsNewSlot { get; }
        public bool IsFinal { get; }
        public ushort Flags { get; }
        public ushort ImplFlags { get; }
        public bool HasInternalCall => (ImplFlags & MetadataFlagBits.InternalCall) != 0;
        public bool HasNoInlining => (ImplFlags & MetadataFlagBits.NoInlining) != 0;
        public bool HasAggressiveInlining => (ImplFlags & MetadataFlagBits.AggressiveInlining) != 0;
        public int VTableSlot { get; internal set; } = -1;
        public RuntimeModule? BodyModule { get; internal set; }
        public Cnidaria.Cs.BytecodeFunction? Body { get; internal set; }
        public RuntimeMethod? GenericMethodDefinition { get; internal set; }
        public RuntimeType[] MethodGenericArguments { get; internal set; } = Array.Empty<RuntimeType>();
        public int GenericArity { get; internal set; }
        public bool IsPrivate =>
            (((System.Reflection.MethodAttributes)Flags) &
            System.Reflection.MethodAttributes.MemberAccessMask) ==
            System.Reflection.MethodAttributes.Private;
        public bool IsPublic =>
            (((System.Reflection.MethodAttributes)Flags) &
            System.Reflection.MethodAttributes.MemberAccessMask) ==
            System.Reflection.MethodAttributes.Public;
        public RuntimeMethod(
            int methodId,
            RuntimeType declType,
            string name,
            RuntimeType ret,
            RuntimeType[] ps,
            bool hasThis,
            bool isVirtual,
            bool isStatic,
            bool isNewSlot,
            bool isFinal,
            ushort flags,
            ushort implFlags)
        {
            MethodId = methodId;
            DeclaringType = declType;
            Name = name;
            ReturnType = ret;
            ParameterTypes = ps;
            HasThis = hasThis;
            IsVirtual = isVirtual;
            IsStatic = isStatic;
            IsNewSlot = isNewSlot;
            IsFinal = isFinal;
            Flags = flags;
            ImplFlags = implFlags;
        }
    }
}
