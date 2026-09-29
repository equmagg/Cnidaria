using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.Wasm;

public sealed class WasmModule
{
    public WasmTarget Target { get; }
    public List<WasmRecursionGroup> Types { get; } = new List<WasmRecursionGroup>();
    public List<WasmImport> Imports { get; } = new List<WasmImport>();
    public List<WasmFunction> Functions { get; } = new List<WasmFunction>();
    public List<WasmTable> Tables { get; } = new List<WasmTable>();
    public List<WasmMemoryType> Memories { get; } = new List<WasmMemoryType>();
    public List<WasmTag> Tags { get; } = new List<WasmTag>();
    public List<WasmGlobal> Globals { get; } = new List<WasmGlobal>();
    public List<WasmExport> Exports { get; } = new List<WasmExport>();
    public uint? Start { get; set; }
    public List<WasmElementSegment> Elements { get; } = new List<WasmElementSegment>();
    public List<WasmDataSegment> Data { get; } = new List<WasmDataSegment>();
    public List<WasmCustomSection> CustomSections { get; } = new List<WasmCustomSection>();
    public WasmNames Names { get; } = new WasmNames();

    public WasmModule(WasmTarget target)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public int TypeCount => Types.Sum(static group => group.Types.Length);

    public WasmSubType GetType(uint index)
    {
        uint at = index;
        foreach (var group in Types)
        {
            if (at < group.Types.Length)
                return group.Types[(int)at];
            at -= (uint)group.Types.Length;
        }
        throw new ArgumentOutOfRangeException(nameof(index), $"Type {index} is not defined");
    }

    public bool TryGetFunctionType(uint typeIndex, out WasmFunctionType type)
    {
        type = null!;
        if (typeIndex >= TypeCount)
            return false;
        var composite = GetType(typeIndex).Composite;
        if (composite.Kind != WasmCompositeKind.Function)
            return false;
        type = composite.Function!;
        return true;
    }

    public IEnumerable<WasmSubType> AllTypes => Types.SelectMany(static group => group.Types);

    public uint InternType(WasmFunctionType signature)
    {
        uint index = 0;
        foreach (var group in Types)
        {
            if (group.Types.Length == 1 && group.Types[0].IsPlain && group.Types[0].Composite.Kind == WasmCompositeKind.Function
                && group.Types[0].Composite.Function!.Equals(signature))
                return index;
            index += (uint)group.Types.Length;
        }
        Types.Add(new WasmRecursionGroup(WasmSubType.Of(signature)));
        return index;
    }

    public int ImportedFunctionCount => Imports.Count(static import => import.Kind == WasmExternalKind.Function);
    public int ImportedTableCount => Imports.Count(static import => import.Kind == WasmExternalKind.Table);
    public int ImportedMemoryCount => Imports.Count(static import => import.Kind == WasmExternalKind.Memory);
    public int ImportedGlobalCount => Imports.Count(static import => import.Kind == WasmExternalKind.Global);
    public int ImportedTagCount => Imports.Count(static import => import.Kind == WasmExternalKind.Tag);

    public int FunctionCount => ImportedFunctionCount + Functions.Count;
    public int TableCount => ImportedTableCount + Tables.Count;
    public int MemoryCount => ImportedMemoryCount + Memories.Count;
    public int GlobalCount => ImportedGlobalCount + Globals.Count;
    public int TagCount => ImportedTagCount + Tags.Count;

    public uint AddImport(WasmImport import)
    {
        if (import is null)
            throw new ArgumentNullException(nameof(import));
        uint index = (uint)Imports.Count(existing => existing.Kind == import.Kind);
        Imports.Add(import);
        return index;
    }

    public uint FunctionTypeIndex(uint function)
    {
        foreach (var import in Imports)
        {
            if (import.Kind != WasmExternalKind.Function)
                continue;
            if (function == 0)
                return import.TypeIndex;
            function--;
        }
        return function < Functions.Count ? Functions[(int)function].TypeIndex : throw new ArgumentOutOfRangeException(nameof(function));
    }

    public uint TagTypeIndex(uint tag)
    {
        foreach (var import in Imports)
        {
            if (import.Kind != WasmExternalKind.Tag)
                continue;
            if (tag == 0)
                return import.TypeIndex;
            tag--;
        }
        return tag < Tags.Count ? Tags[(int)tag].TypeIndex : throw new ArgumentOutOfRangeException(nameof(tag));
    }

    public WasmTableType TableType(uint table)
        => Pick(WasmExternalKind.Table, table, static import => import.Table, Tables.Select(static t => t.Type).ToList());

    public WasmMemoryType MemoryType(uint memory)
        => Pick(WasmExternalKind.Memory, memory, static import => import.Memory, Memories);

    public WasmGlobalType GlobalType(uint global)
        => Pick(WasmExternalKind.Global, global, static import => import.Global, Globals.Select(static g => g.Type).ToList());

    private T Pick<T>(WasmExternalKind kind, uint index, Func<WasmImport, T> fromImport, IReadOnlyList<T> defined)
    {
        foreach (var import in Imports)
        {
            if (import.Kind != kind)
                continue;
            if (index == 0)
                return fromImport(import);
            index--;
        }
        return index < defined.Count ? defined[(int)index] : throw new ArgumentOutOfRangeException(nameof(index));
    }

    public byte[] ToBytes() => WasmModuleWriter.Write(this);

    public static WasmModule Decode(ReadOnlySpan<byte> bytes, WasmTarget? target = null) => WasmModuleReader.Read(bytes, target);

    public void Validate() => WasmValidator.Validate(this);

    public string FormatText() => WasmDisassembler.Disassemble(this);
}

public sealed class WasmImport
{
    public string Module { get; }
    public string Name { get; }
    public WasmExternalKind Kind { get; }

    public uint TypeIndex { get; }
    public WasmTableType Table { get; }
    public WasmMemoryType Memory { get; }
    public WasmGlobalType Global { get; }

    private WasmImport(string module, string name, WasmExternalKind kind, uint typeIndex, WasmTableType table, WasmMemoryType memory, WasmGlobalType global)
    {
        Module = module ?? throw new ArgumentNullException(nameof(module));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Kind = kind;
        TypeIndex = typeIndex;
        Table = table;
        Memory = memory;
        Global = global;
    }

    public static WasmImport Function(string module, string name, uint typeIndex)
        => new WasmImport(module, name, WasmExternalKind.Function, typeIndex, default, default, default);

    public static WasmImport OfTable(string module, string name, WasmTableType table)
        => new WasmImport(module, name, WasmExternalKind.Table, 0, table, default, default);

    public static WasmImport OfMemory(string module, string name, WasmMemoryType memory)
        => new WasmImport(module, name, WasmExternalKind.Memory, 0, default, memory, default);

    public static WasmImport OfGlobal(string module, string name, WasmGlobalType global)
        => new WasmImport(module, name, WasmExternalKind.Global, 0, default, default, global);

    public static WasmImport OfTag(string module, string name, uint typeIndex)
        => new WasmImport(module, name, WasmExternalKind.Tag, typeIndex, default, default, default);
}

public sealed class WasmFunction
{
    public uint TypeIndex { get; set; }
    public List<WasmValueType> Locals { get; } = new List<WasmValueType>();

    public List<WasmInstruction> Body { get; } = new List<WasmInstruction>();

    public WasmFunction(uint typeIndex)
    {
        TypeIndex = typeIndex;
    }
}

public sealed class WasmTable
{
    public WasmTableType Type { get; }

    /// <summary>The value every slot starts with; empty for a nullable table that starts out null</summary>
    public ImmutableArray<WasmInstruction> Initializer { get; }

    public WasmTable(WasmTableType type, IEnumerable<WasmInstruction>? initializer = null)
    {
        Type = type;
        Initializer = initializer?.ToImmutableArray() ?? ImmutableArray<WasmInstruction>.Empty;
    }

    public bool HasInitializer => !Initializer.IsEmpty;
}

public sealed class WasmTag
{
    public uint TypeIndex { get; }

    public WasmTag(uint typeIndex)
    {
        TypeIndex = typeIndex;
    }
}

public sealed class WasmGlobal
{
    public WasmGlobalType Type { get; }
    public ImmutableArray<WasmInstruction> Initializer { get; }

    public WasmGlobal(WasmGlobalType type, IEnumerable<WasmInstruction> initializer)
    {
        Type = type;
        Initializer = initializer?.ToImmutableArray() ?? ImmutableArray<WasmInstruction>.Empty;
    }
}

public sealed class WasmExport
{
    public string Name { get; }
    public WasmExternalKind Kind { get; }
    public uint Index { get; }

    public WasmExport(string name, WasmExternalKind kind, uint index)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Kind = kind;
        Index = index;
    }
}

public enum WasmSegmentMode : byte
{
    Active,
    Passive,
    Declarative,
}

public sealed class WasmElementSegment
{
    public WasmSegmentMode Mode { get; }
    public uint Table { get; }
    public ImmutableArray<WasmInstruction> Offset { get; }
    public WasmValueType ElementType { get; }

    public bool UsesExpressions { get; }
    public ImmutableArray<uint> Functions { get; }
    public ImmutableArray<ImmutableArray<WasmInstruction>> Expressions { get; }

    public WasmElementSegment(WasmSegmentMode mode, uint table, IEnumerable<WasmInstruction>? offset, IEnumerable<uint> functions)
    {
        Mode = mode;
        Table = table;
        Offset = offset?.ToImmutableArray() ?? ImmutableArray<WasmInstruction>.Empty;
        ElementType = WasmValueType.Reference(WasmHeapType.Func, false);
        Functions = functions?.ToImmutableArray() ?? ImmutableArray<uint>.Empty;
        Expressions = ImmutableArray<ImmutableArray<WasmInstruction>>.Empty;
    }

    public WasmElementSegment(WasmSegmentMode mode, uint table, IEnumerable<WasmInstruction>? offset, WasmValueType elementType, IEnumerable<ImmutableArray<WasmInstruction>> expressions)
    {
        Mode = mode;
        Table = table;
        Offset = offset?.ToImmutableArray() ?? ImmutableArray<WasmInstruction>.Empty;
        ElementType = elementType;
        UsesExpressions = true;
        Functions = ImmutableArray<uint>.Empty;
        Expressions = expressions?.ToImmutableArray() ?? ImmutableArray<ImmutableArray<WasmInstruction>>.Empty;
    }

    public int Count => UsesExpressions ? Expressions.Length : Functions.Length;
}

public sealed class WasmDataSegment
{
    public WasmSegmentMode Mode { get; }
    public uint Memory { get; }
    public ImmutableArray<WasmInstruction> Offset { get; }
    public ImmutableArray<byte> Bytes { get; }

    public WasmDataSegment(WasmSegmentMode mode, uint memory, IEnumerable<WasmInstruction>? offset, IEnumerable<byte> bytes)
    {
        if (mode == WasmSegmentMode.Declarative)
            throw new ArgumentException("A data segment is active or passive", nameof(mode));
        Mode = mode;
        Memory = memory;
        Offset = offset?.ToImmutableArray() ?? ImmutableArray<WasmInstruction>.Empty;
        Bytes = bytes?.ToImmutableArray() ?? ImmutableArray<byte>.Empty;
    }

    public static WasmDataSegment Active(ulong address, IEnumerable<byte> bytes, bool is64Bit = false, uint memory = 0)
        => new WasmDataSegment(
            WasmSegmentMode.Active,
            memory,
            new[] { is64Bit ? WasmInstruction.I64Const(unchecked((long)address)) : WasmInstruction.I32Const(unchecked((int)(uint)address)) },
            bytes);

    public static WasmDataSegment Passive(IEnumerable<byte> bytes)
        => new WasmDataSegment(WasmSegmentMode.Passive, 0, null, bytes);
}

public sealed class WasmCustomSection
{
    public string Name { get; }
    public ImmutableArray<byte> Content { get; }

    public byte After { get; }

    public WasmCustomSection(string name, IEnumerable<byte> content, byte after = 0)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Content = content?.ToImmutableArray() ?? ImmutableArray<byte>.Empty;
        After = after;
    }
}

public sealed class WasmNames
{
    public string? Module { get; set; }
    public Dictionary<uint, string> Functions { get; } = new Dictionary<uint, string>();
    public Dictionary<uint, Dictionary<uint, string>> Locals { get; } = new Dictionary<uint, Dictionary<uint, string>>();
    public Dictionary<uint, string> Types { get; } = new Dictionary<uint, string>();
    public Dictionary<uint, string> Tables { get; } = new Dictionary<uint, string>();
    public Dictionary<uint, string> Memories { get; } = new Dictionary<uint, string>();
    public Dictionary<uint, string> Globals { get; } = new Dictionary<uint, string>();
    public Dictionary<uint, string> Elements { get; } = new Dictionary<uint, string>();
    public Dictionary<uint, string> Data { get; } = new Dictionary<uint, string>();
    public Dictionary<uint, Dictionary<uint, string>> Fields { get; } = new Dictionary<uint, Dictionary<uint, string>>();
    public Dictionary<uint, string> Tags { get; } = new Dictionary<uint, string>();

    public bool IsEmpty
        => Module is null && Functions.Count == 0 && Locals.Count == 0 && Types.Count == 0 && Tables.Count == 0 && Memories.Count == 0
            && Globals.Count == 0 && Elements.Count == 0 && Data.Count == 0 && Fields.Count == 0 && Tags.Count == 0;

    public string? LocalName(uint function, uint local)
        => Locals.TryGetValue(function, out var names) && names.TryGetValue(local, out var name) ? name : null;

    public string? FieldName(uint type, uint field)
        => Fields.TryGetValue(type, out var names) && names.TryGetValue(field, out var name) ? name : null;

    public void SetLocalName(uint function, uint local, string name)
    {
        if (!Locals.TryGetValue(function, out var names))
            Locals[function] = names = new Dictionary<uint, string>();
        names[local] = name;
    }

    public void SetFieldName(uint type, uint field, string name)
    {
        if (!Fields.TryGetValue(type, out var names))
            Fields[type] = names = new Dictionary<uint, string>();
        names[field] = name;
    }
}

internal static class WasmSectionId
{
    public const byte Custom = 0;
    public const byte Type = 1;
    public const byte Import = 2;
    public const byte Function = 3;
    public const byte Table = 4;
    public const byte Memory = 5;
    public const byte Global = 6;
    public const byte Export = 7;
    public const byte Start = 8;
    public const byte Element = 9;
    public const byte Code = 10;
    public const byte Data = 11;
    public const byte DataCount = 12;
    public const byte Tag = 13;

    // The tag section sits between memory and global, and data count before code, so ids do not give the order
    public static int Order(byte id)
        => id switch
        {
            Type => 1,
            Import => 2,
            Function => 3,
            Table => 4,
            Memory => 5,
            Tag => 6,
            Global => 7,
            Export => 8,
            Start => 9,
            Element => 10,
            DataCount => 11,
            Code => 12,
            Data => 13,
            _ => 0,
        };
}

public static class WasmModuleWriter
{
    public static byte[] Write(WasmModule module)
    {
        if (module is null)
            throw new ArgumentNullException(nameof(module));
        var output = new WasmByteWriter();
        output.Bytes(new byte[] { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00 });
        var customs = new List<WasmCustomSection>(module.CustomSections);
        WriteCustoms(output, customs, 0);

        if (module.Types.Count != 0)
            Section(output, customs, WasmSectionId.Type, body =>
            {
                body.U32((uint)module.Types.Count);
                foreach (var group in module.Types)
                    WasmTypeEncoding.WriteRecursionGroup(body, group);
            });

        if (module.Imports.Count != 0)
            Section(output, customs, WasmSectionId.Import, body =>
            {
                body.U32((uint)module.Imports.Count);
                foreach (var import in module.Imports)
                {
                    body.Name(import.Module);
                    body.Name(import.Name);
                    body.Byte((byte)import.Kind);
                    switch (import.Kind)
                    {
                        case WasmExternalKind.Function: body.U32(import.TypeIndex); break;
                        case WasmExternalKind.Table: WasmTypeEncoding.WriteTableType(body, import.Table); break;
                        case WasmExternalKind.Memory: WasmTypeEncoding.WriteLimits(body, import.Memory.Limits); break;
                        case WasmExternalKind.Global: WasmTypeEncoding.WriteGlobalType(body, import.Global); break;
                        default: body.Byte(0); body.U32(import.TypeIndex); break;
                    }
                }
            });

        if (module.Functions.Count != 0)
            Section(output, customs, WasmSectionId.Function, body =>
            {
                body.U32((uint)module.Functions.Count);
                foreach (var function in module.Functions)
                    body.U32(function.TypeIndex);
            });

        if (module.Tables.Count != 0)
            Section(output, customs, WasmSectionId.Table, body =>
            {
                body.U32((uint)module.Tables.Count);
                foreach (var table in module.Tables)
                {
                    if (table.HasInitializer)
                    {
                        body.Byte(0x40);
                        body.Byte(0x00);
                        WasmTypeEncoding.WriteTableType(body, table.Type);
                        WriteExpression(body, table.Initializer);
                    }
                    else
                        WasmTypeEncoding.WriteTableType(body, table.Type);
                }
            });

        if (module.Memories.Count != 0)
            Section(output, customs, WasmSectionId.Memory, body =>
            {
                body.U32((uint)module.Memories.Count);
                foreach (var memory in module.Memories)
                    WasmTypeEncoding.WriteLimits(body, memory.Limits);
            });

        if (module.Tags.Count != 0)
            Section(output, customs, WasmSectionId.Tag, body =>
            {
                body.U32((uint)module.Tags.Count);
                foreach (var tag in module.Tags)
                {
                    body.Byte(0);
                    body.U32(tag.TypeIndex);
                }
            });

        if (module.Globals.Count != 0)
            Section(output, customs, WasmSectionId.Global, body =>
            {
                body.U32((uint)module.Globals.Count);
                foreach (var global in module.Globals)
                {
                    WasmTypeEncoding.WriteGlobalType(body, global.Type);
                    WriteExpression(body, global.Initializer);
                }
            });

        if (module.Exports.Count != 0)
            Section(output, customs, WasmSectionId.Export, body =>
            {
                body.U32((uint)module.Exports.Count);
                foreach (var export in module.Exports)
                {
                    body.Name(export.Name);
                    body.Byte((byte)export.Kind);
                    body.U32(export.Index);
                }
            });

        if (module.Start is uint start)
            Section(output, customs, WasmSectionId.Start, body => body.U32(start));

        if (module.Elements.Count != 0)
            Section(output, customs, WasmSectionId.Element, body =>
            {
                body.U32((uint)module.Elements.Count);
                foreach (var segment in module.Elements)
                    WriteElementSegment(body, segment);
            });

        if (module.Data.Count != 0)
            Section(output, customs, WasmSectionId.DataCount, body => body.U32((uint)module.Data.Count));

        if (module.Functions.Count != 0)
            Section(output, customs, WasmSectionId.Code, body =>
            {
                body.U32((uint)module.Functions.Count);
                foreach (var function in module.Functions)
                {
                    var code = new WasmByteWriter();
                    var runs = new List<(uint Count, WasmValueType Type)>();
                    foreach (var local in function.Locals)
                    {
                        if (runs.Count != 0 && runs[^1].Type == local)
                            runs[^1] = (runs[^1].Count + 1, local);
                        else
                            runs.Add((1, local));
                    }
                    code.U32((uint)runs.Count);
                    foreach (var (count, type) in runs)
                    {
                        code.U32(count);
                        WasmTypeEncoding.WriteValueType(code, type);
                    }
                    WriteExpression(code, function.Body);
                    body.Sized(code);
                }
            });

        if (module.Data.Count != 0)
            Section(output, customs, WasmSectionId.Data, body =>
            {
                body.U32((uint)module.Data.Count);
                foreach (var segment in module.Data)
                {
                    if (segment.Mode == WasmSegmentMode.Passive)
                        body.Byte(1);
                    else if (segment.Memory == 0)
                    {
                        body.Byte(0);
                        WriteExpression(body, segment.Offset);
                    }
                    else
                    {
                        body.Byte(2);
                        body.U32(segment.Memory);
                        WriteExpression(body, segment.Offset);
                    }
                    body.Vector(segment.Bytes.AsSpan());
                }
            });

        foreach (var custom in customs)
            WriteCustom(output, custom);
        if (!module.Names.IsEmpty)
        {
            var content = new WasmByteWriter();
            content.Name("name");
            WriteNames(content, module.Names);
            output.Byte(WasmSectionId.Custom);
            output.Sized(content);
        }
        return output.ToArray();
    }

    private static void Section(WasmByteWriter output, List<WasmCustomSection> customs, byte id, Action<WasmByteWriter> write)
    {
        var body = new WasmByteWriter();
        write(body);
        output.Byte(id);
        output.Sized(body);
        WriteCustoms(output, customs, id);
    }

    // A custom section whose neighbour is not written waits for the end of the module
    private static void WriteCustoms(WasmByteWriter output, List<WasmCustomSection> customs, byte after)
    {
        foreach (var custom in customs.Where(custom => custom.After == after).ToList())
        {
            WriteCustom(output, custom);
            customs.Remove(custom);
        }
    }

    private static void WriteCustom(WasmByteWriter output, WasmCustomSection custom)
    {
        var content = new WasmByteWriter();
        content.Name(custom.Name);
        content.Bytes(custom.Content.AsSpan());
        output.Byte(WasmSectionId.Custom);
        output.Sized(content);
    }

    internal static void WriteExpression(WasmByteWriter output, IEnumerable<WasmInstruction> instructions)
    {
        WasmCodeEncoder.Encode(output, instructions);
        output.Byte(0x0B);
    }

    private static void WriteElementSegment(WasmByteWriter body, WasmElementSegment segment)
    {
        bool plainTable = segment.Mode == WasmSegmentMode.Active && segment.Table == 0;
        if (!segment.UsesExpressions)
        {
            switch (segment.Mode)
            {
                case WasmSegmentMode.Active when plainTable:
                    body.U32(0);
                    WriteExpression(body, segment.Offset);
                    break;
                case WasmSegmentMode.Active:
                    body.U32(2);
                    body.U32(segment.Table);
                    WriteExpression(body, segment.Offset);
                    body.Byte(0);
                    break;
                case WasmSegmentMode.Passive:
                    body.U32(1);
                    body.Byte(0);
                    break;
                default:
                    body.U32(3);
                    body.Byte(0);
                    break;
            }
            body.U32((uint)segment.Functions.Length);
            foreach (var function in segment.Functions)
                body.U32(function);
            return;
        }

        switch (segment.Mode)
        {
            case WasmSegmentMode.Active when plainTable && segment.ElementType == WasmValueType.FuncRef:
                body.U32(4);
                WriteExpression(body, segment.Offset);
                break;
            case WasmSegmentMode.Active:
                body.U32(6);
                body.U32(segment.Table);
                WriteExpression(body, segment.Offset);
                WasmTypeEncoding.WriteValueType(body, segment.ElementType);
                break;
            case WasmSegmentMode.Passive:
                body.U32(5);
                WasmTypeEncoding.WriteValueType(body, segment.ElementType);
                break;
            default:
                body.U32(7);
                WasmTypeEncoding.WriteValueType(body, segment.ElementType);
                break;
        }
        body.U32((uint)segment.Expressions.Length);
        foreach (var expression in segment.Expressions)
            WriteExpression(body, expression);
    }

    private static void WriteNames(WasmByteWriter output, WasmNames names)
    {
        if (names.Module is not null)
            Subsection(output, 0, body => body.Name(names.Module));
        NameMap(output, 1, names.Functions);
        IndirectNameMap(output, 2, names.Locals);
        NameMap(output, 4, names.Types);
        NameMap(output, 5, names.Tables);
        NameMap(output, 6, names.Memories);
        NameMap(output, 7, names.Globals);
        NameMap(output, 8, names.Elements);
        NameMap(output, 9, names.Data);
        IndirectNameMap(output, 10, names.Fields);
        NameMap(output, 11, names.Tags);
    }

    private static void Subsection(WasmByteWriter output, byte id, Action<WasmByteWriter> write)
    {
        var body = new WasmByteWriter();
        write(body);
        output.Byte(id);
        output.Sized(body);
    }

    private static void NameMap(WasmByteWriter output, byte id, Dictionary<uint, string> map)
    {
        if (map.Count != 0)
            Subsection(output, id, body => WriteNameMap(body, map));
    }

    private static void WriteNameMap(WasmByteWriter body, Dictionary<uint, string> map)
    {
        body.U32((uint)map.Count);
        foreach (var pair in map.OrderBy(static pair => pair.Key))
        {
            body.U32(pair.Key);
            body.Name(pair.Value);
        }
    }

    private static void IndirectNameMap(WasmByteWriter output, byte id, Dictionary<uint, Dictionary<uint, string>> map)
    {
        var used = map.Where(static pair => pair.Value.Count != 0).OrderBy(static pair => pair.Key).ToList();
        if (used.Count == 0)
            return;
        Subsection(output, id, body =>
        {
            body.U32((uint)used.Count);
            foreach (var pair in used)
            {
                body.U32(pair.Key);
                WriteNameMap(body, pair.Value);
            }
        });
    }
}

public static class WasmModuleReader
{
    public static WasmModule Read(ReadOnlySpan<byte> bytes, WasmTarget? target = null)
    {
        var input = new WasmByteReader(bytes);
        var header = input.Bytes(8);
        if (header[0] != 0 || header[1] != 0x61 || header[2] != 0x73 || header[3] != 0x6D)
            throw new WasmBinaryException("The input is not a WebAssembly module", 0);
        if (header[4] != 1 || header[5] != 0 || header[6] != 0 || header[7] != 0)
            throw new WasmBinaryException("Only version 1 of the binary format is known", 4);

        var module = new WasmModule(target ?? Guess(bytes));
        var functionTypes = new List<uint>();
        uint? dataCount = null;
        int lastOrder = 0;
        byte lastId = 0;
        while (!input.AtEnd)
        {
            int sectionStart = input.Offset;
            byte id = input.Byte();
            int size = checked((int)input.U32());
            var body = new WasmByteReader(input.Bytes(size), input.Offset - size);
            if (id != WasmSectionId.Custom)
            {
                int order = WasmSectionId.Order(id);
                if (order == 0)
                    throw new WasmBinaryException($"Unknown section {id}", sectionStart);
                if (order <= lastOrder)
                    throw new WasmBinaryException($"Section {id} is out of order or repeated", sectionStart);
                lastOrder = order;
                lastId = id;
            }

            switch (id)
            {
                case WasmSectionId.Custom:
                {
                    string name = body.Name();
                    var content = body.Bytes(body.Length - body.Position);
                    if (name == "name")
                    {
                        // A malformed name section is only debugging information, so it is dropped rather than rejected
                        try
                        {
                            var names = new WasmByteReader(content);
                            ReadNames(ref names, module.Names);
                        }
                        catch (WasmBinaryException)
                        {
                        }
                    }
                    else
                        module.CustomSections.Add(new WasmCustomSection(name, content.ToArray(), lastId));
                    break;
                }
                case WasmSectionId.Type:
                {
                    uint count = body.Count();
                    for (uint i = 0; i < count; i++)
                        module.Types.Add(WasmTypeEncoding.ReadRecursionGroup(ref body));
                    break;
                }
                case WasmSectionId.Import:
                {
                    uint count = body.Count();
                    for (uint i = 0; i < count; i++)
                    {
                        string moduleName = body.Name();
                        string name = body.Name();
                        byte kind = body.Byte();
                        switch (kind)
                        {
                            case 0: module.Imports.Add(WasmImport.Function(moduleName, name, body.U32())); break;
                            case 1: module.Imports.Add(WasmImport.OfTable(moduleName, name, WasmTypeEncoding.ReadTableType(ref body))); break;
                            case 2: module.Imports.Add(WasmImport.OfMemory(moduleName, name, new WasmMemoryType(WasmTypeEncoding.ReadLimits(ref body)))); break;
                            case 3: module.Imports.Add(WasmImport.OfGlobal(moduleName, name, WasmTypeEncoding.ReadGlobalType(ref body))); break;
                            case 4:
                                if (body.Byte() != 0)
                                    throw body.Error("A tag has attribute 0");
                                module.Imports.Add(WasmImport.OfTag(moduleName, name, body.U32()));
                                break;
                            default:
                                throw body.Error($"Unknown import kind {kind}");
                        }
                    }
                    break;
                }
                case WasmSectionId.Function:
                {
                    uint count = body.Count();
                    for (uint i = 0; i < count; i++)
                        functionTypes.Add(body.U32());
                    break;
                }
                case WasmSectionId.Table:
                {
                    uint count = body.Count();
                    for (uint i = 0; i < count; i++)
                    {
                        if (body.Peek() == 0x40)
                        {
                            body.Byte();
                            if (body.Byte() != 0)
                                throw body.Error("A table with an initializer is written 0x40 0x00");
                            var type = WasmTypeEncoding.ReadTableType(ref body);
                            module.Tables.Add(new WasmTable(type, WasmCodeDecoder.DecodeExpression(ref body)));
                        }
                        else
                            module.Tables.Add(new WasmTable(WasmTypeEncoding.ReadTableType(ref body)));
                    }
                    break;
                }
                case WasmSectionId.Memory:
                {
                    uint count = body.Count();
                    for (uint i = 0; i < count; i++)
                        module.Memories.Add(new WasmMemoryType(WasmTypeEncoding.ReadLimits(ref body)));
                    break;
                }
                case WasmSectionId.Tag:
                {
                    uint count = body.Count();
                    for (uint i = 0; i < count; i++)
                    {
                        if (body.Byte() != 0)
                            throw body.Error("A tag has attribute 0");
                        module.Tags.Add(new WasmTag(body.U32()));
                    }
                    break;
                }
                case WasmSectionId.Global:
                {
                    uint count = body.Count();
                    for (uint i = 0; i < count; i++)
                    {
                        var type = WasmTypeEncoding.ReadGlobalType(ref body);
                        module.Globals.Add(new WasmGlobal(type, WasmCodeDecoder.DecodeExpression(ref body)));
                    }
                    break;
                }
                case WasmSectionId.Export:
                {
                    uint count = body.Count();
                    for (uint i = 0; i < count; i++)
                    {
                        string name = body.Name();
                        byte kind = body.Byte();
                        if (kind > 4)
                            throw body.Error($"Unknown export kind {kind}");
                        module.Exports.Add(new WasmExport(name, (WasmExternalKind)kind, body.U32()));
                    }
                    break;
                }
                case WasmSectionId.Start:
                    module.Start = body.U32();
                    break;
                case WasmSectionId.Element:
                {
                    uint count = body.Count();
                    for (uint i = 0; i < count; i++)
                        module.Elements.Add(ReadElementSegment(ref body));
                    break;
                }
                case WasmSectionId.DataCount:
                    dataCount = body.U32();
                    break;
                case WasmSectionId.Code:
                {
                    uint count = body.Count();
                    if (count != functionTypes.Count)
                        throw body.Error("The code section does not match the function section");
                    for (uint i = 0; i < count; i++)
                    {
                        int size2 = checked((int)body.U32());
                        var code = new WasmByteReader(body.Bytes(size2), body.Offset - size2);
                        var function = new WasmFunction(functionTypes[(int)i]);
                        uint runs = code.Count();
                        ulong total = 0;
                        for (uint r = 0; r < runs; r++)
                        {
                            uint repeat = code.U32();
                            total += repeat;
                            if (total > uint.MaxValue)
                                throw code.Error("A function has too many locals");
                            var type = WasmTypeEncoding.ReadValueType(ref code);
                            if (total > 50000)
                                throw code.Error("A function has more locals than engines accept");
                            for (uint k = 0; k < repeat; k++)
                                function.Locals.Add(type);
                        }
                        function.Body.AddRange(WasmCodeDecoder.DecodeExpression(ref code));
                        if (!code.AtEnd)
                            throw code.Error("A function body continues past its end");
                        module.Functions.Add(function);
                    }
                    break;
                }
                case WasmSectionId.Data:
                {
                    uint count = body.Count();
                    if (dataCount is uint expected && expected != count)
                        throw body.Error("The data section does not match the data count");
                    for (uint i = 0; i < count; i++)
                    {
                        uint flags = body.U32();
                        switch (flags)
                        {
                            case 0:
                            {
                                var offset = WasmCodeDecoder.DecodeExpression(ref body);
                                module.Data.Add(new WasmDataSegment(WasmSegmentMode.Active, 0, offset, body.Bytes(checked((int)body.U32())).ToArray()));
                                break;
                            }
                            case 1:
                                module.Data.Add(WasmDataSegment.Passive(body.Bytes(checked((int)body.U32())).ToArray()));
                                break;
                            case 2:
                            {
                                uint memory = body.U32();
                                var offset = WasmCodeDecoder.DecodeExpression(ref body);
                                module.Data.Add(new WasmDataSegment(WasmSegmentMode.Active, memory, offset, body.Bytes(checked((int)body.U32())).ToArray()));
                                break;
                            }
                            default:
                                throw body.Error($"Unknown data segment flags {flags}");
                        }
                    }
                    break;
                }
            }
            if (id != WasmSectionId.Custom && !body.AtEnd)
                throw body.Error($"Section {id} is longer than its contents");
        }
        if (functionTypes.Count != 0 && module.Functions.Count == 0)
            throw new WasmBinaryException("The function section has no code section", bytes.Length);
        if (dataCount is uint declared && declared != module.Data.Count)
            throw new WasmBinaryException("The data count does not match the data section", bytes.Length);
        return module;
    }

    // Only a 64-bit memory tells the two apart, so a module without one reads as wasm32
    private static WasmTarget Guess(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var module = Read(bytes, WasmTarget.Wasm32);
            bool is64 = module.Imports.Any(static import => import.Kind == WasmExternalKind.Memory && import.Memory.Limits.Is64Bit)
                || module.Memories.Any(static memory => memory.Limits.Is64Bit);
            return is64 ? WasmTarget.Wasm64 : WasmTarget.Wasm32;
        }
        catch (WasmBinaryException)
        {
            return WasmTarget.Wasm32;
        }
    }

    private static WasmElementSegment ReadElementSegment(ref WasmByteReader body)
    {
        uint flags = body.U32();
        if (flags > 7)
            throw body.Error($"Unknown element segment flags {flags}");
        var mode = (flags & 1) == 0 ? WasmSegmentMode.Active : (flags & 2) == 0 ? WasmSegmentMode.Passive : WasmSegmentMode.Declarative;
        uint table = 0;
        var offset = ImmutableArray<WasmInstruction>.Empty;
        if (mode == WasmSegmentMode.Active)
        {
            if ((flags & 2) != 0)
                table = body.U32();
            offset = WasmCodeDecoder.DecodeExpression(ref body);
        }
        bool expressions = (flags & 4) != 0;
        var elementType = WasmValueType.FuncRef;
        if ((flags & 3) != 0)
        {
            if (expressions)
                elementType = WasmTypeEncoding.ReadReferenceType(ref body);
            else if (body.Byte() != 0)
                throw body.Error("Only functions are listed by index");
        }
        uint count = body.Count();
        if (!expressions)
        {
            var functions = new uint[count];
            for (uint i = 0; i < count; i++)
                functions[i] = body.U32();
            return new WasmElementSegment(mode, table, offset, functions);
        }
        var items = new ImmutableArray<WasmInstruction>[count];
        for (uint i = 0; i < count; i++)
            items[i] = WasmCodeDecoder.DecodeExpression(ref body);
        return new WasmElementSegment(mode, table, offset, elementType, items);
    }

    private static void ReadNames(ref WasmByteReader input, WasmNames names)
    {
        int lastId = -1;
        while (!input.AtEnd)
        {
            byte id = input.Byte();
            int size = checked((int)input.U32());
            var body = new WasmByteReader(input.Bytes(size));
            if (id <= lastId)
                throw body.Error("Name subsections are out of order");
            lastId = id;
            switch (id)
            {
                case 0: names.Module = body.Name(); break;
                case 1: ReadNameMap(ref body, names.Functions); break;
                case 2: ReadIndirectNameMap(ref body, names.Locals); break;
                case 4: ReadNameMap(ref body, names.Types); break;
                case 5: ReadNameMap(ref body, names.Tables); break;
                case 6: ReadNameMap(ref body, names.Memories); break;
                case 7: ReadNameMap(ref body, names.Globals); break;
                case 8: ReadNameMap(ref body, names.Elements); break;
                case 9: ReadNameMap(ref body, names.Data); break;
                case 10: ReadIndirectNameMap(ref body, names.Fields); break;
                case 11: ReadNameMap(ref body, names.Tags); break;
            }
        }
    }

    private static void ReadNameMap(ref WasmByteReader body, Dictionary<uint, string> map)
    {
        uint count = body.Count();
        for (uint i = 0; i < count; i++)
        {
            uint index = body.U32();
            map[index] = body.Name();
        }
    }

    private static void ReadIndirectNameMap(ref WasmByteReader body, Dictionary<uint, Dictionary<uint, string>> map)
    {
        uint count = body.Count();
        for (uint i = 0; i < count; i++)
        {
            uint index = body.U32();
            var inner = new Dictionary<uint, string>();
            ReadNameMap(ref body, inner);
            map[index] = inner;
        }
    }
}
