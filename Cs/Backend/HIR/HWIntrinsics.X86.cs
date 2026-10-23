using Cnidaria.X86;
using System;
using System.Collections.Generic;

namespace Cnidaria.Cs
{
    internal enum X86Isa : byte
    {
        X86Base,
        Sse,
        Sse2,
        Sse3,
        Ssse3,
        Sse41,
        Sse42,
        Lzcnt,
        Popcnt,
        Avx,
        Avx2,
        Bmi1,
        Bmi2,
        Fma,
        Aes,
        Pclmulqdq,
        Gfni,
        AvxVnni,
        AvxVnniInt8,
        AvxVnniInt16,
        X86Serialize,
        Avx512F,
        Avx512BW,
        Avx512CD,
        Avx512DQ,
        Avx512Vbmi,
        Avx512Vbmi2,
        Avx10v1,
        Avx10v2,
    }

    internal enum X86IsaVariant : byte
    {
        None,
        X64,
        VL,
        V256,
        V512,
    }

    internal enum X86IntrinsicForm : byte
    {
        Simple,
        Scalar,
        Shift,
        Compare,
        CompareFlags,
        Store,
        MaskMove,
        BlendVariable,
        Crc32,
        DivRem,
        Barrier,
        Prefetch,
        Vex,
        VexStore,
        Tied,
        Gather,
        GatherMask,
        BitFieldExtract,
        MultiplyNoFlags,
        MaskCompare,
        MaskedBlend,
        MaskedMerge,
        MaskedLoad,
        MaskedStore,
        MaskToBits,
    }

    [Flags]
    internal enum X86IntrinsicFlags : byte
    {
        None = 0,
        Commutative = 1 << 0,
        IntegerCommutative = 1 << 1,
        BaseFromFirstArg = 1 << 2,
        BaseFromSecondArg = 1 << 3,
        SwapOperands = 1 << 4,
        TableImmediate = 1 << 5,
    }

    internal sealed class X86IntrinsicInfo
    {
        public X86IntrinsicInfo(X86Isa isa, bool x64, string name, X86IntrinsicForm form, X86IntrinsicFlags flags, byte immediate, byte immediateBound, X86InstrKind[] instructions)
        {
            Isa = isa;
            X64 = x64;
            Name = name;
            Form = form;
            Flags = flags;
            Immediate = immediate;
            ImmediateBound = immediateBound;
            Instructions = instructions;
        }

        public X86Isa Isa { get; }
        public bool X64 { get; }
        public string Name { get; }
        public X86IntrinsicForm Form { get; }
        public X86IntrinsicFlags Flags { get; }
        public byte Immediate { get; }
        public byte ImmediateBound { get; }
        public X86InstrKind[] Instructions { get; }

        public bool Has(X86IntrinsicFlags flag) => (Flags & flag) != 0;

        public X86InstrKind Instruction(RuntimePrimitiveKind kind)
        {
            int index = X86Intrinsics.KindIndex(kind);
            X86InstrKind specific = index >= 0 ? Instructions[index] : X86InstrKind.Invalid;
            return specific != X86InstrKind.Invalid ? specific : Instructions[X86Intrinsics.AnyKindIndex];
        }

        public override string ToString() => (X64 ? Isa + ".X64." : Isa + ".") + Name;
    }

    // A node's Int32 is FirstId plus its row; Int64 adds the mask of address operands, from bit 16, to the element kind and width
    internal static class X86Intrinsics
    {
        public const int FirstId = 0x400;
        public const int AnyKindIndex = 10;
        public const string Namespace = "System.Runtime.Intrinsics.X86";
        private const long ImmediateBit = 1L << 24;
        private const long RoundingBit = 1L << 25;

        private static readonly List<X86IntrinsicInfo> Rows = new List<X86IntrinsicInfo>();
        private static readonly Dictionary<(X86Isa, bool, string), int> Lookup = new Dictionary<(X86Isa, bool, string), int>();

        static X86Intrinsics() => Build();

        public static bool IsX86(GenTree node) => node.Int32 >= FirstId;

        public static X86IntrinsicInfo Info(GenTree node) => Rows[node.Int32 - FirstId];

        public static X86IntrinsicInfo Info(int id) => Rows[id - FirstId];

        public static long Encode(RuntimePrimitiveKind baseKind, int simdSize, int addressMask, bool immediate = false, bool rounding = false)
            => HWIntrinsics.Encode(baseKind, simdSize) | ((long)addressMask << 16) | (immediate ? ImmediateBit : 0) | (rounding ? RoundingBit : 0);

        private static int AddressMask(GenTree node) => (int)((node.Int64 >> 16) & 0xFF);

        public static bool IsAddressOperand(GenTree node, int operandIndex) => (AddressMask(node) & (1 << operandIndex)) != 0;

        public static int RoundingOperandIndex(GenTree node) => (node.Int64 & RoundingBit) != 0 ? node.Operands.Length - 1 : -1;

        public static bool IsVex(X86IntrinsicInfo info) => info.Form >= X86IntrinsicForm.Vex;

        public static int KindIndex(RuntimePrimitiveKind kind)
            => kind switch
            {
                RuntimePrimitiveKind.Int8 => 0,
                RuntimePrimitiveKind.UInt8 => 1,
                RuntimePrimitiveKind.Int16 => 2,
                RuntimePrimitiveKind.UInt16 => 3,
                RuntimePrimitiveKind.Int32 => 4,
                RuntimePrimitiveKind.UInt32 => 5,
                RuntimePrimitiveKind.Int64 => 6,
                RuntimePrimitiveKind.UInt64 => 7,
                RuntimePrimitiveKind.Single => 8,
                RuntimePrimitiveKind.Double => 9,
                _ => -1,
            };

        public static bool TryGetIsa(RuntimeType type, out X86Isa isa, out X86IsaVariant variant)
        {
            isa = default;
            variant = X86IsaVariant.None;
            if (type.Namespace != Namespace)
                return false;
            string name = type.Name;
            int nested = name.IndexOf('+');
            if (nested >= 0)
            {
                if (!Enum.TryParse(name.Substring(nested + 1), ignoreCase: false, out variant) || !Enum.IsDefined(variant) || variant == X86IsaVariant.None)
                    return false;
                name = name.Substring(0, nested);
            }
            return Enum.TryParse(name, ignoreCase: false, out isa) && Enum.IsDefined(isa);
        }

        public static bool IsSupported(TargetInfo target, X86Isa isa, X86IsaVariant variant)
        {
            if (target.Architecture != TargetArchitectureKind.X86_64)
                return false;
            bool Has(TargetArchitectureFeatures feature) => HWIntrinsics.HasFeature(target, feature);
            bool wide = variant switch
            {
                X86IsaVariant.V256 => Has(TargetArchitectureFeatures.X86Avx),
                X86IsaVariant.V512 => Has(TargetArchitectureFeatures.X86Avx512),
                _ => true,
            };
            return wide && isa switch
            {
                X86Isa.X86Base or X86Isa.Sse or X86Isa.Sse2 => true,
                X86Isa.Sse3 => Has(TargetArchitectureFeatures.X86Sse3),
                X86Isa.Ssse3 => Has(TargetArchitectureFeatures.X86Ssse3),
                X86Isa.Sse41 => Has(TargetArchitectureFeatures.X86Sse41),
                X86Isa.Sse42 or X86Isa.Popcnt => Has(TargetArchitectureFeatures.X86Sse42),
                X86Isa.Lzcnt => Has(TargetArchitectureFeatures.X86Lzcnt),
                X86Isa.Avx => Has(TargetArchitectureFeatures.X86Avx) && SimdTypes.TargetSupports(target, 32),
                X86Isa.Avx2 or X86Isa.Bmi1 or X86Isa.Bmi2 or X86Isa.Fma => Has(TargetArchitectureFeatures.X86Avx2),
                X86Isa.Aes => Has(TargetArchitectureFeatures.X86Aes),
                X86Isa.Pclmulqdq => Has(TargetArchitectureFeatures.X86Aes) &&
                    (variant is not (X86IsaVariant.V256 or X86IsaVariant.V512) || Has(TargetArchitectureFeatures.X86Vpclmulqdq)),
                X86Isa.Gfni => Has(TargetArchitectureFeatures.X86Gfni),
                X86Isa.AvxVnni => Has(TargetArchitectureFeatures.X86AvxVnni | TargetArchitectureFeatures.X86Avx2),
                X86Isa.AvxVnniInt8 or X86Isa.AvxVnniInt16 => Has(TargetArchitectureFeatures.X86AvxVnniInt | TargetArchitectureFeatures.X86Avx2) &&
                    (variant != X86IsaVariant.V512 || Has(TargetArchitectureFeatures.X86Avx10v2)),
                X86Isa.X86Serialize => Has(TargetArchitectureFeatures.X86Serialize),
                X86Isa.Avx512F or X86Isa.Avx512BW or X86Isa.Avx512CD or X86Isa.Avx512DQ => Has(TargetArchitectureFeatures.X86Avx512),
                X86Isa.Avx512Vbmi => Has(TargetArchitectureFeatures.X86Avx512 | TargetArchitectureFeatures.X86Avx512Vbmi),
                X86Isa.Avx512Vbmi2 => Has(TargetArchitectureFeatures.X86Avx512 | TargetArchitectureFeatures.X86Avx512Vbmi2),
                X86Isa.Avx10v1 => Has(TargetArchitectureFeatures.X86Avx10v1),
                X86Isa.Avx10v2 => Has(TargetArchitectureFeatures.X86Avx10v1 | TargetArchitectureFeatures.X86Avx10v2),
                _ => false,
            };
        }

        private static X86Isa RowIsa(X86Isa isa)
            => isa switch
            {
                X86Isa.Avx512BW or X86Isa.Avx512CD or X86Isa.Avx512DQ or X86Isa.Avx512Vbmi or X86Isa.Avx512Vbmi2 or X86Isa.Avx10v1 => X86Isa.Avx512F,
                X86Isa.AvxVnniInt16 => X86Isa.AvxVnniInt8,
                _ => isa,
            };

        public static bool TryLookup(X86Isa isa, bool x64, string name, out int id)
        {
            if (Lookup.TryGetValue((RowIsa(isa), x64, name), out int row))
            {
                id = FirstId + row;
                return true;
            }
            id = 0;
            return false;
        }

        public static bool TouchesMemory(GenTree node)
        {
            var info = Info(node);
            return info.Form is X86IntrinsicForm.Store or X86IntrinsicForm.VexStore or X86IntrinsicForm.MaskedStore or X86IntrinsicForm.MaskMove or
                X86IntrinsicForm.Barrier or X86IntrinsicForm.Prefetch || AddressMask(node) != 0;
        }

        public static GenTreeFlags EffectFlags(GenTree node)
        {
            var info = Info(node);
            return info.Form switch
            {
                X86IntrinsicForm.Store or X86IntrinsicForm.VexStore or X86IntrinsicForm.MaskedStore or X86IntrinsicForm.MaskMove =>
                    GenTreeFlags.SideEffect | GenTreeFlags.MemoryWrite | GenTreeFlags.Indirect | GenTreeFlags.CanThrow | GenTreeFlags.Ordered,
                X86IntrinsicForm.MultiplyNoFlags when AddressMask(node) != 0 =>
                    GenTreeFlags.SideEffect | GenTreeFlags.MemoryWrite | GenTreeFlags.Indirect | GenTreeFlags.CanThrow | GenTreeFlags.Ordered,
                X86IntrinsicForm.Barrier =>
                    GenTreeFlags.SideEffect | GenTreeFlags.MemoryRead | GenTreeFlags.MemoryWrite | GenTreeFlags.GlobalRef | GenTreeFlags.Ordered,
                X86IntrinsicForm.Prefetch => GenTreeFlags.SideEffect | GenTreeFlags.Ordered,
                X86IntrinsicForm.DivRem => GenTreeFlags.CanThrow,
                _ when AddressMask(node) != 0 => GenTreeFlags.MemoryRead | GenTreeFlags.Indirect | GenTreeFlags.CanThrow,
                _ => GenTreeFlags.None,
            };
        }

        // The operand an imm8 comes from, or -1 when the instruction takes none or the row fixes it
        public static int ImmediateOperandIndex(GenTree node)
        {
            var info = Info(node);
            if (info.Has(X86IntrinsicFlags.TableImmediate))
                return -1;
            if (IsVex(info))
                return (node.Int64 & ImmediateBit) != 0 ? node.Operands.Length - 1 : -1;
            switch (info.Form)
            {
                case X86IntrinsicForm.Simple:
                    return X86InstructionTable.Get(info.Instruction(HWIntrinsics.BaseKind(node))).Format == X86InstructionFormat.Ternary
                        ? node.Operands.Length - 1
                        : -1;
                case X86IntrinsicForm.Shift:
                    return SimdTypes.IsSimdKind(node.Operands[1].StackKind) ? -1 : 1;
                default:
                    return -1;
            }
        }

        public static bool IsCommutative(GenTree node)
        {
            var info = Info(node);
            return info.Has(X86IntrinsicFlags.Commutative) ||
                info.Has(X86IntrinsicFlags.IntegerCommutative) && !HWIntrinsics.IsFloating(HWIntrinsics.BaseKind(node));
        }

        public static bool IsCompareSwap(GenTree node, out bool scalar)
        {
            var info = Info(node);
            X86InstrKind instruction = info.Instruction(HWIntrinsics.BaseKind(node));
            scalar = instruction is X86InstrKind.Cmpss or X86InstrKind.Cmpsd;
            if (instruction is X86InstrKind.Cmpps or X86InstrKind.Cmppd or X86InstrKind.Cmpss or X86InstrKind.Cmpsd)
                return info.Has(X86IntrinsicFlags.SwapOperands);
            return info.Immediate == ComparePredicate.LessThan && !info.Has(X86IntrinsicFlags.SwapOperands);
        }

        public static bool CompareFlagsNeedParity(GenTree node)
        {
            var info = Info(node);
            return info.Instruction(HWIntrinsics.BaseKind(node)) is X86InstrKind.Comiss or X86InstrKind.Ucomiss or X86InstrKind.Comisd or X86InstrKind.Ucomisd &&
                (X86Condition)info.Immediate is X86Condition.E or X86Condition.Ne;
        }

        public static bool IsDivRem(GenTree node, out bool remainder)
        {
            var info = Info(node);
            remainder = info.Name.EndsWith(".Remainder", StringComparison.Ordinal);
            return info.Form == X86IntrinsicForm.DivRem;
        }

        public static string Format(GenTree node) => $"{Info(node)}<{HWIntrinsics.BaseKind(node)}>";

        private static class ComparePredicate
        {
            public const byte Equal = 0;
            public const byte LessThan = 1;
            public const byte LessThanOrEqual = 2;
            public const byte Unordered = 3;
            public const byte NotEqual = 4;
            public const byte NotLessThan = 5;
            public const byte NotLessThanOrEqual = 6;
            public const byte Ordered = 7;
        }

        private static void Row(
            X86Isa isa,
            string name,
            X86IntrinsicForm form,
            X86InstrKind any = X86InstrKind.Invalid,
            X86InstrKind i8 = X86InstrKind.Invalid,
            X86InstrKind i16 = X86InstrKind.Invalid,
            X86InstrKind i32 = X86InstrKind.Invalid,
            X86InstrKind i64 = X86InstrKind.Invalid,
            X86InstrKind s8 = X86InstrKind.Invalid,
            X86InstrKind u8 = X86InstrKind.Invalid,
            X86InstrKind s16 = X86InstrKind.Invalid,
            X86InstrKind u16 = X86InstrKind.Invalid,
            X86InstrKind s32 = X86InstrKind.Invalid,
            X86InstrKind u32 = X86InstrKind.Invalid,
            X86InstrKind s64 = X86InstrKind.Invalid,
            X86InstrKind u64 = X86InstrKind.Invalid,
            X86InstrKind f32 = X86InstrKind.Invalid,
            X86InstrKind f64 = X86InstrKind.Invalid,
            X86IntrinsicFlags flags = X86IntrinsicFlags.None,
            int imm = 0,
            bool x64 = false,
            int bound = 0xFF)
        {
            static X86InstrKind Pick(X86InstrKind specific, X86InstrKind both) => specific != X86InstrKind.Invalid ? specific : both;
            var instructions = new[]
            {
                Pick(s8, i8), Pick(u8, i8), Pick(s16, i16), Pick(u16, i16), Pick(s32, i32), Pick(u32, i32),
                Pick(s64, i64), Pick(u64, i64), f32, f64, any,
            };
            Lookup.Add((isa, x64, name), Rows.Count);
            Rows.Add(new X86IntrinsicInfo(isa, x64, name, form, flags, (byte)imm, (byte)bound, instructions));
        }

        private static void Ints(X86Isa isa, string name, X86IntrinsicForm form, X86InstrKind instruction, X86IntrinsicFlags flags = X86IntrinsicFlags.None, int imm = 0)
            => Row(isa, name, form, i8: instruction, i16: instruction, i32: instruction, i64: instruction, flags: flags, imm: imm);

        private static void PackedCompares(X86Isa isa, X86InstrKind packed, X86InstrKind scalar, X86InstrKind ordered, X86InstrKind unordered, bool singlePrecision)
        {
            const X86IntrinsicForm C = X86IntrinsicForm.Compare;
            const X86IntrinsicFlags Comm = X86IntrinsicFlags.Commutative, Swap = X86IntrinsicFlags.SwapOperands;
            void F(string name, X86InstrKind instruction, X86IntrinsicFlags flags, int imm)
                => Row(isa, name, C, f32: singlePrecision ? instruction : X86InstrKind.Invalid, f64: singlePrecision ? X86InstrKind.Invalid : instruction,
                    flags: flags | X86IntrinsicFlags.TableImmediate, imm: imm);

            F("CompareGreaterThanOrEqual", packed, Swap, ComparePredicate.LessThanOrEqual);
            F("CompareLessThanOrEqual", packed, 0, ComparePredicate.LessThanOrEqual);
            F("CompareNotEqual", packed, Comm, ComparePredicate.NotEqual);
            F("CompareNotGreaterThan", packed, Swap, ComparePredicate.NotLessThan);
            F("CompareNotGreaterThanOrEqual", packed, Swap, ComparePredicate.NotLessThanOrEqual);
            F("CompareNotLessThan", packed, 0, ComparePredicate.NotLessThan);
            F("CompareNotLessThanOrEqual", packed, 0, ComparePredicate.NotLessThanOrEqual);
            F("CompareOrdered", packed, Comm, ComparePredicate.Ordered);
            F("CompareUnordered", packed, Comm, ComparePredicate.Unordered);
            if (singlePrecision)
            {
                F("CompareEqual", packed, Comm, ComparePredicate.Equal);
                F("CompareGreaterThan", packed, Swap, ComparePredicate.LessThan);
                F("CompareLessThan", packed, 0, ComparePredicate.LessThan);
            }

            F("CompareScalarEqual", scalar, 0, ComparePredicate.Equal);
            F("CompareScalarGreaterThan", scalar, Swap, ComparePredicate.LessThan);
            F("CompareScalarGreaterThanOrEqual", scalar, Swap, ComparePredicate.LessThanOrEqual);
            F("CompareScalarLessThan", scalar, 0, ComparePredicate.LessThan);
            F("CompareScalarLessThanOrEqual", scalar, 0, ComparePredicate.LessThanOrEqual);
            F("CompareScalarNotEqual", scalar, 0, ComparePredicate.NotEqual);
            F("CompareScalarNotGreaterThan", scalar, Swap, ComparePredicate.NotLessThan);
            F("CompareScalarNotGreaterThanOrEqual", scalar, Swap, ComparePredicate.NotLessThanOrEqual);
            F("CompareScalarNotLessThan", scalar, 0, ComparePredicate.NotLessThan);
            F("CompareScalarNotLessThanOrEqual", scalar, 0, ComparePredicate.NotLessThanOrEqual);
            F("CompareScalarOrdered", scalar, 0, ComparePredicate.Ordered);
            F("CompareScalarUnordered", scalar, 0, ComparePredicate.Unordered);

            void B(string name, X86InstrKind instruction, X86Condition condition, X86IntrinsicFlags flags)
                => Row(isa, name, X86IntrinsicForm.CompareFlags, f32: singlePrecision ? instruction : X86InstrKind.Invalid,
                    f64: singlePrecision ? X86InstrKind.Invalid : instruction, flags: flags | X86IntrinsicFlags.TableImmediate, imm: (int)condition);
            foreach (var (prefix, instruction) in new[] { ("CompareScalarOrdered", ordered), ("CompareScalarUnordered", unordered) })
            {
                B(prefix + "Equal", instruction, X86Condition.E, 0);
                B(prefix + "NotEqual", instruction, X86Condition.Ne, 0);
                B(prefix + "GreaterThan", instruction, X86Condition.A, 0);
                B(prefix + "GreaterThanOrEqual", instruction, X86Condition.Ae, 0);
                B(prefix + "LessThan", instruction, X86Condition.A, Swap);
                B(prefix + "LessThanOrEqual", instruction, X86Condition.Ae, Swap);
            }
        }

        private static void Build()
        {
            const X86IntrinsicForm S = X86IntrinsicForm.Simple, Sc = X86IntrinsicForm.Scalar, St = X86IntrinsicForm.Store;
            const X86IntrinsicFlags Comm = X86IntrinsicFlags.Commutative, IntComm = X86IntrinsicFlags.IntegerCommutative;
            const X86IntrinsicFlags First = X86IntrinsicFlags.BaseFromFirstArg, Second = X86IntrinsicFlags.BaseFromSecondArg;
            const X86IntrinsicFlags Swap = X86IntrinsicFlags.SwapOperands, Fixed = X86IntrinsicFlags.TableImmediate;
            const X86Isa B = X86Isa.X86Base, Sse = X86Isa.Sse, Sse2 = X86Isa.Sse2, Sse3 = X86Isa.Sse3;
            const X86Isa Ssse3 = X86Isa.Ssse3, Sse41 = X86Isa.Sse41, Sse42 = X86Isa.Sse42;

            Row(B, "BitScanForward", S, i32: X86InstrKind.Bsf);
            Row(B, "BitScanReverse", S, i32: X86InstrKind.Bsr);
            Row(B, "BitScanForward", S, i64: X86InstrKind.Bsf, x64: true);
            Row(B, "BitScanReverse", S, i64: X86InstrKind.Bsr, x64: true);
            Row(B, "Pause", X86IntrinsicForm.Barrier, any: X86InstrKind.Pause);
            foreach (bool x64 in new[] { false, true })
            {
                // The remainder row follows the quotient row, which is the one the method name finds
                Row(B, "DivRem", X86IntrinsicForm.DivRem, s32: X86InstrKind.Idiv, u32: X86InstrKind.Div, s64: X86InstrKind.Idiv, u64: X86InstrKind.Div, flags: Second, x64: x64);
                Row(B, "DivRem.Remainder", X86IntrinsicForm.DivRem, s32: X86InstrKind.Idiv, u32: X86InstrKind.Div, s64: X86InstrKind.Idiv, u64: X86InstrKind.Div, flags: Second, x64: x64);
            }

            Row(Sse, "Add", S, f32: X86InstrKind.Addps, flags: Comm);
            Row(Sse, "AddScalar", S, f32: X86InstrKind.Addss);
            Row(Sse, "And", S, f32: X86InstrKind.Andps, flags: Comm);
            Row(Sse, "AndNot", S, f32: X86InstrKind.Andnps);
            PackedCompares(Sse, X86InstrKind.Cmpps, X86InstrKind.Cmpss, X86InstrKind.Comiss, X86InstrKind.Ucomiss, singlePrecision: true);
            Row(Sse, "ConvertScalarToVector128Single", S, s32: X86InstrKind.Cvtsi2ss, flags: Second);
            Row(Sse, "ConvertScalarToVector128Single", S, s64: X86InstrKind.Cvtsi2ss, flags: Second, x64: true);
            Row(Sse, "ConvertToInt32", S, f32: X86InstrKind.Cvtss2si);
            Row(Sse, "ConvertToInt32WithTruncation", S, f32: X86InstrKind.Cvttss2si);
            Row(Sse, "ConvertToInt64", S, f32: X86InstrKind.Cvtss2si, x64: true);
            Row(Sse, "ConvertToInt64WithTruncation", S, f32: X86InstrKind.Cvttss2si, x64: true);
            Row(Sse, "Divide", S, f32: X86InstrKind.Divps);
            Row(Sse, "DivideScalar", S, f32: X86InstrKind.Divss);
            Row(Sse, "LoadAlignedVector128", S, f32: X86InstrKind.Movaps);
            Row(Sse, "LoadHigh", S, f32: X86InstrKind.Movhps);
            Row(Sse, "LoadLow", S, f32: X86InstrKind.Movlps);
            Row(Sse, "LoadScalarVector128", S, f32: X86InstrKind.Movss);
            Row(Sse, "LoadVector128", S, f32: X86InstrKind.Movups);
            Row(Sse, "Max", S, f32: X86InstrKind.Maxps);
            Row(Sse, "MaxScalar", S, f32: X86InstrKind.Maxss);
            Row(Sse, "Min", S, f32: X86InstrKind.Minps);
            Row(Sse, "MinScalar", S, f32: X86InstrKind.Minss);
            Row(Sse, "MoveHighToLow", S, f32: X86InstrKind.Movhlps);
            Row(Sse, "MoveLowToHigh", S, f32: X86InstrKind.Movlhps);
            Row(Sse, "MoveMask", S, f32: X86InstrKind.Movmskps);
            Row(Sse, "MoveScalar", Sc, f32: X86InstrKind.Movss);
            Row(Sse, "Multiply", S, f32: X86InstrKind.Mulps, flags: Comm);
            Row(Sse, "MultiplyScalar", S, f32: X86InstrKind.Mulss);
            Row(Sse, "Or", S, f32: X86InstrKind.Orps, flags: Comm);
            Row(Sse, "Prefetch0", X86IntrinsicForm.Prefetch, any: X86InstrKind.Prefetcht0);
            Row(Sse, "Prefetch1", X86IntrinsicForm.Prefetch, any: X86InstrKind.Prefetcht1);
            Row(Sse, "Prefetch2", X86IntrinsicForm.Prefetch, any: X86InstrKind.Prefetcht2);
            Row(Sse, "PrefetchNonTemporal", X86IntrinsicForm.Prefetch, any: X86InstrKind.Prefetchnta);
            Row(Sse, "Reciprocal", S, f32: X86InstrKind.Rcpps);
            Row(Sse, "ReciprocalScalar", Sc, f32: X86InstrKind.Rcpss);
            Row(Sse, "ReciprocalSqrt", S, f32: X86InstrKind.Rsqrtps);
            Row(Sse, "ReciprocalSqrtScalar", Sc, f32: X86InstrKind.Rsqrtss);
            Row(Sse, "Shuffle", S, f32: X86InstrKind.Shufps);
            Row(Sse, "Sqrt", S, f32: X86InstrKind.Sqrtps);
            Row(Sse, "SqrtScalar", Sc, f32: X86InstrKind.Sqrtss);
            Row(Sse, "Store", St, f32: X86InstrKind.Movups);
            Row(Sse, "StoreAligned", St, f32: X86InstrKind.Movaps);
            Row(Sse, "StoreAlignedNonTemporal", St, f32: X86InstrKind.Movntps);
            Row(Sse, "StoreFence", X86IntrinsicForm.Barrier, any: X86InstrKind.Sfence);
            Row(Sse, "StoreHigh", St, f32: X86InstrKind.Movhps);
            Row(Sse, "StoreLow", St, f32: X86InstrKind.Movlps);
            Row(Sse, "StoreScalar", St, f32: X86InstrKind.Movss);
            Row(Sse, "Subtract", S, f32: X86InstrKind.Subps);
            Row(Sse, "SubtractScalar", S, f32: X86InstrKind.Subss);
            Row(Sse, "UnpackHigh", S, f32: X86InstrKind.Unpckhps);
            Row(Sse, "UnpackLow", S, f32: X86InstrKind.Unpcklps);
            Row(Sse, "Xor", S, f32: X86InstrKind.Xorps, flags: Comm);

            Row(Sse2, "Add", S, i8: X86InstrKind.Paddb, i16: X86InstrKind.Paddw, i32: X86InstrKind.Paddd, i64: X86InstrKind.Paddq, f64: X86InstrKind.Addpd, flags: Comm);
            Row(Sse2, "AddSaturate", S, s8: X86InstrKind.Paddsb, u8: X86InstrKind.Paddusb, s16: X86InstrKind.Paddsw, u16: X86InstrKind.Paddusw, flags: Comm);
            Row(Sse2, "AddScalar", S, f64: X86InstrKind.Addsd);
            Row(Sse2, "And", S, i8: X86InstrKind.Pand, i16: X86InstrKind.Pand, i32: X86InstrKind.Pand, i64: X86InstrKind.Pand, f64: X86InstrKind.Andpd, flags: Comm);
            Row(Sse2, "AndNot", S, i8: X86InstrKind.Pandn, i16: X86InstrKind.Pandn, i32: X86InstrKind.Pandn, i64: X86InstrKind.Pandn, f64: X86InstrKind.Andnpd);
            Row(Sse2, "Average", S, u8: X86InstrKind.Pavgb, u16: X86InstrKind.Pavgw, flags: Comm);
            Row(Sse2, "CompareEqual", X86IntrinsicForm.Compare, i8: X86InstrKind.Pcmpeqb, i16: X86InstrKind.Pcmpeqw, i32: X86InstrKind.Pcmpeqd,
                f64: X86InstrKind.Cmppd, flags: Comm | Fixed, imm: ComparePredicate.Equal);
            Row(Sse2, "CompareGreaterThan", X86IntrinsicForm.Compare, s8: X86InstrKind.Pcmpgtb, s16: X86InstrKind.Pcmpgtw, s32: X86InstrKind.Pcmpgtd,
                f64: X86InstrKind.Cmppd, flags: Swap | Fixed, imm: ComparePredicate.LessThan);
            Row(Sse2, "CompareLessThan", X86IntrinsicForm.Compare, s8: X86InstrKind.Pcmpgtb, s16: X86InstrKind.Pcmpgtw, s32: X86InstrKind.Pcmpgtd,
                f64: X86InstrKind.Cmppd, flags: Fixed, imm: ComparePredicate.LessThan);
            PackedCompares(Sse2, X86InstrKind.Cmppd, X86InstrKind.Cmpsd, X86InstrKind.Comisd, X86InstrKind.Ucomisd, singlePrecision: false);
            Row(Sse2, "ConvertScalarToVector128Double", S, s32: X86InstrKind.Cvtsi2sd, f32: X86InstrKind.Cvtss2sd, flags: Second);
            Row(Sse2, "ConvertScalarToVector128Double", S, s64: X86InstrKind.Cvtsi2sd, flags: Second, x64: true);
            Row(Sse2, "ConvertScalarToVector128Int32", S, s32: X86InstrKind.Movd);
            Row(Sse2, "ConvertScalarToVector128UInt32", S, u32: X86InstrKind.Movd);
            Row(Sse2, "ConvertScalarToVector128Int64", S, s64: X86InstrKind.Movq, x64: true);
            Row(Sse2, "ConvertScalarToVector128UInt64", S, u64: X86InstrKind.Movq, x64: true);
            Row(Sse2, "ConvertScalarToVector128Single", S, f64: X86InstrKind.Cvtsd2ss, flags: Second);
            Row(Sse2, "ConvertToInt32", S, s32: X86InstrKind.Movd, f64: X86InstrKind.Cvtsd2si);
            Row(Sse2, "ConvertToInt32WithTruncation", S, f64: X86InstrKind.Cvttsd2si);
            Row(Sse2, "ConvertToUInt32", S, u32: X86InstrKind.Movd);
            Row(Sse2, "ConvertToInt64", S, s64: X86InstrKind.Movq, f64: X86InstrKind.Cvtsd2si, x64: true);
            Row(Sse2, "ConvertToInt64WithTruncation", S, f64: X86InstrKind.Cvttsd2si, x64: true);
            Row(Sse2, "ConvertToUInt64", S, u64: X86InstrKind.Movq, x64: true);
            Row(Sse2, "ConvertToVector128Double", S, s32: X86InstrKind.Cvtdq2pd, f32: X86InstrKind.Cvtps2pd, flags: First);
            Row(Sse2, "ConvertToVector128Int32", S, f32: X86InstrKind.Cvtps2dq, f64: X86InstrKind.Cvtpd2dq, flags: First);
            Row(Sse2, "ConvertToVector128Int32WithTruncation", S, f32: X86InstrKind.Cvttps2dq, f64: X86InstrKind.Cvttpd2dq, flags: First);
            Row(Sse2, "ConvertToVector128Single", S, s32: X86InstrKind.Cvtdq2ps, f64: X86InstrKind.Cvtpd2ps, flags: First);
            Row(Sse2, "Divide", S, f64: X86InstrKind.Divpd);
            Row(Sse2, "DivideScalar", S, f64: X86InstrKind.Divsd);
            Row(Sse2, "Extract", S, u16: X86InstrKind.Pextrw);
            Row(Sse2, "Insert", S, i16: X86InstrKind.Pinsrw);
            Row(Sse2, "LoadAlignedVector128", S, any: X86InstrKind.Movaps);
            Row(Sse2, "LoadFence", X86IntrinsicForm.Barrier, any: X86InstrKind.Lfence);
            Row(Sse2, "LoadHigh", S, f64: X86InstrKind.Movhpd);
            Row(Sse2, "LoadLow", S, f64: X86InstrKind.Movlpd);
            Row(Sse2, "LoadScalarVector128", S, i32: X86InstrKind.Movd, i64: X86InstrKind.Movq, f64: X86InstrKind.Movsd);
            Row(Sse2, "LoadVector128", S, any: X86InstrKind.Movups);
            Row(Sse2, "MaskMove", X86IntrinsicForm.MaskMove, i8: X86InstrKind.Maskmovdqu);
            Row(Sse2, "Max", S, u8: X86InstrKind.Pmaxub, s16: X86InstrKind.Pmaxsw, f64: X86InstrKind.Maxpd, flags: IntComm);
            Row(Sse2, "MaxScalar", S, f64: X86InstrKind.Maxsd);
            Row(Sse2, "MemoryFence", X86IntrinsicForm.Barrier, any: X86InstrKind.Mfence);
            Row(Sse2, "Min", S, u8: X86InstrKind.Pminub, s16: X86InstrKind.Pminsw, f64: X86InstrKind.Minpd, flags: IntComm);
            Row(Sse2, "MinScalar", S, f64: X86InstrKind.Minsd);
            Row(Sse2, "MoveMask", S, i8: X86InstrKind.Pmovmskb, f64: X86InstrKind.Movmskpd);
            Row(Sse2, "MoveScalar", Sc, i64: X86InstrKind.Movq, f64: X86InstrKind.Movsd);
            Row(Sse2, "Multiply", S, u32: X86InstrKind.Pmuludq, f64: X86InstrKind.Mulpd, flags: Comm | First);
            Row(Sse2, "MultiplyAddAdjacent", S, s16: X86InstrKind.Pmaddwd, flags: Comm | First);
            Row(Sse2, "MultiplyHigh", S, s16: X86InstrKind.Pmulhw, u16: X86InstrKind.Pmulhuw, flags: Comm);
            Row(Sse2, "MultiplyLow", S, i16: X86InstrKind.Pmullw, flags: Comm);
            Row(Sse2, "MultiplyScalar", S, f64: X86InstrKind.Mulsd);
            Row(Sse2, "Or", S, i8: X86InstrKind.Por, i16: X86InstrKind.Por, i32: X86InstrKind.Por, i64: X86InstrKind.Por, f64: X86InstrKind.Orpd, flags: Comm);
            Row(Sse2, "PackSignedSaturate", S, s16: X86InstrKind.Packsswb, s32: X86InstrKind.Packssdw, flags: First);
            Row(Sse2, "PackUnsignedSaturate", S, s16: X86InstrKind.Packuswb, flags: First);
            Row(Sse2, "ShiftLeftLogical", X86IntrinsicForm.Shift, i16: X86InstrKind.Psllw, i32: X86InstrKind.Pslld, i64: X86InstrKind.Psllq);
            Ints(Sse2, "ShiftLeftLogical128BitLane", X86IntrinsicForm.Shift, X86InstrKind.Pslldq);
            Row(Sse2, "ShiftRightArithmetic", X86IntrinsicForm.Shift, s16: X86InstrKind.Psraw, s32: X86InstrKind.Psrad);
            Row(Sse2, "ShiftRightLogical", X86IntrinsicForm.Shift, i16: X86InstrKind.Psrlw, i32: X86InstrKind.Psrld, i64: X86InstrKind.Psrlq);
            Ints(Sse2, "ShiftRightLogical128BitLane", X86IntrinsicForm.Shift, X86InstrKind.Psrldq);
            Row(Sse2, "Shuffle", S, i32: X86InstrKind.Pshufd, f64: X86InstrKind.Shufpd);
            Row(Sse2, "ShuffleHigh", S, i16: X86InstrKind.Pshufhw);
            Row(Sse2, "ShuffleLow", S, i16: X86InstrKind.Pshuflw);
            Row(Sse2, "Sqrt", S, f64: X86InstrKind.Sqrtpd);
            Row(Sse2, "SqrtScalar", Sc, f64: X86InstrKind.Sqrtsd);
            Row(Sse2, "Store", St, any: X86InstrKind.Movups);
            Row(Sse2, "StoreAligned", St, any: X86InstrKind.Movaps);
            Row(Sse2, "StoreAlignedNonTemporal", St, i8: X86InstrKind.Movntdq, i16: X86InstrKind.Movntdq, i32: X86InstrKind.Movntdq, i64: X86InstrKind.Movntdq, f64: X86InstrKind.Movntpd);
            Row(Sse2, "StoreHigh", St, f64: X86InstrKind.Movhpd);
            Row(Sse2, "StoreLow", St, f64: X86InstrKind.Movlpd);
            Row(Sse2, "StoreNonTemporal", St, i32: X86InstrKind.Movnti);
            Row(Sse2, "StoreNonTemporal", St, i64: X86InstrKind.Movnti, x64: true);
            Row(Sse2, "StoreScalar", St, i32: X86InstrKind.Movd, i64: X86InstrKind.Movq, f64: X86InstrKind.Movsd);
            Row(Sse2, "Subtract", S, i8: X86InstrKind.Psubb, i16: X86InstrKind.Psubw, i32: X86InstrKind.Psubd, i64: X86InstrKind.Psubq, f64: X86InstrKind.Subpd);
            Row(Sse2, "SubtractSaturate", S, s8: X86InstrKind.Psubsb, u8: X86InstrKind.Psubusb, s16: X86InstrKind.Psubsw, u16: X86InstrKind.Psubusw);
            Row(Sse2, "SubtractScalar", S, f64: X86InstrKind.Subsd);
            Row(Sse2, "SumAbsoluteDifferences", S, u8: X86InstrKind.Psadbw, flags: Comm | First);
            Row(Sse2, "UnpackHigh", S, i8: X86InstrKind.Punpckhbw, i16: X86InstrKind.Punpckhwd, i32: X86InstrKind.Punpckhdq, i64: X86InstrKind.Punpckhqdq, f64: X86InstrKind.Unpckhpd);
            Row(Sse2, "UnpackLow", S, i8: X86InstrKind.Punpcklbw, i16: X86InstrKind.Punpcklwd, i32: X86InstrKind.Punpckldq, i64: X86InstrKind.Punpcklqdq, f64: X86InstrKind.Unpcklpd);
            Row(Sse2, "Xor", S, i8: X86InstrKind.Pxor, i16: X86InstrKind.Pxor, i32: X86InstrKind.Pxor, i64: X86InstrKind.Pxor, f64: X86InstrKind.Xorpd, flags: Comm);

            Row(Sse3, "AddSubtract", S, f32: X86InstrKind.Addsubps, f64: X86InstrKind.Addsubpd);
            Row(Sse3, "HorizontalAdd", S, f32: X86InstrKind.Haddps, f64: X86InstrKind.Haddpd);
            Row(Sse3, "HorizontalSubtract", S, f32: X86InstrKind.Hsubps, f64: X86InstrKind.Hsubpd);
            Row(Sse3, "LoadAndDuplicateToVector128", S, f64: X86InstrKind.Movddup);
            Ints(Sse3, "LoadDquVector128", S, X86InstrKind.Lddqu);
            Row(Sse3, "MoveAndDuplicate", S, f64: X86InstrKind.Movddup);
            Row(Sse3, "MoveHighAndDuplicate", S, f32: X86InstrKind.Movshdup);
            Row(Sse3, "MoveLowAndDuplicate", S, f32: X86InstrKind.Movsldup);

            Row(Ssse3, "Abs", S, s8: X86InstrKind.Pabsb, s16: X86InstrKind.Pabsw, s32: X86InstrKind.Pabsd, flags: First);
            Ints(Ssse3, "AlignRight", S, X86InstrKind.Palignr);
            Row(Ssse3, "HorizontalAdd", S, s16: X86InstrKind.Phaddw, s32: X86InstrKind.Phaddd);
            Row(Ssse3, "HorizontalAddSaturate", S, s16: X86InstrKind.Phaddsw);
            Row(Ssse3, "HorizontalSubtract", S, s16: X86InstrKind.Phsubw, s32: X86InstrKind.Phsubd);
            Row(Ssse3, "HorizontalSubtractSaturate", S, s16: X86InstrKind.Phsubsw);
            Row(Ssse3, "MultiplyAddAdjacent", S, u8: X86InstrKind.Pmaddubsw, flags: First);
            Row(Ssse3, "MultiplyHighRoundScale", S, s16: X86InstrKind.Pmulhrsw, flags: Comm);
            Row(Ssse3, "Shuffle", S, i8: X86InstrKind.Pshufb);
            Row(Ssse3, "Sign", S, s8: X86InstrKind.Psignb, s16: X86InstrKind.Psignw, s32: X86InstrKind.Psignd);

            Row(Sse41, "Blend", S, i16: X86InstrKind.Pblendw, f32: X86InstrKind.Blendps, f64: X86InstrKind.Blendpd);
            Row(Sse41, "BlendVariable", X86IntrinsicForm.BlendVariable, i8: X86InstrKind.Pblendvb, i16: X86InstrKind.Pblendvb, i32: X86InstrKind.Pblendvb,
                i64: X86InstrKind.Pblendvb, f32: X86InstrKind.Blendvps, f64: X86InstrKind.Blendvpd);
            foreach (var (name, mode) in new[] { ("Ceiling", 0xA), ("Floor", 0x9), ("RoundCurrentDirection", 0x4), ("RoundToNearestInteger", 0x8),
                ("RoundToNegativeInfinity", 0x9), ("RoundToPositiveInfinity", 0xA), ("RoundToZero", 0xB) })
            {
                Row(Sse41, name, S, f32: X86InstrKind.Roundps, f64: X86InstrKind.Roundpd, flags: Fixed, imm: mode);
                Row(Sse41, name + "Scalar", Sc, f32: X86InstrKind.Roundss, f64: X86InstrKind.Roundsd, flags: Fixed, imm: mode);
            }
            Row(Sse41, "CompareEqual", X86IntrinsicForm.Compare, i64: X86InstrKind.Pcmpeqq, flags: Comm | Fixed, imm: ComparePredicate.Equal);
            Row(Sse41, "ConvertToVector128Int16", S, s8: X86InstrKind.Pmovsxbw, u8: X86InstrKind.Pmovzxbw, flags: First);
            Row(Sse41, "ConvertToVector128Int32", S, s8: X86InstrKind.Pmovsxbd, u8: X86InstrKind.Pmovzxbd, s16: X86InstrKind.Pmovsxwd, u16: X86InstrKind.Pmovzxwd, flags: First);
            Row(Sse41, "ConvertToVector128Int64", S, s8: X86InstrKind.Pmovsxbq, u8: X86InstrKind.Pmovzxbq, s16: X86InstrKind.Pmovsxwq, u16: X86InstrKind.Pmovzxwq,
                s32: X86InstrKind.Pmovsxdq, u32: X86InstrKind.Pmovzxdq, flags: First);
            Row(Sse41, "DotProduct", S, f32: X86InstrKind.Dpps, f64: X86InstrKind.Dppd);
            Row(Sse41, "Extract", S, u8: X86InstrKind.Pextrb, i32: X86InstrKind.Pextrd, f32: X86InstrKind.Pshufd);
            Row(Sse41, "Extract", S, i64: X86InstrKind.Pextrq, x64: true);
            Row(Sse41, "Insert", S, i8: X86InstrKind.Pinsrb, i32: X86InstrKind.Pinsrd, f32: X86InstrKind.Insertps);
            Row(Sse41, "Insert", S, i64: X86InstrKind.Pinsrq, x64: true);
            Ints(Sse41, "LoadAlignedVector128NonTemporal", S, X86InstrKind.Movntdqa);
            Row(Sse41, "Max", S, s8: X86InstrKind.Pmaxsb, u16: X86InstrKind.Pmaxuw, s32: X86InstrKind.Pmaxsd, u32: X86InstrKind.Pmaxud, flags: Comm);
            Row(Sse41, "Min", S, s8: X86InstrKind.Pminsb, u16: X86InstrKind.Pminuw, s32: X86InstrKind.Pminsd, u32: X86InstrKind.Pminud, flags: Comm);
            Row(Sse41, "MinHorizontal", S, u16: X86InstrKind.Phminposuw);
            Row(Sse41, "MultipleSumAbsoluteDifferences", S, u8: X86InstrKind.Mpsadbw, flags: First);
            Row(Sse41, "Multiply", S, s32: X86InstrKind.Pmuldq, flags: Comm | First);
            Row(Sse41, "MultiplyLow", S, i32: X86InstrKind.Pmulld, flags: Comm);
            Row(Sse41, "PackUnsignedSaturate", S, s32: X86InstrKind.Packusdw, flags: First);
            Ints(Sse41, "TestC", X86IntrinsicForm.CompareFlags, X86InstrKind.Ptest, Fixed, (int)X86Condition.B);
            Ints(Sse41, "TestNotZAndNotC", X86IntrinsicForm.CompareFlags, X86InstrKind.Ptest, Fixed, (int)X86Condition.A);
            Ints(Sse41, "TestZ", X86IntrinsicForm.CompareFlags, X86InstrKind.Ptest, Fixed, (int)X86Condition.E);

            Row(Sse42, "CompareGreaterThan", X86IntrinsicForm.Compare, s64: X86InstrKind.Pcmpgtq, flags: Swap | Fixed, imm: ComparePredicate.LessThan);
            Row(Sse42, "Crc32", X86IntrinsicForm.Crc32, u8: X86InstrKind.Crc32, u16: X86InstrKind.Crc32, u32: X86InstrKind.Crc32, flags: Second);
            Row(Sse42, "Crc32", X86IntrinsicForm.Crc32, u64: X86InstrKind.Crc32, flags: Second, x64: true);

            Row(X86Isa.Lzcnt, "LeadingZeroCount", S, u32: X86InstrKind.Lzcnt);
            Row(X86Isa.Lzcnt, "LeadingZeroCount", S, u64: X86InstrKind.Lzcnt, x64: true);

            BuildAvx();
            BuildAvx2();
            BuildScalarExtensions();
            BuildAvx512();
            BuildAvx10v2();
        }

        private static void Integers(X86Isa isa, string name, X86IntrinsicForm form, X86InstrKind i8, X86InstrKind i16, X86InstrKind i32, X86InstrKind i64,
            X86IntrinsicFlags flags = X86IntrinsicFlags.None)
            => Row(isa, name, form, i8: i8, i16: i16, i32: i32, i64: i64, flags: flags);

        private static readonly (string Name, int Predicate)[] VexComparePredicates =
        {
            ("CompareEqual", 0x00), ("CompareLessThan", 0x01), ("CompareLessThanOrEqual", 0x02), ("CompareUnordered", 0x03),
            ("CompareNotEqual", 0x04), ("CompareNotLessThan", 0x05), ("CompareNotLessThanOrEqual", 0x06), ("CompareOrdered", 0x07),
            ("CompareNotGreaterThanOrEqual", 0x09), ("CompareNotGreaterThan", 0x0A), ("CompareGreaterThanOrEqual", 0x0D), ("CompareGreaterThan", 0x0E),
        };

        private static readonly (string Name, int Mode)[] RoundingModes =
        {
            ("Ceiling", 0xA), ("Floor", 0x9), ("RoundCurrentDirection", 0x4), ("RoundToNearestInteger", 0x8),
            ("RoundToNegativeInfinity", 0x9), ("RoundToPositiveInfinity", 0xA), ("RoundToZero", 0xB),
        };

        private static void BuildAvx()
        {
            const X86IntrinsicForm V = X86IntrinsicForm.Vex, VS = X86IntrinsicForm.VexStore;
            const X86IntrinsicFlags First = X86IntrinsicFlags.BaseFromFirstArg, Second = X86IntrinsicFlags.BaseFromSecondArg;
            const X86IntrinsicFlags Swap = X86IntrinsicFlags.SwapOperands, Fixed = X86IntrinsicFlags.TableImmediate;
            const X86Isa A = X86Isa.Avx;

            Row(A, "Add", V, f32: X86InstrKind.Addps, f64: X86InstrKind.Addpd);
            Row(A, "AddSubtract", V, f32: X86InstrKind.Addsubps, f64: X86InstrKind.Addsubpd);
            Row(A, "And", V, f32: X86InstrKind.Andps, f64: X86InstrKind.Andpd);
            Row(A, "AndNot", V, f32: X86InstrKind.Andnps, f64: X86InstrKind.Andnpd);
            Row(A, "Blend", V, f32: X86InstrKind.Blendps, f64: X86InstrKind.Blendpd);
            Row(A, "BlendVariable", V, f32: X86InstrKind.Blendvps, f64: X86InstrKind.Blendvpd);
            Row(A, "BroadcastScalarToVector128", V, f32: X86InstrKind.Vbroadcastss);
            Row(A, "BroadcastScalarToVector256", V, f32: X86InstrKind.Vbroadcastss, f64: X86InstrKind.Vbroadcastsd);
            Row(A, "BroadcastVector128ToVector256", V, any: X86InstrKind.Vbroadcastf128);
            foreach (var (name, mode) in RoundingModes)
                Row(A, name, V, f32: X86InstrKind.Roundps, f64: X86InstrKind.Roundpd, flags: Fixed, imm: mode);
            Row(A, "Compare", V, f32: X86InstrKind.Cmpps, f64: X86InstrKind.Cmppd, bound: 31);
            foreach (var (name, predicate) in VexComparePredicates)
                Row(A, name, V, f32: X86InstrKind.Cmpps, f64: X86InstrKind.Cmppd, flags: Fixed, imm: predicate);
            Row(A, "CompareScalar", V, f32: X86InstrKind.Cmpss, f64: X86InstrKind.Cmpsd, bound: 31);
            Row(A, "ConvertToVector128Int32", V, f64: X86InstrKind.Cvtpd2dq, flags: First);
            Row(A, "ConvertToVector128Int32WithTruncation", V, f64: X86InstrKind.Cvttpd2dq, flags: First);
            Row(A, "ConvertToVector128Single", V, f64: X86InstrKind.Cvtpd2ps, flags: First);
            Row(A, "ConvertToVector256Double", V, s32: X86InstrKind.Cvtdq2pd, f32: X86InstrKind.Cvtps2pd, flags: First);
            Row(A, "ConvertToVector256Int32", V, f32: X86InstrKind.Cvtps2dq, flags: First);
            Row(A, "ConvertToVector256Int32WithTruncation", V, f32: X86InstrKind.Cvttps2dq, flags: First);
            Row(A, "ConvertToVector256Single", V, s32: X86InstrKind.Cvtdq2ps, flags: First);
            Row(A, "Divide", V, f32: X86InstrKind.Divps, f64: X86InstrKind.Divpd);
            Row(A, "DotProduct", V, f32: X86InstrKind.Dpps);
            Row(A, "DuplicateEvenIndexed", V, f32: X86InstrKind.Movsldup, f64: X86InstrKind.Movddup);
            Row(A, "DuplicateOddIndexed", V, f32: X86InstrKind.Movshdup);
            Row(A, "ExtractVector128", V, any: X86InstrKind.Vextractf128);
            Row(A, "HorizontalAdd", V, f32: X86InstrKind.Haddps, f64: X86InstrKind.Haddpd);
            Row(A, "HorizontalSubtract", V, f32: X86InstrKind.Hsubps, f64: X86InstrKind.Hsubpd);
            Row(A, "InsertVector128", V, any: X86InstrKind.Vinsertf128);
            Row(A, "LoadAlignedVector256", V, any: X86InstrKind.Movaps);
            Row(A, "LoadDquVector256", V, any: X86InstrKind.Lddqu);
            Row(A, "LoadVector256", V, any: X86InstrKind.Movups);
            Row(A, "MaskLoad", V, f32: X86InstrKind.Vmaskmovps, f64: X86InstrKind.Vmaskmovpd, flags: Swap);
            Row(A, "MaskStore", VS, f32: X86InstrKind.Vmaskmovps, f64: X86InstrKind.Vmaskmovpd, flags: Second);
            Row(A, "Max", V, f32: X86InstrKind.Maxps, f64: X86InstrKind.Maxpd);
            Row(A, "Min", V, f32: X86InstrKind.Minps, f64: X86InstrKind.Minpd);
            Row(A, "MoveMask", V, f32: X86InstrKind.Movmskps, f64: X86InstrKind.Movmskpd, flags: First);
            Row(A, "Multiply", V, f32: X86InstrKind.Mulps, f64: X86InstrKind.Mulpd);
            Row(A, "Or", V, f32: X86InstrKind.Orps, f64: X86InstrKind.Orpd);
            Row(A, "Permute", V, f32: X86InstrKind.Vpermilps, f64: X86InstrKind.Vpermilpd);
            Row(A, "Permute2x128", V, any: X86InstrKind.Vperm2f128);
            Row(A, "PermuteVar", V, f32: X86InstrKind.Vpermilps, f64: X86InstrKind.Vpermilpd);
            Row(A, "Reciprocal", V, f32: X86InstrKind.Rcpps);
            Row(A, "ReciprocalSqrt", V, f32: X86InstrKind.Rsqrtps);
            Row(A, "Shuffle", V, f32: X86InstrKind.Shufps, f64: X86InstrKind.Shufpd);
            Row(A, "Sqrt", V, f32: X86InstrKind.Sqrtps, f64: X86InstrKind.Sqrtpd);
            Row(A, "Store", VS, any: X86InstrKind.Movups);
            Row(A, "StoreAligned", VS, any: X86InstrKind.Movaps);
            Row(A, "StoreAlignedNonTemporal", VS, i8: X86InstrKind.Movntdq, i16: X86InstrKind.Movntdq, i32: X86InstrKind.Movntdq, i64: X86InstrKind.Movntdq,
                f32: X86InstrKind.Movntps, f64: X86InstrKind.Movntpd, flags: Second);
            Row(A, "Subtract", V, f32: X86InstrKind.Subps, f64: X86InstrKind.Subpd);
            foreach (var (name, condition) in new[] { ("TestC", X86Condition.B), ("TestNotZAndNotC", X86Condition.A), ("TestZ", X86Condition.E) })
            {
                Row(A, name, X86IntrinsicForm.CompareFlags, i8: X86InstrKind.Ptest, i16: X86InstrKind.Ptest, i32: X86InstrKind.Ptest, i64: X86InstrKind.Ptest,
                    f32: X86InstrKind.Vtestps, f64: X86InstrKind.Vtestpd, flags: First | Fixed, imm: (int)condition);
            }
            Row(A, "UnpackHigh", V, f32: X86InstrKind.Unpckhps, f64: X86InstrKind.Unpckhpd);
            Row(A, "UnpackLow", V, f32: X86InstrKind.Unpcklps, f64: X86InstrKind.Unpcklpd);
            Row(A, "Xor", V, f32: X86InstrKind.Xorps, f64: X86InstrKind.Xorpd);
        }

        private static void BuildAvx2()
        {
            const X86IntrinsicForm V = X86IntrinsicForm.Vex, VS = X86IntrinsicForm.VexStore;
            const X86IntrinsicFlags First = X86IntrinsicFlags.BaseFromFirstArg, Second = X86IntrinsicFlags.BaseFromSecondArg;
            const X86IntrinsicFlags Swap = X86IntrinsicFlags.SwapOperands;
            const X86Isa A = X86Isa.Avx2;

            Row(A, "Abs", V, s8: X86InstrKind.Pabsb, s16: X86InstrKind.Pabsw, s32: X86InstrKind.Pabsd, flags: First);
            Integers(A, "Add", V, X86InstrKind.Paddb, X86InstrKind.Paddw, X86InstrKind.Paddd, X86InstrKind.Paddq);
            Row(A, "AddSaturate", V, s8: X86InstrKind.Paddsb, u8: X86InstrKind.Paddusb, s16: X86InstrKind.Paddsw, u16: X86InstrKind.Paddusw);
            Integers(A, "AlignRight", V, X86InstrKind.Palignr, X86InstrKind.Palignr, X86InstrKind.Palignr, X86InstrKind.Palignr);
            Integers(A, "And", V, X86InstrKind.Pand, X86InstrKind.Pand, X86InstrKind.Pand, X86InstrKind.Pand);
            Integers(A, "AndNot", V, X86InstrKind.Pandn, X86InstrKind.Pandn, X86InstrKind.Pandn, X86InstrKind.Pandn);
            Row(A, "Average", V, u8: X86InstrKind.Pavgb, u16: X86InstrKind.Pavgw);
            Row(A, "Blend", V, i16: X86InstrKind.Pblendw, i32: X86InstrKind.Vpblendd);
            Integers(A, "BlendVariable", V, X86InstrKind.Pblendvb, X86InstrKind.Pblendvb, X86InstrKind.Pblendvb, X86InstrKind.Pblendvb);
            Row(A, "BroadcastScalarToVector128", V, i8: X86InstrKind.Vpbroadcastb, i16: X86InstrKind.Vpbroadcastw, i32: X86InstrKind.Vpbroadcastd,
                i64: X86InstrKind.Vpbroadcastq, f32: X86InstrKind.Vbroadcastss, f64: X86InstrKind.Movddup);
            Row(A, "BroadcastScalarToVector256", V, i8: X86InstrKind.Vpbroadcastb, i16: X86InstrKind.Vpbroadcastw, i32: X86InstrKind.Vpbroadcastd,
                i64: X86InstrKind.Vpbroadcastq, f32: X86InstrKind.Vbroadcastss, f64: X86InstrKind.Vbroadcastsd);
            Row(A, "BroadcastVector128ToVector256", V, any: X86InstrKind.Vbroadcasti128);
            Integers(A, "CompareEqual", V, X86InstrKind.Pcmpeqb, X86InstrKind.Pcmpeqw, X86InstrKind.Pcmpeqd, X86InstrKind.Pcmpeqq);
            Row(A, "CompareGreaterThan", V, s8: X86InstrKind.Pcmpgtb, s16: X86InstrKind.Pcmpgtw, s32: X86InstrKind.Pcmpgtd, s64: X86InstrKind.Pcmpgtq);
            Row(A, "ConvertToInt32", V, s32: X86InstrKind.Movd, flags: First);
            Row(A, "ConvertToUInt32", V, u32: X86InstrKind.Movd, flags: First);
            Row(A, "ConvertToVector256Int16", V, s8: X86InstrKind.Pmovsxbw, u8: X86InstrKind.Pmovzxbw, flags: First);
            Row(A, "ConvertToVector256Int32", V, s8: X86InstrKind.Pmovsxbd, u8: X86InstrKind.Pmovzxbd, s16: X86InstrKind.Pmovsxwd, u16: X86InstrKind.Pmovzxwd, flags: First);
            Row(A, "ConvertToVector256Int64", V, s8: X86InstrKind.Pmovsxbq, u8: X86InstrKind.Pmovzxbq, s16: X86InstrKind.Pmovsxwq, u16: X86InstrKind.Pmovzxwq,
                s32: X86InstrKind.Pmovsxdq, u32: X86InstrKind.Pmovzxdq, flags: First);
            Row(A, "ExtractVector128", V, any: X86InstrKind.Vextracti128);
            foreach (var (name, form) in new[] { ("GatherVector128", X86IntrinsicForm.Gather), ("GatherVector256", X86IntrinsicForm.Gather),
                ("GatherMaskVector128", X86IntrinsicForm.GatherMask), ("GatherMaskVector256", X86IntrinsicForm.GatherMask) })
            {
                Row(A, name, form, i32: X86InstrKind.Vpgatherdd, i64: X86InstrKind.Vpgatherdq, f32: X86InstrKind.Vgatherdps, f64: X86InstrKind.Vgatherdpd);
                Row(A, name + ".Q", form, i32: X86InstrKind.Vpgatherqd, i64: X86InstrKind.Vpgatherqq, f32: X86InstrKind.Vgatherqps, f64: X86InstrKind.Vgatherqpd);
            }
            Row(A, "HorizontalAdd", V, s16: X86InstrKind.Phaddw, s32: X86InstrKind.Phaddd);
            Row(A, "HorizontalAddSaturate", V, s16: X86InstrKind.Phaddsw);
            Row(A, "HorizontalSubtract", V, s16: X86InstrKind.Phsubw, s32: X86InstrKind.Phsubd);
            Row(A, "HorizontalSubtractSaturate", V, s16: X86InstrKind.Phsubsw);
            Row(A, "InsertVector128", V, any: X86InstrKind.Vinserti128);
            Integers(A, "LoadAlignedVector256NonTemporal", V, X86InstrKind.Movntdqa, X86InstrKind.Movntdqa, X86InstrKind.Movntdqa, X86InstrKind.Movntdqa);
            Row(A, "MaskLoad", V, i32: X86InstrKind.Vpmaskmovd, i64: X86InstrKind.Vpmaskmovq, flags: Swap);
            Row(A, "MaskStore", VS, i32: X86InstrKind.Vpmaskmovd, i64: X86InstrKind.Vpmaskmovq, flags: Second);
            Row(A, "Max", V, s8: X86InstrKind.Pmaxsb, u8: X86InstrKind.Pmaxub, s16: X86InstrKind.Pmaxsw, u16: X86InstrKind.Pmaxuw, s32: X86InstrKind.Pmaxsd,
                u32: X86InstrKind.Pmaxud);
            Row(A, "Min", V, s8: X86InstrKind.Pminsb, u8: X86InstrKind.Pminub, s16: X86InstrKind.Pminsw, u16: X86InstrKind.Pminuw, s32: X86InstrKind.Pminsd,
                u32: X86InstrKind.Pminud);
            Row(A, "MoveMask", V, i8: X86InstrKind.Pmovmskb, flags: First);
            Row(A, "MultipleSumAbsoluteDifferences", V, u16: X86InstrKind.Mpsadbw);
            Row(A, "Multiply", V, s32: X86InstrKind.Pmuldq, u32: X86InstrKind.Pmuludq, flags: First);
            Row(A, "MultiplyAddAdjacent", V, s16: X86InstrKind.Pmaddubsw, s32: X86InstrKind.Pmaddwd);
            Row(A, "MultiplyHigh", V, s16: X86InstrKind.Pmulhw, u16: X86InstrKind.Pmulhuw);
            Row(A, "MultiplyHighRoundScale", V, s16: X86InstrKind.Pmulhrsw);
            Row(A, "MultiplyLow", V, i16: X86InstrKind.Pmullw, i32: X86InstrKind.Pmulld);
            Integers(A, "Or", V, X86InstrKind.Por, X86InstrKind.Por, X86InstrKind.Por, X86InstrKind.Por);
            Row(A, "PackSignedSaturate", V, s8: X86InstrKind.Packsswb, s16: X86InstrKind.Packssdw);
            Row(A, "PackUnsignedSaturate", V, u8: X86InstrKind.Packuswb, u16: X86InstrKind.Packusdw);
            Row(A, "Permute2x128", V, any: X86InstrKind.Vperm2i128);
            Row(A, "Permute4x64", V, i64: X86InstrKind.Vpermq, f64: X86InstrKind.Vpermpd);
            Row(A, "PermuteVar8x32", V, i32: X86InstrKind.Vpermd, f32: X86InstrKind.Vpermps, flags: Swap);
            Row(A, "ShiftLeftLogical", V, i16: X86InstrKind.Psllw, i32: X86InstrKind.Pslld, i64: X86InstrKind.Psllq);
            Integers(A, "ShiftLeftLogical128BitLane", V, X86InstrKind.Pslldq, X86InstrKind.Pslldq, X86InstrKind.Pslldq, X86InstrKind.Pslldq);
            Row(A, "ShiftLeftLogicalVariable", V, i32: X86InstrKind.Vpsllvd, i64: X86InstrKind.Vpsllvq);
            Row(A, "ShiftRightArithmetic", V, s16: X86InstrKind.Psraw, s32: X86InstrKind.Psrad);
            Row(A, "ShiftRightArithmeticVariable", V, s32: X86InstrKind.Vpsravd);
            Row(A, "ShiftRightLogical", V, i16: X86InstrKind.Psrlw, i32: X86InstrKind.Psrld, i64: X86InstrKind.Psrlq);
            Integers(A, "ShiftRightLogical128BitLane", V, X86InstrKind.Psrldq, X86InstrKind.Psrldq, X86InstrKind.Psrldq, X86InstrKind.Psrldq);
            Row(A, "ShiftRightLogicalVariable", V, i32: X86InstrKind.Vpsrlvd, i64: X86InstrKind.Vpsrlvq);
            Row(A, "Shuffle", V, i8: X86InstrKind.Pshufb, i32: X86InstrKind.Pshufd);
            Row(A, "ShuffleHigh", V, i16: X86InstrKind.Pshufhw);
            Row(A, "ShuffleLow", V, i16: X86InstrKind.Pshuflw);
            Row(A, "Sign", V, s8: X86InstrKind.Psignb, s16: X86InstrKind.Psignw, s32: X86InstrKind.Psignd);
            Integers(A, "Subtract", V, X86InstrKind.Psubb, X86InstrKind.Psubw, X86InstrKind.Psubd, X86InstrKind.Psubq);
            Row(A, "SubtractSaturate", V, s8: X86InstrKind.Psubsb, u8: X86InstrKind.Psubusb, s16: X86InstrKind.Psubsw, u16: X86InstrKind.Psubusw);
            Row(A, "SumAbsoluteDifferences", V, u16: X86InstrKind.Psadbw);
            Integers(A, "UnpackHigh", V, X86InstrKind.Punpckhbw, X86InstrKind.Punpckhwd, X86InstrKind.Punpckhdq, X86InstrKind.Punpckhqdq);
            Integers(A, "UnpackLow", V, X86InstrKind.Punpcklbw, X86InstrKind.Punpcklwd, X86InstrKind.Punpckldq, X86InstrKind.Punpcklqdq);
            Integers(A, "Xor", V, X86InstrKind.Pxor, X86InstrKind.Pxor, X86InstrKind.Pxor, X86InstrKind.Pxor);
        }

        private static void BuildScalarExtensions()
        {
            const X86IntrinsicForm V = X86IntrinsicForm.Vex, T = X86IntrinsicForm.Tied;
            const X86IntrinsicFlags Second = X86IntrinsicFlags.BaseFromSecondArg;

            Row(X86Isa.Popcnt, "PopCount", V, u32: X86InstrKind.Popcnt);
            Row(X86Isa.Popcnt, "PopCount", V, u64: X86InstrKind.Popcnt, x64: true);

            foreach (bool x64 in new[] { false, true })
            {
                X86IntrinsicFlags none = X86IntrinsicFlags.None;
                void Gpr(X86Isa isa, string name, X86IntrinsicForm form, X86InstrKind instruction)
                    => Row(isa, name, form, i32: x64 ? X86InstrKind.Invalid : instruction, i64: x64 ? instruction : X86InstrKind.Invalid, flags: none, x64: x64);
                Gpr(X86Isa.Bmi1, "AndNot", V, X86InstrKind.Andn);
                Gpr(X86Isa.Bmi1, "BitFieldExtract", X86IntrinsicForm.BitFieldExtract, X86InstrKind.Bextr);
                Gpr(X86Isa.Bmi1, "ExtractLowestSetBit", V, X86InstrKind.Blsi);
                Gpr(X86Isa.Bmi1, "GetMaskUpToLowestSetBit", V, X86InstrKind.Blsmsk);
                Gpr(X86Isa.Bmi1, "ResetLowestSetBit", V, X86InstrKind.Blsr);
                Gpr(X86Isa.Bmi1, "TrailingZeroCount", V, X86InstrKind.Tzcnt);
                Gpr(X86Isa.Bmi2, "MultiplyNoFlags", X86IntrinsicForm.MultiplyNoFlags, X86InstrKind.Mulx);
                Gpr(X86Isa.Bmi2, "ParallelBitDeposit", V, X86InstrKind.Pdep);
                Gpr(X86Isa.Bmi2, "ParallelBitExtract", V, X86InstrKind.Pext);
                Gpr(X86Isa.Bmi2, "ZeroHighBits", V, X86InstrKind.Bzhi);
            }

            foreach (var (name, packed, scalar) in new[]
            {
                ("MultiplyAdd", "Vfmadd213", true), ("MultiplyAddNegated", "Vfnmadd213", true), ("MultiplySubtract", "Vfmsub213", true),
                ("MultiplySubtractNegated", "Vfnmsub213", true), ("MultiplyAddSubtract", "Vfmaddsub213", false), ("MultiplySubtractAdd", "Vfmsubadd213", false),
            })
            {
                Row(X86Isa.Fma, name, T, f32: Enum.Parse<X86InstrKind>(packed + "ps"), f64: Enum.Parse<X86InstrKind>(packed + "pd"));
                if (scalar)
                    Row(X86Isa.Fma, name + "Scalar", T, f32: Enum.Parse<X86InstrKind>(packed + "ss"), f64: Enum.Parse<X86InstrKind>(packed + "sd"));
            }

            Row(X86Isa.Aes, "Decrypt", V, u8: X86InstrKind.Aesdec);
            Row(X86Isa.Aes, "DecryptLast", V, u8: X86InstrKind.Aesdeclast);
            Row(X86Isa.Aes, "Encrypt", V, u8: X86InstrKind.Aesenc);
            Row(X86Isa.Aes, "EncryptLast", V, u8: X86InstrKind.Aesenclast);
            Row(X86Isa.Aes, "InverseMixColumns", V, u8: X86InstrKind.Aesimc);
            Row(X86Isa.Aes, "KeygenAssist", V, u8: X86InstrKind.Aeskeygenassist);
            Row(X86Isa.Pclmulqdq, "CarrylessMultiply", V, i64: X86InstrKind.Pclmulqdq);
            Row(X86Isa.Gfni, "GaloisFieldAffineTransform", V, u8: X86InstrKind.Gf2p8affineqb);
            Row(X86Isa.Gfni, "GaloisFieldAffineTransformInverse", V, u8: X86InstrKind.Gf2p8affineinvqb);
            Row(X86Isa.Gfni, "GaloisFieldMultiply", V, u8: X86InstrKind.Gf2p8mulb);
            Row(X86Isa.AvxVnni, "MultiplyWideningAndAdd", T, u8: X86InstrKind.Vpdpbusd, s16: X86InstrKind.Vpdpwssd, flags: Second);
            Row(X86Isa.AvxVnni, "MultiplyWideningAndAddSaturate", T, u8: X86InstrKind.Vpdpbusds, s16: X86InstrKind.Vpdpwssds, flags: Second);
            Row(X86Isa.X86Serialize, "Serialize", X86IntrinsicForm.Barrier, any: X86InstrKind.Serialize);
        }

        private static void BuildAvx10v2()
        {
            const X86IntrinsicForm V = X86IntrinsicForm.Vex, T = X86IntrinsicForm.Tied;
            const X86IntrinsicFlags First = X86IntrinsicFlags.BaseFromFirstArg, Second = X86IntrinsicFlags.BaseFromSecondArg;
            const X86Isa A = X86Isa.Avx10v2;

            Row(A, "ConvertToByteWithSaturationAndZeroExtendToInt32", V, f32: X86InstrKind.Vcvtps2iubs, flags: First);
            Row(A, "ConvertToByteWithTruncatedSaturationAndZeroExtendToInt32", V, f32: X86InstrKind.Vcvttps2iubs, flags: First);
            Row(A, "ConvertToSByteWithSaturationAndZeroExtendToInt32", V, f32: X86InstrKind.Vcvtps2ibs, flags: First);
            Row(A, "ConvertToSByteWithTruncatedSaturationAndZeroExtendToInt32", V, f32: X86InstrKind.Vcvttps2ibs, flags: First);
            Row(A, "MinMax", V, f32: X86InstrKind.Vminmaxps, f64: X86InstrKind.Vminmaxpd);
            Row(A, "MinMaxScalar", V, f32: X86InstrKind.Vminmaxss, f64: X86InstrKind.Vminmaxsd);
            Row(A, "MoveScalar", V, i16: X86InstrKind.Vmovw, i32: X86InstrKind.Movd);
            Row(A, "MultipleSumAbsoluteDifferences", V, u16: X86InstrKind.Mpsadbw);
            Row(A, "StoreScalar", X86IntrinsicForm.VexStore, i16: X86InstrKind.Vmovw, flags: Second);

            foreach (var (name, suffix) in new[] { ("MultiplyWideningAndAdd", "d"), ("MultiplyWideningAndAddSaturate", "ds") })
            {
                X86InstrKind I(string mnemonic) => Enum.Parse<X86InstrKind>(mnemonic + suffix);
                Row(X86Isa.AvxVnniInt8, name, T, s8: I("Vpdpbss"), u8: I("Vpdpbuu"), u16: I("Vpdpwuu"), flags: Second);
                Row(X86Isa.AvxVnniInt8, name + ".Mixed", T, s8: I("Vpdpbsu"), s16: I("Vpdpwsu"), u16: I("Vpdpwus"), flags: Second);
            }
        }

        private static void Sized(X86Isa isa, string name, X86IntrinsicForm form, X86InstrKind i8, X86InstrKind i16, X86InstrKind i32, X86InstrKind i64,
            X86InstrKind f32, X86InstrKind f64, X86IntrinsicFlags flags = X86IntrinsicFlags.None)
            => Row(isa, name, form, i8: i8, i16: i16, i32: i32, i64: i64, f32: f32, f64: f64, flags: flags);

        private static void BuildAvx512()
        {
            const X86IntrinsicForm V = X86IntrinsicForm.Vex, VS = X86IntrinsicForm.VexStore, T = X86IntrinsicForm.Tied;
            const X86IntrinsicFlags First = X86IntrinsicFlags.BaseFromFirstArg, Second = X86IntrinsicFlags.BaseFromSecondArg;
            const X86IntrinsicFlags Swap = X86IntrinsicFlags.SwapOperands, Fixed = X86IntrinsicFlags.TableImmediate;
            const X86Isa A = X86Isa.Avx512F;

            Row(A, "Abs", V, s8: X86InstrKind.Pabsb, s16: X86InstrKind.Pabsw, s32: X86InstrKind.Pabsd, s64: X86InstrKind.Vpabsq, flags: First);
            Sized(A, "Add", V, X86InstrKind.Paddb, X86InstrKind.Paddw, X86InstrKind.Paddd, X86InstrKind.Paddq, X86InstrKind.Addps, X86InstrKind.Addpd);
            Row(A, "AddSaturate", V, s8: X86InstrKind.Paddsb, u8: X86InstrKind.Paddusb, s16: X86InstrKind.Paddsw, u16: X86InstrKind.Paddusw);
            Row(A, "AddScalar", V, f32: X86InstrKind.Addss, f64: X86InstrKind.Addsd);
            Integers(A, "AlignRight", V, X86InstrKind.Palignr, X86InstrKind.Palignr, X86InstrKind.Palignr, X86InstrKind.Palignr);
            Row(A, "AlignRight32", V, i32: X86InstrKind.Valignd);
            Row(A, "AlignRight64", V, i64: X86InstrKind.Valignq);
            Sized(A, "And", V, X86InstrKind.Pand, X86InstrKind.Pand, X86InstrKind.Pand, X86InstrKind.Vpandq, X86InstrKind.Andps, X86InstrKind.Andpd);
            Sized(A, "AndNot", V, X86InstrKind.Pandn, X86InstrKind.Pandn, X86InstrKind.Pandn, X86InstrKind.Vpandnq, X86InstrKind.Andnps, X86InstrKind.Andnpd);
            Row(A, "Average", V, u8: X86InstrKind.Pavgb, u16: X86InstrKind.Pavgw);
            Sized(A, "BlendVariable", X86IntrinsicForm.MaskedBlend, X86InstrKind.Vpblendmb, X86InstrKind.Vpblendmw, X86InstrKind.Vpblendmd, X86InstrKind.Vpblendmq,
                X86InstrKind.Vblendmps, X86InstrKind.Vblendmpd);
            Row(A, "BroadcastPairScalarToVector128", V, i32: X86InstrKind.Vbroadcasti32x2);
            Row(A, "BroadcastPairScalarToVector256", V, i32: X86InstrKind.Vbroadcasti32x2, f32: X86InstrKind.Vbroadcastf32x2);
            Row(A, "BroadcastPairScalarToVector512", V, i32: X86InstrKind.Vbroadcasti32x2, f32: X86InstrKind.Vbroadcastf32x2);
            Sized(A, "BroadcastScalarToVector512", V, X86InstrKind.Vpbroadcastb, X86InstrKind.Vpbroadcastw, X86InstrKind.Vpbroadcastd, X86InstrKind.Vpbroadcastq,
                X86InstrKind.Vbroadcastss, X86InstrKind.Vbroadcastsd);
            Sized(A, "BroadcastVector128ToVector512", V, X86InstrKind.Vbroadcasti32x4, X86InstrKind.Vbroadcasti32x4, X86InstrKind.Vbroadcasti32x4,
                X86InstrKind.Vbroadcasti64x2, X86InstrKind.Vbroadcastf32x4, X86InstrKind.Vbroadcastf64x2);
            Sized(A, "BroadcastVector256ToVector512", V, X86InstrKind.Vbroadcasti32x8, X86InstrKind.Vbroadcasti32x8, X86InstrKind.Vbroadcasti32x8,
                X86InstrKind.Vbroadcasti64x4, X86InstrKind.Vbroadcastf32x8, X86InstrKind.Vbroadcastf64x4);
            Row(A, "Classify", X86IntrinsicForm.MaskCompare, f32: X86InstrKind.Vfpclassps, f64: X86InstrKind.Vfpclasspd);
            Row(A, "ClassifyScalar", X86IntrinsicForm.MaskCompare, f32: X86InstrKind.Vfpclassss, f64: X86InstrKind.Vfpclasssd);
            Row(A, "Compare", X86IntrinsicForm.MaskCompare, f32: X86InstrKind.Cmpps, f64: X86InstrKind.Cmppd, bound: 31);
            // Integer compares map the float predicate to their own
            foreach (var (name, predicate) in VexComparePredicates)
            {
                bool integer = name is not ("CompareOrdered" or "CompareUnordered");
                Row(A, name, X86IntrinsicForm.MaskCompare,
                    s8: integer ? X86InstrKind.Vpcmpb : X86InstrKind.Invalid, u8: integer ? X86InstrKind.Vpcmpub : X86InstrKind.Invalid,
                    s16: integer ? X86InstrKind.Vpcmpw : X86InstrKind.Invalid, u16: integer ? X86InstrKind.Vpcmpuw : X86InstrKind.Invalid,
                    s32: integer ? X86InstrKind.Vpcmpd : X86InstrKind.Invalid, u32: integer ? X86InstrKind.Vpcmpud : X86InstrKind.Invalid,
                    s64: integer ? X86InstrKind.Vpcmpq : X86InstrKind.Invalid, u64: integer ? X86InstrKind.Vpcmpuq : X86InstrKind.Invalid,
                    f32: X86InstrKind.Cmpps, f64: X86InstrKind.Cmppd, flags: Fixed, imm: predicate);
            }
            Sized(A, "Compress", X86IntrinsicForm.MaskedMerge, X86InstrKind.Vpcompressb, X86InstrKind.Vpcompressw, X86InstrKind.Vpcompressd,
                X86InstrKind.Vpcompressq, X86InstrKind.Vcompressps, X86InstrKind.Vcompresspd);
            Sized(A, "CompressStore", X86IntrinsicForm.MaskedStore, X86InstrKind.Vpcompressb, X86InstrKind.Vpcompressw, X86InstrKind.Vpcompressd,
                X86InstrKind.Vpcompressq, X86InstrKind.Vcompressps, X86InstrKind.Vcompresspd, flags: Second);
            foreach (bool x64 in new[] { false, true })
            {
                X86InstrKind I(X86InstrKind instruction, bool wide) => wide == x64 ? instruction : X86InstrKind.Invalid;
                Row(A, "ConvertScalarToVector128Double", V, s32: I(X86InstrKind.Cvtsi2sd, false), u32: I(X86InstrKind.Vcvtusi2sd, false),
                    s64: I(X86InstrKind.Cvtsi2sd, true), u64: I(X86InstrKind.Vcvtusi2sd, true), flags: Second, x64: x64);
                Row(A, "ConvertScalarToVector128Single", V, s32: I(X86InstrKind.Cvtsi2ss, false), u32: I(X86InstrKind.Vcvtusi2ss, false),
                    s64: I(X86InstrKind.Cvtsi2ss, true), u64: I(X86InstrKind.Vcvtusi2ss, true), f64: I(X86InstrKind.Cvtsd2ss, false), flags: Second, x64: x64);
                string bits = x64 ? "64" : "32";
                Row(A, "ConvertToInt" + bits, V, f32: X86InstrKind.Cvtss2si, f64: X86InstrKind.Cvtsd2si, flags: First, x64: x64);
                Row(A, "ConvertToUInt" + bits, V, f32: X86InstrKind.Vcvtss2usi, f64: X86InstrKind.Vcvtsd2usi, flags: First, x64: x64);
                Row(A, "ConvertToUInt" + bits + "WithTruncation", V, f32: X86InstrKind.Vcvttss2usi, f64: X86InstrKind.Vcvttsd2usi, flags: First, x64: x64);
            }
            foreach (string width in new[] { "128", "256" })
            {
                string to = "ConvertToVector" + width;
                Row(A, to + "Byte", V, i16: X86InstrKind.Vpmovwb, i32: X86InstrKind.Vpmovdb, i64: X86InstrKind.Vpmovqb, flags: First);
                Row(A, to + "ByteWithSaturation", V, u16: X86InstrKind.Vpmovuswb, u32: X86InstrKind.Vpmovusdb, u64: X86InstrKind.Vpmovusqb, flags: First);
                Row(A, to + "SByte", V, i16: X86InstrKind.Vpmovwb, i32: X86InstrKind.Vpmovdb, i64: X86InstrKind.Vpmovqb, flags: First);
                Row(A, to + "SByteWithSaturation", V, s16: X86InstrKind.Vpmovswb, s32: X86InstrKind.Vpmovsdb, s64: X86InstrKind.Vpmovsqb, flags: First);
                Row(A, to + "Int16", V, i32: X86InstrKind.Vpmovdw, i64: X86InstrKind.Vpmovqw, flags: First);
                Row(A, to + "Int16WithSaturation", V, s32: X86InstrKind.Vpmovsdw, s64: X86InstrKind.Vpmovsqw, flags: First);
                Row(A, to + "UInt16", V, i32: X86InstrKind.Vpmovdw, i64: X86InstrKind.Vpmovqw, flags: First);
                Row(A, to + "UInt16WithSaturation", V, u32: X86InstrKind.Vpmovusdw, u64: X86InstrKind.Vpmovusqw, flags: First);
                Row(A, to + "Int32", V, i64: X86InstrKind.Vpmovqd, f64: X86InstrKind.Cvtpd2dq, flags: First);
                Row(A, to + "Int32WithSaturation", V, s64: X86InstrKind.Vpmovsqd, flags: First);
                Row(A, to + "Int32WithTruncation", V, f64: X86InstrKind.Cvttpd2dq, flags: First);
                Row(A, to + "UInt32", V, i64: X86InstrKind.Vpmovqd, f32: X86InstrKind.Vcvtps2udq, f64: X86InstrKind.Vcvtpd2udq, flags: First);
                Row(A, to + "UInt32WithSaturation", V, u64: X86InstrKind.Vpmovusqd, flags: First);
                Row(A, to + "UInt32WithTruncation", V, f32: X86InstrKind.Vcvttps2udq, f64: X86InstrKind.Vcvttpd2udq, flags: First);
                Row(A, to + "Double", V, u32: X86InstrKind.Vcvtudq2pd, s64: X86InstrKind.Vcvtqq2pd, u64: X86InstrKind.Vcvtuqq2pd, flags: First);
                Row(A, to + "Single", V, u32: X86InstrKind.Vcvtudq2ps, s64: X86InstrKind.Vcvtqq2ps, u64: X86InstrKind.Vcvtuqq2ps, f64: X86InstrKind.Cvtpd2ps, flags: First);
                Row(A, to + "Int64", V, f32: X86InstrKind.Vcvtps2qq, f64: X86InstrKind.Vcvtpd2qq, flags: First);
                Row(A, to + "Int64WithTruncation", V, f32: X86InstrKind.Vcvttps2qq, f64: X86InstrKind.Vcvttpd2qq, flags: First);
                Row(A, to + "UInt64", V, f32: X86InstrKind.Vcvtps2uqq, f64: X86InstrKind.Vcvtpd2uqq, flags: First);
                Row(A, to + "UInt64WithTruncation", V, f32: X86InstrKind.Vcvttps2uqq, f64: X86InstrKind.Vcvttpd2uqq, flags: First);
            }
            Row(A, "ConvertToVector512Double", V, s32: X86InstrKind.Cvtdq2pd, u32: X86InstrKind.Vcvtudq2pd, s64: X86InstrKind.Vcvtqq2pd, u64: X86InstrKind.Vcvtuqq2pd,
                f32: X86InstrKind.Cvtps2pd, flags: First);
            foreach (string to in new[] { "ConvertToVector512Int16", "ConvertToVector512UInt16" })
                Row(A, to, V, s8: X86InstrKind.Pmovsxbw, u8: X86InstrKind.Pmovzxbw, flags: First);
            Row(A, "ConvertToVector512Int32", V, s8: X86InstrKind.Pmovsxbd, u8: X86InstrKind.Pmovzxbd, s16: X86InstrKind.Pmovsxwd, u16: X86InstrKind.Pmovzxwd,
                f32: X86InstrKind.Cvtps2dq, flags: First);
            Row(A, "ConvertToVector512UInt32", V, s8: X86InstrKind.Pmovsxbd, u8: X86InstrKind.Pmovzxbd, s16: X86InstrKind.Pmovsxwd, u16: X86InstrKind.Pmovzxwd,
                f32: X86InstrKind.Vcvtps2udq, flags: First);
            Row(A, "ConvertToVector512Int32WithTruncation", V, f32: X86InstrKind.Cvttps2dq, flags: First);
            Row(A, "ConvertToVector512UInt32WithTruncation", V, f32: X86InstrKind.Vcvttps2udq, flags: First);
            Row(A, "ConvertToVector512Int64", V, s8: X86InstrKind.Pmovsxbq, u8: X86InstrKind.Pmovzxbq, s16: X86InstrKind.Pmovsxwq, u16: X86InstrKind.Pmovzxwq,
                s32: X86InstrKind.Pmovsxdq, u32: X86InstrKind.Pmovzxdq, f32: X86InstrKind.Vcvtps2qq, f64: X86InstrKind.Vcvtpd2qq, flags: First);
            Row(A, "ConvertToVector512UInt64", V, s8: X86InstrKind.Pmovsxbq, u8: X86InstrKind.Pmovzxbq, s16: X86InstrKind.Pmovsxwq, u16: X86InstrKind.Pmovzxwq,
                s32: X86InstrKind.Pmovsxdq, u32: X86InstrKind.Pmovzxdq, f32: X86InstrKind.Vcvtps2uqq, f64: X86InstrKind.Vcvtpd2uqq, flags: First);
            Row(A, "ConvertToVector512Int64WithTruncation", V, f32: X86InstrKind.Vcvttps2qq, f64: X86InstrKind.Vcvttpd2qq, flags: First);
            Row(A, "ConvertToVector512UInt64WithTruncation", V, f32: X86InstrKind.Vcvttps2uqq, f64: X86InstrKind.Vcvttpd2uqq, flags: First);
            Row(A, "ConvertToVector512Single", V, s32: X86InstrKind.Cvtdq2ps, u32: X86InstrKind.Vcvtudq2ps, flags: First);
            Row(A, "DetectConflicts", V, i32: X86InstrKind.Vpconflictd, i64: X86InstrKind.Vpconflictq);
            Row(A, "Divide", V, f32: X86InstrKind.Divps, f64: X86InstrKind.Divpd);
            Row(A, "DivideScalar", V, f32: X86InstrKind.Divss, f64: X86InstrKind.Divsd);
            Row(A, "DuplicateEvenIndexed", V, f32: X86InstrKind.Movsldup, f64: X86InstrKind.Movddup);
            Row(A, "DuplicateOddIndexed", V, f32: X86InstrKind.Movshdup);
            Sized(A, "Expand", X86IntrinsicForm.MaskedMerge, X86InstrKind.Vpexpandb, X86InstrKind.Vpexpandw, X86InstrKind.Vpexpandd, X86InstrKind.Vpexpandq,
                X86InstrKind.Vexpandps, X86InstrKind.Vexpandpd);
            Sized(A, "ExpandLoad", X86IntrinsicForm.MaskedLoad, X86InstrKind.Vpexpandb, X86InstrKind.Vpexpandw, X86InstrKind.Vpexpandd, X86InstrKind.Vpexpandq,
                X86InstrKind.Vexpandps, X86InstrKind.Vexpandpd);
            Sized(A, "ExtractVector128", V, X86InstrKind.Vextracti32x4, X86InstrKind.Vextracti32x4, X86InstrKind.Vextracti32x4, X86InstrKind.Vextracti64x2,
                X86InstrKind.Vextractf32x4, X86InstrKind.Vextractf64x2);
            Sized(A, "ExtractVector256", V, X86InstrKind.Vextracti32x8, X86InstrKind.Vextracti32x8, X86InstrKind.Vextracti32x8, X86InstrKind.Vextracti64x4,
                X86InstrKind.Vextractf32x8, X86InstrKind.Vextractf64x4);
            Row(A, "Fixup", T, f32: X86InstrKind.Vfixupimmps, f64: X86InstrKind.Vfixupimmpd);
            Row(A, "FixupScalar", T, f32: X86InstrKind.Vfixupimmss, f64: X86InstrKind.Vfixupimmsd);
            foreach (var (name, fma, scalar) in new[]
            {
                ("FusedMultiplyAdd", "Vfmadd213", true), ("FusedMultiplyAddNegated", "Vfnmadd213", true), ("FusedMultiplySubtract", "Vfmsub213", true),
                ("FusedMultiplySubtractNegated", "Vfnmsub213", true), ("FusedMultiplyAddSubtract", "Vfmaddsub213", false),
                ("FusedMultiplySubtractAdd", "Vfmsubadd213", false),
            })
            {
                Row(A, name, T, f32: Enum.Parse<X86InstrKind>(fma + "ps"), f64: Enum.Parse<X86InstrKind>(fma + "pd"));
                if (scalar)
                    Row(A, name + "Scalar", T, f32: Enum.Parse<X86InstrKind>(fma + "ss"), f64: Enum.Parse<X86InstrKind>(fma + "sd"));
            }
            Row(A, "GetExponent", V, f32: X86InstrKind.Vgetexpps, f64: X86InstrKind.Vgetexppd);
            Row(A, "GetExponentScalar", V, f32: X86InstrKind.Vgetexpss, f64: X86InstrKind.Vgetexpsd);
            Row(A, "GetMantissa", V, f32: X86InstrKind.Vgetmantps, f64: X86InstrKind.Vgetmantpd, bound: 15);
            Row(A, "GetMantissaScalar", V, f32: X86InstrKind.Vgetmantss, f64: X86InstrKind.Vgetmantsd, bound: 15);
            Sized(A, "InsertVector128", V, X86InstrKind.Vinserti32x4, X86InstrKind.Vinserti32x4, X86InstrKind.Vinserti32x4, X86InstrKind.Vinserti64x2,
                X86InstrKind.Vinsertf32x4, X86InstrKind.Vinsertf64x2);
            Sized(A, "InsertVector256", V, X86InstrKind.Vinserti32x8, X86InstrKind.Vinserti32x8, X86InstrKind.Vinserti32x8, X86InstrKind.Vinserti64x4,
                X86InstrKind.Vinsertf32x8, X86InstrKind.Vinsertf64x4);
            Row(A, "LeadingZeroCount", V, i32: X86InstrKind.Vplzcntd, i64: X86InstrKind.Vplzcntq);
            Row(A, "LoadAlignedVector512", V, any: X86InstrKind.Movaps);
            Integers(A, "LoadAlignedVector512NonTemporal", V, X86InstrKind.Movntdqa, X86InstrKind.Movntdqa, X86InstrKind.Movntdqa, X86InstrKind.Movntdqa);
            Row(A, "LoadVector512", V, any: X86InstrKind.Movups);
            Sized(A, "MaskLoad", X86IntrinsicForm.MaskedLoad, X86InstrKind.Vmovdqu8, X86InstrKind.Vmovdqu16, X86InstrKind.Vmovdqu32, X86InstrKind.Vmovdqu64,
                X86InstrKind.Movups, X86InstrKind.Movupd);
            Sized(A, "MaskLoadAligned", X86IntrinsicForm.MaskedLoad, X86InstrKind.Invalid, X86InstrKind.Invalid, X86InstrKind.Vmovdqa32, X86InstrKind.Vmovdqa64,
                X86InstrKind.Movaps, X86InstrKind.Movapd);
            Sized(A, "MaskStore", X86IntrinsicForm.MaskedStore, X86InstrKind.Vmovdqu8, X86InstrKind.Vmovdqu16, X86InstrKind.Vmovdqu32, X86InstrKind.Vmovdqu64,
                X86InstrKind.Movups, X86InstrKind.Movupd, flags: Second);
            Sized(A, "MaskStoreAligned", X86IntrinsicForm.MaskedStore, X86InstrKind.Invalid, X86InstrKind.Invalid, X86InstrKind.Vmovdqa32, X86InstrKind.Vmovdqa64,
                X86InstrKind.Movaps, X86InstrKind.Movapd, flags: Second);
            Row(A, "Max", V, s8: X86InstrKind.Pmaxsb, u8: X86InstrKind.Pmaxub, s16: X86InstrKind.Pmaxsw, u16: X86InstrKind.Pmaxuw, s32: X86InstrKind.Pmaxsd,
                u32: X86InstrKind.Pmaxud, s64: X86InstrKind.Vpmaxsq, u64: X86InstrKind.Vpmaxuq, f32: X86InstrKind.Maxps, f64: X86InstrKind.Maxpd);
            Row(A, "Min", V, s8: X86InstrKind.Pminsb, u8: X86InstrKind.Pminub, s16: X86InstrKind.Pminsw, u16: X86InstrKind.Pminuw, s32: X86InstrKind.Pminsd,
                u32: X86InstrKind.Pminud, s64: X86InstrKind.Vpminsq, u64: X86InstrKind.Vpminuq, f32: X86InstrKind.Minps, f64: X86InstrKind.Minpd);
            Sized(A, "MoveMask", X86IntrinsicForm.MaskToBits, X86InstrKind.Vpmovb2m, X86InstrKind.Vpmovw2m, X86InstrKind.Vpmovd2m, X86InstrKind.Vpmovq2m,
                X86InstrKind.Vpmovd2m, X86InstrKind.Vpmovq2m, flags: First);
            Row(A, "MultiShift", V, i64: X86InstrKind.Vpmultishiftqb, flags: Second);
            Row(A, "Multiply", V, s64: X86InstrKind.Pmuldq, u64: X86InstrKind.Pmuludq, f32: X86InstrKind.Mulps, f64: X86InstrKind.Mulpd);
            Row(A, "MultiplyAddAdjacent", V, s16: X86InstrKind.Pmaddubsw, s32: X86InstrKind.Pmaddwd);
            Row(A, "MultiplyHigh", V, s16: X86InstrKind.Pmulhw, u16: X86InstrKind.Pmulhuw);
            Row(A, "MultiplyHighRoundScale", V, s16: X86InstrKind.Pmulhrsw);
            Row(A, "MultiplyLow", V, i16: X86InstrKind.Pmullw, i32: X86InstrKind.Pmulld, i64: X86InstrKind.Vpmullq);
            Row(A, "MultiplyScalar", V, f32: X86InstrKind.Mulss, f64: X86InstrKind.Mulsd);
            Sized(A, "Or", V, X86InstrKind.Por, X86InstrKind.Por, X86InstrKind.Por, X86InstrKind.Vporq, X86InstrKind.Orps, X86InstrKind.Orpd);
            Row(A, "PackSignedSaturate", V, s8: X86InstrKind.Packsswb, s16: X86InstrKind.Packssdw);
            Row(A, "PackUnsignedSaturate", V, u8: X86InstrKind.Packuswb, u16: X86InstrKind.Packusdw);
            Row(A, "Permute2x64", V, f64: X86InstrKind.Vpermilpd);
            Row(A, "Permute4x32", V, f32: X86InstrKind.Vpermilps);
            Row(A, "Permute4x64", V, i64: X86InstrKind.Vpermq, f64: X86InstrKind.Vpermpd);
            Row(A, "PermuteVar2x64", V, f64: X86InstrKind.Vpermilpd);
            Row(A, "PermuteVar4x32", V, f32: X86InstrKind.Vpermilps);
            foreach (string name in new[] { "PermuteVar16x8", "PermuteVar32x8", "PermuteVar64x8" })
            {
                Row(A, name, V, i8: X86InstrKind.Vpermb, flags: Swap);
                Row(A, name + "x2", T, i8: X86InstrKind.Vpermt2b);
            }
            foreach (string name in new[] { "PermuteVar8x16", "PermuteVar16x16", "PermuteVar32x16" })
            {
                Row(A, name, V, i16: X86InstrKind.Vpermw, flags: Swap);
                Row(A, name + "x2", T, i16: X86InstrKind.Vpermt2w);
            }
            Row(A, "PermuteVar16x32", V, i32: X86InstrKind.Vpermd, f32: X86InstrKind.Vpermps, flags: Swap);
            foreach (string name in new[] { "PermuteVar4x32x2", "PermuteVar8x32x2", "PermuteVar16x32x2" })
                Row(A, name, T, i32: X86InstrKind.Vpermt2d, f32: X86InstrKind.Vpermt2ps);
            foreach (string name in new[] { "PermuteVar4x64", "PermuteVar8x64" })
                Row(A, name, V, i64: X86InstrKind.Vpermq, f64: X86InstrKind.Vpermpd, flags: Swap);
            foreach (string name in new[] { "PermuteVar2x64x2", "PermuteVar4x64x2", "PermuteVar8x64x2" })
                Row(A, name, T, i64: X86InstrKind.Vpermt2q, f64: X86InstrKind.Vpermt2pd);
            Row(A, "Range", V, f32: X86InstrKind.Vrangeps, f64: X86InstrKind.Vrangepd, bound: 15);
            Row(A, "RangeScalar", V, f32: X86InstrKind.Vrangess, f64: X86InstrKind.Vrangesd, bound: 15);
            Row(A, "Reciprocal14", V, f32: X86InstrKind.Vrcp14ps, f64: X86InstrKind.Vrcp14pd);
            Row(A, "Reciprocal14Scalar", V, f32: X86InstrKind.Vrcp14ss, f64: X86InstrKind.Vrcp14sd);
            Row(A, "ReciprocalSqrt14", V, f32: X86InstrKind.Vrsqrt14ps, f64: X86InstrKind.Vrsqrt14pd);
            Row(A, "ReciprocalSqrt14Scalar", V, f32: X86InstrKind.Vrsqrt14ss, f64: X86InstrKind.Vrsqrt14sd);
            Row(A, "Reduce", V, f32: X86InstrKind.Vreduceps, f64: X86InstrKind.Vreducepd);
            Row(A, "ReduceScalar", V, f32: X86InstrKind.Vreducess, f64: X86InstrKind.Vreducesd);
            Row(A, "RotateLeft", V, i32: X86InstrKind.Vprold, i64: X86InstrKind.Vprolq);
            Row(A, "RotateLeftVariable", V, i32: X86InstrKind.Vprolvd, i64: X86InstrKind.Vprolvq);
            Row(A, "RotateRight", V, i32: X86InstrKind.Vprord, i64: X86InstrKind.Vprorq);
            Row(A, "RotateRightVariable", V, i32: X86InstrKind.Vprorvd, i64: X86InstrKind.Vprorvq);
            Row(A, "RoundScale", V, f32: X86InstrKind.Vrndscaleps, f64: X86InstrKind.Vrndscalepd);
            Row(A, "RoundScaleScalar", V, f32: X86InstrKind.Vrndscaless, f64: X86InstrKind.Vrndscalesd);
            Row(A, "Scale", V, f32: X86InstrKind.Vscalefps, f64: X86InstrKind.Vscalefpd);
            Row(A, "ScaleScalar", V, f32: X86InstrKind.Vscalefss, f64: X86InstrKind.Vscalefsd);
            Row(A, "ShiftLeftLogical", V, i16: X86InstrKind.Psllw, i32: X86InstrKind.Pslld, i64: X86InstrKind.Psllq);
            Integers(A, "ShiftLeftLogical128BitLane", V, X86InstrKind.Pslldq, X86InstrKind.Pslldq, X86InstrKind.Pslldq, X86InstrKind.Pslldq);
            Row(A, "ShiftLeftLogicalVariable", V, i16: X86InstrKind.Vpsllvw, i32: X86InstrKind.Vpsllvd, i64: X86InstrKind.Vpsllvq);
            Row(A, "ShiftRightArithmetic", V, s16: X86InstrKind.Psraw, s32: X86InstrKind.Psrad, s64: X86InstrKind.Vpsraq);
            Row(A, "ShiftRightArithmeticVariable", V, s16: X86InstrKind.Vpsravw, s32: X86InstrKind.Vpsravd, s64: X86InstrKind.Vpsravq);
            Row(A, "ShiftRightLogical", V, i16: X86InstrKind.Psrlw, i32: X86InstrKind.Psrld, i64: X86InstrKind.Psrlq);
            Integers(A, "ShiftRightLogical128BitLane", V, X86InstrKind.Psrldq, X86InstrKind.Psrldq, X86InstrKind.Psrldq, X86InstrKind.Psrldq);
            Row(A, "ShiftRightLogicalVariable", V, i16: X86InstrKind.Vpsrlvw, i32: X86InstrKind.Vpsrlvd, i64: X86InstrKind.Vpsrlvq);
            Row(A, "Shuffle", V, i8: X86InstrKind.Pshufb, i32: X86InstrKind.Pshufd, f32: X86InstrKind.Shufps, f64: X86InstrKind.Shufpd);
            foreach (string name in new[] { "Shuffle2x128", "Shuffle4x128" })
            {
                Row(A, name, V, i32: X86InstrKind.Vshufi32x4, i64: X86InstrKind.Vshufi64x2, f32: X86InstrKind.Vshuff32x4, f64: X86InstrKind.Vshuff64x2);
            }
            Row(A, "ShuffleHigh", V, i16: X86InstrKind.Pshufhw);
            Row(A, "ShuffleLow", V, i16: X86InstrKind.Pshuflw);
            Row(A, "Sqrt", V, f32: X86InstrKind.Sqrtps, f64: X86InstrKind.Sqrtpd);
            Row(A, "SqrtScalar", V, f32: X86InstrKind.Sqrtss, f64: X86InstrKind.Sqrtsd);
            Row(A, "Store", VS, any: X86InstrKind.Movups);
            Row(A, "StoreAligned", VS, any: X86InstrKind.Movaps);
            Row(A, "StoreAlignedNonTemporal", VS, i8: X86InstrKind.Movntdq, i16: X86InstrKind.Movntdq, i32: X86InstrKind.Movntdq, i64: X86InstrKind.Movntdq,
                f32: X86InstrKind.Movntps, f64: X86InstrKind.Movntpd, flags: Second);
            Sized(A, "Subtract", V, X86InstrKind.Psubb, X86InstrKind.Psubw, X86InstrKind.Psubd, X86InstrKind.Psubq, X86InstrKind.Subps, X86InstrKind.Subpd);
            Row(A, "SubtractSaturate", V, s8: X86InstrKind.Psubsb, u8: X86InstrKind.Psubusb, s16: X86InstrKind.Psubsw, u16: X86InstrKind.Psubusw);
            Row(A, "SubtractScalar", V, f32: X86InstrKind.Subss, f64: X86InstrKind.Subsd);
            Row(A, "SumAbsoluteDifferences", V, u16: X86InstrKind.Psadbw);
            Row(A, "SumAbsoluteDifferencesInBlock32", V, u16: X86InstrKind.Vdbpsadbw);
            Sized(A, "TernaryLogic", T, X86InstrKind.Vpternlogd, X86InstrKind.Vpternlogd, X86InstrKind.Vpternlogd, X86InstrKind.Vpternlogq,
                X86InstrKind.Vpternlogd, X86InstrKind.Vpternlogq);
            Sized(A, "UnpackHigh", V, X86InstrKind.Punpckhbw, X86InstrKind.Punpckhwd, X86InstrKind.Punpckhdq, X86InstrKind.Punpckhqdq,
                X86InstrKind.Unpckhps, X86InstrKind.Unpckhpd);
            Sized(A, "UnpackLow", V, X86InstrKind.Punpcklbw, X86InstrKind.Punpcklwd, X86InstrKind.Punpckldq, X86InstrKind.Punpcklqdq,
                X86InstrKind.Unpcklps, X86InstrKind.Unpcklpd);
            Sized(A, "Xor", V, X86InstrKind.Pxor, X86InstrKind.Pxor, X86InstrKind.Pxor, X86InstrKind.Vpxorq, X86InstrKind.Xorps, X86InstrKind.Xorpd);
        }
    }
}
