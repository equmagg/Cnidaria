using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Cnidaria.Wasm;

public sealed class WasmProgram
{
    public WasmTarget Target { get; }

    public ImmutableArray<WasmRecursionGroup> Types { get; }
    public WasmCodeSection Code { get; }
    public ImmutableArray<WasmDataSection> DataSections { get; }
    public ImmutableArray<WasmProgramGlobal> Globals { get; }
    public ImmutableArray<WasmProgramTag> Tags { get; }
    public ImmutableArray<WasmProgramImport> Imports { get; }
    public ImmutableArray<WasmObjectSymbol> Symbols { get; }
    public ImmutableArray<WasmProgramExport> Exports { get; }
    public string EntrySymbol { get; }

    public WasmProgram(
        WasmTarget target,
        ImmutableArray<WasmRecursionGroup> types,
        WasmCodeSection code,
        ImmutableArray<WasmDataSection> dataSections,
        ImmutableArray<WasmProgramGlobal> globals,
        ImmutableArray<WasmProgramTag> tags,
        ImmutableArray<WasmProgramImport> imports,
        ImmutableArray<WasmObjectSymbol> symbols,
        ImmutableArray<WasmProgramExport> exports,
        string entrySymbol)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Types = types.IsDefault ? ImmutableArray<WasmRecursionGroup>.Empty : types;
        Code = code ?? throw new ArgumentNullException(nameof(code));
        DataSections = dataSections.IsDefault ? ImmutableArray<WasmDataSection>.Empty : dataSections;
        Globals = globals.IsDefault ? ImmutableArray<WasmProgramGlobal>.Empty : globals;
        Tags = tags.IsDefault ? ImmutableArray<WasmProgramTag>.Empty : tags;
        Imports = imports.IsDefault ? ImmutableArray<WasmProgramImport>.Empty : imports;
        Symbols = symbols.IsDefault ? ImmutableArray<WasmObjectSymbol>.Empty : symbols;
        Exports = exports.IsDefault ? ImmutableArray<WasmProgramExport>.Empty : exports;
        EntrySymbol = entrySymbol ?? string.Empty;
    }

    public WasmProgram(WasmTarget target, IEnumerable<WasmFunctionBody> functions, string entrySymbol = "")
        : this(
            target,
            ImmutableArray<WasmRecursionGroup>.Empty,
            new WasmCodeSection(functions),
            ImmutableArray<WasmDataSection>.Empty,
            ImmutableArray<WasmProgramGlobal>.Empty,
            ImmutableArray<WasmProgramTag>.Empty,
            ImmutableArray<WasmProgramImport>.Empty,
            ImmutableArray<WasmObjectSymbol>.Empty,
            ImmutableArray<WasmProgramExport>.Empty,
            entrySymbol)
    {
    }

    public int TypeCount => Types.Sum(static group => group.Types.Length);

    public string FormatText(WasmAssemblyWriterOptions? options = null)
        => WasmDisassembler.Disassemble(this, options);

    public WasmProgram Link(params WasmProgram[] objects)
        => WasmObjectComposer.Compose(this, objects);

    public WasmModule LinkModule(WasmLinkOptions? options = null)
        => WasmObjectLinker.Link(this, options ?? new WasmLinkOptions());

    public byte[] ToModuleBytes(WasmLinkOptions? options = null)
        => LinkModule(options).ToBytes();
}

public sealed class WasmCodeSection
{
    public ImmutableArray<WasmFunctionBody> Functions { get; }

    public WasmCodeSection(IEnumerable<WasmFunctionBody> functions)
    {
        Functions = functions?.ToImmutableArray() ?? ImmutableArray<WasmFunctionBody>.Empty;
    }

    public string Format(WasmAssemblyWriterOptions? options = null)
        => WasmDisassembler.Disassemble(this, options);
}

public sealed class WasmFunctionBody
{
    public string Name { get; }
    public WasmFunctionType Signature { get; }
    public ImmutableArray<WasmValueType> Locals { get; }

    /// <summary>The body without the end that closes it</summary>
    public ImmutableArray<WasmInstruction> Instructions { get; }

    public ImmutableArray<string?> LocalNames { get; }

    public WasmFunctionBody(
        string name,
        WasmFunctionType signature,
        IEnumerable<WasmValueType>? locals,
        IEnumerable<WasmInstruction> instructions,
        IEnumerable<string?>? localNames = null)
    {
        Name = name ?? string.Empty;
        Signature = signature ?? throw new ArgumentNullException(nameof(signature));
        Locals = locals?.ToImmutableArray() ?? ImmutableArray<WasmValueType>.Empty;
        Instructions = instructions?.ToImmutableArray() ?? ImmutableArray<WasmInstruction>.Empty;
        LocalNames = localNames?.ToImmutableArray() ?? ImmutableArray<string?>.Empty;
    }
}

public sealed class WasmDataSection
{
    public string Name { get; }
    public WasmObjectSectionKind Kind { get; }
    public int Alignment { get; }
    public ImmutableArray<byte> Data { get; }
    public int BssSize { get; }
    public ImmutableArray<WasmObjectRelocation> Relocations { get; }

    public WasmDataSection(
        string name,
        WasmObjectSectionKind kind,
        int alignment,
        ImmutableArray<byte> data,
        int bssSize,
        ImmutableArray<WasmObjectRelocation> relocations)
    {
        Name = string.IsNullOrWhiteSpace(name) ? ".data" : name;
        Kind = kind;
        Alignment = Math.Max(1, alignment);
        Data = data.IsDefault ? ImmutableArray<byte>.Empty : data;
        BssSize = Math.Max(0, bssSize);
        Relocations = relocations.IsDefault ? ImmutableArray<WasmObjectRelocation>.Empty : relocations;
    }

    public int Size => Kind == WasmObjectSectionKind.Bss ? BssSize : Data.Length;
}

public sealed class WasmProgramGlobal
{
    public string Name { get; }
    public WasmGlobalType Type { get; }

    public ImmutableArray<WasmInstruction> Initializer { get; }

    public WasmProgramGlobal(string name, WasmGlobalType type, IEnumerable<WasmInstruction> initializer)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type;
        Initializer = initializer?.ToImmutableArray() ?? ImmutableArray<WasmInstruction>.Empty;
    }
}

public sealed class WasmProgramTag
{
    public string Name { get; }
    public WasmFunctionType Signature { get; }

    public WasmProgramTag(string name, WasmFunctionType signature)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Signature = signature ?? throw new ArgumentNullException(nameof(signature));
    }
}

public sealed class WasmProgramImport
{
    public string SymbolName { get; }
    public string Module { get; }
    public string Field { get; }
    public WasmExternalKind Kind { get; }
    public WasmFunctionType? Signature { get; }
    public WasmGlobalType Global { get; }

    private WasmProgramImport(string symbolName, string module, string field, WasmExternalKind kind, WasmFunctionType? signature, WasmGlobalType global)
    {
        SymbolName = symbolName ?? throw new ArgumentNullException(nameof(symbolName));
        Module = module ?? throw new ArgumentNullException(nameof(module));
        Field = field ?? throw new ArgumentNullException(nameof(field));
        Kind = kind;
        Signature = signature;
        Global = global;
    }

    public static WasmProgramImport Function(string symbolName, WasmFunctionType signature, string module = "env", string? field = null)
        => new WasmProgramImport(symbolName, module, field ?? symbolName, WasmExternalKind.Function, signature ?? throw new ArgumentNullException(nameof(signature)), default);

    public static WasmProgramImport OfGlobal(string symbolName, WasmGlobalType type, string module = "env", string? field = null)
        => new WasmProgramImport(symbolName, module, field ?? symbolName, WasmExternalKind.Global, null, type);

    public static WasmProgramImport OfTag(string symbolName, WasmFunctionType signature, string module = "env", string? field = null)
        => new WasmProgramImport(symbolName, module, field ?? symbolName, WasmExternalKind.Tag, signature ?? throw new ArgumentNullException(nameof(signature)), default);
}

public sealed class WasmProgramExport
{
    public string Name { get; }
    public string SymbolName { get; }

    public WasmProgramExport(string name, string symbolName)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        SymbolName = symbolName ?? throw new ArgumentNullException(nameof(symbolName));
    }
}

public sealed class WasmObjectSymbol
{
    public string Name { get; }
    public string SectionName { get; }

    public int Offset { get; }
    public int Size { get; }
    public WasmObjectSymbolBinding Binding { get; }
    public WasmObjectSymbolKind Kind { get; }
    public bool IsTentative { get; }

    public WasmObjectSymbol(
        string name,
        string sectionName,
        int offset,
        int size,
        WasmObjectSymbolBinding binding,
        WasmObjectSymbolKind kind,
        bool isTentative = false)
    {
        Name = name ?? string.Empty;
        SectionName = sectionName ?? string.Empty;
        Offset = Math.Max(0, offset);
        Size = Math.Max(0, size);
        Binding = binding;
        Kind = kind;
        IsTentative = isTentative;
    }
}

public sealed class WasmObjectRelocation
{
    public string SectionName { get; }
    public int Offset { get; }
    public string SymbolName { get; }
    public long Addend { get; }
    public WasmObjectRelocationKind Kind { get; }

    public WasmObjectRelocation(string sectionName, int offset, string symbolName, long addend, WasmObjectRelocationKind kind)
    {
        SectionName = sectionName ?? string.Empty;
        Offset = Math.Max(0, offset);
        SymbolName = symbolName ?? string.Empty;
        Addend = addend;
        Kind = kind;
    }
}

public static class WasmObjectSections
{
    public const string Code = ".code";
    public const string Globals = ".globals";
    public const string Tags = ".tags";
}

public enum WasmObjectSectionKind : byte
{
    Code,
    Rodata,
    Data,
    Bss,
}

public enum WasmObjectSymbolBinding : byte
{
    Local,
    Global,
    External,
}

public enum WasmObjectSymbolKind : byte
{
    None,
    Function,
    Object,
    Global,
    Tag,
    Section,
}

/// <summary>What a linker writes into data: a symbol's address in memory, or a function's slot in the table</summary>
public enum WasmObjectRelocationKind : byte
{
    None,
    MemoryAddress32,
    MemoryAddress64,
    TableIndex32,
    TableIndex64,
}

[Flags]
public enum WasmIsaFlags : uint
{
    None = 0,
    MutableGlobals = 1u << 0,
    SignExtension = 1u << 1,
    SaturatingFloatToInt = 1u << 2,
    MultiValue = 1u << 3,
    BulkMemory = 1u << 4,
    ReferenceTypes = 1u << 5,
    Simd128 = 1u << 6,
    RelaxedSimd = 1u << 7,
    TailCall = 1u << 8,
    ExtendedConst = 1u << 9,
    TypedFunctionReferences = 1u << 10,
    GarbageCollection = 1u << 11,
    ExceptionHandling = 1u << 12,
    MultiMemory = 1u << 13,
    Memory64 = 1u << 14,

    Wasm2 = MutableGlobals | SignExtension | SaturatingFloatToInt | MultiValue | BulkMemory | ReferenceTypes | Simd128,
    Wasm3 = Wasm2 | RelaxedSimd | TailCall | ExtendedConst | TypedFunctionReferences | GarbageCollection | ExceptionHandling | MultiMemory | Memory64,
}

public enum WasmHeapTypeKind : byte
{
    Concrete,
    Func,
    NoFunc,
    Extern,
    NoExtern,
    Any,
    Eq,
    I31,
    Struct,
    Array,
    None,
    Exn,
    NoExn,
}

public readonly struct WasmHeapType : IEquatable<WasmHeapType>
{
    public WasmHeapTypeKind Kind { get; }
    public uint TypeIndex { get; }

    private WasmHeapType(WasmHeapTypeKind kind, uint typeIndex)
    {
        Kind = kind;
        TypeIndex = typeIndex;
    }

    public static WasmHeapType Func => new WasmHeapType(WasmHeapTypeKind.Func, 0);
    public static WasmHeapType NoFunc => new WasmHeapType(WasmHeapTypeKind.NoFunc, 0);
    public static WasmHeapType Extern => new WasmHeapType(WasmHeapTypeKind.Extern, 0);
    public static WasmHeapType NoExtern => new WasmHeapType(WasmHeapTypeKind.NoExtern, 0);
    public static WasmHeapType Any => new WasmHeapType(WasmHeapTypeKind.Any, 0);
    public static WasmHeapType Eq => new WasmHeapType(WasmHeapTypeKind.Eq, 0);
    public static WasmHeapType I31 => new WasmHeapType(WasmHeapTypeKind.I31, 0);
    public static WasmHeapType Struct => new WasmHeapType(WasmHeapTypeKind.Struct, 0);
    public static WasmHeapType Array => new WasmHeapType(WasmHeapTypeKind.Array, 0);
    public static WasmHeapType None => new WasmHeapType(WasmHeapTypeKind.None, 0);
    public static WasmHeapType Exn => new WasmHeapType(WasmHeapTypeKind.Exn, 0);
    public static WasmHeapType NoExn => new WasmHeapType(WasmHeapTypeKind.NoExn, 0);

    public static WasmHeapType Concrete(uint typeIndex) => new WasmHeapType(WasmHeapTypeKind.Concrete, typeIndex);

    public static WasmHeapType Abstract(WasmHeapTypeKind kind)
        => kind == WasmHeapTypeKind.Concrete ? throw new ArgumentException("A concrete heap type names its type", nameof(kind)) : new WasmHeapType(kind, 0);

    public bool IsConcrete => Kind == WasmHeapTypeKind.Concrete;

    public byte Code => Kind switch
    {
        WasmHeapTypeKind.Func => 0x70,
        WasmHeapTypeKind.Extern => 0x6F,
        WasmHeapTypeKind.Any => 0x6E,
        WasmHeapTypeKind.Eq => 0x6D,
        WasmHeapTypeKind.I31 => 0x6C,
        WasmHeapTypeKind.Struct => 0x6B,
        WasmHeapTypeKind.Array => 0x6A,
        WasmHeapTypeKind.Exn => 0x69,
        WasmHeapTypeKind.None => 0x71,
        WasmHeapTypeKind.NoExtern => 0x72,
        WasmHeapTypeKind.NoFunc => 0x73,
        WasmHeapTypeKind.NoExn => 0x74,
        _ => throw new InvalidOperationException("A concrete heap type is written as its index"),
    };

    public static bool TryFromCode(byte code, out WasmHeapType heapType)
    {
        WasmHeapTypeKind? kind = code switch
        {
            0x70 => WasmHeapTypeKind.Func,
            0x6F => WasmHeapTypeKind.Extern,
            0x6E => WasmHeapTypeKind.Any,
            0x6D => WasmHeapTypeKind.Eq,
            0x6C => WasmHeapTypeKind.I31,
            0x6B => WasmHeapTypeKind.Struct,
            0x6A => WasmHeapTypeKind.Array,
            0x69 => WasmHeapTypeKind.Exn,
            0x71 => WasmHeapTypeKind.None,
            0x72 => WasmHeapTypeKind.NoExtern,
            0x73 => WasmHeapTypeKind.NoFunc,
            0x74 => WasmHeapTypeKind.NoExn,
            _ => null,
        };
        heapType = kind is { } value ? new WasmHeapType(value, 0) : default;
        return kind.HasValue;
    }

    public string Name => Kind switch
    {
        WasmHeapTypeKind.Func => "func",
        WasmHeapTypeKind.NoFunc => "nofunc",
        WasmHeapTypeKind.Extern => "extern",
        WasmHeapTypeKind.NoExtern => "noextern",
        WasmHeapTypeKind.Any => "any",
        WasmHeapTypeKind.Eq => "eq",
        WasmHeapTypeKind.I31 => "i31",
        WasmHeapTypeKind.Struct => "struct",
        WasmHeapTypeKind.Array => "array",
        WasmHeapTypeKind.None => "none",
        WasmHeapTypeKind.Exn => "exn",
        WasmHeapTypeKind.NoExn => "noexn",
        _ => TypeIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    public static bool TryParse(string text, out WasmHeapType heapType)
    {
        heapType = default;
        foreach (var kind in Enum.GetValues<WasmHeapTypeKind>())
        {
            if (kind != WasmHeapTypeKind.Concrete && new WasmHeapType(kind, 0).Name == text)
            {
                heapType = new WasmHeapType(kind, 0);
                return true;
            }
        }
        return false;
    }

    public WasmHeapType RemapTypes(Func<uint, uint> map)
        => IsConcrete ? Concrete(map(TypeIndex)) : this;

    public bool Equals(WasmHeapType other) => Kind == other.Kind && TypeIndex == other.TypeIndex;
    public override bool Equals(object? obj) => obj is WasmHeapType other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Kind, TypeIndex);
    public static bool operator ==(WasmHeapType left, WasmHeapType right) => left.Equals(right);
    public static bool operator !=(WasmHeapType left, WasmHeapType right) => !left.Equals(right);
    public override string ToString() => Name;
}

public enum WasmValueKind : byte
{
    I32,
    I64,
    F32,
    F64,
    V128,
    Reference,
}

public readonly struct WasmValueType : IEquatable<WasmValueType>
{
    public WasmValueKind Kind { get; }
    public bool Nullable { get; }
    public WasmHeapType HeapType { get; }

    private WasmValueType(WasmValueKind kind, bool nullable, WasmHeapType heapType)
    {
        Kind = kind;
        Nullable = nullable;
        HeapType = heapType;
    }

    public static WasmValueType I32 => new WasmValueType(WasmValueKind.I32, false, default);
    public static WasmValueType I64 => new WasmValueType(WasmValueKind.I64, false, default);
    public static WasmValueType F32 => new WasmValueType(WasmValueKind.F32, false, default);
    public static WasmValueType F64 => new WasmValueType(WasmValueKind.F64, false, default);
    public static WasmValueType V128 => new WasmValueType(WasmValueKind.V128, false, default);
    public static WasmValueType FuncRef => Reference(WasmHeapType.Func);
    public static WasmValueType ExternRef => Reference(WasmHeapType.Extern);
    public static WasmValueType AnyRef => Reference(WasmHeapType.Any);
    public static WasmValueType EqRef => Reference(WasmHeapType.Eq);
    public static WasmValueType I31Ref => Reference(WasmHeapType.I31);
    public static WasmValueType StructRef => Reference(WasmHeapType.Struct);
    public static WasmValueType ArrayRef => Reference(WasmHeapType.Array);
    public static WasmValueType NullRef => Reference(WasmHeapType.None);
    public static WasmValueType NullFuncRef => Reference(WasmHeapType.NoFunc);
    public static WasmValueType NullExternRef => Reference(WasmHeapType.NoExtern);
    public static WasmValueType ExnRef => Reference(WasmHeapType.Exn);
    public static WasmValueType NullExnRef => Reference(WasmHeapType.NoExn);

    public static WasmValueType Reference(WasmHeapType heapType, bool nullable = true)
        => new WasmValueType(WasmValueKind.Reference, nullable, heapType);

    public static WasmValueType Reference(uint typeIndex, bool nullable = true)
        => Reference(WasmHeapType.Concrete(typeIndex), nullable);

    public bool IsReference => Kind == WasmValueKind.Reference;
    public bool IsNumeric => Kind is WasmValueKind.I32 or WasmValueKind.I64 or WasmValueKind.F32 or WasmValueKind.F64;
    public bool IsDefaultable => Kind != WasmValueKind.Reference || Nullable;
    public int Size => Kind switch
    {
        WasmValueKind.I32 or WasmValueKind.F32 => 4,
        WasmValueKind.I64 or WasmValueKind.F64 => 8,
        WasmValueKind.V128 => 16,
        _ => 0,
    };

    public WasmValueType AsNullable(bool nullable)
        => IsReference ? new WasmValueType(Kind, nullable, HeapType) : this;

    public WasmValueType RemapTypes(Func<uint, uint> map)
        => IsReference ? new WasmValueType(Kind, Nullable, HeapType.RemapTypes(map)) : this;

    public string Name => Kind switch
    {
        WasmValueKind.I32 => "i32",
        WasmValueKind.I64 => "i64",
        WasmValueKind.F32 => "f32",
        WasmValueKind.F64 => "f64",
        WasmValueKind.V128 => "v128",
        _ => Nullable && !HeapType.IsConcrete ? Shorthand(HeapType.Kind) : $"(ref {(Nullable ? "null " : "")}{HeapType.Name})",
    };

    private static string Shorthand(WasmHeapTypeKind kind)
        => kind switch
        {
            WasmHeapTypeKind.Func => "funcref",
            WasmHeapTypeKind.Extern => "externref",
            WasmHeapTypeKind.Any => "anyref",
            WasmHeapTypeKind.Eq => "eqref",
            WasmHeapTypeKind.I31 => "i31ref",
            WasmHeapTypeKind.Struct => "structref",
            WasmHeapTypeKind.Array => "arrayref",
            WasmHeapTypeKind.None => "nullref",
            WasmHeapTypeKind.NoFunc => "nullfuncref",
            WasmHeapTypeKind.NoExtern => "nullexternref",
            WasmHeapTypeKind.Exn => "exnref",
            _ => "nullexnref",
        };

    public static bool TryParse(string text, out WasmValueType type)
    {
        switch (text)
        {
            case "i32": type = I32; return true;
            case "i64": type = I64; return true;
            case "f32": type = F32; return true;
            case "f64": type = F64; return true;
            case "v128": type = V128; return true;
        }
        foreach (var kind in Enum.GetValues<WasmHeapTypeKind>())
        {
            if (kind != WasmHeapTypeKind.Concrete && Shorthand(kind) == text)
            {
                type = Reference(WasmHeapType.Abstract(kind));
                return true;
            }
        }
        type = default;
        return false;
    }

    public bool Equals(WasmValueType other) => Kind == other.Kind && Nullable == other.Nullable && HeapType == other.HeapType;
    public override bool Equals(object? obj) => obj is WasmValueType other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Kind, Nullable, HeapType);
    public static bool operator ==(WasmValueType left, WasmValueType right) => left.Equals(right);
    public static bool operator !=(WasmValueType left, WasmValueType right) => !left.Equals(right);
    public override string ToString() => Name;
}

public enum WasmPackedType : byte
{
    None,
    I8,
    I16,
}

public readonly struct WasmStorageType : IEquatable<WasmStorageType>
{
    public WasmValueType ValueType { get; }
    public WasmPackedType Packed { get; }

    public WasmStorageType(WasmValueType valueType)
    {
        ValueType = valueType;
        Packed = WasmPackedType.None;
    }

    private WasmStorageType(WasmPackedType packed)
    {
        ValueType = WasmValueType.I32;
        Packed = packed;
    }

    public static WasmStorageType I8 => new WasmStorageType(WasmPackedType.I8);
    public static WasmStorageType I16 => new WasmStorageType(WasmPackedType.I16);

    public bool IsPacked => Packed != WasmPackedType.None;

    public WasmValueType Unpacked => IsPacked ? WasmValueType.I32 : ValueType;

    public int Size => Packed switch { WasmPackedType.I8 => 1, WasmPackedType.I16 => 2, _ => ValueType.Size };

    public string Name => Packed switch { WasmPackedType.I8 => "i8", WasmPackedType.I16 => "i16", _ => ValueType.Name };

    public WasmStorageType RemapTypes(Func<uint, uint> map)
        => IsPacked ? this : new WasmStorageType(ValueType.RemapTypes(map));

    public bool Equals(WasmStorageType other) => Packed == other.Packed && ValueType == other.ValueType;
    public override bool Equals(object? obj) => obj is WasmStorageType other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ValueType, Packed);
    public static implicit operator WasmStorageType(WasmValueType type) => new WasmStorageType(type);
    public static bool operator ==(WasmStorageType left, WasmStorageType right) => left.Equals(right);
    public static bool operator !=(WasmStorageType left, WasmStorageType right) => !left.Equals(right);
    public override string ToString() => Name;
}

public readonly struct WasmFieldType : IEquatable<WasmFieldType>
{
    public WasmStorageType Storage { get; }
    public bool Mutable { get; }

    public WasmFieldType(WasmStorageType storage, bool mutable)
    {
        Storage = storage;
        Mutable = mutable;
    }

    public WasmFieldType RemapTypes(Func<uint, uint> map) => new WasmFieldType(Storage.RemapTypes(map), Mutable);

    public bool Equals(WasmFieldType other) => Storage == other.Storage && Mutable == other.Mutable;
    public override bool Equals(object? obj) => obj is WasmFieldType other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Storage, Mutable);
    public static bool operator ==(WasmFieldType left, WasmFieldType right) => left.Equals(right);
    public static bool operator !=(WasmFieldType left, WasmFieldType right) => !left.Equals(right);
}

public sealed class WasmFunctionType : IEquatable<WasmFunctionType>
{
    public static WasmFunctionType Empty { get; } = new WasmFunctionType(ImmutableArray<WasmValueType>.Empty, ImmutableArray<WasmValueType>.Empty);

    public ImmutableArray<WasmValueType> Parameters { get; }
    public ImmutableArray<WasmValueType> Results { get; }

    public WasmFunctionType(IEnumerable<WasmValueType>? parameters, IEnumerable<WasmValueType>? results)
    {
        Parameters = parameters?.ToImmutableArray() ?? ImmutableArray<WasmValueType>.Empty;
        Results = results?.ToImmutableArray() ?? ImmutableArray<WasmValueType>.Empty;
    }

    public static WasmFunctionType Of(IEnumerable<WasmValueType> parameters, params WasmValueType[] results)
        => new WasmFunctionType(parameters, results);

    public WasmFunctionType RemapTypes(Func<uint, uint> map)
        => new WasmFunctionType(Parameters.Select(type => type.RemapTypes(map)), Results.Select(type => type.RemapTypes(map)));

    public bool Equals(WasmFunctionType? other)
        => other is not null && Parameters.SequenceEqual(other.Parameters) && Results.SequenceEqual(other.Results);

    public override bool Equals(object? obj) => Equals(obj as WasmFunctionType);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var type in Parameters)
            hash.Add(type);
        hash.Add(-1);
        foreach (var type in Results)
            hash.Add(type);
        return hash.ToHashCode();
    }

    public override string ToString()
        => "(" + string.Join(" ", Parameters.Select(static type => type.Name)) + ") -> (" + string.Join(" ", Results.Select(static type => type.Name)) + ")";
}

public enum WasmCompositeKind : byte
{
    Function,
    Struct,
    Array,
}

public sealed class WasmCompositeType : IEquatable<WasmCompositeType>
{
    public WasmCompositeKind Kind { get; }
    public WasmFunctionType? Function { get; }
    public ImmutableArray<WasmFieldType> Fields { get; }

    private WasmCompositeType(WasmCompositeKind kind, WasmFunctionType? function, ImmutableArray<WasmFieldType> fields)
    {
        Kind = kind;
        Function = function;
        Fields = fields.IsDefault ? ImmutableArray<WasmFieldType>.Empty : fields;
    }

    public static WasmCompositeType Of(WasmFunctionType function)
        => new WasmCompositeType(WasmCompositeKind.Function, function ?? throw new ArgumentNullException(nameof(function)), default);

    public static WasmCompositeType Struct(IEnumerable<WasmFieldType> fields)
        => new WasmCompositeType(WasmCompositeKind.Struct, null, fields.ToImmutableArray());

    public static WasmCompositeType Array(WasmFieldType element)
        => new WasmCompositeType(WasmCompositeKind.Array, null, ImmutableArray.Create(element));

    public WasmFieldType Element => Kind == WasmCompositeKind.Array ? Fields[0] : throw new InvalidOperationException("Only an array has an element type");

    public WasmCompositeType RemapTypes(Func<uint, uint> map)
        => Kind == WasmCompositeKind.Function
            ? Of(Function!.RemapTypes(map))
            : new WasmCompositeType(Kind, null, Fields.Select(field => field.RemapTypes(map)).ToImmutableArray());

    public bool Equals(WasmCompositeType? other)
        => other is not null && Kind == other.Kind && (Kind == WasmCompositeKind.Function ? Function!.Equals(other.Function) : Fields.SequenceEqual(other.Fields));

    public override bool Equals(object? obj) => Equals(obj as WasmCompositeType);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Function);
        foreach (var field in Fields)
            hash.Add(field);
        return hash.ToHashCode();
    }
}

public sealed class WasmSubType : IEquatable<WasmSubType>
{
    public bool Final { get; }
    public ImmutableArray<uint> Supertypes { get; }
    public WasmCompositeType Composite { get; }

    public WasmSubType(bool final, IEnumerable<uint>? supertypes, WasmCompositeType composite)
    {
        Final = final;
        Supertypes = supertypes?.ToImmutableArray() ?? ImmutableArray<uint>.Empty;
        Composite = composite ?? throw new ArgumentNullException(nameof(composite));
    }

    public static WasmSubType Of(WasmFunctionType function) => new WasmSubType(true, null, WasmCompositeType.Of(function));
    public static WasmSubType Of(WasmCompositeType composite) => new WasmSubType(true, null, composite);

    public bool IsPlain => Final && Supertypes.IsEmpty;

    public WasmSubType RemapTypes(Func<uint, uint> map)
        => new WasmSubType(Final, Supertypes.Select(map), Composite.RemapTypes(map));

    public bool Equals(WasmSubType? other)
        => other is not null && Final == other.Final && Supertypes.SequenceEqual(other.Supertypes) && Composite.Equals(other.Composite);

    public override bool Equals(object? obj) => Equals(obj as WasmSubType);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Final);
        foreach (var supertype in Supertypes)
            hash.Add(supertype);
        hash.Add(Composite);
        return hash.ToHashCode();
    }
}

public sealed class WasmRecursionGroup : IEquatable<WasmRecursionGroup>
{
    public ImmutableArray<WasmSubType> Types { get; }

    public WasmRecursionGroup(IEnumerable<WasmSubType> types)
    {
        Types = types?.ToImmutableArray() ?? ImmutableArray<WasmSubType>.Empty;
    }

    public WasmRecursionGroup(WasmSubType type)
        : this(new[] { type })
    {
    }

    public WasmRecursionGroup RemapTypes(Func<uint, uint> map)
        => new WasmRecursionGroup(Types.Select(type => type.RemapTypes(map)));

    public bool Equals(WasmRecursionGroup? other) => other is not null && Types.SequenceEqual(other.Types);
    public override bool Equals(object? obj) => Equals(obj as WasmRecursionGroup);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var type in Types)
            hash.Add(type);
        return hash.ToHashCode();
    }
}

public readonly struct WasmLimits : IEquatable<WasmLimits>
{
    public ulong Minimum { get; }
    public ulong? Maximum { get; }
    public bool Shared { get; }

    public bool Is64Bit { get; }

    public WasmLimits(ulong minimum, ulong? maximum = null, bool shared = false, bool is64Bit = false)
    {
        Minimum = minimum;
        Maximum = maximum;
        Shared = shared;
        Is64Bit = is64Bit;
    }

    public WasmValueType AddressType => Is64Bit ? WasmValueType.I64 : WasmValueType.I32;

    public bool Equals(WasmLimits other)
        => Minimum == other.Minimum && Maximum == other.Maximum && Shared == other.Shared && Is64Bit == other.Is64Bit;

    public override bool Equals(object? obj) => obj is WasmLimits other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Minimum, Maximum, Shared, Is64Bit);
}

public readonly struct WasmTableType
{
    public WasmValueType ElementType { get; }
    public WasmLimits Limits { get; }

    public WasmTableType(WasmValueType elementType, WasmLimits limits)
    {
        if (!elementType.IsReference)
            throw new ArgumentException("A table holds references", nameof(elementType));
        ElementType = elementType;
        Limits = limits;
    }

    public WasmValueType AddressType => Limits.AddressType;
}

public readonly struct WasmMemoryType
{
    public const ulong PageSize = 65536;

    public WasmLimits Limits { get; }

    public WasmMemoryType(WasmLimits limits)
    {
        Limits = limits;
    }

    public WasmValueType AddressType => Limits.AddressType;
}

public readonly struct WasmGlobalType
{
    public WasmValueType ValueType { get; }
    public bool Mutable { get; }

    public WasmGlobalType(WasmValueType valueType, bool mutable)
    {
        ValueType = valueType;
        Mutable = mutable;
    }
}

public enum WasmExternalKind : byte
{
    Function = 0,
    Table = 1,
    Memory = 2,
    Global = 3,
    Tag = 4,
}

public enum WasmBlockTypeKind : byte
{
    Empty,
    Value,
    Function,
}

public readonly struct WasmBlockType
{
    public WasmBlockTypeKind Kind { get; }
    public WasmValueType ValueType { get; }
    public uint TypeIndex { get; }

    /// <summary>A signature not yet given an index; whoever builds the module interns it</summary>
    public WasmFunctionType? Signature { get; }

    private WasmBlockType(WasmBlockTypeKind kind, WasmValueType valueType, uint typeIndex, WasmFunctionType? signature)
    {
        Kind = kind;
        ValueType = valueType;
        TypeIndex = typeIndex;
        Signature = signature;
    }

    public static WasmBlockType Empty => default;
    public static WasmBlockType Of(WasmValueType valueType) => new WasmBlockType(WasmBlockTypeKind.Value, valueType, 0, null);
    public static WasmBlockType OfType(uint typeIndex) => new WasmBlockType(WasmBlockTypeKind.Function, default, typeIndex, null);

    public static WasmBlockType OfSignature(WasmFunctionType signature)
    {
        if (signature is null)
            throw new ArgumentNullException(nameof(signature));
        if (signature.Parameters.IsEmpty && signature.Results.IsEmpty)
            return Empty;
        if (signature.Parameters.IsEmpty && signature.Results.Length == 1)
            return Of(signature.Results[0]);
        return new WasmBlockType(WasmBlockTypeKind.Function, default, uint.MaxValue, signature);
    }

    public bool NeedsTypeIndex => Kind == WasmBlockTypeKind.Function && TypeIndex == uint.MaxValue;

    public WasmBlockType WithTypeIndex(uint typeIndex) => new WasmBlockType(WasmBlockTypeKind.Function, default, typeIndex, Signature);

    public WasmBlockType RemapTypes(Func<uint, uint> map)
        => Kind switch
        {
            WasmBlockTypeKind.Value => Of(ValueType.RemapTypes(map)),
            WasmBlockTypeKind.Function when NeedsTypeIndex => new WasmBlockType(Kind, default, TypeIndex, Signature!.RemapTypes(map)),
            WasmBlockTypeKind.Function => OfType(map(TypeIndex)),
            _ => this,
        };
}

public enum WasmInstructionFormat : byte
{
    None,
    BlockType,
    Label,
    LabelTable,
    Function,
    CallIndirect,
    Type,
    TypeField,
    TypeCount,
    TypeData,
    TypeElement,
    TypeType,
    Local,
    Global,
    Table,
    Memory,
    MemoryArgument,
    I32,
    I64,
    F32,
    F64,
    HeapType,
    ReferenceType,
    BranchCast,
    SelectTypes,
    Tag,
    TryTable,
    Data,
    Element,
    MemoryInit,
    MemoryCopy,
    TableInit,
    TableCopy,
    V128,
    Lane,
    MemoryLane,
    Shuffle,
}

public enum WasmInstrKind : int
{
    Unreachable = 0x00,
    Nop = 0x01,
    Block = 0x02,
    Loop = 0x03,
    If = 0x04,
    Else = 0x05,
    Throw = 0x08,
    ThrowRef = 0x0A,
    End = 0x0B,
    Br = 0x0C,
    BrIf = 0x0D,
    BrTable = 0x0E,
    Return = 0x0F,
    Call = 0x10,
    CallIndirect = 0x11,
    ReturnCall = 0x12,
    ReturnCallIndirect = 0x13,
    CallRef = 0x14,
    ReturnCallRef = 0x15,
    Drop = 0x1A,
    Select = 0x1B,
    SelectTyped = 0x1C,
    TryTable = 0x1F,
    LocalGet = 0x20,
    LocalSet = 0x21,
    LocalTee = 0x22,
    GlobalGet = 0x23,
    GlobalSet = 0x24,
    TableGet = 0x25,
    TableSet = 0x26,
    I32Load = 0x28,
    I64Load = 0x29,
    F32Load = 0x2A,
    F64Load = 0x2B,
    I32Load8S = 0x2C,
    I32Load8U = 0x2D,
    I32Load16S = 0x2E,
    I32Load16U = 0x2F,
    I64Load8S = 0x30,
    I64Load8U = 0x31,
    I64Load16S = 0x32,
    I64Load16U = 0x33,
    I64Load32S = 0x34,
    I64Load32U = 0x35,
    I32Store = 0x36,
    I64Store = 0x37,
    F32Store = 0x38,
    F64Store = 0x39,
    I32Store8 = 0x3A,
    I32Store16 = 0x3B,
    I64Store8 = 0x3C,
    I64Store16 = 0x3D,
    I64Store32 = 0x3E,
    MemorySize = 0x3F,
    MemoryGrow = 0x40,
    I32Const = 0x41,
    I64Const = 0x42,
    F32Const = 0x43,
    F64Const = 0x44,
    I32Eqz = 0x45,
    I32Eq = 0x46,
    I32Ne = 0x47,
    I32LtS = 0x48,
    I32LtU = 0x49,
    I32GtS = 0x4A,
    I32GtU = 0x4B,
    I32LeS = 0x4C,
    I32LeU = 0x4D,
    I32GeS = 0x4E,
    I32GeU = 0x4F,
    I64Eqz = 0x50,
    I64Eq = 0x51,
    I64Ne = 0x52,
    I64LtS = 0x53,
    I64LtU = 0x54,
    I64GtS = 0x55,
    I64GtU = 0x56,
    I64LeS = 0x57,
    I64LeU = 0x58,
    I64GeS = 0x59,
    I64GeU = 0x5A,
    F32Eq = 0x5B,
    F32Ne = 0x5C,
    F32Lt = 0x5D,
    F32Gt = 0x5E,
    F32Le = 0x5F,
    F32Ge = 0x60,
    F64Eq = 0x61,
    F64Ne = 0x62,
    F64Lt = 0x63,
    F64Gt = 0x64,
    F64Le = 0x65,
    F64Ge = 0x66,
    I32Clz = 0x67,
    I32Ctz = 0x68,
    I32Popcnt = 0x69,
    I32Add = 0x6A,
    I32Sub = 0x6B,
    I32Mul = 0x6C,
    I32DivS = 0x6D,
    I32DivU = 0x6E,
    I32RemS = 0x6F,
    I32RemU = 0x70,
    I32And = 0x71,
    I32Or = 0x72,
    I32Xor = 0x73,
    I32Shl = 0x74,
    I32ShrS = 0x75,
    I32ShrU = 0x76,
    I32Rotl = 0x77,
    I32Rotr = 0x78,
    I64Clz = 0x79,
    I64Ctz = 0x7A,
    I64Popcnt = 0x7B,
    I64Add = 0x7C,
    I64Sub = 0x7D,
    I64Mul = 0x7E,
    I64DivS = 0x7F,
    I64DivU = 0x80,
    I64RemS = 0x81,
    I64RemU = 0x82,
    I64And = 0x83,
    I64Or = 0x84,
    I64Xor = 0x85,
    I64Shl = 0x86,
    I64ShrS = 0x87,
    I64ShrU = 0x88,
    I64Rotl = 0x89,
    I64Rotr = 0x8A,
    F32Abs = 0x8B,
    F32Neg = 0x8C,
    F32Ceil = 0x8D,
    F32Floor = 0x8E,
    F32Trunc = 0x8F,
    F32Nearest = 0x90,
    F32Sqrt = 0x91,
    F32Add = 0x92,
    F32Sub = 0x93,
    F32Mul = 0x94,
    F32Div = 0x95,
    F32Min = 0x96,
    F32Max = 0x97,
    F32Copysign = 0x98,
    F64Abs = 0x99,
    F64Neg = 0x9A,
    F64Ceil = 0x9B,
    F64Floor = 0x9C,
    F64Trunc = 0x9D,
    F64Nearest = 0x9E,
    F64Sqrt = 0x9F,
    F64Add = 0xA0,
    F64Sub = 0xA1,
    F64Mul = 0xA2,
    F64Div = 0xA3,
    F64Min = 0xA4,
    F64Max = 0xA5,
    F64Copysign = 0xA6,
    I32WrapI64 = 0xA7,
    I32TruncF32S = 0xA8,
    I32TruncF32U = 0xA9,
    I32TruncF64S = 0xAA,
    I32TruncF64U = 0xAB,
    I64ExtendI32S = 0xAC,
    I64ExtendI32U = 0xAD,
    I64TruncF32S = 0xAE,
    I64TruncF32U = 0xAF,
    I64TruncF64S = 0xB0,
    I64TruncF64U = 0xB1,
    F32ConvertI32S = 0xB2,
    F32ConvertI32U = 0xB3,
    F32ConvertI64S = 0xB4,
    F32ConvertI64U = 0xB5,
    F32DemoteF64 = 0xB6,
    F64ConvertI32S = 0xB7,
    F64ConvertI32U = 0xB8,
    F64ConvertI64S = 0xB9,
    F64ConvertI64U = 0xBA,
    F64PromoteF32 = 0xBB,
    I32ReinterpretF32 = 0xBC,
    I64ReinterpretF64 = 0xBD,
    F32ReinterpretI32 = 0xBE,
    F64ReinterpretI64 = 0xBF,
    I32Extend8S = 0xC0,
    I32Extend16S = 0xC1,
    I64Extend8S = 0xC2,
    I64Extend16S = 0xC3,
    I64Extend32S = 0xC4,
    RefNull = 0xD0,
    RefIsNull = 0xD1,
    RefFunc = 0xD2,
    RefEq = 0xD3,
    RefAsNonNull = 0xD4,
    BrOnNull = 0xD5,
    BrOnNonNull = 0xD6,
    StructNew = 0xFB_0000,
    StructNewDefault = 0xFB_0001,
    StructGet = 0xFB_0002,
    StructGetS = 0xFB_0003,
    StructGetU = 0xFB_0004,
    StructSet = 0xFB_0005,
    ArrayNew = 0xFB_0006,
    ArrayNewDefault = 0xFB_0007,
    ArrayNewFixed = 0xFB_0008,
    ArrayNewData = 0xFB_0009,
    ArrayNewElem = 0xFB_000A,
    ArrayGet = 0xFB_000B,
    ArrayGetS = 0xFB_000C,
    ArrayGetU = 0xFB_000D,
    ArraySet = 0xFB_000E,
    ArrayLen = 0xFB_000F,
    ArrayFill = 0xFB_0010,
    ArrayCopy = 0xFB_0011,
    ArrayInitData = 0xFB_0012,
    ArrayInitElem = 0xFB_0013,
    RefTest = 0xFB_0014,
    RefTestNull = 0xFB_0015,
    RefCast = 0xFB_0016,
    RefCastNull = 0xFB_0017,
    BrOnCast = 0xFB_0018,
    BrOnCastFail = 0xFB_0019,
    AnyConvertExtern = 0xFB_001A,
    ExternConvertAny = 0xFB_001B,
    RefI31 = 0xFB_001C,
    I31GetS = 0xFB_001D,
    I31GetU = 0xFB_001E,
    I32TruncSatF32S = 0xFC_0000,
    I32TruncSatF32U = 0xFC_0001,
    I32TruncSatF64S = 0xFC_0002,
    I32TruncSatF64U = 0xFC_0003,
    I64TruncSatF32S = 0xFC_0004,
    I64TruncSatF32U = 0xFC_0005,
    I64TruncSatF64S = 0xFC_0006,
    I64TruncSatF64U = 0xFC_0007,
    MemoryInit = 0xFC_0008,
    DataDrop = 0xFC_0009,
    MemoryCopy = 0xFC_000A,
    MemoryFill = 0xFC_000B,
    TableInit = 0xFC_000C,
    ElemDrop = 0xFC_000D,
    TableCopy = 0xFC_000E,
    TableGrow = 0xFC_000F,
    TableSize = 0xFC_0010,
    TableFill = 0xFC_0011,
    V128Load = 0xFD_0000,
    V128Load8x8S = 0xFD_0001,
    V128Load8x8U = 0xFD_0002,
    V128Load16x4S = 0xFD_0003,
    V128Load16x4U = 0xFD_0004,
    V128Load32x2S = 0xFD_0005,
    V128Load32x2U = 0xFD_0006,
    V128Load8Splat = 0xFD_0007,
    V128Load16Splat = 0xFD_0008,
    V128Load32Splat = 0xFD_0009,
    V128Load64Splat = 0xFD_000A,
    V128Store = 0xFD_000B,
    V128Const = 0xFD_000C,
    I8x16Shuffle = 0xFD_000D,
    I8x16Swizzle = 0xFD_000E,
    I8x16Splat = 0xFD_000F,
    I16x8Splat = 0xFD_0010,
    I32x4Splat = 0xFD_0011,
    I64x2Splat = 0xFD_0012,
    F32x4Splat = 0xFD_0013,
    F64x2Splat = 0xFD_0014,
    I8x16ExtractLaneS = 0xFD_0015,
    I8x16ExtractLaneU = 0xFD_0016,
    I8x16ReplaceLane = 0xFD_0017,
    I16x8ExtractLaneS = 0xFD_0018,
    I16x8ExtractLaneU = 0xFD_0019,
    I16x8ReplaceLane = 0xFD_001A,
    I32x4ExtractLane = 0xFD_001B,
    I32x4ReplaceLane = 0xFD_001C,
    I64x2ExtractLane = 0xFD_001D,
    I64x2ReplaceLane = 0xFD_001E,
    F32x4ExtractLane = 0xFD_001F,
    F32x4ReplaceLane = 0xFD_0020,
    F64x2ExtractLane = 0xFD_0021,
    F64x2ReplaceLane = 0xFD_0022,
    I8x16Eq = 0xFD_0023,
    I8x16Ne = 0xFD_0024,
    I8x16LtS = 0xFD_0025,
    I8x16LtU = 0xFD_0026,
    I8x16GtS = 0xFD_0027,
    I8x16GtU = 0xFD_0028,
    I8x16LeS = 0xFD_0029,
    I8x16LeU = 0xFD_002A,
    I8x16GeS = 0xFD_002B,
    I8x16GeU = 0xFD_002C,
    I16x8Eq = 0xFD_002D,
    I16x8Ne = 0xFD_002E,
    I16x8LtS = 0xFD_002F,
    I16x8LtU = 0xFD_0030,
    I16x8GtS = 0xFD_0031,
    I16x8GtU = 0xFD_0032,
    I16x8LeS = 0xFD_0033,
    I16x8LeU = 0xFD_0034,
    I16x8GeS = 0xFD_0035,
    I16x8GeU = 0xFD_0036,
    I32x4Eq = 0xFD_0037,
    I32x4Ne = 0xFD_0038,
    I32x4LtS = 0xFD_0039,
    I32x4LtU = 0xFD_003A,
    I32x4GtS = 0xFD_003B,
    I32x4GtU = 0xFD_003C,
    I32x4LeS = 0xFD_003D,
    I32x4LeU = 0xFD_003E,
    I32x4GeS = 0xFD_003F,
    I32x4GeU = 0xFD_0040,
    F32x4Eq = 0xFD_0041,
    F32x4Ne = 0xFD_0042,
    F32x4Lt = 0xFD_0043,
    F32x4Gt = 0xFD_0044,
    F32x4Le = 0xFD_0045,
    F32x4Ge = 0xFD_0046,
    F64x2Eq = 0xFD_0047,
    F64x2Ne = 0xFD_0048,
    F64x2Lt = 0xFD_0049,
    F64x2Gt = 0xFD_004A,
    F64x2Le = 0xFD_004B,
    F64x2Ge = 0xFD_004C,
    V128Not = 0xFD_004D,
    V128And = 0xFD_004E,
    V128Andnot = 0xFD_004F,
    V128Or = 0xFD_0050,
    V128Xor = 0xFD_0051,
    V128Bitselect = 0xFD_0052,
    V128AnyTrue = 0xFD_0053,
    V128Load8Lane = 0xFD_0054,
    V128Load16Lane = 0xFD_0055,
    V128Load32Lane = 0xFD_0056,
    V128Load64Lane = 0xFD_0057,
    V128Store8Lane = 0xFD_0058,
    V128Store16Lane = 0xFD_0059,
    V128Store32Lane = 0xFD_005A,
    V128Store64Lane = 0xFD_005B,
    V128Load32Zero = 0xFD_005C,
    V128Load64Zero = 0xFD_005D,
    F32x4DemoteF64x2Zero = 0xFD_005E,
    F64x2PromoteLowF32x4 = 0xFD_005F,
    I8x16Abs = 0xFD_0060,
    I8x16Neg = 0xFD_0061,
    I8x16Popcnt = 0xFD_0062,
    I8x16AllTrue = 0xFD_0063,
    I8x16Bitmask = 0xFD_0064,
    I8x16NarrowI16x8S = 0xFD_0065,
    I8x16NarrowI16x8U = 0xFD_0066,
    F32x4Ceil = 0xFD_0067,
    F32x4Floor = 0xFD_0068,
    F32x4Trunc = 0xFD_0069,
    F32x4Nearest = 0xFD_006A,
    I8x16Shl = 0xFD_006B,
    I8x16ShrS = 0xFD_006C,
    I8x16ShrU = 0xFD_006D,
    I8x16Add = 0xFD_006E,
    I8x16AddSatS = 0xFD_006F,
    I8x16AddSatU = 0xFD_0070,
    I8x16Sub = 0xFD_0071,
    I8x16SubSatS = 0xFD_0072,
    I8x16SubSatU = 0xFD_0073,
    F64x2Ceil = 0xFD_0074,
    F64x2Floor = 0xFD_0075,
    I8x16MinS = 0xFD_0076,
    I8x16MinU = 0xFD_0077,
    I8x16MaxS = 0xFD_0078,
    I8x16MaxU = 0xFD_0079,
    F64x2Trunc = 0xFD_007A,
    I8x16AvgrU = 0xFD_007B,
    I16x8ExtaddPairwiseI8x16S = 0xFD_007C,
    I16x8ExtaddPairwiseI8x16U = 0xFD_007D,
    I32x4ExtaddPairwiseI16x8S = 0xFD_007E,
    I32x4ExtaddPairwiseI16x8U = 0xFD_007F,
    I16x8Abs = 0xFD_0080,
    I16x8Neg = 0xFD_0081,
    I16x8Q15mulrSatS = 0xFD_0082,
    I16x8AllTrue = 0xFD_0083,
    I16x8Bitmask = 0xFD_0084,
    I16x8NarrowI32x4S = 0xFD_0085,
    I16x8NarrowI32x4U = 0xFD_0086,
    I16x8ExtendLowI8x16S = 0xFD_0087,
    I16x8ExtendHighI8x16S = 0xFD_0088,
    I16x8ExtendLowI8x16U = 0xFD_0089,
    I16x8ExtendHighI8x16U = 0xFD_008A,
    I16x8Shl = 0xFD_008B,
    I16x8ShrS = 0xFD_008C,
    I16x8ShrU = 0xFD_008D,
    I16x8Add = 0xFD_008E,
    I16x8AddSatS = 0xFD_008F,
    I16x8AddSatU = 0xFD_0090,
    I16x8Sub = 0xFD_0091,
    I16x8SubSatS = 0xFD_0092,
    I16x8SubSatU = 0xFD_0093,
    F64x2Nearest = 0xFD_0094,
    I16x8Mul = 0xFD_0095,
    I16x8MinS = 0xFD_0096,
    I16x8MinU = 0xFD_0097,
    I16x8MaxS = 0xFD_0098,
    I16x8MaxU = 0xFD_0099,
    I16x8AvgrU = 0xFD_009B,
    I16x8ExtmulLowI8x16S = 0xFD_009C,
    I16x8ExtmulHighI8x16S = 0xFD_009D,
    I16x8ExtmulLowI8x16U = 0xFD_009E,
    I16x8ExtmulHighI8x16U = 0xFD_009F,
    I32x4Abs = 0xFD_00A0,
    I32x4Neg = 0xFD_00A1,
    I32x4AllTrue = 0xFD_00A3,
    I32x4Bitmask = 0xFD_00A4,
    I32x4ExtendLowI16x8S = 0xFD_00A7,
    I32x4ExtendHighI16x8S = 0xFD_00A8,
    I32x4ExtendLowI16x8U = 0xFD_00A9,
    I32x4ExtendHighI16x8U = 0xFD_00AA,
    I32x4Shl = 0xFD_00AB,
    I32x4ShrS = 0xFD_00AC,
    I32x4ShrU = 0xFD_00AD,
    I32x4Add = 0xFD_00AE,
    I32x4Sub = 0xFD_00B1,
    I32x4Mul = 0xFD_00B5,
    I32x4MinS = 0xFD_00B6,
    I32x4MinU = 0xFD_00B7,
    I32x4MaxS = 0xFD_00B8,
    I32x4MaxU = 0xFD_00B9,
    I32x4DotI16x8S = 0xFD_00BA,
    I32x4ExtmulLowI16x8S = 0xFD_00BC,
    I32x4ExtmulHighI16x8S = 0xFD_00BD,
    I32x4ExtmulLowI16x8U = 0xFD_00BE,
    I32x4ExtmulHighI16x8U = 0xFD_00BF,
    I64x2Abs = 0xFD_00C0,
    I64x2Neg = 0xFD_00C1,
    I64x2AllTrue = 0xFD_00C3,
    I64x2Bitmask = 0xFD_00C4,
    I64x2ExtendLowI32x4S = 0xFD_00C7,
    I64x2ExtendHighI32x4S = 0xFD_00C8,
    I64x2ExtendLowI32x4U = 0xFD_00C9,
    I64x2ExtendHighI32x4U = 0xFD_00CA,
    I64x2Shl = 0xFD_00CB,
    I64x2ShrS = 0xFD_00CC,
    I64x2ShrU = 0xFD_00CD,
    I64x2Add = 0xFD_00CE,
    I64x2Sub = 0xFD_00D1,
    I64x2Mul = 0xFD_00D5,
    I64x2Eq = 0xFD_00D6,
    I64x2Ne = 0xFD_00D7,
    I64x2LtS = 0xFD_00D8,
    I64x2GtS = 0xFD_00D9,
    I64x2LeS = 0xFD_00DA,
    I64x2GeS = 0xFD_00DB,
    I64x2ExtmulLowI32x4S = 0xFD_00DC,
    I64x2ExtmulHighI32x4S = 0xFD_00DD,
    I64x2ExtmulLowI32x4U = 0xFD_00DE,
    I64x2ExtmulHighI32x4U = 0xFD_00DF,
    F32x4Abs = 0xFD_00E0,
    F32x4Neg = 0xFD_00E1,
    F32x4Sqrt = 0xFD_00E3,
    F32x4Add = 0xFD_00E4,
    F32x4Sub = 0xFD_00E5,
    F32x4Mul = 0xFD_00E6,
    F32x4Div = 0xFD_00E7,
    F32x4Min = 0xFD_00E8,
    F32x4Max = 0xFD_00E9,
    F32x4Pmin = 0xFD_00EA,
    F32x4Pmax = 0xFD_00EB,
    F64x2Abs = 0xFD_00EC,
    F64x2Neg = 0xFD_00ED,
    F64x2Sqrt = 0xFD_00EF,
    F64x2Add = 0xFD_00F0,
    F64x2Sub = 0xFD_00F1,
    F64x2Mul = 0xFD_00F2,
    F64x2Div = 0xFD_00F3,
    F64x2Min = 0xFD_00F4,
    F64x2Max = 0xFD_00F5,
    F64x2Pmin = 0xFD_00F6,
    F64x2Pmax = 0xFD_00F7,
    I32x4TruncSatF32x4S = 0xFD_00F8,
    I32x4TruncSatF32x4U = 0xFD_00F9,
    F32x4ConvertI32x4S = 0xFD_00FA,
    F32x4ConvertI32x4U = 0xFD_00FB,
    I32x4TruncSatF64x2SZero = 0xFD_00FC,
    I32x4TruncSatF64x2UZero = 0xFD_00FD,
    F64x2ConvertLowI32x4S = 0xFD_00FE,
    F64x2ConvertLowI32x4U = 0xFD_00FF,
    I8x16RelaxedSwizzle = 0xFD_0100,
    I32x4RelaxedTruncF32x4S = 0xFD_0101,
    I32x4RelaxedTruncF32x4U = 0xFD_0102,
    I32x4RelaxedTruncF64x2SZero = 0xFD_0103,
    I32x4RelaxedTruncF64x2UZero = 0xFD_0104,
    F32x4RelaxedMadd = 0xFD_0105,
    F32x4RelaxedNmadd = 0xFD_0106,
    F64x2RelaxedMadd = 0xFD_0107,
    F64x2RelaxedNmadd = 0xFD_0108,
    I8x16RelaxedLaneselect = 0xFD_0109,
    I16x8RelaxedLaneselect = 0xFD_010A,
    I32x4RelaxedLaneselect = 0xFD_010B,
    I64x2RelaxedLaneselect = 0xFD_010C,
    F32x4RelaxedMin = 0xFD_010D,
    F32x4RelaxedMax = 0xFD_010E,
    F64x2RelaxedMin = 0xFD_010F,
    F64x2RelaxedMax = 0xFD_0110,
    I16x8RelaxedQ15mulrS = 0xFD_0111,
    I16x8RelaxedDotI8x16I7x16S = 0xFD_0112,
    I32x4RelaxedDotI8x16I7x16AddS = 0xFD_0113,
}

internal readonly struct WasmInstructionMetadata
{
    public WasmInstrKind Kind { get; }
    public string Mnemonic { get; }
    public WasmInstructionFormat Format { get; }

    // Operands before the colon, results after it; a and t are the address types of the memory and table used
    public string? Signature { get; }
    public WasmIsaFlags RequiredIsa { get; }

    public byte NaturalAlignment { get; }

    public WasmInstructionMetadata(WasmInstrKind kind, string mnemonic, WasmInstructionFormat format, string? signature, WasmIsaFlags requiredIsa, byte naturalAlignment)
    {
        Kind = kind;
        Mnemonic = mnemonic;
        Format = format;
        Signature = signature;
        RequiredIsa = requiredIsa;
        NaturalAlignment = naturalAlignment;
    }

    public byte Prefix => (byte)((int)Kind >> 16);
    public uint Code => Prefix == 0 ? (uint)Kind : (uint)Kind & 0xFFFF;
}

internal static class WasmInstructionTable
{
    private static readonly Dictionary<WasmInstrKind, WasmInstructionMetadata> ByKind = new Dictionary<WasmInstrKind, WasmInstructionMetadata>();
    private static readonly Dictionary<string, WasmInstructionMetadata> ByMnemonic = new Dictionary<string, WasmInstructionMetadata>(StringComparer.Ordinal);

    static WasmInstructionTable()
    {
        Add(WasmInstrKind.Unreachable, "unreachable", WasmInstructionFormat.None, null, WasmIsaFlags.None, 0);
        Add(WasmInstrKind.Nop, "nop", WasmInstructionFormat.None, ":", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.Block, "block", WasmInstructionFormat.BlockType, null, WasmIsaFlags.None, 0);
        Add(WasmInstrKind.Loop, "loop", WasmInstructionFormat.BlockType, null, WasmIsaFlags.None, 0);
        Add(WasmInstrKind.If, "if", WasmInstructionFormat.BlockType, null, WasmIsaFlags.None, 0);
        Add(WasmInstrKind.Else, "else", WasmInstructionFormat.None, null, WasmIsaFlags.None, 0);
        Add(WasmInstrKind.Throw, "throw", WasmInstructionFormat.Tag, null, WasmIsaFlags.ExceptionHandling, 0);
        Add(WasmInstrKind.ThrowRef, "throw_ref", WasmInstructionFormat.None, null, WasmIsaFlags.ExceptionHandling, 0);
        Add(WasmInstrKind.End, "end", WasmInstructionFormat.None, null, WasmIsaFlags.None, 0);
        Add(WasmInstrKind.Br, "br", WasmInstructionFormat.Label, null, WasmIsaFlags.None, 0);
        Add(WasmInstrKind.BrIf, "br_if", WasmInstructionFormat.Label, null, WasmIsaFlags.None, 0);
        Add(WasmInstrKind.BrTable, "br_table", WasmInstructionFormat.LabelTable, null, WasmIsaFlags.None, 0);
        Add(WasmInstrKind.Return, "return", WasmInstructionFormat.None, null, WasmIsaFlags.None, 0);
        Add(WasmInstrKind.Call, "call", WasmInstructionFormat.Function, null, WasmIsaFlags.None, 0);
        Add(WasmInstrKind.CallIndirect, "call_indirect", WasmInstructionFormat.CallIndirect, null, WasmIsaFlags.None, 0);
        Add(WasmInstrKind.ReturnCall, "return_call", WasmInstructionFormat.Function, null, WasmIsaFlags.TailCall, 0);
        Add(WasmInstrKind.ReturnCallIndirect, "return_call_indirect", WasmInstructionFormat.CallIndirect, null, WasmIsaFlags.TailCall, 0);
        Add(WasmInstrKind.CallRef, "call_ref", WasmInstructionFormat.Type, null, WasmIsaFlags.TypedFunctionReferences, 0);
        Add(WasmInstrKind.ReturnCallRef, "return_call_ref", WasmInstructionFormat.Type, null, WasmIsaFlags.TailCall | WasmIsaFlags.TypedFunctionReferences, 0);
        Add(WasmInstrKind.Drop, "drop", WasmInstructionFormat.None, null, WasmIsaFlags.None, 0);
        Add(WasmInstrKind.Select, "select", WasmInstructionFormat.None, null, WasmIsaFlags.None, 0);
        Add(WasmInstrKind.SelectTyped, "select", WasmInstructionFormat.SelectTypes, null, WasmIsaFlags.ReferenceTypes, 0);
        Add(WasmInstrKind.TryTable, "try_table", WasmInstructionFormat.TryTable, null, WasmIsaFlags.ExceptionHandling, 0);
        Add(WasmInstrKind.LocalGet, "local.get", WasmInstructionFormat.Local, null, WasmIsaFlags.None, 0);
        Add(WasmInstrKind.LocalSet, "local.set", WasmInstructionFormat.Local, null, WasmIsaFlags.None, 0);
        Add(WasmInstrKind.LocalTee, "local.tee", WasmInstructionFormat.Local, null, WasmIsaFlags.None, 0);
        Add(WasmInstrKind.GlobalGet, "global.get", WasmInstructionFormat.Global, null, WasmIsaFlags.None, 0);
        Add(WasmInstrKind.GlobalSet, "global.set", WasmInstructionFormat.Global, null, WasmIsaFlags.None, 0);
        Add(WasmInstrKind.TableGet, "table.get", WasmInstructionFormat.Table, null, WasmIsaFlags.ReferenceTypes, 0);
        Add(WasmInstrKind.TableSet, "table.set", WasmInstructionFormat.Table, null, WasmIsaFlags.ReferenceTypes, 0);
        Add(WasmInstrKind.I32Load, "i32.load", WasmInstructionFormat.MemoryArgument, "a:i", WasmIsaFlags.None, 2);
        Add(WasmInstrKind.I64Load, "i64.load", WasmInstructionFormat.MemoryArgument, "a:I", WasmIsaFlags.None, 3);
        Add(WasmInstrKind.F32Load, "f32.load", WasmInstructionFormat.MemoryArgument, "a:f", WasmIsaFlags.None, 2);
        Add(WasmInstrKind.F64Load, "f64.load", WasmInstructionFormat.MemoryArgument, "a:F", WasmIsaFlags.None, 3);
        Add(WasmInstrKind.I32Load8S, "i32.load8_s", WasmInstructionFormat.MemoryArgument, "a:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32Load8U, "i32.load8_u", WasmInstructionFormat.MemoryArgument, "a:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32Load16S, "i32.load16_s", WasmInstructionFormat.MemoryArgument, "a:i", WasmIsaFlags.None, 1);
        Add(WasmInstrKind.I32Load16U, "i32.load16_u", WasmInstructionFormat.MemoryArgument, "a:i", WasmIsaFlags.None, 1);
        Add(WasmInstrKind.I64Load8S, "i64.load8_s", WasmInstructionFormat.MemoryArgument, "a:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64Load8U, "i64.load8_u", WasmInstructionFormat.MemoryArgument, "a:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64Load16S, "i64.load16_s", WasmInstructionFormat.MemoryArgument, "a:I", WasmIsaFlags.None, 1);
        Add(WasmInstrKind.I64Load16U, "i64.load16_u", WasmInstructionFormat.MemoryArgument, "a:I", WasmIsaFlags.None, 1);
        Add(WasmInstrKind.I64Load32S, "i64.load32_s", WasmInstructionFormat.MemoryArgument, "a:I", WasmIsaFlags.None, 2);
        Add(WasmInstrKind.I64Load32U, "i64.load32_u", WasmInstructionFormat.MemoryArgument, "a:I", WasmIsaFlags.None, 2);
        Add(WasmInstrKind.I32Store, "i32.store", WasmInstructionFormat.MemoryArgument, "ai:", WasmIsaFlags.None, 2);
        Add(WasmInstrKind.I64Store, "i64.store", WasmInstructionFormat.MemoryArgument, "aI:", WasmIsaFlags.None, 3);
        Add(WasmInstrKind.F32Store, "f32.store", WasmInstructionFormat.MemoryArgument, "af:", WasmIsaFlags.None, 2);
        Add(WasmInstrKind.F64Store, "f64.store", WasmInstructionFormat.MemoryArgument, "aF:", WasmIsaFlags.None, 3);
        Add(WasmInstrKind.I32Store8, "i32.store8", WasmInstructionFormat.MemoryArgument, "ai:", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32Store16, "i32.store16", WasmInstructionFormat.MemoryArgument, "ai:", WasmIsaFlags.None, 1);
        Add(WasmInstrKind.I64Store8, "i64.store8", WasmInstructionFormat.MemoryArgument, "aI:", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64Store16, "i64.store16", WasmInstructionFormat.MemoryArgument, "aI:", WasmIsaFlags.None, 1);
        Add(WasmInstrKind.I64Store32, "i64.store32", WasmInstructionFormat.MemoryArgument, "aI:", WasmIsaFlags.None, 2);
        Add(WasmInstrKind.MemorySize, "memory.size", WasmInstructionFormat.Memory, ":a", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.MemoryGrow, "memory.grow", WasmInstructionFormat.Memory, "a:a", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32Const, "i32.const", WasmInstructionFormat.I32, ":i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64Const, "i64.const", WasmInstructionFormat.I64, ":I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Const, "f32.const", WasmInstructionFormat.F32, ":f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Const, "f64.const", WasmInstructionFormat.F64, ":F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32Eqz, "i32.eqz", WasmInstructionFormat.None, "i:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32Eq, "i32.eq", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32Ne, "i32.ne", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32LtS, "i32.lt_s", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32LtU, "i32.lt_u", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32GtS, "i32.gt_s", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32GtU, "i32.gt_u", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32LeS, "i32.le_s", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32LeU, "i32.le_u", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32GeS, "i32.ge_s", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32GeU, "i32.ge_u", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64Eqz, "i64.eqz", WasmInstructionFormat.None, "I:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64Eq, "i64.eq", WasmInstructionFormat.None, "II:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64Ne, "i64.ne", WasmInstructionFormat.None, "II:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64LtS, "i64.lt_s", WasmInstructionFormat.None, "II:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64LtU, "i64.lt_u", WasmInstructionFormat.None, "II:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64GtS, "i64.gt_s", WasmInstructionFormat.None, "II:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64GtU, "i64.gt_u", WasmInstructionFormat.None, "II:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64LeS, "i64.le_s", WasmInstructionFormat.None, "II:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64LeU, "i64.le_u", WasmInstructionFormat.None, "II:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64GeS, "i64.ge_s", WasmInstructionFormat.None, "II:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64GeU, "i64.ge_u", WasmInstructionFormat.None, "II:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Eq, "f32.eq", WasmInstructionFormat.None, "ff:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Ne, "f32.ne", WasmInstructionFormat.None, "ff:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Lt, "f32.lt", WasmInstructionFormat.None, "ff:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Gt, "f32.gt", WasmInstructionFormat.None, "ff:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Le, "f32.le", WasmInstructionFormat.None, "ff:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Ge, "f32.ge", WasmInstructionFormat.None, "ff:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Eq, "f64.eq", WasmInstructionFormat.None, "FF:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Ne, "f64.ne", WasmInstructionFormat.None, "FF:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Lt, "f64.lt", WasmInstructionFormat.None, "FF:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Gt, "f64.gt", WasmInstructionFormat.None, "FF:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Le, "f64.le", WasmInstructionFormat.None, "FF:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Ge, "f64.ge", WasmInstructionFormat.None, "FF:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32Clz, "i32.clz", WasmInstructionFormat.None, "i:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32Ctz, "i32.ctz", WasmInstructionFormat.None, "i:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32Popcnt, "i32.popcnt", WasmInstructionFormat.None, "i:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32Add, "i32.add", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32Sub, "i32.sub", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32Mul, "i32.mul", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32DivS, "i32.div_s", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32DivU, "i32.div_u", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32RemS, "i32.rem_s", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32RemU, "i32.rem_u", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32And, "i32.and", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32Or, "i32.or", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32Xor, "i32.xor", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32Shl, "i32.shl", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32ShrS, "i32.shr_s", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32ShrU, "i32.shr_u", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32Rotl, "i32.rotl", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32Rotr, "i32.rotr", WasmInstructionFormat.None, "ii:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64Clz, "i64.clz", WasmInstructionFormat.None, "I:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64Ctz, "i64.ctz", WasmInstructionFormat.None, "I:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64Popcnt, "i64.popcnt", WasmInstructionFormat.None, "I:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64Add, "i64.add", WasmInstructionFormat.None, "II:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64Sub, "i64.sub", WasmInstructionFormat.None, "II:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64Mul, "i64.mul", WasmInstructionFormat.None, "II:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64DivS, "i64.div_s", WasmInstructionFormat.None, "II:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64DivU, "i64.div_u", WasmInstructionFormat.None, "II:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64RemS, "i64.rem_s", WasmInstructionFormat.None, "II:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64RemU, "i64.rem_u", WasmInstructionFormat.None, "II:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64And, "i64.and", WasmInstructionFormat.None, "II:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64Or, "i64.or", WasmInstructionFormat.None, "II:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64Xor, "i64.xor", WasmInstructionFormat.None, "II:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64Shl, "i64.shl", WasmInstructionFormat.None, "II:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64ShrS, "i64.shr_s", WasmInstructionFormat.None, "II:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64ShrU, "i64.shr_u", WasmInstructionFormat.None, "II:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64Rotl, "i64.rotl", WasmInstructionFormat.None, "II:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64Rotr, "i64.rotr", WasmInstructionFormat.None, "II:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Abs, "f32.abs", WasmInstructionFormat.None, "f:f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Neg, "f32.neg", WasmInstructionFormat.None, "f:f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Ceil, "f32.ceil", WasmInstructionFormat.None, "f:f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Floor, "f32.floor", WasmInstructionFormat.None, "f:f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Trunc, "f32.trunc", WasmInstructionFormat.None, "f:f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Nearest, "f32.nearest", WasmInstructionFormat.None, "f:f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Sqrt, "f32.sqrt", WasmInstructionFormat.None, "f:f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Add, "f32.add", WasmInstructionFormat.None, "ff:f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Sub, "f32.sub", WasmInstructionFormat.None, "ff:f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Mul, "f32.mul", WasmInstructionFormat.None, "ff:f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Div, "f32.div", WasmInstructionFormat.None, "ff:f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Min, "f32.min", WasmInstructionFormat.None, "ff:f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Max, "f32.max", WasmInstructionFormat.None, "ff:f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32Copysign, "f32.copysign", WasmInstructionFormat.None, "ff:f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Abs, "f64.abs", WasmInstructionFormat.None, "F:F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Neg, "f64.neg", WasmInstructionFormat.None, "F:F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Ceil, "f64.ceil", WasmInstructionFormat.None, "F:F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Floor, "f64.floor", WasmInstructionFormat.None, "F:F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Trunc, "f64.trunc", WasmInstructionFormat.None, "F:F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Nearest, "f64.nearest", WasmInstructionFormat.None, "F:F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Sqrt, "f64.sqrt", WasmInstructionFormat.None, "F:F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Add, "f64.add", WasmInstructionFormat.None, "FF:F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Sub, "f64.sub", WasmInstructionFormat.None, "FF:F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Mul, "f64.mul", WasmInstructionFormat.None, "FF:F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Div, "f64.div", WasmInstructionFormat.None, "FF:F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Min, "f64.min", WasmInstructionFormat.None, "FF:F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Max, "f64.max", WasmInstructionFormat.None, "FF:F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64Copysign, "f64.copysign", WasmInstructionFormat.None, "FF:F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32WrapI64, "i32.wrap_i64", WasmInstructionFormat.None, "I:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32TruncF32S, "i32.trunc_f32_s", WasmInstructionFormat.None, "f:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32TruncF32U, "i32.trunc_f32_u", WasmInstructionFormat.None, "f:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32TruncF64S, "i32.trunc_f64_s", WasmInstructionFormat.None, "F:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32TruncF64U, "i32.trunc_f64_u", WasmInstructionFormat.None, "F:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64ExtendI32S, "i64.extend_i32_s", WasmInstructionFormat.None, "i:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64ExtendI32U, "i64.extend_i32_u", WasmInstructionFormat.None, "i:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64TruncF32S, "i64.trunc_f32_s", WasmInstructionFormat.None, "f:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64TruncF32U, "i64.trunc_f32_u", WasmInstructionFormat.None, "f:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64TruncF64S, "i64.trunc_f64_s", WasmInstructionFormat.None, "F:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64TruncF64U, "i64.trunc_f64_u", WasmInstructionFormat.None, "F:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32ConvertI32S, "f32.convert_i32_s", WasmInstructionFormat.None, "i:f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32ConvertI32U, "f32.convert_i32_u", WasmInstructionFormat.None, "i:f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32ConvertI64S, "f32.convert_i64_s", WasmInstructionFormat.None, "I:f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32ConvertI64U, "f32.convert_i64_u", WasmInstructionFormat.None, "I:f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32DemoteF64, "f32.demote_f64", WasmInstructionFormat.None, "F:f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64ConvertI32S, "f64.convert_i32_s", WasmInstructionFormat.None, "i:F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64ConvertI32U, "f64.convert_i32_u", WasmInstructionFormat.None, "i:F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64ConvertI64S, "f64.convert_i64_s", WasmInstructionFormat.None, "I:F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64ConvertI64U, "f64.convert_i64_u", WasmInstructionFormat.None, "I:F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64PromoteF32, "f64.promote_f32", WasmInstructionFormat.None, "f:F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32ReinterpretF32, "i32.reinterpret_f32", WasmInstructionFormat.None, "f:i", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I64ReinterpretF64, "i64.reinterpret_f64", WasmInstructionFormat.None, "F:I", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F32ReinterpretI32, "f32.reinterpret_i32", WasmInstructionFormat.None, "i:f", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.F64ReinterpretI64, "f64.reinterpret_i64", WasmInstructionFormat.None, "I:F", WasmIsaFlags.None, 0);
        Add(WasmInstrKind.I32Extend8S, "i32.extend8_s", WasmInstructionFormat.None, "i:i", WasmIsaFlags.SignExtension, 0);
        Add(WasmInstrKind.I32Extend16S, "i32.extend16_s", WasmInstructionFormat.None, "i:i", WasmIsaFlags.SignExtension, 0);
        Add(WasmInstrKind.I64Extend8S, "i64.extend8_s", WasmInstructionFormat.None, "I:I", WasmIsaFlags.SignExtension, 0);
        Add(WasmInstrKind.I64Extend16S, "i64.extend16_s", WasmInstructionFormat.None, "I:I", WasmIsaFlags.SignExtension, 0);
        Add(WasmInstrKind.I64Extend32S, "i64.extend32_s", WasmInstructionFormat.None, "I:I", WasmIsaFlags.SignExtension, 0);
        Add(WasmInstrKind.RefNull, "ref.null", WasmInstructionFormat.HeapType, null, WasmIsaFlags.ReferenceTypes, 0);
        Add(WasmInstrKind.RefIsNull, "ref.is_null", WasmInstructionFormat.None, null, WasmIsaFlags.ReferenceTypes, 0);
        Add(WasmInstrKind.RefFunc, "ref.func", WasmInstructionFormat.Function, null, WasmIsaFlags.ReferenceTypes, 0);
        Add(WasmInstrKind.RefEq, "ref.eq", WasmInstructionFormat.None, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.RefAsNonNull, "ref.as_non_null", WasmInstructionFormat.None, null, WasmIsaFlags.TypedFunctionReferences, 0);
        Add(WasmInstrKind.BrOnNull, "br_on_null", WasmInstructionFormat.Label, null, WasmIsaFlags.TypedFunctionReferences, 0);
        Add(WasmInstrKind.BrOnNonNull, "br_on_non_null", WasmInstructionFormat.Label, null, WasmIsaFlags.TypedFunctionReferences, 0);
        Add(WasmInstrKind.StructNew, "struct.new", WasmInstructionFormat.Type, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.StructNewDefault, "struct.new_default", WasmInstructionFormat.Type, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.StructGet, "struct.get", WasmInstructionFormat.TypeField, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.StructGetS, "struct.get_s", WasmInstructionFormat.TypeField, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.StructGetU, "struct.get_u", WasmInstructionFormat.TypeField, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.StructSet, "struct.set", WasmInstructionFormat.TypeField, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.ArrayNew, "array.new", WasmInstructionFormat.Type, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.ArrayNewDefault, "array.new_default", WasmInstructionFormat.Type, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.ArrayNewFixed, "array.new_fixed", WasmInstructionFormat.TypeCount, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.ArrayNewData, "array.new_data", WasmInstructionFormat.TypeData, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.ArrayNewElem, "array.new_elem", WasmInstructionFormat.TypeElement, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.ArrayGet, "array.get", WasmInstructionFormat.Type, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.ArrayGetS, "array.get_s", WasmInstructionFormat.Type, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.ArrayGetU, "array.get_u", WasmInstructionFormat.Type, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.ArraySet, "array.set", WasmInstructionFormat.Type, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.ArrayLen, "array.len", WasmInstructionFormat.None, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.ArrayFill, "array.fill", WasmInstructionFormat.Type, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.ArrayCopy, "array.copy", WasmInstructionFormat.TypeType, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.ArrayInitData, "array.init_data", WasmInstructionFormat.TypeData, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.ArrayInitElem, "array.init_elem", WasmInstructionFormat.TypeElement, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.RefTest, "ref.test", WasmInstructionFormat.ReferenceType, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.RefTestNull, "ref.test", WasmInstructionFormat.ReferenceType, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.RefCast, "ref.cast", WasmInstructionFormat.ReferenceType, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.RefCastNull, "ref.cast", WasmInstructionFormat.ReferenceType, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.BrOnCast, "br_on_cast", WasmInstructionFormat.BranchCast, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.BrOnCastFail, "br_on_cast_fail", WasmInstructionFormat.BranchCast, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.AnyConvertExtern, "any.convert_extern", WasmInstructionFormat.None, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.ExternConvertAny, "extern.convert_any", WasmInstructionFormat.None, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.RefI31, "ref.i31", WasmInstructionFormat.None, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.I31GetS, "i31.get_s", WasmInstructionFormat.None, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.I31GetU, "i31.get_u", WasmInstructionFormat.None, null, WasmIsaFlags.GarbageCollection, 0);
        Add(WasmInstrKind.I32TruncSatF32S, "i32.trunc_sat_f32_s", WasmInstructionFormat.None, "f:i", WasmIsaFlags.SaturatingFloatToInt, 0);
        Add(WasmInstrKind.I32TruncSatF32U, "i32.trunc_sat_f32_u", WasmInstructionFormat.None, "f:i", WasmIsaFlags.SaturatingFloatToInt, 0);
        Add(WasmInstrKind.I32TruncSatF64S, "i32.trunc_sat_f64_s", WasmInstructionFormat.None, "F:i", WasmIsaFlags.SaturatingFloatToInt, 0);
        Add(WasmInstrKind.I32TruncSatF64U, "i32.trunc_sat_f64_u", WasmInstructionFormat.None, "F:i", WasmIsaFlags.SaturatingFloatToInt, 0);
        Add(WasmInstrKind.I64TruncSatF32S, "i64.trunc_sat_f32_s", WasmInstructionFormat.None, "f:I", WasmIsaFlags.SaturatingFloatToInt, 0);
        Add(WasmInstrKind.I64TruncSatF32U, "i64.trunc_sat_f32_u", WasmInstructionFormat.None, "f:I", WasmIsaFlags.SaturatingFloatToInt, 0);
        Add(WasmInstrKind.I64TruncSatF64S, "i64.trunc_sat_f64_s", WasmInstructionFormat.None, "F:I", WasmIsaFlags.SaturatingFloatToInt, 0);
        Add(WasmInstrKind.I64TruncSatF64U, "i64.trunc_sat_f64_u", WasmInstructionFormat.None, "F:I", WasmIsaFlags.SaturatingFloatToInt, 0);
        Add(WasmInstrKind.MemoryInit, "memory.init", WasmInstructionFormat.MemoryInit, "aii:", WasmIsaFlags.BulkMemory, 0);
        Add(WasmInstrKind.DataDrop, "data.drop", WasmInstructionFormat.Data, ":", WasmIsaFlags.BulkMemory, 0);
        Add(WasmInstrKind.MemoryCopy, "memory.copy", WasmInstructionFormat.MemoryCopy, null, WasmIsaFlags.BulkMemory, 0);
        Add(WasmInstrKind.MemoryFill, "memory.fill", WasmInstructionFormat.Memory, "aia:", WasmIsaFlags.BulkMemory, 0);
        Add(WasmInstrKind.TableInit, "table.init", WasmInstructionFormat.TableInit, "tii:", WasmIsaFlags.BulkMemory, 0);
        Add(WasmInstrKind.ElemDrop, "elem.drop", WasmInstructionFormat.Element, ":", WasmIsaFlags.BulkMemory, 0);
        Add(WasmInstrKind.TableCopy, "table.copy", WasmInstructionFormat.TableCopy, null, WasmIsaFlags.BulkMemory, 0);
        Add(WasmInstrKind.TableGrow, "table.grow", WasmInstructionFormat.Table, null, WasmIsaFlags.ReferenceTypes, 0);
        Add(WasmInstrKind.TableSize, "table.size", WasmInstructionFormat.Table, ":t", WasmIsaFlags.ReferenceTypes, 0);
        Add(WasmInstrKind.TableFill, "table.fill", WasmInstructionFormat.Table, null, WasmIsaFlags.ReferenceTypes, 0);
        Add(WasmInstrKind.V128Load, "v128.load", WasmInstructionFormat.MemoryArgument, "a:v", WasmIsaFlags.Simd128, 4);
        Add(WasmInstrKind.V128Load8x8S, "v128.load8x8_s", WasmInstructionFormat.MemoryArgument, "a:v", WasmIsaFlags.Simd128, 3);
        Add(WasmInstrKind.V128Load8x8U, "v128.load8x8_u", WasmInstructionFormat.MemoryArgument, "a:v", WasmIsaFlags.Simd128, 3);
        Add(WasmInstrKind.V128Load16x4S, "v128.load16x4_s", WasmInstructionFormat.MemoryArgument, "a:v", WasmIsaFlags.Simd128, 3);
        Add(WasmInstrKind.V128Load16x4U, "v128.load16x4_u", WasmInstructionFormat.MemoryArgument, "a:v", WasmIsaFlags.Simd128, 3);
        Add(WasmInstrKind.V128Load32x2S, "v128.load32x2_s", WasmInstructionFormat.MemoryArgument, "a:v", WasmIsaFlags.Simd128, 3);
        Add(WasmInstrKind.V128Load32x2U, "v128.load32x2_u", WasmInstructionFormat.MemoryArgument, "a:v", WasmIsaFlags.Simd128, 3);
        Add(WasmInstrKind.V128Load8Splat, "v128.load8_splat", WasmInstructionFormat.MemoryArgument, "a:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.V128Load16Splat, "v128.load16_splat", WasmInstructionFormat.MemoryArgument, "a:v", WasmIsaFlags.Simd128, 1);
        Add(WasmInstrKind.V128Load32Splat, "v128.load32_splat", WasmInstructionFormat.MemoryArgument, "a:v", WasmIsaFlags.Simd128, 2);
        Add(WasmInstrKind.V128Load64Splat, "v128.load64_splat", WasmInstructionFormat.MemoryArgument, "a:v", WasmIsaFlags.Simd128, 3);
        Add(WasmInstrKind.V128Store, "v128.store", WasmInstructionFormat.MemoryArgument, "av:", WasmIsaFlags.Simd128, 4);
        Add(WasmInstrKind.V128Const, "v128.const", WasmInstructionFormat.V128, ":v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16Shuffle, "i8x16.shuffle", WasmInstructionFormat.Shuffle, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16Swizzle, "i8x16.swizzle", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16Splat, "i8x16.splat", WasmInstructionFormat.None, "i:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8Splat, "i16x8.splat", WasmInstructionFormat.None, "i:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4Splat, "i32x4.splat", WasmInstructionFormat.None, "i:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2Splat, "i64x2.splat", WasmInstructionFormat.None, "I:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Splat, "f32x4.splat", WasmInstructionFormat.None, "f:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Splat, "f64x2.splat", WasmInstructionFormat.None, "F:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16ExtractLaneS, "i8x16.extract_lane_s", WasmInstructionFormat.Lane, "v:i", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16ExtractLaneU, "i8x16.extract_lane_u", WasmInstructionFormat.Lane, "v:i", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16ReplaceLane, "i8x16.replace_lane", WasmInstructionFormat.Lane, "vi:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8ExtractLaneS, "i16x8.extract_lane_s", WasmInstructionFormat.Lane, "v:i", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8ExtractLaneU, "i16x8.extract_lane_u", WasmInstructionFormat.Lane, "v:i", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8ReplaceLane, "i16x8.replace_lane", WasmInstructionFormat.Lane, "vi:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4ExtractLane, "i32x4.extract_lane", WasmInstructionFormat.Lane, "v:i", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4ReplaceLane, "i32x4.replace_lane", WasmInstructionFormat.Lane, "vi:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2ExtractLane, "i64x2.extract_lane", WasmInstructionFormat.Lane, "v:I", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2ReplaceLane, "i64x2.replace_lane", WasmInstructionFormat.Lane, "vI:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4ExtractLane, "f32x4.extract_lane", WasmInstructionFormat.Lane, "v:f", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4ReplaceLane, "f32x4.replace_lane", WasmInstructionFormat.Lane, "vf:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2ExtractLane, "f64x2.extract_lane", WasmInstructionFormat.Lane, "v:F", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2ReplaceLane, "f64x2.replace_lane", WasmInstructionFormat.Lane, "vF:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16Eq, "i8x16.eq", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16Ne, "i8x16.ne", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16LtS, "i8x16.lt_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16LtU, "i8x16.lt_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16GtS, "i8x16.gt_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16GtU, "i8x16.gt_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16LeS, "i8x16.le_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16LeU, "i8x16.le_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16GeS, "i8x16.ge_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16GeU, "i8x16.ge_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8Eq, "i16x8.eq", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8Ne, "i16x8.ne", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8LtS, "i16x8.lt_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8LtU, "i16x8.lt_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8GtS, "i16x8.gt_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8GtU, "i16x8.gt_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8LeS, "i16x8.le_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8LeU, "i16x8.le_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8GeS, "i16x8.ge_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8GeU, "i16x8.ge_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4Eq, "i32x4.eq", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4Ne, "i32x4.ne", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4LtS, "i32x4.lt_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4LtU, "i32x4.lt_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4GtS, "i32x4.gt_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4GtU, "i32x4.gt_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4LeS, "i32x4.le_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4LeU, "i32x4.le_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4GeS, "i32x4.ge_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4GeU, "i32x4.ge_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Eq, "f32x4.eq", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Ne, "f32x4.ne", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Lt, "f32x4.lt", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Gt, "f32x4.gt", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Le, "f32x4.le", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Ge, "f32x4.ge", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Eq, "f64x2.eq", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Ne, "f64x2.ne", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Lt, "f64x2.lt", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Gt, "f64x2.gt", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Le, "f64x2.le", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Ge, "f64x2.ge", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.V128Not, "v128.not", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.V128And, "v128.and", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.V128Andnot, "v128.andnot", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.V128Or, "v128.or", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.V128Xor, "v128.xor", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.V128Bitselect, "v128.bitselect", WasmInstructionFormat.None, "vvv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.V128AnyTrue, "v128.any_true", WasmInstructionFormat.None, "v:i", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.V128Load8Lane, "v128.load8_lane", WasmInstructionFormat.MemoryLane, "av:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.V128Load16Lane, "v128.load16_lane", WasmInstructionFormat.MemoryLane, "av:v", WasmIsaFlags.Simd128, 1);
        Add(WasmInstrKind.V128Load32Lane, "v128.load32_lane", WasmInstructionFormat.MemoryLane, "av:v", WasmIsaFlags.Simd128, 2);
        Add(WasmInstrKind.V128Load64Lane, "v128.load64_lane", WasmInstructionFormat.MemoryLane, "av:v", WasmIsaFlags.Simd128, 3);
        Add(WasmInstrKind.V128Store8Lane, "v128.store8_lane", WasmInstructionFormat.MemoryLane, "av:", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.V128Store16Lane, "v128.store16_lane", WasmInstructionFormat.MemoryLane, "av:", WasmIsaFlags.Simd128, 1);
        Add(WasmInstrKind.V128Store32Lane, "v128.store32_lane", WasmInstructionFormat.MemoryLane, "av:", WasmIsaFlags.Simd128, 2);
        Add(WasmInstrKind.V128Store64Lane, "v128.store64_lane", WasmInstructionFormat.MemoryLane, "av:", WasmIsaFlags.Simd128, 3);
        Add(WasmInstrKind.V128Load32Zero, "v128.load32_zero", WasmInstructionFormat.MemoryArgument, "a:v", WasmIsaFlags.Simd128, 2);
        Add(WasmInstrKind.V128Load64Zero, "v128.load64_zero", WasmInstructionFormat.MemoryArgument, "a:v", WasmIsaFlags.Simd128, 3);
        Add(WasmInstrKind.F32x4DemoteF64x2Zero, "f32x4.demote_f64x2_zero", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2PromoteLowF32x4, "f64x2.promote_low_f32x4", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16Abs, "i8x16.abs", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16Neg, "i8x16.neg", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16Popcnt, "i8x16.popcnt", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16AllTrue, "i8x16.all_true", WasmInstructionFormat.None, "v:i", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16Bitmask, "i8x16.bitmask", WasmInstructionFormat.None, "v:i", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16NarrowI16x8S, "i8x16.narrow_i16x8_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16NarrowI16x8U, "i8x16.narrow_i16x8_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Ceil, "f32x4.ceil", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Floor, "f32x4.floor", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Trunc, "f32x4.trunc", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Nearest, "f32x4.nearest", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16Shl, "i8x16.shl", WasmInstructionFormat.None, "vi:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16ShrS, "i8x16.shr_s", WasmInstructionFormat.None, "vi:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16ShrU, "i8x16.shr_u", WasmInstructionFormat.None, "vi:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16Add, "i8x16.add", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16AddSatS, "i8x16.add_sat_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16AddSatU, "i8x16.add_sat_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16Sub, "i8x16.sub", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16SubSatS, "i8x16.sub_sat_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16SubSatU, "i8x16.sub_sat_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Ceil, "f64x2.ceil", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Floor, "f64x2.floor", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16MinS, "i8x16.min_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16MinU, "i8x16.min_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16MaxS, "i8x16.max_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16MaxU, "i8x16.max_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Trunc, "f64x2.trunc", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16AvgrU, "i8x16.avgr_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8ExtaddPairwiseI8x16S, "i16x8.extadd_pairwise_i8x16_s", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8ExtaddPairwiseI8x16U, "i16x8.extadd_pairwise_i8x16_u", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4ExtaddPairwiseI16x8S, "i32x4.extadd_pairwise_i16x8_s", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4ExtaddPairwiseI16x8U, "i32x4.extadd_pairwise_i16x8_u", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8Abs, "i16x8.abs", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8Neg, "i16x8.neg", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8Q15mulrSatS, "i16x8.q15mulr_sat_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8AllTrue, "i16x8.all_true", WasmInstructionFormat.None, "v:i", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8Bitmask, "i16x8.bitmask", WasmInstructionFormat.None, "v:i", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8NarrowI32x4S, "i16x8.narrow_i32x4_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8NarrowI32x4U, "i16x8.narrow_i32x4_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8ExtendLowI8x16S, "i16x8.extend_low_i8x16_s", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8ExtendHighI8x16S, "i16x8.extend_high_i8x16_s", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8ExtendLowI8x16U, "i16x8.extend_low_i8x16_u", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8ExtendHighI8x16U, "i16x8.extend_high_i8x16_u", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8Shl, "i16x8.shl", WasmInstructionFormat.None, "vi:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8ShrS, "i16x8.shr_s", WasmInstructionFormat.None, "vi:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8ShrU, "i16x8.shr_u", WasmInstructionFormat.None, "vi:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8Add, "i16x8.add", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8AddSatS, "i16x8.add_sat_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8AddSatU, "i16x8.add_sat_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8Sub, "i16x8.sub", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8SubSatS, "i16x8.sub_sat_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8SubSatU, "i16x8.sub_sat_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Nearest, "f64x2.nearest", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8Mul, "i16x8.mul", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8MinS, "i16x8.min_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8MinU, "i16x8.min_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8MaxS, "i16x8.max_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8MaxU, "i16x8.max_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8AvgrU, "i16x8.avgr_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8ExtmulLowI8x16S, "i16x8.extmul_low_i8x16_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8ExtmulHighI8x16S, "i16x8.extmul_high_i8x16_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8ExtmulLowI8x16U, "i16x8.extmul_low_i8x16_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I16x8ExtmulHighI8x16U, "i16x8.extmul_high_i8x16_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4Abs, "i32x4.abs", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4Neg, "i32x4.neg", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4AllTrue, "i32x4.all_true", WasmInstructionFormat.None, "v:i", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4Bitmask, "i32x4.bitmask", WasmInstructionFormat.None, "v:i", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4ExtendLowI16x8S, "i32x4.extend_low_i16x8_s", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4ExtendHighI16x8S, "i32x4.extend_high_i16x8_s", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4ExtendLowI16x8U, "i32x4.extend_low_i16x8_u", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4ExtendHighI16x8U, "i32x4.extend_high_i16x8_u", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4Shl, "i32x4.shl", WasmInstructionFormat.None, "vi:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4ShrS, "i32x4.shr_s", WasmInstructionFormat.None, "vi:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4ShrU, "i32x4.shr_u", WasmInstructionFormat.None, "vi:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4Add, "i32x4.add", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4Sub, "i32x4.sub", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4Mul, "i32x4.mul", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4MinS, "i32x4.min_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4MinU, "i32x4.min_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4MaxS, "i32x4.max_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4MaxU, "i32x4.max_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4DotI16x8S, "i32x4.dot_i16x8_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4ExtmulLowI16x8S, "i32x4.extmul_low_i16x8_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4ExtmulHighI16x8S, "i32x4.extmul_high_i16x8_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4ExtmulLowI16x8U, "i32x4.extmul_low_i16x8_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4ExtmulHighI16x8U, "i32x4.extmul_high_i16x8_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2Abs, "i64x2.abs", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2Neg, "i64x2.neg", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2AllTrue, "i64x2.all_true", WasmInstructionFormat.None, "v:i", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2Bitmask, "i64x2.bitmask", WasmInstructionFormat.None, "v:i", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2ExtendLowI32x4S, "i64x2.extend_low_i32x4_s", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2ExtendHighI32x4S, "i64x2.extend_high_i32x4_s", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2ExtendLowI32x4U, "i64x2.extend_low_i32x4_u", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2ExtendHighI32x4U, "i64x2.extend_high_i32x4_u", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2Shl, "i64x2.shl", WasmInstructionFormat.None, "vi:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2ShrS, "i64x2.shr_s", WasmInstructionFormat.None, "vi:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2ShrU, "i64x2.shr_u", WasmInstructionFormat.None, "vi:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2Add, "i64x2.add", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2Sub, "i64x2.sub", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2Mul, "i64x2.mul", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2Eq, "i64x2.eq", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2Ne, "i64x2.ne", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2LtS, "i64x2.lt_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2GtS, "i64x2.gt_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2LeS, "i64x2.le_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2GeS, "i64x2.ge_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2ExtmulLowI32x4S, "i64x2.extmul_low_i32x4_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2ExtmulHighI32x4S, "i64x2.extmul_high_i32x4_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2ExtmulLowI32x4U, "i64x2.extmul_low_i32x4_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I64x2ExtmulHighI32x4U, "i64x2.extmul_high_i32x4_u", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Abs, "f32x4.abs", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Neg, "f32x4.neg", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Sqrt, "f32x4.sqrt", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Add, "f32x4.add", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Sub, "f32x4.sub", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Mul, "f32x4.mul", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Div, "f32x4.div", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Min, "f32x4.min", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Max, "f32x4.max", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Pmin, "f32x4.pmin", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4Pmax, "f32x4.pmax", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Abs, "f64x2.abs", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Neg, "f64x2.neg", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Sqrt, "f64x2.sqrt", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Add, "f64x2.add", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Sub, "f64x2.sub", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Mul, "f64x2.mul", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Div, "f64x2.div", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Min, "f64x2.min", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Max, "f64x2.max", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Pmin, "f64x2.pmin", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2Pmax, "f64x2.pmax", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4TruncSatF32x4S, "i32x4.trunc_sat_f32x4_s", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4TruncSatF32x4U, "i32x4.trunc_sat_f32x4_u", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4ConvertI32x4S, "f32x4.convert_i32x4_s", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F32x4ConvertI32x4U, "f32x4.convert_i32x4_u", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4TruncSatF64x2SZero, "i32x4.trunc_sat_f64x2_s_zero", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I32x4TruncSatF64x2UZero, "i32x4.trunc_sat_f64x2_u_zero", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2ConvertLowI32x4S, "f64x2.convert_low_i32x4_s", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.F64x2ConvertLowI32x4U, "f64x2.convert_low_i32x4_u", WasmInstructionFormat.None, "v:v", WasmIsaFlags.Simd128, 0);
        Add(WasmInstrKind.I8x16RelaxedSwizzle, "i8x16.relaxed_swizzle", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.RelaxedSimd, 0);
        Add(WasmInstrKind.I32x4RelaxedTruncF32x4S, "i32x4.relaxed_trunc_f32x4_s", WasmInstructionFormat.None, "v:v", WasmIsaFlags.RelaxedSimd, 0);
        Add(WasmInstrKind.I32x4RelaxedTruncF32x4U, "i32x4.relaxed_trunc_f32x4_u", WasmInstructionFormat.None, "v:v", WasmIsaFlags.RelaxedSimd, 0);
        Add(WasmInstrKind.I32x4RelaxedTruncF64x2SZero, "i32x4.relaxed_trunc_f64x2_s_zero", WasmInstructionFormat.None, "v:v", WasmIsaFlags.RelaxedSimd, 0);
        Add(WasmInstrKind.I32x4RelaxedTruncF64x2UZero, "i32x4.relaxed_trunc_f64x2_u_zero", WasmInstructionFormat.None, "v:v", WasmIsaFlags.RelaxedSimd, 0);
        Add(WasmInstrKind.F32x4RelaxedMadd, "f32x4.relaxed_madd", WasmInstructionFormat.None, "vvv:v", WasmIsaFlags.RelaxedSimd, 0);
        Add(WasmInstrKind.F32x4RelaxedNmadd, "f32x4.relaxed_nmadd", WasmInstructionFormat.None, "vvv:v", WasmIsaFlags.RelaxedSimd, 0);
        Add(WasmInstrKind.F64x2RelaxedMadd, "f64x2.relaxed_madd", WasmInstructionFormat.None, "vvv:v", WasmIsaFlags.RelaxedSimd, 0);
        Add(WasmInstrKind.F64x2RelaxedNmadd, "f64x2.relaxed_nmadd", WasmInstructionFormat.None, "vvv:v", WasmIsaFlags.RelaxedSimd, 0);
        Add(WasmInstrKind.I8x16RelaxedLaneselect, "i8x16.relaxed_laneselect", WasmInstructionFormat.None, "vvv:v", WasmIsaFlags.RelaxedSimd, 0);
        Add(WasmInstrKind.I16x8RelaxedLaneselect, "i16x8.relaxed_laneselect", WasmInstructionFormat.None, "vvv:v", WasmIsaFlags.RelaxedSimd, 0);
        Add(WasmInstrKind.I32x4RelaxedLaneselect, "i32x4.relaxed_laneselect", WasmInstructionFormat.None, "vvv:v", WasmIsaFlags.RelaxedSimd, 0);
        Add(WasmInstrKind.I64x2RelaxedLaneselect, "i64x2.relaxed_laneselect", WasmInstructionFormat.None, "vvv:v", WasmIsaFlags.RelaxedSimd, 0);
        Add(WasmInstrKind.F32x4RelaxedMin, "f32x4.relaxed_min", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.RelaxedSimd, 0);
        Add(WasmInstrKind.F32x4RelaxedMax, "f32x4.relaxed_max", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.RelaxedSimd, 0);
        Add(WasmInstrKind.F64x2RelaxedMin, "f64x2.relaxed_min", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.RelaxedSimd, 0);
        Add(WasmInstrKind.F64x2RelaxedMax, "f64x2.relaxed_max", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.RelaxedSimd, 0);
        Add(WasmInstrKind.I16x8RelaxedQ15mulrS, "i16x8.relaxed_q15mulr_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.RelaxedSimd, 0);
        Add(WasmInstrKind.I16x8RelaxedDotI8x16I7x16S, "i16x8.relaxed_dot_i8x16_i7x16_s", WasmInstructionFormat.None, "vv:v", WasmIsaFlags.RelaxedSimd, 0);
        Add(WasmInstrKind.I32x4RelaxedDotI8x16I7x16AddS, "i32x4.relaxed_dot_i8x16_i7x16_add_s", WasmInstructionFormat.None, "vvv:v", WasmIsaFlags.RelaxedSimd, 0);
    }

    private static void Add(WasmInstrKind kind, string mnemonic, WasmInstructionFormat format, string? signature, WasmIsaFlags requiredIsa, byte naturalAlignment)
    {
        var metadata = new WasmInstructionMetadata(kind, mnemonic, format, signature, requiredIsa, naturalAlignment);
        ByKind.Add(kind, metadata);
        // Instructions sharing a mnemonic are told apart by their immediates, and the first one is the plain form
        ByMnemonic.TryAdd(mnemonic, metadata);
    }

    public static WasmInstructionMetadata Get(WasmInstrKind kind)
        => ByKind.TryGetValue(kind, out var metadata) ? metadata : throw new ArgumentOutOfRangeException(nameof(kind), $"Unknown WebAssembly instruction 0x{(int)kind:X}");

    public static bool TryGet(WasmInstrKind kind, out WasmInstructionMetadata metadata)
        => ByKind.TryGetValue(kind, out metadata);

    public static bool TryGet(string mnemonic, out WasmInstructionMetadata metadata)
        => ByMnemonic.TryGetValue(mnemonic, out metadata);

    public static IEnumerable<WasmInstructionMetadata> All => ByKind.Values;
}

public readonly struct WasmMemoryArgument
{
    public uint Alignment { get; }
    public ulong Offset { get; }
    public uint Memory { get; }

    public WasmMemoryArgument(uint alignment, ulong offset, uint memory = 0)
    {
        Alignment = alignment;
        Offset = offset;
        Memory = memory;
    }
}

public enum WasmCatchKind : byte
{
    Catch = 0,
    CatchRef = 1,
    CatchAll = 2,
    CatchAllRef = 3,
}

public readonly struct WasmCatch
{
    public WasmCatchKind Kind { get; }
    public uint Tag { get; }
    public uint Label { get; }
    public string? TagSymbol { get; }

    public WasmCatch(WasmCatchKind kind, uint tag, uint label, string? tagSymbol = null)
    {
        Kind = kind;
        Tag = tag;
        Label = label;
        TagSymbol = string.IsNullOrEmpty(tagSymbol) ? null : tagSymbol;
    }

    public bool HasTag => Kind is WasmCatchKind.Catch or WasmCatchKind.CatchRef;
}

// An index not known before linking is a symbol: a function, global or tag, a data address or a table slot
public readonly struct WasmInstruction
{
    public WasmInstrKind Kind { get; }
    public uint Index { get; }
    public uint Index2 { get; }
    public ulong Value { get; }
    public WasmMemoryArgument MemoryArgument { get; }
    public WasmBlockType BlockType { get; }
    public WasmValueType Type { get; }
    public WasmValueType Type2 { get; }
    public ImmutableArray<uint> Labels { get; }
    public ImmutableArray<WasmValueType> Types { get; }
    public ImmutableArray<byte> Bytes { get; }
    public ImmutableArray<WasmCatch> Catches { get; }
    public WasmFunctionType? Signature { get; }
    public string? Symbol { get; }
    public long Addend { get; }

    private WasmInstruction(
        WasmInstrKind kind,
        uint index,
        uint index2,
        ulong value,
        WasmMemoryArgument memoryArgument,
        WasmBlockType blockType,
        WasmValueType type,
        WasmValueType type2,
        ImmutableArray<uint> labels,
        ImmutableArray<WasmValueType> types,
        ImmutableArray<byte> bytes,
        ImmutableArray<WasmCatch> catches,
        WasmFunctionType? signature,
        string? symbol,
        long addend)
    {
        Kind = kind;
        Index = index;
        Index2 = index2;
        Value = value;
        MemoryArgument = memoryArgument;
        BlockType = blockType;
        Type = type;
        Type2 = type2;
        Labels = labels.IsDefault ? ImmutableArray<uint>.Empty : labels;
        Types = types.IsDefault ? ImmutableArray<WasmValueType>.Empty : types;
        Bytes = bytes.IsDefault ? ImmutableArray<byte>.Empty : bytes;
        Catches = catches.IsDefault ? ImmutableArray<WasmCatch>.Empty : catches;
        Signature = signature;
        Symbol = string.IsNullOrEmpty(symbol) ? null : symbol;
        Addend = addend;
    }

    public static WasmInstruction Create(
        WasmInstrKind kind,
        uint index = 0,
        uint index2 = 0,
        ulong value = 0,
        WasmMemoryArgument memoryArgument = default,
        WasmBlockType blockType = default,
        WasmValueType type = default,
        WasmValueType type2 = default,
        ImmutableArray<uint> labels = default,
        ImmutableArray<WasmValueType> types = default,
        ImmutableArray<byte> bytes = default,
        ImmutableArray<WasmCatch> catches = default,
        WasmFunctionType? signature = null,
        string? symbol = null,
        long addend = 0)
        => new WasmInstruction(kind, index, index2, value, memoryArgument, blockType, type, type2, labels, types, bytes, catches, signature, symbol, addend);

    internal WasmInstructionMetadata Metadata => WasmInstructionTable.Get(Kind);

    public string Mnemonic => Metadata.Mnemonic;
    public bool HasSymbol => Symbol is not null;
    public int I32 => unchecked((int)Value);
    public long I64 => unchecked((long)Value);
    public float F32 => BitConverter.Int32BitsToSingle(unchecked((int)Value));
    public double F64 => BitConverter.Int64BitsToDouble(unchecked((long)Value));
    public int Lane => (int)Index2;

    public static WasmInstruction Simple(WasmInstrKind kind) => Create(kind);
    public static WasmInstruction WithIndex(WasmInstrKind kind, uint index) => Create(kind, index);
    public static WasmInstruction WithIndices(WasmInstrKind kind, uint index, uint index2) => Create(kind, index, index2);
    public static WasmInstruction WithSymbol(WasmInstrKind kind, string symbol, long addend = 0) => Create(kind, symbol: symbol, addend: addend);

    public static WasmInstruction Structured(WasmInstrKind kind, WasmBlockType blockType) => Create(kind, blockType: blockType);
    public static WasmInstruction Block(WasmBlockType blockType = default) => Structured(WasmInstrKind.Block, blockType);
    public static WasmInstruction Loop(WasmBlockType blockType = default) => Structured(WasmInstrKind.Loop, blockType);
    public static WasmInstruction If(WasmBlockType blockType = default) => Structured(WasmInstrKind.If, blockType);
    public static WasmInstruction TryTable(WasmBlockType blockType, IEnumerable<WasmCatch> catches) => Create(WasmInstrKind.TryTable, blockType: blockType, catches: catches.ToImmutableArray());
    public static WasmInstruction Else() => Simple(WasmInstrKind.Else);
    public static WasmInstruction End() => Simple(WasmInstrKind.End);
    public static WasmInstruction Br(uint depth) => WithIndex(WasmInstrKind.Br, depth);
    public static WasmInstruction BrIf(uint depth) => WithIndex(WasmInstrKind.BrIf, depth);
    public static WasmInstruction Return() => Simple(WasmInstrKind.Return);

    public static WasmInstruction BrTable(IEnumerable<uint> targets, uint defaultTarget)
        => Create(WasmInstrKind.BrTable, defaultTarget, labels: targets.ToImmutableArray());

    public static WasmInstruction Call(uint function) => WithIndex(WasmInstrKind.Call, function);
    public static WasmInstruction Call(string function) => WithSymbol(WasmInstrKind.Call, function);
    public static WasmInstruction ReturnCall(uint function) => WithIndex(WasmInstrKind.ReturnCall, function);
    public static WasmInstruction ReturnCall(string function) => WithSymbol(WasmInstrKind.ReturnCall, function);
    public static WasmInstruction CallRef(uint typeIndex) => WithIndex(WasmInstrKind.CallRef, typeIndex);
    public static WasmInstruction ReturnCallRef(uint typeIndex) => WithIndex(WasmInstrKind.ReturnCallRef, typeIndex);
    public static WasmInstruction CallIndirect(uint typeIndex, uint table = 0) => WithIndices(WasmInstrKind.CallIndirect, typeIndex, table);

    public static WasmInstruction CallIndirect(WasmFunctionType signature, uint table = 0)
        => Create(WasmInstrKind.CallIndirect, uint.MaxValue, table, signature: signature);

    public static WasmInstruction ReturnCallIndirect(WasmFunctionType signature, uint table = 0)
        => Create(WasmInstrKind.ReturnCallIndirect, uint.MaxValue, table, signature: signature);

    public static WasmInstruction Throw(uint tag) => WithIndex(WasmInstrKind.Throw, tag);
    public static WasmInstruction Throw(string tag) => WithSymbol(WasmInstrKind.Throw, tag);
    public static WasmInstruction Drop() => Simple(WasmInstrKind.Drop);
    public static WasmInstruction Select() => Simple(WasmInstrKind.Select);
    public static WasmInstruction Select(WasmValueType type) => Create(WasmInstrKind.SelectTyped, types: ImmutableArray.Create(type));

    public static WasmInstruction LocalGet(uint local) => WithIndex(WasmInstrKind.LocalGet, local);
    public static WasmInstruction LocalSet(uint local) => WithIndex(WasmInstrKind.LocalSet, local);
    public static WasmInstruction LocalTee(uint local) => WithIndex(WasmInstrKind.LocalTee, local);
    public static WasmInstruction GlobalGet(uint global) => WithIndex(WasmInstrKind.GlobalGet, global);
    public static WasmInstruction GlobalSet(uint global) => WithIndex(WasmInstrKind.GlobalSet, global);
    public static WasmInstruction GlobalGet(string global) => WithSymbol(WasmInstrKind.GlobalGet, global);
    public static WasmInstruction GlobalSet(string global) => WithSymbol(WasmInstrKind.GlobalSet, global);

    public static WasmInstruction Memory(WasmInstrKind kind, ulong offset = 0, uint? alignment = null, uint memory = 0)
    {
        var metadata = WasmInstructionTable.Get(kind);
        if (metadata.Format is not WasmInstructionFormat.MemoryArgument)
            throw new ArgumentException($"{metadata.Mnemonic} does not access memory through an address", nameof(kind));
        return Create(kind, memoryArgument: new WasmMemoryArgument(alignment ?? metadata.NaturalAlignment, offset, memory));
    }

    public static WasmInstruction MemoryLane(WasmInstrKind kind, int lane, ulong offset = 0, uint? alignment = null, uint memory = 0)
    {
        var metadata = WasmInstructionTable.Get(kind);
        if (metadata.Format is not WasmInstructionFormat.MemoryLane)
            throw new ArgumentException($"{metadata.Mnemonic} does not access a lane in memory", nameof(kind));
        return Create(kind, index2: (uint)lane, memoryArgument: new WasmMemoryArgument(alignment ?? metadata.NaturalAlignment, offset, memory));
    }

    public static WasmInstruction I32Const(int value) => Create(WasmInstrKind.I32Const, value: unchecked((uint)value));
    public static WasmInstruction I64Const(long value) => Create(WasmInstrKind.I64Const, value: unchecked((ulong)value));
    public static WasmInstruction F32Const(float value) => Create(WasmInstrKind.F32Const, value: (uint)BitConverter.SingleToInt32Bits(value));
    public static WasmInstruction F64Const(double value) => Create(WasmInstrKind.F64Const, value: unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));
    public static WasmInstruction F32ConstBits(uint bits) => Create(WasmInstrKind.F32Const, value: bits);
    public static WasmInstruction F64ConstBits(ulong bits) => Create(WasmInstrKind.F64Const, value: bits);

    /// <summary>An address-sized constant the linker fills in: a data symbol's address, or a function's table slot</summary>
    public static WasmInstruction AddressOf(string symbol, long addend = 0, bool is64Bit = false)
        => WithSymbol(is64Bit ? WasmInstrKind.I64Const : WasmInstrKind.I32Const, symbol, addend);

    public static WasmInstruction RefNull(WasmHeapType heapType) => Create(WasmInstrKind.RefNull, type: WasmValueType.Reference(heapType));
    public static WasmInstruction RefFunc(uint function) => WithIndex(WasmInstrKind.RefFunc, function);
    public static WasmInstruction RefFunc(string function) => WithSymbol(WasmInstrKind.RefFunc, function);
    public static WasmInstruction RefTest(WasmValueType type) => Create(type.Nullable ? WasmInstrKind.RefTestNull : WasmInstrKind.RefTest, type: type);
    public static WasmInstruction RefCast(WasmValueType type) => Create(type.Nullable ? WasmInstrKind.RefCastNull : WasmInstrKind.RefCast, type: type);
    public static WasmInstruction BrOnCast(uint depth, WasmValueType from, WasmValueType to) => Create(WasmInstrKind.BrOnCast, depth, type: from, type2: to);
    public static WasmInstruction BrOnCastFail(uint depth, WasmValueType from, WasmValueType to) => Create(WasmInstrKind.BrOnCastFail, depth, type: from, type2: to);

    public static WasmInstruction StructNew(uint type) => WithIndex(WasmInstrKind.StructNew, type);
    public static WasmInstruction StructGet(uint type, uint field) => WithIndices(WasmInstrKind.StructGet, type, field);
    public static WasmInstruction StructSet(uint type, uint field) => WithIndices(WasmInstrKind.StructSet, type, field);
    public static WasmInstruction ArrayNew(uint type) => WithIndex(WasmInstrKind.ArrayNew, type);
    public static WasmInstruction ArrayNewFixed(uint type, uint count) => WithIndices(WasmInstrKind.ArrayNewFixed, type, count);
    public static WasmInstruction ArrayGet(uint type) => WithIndex(WasmInstrKind.ArrayGet, type);
    public static WasmInstruction ArraySet(uint type) => WithIndex(WasmInstrKind.ArraySet, type);

    public static WasmInstruction V128Const(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 16)
            throw new ArgumentException("A v128 constant has sixteen bytes", nameof(bytes));
        return Create(WasmInstrKind.V128Const, bytes: bytes.ToArray().ToImmutableArray());
    }

    public static WasmInstruction Shuffle(ReadOnlySpan<byte> lanes)
    {
        if (lanes.Length != 16)
            throw new ArgumentException("A shuffle names sixteen lanes", nameof(lanes));
        return Create(WasmInstrKind.I8x16Shuffle, bytes: lanes.ToArray().ToImmutableArray());
    }

    public static WasmInstruction LaneOp(WasmInstrKind kind, int lane) => Create(kind, index2: (uint)lane);

    public WasmInstruction WithResolvedIndex(uint index)
        => new WasmInstruction(Kind, index, Index2, Value, MemoryArgument, BlockType, Type, Type2, Labels, Types, Bytes, Catches, Signature, null, 0);

    public WasmInstruction WithResolvedValue(ulong value)
        => new WasmInstruction(Kind, Index, Index2, value, MemoryArgument, BlockType, Type, Type2, Labels, Types, Bytes, Catches, Signature, null, 0);

    public WasmInstruction WithBlockType(WasmBlockType blockType)
        => new WasmInstruction(Kind, Index, Index2, Value, MemoryArgument, blockType, Type, Type2, Labels, Types, Bytes, Catches, Signature, Symbol, Addend);

    public WasmInstruction WithTypeIndex(uint typeIndex)
        => new WasmInstruction(Kind, typeIndex, Index2, Value, MemoryArgument, BlockType, Type, Type2, Labels, Types, Bytes, Catches, Signature, Symbol, Addend);

    public WasmInstruction WithCatches(ImmutableArray<WasmCatch> catches)
        => new WasmInstruction(Kind, Index, Index2, Value, MemoryArgument, BlockType, Type, Type2, Labels, Types, Bytes, catches, Signature, Symbol, Addend);

    public WasmInstruction RemapTypes(Func<uint, uint> map)
    {
        var format = Metadata.Format;
        uint index = Index;
        uint index2 = Index2;
        if (format is WasmInstructionFormat.Type or WasmInstructionFormat.TypeField or WasmInstructionFormat.TypeCount
            or WasmInstructionFormat.TypeData or WasmInstructionFormat.TypeElement or WasmInstructionFormat.TypeType
            || format == WasmInstructionFormat.CallIndirect && Index != uint.MaxValue)
            index = map(Index);
        if (format == WasmInstructionFormat.TypeType)
            index2 = map(Index2);
        return new WasmInstruction(
            Kind,
            index,
            index2,
            Value,
            MemoryArgument,
            BlockType.RemapTypes(map),
            Type.RemapTypes(map),
            Type2.RemapTypes(map),
            Labels,
            Types.Select(type => type.RemapTypes(map)).ToImmutableArray(),
            Bytes,
            Catches,
            Signature?.RemapTypes(map),
            Symbol,
            Addend);
    }

    public override string ToString()
        => WasmAssemblyWriter.FormatInstruction(this, null);
}

internal sealed class WasmInstructionBuilder
{
    private readonly List<WasmInstruction> _instructions = new List<WasmInstruction>();
    private readonly List<string?> _labels = new List<string?>();

    public int Count => _instructions.Count;
    public int Depth => _labels.Count;

    public void Emit(WasmInstruction instruction)
    {
        switch (instruction.Kind)
        {
            case WasmInstrKind.Block:
            case WasmInstrKind.Loop:
            case WasmInstrKind.If:
            case WasmInstrKind.TryTable:
                _labels.Add(null);
                break;
            case WasmInstrKind.End:
                if (_labels.Count == 0)
                    throw new InvalidOperationException("An end closes no block");
                _labels.RemoveAt(_labels.Count - 1);
                break;
        }
        _instructions.Add(instruction);
    }

    public void Begin(WasmInstruction structured, string label)
    {
        Emit(structured);
        _labels[^1] = label;
    }

    public uint DepthOf(string label)
    {
        for (int depth = 0; depth < _labels.Count; depth++)
        {
            if (StringComparer.Ordinal.Equals(_labels[_labels.Count - 1 - depth], label))
                return (uint)depth;
        }
        throw new InvalidOperationException($"No open block is labelled {label}");
    }

    public void Branch(string label) => Emit(WasmInstruction.Br(DepthOf(label)));
    public void BranchIf(string label) => Emit(WasmInstruction.BrIf(DepthOf(label)));

    public ImmutableArray<WasmInstruction> ToImmutable()
    {
        if (_labels.Count != 0)
            throw new InvalidOperationException("A block is left open");
        return _instructions.ToImmutableArray();
    }

    public void Clear()
    {
        _instructions.Clear();
        _labels.Clear();
    }
}

public sealed class WasmTarget
{
    public static WasmTarget Wasm32 { get; } = new WasmTarget(32, WasmIsaFlags.Wasm3);
    public static WasmTarget Wasm64 { get; } = new WasmTarget(64, WasmIsaFlags.Wasm3);

    public static WasmTarget FromTargetInfo(Cnidaria.C.TargetInfo target)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        return CreateFromDescriptor(target.Architecture);
    }

    public static WasmTarget FromTargetInfo(Cnidaria.Cs.TargetInfo target)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        return CreateFromDescriptor(target.Architecture);
    }

    private static WasmTarget CreateFromDescriptor(TargetArchitectureKind architecture)
    {
        if (architecture is not TargetArchitectureKind.Wasm32 and not TargetArchitectureKind.Wasm64)
            throw new ArgumentException("Target architecture is not WebAssembly", nameof(architecture));
        // Every proposal the target knows is part of WebAssembly 3.0, so there is nothing optional to choose
        return architecture == TargetArchitectureKind.Wasm64 ? Wasm64 : Wasm32;
    }

    public int XLen { get; }
    public WasmIsaFlags Isa { get; }
    public TargetEndianness Endianness => TargetEndianness.Little;
    public bool Is32Bit => XLen == 32;
    public bool Is64Bit => XLen == 64;
    public int PointerSize => XLen / 8;

    public WasmValueType AddressType => Is64Bit ? WasmValueType.I64 : WasmValueType.I32;

    public WasmTarget(int xlen, WasmIsaFlags isa)
    {
        if (xlen is not 32 and not 64)
            throw new ArgumentOutOfRangeException(nameof(xlen));
        XLen = xlen;
        Isa = xlen == 64 ? isa | WasmIsaFlags.Memory64 : isa;
    }

    public bool Has(WasmIsaFlags flags)
        => (Isa & flags) == flags;

    public override string ToString()
        => Is64Bit ? "wasm64" : "wasm32";
}
