using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Cnidaria.Cs
{
    internal static class LinearLiveness
    {
        public static GenTreeMethod Attach(GenTreeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            var layout = PositionLayout.Build(method);
            var intervals = BuildIntervals(method, layout, out var intervalMap);
            MarkUnusedValueDefinitions(method, intervalMap);

            var refPositions = BuildRefPositions(method, intervalMap, layout);
            method.AttachLiveness(intervals, intervalMap, refPositions);
            return method;
        }

        public static ImmutableArray<LinearRefPosition> BuildRefPositions(
            GenTreeMethod method,
            IReadOnlyDictionary<GenTree, LinearLiveInterval> intervals,
            PositionLayout layout)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            var result = ImmutableArray.CreateBuilder<LinearRefPosition>();

            for (int i = 0; i < method.LinearNodes.Length; i++)
            {
                var node = method.LinearNodes[i];
                int usePosition = layout.NodePositions[node.LinearId];
                int defPosition = usePosition + 1;

                AddInternalRegisterRefPositions(result, node, usePosition);

                if (node.LinearKind is GenTreeLinearKind.Copy or GenTreeLinearKind.PhiCopy)
                {
                    if (node.RegisterResult is null || node.RegisterUses.Length != 1)
                        throw new InvalidOperationException("linear IR copy node must have one source and one destination value.");

                    int copyUsePosition = usePosition;
                    int copyDefPosition = defPosition;
                    if (node.IsPhiCopy)
                    {
                        copyUsePosition = ComputePhiCopySourcePosition(layout, node);
                        copyDefPosition = ComputePhiCopyDestinationPosition(layout, node);
                    }

                    AddValueCopyRefPositions(
                        result,
                        method,
                        intervals,
                        node.LinearId,
                        copyUsePosition,
                        copyDefPosition,
                        operandIndex: 0,
                        sourceValue: node.RegisterUses[0],
                        destinationValue: node.RegisterResult);
                }
                else if (node.HasLoweringFlag(GenTreeLinearFlags.AbiCall))
                {
                    AddCallRefPositions(result, method, intervals, node, usePosition, defPosition);
                }
                else if (node.Kind == GenTreeKind.Return)
                {
                    AddReturnRefPositions(result, method, intervals, node, usePosition);
                }
                else
                {
                    AddDefaultNodeRefPositions(result, method, intervals, node, usePosition, defPosition);
                }

                if (node.HasLoweringFlag(GenTreeLinearFlags.CallerSavedKill))
                    AddRegisterKills(result, node, usePosition, MachineRegisters.CallerSavedRegisters);
            }

            AddInitialArgumentFixedRefPositions(result, method, layout);

            result.Sort(static (a, b) =>
            {
                int c = a.Position.CompareTo(b.Position);
                if (c != 0)
                    return c;
                c = RefKindSortOrder(a.Kind).CompareTo(RefKindSortOrder(b.Kind));
                if (c != 0)
                    return c;
                c = a.NodeId.CompareTo(b.NodeId);
                if (c != 0)
                    return c;
                c = a.OperandIndex.CompareTo(b.OperandIndex);
                if (c != 0)
                    return c;
                return a.AbiSegmentIndex.CompareTo(b.AbiSegmentIndex);
            });

            return result.ToImmutable();
        }

        private static int RefKindSortOrder(LinearRefPositionKind kind)
            => kind switch
            {
                LinearRefPositionKind.Internal => 0,
                LinearRefPositionKind.Use => 1,
                LinearRefPositionKind.Kill => 2,
                LinearRefPositionKind.Def => 3,
                _ => 4,
            };

        private static void AddInitialArgumentFixedRefPositions(
            ImmutableArray<LinearRefPosition>.Builder result,
            GenTreeMethod method,
            PositionLayout layout)
        {
            if (method.Values.IsDefaultOrEmpty || method.ArgTypes.IsDefaultOrEmpty)
                return;

            for (int i = 0; i < method.Values.Length; i++)
            {
                var info = method.Values[i];
                if (!IsInitialSsaArgumentValue(info))
                    continue;

                if ((uint)info.Value.SsaSlot.Index >= (uint)method.ArgTypes.Length)
                    continue;

                AddInitialArgumentFixedRefPositions(result, method, layout, info, info.Value.SsaSlot.Index);
            }
        }

        private static bool IsInitialSsaArgumentValue(GenTreeValueInfo info)
            => info.Value.IsSsaValue &&
               info.Value.SsaSlot.Kind == SsaSlotKind.Arg &&
               info.DefinitionBlockId < 0 &&
               info.DefinitionNodeId < 0;

        private static void AddInitialArgumentFixedRefPositions(
            ImmutableArray<LinearRefPosition>.Builder result,
            GenTreeMethod method,
            PositionLayout layout,
            GenTreeValueInfo info,
            int argumentIndex)
        {
            int general = 0;
            int floating = 0;
            int hiddenReturnBufferInsertionIndex = MachineAbi.HiddenReturnBufferInsertionIndex(
                method.RuntimeMethod,
                method.ArgTypes.Length);
            int position = ComputeInitialDefinitionPosition(layout, info);

            for (int i = 0; i <= argumentIndex; i++)
            {
                if (hiddenReturnBufferInsertionIndex == i)
                    _ = GetMaybeArgumentRegister(RegisterClass.General, ref general, ref floating);

                RuntimeType currentType = method.ArgTypes[i];
                GenStackKind currentStackKind = i == argumentIndex ? info.StackKind : StackKindForAbi(currentType);
                var abi = i == argumentIndex
                    ? MachineAbi.ClassifyValue(info.Type, currentStackKind, isReturn: false)
                    : MachineAbi.ClassifyValue(currentType, currentStackKind, isReturn: false);

                if (abi.PassingKind == AbiValuePassingKind.Void)
                    continue;

                if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                {
                    var register = GetMaybeArgumentRegister(abi.RegisterClass, ref general, ref floating);
                    if (i == argumentIndex && register != MachineRegister.Invalid)
                    {
                        result.Add(new LinearRefPosition(
                            -1,
                            position,
                            -1,
                            LinearRefPositionKind.Def,
                            info.RepresentativeNode,
                            abi.RegisterClass,
                            register,
                            GetValueRefFlags(info) | LinearRefPositionFlags.FixedRegister | LinearRefPositionFlags.RequiresRegister));
                    }
                    continue;
                }

                if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    var segments = MachineAbi.GetRegisterSegments(abi);
                    for (int s = 0; s < segments.Length; s++)
                    {
                        var segment = segments[s];
                        var register = GetMaybeArgumentRegister(segment.RegisterClass, ref general, ref floating);
                        if (i == argumentIndex && register != MachineRegister.Invalid)
                        {
                            result.Add(new LinearRefPosition(
                                -1,
                                position,
                                -1,
                                s,
                                segment.Offset,
                                segment.Size,
                                LinearRefPositionKind.Def,
                                info.RepresentativeNode,
                                segment.RegisterClass,
                                register,
                                WithSegmentGcFlags(GetValueRefFlags(info) | LinearRefPositionFlags.FixedRegister | LinearRefPositionFlags.RequiresRegister, segment)));
                        }
                    }
                    continue;
                }

                if (abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect)
                {
                    continue;
                }
            }
        }

        private static MachineRegister GetMaybeArgumentRegister(RegisterClass registerClass, ref int generalIndex, ref int floatIndex)
        {
            if (registerClass == RegisterClass.Float)
                return MachineRegisters.GetFloatArgumentRegister(floatIndex++);
            if (registerClass == RegisterClass.General)
                return MachineRegisters.GetIntegerArgumentRegister(generalIndex++);
            return MachineRegister.Invalid;
        }

        private static GenStackKind StackKindForAbi(RuntimeType? type)
            => MachineAbi.StackKindForType(type);

        private static void AddValueCopyRefPositions(
            ImmutableArray<LinearRefPosition>.Builder result,
            GenTreeMethod method,
            IReadOnlyDictionary<GenTree, LinearLiveInterval> intervals,
            int nodeId,
            int usePosition,
            int defPosition,
            int operandIndex,
            GenTree sourceValue,
            GenTree destinationValue)
        {
            var sourceInfo = method.GetValueInfo(sourceValue);
            var destinationInfo = method.GetValueInfo(destinationValue);
            var sourceFlags = GetValueRefFlags(sourceInfo);
            var destinationFlags = GetValueRefFlags(destinationInfo);

            if (IsLastUse(intervals, sourceValue, usePosition))
                sourceFlags |= LinearRefPositionFlags.LastUse;

            var sourceAbi = MachineAbi.ClassifyStorageValue(sourceInfo.Type, sourceInfo.StackKind);
            var destinationAbi = MachineAbi.ClassifyStorageValue(destinationInfo.Type, destinationInfo.StackKind);
            if (sourceAbi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect)
                sourceFlags |= LinearRefPositionFlags.StackOnly | LinearRefPositionFlags.ExposedMemory;
            if (destinationAbi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect)
                destinationFlags |= LinearRefPositionFlags.StackOnly | LinearRefPositionFlags.ExposedMemory;


            if (sourceAbi.PassingKind == AbiValuePassingKind.MultiRegister ||
                destinationAbi.PassingKind == AbiValuePassingKind.MultiRegister)
            {
                if (sourceAbi.PassingKind != AbiValuePassingKind.MultiRegister ||
                    destinationAbi.PassingKind != AbiValuePassingKind.MultiRegister)
                {
                    throw new InvalidOperationException(
                        "Cannot build linear IR ref-positions for a scalar/aggregate copy: " + sourceValue + " -> " + destinationValue + ".");
                }

                var sourceSegments = MachineAbi.GetRegisterSegments(sourceAbi);
                var destinationSegments = MachineAbi.GetRegisterSegments(destinationAbi);
                if (sourceSegments.Length != destinationSegments.Length)
                {
                    throw new InvalidOperationException(
                        "Cannot build linear IR ref-positions for multi-register values with different segment counts: " +
                        sourceValue + " -> " + destinationValue + ".");
                }

                AddSegmentRefPositions(
                    result,
                    nodeId,
                    usePosition,
                    operandIndex,
                    LinearRefPositionKind.Use,
                    sourceValue,
                    sourceSegments,
                    sourceFlags | LinearRefPositionFlags.RequiresRegister,
                    fixedRegisters: default);

                AddSegmentRefPositions(
                    result,
                    nodeId,
                    defPosition,
                    -1 - operandIndex,
                    LinearRefPositionKind.Def,
                    destinationValue,
                    destinationSegments,
                    destinationFlags | LinearRefPositionFlags.RequiresRegister,
                    fixedRegisters: default);
                return;
            }

            result.Add(new LinearRefPosition(
                nodeId,
                usePosition,
                operandIndex,
                LinearRefPositionKind.Use,
                sourceValue,
                sourceInfo.RegisterClass,
                MachineRegister.Invalid,
                sourceFlags));

            result.Add(new LinearRefPosition(
                nodeId,
                defPosition,
                -1 - operandIndex,
                LinearRefPositionKind.Def,
                destinationValue,
                destinationInfo.RegisterClass,
                MachineRegister.Invalid,
                destinationFlags));
        }

        private static void AddDefaultNodeRefPositions(
            ImmutableArray<LinearRefPosition>.Builder result,
            GenTreeMethod method,
            IReadOnlyDictionary<GenTree, LinearLiveInterval> intervals,
            GenTree node,
            int usePosition,
            int defPosition)
        {
            bool registerOnly = node.HasLoweringFlag(GenTreeLinearFlags.RequiresRegisterOperands);

            for (int u = 0; u < node.RegisterUses.Length; u++)
            {
                var value = node.RegisterUses[u];
                var info = method.GetValueInfo(value);
                var abi = MachineAbi.ClassifyStorageValue(info.Type, info.StackKind);
                var flags = GetValueRefFlags(info);
                int operandIndex = GetOperandIndexForRegisterUse(node, u);
                ApplyMemoryUseFlags(node.LinearMemoryAccess, operandIndex, ref flags);

                var operandFlags = GetOperandFlags(node, u);
                bool hardRegisterUse = RequiresRegisterForUse(node, operandIndex, abi, operandFlags);
                if (hardRegisterUse)
                    flags |= LinearRefPositionFlags.RequiresRegister;
                else if (CanUseOperandFromMemory(node, operandIndex, abi, operandFlags))
                    flags |= LinearRefPositionFlags.RegOptional;

                if (IsLastUse(intervals, value, usePosition))
                    flags |= LinearRefPositionFlags.LastUse;

                if (abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect)
                    flags |= LinearRefPositionFlags.StackOnly | LinearRefPositionFlags.ExposedMemory;
                if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    AddSegmentRefPositions(
                        result,
                        node.LinearId,
                        usePosition,
                        u,
                        LinearRefPositionKind.Use,
                        value,
                        MachineAbi.GetRegisterSegments(abi),
                        flags,
                        fixedRegisters: default);
                    continue;
                }

                result.Add(new LinearRefPosition(
                    node.LinearId,
                    usePosition,
                    u,
                    LinearRefPositionKind.Use,
                    value,
                    info.RegisterClass,
                    MachineRegister.Invalid,
                    flags));
            }

            if (node.RegisterResult is not null)
            {
                var value = node.RegisterResult;
                var info = method.GetValueInfo(value);
                var flags = GetValueRefFlags(info);
                var abi = MachineAbi.ClassifyStorageValue(info.Type, info.StackKind);
                if (RequiresRegisterForDefinition(node, abi, registerOnly))
                    flags |= LinearRefPositionFlags.RequiresRegister;

                if (abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect)
                    flags |= LinearRefPositionFlags.StackOnly | LinearRefPositionFlags.ExposedMemory;
                if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    AddSegmentRefPositions(
                        result,
                        node.LinearId,
                        defPosition,
                        -1,
                        LinearRefPositionKind.Def,
                        value,
                        MachineAbi.GetRegisterSegments(abi),
                        flags,
                        fixedRegisters: default);
                    return;
                }

                result.Add(new LinearRefPosition(
                    node.LinearId,
                    defPosition,
                    -1,
                    LinearRefPositionKind.Def,
                    value,
                    info.RegisterClass,
                    MachineRegister.Invalid,
                    flags));
            }
        }

        private static LirOperandFlags GetOperandFlags(GenTree node, int registerUseIndex)
        {
            if (node.OperandFlags.IsDefaultOrEmpty || node.Operands.IsDefaultOrEmpty)
                return LirOperandFlags.None;

            int seenRegisterUses = 0;
            int limit = Math.Min(node.Operands.Length, node.OperandFlags.Length);
            for (int i = 0; i < limit; i++)
            {
                var flags = node.OperandFlags[i];
                if ((flags & LirOperandFlags.Contained) != 0)
                    continue;

                if (seenRegisterUses == registerUseIndex)
                    return flags;

                seenRegisterUses++;
            }

            return LirOperandFlags.None;
        }

        private static int GetOperandIndexForRegisterUse(GenTree node, int registerUseIndex)
        {
            if (node.Operands.IsDefaultOrEmpty)
                return registerUseIndex;

            int seenRegisterUses = 0;
            int flagCount = node.OperandFlags.IsDefaultOrEmpty ? 0 : node.OperandFlags.Length;
            for (int i = 0; i < node.Operands.Length; i++)
            {
                var flags = i < flagCount ? node.OperandFlags[i] : LirOperandFlags.None;
                if ((flags & LirOperandFlags.Contained) != 0)
                    continue;

                if (seenRegisterUses == registerUseIndex)
                    return i;

                seenRegisterUses++;
            }

            return registerUseIndex;
        }

        private static bool RequiresRegisterForUse(GenTree node, int operandIndex, AbiValueInfo abi, LirOperandFlags operandFlags)
        {
            if (node.HasLoweringFlag(GenTreeLinearFlags.RequiresRegisterOperands))
                return true;

            if (node.LinearMemoryAccess.HasAddressOperand(operandIndex))
                return true;

            if ((operandFlags & LirOperandFlags.RegOptional) != 0 && CanUseOperandFromMemory(node, operandIndex, abi, operandFlags))
                return false;

            return !CanUseOperandFromMemory(node, operandIndex, abi, operandFlags);
        }

        private static bool CanUseOperandFromMemory(GenTree node, int operandIndex, AbiValueInfo abi, LirOperandFlags operandFlags)
        {
            if ((operandFlags & LirOperandFlags.RegOptional) != 0)
                return true;

            if (!node.LinearMemoryAccess.HasValueOperand(operandIndex))
                return false;

            if (abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect)
                return true;

            if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                return true;

            return false;
        }

        private static bool RequiresRegisterForDefinition(GenTree node, AbiValueInfo abi, bool registerOnly)
        {
            if (registerOnly)
                return true;

            if (abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect or AbiValuePassingKind.MultiRegister)
                return false;

            return node.Kind switch
            {
                GenTreeKind.Local => false,
                GenTreeKind.Arg => false,
                GenTreeKind.Temp => false,
                GenTreeKind.TempAddr => false,
                GenTreeKind.StoreLocal => false,
                GenTreeKind.StoreArg => false,
                GenTreeKind.StoreTemp => false,
                GenTreeKind.DefaultValue => false,
                _ => true,
            };
        }

        private static void ApplyMemoryUseFlags(LinearMemoryAccess memory, int operandIndex, ref LinearRefPositionFlags flags)
        {
            if (memory.IsNone)
                return;

            if (memory.HasAddressOperand(operandIndex))
            {
                flags |= LinearRefPositionFlags.Address | LinearRefPositionFlags.DelayFree;
                if (memory.IsAddressProducer || memory.Reads || memory.Writes)
                    flags |= LinearRefPositionFlags.ExposedMemory;
            }

            if (memory.HasValueOperand(operandIndex))
            {
                if (memory.IsBlockCopy)
                    flags |= LinearRefPositionFlags.StackOnly | LinearRefPositionFlags.ExposedMemory;
                if ((memory.Flags & LinearMemoryAccessFlags.RequiresWriteBarrier) != 0)
                    flags |= LinearRefPositionFlags.WriteBarrier;
            }
        }

        private static void AddSegmentRefPositions(
            ImmutableArray<LinearRefPosition>.Builder result,
            int nodeId,
            int position,
            int operandIndex,
            LinearRefPositionKind kind,
            GenTree value,
            ImmutableArray<AbiRegisterSegment> segments,
            LinearRefPositionFlags flags,
            ReadOnlySpan<MachineRegister> fixedRegisters)
        {
            for (int s = 0; s < segments.Length; s++)
            {
                var segment = segments[s];
                MachineRegister fixedRegister = s < fixedRegisters.Length ? fixedRegisters[s] : MachineRegister.Invalid;
                var segmentFlags = segment.ContainsGcPointers
                    ? flags | LinearRefPositionFlags.GcRef
                    : flags & ~LinearRefPositionFlags.GcRef;

                result.Add(new LinearRefPosition(
                    nodeId,
                    position,
                    operandIndex >= 0 ? operandIndex : -1 - s,
                    s,
                    segment.Offset,
                    segment.Size,
                    kind,
                    value,
                    segment.RegisterClass,
                    fixedRegister,
                    segmentFlags));
            }
        }

        private static LinearRefPositionFlags WithSegmentGcFlags(LinearRefPositionFlags flags, AbiRegisterSegment segment)
        {
            return segment.ContainsGcPointers
                ? flags | LinearRefPositionFlags.GcRef
                : flags & ~LinearRefPositionFlags.GcRef;
        }

        private static void AddCallRefPositions(
            ImmutableArray<LinearRefPosition>.Builder result,
            GenTreeMethod method,
            IReadOnlyDictionary<GenTree, LinearLiveInterval> intervals,
            GenTree node,
            int usePosition,
            int defPosition)
        {
            var descriptor = MachineAbi.BuildCallDescriptor(
                node.RegisterUses,
                method.GetValueInfo,
                node.RegisterResult,
                node.Method,
                node.Kind == GenTreeKind.NewObject);

            for (int i = 0; i < descriptor.ArgumentSegments.Length; i++)
            {
                var segment = descriptor.ArgumentSegments[i];
                if (segment.IsHiddenReturnBuffer)
                    continue;

                var value = segment.Value;
                var info = method.GetValueInfo(value);
                var flags = GetValueRefFlags(info);
                if (IsLastUse(intervals, value, usePosition))
                    flags |= LinearRefPositionFlags.LastUse;

                if (segment.IsRegister)
                    flags |= LinearRefPositionFlags.FixedRegister | LinearRefPositionFlags.RequiresRegister;

                if (segment.IsAbiSegment)
                    flags = WithSegmentGcFlags(flags, segment.ToRegisterSegment());
                else if (segment.ContainsGcPointers)
                    flags |= LinearRefPositionFlags.GcRef;

                result.Add(new LinearRefPosition(
                    node.LinearId,
                    usePosition,
                    segment.OperandIndex,
                    segment.IsAbiSegment ? segment.SegmentIndex : -1,
                    segment.Offset,
                    segment.Size,
                    LinearRefPositionKind.Use,
                    value,
                    segment.RegisterClass,
                    segment.IsRegister ? segment.Location.Register : MachineRegister.Invalid,
                    flags));
            }

            if (node.RegisterResult is not null)
            {
                var value = node.RegisterResult;
                var info = method.GetValueInfo(value);
                AddReturnDefRefPositions(result, node.LinearId, defPosition, value, info, descriptor.ReturnAbi);
            }
        }

        private static void AddReturnRefPositions(
            ImmutableArray<LinearRefPosition>.Builder result,
            GenTreeMethod method,
            IReadOnlyDictionary<GenTree, LinearLiveInterval> intervals,
            GenTree node,
            int usePosition)
        {
            if (node.RegisterUses.Length == 0)
                return;

            if (node.RegisterUses.Length != 1)
                throw new InvalidOperationException("Return GenTree GenTree LIR node must have zero or one value use.");

            var value = node.RegisterUses[0];
            var info = method.GetValueInfo(value);
            var abi = MachineAbi.ClassifyValue(info.Type, info.StackKind, isReturn: true);
            var flags = GetValueRefFlags(info);
            if (IsLastUse(intervals, value, usePosition))
                flags |= LinearRefPositionFlags.LastUse;

            if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
            {
                var reg = abi.RegisterClass == RegisterClass.Float
                    ? MachineRegisters.FloatReturnValue0
                    : MachineRegisters.ReturnValue0;
                result.Add(new LinearRefPosition(
                    node.LinearId,
                    usePosition,
                    0,
                    LinearRefPositionKind.Use,
                    value,
                    abi.RegisterClass,
                    reg,
                    flags | LinearRefPositionFlags.FixedRegister | LinearRefPositionFlags.RequiresRegister));
                return;
            }

            if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
            {
                var segments = MachineAbi.GetRegisterSegments(abi);
                int generalRet = 0;
                int floatRet = 0;
                for (int i = 0; i < segments.Length; i++)
                {
                    var segment = segments[i];
                    var reg = GetReturnRegister(segment.RegisterClass, ref generalRet, ref floatRet);
                    result.Add(new LinearRefPosition(
                        node.LinearId,
                        usePosition,
                        i,
                        i,
                        segment.Offset,
                        segment.Size,
                        LinearRefPositionKind.Use,
                        value,
                        segment.RegisterClass,
                        reg,
                        WithSegmentGcFlags(flags | LinearRefPositionFlags.FixedRegister | LinearRefPositionFlags.RequiresRegister, segment)));
                }
                return;
            }

            result.Add(new LinearRefPosition(
                node.LinearId,
                usePosition,
                0,
                LinearRefPositionKind.Use,
                value,
                info.RegisterClass,
                MachineRegister.Invalid,
                flags));
        }

        private static void AddReturnDefRefPositions(
            ImmutableArray<LinearRefPosition>.Builder result,
            int nodeId,
            int defPosition,
            GenTree value,
            GenTreeValueInfo info,
            AbiValueInfo abi)
        {
            var flags = GetValueRefFlags(info);

            if (abi.PassingKind == AbiValuePassingKind.Void)
                return;

            if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
            {
                var reg = abi.RegisterClass == RegisterClass.Float
                    ? MachineRegisters.FloatReturnValue0
                    : MachineRegisters.ReturnValue0;
                result.Add(new LinearRefPosition(
                    nodeId,
                    defPosition,
                    -1,
                    LinearRefPositionKind.Def,
                    value,
                    abi.RegisterClass,
                    reg,
                    flags | LinearRefPositionFlags.FixedRegister | LinearRefPositionFlags.RequiresRegister));
                return;
            }

            if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
            {
                var segments = MachineAbi.GetRegisterSegments(abi);
                int generalRet = 0;
                int floatRet = 0;
                for (int i = 0; i < segments.Length; i++)
                {
                    var segment = segments[i];
                    var reg = GetReturnRegister(segment.RegisterClass, ref generalRet, ref floatRet);
                    result.Add(new LinearRefPosition(
                        nodeId,
                        defPosition,
                        -1 - i,
                        i,
                        segment.Offset,
                        segment.Size,
                        LinearRefPositionKind.Def,
                        value,
                        segment.RegisterClass,
                        reg,
                        WithSegmentGcFlags(flags | LinearRefPositionFlags.FixedRegister | LinearRefPositionFlags.RequiresRegister, segment)));
                }
                return;
            }

            result.Add(new LinearRefPosition(
                nodeId,
                defPosition,
                -1,
                LinearRefPositionKind.Def,
                value,
                info.RegisterClass,
                MachineRegister.Invalid,
                flags));
        }

        private static MachineRegister GetReturnRegister(RegisterClass registerClass, ref int generalIndex, ref int floatIndex)
        {
            if (registerClass == RegisterClass.Float)
            {
                int index = floatIndex++;
                return index switch
                {
                    0 => MachineRegisters.FloatReturnValue0,
                    1 => MachineRegisters.FloatReturnValue1,
                    2 => MachineRegisters.FloatReturnValue2,
                    3 => MachineRegisters.FloatReturnValue3,
                    _ => MachineRegister.Invalid,
                };
            }

            if (registerClass == RegisterClass.General)
            {
                int index = generalIndex++;
                return index switch
                {
                    0 => MachineRegisters.ReturnValue0,
                    1 => MachineRegisters.ReturnValue1,
                    2 => MachineRegisters.ReturnValue2,
                    3 => MachineRegisters.ReturnValue3,
                    _ => MachineRegister.Invalid,
                };
            }

            return MachineRegister.Invalid;
        }

        private static void AddInternalRegisterRefPositions(
            ImmutableArray<LinearRefPosition>.Builder result,
            GenTree node,
            int position)
        {
            if (node.LinearLowering.InternalGeneralRegisters != 0)
            {
                result.Add(new LinearRefPosition(
                    node.LinearId,
                    position,
                    -1,
                    LinearRefPositionKind.Internal,
                    value: null,
                    RegisterClass.General,
                    MachineRegister.Invalid,
                    LinearRefPositionFlags.Internal | LinearRefPositionFlags.RequiresRegister,
                    MachineRegisters.DefaultMaskForClass(RegisterClass.General),
                    node.LinearLowering.InternalGeneralRegisters));
            }

            if (node.LinearLowering.InternalFloatRegisters != 0)
            {
                result.Add(new LinearRefPosition(
                    node.LinearId,
                    position,
                    -2,
                    LinearRefPositionKind.Internal,
                    value: null,
                    RegisterClass.Float,
                    MachineRegister.Invalid,
                    LinearRefPositionFlags.Internal | LinearRefPositionFlags.RequiresRegister,
                    MachineRegisters.DefaultMaskForClass(RegisterClass.Float),
                    node.LinearLowering.InternalFloatRegisters));
            }
        }

        private static void AddRegisterKills(
            ImmutableArray<LinearRefPosition>.Builder result,
            GenTree node,
            int position,
            ImmutableArray<MachineRegister> registers)
        {
            ulong killMask = MachineRegisters.MaskOf(registers);
            if (killMask == 0)
                return;

            result.Add(new LinearRefPosition(
                node.LinearId,
                position,
                -1,
                LinearRefPositionKind.Kill,
                value: null,
                RegisterClass.Invalid,
                MachineRegister.Invalid,
                LinearRefPositionFlags.Internal,
                killMask));
        }

        private static LinearRefPositionFlags GetValueRefFlags(GenTreeValueInfo info)
        {
            LinearRefPositionFlags flags = LinearRefPositionFlags.None;

            if (info.Type is not null)
            {
                if (info.Type.Kind == RuntimeTypeKind.ByRef)
                    flags |= LinearRefPositionFlags.ByRef;
                else if (info.Type.IsReferenceType || info.Type.Kind == RuntimeTypeKind.TypeParam)
                    flags |= LinearRefPositionFlags.GcRef;
                else if (info.Type.IsValueType && info.StackKind == GenStackKind.Value)
                    flags |= LinearRefPositionFlags.StructByValue;
            }

            if (info.StackKind == GenStackKind.Ref || info.StackKind == GenStackKind.Null)
                flags |= LinearRefPositionFlags.GcRef;
            else if (info.StackKind == GenStackKind.ByRef)
                flags |= LinearRefPositionFlags.ByRef;
            else if (info.StackKind == GenStackKind.Value)
                flags |= LinearRefPositionFlags.StructByValue;

            return flags;
        }

        private static bool IsLastUse(IReadOnlyDictionary<GenTree, LinearLiveInterval> intervals, GenTree value, int usePosition)
        {
            if (!intervals.TryGetValue(value, out var interval) || interval.UsePositions.Length == 0)
                return false;

            return interval.UsePositions[interval.UsePositions.Length - 1] == usePosition;
        }

        private static void MarkUnusedValueDefinitions(
            GenTreeMethod method,
            IReadOnlyDictionary<GenTree, LinearLiveInterval> intervals)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (intervals is null)
                throw new ArgumentNullException(nameof(intervals));

            foreach (var node in method.LinearNodes)
            {
                if (!node.HasLoweringFlag(GenTreeLinearFlags.UnusedValue))
                    continue;

                node.LinearLowering = new GenTreeLinearLoweringInfo(
                    node.LinearLowering.Flags & ~GenTreeLinearFlags.UnusedValue,
                    node.LinearLowering.InternalGeneralRegisters,
                    node.LinearLowering.InternalFloatRegisters);
            }

            var unusedValues = new HashSet<GenTreeValueKey>();
            for (int i = 0; i < method.Values.Length; i++)
            {
                var value = method.Values[i].RepresentativeNode;
                if (!intervals.TryGetValue(value, out var interval) || interval.UsePositions.Length == 0)
                    unusedValues.Add(method.Values[i].Value);
            }

            if (unusedValues.Count == 0)
                return;

            foreach (var node in method.LinearNodes)
            {
                if (node.RegisterResults.Length == 0)
                    continue;

                bool allResultsUnused = true;
                for (int r = 0; r < node.RegisterResults.Length; r++)
                {
                    if (!unusedValues.Contains(node.RegisterResults[r].LinearValueKey))
                    {
                        allResultsUnused = false;
                        break;
                    }
                }

                if (!allResultsUnused)
                    continue;

                node.LinearLowering = new GenTreeLinearLoweringInfo(
                    node.LinearLowering.Flags | GenTreeLinearFlags.UnusedValue,
                    node.LinearLowering.InternalGeneralRegisters,
                    node.LinearLowering.InternalFloatRegisters);
            }
        }

        public static ImmutableArray<LinearLiveInterval> BuildIntervals(
            GenTreeMethod method,
            PositionLayout layout,
            out IReadOnlyDictionary<GenTree, LinearLiveInterval> intervalMap)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            var liveIn = NewSetArray(method.Blocks.Length);
            var liveOut = NewSetArray(method.Blocks.Length);
            var blockUses = NewSetArray(method.Blocks.Length);
            var blockDefs = NewSetArray(method.Blocks.Length);
            var localUseEnds = NewPositionMapArray(method.Blocks.Length);
            var localDefStarts = NewPositionMapArray(method.Blocks.Length);
            var usePositions = new Dictionary<GenTree, SortedSet<int>>();
            var defPositions = new Dictionary<GenTree, int>();
            var phiCopies = new List<GenTree>();
            var ssaValueNodes = new Dictionary<SsaValueName, GenTree>();

            for (int i = 0; i < method.Values.Length; i++)
            {
                var info = method.Values[i];
                var value = info.RepresentativeNode;
                usePositions[value] = new SortedSet<int>();
                defPositions[value] = ComputeInitialDefinitionPosition(layout, info);

                if (info.Value.IsSsaValue)
                    ssaValueNodes[new SsaValueName(info.Value.SsaSlot, info.Value.SsaVersion)] = value;
            }

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var block = method.Blocks[b];
                for (int n = 0; n < block.LinearNodes.Length; n++)
                {
                    var node = block.LinearNodes[n];
                    int usePos = layout.NodePositions[node.LinearId];
                    int defPos = usePos + 1;

                    if (node.IsPhiCopy)
                    {
                        int groupEnd = n;
                        while (groupEnd + 1 < block.LinearNodes.Length && SamePhiCopyGroup(node, block.LinearNodes[groupEnd + 1]))
                            groupEnd++;

                        for (int i = n; i <= groupEnd; i++)
                            phiCopies.Add(block.LinearNodes[i]);

                        n = groupEnd;
                        continue;
                    }

                    bool hasDef = node.RegisterResult is not null;
                    for (int u = 0; u < node.RegisterUses.Length; u++)
                    {
                        var use = node.RegisterUses[u];
                        int useEnd = ComputeUseEnd(node, u, usePos, defPos, hasDef);
                        RecordUse(usePositions, localUseEnds[b], use, usePos, useEnd);
                        if (!blockDefs[b].Contains(use))
                            blockUses[b].Add(use);
                    }

                    if (hasDef)
                    {
                        var resultValue = node.RegisterResult;
                        if (resultValue is null)
                            throw new InvalidOperationException("Linear node was marked as defining a register result, but no result value is attached.");
                        RecordDefinition(blockDefs[b], localDefStarts[b], defPositions, resultValue, defPos);
                    }

                }
            }

            for (int i = 0; i < phiCopies.Count; i++)
            {
                var copy = phiCopies[i];
                if (copy.RegisterResult is null || copy.RegisterUses.Length != 1)
                    throw new InvalidOperationException("linear IR phi copy node must have one source and one destination value.");

                int fromBlockId = copy.LinearPhiCopyFromBlockId;
                int toBlockId = copy.LinearPhiCopyToBlockId;
                if ((uint)fromBlockId >= (uint)method.Blocks.Length ||
                    (uint)toBlockId >= (uint)method.Blocks.Length)
                {
                    throw new InvalidOperationException($"linear IR phi copy node {copy.LinearId} has invalid edge B{fromBlockId}->B{toBlockId}.");
                }

                var sourceValue = copy.RegisterUses[0];
                int usePosition = ComputePhiCopySourcePosition(layout, copy);
                RecordUse(usePositions, localUseEnds[fromBlockId], sourceValue, usePosition, usePosition + 1);
                if (!blockDefs[fromBlockId].Contains(sourceValue))
                    blockUses[fromBlockId].Add(sourceValue);

                var destinationValue = copy.RegisterResult;
                int definitionPosition = ComputePhiCopyDestinationPosition(layout, copy);
                RecordDefinition(blockDefs[toBlockId], localDefStarts[toBlockId], defPositions, destinationValue, definitionPosition);

                blockUses[toBlockId].Remove(destinationValue);
            }

            RecordIdentityPhiInputUses(
                method,
                layout,
                ssaValueNodes,
                usePositions,
                localUseEnds,
                blockUses,
                blockDefs);

            var dataflowOrder = method.LinearBlockOrder.IsDefaultOrEmpty
                ? LinearBlockOrder.Compute(method.Cfg)
                : method.LinearBlockOrder;
            var newOut = new HashSet<GenTree>();
            var newIn = new HashSet<GenTree>();
            bool changed;
            do
            {
                changed = false;
                for (int r = dataflowOrder.Length - 1; r >= 0; r--)
                {
                    int blockId = dataflowOrder[r];
                    newOut.Clear();
                    var successors = method.Cfg.Blocks[blockId].Successors;
                    for (int s = 0; s < successors.Length; s++)
                        newOut.UnionWith(liveIn[successors[s].ToBlockId]);

                    newIn.Clear();
                    newIn.UnionWith(newOut);
                    newIn.ExceptWith(blockDefs[blockId]);
                    newIn.UnionWith(blockUses[blockId]);

                    if (!SetEquals(liveOut[blockId], newOut))
                    {
                        liveOut[blockId].Clear();
                        liveOut[blockId].UnionWith(newOut);
                        changed = true;
                    }

                    if (!SetEquals(liveIn[blockId], newIn))
                    {
                        liveIn[blockId].Clear();
                        liveIn[blockId].UnionWith(newIn);
                        changed = true;
                    }
                }
            }
            while (changed);

            var ranges = new Dictionary<GenTree, List<LinearLiveRange>>();
            for (int i = 0; i < method.Values.Length; i++)
                ranges[method.Values[i].RepresentativeNode] = new List<LinearLiveRange>();

            var blockValues = new HashSet<GenTree>();
            for (int b = 0; b < method.Blocks.Length; b++)
            {
                int blockStart = layout.BlockStartPositions[b];
                int blockEnd = layout.BlockEndPositions[b] + 1;
                blockValues.Clear();
                blockValues.UnionWith(liveIn[b]);
                blockValues.UnionWith(liveOut[b]);
                foreach (var kv in localUseEnds[b])
                    blockValues.Add(kv.Key);
                foreach (var kv in localDefStarts[b])
                    blockValues.Add(kv.Key);

                foreach (var value in blockValues)
                {
                    bool isLiveIn = liveIn[b].Contains(value);
                    bool isLiveOut = liveOut[b].Contains(value);
                    bool hasLocalDef = localDefStarts[b].TryGetValue(value, out int defStart);
                    bool hasLocalUse = localUseEnds[b].TryGetValue(value, out int useEnd);

                    int start;
                    if (isLiveIn)
                        start = blockStart;
                    else if (hasLocalDef)
                        start = defStart;
                    else if (hasLocalUse)
                        start = blockStart;
                    else
                        continue;

                    int end;
                    if (isLiveOut)
                        end = blockEnd;
                    else if (hasLocalUse)
                        end = useEnd;
                    else
                        continue; // Dead local def

                    AddRange(ranges, value, start, end);
                }
            }

            var result = ImmutableArray.CreateBuilder<LinearLiveInterval>(method.Values.Length);
            var map = new Dictionary<GenTree, LinearLiveInterval>();
            for (int i = 0; i < method.Values.Length; i++)
            {
                var value = method.Values[i].RepresentativeNode;
                var mergedRanges = MergeRanges(ranges[value]);
                var uses = usePositions.TryGetValue(value, out var positions)
                    ? positions.ToImmutableArray()
                    : ImmutableArray<int>.Empty;
                int def = defPositions.TryGetValue(value, out var defPos) ? defPos : layout.FirstPosition;
                var interval = new LinearLiveInterval(value, mergedRanges, uses, def);
                VerifyTreeTempInvariant(method, layout, method.Values[i], interval);
                result.Add(interval);
                map[value] = interval;
            }

            intervalMap = map;
            return result.ToImmutable();
        }



        private static void VerifyTreeTempInvariant(
            GenTreeMethod method,
            PositionLayout layout,
            GenTreeValueInfo info,
            LinearLiveInterval interval)
        {
            if (!info.Value.IsTreeNode)
                return;

            if (interval.UsePositions.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Tree temp {info.Value} has {interval.UsePositions.Length} distinct use positions. " +
                    "Tree temps must be single-def/single-use; use a local descriptor or SSA temp for duplicated values.");
            }

            if (interval.Ranges.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Tree temp {info.Value} has multiple live ranges. " +
                    "Tree temps cannot have lifetime holes or CFG liveness; use a local descriptor or SSA temp.");
            }

            if (interval.Ranges.Length == 0)
                return;

            int definitionBlockId = info.DefinitionBlockId;
            if ((uint)definitionBlockId >= (uint)method.Blocks.Length)
            {
                throw new InvalidOperationException(
                    $"Tree temp {info.Value} has no valid definition block. " +
                    "Only local descriptors / SSA values may be live without a concrete tree definition.");
            }

            var range = interval.Ranges[0];
            int blockStart = layout.BlockStartPositions[definitionBlockId];
            int blockEndExclusive = layout.BlockEndPositions[definitionBlockId] + 1;
            if (range.Start < blockStart || range.End > blockEndExclusive)
            {
                throw new InvalidOperationException(
                    $"Tree temp {info.Value} is live outside B{definitionBlockId}: {range}. " +
                    "Tree temps must not cross basic-block boundaries; materialize the value as a local descriptor / SSA temp.");
            }
        }

        private static bool SamePhiCopyGroup(GenTree left, GenTree right)
            => left.IsPhiCopy &&
               right.IsPhiCopy &&
               left.LinearPhiCopyFromBlockId == right.LinearPhiCopyFromBlockId &&
               left.LinearPhiCopyToBlockId == right.LinearPhiCopyToBlockId;

        private static void RecordIdentityPhiInputUses(
            GenTreeMethod method,
            PositionLayout layout,
            IReadOnlyDictionary<SsaValueName, GenTree> ssaValueNodes,
            Dictionary<GenTree, SortedSet<int>> usePositions,
            Dictionary<GenTree, int>[] localUseEnds,
            HashSet<GenTree>[] blockUses,
            HashSet<GenTree>[] blockDefs)
        {
            var ssa = method.Ssa;
            if (ssa is null)
                return;

            for (int b = 0; b < ssa.Blocks.Length; b++)
            {
                var block = ssa.Blocks[b];
                for (int p = 0; p < block.Phis.Length; p++)
                {
                    var phi = block.Phis[p];
                    if (!ssaValueNodes.TryGetValue(phi.Target, out var targetValue))
                        continue;

                    for (int i = 0; i < phi.Inputs.Length; i++)
                    {
                        var input = phi.Inputs[i];
                        if (!input.Value.Equals(phi.Target))
                            continue;

                        int fromBlockId = input.PredecessorBlockId;
                        if ((uint)fromBlockId >= (uint)method.Blocks.Length)
                            throw new InvalidOperationException($"SSA phi input references invalid predecessor B{fromBlockId}.");

                        int usePosition = ComputePhiInputSourcePosition(layout, fromBlockId);
                        int useEnd = layout.BlockEndPositions[fromBlockId] + 1;
                        RecordUse(usePositions, localUseEnds[fromBlockId], targetValue, usePosition, useEnd);

                        if (!blockDefs[fromBlockId].Contains(targetValue))
                            blockUses[fromBlockId].Add(targetValue);
                    }
                }
            }
        }

        private static int ComputeInitialDefinitionPosition(PositionLayout layout, GenTreeValueInfo info)
        {
            if (info.DefinitionNodeId >= 0 && layout.NodePositions.TryGetValue(info.DefinitionNodeId, out int nodePos))
                return nodePos + 1;

            if ((uint)info.DefinitionBlockId < (uint)layout.BlockStartPositions.Length)
                return layout.BlockStartPositions[info.DefinitionBlockId];

            return layout.FirstPosition;
        }

        private static int ComputePhiCopySourcePosition(PositionLayout layout, GenTree node)
        {
            if (!node.IsPhiCopy)
                throw new InvalidOperationException("Expected a phi copy node.");
            if ((uint)node.LinearPhiCopyFromBlockId >= (uint)layout.BlockEndPositions.Length)
                throw new InvalidOperationException($"Invalid phi-copy source block B{node.LinearPhiCopyFromBlockId} for node {node.LinearId}.");

            return ComputePhiInputSourcePosition(layout, node.LinearPhiCopyFromBlockId);
        }

        private static int ComputePhiInputSourcePosition(PositionLayout layout, int fromBlockId)
        {
            int blockEnd = layout.BlockEndPositions[fromBlockId];
            int blockStart = layout.BlockStartPositions[fromBlockId];
            return blockEnd > blockStart ? blockEnd - 1 : blockStart;
        }

        private static int ComputePhiCopyDestinationPosition(PositionLayout layout, GenTree node)
        {
            if (!node.IsPhiCopy)
                throw new InvalidOperationException("Expected a phi copy node.");
            if ((uint)node.LinearPhiCopyToBlockId >= (uint)layout.BlockStartPositions.Length)
                throw new InvalidOperationException($"Invalid phi-copy destination block B{node.LinearPhiCopyToBlockId} for node {node.LinearId}.");

            return layout.BlockStartPositions[node.LinearPhiCopyToBlockId];
        }

        private static int ComputeUseEnd(GenTree node, int operandIndex, int usePosition, int defPosition, bool hasDef)
        {
            int end = usePosition + 1;

            if (hasDef && UseRequiresDelayedFree(node, operandIndex))
                end = defPosition + 1;

            return end;
        }

        private static bool UseRequiresDelayedFree(GenTree node, int operandIndex)
            => node.LinearMemoryAccess.HasAddressOperand(operandIndex);

        private static void RecordUse(
            Dictionary<GenTree, SortedSet<int>> allUsePositions,
            Dictionary<GenTree, int> blockUseEnds,
            GenTree value,
            int position,
            int end)
        {
            AddUsePosition(allUsePositions, value, position);
            if (!blockUseEnds.TryGetValue(value, out int current) || end > current)
                blockUseEnds[value] = end;
        }

        private static void RecordDefinition(
            HashSet<GenTree> blockDefs,
            Dictionary<GenTree, int> blockDefStarts,
            Dictionary<GenTree, int> allDefPositions,
            GenTree value,
            int position)
        {
            blockDefs.Add(value);
            SetEarliestDef(allDefPositions, value, position);
            if (!blockDefStarts.TryGetValue(value, out int current) || position < current)
                blockDefStarts[value] = position;
        }

        private static void AddRange(Dictionary<GenTree, List<LinearLiveRange>> ranges, GenTree value, int start, int end)
        {
            if (end <= start)
                return;

            if (!ranges.TryGetValue(value, out var list))
            {
                list = new List<LinearLiveRange>();
                ranges[value] = list;
            }
            list.Add(new LinearLiveRange(start, end));
        }

        private static ImmutableArray<LinearLiveRange> MergeRanges(List<LinearLiveRange> ranges)
        {
            if (ranges.Count == 0)
                return ImmutableArray<LinearLiveRange>.Empty;

            ranges.Sort(static (a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));

            var merged = ImmutableArray.CreateBuilder<LinearLiveRange>();

            int start = ranges[0].Start;
            int end = ranges[0].End;

            for (int i = 1; i < ranges.Count; i++)
            {
                var range = ranges[i];

                if (range.Start <= end)
                {
                    if (range.End > end)
                        end = range.End;
                    continue;
                }

                merged.Add(new LinearLiveRange(start, end));
                start = range.Start;
                end = range.End;
            }

            merged.Add(new LinearLiveRange(start, end));

            return merged.ToImmutable();
        }

        private static HashSet<GenTree>[] NewSetArray(int count)
        {
            var result = new HashSet<GenTree>[count];
            for (int i = 0; i < result.Length; i++)
                result[i] = new HashSet<GenTree>();
            return result;
        }

        private static Dictionary<GenTree, int>[] NewPositionMapArray(int count)
        {
            var result = new Dictionary<GenTree, int>[count];
            for (int i = 0; i < result.Length; i++)
                result[i] = new Dictionary<GenTree, int>();
            return result;
        }

        private static bool SetEquals(HashSet<GenTree> left, HashSet<GenTree> right)
            => left.Count == right.Count && left.SetEquals(right);

        private static void AddUsePosition(Dictionary<GenTree, SortedSet<int>> positions, GenTree value, int position)
        {
            if (!positions.TryGetValue(value, out var set))
            {
                set = new SortedSet<int>();
                positions[value] = set;
            }
            set.Add(position);
        }

        private static void SetEarliestDef(Dictionary<GenTree, int> definitions, GenTree value, int position)
        {
            if (!definitions.TryGetValue(value, out int current) || position < current)
                definitions[value] = position;
        }

        public sealed class PositionLayout
        {
            public Dictionary<int, int> NodePositions { get; }
            public int[] BlockStartPositions { get; }
            public int[] BlockEndPositions { get; }
            public int FirstPosition { get; }

            private PositionLayout(Dictionary<int, int> nodePositions, int[] blockStartPositions, int[] blockEndPositions, int firstPosition)
            {
                NodePositions = nodePositions;
                BlockStartPositions = blockStartPositions;
                BlockEndPositions = blockEndPositions;
                FirstPosition = firstPosition;
            }

            public static PositionLayout Build(GenTreeMethod method)
            {
                var nodePositions = new Dictionary<int, int>();
                var starts = new int[method.Blocks.Length];
                var ends = new int[method.Blocks.Length];
                int position = 0;

                var order = method.LinearBlockOrder.IsDefaultOrEmpty
                    ? LinearBlockOrder.Compute(method.Cfg)
                    : method.LinearBlockOrder;

                for (int o = 0; o < order.Length; o++)
                {
                    int b = order[o];
                    starts[b] = position;
                    var nodes = method.Blocks[b].LinearNodes;
                    for (int n = 0; n < nodes.Length; n++)
                    {
                        var node = nodes[n];
                        nodePositions[node.LinearId] = position;

                        if (node.IsPhiCopy)
                        {
                            while (n + 1 < nodes.Length && SamePhiCopyGroup(node, nodes[n + 1]))
                            {
                                n++;
                                nodePositions[nodes[n].LinearId] = position;
                            }
                        }

                        position += 2;
                    }

                    ends[b] = position;
                    position += 2;
                }

                return new PositionLayout(nodePositions, starts, ends, firstPosition: 0);
            }

            private static bool SamePhiCopyGroup(GenTree left, GenTree right)
                => left.IsPhiCopy &&
                   right.IsPhiCopy &&
                   left.LinearPhiCopyFromBlockId == right.LinearPhiCopyFromBlockId &&
                   left.LinearPhiCopyToBlockId == right.LinearPhiCopyToBlockId;
        }
    }
}
