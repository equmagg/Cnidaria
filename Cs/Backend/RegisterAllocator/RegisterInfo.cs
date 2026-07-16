using System;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal static class RegisterInfo
    {
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
            return target.IsRegisterBytecode
                ? MachineRegisters.RegisterBytecodeAllocatableGprs
                : MachineRegisters.DefaultAllocatableGprs;
        }

        public static ImmutableArray<MachineRegister> AllocatableFloatingRegisters(TargetInfo target)
        {
            ValidateTarget(target);
            if (target.IsRiscV && AbiFloatingRegisterSize(target) == 0)
                return ImmutableArray<MachineRegister>.Empty;
            return target.IsRegisterBytecode
                ? MachineRegisters.RegisterBytecodeAllocatableFprs
                : MachineRegisters.DefaultAllocatableFprs;
        }

        public static ImmutableArray<MachineRegister> CallerSavedScalarRegisters(TargetInfo target)
        {
            ValidateTarget(target);
            return target.IsRiscV && AbiFloatingRegisterSize(target) == 0
                ? MachineRegisters.CallerSavedGprs
                : MachineRegisters.CallerSavedScalarRegisters;
        }

        public static bool IsCalleeSaved(TargetInfo target, MachineRegister register)
        {
            ValidateTarget(target);
            return IsAvailable(target, register) && MachineRegisters.IsCalleeSaved(register);
        }

        public static bool IsCallerSaved(TargetInfo target, MachineRegister register)
        {
            ValidateTarget(target);
            return IsAvailable(target, register) && MachineRegisters.IsCallerSaved(register);
        }

        public static bool IsReserved(TargetInfo target, MachineRegister register)
        {
            ValidateTarget(target);
            return !IsAvailable(target, register) || MachineRegisters.IsReserved(register);
        }

        public static int RegisterSaveSize(TargetInfo target, MachineRegister register)
        {
            ValidateTarget(target);

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
            return target.IsRiscV
                ? RegisterSaveSize(target, register)
                : MachineRegisters.RegisterSaveAlignment(register);
        }

        public static MachineRegister GetIntegerArgumentRegister(TargetInfo target, int index)
        {
            ValidateTarget(target);
            return MachineRegisters.GetIntegerArgumentRegister(index);
        }

        public static MachineRegister GetFloatArgumentRegister(TargetInfo target, int index)
        {
            ValidateTarget(target);
            return target.IsRiscV && AbiFloatingRegisterSize(target) == 0
                ? MachineRegister.Invalid
                : MachineRegisters.GetFloatArgumentRegister(index);
        }

        public static MachineRegister GetIntegerReturnRegister(TargetInfo target, int index)
        {
            ValidateTarget(target);
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

        private static bool IsAvailable(TargetInfo target, MachineRegister register)
            => !target.IsRiscV ||
               MachineRegisters.GetClass(register) != RegisterClass.Float ||
               AbiFloatingRegisterSize(target) != 0;
    }
}
