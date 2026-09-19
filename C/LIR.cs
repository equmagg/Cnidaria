using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Cnidaria.C;

public sealed class LirOptions
{
    public static LirOptions Default { get; } = new LirOptions();

    public bool KeepNops { get; }
    public bool EmitValueNumberComments { get; }
    public bool PreserveUnreachableBlocks { get; }

    public LirOptions(
        bool keepNops = false,
        bool emitValueNumberComments = true,
        bool preserveUnreachableBlocks = false)
    {
        KeepNops = keepNops;
        EmitValueNumberComments = emitValueNumberComments;
        PreserveUnreachableBlocks = preserveUnreachableBlocks;
    }
}

public enum LirRegisterClass : byte
{
    Void,
    General,
    Floating,
    Vector,
    Address,
    Aggregate,
    Memory,
    Unknown,
}

public enum LirOperandKind : byte
{
    None,
    Register,
    Immediate,
    Symbol,
    StackSlot,
    Address,
    Label,
    Undefined,
    Void,
}

public enum LirAddressKind : byte
{
    StackSlot,
    Symbol,
    Indirect,
    Element,
    Field,
}

public enum LirInstructionKind : ushort
{
    Nop,
    Parameter,
    Copy,
    ParallelCopy,
    Constant,
    Zero,
    Unary,
    Binary,
    Convert,
    Cast,
    AddressOf,
    Load,
    Store,
    ZeroMemory,
    Call,
    VaStart,
    VaArg,
    InlineAssembly,
    Jump,
    Branch,
    Switch,
    Return,
    Unreachable,
}

public enum LirProblemKind : byte
{
    MissingTarget,
    UnsupportedNode,
    InvalidAddress,
    PromotedAddressTakenValue,
}

public sealed class LirModule
{
    public SemanticModel SemanticModel { get; }
    public GimpleTree InputGimpleTree { get; }
    public GimplePipelineResult Gimple { get; }
    public ImmutableArray<LirGlobal> Globals { get; }
    public ImmutableArray<LirFunction> Functions { get; }
    public ImmutableArray<LirProblem> Problems { get; }

    private LirModule(
        GimplePipelineResult gimple,
        ImmutableArray<LirGlobal> globals,
        ImmutableArray<LirFunction> functions,
        ImmutableArray<LirProblem> problems)
    {
        Gimple = gimple ?? throw new ArgumentNullException(nameof(gimple));
        InputGimpleTree = gimple.InputTree;
        SemanticModel = gimple.SemanticModel;
        Globals = globals.IsDefault ? ImmutableArray<LirGlobal>.Empty : globals;
        Functions = functions.IsDefault ? ImmutableArray<LirFunction>.Empty : functions;
        Problems = problems.IsDefault ? ImmutableArray<LirProblem>.Empty : problems;
    }

    public static LirModule Lower(SemanticModel semanticModel, GimplePipelineOptions? gimpleOptions = null, LirOptions? options = null)
    {
        if (semanticModel is null)
            throw new ArgumentNullException(nameof(semanticModel));

        return Lower(GimplePipeline.Run(semanticModel, gimpleOptions), options);
    }

    public static LirModule Lower(GimpleTree gimpleTree, GimplePipelineOptions? gimpleOptions = null, LirOptions? options = null)
    {
        if (gimpleTree is null)
            throw new ArgumentNullException(nameof(gimpleTree));

        return Lower(GimplePipeline.Run(gimpleTree, gimpleOptions), options);
    }

    public static LirModule Lower(ControlFlowGraph controlFlowGraph, GimplePipelineOptions? gimpleOptions = null, LirOptions? options = null)
    {
        if (controlFlowGraph is null)
            throw new ArgumentNullException(nameof(controlFlowGraph));

        return Lower(GimplePipeline.Run(controlFlowGraph, gimpleOptions), options);
    }

    public static LirModule Lower(GimplePipelineResult gimple, LirOptions? options = null)
    {
        if (gimple is null)
            throw new ArgumentNullException(nameof(gimple));

        options ??= LirOptions.Default;
        var globals = ImmutableArray.CreateBuilder<LirGlobal>();
        var functions = ImmutableArray.CreateBuilder<LirFunction>();
        var problems = ImmutableArray.CreateBuilder<LirProblem>();

        foreach (var member in gimple.InputTree.Members)
        {
            if (member is not GimpleGlobalDeclaration global)
                continue;

            foreach (var declaration in global.Declarators)
            {
                if (declaration.StorageClass == StorageClass.Typedef ||
                    declaration.Symbol is TypeAliasSymbol ||
                    declaration.Symbol is FunctionSymbol ||
                    declaration.Type.Type is FunctionType)
                {
                    continue;
                }
                globals.Add(new LirGlobal(declaration.Symbol, declaration.Type, declaration.StorageClass, declaration.Initializer));
            }
        }

        foreach (var function in gimple.Functions)
        {
            var lowered = LirFunctionBuilder.Lower(function, options, gimple.SemanticModel.Compilation.Options.Target);
            functions.Add(lowered.Function);
            problems.AddRange(lowered.Problems);
        }

        foreach (var gimpleProblem in gimple.Problems)
            problems.Add(new LirProblem(LirProblemKind.UnsupportedNode, gimpleProblem.Block, null, gimpleProblem.Message));

        return new LirModule(gimple, globals.ToImmutable(), functions.ToImmutable(), problems.ToImmutable());
    }

    public override string ToString()
        => LirPrinter.Print(this);
}

public sealed class LirGlobal
{
    public Symbol? Symbol { get; }
    public QualifiedType Type { get; }
    public StorageClass StorageClass { get; }
    public GimpleInitializer? Initializer { get; }

    public LirGlobal(Symbol? symbol, QualifiedType type, StorageClass storageClass, GimpleInitializer? initializer)
    {
        Symbol = symbol;
        Type = GimpleTypeHelpers.Normalize(type);
        StorageClass = storageClass;
        Initializer = initializer;
    }
}

public sealed class LirFunction
{
    public GimpleFunctionAnnotations GimpleFunctionAnnotations { get; }
    public FunctionSymbol? Symbol => GimpleFunctionAnnotations.Symbol;
    public LirBlock Entry { get; }
    public ImmutableArray<LirVirtualRegister> VirtualRegisters { get; }
    public ImmutableArray<LirStackSlot> StackSlots { get; }
    public ImmutableArray<LirBlock> Blocks { get; }
    public ImmutableArray<LirProblem> Problems { get; }

    internal LirFunction(
        GimpleFunctionAnnotations gimpleFunction,
        LirBlock entry,
        ImmutableArray<LirVirtualRegister> virtualRegisters,
        ImmutableArray<LirStackSlot> stackSlots,
        ImmutableArray<LirBlock> blocks,
        ImmutableArray<LirProblem> problems)
    {
        GimpleFunctionAnnotations = gimpleFunction ?? throw new ArgumentNullException(nameof(gimpleFunction));
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        VirtualRegisters = virtualRegisters.IsDefault ? ImmutableArray<LirVirtualRegister>.Empty : virtualRegisters;
        StackSlots = stackSlots.IsDefault ? ImmutableArray<LirStackSlot>.Empty : stackSlots;
        Blocks = blocks.IsDefault ? ImmutableArray<LirBlock>.Empty : blocks;
        Problems = problems.IsDefault ? ImmutableArray<LirProblem>.Empty : problems;
    }

    public override string ToString()
        => LirPrinter.Print(this);
}

public sealed class LirBlock
{
    private ImmutableArray<LirInstruction> _instructions;

    public int Ordinal { get; }
    public string Name { get; }
    public ControlFlowBlock? SourceBlock { get; }
    public bool IsEdgeSplit { get; }
    public ImmutableArray<LirInstruction> Instructions => _instructions;

    internal LirBlock(int ordinal, string name, ControlFlowBlock? sourceBlock, bool isEdgeSplit)
    {
        if (ordinal < 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal));

        Ordinal = ordinal;
        Name = string.IsNullOrWhiteSpace(name)
            ? "bb" + ordinal.ToString(CultureInfo.InvariantCulture)
            : name;
        SourceBlock = sourceBlock;
        IsEdgeSplit = isEdgeSplit;
        _instructions = ImmutableArray<LirInstruction>.Empty;
    }

    internal void SetInstructions(ImmutableArray<LirInstruction> instructions)
        => _instructions = instructions.IsDefault ? ImmutableArray<LirInstruction>.Empty : instructions;

    public override string ToString() => Name;
}

public sealed class LirVirtualRegister
{
    public int Ordinal { get; }
    public string Name { get; }
    public QualifiedType Type { get; }
    public LirRegisterClass RegisterClass { get; }
    public GimpleName? SourceName { get; }
    public ValueNumber? ValueNumber { get; }
    public MachineRegister FixedRegister { get; }
    public bool HasFixedRegister => FixedRegister != MachineRegister.Invalid;
    public bool IsCompilerTemporary => SourceName is null;
    /// <summary>The frame slot this register's storage is, when it shares one with a named object</summary>
    public LirStackSlot? HomeSlot { get; internal set; }

    internal LirVirtualRegister(int ordinal, QualifiedType type, LirRegisterClass registerClass, GimpleName? sourceName, ValueNumber? valueNumber, MachineRegister fixedRegister)
    {
        if (ordinal < 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal));

        Ordinal = ordinal;
        Type = GimpleTypeHelpers.Normalize(type);
        RegisterClass = registerClass;
        SourceName = sourceName;
        ValueNumber = valueNumber;
        FixedRegister = fixedRegister;
        Name = "%v" + ordinal.ToString(CultureInfo.InvariantCulture);
    }

    public override string ToString() => Name;
}

public sealed class LirStackSlot
{
    public int Ordinal { get; }
    public string Name { get; }
    public QualifiedType Type { get; }
    public int Size { get; }
    public int Alignment { get; }
    public Symbol? Symbol { get; }
    public GimpleTemporaryValue? Temporary { get; }
    public bool IsParameter { get; }
    public StorageClass StorageClass { get; }

    internal LirStackSlot(
        int ordinal,
        string name,
        QualifiedType type,
        int size,
        int alignment,
        Symbol? symbol,
        GimpleTemporaryValue? temporary,
        bool isParameter,
        StorageClass storageClass)
    {
        if (ordinal < 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal));

        Ordinal = ordinal;
        Name = string.IsNullOrWhiteSpace(name)
            ? "slot" + ordinal.ToString(CultureInfo.InvariantCulture)
            : name;
        Type = GimpleTypeHelpers.Normalize(type);
        Size = size < 0 ? 0 : size;
        Alignment = alignment <= 0 ? 1 : alignment;
        Symbol = symbol;
        Temporary = temporary;
        IsParameter = isParameter;
        StorageClass = storageClass;
    }

    public override string ToString()
        => "slot" + Ordinal.ToString(CultureInfo.InvariantCulture);
}

public sealed class LirAddress
{
    public LirAddressKind Kind { get; }
    public QualifiedType ElementType { get; }
    public LirStackSlot? StackSlot { get; }
    public Symbol? Symbol { get; }
    public LirOperand? BaseOperand { get; }
    public LirAddress? BaseAddress { get; }
    public LirOperand? Index { get; }
    public int Scale { get; }
    public int Displacement { get; }
    public FieldSymbol? Field { get; }

    private LirAddress(
        LirAddressKind kind,
        QualifiedType elementType,
        LirStackSlot? stackSlot,
        Symbol? symbol,
        LirOperand? baseOperand,
        LirAddress? baseAddress,
        LirOperand? index,
        int scale,
        int displacement,
        FieldSymbol? field)
    {
        Kind = kind;
        ElementType = GimpleTypeHelpers.Normalize(elementType);
        StackSlot = stackSlot;
        Symbol = symbol;
        BaseOperand = baseOperand;
        BaseAddress = baseAddress;
        Index = index;
        Scale = scale;
        Displacement = displacement;
        Field = field;
    }

    public static LirAddress ForStackSlot(LirStackSlot slot)
    {
        if (slot is null)
            throw new ArgumentNullException(nameof(slot));

        return new LirAddress(LirAddressKind.StackSlot, slot.Type, slot, symbol: null, baseOperand: null, baseAddress: null, index: null, scale: 1, displacement: 0, field: null);
    }

    public static LirAddress ForSymbol(Symbol symbol, QualifiedType type)
    {
        if (symbol is null)
            throw new ArgumentNullException(nameof(symbol));

        return new LirAddress(LirAddressKind.Symbol, type, stackSlot: null, symbol, baseOperand: null, baseAddress: null, index: null, scale: 1, displacement: 0, field: null);
    }

    public static LirAddress Indirect(LirOperand baseOperand, QualifiedType elementType)
    {
        if (baseOperand is null)
            throw new ArgumentNullException(nameof(baseOperand));

        return new LirAddress(LirAddressKind.Indirect, elementType, stackSlot: null, symbol: null, baseOperand, baseAddress: null, index: null, scale: 1, displacement: 0, field: null);
    }

    public static LirAddress Element(LirAddress baseAddress, LirOperand? index, QualifiedType elementType, int scale)
    {
        if (baseAddress is null)
            throw new ArgumentNullException(nameof(baseAddress));

        return new LirAddress(LirAddressKind.Element, elementType, stackSlot: null, symbol: null, baseOperand: null, baseAddress, index, scale <= 0 ? 1 : scale, displacement: 0, field: null);
    }

    public static LirAddress ForField(LirAddress baseAddress, FieldSymbol? field, QualifiedType fieldType, int displacement)
    {
        if (baseAddress is null)
            throw new ArgumentNullException(nameof(baseAddress));

        return new LirAddress(LirAddressKind.Field, fieldType, stackSlot: null, symbol: null, baseOperand: null, baseAddress, index: null, scale: 1, displacement, field);
    }
}

public sealed class LirOperand
{
    public static LirOperand None { get; } = new LirOperand(LirOperandKind.None, new QualifiedType(TypeCatalog.Instance.Void), null, null, null, null, null, null, null);
    public static LirOperand Void { get; } = new LirOperand(LirOperandKind.Void, new QualifiedType(TypeCatalog.Instance.Void), null, null, null, null, null, null, null);

    public LirOperandKind Kind { get; }
    public QualifiedType Type { get; }
    public LirVirtualRegister? Register { get; }
    public object? Immediate { get; }
    public Symbol? Symbol { get; }
    public LirStackSlot? StackSlot { get; }
    public LirAddress? Address { get; }
    public LirBlock? Label { get; }
    public GimpleName? UndefinedName { get; }

    private LirOperand(
        LirOperandKind kind,
        QualifiedType type,
        LirVirtualRegister? register,
        object? immediate,
        Symbol? symbol,
        LirStackSlot? stackSlot,
        LirAddress? address,
        LirBlock? label,
        GimpleName? undefinedName)
    {
        Kind = kind;
        Type = GimpleTypeHelpers.Normalize(type);
        Register = register;
        Immediate = immediate;
        Symbol = symbol;
        StackSlot = stackSlot;
        Address = address;
        Label = label;
        UndefinedName = undefinedName;
    }

    public static LirOperand ForRegister(LirVirtualRegister register)
    {
        if (register is null)
            throw new ArgumentNullException(nameof(register));

        return new LirOperand(LirOperandKind.Register, register.Type, register, null, null, null, null, null, null);
    }

    public static LirOperand ImmediateValue(object? value, QualifiedType type)
        => new LirOperand(LirOperandKind.Immediate, type, null, value, null, null, null, null, null);

    public static LirOperand ForSymbol(Symbol symbol, QualifiedType type)
    {
        if (symbol is null)
            throw new ArgumentNullException(nameof(symbol));

        return new LirOperand(LirOperandKind.Symbol, type, null, null, symbol, null, null, null, null);
    }

    public static LirOperand ForStackSlot(LirStackSlot slot)
    {
        if (slot is null)
            throw new ArgumentNullException(nameof(slot));

        return new LirOperand(LirOperandKind.StackSlot, slot.Type, null, null, null, slot, null, null, null);
    }

    public static LirOperand ForAddress(LirAddress address)
    {
        if (address is null)
            throw new ArgumentNullException(nameof(address));

        return new LirOperand(LirOperandKind.Address, new QualifiedType(TypeCatalog.Instance.PointerTo(address.ElementType)), null, null, null, null, address, null, null);
    }

    /// <summary>Names the object an address locates rather than the address itself</summary>
    public static LirOperand ForObjectAt(LirAddress address)
    {
        if (address is null)
            throw new ArgumentNullException(nameof(address));

        return new LirOperand(LirOperandKind.Address, address.ElementType, null, null, null, null, address, null, null);
    }

    public static LirOperand ForLabel(LirBlock block)
    {
        if (block is null)
            throw new ArgumentNullException(nameof(block));

        return new LirOperand(LirOperandKind.Label, new QualifiedType(TypeCatalog.Instance.Void), null, null, null, null, null, block, null);
    }

    public static LirOperand Undefined(GimpleName? name, QualifiedType type)
        => new LirOperand(LirOperandKind.Undefined, type, null, null, null, null, null, null, name);

    public bool ReferencesSameRegister(LirVirtualRegister register)
        => Kind == LirOperandKind.Register && ReferenceEquals(Register, register);
}

public readonly struct LirParallelCopy
{
    public LirVirtualRegister Destination { get; }
    public LirOperand Source { get; }

    public LirParallelCopy(LirVirtualRegister destination, LirOperand source)
    {
        Destination = destination ?? throw new ArgumentNullException(nameof(destination));
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }
}

public readonly struct LirSwitchCase
{
    public LirOperand Value { get; }
    public LirBlock Target { get; }

    public LirSwitchCase(LirOperand value, LirBlock target)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Target = target ?? throw new ArgumentNullException(nameof(target));
    }
}

/// <summary>A two-armed branch whose only effect is choosing one of two values for one register</summary>
public readonly struct LirSelectDiamond
{
    public LirInstruction Branch { get; }
    public LirBlock TrueArm { get; }
    public LirBlock FalseArm { get; }
    public LirBlock Join { get; }
    public LirVirtualRegister Destination { get; }
    public LirOperand TrueValue { get; }
    public LirOperand FalseValue { get; }

    internal LirSelectDiamond(
        LirInstruction branch,
        LirBlock trueArm,
        LirBlock falseArm,
        LirBlock join,
        LirVirtualRegister destination,
        LirOperand trueValue,
        LirOperand falseValue)
    {
        Branch = branch;
        TrueArm = trueArm;
        FalseArm = falseArm;
        Join = join;
        Destination = destination;
        TrueValue = trueValue;
        FalseValue = falseValue;
    }
}

/// <summary>Finds the conditional expressions a backend can turn into a conditional move</summary>
public static class LirSelect
{
    /// <summary>Maps each head block whose branch only picks between two values for one register</summary>
    public static Dictionary<LirBlock, LirSelectDiamond> FindDiamonds(LirFunction function, TargetInfo target)
    {
        var result = new Dictionary<LirBlock, LirSelectDiamond>();
        if (function is null || target is null || function.Blocks.Length < 4)
            return result;

        var predecessors = CountPredecessors(function);
        foreach (var block in function.Blocks)
        {
            if (block.Instructions.Length == 0)
                continue;
            var branch = block.Instructions[block.Instructions.Length - 1];
            if (branch.Kind != LirInstructionKind.Branch ||
                branch.TrueTarget is not { } trueArm ||
                branch.FalseTarget is not { } falseArm ||
                ReferenceEquals(trueArm, falseArm))
            {
                continue;
            }

            if (!TryReadArm(trueArm, predecessors, out var trueCopy, out var join) ||
                !TryReadArm(falseArm, predecessors, out var falseCopy, out var falseJoin) ||
                !ReferenceEquals(join, falseJoin) ||
                !ReferenceEquals(trueCopy.Destination, falseCopy.Destination))
            {
                continue;
            }

            var destination = trueCopy.Destination;
            if (destination.RegisterClass is not (LirRegisterClass.General or LirRegisterClass.Address))
                continue;
            if (Math.Max(1, target.SizeOf(destination.Type)) > Math.Max(1, target.RegisterSize))
                continue;
            if (!IsSelectableValue(trueCopy.Source, target) || !IsSelectableValue(falseCopy.Source, target))
                continue;

            result.Add(block, new LirSelectDiamond(
                branch, trueArm, falseArm, join, destination, trueCopy.Source, falseCopy.Source));
        }

        return result;
    }

    // An arm may do nothing but move one value into the shared register and fall into the join
    private static bool TryReadArm(
        LirBlock arm,
        Dictionary<LirBlock, int> predecessors,
        out LirParallelCopy copy,
        out LirBlock join)
    {
        copy = default;
        join = null!;
        if (!predecessors.TryGetValue(arm, out var count) || count != 1)
            return false;
        if (arm.Instructions.Length != 2)
            return false;

        var move = arm.Instructions[0];
        if (move.Kind != LirInstructionKind.ParallelCopy || move.ParallelCopies.Length != 1)
            return false;

        var jump = arm.Instructions[1];
        if (jump.Kind != LirInstructionKind.Jump || jump.Target is null)
            return false;

        copy = move.ParallelCopies[0];
        join = jump.Target;
        return true;
    }

    private static bool IsSelectableValue(LirOperand operand, TargetInfo target)
    {
        if (operand.Kind == LirOperandKind.Register && operand.Register is not null)
        {
            return operand.Register.RegisterClass is LirRegisterClass.General or LirRegisterClass.Address &&
                Math.Max(1, target.SizeOf(operand.Register.Type)) <= Math.Max(1, target.RegisterSize);
        }

        return operand.Kind == LirOperandKind.Immediate && operand.Immediate is not string;
    }

    private static Dictionary<LirBlock, int> CountPredecessors(LirFunction function)
    {
        var counts = new Dictionary<LirBlock, int>(function.Blocks.Length);
        foreach (var block in function.Blocks)
            counts[block] = 0;

        foreach (var block in function.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                Count(counts, instruction.Target);
                Count(counts, instruction.TrueTarget);
                Count(counts, instruction.FalseTarget);
                foreach (var switchCase in instruction.SwitchCases)
                    Count(counts, switchCase.Target);
            }
        }

        return counts;
    }

    private static void Count(Dictionary<LirBlock, int> counts, LirBlock? block)
    {
        if (block is not null && counts.TryGetValue(block, out var count))
            counts[block] = count + 1;
    }
}

/// <summary>Divisors and multipliers that an integer operation can be reduced to shifts for</summary>
public static class LirStrengthReduction
{
    /// <summary>Reports a divisor of 2^k or -2^k, with the shift and whether the quotient has to be negated</summary>
    public static bool TryGetPowerOfTwoDivisor(LirInstruction instruction, TargetInfo target, out int shift, out bool negated)
    {
        shift = 0;
        negated = false;
        if (instruction is null || target is null)
            return false;
        if (instruction.Operator is not "/" and not "%")
            return false;
        if (!TryGetOperationShape(instruction, target, out var bits, out var isSigned))
            return false;
        if (!TryGetIntegerConstant(instruction.Operands[1], bits, isSigned, out var divisor))
            return false;

        var magnitude = unchecked((ulong)divisor);
        if (isSigned && divisor < 0)
        {
            magnitude = MaskBits(unchecked(0UL - magnitude), bits);
            // The remainder keeps the sign of the dividend, so only the quotient cares about the divisor's
            negated = instruction.Operator == "/";
        }

        if (magnitude == 0 || (magnitude & (magnitude - 1)) != 0)
            return false;
        shift = CountTrailingZeros(magnitude);
        // A divisor of one is an identity the folder already handles
        return shift >= 1 && shift < bits;
    }

    /// <summary>Reports a multiplier of 2^k, which a shift computes in less latency than a multiply</summary>
    public static bool TryGetPowerOfTwoFactor(LirInstruction instruction, TargetInfo target, out int shift)
    {
        shift = 0;
        if (instruction is null || target is null || instruction.Operator != "*")
            return false;
        if (!TryGetOperationShape(instruction, target, out var bits, out var isSigned))
            return false;
        if (!TryGetIntegerConstant(instruction.Operands[1], bits, isSigned, out var factor))
            return false;

        var magnitude = unchecked((ulong)factor);
        if (magnitude == 0 || (magnitude & (magnitude - 1)) != 0)
            return false;
        shift = CountTrailingZeros(magnitude);
        return shift >= 1 && shift < bits;
    }

    /// <summary>Reports a constant multiplier of any value, normalized to the operation's width</summary>
    public static bool TryGetConstantFactor(LirInstruction instruction, TargetInfo target, out long factor)
    {
        factor = 0;
        if (instruction is null || target is null || instruction.Operator != "*")
            return false;
        if (!TryGetOperationShape(instruction, target, out var bits, out var isSigned))
            return false;
        return TryGetIntegerConstant(instruction.Operands[1], bits, isSigned, out factor);
    }

    /// <summary>The reciprocal a division by a constant multiplies by instead</summary>
    public readonly struct MagicDivisor
    {
        public MagicDivisor(long multiplier, int shift, bool addDividend, bool subtractDividend, bool negateQuotient)
        {
            Multiplier = multiplier;
            Shift = shift;
            AddDividend = addDividend;
            SubtractDividend = subtractDividend;
            NegateQuotient = negateQuotient;
        }

        public long Multiplier { get; }
        public int Shift { get; }
        public bool AddDividend { get; }
        public bool SubtractDividend { get; }
        public bool NegateQuotient { get; }
    }

    /// <summary>Reports the reciprocal that replaces a division by this instruction's constant divisor</summary>
    public static bool TryGetMagicDivisor(LirInstruction instruction, TargetInfo target, out MagicDivisor magic)
    {
        magic = default;
        if (instruction is null || target is null)
            return false;
        if (instruction.Operator is not "/" and not "%")
            return false;
        if (!TryGetOperationShape(instruction, target, out var bits, out var isSigned))
            return false;
        if (bits is not (32 or 64))
            return false;
        return TryGetIntegerConstant(instruction.Operands[1], bits, isSigned, out var divisor) &&
            TryGetMagicDivisor(divisor, bits, isSigned, out magic);
    }

    /// <summary>Reports whether these two neighbours divide the same operands the two ways round</summary>
    public static bool IsDivideRemainderPair(LirInstruction first, LirInstruction second)
    {
        if (first is null || second is null)
            return false;
        if (first.Kind != LirInstructionKind.Binary || second.Kind != LirInstructionKind.Binary)
            return false;
        if (first.Result is null || second.Result is null)
            return false;
        if (!(first.Operator == "/" && second.Operator == "%") &&
            !(first.Operator == "%" && second.Operator == "/"))
            return false;
        if (first.Operands.Length != 2 || second.Operands.Length != 2)
            return false;
        return SameDivisionOperand(first.Operands[0], second.Operands[0]) &&
            SameDivisionOperand(first.Operands[1], second.Operands[1]);
    }

    private static bool SameDivisionOperand(LirOperand left, LirOperand right)
    {
        if (left.Kind != right.Kind)
            return false;
        // The width the operation runs at follows the operand types, so they have to agree as well
        if (!ReferenceEquals(left.Type.Type, right.Type.Type))
            return false;
        return left.Kind switch
        {
            LirOperandKind.Register => ReferenceEquals(left.Register, right.Register),
            LirOperandKind.Immediate => left.Immediate is not string &&
                right.Immediate is not string &&
                ToInt64(left.Immediate) == ToInt64(right.Immediate),
            _ => false,
        };
    }

    /// <summary>Reports the reciprocal that replaces a division by this divisor at this width</summary>
    public static bool TryGetMagicDivisor(long divisor, int bits, bool isSigned, out MagicDivisor magic)
    {
        magic = default;
        if (bits is not (32 or 64))
            return false;

        // A power of two is already a shift, and 0, 1 and -1 are identities the folder handles
        var magnitude = isSigned && divisor < 0
            ? MaskBits(unchecked(0UL - (ulong)divisor), bits)
            : MaskBits(unchecked((ulong)divisor), bits);
        if (magnitude <= 1 || (magnitude & (magnitude - 1)) == 0)
            return false;

        return isSigned
            ? TryGetSignedMagic(magnitude, divisor < 0, bits, out magic)
            : TryGetUnsignedMagic(magnitude, bits, out magic);
    }

    // Granlund and Montgomery's choice of reciprocal, over the operation's width rather than a machine's
    private static bool TryGetSignedMagic(ulong magnitude, bool negative, int bits, out MagicDivisor magic)
    {
        magic = default;
        var signBit = 1UL << (bits - 1);
        var threshold = signBit + (negative ? 1UL : 0UL);
        var complement = threshold - 1 - threshold % magnitude;
        if (complement == 0)
            return false;

        var quotientLow = signBit / complement;
        var remainderLow = signBit - quotientLow * complement;
        var quotientHigh = signBit / magnitude;
        var remainderHigh = signBit - quotientHigh * magnitude;

        var power = bits - 1;
        ulong delta;
        do
        {
            power++;
            quotientLow = MaskBits(quotientLow * 2, bits);
            remainderLow *= 2;
            if (remainderLow >= complement)
            {
                quotientLow++;
                remainderLow -= complement;
            }

            quotientHigh = MaskBits(quotientHigh * 2, bits);
            remainderHigh *= 2;
            if (remainderHigh >= magnitude)
            {
                quotientHigh++;
                remainderHigh -= magnitude;
            }

            delta = magnitude - remainderHigh;
        }
        while (power < 2 * bits && (quotientLow < delta || (quotientLow == delta && remainderLow == 0)));

        var multiplier = MaskBits(quotientHigh + 1, bits);
        var shift = power - bits;
        if (shift < 0 || shift >= bits)
            return false;

        // A reciprocal that does not fit the signed width is off by exactly the dividend
        var overflowed = (multiplier & signBit) != 0;
        magic = new MagicDivisor(
            NormalizeConstant(unchecked((long)(negative ? MaskBits(0UL - multiplier, bits) : multiplier)), bits, isSigned: true),
            shift,
            addDividend: overflowed && !negative,
            subtractDividend: overflowed && negative,
            negateQuotient: false);
        return true;
    }

    private static bool TryGetUnsignedMagic(ulong magnitude, int bits, out MagicDivisor magic)
    {
        magic = default;
        var signBit = 1UL << (bits - 1);
        var widthMask = MaskBits(ulong.MaxValue, bits);
        var complement = widthMask - MaskBits(0UL - magnitude, bits) % magnitude;

        var quotientLow = signBit / complement;
        var remainderLow = signBit - quotientLow * complement;
        var quotientHigh = (signBit - 1) / magnitude;
        var remainderHigh = signBit - 1 - quotientHigh * magnitude;

        var power = bits - 1;
        var addDividend = false;
        ulong delta;
        do
        {
            power++;
            if (remainderLow >= complement - remainderLow)
            {
                quotientLow = MaskBits(quotientLow * 2 + 1, bits);
                remainderLow = remainderLow * 2 - complement;
            }
            else
            {
                quotientLow = MaskBits(quotientLow * 2, bits);
                remainderLow *= 2;
            }

            if (remainderHigh + 1 >= magnitude - remainderHigh)
            {
                addDividend |= quotientHigh >= signBit - 1;
                quotientHigh = MaskBits(quotientHigh * 2 + 1, bits);
                remainderHigh = remainderHigh * 2 + 1 - magnitude;
            }
            else
            {
                addDividend |= quotientHigh >= signBit;
                quotientHigh = MaskBits(quotientHigh * 2, bits);
                remainderHigh = remainderHigh * 2 + 1;
            }

            delta = magnitude - 1 - remainderHigh;
        }
        while (power < 2 * bits && (quotientLow < delta || (quotientLow == delta && remainderLow == 0)));

        var shift = power - bits;
        // The add form recovers the bit the reciprocal lost, and needs a shift left to spend
        if (addDividend && shift < 1)
            return false;
        if (shift < 0 || shift >= bits)
            return false;

        magic = new MagicDivisor(
            NormalizeConstant(unchecked((long)MaskBits(quotientHigh + 1, bits)), bits, isSigned: false),
            shift,
            addDividend,
            subtractDividend: false,
            negateQuotient: false);
        return true;
    }

    /// <summary>Splits a divisor into the shift and odd inverse that divide an exact multiple of it</summary>
    /// <remarks>A pointer difference is always a whole number of elements, so it needs no real divide</remarks>
    public static void GetExactDivisorFactors(int divisor, int bits, out int shift, out long inverse)
    {
        if (divisor <= 0)
            throw new ArgumentOutOfRangeException(nameof(divisor));

        shift = CountTrailingZeros((ulong)divisor);
        var odd = (ulong)divisor >> shift;
        // Newton iteration doubles the number of correct bits, and an odd seed starts with three
        var reciprocal = odd;
        for (var i = 0; i < 6; i++)
            reciprocal = unchecked(reciprocal * (2UL - odd * reciprocal));
        inverse = NormalizeConstant(unchecked((long)reciprocal), bits, isSigned: true);
    }

    /// <summary>Sign or zero extends a constant of the given width to the whole 64 bit domain</summary>
    public static long NormalizeConstant(long value, int bits, bool isSigned)
    {
        if (bits >= 64)
            return value;
        var mask = (1UL << bits) - 1;
        var raw = unchecked((ulong)value) & mask;
        if (isSigned && (raw & (1UL << (bits - 1))) != 0)
            raw |= ~mask;
        return unchecked((long)raw);
    }

    // The operation has to run in one machine register: wider is a software sequence over pairs
    private static bool TryGetOperationShape(LirInstruction instruction, TargetInfo target, out int bits, out bool isSigned)
    {
        bits = 0;
        isSigned = false;
        if (instruction.Kind != LirInstructionKind.Binary || instruction.Operands.Length != 2 || instruction.Result is null)
            return false;

        var type = instruction.Operands[0].Type;
        if (type.Type.Kind is not (TypeKind.Builtin or TypeKind.Enum) || IsFloating(type))
            return false;

        var size = Math.Max(1, target.SizeOf(type));
        if (size < 4 || size > Math.Max(1, target.RegisterSize))
            return false;

        bits = size * 8;
        isSigned = IsSignedInteger(type);
        return true;
    }

    private static bool TryGetIntegerConstant(LirOperand operand, int bits, bool isSigned, out long value)
    {
        value = 0;
        if (operand.Kind != LirOperandKind.Immediate || operand.Immediate is string || IsFloating(operand.Type))
            return false;
        value = NormalizeConstant(ToInt64(operand.Immediate), bits, isSigned);
        return true;
    }

    private static bool IsSignedInteger(QualifiedType type)
    {
        if (type.Type is EnumType)
            return true;
        return type.Type is BuiltinType builtin && builtin.BuiltinKind is BuiltinTypeKind.SignedChar or BuiltinTypeKind.Short
            or BuiltinTypeKind.Int or BuiltinTypeKind.Long or BuiltinTypeKind.LongLong;
    }

    private static bool IsFloating(QualifiedType type)
        => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Float or BuiltinTypeKind.Double or BuiltinTypeKind.LongDouble };

    private static long ToInt64(object? value)
        => value switch
        {
            null => 0,
            bool b => b ? 1 : 0,
            byte b => b,
            sbyte s => s,
            short s => s,
            ushort u => u,
            int i => i,
            uint u => u,
            long l => l,
            ulong u => unchecked((long)u),
            char c => c,
            _ => Convert.ToInt64(value, CultureInfo.InvariantCulture),
        };

    private static ulong MaskBits(ulong value, int bits)
        => bits >= 64 ? value : value & ((1UL << bits) - 1);

    private static int CountTrailingZeros(ulong value)
    {
        var count = 0;
        while ((value & 1) == 0)
        {
            value >>= 1;
            count++;
        }
        return count;
    }
}

/// <summary>A switch lowered to one indexed branch through a table of block labels</summary>
public readonly struct LirJumpTablePlan
{
    /// <summary>The case value the first table entry stands for; the index is the selector minus this</summary>
    public long Minimum { get; }
    /// <summary>One entry per value in the covered range, gaps filled with the default block</summary>
    public ImmutableArray<LirBlock> Targets { get; }

    internal LirJumpTablePlan(long minimum, ImmutableArray<LirBlock> targets)
    {
        Minimum = minimum;
        Targets = targets;
    }
}

public static class LirJumpTable
{
    // A handful of compares predicts better than an indirect branch, so the table has to earn its place
    private const int MinimumCaseCount = 5;
    // Gaps cost a default entry each, so where gcc allows eight times the case count this allows three
    private const int MaximumSpreadRatio = 3;
    // A backstop against a crafted range that passes the ratio test only because the switch is huge
    private const int MaximumEntryCount = 1024;

    /// <summary>Reports whether a table beats a compare chain, and builds it when it does</summary>
    public static bool TryPlan(
        ImmutableArray<LirSwitchCase> cases,
        IReadOnlyList<long> caseValues,
        LirBlock? defaultTarget,
        int selectorSize,
        bool selectorIsSigned,
        out LirJumpTablePlan plan)
    {
        plan = default;
        if (defaultTarget is null || cases.IsDefaultOrEmpty || caseValues is null || caseValues.Count != cases.Length)
            return false;
        if (cases.Length < MinimumCaseCount || selectorSize <= 0 || selectorSize > 8)
            return false;

        var bits = selectorSize * 8;
        var normalized = new long[cases.Length];
        for (var i = 0; i < normalized.Length; i++)
            normalized[i] = Normalize(caseValues[i], bits, selectorIsSigned);

        var minimum = normalized[0];
        var maximum = normalized[0];
        for (var i = 1; i < normalized.Length; i++)
        {
            if (normalized[i] < minimum)
                minimum = normalized[i];
            if (normalized[i] > maximum)
                maximum = normalized[i];
        }

        // Unsigned so that a range spanning the whole selector width fails the cap instead of overflowing
        var spread = unchecked((ulong)maximum - (ulong)minimum);
        if (spread >= MaximumEntryCount)
            return false;

        var entryCount = (int)spread + 1;
        if (entryCount < cases.Length || entryCount > (long)cases.Length * MaximumSpreadRatio)
            return false;

        var targets = new LirBlock[entryCount];
        var assigned = new bool[entryCount];
        for (var i = 0; i < entryCount; i++)
            targets[i] = defaultTarget;
        foreach (var (value, @case) in normalized.Zip(cases))
        {
            // C forbids duplicate case values; should the tree carry one, the chain would take the first
            var index = (int)(value - minimum);
            if (assigned[index])
                continue;
            assigned[index] = true;
            targets[index] = @case.Target;
        }

        plan = new LirJumpTablePlan(minimum, ImmutableArray.Create(targets));
        return true;
    }

    private static long Normalize(long value, int bits, bool signed)
    {
        if (bits >= 64)
            return value;
        var mask = (1UL << bits) - 1;
        var raw = unchecked((ulong)value) & mask;
        if (signed && (raw & (1UL << (bits - 1))) != 0)
            raw |= ~mask;
        return unchecked((long)raw);
    }
}

public sealed class LirInstruction
{
    public int Ordinal { get; }
    public LirInstructionKind Kind { get; }
    public LirVirtualRegister? Result { get; }
    public ImmutableArray<LirOperand> Operands { get; }
    public LirAddress? Address { get; }
    public string Operator { get; }
    /// <summary>Gets the tree code the instruction was selected from</summary>
    public GimpleTreeCode TreeCode { get; }
    public GimpleConversionKind? ConversionKind { get; }
    public FunctionType? CallSignature { get; }
    public ImmutableArray<LirParallelCopy> ParallelCopies { get; }
    public ImmutableArray<LirSwitchCase> SwitchCases { get; }
    public LirBlock? Target { get; }
    public LirBlock? TrueTarget { get; }
    public LirBlock? FalseTarget { get; }
    public GimpleStatement? SourceStatement { get; }
    public GimpleValue? SourceValue { get; }
    public GimpleStatementAnnotations? SourceInstruction { get; }
    public ValueNumber? ValueNumber { get; }
    public bool IsTerminator => Kind is LirInstructionKind.Jump or LirInstructionKind.Branch or LirInstructionKind.Switch or LirInstructionKind.Return or LirInstructionKind.Unreachable ||
        (Kind == LirInstructionKind.InlineAssembly && SourceStatement is GimpleAsmStatement { IsGoto: true });

    internal LirInstruction(
        int ordinal,
        LirInstructionKind kind,
        LirVirtualRegister? result,
        ImmutableArray<LirOperand> operands,
        LirAddress? address,
        string? op,
        GimpleConversionKind? conversionKind,
        FunctionType? callSignature,
        ImmutableArray<LirParallelCopy> parallelCopies,
        ImmutableArray<LirSwitchCase> switchCases,
        LirBlock? target,
        LirBlock? trueTarget,
        LirBlock? falseTarget,
        GimpleStatement? sourceStatement,
        GimpleValue? sourceValue,
        GimpleStatementAnnotations? sourceInstruction,
        ValueNumber? valueNumber,
        GimpleTreeCode treeCode = GimpleTreeCode.None)
    {
        if (ordinal < 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal));

        Ordinal = ordinal;
        Kind = kind;
        Result = result;
        Operands = operands.IsDefault ? ImmutableArray<LirOperand>.Empty : operands;
        Address = address;
        Operator = op ?? string.Empty;
        TreeCode = treeCode;
        ConversionKind = conversionKind;
        CallSignature = callSignature;
        ParallelCopies = parallelCopies.IsDefault ? ImmutableArray<LirParallelCopy>.Empty : parallelCopies;
        SwitchCases = switchCases.IsDefault ? ImmutableArray<LirSwitchCase>.Empty : switchCases;
        Target = target;
        TrueTarget = trueTarget;
        FalseTarget = falseTarget;
        SourceStatement = sourceStatement;
        SourceValue = sourceValue;
        SourceInstruction = sourceInstruction;
        ValueNumber = valueNumber;
    }

    internal LirInstruction WithResult(LirVirtualRegister result)
        => new LirInstruction(Ordinal, Kind, result, Operands, Address, Operator, ConversionKind, CallSignature,
            ParallelCopies, SwitchCases, Target, TrueTarget, FalseTarget, SourceStatement, SourceValue,
            SourceInstruction, ValueNumber, TreeCode);
}

public sealed class LirProblem
{
    public LirProblemKind Kind { get; }
    public ControlFlowBlock? Block { get; }
    public GimpleNode? Node { get; }
    public string Message { get; }

    public LirProblem(LirProblemKind kind, ControlFlowBlock? block, GimpleNode? node, string? message)
    {
        Kind = kind;
        Block = block;
        Node = node;
        Message = message ?? string.Empty;
    }

    public override string ToString() => Message;
}

internal sealed class LirFunctionBuilder
{
    private const int MaxRotatedLoopHeaderInstructions = 8;
    private const int MaxSharedSymbolBases = 4;

    private readonly Dictionary<LirVirtualRegister, LirVirtualRegister> _widenedIndices = new();
    private readonly Dictionary<Symbol, (LirVirtualRegister Register, ControlFlowBlock Block)> _symbolBases = new();
    private readonly HashSet<Symbol> _sharedSymbols = new();
    private readonly HashSet<object> _sharedAddressKeys = new();

    private readonly GimpleFunctionAnnotations _function;
    private readonly ControlFlowFunction _controlFlowFunction;
    private readonly LirOptions _options;
    private readonly TargetInfo _target;
    private readonly List<LirVirtualRegister> _registers = new();
    private readonly List<LirStackSlot> _stackSlots = new();
    private readonly List<LirBlock> _blocks = new();
    private readonly List<LirProblem> _problems = new();
    private readonly Dictionary<GimpleName, LirVirtualRegister> _registersByName = new();
    private readonly Dictionary<ControlFlowBlock, LirBlock> _blocksByControlFlowBlock = new();
    private readonly Dictionary<Symbol, LirStackSlot> _stackSlotsBySymbol = new();
    private readonly Dictionary<GimpleTemporaryValue, LirStackSlot> _stackSlotsByTemporary = new();
    private readonly Dictionary<Symbol, GimpleVariableDeclaration> _localDeclarationsBySymbol = new();
    private readonly Dictionary<Symbol, GimpleVariable> _promotedSymbols = new();
    private readonly Dictionary<GimpleTemporaryValue, GimpleVariable> _promotedTemporaries = new();
    private readonly Dictionary<(ControlFlowBlock Source, ControlFlowBlock Target), LirBlock> _edgeSplitBlocks = new();
    private readonly Dictionary<(ControlFlowBlock Source, ControlFlowBlock Target), List<LirParallelCopy>> _edgeCopies = new();
    private readonly Dictionary<LirBlock, List<LirInstruction>> _instructions = new();
    private readonly HashSet<GimpleName> _usedGimpleNames = new();
    private readonly Dictionary<GimpleName, int> _gimpleUseCounts = new();
    private readonly HashSet<ControlFlowBlock> _reachableControlFlowBlocks = new();
    private readonly Dictionary<ControlFlowBlock, GimpleBlockAnnotations> _gimpleBlocksByControlFlowBlock = new();

    private int _nextInstructionOrdinal;
    private GimpleStatementAnnotations? _currentInstruction;
    private LirBlock? _currentBlock;
    private ControlFlowBlock? _currentGimpleBlock;

    private LirFunctionBuilder(GimpleFunctionAnnotations function, LirOptions options, TargetInfo target)
    {
        _function = function ?? throw new ArgumentNullException(nameof(function));
        _controlFlowFunction = function.ControlFlowFunction;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _target = target;
    }

    public static LoweredLirFunction Lower(GimpleFunctionAnnotations function, LirOptions options, TargetInfo? target)
    {
        var builder = new LirFunctionBuilder(function, options, target ?? TargetInfo.Default);
        return builder.Lower();
    }

    private LoweredLirFunction Lower()
    {
        IndexPromotedVariables();
        IndexReachableControlFlowBlocks();
        ScanLocalDeclarations();
        IndexUsedGimpleNames();
        CreateVirtualRegistersForGimpleNames();
        CreateBaseBlocks();
        CreateParameterStackSlots();
        CreateEdgeSplitBlocksForPhis();
        TranslateBaseBlocks();
        TranslateEdgeSplitBlocks();
        SealBlocks();
        CoalesceCopies();
        LayoutBlocks();

        var entry = _blocksByControlFlowBlock.TryGetValue(_controlFlowFunction.Entry, out var entryBlock)
            ? entryBlock
            : _blocks.First();

        var function = new LirFunction(
            _function,
            entry,
            _registers.ToImmutableArray(),
            _stackSlots.ToImmutableArray(),
            _blocks.ToImmutableArray(),
            _problems.ToImmutableArray());

        return new LoweredLirFunction(function, _problems.ToImmutableArray());
    }

    private void ScanLocalDeclarations()
    {
        var referencedSymbols = new HashSet<Symbol>();
        foreach (var block in _function.Blocks)
        {
            if (_reachableControlFlowBlocks.Count != 0 && !_reachableControlFlowBlocks.Contains(block.ControlFlowBlock))
                continue;

            foreach (var instruction in block.Statements)
            {
                if (instruction.Statement is not GimpleDeclarationStatement)
                    SymbolCollector.Collect(instruction.Statement, referencedSymbols);
            }
        }

        foreach (var block in _function.Blocks)
        {
            if (_reachableControlFlowBlocks.Count != 0 && !_reachableControlFlowBlocks.Contains(block.ControlFlowBlock))
                continue;

            foreach (var instruction in block.Statements)
            {
                if (instruction.Statement is not GimpleDeclarationStatement declarationStatement ||
                    declarationStatement.Symbol is null ||
                    !referencedSymbols.Contains(declarationStatement.Symbol))
                {
                    continue;
                }

                _localDeclarationsBySymbol[declarationStatement.Symbol] = declarationStatement.Declaration;
                if (!_promotedSymbols.ContainsKey(declarationStatement.Symbol) && IsStackAllocatedLocal(declarationStatement.Declaration))
                    _ = GetOrCreateStackSlot(declarationStatement.Declaration);
            }
        }
    }

    private void IndexPromotedVariables()
    {
        foreach (var variable in _function.Variables)
        {
            if (variable.Kind == GimpleVariableKind.Symbol && variable.Symbol is not null && !_promotedSymbols.ContainsKey(variable.Symbol))
                _promotedSymbols.Add(variable.Symbol, variable);
            else if (variable.Kind == GimpleVariableKind.Temporary && variable.Temporary is not null && !_promotedTemporaries.ContainsKey(variable.Temporary))
                _promotedTemporaries.Add(variable.Temporary, variable);
        }
    }

    private void IndexReachableControlFlowBlocks()
    {
        _reachableControlFlowBlocks.Clear();
        _gimpleBlocksByControlFlowBlock.Clear();
        if (_function.Blocks.Length == 0)
            return;

        foreach (var block in _function.Blocks)
        {
            if (!_gimpleBlocksByControlFlowBlock.ContainsKey(block.ControlFlowBlock))
                _gimpleBlocksByControlFlowBlock.Add(block.ControlFlowBlock, block);
        }

        if (!_gimpleBlocksByControlFlowBlock.TryGetValue(_controlFlowFunction.Entry, out var entry))
            entry = _function.Blocks[0];

        var stack = new Stack<GimpleBlockAnnotations>();
        _reachableControlFlowBlocks.Add(entry.ControlFlowBlock);
        stack.Push(entry);

        while (stack.Count != 0)
        {
            var block = stack.Pop();
            foreach (var successor in EnumerateOptimizedSuccessors(block))
            {
                if (successor.IsExit)
                    continue;

                if (!_reachableControlFlowBlocks.Add(successor))
                    continue;

                if (_gimpleBlocksByControlFlowBlock.TryGetValue(successor, out var successorBlock))
                    stack.Push(successorBlock);
            }
        }
    }

    private IEnumerable<ControlFlowBlock> EnumerateOptimizedSuccessors(GimpleBlockAnnotations block)
    {
        var terminator = block.Statements.Length == 0 ? null : block.Statements[^1].Statement;
        switch (terminator)
        {
            case GimpleGotoStatement gotoStatement:
                if (_controlFlowFunction.TryGetBlock(gotoStatement.Target, out var gotoTarget) && gotoTarget is not null)
                    yield return gotoTarget;
                yield break;

            case GimpleCondStatement conditional:
                if (_controlFlowFunction.TryGetBlock(conditional.WhenTrue, out var trueTarget) && trueTarget is not null)
                    yield return trueTarget;
                if (_controlFlowFunction.TryGetBlock(conditional.WhenFalse, out var falseTarget) && falseTarget is not null && !ReferenceEquals(falseTarget, trueTarget))
                    yield return falseTarget;
                yield break;

            case GimpleSwitchStatement switchStatement:
                var seen = new HashSet<ControlFlowBlock>();
                foreach (var switchCase in switchStatement.Cases)
                {
                    if (_controlFlowFunction.TryGetBlock(switchCase.Target, out var caseTarget) && caseTarget is not null && seen.Add(caseTarget))
                        yield return caseTarget;
                }

                if (_controlFlowFunction.TryGetBlock(switchStatement.DefaultLabel, out var defaultTarget) && defaultTarget is not null && seen.Add(defaultTarget))
                    yield return defaultTarget;
                yield break;

            case GimpleReturnStatement:
                yield break;
        }

        foreach (var edge in block.ControlFlowBlock.Successors)
        {
            if (!edge.Target.IsExit)
                yield return edge.Target;
        }
    }

    private void CreateVirtualRegistersForGimpleNames()
    {
        foreach (var definition in _function.Definitions)
        {
            if (definition.Name.Variable.Kind == GimpleVariableKind.Memory || definition.Name.IsUndefined)
                continue;

            if (_registersByName.ContainsKey(definition.Name))
                continue;

            if (!IsGimpleNameUsed(definition.Name))
                continue;

            _function.ValueNumbering.TryGetValueNumber(definition.Name, out var valueNumber);
            var register = NewVirtualRegister(definition.Name.Type, definition.Name, valueNumber);
            _registersByName.Add(definition.Name, register);
        }
    }

    private void IndexUsedGimpleNames()
    {
        foreach (var block in _function.Blocks)
        {
            if (_reachableControlFlowBlocks.Count != 0 && !_reachableControlFlowBlocks.Contains(block.ControlFlowBlock))
                continue;

            foreach (var phi in block.Phis)
            {
                foreach (var operand in phi.Operands)
                {
                    if (!operand.Value.IsUndefined && operand.Value.Variable.Kind != GimpleVariableKind.Memory)
                        RecordGimpleUse(operand.Value);
                }
            }

            foreach (var instruction in block.Statements)
            {
                foreach (var use in instruction.Uses)
                {
                    if (use.Kind != GimpleUseKind.Memory && !use.Name.IsUndefined && use.Name.Variable.Kind != GimpleVariableKind.Memory)
                        RecordGimpleUse(use.Name);
                }
            }
        }
    }

    private void RecordGimpleUse(GimpleName name)
    {
        _usedGimpleNames.Add(name);
        _gimpleUseCounts.TryGetValue(name, out var count);
        _gimpleUseCounts[name] = count + 1;
    }

    private bool IsGimpleNameUsed(GimpleName name)
        => _usedGimpleNames.Contains(name);

    private void CreateBaseBlocks()
    {
        foreach (var controlFlowBlock in _controlFlowFunction.RealBlocks)
        {
            if (!_options.PreserveUnreachableBlocks)
            {
                if (!controlFlowBlock.IsReachable)
                    continue;

                if (_reachableControlFlowBlocks.Count != 0 && !_reachableControlFlowBlocks.Contains(controlFlowBlock))
                    continue;
            }

            var block = NewBlock(GetBlockName(controlFlowBlock), controlFlowBlock, isEdgeSplit: false);
            _blocksByControlFlowBlock.Add(controlFlowBlock, block);
        }
    }

    private void CreateParameterStackSlots()
    {
        var functionType = _function.Symbol?.FunctionType;
        if (functionType is null)
            return;

        foreach (var parameter in functionType.Parameters)
        {
            if (_promotedSymbols.ContainsKey(parameter))
                continue;

            _ = GetOrCreateStackSlot(parameter, parameter.Type, isParameter: true, storageClass: StorageClass.Auto);
        }
    }

    private void CreateEdgeSplitBlocksForPhis()
    {
        foreach (var block in _function.Blocks)
        {
            if (block.Phis.Length == 0)
                continue;

            foreach (var phi in block.Phis)
            {
                if (phi.Result.Variable.Kind == GimpleVariableKind.Memory || !IsGimpleNameUsed(phi.Result))
                    continue;

                if (!_blocksByControlFlowBlock.ContainsKey(phi.Block))
                    continue;

                var destination = GetRegister(phi.Result);
                var seenPredecessors = new HashSet<ControlFlowBlock>();
                foreach (var operand in phi.Operands)
                {
                    if (!seenPredecessors.Add(operand.Predecessor) ||
                        !_blocksByControlFlowBlock.ContainsKey(operand.Predecessor))
                        continue;

                    var source = GetOperand(operand.Value);
                    if (source.ReferencesSameRegister(destination))
                        continue;

                    var key = (operand.Predecessor, phi.Block);
                    if (!_edgeCopies.TryGetValue(key, out var copies))
                    {
                        copies = new List<LirParallelCopy>();
                        _edgeCopies.Add(key, copies);
                    }

                    copies.Add(new LirParallelCopy(destination, source));
                }
            }
        }

        foreach (var pair in _edgeCopies)
        {
            var source = pair.Key.Source;
            var target = pair.Key.Target;
            if (!RequiresEdgeSplitForCopies(source, target))
                continue;

            var name = "edge_" + source.Ordinal.ToString(CultureInfo.InvariantCulture) + "_to_" + target.Ordinal.ToString(CultureInfo.InvariantCulture);
            var split = NewBlock(name, sourceBlock: source, isEdgeSplit: true);
            _edgeSplitBlocks.Add(pair.Key, split);
        }
    }

    private bool RequiresEdgeSplitForCopies(ControlFlowBlock source, ControlFlowBlock target)
    {
        if (!_edgeCopies.ContainsKey((source, target)))
            return false;

        return !CanInlineEdgeCopies(source, target);
    }

    private bool CanInlineEdgeCopies(ControlFlowBlock source, ControlFlowBlock target)
    {
        if (!_gimpleBlocksByControlFlowBlock.TryGetValue(source, out var sourceBlock))
            return source.UniqueSuccessors.Length == 1 && ReferenceEquals(source.UniqueSuccessors[0], target);

        var successorCount = 0;
        foreach (var successor in EnumerateOptimizedSuccessors(sourceBlock))
        {
            if (successor.IsExit)
                continue;

            if (!ReferenceEquals(successor, target))
                return false;

            successorCount++;
        }

        if (successorCount != 1)
            return false;

        var terminator = sourceBlock.Statements.Length == 0 ? null : sourceBlock.Statements[^1].Statement;
        return terminator is null or GimpleGotoStatement or GimpleCondStatement;
    }

    private void TranslateBaseBlocks()
    {
        CountSharedSymbols();
        _symbolBases.Clear();
        foreach (var gimpleBlock in _function.Blocks)
        {
            if (!_blocksByControlFlowBlock.TryGetValue(gimpleBlock.ControlFlowBlock, out var block))
                continue;

            _currentBlock = block;
            _currentGimpleBlock = gimpleBlock.ControlFlowBlock;
            _widenedIndices.Clear();
            CountSharedAddressKeys(gimpleBlock);

            if (ReferenceEquals(gimpleBlock.ControlFlowBlock, _controlFlowFunction.Entry))
                EmitEntryParameters(block);

            foreach (var instruction in gimpleBlock.Statements)
                TranslateInstruction(block, instruction);

            EnsureTerminator(block, gimpleBlock.ControlFlowBlock);
            _currentBlock = null;
            _currentGimpleBlock = null;
        }
    }

    private void EmitEntryParameters(LirBlock block)
    {
        var functionType = _function.Symbol?.FunctionType;
        if (functionType is null)
            return;

        for (var i = 0; i < functionType.Parameters.Length; i++)
        {
            var parameter = functionType.Parameters[i];
            var definition = _function.Definitions.FirstOrDefault(d =>
                d.Kind == GimpleDefinitionKind.Entry &&
                ReferenceEquals(d.Parameter, parameter) &&
                d.Name.Variable.Kind != GimpleVariableKind.Memory);

            if (definition is not null && _registersByName.TryGetValue(definition.Name, out var register))
            {
                _function.ValueNumbering.TryGetValueNumber(definition, out var valueNumber);
                Emit(block, LirInstructionKind.Parameter, register, ImmutableArray<LirOperand>.Empty, address: null, op: parameter.Name, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: null, sourceValue: null, sourceInstruction: null, valueNumber: valueNumber);
                continue;
            }

            if (_stackSlotsBySymbol.TryGetValue(parameter, out var slot))
            {
                var temporary = NewVirtualRegister(parameter.Type, sourceName: null, valueNumber: null);

                // An aggregate lands in the slot the body reads, with no home of its own in between
                if (GimpleTypes.IsAggregate(parameter.Type))
                {
                    temporary.HomeSlot = slot;
                    Emit(block, LirInstructionKind.Parameter, temporary, ImmutableArray<LirOperand>.Empty, address: null, op: parameter.Name, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: null, sourceValue: null, sourceInstruction: null, valueNumber: null);
                    continue;
                }

                Emit(block, LirInstructionKind.Parameter, temporary, ImmutableArray<LirOperand>.Empty, address: null, op: parameter.Name, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: null, sourceValue: null, sourceInstruction: null, valueNumber: null);
                Emit(block, LirInstructionKind.Store, null, ImmutableArray.Create(LirOperand.ForRegister(temporary)), LirAddress.ForStackSlot(slot), op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: null, sourceValue: null, sourceInstruction: null, valueNumber: null);
            }
        }
    }

    private void TranslateEdgeSplitBlocks()
    {
        foreach (var pair in _edgeSplitBlocks)
        {
            var key = pair.Key;
            var block = pair.Value;
            var copies = _edgeCopies.TryGetValue(key, out var copyList)
                ? copyList.ToImmutableArray()
                : ImmutableArray<LirParallelCopy>.Empty;

            if (copies.Length != 0)
                Emit(block, LirInstructionKind.ParallelCopy, null, ImmutableArray<LirOperand>.Empty, address: null, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: copies, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: null, sourceValue: null, sourceInstruction: null, valueNumber: null);

            Emit(block, LirInstructionKind.Jump, null, ImmutableArray<LirOperand>.Empty, address: null, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: GetBaseBlock(key.Target), trueTarget: null, falseTarget: null, sourceStatement: null, sourceValue: null, sourceInstruction: null, valueNumber: null);
        }
    }

    private void TranslateInstruction(LirBlock block, GimpleStatementAnnotations instruction)
    {
        _currentInstruction = instruction;

        switch (instruction.Statement)
        {
            case GimpleDeclarationStatement declaration:
                if (_options.KeepNops)
                    EmitNop(block, declaration);
                break;

            case GimpleAssignStatement assign:
                TranslateAssign(block, instruction, assign);
                break;

            case GimpleCallStatement call:
                TranslateCall(block, instruction, call);
                break;

            case GimpleGotoStatement gotoStatement:
                EmitJump(block, instruction.Block, gotoStatement.Target, gotoStatement);
                break;

            case GimpleCondStatement conditional:
                TranslateCond(block, instruction, conditional);
                break;

            case GimpleSwitchStatement switchStatement:
                TranslateSwitch(block, instruction, switchStatement);
                break;

            case GimpleReturnStatement returnStatement:
                TranslateReturn(block, instruction, returnStatement);
                break;

            case GimpleAsmStatement asmStatement:
                TranslateAsm(block, instruction, asmStatement);
                break;

            case GimpleNopStatement nop:
                if (_options.KeepNops)
                    EmitNop(block, nop);
                break;

            default:
                _problems.Add(new LirProblem(LirProblemKind.UnsupportedNode, instruction.Block, instruction.Statement, "Unsupported GIMPLE statement in LIR lowering: " + instruction.Statement.Kind));
                if (_options.KeepNops)
                    EmitNop(block, instruction.Statement);
                break;
        }

        _currentInstruction = null;
    }

    private void TranslateAssign(LirBlock block, GimpleStatementAnnotations instruction, GimpleAssignStatement assign)
    {
        var definition = GetPrimaryDefinition(instruction);
        var operandStart = definition is null ? 1 : 0;

        if (assign.IsConstructor)
        {
            TranslateConstructor(block, instruction, assign, definition);
            return;
        }

        if (definition is not null && !IsGimpleNameUsed(definition.Name))
            return;

        var destination = definition is null ? null : GetRegister(definition.Name);
        if (definition is null &&
            assign.RhsClass == GimpleRhsClass.Single &&
            assign.Operands.Length != 0 &&
            TryEmitAggregateInPlace(block, instruction, operandStart, assign.Operands[0], out var inPlace))
        {
            var inPlaceAddress = TryGetExpression(instruction, 0, out var lhsExpression)
                ? EmitAddress(block, lhsExpression)
                : EmitAddress(block, assign.Lhs);
            Emit(block, LirInstructionKind.Store, null, ImmutableArray.Create(inPlace), inPlaceAddress, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: assign, sourceValue: assign.Op1, sourceInstruction: instruction, valueNumber: null);
            return;
        }

        var value = EmitRhs(block, instruction, assign, operandStart, destination);

        if (IsVoid(assign.Lhs.Type) || value.Kind == LirOperandKind.Void || IsVoid(value.Type))
            return;

        if (definition is not null && destination is not null)
        {
            if (value.ReferencesSameRegister(destination))
                return;

            _function.ValueNumbering.TryGetValueNumber(definition, out var definitionValueNumber);
            Emit(block, LirInstructionKind.Copy, destination, ImmutableArray.Create(value), address: null, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: assign, sourceValue: assign.Op1, sourceInstruction: instruction, valueNumber: definitionValueNumber);
            return;
        }

        var address = TryGetExpression(instruction, 0, out var addressExpression)
            ? EmitAddress(block, addressExpression)
            : EmitAddress(block, assign.Lhs);

        Emit(block, LirInstructionKind.Store, null, ImmutableArray.Create(value), address, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: assign, sourceValue: assign.Op1, sourceInstruction: instruction, valueNumber: null);
    }

    /// <summary>Selects the instruction that computes an assignment right-hand side</summary>
    private LirOperand EmitRhs(
        LirBlock block,
        GimpleStatementAnnotations instruction,
        GimpleAssignStatement assign,
        int operandStart,
        LirVirtualRegister? destination)
    {
        switch (assign.RhsClass)
        {
            case GimpleRhsClass.Single:
                return EmitOperand(block, instruction, assign, operandStart, destination);

            case GimpleRhsClass.Unary:
                return EmitUnaryRhs(block, instruction, assign, operandStart, destination);

            case GimpleRhsClass.Binary:
                return EmitBinaryRhs(block, instruction, assign, operandStart, destination);

            default:
                _problems.Add(new LirProblem(
                    LirProblemKind.UnsupportedNode,
                    instruction.Block,
                    assign,
                    "Unsupported GIMPLE assignment subcode in LIR lowering: " + GimpleOperators.Name(assign.Subcode)));
                return LirOperand.Undefined(null, assign.Lhs.Type);
        }
    }

    private LirOperand EmitOperand(
        LirBlock block,
        GimpleStatementAnnotations instruction,
        GimpleAssignStatement assign,
        int index,
        LirVirtualRegister? destination)
    {
        return TryGetExpression(instruction, index, out var expression)
            ? EmitValue(block, expression, destination: destination)
            : EmitValue(block, assign.Operands[0], expression: null, destination: destination);
    }

    private LirOperand EmitUnaryRhs(
        LirBlock block,
        GimpleStatementAnnotations instruction,
        GimpleAssignStatement assign,
        int operandStart,
        LirVirtualRegister? destination)
    {
        var operandExpression = TryGetExpression(instruction, operandStart, out var rewritten) ? rewritten : null;
        var operand = operandExpression is null
            ? EmitValue(block, assign.Operands[0])
            : EmitValue(block, operandExpression);

        if (IsVoid(assign.Lhs.Type))
            return LirOperand.Void;

        var result = GetResultRegister(assign.Lhs.Type, GetDefinitionValueNumber(instruction), destination);

        if (GimpleOperators.IsConversion(assign.Subcode))
        {
            var kind = assign.Subcode == GimpleTreeCode.ViewConvertExpr
                ? LirInstructionKind.Cast
                : LirInstructionKind.Convert;

            Emit(block, kind, result, ImmutableArray.Create(operand), address: null, op: GimpleOperators.Name(assign.Subcode),
                conversionKind: ConversionKindOf(assign.Subcode), callSignature: null, parallelCopies: default, switchCases: default,
                target: null, trueTarget: null, falseTarget: null, sourceStatement: assign, sourceValue: assign.Op1,
                sourceInstruction: instruction, valueNumber: GetDefinitionValueNumber(instruction), treeCode: assign.Subcode);
            return LirOperand.ForRegister(result);
        }

        Emit(block, LirInstructionKind.Unary, result, ImmutableArray.Create(operand), address: null, op: GimpleOperators.Spelling(assign.Subcode),
            conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null,
            falseTarget: null, sourceStatement: assign, sourceValue: assign.Op1, sourceInstruction: instruction,
            valueNumber: GetDefinitionValueNumber(instruction), treeCode: assign.Subcode);
        return LirOperand.ForRegister(result);
    }

    private LirOperand EmitBinaryRhs(
        LirBlock block,
        GimpleStatementAnnotations instruction,
        GimpleAssignStatement assign,
        int operandStart,
        LirVirtualRegister? destination)
    {
        var leftExpression = TryGetExpression(instruction, operandStart, out var rewrittenLeft) ? rewrittenLeft : null;
        var rightExpression = TryGetExpression(instruction, operandStart + 1, out var rewrittenRight) ? rewrittenRight : null;
        var left = leftExpression is null ? EmitValue(block, assign.Operands[0]) : EmitValue(block, leftExpression);
        var right = rightExpression is null ? EmitValue(block, assign.Operands[1]) : EmitValue(block, rightExpression);

        if (IsVoid(assign.Lhs.Type))
            return LirOperand.Void;

        var result = GetResultRegister(assign.Lhs.Type, GetDefinitionValueNumber(instruction), destination);
        Emit(block, LirInstructionKind.Binary, result, ImmutableArray.Create(left, right), address: null,
            op: GimpleOperators.Spelling(assign.Subcode), conversionKind: null, callSignature: null, parallelCopies: default,
            switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: assign,
            sourceValue: assign.Op1, sourceInstruction: instruction, valueNumber: GetDefinitionValueNumber(instruction),
            treeCode: assign.Subcode);
        return LirOperand.ForRegister(result);
    }

    private void TranslateConstructor(
        LirBlock block,
        GimpleStatementAnnotations instruction,
        GimpleAssignStatement assign,
        GimpleDefinition? definition)
    {
        if (definition is not null)
        {
            var destination = GetRegister(definition.Name);
            _function.ValueNumbering.TryGetValueNumber(definition, out var valueNumber);
            Emit(block, LirInstructionKind.Zero, destination, ImmutableArray<LirOperand>.Empty, address: null, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: assign, sourceValue: null, sourceInstruction: instruction, valueNumber: valueNumber);
            return;
        }

        var address = TryGetExpression(instruction, 0, out var addressExpression)
            ? EmitAddress(block, addressExpression)
            : EmitAddress(block, assign.Lhs);
        var size = _target.SizeOf(assign.Lhs.Type);
        Emit(block, LirInstructionKind.ZeroMemory, null, ImmutableArray.Create(LirOperand.ImmediateValue(size, TypeCatalog.Instance.Builtin(BuiltinTypeKind.Int))), address, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: assign, sourceValue: null, sourceInstruction: instruction, valueNumber: null);
    }

    private void TranslateCall(LirBlock block, GimpleStatementAnnotations instruction, GimpleCallStatement call)
    {
        var definition = GetPrimaryDefinition(instruction);
        var hasLhsAddress = call.Lhs is not null && definition is null;
        var functionIndex = hasLhsAddress ? 1 : 0;

        var destination = definition is not null && IsGimpleNameUsed(definition.Name)
            ? GetRegister(definition.Name)
            : null;

        var value = EmitCall(block, instruction, call, functionIndex, destination);

        if (call.Lhs is null || value.Kind == LirOperandKind.Void || IsVoid(call.Type))
            return;

        if (definition is not null)
        {
            if (destination is null || value.ReferencesSameRegister(destination))
                return;

            _function.ValueNumbering.TryGetValueNumber(definition, out var valueNumber);
            Emit(block, LirInstructionKind.Copy, destination, ImmutableArray.Create(value), address: null, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: call, sourceValue: null, sourceInstruction: instruction, valueNumber: valueNumber);
            return;
        }

        var address = TryGetExpression(instruction, 0, out var addressExpression)
            ? EmitAddress(block, addressExpression)
            : EmitAddress(block, call.Lhs);

        Emit(block, LirInstructionKind.Store, null, ImmutableArray.Create(value), address, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: call, sourceValue: null, sourceInstruction: instruction, valueNumber: null);
    }

    private void TranslateCond(LirBlock block, GimpleStatementAnnotations instruction, GimpleCondStatement conditional)
    {
        var trueTarget = ResolveTarget(instruction.Block, conditional.WhenTrue, conditional);
        var falseTarget = ResolveTarget(instruction.Block, conditional.WhenFalse, conditional);

        if (ReferenceEquals(trueTarget, falseTarget))
        {
            EmitJump(block, instruction.Block, conditional.WhenTrue, conditional);
            return;
        }

        var leftExpression = TryGetExpression(instruction, 0, out var rewrittenLeft) ? rewrittenLeft : null;
        var rightExpression = TryGetExpression(instruction, 1, out var rewrittenRight) ? rewrittenRight : null;
        var left = leftExpression is null ? EmitValue(block, conditional.Lhs) : EmitValue(block, leftExpression);

        // A test against zero stays a single-operand branch so the target keeps its truth test
        if (conditional.Code == GimpleTreeCode.NeExpr && IsZeroConstant(conditional.Rhs))
        {
            if (TryGetImmediateTruth(left, out var truth))
            {
                EmitJump(block, instruction.Block, truth ? conditional.WhenTrue : conditional.WhenFalse, conditional);
                return;
            }

            Emit(block, LirInstructionKind.Branch, null, ImmutableArray.Create(left), address: null, op: string.Empty, conversionKind: null,
                callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: trueTarget,
                falseTarget: falseTarget, sourceStatement: conditional, sourceValue: conditional.Lhs, sourceInstruction: instruction,
                valueNumber: null, treeCode: conditional.Code);
            return;
        }

        var right = rightExpression is null ? EmitValue(block, conditional.Rhs) : EmitValue(block, rightExpression);
        Emit(block, LirInstructionKind.Branch, null, ImmutableArray.Create(left, right), address: null,
            op: GimpleOperators.Spelling(conditional.Code), conversionKind: null, callSignature: null, parallelCopies: default,
            switchCases: default, target: null, trueTarget: trueTarget, falseTarget: falseTarget, sourceStatement: conditional,
            sourceValue: conditional.Lhs, sourceInstruction: instruction, valueNumber: null, treeCode: conditional.Code);
    }

    private static bool IsZeroConstant(GimpleValue value)
        => value is GimpleConstantValue constant && IsZeroObject(constant.Value);

    private static bool IsZeroObject(object? value)
    {
        switch (value)
        {
            case null: return true;
            case sbyte number: return number == 0;
            case byte number: return number == 0;
            case short number: return number == 0;
            case ushort number: return number == 0;
            case int number: return number == 0;
            case uint number: return number == 0;
            case long number: return number == 0;
            case ulong number: return number == 0;
            case bool flag: return !flag;
            case char character: return character == '\0';
            default: return false;
        }
    }

    private void TranslateSwitch(LirBlock block, GimpleStatementAnnotations instruction, GimpleSwitchStatement switchStatement)
    {
        var value = TryGetExpression(instruction, 0, out var expression)
            ? EmitValue(block, expression)
            : EmitValue(block, switchStatement.Expression);
        var cases = ImmutableArray.CreateBuilder<LirSwitchCase>();

        foreach (var switchCase in switchStatement.Cases)
        {
            var target = ResolveTarget(instruction.Block, switchCase.Target, switchStatement);
            cases.Add(new LirSwitchCase(LirOperand.ImmediateValue(switchCase.Value.Value, switchCase.Value.Type), target));
        }

        var defaultTarget = ResolveTarget(instruction.Block, switchStatement.DefaultLabel, switchStatement);
        Emit(block, LirInstructionKind.Switch, null, ImmutableArray.Create(value), address: null, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: cases.ToImmutable(), target: defaultTarget, trueTarget: null, falseTarget: null, sourceStatement: switchStatement, sourceValue: switchStatement.Expression, sourceInstruction: instruction, valueNumber: null);
    }

    private void TranslateReturn(LirBlock block, GimpleStatementAnnotations instruction, GimpleReturnStatement returnStatement)
    {
        var operands = ImmutableArray<LirOperand>.Empty;
        if (returnStatement.Expression is not null)
        {
            var value = TryEmitAggregateInPlace(block, instruction, 0, returnStatement.Expression, out var inPlace)
                ? inPlace
                : TryGetExpression(instruction, 0, out var expression)
                    ? EmitValue(block, expression)
                    : EmitValue(block, returnStatement.Expression);
            operands = ImmutableArray.Create(value);
        }

        Emit(block, LirInstructionKind.Return, null, operands, address: null, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: returnStatement, sourceValue: returnStatement.Expression, sourceInstruction: instruction, valueNumber: null);
    }

    private void TranslateAsm(LirBlock block, GimpleStatementAnnotations instruction, GimpleAsmStatement asmStatement)
    {
        var operands = ImmutableArray.CreateBuilder<LirOperand>();
        var copies = ImmutableArray.CreateBuilder<LirParallelCopy>();
        var postStores = new List<(GimplePlace Target, LirVirtualRegister Register, GimpleOperandInfo? AddressExpression)>();
        var definitions = instruction.Definitions
            .Where(static definition => definition.Name.Variable.Kind != GimpleVariableKind.Memory)
            .ToArray();
        var definitionIndex = 0;
        var expressionIndex = 0;

        foreach (var output in asmStatement.Outputs)
        {
            GimpleOperandInfo? initialExpression = null;
            if (output.IsReadWrite && output.Value is not null)
            {
                if (TryGetExpression(instruction, expressionIndex, out var expression))
                    initialExpression = expression;
                expressionIndex++;
            }

            GimpleOperandInfo? addressExpression = null;
            var storage = output.Target is null
                ? InlineAsmOperandStorage.Register
                : InlineAsmConstraints.PreferredStorage(output.Constraint, output.Target.Type);
            var hasAddressExpression = output.Target is not null &&
                (storage == InlineAsmOperandStorage.Memory || !TryGetVariableDefinitionTarget(output.Target, definitions, definitionIndex));
            if (hasAddressExpression)
            {
                if (TryGetExpression(instruction, expressionIndex, out var expression))
                    addressExpression = expression;
                expressionIndex++;
            }

            if (output.Target is null)
                continue;

            if (storage == InlineAsmOperandStorage.Memory)
            {
                operands.Add(LirOperand.ForAddress(addressExpression is null ? EmitAddress(block, output.Target) : EmitAddress(block, addressExpression)));
                continue;
            }

            LirVirtualRegister destination;
            if (definitionIndex < definitions.Length && definitions[definitionIndex].Target is not null && ReferenceSamePlace(definitions[definitionIndex].Target!, output.Target))
            {
                destination = GetRegister(definitions[definitionIndex].Name);
                definitionIndex++;
            }
            else
            {
                destination = NewVirtualRegister(output.Target.Type, sourceName: null, valueNumber: null);
                if (!asmStatement.IsGoto)
                    postStores.Add((output.Target, destination, addressExpression));
            }

            if (output.IsReadWrite && output.Value is not null)
            {
                var source = initialExpression is null ? EmitValue(block, output.Value) : EmitValue(block, initialExpression);
                if (!source.ReferencesSameRegister(destination))
                    Emit(block, LirInstructionKind.Copy, destination, ImmutableArray.Create(source), address: null, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: asmStatement, sourceValue: output.Value, sourceInstruction: instruction, valueNumber: null);
                copies.Add(new LirParallelCopy(destination, LirOperand.ForRegister(destination)));
            }
            else
            {
                copies.Add(new LirParallelCopy(destination, LirOperand.Void));
            }
        }

        foreach (var input in asmStatement.Inputs)
        {
            GimpleOperandInfo? expression = null;
            if (TryGetExpression(instruction, expressionIndex, out var rewritten))
                expression = rewritten;
            expressionIndex++;

            if (input.Value is null)
            {
                operands.Add(LirOperand.Void);
                continue;
            }

            var storage = InlineAsmConstraints.PreferredStorage(input.Constraint, input.Value.Type);
            if (storage == InlineAsmOperandStorage.Memory && input.Value is GimplePlace place)
            {
                operands.Add(LirOperand.ForAddress(expression is null ? EmitAddress(block, place) : EmitAddress(block, expression)));
                continue;
            }

            var value = expression is null ? EmitValue(block, input.Value) : EmitValue(block, expression);
            if (storage == InlineAsmOperandStorage.Register)
                value = MaterializeAsmRegisterInput(block, asmStatement, input.Value, value, instruction);
            operands.Add(value);
        }

        foreach (var label in asmStatement.GotoLabels)
            operands.Add(LirOperand.ForLabel(ResolveTarget(instruction.Block, label, asmStatement)));

        Emit(block, LirInstructionKind.InlineAssembly, null, operands.ToImmutable(), address: null, op: asmStatement.Text, conversionKind: null, callSignature: null, parallelCopies: copies.ToImmutable(), switchCases: default, target: asmStatement.IsGoto ? FindAsmGotoFallthrough(instruction.Block, asmStatement) : null, trueTarget: null, falseTarget: null, sourceStatement: asmStatement, sourceValue: null, sourceInstruction: instruction, valueNumber: null);

        foreach (var store in postStores)
        {
            var address = store.AddressExpression is null ? EmitAddress(block, store.Target) : EmitAddress(block, store.AddressExpression);
            Emit(block, LirInstructionKind.Store, null, ImmutableArray.Create(LirOperand.ForRegister(store.Register)), address, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: asmStatement, sourceValue: store.Target, sourceInstruction: instruction, valueNumber: null);
        }
    }

    private LirBlock? FindAsmGotoFallthrough(ControlFlowBlock source, GimpleAsmStatement statement)
    {
        foreach (var edge in source.Successors)
        {
            if (edge.Kind == ControlFlowEdgeKind.FallThrough && !edge.Target.IsExit)
                return GetBaseBlock(edge.Target);
        }

        foreach (var edge in source.Successors)
        {
            if (!edge.Target.IsExit)
                return GetBaseBlock(edge.Target);
        }

        return null;
    }

    private LirOperand MaterializeAsmRegisterInput(LirBlock block, GimpleAsmStatement statement, GimpleValue sourceValue, LirOperand value, GimpleStatementAnnotations instruction)
    {
        if (value.Kind == LirOperandKind.Register)
            return value;

        var register = NewVirtualRegister(value.Type, sourceName: null, valueNumber: null);
        Emit(block, LirInstructionKind.Copy, register, ImmutableArray.Create(value), address: null, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: statement, sourceValue: sourceValue, sourceInstruction: instruction, valueNumber: null);
        return LirOperand.ForRegister(register);
    }

    private static bool TryGetVariableDefinitionTarget(GimplePlace target, GimpleDefinition[] definitions, int startIndex)
    {
        for (var i = startIndex; i < definitions.Length; i++)
        {
            if (definitions[i].Target is not null && ReferenceSamePlace(definitions[i].Target!, target))
                return true;
        }
        return false;
    }

    private static bool ReferenceSamePlace(GimplePlace left, GimplePlace right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is GimpleSymbolValue leftSymbol && right is GimpleSymbolValue rightSymbol)
            return ReferenceEquals(leftSymbol.Symbol, rightSymbol.Symbol);
        if (left is GimpleTemporaryValue leftTemporary && right is GimpleTemporaryValue rightTemporary)
            return ReferenceEquals(leftTemporary, rightTemporary);
        return false;
    }

    private LirOperand EmitValue(LirBlock block, GimpleOperandInfo expression, LirVirtualRegister? destination = null)
    {
        if (expression.Name is not null && !expression.IsAddress)
            return GetOperand(expression.Name);

        if (expression.IsAddress)
        {
            var address = EmitAddress(block, expression);
            return EmitAddressValue(block, address, expression.Original, expression, destination);
        }

        return EmitValue(block, expression.Original, expression, destination);
    }

    private LirOperand EmitValue(LirBlock block, GimpleValue value)
        => EmitValue(block, value, expression: null, destination: null);

    /// <summary>Names an aggregate that already sits in memory by its address instead of copying it into a home</summary>
    private bool TryEmitAggregateInPlace(
        LirBlock block,
        GimpleStatementAnnotations instruction,
        int index,
        GimpleValue fallback,
        [NotNullWhen(true)] out LirOperand? operand)
    {
        operand = default;
        var expression = TryGetExpression(instruction, index, out var rewritten) ? rewritten : null;
        var value = expression?.Original ?? fallback;

        if (!GimpleTypes.IsAggregate(value.Type))
            return false;
        // A name already stands for storage the back end can address, and an address is a value of its own
        if (expression is not null && (expression.Name is not null || expression.IsAddress))
            return false;
        if (value is not (GimpleIndirectExpression or GimpleElementAccessExpression or
            GimpleMemberAccessExpression or GimpleSymbolValue or GimpleTemporaryValue))
        {
            return false;
        }

        operand = LirOperand.ForObjectAt(EmitAddress(block, value, expression));
        return true;
    }

    private LirOperand EmitValue(LirBlock block, GimpleValue value, GimpleOperandInfo? expression, LirVirtualRegister? destination = null)
    {
        switch (value)
        {
            case GimpleName name:
                return GetOperand(name);

            case GimpleSymbolValue symbolValue:
                if (expression?.Name is not null)
                    return GetOperand(expression.Name);
                if (symbolValue.Symbol is FunctionSymbol)
                    return LirOperand.ForSymbol(symbolValue.Symbol, new QualifiedType(TypeCatalog.Instance.PointerTo(symbolValue.Type)));
                return EmitLoad(block, EmitAddress(block, symbolValue), symbolValue.Type, value, expression, destination);

            case GimpleTemporaryValue temporary:
                if (expression?.Name is not null)
                    return GetOperand(expression.Name);
                return EmitLoad(block, EmitAddress(block, temporary), temporary.Type, value, expression, destination);

            case GimpleConstantValue constant:
                return LirOperand.ImmediateValue(constant.Value, constant.Type);

            case GimpleAddressOfExpression addressOf:
                return EmitAddressOf(block, addressOf, expression, destination);

            case GimpleIndirectExpression indirect:
                return EmitLoad(block, EmitAddress(block, indirect, expression), indirect.Type, indirect, expression, destination);

            case GimpleElementAccessExpression elementAccess:
                return EmitLoad(block, EmitAddress(block, elementAccess, expression), elementAccess.Type, elementAccess, expression, destination);

            case GimpleMemberAccessExpression memberAccess:
                return EmitLoad(block, EmitAddress(block, memberAccess, expression), memberAccess.Type, memberAccess, expression, destination);

            case GimpleErrorValue:
                return LirOperand.Undefined(null, value.Type);

            default:
                _problems.Add(new LirProblem(LirProblemKind.UnsupportedNode, _currentInstruction?.Block, value, "Unsupported GIMPLE value in LIR lowering: " + value.Kind));
                return LirOperand.Undefined(null, value.Type);
        }
    }

    // A declaration address stays a symbol operand so direct references keep their relocation
    private LirOperand EmitAddressOf(LirBlock block, GimpleAddressOfExpression addressOf, GimpleOperandInfo? expression, LirVirtualRegister? destination)
    {
        if (addressOf.Target is GimpleSymbolValue { Symbol: FunctionSymbol function } functionValue)
        {
            if (function.IntrinsicKind is RuntimeIntrinsicKind.BuiltinVaStart or RuntimeIntrinsicKind.BuiltinVaArg)
            {
                _problems.Add(new LirProblem(
                    LirProblemKind.UnsupportedNode,
                    _currentInstruction?.Block,
                    addressOf,
                    "Cannot take address of compiler intrinsic. "));
                return LirOperand.Undefined(null, addressOf.Type);
            }

            return LirOperand.ForSymbol(functionValue.Symbol, addressOf.Type);
        }

        return EmitAddressValue(block, EmitAddress(block, addressOf.Target, GetChild(expression, 0)), addressOf.Type, addressOf, expression, destination);
    }

    /// <summary>Maps a conversion tree code onto the semantic conversion the pipeline records</summary>
    private static GimpleConversionKind ConversionKindOf(GimpleTreeCode code)
    {
        switch (code)
        {
            case GimpleTreeCode.FloatExpr:
            case GimpleTreeCode.FixTruncExpr:
            case GimpleTreeCode.ConvertExpr:
                return GimpleConversionKind.Explicit;
            case GimpleTreeCode.ViewConvertExpr:
                return GimpleConversionKind.Explicit;
            case GimpleTreeCode.NopExpr:
                return GimpleConversionKind.Implicit;
            default:
                return GimpleConversionKind.Identity;
        }
    }

    private ValueNumber? GetDefinitionValueNumber(GimpleStatementAnnotations instruction)
    {
        var definition = GetPrimaryDefinition(instruction);
        if (definition is null)
            return null;

        _function.ValueNumbering.TryGetValueNumber(definition, out var valueNumber);
        return valueNumber;
    }

    private LirOperand EmitCall(
        LirBlock block,
        GimpleStatementAnnotations instruction,
        GimpleCallStatement call,
        int functionIndex,
        LirVirtualRegister? destination)
    {
        if (TryGetRuntimeIntrinsic(call.Function, out var intrinsic))
        {
            switch (intrinsic)
            {
                case RuntimeIntrinsicKind.BuiltinVaStart:
                    return EmitVaStart(block, instruction, call, functionIndex, destination);
                case RuntimeIntrinsicKind.BuiltinVaArg:
                    return EmitVaArg(block, instruction, call, functionIndex, destination);
            }
        }

        var operands = ImmutableArray.CreateBuilder<LirOperand>(call.Arguments.Length + 1);
        operands.Add(EmitCallOperand(block, instruction, functionIndex, call.Function));
        for (var i = 0; i < call.Arguments.Length; i++)
            operands.Add(EmitCallOperand(block, instruction, functionIndex + 1 + i, call.Arguments[i]));

        // A destination the renamer left in memory still needs the result materialized before it is stored
        var storesToMemory = call.Lhs is not null && GetPrimaryDefinition(instruction) is null;
        var needsResult = destination is not null || storesToMemory || RequiresMaterializedCallResult(call.Type);
        LirVirtualRegister? result = IsVoid(call.Type) || !needsResult
            ? null
            : destination ?? NewVirtualRegister(call.Type, sourceName: null, valueNumber: null);

        Emit(block, LirInstructionKind.Call, result, operands.ToImmutable(), address: null, op: string.Empty, conversionKind: null,
            callSignature: call.FunctionType, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null,
            sourceStatement: call, sourceValue: null, sourceInstruction: instruction, valueNumber: GetDefinitionValueNumber(instruction),
            treeCode: GimpleTreeCode.CallExpr);
        // Reaching past the call would make the allocator carry the address in a callee-saved register,
        // and the prologue, every epilogue and the wider frame cost more than recomputing it does
        _symbolBases.Clear();
        return result is null ? LirOperand.Void : LirOperand.ForRegister(result);
    }

    private LirOperand EmitCallOperand(LirBlock block, GimpleStatementAnnotations instruction, int index, GimpleValue original)
        => TryGetExpression(instruction, index, out var expression)
            ? EmitValue(block, expression)
            : EmitValue(block, original);

    private bool RequiresMaterializedCallResult(QualifiedType type)
    {
        if (IsVoid(type))
            return false;

        return type.Type is RVVectorType || CAbi.RequiresHiddenReturnBuffer(_target, type);
    }

    private LirOperand EmitVaStart(
        LirBlock block,
        GimpleStatementAnnotations instruction,
        GimpleCallStatement call,
        int functionIndex,
        LirVirtualRegister? destination)
    {
        if (call.Arguments.Length > 1)
        {
            _problems.Add(new LirProblem(
                LirProblemKind.UnsupportedNode,
                instruction.Block,
                call,
                "__builtin_va_start expects zero or one explicit argument after macro expansion."));
        }

        var operands = ImmutableArray.CreateBuilder<LirOperand>(call.Arguments.Length);
        for (var i = 0; i < call.Arguments.Length; i++)
            operands.Add(EmitCallOperand(block, instruction, functionIndex + 1 + i, call.Arguments[i]));

        LirVirtualRegister? result = IsVoid(call.Type)
            ? null
            : destination ?? NewVirtualRegister(call.Type, sourceName: null, valueNumber: null);

        Emit(block, LirInstructionKind.VaStart, result, operands.ToImmutable(), address: null, op: string.Empty, conversionKind: null,
            callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null,
            sourceStatement: call, sourceValue: null, sourceInstruction: instruction, valueNumber: GetDefinitionValueNumber(instruction));
        return result is null ? LirOperand.Void : LirOperand.ForRegister(result);
    }

    private LirOperand EmitVaArg(
        LirBlock block,
        GimpleStatementAnnotations instruction,
        GimpleCallStatement call,
        int functionIndex,
        LirVirtualRegister? destination)
    {
        if (call.Arguments.Length != 4)
        {
            _problems.Add(new LirProblem(
                LirProblemKind.UnsupportedNode,
                instruction.Block,
                call,
                "__builtin_va_arg expects a va_list pointer, kind, size, and alignment."));
        }

        var operands = ImmutableArray.CreateBuilder<LirOperand>(call.Arguments.Length);
        for (var i = 0; i < call.Arguments.Length; i++)
            operands.Add(EmitCallOperand(block, instruction, functionIndex + 1 + i, call.Arguments[i]));

        LirVirtualRegister? result = IsVoid(call.Type)
            ? null
            : destination ?? NewVirtualRegister(call.Type, sourceName: null, valueNumber: null);

        Emit(block, LirInstructionKind.VaArg, result, operands.ToImmutable(), address: null, op: string.Empty, conversionKind: null,
            callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null,
            sourceStatement: call, sourceValue: null, sourceInstruction: instruction, valueNumber: GetDefinitionValueNumber(instruction));
        return result is null ? LirOperand.Void : LirOperand.ForRegister(result);
    }

    private static bool TryGetRuntimeIntrinsic(GimpleValue value, out RuntimeIntrinsicKind intrinsic)
    {
        while (true)
        {
            switch (value)
            {
                case GimpleAddressOfExpression address:
                    value = address.Target;
                    continue;
                case GimpleSymbolValue { Symbol: FunctionSymbol function } when function.IntrinsicKind != RuntimeIntrinsicKind.None:
                    intrinsic = function.IntrinsicKind;
                    return true;
                default:
                    intrinsic = RuntimeIntrinsicKind.None;
                    return false;
            }
        }
    }

    private LirOperand EmitLoad(LirBlock block, LirAddress address, QualifiedType type, GimpleValue sourceValue, GimpleOperandInfo? expression, LirVirtualRegister? destination = null)
    {
        var result = GetResultRegister(type, GetValueNumber(expression), destination);
        Emit(block, LirInstructionKind.Load, result, ImmutableArray<LirOperand>.Empty, address, op: string.Empty, conversionKind: null,
            callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null,
            sourceStatement: _currentInstruction?.Statement, sourceValue: sourceValue, sourceInstruction: _currentInstruction, valueNumber: GetValueNumber(expression));
        return LirOperand.ForRegister(result);
    }

    private LirOperand EmitAddressValue(LirBlock block, LirAddress address, GimpleValue sourceValue, GimpleOperandInfo? expression, LirVirtualRegister? destination = null)
        => EmitAddressValue(block, address, new QualifiedType(TypeCatalog.Instance.PointerTo(address.ElementType)), sourceValue, expression, destination);
    private LirOperand EmitAddressValue(LirBlock block, LirAddress address, QualifiedType resultType, GimpleValue sourceValue, GimpleOperandInfo? expression, LirVirtualRegister? destination = null)
    {
        var result = GetResultRegister(resultType, expression, destination);
        Emit(block, LirInstructionKind.AddressOf, result, ImmutableArray<LirOperand>.Empty, address, op: string.Empty, conversionKind: null,
            callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null,
            sourceStatement: _currentInstruction?.Statement, sourceValue: sourceValue, sourceInstruction: _currentInstruction, valueNumber: GetValueNumber(expression));
        return LirOperand.ForRegister(result);
    }

    private LirAddress EmitAddress(LirBlock block, GimpleOperandInfo expression)
    {
        if (expression.Name is not null && expression.IsAddress)
            return EmitPromotedAddress(block, expression);

        return EmitAddress(block, expression.Original, expression);
    }

    private LirAddress EmitAddress(LirBlock block, GimplePlace place)
        => EmitAddress(block, place, expression: null);

    private LirAddress EmitAddress(LirBlock block, GimplePlace place, GimpleOperandInfo? expression)
        => EmitAddress(block, (GimpleValue)place, expression);

    private LirAddress EmitAddress(LirBlock block, GimpleValue value, GimpleOperandInfo? expression)
    {
        if (expression?.Name is not null && expression.IsAddress)
            return EmitPromotedAddress(block, expression);

        switch (value)
        {
            case GimpleSymbolValue symbolValue:
                if (_stackSlotsBySymbol.TryGetValue(symbolValue.Symbol, out var slot))
                    return LirAddress.ForStackSlot(slot);

                if (_localDeclarationsBySymbol.TryGetValue(symbolValue.Symbol, out var declaration) && IsStackAllocatedLocal(declaration))
                    return LirAddress.ForStackSlot(GetOrCreateStackSlot(declaration));

                return ShareSymbolBase(block, symbolValue);

            case GimpleTemporaryValue temporary:
                return LirAddress.ForStackSlot(GetOrCreateStackSlot(temporary));

            case GimpleIndirectExpression indirect:
                {
                    var pointer = GetChild(expression, 0) is { } child
                        ? EmitValue(block, child)
                        : EmitValue(block, indirect.Address);
                    return LirAddress.Indirect(pointer, indirect.Type);
                }

            case GimpleElementAccessExpression elementAccess:
                return EmitElementAddress(block, elementAccess, expression);

            case GimpleMemberAccessExpression memberAccess:
                return EmitMemberAddress(block, memberAccess, expression);

            default:
                _problems.Add(new LirProblem(LirProblemKind.InvalidAddress, _currentInstruction?.Block, value, $"Cannot form an LIR address for GIMPLE value: {value.Kind}"));
                return LirAddress.Indirect(LirOperand.Undefined(null, value.Type), value.Type);
        }
    }

    private LirAddress EmitElementAddress(LirBlock block, GimpleElementAccessExpression elementAccess, GimpleOperandInfo? expression)
    {
        var baseExpression = GetChild(expression, 0);
        LirAddress baseAddress;
        if (elementAccess.Expression.Type.Type is PointerType)
        {
            var pointer = EmitPointerElementBaseValue(block, elementAccess.Expression, baseExpression);
            baseAddress = LirAddress.Indirect(pointer, elementAccess.Type);
        }
        else if (baseExpression is not null && baseExpression.IsAddress)
        {
            baseAddress = EmitAddress(block, baseExpression);
        }
        else if (elementAccess.Expression is GimplePlace basePlace)
        {
            baseAddress = EmitAddress(block, basePlace, baseExpression);
        }
        else
        {
            baseAddress = LirAddress.Indirect(baseExpression is null ? EmitValue(block, elementAccess.Expression) : EmitValue(block, baseExpression), elementAccess.Type);
        }

        LirOperand? index = null;
        GimpleOperandInfo? indexExpression = null;
        if (elementAccess.Index is not null)
        {
            indexExpression = GetChild(expression, 1);
            index = indexExpression is null ? EmitValue(block, elementAccess.Index) : EmitValue(block, indexExpression);
        }

        var scale = _target.SizeOf(elementAccess.Type);
        if (AddressIndexKey(indexExpression?.Name) is { } indexKey && _sharedAddressKeys.Contains(indexKey))
            index = WidenAddressIndex(block, index);
        return LirAddress.Element(baseAddress, index, elementAccess.Type, scale);
    }
    // Widening here instead of in the emitter gives the value a register and shares it across a block
    private LirOperand? WidenAddressIndex(LirBlock block, LirOperand? index)
    {
        if (index is null || index.Kind != LirOperandKind.Register || index.Register is null)
            return index;

        // Only a scaled-index addressing mode reads the index as a register of its own
        if (!TargetRegisterInfo.IsX86(_target))
            return index;

        var register = index.Register;
        if (register.RegisterClass is not (LirRegisterClass.General or LirRegisterClass.Address))
            return index;
        if (register.Type.Type.Kind is not (TypeKind.Builtin or TypeKind.Enum))
            return index;

        var pointerSize = Math.Max(1, _target.PointerSize);
        if (Math.Max(1, _target.SizeOf(register.Type)) >= pointerSize)
            return index;
        if (!TryGetPointerSizedIntegerType(register.Type, pointerSize, out var widenedType))
            return index;

        if (_widenedIndices.TryGetValue(register, out var cached))
            return LirOperand.ForRegister(cached);

        var result = NewVirtualRegister(widenedType, sourceName: null, valueNumber: null);
        Emit(block, LirInstructionKind.Convert, result, ImmutableArray.Create(index), address: null,
            op: GimpleOperators.Name(GimpleTreeCode.NopExpr), conversionKind: GimpleConversionKind.Implicit,
            callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null,
            falseTarget: null, sourceStatement: null, sourceValue: null, sourceInstruction: null, valueNumber: null);
        _widenedIndices.Add(register, result);
        return LirOperand.ForRegister(result);
    }

    /// <summary>
    /// Computes a symbol's address into a register once and reuses it wherever that computation
    /// dominates, which is what a target without a symbolic memory operand otherwise repeats at
    /// every access. The address is invariant, so the only question is reach, not validity.
    /// </summary>
    private LirAddress ShareSymbolBase(LirBlock block, GimpleSymbolValue symbolValue)
    {
        var symbol = symbolValue.Symbol;
        if (_currentGimpleBlock is null || !TargetRegisterInfo.MaterializesSymbolAddresses(_target))
            return LirAddress.ForSymbol(symbol, symbolValue.Type);

        if (_symbolBases.TryGetValue(symbol, out var cached) && cached.Block.Dominates(_currentGimpleBlock))
            return LirAddress.Indirect(LirOperand.ForRegister(cached.Register), symbolValue.Type);

        // One address has to serve several accesses, or the emitter scratch is the cheaper way to get it
        if (!_sharedSymbols.Contains(symbol))
            return LirAddress.ForSymbol(symbol, symbolValue.Type);

        var pointerType = new QualifiedType(TypeCatalog.Instance.PointerTo(symbolValue.Type));
        var result = NewVirtualRegister(pointerType, sourceName: null, valueNumber: null);
        Emit(block, LirInstructionKind.AddressOf, result, ImmutableArray<LirOperand>.Empty,
            LirAddress.ForSymbol(symbol, symbolValue.Type), op: string.Empty, conversionKind: null,
            callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null,
            falseTarget: null, sourceStatement: null, sourceValue: null, sourceInstruction: null, valueNumber: null);
        _symbolBases[symbol] = (result, _currentGimpleBlock);
        return LirAddress.Indirect(LirOperand.ForRegister(result), symbolValue.Type);
    }

    /// <summary>
    /// Picks the symbols whose address earns a register. A single access already costs what the
    /// emitter scratch costs, and reuse only carries as far as a call, so the promise of a saving
    /// is a block that reads the symbol twice; reaching further is what the dominance test adds on
    /// top. Holding more than a handful of addresses gives back in spills what the sharing saved.
    /// </summary>
    private void CountSharedSymbols()
    {
        _sharedSymbols.Clear();
        if (!TargetRegisterInfo.MaterializesSymbolAddresses(_target))
            return;

        var repeats = new Dictionary<Symbol, int>();
        var counts = new Dictionary<object, int>();
        foreach (var gimpleBlock in _function.Blocks)
        {
            counts.Clear();
            foreach (var instruction in gimpleBlock.Statements)
                CountStatementAddressKeys(instruction.Statement, counts);

            foreach (var pair in counts)
            {
                if (pair.Value <= 1 || pair.Key is not Symbol symbol)
                    continue;
                repeats.TryGetValue(symbol, out var best);
                repeats[symbol] = Math.Max(best, pair.Value);
            }
        }

        foreach (var pair in repeats.OrderByDescending(static pair => pair.Value))
        {
            if (_sharedSymbols.Count == MaxSharedSymbolBases)
                break;
            if (!IsFrameResident(pair.Key))
                _sharedSymbols.Add(pair.Key);
        }
    }

    // A symbol the frame holds is reached through the stack pointer, not through an address of its own
    private bool IsFrameResident(Symbol symbol)
        => _stackSlotsBySymbol.ContainsKey(symbol) ||
            (_localDeclarationsBySymbol.TryGetValue(symbol, out var declaration) && IsStackAllocatedLocal(declaration));

    // One widened value has to replace several, or the emitter temporary is the cheaper way to get it
    private void CountSharedAddressKeys(GimpleBlockAnnotations gimpleBlock)
    {
        _sharedAddressKeys.Clear();
        var counts = new Dictionary<object, int>();
        foreach (var instruction in gimpleBlock.Statements)
            CountStatementAddressKeys(instruction.Statement, counts);

        foreach (var pair in counts)
        {
            if (pair.Value > 1)
                _sharedAddressKeys.Add(pair.Key);
        }
    }

    private static void CountStatementAddressKeys(GimpleStatement statement, Dictionary<object, int> counts)
    {
        switch (statement)
        {
            case GimpleAssignStatement assign:
                CountValueAddressKeys(assign.Lhs, counts);
                foreach (var operand in assign.Operands)
                    CountValueAddressKeys(operand, counts);
                break;
            case GimpleCallStatement call:
                CountValueAddressKeys(call.Lhs, counts);
                foreach (var argument in call.Arguments)
                    CountValueAddressKeys(argument, counts);
                break;
            case GimpleCondStatement branch:
                CountValueAddressKeys(branch.Lhs, counts);
                CountValueAddressKeys(branch.Rhs, counts);
                break;
            case GimpleReturnStatement @return:
                CountValueAddressKeys(@return.Expression, counts);
                break;
            case GimpleSwitchStatement dispatch:
                CountValueAddressKeys(dispatch.Expression, counts);
                break;
        }
    }

    // An unrecognized node only hides accesses, which leaves the sharing out and changes nothing else
    private static void CountValueAddressKeys(GimpleValue? value, Dictionary<object, int> counts)
    {
        switch (value)
        {
            case null:
                return;
            case GimpleSymbolValue symbolValue:
                counts.TryGetValue(symbolValue.Symbol, out var symbolCount);
                counts[symbolValue.Symbol] = symbolCount + 1;
                return;
            case GimpleElementAccessExpression element:
                CountValueAddressKeys(element.Expression, counts);
                CountValueAddressKeys(element.Index, counts);
                if (element.Index is GimpleName indexName && AddressIndexKey(indexName) is { } key)
                {
                    counts.TryGetValue(key, out var count);
                    counts[key] = count + 1;
                }
                return;
            case GimpleMemberAccessExpression member:
                CountValueAddressKeys(member.Expression, counts);
                return;
            case GimpleIndirectExpression indirect:
                CountValueAddressKeys(indirect.Address, counts);
                return;
            case GimpleAddressOfExpression address:
                CountValueAddressKeys(address.Target, counts);
                return;
            case GimpleUnaryExpression unary:
                CountValueAddressKeys(unary.Operand, counts);
                return;
            case GimpleBinaryExpression binary:
                CountValueAddressKeys(binary.Left, counts);
                CountValueAddressKeys(binary.Right, counts);
                return;
            case GimpleConversionExpression conversion:
                CountValueAddressKeys(conversion.Operand, counts);
                return;
            case GimpleCastExpression cast:
                CountValueAddressKeys(cast.Operand, counts);
                return;
        }
    }

    private static object? AddressIndexKey(GimpleName? index)
        => index is null ? null : (index.Variable, index.Version);

    private bool TryGetPointerSizedIntegerType(QualifiedType source, int pointerSize, out QualifiedType type)
    {
        var unsigned = IsUnsignedIntegerType(source);
        foreach (var candidate in new[]
        {
            unsigned ? TypeCatalog.Instance.UnsignedLong : TypeCatalog.Instance.Long,
            unsigned ? TypeCatalog.Instance.UnsignedLongLong : TypeCatalog.Instance.LongLong,
        })
        {
            type = new QualifiedType(candidate);
            if (_target.SizeOf(type) == pointerSize)
                return true;
        }

        type = default;
        return false;
    }

    private static bool IsUnsignedIntegerType(QualifiedType type)
        => type.Type is BuiltinType builtin &&
           builtin.BuiltinKind is BuiltinTypeKind.Bool or BuiltinTypeKind.UnsignedChar or BuiltinTypeKind.UnsignedShort
               or BuiltinTypeKind.UnsignedInt or BuiltinTypeKind.UnsignedLong or BuiltinTypeKind.UnsignedLongLong;

    private LirOperand EmitPointerElementBaseValue(LirBlock block, GimpleValue originalBase, GimpleOperandInfo? rewrittenBase)
    {
        if (rewrittenBase?.Name is not null)
            return GetOperand(rewrittenBase.Name);

        return rewrittenBase is null
            ? EmitValue(block, originalBase)
            : EmitValue(block, originalBase, rewrittenBase);
    }
    private LirAddress EmitMemberAddress(LirBlock block, GimpleMemberAccessExpression memberAccess, GimpleOperandInfo? expression)
    {
        var baseExpression = GetChild(expression, 0);
        LirAddress baseAddress;
        if (memberAccess.ThroughPointer)
        {
            var pointer = baseExpression is null ? EmitValue(block, memberAccess.Expression) : EmitValue(block, baseExpression);
            baseAddress = LirAddress.Indirect(pointer, memberAccess.Expression.Type);
        }
        else if (baseExpression is not null && baseExpression.IsAddress)
        {
            baseAddress = EmitAddress(block, baseExpression);
        }
        else if (memberAccess.Expression is GimplePlace basePlace)
        {
            baseAddress = EmitAddress(block, basePlace, baseExpression);
        }
        else
        {
            baseAddress = LirAddress.Indirect(baseExpression is null ? EmitValue(block, memberAccess.Expression) : EmitValue(block, baseExpression), memberAccess.Expression.Type);
        }

        return LirAddress.ForField(baseAddress, memberAccess.Field, memberAccess.Type, GetFieldOffset(memberAccess.Field));
    }

    private LirAddress EmitPromotedAddress(LirBlock block, GimpleOperandInfo expression)
    {
        var name = expression.Name!;
        if (name.Variable.Kind != GimpleVariableKind.Temporary)
        {
            _problems.Add(new LirProblem(
            LirProblemKind.PromotedAddressTakenValue,
            _currentInstruction?.Block,
            expression.Original,
            $"Address was requested for promoted GIMPLE value '{name}'."));
        }

        LirStackSlot slot;
        if (name.Variable.Symbol is not null)
            slot = GetOrCreateStackSlot(name.Variable.Symbol, name.Type, isParameter: false, StorageClass.Auto);
        else if (name.Variable.Temporary is not null)
            slot = GetOrCreateStackSlot(name.Variable.Temporary);
        else
            slot = GetOrCreateAnonymousStackSlot(name.Type, name.Variable.Name);

        Emit(block, LirInstructionKind.Store, null, ImmutableArray.Create(GetOperand(name)), LirAddress.ForStackSlot(slot), op: string.Empty, conversionKind: null,
            callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: _currentInstruction?.Statement,
            sourceValue: expression.Original, sourceInstruction: _currentInstruction, valueNumber: null);
        return LirAddress.ForStackSlot(slot);
    }

    private void EnsureTerminator(LirBlock block, ControlFlowBlock source)
    {
        if (HasTerminator(block))
            return;

        if (source.Successors.Length == 0)
        {
            Emit(block, LirInstructionKind.Return, null, ImmutableArray<LirOperand>.Empty, address: null, op: string.Empty, conversionKind: null, callSignature: null,
                parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: null, sourceValue: null,
                sourceInstruction: null, valueNumber: null);
            return;
        }

        var nonExitSuccessors = source.Successors.Where(static edge => !edge.Target.IsExit).ToArray();
        if (nonExitSuccessors.Length == 0)
        {
            Emit(block, LirInstructionKind.Return, null, ImmutableArray<LirOperand>.Empty, address: null, op: string.Empty, conversionKind: null, callSignature: null,
                parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: null, sourceValue: null,
                sourceInstruction: null, valueNumber: null);
            return;
        }

        EmitJumpToControlFlowTarget(block, source, nonExitSuccessors[0].Target, statement: null, sourceInstruction: null);
    }

    private void EmitJump(LirBlock block, ControlFlowBlock source, GimpleLabel targetLabel, GimpleStatement statement)
    {
        if (_controlFlowFunction.TryGetBlock(targetLabel, out var target) && target is not null)
        {
            EmitJumpToControlFlowTarget(block, source, target, statement, _currentInstruction);
            return;
        }

        _problems.Add(new LirProblem(LirProblemKind.MissingTarget, source, statement, $"Missing LIR target for label '{targetLabel.Name}'."));
        EmitJumpToBlock(block, _blocksByControlFlowBlock.TryGetValue(source, out var self) ? self : _blocks[0], statement, _currentInstruction);
    }

    private void EmitJumpToControlFlowTarget(
        LirBlock block,
        ControlFlowBlock source,
        ControlFlowBlock target,
        GimpleStatement? statement,
        GimpleStatementAnnotations? sourceInstruction)
    {
        if (_edgeSplitBlocks.TryGetValue((source, target), out var split))
        {
            EmitJumpToBlock(block, split, statement, sourceInstruction);
            return;
        }

        EmitEdgeCopies(block, source, target);
        EmitJumpToBlock(block, GetBaseBlock(target), statement, sourceInstruction);
    }

    private void EmitJumpToBlock(
        LirBlock block,
        LirBlock target,
        GimpleStatement? statement,
        GimpleStatementAnnotations? sourceInstruction)
    {
        Emit(block, LirInstructionKind.Jump, null, ImmutableArray<LirOperand>.Empty, address: null, op: string.Empty, conversionKind: null, callSignature: null,
            parallelCopies: default, switchCases: default, target: target, trueTarget: null, falseTarget: null, sourceStatement: statement, sourceValue: null,
            sourceInstruction: sourceInstruction, valueNumber: null);
    }

    private void EmitEdgeCopies(LirBlock block, ControlFlowBlock source, ControlFlowBlock target)
    {
        if (!_edgeCopies.TryGetValue((source, target), out var copies) || copies.Count == 0)
            return;

        Emit(block, LirInstructionKind.ParallelCopy, null, ImmutableArray<LirOperand>.Empty, address: null, op: string.Empty, conversionKind: null, callSignature: null,
            parallelCopies: copies.ToImmutableArray(), switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: null, sourceValue: null,
            sourceInstruction: null, valueNumber: null);
    }

    private LirBlock ResolveTarget(ControlFlowBlock source, GimpleLabel label, GimpleStatement statement)
    {
        if (_controlFlowFunction.TryGetBlock(label, out var target) && target is not null)
            return Redirect(source, target);

        _problems.Add(new LirProblem(LirProblemKind.MissingTarget, source, statement, $"Missing LIR target for label '{label.Name}'."));
        return _blocksByControlFlowBlock.TryGetValue(source, out var self) ? self : _blocks[0];
    }

    private LirBlock Redirect(ControlFlowBlock source, ControlFlowBlock target)
    {
        if (_edgeSplitBlocks.TryGetValue((source, target), out var split))
            return split;

        return GetBaseBlock(target);
    }

    private LirBlock GetBaseBlock(ControlFlowBlock target)
    {
        if (_blocksByControlFlowBlock.TryGetValue(target, out var block))
            return block;

        _problems.Add(new LirProblem(LirProblemKind.MissingTarget, target, target.GimpleBlock, $"Missing LIR block for CFG block '{target}'."));
        return _blocks[0];
    }

    private void SealBlocks()
    {
        foreach (var block in _blocks)
        {
            if (_instructions.TryGetValue(block, out var instructions))
                block.SetInstructions(instructions.ToImmutableArray());
            else
                block.SetInstructions(ImmutableArray<LirInstruction>.Empty);
        }
    }

    /// <summary>
    /// Folds <c>%dst = copy %src</c> into the instruction that defines <c>%src</c> whenever that
    /// definition is the only one, the copy is its only use, and both registers hold the same
    /// representation. Without this the register allocator has to coalesce the pair, and a
    /// missed coalesce becomes a machine move.
    /// </summary>
    private void CoalesceCopies()
    {
        var definitions = new Dictionary<LirVirtualRegister, int>();
        var uses = new Dictionary<LirVirtualRegister, int>();
        foreach (var block in _blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                CountDefinitions(instruction, definitions);
                CountUses(instruction, uses);
            }
        }

        foreach (var block in _blocks)
        {
            List<LirInstruction>? rewritten = null;
            var definitionIndices = new Dictionary<LirVirtualRegister, int>();
            var instructions = block.Instructions;

            for (var i = 0; i < instructions.Length; i++)
            {
                var instruction = instructions[i];
                if (instruction.Kind == LirInstructionKind.Copy &&
                    instruction.Result is { } destination &&
                    instruction.Operands.Length == 1 &&
                    instruction.Operands[0].Kind == LirOperandKind.Register &&
                    instruction.Operands[0].Register is { } source &&
                    !ReferenceEquals(source, destination) &&
                    !source.HasFixedRegister &&
                    !destination.HasFixedRegister &&
                    source.RegisterClass == destination.RegisterClass &&
                    SameType(source.Type, destination.Type) &&
                    Count(definitions, source) == 1 &&
                    Count(uses, source) == 1 &&
                    Count(definitions, destination) == 1 &&
                    definitionIndices.TryGetValue(source, out var definitionIndex))
                {
                    rewritten ??= new List<LirInstruction>(instructions);
                    rewritten[definitionIndex] = rewritten[definitionIndex].WithResult(destination);
                    definitionIndices[destination] = definitionIndex;
                    rewritten[i] = null!;
                    continue;
                }

                if (instruction.Result is not null && IsRetargetableDefinition(instruction))
                    definitionIndices[instruction.Result] = i;
            }

            if (rewritten is not null)
                block.SetInstructions(rewritten.Where(static instruction => instruction is not null).ToImmutableArray());
        }
    }

    private static bool IsRetargetableDefinition(LirInstruction instruction)
        => instruction.Kind is not (LirInstructionKind.Parameter or LirInstructionKind.InlineAssembly);

    private static int Count(Dictionary<LirVirtualRegister, int> counts, LirVirtualRegister register)
        => counts.TryGetValue(register, out var count) ? count : 0;

    private static void Add(Dictionary<LirVirtualRegister, int> counts, LirVirtualRegister register)
        => counts[register] = Count(counts, register) + 1;

    private static void CountDefinitions(LirInstruction instruction, Dictionary<LirVirtualRegister, int> definitions)
    {
        if (instruction.Result is not null)
            Add(definitions, instruction.Result);

        foreach (var copy in instruction.ParallelCopies)
            Add(definitions, copy.Destination);
    }

    private static void CountUses(LirInstruction instruction, Dictionary<LirVirtualRegister, int> uses)
    {
        foreach (var operand in instruction.Operands)
            CountOperandUses(operand, uses);

        if (instruction.Address is not null)
            CountAddressUses(instruction.Address, uses);

        foreach (var copy in instruction.ParallelCopies)
            CountOperandUses(copy.Source, uses);

        foreach (var @case in instruction.SwitchCases)
            CountOperandUses(@case.Value, uses);
    }

    private static void CountOperandUses(LirOperand operand, Dictionary<LirVirtualRegister, int> uses)
    {
        switch (operand.Kind)
        {
            case LirOperandKind.Register:
                if (operand.Register is not null)
                    Add(uses, operand.Register);
                break;

            case LirOperandKind.Address:
                if (operand.Address is not null)
                    CountAddressUses(operand.Address, uses);
                break;
        }
    }

    private static void CountAddressUses(LirAddress address, Dictionary<LirVirtualRegister, int> uses)
    {
        if (address.BaseOperand is not null)
            CountOperandUses(address.BaseOperand, uses);
        if (address.BaseAddress is not null)
            CountAddressUses(address.BaseAddress, uses);
        if (address.Index is not null)
            CountOperandUses(address.Index, uses);
    }

    private void LayoutBlocks()
    {
        if (_blocks.Count <= 1)
            return;

        var original = _blocks.ToArray();
        var predecessorCounts = new Dictionary<LirBlock, int>(original.Length);
        foreach (var block in original)
            predecessorCounts.Add(block, 0);

        foreach (var block in original)
        {
            foreach (var successor in EnumerateLirSuccessors(block))
            {
                if (predecessorCounts.TryGetValue(successor, out var count))
                    predecessorCounts[successor] = count + 1;
            }
        }

        var placed = new HashSet<LirBlock>();
        var layout = new List<LirBlock>(original.Length);
        var entry = _blocksByControlFlowBlock.TryGetValue(_controlFlowFunction.Entry, out var entryBlock)
            ? entryBlock
            : original[0];
        var current = entry;

        while (layout.Count != original.Length)
        {
            while (placed.Add(current))
            {
                layout.Add(current);
                var next = SelectLayoutSuccessor(current, placed, predecessorCounts);
                if (next is null)
                    break;
                current = next;
            }

            LirBlock? fallback = null;
            foreach (var block in original)
            {
                if (!placed.Contains(block))
                {
                    fallback = block;
                    break;
                }
            }

            if (fallback is null)
                break;
            current = fallback;
        }

        RotateLoopHeaders(layout);
        _blocks.Clear();
        _blocks.AddRange(layout);
    }

    // A loop laid out test first pays two taken branches an iteration, the conditional one at the top
    // and the unconditional back edge. Moving the test behind the body leaves only the conditional one
    private static void RotateLoopHeaders(List<LirBlock> layout)
    {
        var index = new Dictionary<LirBlock, int>(layout.Count);
        for (var i = 0; i < layout.Count; i++)
            index[layout[i]] = i;

        var predecessors = new Dictionary<LirBlock, List<LirBlock>>(layout.Count);
        foreach (var block in layout)
        {
            foreach (var successor in EnumerateLirSuccessors(block))
            {
                if (!index.ContainsKey(successor))
                    continue;
                if (!predecessors.TryGetValue(successor, out var list))
                {
                    list = new List<LirBlock>();
                    predecessors.Add(successor, list);
                }
                list.Add(block);
            }
        }

        // A rotation reorders only the blocks of its own loop, so one pass settles nested loops too
        for (var latch = 1; latch < layout.Count; latch++)
        {
            var terminator = LayoutTerminator(layout[latch]);
            if (terminator is null || terminator.Kind != LirInstructionKind.Jump || terminator.Target is null)
                continue;
            if (!index.TryGetValue(terminator.Target, out var header) || header == 0 || header >= latch)
                continue;
            if (!CanRotateLoopHeader(layout, index, predecessors, header, latch))
                continue;

            var block = layout[header];
            layout.RemoveAt(header);
            layout.Insert(latch, block);
            for (var i = header; i <= latch; i++)
                index[layout[i]] = i;
        }
    }

    private static bool CanRotateLoopHeader(
        List<LirBlock> layout,
        Dictionary<LirBlock, int> index,
        Dictionary<LirBlock, List<LirBlock>> predecessors,
        int header,
        int latch)
    {
        var terminator = LayoutTerminator(layout[header]);
        if (terminator is null || terminator.Kind != LirInstructionKind.Branch)
            return false;
        if (terminator.TrueTarget is null || terminator.FalseTarget is null)
            return false;

        // The test now runs into the body instead of being jumped to, so it has to be cheap
        if (layout[header].Instructions.Length > MaxRotatedLoopHeaderInstructions)
            return false;

        var body = CollectLoopBlocks(layout, index, predecessors, header, latch);
        if (body is null)
            return false;

        // One edge has to continue into the body and the other has to leave, or nothing is gained
        if (body.Contains(terminator.TrueTarget) == body.Contains(terminator.FalseTarget))
            return false;

        // The test is moving forward to the latch, so nothing it guards may already sit behind it
        foreach (var block in body)
        {
            if (index[block] < header)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Collects the loop the back edge closes: the blocks that reach the latch without passing
    /// through the header. Returns null when the walk escapes to the function entry, which means
    /// the header does not dominate the latch and the edge closes no loop of its own.
    /// </summary>
    private static HashSet<LirBlock>? CollectLoopBlocks(
        List<LirBlock> layout,
        Dictionary<LirBlock, int> index,
        Dictionary<LirBlock, List<LirBlock>> predecessors,
        int header,
        int latch)
    {
        var entry = layout[0];
        var body = new HashSet<LirBlock> { layout[header] };
        var pending = new Stack<LirBlock>();
        if (body.Add(layout[latch]))
            pending.Push(layout[latch]);

        while (pending.Count != 0)
        {
            var block = pending.Pop();
            if (ReferenceEquals(block, entry) || !predecessors.TryGetValue(block, out var list) || list.Count == 0)
                return null;

            foreach (var predecessor in list)
            {
                if (!index.ContainsKey(predecessor))
                    return null;
                if (body.Add(predecessor))
                    pending.Push(predecessor);
            }
        }

        return body;
    }

    private static LirInstruction? LayoutTerminator(LirBlock block)
        => block.Instructions.Length == 0 ? null : block.Instructions[^1];

    private LirBlock? SelectLayoutSuccessor(
        LirBlock block,
        HashSet<LirBlock> placed,
        Dictionary<LirBlock, int> predecessorCounts)
    {
        if (block.Instructions.Length == 0)
            return null;

        var terminator = block.Instructions[^1];
        switch (terminator.Kind)
        {
            case LirInstructionKind.Jump:
                return CanFollowLayoutEdge(block, terminator.Target, placed, predecessorCounts)
                    ? terminator.Target
                    : null;

            case LirInstructionKind.Branch:
                return BetterLayoutSuccessor(block, terminator.TrueTarget, terminator.FalseTarget, placed, predecessorCounts);

            case LirInstructionKind.Switch:
                if (CanFollowLayoutEdge(block, terminator.Target, placed, predecessorCounts))
                    return terminator.Target;
                foreach (var @case in terminator.SwitchCases)
                {
                    if (CanFollowLayoutEdge(block, @case.Target, placed, predecessorCounts))
                        return @case.Target;
                }
                return null;

            case LirInstructionKind.InlineAssembly when terminator.SourceStatement is GimpleAsmStatement { IsGoto: true }:
                return CanFollowLayoutEdge(block, terminator.Target, placed, predecessorCounts)
                    ? terminator.Target
                    : null;

            default:
                return null;
        }
    }

    private LirBlock? BetterLayoutSuccessor(
        LirBlock source,
        LirBlock? first,
        LirBlock? second,
        HashSet<LirBlock> placed,
        Dictionary<LirBlock, int> predecessorCounts)
    {
        var firstScore = LayoutScore(source, first, placed, predecessorCounts);
        var secondScore = LayoutScore(source, second, placed, predecessorCounts);
        if (firstScore == int.MinValue && secondScore == int.MinValue)
            return null;
        return firstScore >= secondScore ? first : second;
    }

    private static int LayoutScore(
        LirBlock source,
        LirBlock? target,
        HashSet<LirBlock> placed,
        Dictionary<LirBlock, int> predecessorCounts)
    {
        if (target is null || placed.Contains(target))
            return int.MinValue;

        var score = target.IsEdgeSplit ? 300 : 0;
        if (predecessorCounts.TryGetValue(target, out var predecessors) && predecessors <= 1)
            score += 80;

        var sourceBlock = source.SourceBlock;
        var targetBlock = target.SourceBlock;
        if (sourceBlock is not null && targetBlock is not null)
        {
            var delta = targetBlock.Ordinal - sourceBlock.Ordinal;
            if (delta == 1)
                score += 500;
            else if (delta > 1)
                score += Math.Max(0, 120 - delta);
            else if (delta < 0)
                score -= 40;
        }

        return score;
    }

    private bool CanFollowLayoutEdge(
        LirBlock source,
        LirBlock? target,
        HashSet<LirBlock> placed,
        Dictionary<LirBlock, int> predecessorCounts)
    {
        if (target is null || placed.Contains(target))
            return false;
        if (!predecessorCounts.TryGetValue(target, out var predecessors) || predecessors <= 1)
            return true;

        var sourceBlock = source.SourceBlock;
        var targetBlock = target.SourceBlock;
        if (sourceBlock is null || targetBlock is null || targetBlock.Ordinal <= sourceBlock.Ordinal + 1)
            return true;

        var nextOrdinal = sourceBlock.Ordinal + 1;
        if (nextOrdinal < _controlFlowFunction.RealBlocks.Length &&
            _blocksByControlFlowBlock.TryGetValue(_controlFlowFunction.RealBlocks[nextOrdinal], out var lexicalNext) &&
            !placed.Contains(lexicalNext))
            return false;

        return true;
    }

    private static IEnumerable<LirBlock> EnumerateLirSuccessors(LirBlock block)
    {
        if (block.Instructions.Length == 0)
            yield break;

        var terminator = block.Instructions[^1];
        switch (terminator.Kind)
        {
            case LirInstructionKind.Jump:
                if (terminator.Target is not null)
                    yield return terminator.Target;
                break;

            case LirInstructionKind.Branch:
                if (terminator.TrueTarget is not null)
                    yield return terminator.TrueTarget;
                if (terminator.FalseTarget is not null && !ReferenceEquals(terminator.FalseTarget, terminator.TrueTarget))
                    yield return terminator.FalseTarget;
                break;

            case LirInstructionKind.Switch:
                foreach (var @case in terminator.SwitchCases)
                    yield return @case.Target;
                if (terminator.Target is not null)
                    yield return terminator.Target;
                break;

            case LirInstructionKind.InlineAssembly when terminator.SourceStatement is GimpleAsmStatement { IsGoto: true }:
                foreach (var operand in terminator.Operands)
                {
                    if (operand.Kind == LirOperandKind.Label && operand.Label is not null)
                        yield return operand.Label;
                }
                if (terminator.Target is not null)
                    yield return terminator.Target;
                break;
        }
    }

    private LirInstruction Emit(
        LirBlock block,
        LirInstructionKind kind,
        LirVirtualRegister? result,
        ImmutableArray<LirOperand> operands,
        LirAddress? address,
        string? op,
        GimpleConversionKind? conversionKind,
        FunctionType? callSignature,
        ImmutableArray<LirParallelCopy> parallelCopies,
        ImmutableArray<LirSwitchCase> switchCases,
        LirBlock? target,
        LirBlock? trueTarget,
        LirBlock? falseTarget,
        GimpleStatement? sourceStatement,
        GimpleValue? sourceValue,
        GimpleStatementAnnotations? sourceInstruction,
        ValueNumber? valueNumber,
        GimpleTreeCode treeCode = GimpleTreeCode.None)
    {
        var instruction = new LirInstruction(
            _nextInstructionOrdinal++,
            kind,
            result,
            operands,
            address,
            op,
            conversionKind,
            callSignature,
            parallelCopies,
            switchCases,
            target,
            trueTarget,
            falseTarget,
            sourceStatement,
            sourceValue,
            sourceInstruction,
            valueNumber,
            treeCode);

        if (!_instructions.TryGetValue(block, out var list))
        {
            list = new List<LirInstruction>();
            _instructions.Add(block, list);
        }

        list.Add(instruction);
        return instruction;
    }

    private void EmitNop(LirBlock block, GimpleStatement statement)
        => Emit(block, LirInstructionKind.Nop, null, ImmutableArray<LirOperand>.Empty, address: null, op: string.Empty, conversionKind: null,
            callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null,
            sourceStatement: statement, sourceValue: null, sourceInstruction: _currentInstruction, valueNumber: null);

    private bool HasTerminator(LirBlock block)
    {
        return _instructions.TryGetValue(block, out var list) &&
               list.Count != 0 &&
               list[^1].IsTerminator;
    }

    private LirBlock NewBlock(string name, ControlFlowBlock? sourceBlock, bool isEdgeSplit)
    {
        var block = new LirBlock(_blocks.Count, name, sourceBlock, isEdgeSplit);
        _blocks.Add(block);
        return block;
    }

    private LirVirtualRegister NewVirtualRegister(QualifiedType type, GimpleName? sourceName, ValueNumber? valueNumber)
    {
        var registerClass = GetRegisterClass(type);
        var fixedRegister = TryGetFixedRegister(sourceName, registerClass);
        var register = new LirVirtualRegister(_registers.Count, type, registerClass, sourceName, valueNumber, fixedRegister);
        _registers.Add(register);
        return register;
    }

    private LirVirtualRegister GetResultRegister(QualifiedType type, GimpleOperandInfo? expression, LirVirtualRegister? destination)
        => GetResultRegister(type, GetValueNumber(expression), destination);

    private LirVirtualRegister GetResultRegister(QualifiedType type, ValueNumber? valueNumber, LirVirtualRegister? destination)
    {
        if (destination is not null && SameType(destination.Type, type))
            return destination;

        return NewVirtualRegister(type, sourceName: null, valueNumber);
    }

    private MachineRegister TryGetFixedRegister(GimpleName? sourceName, LirRegisterClass registerClass)
    {
        if (sourceName?.Variable.Symbol is not VariableSymbol variable || variable.ExplicitRegisterName is null)
            return MachineRegister.Invalid;
        return TargetRegisterInfo.TryParseExplicitRegister(_target, variable.ExplicitRegisterName, registerClass, out var register)
            ? register
            : MachineRegister.Invalid;
    }

    private LirVirtualRegister GetRegister(GimpleName name)
    {
        if (!_registersByName.TryGetValue(name, out var register))
        {
            _function.ValueNumbering.TryGetValueNumber(name, out var valueNumber);
            register = NewVirtualRegister(name.Type, name, valueNumber);
            _registersByName.Add(name, register);
        }

        return register;
    }

    private LirOperand GetOperand(GimpleName name)
    {
        if (name.IsUndefined)
            return LirOperand.Undefined(name, name.Type);

        return LirOperand.ForRegister(GetRegister(name));
    }

    private LirStackSlot GetOrCreateStackSlot(GimpleVariableDeclaration declaration)
    {
        if (declaration.Symbol is not null && _stackSlotsBySymbol.TryGetValue(declaration.Symbol, out var existing))
            return existing;

        var name = declaration.Symbol?.Name ?? "local";
        var slot = NewStackSlot(name, declaration.Type, declaration.Symbol, temporary: null, isParameter: declaration.Symbol is ParameterSymbol, declaration.StorageClass);
        if (declaration.Symbol is not null)
            _stackSlotsBySymbol[declaration.Symbol] = slot;
        return slot;
    }

    private LirStackSlot GetOrCreateStackSlot(Symbol symbol, QualifiedType type, bool isParameter, StorageClass storageClass)
    {
        if (_stackSlotsBySymbol.TryGetValue(symbol, out var existing))
            return existing;

        var slot = NewStackSlot(symbol.Name, type, symbol, temporary: null, isParameter, storageClass);
        _stackSlotsBySymbol.Add(symbol, slot);
        return slot;
    }

    private LirStackSlot GetOrCreateStackSlot(GimpleTemporaryValue temporary)
    {
        if (_stackSlotsByTemporary.TryGetValue(temporary, out var existing))
            return existing;

        var slot = NewStackSlot(temporary.Name, temporary.Type, symbol: null, temporary, isParameter: false, StorageClass.Auto);
        _stackSlotsByTemporary.Add(temporary, slot);
        return slot;
    }

    private LirStackSlot GetOrCreateAnonymousStackSlot(QualifiedType type, string name)
        => NewStackSlot(name, type, symbol: null, temporary: null, isParameter: false, StorageClass.Auto);

    private LirStackSlot NewStackSlot(string name, QualifiedType type, Symbol? symbol, GimpleTemporaryValue? temporary, bool isParameter, StorageClass storageClass)
    {
        var size = _target.SizeOf(type);
        var alignment = _target.AlignOf(type);
        var slot = new LirStackSlot(_stackSlots.Count, name, type, size, alignment, symbol, temporary, isParameter, storageClass);
        _stackSlots.Add(slot);
        return slot;
    }

    private GimpleDefinition? GetPrimaryDefinition(GimpleStatementAnnotations instruction)
    {
        foreach (var definition in instruction.Definitions)
        {
            if (definition.Name.Variable.Kind != GimpleVariableKind.Memory)
                return definition;
        }

        return null;
    }

    private bool TryGetExpression(GimpleStatementAnnotations instruction, int index, out GimpleOperandInfo expression)
    {
        if (index >= 0 && index < instruction.Operands.Length)
        {
            expression = instruction.Operands[index];
            return true;
        }

        expression = null!;
        return false;
    }

    private GimpleOperandInfo? GetChild(GimpleOperandInfo? expression, int index)
    {
        if (expression is null || index < 0 || index >= expression.Children.Length)
            return null;

        return expression.Children[index];
    }

    private ValueNumber? GetValueNumber(GimpleOperandInfo? expression)
    {
        if (expression is null)
            return null;

        _function.ValueNumbering.TryGetValueNumber(expression, out var valueNumber);
        return valueNumber;
    }

    private bool IsStackAllocatedLocal(GimpleVariableDeclaration declaration)
    {
        if (declaration.Symbol is not VariableSymbol variable)
            return declaration.Symbol is ParameterSymbol;

        return variable.StorageClass is StorageClass.None or StorageClass.Auto or StorageClass.Register;
    }

    private int GetFieldOffset(FieldSymbol? field)
        => field is null ? 0 : _target.GetFieldPlacement(field).ByteOffset;

    private static int AlignTo(int value, int alignment)
    {
        if (alignment <= 1)
            return value;

        var remainder = value % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }

    private static string GetBlockName(ControlFlowBlock block)
        => block.Label?.Name ?? "bb" + block.Ordinal.ToString(CultureInfo.InvariantCulture);

    private static string TokenText(SyntaxToken token)
        => string.IsNullOrEmpty(token.Text) ? token.Kind.ToString() : token.Text;

    private static bool IsComparisonOperator(string op)
        => op is "==" or "!=" or "<" or "<=" or ">" or ">=";

    private static bool TryGetImmediateTruth(LirOperand operand, out bool truth)
    {
        if (operand.Kind != LirOperandKind.Immediate)
        {
            truth = false;
            return false;
        }

        switch (operand.Immediate)
        {
            case null:
                truth = false;
                return true;
            case bool value:
                truth = value;
                return true;
            case char value:
                truth = value != 0;
                return true;
            case byte value:
                truth = value != 0;
                return true;
            case sbyte value:
                truth = value != 0;
                return true;
            case short value:
                truth = value != 0;
                return true;
            case ushort value:
                truth = value != 0;
                return true;
            case int value:
                truth = value != 0;
                return true;
            case uint value:
                truth = value != 0;
                return true;
            case long value:
                truth = value != 0;
                return true;
            case ulong value:
                truth = value != 0;
                return true;
            case float value:
                truth = value != 0.0f;
                return true;
            case double value:
                truth = value != 0.0;
                return true;
            case decimal value:
                truth = value != 0m;
                return true;
            default:
                truth = false;
                return false;
        }
    }

    private static bool SameType(QualifiedType left, QualifiedType right)
        => string.Equals(left.ToDisplayString(), right.ToDisplayString(), StringComparison.Ordinal);

    private static bool IsVoid(QualifiedType type)
        => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Void };

    private LirRegisterClass GetRegisterClass(QualifiedType type) => CAbi.PreferredLirRegisterClass(_target, type);

    internal readonly struct LoweredLirFunction
    {
        public LirFunction Function { get; }
        public ImmutableArray<LirProblem> Problems { get; }

        public LoweredLirFunction(LirFunction function, ImmutableArray<LirProblem> problems)
        {
            Function = function ?? throw new ArgumentNullException(nameof(function));
            Problems = problems.IsDefault ? ImmutableArray<LirProblem>.Empty : problems;
        }
    }
}

public sealed class LirPrinter
{
    private readonly StringBuilder _builder = new();
    private readonly LirOptions _options;
    private int _indent;

    private LirPrinter(LirOptions? options)
    {
        _options = options ?? LirOptions.Default;
    }

    public static string Print(LirModule module, LirOptions? options = null)
    {
        if (module is null)
            throw new ArgumentNullException(nameof(module));

        var printer = new LirPrinter(options);
        printer.WriteModule(module);
        return printer._builder.ToString();
    }

    public static string Print(LirFunction function, LirOptions? options = null)
    {
        if (function is null)
            throw new ArgumentNullException(nameof(function));

        var printer = new LirPrinter(options);
        printer.WriteFunction(function);
        return printer._builder.ToString();
    }

    public static void WriteTo(TextWriter writer, LirModule module, LirOptions? options = null)
    {
        if (writer is null)
            throw new ArgumentNullException(nameof(writer));

        writer.Write(Print(module, options));
    }

    public static void WriteTo(TextWriter writer, LirFunction function, LirOptions? options = null)
    {
        if (writer is null)
            throw new ArgumentNullException(nameof(writer));

        writer.Write(Print(function, options));
    }

    private void WriteModule(LirModule module)
    {
        WriteLine("lir module");
        _indent++;

        foreach (var global in module.Globals)
            WriteLine("global " + FormatSymbol(global.Symbol) + " : " + global.Type.ToDisplayString() + " storage=" + global.StorageClass);

        if (module.Globals.Length != 0 && module.Functions.Length != 0)
            WriteLine(string.Empty);

        for (var i = 0; i < module.Functions.Length; i++)
        {
            if (i != 0)
                WriteLine(string.Empty);
            WriteFunction(module.Functions[i]);
        }

        if (module.Problems.Length != 0)
        {
            WriteLine(string.Empty);
            WriteLine("problems");
            _indent++;
            foreach (var problem in module.Problems)
                WriteLine(problem.Kind + ": " + problem.Message);
            _indent--;
        }

        _indent--;
    }

    private void WriteFunction(LirFunction function)
    {
        var name = function.Symbol?.Name ?? "<anonymous-function>";
        WriteLine("function @" + name);
        _indent++;

        if (function.VirtualRegisters.Length != 0)
        {
            WriteLine("vregs");
            _indent++;
            foreach (var register in function.VirtualRegisters)
            {
                var line = register.Name + " : " + register.Type.ToDisplayString() + " class=" + register.RegisterClass;
                if (register.SourceName is not null)
                    line += " source=" + register.SourceName;
                if (_options.EmitValueNumberComments && register.ValueNumber is not null)
                    line += " ; " + register.ValueNumber;
                WriteLine(line);
            }
            _indent--;
        }

        if (function.StackSlots.Length != 0)
        {
            WriteLine("stack");
            _indent++;
            foreach (var slot in function.StackSlots)
            {
                var line = slot + " " + slot.Name + " : " + slot.Type.ToDisplayString() +
                           " size=" + slot.Size.ToString(CultureInfo.InvariantCulture) +
                           " align=" + slot.Alignment.ToString(CultureInfo.InvariantCulture);
                if (slot.IsParameter)
                    line += " parameter";
                WriteLine(line);
            }
            _indent--;
        }

        foreach (var block in function.Blocks)
        {
            WriteLine(block.Name + ":");
            _indent++;
            foreach (var instruction in block.Instructions)
                WriteInstruction(instruction);
            _indent--;
        }

        if (function.Problems.Length != 0)
        {
            WriteLine("problems");
            _indent++;
            foreach (var problem in function.Problems)
                WriteLine(problem.Kind + ": " + problem.Message);
            _indent--;
        }

        _indent--;
    }

    private void WriteInstruction(LirInstruction instruction)
    {
        var line = instruction.Ordinal.ToString(CultureInfo.InvariantCulture).PadLeft(4, ' ') + ": ";
        switch (instruction.Kind)
        {
            case LirInstructionKind.Nop:
                line += "nop";
                break;

            case LirInstructionKind.Parameter:
                line += FormatResult(instruction) + " = param";
                if (!string.IsNullOrEmpty(instruction.Operator))
                    line += " " + instruction.Operator;
                break;

            case LirInstructionKind.Copy:
                line += FormatResult(instruction) + " = copy " + FormatOperand(instruction.Operands[0]);
                break;

            case LirInstructionKind.ParallelCopy:
                line += "parallelcopy ";
                line += string.Join(", ", instruction.ParallelCopies.Select(move => move.Destination + " <- " + FormatOperand(move.Source)));
                break;

            case LirInstructionKind.Constant:
                line += FormatResult(instruction) + " = const " + FormatOperand(instruction.Operands[0]);
                break;

            case LirInstructionKind.Zero:
                line += FormatResult(instruction) + " = zero";
                break;

            case LirInstructionKind.Unary:
                line += FormatResult(instruction) + " = " + instruction.Operator + " " + FormatOperand(instruction.Operands[0]);
                break;

            case LirInstructionKind.Binary:
                line += FormatResult(instruction) + " = " + FormatOperand(instruction.Operands[0]) + " " + instruction.Operator + " " + FormatOperand(instruction.Operands[1]);
                break;

            case LirInstructionKind.Convert:
                line += FormatResult(instruction) + " = convert." + instruction.Operator + " " + FormatOperand(instruction.Operands[0]);
                break;

            case LirInstructionKind.Cast:
                line += FormatResult(instruction) + " = cast " + FormatOperand(instruction.Operands[0]);
                break;

            case LirInstructionKind.AddressOf:
                line += FormatResult(instruction) + " = addressof " + FormatAddress(instruction.Address);
                break;

            case LirInstructionKind.Load:
                line += FormatResult(instruction) + " = load " + FormatAddress(instruction.Address);
                break;

            case LirInstructionKind.Store:
                line += "store " + FormatAddress(instruction.Address) + " <- " + FormatOperand(instruction.Operands[0]);
                break;

            case LirInstructionKind.ZeroMemory:
                line += "zeromem " + FormatAddress(instruction.Address);
                if (instruction.Operands.Length != 0)
                    line += " bytes=" + FormatOperand(instruction.Operands[0]);
                break;

            case LirInstructionKind.Call:
                if (instruction.Result is not null)
                    line += FormatResult(instruction) + " = ";
                line += "call " + FormatOperand(instruction.Operands[0]) + "(" + string.Join(", ", instruction.Operands.Skip(1).Select(FormatOperand)) + ")";
                break;

            case LirInstructionKind.VaStart:
                if (instruction.Result is not null)
                    line += FormatResult(instruction) + " = ";
                line += "vastart";
                if (instruction.Operands.Length != 0)
                    line += " " + string.Join(", ", instruction.Operands.Select(FormatOperand));
                break;

            case LirInstructionKind.VaArg:
                if (instruction.Result is not null)
                    line += FormatResult(instruction) + " = ";
                line += "vaarg " + string.Join(", ", instruction.Operands.Select(FormatOperand));
                break;

            case LirInstructionKind.InlineAssembly:
                line += "asm \"" + EscapeString(instruction.Operator) + "\"";
                break;

            case LirInstructionKind.Jump:
                line += "jump " + FormatBlock(instruction.Target);
                break;

            case LirInstructionKind.Branch:
                line += instruction.Operands.Length == 2 && !string.IsNullOrEmpty(instruction.Operator)
                    ? "branch " + FormatOperand(instruction.Operands[0]) + " " + instruction.Operator + " " + FormatOperand(instruction.Operands[1]) + " ? " + FormatBlock(instruction.TrueTarget) + " : " + FormatBlock(instruction.FalseTarget)
                    : "branch " + FormatOperand(instruction.Operands[0]) + " ? " + FormatBlock(instruction.TrueTarget) + " : " + FormatBlock(instruction.FalseTarget);
                break;

            case LirInstructionKind.Switch:
                line += "switch " + FormatOperand(instruction.Operands[0]) + " default " + FormatBlock(instruction.Target);
                if (instruction.SwitchCases.Length != 0)
                    line += " { " + string.Join(", ", instruction.SwitchCases.Select(c => FormatOperand(c.Value) + " -> " + FormatBlock(c.Target))) + " }";
                break;

            case LirInstructionKind.Return:
                line += instruction.Operands.Length == 0 ? "return" : "return " + FormatOperand(instruction.Operands[0]);
                break;

            case LirInstructionKind.Unreachable:
                line += "unreachable";
                break;

            default:
                line += instruction.Kind.ToString().ToLowerInvariant();
                break;
        }

        if (_options.EmitValueNumberComments && instruction.ValueNumber is not null)
            line += " ; " + instruction.ValueNumber;

        WriteLine(line);
    }

    private static string FormatResult(LirInstruction instruction)
        => instruction.Result?.ToString() ?? "%void";

    private static string FormatOperand(LirOperand operand)
    {
        switch (operand.Kind)
        {
            case LirOperandKind.Register:
                return operand.Register!.ToString();
            case LirOperandKind.Immediate:
                return FormatImmediate(operand.Immediate);
            case LirOperandKind.Symbol:
                return FormatSymbol(operand.Symbol);
            case LirOperandKind.StackSlot:
                return operand.StackSlot!.ToString();
            case LirOperandKind.Address:
                return FormatAddress(operand.Address);
            case LirOperandKind.Label:
                return FormatBlock(operand.Label);
            case LirOperandKind.Undefined:
                return operand.UndefinedName is null ? "undef" : "undef(" + operand.UndefinedName + ")";
            case LirOperandKind.Void:
                return "void";
            default:
                return "<none>";
        }
    }

    private static string FormatAddress(LirAddress? address)
    {
        if (address is null)
            return "<addr>";

        switch (address.Kind)
        {
            case LirAddressKind.StackSlot:
                return "&" + address.StackSlot;
            case LirAddressKind.Symbol:
                return "&" + FormatSymbol(address.Symbol);
            case LirAddressKind.Indirect:
                return "*" + FormatOperand(address.BaseOperand ?? LirOperand.None);
            case LirAddressKind.Element:
                return "element(" + FormatAddress(address.BaseAddress) +
                       (address.Index is null ? string.Empty : ", index=" + FormatOperand(address.Index)) +
                       ", scale=" + address.Scale.ToString(CultureInfo.InvariantCulture) + ")";
            case LirAddressKind.Field:
                return "field(" + FormatAddress(address.BaseAddress) + ", ." +
                       (address.Field?.Name ?? "<field>") + ", +" +
                       address.Displacement.ToString(CultureInfo.InvariantCulture) + ")";
            default:
                return "<addr>";
        }
    }

    private static string FormatSymbol(Symbol? symbol)
        => symbol is null ? "@<anonymous>" : "@" + symbol.Name;

    private static string FormatBlock(LirBlock? block)
        => block?.Name ?? "<missing>";

    private static string EscapeString(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default: builder.Append(ch); break;
            }
        }

        return builder.ToString();
    }

    private static string FormatImmediate(object? value)
    {
        if (value is null)
            return "null";

        if (value is string text)
            return "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

        if (value is char ch)
            return "'" + ch.ToString().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";

        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture);

        return value.ToString() ?? string.Empty;
    }

    private void WriteLine(string text)
    {
        if (text.Length != 0)
            _builder.Append(' ', _indent * 2);
        _builder.AppendLine(text);
    }
}
