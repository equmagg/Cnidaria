using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Cnidaria.Wasm;

public sealed class WasmBinaryException : FormatException
{
    public int Offset { get; }

    public WasmBinaryException(string message, int offset)
        : base($"{message} (at byte 0x{offset:X})")
    {
        Offset = offset;
    }
}

internal sealed class WasmByteWriter
{
    private readonly List<byte> _bytes = new List<byte>();

    public int Length => _bytes.Count;

    public void Byte(byte value) => _bytes.Add(value);

    public void Bytes(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
            _bytes.Add(b);
    }

    public void Bytes(WasmByteWriter other) => _bytes.AddRange(other._bytes);

    public void U32(uint value) => U64(value);

    public void U64(ulong value)
    {
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
                b |= 0x80;
            _bytes.Add(b);
        }
        while (value != 0);
    }

    public void S64(long value)
    {
        for (;;)
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            bool done = value == 0 && (b & 0x40) == 0 || value == -1 && (b & 0x40) != 0;
            _bytes.Add(done ? b : (byte)(b | 0x80));
            if (done)
                return;
        }
    }

    public void Name(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        U32((uint)bytes.Length);
        Bytes(bytes);
    }

    public void Vector(ReadOnlySpan<byte> bytes)
    {
        U32((uint)bytes.Length);
        Bytes(bytes);
    }

    public void Sized(WasmByteWriter content)
    {
        U32((uint)content.Length);
        Bytes(content);
    }

    public byte[] ToArray() => _bytes.ToArray();
}

internal ref struct WasmByteReader
{
    private readonly ReadOnlySpan<byte> _bytes;
    private readonly int _base;

    public int Position { get; set; }

    public WasmByteReader(ReadOnlySpan<byte> bytes, int baseOffset = 0)
    {
        _bytes = bytes;
        _base = baseOffset;
        Position = 0;
    }

    public int Offset => _base + Position;
    public int Length => _bytes.Length;
    public bool AtEnd => Position >= _bytes.Length;

    public WasmBinaryException Error(string message) => new WasmBinaryException(message, Offset);

    public byte Byte()
    {
        if (Position >= _bytes.Length)
            throw Error("Unexpected end of input");
        return _bytes[Position++];
    }

    public byte Peek()
    {
        if (Position >= _bytes.Length)
            throw Error("Unexpected end of input");
        return _bytes[Position];
    }

    public ReadOnlySpan<byte> Bytes(int count)
    {
        if (count < 0 || count > _bytes.Length - Position)
            throw Error("Unexpected end of input");
        var span = _bytes.Slice(Position, count);
        Position += count;
        return span;
    }

    public uint U32() => (uint)Unsigned(32);
    public ulong U64() => Unsigned(64);

    private ulong Unsigned(int bits)
    {
        ulong result = 0;
        int shift = 0;
        int maxBytes = (bits + 6) / 7;
        for (int i = 0; ; i++)
        {
            if (i == maxBytes)
                throw Error("An integer is too long");
            byte b = Byte();
            if (i == maxBytes - 1 && (b & 0x7F) >> (bits - shift) != 0)
                throw Error("An integer is too large");
            result |= (ulong)(b & 0x7F) << shift;
            shift += 7;
            if ((b & 0x80) == 0)
                return result;
        }
    }

    public int S32() => (int)Signed(32);
    public long S64() => Signed(64);

    public long Signed(int bits)
    {
        long result = 0;
        int shift = 0;
        int maxBytes = (bits + 6) / 7;
        for (int i = 0; ; i++)
        {
            if (i == maxBytes)
                throw Error("An integer is too long");
            byte b = Byte();
            if (i == maxBytes - 1)
            {
                // The unused bits of the last byte must repeat the sign
                int used = bits - shift;
                int rest = (b & 0x7F) >> (used - 1);
                if (rest != 0 && rest != (0x7F >> (used - 1)))
                    throw Error("An integer is too large");
            }
            result |= (long)(b & 0x7F) << shift;
            shift += 7;
            if ((b & 0x80) == 0)
            {
                if (shift < 64 && (b & 0x40) != 0)
                    result |= -1L << shift;
                return result;
            }
        }
    }

    public string Name()
    {
        int start = Offset;
        var bytes = Bytes(checked((int)U32()));
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new WasmBinaryException("A name is not valid UTF-8", start);
        }
    }

    public uint Count()
    {
        uint count = U32();
        if (count > _bytes.Length - Position)
            throw Error("A vector is longer than the input");
        return count;
    }
}

internal static class WasmTypeEncoding
{
    public static void WriteHeapType(WasmByteWriter output, WasmHeapType heapType)
    {
        if (heapType.IsConcrete)
            output.S64(heapType.TypeIndex);
        else
            output.Byte(heapType.Code);
    }

    public static void WriteValueType(WasmByteWriter output, WasmValueType type)
    {
        switch (type.Kind)
        {
            case WasmValueKind.I32: output.Byte(0x7F); break;
            case WasmValueKind.I64: output.Byte(0x7E); break;
            case WasmValueKind.F32: output.Byte(0x7D); break;
            case WasmValueKind.F64: output.Byte(0x7C); break;
            case WasmValueKind.V128: output.Byte(0x7B); break;
            default:
                if (type.Nullable && !type.HeapType.IsConcrete)
                {
                    output.Byte(type.HeapType.Code);
                    break;
                }
                output.Byte(type.Nullable ? (byte)0x63 : (byte)0x64);
                WriteHeapType(output, type.HeapType);
                break;
        }
    }

    public static void WriteValueTypes(WasmByteWriter output, ImmutableArray<WasmValueType> types)
    {
        output.U32((uint)types.Length);
        foreach (var type in types)
            WriteValueType(output, type);
    }

    public static void WriteStorageType(WasmByteWriter output, WasmStorageType storage)
    {
        switch (storage.Packed)
        {
            case WasmPackedType.I8: output.Byte(0x78); break;
            case WasmPackedType.I16: output.Byte(0x77); break;
            default: WriteValueType(output, storage.ValueType); break;
        }
    }

    public static void WriteFieldType(WasmByteWriter output, WasmFieldType field)
    {
        WriteStorageType(output, field.Storage);
        output.Byte(field.Mutable ? (byte)1 : (byte)0);
    }

    public static void WriteCompositeType(WasmByteWriter output, WasmCompositeType composite)
    {
        switch (composite.Kind)
        {
            case WasmCompositeKind.Function:
                output.Byte(0x60);
                WriteValueTypes(output, composite.Function!.Parameters);
                WriteValueTypes(output, composite.Function.Results);
                break;
            case WasmCompositeKind.Struct:
                output.Byte(0x5F);
                output.U32((uint)composite.Fields.Length);
                foreach (var field in composite.Fields)
                    WriteFieldType(output, field);
                break;
            default:
                output.Byte(0x5E);
                WriteFieldType(output, composite.Element);
                break;
        }
    }

    public static void WriteSubType(WasmByteWriter output, WasmSubType type)
    {
        if (!type.IsPlain)
        {
            output.Byte(type.Final ? (byte)0x4F : (byte)0x50);
            output.U32((uint)type.Supertypes.Length);
            foreach (var supertype in type.Supertypes)
                output.U32(supertype);
        }
        WriteCompositeType(output, type.Composite);
    }

    public static void WriteRecursionGroup(WasmByteWriter output, WasmRecursionGroup group)
    {
        if (group.Types.Length != 1)
        {
            output.Byte(0x4E);
            output.U32((uint)group.Types.Length);
        }
        foreach (var type in group.Types)
            WriteSubType(output, type);
    }

    public static void WriteLimits(WasmByteWriter output, WasmLimits limits)
    {
        byte flags = (byte)((limits.Maximum.HasValue ? 1 : 0) | (limits.Shared ? 2 : 0) | (limits.Is64Bit ? 4 : 0));
        output.Byte(flags);
        output.U64(limits.Minimum);
        if (limits.Maximum is ulong maximum)
            output.U64(maximum);
    }

    public static void WriteTableType(WasmByteWriter output, WasmTableType table)
    {
        WriteValueType(output, table.ElementType);
        WriteLimits(output, table.Limits);
    }

    public static void WriteGlobalType(WasmByteWriter output, WasmGlobalType global)
    {
        WriteValueType(output, global.ValueType);
        output.Byte(global.Mutable ? (byte)1 : (byte)0);
    }

    public static WasmHeapType ReadHeapType(ref WasmByteReader input)
    {
        byte first = input.Peek();
        if (WasmHeapType.TryFromCode(first, out var heapType))
        {
            input.Byte();
            return heapType;
        }
        long index = input.Signed(33);
        if (index < 0)
            throw input.Error($"Unknown heap type 0x{first:X2}");
        return WasmHeapType.Concrete(checked((uint)index));
    }

    public static WasmValueType ReadValueType(ref WasmByteReader input)
    {
        byte code = input.Byte();
        switch (code)
        {
            case 0x7F: return WasmValueType.I32;
            case 0x7E: return WasmValueType.I64;
            case 0x7D: return WasmValueType.F32;
            case 0x7C: return WasmValueType.F64;
            case 0x7B: return WasmValueType.V128;
            case 0x63: return WasmValueType.Reference(ReadHeapType(ref input), true);
            case 0x64: return WasmValueType.Reference(ReadHeapType(ref input), false);
        }
        if (WasmHeapType.TryFromCode(code, out var heapType))
            return WasmValueType.Reference(heapType, true);
        input.Position--;
        throw input.Error($"Unknown value type 0x{code:X2}");
    }

    public static WasmValueType ReadReferenceType(ref WasmByteReader input)
    {
        int start = input.Offset;
        var type = ReadValueType(ref input);
        if (!type.IsReference)
            throw new WasmBinaryException("A reference type was expected", start);
        return type;
    }

    public static ImmutableArray<WasmValueType> ReadValueTypes(ref WasmByteReader input)
    {
        uint count = input.Count();
        var types = ImmutableArray.CreateBuilder<WasmValueType>((int)count);
        for (uint i = 0; i < count; i++)
            types.Add(ReadValueType(ref input));
        return types.MoveToImmutable();
    }

    public static WasmFieldType ReadFieldType(ref WasmByteReader input)
    {
        WasmStorageType storage;
        switch (input.Peek())
        {
            case 0x78: input.Byte(); storage = WasmStorageType.I8; break;
            case 0x77: input.Byte(); storage = WasmStorageType.I16; break;
            default: storage = ReadValueType(ref input); break;
        }
        byte mutability = input.Byte();
        if (mutability > 1)
            throw input.Error("Mutability is 0 or 1");
        return new WasmFieldType(storage, mutability == 1);
    }

    public static WasmCompositeType ReadCompositeType(ref WasmByteReader input)
    {
        byte form = input.Byte();
        switch (form)
        {
            case 0x60:
            {
                var parameters = ReadValueTypes(ref input);
                var results = ReadValueTypes(ref input);
                return WasmCompositeType.Of(new WasmFunctionType(parameters, results));
            }
            case 0x5F:
            {
                uint count = input.Count();
                var fields = new WasmFieldType[count];
                for (uint i = 0; i < count; i++)
                    fields[i] = ReadFieldType(ref input);
                return WasmCompositeType.Struct(fields);
            }
            case 0x5E:
                return WasmCompositeType.Array(ReadFieldType(ref input));
            default:
                input.Position--;
                throw input.Error($"Unknown type form 0x{form:X2}");
        }
    }

    public static WasmSubType ReadSubType(ref WasmByteReader input)
    {
        byte form = input.Peek();
        if (form is not 0x50 and not 0x4F)
            return WasmSubType.Of(ReadCompositeType(ref input));
        input.Byte();
        uint count = input.Count();
        var supertypes = new uint[count];
        for (uint i = 0; i < count; i++)
            supertypes[i] = input.U32();
        return new WasmSubType(form == 0x4F, supertypes, ReadCompositeType(ref input));
    }

    public static WasmRecursionGroup ReadRecursionGroup(ref WasmByteReader input)
    {
        if (input.Peek() != 0x4E)
            return new WasmRecursionGroup(ReadSubType(ref input));
        input.Byte();
        uint count = input.Count();
        var types = new WasmSubType[count];
        for (uint i = 0; i < count; i++)
            types[i] = ReadSubType(ref input);
        return new WasmRecursionGroup(types);
    }

    public static WasmLimits ReadLimits(ref WasmByteReader input)
    {
        byte flags = input.Byte();
        if (flags > 7)
            throw input.Error($"Unknown limits flags 0x{flags:X2}");
        bool is64 = (flags & 4) != 0;
        ulong minimum = is64 ? input.U64() : input.U32();
        ulong? maximum = (flags & 1) != 0 ? is64 ? input.U64() : input.U32() : null;
        return new WasmLimits(minimum, maximum, (flags & 2) != 0, is64);
    }

    public static WasmTableType ReadTableType(ref WasmByteReader input)
    {
        var elementType = ReadReferenceType(ref input);
        return new WasmTableType(elementType, ReadLimits(ref input));
    }

    public static WasmGlobalType ReadGlobalType(ref WasmByteReader input)
    {
        var type = ReadValueType(ref input);
        byte mutability = input.Byte();
        if (mutability > 1)
            throw input.Error("Mutability is 0 or 1");
        return new WasmGlobalType(type, mutability == 1);
    }
}

internal static class WasmCodeEncoder
{
    public static void Encode(WasmByteWriter output, IEnumerable<WasmInstruction> instructions)
    {
        foreach (var instruction in instructions)
            Encode(output, instruction);
    }

    public static byte[] Encode(IEnumerable<WasmInstruction> instructions)
    {
        var output = new WasmByteWriter();
        Encode(output, instructions);
        return output.ToArray();
    }

    public static void Encode(WasmByteWriter output, WasmInstruction instruction)
    {
        var metadata = instruction.Metadata;
        if (instruction.HasSymbol)
            throw new InvalidOperationException($"{metadata.Mnemonic} still refers to the symbol {instruction.Symbol}, which only a linker resolves");
        if (metadata.Prefix == 0)
            output.Byte((byte)metadata.Code);
        else
        {
            output.Byte(metadata.Prefix);
            output.U32(metadata.Code);
        }

        switch (metadata.Format)
        {
            case WasmInstructionFormat.None:
                break;
            case WasmInstructionFormat.BlockType:
                WriteBlockType(output, instruction.BlockType);
                break;
            case WasmInstructionFormat.TryTable:
                WriteBlockType(output, instruction.BlockType);
                output.U32((uint)instruction.Catches.Length);
                foreach (var handler in instruction.Catches)
                {
                    if (handler.TagSymbol is not null)
                        throw new InvalidOperationException($"try_table still refers to the tag {handler.TagSymbol}, which only a linker resolves");
                    output.Byte((byte)handler.Kind);
                    if (handler.HasTag)
                        output.U32(handler.Tag);
                    output.U32(handler.Label);
                }
                break;
            case WasmInstructionFormat.Label:
            case WasmInstructionFormat.Function:
            case WasmInstructionFormat.Type:
            case WasmInstructionFormat.Local:
            case WasmInstructionFormat.Global:
            case WasmInstructionFormat.Table:
            case WasmInstructionFormat.Memory:
            case WasmInstructionFormat.Tag:
            case WasmInstructionFormat.Data:
            case WasmInstructionFormat.Element:
                output.U32(instruction.Index);
                break;
            case WasmInstructionFormat.LabelTable:
                output.U32((uint)instruction.Labels.Length);
                foreach (var label in instruction.Labels)
                    output.U32(label);
                output.U32(instruction.Index);
                break;
            case WasmInstructionFormat.CallIndirect:
                if (instruction.Index == uint.MaxValue)
                    throw new InvalidOperationException($"{metadata.Mnemonic} names its signature, which the module has to intern first");
                output.U32(instruction.Index);
                output.U32(instruction.Index2);
                break;
            case WasmInstructionFormat.TypeField:
            case WasmInstructionFormat.TypeCount:
            case WasmInstructionFormat.TypeData:
            case WasmInstructionFormat.TypeElement:
            case WasmInstructionFormat.TypeType:
            case WasmInstructionFormat.MemoryInit:
            case WasmInstructionFormat.MemoryCopy:
            case WasmInstructionFormat.TableInit:
            case WasmInstructionFormat.TableCopy:
                output.U32(instruction.Index);
                output.U32(instruction.Index2);
                break;
            case WasmInstructionFormat.MemoryArgument:
                WriteMemoryArgument(output, instruction.MemoryArgument);
                break;
            case WasmInstructionFormat.MemoryLane:
                WriteMemoryArgument(output, instruction.MemoryArgument);
                output.Byte((byte)instruction.Index2);
                break;
            case WasmInstructionFormat.I32:
                output.S64(instruction.I32);
                break;
            case WasmInstructionFormat.I64:
                output.S64(instruction.I64);
                break;
            case WasmInstructionFormat.F32:
            {
                Span<byte> bytes = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)instruction.Value);
                output.Bytes(bytes);
                break;
            }
            case WasmInstructionFormat.F64:
            {
                Span<byte> bytes = stackalloc byte[8];
                BinaryPrimitives.WriteUInt64LittleEndian(bytes, instruction.Value);
                output.Bytes(bytes);
                break;
            }
            case WasmInstructionFormat.HeapType:
            case WasmInstructionFormat.ReferenceType:
                WasmTypeEncoding.WriteHeapType(output, instruction.Type.HeapType);
                break;
            case WasmInstructionFormat.BranchCast:
                output.Byte((byte)((instruction.Type.Nullable ? 1 : 0) | (instruction.Type2.Nullable ? 2 : 0)));
                output.U32(instruction.Index);
                WasmTypeEncoding.WriteHeapType(output, instruction.Type.HeapType);
                WasmTypeEncoding.WriteHeapType(output, instruction.Type2.HeapType);
                break;
            case WasmInstructionFormat.SelectTypes:
                WasmTypeEncoding.WriteValueTypes(output, instruction.Types);
                break;
            case WasmInstructionFormat.V128:
            case WasmInstructionFormat.Shuffle:
                if (instruction.Bytes.Length != 16)
                    throw new InvalidOperationException($"{metadata.Mnemonic} carries sixteen bytes");
                output.Bytes(instruction.Bytes.AsSpan());
                break;
            case WasmInstructionFormat.Lane:
                output.Byte((byte)instruction.Index2);
                break;
            default:
                throw new InvalidOperationException($"Cannot encode {metadata.Mnemonic}");
        }
    }

    private static void WriteBlockType(WasmByteWriter output, WasmBlockType blockType)
    {
        switch (blockType.Kind)
        {
            case WasmBlockTypeKind.Empty:
                output.Byte(0x40);
                break;
            case WasmBlockTypeKind.Value:
                WasmTypeEncoding.WriteValueType(output, blockType.ValueType);
                break;
            default:
                if (blockType.NeedsTypeIndex)
                    throw new InvalidOperationException("A block names its signature, which the module has to intern first");
                output.S64(blockType.TypeIndex);
                break;
        }
    }

    private static void WriteMemoryArgument(WasmByteWriter output, WasmMemoryArgument argument)
    {
        if (argument.Memory != 0)
        {
            output.U32(argument.Alignment | 0x40);
            output.U32(argument.Memory);
        }
        else
            output.U32(argument.Alignment);
        output.U64(argument.Offset);
    }
}

internal static class WasmCodeDecoder
{
    public static ImmutableArray<WasmInstruction> DecodeExpression(ref WasmByteReader input)
    {
        var instructions = ImmutableArray.CreateBuilder<WasmInstruction>();
        int depth = 0;
        for (;;)
        {
            var instruction = Decode(ref input);
            switch (instruction.Kind)
            {
                case WasmInstrKind.Block:
                case WasmInstrKind.Loop:
                case WasmInstrKind.If:
                case WasmInstrKind.TryTable:
                    depth++;
                    break;
                case WasmInstrKind.End:
                    if (depth == 0)
                        return instructions.ToImmutable();
                    depth--;
                    break;
            }
            instructions.Add(instruction);
        }
    }

    public static WasmInstruction Decode(ref WasmByteReader input)
    {
        int start = input.Offset;
        byte first = input.Byte();
        WasmInstrKind kind = first is 0xFB or 0xFC or 0xFD
            ? (WasmInstrKind)(first << 16 | (int)Math.Min(input.U32(), 0xFFFFu))
            : (WasmInstrKind)first;
        if (!WasmInstructionTable.TryGet(kind, out var metadata))
            throw new WasmBinaryException(first is 0xFB or 0xFC or 0xFD ? $"Unknown instruction 0x{first:X2} 0x{(int)kind & 0xFFFF:X}" : $"Unknown instruction 0x{first:X2}", start);

        switch (metadata.Format)
        {
            case WasmInstructionFormat.None:
                return WasmInstruction.Simple(kind);
            case WasmInstructionFormat.BlockType:
                return WasmInstruction.Structured(kind, ReadBlockType(ref input));
            case WasmInstructionFormat.TryTable:
            {
                var blockType = ReadBlockType(ref input);
                uint count = input.Count();
                var catches = ImmutableArray.CreateBuilder<WasmCatch>((int)count);
                for (uint i = 0; i < count; i++)
                {
                    byte handler = input.Byte();
                    if (handler > 3)
                        throw input.Error($"Unknown catch clause {handler}");
                    var catchKind = (WasmCatchKind)handler;
                    uint tag = catchKind is WasmCatchKind.Catch or WasmCatchKind.CatchRef ? input.U32() : 0;
                    catches.Add(new WasmCatch(catchKind, tag, input.U32()));
                }
                return WasmInstruction.Create(kind, blockType: blockType, catches: catches.MoveToImmutable());
            }
            case WasmInstructionFormat.Label:
            case WasmInstructionFormat.Function:
            case WasmInstructionFormat.Type:
            case WasmInstructionFormat.Local:
            case WasmInstructionFormat.Global:
            case WasmInstructionFormat.Table:
            case WasmInstructionFormat.Memory:
            case WasmInstructionFormat.Tag:
            case WasmInstructionFormat.Data:
            case WasmInstructionFormat.Element:
                return WasmInstruction.WithIndex(kind, input.U32());
            case WasmInstructionFormat.LabelTable:
            {
                uint count = input.Count();
                var labels = ImmutableArray.CreateBuilder<uint>((int)count);
                for (uint i = 0; i < count; i++)
                    labels.Add(input.U32());
                return WasmInstruction.Create(kind, input.U32(), labels: labels.MoveToImmutable());
            }
            case WasmInstructionFormat.CallIndirect:
            case WasmInstructionFormat.TypeField:
            case WasmInstructionFormat.TypeCount:
            case WasmInstructionFormat.TypeData:
            case WasmInstructionFormat.TypeElement:
            case WasmInstructionFormat.TypeType:
            case WasmInstructionFormat.MemoryInit:
            case WasmInstructionFormat.MemoryCopy:
            case WasmInstructionFormat.TableInit:
            case WasmInstructionFormat.TableCopy:
            {
                uint index = input.U32();
                return WasmInstruction.WithIndices(kind, index, input.U32());
            }
            case WasmInstructionFormat.MemoryArgument:
                return WasmInstruction.Create(kind, memoryArgument: ReadMemoryArgument(ref input));
            case WasmInstructionFormat.MemoryLane:
            {
                var argument = ReadMemoryArgument(ref input);
                return WasmInstruction.Create(kind, index2: input.Byte(), memoryArgument: argument);
            }
            case WasmInstructionFormat.I32:
                return WasmInstruction.I32Const(input.S32());
            case WasmInstructionFormat.I64:
                return WasmInstruction.I64Const(input.S64());
            case WasmInstructionFormat.F32:
                return WasmInstruction.F32ConstBits(BinaryPrimitives.ReadUInt32LittleEndian(input.Bytes(4)));
            case WasmInstructionFormat.F64:
                return WasmInstruction.F64ConstBits(BinaryPrimitives.ReadUInt64LittleEndian(input.Bytes(8)));
            case WasmInstructionFormat.HeapType:
                return WasmInstruction.RefNull(WasmTypeEncoding.ReadHeapType(ref input));
            case WasmInstructionFormat.ReferenceType:
            {
                bool nullable = kind is WasmInstrKind.RefTestNull or WasmInstrKind.RefCastNull;
                return WasmInstruction.Create(kind, type: WasmValueType.Reference(WasmTypeEncoding.ReadHeapType(ref input), nullable));
            }
            case WasmInstructionFormat.BranchCast:
            {
                byte flags = input.Byte();
                if (flags > 3)
                    throw input.Error($"Unknown cast flags 0x{flags:X2}");
                uint label = input.U32();
                var from = WasmValueType.Reference(WasmTypeEncoding.ReadHeapType(ref input), (flags & 1) != 0);
                var to = WasmValueType.Reference(WasmTypeEncoding.ReadHeapType(ref input), (flags & 2) != 0);
                return WasmInstruction.Create(kind, label, type: from, type2: to);
            }
            case WasmInstructionFormat.SelectTypes:
                return WasmInstruction.Create(kind, types: WasmTypeEncoding.ReadValueTypes(ref input));
            case WasmInstructionFormat.V128:
            case WasmInstructionFormat.Shuffle:
                return WasmInstruction.Create(kind, bytes: input.Bytes(16).ToArray().ToImmutableArray());
            case WasmInstructionFormat.Lane:
                return WasmInstruction.LaneOp(kind, input.Byte());
            default:
                throw new WasmBinaryException($"Cannot decode {metadata.Mnemonic}", start);
        }
    }

    private static WasmBlockType ReadBlockType(ref WasmByteReader input)
    {
        byte first = input.Peek();
        if (first == 0x40)
        {
            input.Byte();
            return WasmBlockType.Empty;
        }
        if (first is 0x7F or 0x7E or 0x7D or 0x7C or 0x7B or 0x63 or 0x64 || WasmHeapType.TryFromCode(first, out _))
            return WasmBlockType.Of(WasmTypeEncoding.ReadValueType(ref input));
        long index = input.Signed(33);
        if (index < 0)
            throw input.Error("A block type index is not negative");
        return WasmBlockType.OfType(checked((uint)index));
    }

    private static WasmMemoryArgument ReadMemoryArgument(ref WasmByteReader input)
    {
        uint flags = input.U32();
        if (flags >= 128)
            throw input.Error("A memory argument's alignment is out of range");
        uint memory = (flags & 0x40) != 0 ? input.U32() : 0;
        return new WasmMemoryArgument(flags & ~0x40u, input.U64(), memory);
    }
}
