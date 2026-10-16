using System;
using System.Collections.Generic;
using System.Linq;

namespace Cnidaria.X86;

internal static class X86CodeEncoder
{
    public static byte[] Encode(X86Program obj, ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
    {
        if (obj is null)
            throw new ArgumentNullException(nameof(obj));
        return X86ObjectLinker.LinkFlat(obj, imageBase, externalSymbols).ToArray();
    }

    public static byte[] Encode(IEnumerable<X86Instruction> instructions, X86Target target, IReadOnlyDictionary<string, int>? labels = null)
    {
        if (instructions is null)
            throw new ArgumentNullException(nameof(instructions));
        if (target is null)
            throw new ArgumentNullException(nameof(target));

        var symbols = labels?.ToDictionary(kv => kv.Key, kv => (ulong)Math.Max(0, kv.Value), StringComparer.Ordinal);
        var output = new List<byte>();
        foreach (var instruction in instructions)
        {
            var pc = (ulong)output.Count;
            output.AddRange(Encode(instruction, target, pc, symbols));
        }
        return output.ToArray();
    }

    public static byte[] Encode(X86Instruction instruction, X86Target target)
        => Encode(instruction, target, 0, null);

    public static byte[] Encode(X86Instruction instruction, X86Target target, ulong pc, IReadOnlyDictionary<string, ulong>? symbols)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));

        var firstPass = EncodeCore(instruction, target, pc, symbols);
        return EncodeCore(instruction, target, checked(pc + (ulong)firstPass.Length), symbols);
    }

    public static int GetEncodedLength(X86Instruction instruction, X86Target target)
        => Encode(instruction, target).Length;

    private static byte[] EncodeCore(X86Instruction instruction, X86Target target, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
    {
        if (instruction.Opcode == X86InstrKind.Raw)
            return instruction.RawBytes.ToArray();

        var selection = Select(instruction, target);
        var writer = new X86InstructionWriter();
        switch (selection.Form.Space)
        {
            case X86EncodingSpace.Legacy:
                EmitLegacy(writer, selection, instruction, target, nextIp, symbols);
                break;
            case X86EncodingSpace.Vex:
                EmitVex(writer, selection, instruction, target, nextIp, symbols);
                break;
            default:
                EmitEvex(writer, selection, instruction, target, nextIp, symbols);
                break;
        }
        return writer.ToArray();
    }

    internal readonly struct X86Selection
    {
        public X86Selection(X86Form form, X86Operand[] operands, int operandSize)
        {
            Form = form;
            Operands = operands;
            OperandSize = operandSize;
        }

        public X86Form Form { get; }
        public X86Operand[] Operands { get; }
        public int OperandSize { get; }
    }

    // With AVX present every SSE instruction takes its VEX form; a legacy two-operand shape gains its destination as vvvv
    internal static byte[] EncodeWithForm(X86Form form, X86Instruction instruction, X86Target target)
    {
        var operands = Canonicalize(instruction, target);
        if (!Match(form, operands, instruction, target, out int size))
            throw new NotSupportedException($"{form} does not accept the operands");
        var writer = new X86InstructionWriter();
        var selection = new X86Selection(form, operands, size);
        if (form.Space == X86EncodingSpace.Legacy)
            EmitLegacy(writer, selection, instruction, target, 0, null);
        else if (form.Space == X86EncodingSpace.Vex)
            EmitVex(writer, selection, instruction, target, 0, null);
        else
            EmitEvex(writer, selection, instruction, target, 0, null);
        return writer.ToArray();
    }

    internal static X86Selection Select(X86Instruction instruction, X86Target target)
    {
        var operands = Canonicalize(instruction, target);
        var forms = X86Forms.ForKind(instruction.Opcode);
        bool preferVex = target.Has(X86IsaFlags.Avx) && HasVexForm(forms);
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var form in forms)
            {
                if (preferVex && form.Space == X86EncodingSpace.Legacy)
                    continue;
                X86Operand[]? candidate = pass == 0 ? operands : Expand(form, operands);
                if (candidate is not null && Match(form, candidate, instruction, target, out int size))
                    return new X86Selection(form, candidate, size);
            }
        }
        throw new NotSupportedException($"No encoding of {instruction.Opcode} for operands ({string.Join(", ", operands.Select(Describe))}) on this target");
    }

    private static bool HasVexForm(IReadOnlyList<X86Form> forms)
    {
        foreach (var form in forms)
        {
            if (form.Space == X86EncodingSpace.Vex)
                return true;
        }
        return false;
    }

    private static X86Operand[] Canonicalize(X86Instruction instruction, X86Target target)
    {
        var operands = new X86Operand[instruction.OperandCount];
        for (int i = 0; i < operands.Length; i++)
            operands[i] = instruction.GetOperand(i);

        // A 64-bit immediate that fits 32 bits unsigned is loaded through the zero-extending 32-bit mov
        if (instruction.Opcode == X86InstrKind.Mov && operands.Length == 2 &&
            operands[0].Kind == X86OperandKind.Register && X86Registers.IsGeneral(operands[0].Register) && OperandSize(operands[0], target) == 8 &&
            operands[1].Kind == X86OperandKind.Immediate && operands[1].Immediate >= 0 && operands[1].Immediate <= uint.MaxValue)
        {
            operands[0] = operands[0].WithSize(4);
        }
        if (instruction.Opcode == X86InstrKind.Setcc && operands.Length == 1)
            operands[0] = operands[0].WithSize(1);
        return operands;
    }

    private static X86Operand[]? Expand(X86Form form, X86Operand[] operands)
    {
        if (form.Space == X86EncodingSpace.Legacy || operands.Length == 0)
            return null;
        int expected = form.ExplicitOperandCount;
        bool vvvvFirst = form.Operands.Length > 1 && (form.Operands[0].Role == X86OperandRole.Vvvv || form.Operands[1].Role == X86OperandRole.Vvvv);
        if (!vvvvFirst)
            return null;
        int extra = form.Has(X86FormFlags.LegacyXmm0) ? 2 : 1;
        if (operands.Length + extra != expected)
            return null;
        var expanded = new List<X86Operand>(expected) { operands[0], operands[0] };
        expanded.AddRange(operands.Skip(1));
        if (form.Has(X86FormFlags.LegacyXmm0))
            expanded.Add(X86Operand.RegisterOperand(X86Register.Xmm0, 16));
        return expanded.ToArray();
    }

    private static bool Match(X86Form form, X86Operand[] operands, X86Instruction instruction, X86Target target, out int size)
    {
        size = 0;
        if (form.Isa != X86IsaFlags.None && !target.Has(form.Isa))
            return false;
        if (form.Has(X86FormFlags.Mode64) && !target.Is64Bit)
            return false;
        if (form.ExplicitOperandCount != operands.Length)
            return false;
        if ((instruction.OpMask != 0 || instruction.Zeroing) && !form.Has(X86FormFlags.Masking))
            return false;
        if (instruction.Rounding != X86RoundingMode.None && !form.Has(X86FormFlags.Rounding))
            return false;

        int variable = 0;
        int y = 0;
        int j = 0;
        foreach (var spec in form.Operands)
        {
            if (spec.Role == X86OperandRole.Implicit && !spec.One && spec.Fixed != X86Register.Rcx)
                continue;
            var operand = operands[j++];
            if (spec.Role is X86OperandRole.Immediate or X86OperandRole.Relative || spec.One)
                continue;
            if (!MatchLocation(form, spec, operand, target, ref variable, ref y))
                return false;
        }

        if (form.UsesOperandSize)
        {
            if (variable == 0)
                variable = form.Has(X86FormFlags.Default64) && target.Is64Bit ? 8 : target.XLen / 8;
            if (form.Has(X86FormFlags.Default64) && target.Is64Bit && variable == 4)
                return false;
            if (variable == 8 && !target.Is64Bit)
                return false;
            size = variable;
        }
        else if (y != 0)
        {
            size = y;
        }

        j = 0;
        foreach (var spec in form.Operands)
        {
            if (spec.Role == X86OperandRole.Implicit && !spec.One && spec.Fixed != X86Register.Rcx)
                continue;
            var operand = operands[j++];
            if (spec.One)
            {
                if (operand.Kind != X86OperandKind.Immediate || operand.Immediate != 1)
                    return false;
                continue;
            }
            if (spec.Role == X86OperandRole.Immediate && !MatchImmediate(spec, operand, size))
                return false;
            if (spec.Role == X86OperandRole.Relative)
            {
                if (operand.Kind is not (X86OperandKind.Immediate or X86OperandKind.Symbol))
                    return false;
                if (spec.Size == 1 ? operand.Size != 1 || operand.Kind != X86OperandKind.Immediate : operand.Size == 1)
                    return false;
            }
        }
        return true;
    }

    private static bool MatchLocation(X86Form form, X86OperandSpec spec, X86Operand operand, X86Target target, ref int variable, ref int y)
    {
        if (spec.Role == X86OperandRole.Implicit)
            return operand.Kind == X86OperandKind.Register && operand.Register == spec.Fixed && (spec.Class != X86OperandClass.Gpr || operand.Size == spec.Size);

        if (operand.Kind == X86OperandKind.Memory)
        {
            if (!spec.AllowsMemory)
                return false;
            if (spec.IndexClass != X86OperandClass.Gpr ? IndexClassOf(operand.IndexRegister) != spec.IndexClass : X86Registers.IsVector(operand.IndexRegister))
                return false;
            if (operand.IsBroadcast && (spec.Broadcast == 0 || form.Space != X86EncodingSpace.Evex || operand.Size != spec.MemorySize))
                return false;
            if (spec.Class != X86OperandClass.Gpr || operand.Size == 0)
                return true;
            return MatchGprSize(spec.MemorySize == spec.Size ? spec.Size : spec.MemorySize, operand.Size, ref variable, ref y);
        }
        if (operand.Kind != X86OperandKind.Register || !spec.AllowsRegister)
            return false;

        var register = operand.Register;
        switch (spec.Class)
        {
            case X86OperandClass.Gpr:
                return X86Registers.IsGeneral(register) && MatchGprSize(spec.Size, operand.Size == 0 ? target.XLen / 8 : operand.Size, ref variable, ref y);
            case X86OperandClass.Xmm:
                return X86Registers.IsXmm(register) && Encodable(form, register);
            case X86OperandClass.Ymm:
                return X86Registers.IsYmm(register) && Encodable(form, register);
            case X86OperandClass.Zmm:
                return X86Registers.IsZmm(register);
            case X86OperandClass.Mask:
                return X86Registers.IsMask(register);
            default:
                return false;
        }
    }

    private static X86OperandClass IndexClassOf(X86Register index)
        => X86Registers.IsXmm(index) ? X86OperandClass.Xmm : X86Registers.IsYmm(index) ? X86OperandClass.Ymm : X86Registers.IsZmm(index) ? X86OperandClass.Zmm : X86OperandClass.Gpr;

    private static bool HasIndex(X86Operand memory)
        => X86Registers.IsGeneral(memory.IndexRegister) || X86Registers.IsVector(memory.IndexRegister);

    private static bool Encodable(X86Form form, X86Register register)
        => form.Space == X86EncodingSpace.Evex || X86Registers.Index(register) < 16;

    private static bool MatchGprSize(byte expected, int actual, ref int variable, ref int y)
    {
        switch (expected)
        {
            case X86OperandSpec.SizeV:
                if (actual is not (2 or 4 or 8) || variable != 0 && variable != actual)
                    return false;
                variable = actual;
                return true;
            case X86OperandSpec.SizeY:
                if (actual is not (4 or 8) || y != 0 && y != actual)
                    return false;
                y = actual;
                return true;
            default:
                return actual == expected;
        }
    }

    private static bool MatchImmediate(X86OperandSpec spec, X86Operand operand, int size)
    {
        if (operand.Kind is not (X86OperandKind.Immediate or X86OperandKind.Symbol))
            return false;
        bool symbol = operand.Kind == X86OperandKind.Symbol;
        long value = operand.Immediate;
        int width = spec.Size switch
        {
            X86OperandSpec.SizeZ => size == 2 ? 2 : 4,
            X86OperandSpec.SizeV => size,
            _ => spec.Size,
        };
        bool signExtended = spec.SignedByte || spec.Size == X86OperandSpec.SizeZ && size == 8;
        switch (width)
        {
            case 1:
                return !symbol && (spec.SignedByte ? value is >= sbyte.MinValue and <= sbyte.MaxValue : value is >= sbyte.MinValue and <= byte.MaxValue);
            case 2:
                return !symbol && value is >= short.MinValue and <= ushort.MaxValue;
            case 4:
                if (symbol)
                    return !spec.SignedByte;
                return signExtended ? value is >= int.MinValue and <= int.MaxValue : value is >= int.MinValue and <= uint.MaxValue;
            default:
                return true;
        }
    }

    private static int ImmediateWidth(X86OperandSpec spec, int size)
        => spec.Size switch
        {
            X86OperandSpec.SizeZ => size == 2 ? 2 : 4,
            X86OperandSpec.SizeV => size,
            _ => spec.Size,
        };

    private static X86Operand Find(X86Selection selection, X86OperandRole role)
    {
        int j = 0;
        foreach (var spec in selection.Form.Operands)
        {
            if (spec.Role == X86OperandRole.Implicit && !spec.One && spec.Fixed != X86Register.Rcx)
                continue;
            var operand = selection.Operands[j++];
            if (spec.Role == role)
                return operand;
        }
        return X86Operand.None;
    }

    private static void EmitLegacy(X86InstructionWriter writer, X86Selection selection, X86Instruction instruction, X86Target target, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
    {
        var form = selection.Form;
        int size = selection.OperandSize;
        if (form.W == X86WMode.OperandSize && size == 2 && (form.Prefixes & X86Forms.Prefix66) == 0)
            writer.WriteByte(0x66);
        if ((form.Prefixes & X86Forms.Prefix66) != 0)
            writer.WriteByte(0x66);
        if ((form.Prefixes & X86Forms.PrefixF2) != 0)
            writer.WriteByte(0xF2);
        if ((form.Prefixes & X86Forms.PrefixF3) != 0)
            writer.WriteByte(0xF3);

        var reg = Find(selection, X86OperandRole.Reg);
        var rm = Find(selection, X86OperandRole.Rm);
        var opcodeRegister = Find(selection, X86OperandRole.OpcodeRegister);
        bool w = form.W == X86WMode.One || form.W == X86WMode.OperandSize && size == 8 && !form.Has(X86FormFlags.Default64);
        bool r = reg.Kind == X86OperandKind.Register && X86Registers.Index(reg.Register) >= 8;
        bool x = rm.Kind == X86OperandKind.Memory && X86Registers.IsGeneral(rm.IndexRegister) && X86Registers.Index(rm.IndexRegister) >= 8;
        bool b = rm.Kind == X86OperandKind.Register && X86Registers.Index(rm.Register) >= 8 ||
            rm.Kind == X86OperandKind.Memory && X86Registers.IsGeneral(rm.BaseRegister) && X86Registers.Index(rm.BaseRegister) >= 8 ||
            opcodeRegister.Kind == X86OperandKind.Register && X86Registers.Index(opcodeRegister.Register) >= 8;
        bool force = false;
        foreach (var operand in selection.Operands)
            force |= RequiresByteRex(operand);
        EmitRex(writer, target, w, r, x, b, force);

        if (form.Map >= 1)
            writer.WriteByte(0x0F);
        if (form.Map == 2)
            writer.WriteByte(0x38);
        else if (form.Map == 3)
            writer.WriteByte(0x3A);

        int opcode = form.Opcode;
        if (form.Has(X86FormFlags.OpcodeRegister))
            opcode += X86Registers.Index(opcodeRegister.Register) & 7;
        if (form.Has(X86FormFlags.OpcodeCondition))
            opcode += (int)instruction.Condition & 0xF;
        writer.WriteByte((byte)opcode);

        if (form.FixedModRm >= 0)
            writer.WriteByte((byte)form.FixedModRm);
        else if (form.HasModRm)
            EmitModRm(writer, reg.Kind == X86OperandKind.Register ? X86Registers.Index(reg.Register) : Math.Max((int)form.Extension, 0), rm, target, nextIp, symbols);
        EmitTrailing(writer, selection, target, nextIp, symbols);
    }

    private static void EmitVex(X86InstructionWriter writer, X86Selection selection, X86Instruction instruction, X86Target target, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
    {
        var form = selection.Form;
        var reg = Find(selection, X86OperandRole.Reg);
        var rm = Find(selection, X86OperandRole.Rm);
        var vvvv = Find(selection, X86OperandRole.Vvvv);
        int regIndex = reg.Kind == X86OperandKind.Register ? X86Registers.Index(reg.Register) : Math.Max((int)form.Extension, 0);
        bool r = regIndex >= 8;
        bool x = rm.Kind == X86OperandKind.Memory && HasIndex(rm) && (X86Registers.Index(rm.IndexRegister) & 8) != 0;
        bool b = rm.Kind == X86OperandKind.Register && X86Registers.Index(rm.Register) >= 8 ||
            rm.Kind == X86OperandKind.Memory && X86Registers.IsGeneral(rm.BaseRegister) && X86Registers.Index(rm.BaseRegister) >= 8;
        bool w = form.W == X86WMode.One || form.W == X86WMode.OperandSize && selection.OperandSize == 8;
        int v = vvvv.Kind == X86OperandKind.Register ? X86Registers.Index(vvvv.Register) : 0;
        if (!target.Is64Bit && (r || x || b || v >= 8))
            throw new NotSupportedException("Extended AVX registers require an x86-64 target");
        int pp = PrefixCode(form.Prefixes);
        int l = form.Has(X86FormFlags.VectorLengthIgnored) ? 0 : form.VectorLength;
        if (form.Map == 1 && !w && !x && !b)
        {
            writer.WriteByte(0xC5);
            writer.WriteByte((byte)((r ? 0 : 0x80) | (((~v) & 0xF) << 3) | (l << 2) | pp));
        }
        else
        {
            writer.WriteByte(0xC4);
            writer.WriteByte((byte)((r ? 0 : 0x80) | (x ? 0 : 0x40) | (b ? 0 : 0x20) | form.Map));
            writer.WriteByte((byte)((w ? 0x80 : 0) | (((~v) & 0xF) << 3) | (l << 2) | pp));
        }
        writer.WriteByte(form.Opcode);
        if (form.HasModRm)
            EmitModRm(writer, regIndex, rm, target, nextIp, symbols);
        EmitTrailing(writer, selection, target, nextIp, symbols);
    }

    private static void EmitEvex(X86InstructionWriter writer, X86Selection selection, X86Instruction instruction, X86Target target, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
    {
        var form = selection.Form;
        var reg = Find(selection, X86OperandRole.Reg);
        var rm = Find(selection, X86OperandRole.Rm);
        var vvvv = Find(selection, X86OperandRole.Vvvv);
        if (!target.Is64Bit)
            throw new NotSupportedException("EVEX encodings require an x86-64 target");
        int regIndex = reg.Kind == X86OperandKind.Register ? X86Registers.Index(reg.Register) : Math.Max((int)form.Extension, 0);
        int rmIndex = rm.Kind == X86OperandKind.Register ? X86Registers.Index(rm.Register) : 0;
        bool memory = rm.Kind == X86OperandKind.Memory;
        bool x = memory ? HasIndex(rm) && (X86Registers.Index(rm.IndexRegister) & 8) != 0 : (rmIndex & 16) != 0;
        bool b = memory ? X86Registers.IsGeneral(rm.BaseRegister) && X86Registers.Index(rm.BaseRegister) >= 8 : (rmIndex & 8) != 0;
        bool w = form.W == X86WMode.One || form.W == X86WMode.OperandSize && selection.OperandSize == 8;
        int v = vvvv.Kind == X86OperandKind.Register ? X86Registers.Index(vvvv.Register) : 0;
        int l = form.Has(X86FormFlags.VectorLengthIgnored) ? 0 : form.VectorLength;
        bool broadcast = memory && rm.IsBroadcast;
        // Embedded rounding sets EVEX.b and puts the mode in L'L
        if (instruction.Rounding != X86RoundingMode.None)
        {
            if (memory)
                throw new NotSupportedException("Embedded rounding requires a register operand");
            broadcast = true;
            l = (int)instruction.Rounding - 1;
        }
        writer.WriteByte(0x62);
        writer.WriteByte((byte)(((regIndex & 8) != 0 ? 0 : 0x80) | (x ? 0 : 0x40) | (b ? 0 : 0x20) | ((regIndex & 16) != 0 ? 0 : 0x10) | form.Map));
        writer.WriteByte((byte)((w ? 0x80 : 0) | (((~v) & 0xF) << 3) | 0x04 | PrefixCode(form.Prefixes)));
        writer.WriteByte((byte)((instruction.Zeroing ? 0x80 : 0) | (l << 5) | (broadcast ? 0x10 : 0) | ((v & 16) != 0 ? 0 : 0x08) | (instruction.OpMask & 7)));
        writer.WriteByte(form.Opcode);
        if (form.HasModRm)
            EmitModRm(writer, regIndex, rm, target, nextIp, symbols, Disp8Scale(form, broadcast));
        EmitTrailing(writer, selection, target, nextIp, symbols);
    }

    internal static int Disp8Scale(X86Form form, bool broadcast)
    {
        int vector = 16 << form.VectorLength;
        int element = form.ElementSize != 0 ? form.ElementSize : form.W == X86WMode.One ? 8 : 4;
        return form.Tuple switch
        {
            X86Tuple.Full => broadcast ? element : vector,
            X86Tuple.FullMemory => vector,
            X86Tuple.Half => broadcast ? element : vector / 2,
            X86Tuple.HalfMemory => vector / 2,
            X86Tuple.QuarterMemory => vector / 4,
            X86Tuple.EighthMemory => vector / 8,
            X86Tuple.Scalar or X86Tuple.Fixed => element,
            X86Tuple.Tuple2 => element * 2,
            X86Tuple.Tuple4 => element * 4,
            X86Tuple.Tuple8 => element * 8,
            X86Tuple.Mem128 => 16,
            X86Tuple.MoveDup => vector == 16 ? 8 : vector,
            _ => 1,
        };
    }

    private static int PrefixCode(byte prefixes)
        => (prefixes & X86Forms.Prefix66) != 0 ? 1 : (prefixes & X86Forms.PrefixF3) != 0 ? 2 : (prefixes & X86Forms.PrefixF2) != 0 ? 3 : 0;

    private static void EmitTrailing(X86InstructionWriter writer, X86Selection selection, X86Target target, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
    {
        int j = 0;
        foreach (var spec in selection.Form.Operands)
        {
            if (spec.Role == X86OperandRole.Implicit && !spec.One && spec.Fixed != X86Register.Rcx)
                continue;
            var operand = selection.Operands[j++];
            switch (spec.Role)
            {
                case X86OperandRole.Immediate:
                    {
                        int width = ImmediateWidth(spec, selection.OperandSize);
                        bool signExtended = spec.SignedByte || spec.Size == X86OperandSpec.SizeZ && selection.OperandSize == 8;
                        WriteImmediate(writer, ResolveImmediate(operand, symbols, nextIp), width, signExtended);
                        break;
                    }
                case X86OperandRole.Relative:
                    {
                        long displacement = ResolveRelative(operand, symbols, nextIp);
                        if (spec.Size == 1)
                            writer.WriteInt8(checked((sbyte)displacement));
                        else
                            writer.WriteInt32(checked((int)displacement));
                        break;
                    }
                case X86OperandRole.Is4:
                    writer.WriteByte((byte)(X86Registers.Index(operand.Register) << 4));
                    break;
            }
        }
    }

    private static string Describe(X86Operand operand)
        => operand.Kind switch
        {
            X86OperandKind.Register => X86Registers.Format(operand.Register, operand.Size),
            X86OperandKind.Memory => $"mem{operand.Size * 8}",
            X86OperandKind.Immediate => $"imm({operand.Immediate})",
            X86OperandKind.Symbol => "sym",
            _ => operand.Kind.ToString(),
        };

    private static void EmitModRm(X86InstructionWriter writer, int regField, X86Operand rmOperand, X86Target target, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols, int disp8Scale = 1)
    {
        if (rmOperand.Kind == X86OperandKind.Register)
        {
            var rm = X86Registers.Index(rmOperand.Register) & 7;
            writer.WriteByte((byte)(0xC0 | ((regField & 7) << 3) | rm));
            return;
        }
        if (rmOperand.Kind != X86OperandKind.Memory)
            throw new NotSupportedException("ModRM operand must be register or memory");

        var displacement = ResolveMemoryDisplacement(rmOperand, symbols, nextIp);
        var baseRegister = rmOperand.BaseRegister;
        var hasBase = X86Registers.IsGeneral(baseRegister);
        var hasIndex = HasIndex(rmOperand);
        var ripRelative = rmOperand.IsRipRelative || baseRegister == X86Register.Rip;

        if (ripRelative)
        {
            writer.WriteByte((byte)(((regField & 7) << 3) | 0x05));
            writer.WriteInt32(checked((int)displacement));
            return;
        }

        if (!hasBase && !hasIndex)
        {
            if (target.Is64Bit)
            {
                writer.WriteByte((byte)(((regField & 7) << 3) | 0x04));
                writer.WriteByte(0x25);
            }
            else
            {
                writer.WriteByte((byte)(((regField & 7) << 3) | 0x05));
            }
            writer.WriteInt32(checked((int)displacement));
            return;
        }

        if (hasIndex && rmOperand.IndexRegister == X86Register.Rsp)
            throw new NotSupportedException("rsp cannot be encoded as an x86 index register");

        var baseIndex = hasBase ? X86Registers.Index(baseRegister) : 5;
        var baseLow = baseIndex & 7;
        var needsSib = hasIndex || baseLow == 4 || !hasBase;
        bool compressed = displacement % disp8Scale == 0 && FitsSignedByte(displacement / disp8Scale);
        int mod;
        var forceDisp8Zero = hasBase && displacement == 0 && baseLow == 5;
        if (!hasBase)
            mod = 0;
        else if (displacement == 0 && baseLow != 5)
            mod = 0;
        else if (compressed || forceDisp8Zero)
            mod = 1;
        else
            mod = 2;

        writer.WriteByte((byte)((mod << 6) | ((regField & 7) << 3) | (needsSib ? 4 : baseLow)));
        if (needsSib)
        {
            var scale = ScaleBits(rmOperand.Scale);
            var index = hasIndex ? X86Registers.Index(rmOperand.IndexRegister) & 7 : 4;
            var sibBase = hasBase ? baseLow : 5;
            writer.WriteByte((byte)((scale << 6) | (index << 3) | sibBase));
        }

        if (mod == 1)
            writer.WriteInt8((sbyte)(displacement / disp8Scale));
        else if (mod == 2 || !hasBase)
            writer.WriteInt32(checked((int)displacement));
    }

    private static void EmitRex(X86InstructionWriter writer, X86Target target, bool w = false, bool r = false, bool x = false, bool b = false, bool force = false)
    {
        if (!target.Is64Bit)
        {
            if (force)
                throw new NotSupportedException("i386 has no byte form of esp, ebp, esi or edi.");
            if (w || r || x || b)
                throw new NotSupportedException("REX prefixes require an x86-64 target");
            return;
        }
        if (!w && !r && !x && !b && !force)
            return;
        writer.WriteByte((byte)(0x40 | (w ? 0x08 : 0) | (r ? 0x04 : 0) | (x ? 0x02 : 0) | (b ? 0x01 : 0)));
    }

    private static int OperandSize(X86Operand operand, X86Target target)
        => operand.Size != 0 ? operand.Size : target.XLen / 8;

    private static bool RequiresByteRex(X86Operand operand)
        => operand.Kind == X86OperandKind.Register && operand.Size == 1 && X86Registers.IsGeneral(operand.Register) && X86Registers.Index(operand.Register) >= 4;

    private static long ResolveImmediate(X86Operand operand, IReadOnlyDictionary<string, ulong>? symbols, ulong nextIp)
    {
        if (operand.Kind == X86OperandKind.Immediate)
            return operand.Immediate;
        if (operand.Kind != X86OperandKind.Symbol)
            throw new InvalidOperationException("Operand is not immediate or symbolic");
        var value = checked((long)ResolveSymbol(symbols, operand.Symbol!) + operand.Addend);
        if (operand.RelocationKind is X86ObjectRelocationKind.Relative8 or X86ObjectRelocationKind.Relative32)
            return checked(value - (long)nextIp);
        return value;
    }

    private static long ResolveRelative(X86Operand operand, IReadOnlyDictionary<string, ulong>? symbols, ulong nextIp)
    {
        if (operand.Kind == X86OperandKind.Immediate)
            return operand.Immediate;
        if (operand.Kind == X86OperandKind.Symbol)
            return checked((long)ResolveSymbol(symbols, operand.Symbol!) + operand.Addend - (long)nextIp);
        throw new InvalidOperationException("Operand is not a relative branch target");
    }

    private static long ResolveMemoryDisplacement(X86Operand operand, IReadOnlyDictionary<string, ulong>? symbols, ulong nextIp)
    {
        var value = operand.Displacement;
        if (!operand.HasSymbol)
            return value;

        var symbolValue = checked((long)ResolveSymbol(symbols, operand.Symbol!) + operand.Addend);
        if (operand.RelocationKind == X86ObjectRelocationKind.RipRelative32 || operand.IsRipRelative)
            return checked(symbolValue + value - (long)nextIp);
        return checked(symbolValue + value);
    }

    private static ulong ResolveSymbol(IReadOnlyDictionary<string, ulong>? symbols, string symbol)
    {
        if (symbols is null)
            return 0;
        if (symbols.TryGetValue(symbol, out var value))
            return value;
        throw new KeyNotFoundException($"Undefined x86 symbol: {symbol}");
    }

    private static void WriteImmediate(X86InstructionWriter writer, long value, int size, bool signExtended = false)
    {
        switch (size)
        {
            case 1:
                if (value < sbyte.MinValue ||
                    (!signExtended && value > byte.MaxValue) ||
                    (signExtended && value > sbyte.MaxValue))
                {
                    throw new OverflowException("Immediate does not fit 8 bits.");
                }
                writer.WriteInt8(unchecked((sbyte)value));
                break;
            case 2:
                if (value < short.MinValue ||
                    (!signExtended && value > ushort.MaxValue) ||
                    (signExtended && value > short.MaxValue))
                {
                    throw new OverflowException("Immediate does not fit 16 bits.");
                }
                writer.WriteInt16(unchecked((short)value));
                break;
            case 4:
                if (value < int.MinValue ||
                    (!signExtended && value > uint.MaxValue) ||
                    (signExtended && value > int.MaxValue))
                {
                    throw new OverflowException("Immediate does not fit 32 bits.");
                }
                writer.WriteInt32(unchecked((int)value));
                break;
            case 8:
                writer.WriteInt64(value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(size));
        }
    }

    private static int ScaleBits(int scale)
    {
        return scale switch
        {
            1 => 0,
            2 => 1,
            4 => 2,
            8 => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(scale)),
        };
    }

    private static bool FitsSignedByte(long value)
        => value >= sbyte.MinValue && value <= sbyte.MaxValue;

    private sealed class X86InstructionWriter
    {
        private readonly List<byte> _bytes = new List<byte>();

        public void WriteByte(byte value)
            => _bytes.Add(value);

        public void WriteInt8(sbyte value)
            => _bytes.Add(unchecked((byte)value));

        public void WriteInt16(short value)
        {
            _bytes.Add((byte)value);
            _bytes.Add((byte)(value >> 8));
        }

        public void WriteInt32(int value)
        {
            _bytes.Add((byte)value);
            _bytes.Add((byte)(value >> 8));
            _bytes.Add((byte)(value >> 16));
            _bytes.Add((byte)(value >> 24));
        }

        public void WriteInt64(long value)
        {
            for (var i = 0; i < 8; i++)
                _bytes.Add((byte)(value >> (i * 8)));
        }

        public byte[] ToArray()
            => _bytes.ToArray();
    }
}
