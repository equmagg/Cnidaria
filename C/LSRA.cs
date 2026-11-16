using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Cnidaria.Cs;

namespace Cnidaria.C;

[Flags]
internal enum ScratchClass : byte
{
    None = 0,
    General = 1,
    Floating = 2,
    All = General | Floating,
}

/// <summary>What a code generator's emission routines need of the scratch registers the allocator also hands out</summary>
internal interface IScratchNeeds
{
    int BorrowAreaSize { get; }
    ScratchClass Needs(LirInstruction instruction, LirInstruction? previous);
    ImmutableHashSet<MachineRegister> Registers(ScratchClass classes);
    int SpareCount(ScratchClass scratchClass);
    bool MayFoldLoad(LirInstruction? previous, LirInstruction instruction);
    bool ReadsRightAfterWrite(LirInstruction instruction);
    int StackAccessScratch(int spillAreaEnd, bool dynamicStack);
}

public sealed class LSRAOptions
{
    public ImmutableArray<MachineRegister> GeneralRegisters { get; }
    public ImmutableArray<MachineRegister> FloatingRegisters { get; }
    public ImmutableArray<MachineRegister> VectorRegisters { get; }
    public ImmutableArray<LirRegisterClass> CallBoundarySplitClasses { get; }

    public int StackAlignment { get; }
    public int SpillSlotSize { get; }
    public int SpillSlotAlignment { get; }
    public int StackArgumentSlotSize { get; }

    public LSRAOptions(
        ImmutableArray<MachineRegister> generalRegisters = default,
        ImmutableArray<MachineRegister> floatingRegisters = default,
        ImmutableArray<MachineRegister> vectorRegisters = default,
        ImmutableArray<LirRegisterClass> callBoundarySplitClasses = default,
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

        CallBoundarySplitClasses = callBoundarySplitClasses.IsDefault
            ? ImmutableArray<LirRegisterClass>.Empty
            : callBoundarySplitClasses;

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
            _ => new LSRAOptions(
                generalRegisters: TargetRegisterInfo.AllocatableGeneralRegisters(target),
                floatingRegisters: TargetRegisterInfo.AllocatableFloatingRegisters(target))
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
    private const int MaxWeightedLoopDepth = 3;

    private readonly LirFunction _function;
    private readonly TargetInfo _target;
    private readonly LSRAOptions _options;
    private readonly Dictionary<LirVirtualRegister, List<LirVirtualRegister>> _copyPreferences = new();
    private readonly List<int> _callPositions = new();
    private readonly List<int> _blockStarts = new();
    private readonly List<int> _blockWeights = new();
    private readonly List<int> _blockRuns = new();
    private readonly Dictionary<int, Dictionary<LirVirtualRegister, MachineRegister>> _callArgumentTargets = new();
    private readonly ClobberTable _instructionClobbers = new();
    private readonly Dictionary<int, ScratchClass> _scratchKills = new();
    private readonly Dictionary<int, LirAddress> _foldableLoadAddresses = new();
    private readonly List<InlineAssemblySite> _inlineAssemblySites = new();
    private readonly Dictionary<MachineRegister, int> _incomingRegisterReleasePositions = new();
    private readonly Dictionary<string, ImmutableArray<MachineRegister>> _incomingParameterRegisters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImmutableArray<AbiLocation>> _incomingParameterLocations = new(StringComparer.Ordinal);
    private readonly Dictionary<LirVirtualRegister, List<MachineRegister>> _abiPreferences = new();
    private readonly Dictionary<LirVirtualRegister, List<LirVirtualRegister>> _phiWebs = new();
    private readonly Dictionary<LirVirtualRegister, LirVirtualRegister> _sameBitsSources = new();
    private readonly Dictionary<LirVirtualRegister, LirInstruction> _definingInstructions = new();
    private readonly IScratchNeeds? _scratchNeeds;
    private readonly Dictionary<MachineRegister, List<LiveInterval>> _registerOccupants = new();
    private readonly Dictionary<LirInstruction, ParallelCopyCycleRegisters> _parallelCopyCycleRegisters = new();
    private readonly HashSet<LirInstruction> _conservativeAsmSites;
    private bool _hasRegisterGroups;
    private ImmutableArray<MachineRegister> _generalRegisters;

    private LinearScanRegisterAllocator(LirFunction function, TargetInfo target, LSRAOptions? options, IScratchNeeds? scratchNeeds, HashSet<LirInstruction> conservativeAsmSites)
    {
        _function = function ?? throw new ArgumentNullException(nameof(function));
        _target = target ?? TargetInfo.Default;
        _options = options ?? LSRAOptions.ForTarget(_target);
        _scratchNeeds = scratchNeeds;
        _conservativeAsmSites = conservativeAsmSites;
    }

    public static AllocationResult Allocate(LirFunction function, TargetInfo? target = null, LSRAOptions? options = null,
        IScratchNeeds? scratchNeeds = null)
    {
        var conservativeAsmSites = new HashSet<LirInstruction>();
        while (true)
        {
            AllocationResult? result = new LinearScanRegisterAllocator(function, target ?? TargetInfo.Default, options, scratchNeeds, conservativeAsmSites).Allocate();
            if (result is not null)
                return result;
        }
    }

    private AllocationResult? Allocate()
    {
        _callPositions.Clear();
        _blockStarts.Clear();
        _blockWeights.Clear();
        _blockRuns.Clear();
        _callArgumentTargets.Clear();
        _instructionClobbers.Clear();
        _scratchKills.Clear();
        _foldableLoadAddresses.Clear();
        _inlineAssemblySites.Clear();
        _incomingRegisterReleasePositions.Clear();
        _abiPreferences.Clear();
        _phiWebs.Clear();
        _sameBitsSources.Clear();
        _definingInstructions.Clear();
        _generalRegisters = _options.GeneralRegisters;
        if (HasDynamicStack(_function) && TargetRegisterInfo.FrameBaseRegister(_target) is var frameBase && frameBase != MachineRegister.Invalid)
            _generalRegisters = _generalRegisters.Remove(frameBase);
        BuildIncomingParameterRegisters();
        var intervals = BuildIntervals();
        foreach (var interval in intervals.Values)
            interval.RegisterCount = TargetRegisterInfo.RegisterGroupCount(_target, interval.Register.Type);
        var allocations = new Dictionary<LirVirtualRegister, VirtualRegisterAllocation>();

        foreach (var interval in intervals.Values)
        {
            if (RequiresStackBackedScalar(interval.Register))
                allocations[interval.Register] = VirtualRegisterAllocation.Spilled(interval.Register, interval.Register.RegisterClass);
        }

        AllocateClasses(intervals, new[] { LirRegisterClass.General, LirRegisterClass.Address }, _generalRegisters, allocations, _copyPreferences, _abiPreferences, _phiWebs, _sameBitsSources, _definingInstructions, _target, _options.CallBoundarySplitClasses, _callPositions, _callArgumentTargets, _instructionClobbers, _inlineAssemblySites, _incomingRegisterReleasePositions, CheaperInMemory, SpillWeight);
        AllocateClasses(intervals, new[] { LirRegisterClass.Floating }, _options.FloatingRegisters, allocations, _copyPreferences, _abiPreferences, _phiWebs, _sameBitsSources, _definingInstructions, _target, _options.CallBoundarySplitClasses, _callPositions, _callArgumentTargets, _instructionClobbers, _inlineAssemblySites, _incomingRegisterReleasePositions, CheaperInMemory, SpillWeight);
        AllocateClasses(intervals, new[] { LirRegisterClass.Vector }, _options.VectorRegisters, allocations, _copyPreferences, _abiPreferences, _phiWebs, _sameBitsSources, _definingInstructions, _target, _options.CallBoundarySplitClasses, _callPositions, _callArgumentTargets, _instructionClobbers, _inlineAssemblySites, _incomingRegisterReleasePositions, CheaperInMemory, SpillWeight);

        var scratchOccupants = new Dictionary<MachineRegister, List<(int Start, int End, int Reach, LiveInterval Interval)>>();
        var managedScratch = _scratchNeeds?.Registers(ScratchClass.All);
        foreach (var interval in intervals.Values.OrderBy(static i => i.Register.Ordinal))
        {
            if (allocations.TryGetValue(interval.Register, out var assigned))
            {
                if (!assigned.IsSpilled && assigned.PhysicalRegister != MachineRegister.Invalid)
                {
                    if (!_registerOccupants.TryGetValue(assigned.PhysicalRegister, out List<LiveInterval>? held))
                        _registerOccupants.Add(assigned.PhysicalRegister, held = new List<LiveInterval>());
                    held.Add(interval);
                    _hasRegisterGroups |= interval.RegisterCount > 1;
                }
                if (managedScratch is not null && !assigned.IsSpilled && managedScratch.Contains(assigned.PhysicalRegister))
                {
                    if (!scratchOccupants.TryGetValue(assigned.PhysicalRegister, out var occupants))
                        scratchOccupants.Add(assigned.PhysicalRegister, occupants = new List<(int Start, int End, int Reach, LiveInterval Interval)>());
                    foreach (var range in interval.Ranges)
                        occupants.Add((range.Start, range.End, range.End, interval));
                    if (interval.Ranges.Count == 0)
                        occupants.Add((interval.Start, interval.Start, interval.Start, interval));
                }
                continue;
            }

            if (interval.Register.RegisterClass is LirRegisterClass.Void or LirRegisterClass.Memory)
                continue;

            allocations.Add(interval.Register, VirtualRegisterAllocation.Spilled(interval.Register, interval.Register.RegisterClass));
        }

        if (!AssignInlineAssemblyRegisters(allocations))
            return null;

        var callPreservations = BuildCallPreservations(intervals, allocations, out var preservationRegisters, out var stackPreservedRegisters);
        foreach (var register in stackPreservedRegisters)
            allocations[register] = allocations[register].WithCallPreservation();

        foreach (var occupants in scratchOccupants.Values)
        {
            occupants.Sort(static (left, right) => left.Start.CompareTo(right.Start));
            for (int i = 1; i < occupants.Count; i++)
            {
                if (occupants[i].Reach < occupants[i - 1].Reach)
                    occupants[i] = occupants[i] with { Reach = occupants[i - 1].Reach };
            }
        }
        Func<MachineRegister, int, bool> isScratchFree = (register, position) => IsScratchFree(scratchOccupants, register, position);

        var frame = LayoutStackFrame(allocations, preservationRegisters, isScratchFree);
        foreach (var pair in allocations.ToArray())
        {
            if (!pair.Value.NeedsStackSlot)
                continue;
            if (pair.Key.HomeSlot is not null)
            {
                allocations[pair.Key] = pair.Value.WithStackOffset(frame.StackSlotOffsets[pair.Key.HomeSlot]);
                continue;
            }

            if (!frame.SpillOffsets.TryGetValue(pair.Key, out var offset))
                throw new InvalidOperationException("Missing spill slot for " + pair.Key.Name + ".");

            allocations[pair.Key] = pair.Value.WithStackOffset(offset);
        }

        callPreservations = BindCallPreservationStackOffsets(callPreservations, allocations);
        Dictionary<LirInstruction, InlineAssemblyRegisters>? inlineAssemblyRegisters = null;
        foreach (InlineAssemblySite site in _inlineAssemblySites)
            (inlineAssemblyRegisters ??= new Dictionary<LirInstruction, InlineAssemblyRegisters>()).Add(site.Instruction, site.Registers);

        return new AllocationResult(_function, allocations, frame, callPreservations, _scratchKills, isScratchFree, _parallelCopyCycleRegisters, inlineAssemblyRegisters);
    }

    // Only copies of one value overlap in a register, so the scan backs up while an earlier range still reaches the position
    // one value can live inside another's lifetime hole, which is why the search runs over ranges, not intervals
    private bool IsScratchFree(Dictionary<MachineRegister, List<(int Start, int End, int Reach, LiveInterval Interval)>> occupants, MachineRegister register, int position)
    {
        if (_instructionClobbers.Clobbers(position, register))
            return false;
        if (!occupants.TryGetValue(register, out var ranges))
            return true;
        var low = 0;
        var high = ranges.Count - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (ranges[middle].Start <= position + 1)
                low = middle + 1;
            else
                high = middle - 1;
        }
        for (int index = high; index >= 0 && ranges[index].Reach >= position; index--)
        {
            if (ranges[index].End < position)
                continue;
            var interval = ranges[index].Interval;
            if (interval.IsLiveBefore(position) || interval.HasDefinitionAtPosition(position) || interval.HasDefinitionAtPosition(position + 1))
                return false;
        }
        return true;
    }

    // A backward edge in layout order closes a loop, and an edge-split block only carries the edge of its source
    private static void RecordBlockEdges(
        LirBlock block,
        int end,
        Dictionary<LirBlock, BlockRange> placed,
        Dictionary<LirBlock, int> loopEnds,
        Dictionary<LirBlock, int> predecessorCounts)
    {
        foreach (var successor in SuccessorsOf(block))
        {
            predecessorCounts[successor] = predecessorCounts.GetValueOrDefault(successor) + 1;
            if (block.IsEdgeSplit)
                continue;
            var target = successor.IsEdgeSplit && successor.Instructions.Length != 0 && successor.Instructions[^1].Kind == LirInstructionKind.Jump
                ? successor.Instructions[^1].Target
                : successor;
            if (target is not null && (ReferenceEquals(target, block) || placed.ContainsKey(target)))
                loopEnds[target] = end;
        }
    }

    private int BlockWeight(LirBlock block, BlockRange range, int loopDepth, Dictionary<LirBlock, BlockRange> placed)
    {
        // Edge-split blocks may sit far from both ends of their edge, so their place says nothing about their loop
        if (block.IsEdgeSplit && block.Instructions.Length != 0 && block.Instructions[^1] is { Kind: LirInstructionKind.Jump, Target: { } target } &&
            placed.TryGetValue(target, out var targetRange) && targetRange.Start < range.Start)
            return WeightAt(targetRange.Start);

        return 1 << 3 * Math.Min(loopDepth, MaxWeightedLoopDepth);
    }

    private int BlockIndexAt(int position)
    {
        var index = _blockStarts.BinarySearch(position);
        return index >= 0 ? index : ~index - 1;
    }

    private int WeightAt(int position)
        => _blockWeights[BlockIndexAt(position)];

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
        var loopEnds = new Dictionary<LirBlock, int>();
        var predecessorCounts = new Dictionary<LirBlock, int>();
        var position = 0;

        foreach (var block in _function.Blocks)
        {
            var start = position;
            var uses = new HashSet<LirVirtualRegister>();
            var defs = new HashSet<LirVirtualRegister>();
            var firstUses = new Dictionary<LirVirtualRegister, int>();
            var lastUses = new Dictionary<LirVirtualRegister, int>();
            var firstDefs = new Dictionary<LirVirtualRegister, int>();

            LirInstruction? previous = null;
            foreach (var instruction in block.Instructions)
            {
                var pos = position;
                position += 2;
                RecordInstructionClobbers(instruction, previous, pos);
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
                void OnUse(LirVirtualRegister register)
                {
                    if (!firstUses.ContainsKey(register))
                        firstUses.Add(register, pos);
                    lastUses[register] = pos;
                    if (!defs.Contains(register))
                        uses.Add(register);
                }
                VisitInstructionUses(instruction, pos, intervals, OnUse);

                // The x86 emitter may build a load's address where the load's one reader stands
                if (_scratchNeeds is not null && _scratchNeeds.MayFoldLoad(previous, instruction))
                {
                    VisitAddress(previous!.Address!, pos, intervals, OnUse);
                    _foldableLoadAddresses[pos] = previous!.Address!;
                }

                // The result is written before the right operand is read, so the two cannot share a register
                if (_scratchNeeds is not null && UpdatesFirstOperandInPlace(instruction) &&
                    _scratchNeeds.ReadsRightAfterWrite(instruction) &&
                    instruction.Operands[1] is { Kind: LirOperandKind.Register, Register: { } right } && ShouldTrack(right))
                {
                    Touch(intervals, right, pos + 1, isUse: true);
                    lastUses[right] = pos + 1;
                }

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
                if (instruction.Result is not null && instruction.Kind != LirInstructionKind.ParallelCopy)
                    _definingInstructions[instruction.Result] = instruction;

                // An output nothing reads is written all the same, so it must not share a register with another output
                if (instruction.Kind == LirInstructionKind.InlineAssembly)
                {
                    foreach (LirParallelCopy copy in instruction.ParallelCopies)
                    {
                        if (!ShouldTrack(copy.Destination))
                            continue;
                        Touch(intervals, copy.Destination, pos + 1, isUse: true);
                        lastUses[copy.Destination] = pos + 1;
                    }
                }
                previous = instruction;
            }

            RecordBlockEdges(block, position, blockRanges, loopEnds, predecessorCounts);
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

        var loopDepth = 0;
        var loopClosings = new Dictionary<int, int>();
        LirBlock? previousBlock = null;
        foreach (var block in _function.Blocks)
        {
            if (!blockRanges.TryGetValue(block, out var range))
                continue;

            if (loopClosings.Remove(range.Start, out var closedLoops))
                loopDepth -= closedLoops;
            if (loopEnds.TryGetValue(block, out var loopEnd))
            {
                loopDepth++;
                loopClosings[loopEnd] = loopClosings.GetValueOrDefault(loopEnd) + 1;
            }
            _blockRuns.Add(previousBlock is not null && predecessorCounts.GetValueOrDefault(block) == 1 &&
                previousBlock.Instructions.Length != 0 && previousBlock.Instructions[^1] is { Kind: LirInstructionKind.Jump } jump && ReferenceEquals(jump.Target, block)
                ? _blockRuns[^1]
                : _blockRuns.Count);
            _blockStarts.Add(range.Start);
            _blockWeights.Add(BlockWeight(block, range, loopDepth, blockRanges));
            previousBlock = block;

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

                // A value that arrives live and is written again later in the block is dead between
                // its last read and that write. Leaving the gap out of the interval is what lets the
                // allocator hand the write the register the value it reads from already holds, which
                // on a loop latch is the difference between an in-place update and a copy back
                if (liveIn[block].Contains(register) &&
                    blockFirstDefs[block].TryGetValue(register, out var rewrite) &&
                    rewrite > range.Start &&
                    blockLastUses[block].TryGetValue(register, out var lastRead) &&
                    lastRead < rewrite)
                {
                    interval.AddRange(range.Start, lastRead + 1);
                    interval.AddRange(rewrite, Math.Max(rewrite, end));
                    continue;
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


    // A definition one slot past the reads lets the result reuse a register that dies here, which is
    // sound for every operator below: each reads its sources before it writes the destination
    private int DefinitionPosition(LirInstruction instruction, int position)
        => instruction.Kind is LirInstructionKind.Copy or LirInstructionKind.ParallelCopy or LirInstructionKind.Call
               or LirInstructionKind.Convert or LirInstructionKind.Select ||
           instruction.Kind == LirInstructionKind.Load && instruction.Result is not null &&
               TargetRegisterInfo.RegisterGroupCount(_target, instruction.Result.Type) == 1 ||
           UpdatesFirstOperandInPlace(instruction) ||
           IsThreeAddressArithmetic(instruction)
            ? position + 1
            : position;

    // x86 writes some results through fixed registers or setcc before the last read, and a register pair is written half by half
    private bool IsThreeAddressArithmetic(LirInstruction instruction)
    {
        if (instruction.Kind is not (LirInstructionKind.Unary or LirInstructionKind.Binary) ||
            _target.IsX86 ||
            instruction.Result is null ||
            instruction.Result.RegisterClass is not (LirRegisterClass.General or LirRegisterClass.Address or LirRegisterClass.Floating) ||
            TargetRegisterInfo.RegisterGroupCount(_target, instruction.Result.Type) != 1)
        {
            return false;
        }

        foreach (LirOperand operand in instruction.Operands)
        {
            if (operand.Kind == LirOperandKind.Register && operand.Register is not null &&
                TargetRegisterInfo.RegisterGroupCount(_target, operand.Register.Type) != 1)
            {
                return false;
            }
        }

        return true;
    }

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
        _incomingParameterLocations.Clear();
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
            _incomingParameterLocations[parameter.Name] = locations;
        }
    }

    private void RecordInstructionClobbers(LirInstruction instruction, LirInstruction? previous, int position)
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

        var early = ImmutableHashSet<MachineRegister>.Empty;
        if (TargetRegisterInfo.IsX86(_target) &&
            instruction.Kind == LirInstructionKind.Binary &&
            instruction.Result is not null &&
            instruction.Result.RegisterClass is LirRegisterClass.General or LirRegisterClass.Address)
        {
            // A power of two divisor is reduced to shifts, which need none of the fixed registers
            if (instruction.Operator is "/" or "%" &&
                !LirStrengthReduction.TryGetPowerOfTwoDivisor(instruction, _target, out _, out _))
            {
                // div and idiv read and write rax:rdx, and the emitted sequence parks the divisor in rcx
                clobbers.Add(TargetRegisterInfo.X86AccumulatorRegister(_target));
                clobbers.Add(TargetRegisterInfo.X86DataRegister(_target));
                clobbers.Add(TargetRegisterInfo.X86CounterRegister(_target));
            }
            else if (instruction.Operator is "<<" or ">>" or "rotl" or "rotr" &&
                     instruction.Operands.Length == 2 &&
                     instruction.Operands[1].Kind != LirOperandKind.Immediate)
            {
                // A variable shift count has to be in cl, which goes there before the result is written
                clobbers.Add(TargetRegisterInfo.X86CounterRegister(_target));
                early = early.Add(TargetRegisterInfo.X86CounterRegister(_target));
            }
        }

        // The x86 emitter builds copies through memory with a third scratch register
        if (TargetRegisterInfo.IsX86(_target) && NeedsX86BlockCopyScratch(instruction))
            clobbers.Add(TargetRegisterInfo.X86AccumulatorRegister(_target));

        if (_scratchNeeds is not null && _scratchNeeds.Needs(instruction, previous) is var needs && needs != ScratchClass.None)
        {
            early = early.Union(_scratchNeeds.Registers(needs));
            clobbers.UnionWith(early);
            _scratchKills[position] = needs;
        }

        if (clobbers.Count != 0)
            _instructionClobbers.Add(position, clobbers.ToImmutable(), early);
    }

    private bool NeedsX86BlockCopyScratch(LirInstruction instruction)
    {
        if (instruction.Kind is LirInstructionKind.ZeroMemory or LirInstructionKind.VaStart or LirInstructionKind.VaArg)
            return true;
        if (instruction.Kind == LirInstructionKind.InlineAssembly &&
            instruction.SourceStatement is GimpleAsmStatement asmStatement && InlineAsmConstraints.FormatsThroughScratch(asmStatement))
            return true;

        if (instruction.Result is not null && IsX86BlockCopyStorage(instruction.Result.Type))
            return true;

        foreach (var operand in instruction.Operands)
        {
            if (IsX86BlockCopyStorage(operand.Type))
                return true;
        }

        foreach (var copy in instruction.ParallelCopies)
        {
            if (IsX86BlockCopyStorage(copy.Destination.Type) || IsX86BlockCopyStorage(copy.Source.Type))
                return true;
        }

        return false;
    }

    // Mirrors RequiresBlockCopyStorage in the x86 code generator
    private bool IsX86BlockCopyStorage(QualifiedType type)
        => type.Type.Kind is TypeKind.Struct or TypeKind.Union or TypeKind.Array ||
           (type.Type.Kind is not (TypeKind.Pointer or TypeKind.Function) &&
            Math.Max(1, _target.SizeOf(type)) > Math.Max(1, _target.RegisterSize));

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
            if (operand.Kind == LirOperandKind.Segments)
            {
                for (var j = 0; j < operand.Parts.Length; j++)
                {
                    if (operand.Parts[j].Register is { } part && ShouldTrack(part))
                    {
                        RecordCallArgumentTarget(targets, part, j < locations.Length
                            ? SingleCompatibleArgumentRegister(ImmutableArray.Create(locations[j]), part.RegisterClass)
                            : MachineRegister.Invalid);
                    }
                }
                continue;
            }
            if (operand.Kind != LirOperandKind.Register || operand.Register is null || !ShouldTrack(operand.Register))
                continue;

            RecordCallArgumentTarget(targets, operand.Register, SingleCompatibleArgumentRegister(locations, operand.Register.RegisterClass));
        }

        _callArgumentTargets[position] = targets;
    }

    private void RecordCallArgumentTarget(Dictionary<LirVirtualRegister, MachineRegister> targets, LirVirtualRegister register, MachineRegister target)
    {
        if (target == MachineRegister.Invalid)
        {
            targets[register] = MachineRegister.Invalid;
            return;
        }

        if (targets.TryGetValue(register, out var existing) && existing != target)
            targets[register] = MachineRegister.Invalid;
        else
            targets[register] = target;
        AddAbiPreference(register, target);
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
            if (instruction.Operands.Length == 1 && instruction.Operands[0].Immediate is int segment)
            {
                if (_incomingParameterLocations.TryGetValue(instruction.Operator, out var locations) && segment < locations.Length &&
                    locations[segment].Kind == AbiLocationKind.Register)
                {
                    AddAbiPreference(instruction.Result, locations[segment].Register);
                }
            }
            else
            {
                foreach (var register in incomingRegisters)
                    AddAbiPreference(instruction.Result, register);
            }
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
        var returnType = _function.Symbol?.FunctionType?.ReturnType ?? operand.Type;
        var returnValue = CAbi.ClassifyValue(_target, returnType, isReturn: true, isVariadicUnnamedArgument: false);
        if (operand.Kind == LirOperandKind.Segments)
        {
            for (var i = 0; i < operand.Parts.Length && i < returnValue.Segments.Length; i++)
            {
                if (operand.Parts[i].Register is { } part)
                    AddAbiPreference(part, CAbi.SegmentReturnRegister(returnValue, i));
            }
            return;
        }
        if (operand.Kind != LirOperandKind.Register || operand.Register is null)
            return;

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
        var site = new InlineAssemblySite(instruction, position);
        if (instruction.SourceStatement is not GimpleAsmStatement asmStatement)
            return site;

        ImmutableHashSet<MachineRegister>.Builder reserved = ImmutableHashSet.CreateBuilder<MachineRegister>();
        ImmutableHashSet<MachineRegister>.Builder touched = ImmutableHashSet.CreateBuilder<MachineRegister>();
        ImmutableHashSet<MachineRegister>.Builder? classes = _conservativeAsmSites.Contains(instruction) ? ImmutableHashSet.CreateBuilder<MachineRegister>() : null;
        ReserveInlineAssemblyTemplateRegisters(asmStatement.Text, reserved, touched);

        var copyIndex = 0;
        var operandIndex = 0;
        for (var index = 0; index < asmStatement.Outputs.Length; index++)
        {
            GimpleAsmOperand output = asmStatement.Outputs[index];
            if (output.Target is null)
                continue;

            if (InlineAsmConstraints.PreferredStorage(output.Constraint, output.Target.Type) == InlineAsmOperandStorage.Memory)
            {
                operandIndex++;
                continue;
            }

            if (copyIndex < instruction.ParallelCopies.Length)
            {
                LirVirtualRegister destination = instruction.ParallelCopies[copyIndex].Destination;
                switch (FixedInlineAssemblyRegister(output.Constraint, output.Target.Type, out MachineRegister fixedRegister))
                {
                    case true:
                        AddInlineAssemblyRegister(reserved, touched, fixedRegister);
                        site.OutputTargets[destination] = fixedRegister;
                        AddAbiPreference(destination, fixedRegister);
                        break;
                    case false:
                        reserved.UnionWith(ClassRegisters(CAbi.PreferredLirRegisterClass(_target, output.Target.Type)));
                        break;
                    default:
                        site.GenericOutputs.Add((index, destination));
                        site.GenericRegisters.Add(destination);
                        classes?.UnionWith(ClassRegisters(destination.RegisterClass));
                        break;
                }
            }
            copyIndex++;
        }

        for (var index = 0; index < asmStatement.Inputs.Length; index++)
        {
            GimpleAsmOperand input = asmStatement.Inputs[index];
            if (input.Value is not null &&
                InlineAsmConstraints.PreferredStorage(input.Constraint, input.Value.Type) == InlineAsmOperandStorage.Register &&
                InlineAsmConstraints.MatchingOperand(input.Constraint) is null &&
                operandIndex < instruction.Operands.Length)
            {
                LirOperand value = instruction.Operands[operandIndex];
                LirVirtualRegister? source = value.Kind == LirOperandKind.Register ? value.Register : null;
                switch (FixedInlineAssemblyRegister(input.Constraint, input.Value.Type, out MachineRegister fixedRegister))
                {
                    case true:
                        AddInlineAssemblyRegister(reserved, touched, fixedRegister);
                        if (source is not null)
                        {
                            site.InputTargets[source] = fixedRegister;
                            AddAbiPreference(source, fixedRegister);
                        }
                        break;
                    case false:
                        reserved.UnionWith(ClassRegisters(CAbi.PreferredLirRegisterClass(_target, input.Value.Type)));
                        break;
                    default:
                        site.GenericInputs.Add((index, value));
                        if (source is not null)
                            site.GenericRegisters.Add(source);
                        classes?.UnionWith(ClassRegisters(source?.RegisterClass ?? CAbi.PreferredLirRegisterClass(_target, input.Value.Type)));
                        break;
                }
            }

            operandIndex++;
        }

        foreach (var clobber in asmStatement.Clobbers)
        {
            if (clobber == "memory" ||
                clobber == "cc" ||
                clobber == "redzone")
            {
                continue;
            }

            if (TryParseInlineAssemblyRegister(clobber, out var register))
                AddInlineAssemblyRegister(reserved, touched, register);
        }

        site.ReservedRegisters = reserved.ToImmutable();
        site.TouchedRegisters = touched.ToImmutable();
        if (classes is not null)
            site.ConservativeRegisters = classes.ToImmutable();
        return site;
    }

    // True names a register, false a register constraint the allocator cannot read, null an operand any register of its class can take
    private bool? FixedInlineAssemblyRegister(string constraint, QualifiedType type, out MachineRegister register)
    {
        register = MachineRegister.Invalid;
        string? name = InlineAsmConstraints.ExplicitRegisterName(constraint);
        if (name is null && _target.IsX86 && !CAbi.IsFloating(type))
            name = InlineAsmConstraints.X86RegisterLetterName(constraint);
        if (name is null)
            return null;
        return TargetRegisterInfo.TryParseExplicitRegister(_target, name, CAbi.PreferredLirRegisterClass(_target, type), out register);
    }

    private ImmutableArray<MachineRegister> ClassRegisters(LirRegisterClass registerClass)
        => registerClass switch
        {
            LirRegisterClass.General or LirRegisterClass.Address => _generalRegisters,
            LirRegisterClass.Floating => _options.FloatingRegisters,
            LirRegisterClass.Vector => _options.VectorRegisters,
            _ => ImmutableArray<MachineRegister>.Empty,
        };

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
        // Convert is included because most conversions are register no-ops or an in-place
        // extension: sharing a register with the source removes the move that carries them
        if (instruction.Kind is LirInstructionKind.Copy or LirInstructionKind.Convert &&
            instruction.Result is not null &&
            instruction.Operands.Length != 0 &&
            instruction.Operands[0].Kind == LirOperandKind.Register &&
            instruction.Operands[0].Register is not null)
        {
            AddCopyPreference(instruction.Result, instruction.Operands[0].Register!);
        }

        RecordFirstOperandPreference(instruction);
        RecordSameBitsSource(instruction);

        // A select is a phi resolved in place, and either of its values in the result's register spares a move
        if (instruction.Kind == LirInstructionKind.Select && instruction.Result is not null)
        {
            for (var i = 2; i < instruction.Operands.Length; i++)
            {
                if (instruction.Operands[i].Register is LirVirtualRegister value)
                {
                    AddCopyPreference(instruction.Result, value);
                    JoinPhiWeb(instruction.Result, value);
                }
            }
            return;
        }

        if (instruction.Kind != LirInstructionKind.ParallelCopy)
            return;

        foreach (var copy in instruction.ParallelCopies)
        {
            if (copy.Source.Kind == LirOperandKind.Register && copy.Source.Register is not null)
            {
                AddCopyPreference(copy.Destination, copy.Source.Register);
                JoinPhiWeb(copy.Destination, copy.Source.Register);
            }
        }
    }

    private void RecordSameBitsSource(LirInstruction instruction)
    {
        if (instruction.Kind is not (LirInstructionKind.Copy or LirInstructionKind.Convert) ||
            instruction.Result is null ||
            instruction.Operands.Length == 0 ||
            instruction.Operands[0].Kind != LirOperandKind.Register)
        {
            return;
        }

        LirVirtualRegister? source = instruction.Operands[0].Register;
        LirVirtualRegister result = instruction.Result;
        if (source is null || !ShouldTrack(source) || !ShouldTrack(result) ||
            !IsGeneralLikeClass(source.RegisterClass) || !IsGeneralLikeClass(result.RegisterClass) ||
            !HoldsSameBits(source.Type, result.Type))
        {
            return;
        }

        _sameBitsSources[result] = source;
    }

    // Narrower integers have a target-specific canonical extension a conversion may rewrite
    private bool HoldsSameBits(QualifiedType source, QualifiedType destination)
    {
        int size = _target.SizeOf(source);
        if (size != _target.SizeOf(destination))
            return false;
        bool sourcePointer = source.Type.Kind == TypeKind.Pointer;
        bool destinationPointer = destination.Type.Kind == TypeKind.Pointer;
        if (sourcePointer && destinationPointer)
            return true;
        if (!(sourcePointer || GimpleTypes.IsIntegerLike(source)) || !(destinationPointer || GimpleTypes.IsIntegerLike(destination)))
            return false;

        // The register bytecode keeps no extension invariant, it only re-extends a narrow value whose signedness changes
        if (_target.IsRegisterBytecode)
            return size >= 4 || source.Type.ToDisplayString() == destination.Type.ToDisplayString();

        return size >= _target.RegisterSize;
    }

    private void JoinPhiWeb(LirVirtualRegister destination, LirVirtualRegister source)
    {
        if (!ShouldTrack(destination) || !ShouldTrack(source) || !AreCoalescableClasses(destination.RegisterClass, source.RegisterClass))
            return;

        var web = PhiWebOf(destination);
        var other = PhiWebOf(source);
        if (ReferenceEquals(web, other))
            return;
        if (web.Count < other.Count)
            (web, other) = (other, web);
        foreach (var member in other)
        {
            web.Add(member);
            _phiWebs[member] = web;
        }
    }

    private List<LirVirtualRegister> PhiWebOf(LirVirtualRegister register)
    {
        if (!_phiWebs.TryGetValue(register, out var web))
        {
            web = new List<LirVirtualRegister> { register };
            _phiWebs.Add(register, web);
        }
        return web;
    }

    // Collapses the move into a two-address operation on x86, and elsewhere keeps a recurrence in
    // place, which costs the loop latch its copies. One-directional: the operand is allocated first
    private void RecordFirstOperandPreference(LirInstruction instruction)
    {
        if (instruction.Result is null || !UpdatesFirstOperandInPlace(instruction))
            return;

        var source = instruction.Operands[0];
        if (source.Kind != LirOperandKind.Register || source.Register is null)
            return;

        if (!ShouldTrack(instruction.Result) || !ShouldTrack(source.Register))
            return;

        if (!AreCoalescableClasses(instruction.Result.RegisterClass, source.Register.RegisterClass))
            return;

        AddPreferenceEdge(instruction.Result, source.Register);
    }

    // Division and comparison are excluded: the first lands in fixed registers, the second writes
    // the destination through setcc
    private bool UpdatesFirstOperandInPlace(LirInstruction instruction)
    {
        // Wider than a register is a software sequence over fixed pairs, not an in-place operation
        if (instruction.Result is null ||
            Math.Max(1, _target.SizeOf(instruction.Result.Type)) > Math.Max(1, _target.RegisterSize))
            return false;

        switch (instruction.Kind)
        {
            case LirInstructionKind.Unary:
                return instruction.Operands.Length == 1 &&
                    instruction.Operator is "+" or "-" or "~" or "!";

            case LirInstructionKind.Binary:
                return instruction.Operands.Length == 2 &&
                    instruction.Operator is "+" or "-" or "*" or "&" or "|" or "^" or "<<" or ">>" or "rotl" or "rotr";

            default:
                return false;
        }
    }

    /// <summary>
    /// Records the pair in both directions: the preference is resolved through whichever side is
    /// allocated first, and for a phi copy on a loop latch that is the destination, not the source.
    /// </summary>
    private void AddCopyPreference(LirVirtualRegister destination, LirVirtualRegister source)
    {
        if (!ShouldTrack(destination) || !ShouldTrack(source))
            return;

        if (!AreCoalescableClasses(destination.RegisterClass, source.RegisterClass))
            return;

        AddPreferenceEdge(destination, source);
        AddPreferenceEdge(source, destination);
    }

    private void AddPreferenceEdge(LirVirtualRegister from, LirVirtualRegister to)
    {
        if (!_copyPreferences.TryGetValue(from, out var list))
        {
            list = new List<LirVirtualRegister>();
            _copyPreferences.Add(from, list);
        }

        if (!list.Contains(to))
            list.Add(to);
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

            case LirOperandKind.Segments:
                foreach (var part in operand.Parts)
                    VisitOperand(part, position, intervals, onUse);
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
        IReadOnlyDictionary<LirVirtualRegister, List<LirVirtualRegister>> phiWebs,
        IReadOnlyDictionary<LirVirtualRegister, LirVirtualRegister> sameBitsSources,
        IReadOnlyDictionary<LirVirtualRegister, LirInstruction> definingInstructions,
        TargetInfo target,
        ImmutableArray<LirRegisterClass> callBoundarySplitClasses,
        IReadOnlyList<int> callPositions,
        IReadOnlyDictionary<int, Dictionary<LirVirtualRegister, MachineRegister>> callArgumentTargets,
        ClobberTable instructionClobbers,
        IReadOnlyList<InlineAssemblySite> inlineAssemblySites,
        IReadOnlyDictionary<MachineRegister, int> incomingRegisterReleasePositions,
        Func<LiveInterval, bool> cheaperInMemory,
        Func<LiveInterval, long> spillWeight)
    {
        if (physicalRegisters.IsDefaultOrEmpty)
            return;

        var sharedValueRoots = new Dictionary<LirVirtualRegister, LirVirtualRegister>();

        var intervals = allIntervals.Values
            .Where(i => !allocations.ContainsKey(i.Register) && registerClasses.Contains(i.Register.RegisterClass))
            .OrderBy(static i => i.Start)
            .ThenBy(static i => i.End)
            .ToList();

        var active = new List<LiveInterval>();
        var valueNumberPreferredRegisters = new Dictionary<ValueNumber, MachineRegister>();
        var callArgumentRegisters = CallArgumentRegisters(target, registerClasses);
        bool CrossesCalls(LirVirtualRegister register)
            => callBoundarySplitClasses.Contains(register.RegisterClass) &&
               allIntervals.TryGetValue(register, out var other) && IntervalSpansPosition(other, callPositions);

        foreach (var interval in intervals)
        {
            ExpireOldIntervals(interval, active, allocations);

            var allowedRegisters = AllowedRegistersForInterval(interval, physicalRegisters, abiPreferences, callArgumentRegisters, target, callBoundarySplitClasses, callPositions, callArgumentTargets, instructionClobbers, inlineAssemblySites, incomingRegisterReleasePositions);
            if (allowedRegisters.IsDefaultOrEmpty)
            {
                allocations[interval.Register] = VirtualRegisterAllocation.Spilled(interval.Register, interval.Register.RegisterClass);
                continue;
            }

            if (TryShareSourceRegister(interval, allowedRegisters, active, allocations, allIntervals, sameBitsSources, sharedValueRoots, phiWebs,
                    copyPreferences, abiPreferences, target, callPositions, out LirVirtualRegister sourceRoot))
            {
                sharedValueRoots[interval.Register] = sourceRoot;
                allocations[interval.Register] = VirtualRegisterAllocation.InRegister(interval.Register, interval.PhysicalRegister);
                RememberValueNumberRegister(interval, interval.PhysicalRegister, valueNumberPreferredRegisters);
                InsertActive(active, interval);
                continue;
            }

            MachineRegister reg = PreferredFreeRegister(interval, allowedRegisters, active, allocations, copyPreferences, abiPreferences, phiWebs, valueNumberPreferredRegisters, definingInstructions, target, CrossesCalls(interval.Register), CrossesCalls);
            if (reg == MachineRegister.Invalid)
            {
                SpillAtInterval(interval, active, allocations, valueNumberPreferredRegisters, allowedRegisters, spillWeight);
            }
            else if (!interval.Register.HasFixedRegister && !TargetRegisterInfo.IsCalleeSaved(target, reg) &&
                     CrossesCalls(interval.Register) && cheaperInMemory(interval))
            {
                allocations[interval.Register] = VirtualRegisterAllocation.Spilled(interval.Register, interval.Register.RegisterClass);
            }
            else
            {
                interval.PhysicalRegister = reg;
                allocations[interval.Register] = VirtualRegisterAllocation.InRegister(interval.Register, reg);
                RememberValueNumberRegister(interval, reg, valueNumberPreferredRegisters);
                InsertActive(active, interval);
            }
        }
    }

    // A copy of a still-live source with the same bits can hold the source's register while neither is written again
    private static bool TryShareSourceRegister(
        LiveInterval interval,
        ImmutableArray<MachineRegister> allowedRegisters,
        List<LiveInterval> active,
        IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
        Dictionary<LirVirtualRegister, LiveInterval> allIntervals,
        IReadOnlyDictionary<LirVirtualRegister, LirVirtualRegister> sameBitsSources,
        IReadOnlyDictionary<LirVirtualRegister, LirVirtualRegister> sharedValueRoots,
        IReadOnlyDictionary<LirVirtualRegister, List<LirVirtualRegister>> phiWebs,
        IReadOnlyDictionary<LirVirtualRegister, List<LirVirtualRegister>> copyPreferences,
        IReadOnlyDictionary<LirVirtualRegister, List<MachineRegister>> abiPreferences,
        TargetInfo target,
        IReadOnlyList<int> callPositions,
        out LirVirtualRegister root)
    {
        root = interval.Register;
        if (!sameBitsSources.TryGetValue(interval.Register, out LirVirtualRegister? source) ||
            interval.Register.HasFixedRegister ||
            interval.RegisterCount != 1 ||
            interval.DefinitionPositions.Count != 1)
        {
            return false;
        }

        LiveInterval? sourceInterval = null;
        foreach (LiveInterval candidate in active)
        {
            if (ReferenceEquals(candidate.Register, source))
            {
                sourceInterval = candidate;
                break;
            }
        }

        if (sourceInterval is null || sourceInterval.RegisterCount != 1 || sourceInterval.Register.HasFixedRegister ||
            !sourceInterval.Overlaps(interval))
        {
            return false;
        }

        MachineRegister register = sourceInterval.PhysicalRegister;
        if (phiWebs.TryGetValue(interval.Register, out List<LirVirtualRegister>? web) && !WebHoldsRegister(web, register, allocations))
            return false;
        if (!ContainsRegister(allowedRegisters, register) ||
            !TargetRegisterInfo.IsCalleeSaved(target, register) && IntervalSpansPosition(interval, callPositions) ||
            HasFreeAbiRegister(interval, interval.Register, allowedRegisters, active, abiPreferences))
        {
            return false;
        }

        if (copyPreferences.TryGetValue(interval.Register, out List<LirVirtualRegister>? neighbours))
        {
            foreach (LirVirtualRegister neighbour in neighbours)
            {
                if (!allocations.ContainsKey(neighbour) && HasFreeAbiRegister(interval, neighbour, allowedRegisters, active, abiPreferences))
                    return false;
            }
        }

        root = sharedValueRoots.TryGetValue(source, out LirVirtualRegister? sourceRoot) ? sourceRoot : source;
        if (allIntervals.TryGetValue(root, out LiveInterval? rootInterval))
        {
            foreach (int definition in rootInterval.DefinitionPositions)
            {
                if (interval.OverlapsSpan(definition, definition))
                    return false;
            }
        }

        foreach (LiveInterval occupant in active)
        {
            if (!occupant.Occupies(register) || !occupant.Overlaps(interval) || ReferenceEquals(occupant.Register, root))
                continue;
            if (!sharedValueRoots.TryGetValue(occupant.Register, out LirVirtualRegister? occupantRoot) || !ReferenceEquals(occupantRoot, root))
                return false;
        }

        interval.PhysicalRegister = register;
        return true;
    }

    private static bool WebHoldsRegister(
        List<LirVirtualRegister> web,
        MachineRegister register,
        IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations)
    {
        foreach (LirVirtualRegister member in web)
        {
            if (allocations.TryGetValue(member, out VirtualRegisterAllocation? allocation) && !allocation.IsSpilled && allocation.PhysicalRegister == register)
                return true;
        }

        return false;
    }

    private static bool HasFreeAbiRegister(
        LiveInterval interval,
        LirVirtualRegister register,
        ImmutableArray<MachineRegister> allowedRegisters,
        List<LiveInterval> active,
        IReadOnlyDictionary<LirVirtualRegister, List<MachineRegister>> abiPreferences)
    {
        if (!abiPreferences.TryGetValue(register, out List<MachineRegister>? preferred))
            return false;
        foreach (MachineRegister candidate in preferred)
        {
            if (IsFreeRegister(interval, candidate, allowedRegisters, active))
                return true;
        }

        return false;
    }

    private int MemoryCost(LiveInterval interval)
    {
        var cost = 0;
        foreach (var position in interval.UsePositions)
            cost += WeightAt(position);
        foreach (var position in interval.DefinitionPositions)
            cost += WeightAt(position);
        return cost;
    }

    // What leaving a register would cost per position it stops occupying, the constant keeps the shortest values from being untouchable
    private long SpillWeight(LiveInterval interval)
    {
        if (interval.SpillWeight < 0)
        {
            var length = 0;
            foreach (var range in interval.Ranges)
                length += range.End - range.Start;
            interval.SpillWeight = MemoryCost(interval) * 1024L / (length + 16);
        }

        return interval.SpillWeight;
    }

    // In a caller-saved register a value pays a save and a restore at each call it spans, in memory an access at each reference
    private bool CheaperInMemory(LiveInterval interval)
    {
        var memoryCost = MemoryCost(interval);
        var preservationCost = 0;
        var previous = -1;
        foreach (var position in _callPositions)
        {
            if (position < interval.Start)
                continue;
            if (position >= interval.End)
                break;
            if (!interval.IsLiveAcross(position))
                continue;
            if (previous < 0 || !SharesCallPreservation(interval, previous, position))
                preservationCost += 2 * WeightAt(position);
            if (preservationCost >= memoryCost)
                return true;
            previous = position;
        }

        return false;
    }

    private static ImmutableArray<MachineRegister> AllowedRegistersForInterval(
        LiveInterval interval,
        ImmutableArray<MachineRegister> physicalRegisters,
        IReadOnlyDictionary<LirVirtualRegister, List<MachineRegister>> abiPreferences,
        ImmutableArray<MachineRegister> callArgumentRegisters,
        TargetInfo target,
        ImmutableArray<LirRegisterClass> callBoundarySplitClasses,
        IReadOnlyList<int> callPositions,
        IReadOnlyDictionary<int, Dictionary<LirVirtualRegister, MachineRegister>> callArgumentTargets,
        ClobberTable instructionClobbers,
        IReadOnlyList<InlineAssemblySite> inlineAssemblySites,
        IReadOnlyDictionary<MachineRegister, int> incomingRegisterReleasePositions)
    {
        if (interval.Register.HasFixedRegister)
            return ImmutableArray.Create(interval.Register.FixedRegister);

        var spansCall = IntervalSpansPosition(interval, callPositions);
        var safeCallArgumentRegister = SafeCallArgumentRegister(interval, callPositions, callArgumentTargets, out var liveBeforeCall);
        var clobbered = instructionClobbers.Clobbered(interval);
        var canSplitAcrossCalls = callBoundarySplitClasses.Contains(interval.Register.RegisterClass);
        var preferCalleeSaved = canSplitAcrossCalls && spansCall;
        var orderedRegisters = preferCalleeSaved
            ? physicalRegisters.OrderByDescending(register => TargetRegisterInfo.IsCalleeSaved(target, register)).ToImmutableArray()
            : physicalRegisters;
        var builder = ImmutableArray.CreateBuilder<MachineRegister>();
        foreach (var register in orderedRegisters)
        {
            if (incomingRegisterReleasePositions.TryGetValue(register, out var releasePosition) &&
                interval.Start <= releasePosition &&
                !(interval.Start == releasePosition && HasRegisterPreference(interval.Register, register, abiPreferences)))
                continue;
            if (spansCall && !CanUseRegisterAcrossCall(target, interval, register) && !canSplitAcrossCalls)
                continue;
            if (liveBeforeCall && ContainsRegister(callArgumentRegisters, register))
            {
                if (canSplitAcrossCalls)
                {
                    if (HasUnpreservedLiveBeforeCall(interval, callPositions) && register != safeCallArgumentRegister)
                        continue;
                }
                else if (register != safeCallArgumentRegister)
                {
                    continue;
                }
            }
            if (clobbered is not null && clobbered.Contains(register))
                continue;

            var reserved = false;
            foreach (var site in inlineAssemblySites)
            {
                bool named = site.ReservedRegisters.Contains(register);
                if (!named && !site.ConservativeRegisters.Contains(register))
                    continue;
                bool liveBefore = interval.IsLiveBefore(site.Position);
                bool defined = interval.HasDefinitionAtPosition(site.Position);
                if (!liveBefore && !defined)
                    continue;

                if (named)
                {
                    bool ownFixedInput = liveBefore &&
                        !interval.IsLiveAcross(site.Position) &&
                        site.InputTargets.TryGetValue(interval.Register, out MachineRegister inputTarget) &&
                        inputTarget == register;
                    bool ownFixedOutput = defined &&
                        site.OutputTargets.TryGetValue(interval.Register, out MachineRegister outputTarget) &&
                        outputTarget == register;
                    if (ownFixedInput || ownFixedOutput)
                        continue;
                }
                else if (site.GenericRegisters.Contains(interval.Register))
                {
                    continue;
                }

                reserved = true;
                break;
            }

            if (!reserved)
                builder.Add(register);
        }

        return builder.ToImmutable();
    }

    private sealed class ClobberTable
    {
        private readonly List<int> _positions = new();
        private readonly List<ImmutableHashSet<MachineRegister>> _registers = new();
        private readonly List<ImmutableHashSet<MachineRegister>> _early = new();

        public void Clear()
        {
            _positions.Clear();
            _registers.Clear();
            _early.Clear();
        }

        public void Add(int position, ImmutableHashSet<MachineRegister> registers, ImmutableHashSet<MachineRegister> early)
        {
            _positions.Add(position);
            _registers.Add(registers);
            _early.Add(early);
        }

        public HashSet<MachineRegister>? Clobbered(LiveInterval interval)
        {
            var index = _positions.BinarySearch(Math.Max(0, interval.Start - 1));
            if (index < 0)
                index = ~index;

            HashSet<MachineRegister>? clobbered = null;
            for (; index < _positions.Count && _positions[index] < interval.End; index++)
            {
                var position = _positions[index];
                if (interval.IsLiveBefore(position))
                    (clobbered ??= new HashSet<MachineRegister>()).UnionWith(_registers[index]);
                else if (!_early[index].IsEmpty && (interval.HasDefinitionAtPosition(position) || interval.HasDefinitionAtPosition(position + 1)))
                    (clobbered ??= new HashSet<MachineRegister>()).UnionWith(_early[index]);
            }

            return clobbered;
        }

        public bool Clobbers(int position, MachineRegister register)
        {
            var index = _positions.BinarySearch(position);
            return index >= 0 && _registers[index].Contains(register);
        }
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

    private static int CountSpannedPositions(LiveInterval interval, IReadOnlyList<int> positions)
    {
        var count = 0;
        foreach (var position in positions)
        {
            if (position < interval.Start)
                continue;
            if (position >= interval.End)
                break;
            if (interval.IsLiveAcross(position))
                count++;
        }

        return count;
    }

    private static bool HasUnpreservedLiveBeforeCall(LiveInterval interval, IReadOnlyList<int> positions)
    {
        foreach (var position in positions)
        {
            if (position < interval.Start)
                continue;
            if (position >= interval.End)
                break;
            if (interval.IsLiveBefore(position) && !interval.IsLiveAcross(position))
                return true;
        }

        return false;
    }

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
        IReadOnlyDictionary<LirVirtualRegister, List<LirVirtualRegister>> phiWebs,
        IReadOnlyDictionary<ValueNumber, MachineRegister> valueNumberPreferredRegisters,
        IReadOnlyDictionary<LirVirtualRegister, LirInstruction> definingInstructions,
        TargetInfo target,
        bool preferCalleeSaved,
        Func<LirVirtualRegister, bool> crossesCalls)
    {
        if (interval.Register.HasFixedRegister && IsFreeRegister(interval, interval.Register.FixedRegister, physicalRegisters, active))
            return interval.Register.FixedRegister;

        abiPreferences.TryGetValue(interval.Register, out var preferredRegisters);
        phiWebs.TryGetValue(interval.Register, out var web);

        // A web shares one register: each member placed in one saves its copy, the ABI register the one at the use
        var webPlaced = false;
        if (web is not null)
        {
            Span<int> placed = stackalloc int[256];
            foreach (var member in web)
            {
                if (allocations.TryGetValue(member, out var memberAllocation) && !memberAllocation.IsSpilled)
                {
                    placed[(int)memberAllocation.PhysicalRegister]++;
                    webPlaced = true;
                }
            }

            if (webPlaced)
            {
                var joined = MachineRegister.Invalid;
                var joinedScore = 0;
                foreach (var member in web)
                {
                    if (!allocations.TryGetValue(member, out var memberAllocation) || memberAllocation.IsSpilled)
                        continue;
                    var candidate = memberAllocation.PhysicalRegister;
                    var score = placed[(int)candidate] + (preferredRegisters is not null && preferredRegisters.Contains(candidate) ? 1 : 0);
                    if (score <= joinedScore ||
                        preferCalleeSaved && !TargetRegisterInfo.IsCalleeSaved(target, candidate) ||
                        !IsFreeRegister(interval, candidate, physicalRegisters, active))
                        continue;
                    joined = candidate;
                    joinedScore = score;
                }
                if (joined != MachineRegister.Invalid)
                    return joined;
            }
            else if (!preferCalleeSaved)
            {
                foreach (var member in web)
                {
                    if (crossesCalls(member))
                    {
                        preferCalleeSaved = true;
                        break;
                    }
                }
            }
        }

        if (FreeAbiRegister() is var abiRegister && abiRegister != MachineRegister.Invalid)
            return abiRegister;

        if (web is not null && !webPlaced)
        {
            foreach (var member in web)
            {
                if (member == interval.Register || !abiPreferences.TryGetValue(member, out var memberRegisters))
                    continue;
                foreach (var preferred in memberRegisters)
                {
                    if (preferCalleeSaved && !TargetRegisterInfo.IsCalleeSaved(target, preferred))
                        continue;
                    if (IsFreeRegister(interval, preferred, physicalRegisters, active))
                        return preferred;
                }
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
                if (preferCalleeSaved && !TargetRegisterInfo.IsCalleeSaved(target, preferred))
                    continue;
                if (IsFreeRegister(interval, preferred, physicalRegisters, active))
                    return preferred;
            }

            for (var i = preferredSources.Count - 1; i >= 0; i--)
            {
                var source = preferredSources[i];
                if (allocations.ContainsKey(source) || !abiPreferences.TryGetValue(source, out var sourceRegisters))
                    continue;
                var sourceCrossesCalls = preferCalleeSaved || crossesCalls(source);
                foreach (var preferred in sourceRegisters)
                {
                    if (sourceCrossesCalls && !TargetRegisterInfo.IsCalleeSaved(target, preferred))
                        continue;
                    if (IsFreeRegister(interval, preferred, physicalRegisters, active))
                        return preferred;
                }
            }
        }

        var valueNumber = interval.Register.ValueNumber;
        if (valueNumber is not null &&
            !valueNumber.IsMemoryDependent &&
            !valueNumber.IsUnique &&
            valueNumberPreferredRegisters.TryGetValue(valueNumber, out var valueNumberRegister) &&
            (!preferCalleeSaved || TargetRegisterInfo.IsCalleeSaved(target, valueNumberRegister)) &&
            IsFreeRegister(interval, valueNumberRegister, physicalRegisters, active))
        {
            return valueNumberRegister;
        }

        if (web is not null && !webPlaced && preferCalleeSaved)
        {
            foreach (var register in physicalRegisters)
            {
                if (TargetRegisterInfo.IsCalleeSaved(target, register) && IsFreeRegister(interval, register, physicalRegisters, active))
                    return register;
            }
        }

        if (web is not null && definingInstructions.TryGetValue(interval.Register, out LirInstruction? definition))
        {
            foreach (MachineRegister register in physicalRegisters)
            {
                if (IsFreeRegister(interval, register, physicalRegisters, active) &&
                    !HoldsOperandOfOtherWeb(definition, register, web, allocations, phiWebs))
                {
                    return register;
                }
            }
        }

        return FirstFreeRegister(interval, physicalRegisters, active);

        MachineRegister FreeAbiRegister()
        {
            if (preferredRegisters is null)
                return MachineRegister.Invalid;
            foreach (var preferred in preferredRegisters)
            {
                if (preferCalleeSaved && !TargetRegisterInfo.IsCalleeSaved(target, preferred))
                    continue;
                if (IsFreeRegister(interval, preferred, physicalRegisters, active))
                    return preferred;
            }
            return MachineRegister.Invalid;
        }
    }

    // A web member in the register of another web's operand dying under it turns that loop's latch copies into a cycle
    private static bool HoldsOperandOfOtherWeb(
        LirInstruction definition,
        MachineRegister register,
        List<LirVirtualRegister> web,
        IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
        IReadOnlyDictionary<LirVirtualRegister, List<LirVirtualRegister>> phiWebs)
    {
        foreach (LirOperand operand in definition.Operands)
        {
            if (operand.Kind == LirOperandKind.Register && operand.Register is not null &&
                allocations.TryGetValue(operand.Register, out VirtualRegisterAllocation? allocation) &&
                !allocation.IsSpilled && allocation.PhysicalRegister == register &&
                phiWebs.TryGetValue(operand.Register, out List<LirVirtualRegister>? operandWeb) &&
                !ReferenceEquals(operandWeb, web))
            {
                return true;
            }
        }

        return false;
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

    /// <summary>A value wider than one register needs an aligned run, the way a vector group is addressed</summary>
    private static bool IsGroupBase(MachineRegister register, int registerCount)
        => registerCount <= 1 || (((int)register - (int)MachineRegister.V0) & (registerCount - 1)) == 0;

    private static bool IsFreeRegister(LiveInterval current, MachineRegister register, ImmutableArray<MachineRegister> physicalRegisters, List<LiveInterval> active)
    {
        if (register == MachineRegister.Invalid || !IsGroupBase(register, current.RegisterCount))
            return false;

        for (var offset = 0; offset < current.RegisterCount; offset++)
        {
            var member = (MachineRegister)((int)register + offset);
            if (!ContainsRegister(physicalRegisters, member))
                return false;

            foreach (var interval in active)
            {
                if (interval.Occupies(member) && interval.Overlaps(current))
                    return false;
            }
        }

        return true;
    }

    private static MachineRegister FirstFreeRegister(LiveInterval current, ImmutableArray<MachineRegister> physicalRegisters, List<LiveInterval> active)
    {
        foreach (var register in physicalRegisters)
        {
            if (IsFreeRegister(current, register, physicalRegisters, active))
                return register;
        }

        return MachineRegister.Invalid;
    }

    private static void SpillAtInterval(
        LiveInterval current,
        List<LiveInterval> active,
        Dictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
        Dictionary<ValueNumber, MachineRegister> valueNumberPreferredRegisters,
        ImmutableArray<MachineRegister> allowedRegisters,
        Func<LiveInterval, long> weightOf)
    {
        List<LiveInterval>? spill = null;
        var spillRegister = MachineRegister.Invalid;
        var spillWeight = 0L;
        var spillNextUse = -1;
        var spillEnd = -1;
        foreach (var register in allowedRegisters)
        {
            if (!IsGroupBase(register, current.RegisterCount) || !CoversGroup(register, current.RegisterCount, allowedRegisters))
                continue;

            var conflicts = new List<LiveInterval>();
            var hasFixedConflict = false;
            var weight = 0L;
            var nearestNextUse = int.MaxValue;
            var farthestEnd = -1;
            foreach (var candidate in active)
            {
                if (!OverlapsGroup(candidate, register, current.RegisterCount) || !candidate.Overlaps(current))
                    continue;
                if (candidate.Register.HasFixedRegister)
                {
                    hasFixedConflict = true;
                    break;
                }

                conflicts.Add(candidate);
                weight += weightOf(candidate);
                nearestNextUse = Math.Min(nearestNextUse, candidate.NextUseAtOrAfter(current.Start));
                farthestEnd = Math.Max(farthestEnd, candidate.End);
            }

            if (hasFixedConflict || conflicts.Count == 0)
                continue;

            if (spill is null ||
                weight < spillWeight ||
                weight == spillWeight && (nearestNextUse > spillNextUse ||
                    nearestNextUse == spillNextUse && conflicts.Count < spill.Count ||
                    nearestNextUse == spillNextUse && conflicts.Count == spill.Count && farthestEnd > spillEnd))
            {
                spill = conflicts;
                spillRegister = register;
                spillWeight = weight;
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

        var currentWeight = weightOf(current);
        var currentNextUse = current.NextUseAtOrAfter(current.Start);
        if (current.Register.HasFixedRegister ||
            spillWeight < currentWeight ||
            spillWeight == currentWeight && (spillNextUse > currentNextUse || spillNextUse == currentNextUse && spillEnd > current.End))
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

    private static bool CoversGroup(MachineRegister register, int registerCount, ImmutableArray<MachineRegister> registers)
    {
        for (var offset = 0; offset < registerCount; offset++)
        {
            if (!ContainsRegister(registers, (MachineRegister)((int)register + offset)))
                return false;
        }

        return true;
    }

    private static bool OverlapsGroup(LiveInterval interval, MachineRegister register, int registerCount)
    {
        for (var offset = 0; offset < registerCount; offset++)
        {
            if (interval.Occupies((MachineRegister)((int)register + offset)))
                return true;
        }

        return false;
    }

    private static void InsertActive(List<LiveInterval> active, LiveInterval interval)
    {
        active.Add(interval);
        active.Sort(static (a, b) => a.End.CompareTo(b.End));
    }

    private IReadOnlyDictionary<int, ImmutableArray<CallPreservation>> BuildCallPreservations(
        IReadOnlyDictionary<LirVirtualRegister, LiveInterval> intervals,
        IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
        out ImmutableHashSet<MachineRegister> preservationRegisters,
        out ImmutableHashSet<LirVirtualRegister> stackPreservedRegisters)
    {
        var usedPreservationRegisters = ImmutableHashSet.CreateBuilder<MachineRegister>();
        var stackPreserved = ImmutableHashSet.CreateBuilder<LirVirtualRegister>();
        var result = new Dictionary<int, ImmutableArray<CallPreservation>>();

        if (_options.CallBoundarySplitClasses.IsDefaultOrEmpty)
        {
            preservationRegisters = usedPreservationRegisters.ToImmutable();
            stackPreservedRegisters = stackPreserved.ToImmutable();
            return result;
        }

        var previousPosition = -1;
        ImmutableArray<CallPreservation>.Builder? previous = null;
        foreach (var position in _callPositions)
        {
            var occupied = new HashSet<MachineRegister>();
            foreach (var pair in allocations)
            {
                var allocation = pair.Value;
                if (allocation.IsSpilled || allocation.PhysicalRegister == MachineRegister.Invalid)
                    continue;
                if (!intervals.TryGetValue(pair.Key, out var interval))
                    continue;
                if (interval.IsLiveBefore(position) || interval.HasDefinitionAtPosition(position + 1))
                    occupied.Add(allocation.PhysicalRegister);
            }

            var builder = ImmutableArray.CreateBuilder<CallPreservation>();
            foreach (var pair in allocations.OrderBy(static p => p.Key.Ordinal))
            {
                var allocation = pair.Value;
                if (allocation.IsSpilled || allocation.PhysicalRegister == MachineRegister.Invalid)
                    continue;
                if (!_options.CallBoundarySplitClasses.Contains(pair.Key.RegisterClass))
                    continue;
                if (TargetRegisterInfo.IsCalleeSaved(_target, allocation.PhysicalRegister))
                    continue;
                if (!intervals.TryGetValue(pair.Key, out var interval) || !interval.IsLiveAcross(position))
                    continue;

                if (previous is not null && SharesCallPreservation(interval, previousPosition, position) &&
                    TryContinuePreservation(pair.Key, previous, builder, previousPosition, position, occupied, intervals, allocations))
                    continue;

                var preservationRegister = FindCallPreservationRegister(interval, occupied);
                if (preservationRegister != MachineRegister.Invalid)
                {
                    occupied.Add(preservationRegister);
                    usedPreservationRegisters.Add(preservationRegister);
                    builder.Add(CallPreservation.InRegister(pair.Key, allocation.PhysicalRegister, preservationRegister));
                }
                else
                {
                    stackPreserved.Add(pair.Key);
                    builder.Add(CallPreservation.OnStack(pair.Key, allocation.PhysicalRegister));
                }
            }

            if (previous is { Count: not 0 })
                result[previousPosition] = previous.ToImmutable();
            previous = builder;
            previousPosition = position;
        }

        if (previous is { Count: not 0 })
            result[previousPosition] = previous.ToImmutable();

        preservationRegisters = usedPreservationRegisters.ToImmutable();
        stackPreservedRegisters = stackPreserved.ToImmutable();
        return result;
    }

    // With no reference between two calls of a straight run of blocks the value can stay where the first call left it
    private bool SharesCallPreservation(LiveInterval interval, int first, int second)
        => _blockRuns[BlockIndexAt(first)] == _blockRuns[BlockIndexAt(second)] && !interval.IsReferencedBetween(first, second);

    private bool TryContinuePreservation(
        LirVirtualRegister register,
        ImmutableArray<CallPreservation>.Builder previous,
        ImmutableArray<CallPreservation>.Builder current,
        int previousPosition,
        int position,
        HashSet<MachineRegister> occupied,
        IReadOnlyDictionary<LirVirtualRegister, LiveInterval> intervals,
        IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations)
    {
        for (var i = 0; i < previous.Count; i++)
        {
            var preservation = previous[i];
            if (!ReferenceEquals(preservation.Register, register))
                continue;

            if (preservation.UsesRegister)
            {
                if (occupied.Contains(preservation.PreservationRegister) ||
                    !IsRegisterIdleBetween(preservation.PreservationRegister, previousPosition, position, intervals, allocations))
                    return false;
                occupied.Add(preservation.PreservationRegister);
            }

            previous[i] = preservation.WithoutRestore();
            current.Add(preservation.WithoutSave());
            return true;
        }

        return false;
    }

    private bool IsRegisterIdleBetween(
        MachineRegister register,
        int first,
        int last,
        IReadOnlyDictionary<LirVirtualRegister, LiveInterval> intervals,
        IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations)
    {
        for (var position = first + 2; position < last; position += 2)
        {
            if (_instructionClobbers.Clobbers(position, register))
                return false;
        }

        foreach (var site in _inlineAssemblySites)
        {
            if (site.Position > first && site.Position < last && (site.TouchedRegisters.Contains(register) || site.ReservedRegisters.Contains(register)))
                return false;
        }

        foreach (var pair in allocations)
        {
            if (!pair.Value.IsSpilled && pair.Value.PhysicalRegister == register &&
                intervals.TryGetValue(pair.Key, out var interval) && interval.OverlapsSpan(first, last))
                return false;
        }

        return true;
    }

    private MachineRegister FindCallPreservationRegister(LiveInterval interval, HashSet<MachineRegister> occupied)
    {
        var registers = interval.Register.RegisterClass switch
        {
            LirRegisterClass.General or LirRegisterClass.Address => _generalRegisters,
            LirRegisterClass.Floating => _options.FloatingRegisters,
            LirRegisterClass.Vector => _options.VectorRegisters,
            _ => ImmutableArray<MachineRegister>.Empty,
        };

        foreach (var register in registers)
        {
            if (occupied.Contains(register))
                continue;
            if (!CanUseRegisterAcrossCall(_target, interval, register))
                continue;
            return register;
        }

        return MachineRegister.Invalid;
    }

    private static IReadOnlyDictionary<int, ImmutableArray<CallPreservation>> BindCallPreservationStackOffsets(
        IReadOnlyDictionary<int, ImmutableArray<CallPreservation>> preservations,
        IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations)
    {
        if (preservations.Count == 0)
            return preservations;

        var result = new Dictionary<int, ImmutableArray<CallPreservation>>();
        foreach (var pair in preservations)
        {
            var builder = ImmutableArray.CreateBuilder<CallPreservation>(pair.Value.Length);
            foreach (var preservation in pair.Value)
            {
                if (preservation.UsesRegister)
                {
                    builder.Add(preservation);
                    continue;
                }

                if (!allocations.TryGetValue(preservation.Register, out var allocation) || allocation.StackOffset < 0)
                    throw new InvalidOperationException("Missing call-preservation spill slot for " + preservation.Register.Name + ".");
                builder.Add(preservation.WithStackOffset(allocation.StackOffset));
            }
            result[pair.Key] = builder.ToImmutable();
        }

        return result;
    }

    private static bool HasDynamicStack(LirFunction function)
        => function.Blocks.Any(static block => block.Instructions.Any(static instruction =>
            instruction.Kind is LirInstructionKind.StackAllocate or LirInstructionKind.StackRestore));

    private StackFrameMap LayoutStackFrame(
        Dictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
        ImmutableHashSet<MachineRegister> preservationRegisters,
        Func<MachineRegister, int, bool> isScratchFree)
    {
        var offset = 0;

        // Run-time stack allocation addresses the frame from a base register and puts new space above an aligned outgoing area
        var dynamicStack = HasDynamicStack(_function);
        var outgoingSize = ComputeOutgoingArgumentAreaSize();
        if (dynamicStack)
            outgoingSize = AlignUp(outgoingSize, _options.StackAlignment);
        offset = AlignUp(offset, _options.StackArgumentSlotSize);
        var outgoingOffset = offset;
        offset = checked(offset + outgoingSize);

        var frameBaseSaveOffset = -1;
        if (dynamicStack)
        {
            offset = AlignUp(offset, _target.PointerAlignment);
            frameBaseSaveOffset = offset;
            offset = checked(offset + _target.PointerSize);
        }

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
            offset = checked(offset + Math.Max(1, ParameterHomeSize(slot)));
        }
        var stackSlotAreaSize = checked(offset - stackSlotAreaOffset);

        var spillOffsets = new Dictionary<LirVirtualRegister, int>();
        var spillAreaOffset = offset;
        foreach (var pair in allocations.OrderBy(static p => p.Key.Ordinal))
        {
            // A register whose storage is a named slot needs no spill slot of its own
            if (!pair.Value.NeedsStackSlot || pair.Key.HomeSlot is not null)
                continue;

            offset = AlignUp(offset, SpillSlotAlignmentFor(pair.Key));
            spillOffsets.Add(pair.Key, offset);
            offset = checked(offset + SpillSlotSizeFor(pair.Key));
        }
        var spillAreaSize = checked(offset - spillAreaOffset);

        var stackAccessScratch = _scratchNeeds?.StackAccessScratch(offset, dynamicStack) ?? 0;
        var parallelCopyTempSize = ComputeParallelCopyTempSize(allocations, spillOffsets, isScratchFree, stackAccessScratch, out var mayBorrow);
        offset = AlignUp(offset, _options.StackAlignment);
        var parallelCopyTempOffset = offset;
        offset = checked(offset + parallelCopyTempSize);
        var floatingImmediateTempSize = ComputeFloatingImmediateTempSize();
        if (floatingImmediateTempSize != 0)
            offset = AlignUp(offset, _options.SpillSlotAlignment);
        var floatingImmediateTempOffset = offset;
        offset = checked(offset + floatingImmediateTempSize);

        var scratchBorrowSize = mayBorrow ? _scratchNeeds!.BorrowAreaSize : 0;
        if (scratchBorrowSize != 0)
            offset = AlignUp(offset, 16);
        var scratchBorrowOffset = scratchBorrowSize != 0 ? offset : -1;
        offset = checked(offset + scratchBorrowSize);

        var usedRegisters = allocations.Values
            .Where(static a => !a.IsSpilled && a.PhysicalRegister != MachineRegister.Invalid)
            .Select(static a => a.PhysicalRegister)
            .Concat(preservationRegisters)
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
            savedRegisterOffsets,
            frameBaseSaveOffset,
            scratchBorrowOffset,
            scratchBorrowSize);
    }

    private bool RequiresHiddenReturnBuffer()
    {
        var returnType = _function.Symbol?.FunctionType?.ReturnType;
        return returnType.HasValue && CAbi.RequiresHiddenReturnBuffer(_target, returnType.Value);
    }

    /// <summary>Reports how much room a slot takes, which for a register-passed argument is whole registers</summary>
    private int ParameterHomeSize(LirStackSlot slot)
    {
        var size = Math.Max(1, slot.Size);
        if (!slot.IsParameter)
            return size;

        var value = CAbi.ClassifyValue(_target, slot.Type, isReturn: false, isVariadicUnnamedArgument: false);
        if (value.PassingKind != AbiPassingKind.MultiRegister && value.PassingKind != AbiPassingKind.Scalar)
            return size;

        return Math.Max(size, AlignUp(size, _options.SpillSlotSize));
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

    private int ComputeFloatingImmediateTempSize()
    {
        if (!NeedsFloatingImmediateTemp())
            return 0;

        var minimum = _target.Architecture == TargetArchitectureKind.I386 ? 32 : 8;
        return AlignUp(Math.Max(minimum, _options.SpillSlotSize), _options.SpillSlotAlignment);
    }

    private bool NeedsFloatingImmediateTemp()
    {
        if (_target.IsRegisterBytecode)
            return false;

        if (_target.Architecture == TargetArchitectureKind.I386)
            return HasWideScalarUse();

        if (_target.IsRiscV)
            return HasWideScalarUse() || HasRiscVScalarBitsTempUse();

        if (_target.Architecture == TargetArchitectureKind.X86_64 && TargetRegisterInfo.IsWindowsX64(_target))
            return HasWindowsX64VariadicFloatingArgument();

        return false;
    }

    private bool HasWideScalarUse()
    {
        foreach (var register in _function.VirtualRegisters)
        {
            if (RequiresStackBackedScalar(register))
                return true;
        }

        foreach (var instruction in _function.Blocks.SelectMany(static b => b.Instructions))
        {
            if (instruction.Result is not null && IsWideScalarType(instruction.Result.Type))
                return true;

            foreach (var operand in instruction.Operands)
            {
                if (IsWideScalarType(operand.Type))
                    return true;
            }

            foreach (var copy in instruction.ParallelCopies)
            {
                if (IsWideScalarType(copy.Destination.Type) || IsWideScalarType(copy.Source.Type))
                    return true;
            }
        }

        return false;
    }

    private bool IsWideScalarType(QualifiedType type)
    {
        if (type.Type.Kind is TypeKind.Pointer or TypeKind.Array or TypeKind.Function or TypeKind.Struct or TypeKind.Union or TypeKind.Vector)
            return false;
        if (CAbi.IsFloating(type) && CAbi.UsesHardwareFloatingRegister(_target, type, isVariadicUnnamedArgument: false))
            return false;
        return SizeOfStorage(type) > Math.Max(1, _target.RegisterSize);
    }

    private bool HasRiscVScalarBitsTempUse()
    {
        foreach (var instruction in _function.Blocks.SelectMany(static b => b.Instructions))
        {
            if (instruction.Kind != LirInstructionKind.Call)
                continue;

            var signature = instruction.CallSignature;
            for (var i = 1; i < instruction.Operands.Length; i++)
            {
                var operand = instruction.Operands[i];
                var isVariadicUnnamed = signature is not null && signature.IsVariadic && i - 1 >= signature.Parameters.Length;
                var value = CAbi.ClassifyValue(_target, operand.Type, isReturn: false, isVariadicUnnamed);
                if (value.PassingKind == AbiPassingKind.MultiRegister &&
                    !CAbi.IsAggregate(operand.Type) &&
                    !RequiresStackBackedScalarStorage(operand.Type))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool RequiresStackBackedScalarStorage(QualifiedType type)
    {
        if (CAbi.IsAggregate(type) || type.Type.Kind is TypeKind.Pointer or TypeKind.Array or TypeKind.Function or TypeKind.Vector)
            return false;
        if (CAbi.UsesHardwareFloatingRegister(_target, type, isVariadicUnnamedArgument: false))
            return false;
        return SizeOfStorage(type) > Math.Max(1, _target.RegisterSize);
    }

    private bool HasWindowsX64VariadicFloatingArgument()
    {
        foreach (var instruction in _function.Blocks.SelectMany(static b => b.Instructions))
        {
            var signature = instruction.CallSignature;
            if (instruction.Kind != LirInstructionKind.Call || signature is null || !signature.IsVariadic)
                continue;

            var cursor = new AbiCursor();
            if (instruction.Result is not null && CAbi.RequiresHiddenReturnBuffer(_target, instruction.Result.Type))
                _ = CAbi.AssignHiddenReturnBufferLocation(_target, ref cursor, _options.StackArgumentSlotSize);

            for (var i = 1; i < instruction.Operands.Length; i++)
            {
                var operand = instruction.Operands[i];
                var isVariadicUnnamed = i - 1 >= signature.Parameters.Length;
                var value = CAbi.ClassifyValue(_target, operand.Type, isReturn: false, isVariadicUnnamed);
                foreach (var location in CAbi.AssignArgumentLocations(value, ref cursor, _options.StackArgumentSlotSize))
                {
                    if (isVariadicUnnamed && CAbi.IsFloating(operand.Type) && location.Kind == AbiLocationKind.Register)
                        return true;
                }
            }
        }

        return false;
    }

    private int ComputeParallelCopyTempSize(
        IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
        IReadOnlyDictionary<LirVirtualRegister, int> spillOffsets,
        Func<MachineRegister, int, bool> isScratchFree,
        int stackAccessScratch,
        out bool mayBorrow)
    {
        var maxSize = 0;
        var position = 0;
        mayBorrow = false;
        foreach (var instruction in _function.Blocks.SelectMany(static b => b.Instructions))
        {
            var instructionPosition = position;
            position += 2;

            if (instruction.Kind != LirInstructionKind.ParallelCopy)
            {
                mayBorrow = mayBorrow || _scratchNeeds is not null && MayBorrowScratch(instruction, instructionPosition, allocations, isScratchFree, int.MaxValue, stackAccessScratch);
                continue;
            }

            var physicalCopies = ImmutableArray.CreateBuilder<LirParallelCopy>(instruction.ParallelCopies.Length);
            foreach (var copy in instruction.ParallelCopies)
            {
                if (RequiresPhysicalParallelCopy(copy, allocations, spillOffsets))
                    physicalCopies.Add(copy);
            }

            if (physicalCopies.Count <= 1)
                continue;

            var copies = physicalCopies.ToImmutable();
            if (!HasBlockCopyParallelCopy(copies) && CanOrderParallelCopies(copies, allocations, spillOffsets))
                continue;
            if (!HasBlockCopyParallelCopy(copies) && TryChooseCycleRegisters(copies, instructionPosition, allocations, out ParallelCopyCycleRegisters cycleRegisters))
            {
                _parallelCopyCycleRegisters.Add(instruction, cycleRegisters);
                continue;
            }

            // Through the frame temporaries the copies read into one general scratch register and write out of another
            mayBorrow = mayBorrow || _scratchNeeds is not null && MayBorrowScratch(instruction, instructionPosition, allocations, isScratchFree, 2, 0);

            var size = 0;
            foreach (var copy in copies)
                size = checked(size + ParallelCopyTempSlotSize(copy));
            maxSize = Math.Max(maxSize, size);
        }

        return maxSize;
    }

    // Register moves that form a cycle go round through a register nothing holds at the copy, found after allocation
    private bool TryChooseCycleRegisters(
        ImmutableArray<LirParallelCopy> copies,
        int position,
        IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
        out ParallelCopyCycleRegisters cycleRegisters)
    {
        cycleRegisters = default;
        if (_hasRegisterGroups)
            return false;

        var destinations = new MachineRegister[copies.Length];
        var sources = new MachineRegister[copies.Length];
        var floating = new Dictionary<MachineRegister, bool>();
        for (var i = 0; i < copies.Length; i++)
        {
            LirParallelCopy copy = copies[i];
            LirRegisterClass registerClass = copy.Destination.RegisterClass;
            if (registerClass is not (LirRegisterClass.General or LirRegisterClass.Address or LirRegisterClass.Floating) ||
                !TryGetDestinationPhysicalRegister(copy.Destination, allocations, out destinations[i]))
                return false;

            bool isFloating = registerClass == LirRegisterClass.Floating;
            if (copy.Source.Kind == LirOperandKind.Immediate)
            {
                if (isFloating || copy.Source.Immediate is null or string or float or double or decimal)
                    return false;
                sources[i] = MachineRegister.Invalid;
            }
            else if (copy.Source.Register is not LirVirtualRegister source || !TryGetOperandPhysicalRegister(copy.Source, allocations, out sources[i]) ||
                     (source.RegisterClass == LirRegisterClass.Floating) != isFloating || _target.SizeOf(source.Type) != _target.SizeOf(copy.Destination.Type))
            {
                return false;
            }

            floating[destinations[i]] = isFloating;
            if (sources[i] != MachineRegister.Invalid)
                floating[sources[i]] = isFloating;
        }

        bool generalCycle = false;
        bool floatingCycle = false;
        RegisterMoveSequencer.Order(destinations, sources, blocked =>
        {
            if (floating[blocked])
                floatingCycle = true;
            else
                generalCycle = true;
            return MachineRegister.Invalid;
        });

        MachineRegister general = MachineRegister.Invalid;
        MachineRegister floatingRegister = MachineRegister.Invalid;
        if (generalCycle && !TryFindFreeRegister(_generalRegisters, floating, position, out general) ||
            floatingCycle && !TryFindFreeRegister(_options.FloatingRegisters, floating, position, out floatingRegister))
            return false;

        cycleRegisters = new ParallelCopyCycleRegisters(general, floatingRegister);
        return generalCycle || floatingCycle;
    }

    private bool AssignInlineAssemblyRegisters(IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations)
    {
        var assigned = true;
        foreach (InlineAssemblySite site in _inlineAssemblySites)
        {
            if (site.Instruction.SourceStatement is not GimpleAsmStatement asmStatement)
                continue;

            Dictionary<MachineRegister, bool> taken = site.ReservedRegisters.ToDictionary(static register => register, static _ => false);
            var inputOwners = new Dictionary<MachineRegister, LirVirtualRegister>();
            ImmutableHashSet<MachineRegister>.Builder fresh = ImmutableHashSet.CreateBuilder<MachineRegister>();
            var outputs = new MachineRegister[asmStatement.Outputs.Length];
            var inputs = new MachineRegister[asmStatement.Inputs.Length];
            Array.Fill(outputs, MachineRegister.Invalid);
            Array.Fill(inputs, MachineRegister.Invalid);
            var satisfied = true;
            foreach ((int index, LirVirtualRegister destination) in site.GenericOutputs)
            {
                string constraint = asmStatement.Outputs[index].Constraint;
                MachineRegister register = OwnInlineAssemblyRegister(destination, constraint, allocations);
                if (register == MachineRegister.Invalid || taken.ContainsKey(register))
                    register = FreeInlineAssemblyRegister(site.Position, destination.Type, destination.RegisterClass, constraint, taken, fresh);
                outputs[index] = register;
                satisfied &= register != MachineRegister.Invalid && taken.TryAdd(register, false);
            }

            foreach ((int index, LirOperand value) in site.GenericInputs)
            {
                string constraint = asmStatement.Inputs[index].Constraint;
                LirVirtualRegister? source = value.Kind == LirOperandKind.Register ? value.Register : null;
                MachineRegister register = source is null ? MachineRegister.Invalid : OwnInlineAssemblyRegister(source, constraint, allocations);
                if (register != MachineRegister.Invalid && inputOwners.TryGetValue(register, out LirVirtualRegister? owner) && ReferenceEquals(owner, source))
                {
                    inputs[index] = register;
                    continue;
                }

                if (register == MachineRegister.Invalid || taken.ContainsKey(register))
                    register = FreeInlineAssemblyRegister(site.Position, value.Type, source?.RegisterClass ?? CAbi.PreferredLirRegisterClass(_target, value.Type), constraint, taken, fresh);
                else
                    inputOwners.Add(register, source!);
                inputs[index] = register;
                satisfied &= register != MachineRegister.Invalid && taken.TryAdd(register, false);
            }

            if (!satisfied)
            {
                if (!_conservativeAsmSites.Add(site.Instruction))
                    throw new InvalidOperationException($"Inline assembly at LIR position {site.Position} of '{_function.Symbol?.Name}' has more operands than free registers.");
                assigned = false;
                continue;
            }

            site.Registers = new InlineAssemblyRegisters(outputs.ToImmutableArray(), inputs.ToImmutableArray());
            if (fresh.Count != 0)
                site.TouchedRegisters = site.TouchedRegisters.Union(fresh);
        }

        return assigned;
    }

    private MachineRegister OwnInlineAssemblyRegister(LirVirtualRegister register, string constraint, IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations)
        => allocations.TryGetValue(register, out VirtualRegisterAllocation? allocation) && !allocation.IsSpilled && allocation.PhysicalRegister != MachineRegister.Invalid &&
           FitsInlineAssemblyConstraint(constraint, allocation.PhysicalRegister)
            ? allocation.PhysicalRegister
            : MachineRegister.Invalid;

    private MachineRegister FreeInlineAssemblyRegister(
        int position,
        QualifiedType type,
        LirRegisterClass registerClass,
        string constraint,
        Dictionary<MachineRegister, bool> taken,
        ImmutableHashSet<MachineRegister>.Builder fresh)
    {
        if (TargetRegisterInfo.RegisterGroupCount(_target, type) != 1)
            return MachineRegister.Invalid;
        // The emitter binds its scratch registers by what the allocation leaves free, which knows nothing of these
        ImmutableHashSet<MachineRegister>? scratch = _scratchNeeds?.Registers(ScratchClass.All);
        ImmutableArray<MachineRegister> candidates = ClassRegisters(registerClass)
            .Where(register => FitsInlineAssemblyConstraint(constraint, register) && (scratch is null || !scratch.Contains(register)))
            .ToImmutableArray();
        if (!TryFindFreeRegister(candidates, taken, position, out MachineRegister register) &&
            !TryFindFreeRegister(candidates, taken, position, out register, unusedCalleeSaved: true))
            return MachineRegister.Invalid;
        fresh.Add(register);
        return register;
    }

    // i386 'q' asks for a register with a byte form, which only eax, ecx, edx and ebx have
    private bool FitsInlineAssemblyConstraint(string constraint, MachineRegister register)
        => _target.Architecture != TargetArchitectureKind.I386 ||
           !InlineAsmConstraints.OperandConstraint(constraint).Contains('q') ||
           register is MachineRegister.X0 or MachineRegister.X1 or MachineRegister.X2 or MachineRegister.X3;

    // A callee-saved register the function never uses would need saving first
    private bool TryFindFreeRegister(ImmutableArray<MachineRegister> candidates, Dictionary<MachineRegister, bool> copied, int position, out MachineRegister register, bool unusedCalleeSaved = false)
    {
        foreach (MachineRegister candidate in candidates)
        {
            if (copied.ContainsKey(candidate) || _instructionClobbers.Clobbers(position, candidate) ||
                _incomingRegisterReleasePositions.TryGetValue(candidate, out int release) && release >= position)
                continue;

            var used = false;
            var held = false;
            // A group of up to eight vector registers is listed under its first one
            for (var first = (int)candidate; !held && first >= (_hasRegisterGroups ? (int)candidate - 7 : (int)candidate); first--)
            {
                if (!_registerOccupants.TryGetValue((MachineRegister)first, out List<LiveInterval>? occupants))
                    continue;
                foreach (LiveInterval interval in occupants)
                {
                    if (!interval.Occupies(candidate))
                        continue;
                    used = true;
                    held = interval.IsLiveBefore(position) || interval.HasDefinitionAtPosition(position) || interval.HasDefinitionAtPosition(position + 1);
                    if (held)
                        break;
                }
            }

            if (held || !used && !unusedCalleeSaved && TargetRegisterInfo.IsCalleeSaved(_target, candidate))
                continue;
            register = candidate;
            return true;
        }

        register = MachineRegister.Invalid;
        return false;
    }

    // The emitter borrows only past the spares and every free home of a class, a spilled value needing at most one
    private bool MayBorrowScratch(
        LirInstruction instruction,
        int position,
        IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
        Func<MachineRegister, int, bool> isScratchFree,
        int roleLimit,
        int stackAccessScratch)
    {
        var general = 0;
        var vector = 0;
        void Count(LirVirtualRegister? register)
        {
            if (register is null || !allocations.TryGetValue(register, out var allocation) || !allocation.IsSpilled)
                return;
            if (register.RegisterClass is LirRegisterClass.Floating or LirRegisterClass.Vector)
                vector++;
            else
                general++;
        }
        void CountOperand(LirOperand operand)
        {
            Count(operand.Register);
            if (operand.Address is not null)
                CountAddress(operand.Address);
            foreach (var part in operand.Parts)
                CountOperand(part);
        }
        void CountAddress(LirAddress address)
        {
            if (address.BaseOperand is not null)
                CountOperand(address.BaseOperand);
            if (address.Index is not null)
                CountOperand(address.Index);
            if (address.BaseAddress is not null)
                CountAddress(address.BaseAddress);
        }

        Count(instruction.Result);
        foreach (var operand in instruction.Operands)
            CountOperand(operand);
        if (instruction.Address is not null)
            CountAddress(instruction.Address);
        foreach (var copy in instruction.ParallelCopies)
        {
            Count(copy.Destination);
            CountOperand(copy.Source);
        }

        general = Math.Min(general, roleLimit);
        vector = Math.Min(vector, roleLimit);
        if (_foldableLoadAddresses.TryGetValue(position, out var foldedAddress))
            CountAddress(foldedAddress);
        if (general + vector != 0)
            general += stackAccessScratch;
        var needs = _scratchNeeds!;
        var kills = _scratchKills.TryGetValue(position, out var killed) ? killed : ScratchClass.None;
        return (kills & ScratchClass.General) == 0 && general > needs.SpareCount(ScratchClass.General) + FreeScratch(ScratchClass.General) ||
            (kills & ScratchClass.Floating) == 0 && vector > needs.SpareCount(ScratchClass.Floating) + FreeScratch(ScratchClass.Floating);

        int FreeScratch(ScratchClass scratchClass)
        {
            var free = 0;
            foreach (var register in needs.Registers(scratchClass))
            {
                if (isScratchFree(register, position))
                    free++;
            }
            return free;
        }
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

    private bool HasBlockCopyParallelCopy(ImmutableArray<LirParallelCopy> copies)
    {
        foreach (var copy in copies)
        {
            if (copy.Destination.RegisterClass == LirRegisterClass.Aggregate || RequiresStackBackedScalar(copy.Destination))
                return true;
            // The i386 emitter stages a 64-bit integer through the frame, even from registers
            if (_target.Architecture == TargetArchitectureKind.I386 && GimpleTypes.IsIntegerLike(copy.Destination.Type) &&
                SizeOfStorage(copy.Destination.Type) > _target.PointerSize)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reports whether the copies can be run in an order that never reads storage an earlier one
    /// wrote, which is what lets the frame skip a staging area for them.
    /// </summary>
    private static bool CanOrderParallelCopies(
        ImmutableArray<LirParallelCopy> copies,
        IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
        IReadOnlyDictionary<LirVirtualRegister, int> spillOffsets)
    {
        var destinations = new PhysicalStorageKey[copies.Length];
        var sources = new PhysicalStorageKey[copies.Length];
        for (var i = 0; i < copies.Length; i++)
        {
            destinations[i] = TryGetDestinationPhysicalRegister(copies[i].Destination, allocations, out var destinationRegister)
                ? PhysicalStorageKey.ForRegister(destinationRegister)
                : TryGetDestinationStackOffset(copies[i].Destination, allocations, spillOffsets, out var destinationOffset)
                    ? PhysicalStorageKey.ForStackOffset(destinationOffset)
                    : default;
            sources[i] = TryGetOperandPhysicalRegister(copies[i].Source, allocations, out var sourceRegister)
                ? PhysicalStorageKey.ForRegister(sourceRegister)
                : TryGetOperandStackOffset(copies[i].Source, allocations, spillOffsets, out var sourceOffset)
                    ? PhysicalStorageKey.ForStackOffset(sourceOffset)
                    : default;
        }

        return ParallelCopySequencer.TryOrder(destinations, sources, out _);
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

    private sealed class InlineAssemblySite
    {
        public LirInstruction Instruction { get; }
        public int Position { get; }
        public ImmutableHashSet<MachineRegister> ReservedRegisters { get; set; } = ImmutableHashSet<MachineRegister>.Empty;
        public ImmutableHashSet<MachineRegister> ConservativeRegisters { get; set; } = ImmutableHashSet<MachineRegister>.Empty;
        public ImmutableHashSet<MachineRegister> TouchedRegisters { get; set; } = ImmutableHashSet<MachineRegister>.Empty;
        public Dictionary<LirVirtualRegister, MachineRegister> InputTargets { get; } = new();
        public Dictionary<LirVirtualRegister, MachineRegister> OutputTargets { get; } = new();
        public List<(int Index, LirVirtualRegister Destination)> GenericOutputs { get; } = new();
        public List<(int Index, LirOperand Value)> GenericInputs { get; } = new();
        public HashSet<LirVirtualRegister> GenericRegisters { get; } = new();
        public InlineAssemblyRegisters Registers { get; set; } = new(ImmutableArray<MachineRegister>.Empty, ImmutableArray<MachineRegister>.Empty);

        public InlineAssemblySite(LirInstruction instruction, int position)
        {
            Instruction = instruction;
            Position = position;
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
        public int RegisterCount { get; set; } = 1;
        public long SpillWeight { get; set; } = -1;
        public IReadOnlyList<LiveRange> Ranges => _ranges;
        public List<int> UsePositions => _usePositions;
        public List<int> DefinitionPositions => _definitionPositions;

        /// <summary>Tests whether the assignment of this interval covers a physical register</summary>
        public bool Occupies(MachineRegister register)
            => PhysicalRegister != MachineRegister.Invalid &&
               register >= PhysicalRegister &&
               (int)register < (int)PhysicalRegister + RegisterCount;

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

        public bool HasDefinitionAtPosition(int position)
            => HasDefinitionAt(position);

        /// <summary>Tests for a use after the first call up to the second one, or a definition up to the second call's result</summary>
        public bool IsReferencedBetween(int firstCall, int secondCall)
            => HasPositionIn(_usePositions, firstCall + 1, secondCall) || HasPositionIn(_definitionPositions, firstCall + 1, secondCall + 1);

        private static bool HasPositionIn(List<int> positions, int low, int high)
        {
            var index = positions.BinarySearch(low);
            if (index < 0)
                index = ~index;
            return index < positions.Count && positions[index] <= high;
        }

        public bool OverlapsSpan(int start, int end)
        {
            foreach (var range in _ranges)
            {
                if (range.Start > end)
                    return false;
                if (start < range.End)
                    return true;
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

internal readonly struct PhysicalStorageKey : IEquatable<PhysicalStorageKey>
{
    private readonly int _registerPlusOne;
    private readonly int _stackOffsetPlusOne;

    private PhysicalStorageKey(int registerPlusOne, int stackOffsetPlusOne)
    {
        _registerPlusOne = registerPlusOne;
        _stackOffsetPlusOne = stackOffsetPlusOne;
    }

    public static PhysicalStorageKey ForRegister(MachineRegister register)
        => new PhysicalStorageKey((int)register + 1, 0);

    public static PhysicalStorageKey ForStackOffset(int stackOffset)
        => new PhysicalStorageKey(0, stackOffset + 1);

    public bool IsKnown => _registerPlusOne != 0 || _stackOffsetPlusOne != 0;

    public bool Equals(PhysicalStorageKey other)
        => _registerPlusOne == other._registerPlusOne && _stackOffsetPlusOne == other._stackOffsetPlusOne;

    public override bool Equals(object? obj) => obj is PhysicalStorageKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_registerPlusOne, _stackOffsetPlusOne);
}

/// <summary>Orders moves into distinct registers; a step with no move index breaks a cycle through a scratch</summary>
internal static class RegisterMoveSequencer
{
    public static ImmutableArray<(int Move, MachineRegister From, MachineRegister To)> Order(
        IReadOnlyList<MachineRegister> destinations,
        IReadOnlyList<MachineRegister> sources,
        Func<MachineRegister, MachineRegister> scratchFor)
    {
        var current = sources.ToArray();
        var pending = new List<int>();
        for (var i = 0; i < destinations.Count; i++)
        {
            if (current[i] != MachineRegister.Invalid && current[i] != destinations[i])
                pending.Add(i);
        }

        var steps = ImmutableArray.CreateBuilder<(int Move, MachineRegister From, MachineRegister To)>();
        while (pending.Count != 0)
        {
            var ready = pending.FindIndex(i => !pending.Exists(j => j != i && current[j] == destinations[i]));
            if (ready >= 0)
            {
                var move = pending[ready];
                steps.Add((move, current[move], destinations[move]));
                pending.RemoveAt(ready);
                continue;
            }

            var blocked = destinations[pending[0]];
            var scratch = scratchFor(blocked);
            steps.Add((-1, blocked, scratch));
            foreach (var move in pending)
            {
                if (current[move] == blocked)
                    current[move] = scratch;
            }
        }

        for (var i = 0; i < destinations.Count; i++)
        {
            if (sources[i] == MachineRegister.Invalid)
                steps.Add((i, MachineRegister.Invalid, destinations[i]));
        }

        return steps.ToImmutable();
    }
}

internal static class ParallelCopySequencer
{
    /// <summary>Orders the copies so each one runs before whichever copy overwrites the storage it reads</summary>
    public static bool TryOrder(
        IReadOnlyList<PhysicalStorageKey> destinations,
        IReadOnlyList<PhysicalStorageKey> sources,
        out ImmutableArray<int> order)
    {
        order = ImmutableArray<int>.Empty;
        if (destinations is null)
            throw new ArgumentNullException(nameof(destinations));
        if (sources is null)
            throw new ArgumentNullException(nameof(sources));
        if (destinations.Count != sources.Count)
            throw new ArgumentException("Destination and source storage counts must match.", nameof(sources));

        var count = destinations.Count;
        if (count == 0)
            return true;

        for (var i = 0; i < count; i++)
        {
            if (!destinations[i].IsKnown)
                return false;

            for (var j = i + 1; j < count; j++)
            {
                if (destinations[i].Equals(destinations[j]))
                    return false;
            }
        }

        var blockers = new int[count];
        for (var i = 0; i < count; i++)
        {
            for (var j = 0; j < count; j++)
            {
                if (i != j && sources[j].IsKnown && sources[j].Equals(destinations[i]))
                    blockers[i]++;
            }
        }

        var emitted = new bool[count];
        var result = ImmutableArray.CreateBuilder<int>(count);
        while (result.Count < count)
        {
            var progressed = false;
            for (var i = 0; i < count; i++)
            {
                if (emitted[i] || blockers[i] != 0)
                    continue;

                emitted[i] = true;
                result.Add(i);
                progressed = true;
                if (!sources[i].IsKnown)
                    continue;

                for (var k = 0; k < count; k++)
                {
                    if (k != i && !emitted[k] && sources[i].Equals(destinations[k]))
                        blockers[k]--;
                }
            }

            if (!progressed)
                return false;
        }

        order = result.ToImmutable();
        return true;
    }

}

internal readonly record struct ParallelCopyCycleRegisters(MachineRegister General, MachineRegister Floating);

internal readonly record struct InlineAssemblyRegisters(ImmutableArray<MachineRegister> Outputs, ImmutableArray<MachineRegister> Inputs);

internal sealed class AllocationResult
{
    private readonly IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> _allocations;
    private readonly IReadOnlyDictionary<int, ImmutableArray<CallPreservation>> _callPreservations;
    private readonly IReadOnlyDictionary<int, ScratchClass> _scratchKills;
    private readonly Func<MachineRegister, int, bool>? _isScratchFree;
    private readonly IReadOnlyDictionary<LirInstruction, ParallelCopyCycleRegisters> _parallelCopyCycleRegisters;
    private readonly IReadOnlyDictionary<LirInstruction, InlineAssemblyRegisters>? _inlineAssemblyRegisters;

    public LirFunction Function { get; }
    public StackFrameMap Frame { get; }
    public IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> Allocations => _allocations;

    public AllocationResult(
        LirFunction function,
        IReadOnlyDictionary<LirVirtualRegister, VirtualRegisterAllocation> allocations,
        StackFrameMap frame,
        IReadOnlyDictionary<int, ImmutableArray<CallPreservation>>? callPreservations = null,
        IReadOnlyDictionary<int, ScratchClass>? scratchKills = null,
        Func<MachineRegister, int, bool>? isScratchFree = null,
        IReadOnlyDictionary<LirInstruction, ParallelCopyCycleRegisters>? parallelCopyCycleRegisters = null,
        IReadOnlyDictionary<LirInstruction, InlineAssemblyRegisters>? inlineAssemblyRegisters = null)
    {
        _isScratchFree = isScratchFree;
        _parallelCopyCycleRegisters = parallelCopyCycleRegisters ?? new Dictionary<LirInstruction, ParallelCopyCycleRegisters>();
        _inlineAssemblyRegisters = inlineAssemblyRegisters;
        Function = function ?? throw new ArgumentNullException(nameof(function));
        _allocations = allocations ?? throw new ArgumentNullException(nameof(allocations));
        _callPreservations = callPreservations ?? ImmutableDictionary<int, ImmutableArray<CallPreservation>>.Empty;
        _scratchKills = scratchKills ?? ImmutableDictionary<int, ScratchClass>.Empty;
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
    }

    public VirtualRegisterAllocation this[LirVirtualRegister register]
        => _allocations.TryGetValue(register, out var allocation)
            ? allocation
            : throw new KeyNotFoundException("No physical allocation for " + register.Name + ".");

    public ScratchClass GetScratchKills(int position)
        => _scratchKills.TryGetValue(position, out var kills) ? kills : ScratchClass.None;

    public InlineAssemblyRegisters GetInlineAssemblyRegisters(LirInstruction instruction)
        => _inlineAssemblyRegisters is not null && _inlineAssemblyRegisters.TryGetValue(instruction, out InlineAssemblyRegisters registers)
            ? registers
            : throw new KeyNotFoundException("No inline assembly operand registers for the instruction.");

    public bool IsScratchFree(MachineRegister register, int position)
        => _isScratchFree?.Invoke(register, position) ?? false;

    public bool TryGetAllocation(LirVirtualRegister register, out VirtualRegisterAllocation allocation)
        => _allocations.TryGetValue(register, out allocation!);

    public bool TryGetPhysicalRegister(LirVirtualRegister register, out MachineRegister physicalRegister)
    {
        physicalRegister = MachineRegister.Invalid;
        if (register is null || !_allocations.TryGetValue(register, out var allocation) || allocation.IsSpilled)
            return false;

        physicalRegister = allocation.PhysicalRegister;
        return physicalRegister != MachineRegister.Invalid;
    }

    public bool TryGetPhysicalRegister(LirOperand operand, out MachineRegister physicalRegister)
    {
        physicalRegister = MachineRegister.Invalid;
        return operand.Kind == LirOperandKind.Register &&
            operand.Register is not null &&
            TryGetPhysicalRegister(operand.Register, out physicalRegister);
    }

    public bool TryGetStackOffset(LirVirtualRegister register, out int stackOffset)
    {
        stackOffset = -1;
        if (register is null || !_allocations.TryGetValue(register, out var allocation) || !allocation.IsSpilled)
            return false;

        stackOffset = allocation.StackOffset;
        return stackOffset >= 0;
    }

    public bool TryGetStackOffset(LirOperand operand, out int stackOffset)
    {
        stackOffset = -1;
        return operand.Kind == LirOperandKind.Register &&
            operand.Register is not null &&
            TryGetStackOffset(operand.Register, out stackOffset);
    }

    public bool ReferencesSamePhysicalStorage(LirOperand source, LirVirtualRegister destination)
    {
        if (source.Kind != LirOperandKind.Register || source.Register is null)
            return false;

        if (!_allocations.TryGetValue(source.Register, out var sourceAllocation) ||
            !_allocations.TryGetValue(destination, out var destinationAllocation))
        {
            return false;
        }

        if (!sourceAllocation.IsSpilled && !destinationAllocation.IsSpilled)
            return sourceAllocation.PhysicalRegister == destinationAllocation.PhysicalRegister;

        return sourceAllocation.IsSpilled && destinationAllocation.IsSpilled &&
            sourceAllocation.StackOffset == destinationAllocation.StackOffset;
    }

    public bool RequiresPhysicalParallelCopy(LirParallelCopy copy)
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

        return !ReferencesSamePhysicalStorage(copy.Source, copy.Destination);
    }

    // Blocks whose parallel copies all move nothing hold only a jump, so branches can go where their chain ends
    public IReadOnlyDictionary<LirBlock, LirBlock> FindJumpOnlyBlocks()
    {
        var targets = new Dictionary<LirBlock, LirBlock>();
        foreach (var block in Function.Blocks)
        {
            if (!ReferenceEquals(block, Function.Entry) && TryGetJumpOnlyTarget(block, out var target))
                targets.Add(block, target);
        }

        var resolved = new Dictionary<LirBlock, LirBlock>(targets.Count);
        foreach (var pair in targets)
        {
            var target = pair.Value;
            for (var steps = 0; steps < targets.Count && targets.TryGetValue(target, out var next); steps++)
                target = next;
            // A loop made of nothing but such jumps is a real infinite loop, so its blocks stay
            if (!targets.ContainsKey(target))
                resolved.Add(pair.Key, target);
        }

        return resolved;
    }

    private bool TryGetJumpOnlyTarget(LirBlock block, out LirBlock target)
    {
        target = null!;
        var instructions = block.Instructions;
        if (instructions.Length == 0 || instructions[^1] is not { Kind: LirInstructionKind.Jump, Target: LirBlock jumpTarget })
            return false;

        for (var i = 0; i < instructions.Length - 1; i++)
        {
            if (instructions[i].Kind != LirInstructionKind.ParallelCopy)
                return false;
            foreach (var copy in instructions[i].ParallelCopies)
            {
                if (RequiresPhysicalParallelCopy(copy))
                    return false;
            }
        }

        target = jumpTarget;
        return true;
    }

    // Each step names the copy it moves, or for a cycle break the copy reading the saved register, and an invalid source is the copy's immediate
    public bool TrySequenceCyclicParallelCopy(LirInstruction instruction, IReadOnlyList<LirParallelCopy> copies, out ImmutableArray<(int Copy, MachineRegister From, MachineRegister To)> steps)
    {
        steps = ImmutableArray<(int Copy, MachineRegister From, MachineRegister To)>.Empty;
        if (!_parallelCopyCycleRegisters.TryGetValue(instruction, out ParallelCopyCycleRegisters cycleRegisters))
            return false;

        var destinations = new MachineRegister[copies.Count];
        var sources = new MachineRegister[copies.Count];
        var floating = new Dictionary<MachineRegister, bool>();
        for (var i = 0; i < copies.Count; i++)
        {
            if (!TryGetPhysicalRegister(copies[i].Destination, out destinations[i]))
                throw new InvalidOperationException("A cyclic parallel copy writes a register the allocator did not assign.");
            sources[i] = copies[i].Source.Kind == LirOperandKind.Immediate ? MachineRegister.Invalid
                : TryGetPhysicalRegister(copies[i].Source, out MachineRegister source) ? source
                : throw new InvalidOperationException("A cyclic parallel copy reads a value outside a register.");
            floating[destinations[i]] = copies[i].Destination.RegisterClass == LirRegisterClass.Floating;
        }

        var builder = ImmutableArray.CreateBuilder<(int Copy, MachineRegister From, MachineRegister To)>();
        foreach ((int move, MachineRegister from, MachineRegister to) in RegisterMoveSequencer.Order(destinations, sources,
            blocked => floating[blocked] ? cycleRegisters.Floating : cycleRegisters.General))
        {
            builder.Add((move >= 0 ? move : Array.IndexOf(sources, from), from, to));
        }

        steps = builder.ToImmutable();
        return true;
    }

    public bool TryOrderParallelCopies(IReadOnlyList<LirParallelCopy> copies, out ImmutableArray<LirParallelCopy> ordered)
    {
        ordered = ImmutableArray<LirParallelCopy>.Empty;
        if (copies is null)
            throw new ArgumentNullException(nameof(copies));

        var destinations = new PhysicalStorageKey[copies.Count];
        var sources = new PhysicalStorageKey[copies.Count];
        for (var i = 0; i < copies.Count; i++)
        {
            destinations[i] = TryGetPhysicalRegister(copies[i].Destination, out var destinationRegister)
                ? PhysicalStorageKey.ForRegister(destinationRegister)
                : TryGetStackOffset(copies[i].Destination, out var destinationOffset)
                    ? PhysicalStorageKey.ForStackOffset(destinationOffset)
                    : default;
            sources[i] = TryGetPhysicalRegister(copies[i].Source, out var sourceRegister)
                ? PhysicalStorageKey.ForRegister(sourceRegister)
                : TryGetStackOffset(copies[i].Source, out var sourceOffset)
                    ? PhysicalStorageKey.ForStackOffset(sourceOffset)
                    : default;
        }

        if (!ParallelCopySequencer.TryOrder(destinations, sources, out var order))
            return false;

        var result = ImmutableArray.CreateBuilder<LirParallelCopy>(order.Length);
        foreach (var index in order)
            result.Add(copies[index]);
        ordered = result.ToImmutable();
        return true;
    }

    public bool HasPhysicalStorageClobber(IReadOnlyList<LirParallelCopy> copies)
    {
        for (var i = 0; i < copies.Count; i++)
        {
            var destination = copies[i].Destination;
            var hasDestinationRegister = TryGetPhysicalRegister(destination, out var destinationRegister);
            var hasDestinationStackOffset = TryGetStackOffset(destination, out var destinationStackOffset);
            if (!hasDestinationRegister && !hasDestinationStackOffset)
                continue;

            for (var j = 0; j < copies.Count; j++)
            {
                if (i == j && ReferencesSamePhysicalStorage(copies[j].Source, destination))
                    continue;

                if (hasDestinationRegister &&
                    TryGetPhysicalRegister(copies[j].Source, out var sourceRegister) &&
                    sourceRegister == destinationRegister)
                {
                    return true;
                }

                if (hasDestinationStackOffset &&
                    TryGetStackOffset(copies[j].Source, out var sourceStackOffset) &&
                    sourceStackOffset == destinationStackOffset)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public ImmutableArray<CallPreservation> GetCallPreservations(int position)
        => _callPreservations.TryGetValue(position, out var preservations)
            ? preservations
            : ImmutableArray<CallPreservation>.Empty;

    public bool TryGetCallPreservation(int position, LirVirtualRegister register, out CallPreservation preservation)
    {
        foreach (var candidate in GetCallPreservations(position))
        {
            if (!ReferenceEquals(candidate.Register, register))
                continue;
            preservation = candidate;
            return true;
        }

        preservation = default;
        return false;
    }
}

internal readonly struct CallPreservation
{
    public LirVirtualRegister Register { get; }
    public MachineRegister PhysicalRegister { get; }
    public MachineRegister PreservationRegister { get; }
    public int StackOffset { get; }
    public bool NeedsSave { get; }
    public bool NeedsRestore { get; }
    public bool UsesRegister => PreservationRegister != MachineRegister.Invalid;

    private CallPreservation(
        LirVirtualRegister register,
        MachineRegister physicalRegister,
        MachineRegister preservationRegister,
        int stackOffset,
        bool needsSave = true,
        bool needsRestore = true)
    {
        Register = register ?? throw new ArgumentNullException(nameof(register));
        PhysicalRegister = physicalRegister;
        PreservationRegister = preservationRegister;
        StackOffset = stackOffset;
        NeedsSave = needsSave;
        NeedsRestore = needsRestore;
    }

    public static CallPreservation InRegister(
        LirVirtualRegister register,
        MachineRegister physicalRegister,
        MachineRegister preservationRegister)
        => new CallPreservation(register, physicalRegister, preservationRegister, -1);

    public static CallPreservation OnStack(LirVirtualRegister register, MachineRegister physicalRegister)
        => new CallPreservation(register, physicalRegister, MachineRegister.Invalid, -1);

    public CallPreservation WithStackOffset(int stackOffset)
        => new CallPreservation(Register, PhysicalRegister, PreservationRegister, stackOffset, NeedsSave, NeedsRestore);

    public CallPreservation WithoutSave()
        => new CallPreservation(Register, PhysicalRegister, PreservationRegister, StackOffset, needsSave: false, NeedsRestore);

    public CallPreservation WithoutRestore()
        => new CallPreservation(Register, PhysicalRegister, PreservationRegister, StackOffset, NeedsSave, needsRestore: false);
}

internal sealed class VirtualRegisterAllocation
{
    public LirVirtualRegister Register { get; }
    public LirRegisterClass RegisterClass { get; }
    public bool IsSpilled { get; }
    public bool PreserveAcrossCalls { get; }
    public bool NeedsStackSlot => IsSpilled || PreserveAcrossCalls;
    public MachineRegister PhysicalRegister { get; }
    public int StackOffset { get; }

    private VirtualRegisterAllocation(
        LirVirtualRegister register,
        LirRegisterClass registerClass,
        bool isSpilled,
        bool preserveAcrossCalls,
        MachineRegister physicalRegister,
        int stackOffset)
    {
        Register = register ?? throw new ArgumentNullException(nameof(register));
        RegisterClass = registerClass;
        IsSpilled = isSpilled;
        PreserveAcrossCalls = preserveAcrossCalls;
        PhysicalRegister = physicalRegister;
        StackOffset = stackOffset;
    }

    public static VirtualRegisterAllocation InRegister(LirVirtualRegister register, MachineRegister physicalRegister)
        => new VirtualRegisterAllocation(register, register.RegisterClass, isSpilled: false, preserveAcrossCalls: false, physicalRegister, stackOffset: -1);

    public static VirtualRegisterAllocation Spilled(LirVirtualRegister register, LirRegisterClass registerClass)
        => new VirtualRegisterAllocation(register, registerClass, isSpilled: true, preserveAcrossCalls: false, MachineRegister.Invalid, stackOffset: -1);

    public VirtualRegisterAllocation WithCallPreservation()
        => new VirtualRegisterAllocation(Register, RegisterClass, IsSpilled, preserveAcrossCalls: true, PhysicalRegister, StackOffset);

    public VirtualRegisterAllocation WithStackOffset(int stackOffset)
        => new VirtualRegisterAllocation(Register, RegisterClass, IsSpilled, PreserveAcrossCalls, PhysicalRegister, stackOffset);

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
    public int FrameBaseSaveOffset { get; }
    public bool HasDynamicStack => FrameBaseSaveOffset >= 0;
    public int ScratchBorrowOffset { get; }
    public int ScratchBorrowSize { get; }
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
        IReadOnlyDictionary<MachineRegister, int> savedRegisterOffsets,
        int frameBaseSaveOffset = -1,
        int scratchBorrowOffset = -1,
        int scratchBorrowSize = 0)
    {
        FrameSize = frameSize;
        FrameBaseSaveOffset = frameBaseSaveOffset;
        ScratchBorrowOffset = scratchBorrowOffset;
        ScratchBorrowSize = scratchBorrowSize;
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

internal readonly record struct NativeUnwindInfo(
    string Symbol,
    int FrameSize,
    int ReturnAddressOffset,
    int FramePointerOffset,
    ImmutableArray<(MachineRegister Register, int Offset, int Size)> SavedRegisters);
