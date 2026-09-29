using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.Wasm;

public sealed class WasmLinkOptions
{
    public ulong GlobalBase { get; set; } = 1024;
    public ulong StackSize { get; set; } = 64 * 1024;
    public ulong? MaximumPages { get; set; }
    public string? MemoryExportName { get; set; } = "memory";
    public string? TableExportName { get; set; } = "__indirect_function_table";
    public string? EntryExportName { get; set; } = "_start";
    public bool IncludeNames { get; set; } = true;
}

/// <summary>Turns a program into a module: lays out linear memory and resolves every symbol to an index or an address</summary>
internal static class WasmObjectLinker
{
    public const string StackPointer = "__stack_pointer";
    public const string HeapBase = "__heap_base";
    public const string DataEnd = "__data_end";
    public const string GlobalBase = "__global_base";

    public static WasmModule Link(WasmProgram program, WasmLinkOptions options)
    {
        var target = program.Target;
        var module = new WasmModule(target);
        module.Types.AddRange(program.Types);
        var names = module.Names;

        var functions = new Dictionary<string, uint>(StringComparer.Ordinal);
        var globals = new Dictionary<string, uint>(StringComparer.Ordinal);
        var tags = new Dictionary<string, uint>(StringComparer.Ordinal);
        var addresses = new Dictionary<string, ulong>(StringComparer.Ordinal);

        foreach (var import in program.Imports)
        {
            switch (import.Kind)
            {
                case WasmExternalKind.Function:
                {
                    uint index = module.AddImport(WasmImport.Function(import.Module, import.Field, module.InternType(import.Signature!)));
                    Define(functions, import.SymbolName, index);
                    names.Functions[index] = import.SymbolName;
                    break;
                }
                case WasmExternalKind.Global:
                {
                    uint index = module.AddImport(WasmImport.OfGlobal(import.Module, import.Field, import.Global));
                    Define(globals, import.SymbolName, index);
                    names.Globals[index] = import.SymbolName;
                    break;
                }
                case WasmExternalKind.Tag:
                {
                    uint index = module.AddImport(WasmImport.OfTag(import.Module, import.Field, module.InternType(import.Signature!)));
                    Define(tags, import.SymbolName, index);
                    names.Tags[index] = import.SymbolName;
                    break;
                }
                default:
                    throw new InvalidOperationException($"A program imports functions, globals and tags, not {import.Kind}");
            }
        }

        uint firstFunction = (uint)module.ImportedFunctionCount;
        for (int i = 0; i < program.Code.Functions.Length; i++)
        {
            var body = program.Code.Functions[i];
            Define(functions, body.Name, firstFunction + (uint)i);
        }

        // Read-only data first, then initialised data, then zeroed data, with the stack above them all
        var sectionBases = new Dictionary<string, ulong>(StringComparer.Ordinal);
        ulong address = options.GlobalBase;
        foreach (var kind in new[] { WasmObjectSectionKind.Rodata, WasmObjectSectionKind.Data, WasmObjectSectionKind.Bss })
        {
            foreach (var section in program.DataSections.Where(section => section.Kind == kind))
            {
                address = Align(address, (ulong)section.Alignment);
                if (!sectionBases.TryAdd(section.Name, address))
                    throw new InvalidOperationException($"The data section {section.Name} appears twice");
                address += (ulong)section.Size;
            }
        }
        ulong dataEnd = address;
        ulong stackTop = Align(Align(dataEnd, 16) + options.StackSize, 16);
        ulong pages = Math.Max(1, (stackTop + WasmMemoryType.PageSize - 1) / WasmMemoryType.PageSize);
        if (!target.Is64Bit && stackTop > uint.MaxValue)
            throw new InvalidOperationException("The data and the stack do not fit a 32-bit memory");
        module.Memories.Add(new WasmMemoryType(new WasmLimits(pages, options.MaximumPages, false, target.Is64Bit)));

        foreach (var symbol in program.Symbols)
        {
            if (symbol.Binding == WasmObjectSymbolBinding.External || !sectionBases.TryGetValue(symbol.SectionName, out var sectionBase))
                continue;
            if (symbol.Kind == WasmObjectSymbolKind.Section)
                addresses.TryAdd(symbol.Name, sectionBase);
            else
                Define(addresses, symbol.Name, sectionBase + (ulong)symbol.Offset);
        }
        foreach (var (name, sectionBase) in sectionBases)
            addresses.TryAdd(name, sectionBase);
        addresses.TryAdd(HeapBase, stackTop);
        addresses.TryAdd(DataEnd, dataEnd);
        addresses.TryAdd(GlobalBase, options.GlobalBase);

        var addressType = target.AddressType;
        uint stackPointer = (uint)module.GlobalCount;
        module.Globals.Add(new WasmGlobal(new WasmGlobalType(addressType, true), new[] { AddressConstant(stackTop, target) }));
        if (!globals.TryAdd(StackPointer, stackPointer))
            throw new InvalidOperationException($"{StackPointer} is defined by the linker");
        names.Globals[stackPointer] = StackPointer;
        foreach (var global in program.Globals)
        {
            uint index = (uint)(module.ImportedGlobalCount + module.Globals.Count);
            Define(globals, global.Name, index);
            names.Globals[index] = global.Name;
            module.Globals.Add(new WasmGlobal(global.Type, ImmutableArray<WasmInstruction>.Empty));
        }
        foreach (var tag in program.Tags)
        {
            uint index = (uint)(module.ImportedTagCount + module.Tags.Count);
            Define(tags, tag.Name, index);
            names.Tags[index] = tag.Name;
            module.Tags.Add(new WasmTag(module.InternType(tag.Signature)));
        }

        // A function whose address is taken gets a slot in the table; slot zero stays empty as the null pointer
        var slots = new Dictionary<string, uint>(StringComparer.Ordinal);
        var slotOrder = new List<uint>();
        bool needsTable = false;
        uint SlotOf(string symbol)
        {
            if (!slots.TryGetValue(symbol, out var slot))
            {
                slot = (uint)slotOrder.Count + 1;
                slots.Add(symbol, slot);
                slotOrder.Add(functions[symbol]);
            }
            return slot;
        }

        var declared = new HashSet<uint>();
        var resolver = new Resolver(module, functions, globals, tags, addresses, SlotOf, declared);
        for (int i = 0; i < program.Code.Functions.Length; i++)
        {
            var body = program.Code.Functions[i];
            var function = new WasmFunction(module.InternType(body.Signature));
            function.Locals.AddRange(body.Locals);
            foreach (var instruction in body.Instructions)
            {
                if (instruction.Kind is WasmInstrKind.CallIndirect or WasmInstrKind.ReturnCallIndirect && instruction.Index2 == 0)
                    needsTable = true;
                function.Body.Add(resolver.Resolve(instruction, body.Name));
            }
            module.Functions.Add(function);
            uint index = firstFunction + (uint)i;
            names.Functions[index] = body.Name;
            for (int local = 0; local < body.LocalNames.Length; local++)
            {
                if (body.LocalNames[local] is string localName)
                    names.SetLocalName(index, (uint)local, localName);
            }
        }
        for (int i = 0; i < program.Globals.Length; i++)
        {
            var global = program.Globals[i];
            int at = module.Globals.Count - program.Globals.Length + i;
            module.Globals[at] = new WasmGlobal(global.Type, global.Initializer.Select(instruction => resolver.Resolve(instruction, global.Name)));
        }

        for (int i = 0; i < program.DataSections.Length; i++)
        {
            var section = program.DataSections[i];
            if (section.Kind == WasmObjectSectionKind.Bss)
            {
                if (!section.Relocations.IsEmpty)
                    throw new InvalidOperationException($"The zeroed section {section.Name} cannot hold relocations");
                continue;
            }
            if (section.Data.IsEmpty)
                continue;
            var bytes = section.Data.ToArray();
            foreach (var relocation in section.Relocations)
                Patch(bytes, relocation, section.Name, functions, addresses, SlotOf);
            module.Data.Add(WasmDataSegment.Active(sectionBases[section.Name], bytes, target.Is64Bit));
            names.Data[(uint)(module.Data.Count - 1)] = section.Name;
        }

        if (slotOrder.Count != 0 || needsTable)
        {
            ulong size = (ulong)slotOrder.Count + 1;
            module.Tables.Add(new WasmTable(new WasmTableType(WasmValueType.FuncRef, new WasmLimits(size, size, false, target.Is64Bit))));
            if (slotOrder.Count != 0)
                module.Elements.Add(new WasmElementSegment(WasmSegmentMode.Active, 0, new[] { AddressConstant(1, target) }, slotOrder));
        }
        var undeclared = declared.Except(slotOrder).OrderBy(static index => index).ToList();
        if (undeclared.Count != 0)
            module.Elements.Add(new WasmElementSegment(WasmSegmentMode.Declarative, 0, null, undeclared));

        var exportNames = new HashSet<string>(StringComparer.Ordinal);
        void Export(string name, WasmExternalKind kind, uint index)
        {
            if (!exportNames.Add(name))
                throw new InvalidOperationException($"The export name \"{name}\" is used twice");
            module.Exports.Add(new WasmExport(name, kind, index));
        }
        if (options.MemoryExportName is not null)
            Export(options.MemoryExportName, WasmExternalKind.Memory, 0);
        if (options.TableExportName is not null && module.Tables.Count != 0)
            Export(options.TableExportName, WasmExternalKind.Table, 0);
        foreach (var export in program.Exports)
        {
            if (functions.TryGetValue(export.SymbolName, out var function))
                Export(export.Name, WasmExternalKind.Function, function);
            else if (globals.TryGetValue(export.SymbolName, out var global))
                Export(export.Name, WasmExternalKind.Global, global);
            else if (tags.TryGetValue(export.SymbolName, out var tag))
                Export(export.Name, WasmExternalKind.Tag, tag);
            else
                throw new InvalidOperationException($"The export \"{export.Name}\" names {export.SymbolName}, which is not a function, global or tag");
        }
        if (program.EntrySymbol.Length != 0 && options.EntryExportName is not null)
        {
            if (!functions.TryGetValue(program.EntrySymbol, out var entry))
                throw new InvalidOperationException($"The entry point {program.EntrySymbol} is not a function");
            if (!module.Exports.Any(export => export.Kind == WasmExternalKind.Function && export.Name == options.EntryExportName))
                Export(options.EntryExportName, WasmExternalKind.Function, entry);
        }

        if (!options.IncludeNames)
        {
            names.Functions.Clear();
            names.Globals.Clear();
            names.Tags.Clear();
            names.Data.Clear();
            names.Locals.Clear();
        }
        return module;
    }

    private static void Define<T>(Dictionary<string, T> symbols, string name, T value)
    {
        if (string.IsNullOrEmpty(name))
            throw new InvalidOperationException("A definition has no name");
        if (!symbols.TryAdd(name, value))
            throw new InvalidOperationException($"The symbol {name} is defined twice");
    }

    private static ulong Align(ulong value, ulong alignment)
        => alignment <= 1 ? value : checked((value + alignment - 1) / alignment * alignment);

    private static WasmInstruction AddressConstant(ulong value, WasmTarget target)
        => target.Is64Bit ? WasmInstruction.I64Const(unchecked((long)value)) : WasmInstruction.I32Const(unchecked((int)(uint)value));

    private static void Patch(
        byte[] bytes,
        WasmObjectRelocation relocation,
        string section,
        Dictionary<string, uint> functions,
        Dictionary<string, ulong> addresses,
        Func<string, uint> slotOf)
    {
        bool wide = relocation.Kind is WasmObjectRelocationKind.MemoryAddress64 or WasmObjectRelocationKind.TableIndex64;
        int size = wide ? 8 : 4;
        if (relocation.Kind == WasmObjectRelocationKind.None)
            return;
        if (relocation.Offset + size > bytes.Length)
            throw new InvalidOperationException($"A relocation at {section}+{relocation.Offset} runs past the end of the section");
        ulong value;
        bool isFunction = functions.ContainsKey(relocation.SymbolName);
        if (relocation.Kind is WasmObjectRelocationKind.TableIndex32 or WasmObjectRelocationKind.TableIndex64 || isFunction)
        {
            if (!isFunction)
                throw new InvalidOperationException($"{relocation.SymbolName} in {section} is not a function, so it has no table slot");
            if (relocation.Addend != 0)
                throw new InvalidOperationException($"A function pointer to {relocation.SymbolName} cannot be offset");
            value = slotOf(relocation.SymbolName);
        }
        else if (addresses.TryGetValue(relocation.SymbolName, out var symbolAddress))
            value = unchecked(symbolAddress + (ulong)relocation.Addend);
        else
            throw new InvalidOperationException($"Undefined symbol {relocation.SymbolName} in {section}");
        if (!wide && value > uint.MaxValue)
            throw new InvalidOperationException($"The address of {relocation.SymbolName} does not fit 32 bits");
        for (int i = 0; i < size; i++)
            bytes[relocation.Offset + i] = (byte)(value >> (i * 8));
    }

    private sealed class Resolver
    {
        private readonly WasmModule _module;
        private readonly Dictionary<string, uint> _functions;
        private readonly Dictionary<string, uint> _globals;
        private readonly Dictionary<string, uint> _tags;
        private readonly Dictionary<string, ulong> _addresses;
        private readonly Func<string, uint> _slotOf;
        private readonly HashSet<uint> _declared;

        public Resolver(
            WasmModule module,
            Dictionary<string, uint> functions,
            Dictionary<string, uint> globals,
            Dictionary<string, uint> tags,
            Dictionary<string, ulong> addresses,
            Func<string, uint> slotOf,
            HashSet<uint> declared)
        {
            _module = module;
            _functions = functions;
            _globals = globals;
            _tags = tags;
            _addresses = addresses;
            _slotOf = slotOf;
            _declared = declared;
        }

        public WasmInstruction Resolve(WasmInstruction instruction, string owner)
        {
            var metadata = instruction.Metadata;
            if (instruction.BlockType.NeedsTypeIndex)
                instruction = instruction.WithBlockType(instruction.BlockType.WithTypeIndex(_module.InternType(instruction.BlockType.Signature!)));
            if (metadata.Format == WasmInstructionFormat.CallIndirect && instruction.Index == uint.MaxValue)
            {
                if (instruction.Signature is null)
                    throw new InvalidOperationException($"{metadata.Mnemonic} in {owner} names neither a type nor a signature");
                instruction = instruction.WithTypeIndex(_module.InternType(instruction.Signature));
            }
            if (instruction.Catches.Any(static handler => handler.TagSymbol is not null))
            {
                instruction = instruction.WithCatches(instruction.Catches
                    .Select(handler => handler.TagSymbol is null ? handler : new WasmCatch(handler.Kind, Lookup(_tags, handler.TagSymbol, "tag", owner), handler.Label))
                    .ToImmutableArray());
            }
            if (!instruction.HasSymbol)
            {
                if (instruction.Kind == WasmInstrKind.RefFunc)
                    _declared.Add(instruction.Index);
                return instruction;
            }

            string symbol = instruction.Symbol!;
            switch (metadata.Format)
            {
                case WasmInstructionFormat.Function:
                {
                    uint function = Lookup(_functions, symbol, "function", owner);
                    if (instruction.Kind == WasmInstrKind.RefFunc)
                        _declared.Add(function);
                    return instruction.WithResolvedIndex(function);
                }
                case WasmInstructionFormat.Global:
                    return instruction.WithResolvedIndex(Lookup(_globals, symbol, "global", owner));
                case WasmInstructionFormat.Tag:
                    return instruction.WithResolvedIndex(Lookup(_tags, symbol, "tag", owner));
                case WasmInstructionFormat.I32:
                case WasmInstructionFormat.I64:
                {
                    ulong value;
                    if (_functions.ContainsKey(symbol))
                    {
                        if (instruction.Addend != 0)
                            throw new InvalidOperationException($"A function pointer to {symbol} in {owner} cannot be offset");
                        value = _slotOf(symbol);
                    }
                    else if (_addresses.TryGetValue(symbol, out var address))
                        value = unchecked(address + (ulong)instruction.Addend);
                    else
                        throw new InvalidOperationException($"Undefined symbol {symbol} in {owner}");
                    if (metadata.Format == WasmInstructionFormat.I32 && value > uint.MaxValue)
                        throw new InvalidOperationException($"The address of {symbol} does not fit 32 bits");
                    return instruction.WithResolvedValue(value);
                }
                default:
                    throw new InvalidOperationException($"{metadata.Mnemonic} in {owner} cannot refer to the symbol {symbol}");
            }
        }

        private static uint Lookup(Dictionary<string, uint> symbols, string name, string what, string owner)
            => symbols.TryGetValue(name, out var index) ? index : throw new InvalidOperationException($"Undefined {what} {name} in {owner}");
    }
}

/// <summary>Merges programs into one: types are shared, local symbols renamed apart and global symbols resolved</summary>
public static class WasmObjectComposer
{
    public static WasmProgram Compose(WasmProgram primary, params WasmProgram[] libraries)
    {
        if (primary is null)
            throw new ArgumentNullException(nameof(primary));
        libraries ??= Array.Empty<WasmProgram>();
        var inputs = new WasmProgram[libraries.Length + 1];
        inputs[0] = primary;
        for (int i = 0; i < libraries.Length; i++)
        {
            inputs[i + 1] = libraries[i] ?? throw new ArgumentNullException(nameof(libraries));
            if (inputs[i + 1].Target.XLen != primary.Target.XLen)
                throw new InvalidOperationException($"Cannot compose a {inputs[i + 1].Target} program into a {primary.Target} one");
        }

        var types = new List<WasmRecursionGroup>();
        var typeMaps = ComposeTypes(inputs, types);
        var renames = inputs.Select((input, i) => BuildLocalRenames(input, i)).ToArray();
        string Rename(int input, string name) => renames[input].TryGetValue(name, out var renamed) ? renamed : name;

        // A strong definition wins over a tentative one, and two strong ones clash
        var winners = new Dictionary<string, (int Input, bool Tentative, int Size)>(StringComparer.Ordinal);
        void Offer(int input, string name, bool tentative, int size)
        {
            if (!winners.TryGetValue(name, out var current))
                winners[name] = (input, tentative, size);
            else if (!current.Tentative && !tentative)
                throw new InvalidOperationException($"Duplicate global symbol: {name}");
            else if (current.Tentative && (!tentative || size > current.Size))
                winners[name] = (input, tentative, size);
        }
        for (int i = 0; i < inputs.Length; i++)
        {
            var input = inputs[i];
            foreach (var function in input.Code.Functions)
                Offer(i, Rename(i, function.Name), false, 0);
            foreach (var global in input.Globals)
                Offer(i, Rename(i, global.Name), false, 0);
            foreach (var tag in input.Tags)
                Offer(i, Rename(i, tag.Name), false, 0);
            foreach (var symbol in input.Symbols)
            {
                if (symbol.Kind is WasmObjectSymbolKind.Object && symbol.Binding != WasmObjectSymbolBinding.External)
                    Offer(i, Rename(i, symbol.Name), symbol.IsTentative, symbol.Size);
            }
        }
        bool Wins(int input, string name) => winners.TryGetValue(name, out var winner) && winner.Input == input;

        var functions = new List<WasmFunctionBody>();
        var globals = new List<WasmProgramGlobal>();
        var tags = new List<WasmProgramTag>();
        var imports = new List<WasmProgramImport>();
        var importNames = new HashSet<string>(StringComparer.Ordinal);
        var exports = new List<WasmProgramExport>();
        var sectionBuilders = new Dictionary<string, ComposedSectionBuilder>(StringComparer.Ordinal);
        var sectionOrder = new List<string>();
        var sectionBases = new Dictionary<(int Input, string Section), int>();
        var symbols = new List<WasmObjectSymbol>();

        for (int i = 0; i < inputs.Length; i++)
        {
            var input = inputs[i];
            var map = typeMaps[i];
            int index = i;
            WasmInstruction Rewrite(WasmInstruction instruction)
            {
                instruction = instruction.RemapTypes(map);
                if (instruction.HasSymbol)
                    instruction = WasmInstruction.Create(
                        instruction.Kind, instruction.Index, instruction.Index2, instruction.Value, instruction.MemoryArgument, instruction.BlockType,
                        instruction.Type, instruction.Type2, instruction.Labels, instruction.Types, instruction.Bytes, instruction.Catches,
                        instruction.Signature, Rename(index, instruction.Symbol!), instruction.Addend);
                if (instruction.Catches.Any(static handler => handler.TagSymbol is not null))
                    instruction = instruction.WithCatches(instruction.Catches
                        .Select(handler => handler.TagSymbol is null ? handler : new WasmCatch(handler.Kind, handler.Tag, handler.Label, Rename(index, handler.TagSymbol)))
                        .ToImmutableArray());
                return instruction;
            }

            foreach (var function in input.Code.Functions)
            {
                string name = Rename(i, function.Name);
                if (!Wins(i, name))
                    continue;
                functions.Add(new WasmFunctionBody(
                    name,
                    function.Signature.RemapTypes(map),
                    function.Locals.Select(local => local.RemapTypes(map)),
                    function.Instructions.Select(Rewrite),
                    function.LocalNames));
            }
            foreach (var global in input.Globals)
            {
                string name = Rename(i, global.Name);
                if (Wins(i, name))
                    globals.Add(new WasmProgramGlobal(name, new WasmGlobalType(global.Type.ValueType.RemapTypes(map), global.Type.Mutable), global.Initializer.Select(Rewrite)));
            }
            foreach (var tag in input.Tags)
            {
                string name = Rename(i, tag.Name);
                if (Wins(i, name))
                    tags.Add(new WasmProgramTag(name, tag.Signature.RemapTypes(map)));
            }
            foreach (var export in input.Exports)
                exports.Add(new WasmProgramExport(export.Name, Rename(i, export.SymbolName)));

            foreach (var section in input.DataSections)
            {
                if (!sectionBuilders.TryGetValue(section.Name, out var builder))
                {
                    builder = new ComposedSectionBuilder(section.Name, section.Kind);
                    sectionBuilders.Add(section.Name, builder);
                    sectionOrder.Add(section.Name);
                }
                else if (builder.Kind != section.Kind)
                    throw new InvalidOperationException($"Cannot compose wasm sections with different kinds: {section.Name}");
                sectionBases[(i, section.Name)] = builder.Append(section, name => Rename(index, name));
            }

            foreach (var symbol in input.Symbols)
            {
                if (symbol.Kind == WasmObjectSymbolKind.Section || symbol.Binding == WasmObjectSymbolBinding.External)
                    continue;
                string name = Rename(i, symbol.Name);
                if (symbol.Kind == WasmObjectSymbolKind.Object)
                {
                    if (!Wins(i, name))
                        continue;
                    if (!sectionBases.TryGetValue((i, symbol.SectionName), out var sectionBase))
                        throw new InvalidOperationException($"Symbol section is missing from composed wasm object: {symbol.SectionName}");
                    symbols.Add(new WasmObjectSymbol(name, symbol.SectionName, sectionBase + symbol.Offset, symbol.Size, symbol.Binding, symbol.Kind, symbol.IsTentative));
                }
            }
        }

        var defined = new HashSet<string>(winners.Keys, StringComparer.Ordinal);
        for (int i = 0; i < inputs.Length; i++)
        {
            foreach (var import in inputs[i].Imports)
            {
                string name = Rename(i, import.SymbolName);
                if (defined.Contains(name) || !importNames.Add(name))
                    continue;
                imports.Add(import.Kind switch
                {
                    WasmExternalKind.Function => WasmProgramImport.Function(name, import.Signature!.RemapTypes(typeMaps[i]), import.Module, import.Field),
                    WasmExternalKind.Tag => WasmProgramImport.OfTag(name, import.Signature!.RemapTypes(typeMaps[i]), import.Module, import.Field),
                    _ => WasmProgramImport.OfGlobal(name, new WasmGlobalType(import.Global.ValueType.RemapTypes(typeMaps[i]), import.Global.Mutable), import.Module, import.Field),
                });
            }
        }

        var bindings = new Dictionary<string, WasmObjectSymbolBinding>(StringComparer.Ordinal);
        for (int i = 0; i < inputs.Length; i++)
        {
            foreach (var symbol in inputs[i].Symbols)
            {
                if (symbol.Binding != WasmObjectSymbolBinding.External && symbol.Kind != WasmObjectSymbolKind.Section)
                    bindings.TryAdd(Rename(i, symbol.Name), symbol.Binding);
            }
        }
        WasmObjectSymbolBinding BindingOf(string name) => bindings.TryGetValue(name, out var binding) ? binding : WasmObjectSymbolBinding.Global;
        for (int i = 0; i < functions.Count; i++)
            symbols.Add(new WasmObjectSymbol(functions[i].Name, WasmObjectSections.Code, i, 0, BindingOf(functions[i].Name), WasmObjectSymbolKind.Function));
        for (int i = 0; i < globals.Count; i++)
            symbols.Add(new WasmObjectSymbol(globals[i].Name, WasmObjectSections.Globals, i, 0, BindingOf(globals[i].Name), WasmObjectSymbolKind.Global));
        for (int i = 0; i < tags.Count; i++)
            symbols.Add(new WasmObjectSymbol(tags[i].Name, WasmObjectSections.Tags, i, 0, BindingOf(tags[i].Name), WasmObjectSymbolKind.Tag));
        foreach (var import in imports)
        {
            var kind = import.Kind switch
            {
                WasmExternalKind.Function => WasmObjectSymbolKind.Function,
                WasmExternalKind.Tag => WasmObjectSymbolKind.Tag,
                _ => WasmObjectSymbolKind.Global,
            };
            symbols.Add(new WasmObjectSymbol(import.SymbolName, string.Empty, 0, 0, WasmObjectSymbolBinding.External, kind));
        }

        var dataSections = new List<WasmDataSection>();
        foreach (var name in sectionOrder)
        {
            var section = sectionBuilders[name].ToSection();
            dataSections.Add(section);
            symbols.Add(new WasmObjectSymbol(section.Name, section.Name, 0, section.Size, WasmObjectSymbolBinding.Local, WasmObjectSymbolKind.Section));
        }

        return new WasmProgram(
            primary.Target,
            types.ToImmutableArray(),
            new WasmCodeSection(functions),
            dataSections.ToImmutableArray(),
            globals.ToImmutableArray(),
            tags.ToImmutableArray(),
            imports.ToImmutableArray(),
            symbols.ToImmutableArray(),
            exports.ToImmutableArray(),
            Rename(0, primary.EntrySymbol));
    }

    // Identical recursion groups are shared, so equal types from different inputs stay equal in the result
    private static Func<uint, uint>[] ComposeTypes(WasmProgram[] inputs, List<WasmRecursionGroup> types)
    {
        const uint Relative = 0xFFF0_0000;
        var existing = new Dictionary<WasmRecursionGroup, uint>();
        uint total = 0;
        var maps = new Func<uint, uint>[inputs.Length];
        for (int i = 0; i < inputs.Length; i++)
        {
            var map = new List<uint>();
            uint start = 0;
            foreach (var group in inputs[i].Types)
            {
                uint end = start + (uint)group.Types.Length;
                uint groupStart = start;
                uint Outside(uint index)
                    => index < map.Count ? map[(int)index] : throw new InvalidOperationException($"Type {index} is referred to before it is defined");
                var key = group.RemapTypes(index => index >= groupStart && index < end ? Relative + (index - groupStart) : Outside(index));
                if (!existing.TryGetValue(key, out var placed))
                {
                    placed = total;
                    types.Add(group.RemapTypes(index => index >= groupStart && index < end ? placed + (index - groupStart) : Outside(index)));
                    existing.Add(key, placed);
                    total += (uint)group.Types.Length;
                }
                for (uint k = 0; k < group.Types.Length; k++)
                    map.Add(placed + k);
                start = end;
            }
            var finalMap = map;
            maps[i] = index => index < finalMap.Count ? finalMap[(int)index] : throw new InvalidOperationException($"Type {index} is not defined");
        }
        return maps;
    }

    private static Dictionary<string, string> BuildLocalRenames(WasmProgram input, int inputIndex)
    {
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        if (inputIndex == 0)
            return renames;
        foreach (var symbol in input.Symbols)
        {
            if (symbol.Binding == WasmObjectSymbolBinding.Local && symbol.Kind != WasmObjectSymbolKind.Section && symbol.Name.Length != 0)
                renames.TryAdd(symbol.Name, $"{symbol.Name}.{inputIndex}");
        }
        return renames;
    }

    private sealed class ComposedSectionBuilder
    {
        private readonly List<byte> _data = new List<byte>();
        private readonly List<WasmObjectRelocation> _relocations = new List<WasmObjectRelocation>();
        private int _bssSize;

        public string Name { get; }
        public WasmObjectSectionKind Kind { get; }
        public int Alignment { get; private set; } = 1;

        public ComposedSectionBuilder(string name, WasmObjectSectionKind kind)
        {
            Name = name;
            Kind = kind;
        }

        public int Append(WasmDataSection section, Func<string, string> rename)
        {
            Alignment = Math.Max(Alignment, section.Alignment);
            if (Kind == WasmObjectSectionKind.Bss)
            {
                _bssSize = AlignUp(_bssSize, section.Alignment);
                int bssBase = _bssSize;
                _bssSize = checked(_bssSize + section.BssSize);
                return bssBase;
            }
            while (_data.Count % section.Alignment != 0)
                _data.Add(0);
            int sectionBase = _data.Count;
            _data.AddRange(section.Data);
            foreach (var relocation in section.Relocations)
                _relocations.Add(new WasmObjectRelocation(Name, checked(sectionBase + relocation.Offset), rename(relocation.SymbolName), relocation.Addend, relocation.Kind));
            return sectionBase;
        }

        public WasmDataSection ToSection()
            => new WasmDataSection(Name, Kind, Alignment, _data.ToImmutableArray(), _bssSize, _relocations.ToImmutableArray());

        private static int AlignUp(int value, int alignment)
        {
            int remainder = value % alignment;
            return remainder == 0 ? value : checked(value + alignment - remainder);
        }
    }
}
