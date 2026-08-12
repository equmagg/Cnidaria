using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Cnidaria.Cs;

namespace Cnidaria.C
{
    public sealed class LSRAOptions
    {
        public static LSRAOptions Default { get; } = new LSRAOptions();

        public ImmutableArray<MachineRegister> GeneralRegisters { get; }
        public ImmutableArray<MachineRegister> FloatingRegisters { get; }
        public ImmutableArray<MachineRegister> VectorRegisters { get; }

        public int StackAlignment { get; }
        public int SpillSlotSize { get; }
        public int SpillSlotAlignment { get; }
        public int StackArgumentSlotSize { get; }

        public LSRAOptions(
            ImmutableArray<MachineRegister> generalRegisters = default,
            ImmutableArray<MachineRegister> floatingRegisters = default,
            ImmutableArray<MachineRegister> vectorRegisters = default,
            int stackAlignment = 16,
            int spillSlotSize = 8,
            int spillSlotAlignment = 8,
            int stackArgumentSlotSize = 8)
        {
            GeneralRegisters = generalRegisters.IsDefault
                ? ImmutableArray.Create(
                    MachineRegister.X18, MachineRegister.X19, MachineRegister.X20, MachineRegister.X21, MachineRegister.X22,
                    MachineRegister.X23, MachineRegister.X24, MachineRegister.X25, MachineRegister.X26, MachineRegister.X27)
                : generalRegisters;

            FloatingRegisters = floatingRegisters.IsDefault
                 ? ImmutableArray.Create(
                    MachineRegister.F18, MachineRegister.F19, MachineRegister.F20, MachineRegister.F21, MachineRegister.F22,
                    MachineRegister.F23, MachineRegister.F24, MachineRegister.F25, MachineRegister.F26, MachineRegister.F27)
                : floatingRegisters;

            VectorRegisters = vectorRegisters.IsDefault
                ? ImmutableArray<MachineRegister>.Empty
                : vectorRegisters;

            StackAlignment = stackAlignment <= 0 ? 16 : stackAlignment;
            SpillSlotSize = spillSlotSize <= 0 ? 8 : spillSlotSize;
            SpillSlotAlignment = spillSlotAlignment <= 0 ? 8 : spillSlotAlignment;
            StackArgumentSlotSize = stackArgumentSlotSize <= 0 ? 8 : stackArgumentSlotSize;
        }

        public static LSRAOptions ForTarget(TargetInfo? target)
        {
            target ??= TargetInfo.Default;
            return target.Architecture switch
            {
                TargetArchitectureKind.RiscV32 or TargetArchitectureKind.RiscV64 => CreateTargetOptions(target),
                TargetArchitectureKind.I386 or TargetArchitectureKind.X86_64 => CreateTargetOptions(target),
                TargetArchitectureKind.Arm32 or TargetArchitectureKind.Arm64 => CreateTargetOptions(target),
                _ => Default
            };
        }

        private static LSRAOptions CreateTargetOptions(TargetInfo target)
        {
            var registerSize = Math.Max(1, target.RegisterSize);
            return new LSRAOptions(
                generalRegisters: TargetRegisterInfo.AllocatableGeneralRegisters(target),
                floatingRegisters: TargetRegisterInfo.AllocatableFloatingRegisters(target),
                vectorRegisters: TargetRegisterInfo.AllocatableVectorRegisters(target),
                stackAlignment: target.Architecture == TargetArchitectureKind.Arm32 ? 8 : 16,
                spillSlotSize: registerSize,
                spillSlotAlignment: registerSize,
                stackArgumentSlotSize: registerSize);
        }
    }

    internal sealed class LinearScanRegisterAllocator
    {
        private readonly LirFunction _function;
        private readonly TargetInfo _target;
        private readonly LSRAOptions _options;
        private readonly Dictionary<LirVirtualRegister, List<LirVirtualRegister>> _copyPreferences = new();
        private readonly List<int> _callPositions = new();
        private readonly Dictionary<int, Dictionary<LirVirtualRegister, MachineRegister>> _callArgumentTargets = new();
        private readonly Dictionary<int, ImmutableHashSet<MachineRegister>> _instructionClobbers = new();
        private readonly List<InlineAssemblySite> _inlineAssemblySites = new();
        private readonly Dictionary<MachineRegister, int> _incomingRegisterReleasePositions = new();
        private readonly Dictionary<string, ImmutableArray<MachineRegister>> _incomingParameterRegisters = new(StringComparer.Ordinal);
        private readonly Dictionary<LirVirtualRegister, List<MachineRegister>> _abiPreferences = new();

        private LinearScanRegisterAllocator(LirFunction function, TargetInfo target, LSRAOptions? options)
        {
            _function = function ?? throw new ArgumentNullException(nameof(function));
            _target = target ?? TargetInfo.Default;
            _options = options ?? LSRAOptions.ForTarget(_target);
        }

        public static AllocationResult Allocate(LirFunction function, TargetInfo? target = null, LSRAOptions? options = null)
             => new LinearScanRegisterAllocator(function, target ?? TargetInfo.Default, options).Allocate();

        private AllocationResult Allocate()
        {
            _callPositions.Clear();
            _callArgumentTargets.Clear();
            _instructionClobbers.Clear();
            _inlineAssemblySites.Clear();
            _incomingRegisterReleasePositions.Clear();
            _abiPreferences.Clear();
            BuildIncomingParameterRegisters();
            var intervals = BuildIntervals();
            var allocations = new Dictionary<LirVirtualRegister, VirtualRegisterAllocation>();

            foreach (var interval in intervals.Values)
            {
                if (RequiresStackBackedScalar(interval.Register))
                    allocations[interval.Register] = VirtualRegisterAllocation.Spilled(interval.Register, interval.Register.RegisterClass);
            }

            AllocateClasses(intervals, new[] { LirRegisterClass.General, LirRegisterClass.Address }, _options.GeneralRegisters, allocations, _copyPreferences, _abiPreferences, _target, _callPositions, _callArgumentTargets, _instructionClobbers, _inlineAssemblySites, _incomingRegisterReleasePositions);
            AllocateClasses(intervals, new[] { LirRegisterClass.Floating }, _options.FloatingRegisters, allocations, _copyPreferences, _abiPreferences, _target, _callPositions, _callArgumentTargets, _instructionClobbers, _inlineAssemblySites, _incomingRegisterReleasePositions);
            AllocateClasses(intervals, new[] { LirRegisterClass.Vector }, _options.VectorRegisters, allocations, _copyPreferences, _abiPreferences, _target, _callPositions, _callArgumentTargets, _instructionClobbers, _inlineAssemblySites, _incomingRegisterReleasePositions);

            foreach (var interval in intervals.Values.OrderBy(static i => i.Register.Ordinal))
            {
                if (allocations.ContainsKey(interval.Register))
                    continue;

                if (interval.Register.RegisterClass is LirRegisterClass.Void or LirRegisterClass.Memory)
                    continue;

                allocations.Add(interval.Register, VirtualRegisterAllocation.Spilled(interval.Register, interval.Register.RegisterClass));
            }

            var frame = LayoutStackFrame(allocations);
            foreach (var pair in allocations.ToArray())
            {
                if (!pair.Value.IsSpilled)
                    continue;

                if (!frame.SpillOffsets.TryGetValue(pair.Key, out var offset))
                    throw new InvalidOperationException("Missing spill slot for " + pair.Key.Name + ".");

                allocations[pair.Key] = pair.Value.WithStackOffset(offset);
            }

            return new AllocationResult(_function, allocations, frame);
        }

        private Dictionary<LirVirtualRegister, LiveInterval> BuildIntervals()
        {
            var intervals = new Dictionary<LirVirtualRegister, LiveInterval>();
            var blockRanges = new Dictionary<LirBlock, BlockRange>();
            var blockUses = new Dictionary<LirBlock, HashSet<LirVirtualRegister>>();
            var blockDefs = new Dictionary<LirBlock, HashSet<LirVirtualRegister>>();
            var blockFirstUses = new Dictionary<LirBlock, Dictionary<LirVirtualRegister, int>>();
            var blockLastUses = new Dictionary<LirBlock, Dictionary<LirVirtualRegister, int>>();
            var blockFirstDefs = new Dictionary<LirBlock, Dictionary<LirVirtualRegister, int>>();
            var liveIn = new Dictionary<LirBlock, HashSet<LirVirtualRegister>>();
            var liveOut = new Dictionary<LirBlock, HashSet<LirVirtualRegister>>();
            var position = 0;

            foreach (var block in _function.Blocks)
            {
                var start = position;
                var uses = new HashSet<LirVirtualRegister>();
                var defs = new HashSet<LirVirtualRegister>();
                var firstUses = new Dictionary<LirVirtualRegister, int>();
                var lastUses = new Dictionary<LirVirtualRegister, int>();
                var firstDefs = new Dictionary<LirVirtualRegister, int>();

                foreach (var instruction in block.Instructions)
                {
                    var pos = position;
                    position += 2;
                    RecordInstructionClobbers(instruction, pos);
                    if (instruction.Kind == LirInstructionKind.Call && !IsRiscVVectorIntrinsicCall(instruction))
                    {
                        _callPositions.Add(pos);
                        RecordCallArgumentTargets(instruction, pos);
                    }
                    else if (instruction.Kind == LirInstructionKind.InlineAssembly)
                        _inlineAssemblySites.Add(CreateInlineAssemblySite(instruction, pos));
                    else if (instruction.Kind == LirInstructionKind.Parameter)
                        RecordIncomingRegisterRelease(instruction, pos);
                    RecordCopyPreferences(instruction);
                    RecordAbiPreferences(instruction);
                    VisitInstructionUses(
                        instruction,
                        pos,
                        intervals,
                        register =>
                        {
                            if (!firstUses.ContainsKey(register))
                                firstUses.Add(register, pos);
                            lastUses[register] = pos;
                            if (!defs.Contains(register))
                                uses.Add(register);
                        });

                    var definitionPosition = DefinitionPosition(instruction, pos);
                    VisitInstructionDefinitions(
                        instruction,
                        definitionPosition,
                        intervals,
                        register =>
                        {
                            defs.Add(register);
                            if (!firstDefs.ContainsKey(register))
                                firstDefs.Add(register, definitionPosition);
                        });
                }

                blockRanges.Add(block, new BlockRange(start, position));
                blockUses.Add(block, uses);
                blockDefs.Add(block, defs);
                blockFirstUses.Add(block, firstUses);
                blockLastUses.Add(block, lastUses);
                blockFirstDefs.Add(block, firstDefs);
                liveIn.Add(block, new HashSet<LirVirtualRegister>());
                liveOut.Add(block, new HashSet<LirVirtualRegister>());
            }

            ComputeBlockLiveness(blockUses, blockDefs, liveIn, liveOut);

            foreach (var block in _function.Blocks)
            {
                if (!blockRanges.TryGetValue(block, out var range))
                    continue;

                var registers = new HashSet<LirVirtualRegister>(liveIn[block]);
                registers.UnionWith(liveOut[block]);
                registers.UnionWith(blockFirstUses[block].Keys);
                registers.UnionWith(blockFirstDefs[block].Keys);

                foreach (var register in registers)
                {
                    if (!ShouldTrack(register))
                        continue;

                    if (!intervals.TryGetValue(register, out var interval))
                    {
                        interval = new LiveInterval(register, range.Start, range.Start);
                        intervals.Add(register, interval);
                    }

                    int start;
                    if (liveIn[block].Contains(register))
                    {
                        start = range.Start;
                    }
                    else if (blockFirstDefs[block].TryGetValue(register, out var firstDef))
                    {
                        start = firstDef;
                    }
                    else if (blockFirstUses[block].TryGetValue(register, out var firstUse))
                    {
                        start = firstUse;
                    }
                    else
                    {
                        continue;
                    }

                    int end;
                    if (liveOut[block].Contains(register))
                    {
                        end = range.End;
                    }
                    else if (blockLastUses[block].TryGetValue(register, out var lastUse))
                    {
                        end = lastUse + 1;
                    }
                    else
                    {
                        end = start;
                    }

                    interval.AddRange(start, Math.Max(start, end));
                }
            }

            foreach (var register in _function.VirtualRegisters)
            {
                if (!ShouldTrack(register) || intervals.ContainsKey(register))
                    continue;

                intervals.Add(register, new LiveInterval(register, 0, 0));
            }

            return intervals;
        }


        private static int DefinitionPosition(LirInstruction instruction, int position)
            => instruction.Kind is LirInstructionKind.Copy or LirInstructionKind.ParallelCopy
                ? position + 1
                : position;

        private void RecordIncomingRegisterRelease(LirInstruction instruction, int position)
        {
            if (!_incomingParameterRegisters.TryGetValue(instruction.Operator, out var registers))
                return;

            foreach (var register in registers)
            {
                if (_incomingRegisterReleasePositions.TryGetValue(register, out var existing))
                    _incomingRegisterReleasePositions[register] = Math.Max(existing, position);
                else
                    _incomingRegisterReleasePositions.Add(register, position);
            }
        }

        private void BuildIncomingParameterRegisters()
        {
            _incomingParameterRegisters.Clear();
            var functionType = _function.Symbol?.FunctionType;
            if (functionType is null)
                return;

            var cursor = new AbiCursor();
            if (CAbi.RequiresHiddenReturnBuffer(_target, functionType.ReturnType))
                _ = CAbi.AssignHiddenReturnBufferLocation(_target, ref cursor, _options.StackArgumentSlotSize);

            foreach (var parameter in functionType.Parameters)
            {
                var value = CAbi.ClassifyValue(_target, parameter.Type, isReturn: false, isVariadicUnnamedArgument: false);
                var locations = CAbi.AssignArgumentLocations(value, ref cursor, _options.StackArgumentSlotSize);
                var registers = ImmutableArray.CreateBuilder<MachineRegister>();
                foreach (var location in locations)
                {
                    if (location.Kind == AbiLocationKind.Register && location.Register != MachineRegister.Invalid && !registers.Contains(location.Register))
                        registers.Add(location.Register);
                }

                _incomingParameterRegisters[parameter.Name] = registers.ToImmutable();
            }
        }

        private void RecordInstructionClobbers(LirInstruction instruction, int position)
        {
            var clobbers = ImmutableHashSet.CreateBuilder<MachineRegister>();

            if (_target.Architecture == TargetArchitectureKind.X86_64 &&
                !TargetRegisterInfo.IsWindowsX64(_target) &&
                instruction.Kind == LirInstructionKind.Call &&
                instruction.CallSignature is { IsVariadic: true })
            {
                clobbers.Add(MachineRegister.X0);
            }

            if (_target.Architecture == TargetArchitectureKind.RiscV32 &&
                instruction.Kind == LirInstructionKind.Binary &&
                instruction.Operator is "/" or "%" &&
                instruction.Result is not null &&
                (instruction.Result.RegisterClass is LirRegisterClass.General or LirRegisterClass.Address) &&
                Math.Max(1, _target.SizeOf(instruction.Result.Type)) > Math.Max(1, _target.RegisterSize))
            {
                clobbers.Add(MachineRegister.X10);
                clobbers.Add(MachineRegister.X11);
                clobbers.Add(MachineRegister.X12);
                clobbers.Add(MachineRegister.X13);
            }

            if (clobbers.Count != 0)
                _instructionClobbers[position] = clobbers.ToImmutable();
        }

        private void RecordCallArgumentTargets(LirInstruction instruction, int position)
        {
            var targets = new Dictionary<LirVirtualRegister, MachineRegister>();
            var cursor = new AbiCursor();
            if (instruction.Result is not null && CAbi.RequiresHiddenReturnBuffer(_target, instruction.Result.Type))
                _ = CAbi.AssignHiddenReturnBufferLocation(_target, ref cursor, _options.StackArgumentSlotSize);

            var signature = instruction.CallSignature;
            for (var i = 1; i < instruction.Operands.Length; i++)
            {
                var operand = instruction.Operands[i];
                var isVariadicUnnamed = signature is not null && signature.IsVariadic && i - 1 >= signature.Parameters.Length;
                var value = CAbi.ClassifyValue(_target, operand.Type, isReturn: false, isVariadicUnnamed);
                var locations = CAbi.AssignArgumentLocations(value, ref cursor, _options.StackArgumentSlotSize);
                if (operand.Kind != LirOperandKind.Register || operand.Register is null || !ShouldTrack(operand.Register))
                    continue;

                var target = SingleCompatibleArgumentRegister(locations, operand.Register.RegisterClass);
                if (target == MachineRegister.Invalid)
                {
                    targets[operand.Register] = MachineRegister.Invalid;
                    continue;
                }

                if (targets.TryGetValue(operand.Register, out var existing) && existing != target)
                    targets[operand.Register] = MachineRegister.Invalid;
                else
                    targets[operand.Register] = target;
                AddAbiPreference(operand.Register, target);
            }

            _callArgumentTargets[position] = targets;
        }

        private static MachineRegister SingleCompatibleArgumentRegister(ImmutableArray<AbiLocation> locations, LirRegisterClass registerClass)
        {
            if (locations.Length != 1 || locations[0].Kind != AbiLocationKind.Register)
                return MachineRegister.Invalid;

            var register = locations[0].Register;
            var physicalClass = MachineRegisters.GetClass(register);
            return registerClass switch
            {
                LirRegisterClass.General or LirRegisterClass.Address when physicalClass == RegisterClass.General => register,
                LirRegisterClass.Floating when physicalClass == RegisterClass.Float => register,
                LirRegisterClass.Vector when physicalClass == RegisterClass.Vector => register,
                _ => MachineRegister.Invalid,
            };
        }

        private void RecordAbiPreferences(LirInstruction instruction)
        {
            if (instruction.Kind == LirInstructionKind.Parameter && instruction.Result is not null &&
                _incomingParameterRegisters.TryGetValue(instruction.Operator, out var incomingRegisters))
            {
                foreach (var register in incomingRegisters)
                    AddAbiPreference(instruction.Result, register);
            }

            if (instruction.Kind == LirInstructionKind.Call && !IsRiscVVectorIntrinsicCall(instruction) && instruction.Result is not null)
            {
                var value = CAbi.ClassifyValue(_target, instruction.Result.Type, isReturn: true, isVariadicUnnamedArgument: false);
                if (value.PassingKind != AbiPassingKind.Indirect && value.Segments.Length != 0)
                    AddAbiPreference(instruction.Result, CAbi.ReturnRegister(value.Segments[0], 0));
            }

            if (instruction.Kind != LirInstructionKind.Return || instruction.Operands.Length == 0)
                return;

            var operand = instruction.Operands[0];
            if (operand.Kind != LirOperandKind.Register || operand.Register is null)
                return;

            var returnType = _function.Symbol?.FunctionType?.ReturnType ?? operand.Type;
            var returnValue = CAbi.ClassifyValue(_target, returnType, isReturn: true, isVariadicUnnamedArgument: false);
            if (returnValue.PassingKind != AbiPassingKind.Indirect && returnValue.Segments.Length != 0)
                AddAbiPreference(operand.Register, CAbi.ReturnRegister(returnValue.Segments[0], 0));
        }

        private void AddAbiPreference(LirVirtualRegister register, MachineRegister physicalRegister)
        {
            if (physicalRegister == MachineRegister.Invalid || !ShouldTrack(register))
                return;

            if (!_abiPreferences.TryGetValue(register, out var preferences))
            {
                preferences = new List<MachineRegister>();
                _abiPreferences.Add(register, preferences);
            }

            if (!preferences.Contains(physicalRegister))
                preferences.Add(physicalRegister);
        }

        private InlineAssemblySite CreateInlineAssemblySite(LirInstruction instruction, int position)
        {
            var reserved = ImmutableHashSet.CreateBuilder<MachineRegister>();
            var touched = ImmutableHashSet.CreateBuilder<MachineRegister>();
            if (instruction.SourceStatement is not GimpleAsmStatement asmStatement)
                return new InlineAssemblySite(position, reserved.ToImmutable(), touched.ToImmutable());

            ReserveInlineAssemblyTemplateRegisters(asmStatement.Text, reserved, touched);

            foreach (var output in asmStatement.Outputs)
            {
                if (output.Target is null ||
                    InlineAsmConstraints.PreferredStorage(output.Constraint, output.Target.Type) != InlineAsmOperandStorage.Register)
                {
                    continue;
                }

                ReserveInlineAssemblyOperand(reserved, touched, output.Constraint, output.Target.Type);
            }

            foreach (var input in asmStatement.Inputs)
            {
                if (input.Value is null ||
                    InlineAsmConstraints.PreferredStorage(input.Constraint, input.Value.Type) != InlineAsmOperandStorage.Register ||
                    InlineAsmConstraints.MatchingOperand(input.Constraint) is not null)
                {
                    continue;
                }

                ReserveInlineAssemblyOperand(reserved, touched, input.Constraint, input.Value.Type);
            }

            foreach (var clobber in asmStatement.Clobbers)
            {
                if (string.Equals(clobber, "memory", StringComparison.Ordinal) ||
                    string.Equals(clobber, "cc", StringComparison.Ordinal) ||
                    string.Equals(clobber, "redzone", StringComparison.Ordinal))
                {
                    continue;
                }

                if (TryParseInlineAssemblyRegister(clobber, out var register))
                    AddInlineAssemblyRegister(reserved, touched, register);
            }

            return new InlineAssemblySite(position, reserved.ToImmutable(), touched.ToImmutable());
        }

        private void ReserveInlineAssemblyTemplateRegisters(
            string text,
            ImmutableHashSet<MachineRegister>.Builder reserved,
            ImmutableHashSet<MachineRegister>.Builder touched)
        {
            if (!_target.IsRiscV || string.IsNullOrEmpty(text))
                return;

            var start = -1;
            for (var i = 0; i <= text.Length; i++)
            {
                var isIdentifier = i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_');
                if (isIdentifier)
                {
                    if (start < 0)
                        start = i;
                    continue;
                }

                if (start < 0)
                    continue;

                var token = text.Substring(start, i - start);
                if (TryParseInlineAssemblyRegister(token, out var register))
                    AddInlineAssemblyRegister(reserved, touched, register);
                start = -1;
            }
        }

        private void ReserveInlineAssemblyOperand(
            ImmutableHashSet<MachineRegister>.Builder reserved,
            ImmutableHashSet<MachineRegister>.Builder touched,
            string constraint,
            QualifiedType type)
        {
            var explicitRegister = InlineAsmConstraints.ExplicitRegisterName(constraint);
            var registerClass = CAbi.PreferredLirRegisterClass(_target, type);
            if (explicitRegister is not null &&
                TargetRegisterInfo.TryParseExplicitRegister(_target, explicitRegister, registerClass, out var fixedRegister))
            {
                AddInlineAssemblyRegister(reserved, touched, fixedRegister);
                return;
            }

            var registers = registerClass switch
            {
                LirRegisterClass.General or LirRegisterClass.Address => _options.GeneralRegisters,
                LirRegisterClass.Floating => _options.FloatingRegisters,
                LirRegisterClass.Vector => _options.VectorRegisters,
                _ => ImmutableArray<MachineRegister>.Empty,
            };
            reserved.UnionWith(registers);
        }

        private void AddInlineAssemblyRegister(
            ImmutableHashSet<MachineRegister>.Builder reserved,
            ImmutableHashSet<MachineRegister>.Builder touched,
            MachineRegister register)
        {
            if (_target.Architecture == TargetArchitectureKind.Arm32 &&
                register >= MachineRegister.V0 && register <= MachineRegister.V15)
            {
                var vectorIndex = (int)register - (int)MachineRegister.V0;
                var firstDouble = (MachineRegister)((int)MachineRegister.F0 + checked(vectorIndex * 2));
                reserved.Add(firstDouble);
                touched.Add(firstDouble);
                var secondDouble = (MachineRegister)((int)firstDouble + 1);
                reserved.Add(secondDouble);
                touched.Add(secondDouble);
                return;
            }

            reserved.Add(register);
            touched.Add(register);
        }

        private bool TryParseInlineAssemblyRegister(string text, out MachineRegister register)
        {
            foreach (var registerClass in new[]
            {
                LirRegisterClass.General,
                LirRegisterClass.Address,
                LirRegisterClass.Vector,
                LirRegisterClass.Floating,
            })
            {
                if (TargetRegisterInfo.TryParseExplicitRegister(_target, text, registerClass, out register))
                    return true;
            }

            register = MachineRegister.Invalid;
            return false;
        }

        private static void ComputeBlockLiveness(
            IReadOnlyDictionary<LirBlock, HashSet<LirVirtualRegister>> blockUses,
            IReadOnlyDictionary<LirBlock, HashSet<LirVirtualRegister>> blockDefs,
            Dictionary<LirBlock, HashSet<LirVirtualRegister>> liveIn,
            Dictionary<LirBlock, HashSet<LirVirtualRegister>> liveOut)
        {
            var blocks = liveIn.Keys.ToArray();
            var changed = true;
            while (changed)
            {
                changed = false;

                for (var blockIndex = blocks.Length - 1; blockIndex >= 0; blockIndex--)
                {
                    var block = blocks[blockIndex];
                    var newOut = new HashSet<LirVirtualRegister>();
                    foreach (var successor in SuccessorsOf(block))
                    {
                        if (!liveIn.TryGetValue(successor, out var successorLiveIn))
                            continue;

                        foreach (var register in successorLiveIn)
                            newOut.Add(register);
                    }

                    var newIn = new HashSet<LirVirtualRegister>(blockUses[block]);
                    foreach (var register in newOut)
                    {
                        if (!blockDefs[block].Contains(register))
                            newIn.Add(register);
                    }

                    if (!liveOut[block].SetEquals(newOut))
                    {
                        liveOut[block] = newOut;
                        changed = true;
                    }

                    if (!liveIn[block].SetEquals(newIn))
                    {
                        liveIn[block] = newIn;
                        changed = true;
                    }
                }
            }
        }

        private static IEnumerable<LirBlock> SuccessorsOf(LirBlock block)
        {
            if (block.Instructions.Length == 0)
                yield break;

            var seenInlineAsmLabels = new HashSet<LirBlock>();
            foreach (var instruction in block.Instructions)
            {
                if (instruction.Kind != LirInstructionKind.InlineAssembly)
                    continue;

                foreach (var operand in instruction.Operands)
                {
                    if (operand.Kind == LirOperandKind.Label && operand.Label is not null && seenInlineAsmLabels.Add(operand.Label))
                        yield return operand.Label;
                }
            }

            var terminator = block.Instructions[block.Instructions.Length - 1];
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
                    var seen = new HashSet<LirBlock>();
                    if (terminator.Target is not null && seen.Add(terminator.Target))
                        yield return terminator.Target;
                    foreach (var @case in terminator.SwitchCases)
                    {
                        if (seen.Add(@case.Target))
                            yield return @case.Target;
                    }
                    break;

                case LirInstructionKind.InlineAssembly:
                    if (terminator.Target is not null)
                        yield return terminator.Target;
                    break;
            }
        }

        private void RecordCopyPreferences(LirInstruction instruction)
        {
            if (instruction.Kind == LirInstructionKind.Copy &&
                instruction.Result is not null &&
                instruction.Operands.Length != 0 &&
                instruction.Operands[0].Kind == LirOperandKind.Register &&
                instruction.Operands[0].Register is not null)
            {
                AddCopyPreference(instruction.Result, instruction.Operands[0].Register!);
            }

            if (instruction.Kind != LirInstructionKind.ParallelCopy)
                return;

            foreach (var copy in instruction.ParallelCopies)
            {
                if (copy.Source.Kind == LirOperandKind.Register && copy.Source.Register is not null)
                    AddCopyPreference(copy.Destination, copy.Source.Register);
            }
        }

        private void AddCopyPreference(LirVirtualRegister destination, LirVirtualRegister source)
        {
            if (!ShouldTrack(destination) || !ShouldTrack(source))
                return;

            if (!AreCoalescableClasses(destination.RegisterClass, source.RegisterClass))
                return;

            if (!_copyPreferences.TryGetValue(destination, out var list))
            {
                list = new List<LirVirtualRegister>();
                _copyPreferences.Add(destination, list);
            }

            if (!list.Contains(source))
                list.Add(source);
        }

        private static bool AreCoalescableClasses(LirRegisterClass left, LirRegisterClass right)
        {
            if (left == right)
                return true;

            return IsGeneralLikeClass(left) && IsGeneralLikeClass(right);
        }

        private static bool IsGeneralLikeClass(LirRegisterClass registerClass)
            => registerClass is LirRegisterClass.General or LirRegisterClass.Address;

        private static void VisitInstructionUses(
            LirInstruction instruction,
            int position,
            Dictionary<LirVirtualRegister, LiveInterval> intervals,
            Action<LirVirtualRegister>? onUse = null)
        {
            foreach (var operand in instruction.Operands)
                VisitOperand(operand, position, intervals, onUse);

            if (instruction.Address is not null)
                VisitAddress(instruction.Address, position, intervals, onUse);

            foreach (var copy in instruction.ParallelCopies)
                VisitOperand(copy.Source, position, intervals, onUse);

            foreach (var @case in instruction.SwitchCases)
                VisitOperand(@case.Value, position, intervals, onUse);
        }

        private static void VisitInstructionDefinitions(
            LirInstruction instruction,
            int position,
            Dictionary<LirVirtualRegister, LiveInterval> intervals,
            Action<LirVirtualRegister>? onDefinition = null)
        {
            if (instruction.Result is not null)
                Touch(intervals, instruction.Result, position, isUse: false, onTouch: onDefinition);

            foreach (var copy in instruction.ParallelCopies)
                Touch(intervals, copy.Destination, position, isUse: false, onTouch: onDefinition);
        }

        private static void VisitOperand(
            LirOperand operand,
            int position,
            Dictionary<LirVirtualRegister, LiveInterval> intervals,
            Action<LirVirtualRegister>? onUse = null)
        {
            switch (operand.Kind)
            {
                case LirOperandKind.Register:
                    if (operand.Register is not null)
                        Touch(intervals, operand.Register, position, isUse: true, onTouch: onUse);
                    break;

                case LirOperandKind.Address:
                    if (operand.Address is not null)
                        VisitAddress(operand.Address, position, intervals, onUse);
                    break;
            }
        }

        private static void VisitAddress(
            LirAddress address,
            int position,
            Dictionary<LirVirtualRegister, LiveInterval> intervals,
            Action<LirVirtualRegister>? onUse = null)
        {
            if (address.BaseOperand is not null)
                VisitOperand(address.BaseOperand, position, intervals, onUse);
            if (address.BaseAddress is not null)
                VisitAddress(address.BaseAddress, position, intervals, onUse);
            if (address.Index is not null)
                VisitOperand(address.Index, position, intervals, onUse);
        }

        private static void Touch(
            Dictionary<LirVirtualRegister, LiveInterval> intervals,
            LirVirtualRegister register,
            int position,
            bool isUse,
            Action<LirVirtualRegister>? onTouch = null)
        {
            if (!ShouldTrack(register))
                return;

            if (!intervals.TryGetValue(register, out var interval))
            {
                interval = new LiveInterval(register, position, position);
                intervals.Add(register, interval);
            }

            interval.Start = Math.Min(interval.Start, position);
            interval.End = Math.Max(interval.End, position + (isUse ? 1 : 0));
            if (isUse)
                interval.AddUse(position);
            else
                interval.AddDefinition(position);
            onTouch?.Invoke(register);
        }

        private static bool ShouldTrack(LirVirtualRegister register)
            => register.RegisterClass is not (LirRegisterClass.Void or LirRegisterClass.Memory);

        private bool RequiresStackBackedScalar(LirVirtualRegister register)
        {
            if (_target.IsRegisterBytecode)
                return false;
            if (register.RegisterClass is
                LirRegisterClass.Void or
                LirRegisterClass.Memory or
                LirRegisterClass.Aggregate or
                LirRegisterClass.Vector)
                return false;
            if (CAbi.IsAggregate(register.Type))
                return false;
            if (register.Type.Type.Kind is TypeKind.Pointer or TypeKind.Array or TypeKind.Function)
                return false;
            if (CAbi.UsesHardwareFloatingRegister(_target, register.Type, isVariadicUnnamedArgument: false))
                return false;
            return Math.Max(1, _target.SizeOf(register.Type)) > Math.Max(1, _target.RegisterSize);
        }

        private static void AllocateClasses(
            Dictionary<LirVirtualRegister, LiveInterval> allIntervals,
            IReadOnlyCollection<LirRegisterClass> registerClasses,
            ImmutableArray<MachineRegister> physicalRegisters,
            Dictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
            IReadOnlyDictionary<LirVirtualRegister, List<LirVirtualRegister>> copyPreferences,
            IReadOnlyDictionary<LirVirtualRegister, List<MachineRegister>> abiPreferences,
            TargetInfo target,
            IReadOnlyList<int> callPositions,
            IReadOnlyDictionary<int, Dictionary<LirVirtualRegister, MachineRegister>> callArgumentTargets,
            IReadOnlyDictionary<int, ImmutableHashSet<MachineRegister>> instructionClobbers,
            IReadOnlyList<InlineAssemblySite> inlineAssemblySites,
            IReadOnlyDictionary<MachineRegister, int> incomingRegisterReleasePositions)
        {
            if (physicalRegisters.IsDefaultOrEmpty)
                return;

            var intervals = allIntervals.Values
                .Where(i => !allocations.ContainsKey(i.Register) && registerClasses.Contains(i.Register.RegisterClass))
                .OrderBy(static i => i.Start)
                .ThenBy(static i => i.End)
                .ToList();

            var active = new List<LiveInterval>();
            var valueNumberPreferredRegisters = new Dictionary<ValueNumber, MachineRegister>();
            var callArgumentRegisters = CallArgumentRegisters(target, registerClasses);

            foreach (var interval in intervals)
            {
                ExpireOldIntervals(interval, active, allocations);

                var allowedRegisters = AllowedRegistersForInterval(interval, physicalRegisters, abiPreferences, callArgumentRegisters, target, callPositions, callArgumentTargets, instructionClobbers, inlineAssemblySites, incomingRegisterReleasePositions);
                if (allowedRegisters.IsDefaultOrEmpty)
                {
                    allocations[interval.Register] = VirtualRegisterAllocation.Spilled(interval.Register, interval.Register.RegisterClass);
                    continue;
                }

                if (ActiveRegisterCount(interval, active, allowedRegisters) == allowedRegisters.Length)
                {
                    SpillAtInterval(interval, active, allocations, valueNumberPreferredRegisters, allowedRegisters);
                }
                else
                {
                    var reg = PreferredFreeRegister(interval, allowedRegisters, active, allocations, copyPreferences, abiPreferences, valueNumberPreferredRegisters);
                    interval.PhysicalRegister = reg;
                    allocations[interval.Register] = VirtualRegisterAllocation.InRegister(interval.Register, reg);
                    RememberValueNumberRegister(interval, reg, valueNumberPreferredRegisters);
                    InsertActive(active, interval);
                }
            }
        }

        private static ImmutableArray<MachineRegister> AllowedRegistersForInterval(
            LiveInterval interval,
            ImmutableArray<MachineRegister> physicalRegisters,
            IReadOnlyDictionary<LirVirtualRegister, List<MachineRegister>> abiPreferences,
            ImmutableArray<MachineRegister> callArgumentRegisters,
            TargetInfo target,
            IReadOnlyList<int> callPositions,
            IReadOnlyDictionary<int, Dictionary<LirVirtualRegister, MachineRegister>> callArgumentTargets,
            IReadOnlyDictionary<int, ImmutableHashSet<MachineRegister>> instructionClobbers,
            IReadOnlyList<InlineAssemblySite> inlineAssemblySites,
            IReadOnlyDictionary<MachineRegister, int> incomingRegisterReleasePositions)
        {
            if (interval.Register.HasFixedRegister)
                return ImmutableArray.Create(interval.Register.FixedRegister);

            var spansCall = IntervalSpansPosition(interval, callPositions);
            var safeCallArgumentRegister = SafeCallArgumentRegister(interval, callPositions, callArgumentTargets, out var liveBeforeCall);
            var builder = ImmutableArray.CreateBuilder<MachineRegister>();
            foreach (var register in physicalRegisters)
            {
                if (incomingRegisterReleasePositions.TryGetValue(register, out var releasePosition) &&
                    interval.Start <= releasePosition &&
                    !(interval.Start == releasePosition && HasRegisterPreference(interval.Register, register, abiPreferences)))
                    continue;
                if (spansCall && !CanUseRegisterAcrossCall(target, interval, register))
                    continue;
                if (liveBeforeCall && ContainsRegister(callArgumentRegisters, register) &&
                    (target.IsRiscV || register != safeCallArgumentRegister))
                    continue;
                if (IsClobberedAtInstruction(interval, register, instructionClobbers))
                    continue;

                var reserved = false;
                foreach (var site in inlineAssemblySites)
                {
                    var conflictsWithSite = IntervalSpansPosition(interval, site.Position) ||
                        (target.IsRiscV && interval.IsLiveBefore(site.Position));
                    if (!conflictsWithSite)
                        continue;
                    if (!site.ReservedRegisters.Contains(register))
                        continue;

                    reserved = true;
                    break;
                }

                if (!reserved)
                    builder.Add(register);
            }

            return builder.ToImmutable();
        }

        private static bool IsClobberedAtInstruction(
            LiveInterval interval,
            MachineRegister register,
            IReadOnlyDictionary<int, ImmutableHashSet<MachineRegister>> instructionClobbers)
        {
            foreach (var pair in instructionClobbers)
            {
                if (pair.Key < interval.Start)
                    continue;
                if (pair.Key >= interval.End)
                    continue;
                if (interval.IsLiveBefore(pair.Key) && pair.Value.Contains(register))
                    return true;
            }

            return false;
        }

        private static bool HasRegisterPreference(
            LirVirtualRegister register,
            MachineRegister physicalRegister,
            IReadOnlyDictionary<LirVirtualRegister, List<MachineRegister>> preferences)
        {
            if (!preferences.TryGetValue(register, out var registers))
                return false;

            foreach (var preferred in registers)
            {
                if (preferred == physicalRegister)
                    return true;
            }

            return false;
        }

        private static ImmutableArray<MachineRegister> CallArgumentRegisters(TargetInfo target, IReadOnlyCollection<LirRegisterClass> registerClasses)
        {
            if (target.IsArm)
                return ImmutableArray<MachineRegister>.Empty;
            if (registerClasses.Contains(LirRegisterClass.General) || registerClasses.Contains(LirRegisterClass.Address))
                return TargetRegisterInfo.IntegerArgumentRegisters(target);
            if (registerClasses.Contains(LirRegisterClass.Floating))
                return TargetRegisterInfo.FloatingArgumentRegisters(target);
            if (registerClasses.Contains(LirRegisterClass.Vector))
                return TargetRegisterInfo.VectorArgumentRegisters(target);
            return ImmutableArray<MachineRegister>.Empty;
        }

        private static bool CanUseRegisterAcrossCall(TargetInfo target, LiveInterval interval, MachineRegister register)
        {
            if (!TargetRegisterInfo.IsCalleeSaved(target, register))
                return false;
            if (target.Architecture == TargetArchitectureKind.Arm64 &&
                interval.Register.RegisterClass == LirRegisterClass.Vector &&
                Math.Max(1, target.SizeOf(interval.Register.Type)) > 8)
            {
                return false;
            }
            return true;
        }

        private static bool IntervalSpansPosition(LiveInterval interval, IReadOnlyList<int> positions)
        {
            foreach (var position in positions)
            {
                if (position < interval.Start)
                    continue;
                if (position >= interval.End)
                    return false;
                if (interval.IsLiveAcross(position))
                    return true;
            }

            return false;
        }

        private static bool IntervalSpansPosition(LiveInterval interval, int position)
            => interval.Spans(position);

        private static MachineRegister SafeCallArgumentRegister(
            LiveInterval interval,
            IReadOnlyList<int> positions,
            IReadOnlyDictionary<int, Dictionary<LirVirtualRegister, MachineRegister>> callArgumentTargets,
            out bool liveBeforeCall)
        {
            liveBeforeCall = false;
            var safeRegister = MachineRegister.Invalid;
            foreach (var position in positions)
            {
                if (position < interval.Start)
                    continue;
                if (position >= interval.End)
                    break;
                if (!interval.IsLiveBefore(position))
                    continue;

                liveBeforeCall = true;
                if (!callArgumentTargets.TryGetValue(position, out var targets) ||
                    !targets.TryGetValue(interval.Register, out var target) ||
                    target == MachineRegister.Invalid)
                {
                    return MachineRegister.Invalid;
                }

                if (safeRegister == MachineRegister.Invalid)
                    safeRegister = target;
                else if (safeRegister != target)
                    return MachineRegister.Invalid;
            }

            return safeRegister;
        }

        private static int ActiveRegisterCount(LiveInterval current, List<LiveInterval> active, ImmutableArray<MachineRegister> registers)
        {
            var count = 0;
            foreach (var register in registers)
            {
                foreach (var interval in active)
                {
                    if (interval.PhysicalRegister != register || !interval.Overlaps(current))
                        continue;
                    count++;
                    break;
                }
            }

            return count;
        }

        private static bool ContainsRegister(ImmutableArray<MachineRegister> registers, MachineRegister register)
        {
            foreach (var candidate in registers)
            {
                if (candidate == register)
                    return true;
            }

            return false;
        }

        private static void ExpireOldIntervals(LiveInterval current, List<LiveInterval> active, Dictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations)
        {
            for (var i = active.Count - 1; i >= 0; i--)
            {
                if (active[i].End > current.Start)
                    continue;

                active.RemoveAt(i);
            }

            active.Sort(static (a, b) => a.End.CompareTo(b.End));
        }

        private static MachineRegister PreferredFreeRegister(
            LiveInterval interval,
            ImmutableArray<MachineRegister> physicalRegisters,
            List<LiveInterval> active,
            IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
            IReadOnlyDictionary<LirVirtualRegister, List<LirVirtualRegister>> copyPreferences,
            IReadOnlyDictionary<LirVirtualRegister, List<MachineRegister>> abiPreferences,
            IReadOnlyDictionary<ValueNumber, MachineRegister> valueNumberPreferredRegisters)
        {
            if (interval.Register.HasFixedRegister && IsFreeRegister(interval, interval.Register.FixedRegister, physicalRegisters, active))
                return interval.Register.FixedRegister;

            if (abiPreferences.TryGetValue(interval.Register, out var preferredRegisters))
            {
                foreach (var preferred in preferredRegisters)
                {
                    if (IsFreeRegister(interval, preferred, physicalRegisters, active))
                        return preferred;
                }
            }

            if (copyPreferences.TryGetValue(interval.Register, out var preferredSources))
            {
                for (var i = preferredSources.Count - 1; i >= 0; i--)
                {
                    var source = preferredSources[i];
                    if (!allocations.TryGetValue(source, out var sourceAllocation) || sourceAllocation.IsSpilled)
                        continue;

                    var preferred = sourceAllocation.PhysicalRegister;
                    if (IsFreeRegister(interval, preferred, physicalRegisters, active))
                        return preferred;
                }
            }

            var valueNumber = interval.Register.ValueNumber;
            if (valueNumber is not null &&
                !valueNumber.IsMemoryDependent &&
                !valueNumber.IsUnique &&
                valueNumberPreferredRegisters.TryGetValue(valueNumber, out var valueNumberRegister) &&
                IsFreeRegister(interval, valueNumberRegister, physicalRegisters, active))
            {
                return valueNumberRegister;
            }

            return FirstFreeRegister(interval, physicalRegisters, active);
        }

        private static void RememberValueNumberRegister(
            LiveInterval interval,
            MachineRegister register,
            Dictionary<ValueNumber, MachineRegister> valueNumberPreferredRegisters)
        {
            var valueNumber = interval.Register.ValueNumber;
            if (valueNumber is null || valueNumber.IsMemoryDependent || valueNumber.IsUnique || register == MachineRegister.Invalid)
                return;

            valueNumberPreferredRegisters[valueNumber] = register;
        }

        private static bool IsFreeRegister(LiveInterval current, MachineRegister register, ImmutableArray<MachineRegister> physicalRegisters, List<LiveInterval> active)
        {
            if (register == MachineRegister.Invalid)
                return false;

            var isAllocatable = false;
            foreach (var physicalRegister in physicalRegisters)
            {
                if (physicalRegister == register)
                {
                    isAllocatable = true;
                    break;
                }
            }

            if (!isAllocatable)
                return false;

            foreach (var interval in active)
            {
                if (interval.PhysicalRegister == register && interval.Overlaps(current))
                    return false;
            }

            return true;
        }

        private static MachineRegister FirstFreeRegister(LiveInterval current, ImmutableArray<MachineRegister> physicalRegisters, List<LiveInterval> active)
        {
            foreach (var register in physicalRegisters)
            {
                var used = false;
                foreach (var interval in active)
                {
                    if (interval.PhysicalRegister == register && interval.Overlaps(current))
                    {
                        used = true;
                        break;
                    }
                }

                if (!used)
                    return register;
            }

            return MachineRegister.Invalid;
        }

        private static void SpillAtInterval(
            LiveInterval current,
            List<LiveInterval> active,
            Dictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
            Dictionary<ValueNumber, MachineRegister> valueNumberPreferredRegisters,
            ImmutableArray<MachineRegister> allowedRegisters)
        {
            List<LiveInterval>? spill = null;
            var spillRegister = MachineRegister.Invalid;
            var spillNextUse = -1;
            var spillEnd = -1;
            foreach (var register in allowedRegisters)
            {
                var conflicts = new List<LiveInterval>();
                var hasFixedConflict = false;
                var nearestNextUse = int.MaxValue;
                var farthestEnd = -1;
                foreach (var candidate in active)
                {
                    if (candidate.PhysicalRegister != register || !candidate.Overlaps(current))
                        continue;
                    if (candidate.Register.HasFixedRegister)
                    {
                        hasFixedConflict = true;
                        break;
                    }

                    conflicts.Add(candidate);
                    nearestNextUse = Math.Min(nearestNextUse, candidate.NextUseAtOrAfter(current.Start));
                    farthestEnd = Math.Max(farthestEnd, candidate.End);
                }

                if (hasFixedConflict || conflicts.Count == 0)
                    continue;

                if (spill is null ||
                    nearestNextUse > spillNextUse ||
                    nearestNextUse == spillNextUse && conflicts.Count < spill.Count ||
                    nearestNextUse == spillNextUse && conflicts.Count == spill.Count && farthestEnd > spillEnd)
                {
                    spill = conflicts;
                    spillRegister = register;
                    spillNextUse = nearestNextUse;
                    spillEnd = farthestEnd;
                }
            }

            if (spill is null)
            {
                if (current.Register.HasFixedRegister)
                    throw new InvalidOperationException("Conflicting fixed-register live intervals for " + current.Register.Name + ".");
                allocations[current.Register] = VirtualRegisterAllocation.Spilled(current.Register, current.Register.RegisterClass);
                return;
            }

            var currentNextUse = current.NextUseAtOrAfter(current.Start);
            if (current.Register.HasFixedRegister ||
                spillNextUse > currentNextUse ||
                spillNextUse == currentNextUse && spillEnd > current.End)
            {
                current.PhysicalRegister = spillRegister;
                allocations[current.Register] = VirtualRegisterAllocation.InRegister(current.Register, current.PhysicalRegister);
                RememberValueNumberRegister(current, current.PhysicalRegister, valueNumberPreferredRegisters);
                foreach (var spilledInterval in spill)
                {
                    allocations[spilledInterval.Register] = VirtualRegisterAllocation.Spilled(spilledInterval.Register, spilledInterval.Register.RegisterClass);
                    active.Remove(spilledInterval);
                }
                InsertActive(active, current);
                return;
            }

            allocations[current.Register] = VirtualRegisterAllocation.Spilled(current.Register, current.Register.RegisterClass);
        }

        private static void InsertActive(List<LiveInterval> active, LiveInterval interval)
        {
            active.Add(interval);
            active.Sort(static (a, b) => a.End.CompareTo(b.End));
        }

        private StackFrameMap LayoutStackFrame(Dictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations)
        {
            var offset = 0;

            var outgoingSize = ComputeOutgoingArgumentAreaSize();
            offset = AlignUp(offset, _options.StackArgumentSlotSize);
            var outgoingOffset = offset;
            offset = checked(offset + outgoingSize);

            var varArgsPointerOffset = -1;
            if (_function.Symbol?.FunctionType?.IsVariadic == true)
            {
                offset = AlignUp(offset, _target.PointerAlignment);
                varArgsPointerOffset = offset;
                offset = checked(offset + _target.PointerSize);
            }

            var hiddenReturnBufferOffset = -1;
            if (RequiresHiddenReturnBuffer())
            {
                offset = AlignUp(offset, _target.PointerAlignment);
                hiddenReturnBufferOffset = offset;
                offset = checked(offset + _target.PointerSize);
            }

            var stackSlotOffsets = new Dictionary<LirStackSlot, int>();
            var stackSlotAreaOffset = offset;
            foreach (var slot in _function.StackSlots.OrderBy(static s => s.Ordinal))
            {
                var align = Math.Max(1, slot.Alignment);
                offset = AlignUp(offset, align);
                stackSlotOffsets.Add(slot, offset);
                offset = checked(offset + Math.Max(1, slot.Size));
            }
            var stackSlotAreaSize = checked(offset - stackSlotAreaOffset);

            var spillOffsets = new Dictionary<LirVirtualRegister, int>();
            var spillAreaOffset = offset;
            foreach (var pair in allocations.OrderBy(static p => p.Key.Ordinal))
            {
                if (!pair.Value.IsSpilled)
                    continue;

                offset = AlignUp(offset, SpillSlotAlignmentFor(pair.Key));
                spillOffsets.Add(pair.Key, offset);
                offset = checked(offset + SpillSlotSizeFor(pair.Key));
            }
            var spillAreaSize = checked(offset - spillAreaOffset);

            var parallelCopyTempSize = ComputeParallelCopyTempSize(allocations, spillOffsets);
            offset = AlignUp(offset, _options.StackAlignment);
            var parallelCopyTempOffset = offset;
            offset = checked(offset + parallelCopyTempSize);
            offset = AlignUp(offset, _options.SpillSlotAlignment);
            var floatingImmediateTempOffset = offset;
            var floatingImmediateTempMinimum = _target.Architecture == TargetArchitectureKind.I386 ? 32 : 8;
            var floatingImmediateTempSize = AlignUp(Math.Max(floatingImmediateTempMinimum, _options.SpillSlotSize), _options.SpillSlotAlignment);
            offset = checked(offset + floatingImmediateTempSize);

            var usedRegisters = allocations.Values
                .Where(static a => !a.IsSpilled && a.PhysicalRegister != MachineRegister.Invalid)
                .Select(static a => a.PhysicalRegister)
                .Concat(_inlineAssemblySites.SelectMany(static site => site.TouchedRegisters))
                .Where(register => TargetRegisterInfo.IsCalleeSaved(_target, register))
                .Distinct()
                .OrderBy(static r => (int)r)
                .ToImmutableArray();

            var savedRegisterOffsets = new Dictionary<MachineRegister, int>();
            var savedRegisterAreaOffset = offset;
            foreach (var register in usedRegisters)
            {
                var saveAlignment = Math.Max(1, Math.Min(RegisterSaveSize(register), _options.StackAlignment));
                offset = AlignUp(offset, saveAlignment);
                savedRegisterOffsets.Add(register, offset);
                offset = checked(offset + RegisterSaveSize(register));
            }
            var savedRegisterAreaSize = checked(offset - savedRegisterAreaOffset);

            var frameSize = AlignUp(offset, _options.StackAlignment);
            return new StackFrameMap(
                frameSize,
                _options.StackAlignment,
                outgoingOffset,
                outgoingSize,
                stackSlotAreaOffset,
                stackSlotAreaSize,
                varArgsPointerOffset,
                varArgsPointerOffset >= 0 ? _target.PointerSize : 0,
                hiddenReturnBufferOffset,
                hiddenReturnBufferOffset >= 0 ? _target.PointerSize : 0,
                spillAreaOffset,
                spillAreaSize,
                parallelCopyTempOffset,
                parallelCopyTempSize,
                floatingImmediateTempOffset,
                floatingImmediateTempSize,
                savedRegisterAreaOffset,
                savedRegisterAreaSize,
                stackSlotOffsets,
                spillOffsets,
                savedRegisterOffsets);
        }

        private bool RequiresHiddenReturnBuffer()
        {
            var returnType = _function.Symbol?.FunctionType?.ReturnType;
            return returnType.HasValue && CAbi.RequiresHiddenReturnBuffer(_target, returnType.Value);
        }

        private int ComputeOutgoingArgumentAreaSize()
        {
            var maxSize = 0;
            foreach (var instruction in _function.Blocks.SelectMany(static b => b.Instructions))
            {
                if (instruction.Kind != LirInstructionKind.Call || IsRiscVVectorIntrinsicCall(instruction))
                    continue;

                var size = CAbi.ComputeOutgoingArgumentAreaSize(
                    instruction,
                    startOperand: 1,
                    _target,
                    _options.StackArgumentSlotSize,
                    includeVariadicHomeArea: true);
                maxSize = Math.Max(maxSize, size);
            }

            return maxSize;
        }

        private bool IsRiscVVectorIntrinsicCall(LirInstruction instruction)
        {
            if (!_target.IsRiscV || instruction.Kind != LirInstructionKind.Call || instruction.Operands.Length == 0)
                return false;

            var callee = instruction.Operands[0];
            return callee.Kind == LirOperandKind.Symbol &&
                callee.Symbol is FunctionSymbol function &&
                function.Name.StartsWith("__riscv_v", StringComparison.Ordinal);
        }

        private int ComputeParallelCopyTempSize(
            IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
            IReadOnlyDictionary<LirVirtualRegister, int> spillOffsets)
        {
            var maxSize = 0;
            foreach (var instruction in _function.Blocks.SelectMany(static b => b.Instructions))
            {
                if (instruction.Kind == LirInstructionKind.InlineAssembly)
                    maxSize = Math.Max(maxSize, ComputeInlineAssemblyTempSize(instruction));

                if (instruction.Kind != LirInstructionKind.ParallelCopy)
                    continue;

                var physicalCopies = ImmutableArray.CreateBuilder<LirParallelCopy>(instruction.ParallelCopies.Length);
                foreach (var copy in instruction.ParallelCopies)
                {
                    if (RequiresPhysicalParallelCopy(copy, allocations, spillOffsets))
                        physicalCopies.Add(copy);
                }

                if (physicalCopies.Count <= 1)
                    continue;

                var copies = physicalCopies.ToImmutable();
                if (!HasBlockCopyParallelCopy(copies) && !HasPhysicalStorageClobber(copies, allocations, spillOffsets))
                    continue;

                var size = 0;
                foreach (var copy in copies)
                    size = checked(size + ParallelCopyTempSlotSize(copy));
                maxSize = Math.Max(maxSize, size);
            }

            return maxSize;
        }

        private int ComputeInlineAssemblyTempSize(LirInstruction instruction)
        {
            if (instruction.SourceStatement is not GimpleAsmStatement asmStatement)
                return 0;

            var registerOperandCount = 0;
            var maxSize = 0;
            foreach (var output in asmStatement.Outputs)
            {
                if (output.Target is null ||
                    InlineAsmConstraints.PreferredStorage(output.Constraint, output.Target.Type) != InlineAsmOperandStorage.Register)
                {
                    continue;
                }

                registerOperandCount++;
                maxSize = Math.Max(maxSize, InlineAssemblyTempSlotSize(output.Target.Type));
            }

            foreach (var input in asmStatement.Inputs)
            {
                if (input.Value is null ||
                    InlineAsmConstraints.PreferredStorage(input.Constraint, input.Value.Type) != InlineAsmOperandStorage.Register)
                {
                    continue;
                }

                registerOperandCount++;
                maxSize = Math.Max(maxSize, InlineAssemblyTempSlotSize(input.Value.Type));
            }

            return registerOperandCount > 1 ? maxSize : 0;
        }

        private int InlineAssemblyTempSlotSize(QualifiedType type)
            => AlignUp(Math.Max(_options.SpillSlotSize, SizeOfStorage(type)), _options.SpillSlotAlignment);

        private static bool RequiresPhysicalParallelCopy(
            LirParallelCopy copy,
            IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
            IReadOnlyDictionary<LirVirtualRegister, int> spillOffsets)
        {
            if (copy.Destination.RegisterClass is LirRegisterClass.Void or LirRegisterClass.Memory)
                return false;

            if (copy.Source.Kind is LirOperandKind.Void or LirOperandKind.None)
                return false;

            if (copy.Source.Kind == LirOperandKind.Register &&
                copy.Source.Register is { RegisterClass: LirRegisterClass.Void or LirRegisterClass.Memory })
            {
                return false;
            }

            return !ReferencesSamePhysicalStorage(copy.Source, copy.Destination, allocations, spillOffsets);
        }

        private bool HasBlockCopyParallelCopy(ImmutableArray<LirParallelCopy> copies)
        {
            foreach (var copy in copies)
            {
                if (copy.Destination.RegisterClass == LirRegisterClass.Aggregate || RequiresStackBackedScalar(copy.Destination))
                    return true;
            }

            return false;
        }

        private static bool HasPhysicalStorageClobber(
            ImmutableArray<LirParallelCopy> copies,
            IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
            IReadOnlyDictionary<LirVirtualRegister, int> spillOffsets)
        {
            for (var i = 0; i < copies.Length; i++)
            {
                var hasDestinationRegister = TryGetDestinationPhysicalRegister(copies[i].Destination, allocations, out var destinationRegister);
                var hasDestinationStackOffset = TryGetDestinationStackOffset(copies[i].Destination, allocations, spillOffsets, out var destinationStackOffset);
                if (!hasDestinationRegister && !hasDestinationStackOffset)
                    continue;

                for (var j = 0; j < copies.Length; j++)
                {
                    if (i == j && ReferencesSamePhysicalStorage(copies[j].Source, copies[i].Destination, allocations, spillOffsets))
                        continue;

                    if (hasDestinationRegister &&
                        TryGetOperandPhysicalRegister(copies[j].Source, allocations, out var sourceRegister) &&
                        sourceRegister == destinationRegister)
                    {
                        return true;
                    }

                    if (hasDestinationStackOffset &&
                        TryGetOperandStackOffset(copies[j].Source, allocations, spillOffsets, out var sourceStackOffset) &&
                        sourceStackOffset == destinationStackOffset)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool ReferencesSamePhysicalStorage(
            LirOperand source,
            LirVirtualRegister destination,
            IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
            IReadOnlyDictionary<LirVirtualRegister, int> spillOffsets)
        {
            if (source.Kind != LirOperandKind.Register || source.Register is null)
                return false;

            if (!allocations.TryGetValue(source.Register, out var sourceAllocation) ||
                !allocations.TryGetValue(destination, out var destinationAllocation))
            {
                return false;
            }

            if (!sourceAllocation.IsSpilled && !destinationAllocation.IsSpilled)
                return sourceAllocation.PhysicalRegister == destinationAllocation.PhysicalRegister;

            if (sourceAllocation.IsSpilled && destinationAllocation.IsSpilled &&
                spillOffsets.TryGetValue(source.Register, out var sourceOffset) &&
                spillOffsets.TryGetValue(destination, out var destinationOffset))
            {
                return sourceOffset == destinationOffset;
            }

            return false;
        }

        private static bool TryGetDestinationPhysicalRegister(
            LirVirtualRegister destination,
            IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
            out MachineRegister register)
        {
            register = MachineRegister.Invalid;
            if (!allocations.TryGetValue(destination, out var allocation) || allocation.IsSpilled)
                return false;

            register = allocation.PhysicalRegister;
            return register != MachineRegister.Invalid;
        }

        private static bool TryGetOperandPhysicalRegister(
            LirOperand operand,
            IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
            out MachineRegister register)
        {
            register = MachineRegister.Invalid;
            if (operand.Kind != LirOperandKind.Register || operand.Register is null)
                return false;

            if (!allocations.TryGetValue(operand.Register, out var allocation) || allocation.IsSpilled)
                return false;

            register = allocation.PhysicalRegister;
            return register != MachineRegister.Invalid;
        }

        private static bool TryGetDestinationStackOffset(
            LirVirtualRegister destination,
            IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
            IReadOnlyDictionary<LirVirtualRegister, int> spillOffsets,
            out int stackOffset)
        {
            stackOffset = -1;
            if (!allocations.TryGetValue(destination, out var allocation) || !allocation.IsSpilled)
                return false;

            return spillOffsets.TryGetValue(destination, out stackOffset) && stackOffset >= 0;
        }

        private static bool TryGetOperandStackOffset(
            LirOperand operand,
            IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
            IReadOnlyDictionary<LirVirtualRegister, int> spillOffsets,
            out int stackOffset)
        {
            stackOffset = -1;
            if (operand.Kind != LirOperandKind.Register || operand.Register is null)
                return false;

            if (!allocations.TryGetValue(operand.Register, out var allocation) || !allocation.IsSpilled)
                return false;

            return spillOffsets.TryGetValue(operand.Register, out stackOffset) && stackOffset >= 0;
        }

        private int ParallelCopyTempSlotSize(LirParallelCopy copy)
        {
            var size = Math.Max(SizeOfStorage(copy.Destination.Type), SizeOfStorage(copy.Source.Type));
            return AlignUp(Math.Max(_options.SpillSlotSize, size), _options.SpillSlotAlignment);
        }

        private int SpillSlotAlignmentFor(LirVirtualRegister register)
            => Math.Max(
                _options.SpillSlotAlignment,
                Math.Min(Math.Max(1, _target.AlignOf(register.Type)), _options.StackAlignment));

        private int SpillSlotSizeFor(LirVirtualRegister register)
            => AlignUp(Math.Max(_options.SpillSlotSize, SizeOfStorage(register.Type)), SpillSlotAlignmentFor(register));

        private int SizeOfStorage(QualifiedType type)
            => Math.Max(1, _target.SizeOf(type));

        private int RegisterSaveSize(MachineRegister register)
            => TargetRegisterInfo.RegisterSaveSize(_target, register, _options.SpillSlotSize);

        private static int AlignUp(int value, int alignment)
        {
            if (alignment <= 1)
                return value;
            var mask = alignment - 1;
            return checked((value + mask) & ~mask);
        }

        private readonly struct InlineAssemblySite
        {
            public int Position { get; }
            public ImmutableHashSet<MachineRegister> ReservedRegisters { get; }
            public ImmutableHashSet<MachineRegister> TouchedRegisters { get; }

            public InlineAssemblySite(
                int position,
                ImmutableHashSet<MachineRegister> reservedRegisters,
                ImmutableHashSet<MachineRegister> touchedRegisters)
            {
                Position = position;
                ReservedRegisters = reservedRegisters ?? ImmutableHashSet<MachineRegister>.Empty;
                TouchedRegisters = touchedRegisters ?? ImmutableHashSet<MachineRegister>.Empty;
            }
        }

        private readonly struct BlockRange
        {
            public int Start { get; }
            public int End { get; }

            public BlockRange(int start, int end)
            {
                Start = start;
                End = Math.Max(start, end);
            }
        }

        private sealed class LiveInterval
        {
            private readonly List<int> _usePositions = new List<int>();
            private readonly List<int> _definitionPositions = new List<int>();
            private readonly List<LiveRange> _ranges = new List<LiveRange>();

            public LirVirtualRegister Register { get; }
            public int Start { get; set; }
            public int End { get; set; }
            public MachineRegister PhysicalRegister { get; set; }

            public LiveInterval(LirVirtualRegister register, int start, int end)
            {
                Register = register ?? throw new ArgumentNullException(nameof(register));
                Start = start;
                End = Math.Max(start, end);
                PhysicalRegister = MachineRegister.Invalid;
            }

            public void AddRange(int start, int end)
            {
                end = Math.Max(start, end);
                Start = Math.Min(Start, start);
                End = Math.Max(End, end);

                if (_ranges.Count == 0)
                {
                    _ranges.Add(new LiveRange(start, end));
                    return;
                }

                var last = _ranges[_ranges.Count - 1];
                if (start <= last.End)
                {
                    _ranges[_ranges.Count - 1] = new LiveRange(last.Start, Math.Max(last.End, end));
                    return;
                }

                _ranges.Add(new LiveRange(start, end));
            }

            public void AddUse(int position)
            {
                if (_usePositions.Count == 0 || _usePositions[_usePositions.Count - 1] != position)
                    _usePositions.Add(position);
            }

            public void AddDefinition(int position)
            {
                if (_definitionPositions.Count == 0 || _definitionPositions[_definitionPositions.Count - 1] != position)
                    _definitionPositions.Add(position);
            }

            public bool Overlaps(LiveInterval other)
            {
                var left = 0;
                var right = 0;
                while (left < _ranges.Count && right < other._ranges.Count)
                {
                    var a = _ranges[left];
                    var b = other._ranges[right];
                    if (a.Start < b.End && b.Start < a.End)
                        return true;
                    if (a.End <= b.Start)
                        left++;
                    else
                        right++;
                }

                return false;
            }

            public bool Spans(int position)
            {
                foreach (var range in _ranges)
                {
                    if (position < range.Start)
                        return false;
                    if (position + 1 < range.End && position >= range.Start)
                        return true;
                }

                return false;
            }

            public bool IsLiveBefore(int position)
            {
                foreach (var range in _ranges)
                {
                    if (position < range.Start)
                        return false;
                    if (position >= range.End)
                        continue;
                    if (range.Start < position)
                        return true;
                    return !HasDefinitionAt(position);
                }

                return false;
            }

            public bool IsLiveAcross(int position)
            {
                foreach (var range in _ranges)
                {
                    if (position < range.Start)
                        return false;
                    if (position >= range.End)
                        continue;
                    if (range.End <= position + 1)
                        return false;
                    if (range.Start < position)
                        return true;
                    return !HasDefinitionAt(position);
                }

                return false;
            }

            private bool HasDefinitionAt(int position)
            {
                foreach (var definitionPosition in _definitionPositions)
                {
                    if (definitionPosition == position)
                        return true;
                    if (definitionPosition > position)
                        return false;
                }

                return false;
            }

            public int NextUseAtOrAfter(int position)
            {
                for (var i = 0; i < _usePositions.Count; i++)
                {
                    var use = _usePositions[i];
                    if (use >= position)
                        return use;
                }

                return int.MaxValue;
            }
        }

        private readonly struct LiveRange
        {
            public int Start { get; }
            public int End { get; }

            public LiveRange(int start, int end)
            {
                Start = start;
                End = Math.Max(start, end);
            }
        }

    }

    internal sealed class AllocationResult
    {
        private readonly IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> _allocations;

        public LirFunction Function { get; }
        public StackFrameMap Frame { get; }
        public IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> Allocations => _allocations;

        public ImmutableArray<MachineRegister> UsedPhysicalRegisters { get; }

        public AllocationResult(
            LirFunction function,
            IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
            StackFrameMap frame)
        {
            Function = function ?? throw new ArgumentNullException(nameof(function));
            _allocations = allocations ?? throw new ArgumentNullException(nameof(allocations));
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            UsedPhysicalRegisters = allocations.Values
                .Where(static a => !a.IsSpilled && a.PhysicalRegister != MachineRegister.Invalid)
                .Select(static a => a.PhysicalRegister)
                .Distinct()
                .OrderBy(static r => (int)r)
                .ToImmutableArray();
        }

        public VirtualRegisterAllocation this[LirVirtualRegister register]
            => _allocations.TryGetValue(register, out var allocation)
                ? allocation
                : throw new KeyNotFoundException("No physical allocation for " + register.Name + ".");

        public bool TryGetAllocation(LirVirtualRegister register, out VirtualRegisterAllocation allocation)
            => _allocations.TryGetValue(register, out allocation!);
    }

    internal sealed class VirtualRegisterAllocation
    {
        public LirVirtualRegister Register { get; }
        public LirRegisterClass RegisterClass { get; }
        public bool IsSpilled { get; }
        public MachineRegister PhysicalRegister { get; }
        public int StackOffset { get; }

        private VirtualRegisterAllocation(
            LirVirtualRegister register,
            LirRegisterClass registerClass,
            bool isSpilled,
            MachineRegister physicalRegister,
            int stackOffset)
        {
            Register = register ?? throw new ArgumentNullException(nameof(register));
            RegisterClass = registerClass;
            IsSpilled = isSpilled;
            PhysicalRegister = physicalRegister;
            StackOffset = stackOffset;
        }

        public static VirtualRegisterAllocation InRegister(LirVirtualRegister register, MachineRegister physicalRegister)
            => new VirtualRegisterAllocation(register, register.RegisterClass, isSpilled: false, physicalRegister, stackOffset: -1);

        public static VirtualRegisterAllocation Spilled(LirVirtualRegister register, LirRegisterClass registerClass)
            => new VirtualRegisterAllocation(register, registerClass, isSpilled: true, MachineRegister.Invalid, stackOffset: -1);

        public VirtualRegisterAllocation WithStackOffset(int stackOffset)
            => new VirtualRegisterAllocation(Register, RegisterClass, isSpilled: true, MachineRegister.Invalid, stackOffset);

        public override string ToString()
            => IsSpilled
                ? Register.Name + " -> [sp+" + StackOffset.ToString(CultureInfo.InvariantCulture) + "]"
                : Register.Name + " -> " + PhysicalRegister;
    }

    internal sealed class StackFrameMap
    {
        public int FrameSize { get; }
        public int FrameAlignment { get; }
        public int OutgoingArgumentAreaOffset { get; }
        public int OutgoingArgumentAreaSize { get; }
        public int StackSlotAreaOffset { get; }
        public int StackSlotAreaSize { get; }
        public int SpillAreaOffset { get; }
        public int SpillAreaSize { get; }
        public int ParallelCopyTempOffset { get; }
        public int ParallelCopyTempSize { get; }
        public int FloatingImmediateTempOffset { get; }
        public int FloatingImmediateTempSize { get; }
        public int SavedRegisterAreaOffset { get; }
        public int SavedRegisterAreaSize { get; }
        public int VarArgsPointerOffset { get; }
        public int VarArgsPointerSize { get; }
        public bool HasVarArgsPointer => VarArgsPointerOffset >= 0;
        public int HiddenReturnBufferOffset { get; }
        public int HiddenReturnBufferSize { get; }
        public bool HasHiddenReturnBuffer => HiddenReturnBufferOffset >= 0;
        public IReadOnlyDictionary<LirStackSlot, int> StackSlotOffsets { get; }
        public IReadOnlyDictionary<LirVirtualRegister, int> SpillOffsets { get; }
        public IReadOnlyDictionary<MachineRegister, int> SavedRegisterOffsets { get; }

        public StackFrameMap(
            int frameSize,
            int frameAlignment,
            int outgoingArgumentAreaOffset,
            int outgoingArgumentAreaSize,
            int stackSlotAreaOffset,
            int stackSlotAreaSize,
            int varArgsPointerOffset,
            int varArgsPointerSize,
            int hiddenReturnBufferOffset,
            int hiddenReturnBufferSize,
            int spillAreaOffset,
            int spillAreaSize,
            int parallelCopyTempOffset,
            int parallelCopyTempSize,
            int floatingImmediateTempOffset,
            int floatingImmediateTempSize,
            int savedRegisterAreaOffset,
            int savedRegisterAreaSize,
            IReadOnlyDictionary<LirStackSlot, int> stackSlotOffsets,
            IReadOnlyDictionary<LirVirtualRegister, int> spillOffsets,
            IReadOnlyDictionary<MachineRegister, int> savedRegisterOffsets)
        {
            FrameSize = frameSize;
            FrameAlignment = frameAlignment <= 0 ? 1 : frameAlignment;
            OutgoingArgumentAreaOffset = outgoingArgumentAreaOffset;
            OutgoingArgumentAreaSize = outgoingArgumentAreaSize;
            StackSlotAreaOffset = stackSlotAreaOffset;
            StackSlotAreaSize = stackSlotAreaSize;
            SpillAreaOffset = spillAreaOffset;
            SpillAreaSize = spillAreaSize;
            ParallelCopyTempOffset = parallelCopyTempOffset;
            ParallelCopyTempSize = parallelCopyTempSize;
            FloatingImmediateTempOffset = floatingImmediateTempOffset;
            FloatingImmediateTempSize = floatingImmediateTempSize;
            SavedRegisterAreaOffset = savedRegisterAreaOffset;
            SavedRegisterAreaSize = savedRegisterAreaSize;
            VarArgsPointerOffset = varArgsPointerOffset;
            VarArgsPointerSize = varArgsPointerSize;
            HiddenReturnBufferOffset = hiddenReturnBufferOffset;
            HiddenReturnBufferSize = hiddenReturnBufferSize;
            StackSlotOffsets = stackSlotOffsets ?? throw new ArgumentNullException(nameof(stackSlotOffsets));
            SpillOffsets = spillOffsets ?? throw new ArgumentNullException(nameof(spillOffsets));
            SavedRegisterOffsets = savedRegisterOffsets ?? throw new ArgumentNullException(nameof(savedRegisterOffsets));
        }
    }
}
