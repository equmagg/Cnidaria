using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Cnidaria.Cs
{
    internal enum RegisterClass : byte
    {
        Invalid,
        General,
        Float,
    }

    internal enum GenTreeValueOrigin : byte
    {
        TreeNode,
        Local,
        Argument,
        Temporary,
    }

    internal readonly struct GenTreeValueInfo
    {
        public readonly GenTreeValueKey Value;
        public readonly GenTree RepresentativeNode;
        public readonly GenTreeValueOrigin Origin;
        public readonly RuntimeType? Type;
        public readonly GenStackKind StackKind;
        public readonly RegisterClass RegisterClass;
        public readonly int DefinitionBlockId;
        public readonly int DefinitionNodeId;

        public GenTreeValueInfo(
            GenTreeValueKey value,
            GenTree representativeNode,
            RuntimeType? type,
            GenStackKind stackKind,
            RegisterClass registerClass,
            int definitionBlockId,
            int definitionNodeId)
        {
            Value = value;
            RepresentativeNode = representativeNode ?? throw new ArgumentNullException(nameof(representativeNode));
            Origin = value.Origin;
            Type = type;
            StackKind = stackKind;
            RegisterClass = registerClass;
            DefinitionBlockId = definitionBlockId;
            DefinitionNodeId = definitionNodeId;
        }

        public GenTreeValueInfo WithDefinitionNode(GenTree representativeNode, int blockId, int nodeId)
            => new GenTreeValueInfo(Value, representativeNode, Type, StackKind, RegisterClass, blockId, nodeId);

        public AbiValueInfo StorageAbi => MachineAbi.ClassifyStorageValue(Type, StackKind);
        public AbiValueInfo ArgumentAbi => MachineAbi.ClassifyValue(Type, StackKind, isReturn: false);
        public AbiValueInfo ReturnAbi => MachineAbi.ClassifyValue(Type, StackKind, isReturn: true);
        public bool IsMultiRegisterStorage => StorageAbi.PassingKind == AbiValuePassingKind.MultiRegister;
        public bool RequiresStackHome => MachineAbi.RequiresStackHome(Type, StackKind);
    }

    internal enum GenTreeLinearKind : byte
    {
        Tree,
        Copy,
        GcPoll,
    }

    [Flags]
    internal enum GenTreeLinearFlags : ushort
    {
        None = 0,

        // The node is an ABI shaped call. Lowering must expose argument and return registers
        AbiCall = 1 << 0,

        // The node can clobber caller saved registers. This is broader than AbiCall because
        // helper expanded nodes such as allocation and type check nodes may still lower to calls
        CallerSavedKill = 1 << 1,

        // The backend expects all non contained operands and any result to be registers
        RequiresRegisterOperands = 1 << 2,

        // The node consumes all operands directly in its own codegen shape
        IsStandaloneLoweredNode = 1 << 3,

        GcSafePoint = 1 << 4,

        HasMemoryOperand = 1 << 5,

        UsesTrackedLocal = 1 << 6,
        DefinesTrackedLocal = 1 << 7,

        UnusedValue = 1 << 8,
    }


    [Flags]
    internal enum LirOperandFlags : ushort
    {
        None = 0,

        Contained = 1 << 0,

        RegOptional = 1 << 1,
    }

    internal enum LinearMemoryAccessKind : byte
    {
        None,
        Local,
        Argument,
        Temporary,
        Field,
        StaticField,
        Indirect,
        ArrayElement,
        ArrayData,
        StackAlloc,
        PointerElement,
        PointerDiff,
    }

    [Flags]
    internal enum LinearMemoryAccessFlags : ushort
    {
        None = 0,
        Read = 1 << 0,
        Write = 1 << 1,
        Address = 1 << 2,
        ByRefAddress = 1 << 3,
        ContainsGcPointers = 1 << 4,
        BlockCopy = 1 << 5,
        RequiresWriteBarrier = 1 << 6,
        BoundsCheck = 1 << 7,
        NullCheck = 1 << 8,
    }

    internal readonly struct LinearMemoryAccess
    {
        public readonly LinearMemoryAccessKind Kind;
        public readonly LinearMemoryAccessFlags Flags;
        public readonly int AddressOperandIndex;
        public readonly int IndexOperandIndex;
        public readonly int ValueOperandIndex;
        public readonly RuntimeType? ElementType;
        public readonly RuntimeField? Field;
        public readonly int Size;
        public readonly int Alignment;

        public LinearMemoryAccess(
            LinearMemoryAccessKind kind,
            LinearMemoryAccessFlags flags,
            int addressOperandIndex = -1,
            int indexOperandIndex = -1,
            int valueOperandIndex = -1,
            RuntimeType? elementType = null,
            RuntimeField? field = null,
            int size = 0,
            int alignment = 1)
        {
            if (addressOperandIndex < -1)
                throw new ArgumentOutOfRangeException(nameof(addressOperandIndex));
            if (indexOperandIndex < -1)
                throw new ArgumentOutOfRangeException(nameof(indexOperandIndex));
            if (valueOperandIndex < -1)
                throw new ArgumentOutOfRangeException(nameof(valueOperandIndex));
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            if (alignment <= 0)
                throw new ArgumentOutOfRangeException(nameof(alignment));

            Kind = kind;
            Flags = flags;
            AddressOperandIndex = addressOperandIndex;
            IndexOperandIndex = indexOperandIndex;
            ValueOperandIndex = valueOperandIndex;
            ElementType = elementType;
            Field = field;
            Size = size;
            Alignment = alignment;
        }

        public static LinearMemoryAccess None => default;
        public bool IsNone => Kind == LinearMemoryAccessKind.None;
        public bool Reads => (Flags & LinearMemoryAccessFlags.Read) != 0;
        public bool Writes => (Flags & LinearMemoryAccessFlags.Write) != 0;
        public bool IsAddressProducer => (Flags & LinearMemoryAccessFlags.Address) != 0;
        public bool IsBlockCopy => (Flags & LinearMemoryAccessFlags.BlockCopy) != 0;
        public bool HasAddressOperand(int operandIndex) => AddressOperandIndex == operandIndex || IndexOperandIndex == operandIndex;
        public bool HasValueOperand(int operandIndex) => ValueOperandIndex == operandIndex;

        public override string ToString()
        {
            if (IsNone)
                return string.Empty;

            var sb = new StringBuilder();
            sb.Append(Kind).Append('(').Append(Flags).Append(')');
            if (AddressOperandIndex >= 0) sb.Append(" addr=").Append(AddressOperandIndex);
            if (IndexOperandIndex >= 0) sb.Append(" index=").Append(IndexOperandIndex);
            if (ValueOperandIndex >= 0) sb.Append(" value=").Append(ValueOperandIndex);
            if (Size != 0) sb.Append(" size=").Append(Size);
            if (Alignment != 1) sb.Append(" align=").Append(Alignment);
            return sb.ToString();
        }
    }

    internal readonly struct GenTreeLinearLoweringInfo
    {
        public readonly GenTreeLinearFlags Flags;
        public readonly byte InternalGeneralRegisters;
        public readonly byte InternalFloatRegisters;

        public GenTreeLinearLoweringInfo(GenTreeLinearFlags flags, byte internalGeneralRegisters, byte internalFloatRegisters)
        {
            Flags = flags;
            InternalGeneralRegisters = internalGeneralRegisters;
            InternalFloatRegisters = internalFloatRegisters;
        }

        public bool HasFlag(GenTreeLinearFlags flag) => (Flags & flag) != 0;

        public override string ToString()
        {
            if (Flags == GenTreeLinearFlags.None && InternalGeneralRegisters == 0 && InternalFloatRegisters == 0)
                return string.Empty;

            var sb = new StringBuilder();
            if (Flags != GenTreeLinearFlags.None)
                sb.Append(Flags);
            if (InternalGeneralRegisters != 0)
            {
                if (sb.Length != 0) sb.Append(' ');
                sb.Append("intTmp=").Append(InternalGeneralRegisters);
            }
            if (InternalFloatRegisters != 0)
            {
                if (sb.Length != 0) sb.Append(' ');
                sb.Append("floatTmp=").Append(InternalFloatRegisters);
            }
            return sb.ToString();
        }
    }



    internal static class GenTreeLinearLoweringClassifier
    {
        public static GenTreeLinearLoweringInfo Classify(GenTree source, GenTree? result, ImmutableArray<GenTree> uses, int blockId = -1)
            => Classify(source, result, uses, blockId, ClassifyMemoryAccess(source));

        public static GenTreeLinearLoweringInfo Classify(GenTree source, GenTree? result, ImmutableArray<GenTree> uses, int blockId, LinearMemoryAccess memoryAccess)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            GenTreeLinearFlags flags = GenTreeLinearFlags.IsStandaloneLoweredNode;

            if (!memoryAccess.IsNone)
                flags |= GenTreeLinearFlags.HasMemoryOperand;

            if ((source.Flags & GenTreeFlags.LocalUse) != 0)
                flags |= GenTreeLinearFlags.UsesTrackedLocal;
            if ((source.Flags & GenTreeFlags.LocalDef) != 0)
                flags |= GenTreeLinearFlags.DefinesTrackedLocal;

            if (RequiresRegisterOperands(source, memoryAccess))
                flags |= GenTreeLinearFlags.RequiresRegisterOperands;

            if (IsAbiCall(source))
                flags |= GenTreeLinearFlags.AbiCall | GenTreeLinearFlags.CallerSavedKill;
            else if (MayClobberCallerSaved(source))
                flags |= GenTreeLinearFlags.CallerSavedKill;

            if (IsGcSafePoint(source, blockId))
                flags |= GenTreeLinearFlags.GcSafePoint;

            byte internalGeneral = InternalGeneralRegisterCount(source, result, memoryAccess);
            byte internalFloat = InternalFloatRegisterCount(source, result);

            return new GenTreeLinearLoweringInfo(flags, internalGeneral, internalFloat);
        }

        public static LinearMemoryAccess ClassifyMemoryAccess(GenTree source)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            static LinearMemoryAccessFlags TypeFlags(RuntimeType? type, GenStackKind stackKind)
            {
                LinearMemoryAccessFlags flags = LinearMemoryAccessFlags.None;
                if (type is not null)
                {
                    if (type.ContainsGcPointers || type.IsReferenceType || type.Kind is RuntimeTypeKind.ByRef or RuntimeTypeKind.TypeParam)
                        flags |= LinearMemoryAccessFlags.ContainsGcPointers;

                    if (MachineAbi.IsBlockCopyValue(type, stackKind))
                        flags |= LinearMemoryAccessFlags.BlockCopy;
                }
                if (stackKind is GenStackKind.Ref or GenStackKind.Null or GenStackKind.ByRef)
                    flags |= LinearMemoryAccessFlags.ContainsGcPointers;
                if (stackKind == GenStackKind.Value)
                    flags |= LinearMemoryAccessFlags.BlockCopy;
                return flags;
            }

            static LinearMemoryAccessFlags StoreTypeFlags(RuntimeType? type, GenStackKind stackKind)
            {
                var flags = TypeFlags(type, stackKind);
                if ((flags & LinearMemoryAccessFlags.ContainsGcPointers) != 0)
                    flags |= LinearMemoryAccessFlags.RequiresWriteBarrier;
                return flags;
            }

            static int SizeOf(RuntimeType? type, GenStackKind stackKind)
            {
                if (type is not null)
                    return Math.Max(1, type.SizeOf);

                return stackKind switch
                {
                    GenStackKind.I4 => 4,
                    GenStackKind.I8 => 8,
                    GenStackKind.R4 => 4,
                    GenStackKind.R8 => 8,
                    GenStackKind.NativeInt => TargetArchitecture.PointerSize,
                    GenStackKind.NativeUInt => TargetArchitecture.PointerSize,
                    GenStackKind.Ref => TargetArchitecture.PointerSize,
                    GenStackKind.Ptr => TargetArchitecture.PointerSize,
                    GenStackKind.ByRef => TargetArchitecture.PointerSize,
                    GenStackKind.Null => TargetArchitecture.PointerSize,
                    GenStackKind.Void => 0,
                    _ => TargetArchitecture.PointerSize,
                };
            }

            static int AlignOf(RuntimeType? type, GenStackKind stackKind)
            {
                if (type is not null)
                    return Math.Max(1, type.AlignOf);

                return stackKind switch
                {
                    GenStackKind.I4 => 4,
                    GenStackKind.I8 => 8,
                    GenStackKind.R4 => 4,
                    GenStackKind.R8 => 8,
                    GenStackKind.NativeInt => TargetArchitecture.PointerSize,
                    GenStackKind.NativeUInt => TargetArchitecture.PointerSize,
                    GenStackKind.Ref => TargetArchitecture.PointerSize,
                    GenStackKind.Ptr => TargetArchitecture.PointerSize,
                    GenStackKind.ByRef => TargetArchitecture.PointerSize,
                    GenStackKind.Null => TargetArchitecture.PointerSize,
                    GenStackKind.Void => 1,
                    _ => TargetArchitecture.PointerSize,
                };
            }

            static RuntimeType? OperandType(GenTree source, int index)
                => (uint)index < (uint)source.Operands.Length ? source.Operands[index].Type : source.RuntimeType ?? source.Type;

            static GenStackKind OperandStackKind(GenTree source, int index)
                => (uint)index < (uint)source.Operands.Length ? source.Operands[index].StackKind : source.StackKind;

            static RuntimeType? LocalLikeStorageType(GenTree source, int operandIndex)
                => source.LocalDescriptor?.Type ?? OperandType(source, operandIndex) ?? source.RuntimeType ?? source.Type;

            static GenStackKind LocalLikeStorageKind(GenTree source, RuntimeType? storageType, int operandIndex)
                => source.LocalDescriptor is not null
                    ? source.LocalDescriptor.StackKind
                    : storageType is not null
                        ? MachineAbi.StackKindForType(storageType)
                        : OperandStackKind(source, operandIndex);

            static RuntimeType? StoreTargetType(GenTree source, int operandIndex)
                => source.Field?.FieldType ?? source.RuntimeType ?? source.Type ?? OperandType(source, operandIndex);

            static GenStackKind StoreTargetKind(GenTree source, RuntimeType? storageType, int operandIndex)
                => storageType is not null ? MachineAbi.StackKindForType(storageType) : OperandStackKind(source, operandIndex);

            switch (source.Kind)
            {
                case GenTreeKind.Local:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.Local, LinearMemoryAccessFlags.Read
                        | TypeFlags(source.Type, source.StackKind), size: SizeOf(source.Type, source.StackKind), alignment: AlignOf(source.Type, source.StackKind));

                case GenTreeKind.Arg:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.Argument, LinearMemoryAccessFlags.Read
                        | TypeFlags(source.Type, source.StackKind), size: SizeOf(source.Type, source.StackKind), alignment: AlignOf(source.Type, source.StackKind));

                case GenTreeKind.Temp:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.Temporary, LinearMemoryAccessFlags.Read
                        | TypeFlags(source.Type, source.StackKind), size: SizeOf(source.Type, source.StackKind), alignment: AlignOf(source.Type, source.StackKind));

                case GenTreeKind.StoreLocal:
                    {
                        RuntimeType? valueType = LocalLikeStorageType(source, 0);
                        GenStackKind valueKind = LocalLikeStorageKind(source, valueType, 0);
                        return new LinearMemoryAccess(LinearMemoryAccessKind.Local, LinearMemoryAccessFlags.Write
                            | TypeFlags(valueType, valueKind), valueOperandIndex: 0, elementType: valueType,
                            size: SizeOf(valueType, valueKind), alignment: AlignOf(valueType, valueKind));
                    }

                case GenTreeKind.StoreArg:
                    {
                        RuntimeType? valueType = LocalLikeStorageType(source, 0);
                        GenStackKind valueKind = LocalLikeStorageKind(source, valueType, 0);
                        return new LinearMemoryAccess(LinearMemoryAccessKind.Argument, LinearMemoryAccessFlags.Write
                            | TypeFlags(valueType, valueKind), valueOperandIndex: 0, elementType: valueType,
                            size: SizeOf(valueType, valueKind), alignment: AlignOf(valueType, valueKind));
                    }

                case GenTreeKind.StoreTemp:
                    {
                        RuntimeType? valueType = LocalLikeStorageType(source, 0);
                        GenStackKind valueKind = LocalLikeStorageKind(source, valueType, 0);
                        return new LinearMemoryAccess(LinearMemoryAccessKind.Temporary, LinearMemoryAccessFlags.Write
                            | TypeFlags(valueType, valueKind), valueOperandIndex: 0, elementType: valueType,
                            size: SizeOf(valueType, valueKind), alignment: AlignOf(valueType, valueKind));
                    }

                case GenTreeKind.LocalAddr:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.Local, LinearMemoryAccessFlags.Address
                        | LinearMemoryAccessFlags.ByRefAddress, size: TargetArchitecture.PointerSize, alignment: TargetArchitecture.PointerSize);

                case GenTreeKind.ArgAddr:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.Argument, LinearMemoryAccessFlags.Address
                        | LinearMemoryAccessFlags.ByRefAddress, size: TargetArchitecture.PointerSize, alignment: TargetArchitecture.PointerSize);

                case GenTreeKind.Field:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.Field, LinearMemoryAccessFlags.Read
                        | LinearMemoryAccessFlags.NullCheck | TypeFlags(source.Type, source.StackKind),
                        addressOperandIndex: 0, field: source.Field, size: SizeOf(source.Type,
                        source.StackKind), alignment: AlignOf(source.Type, source.StackKind));

                case GenTreeKind.FieldAddr:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.Field, LinearMemoryAccessFlags.Address
                        | LinearMemoryAccessFlags.ByRefAddress | LinearMemoryAccessFlags.NullCheck, addressOperandIndex: 0,
                        field: source.Field, size: TargetArchitecture.PointerSize, alignment: TargetArchitecture.PointerSize);

                case GenTreeKind.StaticField:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.StaticField, LinearMemoryAccessFlags.Read
                        | TypeFlags(source.Type, source.StackKind), field: source.Field,
                        size: SizeOf(source.Type, source.StackKind), alignment: AlignOf(source.Type, source.StackKind));

                case GenTreeKind.StaticFieldAddr:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.StaticField, LinearMemoryAccessFlags.Address
                        | LinearMemoryAccessFlags.ByRefAddress, field: source.Field,
                        size: TargetArchitecture.PointerSize, alignment: TargetArchitecture.PointerSize);

                case GenTreeKind.StoreField:
                    {
                        RuntimeType? valueType = StoreTargetType(source, 1);
                        GenStackKind valueKind = StoreTargetKind(source, valueType, 1);
                        return new LinearMemoryAccess(LinearMemoryAccessKind.Field, LinearMemoryAccessFlags.Write
                            | LinearMemoryAccessFlags.NullCheck | StoreTypeFlags(valueType, valueKind),
                            addressOperandIndex: 0, valueOperandIndex: 1, elementType: valueType,
                            field: source.Field, size: SizeOf(valueType, valueKind), alignment: AlignOf(valueType, valueKind));
                    }

                case GenTreeKind.StoreStaticField:
                    {
                        RuntimeType? valueType = StoreTargetType(source, 0);
                        GenStackKind valueKind = StoreTargetKind(source, valueType, 0);
                        return new LinearMemoryAccess(LinearMemoryAccessKind.StaticField, LinearMemoryAccessFlags.Write
                            | StoreTypeFlags(valueType, valueKind), valueOperandIndex: 0, elementType: valueType,
                            field: source.Field, size: SizeOf(valueType, valueKind), alignment: AlignOf(valueType, valueKind));
                    }

                case GenTreeKind.LoadIndirect:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.Indirect, LinearMemoryAccessFlags.Read
                        | TypeFlags(source.RuntimeType ?? source.Type, source.StackKind), addressOperandIndex: 0,
                        elementType: source.RuntimeType ?? source.Type,
                        size: SizeOf(source.RuntimeType ?? source.Type, source.StackKind), alignment: AlignOf(source.RuntimeType ?? source.Type, source.StackKind));

                case GenTreeKind.StoreIndirect:
                    {
                        RuntimeType? valueType = StoreTargetType(source, 1);
                        GenStackKind valueKind = StoreTargetKind(source, valueType, 1);
                        return new LinearMemoryAccess(LinearMemoryAccessKind.Indirect, LinearMemoryAccessFlags.Write
                            | StoreTypeFlags(valueType, valueKind), addressOperandIndex: 0, valueOperandIndex: 1,
                            elementType: valueType, size: SizeOf(valueType, valueKind), alignment: AlignOf(valueType, valueKind));
                    }

                case GenTreeKind.ArrayElement:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.ArrayElement, LinearMemoryAccessFlags.Read
                        | LinearMemoryAccessFlags.NullCheck | LinearMemoryAccessFlags.BoundsCheck
                        | TypeFlags(source.RuntimeType ?? source.Type, source.StackKind), addressOperandIndex: 0,
                        indexOperandIndex: 1, elementType: source.RuntimeType ?? source.Type,
                        size: SizeOf(source.RuntimeType ?? source.Type, source.StackKind), alignment: AlignOf(source.RuntimeType ?? source.Type, source.StackKind));

                case GenTreeKind.ArrayElementAddr:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.ArrayElement, LinearMemoryAccessFlags.Address
                        | LinearMemoryAccessFlags.ByRefAddress | LinearMemoryAccessFlags.NullCheck | LinearMemoryAccessFlags.BoundsCheck,
                        addressOperandIndex: 0, indexOperandIndex: 1, elementType: source.RuntimeType,
                        size: TargetArchitecture.PointerSize, alignment: TargetArchitecture.PointerSize);

                case GenTreeKind.StoreArrayElement:
                    {
                        RuntimeType? valueType = StoreTargetType(source, 2);
                        GenStackKind valueKind = StoreTargetKind(source, valueType, 2);
                        return new LinearMemoryAccess(LinearMemoryAccessKind.ArrayElement, LinearMemoryAccessFlags.Write
                            | LinearMemoryAccessFlags.NullCheck | LinearMemoryAccessFlags.BoundsCheck | StoreTypeFlags(valueType, valueKind),
                            addressOperandIndex: 0, indexOperandIndex: 1, valueOperandIndex: 2, elementType: valueType,
                            size: SizeOf(valueType, valueKind), alignment: AlignOf(valueType, valueKind));
                    }

                case GenTreeKind.ArrayDataRef:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.ArrayData, LinearMemoryAccessFlags.Address
                        | LinearMemoryAccessFlags.ByRefAddress | LinearMemoryAccessFlags.NullCheck,
                        addressOperandIndex: 0, size: TargetArchitecture.PointerSize, alignment: TargetArchitecture.PointerSize);

                case GenTreeKind.StackAlloc:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.StackAlloc, LinearMemoryAccessFlags.Write
                        | LinearMemoryAccessFlags.Address, valueOperandIndex: 0,
                        size: source.Int32 <= 0 ? TargetArchitecture.PointerSize : source.Int32, alignment: TargetArchitecture.PointerSize);

                case GenTreeKind.PointerElementAddr:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.PointerElement, LinearMemoryAccessFlags.Address
                        | LinearMemoryAccessFlags.ByRefAddress, addressOperandIndex: 0, indexOperandIndex: 1,
                        elementType: source.RuntimeType, size: TargetArchitecture.PointerSize, alignment: TargetArchitecture.PointerSize);

                case GenTreeKind.PointerDiff:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.PointerDiff, LinearMemoryAccessFlags.Address,
                        addressOperandIndex: 0, indexOperandIndex: 1, elementType: source.RuntimeType,
                        size: TargetArchitecture.PointerSize, alignment: TargetArchitecture.PointerSize);

                default:
                    return LinearMemoryAccess.None;
            }
        }

        public static bool IsAbiCall(GenTree source)
        {
            return source.Kind is
                GenTreeKind.Call or
                GenTreeKind.VirtualCall or
                GenTreeKind.DelegateInvoke or
                GenTreeKind.NewObject;
        }

        public static bool MayClobberCallerSaved(GenTree source)
        {
            if (IsAbiCall(source))
                return true;

            if (source.Kind is GenTreeKind.Throw or GenTreeKind.Rethrow)
                return true;

            return false;
        }

        public static bool IsGcSafePoint(GenTree source, int blockId = -1)
        {
            if (IsAbiCall(source))
                return true;

            if (source.Kind is
                GenTreeKind.NewArray or
                GenTreeKind.NewDelegate or
                GenTreeKind.Box or
                GenTreeKind.UnboxAny or
                GenTreeKind.CastClass or
                GenTreeKind.IsInst or
                GenTreeKind.ConstString or
                GenTreeKind.Throw or
                GenTreeKind.Rethrow)
                return true;

            return false;
        }

        private static bool RequiresRegisterOperands(GenTree source, LinearMemoryAccess memoryAccess)
        {
            if (source.StackKind == GenStackKind.Value)
                return false;

            if (!memoryAccess.IsNone)
                return false;

            return source.Kind switch
            {
                GenTreeKind.Nop => false,
                GenTreeKind.ConstI4 => false,
                GenTreeKind.ConstI8 => false,
                GenTreeKind.ConstR4Bits => false,
                GenTreeKind.ConstR8Bits => false,
                GenTreeKind.ConstNull => false,
                GenTreeKind.ConstString => false,
                GenTreeKind.DefaultValue => false,
                GenTreeKind.SizeOf => false,
                GenTreeKind.Local => false,
                GenTreeKind.Arg => false,
                GenTreeKind.Temp => false,
                GenTreeKind.StaticField => false,
                GenTreeKind.StaticFieldAddr => false,
                GenTreeKind.Branch => false,
                GenTreeKind.Rethrow => false,
                GenTreeKind.EndFinally => false,
                _ => true,
            };
        }

        private static byte InternalGeneralRegisterCount(GenTree source, GenTree? result, LinearMemoryAccess memoryAccess)
        {
            int count = source.Kind switch
            {
                GenTreeKind.ArrayElement => ArrayElementLoadGeneralScratchCount(result),
                GenTreeKind.ArrayElementAddr => 1,
                GenTreeKind.StoreArrayElement => StoreArrayElementGeneralScratchCount(source),
                GenTreeKind.PointerElementAddr => 1,
                GenTreeKind.PointerDiff => 1,
                GenTreeKind.StackAlloc => 1,
                GenTreeKind.UnboxAny => MultiRegisterLoadGeneralScratchCount(result, needsAddressScratch: true),
                GenTreeKind.Field => MultiRegisterLoadGeneralScratchCount(result, needsAddressScratch: true),
                GenTreeKind.StaticField => MultiRegisterLoadGeneralScratchCount(result, needsAddressScratch: true),
                GenTreeKind.LoadIndirect => MultiRegisterLoadGeneralScratchCount(result, needsAddressScratch: true),
                GenTreeKind.StoreIndirect => MultiRegisterStoreGeneralScratchCount(MultiRegisterOperandValue(source, 1), needsAddressScratch: true),
                GenTreeKind.StoreField => MultiRegisterStoreGeneralScratchCount(MultiRegisterOperandValue(source, 1), needsAddressScratch: true),
                GenTreeKind.StoreStaticField => MultiRegisterStoreGeneralScratchCount(MultiRegisterOperandValue(source, 0), needsAddressScratch: true),

                GenTreeKind.Box => MultiRegisterStoreGeneralScratchCount(MultiRegisterOperandValue(source, 0), needsAddressScratch: true),

                _ => 0,
            };

            if ((memoryAccess.Flags & LinearMemoryAccessFlags.RequiresWriteBarrier) != 0)
                count++;

            if (count > byte.MaxValue)
                throw new InvalidOperationException($"Node {source.Id} requires too many internal general registers.");

            return (byte)count;
        }

        private static byte InternalFloatRegisterCount(GenTree source, GenTree? result)
        {
            int count = source.Kind switch
            {
                GenTreeKind.ArrayElement => MultiRegisterLoadFloatScratchCount(result),
                GenTreeKind.UnboxAny => MultiRegisterLoadFloatScratchCount(result),
                GenTreeKind.Field => MultiRegisterLoadFloatScratchCount(result),
                GenTreeKind.StaticField => MultiRegisterLoadFloatScratchCount(result),
                GenTreeKind.LoadIndirect => MultiRegisterLoadFloatScratchCount(result),
                GenTreeKind.StoreArrayElement => MultiRegisterStoreFloatScratchCount(MultiRegisterOperandValue(source, 2)),
                GenTreeKind.StoreIndirect => MultiRegisterStoreFloatScratchCount(MultiRegisterOperandValue(source, 1)),
                GenTreeKind.StoreField => MultiRegisterStoreFloatScratchCount(MultiRegisterOperandValue(source, 1)),
                GenTreeKind.StoreStaticField => MultiRegisterStoreFloatScratchCount(MultiRegisterOperandValue(source, 0)),
                GenTreeKind.Box => MultiRegisterStoreFloatScratchCount(MultiRegisterOperandValue(source, 0)),
                _ => 0,
            };

            if (count > byte.MaxValue)
                throw new InvalidOperationException($"Node {source.Id} requires too many internal float registers.");

            return (byte)count;
        }

        private static GenTree? MultiRegisterOperandValue(GenTree node, int operandIndex)
        {
            if ((uint)operandIndex >= (uint)node.Operands.Length)
                return null;

            var operand = node.Operands[operandIndex];
            var value = operand.RegisterResult ?? operand;
            return IsMultiRegisterValue(value) ? value : null;
        }

        private static bool IsMultiRegisterValue(GenTree? value)
        {
            if (value is null)
                return false;

            var abi = MachineAbi.ClassifyStorageValue(value.Type, value.StackKind);
            return abi.PassingKind == AbiValuePassingKind.MultiRegister;
        }

        private static bool MultiRegisterValueHasRegisterClass(GenTree? value, RegisterClass registerClass)
        {
            if (value is null)
                return false;

            var abi = MachineAbi.ClassifyStorageValue(value.Type, value.StackKind);
            if (abi.PassingKind != AbiValuePassingKind.MultiRegister)
                return false;

            var segments = MachineAbi.GetRegisterSegments(abi);
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i].RegisterClass == registerClass)
                    return true;
            }

            return false;
        }


        private static int ArrayElementLoadGeneralScratchCount(GenTree? result)
        {
            int count = MultiRegisterLoadGeneralScratchCount(result, needsAddressScratch: true);
            if (count != 0)
                return count;

            if (result is null)
                return 0;

            var abi = MachineAbi.ClassifyStorageValue(result.Type, result.StackKind);
            return abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect ? 1 : 0;
        }

        private static int MultiRegisterLoadGeneralScratchCount(GenTree? result, bool needsAddressScratch)
        {
            if (!IsMultiRegisterValue(result))
                return 0;

            int count = needsAddressScratch ? 1 : 0;
            if (MultiRegisterValueHasRegisterClass(result, RegisterClass.General))
                count++;
            return count;
        }

        private static int MultiRegisterLoadFloatScratchCount(GenTree? result)
            => MultiRegisterValueHasRegisterClass(result, RegisterClass.Float) ? 1 : 0;
        private static int StoreArrayElementGeneralScratchCount(GenTree source)
        {
            var value = StoreArrayElementOperandValue(source);

            int count = MultiRegisterStoreGeneralScratchCount(value, needsAddressScratch: true);
            if (count != 0)
                return count;
            if (value is null)
                return 0;

            var abi = MachineAbi.ClassifyStorageValue(value.Type, value.StackKind);
            return abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect ? 1 : 0;
        }
        private static GenTree? StoreArrayElementOperandValue(GenTree node)
        {
            if ((uint)2 >= (uint)node.Operands.Length)
                return null;

            var operand = node.Operands[2];
            return operand.RegisterResult ?? operand;
        }
        private static int MultiRegisterStoreGeneralScratchCount(GenTree? value, bool needsAddressScratch)
        {
            if (!IsMultiRegisterValue(value))
                return 0;

            int count = needsAddressScratch ? 1 : 0;
            if (MultiRegisterValueHasRegisterClass(value, RegisterClass.General))
                count++;
            return count;
        }

        private static int MultiRegisterStoreFloatScratchCount(GenTree? value)
            => MultiRegisterValueHasRegisterClass(value, RegisterClass.Float) ? 1 : 0;
    }

    internal readonly struct LinearLiveRange
    {
        public readonly int Start;
        public readonly int End;

        public LinearLiveRange(int start, int end)
        {
            if (end < start)
                throw new ArgumentOutOfRangeException(nameof(end));

            Start = start;
            End = end;
        }

        public bool IsEmpty => Start == End;
        public override string ToString() => "[" + Start + ", " + End + ")";
    }

    internal sealed class LinearLiveInterval
    {
        public GenTree Value { get; }
        public ImmutableArray<LinearLiveRange> Ranges { get; }
        public ImmutableArray<int> UsePositions { get; }
        public int DefinitionPosition { get; }

        public bool IsEmpty => Ranges.Length == 0;

        public LinearLiveInterval(GenTree value, ImmutableArray<LinearLiveRange> ranges, ImmutableArray<int> usePositions, int definitionPosition)
        {
            Value = value;
            Ranges = ranges.IsDefault ? ImmutableArray<LinearLiveRange>.Empty : ranges;
            UsePositions = usePositions.IsDefault ? ImmutableArray<int>.Empty : usePositions;
            DefinitionPosition = definitionPosition;
        }
    }


    internal enum LinearRefPositionKind : byte
    {
        Use,
        Def,

        Kill,

        Internal,
    }

    [Flags]
    internal enum LinearRefPositionFlags : uint
    {
        None = 0,
        FixedRegister = 1u << 0,
        RequiresRegister = 1u << 1,
        DelayFree = 1u << 2,
        LastUse = 1u << 3,
        GcRef = 1u << 4,
        ByRef = 1u << 5,
        StructByValue = 1u << 6,
        Internal = 1u << 7,
        Contained = 1u << 8,
        Address = 1u << 9,
        ExposedMemory = 1u << 10,
        WriteBarrier = 1u << 11,
        StackOnly = 1u << 12,
        RegOptional = 1u << 13,
        NoRegisterAtUse = 1u << 14,
        Reload = 1u << 15,
        Spill = 1u << 16,
    }

    internal readonly struct LinearRefPosition
    {
        public readonly int NodeId;
        public readonly int Position;
        public readonly int OperandIndex;
        public readonly int AbiSegmentIndex;
        public readonly int AbiSegmentOffset;
        public readonly int AbiSegmentSize;
        public readonly LinearRefPositionKind Kind;
        public readonly GenTree? Value;
        public readonly RegisterClass RegisterClass;
        public readonly MachineRegister FixedRegister;
        public readonly LinearRefPositionFlags Flags;
        public readonly ulong RegisterMask;
        public readonly byte MinimumRegisterCount;

        public LinearRefPosition(
            int nodeId,
            int position,
            int operandIndex,
            LinearRefPositionKind kind,
            GenTree? value,
            RegisterClass registerClass,
            MachineRegister fixedRegister,
            LinearRefPositionFlags flags,
            ulong registerMask = 0,
            byte minimumRegisterCount = 1)
            : this(
                nodeId,
                position,
                operandIndex,
                -1,
                0,
                0,
                kind,
                value,
                registerClass,
                fixedRegister,
                flags,
                registerMask,
                minimumRegisterCount)
        {
        }

        public LinearRefPosition(
            int nodeId,
            int position,
            int operandIndex,
            int abiSegmentIndex,
            int abiSegmentOffset,
            int abiSegmentSize,
            LinearRefPositionKind kind,
            GenTree? value,
            RegisterClass registerClass,
            MachineRegister fixedRegister,
            LinearRefPositionFlags flags,
            ulong registerMask = 0,
            byte minimumRegisterCount = 1)
        {
            if (abiSegmentIndex < -1)
                throw new ArgumentOutOfRangeException(nameof(abiSegmentIndex));
            if (abiSegmentOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(abiSegmentOffset));
            if (abiSegmentSize < 0)
                throw new ArgumentOutOfRangeException(nameof(abiSegmentSize));
            if (minimumRegisterCount == 0)
                throw new ArgumentOutOfRangeException(nameof(minimumRegisterCount));

            NodeId = nodeId;
            Position = position;
            OperandIndex = operandIndex;
            AbiSegmentIndex = abiSegmentIndex;
            AbiSegmentOffset = abiSegmentOffset;
            AbiSegmentSize = abiSegmentSize;
            Kind = kind;
            Value = value;
            RegisterClass = registerClass;
            FixedRegister = fixedRegister;
            Flags = flags;
            RegisterMask = registerMask != 0
                ? registerMask
                : fixedRegister != MachineRegister.Invalid
                    ? MachineRegisters.MaskOf(fixedRegister)
                    : MachineRegisters.DefaultMaskForClass(registerClass);
            MinimumRegisterCount = minimumRegisterCount;
        }

        public bool HasFlag(LinearRefPositionFlags flag) => (Flags & flag) != 0;
        public bool IsAbiSegment => AbiSegmentIndex >= 0;

        public override string ToString()
        {
            string value = Value is not null ? Value.ToString() : "_";
            string reg = FixedRegister == MachineRegister.Invalid ? "" : " @" + MachineRegisters.Format(FixedRegister);
            string abi = AbiSegmentIndex < 0 ? "" : $" seg{AbiSegmentIndex}+{AbiSegmentOffset}:{AbiSegmentSize}";
            string min = MinimumRegisterCount == 1 ? "" : $" minReg={MinimumRegisterCount}";
            string mask = RegisterMask == 0 ? "" : $" mask={MachineRegisters.FormatMask(RegisterMask)}";
            return $"#{NodeId}@{Position} {Kind}[{OperandIndex}]{value}{abi}{reg}{min}{mask} {Flags}";
        }
    }

    internal static class LinearBlockOrder
    {
        public static ImmutableArray<int> Compute(ControlFlowGraph cfg)
        {
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));

            var result = ImmutableArray.CreateBuilder<int>(cfg.Blocks.Length);
            var seen = new bool[cfg.Blocks.Length];

            void Add(int blockId)
            {
                if ((uint)blockId >= (uint)cfg.Blocks.Length)
                    throw new InvalidOperationException($"CFG block order contains invalid block id B{blockId}.");
                if (seen[blockId])
                    return;
                seen[blockId] = true;
                result.Add(blockId);
            }

            if (cfg.Blocks.Length != 0)
                Add(0);

            for (int i = 0; i < cfg.ReversePostOrder.Length; i++)
                Add(cfg.ReversePostOrder[i]);

            for (int blockId = 0; blockId < cfg.Blocks.Length; blockId++)
                Add(blockId);

            return result.ToImmutable();
        }

        public static ImmutableArray<int> Normalize(ControlFlowGraph cfg, ImmutableArray<int> order)
        {
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));

            if (order.IsDefaultOrEmpty)
                return Compute(cfg);

            var result = ImmutableArray.CreateBuilder<int>(cfg.Blocks.Length);
            var seen = new bool[cfg.Blocks.Length];

            for (int i = 0; i < order.Length; i++)
            {
                int blockId = order[i];
                if ((uint)blockId >= (uint)cfg.Blocks.Length)
                    throw new InvalidOperationException($"GenTree LIR block order contains invalid block id B{blockId}.");
                if (seen[blockId])
                    throw new InvalidOperationException($"GenTree LIR block order contains duplicate block id B{blockId}.");
                seen[blockId] = true;
                result.Add(blockId);
            }

            for (int blockId = 0; blockId < cfg.Blocks.Length; blockId++)
            {
                if (!seen[blockId])
                    result.Add(blockId);
            }

            return result.ToImmutable();
        }
    }

    internal sealed class LinearRationalizationOptions
    {
        public static LinearRationalizationOptions Default => new LinearRationalizationOptions();

        public bool IncludeExceptionEdges { get; set; } = true;

        public bool Validate { get; set; } = true;
    }

    internal static class GenTreeLinearIrRationalizer
    {
        public static GenTreeMethod RationalizeMethod(GenTreeMethod method, SsaMethod? ssa = null, LinearRationalizationOptions? options = null)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            options ??= LinearRationalizationOptions.Default;

            ControlFlowGraph cfg;
            if (ssa is not null)
            {
                method = ssa.GenTreeMethod;
                if (method.Phase < GenTreeMethodPhase.Ssa)
                    throw new InvalidOperationException("Rationalization received SSA data that is not attached to the GenTree method.");
                cfg = ssa.Cfg;
            }
            else
            {
                if (method.Phase < GenTreeMethodPhase.HirLiveness)
                    throw new InvalidOperationException("Rationalization requires HIR liveness or SSA. Run GenTreeBackendPipeline.PrepareHir before HIR->LIR rationalization.");
                cfg = method.Cfg;
            }

            var builder = new MethodBuilder(method, cfg, ssa);
            return builder.Run();
        }

        private sealed class MethodBuilder
        {
            private readonly GenTreeMethod _method;
            private readonly ControlFlowGraph _cfg;
            private readonly SsaMethod? _ssa;
            private readonly Dictionary<GenTreeValueKey, GenTreeValueInfo> _valueInfos = new();
            private readonly Dictionary<SsaSlot, SsaSlotInfo> _ssaSlotInfos = new();
            private readonly Dictionary<SsaValueName, SsaValueDefinition> _ssaDefinitions = new();
            private readonly Dictionary<SsaValueName, GenTree> _ssaValues = new();
            private readonly HashSet<SsaValueName> _usedSsaValues = new();
            private readonly List<GenTree> _allNodes = new();
            private readonly List<GenTree>[] _nodesByBlock;
            private int _nextNodeId;
            private int _nextSyntheticTreeId;
            private int _currentBlockId;
            private int _currentBlockOrdinal;

            public MethodBuilder(GenTreeMethod method, ControlFlowGraph cfg, SsaMethod? ssa = null)
            {
                _method = method;
                _cfg = cfg;
                _ssa = ssa;
                _nodesByBlock = new List<GenTree>[method.Blocks.Length];
                for (int i = 0; i < _nodesByBlock.Length; i++)
                    _nodesByBlock[i] = new List<GenTree>();
                _nextSyntheticTreeId = ComputeNextSyntheticTreeId(method);
                if (ssa is not null)
                    _nextSyntheticTreeId = Math.Max(_nextSyntheticTreeId, ComputeNextSyntheticTreeId(ssa));

                if (ssa is not null)
                {
                    for (int i = 0; i < ssa.Slots.Length; i++)
                        _ssaSlotInfos[ssa.Slots[i].Slot] = ssa.Slots[i];
                    for (int i = 0; i < ssa.ValueDefinitions.Length; i++)
                        _ssaDefinitions[ssa.ValueDefinitions[i].Name] = ssa.ValueDefinitions[i];
                }
            }

            public GenTreeMethod Run()
            {
                ResetExistingLinearState();
                if (_ssa is not null)
                    PrepareSsaValues();

                LowerBlocks();

                if (_ssa is not null)
                    EmitPhiCopies();

                return Freeze();
            }

            private static int ComputeNextSyntheticTreeId(GenTreeMethod method)
            {
                int max = -1;
                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    var statements = method.Blocks[b].Statements;
                    for (int s = 0; s < statements.Length; s++)
                        Visit(statements[s]);
                }
                return max + 1;

                void Visit(GenTree node)
                {
                    if (node.Id > max)
                        max = node.Id;
                    for (int i = 0; i < node.Operands.Length; i++)
                        Visit(node.Operands[i]);
                }
            }

            private static int ComputeNextSyntheticTreeId(SsaMethod method)
            {
                int max = -1;
                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    var statements = method.Blocks[b].Statements;
                    for (int s = 0; s < statements.Length; s++)
                        Visit(statements[s]);
                }
                return max + 1;

                void Visit(SsaTree tree)
                {
                    if (tree.Source.Id > max)
                        max = tree.Source.Id;
                    for (int i = 0; i < tree.Operands.Length; i++)
                        Visit(tree.Operands[i]);
                }
            }

            private void ResetExistingLinearState()
            {
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var statements = _method.Blocks[b].Statements;
                    for (int s = 0; s < statements.Length; s++)
                        Reset(statements[s]);
                }

                static void Reset(GenTree node)
                {
                    node.ResetLinearState();
                    node.ClearSsaAnnotation();
                    for (int i = 0; i < node.Operands.Length; i++)
                        Reset(node.Operands[i]);
                }
            }

            private void PrepareSsaValues()
            {
                if (_ssa is null)
                    return;

                CollectUsedSsaValues();

                for (int b = 0; b < _ssa.Blocks.Length; b++)
                {
                    var block = _ssa.Blocks[b];
                    for (int p = 0; p < block.Phis.Length; p++)
                    {
                        var phi = block.Phis[p];
                        if (!_ssaValues.ContainsKey(phi.Target))
                            _ssaValues[phi.Target] = CreateSsaPlaceholderValueNode(phi.Target);
                    }

                    for (int s = 0; s < block.Statements.Length; s++)
                        AttachSsaAnnotations(block.Statements[s]);
                }
            }

            private void CollectUsedSsaValues()
            {
                if (_ssa is null)
                    return;

                for (int b = 0; b < _ssa.Blocks.Length; b++)
                {
                    var block = _ssa.Blocks[b];
                    for (int p = 0; p < block.Phis.Length; p++)
                    {
                        var phi = block.Phis[p];
                        for (int i = 0; i < phi.Inputs.Length; i++)
                            _usedSsaValues.Add(phi.Inputs[i].Value);
                    }

                    for (int s = 0; s < block.Statements.Length; s++)
                        CollectUsedSsaValues(block.Statements[s]);
                }
            }

            private void CollectUsedSsaValues(SsaTree tree)
            {
                if (tree.Value.HasValue)
                    _usedSsaValues.Add(tree.Value.Value);

                if (tree.LocalFieldBaseValue.HasValue)
                    _usedSsaValues.Add(tree.LocalFieldBaseValue.Value);

                for (int i = 0; i < tree.Operands.Length; i++)
                    CollectUsedSsaValues(tree.Operands[i]);
            }

            private void AttachSsaAnnotations(SsaTree tree)
            {
                if (tree.Value.HasValue)
                {
                    tree.Source.AttachSsaUse(tree.Value.Value);
                }

                for (int i = 0; i < tree.Operands.Length; i++)
                    AttachSsaAnnotations(tree.Operands[i]);

                if (tree.StoreTarget.HasValue)
                {
                    var target = tree.StoreTarget.Value;
                    var info = GetSsaSlotInfo(target.Slot);
                    tree.Source.AttachSsaDefinition(target, info.Type, info.StackKind);
                    AttachSsaDescriptor(tree.Source, target.Slot);
                    _ssaValues[target] = tree.Source;
                    EnsureSsaValueInfo(target, tree.Source);
                }
            }

            private SsaSlotInfo GetSsaSlotInfo(SsaSlot slot)
            {
                if (_ssaSlotInfos.TryGetValue(slot, out var info))
                    return info;
                return new SsaSlotInfo(slot, type: null, stackKind: GenStackKind.Unknown, addressExposed: false);
            }

            private bool TryGetSsaDescriptor(SsaSlot slot, out GenLocalDescriptor descriptor)
            {
                if (slot.HasLclNum)
                {
                    var all = _method.AllLocalDescriptors;
                    if ((uint)slot.LclNum < (uint)all.Length)
                    {
                        descriptor = all[slot.LclNum];
                        return descriptor.Kind switch
                        {
                            GenLocalKind.Argument => slot.Kind == SsaSlotKind.Arg,
                            GenLocalKind.Local => slot.Kind == SsaSlotKind.Local,
                            GenLocalKind.Temporary => slot.Kind == SsaSlotKind.Temp,
                            _ => false,
                        };
                    }
                }

                switch (slot.Kind)
                {
                    case SsaSlotKind.Arg:
                        if ((uint)slot.Index < (uint)_method.ArgDescriptors.Length)
                        {
                            descriptor = _method.ArgDescriptors[slot.Index];
                            return true;
                        }
                        break;

                    case SsaSlotKind.Local:
                        if ((uint)slot.Index < (uint)_method.LocalDescriptors.Length)
                        {
                            descriptor = _method.LocalDescriptors[slot.Index];
                            return true;
                        }
                        break;

                    case SsaSlotKind.Temp:
                        for (int i = 0; i < _method.TempDescriptors.Length; i++)
                        {
                            if (_method.TempDescriptors[i].Index == slot.Index)
                            {
                                descriptor = _method.TempDescriptors[i];
                                return true;
                            }
                        }
                        break;
                }

                descriptor = null!;
                return false;
            }

            private void AttachSsaDescriptor(GenTree node, SsaSlot slot)
            {
                if (node.LocalDescriptor is null && TryGetSsaDescriptor(slot, out var descriptor))
                    node.LocalDescriptor = descriptor;
            }

            private GenTree CreateSsaInitialValueNode(SsaValueName value)
            {
                var info = GetSsaSlotInfo(value.Slot);
                var kind = ToLoadKind(value.Slot.Kind);
                var node = new GenTree(
                    _nextSyntheticTreeId++,
                    kind,
                    pc: -1,
                    BytecodeOp.Nop,
                    info.Type,
                    info.StackKind,
                    GenTreeFlags.LocalUse | GenTreeFlags.Ordered,
                    ImmutableArray<GenTree>.Empty,
                    int32: value.Slot.Index);
                node.AttachSsaUse(value);
                AttachSsaDescriptor(node, value.Slot);
                EnsureSsaValueInfo(value, node);
                return node;
            }

            private GenTree CreateSsaPlaceholderValueNode(SsaValueName value)
            {
                var info = GetSsaSlotInfo(value.Slot);
                var node = new GenTree(
                    _nextSyntheticTreeId++,
                    GenTreeKind.Nop,
                    pc: -1,
                    BytecodeOp.Nop,
                    info.Type,
                    info.StackKind,
                    GenTreeFlags.None,
                    ImmutableArray<GenTree>.Empty);
                node.AttachSsaDefinition(value, info.Type, info.StackKind);
                AttachSsaDescriptor(node, value.Slot);
                EnsureSsaValueInfo(value, node);
                return node;
            }

            private static GenTreeKind ToLoadKind(SsaSlotKind kind)
                => kind switch
                {
                    SsaSlotKind.Arg => GenTreeKind.Arg,
                    SsaSlotKind.Local => GenTreeKind.Local,
                    SsaSlotKind.Temp => GenTreeKind.Temp,
                    _ => GenTreeKind.Nop,
                };

            private GenTree GetOrCreateSsaValue(SsaValueName value, GenTree? suggestedNode = null)
            {
                if (_ssaValues.TryGetValue(value, out var existing))
                {
                    EnsureSsaValueInfo(value, existing);
                    return existing;
                }

                GenTree node;
                if (suggestedNode is not null)
                {
                    var info = GetSsaSlotInfo(value.Slot);
                    suggestedNode.AttachSsaUse(value);
                    suggestedNode.Type = info.Type;
                    suggestedNode.StackKind = info.StackKind;
                    AttachSsaDescriptor(suggestedNode, value.Slot);
                    node = suggestedNode;
                }
                else if (_ssaDefinitions.TryGetValue(value, out var definition) && definition.IsInitial)
                {
                    node = CreateSsaInitialValueNode(value);
                }
                else if (_ssaDefinitions.TryGetValue(value, out definition) && definition.IsPhi)
                {
                    node = CreateSsaPlaceholderValueNode(value);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"SSA value {value} has no lowered defining node.");
                }

                _ssaValues[value] = node;
                EnsureSsaValueInfo(value, node);
                return node;
            }

            private void EnsureSsaValueInfo(SsaValueName value, GenTree representative)
            {
                var key = GenTreeValueKey.ForSsaValue(value);
                if (_valueInfos.ContainsKey(key))
                    return;

                var slotInfo = GetSsaSlotInfo(value.Slot);
                int defBlock = -1;
                int defNode = -1;
                if (_ssaDefinitions.TryGetValue(value, out var definition))
                {
                    defBlock = definition.DefBlockId;
                    defNode = definition.DefTreeId;
                }

                _valueInfos.Add(key, new GenTreeValueInfo(
                    key,
                    representative,
                    slotInfo.Type,
                    slotInfo.StackKind,
                    ResolveRegisterClass(slotInfo.Type, slotInfo.StackKind),
                    defBlock,
                    defNode));
            }

            private static RegisterClass ResolveRegisterClass(RuntimeType? type, GenStackKind stackKind)
            {
                AbiValueInfo abi = MachineAbi.ClassifyStorageValue(type, stackKind);
                if (abi.PassingKind == AbiValuePassingKind.ScalarRegister && abi.RegisterClass != RegisterClass.Invalid)
                    return abi.RegisterClass;

                if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    var segments = MachineAbi.GetRegisterSegments(abi);
                    if (segments.Length != 0)
                    {
                        RegisterClass cls = segments[0].RegisterClass;
                        bool homogeneous = cls != RegisterClass.Invalid;
                        for (int i = 1; i < segments.Length; i++)
                        {
                            if (segments[i].RegisterClass != cls)
                            {
                                homogeneous = false;
                                break;
                            }
                        }
                        if (homogeneous)
                            return cls;
                    }
                }

                return stackKind is GenStackKind.R4 or GenStackKind.R8
                    ? RegisterClass.Float
                    : RegisterClass.General;
            }

            private void LowerBlocks()
            {
                if (_ssa is null)
                {
                    LowerGenTreeBlocks();
                    return;
                }

                for (int b = 0; b < _ssa.Blocks.Length; b++)
                {
                    var block = _ssa.Blocks[b];
                    _currentBlockId = block.Id;
                    _currentBlockOrdinal = _nodesByBlock[_currentBlockId].Count;

                    for (int s = 0; s < block.Statements.Length; s++)
                        LowerStatement(block.Statements[s]);
                }
            }

            private void LowerGenTreeBlocks()
            {
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var block = _method.Blocks[b];
                    _currentBlockId = block.Id;
                    _currentBlockOrdinal = _nodesByBlock[_currentBlockId].Count;

                    for (int s = 0; s < block.Statements.Length; s++)
                        LowerStatement(block.Statements[s]);
                }
            }

            private void LowerStatement(GenTree tree)
            {
                if (tree is null)
                    throw new ArgumentNullException(nameof(tree));

                if (tree.Kind == GenTreeKind.Eval)
                {
                    LowerEval(tree);
                    return;
                }

                if (IsControlTransfer(tree))
                {
                    LowerControlTransfer(tree);
                    return;
                }

                if (MustMaterializeForSideEffects(tree))
                {
                    _ = LowerValueOrVoid(tree);
                    return;
                }

                LowerForSideEffects(tree);
            }

            private void LowerStatement(SsaTree tree)
            {
                if (tree is null)
                    throw new ArgumentNullException(nameof(tree));

                if (tree.Value.HasValue)
                {
                    _ = LowerValueOrVoid(tree);
                    return;
                }

                if (tree.Source.Kind == GenTreeKind.Eval)
                {
                    LowerEval(tree);
                    return;
                }

                if (IsControlTransfer(tree.Source))
                {
                    LowerControlTransfer(tree);
                    return;
                }

                if (tree.StoreTarget.HasValue || MustMaterializeForSideEffects(tree.Source))
                {
                    _ = LowerValueOrVoid(tree);
                    return;
                }

                LowerForSideEffects(tree);
            }

            private void LowerEval(GenTree tree)
            {
                for (int i = 0; i < tree.Operands.Length; i++)
                    LowerForSideEffects(tree.Operands[i]);
            }

            private void LowerEval(SsaTree tree)
            {
                for (int i = 0; i < tree.Operands.Length; i++)
                    LowerForSideEffects(tree.Operands[i]);
            }

            private void LowerForSideEffects(GenTree tree)
            {
                if (IsControlTransfer(tree))
                {
                    LowerControlTransfer(tree);
                    return;
                }

                if (MustMaterializeForSideEffects(tree))
                {
                    _ = LowerValueOrVoid(tree);
                    return;
                }

                for (int i = 0; i < tree.Operands.Length; i++)
                    LowerForSideEffects(tree.Operands[i]);
            }

            private void LowerForSideEffects(SsaTree tree)
            {
                if (tree.Value.HasValue)
                    return;

                if (IsControlTransfer(tree.Source))
                {
                    LowerControlTransfer(tree);
                    return;
                }

                if (tree.StoreTarget.HasValue || MustMaterializeForSideEffects(tree.Source))
                {
                    _ = LowerValueOrVoid(tree);
                    return;
                }

                for (int i = 0; i < tree.Operands.Length; i++)
                    LowerForSideEffects(tree.Operands[i]);
            }

            private static bool MustMaterializeForSideEffects(GenTree tree)
            {
                if (tree.HasSideEffect || tree.CanThrow || tree.ContainsCall)
                    return true;

                if ((tree.Flags & (GenTreeFlags.MemoryWrite | GenTreeFlags.Allocation | GenTreeFlags.ControlFlow | GenTreeFlags.ExceptionFlow | GenTreeFlags.Ordered)) != 0)
                    return true;

                return tree.Kind is
                    GenTreeKind.Call or
                    GenTreeKind.VirtualCall or
                    GenTreeKind.DelegateInvoke or
                    GenTreeKind.NewObject or
                    GenTreeKind.NewDelegate or
                    GenTreeKind.NewArray or
                    GenTreeKind.StoreIndirect or
                    GenTreeKind.StoreLocal or
                    GenTreeKind.StoreArg or
                    GenTreeKind.StoreTemp or
                    GenTreeKind.StoreField or
                    GenTreeKind.StoreStaticField or
                    GenTreeKind.StoreArrayElement or
                    GenTreeKind.Throw or
                    GenTreeKind.Rethrow or
                    GenTreeKind.EndFinally;
            }

            private void LowerControlTransfer(GenTree tree)
            {
                var uses = LowerOperands(tree);

                if (IsBackwardBranch(tree))
                    EmitGcPoll(tree);

                GenTree? result = ProducesValue(tree)
                    ? NewTemp(tree)
                    : null;

                EmitTree(tree, uses, result);
            }

            private void LowerControlTransfer(SsaTree tree)
            {
                var uses = LowerOperands(tree);

                if (IsBackwardBranch(tree.Source))
                    EmitGcPoll(tree.Source);

                GenTree? result = ProducesValue(tree.Source)
                    ? NewTemp(tree.Source)
                    : null;

                EmitTree(tree.Source, uses, result);
            }

            private ImmutableArray<LirOperandFlags> LowerOperands(GenTree tree)
            {
                if (TryLowerCommutativeBinaryOperands(tree, out var binaryOperands))
                    return binaryOperands;

                var flags = ImmutableArray.CreateBuilder<LirOperandFlags>(tree.Operands.Length);
                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    var operandTree = tree.Operands[i];
                    if (TryCreateContainedOperand(tree, i, operandTree, out var containedFlags))
                    {
                        flags.Add(containedFlags);
                        continue;
                    }

                    _ = LowerValueOrVoid(operandTree);
                    flags.Add(LirOperandFlags.None);
                }
                return flags.ToImmutable();
            }

            private ImmutableArray<LirOperandFlags> LowerOperands(SsaTree tree)
            {
                if (TryLowerCommutativeBinaryOperands(tree, out var binaryOperands))
                    return binaryOperands;

                if (tree.LocalFieldBaseValue.HasValue && SsaSlotHelpers.TryGetLocalFieldAccess(tree.Source, out var localFieldAccess))
                    return LowerLocalFieldOperands(tree, localFieldAccess);

                var flags = ImmutableArray.CreateBuilder<LirOperandFlags>(tree.Operands.Length);
                var sources = ImmutableArray.CreateBuilder<GenTree>(tree.Operands.Length);
                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    var operandTree = tree.Operands[i];
                    sources.Add(operandTree.Source);
                    if (TryCreateContainedOperand(tree.Source, i, operandTree.Source, out var containedFlags))
                    {
                        flags.Add(containedFlags);
                        continue;
                    }

                    var value = LowerValueOrVoid(operandTree);
                    if (value is not null)
                        operandTree.Source.RegisterResult = value;
                    flags.Add(LirOperandFlags.None);
                }

                tree.Source.SetOperands(sources.ToImmutable());
                return flags.ToImmutable();
            }

            private ImmutableArray<LirOperandFlags> LowerLocalFieldOperands(SsaTree tree, SsaLocalAccess localFieldAccess)
            {
                var originalOperands = tree.Source.Operands;
                var flags = ImmutableArray.CreateBuilder<LirOperandFlags>(originalOperands.Length);
                var sources = ImmutableArray.CreateBuilder<GenTree>(originalOperands.Length);

                for (int originalIndex = 0, ssaIndex = 0; originalIndex < originalOperands.Length; originalIndex++)
                {
                    if (originalIndex == localFieldAccess.ReceiverOperandIndex)
                    {
                        var receiver = originalOperands[originalIndex];
                        sources.Add(receiver);
                        var value = LowerValueOrVoid(receiver);
                        if (value is not null)
                            receiver.RegisterResult = value;
                        flags.Add(LirOperandFlags.None);
                        continue;
                    }

                    if ((uint)ssaIndex >= (uint)tree.Operands.Length)
                        throw new InvalidOperationException("SSA local-field operand mapping is malformed for node " + tree.Source.Id.ToString() + ".");

                    var operandTree = tree.Operands[ssaIndex++];
                    sources.Add(operandTree.Source);
                    if (TryCreateContainedOperand(tree.Source, originalIndex, operandTree.Source, out var containedFlags))
                    {
                        flags.Add(containedFlags);
                        continue;
                    }
                    {
                        var value = LowerValueOrVoid(operandTree);
                        if (value is not null)
                            operandTree.Source.RegisterResult = value;
                    }

                    flags.Add(LirOperandFlags.None);
                }

                if (sources.Count - 1 > tree.Operands.Length)
                    throw new InvalidOperationException("SSA local-field operand mapping has too many source operands for node " + tree.Source.Id.ToString() + ".");

                tree.Source.SetOperands(sources.ToImmutable());
                return flags.ToImmutable();
            }

            private bool TryLowerCommutativeBinaryOperands(GenTree tree, out ImmutableArray<LirOperandFlags> operands)
            {
                operands = default;

                if (tree.Kind != GenTreeKind.Binary || tree.Operands.Length != 2)
                    return false;

                if (!IsCommutativeBinaryImmediateOp(tree.SourceOp))
                    return false;

                var left = tree.Operands[0];
                var right = tree.Operands[1];

                if (!CanContainBinaryImmediate(tree, operandIndex: 1, left))
                    return false;

                if (CanContainBinaryImmediate(tree, operandIndex: 1, right))
                    return false;

                _ = LowerValue(right);
                left.IsContainedInLinear = true;
                tree.SetOperands(ImmutableArray.Create(right, left));
                operands = ImmutableArray.Create(LirOperandFlags.None, LirOperandFlags.Contained);
                return true;
            }

            private bool TryLowerCommutativeBinaryOperands(SsaTree tree, out ImmutableArray<LirOperandFlags> operands)
            {
                operands = default;

                if (tree.Source.Kind != GenTreeKind.Binary || tree.Operands.Length != 2)
                    return false;

                if (!IsCommutativeBinaryImmediateOp(tree.Source.SourceOp))
                    return false;

                var left = tree.Operands[0];
                var right = tree.Operands[1];

                if (!CanContainBinaryImmediate(tree.Source, operandIndex: 1, left.Source))
                    return false;

                if (CanContainBinaryImmediate(tree.Source, operandIndex: 1, right.Source))
                    return false;

                var rightValue = LowerValue(right);
                right.Source.RegisterResult = rightValue;
                left.Source.IsContainedInLinear = true;
                tree.Source.SetOperands(ImmutableArray.Create(right.Source, left.Source));
                operands = ImmutableArray.Create(LirOperandFlags.None, LirOperandFlags.Contained);
                return true;
            }

            private static bool TryCreateContainedOperand(GenTree parent, int operandIndex, GenTree operand, out LirOperandFlags result)
            {
                if (CanContainBinaryImmediate(parent, operandIndex, operand))
                {
                    operand.IsContainedInLinear = true;
                    result = LirOperandFlags.Contained;
                    return true;
                }

                result = LirOperandFlags.None;
                return false;
            }

            private static bool CanContainBinaryImmediate(GenTree parent, int operandIndex, GenTree operand)
            {
                if (parent.Kind != GenTreeKind.Binary || operandIndex != 1)
                    return false;

                if (operand.Operands.Length != 0)
                    return false;

                if (operand.Kind is not (GenTreeKind.ConstI4 or GenTreeKind.ConstI8))
                    return false;

                if (IsFloatLike(parent.Type, parent.StackKind) || parent.StackKind is GenStackKind.Ref or GenStackKind.Null)
                    return false;

                if (parent.SourceOp is BytecodeOp.Div or BytecodeOp.Div_Un or BytecodeOp.Rem or BytecodeOp.Rem_Un)
                    return false;

                if (parent.SourceOp == BytecodeOp.Cgt_Un)
                    return false;

                return IsBinaryImmediateOp(parent.SourceOp);
            }

            private static bool IsBinaryImmediateOp(BytecodeOp op)
                => op is
                    BytecodeOp.Add or BytecodeOp.Sub or BytecodeOp.Mul or
                    BytecodeOp.And or BytecodeOp.Or or BytecodeOp.Xor or
                    BytecodeOp.Shl or BytecodeOp.Shr or BytecodeOp.Shr_Un or
                    BytecodeOp.Ceq or BytecodeOp.Clt or BytecodeOp.Clt_Un or BytecodeOp.Cgt;

            private static bool IsCommutativeBinaryImmediateOp(BytecodeOp op)
                => op is BytecodeOp.Add or BytecodeOp.Mul or BytecodeOp.And or BytecodeOp.Or or BytecodeOp.Xor or BytecodeOp.Ceq;

            private static bool IsFloatLike(RuntimeType? type, GenStackKind stackKind)
                => stackKind is GenStackKind.R4 or GenStackKind.R8 || type?.Name is "Single" or "Double";

            private GenTree LowerValue(GenTree tree)
            {
                var result = LowerValueOrVoid(tree);
                if (result is null)
                    throw new InvalidOperationException($"Tree node {tree.Id} ({tree.Kind}) does not produce a value.");
                return result;
            }

            private GenTree LowerValue(SsaTree tree)
            {
                var result = LowerValueOrVoid(tree);
                if (result is null)
                    throw new InvalidOperationException($"Tree node {tree.Source.Id} ({tree.Source.Kind}) does not produce a value.");
                return result;
            }

            private GenTree? LowerValueOrVoid(GenTree tree)
            {
                var uses = LowerOperands(tree);

                GenTree? result = ProducesValue(tree) ? NewTemp(tree) : null;

                EmitTree(tree, uses, result);
                return result;
            }

            private GenTree? LowerValueOrVoid(SsaTree tree)
            {
                if (tree.Value.HasValue)
                {
                    var value = GetOrCreateSsaValue(tree.Value.Value, tree.Source);
                    tree.Source.RegisterResult = value;
                    tree.Source.ValueKey = value.ValueKey;
                    return value;
                }

                var uses = LowerOperands(tree);

                if (tree.StoreTarget.HasValue)
                {
                    var targetName = tree.StoreTarget.Value;
                    var target = GetOrCreateSsaValue(targetName, tree.Source);
                    var info = GetSsaSlotInfo(targetName.Slot);
                    tree.Source.AttachSsaDefinition(targetName, info.Type, info.StackKind);
                    tree.Source.RegisterResult = target;
                    EmitTree(tree.Source, uses, target);
                    return target;
                }

                GenTree? result = ProducesValue(tree.Source) ? NewTemp(tree.Source) : null;

                EmitTree(tree.Source, uses, result);
                return result;
            }

            private GenTree NewTemp(GenTree source)
            {
                return GetOrCreateGenTree(source);
            }

            private GenTree GetOrCreateGenTree(GenTree source)
            {
                var key = ValueKeyForNode(source);
                if (_valueInfos.TryGetValue(key, out var existing))
                    return existing.RepresentativeNode;

                _valueInfos.Add(key, new GenTreeValueInfo(
                    key,
                    source,
                    source.Type,
                    source.StackKind,
                    ResolveRegisterClass(source.Type, source.StackKind),
                    _currentBlockId,
                    definitionNodeId: key.IsTreeNode ? -1 : source.LinearId));

                return source;
            }

            private static GenTreeValueKey ValueKeyForNode(GenTree source)
                => source.LinearValueKey;

            private GenTree EmitTree(GenTree tree, ImmutableArray<LirOperandFlags> operands, GenTree? result)
            {
                int id = _nextNodeId++;
                int ordinal = _currentBlockOrdinal++;
                operands = operands.IsDefault ? ImmutableArray<LirOperandFlags>.Empty : operands;
                var uses = BuildUses(tree, operands);
                var memoryAccess = GenTreeLinearLoweringClassifier.ClassifyMemoryAccess(tree);
                var lowering = GenTreeLinearLoweringClassifier.Classify(tree, result, uses, _currentBlockId, memoryAccess);
                tree.SetLinearState(id, _currentBlockId, ordinal, GenTreeLinearKind.Tree, result, operands, uses, lowering, memoryAccess);
                RecordNode(tree);
                return tree;
            }

            private GenTree EmitGcPoll(GenTree? sourceTree)
            {
                var pollTree = new GenTree(
                    _nextSyntheticTreeId++,
                    GenTreeKind.Nop,
                    sourceTree?.Pc ?? -1,
                    BytecodeOp.Nop,
                    type: null,
                    stackKind: GenStackKind.Void,
                    flags: GenTreeFlags.SideEffect | GenTreeFlags.Ordered,
                    operands: ImmutableArray<GenTree>.Empty);

                var lowering = new GenTreeLinearLoweringInfo(
                    GenTreeLinearFlags.IsStandaloneLoweredNode |
                    GenTreeLinearFlags.CallerSavedKill |
                    GenTreeLinearFlags.GcSafePoint,
                    0,
                    0);
                pollTree.SetLinearState(
                    _nextNodeId++,
                    _currentBlockId,
                    _currentBlockOrdinal++,
                    GenTreeLinearKind.GcPoll,
                    result: null,
                    ImmutableArray<LirOperandFlags>.Empty,
                    ImmutableArray<GenTree>.Empty,
                    lowering,
                    LinearMemoryAccess.None);
                RecordNode(pollTree);
                return pollTree;
            }

            private void EmitPhiCopies()
            {
                if (_ssa is null)
                    return;

                for (int b = 0; b < _ssa.Blocks.Length; b++)
                {
                    var block = _ssa.Blocks[b];
                    for (int p = 0; p < block.Phis.Length; p++)
                    {
                        var phi = block.Phis[p];
                        var target = GetOrCreateSsaValue(phi.Target);

                        for (int i = 0; i < phi.Inputs.Length; i++)
                        {
                            var input = phi.Inputs[i];
                            if (input.Value.Equals(phi.Target))
                                continue;

                            var source = GetOrCreateSsaValue(input.Value);
                            EmitPhiCopy(input.PredecessorBlockId, block.Id, source, target);
                        }
                    }
                }
            }

            private void EmitPhiCopy(int fromBlockId, int toBlockId, GenTree source, GenTree destination)
            {
                if ((uint)fromBlockId >= (uint)_nodesByBlock.Length)
                    throw new InvalidOperationException($"SSA phi input references invalid predecessor B{fromBlockId}.");

                var copy = new GenTree(
                    _nextSyntheticTreeId++,
                    GenTreeKind.Copy,
                    pc: -1,
                    BytecodeOp.Nop,
                    destination.Type ?? source.Type,
                    destination.StackKind != GenStackKind.Unknown ? destination.StackKind : source.StackKind,
                    GenTreeFlags.Ordered,
                    ImmutableArray.Create(source));

                var uses = ImmutableArray.Create(source);
                var lowering = new GenTreeLinearLoweringInfo(GenTreeLinearFlags.IsStandaloneLoweredNode, 0, 0);
                int placementBlockId = SelectPhiCopyPlacementBlock(fromBlockId, toBlockId);
                copy.SetLinearState(
                    _nextNodeId++,
                    placementBlockId,
                    ordinal: 0,
                    GenTreeLinearKind.Copy,
                    destination,
                    ImmutableArray<LirOperandFlags>.Empty,
                    uses,
                    lowering,
                    LinearMemoryAccess.None,
                    phiCopyFromBlockId: fromBlockId,
                    phiCopyToBlockId: toBlockId);

                if (placementBlockId == fromBlockId)
                    InsertNodeBeforeTerminator(fromBlockId, copy);
                else
                    InsertNodeAtBlockEntry(toBlockId, copy);

                _allNodes.Add(copy);
                UpdateDefinitionInfo(copy);
            }

            private int SelectPhiCopyPlacementBlock(int fromBlockId, int toBlockId)
            {
                if (CountNormalSuccessors(fromBlockId) <= 1)
                    return fromBlockId;

                if (CountNormalPredecessors(toBlockId) <= 1)
                    return toBlockId;

                throw new InvalidOperationException(
                    $"SSA phi copy for critical edge B{fromBlockId}->B{toBlockId} requires CFG edge splitting before LIR/LSRA.");
            }

            private int CountNormalSuccessors(int blockId)
            {
                int count = 0;
                var successors = _cfg.Blocks[blockId].Successors;
                for (int i = 0; i < successors.Length; i++)
                {
                    if (successors[i].Kind != CfgEdgeKind.Exception)
                        count++;
                }
                return count;
            }

            private int CountNormalPredecessors(int blockId)
            {
                int count = 0;
                var predecessors = _cfg.Blocks[blockId].Predecessors;
                for (int i = 0; i < predecessors.Length; i++)
                {
                    if (predecessors[i].Kind != CfgEdgeKind.Exception)
                        count++;
                }
                return count;
            }

            private void InsertNodeAtBlockEntry(int blockId, GenTree node)
            {
                var list = _nodesByBlock[blockId];
                int index = 0;
                while (index < list.Count && list[index].IsPhiCopy)
                    index++;
                list.Insert(index, node);
            }

            private void InsertNodeBeforeTerminator(int blockId, GenTree node)
            {
                var list = _nodesByBlock[blockId];
                int index = list.Count;
                if (index > 0 && IsControlTransfer(list[index - 1]))
                    index--;
                list.Insert(index, node);
            }

            private static ImmutableArray<GenTree> BuildUses(GenTree tree, ImmutableArray<LirOperandFlags> operandFlags)
            {
                if (tree.Operands.IsDefaultOrEmpty)
                    return ImmutableArray<GenTree>.Empty;

                if (!operandFlags.IsDefault && operandFlags.Length != tree.Operands.Length)
                    throw new InvalidOperationException($"GenTree LIR node {tree.Id} has {tree.Operands.Length} operands but {operandFlags.Length} operand flag entries.");

                var builder = ImmutableArray.CreateBuilder<GenTree>(tree.Operands.Length);
                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    var flags = operandFlags.IsDefaultOrEmpty ? LirOperandFlags.None : operandFlags[i];
                    if ((flags & LirOperandFlags.Contained) != 0)
                        continue;

                    var operandTree = tree.Operands[i];
                    if (operandTree.RegisterResult is not null)
                        builder.Add(operandTree.RegisterResult);
                }
                return builder.ToImmutable();
            }

            private bool IsBackwardBranch(GenTree source)
            {
                if (source.Kind is not (GenTreeKind.Branch or GenTreeKind.BranchTrue or GenTreeKind.BranchFalse))
                    return false;

                return source.TargetBlockId >= 0 && source.TargetBlockId <= _currentBlockId;
            }

            private void RecordNode(GenTree node)
            {
                _nodesByBlock[_currentBlockId].Add(node);
                _allNodes.Add(node);
                UpdateDefinitionInfo(node);
            }

            private void UpdateDefinitionInfo(GenTree node)
            {
                if (node.RegisterResult is not null)
                {
                    var value = ValueKeyForNode(node.RegisterResult);
                    if (_valueInfos.TryGetValue(value, out var info))
                    {
                        _valueInfos[value] = info.WithDefinitionNode(info.RepresentativeNode, node.LinearBlockId, node.LinearId);
                    }
                }
            }

            private static bool IsControlTransfer(GenTree source)
                => source.Kind is GenTreeKind.Branch or GenTreeKind.BranchTrue or GenTreeKind.BranchFalse or
                   GenTreeKind.Return or GenTreeKind.Throw or GenTreeKind.Rethrow or GenTreeKind.EndFinally;

            private GenTreeMethod Freeze()
            {
                _allNodes.Clear();
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var nodes = _nodesByBlock[b].ToImmutableArray();
                    _method.Blocks[b].SetLinearNodes(nodes);
                    for (int i = 0; i < nodes.Length; i++)
                        _allNodes.Add(nodes[i]);
                }

                NormalizeRepresentativeValueKeys();

                var values = new List<GenTreeValueInfo>(_valueInfos.Values);
                values.Sort(static (a, b) => string.Compare(a.Value.ToString(), b.Value.ToString(), StringComparison.Ordinal));

                _method.AttachLinearBackendState(
                    _cfg,
                    _allNodes.ToImmutableArray(),
                    values.ToImmutableArray(),
                    new Dictionary<GenTreeValueKey, GenTreeValueInfo>(_valueInfos),
                    LinearBlockOrder.Compute(_cfg),
                    _ssa);
                return _method;
            }

            private void NormalizeRepresentativeValueKeys()
            {
                foreach (var kv in _valueInfos)
                {
                    var value = kv.Key;
                    var representative = kv.Value.RepresentativeNode;
                    if (!representative.LinearValueKey.Equals(value))
                        representative.ValueKey = value;
                }
            }

            private static bool ProducesValue(GenTree node)
            {
                if (node.StackKind == GenStackKind.Void)
                    return false;

                return node.Kind switch
                {
                    GenTreeKind.Nop => false,
                    GenTreeKind.StoreIndirect => false,
                    GenTreeKind.StoreLocal => false,
                    GenTreeKind.StoreArg => false,
                    GenTreeKind.StoreTemp => false,
                    GenTreeKind.StoreField => false,
                    GenTreeKind.StoreStaticField => false,
                    GenTreeKind.StoreArrayElement => false,
                    GenTreeKind.Eval => false,
                    GenTreeKind.Branch => false,
                    GenTreeKind.BranchTrue => false,
                    GenTreeKind.BranchFalse => false,
                    GenTreeKind.Return => false,
                    GenTreeKind.Throw => false,
                    GenTreeKind.Rethrow => false,
                    GenTreeKind.EndFinally => false,
                    _ => true,
                };
            }
        }
    }



    internal static class GenTreeLinearLowerer
    {
        public static GenTreeMethod LowerMethod(GenTreeMethod rationalizedMethod, LinearRationalizationOptions? options = null)
        {
            if (rationalizedMethod is null)
                throw new ArgumentNullException(nameof(rationalizedMethod));

            options ??= LinearRationalizationOptions.Default;
            if (rationalizedMethod.Phase < GenTreeMethodPhase.RationalizedLir)
                throw new InvalidOperationException("Lowering requires a rationalized LIR method. Run GenTreeLinearIrRationalizer.RationalizeMethod first.");
            if (rationalizedMethod.LinearNodes.IsDefaultOrEmpty && rationalizedMethod.Blocks.Length != 0)
                throw new InvalidOperationException("Lowering requires rationalized LIR nodes. Call GenTreeLinearIrRationalizer.RationalizeMethod first.");

            var result = LinearLiveness.Attach(rationalizedMethod);

            if (options.Validate)
                LinearVerifier.Verify(result);

            return result;
        }
    }

    internal static class LinearLiveness
    {
        public static GenTreeMethod Attach(GenTreeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            var intervals = BuildIntervals(method, out var intervalMap);
            MarkUnusedValueDefinitions(method, intervalMap);
            var refPositions = BuildRefPositions(method, intervalMap);
            method.AttachLiveness(intervals, intervalMap, refPositions);
            return method;
        }


        public static ImmutableArray<LinearRefPosition> BuildRefPositions(
            GenTreeMethod method,
            IReadOnlyDictionary<GenTree, LinearLiveInterval>? intervals = null)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            intervals ??= method.LiveIntervalByNode;
            var layout = PositionLayout.Build(method);
            var result = ImmutableArray.CreateBuilder<LinearRefPosition>();

            for (int i = 0; i < method.LinearNodes.Length; i++)
            {
                var node = method.LinearNodes[i];
                int usePosition = layout.NodePositions[node.LinearId];
                int defPosition = usePosition + 1;

                AddInternalRegisterRefPositions(result, node, usePosition);

                if (node.LinearKind == GenTreeLinearKind.Copy)
                {
                    if (node.RegisterResult is null || node.RegisterUses.Length != 1)
                        throw new InvalidOperationException("linear IR copy node must have one source and one destination value.");

                    AddValueCopyRefPositions(
                        result,
                        method,
                        intervals,
                        node.LinearId,
                        usePosition,
                        defPosition,
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
                {
                    AddRegisterKills(result, node, usePosition, RegisterClass.General, MachineRegisters.CallerSavedGprs);
                    AddRegisterKills(result, node, usePosition, RegisterClass.Float, MachineRegisters.CallerSavedFprs);
                }
            }

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

            if (IsSsaLocalValue(sourceValue) && sourceAbi.PassingKind == AbiValuePassingKind.ScalarRegister)
                sourceFlags |= LinearRefPositionFlags.RequiresRegister;
            if (IsSsaLocalValue(destinationValue) && destinationAbi.PassingKind == AbiValuePassingKind.ScalarRegister)
                destinationFlags |= LinearRefPositionFlags.RequiresRegister;

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

            if (node.RegisterResult is not null && IsSsaLocalValue(node.RegisterResult))
                return true;

            return node.Kind switch
            {
                GenTreeKind.Local => false,
                GenTreeKind.Arg => false,
                GenTreeKind.Temp => false,
                GenTreeKind.StoreLocal => false,
                GenTreeKind.StoreArg => false,
                GenTreeKind.StoreTemp => false,
                GenTreeKind.DefaultValue => false,
                _ => true,
            };
        }

        private static bool IsSsaLocalValue(GenTree value)
            => value.LinearValueKey.IsSsaValue;

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
                if (node.Kind == GenTreeKind.NewObject && node.Method?.DeclaringType.IsValueType == true)
                    AddHiddenReturnBufferDefRefPosition(result, node.LinearId, defPosition, value, info);
                else
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

        private static void AddHiddenReturnBufferDefRefPosition(
            ImmutableArray<LinearRefPosition>.Builder result,
            int nodeId,
            int defPosition,
            GenTree value,
            GenTreeValueInfo info)
        {
            var flags = GetValueRefFlags(info) |
                        LinearRefPositionFlags.StackOnly |
                        LinearRefPositionFlags.ExposedMemory;
            var registerClass = info.RegisterClass == RegisterClass.Invalid
                ? RegisterClass.General
                : info.RegisterClass;

            result.Add(new LinearRefPosition(
                nodeId,
                defPosition,
                -1,
                LinearRefPositionKind.Def,
                value,
                registerClass,
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
            RegisterClass registerClass,
            ImmutableArray<MachineRegister> registers)
        {
            for (int i = 0; i < registers.Length; i++)
            {
                result.Add(new LinearRefPosition(
                    node.LinearId,
                    position,
                    i,
                    LinearRefPositionKind.Kill,
                    value: null,
                    registerClass,
                    registers[i],
                    LinearRefPositionFlags.FixedRegister | LinearRefPositionFlags.Internal));
            }
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
            out IReadOnlyDictionary<GenTree, LinearLiveInterval> intervalMap)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            var layout = PositionLayout.Build(method);
            var liveIn = NewSetArray(method.Blocks.Length);
            var liveOut = NewSetArray(method.Blocks.Length);
            var blockUses = NewSetArray(method.Blocks.Length);
            var blockDefs = NewSetArray(method.Blocks.Length);
            var localUseEnds = NewPositionMapArray(method.Blocks.Length);
            var localDefStarts = NewPositionMapArray(method.Blocks.Length);
            var usePositions = new Dictionary<GenTree, SortedSet<int>>();
            var defPositions = new Dictionary<GenTree, int>();

            for (int i = 0; i < method.Values.Length; i++)
            {
                var info = method.Values[i];
                var value = info.RepresentativeNode;
                usePositions[value] = new SortedSet<int>();
                defPositions[value] = ComputeInitialDefinitionPosition(layout, info);
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
                        {
                            var copy = block.LinearNodes[i];
                            bool hasCopyDef = copy.RegisterResult is not null;
                            for (int u = 0; u < copy.RegisterUses.Length; u++)
                            {
                                var use = copy.RegisterUses[u];
                                int useEnd = ComputeUseEnd(copy, u, usePos, defPos, hasCopyDef);
                                RecordUse(usePositions, localUseEnds[b], use, usePos, useEnd);
                                if (!blockDefs[b].Contains(use))
                                    blockUses[b].Add(use);
                            }
                        }

                        for (int i = n; i <= groupEnd; i++)
                        {
                            var copy = block.LinearNodes[i];
                            var resultValue = copy.RegisterResult;
                            if (resultValue is not null)
                                RecordDefinition(blockDefs[b], localDefStarts[b], defPositions, resultValue, defPos);
                        }

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

            var dataflowOrder = method.LinearBlockOrder.IsDefaultOrEmpty
                ? LinearBlockOrder.Compute(method.Cfg)
                : method.LinearBlockOrder;

            bool changed;
            do
            {
                changed = false;
                for (int r = dataflowOrder.Length - 1; r >= 0; r--)
                {
                    int blockId = dataflowOrder[r];
                    var newOut = new HashSet<GenTree>();
                    var successors = method.Cfg.Blocks[blockId].Successors;
                    for (int s = 0; s < successors.Length; s++)
                        newOut.UnionWith(liveIn[successors[s].ToBlockId]);

                    var newIn = new HashSet<GenTree>(newOut);
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

            var ranges = new Dictionary<GenTree, List<LinearLiveRange>>();
            for (int i = 0; i < method.Values.Length; i++)
                ranges[method.Values[i].RepresentativeNode] = new List<LinearLiveRange>();

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                int blockStart = layout.BlockStartPositions[b];
                int blockEnd = layout.BlockEndPositions[b] + 1;
                var values = new HashSet<GenTree>();
                values.UnionWith(liveIn[b]);
                values.UnionWith(liveOut[b]);
                foreach (var kv in localUseEnds[b])
                    values.Add(kv.Key);
                foreach (var kv in localDefStarts[b])
                    values.Add(kv.Key);

                foreach (var value in values)
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

        private static int ComputeInitialDefinitionPosition(PositionLayout layout, GenTreeValueInfo info)
        {
            if (info.DefinitionNodeId >= 0 && layout.NodePositions.TryGetValue(info.DefinitionNodeId, out int nodePos))
                return nodePos + 1;

            if ((uint)info.DefinitionBlockId < (uint)layout.BlockStartPositions.Length)
                return layout.BlockStartPositions[info.DefinitionBlockId];

            return layout.FirstPosition;
        }

        private static int ComputeUseEnd(GenTree node, int operandIndex, int usePosition, int defPosition, bool hasDef)
        {
            int end = usePosition + 1;

            if (hasDef &&
                (node.HasLoweringFlag(GenTreeLinearFlags.RequiresRegisterOperands) ||
                 node.LinearMemoryAccess.HasAddressOperand(operandIndex) ||
                 node.LinearMemoryAccess.HasValueOperand(operandIndex)))
            {
                end = defPosition + 1;
            }

            return end;
        }

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

        private sealed class PositionLayout
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

    internal static class LinearVerifier
    {
        public static void Verify(GenTreeProgram program)
        {
            if (program is null)
                throw new ArgumentNullException(nameof(program));

            for (int i = 0; i < program.Methods.Length; i++)
                Verify(program.Methods[i]);
        }

        public static void Verify(GenTreeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            VerifyCore(method);
        }

        public static void VerifyBeforeLsra(GenTreeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            VerifyCore(method);
            VerifyLoweringInvariants(method);
        }

        private static void VerifyCore(GenTreeMethod method)
        {
            VerifyBlocks(method);
            VerifyLinearBlockOrder(method);
            VerifyOperands(method);
            VerifyValues(method);
            VerifySsaBinding(method);
            VerifyRefPositions(method);
        }


        private static void VerifyLoweringInvariants(GenTreeMethod method)
        {
            var useRefsByNodeAndUse = new Dictionary<(int NodeId, int UseIndex), List<LinearRefPosition>>();
            var defRefsByNode = new Dictionary<int, List<LinearRefPosition>>();
            var internalRefsByNode = new Dictionary<int, List<LinearRefPosition>>();

            for (int i = 0; i < method.RefPositions.Length; i++)
            {
                var rp = method.RefPositions[i];
                switch (rp.Kind)
                {
                    case LinearRefPositionKind.Use:
                        if (!useRefsByNodeAndUse.TryGetValue((rp.NodeId, rp.OperandIndex), out var uses))
                        {
                            uses = new List<LinearRefPosition>();
                            useRefsByNodeAndUse.Add((rp.NodeId, rp.OperandIndex), uses);
                        }
                        uses.Add(rp);
                        break;

                    case LinearRefPositionKind.Def:
                        if (!defRefsByNode.TryGetValue(rp.NodeId, out var defs))
                        {
                            defs = new List<LinearRefPosition>();
                            defRefsByNode.Add(rp.NodeId, defs);
                        }
                        defs.Add(rp);
                        break;

                    case LinearRefPositionKind.Internal:
                        if (!internalRefsByNode.TryGetValue(rp.NodeId, out var internals))
                        {
                            internals = new List<LinearRefPosition>();
                            internalRefsByNode.Add(rp.NodeId, internals);
                        }
                        internals.Add(rp);
                        break;
                }
            }

            var lastUsePositions = new Dictionary<GenTreeValueKey, int>();
            foreach (var interval in method.LiveIntervals)
            {
                var key = interval.Value.LinearValueKey;
                lastUsePositions[key] = interval.UsePositions.Length == 0 ? -1 : interval.UsePositions[interval.UsePositions.Length - 1];
            }

            var seenLoweringNodeIds = new HashSet<int>();
            for (int n = 0; n < method.LinearNodes.Length; n++)
            {
                var node = method.LinearNodes[n];
                if ((uint)node.LinearId >= (uint)method.LinearNodes.Length)
                    throw new InvalidOperationException($"pre-LSRA lowering invariant failed: node list index {n} contains out-of-range linear node id {node.LinearId}.");

                if (!seenLoweringNodeIds.Add(node.LinearId))
                    throw new InvalidOperationException($"pre-LSRA lowering invariant failed: duplicate linear node id {node.LinearId}.");

                if (node.LinearKind == GenTreeLinearKind.Tree && !node.HasLoweringFlag(GenTreeLinearFlags.IsStandaloneLoweredNode))
                    throw new InvalidOperationException($"pre-LSRA lowering invariant failed: node {node.LinearId} is not marked as standalone lowered LIR.");

                VerifyContainedAndRegOptionalOperands(method, node, useRefsByNodeAndUse);
                VerifyInternalRegisterInvariants(node, internalRefsByNode.TryGetValue(node.LinearId, out var internals) ? internals : null);
                VerifyUnusedValueInvariant(method, node, defRefsByNode.TryGetValue(node.LinearId, out var defs) ? defs : null);
            }

            VerifyLastUseInvariants(method, lastUsePositions);
        }

        private static void VerifyContainedAndRegOptionalOperands(
            GenTreeMethod method,
            GenTree node,
            IReadOnlyDictionary<(int NodeId, int UseIndex), List<LinearRefPosition>> useRefsByNodeAndUse)
        {
            if (node.OperandFlags.IsDefaultOrEmpty || node.Operands.IsDefaultOrEmpty)
                return;

            bool registerOnly = node.HasLoweringFlag(GenTreeLinearFlags.RequiresRegisterOperands);
            int flagCount = Math.Min(node.OperandFlags.Length, node.Operands.Length);
            int useIndex = 0;

            for (int operandIndex = 0; operandIndex < node.Operands.Length; operandIndex++)
            {
                var operandFlags = operandIndex < flagCount ? node.OperandFlags[operandIndex] : LirOperandFlags.None;
                bool contained = (operandFlags & LirOperandFlags.Contained) != 0;
                bool regOptional = (operandFlags & LirOperandFlags.RegOptional) != 0;

                if (contained && regOptional)
                {
                    throw new InvalidOperationException(
                        $"pre-LSRA lowering invariant failed: node {node.LinearId} operand {operandIndex} is both contained and reg-optional.");
                }

                var operand = node.Operands[operandIndex];
                var value = operand.RegisterResult;

                if (contained)
                {
                    if (!operand.IsContainedInLinear)
                    {
                        throw new InvalidOperationException(
                            $"pre-LSRA lowering invariant failed: node {node.LinearId} operand {operandIndex} is marked contained, " +
                            $"but operand node {operand.LinearId} is not marked contained-in-linear.");
                    }

                    if (value is not null)
                    {
                        throw new InvalidOperationException(
                            $"pre-LSRA lowering invariant failed: contained operand {operandIndex} of node {node.LinearId} still exposes register value {value.LinearValueKey}.");
                    }

                    continue;
                }

                if (value is null)
                    continue;

                if (useIndex >= node.RegisterUses.Length || !node.RegisterUses[useIndex].Equals(value))
                {
                    throw new InvalidOperationException(
                        $"pre-LSRA lowering invariant failed: node {node.LinearId} operand {operandIndex} does not map to register-use index {useIndex}.");
                }

                if (regOptional)
                {
                    if (registerOnly)
                    {
                        throw new InvalidOperationException(
                            $"pre-LSRA lowering invariant failed: node {node.LinearId} operand {operandIndex} is reg-optional on a register-only node.");
                    }

                    if (!useRefsByNodeAndUse.TryGetValue((node.LinearId, useIndex), out var refs) || refs.Count == 0)
                    {
                        throw new InvalidOperationException(
                            $"pre-LSRA lowering invariant failed: reg-optional operand {operandIndex} of node {node.LinearId} has no use ref-position.");
                    }

                    for (int r = 0; r < refs.Count; r++)
                    {
                        var rp = refs[r];
                        if (!rp.HasFlag(LinearRefPositionFlags.RegOptional) || rp.HasFlag(LinearRefPositionFlags.RequiresRegister))
                        {
                            throw new InvalidOperationException(
                                $"pre-LSRA lowering invariant failed: reg-optional operand {operandIndex} of node {node.LinearId} produced incompatible ref-position {rp}.");
                        }
                    }
                }

                useIndex++;
            }
        }

        private static void VerifyInternalRegisterInvariants(GenTree node, List<LinearRefPosition>? internalRefs)
        {
            byte expectedGeneral = node.LinearLowering.InternalGeneralRegisters;
            byte expectedFloat = node.LinearLowering.InternalFloatRegisters;
            byte actualGeneral = 0;
            byte actualFloat = 0;

            if (internalRefs is not null)
            {
                for (int i = 0; i < internalRefs.Count; i++)
                {
                    var rp = internalRefs[i];
                    if (!rp.HasFlag(LinearRefPositionFlags.Internal) || !rp.HasFlag(LinearRefPositionFlags.RequiresRegister) || rp.Value is not null)
                    {
                        throw new InvalidOperationException(
                            $"pre-LSRA lowering invariant failed: node {node.LinearId} has malformed internal ref-position {rp}.");
                    }

                    if (rp.RegisterClass == RegisterClass.General)
                        actualGeneral += rp.MinimumRegisterCount;
                    else if (rp.RegisterClass == RegisterClass.Float)
                        actualFloat += rp.MinimumRegisterCount;
                    else
                        throw new InvalidOperationException($"pre-LSRA lowering invariant failed: node {node.LinearId} has invalid internal register class {rp.RegisterClass}.");
                }
            }

            if (actualGeneral != expectedGeneral || actualFloat != expectedFloat)
            {
                throw new InvalidOperationException(
                    $"pre-LSRA lowering invariant failed: node {node.LinearId} internal register refs are gen={actualGeneral}, float={actualFloat}; " +
                    $"lowering requested gen={expectedGeneral}, float={expectedFloat}.");
            }
        }

        private static void VerifyUnusedValueInvariant(GenTreeMethod method, GenTree node, List<LinearRefPosition>? defRefs)
        {
            if (node.RegisterResults.Length == 0)
            {
                if (node.HasLoweringFlag(GenTreeLinearFlags.UnusedValue))
                    throw new InvalidOperationException($"pre-LSRA lowering invariant failed: node {node.LinearId} is marked unused but defines no value.");
                return;
            }

            bool allResultsUnused = true;
            for (int r = 0; r < node.RegisterResults.Length; r++)
            {
                var result = node.RegisterResults[r];
                if (!method.LiveIntervalByNode.TryGetValue(result, out var interval))
                    throw new InvalidOperationException($"pre-LSRA lowering invariant failed: node {node.LinearId} defines {result.LinearValueKey} without a live interval.");

                if (interval.UsePositions.Length != 0)
                    allResultsUnused = false;
            }

            bool markedUnused = node.HasLoweringFlag(GenTreeLinearFlags.UnusedValue);
            if (allResultsUnused != markedUnused)
            {
                throw new InvalidOperationException(
                    $"pre-LSRA lowering invariant failed: node {node.LinearId} unused flag is {markedUnused}, expected {allResultsUnused}.");
            }

            if (!allResultsUnused && (defRefs is null || defRefs.Count == 0))
                throw new InvalidOperationException($"pre-LSRA lowering invariant failed: used result of node {node.LinearId} has no def ref-position.");
        }

        private static void VerifyLastUseInvariants(GenTreeMethod method, IReadOnlyDictionary<GenTreeValueKey, int> lastUsePositions)
        {
            foreach (var rp in method.RefPositions)
            {
                if (rp.Kind != LinearRefPositionKind.Use || rp.Value is null)
                    continue;

                if (!lastUsePositions.TryGetValue(rp.Value.LinearValueKey, out int lastUsePosition))
                    throw new InvalidOperationException($"pre-LSRA lowering invariant failed: use ref-position {rp} references a value without interval.");

                bool expected = rp.Position == lastUsePosition;
                bool actual = rp.HasFlag(LinearRefPositionFlags.LastUse);
                if (expected != actual)
                {
                    throw new InvalidOperationException(
                        $"pre-LSRA lowering invariant failed: use ref-position {rp} last-use flag is {actual}, expected {expected}.");
                }
            }
        }

        private static void VerifyBlocks(GenTreeMethod method)
        {
            if (method.Blocks.Length != method.Cfg.Blocks.Length)
                throw new InvalidOperationException("linear IR block count does not match CFG block count.");

            var seenNodes = new HashSet<int>();

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var block = method.Blocks[b];
                if (block.Id != b)
                    throw new InvalidOperationException($"linear IR requires dense block ids. Expected B{b}, found B{block.Id}.");

                for (int i = 0; i < block.LinearNodes.Length; i++)
                {
                    var node = block.LinearNodes[i];
                    if ((uint)node.LinearId >= (uint)method.LinearNodes.Length)
                        throw new InvalidOperationException($"GenTree GenTree LIR node id {node.LinearId} is outside method node table length {method.LinearNodes.Length}.");

                    if (!seenNodes.Add(node.LinearId))
                        throw new InvalidOperationException("Duplicate GenTree GenTree LIR node id " + node.LinearId + ".");

                    if (node.LinearBlockId != block.Id)
                        throw new InvalidOperationException($"GenTree GenTree LIR node {node.LinearId} is stored in B{block.Id} but says B{node.LinearBlockId}.");

                    if (node.IsPhiCopy)
                    {
                        if ((uint)node.LinearPhiCopyFromBlockId >= (uint)method.Blocks.Length ||
                            (uint)node.LinearPhiCopyToBlockId >= (uint)method.Blocks.Length)
                        {
                            throw new InvalidOperationException(
                                $"linear IR phi copy node {node.LinearId} has invalid edge B{node.LinearPhiCopyFromBlockId}->B{node.LinearPhiCopyToBlockId}.");
                        }

                        if (node.LinearBlockId != node.LinearPhiCopyFromBlockId && node.LinearBlockId != node.LinearPhiCopyToBlockId)
                        {
                            throw new InvalidOperationException(
                                $"linear IR phi copy node {node.LinearId} for edge B{node.LinearPhiCopyFromBlockId}->B{node.LinearPhiCopyToBlockId} " +
                                $"is placed in unrelated B{node.LinearBlockId}.");
                        }
                    }

                    if (node.LinearOrdinal != i)
                        throw new InvalidOperationException($"GenTree GenTree LIR node {node.LinearId} in B{block.Id} has ordinal {node.LinearOrdinal}, expected {i}.");

                    if (node.Previous != (i == 0 ? null : block.LinearNodes[i - 1]))
                        throw new InvalidOperationException($"Broken previous link at GenTree GenTree LIR node {node.LinearId}.");

                    if (node.Next != (i + 1 == block.LinearNodes.Length ? null : block.LinearNodes[i + 1]))
                        throw new InvalidOperationException($"Broken next link at GenTree GenTree LIR node {node.LinearId}.");
                }
            }

            if (seenNodes.Count != method.LinearNodes.Length)
                throw new InvalidOperationException("linear IR method node list does not match block node lists.");

            VerifyGenTreeLinks(method);
        }

        private static void VerifyGenTreeLinks(GenTreeMethod method)
        {
            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var block = method.Blocks[b];
                for (int i = 0; i < block.LinearNodes.Length; i++)
                {
                    var tree = block.LinearNodes[i];
                    if (tree.LinearId < 0)
                        throw new InvalidOperationException($"GenTree node {tree.Id} in B{block.Id} is missing linear IR identity.");
                    if (tree.LinearBlockId != block.Id)
                        throw new InvalidOperationException($"GenTree node {tree.Id} is stored in B{block.Id} but says B{tree.LinearBlockId}.");
                    if (tree.LinearOrdinal != i)
                        throw new InvalidOperationException($"GenTree node {tree.Id} in B{block.Id} has linear IR ordinal {tree.LinearOrdinal}, expected {i}.");
                    if (tree.Previous != (i == 0 ? null : block.LinearNodes[i - 1]))
                        throw new InvalidOperationException($"Broken GenTree previous link at node {tree.Id}.");
                    if (tree.Next != (i + 1 == block.LinearNodes.Length ? null : block.LinearNodes[i + 1]))
                        throw new InvalidOperationException($"Broken GenTree next link at node {tree.Id}.");
                }
            }
        }

        private static void VerifyLinearBlockOrder(GenTreeMethod method)
        {
            if (method.LinearBlockOrder.Length != method.Blocks.Length)
                throw new InvalidOperationException("linear IR linear block order does not cover every block.");

            var seen = new bool[method.Blocks.Length];
            for (int i = 0; i < method.LinearBlockOrder.Length; i++)
            {
                int blockId = method.LinearBlockOrder[i];
                if ((uint)blockId >= (uint)method.Blocks.Length)
                    throw new InvalidOperationException("linear IR linear block order contains invalid block id B" + blockId.ToString() + ".");
                if (seen[blockId])
                    throw new InvalidOperationException("linear IR linear block order contains duplicate block id B" + blockId.ToString() + ".");
                seen[blockId] = true;
            }
        }

        private static void VerifyOperands(GenTreeMethod method)
        {
            foreach (var node in method.LinearNodes)
            {
                if (node.LinearKind != GenTreeLinearKind.Tree)
                    continue;

                if (!node.OperandFlags.IsDefault && node.OperandFlags.Length != node.Operands.Length)
                    throw new InvalidOperationException($"GenTree GenTree LIR node {node.LinearId} has operand flag count {node.OperandFlags.Length} but operand count {node.Operands.Length}.");

                int useIndex = 0;
                for (int i = 0; i < node.Operands.Length; i++)
                {
                    var flags = node.OperandFlags.IsDefaultOrEmpty ? LirOperandFlags.None : node.OperandFlags[i];
                    if ((flags & LirOperandFlags.Contained) != 0)
                        continue;

                    var operandTree = node.Operands[i];
                    if (operandTree.RegisterResult is null)
                        continue;

                    if (useIndex >= node.RegisterUses.Length || !node.RegisterUses[useIndex].Equals(operandTree.RegisterResult))
                        throw new InvalidOperationException($"GenTree GenTree LIR node {node.LinearId} operand/use mapping is inconsistent at operand {i}.");

                    useIndex++;
                }

                if (useIndex != node.RegisterUses.Length)
                    throw new InvalidOperationException($"GenTree GenTree LIR node {node.LinearId} has {node.RegisterUses.Length} uses but only {useIndex} non-contained operands.");
            }
        }

        private static void VerifyValues(GenTreeMethod method)
        {
            var declared = new HashSet<GenTreeValueKey>();
            var tempDefinitions = new Dictionary<GenTree, GenTree>();
            var tempUseCounts = new Dictionary<GenTree, int>();
            var tempUseBlocks = new Dictionary<GenTree, int>();

            for (int i = 0; i < method.Values.Length; i++)
            {
                var info = method.Values[i];
                if (!declared.Add(info.Value))
                    throw new InvalidOperationException("Duplicate GenTree value " + info.Value + ".");

                if (!info.RepresentativeNode.LinearValueKey.Equals(info.Value))
                {
                    throw new InvalidOperationException(
                        "GenTree value " + info.Value + " is represented by node " + info.RepresentativeNode +
                        " whose linear key is " + info.RepresentativeNode.LinearValueKey + ".");
                }
            }

            foreach (var node in method.LinearNodes)
            {
                if (node.RegisterResult is not null)
                {
                    var result = node.RegisterResult;
                    var resultKey = result.LinearValueKey;
                    if (!declared.Contains(resultKey))
                        throw new InvalidOperationException($"Node {node.LinearId} defines undeclared value {resultKey}.");

                    if (method.GetValueInfo(resultKey).Origin == GenTreeValueOrigin.TreeNode)
                    {
                        if (tempDefinitions.ContainsKey(result))
                            throw new InvalidOperationException("GenTree LIR tree node " + result + " has multiple definitions.");
                        tempDefinitions.Add(result, node);
                    }
                }

                for (int i = 0; i < node.RegisterUses.Length; i++)
                {
                    var use = node.RegisterUses[i];
                    var useKey = use.LinearValueKey;
                    if (!declared.Contains(useKey))
                        throw new InvalidOperationException($"Node {node.LinearId} uses undeclared value {useKey}.");

                    if (method.GetValueInfo(useKey).Origin == GenTreeValueOrigin.TreeNode)
                    {
                        tempUseCounts.TryGetValue(use, out int count);
                        count++;
                        tempUseCounts[use] = count;
                        tempUseBlocks[use] = node.LinearBlockId;
                        if (count > 1)
                        {
                            throw new InvalidOperationException($"GenTree LIR tree node {use} has more than one use.");
                        }
                    }
                }
            }

            foreach (var kv in tempUseCounts)
            {
                if (!tempDefinitions.TryGetValue(kv.Key, out var defNode))
                    throw new InvalidOperationException($"GenTree LIR tree node {kv.Key} is used without a definition.");

                if (tempUseBlocks[kv.Key] != defNode.LinearBlockId)
                {
                    throw new InvalidOperationException($"GenTree LIR tree node {kv.Key} crosses a basic-block boundary.");
                }
            }
        }


        private static void VerifySsaBinding(GenTreeMethod method)
        {
            var ssa = method.Ssa;
            if (ssa is null)
                return;

            var declaredSsaValues = new HashSet<SsaValueName>();
            for (int i = 0; i < ssa.InitialValues.Length; i++)
                declaredSsaValues.Add(ssa.InitialValues[i]);
            for (int i = 0; i < ssa.ValueDefinitions.Length; i++)
                declaredSsaValues.Add(ssa.ValueDefinitions[i].Name);

            for (int i = 0; i < method.Values.Length; i++)
            {
                var key = method.Values[i].Value;
                if (!key.IsSsaValue)
                    continue;

                var name = new SsaValueName(key.SsaSlot, key.SsaVersion);
                if (!declaredSsaValues.Contains(name))
                    throw new InvalidOperationException("linear IR contains SSA value " + name + " that is not declared by the SSA side table.");
            }

            var expectedPhiCopies = new Dictionary<(int from, int to, GenTreeValueKey source, GenTreeValueKey destination), int>();
            for (int b = 0; b < ssa.Blocks.Length; b++)
            {
                var block = ssa.Blocks[b];
                for (int p = 0; p < block.Phis.Length; p++)
                {
                    var phi = block.Phis[p];
                    var destination = GenTreeValueKey.ForSsaValue(phi.Target);
                    for (int i = 0; i < phi.Inputs.Length; i++)
                    {
                        var input = phi.Inputs[i];
                        if (input.Value.Equals(phi.Target))
                            continue;

                        var key = (input.PredecessorBlockId, phi.BlockId, GenTreeValueKey.ForSsaValue(input.Value), destination);
                        expectedPhiCopies.TryGetValue(key, out int count);
                        expectedPhiCopies[key] = count + 1;
                    }
                }
            }

            var actualPhiCopies = new Dictionary<(int from, int to, GenTreeValueKey source, GenTreeValueKey destination), int>();
            foreach (var node in method.LinearNodes)
            {
                VerifyPromotedValueIdentity(method, node, node.RegisterResult, isDefinition: true);
                for (int u = 0; u < node.RegisterUses.Length; u++)
                    VerifyPromotedValueIdentity(method, node, node.RegisterUses[u], isDefinition: false);

                if (node.SsaStoreTargetName.HasValue)
                {
                    if (node.RegisterResult is null)
                        throw new InvalidOperationException("SSA store node " + node.LinearId + " has no register result value.");

                    var targetKey = GenTreeValueKey.ForSsaValue(node.SsaStoreTargetName.Value);
                    if (!node.RegisterResult.LinearValueKey.Equals(targetKey))
                        throw new InvalidOperationException("SSA store node " + node.LinearId + " defines " + node.RegisterResult.LinearValueKey + " but is annotated as " + targetKey + ".");
                }

                if (!node.IsPhiCopy)
                    continue;

                if (node.RegisterUses.Length != 1 || node.RegisterResult is null)
                    throw new InvalidOperationException("SSA phi copy node " + node.LinearId + " must have one source and one destination.");

                var source = node.RegisterUses[0].LinearValueKey;
                var destination = node.RegisterResult.LinearValueKey;
                if (!source.IsSsaValue || !destination.IsSsaValue)
                    throw new InvalidOperationException("SSA phi copy node " + node.LinearId + " must copy SSA values, got " + source + " -> " + destination + ".");

                var key = (node.LinearPhiCopyFromBlockId, node.LinearPhiCopyToBlockId, source, destination);
                actualPhiCopies.TryGetValue(key, out int count);
                actualPhiCopies[key] = count + 1;
            }

            foreach (var kv in expectedPhiCopies)
            {
                actualPhiCopies.TryGetValue(kv.Key, out int actual);
                if (actual != kv.Value)
                    throw new InvalidOperationException("Missing or duplicated SSA phi edge copy for B" + kv.Key.from + "->B" + kv.Key.to + " " + kv.Key.source + " -> " + kv.Key.destination + ".");
            }

            foreach (var kv in actualPhiCopies)
            {
                expectedPhiCopies.TryGetValue(kv.Key, out int expected);
                if (expected != kv.Value)
                    throw new InvalidOperationException("Unexpected SSA phi edge copy for B" + kv.Key.from + "->B" + kv.Key.to + " " + kv.Key.source + " -> " + kv.Key.destination + ".");
            }
        }

        private static void VerifyPromotedValueIdentity(GenTreeMethod method, GenTree owner, GenTree? value, bool isDefinition)
        {
            if (value is null)
                return;

            var descriptor = value.LocalDescriptor;
            if (descriptor is null || !descriptor.SsaPromoted)
                return;

            if (!value.LinearValueKey.IsSsaValue)
            {
                string direction = isDefinition ? "defines" : "uses";
                throw new InvalidOperationException("linear IR node " + owner.LinearId + " " + direction + " promoted local " + descriptor + " through a raw local value key " + value.LinearValueKey + ".");
            }

            var key = value.LinearValueKey;
            bool sameSlot = key.SsaSlot.HasLclNum
                ? key.SsaSlot.LclNum == descriptor.LclNum
                : descriptor.Kind switch
                {
                    GenLocalKind.Argument => key.SsaSlot.Kind == SsaSlotKind.Arg && key.SsaSlot.Index == descriptor.Index,
                    GenLocalKind.Local => key.SsaSlot.Kind == SsaSlotKind.Local && key.SsaSlot.Index == descriptor.Index,
                    GenLocalKind.Temporary => key.SsaSlot.Kind == SsaSlotKind.Temp && key.SsaSlot.Index == descriptor.Index,
                    _ => false,
                };

            if (!sameSlot)
                throw new InvalidOperationException("SSA value " + key + " is attached to mismatched local descriptor " + descriptor + ".");
        }


        private static void VerifyRefPositions(GenTreeMethod method)
        {
            int previousPosition = -1;
            for (int i = 0; i < method.RefPositions.Length; i++)
            {
                var rp = method.RefPositions[i];
                if (rp.Position < 0)
                    throw new InvalidOperationException($"linear IR ref-position has negative position: {rp}.");
                if (rp.Position < previousPosition)
                    throw new InvalidOperationException("linear IR ref-positions are not sorted by position.");
                previousPosition = rp.Position;

                if (rp.Kind is LinearRefPositionKind.Use or LinearRefPositionKind.Def)
                {
                    var valueKey = rp.Value is null ? default : rp.Value.LinearValueKey;
                    if (rp.Value is null || !method.ValueInfoByNode.ContainsKey(valueKey))
                        throw new InvalidOperationException($"linear IR ref-position references an unknown value: {rp}.");
                    if (rp.RegisterClass == RegisterClass.Invalid)
                        throw new InvalidOperationException($"GenTree value ref-position has invalid register class: {rp}.");
                    if (rp.FixedRegister != MachineRegister.Invalid && !MachineRegisters.IsRegisterInClass(rp.FixedRegister, rp.RegisterClass))
                        throw new InvalidOperationException($"linear IR fixed ref-position register does not match its class: {rp}.");
                    if (rp.IsAbiSegment && rp.AbiSegmentSize <= 0)
                        throw new InvalidOperationException($"linear IR ABI segment ref-position has invalid segment size: {rp}.");
                }
                else if (rp.Kind == LinearRefPositionKind.Kill)
                {
                    if (rp.Value is not null)
                        throw new InvalidOperationException($"linear IR kill ref-position must not carry a value: {rp}.");
                    if (rp.FixedRegister == MachineRegister.Invalid)
                        throw new InvalidOperationException($"linear IR kill ref-position must carry a fixed register: {rp}.");
                    if (!MachineRegisters.IsRegisterInClass(rp.FixedRegister, rp.RegisterClass))
                        throw new InvalidOperationException($"linear IR kill ref-position register does not match its class: {rp}.");
                }
                else if (rp.Kind == LinearRefPositionKind.Internal)
                {
                    if (rp.Value is not null)
                        throw new InvalidOperationException($"linear IR internal ref-position must not carry a value: {rp}.");
                    if (rp.FixedRegister != MachineRegister.Invalid)
                        throw new InvalidOperationException($"linear IR internal ref-position must be allocated from its mask, not pre-fixed: {rp}.");
                    if (rp.RegisterClass == RegisterClass.Invalid)
                        throw new InvalidOperationException($"linear IR internal ref-position has invalid register class: {rp}.");
                    if (rp.RegisterMask == 0)
                        throw new InvalidOperationException($"linear IR internal ref-position has empty register mask: {rp}.");
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported linear IR ref-position kind: {rp.Kind}.");
                }
            }
        }
    }

    internal static class LinearDumper
    {
        public static string Dump(GenTreeProgram program)
        {
            if (program is null)
                throw new ArgumentNullException(nameof(program));

            var sb = new StringBuilder();
            for (int i = 0; i < program.Methods.Length; i++)
            {
                DumpMethod(sb, program.Methods[i]);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static string Dump(GenTreeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            var sb = new StringBuilder();
            DumpMethod(sb, method);
            return sb.ToString();
        }

        public static string FormatNode(GenTree node)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            var sb = new StringBuilder();
            AppendNode(sb, node);
            return sb.ToString();
        }

        private static void DumpMethod(StringBuilder sb, GenTreeMethod method)
        {
            var rm = method.RuntimeMethod;
            sb.Append("linear method ")
              .Append(method.Module.Name)
              .Append("::")
              .Append(TypeName(rm.DeclaringType))
              .Append('.')
              .Append(rm.Name)
              .Append(" #")
              .Append(rm.MethodId)
              .AppendLine();

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var block = method.Blocks[b];
                sb.Append("B").Append(block.Id).Append(" [pc ")
                  .Append(method.Cfg.Blocks[b].StartPc).Append("..").Append(method.Cfg.Blocks[b].EndPcExclusive).AppendLine(")");

                for (int n = 0; n < block.LinearNodes.Length; n++)
                {
                    sb.Append("  ");
                    AppendNode(sb, block.LinearNodes[n]);
                    sb.AppendLine();
                }
            }

            if (method.LiveIntervals.Length != 0)
            {
                sb.AppendLine("intervals:");
                for (int i = 0; i < method.LiveIntervals.Length; i++)
                    AppendInterval(sb, method.LiveIntervals[i]);
            }

            if (method.RefPositions.Length != 0)
            {
                sb.AppendLine("refpositions:");
                for (int i = 0; i < method.RefPositions.Length; i++)
                    sb.Append("  ").Append(method.RefPositions[i]).AppendLine();
            }
        }

        private static void AppendInterval(StringBuilder sb, LinearLiveInterval interval)
        {
            sb.Append("  ").Append(interval.Value).Append(" def@").Append(interval.DefinitionPosition).Append(" ranges=");
            for (int i = 0; i < interval.Ranges.Length; i++)
            {
                if (i != 0)
                    sb.Append(',');
                sb.Append(interval.Ranges[i]);
            }

            if (interval.UsePositions.Length != 0)
            {
                sb.Append(" uses=");
                for (int i = 0; i < interval.UsePositions.Length; i++)
                {
                    if (i != 0)
                        sb.Append(',');
                    sb.Append(interval.UsePositions[i]);
                }
            }
            sb.AppendLine();
        }

        private static void AppendNode(StringBuilder sb, GenTree node)
        {
            sb.Append('#').Append(node.LinearId).Append(' ');

            if (node.LinearKind == GenTreeLinearKind.Copy)
            {
                sb.Append(node.RegisterResult is not null ? node.RegisterResult.ToString() : "<none>")
                  .Append(" <- copy ")
                  .Append(node.RegisterUses.Length == 0 ? "<missing>" : node.RegisterUses[0].ToString());
                if (node.IsPhiCopy)
                    sb.Append(" ; phi-edge B").Append(node.LinearPhiCopyFromBlockId).Append("->B").Append(node.LinearPhiCopyToBlockId);
                return;
            }

            if (node.LinearKind == GenTreeLinearKind.GcPoll)
            {
                sb.Append("gc.poll");
                string gcLowering = node.LinearLowering.ToString();
                if (!string.IsNullOrEmpty(gcLowering))
                    sb.Append(" ; lower[").Append(gcLowering).Append(']');
                return;
            }

            if (node.RegisterResult is not null)
                sb.Append(node.RegisterResult).Append(" = ");

            AppendTreeShape(sb, node);

            string lowering = node.LinearLowering.ToString();
            if (!string.IsNullOrEmpty(lowering))
                sb.Append(" ; lower[").Append(lowering).Append(']');

            string memory = node.LinearMemoryAccess.ToString();
            if (!string.IsNullOrEmpty(memory))
                sb.Append(" ; mem[").Append(memory).Append(']');
        }

        private static void AppendTreeShape(StringBuilder sb, GenTree node)
        {
            GenTree? source = node;
            if (source is null)
            {
                sb.Append(node.Kind);
                return;
            }

            switch (source.Kind)
            {
                case GenTreeKind.ConstI4:
                    sb.Append(source.Int32);
                    return;
                case GenTreeKind.ConstI8:
                    sb.Append(source.Int64).Append('L');
                    return;
                case GenTreeKind.ConstR4Bits:
                    sb.Append(BitConverter.Int32BitsToSingle(source.Int32).ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('f');
                    return;
                case GenTreeKind.ConstR8Bits:
                    sb.Append(BitConverter.Int64BitsToDouble(source.Int64).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    return;
                case GenTreeKind.ConstNull:
                    sb.Append("null");
                    return;
                case GenTreeKind.ConstString:
                    sb.Append('"').Append(Escape(source.Text ?? string.Empty)).Append('"');
                    return;
                case GenTreeKind.Local:
                    sb.Append("ldloc l").Append(source.Int32);
                    return;
                case GenTreeKind.LocalAddr:
                    sb.Append("ldloca l").Append(source.Int32);
                    return;
                case GenTreeKind.Arg:
                    sb.Append("ldarg a").Append(source.Int32);
                    return;
                case GenTreeKind.ArgAddr:
                    sb.Append("ldarga a").Append(source.Int32);
                    return;
                case GenTreeKind.Temp:
                    sb.Append("ldtmp t").Append(source.Int32);
                    return;
                case GenTreeKind.ExceptionObject:
                    sb.Append("exception");
                    return;
                case GenTreeKind.DefaultValue:
                    sb.Append("default(").Append(TypeName(source.RuntimeType)).Append(')');
                    return;
                case GenTreeKind.SizeOf:
                    sb.Append("sizeof(").Append(TypeName(source.RuntimeType)).Append(')');
                    return;
                case GenTreeKind.Unary:
                    sb.Append(source.SourceOp.ToString().ToLowerInvariant()).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.Binary:
                    sb.Append(source.SourceOp).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.Conv:
                    sb.Append("conv.").Append(source.ConvKind);
                    if (source.ConvFlags != NumericConvFlags.None)
                        sb.Append('.').Append(source.ConvFlags);
                    sb.Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.Call:
                case GenTreeKind.VirtualCall:
                case GenTreeKind.DelegateInvoke:
                    sb.Append(source.Kind == GenTreeKind.VirtualCall ? "callvirt " : source.Kind == GenTreeKind.DelegateInvoke ? "delegate_invoke " : "call ")
                      .Append(MethodName(source.Method)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.NewDelegate:
                    sb.Append("new_delegate ").Append(MethodName(source.Method)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.NewObject:
                    sb.Append("newobj ").Append(MethodName(source.Method)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.Field:
                case GenTreeKind.FieldAddr:
                    sb.Append(source.Kind == GenTreeKind.FieldAddr ? "fieldaddr " : "field ")
                      .Append(FieldName(source.Field)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.StaticField:
                    sb.Append("static_field ").Append(FieldName(source.Field));
                    return;
                case GenTreeKind.StaticFieldAddr:
                    sb.Append("static_field_addr ").Append(FieldName(source.Field));
                    return;
                case GenTreeKind.LoadIndirect:
                    sb.Append("ldobj ").Append(TypeName(source.RuntimeType)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.StoreIndirect:
                    sb.Append("stobj ").Append(TypeName(source.RuntimeType)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.StoreLocal:
                    sb.Append("stloc l").Append(source.Int32).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.StoreArg:
                    sb.Append("starg a").Append(source.Int32).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.StoreTemp:
                    sb.Append("sttmp t").Append(source.Int32).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.StoreField:
                    sb.Append("stfld ").Append(FieldName(source.Field)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.StoreStaticField:
                    sb.Append("stsfld ").Append(FieldName(source.Field)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.NewArray:
                    sb.Append("newarr ").Append(TypeName(source.RuntimeType)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.ArrayElement:
                case GenTreeKind.ArrayElementAddr:
                    sb.Append(source.Kind == GenTreeKind.ArrayElementAddr ? "arr_addr " : "arr_elem ");
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.StoreArrayElement:
                    sb.Append("st_elem ");
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.ArrayDataRef:
                    sb.Append("array_data_ref ");
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.StackAlloc:
                    sb.Append("stackalloc elemSize=").Append(source.Int32).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.PointerElementAddr:
                    sb.Append("ptr_elem_addr elemSize=").Append(source.Int32).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.PointerToByRef:
                    sb.Append("ptr_to_byref ");
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.PointerDiff:
                    sb.Append("ptr_diff elemSize=").Append(source.Int32).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.CastClass:
                    sb.Append("castclass ").Append(TypeName(source.RuntimeType)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.IsInst:
                    sb.Append("isinst ").Append(TypeName(source.RuntimeType)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.Box:
                    sb.Append("box ").Append(TypeName(source.RuntimeType)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.UnboxAny:
                    sb.Append("unbox.any ").Append(TypeName(source.RuntimeType)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.Eval:
                    sb.Append("eval ");
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.Branch:
                    sb.Append("br B").Append(source.TargetBlockId);
                    return;
                case GenTreeKind.BranchTrue:
                case GenTreeKind.BranchFalse:
                    sb.Append(source.Kind == GenTreeKind.BranchTrue ? "brtrue " : "brfalse ");
                    AppendUses(sb, node);
                    sb.Append(" -> B").Append(source.TargetBlockId);
                    return;
                case GenTreeKind.Return:
                    sb.Append("ret ");
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.Throw:
                    sb.Append("throw ");
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.Rethrow:
                    sb.Append("rethrow");
                    return;
                case GenTreeKind.EndFinally:
                    sb.Append("endfinally");
                    return;
            }

            sb.Append(source.Kind).Append(' ');
            AppendUses(sb, node);
        }

        private static void AppendUses(StringBuilder sb, GenTree node)
        {
            if (!node.OperandFlags.IsDefaultOrEmpty)
            {
                for (int i = 0; i < node.Operands.Length; i++)
                {
                    if (i != 0)
                        sb.Append(", ");
                    var flags = i < node.OperandFlags.Length ? node.OperandFlags[i] : LirOperandFlags.None;
                    if ((flags & LirOperandFlags.Contained) != 0)
                        sb.Append("contained(").Append(node.Operands[i].Kind).Append(')');
                    else if (node.Operands[i].RegisterResult is not null)
                        sb.Append(node.Operands[i].RegisterResult!);
                    else
                        sb.Append('_');
                    if ((flags & ~LirOperandFlags.Contained) != 0)
                        sb.Append(" [").Append(flags & ~LirOperandFlags.Contained).Append(']');
                }
                return;
            }

            for (int i = 0; i < node.RegisterUses.Length; i++)
            {
                if (i != 0)
                    sb.Append(", ");
                sb.Append(node.RegisterUses[i]);
            }
        }

        private static string TypeName(RuntimeType? type)
        {
            if (type is null)
                return "?";
            return string.IsNullOrEmpty(type.Namespace) ? type.Name : type.Namespace + "." + type.Name;
        }

        private static string FieldName(RuntimeField? field)
        {
            if (field is null)
                return "<field?>";
            return TypeName(field.DeclaringType) + "." + field.Name;
        }

        private static string MethodName(RuntimeMethod? method)
        {
            if (method is null)
                return "<method?>";
            return TypeName(method.DeclaringType) + "." + method.Name;
        }

        private static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }
    }
}
