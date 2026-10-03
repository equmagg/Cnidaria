using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Cnidaria.Cs
{
    public sealed class EcmaMetadata
    {
        private readonly byte[] _image;
        private readonly (int Rva, int VirtualSize, int RawPointer, int RawSize)[] _sections;
        private readonly int _stringsOffset;
        private readonly int _stringsSize;
        private readonly int _userStringsOffset;
        private readonly int _userStringsSize;
        private readonly int _blobOffset;
        private readonly int _blobSize;
        private readonly int _tablesOffset;
        private readonly EcmaTableLayout _layout;
        private readonly Dictionary<int, string> _strings = new();
        private readonly int[] _propertyGetters;
        private readonly int[] _propertySetters;
        private readonly bool _hasModuleTypeDef;

        public string ModuleName { get; }
        public int EntryPointToken { get; }
        public ReadOnlyMemory<byte> Image => _image;

        public EcmaMetadata(byte[] image)
        {
            _image = image ?? throw new ArgumentNullException(nameof(image));
            var span = image.AsSpan();
            if (span.Length < 0x40 || span[0] != (byte)'M' || span[1] != (byte)'Z')
                throw new BadImageFormatException("Missing DOS header.");

            int peOffset = ReadInt32(span, 0x3C);
            if (ReadInt32(span, peOffset) != 0x00004550)
                throw new BadImageFormatException("Missing PE signature.");
            int coff = peOffset + 4;
            int sectionCount = ReadUInt16(span, coff + 2);
            int optionalSize = ReadUInt16(span, coff + 16);
            int optional = coff + 20;
            int magic = ReadUInt16(span, optional);
            int dataDirectories = magic switch
            {
                0x10B => optional + 96,
                0x20B => optional + 112,
                _ => throw new BadImageFormatException($"Unknown optional header magic 0x{magic:X}."),
            };

            _sections = new (int, int, int, int)[sectionCount];
            int sectionTable = optional + optionalSize;
            for (int i = 0; i < sectionCount; i++)
            {
                int header = sectionTable + i * 40;
                _sections[i] = (ReadInt32(span, header + 12), ReadInt32(span, header + 8), ReadInt32(span, header + 20), ReadInt32(span, header + 16));
            }

            int cliHeaderRva = ReadInt32(span, dataDirectories + 14 * 8);
            if (cliHeaderRva == 0)
                throw new BadImageFormatException("The image has no CLI header.");
            int cliHeader = RvaToOffset(cliHeaderRva);
            int metadataRva = ReadInt32(span, cliHeader + 8);
            EntryPointToken = ReadInt32(span, cliHeader + 20);
            int metadata = RvaToOffset(metadataRva);

            if (ReadInt32(span, metadata) != 0x424A5342)
                throw new BadImageFormatException("Missing metadata root signature.");
            int versionLength = ReadInt32(span, metadata + 12);
            int streamHeaders = metadata + 16 + versionLength;
            int streamCount = ReadUInt16(span, streamHeaders + 2);
            int p = streamHeaders + 4;
            int tablesStream = -1;
            for (int i = 0; i < streamCount; i++)
            {
                int offset = metadata + ReadInt32(span, p);
                int size = ReadInt32(span, p + 4);
                p += 8;
                int nameStart = p;
                while (span[p] != 0)
                    p++;
                string name = Encoding.ASCII.GetString(span[nameStart..p]);
                p = nameStart + ((p - nameStart + 4) & ~3);
                switch (name)
                {
                    case "#~":
                        tablesStream = offset;
                        break;
                    case "#Strings":
                        _stringsOffset = offset;
                        _stringsSize = size;
                        break;
                    case "#US":
                        _userStringsOffset = offset;
                        _userStringsSize = size;
                        break;
                    case "#Blob":
                        _blobOffset = offset;
                        _blobSize = size;
                        break;
                }
            }
            if (tablesStream < 0)
                throw new BadImageFormatException("The metadata has no #~ stream.");

            byte heapSizes = span[tablesStream + 6];
            ulong valid = BinaryPrimitives.ReadUInt64LittleEndian(span[(tablesStream + 8)..]);
            var rowCounts = new int[(int)EcmaTable.Count];
            int rows = tablesStream + 24;
            for (int t = 0; t < 64; t++)
            {
                if ((valid & (1ul << t)) == 0)
                    continue;
                if (t >= (int)EcmaTable.Count)
                    throw new BadImageFormatException($"Unsupported metadata table 0x{t:X2}.");
                rowCounts[t] = ReadInt32(span, rows);
                rows += 4;
            }
            _tablesOffset = rows;
            _layout = new EcmaTableLayout(rowCounts, (heapSizes & 0x01) != 0, (heapSizes & 0x02) != 0, (heapSizes & 0x04) != 0);

            IndexStrings();
            _propertyGetters = new int[rowCounts[(int)EcmaTable.Property] + 1];
            _propertySetters = new int[rowCounts[(int)EcmaTable.Property] + 1];
            for (int rid = 1; rid <= rowCounts[(int)EcmaTable.MethodSemantics]; rid++)
            {
                uint semantics = Read(EcmaTable.MethodSemantics, rid, 0);
                int method = MetadataToken.Make(MetadataToken.MethodDef, (int)Read(EcmaTable.MethodSemantics, rid, 1));
                int association = EcmaCodedIndex.Decode(EcmaCodedKind.HasSemantics, Read(EcmaTable.MethodSemantics, rid, 2));
                if (MetadataToken.Table(association) != MetadataToken.PropertyDef)
                    continue;
                int property = MetadataToken.Rid(association);
                if ((semantics & 0x0002) != 0)
                    _propertyGetters[property] = method;
                if ((semantics & 0x0001) != 0)
                    _propertySetters[property] = method;
            }

            ModuleName = rowCounts[(int)EcmaTable.Assembly] != 0
                ? GetString((int)Read(EcmaTable.Assembly, 1, 7))
                : GetString((int)Read(EcmaTable.Module, 1, 1));
            _hasModuleTypeDef = rowCounts[(int)EcmaTable.TypeDef] != 0 &&
                GetString((int)Read(EcmaTable.TypeDef, 1, 1)) == "<Module>" &&
                Read(EcmaTable.TypeDef, 1, 2) == 0;
        }

        public bool IsModuleTypeDef(int rid) => rid == 1 && _hasModuleTypeDef;
        public (int Start, int End) GetFieldRange(int typeRid)
        {
            int count = _layout.RowCounts[(int)EcmaTable.TypeDef];
            int start = (int)Read(EcmaTable.TypeDef, typeRid, 4);
            int end = typeRid < count ? (int)Read(EcmaTable.TypeDef, typeRid + 1, 4) : _layout.RowCounts[(int)EcmaTable.Field] + 1;
            return (start, end);
        }
        public (int Start, int End) GetMethodRange(int typeRid)
        {
            int count = _layout.RowCounts[(int)EcmaTable.TypeDef];
            int start = (int)Read(EcmaTable.TypeDef, typeRid, 5);
            int end = typeRid < count ? (int)Read(EcmaTable.TypeDef, typeRid + 1, 5) : _layout.RowCounts[(int)EcmaTable.MethodDef] + 1;
            return (start, end);
        }
        public (int Start, int End) GetParamRange(int methodRid)
        {
            int count = _layout.RowCounts[(int)EcmaTable.MethodDef];
            int start = (int)Read(EcmaTable.MethodDef, methodRid, 5);
            int end = methodRid < count ? (int)Read(EcmaTable.MethodDef, methodRid + 1, 5) : _layout.RowCounts[(int)EcmaTable.Param] + 1;
            return (start, end);
        }
        public int FindParamRid(int methodRid, int sequence)
        {
            var (start, end) = GetParamRange(methodRid);
            for (int rid = start; rid < end; rid++)
            {
                if (Read(EcmaTable.Param, rid, 1) == sequence)
                    return rid;
            }
            return 0;
        }
        public int GetMethodOwnerTypeDefRid(int methodRid) => FindOwner(EcmaTable.TypeDef, 5, methodRid);
        public int GetParamOwnerMethodRid(int paramRid) => FindOwner(EcmaTable.MethodDef, 5, paramRid);
        private int FindOwner(EcmaTable ownerTable, int listColumn, int childRid)
        {
            int lo = 1;
            int hi = _layout.RowCounts[(int)ownerTable];
            int found = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) >>> 1;
                if (Read(ownerTable, mid, listColumn) <= childRid)
                {
                    found = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return found;
        }
        public bool TryGetAttributeTypeName(int constructorToken, out string @namespace, out string name)
        {
            int typeToken = MetadataToken.Table(constructorToken) switch
            {
                MetadataToken.MethodDef => MetadataToken.Make(MetadataToken.TypeDef, GetMethodOwnerTypeDefRid(MetadataToken.Rid(constructorToken))),
                MetadataToken.MemberRef => GetMemberRef(MetadataToken.Rid(constructorToken)).ClassToken,
                _ => 0,
            };
            if (MetadataToken.Table(typeToken) == MetadataToken.TypeSpec)
            {
                var signature = new SigReader(GetBlob(GetTypeSpec(MetadataToken.Rid(typeToken)).Signature));
                if ((SigElementType)signature.ReadByte() == SigElementType.GENERICINST)
                {
                    _ = signature.ReadByte();
                    typeToken = EcmaCodedIndex.Decode(EcmaCodedKind.TypeDefOrRef, signature.ReadCompressedUInt());
                }
            }
            switch (MetadataToken.Table(typeToken))
            {
                case MetadataToken.TypeDef when MetadataToken.Rid(typeToken) != 0:
                    var typeDef = GetTypeDef(MetadataToken.Rid(typeToken));
                    @namespace = GetString(typeDef.Namespace);
                    name = GetString(typeDef.Name);
                    return true;
                case MetadataToken.TypeRef:
                    var typeRef = GetTypeRef(MetadataToken.Rid(typeToken));
                    @namespace = GetString(typeRef.Namespace);
                    name = GetString(typeRef.Name);
                    return true;
                default:
                    @namespace = string.Empty;
                    name = string.Empty;
                    return false;
            }
        }
        public (int Start, int End) GetCustomAttributeRange(int parentToken)
        {
            uint key = EcmaCodedIndex.EncodeHasCustomAttribute(parentToken);
            int count = _layout.RowCounts[(int)EcmaTable.CustomAttribute];
            int lo = 1;
            int hi = count + 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >>> 1;
                if (Read(EcmaTable.CustomAttribute, mid, 0) < key)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            int end = lo;
            while (end <= count && Read(EcmaTable.CustomAttribute, end, 0) == key)
                end++;
            return (lo, end);
        }
        public bool IsAttribute(int constructorToken, string @namespace, string name)
            => TryGetAttributeTypeName(constructorToken, out var ns, out var n) &&
               StringComparer.Ordinal.Equals(ns, @namespace) &&
               StringComparer.Ordinal.Equals(n, name);
        private void IndexStrings()
        {
            var heap = _image.AsSpan(_stringsOffset, _stringsSize);
            _strings[0] = string.Empty;
            int start = 1;
            for (int i = 1; i < heap.Length; i++)
            {
                if (heap[i] != 0)
                    continue;
                if (i > start)
                    _strings[start] = Encoding.UTF8.GetString(heap[start..i]);
                start = i + 1;
            }
        }
        private int RvaToOffset(int rva)
        {
            foreach (var section in _sections)
            {
                if (rva >= section.Rva && rva < section.Rva + Math.Max(section.VirtualSize, section.RawSize))
                    return section.RawPointer + (rva - section.Rva);
            }
            throw new BadImageFormatException($"RVA 0x{rva:X} is not inside any section.");
        }
        private uint Read(EcmaTable table, int rid, int column)
        {
            int t = (int)table;
            if ((uint)(rid - 1) >= (uint)_layout.RowCounts[t])
                throw new ArgumentOutOfRangeException(nameof(rid), $"{table} row {rid} does not exist.");
            int offset = _tablesOffset + _layout.TableOffsets[t] + (rid - 1) * _layout.RowSizes[t] + _layout.ColumnOffsets[t][column];
            return _layout.ColumnSizes[t][column] == 2
                ? BinaryPrimitives.ReadUInt16LittleEndian(_image.AsSpan(offset, 2))
                : BinaryPrimitives.ReadUInt32LittleEndian(_image.AsSpan(offset, 4));
        }
        private int ReadToken(EcmaTable table, int rid, int column, EcmaCodedKind kind)
            => EcmaCodedIndex.Decode(kind, Read(table, rid, column));
        private static int ReadInt32(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadInt32LittleEndian(span[offset..]);
        private static int ReadUInt16(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);

        public int GetRowCount(MetadataTableKind table) => _layout.RowCounts[(int)ToEcmaTable(table)];
        private static EcmaTable ToEcmaTable(MetadataTableKind table) => table switch
        {
            MetadataTableKind.AssemblyRef => EcmaTable.AssemblyRef,
            MetadataTableKind.TypeRef => EcmaTable.TypeRef,
            MetadataTableKind.TypeDef => EcmaTable.TypeDef,
            MetadataTableKind.NestedClass => EcmaTable.NestedClass,
            MetadataTableKind.InterfaceImpl => EcmaTable.InterfaceImpl,
            MetadataTableKind.MethodImpl => EcmaTable.MethodImpl,
            MetadataTableKind.Field => EcmaTable.Field,
            MetadataTableKind.MethodDef => EcmaTable.MethodDef,
            MetadataTableKind.Param => EcmaTable.Param,
            MetadataTableKind.MemberRef => EcmaTable.MemberRef,
            MetadataTableKind.TypeSpec => EcmaTable.TypeSpec,
            MetadataTableKind.MethodSpec => EcmaTable.MethodSpec,
            MetadataTableKind.Constant => EcmaTable.Constant,
            MetadataTableKind.Property => EcmaTable.Property,
            MetadataTableKind.CustomAttribute => EcmaTable.CustomAttribute,
            MetadataTableKind.PInvokeMap => EcmaTable.ImplMap,
            MetadataTableKind.ModuleRef => EcmaTable.ModuleRef,
            MetadataTableKind.GenericParam => EcmaTable.GenericParam,
            MetadataTableKind.GenericParamConstraint => EcmaTable.GenericParamConstraint,
            MetadataTableKind.ClassLayout => EcmaTable.ClassLayout,
            MetadataTableKind.FieldRva => EcmaTable.FieldRva,
            MetadataTableKind.StandAloneSig => EcmaTable.StandAloneSig,
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };

        public string GetString(int index)
        {
            if (_strings.TryGetValue(index, out var value))
                return value;
            if ((uint)index >= (uint)_stringsSize)
                throw new ArgumentOutOfRangeException(nameof(index));
            var heap = _image.AsSpan(_stringsOffset + index, _stringsSize - index);
            int end = heap.IndexOf((byte)0);
            return Encoding.UTF8.GetString(end < 0 ? heap : heap[..end]);
        }
        public string GetUserString(int index)
        {
            if ((uint)index >= (uint)_userStringsSize)
                throw new ArgumentOutOfRangeException(nameof(index));
            var heap = _image.AsSpan(_userStringsOffset + index, _userStringsSize - index);
            int length = (int)ReadCompressedUInt(heap, out int headerSize);
            if (length == 0)
                return string.Empty;
            return Encoding.Unicode.GetString(heap.Slice(headerSize, length - 1));
        }
        public ReadOnlySpan<byte> GetBlob(int index)
        {
            if ((uint)index >= (uint)_blobSize)
                throw new ArgumentOutOfRangeException(nameof(index));
            var heap = _image.AsSpan(_blobOffset + index, _blobSize - index);
            int length = (int)ReadCompressedUInt(heap, out int headerSize);
            return heap.Slice(headerSize, length);
        }
        public ReadOnlySpan<byte> GetRvaData(int rva, int length) => _image.AsSpan(RvaToOffset(rva), length);
        private static uint ReadCompressedUInt(ReadOnlySpan<byte> data, out int size)
        {
            byte b0 = data[0];
            if ((b0 & 0x80) == 0)
            {
                size = 1;
                return b0;
            }
            if ((b0 & 0xC0) == 0x80)
            {
                size = 2;
                return (uint)(((b0 & 0x3F) << 8) | data[1]);
            }
            size = 4;
            return (uint)(((b0 & 0x1F) << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);
        }

        public AssemblyRefRow GetAssemblyRef(int rid) => new((int)Read(EcmaTable.AssemblyRef, rid, 6));
        public TypeRefRow GetTypeRef(int rid) => new(
            ReadToken(EcmaTable.TypeRef, rid, 0, EcmaCodedKind.ResolutionScope),
            (int)Read(EcmaTable.TypeRef, rid, 1),
            (int)Read(EcmaTable.TypeRef, rid, 2));
        public TypeDefRow GetTypeDef(int rid) => new(
            (int)Read(EcmaTable.TypeDef, rid, 0),
            (int)Read(EcmaTable.TypeDef, rid, 1),
            (int)Read(EcmaTable.TypeDef, rid, 2),
            (int)Read(EcmaTable.TypeDef, rid, 3),
            (int)Read(EcmaTable.TypeDef, rid, 4),
            (int)Read(EcmaTable.TypeDef, rid, 5));
        public NestedClassRow GetNestedClass(int rid) => new((int)Read(EcmaTable.NestedClass, rid, 0), (int)Read(EcmaTable.NestedClass, rid, 1));
        public InterfaceImplRow GetInterfaceImpl(int rid) => new((int)Read(EcmaTable.InterfaceImpl, rid, 0), (int)Read(EcmaTable.InterfaceImpl, rid, 1));
        public MethodImplRow GetMethodImpl(int rid) => new(
            (int)Read(EcmaTable.MethodImpl, rid, 0),
            ReadToken(EcmaTable.MethodImpl, rid, 1, EcmaCodedKind.MethodDefOrRef),
            ReadToken(EcmaTable.MethodImpl, rid, 2, EcmaCodedKind.MethodDefOrRef));
        public FieldRow GetField(int rid) => new((ushort)Read(EcmaTable.Field, rid, 0), (int)Read(EcmaTable.Field, rid, 1), (int)Read(EcmaTable.Field, rid, 2));
        public MethodDefRow GetMethodDef(int rid) => new(
            (int)Read(EcmaTable.MethodDef, rid, 0),
            (ushort)Read(EcmaTable.MethodDef, rid, 1),
            (ushort)Read(EcmaTable.MethodDef, rid, 2),
            (int)Read(EcmaTable.MethodDef, rid, 3),
            (int)Read(EcmaTable.MethodDef, rid, 4),
            (int)Read(EcmaTable.MethodDef, rid, 5));
        public ParamRow GetParam(int rid) => new((ushort)Read(EcmaTable.Param, rid, 0), (ushort)Read(EcmaTable.Param, rid, 1), (int)Read(EcmaTable.Param, rid, 2));
        public MemberRefRow GetMemberRef(int rid) => new(
            ReadToken(EcmaTable.MemberRef, rid, 0, EcmaCodedKind.MemberRefParent),
            (int)Read(EcmaTable.MemberRef, rid, 1),
            (int)Read(EcmaTable.MemberRef, rid, 2));
        public TypeSpecRow GetTypeSpec(int rid) => new((int)Read(EcmaTable.TypeSpec, rid, 0));
        public MethodSpecRow GetMethodSpec(int rid) => new(
            ReadToken(EcmaTable.MethodSpec, rid, 0, EcmaCodedKind.MethodDefOrRef),
            (int)Read(EcmaTable.MethodSpec, rid, 1));
        public ConstantRow GetConstant(int rid) => new(
            ReadToken(EcmaTable.Constant, rid, 1, EcmaCodedKind.HasConstant),
            (byte)Read(EcmaTable.Constant, rid, 0),
            (int)Read(EcmaTable.Constant, rid, 2));
        public PropertyRow GetProperty(int rid) => new(
            (ushort)Read(EcmaTable.Property, rid, 0),
            (int)Read(EcmaTable.Property, rid, 1),
            (int)Read(EcmaTable.Property, rid, 2),
            _propertyGetters[rid],
            _propertySetters[rid]);
        public CustomAttributeRow GetCustomAttribute(int rid) => new(
            ReadToken(EcmaTable.CustomAttribute, rid, 0, EcmaCodedKind.HasCustomAttribute),
            ReadToken(EcmaTable.CustomAttribute, rid, 1, EcmaCodedKind.CustomAttributeType),
            (int)Read(EcmaTable.CustomAttribute, rid, 2));
        public PInvokeMapRow GetPInvokeMap(int rid) => new(
            (ushort)Read(EcmaTable.ImplMap, rid, 0),
            ReadToken(EcmaTable.ImplMap, rid, 1, EcmaCodedKind.MemberForwarded),
            (int)Read(EcmaTable.ImplMap, rid, 2),
            (int)Read(EcmaTable.ImplMap, rid, 3));
        public ModuleRefRow GetModuleRef(int rid) => new((int)Read(EcmaTable.ModuleRef, rid, 0));
        public GenericParamRow GetGenericParam(int rid) => new(
            (ushort)Read(EcmaTable.GenericParam, rid, 0),
            (ushort)Read(EcmaTable.GenericParam, rid, 1),
            ReadToken(EcmaTable.GenericParam, rid, 2, EcmaCodedKind.TypeOrMethodDef),
            (int)Read(EcmaTable.GenericParam, rid, 3));
        public GenericParamConstraintRow GetGenericParamConstraint(int rid) => new(
            (int)Read(EcmaTable.GenericParamConstraint, rid, 0),
            (int)Read(EcmaTable.GenericParamConstraint, rid, 1));
        public ClassLayoutRow GetClassLayout(int rid) => new(
            (ushort)Read(EcmaTable.ClassLayout, rid, 0),
            (int)Read(EcmaTable.ClassLayout, rid, 1),
            (int)Read(EcmaTable.ClassLayout, rid, 2));
        public FieldRvaRow GetFieldRva(int rid) => new((int)Read(EcmaTable.FieldRva, rid, 0), (int)Read(EcmaTable.FieldRva, rid, 1));
        public StandAloneSigRow GetStandAloneSig(int rid) => new((int)Read(EcmaTable.StandAloneSig, rid, 0));
        /// <summary>Finds the entry point method token</summary>
        public int FindEntryPointMethodDef()
        {
            if (TryFindEntryPointMethodDef(out int methodDefToken))
                return methodDefToken;

            throw new InvalidOperationException("Entry point not found in module metadata.");
        }
        /// <summary>Tries the generated top-level entry point before a compatible static Main</summary>
        public bool TryFindEntryPointMethodDef(out int methodDefToken)
        {
            if (TryFindEntryByName(this, "<Main>$", out methodDefToken))
                return true;

            if (TryFindStaticMain(this, out methodDefToken))
                return true;

            methodDefToken = 0;
            return false;
        }
        private static bool TryFindEntryByName(EcmaMetadata metadata, string name, out int methodDefToken)
        {
            int count = metadata.GetRowCount(MetadataTableKind.MethodDef);

            for (int rid = 1; rid <= count; rid++)
            {
                var row = metadata.GetMethodDef(rid);

                if (!StringComparer.Ordinal.Equals(metadata.GetString(row.Name), name))
                    continue;

                methodDefToken = MetadataToken.Make(MetadataToken.MethodDef, rid);
                return true;
            }

            methodDefToken = 0;
            return false;
        }

        private static bool TryFindStaticMain(EcmaMetadata metadata, out int methodDefToken)
        {
            int count = metadata.GetRowCount(MetadataTableKind.MethodDef);

            for (int rid = 1; rid <= count; rid++)
            {
                var row = metadata.GetMethodDef(rid);

                if (!StringComparer.Ordinal.Equals(metadata.GetString(row.Name), "Main"))
                    continue;

                if (!IsStaticMainSignature(metadata.GetBlob(row.Signature)))
                    continue;

                methodDefToken = MetadataToken.Make(MetadataToken.MethodDef, rid);
                return true;
            }

            methodDefToken = 0;
            return false;
        }

        /// <summary>Checks the supported static entry point signature shapes</summary>
        private static bool IsStaticMainSignature(ReadOnlySpan<byte> sig)
        {
            var r = new SigReader(sig);
            byte cc = r.ReadByte();

            // Entry point signatures cannot carry an instance receiver
            if ((cc & 0x20) != 0)
                return false;

            // Reject generic mains
            if ((cc & 0x10) != 0)
            {
                r.ReadCompressedUInt(); // Consume generic arity
                return false;
            }

            uint paramCount = r.ReadCompressedUInt();

            // Return type must be void
            if ((SigElementType)r.ReadByte() != SigElementType.VOID)
                return false;

            if (paramCount == 0)
                return true;

            if (paramCount != 1)
                return false;

            // arg0: string[]
            if ((SigElementType)r.ReadByte() != SigElementType.SZARRAY)
                return false;
            if ((SigElementType)r.ReadByte() != SigElementType.STRING)
                return false;

            return true;
        }
    }
}
