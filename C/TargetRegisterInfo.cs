using System;
using System.Collections.Immutable;
using Cnidaria.Cs;

namespace Cnidaria.C
{
    internal static class TargetRegisterInfo
    {
        public static bool IsWindowsX64(TargetInfo target)
            => target.Architecture == TargetArchitectureKind.X64 && target.OperatingSystem == OperatingSystemKind.Windows;


        public static bool TryParseExplicitRegister(TargetInfo target, string? text, LirRegisterClass registerClass, out MachineRegister register)
        {
            register = MachineRegister.Invalid;
            if (target is null || string.IsNullOrWhiteSpace(text))
                return false;

            var name = NormalizeExplicitRegisterName(text);
            if (target.IsX86)
                return TryParseX86ExplicitRegister(target, name, registerClass, out register);
            if (target.IsRiscV)
                return TryParseRiscVExplicitRegister(name, registerClass, out register);
            return false;
        }

        private static string NormalizeExplicitRegisterName(string text)
        {
            text = text.Trim();
            if (text.Length >= 2 && text[0] == '{' && text[text.Length - 1] == '}')
                text = text.Substring(1, text.Length - 2).Trim();
            while (text.StartsWith("%", StringComparison.Ordinal))
                text = text.Substring(1);
            return text.ToLowerInvariant();
        }

        private static bool TryParseX86ExplicitRegister(TargetInfo target, string name, LirRegisterClass registerClass, out MachineRegister register)
        {
            register = MachineRegister.Invalid;
            if (name.StartsWith("xmm", StringComparison.Ordinal) || name.StartsWith("ymm", StringComparison.Ordinal))
            {
                if (!int.TryParse(name.Substring(3), out var vectorIndex) || vectorIndex < 0 || vectorIndex > 15)
                    return false;
                if (target.Architecture == TargetArchitectureKind.X86 && vectorIndex >= 8)
                    return false;
                if (registerClass is not LirRegisterClass.Vector and not LirRegisterClass.Floating)
                    return false;
                register = (MachineRegister)((int)MachineRegister.V0 + vectorIndex);
                return true;
            }

            var canonical = CanonicalX86GeneralRegisterName(name);
            if (canonical is null)
                return false;
            if (registerClass is not LirRegisterClass.General and not LirRegisterClass.Address)
                return false;

            return TryMapX86GeneralRegister(target, canonical, out register);
        }

        private static string? CanonicalX86GeneralRegisterName(string name)
        {
            return name switch
            {
                "al" or "ah" or "ax" or "eax" or "rax" => "rax",
                "cl" or "ch" or "cx" or "ecx" or "rcx" => "rcx",
                "dl" or "dh" or "dx" or "edx" or "rdx" => "rdx",
                "bl" or "bh" or "bx" or "ebx" or "rbx" => "rbx",
                "sil" or "si" or "esi" or "rsi" => "rsi",
                "dil" or "di" or "edi" or "rdi" => "rdi",
                "bpl" or "bp" or "ebp" or "rbp" => "rbp",
                "spl" or "sp" or "esp" or "rsp" => "rsp",
                "r8b" or "r8w" or "r8d" or "r8" => "r8",
                "r9b" or "r9w" or "r9d" or "r9" => "r9",
                "r10b" or "r10w" or "r10d" or "r10" => "r10",
                "r11b" or "r11w" or "r11d" or "r11" => "r11",
                "r12b" or "r12w" or "r12d" or "r12" => "r12",
                "r13b" or "r13w" or "r13d" or "r13" => "r13",
                "r14b" or "r14w" or "r14d" or "r14" => "r14",
                "r15b" or "r15w" or "r15d" or "r15" => "r15",
                _ => null,
            };
        }

        private static bool TryMapX86GeneralRegister(TargetInfo target, string canonical, out MachineRegister register)
        {
            register = MachineRegister.Invalid;
            if (target.Architecture == TargetArchitectureKind.X86)
            {
                register = canonical switch
                {
                    "rax" => MachineRegister.X0,
                    "rcx" => MachineRegister.X1,
                    "rdx" => MachineRegister.X2,
                    "rbx" => MachineRegister.X3,
                    "rsi" => MachineRegister.X4,
                    "rdi" => MachineRegister.X5,
                    _ => MachineRegister.Invalid,
                };
                return register != MachineRegister.Invalid;
            }

            if (IsWindowsX64(target))
            {
                register = canonical switch
                {
                    "rax" => MachineRegister.X0,
                    "rcx" => MachineRegister.X1,
                    "rdx" => MachineRegister.X2,
                    "r8" => MachineRegister.X3,
                    "r9" => MachineRegister.X4,
                    "r10" => MachineRegister.X5,
                    "r11" => MachineRegister.X6,
                    "rbx" => MachineRegister.X7,
                    "rsi" => MachineRegister.X8,
                    "rdi" => MachineRegister.X9,
                    "r12" => MachineRegister.X11,
                    "r13" => MachineRegister.X12,
                    "r14" => MachineRegister.X13,
                    "r15" => MachineRegister.X14,
                    _ => MachineRegister.Invalid,
                };
                return register != MachineRegister.Invalid;
            }

            register = canonical switch
            {
                "rax" => MachineRegister.X0,
                "rdi" => MachineRegister.X1,
                "rsi" => MachineRegister.X2,
                "rdx" => MachineRegister.X3,
                "rcx" => MachineRegister.X4,
                "r8" => MachineRegister.X5,
                "r9" => MachineRegister.X6,
                "r10" => MachineRegister.X7,
                "r11" => MachineRegister.X8,
                "rbx" => MachineRegister.X9,
                "r12" => MachineRegister.X11,
                "r13" => MachineRegister.X12,
                "r14" => MachineRegister.X13,
                "r15" => MachineRegister.X14,
                _ => MachineRegister.Invalid,
            };
            return register != MachineRegister.Invalid;
        }

        private static bool TryParseRiscVExplicitRegister(string name, LirRegisterClass registerClass, out MachineRegister register)
        {
            register = MachineRegister.Invalid;
            if (TryParseIndexedRegister(name, 'x', 0, out var index))
            {
                if (registerClass is not LirRegisterClass.General and not LirRegisterClass.Address)
                    return false;
                if (IsReservedRiscVExplicitIntegerRegister(index))
                    return false;
                register = (MachineRegister)((int)MachineRegister.X0 + index);
                return true;
            }
            if (TryParseIndexedRegister(name, 'f', 0, out index))
            {
                if (registerClass != LirRegisterClass.Floating)
                    return false;
                register = (MachineRegister)((int)MachineRegister.F0 + index);
                return true;
            }
            if (TryParseIndexedRegister(name, 'v', 0, out index))
            {
                if (registerClass != LirRegisterClass.Vector)
                    return false;
                register = (MachineRegister)((int)MachineRegister.V0 + index);
                return true;
            }

            if (TryParseRiscVIntegerAbiRegister(name, out index))
            {
                if (registerClass is not LirRegisterClass.General and not LirRegisterClass.Address)
                    return false;
                if (IsReservedRiscVExplicitIntegerRegister(index))
                    return false;
                register = (MachineRegister)((int)MachineRegister.X0 + index);
                return true;
            }
            if (TryParseRiscVFloatAbiRegister(name, out index))
            {
                if (registerClass != LirRegisterClass.Floating)
                    return false;
                register = (MachineRegister)((int)MachineRegister.F0 + index);
                return true;
            }
            return false;
        }

        private static bool TryParseIndexedRegister(string name, char prefix, int first, out int index)
        {
            index = -1;
            if (name.Length < 2 || name[0] != prefix)
                return false;
            if (!int.TryParse(name.Substring(1), out index))
                return false;
            return index >= first && index < 32;
        }

        private static bool IsReservedRiscVExplicitIntegerRegister(int index)
            => index is 0 or 1 or 2 or 3 or 4 or 8;

        private static bool TryParseRiscVIntegerAbiRegister(string name, out int index)
        {
            index = name switch
            {
                "zero" => 0,
                "ra" => 1,
                "sp" => 2,
                "gp" => 3,
                "tp" => 4,
                "t0" => 5,
                "t1" => 6,
                "t2" => 7,
                "s0" or "fp" => 8,
                "s1" => 9,
                "a0" => 10,
                "a1" => 11,
                "a2" => 12,
                "a3" => 13,
                "a4" => 14,
                "a5" => 15,
                "a6" => 16,
                "a7" => 17,
                "s2" => 18,
                "s3" => 19,
                "s4" => 20,
                "s5" => 21,
                "s6" => 22,
                "s7" => 23,
                "s8" => 24,
                "s9" => 25,
                "s10" => 26,
                "s11" => 27,
                "t3" => 28,
                "t4" => 29,
                "t5" => 30,
                "t6" => 31,
                _ => -1,
            };
            return index >= 0;
        }

        private static bool TryParseRiscVFloatAbiRegister(string name, out int index)
        {
            index = name switch
            {
                "ft0" => 0,
                "ft1" => 1,
                "ft2" => 2,
                "ft3" => 3,
                "ft4" => 4,
                "ft5" => 5,
                "ft6" => 6,
                "ft7" => 7,
                "fs0" => 8,
                "fs1" => 9,
                "fa0" => 10,
                "fa1" => 11,
                "fa2" => 12,
                "fa3" => 13,
                "fa4" => 14,
                "fa5" => 15,
                "fa6" => 16,
                "fa7" => 17,
                "fs2" => 18,
                "fs3" => 19,
                "fs4" => 20,
                "fs5" => 21,
                "fs6" => 22,
                "fs7" => 23,
                "fs8" => 24,
                "fs9" => 25,
                "fs10" => 26,
                "fs11" => 27,
                "ft8" => 28,
                "ft9" => 29,
                "ft10" => 30,
                "ft11" => 31,
                _ => -1,
            };
            return index >= 0;
        }

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
