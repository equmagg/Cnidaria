using Cnidaria.X86;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Cnidaria.Cs
{

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
        public readonly TargetInfo Target;
        public readonly int DefinitionBlockId;
        public readonly int DefinitionNodeId;

        public GenTreeValueInfo(
            GenTreeValueKey value,
            GenTree representativeNode,
            RuntimeType? type,
            GenStackKind stackKind,
            RegisterClass registerClass,
            TargetInfo target,
            int definitionBlockId,
            int definitionNodeId)
        {
            Value = value;
            RepresentativeNode = representativeNode ?? throw new ArgumentNullException(nameof(representativeNode));
            Origin = value.Origin;
            Type = type;
            StackKind = stackKind;
            RegisterClass = registerClass;
            Target = target ?? throw new ArgumentNullException(nameof(target));
            DefinitionBlockId = definitionBlockId;
            DefinitionNodeId = definitionNodeId;
        }

        public GenTreeValueInfo WithDefinitionNode(GenTree representativeNode, int blockId, int nodeId)
            => new GenTreeValueInfo(Value, representativeNode, Type, StackKind, RegisterClass, Target, blockId, nodeId);

        public AbiValueInfo StorageAbi => MachineAbi.ClassifyStorageValue(Type, StackKind, Target);
        public AbiValueInfo ArgumentAbi => MachineAbi.ClassifyValue(Type, StackKind, isReturn: false, target: Target);
        public AbiValueInfo ReturnAbi => MachineAbi.ClassifyValue(Type, StackKind, isReturn: true, target: Target);
        public bool IsMultiRegisterStorage => StorageAbi.PassingKind == AbiValuePassingKind.MultiRegister;
        public bool RequiresStackHome => MachineAbi.RequiresStackHome(Type, StackKind, Target);
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

        // The node can clobber caller saved registers.
        // Helper expanded nodes such as allocation and type check nodes may still lower to calls
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
        CallerSavedRegistersPreserved = 1 << 9,

        // The lowered node may execute a real ABI call on a rare path
        MayCall = 1 << 10,
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
        NullCheck,
        ArrayLength,
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
        // None comes from default, where the operand indices are zero rather than the -1 the
        // constructor uses for absent, so a node with no memory access would claim operand zero
        public bool HasAddressOperand(int operandIndex)
            => !IsNone && (AddressOperandIndex == operandIndex || IndexOperandIndex == operandIndex);
        public bool HasValueOperand(int operandIndex) => !IsNone && ValueOperandIndex == operandIndex;

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
        public readonly byte InternalVectorRegisters;

        public GenTreeLinearLoweringInfo(GenTreeLinearFlags flags, byte internalGeneralRegisters, byte internalFloatRegisters, byte internalVectorRegisters = 0)
        {
            Flags = flags;
            InternalGeneralRegisters = internalGeneralRegisters;
            InternalFloatRegisters = internalFloatRegisters;
            InternalVectorRegisters = internalVectorRegisters;
        }

        public bool HasFlag(GenTreeLinearFlags flag) => (Flags & flag) != 0;

        public override string ToString()
        {
            if (Flags == GenTreeLinearFlags.None && InternalGeneralRegisters == 0 && InternalFloatRegisters == 0 && InternalVectorRegisters == 0)
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
            if (InternalVectorRegisters != 0)
            {
                if (sb.Length != 0) sb.Append(' ');
                sb.Append("vectorTmp=").Append(InternalVectorRegisters);
            }
            return sb.ToString();
        }
    }
    internal sealed class GenTreeLinearLoweringClassifier
    {
        private readonly TargetInfo _target;
        private readonly bool _nonCallOperationsClobberCallerSavedRegisters;

        public GenTreeLinearLoweringClassifier(TargetInfo target)
            : this(target, !target.IsRegisterBytecode)
        {
        }

        public GenTreeLinearLoweringClassifier(
            TargetInfo target,
            bool nonCallOperationsClobberCallerSavedRegisters)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _nonCallOperationsClobberCallerSavedRegisters = nonCallOperationsClobberCallerSavedRegisters;
        }

        public GenTreeLinearLoweringInfo Classify(GenTree source, GenTree? result, ImmutableArray<GenTree> uses, int blockId, LinearMemoryAccess memoryAccess)
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
            else if (EmitsReturningHelperCall(source))
                flags |= GenTreeLinearFlags.CallerSavedKill;
            else if (MayClobberCallerSaved(source, _target))
                flags |= _nonCallOperationsClobberCallerSavedRegisters && !ThrowsWithoutReturningCall(source)
                    ? GenTreeLinearFlags.CallerSavedKill
                    : GenTreeLinearFlags.CallerSavedRegistersPreserved;

            if (IsGcSafePoint(source, blockId))
                flags |= GenTreeLinearFlags.GcSafePoint;

            byte internalGeneral = InternalGeneralRegisterCount(source, result, memoryAccess);
            byte internalFloat = InternalFloatRegisterCount(source, result);

            return new GenTreeLinearLoweringInfo(flags, internalGeneral, internalFloat,
                _target.IsRiscV && source.Kind == GenTreeKind.HWIntrinsic ? RiscVHWIntrinsicVectorTemps(source) : (byte)0);
        }
        public LinearMemoryAccess ClassifyMemoryAccess(GenTree source) => ClassifyMemoryAccess(source, _target);
        internal static LinearMemoryAccess ClassifyMemoryAccess(GenTree source, TargetInfo target)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            int pointerSize = target.PointerSize;

            LinearMemoryAccessFlags TypeFlags(RuntimeType? type, GenStackKind stackKind)
            {
                LinearMemoryAccessFlags flags = LinearMemoryAccessFlags.None;
                if (type is not null)
                {
                    if (type.ContainsGcPointers || type.IsReferenceType || type.Kind is RuntimeTypeKind.ByRef or RuntimeTypeKind.TypeParam)
                        flags |= LinearMemoryAccessFlags.ContainsGcPointers;

                    if (MachineAbi.IsBlockCopyValue(type, stackKind, target))
                        flags |= LinearMemoryAccessFlags.BlockCopy;
                }
                if (stackKind is GenStackKind.Ref or GenStackKind.Null or GenStackKind.ByRef)
                    flags |= LinearMemoryAccessFlags.ContainsGcPointers;
                if (stackKind == GenStackKind.Value)
                    flags |= LinearMemoryAccessFlags.BlockCopy;
                return flags;
            }

            LinearMemoryAccessFlags StoreTypeFlags(RuntimeType? type, GenStackKind stackKind)
            {
                var flags = TypeFlags(type, stackKind);
                if ((flags & LinearMemoryAccessFlags.ContainsGcPointers) != 0)
                    flags |= LinearMemoryAccessFlags.RequiresWriteBarrier;
                return flags;
            }

            int SizeOf(RuntimeType? type, GenStackKind stackKind)
            {
                if (type is not null)
                    return Math.Max(1, type.SizeOf);

                return stackKind switch
                {
                    GenStackKind.I4 => 4,
                    GenStackKind.I8 => 8,
                    GenStackKind.R4 => 4,
                    GenStackKind.R8 => 8,
                    GenStackKind.NativeInt => pointerSize,
                    GenStackKind.NativeUInt => pointerSize,
                    GenStackKind.Ref => pointerSize,
                    GenStackKind.Ptr => pointerSize,
                    GenStackKind.ByRef => pointerSize,
                    GenStackKind.Null => pointerSize,
                    GenStackKind.Simd8 => 8,
                    GenStackKind.Simd12 => 12,
                    GenStackKind.Simd16 => 16,
                    GenStackKind.Simd32 => 32,
                    GenStackKind.Simd64 => 64,
                    GenStackKind.Void => 0,
                    _ => pointerSize,
                };
            }

            int AlignOf(RuntimeType? type, GenStackKind stackKind)
            {
                if (type is not null)
                    return Math.Max(1, type.AlignOf);

                return stackKind switch
                {
                    GenStackKind.I4 => 4,
                    GenStackKind.I8 => 8,
                    GenStackKind.R4 => 4,
                    GenStackKind.R8 => 8,
                    GenStackKind.NativeInt => pointerSize,
                    GenStackKind.NativeUInt => pointerSize,
                    GenStackKind.Ref => pointerSize,
                    GenStackKind.Ptr => pointerSize,
                    GenStackKind.ByRef => pointerSize,
                    GenStackKind.Null => pointerSize,
                    GenStackKind.Simd8 => 8,
                    GenStackKind.Simd12 => 16,
                    GenStackKind.Simd16 => 16,
                    GenStackKind.Simd32 => 32,
                    GenStackKind.Simd64 => 64,
                    GenStackKind.Void => 1,
                    _ => pointerSize,
                };
            }

            static RuntimeType? OperandType(GenTree source, int index)
                => (uint)index < (uint)source.Operands.Length ? source.Operands[index].Type : source.RuntimeType ?? source.Type;

            static GenStackKind OperandStackKind(GenTree source, int index)
                => (uint)index < (uint)source.Operands.Length ? source.Operands[index].StackKind : source.StackKind;

            static RuntimeType? LocalLikeStorageType(GenTree source, int operandIndex)
                => source.LocalDescriptor?.Type ?? OperandType(source, operandIndex) ?? source.RuntimeType ?? source.Type;

            GenStackKind LocalLikeStorageKind(GenTree source, RuntimeType? storageType, int operandIndex)
                => source.LocalDescriptor is not null
                    ? source.LocalDescriptor.StackKind
                    : storageType is not null
                        ? MachineAbi.StackKindForType(storageType, target)
                        : OperandStackKind(source, operandIndex);

            static RuntimeType? StoreTargetType(GenTree source, int operandIndex)
                => source.Field?.FieldType ?? source.RuntimeType ?? source.Type ?? OperandType(source, operandIndex);

            GenStackKind StoreTargetKind(GenTree source, RuntimeType? storageType, int operandIndex)
                => storageType is not null ? MachineAbi.StackKindForType(storageType, target) : OperandStackKind(source, operandIndex);

            static LinearMemoryAccessFlags NullCheckFlag(GenTree source)
                => (source.Flags & GenTreeFlags.NullCheckEliminated) == 0
                    ? LinearMemoryAccessFlags.NullCheck
                    : LinearMemoryAccessFlags.None;

            static LinearMemoryAccessFlags BoundsCheckFlag(GenTree source)
                => (source.Flags & GenTreeFlags.BoundsCheckEliminated) == 0
                    ? LinearMemoryAccessFlags.BoundsCheck
                    : LinearMemoryAccessFlags.None;

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
                        | LinearMemoryAccessFlags.ByRefAddress, size: pointerSize, alignment: pointerSize);

                case GenTreeKind.ArgAddr:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.Argument, LinearMemoryAccessFlags.Address
                        | LinearMemoryAccessFlags.ByRefAddress, size: pointerSize, alignment: pointerSize);

                case GenTreeKind.TempAddr:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.Temporary, LinearMemoryAccessFlags.Address
                        | LinearMemoryAccessFlags.ByRefAddress, size: pointerSize, alignment: pointerSize);

                case GenTreeKind.Field:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.Field, LinearMemoryAccessFlags.Read
                        | NullCheckFlag(source) | TypeFlags(source.Type, source.StackKind),
                        addressOperandIndex: 0, field: source.Field, size: SizeOf(source.Type,
                        source.StackKind), alignment: AlignOf(source.Type, source.StackKind));

                case GenTreeKind.FieldAddr:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.Field, LinearMemoryAccessFlags.Address
                        | LinearMemoryAccessFlags.ByRefAddress | NullCheckFlag(source), addressOperandIndex: 0,
                        field: source.Field, size: pointerSize, alignment: pointerSize);

                case GenTreeKind.StaticField:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.StaticField, LinearMemoryAccessFlags.Read
                        | TypeFlags(source.Type, source.StackKind), field: source.Field,
                        size: SizeOf(source.Type, source.StackKind), alignment: AlignOf(source.Type, source.StackKind));

                case GenTreeKind.StaticFieldAddr:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.StaticField, LinearMemoryAccessFlags.Address
                        | LinearMemoryAccessFlags.ByRefAddress, field: source.Field,
                        size: pointerSize, alignment: pointerSize);

                case GenTreeKind.StoreField:
                    {
                        RuntimeType? valueType = StoreTargetType(source, 1);
                        GenStackKind valueKind = StoreTargetKind(source, valueType, 1);
                        return new LinearMemoryAccess(LinearMemoryAccessKind.Field, LinearMemoryAccessFlags.Write
                            | NullCheckFlag(source) | StoreTypeFlags(valueType, valueKind),
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
                        | NullCheckFlag(source) | TypeFlags(source.RuntimeType ?? source.Type, source.StackKind), addressOperandIndex: 0,
                        elementType: source.RuntimeType ?? source.Type,
                        size: SizeOf(source.RuntimeType ?? source.Type, source.StackKind), alignment: AlignOf(source.RuntimeType ?? source.Type, source.StackKind));

                case GenTreeKind.StoreIndirect:
                    {
                        RuntimeType? valueType = StoreTargetType(source, 1);
                        GenStackKind valueKind = StoreTargetKind(source, valueType, 1);
                        return new LinearMemoryAccess(LinearMemoryAccessKind.Indirect, LinearMemoryAccessFlags.Write
                            | NullCheckFlag(source) | StoreTypeFlags(valueType, valueKind), addressOperandIndex: 0, valueOperandIndex: 1,
                            elementType: valueType, size: SizeOf(valueType, valueKind), alignment: AlignOf(valueType, valueKind));
                    }

                case GenTreeKind.NullCheck:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.NullCheck, LinearMemoryAccessFlags.NullCheck,
                        addressOperandIndex: 0, size: 1, alignment: 1);

                case GenTreeKind.ArrayLength:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.ArrayLength, LinearMemoryAccessFlags.Read
                        | NullCheckFlag(source), addressOperandIndex: 0, size: 4, alignment: 4);

                case GenTreeKind.ArrayElement:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.ArrayElement, LinearMemoryAccessFlags.Read
                        | NullCheckFlag(source) | BoundsCheckFlag(source)
                        | TypeFlags(source.RuntimeType ?? source.Type, source.StackKind), addressOperandIndex: 0,
                        indexOperandIndex: 1, elementType: source.RuntimeType ?? source.Type,
                        size: SizeOf(source.RuntimeType ?? source.Type, source.StackKind), alignment: AlignOf(source.RuntimeType ?? source.Type, source.StackKind));

                case GenTreeKind.ArrayElementAddr:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.ArrayElement, LinearMemoryAccessFlags.Address
                        | LinearMemoryAccessFlags.ByRefAddress | NullCheckFlag(source) | BoundsCheckFlag(source),
                        addressOperandIndex: 0, indexOperandIndex: 1, elementType: source.RuntimeType,
                        size: pointerSize, alignment: pointerSize);

                case GenTreeKind.StoreArrayElement:
                    {
                        RuntimeType? valueType = StoreTargetType(source, 2);
                        GenStackKind valueKind = StoreTargetKind(source, valueType, 2);
                        return new LinearMemoryAccess(LinearMemoryAccessKind.ArrayElement, LinearMemoryAccessFlags.Write
                            | NullCheckFlag(source) | BoundsCheckFlag(source) | StoreTypeFlags(valueType, valueKind),
                            addressOperandIndex: 0, indexOperandIndex: 1, valueOperandIndex: 2, elementType: valueType,
                            size: SizeOf(valueType, valueKind), alignment: AlignOf(valueType, valueKind));
                    }

                case GenTreeKind.ArrayDataRef:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.ArrayData, LinearMemoryAccessFlags.Address
                        | LinearMemoryAccessFlags.ByRefAddress | NullCheckFlag(source),
                        addressOperandIndex: 0, size: pointerSize, alignment: pointerSize);

                case GenTreeKind.PointerElementAddr:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.PointerElement, LinearMemoryAccessFlags.Address
                        | LinearMemoryAccessFlags.ByRefAddress, addressOperandIndex: 0, indexOperandIndex: 1,
                        elementType: source.RuntimeType, size: pointerSize, alignment: pointerSize);

                case GenTreeKind.PointerDiff:
                    return new LinearMemoryAccess(LinearMemoryAccessKind.PointerDiff, LinearMemoryAccessFlags.Address,
                        addressOperandIndex: 0, indexOperandIndex: 1, elementType: source.RuntimeType,
                        size: pointerSize, alignment: pointerSize);

                default:
                    return LinearMemoryAccess.None;
            }
        }

        public static bool IsAbiCall(GenTree source)
        {
            if (source.Kind == GenTreeKind.Intrinsic)
                return (RuntimeIntrinsics.GetFlags(source.IntrinsicId) & RuntimeIntrinsicFlags.UsesCallAbi) != 0;

            return source.Kind is
                GenTreeKind.ClassInit or
                GenTreeKind.Call or
                GenTreeKind.IndirectCall or
                GenTreeKind.VirtualCall or
                GenTreeKind.DelegateInvoke or
                GenTreeKind.NewObject;
        }

        // Helper sequences that return to their caller clobber the caller-saved set even on targets
        // where the remaining non-call operations are register preserving.
        private bool EmitsReturningHelperCall(GenTree source)
        {
            if (IsCallFinally(source))
                return true;
            if (_target.IsRegisterBytecode)
                return false;

            return source.Kind is
                GenTreeKind.NewArray or
                GenTreeKind.Box or
                GenTreeKind.NewDelegate or
                GenTreeKind.DelegateCombine or
                GenTreeKind.DelegateRemove;
        }

        // These expand into reserved scratch and allocated internals, and the only call they make throws
        private static bool ThrowsWithoutReturningCall(GenTree source)
            => source.Kind switch
            {
                GenTreeKind.Field or GenTreeKind.FieldAddr or GenTreeKind.StoreField or
                GenTreeKind.StaticField or GenTreeKind.StaticFieldAddr or GenTreeKind.StoreStaticField or
                GenTreeKind.LoadIndirect or GenTreeKind.StoreIndirect or GenTreeKind.NullCheck or
                GenTreeKind.ArrayLength or GenTreeKind.ArrayElement or GenTreeKind.ArrayElementAddr or
                GenTreeKind.StoreArrayElement or GenTreeKind.ArrayDataRef or GenTreeKind.BoundsCheck or GenTreeKind.Conv => true,
                GenTreeKind.Binary => source.StackKind is not (GenStackKind.R4 or GenStackKind.R8),
                _ => false,
            };

        public static bool MayClobberCallerSaved(GenTree source, TargetInfo target)
        {
            if (IsAbiCall(source))
                return true;

            if (source.Kind is
                GenTreeKind.NewArray or
                GenTreeKind.NewDelegate or
                GenTreeKind.DelegateCombine or
                GenTreeKind.DelegateRemove or
                GenTreeKind.Box or
                GenTreeKind.CastClass or
                GenTreeKind.UnboxAny or
                GenTreeKind.Throw or
                GenTreeKind.Rethrow)
                return true;

            if (IsCallFinally(source))
                return true;

            if (source.Kind == GenTreeKind.Binary)
            {
                // No machine target has a floating remainder instruction, so it becomes a call
                if (!target.IsRegisterBytecode &&
                    source.Operator == GenTreeOperator.Rem &&
                    source.StackKind is GenStackKind.R4 or GenStackKind.R8)
                {
                    return true;
                }

                if (source.Operator is
                    GenTreeOperator.AddOvf or GenTreeOperator.AddOvfUn or
                    GenTreeOperator.SubOvf or GenTreeOperator.SubOvfUn or
                    GenTreeOperator.MulOvf or GenTreeOperator.MulOvfUn)
                {
                    return true;
                }

                if (source.Operator is GenTreeOperator.Div or GenTreeOperator.DivUn or GenTreeOperator.Rem or GenTreeOperator.RemUn)
                    return GenTreeArithmeticSemantics.DivRemCanThrow(source, target);
            }

            if (source.Kind == GenTreeKind.Conv && (source.ConvFlags & NumericConvFlags.Checked) != 0)
                return true;

            if (source.Kind is
                GenTreeKind.Field or GenTreeKind.FieldAddr or GenTreeKind.StoreField or
                GenTreeKind.StaticField or GenTreeKind.StaticFieldAddr or GenTreeKind.StoreStaticField or
                GenTreeKind.LoadIndirect or GenTreeKind.StoreIndirect or GenTreeKind.NullCheck or
                GenTreeKind.ArrayLength or GenTreeKind.ArrayElement or GenTreeKind.ArrayElementAddr or
                GenTreeKind.StoreArrayElement or GenTreeKind.ArrayDataRef or GenTreeKind.BoundsCheck)
            {
                return true;
            }

            return false;
        }

        private static bool IsCallFinally(GenTree source)
            => source.Kind == GenTreeKind.CallFinally;

        public bool IsGcSafePoint(GenTree source, int blockId = -1)
        {
            if (IsCallFinally(source))
                return true;
            if (IsAbiCall(source))
            {
                if (source.Kind == GenTreeKind.Intrinsic && RuntimeIntrinsics.IsNoGcSafePoint(source.IntrinsicId))
                    return false;
                return true;
            }

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

        private bool RequiresRegisterOperands(GenTree source, LinearMemoryAccess memoryAccess)
        {
            if (source.StackKind == GenStackKind.Value)
                return false;

            if (!memoryAccess.IsNone)
                return false;

            if (source.Kind == GenTreeKind.Box && IsStackHomeBoxSource(source))
                return false;

            return source.Kind switch
            {
                GenTreeKind.Nop => false,
                GenTreeKind.ClassInit => false,
                GenTreeKind.ConstI4 => false,
                GenTreeKind.ConstI8 => false,
                GenTreeKind.ConstR4Bits => false,
                GenTreeKind.ConstR8Bits => false,
                GenTreeKind.ConstNull => false,
                GenTreeKind.ConstString => false,
                GenTreeKind.TypeHandle => false,
                GenTreeKind.DefaultValue => false,
                GenTreeKind.SizeOf => false,
                GenTreeKind.Local => false,
                GenTreeKind.Arg => false,
                GenTreeKind.Temp => false,
                GenTreeKind.TempAddr => false,
                GenTreeKind.StaticField => false,
                GenTreeKind.StaticFieldAddr => false,
                GenTreeKind.Branch => false,
                GenTreeKind.Return => false,
                GenTreeKind.Rethrow => false,
                GenTreeKind.EndFinally => false,
                GenTreeKind.CallFinally => false,
                GenTreeKind.CatchReturn => false,
                _ => true,
            };
        }

        private byte InternalGeneralRegisterCount(GenTree source, GenTree? result, LinearMemoryAccess memoryAccess)
        {
            int count = source.Kind == GenTreeKind.Box && IsStackHomeBoxSource(source) ? 1 : source.Kind switch
            {
                GenTreeKind.ArrayElement => ArrayElementLoadGeneralScratchCount(result),
                GenTreeKind.ArrayElementAddr => _target.IsRiscV ? 2 : 1,
                GenTreeKind.StoreArrayElement => StoreArrayElementGeneralScratchCount(source),
                GenTreeKind.PointerElementAddr => 1,
                GenTreeKind.PointerDiff => 1,
                GenTreeKind.UnboxAny => MultiRegisterLoadGeneralScratchCount(result, needsAddressScratch: true),
                GenTreeKind.Field => MultiRegisterLoadGeneralScratchCount(result, needsAddressScratch: true),
                GenTreeKind.StaticField => MultiRegisterLoadGeneralScratchCount(result, needsAddressScratch: true) + BlockCopyScratchCount(result),
                GenTreeKind.LoadIndirect => MultiRegisterLoadGeneralScratchCount(result, needsAddressScratch: true),
                GenTreeKind.StoreIndirect => MultiRegisterStoreGeneralScratchCount(MultiRegisterOperandValue(source, 1), needsAddressScratch: true),
                GenTreeKind.StoreField => MultiRegisterStoreGeneralScratchCount(MultiRegisterOperandValue(source, 1), needsAddressScratch: true),
                GenTreeKind.StoreStaticField => MultiRegisterStoreGeneralScratchCount(MultiRegisterOperandValue(source, 0), needsAddressScratch: true) +
                    BlockCopyScratchCount(source.Operands.Length == 0 ? null : source.Operands[0].RegisterResult ?? source.Operands[0]),

                GenTreeKind.Box => MultiRegisterStoreGeneralScratchCount(MultiRegisterOperandValue(source, 0), needsAddressScratch: true),

                _ => 0,
            };

            if ((_target.IsX86 || _target.IsArm) && IsIntegerDivRem(source, result, _target))
                count++;

            if ((_target.IsX86 || _target.IsArm) && (IsCheckedIntegerBinary(source) || IsCheckedIntegerConversion(source)))
                count++;

            // ARM64 keeps two scratch registers out of allocation, and a block-copied value needs both
            // of them, so the address it is copied through has to be allocated
            if (_target.IsArm && count == 0 && RequiresArmBlockCopyAddress(source, result))
                count = 1;

            // The ARM64 type check walks a hierarchy with one pointer more live than those two
            if (_target.IsArm && RequiresArmTypeCheckRegister(source))
                count++;

            // The field and indirection expansions can need an address register beyond what the value
            // itself takes. Asking for it here is what lets them stay out of UsesX86CodegenScratch, so
            // the allocator can keep using the scratch registers across them
            if (_target.IsX86 && IsX86AddressScratchNode(source))
                count++;

            if ((memoryAccess.Flags & LinearMemoryAccessFlags.RequiresWriteBarrier) != 0)
                count++;

            if (_target.IsX86 && source.Kind == GenTreeKind.HWIntrinsic && X86Intrinsics.IsX86(source))
                count += X86IntrinsicGeneralTemps(source);

            if (count > byte.MaxValue)
                throw new InvalidOperationException($"Node {source.Id} requires too many internal general registers.");

            return (byte)count;
        }

        // Node kinds whose x86 expansion needs registers of its own beyond the operands and the result.
        // The allocator keeps the code generator scratch pair free across them and is free to use it
        // anywhere else, which is why the pair is not reserved for the whole method
        private static bool IsX86AddressScratchNode(GenTree source)
            => source.Kind is GenTreeKind.Field or GenTreeKind.FieldAddr or
                GenTreeKind.StaticField or GenTreeKind.StaticFieldAddr or GenTreeKind.StoreStaticField or
                GenTreeKind.StoreField or GenTreeKind.LoadIndirect or GenTreeKind.StoreIndirect;

        internal static bool UsesX86CodegenScratch(GenTree source, TargetInfo target)
        {
            if (!target.IsX86)
                return false;

            switch (source.Kind)
            {
                case GenTreeKind.VirtualCall:
                case GenTreeKind.ClassInit:
                case GenTreeKind.CastClass:
                case GenTreeKind.IsInst:
                case GenTreeKind.UnboxAny:
                case GenTreeKind.NewDelegate:
                case GenTreeKind.DelegateInvoke:
                case GenTreeKind.DelegateCombine:
                case GenTreeKind.DelegateRemove:
                case GenTreeKind.ArrayElement:
                case GenTreeKind.ArrayElementAddr:
                case GenTreeKind.StoreArrayElement:
                case GenTreeKind.ArrayDataRef:
                case GenTreeKind.Box:
                case GenTreeKind.Return:
                    return true;
                case GenTreeKind.Copy:
                case GenTreeKind.Reload:
                case GenTreeKind.Spill:
                    return false;
                default:
                    return false;
            }
        }

        internal static byte RiscVHWIntrinsicVectorTemps(GenTree node)
            => HWIntrinsics.Id(node) switch
            {
                HWIntrinsicId.CreateElements when !HWIntrinsics.HasConstantElements(node) => 2,
                HWIntrinsicId.WithElement or HWIntrinsicId.Combine or HWIntrinsicId.ZeroExtend or
                HWIntrinsicId.WithLower or HWIntrinsicId.WithUpper or HWIntrinsicId.Sum => 2,
                HWIntrinsicId.GetElement or HWIntrinsicId.GetUpper or HWIntrinsicId.AndNot or HWIntrinsicId.ConditionalSelect => 1,
                HWIntrinsicId.Abs when !HWIntrinsics.IsFloating(HWIntrinsics.BaseKind(node)) => 1,
                _ => 0,
            };

        internal static int X86HWIntrinsicFloatTemps(GenTree node, TargetInfo target)
        {
            if (X86Intrinsics.IsX86(node))
                return X86IntrinsicFloatTemps(node);

            RuntimePrimitiveKind kind = HWIntrinsics.BaseKind(node);
            int elementSize = HWIntrinsics.ElementSize(kind);
            bool floating = HWIntrinsics.IsFloating(kind);
            bool unsigned = HWIntrinsics.IsUnsigned(kind);
            bool ssse3 = HWIntrinsics.HasFeature(target, TargetArchitectureFeatures.X86Ssse3);
            bool sse41 = HWIntrinsics.HasFeature(target, TargetArchitectureFeatures.X86Sse41);
            int size = HWIntrinsics.SimdSize(node);
            bool maskCompare = size == 64;
            switch (HWIntrinsics.Id(node))
            {
                case HWIntrinsicId.CreateElements:
                    if (HWIntrinsics.HasConstantElements(node))
                        return 0;
                    if (floating)
                        return kind == RuntimePrimitiveKind.Single && node.Operands.Length >= 3 ? 2 : 1;
                    if (elementSize == 2 || sse41)
                        return 0;
                    return elementSize == 8 ? 2 : 3;
                case HWIntrinsicId.CreateScalar:
                    return kind == RuntimePrimitiveKind.Single && !sse41 ? 1 : 0;
                case HWIntrinsicId.GetElement:
                    if (node.Operands[1].Int32 >= 16 / elementSize)
                        return 1;
                    return !floating && elementSize >= 4 && !sse41 && node.Operands[1].Int32 != 0 ? 1 : 0;
                case HWIntrinsicId.WithElement:
                    if (size > 16)
                        return 1;
                    return !floating && elementSize == 8 && !sse41 ? 2 : 0;
                case HWIntrinsicId.Multiply:
                    return !floating && elementSize == 4 && !sse41 ? 3 : 0;
                case HWIntrinsicId.Abs:
                    if (floating || ssse3 && elementSize <= 4 || maskCompare)
                        return 0;
                    return elementSize <= 2 ? 1 : 2;
                case HWIntrinsicId.Min:
                case HWIntrinsicId.Max:
                    if (maskCompare || kind is RuntimePrimitiveKind.UInt8 or RuntimePrimitiveKind.Int16 ||
                        sse41 && kind is RuntimePrimitiveKind.Int8 or RuntimePrimitiveKind.UInt16 or RuntimePrimitiveKind.Int32 or RuntimePrimitiveKind.UInt32)
                    {
                        return 0;
                    }
                    return kind == RuntimePrimitiveKind.UInt16 || !unsigned ? 2 : 3;
                case HWIntrinsicId.ConditionalSelect:
                    return 1;
                case HWIntrinsicId.EqualsAll:
                    return maskCompare ? 0 : 1;
                case HWIntrinsicId.CompareEqual:
                    return !maskCompare && !floating && elementSize == 8 && !sse41 ? 2 : 0;
                case HWIntrinsicId.EqualsAny:
                    if (maskCompare)
                        return 0;
                    return !floating && elementSize == 8 && !sse41 ? 2 : 1;
                case HWIntrinsicId.CompareGreaterThan:
                case HWIntrinsicId.CompareGreaterThanOrEqual:
                case HWIntrinsicId.CompareLessThan:
                case HWIntrinsicId.CompareLessThanOrEqual:
                    return !maskCompare && !floating && unsigned ? 2 : 0;
                case HWIntrinsicId.ShiftLeft:
                case HWIntrinsicId.ShiftRightArithmetic:
                case HWIntrinsicId.ShiftRightLogical:
                    return node.Operands[1].Kind == GenTreeKind.ConstI4 ? 0 : 2;
                case HWIntrinsicId.ExtractMostSignificantBits:
                    return !maskCompare && elementSize == 2 ? 1 : 0;
                case HWIntrinsicId.Sum:
                    {
                        if (elementSize == 1 || size > 16)
                            return 2;
                        int lanes = size / elementSize;
                        return lanes >= 3 ? 2 : lanes == 2 ? 1 : 0;
                    }
                default:
                    return 0;
            }
        }

        // A temp that must differ from the result is requested twice, as the allocator may hand one out as the result
        private static int X86IntrinsicFloatTemps(GenTree node)
        {
            switch (X86Intrinsics.Info(node).Form)
            {
                case X86IntrinsicForm.Gather:
                case X86IntrinsicForm.GatherMask:
                    return 2;
                case X86IntrinsicForm.Compare:
                    return X86Intrinsics.IsCompareSwap(node, out bool scalar) && scalar ? 2 : 0;
                case X86IntrinsicForm.Shift:
                    return X86Intrinsics.ImmediateOperandIndex(node) == 1 && node.Operands[1].Kind != GenTreeKind.ConstI4 &&
                        X86ShiftHasRegisterCount(X86Intrinsics.Info(node).Instruction(HWIntrinsics.BaseKind(node))) ? 2 : 0;
                default:
                    return 0;
            }
        }

        private static int X86IntrinsicGeneralTemps(GenTree node)
        {
            var info = X86Intrinsics.Info(node);
            if (info.Form == X86IntrinsicForm.CompareFlags)
                return X86Intrinsics.CompareFlagsNeedParity(node) ? 2 : 0;
            if (info.Form == X86IntrinsicForm.DivRem)
                return HWIntrinsics.BaseKind(node) == RuntimePrimitiveKind.Int64 ? 3 : 1;
            if (info.Form == X86IntrinsicForm.BitFieldExtract)
                return node.Operands.Length == 3 ? 1 : 0;
            if (info.Form == X86IntrinsicForm.MultiplyNoFlags)
                return node.Operands.Length == 3 ? 2 : 0;
            int rounding = X86Intrinsics.RoundingOperandIndex(node);
            if (rounding >= 0)
                return node.Operands[rounding].Kind == GenTreeKind.ConstI4 ? 0 : 2;
            int immediate = X86Intrinsics.ImmediateOperandIndex(node);
            if (immediate < 0 || node.Operands[immediate].Kind == GenTreeKind.ConstI4)
                return 0;
            if (X86Intrinsics.IsVex(info))
                return 2;
            return info.Form == X86IntrinsicForm.Shift && X86ShiftHasRegisterCount(info.Instruction(HWIntrinsics.BaseKind(node))) ? 0 : 2;
        }

        internal static bool X86ShiftHasRegisterCount(X86InstrKind instruction)
            => instruction is not (X86InstrKind.Pslldq or X86InstrKind.Psrldq);

        private static bool X86IntrinsicDelaysFree(GenTree node, int operandIndex)
        {
            var info = X86Intrinsics.Info(node);
            switch (info.Form)
            {
                case X86IntrinsicForm.Simple:
                    {
                        int values = node.Operands.Length - (X86Intrinsics.ImmediateOperandIndex(node) >= 0 ? 1 : 0);
                        return values == 2 && operandIndex == 1 && !X86Intrinsics.IsCommutative(node);
                    }
                case X86IntrinsicForm.Scalar:
                case X86IntrinsicForm.BlendVariable:
                case X86IntrinsicForm.Crc32:
                    return operandIndex == 1;
                case X86IntrinsicForm.Shift:
                    return operandIndex == 1 && SimdTypes.IsSimdKind(node.Operands[1].StackKind);
                case X86IntrinsicForm.Compare:
                    if (X86Intrinsics.IsCompareSwap(node, out bool scalar))
                        return !scalar && operandIndex == 0;
                    return operandIndex == 1 && !X86Intrinsics.IsCommutative(node);
                case X86IntrinsicForm.Tied:
                case X86IntrinsicForm.GatherMask:
                    return operandIndex != 0;
                case X86IntrinsicForm.Gather:
                    return operandIndex == 1;
                case X86IntrinsicForm.MultiplyNoFlags:
                case X86IntrinsicForm.MaskedMerge:
                    return operandIndex == 2;
                default:
                    return false;
            }
        }

        // Operands still read after the result register has been written
        internal static bool X86HWIntrinsicDelaysFree(GenTree node, int operandIndex)
        {
            if (X86Intrinsics.IsX86(node))
                return X86IntrinsicDelaysFree(node, operandIndex);

            RuntimePrimitiveKind kind = HWIntrinsics.BaseKind(node);
            bool floating = HWIntrinsics.IsFloating(kind);
            bool signedInteger = !floating && !HWIntrinsics.IsUnsigned(kind);
            return HWIntrinsics.Id(node) switch
            {
                HWIntrinsicId.Subtract or HWIntrinsicId.Divide or HWIntrinsicId.SubtractSaturate or
                HWIntrinsicId.Combine or HWIntrinsicId.WithLower or HWIntrinsicId.WithUpper => operandIndex == 1,
                HWIntrinsicId.AndNot => operandIndex == 0,
                HWIntrinsicId.Negate => !floating && operandIndex == 0,
                HWIntrinsicId.WithElement => floating && operandIndex == 2,
                HWIntrinsicId.CompareGreaterThan => floating ? operandIndex == 0 : signedInteger && operandIndex == 1,
                HWIntrinsicId.CompareGreaterThanOrEqual => (floating || signedInteger) && operandIndex == 0,
                HWIntrinsicId.CompareLessThan => floating ? operandIndex == 1 : signedInteger && operandIndex == 0,
                HWIntrinsicId.CompareLessThanOrEqual => (floating || signedInteger) && operandIndex == 1,
                _ => false,
            };
        }

        internal static bool IsCheckedIntegerBinary(GenTree source)
            => source.Kind == GenTreeKind.Binary &&
               source.Operator is GenTreeOperator.AddOvf or GenTreeOperator.AddOvfUn or
                   GenTreeOperator.SubOvf or GenTreeOperator.SubOvfUn or
                   GenTreeOperator.MulOvf or GenTreeOperator.MulOvfUn;

        internal static bool IsCheckedIntegerConversion(GenTree source)
            => source.Kind == GenTreeKind.Conv &&
               (source.ConvFlags & NumericConvFlags.Checked) != 0 &&
               source.ConvKind is not (NumericConvKind.R4 or NumericConvKind.R8 or NumericConvKind.Bool);

        private static bool IsIntegerDivRem(GenTree source, GenTree? result, TargetInfo target)
        {
            if (source.Kind != GenTreeKind.Binary ||
                source.Operator is not (GenTreeOperator.Div or GenTreeOperator.DivUn or GenTreeOperator.Rem or GenTreeOperator.RemUn))
            {
                return false;
            }

            var stackKind = result?.StackKind ?? source.StackKind;
            if (target.Architecture == TargetArchitectureKind.I386 && stackKind == GenStackKind.I8)
                return false;
            return stackKind is not (GenStackKind.R4 or GenStackKind.R8);
        }

        private byte InternalFloatRegisterCount(GenTree source, GenTree? result)
        {
            int count = source.Kind switch
            {
                GenTreeKind.HWIntrinsic when _target.IsX86 => X86HWIntrinsicFloatTemps(source, _target),
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

        private bool IsStackHomeBoxSource(GenTree node)
        {
            if (node.Operands.IsDefaultOrEmpty)
                return false;

            var operand = node.Operands[0].RegisterResult ?? node.Operands[0];
            return MachineAbi.IsBlockCopyValue(operand.RuntimeType ?? operand.Type, operand.StackKind, _target);
        }

        private static bool RequiresArmTypeCheckRegister(GenTree source)
            => source.Kind is GenTreeKind.CastClass or GenTreeKind.IsInst ||
               (source.Kind == GenTreeKind.UnboxAny && (source.RuntimeType ?? source.Type)?.IsValueType != true);

        private bool RequiresArmBlockCopyAddress(GenTree source, GenTree? result)
        {
            GenTree? value = source.Kind switch
            {
                GenTreeKind.Field or GenTreeKind.ArrayElement or GenTreeKind.LoadIndirect or
                GenTreeKind.StaticField or GenTreeKind.UnboxAny => result,
                GenTreeKind.StoreField or GenTreeKind.StoreIndirect => OperandValue(source, 1),
                GenTreeKind.StoreStaticField or GenTreeKind.Box => OperandValue(source, 0),
                GenTreeKind.StoreArrayElement => StoreArrayElementOperandValue(source),
                _ => null,
            };

            if (value is null)
                return false;

            AbiValueInfo abi = MachineAbi.ClassifyStorageValue(value.Type, value.StackKind, _target);
            return abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect;
        }

        private static GenTree? OperandValue(GenTree node, int operandIndex)
        {
            if ((uint)operandIndex >= (uint)node.Operands.Length)
                return null;

            var operand = node.Operands[operandIndex];
            return operand.RegisterResult ?? operand;
        }

        private GenTree? MultiRegisterOperandValue(GenTree node, int operandIndex)
        {
            if ((uint)operandIndex >= (uint)node.Operands.Length)
                return null;

            var operand = node.Operands[operandIndex];
            var value = operand.RegisterResult ?? operand;
            return IsMultiRegisterValue(value) ? value : null;
        }

        // A value homed on the stack is copied through a general register, besides the static field's address
        private int BlockCopyScratchCount(GenTree? value)
        {
            if (value is null)
                return 0;
            var abi = MachineAbi.ClassifyStorageValue(value.Type, value.StackKind, _target);
            return abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect ? 1 : 0;
        }

        private bool IsMultiRegisterValue(GenTree? value)
        {
            if (value is null)
                return false;

            var abi = MachineAbi.ClassifyStorageValue(value.Type, value.StackKind, _target);
            return abi.PassingKind == AbiValuePassingKind.MultiRegister;
        }

        private bool MultiRegisterValueHasRegisterClass(GenTree? value, RegisterClass registerClass)
        {
            if (value is null)
                return false;

            var abi = MachineAbi.ClassifyStorageValue(value.Type, value.StackKind, _target);
            if (abi.PassingKind != AbiValuePassingKind.MultiRegister)
                return false;

            var segments = MachineAbi.GetRegisterSegments(abi, _target);
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i].RegisterClass == registerClass)
                    return true;
            }

            return false;
        }


        private int ArrayElementLoadGeneralScratchCount(GenTree? result)
        {
            int count = MultiRegisterLoadGeneralScratchCount(result, needsAddressScratch: true);
            if (count == 0 && result is not null)
            {
                var abi = MachineAbi.ClassifyStorageValue(result.Type, result.StackKind, _target);
                if (abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect)
                    count = 1;
            }

            if (_target.IsRiscV)
            {
                // Preserve array and index across the fixed t3-t6 scratch sequence used by
                // the type check, bounds check and address calculation
                count += 2;
            }

            return count;
        }

        private int MultiRegisterLoadGeneralScratchCount(GenTree? result, bool needsAddressScratch)
        {
            if (!IsMultiRegisterValue(result))
                return 0;

            int count = needsAddressScratch ? 1 : 0;
            if (MultiRegisterValueHasRegisterClass(result, RegisterClass.General))
                count++;
            return count;
        }

        private int MultiRegisterLoadFloatScratchCount(GenTree? result)
            => MultiRegisterValueHasRegisterClass(result, RegisterClass.Float) ? 1 : 0;
        private int StoreArrayElementGeneralScratchCount(GenTree source)
        {
            var value = StoreArrayElementOperandValue(source);

            int count = MultiRegisterStoreGeneralScratchCount(value, needsAddressScratch: true);
            AbiValueInfo abi = default;
            if (value is not null)
            {
                abi = MachineAbi.ClassifyStorageValue(value.Type, value.StackKind, _target);
                if (count == 0 && abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect)
                    count = 1;
            }

            if (_target.IsRiscV)
            {
                // RISC-V array checks and address formation use the reserved t3-t6 scratch registers
                // Post LSRA operand normalization may also place spilled array store operands in those registers
                count += 2;
                if (value is not null &&
                    abi.PassingKind == AbiValuePassingKind.ScalarRegister &&
                    abi.RegisterClass == RegisterClass.General)
                {
                    count++;
                }
            }

            // The ARM64 element type check needs one pointer more than that target's two scratch registers
            if (_target.IsArm && count == 0 &&
                GenTree.RequiresArrayElementTypeCheck(source.Kind, source.RuntimeType ?? source.Type))
            {
                count = 1;
            }

            return count;
        }
        private static GenTree? StoreArrayElementOperandValue(GenTree node)
        {
            if ((uint)2 >= (uint)node.Operands.Length)
                return null;

            var operand = node.Operands[2];
            return operand.RegisterResult ?? operand;
        }
        private int MultiRegisterStoreGeneralScratchCount(GenTree? value, bool needsAddressScratch)
        {
            if (!IsMultiRegisterValue(value))
                return 0;

            int count = needsAddressScratch ? 1 : 0;
            if (MultiRegisterValueHasRegisterClass(value, RegisterClass.General))
                count++;
            return count;
        }

        private int MultiRegisterStoreFloatScratchCount(GenTree? value)
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
        public readonly UInt128 RegisterMask;
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
            UInt128 registerMask = default,
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
            UInt128 registerMask = default,
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
                    ? RegisterInfo.MaskOf(fixedRegister)
                    : RegisterInfo.DefaultMaskForClass(registerClass);
            MinimumRegisterCount = minimumRegisterCount;
        }

        public bool HasFlag(LinearRefPositionFlags flag) => (Flags & flag) != 0;
        public bool IsAbiSegment => AbiSegmentIndex >= 0;

        public UInt128 KillRegisterMask => Kind == LinearRefPositionKind.Kill
            ? RegisterMask
            : 0;

        public override string ToString()
        {
            string value = Value is not null ? Value.ToString() : "_";
            string reg = FixedRegister == MachineRegister.Invalid ? "" : " @" + MachineRegisters.Format(FixedRegister);
            string abi = AbiSegmentIndex < 0 ? "" : $" seg{AbiSegmentIndex}+{AbiSegmentOffset}:{AbiSegmentSize}";
            string min = MinimumRegisterCount == 1 ? "" : $" minReg={MinimumRegisterCount}";
            string mask = RegisterMask == 0 ? "" : $" mask={RegisterInfo.FormatMask(RegisterMask)}";
            return $"#{NodeId}@{Position} {Kind}[{OperandIndex}]{value}{abi}{reg}{min}{mask} {Flags}";
        }
    }
}
