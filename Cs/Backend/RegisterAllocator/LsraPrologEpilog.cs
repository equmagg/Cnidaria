using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;

namespace Cnidaria.Cs
{
    internal static class RegisterPrologEpilogGenerator
    {
        public static RegisterAllocatedMethod GenerateMethod(RegisterAllocatedMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            if (method.HasPrologEpilog)
                return method;

            var builder = new MethodBuilder(method);
            return builder.Run();
        }

        private sealed class MethodBuilder
        {
            private readonly RegisterAllocatedMethod _method;
            private readonly ImmutableArray<RegisterFrameRegion>.Builder _frameRegions = ImmutableArray.CreateBuilder<RegisterFrameRegion>();
            private int _nextNodeId;
            private TargetInfo Target => _method.GenTreeMethod.Target;

            public MethodBuilder(RegisterAllocatedMethod method)
            {
                _method = method;
                int max = -1;
                for (int i = 0; i < method.LinearNodes.Length; i++)
                {
                    if (method.LinearNodes[i].Id > max)
                        max = method.LinearNodes[i].Id;
                }
                _nextNodeId = checked(max + 1);
            }

            public RegisterAllocatedMethod Run()
            {
                var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(_method.Blocks.Length);
                var all = ImmutableArray.CreateBuilder<GenTree>(_method.LinearNodes.Length
                    + EstimateFrameNodeCount());

                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var sourceBlock = _method.Blocks[b];
                    var blockLinearNodes = ImmutableArray.CreateBuilder<GenTree>(
                        sourceBlock.LinearNodes.Length + (b == 0 ? EstimatePrologNodeCount() : 0)
                        + EstimateBlockEpilogNodeCount(sourceBlock));

                    if (NeedsFuncletProlog(sourceBlock.Id))
                        AppendProlog(sourceBlock.Id, FuncletIndexForEntryBlock(sourceBlock.Id), blockLinearNodes);

                    for (int i = 0; i < sourceBlock.LinearNodes.Length; i++)
                    {
                        var node = sourceBlock.LinearNodes[i];
                        if (node.Kind == GenTreeKind.Return)
                        {
                            node = NormalizeReturnOperand(sourceBlock.Id, node, blockLinearNodes);
                            int funcletIndex = FuncletIndexForBlock(sourceBlock.Id);
                            bool appendEpilog = funcletIndex != 0 || !ReturnMustRunFinallyBeforeMethodExit(sourceBlock.Id);
                            if (IsHiddenReturnBufferReturn(node))
                            {
                                blockLinearNodes.Add(node);
                                if (appendEpilog)
                                    AppendEpilog(sourceBlock.Id, funcletIndex, blockLinearNodes);
                                blockLinearNodes.Add(CreateVoidReturn(sourceBlock.Id, node, blockLinearNodes.Count));
                                continue;
                            }
                            if (appendEpilog)
                                AppendEpilog(sourceBlock.Id, funcletIndex, blockLinearNodes);
                        }
                        else if (node.Kind == GenTreeKind.EndFinally)
                        {
                            AppendEpilog(sourceBlock.Id, FuncletIndexForBlock(sourceBlock.Id), blockLinearNodes);
                        }
                        else if (node.Kind == GenTreeKind.Branch &&
                            node.SourceOp == BytecodeOp.Leave &&
                            FuncletIndexForBlock(sourceBlock.Id) != 0)
                        {
                            AppendEpilog(sourceBlock.Id, FuncletIndexForBlock(sourceBlock.Id), blockLinearNodes);
                        }

                        blockLinearNodes.Add(node);
                    }

                    var normalized = NormalizeOrdinals(blockLinearNodes.ToImmutable());
                    for (int i = 0; i < normalized.Length; i++)
                        all.Add(normalized[i]);

                    sourceBlock.SetLinearNodes(normalized);
                    blocks.Add(sourceBlock);
                }

                var allNodes = all.ToImmutable();
                var unwindCodes = BuildUnwindCodes(allNodes);

                return new RegisterAllocatedMethod(
                    _method.GenTreeMethod,
                    blocks.ToImmutable(),
                    allNodes,
                    _method.Allocations,
                    _method.AllocationByNode,
                    _method.InternalRegistersByNodeId,
                    _method.SpillSlotCount,
                    _method.ParallelCopyScratchSpillSlot,
                    _method.StackFrame,
                    hasPrologEpilog: true,
                    unwindCodes: unwindCodes,
                    gcLiveRanges: _method.GcLiveRanges,
                    gcTransitions: _method.GcTransitions,
                    gcInterruptibleRanges: _method.GcInterruptibleRanges,
                    funclets: _method.Funclets,
                    frameRegions: _frameRegions.ToImmutable(),
                    gcReportOnlyLeafFunclet: _method.GcReportOnlyLeafFunclet,
                    lsraNodePositions: _method.LsraNodePositions,
                    lsraBlockStartPositions: _method.LsraBlockStartPositions,
                    lsraBlockEndPositions: _method.LsraBlockEndPositions);
            }

            private bool IsHiddenReturnBufferReturn(GenTree node)
            {
                if (node.Kind != GenTreeKind.Return || node.Uses.Length == 0)
                    return false;

                return MachineAbi.RequiresHiddenReturnBuffer(_method.GenTreeMethod.RuntimeMethod, Target);
            }

            private GenTree CreateVoidReturn(int blockId, GenTree source, int ordinal)
            {
                int id = _nextNodeId++;
                var node = new GenTree(
                    id,
                    GenTreeKind.Return,
                    source.Pc,
                    source.SourceOp,
                    type: null,
                    stackKind: GenStackKind.Void,
                    flags: GenTreeFlags.ControlFlow | GenTreeFlags.Ordered,
                    operands: ImmutableArray<GenTree>.Empty);

                return GenTreeLirFactory.Tree(
                    id,
                    blockId,
                    ordinal,
                    node,
                    RegisterOperand.None,
                    ImmutableArray<RegisterOperand>.Empty,
                    (GenTree?)null,
                    linearUses: ImmutableArray<GenTree>.Empty,
                    linearId: id);
            }

            private ImmutableArray<RegisterUnwindCode> BuildUnwindCodes(ImmutableArray<GenTree> nodes)
            {
                var result = ImmutableArray.CreateBuilder<RegisterUnwindCode>();

                for (int i = 0; i < nodes.Length; i++)
                {
                    var node = nodes[i];
                    if (node.Kind != GenTreeKind.StackFrameOp)
                        continue;

                    if (!TryMapUnwindCode(node, out var code))
                        continue;

                    result.Add(code);
                }

                result.Sort(static (a, b) =>
                {
                    int c = a.BlockId.CompareTo(b.BlockId);
                    if (c != 0)
                        return c;
                    c = a.Ordinal.CompareTo(b.Ordinal);
                    if (c != 0)
                        return c;
                    return a.NodeId.CompareTo(b.NodeId);
                });

                return result.ToImmutable();
            }

            private bool TryMapUnwindCode(GenTree node, out RegisterUnwindCode code)
            {
                RegisterUnwindCodeKind kind;
                MachineRegister register = MachineRegister.Invalid;
                int stackOffset = 0;
                int size = 0;

                switch (node.FrameOperation)
                {
                    case FrameOperation.AllocateFrame:
                        kind = RegisterUnwindCodeKind.AllocateStack;
                        size = node.Immediate;
                        break;

                    case FrameOperation.SaveReturnAddress:
                        {
                            kind = RegisterUnwindCodeKind.SaveReturnAddress;
                            if (node.Uses.Length != 0 && node.Uses[0].IsRegister)
                                register = node.Uses[0].Register;
                            if (node.Results.Length != 1)
                            {
                                code = default;
                                return false;
                            }
                            var resultOperand = node.Results[0];
                            stackOffset = resultOperand.FrameOffset;
                            size = resultOperand.FrameSlotSize;
                            break;
                        }

                    case FrameOperation.SaveCalleeSavedRegister:
                        {
                            kind = RegisterUnwindCodeKind.SaveCalleeSavedRegister;
                            if (node.Uses.Length != 0 && node.Uses[0].IsRegister)
                                register = node.Uses[0].Register;
                            if (node.Results.Length != 1)
                            {
                                code = default;
                                return false;
                            }
                            var resultOperand = node.Results[0];
                            stackOffset = resultOperand.FrameOffset;
                            size = resultOperand.FrameSlotSize;
                            break;
                        }

                    case FrameOperation.EstablishFramePointer:
                        kind = RegisterUnwindCodeKind.SetFramePointer;
                        register = RegisterInfo.FramePointer(Target);
                        break;

                    default:
                        code = default;
                        return false;
                }

                code = new RegisterUnwindCode(
                    node.Id,
                    node.BlockId,
                    node.Ordinal,
                    kind,
                    register,
                    stackOffset,
                    size);
                return true;
            }

            private void AppendProlog(int blockId, int funcletIndex, ImmutableArray<GenTree>.Builder nodes)
            {
                int firstNodeIndex = nodes.Count;
                int firstNodeId = _nextNodeId;
                if (funcletIndex != 0)
                {
                    nodes.Add(GenTreeLirFactory.Frame(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        FrameOperation.EnterFuncletFrame,
                        _method.StackFrame.UsesFramePointer
                            ? RegisterOperand.ForRegister(RegisterInfo.FramePointer(Target))
                            : RegisterOperand.ForRegister(RegisterInfo.StackPointer(Target)),
                        ImmutableArray.Create(RegisterOperand.ForRegister(RegisterInfo.StackPointer(Target))),
                        immediate: 0,
                        comment: "funclet prolog: attach to establisher frame"));

                    _frameRegions.Add(new RegisterFrameRegion(
                        RegisterFrameRegionKind.Prolog,
                        funcletIndex,
                        blockId,
                        firstNodeId,
                        _nextNodeId - 1));
                    MarkPrologNodes(nodes, firstNodeIndex);
                    return;
                }

                int frameSize = _method.StackFrame.FrameSize;
                if (frameSize > 0)
                {
                    nodes.Add(GenTreeLirFactory.Frame(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        FrameOperation.AllocateFrame,
                        RegisterOperand.ForRegister(RegisterInfo.StackPointer(Target)),
                        ImmutableArray.Create(RegisterOperand.ForRegister(RegisterInfo.StackPointer(Target))),
                        frameSize,
                        "prolog: sp -= frameSize"));
                }

                for (int i = 0; i < _method.StackFrame.CalleeSavedSlots.Length; i++)
                {
                    var slot = _method.StackFrame.CalleeSavedSlots[i];
                    bool isReturnAddress = slot.Kind == StackFrameSlotKind.ReturnAddress;
                    nodes.Add(GenTreeLirFactory.Frame(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        isReturnAddress ? FrameOperation.SaveReturnAddress : FrameOperation.SaveCalleeSavedRegister,
                        FrameOperand(slot, RegisterFrameBase.StackPointer),
                        ImmutableArray.Create(RegisterOperand.ForRegister(slot.SavedRegister)),
                        immediate: 0,
                        comment: "prolog: save " + MachineRegisters.Format(slot.SavedRegister)));
                }

                if (_method.StackFrame.UsesFramePointer)
                {
                    nodes.Add(GenTreeLirFactory.Frame(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        FrameOperation.EstablishFramePointer,
                        RegisterOperand.ForRegister(RegisterInfo.FramePointer(Target)),
                        ImmutableArray.Create(RegisterOperand.ForRegister(RegisterInfo.StackPointer(Target))),
                        immediate: 0,
                        comment: "prolog: fp = sp"));
                }

                if (_nextNodeId > firstNodeId)
                {
                    _frameRegions.Add(new RegisterFrameRegion(
                        RegisterFrameRegionKind.Prolog,
                        funcletIndex,
                        blockId,
                        firstNodeId,
                        _nextNodeId - 1));
                }

                AppendGcHomeSlotZeroInit(blockId, nodes);
                AppendIncomingArgumentHomeStores(blockId, nodes);
                AppendInitialSsaArgumentValueStores(blockId, nodes);
                MarkPrologNodes(nodes, firstNodeIndex);
            }

            private static void MarkPrologNodes(ImmutableArray<GenTree>.Builder nodes, int firstNodeIndex)
            {
                for (int i = firstNodeIndex; i < nodes.Count; i++)
                    nodes[i].Flags |= GenTreeFlags.Prolog;
            }

            private void AppendGcHomeSlotZeroInit(int blockId, ImmutableArray<GenTree>.Builder nodes)
            {
                ZeroGcDescriptorHomes(blockId, nodes, _method.StackFrame.LocalSlots, _method.GenTreeMethod.LocalDescriptors);
                ZeroGcDescriptorHomes(blockId, nodes, _method.StackFrame.TempSlots, _method.GenTreeMethod.TempDescriptors);
            }

            private void ZeroGcDescriptorHomes(
                int blockId,
                ImmutableArray<GenTree>.Builder nodes,
                ImmutableArray<StackFrameSlot> slots,
                ImmutableArray<GenLocalDescriptor> descriptors)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    var slot = slots[i];
                    if (!ShouldZeroGcHomeSlot(slot, descriptors))
                        continue;

                    EmitZeroFrameSlot(blockId, nodes, slot);
                }
            }

            private static bool ShouldZeroGcHomeSlot(StackFrameSlot slot, ImmutableArray<GenLocalDescriptor> descriptors)
            {
                if (!NeedsHomeGcReporting(slot.Type, StackKindForType(slot.Type)))
                    return false;

                for (int i = 0; i < descriptors.Length; i++)
                {
                    var descriptor = descriptors[i];
                    if (descriptor.Index != slot.Index)
                        continue;

                    return ShouldReportDescriptorHome(descriptor);
                }

                return true;
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

            private void EmitZeroFrameSlot(int blockId, ImmutableArray<GenTree>.Builder nodes, StackFrameSlot slot)
            {
                int remaining = Math.Max(1, slot.Size);
                int offset = 0;
                while (remaining > 0)
                {
                    int chunk = remaining >= 8 ? 8 : remaining >= 4 ? 4 : remaining >= 2 ? 2 : 1;
                    var destination = RegisterOperand.ForFrameSlot(
                        RegisterClass.General,
                        slot.Kind,
                        RegisterFrameBase.StackPointer,
                        slot.Index,
                        checked(slot.Offset + offset),
                        chunk);

                    if (Target.IsX86)
                    {
                        nodes.Add(GenTreeLirFactory.DefaultValue(
                            _nextNodeId++,
                            blockId,
                            nodes.Count,
                            destination,
                            type: null,
                            stackKind: chunk == 8 ? GenStackKind.I8 : GenStackKind.I4,
                            comment: "prolog: zero GC home slot"));
                    }
                    else
                    {
                        nodes.Add(GenTreeLirFactory.Move(
                            _nextNodeId++,
                            blockId,
                            nodes.Count,
                            destination,
                            RegisterOperand.ForRegister(MachineRegisters.Zero),
                            destinationValue: null,
                            sourceValue: null,
                            comment: "prolog: zero GC home slot",
                            moveFlags: MoveFlags.Internal));
                    }

                    offset += chunk;
                    remaining -= chunk;
                }
            }

            private static GenStackKind StackKindForType(RuntimeType? type)
            {
                if (type is null)
                    return GenStackKind.Unknown;
                if (type.Kind == RuntimeTypeKind.ByRef)
                    return GenStackKind.ByRef;
                if (type.IsReferenceType || type.Kind == RuntimeTypeKind.TypeParam)
                    return GenStackKind.Ref;
                if (type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.FunctionPointer)
                    return GenStackKind.Ptr;
                if (type.Name == "Single")
                    return GenStackKind.R4;
                if (type.Name == "Double")
                    return GenStackKind.R8;
                if (type.SizeOf <= 4)
                    return GenStackKind.I4;
                if (type.SizeOf <= 8)
                    return GenStackKind.I8;
                return GenStackKind.Value;
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

            private void AppendIncomingArgumentHomeStores(int blockId, ImmutableArray<GenTree>.Builder nodes)
            {
                var runtimeMethod = _method.GenTreeMethod.RuntimeMethod;
                var argTypes = _method.GenTreeMethod.ArgTypes;
                if (_method.StackFrame.ArgumentSlots.IsDefaultOrEmpty)
                    return;

                int generalArgumentIndex = 0;
                int floatArgumentIndex = 0;
                int incomingStackArgumentIndex = RegisterInfo.MinimumOutgoingArgumentSlots(Target);
                int hiddenReturnBufferIndex = MachineAbi.HiddenReturnBufferInsertionIndex(runtimeMethod, argTypes.Length, Target);

                for (int i = 0; i < argTypes.Length; i++)
                {
                    if (hiddenReturnBufferIndex == i)
                        EmitIncomingHiddenReturnBufferHomeStore(blockId, nodes, argTypes.Length, ref generalArgumentIndex, ref floatArgumentIndex, ref incomingStackArgumentIndex);

                    RuntimeType argType = argTypes[i];
                    var argAbi = MachineAbi.ClassifyValue(argType, MachineAbi.StackKindForType(argType), isReturn: false, target: Target);
                    argAbi = MachineAbi.AdjustArgumentAbiForRegisterAvailability(argAbi, generalArgumentIndex, floatArgumentIndex, Target);
                    if (argAbi.PassingKind == AbiValuePassingKind.Void)
                        continue;

                    bool hasHomeSlot = _method.StackFrame.TryGetArgumentSlot(i, out StackFrameSlot homeSlot);

                    if (argAbi.PassingKind == AbiValuePassingKind.ScalarRegister)
                    {
                        var registerClass = argAbi.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : argAbi.RegisterClass;
                        var source = MachineAbi.AssignScalarArgumentLocation(
                            registerClass,
                            argAbi.Size <= 0 ? Target.PointerSize : argAbi.Size,
                            ref generalArgumentIndex,
                            ref floatArgumentIndex,
                            ref incomingStackArgumentIndex,
                            Target);
                        if (hasHomeSlot)
                            EmitIncomingArgumentHomeStore(blockId, nodes, homeSlot, source, 0, registerClass, source.Size);
                        continue;
                    }

                    if (argAbi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        int aggregateStackSlot = -1;
                        int aggregateStackBaseOffset = 0;
                        var segments = MachineAbi.GetRegisterSegments(argAbi, Target);
                        for (int s = 0; s < segments.Length; s++)
                        {
                            var segment = segments[s];
                            var source = MachineAbi.AssignAggregateSegmentArgumentLocation(
                                segment,
                                ref generalArgumentIndex,
                                ref floatArgumentIndex,
                                ref incomingStackArgumentIndex,
                                ref aggregateStackSlot,
                                ref aggregateStackBaseOffset,
                                Target);
                            if (hasHomeSlot)
                                EmitIncomingArgumentHomeStore(blockId, nodes, homeSlot, source, segment.Offset, segment.RegisterClass, segment.Size);
                        }
                        continue;
                    }

                    int stackSize = argAbi.Size <= 0 ? Target.PointerSize : argAbi.Size;
                    int stackSlot = incomingStackArgumentIndex;
                    incomingStackArgumentIndex = checked(incomingStackArgumentIndex + MachineAbi.StackSlotsForArgumentSize(stackSize, Target));
                    var stackSource = AbiArgumentLocation.ForStack(
                        RegisterClass.General,
                        stackSlot,
                        0,
                        stackSize);
                    if (hasHomeSlot)
                        EmitIncomingArgumentHomeStore(blockId, nodes, homeSlot, stackSource, 0, RegisterClass.General, stackSize);
                }

                if (hiddenReturnBufferIndex == argTypes.Length)
                    EmitIncomingHiddenReturnBufferHomeStore(blockId, nodes, argTypes.Length, ref generalArgumentIndex, ref floatArgumentIndex, ref incomingStackArgumentIndex);
            }


            private void AppendInitialSsaArgumentValueStores(int blockId, ImmutableArray<GenTree>.Builder nodes)
            {
                var method = _method.GenTreeMethod;
                var argTypes = method.ArgTypes;
                if (argTypes.IsDefaultOrEmpty || _method.Allocations.IsDefaultOrEmpty)
                    return;

                var emitted = new HashSet<GenTreeValueKey>();

                for (int i = 0; i < _method.Allocations.Length; i++)
                {
                    var allocation = _method.Allocations[i];
                    var key = allocation.ValueKey;
                    if (!IsInitialSsaArgumentValue(method, key))
                        continue;
                    if (!emitted.Add(key))
                        continue;

                    int argumentIndex = key.SsaSlot.Index;

                    if (!method.ValueInfoByNode.TryGetValue(key, out var valueInfo))
                        continue;

                    RuntimeType? argumentType;
                    GenStackKind argumentStackKind;
                    AbiValueInfo argumentAbi;
                    ImmutableArray<RegisterOperand> sources;

                    if ((uint)argumentIndex < (uint)argTypes.Length)
                    {
                        argumentType = valueInfo.Type ?? argTypes[argumentIndex];
                        argumentStackKind = valueInfo.StackKind == GenStackKind.Unknown
                            ? MachineAbi.StackKindForType(argumentType)
                            : valueInfo.StackKind;

                        argumentAbi = MachineAbi.ClassifyValue(argumentType, argumentStackKind, isReturn: false, target: Target);
                        if (argumentAbi.PassingKind == AbiValuePassingKind.Void)
                            continue;

                        sources = GetIncomingArgumentOperands(
                            argumentIndex,
                            argumentType,
                            argumentStackKind,
                            valueInfo.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : valueInfo.RegisterClass,
                            argumentAbi);
                    }
                    else
                    {
                        if (!TryGetIncomingPromotedArgumentFieldOperand(
                                key,
                                valueInfo.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : valueInfo.RegisterClass,
                                out argumentType,
                                out argumentStackKind,
                                out var promotedFieldSource))
                        {
                            continue;
                        }

                        argumentAbi = MachineAbi.ClassifyValue(argumentType, argumentStackKind, isReturn: false, target: Target);
                        if (argumentAbi.PassingKind == AbiValuePassingKind.Void)
                            continue;

                        sources = ImmutableArray.Create(promotedFieldSource);
                    }

                    int position = Math.Max(0, allocation.DefinitionPosition);

                    if (argumentAbi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        var destination = allocation.ValueLocationAt(position, argumentAbi);
                        EmitInitialSsaArgumentFragments(blockId, nodes, allocation.Value, destination, sources);
                        continue;
                    }

                    if (sources.Length != 1)
                        throw new InvalidOperationException("Scalar incoming argument must have exactly one ABI source operand.");

                    var scalarDestination = allocation.LocationAt(position);
                    if (allocation.Home.IsMemoryOperand && !allocation.Home.Equals(scalarDestination))
                        EmitInitialSsaArgumentMove(blockId, nodes, allocation.Value, allocation.Home, sources[0]);
                    EmitInitialSsaArgumentMove(blockId, nodes, allocation.Value, scalarDestination, sources[0]);
                }
            }

            private static bool IsInitialSsaArgumentValue(GenTreeMethod method, GenTreeValueKey key)
            {
                if (!key.IsSsaValue || key.SsaSlot.Kind != SsaSlotKind.Arg)
                    return false;

                if (!method.ValueInfoByNode.TryGetValue(key, out var valueInfo))
                    return false;

                return valueInfo.DefinitionBlockId < 0 && valueInfo.DefinitionNodeId < 0;
            }

            private void EmitInitialSsaArgumentFragments(
                int blockId,
                ImmutableArray<GenTree>.Builder nodes,
                GenTree value,
                RegisterValueLocation destination,
                ImmutableArray<RegisterOperand> sources)
            {
                if (destination.IsEmpty)
                    return;
                if (destination.Count != sources.Length)
                    throw new InvalidOperationException("Initial SSA argument ABI source count does not match allocated destination fragment count.");

                for (int i = 0; i < sources.Length; i++)
                    EmitInitialSsaArgumentMove(blockId, nodes, value, destination[i], sources[i]);
            }

            private void EmitInitialSsaArgumentMove(
                int blockId,
                ImmutableArray<GenTree>.Builder nodes,
                GenTree value,
                RegisterOperand destination,
                RegisterOperand source)
            {
                if (destination.IsNone || source.IsNone || destination.Equals(source))
                    return;

                if (destination.IsMemoryOperand && source.IsMemoryOperand)
                {
                    if (Target.IsX86)
                    {
                        nodes.Add(GenTreeLirFactory.Move(
                            _nextNodeId++,
                            blockId,
                            nodes.Count,
                            destination,
                            source,
                            destinationValue: value,
                            sourceValue: null,
                            comment: "prolog: initialize initial SSA argument value",
                            moveFlags: MoveFlags.AbiArgument | MoveFlags.Internal));
                        return;
                    }

                    var scratch = RegisterOperand.ForRegister(SelectInitialSsaArgumentCopyScratch(destination, source));
                    nodes.Add(GenTreeLirFactory.Move(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        scratch,
                        source,
                        destinationValue: null,
                        sourceValue: null,
                        comment: "prolog: initialize initial SSA argument value reload",
                        moveFlags: MoveFlags.AbiArgument | MoveFlags.Internal | MoveFlags.Reload));

                    nodes.Add(GenTreeLirFactory.Move(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        destination,
                        scratch,
                        destinationValue: value,
                        sourceValue: null,
                        comment: "prolog: initialize initial SSA argument value store",
                        moveFlags: MoveFlags.AbiArgument | MoveFlags.Internal | MoveFlags.Spill));
                    return;
                }

                nodes.Add(GenTreeLirFactory.Move(
                    _nextNodeId++,
                    blockId,
                    nodes.Count,
                    destination,
                    source,
                    destinationValue: value,
                    sourceValue: null,
                    comment: "prolog: initialize initial SSA argument value",
                    moveFlags: MoveFlags.AbiArgument | MoveFlags.Internal));
            }

            private MachineRegister SelectInitialSsaArgumentCopyScratch(RegisterOperand destination, RegisterOperand source)
            {
                if (destination.RegisterClass == RegisterClass.Float || source.RegisterClass == RegisterClass.Float)
                    return RegisterInfo.ParallelCopyScratch(Target, RegisterClass.Float);
                return RegisterInfo.ParallelCopyScratch(Target, RegisterClass.General);
            }

            private bool TryGetIncomingPromotedArgumentFieldOperand(
                GenTreeValueKey key,
                RegisterClass fieldRegisterClass,
                out RuntimeType? fieldType,
                out GenStackKind fieldStackKind,
                out RegisterOperand source)
            {
                fieldType = null;
                fieldStackKind = GenStackKind.Unknown;
                source = default;

                var method = _method.GenTreeMethod;
                if (!key.SsaSlot.HasLclNum || (uint)key.SsaSlot.LclNum >= (uint)method.AllLocalDescriptors.Length)
                    return false;

                var fieldDescriptor = method.AllLocalDescriptors[key.SsaSlot.LclNum];
                if (fieldDescriptor.Kind != GenLocalKind.Argument || fieldDescriptor.Category != GenLocalCategory.PromotedStructField || fieldDescriptor.ParentLclNum < 0)
                    return false;

                GenLocalDescriptor? parentDescriptor = null;
                if ((uint)fieldDescriptor.ParentLclNum < (uint)method.AllLocalDescriptors.Length)
                    parentDescriptor = method.AllLocalDescriptors[fieldDescriptor.ParentLclNum];

                if (parentDescriptor is null || parentDescriptor.Kind != GenLocalKind.Argument)
                    return false;
                if ((uint)parentDescriptor.Index >= (uint)method.ArgTypes.Length)
                    return false;

                fieldType = fieldDescriptor.Type;
                fieldStackKind = fieldDescriptor.StackKind;

                return TryGetIncomingArgumentFieldOperand(
                    parentDescriptor.Index,
                    parentDescriptor.Type,
                    parentDescriptor.StackKind,
                    fieldDescriptor.FieldOffset,
                    Math.Max(1, fieldDescriptor.FieldSize),
                    fieldRegisterClass,
                    out source);
            }

            private bool TryGetIncomingArgumentFieldOperand(
                int parentArgumentIndex,
                RuntimeType? parentArgumentType,
                GenStackKind parentStackKind,
                int fieldOffset,
                int fieldSize,
                RegisterClass fieldRegisterClass,
                out RegisterOperand source)
            {
                source = default;
                if (parentArgumentIndex < 0 || fieldOffset < 0 || fieldSize <= 0)
                    return false;

                int generalArgumentIndex = 0;
                int floatArgumentIndex = 0;
                int incomingStackArgumentIndex = RegisterInfo.MinimumOutgoingArgumentSlots(Target);
                int hiddenReturnBufferIndex = MachineAbi.HiddenReturnBufferInsertionIndex(
                    _method.GenTreeMethod.RuntimeMethod,
                    _method.GenTreeMethod.ArgTypes.Length,
                    Target);

                for (int i = 0; i <= parentArgumentIndex; i++)
                {
                    if (hiddenReturnBufferIndex == i)
                        ConsumeIncomingHiddenReturnBuffer(ref generalArgumentIndex, ref floatArgumentIndex, ref incomingStackArgumentIndex);

                    RuntimeType currentType = _method.GenTreeMethod.ArgTypes[i];
                    GenStackKind currentStackKind = i == parentArgumentIndex ? parentStackKind : MachineAbi.StackKindForType(currentType);
                    var valueAbi = i == parentArgumentIndex
                        ? MachineAbi.ClassifyValue(parentArgumentType, currentStackKind, isReturn: false, target: Target)
                        : MachineAbi.ClassifyValue(currentType, currentStackKind, isReturn: false, target: Target);
                    var abi = MachineAbi.AdjustArgumentAbiForRegisterAvailability(
                        valueAbi,
                        generalArgumentIndex,
                        floatArgumentIndex,
                        Target);

                    if (i == parentArgumentIndex && !MachineAbi.HaveMatchingArgumentValueLayout(valueAbi, abi, Target))
                    {
                        if (!_method.StackFrame.TryGetArgumentSlot(parentArgumentIndex, out StackFrameSlot homeSlot))
                            return false;

                        source = RegisterOperand.ForFrameSlot(
                            fieldRegisterClass == RegisterClass.Invalid ? RegisterClass.General : fieldRegisterClass,
                            StackFrameSlotKind.Argument,
                            RegisterFrameBase.StackPointer,
                            homeSlot.Index,
                            checked(homeSlot.Offset + fieldOffset),
                            fieldSize);
                        return true;
                    }

                    if (abi.PassingKind == AbiValuePassingKind.Void)
                        continue;

                    if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                    {
                        var registerClass = abi.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : abi.RegisterClass;
                        var location = MachineAbi.AssignScalarArgumentLocation(
                            registerClass,
                            abi.Size <= 0 ? Target.PointerSize : abi.Size,
                            ref generalArgumentIndex,
                            ref floatArgumentIndex,
                            ref incomingStackArgumentIndex,
                            Target);

                        if (i != parentArgumentIndex)
                            continue;

                        int scalarSize = Math.Max(1, location.Size);
                        if (fieldOffset == 0 && fieldSize == scalarSize)
                        {
                            source = OperandForIncomingAbiLocation(location);
                            return true;
                        }

                        int fieldEnd = fieldOffset + fieldSize;
                        if (fieldOffset < 0 || fieldEnd > scalarSize)
                            return false;

                        if (_method.StackFrame.TryGetArgumentSlot(parentArgumentIndex, out StackFrameSlot homeSlot))
                        {
                            source = RegisterOperand.ForFrameSlot(
                                fieldRegisterClass == RegisterClass.Invalid ? registerClass : fieldRegisterClass,
                                StackFrameSlotKind.Argument,
                                RegisterFrameBase.StackPointer,
                                homeSlot.Index,
                                checked(homeSlot.Offset + fieldOffset),
                                fieldSize);

                            return true;
                        }

                        return false;
                    }

                    if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        int aggregateStackSlot = -1;
                        int aggregateStackBaseOffset = 0;
                        var segments = MachineAbi.GetRegisterSegments(abi, Target);
                        for (int s = 0; s < segments.Length; s++)
                        {
                            var segment = segments[s];
                            var location = MachineAbi.AssignAggregateSegmentArgumentLocation(
                                segment,
                                ref generalArgumentIndex,
                                ref floatArgumentIndex,
                                ref incomingStackArgumentIndex,
                                ref aggregateStackSlot,
                                ref aggregateStackBaseOffset,
                                Target);

                            if (i != parentArgumentIndex)
                                continue;

                            int segmentStart = segment.Offset;
                            int segmentEnd = segment.Offset + Math.Max(1, segment.Size);
                            int fieldEnd = fieldOffset + fieldSize;
                            if (fieldOffset < segmentStart || fieldEnd > segmentEnd)
                                continue;

                            if (location.IsRegister)
                            {
                                if (fieldOffset == segment.Offset && fieldSize == Math.Max(1, segment.Size))
                                {
                                    source = OperandForIncomingAbiLocation(location);
                                    return true;
                                }

                                if (_method.StackFrame.TryGetArgumentSlot(parentArgumentIndex, out StackFrameSlot homeSlot))
                                {
                                    source = RegisterOperand.ForFrameSlot(
                                        fieldRegisterClass == RegisterClass.Invalid ? segment.RegisterClass : fieldRegisterClass,
                                        StackFrameSlotKind.Argument,
                                        RegisterFrameBase.StackPointer,
                                        homeSlot.Index,
                                        checked(homeSlot.Offset + fieldOffset),
                                        fieldSize);

                                    return true;
                                }

                                return false;
                            }

                            var adjusted = AbiArgumentLocation.ForStack(
                                fieldRegisterClass == RegisterClass.Invalid ? segment.RegisterClass : fieldRegisterClass,
                                location.StackSlotIndex,
                                checked(location.StackOffset + (fieldOffset - segment.Offset)),
                                fieldSize);
                            source = OperandForIncomingAbiLocation(adjusted);
                            return true;
                        }

                        if (i == parentArgumentIndex)
                            return false;
                        continue;
                    }

                    int stackSize = abi.Size <= 0 ? Target.PointerSize : abi.Size;
                    int stackSlot = incomingStackArgumentIndex;
                    if (i != parentArgumentIndex)
                    {
                        incomingStackArgumentIndex = checked(incomingStackArgumentIndex + MachineAbi.StackSlotsForArgumentSize(stackSize, Target));
                        continue;
                    }

                    var stackLocation = AbiArgumentLocation.ForStack(
                        fieldRegisterClass == RegisterClass.Invalid ? RegisterClass.General : fieldRegisterClass,
                        stackSlot,
                        fieldOffset,
                        fieldSize);

                    source = OperandForIncomingAbiLocation(stackLocation);
                    return true;
                }

                return false;
            }

            private ImmutableArray<RegisterOperand> GetIncomingArgumentOperands(
                int argumentIndex,
                RuntimeType? argumentType,
                GenStackKind stackKind,
                RegisterClass argumentClass,
                AbiValueInfo argumentAbi)
            {
                int generalArgumentIndex = 0;
                int floatArgumentIndex = 0;
                int incomingStackArgumentIndex = RegisterInfo.MinimumOutgoingArgumentSlots(Target);
                int hiddenReturnBufferIndex = MachineAbi.HiddenReturnBufferInsertionIndex(
                    _method.GenTreeMethod.RuntimeMethod,
                    _method.GenTreeMethod.ArgTypes.Length,
                    Target);

                for (int i = 0; i <= argumentIndex; i++)
                {
                    if (hiddenReturnBufferIndex == i)
                        ConsumeIncomingHiddenReturnBuffer(ref generalArgumentIndex, ref floatArgumentIndex, ref incomingStackArgumentIndex);

                    RuntimeType currentType = _method.GenTreeMethod.ArgTypes[i];
                    GenStackKind currentStackKind = i == argumentIndex ? stackKind : MachineAbi.StackKindForType(currentType);
                    var valueAbi = i == argumentIndex
                        ? argumentAbi
                        : MachineAbi.ClassifyValue(currentType, currentStackKind, isReturn: false, target: Target);
                    var abi = MachineAbi.AdjustArgumentAbiForRegisterAvailability(
                        valueAbi,
                        generalArgumentIndex,
                        floatArgumentIndex,
                        Target);

                    if (i == argumentIndex && !MachineAbi.HaveMatchingArgumentValueLayout(valueAbi, abi, Target))
                        return GetIncomingArgumentHomeOperands(argumentIndex, valueAbi);

                    if (abi.PassingKind == AbiValuePassingKind.Void)
                        continue;

                    if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                    {
                        var registerClass = abi.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : abi.RegisterClass;
                        var source = MachineAbi.AssignScalarArgumentLocation(
                            registerClass,
                            abi.Size <= 0 ? Target.PointerSize : abi.Size,
                            ref generalArgumentIndex,
                            ref floatArgumentIndex,
                            ref incomingStackArgumentIndex,
                            Target);

                        if (i == argumentIndex)
                            return ImmutableArray.Create(OperandForIncomingAbiLocation(source));
                        continue;
                    }

                    if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        int aggregateStackSlot = -1;
                        int aggregateStackBaseOffset = 0;
                        var segments = MachineAbi.GetRegisterSegments(abi, Target);
                        var operands = i == argumentIndex
                            ? ImmutableArray.CreateBuilder<RegisterOperand>(segments.Length)
                            : null;

                        for (int s = 0; s < segments.Length; s++)
                        {
                            var source = MachineAbi.AssignAggregateSegmentArgumentLocation(
                                segments[s],
                                ref generalArgumentIndex,
                                ref floatArgumentIndex,
                                ref incomingStackArgumentIndex,
                                ref aggregateStackSlot,
                                ref aggregateStackBaseOffset,
                                Target);

                            operands?.Add(OperandForIncomingAbiLocation(source));
                        }

                        if (i == argumentIndex)
                            return operands!.ToImmutable();
                        continue;
                    }

                    int stackSize = abi.Size <= 0 ? Target.PointerSize : abi.Size;
                    int stackSlot = incomingStackArgumentIndex;
                    incomingStackArgumentIndex = checked(incomingStackArgumentIndex + MachineAbi.StackSlotsForArgumentSize(stackSize, Target));
                    var stackSource = AbiArgumentLocation.ForStack(
                        argumentClass == RegisterClass.Invalid ? RegisterClass.General : argumentClass,
                        stackSlot,
                        0,
                        stackSize);

                    if (i == argumentIndex)
                        return ImmutableArray.Create(OperandForIncomingAbiLocation(stackSource));
                }

                if (hiddenReturnBufferIndex == argumentIndex + 1)
                    ConsumeIncomingHiddenReturnBuffer(ref generalArgumentIndex, ref floatArgumentIndex, ref incomingStackArgumentIndex);

                throw new InvalidOperationException("Invalid initial SSA argument index " + argumentIndex.ToString() + ".");
            }

            private void EmitIncomingHiddenReturnBufferHomeStore(
                int blockId,
                ImmutableArray<GenTree>.Builder nodes,
                int homeIndex,
                ref int generalArgumentIndex,
                ref int floatArgumentIndex,
                ref int incomingStackArgumentIndex)
            {
                var source = MachineAbi.AssignScalarArgumentLocation(
                    RegisterClass.General,
                    Target.PointerSize,
                    ref generalArgumentIndex,
                    ref floatArgumentIndex,
                    ref incomingStackArgumentIndex,
                    Target);

                if (_method.StackFrame.TryGetArgumentSlot(homeIndex, out StackFrameSlot homeSlot))
                    EmitIncomingArgumentHomeStore(blockId, nodes, homeSlot, source, 0, RegisterClass.General, Target.PointerSize);
            }

            private void EmitIncomingArgumentHomeStore(
                int blockId,
                ImmutableArray<GenTree>.Builder nodes,
                StackFrameSlot homeSlot,
                AbiArgumentLocation sourceLocation,
                int destinationOffset,
                RegisterClass registerClass,
                int size)
            {
                int actualSize = Math.Max(1, size);
                var destination = RegisterOperand.ForFrameSlot(
                    registerClass == RegisterClass.Invalid ? RegisterClass.General : registerClass,
                    StackFrameSlotKind.Argument,
                    RegisterFrameBase.StackPointer,
                    homeSlot.Index,
                    checked(homeSlot.Offset + destinationOffset),
                    actualSize);

                var source = OperandForIncomingAbiLocation(sourceLocation);
                if (destination.Equals(source))
                    return;

                if (destination.IsMemoryOperand && source.IsMemoryOperand)
                {
                    if (Target.IsX86 && actualSize <= Target.GeneralRegisterSize)
                    {
                        nodes.Add(GenTreeLirFactory.Move(
                            _nextNodeId++,
                            blockId,
                            nodes.Count,
                            destination,
                            source,
                            destinationValue: null,
                            sourceValue: null,
                            comment: "prolog: home incoming argument",
                            moveFlags: MoveFlags.AbiArgument | MoveFlags.Internal));
                        return;
                    }

                    if (actualSize > Target.GeneralRegisterSize)
                    {
                        EmitIncomingArgumentHomeBlockStore(blockId, nodes, destination, source, actualSize);
                        return;
                    }
                    var scratch = RegisterOperand.ForRegister(SelectInitialSsaArgumentCopyScratch(destination, source));
                    nodes.Add(GenTreeLirFactory.Move(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        scratch,
                        source,
                        destinationValue: null,
                        sourceValue: null,
                        comment: "prolog: home incoming argument reload",
                        moveFlags: MoveFlags.AbiArgument | MoveFlags.Internal | MoveFlags.Reload));

                    nodes.Add(GenTreeLirFactory.Move(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        destination,
                        scratch,
                        destinationValue: null,
                        sourceValue: null,
                        comment: "prolog: home incoming argument store",
                        moveFlags: MoveFlags.AbiArgument | MoveFlags.Internal | MoveFlags.Spill));
                    return;
                }

                nodes.Add(GenTreeLirFactory.Move(
                    _nextNodeId++,
                    blockId,
                    nodes.Count,
                    destination,
                    source,
                    destinationValue: null,
                    sourceValue: null,
                    comment: "prolog: home incoming argument",
                    moveFlags: MoveFlags.AbiArgument | MoveFlags.Internal));
            }
            private void EmitIncomingArgumentHomeBlockStore(
                int blockId,
                ImmutableArray<GenTree>.Builder nodes,
                RegisterOperand destination,
                RegisterOperand source,
                int size)
            {
                int offset = 0;
                while (offset < size)
                {
                    int remaining = size - offset;
                    int chunkSize = remaining >= 8 ? 8 : remaining >= 4 ? 4 : remaining >= 2 ? 2 : 1;
                    var chunkSource = SliceFrameOperand(source, offset, chunkSize);
                    var chunkDestination = SliceFrameOperand(destination, offset, chunkSize);
                    if (Target.IsX86)
                    {
                        nodes.Add(GenTreeLirFactory.Move(
                            _nextNodeId++,
                            blockId,
                            nodes.Count,
                            chunkDestination,
                            chunkSource,
                            destinationValue: null,
                            sourceValue: null,
                            comment: "prolog: home incoming block argument",
                            moveFlags: MoveFlags.AbiArgument | MoveFlags.Internal));
                        offset = checked(offset + chunkSize);
                        continue;
                    }

                    var scratch = RegisterOperand.ForRegister(RegisterInfo.ParallelCopyScratch(Target, RegisterClass.General));

                    nodes.Add(GenTreeLirFactory.Move(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        scratch,
                        chunkSource,
                        destinationValue: null,
                        sourceValue: null,
                        comment: "prolog: home incoming argument block reload",
                        moveFlags: MoveFlags.AbiArgument | MoveFlags.Internal | MoveFlags.Reload));

                    nodes.Add(GenTreeLirFactory.Move(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                       chunkDestination,
                        scratch,
                        destinationValue: null,
                        sourceValue: null,
                        comment: "prolog: home incoming argument block store",
                        moveFlags: MoveFlags.AbiArgument | MoveFlags.Internal | MoveFlags.Spill));

                    offset += chunkSize;
                }
            }

            private static RegisterOperand SliceFrameOperand(RegisterOperand operand, int offset, int size)
            {
                if (!operand.IsFrameSlot)
                    throw new InvalidOperationException("Incoming argument block copy requires finalized frame operands.");

                return RegisterOperand.ForFrameSlot(
                    RegisterClass.General,
                    operand.FrameSlotKind,
                    operand.FrameBase,
                    operand.FrameSlotIndex,
                    checked(operand.FrameOffset + offset),
                    size);
            }
            private ImmutableArray<RegisterOperand> GetIncomingArgumentHomeOperands(int argumentIndex, AbiValueInfo valueAbi)
            {
                if (!_method.StackFrame.TryGetArgumentSlot(argumentIndex, out StackFrameSlot homeSlot))
                    throw new InvalidOperationException("Incoming argument ABI adaptation requires an argument home slot.");

                if (valueAbi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    var segments = MachineAbi.GetRegisterSegments(valueAbi, Target);
                    var operands = ImmutableArray.CreateBuilder<RegisterOperand>(segments.Length);
                    for (int i = 0; i < segments.Length; i++)
                    {
                        var segment = segments[i];
                        operands.Add(RegisterOperand.ForFrameSlot(
                            segment.RegisterClass,
                            StackFrameSlotKind.Argument,
                            RegisterFrameBase.StackPointer,
                            homeSlot.Index,
                            checked(homeSlot.Offset + segment.Offset),
                            segment.Size));
                    }
                    return operands.ToImmutable();
                }

                int size = Math.Max(1, valueAbi.Size <= 0 ? Target.PointerSize : valueAbi.Size);
                var registerClass = valueAbi.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : valueAbi.RegisterClass;
                return ImmutableArray.Create(RegisterOperand.ForFrameSlot(
                    registerClass,
                    StackFrameSlotKind.Argument,
                    RegisterFrameBase.StackPointer,
                    homeSlot.Index,
                    homeSlot.Offset,
                    size));
            }

            private RegisterOperand OperandForIncomingAbiLocation(AbiArgumentLocation location)
            {
                if (location.IsRegister)
                    return RegisterOperand.ForRegister(location.Register);

                return RegisterOperand.ForFrameSlot(
                    location.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : location.RegisterClass,
                    StackFrameSlotKind.Argument,
                    RegisterFrameBase.IncomingArgumentBase,
                    location.StackSlotIndex,
                    checked(location.StackSlotIndex * Target.StackSlotSize + location.StackOffset),
                    Math.Max(1, location.Size));
            }

            private void ConsumeIncomingHiddenReturnBuffer(
                ref int generalArgumentIndex,
                ref int floatArgumentIndex,
                ref int incomingStackArgumentIndex)
            {
                _ = MachineAbi.AssignScalarArgumentLocation(
                    RegisterClass.General,
                    Target.PointerSize,
                    ref generalArgumentIndex,
                    ref floatArgumentIndex,
                    ref incomingStackArgumentIndex,
                    Target);
            }
            private GenTree NormalizeReturnOperand(
                int blockId,
                GenTree returnNode,
                ImmutableArray<GenTree>.Builder nodes)
            {
                if (returnNode.Uses.Length == 0)
                    return returnNode;

                if (returnNode.RegisterUses.Length == 0)
                    throw new InvalidOperationException("Register return node with operands must retain linear IR return value mapping.");

                var returnValue = returnNode.RegisterUses[0];
                var valueInfo = _method.GenTreeMethod.GetValueInfo(returnValue);
                var abi = MachineAbi.ClassifyValue(valueInfo.Type, valueInfo.StackKind, isReturn: true, target: Target);

                if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    var segments = MachineAbi.GetRegisterSegments(abi, Target);
                    if (returnNode.Uses.Length != segments.Length || returnNode.RegisterUses.Length != segments.Length)
                        throw new InvalidOperationException("Multi-register return node must contain one use per ABI return fragment.");

                    var normalizedUses = ImmutableArray.CreateBuilder<RegisterOperand>(segments.Length);
                    int generalReturnIndex = 0;
                    int floatReturnIndex = 0;
                    for (int i = 0; i < segments.Length; i++)
                    {
                        if (!returnNode.RegisterUses[i].Equals(returnValue))
                            throw new InvalidOperationException("Multi-register return fragments must all map to the same GenTree value.");

                        var target = RegisterOperand.ForRegister(
                            GetSegmentReturnRegister(segments[i].RegisterClass, ref generalReturnIndex, ref floatReturnIndex));
                        var source = returnNode.Uses[i];
                        if (!source.Equals(target))
                        {
                            nodes.Add(GenTreeLirFactory.Move(
                                _nextNodeId++,
                                blockId,
                                nodes.Count,
                                target,
                                source,
                                destinationValue: returnValue,
                                sourceValue: returnValue,
                                comment: "return struct fragment to ABI register",
                                moveFlags: MoveFlags.AbiReturn));
                        }
                        normalizedUses.Add(target);
                    }

                    return returnNode.WithOperands(
                        returnNode.Results,
                        normalizedUses.ToImmutable(),
                        returnNode.RegisterResults,
                        returnNode.RegisterUses);
                }

                if (abi.PassingKind != AbiValuePassingKind.ScalarRegister)
                {
                    return returnNode;
                }

                if (returnNode.Uses.Length != 1 || returnNode.RegisterUses.Length != 1)
                    throw new InvalidOperationException("Scalar register return node must have exactly one value use.");

                var returnRegister = abi.RegisterClass == RegisterClass.Float
                    ? RegisterInfo.GetFloatReturnRegister(Target, 0)
                    : RegisterInfo.GetIntegerReturnRegister(Target, 0);
                var returnOperand = RegisterOperand.ForRegister(returnRegister);
                var sourceOperand = returnNode.Uses[0];

                if (!sourceOperand.Equals(returnOperand))
                {
                    nodes.Add(GenTreeLirFactory.Move(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        returnOperand,
                        sourceOperand,
                        destinationValue: returnValue,
                        sourceValue: returnValue,
                        comment: "return value to ABI register",
                        moveFlags: MoveFlags.AbiReturn));
                }

                return returnNode.WithOperands(
                    returnNode.Results,
                    ImmutableArray.Create(returnOperand),
                    returnNode.RegisterResults,
                    returnNode.RegisterUses);
            }

            private MachineRegister GetSegmentReturnRegister(RegisterClass registerClass, ref int generalIndex, ref int floatIndex)
            {
                if (registerClass == RegisterClass.Float)
                {
                    int index = floatIndex++;
                    var register = RegisterInfo.GetFloatReturnRegister(Target, index);
                    if (register == MachineRegister.Invalid)
                        throw new InvalidOperationException("Not enough float return registers for aggregate return.");
                    return register;
                }

                if (registerClass == RegisterClass.General)
                {
                    int index = generalIndex++;
                    var register = RegisterInfo.GetIntegerReturnRegister(Target, index);
                    if (register == MachineRegister.Invalid)
                        throw new InvalidOperationException("Not enough integer return registers for aggregate return.");
                    return register;
                }

                throw new InvalidOperationException($"Invalid return fragment register class {registerClass}.");
            }

            private void AppendEpilog(int blockId, int funcletIndex, ImmutableArray<GenTree>.Builder nodes)
            {
                int firstNodeId = _nextNodeId;
                if (funcletIndex != 0)
                {
                    nodes.Add(GenTreeLirFactory.Frame(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        FrameOperation.LeaveFuncletFrame,
                        RegisterOperand.ForRegister(RegisterInfo.StackPointer(Target)),
                        _method.StackFrame.UsesFramePointer
                            ? ImmutableArray.Create(RegisterOperand.ForRegister(RegisterInfo.FramePointer(Target)))
                            : ImmutableArray.Create(RegisterOperand.ForRegister(RegisterInfo.StackPointer(Target))),
                        immediate: 0,
                        comment: "funclet epilog: detach from establisher frame"));

                    _frameRegions.Add(new RegisterFrameRegion(
                        RegisterFrameRegionKind.Epilog,
                        funcletIndex,
                        blockId,
                        firstNodeId,
                        _nextNodeId - 1));
                    return;
                }

                int frameSize = _method.StackFrame.FrameSize;
                if (_method.StackFrame.UsesFramePointer)
                {
                    nodes.Add(GenTreeLirFactory.Frame(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        FrameOperation.RestoreStackPointerFromFramePointer,
                        RegisterOperand.ForRegister(RegisterInfo.StackPointer(Target)),
                        ImmutableArray.Create(RegisterOperand.ForRegister(RegisterInfo.FramePointer(Target))),
                        immediate: 0,
                        comment: "epilog: sp = fp"));
                }

                for (int i = _method.StackFrame.CalleeSavedSlots.Length - 1; i >= 0; i--)
                {
                    var slot = _method.StackFrame.CalleeSavedSlots[i];
                    bool isReturnAddress = slot.Kind == StackFrameSlotKind.ReturnAddress;
                    nodes.Add(GenTreeLirFactory.Frame(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        isReturnAddress ? FrameOperation.RestoreReturnAddress : FrameOperation.RestoreCalleeSavedRegister,
                        RegisterOperand.ForRegister(slot.SavedRegister),
                        ImmutableArray.Create(FrameOperand(slot, RegisterFrameBase.StackPointer)),
                        immediate: 0,
                        comment: "epilog: restore " + MachineRegisters.Format(slot.SavedRegister)));
                }

                if (frameSize > 0)
                {
                    nodes.Add(GenTreeLirFactory.Frame(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        FrameOperation.FreeFrame,
                        RegisterOperand.ForRegister(RegisterInfo.StackPointer(Target)),
                        ImmutableArray.Create(RegisterOperand.ForRegister(RegisterInfo.StackPointer(Target))),
                        frameSize,
                        "epilog: sp += frameSize"));
                }

                if (_nextNodeId > firstNodeId)
                {
                    _frameRegions.Add(new RegisterFrameRegion(
                        RegisterFrameRegionKind.Epilog,
                        funcletIndex,
                        blockId,
                        firstNodeId,
                        _nextNodeId - 1));
                }
            }

            private static RegisterOperand FrameOperand(StackFrameSlot slot, RegisterFrameBase frameBase)
                => RegisterOperand.ForFrameSlot(
                    slot.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : slot.RegisterClass,
                    slot.Kind,
                    frameBase,
                    slot.Index,
                    slot.Offset,
                    slot.Size);

            private bool NeedsFuncletProlog(int blockId)
            {
                if (blockId == 0)
                    return true;

                for (int i = 0; i < _method.Funclets.Length; i++)
                {
                    var funclet = _method.Funclets[i];
                    if (!funclet.IsRoot && funclet.EntryBlockId == blockId)
                        return true;
                }

                return false;
            }

            private int FuncletIndexForEntryBlock(int blockId)
            {
                for (int i = 0; i < _method.Funclets.Length; i++)
                {
                    if (_method.Funclets[i].EntryBlockId == blockId)
                        return _method.Funclets[i].Index;
                }
                return 0;
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

            private bool ReturnMustRunFinallyBeforeMethodExit(int blockId)
            {
                var regions = _method.GenTreeMethod.Cfg.ExceptionRegions;
                for (int i = 0; i < regions.Length; i++)
                {
                    var region = regions[i];
                    if (region.Kind == CfgExceptionRegionKind.Finally &&
                        blockId >= region.TryStartBlockId &&
                        blockId < region.TryEndBlockIdExclusive)
                    {
                        return true;
                    }
                }
                return false;
            }

            private static ImmutableArray<GenTree> NormalizeOrdinals(ImmutableArray<GenTree> nodes)
            {
                var result = ImmutableArray.CreateBuilder<GenTree>(nodes.Length);
                for (int i = 0; i < nodes.Length; i++)
                    result.Add(nodes[i].WithOrdinal(i));
                return result.ToImmutable();
            }

            private int EstimateFrameNodeCount()
            {
                int result = EstimatePrologNodeCount();
                for (int i = 0; i < _method.Blocks.Length; i++)
                    result += EstimateBlockEpilogNodeCount(_method.Blocks[i]);
                return result;
            }

            private int EstimatePrologNodeCount()
            {
                int result = _method.StackFrame.CalleeSavedSlots.Length;
                if (_method.StackFrame.FrameSize > 0)
                    result += 2;
                result += EstimateGcHomeSlotZeroInitNodeCount();
                return result;
            }

            private int EstimateGcHomeSlotZeroInitNodeCount()
            {
                int result = 0;
                for (int i = 0; i < _method.StackFrame.LocalSlots.Length; i++)
                    if (NeedsHomeGcReporting(_method.StackFrame.LocalSlots[i].Type, StackKindForType(_method.StackFrame.LocalSlots[i].Type)))
                        result += Math.Max(1, (_method.StackFrame.LocalSlots[i].Size + 7) / 8);
                for (int i = 0; i < _method.StackFrame.TempSlots.Length; i++)
                    if (NeedsHomeGcReporting(_method.StackFrame.TempSlots[i].Type, StackKindForType(_method.StackFrame.TempSlots[i].Type)))
                        result += Math.Max(1, (_method.StackFrame.TempSlots[i].Size + 7) / 8);
                return result;
            }

            private int EstimateBlockEpilogNodeCount(GenTreeBlock block)
            {
                int perReturn = _method.StackFrame.CalleeSavedSlots.Length;
                if (_method.StackFrame.FrameSize > 0)
                    perReturn += 2;

                int result = 0;
                for (int i = 0; i < block.LinearNodes.Length; i++)
                {
                    if (block.LinearNodes[i].Kind == GenTreeKind.Return)
                    {
                        result += perReturn;
                        if (block.LinearNodes[i].Uses.Length != 0)
                            result += 1;
                    }
                    else if (block.LinearNodes[i].Kind == GenTreeKind.EndFinally ||
                        (block.LinearNodes[i].Kind == GenTreeKind.Branch &&
                            block.LinearNodes[i].SourceOp == BytecodeOp.Leave &&
                            FuncletIndexForBlock(block.Id) != 0))
                    {
                        result += perReturn;
                    }
                }
                return result;
            }
        }
    }
}
