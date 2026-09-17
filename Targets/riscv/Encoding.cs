using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;

namespace Cnidaria.RiscV;

internal static class RiscVCodeEncoder
{
    public static byte[] Encode(RiscVProgram obj, ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
    {
        if (obj is null)
            throw new ArgumentNullException(nameof(obj));
        return RVObjectLinker.LinkFlat(obj, imageBase, externalSymbols).ToArray();
    }

    public static byte[] Encode(IEnumerable<RVInstruction> instructions, RVTarget target, IReadOnlyDictionary<string, int>? labels = null)
    {
        if (instructions is null)
            throw new ArgumentNullException(nameof(instructions));
        if (target is null)
            throw new ArgumentNullException(nameof(target));

        var list = instructions.ToImmutableArray();
        var bytes = new byte[RVInstructionTable.GetEncodedSize(list)];
        var offset = 0;
        for (int i = 0; i < list.Length; i++)
        {
            var instruction = ResolveInstruction(list[i], offset, labels);
            var encoded = Encode(instruction, target);
            var size = RVInstructionTable.GetEncodedSize(instruction.Opcode);
            WriteInstruction(bytes, offset, encoded, size, target.Endianness);
            offset = checked(offset + size);
        }
        return bytes;
    }

    public static uint Encode(RVInstruction instruction, RVTarget target)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        if (instruction.HasSymbol)
            throw new InvalidOperationException("Symbolic RISC-V instruction must be resolved before binary encoding");

        var metadata = RVInstructionTable.Get(instruction.Opcode);
        if (metadata.Format == RVInstructionFormat.Raw || metadata.Format == RVInstructionFormat.Raw16)
            return unchecked((uint)instruction.Immediate);

        ValidateTarget(instruction.Opcode, metadata, target);
        return RVEncodedCodec.Encode(instruction, metadata.Form
            ?? throw new NotSupportedException("RISC-V instruction has no encoding: " + instruction.Opcode));
    }

    private static RVInstruction ResolveInstruction(RVInstruction instruction, int pc, IReadOnlyDictionary<string, int>? labels)
    {
        if (!instruction.HasSymbol)
            return instruction;
        if (labels is null || !labels.TryGetValue(instruction.Symbol!, out int targetPc))
            throw new InvalidOperationException($"Unresolved symbol: {instruction.Symbol}");

        int relative = checked(targetPc - pc);
        switch (instruction.RelocationKind)
        {
            case RVRelocationKind.RelativeBranch:
            case RVRelocationKind.RelativeJal:
                return instruction.WithImmediate(relative);
            default:
                throw new NotSupportedException($"Unsupported symbolic relocation: {instruction.RelocationKind}");
        }
    }


    private static void ValidateTarget(RVInstrKind opcode, RVInstructionMetadata metadata, RVTarget target)
    {
        if (metadata.Requires64Bit && !target.Is64Bit)
            throw new InvalidOperationException(opcode + " requires RV64 target");
        if (metadata.Requires32Bit && target.Is64Bit)
            throw new InvalidOperationException(opcode + " requires RV32 target");
        if (!target.Has(metadata.RequiredIsa))
            throw new InvalidOperationException(opcode + " requires RISC-V extension " + metadata.RequiredIsa);
    }

    internal static bool IsUnsignedVectorImmediate(RVInstrKind opcode)
        => opcode is RVInstrKind.VsllVi or RVInstrKind.VsrlVi or RVInstrKind.VsraVi
            or RVInstrKind.VnsrlWi or RVInstrKind.VnsraWi or RVInstrKind.VrgatherVi;

    internal static void WriteInstruction(byte[] bytes, int offset, uint value, int size, TargetEndianness endianness)
    {
        if (size == 2)
        {
            WriteUInt16(bytes, offset, (ushort)value, endianness);
            return;
        }
        WriteUInt32(bytes, offset, value, endianness);
    }

    private static void WriteUInt16(byte[] bytes, int offset, ushort value, TargetEndianness endianness)
    {
        if (endianness == TargetEndianness.Little)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            return;
        }
        bytes[offset] = (byte)(value >> 8);
        bytes[offset + 1] = (byte)value;
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value, TargetEndianness endianness)
    {
        if (endianness == TargetEndianness.Little)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
            return;
        }
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }
}

internal static class RiscVCodeDecoder
{
    public static RVInstruction Decode(ushort halfword, RVTarget target)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        if (!target.HasC)
            return RVInstruction.Raw16(halfword);
        return RVEncodedCodec.TryDecode(halfword, compressed: true, target, out var instruction)
            ? instruction
            : RVInstruction.Raw16(halfword);
    }

    public static RVInstruction Decode(uint word, RVTarget target)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        if ((word & 3U) != 3U)
            return Decode((ushort)word, target);
        return RVEncodedCodec.TryDecode(word, compressed: false, target, out var instruction)
            ? instruction
            : RVInstruction.Raw(word);
    }
}
