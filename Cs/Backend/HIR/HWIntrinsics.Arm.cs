using Cnidaria.Arm;
using System;
using System.Collections.Generic;
using I = Cnidaria.Arm.ArmInstrKind;

namespace Cnidaria.Cs;

internal enum ArmIsa : byte
{
    ArmBase,
    AdvSimd,
    Aes,
    Crc32,
    Dp,
    Rdm,
    Sha1,
    Sha256,
}

internal enum ArmIntrinsicCategory : byte
{
    Simd,
    ByElement,
    ShiftLeft,
    ShiftRight,
    Load,
    Store,
    Scalar,
    Helper,
    Special,
}

[Flags]
internal enum ArmIntrinsicFlags : ushort
{
    None = 0,
    Commutative = 1 << 0,
    BaseFromFirstArg = 1 << 1,
    BaseFromSecondArg = 1 << 2,
    Immediate = 1 << 3,
    ReadModifyWrite = 1 << 4,
    Scalar = 1 << 5,
    SpecialCodeGen = 1 << 6,
    NoJumpTable = 1 << 7,
    Tuple = 1 << 8,
    NoCodeGen = 1 << 9,
}

internal sealed class ArmIntrinsicInfo
{
    public ArmIntrinsicInfo(ArmIsa isa, bool arm64, string name, int simdSize, int argCount, ArmIntrinsicCategory category, ArmIntrinsicFlags flags, ArmInstrKind[] instructions)
    {
        Isa = isa;
        Arm64 = arm64;
        Name = name;
        SimdSize = simdSize;
        ArgCount = argCount;
        Category = category;
        Flags = flags;
        Instructions = instructions;
    }

    public ArmIsa Isa { get; }
    public bool Arm64 { get; }
    public string Name { get; }
    public int SimdSize { get; }
    public int ArgCount { get; }
    public ArmIntrinsicCategory Category { get; }
    public ArmIntrinsicFlags Flags { get; }
    public ArmInstrKind[] Instructions { get; }

    public bool Has(ArmIntrinsicFlags flag) => (Flags & flag) != 0;

    public ArmInstrKind Instruction(RuntimePrimitiveKind kind)
    {
        int index = X86Intrinsics.KindIndex(kind);
        return index >= 0 ? Instructions[index] : ArmInstrKind.Invalid;
    }

    public override string ToString() => (Arm64 ? Isa + ".Arm64." : Isa + ".") + Name;
}

// Int64 holds the element kind and width, the address operand mask from bit 16, bit 24 for a possibly variable immediate and immediate bounds from bit 32
internal static class ArmIntrinsics
{
    public const int FirstId = 0x1000;
    public const string Namespace = "System.Runtime.Intrinsics.Arm";
    private const long VariableImmediateBit = 1L << 24;
    private const int BoundsShift = 32;
    private const int TupleShift = 50;

    private static readonly List<ArmIntrinsicInfo> Rows = new List<ArmIntrinsicInfo>();
    private static readonly Dictionary<(ArmIsa, bool, string), int> Lookup = new Dictionary<(ArmIsa, bool, string), int>();

    static ArmIntrinsics() => Build();

    public static bool IsArm(GenTree node) => node.Int32 >= FirstId;

    public static ArmIntrinsicInfo Info(GenTree node) => Rows[node.Int32 - FirstId];

    public static ArmIntrinsicInfo Info(int id) => Rows[id - FirstId];

    public static long Encode(RuntimePrimitiveKind baseKind, int simdSize, int addressMask, bool variableImmediate, ReadOnlySpan<(int Minimum, int Maximum)> bounds)
    {
        long encoded = HWIntrinsics.Encode(baseKind, simdSize) | ((long)addressMask << 16) | (variableImmediate ? VariableImmediateBit : 0);
        for (int i = 0; i < bounds.Length; i++)
            encoded |= ((long)(bounds[i].Maximum & 0xFF) | ((long)bounds[i].Minimum << 8)) << (BoundsShift + 9 * i);
        return encoded;
    }

    // A tuple of vectors travels as its elements, and a tuple result is one node per element
    public static long EncodeTuple(long encoded, int count, int element)
        => encoded | ((long)count << TupleShift) | ((long)element << (TupleShift + 3));

    public static int TupleCount(GenTree node) => (int)((node.Int64 >> TupleShift) & 7);

    public static int TupleElement(GenTree node) => (int)((node.Int64 >> (TupleShift + 3)) & 7);

    public static (int Minimum, int Maximum) ImmediateBounds(GenTree node, int operandIndex)
    {
        ReadOnlySpan<int> immediates = ImmediateOperands(Info(node), node.Operands.Length);
        int slot = immediates.IndexOf(operandIndex);
        long bits = node.Int64 >> (BoundsShift + 9 * slot);
        return ((int)((bits >> 8) & 1), (int)(bits & 0xFF));
    }

    public static bool IsConstantImmediate(GenTree node, int operandIndex)
    {
        GenTree operand = node.Operands[operandIndex];
        (int minimum, int maximum) = ImmediateBounds(node, operandIndex);
        return operand.Kind == GenTreeKind.ConstI4 && operand.Int32 >= minimum && operand.Int32 <= maximum;
    }

    private static int AddressMask(GenTree node) => (int)((node.Int64 >> 16) & 0xFF);

    public static bool IsAddressOperand(GenTree node, int operandIndex) => (AddressMask(node) & (1 << operandIndex)) != 0;

    public static bool MayHaveVariableImmediate(GenTree node) => (node.Int64 & VariableImmediateBit) != 0;

    public static bool HasVariableImmediate(GenTree node)
    {
        foreach (int index in ImmediateOperands(Info(node), node.Operands.Length))
        {
            if (!IsConstantImmediate(node, index))
                return true;
        }
        return false;
    }

    public static bool TryGetIsa(RuntimeType type, out ArmIsa isa, out bool arm64)
    {
        isa = default;
        arm64 = false;
        if (type.Namespace != Namespace)
            return false;
        string name = type.Name;
        int nested = name.IndexOf('+');
        if (nested >= 0)
        {
            if (name.Substring(nested + 1) != "Arm64")
                return false;
            arm64 = true;
            name = name.Substring(0, nested);
        }
        return Enum.TryParse(name, ignoreCase: false, out isa) && Enum.IsDefined(isa);
    }

    public static bool IsSupported(TargetInfo target, ArmIsa isa)
    {
        if (target.Architecture != TargetArchitectureKind.Arm64)
            return false;
        bool Has(TargetArchitectureFeatures feature) => HWIntrinsics.HasFeature(target, feature);
        return isa switch
        {
            ArmIsa.ArmBase or ArmIsa.AdvSimd => true,
            ArmIsa.Aes => Has(TargetArchitectureFeatures.ArmAes),
            ArmIsa.Crc32 => Has(TargetArchitectureFeatures.ArmCrc32),
            ArmIsa.Dp => Has(TargetArchitectureFeatures.ArmDp),
            ArmIsa.Rdm => Has(TargetArchitectureFeatures.ArmRdm),
            ArmIsa.Sha1 => Has(TargetArchitectureFeatures.ArmSha1),
            ArmIsa.Sha256 => Has(TargetArchitectureFeatures.ArmSha256),
            _ => false,
        };
    }

    public static bool TryLookup(ArmIsa isa, bool arm64, string name, out int id)
    {
        if (Lookup.TryGetValue((isa, arm64, name), out int row))
        {
            id = FirstId + row;
            return true;
        }
        id = 0;
        return false;
    }

    public static ReadOnlySpan<int> ImmediateOperands(ArmIntrinsicInfo info, int operandCount)
    {
        if (!info.Has(ArmIntrinsicFlags.Immediate))
            return ReadOnlySpan<int>.Empty;
        return info.Name switch
        {
            "Insert" or "InsertScalar" or "LoadAndInsertScalar" => new[] { 1 },
            "InsertSelectedScalar" => new[] { 1, 3 },
            _ when info.Name.StartsWith("LoadAndInsertScalarVector", StringComparison.Ordinal) => new[] { operandCount - 2 },
            _ => new[] { operandCount - 1 },
        };
    }

    public static bool IsImmediateOperand(GenTree node, int operandIndex)
    {
        foreach (int index in ImmediateOperands(Info(node), node.Operands.Length))
        {
            if (index == operandIndex)
                return true;
        }
        return false;
    }

    // Shifts take a count of the shifted element's bits, the others a lane of the vector before them
    public static (int Minimum, int Maximum) ImmediateRange(ArmIntrinsicInfo info, IReadOnlyList<RuntimeType?> operandTypes, RuntimeType? resultType, int operandIndex, TargetInfo target)
    {
        int ElementBits(RuntimeType? type)
            => type is null ? 0 : 8 * HWIntrinsics.ElementSize(HWIntrinsics.NormalizeBaseKind(SimdTypes.IsSimdType(type, target) ? SimdTypes.ElementType(type).PrimitiveKind : type.PrimitiveKind, target));

        switch (info.Category)
        {
            case ArmIntrinsicCategory.ShiftLeft:
                return (0, ElementBits(operandTypes[0]) - 1);
            case ArmIntrinsicCategory.ShiftRight:
                return (1, ElementBits(resultType));
        }
        RuntimeType? vector = operandTypes[operandIndex - 1];
        if (vector is null || !SimdTypes.IsSimdType(vector, target))
            return (0, -1);
        int lanes = SimdTypes.SizeOf(SimdTypes.KindOf(vector, target)) * 8 / ElementBits(vector);
        if (info.Name == "DotProductBySelectedQuadruplet")
            lanes /= 4;
        return (0, lanes - 1);
    }

    public static bool IsReadModifyWrite(GenTree node)
        => Info(node).Has(ArmIntrinsicFlags.ReadModifyWrite) || IsMixedSaturate(node);

    public static bool IsMixedSaturate(GenTree node)
        => Info(node).Name is "AddSaturate" or "AddSaturateScalar" && node.Operands.Length == 2 &&
           HWIntrinsics.IsUnsigned(ElementKind(node.Operands[0].Type)) != HWIntrinsics.IsUnsigned(ElementKind(node.Operands[1].Type));

    public static bool UsesRegisterShift(GenTree node)
        => HasVariableImmediate(node) && Info(node).Has(ArmIntrinsicFlags.NoJumpTable) &&
           Info(node).Category is ArmIntrinsicCategory.ShiftLeft or ArmIntrinsicCategory.ShiftRight;

    public static bool IsIndexedElementOperand(GenTree node, int operandIndex)
        => Info(node).Category == ArmIntrinsicCategory.ByElement && operandIndex == IndexedElementOperand(node);

    public static int IndexedElementOperand(GenTree node)
        => node.Operands.Length - (Info(node).Has(ArmIntrinsicFlags.Immediate) ? 2 : 1);

    public static RuntimePrimitiveKind ElementKind(RuntimeType? type)
        => type is null ? RuntimePrimitiveKind.None
            : type.Kind == RuntimeTypeKind.Pointer && type.ElementType is RuntimeType pointee ? pointee.PrimitiveKind
            : type.GenericTypeArguments.Length == 1 ? type.GenericTypeArguments[0].PrimitiveKind
            : type.PrimitiveKind;

    public static bool TouchesMemory(GenTree node)
        => Info(node).Category is ArmIntrinsicCategory.Load or ArmIntrinsicCategory.Store || Info(node).Name == "Yield" || AddressMask(node) != 0;

    public static GenTreeFlags EffectFlags(GenTree node)
    {
        ArmIntrinsicInfo info = Info(node);
        GenTreeFlags range = MayHaveVariableImmediate(node) ? GenTreeFlags.CanThrow : GenTreeFlags.None;
        if (info.Category == ArmIntrinsicCategory.Store)
            return GenTreeFlags.SideEffect | GenTreeFlags.MemoryWrite | GenTreeFlags.Indirect | GenTreeFlags.CanThrow | GenTreeFlags.Ordered;
        if (info.Name == "Yield")
            return GenTreeFlags.SideEffect | GenTreeFlags.Ordered;
        if (AddressMask(node) != 0)
            return GenTreeFlags.MemoryRead | GenTreeFlags.Indirect | GenTreeFlags.CanThrow;
        return range;
    }

    public static string Format(GenTree node) => $"{Info(node)}<{HWIntrinsics.BaseKind(node)}>";

    private static void Row(ArmIsa isa, bool arm64, string name, int simdSize, int argCount, ArmIntrinsicCategory category, ArmIntrinsicFlags flags, params ArmInstrKind[] instructions)
    {
        Lookup.Add((isa, arm64, name), Rows.Count);
        Rows.Add(new ArmIntrinsicInfo(isa, arm64, name, simdSize, argCount, category, flags, instructions));
    }

    private static void Build()
    {
        const ArmIsa ArmBase = ArmIsa.ArmBase, AdvSimd = ArmIsa.AdvSimd, Aes = ArmIsa.Aes, Crc32 = ArmIsa.Crc32, Dp = ArmIsa.Dp;
        const ArmIsa Rdm = ArmIsa.Rdm, Sha1 = ArmIsa.Sha1, Sha256 = ArmIsa.Sha256;
        const ArmIntrinsicCategory S = ArmIntrinsicCategory.Simd, E = ArmIntrinsicCategory.ByElement, L = ArmIntrinsicCategory.ShiftLeft;
        const ArmIntrinsicCategory R = ArmIntrinsicCategory.ShiftRight, Ld = ArmIntrinsicCategory.Load, St = ArmIntrinsicCategory.Store;
        const ArmIntrinsicCategory G = ArmIntrinsicCategory.Scalar, H = ArmIntrinsicCategory.Helper, X = ArmIntrinsicCategory.Special;
        const ArmIntrinsicFlags Comm = ArmIntrinsicFlags.Commutative, First = ArmIntrinsicFlags.BaseFromFirstArg, Second = ArmIntrinsicFlags.BaseFromSecondArg;
        const ArmIntrinsicFlags Imm = ArmIntrinsicFlags.Immediate, Rmw = ArmIntrinsicFlags.ReadModifyWrite, Sc = ArmIntrinsicFlags.Scalar;
        const ArmIntrinsicFlags Spec = ArmIntrinsicFlags.SpecialCodeGen, NoTable = ArmIntrinsicFlags.NoJumpTable;
        const ArmIntrinsicFlags Tuple = ArmIntrinsicFlags.Tuple, NoGen = ArmIntrinsicFlags.NoCodeGen;
        const ArmInstrKind N = ArmInstrKind.Invalid;

        Row(AdvSimd, false, "Abs", -1, 1, S, First, I.Abs, N, I.Abs, N, I.Abs, N, N, N, I.Fabs, N);
        Row(AdvSimd, false, "AbsSaturate", -1, 1, S, First, I.Sqabs, N, I.Sqabs, N, I.Sqabs, N, N, N, N, N);
        Row(AdvSimd, false, "AbsScalar", 8, 1, S, Sc, N, N, N, N, N, N, N, N, I.Fabs, I.Fabs);
        Row(AdvSimd, false, "AbsoluteCompareGreaterThan", -1, 2, S, 0, N, N, N, N, N, N, N, N, I.Facgt, N);
        Row(AdvSimd, false, "AbsoluteCompareGreaterThanOrEqual", -1, 2, S, 0, N, N, N, N, N, N, N, N, I.Facge, N);
        Row(AdvSimd, false, "AbsoluteCompareLessThan", -1, 2, S, Spec, N, N, N, N, N, N, N, N, I.Facgt, N);
        Row(AdvSimd, false, "AbsoluteCompareLessThanOrEqual", -1, 2, S, Spec, N, N, N, N, N, N, N, N, I.Facge, N);
        Row(AdvSimd, false, "AbsoluteDifference", -1, 2, S, First | Comm, I.Sabd, I.Uabd, I.Sabd, I.Uabd, I.Sabd, I.Uabd, N, N, I.Fabd, N);
        Row(AdvSimd, false, "AbsoluteDifferenceAdd", -1, 3, S, Rmw, I.Saba, I.Uaba, I.Saba, I.Uaba, I.Saba, I.Uaba, N, N, N, N);
        Row(AdvSimd, false, "AbsoluteDifferenceWideningLower", 8, 2, S, First | Comm, I.Sabdl, I.Uabdl, I.Sabdl, I.Uabdl, I.Sabdl, I.Uabdl, N, N, N, N);
        Row(AdvSimd, false, "AbsoluteDifferenceWideningLowerAndAdd", 8, 3, S, Second | Rmw, I.Sabal, I.Uabal, I.Sabal, I.Uabal, I.Sabal, I.Uabal, N, N, N, N);
        Row(AdvSimd, false, "AbsoluteDifferenceWideningUpper", 16, 2, S, First | Comm, I.Sabdl, I.Uabdl, I.Sabdl, I.Uabdl, I.Sabdl, I.Uabdl, N, N, N, N);
        Row(AdvSimd, false, "AbsoluteDifferenceWideningUpperAndAdd", 16, 3, S, Second | Rmw, I.Sabal, I.Uabal, I.Sabal, I.Uabal, I.Sabal, I.Uabal, N, N, N, N);
        Row(AdvSimd, false, "Add", -1, 2, S, Comm, I.Add, I.Add, I.Add, I.Add, I.Add, I.Add, I.Add, I.Add, I.Fadd, N);
        Row(AdvSimd, false, "AddHighNarrowingLower", 8, 2, S, Comm, I.Addhn, I.Addhn, I.Addhn, I.Addhn, I.Addhn, I.Addhn, N, N, N, N);
        Row(AdvSimd, false, "AddHighNarrowingUpper", 16, 3, S, Rmw, I.Addhn, I.Addhn, I.Addhn, I.Addhn, I.Addhn, I.Addhn, N, N, N, N);
        Row(AdvSimd, false, "AddPairwise", 8, 2, S, 0, I.Addp, I.Addp, I.Addp, I.Addp, I.Addp, I.Addp, N, N, I.Faddp, N);
        Row(AdvSimd, false, "AddPairwiseWidening", -1, 1, S, First, I.Saddlp, I.Uaddlp, I.Saddlp, I.Uaddlp, I.Saddlp, I.Uaddlp, N, N, N, N);
        Row(AdvSimd, false, "AddPairwiseWideningAndAdd", -1, 2, S, Second | Rmw, I.Sadalp, I.Uadalp, I.Sadalp, I.Uadalp, I.Sadalp, I.Uadalp, N, N, N, N);
        Row(AdvSimd, false, "AddPairwiseWideningAndAddScalar", 8, 2, S, Second | Rmw, N, N, N, N, I.Sadalp, I.Uadalp, N, N, N, N);
        Row(AdvSimd, false, "AddPairwiseWideningScalar", 8, 1, S, First, N, N, N, N, I.Saddlp, I.Uaddlp, N, N, N, N);
        Row(AdvSimd, false, "AddRoundedHighNarrowingLower", 8, 2, S, Comm, I.Raddhn, I.Raddhn, I.Raddhn, I.Raddhn, I.Raddhn, I.Raddhn, N, N, N, N);
        Row(AdvSimd, false, "AddRoundedHighNarrowingUpper", 16, 3, S, Rmw, I.Raddhn, I.Raddhn, I.Raddhn, I.Raddhn, I.Raddhn, I.Raddhn, N, N, N, N);
        Row(AdvSimd, false, "AddSaturate", -1, 2, S, Comm, I.Sqadd, I.Uqadd, I.Sqadd, I.Uqadd, I.Sqadd, I.Uqadd, I.Sqadd, I.Uqadd, N, N);
        Row(AdvSimd, false, "AddSaturateScalar", 8, 2, S, Comm | Sc, N, N, N, N, N, N, I.Sqadd, I.Uqadd, N, N);
        Row(AdvSimd, false, "AddScalar", 8, 2, S, Comm | Sc, N, N, N, N, N, N, I.Add, I.Add, I.Fadd, I.Fadd);
        Row(AdvSimd, false, "AddWideningLower", 8, 2, S, Second | Spec, I.Saddl, I.Uaddl, I.Saddl, I.Uaddl, I.Saddl, I.Uaddl, I.Saddw, I.Uaddw, N, N);
        Row(AdvSimd, false, "AddWideningUpper", 16, 2, S, Second | Spec, I.Saddl, I.Uaddl, I.Saddl, I.Uaddl, I.Saddl, I.Uaddl, I.Saddw, I.Uaddw, N, N);
        Row(AdvSimd, false, "And", -1, 2, S, Comm, I.And, I.And, I.And, I.And, I.And, I.And, I.And, I.And, I.And, I.And);
        Row(AdvSimd, false, "BitwiseClear", -1, 2, S, 0, I.Bic, I.Bic, I.Bic, I.Bic, I.Bic, I.Bic, I.Bic, I.Bic, I.Bic, I.Bic);
        Row(AdvSimd, false, "BitwiseSelect", -1, 3, S, Spec, I.Bsl, I.Bsl, I.Bsl, I.Bsl, I.Bsl, I.Bsl, I.Bsl, I.Bsl, I.Bsl, I.Bsl);
        Row(AdvSimd, false, "Ceiling", -1, 1, S, 0, N, N, N, N, N, N, N, N, I.Frintp, N);
        Row(AdvSimd, false, "CeilingScalar", 8, 1, S, Sc, N, N, N, N, N, N, N, N, I.Frintp, I.Frintp);
        Row(AdvSimd, false, "CompareEqual", -1, 2, S, Comm, I.Cmeq, I.Cmeq, I.Cmeq, I.Cmeq, I.Cmeq, I.Cmeq, N, N, I.Fcmeq, N);
        Row(AdvSimd, false, "CompareGreaterThan", -1, 2, S, 0, I.Cmgt, I.Cmhi, I.Cmgt, I.Cmhi, I.Cmgt, I.Cmhi, N, N, I.Fcmgt, N);
        Row(AdvSimd, false, "CompareGreaterThanOrEqual", -1, 2, S, 0, I.Cmge, I.Cmhs, I.Cmge, I.Cmhs, I.Cmge, I.Cmhs, N, N, I.Fcmge, N);
        Row(AdvSimd, false, "CompareLessThan", -1, 2, S, Spec, I.Cmgt, I.Cmhi, I.Cmgt, I.Cmhi, I.Cmgt, I.Cmhi, N, N, I.Fcmgt, N);
        Row(AdvSimd, false, "CompareLessThanOrEqual", -1, 2, S, Spec, I.Cmge, I.Cmhs, I.Cmge, I.Cmhs, I.Cmge, I.Cmhs, N, N, I.Fcmge, N);
        Row(AdvSimd, false, "CompareTest", -1, 2, S, Comm, I.Cmtst, I.Cmtst, I.Cmtst, I.Cmtst, I.Cmtst, I.Cmtst, N, N, I.Cmtst, N);
        Row(AdvSimd, false, "ConvertToInt32RoundAwayFromZero", -1, 1, S, First, N, N, N, N, N, N, N, N, I.Fcvtas, N);
        Row(AdvSimd, false, "ConvertToInt32RoundAwayFromZeroScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, N, N, I.Fcvtas, N);
        Row(AdvSimd, false, "ConvertToInt32RoundToEven", -1, 1, S, First, N, N, N, N, N, N, N, N, I.Fcvtns, N);
        Row(AdvSimd, false, "ConvertToInt32RoundToEvenScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, N, N, I.Fcvtns, N);
        Row(AdvSimd, false, "ConvertToInt32RoundToNegativeInfinity", -1, 1, S, First, N, N, N, N, N, N, N, N, I.Fcvtms, N);
        Row(AdvSimd, false, "ConvertToInt32RoundToNegativeInfinityScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, N, N, I.Fcvtms, N);
        Row(AdvSimd, false, "ConvertToInt32RoundToPositiveInfinity", -1, 1, S, First, N, N, N, N, N, N, N, N, I.Fcvtps, N);
        Row(AdvSimd, false, "ConvertToInt32RoundToPositiveInfinityScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, N, N, I.Fcvtps, N);
        Row(AdvSimd, false, "ConvertToInt32RoundToZero", -1, 1, S, First, N, N, N, N, N, N, N, N, I.Fcvtzs, N);
        Row(AdvSimd, false, "ConvertToInt32RoundToZeroScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, N, N, I.Fcvtzs, N);
        Row(AdvSimd, false, "ConvertToSingle", -1, 1, S, First, N, N, N, N, I.Scvtf, I.Ucvtf, N, N, N, N);
        Row(AdvSimd, false, "ConvertToSingleScalar", 8, 1, S, First | Sc, N, N, N, N, I.Scvtf, I.Ucvtf, N, N, N, N);
        Row(AdvSimd, false, "ConvertToUInt32RoundAwayFromZero", -1, 1, S, First, N, N, N, N, N, N, N, N, I.Fcvtau, N);
        Row(AdvSimd, false, "ConvertToUInt32RoundAwayFromZeroScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, N, N, I.Fcvtau, N);
        Row(AdvSimd, false, "ConvertToUInt32RoundToEven", -1, 1, S, First, N, N, N, N, N, N, N, N, I.Fcvtnu, N);
        Row(AdvSimd, false, "ConvertToUInt32RoundToEvenScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, N, N, I.Fcvtnu, N);
        Row(AdvSimd, false, "ConvertToUInt32RoundToNegativeInfinity", -1, 1, S, First, N, N, N, N, N, N, N, N, I.Fcvtmu, N);
        Row(AdvSimd, false, "ConvertToUInt32RoundToNegativeInfinityScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, N, N, I.Fcvtmu, N);
        Row(AdvSimd, false, "ConvertToUInt32RoundToPositiveInfinity", -1, 1, S, First, N, N, N, N, N, N, N, N, I.Fcvtpu, N);
        Row(AdvSimd, false, "ConvertToUInt32RoundToPositiveInfinityScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, N, N, I.Fcvtpu, N);
        Row(AdvSimd, false, "ConvertToUInt32RoundToZero", -1, 1, S, First, N, N, N, N, N, N, N, N, I.Fcvtzu, N);
        Row(AdvSimd, false, "ConvertToUInt32RoundToZeroScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, N, N, I.Fcvtzu, N);
        Row(AdvSimd, false, "DivideScalar", 8, 2, S, Sc, N, N, N, N, N, N, N, N, I.Fdiv, I.Fdiv);
        Row(AdvSimd, false, "DuplicateSelectedScalarToVector128", -1, 2, S, First | Imm | Spec, I.Dup, I.Dup, I.Dup, I.Dup, I.Dup, I.Dup, N, N, I.Dup, N);
        Row(AdvSimd, false, "DuplicateSelectedScalarToVector64", -1, 2, S, First | Imm | Spec, I.Dup, I.Dup, I.Dup, I.Dup, I.Dup, I.Dup, N, N, I.Dup, N);
        Row(AdvSimd, false, "DuplicateToVector128", 16, 1, S, Spec, I.Dup, I.Dup, I.Dup, I.Dup, I.Dup, I.Dup, N, N, I.Dup, N);
        Row(AdvSimd, false, "DuplicateToVector64", 8, 1, S, Spec, I.Dup, I.Dup, I.Dup, I.Dup, I.Dup, I.Dup, N, N, I.Dup, N);
        Row(AdvSimd, false, "Extract", -1, 2, S, First | Imm | Spec, I.Smov, I.Umov, I.Smov, I.Umov, I.Smov, I.Umov, I.Umov, I.Umov, I.Dup, I.Dup);
        Row(AdvSimd, false, "ExtractNarrowingLower", 8, 1, S, 0, I.Xtn, I.Xtn, I.Xtn, I.Xtn, I.Xtn, I.Xtn, N, N, N, N);
        Row(AdvSimd, false, "ExtractNarrowingSaturateLower", 8, 1, S, 0, I.Sqxtn, I.Uqxtn, I.Sqxtn, I.Uqxtn, I.Sqxtn, I.Uqxtn, N, N, N, N);
        Row(AdvSimd, false, "ExtractNarrowingSaturateUnsignedLower", 8, 1, S, 0, N, I.Sqxtun, N, I.Sqxtun, N, I.Sqxtun, N, N, N, N);
        Row(AdvSimd, false, "ExtractNarrowingSaturateUnsignedUpper", 16, 2, S, Rmw, N, I.Sqxtun, N, I.Sqxtun, N, I.Sqxtun, N, N, N, N);
        Row(AdvSimd, false, "ExtractNarrowingSaturateUpper", 16, 2, S, Rmw, I.Sqxtn, I.Uqxtn, I.Sqxtn, I.Uqxtn, I.Sqxtn, I.Uqxtn, N, N, N, N);
        Row(AdvSimd, false, "ExtractNarrowingUpper", 16, 2, S, Rmw, I.Xtn, I.Xtn, I.Xtn, I.Xtn, I.Xtn, I.Xtn, N, N, N, N);
        Row(AdvSimd, false, "ExtractVector128", 16, 3, S, Imm | Spec, I.Ext, I.Ext, I.Ext, I.Ext, I.Ext, I.Ext, I.Ext, I.Ext, I.Ext, I.Ext);
        Row(AdvSimd, false, "ExtractVector64", 8, 3, S, Imm | Spec, I.Ext, I.Ext, I.Ext, I.Ext, I.Ext, I.Ext, N, N, I.Ext, N);
        Row(AdvSimd, false, "Floor", -1, 1, S, 0, N, N, N, N, N, N, N, N, I.Frintm, N);
        Row(AdvSimd, false, "FloorScalar", 8, 1, S, Sc, N, N, N, N, N, N, N, N, I.Frintm, I.Frintm);
        Row(AdvSimd, false, "FusedAddHalving", -1, 2, S, Comm, I.Shadd, I.Uhadd, I.Shadd, I.Uhadd, I.Shadd, I.Uhadd, N, N, N, N);
        Row(AdvSimd, false, "FusedAddRoundedHalving", -1, 2, S, Comm, I.Srhadd, I.Urhadd, I.Srhadd, I.Urhadd, I.Srhadd, I.Urhadd, N, N, N, N);
        Row(AdvSimd, false, "FusedMultiplyAdd", -1, 3, S, Rmw, N, N, N, N, N, N, N, N, I.Fmla, N);
        Row(AdvSimd, false, "FusedMultiplyAddNegatedScalar", 8, 3, S, Sc | Spec, N, N, N, N, N, N, N, N, I.Fnmadd, I.Fnmadd);
        Row(AdvSimd, false, "FusedMultiplyAddScalar", 8, 3, S, Sc | Spec, N, N, N, N, N, N, N, N, I.Fmadd, I.Fmadd);
        Row(AdvSimd, false, "FusedMultiplySubtract", -1, 3, S, Rmw, N, N, N, N, N, N, N, N, I.Fmls, N);
        Row(AdvSimd, false, "FusedMultiplySubtractNegatedScalar", 8, 3, S, Sc | Spec, N, N, N, N, N, N, N, N, I.Fnmsub, I.Fnmsub);
        Row(AdvSimd, false, "FusedMultiplySubtractScalar", 8, 3, S, Sc | Spec, N, N, N, N, N, N, N, N, I.Fmsub, I.Fmsub);
        Row(AdvSimd, false, "FusedSubtractHalving", -1, 2, S, 0, I.Shsub, I.Uhsub, I.Shsub, I.Uhsub, I.Shsub, I.Uhsub, N, N, N, N);
        Row(AdvSimd, false, "Insert", -1, 3, S, Imm | Rmw | Sc | Spec, I.Ins, I.Ins, I.Ins, I.Ins, I.Ins, I.Ins, I.Ins, I.Ins, I.Ins, I.Ins);
        Row(AdvSimd, false, "InsertScalar", 16, 3, S, Imm | Rmw | Sc | Spec, N, N, N, N, N, N, I.Ins, I.Ins, N, I.Ins);
        Row(AdvSimd, false, "LeadingSignCount", -1, 1, S, 0, I.Cls, N, I.Cls, N, I.Cls, N, N, N, N, N);
        Row(AdvSimd, false, "LeadingZeroCount", -1, 1, S, 0, I.Clz, I.Clz, I.Clz, I.Clz, I.Clz, I.Clz, N, N, N, N);
        Row(AdvSimd, false, "Load2xVector64", 8, 1, Ld, First | Tuple, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, N, N, I.Ld1, N);
        Row(AdvSimd, false, "Load2xVector64AndUnzip", 8, 1, Ld, First | Tuple, I.Ld2, I.Ld2, I.Ld2, I.Ld2, I.Ld2, I.Ld2, N, N, I.Ld2, N);
        Row(AdvSimd, false, "Load3xVector64", 8, 1, Ld, First | Tuple, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, N, N, I.Ld1, N);
        Row(AdvSimd, false, "Load3xVector64AndUnzip", 8, 1, Ld, First | Tuple, I.Ld3, I.Ld3, I.Ld3, I.Ld3, I.Ld3, I.Ld3, N, N, I.Ld3, N);
        Row(AdvSimd, false, "Load4xVector64", 8, 1, Ld, First | Tuple, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, N, N, I.Ld1, N);
        Row(AdvSimd, false, "Load4xVector64AndUnzip", 8, 1, Ld, First | Tuple, I.Ld4, I.Ld4, I.Ld4, I.Ld4, I.Ld4, I.Ld4, N, N, I.Ld4, N);
        Row(AdvSimd, false, "LoadAndInsertScalar", -1, 3, Ld, Imm | Rmw | Sc | Spec, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1);
        Row(AdvSimd, false, "LoadAndInsertScalarVector64x2", 8, 3, Ld, Imm | Rmw | Sc | Spec | Tuple, I.Ld2, I.Ld2, I.Ld2, I.Ld2, I.Ld2, I.Ld2, N, N, I.Ld2, N);
        Row(AdvSimd, false, "LoadAndInsertScalarVector64x3", 8, 3, Ld, Imm | Rmw | Sc | Spec | Tuple, I.Ld3, I.Ld3, I.Ld3, I.Ld3, I.Ld3, I.Ld3, N, N, I.Ld3, N);
        Row(AdvSimd, false, "LoadAndInsertScalarVector64x4", 8, 3, Ld, Imm | Rmw | Sc | Spec | Tuple, I.Ld4, I.Ld4, I.Ld4, I.Ld4, I.Ld4, I.Ld4, N, N, I.Ld4, N);
        Row(AdvSimd, false, "LoadAndReplicateToVector128", 16, 1, Ld, 0, I.Ld1r, I.Ld1r, I.Ld1r, I.Ld1r, I.Ld1r, I.Ld1r, N, N, I.Ld1r, N);
        Row(AdvSimd, false, "LoadAndReplicateToVector64", 8, 1, Ld, 0, I.Ld1r, I.Ld1r, I.Ld1r, I.Ld1r, I.Ld1r, I.Ld1r, N, N, I.Ld1r, N);
        Row(AdvSimd, false, "LoadAndReplicateToVector64x2", 8, 1, Ld, First | Tuple, I.Ld2r, I.Ld2r, I.Ld2r, I.Ld2r, I.Ld2r, I.Ld2r, N, N, I.Ld2r, N);
        Row(AdvSimd, false, "LoadAndReplicateToVector64x3", 8, 1, Ld, First | Tuple, I.Ld3r, I.Ld3r, I.Ld3r, I.Ld3r, I.Ld3r, I.Ld3r, N, N, I.Ld3r, N);
        Row(AdvSimd, false, "LoadAndReplicateToVector64x4", 8, 1, Ld, First | Tuple, I.Ld4r, I.Ld4r, I.Ld4r, I.Ld4r, I.Ld4r, I.Ld4r, N, N, I.Ld4r, N);
        Row(AdvSimd, false, "LoadVector128", 16, 1, H, 0, N, N, N, N, N, N, N, N, N, N);
        Row(AdvSimd, false, "LoadVector64", 8, 1, H, 0, N, N, N, N, N, N, N, N, N, N);
        Row(AdvSimd, false, "Max", -1, 2, S, Comm, I.Smax, I.Umax, I.Smax, I.Umax, I.Smax, I.Umax, N, N, I.Fmax, N);
        Row(AdvSimd, false, "MaxNumber", -1, 2, S, Comm, N, N, N, N, N, N, N, N, I.Fmaxnm, N);
        Row(AdvSimd, false, "MaxNumberScalar", 8, 2, S, Comm | Sc, N, N, N, N, N, N, N, N, I.Fmaxnm, I.Fmaxnm);
        Row(AdvSimd, false, "MaxPairwise", 8, 2, S, 0, I.Smaxp, I.Umaxp, I.Smaxp, I.Umaxp, I.Smaxp, I.Umaxp, N, N, I.Fmaxp, N);
        Row(AdvSimd, false, "Min", -1, 2, S, Comm, I.Smin, I.Umin, I.Smin, I.Umin, I.Smin, I.Umin, N, N, I.Fmin, N);
        Row(AdvSimd, false, "MinNumber", -1, 2, S, Comm, N, N, N, N, N, N, N, N, I.Fminnm, N);
        Row(AdvSimd, false, "MinNumberScalar", 8, 2, S, Comm | Sc, N, N, N, N, N, N, N, N, I.Fminnm, I.Fminnm);
        Row(AdvSimd, false, "MinPairwise", 8, 2, S, 0, I.Sminp, I.Uminp, I.Sminp, I.Uminp, I.Sminp, I.Uminp, N, N, I.Fminp, N);
        Row(AdvSimd, false, "Multiply", -1, 2, S, Comm, I.Mul, I.Mul, I.Mul, I.Mul, I.Mul, I.Mul, N, N, I.Fmul, N);
        Row(AdvSimd, false, "MultiplyAdd", -1, 3, S, Rmw, I.Mla, I.Mla, I.Mla, I.Mla, I.Mla, I.Mla, N, N, N, N);
        Row(AdvSimd, false, "MultiplyAddByScalar", -1, 3, E, Rmw, N, N, I.Mla, I.Mla, I.Mla, I.Mla, N, N, N, N);
        Row(AdvSimd, false, "MultiplyAddBySelectedScalar", -1, 4, E, Imm | Rmw, N, N, I.Mla, I.Mla, I.Mla, I.Mla, N, N, N, N);
        Row(AdvSimd, false, "MultiplyByScalar", -1, 2, E, 0, N, N, I.Mul, I.Mul, I.Mul, I.Mul, N, N, I.Fmul, N);
        Row(AdvSimd, false, "MultiplyBySelectedScalar", -1, 3, E, Imm, N, N, I.Mul, I.Mul, I.Mul, I.Mul, N, N, I.Fmul, N);
        Row(AdvSimd, false, "MultiplyBySelectedScalarWideningLower", 8, 3, E, First | Imm, N, N, I.Smull, I.Umull, I.Smull, I.Umull, N, N, N, N);
        Row(AdvSimd, false, "MultiplyBySelectedScalarWideningLowerAndAdd", 8, 4, E, Second | Imm | Rmw, N, N, I.Smlal, I.Umlal, I.Smlal, I.Umlal, N, N, N, N);
        Row(AdvSimd, false, "MultiplyBySelectedScalarWideningLowerAndSubtract", 8, 4, E, Second | Imm | Rmw, N, N, I.Smlsl, I.Umlsl, I.Smlsl, I.Umlsl, N, N, N, N);
        Row(AdvSimd, false, "MultiplyBySelectedScalarWideningUpper", 16, 3, E, First | Imm, N, N, I.Smull, I.Umull, I.Smull, I.Umull, N, N, N, N);
        Row(AdvSimd, false, "MultiplyBySelectedScalarWideningUpperAndAdd", 16, 4, E, Second | Imm | Rmw, N, N, I.Smlal, I.Umlal, I.Smlal, I.Umlal, N, N, N, N);
        Row(AdvSimd, false, "MultiplyBySelectedScalarWideningUpperAndSubtract", 16, 4, E, Second | Imm | Rmw, N, N, I.Smlsl, I.Umlsl, I.Smlsl, I.Umlsl, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingByScalarSaturateHigh", -1, 2, E, 0, N, N, I.Sqdmulh, N, I.Sqdmulh, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingBySelectedScalarSaturateHigh", -1, 3, E, Imm, N, N, I.Sqdmulh, N, I.Sqdmulh, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingSaturateHigh", -1, 2, S, Comm, N, N, I.Sqdmulh, N, I.Sqdmulh, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingWideningLowerAndAddSaturate", 8, 3, S, Second | Rmw, N, N, I.Sqdmlal, N, I.Sqdmlal, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingWideningLowerAndSubtractSaturate", 8, 3, S, Second | Rmw, N, N, I.Sqdmlsl, N, I.Sqdmlsl, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingWideningLowerByScalarAndAddSaturate", 8, 3, E, Second | Rmw, N, N, I.Sqdmlal, N, I.Sqdmlal, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingWideningLowerByScalarAndSubtractSaturate", 8, 3, E, Second | Rmw, N, N, I.Sqdmlsl, N, I.Sqdmlsl, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingWideningLowerBySelectedScalarAndAddSaturate", 8, 4, E, Second | Imm | Rmw, N, N, I.Sqdmlal, N, I.Sqdmlal, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingWideningLowerBySelectedScalarAndSubtractSaturate", 8, 4, E, Second | Imm | Rmw, N, N, I.Sqdmlsl, N, I.Sqdmlsl, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingWideningSaturateLower", 8, 2, S, First | Comm, N, N, I.Sqdmull, N, I.Sqdmull, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingWideningSaturateLowerByScalar", 8, 2, E, First, N, N, I.Sqdmull, N, I.Sqdmull, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingWideningSaturateLowerBySelectedScalar", 8, 3, E, First | Imm, N, N, I.Sqdmull, N, I.Sqdmull, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingWideningSaturateUpper", 16, 2, S, First | Comm, N, N, I.Sqdmull, N, I.Sqdmull, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingWideningSaturateUpperByScalar", 16, 2, E, First, N, N, I.Sqdmull, N, I.Sqdmull, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingWideningSaturateUpperBySelectedScalar", 16, 3, E, First | Imm, N, N, I.Sqdmull, N, I.Sqdmull, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingWideningUpperAndAddSaturate", 16, 3, S, Second | Rmw, N, N, I.Sqdmlal, N, I.Sqdmlal, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingWideningUpperAndSubtractSaturate", 16, 3, S, Second | Rmw, N, N, I.Sqdmlsl, N, I.Sqdmlsl, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingWideningUpperByScalarAndAddSaturate", 16, 3, E, Second | Rmw, N, N, I.Sqdmlal, N, I.Sqdmlal, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingWideningUpperByScalarAndSubtractSaturate", 16, 3, E, Second | Rmw, N, N, I.Sqdmlsl, N, I.Sqdmlsl, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingWideningUpperBySelectedScalarAndAddSaturate", 16, 4, E, Second | Imm | Rmw, N, N, I.Sqdmlal, N, I.Sqdmlal, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyDoublingWideningUpperBySelectedScalarAndSubtractSaturate", 16, 4, E, Second | Imm | Rmw, N, N, I.Sqdmlsl, N, I.Sqdmlsl, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyRoundedDoublingByScalarSaturateHigh", -1, 2, E, 0, N, N, I.Sqrdmulh, N, I.Sqrdmulh, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyRoundedDoublingBySelectedScalarSaturateHigh", -1, 3, E, Imm, N, N, I.Sqrdmulh, N, I.Sqrdmulh, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyRoundedDoublingSaturateHigh", -1, 2, S, Comm, N, N, I.Sqrdmulh, N, I.Sqrdmulh, N, N, N, N, N);
        Row(AdvSimd, false, "MultiplyScalar", 8, 2, S, Comm | Sc, N, N, N, N, N, N, N, N, I.Fmul, I.Fmul);
        Row(AdvSimd, false, "MultiplyScalarBySelectedScalar", 8, 3, E, Imm | Sc, N, N, N, N, N, N, N, N, I.Fmul, N);
        Row(AdvSimd, false, "MultiplySubtract", -1, 3, S, Rmw, I.Mls, I.Mls, I.Mls, I.Mls, I.Mls, I.Mls, N, N, N, N);
        Row(AdvSimd, false, "MultiplySubtractByScalar", -1, 3, E, Rmw, N, N, I.Mls, I.Mls, I.Mls, I.Mls, N, N, N, N);
        Row(AdvSimd, false, "MultiplySubtractBySelectedScalar", -1, 4, E, Imm | Rmw, N, N, I.Mls, I.Mls, I.Mls, I.Mls, N, N, N, N);
        Row(AdvSimd, false, "MultiplyWideningLower", 8, 2, S, First | Comm, I.Smull, I.Umull, I.Smull, I.Umull, I.Smull, I.Umull, N, N, N, N);
        Row(AdvSimd, false, "MultiplyWideningLowerAndAdd", 8, 3, S, Second | Rmw, I.Smlal, I.Umlal, I.Smlal, I.Umlal, I.Smlal, I.Umlal, N, N, N, N);
        Row(AdvSimd, false, "MultiplyWideningLowerAndSubtract", 8, 3, S, Second | Rmw, I.Smlsl, I.Umlsl, I.Smlsl, I.Umlsl, I.Smlsl, I.Umlsl, N, N, N, N);
        Row(AdvSimd, false, "MultiplyWideningUpper", 16, 2, S, First | Comm, I.Smull, I.Umull, I.Smull, I.Umull, I.Smull, I.Umull, N, N, N, N);
        Row(AdvSimd, false, "MultiplyWideningUpperAndAdd", 16, 3, S, Second | Rmw, I.Smlal, I.Umlal, I.Smlal, I.Umlal, I.Smlal, I.Umlal, N, N, N, N);
        Row(AdvSimd, false, "MultiplyWideningUpperAndSubtract", 16, 3, S, Second | Rmw, I.Smlsl, I.Umlsl, I.Smlsl, I.Umlsl, I.Smlsl, I.Umlsl, N, N, N, N);
        Row(AdvSimd, false, "Negate", -1, 1, S, 0, I.Neg, N, I.Neg, N, I.Neg, N, N, N, I.Fneg, N);
        Row(AdvSimd, false, "NegateSaturate", -1, 1, S, 0, I.Sqneg, N, I.Sqneg, N, I.Sqneg, N, N, N, N, N);
        Row(AdvSimd, false, "NegateScalar", 8, 1, S, Sc, N, N, N, N, N, N, N, N, I.Fneg, I.Fneg);
        Row(AdvSimd, false, "Not", -1, 1, S, 0, I.Not, I.Not, I.Not, I.Not, I.Not, I.Not, I.Not, I.Not, I.Not, I.Not);
        Row(AdvSimd, false, "Or", -1, 2, S, Comm, I.Orr, I.Orr, I.Orr, I.Orr, I.Orr, I.Orr, I.Orr, I.Orr, I.Orr, I.Orr);
        Row(AdvSimd, false, "OrNot", -1, 2, S, 0, I.Orn, I.Orn, I.Orn, I.Orn, I.Orn, I.Orn, I.Orn, I.Orn, I.Orn, I.Orn);
        Row(AdvSimd, false, "PolynomialMultiply", -1, 2, S, Comm, I.Pmul, I.Pmul, N, N, N, N, N, N, N, N);
        Row(AdvSimd, false, "PolynomialMultiplyWideningLower", 8, 2, S, First | Comm, I.Pmull, I.Pmull, N, N, N, N, N, N, N, N);
        Row(AdvSimd, false, "PolynomialMultiplyWideningUpper", 16, 2, S, First | Comm, I.Pmull, I.Pmull, N, N, N, N, N, N, N, N);
        Row(AdvSimd, false, "PopCount", -1, 1, S, 0, I.Cnt, I.Cnt, N, N, N, N, N, N, N, N);
        Row(AdvSimd, false, "ReciprocalEstimate", -1, 1, S, 0, N, N, N, N, N, I.Urecpe, N, N, I.Frecpe, N);
        Row(AdvSimd, false, "ReciprocalSquareRootEstimate", -1, 1, S, 0, N, N, N, N, N, I.Ursqrte, N, N, I.Frsqrte, N);
        Row(AdvSimd, false, "ReciprocalSquareRootStep", -1, 2, S, Comm, N, N, N, N, N, N, N, N, I.Frsqrts, N);
        Row(AdvSimd, false, "ReciprocalStep", -1, 2, S, Comm, N, N, N, N, N, N, N, N, I.Frecps, N);
        Row(AdvSimd, false, "ReverseElement16", -1, 1, S, Spec, N, N, N, N, I.Rev32, I.Rev32, I.Rev64, I.Rev64, N, N);
        Row(AdvSimd, false, "ReverseElement32", -1, 1, S, Spec, N, N, N, N, N, N, I.Rev64, I.Rev64, N, N);
        Row(AdvSimd, false, "ReverseElement8", -1, 1, S, Spec, N, N, I.Rev16, I.Rev16, I.Rev32, I.Rev32, I.Rev64, I.Rev64, N, N);
        Row(AdvSimd, false, "RoundAwayFromZero", -1, 1, S, 0, N, N, N, N, N, N, N, N, I.Frinta, N);
        Row(AdvSimd, false, "RoundAwayFromZeroScalar", 8, 1, S, Sc, N, N, N, N, N, N, N, N, I.Frinta, I.Frinta);
        Row(AdvSimd, false, "RoundToNearest", -1, 1, S, 0, N, N, N, N, N, N, N, N, I.Frintn, N);
        Row(AdvSimd, false, "RoundToNearestScalar", 8, 1, S, Sc, N, N, N, N, N, N, N, N, I.Frintn, I.Frintn);
        Row(AdvSimd, false, "RoundToNegativeInfinity", -1, 1, S, 0, N, N, N, N, N, N, N, N, I.Frintm, N);
        Row(AdvSimd, false, "RoundToNegativeInfinityScalar", 8, 1, S, Sc, N, N, N, N, N, N, N, N, I.Frintm, I.Frintm);
        Row(AdvSimd, false, "RoundToPositiveInfinity", -1, 1, S, 0, N, N, N, N, N, N, N, N, I.Frintp, N);
        Row(AdvSimd, false, "RoundToPositiveInfinityScalar", 8, 1, S, Sc, N, N, N, N, N, N, N, N, I.Frintp, I.Frintp);
        Row(AdvSimd, false, "RoundToZero", -1, 1, S, 0, N, N, N, N, N, N, N, N, I.Frintz, N);
        Row(AdvSimd, false, "RoundToZeroScalar", 8, 1, S, Sc, N, N, N, N, N, N, N, N, I.Frintz, I.Frintz);
        Row(AdvSimd, false, "ShiftArithmetic", -1, 2, S, 0, I.Sshl, N, I.Sshl, N, I.Sshl, N, I.Sshl, N, N, N);
        Row(AdvSimd, false, "ShiftArithmeticRounded", -1, 2, S, 0, I.Srshl, N, I.Srshl, N, I.Srshl, N, I.Srshl, N, N, N);
        Row(AdvSimd, false, "ShiftArithmeticRoundedSaturate", -1, 2, S, 0, I.Sqrshl, N, I.Sqrshl, N, I.Sqrshl, N, I.Sqrshl, N, N, N);
        Row(AdvSimd, false, "ShiftArithmeticRoundedSaturateScalar", 8, 2, S, Sc, N, N, N, N, N, N, I.Sqrshl, N, N, N);
        Row(AdvSimd, false, "ShiftArithmeticRoundedScalar", 8, 2, S, Sc, N, N, N, N, N, N, I.Srshl, N, N, N);
        Row(AdvSimd, false, "ShiftArithmeticSaturate", -1, 2, S, 0, I.Sqshl, N, I.Sqshl, N, I.Sqshl, N, I.Sqshl, N, N, N);
        Row(AdvSimd, false, "ShiftArithmeticSaturateScalar", 8, 2, S, Sc, N, N, N, N, N, N, I.Sqshl, N, N, N);
        Row(AdvSimd, false, "ShiftArithmeticScalar", 8, 2, S, Sc, N, N, N, N, N, N, I.Sshl, N, N, N);
        Row(AdvSimd, false, "ShiftLeftAndInsert", -1, 3, L, Imm | Rmw, I.Sli, I.Sli, I.Sli, I.Sli, I.Sli, I.Sli, I.Sli, I.Sli, I.Sli, I.Sli);
        Row(AdvSimd, false, "ShiftLeftAndInsertScalar", 8, 3, L, Imm | Rmw | Sc, N, N, N, N, N, N, I.Sli, I.Sli, N, N);
        Row(AdvSimd, false, "ShiftLeftLogical", -1, 2, L, Imm | NoTable, I.Shl, I.Shl, I.Shl, I.Shl, I.Shl, I.Shl, I.Shl, I.Shl, N, N);
        Row(AdvSimd, false, "ShiftLeftLogicalSaturate", -1, 2, L, Imm, I.Sqshl, I.Uqshl, I.Sqshl, I.Uqshl, I.Sqshl, I.Uqshl, I.Sqshl, I.Uqshl, N, N);
        Row(AdvSimd, false, "ShiftLeftLogicalSaturateScalar", 8, 2, L, Imm | Sc, N, N, N, N, N, N, I.Sqshl, I.Uqshl, N, N);
        Row(AdvSimd, false, "ShiftLeftLogicalSaturateUnsigned", -1, 2, L, First | Imm, I.Sqshlu, N, I.Sqshlu, N, I.Sqshlu, N, I.Sqshlu, N, N, N);
        Row(AdvSimd, false, "ShiftLeftLogicalSaturateUnsignedScalar", 8, 2, L, First | Imm | Sc, N, N, N, N, N, N, I.Sqshlu, N, N, N);
        Row(AdvSimd, false, "ShiftLeftLogicalScalar", 8, 2, L, Imm | Sc | NoTable, N, N, N, N, N, N, I.Shl, I.Shl, N, N);
        Row(AdvSimd, false, "ShiftLeftLogicalWideningLower", 8, 2, L, First | Imm, I.Sshll, I.Ushll, I.Sshll, I.Ushll, I.Sshll, I.Ushll, N, N, N, N);
        Row(AdvSimd, false, "ShiftLeftLogicalWideningUpper", 16, 2, L, First | Imm, I.Sshll, I.Ushll, I.Sshll, I.Ushll, I.Sshll, I.Ushll, N, N, N, N);
        Row(AdvSimd, false, "ShiftLogical", -1, 2, S, 0, I.Ushl, I.Ushl, I.Ushl, I.Ushl, I.Ushl, I.Ushl, I.Ushl, I.Ushl, N, N);
        Row(AdvSimd, false, "ShiftLogicalRounded", -1, 2, S, 0, I.Urshl, I.Urshl, I.Urshl, I.Urshl, I.Urshl, I.Urshl, I.Urshl, I.Urshl, N, N);
        Row(AdvSimd, false, "ShiftLogicalRoundedSaturate", -1, 2, S, 0, I.Uqrshl, I.Uqrshl, I.Uqrshl, I.Uqrshl, I.Uqrshl, I.Uqrshl, I.Uqrshl, I.Uqrshl, N, N);
        Row(AdvSimd, false, "ShiftLogicalRoundedSaturateScalar", 8, 2, S, Sc, N, N, N, N, N, N, I.Uqrshl, I.Uqrshl, N, N);
        Row(AdvSimd, false, "ShiftLogicalRoundedScalar", 8, 2, S, Sc, N, N, N, N, N, N, I.Urshl, I.Urshl, N, N);
        Row(AdvSimd, false, "ShiftLogicalSaturate", -1, 2, S, 0, I.Uqshl, I.Uqshl, I.Uqshl, I.Uqshl, I.Uqshl, I.Uqshl, I.Uqshl, I.Uqshl, N, N);
        Row(AdvSimd, false, "ShiftLogicalSaturateScalar", 8, 2, S, Sc, N, N, N, N, N, N, I.Uqshl, I.Uqshl, N, N);
        Row(AdvSimd, false, "ShiftLogicalScalar", 8, 2, S, Sc, N, N, N, N, N, N, I.Ushl, I.Ushl, N, N);
        Row(AdvSimd, false, "ShiftRightAndInsert", -1, 3, R, Imm | Rmw, I.Sri, I.Sri, I.Sri, I.Sri, I.Sri, I.Sri, I.Sri, I.Sri, I.Sri, I.Sri);
        Row(AdvSimd, false, "ShiftRightAndInsertScalar", 8, 3, R, Imm | Rmw | Sc, N, N, N, N, N, N, I.Sri, I.Sri, N, N);
        Row(AdvSimd, false, "ShiftRightArithmetic", -1, 2, R, Imm | NoTable, I.Sshr, N, I.Sshr, N, I.Sshr, N, I.Sshr, N, N, N);
        Row(AdvSimd, false, "ShiftRightArithmeticAdd", -1, 3, R, Imm | Rmw, I.Ssra, N, I.Ssra, N, I.Ssra, N, I.Ssra, N, N, N);
        Row(AdvSimd, false, "ShiftRightArithmeticAddScalar", 8, 3, R, Imm | Rmw | Sc, N, N, N, N, N, N, I.Ssra, N, N, N);
        Row(AdvSimd, false, "ShiftRightArithmeticNarrowingSaturateLower", 8, 2, R, Imm, I.Sqshrn, N, I.Sqshrn, N, I.Sqshrn, N, N, N, N, N);
        Row(AdvSimd, false, "ShiftRightArithmeticNarrowingSaturateUnsignedLower", 8, 2, R, Imm, N, I.Sqshrun, N, I.Sqshrun, N, I.Sqshrun, N, N, N, N);
        Row(AdvSimd, false, "ShiftRightArithmeticNarrowingSaturateUnsignedUpper", 16, 3, R, Imm | Rmw, N, I.Sqshrun, N, I.Sqshrun, N, I.Sqshrun, N, N, N, N);
        Row(AdvSimd, false, "ShiftRightArithmeticNarrowingSaturateUpper", 16, 3, R, Imm | Rmw, I.Sqshrn, N, I.Sqshrn, N, I.Sqshrn, N, N, N, N, N);
        Row(AdvSimd, false, "ShiftRightArithmeticRounded", -1, 2, R, Imm, I.Srshr, N, I.Srshr, N, I.Srshr, N, I.Srshr, N, N, N);
        Row(AdvSimd, false, "ShiftRightArithmeticRoundedAdd", -1, 3, R, Imm | Rmw, I.Srsra, N, I.Srsra, N, I.Srsra, N, I.Srsra, N, N, N);
        Row(AdvSimd, false, "ShiftRightArithmeticRoundedAddScalar", 8, 3, R, Imm | Rmw | Sc, N, N, N, N, N, N, I.Srsra, N, N, N);
        Row(AdvSimd, false, "ShiftRightArithmeticRoundedNarrowingSaturateLower", 8, 2, R, Imm, I.Sqrshrn, N, I.Sqrshrn, N, I.Sqrshrn, N, N, N, N, N);
        Row(AdvSimd, false, "ShiftRightArithmeticRoundedNarrowingSaturateUnsignedLower", 8, 2, R, Imm, N, I.Sqrshrun, N, I.Sqrshrun, N, I.Sqrshrun, N, N, N, N);
        Row(AdvSimd, false, "ShiftRightArithmeticRoundedNarrowingSaturateUnsignedUpper", 16, 3, R, Imm | Rmw, N, I.Sqrshrun, N, I.Sqrshrun, N, I.Sqrshrun, N, N, N, N);
        Row(AdvSimd, false, "ShiftRightArithmeticRoundedNarrowingSaturateUpper", 16, 3, R, Imm | Rmw, I.Sqrshrn, N, I.Sqrshrn, N, I.Sqrshrn, N, N, N, N, N);
        Row(AdvSimd, false, "ShiftRightArithmeticRoundedScalar", 8, 2, R, Imm | Sc, N, N, N, N, N, N, I.Srshr, N, N, N);
        Row(AdvSimd, false, "ShiftRightArithmeticScalar", 8, 2, R, Imm | Sc | NoTable, N, N, N, N, N, N, I.Sshr, N, N, N);
        Row(AdvSimd, false, "ShiftRightLogical", -1, 2, R, Imm | NoTable, I.Ushr, I.Ushr, I.Ushr, I.Ushr, I.Ushr, I.Ushr, I.Ushr, I.Ushr, N, N);
        Row(AdvSimd, false, "ShiftRightLogicalAdd", -1, 3, R, Imm | Rmw, I.Usra, I.Usra, I.Usra, I.Usra, I.Usra, I.Usra, I.Usra, I.Usra, N, N);
        Row(AdvSimd, false, "ShiftRightLogicalAddScalar", 8, 3, R, Imm | Rmw | Sc, N, N, N, N, N, N, I.Usra, I.Usra, N, N);
        Row(AdvSimd, false, "ShiftRightLogicalNarrowingLower", 8, 2, R, Imm, I.Shrn, I.Shrn, I.Shrn, I.Shrn, I.Shrn, I.Shrn, N, N, N, N);
        Row(AdvSimd, false, "ShiftRightLogicalNarrowingSaturateLower", 8, 2, R, Imm, I.Uqshrn, I.Uqshrn, I.Uqshrn, I.Uqshrn, I.Uqshrn, I.Uqshrn, N, N, N, N);
        Row(AdvSimd, false, "ShiftRightLogicalNarrowingSaturateUpper", 16, 3, R, Imm | Rmw, I.Uqshrn, I.Uqshrn, I.Uqshrn, I.Uqshrn, I.Uqshrn, I.Uqshrn, N, N, N, N);
        Row(AdvSimd, false, "ShiftRightLogicalNarrowingUpper", 16, 3, R, Imm | Rmw, I.Shrn, I.Shrn, I.Shrn, I.Shrn, I.Shrn, I.Shrn, N, N, N, N);
        Row(AdvSimd, false, "ShiftRightLogicalRounded", -1, 2, R, Imm, I.Urshr, I.Urshr, I.Urshr, I.Urshr, I.Urshr, I.Urshr, I.Urshr, I.Urshr, N, N);
        Row(AdvSimd, false, "ShiftRightLogicalRoundedAdd", -1, 3, R, Imm | Rmw, I.Ursra, I.Ursra, I.Ursra, I.Ursra, I.Ursra, I.Ursra, I.Ursra, I.Ursra, N, N);
        Row(AdvSimd, false, "ShiftRightLogicalRoundedAddScalar", 8, 3, R, Imm | Rmw | Sc, N, N, N, N, N, N, I.Ursra, I.Ursra, N, N);
        Row(AdvSimd, false, "ShiftRightLogicalRoundedNarrowingLower", 8, 2, R, Imm, I.Rshrn, I.Rshrn, I.Rshrn, I.Rshrn, I.Rshrn, I.Rshrn, N, N, N, N);
        Row(AdvSimd, false, "ShiftRightLogicalRoundedNarrowingSaturateLower", 8, 2, R, Imm, I.Uqrshrn, I.Uqrshrn, I.Uqrshrn, I.Uqrshrn, I.Uqrshrn, I.Uqrshrn, N, N, N, N);
        Row(AdvSimd, false, "ShiftRightLogicalRoundedNarrowingSaturateUpper", 16, 3, R, Imm | Rmw, I.Uqrshrn, I.Uqrshrn, I.Uqrshrn, I.Uqrshrn, I.Uqrshrn, I.Uqrshrn, N, N, N, N);
        Row(AdvSimd, false, "ShiftRightLogicalRoundedNarrowingUpper", 16, 3, R, Imm | Rmw, I.Rshrn, I.Rshrn, I.Rshrn, I.Rshrn, I.Rshrn, I.Rshrn, N, N, N, N);
        Row(AdvSimd, false, "ShiftRightLogicalRoundedScalar", 8, 2, R, Imm | Sc, N, N, N, N, N, N, I.Urshr, I.Urshr, N, N);
        Row(AdvSimd, false, "ShiftRightLogicalScalar", 8, 2, R, Imm | Sc | NoTable, N, N, N, N, N, N, I.Ushr, I.Ushr, N, N);
        Row(AdvSimd, false, "SignExtendWideningLower", 8, 1, S, First, I.Sshll, N, I.Sshll, N, I.Sshll, N, N, N, N, N);
        Row(AdvSimd, false, "SignExtendWideningUpper", 16, 1, S, First, I.Sshll, N, I.Sshll, N, I.Sshll, N, N, N, N, N);
        Row(AdvSimd, false, "SqrtScalar", 8, 1, S, Sc, N, N, N, N, N, N, N, N, I.Fsqrt, I.Fsqrt);
        Row(AdvSimd, false, "Store", -1, 2, St, Second | Tuple | Spec, I.St1, I.St1, I.St1, I.St1, I.St1, I.St1, N, N, I.St1, N);
        Row(AdvSimd, false, "StoreSelectedScalar", -1, 3, St, Second | Tuple | Imm | Sc | Spec, I.St1, I.St1, I.St1, I.St1, I.St1, I.St1, I.St1, I.St1, I.St1, I.St1);
        Row(AdvSimd, false, "StoreVectorAndZip", 8, 2, St, Second | Tuple | Spec, I.St2, I.St2, I.St2, I.St2, I.St2, I.St2, I.St2, I.St2, I.St2, I.St2);
        Row(AdvSimd, false, "Subtract", -1, 2, S, 0, I.Sub, I.Sub, I.Sub, I.Sub, I.Sub, I.Sub, I.Sub, I.Sub, I.Fsub, N);
        Row(AdvSimd, false, "SubtractHighNarrowingLower", 8, 2, S, 0, I.Subhn, I.Subhn, I.Subhn, I.Subhn, I.Subhn, I.Subhn, N, N, N, N);
        Row(AdvSimd, false, "SubtractHighNarrowingUpper", 16, 3, S, Rmw, I.Subhn, I.Subhn, I.Subhn, I.Subhn, I.Subhn, I.Subhn, N, N, N, N);
        Row(AdvSimd, false, "SubtractRoundedHighNarrowingLower", 8, 2, S, 0, I.Rsubhn, I.Rsubhn, I.Rsubhn, I.Rsubhn, I.Rsubhn, I.Rsubhn, N, N, N, N);
        Row(AdvSimd, false, "SubtractRoundedHighNarrowingUpper", 16, 3, S, Rmw, I.Rsubhn, I.Rsubhn, I.Rsubhn, I.Rsubhn, I.Rsubhn, I.Rsubhn, N, N, N, N);
        Row(AdvSimd, false, "SubtractSaturate", -1, 2, S, 0, I.Sqsub, I.Uqsub, I.Sqsub, I.Uqsub, I.Sqsub, I.Uqsub, I.Sqsub, I.Uqsub, N, N);
        Row(AdvSimd, false, "SubtractSaturateScalar", 8, 2, S, Sc, N, N, N, N, N, N, I.Sqsub, I.Uqsub, N, N);
        Row(AdvSimd, false, "SubtractScalar", 8, 2, S, Sc, N, N, N, N, N, N, I.Sub, I.Sub, I.Fsub, I.Fsub);
        Row(AdvSimd, false, "SubtractWideningLower", 8, 2, S, Second | Spec, I.Ssubl, I.Usubl, I.Ssubl, I.Usubl, I.Ssubl, I.Usubl, I.Ssubw, I.Usubw, N, N);
        Row(AdvSimd, false, "SubtractWideningUpper", 16, 2, S, Second | Spec, I.Ssubl, I.Usubl, I.Ssubl, I.Usubl, I.Ssubl, I.Usubl, I.Ssubw, I.Usubw, N, N);
        Row(AdvSimd, false, "VectorTableLookup", 8, 2, S, Spec | Tuple, I.Tbl, I.Tbl, N, N, N, N, N, N, N, N);
        Row(AdvSimd, false, "VectorTableLookupExtension", 8, 3, S, Spec | Rmw | Tuple, I.Tbx, I.Tbx, N, N, N, N, N, N, N, N);
        Row(AdvSimd, false, "Xor", -1, 2, S, Comm, I.Eor, I.Eor, I.Eor, I.Eor, I.Eor, I.Eor, I.Eor, I.Eor, I.Eor, I.Eor);
        Row(AdvSimd, false, "ZeroExtendWideningLower", 8, 1, S, First, I.Ushll, I.Ushll, I.Ushll, I.Ushll, I.Ushll, I.Ushll, N, N, N, N);
        Row(AdvSimd, false, "ZeroExtendWideningUpper", 16, 1, S, First, I.Ushll, I.Ushll, I.Ushll, I.Ushll, I.Ushll, I.Ushll, N, N, N, N);
        Row(AdvSimd, true, "Abs", 16, 1, S, First, N, N, N, N, N, N, I.Abs, N, N, I.Fabs);
        Row(AdvSimd, true, "AbsSaturate", 16, 1, S, First, N, N, N, N, N, N, I.Sqabs, N, N, N);
        Row(AdvSimd, true, "AbsSaturateScalar", 8, 1, S, First | Sc, I.Sqabs, N, I.Sqabs, N, I.Sqabs, N, I.Sqabs, N, N, N);
        Row(AdvSimd, true, "AbsScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, I.Abs, N, N, N);
        Row(AdvSimd, true, "AbsoluteCompareGreaterThan", 16, 2, S, 0, N, N, N, N, N, N, N, N, N, I.Facgt);
        Row(AdvSimd, true, "AbsoluteCompareGreaterThanOrEqual", 16, 2, S, 0, N, N, N, N, N, N, N, N, N, I.Facge);
        Row(AdvSimd, true, "AbsoluteCompareGreaterThanOrEqualScalar", 8, 2, S, Sc, N, N, N, N, N, N, N, N, I.Facge, I.Facge);
        Row(AdvSimd, true, "AbsoluteCompareGreaterThanScalar", 8, 2, S, Sc, N, N, N, N, N, N, N, N, I.Facgt, I.Facgt);
        Row(AdvSimd, true, "AbsoluteCompareLessThan", 16, 2, S, Spec, N, N, N, N, N, N, N, N, N, I.Facgt);
        Row(AdvSimd, true, "AbsoluteCompareLessThanOrEqual", 16, 2, S, Spec, N, N, N, N, N, N, N, N, N, I.Facge);
        Row(AdvSimd, true, "AbsoluteCompareLessThanOrEqualScalar", 8, 2, S, Sc | Spec, N, N, N, N, N, N, N, N, I.Facge, I.Facge);
        Row(AdvSimd, true, "AbsoluteCompareLessThanScalar", 8, 2, S, Sc | Spec, N, N, N, N, N, N, N, N, I.Facgt, I.Facgt);
        Row(AdvSimd, true, "AbsoluteDifference", 16, 2, S, 0, N, N, N, N, N, N, N, N, N, I.Fabd);
        Row(AdvSimd, true, "AbsoluteDifferenceScalar", 8, 2, S, Sc, N, N, N, N, N, N, N, N, I.Fabd, I.Fabd);
        Row(AdvSimd, true, "Add", 16, 2, S, Comm, N, N, N, N, N, N, N, N, N, I.Fadd);
        Row(AdvSimd, true, "AddAcross", -1, 1, S, First, I.Addv, I.Addv, I.Addv, I.Addv, I.Addv, I.Addv, N, N, N, N);
        Row(AdvSimd, true, "AddAcrossWidening", -1, 1, S, First, I.Saddlv, I.Uaddlv, I.Saddlv, I.Uaddlv, I.Saddlv, I.Uaddlv, N, N, N, N);
        Row(AdvSimd, true, "AddPairwise", 16, 2, S, 0, I.Addp, I.Addp, I.Addp, I.Addp, I.Addp, I.Addp, I.Addp, I.Addp, I.Faddp, I.Faddp);
        Row(AdvSimd, true, "AddPairwiseScalar", -1, 1, S, First, N, N, N, N, N, N, I.Addp, I.Addp, I.Faddp, I.Faddp);
        Row(AdvSimd, true, "AddSaturate", -1, 2, S, First | Rmw, I.Suqadd, I.Usqadd, I.Suqadd, I.Usqadd, I.Suqadd, I.Usqadd, I.Suqadd, I.Usqadd, N, N);
        Row(AdvSimd, true, "AddSaturateScalar", 8, 2, S, First | Rmw | Sc | Spec, I.Sqadd, I.Uqadd, I.Sqadd, I.Uqadd, I.Sqadd, I.Uqadd, I.Suqadd, I.Usqadd, N, N);
        Row(AdvSimd, true, "Ceiling", 16, 1, S, 0, N, N, N, N, N, N, N, N, N, I.Frintp);
        Row(AdvSimd, true, "CompareEqual", 16, 2, S, Comm, N, N, N, N, N, N, I.Cmeq, I.Cmeq, N, I.Fcmeq);
        Row(AdvSimd, true, "CompareEqualScalar", 8, 2, S, Comm | Sc, N, N, N, N, N, N, I.Cmeq, I.Cmeq, I.Fcmeq, I.Fcmeq);
        Row(AdvSimd, true, "CompareGreaterThan", 16, 2, S, 0, N, N, N, N, N, N, I.Cmgt, I.Cmhi, N, I.Fcmgt);
        Row(AdvSimd, true, "CompareGreaterThanOrEqual", 16, 2, S, 0, N, N, N, N, N, N, I.Cmge, I.Cmhs, N, I.Fcmge);
        Row(AdvSimd, true, "CompareGreaterThanOrEqualScalar", 8, 2, S, Sc, N, N, N, N, N, N, I.Cmge, I.Cmhs, I.Fcmge, I.Fcmge);
        Row(AdvSimd, true, "CompareGreaterThanScalar", 8, 2, S, Sc, N, N, N, N, N, N, I.Cmgt, I.Cmhi, I.Fcmgt, I.Fcmgt);
        Row(AdvSimd, true, "CompareLessThan", 16, 2, S, Spec, N, N, N, N, N, N, I.Cmgt, I.Cmhi, N, I.Fcmgt);
        Row(AdvSimd, true, "CompareLessThanOrEqual", 16, 2, S, Spec, N, N, N, N, N, N, I.Cmge, I.Cmhs, N, I.Fcmge);
        Row(AdvSimd, true, "CompareLessThanOrEqualScalar", 8, 2, S, Sc | Spec, N, N, N, N, N, N, I.Cmge, I.Cmhs, I.Fcmge, I.Fcmge);
        Row(AdvSimd, true, "CompareLessThanScalar", 8, 2, S, Sc | Spec, N, N, N, N, N, N, I.Cmgt, I.Cmhi, I.Fcmgt, I.Fcmgt);
        Row(AdvSimd, true, "CompareTest", 16, 2, S, Comm, N, N, N, N, N, N, I.Cmtst, I.Cmtst, N, I.Cmtst);
        Row(AdvSimd, true, "CompareTestScalar", 8, 2, S, Comm | Sc, N, N, N, N, N, N, I.Cmtst, I.Cmtst, N, I.Cmtst);
        Row(AdvSimd, true, "ConvertToDouble", -1, 1, S, First, N, N, N, N, N, N, I.Scvtf, I.Ucvtf, I.Fcvtl, N);
        Row(AdvSimd, true, "ConvertToDoubleScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, I.Scvtf, I.Ucvtf, N, N);
        Row(AdvSimd, true, "ConvertToDoubleUpper", 16, 1, S, First, N, N, N, N, N, N, N, N, I.Fcvtl, N);
        Row(AdvSimd, true, "ConvertToInt64RoundAwayFromZero", 16, 1, S, First, N, N, N, N, N, N, N, N, N, I.Fcvtas);
        Row(AdvSimd, true, "ConvertToInt64RoundAwayFromZeroScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, N, N, N, I.Fcvtas);
        Row(AdvSimd, true, "ConvertToInt64RoundToEven", 16, 1, S, First, N, N, N, N, N, N, N, N, N, I.Fcvtns);
        Row(AdvSimd, true, "ConvertToInt64RoundToEvenScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, N, N, N, I.Fcvtns);
        Row(AdvSimd, true, "ConvertToInt64RoundToNegativeInfinity", 16, 1, S, First, N, N, N, N, N, N, N, N, N, I.Fcvtms);
        Row(AdvSimd, true, "ConvertToInt64RoundToNegativeInfinityScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, N, N, N, I.Fcvtms);
        Row(AdvSimd, true, "ConvertToInt64RoundToPositiveInfinity", 16, 1, S, First, N, N, N, N, N, N, N, N, N, I.Fcvtps);
        Row(AdvSimd, true, "ConvertToInt64RoundToPositiveInfinityScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, N, N, N, I.Fcvtps);
        Row(AdvSimd, true, "ConvertToInt64RoundToZero", 16, 1, S, First, N, N, N, N, N, N, N, N, N, I.Fcvtzs);
        Row(AdvSimd, true, "ConvertToInt64RoundToZeroScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, N, N, N, I.Fcvtzs);
        Row(AdvSimd, true, "ConvertToSingleLower", 8, 1, S, 0, N, N, N, N, N, N, N, N, I.Fcvtn, N);
        Row(AdvSimd, true, "ConvertToSingleRoundToOddLower", 8, 1, S, 0, N, N, N, N, N, N, N, N, I.Fcvtxn, N);
        Row(AdvSimd, true, "ConvertToSingleRoundToOddUpper", 16, 2, S, Rmw, N, N, N, N, N, N, N, N, I.Fcvtxn, N);
        Row(AdvSimd, true, "ConvertToSingleUpper", 16, 2, S, Rmw, N, N, N, N, N, N, N, N, I.Fcvtn, N);
        Row(AdvSimd, true, "ConvertToUInt64RoundAwayFromZero", 16, 1, S, First, N, N, N, N, N, N, N, N, N, I.Fcvtau);
        Row(AdvSimd, true, "ConvertToUInt64RoundAwayFromZeroScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, N, N, N, I.Fcvtau);
        Row(AdvSimd, true, "ConvertToUInt64RoundToEven", 16, 1, S, First, N, N, N, N, N, N, N, N, N, I.Fcvtnu);
        Row(AdvSimd, true, "ConvertToUInt64RoundToEvenScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, N, N, N, I.Fcvtnu);
        Row(AdvSimd, true, "ConvertToUInt64RoundToNegativeInfinity", 16, 1, S, First, N, N, N, N, N, N, N, N, N, I.Fcvtmu);
        Row(AdvSimd, true, "ConvertToUInt64RoundToNegativeInfinityScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, N, N, N, I.Fcvtmu);
        Row(AdvSimd, true, "ConvertToUInt64RoundToPositiveInfinity", 16, 1, S, First, N, N, N, N, N, N, N, N, N, I.Fcvtpu);
        Row(AdvSimd, true, "ConvertToUInt64RoundToPositiveInfinityScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, N, N, N, I.Fcvtpu);
        Row(AdvSimd, true, "ConvertToUInt64RoundToZero", 16, 1, S, First, N, N, N, N, N, N, N, N, N, I.Fcvtzu);
        Row(AdvSimd, true, "ConvertToUInt64RoundToZeroScalar", 8, 1, S, First | Sc, N, N, N, N, N, N, N, N, N, I.Fcvtzu);
        Row(AdvSimd, true, "Divide", -1, 2, S, 0, N, N, N, N, N, N, N, N, I.Fdiv, I.Fdiv);
        Row(AdvSimd, true, "DuplicateSelectedScalarToVector128", 16, 2, S, First | Imm | Spec, N, N, N, N, N, N, I.Dup, I.Dup, N, I.Dup);
        Row(AdvSimd, true, "DuplicateToVector128", 16, 1, S, Spec, N, N, N, N, N, N, I.Dup, I.Dup, N, I.Dup);
        Row(AdvSimd, true, "DuplicateToVector64", 16, 1, S, Sc | Spec, N, N, N, N, N, N, I.Mov, I.Mov, N, I.Fmov);
        Row(AdvSimd, true, "ExtractNarrowingSaturateScalar", 8, 1, S, Sc, I.Sqxtn, I.Uqxtn, I.Sqxtn, I.Uqxtn, I.Sqxtn, I.Uqxtn, N, N, N, N);
        Row(AdvSimd, true, "ExtractNarrowingSaturateUnsignedScalar", 8, 1, S, Sc, N, I.Sqxtun, N, I.Sqxtun, N, I.Sqxtun, N, N, N, N);
        Row(AdvSimd, true, "Floor", 16, 1, S, 0, N, N, N, N, N, N, N, N, N, I.Frintm);
        Row(AdvSimd, true, "FusedMultiplyAdd", 16, 3, S, Rmw, N, N, N, N, N, N, N, N, N, I.Fmla);
        Row(AdvSimd, true, "FusedMultiplyAddByScalar", -1, 3, E, Rmw, N, N, N, N, N, N, N, N, I.Fmla, I.Fmla);
        Row(AdvSimd, true, "FusedMultiplyAddBySelectedScalar", -1, 4, E, Imm | Rmw, N, N, N, N, N, N, N, N, I.Fmla, I.Fmla);
        Row(AdvSimd, true, "FusedMultiplyAddScalarBySelectedScalar", 8, 4, E, Imm | Rmw | Sc, N, N, N, N, N, N, N, N, I.Fmla, I.Fmla);
        Row(AdvSimd, true, "FusedMultiplySubtract", 16, 3, S, Rmw, N, N, N, N, N, N, N, N, N, I.Fmls);
        Row(AdvSimd, true, "FusedMultiplySubtractByScalar", -1, 3, E, Rmw, N, N, N, N, N, N, N, N, I.Fmls, I.Fmls);
        Row(AdvSimd, true, "FusedMultiplySubtractBySelectedScalar", -1, 4, E, Imm | Rmw, N, N, N, N, N, N, N, N, I.Fmls, I.Fmls);
        Row(AdvSimd, true, "FusedMultiplySubtractScalarBySelectedScalar", 8, 4, E, Imm | Rmw | Sc, N, N, N, N, N, N, N, N, I.Fmls, I.Fmls);
        Row(AdvSimd, true, "InsertSelectedScalar", -1, 4, S, Imm | Rmw | NoTable | Sc | Spec, I.Ins, I.Ins, I.Ins, I.Ins, I.Ins, I.Ins, I.Ins, I.Ins, I.Ins, I.Ins);
        Row(AdvSimd, true, "Load2xVector128", 16, 1, Ld, First | Tuple, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1);
        Row(AdvSimd, true, "Load2xVector128AndUnzip", 16, 1, Ld, First | Tuple, I.Ld2, I.Ld2, I.Ld2, I.Ld2, I.Ld2, I.Ld2, I.Ld2, I.Ld2, I.Ld2, I.Ld2);
        Row(AdvSimd, true, "Load3xVector128", 16, 1, Ld, First | Tuple, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1);
        Row(AdvSimd, true, "Load3xVector128AndUnzip", 16, 1, Ld, First | Tuple, I.Ld3, I.Ld3, I.Ld3, I.Ld3, I.Ld3, I.Ld3, I.Ld3, I.Ld3, I.Ld3, I.Ld3);
        Row(AdvSimd, true, "Load4xVector128", 16, 1, Ld, First | Tuple, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1, I.Ld1);
        Row(AdvSimd, true, "Load4xVector128AndUnzip", 16, 1, Ld, First | Tuple, I.Ld4, I.Ld4, I.Ld4, I.Ld4, I.Ld4, I.Ld4, I.Ld4, I.Ld4, I.Ld4, I.Ld4);
        Row(AdvSimd, true, "LoadAndInsertScalar", -1, 3, Ld, NoGen, N, N, N, N, N, N, N, N, N, N);
        Row(AdvSimd, true, "LoadAndInsertScalarVector128x2", 16, 3, Ld, Imm | Rmw | Sc | Spec | Tuple, I.Ld2, I.Ld2, I.Ld2, I.Ld2, I.Ld2, I.Ld2, I.Ld2, I.Ld2, I.Ld2, I.Ld2);
        Row(AdvSimd, true, "LoadAndInsertScalarVector128x3", 16, 3, Ld, Imm | Rmw | Sc | Spec | Tuple, I.Ld3, I.Ld3, I.Ld3, I.Ld3, I.Ld3, I.Ld3, I.Ld3, I.Ld3, I.Ld3, I.Ld3);
        Row(AdvSimd, true, "LoadAndInsertScalarVector128x4", 16, 3, Ld, Imm | Rmw | Sc | Spec | Tuple, I.Ld4, I.Ld4, I.Ld4, I.Ld4, I.Ld4, I.Ld4, I.Ld4, I.Ld4, I.Ld4, I.Ld4);
        Row(AdvSimd, true, "LoadAndReplicateToVector128", 16, 1, Ld, 0, N, N, N, N, N, N, I.Ld1r, I.Ld1r, N, I.Ld1r);
        Row(AdvSimd, true, "LoadAndReplicateToVector128x2", 16, 1, Ld, First | Tuple, I.Ld2r, I.Ld2r, I.Ld2r, I.Ld2r, I.Ld2r, I.Ld2r, I.Ld2r, I.Ld2r, I.Ld2r, I.Ld2r);
        Row(AdvSimd, true, "LoadAndReplicateToVector128x3", 16, 1, Ld, First | Tuple, I.Ld3r, I.Ld3r, I.Ld3r, I.Ld3r, I.Ld3r, I.Ld3r, I.Ld3r, I.Ld3r, I.Ld3r, I.Ld3r);
        Row(AdvSimd, true, "LoadAndReplicateToVector128x4", 16, 1, Ld, First | Tuple, I.Ld4r, I.Ld4r, I.Ld4r, I.Ld4r, I.Ld4r, I.Ld4r, I.Ld4r, I.Ld4r, I.Ld4r, I.Ld4r);
        Row(AdvSimd, true, "LoadPairScalarVector64", 8, 1, Ld, First | Tuple | Spec, N, N, N, N, I.Ldp, I.Ldp, N, N, I.Ldp, N);
        Row(AdvSimd, true, "LoadPairScalarVector64NonTemporal", 8, 1, Ld, First | Tuple | Spec, N, N, N, N, I.Ldnp, I.Ldnp, N, N, I.Ldnp, N);
        Row(AdvSimd, true, "LoadPairVector128", 16, 1, Ld, First | Tuple | Spec, I.Ldp, I.Ldp, I.Ldp, I.Ldp, I.Ldp, I.Ldp, I.Ldp, I.Ldp, I.Ldp, I.Ldp);
        Row(AdvSimd, true, "LoadPairVector128NonTemporal", 16, 1, Ld, First | Tuple | Spec, I.Ldnp, I.Ldnp, I.Ldnp, I.Ldnp, I.Ldnp, I.Ldnp, I.Ldnp, I.Ldnp, I.Ldnp, I.Ldnp);
        Row(AdvSimd, true, "LoadPairVector64", 8, 1, Ld, First | Tuple | Spec, I.Ldp, I.Ldp, I.Ldp, I.Ldp, I.Ldp, I.Ldp, I.Ldp, I.Ldp, I.Ldp, I.Ldp);
        Row(AdvSimd, true, "LoadPairVector64NonTemporal", 8, 1, Ld, First | Tuple | Spec, I.Ldnp, I.Ldnp, I.Ldnp, I.Ldnp, I.Ldnp, I.Ldnp, I.Ldnp, I.Ldnp, I.Ldnp, I.Ldnp);
        Row(AdvSimd, true, "Max", 16, 2, S, Comm, N, N, N, N, N, N, N, N, N, I.Fmax);
        Row(AdvSimd, true, "MaxAcross", -1, 1, S, First, I.Smaxv, I.Umaxv, I.Smaxv, I.Umaxv, I.Smaxv, I.Umaxv, N, N, I.Fmaxv, N);
        Row(AdvSimd, true, "MaxNumber", 16, 2, S, Comm, N, N, N, N, N, N, N, N, N, I.Fmaxnm);
        Row(AdvSimd, true, "MaxNumberAcross", 16, 1, S, First, N, N, N, N, N, N, N, N, I.Fmaxnmv, N);
        Row(AdvSimd, true, "MaxNumberPairwise", -1, 2, S, 0, N, N, N, N, N, N, N, N, I.Fmaxnmp, I.Fmaxnmp);
        Row(AdvSimd, true, "MaxNumberPairwiseScalar", -1, 1, S, First, N, N, N, N, N, N, N, N, I.Fmaxnmp, I.Fmaxnmp);
        Row(AdvSimd, true, "MaxPairwise", 16, 2, S, 0, I.Smaxp, I.Umaxp, I.Smaxp, I.Umaxp, I.Smaxp, I.Umaxp, N, N, I.Fmaxp, I.Fmaxp);
        Row(AdvSimd, true, "MaxPairwiseScalar", -1, 1, S, First, N, N, N, N, N, N, N, N, I.Fmaxp, I.Fmaxp);
        Row(AdvSimd, true, "MaxScalar", 8, 2, S, Comm | Sc, N, N, N, N, N, N, N, N, I.Fmax, I.Fmax);
        Row(AdvSimd, true, "Min", 16, 2, S, Comm, N, N, N, N, N, N, N, N, N, I.Fmin);
        Row(AdvSimd, true, "MinAcross", -1, 1, S, First, I.Sminv, I.Uminv, I.Sminv, I.Uminv, I.Sminv, I.Uminv, N, N, I.Fminv, N);
        Row(AdvSimd, true, "MinNumber", 16, 2, S, Comm, N, N, N, N, N, N, N, N, N, I.Fminnm);
        Row(AdvSimd, true, "MinNumberAcross", 16, 1, S, First, N, N, N, N, N, N, N, N, I.Fminnmv, N);
        Row(AdvSimd, true, "MinNumberPairwise", -1, 2, S, 0, N, N, N, N, N, N, N, N, I.Fminnmp, I.Fminnmp);
        Row(AdvSimd, true, "MinNumberPairwiseScalar", -1, 1, S, First, N, N, N, N, N, N, N, N, I.Fminnmp, I.Fminnmp);
        Row(AdvSimd, true, "MinPairwise", 16, 2, S, 0, I.Sminp, I.Uminp, I.Sminp, I.Uminp, I.Sminp, I.Uminp, N, N, I.Fminp, I.Fminp);
        Row(AdvSimd, true, "MinPairwiseScalar", -1, 1, S, First, N, N, N, N, N, N, N, N, I.Fminp, I.Fminp);
        Row(AdvSimd, true, "MinScalar", 8, 2, S, Comm | Sc, N, N, N, N, N, N, N, N, I.Fmin, I.Fmin);
        Row(AdvSimd, true, "Multiply", 16, 2, S, Comm, N, N, N, N, N, N, N, N, N, I.Fmul);
        Row(AdvSimd, true, "MultiplyByScalar", 16, 2, E, 0, N, N, N, N, N, N, N, N, N, I.Fmul);
        Row(AdvSimd, true, "MultiplyBySelectedScalar", 16, 3, E, Imm, N, N, N, N, N, N, N, N, N, I.Fmul);
        Row(AdvSimd, true, "MultiplyDoublingSaturateHighScalar", 8, 2, S, Comm | Sc, N, N, I.Sqdmulh, N, I.Sqdmulh, N, N, N, N, N);
        Row(AdvSimd, true, "MultiplyDoublingScalarBySelectedScalarSaturateHigh", 8, 3, E, Imm | Sc, N, N, I.Sqdmulh, N, I.Sqdmulh, N, N, N, N, N);
        Row(AdvSimd, true, "MultiplyDoublingWideningAndAddSaturateScalar", 8, 3, S, Second | Rmw | Sc, N, N, I.Sqdmlal, N, I.Sqdmlal, N, N, N, N, N);
        Row(AdvSimd, true, "MultiplyDoublingWideningAndSubtractSaturateScalar", 8, 3, S, Second | Rmw | Sc, N, N, I.Sqdmlsl, N, I.Sqdmlsl, N, N, N, N, N);
        Row(AdvSimd, true, "MultiplyDoublingWideningSaturateScalar", 8, 2, S, First | Comm | Sc, N, N, I.Sqdmull, N, I.Sqdmull, N, N, N, N, N);
        Row(AdvSimd, true, "MultiplyDoublingWideningSaturateScalarBySelectedScalar", 8, 3, E, First | Imm | Sc, N, N, I.Sqdmull, N, I.Sqdmull, N, N, N, N, N);
        Row(AdvSimd, true, "MultiplyDoublingWideningScalarBySelectedScalarAndAddSaturate", 8, 4, E, Second | Imm | Rmw | Sc, N, N, I.Sqdmlal, N, I.Sqdmlal, N, N, N, N, N);
        Row(AdvSimd, true, "MultiplyDoublingWideningScalarBySelectedScalarAndSubtractSaturate", 8, 4, E, Second | Imm | Rmw | Sc, N, N, I.Sqdmlsl, N, I.Sqdmlsl, N, N, N, N, N);
        Row(AdvSimd, true, "MultiplyExtended", -1, 2, S, Comm, N, N, N, N, N, N, N, N, I.Fmulx, I.Fmulx);
        Row(AdvSimd, true, "MultiplyExtendedByScalar", 16, 2, E, 0, N, N, N, N, N, N, N, N, N, I.Fmulx);
        Row(AdvSimd, true, "MultiplyExtendedBySelectedScalar", -1, 3, E, Imm, N, N, N, N, N, N, N, N, I.Fmulx, I.Fmulx);
        Row(AdvSimd, true, "MultiplyExtendedScalar", 8, 2, S, Comm | Sc, N, N, N, N, N, N, N, N, I.Fmulx, I.Fmulx);
        Row(AdvSimd, true, "MultiplyExtendedScalarBySelectedScalar", 8, 3, E, Imm | Sc, N, N, N, N, N, N, N, N, I.Fmulx, I.Fmulx);
        Row(AdvSimd, true, "MultiplyRoundedDoublingSaturateHighScalar", 8, 2, S, Comm | Sc, N, N, I.Sqrdmulh, N, I.Sqrdmulh, N, N, N, N, N);
        Row(AdvSimd, true, "MultiplyRoundedDoublingScalarBySelectedScalarSaturateHigh", 8, 3, E, Imm | Sc, N, N, I.Sqrdmulh, N, I.Sqrdmulh, N, N, N, N, N);
        Row(AdvSimd, true, "MultiplyScalarBySelectedScalar", 8, 3, E, Imm | Sc, N, N, N, N, N, N, N, N, N, I.Fmul);
        Row(AdvSimd, true, "Negate", 16, 1, S, 0, N, N, N, N, N, N, I.Neg, N, N, I.Fneg);
        Row(AdvSimd, true, "NegateSaturate", 16, 1, S, 0, N, N, N, N, N, N, I.Sqneg, N, N, N);
        Row(AdvSimd, true, "NegateSaturateScalar", 8, 1, S, Sc, I.Sqneg, N, I.Sqneg, N, I.Sqneg, N, I.Sqneg, N, N, N);
        Row(AdvSimd, true, "NegateScalar", 8, 1, S, Sc, N, N, N, N, N, N, I.Neg, N, N, N);
        Row(AdvSimd, true, "ReciprocalEstimate", 16, 1, S, 0, N, N, N, N, N, N, N, N, N, I.Frecpe);
        Row(AdvSimd, true, "ReciprocalEstimateScalar", 8, 1, S, Sc, N, N, N, N, N, N, N, N, I.Frecpe, I.Frecpe);
        Row(AdvSimd, true, "ReciprocalExponentScalar", 8, 1, S, Sc, N, N, N, N, N, N, N, N, I.Frecpx, I.Frecpx);
        Row(AdvSimd, true, "ReciprocalSquareRootEstimate", 16, 1, S, 0, N, N, N, N, N, N, N, N, N, I.Frsqrte);
        Row(AdvSimd, true, "ReciprocalSquareRootEstimateScalar", 8, 1, S, Sc, N, N, N, N, N, N, N, N, I.Frsqrte, I.Frsqrte);
        Row(AdvSimd, true, "ReciprocalSquareRootStep", 16, 2, S, Comm, N, N, N, N, N, N, N, N, N, I.Frsqrts);
        Row(AdvSimd, true, "ReciprocalSquareRootStepScalar", 8, 2, S, Comm | Sc, N, N, N, N, N, N, N, N, I.Frsqrts, I.Frsqrts);
        Row(AdvSimd, true, "ReciprocalStep", 16, 2, S, Comm, N, N, N, N, N, N, N, N, N, I.Frecps);
        Row(AdvSimd, true, "ReciprocalStepScalar", 8, 2, S, Comm | Sc, N, N, N, N, N, N, N, N, I.Frecps, I.Frecps);
        Row(AdvSimd, true, "ReverseElementBits", -1, 1, S, 0, I.Rbit, I.Rbit, N, N, N, N, N, N, N, N);
        Row(AdvSimd, true, "RoundAwayFromZero", 16, 1, S, 0, N, N, N, N, N, N, N, N, N, I.Frinta);
        Row(AdvSimd, true, "RoundToNearest", 16, 1, S, 0, N, N, N, N, N, N, N, N, N, I.Frintn);
        Row(AdvSimd, true, "RoundToNegativeInfinity", 16, 1, S, 0, N, N, N, N, N, N, N, N, N, I.Frintm);
        Row(AdvSimd, true, "RoundToPositiveInfinity", 16, 1, S, 0, N, N, N, N, N, N, N, N, N, I.Frintp);
        Row(AdvSimd, true, "RoundToZero", 16, 1, S, 0, N, N, N, N, N, N, N, N, N, I.Frintz);
        Row(AdvSimd, true, "ShiftArithmeticRoundedSaturateScalar", 8, 2, S, Sc, I.Sqrshl, N, I.Sqrshl, N, I.Sqrshl, N, N, N, N, N);
        Row(AdvSimd, true, "ShiftArithmeticSaturateScalar", 8, 2, S, Sc, I.Sqshl, N, I.Sqshl, N, I.Sqshl, N, N, N, N, N);
        Row(AdvSimd, true, "ShiftLeftLogicalSaturateScalar", 8, 2, L, Imm | Sc, I.Sqshl, I.Uqshl, I.Sqshl, I.Uqshl, I.Sqshl, I.Uqshl, N, N, N, N);
        Row(AdvSimd, true, "ShiftLeftLogicalSaturateUnsignedScalar", 8, 2, L, First | Imm | Sc, I.Sqshlu, N, I.Sqshlu, N, I.Sqshlu, N, N, N, N, N);
        Row(AdvSimd, true, "ShiftLogicalRoundedSaturateScalar", 8, 2, S, Sc, I.Uqrshl, I.Uqrshl, I.Uqrshl, I.Uqrshl, I.Uqrshl, I.Uqrshl, N, N, N, N);
        Row(AdvSimd, true, "ShiftLogicalSaturateScalar", 8, 2, S, Sc, I.Uqshl, I.Uqshl, I.Uqshl, I.Uqshl, I.Uqshl, I.Uqshl, N, N, N, N);
        Row(AdvSimd, true, "ShiftRightArithmeticNarrowingSaturateScalar", 8, 2, R, Imm | Sc, I.Sqshrn, N, I.Sqshrn, N, I.Sqshrn, N, N, N, N, N);
        Row(AdvSimd, true, "ShiftRightArithmeticNarrowingSaturateUnsignedScalar", 8, 2, R, Imm | Sc, N, I.Sqshrun, N, I.Sqshrun, N, I.Sqshrun, N, N, N, N);
        Row(AdvSimd, true, "ShiftRightArithmeticRoundedNarrowingSaturateScalar", 8, 2, R, Imm | Sc, I.Sqrshrn, N, I.Sqrshrn, N, I.Sqrshrn, N, N, N, N, N);
        Row(AdvSimd, true, "ShiftRightArithmeticRoundedNarrowingSaturateUnsignedScalar", 8, 2, R, Imm | Sc, N, I.Sqrshrun, N, I.Sqrshrun, N, I.Sqrshrun, N, N, N, N);
        Row(AdvSimd, true, "ShiftRightLogicalNarrowingSaturateScalar", 8, 2, R, Imm | Sc, I.Uqshrn, I.Uqshrn, I.Uqshrn, I.Uqshrn, I.Uqshrn, I.Uqshrn, N, N, N, N);
        Row(AdvSimd, true, "ShiftRightLogicalRoundedNarrowingSaturateScalar", 8, 2, R, Imm | Sc, I.Uqrshrn, I.Uqrshrn, I.Uqrshrn, I.Uqrshrn, I.Uqrshrn, I.Uqrshrn, N, N, N, N);
        Row(AdvSimd, true, "Sqrt", -1, 1, S, 0, N, N, N, N, N, N, N, N, I.Fsqrt, I.Fsqrt);
        Row(AdvSimd, true, "Store", 16, 2, St, Second | Tuple | Spec, I.St1, I.St1, I.St1, I.St1, I.St1, I.St1, I.St1, I.St1, I.St1, I.St1);
        Row(AdvSimd, true, "StorePair", -1, 3, St, Second | Spec, I.Stp, I.Stp, I.Stp, I.Stp, I.Stp, I.Stp, I.Stp, I.Stp, I.Stp, I.Stp);
        Row(AdvSimd, true, "StorePairNonTemporal", -1, 3, St, Second | Spec, I.Stnp, I.Stnp, I.Stnp, I.Stnp, I.Stnp, I.Stnp, I.Stnp, I.Stnp, I.Stnp, I.Stp);
        Row(AdvSimd, true, "StorePairScalar", 8, 3, St, Second | Spec, N, N, N, N, I.Stp, I.Stp, N, N, I.Stp, N);
        Row(AdvSimd, true, "StorePairScalarNonTemporal", 8, 3, St, Second | Spec, N, N, N, N, I.Stnp, I.Stnp, N, N, I.Stnp, N);
        Row(AdvSimd, true, "StoreSelectedScalar", 16, 3, St, Second | Tuple | Imm | Sc | Spec, I.St2, I.St2, I.St2, I.St2, I.St2, I.St2, I.St2, I.St2, I.St2, I.St2);
        Row(AdvSimd, true, "StoreVectorAndZip", 16, 2, St, Second | Tuple | Spec, I.St2, I.St2, I.St2, I.St2, I.St2, I.St2, I.St2, I.St2, I.St2, I.St2);
        Row(AdvSimd, true, "Subtract", 16, 2, S, 0, N, N, N, N, N, N, N, N, N, I.Fsub);
        Row(AdvSimd, true, "SubtractSaturateScalar", 8, 2, S, Sc, I.Sqsub, I.Uqsub, I.Sqsub, I.Uqsub, I.Sqsub, I.Uqsub, N, N, N, N);
        Row(AdvSimd, true, "TransposeEven", -1, 2, S, 0, I.Trn1, I.Trn1, I.Trn1, I.Trn1, I.Trn1, I.Trn1, I.Trn1, I.Trn1, I.Trn1, I.Trn1);
        Row(AdvSimd, true, "TransposeOdd", -1, 2, S, 0, I.Trn2, I.Trn2, I.Trn2, I.Trn2, I.Trn2, I.Trn2, I.Trn2, I.Trn2, I.Trn2, I.Trn2);
        Row(AdvSimd, true, "UnzipEven", -1, 2, S, 0, I.Uzp1, I.Uzp1, I.Uzp1, I.Uzp1, I.Uzp1, I.Uzp1, I.Uzp1, I.Uzp1, I.Uzp1, I.Uzp1);
        Row(AdvSimd, true, "UnzipOdd", -1, 2, S, 0, I.Uzp2, I.Uzp2, I.Uzp2, I.Uzp2, I.Uzp2, I.Uzp2, I.Uzp2, I.Uzp2, I.Uzp2, I.Uzp2);
        Row(AdvSimd, true, "VectorTableLookup", 16, 2, S, Spec | Tuple, I.Tbl, I.Tbl, N, N, N, N, N, N, N, N);
        Row(AdvSimd, true, "VectorTableLookupExtension", 16, 3, S, Spec | Rmw | Tuple, I.Tbx, I.Tbx, N, N, N, N, N, N, N, N);
        Row(AdvSimd, true, "ZipHigh", -1, 2, S, 0, I.Zip2, I.Zip2, I.Zip2, I.Zip2, I.Zip2, I.Zip2, I.Zip2, I.Zip2, I.Zip2, I.Zip2);
        Row(AdvSimd, true, "ZipLow", -1, 2, S, 0, I.Zip1, I.Zip1, I.Zip1, I.Zip1, I.Zip1, I.Zip1, I.Zip1, I.Zip1, I.Zip1, I.Zip1);
        Row(Aes, false, "Decrypt", 16, 2, S, Rmw, N, I.Aesd, N, N, N, N, N, N, N, N);
        Row(Aes, false, "Encrypt", 16, 2, S, Rmw, N, I.Aese, N, N, N, N, N, N, N, N);
        Row(Aes, false, "InverseMixColumns", 16, 1, S, 0, N, I.Aesimc, N, N, N, N, N, N, N, N);
        Row(Aes, false, "MixColumns", 16, 1, S, 0, N, I.Aesmc, N, N, N, N, N, N, N, N);
        Row(Aes, false, "PolynomialMultiplyWideningLower", 8, 2, S, First | Comm, N, N, N, N, N, N, I.Pmull, I.Pmull, N, N);
        Row(Aes, false, "PolynomialMultiplyWideningUpper", 16, 2, S, First | Comm, N, N, N, N, N, N, I.Pmull, I.Pmull, N, N);
        Row(ArmBase, false, "LeadingZeroCount", 0, 1, G, First, N, N, N, N, I.Clz, I.Clz, N, N, N, N);
        Row(ArmBase, false, "ReverseElementBits", 0, 1, G, 0, N, N, N, N, I.Rbit, I.Rbit, N, N, N, N);
        Row(ArmBase, false, "Yield", 0, 0, X, Spec, N, N, N, N, N, N, N, N, N, N);
        Row(ArmBase, true, "LeadingSignCount", 0, 1, G, First, N, N, N, N, I.Cls, N, I.Cls, N, N, N);
        Row(ArmBase, true, "LeadingZeroCount", 0, 1, G, First, N, N, N, N, N, N, I.Clz, I.Clz, N, N);
        Row(ArmBase, true, "MultiplyHigh", 0, 2, G, 0, N, N, N, N, N, N, I.Smulh, I.Umulh, N, N);
        Row(ArmBase, true, "MultiplyLongAdd", 0, 3, G, Spec, N, N, N, N, N, N, I.Smaddl, I.Umaddl, N, N);
        Row(ArmBase, true, "MultiplyLongNeg", 0, 2, G, 0, N, N, N, N, N, N, I.Smsubl, I.Umsubl, N, N);
        Row(ArmBase, true, "MultiplyLongSub", 0, 3, G, Spec, N, N, N, N, N, N, I.Smsubl, I.Umsubl, N, N);
        Row(ArmBase, true, "ReverseElementBits", 0, 1, G, 0, N, N, N, N, N, N, I.Rbit, I.Rbit, N, N);
        Row(Crc32, false, "ComputeCrc32", 0, 2, G, Second | Spec, N, I.Crc32b, N, I.Crc32h, N, I.Crc32w, N, N, N, N);
        Row(Crc32, false, "ComputeCrc32C", 0, 2, G, Second | Spec, N, I.Crc32cb, N, I.Crc32ch, N, I.Crc32cw, N, N, N, N);
        Row(Crc32, true, "ComputeCrc32", 0, 2, G, Second | Spec, N, N, N, N, N, N, N, I.Crc32x, N, N);
        Row(Crc32, true, "ComputeCrc32C", 0, 2, G, Second | Spec, N, N, N, N, N, N, N, I.Crc32cx, N, N);
        Row(Dp, false, "DotProduct", -1, 3, S, Rmw, N, N, N, N, I.Sdot, I.Udot, N, N, N, N);
        Row(Dp, false, "DotProductBySelectedQuadruplet", -1, 4, E, Imm | Rmw, N, N, N, N, I.Sdot, I.Udot, N, N, N, N);
        Row(Rdm, false, "MultiplyRoundedDoublingAndAddSaturateHigh", -1, 3, S, Rmw, N, N, I.Sqrdmlah, N, I.Sqrdmlah, N, N, N, N, N);
        Row(Rdm, false, "MultiplyRoundedDoublingAndSubtractSaturateHigh", -1, 3, S, Rmw, N, N, I.Sqrdmlsh, N, I.Sqrdmlsh, N, N, N, N, N);
        Row(Rdm, false, "MultiplyRoundedDoublingBySelectedScalarAndAddSaturateHigh", -1, 4, E, Imm | Rmw, N, N, I.Sqrdmlah, N, I.Sqrdmlah, N, N, N, N, N);
        Row(Rdm, false, "MultiplyRoundedDoublingBySelectedScalarAndSubtractSaturateHigh", -1, 4, E, Imm | Rmw, N, N, I.Sqrdmlsh, N, I.Sqrdmlsh, N, N, N, N, N);
        Row(Rdm, true, "MultiplyRoundedDoublingAndAddSaturateHighScalar", 8, 3, S, Rmw | Sc, N, N, I.Sqrdmlah, N, I.Sqrdmlah, N, N, N, N, N);
        Row(Rdm, true, "MultiplyRoundedDoublingAndSubtractSaturateHighScalar", 8, 3, S, Rmw | Sc, N, N, I.Sqrdmlsh, N, I.Sqrdmlsh, N, N, N, N, N);
        Row(Rdm, true, "MultiplyRoundedDoublingScalarBySelectedScalarAndAddSaturateHigh", 8, 4, E, Imm | Rmw | Sc, N, N, I.Sqrdmlah, N, I.Sqrdmlah, N, N, N, N, N);
        Row(Rdm, true, "MultiplyRoundedDoublingScalarBySelectedScalarAndSubtractSaturateHigh", 8, 4, E, Imm | Rmw | Sc, N, N, I.Sqrdmlsh, N, I.Sqrdmlsh, N, N, N, N, N);
        Row(Sha1, false, "FixedRotate", 8, 1, S, Sc, N, N, N, N, N, I.Sha1h, N, N, N, N);
        Row(Sha1, false, "HashUpdateChoose", 16, 3, S, Rmw, N, N, N, N, N, I.Sha1c, N, N, N, N);
        Row(Sha1, false, "HashUpdateMajority", 16, 3, S, Rmw, N, N, N, N, N, I.Sha1m, N, N, N, N);
        Row(Sha1, false, "HashUpdateParity", 16, 3, S, Rmw, N, N, N, N, N, I.Sha1p, N, N, N, N);
        Row(Sha1, false, "ScheduleUpdate0", 16, 3, S, Rmw, N, N, N, N, N, I.Sha1su0, N, N, N, N);
        Row(Sha1, false, "ScheduleUpdate1", 16, 2, S, Rmw, N, N, N, N, N, I.Sha1su1, N, N, N, N);
        Row(Sha256, false, "HashUpdate1", 16, 3, S, Rmw, N, N, N, N, N, I.Sha256h, N, N, N, N);
        Row(Sha256, false, "HashUpdate2", 16, 3, S, Rmw, N, N, N, N, N, I.Sha256h2, N, N, N, N);
        Row(Sha256, false, "ScheduleUpdate0", 16, 2, S, Rmw, N, N, N, N, N, I.Sha256su0, N, N, N, N);
        Row(Sha256, false, "ScheduleUpdate1", 16, 3, S, Rmw, N, N, N, N, N, I.Sha256su1, N, N, N, N);
    }
}
