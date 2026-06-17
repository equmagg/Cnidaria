using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;

namespace Cnidaria.Cs
{
    internal static class RegisterStackLayoutFinalizer
    {
        public static RegisterAllocatedMethod FinalizeMethod(RegisterAllocatedMethod method, RegisterStackLayoutOptions? options = null)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            if (!method.StackFrame.IsEmpty)
                return method;

            options ??= RegisterStackLayoutOptions.Default;
            var builder = new MethodBuilder(method, options);
            return builder.Run();
        }

        private readonly struct StorageInfo
        {
            public readonly int Size;
            public readonly int Alignment;

            public StorageInfo(int size, int alignment)
            {
                Size = size;
                Alignment = alignment;
            }
        }

        private sealed class SpillSpec
        {
            public int Index { get; }
            public RegisterClass RegisterClass { get; private set; }
            public int Size { get; private set; }
            public int Alignment { get; private set; }
            public bool IsParallelCopyScratch { get; private set; }

            public SpillSpec(int index)
            {
                Index = index;
                RegisterClass = RegisterClass.Invalid;
                Size = 0;
                Alignment = 1;
            }

            public void Merge(RegisterClass registerClass, StorageInfo storage, bool isParallelCopyScratch)
            {
                if (registerClass == RegisterClass.Invalid)
                    throw new InvalidOperationException($"Spill slot {Index} has invalid register class.");

                if (RegisterClass == RegisterClass.Invalid)
                    RegisterClass = registerClass;
                else if (RegisterClass != registerClass)
                    RegisterClass = RegisterClass.General;

                if (storage.Size > Size)
                    Size = storage.Size;
                if (storage.Alignment > Alignment)
                    Alignment = storage.Alignment;
                IsParallelCopyScratch |= isParallelCopyScratch;
            }
        }

        private sealed class OutgoingArgumentSpec
        {
            public int Index { get; }
            public RegisterClass RegisterClass { get; private set; }
            public int Size { get; private set; }
            public int Alignment { get; private set; }

            public OutgoingArgumentSpec(int index)
            {
                Index = index;
                RegisterClass = RegisterClass.Invalid;
                Size = 0;
                Alignment = 1;
            }

            public void Merge(RegisterClass registerClass, StorageInfo storage)
            {
                if (registerClass == RegisterClass.Invalid)
                    throw new InvalidOperationException($"Outgoing argument slot {Index} has invalid register class.");

                if (RegisterClass == RegisterClass.Invalid)
                    RegisterClass = registerClass;
                else if (RegisterClass != registerClass)
                    RegisterClass = RegisterClass.General;

                if (storage.Size > Size)
                    Size = storage.Size;
                if (storage.Alignment > Alignment)
                    Alignment = storage.Alignment;
            }
        }

        private sealed class MethodBuilder
        {
            private readonly RegisterAllocatedMethod _method;
            private readonly RegisterStackLayoutOptions _options;
            private readonly Dictionary<int, SpillSpec> _spillSpecs = new();
            private readonly Dictionary<int, OutgoingArgumentSpec> _outgoingArgumentSpecs = new();
            private readonly Dictionary<int, StackFrameSlot> _spillSlots = new();
            private readonly HashSet<int> _explicitLocalSlots = new();
            private readonly HashSet<int> _explicitTempSlots = new();
            private StackFrameLayout _layout = StackFrameLayout.Empty;

            public MethodBuilder(RegisterAllocatedMethod method, RegisterStackLayoutOptions options)
            {
                _method = method;
                _options = options;
            }

            public RegisterAllocatedMethod Run()
            {
                CollectSpillSlotsFromAllocations();
                CollectSpillSlotsFromLinearNodes();
                CollectExplicitUserSlotsFromAllocations();
                CollectExplicitUserSlotsFromLinearNodes();
                CollectOutgoingArgumentSlotsFromLinearNodes();
                _layout = BuildLayout();

                var blocks = RewriteBlocks(out var allNodes);
                var allocations = RewriteAllocations(out var allocationByNode);

                return new RegisterAllocatedMethod(
                    _method.GenTreeMethod,
                    blocks,
                    allNodes,
                    allocations,
                    allocationByNode,
                    _method.InternalRegistersByNodeId,
                    _method.SpillSlotCount,
                    _method.ParallelCopyScratchSpillSlot,
                    _layout,
                    _method.HasPrologEpilog,
                    _method.UnwindCodes,
                    _method.GcLiveRanges,
                    _method.GcTransitions,
                    _method.GcInterruptibleRanges,
                    _method.Funclets,
                    _method.FrameRegions,
                    _method.GcReportOnlyLeafFunclet,
                    lsraNodePositions: _method.LsraNodePositions,
                    lsraBlockStartPositions: _method.LsraBlockStartPositions,
                    lsraBlockEndPositions: _method.LsraBlockEndPositions);
            }

            private void CollectSpillSlotsFromAllocations()
            {
                for (int i = 0; i < _method.Allocations.Length; i++)
                {
                    var allocation = _method.Allocations[i];
                    var valueInfo = _method.GenTreeMethod.GetValueInfo(allocation.Value);
                    var storage = StorageForValue(valueInfo);

                    if (allocation.Home.IsSpillSlot)
                        AddSpillSpec(allocation.Home.SpillSlot, allocation.Home.RegisterClass, storage);

                    for (int s = 0; s < allocation.Segments.Length; s++)
                    {
                        var location = allocation.Segments[s].Location;
                        if (location.IsSpillSlot)
                            AddSpillSpec(location.SpillSlot, location.RegisterClass, storage);
                    }

                    for (int f = 0; f < allocation.Fragments.Length; f++)
                    {
                        var fragment = allocation.Fragments[f];
                        var fragmentStorage = StorageForAbiSegment(fragment.AbiSegment);
                        if (fragment.Home.IsSpillSlot)
                            AddSpillSpec(fragment.Home.SpillSlot, fragment.Home.RegisterClass, fragmentStorage);

                        for (int s = 0; s < fragment.Segments.Length; s++)
                        {
                            var location = fragment.Segments[s].Location;
                            if (location.IsSpillSlot)
                                AddSpillSpec(location.SpillSlot, location.RegisterClass, fragmentStorage);
                        }
                    }
                }
            }

            private void CollectSpillSlotsFromLinearNodes()
            {
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var nodes = _method.Blocks[b].LinearNodes;
                    for (int i = 0; i < nodes.Length; i++)
                        CollectSpillSlotsFromNode(nodes[i]);
                }
            }

            private void CollectExplicitUserSlotsFromAllocations()
            {
                for (int i = 0; i < _method.Allocations.Length; i++)
                {
                    var allocation = _method.Allocations[i];
                    CollectExplicitUserSlotFromOperand(allocation.Home);

                    for (int s = 0; s < allocation.Segments.Length; s++)
                        CollectExplicitUserSlotFromOperand(allocation.Segments[s].Location);

                    for (int f = 0; f < allocation.Fragments.Length; f++)
                    {
                        var fragment = allocation.Fragments[f];
                        CollectExplicitUserSlotFromOperand(fragment.Home);

                        for (int s = 0; s < fragment.Segments.Length; s++)
                            CollectExplicitUserSlotFromOperand(fragment.Segments[s].Location);
                    }
                }
            }

            private void CollectExplicitUserSlotsFromLinearNodes()
            {
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var nodes = _method.Blocks[b].LinearNodes;
                    for (int i = 0; i < nodes.Length; i++)
                        CollectExplicitUserSlotsFromNode(nodes[i]);
                }
            }

            private void CollectExplicitUserSlotsFromNode(GenTree node)
            {
                CollectExplicitUserSlotFromLocalLikeNode(node);

                for (int i = 0; i < node.Results.Length; i++)
                    CollectExplicitUserSlotFromOperand(node.Results[i]);

                for (int i = 0; i < node.Uses.Length; i++)
                    CollectExplicitUserSlotFromOperand(node.Uses[i]);
            }

            private void CollectExplicitUserSlotFromLocalLikeNode(GenTree node)
            {
                if (node.LocalDescriptor is { IsStructField: true })
                    return;

                switch (node.Kind)
                {
                    case GenTreeKind.Local:
                    case GenTreeKind.StoreLocal:
                        if ((uint)node.Int32 < (uint)_method.GenTreeMethod.LocalTypes.Length &&
                            SurvivingLocalLikeNodeRequiresHome(node))
                        {
                            _explicitLocalSlots.Add(node.Int32);
                        }
                        return;

                    case GenTreeKind.LocalAddr:
                        if ((uint)node.Int32 < (uint)_method.GenTreeMethod.LocalTypes.Length)
                            _explicitLocalSlots.Add(node.Int32);
                        return;

                    case GenTreeKind.Temp:
                    case GenTreeKind.StoreTemp:
                        if (ContainsTempIndex(_method.GenTreeMethod.Temps, node.Int32) &&
                            SurvivingLocalLikeNodeRequiresHome(node))
                        {
                            _explicitTempSlots.Add(node.Int32);
                        }
                        return;

                    case GenTreeKind.TempAddr:
                        if (ContainsTempIndex(_method.GenTreeMethod.Temps, node.Int32))
                            _explicitTempSlots.Add(node.Int32);
                        return;
                }
            }

            private static bool SurvivingLocalLikeNodeRequiresHome(GenTree node)
            {
                var descriptor = node.LocalDescriptor;
                if (descriptor is null)
                    return true;

                if (descriptor.IsStructField)
                    return false;

                // A promoted aggregate parent normally has no physical home. If a full-width
                // Local/Temp or StoreLocal/StoreTemp survived promotion, CodeGen will materialize it
                // through the parent frame slot, so the slot must exist even though field locals may
                // also be promoted independently.
                if (descriptor.Category == GenLocalCategory.PromotedStruct &&
                    descriptor.HasPromotedStructFields &&
                    !descriptor.AddressExposed &&
                    !descriptor.MemoryAliased)
                {
                    return true;
                }

                if (descriptor.AddressExposed || descriptor.MemoryAliased || descriptor.DoNotEnregister || !descriptor.SsaPromoted)
                    return true;

                var type = descriptor.Type ?? node.RuntimeType ?? node.Type;
                var stackKind = descriptor.StackKind == GenStackKind.Unknown ? node.StackKind : descriptor.StackKind;

                if (MachineAbi.RequiresStackHome(type, stackKind))
                    return true;

                if (type is not null && type.IsValueType && type.ContainsGcPointers)
                    return true;

                var abi = MachineAbi.ClassifyValue(type, stackKind, isReturn: false);
                return abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect or AbiValuePassingKind.MultiRegister;
            }

            private void CollectExplicitUserSlotFromOperand(RegisterOperand operand)
            {
                if (operand.IsLocalSlot)
                {
                    _explicitLocalSlots.Add(operand.FrameSlotIndex);
                    return;
                }

                if (operand.IsTempSlot)
                    _explicitTempSlots.Add(operand.FrameSlotIndex);
            }

            private void CollectSpillSlotsFromNode(GenTree node)
            {
                for (int i = 0; i < node.Results.Length; i++)
                {
                    GenTree? value = i < node.RegisterResults.Length ? node.RegisterResults[i] : null;
                    CollectSpillSlotFromOperand(node.Results[i], value);
                }

                for (int i = 0; i < node.Uses.Length; i++)
                {
                    GenTree? value = i < node.RegisterUses.Length ? node.RegisterUses[i] : null;
                    if (i < node.UseRoles.Length && node.UseRoles[i] == OperandRole.HiddenReturnBuffer)
                        value = null;
                    CollectSpillSlotFromOperand(node.Uses[i], value);
                }
            }

            private void CollectSpillSlotFromOperand(RegisterOperand operand, GenTree? value)
            {
                if (!operand.IsSpillSlot)
                    return;

                StorageInfo storage;
                if (operand.FrameSlotSize > 0)
                    storage = FragmentStorageForOperand(operand);
                else if (value is not null && _method.GenTreeMethod.ValueInfoByNode.TryGetValue(value.LinearValueKey, out var valueInfo))
                    storage = StorageForValue(valueInfo);
                else
                    storage = StorageForRegisterClass(operand.RegisterClass);

                bool isParallelCopyScratch = operand.SpillSlot == _method.ParallelCopyScratchSpillSlot;
                AddSpillSpec(operand.SpillSlot, operand.RegisterClass, storage, isParallelCopyScratch);
            }

            private void AddSpillSpec(int index, RegisterClass registerClass, StorageInfo storage, bool isParallelCopyScratch = false)
            {
                if (!_spillSpecs.TryGetValue(index, out var spec))
                {
                    spec = new SpillSpec(index);
                    _spillSpecs.Add(index, spec);
                }
                spec.Merge(registerClass, storage, isParallelCopyScratch);
            }

            private void CollectOutgoingArgumentSlotsFromLinearNodes()
            {
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var nodes = _method.Blocks[b].LinearNodes;
                    for (int i = 0; i < nodes.Length; i++)
                        CollectOutgoingArgumentSlotsFromNode(nodes[i]);
                }

                for (int i = 0; i < _options.OutgoingArgumentSlotCount; i++)
                    AddOutgoingArgumentSpec(i, RegisterClass.General, StorageForOutgoingArgumentSlot(RegisterClass.General, StorageForRegisterClass(RegisterClass.General)));
            }

            private void CollectOutgoingArgumentSlotsFromNode(GenTree node)
            {
                for (int i = 0; i < node.Results.Length; i++)
                {
                    GenTree? value = i < node.RegisterResults.Length ? node.RegisterResults[i] : null;
                    CollectOutgoingArgumentSlotFromOperand(node.Results[i], value);
                }

                for (int i = 0; i < node.Uses.Length; i++)
                {
                    GenTree? value = i < node.RegisterUses.Length ? node.RegisterUses[i] : null;
                    if (i < node.UseRoles.Length && node.UseRoles[i] == OperandRole.HiddenReturnBuffer)
                        value = null;
                    CollectOutgoingArgumentSlotFromOperand(node.Uses[i], value);
                }
            }

            private void CollectOutgoingArgumentSlotFromOperand(RegisterOperand operand, GenTree? value)
            {
                if (!operand.IsOutgoingArgumentSlot)
                    return;

                StorageInfo storage;
                if (operand.FrameSlotSize > 0)
                    storage = FragmentStorageForOperand(operand);
                else if (value is not null && _method.GenTreeMethod.ValueInfoByNode.TryGetValue(value.LinearValueKey, out var valueInfo))
                    storage = StorageForValue(valueInfo);
                else
                    storage = StorageForRegisterClass(operand.RegisterClass);

                AddOutgoingArgumentSpec(operand.FrameSlotIndex, operand.RegisterClass, StorageForOutgoingArgumentSlot(operand.RegisterClass, storage));
            }

            private void AddOutgoingArgumentSpec(int index, RegisterClass registerClass, StorageInfo storage)
            {
                if (index < 0)
                    throw new InvalidOperationException("Outgoing argument slot index must be non-negative.");

                if (!_outgoingArgumentSpecs.TryGetValue(index, out var spec))
                {
                    spec = new OutgoingArgumentSpec(index);
                    _outgoingArgumentSpecs.Add(index, spec);
                }
                spec.Merge(registerClass, storage);
            }

            private static StorageInfo FragmentStorageForOperand(RegisterOperand operand)
            {
                if (operand.FrameSlotSize <= 0)
                    return StorageForRegisterClass(operand.RegisterClass);

                var fallback = StorageForRegisterClass(operand.RegisterClass);
                int size = checked(operand.FrameOffset + operand.FrameSlotSize);
                int align = fallback.Alignment;

                return new StorageInfo(size, align);
            }


            private StackFrameLayout BuildLayout()
            {
                int cursor = 0;
                int frameAlignment = Math.Max(1, _options.FrameAlignment);
                if (!IsPowerOfTwo(frameAlignment))
                    throw new InvalidOperationException($"Frame alignment must be a power of two: {frameAlignment}.");

                bool usesFramePointer = ShouldUseFramePointer();
                bool saveReturnAddress = _options.SaveReturnAddressForLeafMethods || (_options.SaveReturnAddressForNonLeafMethods && MethodMayCall());

                var calleeSaved = ImmutableArray.CreateBuilder<StackFrameSlot>();
                int calleeSaveOffset = cursor;
                if (saveReturnAddress || _options.SaveUsedCalleeSavedRegisters || usesFramePointer)
                    AllocateCalleeSavedRegisterSlots(calleeSaved, ref cursor, usesFramePointer, saveReturnAddress);
                int calleeSaveSize = cursor - calleeSaveOffset;

                cursor = AlignUp(cursor, frameAlignment);
                int argHomeOffset = cursor;
                var argSlots = ImmutableArray.CreateBuilder<StackFrameSlot>();
                AllocateArgumentSlots(argSlots, ref cursor);
                int argHomeSize = cursor - argHomeOffset;

                cursor = AlignUp(cursor, frameAlignment);
                int localOffset = cursor;
                var localSlots = ImmutableArray.CreateBuilder<StackFrameSlot>();
                if (_options.AllocateLocalSlots)
                    AllocateLocalSlots(localSlots, ref cursor);
                int localSize = cursor - localOffset;

                cursor = AlignUp(cursor, frameAlignment);
                int tempOffset = cursor;
                var tempSlots = ImmutableArray.CreateBuilder<StackFrameSlot>();
                if (_options.AllocateTempSlots)
                    AllocateTempSlots(tempSlots, ref cursor);
                int tempSize = cursor - tempOffset;

                cursor = AlignUp(cursor, frameAlignment);
                int spillOffset = cursor;
                var spillSlots = ImmutableArray.CreateBuilder<StackFrameSlot>();
                AllocateSpillSlots(spillSlots, ref cursor);
                int spillSize = cursor - spillOffset;

                cursor = AlignUp(cursor, frameAlignment);
                int outgoingOffset = cursor;
                var outgoingSlots = ImmutableArray.CreateBuilder<StackFrameSlot>();
                AllocateOutgoingArgumentSlots(outgoingSlots, ref cursor);
                int outgoingSize = cursor - outgoingOffset;

                int frameSize = AlignUp(cursor, frameAlignment);

                return new StackFrameLayout(
                    frameSize,
                    frameAlignment,
                    calleeSaveOffset,
                    calleeSaveSize,
                    argHomeOffset,
                    argHomeSize,
                    localOffset,
                    localSize,
                    tempOffset,
                    tempSize,
                    spillOffset,
                    spillSize,
                    outgoingOffset,
                    outgoingSize,
                    argSlots.ToImmutable(),
                    localSlots.ToImmutable(),
                    tempSlots.ToImmutable(),
                    spillSlots.ToImmutable(),
                    calleeSaved.ToImmutable(),
                    outgoingSlots.ToImmutable(),
                    usesFramePointer,
                    SelectFrameModel(usesFramePointer, frameSize));
            }

            private RegisterStackFrameModel SelectFrameModel(bool usesFramePointer, int frameSize)
            {
                if (_method.Funclets.Length > 1)
                {
                    if (!usesFramePointer)
                        throw new InvalidOperationException("Funclet methods require a stable frame pointer for the shared establisher frame.");
                    return RegisterStackFrameModel.SharedRootFrameWithFunclets;
                }

                return frameSize == 0 ? RegisterStackFrameModel.Leaf : RegisterStackFrameModel.RootFrame;
            }

            private bool ShouldUseFramePointer()
            {
                if (_options.UseFramePointerForFunclets && _method.Funclets.Length > 1)
                    return true;

                if (_method.GenTreeMethod.Cfg.ExceptionRegions.Length != 0)
                    return true;

                if (!_options.SaveFramePointerWhenFrameIsUsed)
                    return false;

                return UsesSpecificCalleeSavedRegister(MachineRegisters.FramePointer);
            }

            private bool MethodMayCall()
            {
                for (int i = 0; i < _method.GenTreeMethod.LinearNodes.Length; i++)
                {
                    var node = _method.GenTreeMethod.LinearNodes[i];
                    if (node.LinearKind == GenTreeLinearKind.GcPoll || node.HasLoweringFlag(GenTreeLinearFlags.CallerSavedKill))
                        return true;
                }

                return false;
            }

            private void AllocateCalleeSavedRegisterSlots(
                ImmutableArray<StackFrameSlot>.Builder slots,
                ref int cursor,
                bool forceFramePointerSave,
                bool saveReturnAddress)
            {
                int index = 0;

                if (saveReturnAddress)
                    AllocateSavedRegisterSlot(slots, ref cursor, ref index, StackFrameSlotKind.ReturnAddress, MachineRegisters.ReturnAddress);

                var used = new SortedSet<MachineRegister>();
                if (forceFramePointerSave)
                    used.Add(MachineRegisters.FramePointer);
                if (_options.SaveUsedCalleeSavedRegisters)
                    CollectUsedCalleeSavedRegisters(used);

                foreach (var register in used)
                    AllocateSavedRegisterSlot(slots, ref cursor, ref index, StackFrameSlotKind.CalleeSavedRegister, register);
            }

            private bool UsesSpecificCalleeSavedRegister(MachineRegister register)
            {
                for (int i = 0; i < _method.Allocations.Length; i++)
                {
                    if (AllocationUsesRegister(_method.Allocations[i], register))
                        return true;
                }

                return false;
            }

            private void CollectUsedCalleeSavedRegisters(SortedSet<MachineRegister> used)
            {
                for (int i = 0; i < _method.Allocations.Length; i++)
                    CollectCalleeSavedRegisters(_method.Allocations[i], used);
            }

            private static bool AllocationUsesCalleeSavedRegister(RegisterAllocationInfo allocation)
            {
                if (IsCalleeSavedRegisterOperand(allocation.Home))
                    return true;

                for (int i = 0; i < allocation.Segments.Length; i++)
                {
                    if (IsCalleeSavedRegisterOperand(allocation.Segments[i].Location))
                        return true;
                }

                for (int f = 0; f < allocation.Fragments.Length; f++)
                {
                    var fragment = allocation.Fragments[f];
                    if (IsCalleeSavedRegisterOperand(fragment.Home))
                        return true;

                    for (int s = 0; s < fragment.Segments.Length; s++)
                    {
                        if (IsCalleeSavedRegisterOperand(fragment.Segments[s].Location))
                            return true;
                    }
                }

                return false;
            }

            private static bool AllocationUsesRegister(RegisterAllocationInfo allocation, MachineRegister register)
            {
                if (allocation.Home.IsRegister && allocation.Home.Register == register)
                    return true;

                for (int i = 0; i < allocation.Segments.Length; i++)
                {
                    var location = allocation.Segments[i].Location;
                    if (location.IsRegister && location.Register == register)
                        return true;
                }

                for (int f = 0; f < allocation.Fragments.Length; f++)
                {
                    var fragment = allocation.Fragments[f];
                    if (fragment.Home.IsRegister && fragment.Home.Register == register)
                        return true;

                    for (int s = 0; s < fragment.Segments.Length; s++)
                    {
                        var location = fragment.Segments[s].Location;
                        if (location.IsRegister && location.Register == register)
                            return true;
                    }
                }

                return false;
            }

            private static void CollectCalleeSavedRegisters(RegisterAllocationInfo allocation, SortedSet<MachineRegister> used)
            {
                AddCalleeSavedRegister(allocation.Home, used);

                for (int i = 0; i < allocation.Segments.Length; i++)
                    AddCalleeSavedRegister(allocation.Segments[i].Location, used);

                for (int f = 0; f < allocation.Fragments.Length; f++)
                {
                    var fragment = allocation.Fragments[f];
                    AddCalleeSavedRegister(fragment.Home, used);

                    for (int s = 0; s < fragment.Segments.Length; s++)
                        AddCalleeSavedRegister(fragment.Segments[s].Location, used);
                }
            }

            private static bool IsCalleeSavedRegisterOperand(RegisterOperand operand)
                => operand.IsRegister && MachineRegisters.IsCalleeSaved(operand.Register);

            private static void AddCalleeSavedRegister(RegisterOperand operand, SortedSet<MachineRegister> used)
            {
                if (IsCalleeSavedRegisterOperand(operand))
                    used.Add(operand.Register);
            }

            private static void AllocateSavedRegisterSlot(
                ImmutableArray<StackFrameSlot>.Builder slots,
                ref int cursor,
                ref int index,
                StackFrameSlotKind kind,
                MachineRegister register)
            {
                int size = MachineRegisters.RegisterSaveSize(register);
                int align = MachineRegisters.RegisterSaveAlignment(register);
                cursor = AlignUp(cursor, align);
                slots.Add(new StackFrameSlot(
                    kind,
                    index++,
                    cursor,
                    size,
                    align,
                    MachineRegisters.GetClass(register),
                    type: null,
                    savedRegister: register));
                cursor = checked(cursor + size);
            }

            private void AllocateLocalSlots(ImmutableArray<StackFrameSlot>.Builder slots, ref int cursor)
            {
                var descriptors = _method.GenTreeMethod.LocalDescriptors;
                var types = _method.GenTreeMethod.LocalTypes;
                for (int i = 0; i < types.Length; i++)
                {
                    if (!RequiresLocalOrTempHome(StackFrameSlotKind.Local, i, _explicitLocalSlots, descriptors))
                        continue;

                    var storage = StorageForType(types[i]);
                    cursor = AlignUp(cursor, storage.Alignment);
                    slots.Add(new StackFrameSlot(StackFrameSlotKind.Local, i, cursor, storage.Size, storage.Alignment, RegisterClass.Invalid, types[i]));
                    cursor = checked(cursor + storage.Size);
                }

                ValidateExplicitUserSlots(StackFrameSlotKind.Local, _explicitLocalSlots, types.Length);
            }

            private bool RequiresLocalOrTempHome(
                StackFrameSlotKind kind,
                int index,
                HashSet<int> explicitSlots,
                ImmutableArray<GenLocalDescriptor> descriptors)
            {
                if (explicitSlots.Contains(index))
                    return true;

                if (!TryGetTopLevelDescriptor(kind, index, descriptors, out var descriptor))
                    return true;

                if (descriptor.Category == GenLocalCategory.PromotedStruct &&
                    descriptor.HasPromotedStructFields &&
                    !descriptor.AddressExposed &&
                    !descriptor.MemoryAliased)
                    return false;

                if (descriptor.AddressExposed || descriptor.MemoryAliased || descriptor.DoNotEnregister)
                    return true;

                if (!descriptor.SsaPromoted)
                    return true;

                if (MachineAbi.RequiresStackHome(descriptor.Type, descriptor.StackKind))
                    return true;

                if (descriptor.Type is not null && descriptor.Type.IsValueType && descriptor.Type.ContainsGcPointers)
                    return true;

                return false;
            }

            private static bool TryGetTopLevelDescriptor(
                StackFrameSlotKind kind,
                int index,
                ImmutableArray<GenLocalDescriptor> descriptors,
                out GenLocalDescriptor descriptor)
            {
                GenLocalKind localKind = kind switch
                {
                    StackFrameSlotKind.Local => GenLocalKind.Local,
                    StackFrameSlotKind.Temp => GenLocalKind.Temporary,
                    _ => throw new ArgumentOutOfRangeException(nameof(kind)),
                };

                for (int i = 0; i < descriptors.Length; i++)
                {
                    var candidate = descriptors[i];
                    if (candidate.Kind == localKind && !candidate.IsStructField && candidate.Index == index)
                    {
                        descriptor = candidate;
                        return true;
                    }
                }

                descriptor = null!;
                return false;
            }

            private static void ValidateExplicitUserSlots(StackFrameSlotKind kind, HashSet<int> explicitSlots, int slotCount)
            {
                foreach (int index in explicitSlots)
                {
                    if ((uint)index >= (uint)slotCount)
                        throw new InvalidOperationException(kind + " slot " + index.ToString() + " is referenced by LIR but no such slot exists in the method frame table.");
                }
            }

            private void AllocateArgumentSlots(ImmutableArray<StackFrameSlot>.Builder slots, ref int cursor)
            {
                var argTypes = _method.GenTreeMethod.ArgTypes;
                for (int i = 0; i < argTypes.Length; i++)
                {
                    if (!RequiresIncomingArgumentHome(i))
                        continue;

                    var storage = StorageForType(argTypes[i]);
                    cursor = AlignUp(cursor, storage.Alignment);
                    slots.Add(new StackFrameSlot(StackFrameSlotKind.Argument, i, cursor, storage.Size, storage.Alignment, RegisterClass.Invalid, argTypes[i]));
                    cursor = checked(cursor + storage.Size);
                }
            }

            private bool RequiresIncomingArgumentHome(int index)
            {
                if (!TryGetTopLevelArgumentDescriptor(index, out var descriptor))
                    return true;

                if (descriptor.AddressExposed || descriptor.DoNotEnregister || descriptor.MemoryAliased)
                    return true;

                if (!descriptor.SsaPromoted)
                    return true;

                if (MachineAbi.RequiresStackHome(descriptor.Type, descriptor.StackKind))
                    return true;

                if (descriptor.Type is not null && descriptor.Type.IsValueType && descriptor.Type.ContainsGcPointers)
                    return true;

                if (RequiresIncomingArgumentHomeForPromotedFields(index, descriptor))
                    return true;

                return false;
            }

            private bool TryGetTopLevelArgumentDescriptor(int index, out GenLocalDescriptor descriptor)
            {
                var descriptors = _method.GenTreeMethod.ArgDescriptors;
                for (int i = 0; i < descriptors.Length; i++)
                {
                    var candidate = descriptors[i];
                    if (candidate.Kind == GenLocalKind.Argument && !candidate.IsStructField && candidate.Index == index)
                    {
                        descriptor = candidate;
                        return true;
                    }
                }

                descriptor = null!;
                return false;
            }

            private bool RequiresIncomingArgumentHomeForPromotedFields(int index, GenLocalDescriptor parentDescriptor)
            {
                var descriptors = _method.GenTreeMethod.ArgDescriptors;
                for (int i = 0; i < descriptors.Length; i++)
                {
                    var fieldDescriptor = descriptors[i];
                    if (!fieldDescriptor.IsStructField || fieldDescriptor.ParentLclNum != parentDescriptor.LclNum)
                        continue;

                    if (!fieldDescriptor.SsaPromoted)
                        return true;

                    if (PromotedArgumentFieldRequiresParentHome(index, parentDescriptor, fieldDescriptor))
                        return true;
                }

                return false;
            }

            private bool PromotedArgumentFieldRequiresParentHome(
                int parentArgumentIndex,
                GenLocalDescriptor parentDescriptor,
                GenLocalDescriptor fieldDescriptor)
            {
                int fieldOffset = fieldDescriptor.FieldOffset;
                int fieldSize = Math.Max(1, fieldDescriptor.FieldSize);
                if (fieldOffset < 0)
                    return true;

                RuntimeType? parentType = parentDescriptor.Type;
                GenStackKind parentStackKind = parentDescriptor.StackKind == GenStackKind.Unknown
                    ? MachineAbi.StackKindForType(parentType)
                    : parentDescriptor.StackKind;
                var parentAbi = MachineAbi.ClassifyValue(parentType, parentStackKind, isReturn: false);

                if (parentAbi.PassingKind == AbiValuePassingKind.Void)
                    return false;

                if (parentAbi.PassingKind == AbiValuePassingKind.ScalarRegister)
                {
                    int scalarSize = Math.Max(1, parentAbi.Size <= 0 ? TargetArchitecture.PointerSize : parentAbi.Size);
                    return fieldOffset != 0 || fieldSize != scalarSize;
                }

                if (parentAbi.PassingKind != AbiValuePassingKind.MultiRegister)
                    return false;

                var segments = MachineAbi.GetRegisterSegments(parentAbi);
                int fieldEnd = fieldOffset + fieldSize;
                for (int s = 0; s < segments.Length; s++)
                {
                    var segment = segments[s];
                    int segmentStart = segment.Offset;
                    int segmentEnd = segment.Offset + Math.Max(1, segment.Size);
                    if (fieldOffset < segmentStart || fieldEnd > segmentEnd)
                        continue;

                    if (!TryGetIncomingAggregateSegmentLocation(parentArgumentIndex, parentAbi, s, out var location))
                        return true;

                    if (!location.IsRegister)
                        return false;

                    return fieldOffset != segment.Offset || fieldSize != Math.Max(1, segment.Size);
                }

                return true;
            }

            private bool TryGetIncomingAggregateSegmentLocation(
                int argumentIndex,
                AbiValueInfo argumentAbi,
                int requestedSegmentIndex,
                out AbiArgumentLocation location)
            {
                location = default;
                if (argumentIndex < 0 || requestedSegmentIndex < 0)
                    return false;

                int generalArgumentIndex = 0;
                int floatArgumentIndex = 0;
                int incomingStackArgumentIndex = 0;
                int hiddenReturnBufferIndex = MachineAbi.HiddenReturnBufferInsertionIndex(
                    _method.GenTreeMethod.RuntimeMethod,
                    _method.GenTreeMethod.ArgTypes.Length);

                for (int i = 0; i <= argumentIndex; i++)
                {
                    if (hiddenReturnBufferIndex == i)
                        _ = MachineAbi.AssignScalarArgumentLocation(
                            RegisterClass.General,
                            TargetArchitecture.PointerSize,
                            ref generalArgumentIndex,
                            ref floatArgumentIndex,
                            ref incomingStackArgumentIndex);

                    RuntimeType currentType = _method.GenTreeMethod.ArgTypes[i];
                    GenStackKind currentStackKind = MachineAbi.StackKindForType(currentType);
                    var abi = i == argumentIndex
                        ? argumentAbi
                        : MachineAbi.ClassifyValue(currentType, currentStackKind, isReturn: false);

                    if (abi.PassingKind == AbiValuePassingKind.Void)
                        continue;

                    if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                    {
                        var registerClass = abi.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : abi.RegisterClass;
                        _ = MachineAbi.AssignScalarArgumentLocation(
                            registerClass,
                            abi.Size <= 0 ? TargetArchitecture.PointerSize : abi.Size,
                            ref generalArgumentIndex,
                            ref floatArgumentIndex,
                            ref incomingStackArgumentIndex);
                        continue;
                    }

                    if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        int aggregateStackSlot = -1;
                        int aggregateStackBaseOffset = 0;
                        var segments = MachineAbi.GetRegisterSegments(abi);
                        for (int s = 0; s < segments.Length; s++)
                        {
                            var segmentLocation = MachineAbi.AssignAggregateSegmentArgumentLocation(
                                segments[s],
                                ref generalArgumentIndex,
                                ref floatArgumentIndex,
                                ref incomingStackArgumentIndex,
                                ref aggregateStackSlot,
                                ref aggregateStackBaseOffset);

                            if (i == argumentIndex && s == requestedSegmentIndex)
                            {
                                location = segmentLocation;
                                return true;
                            }
                        }
                        continue;
                    }

                    incomingStackArgumentIndex++;
                }

                return false;
            }
            private void AllocateTempSlots(ImmutableArray<StackFrameSlot>.Builder slots, ref int cursor)
            {
                var temps = _method.GenTreeMethod.Temps;
                var descriptors = _method.GenTreeMethod.TempDescriptors;
                for (int i = 0; i < temps.Length; i++)
                {
                    var temp = temps[i];
                    if (!RequiresLocalOrTempHome(StackFrameSlotKind.Temp, temp.Index, _explicitTempSlots, descriptors))
                        continue;

                    var storage = temp.Type is null
                        ? StorageForStackKind(temp.StackKind)
                        : StorageForType(temp.Type);

                    cursor = AlignUp(cursor, storage.Alignment);
                    slots.Add(new StackFrameSlot(StackFrameSlotKind.Temp, temp.Index, cursor, storage.Size, storage.Alignment, RegisterClass.Invalid, temp.Type));
                    cursor = checked(cursor + storage.Size);
                }

                ValidateExplicitTempSlots(_explicitTempSlots, temps);
            }

            private static void ValidateExplicitTempSlots(HashSet<int> explicitSlots, ImmutableArray<GenTemp> temps)
            {
                foreach (int index in explicitSlots)
                {
                    if (!ContainsTempIndex(temps, index))
                        throw new InvalidOperationException("Temp slot " + index.ToString() + " is referenced by LIR but no such temp exists in the method frame table.");
                }
            }

            private static bool ContainsTempIndex(ImmutableArray<GenTemp> temps, int index)
            {
                for (int i = 0; i < temps.Length; i++)
                    if (temps[i].Index == index)
                        return true;
                return false;
            }

            private void AllocateSpillSlots(ImmutableArray<StackFrameSlot>.Builder slots, ref int cursor)
            {
                var specs = new List<SpillSpec>(_spillSpecs.Values);
                specs.Sort(static (a, b) => a.Index.CompareTo(b.Index));

                for (int i = 0; i < specs.Count; i++)
                {
                    var spec = specs[i];
                    var kind = spec.IsParallelCopyScratch ? StackFrameSlotKind.ParallelCopyScratch : StackFrameSlotKind.Spill;
                    int size = spec.Size <= 0 ? StorageForRegisterClass(spec.RegisterClass).Size : spec.Size;
                    int align = spec.Alignment <= 0 ? StorageForRegisterClass(spec.RegisterClass).Alignment : spec.Alignment;
                    cursor = AlignUp(cursor, align);
                    var slot = new StackFrameSlot(kind, spec.Index, cursor, size, align, spec.RegisterClass);
                    slots.Add(slot);
                    _spillSlots[spec.Index] = slot;
                    cursor = checked(cursor + size);
                }
            }

            private static StorageInfo StorageForOutgoingArgumentSlot(RegisterClass registerClass, StorageInfo valueStorage)
            {
                int size = Math.Max(MachineAbi.StackArgumentSlotSize, valueStorage.Size);
                int align = Math.Max(MachineAbi.StackArgumentSlotSize, valueStorage.Alignment);
                return new StorageInfo(size, align);
            }

            private void AllocateOutgoingArgumentSlots(ImmutableArray<StackFrameSlot>.Builder slots, ref int cursor)
            {
                if (_outgoingArgumentSpecs.Count == 0)
                    return;

                var specs = new List<OutgoingArgumentSpec>(_outgoingArgumentSpecs.Values);
                specs.Sort(static (a, b) => a.Index.CompareTo(b.Index));

                int nextIndex = 0;
                for (int i = 0; i < specs.Count; i++)
                {
                    var spec = specs[i];
                    while (nextIndex < spec.Index)
                    {
                        var defaultStorage = StorageForOutgoingArgumentSlot(RegisterClass.General, StorageForRegisterClass(RegisterClass.General));
                        cursor = AlignUp(cursor, defaultStorage.Alignment);
                        slots.Add(new StackFrameSlot(
                            StackFrameSlotKind.OutgoingArgument,
                            nextIndex++,
                            cursor,
                            defaultStorage.Size,
                            defaultStorage.Alignment,
                            RegisterClass.General));
                        cursor = checked(cursor + defaultStorage.Size);
                    }

                    RegisterClass registerClass = spec.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : spec.RegisterClass;
                    var fallback = StorageForRegisterClass(registerClass);
                    int size = spec.Size <= 0 ? fallback.Size : spec.Size;
                    int align = spec.Alignment <= 0 ? fallback.Alignment : spec.Alignment;
                    cursor = AlignUp(cursor, align);
                    slots.Add(new StackFrameSlot(
                        StackFrameSlotKind.OutgoingArgument,
                        spec.Index,
                        cursor,
                        size,
                        align,
                        registerClass));
                    cursor = checked(cursor + size);
                    nextIndex = spec.Index + 1;
                }
            }

            private ImmutableArray<GenTreeBlock> RewriteBlocks(out ImmutableArray<GenTree> allNodes)
            {
                var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(_method.Blocks.Length);
                var all = ImmutableArray.CreateBuilder<GenTree>(_method.LinearNodes.Length);

                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var sourceBlock = _method.Blocks[b];
                    var nodes = ImmutableArray.CreateBuilder<GenTree>(sourceBlock.LinearNodes.Length);
                    for (int i = 0; i < sourceBlock.LinearNodes.Length; i++)
                    {
                        var rewritten = RewriteNode(sourceBlock.LinearNodes[i]);
                        nodes.Add(rewritten);
                        all.Add(rewritten);
                    }
                    sourceBlock.SetLinearNodes(nodes.ToImmutable());
                    blocks.Add(sourceBlock);
                }

                allNodes = all.ToImmutable();
                return blocks.ToImmutable();
            }

            private ImmutableArray<RegisterAllocationInfo> RewriteAllocations(out IReadOnlyDictionary<GenTree, RegisterAllocationInfo> allocationByNode)
            {
                var result = ImmutableArray.CreateBuilder<RegisterAllocationInfo>(_method.Allocations.Length);
                var map = new Dictionary<GenTree, RegisterAllocationInfo>();

                for (int i = 0; i < _method.Allocations.Length; i++)
                {
                    var allocation = _method.Allocations[i];
                    var home = RewriteOperand(allocation.Home);
                    var segments = ImmutableArray.CreateBuilder<RegisterAllocationSegment>(allocation.Segments.Length);
                    for (int s = 0; s < allocation.Segments.Length; s++)
                    {
                        var segment = allocation.Segments[s];
                        segments.Add(new RegisterAllocationSegment(
                            segment.Start,
                            segment.End,
                            RewriteOperand(segment.Location)));
                    }

                    var fragments = ImmutableArray.CreateBuilder<RegisterAllocationFragment>(allocation.Fragments.Length);
                    for (int f = 0; f < allocation.Fragments.Length; f++)
                    {
                        var fragment = allocation.Fragments[f];
                        var fragmentSegments = ImmutableArray.CreateBuilder<RegisterAllocationSegment>(fragment.Segments.Length);
                        for (int s = 0; s < fragment.Segments.Length; s++)
                        {
                            var segment = fragment.Segments[s];
                            fragmentSegments.Add(new RegisterAllocationSegment(
                                segment.Start,
                                segment.End,
                                RewriteOperand(segment.Location)));
                        }

                        fragments.Add(new RegisterAllocationFragment(
                            fragment.SegmentIndex,
                            fragment.AbiSegment,
                            RewriteOperand(fragment.Home),
                            fragmentSegments.ToImmutable()));
                    }

                    var rewritten = new RegisterAllocationInfo(
                        allocation.Value,
                        home,
                        allocation.Ranges,
                        allocation.UsePositions,
                        allocation.DefinitionPosition,
                        segments.ToImmutable(),
                        fragments.ToImmutable());
                    result.Add(rewritten);
                    map[rewritten.Value] = rewritten;
                }

                allocationByNode = map;
                return result.ToImmutable();
            }

            private GenTree RewriteNode(GenTree node)
            {
                var results = ImmutableArray.CreateBuilder<RegisterOperand>(node.Results.Length);
                for (int i = 0; i < node.Results.Length; i++)
                    results.Add(RewriteOperand(node.Results[i]));

                var uses = ImmutableArray.CreateBuilder<RegisterOperand>(node.Uses.Length);
                for (int i = 0; i < node.Uses.Length; i++)
                    uses.Add(RewriteOperand(node.Uses[i]));

                return node.WithOperands(
                    results.ToImmutable(),
                    uses.ToImmutable(),
                    node.RegisterResults,
                    node.RegisterUses,
                    node.UseRoles);
            }

            private RegisterOperand RewriteUnresolvedFrameSlot(RegisterOperand operand)
            {
                StackFrameSlot slot;
                bool found = operand.Kind switch
                {
                    RegisterOperandKind.IncomingArgumentSlot => _layout.TryGetArgumentSlot(operand.FrameSlotIndex, out slot),
                    RegisterOperandKind.LocalSlot => _layout.TryGetLocalSlot(operand.FrameSlotIndex, out slot),
                    RegisterOperandKind.TempSlot => _layout.TryGetTempSlot(operand.FrameSlotIndex, out slot),
                    RegisterOperandKind.OutgoingArgumentSlot => _layout.TryGetOutgoingArgumentSlot(operand.FrameSlotIndex, out slot),
                    _ => throw new InvalidOperationException($"Operand is not an unresolved frame slot: {operand}."),
                };

                if (!found)
                    throw new InvalidOperationException($"Missing finalized frame slot for {operand}.");

                int fragmentOffset = slot.Offset + operand.FrameOffset;
                int fragmentSize = operand.FrameSlotSize > 0 ? operand.FrameSlotSize : slot.Size;
                if (operand.FrameOffset != 0 || operand.FrameSlotSize != 0)
                {
                    if (operand.FrameOffset < 0 || operand.FrameOffset > slot.Size)
                        throw new InvalidOperationException($"Frame slot fragment offset is outside slot bounds: {operand} in {slot}.");
                    if (fragmentSize <= 0 || operand.FrameOffset + fragmentSize > slot.Size)
                        throw new InvalidOperationException($"Frame slot fragment size is outside slot bounds: {operand} in {slot}.");
                }

                return RegisterOperand.ForFrameSlot(
                    operand.RegisterClass,
                    slot.Kind,
                    FrameBaseForUserSlot(),
                    slot.Index,
                    fragmentOffset,
                    fragmentSize,
                    operand.IsAddress);
            }

            private RegisterOperand RewriteOperand(RegisterOperand operand)
            {
                if (operand.IsUnresolvedFrameSlot)
                    return RewriteUnresolvedFrameSlot(operand);

                if (!operand.IsSpillSlot)
                    return operand;

                if (!_spillSlots.TryGetValue(operand.SpillSlot, out var slot))
                    slot = _layout.GetSpillSlot(operand.SpillSlot);

                int fragmentOffset = slot.Offset + operand.FrameOffset;
                int fragmentSize = operand.FrameSlotSize > 0 ? operand.FrameSlotSize : slot.Size;
                if (operand.FrameOffset != 0 || operand.FrameSlotSize != 0)
                {
                    if (operand.FrameOffset < 0 || operand.FrameOffset > slot.Size)
                        throw new InvalidOperationException($"Spill slot fragment offset is outside slot bounds: {operand} in {slot}.");
                    if (fragmentSize <= 0 || operand.FrameOffset + fragmentSize > slot.Size)
                        throw new InvalidOperationException($"Spill slot fragment size is outside slot bounds: {operand} in {slot}.");
                }

                return RegisterOperand.ForFrameSlot(
                    operand.RegisterClass,
                    slot.Kind,
                    FrameBaseForUserSlot(),
                    slot.Index,
                    fragmentOffset,
                    fragmentSize,
                    operand.IsAddress);
            }

            private RegisterFrameBase FrameBaseForUserSlot()
                => _layout.UsesFramePointer ? RegisterFrameBase.FramePointer : RegisterFrameBase.StackPointer;
        }

        private static StorageInfo StorageForValue(GenTreeValueInfo valueInfo)
        {
            if (valueInfo.Type is not null)
                return StorageForType(valueInfo.Type);

            return StorageForStackKind(valueInfo.StackKind);
        }

        private static StorageInfo StorageForType(RuntimeType type)
        {
            if (type.Kind == RuntimeTypeKind.TypeParam || type.IsReferenceType || type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef)
                return new StorageInfo(TargetArchitecture.PointerSize, TargetArchitecture.PointerSize);

            int size = type.SizeOf;
            int align = type.AlignOf;
            if (size <= 0)
                size = 1;
            if (align <= 0)
                align = 1;
            return new StorageInfo(size, align);
        }

        private static StorageInfo StorageForStackKind(GenStackKind stackKind)
        {
            return stackKind switch
            {
                GenStackKind.I4 => new StorageInfo(4, 4),
                GenStackKind.I8 => new StorageInfo(8, 8),
                GenStackKind.R4 => new StorageInfo(4, 4),
                GenStackKind.R8 => new StorageInfo(8, 8),
                GenStackKind.NativeInt => new StorageInfo(TargetArchitecture.PointerSize, TargetArchitecture.PointerSize),
                GenStackKind.NativeUInt => new StorageInfo(TargetArchitecture.PointerSize, TargetArchitecture.PointerSize),
                GenStackKind.Ref => new StorageInfo(TargetArchitecture.PointerSize, TargetArchitecture.PointerSize),
                GenStackKind.Null => new StorageInfo(TargetArchitecture.PointerSize, TargetArchitecture.PointerSize),
                GenStackKind.Ptr => new StorageInfo(TargetArchitecture.PointerSize, TargetArchitecture.PointerSize),
                GenStackKind.ByRef => new StorageInfo(TargetArchitecture.PointerSize, TargetArchitecture.PointerSize),
                GenStackKind.Value => new StorageInfo(8, 8),
                _ => new StorageInfo(TargetArchitecture.PointerSize, TargetArchitecture.PointerSize),
            };
        }

        private static StorageInfo StorageForRegisterClass(RegisterClass registerClass)
        {
            return registerClass == RegisterClass.Float
                ? new StorageInfo(TargetArchitecture.FloatingRegisterSize, TargetArchitecture.FloatingRegisterSize)
                : new StorageInfo(TargetArchitecture.GeneralRegisterSize, TargetArchitecture.GeneralRegisterSize);
        }

        private static StorageInfo StorageForAbiSegment(AbiRegisterSegment segment)
        {
            var fallback = StorageForRegisterClass(segment.RegisterClass);
            int size = segment.Size <= 0 ? fallback.Size : segment.Size;
            int align = Math.Min(Math.Max(1, size), fallback.Alignment);
            return new StorageInfo(size, align);
        }

        private static int AlignUp(int value, int align)
        {
            if (align <= 1)
                return value;
            if (!IsPowerOfTwo(align))
                throw new InvalidOperationException($"Alignment must be a power of two: {align}.");
            int mask = align - 1;
            return checked((value + mask) & ~mask);
        }

        private static bool IsPowerOfTwo(int value)
            => value > 0 && (value & (value - 1)) == 0;
    }
}
