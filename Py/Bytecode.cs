using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Python
{
    public enum PythonOpcode : byte
    {
        Cache = 0,
        BinarySlice = 1,
        BuildTemplate = 2,
        CallFunctionEx = 4,
        CheckExceptionGroupMatch = 5,
        CheckExceptionMatch = 6,
        CleanupThrow = 7,
        DeleteSubscript = 8,
        EndFor = 9,
        EndSend = 10,
        ExitInitCheck = 11,
        FormatSimple = 12,
        FormatWithSpec = 13,
        GetAsyncIterator = 14,
        GetAsyncNext = 15,
        GetIterator = 16,
        Reserved = 17,
        GetLength = 18,
        GetYieldFromIterator = 19,
        InterpreterExit = 20,
        LoadBuildClass = 21,
        LoadLocals = 22,
        MakeFunction = 23,
        MatchKeys = 24,
        MatchMapping = 25,
        MatchSequence = 26,
        Nop = 27,
        NotTaken = 28,
        PopExcept = 29,
        PopIterator = 30,
        PopTop = 31,
        PushExceptionInfo = 32,
        PushNull = 33,
        ReturnGenerator = 34,
        ReturnValue = 35,
        SetupAnnotations = 36,
        StoreSlice = 37,
        StoreSubscript = 38,
        ToBoolean = 39,
        UnaryInvert = 40,
        UnaryNegative = 41,
        UnaryNot = 42,
        WithExceptStart = 43,
        BinaryOperation = 44,
        BuildInterpolation = 45,
        BuildList = 46,
        BuildMap = 47,
        BuildSet = 48,
        BuildSlice = 49,
        BuildString = 50,
        BuildTuple = 51,
        Call = 52,
        CallIntrinsic1 = 53,
        CallIntrinsic2 = 54,
        CallKeyword = 55,
        CompareOperation = 56,
        ContainsOperation = 57,
        ConvertValue = 58,
        Copy = 59,
        CopyFreeVariables = 60,
        DeleteAttribute = 61,
        DeleteDereference = 62,
        DeleteFast = 63,
        DeleteGlobal = 64,
        DeleteName = 65,
        DictionaryMerge = 66,
        DictionaryUpdate = 67,
        EndAsyncFor = 68,
        ExtendedArgument = 69,
        ForIterator = 70,
        GetAwaitable = 71,
        ImportFrom = 72,
        ImportName = 73,
        IsOperation = 74,
        JumpBackward = 75,
        JumpBackwardNoInterrupt = 76,
        JumpForward = 77,
        ListAppend = 78,
        ListExtend = 79,
        LoadAttribute = 80,
        LoadCommonConstant = 81,
        LoadConstant = 82,
        LoadDereference = 83,
        LoadFast = 84,
        LoadFastAndClear = 85,
        LoadFastBorrow = 86,
        LoadFastBorrowLoadFastBorrow = 87,
        LoadFastCheck = 88,
        LoadFastLoadFast = 89,
        LoadFromDictionaryOrDereference = 90,
        LoadFromDictionaryOrGlobals = 91,
        LoadGlobal = 92,
        LoadName = 93,
        LoadSmallInteger = 94,
        LoadSpecial = 95,
        LoadSuperAttribute = 96,
        MakeCell = 97,
        MapAdd = 98,
        MatchClass = 99,
        PopJumpIfFalse = 100,
        PopJumpIfNone = 101,
        PopJumpIfNotNone = 102,
        PopJumpIfTrue = 103,
        RaiseVariableArguments = 104,
        Reraise = 105,
        Send = 106,
        SetAdd = 107,
        SetFunctionAttribute = 108,
        SetUpdate = 109,
        StoreAttribute = 110,
        StoreDereference = 111,
        StoreFast = 112,
        StoreFastLoadFast = 113,
        StoreFastStoreFast = 114,
        StoreGlobal = 115,
        StoreName = 116,
        Swap = 117,
        UnpackExtended = 118,
        UnpackSequence = 119,
        YieldValue = 120,
        Resume = 128,
    }

    public enum PythonBinaryOperation : byte
    {
        Add = 0,
        And = 1,
        FloorDivide = 2,
        LeftShift = 3,
        MatrixMultiply = 4,
        Multiply = 5,
        Remainder = 6,
        Or = 7,
        Power = 8,
        RightShift = 9,
        Subtract = 10,
        TrueDivide = 11,
        Xor = 12,
        InPlaceAdd = 13,
        InPlaceAnd = 14,
        InPlaceFloorDivide = 15,
        InPlaceLeftShift = 16,
        InPlaceMatrixMultiply = 17,
        InPlaceMultiply = 18,
        InPlaceRemainder = 19,
        InPlaceOr = 20,
        InPlacePower = 21,
        InPlaceRightShift = 22,
        InPlaceSubtract = 23,
        InPlaceTrueDivide = 24,
        InPlaceXor = 25,
        Subscript = 26,
    }

    internal enum PythonIntrinsic1 : byte
    {
        Invalid = 0,
        Print = 1,
        ImportStar = 2,
        StopIterationError = 3,
        AsyncGeneratorWrap = 4,
        UnaryPositive = 5,
        ListToTuple = 6,
        TypeVariable = 7,
        ParameterSpecification = 8,
        TypeVariableTuple = 9,
        SubscriptGeneric = 10,
        TypeAlias = 11,
    }

    internal enum BytecodeJumpKind : byte
    {
        None,
        Unconditional,
        UnconditionalNoInterrupt,
        ConditionalForward,
        ForIteratorForward,
        SendForward,
    }

    internal sealed class BytecodeLabel
    {
        internal BytecodeLabel(int id)
        {
            Id = id;
        }

        public int Id { get; }
        public int InstructionIndex { get; internal set; } = -1;
    }

    internal readonly struct BytecodeInstruction
    {
        public BytecodeInstruction(
            PythonOpcode opcode,
            int operand,
            BytecodeLabel? target,
            BytecodeJumpKind jumpKind,
            TextSpan sourceSpan)
        {
            Opcode = opcode;
            Operand = operand;
            Target = target;
            JumpKind = jumpKind;
            SourceSpan = sourceSpan;
        }

        public PythonOpcode Opcode { get; }
        public int Operand { get; }
        public BytecodeLabel? Target { get; }
        public BytecodeJumpKind JumpKind { get; }
        public TextSpan SourceSpan { get; }
    }

    internal sealed class BytecodeExceptionRegion
    {
        public BytecodeExceptionRegion(
            BytecodeLabel start,
            BytecodeLabel end,
            BytecodeLabel handler,
            int stackDepthAdjustment,
            bool preserveLastInstruction)
        {
            Start = start;
            End = end;
            Handler = handler;
            StackDepthAdjustment = stackDepthAdjustment;
            PreserveLastInstruction = preserveLastInstruction;
        }

        public BytecodeLabel Start { get; }
        public BytecodeLabel End { get; }
        public BytecodeLabel Handler { get; }
        public int StackDepthAdjustment { get; }
        public bool PreserveLastInstruction { get; }
        public List<BytecodeExceptionExclusion> Exclusions { get; } = [];
    }

    internal sealed class BytecodeExceptionExclusion
    {
        public BytecodeExceptionExclusion(BytecodeLabel start, BytecodeLabel end)
        {
            Start = start;
            End = end;
        }

        public BytecodeLabel Start { get; }
        public BytecodeLabel End { get; }
    }

    internal sealed class BytecodeBuilder
    {
        private readonly List<BytecodeInstruction> _instructions = [];
        private readonly List<BytecodeLabel> _labels = [];
        private readonly List<BytecodeExceptionRegion> _exceptionRegions = [];

        public int Count => _instructions.Count;
        public IReadOnlyList<BytecodeInstruction> Instructions => _instructions;
        public IReadOnlyList<BytecodeLabel> Labels => _labels;
        public IReadOnlyList<BytecodeExceptionRegion> ExceptionRegions => _exceptionRegions;

        public BytecodeLabel DefineLabel()
        {
            var label = new BytecodeLabel(_labels.Count);
            _labels.Add(label);
            return label;
        }

        public void MarkLabel(BytecodeLabel label)
        {
            ArgumentNullException.ThrowIfNull(label);
            if ((uint)label.Id >= (uint)_labels.Count || !ReferenceEquals(_labels[label.Id], label))
                throw new ArgumentException("The label belongs to another bytecode builder.", nameof(label));
            if (label.InstructionIndex >= 0)
                throw new InvalidOperationException("The label is already bound.");

            label.InstructionIndex = _instructions.Count;
        }

        public void Emit(PythonOpcode opcode, int operand = 0, TextSpan sourceSpan = default)
        {
            if (operand < 0)
                throw new ArgumentOutOfRangeException(nameof(operand));
            if (opcode is PythonOpcode.Cache or PythonOpcode.ExtendedArgument)
                throw new ArgumentException("CACHE and EXTENDED_ARG are emitted by the assembler.", nameof(opcode));

            _instructions.Add(new BytecodeInstruction(
                opcode,
                operand,
                target: null,
                BytecodeJumpKind.None,
                sourceSpan));
        }

        public void EmitJump(BytecodeLabel target, bool noInterrupt = false, TextSpan sourceSpan = default)
        {
            ValidateTarget(target);
            _instructions.Add(new BytecodeInstruction(
                PythonOpcode.JumpForward,
                operand: 0,
                target,
                noInterrupt ? BytecodeJumpKind.UnconditionalNoInterrupt : BytecodeJumpKind.Unconditional,
                sourceSpan));
        }

        public void EmitConditionalJump(PythonOpcode opcode, BytecodeLabel target, TextSpan sourceSpan = default)
        {
            if (opcode is not (
                PythonOpcode.PopJumpIfFalse or
                PythonOpcode.PopJumpIfTrue or
                PythonOpcode.PopJumpIfNone or
                PythonOpcode.PopJumpIfNotNone))
            {
                throw new ArgumentException("The opcode is not a forward conditional jump.", nameof(opcode));
            }

            ValidateTarget(target);
            _instructions.Add(new BytecodeInstruction(
                opcode,
                operand: 0,
                target,
                BytecodeJumpKind.ConditionalForward,
                sourceSpan));
        }

        public void EmitForIterator(BytecodeLabel cleanupTarget, TextSpan sourceSpan = default)
        {
            ValidateTarget(cleanupTarget);
            _instructions.Add(new BytecodeInstruction(
                PythonOpcode.ForIterator,
                operand: 0,
                cleanupTarget,
                BytecodeJumpKind.ForIteratorForward,
                sourceSpan));
        }

        public void EmitSend(BytecodeLabel completionTarget, TextSpan sourceSpan = default)
        {
            ValidateTarget(completionTarget);
            _instructions.Add(new BytecodeInstruction(
                PythonOpcode.Send,
                operand: 0,
                completionTarget,
                BytecodeJumpKind.SendForward,
                sourceSpan));
        }

        public BytecodeExceptionRegion AddExceptionRegion(
            BytecodeLabel start,
            BytecodeLabel end,
            BytecodeLabel handler,
            int stackDepthAdjustment = 0,
            bool preserveLastInstruction = false)
        {
            ValidateTarget(start);
            ValidateTarget(end);
            ValidateTarget(handler);
            var region = new BytecodeExceptionRegion(
                start,
                end,
                handler,
                stackDepthAdjustment,
                preserveLastInstruction);
            _exceptionRegions.Add(region);
            return region;
        }

        public void AddExceptionExclusion(
            BytecodeExceptionRegion region,
            BytecodeLabel start,
            BytecodeLabel end)
        {
            ArgumentNullException.ThrowIfNull(region);
            if (!_exceptionRegions.Contains(region))
                throw new ArgumentException("The exception region belongs to another bytecode builder.", nameof(region));
            ValidateTarget(start);
            ValidateTarget(end);
            region.Exclusions.Add(new BytecodeExceptionExclusion(start, end));
        }

        private void ValidateTarget(BytecodeLabel target)
        {
            ArgumentNullException.ThrowIfNull(target);
            if ((uint)target.Id >= (uint)_labels.Count || !ReferenceEquals(_labels[target.Id], target))
                throw new ArgumentException("The label belongs to another bytecode builder.", nameof(target));
        }
    }

    internal readonly struct BytecodeAssemblyResult
    {
        public BytecodeAssemblyResult(
            ImmutableArray<byte> bytecode,
            ImmutableArray<byte> exceptionTable,
            int stackSize)
        {
            Bytecode = bytecode;
            ExceptionTable = exceptionTable;
            StackSize = stackSize;
        }

        public ImmutableArray<byte> Bytecode { get; }
        public ImmutableArray<byte> ExceptionTable { get; }
        public int StackSize { get; }
    }

    internal static class CPython3146OpcodeProfile
    {
        public const int MagicNumber = 3627;

        public static int GetInlineCacheEntries(PythonOpcode opcode) => opcode switch
        {
            PythonOpcode.LoadGlobal => 4,
            PythonOpcode.BinaryOperation => 5,
            PythonOpcode.UnpackSequence => 1,
            PythonOpcode.CompareOperation => 1,
            PythonOpcode.ContainsOperation => 1,
            PythonOpcode.ForIterator => 1,
            PythonOpcode.LoadSuperAttribute => 1,
            PythonOpcode.LoadAttribute => 9,
            PythonOpcode.StoreAttribute => 4,
            PythonOpcode.Call => 3,
            PythonOpcode.CallKeyword => 3,
            PythonOpcode.StoreSubscript => 1,
            PythonOpcode.Send => 1,
            PythonOpcode.JumpBackward => 1,
            PythonOpcode.ToBoolean => 3,
            PythonOpcode.PopJumpIfTrue => 1,
            PythonOpcode.PopJumpIfFalse => 1,
            PythonOpcode.PopJumpIfNone => 1,
            PythonOpcode.PopJumpIfNotNone => 1,
            _ => 0,
        };

        public static bool IsTerminator(PythonOpcode opcode) => opcode is
            PythonOpcode.ReturnValue or
            PythonOpcode.RaiseVariableArguments or
            PythonOpcode.Reraise or
            PythonOpcode.InterpreterExit;
    }

    internal static class CPython3146Assembler
    {
        private const int MaximumExtendedArguments = 3;

        public static BytecodeAssemblyResult Assemble(
            BytecodeBuilder builder,
            ImmutableArray<EmitDiagnostic>.Builder diagnostics)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(diagnostics);

            var instructions = builder.Instructions;
            ValidateLabels(builder, diagnostics);
            if (HasErrors(diagnostics))
                return default;

            var count = instructions.Count;
            var extendedCounts = new int[count];
            var opcodes = new PythonOpcode[count];
            var operands = new int[count];
            var offsets = new int[count + 1];

            for (var i = 0; i < count; i++)
            {
                var instruction = instructions[i];
                opcodes[i] = instruction.Opcode;
                operands[i] = instruction.Operand;
                extendedCounts[i] = GetExtendedArgumentCount(instruction.Operand);
            }

            var converged = false;
            for (var pass = 0; pass < 16; pass++)
            {
                ComputeOffsets(instructions, opcodes, extendedCounts, offsets);
                var changed = false;

                for (var i = 0; i < count; i++)
                {
                    var instruction = instructions[i];
                    var opcode = ResolveOpcode(instruction, i, offsets);
                    var operand = ResolveOperand(instruction, opcode, i, offsets, extendedCounts[i], diagnostics);
                    if (operand < 0)
                        continue;

                    var extended = GetExtendedArgumentCount(operand);
                    if (extended > MaximumExtendedArguments)
                    {
                        diagnostics.Add(new EmitDiagnostic(
                            EmitDiagnosticCode.OperandOutOfRange,
                            EmitDiagnosticSeverity.Error,
                            instruction.SourceSpan,
                            $"Operand {operand} does not fit in CPython's 32-bit wordcode argument."));
                        continue;
                    }

                    if (opcodes[i] != opcode || operands[i] != operand || extendedCounts[i] != extended)
                    {
                        opcodes[i] = opcode;
                        operands[i] = operand;
                        extendedCounts[i] = extended;
                        changed = true;
                    }
                }

                if (!changed)
                {
                    converged = true;
                    break;
                }
            }

            if (!converged)
            {
                diagnostics.Add(new EmitDiagnostic(
                    EmitDiagnosticCode.InvalidBytecode,
                    EmitDiagnosticSeverity.Error,
                    default,
                    "Jump layout did not converge while resolving EXTENDED_ARG instructions."));
                return default;
            }

            if (HasErrors(diagnostics))
                return default;

            ComputeOffsets(instructions, opcodes, extendedCounts, offsets);
            var bytes = ImmutableArray.CreateBuilder<byte>(checked(offsets[^1] * 2));

            for (var i = 0; i < count; i++)
            {
                WriteInstruction(bytes, opcodes[i], operands[i], extendedCounts[i]);
            }

            var stackAnalysis = AnalyzeStack(builder, opcodes, operands, diagnostics);
            if (HasErrors(diagnostics))
                return default;

            var exceptionTable = AssembleExceptionTable(
                builder,
                offsets,
                stackAnalysis.Depths,
                diagnostics);
            if (HasErrors(diagnostics))
                return default;

            return new BytecodeAssemblyResult(
                bytes.MoveToImmutable(),
                exceptionTable,
                stackAnalysis.MaximumDepth);
        }

        private static void ValidateLabels(
            BytecodeBuilder builder,
            ImmutableArray<EmitDiagnostic>.Builder diagnostics)
        {
            foreach (var label in builder.Labels)
            {
                if (label.InstructionIndex < 0)
                {
                    diagnostics.Add(new EmitDiagnostic(
                        EmitDiagnosticCode.InvalidBytecode,
                        EmitDiagnosticSeverity.Error,
                        default,
                        $"Bytecode label L{label.Id} is not bound."));
                }
                else if (label.InstructionIndex > builder.Count)
                {
                    diagnostics.Add(new EmitDiagnostic(
                        EmitDiagnosticCode.InvalidBytecode,
                        EmitDiagnosticSeverity.Error,
                        default,
                        $"Bytecode label L{label.Id} points outside the instruction stream."));
                }
            }

            foreach (var region in builder.ExceptionRegions)
            {
                if (region.Start.InstructionIndex < 0 ||
                    region.End.InstructionIndex < 0 ||
                    region.Handler.InstructionIndex < 0)
                {
                    continue;
                }
                if (region.Start.InstructionIndex >= region.End.InstructionIndex)
                {
                    diagnostics.Add(new EmitDiagnostic(
                        EmitDiagnosticCode.InvalidBytecode,
                        EmitDiagnosticSeverity.Error,
                        default,
                        "An exception-table region must protect at least one instruction."));
                }
                if (region.Handler.InstructionIndex >= builder.Count)
                {
                    diagnostics.Add(new EmitDiagnostic(
                        EmitDiagnosticCode.InvalidBytecode,
                        EmitDiagnosticSeverity.Error,
                        default,
                        "An exception handler must point at an executable instruction."));
                }
                foreach (var exclusion in region.Exclusions)
                {
                    if (exclusion.Start.InstructionIndex < 0 || exclusion.End.InstructionIndex < 0)
                        continue;
                    if (exclusion.Start.InstructionIndex >= exclusion.End.InstructionIndex ||
                        exclusion.Start.InstructionIndex < region.Start.InstructionIndex ||
                        exclusion.End.InstructionIndex > region.End.InstructionIndex)
                    {
                        diagnostics.Add(new EmitDiagnostic(
                            EmitDiagnosticCode.InvalidBytecode,
                            EmitDiagnosticSeverity.Error,
                            default,
                            "An exception-region exclusion must be a non-empty subrange of its region."));
                    }
                }
            }
        }

        private static void ComputeOffsets(
            IReadOnlyList<BytecodeInstruction> instructions,
            PythonOpcode[] opcodes,
            int[] extendedCounts,
            int[] offsets)
        {
            offsets[0] = 0;
            for (var i = 0; i < instructions.Count; i++)
            {
                var size = checked(
                    extendedCounts[i] +
                    1 +
                    CPython3146OpcodeProfile.GetInlineCacheEntries(opcodes[i]));
                offsets[i + 1] = checked(offsets[i] + size);
            }
        }

        private static PythonOpcode ResolveOpcode(
            BytecodeInstruction instruction,
            int instructionIndex,
            int[] offsets)
        {
            if (instruction.JumpKind == BytecodeJumpKind.None)
                return instruction.Opcode;

            var targetOffset = offsets[instruction.Target!.InstructionIndex];
            var sourceOffset = offsets[instructionIndex];
            var backward = targetOffset < sourceOffset;

            return instruction.JumpKind switch
            {
                BytecodeJumpKind.Unconditional => backward
                    ? PythonOpcode.JumpBackward
                    : PythonOpcode.JumpForward,
                BytecodeJumpKind.UnconditionalNoInterrupt => backward
                    ? PythonOpcode.JumpBackwardNoInterrupt
                    : PythonOpcode.JumpForward,
                BytecodeJumpKind.ConditionalForward => instruction.Opcode,
                BytecodeJumpKind.ForIteratorForward => PythonOpcode.ForIterator,
                BytecodeJumpKind.SendForward => PythonOpcode.Send,
                _ => throw new InvalidOperationException("Unknown jump kind."),
            };
        }

        private static int ResolveOperand(
            BytecodeInstruction instruction,
            PythonOpcode opcode,
            int instructionIndex,
            int[] offsets,
            int extendedCount,
            ImmutableArray<EmitDiagnostic>.Builder diagnostics)
        {
            if (instruction.JumpKind == BytecodeJumpKind.None)
                return instruction.Operand;

            var targetOffset = offsets[instruction.Target!.InstructionIndex];
            var instructionEnd = checked(
                offsets[instructionIndex] +
                extendedCount +
                1 +
                CPython3146OpcodeProfile.GetInlineCacheEntries(opcode));

            var operand = opcode is PythonOpcode.JumpBackward or PythonOpcode.JumpBackwardNoInterrupt
                ? instructionEnd - targetOffset
                : targetOffset - instructionEnd;

            if (operand < 0)
            {
                diagnostics.Add(new EmitDiagnostic(
                    EmitDiagnosticCode.InvalidControlFlow,
                    EmitDiagnosticSeverity.Error,
                    instruction.SourceSpan,
                    $"Opcode {opcode} cannot target a bytecode instruction in that direction."));
                return -1;
            }

            return operand;
        }

        private static int GetExtendedArgumentCount(int operand)
        {
            if (operand <= byte.MaxValue)
                return 0;
            if (operand <= ushort.MaxValue)
                return 1;
            if (operand <= 0x00FF_FFFF)
                return 2;
            return 3;
        }

        private static void WriteInstruction(
            ImmutableArray<byte>.Builder bytes,
            PythonOpcode opcode,
            int operand,
            int extendedCount)
        {
            for (var shift = extendedCount * 8; shift >= 8; shift -= 8)
            {
                bytes.Add((byte)PythonOpcode.ExtendedArgument);
                bytes.Add((byte)((uint)operand >> shift));
            }

            bytes.Add((byte)opcode);
            bytes.Add((byte)operand);

            var caches = CPython3146OpcodeProfile.GetInlineCacheEntries(opcode);
            for (var i = 0; i < caches; i++)
            {
                bytes.Add((byte)PythonOpcode.Cache);
                bytes.Add(0);
            }
        }

        private readonly struct StackAnalysisResult
        {
            public StackAnalysisResult(int maximumDepth, int[] depths)
            {
                MaximumDepth = maximumDepth;
                Depths = depths;
            }

            public int MaximumDepth { get; }
            public int[] Depths { get; }
        }

        private static StackAnalysisResult AnalyzeStack(
            BytecodeBuilder builder,
            PythonOpcode[] opcodes,
            int[] operands,
            ImmutableArray<EmitDiagnostic>.Builder diagnostics)
        {
            var instructions = builder.Instructions;
            if (instructions.Count == 0)
                return new StackAnalysisResult(0, [0]);

            var depths = new int[instructions.Count + 1];
            Array.Fill(depths, int.MinValue);
            depths[0] = 0;
            var work = new Queue<int>();
            work.Enqueue(0);
            var maximum = 0;

            while (work.Count != 0)
            {
                var index = work.Dequeue();
                if (index == instructions.Count)
                    continue;

                var depth = depths[index];
                var instruction = instructions[index];
                var opcode = opcodes[index];
                var operand = operands[index];

                foreach (var region in builder.ExceptionRegions)
                {
                    if (region.Start.InstructionIndex != index)
                        continue;

                    var unwindDepth = checked(depth + region.StackDepthAdjustment);
                    if (unwindDepth < 0)
                    {
                        diagnostics.Add(new EmitDiagnostic(
                            EmitDiagnosticCode.InvalidBytecode,
                            EmitDiagnosticSeverity.Error,
                            instruction.SourceSpan,
                            "An exception handler unwinds below the Python value-stack base."));
                        continue;
                    }

                    var handlerDepth = checked(
                        unwindDepth + 1 + (region.PreserveLastInstruction ? 1 : 0));
                    Propagate(
                        region.Handler.InstructionIndex,
                        handlerDepth,
                        instruction,
                        depths,
                        work,
                        diagnostics,
                        ref maximum);
                }

                if (instruction.JumpKind is
                    BytecodeJumpKind.ConditionalForward or
                    BytecodeJumpKind.ForIteratorForward or
                    BytecodeJumpKind.SendForward)
                {
                    var effect = GetStackEffect(opcode, operand, jumpTaken: true);
                    var branchTarget = instruction.JumpKind == BytecodeJumpKind.ForIteratorForward
                        ? checked(instruction.Target!.InstructionIndex + 1)
                        : instruction.Target!.InstructionIndex;
                    Propagate(
                        branchTarget,
                        checked(depth + effect),
                        instruction,
                        depths,
                        work,
                        diagnostics,
                        ref maximum);
                }

                if (instruction.JumpKind is BytecodeJumpKind.Unconditional or BytecodeJumpKind.UnconditionalNoInterrupt)
                {
                    var effect = GetStackEffect(opcode, operand, jumpTaken: true);
                    Propagate(
                        instruction.Target!.InstructionIndex,
                        checked(depth + effect),
                        instruction,
                        depths,
                        work,
                        diagnostics,
                        ref maximum);
                    continue;
                }

                if (CPython3146OpcodeProfile.IsTerminator(opcode))
                    continue;

                var fallthroughEffect = GetStackEffect(opcode, operand, jumpTaken: false);
                Propagate(
                    index + 1,
                    checked(depth + fallthroughEffect),
                    instruction,
                    depths,
                    work,
                    diagnostics,
                    ref maximum);
            }

            return new StackAnalysisResult(maximum, depths);
        }

        private readonly struct ExceptionTableSegment
        {
            public ExceptionTableSegment(
                int startInstruction,
                int endInstruction,
                BytecodeExceptionRegion region)
            {
                StartInstruction = startInstruction;
                EndInstruction = endInstruction;
                Region = region;
            }

            public int StartInstruction { get; }
            public int EndInstruction { get; }
            public BytecodeExceptionRegion Region { get; }
        }

        private static ImmutableArray<byte> AssembleExceptionTable(
            BytecodeBuilder builder,
            int[] offsets,
            int[] stackDepths,
            ImmutableArray<EmitDiagnostic>.Builder diagnostics)
        {
            if (builder.ExceptionRegions.Count == 0)
                return [];

            var omittedRegions = new HashSet<BytecodeExceptionRegion>();
            foreach (var region in builder.ExceptionRegions)
            {
                if (stackDepths[region.Start.InstructionIndex] != int.MinValue)
                    continue;

                var hasReachableInstruction = false;
                for (var instruction = region.Start.InstructionIndex;
                     instruction < region.End.InstructionIndex;
                     instruction++)
                {
                    if (stackDepths[instruction] == int.MinValue)
                        continue;
                    hasReachableInstruction = true;
                    break;
                }

                omittedRegions.Add(region);
                if (hasReachableInstruction)
                {
                    diagnostics.Add(new EmitDiagnostic(
                        EmitDiagnosticCode.InvalidBytecode,
                        EmitDiagnosticSeverity.Error,
                        default,
                        "An exception region is entered without passing through its start label."));
                }
            }

            var boundaries = new SortedSet<int>();
            foreach (var region in builder.ExceptionRegions)
            {
                if (omittedRegions.Contains(region))
                    continue;
                boundaries.Add(region.Start.InstructionIndex);
                boundaries.Add(region.End.InstructionIndex);
                foreach (var exclusion in region.Exclusions)
                {
                    boundaries.Add(exclusion.Start.InstructionIndex);
                    boundaries.Add(exclusion.End.InstructionIndex);
                }
            }

            var points = new List<int>(boundaries);
            var segments = new List<ExceptionTableSegment>();
            for (var index = 0; index + 1 < points.Count; index++)
            {
                var start = points[index];
                var end = points[index + 1];
                BytecodeExceptionRegion? selected = null;
                foreach (var candidate in builder.ExceptionRegions)
                {
                    if (omittedRegions.Contains(candidate) ||
                        candidate.Start.InstructionIndex > start || candidate.End.InstructionIndex < end ||
                        IsExcluded(candidate, start, end))
                    {
                        continue;
                    }

                    if (selected is null || IsMoreSpecific(candidate, selected))
                        selected = candidate;
                }

                if (selected is null)
                    continue;

                if (segments.Count != 0)
                {
                    var previous = segments[^1];
                    if (previous.EndInstruction == start && ReferenceEquals(previous.Region, selected))
                    {
                        segments[^1] = new ExceptionTableSegment(
                            previous.StartInstruction,
                            end,
                            selected);
                        continue;
                    }
                }

                segments.Add(new ExceptionTableSegment(start, end, selected));
            }

            var bytes = ImmutableArray.CreateBuilder<byte>(checked(segments.Count * 8));
            foreach (var segment in segments)
            {
                var region = segment.Region;
                var normalDepth = stackDepths[region.Start.InstructionIndex];
                var unwindDepth = checked(normalDepth + region.StackDepthAdjustment);
                if (unwindDepth < 0)
                {
                    diagnostics.Add(new EmitDiagnostic(
                        EmitDiagnosticCode.InvalidBytecode,
                        EmitDiagnosticSeverity.Error,
                        default,
                        "An exception-table entry has a negative unwind depth."));
                    continue;
                }

                var start = offsets[segment.StartInstruction];
                var end = offsets[segment.EndInstruction];
                var target = offsets[region.Handler.InstructionIndex];
                if (end <= start || start >= (1 << 30) || end - start >= (1 << 30) ||
                    target >= (1 << 30) || unwindDepth >= (1 << 29))
                {
                    diagnostics.Add(new EmitDiagnostic(
                        EmitDiagnosticCode.OperandOutOfRange,
                        EmitDiagnosticSeverity.Error,
                        default,
                        "An exception-table entry exceeds CPython's 30-bit field limits."));
                    continue;
                }

                WriteExceptionTableItem(bytes, start, markEntryStart: true);
                WriteExceptionTableItem(bytes, end - start, markEntryStart: false);
                WriteExceptionTableItem(bytes, target, markEntryStart: false);
                WriteExceptionTableItem(
                    bytes,
                    checked((unwindDepth << 1) | (region.PreserveLastInstruction ? 1 : 0)),
                    markEntryStart: false);
            }

            return bytes.ToImmutable();
        }

        private static bool IsExcluded(
            BytecodeExceptionRegion region,
            int start,
            int end)
        {
            foreach (var exclusion in region.Exclusions)
            {
                if (exclusion.Start.InstructionIndex <= start && exclusion.End.InstructionIndex >= end)
                    return true;
            }
            return false;
        }

        private static bool IsMoreSpecific(
            BytecodeExceptionRegion candidate,
            BytecodeExceptionRegion current)
        {
            if (candidate.Start.InstructionIndex != current.Start.InstructionIndex)
                return candidate.Start.InstructionIndex > current.Start.InstructionIndex;
            return candidate.End.InstructionIndex < current.End.InstructionIndex;
        }

        private static void WriteExceptionTableItem(
            ImmutableArray<byte>.Builder bytes,
            int value,
            bool markEntryStart)
        {
            if (value < 0 || value >= (1 << 30))
                throw new ArgumentOutOfRangeException(nameof(value));

            var highestShift = 0;
            for (var shift = 24; shift >= 6; shift -= 6)
            {
                if (value >= (1 << shift))
                {
                    highestShift = shift;
                    break;
                }
            }

            for (var shift = highestShift; shift >= 0; shift -= 6)
            {
                var current = (byte)((value >> shift) & 0x3F);
                if (shift != 0)
                    current |= 0x40;
                if (markEntryStart)
                {
                    current |= 0x80;
                    markEntryStart = false;
                }
                bytes.Add(current);
            }
        }

        private static void Propagate(
            int targetIndex,
            int depth,
            BytecodeInstruction source,
            int[] depths,
            Queue<int> work,
            ImmutableArray<EmitDiagnostic>.Builder diagnostics,
            ref int maximum)
        {
            if (depth < 0)
            {
                diagnostics.Add(new EmitDiagnostic(
                    EmitDiagnosticCode.InvalidBytecode,
                    EmitDiagnosticSeverity.Error,
                    source.SourceSpan,
                    "The emitted instruction stream underflows the Python value stack."));
                return;
            }

            if (depth > maximum)
                maximum = depth;

            var previous = depths[targetIndex];
            if (previous == int.MinValue)
            {
                depths[targetIndex] = depth;
                work.Enqueue(targetIndex);
                return;
            }

            if (previous != depth)
            {
                diagnostics.Add(new EmitDiagnostic(
                    EmitDiagnosticCode.InvalidBytecode,
                    EmitDiagnosticSeverity.Error,
                    source.SourceSpan,
                    $"Control-flow paths reach instruction {targetIndex} with incompatible stack depths {previous} and {depth}."));
            }
        }

        private static int GetStackEffect(PythonOpcode opcode, int operand, bool jumpTaken) => opcode switch
        {
            PythonOpcode.Resume or
            PythonOpcode.Nop or
            PythonOpcode.NotTaken or
            PythonOpcode.JumpForward or
            PythonOpcode.JumpBackward or
            PythonOpcode.JumpBackwardNoInterrupt or
            PythonOpcode.SetupAnnotations or
            PythonOpcode.MakeCell or
            PythonOpcode.CopyFreeVariables or
            PythonOpcode.GetYieldFromIterator or
            PythonOpcode.Send => 0,

            PythonOpcode.LoadConstant or
            PythonOpcode.LoadSmallInteger or
            PythonOpcode.LoadName or
            PythonOpcode.LoadGlobal or
            PythonOpcode.LoadFast or
            PythonOpcode.LoadFastCheck or
            PythonOpcode.LoadFastBorrow or
            PythonOpcode.LoadFastAndClear or
            PythonOpcode.LoadDereference or
            PythonOpcode.LoadLocals or
            PythonOpcode.LoadBuildClass or
            PythonOpcode.LoadCommonConstant or
            PythonOpcode.PushNull => 1,

            PythonOpcode.ReturnGenerator => 1,

            PythonOpcode.StoreName or
            PythonOpcode.StoreGlobal or
            PythonOpcode.StoreFast or
            PythonOpcode.StoreDereference or
            PythonOpcode.PopTop or
            PythonOpcode.ReturnValue or
            PythonOpcode.EndSend => -1,

            PythonOpcode.PopIterator => -1,

            PythonOpcode.DeleteName or
            PythonOpcode.DeleteGlobal or
            PythonOpcode.DeleteFast or
            PythonOpcode.DeleteDereference => 0,

            PythonOpcode.Copy => 1,
            PythonOpcode.Swap => 0,
            PythonOpcode.UnaryNegative or
            PythonOpcode.UnaryInvert or
            PythonOpcode.UnaryNot or
            PythonOpcode.ToBoolean or
            PythonOpcode.CallIntrinsic1 or
            PythonOpcode.ConvertValue or
            PythonOpcode.FormatSimple or
            PythonOpcode.YieldValue => 0,

            PythonOpcode.GetIterator => 0,

            PythonOpcode.BinaryOperation or
            PythonOpcode.CompareOperation or
            PythonOpcode.ContainsOperation or
            PythonOpcode.IsOperation or
            PythonOpcode.CallIntrinsic2 or
            PythonOpcode.FormatWithSpec => -1,

            PythonOpcode.BuildTuple or
            PythonOpcode.BuildList or
            PythonOpcode.BuildSet or
            PythonOpcode.BuildString => 1 - operand,

            PythonOpcode.BuildMap => 1 - checked(operand * 2),
            PythonOpcode.BuildSlice => 1 - operand,
            PythonOpcode.BuildTemplate => -1,
            PythonOpcode.BuildInterpolation => -1 - (operand & 1),

            PythonOpcode.LoadSuperAttribute => (operand & 1) == 0 ? -2 : -1,
            PythonOpcode.LoadAttribute => (operand & 1) == 0 ? 0 : 1,
            PythonOpcode.LoadFromDictionaryOrDereference or
            PythonOpcode.LoadFromDictionaryOrGlobals => 0,
            PythonOpcode.StoreAttribute => -2,
            PythonOpcode.DeleteAttribute => -1,
            PythonOpcode.BinarySlice => -2,
            PythonOpcode.StoreSlice => -4,
            PythonOpcode.StoreSubscript => -3,
            PythonOpcode.DeleteSubscript => -2,

            PythonOpcode.Call => -operand - 1,
            PythonOpcode.CallKeyword => -operand - 2,
            PythonOpcode.CallFunctionEx => -3,
            PythonOpcode.MakeFunction => 0,
            PythonOpcode.SetFunctionAttribute => -1,

            PythonOpcode.PopJumpIfFalse or
            PythonOpcode.PopJumpIfTrue or
            PythonOpcode.PopJumpIfNone or
            PythonOpcode.PopJumpIfNotNone => -1,

            PythonOpcode.UnpackSequence => operand - 1,
            PythonOpcode.UnpackExtended => (operand & 0xFF) + (operand >> 8),

            PythonOpcode.ImportName => -1,
            PythonOpcode.ImportFrom => 1,

            PythonOpcode.GetLength => 1,
            PythonOpcode.ForIterator => jumpTaken ? 0 : 1,
            PythonOpcode.EndFor => -1,

            PythonOpcode.ListAppend or
            PythonOpcode.SetAdd => -1,
            PythonOpcode.MapAdd => -2,
            PythonOpcode.ListExtend or
            PythonOpcode.SetUpdate or
            PythonOpcode.DictionaryUpdate or
            PythonOpcode.DictionaryMerge => -1,

            PythonOpcode.RaiseVariableArguments => -operand,
            PythonOpcode.Reraise => -1,
            PythonOpcode.PushExceptionInfo => 1,
            PythonOpcode.PopExcept => -1,
            PythonOpcode.CheckExceptionMatch => 0,

            _ => throw new NotSupportedException($"Stack effect for opcode {opcode} is not implemented."),
        };

        private static bool HasErrors(ImmutableArray<EmitDiagnostic>.Builder diagnostics)
        {
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Severity == EmitDiagnosticSeverity.Error)
                    return true;
            }

            return false;
        }
    }

    public readonly struct PythonDisassembledInstruction
    {
        public PythonDisassembledInstruction(
            int offset,
            PythonOpcode opcode,
            int operand,
            int inlineCacheEntries)
        {
            Offset = offset;
            Opcode = opcode;
            Operand = operand;
            InlineCacheEntries = inlineCacheEntries;
        }

        public int Offset { get; }
        public PythonOpcode Opcode { get; }
        public int Operand { get; }
        public int InlineCacheEntries { get; }
    }

    public static class PythonBytecode
    {
        public static ImmutableArray<PythonDisassembledInstruction> Disassemble(PythonCodeObject codeObject)
        {
            ArgumentNullException.ThrowIfNull(codeObject);
            if (codeObject.Version != PythonBytecodeVersion.CPython3_14_6)
                throw new NotSupportedException($"Unsupported bytecode version {codeObject.Version}.");

            var code = codeObject.Bytecode;
            var result = ImmutableArray.CreateBuilder<PythonDisassembledInstruction>();
            var offset = 0;
            var accumulated = 0;

            while (offset < code.Length / 2)
            {
                var byteOffset = offset * 2;
                var opcode = (PythonOpcode)code[byteOffset];
                var argument = code[byteOffset + 1];

                if (opcode == PythonOpcode.ExtendedArgument)
                {
                    accumulated = checked((accumulated << 8) | argument);
                    offset++;
                    continue;
                }

                var fullArgument = checked((accumulated << 8) | argument);
                accumulated = 0;
                var caches = CPython3146OpcodeProfile.GetInlineCacheEntries(opcode);
                result.Add(new PythonDisassembledInstruction(offset, opcode, fullArgument, caches));
                offset = checked(offset + 1 + caches);
            }

            if (accumulated != 0)
                throw new InvalidOperationException("The code stream ends with an incomplete EXTENDED_ARG chain.");

            return result.ToImmutable();
        }
    }
}
