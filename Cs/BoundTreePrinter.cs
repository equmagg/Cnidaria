using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace Cnidaria.Cs
{
    internal static class BytecodeDump
    {
        public static int FindEntryPointMethodDef(RuntimeModule module)
        {
            if (TryFindEntryByName(module, "<Main>$", out var tok))
                return tok;

            if (TryFindStaticMain(module, out tok))
                return tok;

            throw new InvalidOperationException("Entry point not found in module metadata.");
        }

        private static bool TryFindEntryByName(RuntimeModule m, string name, out int methodDefToken)
        {
            var md = m.Md;
            for (int rid = 1; rid <= md.GetRowCount(MetadataTableKind.MethodDef); rid++)
            {
                var row = md.GetMethodDef(rid);
                if (!StringComparer.Ordinal.Equals(md.GetString(row.Name), name))
                    continue;

                methodDefToken = MetadataToken.Make(MetadataToken.MethodDef, rid);
                return true;
            }

            methodDefToken = 0;
            return false;
        }

        private static bool TryFindStaticMain(RuntimeModule m, out int methodDefToken)
        {
            var md = m.Md;

            for (int rid = 1; rid <= md.GetRowCount(MetadataTableKind.MethodDef); rid++)
            {
                var row = md.GetMethodDef(rid);
                if (!StringComparer.Ordinal.Equals(md.GetString(row.Name), "Main"))
                    continue;

                if (!IsStaticMainStringArraySignature(md.GetBlob(row.Signature)))
                    continue;

                methodDefToken = MetadataToken.Make(MetadataToken.MethodDef, rid);
                return true;
            }

            methodDefToken = 0;
            return false;
        }

        private static bool IsStaticMainStringArraySignature(ReadOnlySpan<byte> sig)
        {
            var r = new SigReader(sig);
            byte cc = r.ReadByte();

            // Must be static (no HASTHIS)
            if ((cc & 0x20) != 0)
                return false;

            // Reject generic mains
            if ((cc & 0x10) != 0)
            {
                r.ReadCompressedUInt(); // generic arity
                return false;
            }

            uint paramCount = r.ReadCompressedUInt();
            if (paramCount != 1)
                return false;

            // ret: void
            if ((SigElementType)r.ReadByte() != SigElementType.VOID)
                return false;

            // arg0: string[]
            if ((SigElementType)r.ReadByte() != SigElementType.SZARRAY)
                return false;
            if ((SigElementType)r.ReadByte() != SigElementType.STRING)
                return false;

            return true;
        }
        public static void DumpReachable(
            IReadOnlyDictionary<string, RuntimeModule> modules,
            RuntimeModule entryModule,
            int entryMethodDefToken)
        {
            if (!entryModule.MethodsByDefToken.TryGetValue(entryMethodDefToken, out var entryFn))
                throw new MissingMethodException(
                    $"No bytecode body for entry token 0x{entryMethodDefToken:X8} in '{entryModule.Name}'.");

            var q = new Queue<(RuntimeModule mod, BytecodeFunction fn)>();
            var seen = new HashSet<(string mod, int tok)>();

            Enqueue(entryModule, entryFn);

            while (q.Count != 0)
            {
                var (mod, fn) = q.Dequeue();

                Console.WriteLine($"== {FormatMethodDef(mod, fn.MethodToken)}");
                Console.WriteLine($"   token=0x{fn.MethodToken:X8} maxStack={fn.MaxStack} locals={fn.LocalTypeTokens.Length}");

                if (fn.LocalTypeTokens.Length != 0)
                {
                    Console.WriteLine("   locals:");
                    for (int i = 0; i < fn.LocalTypeTokens.Length; i++)
                        Console.WriteLine($"     [{i}] {FormatTypeToken(mod, fn.LocalTypeTokens[i])}");
                }

                var insns = fn.Instructions;
                for (int pc = 0; pc < insns.Length; pc++)
                {
                    var ins = insns[pc];
                    string extra = FormatOperandComment(modules, mod, ins);

                    Console.WriteLine(
                        $"{pc:D4}: {ins.Op,-12} {ins.Operand0,10} {ins.Operand1,10} {ins.Operand2,12}   // pop={ins.Pop} push={ins.Push}{extra}");

                    if (ins.Op is BytecodeOp.Call or BytecodeOp.Newobj)
                    {
                        try
                        {
                            var (tmod, tfn) = ResolveCallWithModule(modules, mod, ins.Operand0);
                            Enqueue(tmod, tfn);
                        }
                        catch (MissingMethodException)
                        {
                            // ignore
                        }
                    }
                }

                Console.WriteLine();

                
            }
            void Enqueue(RuntimeModule m, BytecodeFunction f)
            {
                var key = (m.Name, f.MethodToken);
                if (seen.Add(key))
                    q.Enqueue((m, f));
            }
        }

        private static string FormatOperandComment(
            IReadOnlyDictionary<string, RuntimeModule> modules,
            RuntimeModule currentModule,
            Instruction ins)
        {
            switch (ins.Op)
            {
                case BytecodeOp.Call:
                case BytecodeOp.Newobj:
                    {
                        string callSite = FormatMethodToken(currentModule, ins.Operand0);
                        try
                        {
                            var (tmod, tfn) = ResolveCallWithModule(modules, currentModule, ins.Operand0);
                            string resolved = FormatMethodDef(tmod, tfn.MethodToken);
                            return $"   // {callSite} -> {resolved}";
                        }
                        catch (MissingMethodException)
                        {
                            return $"   // {callSite} -> <no body>";
                        }
                    }

                case BytecodeOp.CastClass:
                case BytecodeOp.Box:
                case BytecodeOp.UnboxAny:
                case BytecodeOp.DefaultValue:
                case BytecodeOp.Newarr:
                case BytecodeOp.Ldelem:
                case BytecodeOp.Stelem:
                    return $"   // {FormatTypeToken(currentModule, ins.Operand0)}";

                case BytecodeOp.Ldfld:
                case BytecodeOp.Stfld:
                case BytecodeOp.Ldsfld:
                case BytecodeOp.Stsfld:
                    return $"   // {FormatFieldToken(currentModule, ins.Operand0)}";

                case BytecodeOp.Ldstr:

                    return $"   // userstr#0x{ins.Operand0:X8}";

                default:
                    return "";
            }
        }
        private static (RuntimeModule targetModule, BytecodeFunction fn) ResolveCallWithModule(
            IReadOnlyDictionary<string, RuntimeModule> modules,
            RuntimeModule caller,
            int methodToken)
        {
            int table = MetadataToken.Table(methodToken);
            int rid = MetadataToken.Rid(methodToken);

            if (table == MetadataToken.MethodDef)
            {
                if (!caller.MethodsByDefToken.TryGetValue(methodToken, out var fn))
                    throw new MissingMethodException($"No body for MethodDef 0x{methodToken:X8} in '{caller.Name}'");

                return (caller, fn);
            }
            if (table == MetadataToken.MethodSpec)
            {
                var ms = caller.Md.GetMethodSpec(rid);
                return ResolveCallWithModule(modules, caller, ms.Method);
            }

            if (table != MetadataToken.MemberRef)
                throw new NotSupportedException($"Call token table not supported: 0x{methodToken:X8}");

            var mr = caller.Md.GetMemberRef(rid);
            string methodName = caller.Md.GetString(mr.Name);
            string sigKey = caller.GetSignatureKeyFromThisModule(mr.Signature);

            var (asmName, ns, typeName) = ResolveMemberRefClass(caller, mr.ClassToken);

            if (!modules.TryGetValue(asmName, out var targetModule))
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
            }

            if (!targetModule.MethodsByDefToken.TryGetValue(methodDefTok, out var fn2))
                throw new MissingMethodException($"No body for resolved MethodDef 0x{methodDefTok:X8} in '{asmName}'");

            return (targetModule, fn2);
        }

        private static (string asm, string ns, string name) ResolveMemberRefClass(RuntimeModule caller, int classToken)
        {
            int table = MetadataToken.Table(classToken);
            int rid = MetadataToken.Rid(classToken);

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

            throw new NotSupportedException($"MemberRef.Class token not supported: 0x{classToken:X8}");
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

        private static (string asm, string ns, string name) ResolveTypeRefFullName(RuntimeModule caller, int typeRefRid)
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

            throw new NotSupportedException($"Unsupported TypeRef scope token: 0x{scopeTok:X8}");
        }
        private static (string ns, string name) GetTypeDefFullNameByRid(RuntimeModule m, int rid)
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
        private static (string asm, string ns, string name) ResolveTypeSpecOwner(RuntimeModule caller, ref SigReader sr)
        {
            var et = (SigElementType)sr.ReadByte();

            if (et == SigElementType.GENERICINST)
            {
                var kind = (SigElementType)sr.ReadByte();
                if (kind != SigElementType.CLASS && kind != SigElementType.VALUETYPE)
                    throw new BadImageFormatException($"GENERICINST owner has invalid kind '{kind}'.");

                int ownerTok = DecodeTypeDefOrRefEncodedToToken((int)sr.ReadCompressedUInt());
                return ResolveTypeTokenFullName(caller, ownerTok);
            }

            if (et == SigElementType.CLASS || et == SigElementType.VALUETYPE)
            {
                int tok = DecodeTypeDefOrRefEncodedToToken((int)sr.ReadCompressedUInt());
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
        private static string FormatMethodToken(RuntimeModule caller, int methodToken)
        {
            int table = MetadataToken.Table(methodToken);
            int rid = MetadataToken.Rid(methodToken);

            if (table == MetadataToken.MethodDef)
                return FormatMethodDef(caller, methodToken);

            if (table == MetadataToken.MemberRef)
            {
                var mr = caller.Md.GetMemberRef(rid);
                string methodName = caller.Md.GetString(mr.Name);
                string sigKey = caller.GetSignatureKeyFromThisModule(mr.Signature);
                var (asm, ns, typeName) = ResolveMemberRefClass(caller, mr.ClassToken);
                return $"{asm}:{FormatTypeName(ns, typeName)}::{methodName} {sigKey}";
            }

            return $"<methodTok 0x{methodToken:X8}>";
        }

        private static string FormatMethodDef(RuntimeModule module, int methodDefToken)
        {
            int rid = MetadataToken.Rid(methodDefToken);
            var md = module.Md;
            var mrow = md.GetMethodDef(rid);
            string methodName = md.GetString(mrow.Name);
            string sigKey = module.GetSignatureKeyFromThisModule(mrow.Signature);

            int ownerTypeRid = FindOwningTypeRid(md, rid);
            string typeName = ownerTypeRid != 0
                ? FormatTypeDefFullName(module, ownerTypeRid)
                : "<unknownType>";

            return $"{module.Name}:{typeName}::{methodName} {sigKey}";
        }

        private static int FindOwningTypeRid(IMetadataView md, int methodRid)
        {
            for (int tdIndex = 0; tdIndex < md.GetRowCount(MetadataTableKind.TypeDef); tdIndex++)
            {
                int start = md.GetTypeDef(tdIndex + 1).MethodList;
                int end = (tdIndex + 1 < md.GetRowCount(MetadataTableKind.TypeDef)) 
                    ? md.GetTypeDef(tdIndex+2).MethodList : (md.GetRowCount(MetadataTableKind.MethodDef) + 1);
                if (methodRid >= start && methodRid < end)
                    return tdIndex + 1;
            }
            return 0;
        }

        private static string FormatTypeToken(RuntimeModule ctx, int typeToken)
        {
            int table = MetadataToken.Table(typeToken);
            int rid = MetadataToken.Rid(typeToken);
            var md = ctx.Md;

            switch (table)
            {
                case MetadataToken.TypeDef:
                    return $"{ctx.Name}:{FormatTypeDefFullName(ctx, rid)}";

                case MetadataToken.TypeRef:
                    {
                        var tr = md.GetTypeRef(rid);
                        string name = md.GetString(tr.Name);
                        string ns = md.GetString(tr.Namespace);

                        int scopeTable = MetadataToken.Table(tr.ResolutionScopeToken);
                        int scopeRid = MetadataToken.Rid(tr.ResolutionScopeToken);

                        if (scopeTable == MetadataToken.AssemblyRef)
                        {
                            var ar = md.GetAssemblyRef(scopeRid);
                            string asm = md.GetString(ar.Name);
                            return $"{asm}:{FormatTypeName(ns, name)}";
                        }

                        return $"{ctx.Name}:{FormatTypeName(ns, name)}";
                    }

                case MetadataToken.TypeSpec:

                    return $"{ctx.Name}:TypeSpec#{rid}";

                default:
                    return $"<typeTok 0x{typeToken:X8}>";
            }
        }

        private static string FormatFieldToken(RuntimeModule caller, int fieldToken)
        {
            int table = MetadataToken.Table(fieldToken);
            int rid = MetadataToken.Rid(fieldToken);
            var md = caller.Md;

            if (table == MetadataToken.FieldDef)
            {
                var f = md.GetField(rid);
                string name = md.GetString(f.Name);
                return $"{caller.Name}:<fielddef>::{name}";
            }

            if (table == MetadataToken.MemberRef)
            {
                var mr = md.GetMemberRef(rid);
                string name = md.GetString(mr.Name);
                var (asm, ns, typeName) = ResolveMemberRefClass(caller, mr.ClassToken);
                return $"{asm}:{FormatTypeName(ns, typeName)}::{name}";
            }

            return $"<fieldTok 0x{fieldToken:X8}>";
        }

        private static string FormatTypeDefFullName(RuntimeModule module, int typeDefRid)
        {
            var md = module.Md;
            var td = md.GetTypeDef(typeDefRid);
            string name = md.GetString(td.Name);
            string ns = md.GetString(td.Namespace);

            if (!string.IsNullOrEmpty(ns))
                return $"{ns}.{name}";

            // Namespace for nested is empty by design
            if (TryGetEnclosingTypeRid(md, typeDefRid, out int encRid))
            {
                // Build chain outer to inner
                var names = new List<string>();
                int cur = typeDefRid;
                names.Add(md.GetString(md.GetTypeDef(cur).Name));

                while (TryGetEnclosingTypeRid(md, cur, out int e))
                {
                    cur = e;
                    names.Add(md.GetString(md.GetTypeDef(cur).Name));
                }

                names.Reverse();
                string outerNs = md.GetString(md.GetTypeDef(cur).Namespace);
                string chain = string.Join("+", names);

                return string.IsNullOrEmpty(outerNs) ? chain : $"{outerNs}.{chain}";
            }

            return name;
        }

        private static bool TryGetEnclosingTypeRid(IMetadataView md, int nestedRid, out int enclosingRid)
        {
            for (int i = 0; i < md.GetRowCount(MetadataTableKind.NestedClass); i++)
            {
                var row = md.GetNestedClass(i + 1);
                if (row.NestedTypeRid == nestedRid)
                {
                    enclosingRid = row.EnclosingTypeRid;
                    return true;
                }
            }
            enclosingRid = 0;
            return false;
        }

        private static string FormatTypeName(string ns, string name)
            => string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }
    public static class BoundTreePrinter
    {
        public sealed class Options
        {
            public bool IncludeSyntaxKind { get; init; } = true;
            public bool IncludeSyntaxSpan { get; init; } = false;
            public bool IncludeTypes { get; init; } = true;
            public bool IncludeConstants { get; init; } = true;
            public bool IncludeConversionKind { get; init; } = false;
            public bool IncludeHasErrors { get; init; } = true;
            public bool IncludeIntrinsicInfo { get; init; } = false;
        }

        public static string Print(BoundNode node, Options? options = null)
        {
            options ??= new Options();
            var sb = new StringBuilder(8 * 1024);
            var w = new Writer(sb, options);
            w.WriteNode(node);
            return sb.ToString();
        }

        public static void PrintToConsole(BoundNode node, Options? options = null)
        {
            Console.WriteLine(Print(node, options));
        }
        public static MethodDeclarationSyntax? FindMain(CompilationUnitSyntax cu)
        {
            foreach (var m in cu.Members)
            {
                if (m is BaseNamespaceDeclarationSyntax ns)
                {
                    foreach (var nm in ns.Members)
                    {
                        if (nm is ClassDeclarationSyntax c && (c.Identifier.ValueText ?? "") == "Program")
                        {
                            foreach (var cm in c.Members)
                            {
                                if (cm is MethodDeclarationSyntax md && (md.Identifier.ValueText ?? "") == "Main")
                                    return md;
                            }
                        }
                    }
                }
            }

            return null;
        }
        private sealed class Writer
        {
            private readonly StringBuilder _sb;
            private readonly Options _opt;

            private readonly List<bool> _hasNextAtDepth = new();
            public Writer(StringBuilder sb, Options opt)
            {
                _sb = sb;
                _opt = opt;
            }
            public void WriteNode(BoundNode node)
            {
                WriteNodeCore(node, edgeLabel: null, isLast: true, isRoot: true);
            }

            private void WriteNodeCore(BoundNode? node, string? edgeLabel, bool isLast, bool isRoot)
            {
                if (!isRoot)
                {
                    EnsureLineStart();
                    WritePrefix(isLast);
                }


                if (edgeLabel != null)
                {
                    _sb.Append(edgeLabel);
                    _sb.Append(": ");
                }

                if (node is null)
                {
                    _sb.AppendLine("<null bound>");
                    return;
                }

                // Header line
                var title = GetTitle(node);
                _sb.Append(title);
                _sb.Append(FormatMeta(node));

                if (node is BoundExpression be)
                    _sb.Append(FormatExprExtras(be));

                var inline = FormatInline(node);
                if (inline.Length != 0)
                {
                    _sb.Append(' ');
                    _sb.Append(inline);
                }

                _sb.AppendLine();

                // Children
                var children = GetChildren(node);
                if (children.Length == 0)
                    return;

                if (!isRoot)
                    _hasNextAtDepth.Add(!isLast);

                for (int i = 0; i < children.Length; i++)
                {
                    var c = children[i];
                    bool childLast = (i == children.Length - 1);
                    WriteNodeCore(c.Node, c.Label, childLast, isRoot: false);
                }

                if (!isRoot)
                    _hasNextAtDepth.RemoveAt(_hasNextAtDepth.Count - 1);
            }
            private void EnsureLineStart()
            {
                if (_sb.Length == 0)
                    return;

                char last = _sb[_sb.Length - 1];
                if (last != '\n' && last != '\r')
                    _sb.AppendLine();
            }
            private void WritePrefix(bool isLast)
            {
                for (int i = 0; i < _hasNextAtDepth.Count; i++)
                    _sb.Append(_hasNextAtDepth[i] ? "│  " : "   ");

                _sb.Append(isLast ? "└─ " : "├─ ");
            }

            private static string GetTitle(BoundNode node)
            {
                return node switch
                {
                    BoundCompilationUnit => "BoundCompilationUnit",
                    BoundMethodBody => "BoundMethodBody",

                    BoundBlockStatement => "BoundBlockStatement",
                    BoundStatementList => "BoundStatementList",
                    BoundExpressionStatement => "BoundExpressionStatement",
                    BoundLocalDeclarationStatement => "BoundLocalDeclarationStatement",
                    BoundEmptyStatement => "BoundEmptyStatement",
                    BoundReturnStatement => "BoundReturnStatement",
                    BoundThrowStatement => "BoundThrowStatement",
                    BoundIfStatement => "BoundIfStatement",
                    BoundWhileStatement => "BoundWhileStatement",
                    BoundForStatement => "BoundForStatement",
                    BoundDoWhileStatement => "BoundDoWhileStatement",

                    BoundLabelStatement => "BoundLabelStatement",
                    BoundGotoStatement => "BoundGotoStatement",
                    BoundBreakStatement => "BoundBreakStatement",
                    BoundContinueStatement => "BoundContinueStatement",

                    BoundBadStatement => "BoundBadStatement",
                    BoundBadExpression => "BoundBadExpression",

                    BoundLiteralExpression => "BoundLiteralExpression",
                    BoundThisExpression => "BoundThisExpression",
                    BoundLocalExpression => "BoundLocalExpression",
                    BoundParameterExpression => "BoundParameterExpression",
                    BoundLabelExpression => "BoundLabelExpression",
                    BoundConversionExpression => "BoundConversionExpression",
                    BoundBinaryExpression => "BoundBinaryExpression",
                    BoundUnaryExpression => "BoundUnaryExpression",
                    BoundConditionalExpression => "BoundConditionalExpression",
                    BoundCallExpression => "BoundCallExpression",
                    BoundObjectCreationExpression => "BoundObjectCreationExpression",
                    BoundAssignmentExpression => "BoundAssignmentExpression",
                    BoundCompoundAssignmentExpression => "BoundCompoundAssignmentExpression",
                    BoundIncrementDecrementExpression => "BoundIncrementDecrementExpression",
                    BoundArrayInitializerExpression => "BoundArrayInitializerExpression",
                    BoundArrayCreationExpression => "BoundArrayCreationExpression",
                    BoundArrayElementAccessExpression => "BoundArrayElementAccessExpression",
                    BoundStackAllocArrayCreationExpression => "BoundStackAllocArrayCreationExpression",
                    BoundRefExpression => "BoundRefExpression",
                    BoundAddressOfExpression => "BoundAddressOfExpression",
                    BoundPointerIndirectionExpression => "BoundPointerIndirectionExpression",
                    BoundPointerElementAccessExpression => "BoundPointerElementAccessExpression",
                    BoundTupleExpression => "BoundTupleExpression",
                    BoundSequenceExpression => "BoundSequenceExpression",
                    BoundConditionalGotoStatement => "BoundConditionalGotoStatement",
                    BoundMemberAccessExpression => "BoundMemberAccessExpression",
                    BoundIndexerAccessExpression => "BoundIndexerAccessExpression",
                    BoundLocalFunctionStatement => "BoundLocalFunctionStatement",
                    BoundTryStatement => "BoundTryStatement",
                    BoundCatchBlock => "BoundCatchBlock",
                    BoundCheckedStatement => "BoundCheckedStatement",
                    BoundUncheckedStatement => "BoundUncheckedStatement",
                    BoundCheckedExpression => "BoundCheckedExpression",
                    BoundUncheckedExpression => "BoundUncheckedExpression",
                    BoundSizeOfExpression => "BoundSizeOfExpression",
                    _ => node.GetType().Name
                };
            }
            private readonly struct Child
            {
                public readonly string Label;
                public readonly BoundNode? Node;

                public Child(string label, BoundNode? node)
                {
                    Label = label;
                    Node = node;
                }
            }

            private static Child[] GetChildren(BoundNode node)
            {
                switch (node)
                {
                    case BoundCompilationUnit cu:
                        {
                            var list = new List<Child>(cu.Statements.Length + 2);
                            if (cu.TopLevelMethodBodyOpt != null)
                                list.Add(new Child("TopLevelMethodBody", cu.TopLevelMethodBodyOpt));

                            for (int i = 0; i < cu.Statements.Length; i++)
                                list.Add(new Child($"Statements[{i}]", cu.Statements[i]));

                            return list.ToArray();
                        }

                    case BoundMethodBody mb:
                        return new[] { new Child("Body", mb.Body) };

                    case BoundBlockStatement b:
                        {
                            var list = new Child[b.Statements.Length];
                            for (int i = 0; i < b.Statements.Length; i++)
                                list[i] = new Child($"Statements[{i}]", b.Statements[i]);
                            return list;
                        }

                    case BoundStatementList sl:
                        {
                            var list = new Child[sl.Statements.Length];
                            for (int i = 0; i < sl.Statements.Length; i++)
                                list[i] = new Child($"Statements[{i}]", sl.Statements[i]);
                            return list;
                        }

                    case BoundExpressionStatement es:
                        return new[] { new Child("Expression", es.Expression) };

                    case BoundLocalDeclarationStatement ld:
                        return new[] { new Child("Initializer", ld.Initializer) };

                    case BoundReturnStatement rs:
                        return new[] { new Child("Expression", rs.Expression) };

                    case BoundThrowStatement ts: // <-- add
                        return new[] { new Child("Expression", ts.ExpressionOpt) };

                    case BoundConversionExpression ce:
                        return new[] { new Child("Operand", ce.Operand) };

                    case BoundUnaryExpression un:
                        return new[] { new Child("Operand", un.Operand) };

                    case BoundBinaryExpression bin:
                        return new[]
                        {
                            new Child("Left", bin.Left),
                            new Child("Right", bin.Right)
                        };
                    case BoundConditionalExpression c:
                        return new[]
                        {
                            new Child("Condition", c.Condition),
                            new Child("WhenTrue", c.WhenTrue),
                            new Child("WhenFalse", c.WhenFalse),
                        };
                    case BoundAssignmentExpression asg:
                        return new[]
                        {
                        new Child("Left", asg.Left),
                        new Child("Right", asg.Right)
                        };
                    case BoundCallExpression call:
                        {
                            var list = new List<Child>(1 + call.Arguments.Length + 1);
                            list.Add(new Child("Receiver", call.ReceiverOpt));
                            for (int i = 0; i < call.Arguments.Length; i++)
                                list.Add(new Child($"Arguments[{i}]", call.Arguments[i]));
                            return list.ToArray();
                        }
                    case BoundIfStatement ifs:
                        return new[]
                        {
                            new Child("Condition", ifs.Condition),
                            new Child("Then", ifs.Then),
                            new Child("Else", ifs.ElseOpt),
                        };

                    case BoundWhileStatement wh:
                        return new[]
                        {
                            new Child("Condition", wh.Condition),
                            new Child("Body", wh.Body),
                        };

                    case BoundDoWhileStatement dw:
                        return new[]
                        {
                            new Child("Body", dw.Body),
                            new Child("Condition", dw.Condition),
                        };

                    case BoundForStatement fs:
                        {
                            var list = new List<Child>(fs.Initializers.Length + 1 + fs.Incrementors.Length + 1);

                            for (int i = 0; i < fs.Initializers.Length; i++)
                                list.Add(new Child($"Initializers[{i}]", fs.Initializers[i]));

                            list.Add(new Child("Condition", fs.ConditionOpt));

                            for (int i = 0; i < fs.Incrementors.Length; i++)
                                list.Add(new Child($"Incrementors[{i}]", fs.Incrementors[i]));

                            list.Add(new Child("Body", fs.Body));
                            return list.ToArray();
                        }

                    case BoundCompoundAssignmentExpression ca:
                        return new[]
                        {
                            new Child("Left", ca.Left),
                            new Child("Value", ca.Value),
                        };
                    case BoundArrayInitializerExpression ai:
                        {
                            var list = new Child[ai.Elements.Length];
                            for (int i = 0; i < ai.Elements.Length; i++)
                                list[i] = new Child($"Elements[{i}]", ai.Elements[i]);
                            return list;
                        }
                    case BoundArrayCreationExpression ac:
                        {
                            var list = new List<Child>(ac.DimensionSizes.Length + 1);
                            for (int i = 0; i < ac.DimensionSizes.Length; i++)
                                list.Add(new Child($"DimensionSizes[{i}]", ac.DimensionSizes[i]));
                            list.Add(new Child("Initializer", ac.InitializerOpt));
                            return list.ToArray();
                        }
                    case BoundArrayElementAccessExpression aea:
                        {
                            var list = new List<Child>(aea.Indices.Length + 1);
                            list.Add(new Child("Expression", aea.Expression));
                            for (int i = 0; i < aea.Indices.Length; i++)
                                list.Add(new Child($"Indices[{i}]", aea.Indices[i]));
                            return list.ToArray();
                        }
                    case BoundStackAllocArrayCreationExpression sa:
                        {
                            var list = new List<Child>(2);
                            list.Add(new Child("Count", sa.Count));
                            list.Add(new Child("Initializer", sa.InitializerOpt));
                            return list.ToArray();
                        }
                    case BoundRefExpression re:
                        return new[] { new Child("Operand", re.Operand) };
                    case BoundAddressOfExpression ao:
                        return new[] { new Child("Operand", ao.Operand) };
                    case BoundPointerIndirectionExpression pi:
                        return new[] { new Child("Operand", pi.Operand) };
                    case BoundPointerElementAccessExpression pea:
                        return new[]
                        {
                            new Child("Expression", pea.Expression),
                            new Child("Index", pea.Index),
                        };
                    case BoundTupleExpression t:
                        {
                            var list = new Child[t.Elements.Length];
                            for (int i = 0; i < t.Elements.Length; i++)
                            {
                                var name = (i < t.ElementNames.Length) ? t.ElementNames[i] : null;
                                var label = name is null
                                    ? $"Elements[{i}]"
                                    : $"Elements[{i}] ({name})";
                                list[i] = new Child(label, t.Elements[i]);
                            }
                            return list;
                        }
                    case BoundSequenceExpression seq:
                        {
                            var list = new List<Child>(seq.SideEffects.Length + 1);
                            for (int i = 0; i < seq.SideEffects.Length; i++)
                                list.Add(new Child($"SideEffects[{i}]", seq.SideEffects[i]));
                            list.Add(new Child("Value", seq.Value));
                            return list.ToArray();
                        }
                    case BoundConditionalGotoStatement cg:
                        return new[] { new Child("Condition", cg.Condition) };
                    case BoundObjectCreationExpression oc:
                        {
                            var list = new List<Child>(oc.Arguments.Length);
                            for (int i = 0; i < oc.Arguments.Length; i++)
                                list.Add(new Child($"Arguments[{i}]", oc.Arguments[i]));
                            return list.ToArray();
                        }
                    case BoundMemberAccessExpression ma:
                        {
                            return new[]
                            {
                                new Child("Receiver", ma.ReceiverOpt),
                            };
                        }

                    case BoundLocalFunctionStatement lfs:
                        return new[] { new Child("Body", lfs.Body) };

                    case BoundCheckedStatement cs:
                        return new[] { new Child("Statement", cs.Statement) };

                    case BoundUncheckedStatement us:
                        return new[] { new Child("Statement", us.Statement) };

                    case BoundCheckedExpression ce2:
                        return new[] { new Child("Expression", ce2.Expression) };

                    case BoundUncheckedExpression ue:
                        return new[] { new Child("Expression", ue.Expression) };

                    case BoundTryStatement ts:
                        {
                            var list = new List<Child>(1 + ts.CatchBlocks.Length + 1);
                            list.Add(new Child("TryBlock", ts.TryBlock));
                            for (int i = 0; i < ts.CatchBlocks.Length; i++)
                                list.Add(new Child($"CatchBlocks[{i}]", ts.CatchBlocks[i]));
                            if (ts.FinallyBlockOpt != null)
                                list.Add(new Child("FinallyBlock", ts.FinallyBlockOpt));
                            return list.ToArray();
                        }

                    case BoundCatchBlock cb:
                        return new[]
                        {
                            new Child("Filter", cb.FilterOpt),
                            new Child("Body", cb.Body),
                        };
                    case BoundIncrementDecrementExpression id:
                        return new[]
                        {
                            new Child("Target", id.Target),
                            new Child("Read", id.Read),
                            new Child("Value", id.Value),
                        };

                    default:
                        return Array.Empty<Child>();
                }
            }

            private string FormatMeta(BoundNode node)
            {
                var parts = new List<string>(4);

                if (_opt.IncludeHasErrors)
                    parts.Add($"HasErrors={node.HasErrors}");

                if (_opt.IncludeSyntaxKind && node.Syntax is not null)
                    parts.Add($"Syntax={node.Syntax.Kind}");

                if (_opt.IncludeSyntaxSpan && node.Syntax is not null)
                {
                    var sp = node.Syntax.Span;
                    parts.Add($"Span=[{sp.Start}..{sp.End})");
                }

                if (parts.Count == 0)
                    return "";

                return "  {" + string.Join(", ", parts) + "}";
            }
            private static string FormatSymbol(Symbol? s)
            {
                if (s is null) return "<null-symbol>";
                return s switch
                {
                    MethodSymbol m => FormatMethod(m),
                    LocalSymbol l => FormatLocal(l),
                    ParameterSymbol p => FormatParameter(p),
                    NamedTypeSymbol nt => nt.Name,
                    _ => s.ToString() ?? s.GetType().Name
                };
            }
            private string FormatExprExtras(BoundExpression expr)
            {
                var parts = new List<string>(2);

                if (_opt.IncludeTypes)
                    parts.Add($"Type={FormatType(expr.Type)}");

                if (_opt.IncludeConstants && expr.ConstantValueOpt.HasValue)
                    parts.Add($"Const={FormatConst(expr.ConstantValueOpt.Value)}");

                if (parts.Count == 0)
                    return "";

                return "  [" + string.Join(", ", parts) + "]";
            }

            private string FormatInline(BoundNode node)
            {
                switch (node)
                {
                    case BoundMethodBody mb:
                        return $"Method={FormatMethod(mb.Method)}";

                    case BoundLocalDeclarationStatement ld:
                        return $"Local={FormatLocal(ld.Local)}";

                    case BoundLiteralExpression lit:
                        return $"Value={FormatConst(lit.Value)}";

                    case BoundLocalExpression le:
                        return $"Local={FormatLocal(le.Local)}";

                    case BoundParameterExpression pe:
                        return $"Parameter={FormatParameter(pe.Parameter)}";

                    case BoundThrowStatement ts:
                        return $"Rethrow={(ts.ExpressionOpt is null ? "true" : "false")}";

                    case BoundConversionExpression ce:
                        {
                            var parts = new List<string>(2);
                            if (_opt.IncludeConversionKind)
                                parts.Add($"Conversion={ce.Conversion.Kind} (implicit={ce.Conversion.IsImplicit})");
                            if (ce.IsChecked)
                                parts.Add("Checked=true");
                            return parts.Count == 0 ? "" : string.Join(", ", parts);
                        }

                    case BoundUnaryExpression un:
                        {
                            var text = $"Operator={FormatUnaryOperator(un.OperatorKind)}";
                            if (un.IsChecked) text += ", Checked=true";
                            return text;
                        }
                    case BoundBinaryExpression bin:
                        {
                            var text = $"Operator={FormatBinaryOperator(bin.OperatorKind)}";
                            if (bin.IsChecked) text += ", Checked=true";
                            return text;
                        }

                    case BoundCallExpression call:
                        {
                            var m = FormatMethod(call.Method);
                            if (_opt.IncludeIntrinsicInfo && call.Method is IntrinsicMethodSymbol im)
                                m += $" <intrinsic: {im.IntrinsicName}>";
                            return $"Method={m}";
                        }

                    case BoundObjectCreationExpression oc:
                        {
                            var ctor = oc.ConstructorOpt is null ? "<default>" : FormatMethod(oc.ConstructorOpt);
                            return $"Ctor={ctor}, Args={oc.Arguments.Length}";
                        }

                    case BoundCompoundAssignmentExpression ca:
                        {
                            var text = $"Operator={FormatBinaryOperator(ca.OperatorKind)}";
                            if (ca.IsChecked) text += ", Checked=true";
                            return text;
                        }

                    case BoundIncrementDecrementExpression id:
                        {
                            var op = id.IsIncrement ? "++" : "--";
                            var form = id.IsPostfix ? "postfix" : "prefix";
                            var text = $"Operator={op}, Form={form}";
                            if (id.IsChecked) text += ", Checked=true";
                            return text;
                        }

                    case BoundBreakStatement bs:
                        return $"Target={FormatLabel(bs.TargetLabel)}";

                    case BoundContinueStatement cs:
                        return $"Target={FormatLabel(cs.TargetLabel)}";

                    case BoundWhileStatement wh:
                        return $"BreakLabel={FormatLabel(wh.BreakLabel)}, ContinueLabel={FormatLabel(wh.ContinueLabel)}";

                    case BoundDoWhileStatement dw:
                        return $"BreakLabel={FormatLabel(dw.BreakLabel)}, ContinueLabel={FormatLabel(dw.ContinueLabel)}";

                    case BoundForStatement fs:
                        return $"BreakLabel={FormatLabel(fs.BreakLabel)}, ContinueLabel={FormatLabel(fs.ContinueLabel)}";

                    case BoundThisExpression te:
                        return $"ContainingType={FormatType(te.ContainingType)}";

                    case BoundLabelExpression labelExpr:
                        return $"Label={FormatLabel(labelExpr.Label)}";

                    case BoundLabelStatement ls:
                        return $"Label={FormatLabel(ls.Label)}";

                    case BoundGotoStatement gs:
                        return $"Target={FormatLabel(gs.TargetLabel)}";

                    case BoundArrayInitializerExpression ai:
                        return $"Length={ai.Elements.Length}";

                    case BoundArrayCreationExpression ac: 
                        return $"ElementType={FormatType(ac.ElementType)}, Rank={ac.DimensionSizes.Length}, HasInitializer={(ac.InitializerOpt != null ? "true" : "false")}";

                    case BoundArrayElementAccessExpression aea: 
                        return $"Rank={aea.Indices.Length}, IsLValue={(aea.IsLValue ? "true" : "false")}";

                    case BoundStackAllocArrayCreationExpression sa:
                        return $"ElementType={FormatType(sa.ElementType)}";

                    case BoundRefExpression: 
                        return "Kind=ref";

                    case BoundConditionalGotoStatement cg:
                        return $"Target={FormatLabel(cg.TargetLabel)}, JumpIfTrue={(cg.JumpIfTrue ? "true" : "false")}";

                    case BoundSequenceExpression seq:
                        return $"Locals={FormatLocals(seq.Locals)}, SideEffects={FormatCount(seq.SideEffects)}, ValueType={FormatType(seq.Value.Type)}";

                    case BoundTupleExpression t:
                        return $"Arity={t.Elements.Length}{FormatTupleNames(t.ElementNames)}";

                    case BoundMemberAccessExpression ma:
                        return $"Member={FormatSymbol(ma.Member)}, IsLValue={(ma.IsLValue ? "true" : "false")}";

                    case BoundLocalFunctionStatement lfs:
                        return $"LocalFunction={FormatMethod(lfs.LocalFunction)}";

                    case BoundTryStatement ts:
                        return $"CatchBlocks={ts.CatchBlocks.Length}, HasFinally={(ts.FinallyBlockOpt != null ? "true" : "false")}";

                    case BoundCatchBlock cb:
                        return $"ExceptionType={FormatType(cb.ExceptionType)}";

                    case BoundCheckedStatement:
                        return "Context=checked";

                    case BoundUncheckedStatement:
                        return "Context=unchecked";

                    case BoundCheckedExpression:
                        return "Context=checked";

                    case BoundUncheckedExpression:
                        return "Context=unchecked";

                    default:
                        return "";
                }
            }
            private static string FormatLabel(LabelSymbol l)
                => l is null ? "<null-label>" : l.Name;
            private static string FormatUnaryOperator(BoundUnaryOperatorKind op)
                => op.ToString();
            private static string FormatBinaryOperator(BoundBinaryOperatorKind op)
                => op.ToString();

            private static string FormatType(TypeSymbol t)
                => t is null ? "<null-type>" : t.Name;

            private static string FormatLocal(LocalSymbol l)
            {
                if (l is null) return "<null-local>";

                var text = $"{l.Name}: {FormatType(l.Type)}";

                if (l.IsConst)
                {
                    text = "const " + text;
                    if (l.ConstantValueOpt.HasValue)
                        text += $" = {FormatConst(l.ConstantValueOpt.Value)}";
                }

                return text;
            }
            private static string FormatMethod(MethodSymbol m)
            {
                if (m is null) return "<null-method>";

                var containing = FormatContaining(m.ContainingSymbol);

                var name = m.Name ?? "<unnamed>";

                var typeArgs = m.TypeArguments;
                if (!typeArgs.IsDefaultOrEmpty)
                {
                    var sb = new StringBuilder();
                    sb.Append(name);
                    sb.Append('<');
                    for (int i = 0; i < typeArgs.Length; i++)
                    {
                        if (i != 0) sb.Append(", ");
                        sb.Append(FormatType(typeArgs[i]));
                    }
                    sb.Append('>');
                    name = sb.ToString();
                }

                var ret = FormatType(m.ReturnType);

                var ps = m.Parameters;
                if (ps.IsDefault) return $"{containing}{name}(): {ret}";

                var args = new string[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                    args[i] = FormatParameter(ps[i]);

                return $"{containing}{name}({string.Join(", ", args)}): {ret}";
            }

            private static string FormatContaining(Symbol? s)
                => s is NamedTypeSymbol nt ? nt.Name + "." : "";

            private static string FormatParameter(ParameterSymbol p)
                => p is null ? "<null-param>" : $"{p.Name}: {FormatType(p.Type)}";

            private static string FormatConst(object? value)
            {
                if (value is null) return "null";
                if (value is string s) return Quote(s);
                if (value is char c) return $"'{EscapeChar(c)}'";
                if (value is bool b) return b ? "true" : "false";
                return value.ToString() ?? "<const>";
            }
            private static string FormatCount<T>(System.Collections.Immutable.ImmutableArray<T> arr)
                => arr.IsDefault ? "<default>" : arr.Length.ToString();

            private static string FormatLocals(System.Collections.Immutable.ImmutableArray<LocalSymbol> locals)
            {
                if (locals.IsDefault) return "<default>";
                if (locals.Length == 0) return "0";

                var take = locals.Length <= 4 ? locals.Length : 4;
                var names = new string[take];
                for (int i = 0; i < take; i++)
                    names[i] = locals[i]?.Name ?? "<null>";

                if (take == locals.Length)
                    return $"{locals.Length} ({string.Join(", ", names)})";

                return $"{locals.Length} ({string.Join(", ", names)}, …)";
            }

            private static string FormatTupleNames(System.Collections.Immutable.ImmutableArray<string?> names)
            {
                if (names.IsDefaultOrEmpty) return "";

                bool any = false;
                for (int i = 0; i < names.Length; i++)
                {
                    if (names[i] != null) { any = true; break; }
                }
                if (!any) return "";

                var take = names.Length <= 6 ? names.Length : 6;
                var arr = new string[take];
                for (int i = 0; i < take; i++)
                    arr[i] = names[i] ?? "_";

                if (take == names.Length)
                    return $" Names=({string.Join(", ", arr)})";

                return $" Names=({string.Join(", ", arr)}, …)";
            }
            private static string Quote(string s)
                => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

            private static string EscapeChar(char c)
            {
                return c switch
                {
                    '\\' => "\\\\",
                    '\'' => "\\'",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    _ => c.ToString()
                };
            }
        }
    }
}
