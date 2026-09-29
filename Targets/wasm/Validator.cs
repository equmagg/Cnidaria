using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Cnidaria.Wasm;

public sealed class WasmValidationException : Exception
{
    public int? Function { get; }

    public int? Instruction { get; }

    public WasmValidationException(string message, int? function = null, int? instruction = null)
        : base(function is int f ? $"{message} (function {f}{(instruction is int i ? $", instruction {i}" : "")})" : message)
    {
        Function = function;
        Instruction = instruction;
    }
}

/// <summary>Checks a module against the validation rules of WebAssembly 3.0</summary>
public static class WasmValidator
{
    public static void Validate(WasmModule module)
    {
        if (module is null)
            throw new ArgumentNullException(nameof(module));
        new ModuleContext(module).Validate();
    }

    public static bool TryValidate(WasmModule module, out string? error)
    {
        try
        {
            Validate(module);
            error = null;
            return true;
        }
        catch (WasmValidationException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private sealed class ModuleContext
    {
        public WasmModule Module { get; }
        public List<WasmSubType> Types { get; } = new List<WasmSubType>();
        private readonly List<int> _groupEnd = new List<int>();
        private readonly List<int> _canonical = new List<int>();
        public List<uint> Functions { get; } = new List<uint>();
        public List<WasmTableType> Tables { get; } = new List<WasmTableType>();
        public List<WasmMemoryType> Memories { get; } = new List<WasmMemoryType>();
        public List<WasmGlobalType> Globals { get; } = new List<WasmGlobalType>();
        public List<uint> Tags { get; } = new List<uint>();
        public List<WasmValueType> Elements { get; } = new List<WasmValueType>();
        public HashSet<uint> References { get; } = new HashSet<uint>();
        public int ImportedGlobals { get; private set; }

        public ModuleContext(WasmModule module)
        {
            Module = module;
        }

        private static WasmValidationException Error(string message) => new WasmValidationException(message);

        public void Validate()
        {
            ValidateTypes();
            int typeLimit = Types.Count;

            foreach (var import in Module.Imports)
            {
                switch (import.Kind)
                {
                    case WasmExternalKind.Function:
                        FunctionType(import.TypeIndex);
                        Functions.Add(import.TypeIndex);
                        break;
                    case WasmExternalKind.Table:
                        CheckTableType(import.Table);
                        Tables.Add(import.Table);
                        break;
                    case WasmExternalKind.Memory:
                        CheckMemoryType(import.Memory);
                        Memories.Add(import.Memory);
                        break;
                    case WasmExternalKind.Global:
                        CheckValueType(import.Global.ValueType, typeLimit);
                        Globals.Add(import.Global);
                        break;
                    case WasmExternalKind.Tag:
                        CheckTagType(import.TypeIndex);
                        Tags.Add(import.TypeIndex);
                        break;
                    default:
                        throw Error($"Unknown import kind {import.Kind}");
                }
            }
            ImportedGlobals = Globals.Count;

            foreach (var function in Module.Functions)
            {
                FunctionType(function.TypeIndex);
                Functions.Add(function.TypeIndex);
            }
            foreach (var table in Module.Tables)
                Tables.Add(table.Type);
            foreach (var memory in Module.Memories)
            {
                CheckMemoryType(memory);
                Memories.Add(memory);
            }
            foreach (var tag in Module.Tags)
            {
                CheckTagType(tag.TypeIndex);
                Tags.Add(tag.TypeIndex);
            }
            foreach (var segment in Module.Elements)
                Elements.Add(segment.ElementType);

            // Functions referenced anywhere outside of function bodies may be referenced inside them too
            foreach (var global in Module.Globals)
                CollectReferences(global.Initializer);
            foreach (var table in Module.Tables)
                CollectReferences(table.Initializer);
            foreach (var segment in Module.Elements)
            {
                foreach (var function in segment.Functions)
                    References.Add(function);
                foreach (var expression in segment.Expressions)
                    CollectReferences(expression);
            }
            foreach (var export in Module.Exports)
            {
                if (export.Kind == WasmExternalKind.Function)
                    References.Add(export.Index);
            }

            for (int i = 0; i < Module.Tables.Count; i++)
            {
                var table = Module.Tables[i];
                CheckTableType(table.Type);
                if (table.HasInitializer)
                    CheckConstant(table.Initializer, table.Type.ElementType, ImportedGlobals, $"The initializer of table {i}");
                else if (!table.Type.ElementType.Nullable)
                    throw Error($"Table {i} holds non-null references and so needs an initializer");
            }

            for (int i = 0; i < Module.Globals.Count; i++)
            {
                var global = Module.Globals[i];
                CheckValueType(global.Type.ValueType, typeLimit);
                CheckConstant(global.Initializer, global.Type.ValueType, ImportedGlobals + i, $"The initializer of global {ImportedGlobals + i}");
                Globals.Add(global.Type);
            }

            var exportNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var export in Module.Exports)
            {
                if (!exportNames.Add(export.Name))
                    throw Error($"The export name \"{export.Name}\" is used twice");
                int count = export.Kind switch
                {
                    WasmExternalKind.Function => Functions.Count,
                    WasmExternalKind.Table => Tables.Count,
                    WasmExternalKind.Memory => Memories.Count,
                    WasmExternalKind.Global => Globals.Count,
                    WasmExternalKind.Tag => Tags.Count,
                    _ => throw Error($"Unknown export kind {export.Kind}"),
                };
                if (export.Index >= count)
                    throw Error($"The export \"{export.Name}\" names {export.Kind.ToString().ToLowerInvariant()} {export.Index}, which does not exist");
            }

            if (Module.Start is uint start)
            {
                if (start >= Functions.Count)
                    throw Error($"The start function {start} does not exist");
                var type = FunctionType(Functions[(int)start]);
                if (type.Parameters.Length != 0 || type.Results.Length != 0)
                    throw Error("The start function takes and returns nothing");
            }

            for (int i = 0; i < Module.Elements.Count; i++)
            {
                var segment = Module.Elements[i];
                CheckValueType(segment.ElementType, typeLimit);
                foreach (var function in segment.Functions)
                {
                    if (function >= Functions.Count)
                        throw Error($"Element segment {i} names function {function}, which does not exist");
                }
                foreach (var expression in segment.Expressions)
                    CheckConstant(expression, segment.ElementType, Globals.Count, $"An element of segment {i}");
                if (segment.Mode == WasmSegmentMode.Active)
                {
                    if (segment.Table >= Tables.Count)
                        throw Error($"Element segment {i} fills table {segment.Table}, which does not exist");
                    var table = Tables[(int)segment.Table];
                    if (!IsValueSubtype(segment.ElementType, table.ElementType))
                        throw Error($"Element segment {i} holds {segment.ElementType}, which table {segment.Table} of {table.ElementType} cannot");
                    CheckConstant(segment.Offset, table.AddressType, Globals.Count, $"The offset of element segment {i}");
                }
            }

            for (int i = 0; i < Module.Data.Count; i++)
            {
                var segment = Module.Data[i];
                if (segment.Mode != WasmSegmentMode.Active)
                    continue;
                if (segment.Memory >= Memories.Count)
                    throw Error($"Data segment {i} fills memory {segment.Memory}, which does not exist");
                CheckConstant(segment.Offset, Memories[(int)segment.Memory].AddressType, Globals.Count, $"The offset of data segment {i}");
            }

            int imported = Module.ImportedFunctionCount;
            for (int i = 0; i < Module.Functions.Count; i++)
                new CodeValidator(this, imported + i).ValidateFunction(Module.Functions[i]);
        }

        private void CollectReferences(IEnumerable<WasmInstruction> expression)
        {
            foreach (var instruction in expression)
            {
                if (instruction.Kind == WasmInstrKind.RefFunc)
                    References.Add(instruction.Index);
            }
        }

        private void CheckConstant(ImmutableArray<WasmInstruction> expression, WasmValueType type, int visibleGlobals, string what)
        {
            try
            {
                new CodeValidator(this, -1, visibleGlobals).ValidateConstant(expression, type);
            }
            catch (WasmValidationException exception)
            {
                throw Error($"{what} is invalid: {exception.Message}");
            }
        }

        private void ValidateTypes()
        {
            int start = 0;
            var canonicalGroups = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var group in Module.Types)
            {
                int end = start + group.Types.Length;
                foreach (var type in group.Types)
                {
                    Types.Add(type);
                    _groupEnd.Add(end);
                }
                // Types in a group can refer to one another, but nothing refers forward past its own group
                for (int i = start; i < end; i++)
                {
                    CheckComposite(Types[i].Composite, end);
                    if (Types[i].Supertypes.Length > 1)
                        throw Error($"Type {i} declares more than one supertype");
                    foreach (var supertype in Types[i].Supertypes)
                    {
                        if (supertype >= i)
                            throw Error($"Type {i} declares supertype {supertype}, which is not defined before it");
                    }
                }

                var key = new StringBuilder();
                foreach (var type in group.Types)
                    AppendCanonical(key, type, start, end);
                string text = key.ToString();
                if (!canonicalGroups.TryGetValue(text, out int first))
                    canonicalGroups.Add(text, first = start);
                for (int i = start; i < end; i++)
                    _canonical.Add(first + (i - start));

                for (int i = start; i < end; i++)
                {
                    var type = Types[i];
                    foreach (var supertype in type.Supertypes)
                    {
                        var parent = Types[(int)supertype];
                        if (parent.Final)
                            throw Error($"Type {i} extends the final type {supertype}");
                        if (!IsCompositeSubtype(type.Composite, parent.Composite))
                            throw Error($"Type {i} does not match its supertype {supertype}");
                    }
                }
                start = end;
            }
        }

        private void AppendCanonical(StringBuilder key, WasmSubType type, int start, int end)
        {
            key.Append(type.Final ? 'F' : 'O');
            foreach (var supertype in type.Supertypes)
                AppendIndex(key, supertype, start, end);
            var composite = type.Composite;
            switch (composite.Kind)
            {
                case WasmCompositeKind.Function:
                    key.Append("(func");
                    foreach (var parameter in composite.Function!.Parameters)
                        AppendValue(key, parameter, start, end);
                    key.Append(";");
                    foreach (var result in composite.Function.Results)
                        AppendValue(key, result, start, end);
                    break;
                case WasmCompositeKind.Struct:
                    key.Append("(struct");
                    foreach (var field in composite.Fields)
                        AppendField(key, field, start, end);
                    break;
                default:
                    key.Append("(array");
                    AppendField(key, composite.Element, start, end);
                    break;
            }
            key.Append(')');
        }

        private void AppendField(StringBuilder key, WasmFieldType field, int start, int end)
        {
            key.Append(field.Mutable ? " mut" : " const");
            if (field.Storage.IsPacked)
                key.Append(' ').Append(field.Storage.Name);
            else
                AppendValue(key, field.Storage.ValueType, start, end);
        }

        private void AppendValue(StringBuilder key, WasmValueType type, int start, int end)
        {
            key.Append(' ');
            if (!type.IsReference)
            {
                key.Append(type.Name);
                return;
            }
            key.Append(type.Nullable ? "null " : "ref ");
            if (type.HeapType.IsConcrete)
                AppendIndex(key, type.HeapType.TypeIndex, start, end);
            else
                key.Append(type.HeapType.Name);
        }

        // A reference inside the group is kept relative to it, and one outside by the canonical type it names
        private void AppendIndex(StringBuilder key, uint index, int start, int end)
        {
            if (index >= start && index < end)
                key.Append('r').Append(index - start);
            else
                key.Append('c').Append(_canonical[(int)index]);
            key.Append(' ');
        }

        private void CheckComposite(WasmCompositeType composite, int limit)
        {
            switch (composite.Kind)
            {
                case WasmCompositeKind.Function:
                    foreach (var type in composite.Function!.Parameters)
                        CheckValueType(type, limit);
                    foreach (var type in composite.Function.Results)
                        CheckValueType(type, limit);
                    break;
                default:
                    foreach (var field in composite.Fields)
                    {
                        if (!field.Storage.IsPacked)
                            CheckValueType(field.Storage.ValueType, limit);
                    }
                    break;
            }
        }

        public void CheckValueType(WasmValueType type, int limit)
        {
            if (type.IsReference)
                CheckHeapType(type.HeapType, limit);
        }

        public void CheckHeapType(WasmHeapType heapType, int limit)
        {
            if (heapType.IsConcrete && heapType.TypeIndex >= limit)
                throw Error($"Type {heapType.TypeIndex} is not defined");
        }

        public WasmFunctionType FunctionType(uint index)
        {
            if (index >= Types.Count)
                throw Error($"Type {index} is not defined");
            var composite = Types[(int)index].Composite;
            if (composite.Kind != WasmCompositeKind.Function)
                throw Error($"Type {index} is not a function type");
            return composite.Function!;
        }

        public WasmCompositeType CompositeType(uint index, WasmCompositeKind kind)
        {
            if (index >= Types.Count)
                throw Error($"Type {index} is not defined");
            var composite = Types[(int)index].Composite;
            if (composite.Kind != kind)
                throw Error($"Type {index} is not {(kind == WasmCompositeKind.Struct ? "a struct" : "an array")} type");
            return composite;
        }

        private void CheckTagType(uint index)
        {
            var type = FunctionType(index);
            if (type.Results.Length != 0)
                throw Error($"The type {index} of a tag returns values");
        }

        private void CheckTableType(WasmTableType table)
        {
            CheckValueType(table.ElementType, Types.Count);
            ulong limit = table.Limits.Is64Bit ? ulong.MaxValue : uint.MaxValue;
            CheckLimits(table.Limits, limit, "table");
        }

        private void CheckMemoryType(WasmMemoryType memory)
        {
            ulong limit = memory.Limits.Is64Bit ? 1UL << 48 : 1UL << 16;
            CheckLimits(memory.Limits, limit, "memory");
            if (memory.Limits.Shared && !memory.Limits.Maximum.HasValue)
                throw Error("A shared memory has a maximum size");
        }

        private static void CheckLimits(WasmLimits limits, ulong limit, string what)
        {
            if (limits.Minimum > limit)
                throw Error($"The {what} minimum {limits.Minimum} exceeds {limit}");
            if (limits.Maximum is ulong maximum)
            {
                if (maximum > limit)
                    throw Error($"The {what} maximum {maximum} exceeds {limit}");
                if (maximum < limits.Minimum)
                    throw Error($"The {what} maximum {maximum} is below its minimum {limits.Minimum}");
            }
        }

        public bool TypesEquivalent(uint a, uint b) => _canonical[(int)a] == _canonical[(int)b];

        public bool IsIndexSubtype(uint a, uint b)
        {
            for (;;)
            {
                if (TypesEquivalent(a, b))
                    return true;
                var type = Types[(int)a];
                if (type.Supertypes.IsEmpty)
                    return false;
                a = type.Supertypes[0];
            }
        }

        public WasmHeapTypeKind Top(WasmHeapType heapType)
            => heapType.Kind switch
            {
                WasmHeapTypeKind.Concrete => Types[(int)heapType.TypeIndex].Composite.Kind == WasmCompositeKind.Function ? WasmHeapTypeKind.Func : WasmHeapTypeKind.Any,
                WasmHeapTypeKind.Func or WasmHeapTypeKind.NoFunc => WasmHeapTypeKind.Func,
                WasmHeapTypeKind.Extern or WasmHeapTypeKind.NoExtern => WasmHeapTypeKind.Extern,
                WasmHeapTypeKind.Exn or WasmHeapTypeKind.NoExn => WasmHeapTypeKind.Exn,
                _ => WasmHeapTypeKind.Any,
            };

        private WasmCompositeKind? ConcreteKind(WasmHeapType heapType)
            => heapType.IsConcrete ? Types[(int)heapType.TypeIndex].Composite.Kind : null;

        public bool IsHeapSubtype(WasmHeapType a, WasmHeapType b)
        {
            if (a.IsConcrete && b.IsConcrete)
                return IsIndexSubtype(a.TypeIndex, b.TypeIndex);
            if (a == b)
                return true;
            var kind = ConcreteKind(a);
            switch (b.Kind)
            {
                case WasmHeapTypeKind.Any:
                    return Top(a) == WasmHeapTypeKind.Any;
                case WasmHeapTypeKind.Eq:
                    return a.Kind is WasmHeapTypeKind.I31 or WasmHeapTypeKind.Struct or WasmHeapTypeKind.Array or WasmHeapTypeKind.None
                        || kind is WasmCompositeKind.Struct or WasmCompositeKind.Array;
                case WasmHeapTypeKind.I31:
                    return a.Kind == WasmHeapTypeKind.None;
                case WasmHeapTypeKind.Struct:
                    return a.Kind == WasmHeapTypeKind.None || kind == WasmCompositeKind.Struct;
                case WasmHeapTypeKind.Array:
                    return a.Kind == WasmHeapTypeKind.None || kind == WasmCompositeKind.Array;
                case WasmHeapTypeKind.Func:
                    return Top(a) == WasmHeapTypeKind.Func;
                case WasmHeapTypeKind.Extern:
                    return a.Kind == WasmHeapTypeKind.NoExtern;
                case WasmHeapTypeKind.Exn:
                    return a.Kind == WasmHeapTypeKind.NoExn;
                case WasmHeapTypeKind.Concrete:
                    return ConcreteKind(b) == WasmCompositeKind.Function ? a.Kind == WasmHeapTypeKind.NoFunc : a.Kind == WasmHeapTypeKind.None;
                default:
                    return false;
            }
        }

        public bool IsValueSubtype(WasmValueType a, WasmValueType b)
        {
            if (a.Kind != b.Kind)
                return false;
            if (!a.IsReference)
                return true;
            return (!a.Nullable || b.Nullable) && IsHeapSubtype(a.HeapType, b.HeapType);
        }

        public bool IsStorageSubtype(WasmStorageType a, WasmStorageType b)
            => a.IsPacked || b.IsPacked ? a.Packed == b.Packed : IsValueSubtype(a.ValueType, b.ValueType);

        public bool IsFieldSubtype(WasmFieldType a, WasmFieldType b)
        {
            if (a.Mutable != b.Mutable || !IsStorageSubtype(a.Storage, b.Storage))
                return false;
            return !a.Mutable || IsStorageSubtype(b.Storage, a.Storage);
        }

        private bool IsCompositeSubtype(WasmCompositeType a, WasmCompositeType b)
        {
            if (a.Kind != b.Kind)
                return false;
            switch (a.Kind)
            {
                case WasmCompositeKind.Function:
                {
                    var fa = a.Function!;
                    var fb = b.Function!;
                    if (fa.Parameters.Length != fb.Parameters.Length || fa.Results.Length != fb.Results.Length)
                        return false;
                    for (int i = 0; i < fa.Parameters.Length; i++)
                    {
                        if (!IsValueSubtype(fb.Parameters[i], fa.Parameters[i]))
                            return false;
                    }
                    for (int i = 0; i < fa.Results.Length; i++)
                    {
                        if (!IsValueSubtype(fa.Results[i], fb.Results[i]))
                            return false;
                    }
                    return true;
                }
                case WasmCompositeKind.Struct:
                    if (a.Fields.Length < b.Fields.Length)
                        return false;
                    for (int i = 0; i < b.Fields.Length; i++)
                    {
                        if (!IsFieldSubtype(a.Fields[i], b.Fields[i]))
                            return false;
                    }
                    return true;
                default:
                    return IsFieldSubtype(a.Element, b.Element);
            }
        }
    }

    private sealed class Frame
    {
        public WasmInstrKind Kind { get; set; }
        public ImmutableArray<WasmValueType> Start { get; }
        public ImmutableArray<WasmValueType> End { get; }
        public int Height { get; }
        public int InitHeight { get; }
        public bool Unreachable { get; set; }

        public Frame(WasmInstrKind kind, ImmutableArray<WasmValueType> start, ImmutableArray<WasmValueType> end, int height, int initHeight)
        {
            Kind = kind;
            Start = start;
            End = end;
            Height = height;
            InitHeight = initHeight;
        }

        public ImmutableArray<WasmValueType> LabelTypes => Kind == WasmInstrKind.Loop ? Start : End;
    }

    /// <summary>The algorithm of the specification's appendix: an operand stack where null stands for an unknown type</summary>
    private sealed class CodeValidator
    {
        private readonly ModuleContext _context;
        private readonly int _function;
        private readonly int _visibleGlobals;
        private readonly bool _constant;
        private readonly List<WasmValueType?> _values = new List<WasmValueType?>();
        private readonly List<Frame> _frames = new List<Frame>();
        private readonly List<WasmValueType> _locals = new List<WasmValueType>();
        private readonly List<bool> _initialized = new List<bool>();
        private readonly List<int> _initStack = new List<int>();
        private ImmutableArray<WasmValueType> _results = ImmutableArray<WasmValueType>.Empty;
        private int _position;

        public CodeValidator(ModuleContext context, int function, int visibleGlobals = -1)
        {
            _context = context;
            _function = function;
            _constant = function < 0;
            _visibleGlobals = visibleGlobals < 0 ? int.MaxValue : visibleGlobals;
        }

        private WasmValidationException Error(string message)
            => _constant ? new WasmValidationException(message) : new WasmValidationException(message, _function, _position);

        public void ValidateFunction(WasmFunction function)
        {
            var type = _context.FunctionType(function.TypeIndex);
            foreach (var parameter in type.Parameters)
            {
                _locals.Add(parameter);
                _initialized.Add(true);
            }
            foreach (var local in function.Locals)
            {
                _context.CheckValueType(local, _context.Types.Count);
                _locals.Add(local);
                _initialized.Add(local.IsDefaultable);
            }
            _results = type.Results;
            Run(function.Body, type.Results);
        }

        public void ValidateConstant(ImmutableArray<WasmInstruction> expression, WasmValueType type)
        {
            Run(expression, ImmutableArray.Create(type));
        }

        private void Run(IReadOnlyList<WasmInstruction> body, ImmutableArray<WasmValueType> results)
        {
            _frames.Add(new Frame(WasmInstrKind.Block, ImmutableArray<WasmValueType>.Empty, results, 0, 0));
            for (_position = 0; _position < body.Count; _position++)
                Step(body[_position]);
            if (_frames.Count != 1)
                throw Error("A block is left open at the end of the body");
            PopValues(results);
            if (_values.Count != 0)
                throw Error($"The body leaves {_values.Count} more value(s) than it returns");
        }

        private Frame Current => _frames[^1];

        private void Push(WasmValueType? type) => _values.Add(type);

        private void PushValues(ImmutableArray<WasmValueType> types)
        {
            foreach (var type in types)
                _values.Add(type);
        }

        private WasmValueType? Pop()
        {
            var frame = Current;
            if (_values.Count == frame.Height)
            {
                if (frame.Unreachable)
                    return null;
                throw Error("An operand is missing");
            }
            var value = _values[^1];
            _values.RemoveAt(_values.Count - 1);
            return value;
        }

        private WasmValueType? Pop(WasmValueType expected)
        {
            var actual = Pop();
            if (actual is WasmValueType type && !_context.IsValueSubtype(type, expected))
                throw Error($"Expected {expected} but found {type}");
            return actual;
        }

        private WasmValueType? PopReference()
        {
            var actual = Pop();
            if (actual is WasmValueType type && !type.IsReference)
                throw Error($"Expected a reference but found {type}");
            return actual;
        }

        private WasmValueType?[] PopValues(ImmutableArray<WasmValueType> types)
        {
            var popped = new WasmValueType?[types.Length];
            for (int i = types.Length - 1; i >= 0; i--)
                popped[i] = Pop(types[i]);
            return popped;
        }

        private void Unreachable()
        {
            var frame = Current;
            _values.RemoveRange(frame.Height, _values.Count - frame.Height);
            frame.Unreachable = true;
        }

        private void PushFrame(WasmInstrKind kind, ImmutableArray<WasmValueType> start, ImmutableArray<WasmValueType> end)
        {
            _frames.Add(new Frame(kind, start, end, _values.Count, _initStack.Count));
            PushValues(start);
        }

        private Frame PopFrame()
        {
            if (_frames.Count <= 1)
                throw Error("An end or else closes no block");
            var frame = Current;
            PopValues(frame.End);
            if (_values.Count != frame.Height)
                throw Error($"A block leaves {_values.Count - frame.Height} more value(s) than it declares");
            _frames.RemoveAt(_frames.Count - 1);
            for (int i = frame.InitHeight; i < _initStack.Count; i++)
                _initialized[_initStack[i]] = false;
            _initStack.RemoveRange(frame.InitHeight, _initStack.Count - frame.InitHeight);
            return frame;
        }

        private ImmutableArray<WasmValueType> LabelTypes(uint depth)
        {
            if (depth >= _frames.Count)
                throw Error($"Label {depth} does not exist");
            return _frames[_frames.Count - 1 - (int)depth].LabelTypes;
        }

        private (ImmutableArray<WasmValueType> Parameters, ImmutableArray<WasmValueType> Results) BlockSignature(WasmBlockType blockType)
        {
            switch (blockType.Kind)
            {
                case WasmBlockTypeKind.Empty:
                    return (ImmutableArray<WasmValueType>.Empty, ImmutableArray<WasmValueType>.Empty);
                case WasmBlockTypeKind.Value:
                    _context.CheckValueType(blockType.ValueType, _context.Types.Count);
                    return (ImmutableArray<WasmValueType>.Empty, ImmutableArray.Create(blockType.ValueType));
                default:
                    if (blockType.NeedsTypeIndex)
                        throw Error("A block type has not been given a type index");
                    var type = _context.FunctionType(blockType.TypeIndex);
                    return (type.Parameters, type.Results);
            }
        }

        private WasmFunctionType FunctionOf(uint function)
        {
            if (function >= _context.Functions.Count)
                throw Error($"Function {function} does not exist");
            return _context.FunctionType(_context.Functions[(int)function]);
        }

        private WasmTableType Table(uint index)
            => index < _context.Tables.Count ? _context.Tables[(int)index] : throw Error($"Table {index} does not exist");

        private WasmMemoryType Memory(uint index)
            => index < _context.Memories.Count ? _context.Memories[(int)index] : throw Error($"Memory {index} does not exist");

        private WasmGlobalType Global(uint index)
            => index < _context.Globals.Count && index < _visibleGlobals ? _context.Globals[(int)index] : throw Error($"Global {index} does not exist");

        private WasmFunctionType Tag(uint index)
            => index < _context.Tags.Count ? _context.FunctionType(_context.Tags[(int)index]) : throw Error($"Tag {index} does not exist");

        private WasmValueType Element(uint index)
            => index < _context.Elements.Count ? _context.Elements[(int)index] : throw Error($"Element segment {index} does not exist");

        private void Data(uint index)
        {
            if (index >= _context.Module.Data.Count)
                throw Error($"Data segment {index} does not exist");
        }

        private WasmValueType Local(uint index)
            => index < _locals.Count ? _locals[(int)index] : throw Error($"Local {index} does not exist");

        private WasmCompositeType Struct(uint type) => Wrap(() => _context.CompositeType(type, WasmCompositeKind.Struct));
        private WasmCompositeType Array(uint type) => Wrap(() => _context.CompositeType(type, WasmCompositeKind.Array));
        private WasmFunctionType FunctionType(uint type) => Wrap(() => _context.FunctionType(type));

        private T Wrap<T>(Func<T> read)
        {
            try
            {
                return read();
            }
            catch (WasmValidationException exception) when (exception.Function is null && !_constant)
            {
                throw Error(exception.Message);
            }
        }

        private WasmFieldType Field(WasmCompositeType type, uint field)
            => field < type.Fields.Length ? type.Fields[(int)field] : throw Error($"Field {field} does not exist");

        private void CheckReferenceType(WasmValueType type)
            => Wrap(() => { _context.CheckValueType(type, _context.Types.Count); return 0; });

        private static WasmValueType Concrete(uint type, bool nullable) => WasmValueType.Reference(type, nullable);

        private void CheckMemoryArgument(WasmInstruction instruction, WasmInstructionMetadata metadata)
        {
            var argument = instruction.MemoryArgument;
            var memory = Memory(argument.Memory);
            if (argument.Alignment > metadata.NaturalAlignment)
                throw Error($"The alignment of {metadata.Mnemonic} exceeds its natural alignment");
            if (!memory.Limits.Is64Bit && argument.Offset > uint.MaxValue)
                throw Error("The offset does not fit a 32-bit memory");
        }

        private static int LaneCount(string mnemonic)
            => mnemonic.StartsWith("i8x16", StringComparison.Ordinal) ? 16
                : mnemonic.StartsWith("i16x8", StringComparison.Ordinal) ? 8
                : mnemonic.StartsWith("i64x2", StringComparison.Ordinal) || mnemonic.StartsWith("f64x2", StringComparison.Ordinal) ? 2
                : 4;

        private static bool IsConstantInstruction(WasmInstrKind kind)
            => kind is WasmInstrKind.I32Const or WasmInstrKind.I64Const or WasmInstrKind.F32Const or WasmInstrKind.F64Const or WasmInstrKind.V128Const
                or WasmInstrKind.RefNull or WasmInstrKind.RefFunc or WasmInstrKind.RefI31 or WasmInstrKind.GlobalGet
                or WasmInstrKind.StructNew or WasmInstrKind.StructNewDefault or WasmInstrKind.ArrayNew or WasmInstrKind.ArrayNewDefault or WasmInstrKind.ArrayNewFixed
                or WasmInstrKind.AnyConvertExtern or WasmInstrKind.ExternConvertAny
                or WasmInstrKind.I32Add or WasmInstrKind.I32Sub or WasmInstrKind.I32Mul or WasmInstrKind.I64Add or WasmInstrKind.I64Sub or WasmInstrKind.I64Mul;

        private void Step(WasmInstruction instruction)
        {
            if (!WasmInstructionTable.TryGet(instruction.Kind, out var metadata))
                throw Error($"Unknown instruction 0x{(int)instruction.Kind:X}");
            if (!_context.Module.Target.Has(metadata.RequiredIsa))
                throw Error($"{metadata.Mnemonic} needs {metadata.RequiredIsa & ~_context.Module.Target.Isa}, which the target does not have");
            if (instruction.HasSymbol)
                throw Error($"{metadata.Mnemonic} still refers to the symbol {instruction.Symbol}");
            if (_constant && !IsConstantInstruction(instruction.Kind))
                throw Error($"{metadata.Mnemonic} is not a constant instruction");

            if (metadata.Signature is not null)
            {
                StepSignature(instruction, metadata);
                return;
            }

            switch (instruction.Kind)
            {
                case WasmInstrKind.Unreachable:
                    Unreachable();
                    break;
                case WasmInstrKind.Block:
                case WasmInstrKind.Loop:
                {
                    var (parameters, results) = BlockSignature(instruction.BlockType);
                    PopValues(parameters);
                    PushFrame(instruction.Kind, parameters, results);
                    break;
                }
                case WasmInstrKind.If:
                {
                    var (parameters, results) = BlockSignature(instruction.BlockType);
                    Pop(WasmValueType.I32);
                    PopValues(parameters);
                    PushFrame(WasmInstrKind.If, parameters, results);
                    break;
                }
                case WasmInstrKind.Else:
                {
                    if (Current.Kind != WasmInstrKind.If)
                        throw Error("An else follows no if");
                    var frame = PopFrame();
                    PushFrame(WasmInstrKind.Else, frame.Start, frame.End);
                    break;
                }
                case WasmInstrKind.End:
                {
                    var frame = PopFrame();
                    // An if without an else passes its parameters through the missing branch
                    if (frame.Kind == WasmInstrKind.If)
                    {
                        if (frame.Start.Length != frame.End.Length || frame.Start.Where((type, i) => !_context.IsValueSubtype(type, frame.End[i])).Any())
                            throw Error("An if without an else must leave what it takes");
                    }
                    PushValues(frame.End);
                    break;
                }
                case WasmInstrKind.TryTable:
                {
                    var (parameters, results) = BlockSignature(instruction.BlockType);
                    foreach (var handler in instruction.Catches)
                    {
                        var payload = new List<WasmValueType>();
                        if (handler.HasTag)
                            payload.AddRange(Tag(handler.Tag).Parameters);
                        if (handler.Kind is WasmCatchKind.CatchRef or WasmCatchKind.CatchAllRef)
                            payload.Add(WasmValueType.Reference(WasmHeapType.Exn, false));
                        var label = LabelTypes(handler.Label);
                        if (label.Length != payload.Count || payload.Where((type, i) => !_context.IsValueSubtype(type, label[i])).Any())
                            throw Error($"A catch clause delivers ({string.Join(" ", payload)}) to a label that takes ({string.Join(" ", label)})");
                    }
                    PopValues(parameters);
                    PushFrame(WasmInstrKind.TryTable, parameters, results);
                    break;
                }
                case WasmInstrKind.Throw:
                    PopValues(Tag(instruction.Index).Parameters);
                    Unreachable();
                    break;
                case WasmInstrKind.ThrowRef:
                    Pop(WasmValueType.ExnRef);
                    Unreachable();
                    break;
                case WasmInstrKind.Br:
                    PopValues(LabelTypes(instruction.Index));
                    Unreachable();
                    break;
                case WasmInstrKind.BrIf:
                {
                    var label = LabelTypes(instruction.Index);
                    Pop(WasmValueType.I32);
                    PopValues(label);
                    PushValues(label);
                    break;
                }
                case WasmInstrKind.BrTable:
                {
                    Pop(WasmValueType.I32);
                    var fallback = LabelTypes(instruction.Index);
                    foreach (var target in instruction.Labels)
                    {
                        var label = LabelTypes(target);
                        if (label.Length != fallback.Length)
                            throw Error("The targets of br_table take different numbers of values");
                        var popped = PopValues(label);
                        foreach (var value in popped)
                            Push(value);
                    }
                    PopValues(fallback);
                    Unreachable();
                    break;
                }
                case WasmInstrKind.Return:
                    PopValues(_results);
                    Unreachable();
                    break;
                case WasmInstrKind.Call:
                {
                    var type = FunctionOf(instruction.Index);
                    PopValues(type.Parameters);
                    PushValues(type.Results);
                    break;
                }
                case WasmInstrKind.ReturnCall:
                {
                    var type = FunctionOf(instruction.Index);
                    CheckTailCall(type);
                    PopValues(type.Parameters);
                    Unreachable();
                    break;
                }
                case WasmInstrKind.CallIndirect:
                case WasmInstrKind.ReturnCallIndirect:
                {
                    if (instruction.Index == uint.MaxValue)
                        throw Error("call_indirect has not been given a type index");
                    var table = Table(instruction.Index2);
                    if (!_context.IsValueSubtype(table.ElementType, WasmValueType.FuncRef))
                        throw Error($"call_indirect needs a table of functions, but table {instruction.Index2} holds {table.ElementType}");
                    var type = FunctionType(instruction.Index);
                    Pop(table.AddressType);
                    PopValues(type.Parameters);
                    if (instruction.Kind == WasmInstrKind.ReturnCallIndirect)
                    {
                        CheckTailCall(type);
                        Unreachable();
                    }
                    else
                        PushValues(type.Results);
                    break;
                }
                case WasmInstrKind.CallRef:
                case WasmInstrKind.ReturnCallRef:
                {
                    var type = FunctionType(instruction.Index);
                    Pop(Concrete(instruction.Index, true));
                    PopValues(type.Parameters);
                    if (instruction.Kind == WasmInstrKind.ReturnCallRef)
                    {
                        CheckTailCall(type);
                        Unreachable();
                    }
                    else
                        PushValues(type.Results);
                    break;
                }
                case WasmInstrKind.Drop:
                    Pop();
                    break;
                case WasmInstrKind.Select:
                {
                    Pop(WasmValueType.I32);
                    var first = Pop();
                    var second = Pop();
                    if (first is { IsReference: true } || second is { IsReference: true })
                        throw Error("select without a type cannot choose between references");
                    if (first is WasmValueType a && second is WasmValueType b && a != b)
                        throw Error($"select chooses between {b} and {a}");
                    Push(first ?? second);
                    break;
                }
                case WasmInstrKind.SelectTyped:
                {
                    if (instruction.Types.Length != 1)
                        throw Error("select names exactly one type");
                    var type = instruction.Types[0];
                    CheckReferenceType(type);
                    Pop(WasmValueType.I32);
                    Pop(type);
                    Pop(type);
                    Push(type);
                    break;
                }
                case WasmInstrKind.LocalGet:
                {
                    var type = Local(instruction.Index);
                    if (!_initialized[(int)instruction.Index])
                        throw Error($"Local {instruction.Index} is read before it is set");
                    Push(type);
                    break;
                }
                case WasmInstrKind.LocalSet:
                    Pop(Local(instruction.Index));
                    Initialize(instruction.Index);
                    break;
                case WasmInstrKind.LocalTee:
                {
                    var type = Local(instruction.Index);
                    Pop(type);
                    Initialize(instruction.Index);
                    Push(type);
                    break;
                }
                case WasmInstrKind.GlobalGet:
                {
                    var global = Global(instruction.Index);
                    if (_constant && global.Mutable)
                        throw Error($"Global {instruction.Index} is mutable, so a constant expression cannot read it");
                    Push(global.ValueType);
                    break;
                }
                case WasmInstrKind.GlobalSet:
                {
                    var global = Global(instruction.Index);
                    if (!global.Mutable)
                        throw Error($"Global {instruction.Index} is immutable");
                    Pop(global.ValueType);
                    break;
                }
                case WasmInstrKind.TableGet:
                {
                    var table = Table(instruction.Index);
                    Pop(table.AddressType);
                    Push(table.ElementType);
                    break;
                }
                case WasmInstrKind.TableSet:
                {
                    var table = Table(instruction.Index);
                    Pop(table.ElementType);
                    Pop(table.AddressType);
                    break;
                }
                case WasmInstrKind.TableGrow:
                {
                    var table = Table(instruction.Index);
                    Pop(table.AddressType);
                    Pop(table.ElementType);
                    Push(table.AddressType);
                    break;
                }
                case WasmInstrKind.TableFill:
                {
                    var table = Table(instruction.Index);
                    Pop(table.AddressType);
                    Pop(table.ElementType);
                    Pop(table.AddressType);
                    break;
                }
                case WasmInstrKind.TableCopy:
                {
                    var destination = Table(instruction.Index);
                    var source = Table(instruction.Index2);
                    if (!_context.IsValueSubtype(source.ElementType, destination.ElementType))
                        throw Error($"table.copy cannot put {source.ElementType} into a table of {destination.ElementType}");
                    Pop(destination.Limits.Is64Bit && source.Limits.Is64Bit ? WasmValueType.I64 : WasmValueType.I32);
                    Pop(source.AddressType);
                    Pop(destination.AddressType);
                    break;
                }
                case WasmInstrKind.MemoryCopy:
                {
                    var destination = Memory(instruction.Index);
                    var source = Memory(instruction.Index2);
                    Pop(destination.Limits.Is64Bit && source.Limits.Is64Bit ? WasmValueType.I64 : WasmValueType.I32);
                    Pop(source.AddressType);
                    Pop(destination.AddressType);
                    break;
                }
                case WasmInstrKind.RefNull:
                    CheckReferenceType(instruction.Type);
                    Push(WasmValueType.Reference(instruction.Type.HeapType, true));
                    break;
                case WasmInstrKind.RefIsNull:
                    PopReference();
                    Push(WasmValueType.I32);
                    break;
                case WasmInstrKind.RefFunc:
                    FunctionOf(instruction.Index);
                    if (!_constant && !_context.References.Contains(instruction.Index))
                        throw Error($"ref.func names function {instruction.Index}, which the module does not declare as referenced");
                    Push(Concrete(_context.Functions[(int)instruction.Index], false));
                    break;
                case WasmInstrKind.RefEq:
                    Pop(WasmValueType.EqRef);
                    Pop(WasmValueType.EqRef);
                    Push(WasmValueType.I32);
                    break;
                case WasmInstrKind.RefAsNonNull:
                {
                    var type = PopReference();
                    Push(type?.AsNullable(false));
                    break;
                }
                case WasmInstrKind.BrOnNull:
                {
                    var label = LabelTypes(instruction.Index);
                    var type = PopReference();
                    PopValues(label);
                    PushValues(label);
                    Push(type?.AsNullable(false));
                    break;
                }
                case WasmInstrKind.BrOnNonNull:
                {
                    var label = LabelTypes(instruction.Index);
                    if (label.Length == 0 || !label[^1].IsReference)
                        throw Error("br_on_non_null branches to a label that takes a reference last");
                    var type = PopReference();
                    if (type is WasmValueType reference && !_context.IsValueSubtype(reference.AsNullable(false), label[^1]))
                        throw Error($"br_on_non_null delivers {reference.AsNullable(false)} to a label that takes {label[^1]}");
                    var rest = label.RemoveAt(label.Length - 1);
                    PopValues(rest);
                    PushValues(rest);
                    break;
                }
                case WasmInstrKind.StructNew:
                {
                    var type = Struct(instruction.Index);
                    for (int i = type.Fields.Length - 1; i >= 0; i--)
                        Pop(type.Fields[i].Storage.Unpacked);
                    Push(Concrete(instruction.Index, false));
                    break;
                }
                case WasmInstrKind.StructNewDefault:
                {
                    var type = Struct(instruction.Index);
                    if (type.Fields.Any(static field => !field.Storage.Unpacked.IsDefaultable))
                        throw Error($"Type {instruction.Index} has a field without a default value");
                    Push(Concrete(instruction.Index, false));
                    break;
                }
                case WasmInstrKind.StructGet:
                case WasmInstrKind.StructGetS:
                case WasmInstrKind.StructGetU:
                {
                    var field = Field(Struct(instruction.Index), instruction.Index2);
                    CheckPacking(field, instruction.Kind == WasmInstrKind.StructGet);
                    Pop(Concrete(instruction.Index, true));
                    Push(field.Storage.Unpacked);
                    break;
                }
                case WasmInstrKind.StructSet:
                {
                    var field = Field(Struct(instruction.Index), instruction.Index2);
                    if (!field.Mutable)
                        throw Error($"Field {instruction.Index2} of type {instruction.Index} is immutable");
                    Pop(field.Storage.Unpacked);
                    Pop(Concrete(instruction.Index, true));
                    break;
                }
                case WasmInstrKind.ArrayNew:
                    Pop(WasmValueType.I32);
                    Pop(Array(instruction.Index).Element.Storage.Unpacked);
                    Push(Concrete(instruction.Index, false));
                    break;
                case WasmInstrKind.ArrayNewDefault:
                    if (!Array(instruction.Index).Element.Storage.Unpacked.IsDefaultable)
                        throw Error($"The elements of type {instruction.Index} have no default value");
                    Pop(WasmValueType.I32);
                    Push(Concrete(instruction.Index, false));
                    break;
                case WasmInstrKind.ArrayNewFixed:
                {
                    var element = Array(instruction.Index).Element.Storage.Unpacked;
                    for (uint i = 0; i < instruction.Index2; i++)
                        Pop(element);
                    Push(Concrete(instruction.Index, false));
                    break;
                }
                case WasmInstrKind.ArrayNewData:
                    CheckDataArray(instruction.Index, false);
                    Data(instruction.Index2);
                    Pop(WasmValueType.I32);
                    Pop(WasmValueType.I32);
                    Push(Concrete(instruction.Index, false));
                    break;
                case WasmInstrKind.ArrayNewElem:
                    CheckElementArray(instruction.Index, instruction.Index2, false);
                    Pop(WasmValueType.I32);
                    Pop(WasmValueType.I32);
                    Push(Concrete(instruction.Index, false));
                    break;
                case WasmInstrKind.ArrayGet:
                case WasmInstrKind.ArrayGetS:
                case WasmInstrKind.ArrayGetU:
                {
                    var element = Array(instruction.Index).Element;
                    CheckPacking(element, instruction.Kind == WasmInstrKind.ArrayGet);
                    Pop(WasmValueType.I32);
                    Pop(Concrete(instruction.Index, true));
                    Push(element.Storage.Unpacked);
                    break;
                }
                case WasmInstrKind.ArraySet:
                {
                    var element = MutableElement(instruction.Index);
                    Pop(element.Storage.Unpacked);
                    Pop(WasmValueType.I32);
                    Pop(Concrete(instruction.Index, true));
                    break;
                }
                case WasmInstrKind.ArrayLen:
                    Pop(WasmValueType.ArrayRef);
                    Push(WasmValueType.I32);
                    break;
                case WasmInstrKind.ArrayFill:
                {
                    var element = MutableElement(instruction.Index);
                    Pop(WasmValueType.I32);
                    Pop(element.Storage.Unpacked);
                    Pop(WasmValueType.I32);
                    Pop(Concrete(instruction.Index, true));
                    break;
                }
                case WasmInstrKind.ArrayCopy:
                {
                    var destination = MutableElement(instruction.Index);
                    var source = Array(instruction.Index2).Element;
                    if (!_context.IsStorageSubtype(source.Storage, destination.Storage))
                        throw Error($"array.copy cannot put elements of type {instruction.Index2} into type {instruction.Index}");
                    Pop(WasmValueType.I32);
                    Pop(WasmValueType.I32);
                    Pop(Concrete(instruction.Index2, true));
                    Pop(WasmValueType.I32);
                    Pop(Concrete(instruction.Index, true));
                    break;
                }
                case WasmInstrKind.ArrayInitData:
                    CheckDataArray(instruction.Index, true);
                    Data(instruction.Index2);
                    Pop(WasmValueType.I32);
                    Pop(WasmValueType.I32);
                    Pop(WasmValueType.I32);
                    Pop(Concrete(instruction.Index, true));
                    break;
                case WasmInstrKind.ArrayInitElem:
                    CheckElementArray(instruction.Index, instruction.Index2, true);
                    Pop(WasmValueType.I32);
                    Pop(WasmValueType.I32);
                    Pop(WasmValueType.I32);
                    Pop(Concrete(instruction.Index, true));
                    break;
                case WasmInstrKind.RefTest:
                case WasmInstrKind.RefTestNull:
                case WasmInstrKind.RefCast:
                case WasmInstrKind.RefCastNull:
                {
                    var type = instruction.Type.AsNullable(instruction.Kind is WasmInstrKind.RefTestNull or WasmInstrKind.RefCastNull);
                    CheckReferenceType(type);
                    Pop(WasmValueType.Reference(WasmHeapType.Abstract(_context.Top(type.HeapType)), true));
                    Push(instruction.Kind is WasmInstrKind.RefTest or WasmInstrKind.RefTestNull ? WasmValueType.I32 : type);
                    break;
                }
                case WasmInstrKind.BrOnCast:
                case WasmInstrKind.BrOnCastFail:
                {
                    var from = instruction.Type;
                    var to = instruction.Type2;
                    CheckReferenceType(from);
                    CheckReferenceType(to);
                    if (!_context.IsValueSubtype(to, from))
                        throw Error($"{metadata.Mnemonic} casts {from} to {to}, which is not a subtype of it");
                    var label = LabelTypes(instruction.Index);
                    if (label.Length == 0 || !label[^1].IsReference)
                        throw Error($"{metadata.Mnemonic} branches to a label that takes a reference last");
                    var difference = from.AsNullable(from.Nullable && !to.Nullable);
                    var branched = instruction.Kind == WasmInstrKind.BrOnCast ? to : difference;
                    if (!_context.IsValueSubtype(branched, label[^1]))
                        throw Error($"{metadata.Mnemonic} delivers {branched} to a label that takes {label[^1]}");
                    Pop(from);
                    var rest = label.RemoveAt(label.Length - 1);
                    PopValues(rest);
                    PushValues(rest);
                    Push(instruction.Kind == WasmInstrKind.BrOnCast ? difference : to);
                    break;
                }
                case WasmInstrKind.AnyConvertExtern:
                {
                    var type = Pop(WasmValueType.ExternRef);
                    Push(WasmValueType.Reference(WasmHeapType.Any, type?.Nullable ?? false));
                    break;
                }
                case WasmInstrKind.ExternConvertAny:
                {
                    var type = Pop(WasmValueType.AnyRef);
                    Push(WasmValueType.Reference(WasmHeapType.Extern, type?.Nullable ?? false));
                    break;
                }
                case WasmInstrKind.RefI31:
                    Pop(WasmValueType.I32);
                    Push(WasmValueType.Reference(WasmHeapType.I31, false));
                    break;
                case WasmInstrKind.I31GetS:
                case WasmInstrKind.I31GetU:
                    Pop(WasmValueType.I31Ref);
                    Push(WasmValueType.I32);
                    break;
                default:
                    throw Error($"No typing rule for {metadata.Mnemonic}");
            }
        }

        private void Initialize(uint local)
        {
            if (_initialized[(int)local])
                return;
            _initialized[(int)local] = true;
            _initStack.Add((int)local);
        }

        private void CheckTailCall(WasmFunctionType callee)
        {
            if (callee.Results.Length != _results.Length || callee.Results.Where((type, i) => !_context.IsValueSubtype(type, _results[i])).Any())
                throw Error("A tail call returns what the caller does not");
        }

        private void CheckPacking(WasmFieldType field, bool plain)
        {
            if (plain == field.Storage.IsPacked)
                throw Error(plain ? "A packed field is read with a sign or zero extension" : "Only a packed field is read with an extension");
        }

        private WasmFieldType MutableElement(uint type)
        {
            var element = Array(type).Element;
            if (!element.Mutable)
                throw Error($"The elements of type {type} are immutable");
            return element;
        }

        private void CheckDataArray(uint type, bool mutable)
        {
            var element = mutable ? MutableElement(type) : Array(type).Element;
            if (element.Storage.Unpacked.IsReference)
                throw Error($"The elements of type {type} are references, which data cannot hold");
        }

        private void CheckElementArray(uint type, uint segment, bool mutable)
        {
            var element = mutable ? MutableElement(type) : Array(type).Element;
            var segmentType = Element(segment);
            if (element.Storage.IsPacked || !_context.IsValueSubtype(segmentType, element.Storage.ValueType))
                throw Error($"Element segment {segment} of {segmentType} cannot fill type {type}");
        }

        private void StepSignature(WasmInstruction instruction, WasmInstructionMetadata metadata)
        {
            WasmValueType address = WasmValueType.I32;
            WasmValueType tableAddress = WasmValueType.I32;
            switch (metadata.Format)
            {
                case WasmInstructionFormat.MemoryArgument:
                    CheckMemoryArgument(instruction, metadata);
                    address = Memory(instruction.MemoryArgument.Memory).AddressType;
                    break;
                case WasmInstructionFormat.MemoryLane:
                    CheckMemoryArgument(instruction, metadata);
                    address = Memory(instruction.MemoryArgument.Memory).AddressType;
                    if (instruction.Index2 >= 16 >> metadata.NaturalAlignment)
                        throw Error($"Lane {instruction.Index2} does not exist");
                    break;
                case WasmInstructionFormat.Memory:
                    address = Memory(instruction.Index).AddressType;
                    break;
                case WasmInstructionFormat.MemoryInit:
                    address = Memory(instruction.Index2).AddressType;
                    Data(instruction.Index);
                    break;
                case WasmInstructionFormat.Data:
                    Data(instruction.Index);
                    break;
                case WasmInstructionFormat.Table:
                    tableAddress = Table(instruction.Index).AddressType;
                    break;
                case WasmInstructionFormat.TableInit:
                {
                    var table = Table(instruction.Index2);
                    tableAddress = table.AddressType;
                    var element = Element(instruction.Index);
                    if (!_context.IsValueSubtype(element, table.ElementType))
                        throw Error($"Element segment {instruction.Index} of {element} cannot fill a table of {table.ElementType}");
                    break;
                }
                case WasmInstructionFormat.Element:
                    Element(instruction.Index);
                    break;
                case WasmInstructionFormat.Lane:
                    if (instruction.Index2 >= LaneCount(metadata.Mnemonic))
                        throw Error($"Lane {instruction.Index2} does not exist");
                    break;
                case WasmInstructionFormat.Shuffle:
                    if (instruction.Bytes.Length != 16 || instruction.Bytes.Any(static lane => lane >= 32))
                        throw Error("A shuffle picks sixteen lanes out of thirty-two");
                    break;
                case WasmInstructionFormat.V128:
                    if (instruction.Bytes.Length != 16)
                        throw Error("A v128 constant has sixteen bytes");
                    break;
            }

            string signature = metadata.Signature!;
            int colon = signature.IndexOf(':');
            for (int i = colon - 1; i >= 0; i--)
                Pop(TypeOf(signature[i], address, tableAddress));
            for (int i = colon + 1; i < signature.Length; i++)
                Push(TypeOf(signature[i], address, tableAddress));
        }

        private static WasmValueType TypeOf(char code, WasmValueType address, WasmValueType tableAddress)
            => code switch
            {
                'i' => WasmValueType.I32,
                'I' => WasmValueType.I64,
                'f' => WasmValueType.F32,
                'F' => WasmValueType.F64,
                'v' => WasmValueType.V128,
                'a' => address,
                't' => tableAddress,
                _ => throw new InvalidOperationException($"Unknown operand code {code}"),
            };
    }
}
