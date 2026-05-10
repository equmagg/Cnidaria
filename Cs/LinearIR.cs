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
        // helper expanded nodes such as allocation and type-check nodes may still lower to calls
        CallerSavedKill = 1 << 1,

        // The backend expects all non contained operands and any result to be registers
        RequiresRegisterOperands = 1 << 2,

        // The node consumes all operands directly in its own codegen shape
        IsStandaloneLoweredNode = 1 << 3,

        GcSafePoint = 1 << 4,

        HasMemoryOperand = 1 << 5,

        UsesTrackedLocal = 1 << 6,
        DefinesTrackedLocal = 1 << 7,
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

            byte internalGeneral = InternalGeneralRegisterCount(source, memoryAccess);
            byte internalFloat = InternalFloatRegisterCount(source);

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
                GenTreeKind.Box or
                GenTreeKind.UnboxAny or
                GenTreeKind.CastClass or
                GenTreeKind.IsInst or
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

        private static byte InternalGeneralRegisterCount(GenTree source, LinearMemoryAccess memoryAccess)
        {
            int count = source.Kind switch
            {
                GenTreeKind.ArrayElement => 1,
                GenTreeKind.ArrayElementAddr => 1,
                GenTreeKind.StoreArrayElement => 1,
                GenTreeKind.ArrayDataRef => 0,
                GenTreeKind.PointerElementAddr => 1,
                GenTreeKind.PointerDiff => 1,
                GenTreeKind.StackAlloc => 1,
                _ => 0,
            };

            if ((memoryAccess.Flags & LinearMemoryAccessFlags.RequiresWriteBarrier) != 0)
                count++;

            if (count > byte.MaxValue)
                throw new InvalidOperationException($"Node {source.Id} requires too many internal general registers.");

            return (byte)count;
        }

        private static byte InternalFloatRegisterCount(GenTree source)
        {
            return 0;
        }
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

        public bool SplitCriticalEdges { get; set; }

        public bool Validate { get; set; } = true;
    }

    internal static class GenTreeLinearIrRationalizer
    {
        public static GenTreeProgram LowerProgram(GenTreeProgram program, LinearRationalizationOptions? options = null)
        {
            if (program is null)
                throw new ArgumentNullException(nameof(program));

            options ??= LinearRationalizationOptions.Default;

            var methods = ImmutableArray.CreateBuilder<GenTreeMethod>(program.Methods.Length);
            for (int i = 0; i < program.Methods.Length; i++)
                methods.Add(LowerMethod(program.Methods[i], options));

            return new GenTreeProgram(methods.ToImmutable());
        }

        public static GenTreeMethod LowerMethod(GenTreeMethod method, LinearRationalizationOptions? options = null)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            options ??= LinearRationalizationOptions.Default;
            if (options.SplitCriticalEdges)
                method = GenTreeCriticalEdgeSplitter.SplitCriticalEdges(method);

            var cfg = ControlFlowGraph.Build(method, options.IncludeExceptionEdges);
            var builder = new MethodBuilder(method, cfg);
            var lowered = builder.Run();
            var result = LinearLiveness.Attach(lowered);

            if (options.Validate)
                LinearVerifier.Verify(result);

            return result;
        }

        private sealed class MethodBuilder
        {
            private readonly GenTreeMethod _method;
            private readonly ControlFlowGraph _cfg;
            private readonly Dictionary<GenTreeValueKey, GenTreeValueInfo> _valueInfos = new();
            private readonly List<GenTree> _allNodes = new();
            private readonly List<GenTree>[] _nodesByBlock;
            private int _nextNodeId;
            private int _nextSyntheticTreeId;
            private int _currentBlockId;
            private int _currentBlockOrdinal;

            public MethodBuilder(GenTreeMethod method, ControlFlowGraph cfg)
            {
                _method = method;
                _cfg = cfg;
                _nodesByBlock = new List<GenTree>[method.Blocks.Length];
                for (int i = 0; i < _nodesByBlock.Length; i++)
                    _nodesByBlock[i] = new List<GenTree>();
                _nextSyntheticTreeId = ComputeNextSyntheticTreeId(method);
            }

            public GenTreeMethod Run()
            {
                ResetExistingLinearState();
                LowerBlocks();
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
                    for (int i = 0; i < node.Operands.Length; i++)
                        Reset(node.Operands[i]);
                }
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

                return stackKind == GenStackKind.R8
                    ? RegisterClass.Float
                    : RegisterClass.General;
            }

            private void LowerBlocks()
            {
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var block = _method.Blocks[b];
                    _currentBlockId = block.Id;
                    _currentBlockOrdinal = 0;

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

            private void LowerEval(GenTree tree)
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

            private static bool MustMaterializeForSideEffects(GenTree tree)
            {
                if (tree.HasSideEffect || tree.CanThrow || tree.ContainsCall)
                    return true;

                if ((tree.Flags & (GenTreeFlags.MemoryWrite | GenTreeFlags.Allocation | GenTreeFlags.ControlFlow | GenTreeFlags.ExceptionFlow | GenTreeFlags.Ordered)) != 0)
                    return true;

                return tree.Kind is
                    GenTreeKind.Call or
                    GenTreeKind.VirtualCall or
                    GenTreeKind.NewObject or
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
                => stackKind == GenStackKind.R8 || type?.Name is "Single" or "Double";

            private GenTree LowerValue(GenTree tree)
            {
                var result = LowerValueOrVoid(tree);
                if (result is null)
                    throw new InvalidOperationException($"Tree node {tree.Id} ({tree.Kind}) does not produce a value.");
                return result;
            }

            private GenTree? LowerValueOrVoid(GenTree tree)
            {
                var uses = LowerOperands(tree);

                GenTree? result = ProducesValue(tree) ? NewTemp(tree) : null;

                EmitTree(tree, uses, result);
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

                if (node.RegisterResult is not null)
                {
                    var value = ValueKeyForNode(node.RegisterResult);
                    if (_valueInfos.TryGetValue(value, out var info) && info.Origin == GenTreeValueOrigin.TreeNode)
                        _valueInfos[value] = info.WithDefinitionNode(node, node.LinearBlockId, node.LinearId);
                }
            }

            private static bool IsControlTransfer(GenTree source)
                => source.Kind is GenTreeKind.Branch or GenTreeKind.BranchTrue or GenTreeKind.BranchFalse or
                   GenTreeKind.Return or GenTreeKind.Throw or GenTreeKind.Rethrow or GenTreeKind.EndFinally;

            private GenTreeMethod Freeze()
            {
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var nodes = _nodesByBlock[b].ToImmutableArray();
                    _method.Blocks[b].SetLinearNodes(nodes);
                }

                var values = new List<GenTreeValueInfo>(_valueInfos.Values);
                values.Sort(static (a, b) => string.Compare(a.Value.ToString(), b.Value.ToString(), StringComparison.Ordinal));

                _method.AttachLinearBackendState(
                    _cfg,
                    _allNodes.ToImmutableArray(),
                    values.ToImmutableArray(),
                    new Dictionary<GenTreeValueKey, GenTreeValueInfo>(_valueInfos),
                    LinearBlockOrder.Compute(_cfg));
                return _method;
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

    internal static class LinearLiveness
    {
        public static GenTreeMethod Attach(GenTreeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            var intervals = BuildIntervals(method, out var intervalMap);
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
                result.Add(interval);
                map[value] = interval;
            }

            intervalMap = map;
            return result.ToImmutable();
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

            VerifyBlocks(method);
            VerifyLinearBlockOrder(method);
            VerifyOperands(method);
            VerifyValues(method);
            VerifyRefPositions(method);
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
                if (!declared.Add(method.Values[i].Value))
                    throw new InvalidOperationException("Duplicate GenTree value " + method.Values[i].Value + ".");
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
                    sb.Append(source.Kind == GenTreeKind.VirtualCall ? "callvirt " : "call ")
                      .Append(MethodName(source.Method)).Append(' ');
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
