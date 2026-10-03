using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Cnidaria.Cs
{
    public sealed class RuntimeModule : Cnidaria.Cs.IRuntimeMetadataModule
    {
        public string Name { get; }
        public EcmaMetadata Md { get; }

        public Dictionary<(string ns, string name), int> TypeDefByFullName { get; } = new();

        internal Dictionary<int, DllImportData> PInvokesByMethodToken { get; } = new();
        private readonly Dictionary<int, string> _sigKeyCache = new();
        private readonly Dictionary<int, CilMethodBody?> _cilBodies = new();
        private Dictionary<int, int>? _fieldRvaByRid;
        private readonly Dictionary<int, int> _enclosingByNestedRid = new();
        private readonly Dictionary<int, (string ns, string name)> _fullTypeNameCache = new();
        public RuntimeModule(string name, EcmaMetadata md)
        {
            Name = name;
            Md = md ?? throw new ArgumentNullException(nameof(md));

            BuildTypeIndex();
            BuildPInvokeIndex();
        }

        internal int GetFieldRva(int fieldRid)
        {
            if (_fieldRvaByRid is null)
            {
                _fieldRvaByRid = new Dictionary<int, int>();
                int count = Md.GetRowCount(MetadataTableKind.FieldRva);
                for (int rid = 1; rid <= count; rid++)
                {
                    var row = Md.GetFieldRva(rid);
                    _fieldRvaByRid[row.FieldRid] = row.Rva;
                }
            }
            return _fieldRvaByRid.TryGetValue(fieldRid, out int rva) ? rva : 0;
        }

        // ECMA-335 II.22.8 keeps ClassLayout sorted by Parent.
        internal bool TryGetClassLayout(int typeDefRid, out ClassLayoutRow layout)
        {
            int lo = 1, hi = Md.GetRowCount(MetadataTableKind.ClassLayout);
            while (lo <= hi)
            {
                int mid = (lo + hi) >>> 1;
                layout = Md.GetClassLayout(mid);
                if (layout.ParentTypeDefRid == typeDefRid)
                    return true;
                if (layout.ParentTypeDefRid < typeDefRid)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }
            layout = default;
            return false;
        }
        internal CilMethodBody? GetCilBody(int methodDefToken)
        {
            if (_cilBodies.TryGetValue(methodDefToken, out var body))
                return body;
            int rva = Md.GetMethodDef(MetadataToken.Rid(methodDefToken)).Rva;
            body = rva == 0 ? null : CilBodyReader.Read(Md, rva);
            _cilBodies.Add(methodDefToken, body);
            return body;
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

                case SigElementType.CMOD_REQD:
                case SigElementType.CMOD_OPT:
                    sb.Append("mod<");
                    AppendTypeDefOrRefKey(sb, r.ReadCompressedUInt());
                    sb.Append(',');
                    AppendTypeKey(sb, ref r);
                    sb.Append('>');
                    break;

                case SigElementType.FNPTR:
                    {
                        byte callingConvention = r.ReadByte();
                        uint parameterCount = r.ReadCompressedUInt();
                        sb.Append("fn<cc=").Append(callingConvention).Append(",pc=").Append(parameterCount).Append(",ret=");
                        AppendTypeKey(sb, ref r);
                        sb.Append(",args=[");
                        for (int i = 0; i < parameterCount; i++)
                            AppendTypeKey(sb, ref r);
                        sb.Append("]>");
                        break;
                    }

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
                var (asm, ns, name) = MetadataTypeNames.ResolveTypeRefFullName(this, rid); // nested aware
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

        private void BuildPInvokeIndex()
        {
            int count = Md.GetRowCount(MetadataTableKind.PInvokeMap);
            for (int rid = 1; rid <= count; rid++)
            {
                var row = Md.GetPInvokeMap(rid);
                if (MetadataToken.Table(row.MethodToken) != MetadataToken.MethodDef)
                    throw new InvalidOperationException($"P/Invoke map contains invalid method token 0x{row.MethodToken:X8}.");
                int methodRid = MetadataToken.Rid(row.MethodToken);
                if (methodRid <= 0 || methodRid > Md.GetRowCount(MetadataTableKind.MethodDef))
                    throw new InvalidOperationException($"P/Invoke map method token is out of range: 0x{row.MethodToken:X8}.");
                var method = Md.GetMethodDef(methodRid);
                var data = PInvokeMetadataFlags.Decode(
                    Md.GetString(Md.GetModuleRef(row.ImportScope).Name),
                    Md.GetString(row.ImportName),
                    row.MappingFlags,
                    (method.ImplFlags & MetadataFlagBits.PreserveSig) != 0);
                if (!PInvokesByMethodToken.TryAdd(row.MethodToken, data))
                    throw new InvalidOperationException($"Duplicate P/Invoke map for method token 0x{row.MethodToken:X8}.");
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

        public byte PeekByte()
        {
            if ((uint)_i >= (uint)_s.Length) throw new InvalidOperationException("Signature underflow.");
            return _s[_i];
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
    // Assembly-qualified names of metadata type references, as the runtime type system keys its types.
    internal static class MetadataTypeNames
    {
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



    public sealed class TargetInfo
    {
        public static TargetInfo RegisterBytecode32Bit { get; } = CreateRegisterBytecode(pointerSize: 4);
        public static TargetInfo RegisterBytecode64Bit { get; } = CreateRegisterBytecode(pointerSize: 8);
        public static TargetInfo CreateRegisterBytecode(int pointerSize) => new TargetInfo(
            architecture: TargetArchitectureKind.RegisterBytecode,
            pointerSize: pointerSize,
            generalRegisterSize: TargetArchitecture.GeneralRegisterSize,
            floatingRegisterSize: TargetArchitecture.FloatingRegisterSize,
            stackSlotSize: TargetArchitecture.StackSlotSize,
            stackAlignment: TargetArchitecture.StackAlignment,
            callFrameAlignment: TargetArchitecture.CallFrameAlignment);
        public static TargetInfo RiscV32 { get; } = ForArchitecture(TargetArchitectureKind.RiscV32);
        public static TargetInfo RiscV64 { get; } = ForArchitecture(TargetArchitectureKind.RiscV64);
        public static TargetInfo RVA23Linux { get; } = ForArchitecture(TargetArchitectureKind.RiscV64, OperatingSystemKind.Linux, TargetArchitectureFeatures.RVA23);
        public static TargetInfo X86 { get; } = ForArchitecture(TargetArchitectureKind.I386);
        public static TargetInfo X64 { get; } = ForArchitecture(TargetArchitectureKind.X86_64);
        public static TargetInfo X64Linux { get; } = ForArchitecture(TargetArchitectureKind.X86_64, OperatingSystemKind.Linux);
        public static TargetInfo X64Windows { get; } = ForArchitecture(TargetArchitectureKind.X86_64, OperatingSystemKind.Windows);
        public static TargetInfo Arm32 { get; } = ForArchitecture(TargetArchitectureKind.Arm32);
        public static TargetInfo Arm64 { get; } = ForArchitecture(TargetArchitectureKind.Arm64);

        public static TargetInfo Default => TargetArchitecture.PointerSize == 4 ? RegisterBytecode32Bit : RegisterBytecode64Bit;
        public static TargetInfo Default32Bit => RegisterBytecode32Bit;
        public static TargetInfo Default64Bit => RegisterBytecode64Bit;
        public static TargetInfo ForArchitecture(TargetArchitectureKind architecture,
            OperatingSystemKind operatingSystem = OperatingSystemKind.None,
            TargetArchitectureFeatures features = TargetArchitectureFeatures.None)
        {
            return architecture switch
            {
                TargetArchitectureKind.RegisterBytecode => RegisterBytecode32Bit.WithFeatures(features),
                TargetArchitectureKind.RegisterBytecode64 => RegisterBytecode64Bit.WithFeatures(features),
                TargetArchitectureKind.I386 => new TargetInfo(architecture, pointerSize: 4, 4, 8, 4, 4,
                operatingSystem == OperatingSystemKind.Windows ? 4 : 16,
                operatingSystem: operatingSystem, architectureFeatures: features | TargetArchitectureFeatures.X86Sse2),
                TargetArchitectureKind.X86_64 => new TargetInfo(architecture, pointerSize: 8, 8, 8, 8, 16, 16,
                operatingSystem: operatingSystem, architectureFeatures: features | TargetArchitectureFeatures.X86Sse2),
                TargetArchitectureKind.RiscV32 => new TargetInfo(architecture, pointerSize: 4, 4, 8, 4, 16, 16,
                operatingSystem: operatingSystem, architectureFeatures: features | TargetArchitectureFeatures.RiscVM | TargetArchitectureFeatures.RiscVF),
                TargetArchitectureKind.RiscV64 => new TargetInfo(architecture, pointerSize: 8, 8, 8, 8, 16, 16, operatingSystem: operatingSystem,
                architectureFeatures: features | TargetArchitectureFeatures.RiscVM | TargetArchitectureFeatures.RiscVF | TargetArchitectureFeatures.RiscVD),
                TargetArchitectureKind.Arm32 => new TargetInfo(architecture, pointerSize: 4, 4, 8, 4, 8, 8, operatingSystem: operatingSystem, architectureFeatures: features),
                TargetArchitectureKind.Arm64 => new TargetInfo(architecture, pointerSize: 8, 8, 8, 8, 16, 16, operatingSystem: operatingSystem,
                architectureFeatures: features | TargetArchitectureFeatures.ArmVfp | TargetArchitectureFeatures.ArmVfpD32
                | TargetArchitectureFeatures.ArmNeon | TargetArchitectureFeatures.ArmHardFloat),
                TargetArchitectureKind.Wasm32 => new TargetInfo(architecture, pointerSize: 4, 4, 8, 4, 16, 16, operatingSystem: operatingSystem, architectureFeatures: features),
                TargetArchitectureKind.Wasm64 => new TargetInfo(architecture, pointerSize: 8, 8, 8, 8, 16, 16, operatingSystem: operatingSystem, architectureFeatures: features),
                _ => throw new ArgumentOutOfRangeException(nameof(architecture))
            };
        }
        public TargetArchitectureKind Architecture { get; }
        public TargetArchitectureFeatures ArchitectureFeatures { get; }
        public TargetEndianness Endianness { get; }
        public OperatingSystemKind OperatingSystem { get; }
        public int PointerSize { get; }
        public int GeneralRegisterSize { get; }
        public int FloatingRegisterSize { get; }
        public int StackSlotSize { get; }
        public int StackAlignment { get; }
        public int CallFrameAlignment { get; }
        public int ObjectHeaderSize => PointerSize * 2;
        public int ManagedObjectHeaderSize => IsRegisterBytecode ? ObjectHeaderSize : PointerSize;
        public int SyncBlockSize => PointerSize;
        public int MinimumManagedObjectSize => PointerSize * 2;
        public int MinimumGcObjectSize => SyncBlockSize + MinimumManagedObjectSize;
        public int StringLengthOffset => ManagedObjectHeaderSize;
        public int StringFirstCharOffset => StringLengthOffset + 4;
        public int ArrayLengthOffset => ManagedObjectHeaderSize;
        public int ArrayDataOffset => ArrayLengthOffset + (ManagedObjectHeaderSize == PointerSize ? (Is64Bit ? 8 : 4) : 8);
        public bool Is32Bit => PointerSize == 4;
        public bool Is64Bit => PointerSize == 8;
        public bool IsRegisterBytecode => Architecture is TargetArchitectureKind.RegisterBytecode or TargetArchitectureKind.RegisterBytecode64;
        public bool IsRiscV => Architecture is TargetArchitectureKind.RiscV32 or TargetArchitectureKind.RiscV64;
        public bool IsX86 => Architecture is TargetArchitectureKind.I386 or TargetArchitectureKind.X86_64;
        public bool IsArm => Architecture is TargetArchitectureKind.Arm32 or TargetArchitectureKind.Arm64;
        public TargetInfo(
            TargetArchitectureKind architecture,
            int pointerSize,
            int generalRegisterSize,
            int floatingRegisterSize,
            int stackSlotSize,
            int stackAlignment,
            int callFrameAlignment,
            TargetEndianness endianness = TargetEndianness.Little,
            OperatingSystemKind operatingSystem = OperatingSystemKind.None,
            TargetArchitectureFeatures architectureFeatures = TargetArchitectureFeatures.None)
        {
            if (pointerSize is not 4 and not 8)
                throw new ArgumentOutOfRangeException(nameof(pointerSize));
            if (generalRegisterSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(generalRegisterSize));
            if (floatingRegisterSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(floatingRegisterSize));
            if (stackSlotSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(stackSlotSize));
            if (stackAlignment <= 0)
                throw new ArgumentOutOfRangeException(nameof(stackAlignment));
            if (callFrameAlignment <= 0)
                throw new ArgumentOutOfRangeException(nameof(callFrameAlignment));

            Architecture = architecture;
            ArchitectureFeatures = architectureFeatures;
            Endianness = endianness;
            OperatingSystem = operatingSystem;
            PointerSize = pointerSize;
            GeneralRegisterSize = generalRegisterSize;
            FloatingRegisterSize = floatingRegisterSize;
            StackSlotSize = stackSlotSize;
            StackAlignment = stackAlignment;
            CallFrameAlignment = callFrameAlignment;
        }

        public TargetInfo WithFeatures(TargetArchitectureFeatures features)
            => new TargetInfo(
                Architecture,
                PointerSize,
                GeneralRegisterSize,
                FloatingRegisterSize,
                StackSlotSize,
                StackAlignment,
                CallFrameAlignment,
                Endianness,
                OperatingSystem,
                features);

        public override string ToString()
            => Architecture.ToString();
    }
    public sealed class RuntimeTypeSystem
    {
        private readonly IReadOnlyDictionary<string, RuntimeModule> _modules;

        private readonly Dictionary<(string mod, int tok), RuntimeType> _typeCache = new();
        private readonly Dictionary<(string mod, int tok), RuntimeField> _fieldCache = new();
        private readonly Dictionary<(string mod, int tok), RuntimeMethod> _methodCache = new();

        private readonly Dictionary<(string mod, int token, int contextMethodId), RuntimeType> _typeInMethodContextCache = new();
        private readonly Dictionary<(string mod, int token, int contextMethodId), RuntimeField> _fieldInMethodContextCache = new();
        private readonly Dictionary<(string mod, int token, int contextMethodId), RuntimeMethod> _methodInMethodContextCache = new();

        private readonly Dictionary<(string asm, string ns, string name), RuntimeType> _namedTypes =
            new Dictionary<(string asm, string ns, string name), RuntimeType>();

        private readonly Dictionary<int, RuntimeType> _typeById = new();
        private readonly Dictionary<int, RuntimeMethod> _methodById = new();
        private readonly Dictionary<int, (RuntimeModule Module, int Rid)> _typeDefOrigins = new();
        private readonly Dictionary<int, List<int>> _methodImplRowsByTypeId = new();

        private int _nextTypeId = 1;
        private int _nextFieldId = 1;
        private int _nextMethodId = 1;



        public TargetInfo Target { get; }
        private readonly int ObjectHeaderSize;
        private readonly HashSet<int> _layoutDone = new();
        internal RuntimeType SystemObject { get; private set; } = null!;
        internal RuntimeType SystemString { get; private set; } = null!;
        internal RuntimeType SystemArray { get; private set; } = null!;
        internal RuntimeType SystemValueType { get; private set; } = null!;
        internal RuntimeType SystemEnum { get; private set; } = null!;
        private readonly Dictionary<string, RuntimeType> _constructedTypes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RuntimeMethod> _constructedMethods = new(StringComparer.Ordinal);
        public RuntimeTypeSystem(IReadOnlyDictionary<string, RuntimeModule> modules, TargetInfo? target = null)
        {
            _modules = modules ?? throw new ArgumentNullException(nameof(modules));
            Target = target ?? TargetInfo.Default;
            ObjectHeaderSize = Target.ManagedObjectHeaderSize;

            PrecreateAllTypeDefs();
            IndexWellKnownCoreTypes();
            BindBaseTypes();
            BindInterfaces();
            BuildAllFields();
            IndexMethodImpls();
            foreach (var t in _typeCache.Values)
                EnsureLayout(t);
        }
        public RuntimeMethod ResolveMethod(RuntimeModule module, int methodToken)
        {
            if (_methodCache.TryGetValue((module.Name, methodToken), out var cached))
                return cached;

            int table = MetadataToken.Table(methodToken);
            int rid = MetadataToken.Rid(methodToken);

            if (table == MetadataToken.MethodDef)
            {
                LoadMethodDefOwner(module, rid);
                return _methodCache[(module.Name, methodToken)];
            }

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

            // The signature is the definition's, so `M(!0)` and `M(int)` of `C<int>` are told apart before substitution.
            if (owner.GenericTypeDefinition is RuntimeType ownerDefinition)
            {
                for (int i = 0; i < ownerDefinition.Methods.Length; i++)
                {
                    var definition = ownerDefinition.Methods[i];
                    if (!StringComparer.Ordinal.Equals(definition.Name, methodName) ||
                        definition.HasThis != hasThis ||
                        definition.GenericArity != genericArity ||
                        !ReferenceEquals(definition.ReturnType, ret) ||
                        !SameTypes(definition.ParameterTypes, ps))
                    {
                        continue;
                    }

                    var constructed = owner.Methods[i];
                    _methodCache[(module.Name, methodToken)] = constructed;
                    return constructed;
                }
            }

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

            static bool SameTypes(RuntimeType[] a, RuntimeType[] b)
            {
                if (a.Length != b.Length)
                    return false;
                for (int i = 0; i < a.Length; i++)
                {
                    if (!ReferenceEquals(a[i], b[i]))
                        return false;
                }
                return true;
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
                    if (def.ArrayRank != actual.ArrayRank || def.IsSzArray != actual.IsSzArray)
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

                if (def.Kind == RuntimeTypeKind.FunctionPointer)
                    return FunctionPointerTypesCompatible(def, actual);

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

            var key = (module.Name, methodToken, methodContext.MethodId);
            if (_methodInMethodContextCache.TryGetValue(key, out var cached))
                return cached;

            int table = MetadataToken.Table(methodToken);
            int rid = MetadataToken.Rid(methodToken);

            if (table == MetadataToken.MethodDef)
            {
                var m = ResolveMethod(module, methodToken);
                var method = TryProjectMethodDefFromContext(m, methodContext);
                _methodInMethodContextCache[key] = method;
                return method;
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
                        {
                            _methodInMethodContextCache[key] = cand;
                            return cand;
                        }
                    }
                }
                var method = GetOrCreateConstructedMethod(genericMethod, methodArgs);
                _methodInMethodContextCache[key] = method;
                return method;
            }

            if (table != MetadataToken.MemberRef)
            {
                var method = ResolveMethod(module, methodToken);
                _methodInMethodContextCache[key] = method;
                return method;
            }

            {
                var resolved = ResolveMethod(module, methodToken);

                var ctxOwnerArgs = methodContext.DeclaringType.GenericTypeArguments;
                var ctxMethodArgs = methodContext.MethodGenericArguments;

                resolved = BindMethodToReceiver(resolved, methodContext.DeclaringType);

                RuntimeType substitutedOwner = SubstituteRuntimeType(resolved.DeclaringType, ctxOwnerArgs, ctxMethodArgs);
                if (!ReferenceEquals(substitutedOwner, resolved.DeclaringType))
                {
                    EnsureConstructedMembers(substitutedOwner);
                    resolved = BindMethodToReceiver(resolved, substitutedOwner);
                }

                if (ctxMethodArgs.Length != 0 &&
                    (resolved.GenericArity != 0 || UsesMethodTypeParameters(resolved)))
                {
                    var method = GetOrCreateConstructedMethod(resolved, ctxMethodArgs);
                    _methodInMethodContextCache[key] = method;
                    return method;
                }
                _methodInMethodContextCache[key] = resolved;
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
                    if (def.ArrayRank != actual.ArrayRank || def.IsSzArray != actual.IsSzArray)
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

                if (def.Kind == RuntimeTypeKind.FunctionPointer)
                    return FunctionPointerTypesCompatible(def, actual);

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
            if (t.Kind == RuntimeTypeKind.FunctionPointer)
            {
                if (t.FunctionPointerReturnType is not null && UsesTypeParameters(t.FunctionPointerReturnType, wantMethod))
                    return true;
                for (int i = 0; i < t.FunctionPointerParameterTypes.Length; i++)
                    if (UsesTypeParameters(t.FunctionPointerParameterTypes[i], wantMethod))
                        return true;
                return false;
            }
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

        private static bool FunctionPointerTypesCompatible(RuntimeType left, RuntimeType right)
        {
            if (left.Kind != RuntimeTypeKind.FunctionPointer || right.Kind != RuntimeTypeKind.FunctionPointer ||
                left.FunctionPointerCallingConvention != right.FunctionPointerCallingConvention ||
                left.FunctionPointerReturnByRef != right.FunctionPointerReturnByRef ||
                left.FunctionPointerReturnType is null || right.FunctionPointerReturnType is null ||
                left.FunctionPointerReturnType.TypeId != right.FunctionPointerReturnType.TypeId ||
                left.FunctionPointerParameterTypes.Length != right.FunctionPointerParameterTypes.Length)
                return false;

            for (int i = 0; i < left.FunctionPointerParameterTypes.Length; i++)
            {
                if (left.FunctionPointerParameterByRef[i] != right.FunctionPointerParameterByRef[i] ||
                    left.FunctionPointerParameterTypes[i].TypeId != right.FunctionPointerParameterTypes[i].TypeId)
                    return false;
            }

            return true;
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
            m.MethodDefToken = genericMethod.MethodDefToken;
            m.DllImportData = genericMethod.DllImportData;
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
        internal RuntimeType GetTypeById(int typeId)
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
        internal RuntimeType GetByRefType(RuntimeType elementType)
            => GetOrCreateByRefType(elementType);
        internal RuntimeType GetArrayType(RuntimeType elementType)
        {
            if (elementType is null) throw new ArgumentNullException(nameof(elementType));

            var t = GetOrCreateArrayType(elementType, rank: 1, isSzArray: true);
            EnsureLayout(t);
            return t;
        }

        internal RuntimeType GetArrayType(RuntimeType elementType, int rank)
        {
            if (elementType is null) throw new ArgumentNullException(nameof(elementType));
            if (rank <= 0) throw new ArgumentOutOfRangeException(nameof(rank));
            var t = rank == 1
                ? GetOrCreateArrayType(elementType, rank: 1, isSzArray: true)
                : GetOrCreateArrayType(elementType, rank, isSzArray: false);
            EnsureLayout(t);
            return t;
        }

        internal RuntimeType GetRequiredNamedType(string asm, string ns, string name)
            => FindRequired(asm, ns, name);

        internal RuntimeType GetPointerType(RuntimeType elementType)
        {
            if (elementType is null) throw new ArgumentNullException(nameof(elementType));
            var t = GetOrCreatePointerType(elementType);
            EnsureLayout(t);
            return t;
        }

        internal RuntimeType RegisterSyntheticType(string asm, string ns, string name, RuntimeTypeKind kind)
        {
            asm ??= string.Empty;
            ns ??= string.Empty;
            name ??= string.Empty;

            if (_namedTypes.TryGetValue((asm, ns, name), out var existing))
                return existing;

            var t = new RuntimeType(_nextTypeId++, kind, asm, ns, name)
            {
                IsFinal = kind is RuntimeTypeKind.Struct or RuntimeTypeKind.Enum or RuntimeTypeKind.FunctionPointer
            };
            if (kind == RuntimeTypeKind.Class && SystemObject is not null && !(ns == "System" && name == "Object"))
                t.BaseType = SystemObject;
            t.SizeOf = Target.PointerSize;
            t.AlignOf = Target.PointerSize;
            t.InstanceSize = kind == RuntimeTypeKind.Class ? Target.PointerSize : 0;
            t.StaticSize = 0;
            t.StaticAlign = 1;

            _namedTypes[(asm, ns, name)] = t;
            _typeById[t.TypeId] = t;
            _layoutDone.Add(t.TypeId);
            return t;
        }

        internal RuntimeMethod RegisterSyntheticStaticMethod(
            RuntimeType owner,
            string name,
            RuntimeType returnType,
            RuntimeType[] parameterTypes,
            ushort implFlags = 0,
            int methodId = 0)
        {
            if (owner is null) throw new ArgumentNullException(nameof(owner));
            if (returnType is null) throw new ArgumentNullException(nameof(returnType));
            parameterTypes ??= Array.Empty<RuntimeType>();
            name ??= string.Empty;

            if (methodId <= 0)
                methodId = _nextMethodId++;
            else if (methodId >= _nextMethodId)
                _nextMethodId = methodId + 1;

            if (_methodById.ContainsKey(methodId))
                throw new InvalidOperationException("Duplicate synthetic RuntimeMethod id: " + methodId.ToString());

            var flags = (ushort)(System.Reflection.MethodAttributes.Public |
                                 System.Reflection.MethodAttributes.Static |
                                 System.Reflection.MethodAttributes.HideBySig);
            var method = new RuntimeMethod(
                methodId,
                owner,
                name,
                returnType,
                parameterTypes,
                hasThis: false,
                isVirtual: false,
                isStatic: true,
                isNewSlot: false,
                isFinal: true,
                flags: flags,
                implFlags: implFlags);

            _methodById[method.MethodId] = method;
            var methods = owner.Methods;
            Array.Resize(ref methods, methods.Length + 1);
            methods[^1] = method;
            owner.Methods = methods;
            return method;
        }
        private void PrecreateAllTypeDefs()
        {
            foreach (var kv in _modules)
            {
                var m = kv.Value;
                for (int i = 0; i < m.Md.GetRowCount(MetadataTableKind.TypeDef); i++)
                {
                    int rid = i + 1;
                    if (m.Md.IsModuleTypeDef(rid))
                        continue;
                    int tok = MetadataToken.Make(MetadataToken.TypeDef, rid);

                    var (ns, name) = MetadataTypeNames.GetTypeDefFullNameByRid(m, rid);
                    var td = m.Md.GetTypeDef(i + 1);

                    RuntimeTypeKind kind = InferKindFromTypeDef(m, td);

                    var typeAttributes = (System.Reflection.TypeAttributes)td.Flags;
                    var rt = new RuntimeType(_nextTypeId++, kind, asm: m.Name, ns: ns, name: name)
                    {
                        IsBeforeFieldInit = (typeAttributes & System.Reflection.TypeAttributes.BeforeFieldInit) != 0,
                        IsFinal = kind is RuntimeTypeKind.Struct or RuntimeTypeKind.Enum ||
                            (typeAttributes & System.Reflection.TypeAttributes.Sealed) != 0,
                        IsByRefLike = HasCustomAttribute(m, tok, "System.Runtime.CompilerServices", "IsByRefLikeAttribute")
                    };
                    rt.Loader = this;
                    rt.MethodsPending = true;
                    _typeDefOrigins[rt.TypeId] = (m, rid);
                    _typeCache[(m.Name, tok)] = rt;
                    _namedTypes[(m.Name, ns, name)] = rt;
                    _typeById[rt.TypeId] = rt;
                }
            }
        }

        internal void EnsureRuntimeTypeReady(RuntimeType type)
        {
            if (type is null)
                throw new ArgumentNullException(nameof(type));

            EnsureLayout(type);
        }

        // Constructed generic, array, pointer and by-ref types are interned lazily while the IR is
        // imported. The backend reads sizes and field offsets directly, so every interned type has
        // to carry a computed layout once importing is done.
        internal void EnsureAllTypesReady()
        {
            // Type ids grow with interning, so one pass in id order also reaches the types a layout interns
            for (int id = 1; id < _nextTypeId; id++)
            {
                if (_typeById.TryGetValue(id, out RuntimeType? type))
                    EnsureLayout(type);
            }
        }
        private void EnsureLayout(RuntimeType? t)
        {
            if (t is null) return;
            EnsureConstructedMembers(t);
            if (_layoutDone.Contains(t.TypeId)) return;

            _layoutDone.Add(t.TypeId);
            if (TryGetPrimitiveLayout(t, out int primSize, out int primAlign, out RuntimePrimitiveKind primitiveKind))
            {
                t.PrimitiveKind = primitiveKind;
                t.SizeOf = primSize;
                t.AlignOf = primAlign;
                t.InstanceSize = t.IsReferenceType ? Target.PointerSize : primSize;
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
                    t.SizeOf = Target.PointerSize;
                    t.AlignOf = Target.PointerSize;
                    t.InstanceSize = Target.PointerSize;
                    t.ContainsGcPointers = true;
                    t.GcPointerOffsets = new[] { 0 };
                    LayoutStaticFields(t);
                    return;
                case RuntimeTypeKind.Pointer:
                case RuntimeTypeKind.FunctionPointer:
                    t.SizeOf = Target.PointerSize;
                    t.AlignOf = Target.PointerSize;
                    t.InstanceSize = Target.PointerSize;
                    t.ContainsGcPointers = false;
                    t.GcPointerOffsets = Array.Empty<int>();
                    LayoutStaticFields(t);
                    return;
                case RuntimeTypeKind.ByRef:
                    t.SizeOf = Target.PointerSize;
                    t.AlignOf = Target.PointerSize;
                    t.InstanceSize = Target.PointerSize;
                    t.ContainsGcPointers = true;
                    t.GcPointerOffsets = new[] { 0 };
                    LayoutStaticFields(t);
                    return;

                case RuntimeTypeKind.Array:
                case RuntimeTypeKind.Interface:
                case RuntimeTypeKind.Class:
                    {
                        t.SizeOf = Target.PointerSize;
                        t.AlignOf = Target.PointerSize;
                        t.ContainsGcPointers = true;
                        t.GcPointerOffsets = new[] { 0 };

                        if (t.Kind == RuntimeTypeKind.Array)
                            EnsureLayout(t.ElementType);

                        EnsureLayout(t.BaseType);

                        int offset = (t.BaseType != null)
                            ? t.BaseType.InstanceSize
                            : ObjectHeaderSize;

                        int maxAlign = Target.PointerSize;

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
                            if (t.PackingSize != 0)
                                fa = Math.Min(fa, t.PackingSize);
                            int repeat = (t.InlineArrayLength > 0 && ReferenceEquals(f, t.InlineArrayElementField))
                                ? t.InlineArrayLength
                                : 1;

                            offset = AlignUp(offset, fa);
                            f.Offset = offset;

                            for (int elementIndex = 0; elementIndex < repeat; elementIndex++)
                                AppendGcPointerOffsets(gcOffsets, offset + elementIndex * fs, f.FieldType);

                            offset += checked(fs * repeat);
                            if (fa > maxAlign) maxAlign = fa;
                        }

                        int size = Math.Max(AlignUp(offset, maxAlign), t.ClassSize);
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

            if (fieldType.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.FunctionPointer)
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
        internal RuntimeMethod? ResolveVirtualMethod(RuntimeMethod declaredMethod, RuntimeType objectType)
        {
            if (declaredMethod is null) throw new ArgumentNullException(nameof(declaredMethod));
            if (objectType is null) throw new ArgumentNullException(nameof(objectType));
            if (objectType.Kind == RuntimeTypeKind.Interface ||
                (!objectType.IsReferenceType && !objectType.IsValueType))
            {
                return null;
            }

            objectType = objectType.ArrayOfT ?? objectType;
            EnsureConstructedMembers(objectType);
            EnsureVirtualTable(objectType);

            RuntimeMethod dispatchDeclaration = declaredMethod.GenericMethodDefinition ?? declaredMethod;
            RuntimeType[] methodArguments = declaredMethod.MethodGenericArguments;

            RuntimeMethod? target;
            if (dispatchDeclaration.DeclaringType.Kind == RuntimeTypeKind.Interface)
            {
                target = ResolveInterfaceDispatchTarget(objectType, dispatchDeclaration);
            }
            else
            {
                if (!IsClassAssignableTo(objectType, dispatchDeclaration.DeclaringType))
                    return null;

                RuntimeMethod boundDeclaration = BindMethodToReceiver(dispatchDeclaration, objectType);
                if (!boundDeclaration.IsVirtual)
                    target = boundDeclaration;
                else
                    target = ResolveClassVirtualDispatchTarget(objectType, boundDeclaration, dispatchDeclaration);
            }

            return CloseDispatchTarget(target, methodArguments);
        }

        private RuntimeMethod? ResolveClassVirtualDispatchTarget(
            RuntimeType objectType,
            RuntimeMethod boundDeclaration,
            RuntimeMethod originalDeclaration)
        {
            int slot = boundDeclaration.VTableSlot;
            if (slot < 0)
                slot = originalDeclaration.VTableSlot;
            if ((uint)slot >= (uint)objectType.VTable.Length)
                return null;
            return objectType.VTable[slot];
        }

        private RuntimeMethod? CloseDispatchTarget(RuntimeMethod? target, RuntimeType[] methodArguments)
        {
            if (target is null || target.IsAbstract)
                return null;

            if (methodArguments.Length == 0)
                return target;

            RuntimeMethod genericTarget = target.GenericMethodDefinition ?? target;
            if (genericTarget.GenericArity != methodArguments.Length)
                return null;

            RuntimeType[] existingArguments = target.MethodGenericArguments;
            if (existingArguments.Length == methodArguments.Length)
            {
                bool same = true;
                for (int i = 0; i < existingArguments.Length; i++)
                {
                    if (existingArguments[i].TypeId != methodArguments[i].TypeId)
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                    return target;
            }

            return GetOrCreateConstructedMethod(genericTarget, methodArguments);
        }

        internal bool IsAssignableTo(RuntimeType actualType, RuntimeType targetType)
        {
            if (actualType is null)
                throw new ArgumentNullException(nameof(actualType));
            if (targetType is null)
                throw new ArgumentNullException(nameof(targetType));

            EnsureConstructedMembers(actualType);
            EnsureConstructedMembers(targetType);

            if (targetType.Kind == RuntimeTypeKind.Interface)
                return FindInterfaceImplementationAnchor(actualType, targetType) is not null;

            return IsClassAssignableTo(actualType, targetType);
        }

        private static bool IsClassAssignableTo(RuntimeType actualType, RuntimeType declaredType)
        {
            for (RuntimeType? current = actualType; current is not null; current = current.BaseType)
            {
                if (SameDispatchType(current, declaredType, allowOpenDefinition: true))
                    return true;
            }
            return false;
        }

        private RuntimeMethod? ResolveInterfaceDispatchTarget(RuntimeType actualType, RuntimeMethod declaredMethod)
        {
            RuntimeType? implementationAnchor = FindInterfaceImplementationAnchor(actualType, declaredMethod.DeclaringType);
            if (implementationAnchor is null)
                return null;

            for (RuntimeType? type = actualType; type is not null; type = type.BaseType)
            {
                EnsureConstructedMembers(type);
                RuntimeMethod? explicitImplementation = TryResolveExplicitInterfaceImpl(type, declaredMethod);
                if (explicitImplementation is not null)
                    return ResolveInterfaceImplementationOverride(actualType, explicitImplementation);

                if (type.TypeId == implementationAnchor.TypeId)
                    break;
            }

            RuntimeMethod? implicitImplementation = FindImplicitInterfaceImplementation(
                implementationAnchor,
                declaredMethod);
            if (implicitImplementation is not null)
                return ResolveInterfaceImplementationOverride(actualType, implicitImplementation);

            if (implementationAnchor.BaseType is not null)
            {
                RuntimeMethod? inheritedImplementation = ResolveInterfaceDispatchTarget(
                    implementationAnchor.BaseType,
                    declaredMethod);
                if (inheritedImplementation is not null)
                    return ResolveInterfaceImplementationOverride(actualType, inheritedImplementation);
            }

            return ResolveDefaultInterfaceDispatchTarget(actualType, declaredMethod);
        }

        private RuntimeMethod? ResolveDefaultInterfaceDispatchTarget(
            RuntimeType actualType,
            RuntimeMethod declaredMethod)
        {
            var interfaces = new Dictionary<int, RuntimeType>();
            for (RuntimeType? type = actualType; type is not null; type = type.BaseType)
            {
                EnsureConstructedMembers(type);
                RuntimeType[] directInterfaces = type.Interfaces;
                for (int i = 0; i < directInterfaces.Length; i++)
                    CollectInterfaceClosure(directInterfaces[i], interfaces);
            }

            var candidates = new List<KeyValuePair<RuntimeType, RuntimeMethod>>();
            foreach (RuntimeType interfaceType in interfaces.Values)
            {
                if (!InterfaceDerivesFromOrEquals(
                    interfaceType,
                    declaredMethod.DeclaringType,
                    new HashSet<int>()))
                {
                    continue;
                }

                RuntimeMethod? implementation = TryResolveExplicitInterfaceImpl(interfaceType, declaredMethod);
                if (implementation is null && SameInterfaceType(interfaceType, declaredMethod.DeclaringType))
                    implementation = BindMethodToReceiver(declaredMethod, interfaceType);

                if (implementation is not null)
                    candidates.Add(new KeyValuePair<RuntimeType, RuntimeMethod>(interfaceType, implementation));
            }

            RuntimeType? selectedOwner = null;
            RuntimeMethod? selectedMethod = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                RuntimeType candidateOwner = candidates[i].Key;
                bool mostSpecific = true;
                for (int j = 0; j < candidates.Count; j++)
                {
                    if (i == j)
                        continue;

                    RuntimeType otherOwner = candidates[j].Key;
                    if (!InterfaceDerivesFromOrEquals(candidateOwner, otherOwner, new HashSet<int>()))
                    {
                        mostSpecific = false;
                        break;
                    }
                }

                if (!mostSpecific)
                    continue;

                RuntimeMethod candidateMethod = candidates[i].Value;
                if (selectedMethod is null)
                {
                    selectedOwner = candidateOwner;
                    selectedMethod = candidateMethod;
                    continue;
                }

                if (selectedOwner!.TypeId != candidateOwner.TypeId ||
                    selectedMethod.MethodId != candidateMethod.MethodId)
                {
                    return null;
                }
            }

            return selectedMethod;
        }

        private void CollectInterfaceClosure(
            RuntimeType interfaceType,
            Dictionary<int, RuntimeType> interfaces)
        {
            if (!interfaces.TryAdd(interfaceType.TypeId, interfaceType))
                return;

            EnsureConstructedMembers(interfaceType);
            RuntimeType[] bases = interfaceType.Interfaces;
            for (int i = 0; i < bases.Length; i++)
                CollectInterfaceClosure(bases[i], interfaces);
        }

        private RuntimeType? FindInterfaceImplementationAnchor(RuntimeType actualType, RuntimeType targetInterface)
        {
            for (RuntimeType? current = actualType; current is not null; current = current.BaseType)
            {
                EnsureConstructedMembers(current);
                RuntimeType[] interfaces = current.Interfaces;
                var seen = new HashSet<int>();
                for (int i = 0; i < interfaces.Length; i++)
                {
                    if (InterfaceDerivesFromOrEquals(interfaces[i], targetInterface, seen))
                        return current;
                }
            }

            return null;
        }

        private RuntimeMethod? TryResolveExplicitInterfaceImpl(RuntimeType implementationType, RuntimeMethod declaredMethod)
        {
            Dictionary<int, RuntimeMethod>? map = implementationType.MethodImpls;
            if (map is null || map.Count == 0)
                return null;

            if (map.TryGetValue(declaredMethod.MethodId, out RuntimeMethod? exact))
                return ProjectRuntimeMethodToOwner(implementationType, exact);

            RuntimeMethod? matchedImplementation = null;
            foreach (KeyValuePair<int, RuntimeMethod> pair in map.OrderBy(static pair => pair.Key))
            {
                if (!_methodById.TryGetValue(pair.Key, out RuntimeMethod? interfaceMethod))
                    continue;

                interfaceMethod = BindMethodToReceiver(interfaceMethod, declaredMethod.DeclaringType);
                if (!SameInterfaceMethodIdentity(interfaceMethod, declaredMethod))
                    continue;

                RuntimeMethod implementation = ProjectRuntimeMethodToOwner(implementationType, pair.Value);
                if (matchedImplementation is not null &&
                    !SameDispatchMethod(matchedImplementation, implementation))
                {
                    throw new TypeLoadException(
                        $"Conflicting MethodImpl bodies implement " +
                        $"'{declaredMethod.DeclaringType.Namespace}.{declaredMethod.DeclaringType.Name}.{declaredMethod.Name}' " +
                        $"on '{implementationType.Namespace}.{implementationType.Name}'.");
                }

                matchedImplementation = implementation;
            }

            return matchedImplementation;
        }

        private static bool SameMethodDefinition(RuntimeMethod a, RuntimeMethod b)
            => a.MethodDefToken != 0 &&
               a.MethodDefToken == b.MethodDefToken &&
               ReferenceEquals(a.BodyModule, b.BodyModule);

        private RuntimeMethod ProjectRuntimeMethodToOwner(RuntimeType owner, RuntimeMethod method)
        {
            RuntimeMethod methodDefinition = method.GenericMethodDefinition ?? method;
            if (methodDefinition.DeclaringType.TypeId == owner.TypeId)
                return methodDefinition;

            EnsureConstructedMembers(owner);

            RuntimeType sourceOwner = methodDefinition.DeclaringType;
            RuntimeType sourceDefinition = sourceOwner.GenericTypeDefinition ?? sourceOwner;
            RuntimeType ownerDefinition = owner.GenericTypeDefinition ?? owner;

            if (sourceDefinition.TypeId == ownerDefinition.TypeId)
            {
                EnsureConstructedMembers(sourceOwner);
                RuntimeMethod[] sourceMethods = sourceOwner.Methods;
                RuntimeMethod[] ownerMethods = owner.Methods;
                int count = Math.Min(sourceMethods.Length, ownerMethods.Length);
                for (int i = 0; i < count; i++)
                {
                    RuntimeMethod sourceMethod = sourceMethods[i];
                    if (sourceMethod.MethodId == methodDefinition.MethodId ||
                        SameMethodDefinition(sourceMethod, methodDefinition))
                    {
                        return ownerMethods[i];
                    }
                }
            }

            RuntimeMethod[] methods = owner.Methods;
            for (int i = 0; i < methods.Length; i++)
            {
                RuntimeMethod candidate = methods[i];
                if (!StringComparer.Ordinal.Equals(candidate.Name, methodDefinition.Name) ||
                    candidate.GenericArity != methodDefinition.GenericArity ||
                    candidate.IsStatic != methodDefinition.IsStatic)
                {
                    continue;
                }

                if (SameMethodDefinition(candidate, methodDefinition))
                    return candidate;

                if (SameRuntimeSignature(candidate, methodDefinition))
                    return candidate;
            }

            return methodDefinition;
        }

        private RuntimeMethod? FindImplicitInterfaceImplementation(
            RuntimeType implementationAnchor,
            RuntimeMethod declaredMethod)
        {
            for (RuntimeType? type = implementationAnchor; type is not null; type = type.BaseType)
            {
                EnsureConstructedMembers(type);
                RuntimeMethod[] methods = type.Methods;
                for (int i = 0; i < methods.Length; i++)
                {
                    RuntimeMethod candidate = methods[i];
                    if (candidate.IsStatic ||
                        !candidate.IsPublic ||
                        !StringComparer.Ordinal.Equals(candidate.Name, declaredMethod.Name) ||
                        !SameRuntimeSignature(candidate, declaredMethod))
                    {
                        continue;
                    }

                    return candidate;
                }
            }

            return null;
        }

        private static RuntimeMethod? ResolveInterfaceImplementationOverride(
            RuntimeType actualType,
            RuntimeMethod implementation)
        {
            if (!implementation.IsVirtual)
                return implementation;

            int slot = implementation.VTableSlot;
            if ((uint)slot >= (uint)actualType.VTable.Length)
                return implementation;

            return actualType.VTable[slot];
        }

        private bool InterfaceDerivesFromOrEquals(RuntimeType current, RuntimeType target, HashSet<int> seen)
        {
            if (SameInterfaceType(current, target))
                return true;
            if (!seen.Add(current.TypeId))
                return false;

            EnsureConstructedMembers(current);
            RuntimeType[] interfaces = current.Interfaces;
            for (int i = 0; i < interfaces.Length; i++)
            {
                if (InterfaceDerivesFromOrEquals(interfaces[i], target, seen))
                    return true;
            }
            return false;
        }

        private static bool SameRuntimeSignature(RuntimeMethod left, RuntimeMethod right)
        {
            if (left.GenericArity != right.GenericArity ||
                left.ParameterTypes.Length != right.ParameterTypes.Length ||
                !SameSignatureType(left.ReturnType, right.ReturnType))
            {
                return false;
            }

            for (int i = 0; i < left.ParameterTypes.Length; i++)
            {
                if (!SameSignatureType(left.ParameterTypes[i], right.ParameterTypes[i]))
                    return false;
            }
            return true;
        }

        private static bool SameInterfaceMethodIdentity(RuntimeMethod interfaceMethod, RuntimeMethod declaredMethod)
        {
            RuntimeMethod interfaceDefinition = interfaceMethod.GenericMethodDefinition ?? interfaceMethod;
            RuntimeMethod declaredDefinition = declaredMethod.GenericMethodDefinition ?? declaredMethod;

            if (!StringComparer.Ordinal.Equals(interfaceDefinition.Name, declaredDefinition.Name) ||
                interfaceDefinition.GenericArity != declaredDefinition.GenericArity ||
                !SameRuntimeTypeDefinitionOrExact(interfaceDefinition.DeclaringType, declaredDefinition.DeclaringType) ||
                !SameRuntimeSignature(interfaceDefinition, declaredDefinition))
            {
                return false;
            }

            return true;
        }

        private static bool SameInterfaceType(RuntimeType implemented, RuntimeType declared)
            => SameDispatchType(implemented, declared, allowOpenDefinition: true);

        private static bool SameRuntimeTypeDefinitionOrExact(RuntimeType left, RuntimeType right)
        {
            if (left.TypeId == right.TypeId)
                return true;

            RuntimeType leftDefinition = left.GenericTypeDefinition ?? left;
            RuntimeType rightDefinition = right.GenericTypeDefinition ?? right;
            return leftDefinition.TypeId == rightDefinition.TypeId &&
                (left.GenericTypeDefinition is null || right.GenericTypeDefinition is null);
        }

        private static bool SameSignatureType(RuntimeType left, RuntimeType right)
        {
            if (left.TypeId == right.TypeId)
                return true;

            if (left.Kind != right.Kind)
                return false;

            if (left.Kind == RuntimeTypeKind.TypeParam)
            {
                return left.IsMethodGenericParameter == right.IsMethodGenericParameter &&
                    left.GenericParameterOrdinal == right.GenericParameterOrdinal;
            }

            if (left.Kind is RuntimeTypeKind.Array or RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef)
            {
                if (left.Kind == RuntimeTypeKind.Array &&
                    (left.ArrayRank != right.ArrayRank || left.IsSzArray != right.IsSzArray))
                {
                    return false;
                }

                return left.ElementType is not null &&
                    right.ElementType is not null &&
                    SameSignatureType(left.ElementType, right.ElementType);
            }

            if (left.Kind == RuntimeTypeKind.FunctionPointer)
            {
                if (left.FunctionPointerCallingConvention != right.FunctionPointerCallingConvention ||
                    left.FunctionPointerReturnByRef != right.FunctionPointerReturnByRef ||
                    left.FunctionPointerReturnType is null ||
                    right.FunctionPointerReturnType is null ||
                    !SameSignatureType(left.FunctionPointerReturnType, right.FunctionPointerReturnType) ||
                    left.FunctionPointerParameterTypes.Length != right.FunctionPointerParameterTypes.Length ||
                    left.FunctionPointerParameterByRef.Length != right.FunctionPointerParameterByRef.Length)
                {
                    return false;
                }

                for (int i = 0; i < left.FunctionPointerParameterTypes.Length; i++)
                {
                    if (left.FunctionPointerParameterByRef[i] != right.FunctionPointerParameterByRef[i] ||
                        !SameSignatureType(left.FunctionPointerParameterTypes[i], right.FunctionPointerParameterTypes[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            RuntimeType leftDefinition = left.GenericTypeDefinition ?? left;
            RuntimeType rightDefinition = right.GenericTypeDefinition ?? right;
            if (leftDefinition.TypeId != rightDefinition.TypeId)
                return false;

            RuntimeType[] leftArguments = left.GenericTypeArguments;
            RuntimeType[] rightArguments = right.GenericTypeArguments;
            if (leftArguments.Length != rightArguments.Length)
                return false;

            for (int i = 0; i < leftArguments.Length; i++)
            {
                if (!SameSignatureType(leftArguments[i], rightArguments[i]))
                    return false;
            }

            return leftArguments.Length != 0;
        }

        private static bool SameDispatchType(RuntimeType left, RuntimeType right, bool allowOpenDefinition)
        {
            if (left.TypeId == right.TypeId)
                return true;

            RuntimeType leftDefinition = left.GenericTypeDefinition ?? left;
            RuntimeType rightDefinition = right.GenericTypeDefinition ?? right;
            if (leftDefinition.TypeId != rightDefinition.TypeId)
                return false;

            bool leftOpen = left.GenericTypeDefinition is null;
            bool rightOpen = right.GenericTypeDefinition is null;
            if (leftOpen || rightOpen)
                return allowOpenDefinition;

            RuntimeType[] leftArguments = left.GenericTypeArguments;
            RuntimeType[] rightArguments = right.GenericTypeArguments;
            if (leftArguments.Length != rightArguments.Length)
                return false;

            for (int i = 0; i < leftArguments.Length; i++)
            {
                if (!SameDispatchType(leftArguments[i], rightArguments[i], allowOpenDefinition: false))
                    return false;
            }
            return true;
        }

        private static int AlignUp(int value, int align)
        {
            int mask = align - 1;
            return (value + mask) & ~mask;
        }

        internal (int size, int align) GetStorageSizeAlign(RuntimeType fieldType)
        {
            EnsureLayout(fieldType);

            if (fieldType.Kind == RuntimeTypeKind.TypeParam)
                return (Target.PointerSize, Target.PointerSize);

            // reference types stored as pointers
            if (fieldType.IsReferenceType)
                return (Target.PointerSize, Target.PointerSize);

            if (fieldType.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef or RuntimeTypeKind.FunctionPointer)
                return (Target.PointerSize, Target.PointerSize);

            return (fieldType.SizeOf, fieldType.AlignOf);
        }
        private bool TryGetPrimitiveLayout(RuntimeType t, out int size, out int align, out RuntimePrimitiveKind primitiveKind)
        {
            size = 0;
            align = 0;
            primitiveKind = RuntimePrimitiveKind.None;
            if (t.Namespace != "System") return false;

            switch (t.Name)
            {
                case "Void": primitiveKind = RuntimePrimitiveKind.Void; size = 0; align = 1; return true;
                case "Boolean": primitiveKind = RuntimePrimitiveKind.Boolean; size = 1; align = 1; return true;
                case "Char": primitiveKind = RuntimePrimitiveKind.Char; size = 2; align = 2; return true;
                case "SByte": primitiveKind = RuntimePrimitiveKind.Int8; size = 1; align = 1; return true;
                case "Byte": primitiveKind = RuntimePrimitiveKind.UInt8; size = 1; align = 1; return true;
                case "Int16": primitiveKind = RuntimePrimitiveKind.Int16; size = 2; align = 2; return true;
                case "UInt16": primitiveKind = RuntimePrimitiveKind.UInt16; size = 2; align = 2; return true;
                case "Int32": primitiveKind = RuntimePrimitiveKind.Int32; size = 4; align = 4; return true;
                case "UInt32": primitiveKind = RuntimePrimitiveKind.UInt32; size = 4; align = 4; return true;
                case "Single": primitiveKind = RuntimePrimitiveKind.Single; size = 4; align = 4; return true;
                case "Int64": primitiveKind = RuntimePrimitiveKind.Int64; size = 8; align = 8; return true;
                case "UInt64": primitiveKind = RuntimePrimitiveKind.UInt64; size = 8; align = 8; return true;
                case "Double": primitiveKind = RuntimePrimitiveKind.Double; size = 8; align = 8; return true;
                case "Decimal": primitiveKind = RuntimePrimitiveKind.Decimal; size = 16; align = 8; return true;
                case "IntPtr": primitiveKind = RuntimePrimitiveKind.IntPtr; size = Target.PointerSize; align = Target.PointerSize; return true;
                case "UIntPtr": primitiveKind = RuntimePrimitiveKind.UIntPtr; size = Target.PointerSize; align = Target.PointerSize; return true;
                default:
                    return false;
            }
        }
        internal RuntimeField ResolveFieldInMethodContext(RuntimeModule contextModule, int fieldToken, RuntimeMethod? methodContext)
        {
            if (methodContext is null)
                return ResolveField(contextModule, fieldToken);

            var key = (contextModule.Name, fieldToken, methodContext.MethodId);
            if (_fieldInMethodContextCache.TryGetValue(key, out var cachedField))
                return cachedField;

            int table = MetadataToken.Table(fieldToken);
            int rid = MetadataToken.Rid(fieldToken);

            var ctxOwner = methodContext.DeclaringType;
            var ownerTypeArgs = ctxOwner.GenericTypeArguments;
            var methodTypeArgs = methodContext.MethodGenericArguments;

            if (table == MetadataToken.FieldDef)
            {
                var field = ResolveField(contextModule, fieldToken);

                if (ctxOwner.GenericTypeDefinition is null)
                {
                    _fieldInMethodContextCache[key] = field;
                    return field;
                }

                // A field declared by a nested type of the context owner still shares the enclosing
                // type parameters, so its declaring type has to be instantiated with the same arguments.
                RuntimeType constructedOwner = ReferenceEquals(field.DeclaringType, ctxOwner.GenericTypeDefinition)
                    ? ctxOwner
                    : SubstituteRuntimeType(field.DeclaringType, ownerTypeArgs, methodTypeArgs);

                if (ReferenceEquals(constructedOwner, field.DeclaringType))
                {
                    _fieldInMethodContextCache[key] = field;
                    return field;
                }

                EnsureConstructedMembers(constructedOwner);
                EnsureLayout(constructedOwner);

                var defFields = field.IsStatic
                    ? field.DeclaringType.StaticFields
                    : field.DeclaringType.InstanceFields;

                var actualFields = field.IsStatic
                    ? constructedOwner.StaticFields
                    : constructedOwner.InstanceFields;

                for (int i = 0; i < defFields.Length && i < actualFields.Length; i++)
                {
                    if (ReferenceEquals(defFields[i], field))
                    {
                        _fieldInMethodContextCache[key] = actualFields[i];
                        return actualFields[i];
                    }
                }

                var expectedFieldType = SubstituteRuntimeType(field.FieldType, ownerTypeArgs, methodTypeArgs);

                for (int i = 0; i < actualFields.Length; i++)
                {
                    var cand = actualFields[i];
                    if (!StringComparer.Ordinal.Equals(cand.Name, field.Name))
                        continue;
                    if (!ReferenceEquals(cand.FieldType, expectedFieldType))
                        continue;
                    _fieldInMethodContextCache[key] = cand;
                    return cand;
                }

                _fieldInMethodContextCache[key] = field;
                return field;
            }

            if (table != MetadataToken.MemberRef)
            {
                var field = ResolveField(contextModule, fieldToken);
                _fieldInMethodContextCache[key] = field;
                return field;
            }


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
                owner = SubstituteRuntimeType(owner, ownerTypeArgs, methodTypeArgs);
            fieldType = SubstituteMemberRefFieldType(owner, fieldType);

            EnsureConstructedMembers(owner);
            EnsureLayout(owner);

            for (int i = 0; i < owner.InstanceFields.Length; i++)
            {
                var f = owner.InstanceFields[i];
                if (StringComparer.Ordinal.Equals(f.Name, fieldName) &&
                    ReferenceEquals(f.FieldType, fieldType))
                {
                    _fieldInMethodContextCache[key] = f;
                    return f;
                }
            }

            for (int i = 0; i < owner.StaticFields.Length; i++)
            {
                var f = owner.StaticFields[i];
                if (StringComparer.Ordinal.Equals(f.Name, fieldName) &&
                    ReferenceEquals(f.FieldType, fieldType))
                {
                    _fieldInMethodContextCache[key] = f;
                    return f;
                }
            }

            throw new MissingFieldException($"{owner.Namespace}.{owner.Name}.{fieldName} not found.");
        }
        internal RuntimeField ResolveField(RuntimeModule contextModule, int fieldToken)
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
            fieldType = SubstituteMemberRefFieldType(owner, fieldType);

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

        // A MemberRef field signature is the definition's, so VAR refers to the owner's own type arguments.
        private RuntimeType SubstituteMemberRefFieldType(RuntimeType owner, RuntimeType fieldType)
        {
            var ownerTypeArgs = owner.GenericTypeArguments ?? Array.Empty<RuntimeType>();
            return ownerTypeArgs.Length == 0 ? fieldType : SubstituteRuntimeType(fieldType, ownerTypeArgs);
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
                (asm, ns, name) = MetadataTypeNames.ResolveTypeRefFullName(caller, rid);
            }
            else if (table == MetadataToken.TypeDef)
            {
                var full = MetadataTypeNames.GetTypeDefFullNameByRid(caller, rid);
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
                    if (m.Md.IsModuleTypeDef(rid))
                        continue;
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
        private void IndexMethodImpls()
        {
            foreach (var kv in _modules)
            {
                var m = kv.Value;
                int count = m.Md.GetRowCount(MetadataTableKind.MethodImpl);
                for (int rid = 1; rid <= count; rid++)
                {
                    int classTok = MetadataToken.Make(MetadataToken.TypeDef, m.Md.GetMethodImpl(rid).ClassTypeDefRid);
                    if (!_typeCache.TryGetValue((m.Name, classTok), out var owner))
                        continue;
                    if (!_methodImplRowsByTypeId.TryGetValue(owner.TypeId, out var rows))
                        _methodImplRowsByTypeId[owner.TypeId] = rows = new List<int>();
                    rows.Add(rid);
                }
            }
        }

        // A compilation reaches a small part of the libraries, so a type decodes its methods and MethodImpls on first use
        internal void LoadMethods(RuntimeType type)
        {
            if (_typeDefOrigins.Remove(type.TypeId, out var origin))
            {
                type.MethodsPending = false;
                BuildMethodsForType(origin.Module, type, origin.Rid - 1, origin.Module.Md.GetTypeDef(origin.Rid));
                if (_methodImplRowsByTypeId.Remove(type.TypeId, out var rows))
                {
                    var methodImpls = new Dictionary<int, RuntimeMethod>(rows.Count);
                    foreach (int rid in rows)
                    {
                        var row = origin.Module.Md.GetMethodImpl(rid);
                        var body = ResolveMethod(origin.Module, row.BodyMethodToken);
                        var decl = ResolveMethod(origin.Module, row.DeclarationMethodToken);
                        methodImpls[decl.MethodId] = body;
                    }
                    type.MethodImpls = methodImpls;
                }
                return;
            }

            if (type.GenericTypeDefinition is RuntimeType genericDef)
            {
                EnsureConstructedMembers(type);
                type.MethodsPending = false;
                SubstituteMethods(type, genericDef);
                return;
            }

            type.MethodsPending = false;
        }

        private void LoadMethodDefOwner(RuntimeModule module, int methodRid)
        {
            int ownerRid = module.Md.GetMethodOwnerTypeDefRid(methodRid);
            if (ownerRid != 0 && _typeCache.TryGetValue((module.Name, MetadataToken.Make(MetadataToken.TypeDef, ownerRid)), out var owner))
                _ = owner.Methods;
        }

        private void IndexWellKnownCoreTypes()
        {
            SystemObject = FindRequired("std", "System", "Object");
            SystemString = FindRequired("std", "System", "String");
            SystemArray = FindRequired("std", "System", "Array");
            SystemValueType = FindRequired("std", "System", "ValueType");
            SystemEnum = FindRequired("std", "System", "Enum");
        }

        internal RuntimeType FindPrimitive(RuntimePrimitiveKind kind) => FindRequired("std", "System", kind switch
        {
            RuntimePrimitiveKind.Void => "Void",
            RuntimePrimitiveKind.Boolean => "Boolean",
            RuntimePrimitiveKind.Char => "Char",
            RuntimePrimitiveKind.Int8 => "SByte",
            RuntimePrimitiveKind.UInt8 => "Byte",
            RuntimePrimitiveKind.Int16 => "Int16",
            RuntimePrimitiveKind.UInt16 => "UInt16",
            RuntimePrimitiveKind.Int32 => "Int32",
            RuntimePrimitiveKind.UInt32 => "UInt32",
            RuntimePrimitiveKind.Int64 => "Int64",
            RuntimePrimitiveKind.UInt64 => "UInt64",
            RuntimePrimitiveKind.NativeInt => "IntPtr",
            RuntimePrimitiveKind.NativeUInt => "UIntPtr",
            RuntimePrimitiveKind.Single => "Single",
            RuntimePrimitiveKind.Double => "Double",
            RuntimePrimitiveKind.Decimal => "Decimal",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        });

        private RuntimeType FindRequired(string asm, string ns, string name)
        {
            if (!_namedTypes.TryGetValue((asm, ns, name), out var t))
                throw new TypeLoadException($"Core type not found: {asm}:{ns}.{name}");
            return t;
        }
        private void BuildAllFields()
        {
            foreach (var kv in _modules)
            {
                var m = kv.Value;
                for (int tdIndex = 0; tdIndex < m.Md.GetRowCount(MetadataTableKind.TypeDef); tdIndex++)
                {
                    int typeRid = tdIndex + 1;
                    if (m.Md.IsModuleTypeDef(typeRid))
                        continue;
                    int typeTok = MetadataToken.Make(MetadataToken.TypeDef, typeRid);
                    var declaringType = _typeCache[(m.Name, typeTok)];
                    var td = m.Md.GetTypeDef(tdIndex + 1);

                    BuildFieldsForType(m, declaringType, tdIndex, td);
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
                if ((attrs & System.Reflection.FieldAttributes.HasFieldRVA) != 0)
                {
                    rf.RvaModule = m;
                    rf.Rva = m.GetFieldRva(rid);
                }
                _fieldCache[(m.Name, fieldTok)] = rf;

                if (declaringType.Kind == RuntimeTypeKind.Enum && !isStatic && name == "value__")
                    declaringType.ElementType = fieldType;

                if (isStatic) stat.Add(rf);
                else inst.Add(rf);
            }

            declaringType.InstanceFields = inst.ToArray();
            declaringType.StaticFields = stat.ToArray();

            int typeTok = MetadataToken.Make(MetadataToken.TypeDef, tdIndex + 1);
            if (m.TryGetClassLayout(tdIndex + 1, out var classLayout))
            {
                declaringType.PackingSize = classLayout.PackingSize;
                declaringType.ClassSize = classLayout.ClassSize;
            }
            if (TryGetInlineArrayLengthFromMetadata(m, typeTok, out int inlineArrayLength))
            {
                declaringType.InlineArrayLength = inlineArrayLength;
                if (declaringType.Kind != RuntimeTypeKind.Struct)
                    throw new TypeLoadException($"Inline array type '{declaringType.Namespace}.{declaringType.Name}' must be a struct.");
                if (inlineArrayLength <= 0)
                    throw new TypeLoadException($"Inline array type '{declaringType.Namespace}.{declaringType.Name}' must have a positive length.");
                if (declaringType.InstanceFields.Length != 1)
                    throw new TypeLoadException($"Inline array type '{declaringType.Namespace}.{declaringType.Name}' must have exactly one instance field.");

                declaringType.InlineArrayElementField = declaringType.InstanceFields[0];
            }
        }

        private static bool TryGetInlineArrayLengthFromMetadata(RuntimeModule module, int typeToken, out int length)
        {
            var (start, end) = module.Md.GetCustomAttributeRange(typeToken);
            for (int rid = start; rid < end; rid++)
            {
                var row = module.Md.GetCustomAttribute(rid);
                if (!module.Md.IsAttribute(row.ConstructorToken, "System.Runtime.CompilerServices", "InlineArrayAttribute"))
                    continue;
                var reader = new CustomAttributeBlobReader(module.Md.GetBlob(row.Value));
                length = unchecked((int)reader.ReadUInt32());
                return true;
            }

            length = 0;
            return false;
        }
        // Equality of these types is equality of their bits; floating point is not (NaN, -0.0) and structs may override Equals.
        internal static bool IsBitwiseEquatable(RuntimeType type)
            => type.Kind is RuntimeTypeKind.Enum or RuntimeTypeKind.Pointer or RuntimeTypeKind.FunctionPointer ||
               type.PrimitiveKind is RuntimePrimitiveKind.Boolean or RuntimePrimitiveKind.Char or
                   RuntimePrimitiveKind.Int8 or RuntimePrimitiveKind.UInt8 or RuntimePrimitiveKind.Int16 or RuntimePrimitiveKind.UInt16 or
                   RuntimePrimitiveKind.Int32 or RuntimePrimitiveKind.UInt32 or RuntimePrimitiveKind.Int64 or RuntimePrimitiveKind.UInt64 or
                   RuntimePrimitiveKind.NativeInt or RuntimePrimitiveKind.NativeUInt;

        // Vector*.IsHardwareAccelerated recurse into themselves for the JIT to replace; no backend vectorizes.
        internal static bool IsHardwareAccelerationQuery(RuntimeMethod method)
            => method.IsStatic && StringComparer.Ordinal.Equals(method.Name, "get_IsHardwareAccelerated") &&
               method.DeclaringType.Namespace is "System.Numerics" or "System.Runtime.Intrinsics";
        internal static bool HasCustomAttribute(RuntimeModule module, int token, string @namespace, string name)
        {
            var (start, end) = module.Md.GetCustomAttributeRange(token);
            for (int rid = start; rid < end; rid++)
            {
                if (module.Md.IsAttribute(module.Md.GetCustomAttribute(rid).ConstructorToken, @namespace, name))
                    return true;
            }
            return false;
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
                rm.MethodDefToken = methodTok;
                rm.GenericArity = genericArity;
                if (m.PInvokesByMethodToken.TryGetValue(methodTok, out var dllImport))
                    rm.DllImportData = dllImport;
                _methodById[rm.MethodId] = rm;
                _methodCache[(m.Name, methodTok)] = rm;
                methods.Add(rm);
            }

            declaringType.Methods = methods.ToArray();
        }
        internal void EnsureVirtualTable(RuntimeType type)
        {
            if (type is null)
                throw new ArgumentNullException(nameof(type));

            if (type.Kind is not (RuntimeTypeKind.Class or
                RuntimeTypeKind.Interface or
                RuntimeTypeKind.Struct or
                RuntimeTypeKind.Enum or
                RuntimeTypeKind.Array))
            {
                return;
            }

            if (type.VTableBuildState == 2)
                return;
            if (type.VTableBuildState == 1)
                throw new TypeLoadException($"Circular virtual method table inheritance involving '{type.Namespace}.{type.Name}'.");

            if (type.ArrayOfT is RuntimeType arrayOfT)
            {
                EnsureVirtualTable(arrayOfT);
                type.VTable = arrayOfT.VTable;
                type.VTableBuildState = 2;
                return;
            }

            EnsureConstructedMembers(type);
            type.VTableBuildState = 1;
            try
            {
                if (type.BaseType is not null)
                {
                    EnsureConstructedMembers(type.BaseType);
                    EnsureVirtualTable(type.BaseType);
                }

                BuildVTableForType(type);
                type.VTableBuildState = 2;
            }
            catch
            {
                type.VTable = Array.Empty<RuntimeMethod>();
                type.VTableBuildState = 0;
                throw;
            }
        }

        private void BuildVTableForType(RuntimeType type)
        {
            RuntimeMethod[] baseVtable = type.BaseType?.VTable ?? Array.Empty<RuntimeMethod>();
            var vtable = new List<RuntimeMethod>(baseVtable.Length + 8);
            vtable.AddRange(baseVtable);

            RuntimeMethod[] methods = type.Methods;
            for (int i = 0; i < methods.Length; i++)
                methods[i].VTableSlot = -1;

            for (int i = 0; i < methods.Length; i++)
            {
                RuntimeMethod method = methods[i];
                if (method.IsStatic || !method.IsVirtual)
                    continue;

                RuntimeMethod? overriddenMethod = null;
                if (type.BaseType is not null && !method.IsNewSlot)
                    overriddenMethod = FindOverriddenVirtualMethod(type.BaseType, method);

                if (overriddenMethod is null)
                {
                    method.VTableSlot = vtable.Count;
                    vtable.Add(method);
                    continue;
                }

                int slot = overriddenMethod.VTableSlot;
                if ((uint)slot >= (uint)vtable.Count)
                {
                    throw new TypeLoadException(
                        $"Method '{type.Namespace}.{type.Name}.{method.Name}' has no inherited virtual slot.");
                }

                RuntimeMethod currentTarget = vtable[slot];
                if (overriddenMethod.IsFinal || currentTarget.IsFinal)
                {
                    RuntimeMethod finalMethod = currentTarget.IsFinal ? currentTarget : overriddenMethod;
                    throw new TypeLoadException(
                        $"Method '{type.Namespace}.{type.Name}.{method.Name}' overrides final method " +
                        $"'{finalMethod.DeclaringType.Namespace}.{finalMethod.DeclaringType.Name}.{finalMethod.Name}'.");
                }

                vtable[slot] = method;
                for (int aliasSlot = 0; aliasSlot < baseVtable.Length; aliasSlot++)
                {
                    if (aliasSlot != slot && SameDispatchMethod(baseVtable[aliasSlot], overriddenMethod))
                        vtable[aliasSlot] = method;
                }
                method.VTableSlot = slot;
            }

            ApplyClassMethodImpls(type, vtable);
            type.VTable = vtable.ToArray();
        }

        private RuntimeMethod? FindOverriddenVirtualMethod(RuntimeType baseType, RuntimeMethod method)
        {
            for (RuntimeType? current = baseType; current is not null; current = current.BaseType)
            {
                EnsureConstructedMembers(current);
                RuntimeMethod[] methods = current.Methods;
                for (int i = 0; i < methods.Length; i++)
                {
                    RuntimeMethod candidate = methods[i];
                    if (candidate.IsStatic || !candidate.IsVirtual || candidate.IsPrivate ||
                        !StringComparer.Ordinal.Equals(candidate.Name, method.Name) ||
                        !SameRuntimeSignature(candidate, method))
                    {
                        continue;
                    }

                    return candidate;
                }
            }

            return null;
        }

        private static bool SameDispatchMethod(RuntimeMethod left, RuntimeMethod right)
        {
            if (left.MethodId == right.MethodId)
                return true;

            RuntimeMethod leftDefinition = left.GenericMethodDefinition ?? left;
            RuntimeMethod rightDefinition = right.GenericMethodDefinition ?? right;
            return leftDefinition.MethodId == rightDefinition.MethodId;
        }

        private void ApplyClassMethodImpls(RuntimeType type, List<RuntimeMethod> vtable)
        {
            Dictionary<int, RuntimeMethod>? methodImpls = type.MethodImpls;
            if (methodImpls is null || methodImpls.Count == 0)
                return;

            var appliedBodiesBySlot = new Dictionary<int, RuntimeMethod>();
            foreach (KeyValuePair<int, RuntimeMethod> pair in methodImpls.OrderBy(static pair => pair.Key))
            {
                if (!_methodById.TryGetValue(pair.Key, out RuntimeMethod? declaration))
                    continue;

                RuntimeMethod declarationDefinition = declaration.GenericMethodDefinition ?? declaration;
                if (declarationDefinition.DeclaringType.Kind == RuntimeTypeKind.Interface ||
                    !declarationDefinition.IsVirtual)
                {
                    continue;
                }

                RuntimeMethod boundDeclaration = BindMethodToReceiver(declarationDefinition, type);
                int slot = boundDeclaration.VTableSlot;
                if ((uint)slot >= (uint)vtable.Count)
                {
                    throw new TypeLoadException(
                        $"MethodImpl declaration '{declarationDefinition.DeclaringType.Namespace}." +
                        $"{declarationDefinition.DeclaringType.Name}.{declarationDefinition.Name}' has no virtual slot.");
                }

                RuntimeMethod currentTarget = vtable[slot];
                RuntimeMethod body = ProjectRuntimeMethodToOwner(type, pair.Value);
                if (appliedBodiesBySlot.TryGetValue(slot, out RuntimeMethod? appliedBody) &&
                    !SameDispatchMethod(appliedBody, body))
                {
                    throw new TypeLoadException(
                        $"Conflicting MethodImpl bodies target virtual slot {slot} on " +
                        $"'{type.Namespace}.{type.Name}'.");
                }

                if (!SameDispatchMethod(currentTarget, body) &&
                    (declarationDefinition.IsFinal || currentTarget.IsFinal))
                {
                    RuntimeMethod finalMethod = currentTarget.IsFinal ? currentTarget : declarationDefinition;
                    throw new TypeLoadException(
                        $"MethodImpl on '{type.Namespace}.{type.Name}' overrides final method " +
                        $"'{finalMethod.DeclaringType.Namespace}.{finalMethod.DeclaringType.Name}.{finalMethod.Name}'.");
                }

                appliedBodiesBySlot[slot] = body;
                vtable[slot] = body;
            }
        }

        internal RuntimeType ResolveType(RuntimeModule contextModule, int typeToken)
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

            var (asm, ns, name) = MetadataTypeNames.ResolveTypeRefFullName(contextModule, typeRefRid);
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
                        return GetOrCreateArrayType(elem, rank: 1, isSzArray: true);
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

                        return GetOrCreateArrayType(elem, checked((int)rank), isSzArray: false);
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

                case SigElementType.CMOD_REQD:
                case SigElementType.CMOD_OPT:
                    _ = r.ReadCompressedUInt();
                    return ReadTypeSig(contextModule, ref r);

                case SigElementType.FNPTR:
                    {
                        byte callingConvention = r.ReadByte();
                        uint parameterCount = r.ReadCompressedUInt();
                        var returnType = ReadFunctionPointerSignatureType(contextModule, ref r, out bool returnByRef);
                        int parameterCountInt = checked((int)parameterCount);
                        var parameterTypes = new RuntimeType[parameterCountInt];
                        var parameterByRef = new bool[parameterCountInt];
                        for (int i = 0; i < parameterTypes.Length; i++)
                            parameterTypes[i] = ReadFunctionPointerSignatureType(contextModule, ref r, out parameterByRef[i]);
                        return GetOrCreateFunctionPointerType(
                            callingConvention,
                            returnType,
                            returnByRef,
                            parameterTypes,
                            parameterByRef);
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

        private RuntimeType ReadFunctionPointerSignatureType(
            RuntimeModule contextModule,
            ref SigReader reader,
            out bool isByRef)
        {
            isByRef = reader.PeekByte() == (byte)SigElementType.BYREF;
            if (isByRef)
                _ = reader.ReadByte();
            while (reader.PeekByte() == (byte)SigElementType.CMOD_REQD ||
                   reader.PeekByte() == (byte)SigElementType.CMOD_OPT)
            {
                _ = reader.ReadByte();
                _ = reader.ReadCompressedUInt();
            }
            return ReadTypeSig(contextModule, ref reader);
        }

        private RuntimeType GetOrCreateArrayType(RuntimeType elem, int rank, bool isSzArray)
        {
            if (rank <= 0) throw new ArgumentOutOfRangeException(nameof(rank));
            if (isSzArray && rank != 1) throw new ArgumentException("SZARRAY must have rank 1.", nameof(rank));

            string key = $"arr:{(isSzArray ? "sz:" : "md:")}{rank}:{elem.TypeId}";
            if (_constructedTypes.TryGetValue(key, out var t))
                return t;

            string suffix = isSzArray ? "[]" : rank == 1 ? "[*]" : "[" + new string(',', rank - 1) + "]";

            t = new RuntimeType(
                _nextTypeId++,
                RuntimeTypeKind.Array,
                asm: SystemArray.AssemblyName,
                ns: "System",
                name: elem.Name + suffix);

            t.BaseType = SystemArray;
            t.Loader = this;
            t.ElementType = elem;
            t.ArrayRank = rank;
            t.IsSzArray = isSzArray;
            t.IsFinal = true;

            _constructedTypes[key] = t;
            _typeById[t.TypeId] = t;
            // The instantiation may name T[] again, which is cached now
            if (isSzArray && elem.Kind is not (RuntimeTypeKind.Pointer or RuntimeTypeKind.FunctionPointer or RuntimeTypeKind.ByRef) &&
                !elem.IsByRefLike && _namedTypes.TryGetValue((SystemArray.AssemblyName, "System", "Array`1"), out var arrayOfT))
            {
                t.ArrayOfT = GetOrCreateGenericInstanceType(arrayOfT, new[] { elem });
            }
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
                if (args[i] is null)
                {
                    throw new TypeLoadException(
                        $"Generic instance '{genericDef.Namespace}.{genericDef.Name}' has an unresolved type argument at index {i}.");
                }
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
            t.Loader = this;
            t.MethodsPending = true;
            t.IsBeforeFieldInit = genericDef.IsBeforeFieldInit;
            t.IsFinal = genericDef.IsFinal;
            t.IsByRefLike = genericDef.IsByRefLike;

            _constructedTypes[key] = t;
            _typeById[t.TypeId] = t;
            return t;
        }
        internal void EnsureConstructedMembers(RuntimeType t)
        {
            if (t.ArrayOfT is RuntimeType arrayOfT)
            {
                EnsureConstructedMembers(arrayOfT);
                t.Interfaces = arrayOfT.Interfaces;
                return;
            }

            if (t.GenericTypeDefinition is null)
                return;

            var genericDef = t.GenericTypeDefinition;

            if (t.ConstructedMembersInitialized)
            {
                bool staleInstanceFields = genericDef.InstanceFields.Length != 0 && t.InstanceFields.Length == 0;
                bool staleStaticFields = genericDef.StaticFields.Length != 0 && t.StaticFields.Length == 0;
                bool staleInterfaces = genericDef.Interfaces.Length != 0 && t.Interfaces.Length == 0;

                if (!staleInstanceFields && !staleStaticFields && !staleInterfaces)
                    return;
            }
            t.ConstructedMembersInitialized = true;

            var typeArgs = t.GenericTypeArguments;

            t.InlineArrayLength = genericDef.InlineArrayLength;
            t.InlineArrayElementField = null;
            t.PackingSize = genericDef.PackingSize;
            t.ClassSize = genericDef.ClassSize;

            t.BaseType = genericDef.BaseType is null
                ? null
                : SubstituteRuntimeType(genericDef.BaseType, typeArgs);

            if (genericDef.Interfaces.Length != 0)
            {
                var interfaces = new RuntimeType[genericDef.Interfaces.Length];
                for (int i = 0; i < interfaces.Length; i++)
                    interfaces[i] = SubstituteRuntimeType(genericDef.Interfaces[i], typeArgs);
                t.Interfaces = interfaces;
            }

            if (genericDef.InstanceFields.Length != 0 || genericDef.StaticFields.Length != 0)
            {
                var inst = new RuntimeField[genericDef.InstanceFields.Length];
                var stat = new RuntimeField[genericDef.StaticFields.Length];

                for (int i = 0; i < genericDef.InstanceFields.Length; i++)
                {
                    var src = genericDef.InstanceFields[i];
                    var ft = SubstituteRuntimeType(src.FieldType, typeArgs);
                    inst[i] = new RuntimeField(_nextFieldId++, t, src.Name, ft, isStatic: false);
                    if (ReferenceEquals(src, genericDef.InlineArrayElementField))
                        t.InlineArrayElementField = inst[i];
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

            t.Methods = Array.Empty<RuntimeMethod>();
            t.MethodImpls = null;
            t.MethodsPending = true;

            if (t.BaseType is not null)
                EnsureConstructedMembers(t.BaseType);

            t.VTable = Array.Empty<RuntimeMethod>();
            t.VTableBuildState = 0;
        }

        private void SubstituteMethods(RuntimeType t, RuntimeType genericDef)
        {
            var typeArgs = t.GenericTypeArguments;
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
                    dst.MethodDefToken = src.MethodDefToken;
                    dst.DllImportData = src.DllImportData;
                    dst.GenericArity = src.GenericArity;
                    _methodById[dst.MethodId] = dst;
                    methods[i] = dst;
                }

                t.Methods = methods;
            }

            if (genericDef.MethodImpls is not null)
            {
                var map = new Dictionary<int, RuntimeMethod>(genericDef.MethodImpls.Count);
                foreach (KeyValuePair<int, RuntimeMethod> pair in genericDef.MethodImpls)
                    map[pair.Key] = BindMethodToReceiver(pair.Value, t);
                t.MethodImpls = map;
            }
        }
        internal RuntimeType ResolveTypeInMethodContext(RuntimeModule contextModule, int typeToken, RuntimeMethod? methodContext)
        {
            if (methodContext is null)
                return ResolveType(contextModule, typeToken);

            var key = (contextModule.Name, typeToken, methodContext.MethodId);
            if (_typeInMethodContextCache.TryGetValue(key, out var cached))
                return cached;

            var t = ResolveType(contextModule, typeToken);

            var result = SubstituteRuntimeType(
                t,
                methodContext.DeclaringType.GenericTypeArguments,
                methodContext.MethodGenericArguments);

            EnsureLayout(result);
            _typeInMethodContextCache[key] = result;
            return result;
        }
        internal byte[] GetFieldRvaData(RuntimeField field)
        {
            if (field.RvaModule is not RuntimeModule module || field.Rva == 0)
                throw new InvalidOperationException($"Field '{field.Name}' has no RVA data.");
            EnsureLayout(field.FieldType);
            return module.Md.GetRvaData(field.Rva, field.FieldType.SizeOf).ToArray();
        }
        // A calli signature is a stand-alone method signature; it reads the same as an FNPTR element body.
        internal RuntimeType ResolveCalliSignatureInMethodContext(RuntimeModule contextModule, int signatureToken, RuntimeMethod methodContext)
        {
            var row = contextModule.Md.GetStandAloneSig(MetadataToken.Rid(signatureToken));
            var blob = contextModule.Md.GetBlob(row.Signature);
            var withPrefix = new byte[blob.Length + 1];
            withPrefix[0] = (byte)SigElementType.FNPTR;
            blob.CopyTo(withPrefix.AsSpan(1));
            var reader = new SigReader(withPrefix);
            var type = SubstituteRuntimeType(
                ReadTypeSig(contextModule, ref reader),
                methodContext.DeclaringType.GenericTypeArguments,
                methodContext.MethodGenericArguments);
            EnsureLayout(type);
            return type;
        }
        internal RuntimeType[] ResolveLocalSignatureInMethodContext(RuntimeModule contextModule, int signatureToken, RuntimeMethod methodContext)
        {
            if (signatureToken == 0)
                return Array.Empty<RuntimeType>();
            var row = contextModule.Md.GetStandAloneSig(MetadataToken.Rid(signatureToken));
            var reader = new SigReader(contextModule.Md.GetBlob(row.Signature));
            if (reader.ReadByte() != 0x07)
                throw new BadImageFormatException($"StandAloneSig 0x{signatureToken:X8} is not a local signature.");
            var locals = new RuntimeType[checked((int)reader.ReadCompressedUInt())];
            for (int i = 0; i < locals.Length; i++)
            {
                if (reader.PeekByte() == (byte)SigElementType.PINNED)
                    _ = reader.ReadByte();
                var local = SubstituteRuntimeType(
                    ReadTypeSig(contextModule, ref reader),
                    methodContext.DeclaringType.GenericTypeArguments,
                    methodContext.MethodGenericArguments);
                EnsureLayout(local);
                locals[i] = local;
            }
            return locals;
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
                    return GetOrCreateArrayType(elem, type.ArrayRank <= 0 ? 1 : type.ArrayRank, type.IsSzArray);
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

            if (type.Kind == RuntimeTypeKind.FunctionPointer && type.FunctionPointerReturnType is not null)
            {
                var returnType = SubstituteRuntimeType(type.FunctionPointerReturnType, ownerTypeArgs, methodTypeArgs);
                bool changed = !ReferenceEquals(returnType, type.FunctionPointerReturnType);
                var parameterTypes = new RuntimeType[type.FunctionPointerParameterTypes.Length];
                for (int i = 0; i < parameterTypes.Length; i++)
                {
                    parameterTypes[i] = SubstituteRuntimeType(type.FunctionPointerParameterTypes[i], ownerTypeArgs, methodTypeArgs);
                    changed |= !ReferenceEquals(parameterTypes[i], type.FunctionPointerParameterTypes[i]);
                }

                if (changed)
                    return GetOrCreateFunctionPointerType(
                        type.FunctionPointerCallingConvention,
                        returnType,
                        type.FunctionPointerReturnByRef,
                        parameterTypes,
                        type.FunctionPointerParameterByRef);
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

        private RuntimeType GetOrCreateFunctionPointerType(
            byte callingConvention,
            RuntimeType returnType,
            bool returnByRef,
            RuntimeType[] parameterTypes,
            bool[] parameterByRef)
        {
            var keyBuilder = new StringBuilder();
            keyBuilder.Append("fnptr:").Append(callingConvention).Append(':').Append(returnByRef ? 'r' : 'v').Append(':').Append(returnType.TypeId);
            for (int i = 0; i < parameterTypes.Length; i++)
                keyBuilder.Append(':').Append(parameterByRef[i] ? 'r' : 'v').Append(':').Append(parameterTypes[i].TypeId);
            string key = keyBuilder.ToString();

            if (_constructedTypes.TryGetValue(key, out var existing))
                return existing;

            var type = new RuntimeType(
                _nextTypeId++,
                RuntimeTypeKind.FunctionPointer,
                asm: returnType.AssemblyName,
                ns: string.Empty,
                name: "delegate*");
            type.FunctionPointerCallingConvention = callingConvention;
            type.FunctionPointerReturnType = returnType;
            type.FunctionPointerReturnByRef = returnByRef;
            type.FunctionPointerParameterTypes = parameterTypes;
            type.FunctionPointerParameterByRef = parameterByRef;
            _constructedTypes[key] = type;
            _typeById[type.TypeId] = type;
            return type;
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
                var (ns, name) = MetadataTypeNames.GetTypeDefFullNameByRid(contextModule, rid);
                return (contextModule.Name, ns, name);
            }

            if (table == MetadataToken.TypeRef)
            {
                return MetadataTypeNames.ResolveTypeRefFullName(contextModule, rid);
            }

            if (table == MetadataToken.TypeSpec)
            {
                return (contextModule.Name, "", "typespec");
            }

            throw new NotSupportedException();
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
        FunctionPointer,
    }

    // Mirrors the flag constants of the BCL's System.RuntimeType.
    [Flags]
    internal enum RuntimeTypeInfoFlags
    {
        None = 0,
        ValueType = 1 << 0,
        Enum = 1 << 1,
        Primitive = 1 << 2,
        Array = 1 << 3,
        Pointer = 1 << 4,
        ByRef = 1 << 5,
        Interface = 1 << 6,
        GenericParameter = 1 << 7,
    }
    // What System.Type reports for a runtime type; every backend hands these to the BCL's RuntimeType.
    internal static class RuntimeTypeNames
    {
        public static string Name(RuntimeType type) => type.Kind switch
        {
            RuntimeTypeKind.Array => Name(type.ElementType!) + ArraySuffix(type),
            RuntimeTypeKind.Pointer => Name(type.ElementType!) + "*",
            RuntimeTypeKind.ByRef => Name(type.ElementType!) + "&",
            RuntimeTypeKind.TypeParam or RuntimeTypeKind.FunctionPointer => type.Name,
            _ => SimpleName(type.GenericTypeDefinition ?? type),
        };

        public static string? Namespace(RuntimeType type) => type.Kind switch
        {
            RuntimeTypeKind.Array or RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef => Namespace(type.ElementType!),
            RuntimeTypeKind.TypeParam or RuntimeTypeKind.FunctionPointer => null,
            _ => (type.GenericTypeDefinition ?? type).Namespace is { Length: > 0 } ns ? ns : null,
        };

        // Null while the type still depends on generic parameters, as in runtime.
        public static string? FullName(RuntimeType type) => ContainsGenericParameters(type) ? null : Format(type, fullName: true);

        public static string DisplayName(RuntimeType type) => Format(type, fullName: false);

        public static RuntimeTypeInfoFlags Flags(RuntimeType type)
        {
            var flags = RuntimeTypeInfoFlags.None;
            if (type.Kind is RuntimeTypeKind.Struct or RuntimeTypeKind.Enum)
                flags |= RuntimeTypeInfoFlags.ValueType;
            if (type.Kind == RuntimeTypeKind.Enum)
                flags |= RuntimeTypeInfoFlags.Enum;
            else if (type.PrimitiveKind is not (RuntimePrimitiveKind.None or RuntimePrimitiveKind.Void or RuntimePrimitiveKind.Decimal))
                flags |= RuntimeTypeInfoFlags.Primitive;
            flags |= type.Kind switch
            {
                RuntimeTypeKind.Array => RuntimeTypeInfoFlags.Array,
                RuntimeTypeKind.Pointer => RuntimeTypeInfoFlags.Pointer,
                RuntimeTypeKind.ByRef => RuntimeTypeInfoFlags.ByRef,
                RuntimeTypeKind.Interface => RuntimeTypeInfoFlags.Interface,
                RuntimeTypeKind.TypeParam => RuntimeTypeInfoFlags.GenericParameter,
                _ => RuntimeTypeInfoFlags.None,
            };
            return flags;
        }

        // Nested types carry their enclosing chain, "Outer+Inner", in the metadata name.
        private static string SimpleName(RuntimeType definition)
            => definition.Name.Substring(definition.Name.LastIndexOf('+') + 1);

        private static string ArraySuffix(RuntimeType array)
            => array.IsSzArray ? "[]" : array.ArrayRank == 1 ? "[*]" : "[" + new string(',', array.ArrayRank - 1) + "]";

        private static string Format(RuntimeType type, bool fullName)
        {
            switch (type.Kind)
            {
                case RuntimeTypeKind.Array:
                    return Format(type.ElementType!, fullName) + ArraySuffix(type);
                case RuntimeTypeKind.Pointer:
                    return Format(type.ElementType!, fullName) + "*";
                case RuntimeTypeKind.ByRef:
                    return Format(type.ElementType!, fullName) + "&";
                case RuntimeTypeKind.TypeParam:
                case RuntimeTypeKind.FunctionPointer:
                    return type.Name;
            }

            RuntimeType definition = type.GenericTypeDefinition ?? type;
            string qualified = definition.Namespace.Length == 0 ? definition.Name : definition.Namespace + "." + definition.Name;
            if (type.GenericTypeDefinition is null || type.GenericTypeArguments.Length == 0)
                return qualified;

            // FullName qualifies each argument with its assembly; ToString does not.
            var sb = new StringBuilder(qualified).Append('[');
            for (int i = 0; i < type.GenericTypeArguments.Length; i++)
            {
                RuntimeType argument = type.GenericTypeArguments[i];
                if (i != 0)
                    sb.Append(',');
                if (fullName)
                    sb.Append('[').Append(Format(argument, fullName: true)).Append(", ").Append(argument.AssemblyName).Append(']');
                else
                    sb.Append(Format(argument, fullName: false));
            }
            return sb.Append(']').ToString();
        }

        private static bool ContainsGenericParameters(RuntimeType type)
        {
            if (type.Kind == RuntimeTypeKind.TypeParam)
                return true;
            if (type.ElementType is { } element && ContainsGenericParameters(element))
                return true;
            foreach (RuntimeType argument in type.GenericTypeArguments)
            {
                if (ContainsGenericParameters(argument))
                    return true;
            }
            return false;
        }
    }
    internal enum RuntimePrimitiveKind : byte
    {
        None,
        Void,
        Boolean,
        Char,
        Int8,
        UInt8,
        Int16,
        UInt16,
        Int32,
        UInt32,
        Int64,
        UInt64,
        NativeInt,
        NativeUInt,
        Single,
        Double,
        Decimal,
        IntPtr = NativeInt,
        UIntPtr = NativeUInt,
    }
    internal sealed class RuntimeType
    {
        public int TypeId { get; }
        public RuntimeTypeKind Kind { get; }
        public string AssemblyName { get; }
        public string Namespace { get; }
        public string Name { get; }
        public RuntimePrimitiveKind PrimitiveKind { get; internal set; }
        public bool IsBeforeFieldInit { get; internal set; }
        public bool IsFinal { get; internal set; }
        public bool IsByRefLike { get; internal set; }

        public bool IsValueType => Kind is RuntimeTypeKind.Struct or RuntimeTypeKind.Enum or RuntimeTypeKind.FunctionPointer;
        public bool IsReferenceType => !IsValueType && Kind is not (RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef or RuntimeTypeKind.FunctionPointer);
        public RuntimeType? BaseType { get; internal set; }
        // NativeAOT's closest def type of an SZ array: its base type stays System.Array, but its vtable, interfaces and dispatch are Array<T>'s
        internal RuntimeType? ArrayOfT { get; set; }
        public RuntimeType? ElementType { get; internal set; }
        public byte FunctionPointerCallingConvention { get; internal set; }
        public RuntimeType? FunctionPointerReturnType { get; internal set; }
        public bool FunctionPointerReturnByRef { get; internal set; }
        public RuntimeType[] FunctionPointerParameterTypes { get; internal set; } = Array.Empty<RuntimeType>();
        public bool[] FunctionPointerParameterByRef { get; internal set; } = Array.Empty<bool>();
        public int ArrayRank { get; internal set; }
        public bool IsSzArray { get; internal set; }
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
        public int InlineArrayLength { get; internal set; }
        public RuntimeField? InlineArrayElementField { get; internal set; }
        internal int PackingSize { get; set; }
        internal int ClassSize { get; set; }
        public RuntimeType[] Interfaces { get; internal set; } = Array.Empty<RuntimeType>();
        public RuntimeField[] InstanceFields { get; internal set; } = Array.Empty<RuntimeField>();
        public RuntimeField[] StaticFields { get; internal set; } = Array.Empty<RuntimeField>();
        internal RuntimeTypeSystem? Loader { get; set; }
        internal bool MethodsPending { get; set; }
        public RuntimeMethod[] Methods
        {
            get
            {
                if (MethodsPending)
                    Loader!.LoadMethods(this);
                return _methods;
            }
            internal set => _methods = value;
        }
        public RuntimeMethod[] VTable
        {
            get
            {
                if (VTableBuildState == 0 && Loader is RuntimeTypeSystem loader)
                    loader.EnsureVirtualTable(this);
                return _vtable;
            }
            internal set => _vtable = value;
        }
        internal byte VTableBuildState { get; set; }
        public Dictionary<int, RuntimeMethod>? MethodImpls
        {
            get
            {
                if (MethodsPending)
                    Loader!.LoadMethods(this);
                return _methodImpls;
            }
            internal set => _methodImpls = value;
        }
        private RuntimeMethod[] _methods = Array.Empty<RuntimeMethod>();
        private RuntimeMethod[] _vtable = Array.Empty<RuntimeMethod>();
        private Dictionary<int, RuntimeMethod>? _methodImpls;
        public RuntimeType(int typeId, RuntimeTypeKind kind, string asm, string ns, string name)
        {
            TypeId = typeId;
            Kind = kind;
            AssemblyName = asm;
            Namespace = ns;
            Name = name;
        }
        public override string ToString()
        {
            return $"{Namespace}.{Name}";
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
        internal RuntimeModule? RvaModule { get; set; }
        internal int Rva { get; set; }
        public RuntimeField(int fieldId, RuntimeType declType, string name, RuntimeType fieldType, bool isStatic)
        {
            FieldId = fieldId;
            DeclaringType = declType;
            Name = name;
            FieldType = fieldType;
            IsStatic = isStatic;
        }
    }

    public sealed class RuntimeMethod
    {
        public int MethodId { get; }
        internal RuntimeType DeclaringType { get; }
        public string Name { get; }
        internal RuntimeType ReturnType { get; }
        internal RuntimeType[] ParameterTypes { get; }
        public bool HasThis { get; }
        public bool IsVirtual { get; }
        public bool IsStatic { get; }
        public bool IsNewSlot { get; }
        public bool IsFinal { get; }
        public bool IsAbstract => (((System.Reflection.MethodAttributes)Flags) & System.Reflection.MethodAttributes.Abstract) != 0;
        public ushort Flags { get; }
        public ushort ImplFlags { get; }
        public bool IsExtern => (ImplFlags & MetadataFlagBits.Extern) != 0 ||
            (Flags & (ushort)System.Reflection.MethodAttributes.PinvokeImpl) != 0;
        public DllImportData? DllImportData { get; internal set; }
        public bool HasInternalCall => (ImplFlags & MetadataFlagBits.InternalCall) != 0;
        public bool HasNoInlining => (ImplFlags & MetadataFlagBits.NoInlining) != 0;
        public bool HasAggressiveInlining => (ImplFlags & MetadataFlagBits.AggressiveInlining) != 0;
        private byte _doesNotReturn;
        internal bool DoesNotReturn
        {
            get
            {
                if (_doesNotReturn == 0)
                {
                    _doesNotReturn = BodyModule is not null && MethodDefToken != 0 &&
                        RuntimeTypeSystem.HasCustomAttribute(BodyModule, MethodDefToken, "System.Diagnostics.CodeAnalysis", "DoesNotReturnAttribute")
                        ? (byte)2 : (byte)1;
                }
                return _doesNotReturn == 2;
            }
        }
        internal bool RequiresClassInitializationEntryCheck { get; set; }
        public int VTableSlot { get; internal set; } = -1;
        public RuntimeModule? BodyModule { get; internal set; }
        internal int MethodDefToken { get; set; }
        internal CilMethodBody? CilBody => MethodDefToken == 0 ? null : BodyModule?.GetCilBody(MethodDefToken);
        public RuntimeMethod? GenericMethodDefinition { get; internal set; }
        internal RuntimeType[] MethodGenericArguments { get; set; } = Array.Empty<RuntimeType>();
        public int GenericArity { get; internal set; }
        public bool IsPrivate =>
            (((System.Reflection.MethodAttributes)Flags) &
            System.Reflection.MethodAttributes.MemberAccessMask) ==
            System.Reflection.MethodAttributes.Private;
        public bool IsPublic =>
            (((System.Reflection.MethodAttributes)Flags) &
            System.Reflection.MethodAttributes.MemberAccessMask) ==
            System.Reflection.MethodAttributes.Public;
        internal RuntimeMethod(
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
