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

        PhiCopy,

        GcPoll,
    }
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

                case GenTreeKind.TempAddr:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.Temporary, LinearMemoryAccessFlags.Address
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
                GenTreeKind.DelegateCombine or
                GenTreeKind.DelegateRemove or
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
                GenTreeKind.TempAddr => false,
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

        public ulong KillRegisterMask => Kind == LinearRefPositionKind.Kill
            ? RegisterMask
            : 0;

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
}
