using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Cnidaria.Wasm;

public sealed class WasmAssemblyWriterOptions
{
    public static WasmAssemblyWriterOptions Default { get; } = new WasmAssemblyWriterOptions();

    public bool UseNames { get; set; } = true;

    public bool IncludeIndexComments { get; set; } = true;
}

public sealed class WasmAssemblyException : FormatException
{
    public int Line { get; }
    public int Column { get; }

    public WasmAssemblyException(string message, int line, int column)
        : base($"{message} ({line}:{column})")
    {
        Line = line;
        Column = column;
    }
}

public static class WasmAssembler
{
    public static WasmModule Assemble(string text, WasmTarget? target = null)
        => new WasmAssemblyParser(text ?? throw new ArgumentNullException(nameof(text)), target).ReadModule();

    public static WasmModule Parse(string text, WasmTarget? target = null)
        => Assemble(text, target);
}

public static class WasmDisassembler
{
    public static string Disassemble(WasmModule module, WasmAssemblyWriterOptions? options = null)
        => WasmAssemblyWriter.Write(module, options);

    public static string Disassemble(ReadOnlySpan<byte> binary, WasmAssemblyWriterOptions? options = null)
        => WasmAssemblyWriter.Write(WasmModule.Decode(binary), options);

    public static string Disassemble(WasmProgram program, WasmAssemblyWriterOptions? options = null)
        => WasmAssemblyWriter.Write(program, options);

    public static string Disassemble(WasmCodeSection code, WasmAssemblyWriterOptions? options = null)
        => WasmAssemblyWriter.Write(code, options);

    public static string Disassemble(IEnumerable<WasmInstruction> instructions, WasmAssemblyWriterOptions? options = null)
        => WasmAssemblyWriter.Write(instructions, options);
}

internal static class WasmAssemblyWriter
{
    internal sealed class FormatContext
    {
        public WasmModule? Module { get; }
        public uint Function { get; }
        public bool UseNames { get; }
        public IReadOnlyList<string?>? LocalNames { get; }

        public FormatContext(WasmModule? module, uint function, bool useNames, IReadOnlyList<string?>? localNames = null)
        {
            Module = module;
            Function = function;
            UseNames = useNames;
            LocalNames = localNames;
        }

        public WasmNames? Names => UseNames ? Module?.Names : null;
    }

    public static string Write(WasmModule module, WasmAssemblyWriterOptions? options)
    {
        if (module is null)
            throw new ArgumentNullException(nameof(module));
        options ??= WasmAssemblyWriterOptions.Default;
        var context = new FormatContext(module, uint.MaxValue, options.UseNames);
        var names = context.Names ?? new WasmNames();
        var text = new StringBuilder();
        text.Append("(module");
        if (names.Module is not null)
            text.Append(' ').Append(Id(names.Module));
        text.Append('\n');

        uint typeIndex = 0;
        foreach (var group in module.Types)
        {
            bool rec = group.Types.Length != 1;
            if (rec)
                text.Append("  (rec\n");
            foreach (var type in group.Types)
            {
                text.Append(rec ? "    (type " : "  (type ");
                AppendDefinitionName(text, names.Types, typeIndex, options);
                AppendSubType(text, type, typeIndex, context);
                text.Append(")\n");
                typeIndex++;
            }
            if (rec)
                text.Append("  )\n");
        }

        uint functionIndex = 0, tableIndex = 0, memoryIndex = 0, globalIndex = 0, tagIndex = 0;
        foreach (var import in module.Imports)
        {
            text.Append("  (import ").Append(Quote(import.Module)).Append(' ').Append(Quote(import.Name)).Append(' ');
            switch (import.Kind)
            {
                case WasmExternalKind.Function:
                    text.Append("(func ");
                    AppendDefinitionName(text, names.Functions, functionIndex, options);
                    AppendTypeUse(text, import.TypeIndex, context, functionIndex);
                    functionIndex++;
                    break;
                case WasmExternalKind.Table:
                    text.Append("(table ");
                    AppendDefinitionName(text, names.Tables, tableIndex++, options);
                    AppendTableType(text, import.Table, context);
                    break;
                case WasmExternalKind.Memory:
                    text.Append("(memory ");
                    AppendDefinitionName(text, names.Memories, memoryIndex++, options);
                    AppendLimits(text, import.Memory.Limits);
                    break;
                case WasmExternalKind.Global:
                    text.Append("(global ");
                    AppendDefinitionName(text, names.Globals, globalIndex++, options);
                    AppendGlobalType(text, import.Global, context);
                    break;
                default:
                    text.Append("(tag ");
                    AppendDefinitionName(text, names.Tags, tagIndex++, options);
                    AppendTypeUse(text, import.TypeIndex, context, uint.MaxValue);
                    break;
            }
            text.Length = TrimEnd(text);
            text.Append("))\n");
        }

        foreach (var table in module.Tables)
        {
            text.Append("  (table ");
            AppendDefinitionName(text, names.Tables, tableIndex++, options);
            AppendTableType(text, table.Type, context);
            if (table.HasInitializer)
                AppendExpression(text.Append(' '), table.Initializer, context);
            text.Append(")\n");
        }
        foreach (var memory in module.Memories)
        {
            text.Append("  (memory ");
            AppendDefinitionName(text, names.Memories, memoryIndex++, options);
            AppendLimits(text, memory.Limits);
            text.Append(")\n");
        }
        foreach (var tag in module.Tags)
        {
            text.Append("  (tag ");
            AppendDefinitionName(text, names.Tags, tagIndex++, options);
            AppendTypeUse(text, tag.TypeIndex, context, uint.MaxValue);
            text.Length = TrimEnd(text);
            text.Append(")\n");
        }
        foreach (var global in module.Globals)
        {
            text.Append("  (global ");
            AppendDefinitionName(text, names.Globals, globalIndex++, options);
            AppendGlobalType(text, global.Type, context);
            text.Append(' ');
            AppendExpression(text, global.Initializer, context);
            text.Append(")\n");
        }
        foreach (var export in module.Exports)
        {
            text.Append("  (export ").Append(Quote(export.Name)).Append(" (").Append(KindName(export.Kind)).Append(' ');
            text.Append(Reference(context, export.Kind, export.Index)).Append("))\n");
        }
        if (module.Start is uint start)
            text.Append("  (start ").Append(Reference(context, WasmExternalKind.Function, start)).Append(")\n");

        for (int i = 0; i < module.Elements.Count; i++)
        {
            var segment = module.Elements[i];
            text.Append("  (elem ");
            AppendDefinitionName(text, names.Elements, (uint)i, options);
            if (segment.Mode == WasmSegmentMode.Declarative)
                text.Append("declare ");
            if (segment.Mode == WasmSegmentMode.Active)
            {
                if (segment.Table != 0)
                    text.Append("(table ").Append(Reference(context, WasmExternalKind.Table, segment.Table)).Append(") ");
                text.Append("(offset ");
                AppendExpression(text, segment.Offset, context);
                text.Append(") ");
            }
            if (segment.UsesExpressions)
            {
                text.Append(ValueTypeText(segment.ElementType, context));
                foreach (var item in segment.Expressions)
                {
                    text.Append(" (item ");
                    AppendExpression(text, item, context);
                    text.Append(')');
                }
            }
            else
            {
                text.Append("func");
                foreach (var function in segment.Functions)
                    text.Append(' ').Append(Reference(context, WasmExternalKind.Function, function));
            }
            text.Append(")\n");
        }

        functionIndex = (uint)module.ImportedFunctionCount;
        foreach (var function in module.Functions)
        {
            text.Append("  (func ");
            AppendDefinitionName(text, names.Functions, functionIndex, options);
            AppendTypeUse(text, function.TypeIndex, context, functionIndex);
            text.Length = TrimEnd(text);
            text.Append('\n');
            int parameters = module.TryGetFunctionType(function.TypeIndex, out var signature) ? signature.Parameters.Length : 0;
            for (int i = 0; i < function.Locals.Count; i++)
            {
                text.Append("    (local ");
                var localName = names.LocalName(functionIndex, (uint)(parameters + i));
                if (localName is not null)
                    text.Append(Id(localName)).Append(' ');
                text.Append(ValueTypeText(function.Locals[i], context)).Append(")\n");
            }
            AppendBody(text, function.Body, new FormatContext(module, functionIndex, options.UseNames), 2);
            text.Length = TrimEnd(text);
            text.Append(")\n");
            functionIndex++;
        }

        for (int i = 0; i < module.Data.Count; i++)
        {
            var segment = module.Data[i];
            text.Append("  (data ");
            AppendDefinitionName(text, names.Data, (uint)i, options);
            if (segment.Mode == WasmSegmentMode.Active)
            {
                if (segment.Memory != 0)
                    text.Append("(memory ").Append(Reference(context, WasmExternalKind.Memory, segment.Memory)).Append(") ");
                text.Append("(offset ");
                AppendExpression(text, segment.Offset, context);
                text.Append(") ");
            }
            text.Append(QuoteBytes(segment.Bytes.AsSpan())).Append(")\n");
        }
        text.Append(")\n");
        return text.ToString();
    }

    public static string Write(WasmProgram program, WasmAssemblyWriterOptions? options)
    {
        if (program is null)
            throw new ArgumentNullException(nameof(program));
        options ??= WasmAssemblyWriterOptions.Default;
        var context = new FormatContext(null, uint.MaxValue, options.UseNames);
        var text = new StringBuilder();
        text.Append(";; ").Append(program.Target).Append(" object\n");
        uint typeIndex = 0;
        foreach (var group in program.Types)
        {
            bool rec = group.Types.Length != 1;
            if (rec)
                text.Append("(rec\n");
            foreach (var type in group.Types)
            {
                text.Append(rec ? "  (type " : "(type ");
                if (options.IncludeIndexComments)
                    text.Append("(;").Append(typeIndex).Append(";) ");
                AppendSubType(text, type, typeIndex++, context);
                text.Append(")\n");
            }
            if (rec)
                text.Append(")\n");
        }
        foreach (var import in program.Imports)
        {
            text.Append("(import ").Append(Quote(import.Module)).Append(' ').Append(Quote(import.Field)).Append(' ');
            switch (import.Kind)
            {
                case WasmExternalKind.Function:
                    text.Append("(func ").Append(Id(import.SymbolName));
                    AppendSignature(text, import.Signature!, context, null);
                    break;
                case WasmExternalKind.Tag:
                    text.Append("(tag ").Append(Id(import.SymbolName));
                    AppendSignature(text, import.Signature!, context, null);
                    break;
                default:
                    text.Append("(global ").Append(Id(import.SymbolName)).Append(' ');
                    AppendGlobalType(text, import.Global, context);
                    break;
            }
            text.Append("))\n");
        }
        foreach (var global in program.Globals)
        {
            text.Append("(global ").Append(Id(global.Name)).Append(' ');
            AppendGlobalType(text, global.Type, context);
            text.Append(' ');
            AppendExpression(text, global.Initializer, context);
            text.Append(")\n");
        }
        foreach (var tag in program.Tags)
        {
            text.Append("(tag ").Append(Id(tag.Name));
            AppendSignature(text, tag.Signature, context, null);
            text.Append(")\n");
        }
        text.Append(Write(program.Code, options));
        foreach (var section in program.DataSections)
        {
            text.Append("(data ").Append(Id(section.Name)).Append(' ').Append(section.Kind.ToString().ToLowerInvariant())
                .Append(" align=").Append(section.Alignment);
            if (section.Kind == WasmObjectSectionKind.Bss)
                text.Append(" size=").Append(section.BssSize);
            else
                text.Append(' ').Append(QuoteBytes(section.Data.AsSpan()));
            text.Append(")\n");
            foreach (var relocation in section.Relocations)
            {
                text.Append(";; ").Append(relocation.Offset).Append(": ").Append(relocation.Kind).Append(' ').Append(relocation.SymbolName);
                if (relocation.Addend != 0)
                    text.Append(relocation.Addend > 0 ? "+" : "").Append(relocation.Addend);
                text.Append('\n');
            }
        }
        foreach (var symbol in program.Symbols)
        {
            if (symbol.Binding == WasmObjectSymbolBinding.Local || symbol.Kind == WasmObjectSymbolKind.Section)
                continue;
            text.Append(";; ").Append(symbol.Binding.ToString().ToLowerInvariant()).Append(' ').Append(symbol.Kind.ToString().ToLowerInvariant())
                .Append(' ').Append(symbol.Name);
            if (symbol.SectionName.Length != 0)
                text.Append(" in ").Append(symbol.SectionName).Append('+').Append(symbol.Offset);
            text.Append('\n');
        }
        foreach (var export in program.Exports)
            text.Append("(export ").Append(Quote(export.Name)).Append(' ').Append(Id(export.SymbolName)).Append(")\n");
        if (program.EntrySymbol.Length != 0)
            text.Append(";; entry ").Append(program.EntrySymbol).Append('\n');
        return text.ToString();
    }

    public static string Write(WasmCodeSection code, WasmAssemblyWriterOptions? options)
    {
        if (code is null)
            throw new ArgumentNullException(nameof(code));
        options ??= WasmAssemblyWriterOptions.Default;
        var text = new StringBuilder();
        foreach (var function in code.Functions)
        {
            var context = new FormatContext(null, 0, options.UseNames, function.LocalNames);
            text.Append("(func ").Append(Id(function.Name));
            AppendSignature(text, function.Signature, context, function.LocalNames);
            text.Append('\n');
            for (int i = 0; i < function.Locals.Length; i++)
            {
                text.Append("  (local ");
                int local = function.Signature.Parameters.Length + i;
                if (local < function.LocalNames.Length && function.LocalNames[local] is string name)
                    text.Append(Id(name)).Append(' ');
                text.Append(ValueTypeText(function.Locals[i], context)).Append(")\n");
            }
            AppendBody(text, function.Instructions, context, 1);
            text.Length = TrimEnd(text);
            text.Append(")\n");
        }
        return text.ToString();
    }

    public static string Write(IEnumerable<WasmInstruction> instructions, WasmAssemblyWriterOptions? options)
    {
        var text = new StringBuilder();
        AppendBody(text, instructions, new FormatContext(null, uint.MaxValue, (options ?? WasmAssemblyWriterOptions.Default).UseNames), 0);
        return text.ToString();
    }

    private static int TrimEnd(StringBuilder text)
    {
        int length = text.Length;
        while (length > 0 && text[length - 1] is ' ' or '\n')
            length--;
        return length;
    }

    private static void AppendBody(StringBuilder text, IEnumerable<WasmInstruction> body, FormatContext context, int depth)
    {
        foreach (var instruction in body)
        {
            if (instruction.Kind is WasmInstrKind.End or WasmInstrKind.Else)
                depth--;
            text.Append(' ', Math.Max(depth, 0) * 2).Append(FormatInstruction(instruction, context)).Append('\n');
            if (instruction.Kind is WasmInstrKind.Block or WasmInstrKind.Loop or WasmInstrKind.If or WasmInstrKind.Else or WasmInstrKind.TryTable)
                depth++;
        }
    }

    private static void AppendDefinitionName(StringBuilder text, Dictionary<uint, string> names, uint index, WasmAssemblyWriterOptions options)
    {
        if (options.UseNames && names.TryGetValue(index, out var name))
            text.Append(Id(name)).Append(' ');
        if (options.IncludeIndexComments)
            text.Append("(;").Append(index).Append(";) ");
    }

    private static void AppendSubType(StringBuilder text, WasmSubType type, uint index, FormatContext context)
    {
        if (!type.IsPlain)
        {
            text.Append("(sub ");
            if (type.Final)
                text.Append("final ");
            foreach (var supertype in type.Supertypes)
                text.Append(TypeReference(context, supertype)).Append(' ');
        }
        var composite = type.Composite;
        switch (composite.Kind)
        {
            case WasmCompositeKind.Function:
                text.Append("(func");
                AppendSignature(text, composite.Function!, context, null);
                text.Append(')');
                break;
            case WasmCompositeKind.Struct:
                text.Append("(struct");
                for (int i = 0; i < composite.Fields.Length; i++)
                {
                    text.Append(" (field ");
                    var name = context.Names?.FieldName(index, (uint)i);
                    if (name is not null)
                        text.Append(Id(name)).Append(' ');
                    text.Append(FieldTypeText(composite.Fields[i], context)).Append(')');
                }
                text.Append(')');
                break;
            default:
                text.Append("(array ").Append(FieldTypeText(composite.Element, context)).Append(')');
                break;
        }
        if (!type.IsPlain)
            text.Append(')');
    }

    private static string FieldTypeText(WasmFieldType field, FormatContext context)
    {
        string storage = field.Storage.IsPacked ? field.Storage.Name : ValueTypeText(field.Storage.ValueType, context);
        return field.Mutable ? "(mut " + storage + ")" : storage;
    }

    private static void AppendTypeUse(StringBuilder text, uint typeIndex, FormatContext context, uint function)
    {
        text.Append("(type ").Append(TypeReference(context, typeIndex)).Append(')');
        if (context.Module is not null && context.Module.TryGetFunctionType(typeIndex, out var signature))
        {
            var names = function != uint.MaxValue && context.Names is { } moduleNames
                ? Enumerable.Range(0, signature.Parameters.Length).Select(i => moduleNames.LocalName(function, (uint)i)).ToList()
                : null;
            AppendSignature(text, signature, context, names);
        }
        text.Append(' ');
    }

    private static void AppendSignature(StringBuilder text, WasmFunctionType type, FormatContext context, IReadOnlyList<string?>? names)
    {
        for (int i = 0; i < type.Parameters.Length; i++)
        {
            var name = names is not null && i < names.Count ? names[i] : null;
            text.Append(" (param ");
            if (name is not null)
                text.Append(Id(name)).Append(' ');
            text.Append(ValueTypeText(type.Parameters[i], context)).Append(')');
        }
        if (type.Results.Length != 0)
            text.Append(" (result ").Append(string.Join(" ", type.Results.Select(result => ValueTypeText(result, context)))).Append(')');
    }

    private static void AppendLimits(StringBuilder text, WasmLimits limits)
    {
        if (limits.Is64Bit)
            text.Append("i64 ");
        text.Append(limits.Minimum);
        if (limits.Maximum is ulong maximum)
            text.Append(' ').Append(maximum);
        if (limits.Shared)
            text.Append(" shared");
    }

    private static void AppendTableType(StringBuilder text, WasmTableType table, FormatContext context)
    {
        AppendLimits(text, table.Limits);
        text.Append(' ').Append(ValueTypeText(table.ElementType, context));
    }

    private static void AppendGlobalType(StringBuilder text, WasmGlobalType global, FormatContext context)
    {
        if (global.Mutable)
            text.Append("(mut ").Append(ValueTypeText(global.ValueType, context)).Append(')');
        else
            text.Append(ValueTypeText(global.ValueType, context));
    }

    private static void AppendExpression(StringBuilder text, ImmutableArray<WasmInstruction> expression, FormatContext context)
    {
        for (int i = 0; i < expression.Length; i++)
        {
            if (i != 0)
                text.Append(' ');
            text.Append('(').Append(FormatInstruction(expression[i], context)).Append(')');
        }
    }

    internal static string ValueTypeText(WasmValueType type, FormatContext? context)
    {
        if (!type.IsReference || !type.HeapType.IsConcrete)
            return type.Name;
        return "(ref " + (type.Nullable ? "null " : "") + HeapTypeText(type.HeapType, context) + ")";
    }

    private static string HeapTypeText(WasmHeapType heapType, FormatContext? context)
        => heapType.IsConcrete ? TypeReference(context, heapType.TypeIndex) : heapType.Name;

    private static string KindName(WasmExternalKind kind)
        => kind switch
        {
            WasmExternalKind.Function => "func",
            WasmExternalKind.Table => "table",
            WasmExternalKind.Memory => "memory",
            WasmExternalKind.Global => "global",
            _ => "tag",
        };

    private static string TypeReference(FormatContext? context, uint index)
        => context?.Names is { } names && names.Types.TryGetValue(index, out var name) ? Id(name) : index.ToString(CultureInfo.InvariantCulture);

    private static string Reference(FormatContext? context, WasmExternalKind kind, uint index)
    {
        if (context?.Names is { } names)
        {
            var map = kind switch
            {
                WasmExternalKind.Function => names.Functions,
                WasmExternalKind.Table => names.Tables,
                WasmExternalKind.Memory => names.Memories,
                WasmExternalKind.Global => names.Globals,
                _ => names.Tags,
            };
            if (map.TryGetValue(index, out var name))
                return Id(name);
        }
        return index.ToString(CultureInfo.InvariantCulture);
    }

    private static string SegmentReference(Dictionary<uint, string>? names, uint index)
        => names is not null && names.TryGetValue(index, out var name) ? Id(name) : index.ToString(CultureInfo.InvariantCulture);

    private static string FieldReference(FormatContext? context, uint type, uint field)
        => context?.Names?.FieldName(type, field) is string name ? Id(name) : field.ToString(CultureInfo.InvariantCulture);

    internal static string Id(string name)
    {
        foreach (char c in name)
        {
            if (!IsIdChar(c))
                return "$\"" + Escape(Encoding.UTF8.GetBytes(name)) + "\"";
        }
        return name.Length == 0 ? "$\"\"" : "$" + name;
    }

    internal static bool IsIdChar(char c)
        => c is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z'
            or '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '/' or ':' or '<' or '=' or '>' or '?' or '@' or '\\' or '^' or '_' or '`' or '|' or '~';

    private static string Quote(string text)
        => "\"" + Escape(Encoding.UTF8.GetBytes(text)) + "\"";

    private static string QuoteBytes(ReadOnlySpan<byte> bytes)
        => "\"" + Escape(bytes) + "\"";

    private static string Escape(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder();
        foreach (byte b in bytes)
        {
            if (b is >= 0x20 and < 0x7F && b != '"' && b != '\\')
                text.Append((char)b);
            else
                text.Append('\\').Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }
        return text.ToString();
    }

    public static string FormatInstruction(WasmInstruction instruction, FormatContext? context)
    {
        var metadata = instruction.Metadata;
        var module = context?.Module;
        var names = context?.Names;
        var text = new StringBuilder(metadata.Mnemonic);
        string Symbolic(string fallback) => instruction.HasSymbol ? Id(instruction.Symbol!) : fallback;

        switch (metadata.Format)
        {
            case WasmInstructionFormat.None:
                break;
            case WasmInstructionFormat.BlockType:
                AppendBlockType(text, instruction.BlockType, context);
                break;
            case WasmInstructionFormat.TryTable:
                AppendBlockType(text, instruction.BlockType, context);
                foreach (var handler in instruction.Catches)
                {
                    text.Append(" (").Append(handler.Kind switch
                    {
                        WasmCatchKind.Catch => "catch",
                        WasmCatchKind.CatchRef => "catch_ref",
                        WasmCatchKind.CatchAll => "catch_all",
                        _ => "catch_all_ref",
                    });
                    if (handler.HasTag)
                        text.Append(' ').Append(handler.TagSymbol is not null ? Id(handler.TagSymbol) : Reference(context, WasmExternalKind.Tag, handler.Tag));
                    text.Append(' ').Append(handler.Label).Append(')');
                }
                break;
            case WasmInstructionFormat.Label:
                text.Append(' ').Append(instruction.Index);
                break;
            case WasmInstructionFormat.LabelTable:
                foreach (var label in instruction.Labels)
                    text.Append(' ').Append(label);
                text.Append(' ').Append(instruction.Index);
                break;
            case WasmInstructionFormat.Function:
                text.Append(' ').Append(Symbolic(Reference(context, WasmExternalKind.Function, instruction.Index)));
                break;
            case WasmInstructionFormat.CallIndirect:
                if (instruction.Index2 != 0)
                    text.Append(' ').Append(Reference(context, WasmExternalKind.Table, instruction.Index2));
                if (instruction.Index != uint.MaxValue)
                {
                    text.Append(" (type ").Append(TypeReference(context, instruction.Index)).Append(')');
                    if (module is not null && module.TryGetFunctionType(instruction.Index, out var signature))
                        AppendSignature(text, signature, context!, null);
                }
                else if (instruction.Signature is not null)
                    AppendSignature(text, instruction.Signature, context ?? new FormatContext(null, uint.MaxValue, false), null);
                break;
            case WasmInstructionFormat.Type:
                text.Append(' ').Append(TypeReference(context, instruction.Index));
                break;
            case WasmInstructionFormat.TypeField:
                text.Append(' ').Append(TypeReference(context, instruction.Index)).Append(' ').Append(FieldReference(context, instruction.Index, instruction.Index2));
                break;
            case WasmInstructionFormat.TypeCount:
                text.Append(' ').Append(TypeReference(context, instruction.Index)).Append(' ').Append(instruction.Index2);
                break;
            case WasmInstructionFormat.TypeData:
                text.Append(' ').Append(TypeReference(context, instruction.Index)).Append(' ').Append(SegmentReference(names?.Data, instruction.Index2));
                break;
            case WasmInstructionFormat.TypeElement:
                text.Append(' ').Append(TypeReference(context, instruction.Index)).Append(' ').Append(SegmentReference(names?.Elements, instruction.Index2));
                break;
            case WasmInstructionFormat.TypeType:
                text.Append(' ').Append(TypeReference(context, instruction.Index)).Append(' ').Append(TypeReference(context, instruction.Index2));
                break;
            case WasmInstructionFormat.Local:
            {
                string? localName = null;
                if (context?.LocalNames is { } localNames && instruction.Index < localNames.Count)
                    localName = localNames[(int)instruction.Index];
                else if (context is not null && context.Function != uint.MaxValue)
                    localName = names?.LocalName(context.Function, instruction.Index);
                text.Append(' ').Append(localName is not null ? Id(localName) : instruction.Index.ToString(CultureInfo.InvariantCulture));
                break;
            }
            case WasmInstructionFormat.Global:
                text.Append(' ').Append(Symbolic(Reference(context, WasmExternalKind.Global, instruction.Index)));
                break;
            case WasmInstructionFormat.Tag:
                text.Append(' ').Append(Symbolic(Reference(context, WasmExternalKind.Tag, instruction.Index)));
                break;
            case WasmInstructionFormat.Table:
                if (instruction.Index != 0)
                    text.Append(' ').Append(Reference(context, WasmExternalKind.Table, instruction.Index));
                break;
            case WasmInstructionFormat.Memory:
                if (instruction.Index != 0)
                    text.Append(' ').Append(Reference(context, WasmExternalKind.Memory, instruction.Index));
                break;
            case WasmInstructionFormat.MemoryArgument:
            case WasmInstructionFormat.MemoryLane:
            {
                var argument = instruction.MemoryArgument;
                if (argument.Memory != 0)
                    text.Append(' ').Append(Reference(context, WasmExternalKind.Memory, argument.Memory));
                if (argument.Offset != 0)
                    text.Append(" offset=").Append(argument.Offset);
                if (argument.Alignment != metadata.NaturalAlignment)
                    text.Append(" align=").Append(argument.Alignment < 64 ? (1UL << (int)argument.Alignment).ToString(CultureInfo.InvariantCulture) : "?");
                if (metadata.Format == WasmInstructionFormat.MemoryLane)
                    text.Append(' ').Append(instruction.Lane);
                break;
            }
            case WasmInstructionFormat.I32:
                text.Append(' ').Append(Symbolic(instruction.I32.ToString(CultureInfo.InvariantCulture)));
                break;
            case WasmInstructionFormat.I64:
                text.Append(' ').Append(Symbolic(instruction.I64.ToString(CultureInfo.InvariantCulture)));
                break;
            case WasmInstructionFormat.F32:
                text.Append(' ').Append(FormatF32((uint)instruction.Value));
                break;
            case WasmInstructionFormat.F64:
                text.Append(' ').Append(FormatF64(instruction.Value));
                break;
            case WasmInstructionFormat.HeapType:
                text.Append(' ').Append(HeapTypeText(instruction.Type.HeapType, context));
                break;
            case WasmInstructionFormat.ReferenceType:
                text.Append(' ').Append(ValueTypeText(instruction.Type, context));
                break;
            case WasmInstructionFormat.BranchCast:
                text.Append(' ').Append(instruction.Index).Append(' ').Append(ValueTypeText(instruction.Type, context))
                    .Append(' ').Append(ValueTypeText(instruction.Type2, context));
                break;
            case WasmInstructionFormat.SelectTypes:
                text.Append(" (result ").Append(string.Join(" ", instruction.Types.Select(type => ValueTypeText(type, context)))).Append(')');
                break;
            case WasmInstructionFormat.Data:
                text.Append(' ').Append(SegmentReference(names?.Data, instruction.Index));
                break;
            case WasmInstructionFormat.Element:
                text.Append(' ').Append(SegmentReference(names?.Elements, instruction.Index));
                break;
            case WasmInstructionFormat.MemoryInit:
                if (instruction.Index2 != 0)
                    text.Append(' ').Append(Reference(context, WasmExternalKind.Memory, instruction.Index2));
                text.Append(' ').Append(SegmentReference(names?.Data, instruction.Index));
                break;
            case WasmInstructionFormat.TableInit:
                if (instruction.Index2 != 0)
                    text.Append(' ').Append(Reference(context, WasmExternalKind.Table, instruction.Index2));
                text.Append(' ').Append(SegmentReference(names?.Elements, instruction.Index));
                break;
            case WasmInstructionFormat.MemoryCopy:
                if (instruction.Index != 0 || instruction.Index2 != 0)
                    text.Append(' ').Append(Reference(context, WasmExternalKind.Memory, instruction.Index))
                        .Append(' ').Append(Reference(context, WasmExternalKind.Memory, instruction.Index2));
                break;
            case WasmInstructionFormat.TableCopy:
                if (instruction.Index != 0 || instruction.Index2 != 0)
                    text.Append(' ').Append(Reference(context, WasmExternalKind.Table, instruction.Index))
                        .Append(' ').Append(Reference(context, WasmExternalKind.Table, instruction.Index2));
                break;
            case WasmInstructionFormat.V128 when instruction.Bytes.Length != 16:
                text.Append(" (;malformed;)");
                break;
            case WasmInstructionFormat.V128:
                text.Append(" i32x4");
                for (int i = 0; i < 4; i++)
                {
                    uint lane = (uint)(instruction.Bytes[i * 4] | instruction.Bytes[i * 4 + 1] << 8 | instruction.Bytes[i * 4 + 2] << 16 | instruction.Bytes[i * 4 + 3] << 24);
                    text.Append(" 0x").Append(lane.ToString("x8", CultureInfo.InvariantCulture));
                }
                break;
            case WasmInstructionFormat.Shuffle:
                foreach (var lane in instruction.Bytes)
                    text.Append(' ').Append(lane);
                break;
            case WasmInstructionFormat.Lane:
                text.Append(' ').Append(instruction.Lane);
                break;
        }
        if (instruction.HasSymbol && instruction.Addend != 0)
            text.Append(" (;").Append(instruction.Addend > 0 ? "+" : "").Append(instruction.Addend).Append(";)");
        return text.ToString();
    }

    private static void AppendBlockType(StringBuilder text, WasmBlockType blockType, FormatContext? context)
    {
        if (blockType.Kind == WasmBlockTypeKind.Value)
            text.Append(" (result ").Append(ValueTypeText(blockType.ValueType, context)).Append(')');
        else if (blockType.Kind == WasmBlockTypeKind.Function)
        {
            var signature = blockType.Signature;
            if (!blockType.NeedsTypeIndex)
            {
                text.Append(" (type ").Append(TypeReference(context, blockType.TypeIndex)).Append(')');
                if (context?.Module is { } module && module.TryGetFunctionType(blockType.TypeIndex, out var type))
                    signature = type;
            }
            if (signature is not null)
                AppendSignature(text, signature, context ?? new FormatContext(null, uint.MaxValue, false), null);
        }
    }

    // Hexadecimal floats are exact, so a value always reads back to the same bits
    internal static string FormatF32(uint bits)
    {
        bool negative = (bits & 0x80000000u) != 0;
        uint exponent = (bits >> 23) & 0xFF;
        uint fraction = bits & 0x7FFFFF;
        string sign = negative ? "-" : "";
        if (exponent == 0xFF)
            return fraction == 0 ? sign + "inf" : fraction == 0x400000 ? sign + "nan" : sign + "nan:0x" + fraction.ToString("x", CultureInfo.InvariantCulture);
        if (exponent == 0 && fraction == 0)
            return sign + "0x0p+0";
        return sign + HexFloat(fraction, 23, exponent == 0 ? -126 : (int)exponent - 127, exponent != 0);
    }

    internal static string FormatF64(ulong bits)
    {
        bool negative = (bits & 0x8000000000000000UL) != 0;
        ulong exponent = (bits >> 52) & 0x7FF;
        ulong fraction = bits & 0xFFFFFFFFFFFFFUL;
        string sign = negative ? "-" : "";
        if (exponent == 0x7FF)
            return fraction == 0 ? sign + "inf" : fraction == 0x8000000000000UL ? sign + "nan" : sign + "nan:0x" + fraction.ToString("x", CultureInfo.InvariantCulture);
        if (exponent == 0 && fraction == 0)
            return sign + "0x0p+0";
        return sign + HexFloat(fraction, 52, exponent == 0 ? -1022 : (int)exponent - 1023, exponent != 0);
    }

    private static string HexFloat(ulong fraction, int fractionBits, int exponent, bool normal)
    {
        int digits = (fractionBits + 3) / 4;
        ulong aligned = fraction << (digits * 4 - fractionBits);
        string hex = aligned.ToString("x" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture).TrimEnd('0');
        var text = new StringBuilder(normal ? "0x1" : "0x0");
        if (hex.Length != 0)
            text.Append('.').Append(hex);
        text.Append('p').Append(exponent >= 0 ? "+" : "").Append(exponent);
        return text.ToString();
    }
}

internal sealed class WasmAssemblyParser
{
    private enum TokenKind : byte { Open, Close, Atom, String, Id }

    private readonly struct Token
    {
        public TokenKind Kind { get; }
        public string Text { get; }
        public byte[]? Bytes { get; }
        public int Line { get; }
        public int Column { get; }

        public Token(TokenKind kind, string text, byte[]? bytes, int line, int column)
        {
            Kind = kind;
            Text = text;
            Bytes = bytes;
            Line = line;
            Column = column;
        }
    }

    // A parenthesised list, or a single token when Children is null
    private sealed class Node
    {
        public Token Token { get; }
        public List<Node>? Children { get; }
        public bool IsList => Children is not null;
        public bool IsAtom => Children is null && Token.Kind == TokenKind.Atom;
        public string Head => Children is { Count: > 0 } && Children[0].Token.Kind == TokenKind.Atom && Children[0].Children is null ? Children[0].Token.Text : string.Empty;

        public Node(Token token, List<Node>? children)
        {
            Token = token;
            Children = children;
        }
    }

    private sealed class FunctionContext
    {
        public List<string?> LocalNames { get; }
        public List<string?> Labels { get; } = new List<string?>();

        public FunctionContext(List<string?> localNames)
        {
            LocalNames = localNames;
        }
    }

    private readonly string _text;
    private readonly WasmTarget? _target;
    private readonly List<Token> _tokens = new List<Token>();
    private WasmModule _module = null!;
    private readonly Dictionary<string, uint> _typeIds = new Dictionary<string, uint>(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _functionIds = new Dictionary<string, uint>(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _tableIds = new Dictionary<string, uint>(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _memoryIds = new Dictionary<string, uint>(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _globalIds = new Dictionary<string, uint>(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _tagIds = new Dictionary<string, uint>(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _elementIds = new Dictionary<string, uint>(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _dataIds = new Dictionary<string, uint>(StringComparer.Ordinal);
    private readonly Dictionary<uint, Dictionary<string, uint>> _fieldIds = new Dictionary<uint, Dictionary<string, uint>>();
    private uint _nextType;

    public WasmAssemblyParser(string text, WasmTarget? target)
    {
        _text = text;
        _target = target;
    }

    private static Exception Error(string message, Node node)
        => new WasmAssemblyException(message, node.Token.Line, node.Token.Column);

    public WasmModule ReadModule()
    {
        Tokenize();
        int position = 0;
        var roots = new List<Node>();
        while (position < _tokens.Count)
            roots.Add(ParseNode(ref position));

        List<Node> fields;
        string? moduleName = null;
        if (roots.Count == 1 && roots[0].Head == "module")
        {
            fields = roots[0].Children!.Skip(1).ToList();
            if (fields.Count != 0 && fields[0].Token.Kind == TokenKind.Id && !fields[0].IsList)
            {
                moduleName = fields[0].Token.Text;
                fields.RemoveAt(0);
            }
        }
        else
            fields = roots;

        // A module can be given as its binary encoding, spelt out in strings
        if (fields.Count != 0 && fields[0].IsAtom && fields[0].Token.Text == "binary")
        {
            var bytes = new List<byte>();
            foreach (var part in fields.Skip(1))
                bytes.AddRange(BytesValue(part));
            var decoded = WasmModule.Decode(bytes.ToArray(), _target);
            if (moduleName is not null)
                decoded.Names.Module = moduleName;
            return decoded;
        }

        foreach (var field in fields)
        {
            if (!field.IsList)
                throw Error("A module field is a parenthesised list", field);
        }
        bool is64 = _target?.Is64Bit ?? fields.Any(static field => field.Head == "memory" && field.Children!.Any(static child => child.IsAtom && child.Token.Text == "i64"));
        _module = new WasmModule(_target ?? (is64 ? WasmTarget.Wasm64 : WasmTarget.Wasm32));
        _module.Names.Module = moduleName;

        DeclareIndices(fields);
        // Explicit types take the first indices wherever they stand, and the implicit ones follow them
        foreach (var field in fields)
        {
            if (field.Head == "type")
                _module.Types.Add(new WasmRecursionGroup(ReadTypeDefinition(field)));
            else if (field.Head == "rec")
                _module.Types.Add(new WasmRecursionGroup(field.Children!.Skip(1).Select(ReadTypeDefinition).ToList()));
        }
        var pending = new List<(WasmFunction Function, Node Field, int BodyStart, List<string?> LocalNames)>();
        foreach (var field in fields)
        {
            if (field.Head is not "type" and not "rec")
                ReadField(field, pending);
        }
        foreach (var (function, field, bodyStart, localNames) in pending)
            ReadFunctionBody(function, field, bodyStart, localNames);

        SetNames(_typeIds, _module.Names.Types);
        SetNames(_functionIds, _module.Names.Functions);
        SetNames(_tableIds, _module.Names.Tables);
        SetNames(_memoryIds, _module.Names.Memories);
        SetNames(_globalIds, _module.Names.Globals);
        SetNames(_tagIds, _module.Names.Tags);
        SetNames(_elementIds, _module.Names.Elements);
        SetNames(_dataIds, _module.Names.Data);
        foreach (var (type, fieldIds) in _fieldIds)
        {
            foreach (var (name, field) in fieldIds)
                _module.Names.SetFieldName(type, field, name);
        }
        return _module;
    }

    private static void SetNames(Dictionary<string, uint> ids, Dictionary<uint, string> names)
    {
        foreach (var (name, index) in ids)
            names[index] = name;
    }

    // Every index space is numbered in the order the text defines it, which puts imports first as the format requires
    private void DeclareIndices(List<Node> fields)
    {
        uint types = 0, functions = 0, tables = 0, memories = 0, globals = 0, tags = 0, elements = 0, data = 0;
        foreach (var field in fields)
        {
            var children = field.Children!;
            string? id = IdAt(children, 1);
            switch (field.Head)
            {
                case "type": Declare(_typeIds, id, types++, field); break;
                case "rec":
                    foreach (var type in children.Skip(1))
                    {
                        if (type.Head != "type")
                            throw Error("A recursion group holds type definitions", type);
                        Declare(_typeIds, IdAt(type.Children!, 1), types++, type);
                    }
                    break;
                case "func": Declare(_functionIds, id, functions++, field); break;
                case "table": Declare(_tableIds, id, tables++, field); break;
                case "memory": Declare(_memoryIds, id, memories++, field); break;
                case "global": Declare(_globalIds, id, globals++, field); break;
                case "tag": Declare(_tagIds, id, tags++, field); break;
                case "elem": Declare(_elementIds, id, elements++, field); break;
                case "data": Declare(_dataIds, id, data++, field); break;
                case "import":
                {
                    if (children.Count < 4 || !children[3].IsList)
                        throw Error("An import names a module, a field and a description", field);
                    var description = children[3];
                    string? importId = IdAt(description.Children!, 1);
                    switch (description.Head)
                    {
                        case "func": Declare(_functionIds, importId, functions++, description); break;
                        case "table": Declare(_tableIds, importId, tables++, description); break;
                        case "memory": Declare(_memoryIds, importId, memories++, description); break;
                        case "global": Declare(_globalIds, importId, globals++, description); break;
                        case "tag": Declare(_tagIds, importId, tags++, description); break;
                        default: throw Error($"Unknown import kind {description.Head}", description);
                    }
                    break;
                }
            }
        }

        void Declare(Dictionary<string, uint> ids, string? id, uint index, Node node)
        {
            if (id is not null && !ids.TryAdd(id, index))
                throw Error($"${id} is defined twice", node);
        }
    }

    private static string? IdAt(List<Node> children, int at)
        => at < children.Count && !children[at].IsList && children[at].Token.Kind == TokenKind.Id ? children[at].Token.Text : null;

    private WasmSubType ReadTypeDefinition(Node field)
    {
        if (field.Head != "type")
            throw Error("A type definition was expected", field);
        uint index = _nextType++;
        var children = field.Children!;
        int at = 1;
        OptionalId(children, ref at);
        if (at != children.Count - 1 || !children[at].IsList)
            throw Error("A type definition holds one type", field);
        var node = children[at];
        if (node.Head != "sub")
            return WasmSubType.Of(ReadComposite(node, index));
        var inner = node.Children!;
        int position = 1;
        bool final = false;
        if (position < inner.Count && inner[position].IsAtom && inner[position].Token.Text == "final")
        {
            final = true;
            position++;
        }
        var supertypes = new List<uint>();
        while (position < inner.Count && !inner[position].IsList)
            supertypes.Add(Resolve(_typeIds, inner[position++], "type"));
        if (position != inner.Count - 1)
            throw Error("A subtype ends with its composite type", node);
        return new WasmSubType(final, supertypes, ReadComposite(inner[position], index));
    }

    private WasmCompositeType ReadComposite(Node node, uint typeIndex)
    {
        var children = node.Children ?? throw Error("A composite type was expected", node);
        switch (node.Head)
        {
            case "func":
            {
                int at = 1;
                var signature = ReadSignature(children, ref at, null);
                if (at != children.Count)
                    throw Error("A function type holds only parameters and results", children[at]);
                return WasmCompositeType.Of(signature);
            }
            case "struct":
            {
                var fields = new List<WasmFieldType>();
                foreach (var field in children.Skip(1))
                {
                    if (field.Head != "field")
                        throw Error("A struct type holds fields", field);
                    var parts = field.Children!;
                    if (IdAt(parts, 1) is string id)
                    {
                        if (parts.Count != 3)
                            throw Error("A named field has one type", field);
                        if (!_fieldIds.TryGetValue(typeIndex, out var ids))
                            _fieldIds[typeIndex] = ids = new Dictionary<string, uint>(StringComparer.Ordinal);
                        if (!ids.TryAdd(id, (uint)fields.Count))
                            throw Error($"${id} names two fields", field);
                        fields.Add(FieldType(parts[2]));
                    }
                    else
                    {
                        foreach (var part in parts.Skip(1))
                            fields.Add(FieldType(part));
                    }
                }
                return WasmCompositeType.Struct(fields);
            }
            case "array":
                if (children.Count != 2)
                    throw Error("An array type has one element type", node);
                return WasmCompositeType.Array(FieldType(children[1]));
            default:
                throw Error($"Unknown composite type {node.Head}", node);
        }
    }

    private WasmFieldType FieldType(Node node)
    {
        if (node.Head == "mut")
        {
            if (node.Children!.Count != 2)
                throw Error("mut wraps one type", node);
            return new WasmFieldType(StorageType(node.Children[1]), true);
        }
        return new WasmFieldType(StorageType(node), false);
    }

    private WasmStorageType StorageType(Node node)
    {
        if (node.IsAtom && node.Token.Text == "i8")
            return WasmStorageType.I8;
        if (node.IsAtom && node.Token.Text == "i16")
            return WasmStorageType.I16;
        return ValueType(node);
    }

    private bool IsValueType(Node node)
        => node.IsList ? node.Head == "ref" : node.IsAtom && WasmValueType.TryParse(node.Token.Text, out _);

    private WasmValueType ValueType(Node node)
    {
        if (node.IsList)
        {
            if (node.Head != "ref")
                throw Error("A value type was expected", node);
            var children = node.Children!;
            int at = 1;
            bool nullable = false;
            if (at < children.Count && children[at].IsAtom && children[at].Token.Text == "null")
            {
                nullable = true;
                at++;
            }
            if (at != children.Count - 1)
                throw Error("A reference type names one heap type", node);
            return WasmValueType.Reference(HeapType(children[at]), nullable);
        }
        if (!node.IsAtom || !WasmValueType.TryParse(node.Token.Text, out var type))
            throw Error("A value type was expected", node);
        return type;
    }

    private WasmHeapType HeapType(Node node)
    {
        if (node.IsAtom && WasmHeapType.TryParse(node.Token.Text, out var heapType))
            return heapType;
        return WasmHeapType.Concrete(Resolve(_typeIds, node, "type"));
    }

    private void ReadField(Node field, List<(WasmFunction, Node, int, List<string?>)> pending)
    {
        var children = field.Children!;
        switch (field.Head)
        {
            case "import":
                ReadImport(field);
                break;
            case "func":
                ReadFunction(field, pending);
                break;
            case "table":
                ReadTable(field);
                break;
            case "memory":
                ReadMemory(field);
                break;
            case "global":
                ReadGlobal(field);
                break;
            case "tag":
                ReadTag(field);
                break;
            case "export":
            {
                if (children.Count != 3 || children[1].Token.Kind != TokenKind.String || !children[2].IsList)
                    throw Error("An export names itself and what it exports", field);
                var target = children[2];
                var kind = ExternalKind(target);
                _module.Exports.Add(new WasmExport(StringValue(children[1]), kind, ResolveExternal(kind, target.Children![1])));
                break;
            }
            case "start":
                _module.Start = Resolve(_functionIds, children[1], "function");
                break;
            case "elem":
                ReadElement(field);
                break;
            case "data":
                ReadData(field);
                break;
            default:
                throw Error($"Unknown module field {field.Head}", field);
        }
    }

    private WasmExternalKind ExternalKind(Node node)
        => node.Head switch
        {
            "func" => WasmExternalKind.Function,
            "table" => WasmExternalKind.Table,
            "memory" => WasmExternalKind.Memory,
            "global" => WasmExternalKind.Global,
            "tag" => WasmExternalKind.Tag,
            _ => throw Error($"Unknown external kind {node.Head}", node),
        };

    private uint ResolveExternal(WasmExternalKind kind, Node node)
        => kind switch
        {
            WasmExternalKind.Function => Resolve(_functionIds, node, "function"),
            WasmExternalKind.Table => Resolve(_tableIds, node, "table"),
            WasmExternalKind.Memory => Resolve(_memoryIds, node, "memory"),
            WasmExternalKind.Global => Resolve(_globalIds, node, "global"),
            _ => Resolve(_tagIds, node, "tag"),
        };

    private void ReadImport(Node field)
    {
        var children = field.Children!;
        string module = StringValue(children[1]);
        string name = StringValue(children[2]);
        var description = children[3];
        var inner = description.Children!;
        int at = 1;
        OptionalId(inner, ref at);
        switch (description.Head)
        {
            case "func":
            {
                var localNames = new List<string?>();
                uint index = _module.AddImport(WasmImport.Function(module, name, ReadTypeUse(inner, ref at, localNames)));
                SetLocalNames(index, localNames);
                break;
            }
            case "table":
                _module.AddImport(WasmImport.OfTable(module, name, ReadTableType(inner, ref at)));
                break;
            case "memory":
                _module.AddImport(WasmImport.OfMemory(module, name, new WasmMemoryType(ReadLimits(inner, ref at))));
                break;
            case "global":
                _module.AddImport(WasmImport.OfGlobal(module, name, ReadGlobalType(inner, ref at)));
                break;
            default:
                _module.AddImport(WasmImport.OfTag(module, name, ReadTypeUse(inner, ref at, null)));
                break;
        }
        if (at != inner.Count)
            throw Error("An import description has more than it needs", inner[at]);
    }

    private List<string> ReadInlineExports(List<Node> children, ref int at, out (string Module, string Name)? import)
    {
        var exports = new List<string>();
        import = null;
        while (at < children.Count && children[at].IsList)
        {
            var node = children[at];
            if (node.Head == "export")
            {
                exports.Add(StringValue(node.Children![1]));
                at++;
            }
            else if (node.Head == "import")
            {
                import = (StringValue(node.Children![1]), StringValue(node.Children[2]));
                at++;
            }
            else
                break;
        }
        return exports;
    }

    private void AddExports(List<string> exports, WasmExternalKind kind, uint index)
    {
        foreach (var export in exports)
            _module.Exports.Add(new WasmExport(export, kind, index));
    }

    private void SetLocalNames(uint function, List<string?> localNames)
    {
        for (int i = 0; i < localNames.Count; i++)
        {
            if (localNames[i] is string localName)
                _module.Names.SetLocalName(function, (uint)i, localName);
        }
    }

    private void ReadFunction(Node field, List<(WasmFunction, Node, int, List<string?>)> pending)
    {
        var children = field.Children!;
        int at = 1;
        OptionalId(children, ref at);
        var exports = ReadInlineExports(children, ref at, out var import);
        var localNames = new List<string?>();
        uint typeIndex = ReadTypeUse(children, ref at, localNames);
        uint index;
        if (import is var (moduleName, name))
            index = _module.AddImport(WasmImport.Function(moduleName, name, typeIndex));
        else
        {
            var function = new WasmFunction(typeIndex);
            while (at < children.Count && children[at].Head == "local")
            {
                var local = children[at].Children!;
                if (IdAt(local, 1) is string id)
                {
                    if (local.Count != 3)
                        throw Error("A named local has one type", children[at]);
                    localNames.Add(id);
                    function.Locals.Add(ValueType(local[2]));
                }
                else
                {
                    for (int inner = 1; inner < local.Count; inner++)
                    {
                        localNames.Add(null);
                        function.Locals.Add(ValueType(local[inner]));
                    }
                }
                at++;
            }
            _module.Functions.Add(function);
            index = (uint)(_module.FunctionCount - 1);
            pending.Add((function, field, at, localNames));
        }
        SetLocalNames(index, localNames);
        AddExports(exports, WasmExternalKind.Function, index);
    }

    private void ReadTable(Node field)
    {
        var children = field.Children!;
        int at = 1;
        OptionalId(children, ref at);
        var exports = ReadInlineExports(children, ref at, out var import);
        uint index;
        bool is64 = at < children.Count && children[at].IsAtom && children[at].Token.Text == "i64";
        int typeAt = at + (is64 ? 1 : 0);
        if (import is var (moduleName, name))
            index = _module.AddImport(WasmImport.OfTable(moduleName, name, ReadTableType(children, ref at)));
        else if (typeAt + 1 < children.Count && IsValueType(children[typeAt]) && children[typeAt + 1].Head == "elem")
        {
            var elementType = ValueType(children[typeAt]);
            var items = children[typeAt + 1].Children!;
            var functions = new List<uint>();
            var expressions = new List<ImmutableArray<WasmInstruction>>();
            for (int i = 1; i < items.Count; i++)
            {
                if (items[i].IsList)
                    expressions.Add(ReadConstantExpression(items[i].Head == "item" ? items[i].Children!.Skip(1).ToList() : new List<Node> { items[i] }));
                else
                    functions.Add(Resolve(_functionIds, items[i], "function"));
            }
            ulong count = (ulong)(functions.Count + expressions.Count);
            _module.Tables.Add(new WasmTable(new WasmTableType(elementType, new WasmLimits(count, count, false, is64))));
            index = (uint)(_module.TableCount - 1);
            var offset = new[] { is64 ? WasmInstruction.I64Const(0) : WasmInstruction.I32Const(0) };
            if (expressions.Count != 0)
            {
                expressions.AddRange(functions.Select(function => ImmutableArray.Create(WasmInstruction.RefFunc(function))));
                _module.Elements.Add(new WasmElementSegment(WasmSegmentMode.Active, index, offset, elementType, expressions));
            }
            else
                _module.Elements.Add(new WasmElementSegment(WasmSegmentMode.Active, index, offset, functions));
            at = children.Count;
        }
        else
        {
            var type = ReadTableType(children, ref at);
            var initializer = at < children.Count ? ReadConstantExpression(children.Skip(at).ToList()) : ImmutableArray<WasmInstruction>.Empty;
            at = children.Count;
            _module.Tables.Add(new WasmTable(type, initializer));
            index = (uint)(_module.TableCount - 1);
        }
        if (at != children.Count)
            throw Error("A table definition has more than it needs", children[at]);
        AddExports(exports, WasmExternalKind.Table, index);
    }

    private void ReadMemory(Node field)
    {
        var children = field.Children!;
        int at = 1;
        OptionalId(children, ref at);
        var exports = ReadInlineExports(children, ref at, out var import);
        uint index;
        bool is64 = at < children.Count && children[at].IsAtom && children[at].Token.Text == "i64";
        int dataAt = at + (is64 ? 1 : 0);
        if (import is var (moduleName, name))
            index = _module.AddImport(WasmImport.OfMemory(moduleName, name, new WasmMemoryType(ReadLimits(children, ref at))));
        else if (dataAt < children.Count && children[dataAt].Head == "data")
        {
            var bytes = new List<byte>();
            foreach (var part in children[dataAt].Children!.Skip(1))
                bytes.AddRange(BytesValue(part));
            ulong pages = ((ulong)bytes.Count + WasmMemoryType.PageSize - 1) / WasmMemoryType.PageSize;
            _module.Memories.Add(new WasmMemoryType(new WasmLimits(pages, pages, false, is64)));
            index = (uint)(_module.MemoryCount - 1);
            _module.Data.Add(WasmDataSegment.Active(0, bytes, is64, index));
            at = dataAt + 1;
        }
        else
        {
            _module.Memories.Add(new WasmMemoryType(ReadLimits(children, ref at)));
            index = (uint)(_module.MemoryCount - 1);
        }
        if (at != children.Count)
            throw Error("A memory definition has more than it needs", children[at]);
        AddExports(exports, WasmExternalKind.Memory, index);
    }

    private void ReadGlobal(Node field)
    {
        var children = field.Children!;
        int at = 1;
        OptionalId(children, ref at);
        var exports = ReadInlineExports(children, ref at, out var import);
        var type = ReadGlobalType(children, ref at);
        uint index;
        if (import is var (moduleName, name))
            index = _module.AddImport(WasmImport.OfGlobal(moduleName, name, type));
        else
        {
            _module.Globals.Add(new WasmGlobal(type, ReadConstantExpression(children.Skip(at).ToList())));
            index = (uint)(_module.GlobalCount - 1);
        }
        AddExports(exports, WasmExternalKind.Global, index);
    }

    private void ReadTag(Node field)
    {
        var children = field.Children!;
        int at = 1;
        OptionalId(children, ref at);
        var exports = ReadInlineExports(children, ref at, out var import);
        uint typeIndex = ReadTypeUse(children, ref at, null);
        if (at != children.Count)
            throw Error("A tag definition has more than it needs", children[at]);
        uint index;
        if (import is var (moduleName, name))
            index = _module.AddImport(WasmImport.OfTag(moduleName, name, typeIndex));
        else
        {
            _module.Tags.Add(new WasmTag(typeIndex));
            index = (uint)(_module.TagCount - 1);
        }
        AddExports(exports, WasmExternalKind.Tag, index);
    }

    private void ReadElement(Node field)
    {
        var children = field.Children!;
        int at = 1;
        OptionalId(children, ref at);
        var mode = WasmSegmentMode.Passive;
        uint table = 0;
        var offset = ImmutableArray<WasmInstruction>.Empty;
        if (at < children.Count && children[at].IsAtom && children[at].Token.Text == "declare")
        {
            mode = WasmSegmentMode.Declarative;
            at++;
        }
        if (at < children.Count && children[at].Head == "table")
        {
            table = Resolve(_tableIds, children[at].Children![1], "table");
            at++;
        }
        else if (at + 1 < children.Count && !children[at].IsList && (children[at].Token.Kind == TokenKind.Id || IsInteger(children[at].Token.Text)) && children[at + 1].IsList)
        {
            // The oldest form names the table before the offset
            table = Resolve(_tableIds, children[at], "table");
            at++;
        }
        if (at < children.Count && children[at].IsList && (children[at].Head == "offset" || IsInstructionName(children[at].Head)))
        {
            mode = WasmSegmentMode.Active;
            var node = children[at];
            offset = ReadConstantExpression(node.Head == "offset" ? node.Children!.Skip(1).ToList() : new List<Node> { node });
            at++;
        }

        var elementType = WasmValueType.FuncRef;
        bool expressions = false;
        if (at < children.Count && children[at].IsAtom && children[at].Token.Text == "func")
            at++;
        else if (at < children.Count && IsValueType(children[at]))
        {
            elementType = ValueType(children[at]);
            expressions = true;
            at++;
        }
        var functions = new List<uint>();
        var items = new List<ImmutableArray<WasmInstruction>>();
        for (; at < children.Count; at++)
        {
            var node = children[at];
            if (node.IsList)
            {
                expressions = true;
                items.Add(ReadConstantExpression(node.Head == "item" ? node.Children!.Skip(1).ToList() : new List<Node> { node }));
            }
            else
                functions.Add(Resolve(_functionIds, node, "function"));
        }
        if (expressions)
        {
            items.AddRange(functions.Select(function => ImmutableArray.Create(WasmInstruction.RefFunc(function))));
            _module.Elements.Add(new WasmElementSegment(mode, table, offset, elementType, items));
        }
        else
            _module.Elements.Add(new WasmElementSegment(mode, table, offset, functions));
    }

    private void ReadData(Node field)
    {
        var children = field.Children!;
        int at = 1;
        OptionalId(children, ref at);
        uint memory = 0;
        var mode = WasmSegmentMode.Passive;
        var offset = ImmutableArray<WasmInstruction>.Empty;
        if (at < children.Count && children[at].Head == "memory")
        {
            memory = Resolve(_memoryIds, children[at].Children![1], "memory");
            at++;
        }
        else if (at < children.Count && !children[at].IsList && children[at].Token.Kind != TokenKind.String)
        {
            memory = Resolve(_memoryIds, children[at], "memory");
            at++;
        }
        if (at < children.Count && children[at].IsList)
        {
            mode = WasmSegmentMode.Active;
            var node = children[at];
            offset = ReadConstantExpression(node.Head == "offset" ? node.Children!.Skip(1).ToList() : new List<Node> { node });
            at++;
        }
        var bytes = new List<byte>();
        for (; at < children.Count; at++)
            bytes.AddRange(BytesValue(children[at]));
        _module.Data.Add(new WasmDataSegment(mode, memory, offset, bytes));
    }

    private WasmTableType ReadTableType(List<Node> children, ref int at)
    {
        var limits = ReadLimits(children, ref at);
        if (at >= children.Count)
            throw Error("A table type ends with its element type", children[^1]);
        var type = ValueType(children[at++]);
        if (!type.IsReference)
            throw Error("A table holds references", children[at - 1]);
        return new WasmTableType(type, limits);
    }

    private WasmLimits ReadLimits(List<Node> children, ref int at)
    {
        bool is64 = false;
        if (at < children.Count && children[at].IsAtom && children[at].Token.Text is "i64" or "i32")
        {
            is64 = children[at].Token.Text == "i64";
            at++;
        }
        if (at >= children.Count || children[at].IsList)
            throw Error("Limits start with a minimum", at < children.Count ? children[at] : children[^1]);
        ulong minimum = ParseUnsigned(children[at++]);
        ulong? maximum = null;
        if (at < children.Count && children[at].IsAtom && IsInteger(children[at].Token.Text))
            maximum = ParseUnsigned(children[at++]);
        bool shared = false;
        if (at < children.Count && children[at].IsAtom && children[at].Token.Text == "shared")
        {
            shared = true;
            at++;
        }
        return new WasmLimits(minimum, maximum, shared, is64);
    }

    private WasmGlobalType ReadGlobalType(List<Node> children, ref int at)
    {
        if (at >= children.Count)
            throw Error("A global type was expected", children[^1]);
        var node = children[at++];
        if (node.Head == "mut")
            return new WasmGlobalType(ValueType(node.Children![1]), true);
        return new WasmGlobalType(ValueType(node), false);
    }

    private uint ReadTypeUse(List<Node> children, ref int at, List<string?>? parameterNames)
    {
        uint? explicitIndex = null;
        if (at < children.Count && children[at].Head == "type")
        {
            explicitIndex = Resolve(_typeIds, children[at].Children![1], "type");
            at++;
        }
        int start = at;
        var signature = ReadSignature(children, ref at, parameterNames);
        if (explicitIndex is uint index)
        {
            if (!_module.TryGetFunctionType(index, out var declared))
                throw Error($"Type {index} is not a function type", children[start - 1]);
            if (at != start && !declared.Equals(signature))
                throw Error("The inline signature disagrees with the type it names", children[start]);
            if (at == start && parameterNames is not null)
            {
                for (int i = 0; i < declared.Parameters.Length; i++)
                    parameterNames.Add(null);
            }
            return index;
        }
        return _module.InternType(signature);
    }

    private WasmFunctionType ReadSignature(List<Node> children, ref int at, List<string?>? parameterNames)
    {
        var parameters = new List<WasmValueType>();
        var results = new List<WasmValueType>();
        while (at < children.Count && children[at].Head == "param")
        {
            var param = children[at].Children!;
            if (IdAt(param, 1) is string id)
            {
                if (param.Count != 3)
                    throw Error("A named parameter has one type", children[at]);
                parameterNames?.Add(id);
                parameters.Add(ValueType(param[2]));
            }
            else
            {
                for (int i = 1; i < param.Count; i++)
                {
                    parameterNames?.Add(null);
                    parameters.Add(ValueType(param[i]));
                }
            }
            at++;
        }
        while (at < children.Count && children[at].Head == "result")
        {
            foreach (var result in children[at].Children!.Skip(1))
                results.Add(ValueType(result));
            at++;
        }
        return new WasmFunctionType(parameters, results);
    }

    private static string? OptionalId(List<Node> children, ref int at)
    {
        if (IdAt(children, at) is string id)
        {
            at++;
            return id;
        }
        return null;
    }

    private uint Resolve(Dictionary<string, uint> ids, Node node, string what)
    {
        if (node.IsList)
            throw Error($"A {what} index was expected", node);
        if (node.Token.Kind == TokenKind.Id)
            return ids.TryGetValue(node.Token.Text, out var index) ? index : throw Error($"Unknown {what} ${node.Token.Text}", node);
        return checked((uint)ParseUnsigned(node));
    }

    private ImmutableArray<WasmInstruction> ReadConstantExpression(List<Node> nodes)
    {
        var context = new FunctionContext(new List<string?>());
        var output = new List<WasmInstruction>();
        int at = 0;
        ReadInstructions(nodes, ref at, context, output);
        if (context.Labels.Count != 0)
            throw Error("A block is left open in a constant expression", nodes[^1]);
        return output.ToImmutableArray();
    }

    private void ReadFunctionBody(WasmFunction function, Node field, int bodyStart, List<string?> localNames)
    {
        var children = field.Children!;
        var context = new FunctionContext(localNames);
        int at = bodyStart;
        ReadInstructions(children, ref at, context, function.Body);
        if (context.Labels.Count != 0)
            throw Error("A block is left open at the end of the function", field);
    }

    private static bool IsInstructionName(string name)
        => WasmInstructionTable.TryGet(name, out _) || name is "then" or "else";

    private static WasmInstrKind StructuredKind(string name)
        => name switch
        {
            "block" => WasmInstrKind.Block,
            "loop" => WasmInstrKind.Loop,
            "if" => WasmInstrKind.If,
            _ => WasmInstrKind.TryTable,
        };

    private void ReadInstructions(List<Node> nodes, ref int at, FunctionContext context, List<WasmInstruction> output)
    {
        while (at < nodes.Count)
        {
            var node = nodes[at];
            if (node.IsList)
            {
                ReadFolded(node, context, output);
                at++;
                continue;
            }
            if (node.Token.Kind != TokenKind.Atom)
                throw Error("An instruction was expected", node);
            string name = node.Token.Text;
            at++;
            switch (name)
            {
                case "block":
                case "loop":
                case "if":
                case "try_table":
                {
                    string? label = OptionalId(nodes, ref at);
                    output.Add(ReadStructured(StructuredKind(name), nodes, ref at, context));
                    context.Labels.Add(label);
                    break;
                }
                case "else":
                    OptionalId(nodes, ref at);
                    output.Add(WasmInstruction.Else());
                    break;
                case "end":
                    OptionalId(nodes, ref at);
                    if (context.Labels.Count == 0)
                        throw Error("An end closes no block", node);
                    context.Labels.RemoveAt(context.Labels.Count - 1);
                    output.Add(WasmInstruction.End());
                    break;
                default:
                    output.Add(ReadPlain(name, node, nodes, ref at, context));
                    break;
            }
        }
    }

    // The block type and, for try_table, the handlers, whose labels count from outside the new block
    private WasmInstruction ReadStructured(WasmInstrKind kind, List<Node> nodes, ref int at, FunctionContext context)
    {
        var blockType = ReadBlockType(nodes, ref at);
        if (kind != WasmInstrKind.TryTable)
            return WasmInstruction.Structured(kind, blockType);
        var catches = new List<WasmCatch>();
        while (at < nodes.Count && nodes[at].Head is "catch" or "catch_ref" or "catch_all" or "catch_all_ref")
        {
            var handler = nodes[at++];
            var parts = handler.Children!;
            var catchKind = handler.Head switch
            {
                "catch" => WasmCatchKind.Catch,
                "catch_ref" => WasmCatchKind.CatchRef,
                "catch_all" => WasmCatchKind.CatchAll,
                _ => WasmCatchKind.CatchAllRef,
            };
            bool hasTag = catchKind is WasmCatchKind.Catch or WasmCatchKind.CatchRef;
            if (parts.Count != (hasTag ? 3 : 2))
                throw Error($"{handler.Head} names {(hasTag ? "a tag and " : "")}a label", handler);
            uint tag = hasTag ? Resolve(_tagIds, parts[1], "tag") : 0;
            catches.Add(new WasmCatch(catchKind, tag, Label(parts[^1], context)));
        }
        return WasmInstruction.TryTable(blockType, catches);
    }

    private void ReadFolded(Node node, FunctionContext context, List<WasmInstruction> output)
    {
        var children = node.Children!;
        string head = node.Head;
        int at = 1;
        if (head is "block" or "loop" or "try_table")
        {
            string? label = OptionalId(children, ref at);
            output.Add(ReadStructured(StructuredKind(head), children, ref at, context));
            context.Labels.Add(label);
            ReadInstructions(children, ref at, context, output);
            context.Labels.RemoveAt(context.Labels.Count - 1);
            output.Add(WasmInstruction.End());
            return;
        }
        if (head == "if")
        {
            string? label = OptionalId(children, ref at);
            var blockType = ReadBlockType(children, ref at);
            while (at < children.Count && children[at].Head != "then")
                ReadFolded(children[at++], context, output);
            context.Labels.Add(label);
            output.Add(WasmInstruction.If(blockType));
            if (at < children.Count)
            {
                var then = children[at++].Children!;
                int inner = 1;
                ReadInstructions(then, ref inner, context, output);
            }
            if (at < children.Count && children[at].Head == "else")
            {
                output.Add(WasmInstruction.Else());
                var otherwise = children[at++].Children!;
                int inner = 1;
                ReadInstructions(otherwise, ref inner, context, output);
            }
            if (at != children.Count)
                throw Error("A folded if ends with its then and else", children[at]);
            context.Labels.RemoveAt(context.Labels.Count - 1);
            output.Add(WasmInstruction.End());
            return;
        }
        if (children.Count == 0 || children[0].Token.Kind != TokenKind.Atom)
            throw Error("A folded instruction starts with its name", node);
        var instruction = ReadPlain(head, children[0], children, ref at, context);
        for (; at < children.Count; at++)
        {
            if (!children[at].IsList)
                throw Error("An operand of a folded instruction is itself folded", children[at]);
            ReadFolded(children[at], context, output);
        }
        output.Add(instruction);
    }

    private WasmBlockType ReadBlockType(List<Node> nodes, ref int at)
    {
        if (at < nodes.Count && nodes[at].Head == "type")
            return WasmBlockType.OfType(ReadTypeUse(nodes, ref at, null));
        int start = at;
        var signature = ReadSignature(nodes, ref at, null);
        if (at == start)
            return WasmBlockType.Empty;
        if (signature.Parameters.Length == 0 && signature.Results.Length <= 1)
            return WasmBlockType.OfSignature(signature);
        return WasmBlockType.OfType(_module.InternType(signature));
    }

    private static bool NextIsIndex(List<Node> nodes, int at)
        => at < nodes.Count && !nodes[at].IsList && (nodes[at].Token.Kind == TokenKind.Id || nodes[at].Token.Kind == TokenKind.Atom && IsInteger(nodes[at].Token.Text));

    private Node Next(List<Node> nodes, ref int at, Node after)
        => at < nodes.Count ? nodes[at++] : throw Error($"{after.Token.Text} is missing an immediate", after);

    private WasmInstruction ReadPlain(string name, Node nameNode, List<Node> nodes, ref int at, FunctionContext context)
    {
        if (!WasmInstructionTable.TryGet(name, out var metadata))
            throw Error($"Unknown instruction {name}", nameNode);
        var kind = metadata.Kind;
        switch (metadata.Format)
        {
            case WasmInstructionFormat.None:
                if (kind == WasmInstrKind.Select && at < nodes.Count && nodes[at].Head == "result")
                {
                    var types = new List<WasmValueType>();
                    while (at < nodes.Count && nodes[at].Head == "result")
                        types.AddRange(nodes[at++].Children!.Skip(1).Select(ValueType));
                    return WasmInstruction.Create(WasmInstrKind.SelectTyped, types: types.ToImmutableArray());
                }
                return WasmInstruction.Simple(kind);
            case WasmInstructionFormat.Label:
                return WasmInstruction.WithIndex(kind, Label(Next(nodes, ref at, nameNode), context));
            case WasmInstructionFormat.LabelTable:
            {
                var labels = new List<uint>();
                while (NextIsIndex(nodes, at))
                    labels.Add(Label(nodes[at++], context));
                if (labels.Count == 0)
                    throw Error("br_table needs a default target", nameNode);
                uint fallback = labels[^1];
                labels.RemoveAt(labels.Count - 1);
                return WasmInstruction.BrTable(labels, fallback);
            }
            case WasmInstructionFormat.Function:
                return WasmInstruction.WithIndex(kind, Resolve(_functionIds, Next(nodes, ref at, nameNode), "function"));
            case WasmInstructionFormat.CallIndirect:
            {
                uint table = NextIsIndex(nodes, at) ? Resolve(_tableIds, nodes[at++], "table") : 0;
                uint type = ReadTypeUse(nodes, ref at, null);
                return WasmInstruction.WithIndices(kind, type, table);
            }
            case WasmInstructionFormat.Type:
                return WasmInstruction.WithIndex(kind, Resolve(_typeIds, Next(nodes, ref at, nameNode), "type"));
            case WasmInstructionFormat.TypeField:
            {
                uint type = Resolve(_typeIds, Next(nodes, ref at, nameNode), "type");
                var fieldNode = Next(nodes, ref at, nameNode);
                uint field;
                if (fieldNode.Token.Kind == TokenKind.Id && !fieldNode.IsList)
                {
                    if (!_fieldIds.TryGetValue(type, out var ids) || !ids.TryGetValue(fieldNode.Token.Text, out field))
                        throw Error($"Type {type} has no field ${fieldNode.Token.Text}", fieldNode);
                }
                else
                    field = checked((uint)ParseUnsigned(fieldNode));
                return WasmInstruction.WithIndices(kind, type, field);
            }
            case WasmInstructionFormat.TypeCount:
            {
                uint type = Resolve(_typeIds, Next(nodes, ref at, nameNode), "type");
                return WasmInstruction.WithIndices(kind, type, checked((uint)ParseUnsigned(Next(nodes, ref at, nameNode))));
            }
            case WasmInstructionFormat.TypeData:
            {
                uint type = Resolve(_typeIds, Next(nodes, ref at, nameNode), "type");
                return WasmInstruction.WithIndices(kind, type, Resolve(_dataIds, Next(nodes, ref at, nameNode), "data segment"));
            }
            case WasmInstructionFormat.TypeElement:
            {
                uint type = Resolve(_typeIds, Next(nodes, ref at, nameNode), "type");
                return WasmInstruction.WithIndices(kind, type, Resolve(_elementIds, Next(nodes, ref at, nameNode), "element segment"));
            }
            case WasmInstructionFormat.TypeType:
            {
                uint destination = Resolve(_typeIds, Next(nodes, ref at, nameNode), "type");
                return WasmInstruction.WithIndices(kind, destination, Resolve(_typeIds, Next(nodes, ref at, nameNode), "type"));
            }
            case WasmInstructionFormat.Local:
            {
                var node = Next(nodes, ref at, nameNode);
                if (node.Token.Kind == TokenKind.Id && !node.IsList)
                {
                    int local = context.LocalNames.IndexOf(node.Token.Text);
                    if (local < 0)
                        throw Error($"Unknown local ${node.Token.Text}", node);
                    return WasmInstruction.WithIndex(kind, (uint)local);
                }
                return WasmInstruction.WithIndex(kind, checked((uint)ParseUnsigned(node)));
            }
            case WasmInstructionFormat.Global:
                return WasmInstruction.WithIndex(kind, Resolve(_globalIds, Next(nodes, ref at, nameNode), "global"));
            case WasmInstructionFormat.Tag:
                return WasmInstruction.WithIndex(kind, Resolve(_tagIds, Next(nodes, ref at, nameNode), "tag"));
            case WasmInstructionFormat.Table:
                return WasmInstruction.WithIndex(kind, NextIsIndex(nodes, at) ? Resolve(_tableIds, nodes[at++], "table") : 0);
            case WasmInstructionFormat.Memory:
                return WasmInstruction.WithIndex(kind, NextIsIndex(nodes, at) ? Resolve(_memoryIds, nodes[at++], "memory") : 0);
            case WasmInstructionFormat.MemoryArgument:
            case WasmInstructionFormat.MemoryLane:
            {
                bool lane = metadata.Format == WasmInstructionFormat.MemoryLane;
                uint memory = 0;
                if (NextIsIndex(nodes, at) && (!lane || NextIsIndex(nodes, at + 1) || IsMemoryArgumentAtom(nodes, at + 1)))
                    memory = Resolve(_memoryIds, nodes[at++], "memory");
                ulong offset = 0;
                uint alignment = metadata.NaturalAlignment;
                while (IsMemoryArgumentAtom(nodes, at))
                {
                    string text = nodes[at].Token.Text;
                    if (text.StartsWith("offset=", StringComparison.Ordinal))
                        offset = ParseUnsignedText(text.Substring(7), nodes[at]);
                    else
                    {
                        ulong bytes = ParseUnsignedText(text.Substring(6), nodes[at]);
                        if (bytes == 0 || (bytes & (bytes - 1)) != 0)
                            throw Error("An alignment is a power of two", nodes[at]);
                        alignment = (uint)BitOperations.Log2(bytes);
                    }
                    at++;
                }
                var argument = new WasmMemoryArgument(alignment, offset, memory);
                if (lane)
                    return WasmInstruction.Create(kind, index2: checked((uint)ParseUnsigned(Next(nodes, ref at, nameNode))), memoryArgument: argument);
                return WasmInstruction.Create(kind, memoryArgument: argument);
            }
            case WasmInstructionFormat.I32:
                return WasmInstruction.I32Const(unchecked((int)ParseInteger(Next(nodes, ref at, nameNode), 32)));
            case WasmInstructionFormat.I64:
                return WasmInstruction.I64Const(ParseInteger(Next(nodes, ref at, nameNode), 64));
            case WasmInstructionFormat.F32:
                return WasmInstruction.F32ConstBits((uint)ParseFloat(Next(nodes, ref at, nameNode), false));
            case WasmInstructionFormat.F64:
                return WasmInstruction.F64ConstBits(ParseFloat(Next(nodes, ref at, nameNode), true));
            case WasmInstructionFormat.HeapType:
                return WasmInstruction.RefNull(HeapType(Next(nodes, ref at, nameNode)));
            case WasmInstructionFormat.ReferenceType:
            {
                var type = ValueType(Next(nodes, ref at, nameNode));
                if (!type.IsReference)
                    throw Error($"{name} names a reference type", nameNode);
                return kind is WasmInstrKind.RefTest or WasmInstrKind.RefTestNull ? WasmInstruction.RefTest(type) : WasmInstruction.RefCast(type);
            }
            case WasmInstructionFormat.BranchCast:
            {
                uint label = Label(Next(nodes, ref at, nameNode), context);
                var from = ValueType(Next(nodes, ref at, nameNode));
                var to = ValueType(Next(nodes, ref at, nameNode));
                if (!from.IsReference || !to.IsReference)
                    throw Error($"{name} casts between reference types", nameNode);
                return WasmInstruction.Create(kind, label, type: from, type2: to);
            }
            case WasmInstructionFormat.Data:
                return WasmInstruction.WithIndex(kind, Resolve(_dataIds, Next(nodes, ref at, nameNode), "data segment"));
            case WasmInstructionFormat.Element:
                return WasmInstruction.WithIndex(kind, Resolve(_elementIds, Next(nodes, ref at, nameNode), "element segment"));
            case WasmInstructionFormat.MemoryInit:
            {
                uint memory = 0;
                if (NextIsIndex(nodes, at + 1))
                    memory = Resolve(_memoryIds, nodes[at++], "memory");
                return WasmInstruction.WithIndices(kind, Resolve(_dataIds, Next(nodes, ref at, nameNode), "data segment"), memory);
            }
            case WasmInstructionFormat.TableInit:
            {
                uint table = 0;
                if (NextIsIndex(nodes, at + 1))
                    table = Resolve(_tableIds, nodes[at++], "table");
                return WasmInstruction.WithIndices(kind, Resolve(_elementIds, Next(nodes, ref at, nameNode), "element segment"), table);
            }
            case WasmInstructionFormat.MemoryCopy:
            case WasmInstructionFormat.TableCopy:
            {
                var ids = metadata.Format == WasmInstructionFormat.MemoryCopy ? _memoryIds : _tableIds;
                if (!NextIsIndex(nodes, at))
                    return WasmInstruction.WithIndices(kind, 0, 0);
                uint destination = Resolve(ids, nodes[at++], "index");
                uint source = Resolve(ids, Next(nodes, ref at, nameNode), "index");
                return WasmInstruction.WithIndices(kind, destination, source);
            }
            case WasmInstructionFormat.V128:
                return ReadV128(nodes, ref at, nameNode);
            case WasmInstructionFormat.Shuffle:
            {
                var lanes = new byte[16];
                for (int i = 0; i < 16; i++)
                    lanes[i] = checked((byte)ParseUnsigned(Next(nodes, ref at, nameNode)));
                return WasmInstruction.Shuffle(lanes);
            }
            case WasmInstructionFormat.Lane:
                return WasmInstruction.LaneOp(kind, checked((int)ParseUnsigned(Next(nodes, ref at, nameNode))));
            default:
                throw Error($"{name} cannot be written here", nameNode);
        }
    }

    private static bool IsMemoryArgumentAtom(List<Node> nodes, int at)
        => at < nodes.Count && nodes[at].IsAtom && (nodes[at].Token.Text.StartsWith("offset=", StringComparison.Ordinal) || nodes[at].Token.Text.StartsWith("align=", StringComparison.Ordinal));

    private WasmInstruction ReadV128(List<Node> nodes, ref int at, Node nameNode)
    {
        var shapeNode = Next(nodes, ref at, nameNode);
        string shape = shapeNode.Token.Text;
        var bytes = new byte[16];
        int lanes = shape switch { "i8x16" => 16, "i16x8" => 8, "i32x4" => 4, "i64x2" => 2, "f32x4" => 4, "f64x2" => 2, _ => 0 };
        if (lanes == 0 || shapeNode.IsList)
            throw Error("v128.const names a shape", shapeNode);
        int width = 16 / lanes;
        for (int lane = 0; lane < lanes; lane++)
        {
            var node = Next(nodes, ref at, nameNode);
            ulong value = shape switch
            {
                "f32x4" => ParseFloat(node, false),
                "f64x2" => ParseFloat(node, true),
                _ => unchecked((ulong)ParseInteger(node, width * 8)),
            };
            for (int b = 0; b < width; b++)
                bytes[lane * width + b] = (byte)(value >> (b * 8));
        }
        return WasmInstruction.V128Const(bytes);
    }

    private uint Label(Node node, FunctionContext context)
    {
        if (node.Token.Kind == TokenKind.Id && !node.IsList)
        {
            for (int depth = 0; depth < context.Labels.Count; depth++)
            {
                if (context.Labels[context.Labels.Count - 1 - depth] == node.Token.Text)
                    return (uint)depth;
            }
            throw Error($"Unknown label ${node.Token.Text}", node);
        }
        return checked((uint)ParseUnsigned(node));
    }

    private static bool IsInteger(string text)
    {
        int i = text.StartsWith('+') || text.StartsWith('-') ? 1 : 0;
        if (i >= text.Length)
            return false;
        if (text.AsSpan(i).StartsWith("0x", StringComparison.Ordinal))
            return text.Length > i + 2 && text.AsSpan(i + 2).IndexOfAnyExcept("0123456789abcdefABCDEF_") < 0;
        return text.AsSpan(i).IndexOfAnyExcept("0123456789_") < 0 && char.IsDigit(text[i]);
    }

    private ulong ParseUnsigned(Node node)
        => node.IsList ? throw Error("A number was expected", node) : ParseUnsignedText(node.Token.Text, node);

    private ulong ParseUnsignedText(string text, Node node)
    {
        if (!TryParseMagnitude(text, out var value, out bool negative) || negative || text.StartsWith('+') || value > ulong.MaxValue)
            throw Error($"{text} is not an unsigned integer", node);
        return (ulong)value;
    }

    // Integers in the text format take either reading of their bits, so i32.const accepts -1 and 0xffffffff alike
    private long ParseInteger(Node node, int bits)
    {
        if (node.IsList || !TryParseMagnitude(node.Token.Text, out var magnitude, out bool negative))
            throw Error("An integer was expected", node);
        var limit = BigInteger.One << bits;
        if (negative ? magnitude > limit / 2 : magnitude >= limit)
            throw Error($"{node.Token.Text} does not fit {bits} bits", node);
        var value = negative ? (limit - magnitude) % limit : magnitude;
        return unchecked((long)(ulong)(value & ulong.MaxValue));
    }

    private static bool TryParseMagnitude(string text, out BigInteger value, out bool negative)
    {
        value = BigInteger.Zero;
        negative = text.StartsWith('-');
        int i = text.StartsWith('+') || negative ? 1 : 0;
        bool hex = text.AsSpan(i).StartsWith("0x", StringComparison.Ordinal);
        if (hex)
            i += 2;
        if (i >= text.Length)
            return false;
        int radix = hex ? 16 : 10;
        bool previousDigit = false;
        for (; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '_')
            {
                if (!previousDigit)
                    return false;
                previousDigit = false;
                continue;
            }
            int digit = HexDigit(c);
            if (digit < 0 || digit >= radix)
                return false;
            value = value * radix + digit;
            previousDigit = true;
        }
        return previousDigit;
    }

    private static int HexDigit(char c)
        => c is >= '0' and <= '9' ? c - '0' : c is >= 'a' and <= 'f' ? c - 'a' + 10 : c is >= 'A' and <= 'F' ? c - 'A' + 10 : -1;

    private ulong ParseFloat(Node node, bool isDouble)
    {
        if (node.IsList)
            throw Error("A float was expected", node);
        string text = node.Token.Text.Replace("_", string.Empty);
        bool negative = text.StartsWith('-');
        string body = text.StartsWith('-') || text.StartsWith('+') ? text.Substring(1) : text;
        ulong sign = negative ? (isDouble ? 1UL << 63 : 1UL << 31) : 0;
        if (body == "inf")
            return sign | (isDouble ? 0x7FF0000000000000UL : 0x7F800000UL);
        if (body == "nan")
            return sign | (isDouble ? 0x7FF8000000000000UL : 0x7FC00000UL);
        if (body.StartsWith("nan:0x", StringComparison.Ordinal))
        {
            ulong payload = ParseUnsignedText(body.Substring(4), node);
            ulong maximum = isDouble ? 0xFFFFFFFFFFFFFUL : 0x7FFFFFUL;
            if (payload == 0 || payload > maximum)
                throw Error("A NaN payload is non-zero and fits the significand", node);
            return sign | (isDouble ? 0x7FF0000000000000UL : 0x7F800000UL) | payload;
        }
        if (body.StartsWith("0x", StringComparison.Ordinal))
            return sign | ParseHexFloat(body.Substring(2), isDouble, node);
        if (isDouble)
        {
            if (!double.TryParse(body, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || double.IsInfinity(value))
                throw Error($"{node.Token.Text} is not a float", node);
            return sign | (ulong)BitConverter.DoubleToInt64Bits(value);
        }
        if (!float.TryParse(body, NumberStyles.Float, CultureInfo.InvariantCulture, out float single) || float.IsInfinity(single))
            throw Error($"{node.Token.Text} is not a float", node);
        return sign | (uint)BitConverter.SingleToInt32Bits(single);
    }

    // The digits give an exact binary value, which is rounded once, to nearest even, into the format
    private ulong ParseHexFloat(string text, bool isDouble, Node node)
    {
        int p = text.IndexOfAny(new[] { 'p', 'P' });
        string mantissaText = p < 0 ? text : text.Substring(0, p);
        long exponent = 0;
        if (p >= 0 && !long.TryParse(text.AsSpan(p + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
            throw Error($"0x{text} is not a float", node);
        var mantissa = BigInteger.Zero;
        bool point = false;
        bool anyDigit = false;
        foreach (char c in mantissaText)
        {
            if (c == '.')
            {
                if (point)
                    throw Error($"0x{text} is not a float", node);
                point = true;
                continue;
            }
            int digit = HexDigit(c);
            if (digit < 0)
                throw Error($"0x{text} is not a float", node);
            mantissa = mantissa * 16 + digit;
            anyDigit = true;
            if (point)
                exponent -= 4;
        }
        if (!anyDigit)
            throw Error($"0x{text} is not a float", node);
        if (mantissa.IsZero)
            return 0;

        int significandBits = isDouble ? 53 : 24;
        int minimumExponent = isDouble ? -1074 : -149;
        int maximumExponent = isDouble ? 1023 : 127;
        long length = (long)mantissa.GetBitLength();
        long top = exponent + length - 1;
        long lowest = Math.Max(top - significandBits + 1, minimumExponent);
        long shift = lowest - exponent;
        BigInteger significand;
        if (shift > 0)
        {
            significand = mantissa >> (int)shift;
            var remainder = mantissa - (significand << (int)shift);
            var half = BigInteger.One << (int)(shift - 1);
            if (remainder > half || (remainder == half && !significand.IsEven))
                significand += 1;
        }
        else
            significand = mantissa << (int)-shift;
        if (significand.GetBitLength() > significandBits)
        {
            significand >>= 1;
            lowest++;
        }
        long unbiased = lowest + significandBits - 1;
        if (unbiased > maximumExponent)
            throw Error($"0x{text} is too large", node);
        ulong bits = (ulong)significand;
        if (significand.GetBitLength() < significandBits)
            return bits;
        ulong fractionMask = (1UL << (significandBits - 1)) - 1;
        ulong biased = (ulong)(unbiased + maximumExponent);
        return (biased << (significandBits - 1)) | (bits & fractionMask);
    }

    private string StringValue(Node node)
    {
        if (node.IsList || node.Token.Kind != TokenKind.String)
            throw Error("A string was expected", node);
        try
        {
            return new UTF8Encoding(false, true).GetString(node.Token.Bytes!);
        }
        catch (DecoderFallbackException)
        {
            throw Error("A name is not valid UTF-8", node);
        }
    }

    private byte[] BytesValue(Node node)
    {
        if (node.IsList || node.Token.Kind != TokenKind.String)
            throw Error("A string was expected", node);
        return node.Token.Bytes!;
    }

    private Node ParseNode(ref int position)
    {
        var token = _tokens[position++];
        if (token.Kind == TokenKind.Close)
            throw new WasmAssemblyException("Unbalanced parenthesis", token.Line, token.Column);
        if (token.Kind != TokenKind.Open)
            return new Node(token, null);
        var children = new List<Node>();
        for (;;)
        {
            if (position >= _tokens.Count)
                throw new WasmAssemblyException("A parenthesis is left open", token.Line, token.Column);
            if (_tokens[position].Kind == TokenKind.Close)
            {
                position++;
                return new Node(token, children);
            }
            children.Add(ParseNode(ref position));
        }
    }

    private void Tokenize()
    {
        int i = 0;
        int line = 1;
        int lineStart = 0;
        int annotationDepth = 0;
        while (i < _text.Length)
        {
            char c = _text[i];
            int column = i - lineStart + 1;
            if (c == '\n')
            {
                line++;
                lineStart = ++i;
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }
            if (c == ';' && i + 1 < _text.Length && _text[i + 1] == ';')
            {
                while (i < _text.Length && _text[i] != '\n')
                    i++;
                continue;
            }
            if (c == '(' && i + 1 < _text.Length && _text[i + 1] == ';')
            {
                int depth = 0;
                do
                {
                    if (i + 1 < _text.Length && _text[i] == '(' && _text[i + 1] == ';')
                    {
                        depth++;
                        i += 2;
                    }
                    else if (i + 1 < _text.Length && _text[i] == ';' && _text[i + 1] == ')')
                    {
                        depth--;
                        i += 2;
                    }
                    else
                    {
                        if (i >= _text.Length)
                            throw new WasmAssemblyException("A block comment is left open", line, column);
                        if (_text[i] == '\n')
                        {
                            line++;
                            lineStart = i + 1;
                        }
                        i++;
                    }
                }
                while (depth > 0);
                continue;
            }
            // Annotations such as (@name ...) carry nothing the module needs, so they are skipped whole
            if (c == '(' && i + 1 < _text.Length && _text[i + 1] == '@')
            {
                annotationDepth++;
                i += 2;
                continue;
            }
            if (c == '(')
            {
                if (annotationDepth > 0)
                    annotationDepth++;
                else
                    _tokens.Add(new Token(TokenKind.Open, "(", null, line, column));
                i++;
                continue;
            }
            if (c == ')')
            {
                if (annotationDepth > 0)
                    annotationDepth--;
                else
                    _tokens.Add(new Token(TokenKind.Close, ")", null, line, column));
                i++;
                continue;
            }
            if (c == '"')
            {
                var bytes = ReadString(ref i, line, column);
                if (annotationDepth == 0)
                    _tokens.Add(new Token(TokenKind.String, string.Empty, bytes, line, column));
                continue;
            }
            if (c == '$' && i + 1 < _text.Length && _text[i + 1] == '"')
            {
                i++;
                var bytes = ReadString(ref i, line, column);
                if (annotationDepth == 0)
                    _tokens.Add(new Token(TokenKind.Id, Encoding.UTF8.GetString(bytes), null, line, column));
                continue;
            }
            int start = i;
            while (i < _text.Length && !char.IsWhiteSpace(_text[i]) && _text[i] is not '(' and not ')' and not '"' and not ';')
                i++;
            if (i == start)
                throw new WasmAssemblyException($"Unexpected character {c}", line, column);
            if (annotationDepth > 0)
                continue;
            string atom = _text.Substring(start, i - start);
            _tokens.Add(atom[0] == '$'
                ? new Token(TokenKind.Id, atom.Substring(1), null, line, column)
                : new Token(TokenKind.Atom, atom, null, line, column));
        }
        if (annotationDepth > 0)
            throw new WasmAssemblyException("An annotation is left open", line, 1);
    }

    private byte[] ReadString(ref int i, int line, int column)
    {
        var bytes = new List<byte>();
        i++;
        while (true)
        {
            if (i >= _text.Length)
                throw new WasmAssemblyException("A string is left open", line, column);
            char c = _text[i++];
            if (c == '"')
                return bytes.ToArray();
            if (c != '\\')
            {
                if (char.IsHighSurrogate(c) && i < _text.Length)
                    bytes.AddRange(Encoding.UTF8.GetBytes(new[] { c, _text[i++] }));
                else
                    bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
                continue;
            }
            if (i >= _text.Length)
                throw new WasmAssemblyException("A string is left open", line, column);
            char escape = _text[i++];
            switch (escape)
            {
                case 'n': bytes.Add((byte)'\n'); break;
                case 't': bytes.Add((byte)'\t'); break;
                case 'r': bytes.Add((byte)'\r'); break;
                case '\\': bytes.Add((byte)'\\'); break;
                case '\'': bytes.Add((byte)'\''); break;
                case '"': bytes.Add((byte)'"'); break;
                case 'u':
                {
                    int close = _text.IndexOf('}', i);
                    if (i >= _text.Length || _text[i] != '{' || close < 0)
                        throw new WasmAssemblyException("A \\u escape is written \\u{hex}", line, column);
                    int code = int.Parse(_text.AsSpan(i + 1, close - i - 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    bytes.AddRange(Encoding.UTF8.GetBytes(char.ConvertFromUtf32(code)));
                    i = close + 1;
                    break;
                }
                default:
                {
                    int high = HexDigit(escape);
                    int low = i < _text.Length ? HexDigit(_text[i]) : -1;
                    if (high < 0 || low < 0)
                        throw new WasmAssemblyException($"Unknown escape \\{escape}", line, column);
                    bytes.Add((byte)(high * 16 + low));
                    i++;
                    break;
                }
            }
        }
    }
}
