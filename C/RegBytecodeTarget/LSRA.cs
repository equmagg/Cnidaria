using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Cnidaria.Cs;

namespace Cnidaria.C
{
    internal sealed class LSRAOptions
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

            AllocateClasses(intervals, new[] { LirRegisterClass.General, LirRegisterClass.Address }, _options.GeneralRegisters, allocations);
            AllocateClasses(intervals, new[] { LirRegisterClass.Floating }, _options.FloatingRegisters, allocations);

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
            var position = 0;

            foreach (var block in _function.Blocks)
            {
                foreach (var instruction in block.Instructions)
                {
                    var pos = position++;
                    VisitInstructionUses(instruction, pos, intervals);
                    if (instruction.Result is not null)
                        Touch(intervals, instruction.Result, pos, isUse: false);
                }
            }

            foreach (var register in _function.VirtualRegisters)
            {
                if (!intervals.ContainsKey(register) && register.RegisterClass is not (LirRegisterClass.Void or LirRegisterClass.Memory))
                    intervals.Add(register, new LiveInterval(register, 0, 0));
            }

            return intervals;
        }

        private static void VisitInstructionUses(LirInstruction instruction, int position, Dictionary<LirVirtualRegister, LiveInterval> intervals)
        {
            foreach (var operand in instruction.Operands)
                VisitOperand(operand, position, intervals);

            if (instruction.Address is not null)
                VisitAddress(instruction.Address, position, intervals);

            foreach (var copy in instruction.ParallelCopies)
            {
                VisitOperand(copy.Source, position, intervals);
                Touch(intervals, copy.Destination, position, isUse: false);
            }

            foreach (var @case in instruction.SwitchCases)
                VisitOperand(@case.Value, position, intervals);
        }

        private static void VisitOperand(LirOperand operand, int position, Dictionary<LirVirtualRegister, LiveInterval> intervals)
        {
            switch (operand.Kind)
            {
                case LirOperandKind.Register:
                    if (operand.Register is not null)
                        Touch(intervals, operand.Register, position, isUse: true);
                    break;

                case LirOperandKind.Address:
                    if (operand.Address is not null)
                        VisitAddress(operand.Address, position, intervals);
                    break;
            }
        }

        private static void VisitAddress(LirAddress address, int position, Dictionary<LirVirtualRegister, LiveInterval> intervals)
        {
            if (address.BaseOperand is not null)
                VisitOperand(address.BaseOperand, position, intervals);
            if (address.BaseAddress is not null)
                VisitAddress(address.BaseAddress, position, intervals);
            if (address.Index is not null)
                VisitOperand(address.Index, position, intervals);
        }

        private static void Touch(Dictionary<LirVirtualRegister, LiveInterval> intervals, LirVirtualRegister register, int position, bool isUse)
        {
            if (!intervals.TryGetValue(register, out var interval))
            {
                interval = new LiveInterval(register, position, position);
                intervals.Add(register, interval);
            }

            interval.Start = Math.Min(interval.Start, position);
            interval.End = Math.Max(interval.End, position + (isUse ? 1 : 0));
        }

        private static void AllocateClasses(
            Dictionary<LirVirtualRegister, LiveInterval> allIntervals,
            IReadOnlyCollection<LirRegisterClass> registerClasses,
            ImmutableArray<MachineRegister> physicalRegisters,
            Dictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations)
        {
            if (physicalRegisters.IsDefaultOrEmpty)
                return;

            var intervals = allIntervals.Values
                .Where(i => registerClasses.Contains(i.Register.RegisterClass))
                .OrderBy(static i => i.Start)
                .ThenBy(static i => i.End)
                .ToList();

            var active = new List<LiveInterval>();

            foreach (var interval in intervals)
            {
                ExpireOldIntervals(interval, active, allocations);

                if (active.Count == physicalRegisters.Length)
                {
                    SpillAtInterval(interval, active, allocations);
                }
                else
                {
                    var reg = FirstFreeRegister(physicalRegisters, active);
                    interval.PhysicalRegister = reg;
                    allocations[interval.Register] = VirtualRegisterAllocation.InRegister(interval.Register, reg);
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

        private static void SpillAtInterval(LiveInterval current, List<LiveInterval> active, Dictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations)
        {
            var spill = active[active.Count - 1];
            if (spill.End > current.End)
            {
                current.PhysicalRegister = spill.PhysicalRegister;
                allocations[current.Register] = VirtualRegisterAllocation.InRegister(current.Register, current.PhysicalRegister);
                allocations[spill.Register] = VirtualRegisterAllocation.Spilled(spill.Register, spill.Register.RegisterClass);
                active.RemoveAt(active.Count - 1);
                InsertActive(active, current);
            }
            else
            {
                allocations[current.Register] = VirtualRegisterAllocation.Spilled(current.Register, current.Register.RegisterClass);
            }
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
                offset = checked(offset + _options.SpillSlotSize);
            }
            var spillAreaSize = checked(offset - spillAreaOffset);

            var parallelCopyTempSize = ComputeParallelCopyTempSize();
            offset = AlignUp(offset, _options.SpillSlotAlignment);
            var parallelCopyTempOffset = offset;
            offset = checked(offset + parallelCopyTempSize);

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
                spillAreaOffset,
                spillAreaSize,
                parallelCopyTempOffset,
                parallelCopyTempSize,
                savedRegisterAreaOffset,
                savedRegisterAreaSize,
                stackSlotOffsets,
                spillOffsets,
                savedRegisterOffsets);
        }

        private int ComputeOutgoingArgumentAreaSize()
        {
            var maxStackSlots = 0;

            foreach (var instruction in _function.Blocks.SelectMany(static b => b.Instructions))
            {
                if (instruction.Kind != LirInstructionKind.Call)
                    continue;

                var integerIndex = 0;
                var floatIndex = 0;
                var stackIndex = 0;

                for (var i = 1; i < instruction.Operands.Length; i++)
                {
                    var operand = instruction.Operands[i];
                    var cls = ClassifyArgument(operand.Type);
                    if (cls == AbiRegisterClass.Floating)
                    {
                        if (floatIndex++ >= 8)
                            stackIndex++;
                    }
                    else
                    {
                        if (integerIndex++ >= 8)
                            stackIndex++;
                    }
                }

                maxStackSlots = Math.Max(maxStackSlots, stackIndex);
            }

            return checked(maxStackSlots * _options.StackArgumentSlotSize);
        }

        private int ComputeParallelCopyTempSize()
        {
            var maxCopies = 0;
            foreach (var instruction in _function.Blocks.SelectMany(static b => b.Instructions))
            {
                if (instruction.Kind == LirInstructionKind.ParallelCopy)
                    maxCopies = Math.Max(maxCopies, instruction.ParallelCopies.Length);
            }

            return checked(maxCopies * _options.SpillSlotSize);
        }

        private static AbiRegisterClass ClassifyArgument(QualifiedType type)
        {
            if (type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Float or BuiltinTypeKind.Double })
                return AbiRegisterClass.Floating;

            return AbiRegisterClass.General;
        }

        private static int AlignUp(int value, int alignment)
        {
            if (alignment <= 1)
                return value;
            var mask = alignment - 1;
            return checked((value + mask) & ~mask);
        }

        private sealed class LiveInterval
        {
            public LirVirtualRegister Register { get; }
            public int Start { get; set; }
            public int End { get; set; }
            public MachineRegister PhysicalRegister { get; set; }

            public LiveInterval(LirVirtualRegister register, int start, int end)
            {
                Register = register;
                Start = start;
                End = end;
                PhysicalRegister = MachineRegister.Invalid;
            }
        }

        private enum AbiRegisterClass
        {
            General,
            Floating,
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
        public int SavedRegisterAreaOffset { get; }
        public int SavedRegisterAreaSize { get; }
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
            int spillAreaOffset,
            int spillAreaSize,
            int parallelCopyTempOffset,
            int parallelCopyTempSize,
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
            SavedRegisterAreaOffset = savedRegisterAreaOffset;
            SavedRegisterAreaSize = savedRegisterAreaSize;
            StackSlotOffsets = stackSlotOffsets ?? throw new ArgumentNullException(nameof(stackSlotOffsets));
            SpillOffsets = spillOffsets ?? throw new ArgumentNullException(nameof(spillOffsets));
            SavedRegisterOffsets = savedRegisterOffsets ?? throw new ArgumentNullException(nameof(savedRegisterOffsets));
        }
    }
}
