using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;

namespace Cnidaria.Cs
{
    internal static class RegisterGcInfoBuilder
    {
        public static RegisterAllocatedMethod AttachMethod(RegisterAllocatedMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            var builder = new MethodBuilder(method);
            return builder.Run();
        }

        private sealed class MethodBuilder
        {
            private readonly RegisterAllocatedMethod _method;
            private readonly Dictionary<int, GenTree> _linearTreeById;
            private readonly Dictionary<int, int> _linearTreePositions;
            private readonly Dictionary<int, int> _originalTreePositions;
            private readonly SortedDictionary<int, int> _originalToFinalPositions;
            private readonly Dictionary<int, int> _positionBlockIds;
            private readonly List<int> _knownPositions;
            private readonly int _finalEndPosition;
            private readonly ImmutableArray<int> _finalCallerSavedKillPositions;
            private readonly ImmutableArray<int> _callerFrameSuspendingCallPositions;
            private int _syntheticGcOwnerId;

            public MethodBuilder(RegisterAllocatedMethod method)
            {
                _method = method;
                _linearTreeById = BuildLinearTreeMap(method.GenTreeMethod);
                _originalTreePositions = BuildOriginalLinearTreePositions(method.GenTreeMethod);
                _linearTreePositions = BuildFinalLinearTreePositions(method, out _positionBlockIds, out _finalEndPosition);
                _originalToFinalPositions = BuildOriginalToFinalPositionMap(_originalTreePositions, _linearTreePositions, _finalEndPosition);
                _knownPositions = BuildKnownPositions(_positionBlockIds);
                _finalCallerSavedKillPositions = BuildFinalCallerSavedKillPositions(method, _linearTreePositions);
                _callerFrameSuspendingCallPositions = BuildCallerFrameSuspendingCallPositions(method, _linearTreePositions);
                _syntheticGcOwnerId = int.MinValue;
            }

            public RegisterAllocatedMethod Run()
            {
                var liveRanges = BuildLiveRanges();
                var transitions = BuildTransitions(liveRanges);
                var interruptibleRanges = BuildInterruptibleRanges();

                return new RegisterAllocatedMethod(
                    _method.GenTreeMethod,
                    _method.Blocks,
                    _method.LinearNodes,
                    _method.Allocations,
                    _method.AllocationByNode,
                    _method.InternalRegistersByNodeId,
                    _method.SpillSlotCount,
                    _method.ParallelCopyScratchSpillSlot,
                    _method.StackFrame,
                    _method.HasPrologEpilog,
                    _method.UnwindCodes,
                    liveRanges,
                    transitions,
                    interruptibleRanges,
                    _method.Funclets,
                    _method.FrameRegions,
                    gcReportOnlyLeafFunclet: _method.Funclets.Length > 1,
                    lsraNodePositions: _method.LsraNodePositions,
                    lsraBlockStartPositions: _method.LsraBlockStartPositions,
                    lsraBlockEndPositions: _method.LsraBlockEndPositions);
            }

            private static ImmutableArray<int> BuildFinalCallerSavedKillPositions(
                RegisterAllocatedMethod method,
                Dictionary<int, int> finalPositions)
            {
                var result = new SortedSet<int>();
                for (int i = 0; i < method.LinearNodes.Length; i++)
                {
                    var node = method.LinearNodes[i];
                    if (!node.HasLoweringFlag(GenTreeLinearFlags.CallerSavedKill))
                        continue;

                    int key = node.LinearId >= 0 ? node.LinearId : node.Id;
                    if (finalPositions.TryGetValue(key, out int position))
                        result.Add(position);
                }

                return result.ToImmutableArray();
            }

            private static ImmutableArray<int> BuildCallerFrameSuspendingCallPositions(
                RegisterAllocatedMethod method,
                Dictionary<int, int> finalPositions)
            {
                var result = new SortedSet<int>();
                for (int i = 0; i < method.LinearNodes.Length; i++)
                {
                    var node = method.LinearNodes[i];
                    if (!IsCallerFrameSuspendingManagedCall(node))
                        continue;

                    int key = node.LinearId >= 0 ? node.LinearId : node.Id;
                    if (finalPositions.TryGetValue(key, out int position))
                        result.Add(position);
                }

                return result.ToImmutableArray();
            }

            private static bool IsCallerFrameSuspendingManagedCall(GenTree node)
            {
                if (node is null || !node.HasLoweringFlag(GenTreeLinearFlags.AbiCall))
                    return false;

                var method = node.Method;
                if (method is null || method.HasInternalCall)
                    return false;

                return node.TreeKind switch
                {
                    GenTreeKind.Call => true,
                    GenTreeKind.VirtualCall => true,
                    GenTreeKind.DelegateInvoke => true,
                    GenTreeKind.NewObject => true,
                    _ => false,
                };
            }

            private bool IsCallerFrameSuspendingCallPosition(int position)
            {
                for (int i = 0; i < _callerFrameSuspendingCallPositions.Length; i++)
                {
                    int callPosition = _callerFrameSuspendingCallPositions[i];
                    if (callPosition == position)
                        return true;
                    if (callPosition > position)
                        return false;
                }

                return false;
            }

            private ImmutableArray<RegisterGcLiveRange> BuildLiveRanges()
            {
                var ranges = ImmutableArray.CreateBuilder<RegisterGcLiveRange>();

                for (int i = 0; i < _method.Allocations.Length; i++)
                {
                    var allocation = _method.Allocations[i];
                    if (!_method.GenTreeMethod.ValueInfoByNode.TryGetValue(allocation.ValueKey, out var info))
                        continue;

                    if (info.Type is not null && info.Type.IsValueType && info.Type.ContainsGcPointers && allocation.Fragments.Length != 0)
                    {
                        AddStructFragmentRanges(ranges, allocation, info);
                        continue;
                    }

                    if (info.Type is not null && info.Type.IsValueType && info.Type.ContainsGcPointers)
                    {
                        AddStructHomeRanges(ranges, allocation, info);
                        continue;
                    }

                    if (!TryGetGcRootKind(info.Type, info.StackKind, out var rootKind))
                        continue;

                    for (int s = 0; s < allocation.Segments.Length; s++)
                    {
                        var segment = allocation.Segments[s];
                        if (segment.Location.IsNone || segment.Start == segment.End)
                            continue;

                        var translated = TranslateAllocationRange(segment.Start, segment.End);
                        AddAllocationLiveRangeSlices(
                            ranges,
                            new RegisterGcLiveRoot(allocation.Value, rootKind, segment.Location, info.Type),
                            translated.Start,
                            translated.End);
                    }
                }

                AddHomeSlotRootRanges(ranges);
                AddSafepointOperandRootRanges(ranges);

                return NormalizeLiveRanges(ranges.ToImmutable());
            }

            private void AddHomeSlotRootRanges(ImmutableArray<RegisterGcLiveRange>.Builder ranges)
            {
                if (_knownPositions.Count == 0)
                    return;

                var owners = BuildHomeRootOwners();
                var descriptorLiveness = BuildDescriptorHomeLiveness();
                int methodStart = _knownPositions[0];
                int methodEnd = _knownPositions[_knownPositions.Count - 1] + 2;
                RegisterFrameBase frameBase = _method.StackFrame.UsesFramePointer
                    ? RegisterFrameBase.FramePointer
                    : RegisterFrameBase.StackPointer;

                AddDescriptorHomeSlotRootRanges(
                    ranges,
                    owners,
                    descriptorLiveness,
                    _method.GenTreeMethod.ArgDescriptors,
                    StackFrameSlotKind.Argument,
                    frameBase,
                    methodStart,
                    methodEnd);

                AddDescriptorHomeSlotRootRanges(
                    ranges,
                    owners,
                    descriptorLiveness,
                    _method.GenTreeMethod.LocalDescriptors,
                    StackFrameSlotKind.Local,
                    frameBase,
                    methodStart,
                    methodEnd);

                AddDescriptorHomeSlotRootRanges(
                    ranges,
                    owners,
                    descriptorLiveness,
                    _method.GenTreeMethod.TempDescriptors,
                    StackFrameSlotKind.Temp,
                    frameBase,
                    methodStart,
                    methodEnd);
            }

            private sealed class DescriptorHomeLiveness
            {
                public readonly HashSet<GenLocalDescriptor>[] LiveIn;
                public readonly HashSet<GenLocalDescriptor>[] LiveOut;

                public DescriptorHomeLiveness(HashSet<GenLocalDescriptor>[] liveIn, HashSet<GenLocalDescriptor>[] liveOut)
                {
                    LiveIn = liveIn ?? throw new ArgumentNullException(nameof(liveIn));
                    LiveOut = liveOut ?? throw new ArgumentNullException(nameof(liveOut));
                }

                public bool IsLiveIn(int blockId, GenLocalDescriptor descriptor)
                    => (uint)blockId < (uint)LiveIn.Length && LiveIn[blockId].Contains(descriptor);

                public bool IsLiveOut(int blockId, GenLocalDescriptor descriptor)
                    => (uint)blockId < (uint)LiveOut.Length && LiveOut[blockId].Contains(descriptor);
            }

            private DescriptorHomeLiveness BuildDescriptorHomeLiveness()
            {
                int blockCount = _method.Blocks.Length;
                var liveIn = NewDescriptorSetArray(blockCount);
                var liveOut = NewDescriptorSetArray(blockCount);
                var blockUses = NewDescriptorSetArray(blockCount);
                var blockDefs = NewDescriptorSetArray(blockCount);

                for (int b = 0; b < blockCount; b++)
                {
                    var seenDefinition = new HashSet<GenLocalDescriptor>();
                    var nodes = _method.Blocks[b].LinearNodes;

                    for (int i = 0; i < nodes.Length; i++)
                    {
                        var node = nodes[i];
                        var descriptor = node.LocalDescriptor;
                        if (descriptor is null || !NeedsHomeGcReporting(descriptor.Type, descriptor.StackKind))
                            continue;

                        if (IsDescriptorStore(node))
                        {
                            blockDefs[b].Add(descriptor);
                            seenDefinition.Add(descriptor);
                            continue;
                        }

                        if (IsDescriptorRead(node) && !seenDefinition.Contains(descriptor))
                        {
                            blockUses[b].Add(descriptor);
                        }
                    }
                }

                var dataflowOrder = _method.GenTreeMethod.LinearBlockOrder.IsDefaultOrEmpty
                    ? LinearBlockOrder.Compute(_method.GenTreeMethod.Cfg)
                    : _method.GenTreeMethod.LinearBlockOrder;

                bool changed;
                do
                {
                    changed = false;
                    for (int r = dataflowOrder.Length - 1; r >= 0; r--)
                    {
                        int blockId = dataflowOrder[r];
                        var newOut = new HashSet<GenLocalDescriptor>();
                        var successors = _method.GenTreeMethod.Cfg.Blocks[blockId].Successors;
                        for (int s = 0; s < successors.Length; s++)
                            newOut.UnionWith(liveIn[successors[s].ToBlockId]);

                        var newIn = new HashSet<GenLocalDescriptor>(newOut);
                        newIn.ExceptWith(blockDefs[blockId]);
                        newIn.UnionWith(blockUses[blockId]);

                        if (!SetEquals(liveOut[blockId], newOut))
                        {
                            liveOut[blockId] = newOut;
                            changed = true;
                        }

                        if (!SetEquals(liveIn[blockId], newIn))
                        {
                            liveIn[blockId] = newIn;
                            changed = true;
                        }
                    }
                }
                while (changed);

                return new DescriptorHomeLiveness(liveIn, liveOut);
            }

            private static HashSet<GenLocalDescriptor>[] NewDescriptorSetArray(int length)
            {
                var result = new HashSet<GenLocalDescriptor>[length];
                for (int i = 0; i < result.Length; i++)
                    result[i] = new HashSet<GenLocalDescriptor>();
                return result;
            }

            private static bool SetEquals(HashSet<GenLocalDescriptor> left, HashSet<GenLocalDescriptor> right)
            {
                if (left.Count != right.Count)
                    return false;
                foreach (var value in left)
                    if (!right.Contains(value))
                        return false;
                return true;
            }

            private void AddDescriptorHomeSlotRootRanges(
                ImmutableArray<RegisterGcLiveRange>.Builder ranges,
                Dictionary<GenLocalDescriptor, GenTree> owners,
                DescriptorHomeLiveness descriptorLiveness,
                ImmutableArray<GenLocalDescriptor> descriptors,
                StackFrameSlotKind slotKind,
                RegisterFrameBase frameBase,
                int methodStart,
                int methodEnd)
            {
                for (int i = 0; i < descriptors.Length; i++)
                {
                    var descriptor = descriptors[i];
                    if (!ShouldReportDescriptorHome(descriptor))
                        continue;
                    if (!owners.TryGetValue(descriptor, out var owner))
                        continue;
                    if (!TryGetHomeSlot(slotKind, descriptor.Index, out var slot))
                        continue;

                    var location = RegisterOperand.ForFrameSlot(
                        RegisterClass.General,
                        slot.Kind,
                        frameBase,
                        slot.Index,
                        slot.Offset,
                        slot.Size);

                    if (ShouldReportDescriptorHomeForEntireMethod(descriptor))
                    {
                        AddHomeSlotRootRanges(ranges, owner, descriptor.Type, descriptor.StackKind, location, methodStart, methodEnd);
                        continue;
                    }

                    AddDescriptorHomeSlotRootRanges(ranges, owner, descriptor, location, descriptorLiveness);
                }
            }

            private void AddDescriptorHomeSlotRootRanges(
                ImmutableArray<RegisterGcLiveRange>.Builder ranges,
                GenTree owner,
                GenLocalDescriptor descriptor,
                RegisterOperand location,
                DescriptorHomeLiveness descriptorLiveness)
            {
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var block = _method.Blocks[b];
                    if (!TryGetBlockPositionRange(block, out int blockStart, out int blockEnd))
                        continue;

                    bool active = descriptorLiveness.IsLiveIn(block.Id, descriptor);
                    int activeStart = active ? blockStart : -1;
                    var nodes = block.LinearNodes;

                    for (int i = 0; i < nodes.Length; i++)
                    {
                        var node = nodes[i];
                        if (node.LocalDescriptor is null || !ReferenceEquals(node.LocalDescriptor, descriptor))
                            continue;
                        if (!TryGetLinearPosition(node, out int position))
                            continue;

                        if (IsDescriptorStore(node))
                        {
                            if (active)
                                AddHomeSlotRootRanges(ranges, owner, descriptor.Type, descriptor.StackKind, location, activeStart, position);

                            active = false;
                            activeStart = -1;

                            if (StoreWritesReportableGcValue(node) && IsDescriptorLiveAfterStore(descriptorLiveness, descriptor, block, i))
                            {
                                active = true;
                                activeStart = position + 1;
                            }

                            continue;
                        }

                        if (IsDescriptorRead(node) && !active)
                        {
                            active = true;
                            activeStart = position;
                        }
                    }

                    if (active)
                        AddHomeSlotRootRanges(ranges, owner, descriptor.Type, descriptor.StackKind, location, activeStart, blockEnd);
                }
            }

            private bool TryGetBlockPositionRange(GenTreeBlock block, out int start, out int end)
            {
                start = 0;
                end = 0;
                if (block.LinearNodes.Length == 0)
                    return false;

                if (!TryGetLinearPosition(block.LinearNodes[0], out start))
                    return false;

                if (!TryGetLinearPosition(block.LinearNodes[block.LinearNodes.Length - 1], out int last))
                    return false;

                end = last + 2;
                return true;
            }

            private bool IsDescriptorLiveAfterStore(
                DescriptorHomeLiveness descriptorLiveness,
                GenLocalDescriptor descriptor,
                GenTreeBlock block,
                int storeOrdinal)
            {
                var nodes = block.LinearNodes;
                for (int i = storeOrdinal + 1; i < nodes.Length; i++)
                {
                    var node = nodes[i];
                    if (node.LocalDescriptor is null || !ReferenceEquals(node.LocalDescriptor, descriptor))
                        continue;

                    if (IsDescriptorRead(node))
                        return true;
                    if (IsDescriptorStore(node))
                        return false;
                }

                return descriptorLiveness.IsLiveOut(block.Id, descriptor);
            }

            private bool StoreWritesReportableGcValue(GenTree store)
            {
                if (store.RegisterUses.Length == 0)
                    return true;

                var value = store.RegisterUses[0];
                if (!_method.GenTreeMethod.ValueInfoByNode.TryGetValue(value.LinearValueKey, out var valueInfo))
                    return true;

                if (valueInfo.Type is not null && valueInfo.Type.IsValueType && valueInfo.Type.ContainsGcPointers)
                    return true;

                return TryGetGcRootKind(valueInfo.Type, valueInfo.StackKind, out _);
            }

            private static bool IsDescriptorStore(GenTree node)
                => node.Kind is GenTreeKind.StoreArg or GenTreeKind.StoreLocal or GenTreeKind.StoreTemp;

            private static bool IsDescriptorRead(GenTree node)
                => node.Kind is GenTreeKind.Arg or GenTreeKind.Local or GenTreeKind.Temp or
                   GenTreeKind.ArgAddr or GenTreeKind.LocalAddr or GenTreeKind.TempAddr;

            private Dictionary<GenLocalDescriptor, GenTree> BuildHomeRootOwners()
            {
                var owners = new Dictionary<GenLocalDescriptor, GenTree>();
                var nodes = _method.GenTreeMethod.LinearNodes;
                for (int i = 0; i < nodes.Length; i++)
                {
                    var node = nodes[i];
                    var descriptor = node.LocalDescriptor;
                    if (descriptor is null || owners.ContainsKey(descriptor))
                        continue;
                    if (!NeedsHomeGcReporting(descriptor.Type, descriptor.StackKind))
                        continue;

                    owners.Add(descriptor, node);
                }

                AddSyntheticHomeOwners(owners, _method.GenTreeMethod.ArgDescriptors);
                AddSyntheticHomeOwners(owners, _method.GenTreeMethod.LocalDescriptors);
                AddSyntheticHomeOwners(owners, _method.GenTreeMethod.TempDescriptors);
                return owners;
            }

            private void AddSyntheticHomeOwners(
                Dictionary<GenLocalDescriptor, GenTree> owners,
                ImmutableArray<GenLocalDescriptor> descriptors)
            {
                for (int i = 0; i < descriptors.Length; i++)
                {
                    var descriptor = descriptors[i];
                    if (owners.ContainsKey(descriptor))
                        continue;
                    if (!ShouldReportDescriptorHome(descriptor))
                        continue;

                    owners.Add(descriptor, CreateSyntheticHomeRootOwner(descriptor));
                }
            }

            private GenTree CreateSyntheticHomeRootOwner(GenLocalDescriptor descriptor)
            {
                var kind = descriptor.Kind switch
                {
                    GenLocalKind.Argument => GenTreeKind.Arg,
                    GenLocalKind.Local => GenTreeKind.Local,
                    GenLocalKind.Temporary => GenTreeKind.Temp,
                    _ => GenTreeKind.Nop,
                };

                var owner = new GenTree(
                    _syntheticGcOwnerId++,
                    kind,
                    pc: -1,
                    BytecodeOp.Nop,
                    descriptor.Type,
                    descriptor.StackKind,
                    GenTreeFlags.LocalUse | GenTreeFlags.Ordered,
                    ImmutableArray<GenTree>.Empty,
                    int32: descriptor.Index);
                owner.LocalDescriptor = descriptor;
                owner.ValueKey = descriptor.ValueKey;
                return owner;
            }

            private static bool ShouldReportDescriptorHome(GenLocalDescriptor descriptor)
            {
                if (!NeedsHomeGcReporting(descriptor.Type, descriptor.StackKind))
                    return false;

                if (descriptor.Category == GenLocalCategory.PromotedStruct &&
                    descriptor.HasPromotedStructFields &&
                    !descriptor.AddressExposed &&
                    !descriptor.MemoryAliased)
                    return false;

                return descriptor.AddressExposed || descriptor.MemoryAliased || descriptor.DoNotEnregister || !descriptor.SsaPromoted;
            }

            private static bool ShouldReportDescriptorHomeForEntireMethod(GenLocalDescriptor descriptor)
            {
                if (!ShouldReportDescriptorHome(descriptor))
                    return false;

                return !CanUsePreciseDescriptorHomeLiveness(descriptor);
            }

            private static bool CanUsePreciseDescriptorHomeLiveness(GenLocalDescriptor descriptor)
            {
                if (!descriptor.Tracked)
                    return false;

                if (descriptor.AddressExposed || descriptor.MemoryAliased)
                    return false;

                if (descriptor.Type is not null && descriptor.Type.IsValueType && descriptor.Type.ContainsGcPointers)
                    return false;

                return true;
            }

            private bool TryGetHomeSlot(StackFrameSlotKind slotKind, int index, out StackFrameSlot slot)
            {
                switch (slotKind)
                {
                    case StackFrameSlotKind.Argument:
                        return _method.StackFrame.TryGetArgumentSlot(index, out slot);
                    case StackFrameSlotKind.Local:
                        return _method.StackFrame.TryGetLocalSlot(index, out slot);
                    case StackFrameSlotKind.Temp:
                        return _method.StackFrame.TryGetTempSlot(index, out slot);
                    default:
                        throw new InvalidOperationException("Unsupported GC home slot kind: " + slotKind + ".");
                }
            }

            private void AddHomeSlotRootRanges(
                ImmutableArray<RegisterGcLiveRange>.Builder ranges,
                GenTree owner,
                RuntimeType? type,
                GenStackKind stackKind,
                RegisterOperand location,
                int start,
                int end)
            {
                if (type is not null && type.IsValueType && type.ContainsGcPointers)
                {
                    var gcOffsets = type.GcPointerOffsets;
                    for (int g = 0; g < gcOffsets.Length; g++)
                    {
                        var structRootKind = GetStructGcRootKind(type, gcOffsets[g]);
                        AddLiveRangeSlices(
                            ranges,
                            new RegisterGcLiveRoot(
                                owner,
                                structRootKind,
                                location,
                                type,
                                gcOffsets[g],
                                requiresValueInfo: false),
                            start,
                            end);
                    }

                    return;
                }

                if (!TryGetGcRootKind(type, stackKind, out var rootKind))
                    return;

                AddLiveRangeSlices(
                    ranges,
                    new RegisterGcLiveRoot(owner, rootKind, location, type, requiresValueInfo: false),
                    start,
                    end);
            }

            private void AddSafepointOperandRootRanges(ImmutableArray<RegisterGcLiveRange>.Builder ranges)
            {
                for (int i = 0; i < _method.LinearNodes.Length; i++)
                {
                    var node = _method.LinearNodes[i];
                    if (!node.HasLoweringFlag(GenTreeLinearFlags.GcSafePoint))
                        continue;

                    if (!TryGetLinearPosition(node, out int position))
                        continue;

                    int count = Math.Min(node.Uses.Length, node.RegisterUses.Length);
                    for (int u = 0; u < count; u++)
                    {
                        if (u < node.UseRoles.Length && node.UseRoles[u] == OperandRole.HiddenReturnBuffer)
                            continue;

                        var location = node.Uses[u];
                        if (location.IsNone)
                            continue;

                        var value = node.RegisterUses[u];
                        if (!_method.GenTreeMethod.ValueInfoByNode.TryGetValue(value.LinearValueKey, out var info))
                            continue;

                        AddSafepointOperandRootRange(ranges, value, info, location, position);
                    }
                }
            }

            private void AddSafepointOperandRootRange(
                ImmutableArray<RegisterGcLiveRange>.Builder ranges,
                GenTree value,
                GenTreeValueInfo info,
                RegisterOperand location,
                int position)
            {
                if (info.Type is not null && info.Type.IsValueType && info.Type.ContainsGcPointers)
                {
                    if (location.IsRegister)
                        return;

                    var gcOffsets = info.Type.GcPointerOffsets;
                    for (int g = 0; g < gcOffsets.Length; g++)
                    {
                        var structRootKind = GetStructGcRootKind(info.Type, gcOffsets[g]);
                        AddLiveRangeSlices(
                            ranges,
                            new RegisterGcLiveRoot(
                                value,
                                structRootKind,
                                location,
                                info.Type,
                                gcOffsets[g]),
                            position,
                            position + 1);
                    }

                    return;
                }

                if (!TryGetGcRootKind(info.Type, info.StackKind, out var rootKind))
                    return;

                AddLiveRangeSlices(
                    ranges,
                    new RegisterGcLiveRoot(value, rootKind, location, info.Type),
                    position,
                    position + 1);
            }

            private void AddStructFragmentRanges(
                ImmutableArray<RegisterGcLiveRange>.Builder ranges,
                RegisterAllocationInfo allocation,
                GenTreeValueInfo info)
            {
                var gcOffsets = info.Type!.GcPointerOffsets;
                for (int f = 0; f < allocation.Fragments.Length; f++)
                {
                    var fragment = allocation.Fragments[f];
                    if (!fragment.AbiSegment.ContainsGcPointers)
                        continue;

                    int segmentStart = fragment.AbiSegment.Offset;
                    int segmentEnd = segmentStart + fragment.AbiSegment.Size;
                    for (int s = 0; s < fragment.Segments.Length; s++)
                    {
                        var range = fragment.Segments[s];
                        if (range.Location.IsNone || range.Start == range.End)
                            continue;
                        if (range.Location.IsRegister)
                            continue;

                        for (int g = 0; g < gcOffsets.Length; g++)
                        {
                            int gcOffset = gcOffsets[g];
                            if (segmentStart <= gcOffset && gcOffset < segmentEnd)
                            {
                                var rootKind = GetStructGcRootKind(info.Type, gcOffset);
                                var translated = TranslateAllocationRange(range.Start, range.End);
                                AddLiveRangeSlices(
                                    ranges,
                                    new RegisterGcLiveRoot(
                                        allocation.Value,
                                        rootKind,
                                        range.Location,
                                        info.Type,
                                        gcOffset - segmentStart),
                                    translated.Start,
                                    translated.End);
                            }
                        }
                    }
                }
            }

            private void AddStructHomeRanges(
                ImmutableArray<RegisterGcLiveRange>.Builder ranges,
                RegisterAllocationInfo allocation,
                GenTreeValueInfo info)
            {
                var gcOffsets = info.Type!.GcPointerOffsets;
                for (int s = 0; s < allocation.Segments.Length; s++)
                {
                    var segment = allocation.Segments[s];
                    if (segment.Location.IsNone || segment.Start == segment.End)
                        continue;
                    if (segment.Location.IsRegister)
                        continue;

                    for (int g = 0; g < gcOffsets.Length; g++)
                    {
                        var rootKind = GetStructGcRootKind(info.Type, gcOffsets[g]);
                        var translated = TranslateAllocationRange(segment.Start, segment.End);
                        AddLiveRangeSlices(
                            ranges,
                            new RegisterGcLiveRoot(
                                allocation.Value,
                                rootKind,
                                segment.Location,
                                info.Type,
                                gcOffsets[g]),
                            translated.Start,
                            translated.End);
                    }
                }
            }

            private static RegisterGcRootKind GetStructGcRootKind(RuntimeType type, int gcOffset)
            {
                if (TryGetStructGcRootKind(type, gcOffset, out var rootKind))
                    return rootKind;

                return RegisterGcRootKind.ObjectReference;
            }

            private static bool TryGetStructGcRootKind(RuntimeType type, int gcOffset, out RegisterGcRootKind rootKind)
            {
                if (gcOffset < 0)
                {
                    rootKind = default;
                    return false;
                }

                if (type.Kind == RuntimeTypeKind.ByRef)
                {
                    rootKind = RegisterGcRootKind.InteriorPointer;
                    return true;
                }

                if (type.Kind == RuntimeTypeKind.TypeParam || type.IsReferenceType)
                {
                    rootKind = RegisterGcRootKind.ObjectReference;
                    return true;
                }

                if (!type.IsValueType || type.InstanceFields.Length == 0)
                {
                    rootKind = default;
                    return false;
                }

                var fields = (RuntimeField[])type.InstanceFields.Clone();
                Array.Sort(fields, static (a, b) => a.Offset.CompareTo(b.Offset));

                for (int i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    var fieldType = field.FieldType;
                    int fieldSize = Math.Max(1, fieldType.SizeOf);
                    int fieldStart = field.Offset;
                    int fieldEnd = checked(fieldStart + fieldSize);
                    if (gcOffset < fieldStart || gcOffset >= fieldEnd)
                        continue;

                    if (fieldType.Kind == RuntimeTypeKind.ByRef)
                    {
                        rootKind = RegisterGcRootKind.InteriorPointer;
                        return true;
                    }

                    if (fieldType.Kind == RuntimeTypeKind.TypeParam || fieldType.IsReferenceType)
                    {
                        rootKind = RegisterGcRootKind.ObjectReference;
                        return true;
                    }

                    if (fieldType.IsValueType)
                        return TryGetStructGcRootKind(fieldType, gcOffset - fieldStart, out rootKind);

                    break;
                }

                rootKind = default;
                return false;
            }

            private LinearLiveRange TranslateAllocationRange(int start, int end)
            {
                int translatedStart = TranslateAllocationPosition(start);
                int translatedEnd = TranslateAllocationPosition(end);
                if (translatedEnd < translatedStart)
                    translatedEnd = translatedStart;
                return new LinearLiveRange(translatedStart, translatedEnd);
            }

            private int TranslateAllocationPosition(int position)
            {
                if (_originalToFinalPositions.TryGetValue(position, out int exact))
                    return exact;

                foreach (var kv in _originalToFinalPositions)
                {
                    if (kv.Key >= position)
                        return kv.Value;
                }

                return _finalEndPosition;
            }

            private void AddAllocationLiveRangeSlices(
                ImmutableArray<RegisterGcLiveRange>.Builder ranges,
                RegisterGcLiveRoot root,
                int start,
                int end)
            {
                if (start >= end)
                    return;

                if (!root.Location.IsRegister || !MachineRegisters.IsCallerSaved(root.Location.Register))
                {
                    AddLiveRangeSlices(ranges, root, start, end);
                    return;
                }

                for (int i = 0; i < _finalCallerSavedKillPositions.Length; i++)
                {
                    int kill = _finalCallerSavedKillPositions[i];
                    if (kill < start)
                        continue;
                    if (kill >= end)
                        break;

                    int killEnd = IsCallerFrameSuspendingCallPosition(kill)
                        ? kill
                        : Math.Min(end, kill + 1);
                    if (start < killEnd)
                        AddLiveRangeSlices(ranges, root, start, killEnd);
                    return;
                }

                AddLiveRangeSlices(ranges, root, start, end);
            }

            private void AddLiveRangeSlices(
                ImmutableArray<RegisterGcLiveRange>.Builder ranges,
                RegisterGcLiveRoot root,
                int start,
                int end)
            {
                if (start >= end)
                    return;

                if (_method.Funclets.Length <= 1 || _knownPositions.Count == 0)
                {
                    AddLiveRange(ranges, root, start, end, funcletIndex: 0);
                    return;
                }

                int currentStart = start;
                int previousFunclet = FuncletIndexForPosition(start);

                for (int i = 0; i < _knownPositions.Count; i++)
                {
                    int position = _knownPositions[i];
                    if (position <= start)
                        continue;
                    if (position >= end)
                        break;

                    int funcletIndex = FuncletIndexForPosition(position);
                    if (funcletIndex != previousFunclet)
                    {
                        if (currentStart < position)
                            AddLiveRange(ranges, root, currentStart, position, previousFunclet);
                        currentStart = position;
                        previousFunclet = funcletIndex;
                    }
                }

                if (currentStart < end)
                    AddLiveRange(ranges, root, currentStart, end, previousFunclet);
            }

            private void AddLiveRange(
                ImmutableArray<RegisterGcLiveRange>.Builder ranges,
                RegisterGcLiveRoot root,
                int start,
                int end,
                int funcletIndex)
            {
                var flags = RegisterGcLiveRangeFlags.None;

                if (_method.Funclets.Length > 1)
                    flags |= RegisterGcLiveRangeFlags.ReportOnlyInLeafFunclet;

                if (funcletIndex > 0 && IsFrameBacked(root.Location))
                    flags |= RegisterGcLiveRangeFlags.SharedWithParentFrame;

                if (IsFilterFunclet(funcletIndex) && IsFrameBacked(root.Location))
                    flags |= RegisterGcLiveRangeFlags.Pinned;

                ranges.Add(new RegisterGcLiveRange(root, start, end, funcletIndex, flags));
            }

            private static bool IsFrameBacked(RegisterOperand operand)
                => operand.IsFrameSlot || operand.IsSpillSlot || operand.IsOutgoingArgumentSlot;

            private bool IsFilterFunclet(int funcletIndex)
                => (uint)funcletIndex < (uint)_method.Funclets.Length && _method.Funclets[funcletIndex].Kind == RegisterFuncletKind.Filter;

            private ImmutableArray<RegisterGcInterruptibleRange> BuildInterruptibleRanges()
            {
                var ranges = ImmutableArray.CreateBuilder<RegisterGcInterruptibleRange>();
                bool fullyInterruptible = _method.Funclets.Length > 1 || _method.GenTreeMethod.Cfg.ExceptionRegions.Length != 0;

                if (fullyInterruptible)
                    AddFullyInterruptibleRanges(ranges);
                else
                    AddCallAndPollInterruptibleRanges(ranges);

                return NormalizeInterruptibleRanges(ranges.ToImmutable());
            }

            private void AddFullyInterruptibleRanges(ImmutableArray<RegisterGcInterruptibleRange>.Builder ranges)
            {
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var block = _method.Blocks[b];
                    int start = -1;
                    int end = -1;
                    int firstNodeId = int.MaxValue;
                    int lastNodeId = -1;
                    int funcletIndex = FuncletIndexForBlock(block.Id);

                    for (int i = 0; i < block.LinearNodes.Length; i++)
                    {
                        var node = block.LinearNodes[i];
                        if (!TryGetLinearPosition(node, out int position))
                            continue;

                        if (start < 0)
                            start = position;

                        end = position + 1;
                        firstNodeId = Math.Min(firstNodeId, node.Id);
                        lastNodeId = Math.Max(lastNodeId, node.Id);
                    }

                    if (start >= 0 && end > start && firstNodeId != int.MaxValue)
                    {
                        ranges.Add(new RegisterGcInterruptibleRange(
                            RegisterGcInterruptibleRangeKind.FullyInterruptible,
                            start,
                            end,
                            funcletIndex,
                            firstNodeId,
                            lastNodeId));
                    }
                }
            }

            private void AddCallAndPollInterruptibleRanges(ImmutableArray<RegisterGcInterruptibleRange>.Builder ranges)
            {
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var block = _method.Blocks[b];
                    int funcletIndex = FuncletIndexForBlock(block.Id);

                    for (int i = 0; i < block.LinearNodes.Length; i++)
                    {
                        var node = block.LinearNodes[i];
                        if (!TryClassifyInterruptibleNode(node, out var kind))
                            continue;
                        if (!TryGetLinearPosition(node, out int position))
                            continue;

                        ranges.Add(new RegisterGcInterruptibleRange(
                            kind,
                            position,
                            position + 1,
                            funcletIndex,
                            node.Id,
                            node.Id));
                    }
                }
            }

            private static ImmutableArray<RegisterGcInterruptibleRange> NormalizeInterruptibleRanges(ImmutableArray<RegisterGcInterruptibleRange> source)
            {
                if (source.Length == 0)
                    return ImmutableArray<RegisterGcInterruptibleRange>.Empty;

                var list = new List<RegisterGcInterruptibleRange>(source);
                list.Sort(static (a, b) =>
                {
                    int c = a.StartPosition.CompareTo(b.StartPosition);
                    if (c != 0) return c;
                    c = a.EndPosition.CompareTo(b.EndPosition);
                    if (c != 0) return c;
                    c = a.FuncletIndex.CompareTo(b.FuncletIndex);
                    if (c != 0) return c;
                    return a.Kind.CompareTo(b.Kind);
                });

                var result = ImmutableArray.CreateBuilder<RegisterGcInterruptibleRange>(list.Count);
                var current = list[0];
                for (int i = 1; i < list.Count; i++)
                {
                    var next = list[i];
                    if (current.Kind == next.Kind &&
                        current.FuncletIndex == next.FuncletIndex &&
                        current.EndPosition >= next.StartPosition)
                    {
                        current = new RegisterGcInterruptibleRange(
                            current.Kind,
                            current.StartPosition,
                            Math.Max(current.EndPosition, next.EndPosition),
                            current.FuncletIndex,
                            Math.Min(current.FirstNodeId, next.FirstNodeId),
                            Math.Max(current.LastNodeId, next.LastNodeId));
                    }
                    else
                    {
                        result.Add(current);
                        current = next;
                    }
                }

                result.Add(current);
                return result.ToImmutable();
            }

            private static ImmutableArray<RegisterGcLiveRange> NormalizeLiveRanges(ImmutableArray<RegisterGcLiveRange> source)
            {
                if (source.Length == 0)
                    return ImmutableArray<RegisterGcLiveRange>.Empty;

                var list = new List<RegisterGcLiveRange>(source);
                list.Sort(CompareLiveRanges);

                var result = ImmutableArray.CreateBuilder<RegisterGcLiveRange>(list.Count);
                var current = list[0];
                for (int i = 1; i < list.Count; i++)
                {
                    var next = list[i];
                    if (current.Root.Equals(next.Root) &&
                        current.FuncletIndex == next.FuncletIndex &&
                        current.Flags == next.Flags &&
                        current.EndPosition >= next.StartPosition)
                    {
                        current = new RegisterGcLiveRange(
                            current.Root,
                            current.StartPosition,
                            Math.Max(current.EndPosition, next.EndPosition),
                            current.FuncletIndex,
                            current.Flags);
                    }
                    else
                    {
                        if (!current.IsEmpty)
                            result.Add(current);
                        current = next;
                    }
                }

                if (!current.IsEmpty)
                    result.Add(current);

                return result.ToImmutable();
            }

            private static ImmutableArray<RegisterGcTransition> BuildTransitions(ImmutableArray<RegisterGcLiveRange> liveRanges)
            {
                var transitions = ImmutableArray.CreateBuilder<RegisterGcTransition>(liveRanges.Length * 2);

                for (int i = 0; i < liveRanges.Length; i++)
                {
                    var range = liveRanges[i];
                    transitions.Add(new RegisterGcTransition(range.StartPosition, RegisterGcTransitionKind.Enter, null, range.Root));
                    transitions.Add(new RegisterGcTransition(range.EndPosition, RegisterGcTransitionKind.Exit, range.Root, null));
                }

                transitions.Sort(static (a, b) =>
                {
                    int c = a.Position.CompareTo(b.Position);
                    if (c != 0) return c;
                    return TransitionSortRank(a.Kind).CompareTo(TransitionSortRank(b.Kind));
                });

                return transitions.ToImmutable();
            }

            private static int TransitionSortRank(RegisterGcTransitionKind kind)
            {
                return kind switch
                {
                    RegisterGcTransitionKind.Exit => 0,
                    RegisterGcTransitionKind.Enter => 1,
                    _ => 2,
                };
            }

            private static int CompareLiveRanges(RegisterGcLiveRange a, RegisterGcLiveRange b)
            {
                int c = a.StartPosition.CompareTo(b.StartPosition);
                if (c != 0) return c;
                c = a.EndPosition.CompareTo(b.EndPosition);
                if (c != 0) return c;
                c = a.FuncletIndex.CompareTo(b.FuncletIndex);
                if (c != 0) return c;
                c = a.Flags.CompareTo(b.Flags);
                if (c != 0) return c;
                c = a.Root.Value.Id.CompareTo(b.Root.Value.Id);
                if (c != 0) return c;
                c = a.Root.RootKind.CompareTo(b.Root.RootKind);
                if (c != 0) return c;
                c = a.Root.Offset.CompareTo(b.Root.Offset);
                if (c != 0) return c;
                return string.CompareOrdinal(a.Root.Location.ToString(), b.Root.Location.ToString());
            }

            private bool TryClassifyInterruptibleNode(GenTree node, out RegisterGcInterruptibleRangeKind kind)
            {
                if (node.Kind == GenTreeKind.GcPoll)
                {
                    kind = RegisterGcInterruptibleRangeKind.Poll;
                    return true;
                }

                if (GenTreeLirKinds.IsRealTree(node) && node.Source is not null)
                {
                    if (node.TreeKind is GenTreeKind.Call or GenTreeKind.VirtualCall or GenTreeKind.DelegateInvoke or GenTreeKind.NewObject or GenTreeKind.Throw or GenTreeKind.Rethrow)
                    {
                        kind = RegisterGcInterruptibleRangeKind.Call;
                        return true;
                    }

                    if (node.GenTreeLinearId >= 0 &&
                        _linearTreeById.TryGetValue(node.GenTreeLinearId, out var linearNode) &&
                        linearNode.HasLoweringFlag(GenTreeLinearFlags.GcSafePoint))
                    {
                        kind = RegisterGcInterruptibleRangeKind.Call;
                        return true;
                    }
                }

                kind = default;
                return false;
            }


            private bool TryGetLinearPosition(GenTree node, out int position)
            {
                if (node.GenTreeLinearId >= 0 && _linearTreePositions.TryGetValue(node.GenTreeLinearId, out position))
                    return true;

                if (node.Source is not null)
                {
                    for (int i = 0; i < _method.GenTreeMethod.LinearNodes.Length; i++)
                    {
                        var linearNode = _method.GenTreeMethod.LinearNodes[i];
                        if (linearNode is not null && linearNode.Id == node.Source.Id && _linearTreePositions.TryGetValue(linearNode.LinearId, out position))
                            return true;
                    }
                }

                position = -1;
                return false;
            }

            private int FuncletIndexForPosition(int position)
            {
                if (_positionBlockIds.TryGetValue(position, out int blockId))
                    return FuncletIndexForBlock(blockId);

                int bestPosition = -1;
                int bestBlock = 0;
                foreach (var kv in _positionBlockIds)
                {
                    if (kv.Key <= position && kv.Key > bestPosition)
                    {
                        bestPosition = kv.Key;
                        bestBlock = kv.Value;
                    }
                }

                return FuncletIndexForBlock(bestBlock);
            }

            private int FuncletIndexForBlock(int blockId)
            {
                for (int i = 0; i < _method.Funclets.Length; i++)
                {
                    var blocks = _method.Funclets[i].BlockIds;
                    for (int b = 0; b < blocks.Length; b++)
                    {
                        if (blocks[b] == blockId)
                            return _method.Funclets[i].Index;
                    }
                }

                return 0;
            }


            private static bool NeedsHomeGcReporting(RuntimeType? type, GenStackKind stackKind)
            {
                if (type is not null)
                {
                    if (type.Kind == RuntimeTypeKind.ByRef || type.Kind == RuntimeTypeKind.TypeParam || type.IsReferenceType)
                        return true;
                    if (type.IsValueType && type.ContainsGcPointers)
                        return true;
                }

                return stackKind is GenStackKind.Ref or GenStackKind.ByRef or GenStackKind.Null;
            }

            private static bool TryGetGcRootKind(RuntimeType? type, GenStackKind stackKind, out RegisterGcRootKind kind)
            {
                if (type is not null)
                {
                    if (type.Kind == RuntimeTypeKind.ByRef)
                    {
                        kind = RegisterGcRootKind.InteriorPointer;
                        return true;
                    }

                    if (type.IsReferenceType || type.Kind == RuntimeTypeKind.TypeParam)
                    {
                        kind = RegisterGcRootKind.ObjectReference;
                        return true;
                    }
                }

                if (stackKind == GenStackKind.ByRef)
                {
                    kind = RegisterGcRootKind.InteriorPointer;
                    return true;
                }

                if (stackKind is GenStackKind.Ref or GenStackKind.Null)
                {
                    kind = RegisterGcRootKind.ObjectReference;
                    return true;
                }

                kind = default;
                return false;
            }

            private static Dictionary<int, GenTree> BuildLinearTreeMap(GenTreeMethod method)
            {
                var result = new Dictionary<int, GenTree>();
                for (int i = 0; i < method.LinearNodes.Length; i++)
                    result[method.LinearNodes[i].LinearId] = method.LinearNodes[i];
                return result;
            }

            private static Dictionary<int, int> BuildOriginalLinearTreePositions(GenTreeMethod method)
            {
                var result = new Dictionary<int, int>();
                int position = 0;
                var order = method.LinearBlockOrder.IsDefaultOrEmpty
                    ? LinearBlockOrder.Compute(method.Cfg)
                    : method.LinearBlockOrder;

                for (int o = 0; o < order.Length; o++)
                {
                    int blockId = order[o];
                    int groupPosition = -1;
                    GenTree? groupHead = null;

                    for (int i = 0; i < method.LinearNodes.Length; i++)
                    {
                        var node = method.LinearNodes[i];
                        if (node.LinearBlockId != blockId)
                            continue;

                        if (node.IsPhiCopy && groupHead is not null && SamePhiCopyGroup(groupHead, node))
                        {
                            result[node.LinearId] = groupPosition;
                            continue;
                        }

                        groupHead = node.IsPhiCopy ? node : null;
                        groupPosition = position;
                        result[node.LinearId] = position;
                        position += 2;
                    }

                    position += 2;
                }

                return result;
            }

            private static Dictionary<int, int> BuildFinalLinearTreePositions(
                RegisterAllocatedMethod method,
                out Dictionary<int, int> positionBlockIds,
                out int finalEndPosition)
            {
                var result = new Dictionary<int, int>();
                positionBlockIds = new Dictionary<int, int>();
                int position = 0;
                var order = method.GenTreeMethod.LinearBlockOrder.IsDefaultOrEmpty
                    ? LinearBlockOrder.Compute(method.GenTreeMethod.Cfg)
                    : method.GenTreeMethod.LinearBlockOrder;

                for (int o = 0; o < order.Length; o++)
                {
                    int b = order[o];
                    var nodes = method.Blocks[b].LinearNodes;
                    for (int n = 0; n < nodes.Length; n++)
                    {
                        var node = nodes[n];
                        if (result.ContainsKey(node.LinearId))
                            throw new InvalidOperationException($"Duplicate GenTree LinearId {node.LinearId} in final LIR stream.");
                        result.Add(node.LinearId, position);
                        positionBlockIds[position] = b;

                        if (node.IsPhiCopy)
                        {
                            while (n + 1 < nodes.Length && SamePhiCopyGroup(node, nodes[n + 1]))
                            {
                                n++;
                                if (result.ContainsKey(nodes[n].LinearId))
                                    throw new InvalidOperationException($"Duplicate GenTree LinearId {nodes[n].LinearId} in final LIR stream.");
                                result.Add(nodes[n].LinearId, position);
                            }
                        }

                        position += 2;
                    }
                    position += 2;
                }

                finalEndPosition = position;
                return result;
            }

            private static SortedDictionary<int, int> BuildOriginalToFinalPositionMap(
                Dictionary<int, int> originalPositions,
                Dictionary<int, int> finalPositions,
                int finalEndPosition)
            {
                var result = new SortedDictionary<int, int>();

                foreach (var kv in originalPositions)
                {
                    int linearId = kv.Key;
                    int original = kv.Value;
                    if (!finalPositions.TryGetValue(linearId, out int final))
                        continue;

                    AddPositionMap(result, original, final);
                    AddPositionMap(result, original + 1, final + 1);
                }

                AddPositionMap(result, int.MaxValue, finalEndPosition);
                return result;
            }

            private static void AddPositionMap(SortedDictionary<int, int> map, int original, int final)
            {
                if (map.TryGetValue(original, out int existing))
                {
                    if (final < existing)
                        map[original] = final;
                    return;
                }

                map.Add(original, final);
            }

            private static bool SamePhiCopyGroup(GenTree left, GenTree right)
                => left.IsPhiCopy &&
                   right.IsPhiCopy &&
                   left.LinearPhiCopyFromBlockId == right.LinearPhiCopyFromBlockId &&
                   left.LinearPhiCopyToBlockId == right.LinearPhiCopyToBlockId;

            private static List<int> BuildKnownPositions(Dictionary<int, int> positionBlockIds)
            {
                var result = new List<int>(positionBlockIds.Count);
                foreach (var kv in positionBlockIds)
                    result.Add(kv.Key);
                result.Sort();
                return result;
            }
        }
    }
}
