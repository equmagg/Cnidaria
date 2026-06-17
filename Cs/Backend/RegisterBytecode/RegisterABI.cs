using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;

namespace Cnidaria.Cs
{
    internal enum AbiValuePassingKind : byte
    {
        Void,
        ScalarRegister,
        MultiRegister,
        Stack,
        Indirect,
    }
    internal readonly struct AbiRegisterSegment
    {
        public readonly RegisterClass RegisterClass;
        public readonly int Offset;
        public readonly int Size;
        public readonly bool ContainsGcPointers;

        public AbiRegisterSegment(RegisterClass registerClass, int offset, int size, bool containsGcPointers)
        {
            if (registerClass == RegisterClass.Invalid)
                throw new ArgumentOutOfRangeException(nameof(registerClass));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size));

            RegisterClass = registerClass;
            Offset = offset;
            Size = size;
            ContainsGcPointers = containsGcPointers;
        }

        public override string ToString()
            => RegisterClass + "@+" + Offset.ToString() + ":" + Size.ToString() + (ContainsGcPointers ? " gc" : string.Empty);
    }
    internal readonly struct AbiValueInfo
    {
        public readonly AbiValuePassingKind PassingKind;
        public readonly RegisterClass RegisterClass;
        public readonly int Size;
        public readonly int Alignment;
        public readonly bool ContainsGcPointers;
        public readonly ImmutableArray<AbiRegisterSegment> RegisterSegments;

        public AbiValueInfo(
            AbiValuePassingKind passingKind,
            RegisterClass registerClass,
            int size,
            int alignment,
            bool containsGcPointers,
            ImmutableArray<AbiRegisterSegment> registerSegments = default)
        {
            PassingKind = passingKind;
            RegisterClass = registerClass;
            Size = size;
            Alignment = alignment;
            ContainsGcPointers = containsGcPointers;
            RegisterSegments = registerSegments.IsDefault ? ImmutableArray<AbiRegisterSegment>.Empty : registerSegments;
        }

        public bool IsRegisterPassed => PassingKind is AbiValuePassingKind.ScalarRegister or AbiValuePassingKind.MultiRegister;
        public bool IsStackPassed => PassingKind == AbiValuePassingKind.Stack;
        public bool IsIndirect => PassingKind == AbiValuePassingKind.Indirect;
        public int RegisterCount => PassingKind == AbiValuePassingKind.ScalarRegister ? 1 : RegisterSegments.Length;
    }
    internal enum AbiArgumentRole : byte
    {
        Normal,
        HiddenReturnBuffer,
    }
    internal readonly struct AbiArgumentLocation
    {
        public readonly RegisterClass RegisterClass;
        public readonly MachineRegister Register;
        public readonly int StackSlotIndex;
        public readonly int StackOffset;
        public readonly int Size;

        private AbiArgumentLocation(RegisterClass registerClass, MachineRegister register, int stackSlotIndex, int stackOffset, int size)
        {
            if (registerClass == RegisterClass.Invalid)
                throw new ArgumentOutOfRangeException(nameof(registerClass));
            if (stackSlotIndex < -1)
                throw new ArgumentOutOfRangeException(nameof(stackSlotIndex));
            if (stackOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(stackOffset));
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size));

            RegisterClass = registerClass;
            Register = register;
            StackSlotIndex = stackSlotIndex;
            StackOffset = stackOffset;
            Size = size;
        }

        public static AbiArgumentLocation ForRegister(RegisterClass registerClass, MachineRegister register, int size)
            => new AbiArgumentLocation(registerClass, register, -1, 0, size);

        public static AbiArgumentLocation ForStack(RegisterClass registerClass, int stackSlotIndex, int stackOffset, int size)
            => new AbiArgumentLocation(registerClass, MachineRegister.Invalid, stackSlotIndex, stackOffset, size);

        public bool IsRegister => Register != MachineRegister.Invalid;
        public bool IsStack => Register == MachineRegister.Invalid;

        public override string ToString()
            => IsRegister
                ? MachineRegisters.Format(Register)
                : "outarg[" + StackSlotIndex.ToString() + "+" + StackOffset.ToString() + ":" + Size.ToString() + "]";
    }
    internal readonly struct AbiCallSegment
    {
        public readonly int OperandIndex;
        public readonly int SourceArgumentIndex;
        public readonly int SegmentIndex;
        public readonly GenTree Value;
        public readonly AbiArgumentRole Role;
        public readonly AbiValueInfo ValueAbi;
        public readonly RegisterClass RegisterClass;
        public readonly int Offset;
        public readonly int Size;
        public readonly bool ContainsGcPointers;
        public readonly AbiArgumentLocation Location;

        public AbiCallSegment(
            int operandIndex,
            int sourceArgumentIndex,
            int segmentIndex,
            GenTree value,
            AbiArgumentRole role,
            AbiValueInfo valueAbi,
            RegisterClass registerClass,
            int offset,
            int size,
            bool containsGcPointers,
            AbiArgumentLocation location)
        {
            if (operandIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(operandIndex));
            if (sourceArgumentIndex < -1)
                throw new ArgumentOutOfRangeException(nameof(sourceArgumentIndex));
            if (segmentIndex < -1)
                throw new ArgumentOutOfRangeException(nameof(segmentIndex));
            if (registerClass == RegisterClass.Invalid)
                throw new ArgumentOutOfRangeException(nameof(registerClass));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size));

            OperandIndex = operandIndex;
            SourceArgumentIndex = sourceArgumentIndex;
            SegmentIndex = segmentIndex;
            Value = value;
            Role = role;
            ValueAbi = valueAbi;
            RegisterClass = registerClass;
            Offset = offset;
            Size = size;
            ContainsGcPointers = containsGcPointers;
            Location = location;
        }

        public bool IsHiddenReturnBuffer => Role == AbiArgumentRole.HiddenReturnBuffer;
        public bool IsAbiSegment => SegmentIndex >= 0;
        public bool IsRegister => Location.IsRegister;
        public bool IsStack => Location.IsStack;

        public AbiRegisterSegment ToRegisterSegment()
            => new AbiRegisterSegment(RegisterClass, Offset, Size, ContainsGcPointers);
    }
    internal sealed class AbiCallDescriptor
    {
        public GenTree? ResultValue { get; }
        public AbiValueInfo ReturnAbi { get; }
        public ImmutableArray<AbiCallSegment> ArgumentSegments { get; }
        public int OutgoingStackSlotCount { get; }

        public AbiCallDescriptor(
            GenTree? resultValue,
            AbiValueInfo returnAbi,
            ImmutableArray<AbiCallSegment> argumentSegments,
            int outgoingStackSlotCount)
        {
            if (outgoingStackSlotCount < 0)
                throw new ArgumentOutOfRangeException(nameof(outgoingStackSlotCount));

            ResultValue = resultValue;
            ReturnAbi = returnAbi;
            ArgumentSegments = argumentSegments.IsDefault ? ImmutableArray<AbiCallSegment>.Empty : argumentSegments;
            OutgoingStackSlotCount = outgoingStackSlotCount;
        }

        public bool HasHiddenReturnBuffer
        {
            get
            {
                for (int i = 0; i < ArgumentSegments.Length; i++)
                {
                    if (ArgumentSegments[i].IsHiddenReturnBuffer)
                        return true;
                }
                return false;
            }
        }
    }
    internal static class MachineAbi
    {
        private const int MaxIntegerRegisterSlots = 2;
        private const int MaxFlattenedFloatAggregateFields = 2;
        private const int MaxRegisterAggregateBytes = 16;


        public static int GeneralRegisterSlotSize => TargetArchitecture.GeneralRegisterSize;
        public static int StackArgumentSlotSize => TargetArchitecture.StackSlotSize;

        public static AbiValueInfo AddressValue()
            => Scalar(RegisterClass.General, TargetArchitecture.PointerSize, TargetArchitecture.PointerSize, containsGcPointers: false);

        public static GenStackKind StackKindForType(RuntimeType? type)
        {
            if (type is null)
                return GenStackKind.Unknown;
            if (IsVoid(type))
                return GenStackKind.Void;
            if (type.IsReferenceType)
                return GenStackKind.Ref;
            if (type.Kind == RuntimeTypeKind.Pointer)
                return GenStackKind.Ptr;
            if (type.Kind == RuntimeTypeKind.ByRef)
                return GenStackKind.ByRef;
            if (type.Kind == RuntimeTypeKind.TypeParam)
                return GenStackKind.Value;
            if (type.Kind == RuntimeTypeKind.Enum)
                return type.SizeOf <= 4 ? GenStackKind.I4 : GenStackKind.I8;
            if (type.Namespace == "System")
            {
                switch (type.Name)
                {
                    case "Boolean":
                    case "Char":
                    case "SByte":
                    case "Byte":
                    case "Int16":
                    case "UInt16":
                    case "Int32":
                    case "UInt32":
                        return GenStackKind.I4;
                    case "Int64":
                    case "UInt64":
                        return GenStackKind.I8;
                    case "Single":
                        return GenStackKind.R4;
                    case "Double":
                        return GenStackKind.R8;
                    case "IntPtr":
                        return GenStackKind.NativeInt;
                    case "UIntPtr":
                        return GenStackKind.NativeUInt;
                }
            }
            return GenStackKind.Value;
        }

        public static bool RequiresHiddenReturnBuffer(RuntimeMethod? method)
        {
            if (method is null)
                return false;

            var returnStackKind = StackKindForType(method.ReturnType);
            return ClassifyValue(method.ReturnType, returnStackKind, isReturn: true).PassingKind == AbiValuePassingKind.Indirect;
        }

        public static AbiValueInfo ClassifyStorageValue(RuntimeType? type, GenStackKind stackKind)
        {
            var abi = ClassifyValue(type, stackKind, isReturn: false);
            if (abi.PassingKind == AbiValuePassingKind.Void)
                return abi;

            if (abi.PassingKind == AbiValuePassingKind.Indirect)
            {
                int size = type is null ? TargetArchitecture.PointerSize : Math.Max(1, type.SizeOf);
                int align = type is null ? TargetArchitecture.PointerSize : Math.Max(1, type.AlignOf);
                return new AbiValueInfo(AbiValuePassingKind.Stack, RegisterClass.General, size, align, abi.ContainsGcPointers);
            }

            if (abi.PassingKind == AbiValuePassingKind.Stack)
                return abi;

            if (type is null)
                return abi;

            if (!type.IsValueType)
                return abi;

            if (IsScalarStorageValue(type))
                return abi;

            if (IsPhysicallyPromotableStruct(type, abi))
                return abi;

            return new AbiValueInfo(
                AbiValuePassingKind.Stack,
                RegisterClass.General,
                Math.Max(1, type.SizeOf),
                Math.Max(1, type.AlignOf),
                type.ContainsGcPointers);
        }

        public static bool RequiresAggregateHome(RuntimeType? type, GenStackKind stackKind)
            => ClassifyStorageValue(type, stackKind).PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect;

        public static AbiValueInfo ClassifyValue(RuntimeType? type, GenStackKind stackKind, bool isReturn)
        {
            if (stackKind == GenStackKind.Void || (type is not null && IsVoid(type)))
                return new AbiValueInfo(AbiValuePassingKind.Void, RegisterClass.Invalid, 0, 1, containsGcPointers: false);

            if (stackKind is GenStackKind.Ref or GenStackKind.Null)
                return Scalar(RegisterClass.General, TargetArchitecture.PointerSize, TargetArchitecture.PointerSize, containsGcPointers: true);

            if (stackKind == GenStackKind.ByRef)
                return Scalar(RegisterClass.General, TargetArchitecture.PointerSize, TargetArchitecture.PointerSize, containsGcPointers: true);

            if (stackKind is GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ptr)
                return Scalar(RegisterClass.General, TargetArchitecture.PointerSize, TargetArchitecture.PointerSize, containsGcPointers: false);

            if (type is not null)
            {
                if (type.Kind is RuntimeTypeKind.Pointer)
                    return Scalar(RegisterClass.General, TargetArchitecture.PointerSize, TargetArchitecture.PointerSize, containsGcPointers: false);

                if (type.Kind is RuntimeTypeKind.ByRef or RuntimeTypeKind.TypeParam || type.IsReferenceType)
                    return Scalar(RegisterClass.General, TargetArchitecture.PointerSize, TargetArchitecture.PointerSize, containsGcPointers: true);

                if (type.IsValueType)
                {
                    int exactSize = Math.Max(1, type.SizeOf);
                    int exactAlign = Math.Max(1, type.AlignOf);
                    if (TryClassifyPrimitiveWrapper(type, exactSize, exactAlign, out var primitiveWrapper))
                        return primitiveWrapper;

                    if (TryClassifyEnum(type, exactSize, exactAlign, out var enumAbi))
                        return enumAbi;
                }
            }

            if (stackKind is GenStackKind.R4 or GenStackKind.R8)
            {
                if (type is not null && IsFloatScalar(type, out int floatSize))
                    return Scalar(RegisterClass.Float, floatSize, Math.Min(8, Math.Max(4, floatSize)), containsGcPointers: false);
                return stackKind == GenStackKind.R4
                    ? Scalar(RegisterClass.Float, 4, 4, containsGcPointers: false)
                    : Scalar(RegisterClass.Float, 8, 8, containsGcPointers: false);
            }

            if (stackKind is GenStackKind.I4)
                return IntegerScalar(4, 4, containsGcPointers: false);

            if (stackKind is GenStackKind.I8)
                return IntegerScalar(8, 8, containsGcPointers: false);

            if (type is null)
                return new AbiValueInfo(AbiValuePassingKind.Indirect, RegisterClass.General,
                    TargetArchitecture.PointerSize, TargetArchitecture.PointerSize, containsGcPointers: true);

            if (!type.IsValueType)
                return Scalar(RegisterClass.General, TargetArchitecture.PointerSize, TargetArchitecture.PointerSize, containsGcPointers: true);

            int size = Math.Max(1, type.SizeOf);
            int align = Math.Max(1, type.AlignOf);

            if (size > MaxRegisterAggregateBytes)
                return new AbiValueInfo(isReturn ? AbiValuePassingKind.Indirect : AbiValuePassingKind.Stack, RegisterClass.General, size, align, type.ContainsGcPointers);

            if (TryClassifyHomogeneousFloatAggregate(type, size, maxFields: MaxFlattenedFloatAggregateFields, out var hfaStruct))
                return hfaStruct;

            if (TryClassifySingleRegisterStruct(type, size, align, out var singleRegisterStruct))
                return singleRegisterStruct;

            if (TryClassifyMultiRegisterStruct(type, size, align, out var registerStruct))
                return registerStruct;

            return new AbiValueInfo(isReturn ? AbiValuePassingKind.Indirect : AbiValuePassingKind.Stack, RegisterClass.General, size, align, type.ContainsGcPointers);
        }

        public static bool RequiresStackHome(RuntimeType? type, GenStackKind stackKind)
        {
            var abi = ClassifyStorageValue(type, stackKind);
            return abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect;
        }

        public static bool IsBlockCopyValue(RuntimeType? type, GenStackKind stackKind)
        {
            var abi = ClassifyStorageValue(type, stackKind);
            return abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect;
        }

        public static bool IsRegisterPassedStruct(RuntimeType? type, GenStackKind stackKind)
        {
            var abi = ClassifyValue(type, stackKind, isReturn: false);
            return type is not null && type.IsValueType && abi.IsRegisterPassed;
        }

        public static bool IsPhysicallyPromotableStorage(RuntimeType? type, GenStackKind stackKind)
        {
            if (stackKind is GenStackKind.Void or GenStackKind.Unknown)
                return false;

            var abi = ClassifyStorageValue(type, stackKind);
            if (!abi.IsRegisterPassed)
                return false;

            if (type is null)
                return abi.IsRegisterPassed;

            if (!type.IsValueType)
                return abi.PassingKind == AbiValuePassingKind.ScalarRegister;

            if (IsScalarStorageValue(type))
                return true;

            return IsPhysicallyPromotableStruct(type, abi);
        }

        public static ImmutableArray<AbiRegisterSegment> GetRegisterSegments(AbiValueInfo abi)
        {
            if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
            {
                return ImmutableArray.Create(new AbiRegisterSegment(
                    abi.RegisterClass,
                    offset: 0,
                    size: abi.Size <= 0 ? GeneralRegisterSlotSize : abi.Size,
                    abi.ContainsGcPointers));
            }

            return abi.RegisterSegments;
        }

        public static int HiddenReturnBufferInsertionIndex(RuntimeMethod? method, int explicitArgumentCount)
        {
            if (method is null || !RequiresHiddenReturnBuffer(method))
                return -1;

            return method.HasThis && explicitArgumentCount > 0 ? 1 : 0;
        }

        public static AbiCallDescriptor BuildCallDescriptor(
            ImmutableArray<GenTree> arguments,
            Func<GenTree, GenTreeValueInfo> getValueInfo,
            GenTree? resultValue = null,
            RuntimeMethod? callee = null,
            bool isNewObject = false)
        {
            if (getValueInfo is null)
                throw new ArgumentNullException(nameof(getValueInfo));

            arguments = arguments.IsDefault ? ImmutableArray<GenTree>.Empty : arguments;
            var segments = ImmutableArray.CreateBuilder<AbiCallSegment>();
            int generalArg = 0;
            int floatArg = 0;
            int outgoingArg = 0;
            int operandIndex = 0;
            AbiValueInfo returnAbi = new AbiValueInfo(AbiValuePassingKind.Void, RegisterClass.Invalid, 0, 1, containsGcPointers: false);
            bool needsHiddenReturnBuffer = false;
            bool hiddenReturnBufferInserted = false;
            GenTree? hiddenReturnBufferValue = default;
            if (isNewObject && callee?.DeclaringType.IsValueType == true)
                throw new InvalidOperationException("Value-type newobj must be lowered into a struct materialization temp and a void constructor call before ABI classification.");

            bool referenceTypeNewObject = isNewObject && callee?.HasThis == true;

            if (referenceTypeNewObject)
            {
                _ = AssignScalarArgumentLocation(
                    RegisterClass.General,
                    TargetArchitecture.PointerSize,
                    ref generalArg,
                    ref floatArg,
                    ref outgoingArg);
            }

            if (resultValue is not null)
            {
                var resultInfo = getValueInfo(resultValue);
                returnAbi = ClassifyValue(resultInfo.Type, resultInfo.StackKind, isReturn: true);
                if (returnAbi.PassingKind == AbiValuePassingKind.Indirect)
                {
                    needsHiddenReturnBuffer = true;
                    hiddenReturnBufferValue = resultValue;
                }
            }

            int hiddenReturnBufferInsertionIndex = needsHiddenReturnBuffer
                ? (callee is null ? 0 : HiddenReturnBufferInsertionIndex(callee, arguments.Length))
                : -1;

            if (hiddenReturnBufferInsertionIndex == 0)
                AddHiddenReturnBuffer();

            for (int i = 0; i < arguments.Length; i++)
            {
                if (hiddenReturnBufferInsertionIndex == i)
                    AddHiddenReturnBuffer();

                var value = arguments[i];
                var info = getValueInfo(value);
                var abi = ClassifyValue(info.Type, info.StackKind, isReturn: false);
                if (abi.PassingKind == AbiValuePassingKind.Void)
                    continue;

                if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                {
                    var registerClass = abi.RegisterClass == RegisterClass.Invalid
                        ? (info.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : info.RegisterClass)
                        : abi.RegisterClass;
                    var location = AssignScalarArgumentLocation(
                        registerClass,
                        abi.Size <= 0 ? GeneralRegisterSlotSize : abi.Size,
                        ref generalArg,
                        ref floatArg,
                        ref outgoingArg);

                    segments.Add(new AbiCallSegment(
                        operandIndex++,
                        i,
                        -1,
                        value,
                        AbiArgumentRole.Normal,
                        abi,
                        registerClass,
                        0,
                        abi.Size <= 0 ? GeneralRegisterSlotSize : abi.Size,
                        abi.ContainsGcPointers,
                        location));
                    continue;
                }

                if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    var abiSegments = GetRegisterSegments(abi);
                    int aggregateStackSlot = -1;
                    int aggregateStackBaseOffset = 0;

                    for (int s = 0; s < abiSegments.Length; s++)
                    {
                        var segment = abiSegments[s];
                        var location = AssignAggregateSegmentArgumentLocation(
                            segment,
                            ref generalArg,
                            ref floatArg,
                            ref outgoingArg,
                            ref aggregateStackSlot,
                            ref aggregateStackBaseOffset);

                        segments.Add(new AbiCallSegment(
                            operandIndex++,
                            i,
                            s,
                            value,
                            AbiArgumentRole.Normal,
                            abi,
                            segment.RegisterClass,
                            segment.Offset,
                            segment.Size,
                            segment.ContainsGcPointers,
                            location));
                    }
                    continue;
                }

                var stackClass = info.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : info.RegisterClass;
                int stackSize = abi.Size <= 0 ? TargetArchitecture.PointerSize : abi.Size;
                var stackLocation = AbiArgumentLocation.ForStack(stackClass, outgoingArg++, 0, stackSize);
                segments.Add(new AbiCallSegment(
                    operandIndex++,
                    i,
                    -1,
                    value,
                    AbiArgumentRole.Normal,
                    abi,
                    stackClass,
                    0,
                    stackSize,
                    abi.ContainsGcPointers,
                    stackLocation));
            }

            if (needsHiddenReturnBuffer && !hiddenReturnBufferInserted)
                AddHiddenReturnBuffer();

            return new AbiCallDescriptor(resultValue, returnAbi, segments.ToImmutable(), outgoingArg);

            void AddHiddenReturnBuffer()
            {
                if (!needsHiddenReturnBuffer || hiddenReturnBufferInserted)
                    return;

                var addressAbi = AddressValue();
                var location = AssignScalarArgumentLocation(
                    addressAbi.RegisterClass,
                    addressAbi.Size,
                    ref generalArg,
                    ref floatArg,
                    ref outgoingArg);

                if (hiddenReturnBufferValue is null)
                    throw new InvalidOperationException("Hidden return buffer argument requested without a result value.");

                segments.Add(new AbiCallSegment(
                    operandIndex++,
                    -1,
                    -1,
                    hiddenReturnBufferValue,
                    AbiArgumentRole.HiddenReturnBuffer,
                    addressAbi,
                    RegisterClass.General,
                    0,
                    TargetArchitecture.PointerSize,
                    false,
                    location));
                hiddenReturnBufferInserted = true;
            }
        }


        internal static AbiArgumentLocation AssignAggregateSegmentArgumentLocation(
            AbiRegisterSegment segment,
            ref int generalIndex,
            ref int floatIndex,
            ref int outgoingIndex,
            ref int aggregateStackSlot,
            ref int aggregateStackBaseOffset)
        {
            MachineRegister register;
            if (segment.RegisterClass == RegisterClass.Float)
            {
                register = MachineRegisters.GetFloatArgumentRegister(floatIndex++);
                if (register != MachineRegister.Invalid)
                    return AbiArgumentLocation.ForRegister(RegisterClass.Float, register, segment.Size);
            }
            else if (segment.RegisterClass == RegisterClass.General)
            {
                register = MachineRegisters.GetIntegerArgumentRegister(generalIndex++);
                if (register != MachineRegister.Invalid)
                    return AbiArgumentLocation.ForRegister(RegisterClass.General, register, segment.Size);
            }
            else
            {
                throw new InvalidOperationException("Invalid ABI aggregate segment register class " + segment.RegisterClass + ".");
            }

            if (aggregateStackSlot < 0)
            {
                aggregateStackSlot = outgoingIndex++;
                aggregateStackBaseOffset = segment.Offset;
            }

            return AbiArgumentLocation.ForStack(
                segment.RegisterClass,
                aggregateStackSlot,
                checked(segment.Offset - aggregateStackBaseOffset),
                segment.Size);
        }

        internal static AbiArgumentLocation AssignScalarArgumentLocation(
            RegisterClass registerClass,
            int size,
            ref int generalIndex,
            ref int floatIndex,
            ref int outgoingIndex)
        {
            if (registerClass == RegisterClass.Float)
            {
                var reg = MachineRegisters.GetFloatArgumentRegister(floatIndex++);
                if (reg != MachineRegister.Invalid)
                    return AbiArgumentLocation.ForRegister(RegisterClass.Float, reg, size <= 0 ? 8 : size);
                return AbiArgumentLocation.ForStack(RegisterClass.Float, outgoingIndex++, 0, size <= 0 ? 8 : size);
            }

            if (registerClass == RegisterClass.General)
            {
                var reg = MachineRegisters.GetIntegerArgumentRegister(generalIndex++);
                if (reg != MachineRegister.Invalid)
                    return AbiArgumentLocation.ForRegister(RegisterClass.General, reg, size <= 0 ? GeneralRegisterSlotSize : size);
                return AbiArgumentLocation.ForStack(RegisterClass.General, outgoingIndex++, 0, size <= 0 ? GeneralRegisterSlotSize : size);
            }

            throw new InvalidOperationException("Invalid ABI argument register class " + registerClass + ".");
        }

        private static AbiValueInfo Scalar(RegisterClass registerClass, int size, int alignment, bool containsGcPointers)
            => new AbiValueInfo(AbiValuePassingKind.ScalarRegister, registerClass, Math.Max(1, size), Math.Max(1, alignment), containsGcPointers);

        private static AbiValueInfo IntegerScalar(int size, int alignment, bool containsGcPointers)
        {
            int slotSize = GeneralRegisterSlotSize;
            if (size <= slotSize)
                return Scalar(RegisterClass.General, Math.Max(1, size), Math.Max(1, alignment), containsGcPointers);

            return MultiRegisterInteger(size, alignment, containsGcPointers, MaxIntegerRegisterSlots, null);
        }

        private static AbiValueInfo MultiRegisterInteger(
            int size,
            int alignment,
            bool containsGcPointers,
            int maxRegisterSlots,
            Func<int, int, bool>? gcPointerProvider)
        {
            int slotSize = GeneralRegisterSlotSize;
            int segmentCount = checked((size + slotSize - 1) / slotSize);
            if (segmentCount <= 0)
                return Scalar(RegisterClass.General, slotSize, alignment, containsGcPointers);

            if (segmentCount > maxRegisterSlots)
                return new AbiValueInfo(AbiValuePassingKind.Stack, RegisterClass.General, size, alignment, containsGcPointers);

            if (segmentCount == 1)
                return Scalar(RegisterClass.General, size, alignment, containsGcPointers);

            var segments = ImmutableArray.CreateBuilder<AbiRegisterSegment>(segmentCount);
            for (int offset = 0; offset < size; offset += slotSize)
            {
                int segmentSize = Math.Min(slotSize, size - offset);
                bool segmentContainsGc = gcPointerProvider?.Invoke(offset, segmentSize) ?? containsGcPointers;
                segments.Add(new AbiRegisterSegment(RegisterClass.General, offset, segmentSize, segmentContainsGc));
            }

            return new AbiValueInfo(
                AbiValuePassingKind.MultiRegister,
                RegisterClass.General,
                size,
                alignment,
                containsGcPointers,
                segments.ToImmutable());
        }

        private static bool TryClassifyPrimitiveWrapper(RuntimeType type, int size, int align, out AbiValueInfo abi)
        {
            abi = default;
            if (!IsPrimitiveWrapper(type))
                return false;

            if (type.Name is "Single" or "Double")
                abi = Scalar(RegisterClass.Float, size, align, containsGcPointers: false);
            else
                abi = IntegerScalar(size, align, containsGcPointers: false);
            return true;
        }

        private static bool TryClassifyEnum(RuntimeType type, int size, int align, out AbiValueInfo abi)
        {
            abi = default;
            if (type.Kind != RuntimeTypeKind.Enum)
                return false;

            abi = IntegerScalar(Math.Max(1, size), Math.Max(1, align), containsGcPointers: false);
            return true;
        }

        private static bool TryClassifySingleRegisterStruct(RuntimeType type, int size, int align, out AbiValueInfo abi)
        {
            abi = default;
            if (size > GeneralRegisterSlotSize)
                return false;


            if (TryClassifyHomogeneousFloatAggregate(type, size, maxFields: 1, out var hfa))
            {
                abi = hfa;
                return true;
            }

            abi = Scalar(RegisterClass.General, size, align, type.ContainsGcPointers);
            return true;
        }

        private static bool TryClassifyMultiRegisterStruct(RuntimeType type, int size, int align, out AbiValueInfo abi)
        {
            abi = default;

            if (TryClassifyHomogeneousFloatAggregate(type, size, maxFields: MaxFlattenedFloatAggregateFields, out abi))
                return true;

            if (TryClassifyMixedFieldRegisterStruct(type, size, align, out abi))
                return true;

            int registerSlotSize = GeneralRegisterSlotSize;
            if (size > registerSlotSize * MaxIntegerRegisterSlots)
                return false;

            abi = MultiRegisterInteger(
                size,
                align,
                type.ContainsGcPointers,
                MaxIntegerRegisterSlots,
                (offset, segmentSize) => SegmentContainsGcPointer(type, offset, segmentSize));
            return abi.PassingKind == AbiValuePassingKind.MultiRegister;
        }

        private readonly struct FlattenedAbiField
        {
            public readonly RegisterClass RegisterClass;
            public readonly int Offset;
            public readonly int Size;
            public readonly bool ContainsGcPointers;

            public FlattenedAbiField(RegisterClass registerClass, int offset, int size, bool containsGcPointers)
            {
                RegisterClass = registerClass;
                Offset = offset;
                Size = size;
                ContainsGcPointers = containsGcPointers;
            }
        }

        private static bool TryClassifyMixedFieldRegisterStruct(RuntimeType type, int size, int align, out AbiValueInfo abi)
        {
            abi = default;
            if (type.InstanceFields.Length == 0)
                return false;

            var flattenedFields = new List<FlattenedAbiField>(MaxIntegerRegisterSlots);
            var visitingTypes = new HashSet<int>();
            if (!TryFlattenRegisterFields(type, 0, flattenedFields, MaxIntegerRegisterSlots, visitingTypes))
                return false;
            if (flattenedFields.Count != 2)
                return false;

            var segments = ImmutableArray.CreateBuilder<AbiRegisterSegment>(flattenedFields.Count);
            int expectedOffset = 0;
            for (int i = 0; i < flattenedFields.Count; i++)
            {
                var field = flattenedFields[i];
                if (field.Offset != expectedOffset || field.Offset + field.Size > size)
                    return false;

                segments.Add(new AbiRegisterSegment(field.RegisterClass, field.Offset, field.Size, field.ContainsGcPointers));
                expectedOffset = field.Offset + field.Size;
            }

            if (expectedOffset != size)
                return false;

            abi = new AbiValueInfo(
                AbiValuePassingKind.MultiRegister,
                RegisterClass.General,
                size,
                align,
                type.ContainsGcPointers,
                segments.ToImmutable());
            return true;
        }

        private static bool TryFlattenRegisterFields(
            RuntimeType type,
            int baseOffset,
            List<FlattenedAbiField> flattenedFields,
            int maxSegments,
            HashSet<int> visitingTypes)
        {
            if (type.InstanceFields.Length == 0)
                return false;

            if (!visitingTypes.Add(type.TypeId))
                return false;

            try
            {
                var fields = (RuntimeField[])type.InstanceFields.Clone();
                Array.Sort(fields, static (a, b) => a.Offset.CompareTo(b.Offset));

                int expectedOffset = 0;
                int typeSize = Math.Max(1, type.SizeOf);
                for (int i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    if (field.Offset != expectedOffset)
                        return false;

                    var fieldType = field.FieldType;
                    int fieldSize;
                    if (TryClassifyFieldAsRegisterSegment(fieldType, out var registerClass, out fieldSize, out bool containsGcPointers))
                    {
                        if (fieldSize <= 0 || field.Offset + fieldSize > typeSize)
                            return false;

                        flattenedFields.Add(new FlattenedAbiField(registerClass, baseOffset + field.Offset, fieldSize, containsGcPointers));
                    }
                    else if (fieldType.IsValueType && fieldType.InstanceFields.Length != 0)
                    {
                        fieldSize = Math.Max(1, fieldType.SizeOf);
                        if (fieldSize <= 0 || field.Offset + fieldSize > typeSize)
                            return false;

                        int beforeCount = flattenedFields.Count;
                        if (!TryFlattenRegisterFields(fieldType, baseOffset + field.Offset, flattenedFields, maxSegments, visitingTypes))
                            return false;

                        if (flattenedFields.Count == beforeCount)
                            return false;
                    }
                    else
                    {
                        return false;
                    }

                    if (flattenedFields.Count > maxSegments)
                        return false;

                    expectedOffset = field.Offset + fieldSize;
                }

                return expectedOffset == typeSize;
            }
            finally
            {
                visitingTypes.Remove(type.TypeId);
            }
        }

        private static bool TryClassifyFieldAsRegisterSegment(RuntimeType fieldType, out RegisterClass registerClass, out int size, out bool containsGcPointers)
        {
            registerClass = RegisterClass.Invalid;
            size = 0;
            containsGcPointers = false;

            if (IsFloatScalar(fieldType, out size))
            {
                registerClass = RegisterClass.Float;
                return true;
            }

            if (fieldType.Kind is RuntimeTypeKind.Pointer)
            {
                registerClass = RegisterClass.General;
                size = TargetArchitecture.PointerSize;
                return true;
            }

            if (fieldType.Kind is RuntimeTypeKind.ByRef or RuntimeTypeKind.TypeParam || fieldType.IsReferenceType)
            {
                registerClass = RegisterClass.General;
                size = TargetArchitecture.PointerSize;
                containsGcPointers = true;
                return true;
            }

            if (fieldType.Kind == RuntimeTypeKind.Enum || IsPrimitiveWrapper(fieldType))
            {
                registerClass = RegisterClass.General;
                size = Math.Max(1, fieldType.SizeOf);
                containsGcPointers = fieldType.ContainsGcPointers;
                return size <= GeneralRegisterSlotSize;
            }

            return false;
        }

        private static bool TryClassifyHomogeneousFloatAggregate(RuntimeType type, int size, int maxFields, out AbiValueInfo abi)
        {
            abi = default;
            if (type.ContainsGcPointers || size <= 0 || maxFields <= 0)
                return false;

            var flattenedFields = new List<FlattenedAbiField>(maxFields);
            var visitingTypes = new HashSet<int>();
            if (!TryFlattenRegisterFields(type, 0, flattenedFields, maxFields, visitingTypes))
                return false;
            if (flattenedFields.Count == 0 || flattenedFields.Count > maxFields)
                return false;

            int expectedOffset = 0;
            int elementSize = 0;
            var segments = ImmutableArray.CreateBuilder<AbiRegisterSegment>(flattenedFields.Count);
            for (int i = 0; i < flattenedFields.Count; i++)
            {
                var field = flattenedFields[i];
                if (field.RegisterClass != RegisterClass.Float || field.ContainsGcPointers)
                    return false;
                if (elementSize == 0)
                    elementSize = field.Size;
                else if (elementSize != field.Size)
                    return false;
                if (field.Offset != expectedOffset)
                    return false;

                segments.Add(new AbiRegisterSegment(RegisterClass.Float, field.Offset, field.Size, containsGcPointers: false));
                expectedOffset += field.Size;
            }

            if (expectedOffset != size)
                return false;

            abi = segments.Count == 1
                ? Scalar(RegisterClass.Float, size, Math.Max(elementSize, type.AlignOf), containsGcPointers: false)
                : new AbiValueInfo(
                    AbiValuePassingKind.MultiRegister,
                    RegisterClass.Float,
                    size,
                    Math.Max(elementSize, type.AlignOf),
                    containsGcPointers: false,
                    segments.ToImmutable());
            return true;
        }

        private static bool SegmentContainsGcPointer(RuntimeType type, int offset, int size)
        {
            var gcOffsets = type.GcPointerOffsets;
            int end = offset + size;
            for (int i = 0; i < gcOffsets.Length; i++)
            {
                int gcOffset = gcOffsets[i];
                if (offset <= gcOffset && gcOffset < end)
                    return true;
            }
            return false;
        }

        private static bool IsPhysicallyPromotableStruct(RuntimeType type, AbiValueInfo abi)
        {
            if (!abi.IsRegisterPassed || type.InstanceFields.Length == 0)
                return false;

            int structSize = Math.Max(1, type.SizeOf);
            if (abi.Size > 0 && abi.Size != structSize)
                return false;

            var segments = GetRegisterSegments(abi);
            if (segments.IsDefaultOrEmpty)
                return false;

            var flattenedFields = new List<FlattenedAbiField>(segments.Length);
            var visitingTypes = new HashSet<int>();
            if (!TryFlattenRegisterFields(type, 0, flattenedFields, segments.Length, visitingTypes))
                return false;

            int previousEnd = 0;
            for (int i = 0; i < flattenedFields.Count; i++)
            {
                var field = flattenedFields[i];
                if (field.Offset != previousEnd || field.Size <= 0)
                    return false;

                int fieldEnd = field.Offset + field.Size;
                if (fieldEnd > structSize)
                    return false;

                if (!IsFieldCoveredBySingleAbiSegment(field.Offset, field.Size, field.RegisterClass, segments))
                    return false;

                previousEnd = fieldEnd;
            }

            return previousEnd == structSize;
        }

        private static bool IsFieldCoveredBySingleAbiSegment(
            int fieldOffset,
            int fieldSize,
            RegisterClass fieldRegisterClass,
            ImmutableArray<AbiRegisterSegment> segments)
        {
            int fieldEnd = fieldOffset + fieldSize;
            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                int segmentEnd = segment.Offset + segment.Size;
                if (segment.Offset <= fieldOffset && fieldEnd <= segmentEnd)
                    return segment.RegisterClass == fieldRegisterClass || segment.RegisterClass == RegisterClass.General;
            }

            return false;
        }

        private static bool IsFloatScalar(RuntimeType type, out int size)
        {
            size = 0;
            if (type.Namespace != "System")
                return false;

            if (type.Name == "Single")
            {
                size = 4;
                return true;
            }

            if (type.Name == "Double")
            {
                size = 8;
                return true;
            }

            return false;
        }

        private static bool IsPrimitiveWrapper(RuntimeType type)
        {
            if (type.Namespace != "System" || !type.IsValueType)
                return false;

            return type.Name is
                "Boolean" or "Char" or
                "SByte" or "Byte" or
                "Int16" or "UInt16" or
                "Int32" or "UInt32" or
                "Int64" or "UInt64" or
                "IntPtr" or "UIntPtr" or
                "Half" or "Single" or "Double";
        }

        private static bool IsScalarStorageValue(RuntimeType type)
            => IsPrimitiveWrapper(type) || type.Kind == RuntimeTypeKind.Enum || type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef;

        private static bool IsVoid(RuntimeType type)
            => type.Namespace == "System" && type.Name == "Void";
    }
}
