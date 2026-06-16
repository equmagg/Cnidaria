using System;
using System.Collections.Immutable;
using Cnidaria.Cs;

namespace Cnidaria.C
{
    internal static class CAbi
    {
        public const int RegisterCount = 8;
        public const int MaxRegisterAggregateRegisters = 2;

        public static AbiValue ClassifyValue(TargetInfo target, QualifiedType type, bool isReturn = false)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            if (IsVoid(type))
                return AbiValue.Void(type);
            if (IsFloat32(type))
                return AbiValue.Scalar(type, AbiRegisterClass.Floating, size: 4, alignment: Math.Max(1, target.AlignOf(type)));
            if (IsFloat64(type) || IsLongDouble(type))
                return AbiValue.Scalar(type, AbiRegisterClass.Floating, size: Math.Min(8, Math.Max(1, target.SizeOf(type))), alignment: Math.Max(1, target.AlignOf(type)));

            if (IsAggregate(type))
            {
                var size = Math.Max(1, target.SizeOf(type));
                var alignment = Math.Max(1, target.AlignOf(type));
                if (size <= MaxRegisterAggregateSize(target))
                {
                    var segments = ImmutableArray.CreateBuilder<AbiSegment>();
                    for (var offset = 0; offset < size; offset += target.RegisterSize)
                    {
                        var segmentSize = Math.Min(target.RegisterSize, size - offset);
                        segments.Add(new AbiSegment(offset, segmentSize, AbiRegisterClass.General));
                    }

                    return new AbiValue(type, AbiPassingKind.MultiRegister, size, alignment, segments.ToImmutable());
                }

                return new AbiValue(type, isReturn ? AbiPassingKind.Indirect : AbiPassingKind.Stack, size, alignment, ImmutableArray<AbiSegment>.Empty);
            }

            if (IsPointerLike(type))
                return AbiValue.Scalar(type, AbiRegisterClass.General, size: target.PointerSize, alignment: target.PointerAlignment);
            if (IsIntegerLike(type))
            {
                var size = Math.Max(1, target.SizeOf(type));
                return AbiValue.Scalar(type, AbiRegisterClass.General, size, Math.Max(1, target.AlignOf(type)));
            }

            return AbiValue.Unsupported(type);
        }

        public static AbiLocation AssignArgumentLocation(AbiValue value, ref AbiCursor cursor, int stackSlotSize)
        {
            if (value.PassingKind == AbiPassingKind.Void)
                return AbiLocation.None;
            if (value.PassingKind == AbiPassingKind.Unsupported)
                throw new NotSupportedException("Unsupported ABI value: " + value.Type.ToDisplayString() + ".");
            if (value.PassingKind == AbiPassingKind.Indirect)
                throw new NotSupportedException("Indirect return ABI value cannot be assigned as a normal argument: " + value.Type.ToDisplayString() + ".");

            if (value.PassingKind == AbiPassingKind.Scalar)
            {
                var cls = value.Segments.Length != 0 ? value.Segments[0].RegisterClass : AbiRegisterClass.General;
                return AssignScalarArgumentLocation(cls, value.Size, ref cursor, stackSlotSize);
            }

            if (value.PassingKind == AbiPassingKind.MultiRegister)
            {
                var firstStack = -1;
                foreach (var segment in value.Segments)
                {
                    var loc = AssignScalarArgumentLocation(segment.RegisterClass, segment.Size, ref cursor, stackSlotSize, segment.Offset);
                    if (loc.Kind == AbiLocationKind.Stack && firstStack < 0)
                        firstStack = loc.StackSlotIndex;
                }

                return firstStack < 0 ? AbiLocation.RegisterGroup : AbiLocation.FromStack(firstStack, 0, AlignUp(value.Size, stackSlotSize), value.Alignment);
            }

            var slot = cursor.Stack;
            cursor.Stack = checked(cursor.Stack + SlotsFor(value.Size, stackSlotSize));
            return AbiLocation.FromStack(slot, 0, value.Size, value.Alignment);
        }

        public static AbiLocation AssignSegmentArgumentLocation(AbiSegment segment, ref AbiCursor cursor, int stackSlotSize)
            => AssignScalarArgumentLocation(segment.RegisterClass, segment.Size, ref cursor, stackSlotSize, segment.Offset);

        public static AbiLocation AssignScalarArgumentLocation(AbiRegisterClass registerClass, int size, ref AbiCursor cursor, int stackSlotSize, int stackOffset = 0)
        {
            if (registerClass == AbiRegisterClass.Floating)
            {
                if (cursor.Float < RegisterCount)
                    return AbiLocation.FromRegister((MachineRegister)((int)MachineRegister.F10 + cursor.Float++), size, registerClass, stackOffset);
            }
            else
            {
                if (cursor.Integer < RegisterCount)
                    return AbiLocation.FromRegister((MachineRegister)((int)MachineRegister.X10 + cursor.Integer++), size, registerClass, stackOffset);
            }

            var slot = cursor.Stack;
            cursor.Stack++;
            return AbiLocation.FromStack(slot, stackOffset % stackSlotSize, size, Math.Min(Math.Max(1, size), stackSlotSize));
        }

        public static MachineRegister ReturnRegister(AbiSegment segment, int ordinal)
        {
            if (segment.RegisterClass == AbiRegisterClass.Floating)
                return (MachineRegister)((int)MachineRegister.F10 + ordinal);
            return (MachineRegister)((int)MachineRegister.X10 + ordinal);
        }

        public static bool RequiresHiddenReturnBuffer(TargetInfo target, QualifiedType returnType)
            => ClassifyValue(target, returnType, isReturn: true).PassingKind == AbiPassingKind.Indirect;

        public static AbiLocation AssignHiddenReturnBufferLocation(TargetInfo target, ref AbiCursor cursor, int stackSlotSize)
            => AssignScalarArgumentLocation(AbiRegisterClass.General, target.PointerSize, ref cursor, stackSlotSize);

        public static int MaxRegisterAggregateSize(TargetInfo target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));
            return checked(Math.Max(1, target.RegisterSize) * MaxRegisterAggregateRegisters);
        }

        public static int ComputeOutgoingArgumentAreaSize(LirInstruction instruction, int startOperand, TargetInfo target, int stackSlotSize, bool includeVariadicHomeArea)
        {
            if (instruction is null)
                throw new ArgumentNullException(nameof(instruction));
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            var cursor = new AbiCursor();
            var maxByte = 0;
            if (instruction.Result is not null && RequiresHiddenReturnBuffer(target, instruction.Result.Type))
            {
                var hidden = AssignHiddenReturnBufferLocation(target, ref cursor, stackSlotSize);
                if (hidden.Kind == AbiLocationKind.Stack)
                    maxByte = Math.Max(maxByte, hidden.EndByte(stackSlotSize));
            }

            for (var i = startOperand; i < instruction.Operands.Length; i++)
            {
                var value = ClassifyValue(target, instruction.Operands[i].Type);
                if (value.PassingKind == AbiPassingKind.MultiRegister)
                {
                    foreach (var segment in value.Segments)
                    {
                        var loc = AssignSegmentArgumentLocation(segment, ref cursor, stackSlotSize);
                        if (loc.Kind == AbiLocationKind.Stack)
                            maxByte = Math.Max(maxByte, loc.EndByte(stackSlotSize));
                    }
                }
                else
                {
                    var loc = AssignArgumentLocation(value, ref cursor, stackSlotSize);
                    if (loc.Kind == AbiLocationKind.Stack)
                        maxByte = Math.Max(maxByte, loc.EndByte(stackSlotSize));
                }
            }

            var signature = instruction.CallSignature;
            if (includeVariadicHomeArea && signature is not null && signature.IsVariadic)
            {
                var fixedCount = signature.Parameters.Length;
                var variadicCount = Math.Max(0, instruction.Operands.Length - 1 - fixedCount);
                maxByte = checked(AlignUp(Math.Max(maxByte, cursor.Stack * stackSlotSize), stackSlotSize) + variadicCount * stackSlotSize);
            }

            return AlignUp(maxByte, stackSlotSize);
        }

        public static int SlotsFor(int size, int stackSlotSize)
            => size <= 0 ? 0 : Math.Max(1, AlignUp(size, stackSlotSize) / stackSlotSize);

        public static int AlignUp(int value, int alignment)
        {
            if (alignment <= 1)
                return value;
            var remainder = value % alignment;
            return remainder == 0 ? value : checked(value + alignment - remainder);
        }

        public static bool IsAggregate(QualifiedType type)
            => type.Type.Kind is TypeKind.Struct or TypeKind.Union or TypeKind.Array;

        private static bool IsVoid(QualifiedType type)
            => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Void };

        private static bool IsPointerLike(QualifiedType type)
            => type.Type.Kind is TypeKind.Pointer or TypeKind.Array or TypeKind.Function;

        private static bool IsIntegerLike(QualifiedType type)
            => (type.Type.Kind is TypeKind.Builtin or TypeKind.Enum) && !IsFloat32(type) && !IsFloat64(type) && !IsLongDouble(type) && !IsVoid(type);

        private static bool IsFloat32(QualifiedType type)
            => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Float };

        private static bool IsFloat64(QualifiedType type)
            => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Double };

        private static bool IsLongDouble(QualifiedType type)
            => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.LongDouble };
    }

    internal enum AbiPassingKind
    {
        Void,
        Scalar,
        MultiRegister,
        Stack,
        Indirect,
        Unsupported,
    }

    internal enum AbiRegisterClass
    {
        General,
        Floating,
    }

    internal enum AbiLocationKind
    {
        None,
        Register,
        RegisterGroup,
        Stack,
    }

    internal struct AbiCursor
    {
        public int Integer;
        public int Float;
        public int Stack;
    }

    internal readonly struct AbiSegment
    {
        public int Offset { get; }
        public int Size { get; }
        public AbiRegisterClass RegisterClass { get; }

        public AbiSegment(int offset, int size, AbiRegisterClass registerClass)
        {
            Offset = offset < 0 ? 0 : offset;
            Size = size <= 0 ? 1 : size;
            RegisterClass = registerClass;
        }
    }

    internal readonly struct AbiValue
    {
        public QualifiedType Type { get; }
        public AbiPassingKind PassingKind { get; }
        public int Size { get; }
        public int Alignment { get; }
        public ImmutableArray<AbiSegment> Segments { get; }

        public AbiValue(QualifiedType type, AbiPassingKind passingKind, int size, int alignment, ImmutableArray<AbiSegment> segments)
        {
            Type = type;
            PassingKind = passingKind;
            Size = Math.Max(0, size);
            Alignment = Math.Max(1, alignment);
            Segments = segments.IsDefault ? ImmutableArray<AbiSegment>.Empty : segments;
        }

        public static AbiValue Void(QualifiedType type)
            => new AbiValue(type, AbiPassingKind.Void, 0, 1, ImmutableArray<AbiSegment>.Empty);

        public static AbiValue Scalar(QualifiedType type, AbiRegisterClass registerClass, int size, int alignment)
            => new AbiValue(type, AbiPassingKind.Scalar, size, alignment, ImmutableArray.Create(new AbiSegment(0, size, registerClass)));

        public static AbiValue Unsupported(QualifiedType type)
            => new AbiValue(type, AbiPassingKind.Unsupported, 0, 1, ImmutableArray<AbiSegment>.Empty);
    }

    internal readonly struct AbiLocation
    {
        private readonly AbiLocationKind _kind;
        private readonly MachineRegister _register;
        private readonly int _stackSlotIndex;
        private readonly int _stackOffset;
        private readonly int _size;
        private readonly int _alignment;
        private readonly AbiRegisterClass _registerClass;

        public static readonly AbiLocation None = new AbiLocation(AbiLocationKind.None, MachineRegister.Invalid, 0, 0, 0, 1, AbiRegisterClass.General);
        public static readonly AbiLocation RegisterGroup = new AbiLocation(AbiLocationKind.RegisterGroup, MachineRegister.Invalid, 0, 0, 0, 1, AbiRegisterClass.General);

        public AbiLocationKind Kind => _kind;
        public MachineRegister Register => _register;
        public int StackSlotIndex => _stackSlotIndex;
        public int StackOffset => _stackOffset;
        public int Size => _size;
        public int Alignment => _alignment;
        public AbiRegisterClass RegisterClass => _registerClass;

        private AbiLocation(AbiLocationKind kind, MachineRegister register, int stackSlotIndex, int stackOffset, int size, int alignment, AbiRegisterClass registerClass)
        {
            _kind = kind;
            _register = register;
            _stackSlotIndex = stackSlotIndex;
            _stackOffset = stackOffset < 0 ? 0 : stackOffset;
            _size = size < 0 ? 0 : size;
            _alignment = alignment < 1 ? 1 : alignment;
            _registerClass = registerClass;
        }

        public static AbiLocation FromRegister(MachineRegister register, int size, AbiRegisterClass registerClass, int stackOffset = 0)
        {
            var alignment = Math.Min(size < 1 ? 1 : size, 8);
            return new AbiLocation(AbiLocationKind.Register, register, -1, stackOffset, size, alignment, registerClass);
        }

        public static AbiLocation FromStack(int slotIndex, int offset, int size, int alignment)
            => new AbiLocation(AbiLocationKind.Stack, MachineRegister.Invalid, slotIndex, offset, size, alignment, AbiRegisterClass.General);

        public int StackByteOffset(int stackSlotSize)
            => checked(_stackSlotIndex * stackSlotSize + _stackOffset);

        public int EndByte(int stackSlotSize)
            => checked(StackByteOffset(stackSlotSize) + Math.Max(1, _size));
    }
}
