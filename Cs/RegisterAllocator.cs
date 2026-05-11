using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Cnidaria.Cs
{
    internal enum MachineRegister : byte
    {
        X0 = 0,
        X1 = 1,
        X2 = 2,
        X3 = 3,
        X4 = 4,
        X5 = 5,
        X6 = 6,
        X7 = 7,
        X8 = 8,
        X9 = 9,
        X10 = 10,
        X11 = 11,
        X12 = 12,
        X13 = 13,
        X14 = 14,
        X15 = 15,
        X16 = 16,
        X17 = 17,
        X18 = 18,
        X19 = 19,
        X20 = 20,
        X21 = 21,
        X22 = 22,
        X23 = 23,
        X24 = 24,
        X25 = 25,
        X26 = 26,
        X27 = 27,
        X28 = 28,
        X29 = 29,
        X30 = 30,
        X31 = 31,

        F0 = 32,
        F1 = 33,
        F2 = 34,
        F3 = 35,
        F4 = 36,
        F5 = 37,
        F6 = 38,
        F7 = 39,
        F8 = 40,
        F9 = 41,
        F10 = 42,
        F11 = 43,
        F12 = 44,
        F13 = 45,
        F14 = 46,
        F15 = 47,
        F16 = 48,
        F17 = 49,
        F18 = 50,
        F19 = 51,
        F20 = 52,
        F21 = 53,
        F22 = 54,
        F23 = 55,
        F24 = 56,
        F25 = 57,
        F26 = 58,
        F27 = 59,
        F28 = 60,
        F29 = 61,
        F30 = 62,
        F31 = 63,

        Invalid = 255,
    }

    internal static class MachineRegisters
    {
        public const MachineRegister Zero = MachineRegister.X0;
        public const MachineRegister ReturnAddress = MachineRegister.X1;
        public const MachineRegister StackPointer = MachineRegister.X2;
        public const MachineRegister GlobalPointer = MachineRegister.X3;
        public const MachineRegister ThreadPointer = MachineRegister.X4;
        public const MachineRegister FramePointer = MachineRegister.X8;
        public const MachineRegister ReturnValue0 = MachineRegister.X10;
        public const MachineRegister ReturnValue1 = MachineRegister.X11;
        public const MachineRegister ReturnValue2 = MachineRegister.X12;
        public const MachineRegister ReturnValue3 = MachineRegister.X13;
        public const MachineRegister FloatReturnValue0 = MachineRegister.F10;
        public const MachineRegister FloatReturnValue1 = MachineRegister.F11;
        public const MachineRegister FloatReturnValue2 = MachineRegister.F12;
        public const MachineRegister FloatReturnValue3 = MachineRegister.F13;
        public const MachineRegister ParallelCopyScratch0 = MachineRegister.X29;
        public const MachineRegister ParallelCopyScratch1 = MachineRegister.X30;
        public const MachineRegister BackendScratch = MachineRegister.X31;
        public const MachineRegister TreeScratch3 = MachineRegister.X28;
        public const MachineRegister FloatParallelCopyScratch0 = MachineRegister.F30;
        public const MachineRegister FloatParallelCopyScratch1 = MachineRegister.F31;
        public const MachineRegister FloatBackendScratch = MachineRegister.F29;
        public const MachineRegister FloatTreeScratch3 = MachineRegister.F28;

        public static ImmutableArray<MachineRegister> TreeScratchGprs { get; } = ImmutableArray.Create(
            BackendScratch,
            ParallelCopyScratch0,
            ParallelCopyScratch1,
            TreeScratch3);

        public static ImmutableArray<MachineRegister> TreeScratchFprs { get; } = ImmutableArray.Create(
            FloatBackendScratch,
            FloatParallelCopyScratch0,
            FloatParallelCopyScratch1,
            FloatTreeScratch3);

        public static ImmutableArray<MachineRegister> DefaultAllocatableGprs { get; } = ImmutableArray.Create(
            MachineRegister.X5,
            MachineRegister.X6,
            MachineRegister.X7,
            MachineRegister.X9,
            MachineRegister.X10,
            MachineRegister.X11,
            MachineRegister.X12,
            MachineRegister.X13,
            MachineRegister.X14,
            MachineRegister.X15,
            MachineRegister.X16,
            MachineRegister.X17,
            MachineRegister.X18,
            MachineRegister.X19,
            MachineRegister.X20,
            MachineRegister.X21,
            MachineRegister.X22,
            MachineRegister.X23,
            MachineRegister.X24,
            MachineRegister.X25,
            MachineRegister.X26,
            MachineRegister.X27);

        public static ImmutableArray<MachineRegister> DefaultAllocatableFprs { get; } = ImmutableArray.Create(
            MachineRegister.F0,
            MachineRegister.F1,
            MachineRegister.F2,
            MachineRegister.F3,
            MachineRegister.F4,
            MachineRegister.F5,
            MachineRegister.F6,
            MachineRegister.F7,
            MachineRegister.F8,
            MachineRegister.F9,
            MachineRegister.F10,
            MachineRegister.F11,
            MachineRegister.F12,
            MachineRegister.F13,
            MachineRegister.F14,
            MachineRegister.F15,
            MachineRegister.F16,
            MachineRegister.F17,
            MachineRegister.F18,
            MachineRegister.F19,
            MachineRegister.F20,
            MachineRegister.F21,
            MachineRegister.F22,
            MachineRegister.F23,
            MachineRegister.F24,
            MachineRegister.F25,
            MachineRegister.F26,
            MachineRegister.F27);

        public static ImmutableArray<MachineRegister> DefaultReservedGprs { get; } = ImmutableArray.Create(
            MachineRegister.X0,
            MachineRegister.X1,
            MachineRegister.X2,
            MachineRegister.X3,
            MachineRegister.X4,
            MachineRegister.X8,
            MachineRegister.X28,
            MachineRegister.X29,
            MachineRegister.X30,
            MachineRegister.X31);

        public static ImmutableArray<MachineRegister> DefaultReservedFprs { get; } = ImmutableArray.Create(
            MachineRegister.F28,
            MachineRegister.F29,
            MachineRegister.F30,
            MachineRegister.F31);

        public static ImmutableArray<MachineRegister> CalleeSavedGprs { get; } = ImmutableArray.Create(
            MachineRegister.X8,
            MachineRegister.X9,
            MachineRegister.X18,
            MachineRegister.X19,
            MachineRegister.X20,
            MachineRegister.X21,
            MachineRegister.X22,
            MachineRegister.X23,
            MachineRegister.X24,
            MachineRegister.X25,
            MachineRegister.X26,
            MachineRegister.X27);

        public static ImmutableArray<MachineRegister> CalleeSavedFprs { get; } = ImmutableArray.Create(
            MachineRegister.F8,
            MachineRegister.F9,
            MachineRegister.F18,
            MachineRegister.F19,
            MachineRegister.F20,
            MachineRegister.F21,
            MachineRegister.F22,
            MachineRegister.F23,
            MachineRegister.F24,
            MachineRegister.F25,
            MachineRegister.F26,
            MachineRegister.F27);

        public static ImmutableArray<MachineRegister> CallerSavedGprs { get; } = ImmutableArray.Create(
            MachineRegister.X1,
            MachineRegister.X5,
            MachineRegister.X6,
            MachineRegister.X7,
            MachineRegister.X10,
            MachineRegister.X11,
            MachineRegister.X12,
            MachineRegister.X13,
            MachineRegister.X14,
            MachineRegister.X15,
            MachineRegister.X16,
            MachineRegister.X17,
            MachineRegister.X28,
            MachineRegister.X29,
            MachineRegister.X30,
            MachineRegister.X31);

        public static ImmutableArray<MachineRegister> CallerSavedFprs { get; } = ImmutableArray.Create(
            MachineRegister.F0,
            MachineRegister.F1,
            MachineRegister.F2,
            MachineRegister.F3,
            MachineRegister.F4,
            MachineRegister.F5,
            MachineRegister.F6,
            MachineRegister.F7,
            MachineRegister.F10,
            MachineRegister.F11,
            MachineRegister.F12,
            MachineRegister.F13,
            MachineRegister.F14,
            MachineRegister.F15,
            MachineRegister.F16,
            MachineRegister.F17,
            MachineRegister.F28,
            MachineRegister.F29,
            MachineRegister.F30,
            MachineRegister.F31);

        public static ImmutableArray<MachineRegister> CallerSavedRegisters { get; } =
            CallerSavedGprs.AddRange(CallerSavedFprs);

        public static bool IsCalleeSaved(MachineRegister register)
        {
            var regs = GetClass(register) == RegisterClass.Float ? CalleeSavedFprs : CalleeSavedGprs;
            for (int i = 0; i < regs.Length; i++)
            {
                if (regs[i] == register)
                    return true;
            }
            return false;
        }

        public static bool IsCallerSaved(MachineRegister register)
            => GetClass(register) != RegisterClass.Invalid && !IsReserved(register) && !IsCalleeSaved(register);

        public static bool IsReserved(MachineRegister register)
        {
            var regs = GetClass(register) == RegisterClass.Float ? DefaultReservedFprs : DefaultReservedGprs;
            for (int i = 0; i < regs.Length; i++)
            {
                if (regs[i] == register)
                    return true;
            }
            return false;
        }

        public static ulong MaskOf(MachineRegister register)
        {
            if (register == MachineRegister.Invalid)
                return 0;

            int index = (int)register;
            if ((uint)index >= 64)
                throw new ArgumentOutOfRangeException(nameof(register));

            return 1UL << index;
        }

        public static ulong MaskOf(ImmutableArray<MachineRegister> registers)
        {
            ulong mask = 0;
            if (registers.IsDefault)
                return mask;

            for (int i = 0; i < registers.Length; i++)
                mask |= MaskOf(registers[i]);

            return mask;
        }

        public static ulong DefaultMaskForClass(RegisterClass registerClass)
        {
            return registerClass switch
            {
                RegisterClass.General => MaskOf(DefaultAllocatableGprs),
                RegisterClass.Float => MaskOf(DefaultAllocatableFprs),
                _ => 0,
            };
        }

        public static string FormatMask(ulong mask)
        {
            if (mask == 0)
                return "{}";

            var sb = new StringBuilder();
            sb.Append('{');
            bool first = true;
            for (int i = 0; i < 64; i++)
            {
                if ((mask & (1UL << i)) == 0)
                    continue;

                if (!first)
                    sb.Append(',');
                first = false;
                sb.Append(Format((MachineRegister)i));
            }
            sb.Append('}');
            return sb.ToString();
        }

        public static MachineRegister GetParallelCopyScratch(RegisterClass registerClass)
        {
            return registerClass switch
            {
                RegisterClass.General => ParallelCopyScratch0,
                RegisterClass.Float => FloatParallelCopyScratch0,
                _ => MachineRegister.Invalid,
            };
        }

        public static int RegisterSaveSize(MachineRegister register)
            => GetClass(register) == RegisterClass.Float ? TargetArchitecture.FloatingRegisterSize : TargetArchitecture.GeneralRegisterSize;

        public static int RegisterSaveAlignment(MachineRegister register)
            => GetClass(register) == RegisterClass.Float ? TargetArchitecture.FloatingRegisterSize : TargetArchitecture.GeneralRegisterSize;

        public static RegisterClass GetClass(MachineRegister register)
        {
            if (register >= MachineRegister.X0 && register <= MachineRegister.X31)
                return RegisterClass.General;
            if (register >= MachineRegister.F0 && register <= MachineRegister.F31)
                return RegisterClass.Float;
            return RegisterClass.Invalid;
        }

        public static bool IsRegisterInClass(MachineRegister register, RegisterClass registerClass)
            => GetClass(register) == registerClass;

        public static int IntegerArgumentRegisterCount => 8;
        public static int FloatArgumentRegisterCount => 8;

        public static MachineRegister GetIntegerArgumentRegister(int index)
        {
            return index switch
            {
                0 => MachineRegister.X10,
                1 => MachineRegister.X11,
                2 => MachineRegister.X12,
                3 => MachineRegister.X13,
                4 => MachineRegister.X14,
                5 => MachineRegister.X15,
                6 => MachineRegister.X16,
                7 => MachineRegister.X17,
                _ => MachineRegister.Invalid,
            };
        }

        public static MachineRegister GetFloatArgumentRegister(int index)
        {
            return index switch
            {
                0 => MachineRegister.F10,
                1 => MachineRegister.F11,
                2 => MachineRegister.F12,
                3 => MachineRegister.F13,
                4 => MachineRegister.F14,
                5 => MachineRegister.F15,
                6 => MachineRegister.F16,
                7 => MachineRegister.F17,
                _ => MachineRegister.Invalid,
            };
        }

        public static string ClassSuffix(RegisterClass registerClass)
        {
            return registerClass switch
            {
                RegisterClass.General => "g",
                RegisterClass.Float => "f",
                _ => "?",
            };
        }

        public static string Format(MachineRegister register)
        {
            return register switch
            {
                MachineRegister.X0 => "x0/zero",
                MachineRegister.X1 => "x1/ra",
                MachineRegister.X2 => "x2/sp",
                MachineRegister.X3 => "x3/gp",
                MachineRegister.X4 => "x4/tp",
                MachineRegister.X5 => "x5/t0",
                MachineRegister.X6 => "x6/t1",
                MachineRegister.X7 => "x7/t2",
                MachineRegister.X8 => "x8/fp",
                MachineRegister.X9 => "x9/s1",
                MachineRegister.X10 => "x10/a0",
                MachineRegister.X11 => "x11/a1",
                MachineRegister.X12 => "x12/a2",
                MachineRegister.X13 => "x13/a3",
                MachineRegister.X14 => "x14/a4",
                MachineRegister.X15 => "x15/a5",
                MachineRegister.X16 => "x16/a6",
                MachineRegister.X17 => "x17/a7",
                MachineRegister.X18 => "x18/s2",
                MachineRegister.X19 => "x19/s3",
                MachineRegister.X20 => "x20/s4",
                MachineRegister.X21 => "x21/s5",
                MachineRegister.X22 => "x22/s6",
                MachineRegister.X23 => "x23/s7",
                MachineRegister.X24 => "x24/s8",
                MachineRegister.X25 => "x25/s9",
                MachineRegister.X26 => "x26/s10",
                MachineRegister.X27 => "x27/s11",
                MachineRegister.X28 => "x28/t3",
                MachineRegister.X29 => "x29/t4",
                MachineRegister.X30 => "x30/t5",
                MachineRegister.X31 => "x31/t6",

                MachineRegister.F0 => "f0/ft0",
                MachineRegister.F1 => "f1/ft1",
                MachineRegister.F2 => "f2/ft2",
                MachineRegister.F3 => "f3/ft3",
                MachineRegister.F4 => "f4/ft4",
                MachineRegister.F5 => "f5/ft5",
                MachineRegister.F6 => "f6/ft6",
                MachineRegister.F7 => "f7/ft7",
                MachineRegister.F8 => "f8/fs0",
                MachineRegister.F9 => "f9/fs1",
                MachineRegister.F10 => "f10/fa0",
                MachineRegister.F11 => "f11/fa1",
                MachineRegister.F12 => "f12/fa2",
                MachineRegister.F13 => "f13/fa3",
                MachineRegister.F14 => "f14/fa4",
                MachineRegister.F15 => "f15/fa5",
                MachineRegister.F16 => "f16/fa6",
                MachineRegister.F17 => "f17/fa7",
                MachineRegister.F18 => "f18/fs2",
                MachineRegister.F19 => "f19/fs3",
                MachineRegister.F20 => "f20/fs4",
                MachineRegister.F21 => "f21/fs5",
                MachineRegister.F22 => "f22/fs6",
                MachineRegister.F23 => "f23/fs7",
                MachineRegister.F24 => "f24/fs8",
                MachineRegister.F25 => "f25/fs9",
                MachineRegister.F26 => "f26/fs10",
                MachineRegister.F27 => "f27/fs11",
                MachineRegister.F28 => "f28/ft8",
                MachineRegister.F29 => "f29/ft9",
                MachineRegister.F30 => "f30/ft10",
                MachineRegister.F31 => "f31/ft11",
                _ => "<invalid-reg>",
            };
        }
    }


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

            if (stackKind is GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ptr)
                return Scalar(RegisterClass.General, TargetArchitecture.PointerSize, TargetArchitecture.PointerSize, containsGcPointers: false);

            if (stackKind is GenStackKind.Ref or GenStackKind.Null)
                return Scalar(RegisterClass.General, TargetArchitecture.PointerSize, TargetArchitecture.PointerSize, containsGcPointers: true);

            if (stackKind == GenStackKind.ByRef)
                return Scalar(RegisterClass.General, TargetArchitecture.PointerSize, TargetArchitecture.PointerSize, containsGcPointers: true);

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
            bool valueTypeNewObject = isNewObject && callee?.DeclaringType.IsValueType == true;
            bool referenceTypeNewObject = isNewObject && !valueTypeNewObject && callee?.HasThis == true;

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
                if (returnAbi.PassingKind == AbiValuePassingKind.Indirect || valueTypeNewObject)
                {
                    needsHiddenReturnBuffer = true;
                    hiddenReturnBufferValue = resultValue;
                }
            }

            int hiddenReturnBufferInsertionIndex = needsHiddenReturnBuffer
                ? (valueTypeNewObject ? 0 : (callee is null ? 0 : HiddenReturnBufferInsertionIndex(callee, arguments.Length)))
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

    internal enum RegisterOperandKind : byte
    {
        None,
        Register,
        SpillSlot,
        IncomingArgumentSlot,
        LocalSlot,
        TempSlot,
        OutgoingArgumentSlot,
        FrameSlot,
    }

    internal enum StackFrameSlotKind : byte
    {
        Invalid,
        Argument,
        Local,
        Temp,
        Spill,
        ParallelCopyScratch,
        ReturnAddress,
        CalleeSavedRegister,
        OutgoingArgument,
    }

    internal enum RegisterFrameBase : byte
    {
        None,
        StackPointer,
        FramePointer,
        IncomingArgumentBase,
    }

    internal readonly struct RegisterOperand : IEquatable<RegisterOperand>
    {
        public readonly RegisterOperandKind Kind;
        public readonly RegisterClass RegisterClass;
        public readonly MachineRegister Register;
        public readonly int SpillSlot;
        public readonly StackFrameSlotKind FrameSlotKind;
        public readonly RegisterFrameBase FrameBase;
        public readonly int FrameSlotIndex;
        public readonly int FrameOffset;
        public readonly int FrameSlotSize;
        public readonly bool IsAddress;

        private RegisterOperand(
            RegisterOperandKind kind,
            RegisterClass registerClass,
            MachineRegister register,
            int spillSlot,
            StackFrameSlotKind frameSlotKind,
            RegisterFrameBase frameBase,
            int frameSlotIndex,
            int frameOffset,
            int frameSlotSize,
            bool isAddress)
        {
            Kind = kind;
            RegisterClass = registerClass;
            Register = register;
            SpillSlot = spillSlot;
            FrameSlotKind = frameSlotKind;
            FrameBase = frameBase;
            FrameSlotIndex = frameSlotIndex;
            FrameOffset = frameOffset;
            FrameSlotSize = frameSlotSize;
            IsAddress = isAddress;
        }

        public static RegisterOperand None => new RegisterOperand(
            RegisterOperandKind.None,
            RegisterClass.Invalid,
            MachineRegister.Invalid,
            -1,
            StackFrameSlotKind.Invalid,
            RegisterFrameBase.None,
            -1,
            0,
            0,
            isAddress: false);

        public static RegisterOperand ForRegister(MachineRegister register)
        {
            var registerClass = MachineRegisters.GetClass(register);
            if (registerClass == RegisterClass.Invalid)
                throw new ArgumentOutOfRangeException(nameof(register));
            return new RegisterOperand(
                RegisterOperandKind.Register,
                registerClass,
                register,
                -1,
                StackFrameSlotKind.Invalid,
                RegisterFrameBase.None,
                -1,
                0,
                0,
                isAddress: false);
        }

        public static RegisterOperand ForSpillSlot(RegisterClass registerClass, int slot)
            => ForSpillSlot(registerClass, slot, offset: 0, size: 0);

        public static RegisterOperand ForSpillSlot(RegisterClass registerClass, int slot, int offset, int size)
        {
            if (registerClass == RegisterClass.Invalid)
                throw new ArgumentOutOfRangeException(nameof(registerClass));
            if (slot < 0)
                throw new ArgumentOutOfRangeException(nameof(slot));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            return new RegisterOperand(
                RegisterOperandKind.SpillSlot,
                registerClass,
                MachineRegister.Invalid,
                slot,
                StackFrameSlotKind.Invalid,
                RegisterFrameBase.None,
                -1,
                offset,
                size,
                isAddress: false);
        }

        public static RegisterOperand ForIncomingArgumentSlot(RegisterClass registerClass, int slot)
            => ForIncomingArgumentSlot(registerClass, slot, offset: 0, size: 0);

        public static RegisterOperand ForIncomingArgumentSlot(RegisterClass registerClass, int slot, int offset, int size)
            => ForUnresolvedFrameSlot(RegisterOperandKind.IncomingArgumentSlot, registerClass, StackFrameSlotKind.Argument, slot, offset, size);

        public static RegisterOperand ForLocalSlot(RegisterClass registerClass, int slot)
            => ForLocalSlot(registerClass, slot, offset: 0, size: 0);

        public static RegisterOperand ForLocalSlot(RegisterClass registerClass, int slot, int offset, int size)
            => ForUnresolvedFrameSlot(RegisterOperandKind.LocalSlot, registerClass, StackFrameSlotKind.Local, slot, offset, size);

        public static RegisterOperand ForTempSlot(RegisterClass registerClass, int slot)
            => ForTempSlot(registerClass, slot, offset: 0, size: 0);

        public static RegisterOperand ForTempSlot(RegisterClass registerClass, int slot, int offset, int size)
            => ForUnresolvedFrameSlot(RegisterOperandKind.TempSlot, registerClass, StackFrameSlotKind.Temp, slot, offset, size);

        public static RegisterOperand ForOutgoingArgumentSlot(RegisterClass registerClass, int slot)
            => ForOutgoingArgumentSlot(registerClass, slot, offset: 0, size: 0);

        public static RegisterOperand ForOutgoingArgumentSlot(RegisterClass registerClass, int slot, int offset, int size)
            => ForUnresolvedFrameSlot(RegisterOperandKind.OutgoingArgumentSlot, registerClass, StackFrameSlotKind.OutgoingArgument, slot, offset, size);

        private static RegisterOperand ForUnresolvedFrameSlot(
            RegisterOperandKind kind,
            RegisterClass registerClass,
            StackFrameSlotKind frameSlotKind,
            int slot,
            int offset,
            int size)
        {
            if (registerClass == RegisterClass.Invalid)
                throw new ArgumentOutOfRangeException(nameof(registerClass));
            if (slot < 0)
                throw new ArgumentOutOfRangeException(nameof(slot));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            return new RegisterOperand(
                kind,
                registerClass,
                MachineRegister.Invalid,
                -1,
                frameSlotKind,
                RegisterFrameBase.None,
                slot,
                offset,
                size,
                isAddress: false);
        }

        public static RegisterOperand ForFrameSlot(
            RegisterClass registerClass,
            StackFrameSlotKind frameSlotKind,
            int frameSlotIndex,
            int frameOffset,
            int frameSlotSize,
            bool isAddress = false)
            => ForFrameSlot(
                registerClass,
                frameSlotKind,
                RegisterFrameBase.FramePointer,
                frameSlotIndex,
                frameOffset,
                frameSlotSize,
                isAddress);

        public static RegisterOperand ForFrameSlot(
            RegisterClass registerClass,
            StackFrameSlotKind frameSlotKind,
            RegisterFrameBase frameBase,
            int frameSlotIndex,
            int frameOffset,
            int frameSlotSize,
            bool isAddress = false)
        {
            if (registerClass == RegisterClass.Invalid)
                throw new ArgumentOutOfRangeException(nameof(registerClass));
            if (frameSlotKind == StackFrameSlotKind.Invalid)
                throw new ArgumentOutOfRangeException(nameof(frameSlotKind));
            if (frameBase is not (RegisterFrameBase.StackPointer or RegisterFrameBase.FramePointer or RegisterFrameBase.IncomingArgumentBase))
                throw new ArgumentOutOfRangeException(nameof(frameBase));
            if (frameSlotIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(frameSlotIndex));
            if (frameOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(frameOffset));
            if (frameSlotSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(frameSlotSize));

            return new RegisterOperand(
                RegisterOperandKind.FrameSlot,
                registerClass,
                MachineRegister.Invalid,
                -1,
                frameSlotKind,
                frameBase,
                frameSlotIndex,
                frameOffset,
                frameSlotSize,
                isAddress);
        }

        public bool IsNone => Kind == RegisterOperandKind.None;
        public bool IsRegister => Kind == RegisterOperandKind.Register;
        public bool IsSpillSlot => Kind == RegisterOperandKind.SpillSlot;
        public bool IsIncomingArgumentSlot => Kind == RegisterOperandKind.IncomingArgumentSlot;
        public bool IsLocalSlot => Kind == RegisterOperandKind.LocalSlot;
        public bool IsTempSlot => Kind == RegisterOperandKind.TempSlot;
        public bool IsOutgoingArgumentSlot => Kind == RegisterOperandKind.OutgoingArgumentSlot;
        public bool IsUnresolvedFrameSlot => IsIncomingArgumentSlot || IsLocalSlot || IsTempSlot || IsOutgoingArgumentSlot;
        public bool IsFrameSlot => Kind == RegisterOperandKind.FrameSlot;
        public bool IsMemoryOperand => IsSpillSlot || IsUnresolvedFrameSlot || IsFrameSlot;

        public RegisterOperand AsAddress()
        {
            if (IsAddress)
                return this;
            if (!IsMemoryOperand)
                throw new InvalidOperationException("Only stack or frame memory operands can be addressed: " + this + ".");

            return new RegisterOperand(
                Kind,
                RegisterClass.General,
                MachineRegister.Invalid,
                SpillSlot,
                FrameSlotKind,
                FrameBase,
                FrameSlotIndex,
                FrameOffset,
                FrameSlotSize,
                isAddress: true);
        }

        public bool Equals(RegisterOperand other)
            => Kind == other.Kind &&
               RegisterClass == other.RegisterClass &&
               Register == other.Register &&
               SpillSlot == other.SpillSlot &&
               FrameSlotKind == other.FrameSlotKind &&
               FrameBase == other.FrameBase &&
               FrameSlotIndex == other.FrameSlotIndex &&
               FrameOffset == other.FrameOffset &&
               FrameSlotSize == other.FrameSlotSize &&
               IsAddress == other.IsAddress;

        public override bool Equals(object? obj) => obj is RegisterOperand other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = (hash * 397) ^ (int)RegisterClass;
                hash = (hash * 397) ^ (int)Register;
                hash = (hash * 397) ^ SpillSlot;
                hash = (hash * 397) ^ (int)FrameSlotKind;
                hash = (hash * 397) ^ (int)FrameBase;
                hash = (hash * 397) ^ FrameSlotIndex;
                hash = (hash * 397) ^ FrameOffset;
                hash = (hash * 397) ^ FrameSlotSize;
                hash = (hash * 397) ^ (IsAddress ? 1 : 0);
                return hash;
            }
        }

        public override string ToString()
        {
            string text = Kind switch
            {
                RegisterOperandKind.None => "_",
                RegisterOperandKind.Register => MachineRegisters.Format(Register),
                RegisterOperandKind.SpillSlot => $"spill.{MachineRegisters.ClassSuffix(RegisterClass)}[{SpillSlot}]" +
                (FrameOffset != 0 || FrameSlotSize != 0 ? "+" + FrameOffset.ToString() + ":" + FrameSlotSize.ToString() : string.Empty),
                RegisterOperandKind.IncomingArgumentSlot => FormatUnresolvedFrameSlot("inarg"),
                RegisterOperandKind.LocalSlot => FormatUnresolvedFrameSlot("local"),
                RegisterOperandKind.TempSlot => FormatUnresolvedFrameSlot("temp"),
                RegisterOperandKind.OutgoingArgumentSlot => FormatUnresolvedFrameSlot("outarg"),
                RegisterOperandKind.FrameSlot => "frame." + FrameSlotKind.ToString().ToLowerInvariant() +
                    "[" + FrameSlotIndex.ToString() + "]@" + FrameBaseName(FrameBase) + "+" + FrameOffset.ToString() + ":" + FrameSlotSize.ToString(),
                _ => "?",
            };

            return IsAddress ? "&" + text : text;
        }

        private string FormatUnresolvedFrameSlot(string prefix)
        {
            var text = prefix + "." + MachineRegisters.ClassSuffix(RegisterClass) + "[" + FrameSlotIndex.ToString() + "]";
            if (FrameOffset != 0 || FrameSlotSize != 0)
                text += "+" + FrameOffset.ToString() + ":" + FrameSlotSize.ToString();
            return text;
        }

        private static string FrameBaseName(RegisterFrameBase frameBase)
        {
            return frameBase switch
            {
                RegisterFrameBase.StackPointer => "sp",
                RegisterFrameBase.FramePointer => "fp",
                RegisterFrameBase.IncomingArgumentBase => "inarg",
                _ => "?",
            };
        }
    }

    internal sealed class StackFrameSlot
    {
        public StackFrameSlotKind Kind { get; }
        public int Index { get; }
        public int Offset { get; }
        public int Size { get; }
        public int Alignment { get; }
        public RegisterClass RegisterClass { get; }
        public RuntimeType? Type { get; }
        public MachineRegister SavedRegister { get; }

        public StackFrameSlot(
            StackFrameSlotKind kind,
            int index,
            int offset,
            int size,
            int alignment,
            RegisterClass registerClass = RegisterClass.Invalid,
            RuntimeType? type = null,
            MachineRegister savedRegister = MachineRegister.Invalid)
        {
            if (kind == StackFrameSlotKind.Invalid)
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            if (alignment <= 0)
                throw new ArgumentOutOfRangeException(nameof(alignment));

            Kind = kind;
            Index = index;
            Offset = offset;
            Size = size;
            Alignment = alignment;
            RegisterClass = registerClass;
            Type = type;
            SavedRegister = savedRegister;
        }

        public int EndOffset => Offset + Size;
        public override string ToString() => Kind + "[" + Index + "] +" + Offset + " size=" + Size;
    }

    internal enum RegisterStackFrameModel : byte
    {
        Leaf,
        RootFrame,
        SharedRootFrameWithFunclets,
    }

    internal sealed class StackFrameLayout
    {
        public static StackFrameLayout Empty { get; } = new StackFrameLayout(
            frameSize: 0,
            frameAlignment: 1,
            calleeSaveAreaOffset: 0,
            calleeSaveAreaSize: 0,
            argumentHomeAreaOffset: 0,
            argumentHomeAreaSize: 0,
            localAreaOffset: 0,
            localAreaSize: 0,
            tempAreaOffset: 0,
            tempAreaSize: 0,
            spillAreaOffset: 0,
            spillAreaSize: 0,
            outgoingArgumentAreaOffset: 0,
            outgoingArgumentAreaSize: 0,
            argumentSlots: ImmutableArray<StackFrameSlot>.Empty,
            localSlots: ImmutableArray<StackFrameSlot>.Empty,
            tempSlots: ImmutableArray<StackFrameSlot>.Empty,
            spillSlots: ImmutableArray<StackFrameSlot>.Empty,
            calleeSavedSlots: ImmutableArray<StackFrameSlot>.Empty,
            outgoingArgumentSlots: ImmutableArray<StackFrameSlot>.Empty,
            usesFramePointer: false,
            frameModel: RegisterStackFrameModel.Leaf);

        public int FrameSize { get; }
        public int FrameAlignment { get; }
        public bool UsesFramePointer { get; }
        public RegisterStackFrameModel FrameModel { get; }
        public int CalleeSaveAreaOffset { get; }
        public int CalleeSaveAreaSize { get; }
        public int ArgumentHomeAreaOffset { get; }
        public int ArgumentHomeAreaSize { get; }
        public int LocalAreaOffset { get; }
        public int LocalAreaSize { get; }
        public int TempAreaOffset { get; }
        public int TempAreaSize { get; }
        public int SpillAreaOffset { get; }
        public int SpillAreaSize { get; }
        public int OutgoingArgumentAreaOffset { get; }
        public int OutgoingArgumentAreaSize { get; }
        public ImmutableArray<StackFrameSlot> ArgumentSlots { get; }
        public ImmutableArray<StackFrameSlot> LocalSlots { get; }
        public ImmutableArray<StackFrameSlot> TempSlots { get; }
        public ImmutableArray<StackFrameSlot> SpillSlots { get; }
        public ImmutableArray<StackFrameSlot> CalleeSavedSlots { get; }
        public ImmutableArray<StackFrameSlot> OutgoingArgumentSlots { get; }
        public IReadOnlyDictionary<int, StackFrameSlot> SpillSlotByIndex { get; }

        public bool IsEmpty => FrameSize == 0 &&
            ArgumentSlots.Length == 0 &&
            LocalSlots.Length == 0 &&
            TempSlots.Length == 0 &&
            SpillSlots.Length == 0 &&
            CalleeSavedSlots.Length == 0 &&
            OutgoingArgumentSlots.Length == 0;

        public StackFrameLayout(
            int frameSize,
            int frameAlignment,
            int calleeSaveAreaOffset,
            int calleeSaveAreaSize,
            int argumentHomeAreaOffset,
            int argumentHomeAreaSize,
            int localAreaOffset,
            int localAreaSize,
            int tempAreaOffset,
            int tempAreaSize,
            int spillAreaOffset,
            int spillAreaSize,
            int outgoingArgumentAreaOffset,
            int outgoingArgumentAreaSize,
            ImmutableArray<StackFrameSlot> argumentSlots,
            ImmutableArray<StackFrameSlot> localSlots,
            ImmutableArray<StackFrameSlot> tempSlots,
            ImmutableArray<StackFrameSlot> spillSlots,
            ImmutableArray<StackFrameSlot> calleeSavedSlots,
            ImmutableArray<StackFrameSlot> outgoingArgumentSlots,
            bool usesFramePointer = false,
            RegisterStackFrameModel frameModel = RegisterStackFrameModel.Leaf)
        {
            if (frameSize < 0)
                throw new ArgumentOutOfRangeException(nameof(frameSize));
            if (frameAlignment <= 0)
                throw new ArgumentOutOfRangeException(nameof(frameAlignment));

            FrameSize = frameSize;
            FrameAlignment = frameAlignment;
            UsesFramePointer = usesFramePointer;
            FrameModel = frameModel;
            CalleeSaveAreaOffset = calleeSaveAreaOffset;
            CalleeSaveAreaSize = calleeSaveAreaSize;
            ArgumentHomeAreaOffset = argumentHomeAreaOffset;
            ArgumentHomeAreaSize = argumentHomeAreaSize;
            LocalAreaOffset = localAreaOffset;
            LocalAreaSize = localAreaSize;
            TempAreaOffset = tempAreaOffset;
            TempAreaSize = tempAreaSize;
            SpillAreaOffset = spillAreaOffset;
            SpillAreaSize = spillAreaSize;
            OutgoingArgumentAreaOffset = outgoingArgumentAreaOffset;
            OutgoingArgumentAreaSize = outgoingArgumentAreaSize;
            ArgumentSlots = argumentSlots.IsDefault ? ImmutableArray<StackFrameSlot>.Empty : argumentSlots;
            LocalSlots = localSlots.IsDefault ? ImmutableArray<StackFrameSlot>.Empty : localSlots;
            TempSlots = tempSlots.IsDefault ? ImmutableArray<StackFrameSlot>.Empty : tempSlots;
            SpillSlots = spillSlots.IsDefault ? ImmutableArray<StackFrameSlot>.Empty : spillSlots;
            CalleeSavedSlots = calleeSavedSlots.IsDefault ? ImmutableArray<StackFrameSlot>.Empty : calleeSavedSlots;
            OutgoingArgumentSlots = outgoingArgumentSlots.IsDefault ? ImmutableArray<StackFrameSlot>.Empty : outgoingArgumentSlots;

            var spillMap = new Dictionary<int, StackFrameSlot>();
            for (int i = 0; i < SpillSlots.Length; i++)
                spillMap[SpillSlots[i].Index] = SpillSlots[i];
            SpillSlotByIndex = spillMap;
        }

        public StackFrameSlot GetSpillSlot(int spillSlot)
        {
            if (!SpillSlotByIndex.TryGetValue(spillSlot, out var slot))
                throw new InvalidOperationException("Missing finalized stack slot for spill slot " + spillSlot + ".");
            return slot;
        }

        public bool TryGetArgumentSlot(int index, out StackFrameSlot slot)
            => TryGetIndexedSlot(ArgumentSlots, index, out slot);

        public bool TryGetLocalSlot(int index, out StackFrameSlot slot)
            => TryGetIndexedSlot(LocalSlots, index, out slot);

        public bool TryGetTempSlot(int index, out StackFrameSlot slot)
            => TryGetIndexedSlot(TempSlots, index, out slot);

        public bool TryGetCalleeSavedSlot(MachineRegister register, out StackFrameSlot slot)
        {
            for (int i = 0; i < CalleeSavedSlots.Length; i++)
            {
                if (CalleeSavedSlots[i].SavedRegister == register)
                {
                    slot = CalleeSavedSlots[i];
                    return true;
                }
            }

            slot = null!;
            return false;
        }

        public bool TryGetOutgoingArgumentSlot(int index, out StackFrameSlot slot)
            => TryGetIndexedSlot(OutgoingArgumentSlots, index, out slot);

        private static bool TryGetIndexedSlot(ImmutableArray<StackFrameSlot> slots, int index, out StackFrameSlot slot)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Index == index)
                {
                    slot = slots[i];
                    return true;
                }
            }

            slot = null!;
            return false;
        }
    }

    internal sealed class RegisterStackLayoutOptions
    {
        public static RegisterStackLayoutOptions Default => new RegisterStackLayoutOptions();

        public int FrameAlignment { get; set; } = 16;
        public bool HomeIncomingArguments { get; set; } = true;
        public bool AllocateLocalSlots { get; set; } = true;
        public bool AllocateTempSlots { get; set; } = true;
        public bool SaveUsedCalleeSavedRegisters { get; set; } = true;

        public bool SaveFramePointerWhenFrameIsUsed { get; set; } = true;

        public bool UseFramePointerForFunclets { get; set; } = true;

        public bool SaveReturnAddressForNonLeafMethods { get; set; } = true;
        public bool SaveReturnAddressForLeafMethods { get; set; } = true;

        public int OutgoingArgumentSlotCount { get; set; }
    }

    internal sealed class RegisterAllocatorOptions
    {
        public static RegisterAllocatorOptions Default => new RegisterAllocatorOptions();

        public ImmutableArray<MachineRegister> AllocatableGeneralRegisters { get; set; } = MachineRegisters.DefaultAllocatableGprs;
        public ImmutableArray<MachineRegister> AllocatableFloatRegisters { get; set; } = MachineRegisters.DefaultAllocatableFprs;
        public bool PreferCopySourceRegister { get; set; } = true;
        public bool RespectCallClobbers { get; set; } = true;
        public bool FinalizeStackLayout { get; set; } = true;
        public bool GeneratePrologEpilog { get; set; } = true;
        public bool BuildGcInfo { get; set; } = true;

        public bool Validate { get; set; } = true;
        public RegisterStackLayoutOptions StackLayoutOptions { get; set; } = RegisterStackLayoutOptions.Default;

        public MachineRegister ParallelCopyScratchRegister0 { get; set; } = MachineRegisters.ParallelCopyScratch0;
        public MachineRegister ParallelCopyScratchRegister1 { get; set; } = MachineRegisters.ParallelCopyScratch1;
        public MachineRegister ParallelCopyFloatScratchRegister0 { get; set; } = MachineRegisters.FloatParallelCopyScratch0;
        public MachineRegister ParallelCopyFloatScratchRegister1 { get; set; } = MachineRegisters.FloatParallelCopyScratch1;

        public ImmutableArray<MachineRegister> GetAllocatableRegisters(RegisterClass registerClass)
        {
            return registerClass switch
            {
                RegisterClass.General => AllocatableGeneralRegisters,
                RegisterClass.Float => AllocatableFloatRegisters,
                _ => ImmutableArray<MachineRegister>.Empty,
            };
        }
    }


    internal enum OperandRole : byte
    {
        Normal,
        HiddenReturnBuffer,
    }

    internal enum MoveKind : byte
    {
        None,
        Register,
        Load,
        Store,
        MemoryToMemory,
        LoadAddress,
        StoreAddress,
    }

    [Flags]
    internal enum MoveFlags : ushort
    {
        None = 0,
        Reload = 1 << 0,
        Spill = 1 << 1,
        Split = 1 << 2,
        ParallelCopy = 1 << 3,
        AbiArgument = 1 << 4,
        AbiReturn = 1 << 5,
        HiddenReturnBuffer = 1 << 6,
        Internal = 1 << 7,
    }

    internal enum FrameOperation : byte
    {
        None,
        AllocateFrame,
        SaveReturnAddress,
        SaveCalleeSavedRegister,
        EstablishFramePointer,
        EnterFuncletFrame,
        LeaveFuncletFrame,
        RestoreStackPointerFromFramePointer,
        RestoreCalleeSavedRegister,
        RestoreReturnAddress,
        FreeFrame,
    }

    internal enum RegisterUnwindCodeKind : byte
    {
        None,
        AllocateStack,
        SaveReturnAddress,
        SaveCalleeSavedRegister,
        SetFramePointer,
    }

    internal readonly struct RegisterUnwindCode
    {
        public readonly int NodeId;
        public readonly int BlockId;
        public readonly int Ordinal;
        public readonly RegisterUnwindCodeKind Kind;
        public readonly MachineRegister Register;
        public readonly int StackOffset;
        public readonly int Size;

        public RegisterUnwindCode(
            int nodeId,
            int blockId,
            int ordinal,
            RegisterUnwindCodeKind kind,
            MachineRegister register,
            int stackOffset,
            int size)
        {
            if (nodeId < 0)
                throw new ArgumentOutOfRangeException(nameof(nodeId));
            if (blockId < 0)
                throw new ArgumentOutOfRangeException(nameof(blockId));
            if (ordinal < 0)
                throw new ArgumentOutOfRangeException(nameof(ordinal));
            if (kind == RegisterUnwindCodeKind.None)
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (stackOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(stackOffset));
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size));

            NodeId = nodeId;
            BlockId = blockId;
            Ordinal = ordinal;
            Kind = kind;
            Register = register;
            StackOffset = stackOffset;
            Size = size;
        }

        public override string ToString()
            => Kind + " id=" + NodeId.ToString() + " B" + BlockId.ToString() + ":" + Ordinal.ToString() +
               (Register == MachineRegister.Invalid ? string.Empty : " " + MachineRegisters.Format(Register)) +
               (Size == 0 ? string.Empty : " stack+" + StackOffset.ToString() + ":" + Size.ToString());
    }

    internal enum RegisterGcRootKind : byte
    {
        ObjectReference,
        ByRef,
        InteriorPointer,
    }

    internal readonly struct RegisterGcLiveRoot : IEquatable<RegisterGcLiveRoot>
    {
        public readonly GenTree Value;
        public readonly RegisterGcRootKind RootKind;
        public readonly RegisterOperand Location;
        public readonly int Offset;
        public readonly RuntimeType? Type;
        public readonly bool RequiresValueInfo;

        public RegisterGcLiveRoot(
            GenTree value,
            RegisterGcRootKind rootKind,
            RegisterOperand location,
            RuntimeType? type,
            int offset = 0,
            bool requiresValueInfo = true)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));
            if (location.IsNone)
                throw new ArgumentOutOfRangeException(nameof(location));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));

            Value = value;
            RootKind = rootKind;
            Location = location;
            Offset = offset;
            Type = type;
            RequiresValueInfo = requiresValueInfo;
        }

        public bool Equals(RegisterGcLiveRoot other)
            => ReferenceEquals(Value, other.Value) &&
               RootKind == other.RootKind &&
               Location.Equals(other.Location) &&
               Offset == other.Offset &&
               RequiresValueInfo == other.RequiresValueInfo;

        public override bool Equals(object? obj) => obj is RegisterGcLiveRoot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Value);
                hash = (hash * 397) ^ (int)RootKind;
                hash = (hash * 397) ^ Location.GetHashCode();
                hash = (hash * 397) ^ Offset;
                hash = (hash * 397) ^ RequiresValueInfo.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
            => RootKind + " " + Value + " @ " + Location +
               (Offset == 0 ? string.Empty : "+" + Offset.ToString()) +
               (RequiresValueInfo ? string.Empty : " home");
    }

    [Flags]
    internal enum RegisterGcLiveRangeFlags : ushort
    {
        None = 0,
        Pinned = 1 << 0,
        ReportOnlyInLeafFunclet = 1 << 1,
        SharedWithParentFrame = 1 << 2,
    }

    internal enum RegisterFuncletKind : byte
    {
        Root,
        Catch,
        Finally,
        Fault,
        Filter,
    }

    internal sealed class RegisterFunclet
    {
        public int Index { get; }
        public RegisterFuncletKind Kind { get; }
        public int ExceptionRegionIndex { get; }
        public int ParentFuncletIndex { get; }
        public int EntryBlockId { get; }
        public ImmutableArray<int> BlockIds { get; }

        public RegisterFunclet(
            int index,
            RegisterFuncletKind kind,
            int exceptionRegionIndex,
            int parentFuncletIndex,
            int entryBlockId,
            ImmutableArray<int> blockIds)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (entryBlockId < 0) throw new ArgumentOutOfRangeException(nameof(entryBlockId));

            Index = index;
            Kind = kind;
            ExceptionRegionIndex = exceptionRegionIndex;
            ParentFuncletIndex = parentFuncletIndex;
            EntryBlockId = entryBlockId;
            BlockIds = blockIds.IsDefault ? ImmutableArray<int>.Empty : blockIds;
        }

        public bool IsRoot => Kind == RegisterFuncletKind.Root;

        public static ImmutableArray<RegisterFunclet> Build(GenTreeMethod method)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));

            var cfg = method.Cfg;
            var result = ImmutableArray.CreateBuilder<RegisterFunclet>();
            var rootBlocks = ImmutableArray.CreateBuilder<int>();
            for (int b = 0; b < cfg.Blocks.Length; b++)
            {
                if (!cfg.Blocks[b].IsFuncletBlock)
                    rootBlocks.Add(b);
            }

            result.Add(new RegisterFunclet(
                index: 0,
                RegisterFuncletKind.Root,
                exceptionRegionIndex: -1,
                parentFuncletIndex: -1,
                entryBlockId: 0,
                blockIds: rootBlocks.ToImmutable()));

            var regionToFunclet = new Dictionary<int, int>();
            for (int r = 0; r < cfg.ExceptionRegions.Length; r++)
            {
                var region = cfg.ExceptionRegions[r];
                int index = result.Count;
                regionToFunclet[region.Index] = index;
                var blocks = ImmutableArray.CreateBuilder<int>();
                for (int b = region.HandlerStartBlockId; b < region.HandlerEndBlockIdExclusive && b < cfg.Blocks.Length; b++)
                {
                    if (!IsOwnedByNestedHandler(cfg.ExceptionRegions, region.Index, b))
                        blocks.Add(b);
                }

                result.Add(new RegisterFunclet(
                    index,
                    ToFuncletKind(region.Kind),
                    region.Index,
                    parentFuncletIndex: 0,
                    entryBlockId: region.HandlerStartBlockId,
                    blockIds: blocks.ToImmutable()));
            }

            if (cfg.ExceptionRegions.Length == 0)
                return result.ToImmutable();

            var fixedResult = ImmutableArray.CreateBuilder<RegisterFunclet>(result.Count);
            fixedResult.Add(result[0]);
            for (int i = 1; i < result.Count; i++)
            {
                var funclet = result[i];
                var region = cfg.ExceptionRegions[funclet.ExceptionRegionIndex];
                int parent = 0;
                if (region.ParentIndex >= 0 && regionToFunclet.TryGetValue(region.ParentIndex, out int mappedParent))
                    parent = mappedParent;

                fixedResult.Add(new RegisterFunclet(
                    funclet.Index,
                    funclet.Kind,
                    funclet.ExceptionRegionIndex,
                    parent,
                    funclet.EntryBlockId,
                    funclet.BlockIds));
            }

            return fixedResult.ToImmutable();
        }

        private static bool IsOwnedByNestedHandler(ImmutableArray<CfgExceptionRegion> regions, int regionIndex, int blockId)
        {
            for (int i = 0; i < regions.Length; i++)
            {
                var candidate = regions[i];
                if (candidate.Index == regionIndex || candidate.ParentIndex != regionIndex)
                    continue;

                if (candidate.ContainsHandlerBlock(blockId))
                    return true;
            }

            return false;
        }

        private static RegisterFuncletKind ToFuncletKind(CfgExceptionRegionKind kind)
        {
            return kind switch
            {
                CfgExceptionRegionKind.Catch => RegisterFuncletKind.Catch,
                CfgExceptionRegionKind.Finally => RegisterFuncletKind.Finally,
                CfgExceptionRegionKind.Fault => RegisterFuncletKind.Fault,
                CfgExceptionRegionKind.Filter => RegisterFuncletKind.Filter,
                _ => RegisterFuncletKind.Catch,
            };
        }
    }

    internal enum RegisterFrameRegionKind : byte
    {
        Prolog,
        Epilog,
    }

    internal sealed class RegisterFrameRegion
    {
        public RegisterFrameRegionKind Kind { get; }
        public int FuncletIndex { get; }
        public int BlockId { get; }
        public int FirstNodeId { get; }
        public int LastNodeId { get; }

        public RegisterFrameRegion(RegisterFrameRegionKind kind, int funcletIndex, int blockId, int firstNodeId, int lastNodeId)
        {
            if (funcletIndex < 0) throw new ArgumentOutOfRangeException(nameof(funcletIndex));
            if (blockId < 0) throw new ArgumentOutOfRangeException(nameof(blockId));
            if (firstNodeId < 0) throw new ArgumentOutOfRangeException(nameof(firstNodeId));
            if (lastNodeId < firstNodeId) throw new ArgumentOutOfRangeException(nameof(lastNodeId));

            Kind = kind;
            FuncletIndex = funcletIndex;
            BlockId = blockId;
            FirstNodeId = firstNodeId;
            LastNodeId = lastNodeId;
        }
    }

    internal enum RegisterGcTransitionKind : byte
    {
        Enter,
        Move,
        Exit,
    }

    internal enum RegisterGcInterruptibleRangeKind : byte
    {
        Call,
        Poll,
        FullyInterruptible,
    }

    internal readonly struct RegisterGcLiveRange
    {
        public readonly RegisterGcLiveRoot Root;
        public readonly int StartPosition;
        public readonly int EndPosition;
        public readonly int FuncletIndex;
        public readonly RegisterGcLiveRangeFlags Flags;

        public RegisterGcLiveRange(
            RegisterGcLiveRoot root,
            int startPosition,
            int endPosition,
            int funcletIndex,
            RegisterGcLiveRangeFlags flags)
        {
            if (startPosition < 0) throw new ArgumentOutOfRangeException(nameof(startPosition));
            if (endPosition < startPosition) throw new ArgumentOutOfRangeException(nameof(endPosition));
            if (funcletIndex < 0) throw new ArgumentOutOfRangeException(nameof(funcletIndex));

            Root = root;
            StartPosition = startPosition;
            EndPosition = endPosition;
            FuncletIndex = funcletIndex;
            Flags = flags;
        }

        public bool IsEmpty => StartPosition == EndPosition;
        public override string ToString()
            => Root + " F" + FuncletIndex.ToString() + " [" + StartPosition.ToString() + ", " + EndPosition.ToString() + ")" +
               (Flags == RegisterGcLiveRangeFlags.None ? string.Empty : " " + Flags.ToString());
    }

    internal readonly struct RegisterGcTransition
    {
        public readonly int Position;
        public readonly RegisterGcTransitionKind Kind;
        public readonly RegisterGcLiveRoot? Before;
        public readonly RegisterGcLiveRoot? After;

        public RegisterGcTransition(int position, RegisterGcTransitionKind kind, RegisterGcLiveRoot? before, RegisterGcLiveRoot? after)
        {
            if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
            if (kind == RegisterGcTransitionKind.Enter && after is null)
                throw new ArgumentNullException(nameof(after));
            if (kind == RegisterGcTransitionKind.Exit && before is null)
                throw new ArgumentNullException(nameof(before));

            Position = position;
            Kind = kind;
            Before = before;
            After = after;
        }
    }

    internal sealed class RegisterGcInterruptibleRange
    {
        public RegisterGcInterruptibleRangeKind Kind { get; }
        public int StartPosition { get; }
        public int EndPosition { get; }
        public int FuncletIndex { get; }
        public int FirstNodeId { get; }
        public int LastNodeId { get; }

        public RegisterGcInterruptibleRange(
            RegisterGcInterruptibleRangeKind kind,
            int startPosition,
            int endPosition,
            int funcletIndex,
            int firstNodeId,
            int lastNodeId)
        {
            if (startPosition < 0) throw new ArgumentOutOfRangeException(nameof(startPosition));
            if (endPosition <= startPosition) throw new ArgumentOutOfRangeException(nameof(endPosition));
            if (funcletIndex < 0) throw new ArgumentOutOfRangeException(nameof(funcletIndex));
            if (firstNodeId < 0) throw new ArgumentOutOfRangeException(nameof(firstNodeId));
            if (lastNodeId < firstNodeId) throw new ArgumentOutOfRangeException(nameof(lastNodeId));

            Kind = kind;
            StartPosition = startPosition;
            EndPosition = endPosition;
            FuncletIndex = funcletIndex;
            FirstNodeId = firstNodeId;
            LastNodeId = lastNodeId;
        }

        public override string ToString()
            => Kind + " F" + FuncletIndex.ToString() + " [" + StartPosition.ToString() + ", " + EndPosition.ToString() + ")";
    }

    internal static class GenTreeLirFactory
    {
        private static ImmutableArray<RegisterOperand> OneResult(RegisterOperand result)
            => result.IsNone ? ImmutableArray<RegisterOperand>.Empty : ImmutableArray.Create(result);

        private static ImmutableArray<OperandRole> NormalizeUseRoles(
            ImmutableArray<RegisterOperand> uses,
            ImmutableArray<OperandRole> useRoles)
        {
            uses = uses.IsDefault ? ImmutableArray<RegisterOperand>.Empty : uses;
            if (uses.Length == 0)
                return ImmutableArray<OperandRole>.Empty;
            if (useRoles.IsDefaultOrEmpty)
                return ImmutableArray.CreateRange(new OperandRole[uses.Length]);
            if (useRoles.Length != uses.Length)
                throw new ArgumentException("Use role count must match register use count.", nameof(useRoles));
            return useRoles;
        }

        private static MoveFlags InferMoveFlags(RegisterOperand destination, RegisterOperand source)
        {
            if (source.IsAddress)
                return MoveFlags.None;

            MoveFlags flags = MoveFlags.None;
            if (source.IsMemoryOperand && destination.IsRegister)
                flags |= MoveFlags.Reload;
            if (source.IsRegister && destination.IsMemoryOperand)
                flags |= MoveFlags.Spill;
            if (source.IsMemoryOperand && destination.IsMemoryOperand)
                flags |= MoveFlags.Reload | MoveFlags.Spill;
            return flags;
        }

        private static GenTree Attach(
            GenTree node,
            int id,
            int blockId,
            int ordinal,
            ImmutableArray<RegisterOperand> results,
            ImmutableArray<RegisterOperand> uses,
            ImmutableArray<GenTree> linearResults,
            ImmutableArray<GenTree> linearUses,
            ImmutableArray<LirOperandFlags> linearOperands,
            int linearId,
            FrameOperation frameOperation,
            int immediate,
            string? comment,
            ImmutableArray<OperandRole> useRoles = default,
            MoveFlags moveFlags = MoveFlags.None,
            int phiCopyFromBlockId = -1,
            int phiCopyToBlockId = -1)
        {
            results = results.IsDefault ? ImmutableArray<RegisterOperand>.Empty : results;
            uses = uses.IsDefault ? ImmutableArray<RegisterOperand>.Empty : uses;
            linearResults = linearResults.IsDefault ? ImmutableArray<GenTree>.Empty : linearResults;
            linearUses = linearUses.IsDefault ? ImmutableArray<GenTree>.Empty : linearUses;

            node.RegisterResults = linearResults;
            node.RegisterResult = linearResults.Length == 1 ? linearResults[0] : null;
            node.RegisterUses = linearUses;
            node.OperandFlags = linearOperands.IsDefault ? ImmutableArray<LirOperandFlags>.Empty : linearOperands;
            node.LinearId = linearId >= 0 ? linearId : id;
            node.LinearBlockId = blockId;
            node.LinearOrdinal = ordinal;
            node.LinearKind = node.Kind switch
            {
                GenTreeKind.Copy or GenTreeKind.Reload or GenTreeKind.Spill => GenTreeLinearKind.Copy,
                GenTreeKind.GcPoll => GenTreeLinearKind.GcPoll,
                _ => GenTreeLinearKind.Tree,
            };
            node.LinearPhiCopyFromBlockId = phiCopyFromBlockId;
            node.LinearPhiCopyToBlockId = phiCopyToBlockId;

            node.AttachLsraInfo(new GenTreeLsraInfo
            {
                GtRegNum = results.Length == 1 && results[0].IsRegister ? results[0].Register : MachineRegister.Invalid,
                Home = results.Length == 1 ? results[0] : RegisterOperand.None,
                CodegenResults = results,
                CodegenUses = uses,
                CodegenUseRoles = NormalizeUseRoles(uses, useRoles),
                CodegenResultValues = BuildValueKeys(linearResults),
                CodegenUseValues = BuildValueKeys(linearUses),
                MoveFlags = moveFlags,
                FrameOperation = frameOperation,
                Immediate = immediate,
                Comment = comment,
            });
            return node;
        }

        private static ImmutableArray<GenTreeValueKey> BuildValueKeys(ImmutableArray<GenTree> values)
        {
            if (values.IsDefaultOrEmpty)
                return ImmutableArray<GenTreeValueKey>.Empty;
            var result = ImmutableArray.CreateBuilder<GenTreeValueKey>(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                result.Add(value.LinearValueKey);
            }
            return result.ToImmutable();
        }

        private static GenTree SyntheticNode(int id, GenTreeKind kind, GenStackKind stackKind = GenStackKind.Void, RuntimeType? type = null)
        {
            return new GenTree(
                id,
                kind,
                pc: -1,
                BytecodeOp.Nop,
                type: type,
                stackKind: stackKind,
                flags: GenTreeFlags.SideEffect | GenTreeFlags.Ordered,
                operands: ImmutableArray<GenTree>.Empty);
        }

        public static GenTree Tree(
            int id,
            int blockId,
            int ordinal,
            GenTree source,
            RegisterOperand result,
            ImmutableArray<RegisterOperand> uses,
            GenTree? linearResult,
            ImmutableArray<GenTree> linearUses,
            int linearId = -1,
            ImmutableArray<OperandRole> useRoles = default,
            ImmutableArray<LirOperandFlags> linearOperands = default)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            var linearResults = result.IsNone
                ? ImmutableArray<GenTree>.Empty
                : linearResult is not null
                    ? ImmutableArray.Create(linearResult)
                    : ImmutableArray<GenTree>.Empty;

            return Attach(
                source,
                id,
                blockId,
                ordinal,
                OneResult(result),
                uses,
                linearResults,
                linearUses,
                linearOperands,
                linearId,
                FrameOperation.None,
                immediate: 0,
                comment: null,
                useRoles: useRoles);
        }

        public static GenTree TreeMulti(
            int id,
            int blockId,
            int ordinal,
            GenTree source,
            ImmutableArray<RegisterOperand> results,
            ImmutableArray<RegisterOperand> uses,
            ImmutableArray<GenTree> linearResults,
            ImmutableArray<GenTree> linearUses,
            int linearId = -1,
            ImmutableArray<OperandRole> useRoles = default,
            ImmutableArray<LirOperandFlags> linearOperands = default)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            results = results.IsDefault ? ImmutableArray<RegisterOperand>.Empty : results;
            linearResults = linearResults.IsDefault ? ImmutableArray<GenTree>.Empty : linearResults;
            if (linearResults.Length != 0 && linearResults.Length != results.Length)
                throw new ArgumentException("Multi-register GenTree LIR node must carry one GenTree result per result fragment.", nameof(linearResults));

            return Attach(
                source,
                id,
                blockId,
                ordinal,
                results,
                uses,
                linearResults,
                linearUses,
                linearOperands,
                linearId,
                FrameOperation.None,
                immediate: 0,
                comment: null,
                useRoles: useRoles);
        }

        public static GenTree Move(
            int id,
            int blockId,
            int ordinal,
            RegisterOperand destination,
            RegisterOperand source,
            GenTree? destinationValue,
            GenTree? sourceValue,
            string? comment = null,
            MoveFlags moveFlags = MoveFlags.None,
            int phiCopyFromBlockId = -1,
            int phiCopyToBlockId = -1)
        {
            moveFlags |= InferMoveFlags(destination, source);
            var kind = (moveFlags & MoveFlags.Spill) != 0
                ? GenTreeKind.Spill
                : (moveFlags & MoveFlags.Reload) != 0
                    ? GenTreeKind.Reload
                    : GenTreeKind.Copy;
            var node = SyntheticNode(id, kind, destinationValue?.StackKind ?? sourceValue?.StackKind ?? GenStackKind.Void, destinationValue?.Type ?? sourceValue?.Type);
            return Attach(
                node,
                id,
                blockId,
                ordinal,
                OneResult(destination),
                ImmutableArray.Create(source),
                destinationValue is not null ? ImmutableArray.Create(destinationValue) : ImmutableArray<GenTree>.Empty,
                sourceValue is not null ? ImmutableArray.Create(sourceValue) : ImmutableArray<GenTree>.Empty,
                ImmutableArray<LirOperandFlags>.Empty,
                linearId: id,
                FrameOperation.None,
                immediate: 0,
                comment: comment,
                moveFlags: moveFlags,
                phiCopyFromBlockId: phiCopyFromBlockId,
                phiCopyToBlockId: phiCopyToBlockId);
        }

        public static GenTree Frame(
            int id,
            int blockId,
            int ordinal,
            FrameOperation operation,
            RegisterOperand result,
            ImmutableArray<RegisterOperand> uses,
            int immediate,
            string? comment = null)
        {
            if (operation == FrameOperation.None)
                throw new ArgumentOutOfRangeException(nameof(operation));
            if (immediate < 0)
                throw new ArgumentOutOfRangeException(nameof(immediate));

            var node = SyntheticNode(id, GenTreeKind.StackFrameOp);
            return Attach(
                node,
                id,
                blockId,
                ordinal,
                OneResult(result),
                uses,
                ImmutableArray<GenTree>.Empty,
                ImmutableArray<GenTree>.Empty,
                ImmutableArray<LirOperandFlags>.Empty,
                linearId: id,
                operation,
                immediate,
                comment);
        }

        public static GenTree GcPoll(
            int id,
            int blockId,
            int ordinal,
            int linearId,
            GenTree? source = null,
            string? comment = null)
        {
            if (linearId < 0)
                throw new ArgumentOutOfRangeException(nameof(linearId));

            var node = SyntheticNode(id, GenTreeKind.GcPoll);
            return Attach(
                node,
                id,
                blockId,
                ordinal,
                ImmutableArray<RegisterOperand>.Empty,
                ImmutableArray<RegisterOperand>.Empty,
                ImmutableArray<GenTree>.Empty,
                ImmutableArray<GenTree>.Empty,
                ImmutableArray<LirOperandFlags>.Empty,
                linearId,
                FrameOperation.None,
                immediate: 0,
                comment: comment);
        }
    }

    internal static class GenTreeLirNodeExtensions
    {
        public static GenTree WithOperands(
            this GenTree node,
            ImmutableArray<RegisterOperand> results,
            ImmutableArray<RegisterOperand> uses,
            ImmutableArray<GenTree> linearResults,
            ImmutableArray<GenTree> linearUses)
        {
            var roles = node.Uses.Length == (uses.IsDefault ? 0 : uses.Length)
                ? node.UseRoles
                : default;
            return node.WithOperands(results, uses, linearResults, linearUses, roles);
        }

        public static GenTree WithOperands(
            this GenTree node,
            ImmutableArray<RegisterOperand> results,
            ImmutableArray<RegisterOperand> uses,
            ImmutableArray<GenTree> linearResults,
            ImmutableArray<GenTree> linearUses,
            ImmutableArray<OperandRole> useRoles)
        {
            results = results.IsDefault ? ImmutableArray<RegisterOperand>.Empty : results;
            uses = uses.IsDefault ? ImmutableArray<RegisterOperand>.Empty : uses;
            linearResults = linearResults.IsDefault ? ImmutableArray<GenTree>.Empty : linearResults;
            linearUses = linearUses.IsDefault ? ImmutableArray<GenTree>.Empty : linearUses;

            node.RegisterResults = linearResults;
            node.RegisterResult = linearResults.Length == 1 ? linearResults[0] : null;
            node.RegisterUses = linearUses;
            node.AttachLsraInfo(new GenTreeLsraInfo
            {
                GtRegNum = results.Length == 1 && results[0].IsRegister ? results[0].Register : MachineRegister.Invalid,
                Home = results.Length == 1 ? results[0] : RegisterOperand.None,
                CodegenResults = results,
                CodegenUses = uses,
                CodegenUseRoles = NormalizeUseRoles(uses, useRoles),
                CodegenResultValues = BuildValueKeys(linearResults),
                CodegenUseValues = BuildValueKeys(linearUses),
                InternalRegisters = node.LsraInfo.InternalRegisters,
                MoveFlags = node.MoveFlags,
                FrameOperation = node.FrameOperation,
                Immediate = node.Immediate,
                Comment = node.Comment,
                Flags = node.LsraFlags,
                LocationAtDefinition = node.LsraInfo.LocationAtDefinition,
            });
            return node;
        }

        public static GenTree WithOrdinal(this GenTree node, int ordinal)
        {
            node.LinearOrdinal = ordinal;
            return node;
        }

        public static GenTree WithPlacement(this GenTree node, int blockId, int ordinal)
        {
            node.LinearBlockId = blockId;
            node.LinearOrdinal = ordinal;
            return node;
        }

        private static ImmutableArray<OperandRole> NormalizeUseRoles(ImmutableArray<RegisterOperand> uses, ImmutableArray<OperandRole> useRoles)
        {
            if (uses.Length == 0)
                return ImmutableArray<OperandRole>.Empty;
            if (useRoles.IsDefaultOrEmpty)
                return ImmutableArray.CreateRange(new OperandRole[uses.Length]);
            if (useRoles.Length != uses.Length)
                throw new InvalidOperationException("Codegen use role count does not match use operand count.");
            return useRoles;
        }

        private static ImmutableArray<GenTreeValueKey> BuildValueKeys(ImmutableArray<GenTree> values)
        {
            if (values.IsDefaultOrEmpty)
                return ImmutableArray<GenTreeValueKey>.Empty;
            var result = ImmutableArray.CreateBuilder<GenTreeValueKey>(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                result.Add(value.LinearValueKey);
            }
            return result.ToImmutable();
        }
    }

    internal sealed class RegisterAllocationSegment
    {
        public int Start { get; }
        public int End { get; }
        public RegisterOperand Location { get; }

        public RegisterAllocationSegment(int start, int end, RegisterOperand location)
        {
            if (end < start)
                throw new ArgumentOutOfRangeException(nameof(end));
            if (location.IsNone && end != start)
                throw new ArgumentOutOfRangeException(nameof(location));

            Start = start;
            End = end;
            Location = location;
        }

        public bool IsEmpty => Start == End;
        public bool Contains(int position) => Start <= position && position < End;
        public bool Intersects(int position, int end) => Start < end && position < End;
        public override string ToString() => "[" + Start + ", " + End + ") " + Location;
    }

    internal sealed class RegisterAllocationFragment
    {
        public int SegmentIndex { get; }
        public AbiRegisterSegment AbiSegment { get; }
        public RegisterOperand Home { get; }
        public ImmutableArray<RegisterAllocationSegment> Segments { get; }

        public RegisterAllocationFragment(
            int segmentIndex,
            AbiRegisterSegment abiSegment,
            RegisterOperand home,
            ImmutableArray<RegisterAllocationSegment> segments)
        {
            if (segmentIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(segmentIndex));
            if (home.IsNone && !segments.IsDefaultOrEmpty)
                throw new ArgumentOutOfRangeException(nameof(home));

            SegmentIndex = segmentIndex;
            AbiSegment = abiSegment;
            Home = home;
            Segments = segments.IsDefault ? ImmutableArray<RegisterAllocationSegment>.Empty : NormalizeSegments(segments);
        }

        public RegisterOperand LocationAt(int position)
        {
            if (Home.IsNone || Segments.Length == 0)
                return Home;

            for (int i = 0; i < Segments.Length; i++)
            {
                var segment = Segments[i];
                if (segment.Contains(position))
                    return segment.Location;
            }

            for (int i = Segments.Length - 1; i >= 0; i--)
            {
                if (position >= Segments[i].End)
                    return Segments[i].Location;
            }

            return Segments[0].Location;
        }

        private static ImmutableArray<RegisterAllocationSegment> NormalizeSegments(ImmutableArray<RegisterAllocationSegment> source)
        {
            if (source.Length <= 1)
                return source;

            var list = new List<RegisterAllocationSegment>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                if (!source[i].IsEmpty)
                    list.Add(source[i]);
            }

            list.Sort(static (a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));
            if (list.Count == 0)
                return ImmutableArray<RegisterAllocationSegment>.Empty;

            var merged = ImmutableArray.CreateBuilder<RegisterAllocationSegment>(list.Count);
            var current = list[0];
            for (int i = 1; i < list.Count; i++)
            {
                var next = list[i];
                if (current.Location.Equals(next.Location) && next.Start <= current.End)
                {
                    current = new RegisterAllocationSegment(current.Start, Math.Max(current.End, next.End), current.Location);
                    continue;
                }

                merged.Add(current);
                current = next;
            }

            merged.Add(current);
            return merged.ToImmutable();
        }
    }

    internal readonly struct RegisterValueLocation
    {
        public readonly GenTree Value;
        public readonly AbiValuePassingKind PassingKind;
        public readonly RegisterOperand Scalar;
        public readonly ImmutableArray<RegisterOperand> Fragments;

        public RegisterValueLocation(
            GenTree value,
            AbiValuePassingKind passingKind,
            RegisterOperand scalar,
            ImmutableArray<RegisterOperand> fragments = default)
        {
            Value = value;
            PassingKind = passingKind;
            Scalar = scalar;
            Fragments = fragments.IsDefault ? ImmutableArray<RegisterOperand>.Empty : fragments;
        }

        public bool IsEmpty => Scalar.IsNone && Fragments.Length == 0;
        public bool IsScalar => !Scalar.IsNone && Fragments.Length == 0;
        public bool IsFragmented => Fragments.Length != 0;
        public int Count => Fragments.Length == 0 ? (Scalar.IsNone ? 0 : 1) : Fragments.Length;

        public RegisterOperand this[int index]
        {
            get
            {
                if (Fragments.Length != 0)
                    return Fragments[index];
                if (index == 0 && !Scalar.IsNone)
                    return Scalar;
                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override string ToString()
        {
            if (IsEmpty)
                return Value + " <none>";
            if (Fragments.Length == 0)
                return Value + " " + Scalar;

            var sb = new StringBuilder();
            sb.Append(Value).Append(" {");
            for (int i = 0; i < Fragments.Length; i++)
            {
                if (i != 0)
                    sb.Append(", ");
                sb.Append(i).Append(':').Append(Fragments[i]);
            }
            sb.Append('}');
            return sb.ToString();
        }
    }

    internal sealed class RegisterAllocationInfo
    {
        public GenTree Value { get; }
        public GenTreeValueKey ValueKey { get; }
        public RegisterOperand Home { get; }
        public ImmutableArray<LinearLiveRange> Ranges { get; }
        public ImmutableArray<int> UsePositions { get; }
        public int DefinitionPosition { get; }
        public ImmutableArray<RegisterAllocationSegment> Segments { get; }
        public ImmutableArray<RegisterAllocationFragment> Fragments { get; }

        public RegisterAllocationInfo(
            GenTree value,
            RegisterOperand home,
            ImmutableArray<LinearLiveRange> ranges,
            ImmutableArray<int> usePositions,
            int definitionPosition,
            ImmutableArray<RegisterAllocationSegment> segments = default,
            ImmutableArray<RegisterAllocationFragment> fragments = default)
        {
            Value = value;
            ValueKey = value.LinearValueKey;
            Home = home;
            Ranges = ranges.IsDefault ? ImmutableArray<LinearLiveRange>.Empty : ranges;
            UsePositions = usePositions.IsDefault ? ImmutableArray<int>.Empty : usePositions;
            DefinitionPosition = definitionPosition;
            Segments = segments.IsDefaultOrEmpty ? BuildDefaultSegments(home, Ranges) : NormalizeSegments(segments);
            Fragments = fragments.IsDefaultOrEmpty ? ImmutableArray<RegisterAllocationFragment>.Empty : NormalizeFragments(fragments);
        }

        public RegisterOperand LocationAt(int position)
        {
            if (Home.IsNone || Segments.Length == 0)
                return Home;

            for (int i = 0; i < Segments.Length; i++)
            {
                var segment = Segments[i];
                if (segment.Contains(position))
                    return segment.Location;
            }

            if (position == DefinitionPosition)
                return Segments[0].Location;

            for (int i = Segments.Length - 1; i >= 0; i--)
            {
                if (position >= Segments[i].End)
                    return Segments[i].Location;
            }

            return Segments[0].Location;
        }

        public RegisterOperand LocationAtDefinition()
            => LocationAt(DefinitionPosition);

        public RegisterOperand FragmentLocationAt(int position, int abiSegmentIndex)
        {
            if (abiSegmentIndex < 0)
                return LocationAt(position);

            for (int i = 0; i < Fragments.Length; i++)
            {
                if (Fragments[i].SegmentIndex == abiSegmentIndex)
                    return Fragments[i].LocationAt(position);
            }

            return LocationAt(position);
        }

        public RegisterValueLocation ValueLocationAt(int position, AbiValueInfo abi)
        {
            var scalar = LocationAt(position);
            if (scalar.IsNone || abi.PassingKind == AbiValuePassingKind.Void)
                return new RegisterValueLocation(Value, abi.PassingKind, RegisterOperand.None);

            if (abi.PassingKind != AbiValuePassingKind.MultiRegister)
                return new RegisterValueLocation(Value, abi.PassingKind, scalar);

            var abiSegments = MachineAbi.GetRegisterSegments(abi);
            var fragments = ImmutableArray.CreateBuilder<RegisterOperand>(abiSegments.Length);
            for (int i = 0; i < abiSegments.Length; i++)
            {
                if (TryGetAllocatedFragment(i, out var fragment))
                    fragments.Add(fragment.LocationAt(position));
                else
                    fragments.Add(OperandAtOffset(scalar, abiSegments[i]));
            }

            return new RegisterValueLocation(Value, abi.PassingKind, RegisterOperand.None, fragments.ToImmutable());
        }

        public RegisterValueLocation ValueLocationAtDefinition(AbiValueInfo abi)
            => ValueLocationAt(DefinitionPosition, abi);

        private bool TryGetAllocatedFragment(int segmentIndex, out RegisterAllocationFragment fragment)
        {
            for (int i = 0; i < Fragments.Length; i++)
            {
                if (Fragments[i].SegmentIndex == segmentIndex)
                {
                    fragment = Fragments[i];
                    return true;
                }
            }

            fragment = null!;
            return false;
        }

        private static RegisterOperand OperandAtOffset(RegisterOperand operand, AbiRegisterSegment segment)
        {
            if (operand.IsRegister)
            {
                if (segment.Offset != 0)
                    throw new InvalidOperationException("Cannot address a non-zero ABI segment inside register allocation " + operand + ".");
                if (!MachineRegisters.IsRegisterInClass(operand.Register, segment.RegisterClass))
                    throw new InvalidOperationException("Register allocation class does not match ABI segment class: " + operand + " vs " + segment + ".");
                return operand;
            }

            int offset = checked(operand.FrameOffset + segment.Offset);
            int size = segment.Size;

            if (operand.IsSpillSlot)
                return RegisterOperand.ForSpillSlot(segment.RegisterClass, operand.SpillSlot, offset, size);
            if (operand.IsIncomingArgumentSlot)
                return RegisterOperand.ForIncomingArgumentSlot(segment.RegisterClass, operand.FrameSlotIndex, offset, size);
            if (operand.IsLocalSlot)
                return RegisterOperand.ForLocalSlot(segment.RegisterClass, operand.FrameSlotIndex, offset, size);
            if (operand.IsTempSlot)
                return RegisterOperand.ForTempSlot(segment.RegisterClass, operand.FrameSlotIndex, offset, size);
            if (operand.IsOutgoingArgumentSlot)
                return RegisterOperand.ForOutgoingArgumentSlot(segment.RegisterClass, operand.FrameSlotIndex, offset, size);
            if (operand.IsFrameSlot)
                return RegisterOperand.ForFrameSlot(segment.RegisterClass, operand.FrameSlotKind, operand.FrameBase, operand.FrameSlotIndex, offset, size, operand.IsAddress);

            throw new InvalidOperationException("Cannot address an ABI segment inside allocation operand: " + operand + ".");
        }

        private static ImmutableArray<RegisterAllocationSegment> BuildDefaultSegments(
            RegisterOperand home,
            ImmutableArray<LinearLiveRange> ranges)
        {
            if (home.IsNone || ranges.Length == 0)
                return ImmutableArray<RegisterAllocationSegment>.Empty;

            var builder = ImmutableArray.CreateBuilder<RegisterAllocationSegment>(ranges.Length);
            for (int i = 0; i < ranges.Length; i++)
                builder.Add(new RegisterAllocationSegment(ranges[i].Start, ranges[i].End, home));
            return builder.ToImmutable();
        }

        private static ImmutableArray<RegisterAllocationSegment> NormalizeSegments(ImmutableArray<RegisterAllocationSegment> source)
        {
            if (source.Length <= 1)
                return source;

            var list = new List<RegisterAllocationSegment>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                if (!source[i].IsEmpty)
                    list.Add(source[i]);
            }

            list.Sort(static (a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));
            if (list.Count == 0)
                return ImmutableArray<RegisterAllocationSegment>.Empty;

            var merged = ImmutableArray.CreateBuilder<RegisterAllocationSegment>(list.Count);
            var current = list[0];
            for (int i = 1; i < list.Count; i++)
            {
                var next = list[i];
                if (current.Location.Equals(next.Location) && next.Start <= current.End)
                {
                    int end = next.End > current.End ? next.End : current.End;
                    current = new RegisterAllocationSegment(current.Start, end, current.Location);
                    continue;
                }

                merged.Add(current);
                current = next;
            }
            merged.Add(current);
            return merged.ToImmutable();
        }

        private static ImmutableArray<RegisterAllocationFragment> NormalizeFragments(ImmutableArray<RegisterAllocationFragment> source)
        {
            if (source.Length <= 1)
                return source;

            var list = new List<RegisterAllocationFragment>(source.Length);
            for (int i = 0; i < source.Length; i++)
                list.Add(source[i]);

            list.Sort(static (a, b) => a.SegmentIndex.CompareTo(b.SegmentIndex));

            for (int i = 1; i < list.Count; i++)
            {
                if (list[i - 1].SegmentIndex == list[i].SegmentIndex)
                    throw new InvalidOperationException("Duplicate register allocation fragment " + list[i].SegmentIndex + ".");
            }

            return list.ToImmutableArray();
        }
    }

    internal sealed class RegisterAllocatedMethod
    {
        public GenTreeMethod GenTreeMethod { get; }
        public ImmutableArray<GenTreeBlock> Blocks { get; }
        public ImmutableArray<GenTree> LinearNodes { get; }
        public ImmutableArray<RegisterAllocationInfo> Allocations { get; }
        public IReadOnlyDictionary<GenTree, RegisterAllocationInfo> AllocationByNode { get; }
        public IReadOnlyDictionary<int, ImmutableArray<GenTreeInternalRegister>> InternalRegistersByNodeId { get; }
        public IReadOnlyDictionary<int, int> LsraNodePositions { get; }
        public ImmutableArray<int> LsraBlockStartPositions { get; }
        public ImmutableArray<int> LsraBlockEndPositions { get; }
        public int SpillSlotCount { get; }
        public int ParallelCopyScratchSpillSlot { get; }
        public StackFrameLayout StackFrame { get; }
        public bool HasPrologEpilog { get; }
        public ImmutableArray<RegisterUnwindCode> UnwindCodes { get; }
        public ImmutableArray<RegisterGcLiveRange> GcLiveRanges { get; }
        public ImmutableArray<RegisterGcTransition> GcTransitions { get; }
        public ImmutableArray<RegisterGcInterruptibleRange> GcInterruptibleRanges { get; }
        public ImmutableArray<RegisterFunclet> Funclets { get; }
        public ImmutableArray<RegisterFrameRegion> FrameRegions { get; }
        public bool GcReportOnlyLeafFunclet { get; }

        public RegisterAllocatedMethod(
            GenTreeMethod genTreeMethod,
            ImmutableArray<GenTreeBlock> blocks,
            ImmutableArray<GenTree> nodes,
            ImmutableArray<RegisterAllocationInfo> allocations,
            IReadOnlyDictionary<GenTree, RegisterAllocationInfo> allocationByNode,
            IReadOnlyDictionary<int, ImmutableArray<GenTreeInternalRegister>>? internalRegistersByNodeId,
            int spillSlotCount,
            int parallelCopyScratchSpillSlot,
            StackFrameLayout? stackFrame = null,
            bool hasPrologEpilog = false,
            ImmutableArray<RegisterUnwindCode> unwindCodes = default,
            ImmutableArray<RegisterGcLiveRange> gcLiveRanges = default,
            ImmutableArray<RegisterGcTransition> gcTransitions = default,
            ImmutableArray<RegisterGcInterruptibleRange> gcInterruptibleRanges = default,
            ImmutableArray<RegisterFunclet> funclets = default,
            ImmutableArray<RegisterFrameRegion> frameRegions = default,
            bool? gcReportOnlyLeafFunclet = null,
            IReadOnlyDictionary<int, int>? lsraNodePositions = null,
            ImmutableArray<int> lsraBlockStartPositions = default,
            ImmutableArray<int> lsraBlockEndPositions = default)
        {
            GenTreeMethod = genTreeMethod ?? throw new ArgumentNullException(nameof(genTreeMethod));
            Blocks = blocks.IsDefault ? ImmutableArray<GenTreeBlock>.Empty : blocks;
            LinearNodes = nodes.IsDefault ? ImmutableArray<GenTree>.Empty : nodes;
            Allocations = allocations.IsDefault ? ImmutableArray<RegisterAllocationInfo>.Empty : allocations;
            AllocationByNode = allocationByNode ?? throw new ArgumentNullException(nameof(allocationByNode));
            InternalRegistersByNodeId = internalRegistersByNodeId ?? new Dictionary<int, ImmutableArray<GenTreeInternalRegister>>();
            LsraNodePositions = lsraNodePositions is null
                ? new Dictionary<int, int>()
                : new Dictionary<int, int>(lsraNodePositions);
            LsraBlockStartPositions = lsraBlockStartPositions.IsDefault ? ImmutableArray<int>.Empty : lsraBlockStartPositions;
            LsraBlockEndPositions = lsraBlockEndPositions.IsDefault ? ImmutableArray<int>.Empty : lsraBlockEndPositions;
            SpillSlotCount = spillSlotCount;
            ParallelCopyScratchSpillSlot = parallelCopyScratchSpillSlot;
            StackFrame = stackFrame ?? StackFrameLayout.Empty;
            HasPrologEpilog = hasPrologEpilog;
            UnwindCodes = unwindCodes.IsDefault ? ImmutableArray<RegisterUnwindCode>.Empty : unwindCodes;
            GcLiveRanges = gcLiveRanges.IsDefault ? ImmutableArray<RegisterGcLiveRange>.Empty : gcLiveRanges;
            GcTransitions = gcTransitions.IsDefault ? ImmutableArray<RegisterGcTransition>.Empty : gcTransitions;
            GcInterruptibleRanges = gcInterruptibleRanges.IsDefault ? ImmutableArray<RegisterGcInterruptibleRange>.Empty : gcInterruptibleRanges;
            Funclets = funclets.IsDefault ? RegisterFunclet.Build(GenTreeMethod) : funclets;
            FrameRegions = frameRegions.IsDefault ? ImmutableArray<RegisterFrameRegion>.Empty : frameRegions;
            GcReportOnlyLeafFunclet = gcReportOnlyLeafFunclet ?? Funclets.Length > 1;
        }

        public RegisterOperand GetHome(GenTree value)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));
            if (value.RegisterAllocation is null)
                throw new InvalidOperationException("No register allocation attached to GenTree node " + value + ".");
            return value.RegisterHome;
        }

        public RegisterValueLocation GetValueLocation(GenTree value, int position, bool isReturn = false)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));
            return value.GetRegisterLocation(position, isReturn);
        }

        public RegisterValueLocation GetValueLocationAtDefinition(GenTree value, bool isReturn = false)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));
            if (isReturn)
                return value.GetRegisterLocation(value.RegisterAllocation?.DefinitionPosition ?? 0, isReturn: true);
            if (value.RegisterAllocation is null)
                throw new InvalidOperationException("No register allocation attached to GenTree node " + value + ".");
            return value.RegisterLocationAtDefinition;
        }
    }

    internal static class LinearScanRegisterAllocator
    {
        public static GenTreeProgram AllocateProgram(GenTreeProgram program, RegisterAllocatorOptions? options = null)
        {
            if (program is null)
                throw new ArgumentNullException(nameof(program));

            options ??= RegisterAllocatorOptions.Default;

            var methods = ImmutableArray.CreateBuilder<GenTreeMethod>(program.Methods.Length);
            for (int i = 0; i < program.Methods.Length; i++)
                methods.Add(AllocateMethod(program.Methods[i], options));

            return new GenTreeProgram(methods.ToImmutable());
        }

        public static GenTreeMethod AllocateMethod(GenTreeMethod method, RegisterAllocatorOptions? options = null)
        {
            var registerMethod = AllocateRegisterAllocatedMethod(method, options);
            AttachRegisterAllocatedMethodToLir(registerMethod);
            return registerMethod.GenTreeMethod;
        }

        private static RegisterAllocatedMethod AllocateRegisterAllocatedMethod(GenTreeMethod method, RegisterAllocatorOptions? options = null)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            options ??= RegisterAllocatorOptions.Default;
            ValidateRegisterSet(options.AllocatableGeneralRegisters, RegisterClass.General);
            ValidateRegisterSet(options.AllocatableFloatRegisters, RegisterClass.Float);

            if (method.Phase < GenTreeMethodPhase.LoweredLir)
            {
                throw new InvalidOperationException(
                    $"LSRA requires lowered LIR for method {method.RuntimeMethod}. " +
                    "Run GenTreeLinearLowerer.LowerMethod before register allocation.");
            }

            LinearVerifier.VerifyBeforeLsra(method);

            var allocator = new MethodAllocator(method, options);
            var result = allocator.Run();

            if (options.FinalizeStackLayout)
                result = RegisterStackLayoutFinalizer.FinalizeMethod(result, options.StackLayoutOptions);

            if (options.GeneratePrologEpilog)
            {
                if (result.StackFrame.IsEmpty && !options.FinalizeStackLayout)
                    throw new InvalidOperationException("Prolog/epilog generation requires finalized stack layout.");
                result = RegisterPrologEpilogGenerator.GenerateMethod(result);
            }

            if (options.BuildGcInfo)
                result = RegisterGcInfoBuilder.AttachMethod(result);

            if (options.Validate)
                RegisterAllocationVerifier.Verify(result);

            return result;
        }

        private static void AttachRegisterAllocatedMethodToLir(RegisterAllocatedMethod registerMethod)
        {
            var method = registerMethod.GenTreeMethod;
            var allocationByValue = new Dictionary<GenTreeValueKey, RegisterAllocationInfo>();
            foreach (var allocation in registerMethod.Allocations)
                allocationByValue[allocation.ValueKey] = allocation;

            method.AttachLsraFinalState(
                registerMethod.Allocations,
                allocationByValue,
                registerMethod.SpillSlotCount,
                registerMethod.ParallelCopyScratchSpillSlot,
                registerMethod.StackFrame,
                registerMethod.HasPrologEpilog,
                registerMethod.UnwindCodes,
                registerMethod.GcLiveRanges,
                registerMethod.GcTransitions,
                registerMethod.GcInterruptibleRanges,
                registerMethod.Funclets,
                registerMethod.FrameRegions,
                registerMethod.GcReportOnlyLeafFunclet);

            AttachLocalDescriptorAllocationState(method.ArgDescriptors, allocationByValue);
            AttachLocalDescriptorAllocationState(method.LocalDescriptors, allocationByValue);
            AttachLocalDescriptorAllocationState(method.TempDescriptors, allocationByValue);

            for (int b = 0; b < registerMethod.Blocks.Length; b++)
            {
                var block = registerMethod.Blocks[b];
                var nodes = block.LinearNodes;
                for (int i = 0; i < nodes.Length; i++)
                {
                    var node = nodes[i];
                    node.AttachLsraInfo(BuildLsraInfo(registerMethod, node));
                    int phiCopyFromBlockId = node.LinearPhiCopyFromBlockId;
                    int phiCopyToBlockId = node.LinearPhiCopyToBlockId;
                    node.SetLinearState(
                        node.LinearId >= 0 ? node.LinearId : node.Id,
                        block.Id,
                        i,
                        GenTreeLirKinds.IsCopyKind(node.Kind) ? GenTreeLinearKind.Copy : node.Kind == GenTreeKind.GcPoll ? GenTreeLinearKind.GcPoll : GenTreeLinearKind.Tree,
                        node.RegisterResults,
                        node.OperandFlags,
                        node.RegisterUses,
                        node.LinearLowering,
                        node.LinearMemoryAccess,
                        phiCopyFromBlockId,
                        phiCopyToBlockId);
                }
                block.SetLinearNodes(nodes);
            }

            ValidateSsaDescriptorAllocationState(method, allocationByValue);
        }

        private static void ValidateSsaDescriptorAllocationState(
            GenTreeMethod method,
            IReadOnlyDictionary<GenTreeValueKey, RegisterAllocationInfo> allocationByValue)
        {
            foreach (var kv in allocationByValue)
            {
                var key = kv.Key;
                if (!key.IsSsaValue)
                    continue;

                var descriptors = GetDescriptorsForLocalKind(method, key.LocalKind);
                if (!TryGetDescriptorForAllocationKey(descriptors, key, out var descriptor))
                    throw new InvalidOperationException("SSA allocation has no matching local descriptor: " + key + ".");

                if (!descriptor.IsRegisterCandidate || !descriptor.SsaPromoted)
                    throw new InvalidOperationException("SSA allocation was produced for a non-tracked or non-register-candidate local descriptor " + descriptor + ": " + key + ".");

                if (!descriptor.TryGetSsaAllocation(key.SsaVersion, out var mapped) || !ReferenceEquals(mapped, kv.Value))
                    throw new InvalidOperationException("SSA allocation was not attached to descriptor " + descriptor + ": " + key + ".");
            }
        }

        private static ImmutableArray<GenLocalDescriptor> GetDescriptorsForLocalKind(GenTreeMethod method, GenLocalKind kind)
            => kind switch
            {
                GenLocalKind.Argument => method.ArgDescriptors,
                GenLocalKind.Local => method.LocalDescriptors,
                GenLocalKind.Temporary => method.TempDescriptors,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };

        private static void AttachLocalDescriptorAllocationState(
            ImmutableArray<GenLocalDescriptor> descriptors,
            IReadOnlyDictionary<GenTreeValueKey, RegisterAllocationInfo> allocationByValue)
        {
            for (int i = 0; i < descriptors.Length; i++)
                descriptors[i].ResetRegisterAllocationState();

            foreach (var allocation in allocationByValue.Values)
            {
                if (!TryGetDescriptorForAllocationKey(descriptors, allocation.ValueKey, out var descriptor))
                    continue;

                if (allocation.ValueKey.IsSsaValue)
                    descriptor.SetSsaAllocation(allocation.ValueKey.SsaVersion, allocation);

                AccumulateDescriptorAllocationState(descriptor, allocation);
            }
        }

        private static bool TryGetDescriptorForAllocationKey(
            ImmutableArray<GenLocalDescriptor> descriptors,
            GenTreeValueKey key,
            out GenLocalDescriptor descriptor)
        {
            if (!key.IsLocalDescriptor && !key.IsSsaValue)
            {
                descriptor = null!;
                return false;
            }

            if (key.IsSsaValue && key.SsaSlot.HasLclNum)
            {
                for (int i = 0; i < descriptors.Length; i++)
                {
                    var candidate = descriptors[i];
                    if (candidate.LclNum == key.SsaSlot.LclNum)
                    {
                        descriptor = candidate;
                        return true;
                    }
                }

                descriptor = null!;
                return false;
            }

            for (int i = 0; i < descriptors.Length; i++)
            {
                var candidate = descriptors[i];
                if (candidate.Kind == key.LocalKind && candidate.Index == key.Index)
                {
                    descriptor = candidate;
                    return true;
                }
            }

            descriptor = null!;
            return false;
        }

        private static void AccumulateDescriptorAllocationState(GenLocalDescriptor descriptor, RegisterAllocationInfo allocation)
        {
            if (descriptor.FrameHome.IsNone && !allocation.Home.IsNone)
                descriptor.FrameHome = allocation.Home;

            MachineRegister register = MachineRegister.Invalid;
            if (allocation.Home.IsRegister)
            {
                register = allocation.Home.Register;
            }
            else
            {
                var definition = allocation.LocationAtDefinition();
                if (definition.IsRegister)
                    register = definition.Register;
            }

            if (register != MachineRegister.Invalid && descriptor.RegNum == MachineRegister.Invalid)
                descriptor.RegNum = register;

            descriptor.Register |= register != MachineRegister.Invalid;
            descriptor.Spilled |= allocation.Home.IsMemoryOperand;

            for (int s = 0; s < allocation.Segments.Length && !descriptor.Spilled; s++)
                descriptor.Spilled = allocation.Segments[s].Location.IsMemoryOperand;

            for (int f = 0; f < allocation.Fragments.Length && !descriptor.Spilled; f++)
            {
                var fragment = allocation.Fragments[f];
                for (int s = 0; s < fragment.Segments.Length; s++)
                {
                    if (fragment.Segments[s].Location.IsMemoryOperand)
                    {
                        descriptor.Spilled = true;
                        break;
                    }
                }
            }
        }

        private static ImmutableArray<GenTreeInternalRegister> GetAssignedInternalRegisters(RegisterAllocatedMethod registerMethod, GenTree node)
        {
            int nodeId = node.GenTreeLinearId >= 0 ? node.GenTreeLinearId : node.Id;
            return registerMethod.InternalRegistersByNodeId.TryGetValue(nodeId, out var registers)
                ? registers
                : ImmutableArray<GenTreeInternalRegister>.Empty;
        }

        private static bool IsRegOptionalValueWithoutRegister(GenTreeMethod method, GenTree valueNode)
        {
            var valueKey = valueNode.LinearValueKey;

            if (!method.RegisterAllocationByValue.TryGetValue(valueKey, out var allocation))
                return false;

            for (int i = 0; i < method.RefPositions.Length; i++)
            {
                var rp = method.RefPositions[i];
                if (rp.Kind != LinearRefPositionKind.Use || rp.Value is null)
                    continue;

                var rpValueKey = rp.Value.LinearValueKey;
                if (!rpValueKey.Equals(valueKey))
                    continue;

                if ((rp.Flags & LinearRefPositionFlags.RegOptional) == 0)
                    continue;

                var location = rp.IsAbiSegment
                    ? allocation.FragmentLocationAt(rp.Position, rp.AbiSegmentIndex)
                    : allocation.LocationAt(rp.Position);

                if (!location.IsRegister)
                    return true;
            }

            return false;
        }


        private static GenTreeLsraInfo BuildLsraInfo(RegisterAllocatedMethod registerMethod, GenTree node)
        {
            var method = registerMethod.GenTreeMethod;
            MachineRegister gtReg = MachineRegister.Invalid;
            if (node.Results.Length == 1 && node.Results[0].IsRegister)
                gtReg = node.Results[0].Register;

            var internalRegisters = GetAssignedInternalRegisters(registerMethod, node);
            RegisterValueLocation locationAtDefinition = default;
            if (node.RegisterResults.Length == 1 &&
                method.RegisterAllocationByValue.TryGetValue(node.RegisterResults[0].LinearValueKey, out var resultAllocation))
            {
                var resultInfo = method.GetValueInfo(node.RegisterResults[0]);
                var resultAbi = MachineAbi.ClassifyStorageValue(resultInfo.Type, resultInfo.StackKind);
                locationAtDefinition = resultAllocation.ValueLocationAtDefinition(resultAbi);
            }

            GenTreeLsraFlags flags = GenTreeLsraFlags.None;
            if (IsRegOptionalValueWithoutRegister(method, node))
                flags |= GenTreeLsraFlags.NoRegAtUse;
            if ((node.MoveFlags & MoveFlags.Spill) != 0)
                flags |= GenTreeLsraFlags.Spill;
            if ((node.MoveFlags & MoveFlags.Reload) != 0)
                flags |= GenTreeLsraFlags.Reload;
            if (internalRegisters.Length != 0)
                flags |= GenTreeLsraFlags.ContainsInternalRegister;

            var resultValues = ImmutableArray.CreateBuilder<GenTreeValueKey>(node.RegisterResults.Length);
            for (int i = 0; i < node.RegisterResults.Length; i++)
            {
                var value = node.RegisterResults[i];
                resultValues.Add(value.LinearValueKey);
            }

            var useValues = ImmutableArray.CreateBuilder<GenTreeValueKey>(node.RegisterUses.Length);
            for (int i = 0; i < node.RegisterUses.Length; i++)
            {
                var value = node.RegisterUses[i];
                useValues.Add(value.LinearValueKey);
            }

            return new GenTreeLsraInfo
            {
                GtRegNum = gtReg,
                Flags = flags,
                Home = node.Results.Length == 1 ? node.Results[0] : RegisterOperand.None,
                LocationAtDefinition = locationAtDefinition,
                CodegenResults = node.Results,
                CodegenUses = node.Uses,
                CodegenUseRoles = node.UseRoles,
                CodegenResultValues = resultValues.ToImmutable(),
                CodegenUseValues = useValues.ToImmutable(),
                InternalRegisters = internalRegisters,
                MoveFlags = node.MoveFlags,
                FrameOperation = node.FrameOperation,
                Immediate = node.Immediate,
                Comment = node.Comment
            };
        }

        private static void ValidateRegisterSet(ImmutableArray<MachineRegister> registers, RegisterClass expectedClass)
        {
            if (registers.IsDefaultOrEmpty)
                throw new InvalidOperationException("Register allocator needs at least one allocatable " + expectedClass + " register.");

            var seen = new HashSet<MachineRegister>();
            for (int i = 0; i < registers.Length; i++)
            {
                var reg = registers[i];
                if (reg == MachineRegister.Invalid)
                    throw new InvalidOperationException("Invalid allocatable register.");
                if (!MachineRegisters.IsRegisterInClass(reg, expectedClass))
                    throw new InvalidOperationException("Register " + MachineRegisters.Format(reg) + " is not a " + expectedClass + " register.");
                if (MachineRegisters.IsReserved(reg))
                    throw new InvalidOperationException("Register " + MachineRegisters.Format(reg) + " is reserved and cannot be allocatable.");
                if (!seen.Add(reg))
                    throw new InvalidOperationException("Duplicate allocatable register " + MachineRegisters.Format(reg) + ".");
            }
        }

        private sealed class MethodAllocator
        {
            private readonly GenTreeMethod _method;
            private readonly RegisterAllocatorOptions _options;
            private readonly Dictionary<GenTree, List<GenTree>> _preferences;
            private readonly Dictionary<AllocationPreferenceKey, List<MachineRegister>> _registerPreferences;
            private readonly Dictionary<int, int> _nodePositions;
            private readonly ImmutableArray<int> _linearBlockOrder;
            private readonly int[] _blockStartPositions;
            private readonly int[] _blockEndPositions;
            private readonly ImmutableArray<int> _callPositions;
            private readonly ImmutableArray<LinearRefPosition> _fixedKillRefPositions;
            private readonly Dictionary<int, ImmutableArray<GenTreeInternalRegister>.Builder> _allocatedInternalRegisters = new();
            private readonly Dictionary<GenTree, List<AllocationInterval>> _intervalsByNode = new();
            private readonly Dictionary<GenTree, RegisterOperand> _aggregateHomes = new();
            private readonly Dictionary<GenTree, RegisterAllocationInfo> _allocations = new();
            private readonly List<AllocationInterval> _active = new();
            private readonly List<AllocationInterval> _inactive = new();
            private readonly List<AllocationInterval> _handled = new();
            private int _nextSpillSlot;
            private int _nextNodeId;

            public MethodAllocator(GenTreeMethod method, RegisterAllocatorOptions options)
            {
                _method = method;
                _options = options;
                _linearBlockOrder = method.LinearBlockOrder.IsDefaultOrEmpty
                    ? LinearBlockOrder.Compute(method.Cfg)
                    : method.LinearBlockOrder;
                _preferences = BuildPreferences(method);
                _registerPreferences = BuildRegisterPreferences(method);
                _nodePositions = BuildPositionLayout(method, _linearBlockOrder, out _blockStartPositions, out _blockEndPositions);
                _callPositions = BuildCallPositions(method, _nodePositions);
                _fixedKillRefPositions = BuildFixedKillRefPositions(method, _nodePositions, _options);
                _nextNodeId = ComputeNextNodeId(method);
            }

            public RegisterAllocatedMethod Run()
            {
                AllocateIntervals();
                AttachAllocationsToGenTrees();

                int copyScratchSlot = -1;

                int GetCopyScratchSlot()
                {
                    if (copyScratchSlot < 0)
                        copyScratchSlot = _nextSpillSlot++;
                    return copyScratchSlot;
                }

                var splitPlan = BuildSplitResolutionPlan();
                var blocks = EmitBlocks(GetCopyScratchSlot, splitPlan, out var nodes);

                var allocationList = new List<RegisterAllocationInfo>(_allocations.Values);
                allocationList.Sort(static (a, b) => a.Value.Id.CompareTo(b.Value.Id));

                return new RegisterAllocatedMethod(
                    _method,
                    blocks,
                    nodes,
                    allocationList.ToImmutableArray(),
                    new Dictionary<GenTree, RegisterAllocationInfo>(_allocations),
                    BuildActualInternalRegisterSideTables(),
                    spillSlotCount: _nextSpillSlot,
                    parallelCopyScratchSpillSlot: copyScratchSlot,
                    lsraNodePositions: new Dictionary<int, int>(_nodePositions),
                    lsraBlockStartPositions: ImmutableArray.CreateRange(_blockStartPositions),
                    lsraBlockEndPositions: ImmutableArray.CreateRange(_blockEndPositions));
            }


            private static int ComputeNextNodeId(GenTreeMethod method)
            {
                int max = -1;

                for (int i = 0; i < method.LinearNodes.Length; i++)
                {
                    var node = method.LinearNodes[i];
                    if (node.Id > max)
                        max = node.Id;
                    if (node.LinearId > max)
                        max = node.LinearId;
                }

                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    var nodes = method.Blocks[b].LinearNodes;
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        var node = nodes[i];
                        if (node.Id > max)
                            max = node.Id;
                        if (node.LinearId > max)
                            max = node.LinearId;
                    }
                }

                return checked(max + 1);
            }

            private IReadOnlyDictionary<int, ImmutableArray<GenTreeInternalRegister>> BuildActualInternalRegisterSideTables()
            {
                if (_allocatedInternalRegisters.Count == 0)
                    return new Dictionary<int, ImmutableArray<GenTreeInternalRegister>>();

                var result = new Dictionary<int, ImmutableArray<GenTreeInternalRegister>>(_allocatedInternalRegisters.Count);
                foreach (var pair in _allocatedInternalRegisters)
                    result.Add(pair.Key, pair.Value.ToImmutable());
                return result;
            }


            private void AttachAllocationsToGenTrees()
            {
                foreach (var allocation in _allocations.Values)
                {
                    allocation.Value.AttachRegisterAllocation(allocation);
                }
            }

            private enum AllocationStreamItemKind : byte
            {
                IntervalStart = 0,
                InternalRefPosition = 1,
            }

            private readonly struct AllocationStreamItem
            {
                private AllocationStreamItem(
                    int position,
                    AllocationStreamItemKind kind,
                    AllocationInterval? interval,
                    LinearRefPosition refPosition)
                {
                    Position = position;
                    Kind = kind;
                    Interval = interval;
                    RefPosition = refPosition;
                }

                public int Position { get; }

                public AllocationStreamItemKind Kind { get; }

                public AllocationInterval? Interval { get; }

                public LinearRefPosition RefPosition { get; }

                public static AllocationStreamItem ForInterval(AllocationInterval interval)
                    => new AllocationStreamItem(interval.Start, AllocationStreamItemKind.IntervalStart, interval, default);

                public static AllocationStreamItem ForInternalRefPosition(LinearRefPosition refPosition)
                    => new AllocationStreamItem(refPosition.Position, AllocationStreamItemKind.InternalRefPosition, null, refPosition);
            }

            private void AllocateIntervals()
            {
                var intervals = BuildAllocationIntervals();
                var allocationStream = BuildAllocationStream(intervals);

                for (int i = 0; i < allocationStream.Count; i++)
                {
                    var item = allocationStream[i];
                    UpdateActiveAndInactive(item.Position);

                    if (item.Kind == AllocationStreamItemKind.InternalRefPosition)
                    {
                        AllocateInternalRefPosition(item.RefPosition);
                        continue;
                    }

                    var current = item.Interval!;
                    if (current.IsEmpty)
                    {
                        AssignHome(current);
                        continue;
                    }

                    if (current.RequiresStackHome)
                    {
                        Spill(current);
                        continue;
                    }

                    if (TryAllocatePreferredRegister(current))
                        continue;

                    if (TryAllocateFreeRegister(current))
                        continue;

                    AllocateBlockedRegister(current);
                }
            }

            private List<AllocationStreamItem> BuildAllocationStream(List<AllocationInterval> intervals)
            {
                var result = new List<AllocationStreamItem>(intervals.Count + _method.RefPositions.Length);

                for (int i = 0; i < intervals.Count; i++)
                    result.Add(AllocationStreamItem.ForInterval(intervals[i]));

                for (int i = 0; i < _method.RefPositions.Length; i++)
                {
                    var refPosition = _method.RefPositions[i];
                    if (refPosition.Kind == LinearRefPositionKind.Internal)
                        result.Add(AllocationStreamItem.ForInternalRefPosition(refPosition));
                }

                result.Sort(static (a, b) =>
                {
                    int c = a.Position.CompareTo(b.Position);
                    if (c != 0)
                        return c;

                    c = a.Kind.CompareTo(b.Kind);
                    if (c != 0)
                        return c;

                    if (a.Kind == AllocationStreamItemKind.IntervalStart)
                    {
                        var left = a.Interval!;
                        var right = b.Interval!;
                        c = left.Value.Id.CompareTo(right.Value.Id);
                        if (c != 0)
                            return c;
                        return left.AbiSegmentIndex.CompareTo(right.AbiSegmentIndex);
                    }

                    c = a.RefPosition.NodeId.CompareTo(b.RefPosition.NodeId);
                    if (c != 0)
                        return c;
                    return a.RefPosition.OperandIndex.CompareTo(b.RefPosition.OperandIndex);
                });

                return result;
            }

            private void AllocateInternalRefPosition(LinearRefPosition internalRef)
            {
                if (internalRef.Kind != LinearRefPositionKind.Internal)
                    throw new InvalidOperationException("Expected an internal register ref-position.");

                int count = internalRef.MinimumRegisterCount;
                if (count <= 0)
                    throw new InvalidOperationException($"Invalid internal register count for node {internalRef.NodeId}.");

                ulong alreadySelected = 0;
                for (int i = 0; i < count; i++)
                {
                    var selected = TrySelectFreeInternalRegister(internalRef, alreadySelected);
                    if (selected == MachineRegister.Invalid)
                        selected = SelectInternalRegisterBySpilling(internalRef, alreadySelected);

                    if (selected == MachineRegister.Invalid)
                    {
                        throw new InvalidOperationException(
                            $"Unable to allocate internal {internalRef.RegisterClass} register {i + 1}/{count} for node {internalRef.NodeId}.");
                    }

                    alreadySelected |= MachineRegisters.MaskOf(selected);
                    RecordInternalRegister(internalRef, selected);
                }
            }

            private MachineRegister TrySelectFreeInternalRegister(LinearRefPosition internalRef, ulong alreadySelected)
            {
                ulong forbidden = alreadySelected | HardUseRegisterMaskAt(internalRef.NodeId, internalRef.Position, internalRef.RegisterClass);
                var registers = _options.GetAllocatableRegisters(internalRef.RegisterClass);

                for (int i = 0; i < registers.Length; i++)
                {
                    var register = registers[i];
                    ulong bit = MachineRegisters.MaskOf(register);
                    if ((internalRef.RegisterMask & bit) == 0)
                        continue;
                    if ((forbidden & bit) != 0)
                        continue;
                    if (FindRegisterOwnerAt(register, internalRef.Position) is not null)
                        continue;
                    return register;
                }

                return MachineRegister.Invalid;
            }

            private MachineRegister SelectInternalRegisterBySpilling(LinearRefPosition internalRef, ulong alreadySelected)
            {
                ulong forbidden = alreadySelected | HardUseRegisterMaskAt(internalRef.NodeId, internalRef.Position, internalRef.RegisterClass);
                var registers = _options.GetAllocatableRegisters(internalRef.RegisterClass);

                MachineRegister bestRegister = MachineRegister.Invalid;
                AllocationInterval? bestOwner = null;
                int bestNextUse = -1;

                for (int i = 0; i < registers.Length; i++)
                {
                    var register = registers[i];
                    ulong bit = MachineRegisters.MaskOf(register);
                    if ((internalRef.RegisterMask & bit) == 0)
                        continue;
                    if ((forbidden & bit) != 0)
                        continue;

                    var owner = FindRegisterOwnerAt(register, internalRef.Position);
                    if (owner is null)
                        return register;

                    int nextUse = owner.NextUseAfterOrAt(internalRef.Position);
                    if (nextUse > bestNextUse)
                    {
                        bestRegister = register;
                        bestOwner = owner;
                        bestNextUse = nextUse;
                    }
                }

                if (bestRegister == MachineRegister.Invalid || bestOwner is null)
                    return MachineRegister.Invalid;

                SpillRegisterOwnerAt(bestOwner, internalRef.Position);
                return bestRegister;
            }

            private ulong HardUseRegisterMaskAt(int nodeId, int position, RegisterClass registerClass)
            {
                ulong mask = 0;

                for (int i = 0; i < _method.RefPositions.Length; i++)
                {
                    var use = _method.RefPositions[i];
                    if (use.NodeId != nodeId || use.Position != position || use.Kind != LinearRefPositionKind.Use || use.Value is null)
                        continue;
                    if (use.RegisterClass != registerClass)
                        continue;
                    if ((use.Flags & LinearRefPositionFlags.RegOptional) != 0 && (use.Flags & LinearRefPositionFlags.RequiresRegister) == 0)
                        continue;
                    if (!TryGetAllocationForValue(use.Value, out var allocation))
                        continue;

                    var location = use.IsAbiSegment
                        ? allocation.FragmentLocationAt(position, use.AbiSegmentIndex)
                        : allocation.LocationAt(position);
                    if (location.IsRegister)
                        mask |= MachineRegisters.MaskOf(location.Register);
                }

                return mask;
            }

            private bool TryGetAllocationForValue(GenTree value, out RegisterAllocationInfo allocation)
            {
                if (_allocations.TryGetValue(value, out var exactAllocation))
                {
                    allocation = exactAllocation;
                    return true;
                }

                var key = value.LinearValueKey;
                foreach (var candidate in _allocations.Values)
                {
                    if (candidate.ValueKey.Equals(key))
                    {
                        allocation = candidate;
                        return true;
                    }
                }

                allocation = null!;
                return false;
            }

            private AllocationInterval? FindRegisterOwnerAt(MachineRegister register, int position)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    var active = _active[i];
                    if (active.AssignedRegister == register && active.Covers(position))
                        return active;
                }

                return null;
            }

            private void SpillRegisterOwnerAt(AllocationInterval owner, int position)
            {
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    if (!ReferenceEquals(_active[i], owner))
                        continue;

                    _active.RemoveAt(i);
                    SplitToSpill(owner, position);
                    _handled.Add(owner);
                    return;
                }

                throw new InvalidOperationException($"Cannot spill register owner for {MachineRegisters.Format(owner.AssignedRegister)} at {position}.");
            }

            private void RecordInternalRegister(LinearRefPosition internalRef, MachineRegister register)
            {
                if (!_allocatedInternalRegisters.TryGetValue(internalRef.NodeId, out var builder))
                {
                    builder = ImmutableArray.CreateBuilder<GenTreeInternalRegister>();
                    _allocatedInternalRegisters.Add(internalRef.NodeId, builder);
                }

                builder.Add(new GenTreeInternalRegister(
                    register,
                    internalRef.RegisterClass,
                    internalRef.Position,
                    GenTreeValueKey.ForTree(NodeForLinearId(internalRef.NodeId))));
            }

            private GenTree NodeForLinearId(int nodeId)
            {
                for (int i = 0; i < _method.LinearNodes.Length; i++)
                {
                    var node = _method.LinearNodes[i];
                    int currentId = node.LinearId >= 0 ? node.LinearId : node.Id;
                    if (currentId == nodeId)
                        return node;
                }

                throw new InvalidOperationException($"Internal register ref-position points at missing node {nodeId}.");
            }

            private List<AllocationInterval> BuildAllocationIntervals()
            {
                var result = new List<AllocationInterval>(_method.LiveIntervals.Length);

                for (int i = 0; i < _method.LiveIntervals.Length; i++)
                {
                    var source = _method.LiveIntervals[i];
                    var valueInfo = _method.GetValueInfo(source.Value);
                    var abi = MachineAbi.ClassifyStorageValue(valueInfo.Type, valueInfo.StackKind);

                    if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        _aggregateHomes[source.Value] = CreateAggregateHome(abi);
                        var segments = MachineAbi.GetRegisterSegments(abi);
                        var fragments = new List<AllocationInterval>(segments.Length);

                        for (int s = 0; s < segments.Length; s++)
                        {
                            var segment = segments[s];
                            var segmentUses = GetAllocationUsePositionsForValueSegment(source.Value, s, source.UsePositions);
                            var interval = new AllocationInterval(
                                source.Value,
                                segment.RegisterClass,
                                source.Ranges,
                                segmentUses,
                                source.DefinitionPosition,
                                CrossesCall(source.Ranges),
                                requiresSingleLocation: RequiresSingleLocation(source),
                                requiresStackHome: RefPositionsRequireStackHome(source.Value, s) || IsLiveAcrossExceptionEdge(source),
                                stackHomeSize: segment.Size,
                                stackHomeAlignment: abi.Alignment <= 0 ? TargetArchitecture.PointerSize : abi.Alignment,
                                abiSegmentIndex: s,
                                abiSegmentOffset: segment.Offset,
                                abiSegmentSize: segment.Size);
                            fragments.Add(interval);
                            result.Add(interval);
                        }

                        _intervalsByNode[source.Value] = fragments;
                        continue;
                    }
                    {
                        var scalarUses = GetAllocationUsePositionsForValueSegment(source.Value, -1, source.UsePositions);
                        bool requiresStackHome =
                            MachineAbi.RequiresStackHome(valueInfo.Type, valueInfo.StackKind) ||
                            RefPositionsRequireStackHome(source.Value, -1) ||
                            IsLiveAcrossExceptionEdge(source);
                        var interval = new AllocationInterval(
                            source.Value,
                            valueInfo.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : valueInfo.RegisterClass,
                            source.Ranges,
                            scalarUses,
                            source.DefinitionPosition,
                            CrossesCall(source.Ranges),
                            requiresSingleLocation: RequiresSingleLocation(source),
                            requiresStackHome,
                            stackHomeSize: abi.Size <= 0 ? TargetArchitecture.PointerSize : abi.Size,
                            stackHomeAlignment: abi.Alignment <= 0 ? TargetArchitecture.PointerSize : abi.Alignment);
                        _intervalsByNode.Add(interval.Value, new List<AllocationInterval> { interval });
                        result.Add(interval);
                    }

                }

                return result;
            }

            private bool RequiresSingleLocation(LinearLiveInterval interval)
            {
                if (interval.Ranges.Length == 0)
                    return false;

                if (interval.Ranges.Length != 1)
                    return true;

                var range = interval.Ranges[0];
                for (int i = 0; i < _blockStartPositions.Length; i++)
                {
                    int blockStart = _blockStartPositions[i];
                    int blockEndExclusive = _blockEndPositions[i] + 1;
                    if (blockStart <= range.Start && range.End <= blockEndExclusive)
                        return false;
                }

                return true;
            }

            private bool IsLiveAcrossExceptionEdge(LinearLiveInterval interval)
            {
                if (interval.Ranges.Length == 0)
                    return false;

                for (int b = 0; b < _method.Cfg.Blocks.Length; b++)
                {
                    var block = _method.Cfg.Blocks[b];
                    for (int s = 0; s < block.Successors.Length; s++)
                    {
                        var edge = block.Successors[s];
                        if (edge.Kind != CfgEdgeKind.Exception)
                            continue;

                        int fromPosition = _blockEndPositions[edge.FromBlockId];
                        int toPosition = _blockStartPositions[edge.ToBlockId];
                        if (IsLiveAt(interval, fromPosition) && IsLiveAt(interval, toPosition))
                            return true;
                    }
                }

                return false;
            }

            private static bool IsLiveAt(LinearLiveInterval interval, int position)
            {
                for (int i = 0; i < interval.Ranges.Length; i++)
                {
                    var range = interval.Ranges[i];
                    if (range.Start <= position && position < range.End)
                        return true;
                }

                return false;
            }

            private bool RefPositionsRequireStackHome(GenTree value, int abiSegmentIndex)
            {
                for (int i = 0; i < _method.RefPositions.Length; i++)
                {
                    var rp = _method.RefPositions[i];
                    if (rp.Value is null || !rp.Value.Equals(value))
                        continue;
                    if ((rp.Flags & LinearRefPositionFlags.StackOnly) == 0)
                        continue;
                    if (abiSegmentIndex >= 0)
                    {
                        if (rp.AbiSegmentIndex != abiSegmentIndex)
                            continue;
                    }
                    else if (rp.IsAbiSegment)
                    {
                        continue;
                    }

                    return true;
                }

                return false;
            }


            private ImmutableArray<int> GetAllocationUsePositionsForValueSegment(GenTree value, int abiSegmentIndex, ImmutableArray<int> fallback)
            {
                var positions = ImmutableArray.CreateBuilder<int>();
                int last = int.MinValue;
                bool sawMatchingReference = false;

                for (int i = 0; i < _method.RefPositions.Length; i++)
                {
                    var rp = _method.RefPositions[i];
                    if (rp.Value is null || !rp.Value.Equals(value))
                        continue;

                    if (rp.Kind != LinearRefPositionKind.Use && rp.Kind != LinearRefPositionKind.Def)
                        continue;

                    if (abiSegmentIndex >= 0)
                    {
                        if (rp.AbiSegmentIndex != abiSegmentIndex)
                            continue;
                    }
                    else if (rp.IsAbiSegment)
                    {
                        continue;
                    }

                    sawMatchingReference = true;

                    if (IsAllocationOptionalRefPosition(rp))
                        continue;

                    if (rp.Position == last)
                        continue;

                    positions.Add(rp.Position);
                    last = rp.Position;
                }

                return sawMatchingReference ? positions.ToImmutable() : fallback;
            }

            private static bool IsAllocationOptionalRefPosition(LinearRefPosition refPosition)
            {
                if ((refPosition.Flags & LinearRefPositionFlags.RequiresRegister) != 0)
                    return false;
                if ((refPosition.Flags & LinearRefPositionFlags.FixedRegister) != 0)
                    return false;
                return (refPosition.Flags & LinearRefPositionFlags.RegOptional) != 0;
            }

            private RegisterOperand CreateAggregateHome(AbiValueInfo abi)
            {
                int slot = _nextSpillSlot++;
                return RegisterOperand.ForSpillSlot(
                    RegisterClass.General,
                    slot,
                    offset: 0,
                    size: abi.Size <= 0 ? TargetArchitecture.PointerSize : abi.Size);
            }

            private void UpdateActiveAndInactive(int position)
            {
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    var interval = _active[i];
                    if (!interval.HasAssignedRegister || interval.AssignedRegisterEnd <= position)
                    {
                        _active.RemoveAt(i);
                        _handled.Add(interval);
                    }
                    else if (!interval.Covers(position))
                    {
                        _active.RemoveAt(i);
                        _inactive.Add(interval);
                    }
                }

                for (int i = _inactive.Count - 1; i >= 0; i--)
                {
                    var interval = _inactive[i];
                    if (!interval.HasAssignedRegister || interval.AssignedRegisterEnd <= position)
                    {
                        _inactive.RemoveAt(i);
                        _handled.Add(interval);
                    }
                    else if (interval.Covers(position))
                    {
                        _inactive.RemoveAt(i);
                        _active.Add(interval);
                    }
                }
            }

            private bool TryAllocatePreferredRegister(AllocationInterval current)
            {
                if (TryAllocatePreferredMachineRegister(current))
                    return true;

                if (!_options.PreferCopySourceRegister)
                    return false;

                if (!_preferences.TryGetValue(current.Value, out var preferredValues))
                    return false;

                for (int i = 0; i < preferredValues.Count; i++)
                {
                    var preferred = preferredValues[i];
                    if (!_allocations.TryGetValue(preferred, out var preferredAllocation))
                        continue;

                    var home = preferredAllocation.LocationAt(current.Start);
                    if (!home.IsRegister)
                        continue;

                    if (home.RegisterClass != current.RegisterClass)
                        continue;

                    if (!TryAssignPreferredRegister(current, home.Register))
                        continue;

                    return true;
                }

                return false;
            }

            private bool TryAllocatePreferredMachineRegister(AllocationInterval current)
            {
                return TryAllocatePreferredMachineRegister(current, current.AbiSegmentIndex);
            }

            private bool TryAllocatePreferredMachineRegister(AllocationInterval current, int abiSegmentIndex)
            {
                if (!_registerPreferences.TryGetValue(new AllocationPreferenceKey(current.Value, abiSegmentIndex), out var preferredRegisters))
                    return false;

                for (int i = 0; i < preferredRegisters.Count; i++)
                {
                    if (TryAssignPreferredRegister(current, preferredRegisters[i]))
                        return true;
                }

                return false;
            }

            private bool TryAssignPreferredRegister(AllocationInterval current, MachineRegister register)
            {
                if (current.RequiresStackHome)
                    return false;

                if (register == MachineRegister.Invalid)
                    return false;

                if (!MachineRegisters.IsRegisterInClass(register, current.RegisterClass))
                    return false;

                if (!IsAllocatable(register))
                    return false;

                int freeUntil = FirstRegisterConflictPosition(register, current);
                int segmentEnd = ComputeRegisterSegmentEnd(current, register, freeUntil);
                if (segmentEnd <= current.Start)
                    return false;

                AssignRegisterSegment(current, register, segmentEnd);
                return true;
            }

            private bool TryAllocateFreeRegister(AllocationInterval current)
            {
                MachineRegister bestRegister = MachineRegister.Invalid;
                int bestSegmentEnd = -1;

                var registers = _options.GetAllocatableRegisters(current.RegisterClass);
                for (int i = 0; i < registers.Length; i++)
                {
                    var reg = registers[i];
                    int freeUntil = FirstRegisterConflictPosition(reg, current);
                    int segmentEnd = ComputeRegisterSegmentEnd(current, reg, freeUntil);
                    if (segmentEnd > bestSegmentEnd)
                    {
                        bestSegmentEnd = segmentEnd;
                        bestRegister = reg;
                    }
                }

                if (bestRegister == MachineRegister.Invalid || bestSegmentEnd <= current.Start)
                    return false;

                AssignRegisterSegment(current, bestRegister, bestSegmentEnd);
                return true;
            }

            private void AllocateBlockedRegister(AllocationInterval current)
            {
                int currentNextUse = current.NextUseAfterOrAt(current.Start);

                MachineRegister bestRegister = MachineRegister.Invalid;
                int bestBlockingNextUse = -1;
                int bestSegmentEnd = -1;

                var registers = _options.GetAllocatableRegisters(current.RegisterClass);
                for (int i = 0; i < registers.Length; i++)
                {
                    var reg = registers[i];
                    int blockingNextUse = NextUseOfBlockingIntervals(reg, current);
                    int conflict = FirstRegisterConflictPosition(reg, current);
                    int segmentEnd = ComputeRegisterSegmentEnd(current, reg, conflict);
                    if (segmentEnd <= current.Start)
                        continue;

                    if (blockingNextUse > bestBlockingNextUse ||
                        (blockingNextUse == bestBlockingNextUse && segmentEnd > bestSegmentEnd))
                    {
                        bestBlockingNextUse = blockingNextUse;
                        bestSegmentEnd = segmentEnd;
                        bestRegister = reg;
                    }
                }

                if (bestRegister == MachineRegister.Invalid || currentNextUse >= bestBlockingNextUse)
                {
                    Spill(current);
                    return;
                }

                SplitBlockingIntervals(bestRegister, current, bestSegmentEnd);
                AssignRegisterSegment(current, bestRegister, bestSegmentEnd);
            }

            private bool IsAllocatable(MachineRegister register)
            {
                var registerClass = MachineRegisters.GetClass(register);
                if (registerClass == RegisterClass.Invalid)
                    return false;

                var registers = _options.GetAllocatableRegisters(registerClass);
                for (int i = 0; i < registers.Length; i++)
                {
                    if (registers[i] == register)
                        return true;
                }
                return false;
            }

            private bool IsRegisterClassCompatible(MachineRegister register, AllocationInterval interval)
            {
                if (!MachineRegisters.IsRegisterInClass(register, interval.RegisterClass))
                    return false;

                if (_options.RespectCallClobbers && interval.CrossesCall && MachineRegisters.IsCallerSaved(register))
                    return false;

                return true;
            }

            private int ComputeRegisterSegmentEnd(AllocationInterval interval, MachineRegister register, int freeUntil)
            {
                if (!IsRegisterClassCompatible(register, interval))
                    return interval.Start;

                int end = freeUntil < interval.End ? freeUntil : interval.End;
                if (end <= interval.Start)
                    return interval.Start;

                int incompatibleFixedUse = FirstIncompatibleFixedRefPosition(interval, register, interval.Start, end);
                if (incompatibleFixedUse < end)
                    end = incompatibleFixedUse;

                if (_options.RespectCallClobbers)
                {
                    int fixedKillSplit = FirstFixedRegisterKillPosition(register, interval, interval.Start, end);
                    if (fixedKillSplit < end)
                        end = fixedKillSplit;
                }

                if (interval.RequiresSingleLocation && end < interval.End)
                    return interval.Start;

                return end;
            }

            private int FirstIncompatibleFixedRefPosition(AllocationInterval interval, MachineRegister register, int start, int end)
            {
                for (int i = 0; i < _method.RefPositions.Length; i++)
                {
                    var rp = _method.RefPositions[i];
                    if (rp.Position < start)
                        continue;
                    if (rp.Position >= end)
                        break;
                    if (rp.FixedRegister == MachineRegister.Invalid || rp.FixedRegister == register)
                        continue;
                    if (rp.Kind is not (LinearRefPositionKind.Use or LinearRefPositionKind.Def))
                        continue;
                    if (rp.Value is null || !rp.Value.Equals(interval.Value))
                        continue;
                    if (interval.IsAbiFragment)
                    {
                        if (rp.AbiSegmentIndex != interval.AbiSegmentIndex)
                            continue;
                    }
                    else if (rp.IsAbiSegment)
                    {
                        continue;
                    }
                    if (interval.CrossesPosition(rp.Position) || interval.Covers(rp.Position))
                        return rp.Position;
                }

                return int.MaxValue;
            }

            private int FirstFixedRegisterKillPosition(MachineRegister register, AllocationInterval interval, int start, int end)
            {
                for (int i = 0; i < _fixedKillRefPositions.Length; i++)
                {
                    var kill = _fixedKillRefPositions[i];
                    if (kill.Position < start)
                        continue;
                    if (kill.Position >= end)
                        break;
                    if (kill.FixedRegister == register && interval.CrossesPosition(kill.Position))
                        return kill.Position;
                }

                return int.MaxValue;
            }

            private int FirstCallerSavedSplitPosition(AllocationInterval interval, int start, int end)
            {
                for (int i = 0; i < _callPositions.Length; i++)
                {
                    int callPosition = _callPositions[i];
                    if (callPosition < start)
                        continue;
                    if (callPosition >= end)
                        break;
                    if (interval.CrossesPosition(callPosition))
                        return callPosition;
                }

                return int.MaxValue;
            }

            private bool CrossesCall(ImmutableArray<LinearLiveRange> ranges)
            {
                if (!_options.RespectCallClobbers || _callPositions.Length == 0 || ranges.Length == 0)
                    return false;

                int c = 0;
                for (int r = 0; r < ranges.Length; r++)
                {
                    var range = ranges[r];
                    while (c < _callPositions.Length && _callPositions[c] + 1 < range.Start)
                        c++;

                    int scan = c;
                    while (scan < _callPositions.Length && _callPositions[scan] < range.End)
                    {
                        int callPos = _callPositions[scan];
                        if (range.Start <= callPos && callPos + 1 < range.End)
                            return true;
                        scan++;
                    }
                }

                return false;
            }

            private static Dictionary<int, int> BuildPositionLayout(
                GenTreeMethod method,
                ImmutableArray<int> blockOrder,
                out int[] blockStartPositions,
                out int[] blockEndPositions)
            {
                var result = new Dictionary<int, int>();
                blockStartPositions = new int[method.Blocks.Length];
                blockEndPositions = new int[method.Blocks.Length];
                int position = 0;

                blockOrder = blockOrder.IsDefaultOrEmpty
                    ? LinearBlockOrder.Compute(method.Cfg)
                    : LinearBlockOrder.Normalize(method.Cfg, blockOrder);

                for (int o = 0; o < blockOrder.Length; o++)
                {
                    int b = blockOrder[o];
                    blockStartPositions[b] = position;
                    var nodes = method.Blocks[b].LinearNodes;
                    for (int n = 0; n < nodes.Length; n++)
                    {
                        var node = nodes[n];
                        if (result.ContainsKey(node.LinearId))
                            throw new InvalidOperationException($"Duplicate GenTree LinearId {node.LinearId} in input LIR stream.");
                        result.Add(node.LinearId, position);

                        if (node.IsPhiCopy)
                        {
                            while (n + 1 < nodes.Length && IsSamePhiCopyGroup(node, nodes[n + 1]))
                            {
                                n++;
                                if (result.ContainsKey(nodes[n].LinearId))
                                    throw new InvalidOperationException($"Duplicate GenTree LinearId {nodes[n].LinearId} in input LIR stream.");
                                result.Add(nodes[n].LinearId, position);
                            }
                        }

                        position += 2;
                    }
                    blockEndPositions[b] = position;
                    position += 2;
                }
                return result;
            }

            private static ImmutableArray<int> BuildCallPositions(GenTreeMethod method, Dictionary<int, int> nodePositions)
            {
                var positions = new SortedSet<int>();
                for (int i = 0; i < method.RefPositions.Length; i++)
                {
                    var rp = method.RefPositions[i];
                    if (rp.Kind == LinearRefPositionKind.Kill && rp.FixedRegister != MachineRegister.Invalid)
                        positions.Add(rp.Position);
                }

                if (positions.Count == 0)
                {
                    for (int i = 0; i < method.LinearNodes.Length; i++)
                    {
                        var node = method.LinearNodes[i];
                        if (!node.HasLoweringFlag(GenTreeLinearFlags.CallerSavedKill))
                            continue;
                        if (!nodePositions.TryGetValue(node.LinearId, out int position))
                            throw new InvalidOperationException("Missing GenTree LIR position for call node " + node.LinearId + ".");
                        positions.Add(position);
                    }
                }

                return positions.ToImmutableArray();
            }

            private static ImmutableArray<LinearRefPosition> BuildFixedKillRefPositions(
                GenTreeMethod method, Dictionary<int, int> nodePositions, RegisterAllocatorOptions options)
            {
                var kills = ImmutableArray.CreateBuilder<LinearRefPosition>();
                for (int i = 0; i < method.RefPositions.Length; i++)
                {
                    var rp = method.RefPositions[i];
                    if (rp.Kind == LinearRefPositionKind.Kill && rp.FixedRegister != MachineRegister.Invalid)
                        kills.Add(rp);
                }

                if (kills.Count == 0)
                {
                    for (int n = 0; n < method.LinearNodes.Length; n++)
                    {
                        var node = method.LinearNodes[n];
                        if (!node.HasLoweringFlag(GenTreeLinearFlags.CallerSavedKill))
                            continue;
                        if (!nodePositions.TryGetValue(node.LinearId, out int position))
                            throw new InvalidOperationException("Missing GenTree LIR position for caller-saved kill node " + node.LinearId + ".");

                        var killed = MachineRegisters.CallerSavedRegisters;
                        for (int r = 0; r < killed.Length; r++)
                        {
                            var register = killed[r];
                            kills.Add(new LinearRefPosition(
                                node.LinearId,
                                position,
                                -1,
                                LinearRefPositionKind.Kill,
                                null,
                                MachineRegisters.GetClass(register),
                                register,
                                LinearRefPositionFlags.FixedRegister | LinearRefPositionFlags.Internal));
                        }
                    }
                }

                kills.Sort(static (a, b) =>
                {
                    int c = a.Position.CompareTo(b.Position);
                    if (c != 0)
                        return c;
                    c = a.FixedRegister.CompareTo(b.FixedRegister);
                    if (c != 0)
                        return c;
                    return a.NodeId.CompareTo(b.NodeId);
                });
                return kills.ToImmutable();
            }

            private static bool IsAbiCall(GenTree node)
                => node.HasLoweringFlag(GenTreeLinearFlags.AbiCall);

            private int FirstRegisterConflictPosition(MachineRegister register, AllocationInterval current)
            {
                int conflict = int.MaxValue;

                for (int i = 0; i < _active.Count; i++)
                {
                    var active = _active[i];
                    if (active.AssignedRegister == register)
                    {
                        int p = active.FirstRegisterIntersection(current);
                        if (p < conflict)
                            conflict = p;
                    }
                }

                for (int i = 0; i < _inactive.Count; i++)
                {
                    var inactive = _inactive[i];
                    if (inactive.AssignedRegister == register)
                    {
                        int p = inactive.FirstRegisterIntersection(current);
                        if (p < conflict)
                            conflict = p;
                    }
                }

                return conflict;
            }

            private int NextUseOfBlockingIntervals(MachineRegister register, AllocationInterval current)
            {
                int nextUse = int.MaxValue;

                for (int i = 0; i < _active.Count; i++)
                {
                    var active = _active[i];
                    if (active.AssignedRegister == register && active.FirstRegisterIntersection(current) != int.MaxValue)
                    {
                        int use = active.NextUseAfterOrAt(current.Start);
                        if (use < nextUse)
                            nextUse = use;
                    }
                }

                for (int i = 0; i < _inactive.Count; i++)
                {
                    var inactive = _inactive[i];
                    if (inactive.AssignedRegister == register && inactive.FirstRegisterIntersection(current) != int.MaxValue)
                    {
                        int use = inactive.NextUseAfterOrAt(current.Start);
                        if (use < nextUse)
                            nextUse = use;
                    }
                }

                return nextUse;
            }

            private void SplitBlockingIntervals(MachineRegister register, AllocationInterval current, int currentRegisterEnd)
            {
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    var active = _active[i];
                    if (active.AssignedRegister == register)
                    {
                        int split = active.FirstRegisterIntersection(current, current.Start, currentRegisterEnd);
                        if (split != int.MaxValue)
                        {
                            _active.RemoveAt(i);
                            SplitToSpill(active, split);
                            _handled.Add(active);
                        }
                    }
                }

                for (int i = _inactive.Count - 1; i >= 0; i--)
                {
                    var inactive = _inactive[i];
                    if (inactive.AssignedRegister == register)
                    {
                        int split = inactive.FirstRegisterIntersection(current, current.Start, currentRegisterEnd);
                        if (split != int.MaxValue)
                        {
                            _inactive.RemoveAt(i);
                            SplitToSpill(inactive, split);
                            _handled.Add(inactive);
                        }
                    }
                }
            }

            private void AssignRegisterSegment(AllocationInterval interval, MachineRegister register, int segmentEnd)
            {
                if (!MachineRegisters.IsRegisterInClass(register, interval.RegisterClass))
                    throw new InvalidOperationException(
                        $"Cannot assign {MachineRegisters.Format(register)} to {interval.RegisterClass} interval {interval.Value}.");
                if (segmentEnd <= interval.Start)
                    throw new InvalidOperationException($"Register segment for {interval.Value} is empty.");
                if (interval.RequiresSingleLocation && segmentEnd < interval.End)
                    throw new InvalidOperationException($"Cannot partially split CFG-live interval {interval.Value}.");

                var registerHome = RegisterOperand.ForRegister(register);
                interval.AssignedRegister = register;
                interval.AssignedRegisterEnd = segmentEnd;
                interval.AddSegment(interval.Start, segmentEnd, registerHome);

                if (segmentEnd < interval.End)
                    interval.AddSegment(segmentEnd, interval.End, GetOrCreateSpillHome(interval));

                AssignHome(interval);

                if (interval.Covers(interval.Start))
                    _active.Add(interval);
                else
                    _inactive.Add(interval);
            }

            private void SplitToSpill(AllocationInterval interval, int splitPosition)
            {
                var spillHome = GetOrCreateSpillHome(interval);
                if (interval.RequiresSingleLocation)
                    interval.ReplaceWithSingleSegment(interval.Start, interval.End, spillHome);
                else
                    interval.SplitAssignedRegisterToSpill(splitPosition, spillHome);
                AssignHome(interval);
            }

            private void Spill(AllocationInterval interval)
            {
                interval.ReplaceWithSingleSegment(interval.Start, interval.End, GetOrCreateSpillHome(interval));
                AssignHome(interval);
                _handled.Add(interval);
            }

            private RegisterOperand GetOrCreateSpillHome(AllocationInterval interval)
            {
                if (interval.SpillSlot < 0)
                    interval.SpillSlot = _nextSpillSlot++;
                if (interval.IsAbiFragment)
                    return RegisterOperand.ForSpillSlot(interval.RegisterClass, interval.SpillSlot, 0, interval.AbiSegmentSize);
                if (interval.RequiresStackHome && interval.StackHomeSize > 0)
                    return RegisterOperand.ForSpillSlot(interval.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : interval.RegisterClass,
                        interval.SpillSlot, 0, interval.StackHomeSize);
                return RegisterOperand.ForSpillSlot(interval.RegisterClass, interval.SpillSlot);
            }

            private void AssignHome(AllocationInterval interval)
            {
                if (interval.IsAbiFragment)
                {
                    _allocations[interval.Value] = BuildFragmentedAllocation(interval.Value);
                    return;
                }

                RegisterOperand home = interval.RequiresStackHome
                    ? GetOrCreateSpillHome(interval)
                    : interval.PrimaryHome;

                _allocations[interval.Value] = new RegisterAllocationInfo(
                    interval.Value,
                    home,
                    interval.Ranges,
                    interval.UsePositions,
                    interval.DefinitionPosition,
                    interval.ToRegisterAllocationSegments());
            }

            private RegisterAllocationInfo BuildFragmentedAllocation(GenTree value)
            {
                if (!_intervalsByNode.TryGetValue(value, out var intervals) || intervals.Count == 0)
                    throw new InvalidOperationException($"Missing ABI fragment intervals for {value}.");

                var valueInfo = _method.GetValueInfo(value);
                var abi = MachineAbi.ClassifyValue(valueInfo.Type, valueInfo.StackKind, isReturn: false);
                var abiSegments = MachineAbi.GetRegisterSegments(abi);
                var fragments = ImmutableArray.CreateBuilder<RegisterAllocationFragment>(intervals.Count);

                for (int i = 0; i < intervals.Count; i++)
                {
                    var interval = intervals[i];
                    if (!interval.IsAbiFragment)
                        continue;
                    if ((uint)interval.AbiSegmentIndex >= (uint)abiSegments.Length)
                        throw new InvalidOperationException($"Invalid ABI fragment index {interval.AbiSegmentIndex} for {value}.");

                    fragments.Add(new RegisterAllocationFragment(
                        interval.AbiSegmentIndex,
                        abiSegments[interval.AbiSegmentIndex],
                        interval.PrimaryHome,
                        interval.ToRegisterAllocationSegments()));
                }

                if (!_aggregateHomes.TryGetValue(value, out var aggregateHome))
                    aggregateHome = RegisterOperand.None;

                return new RegisterAllocationInfo(
                    value,
                    aggregateHome,
                    intervals[0].Ranges,
                    intervals[0].UsePositions,
                    intervals[0].DefinitionPosition,
                    segments: ImmutableArray<RegisterAllocationSegment>.Empty,
                    fragments: fragments.ToImmutable());
            }

            private ImmutableArray<GenTreeBlock> EmitBlocks(
                Func<int> getCopyScratchSlot,
                SplitResolutionPlan splitPlan,
                out ImmutableArray<GenTree> allNodes)
            {
                var blockArray = new GenTreeBlock[_method.Blocks.Length];
                var all = ImmutableArray.CreateBuilder<GenTree>();

                for (int orderIndex = 0; orderIndex < _linearBlockOrder.Length; orderIndex++)
                {
                    int b = _linearBlockOrder[orderIndex];
                    var linearBlock = _method.Blocks[b];
                    var nodes = ImmutableArray.CreateBuilder<GenTree>(linearBlock.LinearNodes.Length);
                    var emittedSplitPositions = new HashSet<int>();
                    bool emittedExitMoves = false;

                    void EmitPositionSplitMoves(int position)
                    {
                        if (emittedSplitPositions.Add(position))
                            EmitSplitMovesAtPosition(b, position, getCopyScratchSlot, splitPlan.PositionMoves, nodes, all);
                    }

                    void EmitExitSplitMoves()
                    {
                        if (emittedExitMoves)
                            return;

                        emittedExitMoves = true;
                        EmitBlockSplitMoves(b, getCopyScratchSlot, splitPlan.BlockExitMoves, nodes, all);
                    }

                    EmitBlockSplitMoves(b, getCopyScratchSlot, splitPlan.BlockEntryMoves, nodes, all);
                    EmitPositionSplitMoves(_blockStartPositions[b]);

                    for (int n = 0; n < linearBlock.LinearNodes.Length; n++)
                    {
                        var node = linearBlock.LinearNodes[n];
                        int position = GetNodePosition(node);

                        EmitPositionSplitMoves(position);

                        if (IsBlockTerminatorNode(node))
                            EmitExitSplitMoves();

                        if (IsPhiCopyNode(node))
                        {
                            n = EmitPhiCopyGroup(
                                linearBlock.LinearNodes,
                                n,
                                b,
                                EmitPositionSplitMoves,
                                getCopyScratchSlot,
                                nodes,
                                all);
                        }
                        else if (node.LinearKind == GenTreeLinearKind.Copy)
                        {
                            EmitCopyNode(node, getCopyScratchSlot, nodes, all);
                        }
                        else if (node.LinearKind == GenTreeLinearKind.GcPoll)
                        {
                            EmitGcPollNode(node, nodes, all);
                        }
                        else
                        {
                            EmitTreeNodeSequence(node, nodes, all);
                        }

                        EmitPositionSplitMoves(position + 1);
                    }

                    EmitExitSplitMoves();

                    linearBlock.SetLinearNodes(nodes.ToImmutable());
                    blockArray[b] = linearBlock;
                }

                for (int b = 0; b < blockArray.Length; b++)
                {
                    if (blockArray[b] is null)
                    {
                        var emptyBlock = _method.Blocks[b];
                        emptyBlock.SetLinearNodes(ImmutableArray<GenTree>.Empty);
                        blockArray[b] = emptyBlock;
                    }
                }

                allNodes = all.ToImmutable();
                return ImmutableArray.Create(blockArray);
            }

            private sealed class SplitResolutionPlan
            {
                public readonly Dictionary<int, List<RegisterResolvedMove>> PositionMoves = new();
                public readonly Dictionary<int, List<RegisterResolvedMove>> BlockEntryMoves = new();
                public readonly Dictionary<int, List<RegisterResolvedMove>> BlockExitMoves = new();
            }

            private SplitResolutionPlan BuildSplitResolutionPlan()
            {
                var plan = new SplitResolutionPlan();

                foreach (var allocation in _allocations.Values)
                {
                    AddSplitTransitionMoves(plan, allocation.Value, allocation.Segments);

                    for (int f = 0; f < allocation.Fragments.Length; f++)
                        AddSplitTransitionMoves(plan, allocation.Value, allocation.Fragments[f].Segments);
                }

                return plan;
            }

            private void AddSplitTransitionMoves(
                SplitResolutionPlan plan,
                GenTree value,
                ImmutableArray<RegisterAllocationSegment> segments)
            {
                for (int i = 1; i < segments.Length; i++)
                {
                    var previous = segments[i - 1];
                    var current = segments[i];
                    if (previous.Location.Equals(current.Location))
                        continue;

                    if (TryGetBlockStartingAtPosition(current.Start, out int toBlockId))
                    {
                        AddCfgEdgeResolutionMoves(plan, value, segments, toBlockId);
                        continue;
                    }

                    AddMove(plan.PositionMoves, current.Start, new RegisterResolvedMove(
                        previous.Location,
                        current.Location,
                        value,
                        value,
                        MoveFlags.Split));
                }
            }

            private void AddCfgEdgeResolutionMoves(
                SplitResolutionPlan plan,
                GenTree value,
                ImmutableArray<RegisterAllocationSegment> segments,
                int toBlockId)
            {
                if ((uint)toBlockId >= (uint)_method.Cfg.Blocks.Length)
                    throw new InvalidOperationException($"Invalid split target block B{toBlockId}.");

                var toBlock = _method.Cfg.Blocks[toBlockId];
                int toPosition = _blockStartPositions[toBlockId];
                if (!TryGetLocationAt(segments, toPosition, out var destination))
                    return;

                for (int p = 0; p < toBlock.Predecessors.Length; p++)
                {
                    var edge = toBlock.Predecessors[p];
                    if (edge.Kind == CfgEdgeKind.Exception)
                    {
                        if (TryGetLocationAt(segments, _blockEndPositions[edge.FromBlockId], out var exceptionSource) &&
                            !exceptionSource.Equals(destination))
                        {
                            throw new InvalidOperationException(
                                $"Cannot resolve split of {value} on exception edge {edge}: exception edges cannot execute register moves. " +
                                "The value must remain stack-homed or have the same location at both sides of the edge.");
                        }
                        continue;
                    }

                    int fromPosition = _blockEndPositions[edge.FromBlockId];
                    if (!TryGetLocationAt(segments, fromPosition, out var source))
                        continue;
                    if (source.Equals(destination))
                        continue;

                    var move = new RegisterResolvedMove(source, destination, value, value, MoveFlags.Split);
                    AddNormalEdgeMove(plan, edge, move);
                }
            }

            private void AddNormalEdgeMove(SplitResolutionPlan plan, CfgEdge edge, RegisterResolvedMove move)
            {
                int normalPreds = CountNormalPredecessors(edge.ToBlockId);
                int normalSuccs = CountNormalSuccessors(edge.FromBlockId);

                if (normalPreds == 1)
                {
                    AddMove(plan.BlockEntryMoves, edge.ToBlockId, move);
                    return;
                }

                if (normalSuccs == 1)
                {
                    AddMove(plan.BlockExitMoves, edge.FromBlockId, move);
                    return;
                }

                throw new InvalidOperationException(
                    $"Cannot resolve LSRA split move on unsplit critical edge {edge}: " +
                    $"B{edge.FromBlockId} has {normalSuccs} normal successors and B{edge.ToBlockId} has {normalPreds} normal predecessors.");
            }

            private int CountNormalPredecessors(int blockId)
            {
                int count = 0;
                var predecessors = _method.Cfg.Blocks[blockId].Predecessors;
                for (int i = 0; i < predecessors.Length; i++)
                {
                    if (predecessors[i].Kind != CfgEdgeKind.Exception)
                        count++;
                }
                return count;
            }

            private int CountNormalSuccessors(int blockId)
            {
                int count = 0;
                var successors = _method.Cfg.Blocks[blockId].Successors;
                for (int i = 0; i < successors.Length; i++)
                {
                    if (successors[i].Kind != CfgEdgeKind.Exception)
                        count++;
                }
                return count;
            }

            private bool TryGetBlockStartingAtPosition(int position, out int blockId)
            {
                for (int b = 0; b < _blockStartPositions.Length; b++)
                {
                    if (_blockStartPositions[b] == position)
                    {
                        blockId = b;
                        return true;
                    }
                }

                blockId = -1;
                return false;
            }

            private static bool TryGetLocationAt(
                ImmutableArray<RegisterAllocationSegment> segments,
                int position,
                out RegisterOperand location)
            {
                for (int i = 0; i < segments.Length; i++)
                {
                    var segment = segments[i];
                    if (segment.Contains(position))
                    {
                        location = segment.Location;
                        return true;
                    }
                }

                location = RegisterOperand.None;
                return false;
            }

            private static void AddMove(
                Dictionary<int, List<RegisterResolvedMove>> movesByKey,
                int key,
                RegisterResolvedMove move)
            {
                if (!movesByKey.TryGetValue(key, out var moves))
                {
                    moves = new List<RegisterResolvedMove>();
                    movesByKey.Add(key, moves);
                }

                moves.Add(move);
            }

            private void EmitBlockSplitMoves(
                int blockId,
                Func<int> getCopyScratchSlot,
                Dictionary<int, List<RegisterResolvedMove>> splitMovesByBlock,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (!splitMovesByBlock.TryGetValue(blockId, out var moves) || moves.Count == 0)
                    return;

                EmitResolvedSplitMoves(
                    blockId,
                    blockId,
                    getCopyScratchSlot,
                    moves,
                    blockLinearNodes,
                    allNodes);
            }

            private void EmitSplitMovesAtPosition(
                int blockId,
                int position,
                Func<int> getCopyScratchSlot,
                Dictionary<int, List<RegisterResolvedMove>> splitMoves,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (!splitMoves.TryGetValue(position, out var moves) || moves.Count == 0)
                    return;

                EmitResolvedSplitMoves(
                    blockId,
                    blockId,
                    getCopyScratchSlot,
                    moves,
                    blockLinearNodes,
                    allNodes);
            }

            private void EmitResolvedSplitMoves(
                int insertionBlockId,
                int toBlockId,
                Func<int> getCopyScratchSlot,
                List<RegisterResolvedMove> moves,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                var resolved = RegisterParallelCopyResolver.Resolve(
                    insertionBlockId,
                    toBlockId,
                    new List<RegisterResolvedMove>(moves),
                    getCopyScratchSlot,
                    _options.ParallelCopyScratchRegister0,
                    _options.ParallelCopyFloatScratchRegister0,
                    ref _nextNodeId,
                    CanEmitDirectMemoryMove);

                for (int i = 0; i < resolved.Length; i++)
                {
                    var node = resolved[i].WithOrdinal(blockLinearNodes.Count);
                    blockLinearNodes.Add(node);
                    allNodes.Add(node);
                }
            }

            private static bool IsBlockTerminatorNode(GenTree node)
                => node.Kind is GenTreeKind.Branch or GenTreeKind.BranchTrue or GenTreeKind.BranchFalse or
                   GenTreeKind.Return or GenTreeKind.Throw or GenTreeKind.Rethrow or GenTreeKind.EndFinally;

            private RegisterOperand GetIncomingArgumentOperand(int argumentIndex, RuntimeType? argumentType, GenStackKind stackKind, RegisterClass argumentClass)
            {
                var abi = MachineAbi.ClassifyValue(argumentType, stackKind, isReturn: false);
                var operands = GetIncomingArgumentOperands(argumentIndex, argumentType, stackKind, argumentClass, abi);
                if (operands.Length == 1)
                    return operands[0];

                throw new InvalidOperationException("Incoming aggregate argument requires segment-aware operand enumeration.");
            }

            private ImmutableArray<RegisterOperand> GetIncomingArgumentOperands(
                int argumentIndex,
                RuntimeType? argumentType,
                GenStackKind stackKind,
                RegisterClass argumentClass,
                AbiValueInfo argumentAbi)
            {
                int general = 0;
                int floating = 0;
                int incomingStackSlot = 0;
                int hiddenReturnBufferInsertionIndex = MachineAbi.HiddenReturnBufferInsertionIndex(
                    _method.RuntimeMethod,
                    _method.ArgTypes.Length);

                for (int i = 0; i <= argumentIndex; i++)
                {
                    if (hiddenReturnBufferInsertionIndex == i)
                        ConsumeHiddenReturnBuffer(ref general, ref incomingStackSlot);

                    RuntimeType currentType = _method.ArgTypes[i];
                    GenStackKind currentStackKind = i == argumentIndex ? stackKind : StackKindForAbi(currentType);
                    var abi = i == argumentIndex
                        ? argumentAbi
                        : MachineAbi.ClassifyValue(currentType, currentStackKind, isReturn: false);

                    if (abi.PassingKind == AbiValuePassingKind.Void)
                        continue;

                    if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                    {
                        MachineRegister reg = GetMaybeArgumentRegister(abi.RegisterClass, ref general, ref floating);
                        int abiStackSlot = -1;
                        if (reg == MachineRegister.Invalid)
                            abiStackSlot = incomingStackSlot++;

                        if (i == argumentIndex)
                        {
                            if (reg != MachineRegister.Invalid)
                                return ImmutableArray.Create(RegisterOperand.ForRegister(reg));

                            return ImmutableArray.Create(ForIncomingAbiStackSlot(
                                abi.RegisterClass == RegisterClass.Invalid ? argumentClass : abi.RegisterClass,
                                abiStackSlot,
                                0,
                                Math.Max(1, abi.Size)));
                        }
                        continue;
                    }

                    if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        var segments = MachineAbi.GetRegisterSegments(abi);
                        int baseIncomingSlot = -1;

                        var operands = ImmutableArray.CreateBuilder<RegisterOperand>(segments.Length);
                        for (int s = 0; s < segments.Length; s++)
                        {
                            var segment = segments[s];
                            var reg = GetMaybeArgumentRegister(segment.RegisterClass, ref general, ref floating);
                            if (reg != MachineRegister.Invalid)
                            {
                                operands.Add(RegisterOperand.ForRegister(reg));
                            }
                            else
                            {
                                if (baseIncomingSlot < 0)
                                    baseIncomingSlot = incomingStackSlot++;
                                operands.Add(ForIncomingAbiStackSlot(segment.RegisterClass, baseIncomingSlot, segment.Offset, segment.Size));
                            }
                        }

                        if (i == argumentIndex)
                            return operands.ToImmutable();
                        continue;
                    }

                    int aggregateStackSlot = incomingStackSlot++;
                    if (i == argumentIndex)
                    {
                        return ImmutableArray.Create(ForIncomingAbiStackSlot(
                            argumentClass == RegisterClass.Invalid ? RegisterClass.General : argumentClass,
                            aggregateStackSlot,
                            0,
                            abi.Size <= 0 ? TargetArchitecture.PointerSize : abi.Size));
                    }
                }

                throw new InvalidOperationException($"Invalid incoming argument index {argumentIndex}.");
            }

            private static RegisterOperand ForIncomingAbiStackSlot(
                RegisterClass registerClass,
                int abiStackSlot,
                int offset,
                int size)
            {
                if (abiStackSlot < 0)
                    throw new ArgumentOutOfRangeException(nameof(abiStackSlot));

                return RegisterOperand.ForFrameSlot(
                    registerClass == RegisterClass.Invalid ? RegisterClass.General : registerClass,
                    StackFrameSlotKind.Argument,
                    RegisterFrameBase.IncomingArgumentBase,
                    abiStackSlot,
                    checked(abiStackSlot * MachineAbi.StackArgumentSlotSize + offset),
                    size <= 0 ? TargetArchitecture.PointerSize : size);
            }

            private static void ConsumeHiddenReturnBuffer(ref int generalIndex, ref int incomingStackSlot)
            {
                var retBufferRegister = MachineRegisters.GetIntegerArgumentRegister(generalIndex++);
                if (retBufferRegister == MachineRegister.Invalid)
                    incomingStackSlot++;
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

            private void EmitGcPollNode(
                GenTree node,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                var pollNode = GenTreeLirFactory.GcPoll(
                    _nextNodeId++,
                    node.LinearBlockId,
                    blockLinearNodes.Count,
                    node.LinearId,
                    source: node,
                    comment: "loop backedge GC poll");
                blockLinearNodes.Add(pollNode);
                allNodes.Add(pollNode);
            }

            private static bool IsPhiCopyNode(GenTree node)
                => node.IsPhiCopy;

            private static bool IsSamePhiCopyGroup(GenTree left, GenTree right)
                => left.IsPhiCopy &&
                   right.IsPhiCopy &&
                   left.LinearPhiCopyFromBlockId == right.LinearPhiCopyFromBlockId &&
                   left.LinearPhiCopyToBlockId == right.LinearPhiCopyToBlockId;

            private readonly struct PendingAggregateHomeStore
            {
                public readonly GenTree Value;
                public readonly int Position;
                public readonly AbiValueInfo Abi;
                public readonly RegisterValueLocation Fragments;

                public PendingAggregateHomeStore(GenTree value, int position, AbiValueInfo abi, RegisterValueLocation fragments)
                {
                    Value = value;
                    Position = position;
                    Abi = abi;
                    Fragments = fragments;
                }
            }

            private int EmitPhiCopyGroup(
                ImmutableArray<GenTree> nodes,
                int firstIndex,
                int blockId,
                Action<int> emitBlockSplitMoves,
                Func<int> getCopyScratchSlot,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                var groupHead = nodes[firstIndex];
                var moves = new List<RegisterResolvedMove>();
                var aggregateHomeStores = new List<PendingAggregateHomeStore>();
                int lastIndex = firstIndex;

                for (int i = firstIndex; i < nodes.Length && IsSamePhiCopyGroup(groupHead, nodes[i]); i++)
                {
                    var node = nodes[i];
                    int position = GetNodePosition(node);
                    emitBlockSplitMoves(position);

                    if (!TryBuildPhiCopyMoves(node, position, moves, aggregateHomeStores))
                    {
                        EmitResolvedPhiCopyMoves(
                            blockId,
                            groupHead.LinearPhiCopyFromBlockId,
                            groupHead.LinearPhiCopyToBlockId,
                            moves,
                            getCopyScratchSlot,
                            blockLinearNodes,
                            allNodes);
                        moves.Clear();
                        EmitPendingAggregateHomeStores(blockId, aggregateHomeStores, blockLinearNodes, allNodes);
                        aggregateHomeStores.Clear();
                        EmitCopyNode(node, getCopyScratchSlot, blockLinearNodes, allNodes);
                    }

                    lastIndex = i;
                }

                EmitResolvedPhiCopyMoves(
                    blockId,
                    groupHead.LinearPhiCopyFromBlockId,
                    groupHead.LinearPhiCopyToBlockId,
                    moves,
                    getCopyScratchSlot,
                    blockLinearNodes,
                    allNodes);
                EmitPendingAggregateHomeStores(blockId, aggregateHomeStores, blockLinearNodes, allNodes);

                for (int i = firstIndex; i <= lastIndex; i++)
                {
                    int position = GetNodePosition(nodes[i]);
                    emitBlockSplitMoves(position + 1);
                }

                return lastIndex;
            }

            private void EmitPendingAggregateHomeStores(
                int blockId,
                List<PendingAggregateHomeStore> stores,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                for (int i = 0; i < stores.Count; i++)
                {
                    var store = stores[i];
                    EmitAggregateHomeStores(
                        blockId,
                        store.Value,
                        store.Position,
                        store.Abi,
                        store.Fragments,
                        "phi aggregate home",
                        blockLinearNodes,
                        allNodes);
                }
            }

            private void EmitResolvedPhiCopyMoves(
                int blockId,
                int fromBlockId,
                int toBlockId,
                List<RegisterResolvedMove> moves,
                Func<int> getCopyScratchSlot,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (moves.Count == 0)
                    return;

                var resolved = RegisterParallelCopyResolver.Resolve(
                    fromBlockId,
                    toBlockId,
                    new List<RegisterResolvedMove>(moves),
                    getCopyScratchSlot,
                    _options.ParallelCopyScratchRegister0,
                    _options.ParallelCopyFloatScratchRegister0,
                    ref _nextNodeId,
                    CanEmitDirectMemoryMove,
                    phiCopyFromBlockId: fromBlockId,
                    phiCopyToBlockId: toBlockId,
                    preserveIdentityMoves: true);

                for (int i = 0; i < resolved.Length; i++)
                {
                    var node = resolved[i].WithPlacement(blockId, blockLinearNodes.Count);
                    blockLinearNodes.Add(node);
                    allNodes.Add(node);
                }
            }

            private void EmitResolvedCopyMoves(
                int blockId,
                List<RegisterResolvedMove> moves,
                Func<int> getCopyScratchSlot,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (moves.Count == 0)
                    return;

                var resolved = RegisterParallelCopyResolver.Resolve(
                    blockId,
                    blockId,
                    new List<RegisterResolvedMove>(moves),
                    getCopyScratchSlot,
                    _options.ParallelCopyScratchRegister0,
                    _options.ParallelCopyFloatScratchRegister0,
                    ref _nextNodeId,
                    CanEmitDirectMemoryMove);

                for (int i = 0; i < resolved.Length; i++)
                {
                    var node = resolved[i].WithOrdinal(blockLinearNodes.Count);
                    blockLinearNodes.Add(node);
                    allNodes.Add(node);
                }
            }

            private bool TryBuildPhiCopyMoves(
                GenTree node,
                int position,
                List<RegisterResolvedMove> moves,
                List<PendingAggregateHomeStore> aggregateHomeStores)
            {
                if (node.RegisterResult is null || node.RegisterUses.Length != 1)
                    return false;

                var destinationValue = node.RegisterResult;
                var sourceValue = node.RegisterUses[0];
                var destinationInfo = _method.GetValueInfo(destinationValue);
                var sourceInfo = _method.GetValueInfo(sourceValue);
                var destinationAbi = MachineAbi.ClassifyStorageValue(destinationInfo.Type, destinationInfo.StackKind);
                var sourceAbi = MachineAbi.ClassifyStorageValue(sourceInfo.Type, sourceInfo.StackKind);

                if (destinationAbi.PassingKind == AbiValuePassingKind.MultiRegister ||
                    sourceAbi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    if (destinationAbi.PassingKind != AbiValuePassingKind.MultiRegister ||
                        sourceAbi.PassingKind != AbiValuePassingKind.MultiRegister)
                    {
                        throw new InvalidOperationException(
                            $"Cannot lower scalar/aggregate phi copy as a parallel copy: {sourceValue} -> {destinationValue}.");
                    }

                    var destinationLocation = ValueLocationForDefinition(destinationValue, position + 1, destinationAbi);
                    var sourceLocation = ValueLocationForUse(sourceValue, position, sourceAbi);
                    if (destinationLocation.Count != sourceLocation.Count)
                    {
                        throw new InvalidOperationException(
                            $"Cannot lower multi-register phi copy with different ABI segment counts: {sourceValue} -> {destinationValue}.");
                    }

                    for (int i = 0; i < destinationLocation.Count; i++)
                    {
                        var destination = destinationLocation[i];
                        var source = sourceLocation[i];
                        if (destination.IsNone)
                            continue;
                        if (source.IsNone)
                        {
                            throw new InvalidOperationException(
                                $"Cannot resolve phi copy {sourceValue} -> {destinationValue}: source ABI segment {i} has no physical home at B{node.LinearPhiCopyFromBlockId}->B{node.LinearPhiCopyToBlockId}.");
                        }

                        moves.Add(new RegisterResolvedMove(
                            source,
                            destination,
                            sourceValue,
                            destinationValue,
                            MoveFlags.ParallelCopy));
                    }

                    aggregateHomeStores.Add(new PendingAggregateHomeStore(
                        destinationValue,
                        position + 1,
                        destinationAbi,
                        destinationLocation));
                    return true;
                }

                var scalarDestination = HomeForDefinition(destinationValue, position + 1);
                if (scalarDestination.IsNone)
                    return true;

                var scalarSource = HomeForUse(sourceValue, position);
                if (scalarSource.IsNone)
                {
                    throw new InvalidOperationException(
                        $"Cannot resolve phi copy {sourceValue} -> {destinationValue}: source value has no physical home at B{node.LinearPhiCopyFromBlockId}->B{node.LinearPhiCopyToBlockId}.");
                }

                moves.Add(new RegisterResolvedMove(
                    scalarSource,
                    scalarDestination,
                    sourceValue,
                    destinationValue,
                    MoveFlags.ParallelCopy));

                return true;
            }

            private void EmitCopyNode(
                GenTree node,
                Func<int> getCopyScratchSlot,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (node.RegisterResult is null || node.RegisterUses.Length != 1)
                    return;

                int position = GetNodePosition(node);
                EmitValueCopy(
                    node.LinearBlockId,
                    node.RegisterResult,
                    position + 1,
                    node.RegisterUses[0],
                    position,
                    "linear copy",
                    getCopyScratchSlot,
                    blockLinearNodes,
                    allNodes);
            }

            private void EmitValueCopy(
                int blockId,
                GenTree destinationValue,
                int destinationPosition,
                GenTree sourceValue,
                int sourcePosition,
                string comment,
                Func<int> getCopyScratchSlot,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                var destinationInfo = _method.GetValueInfo(destinationValue);
                var sourceInfo = _method.GetValueInfo(sourceValue);
                var destinationAbi = MachineAbi.ClassifyStorageValue(destinationInfo.Type, destinationInfo.StackKind);
                var sourceAbi = MachineAbi.ClassifyStorageValue(sourceInfo.Type, sourceInfo.StackKind);

                if (destinationAbi.PassingKind == AbiValuePassingKind.MultiRegister &&
                    sourceAbi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    var destinationLocation = ValueLocationForDefinition(destinationValue, destinationPosition, destinationAbi);
                    var sourceLocation = ValueLocationForUse(sourceValue, sourcePosition, sourceAbi);
                    if (destinationLocation.Count != sourceLocation.Count)
                        throw new InvalidOperationException(
                            $"Cannot copy multi-register values with different ABI segment counts: {sourceValue} -> {destinationValue}.");

                    var moves = new List<RegisterResolvedMove>();
                    for (int i = 0; i < destinationLocation.Count; i++)
                    {
                        var destination = destinationLocation[i];
                        var source = sourceLocation[i];
                        if (destination.IsNone || source.IsNone || source.Equals(destination))
                            continue;

                        moves.Add(new RegisterResolvedMove(
                            source,
                            destination,
                            sourceValue,
                            destinationValue,
                            MoveFlags.ParallelCopy));
                    }

                    EmitResolvedCopyMoves(blockId, moves, getCopyScratchSlot, blockLinearNodes, allNodes);

                    EmitAggregateHomeStores(
                        blockId,
                        destinationValue,
                        destinationPosition,
                        destinationAbi,
                        destinationLocation,
                        comment + " aggregate home",
                        blockLinearNodes,
                        allNodes);
                    return;
                }

                var scalarDestination = HomeForDefinition(destinationValue, destinationPosition);
                if (scalarDestination.IsNone)
                    return;

                var scalarSource = HomeForUse(sourceValue, sourcePosition);
                if (scalarSource.Equals(scalarDestination))
                    return;

                EmitMoveSequence(
                    blockId,
                    scalarDestination,
                    scalarSource,
                    destinationValue,
                    sourceValue,
                    comment,
                    blockLinearNodes,
                    allNodes);
            }

            private void EmitMoveSequence(
                int blockId,
                RegisterOperand destination,
                RegisterOperand source,
                GenTree? destinationValue,
                GenTree? sourceValue,
                string comment,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes,
                MoveFlags moveFlags = MoveFlags.None)
            {
                if (destination.IsNone || source.IsNone)
                    return;

                if (!RequiresScratchForMove(source, destination) || CanEmitDirectMemoryMove(destination, source, destinationValue, sourceValue))
                {
                    var direct = GenTreeLirFactory.Move(
                        _nextNodeId++,
                        blockId,
                        blockLinearNodes.Count,
                        destination,
                        source,
                        destinationValue,
                        sourceValue,
                        comment,
                        moveFlags);
                    blockLinearNodes.Add(direct);
                    allNodes.Add(direct);
                    return;
                }

                var scratchClass = RegisterClassForMove(source, destination);
                var scratch = RegisterOperand.ForRegister(GetScratchRegisterForClass(scratchClass));
                var load = GenTreeLirFactory.Move(
                    _nextNodeId++,
                    blockId,
                    blockLinearNodes.Count,
                    scratch,
                    source,
                    destinationValue: null,
                    sourceValue: sourceValue,
                    comment: comment + " reload",
                    moveFlags: moveFlags | MoveFlags.Reload);
                blockLinearNodes.Add(load);
                allNodes.Add(load);

                var store = GenTreeLirFactory.Move(
                    _nextNodeId++,
                    blockId,
                    blockLinearNodes.Count,
                    destination,
                    scratch,
                    destinationValue,
                    sourceValue: null,
                    comment: comment + " store",
                    moveFlags: moveFlags | MoveFlags.Spill);
                blockLinearNodes.Add(store);
                allNodes.Add(store);
            }

            private void EmitAggregateHomeStores(
                int blockId,
                GenTree value,
                int position,
                AbiValueInfo abi,
                RegisterValueLocation fragments,
                string comment,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (abi.PassingKind != AbiValuePassingKind.MultiRegister || !fragments.IsFragmented)
                    return;

                var aggregate = HomeForDefinition(value, position);
                if (aggregate.IsNone)
                    return;

                var segments = MachineAbi.GetRegisterSegments(abi);
                for (int i = 0; i < segments.Length; i++)
                {
                    var destination = OperandAtOffset(aggregate, segments[i]);
                    var source = fragments[i];
                    if (source.IsNone || destination.IsNone || source.Equals(destination))
                        continue;

                    EmitMoveSequence(
                        blockId,
                        destination,
                        source,
                        value,
                        value,
                        comment + " fragment " + i.ToString(),
                        blockLinearNodes,
                        allNodes);
                }
            }

            private void EmitFragmentReloadsFromAggregateHome(
                int blockId,
                GenTree value,
                int position,
                AbiValueInfo abi,
                RegisterValueLocation fragments,
                string comment,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (abi.PassingKind != AbiValuePassingKind.MultiRegister || !fragments.IsFragmented)
                    return;

                var aggregate = HomeForDefinition(value, position);
                if (aggregate.IsNone)
                    return;

                var segments = MachineAbi.GetRegisterSegments(abi);
                for (int i = 0; i < segments.Length; i++)
                {
                    var destination = fragments[i];
                    var source = OperandAtOffset(aggregate, segments[i]);
                    if (destination.IsNone || source.Equals(destination))
                        continue;

                    EmitMoveSequence(
                        blockId,
                        destination,
                        source,
                        value,
                        value,
                        comment + " fragment " + i.ToString(),
                        blockLinearNodes,
                        allNodes);
                }
            }


            private bool CanEmitDirectMemoryMove(
                RegisterOperand destination,
                RegisterOperand source,
                GenTree? destinationValue,
                GenTree? sourceValue)
            {
                if (!destination.IsMemoryOperand || !source.IsMemoryOperand)
                    return false;

                if (!IsWideMemoryOperand(destination) && !IsWideMemoryOperand(source))
                    return false;

                if (destinationValue is not null && IsBlockCopyValue(destinationValue))
                    return true;
                if (sourceValue is not null && IsBlockCopyValue(sourceValue))
                    return true;
                return false;
            }

            private static bool IsWideMemoryOperand(RegisterOperand operand)
                => operand.IsMemoryOperand && operand.FrameSlotSize > MachineAbi.GeneralRegisterSlotSize;

            private bool IsBlockCopyValue(GenTree value)
            {
                var valueInfo = _method.GetValueInfo(value);
                return MachineAbi.IsBlockCopyValue(valueInfo.Type, valueInfo.StackKind);
            }

            private MachineRegister GetScratchRegisterForClass(RegisterClass registerClass)
            {
                if (registerClass is not (RegisterClass.General or RegisterClass.Float))
                    throw new InvalidOperationException($"Cannot select a scratch register for {registerClass} move.");

                var scratch = registerClass == RegisterClass.Float
                    ? _options.ParallelCopyFloatScratchRegister0
                    : _options.ParallelCopyScratchRegister0;

                ValidateReservedScratch(scratch, registerClass);
                return scratch;
            }

            private MachineRegister GetTreeScratchRegister(RegisterClass registerClass, int index)
            {
                var scratchPool = registerClass switch
                {
                    RegisterClass.General => MachineRegisters.TreeScratchGprs,
                    RegisterClass.Float => MachineRegisters.TreeScratchFprs,
                    _ => ImmutableArray<MachineRegister>.Empty,
                };

                var scratch = (uint)index < (uint)scratchPool.Length
                    ? scratchPool[index]
                    : MachineRegister.Invalid;

                if (scratch == MachineRegister.Invalid)
                    throw new InvalidOperationException($"Not enough reserved scratch registers to normalize a {registerClass} tree node.");

                ValidateReservedScratch(scratch, registerClass);
                return scratch;
            }

            private void ValidateReservedScratch(MachineRegister scratch, RegisterClass registerClass)
            {
                if (!MachineRegisters.IsRegisterInClass(scratch, registerClass))
                    throw new InvalidOperationException($"Invalid scratch register {scratch} for {registerClass} move.");
                if (IsAllocatable(scratch))
                    throw new InvalidOperationException($"Scratch register {MachineRegisters.Format(scratch)} must not be allocatable.");
            }

            private static RegisterClass RegisterClassForMove(RegisterOperand source, RegisterOperand destination)
            {
                if (destination.RegisterClass is RegisterClass.General or RegisterClass.Float)
                    return destination.RegisterClass;
                if (source.RegisterClass is RegisterClass.General or RegisterClass.Float)
                    return source.RegisterClass;

                throw new InvalidOperationException($"Invalid move without a concrete register class: {source} -> {destination}.");
            }

            private static bool RequiresScratchForMove(RegisterOperand source, RegisterOperand destination)
                => !source.IsNone && !destination.IsNone && !source.IsRegister && !destination.IsRegister;

            private void EmitTreeNodeSequence(
                GenTree node,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (false)
                    throw new InvalidOperationException("GenTree LIR tree node has no GenTree source.");

                if (IsAbiCall(node))
                {
                    EmitCallLikeTreeNode(node, blockLinearNodes, allNodes);
                    return;
                }

                if (node.Kind == GenTreeKind.Return)
                {
                    EmitReturnTreeNode(node, blockLinearNodes, allNodes);
                    return;
                }

                if (node.HasLoweringFlag(GenTreeLinearFlags.RequiresRegisterOperands))
                    EmitRegisterOnlyTreeNode(node, blockLinearNodes, allNodes);
                else
                {
                    EmitMemoryConstrainedTreeNode(node, blockLinearNodes, allNodes);

                    if (node.RegisterResult is not null)
                    {
                        int definitionPosition = GetNodePosition(node) + 1;
                        var resultValue = node.RegisterResult;
                        var resultInfo = _method.GetValueInfo(resultValue);
                        var abi = MachineAbi.ClassifyStorageValue(resultInfo.Type, resultInfo.StackKind);
                        if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                        {
                            EmitFragmentReloadsFromAggregateHome(
                                node.LinearBlockId,
                                resultValue,
                                definitionPosition,
                                abi,
                                ValueLocationForDefinition(resultValue, definitionPosition, abi),
                                "tree multi-register result reload",
                                blockLinearNodes,
                                allNodes);
                        }
                    }
                }
            }

            private void EmitReturnTreeNode(
                GenTree node,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (node.RegisterUses.Length == 0)
                {
                    var voidReturn = GenTreeLirFactory.Tree(
                        _nextNodeId++,
                        node.LinearBlockId,
                        blockLinearNodes.Count,
                        node,
                        RegisterOperand.None,
                        ImmutableArray<RegisterOperand>.Empty,
                        (GenTree?)null,
                        linearUses: ImmutableArray<GenTree>.Empty,
                        linearId: node.LinearId,
                        linearOperands: node.OperandFlags);
                    blockLinearNodes.Add(voidReturn);
                    allNodes.Add(voidReturn);
                    return;
                }

                if (node.RegisterUses.Length != 1)
                    throw new InvalidOperationException("Return node must have zero or one value use.");

                int position = GetNodePosition(node);
                var value = node.RegisterUses[0];
                var valueInfo = _method.GetValueInfo(value);
                var abi = MachineAbi.ClassifyValue(valueInfo.Type, valueInfo.StackKind, isReturn: true);
                var sourceLocation = ValueLocationForUse(value, position, abi);
                var sourceOperand = sourceLocation.IsScalar ? sourceLocation.Scalar : HomeForUse(value, position);

                if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                {
                    var returnOperand = RegisterOperand.ForRegister(GetReturnRegister(abi.RegisterClass));

                    if (!sourceOperand.Equals(returnOperand))
                    {
                        EmitMoveSequence(
                            node.LinearBlockId,
                            returnOperand,
                            sourceOperand,
                            destinationValue: null,
                            sourceValue: value,
                            comment: "return value to ABI register",
                            blockLinearNodes,
                            allNodes);
                    }

                    var returnNode = GenTreeLirFactory.Tree(
                        _nextNodeId++,
                        node.LinearBlockId,
                        blockLinearNodes.Count,
                        node,
                        RegisterOperand.None,
                        ImmutableArray.Create(returnOperand),
                        (GenTree?)null,
                        linearUses: node.RegisterUses,
                        linearId: node.LinearId,
                        linearOperands: node.OperandFlags);
                    blockLinearNodes.Add(returnNode);
                    allNodes.Add(returnNode);
                    return;
                }

                if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    var returnOperands = ImmutableArray.CreateBuilder<RegisterOperand>();
                    var returnUses = ImmutableArray.CreateBuilder<GenTree>();
                    var segments = MachineAbi.GetRegisterSegments(abi);
                    int generalReturnIndex = 0;
                    int floatReturnIndex = 0;

                    for (int i = 0; i < segments.Length; i++)
                    {
                        var segment = segments[i];
                        var target = RegisterOperand.ForRegister(GetReturnRegister(segment.RegisterClass, ref generalReturnIndex, ref floatReturnIndex));
                        var source = sourceLocation.IsFragmented ? sourceLocation[i] : OperandAtOffset(sourceOperand, segment);

                        if (!source.Equals(target))
                        {
                            EmitMoveSequence(
                                node.LinearBlockId,
                                target,
                                source,
                                destinationValue: null,
                                sourceValue: value,
                                comment: "return struct fragment to ABI register",
                                blockLinearNodes,
                                allNodes);
                        }

                        returnOperands.Add(target);
                        returnUses.Add(value);
                    }

                    var returnNode = GenTreeLirFactory.Tree(
                        _nextNodeId++,
                        node.LinearBlockId,
                        blockLinearNodes.Count,
                        node,
                        RegisterOperand.None,
                        returnOperands.ToImmutable(),
                        (GenTree?)null,
                        linearUses: returnUses.ToImmutable(),
                        linearId: node.LinearId,
                        linearOperands: node.OperandFlags);
                    blockLinearNodes.Add(returnNode);
                    allNodes.Add(returnNode);
                    return;
                }

                var indirectReturnNode = GenTreeLirFactory.Tree(
                    _nextNodeId++,
                    node.LinearBlockId,
                    blockLinearNodes.Count,
                    node,
                    RegisterOperand.None,
                    ImmutableArray.Create(sourceOperand),
                    (GenTree?)null,
                    linearUses: node.RegisterUses,
                    linearId: node.LinearId,
                    linearOperands: node.OperandFlags);
                blockLinearNodes.Add(indirectReturnNode);
                allNodes.Add(indirectReturnNode);
            }

            private void EmitRegisterOnlyTreeNode(
                GenTree node,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                int position = GetNodePosition(node);
                int definitionPosition = position + 1;
                var useOperands = ImmutableArray.CreateBuilder<RegisterOperand>(node.RegisterUses.Length);
                var expandedRegisterUses = ImmutableArray.CreateBuilder<GenTree>(node.RegisterUses.Length);
                var resultOperands = ImmutableArray.CreateBuilder<RegisterOperand>();
                var resultGenTrees = ImmutableArray.CreateBuilder<GenTree>();
                var postTreeStores = new List<RegisterResolvedMove>();
                var scratchUseCounts = new Dictionary<RegisterClass, int>();

                ReserveInternalScratchRegisters(node, scratchUseCounts);

                for (int i = 0; i < node.RegisterUses.Length; i++)
                {
                    var value = node.RegisterUses[i];
                    var valueInfo = _method.GetValueInfo(value);
                    var abi = MachineAbi.ClassifyStorageValue(valueInfo.Type, valueInfo.StackKind);

                    if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        var location = ValueLocationForUse(value, position, abi);
                        var segments = MachineAbi.GetRegisterSegments(abi);
                        if (location.Count != segments.Length)
                            throw new InvalidOperationException($"Multi-register use location count does not match ABI segment count for {value}.");

                        for (int s = 0; s < segments.Length; s++)
                        {
                            var home = location[s];
                            if (home.IsRegister)
                            {
                                useOperands.Add(home);
                                expandedRegisterUses.Add(value);
                                continue;
                            }

                            int scratchIndex = NextScratchIndex(scratchUseCounts, segments[s].RegisterClass);
                            var scratch = RegisterOperand.ForRegister(GetTreeScratchRegister(segments[s].RegisterClass, scratchIndex));
                            EmitMoveSequence(
                                node.LinearBlockId,
                                scratch,
                                home,
                                destinationValue: null,
                                sourceValue: value,
                                "tree multi-register operand reload",
                                blockLinearNodes,
                                allNodes);
                            useOperands.Add(scratch);
                            expandedRegisterUses.Add(value);
                        }
                        continue;
                    }

                    var scalarHome = HomeForUse(value, position);
                    if (scalarHome.IsRegister)
                    {
                        useOperands.Add(scalarHome);
                        expandedRegisterUses.Add(value);
                        continue;
                    }

                    int scalarScratchIndex = NextScratchIndex(scratchUseCounts, scalarHome.RegisterClass);
                    var scalarScratch = RegisterOperand.ForRegister(GetTreeScratchRegister(scalarHome.RegisterClass, scalarScratchIndex));
                    EmitMoveSequence(
                        node.LinearBlockId,
                        scalarScratch,
                        scalarHome,
                        destinationValue: null,
                        sourceValue: value,
                        "tree operand reload",
                        blockLinearNodes,
                        allNodes);
                    useOperands.Add(scalarScratch);
                    expandedRegisterUses.Add(value);
                }

                if (node.RegisterResult is not null)
                {
                    var resultValue = node.RegisterResult;
                    var resultInfo = _method.GetValueInfo(resultValue);
                    var resultAbi = MachineAbi.ClassifyStorageValue(resultInfo.Type, resultInfo.StackKind);

                    if (resultAbi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        var finalLocation = ValueLocationForDefinition(resultValue, definitionPosition, resultAbi);
                        var segments = MachineAbi.GetRegisterSegments(resultAbi);
                        if (finalLocation.Count != segments.Length)
                            throw new InvalidOperationException(
                                $"Multi-register result location count does not match ABI segment count for {resultValue}.");

                        for (int s = 0; s < segments.Length; s++)
                        {
                            var finalFragment = finalLocation[s];
                            if (finalFragment.IsRegister)
                            {
                                resultOperands.Add(finalFragment);
                                resultGenTrees.Add(resultValue);
                                continue;
                            }

                            int scratchIndex = NextScratchIndex(scratchUseCounts, segments[s].RegisterClass);
                            var scratch = RegisterOperand.ForRegister(GetTreeScratchRegister(segments[s].RegisterClass, scratchIndex));
                            resultOperands.Add(scratch);
                            resultGenTrees.Add(resultValue);
                            if (!finalFragment.IsNone)
                                postTreeStores.Add(new RegisterResolvedMove(scratch, finalFragment, resultValue, resultValue));
                        }
                    }
                    else
                    {
                        RegisterOperand finalResult = HomeForDefinition(resultValue, definitionPosition);
                        RegisterOperand nodeResult;
                        if (finalResult.IsRegister || finalResult.IsNone)
                        {
                            nodeResult = finalResult;
                        }
                        else
                        {
                            int scratchIndex = NextScratchIndex(scratchUseCounts, finalResult.RegisterClass);
                            nodeResult = RegisterOperand.ForRegister(GetTreeScratchRegister(finalResult.RegisterClass, scratchIndex));
                            postTreeStores.Add(new RegisterResolvedMove(nodeResult, finalResult, resultValue, resultValue));
                        }

                        if (!nodeResult.IsNone)
                        {
                            resultOperands.Add(nodeResult);
                            resultGenTrees.Add(resultValue);
                        }
                    }
                }

                var treeNode = GenTreeLirFactory.TreeMulti(
                    _nextNodeId++,
                    node.LinearBlockId,
                    blockLinearNodes.Count,
                    node,
                    resultOperands.ToImmutable(),
                    useOperands.ToImmutable(),
                    resultGenTrees.ToImmutable(),
                    expandedRegisterUses.ToImmutable(),
                    linearId: node.LinearId,
                    linearOperands: node.OperandFlags);
                blockLinearNodes.Add(treeNode);
                allNodes.Add(treeNode);

                for (int i = 0; i < postTreeStores.Count; i++)
                {
                    var store = postTreeStores[i];
                    EmitMoveSequence(
                        node.LinearBlockId,
                        store.Destination,
                        store.Source,
                        store.DestinationValue,
                        sourceValue: store.SourceValue,
                        "tree result spill",
                        blockLinearNodes,
                        allNodes);
                }

                if (node.RegisterResult is not null)
                {
                    var resultValue = node.RegisterResult;
                    var resultInfo = _method.GetValueInfo(resultValue);
                    var resultAbi = MachineAbi.ClassifyStorageValue(resultInfo.Type, resultInfo.StackKind);
                    if (resultAbi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        EmitAggregateHomeStores(
                            node.LinearBlockId,
                            resultValue,
                            definitionPosition,
                            resultAbi,
                            ValueLocationForDefinition(resultValue, definitionPosition, resultAbi),
                            "tree multi-register result aggregate home",
                            blockLinearNodes,
                            allNodes);
                    }
                }
            }

            private void EmitCallLikeTreeNode(
                GenTree node,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                int position = GetNodePosition(node);
                int definitionPosition = position + 1;
                var targets = ImmutableArray.CreateBuilder<RegisterOperand>();
                var callRegisterUses = ImmutableArray.CreateBuilder<GenTree>();
                var callUseRoles = ImmutableArray.CreateBuilder<OperandRole>();
                var registerMoves = new List<RegisterResolvedMove>();
                var stackMoves = new List<RegisterResolvedMove>();
                bool valueTypeNewObject = node.Kind == GenTreeKind.NewObject && node.Method?.DeclaringType.IsValueType == true;
                var descriptor = MachineAbi.BuildCallDescriptor(node.RegisterUses, _method.GetValueInfo, node.RegisterResult, node.Method, node.Kind == GenTreeKind.NewObject);

                RegisterOperand finalResult = RegisterOperand.None;
                RegisterOperand valueTypeNewObjectBuffer = RegisterOperand.None;
                RegisterOperand callResult = RegisterOperand.None;
                GenTree? nodeResultValue = null;
                AbiValueInfo resultAbi = descriptor.ReturnAbi;
                GenTree? resultValueOpt = null;

                if (node.RegisterResult is not null)
                {
                    var resultValue = node.RegisterResult;
                    resultValueOpt = resultValue;
                    finalResult = HomeForDefinition(resultValue, definitionPosition);
                    if (finalResult.IsNone && resultAbi.PassingKind == AbiValuePassingKind.Indirect)
                    {
                        finalResult = RegisterOperand.ForSpillSlot(
                            RegisterClass.General,
                            _nextSpillSlot++,
                            0,
                            Math.Max(MachineAbi.StackArgumentSlotSize, resultAbi.Size));
                    }

                    if (valueTypeNewObject)
                    {
                        int bufferSize = Math.Max(MachineAbi.StackArgumentSlotSize, Math.Max(1, resultAbi.Size));
                        valueTypeNewObjectBuffer = finalResult.IsFrameSlot
                            ? finalResult
                            : RegisterOperand.ForSpillSlot(RegisterClass.General, _nextSpillSlot++, 0, bufferSize);
                    }

                    if (!finalResult.IsNone)
                    {
                        if (valueTypeNewObject)
                        {
                            callResult = RegisterOperand.None;
                            nodeResultValue = null;
                        }
                        else if (resultAbi.PassingKind == AbiValuePassingKind.ScalarRegister)
                        {
                            callResult = RegisterOperand.ForRegister(GetReturnRegister(resultAbi.RegisterClass));
                            nodeResultValue = resultValue;
                        }
                        else if (resultAbi.PassingKind == AbiValuePassingKind.MultiRegister)
                        {
                            callResult = RegisterOperand.None;
                            nodeResultValue = null;
                        }
                        else if (resultAbi.PassingKind == AbiValuePassingKind.Indirect)
                        {
                            callResult = RegisterOperand.None;
                            nodeResultValue = null;
                        }
                        else
                        {
                            callResult = finalResult;
                            nodeResultValue = resultValue;
                        }
                    }
                }

                for (int i = 0; i < descriptor.ArgumentSegments.Length; i++)
                {
                    var segment = descriptor.ArgumentSegments[i];
                    var target = GetCallArgumentOperand(segment.Location);
                    RegisterOperand source;
                    MoveFlags moveFlags = MoveFlags.AbiArgument;
                    OperandRole role;
                    GenTree? moveSourceValue;
                    GenTree? moveDestinationValue;

                    if (segment.IsHiddenReturnBuffer)
                    {
                        var retBuffer = valueTypeNewObject ? valueTypeNewObjectBuffer : finalResult;
                        if (node.RegisterResult is null || retBuffer.IsNone)
                            throw new InvalidOperationException("ABI call result has no addressable hidden return buffer home.");

                        source = retBuffer.AsAddress();
                        role = OperandRole.HiddenReturnBuffer;
                        moveFlags |= MoveFlags.HiddenReturnBuffer;
                        moveSourceValue = null;
                        moveDestinationValue = null;
                    }
                    else
                    {
                        var sourceLocation = ValueLocationForUse(segment.Value, position, segment.ValueAbi);
                        source = GetSourceOperandForCallSegment(sourceLocation, segment);
                        role = OperandRole.Normal;
                        moveSourceValue = segment.Value;
                        moveDestinationValue = segment.Value;
                    }

                    targets.Add(target);
                    callRegisterUses.Add(segment.Value);
                    callUseRoles.Add(role);

                    if (source.Equals(target))
                        continue;

                    var move = new RegisterResolvedMove(
                        source,
                        target,
                        moveSourceValue,
                        moveDestinationValue,
                        moveFlags);
                    if (target.IsOutgoingArgumentSlot)
                        stackMoves.Add(move);
                    else
                        registerMoves.Add(move);
                }

                for (int i = 0; i < stackMoves.Count; i++)
                {
                    var move = stackMoves[i];
                    EmitMoveSequence(
                        node.LinearBlockId,
                        move.Destination,
                        move.Source,
                        move.DestinationValue,
                        move.SourceValue,
                        "call stack argument home",
                        blockLinearNodes,
                        allNodes);
                }

                if (registerMoves.Count != 0)
                {
                    int scratchSlot = -1;
                    int GetScratchSlot()
                    {
                        if (scratchSlot < 0)
                            scratchSlot = _nextSpillSlot++;
                        return scratchSlot;
                    }

                    var setup = RegisterParallelCopyResolver.Resolve(
                        node.LinearBlockId,
                        node.LinearBlockId,
                        registerMoves,
                        GetScratchSlot,
                        _options.ParallelCopyScratchRegister0,
                        _options.ParallelCopyFloatScratchRegister0,
                        ref _nextNodeId,
                        CanEmitDirectMemoryMove);

                    for (int i = 0; i < setup.Length; i++)
                    {
                        var setupNode = setup[i].WithOrdinal(blockLinearNodes.Count);
                        blockLinearNodes.Add(setupNode);
                        allNodes.Add(setupNode);
                    }
                }

                var callNode = GenTreeLirFactory.Tree(
                    _nextNodeId++,
                    node.LinearBlockId,
                    blockLinearNodes.Count,
                    node,
                    callResult,
                    targets.ToImmutable(),
                    nodeResultValue,
                    callRegisterUses.ToImmutable(),
                    linearId: node.LinearId,
                    useRoles: callUseRoles.ToImmutable(),
                    linearOperands: node.OperandFlags);
                blockLinearNodes.Add(callNode);
                allNodes.Add(callNode);

                if (valueTypeNewObject && resultValueOpt is not null && !finalResult.IsNone)
                {
                    var resultValue = resultValueOpt;
                    if (resultAbi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        var segments = MachineAbi.GetRegisterSegments(resultAbi);
                        var destinationLocation = ValueLocationForDefinition(resultValue, definitionPosition, resultAbi);
                        for (int i = 0; i < segments.Length; i++)
                        {
                            var segment = segments[i];
                            var destination = destinationLocation.IsFragmented ? destinationLocation[i] : OperandAtOffset(finalResult, segment);
                            var source = OperandAtOffset(valueTypeNewObjectBuffer, segment);
                            EmitMoveSequence(
                                node.LinearBlockId,
                                destination,
                                source,
                                destinationValue: resultValue,
                                sourceValue: resultValue,
                                comment: "newobj value result fragment",
                                blockLinearNodes,
                                allNodes);
                        }

                        EmitAggregateHomeStores(
                            node.LinearBlockId,
                            resultValue,
                            definitionPosition,
                            resultAbi,
                            destinationLocation,
                            "newobj value result aggregate home",
                            blockLinearNodes,
                            allNodes);
                        return;
                    }

                    if (!finalResult.Equals(valueTypeNewObjectBuffer))
                    {
                        EmitMoveSequence(
                            node.LinearBlockId,
                            finalResult,
                            valueTypeNewObjectBuffer,
                            destinationValue: resultValue,
                            sourceValue: resultValue,
                            comment: "newobj value result",
                            blockLinearNodes,
                            allNodes);
                    }
                    return;
                }

                if (resultValueOpt is not null && !finalResult.IsNone)
                {
                    var resultValue = resultValueOpt;
                    if (resultAbi.PassingKind == AbiValuePassingKind.ScalarRegister && !finalResult.Equals(callResult))
                    {
                        EmitMoveSequence(
                            node.LinearBlockId,
                            finalResult,
                            callResult,
                            resultValue,
                            sourceValue: null,
                            "call result from ABI register",
                            blockLinearNodes,
                            allNodes);
                    }
                    else if (resultAbi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        var segments = MachineAbi.GetRegisterSegments(resultAbi);
                        var destinationLocation = ValueLocationForDefinition(resultValue, definitionPosition, resultAbi);
                        int generalReturnIndex = 0;
                        int floatReturnIndex = 0;
                        for (int i = 0; i < segments.Length; i++)
                        {
                            var segment = segments[i];
                            var source = RegisterOperand.ForRegister(GetReturnRegister(segment.RegisterClass, ref generalReturnIndex, ref floatReturnIndex));
                            var destination = destinationLocation.IsFragmented ? destinationLocation[i] : OperandAtOffset(finalResult, segment);
                            EmitMoveSequence(
                                node.LinearBlockId,
                                destination,
                                source,
                                destinationValue: resultValue,
                                sourceValue: null,
                                comment: "call struct result from ABI register",
                                blockLinearNodes,
                                allNodes);
                        }

                        EmitAggregateHomeStores(
                            node.LinearBlockId,
                            resultValue,
                            definitionPosition,
                            resultAbi,
                            destinationLocation,
                            "call struct result aggregate home",
                            blockLinearNodes,
                            allNodes);
                    }
                }
            }

            private static RegisterOperand GetCallArgumentOperand(AbiArgumentLocation location)
            {
                if (location.IsRegister)
                    return RegisterOperand.ForRegister(location.Register);

                return RegisterOperand.ForOutgoingArgumentSlot(
                    location.RegisterClass,
                    location.StackSlotIndex,
                    location.StackOffset,
                    location.Size);
            }

            private static RegisterOperand GetSourceOperandForCallSegment(RegisterValueLocation sourceLocation, AbiCallSegment segment)
            {
                if (segment.IsAbiSegment)
                    return sourceLocation.IsFragmented
                        ? sourceLocation[segment.SegmentIndex]
                        : OperandAtOffset(sourceLocation.Scalar, segment.ToRegisterSegment());

                if (sourceLocation.IsScalar)
                    return sourceLocation.Scalar;

                if (sourceLocation.Count == 1)
                    return sourceLocation[0];

                throw new InvalidOperationException($"Non-fragment ABI argument cannot be read from fragmented source location: {sourceLocation}.");
            }

            private static MachineRegister GetReturnRegister(RegisterClass registerClass, ref int generalIndex, ref int floatIndex)
            {
                int index;
                if (registerClass == RegisterClass.Float)
                {
                    index = floatIndex++;
                    return index switch
                    {
                        0 => MachineRegisters.FloatReturnValue0,
                        1 => MachineRegisters.FloatReturnValue1,
                        _ => throw new InvalidOperationException("Not enough float return registers for multi-register return."),
                    };
                }

                if (registerClass == RegisterClass.General)
                {
                    index = generalIndex++;
                    return index switch
                    {
                        0 => MachineRegisters.ReturnValue0,
                        1 => MachineRegisters.ReturnValue1,
                        _ => throw new InvalidOperationException("Not enough integer return registers for multi-register return."),
                    };
                }

                throw new InvalidOperationException("Invalid return register class " + registerClass + ".");
            }

            private static RegisterOperand OperandAtOffset(RegisterOperand operand, AbiRegisterSegment segment)
            {
                if (operand.IsRegister)
                {
                    if (segment.Offset != 0)
                        throw new InvalidOperationException($"Cannot address a non-zero struct fragment inside a scalar register operand: {operand}.");
                    if (!MachineRegisters.IsRegisterInClass(operand.Register, segment.RegisterClass))
                        throw new InvalidOperationException($"Register operand class does not match ABI segment class: {operand} vs {segment}.");
                    return operand;
                }

                int offset = checked(operand.FrameOffset + segment.Offset);
                int size = segment.Size;

                if (operand.IsSpillSlot)
                    return RegisterOperand.ForSpillSlot(segment.RegisterClass, operand.SpillSlot, offset, size);
                if (operand.IsIncomingArgumentSlot)
                    return RegisterOperand.ForIncomingArgumentSlot(segment.RegisterClass, operand.FrameSlotIndex, offset, size);
                if (operand.IsLocalSlot)
                    return RegisterOperand.ForLocalSlot(segment.RegisterClass, operand.FrameSlotIndex, offset, size);
                if (operand.IsTempSlot)
                    return RegisterOperand.ForTempSlot(segment.RegisterClass, operand.FrameSlotIndex, offset, size);
                if (operand.IsOutgoingArgumentSlot)
                    return RegisterOperand.ForOutgoingArgumentSlot(segment.RegisterClass, operand.FrameSlotIndex, offset, size);
                if (operand.IsFrameSlot)
                    return RegisterOperand.ForFrameSlot(segment.RegisterClass, operand.FrameSlotKind, operand.FrameBase, operand.FrameSlotIndex, offset, size, operand.IsAddress);

                throw new InvalidOperationException($"Cannot address an ABI fragment inside operand: {operand}.");
            }

            private static void ReserveInternalScratchRegisters(GenTree node, Dictionary<RegisterClass, int> scratchUseCounts)
            {
                if (node.LinearLowering.InternalGeneralRegisters != 0)
                    scratchUseCounts[RegisterClass.General] = node.LinearLowering.InternalGeneralRegisters;

                if (node.LinearLowering.InternalFloatRegisters != 0)
                    scratchUseCounts[RegisterClass.Float] = node.LinearLowering.InternalFloatRegisters;
            }

            private static int NextScratchIndex(Dictionary<RegisterClass, int> scratchUseCounts, RegisterClass registerClass)
            {
                scratchUseCounts.TryGetValue(registerClass, out int index);
                scratchUseCounts[registerClass] = index + 1;
                return index;
            }

            private void EmitMemoryConstrainedTreeNode(
                GenTree node,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (false)
                    throw new InvalidOperationException("GenTree LIR tree node has no GenTree source.");

                int position = GetNodePosition(node);
                int definitionPosition = position + 1;
                var useOperands = ImmutableArray.CreateBuilder<RegisterOperand>(node.RegisterUses.Length);
                var expandedRegisterUses = ImmutableArray.CreateBuilder<GenTree>(node.RegisterUses.Length);
                var resultOperands = ImmutableArray.CreateBuilder<RegisterOperand>();
                var resultGenTrees = ImmutableArray.CreateBuilder<GenTree>();
                var postTreeStores = new List<RegisterResolvedMove>();
                var scratchUseCounts = new Dictionary<RegisterClass, int>();

                ReserveInternalScratchRegisters(node, scratchUseCounts);

                for (int i = 0; i < node.RegisterUses.Length; i++)
                {
                    var value = node.RegisterUses[i];
                    var valueInfo = _method.GetValueInfo(value);
                    var abi = MachineAbi.ClassifyStorageValue(valueInfo.Type, valueInfo.StackKind);
                    int operandIndex = GetOperandIndexForRegisterUse(node, i);
                    var operandFlags = GetOperandFlagsForRegisterUse(node, i);
                    bool requiresRegister = RequiresCodegenRegisterUse(node, operandIndex, abi, operandFlags);

                    if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        if (!requiresRegister && CanPassMemoryValueUseByAggregateHome(node, operandIndex, value, position, out var aggregateHome))
                        {
                            useOperands.Add(aggregateHome);
                            expandedRegisterUses.Add(value);
                            continue;
                        }

                        var location = ValueLocationForUse(value, position, abi);
                        var segments = MachineAbi.GetRegisterSegments(abi);
                        if (location.Count != segments.Length)
                            throw new InvalidOperationException($"Multi-register use location count does not match ABI segment count for {value}.");

                        for (int s = 0; s < segments.Length; s++)
                        {
                            var home = location[s];
                            if (requiresRegister && !home.IsRegister)
                            {
                                int scratchIndex = NextScratchIndex(scratchUseCounts, segments[s].RegisterClass);
                                var scratch = RegisterOperand.ForRegister(GetTreeScratchRegister(segments[s].RegisterClass, scratchIndex));
                                EmitMoveSequence(
                                    node.LinearBlockId,
                                    scratch,
                                    home,
                                    destinationValue: null,
                                    sourceValue: value,
                                    "tree memory-operand fragment reload",
                                    blockLinearNodes,
                                    allNodes);
                                home = scratch;
                            }

                            useOperands.Add(home);
                            expandedRegisterUses.Add(value);
                        }
                        continue;
                    }

                    var scalarHome = HomeForUse(value, position);
                    if (requiresRegister && !scalarHome.IsRegister)
                    {
                        var reloadClass = RegisterClassForReload(valueInfo, abi, scalarHome);
                        int scratchIndex = NextScratchIndex(scratchUseCounts, reloadClass);
                        var scratch = RegisterOperand.ForRegister(GetTreeScratchRegister(reloadClass, scratchIndex));
                        EmitMoveSequence(
                            node.LinearBlockId,
                            scratch,
                            scalarHome,
                            destinationValue: null,
                            sourceValue: value,
                            "tree memory-operand reload",
                            blockLinearNodes,
                            allNodes);
                        scalarHome = scratch;
                    }

                    useOperands.Add(scalarHome);
                    expandedRegisterUses.Add(value);
                }

                if (node.RegisterResult is not null)
                {
                    var resultValue = node.RegisterResult;
                    var resultInfo = _method.GetValueInfo(resultValue);
                    var resultAbi = MachineAbi.ClassifyStorageValue(resultInfo.Type, resultInfo.StackKind);
                    if (resultAbi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        var finalLocation = ValueLocationForDefinition(resultValue, definitionPosition, resultAbi);
                        var segments = MachineAbi.GetRegisterSegments(resultAbi);
                        if (finalLocation.Count != segments.Length)
                            throw new InvalidOperationException($"Multi-register result location count does not match ABI segment count for {resultValue}.");

                        for (int s = 0; s < segments.Length; s++)
                        {
                            var finalFragment = finalLocation[s];
                            RegisterOperand nodeFragment = finalFragment;
                            if (RequiresCodegenRegisterDefinition(node, resultAbi) && !finalFragment.IsRegister && !finalFragment.IsNone)
                            {
                                int scratchIndex = NextScratchIndex(scratchUseCounts, segments[s].RegisterClass);
                                nodeFragment = RegisterOperand.ForRegister(GetTreeScratchRegister(segments[s].RegisterClass, scratchIndex));
                                postTreeStores.Add(new RegisterResolvedMove(nodeFragment, finalFragment, resultValue, resultValue));
                            }

                            if (!nodeFragment.IsNone)
                            {
                                resultOperands.Add(nodeFragment);
                                resultGenTrees.Add(resultValue);
                            }
                        }
                    }
                    else
                    {
                        var finalResult = HomeForDefinition(resultValue, definitionPosition);
                        RegisterOperand nodeResult = finalResult;
                        if (RequiresCodegenRegisterDefinition(node, resultAbi) && !finalResult.IsRegister && !finalResult.IsNone)
                        {
                            var resultClass = RegisterClassForReload(resultInfo, resultAbi, finalResult);
                            int scratchIndex = NextScratchIndex(scratchUseCounts, resultClass);
                            nodeResult = RegisterOperand.ForRegister(GetTreeScratchRegister(resultClass, scratchIndex));
                            postTreeStores.Add(new RegisterResolvedMove(nodeResult, finalResult, resultValue, resultValue));
                        }

                        if (!nodeResult.IsNone)
                        {
                            resultOperands.Add(nodeResult);
                            resultGenTrees.Add(resultValue);
                        }
                    }
                }

                var treeNode = GenTreeLirFactory.TreeMulti(
                    _nextNodeId++,
                    node.LinearBlockId,
                    blockLinearNodes.Count,
                    node,
                    resultOperands.ToImmutable(),
                    useOperands.ToImmutable(),
                    resultGenTrees.ToImmutable(),
                    expandedRegisterUses.ToImmutable(),
                    linearId: node.LinearId,
                    linearOperands: node.OperandFlags);

                blockLinearNodes.Add(treeNode);
                allNodes.Add(treeNode);

                for (int i = 0; i < postTreeStores.Count; i++)
                {
                    var store = postTreeStores[i];
                    EmitMoveSequence(
                        node.LinearBlockId,
                        store.Destination,
                        store.Source,
                        store.DestinationValue,
                        sourceValue: store.SourceValue,
                        "tree memory-node result spill",
                        blockLinearNodes,
                        allNodes);
                }

            }

            private bool CanPassMemoryValueUseByAggregateHome(
                GenTree node,
                int operandIndex,
                GenTree value,
                int position,
                out RegisterOperand aggregateHome)
            {
                aggregateHome = RegisterOperand.None;

                if (!node.LinearMemoryAccess.HasValueOperand(operandIndex))
                    return false;

                if (!node.LinearMemoryAccess.IsBlockCopy)
                    return false;

                var home = HomeForUse(value, position);
                if (!home.IsFrameSlot)
                    return false;

                aggregateHome = home;
                return true;
            }

            private static LirOperandFlags GetOperandFlagsForRegisterUse(GenTree node, int registerUseIndex)
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

            private static bool RequiresCodegenRegisterUse(
                GenTree node,
                int operandIndex,
                AbiValueInfo abi,
                LirOperandFlags operandFlags)
            {
                if ((operandFlags & LirOperandFlags.RegOptional) != 0)
                    return false;

                if (node.HasLoweringFlag(GenTreeLinearFlags.RequiresRegisterOperands))
                    return true;

                var memory = node.LinearMemoryAccess;
                if (!memory.IsNone)
                {
                    if (memory.HasAddressOperand(operandIndex))
                        return true;

                    if (memory.HasValueOperand(operandIndex))
                    {
                        if (memory.IsBlockCopy)
                            return false;

                        return abi.PassingKind == AbiValuePassingKind.ScalarRegister;
                    }
                }

                if (abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect or AbiValuePassingKind.MultiRegister)
                    return false;

                return true;
            }

            private static bool RequiresCodegenRegisterDefinition(GenTree node, AbiValueInfo abi)
            {
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

            private static RegisterClass RegisterClassForReload(GenTreeValueInfo info, AbiValueInfo abi, RegisterOperand home)
            {
                if (home.RegisterClass is RegisterClass.General or RegisterClass.Float)
                    return home.RegisterClass;

                if (info.RegisterClass is RegisterClass.General or RegisterClass.Float)
                    return info.RegisterClass;

                if (abi.RegisterClass is RegisterClass.General or RegisterClass.Float)
                    return abi.RegisterClass;

                return RegisterClass.General;
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

            private int GetNodePosition(GenTree node)
            {
                if (!_nodePositions.TryGetValue(node.LinearId, out int position))
                    throw new InvalidOperationException($"Missing GenTree LIR position for node {node.LinearId}.");
                return position;
            }

            private RegisterOperand HomeForUse(GenTree value, int position)
            {
                var home = HomeOrNone(value, position);
                if (home.IsNone)
                    throw new InvalidOperationException($"GenTree value {value} is used but has no assigned register or spill slot at position {position}.");
                return home;
            }

            private RegisterValueLocation ValueLocationForUse(GenTree value, int position, AbiValueInfo abi)
            {
                var location = ValueLocationOrNone(value, position, abi);
                if (location.IsEmpty)
                    throw new InvalidOperationException($"GenTree value {value} is used but has no assigned location at position {position}.");

                if (location.IsFragmented)
                {
                    for (int i = 0; i < location.Count; i++)
                    {
                        if (location[i].IsNone)
                            throw new InvalidOperationException($"GenTree value {value} ABI fragment {i} is used but has no assigned location at position {position}.");
                    }
                }

                return location;
            }

            private RegisterOperand HomeForDefinition(GenTree value)
            {
                if (!_allocations.TryGetValue(value, out var allocation))
                    throw new InvalidOperationException($"Missing allocation for {value}.");
                return allocation.LocationAtDefinition();
            }

            private RegisterOperand HomeForDefinition(GenTree value, int position)
                => HomeOrNone(value, position);

            private RegisterValueLocation ValueLocationForDefinition(GenTree value, int position, AbiValueInfo abi)
                => ValueLocationOrNone(value, position, abi);

            private RegisterOperand HomeOrNone(GenTree value, int position)
            {
                if (!_allocations.TryGetValue(value, out var allocation))
                    throw new InvalidOperationException($"Missing allocation for {value}.");
                return allocation.LocationAt(position);
            }

            private RegisterValueLocation ValueLocationOrNone(GenTree value, int position, AbiValueInfo abi)
            {
                if (!_allocations.TryGetValue(value, out var allocation))
                    throw new InvalidOperationException($"Missing allocation for {value}.");
                return allocation.ValueLocationAt(position, abi);
            }

            private readonly struct AllocationPreferenceKey : IEquatable<AllocationPreferenceKey>
            {
                public readonly GenTree Value;
                public readonly int AbiSegmentIndex;

                public AllocationPreferenceKey(GenTree value, int abiSegmentIndex)
                {
                    Value = value;
                    AbiSegmentIndex = abiSegmentIndex;
                }

                public bool Equals(AllocationPreferenceKey other)
                    => Value.Equals(other.Value) && AbiSegmentIndex == other.AbiSegmentIndex;

                public override bool Equals(object? obj)
                    => obj is AllocationPreferenceKey other && Equals(other);

                public override int GetHashCode()
                {
                    unchecked
                    {
                        return (Value.GetHashCode() * 397) ^ AbiSegmentIndex;
                    }
                }
            }

            private static Dictionary<AllocationPreferenceKey, List<MachineRegister>> BuildRegisterPreferences(GenTreeMethod method)
            {
                var result = new Dictionary<AllocationPreferenceKey, List<MachineRegister>>();

                for (int i = 0; i < method.RefPositions.Length; i++)
                {
                    var rp = method.RefPositions[i];
                    if (rp.Value is null || rp.FixedRegister == MachineRegister.Invalid)
                        continue;
                    if (rp.Kind is not (LinearRefPositionKind.Use or LinearRefPositionKind.Def))
                        continue;

                    AddRegisterPreference(result, rp.Value, rp.IsAbiSegment ? rp.AbiSegmentIndex : -1, rp.FixedRegister);
                }

                for (int i = 0; i < method.Values.Length; i++)
                {
                    var info = method.Values[i];
                    if (!IsInitialSsaArgumentValue(info))
                        continue;

                    if ((uint)info.Value.SsaSlot.Index < (uint)method.ArgTypes.Length)
                        AddIncomingArgumentRegisterPreferences(result, method, info, info.Value.SsaSlot.Index);
                }

                foreach (var node in method.LinearNodes)
                {
                    if (IsAbiCall(node))
                    {
                        var descriptor = MachineAbi.BuildCallDescriptor(node.RegisterUses, method.GetValueInfo, node.RegisterResult, node.Method, node.Kind == GenTreeKind.NewObject);
                        for (int i = 0; i < descriptor.ArgumentSegments.Length; i++)
                        {
                            var segment = descriptor.ArgumentSegments[i];
                            if (segment.IsHiddenReturnBuffer || !segment.IsRegister)
                                continue;

                            AddRegisterPreference(
                                result,
                                segment.Value,
                                segment.IsAbiSegment ? segment.SegmentIndex : -1,
                                segment.Location.Register);
                        }

                        if (node.RegisterResult is not null)
                            AddReturnRegisterPreferences(result, node.RegisterResult, descriptor.ReturnAbi);

                        continue;
                    }

                    if (node.Kind == GenTreeKind.Return && node.RegisterUses.Length == 1)
                    {
                        var value = node.RegisterUses[0];
                        var valueInfo = method.GetValueInfo(value);
                        var abi = MachineAbi.ClassifyValue(valueInfo.Type, valueInfo.StackKind, isReturn: true);
                        AddReturnRegisterPreferences(result, value, abi);
                    }
                }

                return result;
            }

            private static bool IsInitialSsaArgumentValue(GenTreeValueInfo info)
                => info.Value.IsSsaValue &&
                   info.Value.SsaSlot.Kind == SsaSlotKind.Arg &&
                   info.DefinitionBlockId < 0 &&
                   info.DefinitionNodeId < 0;

            private static void AddIncomingArgumentRegisterPreferences(
                Dictionary<AllocationPreferenceKey, List<MachineRegister>> result,
                GenTreeMethod method,
                GenTreeValueInfo info,
                int argumentIndex)
            {
                int general = 0;
                int floating = 0;
                int hiddenReturnBufferInsertionIndex = MachineAbi.HiddenReturnBufferInsertionIndex(
                    method.RuntimeMethod,
                    method.ArgTypes.Length);

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
                            AddRegisterPreference(result, info.RepresentativeNode, -1, register);
                        continue;
                    }

                    if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        var segments = MachineAbi.GetRegisterSegments(abi);
                        for (int s = 0; s < segments.Length; s++)
                        {
                            var register = GetMaybeArgumentRegister(segments[s].RegisterClass, ref general, ref floating);
                            if (i == argumentIndex && register != MachineRegister.Invalid)
                                AddRegisterPreference(result, info.RepresentativeNode, s, register);
                        }
                    }
                }
            }

            private static void AddReturnRegisterPreferences(
                Dictionary<AllocationPreferenceKey, List<MachineRegister>> result,
                GenTree value,
                AbiValueInfo abi)
            {
                if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                {
                    AddRegisterPreference(result, value, -1, GetReturnRegister(abi.RegisterClass));
                    return;
                }

                if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    var segments = MachineAbi.GetRegisterSegments(abi);
                    int general = 0;
                    int floating = 0;
                    for (int s = 0; s < segments.Length; s++)
                        AddRegisterPreference(result, value, s, GetReturnRegister(segments[s].RegisterClass, ref general, ref floating));
                }
            }

            private static MachineRegister GetReturnRegister(RegisterClass registerClass)
            {
                return registerClass switch
                {
                    RegisterClass.Float => MachineRegisters.FloatReturnValue0,
                    RegisterClass.General => MachineRegisters.ReturnValue0,
                    _ => MachineRegister.Invalid,
                };
            }

            private static void AddRegisterPreference(
                Dictionary<AllocationPreferenceKey, List<MachineRegister>> map,
                GenTree value,
                int abiSegmentIndex,
                MachineRegister register)
            {
                if (register == MachineRegister.Invalid)
                    return;

                var key = new AllocationPreferenceKey(value, abiSegmentIndex);
                if (!map.TryGetValue(key, out var list))
                {
                    list = new List<MachineRegister>();
                    map.Add(key, list);
                }

                if (!list.Contains(register))
                    list.Add(register);
            }

            private static Dictionary<GenTree, List<GenTree>> BuildPreferences(GenTreeMethod method)
            {
                var result = new Dictionary<GenTree, List<GenTree>>();

                foreach (var node in method.LinearNodes)
                {
                    if (node.RegisterResult is null || node.RegisterUses.Length != 1)
                        continue;

                    if (node.LinearKind == GenTreeLinearKind.Copy || IsPromotedStoreDef(node) || IsPromotedLoadUse(node))
                        AddClassCompatiblePreference(method, result, node.RegisterResult, node.RegisterUses[0]);
                }

                return result;
            }

            private static bool IsPromotedStoreDef(GenTree node)
            {
                if (node.LinearKind != GenTreeLinearKind.Tree || node.RegisterResult is null || node.RegisterUses.Length != 1)
                    return false;

                return node.Kind is GenTreeKind.StoreArg or GenTreeKind.StoreLocal or GenTreeKind.StoreTemp;
            }

            private static bool IsPromotedLoadUse(GenTree node)
            {
                if (node.LinearKind != GenTreeLinearKind.Tree || node.RegisterResult is null || node.RegisterUses.Length != 1)
                    return false;

                return node.Kind is GenTreeKind.Arg or GenTreeKind.Local or GenTreeKind.Temp;
            }

            private static void AddClassCompatiblePreference(
                GenTreeMethod method,
                Dictionary<GenTree, List<GenTree>> map,
                GenTree left,
                GenTree right)
            {
                var leftClass = method.GetValueInfo(left).RegisterClass;
                var rightClass = method.GetValueInfo(right).RegisterClass;
                if (leftClass != rightClass)
                    return;

                AddPreference(map, left, right);
                AddPreference(map, right, left);
            }

            private static void AddPreference(Dictionary<GenTree, List<GenTree>> map, GenTree value, GenTree preferred)
            {
                if (!map.TryGetValue(value, out var list))
                {
                    list = new List<GenTree>();
                    map.Add(value, list);
                }

                if (!list.Contains(preferred))
                    list.Add(preferred);
            }
        }

        private sealed class AllocationInterval
        {
            private sealed class AllocationSegment
            {
                public int Start;
                public int End;
                public RegisterOperand Location;

                public AllocationSegment(int start, int end, RegisterOperand location)
                {
                    Start = start;
                    End = end;
                    Location = location;
                }
            }

            public GenTree Value { get; }
            public RegisterClass RegisterClass { get; }
            public ImmutableArray<LinearLiveRange> Ranges { get; }
            public ImmutableArray<int> UsePositions { get; }
            public int DefinitionPosition { get; }
            public bool CrossesCall { get; }
            public bool RequiresSingleLocation { get; }
            public bool RequiresStackHome { get; }
            public int StackHomeSize { get; }
            public int StackHomeAlignment { get; }
            public int AbiSegmentIndex { get; }
            public int AbiSegmentOffset { get; }
            public int AbiSegmentSize { get; }
            public int Start { get; }
            public int End { get; }
            public MachineRegister AssignedRegister { get; set; } = MachineRegister.Invalid;
            public int AssignedRegisterEnd { get; set; }
            public int SpillSlot { get; set; } = -1;

            private readonly List<AllocationSegment> _segments = new();

            public bool IsEmpty => Ranges.Length == 0;
            public bool HasAssignedRegister => AssignedRegister != MachineRegister.Invalid && AssignedRegisterEnd > Start;
            public bool IsAbiFragment => AbiSegmentIndex >= 0;

            public RegisterOperand PrimaryHome
            {
                get
                {
                    if (_segments.Count == 0)
                        return RegisterOperand.None;
                    return _segments[0].Location;
                }
            }

            public AllocationInterval(
                GenTree value,
                RegisterClass registerClass,
                ImmutableArray<LinearLiveRange> ranges,
                ImmutableArray<int> usePositions,
                int definitionPosition,
                bool crossesCall,
                bool requiresSingleLocation,
                bool requiresStackHome,
                int stackHomeSize = 0,
                int stackHomeAlignment = 1,
                int abiSegmentIndex = -1,
                int abiSegmentOffset = 0,
                int abiSegmentSize = 0)
            {
                if (stackHomeSize < 0)
                    throw new ArgumentOutOfRangeException(nameof(stackHomeSize));
                if (stackHomeAlignment <= 0)
                    throw new ArgumentOutOfRangeException(nameof(stackHomeAlignment));
                if (abiSegmentIndex < -1)
                    throw new ArgumentOutOfRangeException(nameof(abiSegmentIndex));
                if (abiSegmentOffset < 0)
                    throw new ArgumentOutOfRangeException(nameof(abiSegmentOffset));
                if (abiSegmentSize < 0)
                    throw new ArgumentOutOfRangeException(nameof(abiSegmentSize));

                Value = value;
                RegisterClass = registerClass;
                Ranges = ranges.IsDefault ? ImmutableArray<LinearLiveRange>.Empty : ranges;
                UsePositions = usePositions.IsDefault ? ImmutableArray<int>.Empty : usePositions;
                DefinitionPosition = definitionPosition;
                CrossesCall = crossesCall;
                RequiresSingleLocation = requiresSingleLocation;
                RequiresStackHome = requiresStackHome;
                StackHomeSize = stackHomeSize;
                StackHomeAlignment = stackHomeAlignment;
                AbiSegmentIndex = abiSegmentIndex;
                AbiSegmentOffset = abiSegmentOffset;
                AbiSegmentSize = abiSegmentSize;

                if (Ranges.Length == 0)
                {
                    Start = definitionPosition;
                    End = definitionPosition;
                }
                else
                {
                    Start = Ranges[0].Start;
                    End = Ranges[0].End;
                    for (int i = 1; i < Ranges.Length; i++)
                    {
                        if (Ranges[i].Start < Start)
                            Start = Ranges[i].Start;
                        if (Ranges[i].End > End)
                            End = Ranges[i].End;
                    }
                }

                AssignedRegisterEnd = Start;
            }

            public bool Covers(int position)
            {
                for (int i = 0; i < Ranges.Length; i++)
                {
                    var range = Ranges[i];
                    if (range.Start <= position && position < range.End)
                        return true;
                }
                return false;
            }

            public bool CrossesPosition(int position)
            {
                for (int i = 0; i < Ranges.Length; i++)
                {
                    var range = Ranges[i];
                    if (range.Start <= position && position + 1 < range.End)
                        return true;
                }
                return false;
            }

            public bool Intersects(AllocationInterval other)
            {
                int i = 0;
                int j = 0;
                while (i < Ranges.Length && j < other.Ranges.Length)
                {
                    var a = Ranges[i];
                    var b = other.Ranges[j];
                    if (a.Start < b.End && b.Start < a.End)
                        return true;
                    if (a.End <= b.Start)
                        i++;
                    else
                        j++;
                }
                return false;
            }

            public int FirstIntersection(AllocationInterval other)
                => FirstIntersection(other, int.MinValue, int.MaxValue);

            public int FirstIntersection(AllocationInterval other, int minPosition, int maxPosition)
            {
                int best = int.MaxValue;
                int i = 0;
                int j = 0;
                while (i < Ranges.Length && j < other.Ranges.Length)
                {
                    var a = Ranges[i];
                    var b = other.Ranges[j];
                    int start = Math.Max(Math.Max(a.Start, b.Start), minPosition);
                    int end = Math.Min(Math.Min(a.End, b.End), maxPosition);
                    if (start < end && start < best)
                        best = start;

                    if (a.End <= b.End)
                        i++;
                    else
                        j++;
                }
                return best;
            }

            public int FirstRegisterIntersection(AllocationInterval other)
                => FirstRegisterIntersection(other, int.MinValue, int.MaxValue);

            public int FirstRegisterIntersection(AllocationInterval other, int minPosition, int maxPosition)
            {
                if (!HasAssignedRegister)
                    return int.MaxValue;

                int best = int.MaxValue;
                for (int s = 0; s < _segments.Count; s++)
                {
                    var segment = _segments[s];
                    if (!segment.Location.IsRegister || segment.Location.Register != AssignedRegister)
                        continue;

                    int p = FirstIntersection(other, Math.Max(minPosition, segment.Start), Math.Min(maxPosition, segment.End));
                    if (p < best)
                        best = p;
                }
                return best;
            }

            public int NextUseAfterOrAt(int position)
            {
                for (int i = 0; i < UsePositions.Length; i++)
                {
                    if (UsePositions[i] >= position)
                        return UsePositions[i];
                }
                return int.MaxValue;
            }

            public void AddSegment(int start, int end, RegisterOperand location)
            {
                if (end <= start || location.IsNone)
                    return;

                _segments.Add(new AllocationSegment(start, end, location));
                NormalizeSegments();
            }

            public void ReplaceWithSingleSegment(int start, int end, RegisterOperand location)
            {
                if (location.IsNone)
                    throw new ArgumentOutOfRangeException(nameof(location));

                _segments.Clear();
                AssignedRegister = location.IsRegister ? location.Register : MachineRegister.Invalid;
                AssignedRegisterEnd = location.IsRegister ? end : start;
                AddSegment(start, end, location);
            }

            public void SplitAssignedRegisterToSpill(int splitPosition, RegisterOperand spillHome)
            {
                if (spillHome.IsNone)
                    throw new ArgumentOutOfRangeException(nameof(spillHome));

                if (!HasAssignedRegister)
                {
                    AddSegment(splitPosition, End, spillHome);
                    return;
                }

                for (int i = _segments.Count - 1; i >= 0; i--)
                {
                    var segment = _segments[i];
                    if (!segment.Location.IsRegister || segment.Location.Register != AssignedRegister)
                        continue;
                    if (splitPosition <= segment.Start)
                    {
                        _segments.RemoveAt(i);
                    }
                    else if (splitPosition < segment.End)
                    {
                        segment.End = splitPosition;
                    }
                }

                AssignedRegister = MachineRegister.Invalid;
                AssignedRegisterEnd = splitPosition;
                AddSegment(splitPosition, End, spillHome);
                NormalizeSegments();
            }

            public ImmutableArray<RegisterAllocationSegment> ToRegisterAllocationSegments()
            {
                NormalizeSegments();
                var builder = ImmutableArray.CreateBuilder<RegisterAllocationSegment>(_segments.Count);
                for (int i = 0; i < _segments.Count; i++)
                {
                    var segment = _segments[i];
                    if (segment.End > segment.Start)
                        builder.Add(new RegisterAllocationSegment(segment.Start, segment.End, segment.Location));
                }
                return builder.ToImmutable();
            }

            private void NormalizeSegments()
            {
                if (_segments.Count <= 1)
                    return;

                _segments.Sort(static (a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));

                int w = 0;
                for (int r = 0; r < _segments.Count; r++)
                {
                    var next = _segments[r];
                    if (next.End <= next.Start)
                        continue;

                    if (w != 0)
                    {
                        var prev = _segments[w - 1];
                        if (prev.Location.Equals(next.Location) && next.Start <= prev.End)
                        {
                            if (next.End > prev.End)
                                prev.End = next.End;
                            continue;
                        }
                    }

                    _segments[w++] = next;
                }

                if (w < _segments.Count)
                    _segments.RemoveRange(w, _segments.Count - w);
            }
        }
    }

    internal readonly struct RegisterResolvedMove
    {
        public readonly RegisterOperand Source;
        public readonly RegisterOperand Destination;
        public readonly GenTree? SourceValue;
        public readonly GenTree? DestinationValue;
        public readonly MoveFlags MoveFlags;

        public RegisterResolvedMove(
            RegisterOperand source,
            RegisterOperand destination,
            GenTree? sourceValue,
            GenTree? destinationValue,
            MoveFlags moveFlags = MoveFlags.None)
        {
            if (!source.IsNone && !destination.IsNone && source.RegisterClass != destination.RegisterClass)
                throw new InvalidOperationException($"Cannot move between different register classes: {source} -> {destination}.");

            Source = source;
            Destination = destination;
            SourceValue = sourceValue;
            DestinationValue = destinationValue;
            MoveFlags = moveFlags;
        }

        public RegisterResolvedMove WithSource(RegisterOperand source, GenTree? sourceValue)
            => new RegisterResolvedMove(source, Destination, sourceValue, DestinationValue, MoveFlags);
    }

    internal static class RegisterParallelCopyResolver
    {
        public static ImmutableArray<GenTree> Resolve(
            int fromBlockId,
            int toBlockId,
            List<RegisterResolvedMove> moves,
            Func<int> getScratchSpillSlot,
            MachineRegister generalScratchRegister,
            MachineRegister floatScratchRegister,
            ref int nextNodeId,
            Func<RegisterOperand, RegisterOperand, GenTree?, GenTree?, bool>? canEmitDirectMemoryMove = null,
            int phiCopyFromBlockId = -1,
            int phiCopyToBlockId = -1,
            bool preserveIdentityMoves = false)
        {
            if (moves is null)
                throw new ArgumentNullException(nameof(moves));
            if (getScratchSpillSlot is null)
                throw new ArgumentNullException(nameof(getScratchSpillSlot));
            ValidateScratch(generalScratchRegister, RegisterClass.General);
            ValidateScratch(floatScratchRegister, RegisterClass.Float);

            var result = ImmutableArray.CreateBuilder<GenTree>();

            for (int i = moves.Count - 1; i >= 0; i--)
            {
                var move = moves[i];
                if (move.Source.IsNone || move.Destination.IsNone)
                {
                    moves.RemoveAt(i);
                    continue;
                }

                if (move.Source.Equals(move.Destination))
                {
                    if (preserveIdentityMoves)
                    {
                        EmitMove(
                            result,
                            ref nextNodeId,
                            fromBlockId,
                            move,
                            generalScratchRegister,
                            floatScratchRegister,
                            canEmitDirectMemoryMove,
                            "edge B" + fromBlockId + "->B" + toBlockId + " identity",
                            phiCopyFromBlockId,
                            phiCopyToBlockId);
                    }
                    moves.RemoveAt(i);
                }
            }

            int scratchSlot = -1;

            while (moves.Count != 0)
            {
                int acyclicIndex = FindAcyclicMove(moves);
                if (acyclicIndex >= 0)
                {
                    EmitMove(result, ref nextNodeId, fromBlockId, moves[acyclicIndex], generalScratchRegister,
                        floatScratchRegister, canEmitDirectMemoryMove, "edge B" + fromBlockId + "->B" + toBlockId,
                        phiCopyFromBlockId, phiCopyToBlockId);
                    moves.RemoveAt(acyclicIndex);
                    continue;
                }

                if (scratchSlot < 0)
                    scratchSlot = getScratchSpillSlot();

                var cycleBreak = moves[0];
                EmitMove(
                    result,
                    ref nextNodeId,
                    fromBlockId,
                    new RegisterResolvedMove(
                        cycleBreak.Source,
                        RegisterOperand.ForSpillSlot(cycleBreak.Source.RegisterClass, scratchSlot),
                        cycleBreak.SourceValue,
                        cycleBreak.SourceValue,
                        cycleBreak.MoveFlags | MoveFlags.ParallelCopy | MoveFlags.Spill | MoveFlags.Internal),
                    generalScratchRegister,
                    floatScratchRegister,
                    canEmitDirectMemoryMove,
                    "parallel-copy cycle spill",
                    phiCopyFromBlockId,
                    phiCopyToBlockId);

                for (int i = 0; i < moves.Count; i++)
                {
                    if (moves[i].Source.Equals(cycleBreak.Source))
                    {
                        moves[i] = moves[i].WithSource(
                            RegisterOperand.ForSpillSlot(cycleBreak.Source.RegisterClass, scratchSlot),
                            cycleBreak.SourceValue);
                    }
                }
            }

            return result.ToImmutable();
        }

        private static int FindAcyclicMove(List<RegisterResolvedMove> moves)
        {
            for (int i = 0; i < moves.Count; i++)
            {
                var destination = moves[i].Destination;
                bool destinationIsStillSource = false;
                for (int j = 0; j < moves.Count; j++)
                {
                    if (i == j)
                        continue;
                    if (moves[j].Source.Equals(destination))
                    {
                        destinationIsStillSource = true;
                        break;
                    }
                }

                if (!destinationIsStillSource)
                    return i;
            }

            return -1;
        }

        private static void EmitMove(
            ImmutableArray<GenTree>.Builder result,
            ref int nextNodeId,
            int blockId,
            RegisterResolvedMove move,
            MachineRegister generalScratchRegister,
            MachineRegister floatScratchRegister,
            Func<RegisterOperand, RegisterOperand, GenTree?, GenTree?, bool>? canEmitDirectMemoryMove,
            string comment,
            int phiCopyFromBlockId = -1,
            int phiCopyToBlockId = -1)
        {
            if (!RequiresScratch(move.Source, move.Destination) ||
                (canEmitDirectMemoryMove?.Invoke(move.Destination, move.Source, move.DestinationValue, move.SourceValue) == true))
            {
                result.Add(GenTreeLirFactory.Move(
                    nextNodeId++,
                    blockId,
                    result.Count,
                    move.Destination,
                    move.Source,
                    move.DestinationValue,
                    move.SourceValue,
                    comment,
                    move.MoveFlags | MoveFlags.ParallelCopy,
                    phiCopyFromBlockId,
                    phiCopyToBlockId));
                return;
            }

            var moveClass = move.Destination.RegisterClass is RegisterClass.General or RegisterClass.Float
                ? move.Destination.RegisterClass
                : move.Source.RegisterClass;
            if (moveClass is not (RegisterClass.General or RegisterClass.Float))
                throw new InvalidOperationException($"Invalid parallel-copy move without a concrete register class: {move.Source} -> {move.Destination}.");

            var scratch = RegisterOperand.ForRegister(
                moveClass == RegisterClass.Float ? floatScratchRegister : generalScratchRegister);

            result.Add(GenTreeLirFactory.Move(
                nextNodeId++,
                blockId,
                result.Count,
                scratch,
                move.Source,
                destinationValue: null,
                sourceValue: move.SourceValue,
                comment: comment + " reload",
                moveFlags: move.MoveFlags | MoveFlags.ParallelCopy | MoveFlags.Reload,
                phiCopyFromBlockId: phiCopyFromBlockId,
                phiCopyToBlockId: phiCopyToBlockId));

            result.Add(GenTreeLirFactory.Move(
                nextNodeId++,
                blockId,
                result.Count,
                move.Destination,
                scratch,
                move.DestinationValue,
                sourceValue: null,
                comment: comment + " store",
                moveFlags: move.MoveFlags | MoveFlags.ParallelCopy | MoveFlags.Spill,
                phiCopyFromBlockId: phiCopyFromBlockId,
                phiCopyToBlockId: phiCopyToBlockId));
        }

        private static bool RequiresScratch(RegisterOperand source, RegisterOperand destination)
            => !source.IsNone && !destination.IsNone && !source.IsRegister && !destination.IsRegister;

        private static void ValidateScratch(MachineRegister register, RegisterClass registerClass)
        {
            if (!MachineRegisters.IsRegisterInClass(register, registerClass))
                throw new InvalidOperationException($"Invalid {registerClass} parallel-copy scratch register {register}.");
            if (!MachineRegisters.IsReserved(register))
                throw new InvalidOperationException($"Parallel-copy scratch register {MachineRegisters.Format(register)} must be reserved.");
        }
    }

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
                if (_options.HomeIncomingArguments)
                    AllocateTypedSlots(StackFrameSlotKind.Argument, _method.GenTreeMethod.ArgTypes, argSlots, ref cursor);
                int argHomeSize = cursor - argHomeOffset;

                cursor = AlignUp(cursor, frameAlignment);
                int localOffset = cursor;
                var localSlots = ImmutableArray.CreateBuilder<StackFrameSlot>();
                if (_options.AllocateLocalSlots)
                    AllocateTypedSlots(StackFrameSlotKind.Local, _method.GenTreeMethod.LocalTypes, localSlots, ref cursor);
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

            private bool UsesCalleeSavedRegister()
            {
                for (int i = 0; i < _method.Allocations.Length; i++)
                {
                    if (AllocationUsesCalleeSavedRegister(_method.Allocations[i]))
                        return true;
                }

                return false;
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

            private void AllocateTypedSlots(
                StackFrameSlotKind kind,
                ImmutableArray<RuntimeType> types,
                ImmutableArray<StackFrameSlot>.Builder slots,
                ref int cursor)
            {
                for (int i = 0; i < types.Length; i++)
                {
                    var storage = StorageForType(types[i]);
                    cursor = AlignUp(cursor, storage.Alignment);
                    slots.Add(new StackFrameSlot(kind, i, cursor, storage.Size, storage.Alignment, RegisterClass.Invalid, types[i]));
                    cursor = checked(cursor + storage.Size);
                }
            }

            private void AllocateTempSlots(ImmutableArray<StackFrameSlot>.Builder slots, ref int cursor)
            {
                var temps = _method.GenTreeMethod.Temps;
                for (int i = 0; i < temps.Length; i++)
                {
                    var temp = temps[i];
                    var storage = temp.Type is null
                        ? StorageForStackKind(temp.StackKind)
                        : StorageForType(temp.Type);

                    cursor = AlignUp(cursor, storage.Alignment);
                    slots.Add(new StackFrameSlot(StackFrameSlotKind.Temp, temp.Index, cursor, storage.Size, storage.Alignment, RegisterClass.Invalid, temp.Type));
                    cursor = checked(cursor + storage.Size);
                }
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

    internal static class RegisterPrologEpilogGenerator
    {
        public static RegisterAllocatedMethod GenerateMethod(RegisterAllocatedMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            if (method.HasPrologEpilog)
                return method;

            var builder = new MethodBuilder(method);
            return builder.Run();
        }

        private sealed class MethodBuilder
        {
            private readonly RegisterAllocatedMethod _method;
            private readonly ImmutableArray<RegisterFrameRegion>.Builder _frameRegions = ImmutableArray.CreateBuilder<RegisterFrameRegion>();
            private int _nextNodeId;

            public MethodBuilder(RegisterAllocatedMethod method)
            {
                _method = method;
                _nextNodeId = ComputeNextNodeId(method);
            }

            public RegisterAllocatedMethod Run()
            {
                var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(_method.Blocks.Length);
                var all = ImmutableArray.CreateBuilder<GenTree>(_method.LinearNodes.Length
                    + EstimateFrameNodeCount());

                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var sourceBlock = _method.Blocks[b];
                    var blockLinearNodes = ImmutableArray.CreateBuilder<GenTree>(
                        sourceBlock.LinearNodes.Length + (b == 0 ? EstimatePrologNodeCount() : 0)
                        + EstimateBlockEpilogNodeCount(sourceBlock));

                    if (NeedsFuncletProlog(sourceBlock.Id))
                        AppendProlog(sourceBlock.Id, FuncletIndexForEntryBlock(sourceBlock.Id), blockLinearNodes);

                    for (int i = 0; i < sourceBlock.LinearNodes.Length; i++)
                    {
                        var node = sourceBlock.LinearNodes[i];
                        if (IsReturn(node))
                        {
                            node = NormalizeReturnOperand(sourceBlock.Id, node, blockLinearNodes);
                            AppendEpilog(sourceBlock.Id, FuncletIndexForBlock(sourceBlock.Id), blockLinearNodes);
                        }
                        else if (IsFuncletExit(node))
                        {
                            AppendEpilog(sourceBlock.Id, FuncletIndexForBlock(sourceBlock.Id), blockLinearNodes);
                        }

                        blockLinearNodes.Add(node);
                    }

                    var normalized = NormalizeOrdinals(blockLinearNodes.ToImmutable());
                    for (int i = 0; i < normalized.Length; i++)
                        all.Add(normalized[i]);

                    sourceBlock.SetLinearNodes(normalized);
                    blocks.Add(sourceBlock);
                }

                var allNodes = all.ToImmutable();
                var unwindCodes = BuildUnwindCodes(allNodes);

                return new RegisterAllocatedMethod(
                    _method.GenTreeMethod,
                    blocks.ToImmutable(),
                    allNodes,
                    _method.Allocations,
                    _method.AllocationByNode,
                    _method.InternalRegistersByNodeId,
                    _method.SpillSlotCount,
                    _method.ParallelCopyScratchSpillSlot,
                    _method.StackFrame,
                    hasPrologEpilog: true,
                    unwindCodes: unwindCodes,
                    gcLiveRanges: _method.GcLiveRanges,
                    gcTransitions: _method.GcTransitions,
                    gcInterruptibleRanges: _method.GcInterruptibleRanges,
                    funclets: _method.Funclets,
                    frameRegions: _frameRegions.ToImmutable(),
                    gcReportOnlyLeafFunclet: _method.GcReportOnlyLeafFunclet,
                    lsraNodePositions: _method.LsraNodePositions,
                    lsraBlockStartPositions: _method.LsraBlockStartPositions,
                    lsraBlockEndPositions: _method.LsraBlockEndPositions);
            }

            private static ImmutableArray<RegisterUnwindCode> BuildUnwindCodes(ImmutableArray<GenTree> nodes)
            {
                var result = ImmutableArray.CreateBuilder<RegisterUnwindCode>();

                for (int i = 0; i < nodes.Length; i++)
                {
                    var node = nodes[i];
                    if (node.Kind != GenTreeKind.StackFrameOp)
                        continue;

                    if (!TryMapUnwindCode(node, out var code))
                        continue;

                    result.Add(code);
                }

                result.Sort(static (a, b) =>
                {
                    int c = a.BlockId.CompareTo(b.BlockId);
                    if (c != 0)
                        return c;
                    c = a.Ordinal.CompareTo(b.Ordinal);
                    if (c != 0)
                        return c;
                    return a.NodeId.CompareTo(b.NodeId);
                });

                return result.ToImmutable();
            }

            private static bool TryMapUnwindCode(GenTree node, out RegisterUnwindCode code)
            {
                RegisterUnwindCodeKind kind;
                MachineRegister register = MachineRegister.Invalid;
                int stackOffset = 0;
                int size = 0;

                switch (node.FrameOperation)
                {
                    case FrameOperation.AllocateFrame:
                        kind = RegisterUnwindCodeKind.AllocateStack;
                        size = node.Immediate;
                        break;

                    case FrameOperation.SaveReturnAddress:
                        {
                            kind = RegisterUnwindCodeKind.SaveReturnAddress;
                            if (node.Uses.Length != 0 && node.Uses[0].IsRegister)
                                register = node.Uses[0].Register;
                            if (node.Results.Length != 1)
                            {
                                code = default;
                                return false;
                            }
                            var resultOperand = node.Results[0];
                            stackOffset = resultOperand.FrameOffset;
                            size = resultOperand.FrameSlotSize;
                            break;
                        }

                    case FrameOperation.SaveCalleeSavedRegister:
                        {
                            kind = RegisterUnwindCodeKind.SaveCalleeSavedRegister;
                            if (node.Uses.Length != 0 && node.Uses[0].IsRegister)
                                register = node.Uses[0].Register;
                            if (node.Results.Length != 1)
                            {
                                code = default;
                                return false;
                            }
                            var resultOperand = node.Results[0];
                            stackOffset = resultOperand.FrameOffset;
                            size = resultOperand.FrameSlotSize;
                            break;
                        }

                    case FrameOperation.EstablishFramePointer:
                        kind = RegisterUnwindCodeKind.SetFramePointer;
                        register = MachineRegisters.FramePointer;
                        break;

                    default:
                        code = default;
                        return false;
                }

                code = new RegisterUnwindCode(
                    node.Id,
                    node.BlockId,
                    node.Ordinal,
                    kind,
                    register,
                    stackOffset,
                    size);
                return true;
            }

            private void AppendProlog(int blockId, int funcletIndex, ImmutableArray<GenTree>.Builder nodes)
            {
                int firstNodeId = _nextNodeId;
                if (funcletIndex != 0)
                {
                    nodes.Add(GenTreeLirFactory.Frame(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        FrameOperation.EnterFuncletFrame,
                        _method.StackFrame.UsesFramePointer
                            ? RegisterOperand.ForRegister(MachineRegisters.FramePointer)
                            : RegisterOperand.ForRegister(MachineRegisters.StackPointer),
                        ImmutableArray.Create(RegisterOperand.ForRegister(MachineRegisters.StackPointer)),
                        immediate: 0,
                        comment: "funclet prolog: attach to establisher frame"));

                    _frameRegions.Add(new RegisterFrameRegion(
                        RegisterFrameRegionKind.Prolog,
                        funcletIndex,
                        blockId,
                        firstNodeId,
                        _nextNodeId - 1));
                    return;
                }

                int frameSize = _method.StackFrame.FrameSize;
                if (frameSize > 0)
                {
                    nodes.Add(GenTreeLirFactory.Frame(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        FrameOperation.AllocateFrame,
                        RegisterOperand.ForRegister(MachineRegisters.StackPointer),
                        ImmutableArray.Create(RegisterOperand.ForRegister(MachineRegisters.StackPointer)),
                        frameSize,
                        "prolog: sp -= frameSize"));
                }

                for (int i = 0; i < _method.StackFrame.CalleeSavedSlots.Length; i++)
                {
                    var slot = _method.StackFrame.CalleeSavedSlots[i];
                    bool isReturnAddress = slot.Kind == StackFrameSlotKind.ReturnAddress;
                    nodes.Add(GenTreeLirFactory.Frame(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        isReturnAddress ? FrameOperation.SaveReturnAddress : FrameOperation.SaveCalleeSavedRegister,
                        FrameOperand(slot, RegisterFrameBase.StackPointer),
                        ImmutableArray.Create(RegisterOperand.ForRegister(slot.SavedRegister)),
                        immediate: 0,
                        comment: "prolog: save " + MachineRegisters.Format(slot.SavedRegister)));
                }

                if (_method.StackFrame.UsesFramePointer)
                {
                    nodes.Add(GenTreeLirFactory.Frame(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        FrameOperation.EstablishFramePointer,
                        RegisterOperand.ForRegister(MachineRegisters.FramePointer),
                        ImmutableArray.Create(RegisterOperand.ForRegister(MachineRegisters.StackPointer)),
                        immediate: 0,
                        comment: "prolog: fp = sp"));
                }

                if (_nextNodeId > firstNodeId)
                {
                    _frameRegions.Add(new RegisterFrameRegion(
                        RegisterFrameRegionKind.Prolog,
                        funcletIndex,
                        blockId,
                        firstNodeId,
                        _nextNodeId - 1));
                }

                AppendGcHomeSlotZeroInit(blockId, nodes);
                AppendIncomingArgumentHomeStores(blockId, nodes);
                AppendInitialSsaArgumentValueStores(blockId, nodes);
            }
            private void AppendGcHomeSlotZeroInit(int blockId, ImmutableArray<GenTree>.Builder nodes)
            {
                ZeroFrameSlots(blockId, nodes, _method.StackFrame.LocalSlots);
                ZeroFrameSlots(blockId, nodes, _method.StackFrame.TempSlots);
                ZeroFrameSlots(blockId, nodes, _method.StackFrame.SpillSlots);
                ZeroFrameSlots(blockId, nodes, _method.StackFrame.OutgoingArgumentSlots);
            }

            private void ZeroFrameSlots(int blockId, ImmutableArray<GenTree>.Builder nodes, ImmutableArray<StackFrameSlot> slots)
            {
                for (int i = 0; i < slots.Length; i++)
                    EmitZeroFrameSlot(blockId, nodes, slots[i]);
            }

            private void EmitZeroFrameSlot(int blockId, ImmutableArray<GenTree>.Builder nodes, StackFrameSlot slot)
            {
                int remaining = Math.Max(1, slot.Size);
                int offset = 0;
                while (remaining > 0)
                {
                    int chunk = remaining >= 8 ? 8 : remaining >= 4 ? 4 : remaining >= 2 ? 2 : 1;
                    var destination = RegisterOperand.ForFrameSlot(
                        RegisterClass.General,
                        slot.Kind,
                        RegisterFrameBase.StackPointer,
                        slot.Index,
                        checked(slot.Offset + offset),
                        chunk);

                    nodes.Add(GenTreeLirFactory.Move(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        destination,
                        RegisterOperand.ForRegister(MachineRegisters.Zero),
                        destinationValue: null,
                        sourceValue: null,
                        comment: "prolog: zero GC home slot",
                        moveFlags: MoveFlags.Internal));

                    offset += chunk;
                    remaining -= chunk;
                }
            }

            private static GenStackKind StackKindForType(RuntimeType? type)
            {
                if (type is null)
                    return GenStackKind.Unknown;
                if (type.Kind == RuntimeTypeKind.ByRef)
                    return GenStackKind.ByRef;
                if (type.IsReferenceType || type.Kind == RuntimeTypeKind.TypeParam)
                    return GenStackKind.Ref;
                if (type.Kind == RuntimeTypeKind.Pointer)
                    return GenStackKind.Ptr;
                if (type.Name == "Single")
                    return GenStackKind.R4;
                if (type.Name == "Double")
                    return GenStackKind.R8;
                if (type.SizeOf <= 4)
                    return GenStackKind.I4;
                if (type.SizeOf <= 8)
                    return GenStackKind.I8;
                return GenStackKind.Value;
            }

            private static bool NeedsHomeGcReporting(RuntimeType? type, GenStackKind stackKind)
            {
                if (type is not null)
                {
                    if (type.Kind == RuntimeTypeKind.ByRef || type.Kind == RuntimeTypeKind.TypeParam || type.IsReferenceType)
                        return true;
                    if (type.IsValueType && type.ContainsGcPointers)
                        return true;
                }

                return stackKind is GenStackKind.Ref or GenStackKind.ByRef or GenStackKind.Null;
            }

            private void AppendIncomingArgumentHomeStores(int blockId, ImmutableArray<GenTree>.Builder nodes)
            {
                var runtimeMethod = _method.GenTreeMethod.RuntimeMethod;
                var argTypes = _method.GenTreeMethod.ArgTypes;
                if (argTypes.IsDefaultOrEmpty || _method.StackFrame.ArgumentSlots.IsDefaultOrEmpty)
                    return;

                int generalArgumentIndex = 0;
                int floatArgumentIndex = 0;
                int incomingStackArgumentIndex = 0;
                int hiddenReturnBufferIndex = MachineAbi.HiddenReturnBufferInsertionIndex(runtimeMethod, argTypes.Length);

                for (int i = 0; i < argTypes.Length; i++)
                {
                    if (hiddenReturnBufferIndex == i)
                        ConsumeIncomingHiddenReturnBuffer(ref generalArgumentIndex, ref floatArgumentIndex, ref incomingStackArgumentIndex);

                    RuntimeType argType = argTypes[i];
                    var argAbi = MachineAbi.ClassifyValue(argType, MachineAbi.StackKindForType(argType), isReturn: false);
                    if (argAbi.PassingKind == AbiValuePassingKind.Void)
                        continue;

                    if (!_method.StackFrame.TryGetArgumentSlot(i, out StackFrameSlot homeSlot))
                        continue;

                    if (argAbi.PassingKind == AbiValuePassingKind.ScalarRegister)
                    {
                        var registerClass = argAbi.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : argAbi.RegisterClass;
                        var source = MachineAbi.AssignScalarArgumentLocation(
                            registerClass,
                            argAbi.Size <= 0 ? TargetArchitecture.PointerSize : argAbi.Size,
                            ref generalArgumentIndex,
                            ref floatArgumentIndex,
                            ref incomingStackArgumentIndex);

                        EmitIncomingArgumentHomeStore(blockId, nodes, homeSlot, source, 0, registerClass, source.Size);
                        continue;
                    }

                    if (argAbi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        int aggregateStackSlot = -1;
                        int aggregateStackBaseOffset = 0;
                        var segments = MachineAbi.GetRegisterSegments(argAbi);
                        for (int s = 0; s < segments.Length; s++)
                        {
                            var segment = segments[s];
                            var source = MachineAbi.AssignAggregateSegmentArgumentLocation(
                                segment,
                                ref generalArgumentIndex,
                                ref floatArgumentIndex,
                                ref incomingStackArgumentIndex,
                                ref aggregateStackSlot,
                                ref aggregateStackBaseOffset);

                            EmitIncomingArgumentHomeStore(blockId, nodes, homeSlot, source, segment.Offset, segment.RegisterClass, segment.Size);
                        }
                        continue;
                    }

                    int stackSize = argAbi.Size <= 0 ? TargetArchitecture.PointerSize : argAbi.Size;
                    var stackSource = AbiArgumentLocation.ForStack(
                        RegisterClass.General,
                        incomingStackArgumentIndex++,
                        0,
                        stackSize);

                    EmitIncomingArgumentHomeStore(blockId, nodes, homeSlot, stackSource, 0, RegisterClass.General, stackSize);
                }

                if (hiddenReturnBufferIndex == argTypes.Length)
                    ConsumeIncomingHiddenReturnBuffer(ref generalArgumentIndex, ref floatArgumentIndex, ref incomingStackArgumentIndex);
            }


            private void AppendInitialSsaArgumentValueStores(int blockId, ImmutableArray<GenTree>.Builder nodes)
            {
                var method = _method.GenTreeMethod;
                var argTypes = method.ArgTypes;
                if (argTypes.IsDefaultOrEmpty || _method.Allocations.IsDefaultOrEmpty)
                    return;

                var emitted = new HashSet<GenTreeValueKey>();

                for (int i = 0; i < _method.Allocations.Length; i++)
                {
                    var allocation = _method.Allocations[i];
                    var key = allocation.ValueKey;
                    if (!IsInitialSsaArgumentValue(method, key))
                        continue;
                    if (!emitted.Add(key))
                        continue;

                    int argumentIndex = key.SsaSlot.Index;

                    if (!method.ValueInfoByNode.TryGetValue(key, out var valueInfo))
                        continue;

                    RuntimeType? argumentType;
                    GenStackKind argumentStackKind;
                    AbiValueInfo argumentAbi;
                    ImmutableArray<RegisterOperand> sources;

                    if ((uint)argumentIndex < (uint)argTypes.Length)
                    {
                        argumentType = valueInfo.Type ?? argTypes[argumentIndex];
                        argumentStackKind = valueInfo.StackKind == GenStackKind.Unknown
                            ? MachineAbi.StackKindForType(argumentType)
                            : valueInfo.StackKind;

                        argumentAbi = MachineAbi.ClassifyValue(argumentType, argumentStackKind, isReturn: false);
                        if (argumentAbi.PassingKind == AbiValuePassingKind.Void)
                            continue;

                        sources = GetIncomingArgumentOperands(
                            argumentIndex,
                            argumentType,
                            argumentStackKind,
                            valueInfo.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : valueInfo.RegisterClass,
                            argumentAbi);
                    }
                    else
                    {
                        if (!TryGetIncomingPromotedArgumentFieldOperand(
                                key,
                                valueInfo.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : valueInfo.RegisterClass,
                                out argumentType,
                                out argumentStackKind,
                                out var promotedFieldSource))
                        {
                            continue;
                        }

                        argumentAbi = MachineAbi.ClassifyValue(argumentType, argumentStackKind, isReturn: false);
                        if (argumentAbi.PassingKind == AbiValuePassingKind.Void)
                            continue;

                        sources = ImmutableArray.Create(promotedFieldSource);
                    }

                    int position = Math.Max(0, allocation.DefinitionPosition);

                    if (argumentAbi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        var destination = allocation.ValueLocationAt(position, argumentAbi);
                        EmitInitialSsaArgumentFragments(blockId, nodes, allocation.Value, destination, sources);
                        continue;
                    }

                    if (sources.Length != 1)
                        throw new InvalidOperationException("Scalar incoming argument must have exactly one ABI source operand.");

                    var scalarDestination = allocation.LocationAt(position);
                    EmitInitialSsaArgumentMove(blockId, nodes, allocation.Value, scalarDestination, sources[0]);
                }
            }

            private static bool IsInitialSsaArgumentValue(GenTreeMethod method, GenTreeValueKey key)
            {
                if (!key.IsSsaValue || key.SsaSlot.Kind != SsaSlotKind.Arg)
                    return false;

                if (!method.ValueInfoByNode.TryGetValue(key, out var valueInfo))
                    return false;

                return valueInfo.DefinitionBlockId < 0 && valueInfo.DefinitionNodeId < 0;
            }

            private void EmitInitialSsaArgumentFragments(
                int blockId,
                ImmutableArray<GenTree>.Builder nodes,
                GenTree value,
                RegisterValueLocation destination,
                ImmutableArray<RegisterOperand> sources)
            {
                if (destination.IsEmpty)
                    return;
                if (destination.Count != sources.Length)
                    throw new InvalidOperationException("Initial SSA argument ABI source count does not match allocated destination fragment count.");

                for (int i = 0; i < sources.Length; i++)
                    EmitInitialSsaArgumentMove(blockId, nodes, value, destination[i], sources[i]);
            }

            private void EmitInitialSsaArgumentMove(
                int blockId,
                ImmutableArray<GenTree>.Builder nodes,
                GenTree value,
                RegisterOperand destination,
                RegisterOperand source)
            {
                if (destination.IsNone || source.IsNone || destination.Equals(source))
                    return;

                if (destination.IsMemoryOperand && source.IsMemoryOperand)
                {
                    var scratch = RegisterOperand.ForRegister(SelectInitialSsaArgumentCopyScratch(destination, source));
                    nodes.Add(GenTreeLirFactory.Move(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        scratch,
                        source,
                        destinationValue: null,
                        sourceValue: null,
                        comment: "prolog: initialize initial SSA argument value reload",
                        moveFlags: MoveFlags.AbiArgument | MoveFlags.Internal | MoveFlags.Reload));

                    nodes.Add(GenTreeLirFactory.Move(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        destination,
                        scratch,
                        destinationValue: value,
                        sourceValue: null,
                        comment: "prolog: initialize initial SSA argument value store",
                        moveFlags: MoveFlags.AbiArgument | MoveFlags.Internal | MoveFlags.Spill));
                    return;
                }

                nodes.Add(GenTreeLirFactory.Move(
                    _nextNodeId++,
                    blockId,
                    nodes.Count,
                    destination,
                    source,
                    destinationValue: value,
                    sourceValue: null,
                    comment: "prolog: initialize initial SSA argument value",
                    moveFlags: MoveFlags.AbiArgument | MoveFlags.Internal));
            }

            private static MachineRegister SelectInitialSsaArgumentCopyScratch(RegisterOperand destination, RegisterOperand source)
            {
                if (destination.RegisterClass == RegisterClass.Float || source.RegisterClass == RegisterClass.Float)
                    return MachineRegisters.FloatBackendScratch;
                return MachineRegisters.BackendScratch;
            }

            private bool TryGetIncomingPromotedArgumentFieldOperand(
                GenTreeValueKey key,
                RegisterClass fieldRegisterClass,
                out RuntimeType? fieldType,
                out GenStackKind fieldStackKind,
                out RegisterOperand source)
            {
                fieldType = null;
                fieldStackKind = GenStackKind.Unknown;
                source = default;

                var method = _method.GenTreeMethod;
                if (!key.SsaSlot.HasLclNum || (uint)key.SsaSlot.LclNum >= (uint)method.AllLocalDescriptors.Length)
                    return false;

                var fieldDescriptor = method.AllLocalDescriptors[key.SsaSlot.LclNum];
                if (fieldDescriptor.Kind != GenLocalKind.Argument || fieldDescriptor.Category != GenLocalCategory.PromotedStructField || fieldDescriptor.ParentLclNum < 0)
                    return false;

                GenLocalDescriptor? parentDescriptor = null;
                if ((uint)fieldDescriptor.ParentLclNum < (uint)method.AllLocalDescriptors.Length)
                    parentDescriptor = method.AllLocalDescriptors[fieldDescriptor.ParentLclNum];

                if (parentDescriptor is null || parentDescriptor.Kind != GenLocalKind.Argument)
                    return false;
                if ((uint)parentDescriptor.Index >= (uint)method.ArgTypes.Length)
                    return false;

                fieldType = fieldDescriptor.Type;
                fieldStackKind = fieldDescriptor.StackKind;

                return TryGetIncomingArgumentFieldOperand(
                    parentDescriptor.Index,
                    parentDescriptor.Type,
                    parentDescriptor.StackKind,
                    fieldDescriptor.FieldOffset,
                    Math.Max(1, fieldDescriptor.FieldSize),
                    fieldRegisterClass,
                    out source);
            }

            private bool TryGetIncomingArgumentFieldOperand(
                int parentArgumentIndex,
                RuntimeType? parentArgumentType,
                GenStackKind parentStackKind,
                int fieldOffset,
                int fieldSize,
                RegisterClass fieldRegisterClass,
                out RegisterOperand source)
            {
                source = default;
                if (parentArgumentIndex < 0 || fieldOffset < 0 || fieldSize <= 0)
                    return false;

                int generalArgumentIndex = 0;
                int floatArgumentIndex = 0;
                int incomingStackArgumentIndex = 0;
                int hiddenReturnBufferIndex = MachineAbi.HiddenReturnBufferInsertionIndex(
                    _method.GenTreeMethod.RuntimeMethod,
                    _method.GenTreeMethod.ArgTypes.Length);

                for (int i = 0; i <= parentArgumentIndex; i++)
                {
                    if (hiddenReturnBufferIndex == i)
                        ConsumeIncomingHiddenReturnBuffer(ref generalArgumentIndex, ref floatArgumentIndex, ref incomingStackArgumentIndex);

                    RuntimeType currentType = _method.GenTreeMethod.ArgTypes[i];
                    GenStackKind currentStackKind = i == parentArgumentIndex ? parentStackKind : MachineAbi.StackKindForType(currentType);
                    var abi = i == parentArgumentIndex
                        ? MachineAbi.ClassifyValue(parentArgumentType, currentStackKind, isReturn: false)
                        : MachineAbi.ClassifyValue(currentType, currentStackKind, isReturn: false);

                    if (abi.PassingKind == AbiValuePassingKind.Void)
                        continue;

                    if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                    {
                        var registerClass = abi.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : abi.RegisterClass;
                        var location = MachineAbi.AssignScalarArgumentLocation(
                            registerClass,
                            abi.Size <= 0 ? TargetArchitecture.PointerSize : abi.Size,
                            ref generalArgumentIndex,
                            ref floatArgumentIndex,
                            ref incomingStackArgumentIndex);

                        if (i != parentArgumentIndex)
                            continue;

                        int scalarSize = Math.Max(1, location.Size);
                        if (fieldOffset == 0 && fieldSize == scalarSize)
                        {
                            source = OperandForIncomingAbiLocation(location);
                            return true;
                        }

                        int fieldEnd = fieldOffset + fieldSize;
                        if (fieldOffset < 0 || fieldEnd > scalarSize)
                            return false;

                        // Small structs can be passed as one scalar ABI register even though
                        // their promoted fields are smaller than that register. The prolog
                        // has already materialized the incoming argument into its argument
                        // home before initial SSA argument values are initialized, so use the
                        // home slot as the dependent source for those promoted field values.
                        if (_method.StackFrame.TryGetArgumentSlot(parentArgumentIndex, out StackFrameSlot homeSlot))
                        {
                            source = RegisterOperand.ForFrameSlot(
                                fieldRegisterClass == RegisterClass.Invalid ? registerClass : fieldRegisterClass,
                                StackFrameSlotKind.Argument,
                                RegisterFrameBase.StackPointer,
                                homeSlot.Index,
                                checked(homeSlot.Offset + fieldOffset),
                                fieldSize);

                            return true;
                        }

                        return false;
                    }

                    if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        int aggregateStackSlot = -1;
                        int aggregateStackBaseOffset = 0;
                        var segments = MachineAbi.GetRegisterSegments(abi);
                        for (int s = 0; s < segments.Length; s++)
                        {
                            var segment = segments[s];
                            var location = MachineAbi.AssignAggregateSegmentArgumentLocation(
                                segment,
                                ref generalArgumentIndex,
                                ref floatArgumentIndex,
                                ref incomingStackArgumentIndex,
                                ref aggregateStackSlot,
                                ref aggregateStackBaseOffset);

                            if (i != parentArgumentIndex)
                                continue;

                            int segmentStart = segment.Offset;
                            int segmentEnd = segment.Offset + Math.Max(1, segment.Size);
                            int fieldEnd = fieldOffset + fieldSize;
                            if (fieldOffset < segmentStart || fieldEnd > segmentEnd)
                                continue;

                            if (location.IsRegister)
                            {
                                if (fieldOffset == segment.Offset && fieldSize == Math.Max(1, segment.Size))
                                {
                                    source = OperandForIncomingAbiLocation(location);
                                    return true;
                                }

                                if (_method.StackFrame.TryGetArgumentSlot(parentArgumentIndex, out StackFrameSlot homeSlot))
                                {
                                    source = RegisterOperand.ForFrameSlot(
                                        fieldRegisterClass == RegisterClass.Invalid ? segment.RegisterClass : fieldRegisterClass,
                                        StackFrameSlotKind.Argument,
                                        RegisterFrameBase.StackPointer,
                                        homeSlot.Index,
                                        checked(homeSlot.Offset + fieldOffset),
                                        fieldSize);

                                    return true;
                                }

                                return false;
                            }

                            var adjusted = AbiArgumentLocation.ForStack(
                                fieldRegisterClass == RegisterClass.Invalid ? segment.RegisterClass : fieldRegisterClass,
                                location.StackSlotIndex,
                                checked(location.StackOffset + (fieldOffset - segment.Offset)),
                                fieldSize);
                            source = OperandForIncomingAbiLocation(adjusted);
                            return true;
                        }

                        if (i == parentArgumentIndex)
                            return false;
                        continue;
                    }

                    int stackSize = abi.Size <= 0 ? TargetArchitecture.PointerSize : abi.Size;
                    var stackLocation = AbiArgumentLocation.ForStack(
                        fieldRegisterClass == RegisterClass.Invalid ? RegisterClass.General : fieldRegisterClass,
                        incomingStackArgumentIndex++,
                        i == parentArgumentIndex ? fieldOffset : 0,
                        i == parentArgumentIndex ? fieldSize : stackSize);

                    if (i == parentArgumentIndex)
                    {
                        source = OperandForIncomingAbiLocation(stackLocation);
                        return true;
                    }
                }

                return false;
            }

            private ImmutableArray<RegisterOperand> GetIncomingArgumentOperands(
                int argumentIndex,
                RuntimeType? argumentType,
                GenStackKind stackKind,
                RegisterClass argumentClass,
                AbiValueInfo argumentAbi)
            {
                int generalArgumentIndex = 0;
                int floatArgumentIndex = 0;
                int incomingStackArgumentIndex = 0;
                int hiddenReturnBufferIndex = MachineAbi.HiddenReturnBufferInsertionIndex(
                    _method.GenTreeMethod.RuntimeMethod,
                    _method.GenTreeMethod.ArgTypes.Length);

                for (int i = 0; i <= argumentIndex; i++)
                {
                    if (hiddenReturnBufferIndex == i)
                        ConsumeIncomingHiddenReturnBuffer(ref generalArgumentIndex, ref floatArgumentIndex, ref incomingStackArgumentIndex);

                    RuntimeType currentType = _method.GenTreeMethod.ArgTypes[i];
                    GenStackKind currentStackKind = i == argumentIndex ? stackKind : MachineAbi.StackKindForType(currentType);
                    var abi = i == argumentIndex
                        ? argumentAbi
                        : MachineAbi.ClassifyValue(currentType, currentStackKind, isReturn: false);

                    if (abi.PassingKind == AbiValuePassingKind.Void)
                        continue;

                    if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                    {
                        var registerClass = abi.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : abi.RegisterClass;
                        var source = MachineAbi.AssignScalarArgumentLocation(
                            registerClass,
                            abi.Size <= 0 ? TargetArchitecture.PointerSize : abi.Size,
                            ref generalArgumentIndex,
                            ref floatArgumentIndex,
                            ref incomingStackArgumentIndex);

                        if (i == argumentIndex)
                            return ImmutableArray.Create(OperandForIncomingAbiLocation(source));
                        continue;
                    }

                    if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        int aggregateStackSlot = -1;
                        int aggregateStackBaseOffset = 0;
                        var segments = MachineAbi.GetRegisterSegments(abi);
                        var operands = i == argumentIndex
                            ? ImmutableArray.CreateBuilder<RegisterOperand>(segments.Length)
                            : null;

                        for (int s = 0; s < segments.Length; s++)
                        {
                            var source = MachineAbi.AssignAggregateSegmentArgumentLocation(
                                segments[s],
                                ref generalArgumentIndex,
                                ref floatArgumentIndex,
                                ref incomingStackArgumentIndex,
                                ref aggregateStackSlot,
                                ref aggregateStackBaseOffset);

                            operands?.Add(OperandForIncomingAbiLocation(source));
                        }

                        if (i == argumentIndex)
                            return operands!.ToImmutable();
                        continue;
                    }

                    int stackSize = abi.Size <= 0 ? TargetArchitecture.PointerSize : abi.Size;
                    var stackSource = AbiArgumentLocation.ForStack(
                        argumentClass == RegisterClass.Invalid ? RegisterClass.General : argumentClass,
                        incomingStackArgumentIndex++,
                        0,
                        stackSize);

                    if (i == argumentIndex)
                        return ImmutableArray.Create(OperandForIncomingAbiLocation(stackSource));
                }

                if (hiddenReturnBufferIndex == argumentIndex + 1)
                    ConsumeIncomingHiddenReturnBuffer(ref generalArgumentIndex, ref floatArgumentIndex, ref incomingStackArgumentIndex);

                throw new InvalidOperationException("Invalid initial SSA argument index " + argumentIndex.ToString() + ".");
            }

            private void EmitIncomingArgumentHomeStore(
                int blockId,
                ImmutableArray<GenTree>.Builder nodes,
                StackFrameSlot homeSlot,
                AbiArgumentLocation sourceLocation,
                int destinationOffset,
                RegisterClass registerClass,
                int size)
            {
                int actualSize = Math.Max(1, size);
                var destination = RegisterOperand.ForFrameSlot(
                    registerClass == RegisterClass.Invalid ? RegisterClass.General : registerClass,
                    StackFrameSlotKind.Argument,
                    RegisterFrameBase.StackPointer,
                    homeSlot.Index,
                    checked(homeSlot.Offset + destinationOffset),
                    actualSize);

                var source = OperandForIncomingAbiLocation(sourceLocation);
                if (destination.Equals(source))
                    return;

                if (destination.IsMemoryOperand && source.IsMemoryOperand)
                {
                    var scratch = RegisterOperand.ForRegister(SelectInitialSsaArgumentCopyScratch(destination, source));
                    nodes.Add(GenTreeLirFactory.Move(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        scratch,
                        source,
                        destinationValue: null,
                        sourceValue: null,
                        comment: "prolog: home incoming argument reload",
                        moveFlags: MoveFlags.AbiArgument | MoveFlags.Internal | MoveFlags.Reload));

                    nodes.Add(GenTreeLirFactory.Move(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        destination,
                        scratch,
                        destinationValue: null,
                        sourceValue: null,
                        comment: "prolog: home incoming argument store",
                        moveFlags: MoveFlags.AbiArgument | MoveFlags.Internal | MoveFlags.Spill));
                    return;
                }

                nodes.Add(GenTreeLirFactory.Move(
                    _nextNodeId++,
                    blockId,
                    nodes.Count,
                    destination,
                    source,
                    destinationValue: null,
                    sourceValue: null,
                    comment: "prolog: home incoming argument",
                    moveFlags: MoveFlags.AbiArgument | MoveFlags.Internal));
            }

            private static RegisterOperand OperandForIncomingAbiLocation(AbiArgumentLocation location)
            {
                if (location.IsRegister)
                    return RegisterOperand.ForRegister(location.Register);

                return RegisterOperand.ForFrameSlot(
                    location.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : location.RegisterClass,
                    StackFrameSlotKind.Argument,
                    RegisterFrameBase.IncomingArgumentBase,
                    location.StackSlotIndex,
                    checked(location.StackSlotIndex * MachineAbi.StackArgumentSlotSize + location.StackOffset),
                    Math.Max(1, location.Size));
            }

            private static void ConsumeIncomingHiddenReturnBuffer(
                ref int generalArgumentIndex,
                ref int floatArgumentIndex,
                ref int incomingStackArgumentIndex)
            {
                _ = MachineAbi.AssignScalarArgumentLocation(
                    RegisterClass.General,
                    TargetArchitecture.PointerSize,
                    ref generalArgumentIndex,
                    ref floatArgumentIndex,
                    ref incomingStackArgumentIndex);
            }
            private GenTree NormalizeReturnOperand(
                int blockId,
                GenTree returnNode,
                ImmutableArray<GenTree>.Builder nodes)
            {
                if (returnNode.Uses.Length == 0)
                    return returnNode;

                if (returnNode.RegisterUses.Length == 0)
                    throw new InvalidOperationException("Register return node with operands must retain linear IR return value mapping.");

                var returnValue = returnNode.RegisterUses[0];
                var valueInfo = _method.GenTreeMethod.GetValueInfo(returnValue);
                var abi = MachineAbi.ClassifyValue(valueInfo.Type, valueInfo.StackKind, isReturn: true);

                if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    var segments = MachineAbi.GetRegisterSegments(abi);
                    if (returnNode.Uses.Length != segments.Length || returnNode.RegisterUses.Length != segments.Length)
                        throw new InvalidOperationException("Multi-register return node must contain one use per ABI return fragment.");

                    var normalizedUses = ImmutableArray.CreateBuilder<RegisterOperand>(segments.Length);
                    int generalReturnIndex = 0;
                    int floatReturnIndex = 0;
                    for (int i = 0; i < segments.Length; i++)
                    {
                        if (!returnNode.RegisterUses[i].Equals(returnValue))
                            throw new InvalidOperationException("Multi-register return fragments must all map to the same GenTree value.");

                        var target = RegisterOperand.ForRegister(
                            GetSegmentReturnRegister(segments[i].RegisterClass, ref generalReturnIndex, ref floatReturnIndex));
                        var source = returnNode.Uses[i];
                        if (!source.Equals(target))
                        {
                            nodes.Add(GenTreeLirFactory.Move(
                                _nextNodeId++,
                                blockId,
                                nodes.Count,
                                target,
                                source,
                                destinationValue: returnValue,
                                sourceValue: returnValue,
                                comment: "return struct fragment to ABI register",
                                moveFlags: MoveFlags.AbiReturn));
                        }
                        normalizedUses.Add(target);
                    }

                    return returnNode.WithOperands(
                        returnNode.Results,
                        normalizedUses.ToImmutable(),
                        returnNode.RegisterResults,
                        returnNode.RegisterUses);
                }

                if (abi.PassingKind != AbiValuePassingKind.ScalarRegister)
                {
                    return returnNode;
                }

                if (returnNode.Uses.Length != 1 || returnNode.RegisterUses.Length != 1)
                    throw new InvalidOperationException("Scalar register return node must have exactly one value use.");

                var returnRegister = abi.RegisterClass == RegisterClass.Float
                    ? MachineRegisters.FloatReturnValue0
                    : MachineRegisters.ReturnValue0;
                var returnOperand = RegisterOperand.ForRegister(returnRegister);
                var sourceOperand = returnNode.Uses[0];

                if (!sourceOperand.Equals(returnOperand))
                {
                    nodes.Add(GenTreeLirFactory.Move(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        returnOperand,
                        sourceOperand,
                        destinationValue: returnValue,
                        sourceValue: returnValue,
                        comment: "return value to ABI register",
                        moveFlags: MoveFlags.AbiReturn));
                }

                return returnNode.WithOperands(
                    returnNode.Results,
                    ImmutableArray.Create(returnOperand),
                    returnNode.RegisterResults,
                    returnNode.RegisterUses);
            }

            private static MachineRegister GetSegmentReturnRegister(RegisterClass registerClass, ref int generalIndex, ref int floatIndex)
            {
                if (registerClass == RegisterClass.Float)
                {
                    int index = floatIndex++;
                    return index switch
                    {
                        0 => MachineRegisters.FloatReturnValue0,
                        1 => MachineRegisters.FloatReturnValue1,
                        _ => throw new InvalidOperationException("Not enough float return registers for aggregate return."),
                    };
                }

                if (registerClass == RegisterClass.General)
                {
                    int index = generalIndex++;
                    return index switch
                    {
                        0 => MachineRegisters.ReturnValue0,
                        1 => MachineRegisters.ReturnValue1,
                        _ => throw new InvalidOperationException("Not enough integer return registers for aggregate return."),
                    };
                }

                throw new InvalidOperationException($"Invalid return fragment register class {registerClass}.");
            }

            private void AppendEpilog(int blockId, int funcletIndex, ImmutableArray<GenTree>.Builder nodes)
            {
                int firstNodeId = _nextNodeId;
                if (funcletIndex != 0)
                {
                    nodes.Add(GenTreeLirFactory.Frame(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        FrameOperation.LeaveFuncletFrame,
                        RegisterOperand.ForRegister(MachineRegisters.StackPointer),
                        _method.StackFrame.UsesFramePointer
                            ? ImmutableArray.Create(RegisterOperand.ForRegister(MachineRegisters.FramePointer))
                            : ImmutableArray.Create(RegisterOperand.ForRegister(MachineRegisters.StackPointer)),
                        immediate: 0,
                        comment: "funclet epilog: detach from establisher frame"));

                    _frameRegions.Add(new RegisterFrameRegion(
                        RegisterFrameRegionKind.Epilog,
                        funcletIndex,
                        blockId,
                        firstNodeId,
                        _nextNodeId - 1));
                    return;
                }

                int frameSize = _method.StackFrame.FrameSize;
                if (_method.StackFrame.UsesFramePointer)
                {
                    nodes.Add(GenTreeLirFactory.Frame(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        FrameOperation.RestoreStackPointerFromFramePointer,
                        RegisterOperand.ForRegister(MachineRegisters.StackPointer),
                        ImmutableArray.Create(RegisterOperand.ForRegister(MachineRegisters.FramePointer)),
                        immediate: 0,
                        comment: "epilog: sp = fp"));
                }

                for (int i = _method.StackFrame.CalleeSavedSlots.Length - 1; i >= 0; i--)
                {
                    var slot = _method.StackFrame.CalleeSavedSlots[i];
                    bool isReturnAddress = slot.Kind == StackFrameSlotKind.ReturnAddress;
                    nodes.Add(GenTreeLirFactory.Frame(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        isReturnAddress ? FrameOperation.RestoreReturnAddress : FrameOperation.RestoreCalleeSavedRegister,
                        RegisterOperand.ForRegister(slot.SavedRegister),
                        ImmutableArray.Create(FrameOperand(slot, RegisterFrameBase.StackPointer)),
                        immediate: 0,
                        comment: "epilog: restore " + MachineRegisters.Format(slot.SavedRegister)));
                }

                if (frameSize > 0)
                {
                    nodes.Add(GenTreeLirFactory.Frame(
                        _nextNodeId++,
                        blockId,
                        nodes.Count,
                        FrameOperation.FreeFrame,
                        RegisterOperand.ForRegister(MachineRegisters.StackPointer),
                        ImmutableArray.Create(RegisterOperand.ForRegister(MachineRegisters.StackPointer)),
                        frameSize,
                        "epilog: sp += frameSize"));
                }

                if (_nextNodeId > firstNodeId)
                {
                    _frameRegions.Add(new RegisterFrameRegion(
                        RegisterFrameRegionKind.Epilog,
                        funcletIndex,
                        blockId,
                        firstNodeId,
                        _nextNodeId - 1));
                }
            }

            private static RegisterOperand FrameOperand(StackFrameSlot slot, RegisterFrameBase frameBase)
                => RegisterOperand.ForFrameSlot(
                    slot.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : slot.RegisterClass,
                    slot.Kind,
                    frameBase,
                    slot.Index,
                    slot.Offset,
                    slot.Size);

            private bool NeedsFuncletProlog(int blockId)
            {
                if (blockId == 0)
                    return true;

                for (int i = 0; i < _method.Funclets.Length; i++)
                {
                    var funclet = _method.Funclets[i];
                    if (!funclet.IsRoot && funclet.EntryBlockId == blockId)
                        return true;
                }

                return false;
            }

            private int FuncletIndexForEntryBlock(int blockId)
            {
                for (int i = 0; i < _method.Funclets.Length; i++)
                {
                    if (_method.Funclets[i].EntryBlockId == blockId)
                        return _method.Funclets[i].Index;
                }
                return 0;
            }

            private int FuncletIndexForBlock(int blockId)
            {
                for (int i = 0; i < _method.Funclets.Length; i++)
                {
                    var blocks = _method.Funclets[i].BlockIds;
                    for (int b = 0; b < blocks.Length; b++)
                    {
                        if (blocks[b] == blockId)
                            return _method.Funclets[i].Index;
                    }
                }
                return 0;
            }

            private static bool IsFuncletExit(GenTree node)
                => node.Kind == GenTreeKind.EndFinally;

            private static bool IsReturn(GenTree node)
                => node.Kind == GenTreeKind.Return;

            private static ImmutableArray<GenTree> NormalizeOrdinals(ImmutableArray<GenTree> nodes)
            {
                var result = ImmutableArray.CreateBuilder<GenTree>(nodes.Length);
                for (int i = 0; i < nodes.Length; i++)
                    result.Add(nodes[i].WithOrdinal(i));
                return result.ToImmutable();
            }

            private int EstimateFrameNodeCount()
            {
                int result = EstimatePrologNodeCount();
                for (int i = 0; i < _method.Blocks.Length; i++)
                    result += EstimateBlockEpilogNodeCount(_method.Blocks[i]);
                return result;
            }

            private int EstimatePrologNodeCount()
            {
                int result = _method.StackFrame.CalleeSavedSlots.Length;
                if (_method.StackFrame.FrameSize > 0)
                    result += 2;
                result += EstimateGcHomeSlotZeroInitNodeCount();
                return result;
            }

            private int EstimateGcHomeSlotZeroInitNodeCount()
            {
                int result = 0;
                for (int i = 0; i < _method.StackFrame.LocalSlots.Length; i++)
                    if (NeedsHomeGcReporting(_method.StackFrame.LocalSlots[i].Type, StackKindForType(_method.StackFrame.LocalSlots[i].Type)))
                        result += Math.Max(1, (_method.StackFrame.LocalSlots[i].Size + 7) / 8);
                for (int i = 0; i < _method.StackFrame.TempSlots.Length; i++)
                    if (NeedsHomeGcReporting(_method.StackFrame.TempSlots[i].Type, StackKindForType(_method.StackFrame.TempSlots[i].Type)))
                        result += Math.Max(1, (_method.StackFrame.TempSlots[i].Size + 7) / 8);
                return result;
            }

            private int EstimateBlockEpilogNodeCount(GenTreeBlock block)
            {
                int perReturn = _method.StackFrame.CalleeSavedSlots.Length;
                if (_method.StackFrame.FrameSize > 0)
                    perReturn += 2;

                int result = 0;
                for (int i = 0; i < block.LinearNodes.Length; i++)
                {
                    if (IsReturn(block.LinearNodes[i]))
                    {
                        result += perReturn;
                        if (block.LinearNodes[i].Uses.Length != 0)
                            result += 1;
                    }
                }
                return result;
            }

            private static int ComputeNextNodeId(RegisterAllocatedMethod method)
            {
                int max = -1;
                for (int i = 0; i < method.LinearNodes.Length; i++)
                {
                    if (method.LinearNodes[i].Id > max)
                        max = method.LinearNodes[i].Id;
                }

                return checked(max + 1);
            }
        }
    }

    internal static class RegisterGcInfoBuilder
    {
        public static RegisterAllocatedMethod AttachMethod(RegisterAllocatedMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            var builder = new MethodBuilder(method);
            return builder.Run();
        }

        private sealed class MethodBuilder
        {
            private readonly RegisterAllocatedMethod _method;
            private readonly Dictionary<int, GenTree> _linearTreeById;
            private readonly Dictionary<int, int> _linearTreePositions;
            private readonly Dictionary<int, int> _originalTreePositions;
            private readonly SortedDictionary<int, int> _originalToFinalPositions;
            private readonly Dictionary<int, int> _positionBlockIds;
            private readonly List<int> _knownPositions;
            private readonly int _finalEndPosition;
            private readonly ImmutableArray<int> _finalCallerSavedKillPositions;
            private readonly ImmutableArray<int> _callerFrameSuspendingCallPositions;
            private int _syntheticGcOwnerId;

            public MethodBuilder(RegisterAllocatedMethod method)
            {
                _method = method;
                _linearTreeById = BuildLinearTreeMap(method.GenTreeMethod);
                _originalTreePositions = BuildOriginalLinearTreePositions(method.GenTreeMethod);
                _linearTreePositions = BuildFinalLinearTreePositions(method, out _positionBlockIds, out _finalEndPosition);
                _originalToFinalPositions = BuildOriginalToFinalPositionMap(_originalTreePositions, _linearTreePositions, _finalEndPosition);
                _knownPositions = BuildKnownPositions(_positionBlockIds);
                _finalCallerSavedKillPositions = BuildFinalCallerSavedKillPositions(method, _linearTreePositions);
                _callerFrameSuspendingCallPositions = BuildCallerFrameSuspendingCallPositions(method, _linearTreePositions);
                _syntheticGcOwnerId = int.MinValue;
            }

            public RegisterAllocatedMethod Run()
            {
                var liveRanges = BuildLiveRanges();
                var transitions = BuildTransitions(liveRanges);
                var interruptibleRanges = BuildInterruptibleRanges();

                return new RegisterAllocatedMethod(
                    _method.GenTreeMethod,
                    _method.Blocks,
                    _method.LinearNodes,
                    _method.Allocations,
                    _method.AllocationByNode,
                    _method.InternalRegistersByNodeId,
                    _method.SpillSlotCount,
                    _method.ParallelCopyScratchSpillSlot,
                    _method.StackFrame,
                    _method.HasPrologEpilog,
                    _method.UnwindCodes,
                    liveRanges,
                    transitions,
                    interruptibleRanges,
                    _method.Funclets,
                    _method.FrameRegions,
                    gcReportOnlyLeafFunclet: _method.Funclets.Length > 1,
                    lsraNodePositions: _method.LsraNodePositions,
                    lsraBlockStartPositions: _method.LsraBlockStartPositions,
                    lsraBlockEndPositions: _method.LsraBlockEndPositions);
            }

            private static ImmutableArray<int> BuildFinalCallerSavedKillPositions(
                RegisterAllocatedMethod method,
                Dictionary<int, int> finalPositions)
            {
                var result = new SortedSet<int>();
                for (int i = 0; i < method.LinearNodes.Length; i++)
                {
                    var node = method.LinearNodes[i];
                    if (!node.HasLoweringFlag(GenTreeLinearFlags.CallerSavedKill) && node.LinearKind != GenTreeLinearKind.GcPoll)
                        continue;

                    int key = node.LinearId >= 0 ? node.LinearId : node.Id;
                    if (finalPositions.TryGetValue(key, out int position))
                        result.Add(position);
                }

                return result.ToImmutableArray();
            }

            private static ImmutableArray<int> BuildCallerFrameSuspendingCallPositions(
                RegisterAllocatedMethod method,
                Dictionary<int, int> finalPositions)
            {
                var result = new SortedSet<int>();
                for (int i = 0; i < method.LinearNodes.Length; i++)
                {
                    var node = method.LinearNodes[i];
                    if (!IsCallerFrameSuspendingManagedCall(node))
                        continue;

                    int key = node.LinearId >= 0 ? node.LinearId : node.Id;
                    if (finalPositions.TryGetValue(key, out int position))
                        result.Add(position);
                }

                return result.ToImmutableArray();
            }

            private static bool IsCallerFrameSuspendingManagedCall(GenTree node)
            {
                if (node is null || !node.HasLoweringFlag(GenTreeLinearFlags.AbiCall))
                    return false;

                var method = node.Method;
                if (method is null || method.HasInternalCall)
                    return false;

                return node.TreeKind switch
                {
                    GenTreeKind.Call => true,
                    GenTreeKind.VirtualCall => true,
                    GenTreeKind.NewObject => method.DeclaringType.IsValueType,
                    _ => false,
                };
            }

            private bool IsCallerFrameSuspendingCallPosition(int position)
            {
                for (int i = 0; i < _callerFrameSuspendingCallPositions.Length; i++)
                {
                    int callPosition = _callerFrameSuspendingCallPositions[i];
                    if (callPosition == position)
                        return true;
                    if (callPosition > position)
                        return false;
                }

                return false;
            }

            private ImmutableArray<RegisterGcLiveRange> BuildLiveRanges()
            {
                var ranges = ImmutableArray.CreateBuilder<RegisterGcLiveRange>();

                for (int i = 0; i < _method.Allocations.Length; i++)
                {
                    var allocation = _method.Allocations[i];
                    if (!_method.GenTreeMethod.ValueInfoByNode.TryGetValue(allocation.ValueKey, out var info))
                        continue;

                    if (info.Type is not null && info.Type.IsValueType && info.Type.ContainsGcPointers && allocation.Fragments.Length != 0)
                    {
                        AddStructFragmentRanges(ranges, allocation, info);
                        continue;
                    }

                    if (info.Type is not null && info.Type.IsValueType && info.Type.ContainsGcPointers)
                    {
                        AddStructHomeRanges(ranges, allocation, info);
                        continue;
                    }

                    if (!TryGetGcRootKind(info, out var rootKind))
                        continue;

                    for (int s = 0; s < allocation.Segments.Length; s++)
                    {
                        var segment = allocation.Segments[s];
                        if (segment.Location.IsNone || segment.Start == segment.End)
                            continue;

                        var translated = TranslateAllocationRange(segment.Start, segment.End);
                        AddAllocationLiveRangeSlices(
                            ranges,
                            new RegisterGcLiveRoot(allocation.Value, rootKind, segment.Location, info.Type),
                            translated.Start,
                            translated.End);
                    }
                }

                AddHomeSlotRootRanges(ranges);
                AddSafepointOperandRootRanges(ranges);

                return NormalizeLiveRanges(ranges.ToImmutable());
            }

            private void AddHomeSlotRootRanges(ImmutableArray<RegisterGcLiveRange>.Builder ranges)
            {
                if (_knownPositions.Count == 0)
                    return;

                var owners = BuildHomeRootOwners();
                var descriptorLiveness = BuildDescriptorHomeLiveness();
                int methodStart = _knownPositions[0];
                int methodEnd = _knownPositions[_knownPositions.Count - 1] + 2;
                RegisterFrameBase frameBase = _method.StackFrame.UsesFramePointer
                    ? RegisterFrameBase.FramePointer
                    : RegisterFrameBase.StackPointer;

                AddDescriptorHomeSlotRootRanges(
                    ranges,
                    owners,
                    descriptorLiveness,
                    _method.GenTreeMethod.ArgDescriptors,
                    StackFrameSlotKind.Argument,
                    frameBase,
                    methodStart,
                    methodEnd);

                AddDescriptorHomeSlotRootRanges(
                    ranges,
                    owners,
                    descriptorLiveness,
                    _method.GenTreeMethod.LocalDescriptors,
                    StackFrameSlotKind.Local,
                    frameBase,
                    methodStart,
                    methodEnd);

                AddDescriptorHomeSlotRootRanges(
                    ranges,
                    owners,
                    descriptorLiveness,
                    _method.GenTreeMethod.TempDescriptors,
                    StackFrameSlotKind.Temp,
                    frameBase,
                    methodStart,
                    methodEnd);
            }

            private sealed class DescriptorHomeLiveness
            {
                public readonly HashSet<GenLocalDescriptor>[] LiveIn;
                public readonly HashSet<GenLocalDescriptor>[] LiveOut;

                public DescriptorHomeLiveness(HashSet<GenLocalDescriptor>[] liveIn, HashSet<GenLocalDescriptor>[] liveOut)
                {
                    LiveIn = liveIn ?? throw new ArgumentNullException(nameof(liveIn));
                    LiveOut = liveOut ?? throw new ArgumentNullException(nameof(liveOut));
                }

                public bool IsLiveIn(int blockId, GenLocalDescriptor descriptor)
                    => (uint)blockId < (uint)LiveIn.Length && LiveIn[blockId].Contains(descriptor);

                public bool IsLiveOut(int blockId, GenLocalDescriptor descriptor)
                    => (uint)blockId < (uint)LiveOut.Length && LiveOut[blockId].Contains(descriptor);
            }

            private DescriptorHomeLiveness BuildDescriptorHomeLiveness()
            {
                int blockCount = _method.Blocks.Length;
                var liveIn = NewDescriptorSetArray(blockCount);
                var liveOut = NewDescriptorSetArray(blockCount);
                var blockUses = NewDescriptorSetArray(blockCount);
                var blockDefs = NewDescriptorSetArray(blockCount);

                for (int b = 0; b < blockCount; b++)
                {
                    var seenDefinition = new HashSet<GenLocalDescriptor>();
                    var nodes = _method.Blocks[b].LinearNodes;

                    for (int i = 0; i < nodes.Length; i++)
                    {
                        var node = nodes[i];
                        var descriptor = node.LocalDescriptor;
                        if (descriptor is null || !NeedsHomeGcReporting(descriptor.Type, descriptor.StackKind))
                            continue;

                        if (IsDescriptorStore(node))
                        {
                            blockDefs[b].Add(descriptor);
                            seenDefinition.Add(descriptor);
                            continue;
                        }

                        if (IsDescriptorRead(node) && !seenDefinition.Contains(descriptor))
                        {
                            blockUses[b].Add(descriptor);
                        }
                    }
                }

                var dataflowOrder = _method.GenTreeMethod.LinearBlockOrder.IsDefaultOrEmpty
                    ? LinearBlockOrder.Compute(_method.GenTreeMethod.Cfg)
                    : _method.GenTreeMethod.LinearBlockOrder;

                bool changed;
                do
                {
                    changed = false;
                    for (int r = dataflowOrder.Length - 1; r >= 0; r--)
                    {
                        int blockId = dataflowOrder[r];
                        var newOut = new HashSet<GenLocalDescriptor>();
                        var successors = _method.GenTreeMethod.Cfg.Blocks[blockId].Successors;
                        for (int s = 0; s < successors.Length; s++)
                            newOut.UnionWith(liveIn[successors[s].ToBlockId]);

                        var newIn = new HashSet<GenLocalDescriptor>(newOut);
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

                return new DescriptorHomeLiveness(liveIn, liveOut);
            }

            private static HashSet<GenLocalDescriptor>[] NewDescriptorSetArray(int length)
            {
                var result = new HashSet<GenLocalDescriptor>[length];
                for (int i = 0; i < result.Length; i++)
                    result[i] = new HashSet<GenLocalDescriptor>();
                return result;
            }

            private static bool SetEquals(HashSet<GenLocalDescriptor> left, HashSet<GenLocalDescriptor> right)
            {
                if (left.Count != right.Count)
                    return false;
                foreach (var value in left)
                    if (!right.Contains(value))
                        return false;
                return true;
            }

            private void AddDescriptorHomeSlotRootRanges(
                ImmutableArray<RegisterGcLiveRange>.Builder ranges,
                Dictionary<GenLocalDescriptor, GenTree> owners,
                DescriptorHomeLiveness descriptorLiveness,
                ImmutableArray<GenLocalDescriptor> descriptors,
                StackFrameSlotKind slotKind,
                RegisterFrameBase frameBase,
                int methodStart,
                int methodEnd)
            {
                for (int i = 0; i < descriptors.Length; i++)
                {
                    var descriptor = descriptors[i];
                    if (!ShouldReportDescriptorHome(descriptor))
                        continue;
                    if (!owners.TryGetValue(descriptor, out var owner))
                        continue;
                    if (!TryGetHomeSlot(slotKind, descriptor.Index, out var slot))
                        continue;

                    var location = RegisterOperand.ForFrameSlot(
                        RegisterClass.General,
                        slot.Kind,
                        frameBase,
                        slot.Index,
                        slot.Offset,
                        slot.Size);

                    if (ShouldReportDescriptorHomeForEntireMethod(descriptor))
                    {
                        AddHomeSlotRootRanges(ranges, owner, descriptor.Type, descriptor.StackKind, location, methodStart, methodEnd);
                        continue;
                    }

                    AddDescriptorHomeSlotRootRanges(ranges, owner, descriptor, location, descriptorLiveness);
                }
            }

            private void AddDescriptorHomeSlotRootRanges(
                ImmutableArray<RegisterGcLiveRange>.Builder ranges,
                GenTree owner,
                GenLocalDescriptor descriptor,
                RegisterOperand location,
                DescriptorHomeLiveness descriptorLiveness)
            {
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var block = _method.Blocks[b];
                    if (!TryGetBlockPositionRange(block, out int blockStart, out int blockEnd))
                        continue;

                    bool active = descriptorLiveness.IsLiveIn(block.Id, descriptor);
                    int activeStart = active ? blockStart : -1;
                    var nodes = block.LinearNodes;

                    for (int i = 0; i < nodes.Length; i++)
                    {
                        var node = nodes[i];
                        if (node.LocalDescriptor is null || !ReferenceEquals(node.LocalDescriptor, descriptor))
                            continue;
                        if (!TryGetLinearPosition(node, out int position))
                            continue;

                        if (IsDescriptorStore(node))
                        {
                            if (active)
                                AddHomeSlotRootRanges(ranges, owner, descriptor.Type, descriptor.StackKind, location, activeStart, position);

                            active = false;
                            activeStart = -1;

                            if (StoreWritesReportableGcValue(node) && IsDescriptorLiveAfterStore(descriptorLiveness, descriptor, block, i))
                            {
                                active = true;
                                activeStart = position + 1;
                            }

                            continue;
                        }

                        if (IsDescriptorRead(node) && !active)
                        {
                            active = true;
                            activeStart = position;
                        }
                    }

                    if (active)
                        AddHomeSlotRootRanges(ranges, owner, descriptor.Type, descriptor.StackKind, location, activeStart, blockEnd);
                }
            }

            private bool TryGetBlockPositionRange(GenTreeBlock block, out int start, out int end)
            {
                start = 0;
                end = 0;
                if (block.LinearNodes.Length == 0)
                    return false;

                if (!TryGetLinearPosition(block.LinearNodes[0], out start))
                    return false;

                if (!TryGetLinearPosition(block.LinearNodes[block.LinearNodes.Length - 1], out int last))
                    return false;

                end = last + 2;
                return true;
            }

            private bool IsDescriptorLiveAfterStore(
                DescriptorHomeLiveness descriptorLiveness,
                GenLocalDescriptor descriptor,
                GenTreeBlock block,
                int storeOrdinal)
            {
                var nodes = block.LinearNodes;
                for (int i = storeOrdinal + 1; i < nodes.Length; i++)
                {
                    var node = nodes[i];
                    if (node.LocalDescriptor is null || !ReferenceEquals(node.LocalDescriptor, descriptor))
                        continue;

                    if (IsDescriptorRead(node))
                        return true;
                    if (IsDescriptorStore(node))
                        return false;
                }

                return descriptorLiveness.IsLiveOut(block.Id, descriptor);
            }

            private bool StoreWritesReportableGcValue(GenTree store)
            {
                if (store.RegisterUses.Length == 0)
                    return true;

                var value = store.RegisterUses[0];
                if (!_method.GenTreeMethod.ValueInfoByNode.TryGetValue(value.LinearValueKey, out var valueInfo))
                    return true;

                if (valueInfo.Type is not null && valueInfo.Type.IsValueType && valueInfo.Type.ContainsGcPointers)
                    return true;

                return TryGetGcRootKind(valueInfo, out _);
            }

            private static bool IsDescriptorStore(GenTree node)
                => node.Kind is GenTreeKind.StoreArg or GenTreeKind.StoreLocal or GenTreeKind.StoreTemp;

            private static bool IsDescriptorRead(GenTree node)
                => node.Kind is GenTreeKind.Arg or GenTreeKind.Local or GenTreeKind.Temp;

            private Dictionary<GenLocalDescriptor, GenTree> BuildHomeRootOwners()
            {
                var owners = new Dictionary<GenLocalDescriptor, GenTree>();
                var nodes = _method.GenTreeMethod.LinearNodes;
                for (int i = 0; i < nodes.Length; i++)
                {
                    var node = nodes[i];
                    var descriptor = node.LocalDescriptor;
                    if (descriptor is null || owners.ContainsKey(descriptor))
                        continue;
                    if (!NeedsHomeGcReporting(descriptor.Type, descriptor.StackKind))
                        continue;

                    owners.Add(descriptor, node);
                }

                AddSyntheticHomeOwners(owners, _method.GenTreeMethod.ArgDescriptors);
                AddSyntheticHomeOwners(owners, _method.GenTreeMethod.LocalDescriptors);
                AddSyntheticHomeOwners(owners, _method.GenTreeMethod.TempDescriptors);
                return owners;
            }

            private void AddSyntheticHomeOwners(
                Dictionary<GenLocalDescriptor, GenTree> owners,
                ImmutableArray<GenLocalDescriptor> descriptors)
            {
                for (int i = 0; i < descriptors.Length; i++)
                {
                    var descriptor = descriptors[i];
                    if (owners.ContainsKey(descriptor))
                        continue;
                    if (!ShouldReportDescriptorHome(descriptor))
                        continue;

                    owners.Add(descriptor, CreateSyntheticHomeRootOwner(descriptor));
                }
            }

            private GenTree CreateSyntheticHomeRootOwner(GenLocalDescriptor descriptor)
            {
                var kind = descriptor.Kind switch
                {
                    GenLocalKind.Argument => GenTreeKind.Arg,
                    GenLocalKind.Local => GenTreeKind.Local,
                    GenLocalKind.Temporary => GenTreeKind.Temp,
                    _ => GenTreeKind.Nop,
                };

                var owner = new GenTree(
                    _syntheticGcOwnerId++,
                    kind,
                    pc: -1,
                    BytecodeOp.Nop,
                    descriptor.Type,
                    descriptor.StackKind,
                    GenTreeFlags.LocalUse | GenTreeFlags.Ordered,
                    ImmutableArray<GenTree>.Empty,
                    int32: descriptor.Index);
                owner.LocalDescriptor = descriptor;
                owner.ValueKey = descriptor.ValueKey;
                return owner;
            }

            private static bool ShouldReportDescriptorHome(GenLocalDescriptor descriptor)
            {
                if (!NeedsHomeGcReporting(descriptor.Type, descriptor.StackKind))
                    return false;

                return !descriptor.SsaPromoted || descriptor.AddressExposed || descriptor.DoNotEnregister;
            }

            private static bool ShouldReportDescriptorHomeForEntireMethod(GenLocalDescriptor descriptor)
            {
                if (!ShouldReportDescriptorHome(descriptor))
                    return false;

                if (descriptor.AddressExposed || descriptor.DoNotEnregister || !descriptor.Tracked)
                    return true;

                if (descriptor.Type is not null && descriptor.Type.IsValueType && descriptor.Type.ContainsGcPointers)
                    return true;

                return false;
            }

            private bool TryGetHomeSlot(StackFrameSlotKind slotKind, int index, out StackFrameSlot slot)
            {
                switch (slotKind)
                {
                    case StackFrameSlotKind.Argument:
                        return _method.StackFrame.TryGetArgumentSlot(index, out slot);
                    case StackFrameSlotKind.Local:
                        return _method.StackFrame.TryGetLocalSlot(index, out slot);
                    case StackFrameSlotKind.Temp:
                        return _method.StackFrame.TryGetTempSlot(index, out slot);
                    default:
                        throw new InvalidOperationException("Unsupported GC home slot kind: " + slotKind + ".");
                }
            }

            private void AddHomeSlotRootRanges(
                ImmutableArray<RegisterGcLiveRange>.Builder ranges,
                GenTree owner,
                RuntimeType? type,
                GenStackKind stackKind,
                RegisterOperand location,
                int start,
                int end)
            {
                if (type is not null && type.IsValueType && type.ContainsGcPointers)
                {
                    var gcOffsets = type.GcPointerOffsets;
                    for (int g = 0; g < gcOffsets.Length; g++)
                    {
                        var structRootKind = GetStructGcRootKind(type, gcOffsets[g]);
                        AddLiveRangeSlices(
                            ranges,
                            new RegisterGcLiveRoot(
                                owner,
                                structRootKind,
                                location,
                                type,
                                gcOffsets[g],
                                requiresValueInfo: false),
                            start,
                            end);
                    }

                    return;
                }

                if (!TryGetGcRootKind(type, stackKind, out var rootKind))
                    return;

                AddLiveRangeSlices(
                    ranges,
                    new RegisterGcLiveRoot(owner, rootKind, location, type, requiresValueInfo: false),
                    start,
                    end);
            }

            private void AddSafepointOperandRootRanges(ImmutableArray<RegisterGcLiveRange>.Builder ranges)
            {
                for (int i = 0; i < _method.LinearNodes.Length; i++)
                {
                    var node = _method.LinearNodes[i];
                    if (!node.HasLoweringFlag(GenTreeLinearFlags.GcSafePoint))
                        continue;

                    if (!TryGetLinearPosition(node, out int position))
                        continue;

                    bool excludeCallerSavedRegisterOperands = IsCallerFrameSuspendingManagedCall(node);

                    int count = Math.Min(node.Uses.Length, node.RegisterUses.Length);
                    for (int u = 0; u < count; u++)
                    {
                        if (u < node.UseRoles.Length && node.UseRoles[u] == OperandRole.HiddenReturnBuffer)
                            continue;

                        var location = node.Uses[u];
                        if (location.IsNone)
                            continue;

                        if (excludeCallerSavedRegisterOperands &&
                            location.IsRegister &&
                            MachineRegisters.IsCallerSaved(location.Register))
                        {
                            continue;
                        }

                        var value = node.RegisterUses[u];
                        if (!_method.GenTreeMethod.ValueInfoByNode.TryGetValue(value.LinearValueKey, out var info))
                            continue;

                        AddSafepointOperandRootRange(ranges, value, info, location, position);
                    }
                }
            }

            private void AddSafepointOperandRootRange(
                ImmutableArray<RegisterGcLiveRange>.Builder ranges,
                GenTree value,
                GenTreeValueInfo info,
                RegisterOperand location,
                int position)
            {
                if (info.Type is not null && info.Type.IsValueType && info.Type.ContainsGcPointers)
                {
                    if (location.IsRegister)
                        return;

                    var gcOffsets = info.Type.GcPointerOffsets;
                    for (int g = 0; g < gcOffsets.Length; g++)
                    {
                        var structRootKind = GetStructGcRootKind(info.Type, gcOffsets[g]);
                        AddLiveRangeSlices(
                            ranges,
                            new RegisterGcLiveRoot(
                                value,
                                structRootKind,
                                location,
                                info.Type,
                                gcOffsets[g]),
                            position,
                            position + 1);
                    }

                    return;
                }

                if (!TryGetGcRootKind(info, out var rootKind))
                    return;

                AddLiveRangeSlices(
                    ranges,
                    new RegisterGcLiveRoot(value, rootKind, location, info.Type),
                    position,
                    position + 1);
            }

            private void AddStructFragmentRanges(
                ImmutableArray<RegisterGcLiveRange>.Builder ranges,
                RegisterAllocationInfo allocation,
                GenTreeValueInfo info)
            {
                var gcOffsets = info.Type!.GcPointerOffsets;
                for (int f = 0; f < allocation.Fragments.Length; f++)
                {
                    var fragment = allocation.Fragments[f];
                    if (!fragment.AbiSegment.ContainsGcPointers)
                        continue;

                    int segmentStart = fragment.AbiSegment.Offset;
                    int segmentEnd = segmentStart + fragment.AbiSegment.Size;
                    for (int s = 0; s < fragment.Segments.Length; s++)
                    {
                        var range = fragment.Segments[s];
                        if (range.Location.IsNone || range.Start == range.End)
                            continue;
                        if (range.Location.IsRegister)
                            continue;

                        for (int g = 0; g < gcOffsets.Length; g++)
                        {
                            int gcOffset = gcOffsets[g];
                            if (segmentStart <= gcOffset && gcOffset < segmentEnd)
                            {
                                var rootKind = GetStructGcRootKind(info.Type, gcOffset);
                                var translated = TranslateAllocationRange(range.Start, range.End);
                                AddLiveRangeSlices(
                                    ranges,
                                    new RegisterGcLiveRoot(
                                        allocation.Value,
                                        rootKind,
                                        range.Location,
                                        info.Type,
                                        gcOffset - segmentStart),
                                    translated.Start,
                                    translated.End);
                            }
                        }
                    }
                }
            }

            private void AddStructHomeRanges(
                ImmutableArray<RegisterGcLiveRange>.Builder ranges,
                RegisterAllocationInfo allocation,
                GenTreeValueInfo info)
            {
                var gcOffsets = info.Type!.GcPointerOffsets;
                for (int s = 0; s < allocation.Segments.Length; s++)
                {
                    var segment = allocation.Segments[s];
                    if (segment.Location.IsNone || segment.Start == segment.End)
                        continue;
                    if (segment.Location.IsRegister)
                        continue;

                    for (int g = 0; g < gcOffsets.Length; g++)
                    {
                        var rootKind = GetStructGcRootKind(info.Type, gcOffsets[g]);
                        var translated = TranslateAllocationRange(segment.Start, segment.End);
                        AddLiveRangeSlices(
                            ranges,
                            new RegisterGcLiveRoot(
                                allocation.Value,
                                rootKind,
                                segment.Location,
                                info.Type,
                                gcOffsets[g]),
                            translated.Start,
                            translated.End);
                    }
                }
            }

            private static RegisterGcRootKind GetStructGcRootKind(RuntimeType type, int gcOffset)
            {
                if (TryGetStructGcRootKind(type, gcOffset, out var rootKind))
                    return rootKind;

                return RegisterGcRootKind.ObjectReference;
            }

            private static bool TryGetStructGcRootKind(RuntimeType type, int gcOffset, out RegisterGcRootKind rootKind)
            {
                if (gcOffset < 0)
                {
                    rootKind = default;
                    return false;
                }

                if (type.Kind == RuntimeTypeKind.ByRef)
                {
                    rootKind = RegisterGcRootKind.InteriorPointer;
                    return true;
                }

                if (type.Kind == RuntimeTypeKind.TypeParam || type.IsReferenceType)
                {
                    rootKind = RegisterGcRootKind.ObjectReference;
                    return true;
                }

                if (!type.IsValueType || type.InstanceFields.Length == 0)
                {
                    rootKind = default;
                    return false;
                }

                var fields = (RuntimeField[])type.InstanceFields.Clone();
                Array.Sort(fields, static (a, b) => a.Offset.CompareTo(b.Offset));

                for (int i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    var fieldType = field.FieldType;
                    int fieldSize = Math.Max(1, fieldType.SizeOf);
                    int fieldStart = field.Offset;
                    int fieldEnd = checked(fieldStart + fieldSize);
                    if (gcOffset < fieldStart || gcOffset >= fieldEnd)
                        continue;

                    if (fieldType.Kind == RuntimeTypeKind.ByRef)
                    {
                        rootKind = RegisterGcRootKind.InteriorPointer;
                        return true;
                    }

                    if (fieldType.Kind == RuntimeTypeKind.TypeParam || fieldType.IsReferenceType)
                    {
                        rootKind = RegisterGcRootKind.ObjectReference;
                        return true;
                    }

                    if (fieldType.IsValueType)
                        return TryGetStructGcRootKind(fieldType, gcOffset - fieldStart, out rootKind);

                    break;
                }

                rootKind = default;
                return false;
            }

            private LinearLiveRange TranslateAllocationRange(int start, int end)
            {
                int translatedStart = TranslateAllocationPosition(start);
                int translatedEnd = TranslateAllocationPosition(end);
                if (translatedEnd < translatedStart)
                    translatedEnd = translatedStart;
                return new LinearLiveRange(translatedStart, translatedEnd);
            }

            private int TranslateAllocationPosition(int position)
            {
                if (_originalToFinalPositions.TryGetValue(position, out int exact))
                    return exact;

                foreach (var kv in _originalToFinalPositions)
                {
                    if (kv.Key >= position)
                        return kv.Value;
                }

                return _finalEndPosition;
            }

            private void AddAllocationLiveRangeSlices(
                ImmutableArray<RegisterGcLiveRange>.Builder ranges,
                RegisterGcLiveRoot root,
                int start,
                int end)
            {
                if (start >= end)
                    return;

                if (!root.Location.IsRegister || !MachineRegisters.IsCallerSaved(root.Location.Register))
                {
                    AddLiveRangeSlices(ranges, root, start, end);
                    return;
                }

                for (int i = 0; i < _finalCallerSavedKillPositions.Length; i++)
                {
                    int kill = _finalCallerSavedKillPositions[i];
                    if (kill < start)
                        continue;
                    if (kill >= end)
                        break;

                    int killEnd = IsCallerFrameSuspendingCallPosition(kill)
                        ? kill
                        : Math.Min(end, kill + 1);
                    if (start < killEnd)
                        AddLiveRangeSlices(ranges, root, start, killEnd);
                    return;
                }

                AddLiveRangeSlices(ranges, root, start, end);
            }

            private void AddLiveRangeSlices(
                ImmutableArray<RegisterGcLiveRange>.Builder ranges,
                RegisterGcLiveRoot root,
                int start,
                int end)
            {
                if (start >= end)
                    return;

                if (_method.Funclets.Length <= 1 || _knownPositions.Count == 0)
                {
                    AddLiveRange(ranges, root, start, end, funcletIndex: 0);
                    return;
                }

                int currentStart = start;
                int previousFunclet = FuncletIndexForPosition(start);

                for (int i = 0; i < _knownPositions.Count; i++)
                {
                    int position = _knownPositions[i];
                    if (position <= start)
                        continue;
                    if (position >= end)
                        break;

                    int funcletIndex = FuncletIndexForPosition(position);
                    if (funcletIndex != previousFunclet)
                    {
                        if (currentStart < position)
                            AddLiveRange(ranges, root, currentStart, position, previousFunclet);
                        currentStart = position;
                        previousFunclet = funcletIndex;
                    }
                }

                if (currentStart < end)
                    AddLiveRange(ranges, root, currentStart, end, previousFunclet);
            }

            private void AddLiveRange(
                ImmutableArray<RegisterGcLiveRange>.Builder ranges,
                RegisterGcLiveRoot root,
                int start,
                int end,
                int funcletIndex)
            {
                var flags = RegisterGcLiveRangeFlags.None;

                if (_method.Funclets.Length > 1)
                    flags |= RegisterGcLiveRangeFlags.ReportOnlyInLeafFunclet;

                if (funcletIndex > 0 && IsFrameBacked(root.Location))
                    flags |= RegisterGcLiveRangeFlags.SharedWithParentFrame;

                if (IsFilterFunclet(funcletIndex) && IsFrameBacked(root.Location))
                    flags |= RegisterGcLiveRangeFlags.Pinned;

                ranges.Add(new RegisterGcLiveRange(root, start, end, funcletIndex, flags));
            }

            private static bool IsFrameBacked(RegisterOperand operand)
                => operand.IsFrameSlot || operand.IsSpillSlot || operand.IsOutgoingArgumentSlot;

            private bool IsFilterFunclet(int funcletIndex)
                => (uint)funcletIndex < (uint)_method.Funclets.Length && _method.Funclets[funcletIndex].Kind == RegisterFuncletKind.Filter;

            private ImmutableArray<RegisterGcInterruptibleRange> BuildInterruptibleRanges()
            {
                var ranges = ImmutableArray.CreateBuilder<RegisterGcInterruptibleRange>();
                bool fullyInterruptible = _method.Funclets.Length > 1 || _method.GenTreeMethod.Cfg.ExceptionRegions.Length != 0;

                if (fullyInterruptible)
                    AddFullyInterruptibleRanges(ranges);
                else
                    AddCallAndPollInterruptibleRanges(ranges);

                return NormalizeInterruptibleRanges(ranges.ToImmutable());
            }

            private void AddFullyInterruptibleRanges(ImmutableArray<RegisterGcInterruptibleRange>.Builder ranges)
            {
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var block = _method.Blocks[b];
                    int start = -1;
                    int end = -1;
                    int firstNodeId = int.MaxValue;
                    int lastNodeId = -1;
                    int funcletIndex = FuncletIndexForBlock(block.Id);

                    for (int i = 0; i < block.LinearNodes.Length; i++)
                    {
                        var node = block.LinearNodes[i];
                        if (!TryGetLinearPosition(node, out int position))
                            continue;

                        if (start < 0)
                            start = position;

                        end = position + 1;
                        firstNodeId = Math.Min(firstNodeId, node.Id);
                        lastNodeId = Math.Max(lastNodeId, node.Id);
                    }

                    if (start >= 0 && end > start && firstNodeId != int.MaxValue)
                    {
                        ranges.Add(new RegisterGcInterruptibleRange(
                            RegisterGcInterruptibleRangeKind.FullyInterruptible,
                            start,
                            end,
                            funcletIndex,
                            firstNodeId,
                            lastNodeId));
                    }
                }
            }

            private void AddCallAndPollInterruptibleRanges(ImmutableArray<RegisterGcInterruptibleRange>.Builder ranges)
            {
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var block = _method.Blocks[b];
                    int funcletIndex = FuncletIndexForBlock(block.Id);

                    for (int i = 0; i < block.LinearNodes.Length; i++)
                    {
                        var node = block.LinearNodes[i];
                        if (!TryClassifyInterruptibleNode(node, out var kind))
                            continue;
                        if (!TryGetLinearPosition(node, out int position))
                            continue;

                        ranges.Add(new RegisterGcInterruptibleRange(
                            kind,
                            position,
                            position + 1,
                            funcletIndex,
                            node.Id,
                            node.Id));
                    }
                }
            }

            private static ImmutableArray<RegisterGcInterruptibleRange> NormalizeInterruptibleRanges(ImmutableArray<RegisterGcInterruptibleRange> source)
            {
                if (source.Length == 0)
                    return ImmutableArray<RegisterGcInterruptibleRange>.Empty;

                var list = new List<RegisterGcInterruptibleRange>(source);
                list.Sort(static (a, b) =>
                {
                    int c = a.StartPosition.CompareTo(b.StartPosition);
                    if (c != 0) return c;
                    c = a.EndPosition.CompareTo(b.EndPosition);
                    if (c != 0) return c;
                    c = a.FuncletIndex.CompareTo(b.FuncletIndex);
                    if (c != 0) return c;
                    return a.Kind.CompareTo(b.Kind);
                });

                var result = ImmutableArray.CreateBuilder<RegisterGcInterruptibleRange>(list.Count);
                var current = list[0];
                for (int i = 1; i < list.Count; i++)
                {
                    var next = list[i];
                    if (current.Kind == next.Kind &&
                        current.FuncletIndex == next.FuncletIndex &&
                        current.EndPosition >= next.StartPosition)
                    {
                        current = new RegisterGcInterruptibleRange(
                            current.Kind,
                            current.StartPosition,
                            Math.Max(current.EndPosition, next.EndPosition),
                            current.FuncletIndex,
                            Math.Min(current.FirstNodeId, next.FirstNodeId),
                            Math.Max(current.LastNodeId, next.LastNodeId));
                    }
                    else
                    {
                        result.Add(current);
                        current = next;
                    }
                }

                result.Add(current);
                return result.ToImmutable();
            }

            private static ImmutableArray<RegisterGcLiveRange> NormalizeLiveRanges(ImmutableArray<RegisterGcLiveRange> source)
            {
                if (source.Length == 0)
                    return ImmutableArray<RegisterGcLiveRange>.Empty;

                var list = new List<RegisterGcLiveRange>(source);
                list.Sort(CompareLiveRanges);

                var result = ImmutableArray.CreateBuilder<RegisterGcLiveRange>(list.Count);
                var current = list[0];
                for (int i = 1; i < list.Count; i++)
                {
                    var next = list[i];
                    if (current.Root.Equals(next.Root) &&
                        current.FuncletIndex == next.FuncletIndex &&
                        current.Flags == next.Flags &&
                        current.EndPosition >= next.StartPosition)
                    {
                        current = new RegisterGcLiveRange(
                            current.Root,
                            current.StartPosition,
                            Math.Max(current.EndPosition, next.EndPosition),
                            current.FuncletIndex,
                            current.Flags);
                    }
                    else
                    {
                        if (!current.IsEmpty)
                            result.Add(current);
                        current = next;
                    }
                }

                if (!current.IsEmpty)
                    result.Add(current);

                return result.ToImmutable();
            }

            private static ImmutableArray<RegisterGcTransition> BuildTransitions(ImmutableArray<RegisterGcLiveRange> liveRanges)
            {
                var transitions = ImmutableArray.CreateBuilder<RegisterGcTransition>(liveRanges.Length * 2);

                for (int i = 0; i < liveRanges.Length; i++)
                {
                    var range = liveRanges[i];
                    transitions.Add(new RegisterGcTransition(range.StartPosition, RegisterGcTransitionKind.Enter, null, range.Root));
                    transitions.Add(new RegisterGcTransition(range.EndPosition, RegisterGcTransitionKind.Exit, range.Root, null));
                }

                transitions.Sort(static (a, b) =>
                {
                    int c = a.Position.CompareTo(b.Position);
                    if (c != 0) return c;
                    return TransitionSortRank(a.Kind).CompareTo(TransitionSortRank(b.Kind));
                });

                return transitions.ToImmutable();
            }

            private static int TransitionSortRank(RegisterGcTransitionKind kind)
            {
                return kind switch
                {
                    RegisterGcTransitionKind.Exit => 0,
                    RegisterGcTransitionKind.Enter => 1,
                    _ => 2,
                };
            }

            private static int CompareLiveRanges(RegisterGcLiveRange a, RegisterGcLiveRange b)
            {
                int c = a.StartPosition.CompareTo(b.StartPosition);
                if (c != 0) return c;
                c = a.EndPosition.CompareTo(b.EndPosition);
                if (c != 0) return c;
                c = a.FuncletIndex.CompareTo(b.FuncletIndex);
                if (c != 0) return c;
                c = a.Flags.CompareTo(b.Flags);
                if (c != 0) return c;
                c = a.Root.Value.Id.CompareTo(b.Root.Value.Id);
                if (c != 0) return c;
                c = a.Root.RootKind.CompareTo(b.Root.RootKind);
                if (c != 0) return c;
                c = a.Root.Offset.CompareTo(b.Root.Offset);
                if (c != 0) return c;
                return string.CompareOrdinal(a.Root.Location.ToString(), b.Root.Location.ToString());
            }

            private bool TryClassifyInterruptibleNode(GenTree node, out RegisterGcInterruptibleRangeKind kind)
            {
                if (node.Kind == GenTreeKind.GcPoll)
                {
                    kind = RegisterGcInterruptibleRangeKind.Poll;
                    return true;
                }

                if (GenTreeLirKinds.IsRealTree(node) && node.Source is not null)
                {
                    if (node.TreeKind is GenTreeKind.Call or GenTreeKind.VirtualCall or GenTreeKind.NewObject or GenTreeKind.Throw or GenTreeKind.Rethrow)
                    {
                        kind = RegisterGcInterruptibleRangeKind.Call;
                        return true;
                    }

                    if (node.GenTreeLinearId >= 0 &&
                        _linearTreeById.TryGetValue(node.GenTreeLinearId, out var linearNode) &&
                        linearNode.HasLoweringFlag(GenTreeLinearFlags.GcSafePoint))
                    {
                        kind = RegisterGcInterruptibleRangeKind.Call;
                        return true;
                    }
                }

                kind = default;
                return false;
            }


            private bool TryGetLinearPosition(GenTree node, out int position)
            {
                if (node.GenTreeLinearId >= 0 && _linearTreePositions.TryGetValue(node.GenTreeLinearId, out position))
                    return true;

                if (node.Source is not null)
                {
                    for (int i = 0; i < _method.GenTreeMethod.LinearNodes.Length; i++)
                    {
                        var linearNode = _method.GenTreeMethod.LinearNodes[i];
                        if (linearNode is not null && linearNode.Id == node.Source.Id && _linearTreePositions.TryGetValue(linearNode.LinearId, out position))
                            return true;
                    }
                }

                position = -1;
                return false;
            }

            private int FuncletIndexForPosition(int position)
            {
                if (_positionBlockIds.TryGetValue(position, out int blockId))
                    return FuncletIndexForBlock(blockId);

                int bestPosition = -1;
                int bestBlock = 0;
                foreach (var kv in _positionBlockIds)
                {
                    if (kv.Key <= position && kv.Key > bestPosition)
                    {
                        bestPosition = kv.Key;
                        bestBlock = kv.Value;
                    }
                }

                return FuncletIndexForBlock(bestBlock);
            }

            private int FuncletIndexForBlock(int blockId)
            {
                for (int i = 0; i < _method.Funclets.Length; i++)
                {
                    var blocks = _method.Funclets[i].BlockIds;
                    for (int b = 0; b < blocks.Length; b++)
                    {
                        if (blocks[b] == blockId)
                            return _method.Funclets[i].Index;
                    }
                }

                return 0;
            }

            private static bool IsGcHomeSlot(RegisterOperand location)
                => location.IsFrameSlot && location.FrameSlotKind is StackFrameSlotKind.Argument or StackFrameSlotKind.Local or StackFrameSlotKind.Temp;

            private static bool NeedsHomeGcReporting(RuntimeType? type, GenStackKind stackKind)
            {
                if (type is not null)
                {
                    if (type.Kind == RuntimeTypeKind.ByRef || type.Kind == RuntimeTypeKind.TypeParam || type.IsReferenceType)
                        return true;
                    if (type.IsValueType && type.ContainsGcPointers)
                        return true;
                }

                return stackKind is GenStackKind.Ref or GenStackKind.ByRef or GenStackKind.Null;
            }

            private static bool TryGetGcRootKind(RuntimeType? type, GenStackKind stackKind, out RegisterGcRootKind kind)
            {
                if (type is not null)
                {
                    if (type.Kind == RuntimeTypeKind.ByRef)
                    {
                        kind = RegisterGcRootKind.InteriorPointer;
                        return true;
                    }

                    if (type.IsReferenceType || type.Kind == RuntimeTypeKind.TypeParam)
                    {
                        kind = RegisterGcRootKind.ObjectReference;
                        return true;
                    }
                }

                if (stackKind == GenStackKind.ByRef)
                {
                    kind = RegisterGcRootKind.InteriorPointer;
                    return true;
                }

                if (stackKind is GenStackKind.Ref or GenStackKind.Null)
                {
                    kind = RegisterGcRootKind.ObjectReference;
                    return true;
                }

                kind = default;
                return false;
            }

            private static bool TryGetGcRootKind(GenTreeValueInfo info, out RegisterGcRootKind kind)
                => TryGetGcRootKind(info.Type, info.StackKind, out kind);

            private static Dictionary<int, GenTree> BuildLinearTreeMap(GenTreeMethod method)
            {
                var result = new Dictionary<int, GenTree>();
                for (int i = 0; i < method.LinearNodes.Length; i++)
                    result[method.LinearNodes[i].LinearId] = method.LinearNodes[i];
                return result;
            }

            private static Dictionary<int, int> BuildOriginalLinearTreePositions(GenTreeMethod method)
            {
                var result = new Dictionary<int, int>();
                int position = 0;
                var order = method.LinearBlockOrder.IsDefaultOrEmpty
                    ? LinearBlockOrder.Compute(method.Cfg)
                    : method.LinearBlockOrder;

                for (int o = 0; o < order.Length; o++)
                {
                    int blockId = order[o];
                    int groupPosition = -1;
                    GenTree? groupHead = null;

                    for (int i = 0; i < method.LinearNodes.Length; i++)
                    {
                        var node = method.LinearNodes[i];
                        if (node.LinearBlockId != blockId)
                            continue;

                        if (node.IsPhiCopy && groupHead is not null && SamePhiCopyGroup(groupHead, node))
                        {
                            result[node.LinearId] = groupPosition;
                            continue;
                        }

                        groupHead = node.IsPhiCopy ? node : null;
                        groupPosition = position;
                        result[node.LinearId] = position;
                        position += 2;
                    }

                    position += 2;
                }

                return result;
            }

            private static Dictionary<int, int> BuildFinalLinearTreePositions(
                RegisterAllocatedMethod method,
                out Dictionary<int, int> positionBlockIds,
                out int finalEndPosition)
            {
                var result = new Dictionary<int, int>();
                positionBlockIds = new Dictionary<int, int>();
                int position = 0;
                var order = method.GenTreeMethod.LinearBlockOrder.IsDefaultOrEmpty
                    ? LinearBlockOrder.Compute(method.GenTreeMethod.Cfg)
                    : method.GenTreeMethod.LinearBlockOrder;

                for (int o = 0; o < order.Length; o++)
                {
                    int b = order[o];
                    var nodes = method.Blocks[b].LinearNodes;
                    for (int n = 0; n < nodes.Length; n++)
                    {
                        var node = nodes[n];
                        if (result.ContainsKey(node.LinearId))
                            throw new InvalidOperationException($"Duplicate GenTree LinearId {node.LinearId} in final LIR stream.");
                        result.Add(node.LinearId, position);
                        positionBlockIds[position] = b;

                        if (node.IsPhiCopy)
                        {
                            while (n + 1 < nodes.Length && SamePhiCopyGroup(node, nodes[n + 1]))
                            {
                                n++;
                                if (result.ContainsKey(nodes[n].LinearId))
                                    throw new InvalidOperationException($"Duplicate GenTree LinearId {nodes[n].LinearId} in final LIR stream.");
                                result.Add(nodes[n].LinearId, position);
                            }
                        }

                        position += 2;
                    }
                    position += 2;
                }

                finalEndPosition = position;
                return result;
            }

            private static SortedDictionary<int, int> BuildOriginalToFinalPositionMap(
                Dictionary<int, int> originalPositions,
                Dictionary<int, int> finalPositions,
                int finalEndPosition)
            {
                var result = new SortedDictionary<int, int>();

                foreach (var kv in originalPositions)
                {
                    int linearId = kv.Key;
                    int original = kv.Value;
                    if (!finalPositions.TryGetValue(linearId, out int final))
                        continue;

                    AddPositionMap(result, original, final);
                    AddPositionMap(result, original + 1, final + 1);
                }

                AddPositionMap(result, int.MaxValue, finalEndPosition);
                return result;
            }

            private static void AddPositionMap(SortedDictionary<int, int> map, int original, int final)
            {
                if (map.TryGetValue(original, out int existing))
                {
                    if (final < existing)
                        map[original] = final;
                    return;
                }

                map.Add(original, final);
            }

            private static bool SamePhiCopyGroup(GenTree left, GenTree right)
                => left.IsPhiCopy &&
                   right.IsPhiCopy &&
                   left.LinearPhiCopyFromBlockId == right.LinearPhiCopyFromBlockId &&
                   left.LinearPhiCopyToBlockId == right.LinearPhiCopyToBlockId;

            private static List<int> BuildKnownPositions(Dictionary<int, int> positionBlockIds)
            {
                var result = new List<int>(positionBlockIds.Count);
                foreach (var kv in positionBlockIds)
                    result.Add(kv.Key);
                result.Sort();
                return result;
            }
        }
    }

    internal static class RegisterAllocationVerifier
    {
        public static void Verify(RegisterAllocatedMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            VerifyAllValuesAllocated(method);
            VerifyNoOverlappingRegisterIntervals(method);
            VerifyNoCallerSavedLiveAcrossCalls(method);
            VerifyStackFrameLayout(method);
            VerifyNodeOperands(method);
            VerifyCfgEdgeLocations(method);
            VerifyPrologEpilog(method);
            VerifyUnwindCodes(method);
            VerifyGcInfo(method);
            VerifyFunclets(method);
            VerifyFrameRegions(method);
        }


        private static void VerifyCfgEdgeLocations(RegisterAllocatedMethod method)
        {
            var layout = AllocatorVerifierPositionLayout.Build(method);

            for (int fromId = 0; fromId < method.GenTreeMethod.Cfg.Blocks.Length; fromId++)
            {
                var cfgBlock = method.GenTreeMethod.Cfg.Blocks[fromId];
                for (int s = 0; s < cfgBlock.Successors.Length; s++)
                {
                    var edge = cfgBlock.Successors[s];
                    if ((uint)edge.FromBlockId >= (uint)method.Blocks.Length || (uint)edge.ToBlockId >= (uint)method.Blocks.Length)
                        throw new InvalidOperationException($"post-LSRA CFG location invariant failed: invalid edge {edge}.");

                    var state = BuildExpectedLocationState(method, layout.BlockEndPositions[edge.FromBlockId]);
                    ApplyTrailingPhiCopies(method, edge, method.Blocks[edge.FromBlockId], state);
                    ApplyLeadingSyntheticMoves(method, edge, method.Blocks[edge.ToBlockId], state);
                    ApplyMissingSemanticPhiCopies(method, layout, edge, state);
                    VerifyEdgeLiveInLocations(method, layout, edge, state);
                }
            }
        }

        private static Dictionary<GenTreeValueKey, RegisterValueLocation> BuildExpectedLocationState(
            RegisterAllocatedMethod method,
            int position)
        {
            var state = new Dictionary<GenTreeValueKey, RegisterValueLocation>();
            for (int i = 0; i < method.Allocations.Length; i++)
            {
                var allocation = method.Allocations[i];
                if (!IsAllocationLiveAt(allocation, position))
                    continue;

                var info = method.GenTreeMethod.GetValueInfo(allocation.ValueKey);
                var abi = MachineAbi.ClassifyStorageValue(info.Type, info.StackKind);
                var location = allocation.ValueLocationAt(position, abi);
                if (!location.IsEmpty)
                    state[allocation.ValueKey] = location;
            }
            return state;
        }

        private static void VerifyEdgeLiveInLocations(
            RegisterAllocatedMethod method,
            AllocatorVerifierPositionLayout layout,
            CfgEdge edge,
            IReadOnlyDictionary<GenTreeValueKey, RegisterValueLocation> state)
        {
            int toStart = layout.BlockStartPositions[edge.ToBlockId];
            for (int i = 0; i < method.Allocations.Length; i++)
            {
                var allocation = method.Allocations[i];
                if (!IsAllocationLiveAt(allocation, toStart))
                    continue;

                var info = method.GenTreeMethod.GetValueInfo(allocation.ValueKey);
                var abi = MachineAbi.ClassifyStorageValue(info.Type, info.StackKind);
                var expected = allocation.ValueLocationAt(toStart, abi);
                if (expected.IsEmpty)
                    continue;

                if (!state.TryGetValue(allocation.ValueKey, out var actual))
                {
                    throw new InvalidOperationException(
                        $"post-LSRA CFG location invariant failed on {edge}: live-in value {allocation.ValueKey} has no physical home; expected {expected}.");
                }

                if (!LocationsEqual(actual, expected))
                {
                    throw new InvalidOperationException(
                        $"post-LSRA CFG location invariant failed on {edge}: value {allocation.ValueKey} is {actual}, expected {expected} after synthetic moves.");
                }
            }
        }

        private static void ApplyTrailingPhiCopies(
            RegisterAllocatedMethod method,
            CfgEdge edge,
            GenTreeBlock block,
            Dictionary<GenTreeValueKey, RegisterValueLocation> state)
        {
            for (int i = 0; i < block.LinearNodes.Length; i++)
            {
                var node = block.LinearNodes[i];
                if (!node.IsPhiCopy)
                    continue;

                if (IsMatchingEntryPhiCopy(edge, node))
                    ApplyNodeEffectsToLocationState(method, node, state);
            }
        }

        private static void ApplyLeadingSyntheticMoves(
            RegisterAllocatedMethod method,
            CfgEdge edge,
            GenTreeBlock block,
            Dictionary<GenTreeValueKey, RegisterValueLocation> state)
        {
            for (int i = 0; i < block.LinearNodes.Length; i++)
            {
                var node = block.LinearNodes[i];
                if (IsMatchingEntryPhiCopy(edge, node))
                {
                    ApplyNodeEffectsToLocationState(method, node, state);
                    continue;
                }

                if (node.IsPhiCopy)
                    continue;

                if (IsBlockEntrySplitMove(node))
                {
                    ApplyNodeEffectsToLocationState(method, node, state);
                    continue;
                }

                break;
            }
        }

        private static void ApplyMissingSemanticPhiCopies(
            RegisterAllocatedMethod method,
            AllocatorVerifierPositionLayout layout,
            CfgEdge edge,
            Dictionary<GenTreeValueKey, RegisterValueLocation> state)
        {
            ApplyMissingSemanticPhiCopiesFromBlock(method, layout, edge, method.GenTreeMethod.Blocks[edge.FromBlockId], state);
            if (edge.ToBlockId != edge.FromBlockId)
                ApplyMissingSemanticPhiCopiesFromBlock(method, layout, edge, method.GenTreeMethod.Blocks[edge.ToBlockId], state);
        }

        private static void ApplyMissingSemanticPhiCopiesFromBlock(
            RegisterAllocatedMethod method,
            AllocatorVerifierPositionLayout layout,
            CfgEdge edge,
            GenTreeBlock block,
            Dictionary<GenTreeValueKey, RegisterValueLocation> state)
        {
            for (int i = 0; i < block.LinearNodes.Length; i++)
            {
                var node = block.LinearNodes[i];
                if (!IsMatchingEntryPhiCopy(edge, node))
                    continue;

                if (node.RegisterResult is null || node.RegisterUses.Length != 1)
                    continue;

                var destination = node.RegisterResult;
                var destinationKey = destination.LinearValueKey;
                if (state.ContainsKey(destinationKey))
                    continue;

                var source = node.RegisterUses[0];
                if (!TryGetSourceLocationForSemanticPhi(method, layout, edge, source, state, out var sourceLocation))
                    continue;

                var destinationInfo = method.GenTreeMethod.GetValueInfo(destinationKey);
                var destinationAbi = MachineAbi.ClassifyStorageValue(destinationInfo.Type, destinationInfo.StackKind);
                state[destinationKey] = RetargetLocation(destination, destinationAbi, sourceLocation);
            }
        }

        private static bool TryGetSourceLocationForSemanticPhi(
            RegisterAllocatedMethod method,
            AllocatorVerifierPositionLayout layout,
            CfgEdge edge,
            GenTree source,
            IReadOnlyDictionary<GenTreeValueKey, RegisterValueLocation> state,
            out RegisterValueLocation location)
        {
            var sourceKey = source.LinearValueKey;
            if (state.TryGetValue(sourceKey, out location) && !location.IsEmpty)
                return true;

            if (TryGetAllocation(method, sourceKey, out var allocation))
            {
                var info = method.GenTreeMethod.GetValueInfo(sourceKey);
                var abi = MachineAbi.ClassifyStorageValue(info.Type, info.StackKind);
                int position = layout.BlockEndPositions[edge.FromBlockId];
                if (IsAllocationLiveAt(allocation, position))
                {
                    location = allocation.ValueLocationAt(position, abi);
                    return !location.IsEmpty;
                }
            }

            location = default;
            return false;
        }

        private static RegisterValueLocation RetargetLocation(
            GenTree destination,
            AbiValueInfo destinationAbi,
            RegisterValueLocation sourceLocation)
        {
            if (sourceLocation.IsEmpty || destinationAbi.PassingKind == AbiValuePassingKind.Void)
                return new RegisterValueLocation(destination, destinationAbi.PassingKind, RegisterOperand.None);

            if (destinationAbi.PassingKind == AbiValuePassingKind.MultiRegister)
            {
                if (sourceLocation.IsFragmented)
                    return new RegisterValueLocation(destination, destinationAbi.PassingKind, RegisterOperand.None, sourceLocation.Fragments);

                return new RegisterValueLocation(destination, destinationAbi.PassingKind, sourceLocation.Scalar);
            }

            return new RegisterValueLocation(destination, destinationAbi.PassingKind, sourceLocation[0]);
        }

        private static bool IsMatchingEntryPhiCopy(CfgEdge edge, GenTree node)
            => node.IsPhiCopy &&
               node.LinearPhiCopyFromBlockId == edge.FromBlockId &&
               node.LinearPhiCopyToBlockId == edge.ToBlockId;

        private static bool IsBlockEntrySplitMove(GenTree node)
            => GenTreeLirKinds.IsCopyKind(node.Kind) &&
               node.LinearKind == GenTreeLinearKind.Copy &&
               (node.MoveFlags & MoveFlags.Split) != 0;

        private static void ApplyNodeEffectsToLocationState(
            RegisterAllocatedMethod method,
            GenTree node,
            Dictionary<GenTreeValueKey, RegisterValueLocation> state)
        {
            var results = node.Results;
            if (results.Length == 0)
                return;

            var logicalResults = ResolveLogicalResultValues(method, node, results.Length);
            if (logicalResults.Length == 0)
                return;

            if (logicalResults.Length == 1)
            {
                ApplySingleLogicalResult(method, logicalResults[0], results, state);
                return;
            }

            var grouped = new Dictionary<GenTreeValueKey, List<RegisterOperand>>();
            var values = new Dictionary<GenTreeValueKey, GenTree>();
            int count = Math.Min(logicalResults.Length, results.Length);
            for (int i = 0; i < count; i++)
            {
                var value = logicalResults[i];
                var key = value.LinearValueKey;
                if (!grouped.TryGetValue(key, out var list))
                {
                    list = new List<RegisterOperand>();
                    grouped.Add(key, list);
                    values.Add(key, value);
                }
                list.Add(results[i]);
            }

            foreach (var kv in grouped)
                ApplySingleLogicalResult(method, values[kv.Key], kv.Value.ToImmutableArray(), state);
        }

        private static ImmutableArray<GenTree> ResolveLogicalResultValues(
            RegisterAllocatedMethod method,
            GenTree node,
            int resultOperandCount)
        {
            if (!node.RegisterResults.IsDefaultOrEmpty)
            {
                if (node.RegisterResults.Length == 1 && resultOperandCount > 1)
                    return node.RegisterResults;
                return node.RegisterResults;
            }

            var keys = node.LsraInfo.CodegenResultValues;
            if (keys.IsDefaultOrEmpty)
                return ImmutableArray<GenTree>.Empty;

            var builder = ImmutableArray.CreateBuilder<GenTree>(keys.Length);
            for (int i = 0; i < keys.Length; i++)
            {
                if (method.GenTreeMethod.ValueInfoByNode.TryGetValue(keys[i], out var info))
                    builder.Add(info.RepresentativeNode);
            }
            return builder.ToImmutable();
        }

        private static void ApplySingleLogicalResult(
            RegisterAllocatedMethod method,
            GenTree value,
            ImmutableArray<RegisterOperand> operands,
            Dictionary<GenTreeValueKey, RegisterValueLocation> state)
        {
            if (operands.Length == 0)
                return;

            var info = method.GenTreeMethod.GetValueInfo(value.LinearValueKey);
            var abi = MachineAbi.ClassifyStorageValue(info.Type, info.StackKind);
            if (abi.PassingKind != AbiValuePassingKind.MultiRegister)
            {
                state[value.LinearValueKey] = new RegisterValueLocation(value, abi.PassingKind, operands[0]);
                return;
            }

            var segments = MachineAbi.GetRegisterSegments(abi);
            if (operands.Length >= segments.Length)
            {
                state[value.LinearValueKey] = new RegisterValueLocation(
                    value,
                    abi.PassingKind,
                    RegisterOperand.None,
                    FirstOperands(operands, segments.Length));
                return;
            }

            var fragments = CreateMutableFragmentHome(method, value, abi, state);
            for (int i = 0; i < operands.Length; i++)
            {
                int index = FindFragmentIndex(fragments, operands[i]);
                if (index < 0 && operands.Length == 1)
                    index = FindFragmentIndexInExpectedHome(method, value, abi, operands[i]);
                if ((uint)index < (uint)fragments.Count)
                    fragments[index] = operands[i];
            }

            state[value.LinearValueKey] = new RegisterValueLocation(value, abi.PassingKind, RegisterOperand.None, fragments.ToImmutableArray());
        }


        private static ImmutableArray<RegisterOperand> FirstOperands(ImmutableArray<RegisterOperand> operands, int count)
        {
            if (operands.Length == count)
                return operands;

            var builder = ImmutableArray.CreateBuilder<RegisterOperand>(count);
            for (int i = 0; i < count; i++)
                builder.Add(operands[i]);
            return builder.ToImmutable();
        }

        private static List<RegisterOperand> CreateMutableFragmentHome(
            RegisterAllocatedMethod method,
            GenTree value,
            AbiValueInfo abi,
            IReadOnlyDictionary<GenTreeValueKey, RegisterValueLocation> state)
        {
            var key = value.LinearValueKey;
            var segments = MachineAbi.GetRegisterSegments(abi);
            if (state.TryGetValue(key, out var current) && current.Count == segments.Length)
            {
                var result = new List<RegisterOperand>(segments.Length);
                for (int i = 0; i < current.Count; i++)
                    result.Add(current[i]);
                return result;
            }

            if (TryGetAllocation(method, key, out var allocation))
            {
                var expected = allocation.ValueLocationAt(allocation.DefinitionPosition, abi);
                if (expected.Count == segments.Length)
                {
                    var result = new List<RegisterOperand>(segments.Length);
                    for (int i = 0; i < expected.Count; i++)
                        result.Add(expected[i]);
                    return result;
                }
            }

            var empty = new List<RegisterOperand>(segments.Length);
            for (int i = 0; i < segments.Length; i++)
                empty.Add(RegisterOperand.None);
            return empty;
        }

        private static int FindFragmentIndex(IReadOnlyList<RegisterOperand> fragments, RegisterOperand operand)
        {
            for (int i = 0; i < fragments.Count; i++)
            {
                if (fragments[i].Equals(operand))
                    return i;
            }
            return -1;
        }

        private static int FindFragmentIndexInExpectedHome(
            RegisterAllocatedMethod method,
            GenTree value,
            AbiValueInfo abi,
            RegisterOperand operand)
        {
            if (!TryGetAllocation(method, value.LinearValueKey, out var allocation))
                return -1;

            var expected = allocation.ValueLocationAt(allocation.DefinitionPosition, abi);
            for (int i = 0; i < expected.Count; i++)
            {
                if (expected[i].Equals(operand))
                    return i;
            }
            return -1;
        }

        private static bool TryGetAllocation(RegisterAllocatedMethod method, GenTreeValueKey key, out RegisterAllocationInfo allocation)
        {
            for (int i = 0; i < method.Allocations.Length; i++)
            {
                if (method.Allocations[i].ValueKey.Equals(key))
                {
                    allocation = method.Allocations[i];
                    return true;
                }
            }

            allocation = null!;
            return false;
        }

        private static Dictionary<GenTreeValueKey, RegisterValueLocation> CloneState(
            IReadOnlyDictionary<GenTreeValueKey, RegisterValueLocation> source)
        {
            var result = new Dictionary<GenTreeValueKey, RegisterValueLocation>(source.Count);
            foreach (var kv in source)
                result.Add(kv.Key, kv.Value);
            return result;
        }

        private static bool LocationsEqual(RegisterValueLocation actual, RegisterValueLocation expected)
        {
            if (actual.PassingKind != expected.PassingKind || actual.Count != expected.Count)
                return false;

            for (int i = 0; i < actual.Count; i++)
            {
                if (!actual[i].Equals(expected[i]))
                    return false;
            }

            return true;
        }

        private static bool IsAllocationLiveAt(RegisterAllocationInfo allocation, int position)
        {
            for (int i = 0; i < allocation.Ranges.Length; i++)
            {
                var range = allocation.Ranges[i];
                if (range.Start <= position && position < range.End)
                    return true;
            }
            return false;
        }

        private sealed class AllocatorVerifierPositionLayout
        {
            public readonly Dictionary<int, int> NodePositions;
            public readonly int[] BlockStartPositions;
            public readonly int[] BlockEndPositions;

            private AllocatorVerifierPositionLayout(Dictionary<int, int> nodePositions, int[] blockStartPositions, int[] blockEndPositions)
            {
                NodePositions = nodePositions;
                BlockStartPositions = blockStartPositions;
                BlockEndPositions = blockEndPositions;
            }

            public static AllocatorVerifierPositionLayout Build(RegisterAllocatedMethod method)
            {
                if (method.LsraBlockStartPositions.Length == method.Blocks.Length &&
                    method.LsraBlockEndPositions.Length == method.Blocks.Length &&
                    method.LsraNodePositions.Count != 0)
                {
                    var starts = new int[method.LsraBlockStartPositions.Length];
                    var ends = new int[method.LsraBlockEndPositions.Length];
                    for (int i = 0; i < starts.Length; i++)
                        starts[i] = method.LsraBlockStartPositions[i];
                    for (int i = 0; i < ends.Length; i++)
                        ends[i] = method.LsraBlockEndPositions[i];

                    return new AllocatorVerifierPositionLayout(new Dictionary<int, int>(method.LsraNodePositions), starts, ends);
                }

                var genMethod = method.GenTreeMethod;
                var nodePositions = new Dictionary<int, int>();
                var fallbackStarts = new int[genMethod.Blocks.Length];
                var fallbackEnds = new int[genMethod.Blocks.Length];
                int position = 0;

                var order = genMethod.LinearBlockOrder.IsDefaultOrEmpty
                    ? LinearBlockOrder.Compute(genMethod.Cfg)
                    : LinearBlockOrder.Normalize(genMethod.Cfg, genMethod.LinearBlockOrder);

                for (int o = 0; o < order.Length; o++)
                {
                    int b = order[o];
                    fallbackStarts[b] = position;
                    var nodes = genMethod.Blocks[b].LinearNodes;
                    for (int n = 0; n < nodes.Length; n++)
                    {
                        var node = nodes[n];
                        if (!nodePositions.TryAdd(node.LinearId, position))
                            throw new InvalidOperationException($"post-LSRA CFG location invariant failed: duplicate input LIR node id {node.LinearId}.");

                        if (node.IsPhiCopy)
                        {
                            while (n + 1 < nodes.Length && SamePhiCopyGroup(node, nodes[n + 1]))
                            {
                                n++;
                                if (!nodePositions.TryAdd(nodes[n].LinearId, position))
                                    throw new InvalidOperationException($"post-LSRA CFG location invariant failed: duplicate input LIR node id {nodes[n].LinearId}.");
                            }
                        }

                        position += 2;
                    }

                    fallbackEnds[b] = position;
                    position += 2;
                }

                return new AllocatorVerifierPositionLayout(nodePositions, fallbackStarts, fallbackEnds);
            }

            private static bool SamePhiCopyGroup(GenTree left, GenTree right)
                => left.IsPhiCopy &&
                   right.IsPhiCopy &&
                   left.LinearPhiCopyFromBlockId == right.LinearPhiCopyFromBlockId &&
                   left.LinearPhiCopyToBlockId == right.LinearPhiCopyToBlockId;
        }

        private static void VerifyFunclets(RegisterAllocatedMethod method)
        {
            if (method.Funclets.Length == 0)
                throw new InvalidOperationException("Register method has no root funclet metadata.");

            if (!method.Funclets[0].IsRoot || method.Funclets[0].EntryBlockId != 0)
                throw new InvalidOperationException("Register method funclet 0 must be the root funclet at B0.");

            var seenBlocks = new HashSet<int>();
            for (int i = 0; i < method.Funclets.Length; i++)
            {
                var funclet = method.Funclets[i];
                if (funclet.Index != i)
                    throw new InvalidOperationException($"Funclet index mismatch: expected {i}, found {funclet.Index}.");
                if ((uint)funclet.EntryBlockId >= (uint)method.Blocks.Length)
                    throw new InvalidOperationException($"Funclet {i} has invalid entry block B{funclet.EntryBlockId}.");
                if (funclet.ParentFuncletIndex >= method.Funclets.Length)
                    throw new InvalidOperationException($"Funclet {i} has invalid parent funclet {funclet.ParentFuncletIndex}.");

                for (int b = 0; b < funclet.BlockIds.Length; b++)
                {
                    int blockId = funclet.BlockIds[b];
                    if ((uint)blockId >= (uint)method.Blocks.Length)
                        throw new InvalidOperationException($"Funclet {i} contains invalid block B{blockId}.");
                    if (!seenBlocks.Add(blockId))
                        throw new InvalidOperationException($"Block B{blockId} belongs to more than one funclet.");
                }
            }
        }

        private static void VerifyFrameRegions(RegisterAllocatedMethod method)
        {
            var nodesById = new Dictionary<int, GenTree>();
            foreach (var node in method.LinearNodes)
                nodesById[node.Id] = node;

            for (int i = 0; i < method.FrameRegions.Length; i++)
            {
                var region = method.FrameRegions[i];
                if (region.FuncletIndex >= method.Funclets.Length)
                    throw new InvalidOperationException($"Frame region has invalid funclet index {region.FuncletIndex}.");
                if (!nodesById.TryGetValue(region.FirstNodeId, out var first))
                    throw new InvalidOperationException($"Frame region starts at missing node {region.FirstNodeId}.");
                if (!nodesById.TryGetValue(region.LastNodeId, out var last))
                    throw new InvalidOperationException($"Frame region ends at missing node {region.LastNodeId}.");
                if (first.BlockId != region.BlockId || last.BlockId != region.BlockId)
                    throw new InvalidOperationException("Frame region endpoints are not in the recorded block.");

                if (first.Kind != GenTreeKind.StackFrameOp || last.Kind != GenTreeKind.StackFrameOp)
                    throw new InvalidOperationException("Frame region endpoints do not match region kind.");
            }
        }

        private static void VerifyUnwindCodes(RegisterAllocatedMethod method)
        {
            var nodesById = new Dictionary<int, GenTree>();
            foreach (var node in method.LinearNodes)
                nodesById[node.Id] = node;

            var seen = new HashSet<int>();
            for (int i = 0; i < method.UnwindCodes.Length; i++)
            {
                var code = method.UnwindCodes[i];
                if (!seen.Add(code.NodeId))
                    throw new InvalidOperationException($"Duplicate unwind code for node {code.NodeId}.");

                if (!nodesById.TryGetValue(code.NodeId, out var node))
                    throw new InvalidOperationException($"Unwind code references missing node {code.NodeId}.");

                if (node.Kind != GenTreeKind.StackFrameOp)
                    throw new InvalidOperationException($"Unwind code references non-prolog node {code.NodeId}.");

                if (node.BlockId != code.BlockId || node.Ordinal != code.Ordinal)
                    throw new InvalidOperationException($"Unwind code placement does not match node {code.NodeId}.");

                if (code.Size < 0 || code.StackOffset < 0)
                    throw new InvalidOperationException($"Unwind code contains invalid stack range: {code}.");
            }

            if (method.HasPrologEpilog && !method.StackFrame.IsEmpty && method.UnwindCodes.Length == 0)
                throw new InvalidOperationException("Non-empty framed method has no unwind codes.");
        }

        private static bool IsFuncletEntryBlock(RegisterAllocatedMethod method, int blockId)
        {
            for (int i = 0; i < method.Funclets.Length; i++)
            {
                if (method.Funclets[i].EntryBlockId == blockId)
                    return true;
            }
            return false;
        }

        private static void VerifyGcInfo(RegisterAllocatedMethod method)
        {
            VerifyGcLiveRanges(method);
            VerifyGcTransitions(method);
            VerifyGcInterruptibleRanges(method);
        }

        private static void VerifyGcLiveRanges(RegisterAllocatedMethod method)
        {
            for (int i = 0; i < method.GcLiveRanges.Length; i++)
            {
                var range = method.GcLiveRanges[i];
                if (range.StartPosition >= range.EndPosition)
                    throw new InvalidOperationException($"Empty GC live range: {range}.");
                if ((uint)range.FuncletIndex >= (uint)method.Funclets.Length)
                    throw new InvalidOperationException($"GC live range has invalid funclet index: {range}.");
                VerifyGcRootIdentity(method, range.Root, "GC live range");
                if (range.Root.Location.IsNone)
                    throw new InvalidOperationException($"GC live range has no storage location: {range}.");
                if (range.Root.Offset < 0)
                    throw new InvalidOperationException($"GC live range has negative field offset: {range}.");
                if (range.Root.Offset != 0 && range.Root.Location.IsRegister)
                    throw new InvalidOperationException($"Field GC live range cannot be represented by a bare register: {range}.");
                if (range.Root.Location.IsFrameSlot && range.Root.Offset >= range.Root.Location.FrameSlotSize)
                    throw new InvalidOperationException($"GC live range offset escapes its frame slot: {range}.");
                if ((range.Flags & RegisterGcLiveRangeFlags.Pinned) != 0 && range.FuncletIndex == 0)
                    throw new InvalidOperationException($"Only filter funclet stack roots may be pinned by GC info: {range}.");
                VerifyOperandStorage(method, range.Root.Location, isUse: true);
            }

            if (method.Funclets.Length > 1 && !method.GcReportOnlyLeafFunclet)
                throw new InvalidOperationException("EH methods must use leaf-funclet-only GC reporting.");
        }

        private static void VerifyGcRootIdentity(RegisterAllocatedMethod method, RegisterGcLiveRoot root, string context)
        {
            if (root.RequiresValueInfo)
            {
                if (!method.GenTreeMethod.ValueInfoByNode.ContainsKey(root.Value.LinearValueKey))
                    throw new InvalidOperationException($"{context} references unknown GenTree value {root.Value}.");
                return;
            }

            if (!root.Location.IsFrameSlot)
                throw new InvalidOperationException($"{context} has descriptor-home identity but is not a frame slot root: {root}.");

            if (root.Value.LocalDescriptor is null)
                throw new InvalidOperationException($"{context} has descriptor-home identity but no local descriptor owner: {root}.");

            var descriptor = root.Value.LocalDescriptor;
            if (root.Type is not null && !ReferenceEquals(root.Type, descriptor.Type))
                throw new InvalidOperationException($"{context} descriptor-home type does not match the owner descriptor: {root}.");
        }

        private static void VerifyGcTransitions(RegisterAllocatedMethod method)
        {
            int previousPosition = -1;
            for (int i = 0; i < method.GcTransitions.Length; i++)
            {
                var transition = method.GcTransitions[i];
                if (transition.Position < previousPosition)
                    throw new InvalidOperationException("GC transitions are not sorted by GenTree LIR position.");
                previousPosition = transition.Position;

                switch (transition.Kind)
                {
                    case RegisterGcTransitionKind.Enter:
                        if (transition.After is null || transition.Before is not null)
                            throw new InvalidOperationException("GC enter transition must contain only the after root.");
                        VerifyTransitionRoot(method, transition.After.Value);
                        break;
                    case RegisterGcTransitionKind.Exit:
                        if (transition.Before is null || transition.After is not null)
                            throw new InvalidOperationException("GC exit transition must contain only the before root.");
                        VerifyTransitionRoot(method, transition.Before.Value);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown GC transition kind {transition.Kind}.");
                }
            }
        }

        private static void VerifyTransitionRoot(RegisterAllocatedMethod method, RegisterGcLiveRoot root)
        {
            VerifyGcRootIdentity(method, root, "GC transition");
            if (root.Location.IsNone)
                throw new InvalidOperationException($"GC transition root has no storage location: {root.Value}.");
            if (root.Offset != 0 && root.Location.IsRegister)
                throw new InvalidOperationException($"GC transition field root cannot be represented by a bare register: {root}.");
            VerifyOperandStorage(method, root.Location, isUse: true);
        }

        private static void VerifyGcInterruptibleRanges(RegisterAllocatedMethod method)
        {
            var nodesById = new Dictionary<int, GenTree>();
            foreach (var node in method.LinearNodes)
                nodesById[node.Id] = node;

            int previousStart = -1;
            for (int i = 0; i < method.GcInterruptibleRanges.Length; i++)
            {
                var range = method.GcInterruptibleRanges[i];
                if (range.StartPosition >= range.EndPosition)
                    throw new InvalidOperationException($"Empty GC interruptible range: {range}.");
                if (range.StartPosition < previousStart)
                    throw new InvalidOperationException("GC interruptible ranges are not sorted by GenTree LIR position.");
                previousStart = range.StartPosition;
                if ((uint)range.FuncletIndex >= (uint)method.Funclets.Length)
                    throw new InvalidOperationException($"GC interruptible range has invalid funclet index: {range}.");
                if (!nodesById.ContainsKey(range.FirstNodeId))
                    throw new InvalidOperationException($"GC interruptible range starts at missing node {range.FirstNodeId}.");
                if (!nodesById.ContainsKey(range.LastNodeId))
                    throw new InvalidOperationException($"GC interruptible range ends at missing node {range.LastNodeId}.");
            }

            if (method.Funclets.Length > 1)
            {
                var hasRangeByFunclet = new bool[method.Funclets.Length];
                for (int i = 0; i < method.GcInterruptibleRanges.Length; i++)
                    hasRangeByFunclet[method.GcInterruptibleRanges[i].FuncletIndex] = true;

                for (int i = 0; i < method.Funclets.Length; i++)
                {
                    if (method.Funclets[i].BlockIds.Length != 0 && !hasRangeByFunclet[i])
                        throw new InvalidOperationException($"Funclet {i} has no GC interruptible range.");
                }
            }
        }

        private static void VerifyPrologEpilog(RegisterAllocatedMethod method)
        {
            bool hasFrameLinearNodes = false;
            foreach (var node in method.LinearNodes)
            {
                if (node.Kind == GenTreeKind.StackFrameOp)
                {
                    hasFrameLinearNodes = true;
                    VerifyFrameNode(method, node);
                }
                else if (node.FrameOperation != FrameOperation.None || node.Immediate != 0)
                {
                    throw new InvalidOperationException("Non-frame node carries frame operation metadata.");
                }
            }

            if (!method.HasPrologEpilog)
            {
                if (hasFrameLinearNodes)
                    throw new InvalidOperationException("Method has prolog/epilog nodes but is not marked as frame-code generated.");
                return;
            }

            if (method.StackFrame.IsEmpty)
            {
                if (hasFrameLinearNodes)
                    throw new InvalidOperationException("Empty-frame method must not contain prolog/epilog nodes.");
                return;
            }

            if (method.Blocks.Length == 0)
                throw new InvalidOperationException("Frame-code generated method has no blocks.");

            var entry = method.Blocks[0].LinearNodes;
            int firstNonProlog = 0;
            while (firstNonProlog < entry.Length && IsPrologFrameNode(entry[firstNonProlog]))
                firstNonProlog++;

            if (firstNonProlog == 0)
                throw new InvalidOperationException("Non-empty-frame method is missing an entry prolog.");

            for (int i = firstNonProlog; i < entry.Length; i++)
            {
                if (IsPrologFrameNode(entry[i]))
                    throw new InvalidOperationException("Prolog nodes must be a contiguous prefix of the entry block.");
            }

            bool sawReturn = false;
            foreach (var block in method.Blocks)
            {
                for (int i = 0; i < block.LinearNodes.Length; i++)
                {
                    var node = block.LinearNodes[i];
                    if (node.Kind == GenTreeKind.StackFrameOp)
                    {
                        if (IsPrologFrameOperation(node.FrameOperation))
                        {
                            if (!IsFuncletEntryBlock(method, block.Id) || !IsContiguousPrologPrefix(block.LinearNodes, i))
                                throw new InvalidOperationException("Prolog node found outside a contiguous funclet-entry prolog prefix.");
                        }
                        else if (IsEpilogFrameOperation(node.FrameOperation))
                        {
                            if (!IsInContiguousEpilogBeforeExit(block.LinearNodes, i))
                                throw new InvalidOperationException("Epilog node is not part of a contiguous sequence immediately preceding a return or funclet exit.");
                        }
                    }

                    if (node.Kind == GenTreeKind.EndFinally)
                    {
                        if (!HasContiguousEpilogBefore(block.LinearNodes, i))
                            throw new InvalidOperationException("Funclet exit node is missing an immediately preceding epilog sequence.");
                    }

                    if (node.Kind == GenTreeKind.Return)
                    {
                        sawReturn = true;
                        if (!HasContiguousEpilogBefore(block.LinearNodes, i))
                            throw new InvalidOperationException("Return node is missing an immediately preceding epilog sequence.");
                    }
                }
            }

            if (!sawReturn)
                return;
        }

        private static bool HasContiguousEpilogBefore(ImmutableArray<GenTree> nodes, int returnIndex)
        {
            int i = returnIndex - 1;
            if (i < 0 || !IsEpilogFrameNode(nodes[i]))
                return false;

            while (i >= 0 && IsEpilogFrameNode(nodes[i]))
                i--;

            return true;
        }
        private static bool IsPrologFrameNode(GenTree node)
            => node.Kind == GenTreeKind.StackFrameOp && IsPrologFrameOperation(node.FrameOperation);
        private static bool IsEpilogFrameNode(GenTree node)
            => node.Kind == GenTreeKind.StackFrameOp && IsEpilogFrameOperation(node.FrameOperation);
        private static bool IsPrologFrameOperation(FrameOperation operation)
            => operation is
                FrameOperation.AllocateFrame or
                FrameOperation.SaveReturnAddress or
                FrameOperation.SaveCalleeSavedRegister or
                FrameOperation.EstablishFramePointer or
                FrameOperation.EnterFuncletFrame;
        private static bool IsEpilogFrameOperation(FrameOperation operation)
            => operation is
                FrameOperation.LeaveFuncletFrame or
                FrameOperation.RestoreStackPointerFromFramePointer or
                FrameOperation.RestoreCalleeSavedRegister or
                FrameOperation.RestoreReturnAddress or
                FrameOperation.FreeFrame;
        private static bool IsContiguousPrologPrefix(ImmutableArray<GenTree> nodes, int index)
        {
            if ((uint)index >= (uint)nodes.Length || !IsPrologFrameNode(nodes[index]))
                return false;
            for (int i = 0; i <= index; i++)
            {
                if (!IsPrologFrameNode(nodes[i]))
                    return false;
            }
            return true;
        }
        private static bool IsInContiguousEpilogBeforeExit(ImmutableArray<GenTree> nodes, int index)
        {
            if ((uint)index >= (uint)nodes.Length || !IsEpilogFrameNode(nodes[index]))
                return false;
            int i = index;
            while (i < nodes.Length && IsEpilogFrameNode(nodes[i]))
                i++;
            return i < nodes.Length && nodes[i].Kind is GenTreeKind.Return or GenTreeKind.EndFinally;
        }
        private static void VerifyFrameNode(RegisterAllocatedMethod method, GenTree node)
        {
            if (node.FrameOperation == FrameOperation.None)
                throw new InvalidOperationException("Frame node has no frame operation.");

            if (node.RegisterResults.Length != 0 ||
                node.RegisterUses.Length != 0 ||
                node.LsraInfo.CodegenResultValues.Length != 0 ||
                node.LsraInfo.CodegenUseValues.Length != 0 ||
                method.GenTreeMethod.ValueInfoByNode.ContainsKey(node.ValueKey))
            {
                throw new InvalidOperationException("Frame node must not carry GenTree value metadata.");
            }

            if (node.Operands.Length != 0)
                throw new InvalidOperationException("Frame node must not have GenTree operands.");

            if (node.Results.Length != 1)
                throw new InvalidOperationException("Frame node must have exactly one result operand.");

            var result = node.Results[0];
            switch (node.FrameOperation)
            {
                case FrameOperation.AllocateFrame:
                    RequireFrameKind(node, GenTreeKind.StackFrameOp);
                    RequireRegister(result, MachineRegisters.StackPointer, "frame allocation result");
                    RequireSingleRegisterUse(node, MachineRegisters.StackPointer, "frame allocation use");
                    RequireImmediate(node, method.StackFrame.FrameSize, "frame allocation size");
                    return;

                case FrameOperation.SaveReturnAddress:
                    RequireFrameKind(node, GenTreeKind.StackFrameOp);
                    if (!result.IsFrameSlot || result.FrameSlotKind != StackFrameSlotKind.ReturnAddress)
                        throw new InvalidOperationException("Return-address prolog node must write a return-address frame slot.");
                    if (result.FrameBase != RegisterFrameBase.StackPointer)
                        throw new InvalidOperationException("Return-address prolog node must write a stack-pointer-relative frame slot.");
                    RequireSingleRegisterUse(node, MachineRegisters.ReturnAddress, "return-address save use");
                    RequireImmediate(node, 0, "return-address save immediate");
                    return;

                case FrameOperation.SaveCalleeSavedRegister:
                    RequireFrameKind(node, GenTreeKind.StackFrameOp);
                    if (!result.IsFrameSlot || result.FrameSlotKind != StackFrameSlotKind.CalleeSavedRegister)
                        throw new InvalidOperationException("Callee-save prolog node must write a callee-saved frame slot.");
                    if (result.FrameBase != RegisterFrameBase.StackPointer)
                        throw new InvalidOperationException("Callee-save prolog node must write a stack-pointer-relative frame slot.");
                    if (node.Uses.Length != 1 || !node.Uses[0].IsRegister || !MachineRegisters.IsCalleeSaved(node.Uses[0].Register))
                        throw new InvalidOperationException("Callee-save prolog node must read one callee-saved register.");
                    RequireImmediate(node, 0, "callee-save prolog immediate");
                    return;

                case FrameOperation.EstablishFramePointer:
                    RequireFrameKind(node, GenTreeKind.StackFrameOp);
                    RequireRegister(result, MachineRegisters.FramePointer, "frame pointer establishment result");
                    RequireSingleRegisterUse(node, MachineRegisters.StackPointer, "frame pointer establishment use");
                    RequireImmediate(node, 0, "frame pointer establishment immediate");
                    return;

                case FrameOperation.EnterFuncletFrame:
                    RequireFrameKind(node, GenTreeKind.StackFrameOp);
                    if (!result.IsRegister || result.Register is not (MachineRegister.X2 or MachineRegister.X8))
                        throw new InvalidOperationException("Funclet prolog must establish either SP or FP as the funclet frame anchor.");
                    RequireSingleRegisterUse(node, MachineRegisters.StackPointer, "funclet frame establishment use");
                    RequireImmediate(node, 0, "funclet frame establishment immediate");
                    return;

                case FrameOperation.LeaveFuncletFrame:
                    RequireFrameKind(node, GenTreeKind.StackFrameOp);
                    RequireRegister(result, MachineRegisters.StackPointer, "funclet frame detach result");
                    if (node.Uses.Length != 1 || !node.Uses[0].IsRegister || node.Uses[0].Register is not (MachineRegister.X2 or MachineRegister.X8))
                        throw new InvalidOperationException("Funclet epilog must read exactly one SP or FP frame anchor.");
                    RequireImmediate(node, 0, "funclet frame detach immediate");
                    return;

                case FrameOperation.RestoreStackPointerFromFramePointer:
                    RequireFrameKind(node, GenTreeKind.StackFrameOp);
                    RequireRegister(result, MachineRegisters.StackPointer, "stack pointer restore result");
                    RequireSingleRegisterUse(node, MachineRegisters.FramePointer, "stack pointer restore use");
                    RequireImmediate(node, 0, "stack pointer restore immediate");
                    return;

                case FrameOperation.RestoreCalleeSavedRegister:
                    RequireFrameKind(node, GenTreeKind.StackFrameOp);
                    if (!result.IsRegister || !MachineRegisters.IsCalleeSaved(result.Register))
                        throw new InvalidOperationException("Callee-save epilog node must write one callee-saved register.");
                    if (node.Uses.Length != 1
                        || !node.Uses[0].IsFrameSlot
                        || node.Uses[0].FrameSlotKind != StackFrameSlotKind.CalleeSavedRegister)
                        throw new InvalidOperationException("Callee-save epilog node must read one callee-saved frame slot.");
                    if (node.Uses[0].FrameBase != RegisterFrameBase.StackPointer)
                        throw new InvalidOperationException("Callee-save epilog node must read a stack-pointer-relative frame slot.");
                    RequireImmediate(node, 0, "callee-save epilog immediate");
                    return;

                case FrameOperation.RestoreReturnAddress:
                    RequireFrameKind(node, GenTreeKind.StackFrameOp);
                    RequireRegister(result, MachineRegisters.ReturnAddress, "return-address restore result");
                    if (node.Uses.Length != 1
                        || !node.Uses[0].IsFrameSlot
                        || node.Uses[0].FrameSlotKind != StackFrameSlotKind.ReturnAddress)
                        throw new InvalidOperationException("Return-address epilog node must read one return-address frame slot.");
                    if (node.Uses[0].FrameBase != RegisterFrameBase.StackPointer)
                        throw new InvalidOperationException("Return-address epilog node must read a stack-pointer-relative frame slot.");
                    RequireImmediate(node, 0, "return-address restore immediate");
                    return;

                case FrameOperation.FreeFrame:
                    RequireFrameKind(node, GenTreeKind.StackFrameOp);
                    RequireRegister(result, MachineRegisters.StackPointer, "frame free result");
                    RequireSingleRegisterUse(node, MachineRegisters.StackPointer, "frame free use");
                    RequireImmediate(node, method.StackFrame.FrameSize, "frame free size");
                    return;

                default:
                    throw new InvalidOperationException("Unknown frame operation " + node.FrameOperation + ".");
            }
        }

        private static void RequireFrameKind(GenTree node, GenTreeKind expected)
        {
            if (node.Kind != GenTreeKind.StackFrameOp)
                throw new InvalidOperationException(
                    $"Frame operation {node.FrameOperation} is stored as {node.Kind}, expected {expected}.");
        }

        private static void RequireImmediate(GenTree node, int expected, string what)
        {
            if (node.Immediate != expected)
                throw new InvalidOperationException($"{what} must be {expected}, found {node.Immediate}.");
        }

        private static void RequireRegister(RegisterOperand operand, MachineRegister expected, string what)
        {
            if (!operand.IsRegister || operand.Register != expected)
                throw new InvalidOperationException($"{what} must be {MachineRegisters.Format(expected)}.");
        }

        private static void RequireSingleRegisterUse(GenTree node, MachineRegister expected, string what)
        {
            if (node.Uses.Length != 1)
                throw new InvalidOperationException(what + " must contain exactly one use.");
            RequireRegister(node.Uses[0], expected, what);
        }

        private static void VerifyAllValuesAllocated(RegisterAllocatedMethod method)
        {
            for (int i = 0; i < method.GenTreeMethod.Values.Length; i++)
            {
                var value = method.GenTreeMethod.Values[i].RepresentativeNode;
                if (!method.AllocationByNode.ContainsKey(value))
                    throw new InvalidOperationException($"Missing register allocation for {value}.");
            }
        }

        private static void VerifyNoOverlappingRegisterIntervals(RegisterAllocatedMethod method)
        {
            var segments = CollectLocatedRegisterSegments(method);
            for (int i = 0; i < segments.Count; i++)
            {
                var left = segments[i];
                for (int j = i + 1; j < segments.Count; j++)
                {
                    var right = segments[j];
                    if (left.Segment.Location.Register != right.Segment.Location.Register)
                        continue;
                    if (left.Segment.Start >= right.Segment.End || right.Segment.Start >= left.Segment.End)
                        continue;

                    int start = Math.Max(left.Segment.Start, right.Segment.Start);
                    int end = Math.Min(left.Segment.End, right.Segment.End);
                    if (!RangesIntersect(left.Allocation.Ranges, right.Allocation.Ranges, start, end))
                        continue;

                    throw new InvalidOperationException(
                        "Register " + MachineRegisters.Format(left.Segment.Location.Register) +
                        $" assigned to overlapping allocation segments {left.DisplayName} and {right.DisplayName}.");
                }
            }
        }

        private readonly struct LocatedRegisterSegment
        {
            public readonly RegisterAllocationInfo Allocation;
            public readonly RegisterAllocationSegment Segment;
            public readonly string DisplayName;

            public LocatedRegisterSegment(RegisterAllocationInfo allocation, RegisterAllocationSegment segment, string displayName)
            {
                Allocation = allocation;
                Segment = segment;
                DisplayName = displayName;
            }
        }

        private static List<LocatedRegisterSegment> CollectLocatedRegisterSegments(RegisterAllocatedMethod method)
        {
            var result = new List<LocatedRegisterSegment>();
            for (int i = 0; i < method.Allocations.Length; i++)
            {
                var allocation = method.Allocations[i];
                for (int s = 0; s < allocation.Segments.Length; s++)
                {
                    var segment = allocation.Segments[s];
                    if (segment.Location.IsRegister)
                        result.Add(new LocatedRegisterSegment(allocation, segment, allocation.Value.ToString()));
                }

                for (int f = 0; f < allocation.Fragments.Length; f++)
                {
                    var fragment = allocation.Fragments[f];
                    for (int s = 0; s < fragment.Segments.Length; s++)
                    {
                        var segment = fragment.Segments[s];
                        if (segment.Location.IsRegister)
                            result.Add(new LocatedRegisterSegment(
                                allocation,
                                segment,
                                allocation.Value + "#" + fragment.SegmentIndex.ToString()));
                    }
                }
            }

            return result;
        }
        private static bool RangesIntersect(ImmutableArray<LinearLiveRange> left, ImmutableArray<LinearLiveRange> right, int minPosition, int maxPosition)
        {
            int i = 0;
            int j = 0;
            while (i < left.Length && j < right.Length)
            {
                var a = left[i];
                var b = right[j];
                int start = Math.Max(Math.Max(a.Start, b.Start), minPosition);
                int end = Math.Min(Math.Min(a.End, b.End), maxPosition);
                if (start < end)
                    return true;
                if (a.End <= b.Start)
                    i++;
                else
                    j++;
            }
            return false;
        }

        private static void VerifyNoCallerSavedLiveAcrossCalls(RegisterAllocatedMethod method)
        {
            var callPositions = BuildCallPositions(method.GenTreeMethod);
            if (callPositions.Length == 0)
                return;

            var segments = CollectLocatedRegisterSegments(method);
            for (int i = 0; i < segments.Count; i++)
            {
                var located = segments[i];
                var segment = located.Segment;
                var allocation = located.Allocation;
                if (!MachineRegisters.IsCallerSaved(segment.Location.Register))
                    continue;

                for (int c = 0; c < callPositions.Length; c++)
                {
                    int callPos = callPositions[c];
                    if (!segment.Contains(callPos))
                        continue;

                    if (RangesCrossCall(allocation.Ranges, callPos))
                    {
                        throw new InvalidOperationException(
                            "Caller-saved register " + MachineRegisters.Format(segment.Location.Register) +
                            $" assigned to allocation segment of {located.DisplayName} live across call at GenTree LIR position {callPos}.");
                    }
                }
            }
        }

        private static ImmutableArray<int> BuildCallPositions(GenTreeMethod method)
        {
            var positions = new SortedSet<int>();
            for (int i = 0; i < method.RefPositions.Length; i++)
            {
                var rp = method.RefPositions[i];
                if (rp.Kind == LinearRefPositionKind.Kill && rp.FixedRegister != MachineRegister.Invalid)
                    positions.Add(rp.Position);
            }

            if (positions.Count == 0)
            {
                int position = 0;
                var order = method.LinearBlockOrder.IsDefaultOrEmpty
                    ? LinearBlockOrder.Compute(method.Cfg)
                    : method.LinearBlockOrder;

                for (int o = 0; o < order.Length; o++)
                {
                    int b = order[o];
                    var nodes = method.Blocks[b].LinearNodes;
                    for (int n = 0; n < nodes.Length; n++)
                    {
                        var node = nodes[n];
                        if (node.HasLoweringFlag(GenTreeLinearFlags.CallerSavedKill))
                            positions.Add(position);
                        position += 2;
                    }
                    position += 2;
                }
            }

            return positions.ToImmutableArray();
        }

        private static bool RangesCrossCall(ImmutableArray<LinearLiveRange> ranges, int callPosition)
        {
            for (int i = 0; i < ranges.Length; i++)
            {
                var range = ranges[i];
                if (range.Start <= callPosition && callPosition + 1 < range.End)
                    return true;
            }
            return false;
        }

        private static void VerifyStackFrameLayout(RegisterAllocatedMethod method)
        {
            var layout = method.StackFrame;
            if (layout.IsEmpty)
                return;

            if (layout.FrameAlignment <= 0)
                throw new InvalidOperationException("Invalid stack frame alignment.");
            if (layout.FrameSize < 0 || layout.FrameSize % layout.FrameAlignment != 0)
                throw new InvalidOperationException("Invalid finalized stack frame size " + layout.FrameSize + ".");

            if (method.Funclets.Length > 1)
            {
                if (layout.FrameModel != RegisterStackFrameModel.SharedRootFrameWithFunclets)
                    throw new InvalidOperationException("Funclet method must use a shared root stack frame model.");
                if (!layout.UsesFramePointer)
                    throw new InvalidOperationException("Funclet method must preserve a stable frame pointer.");
            }
            else if (layout.FrameModel == RegisterStackFrameModel.SharedRootFrameWithFunclets)
            {
                throw new InvalidOperationException("Shared funclet stack frame model used by a method without funclets.");
            }
            else if (layout.FrameModel == RegisterStackFrameModel.Leaf && layout.FrameSize != 0)
            {
                throw new InvalidOperationException("Leaf stack frame model cannot have a non-empty frame.");
            }

            var slots = new List<StackFrameSlot>();
            slots.AddRange(layout.CalleeSavedSlots);
            slots.AddRange(layout.ArgumentSlots);
            slots.AddRange(layout.LocalSlots);
            slots.AddRange(layout.TempSlots);
            slots.AddRange(layout.SpillSlots);
            slots.AddRange(layout.OutgoingArgumentSlots);

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot.Offset < 0 || slot.Size < 0 || slot.Alignment <= 0)
                    throw new InvalidOperationException($"Invalid stack frame slot {slot}.");
                if (slot.Offset % slot.Alignment != 0)
                    throw new InvalidOperationException($"Misaligned stack frame slot {slot}.");
                if (slot.EndOffset > layout.FrameSize)
                    throw new InvalidOperationException($"Stack frame slot escapes frame: " + slot + ".");

                for (int j = i + 1; j < slots.Count; j++)
                {
                    var other = slots[j];
                    if (slot.Offset < other.EndOffset && other.Offset < slot.EndOffset)
                        throw new InvalidOperationException($"Overlapping stack frame slots: {slot} and {other}.");
                }
            }
        }

        private static void VerifyNodeOperands(RegisterAllocatedMethod method)
        {
            foreach (var block in method.Blocks)
            {
                foreach (var node in block.LinearNodes)
                    VerifyNode(method, node);
            }
        }

        private static void VerifyNode(RegisterAllocatedMethod method, GenTree node)
        {
            if (node.Kind == GenTreeKind.GcPoll)
            {
                if (node.Results.Length != 0 || node.Uses.Length != 0 ||
                    node.RegisterResults.Length != 0 || node.RegisterUses.Length != 0 ||
                    node.UseRoles.Length != 0 || node.GenTreeLinearId < 0)
                {
                    throw new InvalidOperationException("GC poll node must be standalone and linked to a linear IR poll node.");
                }

                if (node.Source is not null && node.Source.LinearId != node.GenTreeLinearId)
                {
                    throw new InvalidOperationException(
                        $"GC poll node {node.Id} is linked to GenTree GenTree LIR node {node.GenTreeLinearId}, " +
                        $"but its GenTree source has linear IR id {node.Source.LinearId}.");
                }
                return;
            }

            if (node.UseRoles.Length != node.Uses.Length)
                throw new InvalidOperationException("Node use role count does not match use operand count.");

            if (node.Source is not null && node.GenTreeLinearId >= 0 && node.Source.LinearId != node.GenTreeLinearId)
            {
                throw new InvalidOperationException(
                    $"Register node {node.Id} is linked to GenTree GenTree LIR node {node.GenTreeLinearId}, " +
                    $"but its GenTree source has linear IR id {node.Source.LinearId}.");
            }

            for (int i = 0; i < node.Results.Length; i++)
            {
                var operand = node.Results[i];
                if (operand.IsNone)
                    throw new InvalidOperationException("Node results contain empty register operand.");

                VerifyOperandStorage(method, operand, isUse: false);

                if (i < node.RegisterResults.Length)
                    VerifyOperandClass(method, operand, node.RegisterResults[i], "result");
            }

            for (int i = 0; i < node.Uses.Length; i++)
            {
                var operand = node.Uses[i];
                if (operand.IsNone)
                    throw new InvalidOperationException("Node uses empty register operand.");

                VerifyOperandStorage(method, operand, isUse: true);

                if (i < node.RegisterUses.Length && node.UseRoles[i] != OperandRole.HiddenReturnBuffer)
                    VerifyOperandClass(method, operand, node.RegisterUses[i], "use");
            }


            if (GenTreeLirKinds.IsCopyKind(node.Kind) &&
                node.Results.Length == 1 && node.Uses.Length == 1 &&
                node.Results[0].RegisterClass != node.Uses[0].RegisterClass &&
                node.MoveKind != MoveKind.Register)
            {
                throw new InvalidOperationException($"Move crosses register classes: {node.Uses[0]} -> {node.Results[0]}.");
            }

            if (node.MoveKind == MoveKind.MemoryToMemory &&
                !IsBlockCopyMove(method, node))
            {
                throw new InvalidOperationException(
                    $"Move must not be memory-to-memory after copy resolution: {node.Uses[0]} -> {node.Results[0]}.");
            }

            if (GenTreeLirKinds.IsRealTree(node))
            {
                if (IsCallLike(node.TreeKind))
                    VerifyCallLikeAbiShape(method, node);
                else if (node.TreeKind == GenTreeKind.Return)
                    VerifyReturnAbiShape(method, node);

                if (RequiresRegisterOnlyTreeShape(node))
                    VerifyRegisterOnlyTreeShape(node);
            }
        }


        private static bool IsBlockCopyMove(RegisterAllocatedMethod method, GenTree node)
        {
            for (int i = 0; i < node.RegisterResults.Length; i++)
            {
                if (IsBlockCopyValue(method, node.RegisterResults[i]))
                    return true;
            }
            if (node.RegisterUses.Length != 0 && IsBlockCopyValue(method, node.RegisterUses[0]))
                return true;
            return false;
        }

        private static bool IsBlockCopyValue(RegisterAllocatedMethod method, GenTree value)
        {
            var valueInfo = method.GenTreeMethod.GetValueInfo(value);
            return MachineAbi.IsBlockCopyValue(valueInfo.Type, valueInfo.StackKind);
        }

        private static bool IsCallLike(GenTreeKind kind)
            => kind is
                GenTreeKind.Call or
                GenTreeKind.VirtualCall or
                GenTreeKind.NewObject;

        private static bool RequiresRegisterOnlyTreeShape(GenTree node)
        {
            if (!GenTreeLirKinds.IsRealTree(node))
                return false;

            var lowering = GenTreeLinearLoweringClassifier.Classify(
                node.Source,
                node.RegisterResults.Length == 1 ? node.RegisterResults[0] : null,
                node.RegisterUses);

            return lowering.HasFlag(GenTreeLinearFlags.RequiresRegisterOperands) &&
                   !lowering.HasFlag(GenTreeLinearFlags.AbiCall);
        }

        private static void VerifyRegisterOnlyTreeShape(GenTree node)
        {
            for (int i = 0; i < node.Results.Length; i++)
            {
                if (!node.Results[i].IsRegister)
                {
                    throw new InvalidOperationException(
                        $"Lowered tree result {i} must be a register, actual: {node.Results[i]}.");
                }
            }

            for (int i = 0; i < node.Uses.Length; i++)
            {
                if (!node.Uses[i].IsRegister)
                {
                    throw new InvalidOperationException(
                        $"Lowered tree use {i} must be a register, actual: {node.Uses[i]}.");
                }
            }
        }

        private static void VerifyCallLikeAbiShape(RegisterAllocatedMethod method, GenTree node)
        {
            if (node.Uses.Length != node.RegisterUses.Length)
                throw new InvalidOperationException("Call-like node must preserve one GenTree value per ABI argument operand or fragment.");
            if (node.UseRoles.Length != node.Uses.Length)
                throw new InvalidOperationException("Call-like node must preserve one operand role per ABI argument operand or fragment.");

            var descriptor = BuildExpectedCallDescriptorFromAllocatedShape(method, node);
            if (descriptor.ArgumentSegments.Length != node.Uses.Length)
            {
                throw new InvalidOperationException(
                    $"Call-like node ABI operand count mismatch. Actual: {node.Uses.Length}" +
                    $", expected: {descriptor.ArgumentSegments.Length}.");
            }

            for (int i = 0; i < descriptor.ArgumentSegments.Length; i++)
            {
                var segment = descriptor.ArgumentSegments[i];
                var expectedRole = segment.IsHiddenReturnBuffer
                    ? OperandRole.HiddenReturnBuffer
                    : OperandRole.Normal;
                if (node.UseRoles[i] != expectedRole)
                {
                    throw new InvalidOperationException(
                        $"Call argument {i} has wrong ABI role. Actual: {node.UseRoles[i]}, expected: {expectedRole}.");
                }

                if (!node.RegisterUses[i].Equals(segment.Value))
                {
                    throw new InvalidOperationException(
                        $"Call argument {i} has wrong GenTree value metadata. Actual: {node.RegisterUses[i]}, expected: {segment.Value}.");
                }

                var expected = ExpectedAbiArgumentOperand(segment.Location);
                if (!MatchesAbiOperand(method, node.Uses[i], expected))
                {
                    throw new InvalidOperationException(
                        $"Call argument {i} is not in the expected ABI location. Actual: {node.Uses[i]}, expected: {expected}.");
                }
            }

            if (node.RegisterResult is not null)
            {
                var abi = descriptor.ReturnAbi;
                if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                {
                    if (node.RegisterResults.Length != 0 || node.Results.Length != 0)
                    {
                        if (node.RegisterResults.Length != 1 || !node.RegisterResults[0].Equals(node.RegisterResult))
                            throw new InvalidOperationException("Scalar call-like result must preserve its linear IR result metadata.");
                        if (node.Results.Length != 1)
                            throw new InvalidOperationException("Scalar call-like result must have exactly one register result operand.");

                        var expectedReturn = RegisterOperand.ForRegister(
                            abi.RegisterClass == RegisterClass.Float
                                ? MachineRegisters.FloatReturnValue0
                                : MachineRegisters.ReturnValue0);

                        if (!node.Results[0].Equals(expectedReturn))
                        {
                            throw new InvalidOperationException(
                                "Call-like node result is not in the ABI return register. Actual: " +
                                node.Results[0] + ", expected: " + expectedReturn + ".");
                        }
                    }
                }
                else if (abi.PassingKind == AbiValuePassingKind.Indirect)
                {
                    if (node.RegisterResults.Length != 0 || node.Results.Length != 0)
                        throw new InvalidOperationException(
                            "Indirect call-like results must be represented by a hidden return-buffer argument, not a result operand.");
                    if (!descriptor.HasHiddenReturnBuffer)
                        throw new InvalidOperationException("Indirect call-like result has no hidden return-buffer descriptor segment.");
                }
                else if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    if (node.RegisterResults.Length != 0 || node.Results.Length != 0)
                        throw new InvalidOperationException("Multi-register call-like result must not be represented on the call node itself.");
                }
                else if (node.Results.Length != 0 || node.RegisterResults.Length != 0)
                {
                    throw new InvalidOperationException("Void call-like result must not have result operands.");
                }
            }
            else if (node.Results.Length != 0 || node.RegisterResults.Length != 0)
            {
                throw new InvalidOperationException("Void call-like node must not have result operands.");
            }
        }

        private static AbiCallDescriptor BuildExpectedCallDescriptorFromAllocatedShape(RegisterAllocatedMethod method, GenTree node)
        {
            if (node.RegisterUses.Length != node.Uses.Length)
                throw new InvalidOperationException("Call-like node must preserve one GenTree value per ABI argument operand or fragment.");
            if (node.UseRoles.Length != node.Uses.Length)
                throw new InvalidOperationException("Call-like node must preserve one operand role per ABI argument operand or fragment.");

            var explicitArguments = ImmutableArray.CreateBuilder<GenTree>();
            GenTree? hiddenReturnBufferValue = null;

            int index = 0;
            while (index < node.RegisterUses.Length)
            {
                var role = node.UseRoles[index];
                if (role == OperandRole.HiddenReturnBuffer)
                {
                    if (hiddenReturnBufferValue is not null)
                        throw new InvalidOperationException("Call-like node has more than one hidden return-buffer operand.");
                    if (node.RegisterResult is not null)
                        throw new InvalidOperationException("Hidden return-buffer call-like node must not also expose a result value on the call node.");

                    hiddenReturnBufferValue = node.RegisterUses[index];
                    index++;
                    continue;
                }

                if (role != OperandRole.Normal)
                    throw new InvalidOperationException("Call-like node has an unknown ABI operand role: " + role + ".");

                AddCompressedExpandedCallArgument(method, node, explicitArguments, ref index);
            }

            return MachineAbi.BuildCallDescriptor(
                explicitArguments.ToImmutable(),
                method.GenTreeMethod.GetValueInfo,
                hiddenReturnBufferValue ?? node.RegisterResult,
                node.Method,
                node.Kind == GenTreeKind.NewObject);
        }

        private static void AddCompressedExpandedCallArgument(
            RegisterAllocatedMethod method,
            GenTree node,
            ImmutableArray<GenTree>.Builder explicitArguments,
            ref int index)
        {
            if ((uint)index >= (uint)node.RegisterUses.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            var value = node.RegisterUses[index];
            var info = method.GenTreeMethod.GetValueInfo(value);
            var abi = MachineAbi.ClassifyValue(info.Type, info.StackKind, isReturn: false);
            int operandCount = abi.PassingKind == AbiValuePassingKind.MultiRegister
                ? MachineAbi.GetRegisterSegments(abi).Length
                : 1;

            if (operandCount <= 0)
                operandCount = 1;
            if (index + operandCount > node.RegisterUses.Length)
            {
                throw new InvalidOperationException(
                    $"Call-like node has an incomplete expanded ABI argument for {value}. " +
                    $"Actual remaining operands: {node.RegisterUses.Length - index}, expected: {operandCount}.");
            }

            for (int i = 0; i < operandCount; i++)
            {
                int operandIndex = index + i;
                if (node.UseRoles[operandIndex] != OperandRole.Normal)
                {
                    throw new InvalidOperationException(
                        $"Expanded ABI argument fragment {i} for {value} has wrong role. " +
                        $"Actual: {node.UseRoles[operandIndex]}, expected: {OperandRole.Normal}.");
                }

                if (!node.RegisterUses[operandIndex].Equals(value))
                {
                    throw new InvalidOperationException(
                        $"Expanded ABI argument fragment {i} has wrong GenTree value metadata. " +
                        $"Actual: {node.RegisterUses[operandIndex]}, expected: {value}.");
                }
            }

            explicitArguments.Add(value);
            index += operandCount;
        }

        private static RegisterOperand ExpectedAbiArgumentOperand(AbiArgumentLocation location)
        {
            if (location.IsRegister)
                return RegisterOperand.ForRegister(location.Register);

            return RegisterOperand.ForOutgoingArgumentSlot(
                location.RegisterClass,
                location.StackSlotIndex,
                location.StackOffset,
                location.Size);
        }

        private static void VerifyReturnAbiShape(RegisterAllocatedMethod method, GenTree node)
        {
            if (node.Uses.Length == 0)
                return;

            if (node.RegisterUses.Length == 0 || node.Uses.Length != node.RegisterUses.Length)
                throw new InvalidOperationException("Return node must preserve one GenTree value per ABI return operand or fragment.");

            var value = node.RegisterUses[0];
            for (int i = 1; i < node.RegisterUses.Length; i++)
            {
                if (!node.RegisterUses[i].Equals(value))
                    throw new InvalidOperationException("Return node fragments must all refer to the same GenTree value.");
            }

            var valueInfo = method.GenTreeMethod.GetValueInfo(value);
            var abi = MachineAbi.ClassifyValue(valueInfo.Type, valueInfo.StackKind, isReturn: true);

            if (abi.PassingKind is not (AbiValuePassingKind.ScalarRegister or AbiValuePassingKind.MultiRegister))
            {
                if (node.Uses.Length != 1)
                    throw new InvalidOperationException("Composite buffer return must carry exactly one return-buffer operand.");
                if (node.Uses[0].IsRegister)
                    throw new InvalidOperationException($"Composite buffer return must not be rewritten to a scalar return register: {node.Uses[0]}.");
                return;
            }

            var expected = ExpectedReturnOperands(abi);
            if (node.Uses.Length != expected.Length)
                throw new InvalidOperationException($"Return node ABI operand count mismatch. Actual: {node.Uses.Length}, expected: {expected.Length}.");

            for (int i = 0; i < expected.Length; i++)
            {
                if (!node.Uses[i].Equals(expected[i]))
                {
                    throw new InvalidOperationException(
                        $"Return value fragment {i} is not in the ABI return register. Actual: " +
                        $"{node.Uses[i]}, expected: {expected[i]}.");
                }
            }
        }

        private static ImmutableArray<RegisterOperand> ExpectedReturnOperands(AbiValueInfo abi)
        {
            if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                return ImmutableArray.Create(RegisterOperand.ForRegister(
                    abi.RegisterClass == RegisterClass.Float ? MachineRegisters.FloatReturnValue0 : MachineRegisters.ReturnValue0));

            if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
            {
                var segments = MachineAbi.GetRegisterSegments(abi);
                var result = ImmutableArray.CreateBuilder<RegisterOperand>(segments.Length);
                int general = 0;
                int floating = 0;
                for (int i = 0; i < segments.Length; i++)
                    result.Add(RegisterOperand.ForRegister(GetReturnRegisterForVerifier(segments[i].RegisterClass, ref general, ref floating)));
                return result.ToImmutable();
            }

            return ImmutableArray<RegisterOperand>.Empty;
        }

        private static MachineRegister GetReturnRegisterForVerifier(RegisterClass registerClass, ref int generalIndex, ref int floatIndex)
        {
            if (registerClass == RegisterClass.Float)
            {
                int index = floatIndex++;
                return index switch
                {
                    0 => MachineRegisters.FloatReturnValue0,
                    1 => MachineRegisters.FloatReturnValue1,
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
                    _ => MachineRegister.Invalid,
                };
            }
            return MachineRegister.Invalid;
        }

        private static bool MatchesAbiOperand(RegisterAllocatedMethod method, RegisterOperand actual, RegisterOperand expected)
        {
            if (expected.IsRegister)
                return actual.Equals(expected);

            if (!expected.IsOutgoingArgumentSlot)
                return actual.Equals(expected);

            if (actual.RegisterClass != expected.RegisterClass)
                return false;

            if (actual.IsOutgoingArgumentSlot)
            {
                return actual.FrameSlotIndex == expected.FrameSlotIndex &&
                       actual.FrameOffset == expected.FrameOffset &&
                       actual.FrameSlotSize == expected.FrameSlotSize;
            }

            if (!actual.IsFrameSlot || actual.FrameSlotKind != StackFrameSlotKind.OutgoingArgument || actual.FrameSlotIndex != expected.FrameSlotIndex)
                return false;

            if (!method.StackFrame.TryGetOutgoingArgumentSlot(expected.FrameSlotIndex, out var slot))
                return false;

            int expectedOffset = checked(slot.Offset + expected.FrameOffset);
            int expectedSize = expected.FrameSlotSize > 0 ? expected.FrameSlotSize : slot.Size;
            return actual.FrameOffset == expectedOffset && actual.FrameSlotSize == expectedSize;
        }


        private static void VerifyOperandStorage(RegisterAllocatedMethod method, RegisterOperand operand, bool isUse)
        {
            if (operand.IsNone)
                return;

            if (operand.RegisterClass == RegisterClass.Invalid)
                throw new InvalidOperationException("Operand has invalid register class.");

            if (operand.IsRegister)
            {
                if (operand.Register == MachineRegister.Invalid)
                    throw new InvalidOperationException(isUse ? "Node uses invalid register." : "Node has invalid result register.");
                if (!MachineRegisters.IsRegisterInClass(operand.Register, operand.RegisterClass))
                    throw new InvalidOperationException($"Register {MachineRegisters.Format(operand.Register)} does not match operand class {operand.RegisterClass}.");
            }

            if (operand.IsSpillSlot && (uint)operand.SpillSlot >= (uint)method.SpillSlotCount)
                throw new InvalidOperationException((isUse ? "Node uses invalid spill slot " : "Node writes invalid spill slot ") + operand.SpillSlot + ".");

            if (operand.IsUnresolvedFrameSlot)
                throw new InvalidOperationException($"Node contains an unfinalized frame operand: {operand}.");

            if (operand.IsFrameSlot)
            {
                if (operand.FrameSlotKind == StackFrameSlotKind.Invalid)
                    throw new InvalidOperationException("Frame operand has invalid slot kind.");
                if (operand.FrameBase is not (RegisterFrameBase.StackPointer or RegisterFrameBase.FramePointer or RegisterFrameBase.IncomingArgumentBase))
                    throw new InvalidOperationException("Frame operand has invalid base register.");
                if (operand.FrameSlotIndex < 0 || operand.FrameOffset < 0 || operand.FrameSlotSize <= 0)
                    throw new InvalidOperationException("Frame operand has invalid slot coordinates.");
                if (operand.FrameBase != RegisterFrameBase.IncomingArgumentBase)
                {
                    if (method.StackFrame.IsEmpty)
                        throw new InvalidOperationException("Node uses finalized frame operand but method has no finalized stack frame layout.");
                    if (operand.FrameOffset + operand.FrameSlotSize > method.StackFrame.FrameSize)
                        throw new InvalidOperationException($"Frame operand escapes finalized frame: {operand}.");
                }
            }
        }

        private static void VerifyOperandClass(RegisterAllocatedMethod method, RegisterOperand operand, GenTree value, string role)
        {
            var valueInfo = method.GenTreeMethod.GetValueInfo(value);
            var expected = valueInfo.RegisterClass;
            if (operand.RegisterClass == expected)
                return;

            var abi = MachineAbi.ClassifyValue(valueInfo.Type, valueInfo.StackKind, isReturn: false);
            if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
            {
                var segments = MachineAbi.GetRegisterSegments(abi);
                for (int i = 0; i < segments.Length; i++)
                {
                    if (segments[i].RegisterClass == operand.RegisterClass)
                        return;
                }
            }

            var returnAbi = MachineAbi.ClassifyValue(valueInfo.Type, valueInfo.StackKind, isReturn: true);
            if (returnAbi.PassingKind == AbiValuePassingKind.MultiRegister)
            {
                var segments = MachineAbi.GetRegisterSegments(returnAbi);
                for (int i = 0; i < segments.Length; i++)
                {
                    if (segments[i].RegisterClass == operand.RegisterClass)
                        return;
                }
            }

            throw new InvalidOperationException($"Node {role} operand {operand} has class {operand.RegisterClass} but GenTree value {value} requires {expected}.");
        }
    }

}
