using System;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal static class RegisterInfo
    {
        private static readonly ImmutableArray<MachineRegister> X86GeneralRegisters = ImmutableArray.Create(
            MachineRegister.Eax,
            MachineRegister.Edx,
            MachineRegister.Ecx,
            MachineRegister.Esi,
            MachineRegister.Edi,
            MachineRegister.Ebx);

        private static readonly ImmutableArray<MachineRegister> X86FloatRegisters = ImmutableArray.Create(
            MachineRegister.Xmm0,
            MachineRegister.Xmm1,
            MachineRegister.Xmm2,
            MachineRegister.Xmm3,
            MachineRegister.Xmm4,
            MachineRegister.Xmm5,
            MachineRegister.Xmm6,
            MachineRegister.Xmm7);

        private static readonly ImmutableArray<MachineRegister> X64WindowsGeneralRegisters = ImmutableArray.Create(
            MachineRegister.X0,
            MachineRegister.X5,
            MachineRegister.X6,
            MachineRegister.X4,
            MachineRegister.X3,
            MachineRegister.X2,
            MachineRegister.X1,
            MachineRegister.X8,
            MachineRegister.X9,
            MachineRegister.X7,
            MachineRegister.X11,
            MachineRegister.X12,
            MachineRegister.X13,
            MachineRegister.X14);

        private static readonly ImmutableArray<MachineRegister> X64SystemVGeneralRegisters = ImmutableArray.Create(
            MachineRegister.X0,
            MachineRegister.X7,
            MachineRegister.X8,
            MachineRegister.X6,
            MachineRegister.X5,
            MachineRegister.X3,
            MachineRegister.X4,
            MachineRegister.X2,
            MachineRegister.X1,
            MachineRegister.X9,
            MachineRegister.X11,
            MachineRegister.X12,
            MachineRegister.X13,
            MachineRegister.X14);

        private static readonly ImmutableArray<MachineRegister> X64FloatRegisters = ImmutableArray.Create(
            MachineRegister.Xmm0,
            MachineRegister.Xmm1,
            MachineRegister.Xmm2,
            MachineRegister.Xmm3,
            MachineRegister.Xmm4,
            MachineRegister.Xmm5,
            MachineRegister.Xmm6,
            MachineRegister.Xmm7,
            MachineRegister.Xmm8,
            MachineRegister.Xmm9,
            MachineRegister.Xmm10,
            MachineRegister.Xmm11,
            MachineRegister.Xmm12,
            MachineRegister.Xmm13,
            MachineRegister.Xmm14,
            MachineRegister.Xmm15);

        public static void ValidateTarget(TargetInfo target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));
        }

        public static int AbiFloatingRegisterSize(TargetInfo target)
        {
            ValidateTarget(target);
            return MachineAbi.RiscVAbiFloatingRegisterSize(target);
        }

        public static ImmutableArray<MachineRegister> AllocatableGeneralRegisters(TargetInfo target)
        {
            ValidateTarget(target);
            if (target.Architecture == TargetArchitectureKind.I386)
                return X86GeneralRegisters;
            if (target.Architecture == TargetArchitectureKind.X86_64)
                return IsWindowsX64(target) ? X64WindowsGeneralRegisters : X64SystemVGeneralRegisters;
            return target.IsRegisterBytecode
                ? MachineRegisters.RegisterBytecodeAllocatableGprs
                : MachineRegisters.DefaultAllocatableGprs;
        }

        public static ImmutableArray<MachineRegister> AllocatableFloatingRegisters(TargetInfo target)
        {
            ValidateTarget(target);
            if (target.Architecture == TargetArchitectureKind.I386)
                return X86FloatRegisters;
            if (target.Architecture == TargetArchitectureKind.X86_64)
                return X64FloatRegisters;
            if (target.IsRiscV && AbiFloatingRegisterSize(target) == 0)
                return ImmutableArray<MachineRegister>.Empty;
            return target.IsRegisterBytecode
                ? MachineRegisters.RegisterBytecodeAllocatableFprs
                : MachineRegisters.DefaultAllocatableFprs;
        }

        public static ImmutableArray<MachineRegister> CallerSavedScalarRegisters(TargetInfo target)
        {
            ValidateTarget(target);
            if (target.Architecture == TargetArchitectureKind.I386)
            {
                return ImmutableArray.Create(
                    MachineRegister.Eax,
                    MachineRegister.Ecx,
                    MachineRegister.Edx,
                    MachineRegister.Xmm0,
                    MachineRegister.Xmm1,
                    MachineRegister.Xmm2,
                    MachineRegister.Xmm3,
                    MachineRegister.Xmm4,
                    MachineRegister.Xmm5,
                    MachineRegister.Xmm6,
                    MachineRegister.Xmm7);
            }

            if (target.Architecture == TargetArchitectureKind.X86_64)
            {
                var builder = ImmutableArray.CreateBuilder<MachineRegister>();
                builder.Add(MachineRegister.X0);
                if (IsWindowsX64(target))
                {
                    builder.Add(MachineRegister.X1);
                    builder.Add(MachineRegister.X2);
                    builder.Add(MachineRegister.X3);
                    builder.Add(MachineRegister.X4);
                    builder.Add(MachineRegister.X5);
                    builder.Add(MachineRegister.X6);
                }
                else
                {
                    builder.Add(MachineRegister.X1);
                    builder.Add(MachineRegister.X2);
                    builder.Add(MachineRegister.X3);
                    builder.Add(MachineRegister.X4);
                    builder.Add(MachineRegister.X5);
                    builder.Add(MachineRegister.X6);
                    builder.Add(MachineRegister.X7);
                    builder.Add(MachineRegister.X8);
                }

                int xmmCount = IsWindowsX64(target) ? 6 : 16;
                for (int i = 0; i < xmmCount; i++)
                    builder.Add((MachineRegister)((int)MachineRegister.Xmm0 + i));
                return builder.ToImmutable();
            }

            return target.IsRiscV && AbiFloatingRegisterSize(target) == 0
                ? MachineRegisters.CallerSavedGprs
                : MachineRegisters.CallerSavedScalarRegisters;
        }

        public static bool IsCalleeSaved(TargetInfo target, MachineRegister register)
        {
            ValidateTarget(target);
            if (!IsAvailable(target, register))
                return false;

            if (target.Architecture == TargetArchitectureKind.I386)
                return register is MachineRegister.Ebx or MachineRegister.Esi or MachineRegister.Edi or MachineRegister.Ebp;

            if (target.Architecture == TargetArchitectureKind.X86_64)
            {
                if (IsWindowsX64(target))
                {
                    if (register is MachineRegister.X7 or MachineRegister.X8 or MachineRegister.X9 or MachineRegister.X10 or MachineRegister.X11 or MachineRegister.X12 or MachineRegister.X13 or MachineRegister.X14)
                        return true;
                    return (int)register >= (int)MachineRegister.Xmm6 && (int)register <= (int)MachineRegister.Xmm15;
                }

                return register is MachineRegister.X9 or MachineRegister.X10 or MachineRegister.X11 or MachineRegister.X12 or MachineRegister.X13 or MachineRegister.X14;
            }

            return MachineRegisters.IsCalleeSaved(register);
        }

        public static bool IsCallerSaved(TargetInfo target, MachineRegister register)
        {
            ValidateTarget(target);
            if (!IsAvailable(target, register) || IsReserved(target, register))
                return false;
            return !IsCalleeSaved(target, register);
        }

        public static bool IsReserved(TargetInfo target, MachineRegister register)
        {
            ValidateTarget(target);
            if (!IsAvailable(target, register))
                return true;
            if (target.Architecture == TargetArchitectureKind.I386)
                return register is MachineRegister.Esp or MachineRegister.Ebp;
            if (target.Architecture == TargetArchitectureKind.X86_64)
                return register is MachineRegister.X10 or MachineRegister.X15;
            return MachineRegisters.IsReserved(register);
        }

        public static int RegisterSaveSize(TargetInfo target, MachineRegister register)
        {
            ValidateTarget(target);
            if (target.IsX86)
                return MachineRegisters.GetClass(register) == RegisterClass.Float ? 16 : target.GeneralRegisterSize;
            if (!target.IsRiscV)
                return MachineRegisters.RegisterSaveSize(register);

            int floatingRegisterSize = AbiFloatingRegisterSize(target);
            return MachineRegisters.GetClass(register) switch
            {
                RegisterClass.Float => floatingRegisterSize > 0 ? floatingRegisterSize : target.FloatingRegisterSize,
                RegisterClass.Vector => TargetArchitecture.VectorRegisterSize,
                _ => target.GeneralRegisterSize,
            };
        }

        public static int RegisterSaveAlignment(TargetInfo target, MachineRegister register)
        {
            ValidateTarget(target);
            if (target.IsX86)
                return MachineRegisters.GetClass(register) == RegisterClass.Float ? 16 : target.GeneralRegisterSize;
            return target.IsRiscV
                ? RegisterSaveSize(target, register)
                : MachineRegisters.RegisterSaveAlignment(register);
        }

        public static MachineRegister GetIntegerArgumentRegister(TargetInfo target, int index)
        {
            ValidateTarget(target);
            if (target.Architecture == TargetArchitectureKind.I386)
            {
                return index switch
                {
                    0 => MachineRegister.Ecx,
                    1 => MachineRegister.Edx,
                    _ => MachineRegister.Invalid,
                };
            }

            if (target.Architecture == TargetArchitectureKind.X86_64)
            {
                if (IsWindowsX64(target))
                {
                    return index switch
                    {
                        0 => MachineRegister.X1,
                        1 => MachineRegister.X2,
                        2 => MachineRegister.X3,
                        3 => MachineRegister.X4,
                        _ => MachineRegister.Invalid,
                    };
                }

                return index switch
                {
                    0 => MachineRegister.X1,
                    1 => MachineRegister.X2,
                    2 => MachineRegister.X3,
                    3 => MachineRegister.X4,
                    4 => MachineRegister.X5,
                    5 => MachineRegister.X6,
                    _ => MachineRegister.Invalid,
                };
            }

            return MachineRegisters.GetIntegerArgumentRegister(index);
        }

        public static MachineRegister GetFloatArgumentRegister(TargetInfo target, int index)
        {
            ValidateTarget(target);
            if (target.Architecture == TargetArchitectureKind.I386)
                return MachineRegister.Invalid;
            if (target.Architecture == TargetArchitectureKind.X86_64)
            {
                int count = IsWindowsX64(target) ? 4 : 8;
                return (uint)index < (uint)count
                    ? (MachineRegister)((int)MachineRegister.Xmm0 + index)
                    : MachineRegister.Invalid;
            }
            return target.IsRiscV && AbiFloatingRegisterSize(target) == 0
                ? MachineRegister.Invalid
                : MachineRegisters.GetFloatArgumentRegister(index);
        }

        public static MachineRegister GetIntegerReturnRegister(TargetInfo target, int index)
        {
            ValidateTarget(target);
            if (target.Architecture == TargetArchitectureKind.I386)
            {
                return index switch
                {
                    0 => MachineRegister.Eax,
                    1 => MachineRegister.Edx,
                    _ => MachineRegister.Invalid,
                };
            }
            if (target.Architecture == TargetArchitectureKind.X86_64)
            {
                return index switch
                {
                    0 => MachineRegister.X0,
                    1 => IsWindowsX64(target) ? MachineRegister.X2 : MachineRegister.X3,
                    _ => MachineRegister.Invalid,
                };
            }

            return index switch
            {
                0 => MachineRegisters.ReturnValue0,
                1 => MachineRegisters.ReturnValue1,
                2 when !target.IsRiscV => MachineRegisters.ReturnValue2,
                3 when !target.IsRiscV => MachineRegisters.ReturnValue3,
                _ => MachineRegister.Invalid,
            };
        }

        public static MachineRegister GetFloatReturnRegister(TargetInfo target, int index)
        {
            ValidateTarget(target);
            if (target.IsX86)
                return index == 0 ? MachineRegister.Xmm0 : index == 1 && target.Architecture == TargetArchitectureKind.X86_64 ? MachineRegister.Xmm1 : MachineRegister.Invalid;
            if (target.IsRiscV && AbiFloatingRegisterSize(target) == 0)
                return MachineRegister.Invalid;

            return index switch
            {
                0 => MachineRegisters.FloatReturnValue0,
                1 => MachineRegisters.FloatReturnValue1,
                2 when !target.IsRiscV => MachineRegisters.FloatReturnValue2,
                3 when !target.IsRiscV => MachineRegisters.FloatReturnValue3,
                _ => MachineRegister.Invalid,
            };
        }

        public static MachineRegister AccumulatorRegister(TargetInfo target)
        {
            ValidateTarget(target);
            if (!target.IsX86)
                return MachineRegister.Invalid;
            return MachineRegister.X0;
        }

        public static MachineRegister CountRegister(TargetInfo target)
        {
            ValidateTarget(target);
            if (target.Architecture == TargetArchitectureKind.I386)
                return MachineRegister.Ecx;
            if (target.Architecture == TargetArchitectureKind.X86_64)
                return IsWindowsX64(target) ? MachineRegister.X1 : MachineRegister.X4;
            return MachineRegister.Invalid;
        }

        public static MachineRegister DataRegister(TargetInfo target)
        {
            ValidateTarget(target);
            if (target.Architecture == TargetArchitectureKind.I386)
                return MachineRegister.Edx;
            if (target.Architecture == TargetArchitectureKind.X86_64)
                return IsWindowsX64(target) ? MachineRegister.X2 : MachineRegister.X3;
            return MachineRegister.Invalid;
        }

        public static ulong ByteAddressableRegisterMask(TargetInfo target)
        {
            ValidateTarget(target);
            if (target.Architecture != TargetArchitectureKind.I386)
                return 0;
            return MachineRegisters.MaskOf(MachineRegister.X0) |
                   MachineRegisters.MaskOf(MachineRegister.X1) |
                   MachineRegisters.MaskOf(MachineRegister.X2) |
                   MachineRegisters.MaskOf(MachineRegister.X3);
        }

        public static MachineRegister StackPointer(TargetInfo target)
        {
            ValidateTarget(target);
            if (target.Architecture == TargetArchitectureKind.I386)
                return MachineRegister.Esp;
            if (target.Architecture == TargetArchitectureKind.X86_64)
                return MachineRegister.X15;
            return MachineRegisters.StackPointer;
        }

        public static MachineRegister FramePointer(TargetInfo target)
        {
            ValidateTarget(target);
            if (target.Architecture == TargetArchitectureKind.I386)
                return MachineRegister.Ebp;
            if (target.Architecture == TargetArchitectureKind.X86_64)
                return MachineRegister.X10;
            return MachineRegisters.FramePointer;
        }

        public static MachineRegister ReturnAddress(TargetInfo target)
        {
            ValidateTarget(target);
            return target.IsX86 ? MachineRegister.Invalid : MachineRegisters.ReturnAddress;
        }

        public static MachineRegister ParallelCopyScratch(TargetInfo target, RegisterClass registerClass)
        {
            ValidateTarget(target);
            if (target.Architecture == TargetArchitectureKind.I386)
            {
                return registerClass switch
                {
                    RegisterClass.General => MachineRegister.Eax,
                    RegisterClass.Float => MachineRegister.Xmm7,
                    _ => MachineRegister.Invalid,
                };
            }
            if (target.Architecture == TargetArchitectureKind.X86_64)
            {
                return registerClass switch
                {
                    RegisterClass.General => IsWindowsX64(target) ? MachineRegister.X6 : MachineRegister.X8,
                    RegisterClass.Float => IsWindowsX64(target) ? MachineRegister.Xmm5 : MachineRegister.Xmm15,
                    _ => MachineRegister.Invalid,
                };
            }
            return MachineRegisters.GetParallelCopyScratch(registerClass);
        }

        public static MachineRegister IndirectCallTargetRegister(TargetInfo target)
        {
            ValidateTarget(target);
            if (target.Architecture == TargetArchitectureKind.I386)
                return MachineRegister.Eax;
            if (target.Architecture == TargetArchitectureKind.X86_64)
                return IsWindowsX64(target) ? MachineRegister.X6 : MachineRegister.X8;
            return MachineRegisters.TreeScratch3;
        }

        public static int MinimumOutgoingArgumentSlots(TargetInfo target)
        {
            ValidateTarget(target);
            return IsWindowsX64(target) ? 4 : 0;
        }

        public static bool UsesSharedArgumentRegisterSlots(TargetInfo target)
        {
            ValidateTarget(target);
            return IsWindowsX64(target);
        }

        public static bool IsWindowsX64(TargetInfo target)
        {
            ValidateTarget(target);
            return target.Architecture == TargetArchitectureKind.X86_64 && target.OperatingSystem == OperatingSystemKind.Windows;
        }

        private static bool IsAvailable(TargetInfo target, MachineRegister register)
        {
            var registerClass = MachineRegisters.GetClass(register);
            if (registerClass == RegisterClass.Invalid)
                return false;
            if (target.Architecture == TargetArchitectureKind.I386)
                return registerClass == RegisterClass.General
                    ? (int)register <= (int)MachineRegister.Ebp
                    : registerClass == RegisterClass.Float && (int)register <= (int)MachineRegister.Xmm7;
            if (target.Architecture == TargetArchitectureKind.X86_64)
                return registerClass == RegisterClass.General
                    ? (int)register <= (int)MachineRegister.X15
                    : registerClass == RegisterClass.Float && (int)register <= (int)MachineRegister.Xmm15;
            return !target.IsRiscV || registerClass != RegisterClass.Float || AbiFloatingRegisterSize(target) != 0;
        }
    }
}
