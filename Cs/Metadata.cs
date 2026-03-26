using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;

namespace Cnidaria.Cs
{
    internal static class MetadataToken
    {
        public const int TypeRef = 0x01000000;
        public const int TypeDef = 0x02000000;
        public const int FieldDef = 0x04000000;
        public const int MethodDef = 0x06000000;
        public const int MemberRef = 0x0A000000;
        public const int UserString = 0x70000000;
        public const int ParamDef = 0x08000000;
        public const int TypeSpec = 0x1B000000;
        public const int MethodSpec = 0x2B000000;
        public const int AssemblyRef = 0x23000000;
        public const int Constant = 0x0B000000;
        public const int CustomAttribute = 0x0C000000;
        public const int PropertyDef = 0x17000000;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Make(int tableToken, int rid) => tableToken | rid;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Rid(int token) => token & 0x00FFFFFF;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Table(int token) => token & unchecked((int)0xFF000000);
    }
    public enum MetadataTableKind
    {
        AssemblyRef,
        TypeRef,
        TypeDef,
        NestedClass,
        InterfaceImpl,
        MethodImpl,
        Field,
        MethodDef,
        Param,
        MemberRef,
        TypeSpec,
        MethodSpec,
        Constant,
        Property,
        CustomAttribute
    }

    public interface IMetadataView
    {
        string ModuleName { get; }
        string DefaultExternalAssemblyName { get; }

        int GetRowCount(MetadataTableKind table);
        // Heaps
        string GetString(int index);
        string GetUserString(int index);
        ReadOnlySpan<byte> GetBlob(int index);
        int GetBlobLength(int index);
        bool TryCopyBlob(int index, Span<byte> destination, out int bytesWritten);
        // Tables (RID is 1 based)
        AssemblyRefRow GetAssemblyRef(int rid);
        TypeRefRow GetTypeRef(int rid);
        TypeDefRow GetTypeDef(int rid);
        NestedClassRow GetNestedClass(int rid);
        InterfaceImplRow GetInterfaceImpl(int rid);
        MethodImplRow GetMethodImpl(int rid);
        FieldRow GetField(int rid);
        MethodDefRow GetMethodDef(int rid);
        ParamRow GetParam(int rid);
        MemberRefRow GetMemberRef(int rid);
        TypeSpecRow GetTypeSpec(int rid);
        MethodSpecRow GetMethodSpec(int rid);
        ConstantRow GetConstant(int rid);
        PropertyRow GetProperty(int rid);
        CustomAttributeRow GetCustomAttribute(int rid);

    }
    internal sealed class FlatMetadataView : IMetadataView
    {
        private readonly ReadOnlyMemory<byte> _data;
        private readonly SectionDesc[] _sections = new SectionDesc[(int)FlatMdSection.CustomAttributeTable + 1];
        public FlatMetadataView(ReadOnlyMemory<byte> data)
        {
            _data = data;
            ParseHeaderAndDirectory(data.Span);
        }

        public string ModuleName => Encoding.UTF8.GetString(GetSectionBytes(FlatMdSection.ModuleNameUtf8));
        public string DefaultExternalAssemblyName => Encoding.UTF8.GetString(GetSectionBytes(FlatMdSection.DefaultAsmNameUtf8));

        public int GetRowCount(MetadataTableKind table) => table switch
        {
            MetadataTableKind.AssemblyRef => Section(FlatMdSection.AssemblyRefTable).Count,
            MetadataTableKind.TypeRef => Section(FlatMdSection.TypeRefTable).Count,
            MetadataTableKind.TypeDef => Section(FlatMdSection.TypeDefTable).Count,
            MetadataTableKind.NestedClass => Section(FlatMdSection.NestedClassTable).Count,
            MetadataTableKind.InterfaceImpl => Section(FlatMdSection.InterfaceImplTable).Count,
            MetadataTableKind.MethodImpl => Section(FlatMdSection.MethodImplTable).Count,
            MetadataTableKind.Field => Section(FlatMdSection.FieldTable).Count,
            MetadataTableKind.MethodDef => Section(FlatMdSection.MethodDefTable).Count,
            MetadataTableKind.Param => Section(FlatMdSection.ParamTable).Count,
            MetadataTableKind.MemberRef => Section(FlatMdSection.MemberRefTable).Count,
            MetadataTableKind.TypeSpec => Section(FlatMdSection.TypeSpecTable).Count,
            MetadataTableKind.MethodSpec => Section(FlatMdSection.MethodSpecTable).Count,
            MetadataTableKind.Constant => Section(FlatMdSection.ConstantTable).Count,
            MetadataTableKind.Property => Section(FlatMdSection.PropertyTable).Count,
            MetadataTableKind.CustomAttribute => Section(FlatMdSection.CustomAttributeTable).Count,
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };

        public string GetString(int index)
        {
            var s = GetHeapItem(FlatMdSection.StringsIndex, FlatMdSection.StringsData, index);
            return s.Length == 0 ? string.Empty : Encoding.UTF8.GetString(s);
        }

        public string GetUserString(int index)
        {
            var s = GetHeapItem(FlatMdSection.UserStringsIndex, FlatMdSection.UserStringsData, index);
            return s.Length == 0 ? string.Empty : Encoding.UTF8.GetString(s);
        }

        public ReadOnlySpan<byte> GetBlob(int index) =>
            GetHeapItem(FlatMdSection.BlobIndex, FlatMdSection.BlobData, index);

        public int GetBlobLength(int index)
        {
            var indexSec = Section(FlatMdSection.BlobIndex);
            if ((uint)index >= (uint)indexSec.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (indexSec.ElemSize == 8)
            {
                int p = indexSec.Offset + (index * 8) + 4;
                return ReadI32(_data.Span, p);
            }

            if (indexSec.ElemSize == 4)
            {
                int p = indexSec.Offset + (index * 4);
                int end = ReadI32(_data.Span, p);
                int start = index == 0 ? 0 : ReadI32(_data.Span, p - 4);
                if (end < start)
                    throw new InvalidOperationException("Corrupted heap index.");
                return end - start;
            }

            throw new InvalidOperationException("Unsupported BlobIndex element size.");
        }

        public bool TryCopyBlob(int index, Span<byte> destination, out int bytesWritten)
        {
            var src = GetBlob(index);
            bytesWritten = src.Length;
            if (destination.Length < src.Length)
                return false;

            src.CopyTo(destination);
            return true;
        }

        public CustomAttributeRow GetCustomAttribute(int rid)
        {
            int p = GetRowOffset(FlatMdSection.CustomAttributeTable, rid, 16);
            int parentToken = ReadI32(_data.Span, p); p += 4;
            int attributeTypeToken = ReadI32(_data.Span, p); p += 4;
            int value = ReadI32(_data.Span, p); p += 4;
            byte target = _data.Span[p];
            return new CustomAttributeRow(parentToken, attributeTypeToken, value, target);
        }
        public InterfaceImplRow GetInterfaceImpl(int rid)
        {
            int p = GetRowOffset(FlatMdSection.InterfaceImplTable, rid, 8);
            int classTypeDefRid = ReadI32(_data.Span, p); p += 4;
            int interfaceEncoded = ReadI32(_data.Span, p);
            return new InterfaceImplRow(classTypeDefRid, interfaceEncoded);
        }
        public MethodImplRow GetMethodImpl(int rid)
        {
            int p = GetRowOffset(FlatMdSection.MethodImplTable, rid, 12);
            int classTypeDefRid = ReadI32(_data.Span, p); p += 4;
            int bodyMethodToken = ReadI32(_data.Span, p); p += 4;
            int declarationMethodToken = ReadI32(_data.Span, p);
            return new MethodImplRow(classTypeDefRid, bodyMethodToken, declarationMethodToken);
        }
        public AssemblyRefRow GetAssemblyRef(int rid)
        {
            int p = GetRowOffset(FlatMdSection.AssemblyRefTable, rid, 4);
            return new AssemblyRefRow(name: ReadI32(_data.Span, p));
        }

        public TypeRefRow GetTypeRef(int rid)
        {
            int p = GetRowOffset(FlatMdSection.TypeRefTable, rid, 12);
            int scope = ReadI32(_data.Span, p); p += 4;
            int name = ReadI32(_data.Span, p); p += 4;
            int ns = ReadI32(_data.Span, p);
            return new TypeRefRow(scope, name, ns);
        }

        public TypeDefRow GetTypeDef(int rid)
        {
            int p = GetRowOffset(FlatMdSection.TypeDefTable, rid, 24);
            int flags = ReadI32(_data.Span, p); p += 4;
            int name = ReadI32(_data.Span, p); p += 4;
            int ns = ReadI32(_data.Span, p); p += 4;
            int extends = ReadI32(_data.Span, p); p += 4;
            int fieldList = ReadI32(_data.Span, p); p += 4;
            int methodList = ReadI32(_data.Span, p);
            return new TypeDefRow(flags, name, ns, extends, fieldList, methodList);
        }

        public NestedClassRow GetNestedClass(int rid)
        {
            int p = GetRowOffset(FlatMdSection.NestedClassTable, rid, 8);
            int nested = ReadI32(_data.Span, p); p += 4;
            int enclosing = ReadI32(_data.Span, p);
            return new NestedClassRow(nested, enclosing);
        }

        public FieldRow GetField(int rid)
        {
            var sec = Section(FlatMdSection.FieldTable);
            int p = GetRowOffset(FlatMdSection.FieldTable, rid, sec.ElemSize);
            ushort flags = ReadU16(_data.Span, p); p += 2;
            int name = ReadI32(_data.Span, p); p += 4;
            int sig = ReadI32(_data.Span, p);
            return new FieldRow(flags, name, sig);
        }

        public MethodDefRow GetMethodDef(int rid)
        {
            int p = GetRowOffset(FlatMdSection.MethodDefTable, rid, 16);
            ushort implFlags = ReadU16(_data.Span, p); p += 2;
            ushort flags = ReadU16(_data.Span, p); p += 2;
            int name = ReadI32(_data.Span, p); p += 4;
            int sig = ReadI32(_data.Span, p); p += 4;
            int paramList = ReadI32(_data.Span, p);
            return new MethodDefRow(implFlags, flags, name, sig, paramList);
        }

        public ParamRow GetParam(int rid)
        {
            int p = GetRowOffset(FlatMdSection.ParamTable, rid, 8);
            ushort flags = ReadU16(_data.Span, p); p += 2;
            ushort seq = ReadU16(_data.Span, p); p += 2;
            int name = ReadI32(_data.Span, p);
            return new ParamRow(flags, seq, name);
        }

        public MemberRefRow GetMemberRef(int rid)
        {
            int p = GetRowOffset(FlatMdSection.MemberRefTable, rid, 12);
            int cls = ReadI32(_data.Span, p); p += 4;
            int name = ReadI32(_data.Span, p); p += 4;
            int sig = ReadI32(_data.Span, p);
            return new MemberRefRow(cls, name, sig);
        }

        public TypeSpecRow GetTypeSpec(int rid)
        {
            int p = GetRowOffset(FlatMdSection.TypeSpecTable, rid, 4);
            return new TypeSpecRow(ReadI32(_data.Span, p));
        }
        public MethodSpecRow GetMethodSpec(int rid)
        {
            int p = GetRowOffset(FlatMdSection.MethodSpecTable, rid, 8);
            int method = ReadI32(_data.Span, p); p += 4;
            int inst = ReadI32(_data.Span, p);
            return new MethodSpecRow(method, inst);
        }
        public ConstantRow GetConstant(int rid)
        {
            var sec = Section(FlatMdSection.ConstantTable);
            int p = GetRowOffset(FlatMdSection.ConstantTable, rid, sec.ElemSize);
            int parent = ReadI32(_data.Span, p); p += 4;
            byte typeCode = _data.Span[p]; p += 1;
            int value = ReadI32(_data.Span, p);
            return new ConstantRow(parent, typeCode, value);
        }

        public PropertyRow GetProperty(int rid)
        {
            var sec = Section(FlatMdSection.PropertyTable);
            int p = GetRowOffset(FlatMdSection.PropertyTable, rid, sec.ElemSize);
            ushort flags = ReadU16(_data.Span, p); p += 2;
            int name = ReadI32(_data.Span, p); p += 4;
            int sig = ReadI32(_data.Span, p); p += 4;
            int getMethod = ReadI32(_data.Span, p); p += 4;
            int setMethod = ReadI32(_data.Span, p);
            return new PropertyRow(flags, name, sig, getMethod, setMethod);
        }

        // Internal parsing helpers

        private void ParseHeaderAndDirectory(ReadOnlySpan<byte> s)
        {
            if (s.Length < 32)
                throw new InvalidOperationException("Flat metadata is too small.");

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(0, 4));
            if (magic != FlatMetadataBuilder.Magic)
                throw new InvalidOperationException("Invalid flat metadata magic.");

            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(4, 2));
            if (version != FlatMetadataBuilder.Version)
                throw new InvalidOperationException($"Unsupported flat metadata version: {version}");

            int headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(8, 4));
            int totalSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(12, 4));
            int dirOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(16, 4));
            int dirEntrySize = (int)BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(20, 4));
            int dirCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(24, 4));

            if (totalSize > s.Length)
                throw new InvalidOperationException("Flat metadata is truncated.");
            if (dirEntrySize != 20)
                throw new InvalidOperationException("Unexpected directory entry size.");
            if (headerSize > s.Length || dirOffset < 0)
                throw new InvalidOperationException("Invalid flat metadata header.");

            int p = dirOffset;
            for (int i = 0; i < dirCount; i++)
            {
                if (p + 20 > s.Length)
                    throw new InvalidOperationException("Flat metadata directory is truncated.");

                var kind = (FlatMdSection)ReadU16(s, p); p += 2;
                ushort elemSize = ReadU16(s, p); p += 2;
                p += 4; // reserved
                int offset = ReadI32(s, p); p += 4;
                int size = ReadI32(s, p); p += 4;
                int count = ReadI32(s, p); p += 4;

                if ((uint)offset > (uint)s.Length || (uint)size > (uint)(s.Length - offset))
                    throw new InvalidOperationException($"Section {kind} is out of range.");

                _sections[(int)kind] = new SectionDesc(offset, size, count, elemSize);
            }
        }

        private ReadOnlySpan<byte> GetHeapItem(FlatMdSection indexSectionKind, FlatMdSection dataSectionKind, int index)
        {
            var indexSec = Section(indexSectionKind);
            var dataSec = Section(dataSectionKind);

            if ((uint)index >= (uint)indexSec.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            int dataOffset;
            int dataLength;

            if (indexSec.ElemSize == 8)
            {
                int p = indexSec.Offset + (index * 8);
                dataOffset = ReadI32(_data.Span, p);
                dataLength = ReadI32(_data.Span, p + 4);
            }
            else if (indexSec.ElemSize == 4)
            {
                int p = indexSec.Offset + (index * 4);
                int end = ReadI32(_data.Span, p);
                int start = index == 0 ? 0 : ReadI32(_data.Span, p - 4);
                if (end < start)
                    throw new InvalidOperationException("Corrupted heap index.");

                dataOffset = start;
                dataLength = end - start;
            }
            else
            {
                throw new InvalidOperationException($"Unsupported heap index entry size: {indexSec.ElemSize}");
            }

            if ((uint)dataOffset > (uint)dataSec.Size || (uint)dataLength > (uint)(dataSec.Size - dataOffset))
                throw new InvalidOperationException("Heap item points outside of heap data.");

            return _data.Span.Slice(dataSec.Offset + dataOffset, dataLength);
        }

        private ReadOnlySpan<byte> GetSectionBytes(FlatMdSection kind)
        {
            var sec = Section(kind);
            return _data.Span.Slice(sec.Offset, sec.Size);
        }

        private int GetRowOffset(FlatMdSection kind, int rid, int expectedRowSize)
        {
            if (rid <= 0)
                throw new ArgumentOutOfRangeException(nameof(rid));

            var sec = Section(kind);
            if (sec.ElemSize != expectedRowSize)
                throw new InvalidOperationException($"Section {kind} element size mismatch.");

            int index = rid - 1;
            if ((uint)index >= (uint)sec.Count)
                throw new ArgumentOutOfRangeException(nameof(rid));

            return sec.Offset + (index * sec.ElemSize);
        }

        private SectionDesc Section(FlatMdSection kind)
        {
            var s = _sections[(int)kind];
            if (!s.IsDefined)
                throw new InvalidOperationException($"Section {kind} is missing.");
            return s;
        }

        private static ushort ReadU16(ReadOnlySpan<byte> s, int offset) =>
            BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(offset, 2));

        private static int ReadI32(ReadOnlySpan<byte> s, int offset) =>
            BinaryPrimitives.ReadInt32LittleEndian(s.Slice(offset, 4));

        private readonly struct SectionDesc
        {
            public readonly int Offset;
            public readonly int Size;
            public readonly int Count;
            public readonly int ElemSize;
            public readonly bool IsDefined;

            public SectionDesc(int offset, int size, int count, int elemSize)
            {
                Offset = offset;
                Size = size;
                Count = count;
                ElemSize = elemSize;
                IsDefined = true;
            }
        }
    }
    internal sealed class MetadataImageView : IMetadataView
    {
        private readonly MetadataImage _md;

        public MetadataImageView(MetadataImage md)
        {
            _md = md ?? throw new ArgumentNullException(nameof(md));
        }

        public string ModuleName => _md.ModuleName;
        public string DefaultExternalAssemblyName => _md.DefaultExternalAssemblyName;

        public int GetRowCount(MetadataTableKind table) => table switch
        {
            MetadataTableKind.AssemblyRef => _md.AssemblyRefs.Count,
            MetadataTableKind.TypeRef => _md.TypeRefs.Count,
            MetadataTableKind.TypeDef => _md.TypeDefs.Count,
            MetadataTableKind.NestedClass => _md.NestedClasses.Count,
            MetadataTableKind.InterfaceImpl => _md.InterfaceImpls.Count,
            MetadataTableKind.MethodImpl => _md.MethodImpls.Count,
            MetadataTableKind.Field => _md.Fields.Count,
            MetadataTableKind.MethodDef => _md.Methods.Count,
            MetadataTableKind.Param => _md.Params.Count,
            MetadataTableKind.MemberRef => _md.MemberRefs.Count,
            MetadataTableKind.TypeSpec => _md.TypeSpecs.Count,
            MetadataTableKind.MethodSpec => _md.MethodSpecs.Count,
            MetadataTableKind.Constant => _md.Constants.Count,
            MetadataTableKind.Property => _md.Properties.Count,
            MetadataTableKind.CustomAttribute => _md.CustomAttributes.Count,
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };

        public string GetString(int index) => _md.Strings.Get(index);
        public string GetUserString(int index)
        {
            var items = _md.UserStrings.Items;
            if ((uint)index >= (uint)items.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return items[index];
        }

        public ReadOnlySpan<byte> GetBlob(int index) => _md.Blob.Get(index);

        public int GetBlobLength(int index) => _md.Blob.Get(index).Length;

        public bool TryCopyBlob(int index, Span<byte> destination, out int bytesWritten)
        {
            var src = _md.Blob.Get(index);
            bytesWritten = src.Length;
            if (destination.Length < src.Length)
                return false;

            src.CopyTo(destination);
            return true;
        }

        // RID is 1 based
        public AssemblyRefRow GetAssemblyRef(int rid) => _md.AssemblyRefs[rid - 1];
        public TypeRefRow GetTypeRef(int rid) => _md.TypeRefs[rid - 1];
        public TypeDefRow GetTypeDef(int rid) => _md.TypeDefs[rid - 1];
        public NestedClassRow GetNestedClass(int rid) => _md.NestedClasses[rid - 1];
        public InterfaceImplRow GetInterfaceImpl(int rid) => _md.InterfaceImpls[rid - 1];
        public MethodImplRow GetMethodImpl(int rid) => _md.MethodImpls[rid - 1];
        public FieldRow GetField(int rid) => _md.Fields[rid - 1];
        public MethodDefRow GetMethodDef(int rid) => _md.Methods[rid - 1];
        public ParamRow GetParam(int rid) => _md.Params[rid - 1];
        public MemberRefRow GetMemberRef(int rid) => _md.MemberRefs[rid - 1];
        public TypeSpecRow GetTypeSpec(int rid) => _md.TypeSpecs[rid - 1]; 
        public MethodSpecRow GetMethodSpec(int rid) => _md.MethodSpecs[rid - 1];
        public ConstantRow GetConstant(int rid) => _md.Constants[rid - 1];
        public PropertyRow GetProperty(int rid) => _md.Properties[rid - 1];
        public CustomAttributeRow GetCustomAttribute(int rid) => _md.CustomAttributes[rid - 1];
    }
    internal enum FlatMdSection : ushort
    {
        ModuleNameUtf8 = 1,
        DefaultAsmNameUtf8 = 2,

        StringsIndex = 10,
        StringsData = 11,

        UserStringsIndex = 12,
        UserStringsData = 13,

        BlobIndex = 14,
        BlobData = 15,

        AssemblyRefTable = 100,
        TypeRefTable = 101,
        TypeDefTable = 102,
        NestedClassTable = 103,
        InterfaceImplTable = 104, 
        MethodImplTable = 105,
        FieldTable = 106,
        MethodDefTable = 107,
        ParamTable = 108,
        MemberRefTable = 109,
        TypeSpecTable = 110,
        ConstantTable = 111,
        PropertyTable = 112,
        MethodSpecTable = 113,
        CustomAttributeTable = 114,
    }
    internal sealed class AttrBlobWriter
    {
        private readonly List<byte> _buffer = new();

        public void WriteByte(byte value) => _buffer.Add(value);
        public void WriteSByte(sbyte value) => _buffer.Add(unchecked((byte)value));

        public void WriteInt16(short value)
        {
            Span<byte> tmp = stackalloc byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(tmp, value);
            WriteBytes(tmp);
        }

        public void WriteUInt16(ushort value)
        {
            Span<byte> tmp = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(tmp, value);
            WriteBytes(tmp);
        }

        public void WriteInt32(int value)
        {
            Span<byte> tmp = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(tmp, value);
            WriteBytes(tmp);
        }

        public void WriteUInt32(uint value)
        {
            Span<byte> tmp = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(tmp, value);
            WriteBytes(tmp);
        }

        public void WriteInt64(long value)
        {
            Span<byte> tmp = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(tmp, value);
            WriteBytes(tmp);
        }

        public void WriteUInt64(ulong value)
        {
            Span<byte> tmp = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(tmp, value);
            WriteBytes(tmp);
        }

        public void WriteSingle(float value) => WriteUInt32(BitConverter.SingleToUInt32Bits(value));
        public void WriteDouble(double value) => WriteUInt64(BitConverter.DoubleToUInt64Bits(value));

        public void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
                _buffer.Add(bytes[i]);
        }

        public byte[] ToArray() => _buffer.ToArray();
    }
    internal ref struct AttrBlobReader
    {
        private ReadOnlySpan<byte> _data;
        private int _offset;

        public AttrBlobReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _offset = 0;
        }

        public byte ReadByte()
        {
            if ((uint)_offset >= (uint)_data.Length)
                throw new InvalidOperationException("Attribute blob is truncated.");
            return _data[_offset++];
        }

        public sbyte ReadSByte() => unchecked((sbyte)ReadByte());

        public short ReadInt16()
        {
            Ensure(2);
            short v = BinaryPrimitives.ReadInt16LittleEndian(_data.Slice(_offset, 2));
            _offset += 2;
            return v;
        }

        public ushort ReadUInt16()
        {
            Ensure(2);
            ushort v = BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(_offset, 2));
            _offset += 2;
            return v;
        }

        public int ReadInt32()
        {
            Ensure(4);
            int v = BinaryPrimitives.ReadInt32LittleEndian(_data.Slice(_offset, 4));
            _offset += 4;
            return v;
        }

        public uint ReadUInt32()
        {
            Ensure(4);
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_offset, 4));
            _offset += 4;
            return v;
        }

        public long ReadInt64()
        {
            Ensure(8);
            long v = BinaryPrimitives.ReadInt64LittleEndian(_data.Slice(_offset, 8));
            _offset += 8;
            return v;
        }

        public ulong ReadUInt64()
        {
            Ensure(8);
            ulong v = BinaryPrimitives.ReadUInt64LittleEndian(_data.Slice(_offset, 8));
            _offset += 8;
            return v;
        }

        public float ReadSingle() => BitConverter.UInt32BitsToSingle(ReadUInt32());
        public double ReadDouble() => BitConverter.UInt64BitsToDouble(ReadUInt64());

        private void Ensure(int count)
        {
            if ((uint)count > (uint)(_data.Length - _offset))
                throw new InvalidOperationException("Attribute blob is truncated.");
        }
    }
    internal static class FlatMetadataBuilder
    {
        public const uint Magic = 0x444D4E43; // "CNMD" little endian
        /// <summary>Backwards compatibility is NOT preserved</summary>
        public const ushort Version = 1;
        public const int Alignment = 4;

        private const int HeaderSizeFixed = 32;
        private const int DirEntrySize = 20;

        public static int GetRequiredSize(MetadataImage md)
        {
            if (md is null) throw new ArgumentNullException(nameof(md));
            var plan = BuildPlan(md);
            return plan.TotalSize;
        }
        public static bool TryWrite(MetadataImage md, Span<byte> destination, out int bytesWritten)
        {
            if (md is null) throw new ArgumentNullException(nameof(md));

            var plan = BuildPlan(md);
            if (destination.Length < plan.TotalSize)
            {
                bytesWritten = 0;
                return false;
            }

            WriteCore(md, plan, destination);
            bytesWritten = plan.TotalSize;
            return true;
        }
        public static int Write(MetadataImage md, Span<byte> destination)
        {
            if (!TryWrite(md, destination, out var written))
                throw new ArgumentException("Destination span is too small.", nameof(destination));

            return written;
        }

        public static byte[] Build(MetadataImage md)
        {
            if (md is null) throw new ArgumentNullException(nameof(md));

            var plan = BuildPlan(md);
            var bytes = GC.AllocateUninitializedArray<byte>(plan.TotalSize);
            WriteCore(md, plan, bytes);
            return bytes;
        }

        private static void WriteCore(MetadataImage md, LayoutPlan plan, Span<byte> dest)
        {
            dest.Slice(0, plan.TotalSize).Clear();

            WriteHeaderAndDirectory(plan, dest);
            // Raw manifest strings
            WriteUtf8Raw(md.ModuleName, plan.Get(FlatMdSection.ModuleNameUtf8), dest);
            WriteUtf8Raw(md.DefaultExternalAssemblyName, plan.Get(FlatMdSection.DefaultAsmNameUtf8), dest);

            // Heaps
            WriteStringHeap(md.Strings.Items, plan.Get(FlatMdSection.StringsIndex), plan.Get(FlatMdSection.StringsData), dest);
            WriteStringHeap(md.UserStrings.Items, plan.Get(FlatMdSection.UserStringsIndex), plan.Get(FlatMdSection.UserStringsData), dest);
            WriteBlobHeap(md.Blob.Items, plan.Get(FlatMdSection.BlobIndex), plan.Get(FlatMdSection.BlobData), dest);

            // Tables
            WriteAssemblyRefs(md.AssemblyRefs, plan.Get(FlatMdSection.AssemblyRefTable), dest);
            WriteTypeRefs(md.TypeRefs, plan.Get(FlatMdSection.TypeRefTable), dest);
            WriteTypeDefs(md.TypeDefs, plan.Get(FlatMdSection.TypeDefTable), dest);
            WriteNestedClasses(md.NestedClasses, plan.Get(FlatMdSection.NestedClassTable), dest);
            WriteInterfaceImpls(md.InterfaceImpls, plan.Get(FlatMdSection.InterfaceImplTable), dest);
            WriteMethodImpls(md.MethodImpls, plan.Get(FlatMdSection.MethodImplTable), dest);

            WriteFields(md.Fields, plan.Get(FlatMdSection.FieldTable), dest);
            WriteMethods(md.Methods, plan.Get(FlatMdSection.MethodDefTable), dest);
            WriteParams(md.Params, plan.Get(FlatMdSection.ParamTable), dest);
            WriteMemberRefs(md.MemberRefs, plan.Get(FlatMdSection.MemberRefTable), dest);
            WriteTypeSpecs(md.TypeSpecs, plan.Get(FlatMdSection.TypeSpecTable), dest);
            WriteConstants(md.Constants, plan.Get(FlatMdSection.ConstantTable), dest);
            WriteProperties(md.Properties, plan.Get(FlatMdSection.PropertyTable), dest);
            WriteMethodSpecs(md.MethodSpecs, plan.Get(FlatMdSection.MethodSpecTable), dest);
            WriteCustomAttributes(md.CustomAttributes, plan.Get(FlatMdSection.CustomAttributeTable), dest);
        }
        private static LayoutPlan BuildPlan(MetadataImage md)
        {
            var strings = md.Strings.Items;
            var userStrings = md.UserStrings.Items;
            var blobs = md.Blob.Items;

            int stringsDataBytes = SumUtf8Bytes(strings);
            int userStringsDataBytes = SumUtf8Bytes(userStrings);
            int blobDataBytes = SumBlobBytes(blobs);

            int sectionCount = Enum.GetValues<FlatMdSection>().Length;
            int headerBytes = Align(HeaderSizeFixed + checked(sectionCount * DirEntrySize), Alignment);

            var sections = new SectionDesc[sectionCount];
            int cursor = headerBytes;
            int i = 0;

            // Section order is fixed and versioned.
            Add(ref i, FlatMdSection.ModuleNameUtf8, elemSize: 1, count: 1, size: Utf8ByteCount(md.ModuleName), ref cursor, sections);
            Add(ref i, FlatMdSection.DefaultAsmNameUtf8, elemSize: 1, count: 1, size: Utf8ByteCount(md.DefaultExternalAssemblyName), ref cursor, sections);

            Add(ref i, FlatMdSection.StringsIndex, elemSize: 4, count: strings.Count, size: checked(strings.Count * 4), ref cursor, sections);
            Add(ref i, FlatMdSection.StringsData, elemSize: 1, count: 1, size: stringsDataBytes, ref cursor, sections);

            Add(ref i, FlatMdSection.UserStringsIndex, elemSize: 4, count: userStrings.Count, size: checked(userStrings.Count * 4), ref cursor, sections);
            Add(ref i, FlatMdSection.UserStringsData, elemSize: 1, count: 1, size: userStringsDataBytes, ref cursor, sections);
            Add(ref i, FlatMdSection.BlobIndex, elemSize: 4, count: blobs.Count, size: checked(blobs.Count * 4), ref cursor, sections);
            Add(ref i, FlatMdSection.BlobData, elemSize: 1, count: 1, size: blobDataBytes, ref cursor, sections);

            Add(ref i, FlatMdSection.AssemblyRefTable, elemSize: 4, count: md.AssemblyRefs.Count, size: checked(md.AssemblyRefs.Count * 4), ref cursor, sections);
            Add(ref i, FlatMdSection.TypeRefTable, elemSize: 12, count: md.TypeRefs.Count, size: checked(md.TypeRefs.Count * 12), ref cursor, sections);
            Add(ref i, FlatMdSection.TypeDefTable, elemSize: 24, count: md.TypeDefs.Count, size: checked(md.TypeDefs.Count * 24), ref cursor, sections);
            Add(ref i, FlatMdSection.NestedClassTable, elemSize: 8, count: md.NestedClasses.Count, size: checked(md.NestedClasses.Count * 8), ref cursor, sections);
            Add(ref i, FlatMdSection.InterfaceImplTable, elemSize: 8, count: md.InterfaceImpls.Count, size: checked(md.InterfaceImpls.Count * 8), ref cursor, sections);
            Add(ref i, FlatMdSection.MethodImplTable, elemSize: 12, count: md.MethodImpls.Count, size: checked(md.MethodImpls.Count * 12), ref cursor, sections);

            Add(ref i, FlatMdSection.FieldTable, elemSize: 10, count: md.Fields.Count, size: checked(md.Fields.Count * 10), ref cursor, sections);
            Add(ref i, FlatMdSection.MethodDefTable, elemSize: 16, count: md.Methods.Count, size: checked(md.Methods.Count * 16), ref cursor, sections);
            Add(ref i, FlatMdSection.ParamTable, elemSize: 8, count: md.Params.Count, size: checked(md.Params.Count * 8), ref cursor, sections);
            Add(ref i, FlatMdSection.MemberRefTable, elemSize: 12, count: md.MemberRefs.Count, size: checked(md.MemberRefs.Count * 12), ref cursor, sections);
            Add(ref i, FlatMdSection.TypeSpecTable, elemSize: 4, count: md.TypeSpecs.Count, size: checked(md.TypeSpecs.Count * 4), ref cursor, sections);
            Add(ref i, FlatMdSection.ConstantTable, elemSize: 9, count: md.Constants.Count, size: checked(md.Constants.Count * 9), ref cursor, sections);
            Add(ref i, FlatMdSection.PropertyTable, elemSize: 18, count: md.Properties.Count, size: checked(md.Properties.Count * 18), ref cursor, sections);
            Add(ref i, FlatMdSection.MethodSpecTable, elemSize: 8, count: md.MethodSpecs.Count, size: checked(md.MethodSpecs.Count * 8), ref cursor, sections);

            Add(ref i, FlatMdSection.CustomAttributeTable, elemSize: 16, count: md.CustomAttributes.Count, 
                size: checked(md.CustomAttributes.Count * 16), ref cursor, sections);

            return new LayoutPlan(
                headerSize: headerBytes,
                totalSize: Align(cursor, Alignment),
                sections: sections);
        }
        private static void Add(
            ref int index,
            FlatMdSection kind,
            ushort elemSize,
            int count,
            int size,
            ref int cursor,
            SectionDesc[] sections)
        {
            if (index >= sections.Length)
                throw new InvalidOperationException("Section array overflow.");

            cursor = Align(cursor, Alignment);

            sections[index++] = new SectionDesc(
                kind: kind,
                elemSize: elemSize,
                offset: cursor,
                size: size,
                count: count);

            cursor = checked(cursor + size);
        }
        private static void WriteHeaderAndDirectory(LayoutPlan plan, Span<byte> dest)
        {
            // Header (32 bytes)
            int p = 0;
            WriteU32(dest, ref p, Magic);
            WriteU16(dest, ref p, Version);
            WriteU16(dest, ref p, 0); // reserved
            WriteU32(dest, ref p, (uint)plan.HeaderSize);
            WriteU32(dest, ref p, (uint)plan.TotalSize);
            WriteU32(dest, ref p, HeaderSizeFixed); // directory offset
            WriteU32(dest, ref p, DirEntrySize);
            WriteU32(dest, ref p, (uint)plan.Sections.Length);
            WriteU32(dest, ref p, (uint)Alignment);

            // Directory entries
            p = HeaderSizeFixed;
            for (int i = 0; i < plan.Sections.Length; i++)
            {
                var s = plan.Sections[i];
                WriteU16(dest, ref p, (ushort)s.Kind);
                WriteU16(dest, ref p, s.ElemSize);
                WriteU32(dest, ref p, 0); // reserved
                WriteU32(dest, ref p, (uint)s.Offset);
                WriteU32(dest, ref p, (uint)s.Size);
                WriteU32(dest, ref p, (uint)s.Count);
            }
        }
        private static void WriteUtf8Raw(string value, SectionDesc section, Span<byte> dest)
        {
            if (section.Size == 0)
                return;

            int written = Encoding.UTF8.GetBytes(value.AsSpan(), dest.Slice(section.Offset, section.Size));
            if (written != section.Size)
                throw new InvalidOperationException("UTF-8 size mismatch.");
        }
        private static void WriteStringHeap(
            IReadOnlyList<string> items,
            SectionDesc indexSection,
            SectionDesc dataSection,
            Span<byte> dest)
        {
            if (items.Count != indexSection.Count)
                throw new InvalidOperationException("String heap count mismatch.");

            int dataCursor = 0;
            int ip = indexSection.Offset;

            for (int i = 0; i < items.Count; i++)
            {
                string s = items[i];
                if (!string.IsNullOrEmpty(s))
                {
                    int len = Utf8ByteCount(s);
                    int written = Encoding.UTF8.GetBytes(s.AsSpan(), dest.Slice(dataSection.Offset + dataCursor, len));
                    if (written != len)
                        throw new InvalidOperationException("UTF-8 write mismatch.");

                    dataCursor += len;
                }

                WriteU32(dest, ref ip, (uint)dataCursor);
            }

            if (dataCursor != dataSection.Size)
                throw new InvalidOperationException("String heap size mismatch.");
        }

        private static void WriteBlobHeap(
            IReadOnlyList<byte[]> items,
            SectionDesc indexSection,
            SectionDesc dataSection,
            Span<byte> dest)
        {
            if (items.Count != indexSection.Count)
                throw new InvalidOperationException("Blob heap count mismatch.");

            int dataCursor = 0;
            int ip = indexSection.Offset;

            for (int i = 0; i < items.Count; i++)
            {
                var blob = items[i] ?? Array.Empty<byte>();

                if (blob.Length != 0)
                {
                    blob.AsSpan().CopyTo(dest.Slice(dataSection.Offset + dataCursor, blob.Length));
                    dataCursor += blob.Length;
                }

                WriteU32(dest, ref ip, (uint)dataCursor);
            }

            if (dataCursor != dataSection.Size)
                throw new InvalidOperationException("Blob heap size mismatch.");
        }

        private static void WriteCustomAttributes(List<CustomAttributeRow> rows, SectionDesc s, Span<byte> dest)
        {
            int p = s.Offset;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                WriteI32(dest, ref p, r.ParentToken);
                WriteI32(dest, ref p, r.AttributeTypeToken);
                WriteI32(dest, ref p, r.Value);
                WriteU8(dest, ref p, r.Target);
                WriteU8(dest, ref p, 0);
                WriteU8(dest, ref p, 0);
                WriteU8(dest, ref p, 0);
            }
        }
        private static void WriteInterfaceImpls(List<InterfaceImplRow> rows, SectionDesc s, Span<byte> dest)
        {
            int p = s.Offset;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                WriteI32(dest, ref p, r.ClassTypeDefRid);
                WriteI32(dest, ref p, r.InterfaceEncoded);
            }
        }
        private static void WriteMethodImpls(List<MethodImplRow> rows, SectionDesc s, Span<byte> dest)
        {
            int p = s.Offset;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                WriteI32(dest, ref p, r.ClassTypeDefRid);
                WriteI32(dest, ref p, r.BodyMethodToken);
                WriteI32(dest, ref p, r.DeclarationMethodToken);
            }
        }
        private static void WriteAssemblyRefs(List<AssemblyRefRow> rows, SectionDesc s, Span<byte> dest)
        {
            int p = s.Offset;
            for (int i = 0; i < rows.Count; i++)
            {
                WriteI32(dest, ref p, rows[i].Name);
            }
        }

        private static void WriteTypeRefs(List<TypeRefRow> rows, SectionDesc s, Span<byte> dest)
        {
            int p = s.Offset;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                WriteI32(dest, ref p, r.ResolutionScopeToken);
                WriteI32(dest, ref p, r.Name);
                WriteI32(dest, ref p, r.Namespace);
            }
        }

        private static void WriteTypeDefs(List<TypeDefRow> rows, SectionDesc s, Span<byte> dest)
        {
            int p = s.Offset;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                WriteI32(dest, ref p, r.Flags);
                WriteI32(dest, ref p, r.Name);
                WriteI32(dest, ref p, r.Namespace);
                WriteI32(dest, ref p, r.ExtendsEncoded);
                WriteI32(dest, ref p, r.FieldList);
                WriteI32(dest, ref p, r.MethodList);
            }
        }

        private static void WriteNestedClasses(List<NestedClassRow> rows, SectionDesc s, Span<byte> dest)
        {
            int p = s.Offset;
            for (int i = 0; i < rows.Count; i++)
            {
                WriteI32(dest, ref p, rows[i].NestedTypeRid);
                WriteI32(dest, ref p, rows[i].EnclosingTypeRid);
            }
        }

        private static void WriteFields(List<FieldRow> rows, SectionDesc s, Span<byte> dest)
        {
            int p = s.Offset;
            for (int i = 0; i < rows.Count; i++)
            {
                WriteU16(dest, ref p, rows[i].Flags);
                WriteI32(dest, ref p, rows[i].Name);
                WriteI32(dest, ref p, rows[i].Signature);
            }
        }
        private static void WriteMethods(List<MethodDefRow> rows, SectionDesc s, Span<byte> dest)
        {
            int p = s.Offset;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                WriteU16(dest, ref p, r.ImplFlags);
                WriteU16(dest, ref p, r.Flags);
                WriteI32(dest, ref p, r.Name);
                WriteI32(dest, ref p, r.Signature);
                WriteI32(dest, ref p, r.ParamList);
            }
        }

        private static void WriteParams(List<ParamRow> rows, SectionDesc s, Span<byte> dest)
        {
            int p = s.Offset;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                WriteU16(dest, ref p, r.Flags);
                WriteU16(dest, ref p, r.Sequence);
                WriteI32(dest, ref p, r.Name);
            }
        }

        private static void WriteMemberRefs(List<MemberRefRow> rows, SectionDesc s, Span<byte> dest)
        {
            int p = s.Offset;
            for (int i = 0; i < rows.Count; i++)
            {
                WriteI32(dest, ref p, rows[i].ClassToken);
                WriteI32(dest, ref p, rows[i].Name);
                WriteI32(dest, ref p, rows[i].Signature);
            }
        }

        private static void WriteTypeSpecs(List<TypeSpecRow> rows, SectionDesc s, Span<byte> dest)
        {
            int p = s.Offset;
            for (int i = 0; i < rows.Count; i++)
            {
                WriteI32(dest, ref p, rows[i].Signature);
            }
        }

        private static void WriteConstants(List<ConstantRow> rows, SectionDesc s, Span<byte> dest)
        {
            int p = s.Offset;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                WriteI32(dest, ref p, r.ParentToken);
                WriteU8(dest, ref p, r.TypeCode);
                WriteI32(dest, ref p, r.Value);
            }
        }
        private static void WriteProperties(List<PropertyRow> rows, SectionDesc s, Span<byte> dest)
        {
            int p = s.Offset;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                WriteU16(dest, ref p, r.Flags);
                WriteI32(dest, ref p, r.Name);
                WriteI32(dest, ref p, r.Signature);
                WriteI32(dest, ref p, r.GetMethod);
                WriteI32(dest, ref p, r.SetMethod);
            }
        }
        private static void WriteMethodSpecs(List<MethodSpecRow> rows, SectionDesc s, Span<byte> dest)
        {
            int p = s.Offset;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                WriteI32(dest, ref p, r.Method);
                WriteI32(dest, ref p, r.Instantiation);
            }
        }
        private static int SumUtf8Bytes(IReadOnlyList<string> items)
        {
            int total = 0;
            for (int i = 0; i < items.Count; i++)
            {
                total = checked(total + Utf8ByteCount(items[i]));
            }
            return total;
        }
        private static int SumBlobBytes(IReadOnlyList<byte[]> items)
        {
            int total = 0;
            for (int i = 0; i < items.Count; i++)
            {
                var b = items[i];
                total = checked(total + (b?.Length ?? 0));
            }
            return total;
        }

        private static int Utf8ByteCount(string s)
        {
            if (string.IsNullOrEmpty(s))
                return 0;
            return Encoding.UTF8.GetByteCount(s);
        }

        private static int Align(int value, int align)
        {
            int mask = align - 1;
            return checked((value + mask) & ~mask);
        }

        private static void WriteU8(Span<byte> dest, ref int p, byte value)
        {
            dest[p++] = value;
        }

        private static void WriteU16(Span<byte> dest, ref int p, ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(p, 2), value);
            p += 2;
        }

        private static void WriteU32(Span<byte> dest, ref int p, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(p, 4), value);
            p += 4;
        }

        private static void WriteI32(Span<byte> dest, ref int p, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(dest.Slice(p, 4), value);
            p += 4;
        }


        private readonly struct SectionDesc
        {
            public readonly FlatMdSection Kind;
            public readonly ushort ElemSize;
            public readonly int Offset;
            public readonly int Size;
            public readonly int Count;

            public SectionDesc(FlatMdSection kind, ushort elemSize, int offset, int size, int count)
            {
                Kind = kind;
                ElemSize = elemSize;
                Offset = offset;
                Size = size;
                Count = count;
            }

        }
        private sealed class LayoutPlan
        {
            public int HeaderSize { get; }
            public int TotalSize { get; }
            public SectionDesc[] Sections { get; }

            public LayoutPlan(int headerSize, int totalSize, SectionDesc[] sections)
            {
                HeaderSize = headerSize;
                TotalSize = totalSize;
                Sections = sections;
            }

            public SectionDesc Get(FlatMdSection kind)
            {
                for (int i = 0; i < Sections.Length; i++)
                {
                    if (Sections[i].Kind == kind)
                        return Sections[i];
                }
                throw new InvalidOperationException($"Section not found: {kind}");
            }
        }
    }
    public sealed class MetadataImage
    {
        public string ModuleName { get; }
        public string DefaultExternalAssemblyName { get; }

        public StringsHeap Strings { get; } = new();
        public BlobHeap Blob { get; } = new();
        public UserStringsHeap UserStrings { get; } = new();

        public List<AssemblyRefRow> AssemblyRefs { get; } = new();
        public List<TypeRefRow> TypeRefs { get; } = new();
        public List<TypeDefRow> TypeDefs { get; } = new();
        public List<NestedClassRow> NestedClasses { get; } = new();
        public List<InterfaceImplRow> InterfaceImpls { get; } = new();
        public List<MethodImplRow> MethodImpls { get; } = new();
        public List<FieldRow> Fields { get; } = new();
        public List<MethodDefRow> Methods { get; } = new();
        public List<ParamRow> Params { get; } = new();
        public List<MemberRefRow> MemberRefs { get; } = new();
        public List<TypeSpecRow> TypeSpecs { get; } = new();
        public List<MethodSpecRow> MethodSpecs { get; } = new();
        public List<ConstantRow> Constants { get; } = new();
        public List<PropertyRow> Properties { get; } = new();
        public List<CustomAttributeRow> CustomAttributes { get; } = new();
        public MetadataImage(string moduleName, string defaultExternalAssemblyName)
        {
            ModuleName = moduleName ?? "";
            DefaultExternalAssemblyName = string.IsNullOrWhiteSpace(defaultExternalAssemblyName)
                ? "corelib"
                : defaultExternalAssemblyName;
        }
    }
    public sealed class StringsHeap
    {
        private readonly Dictionary<string, int> _map = new(StringComparer.Ordinal);
        private readonly List<string> _items = new() { "" }; // index 0 => empty
        internal IReadOnlyList<string> Items => _items;
        public int Add(string? s)
        {
            s ??= "";
            if (s.Length == 0) return 0;
            if (_map.TryGetValue(s, out var idx)) return idx;
            idx = _items.Count;
            _items.Add(s);
            _map.Add(s, idx);
            return idx;
        }
        public string Get(int index)
        {
            if ((uint)index >= (uint)_items.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _items[index];
        }
    }
    public sealed class UserStringsHeap
    {
        private readonly Dictionary<string, int> _map = new(StringComparer.Ordinal);
        private int _nextRid = 1;
        private readonly List<string> _items = new() { "" };
        internal IReadOnlyList<string> Items => _items;
        public int GetToken(string value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            if (_map.TryGetValue(value, out var rid))
                return MetadataToken.Make(MetadataToken.UserString, rid);

            rid = _nextRid++;
            _map.Add(value, rid);
            _items.Add(value);
            return MetadataToken.Make(MetadataToken.UserString, rid);
        }
    }
    public sealed class BlobHeap
    {
        private readonly Dictionary<string, int> _map = new(StringComparer.Ordinal);
        private readonly List<byte[]> _items = new() { Array.Empty<byte>() }; // index 0 unused
        internal IReadOnlyList<byte[]> Items => _items;
        public int Add(ReadOnlySpan<byte> blob)
        {
            var key = Convert.ToBase64String(blob.ToArray());
            if (_map.TryGetValue(key, out var idx)) return idx;
            idx = _items.Count;
            _items.Add(blob.ToArray());
            _map.Add(key, idx);
            return idx;
        }
        public ReadOnlySpan<byte> Get(int index)
        {
            if ((uint)index >= (uint)_items.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _items[index];
        }
    }
    public struct AssemblyRefRow
    {
        public int Name; // #Strings index
        public AssemblyRefRow(int name) => Name = name;
    }
    public struct TypeRefRow
    {
        public int ResolutionScopeToken;
        public int Name;      // #Strings
        public int Namespace; // #Strings

        public TypeRefRow(int scopeToken, int name, int @namespace)
        {
            ResolutionScopeToken = scopeToken;
            Name = name;
            Namespace = @namespace;
        }
    }
    public struct TypeDefRow
    {
        public int Flags;
        public int Name;      // #Strings
        public int Namespace; // #Strings (empty for nested)
        public int ExtendsEncoded; // TypeDefOrRefEncoded (0 if none)

        public int FieldList;  // first field RID, or next RID if none
        public int MethodList; // first method RID, or next RID if none

        public TypeDefRow(int flags, int name, int @namespace, int extendsEncoded, int fieldList, int methodList)
        {
            Flags = flags;
            Name = name;
            Namespace = @namespace;
            ExtendsEncoded = extendsEncoded;
            FieldList = fieldList;
            MethodList = methodList;
        }
    }
    public struct NestedClassRow
    {
        public int NestedTypeRid;
        public int EnclosingTypeRid;
        public NestedClassRow(int nestedRid, int enclosingRid)
        {
            NestedTypeRid = nestedRid;
            EnclosingTypeRid = enclosingRid;
        }
    }
    public struct InterfaceImplRow
    {
        public int ClassTypeDefRid;
        public int InterfaceEncoded; // TypeDefOrRef coded index

        public InterfaceImplRow(int classTypeDefRid, int interfaceEncoded)
        {
            ClassTypeDefRid = classTypeDefRid;
            InterfaceEncoded = interfaceEncoded;
        }
    }
    public struct MethodImplRow
    {
        public int ClassTypeDefRid;
        public int BodyMethodToken;         // MethodDef
        public int DeclarationMethodToken;  // MethodDefOrMemberRef

        public MethodImplRow(int classTypeDefRid, int bodyMethodToken, int declarationMethodToken)
        {
            ClassTypeDefRid = classTypeDefRid;
            BodyMethodToken = bodyMethodToken;
            DeclarationMethodToken = declarationMethodToken;
        }
    }
    public struct FieldRow
    {
        public ushort Flags;
        public int Name;       // #Strings
        public int Signature;  // #Blob
        public FieldRow(ushort flags, int name, int signature)
        {
            Flags = flags;
            Name = name;
            Signature = signature;
        }
    }
    public struct MethodDefRow
    {
        public ushort ImplFlags;
        public ushort Flags;
        public int Name;      // #Strings
        public int Signature; // #Blob
        public int ParamList; // first param RID, or next RID if none

        public MethodDefRow(ushort implFlags, ushort flags, int name, int signature, int paramList)
        {
            ImplFlags = implFlags;
            Flags = flags;
            Name = name;
            Signature = signature;
            ParamList = paramList;
        }
    }

    public struct ParamRow
    {
        public ushort Flags;
        public ushort Sequence; // 1..N
        public int Name;        // #Strings
        public ParamRow(ushort flags, ushort sequence, int name)
        {
            Flags = flags;
            Sequence = sequence;
            Name = name;
        }
    }
    public struct ConstantRow
    {
        public int ParentToken;
        public byte TypeCode;
        public int Value; // #Blob index
        public ConstantRow(int parentToken, byte typeCode, int valueBlob)
        {
            ParentToken = parentToken;
            TypeCode = typeCode;
            Value = valueBlob;
        }
    }
    public struct PropertyRow
    {
        public ushort Flags;
        public int Name;       // #Strings
        public int Signature;  // #Blob
        public int GetMethod;  // MethodDef
        public int SetMethod;  // MethodDef
        public PropertyRow(ushort flags, int name, int sig, int getMethod, int setMethod)
        {
            Flags = flags;
            Name = name;
            Signature = sig;
            GetMethod = getMethod;
            SetMethod = setMethod;
        }
    }
    public struct MemberRefRow
    {
        public int ClassToken;
        public int Name;      // #Strings
        public int Signature; // #Blob

        public MemberRefRow(int classToken, int name, int signature)
        {
            ClassToken = classToken;
            Name = name;
            Signature = signature;
        }
    }
    public struct TypeSpecRow
    {
        public int Signature; // #Blob
        public TypeSpecRow(int signature) => Signature = signature;
    }
    public struct MethodSpecRow
    {
        public int Method;        // MethodDef or MemberRef token
        public int Instantiation; // #Blob

        public MethodSpecRow(int method, int instantiation)
        {
            Method = method;
            Instantiation = instantiation;
        }
    }
    public struct CustomAttributeRow
    {
        public int ParentToken;
        public int AttributeTypeToken;
        public int Value;              // #Blob
        public byte Target;

        public CustomAttributeRow(int parentToken, int attributeTypeToken, int value, byte target)
        {
            ParentToken = parentToken;
            AttributeTypeToken = attributeTypeToken;
            Value = value;
            Target = target;
        }
    }
    internal enum SigElementType : byte
    {
        END = 0x00,
        VOID = 0x01,
        BOOLEAN = 0x02,
        CHAR = 0x03,
        I1 = 0x04,
        U1 = 0x05,
        I2 = 0x06,
        U2 = 0x07,
        I4 = 0x08,
        U4 = 0x09,
        I8 = 0x0A,
        U8 = 0x0B,
        R4 = 0x0C,
        R8 = 0x0D,
        STRING = 0x0E,
        PTR = 0x0F,
        BYREF = 0x10,
        VALUETYPE = 0x11,
        CLASS = 0x12,
        VAR = 0x13,
        ARRAY = 0x14,
        GENERICINST = 0x15,
        OBJECT = 0x1C,
        I = 0x18,
        U = 0x19,
        SZARRAY = 0x1D,
        MVAR = 0x1E,
    }
    internal sealed class SigWriter
    {
        private readonly List<byte> _b = new();

        public void Byte(byte v) => _b.Add(v);

        public void CompressedUInt(uint value)
        {
            if (value <= 0x7Fu)
            {
                _b.Add((byte)value);
                return;
            }
            if (value <= 0x3FFFu)
            {
                _b.Add((byte)((value >> 8) | 0x80));
                _b.Add((byte)(value & 0xFF));
                return;
            }
            if (value <= 0x1FFFFFFFu)
            {
                _b.Add((byte)((value >> 24) | 0xC0));
                _b.Add((byte)((value >> 16) & 0xFF));
                _b.Add((byte)((value >> 8) & 0xFF));
                _b.Add((byte)(value & 0xFF));
                return;
            }
            throw new ArgumentOutOfRangeException(nameof(value), "CompressedUInt too large.");
        }
        public byte[] ToArray() => _b.ToArray();
    }
    internal static class SigEncoding
    {
        public static uint EncodeTypeDefOrRef(int token)
        {
            int table = MetadataToken.Table(token);
            int rid = MetadataToken.Rid(token);

            uint tag = table switch
            {
                MetadataToken.TypeDef => 0u,
                MetadataToken.TypeRef => 1u,
                MetadataToken.TypeSpec => 2u,
                _ => throw new ArgumentOutOfRangeException(nameof(token), $"Not a TypeDef/Ref/Spec token: 0x{token:X8}")
            };
            return ((uint)rid << 2) | tag;
        }
    }

    internal sealed class MetadataTokenProvider : ITokenProvider
    {
        private static bool EmitParamRows = true;
        private static bool EmitParamNames = true;
        private const ushort ParamAttrIn = 0x0001;
        private const ushort ParamAttrOut = 0x0002;
        private const ushort ParamAttrOptional = 0x0010;
        private const ushort ParamAttrHasDefault = 0x1000;
        public MetadataImage Image { get; }

        private readonly Dictionary<TypeSymbol, int> _typeDefTokens
            = new(ReferenceEqualityComparer<TypeSymbol>.Instance);
        private readonly Dictionary<FieldSymbol, int> _fieldDefTokens
            = new(ReferenceEqualityComparer<FieldSymbol>.Instance);
        private readonly Dictionary<PropertySymbol, int> _propertyDefTokens
            = new(ReferenceEqualityComparer<PropertySymbol>.Instance);
        private readonly Dictionary<MethodSymbol, int> _methodDefTokens
            = new(ReferenceEqualityComparer<MethodSymbol>.Instance);

        private readonly Dictionary<TypeSymbol, int> _typeRefTokens
            = new(ReferenceEqualityComparer<TypeSymbol>.Instance);
        private readonly Dictionary<TypeSymbol, int> _typeSpecTokens
            = new(ReferenceEqualityComparer<TypeSymbol>.Instance);

        private readonly Dictionary<Symbol, int> _memberRefTokens
            = new(ReferenceEqualityComparer<Symbol>.Instance);
        private readonly Dictionary<(int MethodToken, int InstBlob), int> _methodSpecTokens = new();

        private readonly Func<NamedTypeSymbol, string?>? _externalAssemblyResolver;
        private readonly Dictionary<string, int> _assemblyRefTokenByName = new(StringComparer.Ordinal);

        private readonly Dictionary<int, NamedTypeSymbol> _valueTupleDefCache = new();

        private readonly Dictionary<ParameterSymbol, int> _paramDefTokens
            = new(ReferenceEqualityComparer<ParameterSymbol>.Instance);

        private int _defaultExternalAssemblyRefToken; // AssemblyRef
        private int _localFunctionsHostTypeToken;     // TypeDef
        private readonly NamedTypeSymbol _systemObject;
        private NamespaceSymbol? _sysNsCache;
        public MetadataTokenProvider(
            string moduleName,
            NamespaceSymbol moduleGlobalNamespace,
            NamedTypeSymbol systemObject,
            string defaultExternalAssemblyName = "std",
            Func<NamedTypeSymbol, string?>? externalAssemblyResolver = null)
        {
            if (moduleGlobalNamespace is null) throw new ArgumentNullException(nameof(moduleGlobalNamespace));
            _systemObject = systemObject ?? throw new ArgumentNullException(nameof(systemObject));
            _externalAssemblyResolver = externalAssemblyResolver;

            Image = new MetadataImage(moduleName, defaultExternalAssemblyName);
            // default external assembly ref
            _defaultExternalAssemblyRefToken = EnsureAssemblyRef(Image.DefaultExternalAssemblyName);

            var allTypes = CollectAllModuleTypes(moduleGlobalNamespace);
            for (int i = 0; i < allTypes.Length; i++)
            {
                var t = allTypes[i];
                int rid = Image.TypeDefs.Count + 1;
                _typeDefTokens.Add(t, MetadataToken.Make(MetadataToken.TypeDef, rid));
                Image.TypeDefs.Add(default); // placeholder row
            }
            for (int i = 0; i < allTypes.Length; i++)
                FillTypeDefAndMembers(allTypes[i]);

            EmitCustomAttributes(allTypes);
        }
        private static ushort MapParamFlags(ParameterSymbol p)
        {
            if (p is null) return 0;
            ushort flags = 0;
            if (p.RefKind == ParameterRefKind.Out)
                flags |= ParamAttrOut;
            else if (p.RefKind == ParameterRefKind.In || p.IsReadOnlyRef)
                flags |= ParamAttrIn;

            if (p.HasExplicitDefault)
                flags |= (ushort)(ParamAttrOptional | ParamAttrHasDefault);
            return flags;
        }
        private static System.Reflection.FieldAttributes MapFieldAccessibility(Accessibility a) => a switch
        {
            Accessibility.Private => System.Reflection.FieldAttributes.Private,
            Accessibility.ProtectedAndInternal => System.Reflection.FieldAttributes.FamANDAssem,
            Accessibility.Internal => System.Reflection.FieldAttributes.Assembly,
            Accessibility.Protected => System.Reflection.FieldAttributes.Family,
            Accessibility.ProtectedOrInternal => System.Reflection.FieldAttributes.FamORAssem,
            Accessibility.Public => System.Reflection.FieldAttributes.Public,
            _ => System.Reflection.FieldAttributes.Private
        };
        private static System.Reflection.MethodAttributes MapMethodAccessibility(Accessibility a) => a switch
        {
            Accessibility.Private => System.Reflection.MethodAttributes.Private,
            Accessibility.ProtectedAndInternal => System.Reflection.MethodAttributes.FamANDAssem,
            Accessibility.Internal => System.Reflection.MethodAttributes.Assembly,
            Accessibility.Protected => System.Reflection.MethodAttributes.Family,
            Accessibility.ProtectedOrInternal => System.Reflection.MethodAttributes.FamORAssem,
            Accessibility.Public => System.Reflection.MethodAttributes.Public,
            _ => System.Reflection.MethodAttributes.Private
        };
        private static int MapTypeVisibility(NamedTypeSymbol t)
        {
            bool isNested = t.ContainingSymbol is NamedTypeSymbol;

            if (!isNested)
            {
                return t.DeclaredAccessibility == Accessibility.Public
                    ? (int)System.Reflection.TypeAttributes.Public
                    : (int)System.Reflection.TypeAttributes.NotPublic; // internal/default
            }

            return t.DeclaredAccessibility switch
            {
                Accessibility.Public => (int)System.Reflection.TypeAttributes.NestedPublic,
                Accessibility.Private => (int)System.Reflection.TypeAttributes.NestedPrivate,
                Accessibility.Protected => (int)System.Reflection.TypeAttributes.NestedFamily,
                Accessibility.Internal => (int)System.Reflection.TypeAttributes.NestedAssembly,
                Accessibility.ProtectedAndInternal => (int)System.Reflection.TypeAttributes.NestedFamANDAssem,
                Accessibility.ProtectedOrInternal => (int)System.Reflection.TypeAttributes.NestedFamORAssem,
                _ => (int)System.Reflection.TypeAttributes.NestedPrivate
            };
        }
        public int GetUserStringToken(string value) => Image.UserStrings.GetToken(value);
        private NamespaceSymbol GetSystemNamespaceOrThrow()
        {
            if (_sysNsCache != null) return _sysNsCache;
            if (_systemObject.ContainingSymbol is NamespaceSymbol ns && string.Equals(ns.Name, "System", StringComparison.Ordinal))
                return _sysNsCache = ns;
            throw new InvalidOperationException("System.Object must be declared in namespace System.");
        }
        private NamedTypeSymbol GetValueTupleDef(int arity)
        {
            if (_valueTupleDefCache.TryGetValue(arity, out var t))
                return t;

            var sys = GetSystemNamespaceOrThrow();
            var cands = sys.GetTypeMembers("ValueTuple", arity);
            if (cands.IsDefaultOrEmpty)
                throw new InvalidOperationException($"Missing System.ValueTuple with arity {arity}.");

            t = cands[0];
            _valueTupleDefCache[arity] = t;
            return t;
        }
        private void WriteValueTupleSigForElements(SigWriter w, ImmutableArray<TypeSymbol> elems, int start)
        {
            int remaining = elems.Length - start;
            if (remaining == 0)
            {
                var def0 = GetValueTupleDef(0);
                w.Byte((byte)SigElementType.VALUETYPE);
                w.CompressedUInt(SigEncoding.EncodeTypeDefOrRef(GetTypeToken(def0)));
                return;
            }

            if (remaining <= 7)
            {
                var def = GetValueTupleDef(remaining);
                w.Byte((byte)SigElementType.GENERICINST);
                w.Byte((byte)SigElementType.VALUETYPE);
                w.CompressedUInt(SigEncoding.EncodeTypeDefOrRef(GetTypeToken(def)));
                w.CompressedUInt((uint)remaining);
                for (int i = 0; i < remaining; i++)
                    WriteTypeSig(w, elems[start + i]);
                return;
            }

            var def8 = GetValueTupleDef(8);
            w.Byte((byte)SigElementType.GENERICINST);
            w.Byte((byte)SigElementType.VALUETYPE);
            w.CompressedUInt(SigEncoding.EncodeTypeDefOrRef(GetTypeToken(def8)));
            w.CompressedUInt(8);
            for (int i = 0; i < 7; i++)
                WriteTypeSig(w, elems[start + i]);

            WriteValueTupleSigForElements(w, elems, start + 7);
        }
        public int GetTypeToken(TypeSymbol type)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            if (type is TupleTypeSymbol)
                return GetOrAddTypeSpec(type);
            // Named type
            if (type is NamedTypeSymbol nt)
            {
                if (nt is SubstitutedNamedTypeSymbol)
                    return GetOrAddTypeSpec(type);

                if (_typeDefTokens.TryGetValue(nt, out var defTok))
                    return defTok;

                return GetOrAddTypeRef(nt);
            }

            // Array / pointer / type params => TypeSpec
            return GetOrAddTypeSpec(type);
        }
        public int GetMethodToken(MethodSymbol method)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));

            if (IsGenericMethodInstantiation(method))
                return GetOrAddMethodSpec(method);

            if (_methodDefTokens.TryGetValue(method, out var defTok))
                return defTok;

            if (method is LocalFunctionSymbol)
                return GetOrAddLocalFunctionMethodDef(method);

            return GetOrAddMemberRef(method);
        }
        private static bool IsGenericMethodInstantiation(MethodSymbol method)
        {
            if (method is not ConstructedMethodSymbol)
                return false;

            var targs = method.TypeArguments;
            return !targs.IsDefaultOrEmpty;
        }
        public int GetFieldToken(FieldSymbol field)
        {
            if (field is null) throw new ArgumentNullException(nameof(field));

            if (_fieldDefTokens.TryGetValue(field, out var defTok))
                return defTok;

            return GetOrAddMemberRef(field);
        }
        public int GetPropertyToken(PropertySymbol property)
        {
            if (property is null) throw new ArgumentNullException(nameof(property));
            if (_propertyDefTokens.TryGetValue(property, out var defTok))
                return defTok;

            throw new NotSupportedException("Property tokens are available only for module-defined PropertyDef symbols.");
        }
        private int EnsureAssemblyRef(string name)
        {
            if (string.IsNullOrEmpty(name))
                name = Image.DefaultExternalAssemblyName;

            if (_assemblyRefTokenByName.TryGetValue(name, out var tok))
                return tok;

            int nameIdx = Image.Strings.Add(name);
            int rid = Image.AssemblyRefs.Count + 1;
            Image.AssemblyRefs.Add(new AssemblyRefRow(nameIdx));
            tok = MetadataToken.Make(MetadataToken.AssemblyRef, rid);
            _assemblyRefTokenByName[name] = tok;
            return tok;
        }
        
        private ImmutableArray<NamedTypeSymbol> CollectAllModuleTypes(NamespaceSymbol root)
        {
            var set = new HashSet<NamedTypeSymbol>(ReferenceEqualityComparer<NamedTypeSymbol>.Instance);
            var list = new List<NamedTypeSymbol>();

            void AddTypeAndNested(NamedTypeSymbol t)
            {
                if (!set.Add(t)) return;
                list.Add(t);

                var members = t.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is NamedTypeSymbol nt)
                        AddTypeAndNested(nt);
                }
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
        private void FillTypeDefAndMembers(NamedTypeSymbol type)
        {
            int typeDefRid = MetadataToken.Rid(_typeDefTokens[type]);
            int typeDefIndex = typeDefRid - 1;

            bool isNested = type.ContainingSymbol is NamedTypeSymbol;
            string nsName = isNested ? "" : GetNamespaceString(type);
            string mdName = GetMetadataTypeName(type);

            int nameIdx = Image.Strings.Add(mdName);
            int nsIdx = Image.Strings.Add(nsName);

            if (isNested && type.ContainingSymbol is NamedTypeSymbol encNt && _typeDefTokens.ContainsKey(encNt))
            {
                int enclosingRid = MetadataToken.Rid(_typeDefTokens[encNt]);
                Image.NestedClasses.Add(new NestedClassRow(typeDefRid, enclosingRid));
            }

            int extendsEncoded = 0;
            if (type.BaseType is { } bt)
            {
                int btTok = GetTypeToken(bt);
                extendsEncoded = unchecked((int)SigEncoding.EncodeTypeDefOrRef(btTok));
            }

            // InterfaceImpl
            var ifaces = type.Interfaces;
            if (!ifaces.IsDefaultOrEmpty)
            {
                var seen = new HashSet<TypeSymbol>(ReferenceEqualityComparer<TypeSymbol>.Instance);

                for (int i = 0; i < ifaces.Length; i++)
                {
                    if (ifaces[i] is not NamedTypeSymbol iface)
                        continue;
                    if (iface.TypeKind != TypeKind.Interface)
                        continue;
                    if (!seen.Add(iface.OriginalDefinition))
                        continue;

                    int ifaceTok = GetTypeToken(iface);
                    int ifaceEncoded = unchecked((int)SigEncoding.EncodeTypeDefOrRef(ifaceTok));
                    Image.InterfaceImpls.Add(new InterfaceImplRow(typeDefRid, ifaceEncoded));
                }
            }

            int fieldListRid = Image.Fields.Count + 1;

            // Fields
            var members = type.GetMembers();
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is FieldSymbol f)
                {
                    int fieldRid = Image.Fields.Count + 1;
                    int fieldDefToken = MetadataToken.Make(MetadataToken.FieldDef, fieldRid);
                    _fieldDefTokens[f] = fieldDefToken;

                    int fNameIdx = Image.Strings.Add(f.Name);
                    int sigIdx = BuildFieldSig(f.Type);

                    ushort flags = 0;
                    flags |= (ushort)MapFieldAccessibility(f.DeclaredAccessibility);
                    if (f.IsStatic || f.IsConst) flags |= 0x0010; // const must also be static
                    if (f.IsConst) flags |= 0x0040;               // FieldAttributes.Literal

                    Image.Fields.Add(new FieldRow(flags: flags, name: fNameIdx, signature: sigIdx));

                    TryAddFieldConstant(f, fieldDefToken);
                }
            }
            int methodListRid = Image.Methods.Count + 1;

            // Methods
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is MethodSymbol m)
                {
                    int methodRid = Image.Methods.Count + 1;
                    _methodDefTokens[m] = MetadataToken.Make(MetadataToken.MethodDef, methodRid);

                    int mNameIdx = Image.Strings.Add(GetMetadataMethodName(m));
                    int sigIdx = BuildMethodSig(m);

                    int paramListRid = Image.Params.Count + 1;
                    var ps = m.Parameters;
                    if (EmitParamRows)
                    {
                        for (int p = 0; p < ps.Length; p++)
                        {
                            int paramRid = Image.Params.Count + 1;
                            int pNameIdx = EmitParamNames ? Image.Strings.Add(ps[p].Name) : 0;
                            int paramDefToken = MetadataToken.Make(MetadataToken.ParamDef, paramRid);
                            Image.Params.Add(new ParamRow(flags: MapParamFlags(ps[p]), sequence: (ushort)(p + 1), name: pNameIdx));
                            _paramDefTokens[ps[p]] = paramDefToken;
                            TryAddParameterDefault(ps[p], paramDefToken);
                        }
                    }
                    ushort mflags = 0;
                    bool isExplicitInterfaceImpl = m.ExplicitInterfaceImplementation is not null;
                    if (isExplicitInterfaceImpl)
                    {
                        mflags |= (ushort)System.Reflection.MethodAttributes.Private;
                        mflags |= (ushort)System.Reflection.MethodAttributes.Virtual;
                        mflags |= (ushort)System.Reflection.MethodAttributes.Final;
                        mflags |= (ushort)System.Reflection.MethodAttributes.HideBySig;
                        mflags |= (ushort)System.Reflection.MethodAttributes.NewSlot;
                    }
                    else
                    {
                        mflags |= (ushort)MapMethodAccessibility(m.DeclaredAccessibility);
                        if (!m.IsStatic && !m.IsConstructor && (m.IsVirtual || m.IsAbstract || m.IsOverride))
                        {
                            mflags |= (ushort)System.Reflection.MethodAttributes.Virtual;
                            mflags |= (ushort)System.Reflection.MethodAttributes.HideBySig;
                            if (m.IsAbstract)
                                mflags |= (ushort)System.Reflection.MethodAttributes.Abstract;
                            if (!m.IsOverride)
                                mflags |= (ushort)System.Reflection.MethodAttributes.NewSlot;
                            if (m.IsOverride && m.IsSealed)
                                mflags |= (ushort)System.Reflection.MethodAttributes.Final;
                        }
                    }
                    if (m.IsStatic)
                        mflags |= (ushort)System.Reflection.MethodAttributes.Static;
                    if (m.IsExtensionMethod)
                        mflags |= MetadataFlagBits.Extension;
                    
                    ushort implFlags = MethodAttributeFacts.GetMethodImplFlags(m);
                    Image.Methods.Add(new MethodDefRow(implFlags: implFlags, flags: mflags, name: mNameIdx, signature: sigIdx, paramList: paramListRid));
                    if (isExplicitInterfaceImpl)
                    {
                        int bodyMethodToken = _methodDefTokens[m];
                        int declarationMethodToken = GetMethodToken(m.ExplicitInterfaceImplementation!);
                        Image.MethodImpls.Add(new MethodImplRow(typeDefRid, bodyMethodToken, declarationMethodToken));
                    }
                }
            }

            // Properties
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is PropertySymbol p)
                {
                    int propRid = Image.Properties.Count + 1;
                    _propertyDefTokens[p] = MetadataToken.Make(MetadataToken.PropertyDef, propRid);

                    int pNameIdx = Image.Strings.Add(GetMetadataPropertyName(p));
                    int sigIdx = BuildPropertySig(p);

                    int getTok = 0;
                    int setTok = 0;

                    if (p.GetMethod is MethodSymbol gm && _methodDefTokens.TryGetValue(gm, out var gtok))
                        getTok = gtok;
                    if (p.SetMethod is MethodSymbol sm && _methodDefTokens.TryGetValue(sm, out var stok))
                        setTok = stok;

                    Image.Properties.Add(new PropertyRow(flags: 0, name: pNameIdx, sig: sigIdx, getMethod: getTok, setMethod: setTok));
                }
            }
            int typeFlags = MapTypeVisibility(type);
            if (type.TypeKind == TypeKind.Interface)
                typeFlags |= (int)System.Reflection.TypeAttributes.Interface | (int)System.Reflection.TypeAttributes.Abstract;

            Image.TypeDefs[typeDefIndex] = new TypeDefRow(
                flags: typeFlags,
                name: nameIdx,
                @namespace: nsIdx,
                extendsEncoded: extendsEncoded,
                fieldList: fieldListRid,
                methodList: methodListRid);
        }
        private static string GetMetadataMethodName(MethodSymbol method)
        {
            if (method.ExplicitInterfaceImplementation is MethodSymbol ifaceMethod)
            {
                if (ifaceMethod.ContainingSymbol is not NamedTypeSymbol ifaceType)
                    return ifaceMethod.Name;

                return BuildTypeMetadataQualification(ifaceType) + "." + ifaceMethod.Name;
            }

            return method.Name;
        }

        private static string GetMetadataPropertyName(PropertySymbol property)
        {
            if (property.ExplicitInterfaceImplementation is PropertySymbol ifaceProperty)
            {
                if (ifaceProperty.ContainingSymbol is not NamedTypeSymbol ifaceType)
                    return ifaceProperty.Name;

                return BuildTypeMetadataQualification(ifaceType) + "." + ifaceProperty.Name;
            }

            return property.Name;
        }
        private static string BuildTypeMetadataQualification(NamedTypeSymbol type)
        {
            var parts = new Stack<string>();

            Symbol? cur = type;
            while (cur is NamedTypeSymbol nt)
            {
                string part = nt.Arity > 0
                    ? nt.Name + "`" + nt.Arity.ToString()
                    : nt.Name;

                parts.Push(part);
                cur = nt.ContainingSymbol;
            }

            var sb = new StringBuilder();

            if (cur is NamespaceSymbol ns && !ns.IsGlobalNamespace)
            {
                var nsParts = new Stack<string>();
                Symbol? ncur = ns;
                while (ncur is NamespaceSymbol n && !n.IsGlobalNamespace)
                {
                    nsParts.Push(n.Name);
                    ncur = n.ContainingSymbol;
                }

                while (nsParts.Count != 0)
                {
                    if (sb.Length != 0)
                        sb.Append('.');
                    sb.Append(nsParts.Pop());
                }
            }

            while (parts.Count != 0)
            {
                if (sb.Length != 0)
                    sb.Append('.');
                sb.Append(parts.Pop());
            }

            return sb.ToString();
        }
        private void TryAddFieldConstant(FieldSymbol f, int fieldDefToken)
        {
            if (!f.IsConst)
                return;
            if (!f.ConstantValueOpt.HasValue)
                return;

            if (!TryEncodeConstant(f.Type, f.ConstantValueOpt.Value, out byte typeCode, out byte[] bytes))
                return;

            int blobIdx = Image.Blob.Add(bytes);
            Image.Constants.Add(new ConstantRow(parentToken: fieldDefToken, typeCode: typeCode, valueBlob: blobIdx));
        }
        private void TryAddParameterDefault(ParameterSymbol p, int paramDefToken)
        {
            if (p is null)
                return;
            if (!p.HasExplicitDefault)
                return;
            if (!p.DefaultValueOpt.HasValue)
                return;

            if (!TryEncodeConstant(p.Type, p.DefaultValueOpt.Value, out byte typeCode, out byte[] bytes))
                return;

            int blobIdx = Image.Blob.Add(bytes);
            Image.Constants.Add(new ConstantRow(parentToken: paramDefToken, typeCode: typeCode, valueBlob: blobIdx));
        }
        private static bool TryEncodeConstant(TypeSymbol type, object? value, out byte typeCode, out byte[] bytes)
        {
            if (value is null)
            {
                typeCode = 0;
                bytes = Array.Empty<byte>();
                return true;
            }
            if (type is NamedTypeSymbol nt && nt.TypeKind == TypeKind.Enum)
            {
                type = nt.EnumUnderlyingType ?? type;
                if (ReferenceEquals(type, nt))
                {
                    typeCode = 0;
                    bytes = Array.Empty<byte>();
                    return false;
                }
            }
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                    typeCode = 0x02; bytes = new[] { (byte)((bool)value ? 1 : 0) }; return true;
                case SpecialType.System_Char:
                    typeCode = 0x03; bytes = BitConverter.GetBytes((char)value); return true;
                case SpecialType.System_Int8:
                    typeCode = 0x04; bytes = new[] { unchecked((byte)(sbyte)value) }; return true;
                case SpecialType.System_UInt8:
                    typeCode = 0x05; bytes = new[] { (byte)value }; return true;
                case SpecialType.System_Int16:
                    typeCode = 0x06; bytes = BitConverter.GetBytes((short)value); return true;
                case SpecialType.System_UInt16:
                    typeCode = 0x07; bytes = BitConverter.GetBytes((ushort)value); return true;
                case SpecialType.System_Int32:
                    typeCode = 0x08; bytes = BitConverter.GetBytes((int)value); return true;
                case SpecialType.System_UInt32:
                    typeCode = 0x09; bytes = BitConverter.GetBytes((uint)value); return true;
                case SpecialType.System_Int64:
                    typeCode = 0x0A; bytes = BitConverter.GetBytes((long)value); return true;
                case SpecialType.System_UInt64:
                    typeCode = 0x0B; bytes = BitConverter.GetBytes((ulong)value); return true;
                case SpecialType.System_Single:
                    typeCode = 0x0C; bytes = BitConverter.GetBytes((float)value); return true;
                case SpecialType.System_Double:
                    typeCode = 0x0D; bytes = BitConverter.GetBytes((double)value); return true;
                case SpecialType.System_String:
                    typeCode = 0x0E; bytes = Encoding.UTF8.GetBytes((string)value); return true;
                default:
                    typeCode = 0;
                    bytes = Array.Empty<byte>();
                    return false;
            }
        }
        private int GetOrAddTypeRef(NamedTypeSymbol type)
        {
            if (_typeRefTokens.TryGetValue(type, out var tok))
                return tok;

            int scopeTok;
            int nsIdx;
            int nameIdx;

            if (type.ContainingSymbol is NamedTypeSymbol enclosing)
            {
                // nested typeref
                scopeTok = GetTypeToken(enclosing);
                nsIdx = 0;
                nameIdx = Image.Strings.Add(GetMetadataTypeName(type));
            }
            else
            {
                var def = type.OriginalDefinition;
                string asm =
                    _externalAssemblyResolver?.Invoke(def) 
                    ?? Image.DefaultExternalAssemblyName; // fallback to default

                scopeTok = EnsureAssemblyRef(asm);
                nsIdx = Image.Strings.Add(GetNamespaceString(type));
                nameIdx = Image.Strings.Add(GetMetadataTypeName(type));
            }

            int rid = Image.TypeRefs.Count + 1;
            Image.TypeRefs.Add(new TypeRefRow(scopeTok, nameIdx, nsIdx));
            tok = MetadataToken.Make(MetadataToken.TypeRef, rid);
            _typeRefTokens.Add(type, tok);
            return tok;
        }
        private int GetOrAddMethodSpec(MethodSymbol method)
        {
            var targs = method.TypeArguments;
            if (targs.IsDefaultOrEmpty)
                return GetMethodToken(method);

            MethodSymbol baseMethod = FindMethodSpecBaseMethod(method);
            int baseMethodTok = GetMethodToken(baseMethod);

            var w = new SigWriter();
            w.Byte(0x0A); // METHODSPEC
            w.CompressedUInt((uint)targs.Length);
            for (int i = 0; i < targs.Length; i++)
                WriteTypeSig(w, targs[i]);

            int instBlob = Image.Blob.Add(w.ToArray());
            if (_methodSpecTokens.TryGetValue((baseMethodTok, instBlob), out var tok))
                return tok;

            int rid = Image.MethodSpecs.Count + 1;
            Image.MethodSpecs.Add(new MethodSpecRow(baseMethodTok, instBlob));

            tok = MetadataToken.Make(MetadataToken.MethodSpec, rid);
            _methodSpecTokens[(baseMethodTok, instBlob)] = tok;
            return tok;
        }
        private static MethodSymbol FindMethodSpecBaseMethod(MethodSymbol method)
        {
            var original = method.OriginalDefinition;

            if (method.ContainingSymbol is NamedTypeSymbol owner)
            {
                var members = owner.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is not MethodSymbol m)
                        continue;
                    if (m is ConstructedMethodSymbol)
                        continue;
                    if (!ReferenceEquals(m.OriginalDefinition, original))
                        continue;
                    if (m.TypeParameters.Length != original.TypeParameters.Length)
                        continue;
                    if (m.Parameters.Length != original.Parameters.Length)
                        continue;
                    return m;
                }
            }
            return original;
        }
        private int GetOrAddTypeSpec(TypeSymbol type)
        {
            if (_typeSpecTokens.TryGetValue(type, out var tok))
                return tok;

            var w = new SigWriter();
            WriteTypeSig(w, type);
            int blobIdx = Image.Blob.Add(w.ToArray());

            int rid = Image.TypeSpecs.Count + 1;
            Image.TypeSpecs.Add(new TypeSpecRow(blobIdx));

            tok = MetadataToken.Make(MetadataToken.TypeSpec, rid);
            _typeSpecTokens.Add(type, tok);
            return tok;
        }
        private int GetOrAddMemberRef(Symbol member)
        {
            if (_memberRefTokens.TryGetValue(member, out var tok))
                return tok;

            if (member.ContainingSymbol is not NamedTypeSymbol declaringType)
                throw new NotSupportedException("MemberRef for members without declaring NamedTypeSymbol is not supported.");

            int classTok = GetTypeToken(declaringType);
            int nameIdx = Image.Strings.Add(member.Name);

            int sigIdx = member switch
            {
                MethodSymbol m => BuildMethodSig(m),
                FieldSymbol f => BuildFieldSig(f.Type),
                _ => throw new NotSupportedException("MemberRef supports only MethodSymbol/FieldSymbol.")
            };

            int rid = Image.MemberRefs.Count + 1;
            Image.MemberRefs.Add(new MemberRefRow(classTok, nameIdx, sigIdx));

            tok = MetadataToken.Make(MetadataToken.MemberRef, rid);
            _memberRefTokens.Add(member, tok);
            return tok;
        }
        private int GetOrAddLocalFunctionHostTypeDef()
        {
            if (_localFunctionsHostTypeToken != 0)
                return _localFunctionsHostTypeToken;
            int rid = Image.TypeDefs.Count + 1;
            _localFunctionsHostTypeToken = MetadataToken.Make(MetadataToken.TypeDef, rid);

            int nameIdx = Image.Strings.Add("<$LocalFunctions>");
            int nsIdx = 0;

            int objTok = GetTypeToken(_systemObject);
            int extendsEncoded = unchecked((int)SigEncoding.EncodeTypeDefOrRef(objTok));

            int fieldList = Image.Fields.Count + 1;
            int methodList = Image.Methods.Count + 1;

            Image.TypeDefs.Add(new TypeDefRow(flags: 0, name: nameIdx, @namespace: nsIdx, extendsEncoded: extendsEncoded, fieldList: fieldList, methodList: methodList));
            return _localFunctionsHostTypeToken;
        }
        private int GetOrAddLocalFunctionMethodDef(MethodSymbol method)
        {
            _ = GetOrAddLocalFunctionHostTypeDef();

            int methodRid = Image.Methods.Count + 1;
            int tok = MetadataToken.Make(MetadataToken.MethodDef, methodRid);
            _methodDefTokens[method] = tok;

            int nameIdx = Image.Strings.Add(method.Name);
            int sigIdx = BuildMethodSig(method);

            int paramListRid = Image.Params.Count + 1;
            var ps = method.Parameters;
            if (EmitParamRows)
            {
                for (int p = 0; p < ps.Length; p++)
                {
                    int paramRid = Image.Params.Count + 1;
                    int pNameIdx = EmitParamNames ? Image.Strings.Add(ps[p].Name) : 0;
                    int paramDefToken = MetadataToken.Make(MetadataToken.ParamDef, paramRid);
                    _paramDefTokens[ps[p]] = paramDefToken;
                    Image.Params.Add(new ParamRow(flags: MapParamFlags(ps[p]), sequence: (ushort)(p + 1), name: pNameIdx));
                    TryAddParameterDefault(ps[p], paramDefToken);
                }
            }
            ushort flags = 0;
            if (method.IsStatic)
                flags |= (ushort)System.Reflection.MethodAttributes.Static;

            Image.Methods.Add(new MethodDefRow(
                implFlags: 0,
                flags: flags,
                name: nameIdx,
                signature: sigIdx,
                paramList: paramListRid));
            return tok;
        }
        private int BuildFieldSig(TypeSymbol fieldType)
        {
            var w = new SigWriter();
            w.Byte(0x06); // FIELD
            WriteTypeSig(w, fieldType);
            return Image.Blob.Add(w.ToArray());
        }
        private int BuildPropertySig(PropertySymbol property)
        {
            if (property is null) throw new ArgumentNullException(nameof(property));

            var w = new SigWriter();

            byte cc = 0x08; // PROPERTY
            if (!property.IsStatic)
                cc |= 0x20; // HASTHIS

            w.Byte(cc);
            var ps = property.Parameters;
            w.CompressedUInt((uint)ps.Length);

            WriteTypeSig(w, property.Type);
            for (int i = 0; i < ps.Length; i++)
                WriteTypeSig(w, ps[i].Type);

            return Image.Blob.Add(w.ToArray());
        }
        private int BuildMethodSig(MethodSymbol method)
        {
            var w = new SigWriter();
            // 0x00 default, 0x20 HASTHIS, 0x10 GENERIC
            byte cc = 0x00;
            if (!method.IsStatic) cc |= 0x20; // HASTHIS

            var mtps = method.TypeParameters;
            if (!mtps.IsDefaultOrEmpty)
                cc |= 0x10; // GENERIC

            w.Byte(cc);
            if (!mtps.IsDefaultOrEmpty)
                w.CompressedUInt((uint)mtps.Length); // generic arity
            w.CompressedUInt((uint)method.Parameters.Length);

            WriteTypeSig(w, method.ReturnType);

            var ps = method.Parameters;
            for (int i = 0; i < ps.Length; i++)
                WriteTypeSig(w, ps[i].Type);

            return Image.Blob.Add(w.ToArray());
        }
        private void WriteTypeSig(SigWriter w, TypeSymbol type)
        {
            switch (type)
            {
                case null:
                    throw new ArgumentNullException(nameof(type));

                case PointerTypeSymbol ptr:
                    w.Byte((byte)SigElementType.PTR);
                    WriteTypeSig(w, ptr.PointedAtType);
                    return;

                case ArrayTypeSymbol arr:
                    if (arr.Rank == 1)
                    {
                        w.Byte((byte)SigElementType.SZARRAY);
                        WriteTypeSig(w, arr.ElementType);
                        return;
                    }
                    // Multi dim array signature
                    w.Byte((byte)SigElementType.ARRAY);
                    WriteTypeSig(w, arr.ElementType);
                    w.CompressedUInt((uint)arr.Rank);
                    w.CompressedUInt(0); // numsizes
                    w.CompressedUInt(0); // numlobounds
                    return;
                case TypeParameterSymbol tp:
                    w.Byte((byte)(tp.ContainingSymbol is MethodSymbol ? SigElementType.MVAR : SigElementType.VAR));
                    w.CompressedUInt((uint)tp.Ordinal);
                    return;
                case ByRefTypeSymbol br:
                    w.Byte((byte)SigElementType.BYREF);
                    WriteTypeSig(w, br.ElementType);
                    return;
                case TupleTypeSymbol tt:
                    WriteValueTupleSigForElements(w, tt.ElementTypes, 0);
                    return;

                case SubstitutedNamedTypeSymbol snt:
                    {
                        int defTok = GetTypeToken(snt.OriginalDefinition);

                        var effectiveArgs = new List<TypeSymbol>();
                        CollectEffectiveTypeArguments(snt, effectiveArgs);

                        if (effectiveArgs.Count == 0)
                        {
                            w.Byte((byte)(snt.IsValueType ? SigElementType.VALUETYPE : SigElementType.CLASS));
                            w.CompressedUInt(SigEncoding.EncodeTypeDefOrRef(defTok));
                            return;
                        }

                        w.Byte((byte)SigElementType.GENERICINST);
                        w.Byte((byte)(snt.IsValueType ? SigElementType.VALUETYPE : SigElementType.CLASS));
                        w.CompressedUInt(SigEncoding.EncodeTypeDefOrRef(defTok));
                        w.CompressedUInt((uint)effectiveArgs.Count);

                        for (int i = 0; i < effectiveArgs.Count; i++)
                            WriteTypeSig(w, effectiveArgs[i]);

                        return;
                    }


                case NamedTypeSymbol nt:
                    {
                        // primitives
                        if (TryWritePrimitive(w, nt.SpecialType))
                            return;

                        w.Byte((byte)(nt.IsValueType ? SigElementType.VALUETYPE : SigElementType.CLASS));
                        int tok = GetTypeToken(nt); // TypeDef/TypeRef
                        w.CompressedUInt(SigEncoding.EncodeTypeDefOrRef(tok));
                        return;
                    }
                    

                default:
                    throw new NotSupportedException($"TypeSig not supported: {type.GetType().Name}");
            }
        }
        private static void CollectEffectiveTypeArguments(NamedTypeSymbol type, List<TypeSymbol> dest)
        {
            if (type is SubstitutedNamedTypeSymbol snt)
            {
                if (snt.ContainingTypeOpt is not null)
                    CollectEffectiveTypeArguments(snt.ContainingTypeOpt, dest);

                var args = snt.TypeArguments;
                for (int i = 0; i < args.Length; i++)
                    dest.Add(args[i]);

                return;
            }

            if (type.ContainingSymbol is NamedTypeSymbol containing)
                CollectEffectiveTypeArguments(containing, dest);
        }
        private static bool TryWritePrimitive(SigWriter w, SpecialType st)
        {
            SigElementType? et = st switch
            {
                SpecialType.System_Void => SigElementType.VOID,
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
                SpecialType.System_IntPtr => SigElementType.I,
                SpecialType.System_UIntPtr => SigElementType.U,
                SpecialType.System_Single => SigElementType.R4,
                SpecialType.System_Double => SigElementType.R8,
                SpecialType.System_String => SigElementType.STRING,
                SpecialType.System_Object => SigElementType.OBJECT,
                _ => null
            };

            if (et is null) return false;
            w.Byte((byte)et.Value);
            return true;
        }

        private void EmitCustomAttributes(ImmutableArray<NamedTypeSymbol> allTypes)
        {
            for (int i = 0; i < allTypes.Length; i++)
            {
                var t = allTypes[i];
                EmitAttributes(_typeDefTokens[t], t.GetAttributes());

                var members = t.GetMembers();
                for (int m = 0; m < members.Length; m++)
                {
                    switch (members[m])
                    {
                        case FieldSymbol f when _fieldDefTokens.TryGetValue(f, out var ftok):
                            EmitAttributes(ftok, f.GetAttributes());
                            break;

                        case MethodSymbol mm when _methodDefTokens.TryGetValue(mm, out var mtok):
                            EmitAttributes(mtok, mm.GetAttributes());

                            var ps = mm.Parameters;
                            for (int p = 0; p < ps.Length; p++)
                                if (_paramDefTokens.TryGetValue(ps[p], out var ptok))
                                    EmitAttributes(ptok, ps[p].GetAttributes());
                            break;

                        case PropertySymbol p when _propertyDefTokens.TryGetValue(p, out var ptok):
                            EmitAttributes(ptok, p.GetAttributes());
                            break;
                    }
                }
            }
        }
        private void EmitAttributes(int parentToken, ImmutableArray<AttributeData> attrs)
        {
            for (int i = 0; i < attrs.Length; i++)
            {
                var a = attrs[i];
                int attrTypeTok = GetTypeToken(a.AttributeClass);
                int blobIdx = BuildCustomAttributeBlob(a);

                Image.CustomAttributes.Add(new CustomAttributeRow(
                    parentToken: parentToken,
                    attributeTypeToken: attrTypeTok,
                    value: blobIdx,
                    target: (byte)a.Target));
            }
        }
        private int BuildCustomAttributeBlob(AttributeData attr)
        {
            var w = new AttrBlobWriter();

            var ctorParams = attr.Constructor.Parameters;
            w.WriteInt32(ctorParams.Length);
            for (int i = 0; i < ctorParams.Length; i++)
                w.WriteInt32(GetTypeToken(ctorParams[i].Type));

            w.WriteInt32(attr.ConstructorArguments.Length);
            for (int i = 0; i < attr.ConstructorArguments.Length; i++)
                WriteTypedConstant(w, attr.ConstructorArguments[i]);

            w.WriteInt32(attr.NamedArguments.Length);
            for (int i = 0; i < attr.NamedArguments.Length; i++)
            {
                var na = attr.NamedArguments[i];
                w.WriteByte(na.Member is PropertySymbol ? (byte)2 : (byte)1);
                w.WriteInt32(Image.Strings.Add(na.Name));
                WriteTypedConstant(w, na.Value);
            }

            return Image.Blob.Add(w.ToArray());
        }
        private void WriteTypedConstant(AttrBlobWriter w, TypedConstant tc)
        {
            w.WriteInt32(GetTypeToken(tc.Type));

            object? v = tc.Value;
            if (v is null)
            {
                w.WriteByte(0);
                return;
            }

            switch (v)
            {
                case bool x: w.WriteByte(1); w.WriteByte((byte)(x ? 1 : 0)); return;
                case char x: w.WriteByte(2); w.WriteUInt16(x); return;
                case sbyte x: w.WriteByte(3); w.WriteSByte(x); return;
                case byte x: w.WriteByte(4); w.WriteByte(x); return;
                case short x: w.WriteByte(5); w.WriteInt16(x); return;
                case ushort x: w.WriteByte(6); w.WriteUInt16(x); return;
                case int x: w.WriteByte(7); w.WriteInt32(x); return;
                case uint x: w.WriteByte(8); w.WriteUInt32(x); return;
                case long x: w.WriteByte(9); w.WriteInt64(x); return;
                case ulong x: w.WriteByte(10); w.WriteUInt64(x); return;
                case float x: w.WriteByte(11); w.WriteSingle(x); return;
                case double x: w.WriteByte(12); w.WriteDouble(x); return;
                case string x: w.WriteByte(13); w.WriteInt32(Image.Strings.Add(x)); return;
                case TypeSymbol t: w.WriteByte(14); w.WriteInt32(GetTypeToken(t)); return;
                default:
                    throw new NotSupportedException($"Unsupported attribute constant value: {v.GetType().FullName}");
            }
        }

        private static string GetMetadataTypeName(NamedTypeSymbol t)
            => t.Arity == 0 ? t.Name : $"{t.Name}`{t.Arity}";

        private static string GetNamespaceString(NamedTypeSymbol t)
        {
            var parts = new List<string>();
            Symbol? cur = t.ContainingSymbol;
            while (cur is NamespaceSymbol ns && !ns.IsGlobalNamespace)
            {
                parts.Add(ns.Name);
                cur = ns.ContainingSymbol;
            }
            if (parts.Count == 0) return "";
            parts.Reverse();
            return string.Join(".", parts);
        }
    }
    internal interface ITokenProvider
    {
        int GetTypeToken(TypeSymbol type);
        int GetMethodToken(MethodSymbol method);
        int GetFieldToken(FieldSymbol field);
        int GetPropertyToken(PropertySymbol property);
        int GetUserStringToken(string value);
    }
}
