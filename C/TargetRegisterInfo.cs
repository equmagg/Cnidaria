using System;
using System.Collections.Immutable;
using Cnidaria.Cs;

namespace Cnidaria.C
{
    internal static class TargetRegisterInfo
    {
        public static bool IsWindowsX64(TargetInfo target)
            => target.Architecture == TargetArchitectureKind.X64 && target.OperatingSystem == OperatingSystemKind.Windows;

        public static bool UsesUnifiedArgumentCursor(TargetInfo target)
            => IsWindowsX64(target);

        public static int MinimumOutgoingArgumentAreaSize(TargetInfo target, int stackSlotSize)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            return IsWindowsX64(target) ? checked(4 * Math.Max(1, stackSlotSize)) : 0;
        }

        public static int X86AbiFloatingRegisterSize(TargetInfo target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            if (!target.IsX86)
                return 0;

            return target.HasFeature(TargetArchitectureFeatures.X86Sse2) ? 8 : 0;
        }

        public static int ArmAbiFloatingRegisterSize(TargetInfo target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            if (target.Architecture == TargetArchitectureKind.Arm32)
                return 8;

            if (target.Architecture != TargetArchitectureKind.Arm32)
                return 0;

            return target.HasFeature(TargetArchitectureFeatures.ArmVfp) && target.HasFeature(TargetArchitectureFeatures.ArmHardFloat) ? 8 : 0;
        }

        public static int VectorRegisterSize(TargetInfo target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            if (target.IsX86)
                return target.HasFeature(TargetArchitectureFeatures.X86Avx) ? 32 : 16;

            if (target.IsRiscV && target.HasFeature(TargetArchitectureFeatures.RiscVV))
                return 16;

            if (target.Architecture == TargetArchitectureKind.Arm64 || (target.Architecture == TargetArchitectureKind.Arm32 && target.HasFeature(TargetArchitectureFeatures.ArmNeon)))
                return 16;

            return 0;
        }

        public static LirRegisterClass PreferredFloatingPointRegisterClass(TargetInfo target, QualifiedType type, bool isVariadicUnnamedArgument)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            if (!CAbi.IsFloating(type))
                return LirRegisterClass.General;

            if (target.IsRiscV)
            {
                if (isVariadicUnnamedArgument)
                    return LirRegisterClass.General;

                var abiFlen = CAbi.RiscVAbiFloatingRegisterSize(target);
                return abiFlen > 0 && Math.Max(1, target.SizeOf(type)) <= abiFlen
                    ? LirRegisterClass.Floating
                    : LirRegisterClass.General;
            }

            if (target.IsArm)
            {
                if (target.Architecture == TargetArchitectureKind.Arm32 && isVariadicUnnamedArgument)
                    return LirRegisterClass.General;

                var abiFlen = ArmAbiFloatingRegisterSize(target);
                return abiFlen > 0 && Math.Max(1, target.SizeOf(type)) <= abiFlen
                    ? LirRegisterClass.Floating
                    : LirRegisterClass.General;
            }

            if (target.IsX86)
            {
                var abiFlen = X86AbiFloatingRegisterSize(target);
                return abiFlen > 0 && Math.Max(1, target.SizeOf(type)) <= abiFlen
                    ? LirRegisterClass.Vector
                    : LirRegisterClass.General;
            }

            return LirRegisterClass.Floating;
        }

        public static ImmutableArray<MachineRegister> AllocatableGeneralRegisters(TargetInfo target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            return target.Architecture switch
            {
                TargetArchitectureKind.X86 => ImmutableArray.Create(
                    MachineRegister.X0,
                    MachineRegister.X1,
                    MachineRegister.X2,
                    MachineRegister.X3,
                    MachineRegister.X4,
                    MachineRegister.X5),

                TargetArchitectureKind.X64 => ImmutableArray.Create(
                    MachineRegister.X0,
                    MachineRegister.X1,
                    MachineRegister.X2,
                    MachineRegister.X3,
                    MachineRegister.X4,
                    MachineRegister.X5,
                    MachineRegister.X6,
                    MachineRegister.X7,
                    MachineRegister.X8,
                    MachineRegister.X9,
                    MachineRegister.X11,
                    MachineRegister.X12,
                    MachineRegister.X13,
                    MachineRegister.X14),

                TargetArchitectureKind.RiscV32 or TargetArchitectureKind.RiscV64 => ImmutableArray.Create(
                    MachineRegister.X18,
                    MachineRegister.X19,
                    MachineRegister.X20,
                    MachineRegister.X21,
                    MachineRegister.X22,
                    MachineRegister.X23,
                    MachineRegister.X24,
                    MachineRegister.X25,
                    MachineRegister.X26,
                    MachineRegister.X27),

                TargetArchitectureKind.Arm32 => Range(MachineRegister.X0, 13),

                TargetArchitectureKind.Arm64 => ImmutableArray.Create(
                    MachineRegister.X0,
                    MachineRegister.X1,
                    MachineRegister.X2,
                    MachineRegister.X3,
                    MachineRegister.X4,
                    MachineRegister.X5,
                    MachineRegister.X6,
                    MachineRegister.X7,
                    MachineRegister.X8,
                    MachineRegister.X9,
                    MachineRegister.X10,
                    MachineRegister.X11,
                    MachineRegister.X12,
                    MachineRegister.X13,
                    MachineRegister.X14,
                    MachineRegister.X15,
                    MachineRegister.X19,
                    MachineRegister.X20,
                    MachineRegister.X21,
                    MachineRegister.X22,
                    MachineRegister.X23,
                    MachineRegister.X24,
                    MachineRegister.X25,
                    MachineRegister.X26,
                    MachineRegister.X27,
                    MachineRegister.X28),

                _ => ImmutableArray.Create(
                    MachineRegister.X18,
                    MachineRegister.X19,
                    MachineRegister.X20,
                    MachineRegister.X21,
                    MachineRegister.X22,
                    MachineRegister.X23,
                    MachineRegister.X24,
                    MachineRegister.X25,
                    MachineRegister.X26,
                    MachineRegister.X27),
            };
        }

        public static ImmutableArray<MachineRegister> AllocatableFloatingRegisters(TargetInfo target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            if (target.IsX86)
                return ImmutableArray<MachineRegister>.Empty;

            if (target.IsRiscV)
            {
                return CAbi.RiscVAbiFloatingRegisterSize(target) == 0
                    ? ImmutableArray<MachineRegister>.Empty
                    : ImmutableArray.Create(
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
            }

            if (target.Architecture == TargetArchitectureKind.Arm32)
                return ArmAbiFloatingRegisterSize(target) == 0
                    ? ImmutableArray<MachineRegister>.Empty
                    : Range(MachineRegister.F0, target.HasFeature(TargetArchitectureFeatures.ArmVfpD32) || target.HasFeature(TargetArchitectureFeatures.ArmNeon) ? 32 : 16);

            if (target.Architecture == TargetArchitectureKind.Arm64)
                return Range(MachineRegister.F0, 32);

            return ImmutableArray.Create(
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
        }

        public static ImmutableArray<MachineRegister> AllocatableVectorRegisters(TargetInfo target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            if (target.Architecture == TargetArchitectureKind.X86)
                return X86AbiFloatingRegisterSize(target) == 0
                    ? ImmutableArray<MachineRegister>.Empty
                    : Range(MachineRegister.V0, 8);

            if (target.Architecture == TargetArchitectureKind.X64)
                return X86AbiFloatingRegisterSize(target) == 0
                    ? ImmutableArray<MachineRegister>.Empty
                    : Range(MachineRegister.V0, 16);

            if (target.IsRiscV && target.HasFeature(TargetArchitectureFeatures.RiscVV))
                return Range(MachineRegister.V0, 28);

            if (target.Architecture == TargetArchitectureKind.Arm64)
                return Range(MachineRegister.V0, 32);

            if (target.Architecture == TargetArchitectureKind.Arm32 && target.HasFeature(TargetArchitectureFeatures.ArmNeon))
                return Range(MachineRegister.V0, 16);

            return ImmutableArray<MachineRegister>.Empty;
        }

        public static ImmutableArray<MachineRegister> IntegerArgumentRegisters(TargetInfo target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            return target.Architecture switch
            {
                TargetArchitectureKind.X86 => ImmutableArray<MachineRegister>.Empty,
                TargetArchitectureKind.X64 => IsWindowsX64(target)
                    ? ImmutableArray.Create(MachineRegister.X1, MachineRegister.X2, MachineRegister.X3, MachineRegister.X4)
                    : ImmutableArray.Create(MachineRegister.X1, MachineRegister.X2, MachineRegister.X3, MachineRegister.X4, MachineRegister.X5, MachineRegister.X6),
                TargetArchitectureKind.Arm32 => ImmutableArray.Create(MachineRegister.X0, MachineRegister.X1, MachineRegister.X2, MachineRegister.X3),
                TargetArchitectureKind.Arm64 => ImmutableArray.Create(MachineRegister.X0, MachineRegister.X1, MachineRegister.X2, MachineRegister.X3, MachineRegister.X4, MachineRegister.X5, MachineRegister.X6, MachineRegister.X7),
                _ => ImmutableArray.Create(MachineRegister.X10, MachineRegister.X11, MachineRegister.X12, MachineRegister.X13, MachineRegister.X14, MachineRegister.X15, MachineRegister.X16, MachineRegister.X17),
            };
        }

        public static ImmutableArray<MachineRegister> FloatingArgumentRegisters(TargetInfo target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            if (target.IsX86)
                return ImmutableArray<MachineRegister>.Empty;

            if (target.Architecture == TargetArchitectureKind.Arm32)
                return ArmAbiFloatingRegisterSize(target) == 0
                    ? ImmutableArray<MachineRegister>.Empty
                    : Range(MachineRegister.F0, 16);

            if (target.Architecture == TargetArchitectureKind.Arm64)
                return Range(MachineRegister.F0, 8);

            return ImmutableArray.Create(
                MachineRegister.F10,
                MachineRegister.F11,
                MachineRegister.F12,
                MachineRegister.F13,
                MachineRegister.F14,
                MachineRegister.F15,
                MachineRegister.F16,
                MachineRegister.F17);
        }

        public static ImmutableArray<MachineRegister> VectorArgumentRegisters(TargetInfo target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            return target.Architecture switch
            {
                TargetArchitectureKind.X86 => ImmutableArray<MachineRegister>.Empty,
                TargetArchitectureKind.X64 => IsWindowsX64(target)
                    ? ImmutableArray.Create(MachineRegister.V0, MachineRegister.V1, MachineRegister.V2, MachineRegister.V3)
                    : ImmutableArray.Create(MachineRegister.V0, MachineRegister.V1, MachineRegister.V2, MachineRegister.V3, MachineRegister.V4, MachineRegister.V5, MachineRegister.V6, MachineRegister.V7),
                TargetArchitectureKind.Arm64 => ImmutableArray.Create(MachineRegister.V0, MachineRegister.V1, MachineRegister.V2, MachineRegister.V3, MachineRegister.V4, MachineRegister.V5, MachineRegister.V6, MachineRegister.V7),
                _ => ImmutableArray<MachineRegister>.Empty,
            };
        }

        public static ImmutableArray<MachineRegister> IntegerReturnRegisters(TargetInfo target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            return target.Architecture switch
            {
                TargetArchitectureKind.X86 => ImmutableArray.Create(MachineRegister.X0, MachineRegister.X2),
                TargetArchitectureKind.X64 => IsWindowsX64(target)
                    ? ImmutableArray.Create(MachineRegister.X0, MachineRegister.X2)
                    : ImmutableArray.Create(MachineRegister.X0, MachineRegister.X3),
                TargetArchitectureKind.Arm32 or TargetArchitectureKind.Arm64 => ImmutableArray.Create(MachineRegister.X0, MachineRegister.X1),
                _ => ImmutableArray.Create(MachineRegister.X10, MachineRegister.X11),
            };
        }

        public static ImmutableArray<MachineRegister> FloatingReturnRegisters(TargetInfo target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            if (target.IsX86)
                return ImmutableArray<MachineRegister>.Empty;

            if (target.Architecture == TargetArchitectureKind.Arm32)
                return ArmAbiFloatingRegisterSize(target) == 0
                    ? ImmutableArray<MachineRegister>.Empty
                    : ImmutableArray.Create(MachineRegister.F0, MachineRegister.F1);

            if (target.Architecture == TargetArchitectureKind.Arm64)
                return ImmutableArray.Create(MachineRegister.F0, MachineRegister.F1);

            return ImmutableArray.Create(MachineRegister.F10, MachineRegister.F11);
        }

        public static ImmutableArray<MachineRegister> VectorReturnRegisters(TargetInfo target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            if (target.IsX86)
                return ImmutableArray.Create(MachineRegister.V0, MachineRegister.V1);

            if (target.Architecture == TargetArchitectureKind.Arm64)
                return ImmutableArray.Create(MachineRegister.V0, MachineRegister.V1);

            return ImmutableArray<MachineRegister>.Empty;
        }

        public static bool IsCalleeSaved(TargetInfo target, MachineRegister register)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            if (register == MachineRegister.Invalid)
                return false;

            var registerClass = MachineRegisters.GetClass(register);
            if (target.Architecture == TargetArchitectureKind.X86)
            {
                if (registerClass == RegisterClass.Vector)
                    return false;

                return register is MachineRegister.X3 or MachineRegister.X4 or MachineRegister.X5;
            }

            if (target.Architecture == TargetArchitectureKind.X64)
            {
                if (registerClass == RegisterClass.Vector)
                    return IsWindowsX64(target) && register >= MachineRegister.V6 && register <= MachineRegister.V15;

                if (registerClass == RegisterClass.Float)
                    return false;

                return IsWindowsX64(target)
                    ? register is MachineRegister.X7 or MachineRegister.X8 or MachineRegister.X9 or MachineRegister.X11 or MachineRegister.X12 or MachineRegister.X13 or MachineRegister.X14
                    : register is MachineRegister.X9 or MachineRegister.X11 or MachineRegister.X12 or MachineRegister.X13 or MachineRegister.X14;
            }

            if (target.IsRiscV)
            {
                if (registerClass == RegisterClass.Vector)
                    return false;

                if (registerClass == RegisterClass.Float)
                    return register is MachineRegister.F8 or MachineRegister.F9 or MachineRegister.F18 or MachineRegister.F19 or MachineRegister.F20 or MachineRegister.F21 or MachineRegister.F22 or MachineRegister.F23 or MachineRegister.F24 or MachineRegister.F25 or MachineRegister.F26 or MachineRegister.F27;

                return register is MachineRegister.X8 or MachineRegister.X9 or MachineRegister.X18 or MachineRegister.X19 or MachineRegister.X20 or MachineRegister.X21 or MachineRegister.X22 or MachineRegister.X23 or MachineRegister.X24 or MachineRegister.X25 or MachineRegister.X26 or MachineRegister.X27;
            }

            if (target.Architecture == TargetArchitectureKind.Arm32)
            {
                if (registerClass == RegisterClass.Vector)
                    return register >= MachineRegister.V8 && register <= MachineRegister.V15;

                if (registerClass == RegisterClass.Float)
                    return register >= MachineRegister.F8 && register <= MachineRegister.F15;

                return register >= MachineRegister.X4 && register <= MachineRegister.X11;
            }

            if (target.Architecture == TargetArchitectureKind.Arm64)
            {
                if (registerClass == RegisterClass.Vector)
                    return register >= MachineRegister.V8 && register <= MachineRegister.V15;

                if (registerClass == RegisterClass.Float)
                    return register >= MachineRegister.F8 && register <= MachineRegister.F15;

                return register >= MachineRegister.X19 && register <= MachineRegister.X28;
            }

            return true;
        }

        public static int RegisterSaveSize(TargetInfo target, MachineRegister register, int defaultSpillSlotSize)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            var registerClass = MachineRegisters.GetClass(register);
            if (registerClass == RegisterClass.Vector)
                return Math.Max(1, Math.Max(VectorRegisterSize(target), defaultSpillSlotSize));

            if (registerClass == RegisterClass.Float)
            {
                if (target.IsRiscV)
                    return Math.Max(4, CAbi.RiscVAbiFloatingRegisterSize(target));

                if (target.IsArm)
                    return Math.Max(8, defaultSpillSlotSize);

                return Math.Max(1, defaultSpillSlotSize);
            }

            return Math.Max(1, target.RegisterSize);
        }

        private static ImmutableArray<MachineRegister> Range(MachineRegister first, int count)
        {
            if (count <= 0)
                return ImmutableArray<MachineRegister>.Empty;

            var builder = ImmutableArray.CreateBuilder<MachineRegister>(count);
            var start = (int)first;
            for (var i = 0; i < count; i++)
                builder.Add((MachineRegister)(start + i));
            return builder.MoveToImmutable();
        }
    }
}
