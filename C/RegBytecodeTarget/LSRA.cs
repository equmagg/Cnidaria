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

        public int StackAlignment { get; }
        public int SpillSlotSize { get; }
        public int SpillSlotAlignment { get; }
        public int StackArgumentSlotSize { get; }

        public LSRAOptions(
            ImmutableArray<MachineRegister> generalRegisters = default,
            ImmutableArray<MachineRegister> floatingRegisters = default,
            int stackAlignment = 16,
            int spillSlotSize = 8,
            int spillSlotAlignment = 8,
            int stackArgumentSlotSize = 8)
        {
            GeneralRegisters = generalRegisters.IsDefaultOrEmpty
                ? ImmutableArray.Create(
                    MachineRegister.X18, MachineRegister.X19, MachineRegister.X20, MachineRegister.X21, MachineRegister.X22,
                    MachineRegister.X23, MachineRegister.X24, MachineRegister.X25, MachineRegister.X26, MachineRegister.X27)
                : generalRegisters;

            FloatingRegisters = floatingRegisters.IsDefaultOrEmpty
                ? ImmutableArray.Create(
                    MachineRegister.F18, MachineRegister.F19, MachineRegister.F20, MachineRegister.F21, MachineRegister.F22,
                    MachineRegister.F23, MachineRegister.F24, MachineRegister.F25, MachineRegister.F26, MachineRegister.F27)
                : floatingRegisters;

            StackAlignment = stackAlignment <= 0 ? 16 : stackAlignment;
            SpillSlotSize = spillSlotSize <= 0 ? 8 : spillSlotSize;
            SpillSlotAlignment = spillSlotAlignment <= 0 ? 8 : spillSlotAlignment;
            StackArgumentSlotSize = stackArgumentSlotSize <= 0 ? 8 : stackArgumentSlotSize;
        }
    }

    internal sealed class LinearScanRegisterAllocator
    {
        private readonly LirFunction _function;
        private readonly TargetInfo _target;
        private readonly LSRAOptions _options;
        private readonly Dictionary<LirVirtualRegister, List<LirVirtualRegister>> _copyPreferences = new();

        private LinearScanRegisterAllocator(LirFunction function, TargetInfo target, LSRAOptions options)
        {
            _function = function ?? throw new ArgumentNullException(nameof(function));
            _target = target ?? TargetInfo.Default;
            _options = options ?? LSRAOptions.Default;
        }

        public static AllocationResult Allocate(LirFunction function, TargetInfo? target = null, LSRAOptions? options = null)
            => new LinearScanRegisterAllocator(function, target ?? TargetInfo.Default, options ?? LSRAOptions.Default).Allocate();

        private AllocationResult Allocate()
        {
            var intervals = BuildIntervals();
            var allocations = new Dictionary<LirVirtualRegister, VirtualRegisterAllocation>();

            AllocateClasses(intervals, new[] { LirRegisterClass.General, LirRegisterClass.Address }, _options.GeneralRegisters, allocations, _copyPreferences);
            AllocateClasses(intervals, new[] { LirRegisterClass.Floating }, _options.FloatingRegisters, allocations, _copyPreferences);

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
            var liveIn = new Dictionary<LirBlock, HashSet<LirVirtualRegister>>();
            var liveOut = new Dictionary<LirBlock, HashSet<LirVirtualRegister>>();
            var position = 0;

            foreach (var block in _function.Blocks)
            {
                var start = position;
                var uses = new HashSet<LirVirtualRegister>();
                var defs = new HashSet<LirVirtualRegister>();

                foreach (var instruction in block.Instructions)
                {
                    var pos = position++;
                    RecordCopyPreferences(instruction);
                    VisitInstructionUses(
                        instruction,
                        pos,
                        intervals,
                        register =>
                        {
                            if (!defs.Contains(register))
                                uses.Add(register);
                        });

                    VisitInstructionDefinitions(
                        instruction,
                        pos,
                        intervals,
                        register => defs.Add(register));
                }

                blockRanges.Add(block, new BlockRange(start, position));
                blockUses.Add(block, uses);
                blockDefs.Add(block, defs);
                liveIn.Add(block, new HashSet<LirVirtualRegister>());
                liveOut.Add(block, new HashSet<LirVirtualRegister>());
            }

            ComputeBlockLiveness(blockUses, blockDefs, liveIn, liveOut);

            foreach (var block in _function.Blocks)
            {
                if (!blockRanges.TryGetValue(block, out var range))
                    continue;

                foreach (var register in liveIn[block])
                    Extend(intervals, register, range.Start, range.End);
                foreach (var register in liveOut[block])
                    Extend(intervals, register, range.Start, range.End);
            }

            foreach (var register in _function.VirtualRegisters)
            {
                if (!ShouldTrack(register) || intervals.ContainsKey(register))
                    continue;

                intervals.Add(register, new LiveInterval(register, 0, 0));
            }

            return intervals;
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
            onTouch?.Invoke(register);
        }

        private static void Extend(Dictionary<LirVirtualRegister, LiveInterval> intervals, LirVirtualRegister register, int start, int end)
        {
            if (!ShouldTrack(register))
                return;

            if (!intervals.TryGetValue(register, out var interval))
            {
                interval = new LiveInterval(register, start, end);
                intervals.Add(register, interval);
                return;
            }

            interval.Start = Math.Min(interval.Start, start);
            interval.End = Math.Max(interval.End, end);
        }

        private static bool ShouldTrack(LirVirtualRegister register)
            => register.RegisterClass is not (LirRegisterClass.Void or LirRegisterClass.Memory);

        private static void AllocateClasses(
            Dictionary<LirVirtualRegister, LiveInterval> allIntervals,
            IReadOnlyCollection<LirRegisterClass> registerClasses,
            ImmutableArray<MachineRegister> physicalRegisters,
            Dictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
            IReadOnlyDictionary<LirVirtualRegister, List<LirVirtualRegister>> copyPreferences)
        {
            if (physicalRegisters.IsDefaultOrEmpty)
                return;

            var intervals = allIntervals.Values
                .Where(i => registerClasses.Contains(i.Register.RegisterClass))
                .OrderBy(static i => i.Start)
                .ThenBy(static i => i.End)
                .ToList();

            var active = new List<LiveInterval>();
            var valueNumberPreferredRegisters = new Dictionary<ValueNumber, MachineRegister>();

            foreach (var interval in intervals)
            {
                ExpireOldIntervals(interval, active, allocations);

                if (active.Count == physicalRegisters.Length)
                {
                    SpillAtInterval(interval, active, allocations, valueNumberPreferredRegisters);
                }
                else
                {
                    var reg = PreferredFreeRegister(interval, physicalRegisters, active, allocations, copyPreferences, valueNumberPreferredRegisters);
                    interval.PhysicalRegister = reg;
                    allocations[interval.Register] = VirtualRegisterAllocation.InRegister(interval.Register, reg);
                    RememberValueNumberRegister(interval, reg, valueNumberPreferredRegisters);
                    InsertActive(active, interval);
                }
            }
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
            IReadOnlyDictionary<ValueNumber, MachineRegister> valueNumberPreferredRegisters)
        {
            if (copyPreferences.TryGetValue(interval.Register, out var preferredSources))
            {
                for (var i = preferredSources.Count - 1; i >= 0; i--)
                {
                    var source = preferredSources[i];
                    if (!allocations.TryGetValue(source, out var sourceAllocation) || sourceAllocation.IsSpilled)
                        continue;

                    var preferred = sourceAllocation.PhysicalRegister;
                    if (IsFreeRegister(preferred, physicalRegisters, active))
                        return preferred;
                }
            }

            var valueNumber = interval.Register.ValueNumber;
            if (valueNumber is not null &&
                !valueNumber.IsMemoryDependent &&
                !valueNumber.IsUnique &&
                valueNumberPreferredRegisters.TryGetValue(valueNumber, out var valueNumberRegister) &&
                IsFreeRegister(valueNumberRegister, physicalRegisters, active))
            {
                return valueNumberRegister;
            }

            return FirstFreeRegister(physicalRegisters, active);
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

        private static bool IsFreeRegister(MachineRegister register, ImmutableArray<MachineRegister> physicalRegisters, List<LiveInterval> active)
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
                if (interval.PhysicalRegister == register)
                    return false;
            }

            return true;
        }

        private static MachineRegister FirstFreeRegister(ImmutableArray<MachineRegister> physicalRegisters, List<LiveInterval> active)
        {
            foreach (var register in physicalRegisters)
            {
                var used = false;
                foreach (var interval in active)
                {
                    if (interval.PhysicalRegister == register)
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
            Dictionary<ValueNumber, MachineRegister> valueNumberPreferredRegisters)
        {
            var spill = active[0];
            var spillNextUse = spill.NextUseAtOrAfter(current.Start);
            for (var i = 1; i < active.Count; i++)
            {
                var candidate = active[i];
                var candidateNextUse = candidate.NextUseAtOrAfter(current.Start);
                if (candidateNextUse > spillNextUse ||
                    candidateNextUse == spillNextUse && candidate.End > spill.End)
                {
                    spill = candidate;
                    spillNextUse = candidateNextUse;
                }
            }

            var currentNextUse = current.NextUseAtOrAfter(current.Start);
            if (spillNextUse > currentNextUse ||
                spillNextUse == currentNextUse && spill.End > current.End)
            {
                current.PhysicalRegister = spill.PhysicalRegister;
                allocations[current.Register] = VirtualRegisterAllocation.InRegister(current.Register, current.PhysicalRegister);
                RememberValueNumberRegister(current, current.PhysicalRegister, valueNumberPreferredRegisters);
                allocations[spill.Register] = VirtualRegisterAllocation.Spilled(spill.Register, spill.Register.RegisterClass);
                active.Remove(spill);
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

                offset = AlignUp(offset, _options.SpillSlotAlignment);
                spillOffsets.Add(pair.Key, offset);
                offset = checked(offset + SpillSlotSizeFor(pair.Key));
            }
            var spillAreaSize = checked(offset - spillAreaOffset);

            var parallelCopyTempSize = ComputeParallelCopyTempSize(allocations, spillOffsets);
            offset = AlignUp(offset, _options.SpillSlotAlignment);
            var parallelCopyTempOffset = offset;
            offset = checked(offset + parallelCopyTempSize);

            // Floating-point immediates are materialized by storing their raw bits through
            // an integer register and then loading them into an FPR. Keep this scratch
            // slot separate from the parallel-copy temporary area because an immediate
            // can be used while parallel-copy temporaries already contain live data.
            offset = AlignUp(offset, _options.SpillSlotAlignment);
            var floatingImmediateTempOffset = offset;
            var floatingImmediateTempSize = AlignUp(Math.Max(8, _options.SpillSlotSize), _options.SpillSlotAlignment);
            offset = checked(offset + floatingImmediateTempSize);

            var usedRegisters = allocations.Values
                .Where(static a => !a.IsSpilled && a.PhysicalRegister != MachineRegister.Invalid)
                .Select(static a => a.PhysicalRegister)
                .Distinct()
                .OrderBy(static r => (int)r)
                .ToImmutableArray();

            var savedRegisterOffsets = new Dictionary<MachineRegister, int>();
            var savedRegisterAreaOffset = offset;
            foreach (var register in usedRegisters)
            {
                offset = AlignUp(offset, 8);
                savedRegisterOffsets.Add(register, offset);
                offset = checked(offset + 8);
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
                if (instruction.Kind != LirInstructionKind.Call)
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

        private int ComputeParallelCopyTempSize(
            IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
            IReadOnlyDictionary<LirVirtualRegister, int> spillOffsets)
        {
            var maxSize = 0;
            foreach (var instruction in _function.Blocks.SelectMany(static b => b.Instructions))
            {
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
                if (!HasAggregateParallelCopy(copies) && !HasPhysicalStorageClobber(copies, allocations, spillOffsets))
                    continue;

                var size = 0;
                foreach (var copy in copies)
                    size = checked(size + ParallelCopyTempSlotSize(copy));
                maxSize = Math.Max(maxSize, size);
            }

            return maxSize;
        }

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

        private static bool HasAggregateParallelCopy(ImmutableArray<LirParallelCopy> copies)
        {
            foreach (var copy in copies)
            {
                if (copy.Destination.RegisterClass == LirRegisterClass.Aggregate)
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

        private int SpillSlotSizeFor(LirVirtualRegister register)
            => AlignUp(Math.Max(_options.SpillSlotSize, SizeOfStorage(register.Type)), _options.SpillSlotAlignment);

        private int SizeOfStorage(QualifiedType type)
            => Math.Max(1, _target.SizeOf(type));

        private static int AlignUp(int value, int alignment)
        {
            if (alignment <= 1)
                return value;
            var mask = alignment - 1;
            return checked((value + mask) & ~mask);
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

            public void AddUse(int position)
            {
                if (_usePositions.Count == 0 || _usePositions[_usePositions.Count - 1] != position)
                    _usePositions.Add(position);
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
