using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;

namespace Cnidaria.Cs
{
    internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new();

        private ReferenceEqualityComparer() { }

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }

    public interface IRuntimeMetadataModule
    {
        string Name { get; }
        EcmaMetadata Md { get; }
        Dictionary<(string ns, string name), int> TypeDefByFullName { get; }
        string GetSignatureKeyFromThisModule(int sigBlobIdx);
        (string ns, string name) GetTypeDefFullNameByRid(int rid);
    }
    internal static class MetadataToken
    {
        public const int Module = 0x00000000;
        public const int TypeRef = 0x01000000;
        public const int TypeDef = 0x02000000;
        public const int FieldDef = 0x04000000;
        public const int MethodDef = 0x06000000;
        public const int ParamDef = 0x08000000;
        public const int InterfaceImpl = 0x09000000;
        public const int MemberRef = 0x0A000000;
        public const int Constant = 0x0B000000;
        public const int CustomAttribute = 0x0C000000;
        public const int StandAloneSig = 0x11000000;
        public const int PropertyDef = 0x17000000;
        public const int ModuleRef = 0x1A000000;
        public const int TypeSpec = 0x1B000000;
        public const int Assembly = 0x20000000;
        public const int AssemblyRef = 0x23000000;
        public const int GenericParam = 0x2A000000;
        public const int MethodSpec = 0x2B000000;
        public const int UserString = 0x70000000;
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
        CustomAttribute,
        PInvokeMap,
        ModuleRef,
        GenericParam,
        GenericParamConstraint,
        ClassLayout,
        FieldRva,
        StandAloneSig,
    }

    internal sealed class MetadataBuffer
    {
        private byte[] _data;
        private int _length;

        public MetadataBuffer(int capacity = 256) => _data = new byte[Math.Max(capacity, 16)];

        public int Length => _length;
        public ReadOnlySpan<byte> Span => _data.AsSpan(0, _length);

        private Span<byte> Reserve(int count)
        {
            int required = checked(_length + count);
            if (required > _data.Length)
                Array.Resize(ref _data, Math.Max(required, _data.Length * 2));
            var span = _data.AsSpan(_length, count);
            _length = required;
            return span;
        }
        public void WriteByte(byte value) => Reserve(1)[0] = value;
        public void WriteUInt16(ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Reserve(2), value);
        public void WriteUInt32(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Reserve(4), value);
        public void WriteUInt64(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Reserve(8), value);
        public void WriteInt32(int value) => BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), value);
        public void WriteBytes(ReadOnlySpan<byte> bytes) => bytes.CopyTo(Reserve(bytes.Length));
        public void WriteZeros(int count) => Reserve(count).Clear();
        public void WriteCompressedUInt(uint value)
        {
            if (value <= 0x7Fu)
            {
                WriteByte((byte)value);
            }
            else if (value <= 0x3FFFu)
            {
                WriteByte((byte)((value >> 8) | 0x80));
                WriteByte((byte)value);
            }
            else if (value <= 0x1FFFFFFFu)
            {
                BinaryPrimitives.WriteUInt32BigEndian(Reserve(4), value | 0xC0000000u);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Compressed integer is too large.");
            }
        }
        public void Align(int alignment)
        {
            int padding = (alignment - (_length % alignment)) % alignment;
            WriteZeros(padding);
        }
        public void PatchUInt32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(offset, 4), value);
        public byte[] ToArray() => _data.AsSpan(0, _length).ToArray();
    }
    public sealed class MetadataImage
    {
        public string ModuleName { get; }

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
        public List<PropertyMapRow> PropertyMaps { get; } = new();
        public List<CustomAttributeRow> CustomAttributes { get; } = new();
        public List<PInvokeMapRow> PInvokeMaps { get; } = new();
        public List<ModuleRefRow> ModuleRefs { get; } = new();
        public List<GenericParamRow> GenericParams { get; } = new();
        public List<GenericParamConstraintRow> GenericParamConstraints { get; } = new();
        public List<ClassLayoutRow> ClassLayouts { get; } = new();
        public List<FieldRvaRow> FieldRvas { get; } = new();
        public List<byte[]> FieldRvaData { get; } = new();
        public List<StandAloneSigRow> StandAloneSigs { get; } = new();
        public Dictionary<int, byte[]> MethodBodies { get; } = new();
        public int EntryPointToken { get; set; }
        public MetadataImage(string moduleName)
        {
            ModuleName = moduleName ?? "";
        }
    }
    public sealed class StringsHeap
    {
        private readonly Dictionary<string, int> _offsets = new(StringComparer.Ordinal);
        private readonly MetadataBuffer _bytes = new(1024);

        public StringsHeap() => _bytes.WriteByte(0);

        internal ReadOnlySpan<byte> Bytes => _bytes.Span;
        public int Add(string? s)
        {
            if (string.IsNullOrEmpty(s))
                return 0;
            if (_offsets.TryGetValue(s, out int offset))
                return offset;

            offset = _bytes.Length;
            _bytes.WriteBytes(Encoding.UTF8.GetBytes(s));
            _bytes.WriteByte(0);
            _offsets.Add(s, offset);
            return offset;
        }
    }
    public sealed class UserStringsHeap
    {
        private readonly Dictionary<string, int> _offsets = new(StringComparer.Ordinal);
        private readonly MetadataBuffer _bytes = new(1024);

        public UserStringsHeap() => _bytes.WriteByte(0);

        internal ReadOnlySpan<byte> Bytes => _bytes.Span;
        public int GetToken(string value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            if (!_offsets.TryGetValue(value, out int offset))
            {
                offset = _bytes.Length;
                if (offset > 0x00FFFFFF)
                    throw new InvalidOperationException("The user string heap exceeds the token range.");

                _bytes.WriteCompressedUInt((uint)(value.Length * 2 + 1));
                byte terminal = 0;
                foreach (char c in value)
                {
                    _bytes.WriteUInt16(c);
                    if (terminal == 0 && RequiresNonAsciiMarker(c))
                        terminal = 1;
                }
                _bytes.WriteByte(terminal);
                _offsets.Add(value, offset);
            }
            return MetadataToken.Make(MetadataToken.UserString, offset);
        }
        private static bool RequiresNonAsciiMarker(char c)
            => c > 0xFF || c is (>= '\u0001' and <= '\u0008') or (>= '\u000E' and <= '\u001F') or '\'' or '-' or '\u007F';
    }
    public sealed class BlobHeap
    {
        private readonly Dictionary<byte[], int> _offsets = new(ByteArrayComparer.Instance);
        private readonly MetadataBuffer _bytes = new(4096);

        public BlobHeap() => _bytes.WriteByte(0);

        internal ReadOnlySpan<byte> Bytes => _bytes.Span;
        public int Add(ReadOnlySpan<byte> blob)
        {
            if (blob.IsEmpty)
                return 0;
            var key = blob.ToArray();
            if (_offsets.TryGetValue(key, out int offset))
                return offset;

            offset = _bytes.Length;
            _bytes.WriteCompressedUInt((uint)blob.Length);
            _bytes.WriteBytes(blob);
            _offsets.Add(key, offset);
            return offset;
        }
    }
    internal sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();
        public bool Equals(byte[]? x, byte[]? y) => x.AsSpan().SequenceEqual(y);
        public int GetHashCode(byte[] obj)
        {
            var hash = new HashCode();
            hash.AddBytes(obj);
            return hash.ToHashCode();
        }
    }
    public struct AssemblyRefRow
    {
        public int Name; // #Strings
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
    public struct PInvokeMapRow
    {
        public ushort MappingFlags;
        public int MethodToken;
        public int ImportName;  // #Strings
        public int ImportScope; // ModuleRef RID

        public PInvokeMapRow(ushort mappingFlags, int methodToken, int importName, int importScope)
        {
            MappingFlags = mappingFlags;
            MethodToken = methodToken;
            ImportName = importName;
            ImportScope = importScope;
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
        public int Rva;
        public ushort ImplFlags;
        public ushort Flags;
        public int Name;      // #Strings
        public int Signature; // #Blob
        public int ParamList; // first param RID, or next RID if none

        public MethodDefRow(int rva, ushort implFlags, ushort flags, int name, int signature, int paramList)
        {
            Rva = rva;
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
        public ushort Sequence; // 0 for the return value, 1..N for parameters
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
        public int GetMethod;  // MethodDef token from MethodSemantics
        public int SetMethod;  // MethodDef token from MethodSemantics
        public PropertyRow(ushort flags, int name, int sig, int getMethod, int setMethod)
        {
            Flags = flags;
            Name = name;
            Signature = sig;
            GetMethod = getMethod;
            SetMethod = setMethod;
        }
    }
    public struct PropertyMapRow
    {
        public int ParentTypeDefRid;
        public int PropertyList;
        public PropertyMapRow(int parentTypeDefRid, int propertyList)
        {
            ParentTypeDefRid = parentTypeDefRid;
            PropertyList = propertyList;
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
        public int ConstructorToken; // MethodDef or MemberRef
        public int Value;            // #Blob

        public CustomAttributeRow(int parentToken, int constructorToken, int value)
        {
            ParentToken = parentToken;
            ConstructorToken = constructorToken;
            Value = value;
        }
    }
    public struct ModuleRefRow
    {
        public int Name; // #Strings
        public ModuleRefRow(int name) => Name = name;
    }
    public struct GenericParamRow
    {
        public ushort Number;
        public ushort Flags;
        public int OwnerToken; // TypeDef or MethodDef
        public int Name;       // #Strings
        public GenericParamRow(ushort number, ushort flags, int ownerToken, int name)
        {
            Number = number;
            Flags = flags;
            OwnerToken = ownerToken;
            Name = name;
        }
    }
    public struct GenericParamConstraintRow
    {
        public int OwnerRid;          // GenericParam
        public int ConstraintEncoded; // TypeDefOrRef coded index
        public GenericParamConstraintRow(int ownerRid, int constraintEncoded)
        {
            OwnerRid = ownerRid;
            ConstraintEncoded = constraintEncoded;
        }
    }
    public struct ClassLayoutRow
    {
        public ushort PackingSize;
        public int ClassSize;
        public int ParentTypeDefRid;
        public ClassLayoutRow(ushort packingSize, int classSize, int parentTypeDefRid)
        {
            PackingSize = packingSize;
            ClassSize = classSize;
            ParentTypeDefRid = parentTypeDefRid;
        }
    }
    public struct FieldRvaRow
    {
        public int Rva;
        public int FieldRid;
        public FieldRvaRow(int rva, int fieldRid)
        {
            Rva = rva;
            FieldRid = fieldRid;
        }
    }
    public struct StandAloneSigRow
    {
        public int Signature; // #Blob
        public StandAloneSigRow(int signature) => Signature = signature;
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
        FNPTR = 0x1B,
        OBJECT = 0x1C,
        CMOD_REQD = 0x1F,
        CMOD_OPT = 0x20,
        I = 0x18,
        U = 0x19,
        SZARRAY = 0x1D,
        MVAR = 0x1E,
        PINNED = 0x45,
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

    internal static class CustomAttributeBlob
    {
        public const ushort Prolog = 0x0001;
        public const byte NamedField = 0x53;
        public const byte NamedProperty = 0x54;
        public const byte TypeTag = 0x50;
        public const byte BoxedTag = 0x51;
        public const byte EnumTag = 0x55;

        public static void WriteSerString(MetadataBuffer w, string? value)
        {
            if (value is null)
            {
                w.WriteByte(0xFF);
                return;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            w.WriteCompressedUInt((uint)bytes.Length);
            w.WriteBytes(bytes);
        }
    }
    internal ref struct CustomAttributeBlobReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public CustomAttributeBlobReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _offset = 0;
            if (data.Length < 2 || BinaryPrimitives.ReadUInt16LittleEndian(data) != CustomAttributeBlob.Prolog)
                throw new BadImageFormatException("Custom attribute blob has no prolog.");
            _offset = 2;
        }
        private ReadOnlySpan<byte> Take(int count)
        {
            if ((uint)count > (uint)(_data.Length - _offset))
                throw new BadImageFormatException("Custom attribute blob is truncated.");
            var span = _data.Slice(_offset, count);
            _offset += count;
            return span;
        }
        public byte ReadByte() => Take(1)[0];
        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
        public string? ReadSerString()
        {
            if (_offset < _data.Length && _data[_offset] == 0xFF)
            {
                _offset++;
                return null;
            }
            var reader = new SigReader(_data[_offset..]);
            uint length = reader.ReadCompressedUInt();
            int headerSize = length <= 0x7F ? 1 : length <= 0x3FFF ? 2 : 4;
            _offset += headerSize;
            return Encoding.UTF8.GetString(Take((int)length));
        }
        public object ReadPrimitive(SigElementType type) => type switch
        {
            SigElementType.BOOLEAN => ReadByte() != 0,
            SigElementType.CHAR => (char)ReadUInt16(),
            SigElementType.I1 => unchecked((sbyte)ReadByte()),
            SigElementType.U1 => ReadByte(),
            SigElementType.I2 => unchecked((short)ReadUInt16()),
            SigElementType.U2 => ReadUInt16(),
            SigElementType.I4 => unchecked((int)ReadUInt32()),
            SigElementType.U4 => ReadUInt32(),
            SigElementType.I8 => unchecked((long)ReadUInt64()),
            SigElementType.U8 => ReadUInt64(),
            SigElementType.R4 => BitConverter.UInt32BitsToSingle(ReadUInt32()),
            SigElementType.R8 => BitConverter.UInt64BitsToDouble(ReadUInt64()),
            _ => throw new BadImageFormatException($"Element type {type} is not a custom attribute primitive."),
        };
    }
    internal sealed class MetadataTokenProvider : ITokenProvider
    {
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
        private readonly Dictionary<(string Assembly, string Namespace, string Name), int> _externalTypeRefTokens = new();
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
        private readonly Dictionary<MethodSymbol, int> _returnParamTokens
            = new(ReferenceEqualityComparer<MethodSymbol>.Instance);
        private readonly Dictionary<(string Namespace, string Name), int> _typeDefTokensByName = new();
        private readonly Dictionary<string, int> _moduleRefRids = new(StringComparer.Ordinal);

        private readonly string _defaultExternalAssemblyName;
        private readonly IReadOnlyDictionary<NamedTypeSymbol, IReadOnlyList<MethodSymbol>> _synthesizedMethods;
        private readonly Dictionary<MethodSymbol, string> _synthesizedMethodNames = new(ReferenceEqualityComparer<MethodSymbol>.Instance);
        private readonly Dictionary<MethodSymbol, NamedTypeSymbol> _synthesizedMethodOwners = new(ReferenceEqualityComparer<MethodSymbol>.Instance);
        private readonly Dictionary<byte[], int> _staticDataFieldTokens = new(ByteArrayComparer.Instance);
        private readonly Dictionary<int, int> _typeSpecTokensByBlob = new();
        private readonly Dictionary<(int Class, int Name, int Signature), int> _memberRefTokensByRow = new();
        private readonly Dictionary<int, int> _standAloneSigTokensByBlob = new();
        private readonly Dictionary<(int ArrayType, ArrayMethodKind Kind), int> _arrayMethodTokens = new();
        private readonly Dictionary<WellKnownMethod, int> _wellKnownMethodTokens = new();
        private readonly NamespaceSymbol _moduleGlobalNamespace;
        private readonly NamespaceSymbol _metadataLookupGlobalNamespace;
        private readonly NamedTypeSymbol _systemObject;
        private NamespaceSymbol? _sysNsCache;
        private ImmutableArray<NamedTypeSymbol> _allTypes;
        private bool _finished;
        public MetadataTokenProvider(
            string moduleName,
            NamespaceSymbol moduleGlobalNamespace,
            NamedTypeSymbol systemObject,
            string defaultExternalAssemblyName = "std",
            Func<NamedTypeSymbol, string?>? externalAssemblyResolver = null,
            NamespaceSymbol? metadataLookupGlobalNamespace = null,
            IReadOnlyDictionary<NamedTypeSymbol, IReadOnlyList<MethodSymbol>>? synthesizedMethods = null,
            IReadOnlyList<byte[]>? staticData = null)
        {
            if (moduleGlobalNamespace is null) throw new ArgumentNullException(nameof(moduleGlobalNamespace));
            _moduleGlobalNamespace = moduleGlobalNamespace;
            _metadataLookupGlobalNamespace = metadataLookupGlobalNamespace ?? moduleGlobalNamespace;
            _systemObject = systemObject ?? throw new ArgumentNullException(nameof(systemObject));
            _externalAssemblyResolver = externalAssemblyResolver;
            _defaultExternalAssemblyName = string.IsNullOrWhiteSpace(defaultExternalAssemblyName) ? "std" : defaultExternalAssemblyName;
            _synthesizedMethods = synthesizedMethods ?? new Dictionary<NamedTypeSymbol, IReadOnlyList<MethodSymbol>>();
            NameSynthesizedMethods();

            Image = new MetadataImage(moduleName);
            Image.TypeDefs.Add(new TypeDefRow(0, Image.Strings.Add("<Module>"), 0, 0, fieldList: 1, methodList: 1));

            _allTypes = CollectAllModuleTypes(moduleGlobalNamespace);
            for (int i = 0; i < _allTypes.Length; i++)
            {
                var t = _allTypes[i];
                int rid = Image.TypeDefs.Count + 1;
                int token = MetadataToken.Make(MetadataToken.TypeDef, rid);
                _typeDefTokens.Add(t, token);
                if (t.ContainingSymbol is not NamedTypeSymbol)
                    _typeDefTokensByName.TryAdd((GetNamespaceString(t), GetMetadataTypeName(t)), token);
                Image.TypeDefs.Add(default); // placeholder row
            }
            for (int i = 0; i < _allTypes.Length; i++)
                FillTypeDefAndMembers(_allTypes[i]);
            if (staticData is not null && staticData.Count != 0)
                EmitStaticDataFields(staticData);
        }
        private void NameSynthesizedMethods()
        {
            foreach (var (owner, methods) in _synthesizedMethods)
            {
                for (int i = 0; i < methods.Count; i++)
                {
                    var method = methods[i];
                    _synthesizedMethodOwners.Add(method, owner);
                    Symbol? outer = method.ContainingSymbol;
                    while (outer is MethodSymbol { ContainingSymbol: MethodSymbol enclosing })
                        outer = enclosing;
                    string outerName = outer is MethodSymbol outerMethod ? GetMetadataMemberName(outerMethod) : "";
                    _synthesizedMethodNames.Add(method, method is LocalFunctionSymbol
                        ? $"<{outerName}>g__{method.Name}|{i}"
                        : $"<{outerName}>b__{i}");
                }
            }
        }
        private void EmitStaticDataFields(IReadOnlyList<byte[]> staticData)
        {
            var sizes = new List<int>();
            foreach (var data in staticData)
            {
                if (!sizes.Contains(data.Length))
                    sizes.Add(data.Length);
            }
            sizes.Sort();

            int hostRid = Image.TypeDefs.Count + 1;
            var objectToken = GetTypeToken(_systemObject);
            Image.TypeDefs.Add(new TypeDefRow(
                (int)(System.Reflection.TypeAttributes.NotPublic | System.Reflection.TypeAttributes.Sealed | System.Reflection.TypeAttributes.Abstract),
                Image.Strings.Add("<PrivateImplementationDetails>"),
                0,
                unchecked((int)SigEncoding.EncodeTypeDefOrRef(objectToken)),
                Image.Fields.Count + 1,
                Image.Methods.Count + 1));

            var sizeTypeTokens = new Dictionary<int, int>();
            for (int i = 0; i < sizes.Count; i++)
                sizeTypeTokens.Add(sizes[i], MetadataToken.Make(MetadataToken.TypeDef, hostRid + 1 + i));

            const ushort FieldFlags = (ushort)(System.Reflection.FieldAttributes.Assembly | System.Reflection.FieldAttributes.Static |
                System.Reflection.FieldAttributes.InitOnly | System.Reflection.FieldAttributes.HasFieldRVA);
            foreach (var data in staticData)
            {
                if (_staticDataFieldTokens.ContainsKey(data))
                    continue;
                var signature = new SigWriter();
                signature.Byte(0x06);
                signature.Byte((byte)SigElementType.VALUETYPE);
                signature.CompressedUInt(SigEncoding.EncodeTypeDefOrRef(sizeTypeTokens[data.Length]));
                Image.Fields.Add(new FieldRow(FieldFlags, Image.Strings.Add(HashName(data)), Image.Blob.Add(signature.ToArray())));
                int fieldRid = Image.Fields.Count;
                Image.FieldRvas.Add(new FieldRvaRow(0, fieldRid));
                Image.FieldRvaData.Add(data);
                _staticDataFieldTokens.Add(data, MetadataToken.Make(MetadataToken.FieldDef, fieldRid));
            }

            int valueTypeToken = GetTypeToken(FindCoreType("System", "ValueType")
                ?? throw new InvalidOperationException("The core library does not define System.ValueType."));
            foreach (int size in sizes)
            {
                Image.TypeDefs.Add(new TypeDefRow(
                    (int)(System.Reflection.TypeAttributes.NestedAssembly | System.Reflection.TypeAttributes.ExplicitLayout | System.Reflection.TypeAttributes.Sealed),
                    Image.Strings.Add("__StaticArrayInitTypeSize=" + size.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    0,
                    unchecked((int)SigEncoding.EncodeTypeDefOrRef(valueTypeToken)),
                    Image.Fields.Count + 1,
                    Image.Methods.Count + 1));
                int rid = Image.TypeDefs.Count;
                Image.NestedClasses.Add(new NestedClassRow(rid, hostRid));
                Image.ClassLayouts.Add(new ClassLayoutRow(1, size, rid));
            }
        }
        private static string HashName(ReadOnlySpan<byte> data)
        {
            ulong hash = 0xCBF29CE484222325ul;
            foreach (byte b in data)
                hash = (hash ^ b) * 0x100000001B3ul;
            return hash.ToString("X16", System.Globalization.CultureInfo.InvariantCulture) + "_" + data.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        public void Finish()
        {
            if (_finished)
                return;
            _finished = true;

            var genericParamRids = EmitGenericParameters();
            EmitCustomAttributes(_allTypes, genericParamRids);

            StableSort(Image.Constants, static row => EcmaCodedIndex.EncodeHasConstant(row.ParentToken));
            StableSort(Image.CustomAttributes, static row => EcmaCodedIndex.EncodeHasCustomAttribute(row.ParentToken));
        }
        private static void StableSort<T>(List<T> rows, Func<T, uint> key)
        {
            var keyed = new (uint Key, int Index, T Row)[rows.Count];
            for (int i = 0; i < keyed.Length; i++)
                keyed[i] = (key(rows[i]), i, rows[i]);
            Array.Sort(keyed, static (a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Index.CompareTo(b.Index));
            for (int i = 0; i < keyed.Length; i++)
                rows[i] = keyed[i].Row;
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
            if (_sysNsCache != null)
                return _sysNsCache;

            if (TryGetSystemNamespace(_metadataLookupGlobalNamespace, out var lookupSystem))
                return _sysNsCache = lookupSystem;
            if (!ReferenceEquals(_metadataLookupGlobalNamespace, _moduleGlobalNamespace) &&
                TryGetSystemNamespace(_moduleGlobalNamespace, out var moduleSystem))
            {
                return _sysNsCache = moduleSystem;
            }

            if (_systemObject.ContainingSymbol is NamespaceSymbol ns &&
                string.Equals(ns.Name, "System", StringComparison.Ordinal))
            {
                return _sysNsCache = ns;
            }

            throw new InvalidOperationException("Namespace 'System' not found.");
        }
        private static bool TryGetSystemNamespace(NamespaceSymbol root, out NamespaceSymbol systemNamespace)
        {
            var nss = root.GetNamespaceMembers();
            for (int i = 0; i < nss.Length; i++)
            {
                if (string.Equals(nss[i].Name, "System", StringComparison.Ordinal))
                {
                    systemNamespace = nss[i];
                    return true;
                }
            }
            systemNamespace = null!;
            return false;
        }
        private NamedTypeSymbol GetValueTupleDef(int arity)
        {
            if (_valueTupleDefCache.TryGetValue(arity, out var t))
                return t;

            if (TryGetValueTupleDef(_metadataLookupGlobalNamespace, arity, out t) ||
                (!ReferenceEquals(_metadataLookupGlobalNamespace, _moduleGlobalNamespace) &&
                TryGetValueTupleDef(_moduleGlobalNamespace, arity, out t)) ||
                (_systemObject.ContainingSymbol is NamespaceSymbol systemNamespace &&
                TryGetValueTupleDef(systemNamespace, arity, out t)))
            {
                _valueTupleDefCache[arity] = t;
                return t;
            }
            throw new InvalidOperationException($"Missing System.ValueTuple with arity {arity}.");
        }
        private static bool TryGetValueTupleDef(NamespaceSymbol rootOrSystem, int arity, out NamedTypeSymbol type)
        {
            NamespaceSymbol systemNamespace;

            if (rootOrSystem.IsGlobalNamespace)
            {
                if (!TryGetSystemNamespace(rootOrSystem, out systemNamespace))
                {
                    type = null!;
                    return false;
                }
            }
            else if (string.Equals(rootOrSystem.Name, "System", StringComparison.Ordinal))
            {
                systemNamespace = rootOrSystem;
            }
            else
            {
                type = null!;
                return false;
            }

            var cands = systemNamespace.GetTypeMembers("ValueTuple", arity);
            if (cands.IsDefaultOrEmpty)
            {
                type = null!;
                return false;
            }

            type = cands[0];
            return true;
        }
        private void WriteValueTupleSigForElements(SigWriter w, ImmutableArray<TypeSymbol> elems, int start)
        {
            int remaining = elems.Length - start;
            if (remaining == 0)
            {
                var def0 = GetValueTupleDef(0);
                w.Byte((byte)SigElementType.VALUETYPE);
                w.CompressedUInt(SigEncoding.EncodeTypeDefOrRef(GetTypeDefOrRefToken(def0)));
                return;
            }

            if (remaining <= 7)
            {
                var def = GetValueTupleDef(remaining);
                w.Byte((byte)SigElementType.GENERICINST);
                w.Byte((byte)SigElementType.VALUETYPE);
                w.CompressedUInt(SigEncoding.EncodeTypeDefOrRef(GetTypeDefOrRefToken(def)));
                w.CompressedUInt((uint)remaining);
                for (int i = 0; i < remaining; i++)
                    WriteTypeSig(w, elems[start + i]);
                return;
            }

            var def8 = GetValueTupleDef(8);
            w.Byte((byte)SigElementType.GENERICINST);
            w.Byte((byte)SigElementType.VALUETYPE);
            w.CompressedUInt(SigEncoding.EncodeTypeDefOrRef(GetTypeDefOrRefToken(def8)));
            w.CompressedUInt(8);
            for (int i = 0; i < 7; i++)
                WriteTypeSig(w, elems[start + i]);

            WriteValueTupleSigForElements(w, elems, start + 7);
        }
        public int GetTypeToken(TypeSymbol type)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            if (type is NamedTypeSymbol nt and not TupleTypeSymbol and not SubstitutedNamedTypeSymbol && !HasTypeArguments(nt))
                return GetTypeDefOrRefToken(nt);
            return GetOrAddTypeSpec(type);
        }
        private int GetTypeDefOrRefToken(NamedTypeSymbol definition)
            => _typeDefTokens.TryGetValue(definition, out var defTok) ? defTok : GetOrAddTypeRef(definition);
        private static bool HasTypeArguments(NamedTypeSymbol type)
        {
            for (Symbol? current = type; current is NamedTypeSymbol named; current = named.ContainingSymbol)
            {
                if (!named.TypeArguments.IsDefaultOrEmpty)
                    return true;
            }
            return false;
        }
        private NamedTypeSymbol? GetDeclaringType(Symbol member)
            => member is MethodSymbol method && _synthesizedMethodOwners.TryGetValue(method, out var owner)
                ? owner
                : member.ContainingSymbol as NamedTypeSymbol;
        private bool IsMemberOfGenericTypeDefinition(Symbol member)
            => GetDeclaringType(member) is NamedTypeSymbol type and not SubstitutedNamedTypeSymbol and not TupleTypeSymbol && HasTypeArguments(type);
        public int GetMethodToken(MethodSymbol method)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));

            if (IsGenericMethodInstantiation(method))
                return GetOrAddMethodSpec(method);

            if (_methodDefTokens.TryGetValue(method, out var defTok))
                return IsMemberOfGenericTypeDefinition(method) ? GetOrAddMemberRef(method) : defTok;

            if (method.ContainingSymbol is not NamedTypeSymbol)
                throw new InvalidOperationException($"Method '{method.Name}' was not registered as a synthesized method of its containing type.");

            return GetOrAddMemberRef(method);
        }
        public int GetMethodDefinitionToken(MethodSymbol method)
            => _methodDefTokens.TryGetValue(method, out int token)
                ? token
                : throw new InvalidOperationException($"Method '{method.Name}' is not defined in this module.");
        public int GetGenericMethodInstanceToken(MethodSymbol definition, ImmutableArray<TypeSymbol> typeArguments)
        {
            int baseMethodTok = GetMethodToken(definition);
            var w = new SigWriter();
            w.Byte(0x0A);
            w.CompressedUInt((uint)typeArguments.Length);
            for (int i = 0; i < typeArguments.Length; i++)
                WriteTypeSig(w, typeArguments[i]);
            return GetOrAddMethodSpecRow(baseMethodTok, Image.Blob.Add(w.ToArray()));
        }
        private int GetOrAddMethodSpecRow(int baseMethodTok, int instBlob)
        {
            if (_methodSpecTokens.TryGetValue((baseMethodTok, instBlob), out var tok))
                return tok;

            Image.MethodSpecs.Add(new MethodSpecRow(baseMethodTok, instBlob));
            tok = MetadataToken.Make(MetadataToken.MethodSpec, Image.MethodSpecs.Count);
            _methodSpecTokens[(baseMethodTok, instBlob)] = tok;
            return tok;
        }
        public int GetLocalSignatureToken(IReadOnlyList<TypeSymbol> localTypes)
        {
            if (localTypes.Count == 0)
                return 0;
            var w = new SigWriter();
            w.Byte(0x07);
            w.CompressedUInt((uint)localTypes.Count);
            for (int i = 0; i < localTypes.Count; i++)
                WriteTypeSig(w, localTypes[i]);
            return GetOrAddStandAloneSig(Image.Blob.Add(w.ToArray()));
        }
        public int GetCalliSignatureToken(FunctionPointerTypeSymbol functionPointer)
        {
            var w = new SigWriter();
            WriteFunctionPointerMethodSignature(w, functionPointer);
            return GetOrAddStandAloneSig(Image.Blob.Add(w.ToArray()));
        }
        private int GetOrAddStandAloneSig(int blob)
        {
            if (_standAloneSigTokensByBlob.TryGetValue(blob, out int token))
                return token;
            Image.StandAloneSigs.Add(new StandAloneSigRow(blob));
            token = MetadataToken.Make(MetadataToken.StandAloneSig, Image.StandAloneSigs.Count);
            _standAloneSigTokensByBlob.Add(blob, token);
            return token;
        }
        public int GetStaticDataFieldToken(byte[] data)
            => _staticDataFieldTokens.TryGetValue(data, out int token)
                ? token
                : throw new InvalidOperationException("Static data was not registered before emission.");
        public int GetArrayMethodToken(ArrayTypeSymbol arrayType, ArrayMethodKind kind)
        {
            int arrayToken = GetTypeToken(arrayType);
            if (_arrayMethodTokens.TryGetValue((arrayToken, kind), out int token))
                return token;

            var w = new SigWriter();
            w.Byte(0x20);
            int parameterCount = arrayType.Rank + (kind == ArrayMethodKind.Set ? 1 : 0);
            w.CompressedUInt((uint)parameterCount);
            switch (kind)
            {
                case ArrayMethodKind.Get:
                    WriteTypeSig(w, arrayType.ElementType);
                    break;
                case ArrayMethodKind.Address:
                    w.Byte((byte)SigElementType.BYREF);
                    WriteTypeSig(w, arrayType.ElementType);
                    break;
                default:
                    w.Byte((byte)SigElementType.VOID);
                    break;
            }
            for (int i = 0; i < arrayType.Rank; i++)
                w.Byte((byte)SigElementType.I4);
            if (kind == ArrayMethodKind.Set)
                WriteTypeSig(w, arrayType.ElementType);

            string name = kind switch
            {
                ArrayMethodKind.Constructor => ".ctor",
                ArrayMethodKind.Get => "Get",
                ArrayMethodKind.Set => "Set",
                _ => "Address",
            };
            token = GetOrAddMemberRefRow(arrayToken, Image.Strings.Add(name), Image.Blob.Add(w.ToArray()));
            _arrayMethodTokens.Add((arrayToken, kind), token);
            return token;
        }
        public int GetWellKnownMethodToken(WellKnownMethod method)
        {
            if (_wellKnownMethodTokens.TryGetValue(method, out int token))
                return token;

            var (ns, typeName, name, parameterCount, isStatic) = method switch
            {
                WellKnownMethod.DelegateCombine => ("System", "Delegate", "Combine", 2, true),
                WellKnownMethod.DelegateRemove => ("System", "Delegate", "Remove", 2, true),
                WellKnownMethod.TypeGetTypeFromHandle => ("System", "Type", "GetTypeFromHandle", 1, true),
                WellKnownMethod.TypeIsValueType => ("System", "Type", "get_IsValueType", 0, false),
                WellKnownMethod.TypeIsPrimitive => ("System", "Type", "get_IsPrimitive", 0, false),
                WellKnownMethod.TypeIsEnum => ("System", "Type", "get_IsEnum", 0, false),
                WellKnownMethod.ObjectGetType => ("System", "Object", "GetType", 0, false),
                WellKnownMethod.ArrayDataReference => ("System.Runtime.InteropServices", "MemoryMarshal", "GetArrayDataReference", 1, true),
                _ => throw new ArgumentOutOfRangeException(nameof(method)),
            };
            var type = FindCoreType(ns, typeName)
                ?? throw new InvalidOperationException($"The core library does not define '{ns}.{typeName}'.");
            var members = type.GetMembers();
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is MethodSymbol candidate &&
                    candidate.Name == name &&
                    candidate.IsStatic == isStatic &&
                    candidate.Parameters.Length == parameterCount &&
                    candidate.TypeParameters.IsDefaultOrEmpty)
                {
                    token = GetMethodToken(candidate);
                    _wellKnownMethodTokens.Add(method, token);
                    return token;
                }
            }
            throw new InvalidOperationException($"The core library does not define '{ns}.{typeName}.{name}'.");
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
                return IsMemberOfGenericTypeDefinition(field) ? GetOrAddMemberRef(field) : defTok;

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
                name = _defaultExternalAssemblyName;

            if (_assemblyRefTokenByName.TryGetValue(name, out var tok))
                return tok;

            int nameIdx = Image.Strings.Add(name);
            int rid = Image.AssemblyRefs.Count + 1;
            Image.AssemblyRefs.Add(new AssemblyRefRow(nameIdx));
            tok = MetadataToken.Make(MetadataToken.AssemblyRef, rid);
            _assemblyRefTokenByName[name] = tok;
            return tok;
        }
        private int EnsureModuleRef(string name)
        {
            if (_moduleRefRids.TryGetValue(name, out int rid))
                return rid;

            Image.ModuleRefs.Add(new ModuleRefRow(Image.Strings.Add(name)));
            rid = Image.ModuleRefs.Count;
            _moduleRefRids.Add(name, rid);
            return rid;
        }
        private int GetOrAddExternalTypeRef(string @namespace, string name, string? assemblyName = null)
        {
            if (_typeDefTokensByName.TryGetValue((@namespace, name), out int typeDefToken))
                return typeDefToken;

            string assembly = string.IsNullOrEmpty(assemblyName)
                ? _defaultExternalAssemblyName
                : assemblyName;
            var key = (assembly, @namespace, name);
            if (_externalTypeRefTokens.TryGetValue(key, out var token))
                return token;

            int scopeToken = EnsureAssemblyRef(assembly);
            int namespaceIndex = Image.Strings.Add(@namespace);
            int nameIndex = Image.Strings.Add(name);
            int rid = Image.TypeRefs.Count + 1;
            Image.TypeRefs.Add(new TypeRefRow(scopeToken, nameIndex, namespaceIndex));
            token = MetadataToken.Make(MetadataToken.TypeRef, rid);
            _externalTypeRefTokens.Add(key, token);
            return token;
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

            var ifaces = type.Interfaces;
            if (!ifaces.IsDefaultOrEmpty)
            {
                var seen = new List<TypeSymbol>();

                for (int i = 0; i < ifaces.Length; i++)
                {
                    if (ifaces[i] is not NamedTypeSymbol iface)
                        continue;
                    if (iface.TypeKind != TypeKind.Interface)
                        continue;
                    if (seen.Exists(existing => LocalScopeBinder.AreSameType(existing, iface)))
                        continue;
                    seen.Add(iface);

                    int ifaceTok = GetTypeToken(iface);
                    int ifaceEncoded = unchecked((int)SigEncoding.EncodeTypeDefOrRef(ifaceTok));
                    Image.InterfaceImpls.Add(new InterfaceImplRow(typeDefRid, ifaceEncoded));
                }
            }

            int fieldListRid = Image.Fields.Count + 1;

            if (type.TypeKind == TypeKind.Enum && type.EnumUnderlyingType is { } underlying)
            {
                const ushort EnumValueFieldFlags = (ushort)(System.Reflection.FieldAttributes.Public |
                    System.Reflection.FieldAttributes.SpecialName | System.Reflection.FieldAttributes.RTSpecialName);
                Image.Fields.Add(new FieldRow(EnumValueFieldFlags, Image.Strings.Add("value__"), BuildFieldSig(underlying)));
            }

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

                    var flags = MapFieldAccessibility(f.DeclaredAccessibility);
                    if (f.IsStatic || f.IsConst)
                        flags |= System.Reflection.FieldAttributes.Static;
                    if (f.IsReadOnly)
                        flags |= System.Reflection.FieldAttributes.InitOnly;
                    if (f.IsConst)
                        flags |= System.Reflection.FieldAttributes.Literal;
                    if (TryAddFieldConstant(f, fieldDefToken))
                        flags |= System.Reflection.FieldAttributes.HasDefault;

                    Image.Fields.Add(new FieldRow(flags: (ushort)flags, name: fNameIdx, signature: sigIdx));
                }
            }
            int methodListRid = Image.Methods.Count + 1;

            bool isDelegate = type.TypeKind == TypeKind.Delegate;
            bool hasAbstractMethod = false;
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is MethodSymbol m)
                {
                    int methodRid = Image.Methods.Count + 1;
                    int methodToken = MetadataToken.Make(MetadataToken.MethodDef, methodRid);
                    _methodDefTokens[m] = methodToken;

                    int mNameIdx = Image.Strings.Add(GetMetadataMethodName(m));
                    int sigIdx = BuildMethodSig(m);
                    int paramListRid = AddParamRows(m);

                    var mflags = MapMethodFlags(m, type);
                    if ((mflags & System.Reflection.MethodAttributes.Abstract) != 0)
                        hasAbstractMethod = true;

                    var implFlags = (System.Reflection.MethodImplAttributes)MethodAttributeFacts.GetMethodImplFlags(m);
                    if (isDelegate && !m.IsStatic)
                        implFlags |= System.Reflection.MethodImplAttributes.Runtime;
                    if (m.IsExtern)
                        implFlags |= (System.Reflection.MethodImplAttributes)MetadataFlagBits.Extern;

                    var dllImport = m.GetDllImportData();
                    if (dllImport is not null)
                    {
                        mflags |= System.Reflection.MethodAttributes.PinvokeImpl;
                        implFlags &= ~(System.Reflection.MethodImplAttributes)MetadataFlagBits.Extern;
                        if (dllImport.PreserveSig)
                            implFlags |= System.Reflection.MethodImplAttributes.PreserveSig;
                        else
                            implFlags &= ~System.Reflection.MethodImplAttributes.PreserveSig;
                    }
                    Image.Methods.Add(new MethodDefRow(rva: 0, implFlags: (ushort)implFlags, flags: (ushort)mflags, name: mNameIdx, signature: sigIdx, paramList: paramListRid));
                    if (dllImport is not null)
                    {
                        Image.PInvokeMaps.Add(new PInvokeMapRow(
                            PInvokeMetadataFlags.Encode(dllImport),
                            methodToken,
                            Image.Strings.Add(string.IsNullOrEmpty(dllImport.EntryPointName) ? m.Name : dllImport.EntryPointName),
                            EnsureModuleRef(dllImport.ModuleName)));
                    }
                    if (m.ExplicitInterfaceImplementation is not null)
                    {
                        int declarationMethodToken = GetMethodToken(m.ExplicitInterfaceImplementation);
                        Image.MethodImpls.Add(new MethodImplRow(typeDefRid, methodToken, declarationMethodToken));
                    }
                    if (m is SourceMethodSymbol source)
                    {
                        foreach (var interfaceMethod in source.ImplicitStaticInterfaceImplementations)
                            Image.MethodImpls.Add(new MethodImplRow(typeDefRid, methodToken, GetMethodToken(interfaceMethod)));
                    }
                }
            }

            if (_synthesizedMethods.TryGetValue(type, out var synthesized))
            {
                foreach (var m in synthesized)
                {
                    _methodDefTokens[m] = MetadataToken.Make(MetadataToken.MethodDef, Image.Methods.Count + 1);
                    int nameIndex = Image.Strings.Add(_synthesizedMethodNames[m]);
                    int signature = BuildMethodSig(m);
                    int paramList = AddParamRows(m);
                    var flags = System.Reflection.MethodAttributes.Private | System.Reflection.MethodAttributes.HideBySig;
                    if (m.IsStatic)
                        flags |= System.Reflection.MethodAttributes.Static;
                    Image.Methods.Add(new MethodDefRow(0, 0, (ushort)flags, nameIndex, signature, paramList));
                }
            }

            int propertyListRid = Image.Properties.Count + 1;
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
            if (Image.Properties.Count >= propertyListRid)
                Image.PropertyMaps.Add(new PropertyMapRow(typeDefRid, propertyListRid));

            var typeFlags = (System.Reflection.TypeAttributes)MapTypeVisibility(type);
            switch (type.TypeKind)
            {
                case TypeKind.Interface:
                    typeFlags |= System.Reflection.TypeAttributes.Interface | System.Reflection.TypeAttributes.Abstract;
                    break;
                case TypeKind.Struct:
                case TypeKind.Enum:
                    typeFlags |= System.Reflection.TypeAttributes.Sealed;
                    if (type.TypeKind == TypeKind.Struct)
                        typeFlags |= System.Reflection.TypeAttributes.SequentialLayout;
                    break;
                case TypeKind.Delegate:
                    typeFlags |= System.Reflection.TypeAttributes.Sealed;
                    break;
                default:
                    if (type.IsSealed)
                        typeFlags |= System.Reflection.TypeAttributes.Sealed;
                    if (hasAbstractMethod || HasTypeModifier(type, SyntaxKind.AbstractKeyword))
                        typeFlags |= System.Reflection.TypeAttributes.Abstract;
                    if (HasTypeModifier(type, SyntaxKind.StaticKeyword))
                        typeFlags |= System.Reflection.TypeAttributes.Abstract | System.Reflection.TypeAttributes.Sealed;
                    break;
            }

            bool hasExplicitStaticConstructor = false;
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is MethodSymbol method &&
                    method.IsStatic &&
                    method.Parameters.Length == 0 &&
                    StringComparer.Ordinal.Equals(method.Name, ".cctor") &&
                    !method.DeclaringSyntaxReferences.IsDefaultOrEmpty)
                {
                    hasExplicitStaticConstructor = true;
                    break;
                }
            }
            if (!hasExplicitStaticConstructor && type.TypeKind != TypeKind.Interface)
                typeFlags |= System.Reflection.TypeAttributes.BeforeFieldInit;

            Image.TypeDefs[typeDefIndex] = new TypeDefRow(
                flags: (int)typeFlags,
                name: nameIdx,
                @namespace: nsIdx,
                extendsEncoded: extendsEncoded,
                fieldList: fieldListRid,
                methodList: methodListRid);
        }
        private int AddParamRows(MethodSymbol method)
        {
            int paramListRid = Image.Params.Count + 1;
            if (HasReturnValueAttributes(method) || GetTupleElementNames(method.ReturnType) is not null)
            {
                _returnParamTokens[method] = MetadataToken.Make(MetadataToken.ParamDef, Image.Params.Count + 1);
                Image.Params.Add(new ParamRow(flags: 0, sequence: 0, name: 0));
            }

            var ps = method.Parameters;
            for (int p = 0; p < ps.Length; p++)
            {
                int paramDefToken = MetadataToken.Make(MetadataToken.ParamDef, Image.Params.Count + 1);
                _paramDefTokens[ps[p]] = paramDefToken;
                ushort flags = MapParamFlags(ps[p]);
                if (TryAddParameterDefault(ps[p], paramDefToken))
                    flags |= ParamAttrHasDefault;
                else
                    flags &= unchecked((ushort)~ParamAttrHasDefault);
                Image.Params.Add(new ParamRow(flags: flags, sequence: (ushort)(p + 1), name: Image.Strings.Add(ps[p].Name)));
            }
            return paramListRid;
        }
        private static bool HasReturnValueAttributes(MethodSymbol method)
        {
            var attributes = method.GetAttributes();
            for (int i = 0; i < attributes.Length; i++)
            {
                if (attributes[i].Target == AttributeApplicationTarget.ReturnValue)
                    return true;
            }
            return false;
        }
        private static System.Reflection.MethodAttributes MapMethodFlags(MethodSymbol m, NamedTypeSymbol owner)
        {
            System.Reflection.MethodAttributes flags;
            if (m.ExplicitInterfaceImplementation is not null)
            {
                flags = m.IsStatic
                    ? System.Reflection.MethodAttributes.Private | System.Reflection.MethodAttributes.HideBySig
                    : System.Reflection.MethodAttributes.Private |
                        System.Reflection.MethodAttributes.Virtual |
                        System.Reflection.MethodAttributes.Final |
                        System.Reflection.MethodAttributes.HideBySig |
                        System.Reflection.MethodAttributes.NewSlot;
            }
            else
            {
                flags = MapMethodAccessibility(m.DeclaredAccessibility) | System.Reflection.MethodAttributes.HideBySig;
                bool isDelegateInvoke = owner.TypeKind == TypeKind.Delegate && !m.IsStatic && !m.IsConstructor;
                if (!m.IsStatic && !m.IsConstructor && (m.IsVirtual || m.IsAbstract || m.IsOverride || isDelegateInvoke))
                {
                    flags |= System.Reflection.MethodAttributes.Virtual;
                    if (m.IsAbstract)
                        flags |= System.Reflection.MethodAttributes.Abstract;
                    if (!m.IsOverride)
                        flags |= System.Reflection.MethodAttributes.NewSlot;
                    if (m.IsOverride && m.IsSealed)
                        flags |= System.Reflection.MethodAttributes.Final;
                }
                else if (m.IsStatic && owner.TypeKind == TypeKind.Interface && (m.IsVirtual || m.IsAbstract))
                {
                    flags |= System.Reflection.MethodAttributes.Virtual;
                    if (m.IsAbstract)
                        flags |= System.Reflection.MethodAttributes.Abstract;
                }
                else if (m is SourceMethodSymbol { ImplementsInterfaceMethodImplicitly: true })
                {
                    flags |= System.Reflection.MethodAttributes.Virtual | System.Reflection.MethodAttributes.Final | System.Reflection.MethodAttributes.NewSlot;
                }
            }
            if (m.IsStatic)
                flags |= System.Reflection.MethodAttributes.Static;
            if (m.IsSpecialName)
                flags |= System.Reflection.MethodAttributes.SpecialName;
            if (m.IsRuntimeSpecialName)
                flags |= System.Reflection.MethodAttributes.RTSpecialName;
            return flags;
        }
        private static bool HasTypeModifier(NamedTypeSymbol type, SyntaxKind modifier)
        {
            var references = type.DeclaringSyntaxReferences;
            for (int i = 0; i < references.Length; i++)
            {
                if (references[i].Node is not BaseTypeDeclarationSyntax declaration)
                    continue;
                var modifiers = declaration.Modifiers;
                for (int j = 0; j < modifiers.Count; j++)
                {
                    if (modifiers[j].Kind == modifier)
                        return true;
                }
            }
            return false;
        }
        private string GetMetadataMethodName(MethodSymbol method)
        {
            if (_synthesizedMethodNames.TryGetValue(method, out var synthesizedName))
                return synthesizedName;

            if (method.ExplicitInterfaceImplementation is MethodSymbol ifaceMethod)
            {
                if (ifaceMethod.ContainingSymbol is not NamedTypeSymbol ifaceType)
                    return ifaceMethod.Name;

                return BuildTypeMetadataQualification(ifaceType) + "." + ifaceMethod.Name;
            }

            return GetMetadataMemberName(method);
        }
        private static string GetMetadataMemberName(MethodSymbol method)
            => method.IsConstructor ? (method.IsStatic ? ".cctor" : ".ctor") : method.Name;

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
        // Roslyn's explicit-implementation names qualify the interface as C# displays it: "System.Collections.Generic.IEnumerable<T>.GetEnumerator".
        private static string BuildTypeMetadataQualification(NamedTypeSymbol type)
        {
            var sb = new StringBuilder();
            AppendQualifiedTypeName(sb, type);
            return sb.ToString();
        }
        private static void AppendQualifiedTypeName(StringBuilder sb, TypeSymbol type)
        {
            switch (type)
            {
                case ArrayTypeSymbol array:
                    AppendQualifiedTypeName(sb, array.ElementType);
                    sb.Append('[').Append(',', array.Rank - 1).Append(']');
                    return;
                case PointerTypeSymbol pointer:
                    AppendQualifiedTypeName(sb, pointer.PointedAtType);
                    sb.Append('*');
                    return;
                case NamedTypeSymbol named:
                    if (named.ContainingSymbol is NamedTypeSymbol outer)
                    {
                        AppendQualifiedTypeName(sb, outer);
                        sb.Append('.');
                    }
                    else if (named.ContainingSymbol is NamespaceSymbol { IsGlobalNamespace: false } ns)
                    {
                        AppendNamespaceName(sb, ns);
                        sb.Append('.');
                    }
                    sb.Append(named.Name);
                    if (named.Arity > 0)
                    {
                        var arguments = named.TypeArguments;
                        sb.Append('<');
                        for (int i = 0; i < arguments.Length; i++)
                        {
                            if (i != 0)
                                sb.Append(',');
                            AppendQualifiedTypeName(sb, arguments[i]);
                        }
                        sb.Append('>');
                    }
                    return;
                default:
                    sb.Append(type.Name);
                    return;
            }
        }
        private static void AppendNamespaceName(StringBuilder sb, NamespaceSymbol ns)
        {
            if (ns.ContainingSymbol is NamespaceSymbol { IsGlobalNamespace: false } parent)
            {
                AppendNamespaceName(sb, parent);
                sb.Append('.');
            }
            sb.Append(ns.Name);
        }
        private bool TryAddFieldConstant(FieldSymbol f, int fieldDefToken)
        {
            if (!f.IsConst || !f.ConstantValueOpt.HasValue)
                return false;
            if (!TryEncodeConstant(f.Type, f.ConstantValueOpt.Value, out byte typeCode, out byte[] bytes))
                return false;

            Image.Constants.Add(new ConstantRow(parentToken: fieldDefToken, typeCode: typeCode, valueBlob: Image.Blob.Add(bytes)));
            return true;
        }
        private bool TryAddParameterDefault(ParameterSymbol p, int paramDefToken)
        {
            if (!p.HasExplicitDefault || !p.DefaultValueOpt.HasValue)
                return false;
            if (!TryEncodeConstant(p.Type, p.DefaultValueOpt.Value, out byte typeCode, out byte[] bytes))
                return false;

            Image.Constants.Add(new ConstantRow(parentToken: paramDefToken, typeCode: typeCode, valueBlob: Image.Blob.Add(bytes)));
            return true;
        }
        private static bool TryEncodeConstant(TypeSymbol type, object? value, out byte typeCode, out byte[] bytes)
        {
            if (value is null)
            {
                typeCode = (byte)SigElementType.CLASS;
                bytes = new byte[4];
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
                    typeCode = 0x0E; bytes = Encoding.Unicode.GetBytes((string)value); return true;
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
                scopeTok = GetTypeDefOrRefToken(enclosing.OriginalDefinition);
                nsIdx = 0;
                nameIdx = Image.Strings.Add(GetMetadataTypeName(type));
            }
            else
            {
                var def = type.OriginalDefinition;
                string asm =
                    _externalAssemblyResolver?.Invoke(def)
                    ?? _defaultExternalAssemblyName;

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

            return GetOrAddMethodSpecRow(baseMethodTok, Image.Blob.Add(w.ToArray()));
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

            if (!_typeSpecTokensByBlob.TryGetValue(blobIdx, out tok))
            {
                Image.TypeSpecs.Add(new TypeSpecRow(blobIdx));
                tok = MetadataToken.Make(MetadataToken.TypeSpec, Image.TypeSpecs.Count);
                _typeSpecTokensByBlob.Add(blobIdx, tok);
            }
            _typeSpecTokens.Add(type, tok);
            return tok;
        }
        private int GetOrAddMemberRef(Symbol member)
        {
            if (_memberRefTokens.TryGetValue(member, out var tok))
                return tok;

            if (GetDeclaringType(member) is not NamedTypeSymbol declaringType)
                throw new NotSupportedException("MemberRef for members without declaring NamedTypeSymbol is not supported.");

            int classTok = GetTypeToken(declaringType);
            int nameIdx;
            int sigIdx;
            switch (member)
            {
                case MethodSymbol method:
                    var definition = method.OriginalDefinition;
                    nameIdx = Image.Strings.Add(GetMetadataMethodName(definition));
                    sigIdx = BuildMethodSig(definition);
                    break;
                case FieldSymbol field:
                    nameIdx = Image.Strings.Add(field.Name);
                    sigIdx = BuildFieldSig(field.OriginalDefinition.Type);
                    break;
                default:
                    throw new NotSupportedException("MemberRef supports only MethodSymbol/FieldSymbol.");
            }

            tok = GetOrAddMemberRefRow(classTok, nameIdx, sigIdx);
            _memberRefTokens.Add(member, tok);
            return tok;
        }
        private int GetOrAddMemberRefRow(int classTok, int nameIdx, int sigIdx)
        {
            if (_memberRefTokensByRow.TryGetValue((classTok, nameIdx, sigIdx), out int tok))
                return tok;
            Image.MemberRefs.Add(new MemberRefRow(classTok, nameIdx, sigIdx));
            tok = MetadataToken.Make(MetadataToken.MemberRef, Image.MemberRefs.Count);
            _memberRefTokensByRow.Add((classTok, nameIdx, sigIdx), tok);
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

            WriteMethodReturnType(w, method);

            var ps = method.Parameters;
            for (int i = 0; i < ps.Length; i++)
                WriteTypeSig(w, ps[i].Type);

            return Image.Blob.Add(w.ToArray());
        }
        private void WriteMethodReturnType(SigWriter writer, MethodSymbol method)
        {
            if (method.ReturnsByRefReadonly && method.ReturnType is ByRefTypeSymbol byRef)
            {
                writer.Byte((byte)SigElementType.BYREF);
                WriteCustomModifier(
                    writer,
                    true,
                    "System.Runtime.InteropServices",
                    "InAttribute");
                WriteTypeSig(writer, byRef.ElementType);
                return;
            }

            WriteTypeSig(writer, method.ReturnType);
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

                case FunctionPointerTypeSymbol functionPointer:
                    w.Byte((byte)SigElementType.FNPTR);
                    WriteFunctionPointerMethodSignature(w, functionPointer);
                    return;

                case ArrayTypeSymbol arr:
                    if (arr.IsSZArray)
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
                    w.Byte((byte)(tp.ContainingSymbol is NamedTypeSymbol ? SigElementType.VAR : SigElementType.MVAR));
                    w.CompressedUInt((uint)GetMetadataTypeParameterIndex(tp));
                    return;
                case ByRefTypeSymbol br:
                    w.Byte((byte)SigElementType.BYREF);
                    WriteTypeSig(w, br.ElementType);
                    return;
                case TupleTypeSymbol tt:
                    WriteValueTupleSigForElements(w, tt.ElementTypes, 0);
                    return;

                case NamedTypeSymbol nt:
                    {
                        if (TryWritePrimitive(w, nt.SpecialType))
                            return;

                        int defTok = GetTypeDefOrRefToken(nt.OriginalDefinition);
                        var effectiveArgs = new List<TypeSymbol>();
                        CollectEffectiveTypeArguments(nt, effectiveArgs);
                        if (effectiveArgs.Count != 0)
                            w.Byte((byte)SigElementType.GENERICINST);
                        w.Byte((byte)(nt.IsValueType ? SigElementType.VALUETYPE : SigElementType.CLASS));
                        w.CompressedUInt(SigEncoding.EncodeTypeDefOrRef(defTok));
                        if (effectiveArgs.Count != 0)
                        {
                            w.CompressedUInt((uint)effectiveArgs.Count);
                            for (int i = 0; i < effectiveArgs.Count; i++)
                                WriteTypeSig(w, effectiveArgs[i]);
                        }
                        return;
                    }

                default:
                    throw new NotSupportedException($"TypeSig not supported: {type.GetType().Name}");
            }
        }
        private void WriteFunctionPointerMethodSignature(SigWriter w, FunctionPointerTypeSymbol functionPointer)
        {
            w.Byte(functionPointer.CallingConvention switch
            {
                FunctionPointerCallingConvention.Managed => (byte)0x00,
                FunctionPointerCallingConvention.Cdecl => (byte)0x01,
                FunctionPointerCallingConvention.Stdcall => (byte)0x02,
                FunctionPointerCallingConvention.Thiscall => (byte)0x03,
                FunctionPointerCallingConvention.Fastcall => (byte)0x04,
                _ => (byte)0x09
            });
            w.CompressedUInt((uint)functionPointer.Parameters.Length);
            WriteFunctionPointerSignatureType(w, functionPointer.ReturnType, functionPointer.ReturnRefKind);
            for (int i = 0; i < functionPointer.Parameters.Length; i++)
            {
                var parameter = functionPointer.Parameters[i];
                WriteFunctionPointerSignatureType(w, parameter.Type, parameter.RefKind);
            }
        }
        private void WriteFunctionPointerSignatureType(
            SigWriter writer,
            TypeSymbol type,
            FunctionPointerRefKind refKind)
        {
            if (refKind != FunctionPointerRefKind.None)
            {
                writer.Byte((byte)SigElementType.BYREF);
                switch (refKind)
                {
                    case FunctionPointerRefKind.Out:
                        WriteCustomModifier(
                            writer,
                            true,
                            "System.Runtime.InteropServices",
                            "OutAttribute");
                        break;
                    case FunctionPointerRefKind.In:
                        WriteCustomModifier(
                            writer,
                            true,
                            "System.Runtime.InteropServices",
                            "InAttribute");
                        break;
                    case FunctionPointerRefKind.RefReadOnly:
                        WriteCustomModifier(
                            writer,
                            false,
                            "System.Runtime.CompilerServices",
                            "RequiresLocationAttribute");
                        break;
                }
            }
            WriteTypeSig(writer, type);
        }

        private void WriteCustomModifier(
            SigWriter writer,
            bool required,
            string @namespace,
            string name)
        {
            writer.Byte((byte)(required ? SigElementType.CMOD_REQD : SigElementType.CMOD_OPT));
            int token = GetOrAddExternalTypeRef(@namespace, name);
            writer.CompressedUInt(SigEncoding.EncodeTypeDefOrRef(token));
        }
        private static void CollectEffectiveTypeArguments(NamedTypeSymbol type, List<TypeSymbol> dest)
        {
            var containing = (type as SubstitutedNamedTypeSymbol)?.ContainingTypeOpt ?? type.OriginalDefinition.ContainingSymbol as NamedTypeSymbol;
            if (containing is not null)
                CollectEffectiveTypeArguments(containing, dest);
            dest.AddRange(type.TypeArguments);
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

        private Dictionary<TypeParameterSymbol, int> EmitGenericParameters()
        {
            var owners = new List<(uint Coded, int OwnerToken, ImmutableArray<TypeParameterSymbol> Parameters, Symbol Owner)>();
            foreach (var (type, token) in _typeDefTokens)
            {
                if (type is not NamedTypeSymbol named)
                    continue;
                var parameters = GetTypeParametersInMetadataOrder(named);
                if (!parameters.IsDefaultOrEmpty)
                    owners.Add((EcmaCodedIndex.EncodeTypeOrMethodDef(token), token, parameters, named));
            }
            foreach (var (method, token) in _methodDefTokens)
            {
                var parameters = method.TypeParameters;
                if (!parameters.IsDefaultOrEmpty)
                    owners.Add((EcmaCodedIndex.EncodeTypeOrMethodDef(token), token, parameters, method));
            }
            owners.Sort(static (a, b) => a.Coded.CompareTo(b.Coded));

            var rids = new Dictionary<TypeParameterSymbol, int>(ReferenceEqualityComparer<TypeParameterSymbol>.Instance);
            var declared = new List<(int Rid, TypeParameterSymbol Parameter)>();
            foreach (var owner in owners)
            {
                for (int i = 0; i < owner.Parameters.Length; i++)
                {
                    var tp = owner.Parameters[i];
                    Image.GenericParams.Add(new GenericParamRow((ushort)i, MapGenericParamFlags(tp), owner.OwnerToken, Image.Strings.Add(tp.Name)));
                    int rid = Image.GenericParams.Count;
                    declared.Add((rid, tp));
                    if (ReferenceEquals(tp.ContainingSymbol, owner.Owner))
                        rids.TryAdd(tp, rid);
                }
            }
            foreach (var (rid, tp) in declared)
            {
                if ((tp.GenericConstraint & GenericConstraintsFlags.StructConstraint) != 0 &&
                    FindCoreType("System", "ValueType") is { } valueType)
                {
                    Image.GenericParamConstraints.Add(new GenericParamConstraintRow(rid, unchecked((int)SigEncoding.EncodeTypeDefOrRef(GetTypeToken(valueType)))));
                }
                var constraints = tp.ConstraintTypes;
                for (int i = 0; i < constraints.Length; i++)
                {
                    if (constraints[i].Kind == SymbolKind.Error)
                        continue;
                    int encoded = unchecked((int)SigEncoding.EncodeTypeDefOrRef(GetTypeToken(constraints[i])));
                    Image.GenericParamConstraints.Add(new GenericParamConstraintRow(rid, encoded));
                }
            }
            return rids;
        }
        private static ushort MapGenericParamFlags(TypeParameterSymbol tp)
        {
            var flags = System.Reflection.GenericParameterAttributes.None;
            var constraint = tp.GenericConstraint;
            if ((constraint & GenericConstraintsFlags.ClassConstraint) != 0)
                flags |= System.Reflection.GenericParameterAttributes.ReferenceTypeConstraint;
            if ((constraint & (GenericConstraintsFlags.StructConstraint | GenericConstraintsFlags.UnmanagedConstraint)) != 0)
                flags |= System.Reflection.GenericParameterAttributes.NotNullableValueTypeConstraint |
                    System.Reflection.GenericParameterAttributes.DefaultConstructorConstraint;
            if ((constraint & GenericConstraintsFlags.ConstructorConstraint) != 0)
                flags |= System.Reflection.GenericParameterAttributes.DefaultConstructorConstraint;
            if ((constraint & GenericConstraintsFlags.AllowsRefStruct) != 0)
                flags |= (System.Reflection.GenericParameterAttributes)0x0020;
            return (ushort)flags;
        }
        private static ImmutableArray<TypeParameterSymbol> GetTypeParametersInMetadataOrder(NamedTypeSymbol type)
        {
            if (type.ContainingSymbol is not NamedTypeSymbol containing)
                return type.TypeParameters;

            var enclosing = GetTypeParametersInMetadataOrder(containing);
            if (enclosing.IsDefaultOrEmpty)
                return type.TypeParameters;
            return enclosing.AddRange(type.TypeParameters);
        }
        private static int GetMetadataTypeParameterIndex(TypeParameterSymbol tp)
        {
            if (tp.ContainingSymbol is not NamedTypeSymbol declaring)
                return tp.Ordinal;

            int index = tp.Ordinal;
            for (var outer = declaring.ContainingSymbol as NamedTypeSymbol; outer is not null; outer = outer.ContainingSymbol as NamedTypeSymbol)
                index += outer.TypeParameters.Length;
            return index;
        }
        private NamedTypeSymbol? FindCoreType(string @namespace, string name)
        {
            Symbol? objectRoot = _systemObject;
            while (objectRoot is not null && objectRoot is not NamespaceSymbol { IsGlobalNamespace: true })
                objectRoot = objectRoot.ContainingSymbol;

            return FindType(_moduleGlobalNamespace, @namespace, name)
                ?? FindType(_metadataLookupGlobalNamespace, @namespace, name)
                ?? (objectRoot is NamespaceSymbol global ? FindType(global, @namespace, name) : null);
        }
        private static NamedTypeSymbol? FindType(NamespaceSymbol current, string @namespace, string name)
        {
            if (@namespace.Length != 0)
            {
                foreach (string part in @namespace.Split('.'))
                {
                    NamespaceSymbol? next = null;
                    var children = current.GetNamespaceMembers();
                    for (int i = 0; i < children.Length; i++)
                    {
                        if (StringComparer.Ordinal.Equals(children[i].Name, part))
                        {
                            next = children[i];
                            break;
                        }
                    }
                    if (next is null)
                        return null;
                    current = next;
                }
            }
            var types = current.GetTypeMembers(name, 0);
            return types.IsDefaultOrEmpty ? null : types[0];
        }
        private int GetWellKnownAttributeConstructorToken(string @namespace, string name)
        {
            var type = FindCoreType(@namespace, name)
                ?? throw new InvalidOperationException($"The core library does not define '{@namespace}.{name}'.");
            var members = type.GetMembers();
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is MethodSymbol { IsConstructor: true, IsStatic: false } ctor && ctor.Parameters.Length == 0)
                    return GetMethodToken(ctor);
            }
            throw new InvalidOperationException($"'{@namespace}.{name}' has no parameterless constructor.");
        }
        private void EmitCustomAttributes(ImmutableArray<NamedTypeSymbol> allTypes, Dictionary<TypeParameterSymbol, int> genericParamRids)
        {
            int extensionCtor = 0;
            int EmitExtensionAttribute(int parentToken)
            {
                if (extensionCtor == 0)
                    extensionCtor = GetWellKnownAttributeConstructorToken("System.Runtime.CompilerServices", "ExtensionAttribute");
                Image.CustomAttributes.Add(new CustomAttributeRow(parentToken, extensionCtor, Image.Blob.Add(EmptyCustomAttributeBlob)));
                return extensionCtor;
            }

            bool moduleHasExtensions = false;
            for (int i = 0; i < allTypes.Length; i++)
            {
                var t = allTypes[i];
                int typeToken = _typeDefTokens[t];
                EmitAttributes(typeToken, t.GetAttributes());
                if (t.IsRefLikeType)
                {
                    int ctor = GetWellKnownAttributeConstructorToken("System.Runtime.CompilerServices", "IsByRefLikeAttribute");
                    Image.CustomAttributes.Add(new CustomAttributeRow(typeToken, ctor, Image.Blob.Add(EmptyCustomAttributeBlob)));
                }
                EmitGenericParameterAttributes(t.TypeParameters, genericParamRids);

                bool typeHasExtensions = false;
                var members = t.GetMembers();
                for (int m = 0; m < members.Length; m++)
                {
                    switch (members[m])
                    {
                        case FieldSymbol f when _fieldDefTokens.TryGetValue(f, out var ftok):
                            EmitAttributes(ftok, f.GetAttributes());
                            EmitTupleElementNames(ftok, f.Type);
                            break;

                        case MethodSymbol mm when _methodDefTokens.TryGetValue(mm, out var mtok):
                            EmitMethodAttributes(mm, mtok);
                            EmitGenericParameterAttributes(mm.TypeParameters, genericParamRids);
                            if (mm.IsExtensionMethod)
                            {
                                EmitExtensionAttribute(mtok);
                                typeHasExtensions = true;
                            }
                            if (mm.ExtensionMember is { } extensionMember)
                                EmitExtensionMarker(mtok, extensionMember.GroupingTypeName);
                            break;

                        case PropertySymbol p when _propertyDefTokens.TryGetValue(p, out var ptok):
                            EmitAttributes(ptok, p.GetAttributes());
                            EmitTupleElementNames(ptok, p.Type);
                            break;
                    }
                }
                if (typeHasExtensions)
                {
                    EmitExtensionAttribute(typeToken);
                    moduleHasExtensions = true;
                }
            }
            foreach (var (method, token) in _methodDefTokens)
            {
                if (method.ContainingSymbol is not NamedTypeSymbol)
                    EmitMethodAttributes(method, token);
            }
            if (moduleHasExtensions)
                EmitExtensionAttribute(MetadataToken.Make(MetadataToken.Assembly, 1));
        }
        private void EmitMethodAttributes(MethodSymbol method, int methodToken)
        {
            var attributes = method.GetAttributes();
            for (int i = 0; i < attributes.Length; i++)
            {
                int parent = methodToken;
                if (attributes[i].Target == AttributeApplicationTarget.ReturnValue)
                    parent = _returnParamTokens[method];
                EmitAttribute(parent, attributes[i]);
            }

            if (_returnParamTokens.TryGetValue(method, out int returnToken))
                EmitTupleElementNames(returnToken, method.ReturnType);

            var ps = method.Parameters;
            for (int p = 0; p < ps.Length; p++)
            {
                if (!_paramDefTokens.TryGetValue(ps[p], out var ptok))
                    continue;
                EmitAttributes(ptok, ps[p].GetAttributes());
                EmitTupleElementNames(ptok, ps[p].Type);
                if (ps[p].IsParams)
                {
                    int ctor = ps[p].Type is ArrayTypeSymbol
                        ? GetWellKnownAttributeConstructorToken("System", "ParamArrayAttribute")
                        : GetWellKnownAttributeConstructorToken("System.Runtime.CompilerServices", "ParamCollectionAttribute");
                    Image.CustomAttributes.Add(new CustomAttributeRow(ptok, ctor, Image.Blob.Add(EmptyCustomAttributeBlob)));
                }
            }
        }
        private int _extensionMarkerConstructor;
        private void EmitExtensionMarker(int methodToken, string groupingTypeName)
        {
            if (_extensionMarkerConstructor == 0)
            {
                var attributeType = FindCoreType("System.Runtime.CompilerServices", "ExtensionMarkerAttribute")
                    ?? throw new InvalidOperationException("The core library does not define 'System.Runtime.CompilerServices.ExtensionMarkerAttribute'.");
                foreach (var member in attributeType.GetMembers())
                {
                    if (member is MethodSymbol { IsConstructor: true, IsStatic: false } ctor && ctor.Parameters.Length == 1)
                        _extensionMarkerConstructor = GetMethodToken(ctor);
                }
            }

            var blob = new MetadataBuffer();
            blob.WriteUInt16(CustomAttributeBlob.Prolog);
            CustomAttributeBlob.WriteSerString(blob, groupingTypeName);
            blob.WriteUInt16(0);
            Image.CustomAttributes.Add(new CustomAttributeRow(methodToken, _extensionMarkerConstructor, Image.Blob.Add(blob.Span)));
        }
        private int _tupleElementNamesConstructor = -1;
        private void EmitTupleElementNames(int parentToken, TypeSymbol type)
        {
            if (GetTupleElementNames(type) is not { } names)
                return;
            if (_tupleElementNamesConstructor < 0)
            {
                _tupleElementNamesConstructor = 0;
                var attributeType = FindCoreType("System.Runtime.CompilerServices", "TupleElementNamesAttribute");
                foreach (var member in attributeType?.GetMembers() ?? ImmutableArray<Symbol>.Empty)
                {
                    if (member is MethodSymbol { IsConstructor: true, IsStatic: false } ctor && ctor.Parameters.Length == 1)
                        _tupleElementNamesConstructor = GetMethodToken(ctor);
                }
            }
            if (_tupleElementNamesConstructor == 0)
                return;

            var blob = new MetadataBuffer();
            blob.WriteUInt16(1);
            blob.WriteUInt32((uint)names.Length);
            foreach (var name in names)
                CustomAttributeBlob.WriteSerString(blob, name);
            blob.WriteUInt16(0);
            Image.CustomAttributes.Add(new CustomAttributeRow(parentToken, _tupleElementNamesConstructor, Image.Blob.Add(blob.Span)));
        }
        // Element names of every tuple in the type, depth-first pre-order as TupleElementNamesAttribute stores them.
        internal static string?[]? GetTupleElementNames(TypeSymbol type)
        {
            var names = new List<string?>();
            if (!CollectTupleElementNames(type, names) || !names.Exists(n => n is not null))
                return null;
            return names.ToArray();

            static bool CollectTupleElementNames(TypeSymbol type, List<string?> names)
            {
                switch (type)
                {
                    case ArrayTypeSymbol array:
                        return CollectTupleElementNames(array.ElementType, names);
                    case ByRefTypeSymbol byRef:
                        return CollectTupleElementNames(byRef.ElementType, names);
                    case PointerTypeSymbol pointer:
                        return CollectTupleElementNames(pointer.PointedAtType, names);
                    case TupleTypeSymbol tuple:
                        if (tuple.Cardinality > 7)
                            return false;
                        for (int i = 0; i < tuple.Cardinality; i++)
                            names.Add(tuple.ElementNames.IsDefaultOrEmpty ? null : tuple.ElementNames[i]);
                        foreach (var element in tuple.ElementTypes)
                        {
                            if (!CollectTupleElementNames(element, names))
                                return false;
                        }
                        return true;
                    case NamedTypeSymbol named when named.ContainingSymbol is not NamedTypeSymbol:
                        if (IsValueTupleType(named))
                        {
                            if (named.TypeArguments.Length > 7)
                                return false;
                            for (int i = 0; i < named.TypeArguments.Length; i++)
                                names.Add(null);
                        }
                        foreach (var argument in named.TypeArguments)
                        {
                            if (!CollectTupleElementNames(argument, names))
                                return false;
                        }
                        return true;
                    default:
                        return true;
                }
            }
        }
        internal static bool IsValueTupleType(NamedTypeSymbol type)
        {
            var definition = type.OriginalDefinition;
            return definition.Name == "ValueTuple" && definition.Arity > 0 &&
                   definition.ContainingSymbol is NamespaceSymbol { Name: "System" } system &&
                   system.ContainingSymbol is NamespaceSymbol { IsGlobalNamespace: true };
        }
        private void EmitGenericParameterAttributes(ImmutableArray<TypeParameterSymbol> parameters, Dictionary<TypeParameterSymbol, int> genericParamRids)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                if (!genericParamRids.TryGetValue(parameters[i], out int rid))
                    continue;
                int token = MetadataToken.Make(MetadataToken.GenericParam, rid);
                EmitAttributes(token, parameters[i].GetAttributes());
                if ((parameters[i].GenericConstraint & GenericConstraintsFlags.UnmanagedConstraint) != 0)
                {
                    int ctor = GetWellKnownAttributeConstructorToken("System.Runtime.CompilerServices", "IsUnmanagedAttribute");
                    Image.CustomAttributes.Add(new CustomAttributeRow(token, ctor, Image.Blob.Add(EmptyCustomAttributeBlob)));
                }
            }
        }
        private void EmitAttributes(int parentToken, ImmutableArray<AttributeData> attrs)
        {
            for (int i = 0; i < attrs.Length; i++)
                EmitAttribute(parentToken, attrs[i]);
        }
        private void EmitAttribute(int parentToken, AttributeData attribute)
        {
            int ctorToken = GetMethodToken(attribute.Constructor);
            Image.CustomAttributes.Add(new CustomAttributeRow(parentToken, ctorToken, BuildCustomAttributeBlob(attribute)));
        }
        private static ReadOnlySpan<byte> EmptyCustomAttributeBlob => new byte[] { 0x01, 0x00, 0x00, 0x00 };
        private int BuildCustomAttributeBlob(AttributeData attr)
        {
            var w = new MetadataBuffer(64);
            w.WriteUInt16(CustomAttributeBlob.Prolog);

            var ctorParams = attr.Constructor.Parameters;
            if (ctorParams.Length != attr.ConstructorArguments.Length)
                throw new InvalidOperationException($"Attribute '{attr.AttributeClass.Name}' has {attr.ConstructorArguments.Length} arguments for {ctorParams.Length} constructor parameters.");
            for (int i = 0; i < ctorParams.Length; i++)
                WriteCustomAttributeValue(w, ctorParams[i].Type, attr.ConstructorArguments[i]);

            w.WriteUInt16(checked((ushort)attr.NamedArguments.Length));
            for (int i = 0; i < attr.NamedArguments.Length; i++)
            {
                var na = attr.NamedArguments[i];
                TypeSymbol memberType = na.Member switch
                {
                    PropertySymbol property => property.Type,
                    FieldSymbol field => field.Type,
                    _ => na.Value.Type,
                };
                w.WriteByte(na.Member is PropertySymbol ? CustomAttributeBlob.NamedProperty : CustomAttributeBlob.NamedField);
                WriteFieldOrPropType(w, memberType);
                CustomAttributeBlob.WriteSerString(w, na.Name);
                WriteCustomAttributeValue(w, memberType, na.Value);
            }

            return Image.Blob.Add(w.Span);
        }
        private void WriteCustomAttributeValue(MetadataBuffer w, TypeSymbol type, TypedConstant value)
        {
            if (type is ArrayTypeSymbol array)
            {
                if (value.Value is null)
                {
                    w.WriteUInt32(uint.MaxValue);
                    return;
                }
                var elements = (ImmutableArray<TypedConstant>)value.Value;
                w.WriteUInt32((uint)elements.Length);
                for (int i = 0; i < elements.Length; i++)
                    WriteCustomAttributeValue(w, array.ElementType, elements[i]);
                return;
            }
            if (type.SpecialType == SpecialType.System_Object)
            {
                TypeSymbol actual = value.Value is null ? FindCoreType("System", "String") ?? value.Type : value.Type;
                WriteFieldOrPropType(w, actual);
                WriteCustomAttributeValue(w, actual, value);
                return;
            }
            if (IsSystemType(type))
            {
                CustomAttributeBlob.WriteSerString(w, value.Value is TypeSymbol t ? FormatSerializedTypeName(t) : null);
                return;
            }
            if (type is NamedTypeSymbol { TypeKind: TypeKind.Enum } enumType && enumType.EnumUnderlyingType is { } underlying)
                type = underlying;

            object? v = value.Value;
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean: w.WriteByte(Convert.ToBoolean(v) ? (byte)1 : (byte)0); return;
                case SpecialType.System_Char: w.WriteUInt16(Convert.ToChar(v)); return;
                case SpecialType.System_Int8: w.WriteByte(unchecked((byte)Convert.ToSByte(v))); return;
                case SpecialType.System_UInt8: w.WriteByte(Convert.ToByte(v)); return;
                case SpecialType.System_Int16: w.WriteUInt16(unchecked((ushort)Convert.ToInt16(v))); return;
                case SpecialType.System_UInt16: w.WriteUInt16(Convert.ToUInt16(v)); return;
                case SpecialType.System_Int32: w.WriteInt32(Convert.ToInt32(v)); return;
                case SpecialType.System_UInt32: w.WriteUInt32(Convert.ToUInt32(v)); return;
                case SpecialType.System_Int64: w.WriteUInt64(unchecked((ulong)Convert.ToInt64(v))); return;
                case SpecialType.System_UInt64: w.WriteUInt64(Convert.ToUInt64(v)); return;
                case SpecialType.System_Single: w.WriteUInt32(BitConverter.SingleToUInt32Bits(Convert.ToSingle(v))); return;
                case SpecialType.System_Double: w.WriteUInt64(BitConverter.DoubleToUInt64Bits(Convert.ToDouble(v))); return;
                case SpecialType.System_String: CustomAttributeBlob.WriteSerString(w, (string?)v); return;
                default:
                    throw new NotSupportedException($"Attribute argument type '{type.Name}' cannot be encoded.");
            }
        }
        private void WriteFieldOrPropType(MetadataBuffer w, TypeSymbol type)
        {
            if (type is ArrayTypeSymbol { IsSZArray: true } array)
            {
                w.WriteByte((byte)SigElementType.SZARRAY);
                WriteFieldOrPropType(w, array.ElementType);
                return;
            }
            if (IsSystemType(type))
            {
                w.WriteByte(CustomAttributeBlob.TypeTag);
                return;
            }
            if (type is NamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
            {
                w.WriteByte(CustomAttributeBlob.EnumTag);
                CustomAttributeBlob.WriteSerString(w, FormatSerializedTypeName(enumType));
                return;
            }
            SigElementType element = type.SpecialType switch
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
                SpecialType.System_String => SigElementType.STRING,
                SpecialType.System_Object => (SigElementType)CustomAttributeBlob.BoxedTag,
                _ => throw new NotSupportedException($"Attribute member type '{type.Name}' cannot be encoded."),
            };
            w.WriteByte((byte)element);
        }
        private static bool IsSystemType(TypeSymbol type)
            => type is NamedTypeSymbol { Name: "Type", Arity: 0 } named &&
               named.ContainingSymbol is NamespaceSymbol { Name: "System" } ns &&
               ns.ContainingSymbol is NamespaceSymbol { IsGlobalNamespace: true };
        private string FormatSerializedTypeName(TypeSymbol type)
        {
            var sb = new StringBuilder();
            AppendSerializedTypeName(sb, type);
            if (type is NamedTypeSymbol named && !_typeDefTokens.ContainsKey(named.OriginalDefinition))
            {
                string? assembly = _externalAssemblyResolver?.Invoke(named.OriginalDefinition);
                sb.Append(", ").Append(string.IsNullOrEmpty(assembly) ? _defaultExternalAssemblyName : assembly);
            }
            return sb.ToString();
        }
        private void AppendSerializedTypeName(StringBuilder sb, TypeSymbol type)
        {
            switch (type)
            {
                case ArrayTypeSymbol array:
                    AppendSerializedTypeName(sb, array.ElementType);
                    sb.Append(array.IsSZArray ? "[]" : "[" + new string(',', array.Rank - 1) + "]");
                    return;
                case NamedTypeSymbol named:
                    var definition = named.OriginalDefinition;
                    if (definition.ContainingSymbol is NamedTypeSymbol containing)
                    {
                        AppendSerializedTypeName(sb, containing.OriginalDefinition);
                        sb.Append('+');
                    }
                    else
                    {
                        string ns = GetNamespaceString(definition);
                        if (ns.Length != 0)
                            sb.Append(ns).Append('.');
                    }
                    sb.Append(GetMetadataTypeName(definition));
                    if (!ReferenceEquals(named, definition) && !named.TypeArguments.IsDefaultOrEmpty)
                    {
                        sb.Append('[');
                        var arguments = named.TypeArguments;
                        for (int i = 0; i < arguments.Length; i++)
                        {
                            if (i != 0)
                                sb.Append(',');
                            sb.Append('[').Append(FormatSerializedTypeName(arguments[i])).Append(']');
                        }
                        sb.Append(']');
                    }
                    return;
                default:
                    throw new NotSupportedException($"Type '{type.Name}' cannot appear in an attribute argument.");
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
    internal enum ArrayMethodKind : byte
    {
        Constructor,
        Get,
        Set,
        Address,
    }
    internal enum WellKnownMethod : byte
    {
        DelegateCombine,
        DelegateRemove,
        TypeGetTypeFromHandle,
        TypeIsValueType,
        TypeIsPrimitive,
        TypeIsEnum,
        ObjectGetType,
        ArrayDataReference,
    }
    internal interface ITokenProvider
    {
        int GetTypeToken(TypeSymbol type);
        int GetMethodToken(MethodSymbol method);
        int GetFieldToken(FieldSymbol field);
        int GetPropertyToken(PropertySymbol property);
        int GetUserStringToken(string value);
        int GetMethodDefinitionToken(MethodSymbol method);
        int GetGenericMethodInstanceToken(MethodSymbol definition, ImmutableArray<TypeSymbol> typeArguments);
        int GetLocalSignatureToken(IReadOnlyList<TypeSymbol> localTypes);
        int GetCalliSignatureToken(FunctionPointerTypeSymbol functionPointer);
        int GetStaticDataFieldToken(byte[] data);
        int GetArrayMethodToken(ArrayTypeSymbol arrayType, ArrayMethodKind kind);
        int GetWellKnownMethodToken(WellKnownMethod method);
    }
}
