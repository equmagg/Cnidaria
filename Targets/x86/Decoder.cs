using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.X86;

internal static class X86CodeDecoder
{
    public static X86Program Decode(byte[] bytes, X86Target target, ulong imageBase = 0)
    {
        var instructions = DecodeInstructions(bytes, target, imageBase);
        return new X86Program(target, instructions, null);
    }

    public static ImmutableArray<X86Instruction> DecodeInstructions(byte[] bytes, X86Target target, ulong imageBase = 0)
    {
        if (bytes is null)
            throw new ArgumentNullException(nameof(bytes));
        if (target is null)
            throw new ArgumentNullException(nameof(target));

        var reader = new X86InstructionReader(bytes);
        var builder = ImmutableArray.CreateBuilder<X86Instruction>();
        while (!reader.End)
            builder.Add(DecodeOne(reader, target));
        return builder.ToImmutable();
    }

    private struct DecodeState
    {
        public byte Prefixes;
        public bool W;
        public bool R;
        public bool X;
        public bool B;
        public bool RHigh;
        public bool VHigh;
        public int Vvvv;
        public int Length;
        public int OpMask;
        public bool Zeroing;
        public bool Broadcast;
        public X86EncodingSpace Space;
    }

    private static X86Instruction DecodeOne(X86InstructionReader reader, X86Target target)
    {
        int start = reader.Position;
        try
        {
            var state = new DecodeState();
            while (!reader.End && reader.PeekByte() is 0x66 or 0xF2 or 0xF3)
            {
                state.Prefixes |= reader.ReadByte() switch { 0x66 => X86Forms.Prefix66, 0xF3 => X86Forms.PrefixF3, _ => X86Forms.PrefixF2 };
            }
            if (target.Is64Bit && !reader.End && reader.PeekByte() is >= 0x40 and <= 0x4F)
            {
                byte rex = reader.ReadByte();
                state.W = (rex & 8) != 0;
                state.R = (rex & 4) != 0;
                state.X = (rex & 2) != 0;
                state.B = (rex & 1) != 0;
            }

            byte map;
            byte opcode = reader.ReadByte();
            if (opcode is 0xC4 or 0xC5 && (target.Is64Bit || (reader.PeekByte() & 0xC0) == 0xC0))
            {
                state.Space = X86EncodingSpace.Vex;
                map = ReadVex(reader, opcode, ref state);
                opcode = reader.ReadByte();
            }
            else if (opcode == 0x62 && target.Is64Bit)
            {
                state.Space = X86EncodingSpace.Evex;
                map = ReadEvex(reader, ref state);
                opcode = reader.ReadByte();
            }
            else
            {
                map = 0;
                if (opcode == 0x0F)
                {
                    map = 1;
                    opcode = reader.ReadByte();
                    if (opcode is 0x38 or 0x3A)
                    {
                        map = opcode == 0x38 ? (byte)2 : (byte)3;
                        opcode = reader.ReadByte();
                    }
                }
            }

            var candidates = X86Forms.ForOpcode(state.Space, map, opcode);
            int modRm = reader.End ? -1 : reader.PeekByte();
            X86Form? best = null;
            int bestScore = -1;
            foreach (var form in candidates)
            {
                int score = Accepts(form, state, modRm, target, exactPrefixes: true) ? 2
                    : Accepts(form, state, modRm, target, exactPrefixes: false) ? 0 : -1;
                if (score < 0)
                    continue;
                if (state.W && form.W == X86WMode.One)
                    score++;
                if (score > bestScore)
                {
                    best = form;
                    bestScore = score;
                }
            }
            if (best is not null)
                return Build(best, state, opcode, reader, target);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or InvalidOperationException or NotSupportedException or OverflowException or ArgumentOutOfRangeException)
        {
        }

        reader.Position = start + 1;
        return X86Instruction.Raw(new[] { reader.Bytes[start] });
    }

    private static byte ReadVex(X86InstructionReader reader, byte prefix, ref DecodeState state)
    {
        byte map;
        int pp;
        if (prefix == 0xC5)
        {
            byte b = reader.ReadByte();
            state.R = (b & 0x80) == 0;
            state.Vvvv = (~(b >> 3)) & 0xF;
            state.Length = (b >> 2) & 1;
            pp = b & 3;
            map = 1;
        }
        else
        {
            byte b1 = reader.ReadByte();
            byte b2 = reader.ReadByte();
            state.R = (b1 & 0x80) == 0;
            state.X = (b1 & 0x40) == 0;
            state.B = (b1 & 0x20) == 0;
            map = (byte)(b1 & 0x1F);
            state.W = (b2 & 0x80) != 0;
            state.Vvvv = (~(b2 >> 3)) & 0xF;
            state.Length = (b2 >> 2) & 1;
            pp = b2 & 3;
        }
        state.Prefixes = PrefixFromCode(pp);
        return map;
    }

    private static byte ReadEvex(X86InstructionReader reader, ref DecodeState state)
    {
        byte p0 = reader.ReadByte();
        byte p1 = reader.ReadByte();
        byte p2 = reader.ReadByte();
        if ((p1 & 0x04) == 0)
            throw new InvalidOperationException("EVEX P1 bit 2 must be set");
        state.R = (p0 & 0x80) == 0;
        state.X = (p0 & 0x40) == 0;
        state.B = (p0 & 0x20) == 0;
        state.RHigh = (p0 & 0x10) == 0;
        state.W = (p1 & 0x80) != 0;
        state.Vvvv = (~(p1 >> 3)) & 0xF;
        state.Prefixes = PrefixFromCode(p1 & 3);
        state.Zeroing = (p2 & 0x80) != 0;
        state.Length = (p2 >> 5) & 3;
        state.Broadcast = (p2 & 0x10) != 0;
        state.VHigh = (p2 & 0x08) == 0;
        state.OpMask = p2 & 7;
        return (byte)(p0 & 7);
    }

    private static byte PrefixFromCode(int pp)
        => pp switch { 1 => X86Forms.Prefix66, 2 => X86Forms.PrefixF3, 3 => X86Forms.PrefixF2, _ => 0 };

    private static bool Accepts(X86Form form, DecodeState state, int modRm, X86Target target, bool exactPrefixes)
    {
        if (form.Has(X86FormFlags.Mode64) && !target.Is64Bit)
            return false;
        if (form.Space == X86EncodingSpace.Legacy)
        {
            byte required = form.Prefixes;
            byte present = state.Prefixes;
            if ((required & ~present) != 0)
                return false;
            byte extra = (byte)(present & ~required);
            if (extra != 0 && (exactPrefixes || extra != X86Forms.Prefix66 || form.W != X86WMode.OperandSize))
                return false;
        }
        else
        {
            if (form.Prefixes != state.Prefixes)
                return false;
            bool rounding = form.Space == X86EncodingSpace.Evex && state.Broadcast && modRm >= 0 && (modRm >> 6) == 3;
            if (rounding && !form.Has(X86FormFlags.Rounding))
                return false;
            if (!rounding && !form.Has(X86FormFlags.VectorLengthIgnored) && form.VectorLength != state.Length)
                return false;
            if ((state.OpMask != 0 || state.Zeroing) && !form.Has(X86FormFlags.Masking))
                return false;
        }

        if (form.W == X86WMode.One && !state.W || form.W == X86WMode.Zero && state.W)
            return false;
        if (!form.HasModRm)
            return true;
        if (modRm < 0)
            return false;
        if (form.FixedModRm >= 0)
            return modRm == form.FixedModRm;
        if (form.Extension >= 0 && ((modRm >> 3) & 7) != form.Extension)
            return false;
        bool register = (modRm >> 6) == 3;
        foreach (var spec in form.Operands)
        {
            if (spec.Role != X86OperandRole.Rm)
                continue;
            return register ? spec.AllowsRegister : spec.AllowsMemory;
        }
        return true;
    }

    private static X86Instruction Build(X86Form form, DecodeState state, byte opcode, X86InstructionReader reader, X86Target target)
    {
        int modRm = form.HasModRm ? reader.ReadByte() : 0;
        if (form.FixedModRm >= 0)
            modRm = form.FixedModRm;
        bool hasRm = Array.Exists(form.Operands, o => o.Role == X86OperandRole.Rm);

        int variable;
        if ((state.Prefixes & ~form.Prefixes & X86Forms.Prefix66) != 0)
            variable = 2;
        else if (state.W && form.W != X86WMode.One || form.W == X86WMode.One && form.UsesOperandSize)
            variable = 8;
        else if (form.Has(X86FormFlags.Default64) && target.Is64Bit)
            variable = 8;
        else
            variable = 4;
        int y = state.W ? 8 : 4;

        X86Operand rm = X86Operand.None;
        if (hasRm && (modRm >> 6) != 3)
        {
            int scale = form.Space == X86EncodingSpace.Evex ? X86CodeEncoder.Disp8Scale(form, state.Broadcast) : 1;
            var index = Array.Find(form.Operands, o => o.Role == X86OperandRole.Rm).IndexClass;
            rm = DecodeMemory(reader, target, modRm, state, scale, index);
        }

        var operands = new List<X86Operand>(4);
        foreach (var spec in form.Operands)
        {
            switch (spec.Role)
            {
                case X86OperandRole.Implicit:
                    if (spec.One)
                        operands.Add(X86Operand.ImmediateOperand(1, 1));
                    else if (spec.Fixed == X86Register.Rcx)
                        operands.Add(X86Operand.RegisterOperand(X86Register.Rcx, 1));
                    break;
                case X86OperandRole.Reg:
                    {
                        int index = ((modRm >> 3) & 7) | (state.R ? 8 : 0) | (state.RHigh ? 16 : 0);
                        operands.Add(Register(spec, index, variable, y));
                        break;
                    }
                case X86OperandRole.Rm:
                    if ((modRm >> 6) == 3)
                    {
                        int index = (modRm & 7) | (state.B ? 8 : 0) | (form.Space == X86EncodingSpace.Evex && state.X ? 16 : 0);
                        operands.Add(Register(spec, index, variable, y));
                    }
                    else
                    {
                        bool broadcast = form.Space == X86EncodingSpace.Evex && state.Broadcast;
                        int size = spec.Class == X86OperandClass.Gpr
                            ? GprSize(spec.MemorySize == spec.Size ? spec.Size : spec.MemorySize, variable, y)
                            : spec.MemorySize;
                        var memory = rm.WithSize(size);
                        operands.Add(broadcast ? memory.WithBroadcast(true) : memory);
                    }
                    break;
                case X86OperandRole.Vvvv:
                    operands.Add(Register(spec, state.Vvvv | (state.VHigh ? 16 : 0), variable, y));
                    break;
                case X86OperandRole.OpcodeRegister:
                    operands.Add(Register(spec, (opcode - form.Opcode) | (state.B ? 8 : 0), variable, y));
                    break;
                case X86OperandRole.Immediate:
                    {
                        int width = spec.Size switch
                        {
                            X86OperandSpec.SizeZ => variable == 2 ? 2 : 4,
                            X86OperandSpec.SizeV => variable,
                            _ => spec.Size,
                        };
                        long value = width switch { 1 => reader.ReadInt8(), 2 => reader.ReadInt16(), 4 => reader.ReadInt32(), _ => reader.ReadInt64() };
                        operands.Add(X86Operand.ImmediateOperand(value, width));
                        break;
                    }
                case X86OperandRole.Relative:
                    operands.Add(X86Operand.ImmediateOperand(spec.Size == 1 ? reader.ReadInt8() : reader.ReadInt32(), spec.Size));
                    break;
                case X86OperandRole.Is4:
                    operands.Add(X86Operand.RegisterOperand(X86Registers.Vector((reader.ReadByte() >> 4) & 15, spec.Size), spec.Size));
                    break;
            }
        }

        var condition = form.Has(X86FormFlags.OpcodeCondition) ? (X86Condition)((opcode - form.Opcode) & 0xF) : X86Condition.E;
        X86Operand Operand(int i) => i < operands.Count ? operands[i] : X86Operand.None;
        var rounding = form.Space == X86EncodingSpace.Evex && state.Broadcast && hasRm && (modRm >> 6) == 3
            ? (X86RoundingMode)(state.Length + 1)
            : X86RoundingMode.None;
        return new X86Instruction(form.Kind, Operand(0), Operand(1), Operand(2), condition, default, Operand(3), state.OpMask, state.Zeroing, rounding);
    }

    private static int GprSize(byte size, int variable, int y)
        => size switch
        {
            X86OperandSpec.SizeV => variable,
            X86OperandSpec.SizeY => y,
            _ => size,
        };

    private static X86Operand Register(X86OperandSpec spec, int index, int variable, int y)
    {
        switch (spec.Class)
        {
            case X86OperandClass.Gpr:
                {
                    int size = GprSize(spec.Size, variable, y);
                    return X86Operand.RegisterOperand((X86Register)(index & 15), size);
                }
            case X86OperandClass.Xmm:
                return X86Operand.RegisterOperand(X86Registers.Vector(index & 31, 16), 16);
            case X86OperandClass.Ymm:
                return X86Operand.RegisterOperand(X86Registers.Vector(index & 31, 32), 32);
            case X86OperandClass.Zmm:
                return X86Operand.RegisterOperand(X86Registers.Vector(index & 31, 64), 64);
            case X86OperandClass.Mask:
                return X86Operand.RegisterOperand((X86Register)((int)X86Register.K0 + (index & 7)), 8);
            default:
                throw new InvalidOperationException("Operand class has no register form");
        }
    }

    private static X86Operand DecodeMemory(X86InstructionReader reader, X86Target target, int modRm, DecodeState state, int disp8Scale, X86OperandClass indexClass)
    {
        int mod = (modRm >> 6) & 3;
        X86Register baseRegister = X86Register.Invalid;
        X86Register indexRegister = X86Register.Invalid;
        int scale = 1;
        long displacement = 0;
        bool ripRelative = false;

        if ((modRm & 7) == 4)
        {
            byte sib = reader.ReadByte();
            scale = 1 << ((sib >> 6) & 3);
            int index = ((sib >> 3) & 7) | (state.X ? 8 : 0);
            int b = (sib & 7) | (state.B ? 8 : 0);
            if (indexClass != X86OperandClass.Gpr)
                indexRegister = X86Registers.Vector(index | (state.VHigh ? 16 : 0), indexClass switch { X86OperandClass.Xmm => 16, X86OperandClass.Ymm => 32, _ => 64 });
            else if (index != 4)
                indexRegister = (X86Register)index;
            if (mod == 0 && (b & 7) == 5)
                displacement = reader.ReadInt32();
            else
                baseRegister = (X86Register)b;
        }
        else if (mod == 0 && (modRm & 7) == 5)
        {
            displacement = reader.ReadInt32();
            if (target.Is64Bit)
            {
                baseRegister = X86Register.Rip;
                ripRelative = true;
            }
        }
        else
        {
            baseRegister = (X86Register)((modRm & 7) | (state.B ? 8 : 0));
        }

        if (mod == 1)
            displacement = (long)reader.ReadInt8() * disp8Scale;
        else if (mod == 2)
            displacement = reader.ReadInt32();

        return X86Operand.Memory(baseRegister, displacement, 0, indexRegister, scale, null, X86ObjectRelocationKind.None, 0, ripRelative);
    }

    private sealed class X86InstructionReader
    {
        public byte[] Bytes { get; }
        public int Position { get; set; }
        public bool End => Position >= Bytes.Length;

        public X86InstructionReader(byte[] bytes)
        {
            Bytes = bytes;
        }

        public byte PeekByte()
            => Bytes[Position];

        public byte ReadByte()
            => Bytes[Position++];

        public sbyte ReadInt8()
            => unchecked((sbyte)ReadByte());

        public short ReadInt16()
        {
            var value = (ushort)(ReadByte() | (ReadByte() << 8));
            return unchecked((short)value);
        }

        public int ReadInt32()
        {
            var value = (uint)(ReadByte() | (ReadByte() << 8) | (ReadByte() << 16) | (ReadByte() << 24));
            return unchecked((int)value);
        }

        public long ReadInt64()
        {
            ulong value = 0;
            for (var i = 0; i < 8; i++)
                value |= (ulong)ReadByte() << (i * 8);
            return unchecked((long)value);
        }
    }
}
