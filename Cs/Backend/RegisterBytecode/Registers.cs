using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;

namespace Cnidaria.Cs
{
    public enum MachineRegister : byte
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
}
