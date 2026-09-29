using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Cnidaria.Arm;

public sealed class ArmAssemblyWriterOptions
{
    public static ArmAssemblyWriterOptions Default { get; } = new ArmAssemblyWriterOptions();

    public bool IncludeLabels { get; set; } = true;
    public bool UseHexImmediates { get; set; } = true;
    public bool UseRegisterAliases { get; set; } = true;
}

public static class ArmAssembler
{
    public static ArmProgram Assemble(string text, ArmTarget target)
        => ArmAssemblyParser.Parse(text, target);

    public static ArmProgram Parse(string text, ArmTarget target)
        => Assemble(text, target);
}

public static class ArmDisassembler
{
    public static string Disassemble(ArmProgram obj, ArmAssemblyWriterOptions? options = null)
        => ArmAssemblyWriter.Write(obj, options);

    public static string Disassemble(ArmTextSection text, ArmAssemblyWriterOptions? options = null)
        => ArmAssemblyWriter.Write(text, options);

    public static string Disassemble(IEnumerable<ArmInstruction> instructions, ArmAssemblyWriterOptions? options = null)
        => ArmAssemblyWriter.Write(instructions, options);
}

internal static class ArmAssemblyParser
{
    public static ArmProgram Parse(string text, ArmTarget target)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));
        if (target is null)
            throw new ArgumentNullException(nameof(target));

        var instructions = ImmutableArray.CreateBuilder<ArmInstruction>();
        var labels = new Dictionary<string, int>(StringComparer.Ordinal);
        var position = 0;
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = StripComment(lines[lineIndex]).Trim();
            if (line.Length == 0)
                continue;

            while (true)
            {
                var colon = FindLabelColon(line);
                if (colon < 0)
                    break;
                var label = line.Substring(0, colon).Trim();
                ValidateLabel(label, lineIndex + 1);
                if (!labels.TryAdd(label, position))
                    throw new FormatException($"Duplicate ARM label '{label}' on line {lineIndex + 1}.");
                line = line.Substring(colon + 1).Trim();
                if (line.Length == 0)
                    break;
            }

            if (line.Length == 0)
                continue;

            foreach (var instruction in ParseInstruction(line, target, lineIndex + 1))
            {
                instructions.Add(instruction);
                position = checked(position + 4);
            }
        }

        return new ArmProgram(target, instructions.ToImmutable(), labels);
    }

    private static IEnumerable<ArmInstruction> ParseInstruction(string line, ArmTarget target, int lineNumber)
    {
        var split = FirstWhitespace(line);
        var mnemonicText = (split < 0 ? line : line.Substring(0, split)).Trim().ToLowerInvariant();
        var operandText = split < 0 ? string.Empty : line.Substring(split + 1).Trim();
        var operands = SplitOperands(operandText);

        if (mnemonicText is ".text" or ".arm" or ".code" or ".syntax" or ".arch" or ".cpu" or ".fpu" or ".align" or ".p2align" or ".globl" or ".global" or ".type" or ".size" or "global" or "extern")
            yield break;

        if (mnemonicText is ".word" or ".long")
        {
            foreach (var operand in operands)
                yield return ArmInstruction.Raw(unchecked((uint)ParseInteger(operand)));
            yield break;
        }

        if (target.Is32Bit && TryParseA32ConversionMnemonic(mnemonicText, out var conversionOpcode, out var conversionCondition, out var destinationSize, out var sourceSize))
        {
            ArmInstruction conversionInstruction;
            try
            {
                RequireCount(operands, 2, mnemonicText);
                var destination = ParseRegister(operands[0], target, 0);
                var source = ParseRegister(operands[1], target, 0);
                if (destination.Size != destinationSize || source.Size != sourceSize)
                    throw new FormatException("VFP conversion operand sizes do not match the mnemonic.");
                conversionInstruction = ArmInstruction.Binary(conversionOpcode, destination, source, conversionCondition);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException or NotSupportedException)
            {
                throw Error(lineNumber, ex.Message);
            }
            yield return conversionInstruction;
            yield break;
        }

        var condition = ArmCondition.Al;
        var setFlags = false;
        var precision = 0;
        var mnemonic = mnemonicText;

        if (mnemonic.EndsWith(".f32", StringComparison.Ordinal))
        {
            precision = 4;
            mnemonic = mnemonic.Substring(0, mnemonic.Length - 4);
        }
        else if (mnemonic.EndsWith(".f64", StringComparison.Ordinal))
        {
            precision = 8;
            mnemonic = mnemonic.Substring(0, mnemonic.Length - 4);
        }

        var parsedA32MemoryMnemonic = false;
        if (target.Is32Bit && (mnemonic.StartsWith("vldr", StringComparison.Ordinal) || mnemonic.StartsWith("vstr", StringComparison.Ordinal)))
        {
            ParseA32Mnemonic(ref mnemonic, ref condition, ref setFlags);
            parsedA32MemoryMnemonic = true;
        }

        if (mnemonic.StartsWith("v", StringComparison.Ordinal) && mnemonic.Length > 1 && mnemonic is not "vldr" and not "vstr")
            mnemonic = "f" + mnemonic.Substring(1);

        if (target.Is64Bit && mnemonic.StartsWith("b.", StringComparison.Ordinal))
        {
            if (!ArmConditions.TryParse(mnemonic.Substring(2), out condition))
                throw Error(lineNumber, "Invalid AArch64 branch condition.");
            mnemonic = "b";
        }
        else if (target.Is32Bit && !parsedA32MemoryMnemonic)
        {
            ParseA32Mnemonic(ref mnemonic, ref condition, ref setFlags);
        }

        if (mnemonic is "adds" or "subs" or "ands" or "bics" or "adcs" or "sbcs")
        {
            setFlags = true;
        }

        if (!ArmMnemonics.TryParse(mnemonic, out var opcode))
        {
            opcode = mnemonic switch
            {
                "vldr" => ArmInstrKind.Ldr,
                "vstr" => ArmInstrKind.Str,
                "ldmia" or "ldmfd" or "ldmib" or "ldmda" or "ldmdb" => ArmInstrKind.Ldm,
                "stmia" or "stmib" or "stmda" or "stmdb" or "stmfd" => ArmInstrKind.Stm,
                _ => ArmInstrKind.Invalid,
            };
        }
        if (opcode == ArmInstrKind.Invalid)
            throw Error(lineNumber, "Unknown ARM mnemonic: " + mnemonicText);

        ArmInstruction instruction;
        try
        {
            instruction = BuildInstruction(opcode, mnemonic, operands, target, condition, setFlags, precision);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException or NotSupportedException)
        {
            throw Error(lineNumber, ex.Message);
        }
        yield return instruction;
    }

    private static ArmInstruction BuildInstruction(
        ArmInstrKind opcode,
        string mnemonic,
        IReadOnlyList<string> operands,
        ArmTarget target,
        ArmCondition condition,
        bool setFlags,
        int precision)
    {
        switch (opcode)
        {
            case ArmInstrKind.Nop:
            case ArmInstrKind.Yield:
            case ArmInstrKind.Wfe:
            case ArmInstrKind.Wfi:
            case ArmInstrKind.Sev:
            case ArmInstrKind.Sevl:
                RequireCount(operands, 0, mnemonic);
                return new ArmInstruction(opcode, condition: condition);

            case ArmInstrKind.Ret:
                if (operands.Count == 0)
                    return new ArmInstruction(opcode, condition: condition);
                RequireCount(operands, 1, mnemonic);
                return ArmInstruction.Unary(opcode, ParseRegister(operands[0], target, precision), condition);

            case ArmInstrKind.B:
            case ArmInstrKind.Bl:
                RequireCount(operands, 1, mnemonic);
                return new ArmInstruction(opcode, ParseBranchTarget(operands[0], opcode, condition), condition: condition);

            case ArmInstrKind.Bx:
            case ArmInstrKind.Blx:
            case ArmInstrKind.Br:
            case ArmInstrKind.Blr:
                RequireCount(operands, 1, mnemonic);
                return ArmInstruction.Unary(opcode, ParseRegister(operands[0], target, precision), condition);

            case ArmInstrKind.Cbz:
            case ArmInstrKind.Cbnz:
                RequireCount(operands, 2, mnemonic);
                return new ArmInstruction(opcode, ParseRegister(operands[0], target, precision), ParseBranchTarget(operands[1], opcode, condition));

            case ArmInstrKind.Tbz:
            case ArmInstrKind.Tbnz:
                RequireCount(operands, 3, mnemonic);
                return new ArmInstruction(opcode, ParseRegister(operands[0], target, precision), ParseImmediate(operands[1]), ParseBranchTarget(operands[2], opcode, condition));

            case ArmInstrKind.Adr:
            case ArmInstrKind.Adrp:
                RequireCount(operands, 2, mnemonic);
                return ArmInstruction.Binary(opcode, ParseRegister(operands[0], target, precision), ParseAddressTarget(operands[1], opcode));

            case ArmInstrKind.Mov:
            case ArmInstrKind.Mvn:
                if (operands.Count is < 2 or > 3)
                    throw new FormatException(mnemonic + " expects two operands and an optional shift.");
                return new ArmInstruction(opcode, ParseRegister(operands[0], target, precision), ParseDataOperandWithSuffix(operands, 1, target, precision), condition: condition, setFlags: setFlags);

            case ArmInstrKind.Movw:
            case ArmInstrKind.Movt:
                RequireCount(operands, 2, mnemonic);
                return ArmInstruction.Binary(opcode, ParseRegister(operands[0], target, precision), ParseImmediate(operands[1]), condition);

            case ArmInstrKind.Movz:
            case ArmInstrKind.Movn:
            case ArmInstrKind.Movk:
                if (operands.Count is < 2 or > 3)
                    throw new FormatException(mnemonic + " expects two or three operands.");
                return new ArmInstruction(opcode, ParseRegister(operands[0], target, precision), ParseImmediate(operands[1]),
                    operands.Count == 3 ? ParseShiftAmountOperand(operands[2]) : ArmOperand.ImmediateOperand(0), condition: condition);

            case ArmInstrKind.Cmp:
            case ArmInstrKind.Cmn:
            case ArmInstrKind.Tst:
            case ArmInstrKind.Teq:
            case ArmInstrKind.Fcmp:
                if (operands.Count is < 2 or > 3)
                    throw new FormatException(mnemonic + " expects two operands and an optional shift.");
                return ArmInstruction.Binary(opcode, ParseRegister(operands[0], target, precision), ParseDataOperandWithSuffix(operands, 1, target, precision), condition);

            case ArmInstrKind.Add:
            case ArmInstrKind.Adds:
            case ArmInstrKind.Adc:
            case ArmInstrKind.Adcs:
            case ArmInstrKind.Sub:
            case ArmInstrKind.Subs:
            case ArmInstrKind.Sbc:
            case ArmInstrKind.Sbcs:
            case ArmInstrKind.Rsb:
            case ArmInstrKind.And:
            case ArmInstrKind.Ands:
            case ArmInstrKind.Orr:
            case ArmInstrKind.Eor:
            case ArmInstrKind.Bic:
            case ArmInstrKind.Bics:
            case ArmInstrKind.Fadd:
            case ArmInstrKind.Fsub:
            case ArmInstrKind.Fmul:
            case ArmInstrKind.Fdiv:
                if (operands.Count is < 3 or > 4)
                    throw new FormatException(mnemonic + " expects three operands and an optional shift.");
                return new ArmInstruction(opcode,
                    ParseRegister(operands[0], target, precision),
                    ParseRegister(operands[1], target, precision),
                    ParseDataOperandWithSuffix(operands, 2, target, precision),
                    condition: condition,
                    setFlags: setFlags);

            case ArmInstrKind.Lsl:
            case ArmInstrKind.Lsr:
            case ArmInstrKind.Asr:
            case ArmInstrKind.Ror:
                RequireCount(operands, 3, mnemonic);
                return ArmInstruction.Ternary(opcode, ParseRegister(operands[0], target, precision), ParseRegister(operands[1], target, precision), ParseDataOperand(operands[2], target, precision), condition).WithSetFlags(setFlags);

            case ArmInstrKind.Mul:
            case ArmInstrKind.Umulh:
            case ArmInstrKind.Smulh:
            case ArmInstrKind.Udiv:
            case ArmInstrKind.Sdiv:
                RequireCount(operands, 3, mnemonic);
                return ArmInstruction.Ternary(opcode, ParseRegister(operands[0], target, precision), ParseRegister(operands[1], target, precision), ParseRegister(operands[2], target, precision), condition).WithSetFlags(setFlags);

            case ArmInstrKind.Mla:
            case ArmInstrKind.Mls:
            case ArmInstrKind.Madd:
            case ArmInstrKind.Msub:
                RequireCount(operands, 4, mnemonic);
                return ArmInstruction.Quaternary(opcode,
                    ParseRegister(operands[0], target, precision),
                    ParseRegister(operands[1], target, precision),
                    ParseRegister(operands[2], target, precision),
                    ParseRegister(operands[3], target, precision),
                    condition).WithSetFlags(setFlags);

            case ArmInstrKind.Umull:
            case ArmInstrKind.Smull:
                RequireCount(operands, 4, mnemonic);
                return ArmInstruction.Quaternary(opcode,
                    ParseRegister(operands[0], target, precision),
                    ParseRegister(operands[1], target, precision),
                    ParseRegister(operands[2], target, precision),
                    ParseRegister(operands[3], target, precision),
                    condition).WithSetFlags(setFlags);

            case ArmInstrKind.Clz:
            case ArmInstrKind.Rev:
            case ArmInstrKind.Rev16:
            case ArmInstrKind.Revsh:
            case ArmInstrKind.Rbit:
            case ArmInstrKind.Sxtb:
            case ArmInstrKind.Sxth:
            case ArmInstrKind.Sxtw:
            case ArmInstrKind.Uxtb:
            case ArmInstrKind.Uxth:
            case ArmInstrKind.Fmov:
            case ArmInstrKind.Fsqrt:
            case ArmInstrKind.Fabs:
            case ArmInstrKind.Fneg:
            case ArmInstrKind.Scvtf:
            case ArmInstrKind.Ucvtf:
            case ArmInstrKind.Fcvtzs:
            case ArmInstrKind.Fcvtzu:
            case ArmInstrKind.Fcvt:
                RequireCount(operands, 2, mnemonic);
                return ArmInstruction.Binary(opcode, ParseRegister(operands[0], target, precision), ParseRegister(operands[1], target, precision), condition);

            case ArmInstrKind.Ldr:
            case ArmInstrKind.Str:
            case ArmInstrKind.Ldrb:
            case ArmInstrKind.Strb:
            case ArmInstrKind.Ldrh:
            case ArmInstrKind.Strh:
            case ArmInstrKind.Ldrsb:
            case ArmInstrKind.Ldrsh:
            case ArmInstrKind.Ldrsw:
            case ArmInstrKind.Ldur:
            case ArmInstrKind.Stur:
                if (operands.Count is < 2 or > 3)
                    throw new FormatException(mnemonic + " expects two operands and an optional post-index.");
                var data = ParseRegister(operands[0], target, precision);
                var address = ParseMemoryOrLiteral(operands[1], target, data.Size, opcode);
                if (operands.Count == 3)
                {
                    if (address.Kind != ArmOperandKind.Memory || address.AddressingMode != ArmAddressingMode.Offset || address.Immediate != 0)
                        throw new FormatException("Post-index addressing requires a base-only memory operand.");
                    var post = ParseImmediate(operands[2]).Immediate;
                    address = ArmOperand.Memory(address.BaseRegister, post, address.Size, ArmAddressingMode.PostIndex);
                }
                return ArmInstruction.Binary(opcode, data, address, condition);

            case ArmInstrKind.Ldaxr:
            case ArmInstrKind.Ldaxrb:
            case ArmInstrKind.Ldaxrh:
                RequireCount(operands, 2, mnemonic);
                return ArmInstruction.Binary(
                    opcode,
                    ParseRegister(operands[0], target, precision),
                    ParseMemoryOrLiteral(operands[1], target, 0, opcode),
                    condition);

            case ArmInstrKind.Stlxr:
            case ArmInstrKind.Stlxrb:
            case ArmInstrKind.Stlxrh:
                RequireCount(operands, 3, mnemonic);
                return ArmInstruction.Ternary(
                    opcode,
                    ParseRegister(operands[0], target, precision),
                    ParseRegister(operands[1], target, precision),
                    ParseMemoryOrLiteral(operands[2], target, 0, opcode),
                    condition);

            case ArmInstrKind.Ldp:
            case ArmInstrKind.Stp:
                if (operands.Count is < 3 or > 4)
                    throw new FormatException(mnemonic + " expects three operands and an optional post-index.");
                var first = ParseRegister(operands[0], target, precision);
                var second = ParseRegister(operands[1], target, precision);
                var pairAddress = ParseMemoryOrLiteral(operands[2], target, first.Size, opcode);
                if (operands.Count == 4)
                {
                    if (pairAddress.Kind != ArmOperandKind.Memory || pairAddress.AddressingMode != ArmAddressingMode.Offset || pairAddress.Immediate != 0)
                        throw new FormatException("Post-index addressing requires a base-only memory operand.");
                    pairAddress = ArmOperand.Memory(pairAddress.BaseRegister, ParseImmediate(operands[3]).Immediate, pairAddress.Size, ArmAddressingMode.PostIndex);
                }
                return ArmInstruction.Ternary(opcode, first, second, pairAddress, condition);

            case ArmInstrKind.Push:
            case ArmInstrKind.Pop:
                RequireCount(operands, 1, mnemonic);
                return ArmInstruction.Unary(opcode, ParseRegisterList(operands[0], target), condition);

            case ArmInstrKind.Ldm:
            case ArmInstrKind.Stm:
                RequireCount(operands, 2, mnemonic);
                var baseText = operands[0].Trim();
                var writeback = baseText.EndsWith("!", StringComparison.Ordinal);
                if (writeback)
                    baseText = baseText.Substring(0, baseText.Length - 1).TrimEnd();
                var mode = mnemonic switch
                {
                    "ldmia" or "ldmfd" or "stmia" => 1,
                    "ldmdb" or "stmdb" or "stmfd" => 2,
                    "ldmib" or "stmib" => 3,
                    "ldmda" or "stmda" => 4,
                    _ => 1,
                };
                return new ArmInstruction(opcode, ParseRegister(baseText, target, precision), ParseRegisterList(operands[1], target), ArmOperand.ImmediateOperand(mode), condition: condition, setFlags: writeback);

            case ArmInstrKind.Svc:
            case ArmInstrKind.Brk:
            case ArmInstrKind.Bkpt:
                RequireCount(operands, 1, mnemonic);
                return ArmInstruction.Unary(opcode, ParseImmediate(operands[0]), condition);

            case ArmInstrKind.Mrs:
                RequireCount(operands, 2, mnemonic);
                return ArmInstruction.Binary(opcode, ParseRegister(operands[0], target, precision), ParseSystemRegister(operands[1]), condition);

            case ArmInstrKind.Msr:
                RequireCount(operands, 2, mnemonic);
                return ArmInstruction.Binary(opcode, ParseSystemRegister(operands[0]), ParseRegister(operands[1], target, precision), condition);

            case ArmInstrKind.Dmb:
            case ArmInstrKind.Dsb:
            case ArmInstrKind.Isb:
                if (operands.Count == 0)
                    return new ArmInstruction(opcode);
                RequireCount(operands, 1, mnemonic);
                return ArmInstruction.Unary(opcode, ParseBarrierOption(operands[0]));

            default:
                throw new NotSupportedException("Assembler support is missing for " + opcode + ".");
        }
    }

    private static ArmOperand ParseDataOperandWithSuffix(IReadOnlyList<string> operands, int index, ArmTarget target, int size)
    {
        var operand = ParseDataOperand(operands[index], target, size);
        if (index + 1 >= operands.Count)
            return operand;
        var suffix = operands[index + 1].Trim();
        if (operand.Kind != ArmOperandKind.Register)
            throw new FormatException("A shift or extension suffix requires a register operand.");
        return ParseRegisterSuffix(operand, suffix);
    }

    private static ArmOperand ParseDataOperand(string text, ArmTarget target, int size)
    {
        text = text.Trim();
        if (text.StartsWith("#", StringComparison.Ordinal) || IsInteger(text))
            return ParseImmediate(text);
        if (ArmRegisters.TryParse(text, target, out var register, out var parsedSize))
            return ArmOperand.RegisterOperand(register, size > 0 ? size : parsedSize);
        return ParseSymbol(text, ArmRelocationKind.None);
    }

    private static ArmOperand ParseRegisterSuffix(ArmOperand register, string suffix)
    {
        var split = FirstWhitespace(suffix);
        var name = (split < 0 ? suffix : suffix.Substring(0, split)).Trim().ToLowerInvariant();
        var amountText = split < 0 ? string.Empty : suffix.Substring(split + 1).Trim();
        if (TryParseShift(name, out var shift))
        {
            var amount = shift == ArmShiftKind.Rrx ? 1 : checked((int)ParseInteger(amountText));
            return ArmOperand.ShiftedRegister(register.Register, shift, amount, register.Size);
        }
        if (TryParseExtend(name, out var extend))
        {
            var amount = amountText.Length == 0 ? 0 : checked((int)ParseInteger(amountText));
            return ArmOperand.ExtendedRegister(register.Register, extend, amount, register.Size);
        }
        throw new FormatException("Invalid ARM shift or extension: " + suffix);
    }

    private static ArmOperand ParseMemoryOrLiteral(string text, ArmTarget target, int size, ArmInstrKind opcode)
    {
        text = text.Trim();
        if (!text.StartsWith("[", StringComparison.Ordinal))
        {
            if (TryParseInteger(text, out var immediate))
                return ArmOperand.Literal(immediate, size);
            return ParseSymbol(text, ArmRelocationKind.LoadLiteral, size);
        }
        return ParseMemory(text, target, size);
    }

    private static ArmOperand ParseMemory(string text, ArmTarget target, int size)
    {
        var preIndex = text.EndsWith("!", StringComparison.Ordinal);
        if (preIndex)
            text = text.Substring(0, text.Length - 1).TrimEnd();
        if (text.Length < 2 || text[0] != '[' || text[^1] != ']')
            throw new FormatException("Invalid ARM memory operand: " + text);
        var inner = text.Substring(1, text.Length - 2);
        var parts = SplitOperands(inner);
        if (parts.Count == 0 || parts.Count > 3)
            throw new FormatException("Invalid ARM memory operand: " + text);
        var baseOperand = ParseRegister(parts[0], target, target.Is64Bit ? 8 : 4);
        if (parts.Count == 1)
            return ArmOperand.Memory(baseOperand.Register, 0, size, preIndex ? ArmAddressingMode.PreIndex : ArmAddressingMode.Offset);
        if (parts[1].TrimStart().StartsWith("#", StringComparison.Ordinal) || IsInteger(parts[1]))
        {
            var displacement = ParseImmediate(parts[1]).Immediate;
            return ArmOperand.Memory(baseOperand.Register, displacement, size, preIndex ? ArmAddressingMode.PreIndex : ArmAddressingMode.Offset);
        }
        var index = ParseRegister(parts[1], target, target.Is64Bit ? 8 : 4);
        var shift = ArmShiftKind.None;
        var shiftAmount = 0;
        var extend = ArmExtendKind.None;
        if (parts.Count == 3)
        {
            var suffix = ParseRegisterSuffix(index, parts[2]);
            shift = suffix.Shift;
            shiftAmount = suffix.ShiftAmount;
            extend = suffix.Extend;
        }
        return ArmOperand.Memory(baseOperand.Register, 0, size, preIndex ? ArmAddressingMode.PreIndex : ArmAddressingMode.Offset, index.Register, shift, shiftAmount, extend);
    }

    private static ArmOperand ParseBranchTarget(string text, ArmInstrKind opcode, ArmCondition condition)
    {
        if (TryParseInteger(text, out var immediate))
            return ArmOperand.ImmediateOperand(immediate);
        var relocation = opcode switch
        {
            ArmInstrKind.Bl => ArmRelocationKind.Call,
            ArmInstrKind.Cbz or ArmInstrKind.Cbnz => ArmRelocationKind.CompareBranch,
            ArmInstrKind.Tbz or ArmInstrKind.Tbnz => ArmRelocationKind.TestBranch,
            _ when condition != ArmCondition.Al => ArmRelocationKind.ConditionalBranch,
            _ => ArmRelocationKind.Branch,
        };
        return ParseSymbol(text, relocation);
    }

    private static ArmOperand ParseAddressTarget(string text, ArmInstrKind opcode)
    {
        if (TryParseInteger(text, out var immediate))
            return ArmOperand.ImmediateOperand(immediate);
        return ParseSymbol(text, opcode == ArmInstrKind.Adrp ? ArmRelocationKind.Adrp : ArmRelocationKind.Adr);
    }

    private static ArmOperand ParseSymbol(string text, ArmRelocationKind relocation, int size = 0)
    {
        text = text.Trim();
        var split = FindSymbolAddend(text);
        if (split < 0)
            return ArmOperand.SymbolOperand(text, relocation, 0, size);
        var symbol = text.Substring(0, split).Trim();
        var addend = ParseInteger(text.Substring(split));
        return ArmOperand.SymbolOperand(symbol, relocation, addend, size);
    }

    private static ArmOperand ParseRegister(string text, ArmTarget target, int forcedSize)
    {
        var register = ArmRegisters.Parse(text.Trim(), target, out var size);
        return ArmOperand.RegisterOperand(register, forcedSize > 0 ? forcedSize : size);
    }

    private static ArmOperand ParseImmediate(string text)
        => ArmOperand.ImmediateOperand(ParseInteger(text));

    private static ArmOperand ParseShiftAmountOperand(string text)
    {
        text = text.Trim();
        var split = FirstWhitespace(text);
        if (split < 0)
            return ParseImmediate(text);
        var shift = text.Substring(0, split).Trim().ToLowerInvariant();
        if (shift != "lsl")
            throw new FormatException("Move-wide instructions only support LSL.");
        return ParseImmediate(text.Substring(split + 1));
    }

    private static ArmOperand ParseRegisterList(string text, ArmTarget target)
    {
        text = text.Trim();
        if (text.Length < 2 || text[0] != '{' || text[^1] != '}')
            throw new FormatException("Invalid ARM register list: " + text);
        var entries = SplitOperands(text.Substring(1, text.Length - 2));
        uint mask = 0;
        foreach (var entryText in entries)
        {
            var entry = entryText.Trim();
            var dash = entry.IndexOf('-');
            if (dash >= 0)
            {
                var first = ParseRegister(entry.Substring(0, dash), target, 4).Register;
                var last = ParseRegister(entry.Substring(dash + 1), target, 4).Register;
                var firstIndex = ToA32RegisterIndex(first);
                var lastIndex = ToA32RegisterIndex(last);
                if (firstIndex > lastIndex)
                    throw new FormatException("ARM register-list range is reversed.");
                for (var i = firstIndex; i <= lastIndex; i++)
                    mask |= 1u << i;
            }
            else
            {
                mask |= 1u << ToA32RegisterIndex(ParseRegister(entry, target, 4).Register);
            }
        }
        return ArmOperand.RegisterList(mask);
    }

    private static int ToA32RegisterIndex(ArmRegister register)
    {
        if (register == ArmRegister.Sp)
            return 13;
        if (ArmRegisters.IsAArch32General(register))
            return ArmRegisters.Index(register);
        throw new FormatException("AArch32 register list contains a non-GPR.");
    }

    private static ArmOperand ParseSystemRegister(string text)
    {
        var register = text.Trim().ToLowerInvariant() switch
        {
            "cpsr" or "cpsr_cxsf" or "cpsr_fsxc" => ArmSystemRegister.Cpsr,
            "spsr" or "spsr_cxsf" or "spsr_fsxc" => ArmSystemRegister.Spsr,
            "nzcv" => ArmSystemRegister.Nzcv,
            "fpcr" => ArmSystemRegister.Fpcr,
            "fpsr" => ArmSystemRegister.Fpsr,
            "tpidr_el0" => ArmSystemRegister.TpidrEl0,
            "tpidrro_el0" => ArmSystemRegister.TpidrroEl0,
            "tpidr_el1" => ArmSystemRegister.TpidrEl1,
            "sp_el0" => ArmSystemRegister.SpEl0,
            "elr_el1" => ArmSystemRegister.ElrEl1,
            "spsr_el1" => ArmSystemRegister.SpsrEl1,
            "currentel" => ArmSystemRegister.CurrentEl,
            "daif" => ArmSystemRegister.Daif,
            _ => ArmSystemRegister.Invalid,
        };
        if (register == ArmSystemRegister.Invalid)
            throw new FormatException("Unknown ARM system register: " + text);
        return ArmOperand.SystemRegisterOperand(register);
    }

    private static ArmOperand ParseBarrierOption(string text)
    {
        var value = text.Trim().ToLowerInvariant() switch
        {
            "oshld" => 1,
            "oshst" => 2,
            "osh" => 3,
            "nshld" => 5,
            "nshst" => 6,
            "nsh" => 7,
            "ishld" => 9,
            "ishst" => 10,
            "ish" => 11,
            "ld" => 13,
            "st" => 14,
            "sy" => 15,
            _ => checked((int)ParseInteger(text)),
        };
        return ArmOperand.ImmediateOperand(value);
    }

    private static void ParseA32Mnemonic(ref string mnemonic, ref ArmCondition condition, ref bool setFlags)
    {
        if (IsA32Mnemonic(mnemonic))
            return;

        foreach (var suffix in ConditionSuffixes)
        {
            if (!mnemonic.EndsWith(suffix, StringComparison.Ordinal) || mnemonic.Length <= suffix.Length)
                continue;
            var candidate = mnemonic.Substring(0, mnemonic.Length - suffix.Length);
            if (IsA32Mnemonic(candidate))
            {
                mnemonic = candidate;
                ArmConditions.TryParse(suffix, out condition);
                return;
            }
            if (candidate.EndsWith("s", StringComparison.Ordinal) && candidate.Length > 1)
            {
                candidate = candidate.Substring(0, candidate.Length - 1);
                if (IsA32Mnemonic(candidate))
                {
                    mnemonic = candidate;
                    setFlags = true;
                    ArmConditions.TryParse(suffix, out condition);
                    return;
                }
            }
        }

        if (mnemonic.EndsWith("s", StringComparison.Ordinal) && mnemonic.Length > 1)
        {
            var candidate = mnemonic.Substring(0, mnemonic.Length - 1);
            if (IsA32Mnemonic(candidate))
            {
                mnemonic = candidate;
                setFlags = true;
            }
        }
    }

    private static bool IsA32Mnemonic(string mnemonic)
        => ArmMnemonics.TryParse(mnemonic, out _) || mnemonic is
            "vldr" or "vstr" or
            "ldmia" or "ldmfd" or "ldmib" or "ldmda" or "ldmdb" or
            "stmia" or "stmib" or "stmda" or "stmdb" or "stmfd";

    private static bool TryParseA32ConversionMnemonic(string mnemonic, out ArmInstrKind opcode, out ArmCondition condition, out int destinationSize, out int sourceSize)
    {
        opcode = ArmInstrKind.Invalid;
        condition = ArmCondition.Al;
        destinationSize = 0;
        sourceSize = 0;
        var parts = mnemonic.Split('.');
        if (parts.Length != 3)
            return false;
        var head = parts[0].ToLowerInvariant();
        if (head != "vcvt")
        {
            if (!head.StartsWith("vcvt", StringComparison.Ordinal) || !ArmConditions.TryParse(head.Substring(4), out condition))
                return false;
        }

        var destinationType = parts[1].ToLowerInvariant();
        var sourceType = parts[2].ToLowerInvariant();
        if (destinationType is "f32" or "f64" && sourceType is "s32" or "u32")
        {
            opcode = sourceType == "s32" ? ArmInstrKind.Scvtf : ArmInstrKind.Ucvtf;
            destinationSize = destinationType == "f64" ? 8 : 4;
            sourceSize = 4;
            return true;
        }
        if (destinationType is "s32" or "u32" && sourceType is "f32" or "f64")
        {
            opcode = destinationType == "s32" ? ArmInstrKind.Fcvtzs : ArmInstrKind.Fcvtzu;
            destinationSize = 4;
            sourceSize = sourceType == "f64" ? 8 : 4;
            return true;
        }
        return false;
    }

    private static bool TryParseShift(string text, out ArmShiftKind shift)
    {
        shift = text switch
        {
            "lsl" => ArmShiftKind.Lsl,
            "lsr" => ArmShiftKind.Lsr,
            "asr" => ArmShiftKind.Asr,
            "ror" => ArmShiftKind.Ror,
            "rrx" => ArmShiftKind.Rrx,
            _ => ArmShiftKind.None,
        };
        return shift != ArmShiftKind.None;
    }

    private static bool TryParseExtend(string text, out ArmExtendKind extend)
    {
        extend = text switch
        {
            "uxtb" => ArmExtendKind.Uxtb,
            "uxth" => ArmExtendKind.Uxth,
            "uxtw" => ArmExtendKind.Uxtw,
            "uxtx" => ArmExtendKind.Uxtx,
            "sxtb" => ArmExtendKind.Sxtb,
            "sxth" => ArmExtendKind.Sxth,
            "sxtw" => ArmExtendKind.Sxtw,
            "sxtx" => ArmExtendKind.Sxtx,
            _ => ArmExtendKind.None,
        };
        return extend != ArmExtendKind.None;
    }

    private static readonly string[] ConditionSuffixes =
    {
        "eq", "ne", "cs", "hs", "cc", "lo", "mi", "pl", "vs", "vc", "hi", "ls", "ge", "lt", "gt", "le", "al", "nv"
    };

    private static void RequireCount(IReadOnlyList<string> operands, int count, string mnemonic)
    {
        if (operands.Count != count)
            throw new FormatException($"{mnemonic} expects {count} operand{(count == 1 ? string.Empty : "s")}.");
    }

    private static List<string> SplitOperands(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return result;
        var square = 0;
        var curly = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '[': square++; break;
                case ']': square--; break;
                case '{': curly++; break;
                case '}': curly--; break;
                case ',' when square == 0 && curly == 0:
                    result.Add(text.Substring(start, i - start).Trim());
                    start = i + 1;
                    break;
            }
        }
        result.Add(text.Substring(start).Trim());
        return result;
    }

    private static string StripComment(string line)
    {
        var slash = line.IndexOf("//", StringComparison.Ordinal);
        var at = line.IndexOf('@');
        var semicolon = line.IndexOf(';');
        var cut = line.Length;
        if (slash >= 0) cut = Math.Min(cut, slash);
        if (at >= 0) cut = Math.Min(cut, at);
        if (semicolon >= 0) cut = Math.Min(cut, semicolon);
        return line.Substring(0, cut);
    }

    private static int FindLabelColon(string line)
    {
        var square = 0;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '[') square++;
            else if (line[i] == ']') square--;
            else if (line[i] == ':' && square == 0) return i;
            else if (char.IsWhiteSpace(line[i]) && square == 0) return -1;
        }
        return -1;
    }

    private static void ValidateLabel(string label, int lineNumber)
    {
        if (label.Length == 0 || !(char.IsLetter(label[0]) || label[0] is '_' or '.' or '$'))
            throw Error(lineNumber, "Invalid ARM label: " + label);
        for (var i = 1; i < label.Length; i++)
        {
            if (!(char.IsLetterOrDigit(label[i]) || label[i] is '_' or '.' or '$'))
                throw Error(lineNumber, "Invalid ARM label: " + label);
        }
    }

    private static int FirstWhitespace(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
                return i;
        }
        return -1;
    }

    private static int FindSymbolAddend(string text)
    {
        for (var i = 1; i < text.Length; i++)
        {
            if (text[i] is '+' or '-')
                return i;
        }
        return -1;
    }

    private static bool IsInteger(string text)
        => TryParseInteger(text, out _);

    private static long ParseInteger(string text)
    {
        if (TryParseInteger(text, out var value))
            return value;
        throw new FormatException("Invalid integer: " + text);
    }

    private static bool TryParseInteger(string text, out long value)
    {
        text = text.Trim();
        if (text.StartsWith("#", StringComparison.Ordinal) || text.StartsWith("=", StringComparison.Ordinal))
            text = text.Substring(1).TrimStart();
        var negative = text.StartsWith("-", StringComparison.Ordinal);
        var positive = text.StartsWith("+", StringComparison.Ordinal);
        if (negative || positive)
            text = text.Substring(1);
        ulong parsed;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!ulong.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out parsed))
            {
                value = 0;
                return false;
            }
        }
        else if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out parsed))
        {
            value = 0;
            return false;
        }
        if (negative)
        {
            if (parsed > 0x8000000000000000UL)
            {
                value = 0;
                return false;
            }
            value = parsed == 0x8000000000000000UL ? long.MinValue : -(long)parsed;
        }
        else
        {
            value = unchecked((long)parsed);
        }
        return true;
    }

    private static FormatException Error(int lineNumber, string message)
        => new FormatException($"ARM assembly line {lineNumber}: {message}");
}

internal static class ArmAssemblyWriter
{
    public static string Write(ArmProgram obj, ArmAssemblyWriterOptions? options = null)
    {
        if (obj is null)
            throw new ArgumentNullException(nameof(obj));
        return Write(obj.Text, options, obj.Target);
    }

    public static string Write(ArmTextSection text, ArmAssemblyWriterOptions? options = null)
        => Write(text, options, ArmTarget.Arm64);

    public static string Write(IEnumerable<ArmInstruction> instructions, ArmAssemblyWriterOptions? options = null)
        => Write(new ArmTextSection(instructions), options, ArmTarget.Arm64);

    private static string Write(ArmTextSection text, ArmAssemblyWriterOptions? options, ArmTarget target)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));
        options ??= ArmAssemblyWriterOptions.Default;
        var labelsByOffset = text.Labels
            .GroupBy(static pair => pair.Value)
            .ToDictionary(static group => group.Key, static group => group.Select(static pair => pair.Key).OrderBy(static name => name, StringComparer.Ordinal).ToImmutableArray());
        var sb = new StringBuilder();
        var position = 0;
        foreach (var instruction in text.Instructions)
        {
            if (options.IncludeLabels && labelsByOffset.TryGetValue(position, out var labels))
            {
                foreach (var label in labels)
                    sb.Append(label).AppendLine(":");
            }
            sb.Append("    ").AppendLine(WriteInstruction(instruction, target, options));
            position += 4;
        }
        if (options.IncludeLabels && labelsByOffset.TryGetValue(position, out var endLabels))
        {
            foreach (var label in endLabels)
                sb.Append(label).AppendLine(":");
        }
        return sb.ToString();
    }

    public static string WriteInstruction(ArmInstruction instruction, ArmTarget target, ArmAssemblyWriterOptions? options = null)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        options ??= ArmAssemblyWriterOptions.Default;
        if (instruction.Opcode == ArmInstrKind.Raw)
            return ".word 0x" + instruction.RawWord.ToString("x8", CultureInfo.InvariantCulture);

        var mnemonic = FormatMnemonic(instruction, target);
        var operands = new List<string>(4);
        AddOperand(operands, instruction.Operand0, instruction, target, options);
        AddOperand(operands, instruction.Operand1, instruction, target, options);
        if (instruction.Opcode is not ArmInstrKind.Ldm and not ArmInstrKind.Stm)
            AddOperand(operands, instruction.Operand2, instruction, target, options);
        AddOperand(operands, instruction.Operand3, instruction, target, options);

        if (instruction.Opcode is (ArmInstrKind.Movz or ArmInstrKind.Movn or ArmInstrKind.Movk) && operands.Count > 2)
            operands[2] = "lsl " + operands[2];

        if (target.Is32Bit && instruction.Opcode == ArmInstrKind.Msr && operands.Count > 0 && (instruction.Operand0.SystemRegister == ArmSystemRegister.Cpsr || instruction.Operand0.SystemRegister == ArmSystemRegister.Spsr))
            operands[0] += "_fsxc";

        if (instruction.Opcode is (ArmInstrKind.Ldm or ArmInstrKind.Stm) && instruction.SetFlags && operands.Count > 0)
            operands[0] += "!";

        if (target.Is64Bit && instruction.Opcode is (ArmInstrKind.Strb or ArmInstrKind.Strh) && operands.Count > 0 &&
            operands[0].Length > 1 && operands[0][0] == 'x' && (char.IsDigit(operands[0][1]) || operands[0] == "xzr"))
            operands[0] = "w" + operands[0].Substring(1);

        return operands.Count == 0 ? mnemonic : mnemonic + " " + string.Join(", ", operands);
    }

    private static string FormatMnemonic(ArmInstruction instruction, ArmTarget target)
    {
        string mnemonic;
        if (target.Is32Bit && instruction.Opcode is (ArmInstrKind.Ldr or ArmInstrKind.Str) && ArmRegisters.IsVector(instruction.Operand0.Register))
        {
            mnemonic = instruction.Opcode == ArmInstrKind.Ldr ? "vldr" : "vstr";
        }
        else if (target.Is32Bit && instruction.Opcode is (ArmInstrKind.Fmov or ArmInstrKind.Fadd or ArmInstrKind.Fsub or ArmInstrKind.Fmul or ArmInstrKind.Fdiv or ArmInstrKind.Fsqrt or ArmInstrKind.Fabs or ArmInstrKind.Fneg or ArmInstrKind.Fcmp))
        {
            mnemonic = "v" + ArmMnemonics.Format(instruction.Opcode).Substring(1);
            var size = instruction.Operand0.Size > 0 ? instruction.Operand0.Size : instruction.Operand1.Size;
            mnemonic += size == 8 ? ".f64" : ".f32";
        }
        else if (target.Is32Bit && instruction.Opcode == ArmInstrKind.Fcvt)
        {
            mnemonic = instruction.Operand0.Size == 8 ? "vcvt.f64.f32" : "vcvt.f32.f64";
        }
        else if (target.Is32Bit && instruction.Opcode is (ArmInstrKind.Scvtf or ArmInstrKind.Ucvtf or ArmInstrKind.Fcvtzs or ArmInstrKind.Fcvtzu))
        {
            var destinationType = instruction.Opcode switch
            {
                ArmInstrKind.Scvtf or ArmInstrKind.Ucvtf => instruction.Operand0.Size == 8 ? "f64" : "f32",
                ArmInstrKind.Fcvtzs => "s32",
                _ => "u32",
            };
            var sourceType = instruction.Opcode switch
            {
                ArmInstrKind.Scvtf => "s32",
                ArmInstrKind.Ucvtf => "u32",
                _ => instruction.Operand1.Size == 8 ? "f64" : "f32",
            };
            mnemonic = "vcvt." + destinationType + "." + sourceType;
        }
        else
        {
            mnemonic = ArmMnemonics.Format(instruction.Opcode);
        }

        if (target.Is64Bit && instruction.Opcode == ArmInstrKind.B && instruction.Condition != ArmCondition.Al)
            return "b." + ArmConditions.Format(instruction.Condition);

        if (target.Is32Bit && instruction.Opcode is (ArmInstrKind.Ldm or ArmInstrKind.Stm))
        {
            var mode = instruction.Operand2.Kind == ArmOperandKind.Immediate ? instruction.Operand2.Immediate : 1;
            mnemonic += mode switch
            {
                2 => "db",
                3 => "ib",
                4 => "da",
                _ => "ia",
            };
        }
        else if (target.Is32Bit && instruction.SetFlags && instruction.Opcode is
            ArmInstrKind.Mov or ArmInstrKind.Mvn or ArmInstrKind.Eor or ArmInstrKind.Orr or ArmInstrKind.Rsb or
            ArmInstrKind.Adc or ArmInstrKind.Sbc or ArmInstrKind.Mul or ArmInstrKind.Mla or ArmInstrKind.Umull or
            ArmInstrKind.Smull or ArmInstrKind.Lsl or ArmInstrKind.Lsr or ArmInstrKind.Asr or ArmInstrKind.Ror)
        {
            mnemonic += "s";
        }

        if (target.Is32Bit && instruction.Condition != ArmCondition.Al)
        {
            var suffix = ArmConditions.Format(instruction.Condition);
            var dot = mnemonic.IndexOf('.');
            mnemonic = dot > 0 ? mnemonic.Insert(dot, suffix) : mnemonic + suffix;
        }
        return mnemonic;
    }

    private static void AddOperand(List<string> operands, ArmOperand operand, ArmInstruction instruction, ArmTarget target, ArmAssemblyWriterOptions options)
    {
        if (operand.Kind == ArmOperandKind.None)
            return;
        operands.Add(FormatOperand(operand, instruction, target, options));
    }

    private static string FormatOperand(ArmOperand operand, ArmInstruction instruction, ArmTarget target, ArmAssemblyWriterOptions options)
    {
        return operand.Kind switch
        {
            ArmOperandKind.Register => ArmRegisters.Format(operand.Register, operand.Size, target),
            ArmOperandKind.Immediate when instruction.Opcode is ArmInstrKind.Dmb or ArmInstrKind.Dsb or ArmInstrKind.Isb => FormatBarrierOption(operand.Immediate, options),
            ArmOperandKind.Immediate => "#" + FormatInteger(operand.Immediate, options),
            ArmOperandKind.Symbol => FormatSymbol(operand, options),
            ArmOperandKind.ShiftedRegister => FormatShiftedRegister(operand, target, options),
            ArmOperandKind.ExtendedRegister => FormatExtendedRegister(operand, target, options),
            ArmOperandKind.Memory => FormatMemory(operand, target, options),
            ArmOperandKind.RegisterList => FormatRegisterList(operand.RegisterMask),
            ArmOperandKind.SystemRegister => FormatSystemRegister(operand.SystemRegister),
            _ => string.Empty,
        };
    }

    private static string FormatShiftedRegister(ArmOperand operand, ArmTarget target, ArmAssemblyWriterOptions options)
    {
        var register = ArmRegisters.Format(operand.Register, operand.Size, target);
        if (operand.Shift == ArmShiftKind.None || operand.Shift == ArmShiftKind.Lsl && operand.ShiftAmount == 0)
            return register;
        if (operand.Shift == ArmShiftKind.Rrx)
            return register + ", rrx";
        return register + ", " + FormatShift(operand.Shift) + " #" + FormatInteger(operand.ShiftAmount, options);
    }

    private static string FormatExtendedRegister(ArmOperand operand, ArmTarget target, ArmAssemblyWriterOptions options)
    {
        var result = ArmRegisters.Format(operand.Register, operand.Size, target) + ", " + operand.Extend.ToString().ToLowerInvariant();
        if (operand.ShiftAmount != 0)
            result += " #" + FormatInteger(operand.ShiftAmount, options);
        return result;
    }

    private static string FormatMemory(ArmOperand operand, ArmTarget target, ArmAssemblyWriterOptions options)
    {
        if (operand.AddressingMode == ArmAddressingMode.Literal)
            return FormatInteger(operand.Immediate, options);
        var sb = new StringBuilder();
        sb.Append('[').Append(ArmRegisters.Format(operand.BaseRegister, target.Is64Bit ? 8 : 4, target));
        if (operand.AddressingMode != ArmAddressingMode.PostIndex)
        {
            if (operand.IndexRegister != ArmRegister.Invalid)
            {
                var indexSize = target.Is64Bit && operand.Extend is not ArmExtendKind.Uxtw and not ArmExtendKind.Sxtw ? 8 : 4;
                sb.Append(", ").Append(ArmRegisters.Format(operand.IndexRegister, indexSize, target));
                if (operand.Extend != ArmExtendKind.None)
                {
                    sb.Append(", ").Append(operand.Extend.ToString().ToLowerInvariant());
                    if (operand.ShiftAmount != 0)
                        sb.Append(" #").Append(FormatInteger(operand.ShiftAmount, options));
                }
                else if (operand.Shift != ArmShiftKind.None || operand.ShiftAmount != 0)
                {
                    sb.Append(", ").Append(FormatShift(operand.Shift == ArmShiftKind.None ? ArmShiftKind.Lsl : operand.Shift));
                    sb.Append(" #").Append(FormatInteger(operand.ShiftAmount, options));
                }
            }
            else if (operand.Immediate != 0)
            {
                sb.Append(", #").Append(FormatInteger(operand.Immediate, options));
            }
        }
        sb.Append(']');
        if (operand.AddressingMode == ArmAddressingMode.PreIndex)
            sb.Append('!');
        else if (operand.AddressingMode == ArmAddressingMode.PostIndex)
            sb.Append(", #").Append(FormatInteger(operand.Immediate, options));
        return sb.ToString();
    }

    private static string FormatRegisterList(uint mask)
    {
        var names = new List<string>();
        var index = 0;
        while (index < 16)
        {
            if ((mask & (1u << index)) == 0)
            {
                index++;
                continue;
            }
            var first = index;
            var last = index;
            while (last + 1 < 16 && (mask & (1u << (last + 1))) != 0)
                last++;
            if (last - first >= 2)
                names.Add(FormatA32Register(first) + "-" + FormatA32Register(last));
            else
            {
                names.Add(FormatA32Register(first));
                if (last != first)
                    names.Add(FormatA32Register(last));
            }
            index = last + 1;
        }
        return "{" + string.Join(", ", names) + "}";
    }

    private static string FormatA32Register(int index)
        => index switch
        {
            13 => "sp",
            14 => "lr",
            15 => "pc",
            _ => "r" + index.ToString(CultureInfo.InvariantCulture),
        };

    private static string FormatSystemRegister(ArmSystemRegister register)
        => register switch
        {
            ArmSystemRegister.Cpsr => "cpsr",
            ArmSystemRegister.Spsr => "spsr",
            ArmSystemRegister.Nzcv => "nzcv",
            ArmSystemRegister.Fpcr => "fpcr",
            ArmSystemRegister.Fpsr => "fpsr",
            ArmSystemRegister.TpidrEl0 => "tpidr_el0",
            ArmSystemRegister.TpidrroEl0 => "tpidrro_el0",
            ArmSystemRegister.TpidrEl1 => "tpidr_el1",
            ArmSystemRegister.SpEl0 => "sp_el0",
            ArmSystemRegister.ElrEl1 => "elr_el1",
            ArmSystemRegister.SpsrEl1 => "spsr_el1",
            ArmSystemRegister.CurrentEl => "currentel",
            ArmSystemRegister.Daif => "daif",
            _ => "invalid",
        };

    private static string FormatSymbol(ArmOperand operand, ArmAssemblyWriterOptions options)
    {
        var result = operand.Symbol ?? string.Empty;
        if (operand.Addend > 0)
            result += "+" + FormatInteger(operand.Addend, options);
        else if (operand.Addend < 0)
            result += "-" + FormatInteger(-operand.Addend, options);
        return result;
    }

    private static string FormatBarrierOption(long value, ArmAssemblyWriterOptions options)
        => value switch
        {
            1 => "oshld",
            2 => "oshst",
            3 => "osh",
            5 => "nshld",
            6 => "nshst",
            7 => "nsh",
            9 => "ishld",
            10 => "ishst",
            11 => "ish",
            13 => "ld",
            14 => "st",
            15 => "sy",
            _ => "#" + FormatInteger(value, options),
        };

    private static string FormatShift(ArmShiftKind shift)
        => shift switch
        {
            ArmShiftKind.Lsl => "lsl",
            ArmShiftKind.Lsr => "lsr",
            ArmShiftKind.Asr => "asr",
            ArmShiftKind.Ror => "ror",
            ArmShiftKind.Rrx => "rrx",
            _ => "lsl",
        };

    private static string FormatInteger(long value, ArmAssemblyWriterOptions options)
    {
        if (!options.UseHexImmediates || value is > -10 and < 10)
            return value.ToString(CultureInfo.InvariantCulture);
        if (value < 0)
            return "-0x" + unchecked((ulong)-value).ToString("x", CultureInfo.InvariantCulture);
        return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
    }
}
