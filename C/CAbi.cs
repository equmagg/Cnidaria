using System;
using System.Collections.Immutable;
using Cnidaria.Cs;

namespace Cnidaria.C
{
    internal static class CAbi
    {
        public const int RegisterCount = 8;
        public const int MaxRegisterAggregateRegisters = 2;

        public static LirRegisterClass PreferredLirRegisterClass(TargetInfo target, QualifiedType type)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            if (type.Type is BuiltinType builtin)
            {
                return builtin.BuiltinKind switch
                {
                    BuiltinTypeKind.Void => LirRegisterClass.Void,
                    BuiltinTypeKind.Float or BuiltinTypeKind.Double or BuiltinTypeKind.LongDouble =>
                        TargetRegisterInfo.PreferredFloatingPointRegisterClass(target, type, isVariadicUnnamedArgument: false),
                    _ => LirRegisterClass.General,
                };
            }

            return type.Type.Kind switch
            {
                TypeKind.Pointer or TypeKind.Function => LirRegisterClass.Address,
                TypeKind.Array or TypeKind.Struct or TypeKind.Union => LirRegisterClass.Aggregate,
                TypeKind.Enum => LirRegisterClass.General,
                TypeKind.Error => LirRegisterClass.Unknown,
                _ => LirRegisterClass.Unknown,
            };
        }

        public static bool UsesHardwareFloatingRegister(TargetInfo target, QualifiedType type, bool isVariadicUnnamedArgument)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            return HardwareFloatingAbiRegisterClass(target, type, isVariadicUnnamedArgument).HasValue;
        }

        private static AbiRegisterClass? HardwareFloatingAbiRegisterClass(TargetInfo target, QualifiedType type, bool isVariadicUnnamedArgument)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            if (!IsFloating(type))
                return null;

            if (target.IsRiscV)
            {
                if (isVariadicUnnamedArgument)
                    return null;

                var abiFlen = RiscVAbiFloatingRegisterSize(target);
                return abiFlen > 0 && Math.Max(1, target.SizeOf(type)) <= abiFlen
                    ? AbiRegisterClass.Floating
                    : null;
            }

            if (target.IsArm)
            {
                if (target.Architecture == TargetArchitectureKind.Arm32 && isVariadicUnnamedArgument)
                    return null;

                var abiFlen = TargetRegisterInfo.ArmAbiFloatingRegisterSize(target);
                return abiFlen > 0 && Math.Max(1, target.SizeOf(type)) <= abiFlen
                    ? AbiRegisterClass.Floating
                    : null;
            }

            if (target.IsX86)
            {
                var abiFlen = TargetRegisterInfo.X86AbiFloatingRegisterSize(target);
                return abiFlen > 0 && Math.Max(1, target.SizeOf(type)) <= abiFlen
                    ? AbiRegisterClass.Vector
                    : null;
            }

            return AbiRegisterClass.Floating;
        }

        internal static AbiValue ClassifyValue(TargetInfo target, QualifiedType type, bool isReturn, bool isVariadicUnnamedArgument)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            if (IsVoid(type))
                return AbiValue.Void(type);

            if (target.IsRiscV)
                return ClassifyRiscVValue(target, type, isReturn, isVariadicUnnamedArgument);

            if (target.IsArm)
                return ClassifyArmValue(target, type, isReturn, isVariadicUnnamedArgument);

            if (target.IsX86)
                return ClassifyX86Value(target, type, isReturn, isVariadicUnnamedArgument);

            return ClassifyRegisterBytecodeValue(target, type, isReturn);
        }

        private static AbiValue ClassifyRegisterBytecodeValue(TargetInfo target, QualifiedType type, bool isReturn)
        {
            if (IsFloat32(type))
                return ScalarForTarget(target, type, AbiRegisterClass.Floating, size: 4, alignment: Math.Max(1, target.AlignOf(type)));
            if (IsFloat64(type) || IsLongDouble(type))
                return ScalarForTarget(target, type, AbiRegisterClass.Floating, size: Math.Min(8, Math.Max(1, target.SizeOf(type))), alignment: Math.Max(1, target.AlignOf(type)));

            if (IsAggregate(type))
                return ClassifySmallRegisterAggregate(target, type, isReturn, passLargeByReference: false);

            if (IsPointerLike(type))
                return ScalarForTarget(target, type, AbiRegisterClass.General, size: target.PointerSize, alignment: target.PointerAlignment);
            if (IsIntegerLike(type))
            {
                var size = Math.Max(1, target.SizeOf(type));
                return ScalarForTarget(target, type, AbiRegisterClass.General, size, Math.Max(1, target.AlignOf(type)));
            }

            return AbiValue.Unsupported(type);
        }

        private static AbiValue ClassifyRiscVValue(TargetInfo target, QualifiedType type, bool isReturn, bool isVariadicUnnamedArgument)
        {
            var size = Math.Max(1, target.SizeOf(type));
            var alignment = Math.Max(1, target.AlignOf(type));

            var fpClass = HardwareFloatingAbiRegisterClass(target, type, isVariadicUnnamedArgument);
            if (fpClass.HasValue)
                return ScalarForTarget(target, type, fpClass.Value, size, alignment);

            if (IsAggregate(type))
                return ClassifySmallRegisterAggregate(target, type, isReturn, passLargeByReference: true);

            if (IsPointerLike(type))
                return ScalarForTarget(target, type, AbiRegisterClass.General, size: target.PointerSize, alignment: target.PointerAlignment);

            if (IsIntegerLike(type) || IsFloating(type))
                return ClassifyIntegerConventionScalar(target, type, size, alignment, isReturn, isVariadicUnnamedArgument);

            return AbiValue.Unsupported(type);
        }


        private static AbiValue ClassifyArmValue(TargetInfo target, QualifiedType type, bool isReturn, bool isVariadicUnnamedArgument)
        {
            var size = Math.Max(1, target.SizeOf(type));
            var alignment = Math.Max(1, target.AlignOf(type));

            var fpClass = HardwareFloatingAbiRegisterClass(target, type, isVariadicUnnamedArgument);
            if (fpClass.HasValue)
                return ScalarForTarget(target, type, fpClass.Value, size, alignment);

            if (IsAggregate(type))
                return target.Architecture == TargetArchitectureKind.Arm64
                    ? ClassifySmallRegisterAggregate(target, type, isReturn, passLargeByReference: true)
                    : ClassifyArm32Aggregate(target, type, isReturn);

            if (IsPointerLike(type))
                return ScalarForTarget(target, type, AbiRegisterClass.General, size: target.PointerSize, alignment: target.PointerAlignment);

            if (IsIntegerLike(type) || IsFloating(type))
                return target.Architecture == TargetArchitectureKind.Arm32
                    ? ClassifyArm32Scalar(target, type, size, alignment, isReturn)
                    : ClassifyIntegerConventionScalar(target, type, size, alignment, isReturn, isVariadicUnnamedArgument: false);

            return AbiValue.Unsupported(type);
        }

        private static AbiValue ClassifyArm32Scalar(TargetInfo target, QualifiedType type, int size, int alignment, bool isReturn)
        {
            if (size <= target.RegisterSize)
                return ScalarForTarget(target, type, AbiRegisterClass.General, size, alignment);

            if (size <= checked(target.RegisterSize * 2))
            {
                var requireAlignedPair = !isReturn && alignment >= checked(target.RegisterSize * 2);
                return AbiValue.MultiRegister(type, size, alignment, CreateGeneralSegments(target, size, alignment, requireAlignedPair));
            }

            return isReturn ? IndirectForTarget(target, type, size, alignment) : AbiValue.Stack(type, size, alignment);
        }

        private static AbiValue ClassifyArm32Aggregate(TargetInfo target, QualifiedType type, bool isReturn)
        {
            var size = Math.Max(1, target.SizeOf(type));
            var alignment = Math.Max(1, target.AlignOf(type));

            if (isReturn)
            {
                if (size <= target.RegisterSize)
                    return AbiValue.MultiRegister(type, size, alignment, CreateGeneralSegments(target, size, alignment, requireVariadicAlignedPair: false));

                return IndirectForTarget(target, type, size, alignment);
            }

            if (size <= checked(target.RegisterSize * 4))
                return AbiValue.MultiRegister(type, size, alignment, CreateGeneralSegments(target, size, alignment, requireVariadicAlignedPair: false));

            return AbiValue.Stack(type, size, alignment);
        }

        private static AbiValue ClassifyX86Value(TargetInfo target, QualifiedType type, bool isReturn, bool isVariadicUnnamedArgument)
        {
            var size = Math.Max(1, target.SizeOf(type));
            var alignment = Math.Max(1, target.AlignOf(type));

            var fpClass = HardwareFloatingAbiRegisterClass(target, type, isVariadicUnnamedArgument);
            if (fpClass.HasValue)
                return ScalarForTarget(target, type, fpClass.Value, size, alignment);

            if (IsLongDouble(type))
                return isReturn || TargetRegisterInfo.IsWindowsX64(target)
                    ? IndirectForTarget(target, type, size, alignment)
                    : AbiValue.Stack(type, size, alignment);

            if (IsAggregate(type))
                return ClassifyX86Aggregate(target, type, isReturn);

            if (IsPointerLike(type))
                return ScalarForTarget(target, type, AbiRegisterClass.General, target.PointerSize, target.PointerAlignment);

            if (IsIntegerLike(type) || IsFloating(type))
                return ClassifyX86Scalar(target, type, size, alignment, isReturn);

            return AbiValue.Unsupported(type);
        }

        private static AbiValue ClassifyX86Scalar(TargetInfo target, QualifiedType type, int size, int alignment, bool isReturn)
        {
            if (target.Architecture == TargetArchitectureKind.X86)
            {
                if (size <= target.RegisterSize)
                    return ScalarForTarget(target, type, AbiRegisterClass.General, size, alignment);

                if (size <= 8)
                    return AbiValue.MultiRegister(type, size, alignment, CreateGeneralSegments(target, size, alignment, requireVariadicAlignedPair: false));

                return isReturn ? IndirectForTarget(target, type, size, alignment) : AbiValue.Stack(type, size, alignment);
            }

            if (size <= target.RegisterSize)
                return ScalarForTarget(target, type, AbiRegisterClass.General, size, alignment);

            if (size <= checked(target.RegisterSize * MaxRegisterAggregateRegisters))
                return AbiValue.MultiRegister(type, size, alignment, CreateGeneralSegments(target, size, alignment, requireVariadicAlignedPair: false));

            return isReturn ? IndirectForTarget(target, type, size, alignment) : AbiValue.Stack(type, size, alignment);
        }

        private static AbiValue ClassifyX86Aggregate(TargetInfo target, QualifiedType type, bool isReturn)
        {
            var size = Math.Max(1, target.SizeOf(type));
            var alignment = Math.Max(1, target.AlignOf(type));

            if (target.Architecture == TargetArchitectureKind.X86)
                return isReturn ? IndirectForTarget(target, type, size, alignment) : AbiValue.Stack(type, size, alignment);

            if (TargetRegisterInfo.IsWindowsX64(target))
            {
                if (size is 1 or 2 or 4 or 8)
                    return ScalarForTarget(target, type, AbiRegisterClass.General, size, alignment);

                return IndirectForTarget(target, type, size, alignment);
            }

            if (size <= checked(target.RegisterSize * MaxRegisterAggregateRegisters))
                return AbiValue.MultiRegister(type, size, alignment, CreateGeneralSegments(target, size, alignment, requireVariadicAlignedPair: false));

            return isReturn ? IndirectForTarget(target, type, size, alignment) : AbiValue.Stack(type, size, alignment);
        }

        private static AbiValue ClassifySmallRegisterAggregate(TargetInfo target, QualifiedType type, bool isReturn, bool passLargeByReference)
        {
            var size = Math.Max(1, target.SizeOf(type));
            var alignment = Math.Max(1, target.AlignOf(type));
            if (size <= MaxRegisterAggregateSize(target))
                return AbiValue.MultiRegister(type, size, alignment, CreateGeneralSegments(target, size, alignment, requireVariadicAlignedPair: false));

            if (passLargeByReference)
                return IndirectForTarget(target, type, size, alignment);

            return new AbiValue(type, isReturn ? AbiPassingKind.Indirect : AbiPassingKind.Stack, size, alignment, ImmutableArray<AbiSegment>.Empty, indirectSize: target.PointerSize);
        }

        private static AbiValue ClassifyIntegerConventionScalar(
            TargetInfo target,
            QualifiedType type,
            int size,
            int alignment,
            bool isReturn,
            bool isVariadicUnnamedArgument)
        {
            var registerSize = Math.Max(1, target.RegisterSize);
            if (size <= registerSize)
                return ScalarForTarget(target, type, AbiRegisterClass.General, size, alignment);

            if (size <= checked(registerSize * MaxRegisterAggregateRegisters))
            {
                var requirePair = isVariadicUnnamedArgument && size == checked(registerSize * 2) && alignment >= checked(registerSize * 2);
                return AbiValue.MultiRegister(type, size, alignment, CreateGeneralSegments(target, size, alignment, requirePair));
            }

            return IndirectForTarget(target, type, size, alignment);
        }

        private static AbiValue ScalarForTarget(TargetInfo target, QualifiedType type, AbiRegisterClass registerClass, int size, int alignment)
            => new AbiValue(type, AbiPassingKind.Scalar, size, alignment, ImmutableArray.Create(CreateSegment(target, 0, size, registerClass)));

        private static AbiValue IndirectForTarget(TargetInfo target, QualifiedType type, int size, int alignment)
            => new AbiValue(type, AbiPassingKind.Indirect, size, alignment, ImmutableArray.Create(CreateSegment(target, 0, target.PointerSize, AbiRegisterClass.General)), indirectSize: target.PointerSize);

        private static ImmutableArray<AbiSegment> CreateGeneralSegments(TargetInfo target, int size, int alignment, bool requireVariadicAlignedPair)
        {
            var registerSize = Math.Max(1, target.RegisterSize);
            var segments = ImmutableArray.CreateBuilder<AbiSegment>();
            for (var offset = 0; offset < size; offset += registerSize)
            {
                var segmentSize = Math.Min(registerSize, size - offset);
                var isFirstSegment = offset == 0;
                var registerSlotAlignment = requireVariadicAlignedPair && isFirstSegment ? 2 : 1;
                var minimumRegisterSlots = requireVariadicAlignedPair && isFirstSegment ? 2 : 1;
                var forceStackAfterStack = requireVariadicAlignedPair && isFirstSegment;
                var stackSlotAlignment = isFirstSegment ? Math.Max(1, AlignUp(alignment, registerSize) / registerSize) : 1;
                segments.Add(CreateSegment(target, offset, segmentSize, AbiRegisterClass.General, registerSlotAlignment, minimumRegisterSlots, forceStackAfterStack, stackSlotAlignment));
            }

            return segments.ToImmutable();
        }

        private static AbiSegment CreateSegment(
            TargetInfo target,
            int offset,
            int size,
            AbiRegisterClass registerClass,
            int registerSlotAlignment = 1,
            int minimumRegisterSlots = 1,
            bool forceStackAfterStack = false,
            int stackSlotAlignment = 1)
        {
            var argumentRegisters = registerClass switch
            {
                AbiRegisterClass.Floating => TargetRegisterInfo.FloatingArgumentRegisters(target),
                AbiRegisterClass.Vector => TargetRegisterInfo.VectorArgumentRegisters(target),
                _ => TargetRegisterInfo.IntegerArgumentRegisters(target),
            };
            var returnRegisters = registerClass switch
            {
                AbiRegisterClass.Floating => TargetRegisterInfo.FloatingReturnRegisters(target),
                AbiRegisterClass.Vector => TargetRegisterInfo.VectorReturnRegisters(target),
                _ => TargetRegisterInfo.IntegerReturnRegisters(target),
            };

            return new AbiSegment(
                offset,
                size,
                registerClass,
                registerSlotAlignment,
                minimumRegisterSlots,
                forceStackAfterStack,
                stackSlotAlignment,
                argumentRegisters,
                returnRegisters,
                TargetRegisterInfo.UsesUnifiedArgumentCursor(target));
        }

        public static AbiLocation AssignArgumentLocation(AbiValue value, ref AbiCursor cursor, int stackSlotSize)
        {
            if (value.PassingKind == AbiPassingKind.Void)
                return AbiLocation.None;
            if (value.PassingKind == AbiPassingKind.Unsupported)
                throw new NotSupportedException("Unsupported ABI value: " + value.Type.ToDisplayString() + ".");
            if (value.PassingKind == AbiPassingKind.Indirect)
            {
                var segment = value.Segments.Length != 0 ? value.Segments[0] : new AbiSegment(0, value.IndirectSize, AbiRegisterClass.General);
                return AssignSegmentArgumentLocation(segment, ref cursor, stackSlotSize);
            }

            if (value.PassingKind == AbiPassingKind.Scalar)
            {
                var segment = value.Segments.Length != 0 ? value.Segments[0] : new AbiSegment(0, value.Size, AbiRegisterClass.General);
                return AssignSegmentArgumentLocation(segment, ref cursor, stackSlotSize);
            }

            if (value.PassingKind == AbiPassingKind.MultiRegister)
            {
                var firstStack = -1;
                foreach (var segment in value.Segments)
                {
                    var loc = AssignSegmentArgumentLocation(segment, ref cursor, stackSlotSize);
                    if (loc.Kind == AbiLocationKind.Stack && firstStack < 0)
                        firstStack = loc.StackSlotIndex;
                }

                return firstStack < 0 ? AbiLocation.RegisterGroup : AbiLocation.FromStack(firstStack, 0, AlignUp(value.Size, stackSlotSize), value.Alignment);
            }

            cursor.Stack = AlignRegisterCursor(cursor.Stack, Math.Max(1, AlignUp(value.Alignment, stackSlotSize) / stackSlotSize));
            var slot = cursor.Stack;
            cursor.Stack = checked(cursor.Stack + SlotsFor(value.Size, stackSlotSize));
            return AbiLocation.FromStack(slot, 0, value.Size, value.Alignment);
        }

        public static AbiLocation AssignSegmentArgumentLocation(AbiSegment segment, ref AbiCursor cursor, int stackSlotSize)
            => AssignScalarArgumentLocation(
                segment.RegisterClass,
                segment.Size,
                ref cursor,
                stackSlotSize,
                segment.Offset,
                segment.RegisterSlotAlignment,
                segment.MinimumRegisterSlots,
                segment.ForceStackAfterStack,
                segment.StackSlotAlignment,
                segment.ArgumentRegisters,
                segment.UsesUnifiedArgumentCursor);

        public static AbiLocation AssignScalarArgumentLocation(
            AbiRegisterClass registerClass,
            int size,
            ref AbiCursor cursor,
            int stackSlotSize,
            int stackOffset = 0,
            int registerSlotAlignment = 1,
            int minimumRegisterSlots = 1,
            bool forceStackAfterStack = false,
            int stackSlotAlignment = 1,
            ImmutableArray<MachineRegister> argumentRegisters = default,
            bool usesUnifiedArgumentCursor = false)
        {
            if (stackSlotSize <= 0)
                stackSlotSize = 1;

            if (argumentRegisters.IsDefault)
            {
                argumentRegisters = registerClass switch
                {
                    AbiRegisterClass.Floating => ImmutableArray.Create(MachineRegister.F10, MachineRegister.F11, MachineRegister.F12, MachineRegister.F13, MachineRegister.F14, MachineRegister.F15, MachineRegister.F16, MachineRegister.F17),
                    AbiRegisterClass.Vector => ImmutableArray.Create(MachineRegister.V0, MachineRegister.V1, MachineRegister.V2, MachineRegister.V3, MachineRegister.V4, MachineRegister.V5, MachineRegister.V6, MachineRegister.V7),
                    _ => ImmutableArray.Create(MachineRegister.X10, MachineRegister.X11, MachineRegister.X12, MachineRegister.X13, MachineRegister.X14, MachineRegister.X15, MachineRegister.X16, MachineRegister.X17),
                };
            }

            if (!cursor.ForceStack && argumentRegisters.Length != 0)
            {
                if (usesUnifiedArgumentCursor)
                {
                    var slot = cursor.Unified++;
                    if (slot < argumentRegisters.Length)
                        return AbiLocation.FromRegister(argumentRegisters[slot], size, registerClass, stackOffset);
                }
                else if (registerClass == AbiRegisterClass.Floating)
                {
                    if (cursor.Float < argumentRegisters.Length)
                        return AbiLocation.FromRegister(argumentRegisters[cursor.Float++], size, registerClass, stackOffset);
                }
                else if (registerClass == AbiRegisterClass.Vector)
                {
                    if (cursor.Vector < argumentRegisters.Length)
                        return AbiLocation.FromRegister(argumentRegisters[cursor.Vector++], size, registerClass, stackOffset);
                }
                else
                {
                    var alignedIntegerCursor = AlignRegisterCursor(cursor.Integer, registerSlotAlignment);
                    if (alignedIntegerCursor + Math.Max(1, minimumRegisterSlots) <= argumentRegisters.Length)
                    {
                        cursor.Integer = alignedIntegerCursor;
                        return AbiLocation.FromRegister(argumentRegisters[cursor.Integer++], size, registerClass, stackOffset);
                    }
                }
            }

            if (usesUnifiedArgumentCursor)
                cursor.Stack = Math.Max(cursor.Stack, argumentRegisters.Length);

            cursor.Stack = AlignRegisterCursor(cursor.Stack, stackSlotAlignment);
            var stackSlot = cursor.Stack;
            var offset = PositiveModulo(stackOffset, stackSlotSize);
            cursor.Stack = checked(cursor.Stack + Math.Max(1, SlotsFor(offset + Math.Max(1, size), stackSlotSize)));
            if (forceStackAfterStack)
                cursor.ForceStack = true;
            return AbiLocation.FromStack(stackSlot, offset, size, Math.Min(Math.Max(1, size), stackSlotSize));
        }

        public static MachineRegister ReturnRegister(AbiSegment segment, int ordinal)
        {
            if (ordinal >= 0 && ordinal < segment.ReturnRegisters.Length)
                return segment.ReturnRegisters[ordinal];

            if (segment.RegisterClass == AbiRegisterClass.Floating)
                return (MachineRegister)((int)MachineRegister.F10 + ordinal);
            if (segment.RegisterClass == AbiRegisterClass.Vector)
                return (MachineRegister)((int)MachineRegister.V0 + ordinal);
            return (MachineRegister)((int)MachineRegister.X10 + ordinal);
        }

        public static bool RequiresHiddenReturnBuffer(TargetInfo target, QualifiedType returnType)
            => ClassifyValue(target, returnType, isReturn: true, isVariadicUnnamedArgument: false).PassingKind == AbiPassingKind.Indirect;

        public static AbiLocation AssignHiddenReturnBufferLocation(TargetInfo target, ref AbiCursor cursor, int stackSlotSize)
            => AssignSegmentArgumentLocation(CreateSegment(target, 0, target.PointerSize, AbiRegisterClass.General), ref cursor, stackSlotSize);

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
            var maxByte = TargetRegisterInfo.MinimumOutgoingArgumentAreaSize(target, stackSlotSize);
            if (instruction.Result is not null && RequiresHiddenReturnBuffer(target, instruction.Result.Type))
            {
                var hidden = AssignHiddenReturnBufferLocation(target, ref cursor, stackSlotSize);
                if (hidden.Kind == AbiLocationKind.Stack)
                    maxByte = Math.Max(maxByte, hidden.EndByte(stackSlotSize));
            }

            var signature = instruction.CallSignature;
            for (var i = startOperand; i < instruction.Operands.Length; i++)
            {
                var isVariadicUnnamed = IsVariadicUnnamedArgument(signature, i - startOperand);
                var value = ClassifyValue(target, instruction.Operands[i].Type, isReturn: false, isVariadicUnnamed);
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

            if (includeVariadicHomeArea && signature is not null && signature.IsVariadic)
            {
                var fixedCount = signature.Parameters.Length;
                var variadicCount = Math.Max(0, instruction.Operands.Length - 1 - fixedCount);
                var homeSlotSize = VariadicHomeSlotSize(target, stackSlotSize);
                maxByte = checked(AlignUp(Math.Max(maxByte, cursor.Stack * stackSlotSize), homeSlotSize) + variadicCount * homeSlotSize);
            }

            return AlignUp(maxByte, stackSlotSize);
        }

        public static int VariadicHomeSlotSize(TargetInfo target, int stackSlotSize)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));
            return target.IsRiscV || target.Architecture == TargetArchitectureKind.Arm64
                ? Math.Max(8, Math.Max(1, stackSlotSize))
                : Math.Max(1, stackSlotSize);
        }

        public static int RiscVAbiFloatingRegisterSize(TargetInfo target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            if (!target.IsRiscV)
                return 0;

            var features = target.ArchitectureFeatures;
            if ((features & TargetArchitectureFeatures.RiscVD) != 0)
                return 8;
            if ((features & TargetArchitectureFeatures.RiscVF) != 0)
                return 4;
            return 0;
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

        private static bool IsVariadicUnnamedArgument(FunctionType? signature, int zeroBasedArgumentIndex)
            => signature is not null && signature.IsVariadic && zeroBasedArgumentIndex >= signature.Parameters.Length;

        private static int AlignRegisterCursor(int value, int alignment)
        {
            if (alignment <= 1)
                return value;
            var remainder = value % alignment;
            return remainder == 0 ? value : checked(value + alignment - remainder);
        }

        private static int PositiveModulo(int value, int modulus)
        {
            if (modulus <= 1)
                return 0;
            var remainder = value % modulus;
            return remainder < 0 ? remainder + modulus : remainder;
        }

        private static bool IsVoid(QualifiedType type)
            => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Void };

        private static bool IsPointerLike(QualifiedType type)
            => type.Type.Kind is TypeKind.Pointer or TypeKind.Array or TypeKind.Function;

        private static bool IsIntegerLike(QualifiedType type)
            => (type.Type.Kind is TypeKind.Builtin or TypeKind.Enum) && !IsFloating(type) && !IsVoid(type);

        internal static bool IsFloating(QualifiedType type)
            => IsFloat32(type) || IsFloat64(type) || IsLongDouble(type);

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
        Vector,
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
        public int Vector;
        public int Stack;
        public int Unified;
        public bool ForceStack;
    }

    internal readonly struct AbiSegment
    {
        public int Offset { get; }
        public int Size { get; }
        public AbiRegisterClass RegisterClass { get; }
        public int RegisterSlotAlignment { get; }
        public int MinimumRegisterSlots { get; }
        public bool ForceStackAfterStack { get; }
        public int StackSlotAlignment { get; }
        public ImmutableArray<MachineRegister> ArgumentRegisters { get; }
        public ImmutableArray<MachineRegister> ReturnRegisters { get; }
        public bool UsesUnifiedArgumentCursor { get; }

        public AbiSegment(
            int offset,
            int size,
            AbiRegisterClass registerClass,
            int registerSlotAlignment = 1,
            int minimumRegisterSlots = 1,
            bool forceStackAfterStack = false,
            int stackSlotAlignment = 1,
            ImmutableArray<MachineRegister> argumentRegisters = default,
            ImmutableArray<MachineRegister> returnRegisters = default,
            bool usesUnifiedArgumentCursor = false)
        {
            Offset = offset < 0 ? 0 : offset;
            Size = size <= 0 ? 1 : size;
            RegisterClass = registerClass;
            RegisterSlotAlignment = registerSlotAlignment <= 1 ? 1 : registerSlotAlignment;
            MinimumRegisterSlots = minimumRegisterSlots <= 1 ? 1 : minimumRegisterSlots;
            ForceStackAfterStack = forceStackAfterStack;
            StackSlotAlignment = stackSlotAlignment <= 1 ? 1 : stackSlotAlignment;
            ArgumentRegisters = argumentRegisters.IsDefault ? ImmutableArray<MachineRegister>.Empty : argumentRegisters;
            ReturnRegisters = returnRegisters.IsDefault ? ImmutableArray<MachineRegister>.Empty : returnRegisters;
            UsesUnifiedArgumentCursor = usesUnifiedArgumentCursor;
        }
    }

    internal readonly struct AbiValue
    {
        public QualifiedType Type { get; }
        public AbiPassingKind PassingKind { get; }
        public int Size { get; }
        public int Alignment { get; }
        public ImmutableArray<AbiSegment> Segments { get; }
        public int IndirectSize { get; }

        public AbiValue(QualifiedType type, AbiPassingKind passingKind, int size, int alignment, ImmutableArray<AbiSegment> segments, int indirectSize = 0)
        {
            Type = type;
            PassingKind = passingKind;
            Size = Math.Max(0, size);
            Alignment = Math.Max(1, alignment);
            Segments = segments.IsDefault ? ImmutableArray<AbiSegment>.Empty : segments;
            IndirectSize = indirectSize <= 0 ? Math.Max(1, Size) : indirectSize;
        }

        public static AbiValue Void(QualifiedType type)
            => new AbiValue(type, AbiPassingKind.Void, 0, 1, ImmutableArray<AbiSegment>.Empty);

        public static AbiValue Scalar(QualifiedType type, AbiRegisterClass registerClass, int size, int alignment)
            => new AbiValue(type, AbiPassingKind.Scalar, size, alignment, ImmutableArray.Create(new AbiSegment(0, size, registerClass)));

        public static AbiValue MultiRegister(QualifiedType type, int size, int alignment, ImmutableArray<AbiSegment> segments)
            => new AbiValue(type, AbiPassingKind.MultiRegister, size, alignment, segments);

        public static AbiValue Stack(QualifiedType type, int size, int alignment)
            => new AbiValue(type, AbiPassingKind.Stack, size, alignment, ImmutableArray<AbiSegment>.Empty);

        public static AbiValue Indirect(QualifiedType type, int size, int alignment, int pointerSize)
            => new AbiValue(type, AbiPassingKind.Indirect, size, alignment, ImmutableArray<AbiSegment>.Empty, indirectSize: pointerSize);

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
