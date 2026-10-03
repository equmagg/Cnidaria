using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Cnidaria.Cs
{
    internal enum EcmaTable : byte
    {
        Module = 0x00,
        TypeRef = 0x01,
        TypeDef = 0x02,
        Field = 0x04,
        MethodDef = 0x06,
        Param = 0x08,
        InterfaceImpl = 0x09,
        MemberRef = 0x0A,
        Constant = 0x0B,
        CustomAttribute = 0x0C,
        FieldMarshal = 0x0D,
        DeclSecurity = 0x0E,
        ClassLayout = 0x0F,
        FieldLayout = 0x10,
        StandAloneSig = 0x11,
        EventMap = 0x12,
        Event = 0x14,
        PropertyMap = 0x15,
        Property = 0x17,
        MethodSemantics = 0x18,
        MethodImpl = 0x19,
        ModuleRef = 0x1A,
        TypeSpec = 0x1B,
        ImplMap = 0x1C,
        FieldRva = 0x1D,
        Assembly = 0x20,
        AssemblyRef = 0x23,
        File = 0x26,
        ExportedType = 0x27,
        ManifestResource = 0x28,
        NestedClass = 0x29,
        GenericParam = 0x2A,
        MethodSpec = 0x2B,
        GenericParamConstraint = 0x2C,
        Count = 0x2D,
    }
    internal enum EcmaCodedKind : byte
    {
        TypeDefOrRef,
        HasConstant,
        HasCustomAttribute,
        HasFieldMarshal,
        HasDeclSecurity,
        MemberRefParent,
        HasSemantics,
        MethodDefOrRef,
        MemberForwarded,
        Implementation,
        CustomAttributeType,
        ResolutionScope,
        TypeOrMethodDef,
    }
    internal static class EcmaCodedIndex
    {
        private const int Unused = -1;

        private static readonly int[][] s_tables =
        {
            new[] { MetadataToken.TypeDef, MetadataToken.TypeRef, MetadataToken.TypeSpec },
            new[] { MetadataToken.FieldDef, MetadataToken.ParamDef, MetadataToken.PropertyDef },
            new[]
            {
                MetadataToken.MethodDef, MetadataToken.FieldDef, MetadataToken.TypeRef, MetadataToken.TypeDef,
                MetadataToken.ParamDef, MetadataToken.InterfaceImpl, MetadataToken.MemberRef, MetadataToken.Module,
                0x0E000000, MetadataToken.PropertyDef, 0x14000000, MetadataToken.StandAloneSig, MetadataToken.ModuleRef,
                MetadataToken.TypeSpec, MetadataToken.Assembly, MetadataToken.AssemblyRef, 0x26000000, 0x27000000,
                0x28000000, MetadataToken.GenericParam, 0x2C000000, MetadataToken.MethodSpec,
            },
            new[] { MetadataToken.FieldDef, MetadataToken.ParamDef },
            new[] { MetadataToken.TypeDef, MetadataToken.MethodDef, MetadataToken.Assembly },
            new[] { MetadataToken.TypeDef, MetadataToken.TypeRef, MetadataToken.ModuleRef, MetadataToken.MethodDef, MetadataToken.TypeSpec },
            new[] { 0x14000000, MetadataToken.PropertyDef },
            new[] { MetadataToken.MethodDef, MetadataToken.MemberRef },
            new[] { MetadataToken.FieldDef, MetadataToken.MethodDef },
            new[] { 0x26000000, MetadataToken.AssemblyRef, 0x27000000 },
            new[] { Unused, Unused, MetadataToken.MethodDef, MetadataToken.MemberRef, Unused },
            new[] { MetadataToken.Module, MetadataToken.ModuleRef, MetadataToken.AssemblyRef, MetadataToken.TypeRef },
            new[] { MetadataToken.TypeDef, MetadataToken.MethodDef },
        };
        private static readonly byte[] s_tagBits = { 2, 2, 5, 1, 2, 3, 1, 1, 1, 2, 3, 2, 1 };

        public static int TagBits(EcmaCodedKind kind) => s_tagBits[(int)kind];
        public static ReadOnlySpan<int> Tables(EcmaCodedKind kind) => s_tables[(int)kind];

        public static uint Encode(EcmaCodedKind kind, int token)
        {
            int table = MetadataToken.Table(token);
            var tables = s_tables[(int)kind];
            for (int tag = 0; tag < tables.Length; tag++)
            {
                if (tables[tag] == table)
                    return ((uint)MetadataToken.Rid(token) << TagBits(kind)) | (uint)tag;
            }
            throw new ArgumentOutOfRangeException(nameof(token), $"Token 0x{token:X8} cannot be encoded as {kind}.");
        }
        public static int Decode(EcmaCodedKind kind, uint coded)
        {
            int bits = TagBits(kind);
            int tag = (int)(coded & ((1u << bits) - 1));
            int rid = (int)(coded >> bits);
            var tables = s_tables[(int)kind];
            if (tag >= tables.Length || tables[tag] == Unused)
                throw new BadImageFormatException($"Invalid {kind} coded index 0x{coded:X}.");
            return rid == 0 ? 0 : MetadataToken.Make(tables[tag], rid);
        }
        public static uint EncodeHasConstant(int token) => Encode(EcmaCodedKind.HasConstant, token);
        public static uint EncodeHasCustomAttribute(int token) => Encode(EcmaCodedKind.HasCustomAttribute, token);
        public static uint EncodeTypeOrMethodDef(int token) => Encode(EcmaCodedKind.TypeOrMethodDef, token);
    }
    internal enum EcmaColumn : byte
    {
        UInt8Pad,
        UInt16,
        UInt32,
        String,
        Guid,
        Blob,
        Table,
        Coded,
    }
    internal readonly struct EcmaColumnSpec
    {
        public readonly EcmaColumn Kind;
        public readonly byte Argument;
        public EcmaColumnSpec(EcmaColumn kind, byte argument = 0)
        {
            Kind = kind;
            Argument = argument;
        }
    }
    internal sealed class EcmaTableLayout
    {
        private static readonly EcmaColumnSpec[][] s_schema = BuildSchema();

        public readonly int[] RowCounts;
        public readonly int[] RowSizes = new int[(int)EcmaTable.Count];
        public readonly int[][] ColumnOffsets = new int[(int)EcmaTable.Count][];
        public readonly int[][] ColumnSizes = new int[(int)EcmaTable.Count][];
        public readonly int[] TableOffsets = new int[(int)EcmaTable.Count];
        public readonly bool WideStrings;
        public readonly bool WideGuids;
        public readonly bool WideBlobs;
        public readonly int TablesSize;

        public EcmaTableLayout(int[] rowCounts, bool wideStrings, bool wideGuids, bool wideBlobs)
        {
            RowCounts = rowCounts;
            WideStrings = wideStrings;
            WideGuids = wideGuids;
            WideBlobs = wideBlobs;

            int offset = 0;
            for (int t = 0; t < (int)EcmaTable.Count; t++)
            {
                var schema = s_schema[t];
                ColumnOffsets[t] = new int[schema.Length];
                ColumnSizes[t] = new int[schema.Length];
                int rowSize = 0;
                for (int c = 0; c < schema.Length; c++)
                {
                    int size = ColumnSize(schema[c]);
                    ColumnOffsets[t][c] = rowSize;
                    ColumnSizes[t][c] = size;
                    rowSize += size;
                }
                RowSizes[t] = rowSize;
                TableOffsets[t] = offset;
                offset += rowSize * rowCounts[t];
            }
            TablesSize = offset;
        }
        public static ReadOnlySpan<EcmaColumnSpec> Schema(EcmaTable table) => s_schema[(int)table];

        private int ColumnSize(EcmaColumnSpec column) => column.Kind switch
        {
            EcmaColumn.UInt8Pad => 2,
            EcmaColumn.UInt16 => 2,
            EcmaColumn.UInt32 => 4,
            EcmaColumn.String => WideStrings ? 4 : 2,
            EcmaColumn.Guid => WideGuids ? 4 : 2,
            EcmaColumn.Blob => WideBlobs ? 4 : 2,
            EcmaColumn.Table => RowCounts[column.Argument] > 0xFFFF ? 4 : 2,
            EcmaColumn.Coded => CodedIndexSize((EcmaCodedKind)column.Argument),
            _ => throw new InvalidOperationException(),
        };
        private int CodedIndexSize(EcmaCodedKind kind)
        {
            int limit = 1 << (16 - EcmaCodedIndex.TagBits(kind));
            foreach (int table in EcmaCodedIndex.Tables(kind))
            {
                if (table >= 0 && RowCounts[(uint)table >> 24] >= limit)
                    return 4;
            }
            return 2;
        }
        private static EcmaColumnSpec[][] BuildSchema()
        {
            static EcmaColumnSpec U8() => new(EcmaColumn.UInt8Pad);
            static EcmaColumnSpec U16() => new(EcmaColumn.UInt16);
            static EcmaColumnSpec U32() => new(EcmaColumn.UInt32);
            static EcmaColumnSpec Str() => new(EcmaColumn.String);
            static EcmaColumnSpec Guid() => new(EcmaColumn.Guid);
            static EcmaColumnSpec Blob() => new(EcmaColumn.Blob);
            static EcmaColumnSpec Idx(EcmaTable table) => new(EcmaColumn.Table, (byte)table);
            static EcmaColumnSpec Coded(EcmaCodedKind kind) => new(EcmaColumn.Coded, (byte)kind);

            var schema = new EcmaColumnSpec[(int)EcmaTable.Count][];
            for (int i = 0; i < schema.Length; i++)
                schema[i] = Array.Empty<EcmaColumnSpec>();

            schema[(int)EcmaTable.Module] = new[] { U16(), Str(), Guid(), Guid(), Guid() };
            schema[(int)EcmaTable.TypeRef] = new[] { Coded(EcmaCodedKind.ResolutionScope), Str(), Str() };
            schema[(int)EcmaTable.TypeDef] = new[] { U32(), Str(), Str(), Coded(EcmaCodedKind.TypeDefOrRef), Idx(EcmaTable.Field), Idx(EcmaTable.MethodDef) };
            schema[(int)EcmaTable.Field] = new[] { U16(), Str(), Blob() };
            schema[(int)EcmaTable.MethodDef] = new[] { U32(), U16(), U16(), Str(), Blob(), Idx(EcmaTable.Param) };
            schema[(int)EcmaTable.Param] = new[] { U16(), U16(), Str() };
            schema[(int)EcmaTable.InterfaceImpl] = new[] { Idx(EcmaTable.TypeDef), Coded(EcmaCodedKind.TypeDefOrRef) };
            schema[(int)EcmaTable.MemberRef] = new[] { Coded(EcmaCodedKind.MemberRefParent), Str(), Blob() };
            schema[(int)EcmaTable.Constant] = new[] { U8(), Coded(EcmaCodedKind.HasConstant), Blob() };
            schema[(int)EcmaTable.CustomAttribute] = new[] { Coded(EcmaCodedKind.HasCustomAttribute), Coded(EcmaCodedKind.CustomAttributeType), Blob() };
            schema[(int)EcmaTable.FieldMarshal] = new[] { Coded(EcmaCodedKind.HasFieldMarshal), Blob() };
            schema[(int)EcmaTable.DeclSecurity] = new[] { U16(), Coded(EcmaCodedKind.HasDeclSecurity), Blob() };
            schema[(int)EcmaTable.ClassLayout] = new[] { U16(), U32(), Idx(EcmaTable.TypeDef) };
            schema[(int)EcmaTable.FieldLayout] = new[] { U32(), Idx(EcmaTable.Field) };
            schema[(int)EcmaTable.StandAloneSig] = new[] { Blob() };
            schema[(int)EcmaTable.EventMap] = new[] { Idx(EcmaTable.TypeDef), Idx(EcmaTable.Event) };
            schema[(int)EcmaTable.Event] = new[] { U16(), Str(), Coded(EcmaCodedKind.TypeDefOrRef) };
            schema[(int)EcmaTable.PropertyMap] = new[] { Idx(EcmaTable.TypeDef), Idx(EcmaTable.Property) };
            schema[(int)EcmaTable.Property] = new[] { U16(), Str(), Blob() };
            schema[(int)EcmaTable.MethodSemantics] = new[] { U16(), Idx(EcmaTable.MethodDef), Coded(EcmaCodedKind.HasSemantics) };
            schema[(int)EcmaTable.MethodImpl] = new[] { Idx(EcmaTable.TypeDef), Coded(EcmaCodedKind.MethodDefOrRef), Coded(EcmaCodedKind.MethodDefOrRef) };
            schema[(int)EcmaTable.ModuleRef] = new[] { Str() };
            schema[(int)EcmaTable.TypeSpec] = new[] { Blob() };
            schema[(int)EcmaTable.ImplMap] = new[] { U16(), Coded(EcmaCodedKind.MemberForwarded), Str(), Idx(EcmaTable.ModuleRef) };
            schema[(int)EcmaTable.FieldRva] = new[] { U32(), Idx(EcmaTable.Field) };
            schema[(int)EcmaTable.Assembly] = new[] { U32(), U16(), U16(), U16(), U16(), U32(), Blob(), Str(), Str() };
            schema[(int)EcmaTable.AssemblyRef] = new[] { U16(), U16(), U16(), U16(), U32(), Blob(), Str(), Str(), Blob() };
            schema[(int)EcmaTable.File] = new[] { U32(), Str(), Blob() };
            schema[(int)EcmaTable.ExportedType] = new[] { U32(), U32(), Str(), Str(), Coded(EcmaCodedKind.Implementation) };
            schema[(int)EcmaTable.ManifestResource] = new[] { U32(), U32(), Str(), Coded(EcmaCodedKind.Implementation) };
            schema[(int)EcmaTable.NestedClass] = new[] { Idx(EcmaTable.TypeDef), Idx(EcmaTable.TypeDef) };
            schema[(int)EcmaTable.GenericParam] = new[] { U16(), U16(), Coded(EcmaCodedKind.TypeOrMethodDef), Str() };
            schema[(int)EcmaTable.MethodSpec] = new[] { Coded(EcmaCodedKind.MethodDefOrRef), Blob() };
            schema[(int)EcmaTable.GenericParamConstraint] = new[] { Idx(EcmaTable.GenericParam), Coded(EcmaCodedKind.TypeDefOrRef) };
            return schema;
        }
    }
    public static class EcmaImageWriter
    {
        private const int FileAlignment = 0x200;
        private const int SectionAlignment = 0x2000;
        private const int TextRva = 0x2000;
        private const int CliHeaderSize = 72;
        private const int ImportAddressTableSize = 8;
        private const ulong SortedTablesMask = 0x0000_1600_3301_FA00;
        private const int ImportTableSize = 40 + 8 + 14 + 12;

        public static byte[] Write(MetadataImage image, bool isExecutable = false)
        {
            var text = new MetadataBuffer(64 * 1024);
            text.WriteZeros(ImportAddressTableSize + CliHeaderSize);

            var methodRvas = new int[image.Methods.Count + 1];
            for (int rid = 1; rid <= image.Methods.Count; rid++)
            {
                if (!image.MethodBodies.TryGetValue(rid, out var body))
                    continue;
                if ((body[0] & 0x3) != 0x2)
                    text.Align(4);
                methodRvas[rid] = TextRva + text.Length;
                text.WriteBytes(body);
            }

            var fieldRvas = new int[image.FieldRvas.Count];
            for (int i = 0; i < image.FieldRvaData.Count; i++)
            {
                text.Align(8);
                fieldRvas[i] = TextRva + text.Length;
                text.WriteBytes(image.FieldRvaData[i]);
            }

            text.Align(4);
            int metadataOffset = text.Length;
            byte[] metadata = SerializeMetadata(image, methodRvas, fieldRvas);
            text.WriteBytes(metadata);

            text.Align(4);
            int importTableOffset = text.Length;
            int importLookupOffset = importTableOffset + 40;
            int hintNameOffset = importLookupOffset + 8;
            int dllNameOffset = hintNameOffset + 14;
            text.WriteUInt32((uint)(TextRva + importLookupOffset));
            text.WriteUInt32(0);
            text.WriteUInt32(0);
            text.WriteUInt32((uint)(TextRva + dllNameOffset));
            text.WriteUInt32(TextRva);
            text.WriteZeros(20);
            text.WriteUInt32((uint)(TextRva + hintNameOffset));
            text.WriteUInt32(0);
            text.WriteUInt16(0);
            text.WriteBytes(Encoding.ASCII.GetBytes(isExecutable ? "_CorExeMain\0" : "_CorDllMain\0"));
            text.WriteBytes(Encoding.ASCII.GetBytes("mscoree.dll\0"));
            text.WriteByte(0);

            text.Align(4);
            text.WriteZeros(2);
            int entryStubOffset = text.Length;
            uint imageBase = isExecutable ? 0x00400000u : 0x10000000u;
            text.WriteByte(0xFF);
            text.WriteByte(0x25);
            text.WriteUInt32(imageBase + TextRva);
            int textSize = text.Length;

            text.PatchUInt32(0, (uint)(TextRva + hintNameOffset));
            WriteCliHeader(text, TextRva + metadataOffset, metadata.Length, image.EntryPointToken);

            return WritePe(text.ToArray(), textSize, importTableOffset, entryStubOffset, imageBase, isExecutable);
        }
        private static void WriteCliHeader(MetadataBuffer text, int metadataRva, int metadataSize, int entryPointToken)
        {
            int p = ImportAddressTableSize;
            text.PatchUInt32(p, CliHeaderSize);
            text.PatchUInt32(p + 4, 2u | (5u << 16));
            text.PatchUInt32(p + 8, (uint)metadataRva);
            text.PatchUInt32(p + 12, (uint)metadataSize);
            text.PatchUInt32(p + 16, 0x00000001);
            text.PatchUInt32(p + 20, (uint)entryPointToken);
        }
        private static byte[] WritePe(byte[] textBytes, int textSize, int importTableOffset, int entryStubOffset, uint imageBase, bool isExecutable)
        {
            const int HeaderSize = 0x200;
            int textRawSize = Align(textSize, FileAlignment);
            int relocRva = Align(TextRva + textSize, SectionAlignment);
            const int RelocSize = 12;
            int relocRawSize = Align(RelocSize, FileAlignment);
            int imageSize = Align(relocRva + RelocSize, SectionAlignment);

            var pe = new MetadataBuffer(HeaderSize + textRawSize + relocRawSize);
            WriteDosHeader(pe);

            pe.WriteUInt32(0x00004550);
            pe.WriteUInt16(0x014C);
            pe.WriteUInt16(2);
            pe.WriteUInt32(0);
            pe.WriteUInt32(0);
            pe.WriteUInt32(0);
            pe.WriteUInt16(0xE0);
            pe.WriteUInt16((ushort)(isExecutable ? 0x0102 : 0x2102));

            pe.WriteUInt16(0x010B);
            pe.WriteByte(8);
            pe.WriteByte(0);
            pe.WriteUInt32((uint)textRawSize);
            pe.WriteUInt32((uint)relocRawSize);
            pe.WriteUInt32(0);
            pe.WriteUInt32((uint)(TextRva + entryStubOffset));
            pe.WriteUInt32(TextRva);
            pe.WriteUInt32((uint)relocRva);
            pe.WriteUInt32(imageBase);
            pe.WriteUInt32(SectionAlignment);
            pe.WriteUInt32(FileAlignment);
            pe.WriteUInt16(4);
            pe.WriteUInt16(0);
            pe.WriteUInt16(0);
            pe.WriteUInt16(0);
            pe.WriteUInt16(4);
            pe.WriteUInt16(0);
            pe.WriteUInt32(0);
            pe.WriteUInt32((uint)imageSize);
            pe.WriteUInt32(HeaderSize);
            pe.WriteUInt32(0);
            pe.WriteUInt16(3);
            pe.WriteUInt16(0x8540);
            pe.WriteUInt32(0x100000);
            pe.WriteUInt32(0x1000);
            pe.WriteUInt32(0x100000);
            pe.WriteUInt32(0x1000);
            pe.WriteUInt32(0);
            pe.WriteUInt32(16);

            for (int i = 0; i < 16; i++)
            {
                switch (i)
                {
                    case 1:
                        pe.WriteUInt32((uint)(TextRva + importTableOffset));
                        pe.WriteUInt32(ImportTableSize);
                        break;
                    case 5:
                        pe.WriteUInt32((uint)relocRva);
                        pe.WriteUInt32(RelocSize);
                        break;
                    case 12:
                        pe.WriteUInt32(TextRva);
                        pe.WriteUInt32(ImportAddressTableSize);
                        break;
                    case 14:
                        pe.WriteUInt32(TextRva + ImportAddressTableSize);
                        pe.WriteUInt32(CliHeaderSize);
                        break;
                    default:
                        pe.WriteUInt64(0);
                        break;
                }
            }

            WriteSectionHeader(pe, ".text", textSize, TextRva, textRawSize, HeaderSize, 0x60000020);
            WriteSectionHeader(pe, ".reloc", RelocSize, relocRva, relocRawSize, HeaderSize + textRawSize, 0x42000040);
            pe.WriteZeros(HeaderSize - pe.Length);

            pe.WriteBytes(textBytes.AsSpan(0, textSize));
            pe.WriteZeros(textRawSize - textSize);

            int stubOperandRva = TextRva + entryStubOffset + 2;
            int page = stubOperandRva & ~0xFFF;
            pe.WriteUInt32((uint)page);
            pe.WriteUInt32(RelocSize);
            pe.WriteUInt16((ushort)((3 << 12) | (stubOperandRva - page)));
            pe.WriteUInt16(0);
            pe.WriteZeros(relocRawSize - RelocSize);
            return pe.ToArray();
        }
        private static void WriteDosHeader(MetadataBuffer pe)
        {
            ReadOnlySpan<byte> dos = new byte[]
            {
                0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00,
                0xB8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00,
                0x0E, 0x1F, 0xBA, 0x0E, 0x00, 0xB4, 0x09, 0xCD, 0x21, 0xB8, 0x01, 0x4C, 0xCD, 0x21, 0x54, 0x68,
                0x69, 0x73, 0x20, 0x70, 0x72, 0x6F, 0x67, 0x72, 0x61, 0x6D, 0x20, 0x63, 0x61, 0x6E, 0x6E, 0x6F,
                0x74, 0x20, 0x62, 0x65, 0x20, 0x72, 0x75, 0x6E, 0x20, 0x69, 0x6E, 0x20, 0x44, 0x4F, 0x53, 0x20,
                0x6D, 0x6F, 0x64, 0x65, 0x2E, 0x0D, 0x0D, 0x0A, 0x24, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            };
            pe.WriteBytes(dos);
        }
        private static void WriteSectionHeader(MetadataBuffer pe, string name, int virtualSize, int rva, int rawSize, int rawPointer, uint characteristics)
        {
            Span<byte> nameBytes = stackalloc byte[8];
            nameBytes.Clear();
            Encoding.ASCII.GetBytes(name, nameBytes);
            pe.WriteBytes(nameBytes);
            pe.WriteUInt32((uint)virtualSize);
            pe.WriteUInt32((uint)rva);
            pe.WriteUInt32((uint)rawSize);
            pe.WriteUInt32((uint)rawPointer);
            pe.WriteUInt32(0);
            pe.WriteUInt32(0);
            pe.WriteUInt16(0);
            pe.WriteUInt16(0);
            pe.WriteUInt32(characteristics);
        }
        private static int Align(int value, int alignment) => (value + alignment - 1) & ~(alignment - 1);

        private static byte[] SerializeMetadata(MetadataImage image, int[] methodRvas, int[] fieldRvas)
        {
            int moduleName = image.Strings.Add(image.ModuleName + ".dll");
            int assemblyName = image.Strings.Add(image.ModuleName);
            var rows = new TableRows(image);
            var strings = image.Strings.Bytes;
            var userStrings = image.UserStrings.Bytes;
            var blobs = image.Blob.Bytes;

            var layout = new EcmaTableLayout(rows.Counts, strings.Length > 0xFFFF, false, blobs.Length > 0xFFFF);
            var tables = new MetadataBuffer(layout.TablesSize + 64);
            tables.WriteUInt32(0);
            tables.WriteByte(2);
            tables.WriteByte(0);
            tables.WriteByte((byte)((layout.WideStrings ? 0x01 : 0) | (layout.WideBlobs ? 0x04 : 0)));
            tables.WriteByte(1);
            ulong valid = 0;
            for (int t = 0; t < (int)EcmaTable.Count; t++)
            {
                if (rows.Counts[t] != 0)
                    valid |= 1ul << t;
            }
            tables.WriteUInt64(valid);
            tables.WriteUInt64(SortedTablesMask);
            for (int t = 0; t < (int)EcmaTable.Count; t++)
            {
                if (rows.Counts[t] != 0)
                    tables.WriteUInt32((uint)rows.Counts[t]);
            }
            var writer = new RowWriter(tables, layout);
            rows.Write(writer, image, methodRvas, fieldRvas, moduleName, assemblyName);
            tables.Align(4);

            var guid = new MetadataBuffer(16);
            guid.WriteZeros(16);

            string version = "v4.0.30319";
            int versionLength = Align(version.Length + 1, 4);
            var streams = new (string Name, ReadOnlyMemory<byte> Data)[]
            {
                ("#~", tables.ToArray()),
                ("#Strings", PadTo4(strings)),
                ("#US", PadTo4(userStrings)),
                ("#GUID", guid.ToArray()),
                ("#Blob", PadTo4(blobs)),
            };
            int headerSize = 16 + versionLength + 4;
            foreach (var (name, _) in streams)
                headerSize += 8 + Align(name.Length + 1, 4);

            var md = new MetadataBuffer(headerSize + tables.Length + strings.Length + blobs.Length + userStrings.Length + 64);
            md.WriteUInt32(0x424A5342);
            md.WriteUInt16(1);
            md.WriteUInt16(1);
            md.WriteUInt32(0);
            md.WriteUInt32((uint)versionLength);
            md.WriteBytes(Encoding.ASCII.GetBytes(version));
            md.WriteZeros(versionLength - version.Length);
            md.WriteUInt16(0);
            md.WriteUInt16((ushort)streams.Length);
            int offset = headerSize;
            int guidOffset = 0;
            foreach (var (name, data) in streams)
            {
                if (name == "#GUID")
                    guidOffset = offset;
                md.WriteUInt32((uint)offset);
                md.WriteUInt32((uint)data.Length);
                md.WriteBytes(Encoding.ASCII.GetBytes(name));
                md.WriteZeros(Align(name.Length + 1, 4) - name.Length);
                offset += data.Length;
            }
            foreach (var (_, data) in streams)
                md.WriteBytes(data.Span);

            byte[] result = md.ToArray();
            ComputeMvid(result).TryWriteBytes(result.AsSpan(guidOffset, 16));
            return result;
        }
        private static byte[] PadTo4(ReadOnlySpan<byte> data)
        {
            var padded = new byte[Align(data.Length, 4)];
            data.CopyTo(padded);
            return padded;
        }
        private static Guid ComputeMvid(ReadOnlySpan<byte> metadata)
        {
            ulong a = 0xCBF29CE484222325ul;
            ulong b = 0x84222325CBF29CE4ul;
            foreach (byte x in metadata)
            {
                a = (a ^ x) * 0x100000001B3ul;
                b = (b ^ (byte)(x + 0x5B)) * 0x100000001B3ul;
            }
            Span<byte> bytes = stackalloc byte[16];
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, a);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], b);
            bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
            return new Guid(bytes);
        }
        private sealed class RowWriter
        {
            private readonly MetadataBuffer _buffer;
            private readonly EcmaTableLayout _layout;
            private int[] _sizes = Array.Empty<int>();
            private int _column;

            public RowWriter(MetadataBuffer buffer, EcmaTableLayout layout)
            {
                _buffer = buffer;
                _layout = layout;
            }
            public void Begin(EcmaTable table)
            {
                _sizes = _layout.ColumnSizes[(int)table];
                _column = 0;
            }
            public void Value(uint value)
            {
                int size = _sizes[_column++];
                if (size == 2)
                {
                    if (value > 0xFFFF)
                        throw new InvalidOperationException("Metadata column value does not fit its encoding.");
                    _buffer.WriteUInt16((ushort)value);
                }
                else
                {
                    _buffer.WriteUInt32(value);
                }
            }
            public void Value(int value) => Value((uint)value);
            public void Coded(EcmaCodedKind kind, int token) => Value(token == 0 ? 0u : EcmaCodedIndex.Encode(kind, token));
        }
        private sealed class TableRows
        {
            public readonly int[] Counts = new int[(int)EcmaTable.Count];
            private readonly List<(ushort Semantics, int MethodRid, int PropertyRid)> _semantics = new();

            public TableRows(MetadataImage image)
            {
                for (int i = 0; i < image.Properties.Count; i++)
                {
                    var property = image.Properties[i];
                    if (property.SetMethod != 0)
                        _semantics.Add((0x0001, MetadataToken.Rid(property.SetMethod), i + 1));
                    if (property.GetMethod != 0)
                        _semantics.Add((0x0002, MetadataToken.Rid(property.GetMethod), i + 1));
                }

                Counts[(int)EcmaTable.Module] = 1;
                Counts[(int)EcmaTable.TypeRef] = image.TypeRefs.Count;
                Counts[(int)EcmaTable.TypeDef] = image.TypeDefs.Count;
                Counts[(int)EcmaTable.Field] = image.Fields.Count;
                Counts[(int)EcmaTable.MethodDef] = image.Methods.Count;
                Counts[(int)EcmaTable.Param] = image.Params.Count;
                Counts[(int)EcmaTable.InterfaceImpl] = image.InterfaceImpls.Count;
                Counts[(int)EcmaTable.MemberRef] = image.MemberRefs.Count;
                Counts[(int)EcmaTable.Constant] = image.Constants.Count;
                Counts[(int)EcmaTable.CustomAttribute] = image.CustomAttributes.Count;
                Counts[(int)EcmaTable.ClassLayout] = image.ClassLayouts.Count;
                Counts[(int)EcmaTable.StandAloneSig] = image.StandAloneSigs.Count;
                Counts[(int)EcmaTable.PropertyMap] = image.PropertyMaps.Count;
                Counts[(int)EcmaTable.Property] = image.Properties.Count;
                Counts[(int)EcmaTable.MethodSemantics] = _semantics.Count;
                Counts[(int)EcmaTable.MethodImpl] = image.MethodImpls.Count;
                Counts[(int)EcmaTable.ModuleRef] = image.ModuleRefs.Count;
                Counts[(int)EcmaTable.TypeSpec] = image.TypeSpecs.Count;
                Counts[(int)EcmaTable.ImplMap] = image.PInvokeMaps.Count;
                Counts[(int)EcmaTable.FieldRva] = image.FieldRvas.Count;
                Counts[(int)EcmaTable.Assembly] = 1;
                Counts[(int)EcmaTable.AssemblyRef] = image.AssemblyRefs.Count;
                Counts[(int)EcmaTable.NestedClass] = image.NestedClasses.Count;
                Counts[(int)EcmaTable.GenericParam] = image.GenericParams.Count;
                Counts[(int)EcmaTable.MethodSpec] = image.MethodSpecs.Count;
                Counts[(int)EcmaTable.GenericParamConstraint] = image.GenericParamConstraints.Count;
            }
            public void Write(RowWriter w, MetadataImage image, int[] methodRvas, int[] fieldRvas, int moduleName, int assemblyName)
            {
                w.Begin(EcmaTable.Module);
                w.Value(0);
                w.Value(moduleName);
                w.Value(1);
                w.Value(0);
                w.Value(0);

                foreach (var row in image.TypeRefs)
                {
                    w.Begin(EcmaTable.TypeRef);
                    w.Coded(EcmaCodedKind.ResolutionScope, row.ResolutionScopeToken);
                    w.Value(row.Name);
                    w.Value(row.Namespace);
                }
                foreach (var row in image.TypeDefs)
                {
                    w.Begin(EcmaTable.TypeDef);
                    w.Value((uint)row.Flags);
                    w.Value(row.Name);
                    w.Value(row.Namespace);
                    w.Value((uint)row.ExtendsEncoded);
                    w.Value(row.FieldList);
                    w.Value(row.MethodList);
                }
                foreach (var row in image.Fields)
                {
                    w.Begin(EcmaTable.Field);
                    w.Value(row.Flags);
                    w.Value(row.Name);
                    w.Value(row.Signature);
                }
                for (int i = 0; i < image.Methods.Count; i++)
                {
                    var row = image.Methods[i];
                    w.Begin(EcmaTable.MethodDef);
                    w.Value(methodRvas[i + 1]);
                    w.Value(row.ImplFlags);
                    w.Value(row.Flags);
                    w.Value(row.Name);
                    w.Value(row.Signature);
                    w.Value(row.ParamList);
                }
                foreach (var row in image.Params)
                {
                    w.Begin(EcmaTable.Param);
                    w.Value(row.Flags);
                    w.Value(row.Sequence);
                    w.Value(row.Name);
                }
                foreach (var row in image.InterfaceImpls)
                {
                    w.Begin(EcmaTable.InterfaceImpl);
                    w.Value(row.ClassTypeDefRid);
                    w.Value((uint)row.InterfaceEncoded);
                }
                foreach (var row in image.MemberRefs)
                {
                    w.Begin(EcmaTable.MemberRef);
                    w.Coded(EcmaCodedKind.MemberRefParent, row.ClassToken);
                    w.Value(row.Name);
                    w.Value(row.Signature);
                }
                foreach (var row in image.Constants)
                {
                    w.Begin(EcmaTable.Constant);
                    w.Value(row.TypeCode);
                    w.Coded(EcmaCodedKind.HasConstant, row.ParentToken);
                    w.Value(row.Value);
                }
                foreach (var row in image.CustomAttributes)
                {
                    w.Begin(EcmaTable.CustomAttribute);
                    w.Coded(EcmaCodedKind.HasCustomAttribute, row.ParentToken);
                    w.Coded(EcmaCodedKind.CustomAttributeType, row.ConstructorToken);
                    w.Value(row.Value);
                }
                foreach (var row in image.ClassLayouts)
                {
                    w.Begin(EcmaTable.ClassLayout);
                    w.Value(row.PackingSize);
                    w.Value((uint)row.ClassSize);
                    w.Value(row.ParentTypeDefRid);
                }
                foreach (var row in image.StandAloneSigs)
                {
                    w.Begin(EcmaTable.StandAloneSig);
                    w.Value(row.Signature);
                }
                foreach (var row in image.PropertyMaps)
                {
                    w.Begin(EcmaTable.PropertyMap);
                    w.Value(row.ParentTypeDefRid);
                    w.Value(row.PropertyList);
                }
                foreach (var row in image.Properties)
                {
                    w.Begin(EcmaTable.Property);
                    w.Value(row.Flags);
                    w.Value(row.Name);
                    w.Value(row.Signature);
                }
                foreach (var (semantics, methodRid, propertyRid) in _semantics)
                {
                    w.Begin(EcmaTable.MethodSemantics);
                    w.Value(semantics);
                    w.Value(methodRid);
                    w.Coded(EcmaCodedKind.HasSemantics, MetadataToken.Make(MetadataToken.PropertyDef, propertyRid));
                }
                foreach (var row in image.MethodImpls)
                {
                    w.Begin(EcmaTable.MethodImpl);
                    w.Value(row.ClassTypeDefRid);
                    w.Coded(EcmaCodedKind.MethodDefOrRef, row.BodyMethodToken);
                    w.Coded(EcmaCodedKind.MethodDefOrRef, row.DeclarationMethodToken);
                }
                foreach (var row in image.ModuleRefs)
                {
                    w.Begin(EcmaTable.ModuleRef);
                    w.Value(row.Name);
                }
                foreach (var row in image.TypeSpecs)
                {
                    w.Begin(EcmaTable.TypeSpec);
                    w.Value(row.Signature);
                }
                foreach (var row in image.PInvokeMaps)
                {
                    w.Begin(EcmaTable.ImplMap);
                    w.Value(row.MappingFlags);
                    w.Coded(EcmaCodedKind.MemberForwarded, row.MethodToken);
                    w.Value(row.ImportName);
                    w.Value(row.ImportScope);
                }
                for (int i = 0; i < image.FieldRvas.Count; i++)
                {
                    w.Begin(EcmaTable.FieldRva);
                    w.Value(fieldRvas[i]);
                    w.Value(image.FieldRvas[i].FieldRid);
                }

                w.Begin(EcmaTable.Assembly);
                w.Value(0x8004u);
                w.Value(0);
                w.Value(0);
                w.Value(0);
                w.Value(0);
                w.Value(0u);
                w.Value(0);
                w.Value(assemblyName);
                w.Value(0);

                foreach (var row in image.AssemblyRefs)
                {
                    w.Begin(EcmaTable.AssemblyRef);
                    w.Value(0);
                    w.Value(0);
                    w.Value(0);
                    w.Value(0);
                    w.Value(0u);
                    w.Value(0);
                    w.Value(row.Name);
                    w.Value(0);
                    w.Value(0);
                }
                foreach (var row in image.NestedClasses)
                {
                    w.Begin(EcmaTable.NestedClass);
                    w.Value(row.NestedTypeRid);
                    w.Value(row.EnclosingTypeRid);
                }
                foreach (var row in image.GenericParams)
                {
                    w.Begin(EcmaTable.GenericParam);
                    w.Value(row.Number);
                    w.Value(row.Flags);
                    w.Coded(EcmaCodedKind.TypeOrMethodDef, row.OwnerToken);
                    w.Value(row.Name);
                }
                foreach (var row in image.MethodSpecs)
                {
                    w.Begin(EcmaTable.MethodSpec);
                    w.Coded(EcmaCodedKind.MethodDefOrRef, row.Method);
                    w.Value(row.Instantiation);
                }
                foreach (var row in image.GenericParamConstraints)
                {
                    w.Begin(EcmaTable.GenericParamConstraint);
                    w.Value(row.OwnerRid);
                    w.Value((uint)row.ConstraintEncoded);
                }
            }
        }
    }
}
