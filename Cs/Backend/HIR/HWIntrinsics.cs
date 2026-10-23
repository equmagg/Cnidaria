using System;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal enum HWIntrinsicId : ushort
    {
        None,

        Zero,
        AllBitsSet,
        Broadcast,
        CreateElements,
        CreateScalar,
        CreateScalarUnsafe,
        Combine,
        BitCast,
        ZeroExtend,
        GetUpper,
        WithLower,
        WithUpper,

        GetElement,
        WithElement,
        ToScalar,

        Add,
        Subtract,
        Multiply,
        Divide,
        Negate,
        Abs,
        Min,
        Max,
        Sqrt,
        Floor,
        Ceiling,
        AddSaturate,
        SubtractSaturate,

        And,
        Or,
        Xor,
        AndNot,
        Not,
        ConditionalSelect,

        CompareEqual,
        CompareGreaterThan,
        CompareGreaterThanOrEqual,
        CompareLessThan,
        CompareLessThanOrEqual,

        ShiftLeft,
        ShiftRightArithmetic,
        ShiftRightLogical,

        EqualsAll,
        EqualsAny,
        ExtractMostSignificantBits,
        Sum,
    }

    // A HWIntrinsic node keeps its operation in Int32 and its element kind and vector width in Int64
    internal static class HWIntrinsics
    {
        public static HWIntrinsicId Id(GenTree node) => (HWIntrinsicId)node.Int32;

        public static RuntimePrimitiveKind BaseKind(GenTree node) => (RuntimePrimitiveKind)(node.Int64 & 0xFF);

        public static int SimdSize(GenTree node) => (int)((node.Int64 >> 8) & 0xFF);

        public static long Encode(RuntimePrimitiveKind baseKind, int simdSize) => (long)baseKind | ((long)simdSize << 8);

        public static int ElementSize(RuntimePrimitiveKind kind)
            => kind switch
            {
                RuntimePrimitiveKind.Int8 or RuntimePrimitiveKind.UInt8 => 1,
                RuntimePrimitiveKind.Int16 or RuntimePrimitiveKind.UInt16 => 2,
                RuntimePrimitiveKind.Int32 or RuntimePrimitiveKind.UInt32 or RuntimePrimitiveKind.Single => 4,
                RuntimePrimitiveKind.Int64 or RuntimePrimitiveKind.UInt64 or RuntimePrimitiveKind.Double or
                RuntimePrimitiveKind.NativeInt or RuntimePrimitiveKind.NativeUInt => 8,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };

        public static bool IsFloating(RuntimePrimitiveKind kind)
            => kind is RuntimePrimitiveKind.Single or RuntimePrimitiveKind.Double;

        public static bool IsUnsigned(RuntimePrimitiveKind kind)
            => kind is RuntimePrimitiveKind.UInt8 or RuntimePrimitiveKind.UInt16 or RuntimePrimitiveKind.UInt32 or
                RuntimePrimitiveKind.UInt64 or RuntimePrimitiveKind.NativeUInt;

        public static bool IsCommutative(HWIntrinsicId id)
            => id is HWIntrinsicId.Add or HWIntrinsicId.Multiply or HWIntrinsicId.Min or HWIntrinsicId.Max or
                HWIntrinsicId.And or HWIntrinsicId.Or or HWIntrinsicId.Xor or HWIntrinsicId.CompareEqual or
                HWIntrinsicId.AddSaturate or HWIntrinsicId.EqualsAll or HWIntrinsicId.EqualsAny;

        public static RuntimePrimitiveKind NormalizeBaseKind(RuntimePrimitiveKind kind, TargetInfo target)
            => kind switch
            {
                RuntimePrimitiveKind.NativeInt => target.Is64Bit ? RuntimePrimitiveKind.Int64 : RuntimePrimitiveKind.Int32,
                RuntimePrimitiveKind.NativeUInt => target.Is64Bit ? RuntimePrimitiveKind.UInt64 : RuntimePrimitiveKind.UInt32,
                _ => kind,
            };

        public static bool HasFeature(TargetInfo target, TargetArchitectureFeatures feature)
            => (target.ArchitectureFeatures & feature) == feature;

        public static bool IsSupported(TargetInfo target, HWIntrinsicId id, RuntimePrimitiveKind baseKind, int simdSize)
        {
            if (!SimdTypes.TargetSupports(target, simdSize))
                return false;

            if (target.IsRiscV)
            {
                return id switch
                {
                    HWIntrinsicId.Divide or HWIntrinsicId.Sqrt => IsFloating(baseKind),
                    HWIntrinsicId.Floor or HWIntrinsicId.Ceiling => false,
                    HWIntrinsicId.Min or HWIntrinsicId.Max or HWIntrinsicId.AddSaturate or HWIntrinsicId.SubtractSaturate or
                    HWIntrinsicId.ShiftLeft or HWIntrinsicId.ShiftRightArithmetic or HWIntrinsicId.ShiftRightLogical => !IsFloating(baseKind),
                    _ => true,
                };
            }

            bool sse41 = HasFeature(target, TargetArchitectureFeatures.X86Sse41);
            bool sse42 = HasFeature(target, TargetArchitectureFeatures.X86Sse42);
            bool floating = IsFloating(baseKind);
            int elementSize = ElementSize(baseKind);
            return id switch
            {
                HWIntrinsicId.Multiply => floating || elementSize == 2 || elementSize == 4,
                HWIntrinsicId.Divide or HWIntrinsicId.Sqrt => floating,
                HWIntrinsicId.Floor or HWIntrinsicId.Ceiling => floating && sse41,
                HWIntrinsicId.Abs => true,
                HWIntrinsicId.Min or HWIntrinsicId.Max => !floating && (elementSize != 8 || sse42),
                HWIntrinsicId.AddSaturate or HWIntrinsicId.SubtractSaturate => !floating && elementSize <= 2,
                HWIntrinsicId.CompareGreaterThan or HWIntrinsicId.CompareGreaterThanOrEqual or
                HWIntrinsicId.CompareLessThan or HWIntrinsicId.CompareLessThanOrEqual => floating || elementSize != 8 || sse42,
                HWIntrinsicId.ShiftLeft or HWIntrinsicId.ShiftRightLogical => !floating && elementSize != 1,
                HWIntrinsicId.ShiftRightArithmetic => !floating && elementSize is 2 or 4,
                HWIntrinsicId.WithElement => elementSize is 2 or 8 || sse41,
                _ => true,
            };
        }

        public static bool IsContainableOperand(GenTree node, int operandIndex, GenTree operand)
        {
            if (X86Intrinsics.IsX86(node))
            {
                return operand.Kind == GenTreeKind.ConstI4 &&
                    (operandIndex == X86Intrinsics.ImmediateOperandIndex(node) || operandIndex == X86Intrinsics.RoundingOperandIndex(node));
            }

            switch (Id(node))
            {
                case HWIntrinsicId.ShiftLeft:
                case HWIntrinsicId.ShiftRightArithmetic:
                case HWIntrinsicId.ShiftRightLogical:
                case HWIntrinsicId.GetElement:
                case HWIntrinsicId.WithElement:
                    return operandIndex == 1 && operand.Kind == GenTreeKind.ConstI4;
                case HWIntrinsicId.Broadcast:
                case HWIntrinsicId.CreateElements:
                    return HasConstantElements(node);
                default:
                    return false;
            }
        }

        public static bool HasConstantElements(GenTree node)
            => HasConstantElements(node.Operands);

        public static bool HasConstantElements(ImmutableArray<GenTree> elements)
        {
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i].Kind is not (GenTreeKind.ConstI4 or GenTreeKind.ConstI8 or GenTreeKind.ConstR4Bits or GenTreeKind.ConstR8Bits))
                    return false;
            }
            return elements.Length != 0;
        }

        public static bool TouchesMemory(GenTree node)
            => X86Intrinsics.IsX86(node) && X86Intrinsics.TouchesMemory(node);

        public static GenTreeFlags EffectFlags(GenTree node)
            => X86Intrinsics.IsX86(node) ? X86Intrinsics.EffectFlags(node) : GenTreeFlags.None;

        public static long ValueNumberKey(int id, long info) => ((long)id << 32) | info;

        public static string Format(GenTree node)
            => X86Intrinsics.IsX86(node) ? X86Intrinsics.Format(node) : $"{Id(node)}<{BaseKind(node)}x{SimdSize(node)}>";
    }
    internal static class SimdTypes
    {
        public static bool IsSimdKind(GenStackKind kind)
            => kind is GenStackKind.Simd8 or GenStackKind.Simd12 or GenStackKind.Simd16 or GenStackKind.Simd32 or GenStackKind.Simd64;

        public static int SizeOf(GenStackKind kind)
            => kind switch
            {
                GenStackKind.Simd8 => 8,
                GenStackKind.Simd12 => 12,
                GenStackKind.Simd16 => 16,
                GenStackKind.Simd32 => 32,
                GenStackKind.Simd64 => 64,
                _ => 0,
            };

        public static GenStackKind KindForSize(int size)
            => size switch
            {
                8 => GenStackKind.Simd8,
                12 => GenStackKind.Simd12,
                16 => GenStackKind.Simd16,
                32 => GenStackKind.Simd32,
                64 => GenStackKind.Simd64,
                _ => throw new ArgumentOutOfRangeException(nameof(size)),
            };

        public static bool TargetSupports(TargetInfo? target, int size)
            => target is not null && (target.Architecture == TargetArchitectureKind.RiscV64 &&
                HWIntrinsics.HasFeature(target, TargetArchitectureFeatures.RiscVV) && size is 8 or 12 or 16 or 32 or 64 ||
                target.Architecture == TargetArchitectureKind.X86_64 && size switch
            {
                8 or 12 or 16 => true,
                32 => HWIntrinsics.HasFeature(target, TargetArchitectureFeatures.X86Avx2),
                64 => HWIntrinsics.HasFeature(target, TargetArchitectureFeatures.X86Avx512),
                _ => false,
            });

        public static int MaxSize(TargetInfo target)
            => TargetSupports(target, 64) ? 64 : TargetSupports(target, 32) ? 32 : 16;

        public static GenStackKind KindOf(RuntimeType? type, TargetInfo? target)
        {
            if (type is null || target is null || type.Kind != RuntimeTypeKind.Struct)
                return GenStackKind.Unknown;
            int numericsSize = NumericsSize(type);
            if (numericsSize != 0 && TargetSupports(target, numericsSize))
                return KindForSize(numericsSize);
            if (type.GenericTypeArguments.Length != 1)
                return GenStackKind.Unknown;
            int size = target.IsRiscV && type.GenericTypeDefinition is RuntimeType definition &&
                definition.Namespace == "System.Numerics" && definition.Name == "Vector`1" ? 16 : DefinitionSize(type);
            if (size == 0 || !TargetSupports(target, size) || !IsSupportedElement(type.GenericTypeArguments[0]))
                return GenStackKind.Unknown;
            return KindForSize(size);
        }

        public static bool IsSimdType(RuntimeType? type, TargetInfo? target)
            => KindOf(type, target) != GenStackKind.Unknown;

        public static int NumericsSize(RuntimeType type)
            => type.Namespace == "System.Numerics" ? type.Name switch
            {
                "Vector2" => 8,
                "Vector3" => 12,
                "Vector4" => 16,
                _ => 0,
            } : 0;

        public static RuntimeType ElementType(RuntimeType simdType)
            => NumericsSize(simdType) != 0 ? simdType.InstanceFields[0].FieldType : simdType.GenericTypeArguments[0];

        public static bool IsSupportedElement(RuntimeType element)
            => element.Kind != RuntimeTypeKind.Enum && element.PrimitiveKind is
                RuntimePrimitiveKind.Int8 or RuntimePrimitiveKind.UInt8 or
                RuntimePrimitiveKind.Int16 or RuntimePrimitiveKind.UInt16 or
                RuntimePrimitiveKind.Int32 or RuntimePrimitiveKind.UInt32 or
                RuntimePrimitiveKind.Int64 or RuntimePrimitiveKind.UInt64 or
                RuntimePrimitiveKind.NativeInt or RuntimePrimitiveKind.NativeUInt or
                RuntimePrimitiveKind.Single or RuntimePrimitiveKind.Double;

        private static int DefinitionSize(RuntimeType type)
        {
            if (type.GenericTypeDefinition is not RuntimeType definition ||
                definition.Namespace != "System.Runtime.Intrinsics")
                return 0;
            return definition.Name switch
            {
                "Vector64`1" => 8,
                "Vector128`1" => 16,
                "Vector256`1" => 32,
                "Vector512`1" => 64,
                _ => 0,
            };
        }
    }
}
