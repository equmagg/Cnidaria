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
            MachineRegister.X4,
            MachineRegister.X3,
            MachineRegister.X2,
            MachineRegister.X1,
            MachineRegister.X5,
            MachineRegister.X8,
            MachineRegister.X9,
            MachineRegister.X7,
            MachineRegister.X11,
            MachineRegister.X12,
            MachineRegister.X13,
            MachineRegister.X14);

        private static readonly ImmutableArray<MachineRegister> X64SystemVGeneralRegisters = ImmutableArray.Create(
            MachineRegister.X0,
            MachineRegister.X6,
            MachineRegister.X5,
            MachineRegister.X3,
            MachineRegister.X4,
            MachineRegister.X2,
            MachineRegister.X1,
            MachineRegister.X7,
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

        // Volatile first, then the argument registers in reverse, then the callee saved block
        private static readonly ImmutableArray<MachineRegister> Arm64GeneralRegisters = ImmutableArray.Create(
            MachineRegister.X9,
            MachineRegister.X10,
            MachineRegister.X11,
            MachineRegister.X12,
            MachineRegister.X13,
            MachineRegister.X14,
            MachineRegister.X15,
            MachineRegister.X8,
            MachineRegister.X7,
            MachineRegister.X6,
            MachineRegister.X5,
            MachineRegister.X4,
            MachineRegister.X3,
            MachineRegister.X2,
            MachineRegister.X1,
            MachineRegister.X0,
            MachineRegister.X19,
            MachineRegister.X20,
            MachineRegister.X21,
            MachineRegister.X22,
            MachineRegister.X23,
            MachineRegister.X24,
            MachineRegister.X25,
            MachineRegister.X26,
            MachineRegister.X27,
            MachineRegister.X28);

        private static readonly ImmutableArray<MachineRegister> Arm64FloatRegisters = ImmutableArray.Create(
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
            MachineRegister.F27,
            MachineRegister.F7,
            MachineRegister.F6,
            MachineRegister.F5,
            MachineRegister.F4,
            MachineRegister.F3,
            MachineRegister.F2,
            MachineRegister.F1,
            MachineRegister.F0,
            MachineRegister.F8,
            MachineRegister.F9,
            MachineRegister.F10,
            MachineRegister.F11,
            MachineRegister.F12,
            MachineRegister.F13,
            MachineRegister.F14,
            MachineRegister.F15);

        private static readonly ImmutableArray<MachineRegister> Arm64CallerSavedScalarRegisters = ImmutableArray.Create(
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
            MachineRegister.X16,
            MachineRegister.X17,
            MachineRegister.X30,
            MachineRegister.F0,
            MachineRegister.F1,
            MachineRegister.F2,
            MachineRegister.F3,
            MachineRegister.F4,
            MachineRegister.F5,
            MachineRegister.F6,
            MachineRegister.F7,
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
            MachineRegister.F27,
            MachineRegister.F28,
            MachineRegister.F29,
            MachineRegister.F30,
            MachineRegister.F31);

        private static readonly ImmutableArray<MachineRegister> Arm64TreeScratchGprs = ImmutableArray.Create(
            MachineRegister.X16,
            MachineRegister.X17);

        private static readonly ImmutableArray<MachineRegister> Arm64TreeScratchFprs = ImmutableArray.Create(
            MachineRegister.F30,
            MachineRegister.F31,
            MachineRegister.F29,
            MachineRegister.F28);

        public static void ValidateTarget(TargetInfo target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));
            if (target.Architecture == TargetArchitectureKind.Arm32)
                throw new NotSupportedException("The register allocator has no Arm32 register model.");
        }

        public static bool IsArm64(TargetInfo target)
        {
            ValidateTarget(target);
            return target.Architecture == TargetArchitectureKind.Arm64;
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
            if (IsArm64(target))
                return Arm64GeneralRegisters;
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
            if (IsArm64(target))
                return Arm64FloatRegisters;
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

            if (IsArm64(target))
                return Arm64CallerSavedScalarRegisters;

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

            if (IsArm64(target))
            {
                if ((int)register >= (int)MachineRegister.X19 && (int)register <= (int)MachineRegister.X29)
                    return true;
                return (int)register >= (int)MachineRegister.F8 && (int)register <= (int)MachineRegister.F15;
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
            {
                // Only the register that resolves parallel copies stays back. It stages the memory to
                // memory moves an edge can require, and those sit between nodes, where there is no node
                // to hang an internal register on and any allocatable register may already be live
                return register is MachineRegister.X10 or MachineRegister.X15 ||
                    register == ParallelCopyScratch(target, RegisterClass.General);
            }
            if (IsArm64(target))
            {
                // x18 belongs to the platform, x16 and x17 stage the moves that sit between nodes
                return register is
                    MachineRegister.X16 or MachineRegister.X17 or MachineRegister.X18 or
                    MachineRegister.X29 or MachineRegister.X30 or MachineRegister.X31 ||
                    ((int)register >= (int)MachineRegister.F28 && (int)register <= (int)MachineRegister.F31);
            }
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

            if (IsArm64(target))
            {
                return (uint)index < 8u
                    ? (MachineRegister)((int)MachineRegister.X0 + index)
                    : MachineRegister.Invalid;
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
            if (IsArm64(target))
            {
                return (uint)index < 8u
                    ? (MachineRegister)((int)MachineRegister.F0 + index)
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
            if (IsArm64(target))
            {
                return (uint)index < 2u
                    ? (MachineRegister)((int)MachineRegister.X0 + index)
                    : MachineRegister.Invalid;
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
            if (IsArm64(target))
            {
                return (uint)index < 4u
                    ? (MachineRegister)((int)MachineRegister.F0 + index)
                    : MachineRegister.Invalid;
            }

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
            if (IsArm64(target))
                return MachineRegister.X31;
            return MachineRegisters.StackPointer;
        }

        public static MachineRegister FramePointer(TargetInfo target)
        {
            ValidateTarget(target);
            if (target.Architecture == TargetArchitectureKind.I386)
                return MachineRegister.Ebp;
            if (target.Architecture == TargetArchitectureKind.X86_64)
                return MachineRegister.X10;
            if (IsArm64(target))
                return MachineRegister.X29;
            return MachineRegisters.FramePointer;
        }

        public static MachineRegister ReturnAddress(TargetInfo target)
        {
            ValidateTarget(target);
            if (target.IsX86)
                return MachineRegister.Invalid;
            return IsArm64(target) ? MachineRegister.X30 : MachineRegisters.ReturnAddress;
        }

        // Targets without one store an immediate instead of moving out of it
        public static MachineRegister ZeroRegister(TargetInfo target)
        {
            ValidateTarget(target);
            if (target.IsX86 || IsArm64(target))
                return MachineRegister.Invalid;
            return MachineRegisters.Zero;
        }

        // A dedicated register costs no argument slot, unlike passing the pointer as an argument
        public static MachineRegister ReturnBufferRegister(TargetInfo target)
        {
            ValidateTarget(target);
            return IsArm64(target) ? MachineRegister.X8 : MachineRegister.Invalid;
        }

        // The code generator still needs a couple of registers of its own for the sequences it expands
        // inline - address arithmetic, block moves, the runtime type checks. Rather than holding them
        // back from every method, the nodes that expand into those sequences report the pair as killed,
        // so the allocator keeps them free exactly where they are needed and uses them everywhere else
        public static ImmutableArray<MachineRegister> CodegenScratchRegisters(TargetInfo target)
        {
            ValidateTarget(target);
            if (target.Architecture == TargetArchitectureKind.X86_64)
            {
                return IsWindowsX64(target)
                    ? ImmutableArray.Create(MachineRegister.X6, MachineRegister.X5)
                    : ImmutableArray.Create(MachineRegister.X8, MachineRegister.X7);
            }
            return ImmutableArray<MachineRegister>.Empty;
        }

        public static ulong CodegenScratchMask(TargetInfo target)
            => MachineRegisters.MaskOf(CodegenScratchRegisters(target));

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
            if (IsArm64(target))
            {
                return registerClass switch
                {
                    RegisterClass.General => MachineRegister.X16,
                    RegisterClass.Float => MachineRegister.F30,
                    _ => MachineRegister.Invalid,
                };
            }
            return MachineRegisters.GetParallelCopyScratch(registerClass);
        }

        // Registers held back from the allocator for the sequences the code generator expands
        public static ImmutableArray<MachineRegister> TreeScratchRegisters(TargetInfo target, RegisterClass registerClass)
        {
            ValidateTarget(target);
            if (target.IsX86)
                return ImmutableArray<MachineRegister>.Empty;

            if (IsArm64(target))
            {
                return registerClass switch
                {
                    RegisterClass.General => Arm64TreeScratchGprs,
                    RegisterClass.Float => Arm64TreeScratchFprs,
                    _ => ImmutableArray<MachineRegister>.Empty,
                };
            }

            return registerClass switch
            {
                RegisterClass.General => MachineRegisters.TreeScratchGprs,
                RegisterClass.Float => MachineRegisters.TreeScratchFprs,
                RegisterClass.Vector => MachineRegisters.TreeScratchVprs,
                _ => ImmutableArray<MachineRegister>.Empty,
            };
        }

        public static bool IsScratchRegister(TargetInfo target, MachineRegister register)
        {
            ValidateTarget(target);
            if (register == MachineRegister.Invalid)
                return false;
            if (register == ParallelCopyScratch(target, RegisterClass.General) ||
                register == ParallelCopyScratch(target, RegisterClass.Float))
            {
                return !target.IsX86;
            }

            var pool = TreeScratchRegisters(target, MachineRegisters.GetClass(register));
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i] == register)
                    return true;
            }
            return false;
        }

        public static MachineRegister IndirectCallTargetRegister(TargetInfo target)
        {
            ValidateTarget(target);
            if (target.Architecture == TargetArchitectureKind.I386)
                return MachineRegister.Eax;
            if (target.Architecture == TargetArchitectureKind.X86_64)
                return IsWindowsX64(target) ? MachineRegister.X6 : MachineRegister.X8;
            return IsArm64(target) ? MachineRegister.X17 : MachineRegisters.TreeScratch3;
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
            if (target.IsArm)
                return registerClass is RegisterClass.General or RegisterClass.Float;
            return !target.IsRiscV || registerClass != RegisterClass.Float || AbiFloatingRegisterSize(target) != 0;
        }
    }
}
