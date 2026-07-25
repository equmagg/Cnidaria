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
            private TargetInfo Target => _method.GenTreeMethod.Target;

            private StorageInfo StorageForDescriptor(GenLocalDescriptor descriptor)
                => RegisterStackLayoutFinalizer.StorageForDescriptor(descriptor, Target);

            private StorageInfo StorageForValue(GenTreeValueInfo valueInfo)
                => RegisterStackLayoutFinalizer.StorageForValue(valueInfo, Target);

            private StorageInfo StorageForType(RuntimeType type)
                => RegisterStackLayoutFinalizer.StorageForType(type, Target);

            private StorageInfo StorageForStackKind(GenStackKind stackKind)
                => RegisterStackLayoutFinalizer.StorageForStackKind(stackKind, Target);

            private StorageInfo StorageForRegisterClass(RegisterClass registerClass)
                => RegisterStackLayoutFinalizer.StorageForRegisterClass(registerClass, Target);

            private StorageInfo StorageForAbiSegment(AbiRegisterSegment segment)
                => RegisterStackLayoutFinalizer.StorageForAbiSegment(segment, Target);

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
                switch (node.Kind)
                {
                    case GenTreeKind.Local:
                    case GenTreeKind.StoreLocal:
                        if (ContainsDescriptorIndex(_method.GenTreeMethod.LocalDescriptors, GenLocalKind.Local, node.Int32) &&
                            SurvivingLocalLikeNodeRequiresHome(node))
                        {
                            _explicitLocalSlots.Add(node.Int32);
                        }
                        return;

                    case GenTreeKind.LocalAddr:
                        if (ContainsDescriptorIndex(_method.GenTreeMethod.LocalDescriptors, GenLocalKind.Local, node.Int32))
                            _explicitLocalSlots.Add(node.Int32);
                        return;

                    case GenTreeKind.Temp:
                    case GenTreeKind.StoreTemp:
                        if (ContainsDescriptorIndex(_method.GenTreeMethod.TempDescriptors, GenLocalKind.Temporary, node.Int32) &&
                            SurvivingLocalLikeNodeRequiresHome(node))
                        {
                            _explicitTempSlots.Add(node.Int32);
                        }
                        return;

                    case GenTreeKind.TempAddr:
                        if (ContainsDescriptorIndex(_method.GenTreeMethod.TempDescriptors, GenLocalKind.Temporary, node.Int32))
                            _explicitTempSlots.Add(node.Int32);
                        return;
                }
            }

            private bool SurvivingLocalLikeNodeRequiresHome(GenTree node)
            {
                var descriptor = node.LocalDescriptor;
                if (descriptor is null)
                    return true;

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

                if (MachineAbi.RequiresStackHome(type, stackKind, Target))
                    return true;

                if (type is not null && type.IsValueType && type.ContainsGcPointers)
                    return true;

                var abi = MachineAbi.ClassifyValue(type, stackKind, isReturn: false, target: Target);
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

                int minimumSlotCount = _options.OutgoingArgumentSlotCount;
                if (MethodMayCall())
                    minimumSlotCount = Math.Max(minimumSlotCount, RegisterInfo.MinimumOutgoingArgumentSlots(Target));

                for (int i = 0; i < minimumSlotCount; i++)
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

            private StorageInfo FragmentStorageForOperand(RegisterOperand operand)
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
                bool saveReturnAddress = RegisterInfo.ReturnAddress(Target) != MachineRegister.Invalid &&
                    (_options.SaveReturnAddressForLeafMethods || (_options.SaveReturnAddressForNonLeafMethods && MethodMayCall()));

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

                int gcSpillOffset = cursor;
                int gcRootSpillSlotCount = 0;
                int gcSpillSize = 0;
                bool supportsGcTransitionArea = Target.IsRiscV || Target.IsX86;
                bool hasGcSafePoint = supportsGcTransitionArea && MethodHasGcSafePoint();
                int typeOperationScratchSize = supportsGcTransitionArea ? ComputeTypeOperationScratchSize() : 0;
                if (supportsGcTransitionArea && (hasGcSafePoint || typeOperationScratchSize != 0))
                {
                    int transitionAlignment = Math.Max(Target.PointerSize, ComputeNewObjectArgumentSaveAlignment());
                    cursor = AlignUp(cursor, transitionAlignment);
                    gcSpillOffset = cursor;
                    gcRootSpillSlotCount = hasGcSafePoint ? ComputeGcRootSpillSlotCount() : 0;
                    int gcRootSpillSize = checked(gcRootSpillSlotCount * Target.PointerSize);
                    int gcSpillCursor = gcRootSpillSize;
                    if (typeOperationScratchSize != 0)
                    {
                        int typeOperationScratchOffset = AlignUp(
                            checked(gcSpillOffset + gcSpillCursor),
                            ComputeTypeOperationScratchAlignment());
                        gcSpillCursor = checked(typeOperationScratchOffset - gcSpillOffset + typeOperationScratchSize);
                    }

                    if (MethodHasReferenceNewObject())
                    {
                        gcSpillCursor = AlignUp(gcSpillCursor, ComputeNewObjectArgumentSaveAlignment());
                        gcSpillCursor = checked(gcSpillCursor + ComputeNewObjectArgumentSaveSize());
                    }

                    gcSpillSize = gcSpillCursor;
                    cursor = checked(cursor + gcSpillSize);
                }

                int frameSize = AlignFrameSize(cursor, frameAlignment);

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
                    SelectFrameModel(usesFramePointer, frameSize),
                    gcSpillOffset,
                    gcSpillSize,
                    gcRootSpillSlotCount);
            }


            private int AlignFrameSize(int size, int alignment)
            {
                if (!Target.IsX86 || alignment <= Target.PointerSize || (size == 0 && !MethodMayCall()))
                    return AlignUp(size, alignment);

                int sizeWithReturnAddress = checked(size + Target.PointerSize);
                return checked(AlignUp(sizeWithReturnAddress, alignment) - Target.PointerSize);
            }

            private bool MethodHasGcSafePoint()
            {
                for (int i = 0; i < _method.LinearNodes.Length; i++)
                {
                    GenTree node = _method.LinearNodes[i];
                    if (node.TreeKind is
                        GenTreeKind.ClassInit or
                        GenTreeKind.GcPoll or
                        GenTreeKind.NewObject or
                        GenTreeKind.NewArray or
                        GenTreeKind.NewDelegate or
                        GenTreeKind.DelegateCombine or
                        GenTreeKind.DelegateRemove or
                        GenTreeKind.Box)
                    {
                        return true;
                    }
                    if (node.TreeKind is GenTreeKind.IndirectCall or GenTreeKind.VirtualCall or GenTreeKind.DelegateInvoke)
                        return true;
                    if (node.TreeKind == GenTreeKind.Call &&
                        (node.Method?.HasInternalCall != true ||
                        (node.Method is not null &&
                         ((Target.IsRiscV && RiscVRuntime.IsGcSafePointInternalCall(node.Method)) ||
                          (Target.IsX86 && X86Runtime.IsGcSafePointInternalCall(node.Method))))))
                    {
                        return true;
                    }
                }
                return false;
            }

            private int ComputeTypeOperationScratchSize()
            {
                int size = 0;
                for (int i = 0; i < _method.LinearNodes.Length; i++)
                {
                    GenTree node = _method.LinearNodes[i];
                    RuntimeType? type = TypeOperationScratchType(node);
                    if (type?.IsValueType == true)
                        size = Math.Max(size, Math.Max(1, type.SizeOf));

                    if (node.TreeKind == GenTreeKind.NewDelegate && node.Uses.Length != 0)
                        size = Math.Max(size, Target.PointerSize);
                    else if (node.TreeKind is GenTreeKind.DelegateCombine or GenTreeKind.DelegateRemove)
                        size = Math.Max(size, checked(Target.PointerSize * 3));
                }

                return size == 0 ? 0 : AlignUp(size, ComputeTypeOperationScratchAlignment());
            }

            private int ComputeTypeOperationScratchAlignment()
            {
                int alignment = Target.PointerSize;
                for (int i = 0; i < _method.LinearNodes.Length; i++)
                {
                    RuntimeType? type = TypeOperationScratchType(_method.LinearNodes[i]);
                    if (type?.IsValueType == true)
                        alignment = Math.Max(alignment, Math.Max(1, type.AlignOf));
                }
                return alignment;
            }

            private RuntimeType? TypeOperationScratchType(GenTree node)
            {
                if (node.TreeKind is not (GenTreeKind.Box or GenTreeKind.UnboxAny))
                    return null;

                if (node.TreeKind == GenTreeKind.Box && !node.RegisterUses.IsDefaultOrEmpty)
                    return _method.GenTreeMethod.GetValueInfo(node.RegisterUses[0]).Type ?? node.RuntimeType ?? node.Type;

                return node.RuntimeType ?? node.Type;
            }

            private int ComputeNewObjectArgumentSaveAlignment()
            {
                int alignment = Target.PointerSize;
                for (int i = 1; i < 32; i++)
                {
                    MachineRegister register = RegisterInfo.GetIntegerArgumentRegister(Target, i);
                    if (register == MachineRegister.Invalid)
                        break;
                    alignment = Math.Max(alignment, RegisterInfo.RegisterSaveAlignment(Target, register));
                }
                for (int i = 0; i < 32; i++)
                {
                    MachineRegister register = RegisterInfo.GetFloatArgumentRegister(Target, i);
                    if (register == MachineRegister.Invalid)
                        break;
                    alignment = Math.Max(alignment, RegisterInfo.RegisterSaveAlignment(Target, register));
                }
                return Math.Max(1, alignment);
            }

            private int ComputeNewObjectArgumentSaveSize()
            {
                int cursor = 0;
                for (int i = 1; i < 32; i++)
                {
                    MachineRegister register = RegisterInfo.GetIntegerArgumentRegister(Target, i);
                    if (register == MachineRegister.Invalid)
                        break;
                    int alignment = RegisterInfo.RegisterSaveAlignment(Target, register);
                    int size = RegisterInfo.RegisterSaveSize(Target, register);
                    cursor = AlignUp(cursor, alignment);
                    cursor = checked(cursor + size);
                }
                for (int i = 0; i < 32; i++)
                {
                    MachineRegister register = RegisterInfo.GetFloatArgumentRegister(Target, i);
                    if (register == MachineRegister.Invalid)
                        break;
                    int alignment = RegisterInfo.RegisterSaveAlignment(Target, register);
                    int size = RegisterInfo.RegisterSaveSize(Target, register);
                    cursor = AlignUp(cursor, alignment);
                    cursor = checked(cursor + size);
                }
                return cursor;
            }

            private bool MethodHasReferenceNewObject()
            {
                for (int i = 0; i < _method.LinearNodes.Length; i++)
                {
                    GenTree node = _method.LinearNodes[i];
                    if (node.TreeKind == GenTreeKind.NewObject && node.Method?.DeclaringType.IsValueType == false)
                        return true;
                }
                return false;
            }

            private int ComputeGcRootSpillSlotCount()
            {
                int count = 0;

                for (int i = 0; i < _method.Allocations.Length; i++)
                {
                    var allocation = _method.Allocations[i];
                    if (!_method.GenTreeMethod.ValueInfoByNode.TryGetValue(allocation.ValueKey, out var info))
                        continue;
                    count = checked(count + GcCellCount(info.Type, info.StackKind));
                }

                count = checked(count + GcCellCount(_method.GenTreeMethod.ArgDescriptors));
                count = checked(count + GcCellCount(_method.GenTreeMethod.LocalDescriptors));
                count = checked(count + GcCellCount(_method.GenTreeMethod.TempDescriptors));
                count = checked(count + GcSafePointOperandCellCount());
                return checked(count + 1);
            }

            private int GcSafePointOperandCellCount()
            {
                int maximum = 0;
                for (int i = 0; i < _method.LinearNodes.Length; i++)
                {
                    GenTree node = _method.LinearNodes[i];
                    if (!node.HasLoweringFlag(GenTreeLinearFlags.GcSafePoint))
                        continue;

                    int count = 0;
                    int operandCount = Math.Min(node.Uses.Length, node.RegisterUses.Length);
                    for (int u = 0; u < operandCount; u++)
                    {
                        if (u < node.UseRoles.Length && node.UseRoles[u] == OperandRole.HiddenReturnBuffer)
                            continue;
                        GenTree value = node.RegisterUses[u];
                        if (!_method.GenTreeMethod.ValueInfoByNode.TryGetValue(value.LinearValueKey, out var info))
                            continue;
                        count = checked(count + GcCellCount(info.Type, info.StackKind));
                    }
                    maximum = Math.Max(maximum, count);
                }
                return maximum;
            }

            private static int GcCellCount(ImmutableArray<GenLocalDescriptor> descriptors)
            {
                int count = 0;
                for (int i = 0; i < descriptors.Length; i++)
                {
                    var descriptor = descriptors[i];
                    if (descriptor.IsStructField)
                        continue;
                    count = checked(count + GcCellCount(descriptor.Type, descriptor.StackKind));
                }
                return count;
            }

            private static int GcCellCount(RuntimeType? type, GenStackKind stackKind)
            {
                if (type is not null)
                {
                    if (type.IsReferenceType || type.Kind is RuntimeTypeKind.ByRef or RuntimeTypeKind.TypeParam)
                        return 1;
                    if (type.IsValueType && type.ContainsGcPointers)
                        return type.GcPointerOffsets.Length;
                }

                return stackKind is GenStackKind.Ref or GenStackKind.ByRef or GenStackKind.Null ? 1 : 0;
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
                if ((Target.IsRiscV || Target.IsX86) && MethodHasGcSafePoint())
                    return true;
                if ((Target.IsRiscV || Target.IsX86) && ComputeTypeOperationScratchSize() != 0)
                    return true;

                if (_options.UseFramePointerForFunclets && _method.Funclets.Length > 1)
                    return true;

                if (_method.GenTreeMethod.Cfg.ExceptionRegions.Length != 0)
                    return true;

                if (MethodHasDynamicStackAllocation())
                    return true;

                if (UsesSpecificCalleeSavedRegister(RegisterInfo.FramePointer(Target)))
                    return true;

                return _options.SaveFramePointerWhenFrameIsUsed;
            }

            private bool MethodHasDynamicStackAllocation()
            {
                for (int i = 0; i < _method.LinearNodes.Length; i++)
                {
                    if (_method.LinearNodes[i].TreeKind == GenTreeKind.StackAlloc)
                        return true;
                }
                return false;
            }

            private bool MethodMayCall()
            {
                for (int i = 0; i < _method.LinearNodes.Length; i++)
                {
                    var node = _method.LinearNodes[i];
                    if (node.HasLoweringFlag(GenTreeLinearFlags.CallerSavedKill))
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
                    AllocateSavedRegisterSlot(slots, ref cursor, ref index, StackFrameSlotKind.ReturnAddress, RegisterInfo.ReturnAddress(Target));

                var used = new SortedSet<MachineRegister>();
                if (forceFramePointerSave)
                    used.Add(RegisterInfo.FramePointer(Target));
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

            private bool AllocationUsesCalleeSavedRegister(RegisterAllocationInfo allocation)
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

            private void CollectCalleeSavedRegisters(RegisterAllocationInfo allocation, SortedSet<MachineRegister> used)
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

            private bool IsCalleeSavedRegisterOperand(RegisterOperand operand)
                => operand.IsRegister && RegisterInfo.IsCalleeSaved(Target, operand.Register);

            private void AddCalleeSavedRegister(RegisterOperand operand, SortedSet<MachineRegister> used)
            {
                if (IsCalleeSavedRegisterOperand(operand))
                    used.Add(operand.Register);
            }

            private void AllocateSavedRegisterSlot(
                ImmutableArray<StackFrameSlot>.Builder slots,
                ref int cursor,
                ref int index,
                StackFrameSlotKind kind,
                MachineRegister register)
            {
                int size = RegisterInfo.RegisterSaveSize(Target, register);
                int align = RegisterInfo.RegisterSaveAlignment(Target, register);
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
                var allocated = new HashSet<int>();
                for (int i = 0; i < descriptors.Length; i++)
                {
                    var descriptor = descriptors[i];
                    if (descriptor.Kind != GenLocalKind.Local)
                        continue;

                    if (!allocated.Add(descriptor.Index))
                        continue;

                    if (!RequiresDescriptorHome(descriptor, _explicitLocalSlots))
                        continue;

                    var storage = StorageForDescriptor(descriptor);
                    cursor = AlignUp(cursor, storage.Alignment);
                    slots.Add(new StackFrameSlot(StackFrameSlotKind.Local, descriptor.Index, cursor, storage.Size, storage.Alignment, RegisterClass.Invalid, descriptor.Type));
                    cursor = checked(cursor + storage.Size);
                }

                ValidateExplicitUserSlots(StackFrameSlotKind.Local, _explicitLocalSlots, descriptors);
            }

            private bool RequiresDescriptorHome(GenLocalDescriptor descriptor, HashSet<int> explicitSlots)
            {
                if (explicitSlots.Contains(descriptor.Index))
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

                if (MachineAbi.RequiresStackHome(descriptor.Type, descriptor.StackKind, Target))
                    return true;

                if (descriptor.Type is not null && descriptor.Type.IsValueType && descriptor.Type.ContainsGcPointers)
                    return true;

                return false;
            }

            private static void ValidateExplicitUserSlots(
                StackFrameSlotKind kind,
                HashSet<int> explicitSlots,
                ImmutableArray<GenLocalDescriptor> descriptors)
            {
                GenLocalKind localKind = kind switch
                {
                    StackFrameSlotKind.Local => GenLocalKind.Local,
                    StackFrameSlotKind.Temp => GenLocalKind.Temporary,
                    _ => throw new ArgumentOutOfRangeException(nameof(kind)),
                };

                foreach (int index in explicitSlots)
                {
                    if (!ContainsDescriptorIndex(descriptors, localKind, index))
                        throw new InvalidOperationException(kind + " slot " + index.ToString() + " is referenced by LIR but no such slot exists in the method frame table.");
                }
            }

            private static bool ContainsDescriptorIndex(ImmutableArray<GenLocalDescriptor> descriptors, GenLocalKind kind, int index)
            {
                for (int i = 0; i < descriptors.Length; i++)
                {
                    var descriptor = descriptors[i];
                    if (descriptor.Kind == kind && descriptor.Index == index)
                        return true;
                }

                return false;
            }

            private void AllocateArgumentSlots(ImmutableArray<StackFrameSlot>.Builder slots, ref int cursor)
            {
                var argTypes = _method.GenTreeMethod.ArgTypes;
                int hiddenReturnBufferHomeIndex = argTypes.Length;
                if (MachineAbi.RequiresHiddenReturnBuffer(_method.GenTreeMethod.RuntimeMethod, Target))
                {
                    cursor = AlignUp(cursor, Target.PointerSize);
                    slots.Add(new StackFrameSlot(
                        StackFrameSlotKind.Argument,
                        hiddenReturnBufferHomeIndex,
                        cursor,
                        Target.PointerSize,
                        Target.PointerSize,
                        RegisterClass.General));
                    cursor = checked(cursor + Target.PointerSize);
                }

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
                RuntimeType argType = _method.GenTreeMethod.ArgTypes[index];
                var argAbi = MachineAbi.ClassifyValue(argType, MachineAbi.StackKindForType(argType), isReturn: false, target: Target);
                var effectiveArgAbi = GetEffectiveIncomingArgumentAbi(index, argAbi);
                if (!MachineAbi.HaveMatchingArgumentValueLayout(argAbi, effectiveArgAbi, Target))
                    return true;
                if (effectiveArgAbi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect)
                    return true;

                if (!TryGetTopLevelArgumentDescriptor(index, out var descriptor))
                    return true;

                if (descriptor.AddressExposed || descriptor.DoNotEnregister || descriptor.MemoryAliased)
                    return true;

                if (!descriptor.SsaPromoted)
                    return true;

                if (MachineAbi.RequiresStackHome(descriptor.Type, descriptor.StackKind, Target))
                    return true;

                if (descriptor.Type is not null && descriptor.Type.IsValueType && descriptor.Type.ContainsGcPointers)
                    return true;

                if (RequiresIncomingArgumentHomeForPromotedFields(index, descriptor))
                    return true;

                return false;
            }

            private AbiValueInfo GetEffectiveIncomingArgumentAbi(int argumentIndex, AbiValueInfo requestedArgumentAbi)
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
                    {
                        _ = MachineAbi.AssignScalarArgumentLocation(
                            RegisterClass.General,
                            Target.PointerSize,
                            ref generalArgumentIndex,
                            ref floatArgumentIndex,
                            ref incomingStackArgumentIndex,
                            Target);
                    }

                    RuntimeType currentType = _method.GenTreeMethod.ArgTypes[i];
                    var valueAbi = i == argumentIndex
                        ? requestedArgumentAbi
                        : MachineAbi.ClassifyValue(
                            currentType,
                            MachineAbi.StackKindForType(currentType),
                            isReturn: false,
                            target: Target);
                    var effectiveAbi = MachineAbi.AdjustArgumentAbiForRegisterAvailability(
                        valueAbi,
                        generalArgumentIndex,
                        floatArgumentIndex,
                        Target);
                    if (i == argumentIndex)
                        return effectiveAbi;

                    ConsumeIncomingArgumentAbi(
                        effectiveAbi,
                        ref generalArgumentIndex,
                        ref floatArgumentIndex,
                        ref incomingStackArgumentIndex);
                }

                throw new InvalidOperationException("Invalid incoming argument index " + argumentIndex.ToString() + ".");
            }

            private void ConsumeIncomingArgumentAbi(
                AbiValueInfo abi,
                ref int generalArgumentIndex,
                ref int floatArgumentIndex,
                ref int incomingStackArgumentIndex)
            {
                if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                {
                    var registerClass = abi.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : abi.RegisterClass;
                    _ = MachineAbi.AssignScalarArgumentLocation(
                        registerClass,
                        abi.Size <= 0 ? Target.PointerSize : abi.Size,
                        ref generalArgumentIndex,
                        ref floatArgumentIndex,
                        ref incomingStackArgumentIndex,
                        Target);
                    return;
                }

                if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    int aggregateStackSlot = -1;
                    int aggregateStackBaseOffset = 0;
                    var segments = MachineAbi.GetRegisterSegments(abi, Target);
                    for (int i = 0; i < segments.Length; i++)
                    {
                        _ = MachineAbi.AssignAggregateSegmentArgumentLocation(
                            segments[i],
                            ref generalArgumentIndex,
                            ref floatArgumentIndex,
                            ref incomingStackArgumentIndex,
                            ref aggregateStackSlot,
                            ref aggregateStackBaseOffset,
                            Target);
                    }
                    return;
                }

                if (abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect)
                {
                    int stackSize = abi.Size <= 0 ? Target.PointerSize : abi.Size;
                    incomingStackArgumentIndex = checked(
                        incomingStackArgumentIndex + MachineAbi.StackSlotsForArgumentSize(stackSize, Target));
                }
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
                var parentAbi = MachineAbi.ClassifyValue(parentType, parentStackKind, isReturn: false, target: Target);
                var effectiveParentAbi = GetEffectiveIncomingArgumentAbi(parentArgumentIndex, parentAbi);
                if (!MachineAbi.HaveMatchingArgumentValueLayout(parentAbi, effectiveParentAbi, Target))
                    return true;

                if (parentAbi.PassingKind == AbiValuePassingKind.Void)
                    return false;

                if (parentAbi.PassingKind == AbiValuePassingKind.ScalarRegister)
                {
                    int scalarSize = Math.Max(1, parentAbi.Size <= 0 ? Target.PointerSize : parentAbi.Size);
                    return fieldOffset != 0 || fieldSize != scalarSize;
                }

                if (parentAbi.PassingKind != AbiValuePassingKind.MultiRegister)
                    return false;

                var segments = MachineAbi.GetRegisterSegments(parentAbi, Target);
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
                int incomingStackArgumentIndex = RegisterInfo.MinimumOutgoingArgumentSlots(Target);
                int hiddenReturnBufferIndex = MachineAbi.HiddenReturnBufferInsertionIndex(
                    _method.GenTreeMethod.RuntimeMethod,
                    _method.GenTreeMethod.ArgTypes.Length,
                    Target);

                for (int i = 0; i <= argumentIndex; i++)
                {
                    if (hiddenReturnBufferIndex == i)
                        _ = MachineAbi.AssignScalarArgumentLocation(
                            RegisterClass.General,
                            Target.PointerSize,
                            ref generalArgumentIndex,
                            ref floatArgumentIndex,
                            ref incomingStackArgumentIndex,
                            Target);

                    RuntimeType currentType = _method.GenTreeMethod.ArgTypes[i];
                    GenStackKind currentStackKind = MachineAbi.StackKindForType(currentType);
                    var valueAbi = i == argumentIndex
                        ? argumentAbi
                        : MachineAbi.ClassifyValue(currentType, currentStackKind, isReturn: false, target: Target);
                    var abi = MachineAbi.AdjustArgumentAbiForRegisterAvailability(
                        valueAbi,
                        generalArgumentIndex,
                        floatArgumentIndex,
                        Target);

                    if (i == argumentIndex && !MachineAbi.HaveMatchingArgumentValueLayout(valueAbi, abi, Target))
                        return false;

                    if (abi.PassingKind == AbiValuePassingKind.Void)
                        continue;

                    if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                    {
                        var registerClass = abi.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : abi.RegisterClass;
                        _ = MachineAbi.AssignScalarArgumentLocation(
                            registerClass,
                            abi.Size <= 0 ? Target.PointerSize : abi.Size,
                            ref generalArgumentIndex,
                            ref floatArgumentIndex,
                            ref incomingStackArgumentIndex,
                            Target);
                        continue;
                    }

                    if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        int aggregateStackSlot = -1;
                        int aggregateStackBaseOffset = 0;
                        var segments = MachineAbi.GetRegisterSegments(abi, Target);
                        for (int s = 0; s < segments.Length; s++)
                        {
                            var segmentLocation = MachineAbi.AssignAggregateSegmentArgumentLocation(
                                segments[s],
                                ref generalArgumentIndex,
                                ref floatArgumentIndex,
                                ref incomingStackArgumentIndex,
                                ref aggregateStackSlot,
                                ref aggregateStackBaseOffset,
                                Target);

                            if (i == argumentIndex && s == requestedSegmentIndex)
                            {
                                location = segmentLocation;
                                return true;
                            }
                        }
                        continue;
                    }

                    int stackSize = abi.Size <= 0 ? Target.PointerSize : abi.Size;
                    incomingStackArgumentIndex = checked(incomingStackArgumentIndex + MachineAbi.StackSlotsForArgumentSize(stackSize, Target));
                }

                return false;
            }
            private void AllocateTempSlots(ImmutableArray<StackFrameSlot>.Builder slots, ref int cursor)
            {
                var descriptors = _method.GenTreeMethod.TempDescriptors;
                var allocated = new HashSet<int>();
                for (int i = 0; i < descriptors.Length; i++)
                {
                    var descriptor = descriptors[i];
                    if (descriptor.Kind != GenLocalKind.Temporary)
                        continue;

                    if (!allocated.Add(descriptor.Index))
                        continue;

                    if (!RequiresDescriptorHome(descriptor, _explicitTempSlots))
                        continue;

                    var storage = StorageForDescriptor(descriptor);
                    cursor = AlignUp(cursor, storage.Alignment);
                    slots.Add(new StackFrameSlot(StackFrameSlotKind.Temp, descriptor.Index, cursor, storage.Size, storage.Alignment, RegisterClass.Invalid, descriptor.Type));
                    cursor = checked(cursor + storage.Size);
                }

                ValidateExplicitUserSlots(StackFrameSlotKind.Temp, _explicitTempSlots, descriptors);
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

            private StorageInfo StorageForOutgoingArgumentSlot(RegisterClass registerClass, StorageInfo valueStorage)
            {
                int size = Math.Max(Target.StackSlotSize, valueStorage.Size);
                int align = Math.Max(Target.StackSlotSize, valueStorage.Alignment);
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
                    if (spec.Index < nextIndex)
                        continue;

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
                    nextIndex = checked(spec.Index + MachineAbi.StackSlotsForArgumentSize(size, Target));
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

        private static StorageInfo StorageForDescriptor(GenLocalDescriptor descriptor, TargetInfo target)
        {
            if (descriptor.Type is not null)
                return StorageForType(descriptor.Type, target);

            return StorageForStackKind(descriptor.StackKind, target);
        }

        private static StorageInfo StorageForValue(GenTreeValueInfo valueInfo, TargetInfo target)
        {
            if (valueInfo.Type is not null)
                return StorageForType(valueInfo.Type, target);

            return StorageForStackKind(valueInfo.StackKind, target);
        }

        private static StorageInfo StorageForType(RuntimeType type, TargetInfo target)
        {
            if (type.Kind == RuntimeTypeKind.TypeParam || type.IsReferenceType || type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.FunctionPointer or RuntimeTypeKind.ByRef)
                return new StorageInfo(target.PointerSize, target.PointerSize);

            int size = type.SizeOf;
            int align = type.AlignOf;
            if (size <= 0)
                size = 1;
            if (align <= 0)
                align = 1;
            return new StorageInfo(size, align);
        }

        private static StorageInfo StorageForStackKind(GenStackKind stackKind, TargetInfo target)
        {
            return stackKind switch
            {
                GenStackKind.I4 => new StorageInfo(4, 4),
                GenStackKind.I8 => new StorageInfo(8, 8),
                GenStackKind.R4 => new StorageInfo(4, 4),
                GenStackKind.R8 => new StorageInfo(8, 8),
                GenStackKind.NativeInt => new StorageInfo(target.PointerSize, target.PointerSize),
                GenStackKind.NativeUInt => new StorageInfo(target.PointerSize, target.PointerSize),
                GenStackKind.Ref => new StorageInfo(target.PointerSize, target.PointerSize),
                GenStackKind.Null => new StorageInfo(target.PointerSize, target.PointerSize),
                GenStackKind.Ptr => new StorageInfo(target.PointerSize, target.PointerSize),
                GenStackKind.ByRef => new StorageInfo(target.PointerSize, target.PointerSize),
                GenStackKind.Value => new StorageInfo(8, 8),
                _ => new StorageInfo(target.PointerSize, target.PointerSize),
            };
        }

        private static StorageInfo StorageForRegisterClass(RegisterClass registerClass, TargetInfo target)
        {
            if (registerClass != RegisterClass.Float)
                return new StorageInfo(target.GeneralRegisterSize, target.GeneralRegisterSize);

            int size = RegisterInfo.AbiFloatingRegisterSize(target);
            if (size <= 0)
                size = target.FloatingRegisterSize;
            return new StorageInfo(size, size);
        }

        private static StorageInfo StorageForAbiSegment(AbiRegisterSegment segment, TargetInfo target)
        {
            var fallback = StorageForRegisterClass(segment.RegisterClass, target);
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
