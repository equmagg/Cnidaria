using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.Wasm;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace System
{
    internal static partial class PackedSpanHelpers
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool CanUsePackedIndexOf<T>(T value)
        {
            return Unsafe.BitCast<T, ushort>(value) - 1u < 254u;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeFirstIndex(ref short searchSpace, ref short current, Vector128<byte> equals)
        {
            uint notEqualsElements = equals.ExtractMostSignificantBits();
            int index = BitOperations.TrailingZeroCount(notEqualsElements);
            return index + (int)((nuint)Unsafe.ByteOffset(ref searchSpace, ref current) / sizeof(short));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeFirstIndex(ref short searchSpace, ref short current, Vector256<byte> equals)
        {
            uint notEqualsElements = FixUpPackedVector256Result(equals).ExtractMostSignificantBits();
            int index = BitOperations.TrailingZeroCount(notEqualsElements);
            return index + (int)((nuint)Unsafe.ByteOffset(ref searchSpace, ref current) / sizeof(short));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeFirstIndex(ref short searchSpace, ref short current, Vector512<byte> equals)
        {
            ulong notEqualsElements = FixUpPackedVector512Result(equals).ExtractMostSignificantBits();
            int index = BitOperations.TrailingZeroCount(notEqualsElements);
            return index + (int)((nuint)Unsafe.ByteOffset(ref searchSpace, ref current) / sizeof(short));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeFirstIndexOverlapped(ref short searchSpace, ref short current0, ref short current1, Vector128<byte> equals)
        {
            uint notEqualsElements = equals.ExtractMostSignificantBits();
            int offsetInVector = BitOperations.TrailingZeroCount(notEqualsElements);
            if (offsetInVector >= Vector128<short>.Count)
            {
                // We matched within the second vector
                current0 = ref current1;
                offsetInVector -= Vector128<short>.Count;
            }
            return offsetInVector + (int)((nuint)Unsafe.ByteOffset(ref searchSpace, ref current0) / sizeof(short));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeFirstIndexOverlapped(ref short searchSpace, ref short current0, ref short current1, Vector256<byte> equals)
        {
            uint notEqualsElements = FixUpPackedVector256Result(equals).ExtractMostSignificantBits();
            int offsetInVector = BitOperations.TrailingZeroCount(notEqualsElements);
            if (offsetInVector >= Vector256<short>.Count)
            {
                // We matched within the second vector
                current0 = ref current1;
                offsetInVector -= Vector256<short>.Count;
            }
            return offsetInVector + (int)((nuint)Unsafe.ByteOffset(ref searchSpace, ref current0) / sizeof(short));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeFirstIndexOverlapped(ref short searchSpace, ref short current0, ref short current1, Vector512<byte> equals)
        {
            ulong notEqualsElements = FixUpPackedVector512Result(equals).ExtractMostSignificantBits();
            int offsetInVector = BitOperations.TrailingZeroCount(notEqualsElements);
            if (offsetInVector >= Vector512<short>.Count)
            {
                // We matched within the second vector
                current0 = ref current1;
                offsetInVector -= Vector512<short>.Count;
            }
            return offsetInVector + (int)((nuint)Unsafe.ByteOffset(ref searchSpace, ref current0) / sizeof(short));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<byte> FixUpPackedVector256Result(Vector256<byte> result)
        {
            // Avx2.PackUnsignedSaturate(Vector256.Create((short)1), Vector256.Create((short)2)) will result in
            // 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2
            // We want to swap the X and Y bits
            // 1, 1, 1, 1, 1, 1, 1, 1, X, X, X, X, X, X, X, X, Y, Y, Y, Y, Y, Y, Y, Y, 2, 2, 2, 2, 2, 2, 2, 2
            return Avx2.Permute4x64(result.AsInt64(), 0b_11_01_10_00).AsByte();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<byte> FixUpPackedVector512Result(Vector512<byte> result)
        {
            // Avx512BW.PackUnsignedSaturate will interleave the inputs in 8-byte blocks.
            // We want to preserve the order of the two input vectors, so we deinterleave the packed value.
            return Avx512F.PermuteVar8x64(result.AsInt64(), Vector512.Create(0, 2, 4, 6, 1, 3, 5, 7)).AsByte();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector512<byte> GetMatchMask<TNegator>(Vector512<byte> left, Vector512<byte> right)
             where TNegator : struct, SpanHelpers.INegator<short>
        {
            return (typeof(TNegator) == typeof(SpanHelpers.DontNegate<short>))
                 ? Vector512.Equals(left, right) : ~Vector512.Equals(left, right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasMatch<TNegator>(Vector512<byte> left, Vector512<byte> right)
            where TNegator : struct, SpanHelpers.INegator<short>
        {
            return (typeof(TNegator) == typeof(SpanHelpers.DontNegate<short>))
                 ? Vector512.EqualsAny(left, right) : !Vector512.EqualsAll(left, right);
        }

        private interface ITransform
        {
            static abstract short TransformInput(short input);
            static abstract Vector128<byte> TransformInput(Vector128<byte> input);
            static abstract Vector256<byte> TransformInput(Vector256<byte> input);
            static abstract Vector512<byte> TransformInput(Vector512<byte> input);
        }

        private readonly struct NopTransform : ITransform
        {
            public static short TransformInput(short input) => input;
            public static Vector128<byte> TransformInput(Vector128<byte> input) => input;
            public static Vector256<byte> TransformInput(Vector256<byte> input) => input;
            public static Vector512<byte> TransformInput(Vector512<byte> input) => input;
        }

        private readonly struct Or20Transform : ITransform
        {
            public static short TransformInput(short input) => (short)(input | 0x20);
            public static Vector128<byte> TransformInput(Vector128<byte> input) => input | Vector128.Create((byte)0x20);
            public static Vector256<byte> TransformInput(Vector256<byte> input) => input | Vector256.Create((byte)0x20);
            public static Vector512<byte> TransformInput(Vector512<byte> input) => input | Vector512.Create((byte)0x20);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOf(ref char searchSpace, char value, int length) =>
            IndexOf<SpanHelpers.DontNegate<short>, NopTransform>(ref Unsafe.As<char, short>(ref searchSpace), (short)value, length);

        private static int IndexOf<TNegator, TTransform>(ref short searchSpace, short value, int length)
            where TNegator : struct, SpanHelpers.INegator<short>
            where TTransform : struct, ITransform
        {
            if (length < Vector128<short>.Count)
            {
                nuint offset = 0;

                if (length >= 4)
                {
                    length -= 4;

                    if (TNegator.NegateIfNeeded(TTransform.TransformInput(searchSpace) == value)) return 0;
                    if (TNegator.NegateIfNeeded(TTransform.TransformInput(Unsafe.Add(ref searchSpace, 1)) == value)) return 1;
                    if (TNegator.NegateIfNeeded(TTransform.TransformInput(Unsafe.Add(ref searchSpace, 2)) == value)) return 2;
                    if (TNegator.NegateIfNeeded(TTransform.TransformInput(Unsafe.Add(ref searchSpace, 3)) == value)) return 3;

                    offset = 4;
                }

                while (length > 0)
                {
                    length -= 1;

                    if (TNegator.NegateIfNeeded(TTransform.TransformInput(Unsafe.Add(ref searchSpace, offset)) == value)) return (int)offset;

                    offset += 1;
                }
            }
            else
            {
                ref short currentSearchSpace = ref searchSpace;

#pragma warning disable IntrinsicsInSystemPrivateCoreLibAttributeNotSpecificEnough // The else condition for this if statement is identical in semantics to Avx2 specific code
                if (Avx512BW.IsSupported && Vector512.IsHardwareAccelerated && length > Vector512<short>.Count)
#pragma warning restore IntrinsicsInSystemPrivateCoreLibAttributeNotSpecificEnough
                {
                    Vector512<byte> packedValue = Vector512.Create((byte)value);

                    if (length > 2 * Vector512<short>.Count)
                    {
                        // Process the input in chunks of 64 characters (2 * Vector512<short>).
                        // If the input length is a multiple of 64, don't consume the last 16 characters in this loop.
                        // Let the fallback below handle it instead. This is why the condition is
                        // ">" instead of ">=" above, and why "IsAddressLessThan" is used instead of "IsAddressLessThanOrEqualTo".
                        ref short twoVectorsAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - (2 * Vector512<short>.Count));

                        do
                        {
                            Vector512<short> source0 = Vector512.LoadUnsafe(ref currentSearchSpace);
                            Vector512<short> source1 = Vector512.LoadUnsafe(ref currentSearchSpace, (nuint)Vector512<short>.Count);
                            Vector512<byte> packedSource = TTransform.TransformInput(PackSources(source0, source1));

                            if (HasMatch<TNegator>(packedValue, packedSource))
                            {
                                return ComputeFirstIndex(ref searchSpace, ref currentSearchSpace, GetMatchMask<TNegator>(packedValue, packedSource));
                            }

                            currentSearchSpace = ref Unsafe.Add(ref currentSearchSpace, 2 * Vector512<short>.Count);
                        }
                        while (Unsafe.IsAddressLessThan(ref currentSearchSpace, ref twoVectorsAwayFromEnd));
                    }

                    // We have 1-32 characters remaining. Process the first and last vector in the search space.
                    // They may overlap, but we'll handle that in the index calculation if we do get a match.
                    {
                        ref short oneVectorAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - Vector512<short>.Count);

                        ref short firstVector = ref Unsafe.IsAddressGreaterThan(ref currentSearchSpace, ref oneVectorAwayFromEnd)
                            ? ref oneVectorAwayFromEnd
                            : ref currentSearchSpace;

                        Vector512<short> source0 = Vector512.LoadUnsafe(ref firstVector);
                        Vector512<short> source1 = Vector512.LoadUnsafe(ref oneVectorAwayFromEnd);
                        Vector512<byte> packedSource = TTransform.TransformInput(PackSources(source0, source1));

                        if (HasMatch<TNegator>(packedValue, packedSource))
                        {
                            return ComputeFirstIndexOverlapped(ref searchSpace, ref firstVector, ref oneVectorAwayFromEnd, GetMatchMask<TNegator>(packedValue, packedSource));
                        }
                    }
                }
#pragma warning disable IntrinsicsInSystemPrivateCoreLibAttributeNotSpecificEnough // The else condition for this if statement is identical in semantics to Avx2 specific code
                else if (Avx2.IsSupported && length > Vector256<short>.Count)
#pragma warning restore IntrinsicsInSystemPrivateCoreLibAttributeNotSpecificEnough
                {
                    Vector256<byte> packedValue = Vector256.Create((byte)value);

                    if (length > 2 * Vector256<short>.Count)
                    {
                        // Process the input in chunks of 32 characters (2 * Vector256<short>).
                        // If the input length is a multiple of 32, don't consume the last 16 characters in this loop.
                        // Let the fallback below handle it instead. This is why the condition is
                        // ">" instead of ">=" above, and why "IsAddressLessThan" is used instead of "IsAddressLessThanOrEqualTo".
                        ref short twoVectorsAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - (2 * Vector256<short>.Count));

                        do
                        {
                            Vector256<short> source0 = Vector256.LoadUnsafe(ref currentSearchSpace);
                            Vector256<short> source1 = Vector256.LoadUnsafe(ref currentSearchSpace, (nuint)Vector256<short>.Count);
                            Vector256<byte> packedSource = TTransform.TransformInput(PackSources(source0, source1));
                            Vector256<byte> result = Vector256.Equals(packedValue, packedSource);
                            result = NegateIfNeeded<TNegator>(result);

                            if (result != Vector256<byte>.Zero)
                            {
                                return ComputeFirstIndex(ref searchSpace, ref currentSearchSpace, result);
                            }

                            currentSearchSpace = ref Unsafe.Add(ref currentSearchSpace, 2 * Vector256<short>.Count);
                        }
                        while (Unsafe.IsAddressLessThan(ref currentSearchSpace, ref twoVectorsAwayFromEnd));
                    }

                    // We have 1-32 characters remaining. Process the first and last vector in the search space.
                    // They may overlap, but we'll handle that in the index calculation if we do get a match.
                    {
                        ref short oneVectorAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - Vector256<short>.Count);

                        ref short firstVector = ref Unsafe.IsAddressGreaterThan(ref currentSearchSpace, ref oneVectorAwayFromEnd)
                            ? ref oneVectorAwayFromEnd
                            : ref currentSearchSpace;

                        Vector256<short> source0 = Vector256.LoadUnsafe(ref firstVector);
                        Vector256<short> source1 = Vector256.LoadUnsafe(ref oneVectorAwayFromEnd);
                        Vector256<byte> packedSource = TTransform.TransformInput(PackSources(source0, source1));
                        Vector256<byte> result = Vector256.Equals(packedValue, packedSource);
                        result = NegateIfNeeded<TNegator>(result);

                        if (result != Vector256<byte>.Zero)
                        {
                            return ComputeFirstIndexOverlapped(ref searchSpace, ref firstVector, ref oneVectorAwayFromEnd, result);
                        }
                    }
                }
                else
                {
                    Vector128<byte> packedValue = Vector128.Create((byte)value);

#pragma warning disable IntrinsicsInSystemPrivateCoreLibConditionParsing // A negated IsSupported condition isn't parseable by the intrinsics analyzer, but in this case, it is only used in combination
                                                                         // with the check above of Avx2.IsSupported && length > Vector256<short>.Count which makes the logic
                                                                         // in this if statement dead code when Avx2.IsSupported. Presumably this negated IsSupported check is to assist the JIT in
                                                                         // not generating dead code.
#pragma warning disable IntrinsicsInSystemPrivateCoreLibAttributeNotSpecificEnough // This is paired with the check above, and since these if statements are contained in 1 function, the code
                                                                                   // may take a dependence on the JIT compiler producing a consistent value for the result of a call to IsSupported
                                                                                   // This logic MUST NOT be extracted to a helper function
                    if (!Avx2.IsSupported && length > 2 * Vector128<short>.Count)
#pragma warning restore IntrinsicsInSystemPrivateCoreLibAttributeNotSpecificEnough
#pragma warning restore IntrinsicsInSystemPrivateCoreLibConditionParsing
                    {
                        // Process the input in chunks of 16 characters (2 * Vector128<short>).
                        // If the input length is a multiple of 16, don't consume the last 16 characters in this loop.
                        // Let the fallback below handle it instead. This is why the condition is
                        // ">" instead of ">=" above, and why "IsAddressLessThan" is used instead of "IsAddressLessThanOrEqualTo".
                        ref short twoVectorsAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - (2 * Vector128<short>.Count));

                        do
                        {
                            Vector128<short> source0 = Vector128.LoadUnsafe(ref currentSearchSpace);
                            Vector128<short> source1 = Vector128.LoadUnsafe(ref currentSearchSpace, (nuint)Vector128<short>.Count);
                            Vector128<byte> packedSource = TTransform.TransformInput(PackSources(source0, source1));
                            Vector128<byte> result = Vector128.Equals(packedValue, packedSource);
                            result = NegateIfNeeded<TNegator>(result);

                            if (result != Vector128<byte>.Zero)
                            {
                                return ComputeFirstIndex(ref searchSpace, ref currentSearchSpace, result);
                            }

                            currentSearchSpace = ref Unsafe.Add(ref currentSearchSpace, 2 * Vector128<short>.Count);
                        }
                        while (Unsafe.IsAddressLessThan(ref currentSearchSpace, ref twoVectorsAwayFromEnd));
                    }

                    // We have 1-16 characters remaining. Process the first and last vector in the search space.
                    // They may overlap, but we'll handle that in the index calculation if we do get a match.
                    {
                        ref short oneVectorAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - Vector128<short>.Count);

                        ref short firstVector = ref Unsafe.IsAddressGreaterThan(ref currentSearchSpace, ref oneVectorAwayFromEnd)
                            ? ref oneVectorAwayFromEnd
                            : ref currentSearchSpace;

                        Vector128<short> source0 = Vector128.LoadUnsafe(ref firstVector);
                        Vector128<short> source1 = Vector128.LoadUnsafe(ref oneVectorAwayFromEnd);
                        Vector128<byte> packedSource = TTransform.TransformInput(PackSources(source0, source1));
                        Vector128<byte> result = Vector128.Equals(packedValue, packedSource);
                        result = NegateIfNeeded<TNegator>(result);

                        if (result != Vector128<byte>.Zero)
                        {
                            return ComputeFirstIndexOverlapped(ref searchSpace, ref firstVector, ref oneVectorAwayFromEnd, result);
                        }
                    }
                }
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOfAny(ref char searchSpace, char value0, char value1, int length) =>
            IndexOfAny<SpanHelpers.DontNegate<short>, NopTransform>(ref Unsafe.As<char, short>(ref searchSpace), (short)value0, (short)value1, length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOfAny(ref char searchSpace, char value0, char value1, char value2, int length) =>
            IndexOfAny<SpanHelpers.DontNegate<short>>(ref Unsafe.As<char, short>(ref searchSpace), (short)value0, (short)value1, (short)value2, length);

        private static int IndexOfAny<TNegator, TTransform>(ref short searchSpace, short value0, short value1, int length)
            where TNegator : struct, SpanHelpers.INegator<short>
            where TTransform : struct, ITransform
        {
            if (length < Vector128<short>.Count)
            {
                nuint offset = 0;
                short lookUp;

                if (length >= 4)
                {
                    length -= 4;

                    lookUp = TTransform.TransformInput(searchSpace);
                    if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1)) return 0;
                    lookUp = TTransform.TransformInput(Unsafe.Add(ref searchSpace, 1));
                    if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1)) return 1;
                    lookUp = TTransform.TransformInput(Unsafe.Add(ref searchSpace, 2));
                    if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1)) return 2;
                    lookUp = TTransform.TransformInput(Unsafe.Add(ref searchSpace, 3));
                    if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1)) return 3;

                    offset = 4;
                }

                while (length > 0)
                {
                    length -= 1;

                    lookUp = TTransform.TransformInput(Unsafe.Add(ref searchSpace, offset));
                    if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1)) return (int)offset;

                    offset += 1;
                }
            }
            else
            {
                ref short currentSearchSpace = ref searchSpace;
#pragma warning disable IntrinsicsInSystemPrivateCoreLibAttributeNotSpecificEnough // The else condition for this if statement is identical in semantics to Avx2 specific code
                if (Avx512BW.IsSupported && Vector512.IsHardwareAccelerated && length > Vector512<short>.Count)
#pragma warning restore IntrinsicsInSystemPrivateCoreLibAttributeNotSpecificEnough
                {
                    Vector512<byte> packedValue0 = Vector512.Create((byte)value0);
                    Vector512<byte> packedValue1 = Vector512.Create((byte)value1);

                    if (length > 2 * Vector512<short>.Count)
                    {
                        // Process the input in chunks of 64 characters (2 * Vector512<short>).
                        // If the input length is a multiple of 64, don't consume the last 16 characters in this loop.
                        // Let the fallback below handle it instead. This is why the condition is
                        // ">" instead of ">=" above, and why "IsAddressLessThan" is used instead of "IsAddressLessThanOrEqualTo".
                        ref short twoVectorsAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - (2 * Vector512<short>.Count));

                        do
                        {
                            Vector512<short> source0 = Vector512.LoadUnsafe(ref currentSearchSpace);
                            Vector512<short> source1 = Vector512.LoadUnsafe(ref currentSearchSpace, (nuint)Vector512<short>.Count);
                            Vector512<byte> packedSource = TTransform.TransformInput(PackSources(source0, source1));
                            Vector512<byte> result = NegateIfNeeded<TNegator>(Vector512.Equals(packedValue0, packedSource) | Vector512.Equals(packedValue1, packedSource));

                            if (result != Vector512<byte>.Zero)
                            {
                                return ComputeFirstIndex(ref searchSpace, ref currentSearchSpace, result);
                            }

                            currentSearchSpace = ref Unsafe.Add(ref currentSearchSpace, 2 * Vector512<short>.Count);
                        }
                        while (Unsafe.IsAddressLessThan(ref currentSearchSpace, ref twoVectorsAwayFromEnd));
                    }

                    // We have 1-32 characters remaining. Process the first and last vector in the search space.
                    // They may overlap, but we'll handle that in the index calculation if we do get a match.
                    {
                        ref short oneVectorAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - Vector512<short>.Count);

                        ref short firstVector = ref Unsafe.IsAddressGreaterThan(ref currentSearchSpace, ref oneVectorAwayFromEnd)
                            ? ref oneVectorAwayFromEnd
                            : ref currentSearchSpace;

                        Vector512<short> source0 = Vector512.LoadUnsafe(ref firstVector);
                        Vector512<short> source1 = Vector512.LoadUnsafe(ref oneVectorAwayFromEnd);
                        Vector512<byte> packedSource = TTransform.TransformInput(PackSources(source0, source1));
                        Vector512<byte> result = NegateIfNeeded<TNegator>(Vector512.Equals(packedValue0, packedSource) | Vector512.Equals(packedValue1, packedSource));

                        if (result != Vector512<byte>.Zero)
                        {
                            return ComputeFirstIndexOverlapped(ref searchSpace, ref firstVector, ref oneVectorAwayFromEnd, result);
                        }
                    }
                }
#pragma warning disable IntrinsicsInSystemPrivateCoreLibAttributeNotSpecificEnough // The else condition for this if statement is identical in semantics to Avx2 specific code
                else if (Avx2.IsSupported && length > Vector256<short>.Count)
#pragma warning restore IntrinsicsInSystemPrivateCoreLibAttributeNotSpecificEnough
                {
                    Vector256<byte> packedValue0 = Vector256.Create((byte)value0);
                    Vector256<byte> packedValue1 = Vector256.Create((byte)value1);

                    if (length > 2 * Vector256<short>.Count)
                    {
                        // Process the input in chunks of 32 characters (2 * Vector256<short>).
                        // If the input length is a multiple of 32, don't consume the last 16 characters in this loop.
                        // Let the fallback below handle it instead. This is why the condition is
                        // ">" instead of ">=" above, and why "IsAddressLessThan" is used instead of "IsAddressLessThanOrEqualTo".
                        ref short twoVectorsAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - (2 * Vector256<short>.Count));

                        do
                        {
                            Vector256<short> source0 = Vector256.LoadUnsafe(ref currentSearchSpace);
                            Vector256<short> source1 = Vector256.LoadUnsafe(ref currentSearchSpace, (nuint)Vector256<short>.Count);
                            Vector256<byte> packedSource = TTransform.TransformInput(PackSources(source0, source1));
                            Vector256<byte> result = Vector256.Equals(packedValue0, packedSource) | Vector256.Equals(packedValue1, packedSource);
                            result = NegateIfNeeded<TNegator>(result);

                            if (result != Vector256<byte>.Zero)
                            {
                                return ComputeFirstIndex(ref searchSpace, ref currentSearchSpace, result);
                            }

                            currentSearchSpace = ref Unsafe.Add(ref currentSearchSpace, 2 * Vector256<short>.Count);
                        }
                        while (Unsafe.IsAddressLessThan(ref currentSearchSpace, ref twoVectorsAwayFromEnd));
                    }

                    // We have 1-32 characters remaining. Process the first and last vector in the search space.
                    // They may overlap, but we'll handle that in the index calculation if we do get a match.
                    {
                        ref short oneVectorAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - Vector256<short>.Count);

                        ref short firstVector = ref Unsafe.IsAddressGreaterThan(ref currentSearchSpace, ref oneVectorAwayFromEnd)
                            ? ref oneVectorAwayFromEnd
                            : ref currentSearchSpace;

                        Vector256<short> source0 = Vector256.LoadUnsafe(ref firstVector);
                        Vector256<short> source1 = Vector256.LoadUnsafe(ref oneVectorAwayFromEnd);
                        Vector256<byte> packedSource = TTransform.TransformInput(PackSources(source0, source1));
                        Vector256<byte> result = Vector256.Equals(packedValue0, packedSource) | Vector256.Equals(packedValue1, packedSource);
                        result = NegateIfNeeded<TNegator>(result);

                        if (result != Vector256<byte>.Zero)
                        {
                            return ComputeFirstIndexOverlapped(ref searchSpace, ref firstVector, ref oneVectorAwayFromEnd, result);
                        }
                    }
                }
                else
                {
                    Vector128<byte> packedValue0 = Vector128.Create((byte)value0);
                    Vector128<byte> packedValue1 = Vector128.Create((byte)value1);

#pragma warning disable IntrinsicsInSystemPrivateCoreLibConditionParsing // A negated IsSupported condition isn't parseable by the intrinsics analyzer, but in this case, it is only used in combination
                                                                         // with the check above of Avx2.IsSupported && length > Vector256<short>.Count which makes the logic
                                                                         // in this if statement dead code when Avx2.IsSupported. Presumably this negated IsSupported check is to assist the JIT in
                                                                         // not generating dead code.
#pragma warning disable IntrinsicsInSystemPrivateCoreLibAttributeNotSpecificEnough // This is paired with the check above, and since these if statements are contained in 1 function, the code
                                                                                   // may take a dependence on the JIT compiler producing a consistent value for the result of a call to IsSupported
                                                                                   // This logic MUST NOT be extracted to a helper function
                    if (!Avx2.IsSupported && length > 2 * Vector128<short>.Count)
#pragma warning restore IntrinsicsInSystemPrivateCoreLibAttributeNotSpecificEnough
#pragma warning restore IntrinsicsInSystemPrivateCoreLibConditionParsing
                    {
                        // Process the input in chunks of 16 characters (2 * Vector128<short>).
                        // If the input length is a multiple of 16, don't consume the last 16 characters in this loop.
                        // Let the fallback below handle it instead. This is why the condition is
                        // ">" instead of ">=" above, and why "IsAddressLessThan" is used instead of "IsAddressLessThanOrEqualTo".
                        ref short twoVectorsAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - (2 * Vector128<short>.Count));

                        do
                        {
                            Vector128<short> source0 = Vector128.LoadUnsafe(ref currentSearchSpace);
                            Vector128<short> source1 = Vector128.LoadUnsafe(ref currentSearchSpace, (nuint)Vector128<short>.Count);
                            Vector128<byte> packedSource = TTransform.TransformInput(PackSources(source0, source1));
                            Vector128<byte> result = Vector128.Equals(packedValue0, packedSource) | Vector128.Equals(packedValue1, packedSource);
                            result = NegateIfNeeded<TNegator>(result);

                            if (result != Vector128<byte>.Zero)
                            {
                                return ComputeFirstIndex(ref searchSpace, ref currentSearchSpace, result);
                            }

                            currentSearchSpace = ref Unsafe.Add(ref currentSearchSpace, 2 * Vector128<short>.Count);
                        }
                        while (Unsafe.IsAddressLessThan(ref currentSearchSpace, ref twoVectorsAwayFromEnd));
                    }

                    // We have 1-16 characters remaining. Process the first and last vector in the search space.
                    // They may overlap, but we'll handle that in the index calculation if we do get a match.
                    {
                        ref short oneVectorAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - Vector128<short>.Count);

                        ref short firstVector = ref Unsafe.IsAddressGreaterThan(ref currentSearchSpace, ref oneVectorAwayFromEnd)
                            ? ref oneVectorAwayFromEnd
                            : ref currentSearchSpace;

                        Vector128<short> source0 = Vector128.LoadUnsafe(ref firstVector);
                        Vector128<short> source1 = Vector128.LoadUnsafe(ref oneVectorAwayFromEnd);
                        Vector128<byte> packedSource = TTransform.TransformInput(PackSources(source0, source1));
                        Vector128<byte> result = Vector128.Equals(packedValue0, packedSource) | Vector128.Equals(packedValue1, packedSource);
                        result = NegateIfNeeded<TNegator>(result);

                        if (result != Vector128<byte>.Zero)
                        {
                            return ComputeFirstIndexOverlapped(ref searchSpace, ref firstVector, ref oneVectorAwayFromEnd, result);
                        }
                    }
                }
            }

            return -1;
        }

        private static int IndexOfAny<TNegator>(ref short searchSpace, short value0, short value1, short value2, int length)
            where TNegator : struct, SpanHelpers.INegator<short>
        {
            if (length < Vector128<short>.Count)
            {
                nuint offset = 0;
                short lookUp;

                if (length >= 4)
                {
                    length -= 4;

                    lookUp = searchSpace;
                    if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1 || lookUp == value2)) return 0;
                    lookUp = Unsafe.Add(ref searchSpace, 1);
                    if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1 || lookUp == value2)) return 1;
                    lookUp = Unsafe.Add(ref searchSpace, 2);
                    if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1 || lookUp == value2)) return 2;
                    lookUp = Unsafe.Add(ref searchSpace, 3);
                    if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1 || lookUp == value2)) return 3;

                    offset = 4;
                }

                while (length > 0)
                {
                    length -= 1;

                    lookUp = Unsafe.Add(ref searchSpace, offset);
                    if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1 || lookUp == value2)) return (int)offset;

                    offset += 1;
                }
            }
            else
            {
                ref short currentSearchSpace = ref searchSpace;

#pragma warning disable IntrinsicsInSystemPrivateCoreLibAttributeNotSpecificEnough // The else condition for this if statement is identical in semantics to Avx2 specific code
                if (Avx512BW.IsSupported && Vector512.IsHardwareAccelerated && length > Vector512<short>.Count)
#pragma warning restore IntrinsicsInSystemPrivateCoreLibAttributeNotSpecificEnough
                {
                    Vector512<byte> packedValue0 = Vector512.Create((byte)value0);
                    Vector512<byte> packedValue1 = Vector512.Create((byte)value1);
                    Vector512<byte> packedValue2 = Vector512.Create((byte)value2);

                    if (length > 2 * Vector512<short>.Count)
                    {
                        // Process the input in chunks of 64 characters (2 * Vector512<short>).
                        // If the input length is a multiple of 64, don't consume the last 16 characters in this loop.
                        // Let the fallback below handle it instead. This is why the condition is
                        // ">" instead of ">=" above, and why "IsAddressLessThan" is used instead of "IsAddressLessThanOrEqualTo".
                        ref short twoVectorsAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - (2 * Vector512<short>.Count));

                        do
                        {
                            Vector512<short> source0 = Vector512.LoadUnsafe(ref currentSearchSpace);
                            Vector512<short> source1 = Vector512.LoadUnsafe(ref currentSearchSpace, (nuint)Vector512<short>.Count);
                            Vector512<byte> packedSource = PackSources(source0, source1);
                            Vector512<byte> result = NegateIfNeeded<TNegator>(Vector512.Equals(packedValue0, packedSource) | Vector512.Equals(packedValue1, packedSource) | Vector512.Equals(packedValue2, packedSource));

                            if (result != Vector512<byte>.Zero)
                            {
                                return ComputeFirstIndex(ref searchSpace, ref currentSearchSpace, result);
                            }

                            currentSearchSpace = ref Unsafe.Add(ref currentSearchSpace, 2 * Vector512<short>.Count);
                        }
                        while (Unsafe.IsAddressLessThan(ref currentSearchSpace, ref twoVectorsAwayFromEnd));
                    }

                    // We have 1-32 characters remaining. Process the first and last vector in the search space.
                    // They may overlap, but we'll handle that in the index calculation if we do get a match.
                    {
                        ref short oneVectorAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - Vector512<short>.Count);

                        ref short firstVector = ref Unsafe.IsAddressGreaterThan(ref currentSearchSpace, ref oneVectorAwayFromEnd)
                            ? ref oneVectorAwayFromEnd
                            : ref currentSearchSpace;

                        Vector512<short> source0 = Vector512.LoadUnsafe(ref firstVector);
                        Vector512<short> source1 = Vector512.LoadUnsafe(ref oneVectorAwayFromEnd);
                        Vector512<byte> packedSource = PackSources(source0, source1);
                        Vector512<byte> result = NegateIfNeeded<TNegator>(Vector512.Equals(packedValue0, packedSource) | Vector512.Equals(packedValue1, packedSource) | Vector512.Equals(packedValue2, packedSource));

                        if (result != Vector512<byte>.Zero)
                        {
                            return ComputeFirstIndexOverlapped(ref searchSpace, ref firstVector, ref oneVectorAwayFromEnd, result);
                        }
                    }
                }
#pragma warning disable IntrinsicsInSystemPrivateCoreLibAttributeNotSpecificEnough // The else condition for this if statement is identical in semantics to Avx2 specific code
                else if (Avx2.IsSupported && length > Vector256<short>.Count)
#pragma warning restore IntrinsicsInSystemPrivateCoreLibAttributeNotSpecificEnough
                {
                    Vector256<byte> packedValue0 = Vector256.Create((byte)value0);
                    Vector256<byte> packedValue1 = Vector256.Create((byte)value1);
                    Vector256<byte> packedValue2 = Vector256.Create((byte)value2);

                    if (length > 2 * Vector256<short>.Count)
                    {
                        // Process the input in chunks of 32 characters (2 * Vector256<short>).
                        // If the input length is a multiple of 32, don't consume the last 16 characters in this loop.
                        // Let the fallback below handle it instead. This is why the condition is
                        // ">" instead of ">=" above, and why "IsAddressLessThan" is used instead of "IsAddressLessThanOrEqualTo".
                        ref short twoVectorsAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - (2 * Vector256<short>.Count));

                        do
                        {
                            Vector256<short> source0 = Vector256.LoadUnsafe(ref currentSearchSpace);
                            Vector256<short> source1 = Vector256.LoadUnsafe(ref currentSearchSpace, (nuint)Vector256<short>.Count);
                            Vector256<byte> packedSource = PackSources(source0, source1);
                            Vector256<byte> result = Vector256.Equals(packedValue0, packedSource) | Vector256.Equals(packedValue1, packedSource) | Vector256.Equals(packedValue2, packedSource);
                            result = NegateIfNeeded<TNegator>(result);

                            if (result != Vector256<byte>.Zero)
                            {
                                return ComputeFirstIndex(ref searchSpace, ref currentSearchSpace, result);
                            }

                            currentSearchSpace = ref Unsafe.Add(ref currentSearchSpace, 2 * Vector256<short>.Count);
                        }
                        while (Unsafe.IsAddressLessThan(ref currentSearchSpace, ref twoVectorsAwayFromEnd));
                    }

                    // We have 1-32 characters remaining. Process the first and last vector in the search space.
                    // They may overlap, but we'll handle that in the index calculation if we do get a match.
                    {
                        ref short oneVectorAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - Vector256<short>.Count);

                        ref short firstVector = ref Unsafe.IsAddressGreaterThan(ref currentSearchSpace, ref oneVectorAwayFromEnd)
                            ? ref oneVectorAwayFromEnd
                            : ref currentSearchSpace;

                        Vector256<short> source0 = Vector256.LoadUnsafe(ref firstVector);
                        Vector256<short> source1 = Vector256.LoadUnsafe(ref oneVectorAwayFromEnd);
                        Vector256<byte> packedSource = PackSources(source0, source1);
                        Vector256<byte> result = Vector256.Equals(packedValue0, packedSource) | Vector256.Equals(packedValue1, packedSource) | Vector256.Equals(packedValue2, packedSource);
                        result = NegateIfNeeded<TNegator>(result);

                        if (result != Vector256<byte>.Zero)
                        {
                            return ComputeFirstIndexOverlapped(ref searchSpace, ref firstVector, ref oneVectorAwayFromEnd, result);
                        }
                    }
                }
                else
                {
                    Vector128<byte> packedValue0 = Vector128.Create((byte)value0);
                    Vector128<byte> packedValue1 = Vector128.Create((byte)value1);
                    Vector128<byte> packedValue2 = Vector128.Create((byte)value2);

#pragma warning disable IntrinsicsInSystemPrivateCoreLibConditionParsing // A negated IsSupported condition isn't parseable by the intrinsics analyzer, but in this case, it is only used in combination
                                                                         // with the check above of Avx2.IsSupported && length > Vector256<short>.Count which makes the logic
                                                                         // in this if statement dead code when Avx2.IsSupported. Presumably this negated IsSupported check is to assist the JIT in
                                                                         // not generating dead code.
#pragma warning disable IntrinsicsInSystemPrivateCoreLibAttributeNotSpecificEnough // This is paired with the check above, and since these if statements are contained in 1 function, the code
                                                                                   // may take a dependence on the JIT compiler producing a consistent value for the result of a call to IsSupported
                                                                                   // This logic MUST NOT be extracted to a helper function
                    if (!Avx2.IsSupported && length > 2 * Vector128<short>.Count)
#pragma warning restore IntrinsicsInSystemPrivateCoreLibAttributeNotSpecificEnough
#pragma warning restore IntrinsicsInSystemPrivateCoreLibConditionParsing
                    {
                        // Process the input in chunks of 16 characters (2 * Vector128<short>).
                        // If the input length is a multiple of 16, don't consume the last 16 characters in this loop.
                        // Let the fallback below handle it instead. This is why the condition is
                        // ">" instead of ">=" above, and why "IsAddressLessThan" is used instead of "IsAddressLessThanOrEqualTo".
                        ref short twoVectorsAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - (2 * Vector128<short>.Count));

                        do
                        {
                            Vector128<short> source0 = Vector128.LoadUnsafe(ref currentSearchSpace);
                            Vector128<short> source1 = Vector128.LoadUnsafe(ref currentSearchSpace, (nuint)Vector128<short>.Count);
                            Vector128<byte> packedSource = PackSources(source0, source1);
                            Vector128<byte> result = Vector128.Equals(packedValue0, packedSource) | Vector128.Equals(packedValue1, packedSource) | Vector128.Equals(packedValue2, packedSource);
                            result = NegateIfNeeded<TNegator>(result);

                            if (result != Vector128<byte>.Zero)
                            {
                                return ComputeFirstIndex(ref searchSpace, ref currentSearchSpace, result);
                            }

                            currentSearchSpace = ref Unsafe.Add(ref currentSearchSpace, 2 * Vector128<short>.Count);
                        }
                        while (Unsafe.IsAddressLessThan(ref currentSearchSpace, ref twoVectorsAwayFromEnd));
                    }

                    // We have 1-16 characters remaining. Process the first and last vector in the search space.
                    // They may overlap, but we'll handle that in the index calculation if we do get a match.
                    {
                        ref short oneVectorAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - Vector128<short>.Count);

                        ref short firstVector = ref Unsafe.IsAddressGreaterThan(ref currentSearchSpace, ref oneVectorAwayFromEnd)
                            ? ref oneVectorAwayFromEnd
                            : ref currentSearchSpace;

                        Vector128<short> source0 = Vector128.LoadUnsafe(ref firstVector);
                        Vector128<short> source1 = Vector128.LoadUnsafe(ref oneVectorAwayFromEnd);
                        Vector128<byte> packedSource = PackSources(source0, source1);
                        Vector128<byte> result = Vector128.Equals(packedValue0, packedSource) | Vector128.Equals(packedValue1, packedSource) | Vector128.Equals(packedValue2, packedSource);
                        result = NegateIfNeeded<TNegator>(result);

                        if (result != Vector128<byte>.Zero)
                        {
                            return ComputeFirstIndexOverlapped(ref searchSpace, ref firstVector, ref oneVectorAwayFromEnd, result);
                        }
                    }
                }
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOfAnyExcept(ref char searchSpace, char value, int length) =>
            IndexOf<SpanHelpers.Negate<short>, NopTransform>(ref Unsafe.As<char, short>(ref searchSpace), (short)value, length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOfAnyExcept(ref char searchSpace, char value0, char value1, int length) =>
            IndexOfAny<SpanHelpers.Negate<short>, NopTransform>(ref Unsafe.As<char, short>(ref searchSpace), (short)value0, (short)value1, length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOfAnyExcept(ref char searchSpace, char value0, char value1, char value2, int length) =>
            IndexOfAny<SpanHelpers.Negate<short>>(ref Unsafe.As<char, short>(ref searchSpace), (short)value0, (short)value1, (short)value2, length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOfAnyExceptIgnoreCase(ref char searchSpace, char value, int length)
        {
            return IndexOf<SpanHelpers.Negate<short>, Or20Transform>(ref Unsafe.As<char, short>(ref searchSpace), (short)value, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOfAnyExceptIgnoreCase(ref char searchSpace, char value0, char value1, int length)
        {
            return IndexOfAny<SpanHelpers.Negate<short>, Or20Transform>(ref Unsafe.As<char, short>(ref searchSpace), (short)value0, (short)value1, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOfAnyIgnoreCase(ref char searchSpace, char value, int length)
        {
            return IndexOf<SpanHelpers.DontNegate<short>, Or20Transform>(ref Unsafe.As<char, short>(ref searchSpace), (short)value, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOfAnyIgnoreCase(ref char searchSpace, char value0, char value1, int length)
        {
            return IndexOfAny<SpanHelpers.DontNegate<short>, Or20Transform>(ref Unsafe.As<char, short>(ref searchSpace), (short)value0, (short)value1, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool NegateIfNeeded<TNegator>(bool result)
            where TNegator : struct, SpanHelpers.INegator<short> =>
            typeof(TNegator) == typeof(SpanHelpers.DontNegate<short>) ? result : !result;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<byte> NegateIfNeeded<TNegator>(Vector128<byte> result)
            where TNegator : struct, SpanHelpers.INegator<short> =>
            typeof(TNegator) == typeof(SpanHelpers.DontNegate<short>) ? result : ~result;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<byte> NegateIfNeeded<TNegator>(Vector256<byte> result)
            where TNegator : struct, SpanHelpers.INegator<short> =>
            typeof(TNegator) == typeof(SpanHelpers.DontNegate<short>) ? result : ~result;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector512<byte> NegateIfNeeded<TNegator>(Vector512<byte> result)
            where TNegator : struct, SpanHelpers.INegator<short> =>
            typeof(TNegator) == typeof(SpanHelpers.DontNegate<short>) ? result : ~result;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector512<byte> PackSources(Vector512<short> source0, Vector512<short> source1)
        {
            // Pack two vectors of characters into bytes. While the type is Vector256<short>, these are really UInt16 characters.
            // X86: Downcast every character using saturation.
            // - Values <= 32767 result in min(value, 255).
            // - Values  > 32767 result in 0. Because of this we can't accept needles that contain 0.
            return Avx512BW.PackUnsignedSaturate(source0, source1).AsByte();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<byte> PackSources(Vector256<short> source0, Vector256<short> source1)
        {
            // Pack two vectors of characters into bytes. While the type is Vector256<short>, these are really UInt16 characters.
            // X86: Downcast every character using saturation.
            // - Values <= 32767 result in min(value, 255).
            // - Values  > 32767 result in 0. Because of this we can't accept needles that contain 0.
            return Avx2.PackUnsignedSaturate(source0, source1).AsByte();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<byte> PackSources(Vector128<short> source0, Vector128<short> source1)
        {
            // Pack two vectors of characters into bytes. While the type is Vector128<short>, these are really UInt16 characters.
            // X86: Downcast every character using saturation.
            // - Values <= 32767 result in min(value, 255).
            // - Values  > 32767 result in 0. Because of this we can't accept needles that contain 0.
            return Sse2.PackUnsignedSaturate(source0, source1).AsByte();
        }

        public static bool PackedIndexOfIsSupported => Sse2.IsSupported;
    }
    public static class Environment
    {
        internal const bool IsDebug = false;
        internal const bool IsRelease = !IsDebug;
        public static bool Is64BitProcess => IntPtr.Size == 8;
        internal const string NewLineConst = "\n";
        public static string NewLine => NewLineConst;
        private static volatile OperatingSystem? s_osVersion;
        public static OperatingSystem OSVersion
        {
            get
            {
                OperatingSystem? osVersion = s_osVersion;
                if (osVersion == null)
                {
                    System.Threading.Interlocked.CompareExchange(ref s_osVersion, GetOSVersion(), null);
                    osVersion = s_osVersion;
                }
                return osVersion;
            }
        }

        private static OperatingSystem GetOSVersion()
        {
            throw new NotSupportedException();
        }

        public static int ProcessorCount { get; } = 1;
        /// <summary>Gets the number of milliseconds elapsed since the system started.</summary>
        /// <value>A 32-bit signed integer containing the amount of time in milliseconds that has passed since the last time the computer was started.</value>
        public static int TickCount => (int)TickCount64;
        /// <summary>Gets the number of milliseconds elapsed since the system started.</summary>
        public static long TickCount64 => throw new NotSupportedException();
    }

    public struct Void { }
    public struct RuntimeTypeHandle : IEquatable<RuntimeTypeHandle>
    {
        internal readonly IntPtr _value;

        internal RuntimeTypeHandle(IntPtr value) => _value = value;

        public IntPtr Value => _value;

        public override bool Equals(object? obj) => obj is RuntimeTypeHandle handle && handle._value == _value;

        public bool Equals(RuntimeTypeHandle handle) => handle._value == _value;

        public override int GetHashCode() => _value.GetHashCode();

        public static bool operator ==(RuntimeTypeHandle left, RuntimeTypeHandle right) => left._value == right._value;

        public static bool operator !=(RuntimeTypeHandle left, RuntimeTypeHandle right) => left._value != right._value;
    }

    public class Object
    {
        public Object() { }
        public virtual string ToString() => GetType().ToString();
        public virtual bool Equals(object? obj)
        {
            return this == obj;
        }
        public static bool Equals(object? objA, object? objB)
        {
            if (objA == objB)
            {
                return true;
            }
            if (objA == null || objB == null)
            {
                return false;
            }
            return objA.Equals(objB);
        }
        public static bool ReferenceEquals(object? objA, object? objB)
        {
            return objA == objB;
        }
        public virtual int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
        // Returns a Type object which represent this object instance.
        [Intrinsic]
        public Type GetType() => RuntimeType.FromHandle(System.Runtime.RuntimeImports.RhGetObjectTypeHandle(this));
    }

    public class ValueType
    {
        protected ValueType() { }
    }
    public abstract class Enum : ValueType
    {
        protected Enum() { }
        public bool HasFlag(Enum flag)
        {
            return false;
        }
    }
    // NativeAOT's template for SZ arrays: T[] takes its vtable and generic collection interfaces from Array<T> but keeps System.Array as its base
    internal class Array<T> : Array, IEnumerable<T>, ICollection<T>, IList<T>, IReadOnlyList<T>
    {
        private Array() { }

        public new IEnumerator<T> GetEnumerator() => new SZGenericArrayEnumerator<T>(Unsafe.As<T[]>(this));

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public int Count => Unsafe.As<T[]>(this).Length;

        public bool IsReadOnly => true;

        public T this[int index]
        {
            get => Unsafe.As<T[]>(this)[index];
            set => Unsafe.As<T[]>(this)[index] = value;
        }

        public int IndexOf(T item) => Array.IndexOf(Unsafe.As<T[]>(this), item);

        public bool Contains(T item) => IndexOf(item) >= 0;

        public void CopyTo(T[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);
            T[] source = Unsafe.As<T[]>(this);
            if ((uint)arrayIndex > (uint)array.Length || array.Length - arrayIndex < source.Length)
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            for (int i = 0; i < source.Length; i++)
                array[arrayIndex + i] = source[i];
        }

        public void Add(T item) => ThrowHelper.ThrowNotSupportedException();

        public void Clear() => ThrowHelper.ThrowNotSupportedException();

        public void Insert(int index, T item) => ThrowHelper.ThrowNotSupportedException();

        public bool Remove(T item)
        {
            ThrowHelper.ThrowNotSupportedException();
            return false;
        }

        public void RemoveAt(int index) => ThrowHelper.ThrowNotSupportedException();
    }
    internal sealed class SZGenericArrayEnumerator<T> : IEnumerator<T>
    {
        private readonly T[] _array;
        private int _index = -1;

        internal SZGenericArrayEnumerator(T[] array) => _array = array;

        public bool MoveNext()
        {
            if (_index < _array.Length - 1)
            {
                _index++;
                return true;
            }
            _index = _array.Length;
            return false;
        }

        public T Current
        {
            get
            {
                if ((uint)_index >= (uint)_array.Length)
                    ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                return _array[_index];
            }
        }

        object? System.Collections.IEnumerator.Current => Current;

        public void Reset() => _index = -1;

        public void Dispose() { }
    }
    public class Array
    {
        private static class EmptyArray<T>
        {
            internal static readonly T[] Value = new T[0];
        }
        public static int MaxLength => 0X7FFFFFC7;
        public int Length
        {
            [MethodImpl(MethodImplOptions.InternalCall)]
            get { return 0; }
        }
        // SZ arrays enumerate through Array<T>; other arrays have no enumerator yet and fail the cast
        public System.Collections.IEnumerator GetEnumerator() => ((System.Collections.IEnumerable)this).GetEnumerator();

        public static T[] Empty<T>()
        {
            return EmptyArray<T>.Value;
        }

        public static int IndexOf<T>(T[] array, T value)
        {
            if ((object)array == null) throw new ArgumentNullException("array");
            return IndexOf<T>(array, value, 0, array.Length);
        }

        public static int IndexOf<T>(T[] array, T value, int startIndex)
        {
            if ((object)array == null) throw new ArgumentNullException("array");

            int len = array.Length;
            if ((uint)startIndex > (uint)len) throw new ArgumentOutOfRangeException("startIndex");

            return IndexOf<T>(array, value, startIndex, len - startIndex);
        }

        public static int IndexOf<T>(T[] array, T value, int startIndex, int count)
        {
            if ((object)array == null) throw new ArgumentNullException("array");

            int len = array.Length;
            if ((uint)startIndex > (uint)len) throw new ArgumentOutOfRangeException("startIndex");
            if (count < 0 || startIndex > len - count) throw new ArgumentOutOfRangeException("count");

            int end = startIndex + count;

            if ((object)value == null)
            {
                for (int i = startIndex; i < end; i++)
                {
                    if ((object)array[i] == null)
                        return i;
                }

                return -1;
            }

            object boxedValue = value;
            for (int i = startIndex; i < end; i++)
            {
                if (boxedValue.Equals(array[i]))
                    return i;
            }

            return -1;
        }

        public static void Clear(Array array)
        {
            if ((object)array == null) throw new ArgumentNullException("array");
            Clear(array, 0, array.Length);
        }

        public static void Clear(Array array, int index, int length)
        {
            if ((object)array == null) throw new ArgumentNullException("array");
            if (index < 0) throw new ArgumentOutOfRangeException("index");
            if (length < 0) throw new ArgumentOutOfRangeException("length");

            int alen = array.Length;
            if (alen - index < length) throw new IndexOutOfRangeException();
            if (length == 0) return;

            ClearInternal(array, index, length);
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static void ClearInternal(Array array, int index, int length)
        {
            // handled in runtime
        }

        public static void Fill<T>(T[] array, T value)
        {
            if ((object)array == null) throw new ArgumentNullException("array");
            Fill<T>(array, value, 0, array.Length);
        }
        public static void Fill<T>(T[] array, T value, int startIndex, int count)
        {
            if ((object)array == null) throw new ArgumentNullException("array");

            int len = array.Length;
            if ((uint)startIndex > (uint)len) throw new ArgumentOutOfRangeException("startIndex");
            if (count < 0 || startIndex > len - count) throw new ArgumentOutOfRangeException("count");
            if (count == 0) return;

            ref T r0 = ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference<T>(array);
            ref T dst = ref System.Runtime.CompilerServices.Unsafe.Add<T>(ref r0, startIndex);

            for (int i = 0; i < count; i++)
                System.Runtime.CompilerServices.Unsafe.Add<T>(ref dst, i) = value;
        }
        public static void Resize<T>([NotNull] ref T[] array, int newSize)
        {
            if (newSize < 0)
                throw new ArgumentOutOfRangeException();

            T[] larray = array; // local copy
            if (larray == null)
            {
                array = new T[newSize];
                return;
            }

            if (larray.Length != newSize)
            {
                T[] newArray = new T[newSize];
                Buffer.Memmove<T>(
                    ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference<T>(newArray),
                    ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference<T>(larray),
                    (uint)Math.Min(newSize, larray.Length));
                array = newArray;
            }
        }

        public static void Copy(Array sourceArray, Array destinationArray, long length)
        {
            int ilength = (int)length;
            if (length != ilength)
                throw new ArgumentOutOfRangeException("length");

            Copy(sourceArray, destinationArray, ilength);
        }
        public static void Copy(Array sourceArray, long sourceIndex, Array destinationArray, long destinationIndex, long length)
        {
            int isourceIndex = (int)sourceIndex;
            int idestinationIndex = (int)destinationIndex;
            int ilength = (int)length;

            if (sourceIndex != isourceIndex)
                throw new ArgumentOutOfRangeException("sourceIndex");
            if (destinationIndex != idestinationIndex)
                throw new ArgumentOutOfRangeException("destinationIndex");
            if (length != ilength)
                throw new ArgumentOutOfRangeException("length");

            Copy(sourceArray, isourceIndex, destinationArray, idestinationIndex, ilength);
        }
        public static unsafe void Copy(Array sourceArray, Array destinationArray, int length)
        {
            if ((object)sourceArray == null) throw new ArgumentNullException("sourceArray");
            if ((object)destinationArray == null) throw new ArgumentNullException("destinationArray");
            if (length < 0) throw new ArgumentOutOfRangeException("length");

            Copy(sourceArray, 0, destinationArray, 0, length);
        }
        public static unsafe void Copy(Array sourceArray, int sourceIndex, Array destinationArray, int destinationIndex, int length)
        {
            if ((object)sourceArray == null) throw new ArgumentNullException("sourceArray");
            if ((object)destinationArray == null) throw new ArgumentNullException("destinationArray");

            if (sourceIndex < 0) throw new ArgumentOutOfRangeException("sourceIndex");
            if (destinationIndex < 0) throw new ArgumentOutOfRangeException("destinationIndex");
            if (length < 0) throw new ArgumentOutOfRangeException("length");

            int srcLen = sourceArray.Length;
            int dstLen = destinationArray.Length;

            if ((uint)sourceIndex > (uint)srcLen) throw new ArgumentOutOfRangeException("sourceIndex");
            if ((uint)destinationIndex > (uint)dstLen) throw new ArgumentOutOfRangeException("destinationIndex");

            if (srcLen - sourceIndex < length) throw new ArgumentException();
            if (dstLen - destinationIndex < length) throw new ArgumentException();

            if (length == 0) return;

            if (!CopyInternal(sourceArray, sourceIndex, destinationArray, destinationIndex, length))
                throw new ArrayTypeMismatchException();
        }
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static bool CopyInternal(Array sourceArray, int sourceIndex, Array destinationArray, int destinationIndex, int length)
            => false;
    }

    public abstract unsafe class Delegate
    {
        internal object? _target; // do not rename
        internal nint _methodPtr; // do not rename
        internal nint _methodModule;

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static Delegate? Combine(Delegate? a, Delegate? b) => null;

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static Delegate? Remove(Delegate? source, Delegate? value) => null;
    }
    public abstract class MulticastDelegate : Delegate
    {
        private object? _invocationList;
        private nint _invocationCount;
    }

    public enum StringComparison
    {
        CurrentCulture = 0,
        CurrentCultureIgnoreCase = 1,
        InvariantCulture = 2,
        InvariantCultureIgnoreCase = 3,
        Ordinal = 4,
        OrdinalIgnoreCase = 5,
    }
    [Flags]
    public enum StringSplitOptions
    {
        None = 0,
        RemoveEmptyEntries = 1,
        TrimEntries = 2
    }
    public sealed class String
    {
        private readonly int _stringLength;  // do not rename
        private char _firstChar;  // do not rename

        /// <summary>Maximum length allowed for a string.</summary>
        /// <remarks>Keep in sync with AllocateString in gchelpers.cpp.</remarks>
        internal const int MaxLength = 0x3FFFFFDF;
        public const string Empty = "";
        internal const int StackallocCharBufferSizeLimit = 256;

        public static string Format([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0)
        {
            return FormatHelper(null, format, new ReadOnlySpan<object?>(in arg0));
        }

        public static string Format([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1)
        {
            TwoObjects two = new TwoObjects(arg0, arg1);
            return FormatHelper(null, format, two);
        }

        public static string Format([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1, object? arg2)
        {
            ThreeObjects three = new ThreeObjects(arg0, arg1, arg2);
            return FormatHelper(null, format, three);
        }

        public static string Format([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params object?[] args)
        {
            if (args is null)
            {
                // To preserve the original exception behavior, throw an exception about format if both
                // args and format are null. The actual null check for format is in FormatHelper.
                ArgumentNullException.Throw(format is null ? nameof(format) : nameof(args));
            }

            return FormatHelper(null, format, (ReadOnlySpan<object?>)args);
        }

        /// <summary>
        /// Replaces the format item in a specified string with the string representation of a corresponding object in a specified span.
        /// </summary>
        /// <param name="format">A <see href="https://learn.microsoft.com/dotnet/standard/base-types/composite-formatting">composite format string</see>.</param>
        /// <param name="args">An object span that contains zero or more objects to format.</param>
        /// <returns>A copy of <paramref name="format"/> in which the format items have been replaced by the string representation of the corresponding objects in <paramref name="args"/>.</returns>
        public static string Format([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params ReadOnlySpan<object?> args)
        {
            return FormatHelper(null, format, args);
        }

        public static string Format(IFormatProvider? provider, [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0)
        {
            return FormatHelper(provider, format, new ReadOnlySpan<object?>(in arg0));
        }

        public static string Format(IFormatProvider? provider, [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1)
        {
            TwoObjects two = new TwoObjects(arg0, arg1);
            return FormatHelper(provider, format, two);
        }

        public static string Format(IFormatProvider? provider, [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1, object? arg2)
        {
            ThreeObjects three = new ThreeObjects(arg0, arg1, arg2);
            return FormatHelper(provider, format, three);
        }

        public static string Format(IFormatProvider? provider, [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params object?[] args)
        {
            if (args is null)
            {
                // To preserve the original exception behavior, throw an exception about format if both
                // args and format are null. The actual null check for format is in FormatHelper.
                ArgumentNullException.Throw(format is null ? nameof(format) : nameof(args));
            }

            return FormatHelper(provider, format, (ReadOnlySpan<object?>)args);
        }

        /// <summary>
        /// Replaces the format items in a string with the string representations of corresponding objects in a specified span.
        /// A parameter supplies culture-specific formatting information.
        /// </summary>
        /// <param name="provider">An object that supplies culture-specific formatting information.</param>
        /// <param name="format">A <see href="https://learn.microsoft.com/dotnet/standard/base-types/composite-formatting">composite format string</see>.</param>
        /// <param name="args">An object span that contains zero or more objects to format.</param>
        /// <returns>A copy of <paramref name="format"/> in which the format items have been replaced by the string representation of the corresponding objects in <paramref name="args"/>.</returns>
        public static string Format(IFormatProvider? provider, [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params ReadOnlySpan<object?> args)
        {
            return FormatHelper(provider, format, args);
        }

        private static string FormatHelper(IFormatProvider? provider, string format, ReadOnlySpan<object?> args)
        {
            ArgumentNullException.ThrowIfNull(format);

            var sb = new ValueStringBuilder(stackalloc char[StackallocCharBufferSizeLimit]);
            sb.EnsureCapacity(format.Length + args.Length * 8);
            sb.AppendFormatHelper(provider, format, args);
            return sb.ToString();
        }

        public int Length
        {
            [Intrinsic]
            get { return _stringLength; }
        }
        [NonVersionable]
        public ref readonly char GetPinnableReference() { return ref _firstChar; }
        internal ref char GetRawStringData() { return ref _firstChar; }
        internal ref byte GetRawStringDataAsUInt8() { return ref System.Runtime.CompilerServices.Unsafe.As<char, byte>(ref _firstChar); }
        internal ref ushort GetRawStringDataAsUInt16() { return ref System.Runtime.CompilerServices.Unsafe.As<char, ushort>(ref _firstChar); }
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static string FastAllocateString(int length) { return null; }
        internal String() { }
        public String(char ch, int length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException("length");
            ref char dst = ref GetRawStringData();
            for (int i = 0; i < length; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = ch;
        }
        public String(char[] value)
        {
            if (value == null) throw new ArgumentNullException("length");
            int n = value.Length;
            ref char dst = ref GetRawStringData();
            for (int i = 0; i < n; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = value[i];
        }
        public String(char[] value, int startIndex, int length)
        {
            if (value == null) throw new ArgumentNullException("length");
            if ((uint)startIndex > (uint)value.Length) throw new ArgumentOutOfRangeException("startIndex");
            if (length < 0) throw new ArgumentOutOfRangeException("length");
            if (startIndex + length > value.Length) throw new ArgumentOutOfRangeException("length");

            ref char dst = ref GetRawStringData();
            for (int i = 0; i < length; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = value[startIndex + i];
        }
        public String(ReadOnlySpan<char> value)
        {
            ref char dst = ref GetRawStringData();
            for (int i = 0; i < value.Length; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = value[i];
        }
        public unsafe String(char* value)
        {
            if (value == null) throw new ArgumentNullException("value");

            ref char dst = ref GetRawStringData();
            for (int i = 0; i < Length; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = value[i];
        }

        public char this[int index]
        {
            [Intrinsic]
            get
            {
                if ((uint)index >= (uint)_stringLength)
                    throw new IndexOutOfRangeException();
                return System.Runtime.CompilerServices.Unsafe.Add<char>(ref _firstChar, index);
            }
        }

        /// <summary>Copies the contents of this string into the destination span.</summary>
        /// <param name="destination">The span into which to copy this string's contents.</param>
        /// <exception cref="ArgumentException">The destination span is shorter than the source string.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(Span<char> destination)
        {
            if ((uint)Length <= (uint)destination.Length)
            {
                Buffer.Memmove(ref destination._reference, ref _firstChar, (uint)Length);
            }
            else
            {
                throw new ArgumentException();
            }
        }

        /// <summary>Copies the contents of this string into the destination span.</summary>
        /// <param name="destination">The span into which to copy this string's contents.</param>
        /// <returns>true if the data was copied; false if the destination was too short to fit the contents of the string.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryCopyTo(Span<char> destination)
        {
            bool retVal = false;
            if ((uint)Length <= (uint)destination.Length)
            {
                Buffer.Memmove(ref destination._reference, ref _firstChar, (uint)Length);
                retVal = true;
            }
            return retVal;
        }

        public override string ToString() => this;
        // Determines whether two strings match.
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (ReferenceEquals(this, obj))
                return true;

            if (obj is not string str)
                return false;

            if (this.Length != str.Length)
                return false;

            return EqualsHelper(this, str);
        }
        public bool Equals([NotNullWhen(true)] string? value)
        {
            if (ReferenceEquals(this, value))
                return true;
            if (value == null)
                return false;
            int n = Length;
            if (n != value.Length) return false;
            ref char a = ref GetRawStringData();
            ref char b = ref value.GetRawStringData();
            for (int i = 0; i < n; i++)
            {
                if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref a, i) !=
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref b, i))
                    return false;
            }
            return true;
        }
        public bool Equals([NotNullWhen(true)] string? value, StringComparison comparisonType)
        {
            if (ReferenceEquals(this, value))
            {
                CheckStringComparison(comparisonType);
                return true;
            }

            if (value is null)
            {
                CheckStringComparison(comparisonType);
                return false;
            }

            switch (comparisonType)
            {
                case StringComparison.CurrentCulture:
                case StringComparison.CurrentCultureIgnoreCase:
                    return System.Globalization.CultureInfo.CurrentCulture.CompareInfo.Compare(this, value, GetCaseCompareOfComparisonCulture(comparisonType)) == 0;

                case StringComparison.InvariantCulture:
                case StringComparison.InvariantCultureIgnoreCase:
                    return System.Globalization.CompareInfo.Invariant.Compare(this, value, GetCaseCompareOfComparisonCulture(comparisonType)) == 0;

                case StringComparison.Ordinal:
                    if (this.Length != value.Length)
                        return false;
                    return EqualsHelper(this, value);

                case StringComparison.OrdinalIgnoreCase:
                    if (this.Length != value.Length)
                        return false;

                    return EqualsOrdinalIgnoreCaseNoLengthCheck(this, value);

                default:
                    throw new ArgumentException();
            }
        }
        public static bool Equals(string? a, string? b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a is null || b is null || a.Length != b.Length)
            {
                return false;
            }

            return EqualsHelper(a, b);
        }
        public static bool Equals(string? a, string? b, StringComparison comparisonType)
        {
            if (ReferenceEquals(a, b))
            {
                CheckStringComparison(comparisonType);
                return true;
            }

            if (a is null || b is null)
            {
                CheckStringComparison(comparisonType);
                return false;
            }

            switch (comparisonType)
            {
                case StringComparison.CurrentCulture:
                case StringComparison.CurrentCultureIgnoreCase:
                    return CultureInfo.CurrentCulture.CompareInfo.Compare(a, b, GetCaseCompareOfComparisonCulture(comparisonType)) == 0;

                case StringComparison.InvariantCulture:
                case StringComparison.InvariantCultureIgnoreCase:
                    return CompareInfo.Invariant.Compare(a, b, GetCaseCompareOfComparisonCulture(comparisonType)) == 0;

                case StringComparison.Ordinal:
                    if (a.Length != b.Length)
                        return false;
                    return EqualsHelper(a, b);

                case StringComparison.OrdinalIgnoreCase:
                    if (a.Length != b.Length)
                        return false;

                    return EqualsOrdinalIgnoreCaseNoLengthCheck(a, b);

                default:
                    throw new ArgumentException();
            }
        }
        private static bool EqualsOrdinalIgnoreCaseNoLengthCheck(string strA, string strB)
        {
            return System.Globalization.Ordinal.EqualsIgnoreCase(ref strA.GetRawStringData(), ref strB.GetRawStringData(), strB.Length);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool EqualsHelper(string strA, string strB)
        {
            return SpanHelpers.SequenceEqual(
                ref strA.GetRawStringDataAsUInt8(),
                ref strB.GetRawStringDataAsUInt8(),
                ((uint)strA.Length) * sizeof(char));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CompareOrdinalHelper(string strA, int indexA, int countA, string strB, int indexB, int countB)
        {
            return SpanHelpers.SequenceCompareTo(
                ref Unsafe.Add(ref strA.GetRawStringData(), (nint)(uint)indexA /* force zero-extension */), countA,
                ref Unsafe.Add(ref strB.GetRawStringData(), (nint)(uint)indexB /* force zero-extension */), countB);
        }
        internal static void CheckStringComparison(StringComparison comparisonType)
        {
            // Single comparison to check if comparisonType is within [CurrentCulture .. OrdinalIgnoreCase]
            if ((uint)comparisonType > (uint)StringComparison.OrdinalIgnoreCase)
            {
                throw new ArgumentException();
            }
        }
        internal static System.Globalization.CompareOptions GetCaseCompareOfComparisonCulture(StringComparison comparisonType)
        {
            // Culture enums can be & with CompareOptions.IgnoreCase 0x01 to extract if IgnoreCase or CompareOptions.None 0x00
            //
            // CompareOptions.None                          0x00
            // CompareOptions.IgnoreCase                    0x01
            //
            // StringComparison.CurrentCulture:             0x00
            // StringComparison.InvariantCulture:           0x02
            // StringComparison.Ordinal                     0x04
            //
            // StringComparison.CurrentCultureIgnoreCase:   0x01
            // StringComparison.InvariantCultureIgnoreCase: 0x03
            // StringComparison.OrdinalIgnoreCase           0x05

            return (System.Globalization.CompareOptions)((int)comparisonType & (int)System.Globalization.CompareOptions.IgnoreCase);
        }
        private static System.Globalization.CompareOptions GetCompareOptionsFromOrdinalStringComparison(StringComparison comparisonType)
        {
            // StringComparison.Ordinal (0x04) --> CompareOptions.Ordinal (0x4000_0000)
            // StringComparison.OrdinalIgnoreCase (0x05) -> CompareOptions.OrdinalIgnoreCase (0x1000_0000)

            int ct = (int)comparisonType;
            return (System.Globalization.CompareOptions)((ct & -ct) << 28); // neg and shl
        }
        public static bool operator ==(string left, string right)
        {
            if ((object)left == (object)right) return true;
            if ((object)left == null || (object)right == null) return false;
            return left.Equals(right);
        }
        public static bool operator !=(string left, string right) => !(left == right);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            ulong seed = Marvin.DefaultSeed;

            // Multiplication below will not overflow since going from positive Int32 to UInt32.
            return Marvin.ComputeHash32(ref Unsafe.As<char, byte>(ref _firstChar), (uint)_stringLength * 2 /* in bytes, not chars */, (uint)seed, (uint)(seed >> 32));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetHashCodeOrdinalIgnoreCase()
        {
            ulong seed = Marvin.DefaultSeed;
            return Marvin.ComputeHash32OrdinalIgnoreCase(ref _firstChar, _stringLength /* in chars, not bytes */, (uint)seed, (uint)(seed >> 32));
        }
        // A span-based equivalent of String.GetHashCode(). Computes an ordinal hash code.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetHashCode(ReadOnlySpan<char> value)
        {
            ulong seed = Marvin.DefaultSeed;

            // Multiplication below will not overflow since going from positive Int32 to UInt32.
            return Marvin.ComputeHash32(ref Unsafe.As<char, byte>(
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(value)), (uint)value.Length * 2 /* in bytes, not chars */, (uint)seed, (uint)(seed >> 32));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetHashCodeOrdinalIgnoreCase(ReadOnlySpan<char> value)
        {
            ulong seed = Marvin.DefaultSeed;
            return Marvin.ComputeHash32OrdinalIgnoreCase(
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(value), value.Length /* in chars, not bytes */, (uint)seed, (uint)(seed >> 32));
        }
        // Important GetNonRandomizedHashCode{OrdinalIgnoreCase} notes:
        //
        // Use if and only if 'Denial of Service' attacks are not a concern (i.e. never used for free-form user input),
        // or are otherwise mitigated.
        //
        // The string-based implementation relies on System.String being null terminated. All reads are performed
        // two characters at a time, so for odd-length strings, the final read will include the null terminator.
        // This implementation must not be used as-is with spans, or otherwise arbitrary char refs/pointers, as
        // they're not guaranteed to be null-terminated.
        //
        // For spans, we must produce the exact same value as is used for strings: consumers like Dictionary<>
        // rely on str.GetNonRandomizedHashCode() == GetNonRandomizedHashCode(str.AsSpan()). As such, we must
        // restructure the comparison so that for odd-length spans, we simulate the null terminator and include
        // it in the hash computation exactly as does str.GetNonRandomizedHashCode().
        internal unsafe int GetNonRandomizedHashCode()
        {
            fixed (char* src = &_firstChar)
            {
                uint hash1 = (5381 << 16) + 5381;
                uint hash2 = hash1;

                uint* ptr = (uint*)src;
                int length = Length;

                while (length > 2)
                {
                    length -= 4;
                    hash1 = (System.Numerics.BitOperations.RotateLeft(hash1, 5) + hash1) ^ ptr[0];
                    hash2 = (System.Numerics.BitOperations.RotateLeft(hash2, 5) + hash2) ^ ptr[1];
                    ptr += 2;
                }

                if (length > 0)
                {
                    hash2 = (System.Numerics.BitOperations.RotateLeft(hash2, 5) + hash2) ^ ptr[0];
                }

                return (int)(hash1 + (hash2 * 1566083941));
            }
        }

        internal static unsafe int GetNonRandomizedHashCode(ReadOnlySpan<char> span)
        {
            uint hash1 = (5381 << 16) + 5381;
            uint hash2 = hash1;

            int length = span.Length;
            fixed (char* src = &System.Runtime.InteropServices.MemoryMarshal.GetReference(span))
            {
                uint* ptr = (uint*)src;

            LengthSwitch:
                switch (length)
                {
                    default:
                        do
                        {
                            length -= 4;
                            hash1 = System.Numerics.BitOperations.RotateLeft(hash1, 5) + hash1 ^ Unsafe.ReadUnaligned<uint>(ptr);
                            hash2 = System.Numerics.BitOperations.RotateLeft(hash2, 5) + hash2 ^ Unsafe.ReadUnaligned<uint>(ptr + 1);
                            ptr += 2;
                        }
                        while (length >= 4);
                        goto LengthSwitch;

                    case 3:
                        hash1 = BitOperations.RotateLeft(hash1, 5) + hash1 ^ Unsafe.ReadUnaligned<uint>(ptr);
                        uint p1 = *(char*)(ptr + 1);
                        if (!BitConverter.IsLittleEndian)
                        {
                            p1 <<= 16;
                        }

                        hash2 = BitOperations.RotateLeft(hash2, 5) + hash2 ^ p1;
                        break;

                    case 2:
                        hash2 = BitOperations.RotateLeft(hash2, 5) + hash2 ^ Unsafe.ReadUnaligned<uint>(ptr);
                        break;

                    case 1:
                        uint p0 = *(char*)ptr;
                        if (!BitConverter.IsLittleEndian)
                        {
                            p0 <<= 16;
                        }

                        hash2 = BitOperations.RotateLeft(hash2, 5) + hash2 ^ p0;
                        break;

                    case 0:
                        break;
                }
            }

            return (int)(hash1 + (hash2 * 1_566_083_941));
        }
        public static bool IsNullOrEmpty(string value) => (object)value == null || value.Length == 0;
        public static bool IsNullOrWhiteSpace(string value)
        {
            if ((object)value == null) return true;
            int n = value.Length;
            if (n == 0) return true;

            ref char p = ref value.GetRawStringData();
            for (int i = 0; i < n; i++)
            {
                if (!Char.IsWhiteSpace(System.Runtime.CompilerServices.Unsafe.Add<char>(ref p, i)))
                    return false;
            }
            return true;
        }
        public ReadOnlySpan<char> AsSpan()
        {
            ref char r = ref GetRawStringData();
            return new ReadOnlySpan<char>(ref r, Length);
        }
        public ReadOnlySpan<char> AsSpan(int start)
        {
            int len = Length;
            if ((uint)start > (uint)len)
                throw new ArgumentOutOfRangeException("start");

            ref char r = ref GetRawStringData();
            return new ReadOnlySpan<char>(
                ref System.Runtime.CompilerServices.Unsafe.Add<char>(ref r, start),
                len - start);
        }
        public ReadOnlySpan<char> AsSpan(int start, int length)
        {
            int len = Length;
            if ((uint)start > (uint)len)
                throw new ArgumentOutOfRangeException("start");
            if ((uint)length > (uint)(len - start))
                throw new ArgumentOutOfRangeException("length");

            ref char r = ref GetRawStringData();
            return new ReadOnlySpan<char>(
                ref System.Runtime.CompilerServices.Unsafe.Add<char>(ref r, start),
                length);
        }

        /// <summary>Creates a new string by using the specified provider to control the formatting of the specified interpolated string.</summary>
        /// <param name="provider">An object that supplies culture-specific formatting information.</param>
        /// <param name="handler">The interpolated string.</param>
        /// <returns>The string that results for formatting the interpolated string using the specified format provider.</returns>
        public static string Create(
            IFormatProvider? provider,
            [InterpolatedStringHandlerArgument(nameof(provider))] ref DefaultInterpolatedStringHandler handler) =>
            handler.ToStringAndClear();

        /// <summary>Creates a new string by using the specified provider to control the formatting of the specified interpolated string.</summary>
        /// <param name="provider">An object that supplies culture-specific formatting information.</param>
        /// <param name="initialBuffer">The initial buffer that may be used as temporary space as part of the formatting operation. The contents of this buffer may be overwritten.</param>
        /// <param name="handler">The interpolated string.</param>
        /// <returns>The string that results for formatting the interpolated string using the specified format provider.</returns>
        public static string Create(
            IFormatProvider? provider,
            Span<char> initialBuffer,
            [InterpolatedStringHandlerArgument(nameof(provider), nameof(initialBuffer))] ref DefaultInterpolatedStringHandler handler) =>
            handler.ToStringAndClear();

        public static implicit operator ReadOnlySpan<char>(String? value)
        {
            ref char r = ref value.GetRawStringData();
            return new ReadOnlySpan<char>(ref r, value.Length);
        }
        public string Substring(int startIndex) => Substring(startIndex, Length - startIndex);
        public string Substring(int startIndex, int length)
        {
            if ((uint)startIndex > (uint)Length) throw new ArgumentOutOfRangeException("startIndex");
            if (length < 0) throw new ArgumentOutOfRangeException("length");
            if (startIndex + length > Length) throw new ArgumentOutOfRangeException("length");

            if (length == 0) return Empty;
            if (startIndex == 0 && length == Length) return this;

            string dstStr = FastAllocateString(length);
            ref char dst = ref dstStr.GetRawStringData();
            ref char src = ref GetRawStringData();

            for (int i = 0; i < length; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) =
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, startIndex + i);

            return dstStr;
        }
        public int IndexOf(char value) => IndexOf(value, 0, Length);

        public int IndexOf(char value, int startIndex)
        {
            if ((uint)startIndex > (uint)Length) throw new ArgumentOutOfRangeException("startIndex");
            return IndexOf(value, startIndex, Length - startIndex);
        }

        public int IndexOf(char value, int startIndex, int count)
        {
            int len = Length;
            if ((uint)startIndex > (uint)len) throw new ArgumentOutOfRangeException("startIndex");
            if (count < 0 || startIndex > len - count) throw new ArgumentOutOfRangeException("count");

            int end = startIndex + count;
            ref char src = ref GetRawStringData();

            for (int i = startIndex; i < end; i++)
            {
                if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i) == value)
                    return i;
            }

            return -1;
        }
        public int IndexOf(string value) => IndexOf(value, 0, Length);

        public int IndexOf(string value, int startIndex)
        {
            if ((object)value == null) throw new ArgumentNullException("value");
            if ((uint)startIndex > (uint)Length) throw new ArgumentOutOfRangeException("startIndex");

            return IndexOf(value, startIndex, Length - startIndex);
        }

        public int IndexOf(string value, int startIndex, int count)
        {
            if ((object)value == null) throw new ArgumentNullException("value");

            int n = Length;
            if ((uint)startIndex > (uint)n) throw new ArgumentOutOfRangeException("startIndex");
            if (count < 0 || startIndex > n - count) throw new ArgumentOutOfRangeException("count");

            int m = value.Length;
            if (m == 0) return startIndex;
            if (m == 1) return IndexOf(value[0], startIndex, count);
            if (m > count) return -1;

            ref char a = ref GetRawStringData();
            ref char b = ref value.GetRawStringData();

            int last = startIndex + count - m;
            char b0 = System.Runtime.CompilerServices.Unsafe.Add<char>(ref b, 0);

            for (int i = startIndex; i <= last; i++)
            {
                if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref a, i) != b0)
                    continue;

                int j = 1;
                for (; j < m; j++)
                {
                    if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref a, i + j) !=
                        System.Runtime.CompilerServices.Unsafe.Add<char>(ref b, j))
                        break;
                }

                if (j == m)
                    return i;
            }

            return -1;
        }

        public int LastIndexOf(char value)
        {
            int n = Length;
            if (n == 0) return -1;

            ref char src = ref GetRawStringData();
            for (int i = n - 1; i >= 0; i--)
                if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i) == value)
                    return i;

            return -1;
        }

        public int LastIndexOf(char value, int startIndex) => LastIndexOf(value, startIndex, startIndex + 1);

        public int LastIndexOf(char value, int startIndex, int count)
        {
            int len = Length;
            if (len == 0) return -1;

            if ((uint)startIndex >= (uint)len) throw new ArgumentOutOfRangeException("startIndex");
            if (count < 0) throw new ArgumentOutOfRangeException("count");
            if ((uint)count > (uint)startIndex + 1u) throw new ArgumentOutOfRangeException("count");

            int startSearchAt = startIndex + 1 - count;

            ref char src = ref GetRawStringData();
            for (int i = startIndex; i >= startSearchAt; i--)
                if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i) == value)
                    return i;

            return -1;
        }

        public int LastIndexOf(string value) => LastIndexOf(value, Length - 1, Length);

        public int LastIndexOf(string value, int startIndex) => LastIndexOf(value, startIndex, startIndex + 1);

        public int LastIndexOf(string value, int startIndex, int count)
        {
            if ((object)value == null) throw new ArgumentNullException("value");

            int thisLen = Length;
            int valueLen = value.Length;

            if (valueLen == 0)
            {
                if (thisLen == 0)
                {
                    if (startIndex < -1 || startIndex > 0) throw new ArgumentOutOfRangeException("startIndex");
                    if (count < 0) throw new ArgumentOutOfRangeException("count");
                    if (count > 1) throw new ArgumentOutOfRangeException("count");
                    return 0;
                }

                if (count < 0) throw new ArgumentOutOfRangeException("count");
                if (startIndex < 0 || startIndex > thisLen) throw new ArgumentOutOfRangeException("startIndex");

                if (startIndex == thisLen) startIndex = thisLen - 1;
                if (count > startIndex + 1) throw new ArgumentOutOfRangeException("count");

                return startIndex + 1;
            }

            if (thisLen == 0) return -1;

            if (count < 0) throw new ArgumentOutOfRangeException("count");
            if (startIndex < 0 || startIndex > thisLen) throw new ArgumentOutOfRangeException("startIndex");
            if (startIndex == thisLen) startIndex = thisLen - 1;
            if (count > startIndex + 1) throw new ArgumentOutOfRangeException("count");

            int searchStart = startIndex + 1 - count;

            if (valueLen > count) return -1;

            ref char a = ref GetRawStringData();
            ref char b = ref value.GetRawStringData();

            int last = startIndex - valueLen + 1;
            for (int i = last; i >= searchStart; i--)
            {
                if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref a, i) !=
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref b, 0))
                    continue;

                int j = 1;
                for (; j < valueLen; j++)
                {
                    if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref a, i + j) !=
                        System.Runtime.CompilerServices.Unsafe.Add<char>(ref b, j))
                        break;
                }
                if (j == valueLen) return i;
            }

            return -1;
        }

        public bool StartsWith(string value)
        {
            if ((object)value == null) throw new ArgumentNullException("value");
            int n = value.Length;
            if (n > Length) return false;

            ref char a = ref GetRawStringData();
            ref char b = ref value.GetRawStringData();
            for (int i = 0; i < n; i++)
                if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref a, i) !=
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref b, i))
                    return false;
            return true;
        }
        public bool EndsWith(string value)
        {
            if ((object)value == null) throw new ArgumentNullException("value");
            int n = value.Length;
            int len = Length;
            if (n > len) return false;

            ref char a = ref GetRawStringData();
            ref char b = ref value.GetRawStringData();
            int start = len - n;
            for (int i = 0; i < n; i++)
                if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref a, start + i) !=
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref b, i))
                    return false;
            return true;
        }
        public string Replace(char oldChar, char newChar)
        {
            int n = Length;
            if (n == 0) return this;

            // Find first occurrence
            ref char src = ref GetRawStringData();
            int first = -1;
            for (int i = 0; i < n; i++)
            {
                if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i) == oldChar)
                {
                    first = i;
                    break;
                }
            }
            if (first < 0) return this;

            string dstStr = FastAllocateString(n);
            ref char dst = ref dstStr.GetRawStringData();

            for (int i = 0; i < n; i++)
            {
                char c = System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i);
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = (c == oldChar) ? newChar : c;
            }

            return dstStr;
        }
        public string Replace(string oldValue, string newValue)
        {
            if ((object)oldValue == null) throw new ArgumentNullException("oldValue");
            if ((object)newValue == null) newValue = Empty;

            int oldLen = oldValue.Length;
            if (oldLen == 0) throw new ArgumentException("oldValue cannot be empty.", "oldValue");

            int len = Length;
            if (len == 0) return this;

            int first = IndexOf(oldValue, 0);
            if (first < 0) return this;

            int newLen = newValue.Length;

            // Count occurrences
            int count = 0;
            int idx = first;
            while (idx >= 0)
            {
                count++;
                idx = IndexOf(oldValue, idx + oldLen);
            }

            long resultLen = (long)len + (long)count * ((long)newLen - (long)oldLen);
            if (resultLen <= 0) return Empty;
            if (resultLen > int.MaxValue) throw new OutOfMemoryException();

            string dstStr = FastAllocateString((int)resultLen);
            ref char dst = ref dstStr.GetRawStringData();

            ref char src = ref GetRawStringData();
            ref char ov = ref oldValue.GetRawStringData();

            int srcPos = 0;
            int dstPos = 0;
            int match = first;

            while (match >= 0)
            {
                // copy segment before match
                int segLen = match - srcPos;
                for (int i = 0; i < segLen; i++)
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, dstPos + i) =
                        System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, srcPos + i);

                dstPos += segLen;

                // copy replacement
                if (newLen != 0)
                {
                    CopyTo(newValue, ref dst, dstPos);
                    dstPos += newLen;
                }

                srcPos = match + oldLen;
                match = IndexOf(oldValue, srcPos);
            }

            // copy tail
            int tail = len - srcPos;
            for (int i = 0; i < tail; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, dstPos + i) =
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, srcPos + i);

            return dstStr;
        }
        public char[] ToCharArray()
        {
            int n = Length;
            var a = new char[n];
            ref char src = ref GetRawStringData();
            for (int i = 0; i < n; i++)
                a[i] = System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i);
            return a;
        }

        private static string ObjToString(object o)
        {
            if (o == null) return Empty;
            var s = o as string;
            if (s != null) return s;
            return o.ToString();
        }
        private static void CopyTo(string srcStr, ref char dst, int dstIndex)
        {
            int len = srcStr.Length;
            if (len == 0) return;

            ref char src = ref srcStr.GetRawStringData();
            for (int i = 0; i < len; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, dstIndex + i) =
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i);
        }

        public static string Concat(object a, object b)
        {
            string s0 = ObjToString(a);
            string s1 = ObjToString(b);
            return Concat(s0, s1);
        }
        public static string Concat(object a, object b, object c)
        {
            string s0 = ObjToString(a);
            string s1 = ObjToString(b);
            string s2 = ObjToString(c);
            return Concat(s0, s1, s2);
        }
        public static string Concat(object a, object b, object c, object d)
        {
            string s0 = ObjToString(a);
            string s1 = ObjToString(b);
            string s2 = ObjToString(c);
            string s3 = ObjToString(d);
            return Concat(s0, s1, s2, s3);
        }
        public static string Concat(object[] values)
        {
            if (values == null) throw new ArgumentNullException("values");

            int n = values.Length;
            if (n == 0) return Empty;

            var parts = new string[n];
            int total = 0;

            for (int i = 0; i < n; i++)
            {
                string s = ObjToString(values[i]);
                parts[i] = s;
                total += s.Length;
            }

            if (total == 0) return Empty;

            string dstStr = FastAllocateString(total);
            ref char dst = ref dstStr.GetRawStringData();

            int pos = 0;
            for (int i = 0; i < n; i++)
            {
                string s = parts[i];
                int len = s.Length;
                if (len != 0)
                {
                    ref char src = ref s.GetRawStringData();
                    for (int k = 0; k < len; k++)
                        System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, pos + k) =
                            System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, k);
                    pos += len;
                }
            }

            return dstStr;
        }
        public static string Concat(string a, string b)
        {
            if ((object)a == null) a = Empty;
            if ((object)b == null) b = Empty;

            int la = a.Length;
            int lb = b.Length;
            int total = la + lb;
            if (total == 0) return Empty;

            string dstStr = FastAllocateString(total);
            ref char dst = ref dstStr.GetRawStringData();

            int pos = 0;
            if (la != 0)
            {
                ref char src = ref a.GetRawStringData();
                for (int i = 0; i < la; i++)
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) =
                        System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i);
                pos = la;
            }
            if (lb != 0)
            {
                ref char src = ref b.GetRawStringData();
                for (int i = 0; i < lb; i++)
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, pos + i) =
                        System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i);
            }

            return dstStr;
        }
        public static string Concat(string a, string b, string c)
            => Concat(Concat(a, b), c);
        public static string Concat(string a, string b, string c, string d)
            => Concat(Concat(a, b), Concat(c, d));

        public static string Join(char separator, string[] value)
            => Join(separator.ToString(), value);

        public static string Join(char separator, object[] values)
            => Join(separator.ToString(), values);

        public static string Join(string separator, string[] value)
        {
            if ((object)value == null) throw new ArgumentNullException("value");
            if ((object)separator == null) separator = Empty;

            int n = value.Length;
            if (n == 0) return Empty;
            if (n == 1)
            {
                string s0 = value[0];
                return (object)s0 == null ? Empty : s0;
            }

            int sepLen = separator.Length;
            long total = 0;

            for (int i = 0; i < n; i++)
            {
                string s = value[i];
                if ((object)s != null) total += s.Length;
            }
            total += (long)sepLen * (n - 1);

            if (total <= 0) return Empty;
            if (total > int.MaxValue) throw new OutOfMemoryException();

            string dstStr = FastAllocateString((int)total);
            ref char dst = ref dstStr.GetRawStringData();

            int pos = 0;
            for (int i = 0; i < n; i++)
            {
                if (i != 0 && sepLen != 0)
                {
                    CopyTo(separator, ref dst, pos);
                    pos += sepLen;
                }

                string s = value[i];
                if ((object)s != null && s.Length != 0)
                {
                    CopyTo(s, ref dst, pos);
                    pos += s.Length;
                }
            }

            return dstStr;
        }
        public static string Join(string separator, object[] values)
        {
            if ((object)values == null) throw new ArgumentNullException("values");
            if ((object)separator == null) separator = Empty;

            int n = values.Length;
            if (n == 0) return Empty;
            if (n == 1) return ObjToString(values[0]);

            int sepLen = separator.Length;
            var parts = new string[n];
            long total = 0;

            for (int i = 0; i < n; i++)
            {
                string s = ObjToString(values[i]);
                parts[i] = s;
                total += s.Length;
            }
            total += (long)sepLen * (n - 1);

            if (total <= 0) return Empty;
            if (total > int.MaxValue) throw new OutOfMemoryException();

            string dstStr = FastAllocateString((int)total);
            ref char dst = ref dstStr.GetRawStringData();

            int pos = 0;
            for (int i = 0; i < n; i++)
            {
                if (i != 0 && sepLen != 0)
                {
                    CopyTo(separator, ref dst, pos);
                    pos += sepLen;
                }

                string s = parts[i];
                if (s.Length != 0)
                {
                    CopyTo(s, ref dst, pos);
                    pos += s.Length;
                }
            }

            return dstStr;
        }


        public string[] Split(char separator, StringSplitOptions options = StringSplitOptions.None)
        {
            return SplitInternal(new ReadOnlySpan<char>(in separator), int.MaxValue, options);
        }

        public string[] Split(char separator, int count, StringSplitOptions options = StringSplitOptions.None)
        {
            return SplitInternal(new ReadOnlySpan<char>(in separator), count, options);
        }
        public string[] Split(char[] separator, int count, StringSplitOptions options = StringSplitOptions.None)
        {
            return SplitInternal(new ReadOnlySpan<char>(separator), count, options);
        }
        public string[] Split(char[] separator, StringSplitOptions options)
        {
            return SplitInternal(new ReadOnlySpan<char>(separator), int.MaxValue, options);
        }
        public string[] Split(char[] separator, int count)
        {
            return SplitInternal(separator, count, StringSplitOptions.None);
        }

        public string[] Split(string? separator, StringSplitOptions options = StringSplitOptions.None)
        {
            return SplitInternal(separator ?? Empty, null, int.MaxValue, options);
        }

        public string[] Split(string? separator, int count, StringSplitOptions options = StringSplitOptions.None)
        {
            return SplitInternal(separator ?? Empty, null, count, options);
        }

        public string[] Split(string[]? separator, StringSplitOptions options)
        {
            return SplitInternal(null, separator, int.MaxValue, options);
        }

        public string[] Split(string[]? separator, int count, StringSplitOptions options)
        {
            return SplitInternal(null, separator, count, options);
        }

        private static void CheckStringSplitOptions(StringSplitOptions options)
        {
            const StringSplitOptions All =
                StringSplitOptions.None |
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries;

            if ((options & ~All) != 0)
                throw new ArgumentException("options");
        }
        private string[] SplitInternal(string separator, int count, StringSplitOptions options)
        {
            if (count <= 1 || Length == 0)
            {
                return CreateSplitArrayOfThisAsSoleValue(options, count);
            }

            int[] sepListArray = MakeSeparatorList(this, separator, out int sepCount);
            if (sepCount == 0)
            {
                return CreateSplitArrayOfThisAsSoleValue(options, count);
            }

            ReadOnlySpan<int> sepList = new ReadOnlySpan<int>(sepListArray).Slice(0, sepCount);

            return (options != StringSplitOptions.None)
                ? SplitWithPostProcessing(sepList, default, separator.Length, count, options)
                : SplitWithoutPostProcessing(sepList, default, separator.Length, count);
        }
        private string[] SplitInternal(string? separator, string?[]? separators, int count, StringSplitOptions options)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException("count");

            CheckStringSplitOptions(options);

            bool singleSeparator = separator != null;

            if (!singleSeparator && (separators == null || separators.Length == 0))
            {
                // split on whitespace
                return SplitInternal(default(ReadOnlySpan<char>), count, options);
            }

        ShortCircuit:
            if (count <= 1 || Length == 0)
            {
                // Per the method's documentation, we'll short-circuit the search for separators.
                // But we still need to post-process the results based on the caller-provided flags.
                return CreateSplitArrayOfThisAsSoleValue(options, count);
            }

            if (singleSeparator)
            {
                if (separator.Length == 0)
                {
                    count = 1;
                    goto ShortCircuit;
                }
                else
                {
                    return SplitInternal(separator, count, options);
                }
            }

            int[] sepListArray;
            int[] lengthListArray;
            int sepCount;

            MakeSeparatorListAny(this, separators, out sepListArray, out lengthListArray, out sepCount);

            ReadOnlySpan<int> sepList = new ReadOnlySpan<int>(sepListArray).Slice(0, sepCount);
            ReadOnlySpan<int> lengthList = new ReadOnlySpan<int>(lengthListArray).Slice(0, sepCount);

            if (sepList.Length == 0)
            {
                return CreateSplitArrayOfThisAsSoleValue(options, count);
            }

            string[] result = (options != StringSplitOptions.None)
                ? SplitWithPostProcessing(sepList, lengthList, 0, count, options)
                : SplitWithoutPostProcessing(sepList, lengthList, 0, count);

            return result;
        }
        private string[] SplitInternal(ReadOnlySpan<char> separators, int count, StringSplitOptions options)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException("count");

            CheckStringSplitOptions(options);

        ShortCircuit:
            if (count <= 1 || Length == 0)
            {
                // Per the method's documentation, we'll short-circuit the search for separators.
                // But we still need to post-process the results based on the caller-provided flags.
                return CreateSplitArrayOfThisAsSoleValue(options, count);
            }

            if (separators.IsEmpty && count > Length)
            {
                // Caller is already splitting on whitespace; no need for separate trim step if the count is sufficient
                // to examine the whole input.
                options &= ~StringSplitOptions.TrimEntries;
            }

            int[] sepListArray = MakeSeparatorListAny(this, separators, out int sepCount);
            if (sepCount == 0)
            {
                count = 1;
                goto ShortCircuit;
            }

            ReadOnlySpan<int> sepList = new ReadOnlySpan<int>(sepListArray).Slice(0, sepCount);

            string[] result = (options != StringSplitOptions.None)
                ? SplitWithPostProcessing(sepList, default, 1, count, options)
                : SplitWithoutPostProcessing(sepList, default, 1, count);

            return result;
        }
        private static bool IsMatchSeparator(char c, ReadOnlySpan<char> separators)
        {
            if (separators.IsEmpty)
                return Char.IsWhiteSpace(c);

            for (int i = 0; i < separators.Length; i++)
            {
                if (c == separators[i])
                    return true;
            }

            return false;
        }
        private static int[] MakeSeparatorListAny(string source, ReadOnlySpan<char> separators, out int count)
        {
            int len = source.Length;
            count = 0;

            if (len == 0)
                return Array.Empty<int>();

            ref char src = ref source.GetRawStringData();

            // count separators
            for (int i = 0; i < len; i++)
            {
                char c = System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i);
                if (IsMatchSeparator(c, separators))
                    count++;
            }

            if (count == 0)
                return Array.Empty<int>();

            int[] result = new int[count];
            int pos = 0;

            // write separator indices
            for (int i = 0; i < len; i++)
            {
                char c = System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i);
                if (IsMatchSeparator(c, separators))
                    result[pos++] = i;
            }

            return result;
        }
        private static bool MatchStringSeparatorAt(string source, int index, string separator)
        {
            int sepLen = separator.Length;
            if (sepLen == 0)
                return false;

            if (index > source.Length - sepLen)
                return false;

            for (int i = 0; i < sepLen; i++)
            {
                if (source[index + i] != separator[i])
                    return false;
            }

            return true;
        }

        private static int[] MakeSeparatorList(string source, string separator, out int count)
        {
            count = 0;

            int sourceLength = source.Length;
            int sepLen = separator.Length;

            if (sourceLength == 0 || sepLen == 0 || sepLen > sourceLength)
                return Array.Empty<int>();

            // count non overlapping matches
            for (int i = 0; i <= sourceLength - sepLen; i++)
            {
                if (MatchStringSeparatorAt(source, i, separator))
                {
                    count++;
                    i += sepLen - 1;
                }
            }

            if (count == 0)
                return Array.Empty<int>();

            int[] sepList = new int[count];
            int pos = 0;

            // record match positions
            for (int i = 0; i <= sourceLength - sepLen; i++)
            {
                if (MatchStringSeparatorAt(source, i, separator))
                {
                    sepList[pos++] = i;
                    i += sepLen - 1;
                }
            }

            return sepList;
        }

        private static bool TryMatchAnySeparatorAt(string source, int index, string?[] separators, out int matchedLength)
        {
            for (int s = 0; s < separators.Length; s++)
            {
                string sep = separators[s];
                if ((object)sep == null || sep.Length == 0)
                    continue;

                if (MatchStringSeparatorAt(source, index, sep))
                {
                    matchedLength = sep.Length;
                    return true;
                }
            }

            matchedLength = 0;
            return false;
        }

        private static void MakeSeparatorListAny(
            string source,
            string?[] separators,
            out int[] sepList,
            out int[] lengthList,
            out int count)
        {
            count = 0;

            int sourceLength = source.Length;
            if (sourceLength == 0)
            {
                sepList = Array.Empty<int>();
                lengthList = Array.Empty<int>();
                return;
            }

            // count matches
            for (int i = 0; i < sourceLength; i++)
            {
                if (TryMatchAnySeparatorAt(source, i, separators, out int matchedLength))
                {
                    count++;
                    i += matchedLength - 1;
                }
            }

            if (count == 0)
            {
                sepList = Array.Empty<int>();
                lengthList = Array.Empty<int>();
                return;
            }

            sepList = new int[count];
            lengthList = new int[count];

            int pos = 0;

            // record positions and lengths
            for (int i = 0; i < sourceLength; i++)
            {
                if (TryMatchAnySeparatorAt(source, i, separators, out int matchedLength))
                {
                    sepList[pos] = i;
                    lengthList[pos] = matchedLength;
                    pos++;
                    i += matchedLength - 1;
                }
            }
        }
        private string[] CreateSplitArrayOfThisAsSoleValue(StringSplitOptions options, int count)
        {
            if (count != 0)
            {
                string candidate = this;

                if ((options & StringSplitOptions.TrimEntries) != 0)
                {
                    candidate = candidate.Trim();
                }

                if ((options & StringSplitOptions.RemoveEmptyEntries) == 0 || candidate.Length != 0)
                {
                    return new string[] { candidate };
                }
            }

            return Array.Empty<string>();
        }
        // This function may trim entries or omit empty entries
        private string[] SplitWithPostProcessing(ReadOnlySpan<int> sepList, ReadOnlySpan<int> lengthList, int defaultLength, int count, StringSplitOptions options)
        {
            int numReplaces = sepList.Length;

            // Allocate array to hold items. This array may not be
            // filled completely in this function, we will create a
            // new array and copy string references to that new array.
            int maxItems = (numReplaces < count) ? (numReplaces + 1) : count;
            string[] splitStrings = new string[maxItems];

            int currIndex = 0;
            int arrIndex = 0;

            ReadOnlySpan<char> thisEntry;

            for (int i = 0; i < numReplaces; i++)
            {
                thisEntry = this.AsSpan(currIndex, sepList[i] - currIndex);
                if ((options & StringSplitOptions.TrimEntries) != 0)
                {
                    thisEntry = thisEntry.Trim();
                }
                if (!thisEntry.IsEmpty || ((options & StringSplitOptions.RemoveEmptyEntries) == 0))
                {
                    splitStrings[arrIndex++] = thisEntry.ToString();
                }
                currIndex = sepList[i] + (lengthList.IsEmpty ? defaultLength : lengthList[i]);
                if (arrIndex == count - 1)
                {
                    // The next iteration of the loop will provide the final entry into the
                    // results array. If needed, skip over all empty entries before that
                    // point.
                    if ((options & StringSplitOptions.RemoveEmptyEntries) != 0)
                    {
                        while (++i < numReplaces)
                        {
                            thisEntry = this.AsSpan(currIndex, sepList[i] - currIndex);
                            if ((options & StringSplitOptions.TrimEntries) != 0)
                            {
                                thisEntry = thisEntry.Trim();
                            }
                            if (!thisEntry.IsEmpty)
                            {
                                break; // there's useful data here
                            }
                            currIndex = sepList[i] + (lengthList.IsEmpty ? defaultLength : lengthList[i]);
                        }
                    }
                    break;
                }
            }


            // Handle the last substring at the end of the array
            // (could be empty if separator appeared at the end of the input string)
            thisEntry = this.AsSpan(currIndex);
            if ((options & StringSplitOptions.TrimEntries) != 0)
            {
                thisEntry = thisEntry.Trim();
            }
            if (!thisEntry.IsEmpty || ((options & StringSplitOptions.RemoveEmptyEntries) == 0))
            {
                splitStrings[arrIndex++] = thisEntry.ToString();
            }

            Array.Resize<string>(ref splitStrings, arrIndex);
            return splitStrings;
        }
        // This function will not trim entries or special-case empty entries
        private string[] SplitWithoutPostProcessing(ReadOnlySpan<int> sepList, ReadOnlySpan<int> lengthList, int defaultLength, int count)
        {
            int currIndex = 0;
            int arrIndex = 0;

            count--;
            int numActualReplaces = (sepList.Length < count) ? sepList.Length : count;

            // Allocate space for the new array.
            // +1 for the string from the end of the last replace to the end of the string.
            string[] splitStrings = new string[numActualReplaces + 1];

            for (int i = 0; i < numActualReplaces && currIndex < Length; i++)
            {
                splitStrings[arrIndex++] = Substring(currIndex, sepList[i] - currIndex);
                currIndex = sepList[i] + (lengthList.IsEmpty ? defaultLength : lengthList[i]);
            }

            // Handle the last string at the end of the array if there is one.
            if (currIndex < Length && numActualReplaces >= 0)
            {
                splitStrings[arrIndex] = Substring(currIndex);
            }
            else if (arrIndex == numActualReplaces)
            {
                // We had a separator character at the end of a string.  Rather than just allowing
                // a null character, we'll replace the last element in the array with an empty string.
                splitStrings[arrIndex] = Empty;
            }

            return splitStrings;
        }


        public string TrimStart()
        {
            int len = Length;
            if (len == 0) return this;

            int i = 0;
            while (i < len && Char.IsWhiteSpace(this[i])) i++;

            if (i == 0) return this;
            if (i == len) return Empty;
            return Substring(i);
        }

        public string TrimEnd()
        {
            int len = Length;
            if (len == 0) return this;

            int i = len - 1;
            while (i >= 0 && Char.IsWhiteSpace(this[i])) i--;

            if (i == len - 1) return this;
            if (i < 0) return Empty;
            return Substring(0, i + 1);
        }

        public string Trim()
        {
            int len = Length;
            if (len == 0) return this;

            int start = 0;
            while (start < len && Char.IsWhiteSpace(this[start])) start++;
            if (start == len) return Empty;

            int end = len - 1;
            while (end >= start && Char.IsWhiteSpace(this[end])) end--;

            if (start == 0 && end == len - 1) return this;
            return Substring(start, end - start + 1);
        }

        public string PadLeft(int totalWidth) => PadLeft(totalWidth, ' ');
        public string PadLeft(int totalWidth, char paddingChar)
        {
            if (totalWidth < 0) throw new ArgumentOutOfRangeException("totalWidth");

            int oldLength = Length;
            int padCount = totalWidth - oldLength;
            if (padCount <= 0) return this;

            string result = FastAllocateString(totalWidth);
            ref char dst = ref result.GetRawStringData();

            for (int i = 0; i < padCount; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = paddingChar;

            ref char src = ref GetRawStringData();
            for (int i = 0; i < oldLength; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, padCount + i) =
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i);

            return result;
        }

        public string PadRight(int totalWidth) => PadRight(totalWidth, ' ');
        public string PadRight(int totalWidth, char paddingChar)
        {
            if (totalWidth < 0) throw new ArgumentOutOfRangeException("totalWidth");

            int oldLength = Length;
            int padCount = totalWidth - oldLength;
            if (padCount <= 0) return this;

            string result = FastAllocateString(totalWidth);
            ref char dst = ref result.GetRawStringData();

            ref char src = ref GetRawStringData();
            for (int i = 0; i < oldLength; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) =
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i);

            for (int i = 0; i < padCount; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, oldLength + i) = paddingChar;

            return result;
        }
        public bool Contains(char value) => IndexOf(value) >= 0;

        public bool Contains(string value)
        {
            if ((object)value == null) throw new ArgumentNullException("value");
            return IndexOf(value, 0) >= 0;
        }

        public string ToLower() => ToLowerInvariant();
        public string ToLowerInvariant()
        {
            return System.Globalization.TextInfo.Invariant.ToLower(this);
        }
        public string ToUpper() => ToUpperInvariant();
        public string ToUpperInvariant()
        {
            return System.Globalization.TextInfo.Invariant.ToUpper(this);
        }



        private static unsafe int CompareOrdinalHelper(string strA, string strB)
        {
            int length = Math.Min(strA.Length, strB.Length);

            fixed (char* ap = &strA._firstChar) fixed (char* bp = &strB._firstChar)
            {
                char* a = ap;
                char* b = bp;

                if (*(a + 1) != *(b + 1)) goto DiffOffset1;

                length -= 2; a += 2; b += 2;

                // unroll the loop
#if TARGET_64BIT
                while (length >= 12)
                {
                    if (*(long*)a != *(long*)b) goto DiffOffset0;
                    if (*(long*)(a + 4) != *(long*)(b + 4)) goto DiffOffset4;
                    if (*(long*)(a + 8) != *(long*)(b + 8)) goto DiffOffset8;
                    length -= 12; a += 12; b += 12;
                }
#else // TARGET_64BIT
                while (length >= 10)
                {
                    if (*(int*)a != *(int*)b) goto DiffOffset0;
                    if (*(int*)(a + 2) != *(int*)(b + 2)) goto DiffOffset2;
                    if (*(int*)(a + 4) != *(int*)(b + 4)) goto DiffOffset4;
                    if (*(int*)(a + 6) != *(int*)(b + 6)) goto DiffOffset6;
                    if (*(int*)(a + 8) != *(int*)(b + 8)) goto DiffOffset8;
                    length -= 10; a += 10; b += 10;
                }
#endif // TARGET_64BIT

                // Fallback loop:
                // go back to slower code path and do comparison on 4 bytes at a time.
                // This depends on the fact that the String objects are
                // always zero terminated and that the terminating zero is not included
                // in the length. For odd string sizes, the last compare will include
                // the zero terminator.
                while (length > 0)
                {
                    if (*(int*)a != *(int*)b) goto DiffNextInt;
                    length -= 2;
                    a += 2;
                    b += 2;
                }

                // At this point, we have compared all the characters in at least one string.
                // The longer string will be larger.
                return strA.Length - strB.Length;

#if TARGET_64BIT
            DiffOffset8: a += 4; b += 4;
            DiffOffset4: a += 4; b += 4;
#else // TARGET_64BIT
                // Use jumps instead of falling through, since
                // otherwise going to DiffOffset8 will involve
                // 8 add instructions before getting to DiffNextInt
            DiffOffset8: a += 8; b += 8; goto DiffOffset0;
            DiffOffset6: a += 6; b += 6; goto DiffOffset0;
            DiffOffset4: a += 2; b += 2;
            DiffOffset2: a += 2; b += 2;
#endif // TARGET_64BIT

            DiffOffset0:
                // If we reached here, we already see a difference in the unrolled loop above
#if TARGET_64BIT
                if (*(int*)a == *(int*)b)
                {
                    a += 2; b += 2;
                }
#endif // TARGET_64BIT

            DiffNextInt:
                if (*a != *b) return *a - *b;

            DiffOffset1:
                return *(a + 1) - *(b + 1);
            }
        }

        public static int CompareOrdinal(string? strA, string? strB)
        {
            if (object.ReferenceEquals(strA, strB))
            {
                return 0;
            }

            // They can't both be null at this point.
            if (strA == null)
            {
                return -1;
            }
            if (strB == null)
            {
                return 1;
            }

            // Most common case, first character is different.
            // This will return false for empty strings.
            if (strA._firstChar != strB._firstChar)
            {
                return strA._firstChar - strB._firstChar;
            }

            return CompareOrdinalHelper(strA, strB);
        }

        public static int CompareOrdinal(string? strA, int indexA, string? strB, int indexB, int length)
        {
            if (strA == null || strB == null)
            {
                if (object.ReferenceEquals(strA, strB))
                {
                    // They're both null
                    return 0;
                }

                return strA == null ? -1 : 1;
            }

            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            if (indexA < 0 || indexB < 0)
            {
                string paramName = indexA < 0 ? nameof(indexA) : nameof(indexB);
                throw new ArgumentOutOfRangeException(paramName);
            }

            int lengthA = Math.Min(length, strA.Length - indexA);
            int lengthB = Math.Min(length, strB.Length - indexB);

            if (lengthA < 0 || lengthB < 0)
            {
                string paramName = lengthA < 0 ? nameof(indexA) : nameof(indexB);
                throw new ArgumentOutOfRangeException(paramName);
            }

            if (length == 0 || (object.ReferenceEquals(strA, strB) && indexA == indexB))
            {
                return 0;
            }

            return CompareOrdinalHelper(strA, indexA, lengthA, strB, indexB, lengthB);
        }
    }

    public struct Boolean
    {
        private readonly bool m_value;
        internal const int True = 1;
        internal const int False = 0;
        internal const string TrueLiteral = "True";
        internal const string FalseLiteral = "False";
        public override string ToString()
        {
            return m_value ? TrueLiteral : FalseLiteral;
        }
    }
    public readonly struct Char
        : IComparable, IComparable<char>, IEquatable<char>, ISpanFormattable, IBinaryInteger<char>, IMinMaxValue<char>, IUnsignedNumber<char>,
          IUtfChar<char>
    {
        private readonly char m_value;
        private const byte IsWhiteSpaceFlag = 0x80;
        private const byte IsUpperCaseLetterFlag = 0x40;
        private const byte IsLowerCaseLetterFlag = 0x20;
        private const byte UnicodeCategoryMask = 0x1F;
        public const char MaxValue = (char)0xFFFF;
        public const char MinValue = (char)0x00;

        public bool Equals(char obj)
        {
            return m_value == obj;
        }
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (!(obj is char))
            {
                return false;
            }
            return m_value == ((char)obj).m_value;
        }
        public override int GetHashCode()
        {
            return (int)m_value | ((int)m_value << 16);
        }
        public override string ToString()
        {
            return System.Number.CharToString(m_value);
        }

        private static ReadOnlySpan<byte> Latin1CharInfo =>
        [
        //  0     1     2     3     4     5     6     7     8     9     A     B     C     D     E     F
            0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x8E, 0x8E, 0x8E, 0x8E, 0x8E, 0x0E, 0x0E, // U+0000..U+000F
            0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, // U+0010..U+001F
            0x8B, 0x18, 0x18, 0x18, 0x1A, 0x18, 0x18, 0x18, 0x14, 0x15, 0x18, 0x19, 0x18, 0x13, 0x18, 0x18, // U+0020..U+002F
            0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x18, 0x18, 0x19, 0x19, 0x19, 0x18, // U+0030..U+003F
            0x18, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, // U+0040..U+004F
            0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x14, 0x18, 0x15, 0x1B, 0x12, // U+0050..U+005F
            0x1B, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, // U+0060..U+006F
            0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x14, 0x19, 0x15, 0x19, 0x0E, // U+0070..U+007F
            0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x8E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, // U+0080..U+008F
            0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, // U+0090..U+009F
            0x8B, 0x18, 0x1A, 0x1A, 0x1A, 0x1A, 0x1C, 0x18, 0x1B, 0x1C, 0x04, 0x16, 0x19, 0x0F, 0x1C, 0x1B, // U+00A0..U+00AF
            0x1C, 0x19, 0x0A, 0x0A, 0x1B, 0x21, 0x18, 0x18, 0x1B, 0x0A, 0x04, 0x17, 0x0A, 0x0A, 0x0A, 0x18, // U+00B0..U+00BF
            0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, // U+00C0..U+00CF
            0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x19, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x21, // U+00D0..U+00DF
            0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, // U+00E0..U+00EF
            0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x19, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, // U+00F0..U+00FF
        ];

        public static bool IsBetween(char c, char minInclusive, char maxInclusive) =>
            (uint)(c - minInclusive) <= (uint)(maxInclusive - minInclusive);
        private static bool IsBetween(
            System.Globalization.UnicodeCategory c,
            System.Globalization.UnicodeCategory min,
            System.Globalization.UnicodeCategory max) =>
            (uint)(c - min) <= (uint)(max - min);
        public static bool IsAscii(char c) => (uint)c <= '\x007f';
        public static bool IsAsciiLetter(char c) => (uint)((c | 0x20) - 'a') <= 'z' - 'a';
        public static bool IsAsciiDigit(char c) => IsBetween(c, '0', '9');
        public static bool IsSurrogate(char c)
        {
            return IsBetween(c, CharUnicodeInfo.HIGH_SURROGATE_START, CharUnicodeInfo.LOW_SURROGATE_END);
        }
        public static bool IsAsciiLetterOrDigit(char c) => IsAsciiLetter(c) | IsBetween(c, '0', '9');
        public static bool IsAsciiLetterLower(char c) => IsBetween(c, 'a', 'z');
        public static bool IsAsciiLetterUpper(char c) => IsBetween(c, 'A', 'Z');

        public static char ToLowerInvariant(char c) => System.Globalization.TextInfo.ToLowerInvariant(c);
        public static char ToUpperInvariant(char c) => System.Globalization.TextInfo.ToUpperInvariant(c);


        public static bool IsWhiteSpace(char c)
        {
            if (IsLatin1(c))
            {
                return IsWhiteSpaceLatin1(c);
            }
            //return CharUnicodeInfo.GetIsWhiteSpace(c);
            if (c == '\u1680') return true;
            if (c >= '\u2000' && c <= '\u200A') return true;
            if (c == '\u2028' || c == '\u2029') return true;
            if (c == '\u202F' || c == '\u205F') return true;
            if (c == '\u3000' || c == '\uFEFF') return true;
            return false;
        }

        private static bool IsLatin1(char c) => (uint)c < (uint)Latin1CharInfo.Length;
        private static bool IsWhiteSpaceLatin1(char c) => (Latin1CharInfo[c] & IsWhiteSpaceFlag) != 0;
        private static System.Globalization.UnicodeCategory GetLatin1UnicodeCategory(char c)
            => (System.Globalization.UnicodeCategory)(Latin1CharInfo[c] & UnicodeCategoryMask);

        public static bool IsHighSurrogate(char c)
        {
            return IsBetween(c, System.Globalization.CharUnicodeInfo.HIGH_SURROGATE_START, System.Globalization.CharUnicodeInfo.HIGH_SURROGATE_END);
        }

        public static bool IsHighSurrogate(string s, int index)
        {
            if (s == null)
            {
                throw new ArgumentNullException();
            }
            if ((uint)index >= (uint)s.Length)
            {
                throw new ArgumentOutOfRangeException();
            }

            return IsHighSurrogate(s[index]);
        }
        public static bool IsLowSurrogate(char c)
        {
            return IsBetween(c, System.Globalization.CharUnicodeInfo.LOW_SURROGATE_START, System.Globalization.CharUnicodeInfo.LOW_SURROGATE_END);
        }

        public static bool IsLowSurrogate(string s, int index)
        {
            if (s == null)
            {
                throw new ArgumentNullException();
            }
            if ((uint)index >= (uint)s.Length)
            {
                throw new ArgumentOutOfRangeException();
            }

            return IsLowSurrogate(s[index]);
        }

        public int CompareTo(object? value)
        {
            if (value == null)
            {
                return 1;
            }
            if (!(value is char))
            {
                throw new ArgumentException(SR.Arg_MustBeChar);
            }

            return m_value - ((char)value).m_value;
        }

        public int CompareTo(char value)
        {
            return m_value - value;
        }

        public string ToString(IFormatProvider? provider)
        {
            return System.Number.CharToString(m_value);
        }

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        {
            if (!destination.IsEmpty)
            {
                destination[0] = m_value;
                charsWritten = 1;
                return true;
            }

            charsWritten = 0;
            return false;
        }

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => System.Number.CharToString(m_value);

        public static char Parse(string s)
        {
            if (s is null) { ThrowHelper.ThrowArgumentNullException(ExceptionArgument.s); }
            return Parse(s.AsSpan());
        }

        internal static char Parse(ReadOnlySpan<char> s)
        {
            if (s.Length != 1)
            {
                ThrowHelper.ThrowFormatException_NeedSingleChar();
            }
            return s[0];
        }

        public static bool TryParse([NotNullWhen(true)] string? s, out char result)
        {
            if (s is null)
            {
                result = '\0';
                return false;
            }
            return TryParse(s.AsSpan(), out result);
        }

        internal static bool TryParse(ReadOnlySpan<char> s, out char result)
        {
            if (s.Length != 1)
            {
                result = '\0';
                return false;
            }

            result = s[0];
            return true;
        }


        static char IAdditionOperators<char, char, char>.operator +(char left, char right) => (char) (left + right);
        static char IAdditionOperators<char, char, char>.operator checked +(char left, char right) => checked((char)(left + right));
        static char IAdditiveIdentity<char, char>.AdditiveIdentity => (char)0;
        static char IBinaryInteger<char>.LeadingZeroCount(char value) => (char)(BitOperations.LeadingZeroCount(value) - 16);
        static char IBinaryInteger<char>.PopCount(char value) => (char)BitOperations.PopCount(value);
        static char IBinaryInteger<char>.RotateLeft(char value, int rotateAmount) => (char)((value << (rotateAmount & 15)) | (value >> ((16 - rotateAmount) & 15)));
        static char IBinaryInteger<char>.RotateRight(char value, int rotateAmount) => (char)((value >> (rotateAmount & 15)) | (value << ((16 - rotateAmount) & 15)));
        static char IBinaryInteger<char>.TrailingZeroCount(char value) => (char)(BitOperations.TrailingZeroCount(value << 16) - 16);
        int IBinaryInteger<char>.GetShortestBitLength() => (sizeof(char) * 8) - ushort.LeadingZeroCount(m_value);
        int IBinaryInteger<char>.GetByteCount() => sizeof(char);
        static char IBinaryNumber<char>.AllBitsSet => (char)0xFFFF;
        static bool IBinaryNumber<char>.IsPow2(char value) => ushort.IsPow2(value);
        static char IBinaryNumber<char>.Log2(char value) => (char)(ushort.Log2(value));
        static char IBitwiseOperators<char, char, char>.operator &(char left, char right) => (char)(left & right);
        static char IBitwiseOperators<char, char, char>.operator |(char left, char right) => (char)(left | right);
        static char IBitwiseOperators<char, char, char>.operator ^(char left, char right) => (char)(left ^ right);
        static char IBitwiseOperators<char, char, char>.operator ~(char value) => (char)(~value);
        static bool IComparisonOperators<char, char, bool>.operator <(char left, char right) => left < right;
        static bool IComparisonOperators<char, char, bool>.operator <=(char left, char right) => left <= right;
        static bool IComparisonOperators<char, char, bool>.operator >(char left, char right) => left > right;
        static bool IComparisonOperators<char, char, bool>.operator >=(char left, char right) => left >= right;
        static char IDecrementOperators<char>.operator --(char value) => --value;
        static char IDecrementOperators<char>.operator checked --(char value) => checked(--value);
        static char IDivisionOperators<char, char, char>.operator /(char left, char right) => (char)(left / right);
        static bool IEqualityOperators<char, char, bool>.operator ==(char left, char right) => left == right;
        static bool IEqualityOperators<char, char, bool>.operator !=(char left, char right) => left != right;
        static char IIncrementOperators<char>.operator ++(char value) => ++value;
        static char IIncrementOperators<char>.operator checked ++(char value) => checked(++value);
        static char IMinMaxValue<char>.MinValue => MinValue;
        static char IMinMaxValue<char>.MaxValue => MaxValue;
        static char IModulusOperators<char, char, char>.operator %(char left, char right) => (char)(left % right);
        static char IMultiplicativeIdentity<char, char>.MultiplicativeIdentity => (char)1;
        static char IMultiplyOperators<char, char, char>.operator *(char left, char right) => (char)(left * right);
        static char IMultiplyOperators<char, char, char>.operator checked *(char left, char right) => checked((char)(left * right));
        static char INumberBase<char>.One => (char)1;
        static int INumberBase<char>.Radix => 2;
        static char INumberBase<char>.Zero => (char)0;
        static char INumberBase<char>.Abs(char value) => value;
        static bool INumberBase<char>.IsCanonical(char value) => true;
        static bool INumberBase<char>.IsComplexNumber(char value) => false;
        static bool INumberBase<char>.IsEvenInteger(char value) => (value & 1) == 0;
        static bool INumberBase<char>.IsFinite(char value) => true;
        static bool INumberBase<char>.IsImaginaryNumber(char value) => false;
        static bool INumberBase<char>.IsInfinity(char value) => false;
        static bool INumberBase<char>.IsInteger(char value) => true;
        static bool INumberBase<char>.IsNaN(char value) => false;
        static bool INumberBase<char>.IsNegative(char value) => false;
        static bool INumberBase<char>.IsNegativeInfinity(char value) => false;
        static bool INumberBase<char>.IsNormal(char value) => value != 0;
        static bool INumberBase<char>.IsOddInteger(char value) => (value & 1) != 0;
        static bool INumberBase<char>.IsPositive(char value) => true;
        static bool INumberBase<char>.IsPositiveInfinity(char value) => false;
        static bool INumberBase<char>.IsRealNumber(char value) => true;
        static bool INumberBase<char>.IsSubnormal(char value) => false;
        static bool INumberBase<char>.IsZero(char value) => (value == 0);
        static char INumberBase<char>.MaxMagnitude(char x, char y) => (char)Math.Max(x, y);
        static char INumberBase<char>.MaxMagnitudeNumber(char x, char y) => (char)Math.Max(x, y);
        static char INumberBase<char>.MinMagnitude(char x, char y) => (char)Math.Min(x, y);
        static char INumberBase<char>.MinMagnitudeNumber(char x, char y) => (char)Math.Min(x, y);
        static char INumberBase<char>.MultiplyAddEstimate(char left, char right, char addend) => (char)((left * right) + addend);
        static char INumberBase<char>.Parse(string s, NumberStyles style, IFormatProvider? provider) => Parse(s);
        static char INumberBase<char>.Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider) => Parse(s);
        static bool INumberBase<char>.TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, out char result) => TryParse(s, out result);
        static bool INumberBase<char>.TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out char result) => TryParse(s, out result);
        static char IParsable<char>.Parse(string s, IFormatProvider? provider) => Parse(s);
        static bool IParsable<char>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out char result) => TryParse(s, out result);
        static char IShiftOperators<char, int, char>.operator <<(char value, int shiftAmount) => (char)(value << (shiftAmount & 15));
        static char IShiftOperators<char, int, char>.operator >>(char value, int shiftAmount) => (char)(value >> (shiftAmount & 15));
        static char IShiftOperators<char, int, char>.operator >>>(char value, int shiftAmount) => (char)(value >>> (shiftAmount & 15));
        static char ISpanParsable<char>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s);
        static bool ISpanParsable<char>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out char result) => TryParse(s, out result);
        static char ISubtractionOperators<char, char, char>.operator -(char left, char right) => (char)(left - right);
        static char ISubtractionOperators<char, char, char>.operator checked -(char left, char right) => checked((char)(left - right));
        static char IUnaryNegationOperators<char, char>.operator -(char value) => (char)(-value);
        static char IUnaryNegationOperators<char, char>.operator checked -(char value) => checked((char)(-value));
        static char IUnaryPlusOperators<char, char>.operator +(char value) => (char)(+value);
        static char IUtfChar<char>.CastFrom(byte value) => (char)value;
        static char IUtfChar<char>.CastFrom(char value) => value;
        static char IUtfChar<char>.CastFrom(int value) => (char)value;
        static char IUtfChar<char>.CastFrom(uint value) => (char)value;
        static char IUtfChar<char>.CastFrom(ulong value) => (char)value;

        static bool INumberBase<char>.TryConvertFromChecked<TOther>(TOther value, out char result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Checked, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (char)bits;
            return converted;
        }

        static bool INumberBase<char>.TryConvertFromSaturating<TOther>(TOther value, out char result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Saturating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (char)bits;
            return converted;
        }

        static bool INumberBase<char>.TryConvertFromTruncating<TOther>(TOther value, out char result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Truncating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (char)bits;
            return converted;
        }

        static bool INumberBase<char>.TryConvertToChecked<TOther>(char value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<char>.TryConvertToSaturating<TOther>(char value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<char>.TryConvertToTruncating<TOther>(char value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool IBinaryInteger<char>.TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out char value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: true, isUnsigned, 2, false, out ulong bits);
            value = read ? (char)bits : (char)0;
            return read;
        }

        static bool IBinaryInteger<char>.TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out char value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: false, isUnsigned, 2, false, out ulong bits);
            value = read ? (char)bits : (char)0;
            return read;
        }

        bool IBinaryInteger<char>.TryWriteBigEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)m_value, 2, bigEndian: true, destination, out bytesWritten);

        bool IBinaryInteger<char>.TryWriteLittleEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)m_value, 2, bigEndian: false, destination, out bytesWritten);

        static (char Quotient, char Remainder) IBinaryInteger<char>.DivRem(char left, char right)
        {
            char quotient = (char)(left / right);
            return (quotient, (char)(left - quotient * right));
        }
        static char INumber<char>.Clamp(char value, char min, char max) => (char)Math.Clamp((ushort)value, (ushort)min, (ushort)max);
        static char INumber<char>.CopySign(char value, char sign) => value;
        static char INumber<char>.Max(char x, char y) => (char)Math.Max(x, y);
        static char INumber<char>.MaxNumber(char x, char y) => (char)Math.Max(x, y);
        static char INumber<char>.Min(char x, char y) => (char)Math.Min(x, y);
        static char INumber<char>.MinNumber(char x, char y) => (char)Math.Min(x, y);
        static int INumber<char>.Sign(char value) => value == 0 ? 0 : 1;
    }

    public readonly struct SByte
        : IComparable, ISpanFormattable, IComparable<sbyte>, IEquatable<sbyte>, IBinaryInteger<sbyte>, IMinMaxValue<sbyte>, ISignedNumber<sbyte>
    {
        private readonly sbyte m_value;
        public const sbyte MaxValue = (sbyte)0x7F;
        public const sbyte MinValue = unchecked((sbyte)0x80);

        public bool Equals(sbyte obj)
        {
            return m_value == obj;
        }
        public override int GetHashCode()
        {
            return m_value;
        }
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (!(obj is sbyte))
            {
                return false;
            }
            return m_value == ((sbyte)obj).m_value;
        }





        public override string ToString()
        {
            return System.Number.Int32ToString((int)m_value);
        }
    

        public int CompareTo(object? value)
        {
            if (value is null)
                return 1;
            if (value is sbyte other)
                return CompareTo(other);
            throw new ArgumentException(SR.Arg_MustBeSByte);
        }

        public int CompareTo(sbyte value) => m_value < value ? -1 : m_value > value ? 1 : 0;

        public string ToString(string? format) => ToString(format, null);

        public string ToString(IFormatProvider? provider) => ToString(null, provider);

        public string ToString(string? format, IFormatProvider? provider) => Number.FormatInt32(m_value, 0xFF, format, provider);

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
            => Number.TryCopyFormatted(ToString(format.IsEmpty ? null : new string(format), provider), destination, out charsWritten);

        public static sbyte Parse(string s) => Parse(s, NumberStyles.Integer, null);

        public static sbyte Parse(string s, NumberStyles style) => Parse(s, style, null);

        public static sbyte Parse(string s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static sbyte Parse(string s, NumberStyles style, IFormatProvider? provider)
        {
            if (s is null)
                throw new ArgumentNullException(nameof(s));
            return Parse(s.AsSpan(), style, provider);
        }

        public static sbyte Parse(ReadOnlySpan<char> s, NumberStyles style = NumberStyles.Integer, IFormatProvider? provider = null)
        {
            Number.ParseStatus status = Number.TryParseBinaryInteger(s, style, out sbyte result);
            if (status != Number.ParseStatus.OK)
                Number.ThrowParseException(status);
            return result;
        }

        public static sbyte Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static bool TryParse([NotNullWhen(true)] string? s, out sbyte result) => TryParse(s, NumberStyles.Integer, null, out result);

        public static bool TryParse(ReadOnlySpan<char> s, out sbyte result) => TryParse(s, NumberStyles.Integer, null, out result);

        public static bool TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, out sbyte result)
        {
            if (s is null)
            {
                result = 0;
                return false;
            }
            return TryParse(s.AsSpan(), style, provider, out result);
        }

        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out sbyte result)
            => Number.TryParseBinaryInteger(s, style, out result) == Number.ParseStatus.OK;

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out sbyte result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out sbyte result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static (sbyte Quotient, sbyte Remainder) DivRem(sbyte left, sbyte right)
        {
            sbyte quotient = (sbyte)(left / right);
            return (quotient, (sbyte)(left - quotient * right));
        }

        public static sbyte LeadingZeroCount(sbyte value) => (sbyte)(BitOperations.LeadingZeroCount((uint)(byte)value) - 24);

        public static sbyte PopCount(sbyte value) => (sbyte)BitOperations.PopCount((uint)(byte)value);

        public static sbyte RotateLeft(sbyte value, int rotateAmount) => (sbyte)(((byte)value << (rotateAmount & 7)) | ((byte)value >> ((8 - rotateAmount) & 7)));

        public static sbyte RotateRight(sbyte value, int rotateAmount) => (sbyte)(((byte)value >> (rotateAmount & 7)) | ((byte)value << ((8 - rotateAmount) & 7)));

        public static sbyte TrailingZeroCount(sbyte value) => (sbyte)(BitOperations.TrailingZeroCount((uint)(byte)value << 24) - 24);

        public static bool IsPow2(sbyte value) => BitOperations.IsPow2((int)value);

        public static sbyte Log2(sbyte value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), SR.ArgumentOutOfRange_NeedNonNegNum);
            return (sbyte)BitOperations.Log2((uint)(byte)value);
        }

        public static sbyte Clamp(sbyte value, sbyte min, sbyte max) => Math.Clamp(value, min, max);

        public static sbyte Max(sbyte x, sbyte y) => Math.Max(x, y);

        public static sbyte Min(sbyte x, sbyte y) => Math.Min(x, y);

        public static bool IsEvenInteger(sbyte value) => (value & 1) == 0;

        public static bool IsOddInteger(sbyte value) => (value & 1) != 0;

        public static sbyte CreateChecked<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Checked);

        public static sbyte CreateSaturating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Saturating);

        public static sbyte CreateTruncating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Truncating);

        private static sbyte Create<TOther>(TOther value, NumericConversion.Mode mode) where TOther : INumberBase<TOther>
        {
            if (NumericConversion.TryConvertToInteger(value, mode, (long)MinValue, (ulong)MaxValue, out ulong bits))
                return (sbyte)bits;
            sbyte result;
            bool converted = mode switch
            {
                NumericConversion.Mode.Checked => TOther.TryConvertToChecked(value, out result),
                NumericConversion.Mode.Saturating => TOther.TryConvertToSaturating(value, out result),
                _ => TOther.TryConvertToTruncating(value, out result),
            };
            if (!converted)
                ThrowHelper.ThrowNotSupportedException();
            return result;
        }

        public static sbyte Abs(sbyte value) => Math.Abs(value);

        public static sbyte CopySign(sbyte value, sbyte sign)
        {
            sbyte absValue = value;
            if (absValue < 0)
                absValue = (sbyte)(-absValue);
            if (sign >= 0)
            {
                if (absValue < 0)
                    Math.ThrowNegateTwosCompOverflow();
                return absValue;
            }
            return (sbyte)(-absValue);
        }

        public static int Sign(sbyte value) => value < 0 ? -1 : value > 0 ? 1 : 0;

        public static bool IsNegative(sbyte value) => value < 0;

        public static bool IsPositive(sbyte value) => value >= 0;

        public static sbyte MaxMagnitude(sbyte x, sbyte y)
        {
            sbyte absX = x;
            if (absX < 0)
            {
                absX = (sbyte)(-absX);
                if (absX < 0)
                    return x;
            }
            sbyte absY = y;
            if (absY < 0)
            {
                absY = (sbyte)(-absY);
                if (absY < 0)
                    return y;
            }
            if (absX > absY)
                return x;
            if (absX == absY)
                return IsNegative(x) ? y : x;
            return y;
        }

        public static sbyte MinMagnitude(sbyte x, sbyte y)
        {
            sbyte absX = x;
            if (absX < 0)
            {
                absX = (sbyte)(-absX);
                if (absX < 0)
                    return y;
            }
            sbyte absY = y;
            if (absY < 0)
            {
                absY = (sbyte)(-absY);
                if (absY < 0)
                    return x;
            }
            if (absX < absY)
                return x;
            if (absX == absY)
                return IsNegative(x) ? x : y;
            return y;
        }

        static sbyte IAdditionOperators<sbyte, sbyte, sbyte>.operator +(sbyte left, sbyte right) => (sbyte)(left + right);
        static sbyte IAdditionOperators<sbyte, sbyte, sbyte>.operator checked +(sbyte left, sbyte right) => checked((sbyte)(left + right));
        static sbyte ISubtractionOperators<sbyte, sbyte, sbyte>.operator -(sbyte left, sbyte right) => (sbyte)(left - right);
        static sbyte ISubtractionOperators<sbyte, sbyte, sbyte>.operator checked -(sbyte left, sbyte right) => checked((sbyte)(left - right));
        static sbyte IMultiplyOperators<sbyte, sbyte, sbyte>.operator *(sbyte left, sbyte right) => (sbyte)(left * right);
        static sbyte IMultiplyOperators<sbyte, sbyte, sbyte>.operator checked *(sbyte left, sbyte right) => checked((sbyte)(left * right));
        static sbyte IDivisionOperators<sbyte, sbyte, sbyte>.operator /(sbyte left, sbyte right) => (sbyte)(left / right);
        static sbyte IModulusOperators<sbyte, sbyte, sbyte>.operator %(sbyte left, sbyte right) => (sbyte)(left % right);
        static sbyte IBitwiseOperators<sbyte, sbyte, sbyte>.operator &(sbyte left, sbyte right) => (sbyte)(left & right);
        static sbyte IBitwiseOperators<sbyte, sbyte, sbyte>.operator |(sbyte left, sbyte right) => (sbyte)(left | right);
        static sbyte IBitwiseOperators<sbyte, sbyte, sbyte>.operator ^(sbyte left, sbyte right) => (sbyte)(left ^ right);
        static sbyte IBitwiseOperators<sbyte, sbyte, sbyte>.operator ~(sbyte value) => (sbyte)(~value);
        static sbyte IShiftOperators<sbyte, int, sbyte>.operator <<(sbyte value, int shiftAmount) => (sbyte)(value << (shiftAmount & 7));
        static sbyte IShiftOperators<sbyte, int, sbyte>.operator >>(sbyte value, int shiftAmount) => (sbyte)(value >> (shiftAmount & 7));
        static sbyte IShiftOperators<sbyte, int, sbyte>.operator >>>(sbyte value, int shiftAmount) => (sbyte)((byte)value >>> (shiftAmount & 7));
        static bool IEqualityOperators<sbyte, sbyte, bool>.operator ==(sbyte left, sbyte right) => left == right;
        static bool IEqualityOperators<sbyte, sbyte, bool>.operator !=(sbyte left, sbyte right) => left != right;
        static bool IComparisonOperators<sbyte, sbyte, bool>.operator <(sbyte left, sbyte right) => left < right;
        static bool IComparisonOperators<sbyte, sbyte, bool>.operator <=(sbyte left, sbyte right) => left <= right;
        static bool IComparisonOperators<sbyte, sbyte, bool>.operator >(sbyte left, sbyte right) => left > right;
        static bool IComparisonOperators<sbyte, sbyte, bool>.operator >=(sbyte left, sbyte right) => left >= right;
        static sbyte IIncrementOperators<sbyte>.operator ++(sbyte value) => ++value;
        static sbyte IIncrementOperators<sbyte>.operator checked ++(sbyte value) => checked(++value);
        static sbyte IDecrementOperators<sbyte>.operator --(sbyte value) => --value;
        static sbyte IDecrementOperators<sbyte>.operator checked --(sbyte value) => checked(--value);
        static sbyte IUnaryNegationOperators<sbyte, sbyte>.operator -(sbyte value) => (sbyte)(-value);
        static sbyte IUnaryNegationOperators<sbyte, sbyte>.operator checked -(sbyte value) => checked((sbyte)(-value));
        static sbyte IUnaryPlusOperators<sbyte, sbyte>.operator +(sbyte value) => value;
        static sbyte IAdditiveIdentity<sbyte, sbyte>.AdditiveIdentity => 0;
        static sbyte IMultiplicativeIdentity<sbyte, sbyte>.MultiplicativeIdentity => 1;
        static sbyte IMinMaxValue<sbyte>.MinValue => MinValue;
        static sbyte IMinMaxValue<sbyte>.MaxValue => MaxValue;
        static sbyte INumberBase<sbyte>.One => 1;
        static int INumberBase<sbyte>.Radix => 2;
        static sbyte INumberBase<sbyte>.Zero => 0;
        static bool INumberBase<sbyte>.IsCanonical(sbyte value) => true;
        static bool INumberBase<sbyte>.IsComplexNumber(sbyte value) => false;
        static bool INumberBase<sbyte>.IsFinite(sbyte value) => true;
        static bool INumberBase<sbyte>.IsImaginaryNumber(sbyte value) => false;
        static bool INumberBase<sbyte>.IsInfinity(sbyte value) => false;
        static bool INumberBase<sbyte>.IsInteger(sbyte value) => true;
        static bool INumberBase<sbyte>.IsNaN(sbyte value) => false;
        static bool INumberBase<sbyte>.IsNegativeInfinity(sbyte value) => false;
        static bool INumberBase<sbyte>.IsNormal(sbyte value) => value != 0;
        static bool INumberBase<sbyte>.IsPositiveInfinity(sbyte value) => false;
        static bool INumberBase<sbyte>.IsRealNumber(sbyte value) => true;
        static bool INumberBase<sbyte>.IsSubnormal(sbyte value) => false;
        static bool INumberBase<sbyte>.IsZero(sbyte value) => value == 0;
        static sbyte INumberBase<sbyte>.MaxMagnitudeNumber(sbyte x, sbyte y) => MaxMagnitude(x, y);
        static sbyte INumberBase<sbyte>.MinMagnitudeNumber(sbyte x, sbyte y) => MinMagnitude(x, y);
        static sbyte INumber<sbyte>.MaxNumber(sbyte x, sbyte y) => Max(x, y);
        static sbyte INumber<sbyte>.MinNumber(sbyte x, sbyte y) => Min(x, y);

        static bool INumberBase<sbyte>.TryConvertFromChecked<TOther>(TOther value, out sbyte result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Checked, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (sbyte)bits;
            return converted;
        }

        static bool INumberBase<sbyte>.TryConvertFromSaturating<TOther>(TOther value, out sbyte result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Saturating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (sbyte)bits;
            return converted;
        }

        static bool INumberBase<sbyte>.TryConvertFromTruncating<TOther>(TOther value, out sbyte result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Truncating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (sbyte)bits;
            return converted;
        }

        static bool INumberBase<sbyte>.TryConvertToChecked<TOther>(sbyte value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<sbyte>.TryConvertToSaturating<TOther>(sbyte value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<sbyte>.TryConvertToTruncating<TOther>(sbyte value, out TOther result)
        {
            result = default!;
            return false;
        }

        int IBinaryInteger<sbyte>.GetByteCount() => 1;

        int IBinaryInteger<sbyte>.GetShortestBitLength() => m_value >= 0 ? 8 - (int)LeadingZeroCount(m_value) : 8 + 1 - (int)LeadingZeroCount((sbyte)~m_value);

        static bool IBinaryInteger<sbyte>.TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out sbyte value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: true, isUnsigned, 1, true, out ulong bits);
            value = read ? (sbyte)bits : (sbyte)0;
            return read;
        }

        static bool IBinaryInteger<sbyte>.TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out sbyte value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: false, isUnsigned, 1, true, out ulong bits);
            value = read ? (sbyte)bits : (sbyte)0;
            return read;
        }

        bool IBinaryInteger<sbyte>.TryWriteBigEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)m_value, 1, bigEndian: true, destination, out bytesWritten);

        bool IBinaryInteger<sbyte>.TryWriteLittleEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)m_value, 1, bigEndian: false, destination, out bytesWritten);

        static sbyte ISignedNumber<sbyte>.NegativeOne => -1;
    }
    public readonly struct Byte
        : IComparable, ISpanFormattable, IComparable<byte>, IEquatable<byte>, IBinaryInteger<byte>, IMinMaxValue<byte>, IUnsignedNumber<byte>,
          IUtfChar<byte>
    {
        private readonly byte m_value;
        public const byte MaxValue = (byte)0xFF;
        public const byte MinValue = 0;

        public bool Equals(byte obj)
        {
            return m_value == obj;
        }
        public override int GetHashCode()
        {
            return m_value;
        }
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (!(obj is byte))
            {
                return false;
            }
            return m_value == ((byte)obj).m_value;
        }





        public override string ToString()
        {
            return System.Number.UInt32ToString((uint)m_value);
        }
    

        public int CompareTo(object? value)
        {
            if (value is null)
                return 1;
            if (value is byte other)
                return CompareTo(other);
            throw new ArgumentException(SR.Arg_MustBeByte);
        }

        public int CompareTo(byte value) => m_value < value ? -1 : m_value > value ? 1 : 0;

        public string ToString(string? format) => ToString(format, null);

        public string ToString(IFormatProvider? provider) => ToString(null, provider);

        public string ToString(string? format, IFormatProvider? provider) => Number.FormatUInt32(m_value, format, provider);

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
            => Number.TryCopyFormatted(ToString(format.IsEmpty ? null : new string(format), provider), destination, out charsWritten);

        public static byte Parse(string s) => Parse(s, NumberStyles.Integer, null);

        public static byte Parse(string s, NumberStyles style) => Parse(s, style, null);

        public static byte Parse(string s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static byte Parse(string s, NumberStyles style, IFormatProvider? provider)
        {
            if (s is null)
                throw new ArgumentNullException(nameof(s));
            return Parse(s.AsSpan(), style, provider);
        }

        public static byte Parse(ReadOnlySpan<char> s, NumberStyles style = NumberStyles.Integer, IFormatProvider? provider = null)
        {
            Number.ParseStatus status = Number.TryParseBinaryInteger(s, style, out byte result);
            if (status != Number.ParseStatus.OK)
                Number.ThrowParseException(status);
            return result;
        }

        public static byte Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static bool TryParse([NotNullWhen(true)] string? s, out byte result) => TryParse(s, NumberStyles.Integer, null, out result);

        public static bool TryParse(ReadOnlySpan<char> s, out byte result) => TryParse(s, NumberStyles.Integer, null, out result);

        public static bool TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, out byte result)
        {
            if (s is null)
            {
                result = 0;
                return false;
            }
            return TryParse(s.AsSpan(), style, provider, out result);
        }

        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out byte result)
            => Number.TryParseBinaryInteger(s, style, out result) == Number.ParseStatus.OK;

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out byte result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out byte result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static (byte Quotient, byte Remainder) DivRem(byte left, byte right)
        {
            byte quotient = (byte)(left / right);
            return (quotient, (byte)(left - quotient * right));
        }

        public static byte LeadingZeroCount(byte value) => (byte)(BitOperations.LeadingZeroCount((uint)(byte)value) - 24);

        public static byte PopCount(byte value) => (byte)BitOperations.PopCount((uint)(byte)value);

        public static byte RotateLeft(byte value, int rotateAmount) => (byte)(((byte)value << (rotateAmount & 7)) | ((byte)value >> ((8 - rotateAmount) & 7)));

        public static byte RotateRight(byte value, int rotateAmount) => (byte)(((byte)value >> (rotateAmount & 7)) | ((byte)value << ((8 - rotateAmount) & 7)));

        public static byte TrailingZeroCount(byte value) => (byte)(BitOperations.TrailingZeroCount((uint)(byte)value << 24) - 24);

        public static bool IsPow2(byte value) => BitOperations.IsPow2((uint)value);

        public static byte Log2(byte value)
        {
            return (byte)BitOperations.Log2((uint)(byte)value);
        }

        public static byte Clamp(byte value, byte min, byte max) => Math.Clamp(value, min, max);

        public static byte Max(byte x, byte y) => Math.Max(x, y);

        public static byte Min(byte x, byte y) => Math.Min(x, y);

        public static bool IsEvenInteger(byte value) => (value & 1) == 0;

        public static bool IsOddInteger(byte value) => (value & 1) != 0;

        public static byte CreateChecked<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Checked);

        public static byte CreateSaturating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Saturating);

        public static byte CreateTruncating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Truncating);

        private static byte Create<TOther>(TOther value, NumericConversion.Mode mode) where TOther : INumberBase<TOther>
        {
            if (NumericConversion.TryConvertToInteger(value, mode, (long)MinValue, (ulong)MaxValue, out ulong bits))
                return (byte)bits;
            byte result;
            bool converted = mode switch
            {
                NumericConversion.Mode.Checked => TOther.TryConvertToChecked(value, out result),
                NumericConversion.Mode.Saturating => TOther.TryConvertToSaturating(value, out result),
                _ => TOther.TryConvertToTruncating(value, out result),
            };
            if (!converted)
                ThrowHelper.ThrowNotSupportedException();
            return result;
        }

        static byte IAdditionOperators<byte, byte, byte>.operator +(byte left, byte right) => (byte)(left + right);
        static byte IAdditionOperators<byte, byte, byte>.operator checked +(byte left, byte right) => checked((byte)(left + right));
        static byte ISubtractionOperators<byte, byte, byte>.operator -(byte left, byte right) => (byte)(left - right);
        static byte ISubtractionOperators<byte, byte, byte>.operator checked -(byte left, byte right) => checked((byte)(left - right));
        static byte IMultiplyOperators<byte, byte, byte>.operator *(byte left, byte right) => (byte)(left * right);
        static byte IMultiplyOperators<byte, byte, byte>.operator checked *(byte left, byte right) => checked((byte)(left * right));
        static byte IDivisionOperators<byte, byte, byte>.operator /(byte left, byte right) => (byte)(left / right);
        static byte IModulusOperators<byte, byte, byte>.operator %(byte left, byte right) => (byte)(left % right);
        static byte IBitwiseOperators<byte, byte, byte>.operator &(byte left, byte right) => (byte)(left & right);
        static byte IBitwiseOperators<byte, byte, byte>.operator |(byte left, byte right) => (byte)(left | right);
        static byte IBitwiseOperators<byte, byte, byte>.operator ^(byte left, byte right) => (byte)(left ^ right);
        static byte IBitwiseOperators<byte, byte, byte>.operator ~(byte value) => (byte)(~value);
        static byte IShiftOperators<byte, int, byte>.operator <<(byte value, int shiftAmount) => (byte)(value << (shiftAmount & 7));
        static byte IShiftOperators<byte, int, byte>.operator >>(byte value, int shiftAmount) => (byte)(value >> (shiftAmount & 7));
        static byte IShiftOperators<byte, int, byte>.operator >>>(byte value, int shiftAmount) => (byte)((byte)value >>> (shiftAmount & 7));
        static bool IEqualityOperators<byte, byte, bool>.operator ==(byte left, byte right) => left == right;
        static bool IEqualityOperators<byte, byte, bool>.operator !=(byte left, byte right) => left != right;
        static bool IComparisonOperators<byte, byte, bool>.operator <(byte left, byte right) => left < right;
        static bool IComparisonOperators<byte, byte, bool>.operator <=(byte left, byte right) => left <= right;
        static bool IComparisonOperators<byte, byte, bool>.operator >(byte left, byte right) => left > right;
        static bool IComparisonOperators<byte, byte, bool>.operator >=(byte left, byte right) => left >= right;
        static byte IIncrementOperators<byte>.operator ++(byte value) => ++value;
        static byte IIncrementOperators<byte>.operator checked ++(byte value) => checked(++value);
        static byte IDecrementOperators<byte>.operator --(byte value) => --value;
        static byte IDecrementOperators<byte>.operator checked --(byte value) => checked(--value);
        static byte IUnaryNegationOperators<byte, byte>.operator -(byte value) => (byte)(0 - value);
        static byte IUnaryNegationOperators<byte, byte>.operator checked -(byte value) => checked((byte)(0 - value));
        static byte IUnaryPlusOperators<byte, byte>.operator +(byte value) => value;
        static byte IAdditiveIdentity<byte, byte>.AdditiveIdentity => 0;
        static byte IMultiplicativeIdentity<byte, byte>.MultiplicativeIdentity => 1;
        static byte IMinMaxValue<byte>.MinValue => MinValue;
        static byte IMinMaxValue<byte>.MaxValue => MaxValue;
        static byte INumberBase<byte>.One => 1;
        static int INumberBase<byte>.Radix => 2;
        static byte INumberBase<byte>.Zero => 0;
        static bool INumberBase<byte>.IsCanonical(byte value) => true;
        static bool INumberBase<byte>.IsComplexNumber(byte value) => false;
        static bool INumberBase<byte>.IsFinite(byte value) => true;
        static bool INumberBase<byte>.IsImaginaryNumber(byte value) => false;
        static bool INumberBase<byte>.IsInfinity(byte value) => false;
        static bool INumberBase<byte>.IsInteger(byte value) => true;
        static bool INumberBase<byte>.IsNaN(byte value) => false;
        static bool INumberBase<byte>.IsNegativeInfinity(byte value) => false;
        static bool INumberBase<byte>.IsNormal(byte value) => value != 0;
        static bool INumberBase<byte>.IsPositiveInfinity(byte value) => false;
        static bool INumberBase<byte>.IsRealNumber(byte value) => true;
        static bool INumberBase<byte>.IsSubnormal(byte value) => false;
        static bool INumberBase<byte>.IsZero(byte value) => value == 0;
        static byte INumberBase<byte>.MaxMagnitudeNumber(byte x, byte y) => Max(x, y);
        static byte INumberBase<byte>.MinMagnitudeNumber(byte x, byte y) => Min(x, y);
        static byte INumber<byte>.MaxNumber(byte x, byte y) => Max(x, y);
        static byte INumber<byte>.MinNumber(byte x, byte y) => Min(x, y);

        static bool INumberBase<byte>.TryConvertFromChecked<TOther>(TOther value, out byte result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Checked, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (byte)bits;
            return converted;
        }

        static bool INumberBase<byte>.TryConvertFromSaturating<TOther>(TOther value, out byte result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Saturating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (byte)bits;
            return converted;
        }

        static bool INumberBase<byte>.TryConvertFromTruncating<TOther>(TOther value, out byte result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Truncating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (byte)bits;
            return converted;
        }

        static bool INumberBase<byte>.TryConvertToChecked<TOther>(byte value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<byte>.TryConvertToSaturating<TOther>(byte value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<byte>.TryConvertToTruncating<TOther>(byte value, out TOther result)
        {
            result = default!;
            return false;
        }

        int IBinaryInteger<byte>.GetByteCount() => 1;

        int IBinaryInteger<byte>.GetShortestBitLength() => 8 - (int)LeadingZeroCount(m_value);

        static bool IBinaryInteger<byte>.TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out byte value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: true, isUnsigned, 1, false, out ulong bits);
            value = read ? (byte)bits : (byte)0;
            return read;
        }

        static bool IBinaryInteger<byte>.TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out byte value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: false, isUnsigned, 1, false, out ulong bits);
            value = read ? (byte)bits : (byte)0;
            return read;
        }

        bool IBinaryInteger<byte>.TryWriteBigEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)m_value, 1, bigEndian: true, destination, out bytesWritten);

        bool IBinaryInteger<byte>.TryWriteLittleEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)m_value, 1, bigEndian: false, destination, out bytesWritten);

        static byte INumberBase<byte>.Abs(byte value) => value;
        static byte INumber<byte>.CopySign(byte value, byte sign) => value;
        static int INumber<byte>.Sign(byte value) => value == 0 ? 0 : 1;
        static bool INumberBase<byte>.IsNegative(byte value) => false;
        static bool INumberBase<byte>.IsPositive(byte value) => true;
        static byte INumberBase<byte>.MaxMagnitude(byte x, byte y) => Max(x, y);
        static byte INumberBase<byte>.MinMagnitude(byte x, byte y) => Min(x, y);

        static byte IUtfChar<byte>.CastFrom(byte value) => value;
        static byte IUtfChar<byte>.CastFrom(char value) => (byte)value;
        static byte IUtfChar<byte>.CastFrom(int value) => (byte)value;
        static byte IUtfChar<byte>.CastFrom(uint value) => (byte)value;
        static byte IUtfChar<byte>.CastFrom(ulong value) => (byte)value;
    }
    public readonly struct Int16
        : IComparable, ISpanFormattable, IComparable<short>, IEquatable<short>, IBinaryInteger<short>, IMinMaxValue<short>, ISignedNumber<short>
    {
        private readonly short m_value;
        public const short MaxValue = (short)0x7FFF;
        public const short MinValue = unchecked((short)0x8000);

        public bool Equals(short obj)
        {
            return m_value == obj;
        }
        public override int GetHashCode()
        {
            return m_value;
        }
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (!(obj is short))
            {
                return false;
            }
            return m_value == ((short)obj).m_value;
        }





        public override string ToString()
        {
            return System.Number.FormatInt32((int)m_value, 0, null, null);
        }
        public string ToString(string format)
        {
            return System.Number.FormatInt32((int)m_value, 0xFFFF, format, null);
        }
    

        public int CompareTo(object? value)
        {
            if (value is null)
                return 1;
            if (value is short other)
                return CompareTo(other);
            throw new ArgumentException(SR.Arg_MustBeInt16);
        }

        public int CompareTo(short value) => m_value < value ? -1 : m_value > value ? 1 : 0;

        public string ToString(IFormatProvider? provider) => ToString(null, provider);

        public string ToString(string? format, IFormatProvider? provider) => Number.FormatInt32(m_value, 0xFFFF, format, provider);

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
            => Number.TryCopyFormatted(ToString(format.IsEmpty ? null : new string(format), provider), destination, out charsWritten);

        public static short Parse(string s) => Parse(s, NumberStyles.Integer, null);

        public static short Parse(string s, NumberStyles style) => Parse(s, style, null);

        public static short Parse(string s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static short Parse(string s, NumberStyles style, IFormatProvider? provider)
        {
            if (s is null)
                throw new ArgumentNullException(nameof(s));
            return Parse(s.AsSpan(), style, provider);
        }

        public static short Parse(ReadOnlySpan<char> s, NumberStyles style = NumberStyles.Integer, IFormatProvider? provider = null)
        {
            Number.ParseStatus status = Number.TryParseBinaryInteger(s, style, out short result);
            if (status != Number.ParseStatus.OK)
                Number.ThrowParseException(status);
            return result;
        }

        public static short Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static bool TryParse([NotNullWhen(true)] string? s, out short result) => TryParse(s, NumberStyles.Integer, null, out result);

        public static bool TryParse(ReadOnlySpan<char> s, out short result) => TryParse(s, NumberStyles.Integer, null, out result);

        public static bool TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, out short result)
        {
            if (s is null)
            {
                result = 0;
                return false;
            }
            return TryParse(s.AsSpan(), style, provider, out result);
        }

        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out short result)
            => Number.TryParseBinaryInteger(s, style, out result) == Number.ParseStatus.OK;

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out short result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out short result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static (short Quotient, short Remainder) DivRem(short left, short right)
        {
            short quotient = (short)(left / right);
            return (quotient, (short)(left - quotient * right));
        }

        public static short LeadingZeroCount(short value) => (short)(BitOperations.LeadingZeroCount((uint)(ushort)value) - 16);

        public static short PopCount(short value) => (short)BitOperations.PopCount((uint)(ushort)value);

        public static short RotateLeft(short value, int rotateAmount) => (short)(((ushort)value << (rotateAmount & 15)) | ((ushort)value >> ((16 - rotateAmount) & 15)));

        public static short RotateRight(short value, int rotateAmount) => (short)(((ushort)value >> (rotateAmount & 15)) | ((ushort)value << ((16 - rotateAmount) & 15)));

        public static short TrailingZeroCount(short value) => (short)(BitOperations.TrailingZeroCount((uint)(ushort)value << 16) - 16);

        public static bool IsPow2(short value) => BitOperations.IsPow2((int)value);

        public static short Log2(short value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), SR.ArgumentOutOfRange_NeedNonNegNum);
            return (short)BitOperations.Log2((uint)(ushort)value);
        }

        public static short Clamp(short value, short min, short max) => Math.Clamp(value, min, max);

        public static short Max(short x, short y) => Math.Max(x, y);

        public static short Min(short x, short y) => Math.Min(x, y);

        public static bool IsEvenInteger(short value) => (value & 1) == 0;

        public static bool IsOddInteger(short value) => (value & 1) != 0;

        public static short CreateChecked<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Checked);

        public static short CreateSaturating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Saturating);

        public static short CreateTruncating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Truncating);

        private static short Create<TOther>(TOther value, NumericConversion.Mode mode) where TOther : INumberBase<TOther>
        {
            if (NumericConversion.TryConvertToInteger(value, mode, (long)MinValue, (ulong)MaxValue, out ulong bits))
                return (short)bits;
            short result;
            bool converted = mode switch
            {
                NumericConversion.Mode.Checked => TOther.TryConvertToChecked(value, out result),
                NumericConversion.Mode.Saturating => TOther.TryConvertToSaturating(value, out result),
                _ => TOther.TryConvertToTruncating(value, out result),
            };
            if (!converted)
                ThrowHelper.ThrowNotSupportedException();
            return result;
        }

        public static short Abs(short value) => Math.Abs(value);

        public static short CopySign(short value, short sign)
        {
            short absValue = value;
            if (absValue < 0)
                absValue = (short)(-absValue);
            if (sign >= 0)
            {
                if (absValue < 0)
                    Math.ThrowNegateTwosCompOverflow();
                return absValue;
            }
            return (short)(-absValue);
        }

        public static int Sign(short value) => value < 0 ? -1 : value > 0 ? 1 : 0;

        public static bool IsNegative(short value) => value < 0;

        public static bool IsPositive(short value) => value >= 0;

        public static short MaxMagnitude(short x, short y)
        {
            short absX = x;
            if (absX < 0)
            {
                absX = (short)(-absX);
                if (absX < 0)
                    return x;
            }
            short absY = y;
            if (absY < 0)
            {
                absY = (short)(-absY);
                if (absY < 0)
                    return y;
            }
            if (absX > absY)
                return x;
            if (absX == absY)
                return IsNegative(x) ? y : x;
            return y;
        }

        public static short MinMagnitude(short x, short y)
        {
            short absX = x;
            if (absX < 0)
            {
                absX = (short)(-absX);
                if (absX < 0)
                    return y;
            }
            short absY = y;
            if (absY < 0)
            {
                absY = (short)(-absY);
                if (absY < 0)
                    return x;
            }
            if (absX < absY)
                return x;
            if (absX == absY)
                return IsNegative(x) ? x : y;
            return y;
        }

        static short IAdditionOperators<short, short, short>.operator +(short left, short right) => (short)(left + right);
        static short IAdditionOperators<short, short, short>.operator checked +(short left, short right) => checked((short)(left + right));
        static short ISubtractionOperators<short, short, short>.operator -(short left, short right) => (short)(left - right);
        static short ISubtractionOperators<short, short, short>.operator checked -(short left, short right) => checked((short)(left - right));
        static short IMultiplyOperators<short, short, short>.operator *(short left, short right) => (short)(left * right);
        static short IMultiplyOperators<short, short, short>.operator checked *(short left, short right) => checked((short)(left * right));
        static short IDivisionOperators<short, short, short>.operator /(short left, short right) => (short)(left / right);
        static short IModulusOperators<short, short, short>.operator %(short left, short right) => (short)(left % right);
        static short IBitwiseOperators<short, short, short>.operator &(short left, short right) => (short)(left & right);
        static short IBitwiseOperators<short, short, short>.operator |(short left, short right) => (short)(left | right);
        static short IBitwiseOperators<short, short, short>.operator ^(short left, short right) => (short)(left ^ right);
        static short IBitwiseOperators<short, short, short>.operator ~(short value) => (short)(~value);
        static short IShiftOperators<short, int, short>.operator <<(short value, int shiftAmount) => (short)(value << (shiftAmount & 15));
        static short IShiftOperators<short, int, short>.operator >>(short value, int shiftAmount) => (short)(value >> (shiftAmount & 15));
        static short IShiftOperators<short, int, short>.operator >>>(short value, int shiftAmount) => (short)((ushort)value >>> (shiftAmount & 15));
        static bool IEqualityOperators<short, short, bool>.operator ==(short left, short right) => left == right;
        static bool IEqualityOperators<short, short, bool>.operator !=(short left, short right) => left != right;
        static bool IComparisonOperators<short, short, bool>.operator <(short left, short right) => left < right;
        static bool IComparisonOperators<short, short, bool>.operator <=(short left, short right) => left <= right;
        static bool IComparisonOperators<short, short, bool>.operator >(short left, short right) => left > right;
        static bool IComparisonOperators<short, short, bool>.operator >=(short left, short right) => left >= right;
        static short IIncrementOperators<short>.operator ++(short value) => ++value;
        static short IIncrementOperators<short>.operator checked ++(short value) => checked(++value);
        static short IDecrementOperators<short>.operator --(short value) => --value;
        static short IDecrementOperators<short>.operator checked --(short value) => checked(--value);
        static short IUnaryNegationOperators<short, short>.operator -(short value) => (short)(-value);
        static short IUnaryNegationOperators<short, short>.operator checked -(short value) => checked((short)(-value));
        static short IUnaryPlusOperators<short, short>.operator +(short value) => value;
        static short IAdditiveIdentity<short, short>.AdditiveIdentity => 0;
        static short IMultiplicativeIdentity<short, short>.MultiplicativeIdentity => 1;
        static short IMinMaxValue<short>.MinValue => MinValue;
        static short IMinMaxValue<short>.MaxValue => MaxValue;
        static short INumberBase<short>.One => 1;
        static int INumberBase<short>.Radix => 2;
        static short INumberBase<short>.Zero => 0;
        static bool INumberBase<short>.IsCanonical(short value) => true;
        static bool INumberBase<short>.IsComplexNumber(short value) => false;
        static bool INumberBase<short>.IsFinite(short value) => true;
        static bool INumberBase<short>.IsImaginaryNumber(short value) => false;
        static bool INumberBase<short>.IsInfinity(short value) => false;
        static bool INumberBase<short>.IsInteger(short value) => true;
        static bool INumberBase<short>.IsNaN(short value) => false;
        static bool INumberBase<short>.IsNegativeInfinity(short value) => false;
        static bool INumberBase<short>.IsNormal(short value) => value != 0;
        static bool INumberBase<short>.IsPositiveInfinity(short value) => false;
        static bool INumberBase<short>.IsRealNumber(short value) => true;
        static bool INumberBase<short>.IsSubnormal(short value) => false;
        static bool INumberBase<short>.IsZero(short value) => value == 0;
        static short INumberBase<short>.MaxMagnitudeNumber(short x, short y) => MaxMagnitude(x, y);
        static short INumberBase<short>.MinMagnitudeNumber(short x, short y) => MinMagnitude(x, y);
        static short INumber<short>.MaxNumber(short x, short y) => Max(x, y);
        static short INumber<short>.MinNumber(short x, short y) => Min(x, y);

        static bool INumberBase<short>.TryConvertFromChecked<TOther>(TOther value, out short result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Checked, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (short)bits;
            return converted;
        }

        static bool INumberBase<short>.TryConvertFromSaturating<TOther>(TOther value, out short result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Saturating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (short)bits;
            return converted;
        }

        static bool INumberBase<short>.TryConvertFromTruncating<TOther>(TOther value, out short result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Truncating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (short)bits;
            return converted;
        }

        static bool INumberBase<short>.TryConvertToChecked<TOther>(short value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<short>.TryConvertToSaturating<TOther>(short value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<short>.TryConvertToTruncating<TOther>(short value, out TOther result)
        {
            result = default!;
            return false;
        }

        int IBinaryInteger<short>.GetByteCount() => 2;

        int IBinaryInteger<short>.GetShortestBitLength() => m_value >= 0 ? 16 - (int)LeadingZeroCount(m_value) : 16 + 1 - (int)LeadingZeroCount((short)~m_value);

        static bool IBinaryInteger<short>.TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out short value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: true, isUnsigned, 2, true, out ulong bits);
            value = read ? (short)bits : (short)0;
            return read;
        }

        static bool IBinaryInteger<short>.TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out short value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: false, isUnsigned, 2, true, out ulong bits);
            value = read ? (short)bits : (short)0;
            return read;
        }

        bool IBinaryInteger<short>.TryWriteBigEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)m_value, 2, bigEndian: true, destination, out bytesWritten);

        bool IBinaryInteger<short>.TryWriteLittleEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)m_value, 2, bigEndian: false, destination, out bytesWritten);

        static short ISignedNumber<short>.NegativeOne => -1;
    }
    public readonly struct UInt16
        : IComparable, ISpanFormattable, IComparable<ushort>, IEquatable<ushort>, IBinaryInteger<ushort>, IMinMaxValue<ushort>, IUnsignedNumber<ushort>
    {
        private readonly ushort m_value;
        public const ushort MaxValue = (ushort)0xFFFF;
        public const ushort MinValue = 0;

        public bool Equals(ushort obj)
        {
            return m_value == obj;
        }
        public override int GetHashCode()
        {
            return (int)m_value;
        }
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (!(obj is ushort))
            {
                return false;
            }
            return m_value == ((ushort)obj).m_value;
        }





        public override string ToString()
        {
            return System.Number.UInt32ToString((uint)m_value);
        }
        public string ToString(string format)
        {
            return System.Number.FormatInt32(m_value, 0, format, null);
        }
    

        public int CompareTo(object? value)
        {
            if (value is null)
                return 1;
            if (value is ushort other)
                return CompareTo(other);
            throw new ArgumentException(SR.Arg_MustBeUInt16);
        }

        public int CompareTo(ushort value) => m_value < value ? -1 : m_value > value ? 1 : 0;

        public string ToString(IFormatProvider? provider) => ToString(null, provider);

        public string ToString(string? format, IFormatProvider? provider) => Number.FormatUInt32(m_value, format, provider);

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
            => Number.TryCopyFormatted(ToString(format.IsEmpty ? null : new string(format), provider), destination, out charsWritten);

        public static ushort Parse(string s) => Parse(s, NumberStyles.Integer, null);

        public static ushort Parse(string s, NumberStyles style) => Parse(s, style, null);

        public static ushort Parse(string s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static ushort Parse(string s, NumberStyles style, IFormatProvider? provider)
        {
            if (s is null)
                throw new ArgumentNullException(nameof(s));
            return Parse(s.AsSpan(), style, provider);
        }

        public static ushort Parse(ReadOnlySpan<char> s, NumberStyles style = NumberStyles.Integer, IFormatProvider? provider = null)
        {
            Number.ParseStatus status = Number.TryParseBinaryInteger(s, style, out ushort result);
            if (status != Number.ParseStatus.OK)
                Number.ThrowParseException(status);
            return result;
        }

        public static ushort Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static bool TryParse([NotNullWhen(true)] string? s, out ushort result) => TryParse(s, NumberStyles.Integer, null, out result);

        public static bool TryParse(ReadOnlySpan<char> s, out ushort result) => TryParse(s, NumberStyles.Integer, null, out result);

        public static bool TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, out ushort result)
        {
            if (s is null)
            {
                result = 0;
                return false;
            }
            return TryParse(s.AsSpan(), style, provider, out result);
        }

        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out ushort result)
            => Number.TryParseBinaryInteger(s, style, out result) == Number.ParseStatus.OK;

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ushort result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ushort result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static (ushort Quotient, ushort Remainder) DivRem(ushort left, ushort right)
        {
            ushort quotient = (ushort)(left / right);
            return (quotient, (ushort)(left - quotient * right));
        }

        public static ushort LeadingZeroCount(ushort value) => (ushort)(BitOperations.LeadingZeroCount((uint)(ushort)value) - 16);

        public static ushort PopCount(ushort value) => (ushort)BitOperations.PopCount((uint)(ushort)value);

        public static ushort RotateLeft(ushort value, int rotateAmount) => (ushort)(((ushort)value << (rotateAmount & 15)) | ((ushort)value >> ((16 - rotateAmount) & 15)));

        public static ushort RotateRight(ushort value, int rotateAmount) => (ushort)(((ushort)value >> (rotateAmount & 15)) | ((ushort)value << ((16 - rotateAmount) & 15)));

        public static ushort TrailingZeroCount(ushort value) => (ushort)(BitOperations.TrailingZeroCount((uint)(ushort)value << 16) - 16);

        public static bool IsPow2(ushort value) => BitOperations.IsPow2((uint)value);

        public static ushort Log2(ushort value)
        {
            return (ushort)BitOperations.Log2((uint)(ushort)value);
        }

        public static ushort Clamp(ushort value, ushort min, ushort max) => Math.Clamp(value, min, max);

        public static ushort Max(ushort x, ushort y) => Math.Max(x, y);

        public static ushort Min(ushort x, ushort y) => Math.Min(x, y);

        public static bool IsEvenInteger(ushort value) => (value & 1) == 0;

        public static bool IsOddInteger(ushort value) => (value & 1) != 0;

        public static ushort CreateChecked<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Checked);

        public static ushort CreateSaturating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Saturating);

        public static ushort CreateTruncating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Truncating);

        private static ushort Create<TOther>(TOther value, NumericConversion.Mode mode) where TOther : INumberBase<TOther>
        {
            if (NumericConversion.TryConvertToInteger(value, mode, (long)MinValue, (ulong)MaxValue, out ulong bits))
                return (ushort)bits;
            ushort result;
            bool converted = mode switch
            {
                NumericConversion.Mode.Checked => TOther.TryConvertToChecked(value, out result),
                NumericConversion.Mode.Saturating => TOther.TryConvertToSaturating(value, out result),
                _ => TOther.TryConvertToTruncating(value, out result),
            };
            if (!converted)
                ThrowHelper.ThrowNotSupportedException();
            return result;
        }

        static ushort IAdditionOperators<ushort, ushort, ushort>.operator +(ushort left, ushort right) => (ushort)(left + right);
        static ushort IAdditionOperators<ushort, ushort, ushort>.operator checked +(ushort left, ushort right) => checked((ushort)(left + right));
        static ushort ISubtractionOperators<ushort, ushort, ushort>.operator -(ushort left, ushort right) => (ushort)(left - right);
        static ushort ISubtractionOperators<ushort, ushort, ushort>.operator checked -(ushort left, ushort right) => checked((ushort)(left - right));
        static ushort IMultiplyOperators<ushort, ushort, ushort>.operator *(ushort left, ushort right) => (ushort)(left * right);
        static ushort IMultiplyOperators<ushort, ushort, ushort>.operator checked *(ushort left, ushort right) => checked((ushort)(left * right));
        static ushort IDivisionOperators<ushort, ushort, ushort>.operator /(ushort left, ushort right) => (ushort)(left / right);
        static ushort IModulusOperators<ushort, ushort, ushort>.operator %(ushort left, ushort right) => (ushort)(left % right);
        static ushort IBitwiseOperators<ushort, ushort, ushort>.operator &(ushort left, ushort right) => (ushort)(left & right);
        static ushort IBitwiseOperators<ushort, ushort, ushort>.operator |(ushort left, ushort right) => (ushort)(left | right);
        static ushort IBitwiseOperators<ushort, ushort, ushort>.operator ^(ushort left, ushort right) => (ushort)(left ^ right);
        static ushort IBitwiseOperators<ushort, ushort, ushort>.operator ~(ushort value) => (ushort)(~value);
        static ushort IShiftOperators<ushort, int, ushort>.operator <<(ushort value, int shiftAmount) => (ushort)(value << (shiftAmount & 15));
        static ushort IShiftOperators<ushort, int, ushort>.operator >>(ushort value, int shiftAmount) => (ushort)(value >> (shiftAmount & 15));
        static ushort IShiftOperators<ushort, int, ushort>.operator >>>(ushort value, int shiftAmount) => (ushort)((ushort)value >>> (shiftAmount & 15));
        static bool IEqualityOperators<ushort, ushort, bool>.operator ==(ushort left, ushort right) => left == right;
        static bool IEqualityOperators<ushort, ushort, bool>.operator !=(ushort left, ushort right) => left != right;
        static bool IComparisonOperators<ushort, ushort, bool>.operator <(ushort left, ushort right) => left < right;
        static bool IComparisonOperators<ushort, ushort, bool>.operator <=(ushort left, ushort right) => left <= right;
        static bool IComparisonOperators<ushort, ushort, bool>.operator >(ushort left, ushort right) => left > right;
        static bool IComparisonOperators<ushort, ushort, bool>.operator >=(ushort left, ushort right) => left >= right;
        static ushort IIncrementOperators<ushort>.operator ++(ushort value) => ++value;
        static ushort IIncrementOperators<ushort>.operator checked ++(ushort value) => checked(++value);
        static ushort IDecrementOperators<ushort>.operator --(ushort value) => --value;
        static ushort IDecrementOperators<ushort>.operator checked --(ushort value) => checked(--value);
        static ushort IUnaryNegationOperators<ushort, ushort>.operator -(ushort value) => (ushort)(0 - value);
        static ushort IUnaryNegationOperators<ushort, ushort>.operator checked -(ushort value) => checked((ushort)(0 - value));
        static ushort IUnaryPlusOperators<ushort, ushort>.operator +(ushort value) => value;
        static ushort IAdditiveIdentity<ushort, ushort>.AdditiveIdentity => 0;
        static ushort IMultiplicativeIdentity<ushort, ushort>.MultiplicativeIdentity => 1;
        static ushort IMinMaxValue<ushort>.MinValue => MinValue;
        static ushort IMinMaxValue<ushort>.MaxValue => MaxValue;
        static ushort INumberBase<ushort>.One => 1;
        static int INumberBase<ushort>.Radix => 2;
        static ushort INumberBase<ushort>.Zero => 0;
        static bool INumberBase<ushort>.IsCanonical(ushort value) => true;
        static bool INumberBase<ushort>.IsComplexNumber(ushort value) => false;
        static bool INumberBase<ushort>.IsFinite(ushort value) => true;
        static bool INumberBase<ushort>.IsImaginaryNumber(ushort value) => false;
        static bool INumberBase<ushort>.IsInfinity(ushort value) => false;
        static bool INumberBase<ushort>.IsInteger(ushort value) => true;
        static bool INumberBase<ushort>.IsNaN(ushort value) => false;
        static bool INumberBase<ushort>.IsNegativeInfinity(ushort value) => false;
        static bool INumberBase<ushort>.IsNormal(ushort value) => value != 0;
        static bool INumberBase<ushort>.IsPositiveInfinity(ushort value) => false;
        static bool INumberBase<ushort>.IsRealNumber(ushort value) => true;
        static bool INumberBase<ushort>.IsSubnormal(ushort value) => false;
        static bool INumberBase<ushort>.IsZero(ushort value) => value == 0;
        static ushort INumberBase<ushort>.MaxMagnitudeNumber(ushort x, ushort y) => Max(x, y);
        static ushort INumberBase<ushort>.MinMagnitudeNumber(ushort x, ushort y) => Min(x, y);
        static ushort INumber<ushort>.MaxNumber(ushort x, ushort y) => Max(x, y);
        static ushort INumber<ushort>.MinNumber(ushort x, ushort y) => Min(x, y);

        static bool INumberBase<ushort>.TryConvertFromChecked<TOther>(TOther value, out ushort result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Checked, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (ushort)bits;
            return converted;
        }

        static bool INumberBase<ushort>.TryConvertFromSaturating<TOther>(TOther value, out ushort result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Saturating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (ushort)bits;
            return converted;
        }

        static bool INumberBase<ushort>.TryConvertFromTruncating<TOther>(TOther value, out ushort result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Truncating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (ushort)bits;
            return converted;
        }

        static bool INumberBase<ushort>.TryConvertToChecked<TOther>(ushort value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<ushort>.TryConvertToSaturating<TOther>(ushort value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<ushort>.TryConvertToTruncating<TOther>(ushort value, out TOther result)
        {
            result = default!;
            return false;
        }

        int IBinaryInteger<ushort>.GetByteCount() => 2;

        int IBinaryInteger<ushort>.GetShortestBitLength() => 16 - (int)LeadingZeroCount(m_value);

        static bool IBinaryInteger<ushort>.TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out ushort value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: true, isUnsigned, 2, false, out ulong bits);
            value = read ? (ushort)bits : (ushort)0;
            return read;
        }

        static bool IBinaryInteger<ushort>.TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out ushort value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: false, isUnsigned, 2, false, out ulong bits);
            value = read ? (ushort)bits : (ushort)0;
            return read;
        }

        bool IBinaryInteger<ushort>.TryWriteBigEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)m_value, 2, bigEndian: true, destination, out bytesWritten);

        bool IBinaryInteger<ushort>.TryWriteLittleEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)m_value, 2, bigEndian: false, destination, out bytesWritten);

        static ushort INumberBase<ushort>.Abs(ushort value) => value;
        static ushort INumber<ushort>.CopySign(ushort value, ushort sign) => value;
        static int INumber<ushort>.Sign(ushort value) => value == 0 ? 0 : 1;
        static bool INumberBase<ushort>.IsNegative(ushort value) => false;
        static bool INumberBase<ushort>.IsPositive(ushort value) => true;
        static ushort INumberBase<ushort>.MaxMagnitude(ushort x, ushort y) => Max(x, y);
        static ushort INumberBase<ushort>.MinMagnitude(ushort x, ushort y) => Min(x, y);
    }
    public readonly struct Int32
        : IComparable, ISpanFormattable, IComparable<int>, IEquatable<int>, IBinaryInteger<int>, IMinMaxValue<int>, ISignedNumber<int>
    {
        private readonly int m_value; // Do not rename

        public const int MaxValue = 0x7fffffff;
        public const int MinValue = unchecked((int)0x80000000);

        /// <summary>Represents the additive identity (0).</summary>
        private const int AdditiveIdentity = 0;

        /// <summary>Represents the multiplicative identity (1).</summary>
        private const int MultiplicativeIdentity = 1;

        /// <summary>Represents the number one (1).</summary>
        private const int One = 1;

        /// <summary>Represents the number zero (0).</summary>
        private const int Zero = 0;

        /// <summary>Represents the number negative one (-1).</summary>
        private const int NegativeOne = -1;

        /// <summary>Produces the full product of two 32-bit numbers.</summary>
        /// <param name="left">The first number to multiply.</param>
        /// <param name="right">The second number to multiply.</param>
        /// <returns>The number containing the product of the specified numbers.</returns>
        public static long BigMul(int left, int right) => Math.BigMul(left, right);



        public bool Equals(int obj)
        {
            return m_value == obj;
        }
        public override int GetHashCode()
        {
            return m_value;
        }
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (!(obj is int))
            {
                return false;
            }
            return m_value == ((int)obj).m_value;
        }





        public override string ToString()
        {
            return System.Number.FormatInt32(m_value, 0, null, null);
        }
        public string ToString(string? format)
        {
            return System.Number.FormatInt32(m_value, 0, format, null);
        }
        public string ToString(IFormatProvider? provider)
        {
            return System.Number.FormatInt32(m_value, 0, null, provider);
        }
    

        public int CompareTo(object? value)
        {
            if (value is null)
                return 1;
            if (value is int other)
                return CompareTo(other);
            throw new ArgumentException(SR.Arg_MustBeInt32);
        }

        public int CompareTo(int value) => m_value < value ? -1 : m_value > value ? 1 : 0;

        public string ToString(string? format, IFormatProvider? provider) => Number.FormatInt32(m_value, 0, format, provider);

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
            => Number.TryCopyFormatted(ToString(format.IsEmpty ? null : new string(format), provider), destination, out charsWritten);

        public static int Parse(string s) => Parse(s, NumberStyles.Integer, null);

        public static int Parse(string s, NumberStyles style) => Parse(s, style, null);

        public static int Parse(string s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static int Parse(string s, NumberStyles style, IFormatProvider? provider)
        {
            if (s is null)
                throw new ArgumentNullException(nameof(s));
            return Parse(s.AsSpan(), style, provider);
        }

        public static int Parse(ReadOnlySpan<char> s, NumberStyles style = NumberStyles.Integer, IFormatProvider? provider = null)
        {
            Number.ParseStatus status = Number.TryParseBinaryInteger(s, style, out int result);
            if (status != Number.ParseStatus.OK)
                Number.ThrowParseException(status);
            return result;
        }

        public static int Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static bool TryParse([NotNullWhen(true)] string? s, out int result) => TryParse(s, NumberStyles.Integer, null, out result);

        public static bool TryParse(ReadOnlySpan<char> s, out int result) => TryParse(s, NumberStyles.Integer, null, out result);

        public static bool TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, out int result)
        {
            if (s is null)
            {
                result = 0;
                return false;
            }
            return TryParse(s.AsSpan(), style, provider, out result);
        }

        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out int result)
            => Number.TryParseBinaryInteger(s, style, out result) == Number.ParseStatus.OK;

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out int result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out int result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static (int Quotient, int Remainder) DivRem(int left, int right)
        {
            int quotient = (int)(left / right);
            return (quotient, (int)(left - quotient * right));
        }

        public static int LeadingZeroCount(int value) => (int)BitOperations.LeadingZeroCount((uint)(uint)value);

        public static int PopCount(int value) => (int)BitOperations.PopCount((uint)(uint)value);

        public static int RotateLeft(int value, int rotateAmount) => (int)BitOperations.RotateLeft((uint)(uint)value, rotateAmount);

        public static int RotateRight(int value, int rotateAmount) => (int)BitOperations.RotateRight((uint)(uint)value, rotateAmount);

        public static int TrailingZeroCount(int value) => (int)BitOperations.TrailingZeroCount((uint)(uint)value);

        public static bool IsPow2(int value) => BitOperations.IsPow2(value);

        public static int Log2(int value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), SR.ArgumentOutOfRange_NeedNonNegNum);
            return (int)BitOperations.Log2((uint)(uint)value);
        }

        public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);

        public static int Max(int x, int y) => Math.Max(x, y);

        public static int Min(int x, int y) => Math.Min(x, y);

        public static bool IsEvenInteger(int value) => (value & 1) == 0;

        public static bool IsOddInteger(int value) => (value & 1) != 0;

        public static int CreateChecked<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Checked);

        public static int CreateSaturating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Saturating);

        public static int CreateTruncating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Truncating);

        private static int Create<TOther>(TOther value, NumericConversion.Mode mode) where TOther : INumberBase<TOther>
        {
            if (NumericConversion.TryConvertToInteger(value, mode, (long)MinValue, (ulong)MaxValue, out ulong bits))
                return (int)bits;
            int result;
            bool converted = mode switch
            {
                NumericConversion.Mode.Checked => TOther.TryConvertToChecked(value, out result),
                NumericConversion.Mode.Saturating => TOther.TryConvertToSaturating(value, out result),
                _ => TOther.TryConvertToTruncating(value, out result),
            };
            if (!converted)
                ThrowHelper.ThrowNotSupportedException();
            return result;
        }

        public static int Abs(int value) => Math.Abs(value);

        public static int CopySign(int value, int sign)
        {
            int absValue = value;
            if (absValue < 0)
                absValue = (int)(-absValue);
            if (sign >= 0)
            {
                if (absValue < 0)
                    Math.ThrowNegateTwosCompOverflow();
                return absValue;
            }
            return (int)(-absValue);
        }

        public static int Sign(int value) => value < 0 ? -1 : value > 0 ? 1 : 0;

        public static bool IsNegative(int value) => value < 0;

        public static bool IsPositive(int value) => value >= 0;

        public static int MaxMagnitude(int x, int y)
        {
            int absX = x;
            if (absX < 0)
            {
                absX = (int)(-absX);
                if (absX < 0)
                    return x;
            }
            int absY = y;
            if (absY < 0)
            {
                absY = (int)(-absY);
                if (absY < 0)
                    return y;
            }
            if (absX > absY)
                return x;
            if (absX == absY)
                return IsNegative(x) ? y : x;
            return y;
        }

        public static int MinMagnitude(int x, int y)
        {
            int absX = x;
            if (absX < 0)
            {
                absX = (int)(-absX);
                if (absX < 0)
                    return y;
            }
            int absY = y;
            if (absY < 0)
            {
                absY = (int)(-absY);
                if (absY < 0)
                    return x;
            }
            if (absX < absY)
                return x;
            if (absX == absY)
                return IsNegative(x) ? x : y;
            return y;
        }

        static int IAdditionOperators<int, int, int>.operator +(int left, int right) => (int)(left + right);
        static int IAdditionOperators<int, int, int>.operator checked +(int left, int right) => checked((int)(left + right));
        static int ISubtractionOperators<int, int, int>.operator -(int left, int right) => (int)(left - right);
        static int ISubtractionOperators<int, int, int>.operator checked -(int left, int right) => checked((int)(left - right));
        static int IMultiplyOperators<int, int, int>.operator *(int left, int right) => (int)(left * right);
        static int IMultiplyOperators<int, int, int>.operator checked *(int left, int right) => checked((int)(left * right));
        static int IDivisionOperators<int, int, int>.operator /(int left, int right) => (int)(left / right);
        static int IModulusOperators<int, int, int>.operator %(int left, int right) => (int)(left % right);
        static int IBitwiseOperators<int, int, int>.operator &(int left, int right) => (int)(left & right);
        static int IBitwiseOperators<int, int, int>.operator |(int left, int right) => (int)(left | right);
        static int IBitwiseOperators<int, int, int>.operator ^(int left, int right) => (int)(left ^ right);
        static int IBitwiseOperators<int, int, int>.operator ~(int value) => (int)(~value);
        static int IShiftOperators<int, int, int>.operator <<(int value, int shiftAmount) => (int)(value << shiftAmount);
        static int IShiftOperators<int, int, int>.operator >>(int value, int shiftAmount) => (int)(value >> shiftAmount);
        static int IShiftOperators<int, int, int>.operator >>>(int value, int shiftAmount) => (int)((uint)value >>> shiftAmount);
        static bool IEqualityOperators<int, int, bool>.operator ==(int left, int right) => left == right;
        static bool IEqualityOperators<int, int, bool>.operator !=(int left, int right) => left != right;
        static bool IComparisonOperators<int, int, bool>.operator <(int left, int right) => left < right;
        static bool IComparisonOperators<int, int, bool>.operator <=(int left, int right) => left <= right;
        static bool IComparisonOperators<int, int, bool>.operator >(int left, int right) => left > right;
        static bool IComparisonOperators<int, int, bool>.operator >=(int left, int right) => left >= right;
        static int IIncrementOperators<int>.operator ++(int value) => ++value;
        static int IIncrementOperators<int>.operator checked ++(int value) => checked(++value);
        static int IDecrementOperators<int>.operator --(int value) => --value;
        static int IDecrementOperators<int>.operator checked --(int value) => checked(--value);
        static int IUnaryNegationOperators<int, int>.operator -(int value) => (int)(-value);
        static int IUnaryNegationOperators<int, int>.operator checked -(int value) => checked((int)(-value));
        static int IUnaryPlusOperators<int, int>.operator +(int value) => value;
        static int IAdditiveIdentity<int, int>.AdditiveIdentity => 0;
        static int IMultiplicativeIdentity<int, int>.MultiplicativeIdentity => 1;
        static int IMinMaxValue<int>.MinValue => MinValue;
        static int IMinMaxValue<int>.MaxValue => MaxValue;
        static int INumberBase<int>.One => 1;
        static int INumberBase<int>.Radix => 2;
        static int INumberBase<int>.Zero => 0;
        static bool INumberBase<int>.IsCanonical(int value) => true;
        static bool INumberBase<int>.IsComplexNumber(int value) => false;
        static bool INumberBase<int>.IsFinite(int value) => true;
        static bool INumberBase<int>.IsImaginaryNumber(int value) => false;
        static bool INumberBase<int>.IsInfinity(int value) => false;
        static bool INumberBase<int>.IsInteger(int value) => true;
        static bool INumberBase<int>.IsNaN(int value) => false;
        static bool INumberBase<int>.IsNegativeInfinity(int value) => false;
        static bool INumberBase<int>.IsNormal(int value) => value != 0;
        static bool INumberBase<int>.IsPositiveInfinity(int value) => false;
        static bool INumberBase<int>.IsRealNumber(int value) => true;
        static bool INumberBase<int>.IsSubnormal(int value) => false;
        static bool INumberBase<int>.IsZero(int value) => value == 0;
        static int INumberBase<int>.MaxMagnitudeNumber(int x, int y) => MaxMagnitude(x, y);
        static int INumberBase<int>.MinMagnitudeNumber(int x, int y) => MinMagnitude(x, y);
        static int INumber<int>.MaxNumber(int x, int y) => Max(x, y);
        static int INumber<int>.MinNumber(int x, int y) => Min(x, y);

        static bool INumberBase<int>.TryConvertFromChecked<TOther>(TOther value, out int result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Checked, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (int)bits;
            return converted;
        }

        static bool INumberBase<int>.TryConvertFromSaturating<TOther>(TOther value, out int result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Saturating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (int)bits;
            return converted;
        }

        static bool INumberBase<int>.TryConvertFromTruncating<TOther>(TOther value, out int result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Truncating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (int)bits;
            return converted;
        }

        static bool INumberBase<int>.TryConvertToChecked<TOther>(int value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<int>.TryConvertToSaturating<TOther>(int value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<int>.TryConvertToTruncating<TOther>(int value, out TOther result)
        {
            result = default!;
            return false;
        }

        int IBinaryInteger<int>.GetByteCount() => 4;

        int IBinaryInteger<int>.GetShortestBitLength() => m_value >= 0 ? 32 - (int)LeadingZeroCount(m_value) : 32 + 1 - (int)LeadingZeroCount((int)~m_value);

        static bool IBinaryInteger<int>.TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out int value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: true, isUnsigned, 4, true, out ulong bits);
            value = read ? (int)bits : (int)0;
            return read;
        }

        static bool IBinaryInteger<int>.TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out int value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: false, isUnsigned, 4, true, out ulong bits);
            value = read ? (int)bits : (int)0;
            return read;
        }

        bool IBinaryInteger<int>.TryWriteBigEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)m_value, 4, bigEndian: true, destination, out bytesWritten);

        bool IBinaryInteger<int>.TryWriteLittleEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)m_value, 4, bigEndian: false, destination, out bytesWritten);

        static int ISignedNumber<int>.NegativeOne => -1;
    }
    public readonly struct UInt32
        : IComparable, ISpanFormattable, IComparable<uint>, IEquatable<uint>, IBinaryInteger<uint>, IMinMaxValue<uint>, IUnsignedNumber<uint>
    {
        private readonly uint m_value;
        public const uint MaxValue = (uint)0xffffffff;
        public const uint MinValue = 0U;

        public bool Equals(uint obj)
        {
            return m_value == obj;
        }
        public override int GetHashCode()
        {
            return (int)m_value;
        }
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (!(obj is uint))
            {
                return false;
            }
            return m_value == ((uint)obj).m_value;
        }

        public static uint Log2(uint value) => (uint)BitOperations.Log2(value);





        public override string ToString()
        {
            return System.Number.FormatUInt32(m_value, null, null);
        }
        public string ToString(string format)
        {
            return System.Number.FormatUInt32(m_value, format, null);
        }

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
        {
            return Number.TryFormatUInt32(m_value, format, provider, destination, out charsWritten);
        }

        public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
        {
            return Number.TryFormatUInt32(m_value, format, provider, utf8Destination, out bytesWritten);
        }


    

        public int CompareTo(object? value)
        {
            if (value is null)
                return 1;
            if (value is uint other)
                return CompareTo(other);
            throw new ArgumentException(SR.Arg_MustBeUInt32);
        }

        public int CompareTo(uint value) => m_value < value ? -1 : m_value > value ? 1 : 0;

        public string ToString(IFormatProvider? provider) => ToString(null, provider);

        public string ToString(string? format, IFormatProvider? provider) => Number.FormatUInt32(m_value, format, provider);

        public static uint Parse(string s) => Parse(s, NumberStyles.Integer, null);

        public static uint Parse(string s, NumberStyles style) => Parse(s, style, null);

        public static uint Parse(string s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static uint Parse(string s, NumberStyles style, IFormatProvider? provider)
        {
            if (s is null)
                throw new ArgumentNullException(nameof(s));
            return Parse(s.AsSpan(), style, provider);
        }

        public static uint Parse(ReadOnlySpan<char> s, NumberStyles style = NumberStyles.Integer, IFormatProvider? provider = null)
        {
            Number.ParseStatus status = Number.TryParseBinaryInteger(s, style, out uint result);
            if (status != Number.ParseStatus.OK)
                Number.ThrowParseException(status);
            return result;
        }

        public static uint Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static bool TryParse([NotNullWhen(true)] string? s, out uint result) => TryParse(s, NumberStyles.Integer, null, out result);

        public static bool TryParse(ReadOnlySpan<char> s, out uint result) => TryParse(s, NumberStyles.Integer, null, out result);

        public static bool TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, out uint result)
        {
            if (s is null)
            {
                result = 0;
                return false;
            }
            return TryParse(s.AsSpan(), style, provider, out result);
        }

        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out uint result)
            => Number.TryParseBinaryInteger(s, style, out result) == Number.ParseStatus.OK;

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out uint result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out uint result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static (uint Quotient, uint Remainder) DivRem(uint left, uint right)
        {
            uint quotient = (uint)(left / right);
            return (quotient, (uint)(left - quotient * right));
        }

        public static uint LeadingZeroCount(uint value) => (uint)BitOperations.LeadingZeroCount((uint)(uint)value);

        public static uint PopCount(uint value) => (uint)BitOperations.PopCount((uint)(uint)value);

        public static uint RotateLeft(uint value, int rotateAmount) => (uint)BitOperations.RotateLeft((uint)(uint)value, rotateAmount);

        public static uint RotateRight(uint value, int rotateAmount) => (uint)BitOperations.RotateRight((uint)(uint)value, rotateAmount);

        public static uint TrailingZeroCount(uint value) => (uint)BitOperations.TrailingZeroCount((uint)(uint)value);

        public static bool IsPow2(uint value) => BitOperations.IsPow2(value);

        public static uint Clamp(uint value, uint min, uint max) => Math.Clamp(value, min, max);

        public static uint Max(uint x, uint y) => Math.Max(x, y);

        public static uint Min(uint x, uint y) => Math.Min(x, y);

        public static bool IsEvenInteger(uint value) => (value & 1) == 0;

        public static bool IsOddInteger(uint value) => (value & 1) != 0;

        public static uint CreateChecked<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Checked);

        public static uint CreateSaturating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Saturating);

        public static uint CreateTruncating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Truncating);

        private static uint Create<TOther>(TOther value, NumericConversion.Mode mode) where TOther : INumberBase<TOther>
        {
            if (NumericConversion.TryConvertToInteger(value, mode, (long)MinValue, (ulong)MaxValue, out ulong bits))
                return (uint)bits;
            uint result;
            bool converted = mode switch
            {
                NumericConversion.Mode.Checked => TOther.TryConvertToChecked(value, out result),
                NumericConversion.Mode.Saturating => TOther.TryConvertToSaturating(value, out result),
                _ => TOther.TryConvertToTruncating(value, out result),
            };
            if (!converted)
                ThrowHelper.ThrowNotSupportedException();
            return result;
        }

        static uint IAdditionOperators<uint, uint, uint>.operator +(uint left, uint right) => (uint)(left + right);
        static uint IAdditionOperators<uint, uint, uint>.operator checked +(uint left, uint right) => checked((uint)(left + right));
        static uint ISubtractionOperators<uint, uint, uint>.operator -(uint left, uint right) => (uint)(left - right);
        static uint ISubtractionOperators<uint, uint, uint>.operator checked -(uint left, uint right) => checked((uint)(left - right));
        static uint IMultiplyOperators<uint, uint, uint>.operator *(uint left, uint right) => (uint)(left * right);
        static uint IMultiplyOperators<uint, uint, uint>.operator checked *(uint left, uint right) => checked((uint)(left * right));
        static uint IDivisionOperators<uint, uint, uint>.operator /(uint left, uint right) => (uint)(left / right);
        static uint IModulusOperators<uint, uint, uint>.operator %(uint left, uint right) => (uint)(left % right);
        static uint IBitwiseOperators<uint, uint, uint>.operator &(uint left, uint right) => (uint)(left & right);
        static uint IBitwiseOperators<uint, uint, uint>.operator |(uint left, uint right) => (uint)(left | right);
        static uint IBitwiseOperators<uint, uint, uint>.operator ^(uint left, uint right) => (uint)(left ^ right);
        static uint IBitwiseOperators<uint, uint, uint>.operator ~(uint value) => (uint)(~value);
        static uint IShiftOperators<uint, int, uint>.operator <<(uint value, int shiftAmount) => (uint)(value << shiftAmount);
        static uint IShiftOperators<uint, int, uint>.operator >>(uint value, int shiftAmount) => (uint)(value >> shiftAmount);
        static uint IShiftOperators<uint, int, uint>.operator >>>(uint value, int shiftAmount) => (uint)((uint)value >>> shiftAmount);
        static bool IEqualityOperators<uint, uint, bool>.operator ==(uint left, uint right) => left == right;
        static bool IEqualityOperators<uint, uint, bool>.operator !=(uint left, uint right) => left != right;
        static bool IComparisonOperators<uint, uint, bool>.operator <(uint left, uint right) => left < right;
        static bool IComparisonOperators<uint, uint, bool>.operator <=(uint left, uint right) => left <= right;
        static bool IComparisonOperators<uint, uint, bool>.operator >(uint left, uint right) => left > right;
        static bool IComparisonOperators<uint, uint, bool>.operator >=(uint left, uint right) => left >= right;
        static uint IIncrementOperators<uint>.operator ++(uint value) => ++value;
        static uint IIncrementOperators<uint>.operator checked ++(uint value) => checked(++value);
        static uint IDecrementOperators<uint>.operator --(uint value) => --value;
        static uint IDecrementOperators<uint>.operator checked --(uint value) => checked(--value);
        static uint IUnaryNegationOperators<uint, uint>.operator -(uint value) => (uint)(0 - value);
        static uint IUnaryNegationOperators<uint, uint>.operator checked -(uint value) => checked((uint)(0 - value));
        static uint IUnaryPlusOperators<uint, uint>.operator +(uint value) => value;
        static uint IAdditiveIdentity<uint, uint>.AdditiveIdentity => 0;
        static uint IMultiplicativeIdentity<uint, uint>.MultiplicativeIdentity => 1;
        static uint IMinMaxValue<uint>.MinValue => MinValue;
        static uint IMinMaxValue<uint>.MaxValue => MaxValue;
        static uint INumberBase<uint>.One => 1;
        static int INumberBase<uint>.Radix => 2;
        static uint INumberBase<uint>.Zero => 0;
        static bool INumberBase<uint>.IsCanonical(uint value) => true;
        static bool INumberBase<uint>.IsComplexNumber(uint value) => false;
        static bool INumberBase<uint>.IsFinite(uint value) => true;
        static bool INumberBase<uint>.IsImaginaryNumber(uint value) => false;
        static bool INumberBase<uint>.IsInfinity(uint value) => false;
        static bool INumberBase<uint>.IsInteger(uint value) => true;
        static bool INumberBase<uint>.IsNaN(uint value) => false;
        static bool INumberBase<uint>.IsNegativeInfinity(uint value) => false;
        static bool INumberBase<uint>.IsNormal(uint value) => value != 0;
        static bool INumberBase<uint>.IsPositiveInfinity(uint value) => false;
        static bool INumberBase<uint>.IsRealNumber(uint value) => true;
        static bool INumberBase<uint>.IsSubnormal(uint value) => false;
        static bool INumberBase<uint>.IsZero(uint value) => value == 0;
        static uint INumberBase<uint>.MaxMagnitudeNumber(uint x, uint y) => Max(x, y);
        static uint INumberBase<uint>.MinMagnitudeNumber(uint x, uint y) => Min(x, y);
        static uint INumber<uint>.MaxNumber(uint x, uint y) => Max(x, y);
        static uint INumber<uint>.MinNumber(uint x, uint y) => Min(x, y);

        static bool INumberBase<uint>.TryConvertFromChecked<TOther>(TOther value, out uint result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Checked, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (uint)bits;
            return converted;
        }

        static bool INumberBase<uint>.TryConvertFromSaturating<TOther>(TOther value, out uint result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Saturating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (uint)bits;
            return converted;
        }

        static bool INumberBase<uint>.TryConvertFromTruncating<TOther>(TOther value, out uint result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Truncating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (uint)bits;
            return converted;
        }

        static bool INumberBase<uint>.TryConvertToChecked<TOther>(uint value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<uint>.TryConvertToSaturating<TOther>(uint value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<uint>.TryConvertToTruncating<TOther>(uint value, out TOther result)
        {
            result = default!;
            return false;
        }

        int IBinaryInteger<uint>.GetByteCount() => 4;

        int IBinaryInteger<uint>.GetShortestBitLength() => 32 - (int)LeadingZeroCount(m_value);

        static bool IBinaryInteger<uint>.TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out uint value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: true, isUnsigned, 4, false, out ulong bits);
            value = read ? (uint)bits : (uint)0;
            return read;
        }

        static bool IBinaryInteger<uint>.TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out uint value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: false, isUnsigned, 4, false, out ulong bits);
            value = read ? (uint)bits : (uint)0;
            return read;
        }

        bool IBinaryInteger<uint>.TryWriteBigEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)m_value, 4, bigEndian: true, destination, out bytesWritten);

        bool IBinaryInteger<uint>.TryWriteLittleEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)m_value, 4, bigEndian: false, destination, out bytesWritten);

        static uint INumberBase<uint>.Abs(uint value) => value;
        static uint INumber<uint>.CopySign(uint value, uint sign) => value;
        static int INumber<uint>.Sign(uint value) => value == 0 ? 0 : 1;
        static bool INumberBase<uint>.IsNegative(uint value) => false;
        static bool INumberBase<uint>.IsPositive(uint value) => true;
        static uint INumberBase<uint>.MaxMagnitude(uint x, uint y) => Max(x, y);
        static uint INumberBase<uint>.MinMagnitude(uint x, uint y) => Min(x, y);
    }
    public readonly struct Int64
        : IComparable, ISpanFormattable, IComparable<long>, IEquatable<long>, IBinaryInteger<long>, IMinMaxValue<long>, ISignedNumber<long>
    {
        private readonly long m_value;
        public const long MaxValue = 0x7fffffffffffffffL;
        public const long MinValue = unchecked((long)0x8000000000000000L);

        public bool Equals(long obj)
        {
            return m_value == obj;
        }
        // The value of the lower 32 bits XORed with the uppper 32 bits.
        public override int GetHashCode()
        {
            return unchecked((int)((long)m_value)) ^ (int)(m_value >> 32);
        }
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (!(obj is long))
            {
                return false;
            }
            return m_value == ((long)obj).m_value;
        }





        public override string ToString()
        {
            return System.Number.FormatInt64(m_value, null, null);
        }
        public string ToString(IFormatProvider? provider)
        {
            return System.Number.FormatInt64(m_value, null, provider);
        }
        public string ToString(string format)
        {
            return System.Number.FormatInt64(m_value, format, null);
        }
    

        public int CompareTo(object? value)
        {
            if (value is null)
                return 1;
            if (value is long other)
                return CompareTo(other);
            throw new ArgumentException(SR.Arg_MustBeInt64);
        }

        public int CompareTo(long value) => m_value < value ? -1 : m_value > value ? 1 : 0;

        public string ToString(string? format, IFormatProvider? provider) => Number.FormatInt64(m_value, format, provider);

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
            => Number.TryCopyFormatted(ToString(format.IsEmpty ? null : new string(format), provider), destination, out charsWritten);

        public static long Parse(string s) => Parse(s, NumberStyles.Integer, null);

        public static long Parse(string s, NumberStyles style) => Parse(s, style, null);

        public static long Parse(string s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static long Parse(string s, NumberStyles style, IFormatProvider? provider)
        {
            if (s is null)
                throw new ArgumentNullException(nameof(s));
            return Parse(s.AsSpan(), style, provider);
        }

        public static long Parse(ReadOnlySpan<char> s, NumberStyles style = NumberStyles.Integer, IFormatProvider? provider = null)
        {
            Number.ParseStatus status = Number.TryParseBinaryInteger(s, style, out long result);
            if (status != Number.ParseStatus.OK)
                Number.ThrowParseException(status);
            return result;
        }

        public static long Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static bool TryParse([NotNullWhen(true)] string? s, out long result) => TryParse(s, NumberStyles.Integer, null, out result);

        public static bool TryParse(ReadOnlySpan<char> s, out long result) => TryParse(s, NumberStyles.Integer, null, out result);

        public static bool TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, out long result)
        {
            if (s is null)
            {
                result = 0;
                return false;
            }
            return TryParse(s.AsSpan(), style, provider, out result);
        }

        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out long result)
            => Number.TryParseBinaryInteger(s, style, out result) == Number.ParseStatus.OK;

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out long result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out long result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static (long Quotient, long Remainder) DivRem(long left, long right)
        {
            long quotient = (long)(left / right);
            return (quotient, (long)(left - quotient * right));
        }

        public static long LeadingZeroCount(long value) => (long)BitOperations.LeadingZeroCount((ulong)(ulong)value);

        public static long PopCount(long value) => (long)BitOperations.PopCount((ulong)(ulong)value);

        public static long RotateLeft(long value, int rotateAmount) => (long)BitOperations.RotateLeft((ulong)(ulong)value, rotateAmount);

        public static long RotateRight(long value, int rotateAmount) => (long)BitOperations.RotateRight((ulong)(ulong)value, rotateAmount);

        public static long TrailingZeroCount(long value) => (long)BitOperations.TrailingZeroCount((ulong)(ulong)value);

        public static bool IsPow2(long value) => BitOperations.IsPow2(value);

        public static long Log2(long value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), SR.ArgumentOutOfRange_NeedNonNegNum);
            return (long)BitOperations.Log2((ulong)(ulong)value);
        }

        public static long Clamp(long value, long min, long max) => Math.Clamp(value, min, max);

        public static long Max(long x, long y) => Math.Max(x, y);

        public static long Min(long x, long y) => Math.Min(x, y);

        public static bool IsEvenInteger(long value) => (value & 1) == 0;

        public static bool IsOddInteger(long value) => (value & 1) != 0;

        public static long CreateChecked<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Checked);

        public static long CreateSaturating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Saturating);

        public static long CreateTruncating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Truncating);

        private static long Create<TOther>(TOther value, NumericConversion.Mode mode) where TOther : INumberBase<TOther>
        {
            if (NumericConversion.TryConvertToInteger(value, mode, (long)MinValue, (ulong)MaxValue, out ulong bits))
                return (long)bits;
            long result;
            bool converted = mode switch
            {
                NumericConversion.Mode.Checked => TOther.TryConvertToChecked(value, out result),
                NumericConversion.Mode.Saturating => TOther.TryConvertToSaturating(value, out result),
                _ => TOther.TryConvertToTruncating(value, out result),
            };
            if (!converted)
                ThrowHelper.ThrowNotSupportedException();
            return result;
        }

        public static long Abs(long value) => Math.Abs(value);

        public static long CopySign(long value, long sign)
        {
            long absValue = value;
            if (absValue < 0)
                absValue = (long)(-absValue);
            if (sign >= 0)
            {
                if (absValue < 0)
                    Math.ThrowNegateTwosCompOverflow();
                return absValue;
            }
            return (long)(-absValue);
        }

        public static int Sign(long value) => value < 0 ? -1 : value > 0 ? 1 : 0;

        public static bool IsNegative(long value) => value < 0;

        public static bool IsPositive(long value) => value >= 0;

        public static long MaxMagnitude(long x, long y)
        {
            long absX = x;
            if (absX < 0)
            {
                absX = (long)(-absX);
                if (absX < 0)
                    return x;
            }
            long absY = y;
            if (absY < 0)
            {
                absY = (long)(-absY);
                if (absY < 0)
                    return y;
            }
            if (absX > absY)
                return x;
            if (absX == absY)
                return IsNegative(x) ? y : x;
            return y;
        }

        public static long MinMagnitude(long x, long y)
        {
            long absX = x;
            if (absX < 0)
            {
                absX = (long)(-absX);
                if (absX < 0)
                    return y;
            }
            long absY = y;
            if (absY < 0)
            {
                absY = (long)(-absY);
                if (absY < 0)
                    return x;
            }
            if (absX < absY)
                return x;
            if (absX == absY)
                return IsNegative(x) ? x : y;
            return y;
        }

        static long IAdditionOperators<long, long, long>.operator +(long left, long right) => (long)(left + right);
        static long IAdditionOperators<long, long, long>.operator checked +(long left, long right) => checked((long)(left + right));
        static long ISubtractionOperators<long, long, long>.operator -(long left, long right) => (long)(left - right);
        static long ISubtractionOperators<long, long, long>.operator checked -(long left, long right) => checked((long)(left - right));
        static long IMultiplyOperators<long, long, long>.operator *(long left, long right) => (long)(left * right);
        static long IMultiplyOperators<long, long, long>.operator checked *(long left, long right) => checked((long)(left * right));
        static long IDivisionOperators<long, long, long>.operator /(long left, long right) => (long)(left / right);
        static long IModulusOperators<long, long, long>.operator %(long left, long right) => (long)(left % right);
        static long IBitwiseOperators<long, long, long>.operator &(long left, long right) => (long)(left & right);
        static long IBitwiseOperators<long, long, long>.operator |(long left, long right) => (long)(left | right);
        static long IBitwiseOperators<long, long, long>.operator ^(long left, long right) => (long)(left ^ right);
        static long IBitwiseOperators<long, long, long>.operator ~(long value) => (long)(~value);
        static long IShiftOperators<long, int, long>.operator <<(long value, int shiftAmount) => (long)(value << shiftAmount);
        static long IShiftOperators<long, int, long>.operator >>(long value, int shiftAmount) => (long)(value >> shiftAmount);
        static long IShiftOperators<long, int, long>.operator >>>(long value, int shiftAmount) => (long)((ulong)value >>> shiftAmount);
        static bool IEqualityOperators<long, long, bool>.operator ==(long left, long right) => left == right;
        static bool IEqualityOperators<long, long, bool>.operator !=(long left, long right) => left != right;
        static bool IComparisonOperators<long, long, bool>.operator <(long left, long right) => left < right;
        static bool IComparisonOperators<long, long, bool>.operator <=(long left, long right) => left <= right;
        static bool IComparisonOperators<long, long, bool>.operator >(long left, long right) => left > right;
        static bool IComparisonOperators<long, long, bool>.operator >=(long left, long right) => left >= right;
        static long IIncrementOperators<long>.operator ++(long value) => ++value;
        static long IIncrementOperators<long>.operator checked ++(long value) => checked(++value);
        static long IDecrementOperators<long>.operator --(long value) => --value;
        static long IDecrementOperators<long>.operator checked --(long value) => checked(--value);
        static long IUnaryNegationOperators<long, long>.operator -(long value) => (long)(-value);
        static long IUnaryNegationOperators<long, long>.operator checked -(long value) => checked((long)(-value));
        static long IUnaryPlusOperators<long, long>.operator +(long value) => value;
        static long IAdditiveIdentity<long, long>.AdditiveIdentity => 0;
        static long IMultiplicativeIdentity<long, long>.MultiplicativeIdentity => 1;
        static long IMinMaxValue<long>.MinValue => MinValue;
        static long IMinMaxValue<long>.MaxValue => MaxValue;
        static long INumberBase<long>.One => 1;
        static int INumberBase<long>.Radix => 2;
        static long INumberBase<long>.Zero => 0;
        static bool INumberBase<long>.IsCanonical(long value) => true;
        static bool INumberBase<long>.IsComplexNumber(long value) => false;
        static bool INumberBase<long>.IsFinite(long value) => true;
        static bool INumberBase<long>.IsImaginaryNumber(long value) => false;
        static bool INumberBase<long>.IsInfinity(long value) => false;
        static bool INumberBase<long>.IsInteger(long value) => true;
        static bool INumberBase<long>.IsNaN(long value) => false;
        static bool INumberBase<long>.IsNegativeInfinity(long value) => false;
        static bool INumberBase<long>.IsNormal(long value) => value != 0;
        static bool INumberBase<long>.IsPositiveInfinity(long value) => false;
        static bool INumberBase<long>.IsRealNumber(long value) => true;
        static bool INumberBase<long>.IsSubnormal(long value) => false;
        static bool INumberBase<long>.IsZero(long value) => value == 0;
        static long INumberBase<long>.MaxMagnitudeNumber(long x, long y) => MaxMagnitude(x, y);
        static long INumberBase<long>.MinMagnitudeNumber(long x, long y) => MinMagnitude(x, y);
        static long INumber<long>.MaxNumber(long x, long y) => Max(x, y);
        static long INumber<long>.MinNumber(long x, long y) => Min(x, y);

        static bool INumberBase<long>.TryConvertFromChecked<TOther>(TOther value, out long result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Checked, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (long)bits;
            return converted;
        }

        static bool INumberBase<long>.TryConvertFromSaturating<TOther>(TOther value, out long result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Saturating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (long)bits;
            return converted;
        }

        static bool INumberBase<long>.TryConvertFromTruncating<TOther>(TOther value, out long result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Truncating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (long)bits;
            return converted;
        }

        static bool INumberBase<long>.TryConvertToChecked<TOther>(long value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<long>.TryConvertToSaturating<TOther>(long value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<long>.TryConvertToTruncating<TOther>(long value, out TOther result)
        {
            result = default!;
            return false;
        }

        int IBinaryInteger<long>.GetByteCount() => 8;

        int IBinaryInteger<long>.GetShortestBitLength() => m_value >= 0 ? 64 - (int)LeadingZeroCount(m_value) : 64 + 1 - (int)LeadingZeroCount((long)~m_value);

        static bool IBinaryInteger<long>.TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out long value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: true, isUnsigned, 8, true, out ulong bits);
            value = read ? (long)bits : (long)0;
            return read;
        }

        static bool IBinaryInteger<long>.TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out long value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: false, isUnsigned, 8, true, out ulong bits);
            value = read ? (long)bits : (long)0;
            return read;
        }

        bool IBinaryInteger<long>.TryWriteBigEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)m_value, 8, bigEndian: true, destination, out bytesWritten);

        bool IBinaryInteger<long>.TryWriteLittleEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)m_value, 8, bigEndian: false, destination, out bytesWritten);

        static long ISignedNumber<long>.NegativeOne => -1;
    }
    public readonly struct UInt64
        : IComparable, ISpanFormattable, IComparable<ulong>, IEquatable<ulong>, IBinaryInteger<ulong>, IMinMaxValue<ulong>, IUnsignedNumber<ulong>
    {
        private readonly ulong m_value;
        public const ulong MaxValue = (ulong)0xffffffffffffffffL;
        public const ulong MinValue = 0x0;

        public bool Equals(ulong obj)
        {
            return m_value == obj;
        }
        // The value of the lower 32 bits XORed with the uppper 32 bits.
        public override int GetHashCode()
        {
            return ((int)m_value) ^ (int)(m_value >> 32);
        }

        public static ulong Log2(ulong value) => (ulong)System.Numerics.BitOperations.Log2(value);





        public override string ToString()
        {
            return System.Number.FormatUInt64(m_value, null, null);
        }
        public string ToString(IFormatProvider? provider)
        {
            return System.Number.FormatUInt64(m_value, null, provider);
        }
        public string ToString(string format)
        {
            return System.Number.FormatUInt64(m_value, format, null);
        }
    

        public int CompareTo(object? value)
        {
            if (value is null)
                return 1;
            if (value is ulong other)
                return CompareTo(other);
            throw new ArgumentException(SR.Arg_MustBeUInt64);
        }

        public int CompareTo(ulong value) => m_value < value ? -1 : m_value > value ? 1 : 0;

        public string ToString(string? format, IFormatProvider? provider) => Number.FormatUInt64(m_value, format, provider);

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
            => Number.TryCopyFormatted(ToString(format.IsEmpty ? null : new string(format), provider), destination, out charsWritten);

        public static ulong Parse(string s) => Parse(s, NumberStyles.Integer, null);

        public static ulong Parse(string s, NumberStyles style) => Parse(s, style, null);

        public static ulong Parse(string s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static ulong Parse(string s, NumberStyles style, IFormatProvider? provider)
        {
            if (s is null)
                throw new ArgumentNullException(nameof(s));
            return Parse(s.AsSpan(), style, provider);
        }

        public static ulong Parse(ReadOnlySpan<char> s, NumberStyles style = NumberStyles.Integer, IFormatProvider? provider = null)
        {
            Number.ParseStatus status = Number.TryParseBinaryInteger(s, style, out ulong result);
            if (status != Number.ParseStatus.OK)
                Number.ThrowParseException(status);
            return result;
        }

        public static ulong Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static bool TryParse([NotNullWhen(true)] string? s, out ulong result) => TryParse(s, NumberStyles.Integer, null, out result);

        public static bool TryParse(ReadOnlySpan<char> s, out ulong result) => TryParse(s, NumberStyles.Integer, null, out result);

        public static bool TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, out ulong result)
        {
            if (s is null)
            {
                result = 0;
                return false;
            }
            return TryParse(s.AsSpan(), style, provider, out result);
        }

        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out ulong result)
            => Number.TryParseBinaryInteger(s, style, out result) == Number.ParseStatus.OK;

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ulong result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ulong result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static (ulong Quotient, ulong Remainder) DivRem(ulong left, ulong right)
        {
            ulong quotient = (ulong)(left / right);
            return (quotient, (ulong)(left - quotient * right));
        }

        public static ulong LeadingZeroCount(ulong value) => (ulong)BitOperations.LeadingZeroCount((ulong)(ulong)value);

        public static ulong PopCount(ulong value) => (ulong)BitOperations.PopCount((ulong)(ulong)value);

        public static ulong RotateLeft(ulong value, int rotateAmount) => (ulong)BitOperations.RotateLeft((ulong)(ulong)value, rotateAmount);

        public static ulong RotateRight(ulong value, int rotateAmount) => (ulong)BitOperations.RotateRight((ulong)(ulong)value, rotateAmount);

        public static ulong TrailingZeroCount(ulong value) => (ulong)BitOperations.TrailingZeroCount((ulong)(ulong)value);

        public static bool IsPow2(ulong value) => BitOperations.IsPow2(value);

        public static ulong Clamp(ulong value, ulong min, ulong max) => Math.Clamp(value, min, max);

        public static ulong Max(ulong x, ulong y) => Math.Max(x, y);

        public static ulong Min(ulong x, ulong y) => Math.Min(x, y);

        public static bool IsEvenInteger(ulong value) => (value & 1) == 0;

        public static bool IsOddInteger(ulong value) => (value & 1) != 0;

        public static ulong CreateChecked<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Checked);

        public static ulong CreateSaturating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Saturating);

        public static ulong CreateTruncating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Truncating);

        private static ulong Create<TOther>(TOther value, NumericConversion.Mode mode) where TOther : INumberBase<TOther>
        {
            if (NumericConversion.TryConvertToInteger(value, mode, (long)MinValue, (ulong)MaxValue, out ulong bits))
                return (ulong)bits;
            ulong result;
            bool converted = mode switch
            {
                NumericConversion.Mode.Checked => TOther.TryConvertToChecked(value, out result),
                NumericConversion.Mode.Saturating => TOther.TryConvertToSaturating(value, out result),
                _ => TOther.TryConvertToTruncating(value, out result),
            };
            if (!converted)
                ThrowHelper.ThrowNotSupportedException();
            return result;
        }

        static ulong IAdditionOperators<ulong, ulong, ulong>.operator +(ulong left, ulong right) => (ulong)(left + right);
        static ulong IAdditionOperators<ulong, ulong, ulong>.operator checked +(ulong left, ulong right) => checked((ulong)(left + right));
        static ulong ISubtractionOperators<ulong, ulong, ulong>.operator -(ulong left, ulong right) => (ulong)(left - right);
        static ulong ISubtractionOperators<ulong, ulong, ulong>.operator checked -(ulong left, ulong right) => checked((ulong)(left - right));
        static ulong IMultiplyOperators<ulong, ulong, ulong>.operator *(ulong left, ulong right) => (ulong)(left * right);
        static ulong IMultiplyOperators<ulong, ulong, ulong>.operator checked *(ulong left, ulong right) => checked((ulong)(left * right));
        static ulong IDivisionOperators<ulong, ulong, ulong>.operator /(ulong left, ulong right) => (ulong)(left / right);
        static ulong IModulusOperators<ulong, ulong, ulong>.operator %(ulong left, ulong right) => (ulong)(left % right);
        static ulong IBitwiseOperators<ulong, ulong, ulong>.operator &(ulong left, ulong right) => (ulong)(left & right);
        static ulong IBitwiseOperators<ulong, ulong, ulong>.operator |(ulong left, ulong right) => (ulong)(left | right);
        static ulong IBitwiseOperators<ulong, ulong, ulong>.operator ^(ulong left, ulong right) => (ulong)(left ^ right);
        static ulong IBitwiseOperators<ulong, ulong, ulong>.operator ~(ulong value) => (ulong)(~value);
        static ulong IShiftOperators<ulong, int, ulong>.operator <<(ulong value, int shiftAmount) => (ulong)(value << shiftAmount);
        static ulong IShiftOperators<ulong, int, ulong>.operator >>(ulong value, int shiftAmount) => (ulong)(value >> shiftAmount);
        static ulong IShiftOperators<ulong, int, ulong>.operator >>>(ulong value, int shiftAmount) => (ulong)((ulong)value >>> shiftAmount);
        static bool IEqualityOperators<ulong, ulong, bool>.operator ==(ulong left, ulong right) => left == right;
        static bool IEqualityOperators<ulong, ulong, bool>.operator !=(ulong left, ulong right) => left != right;
        static bool IComparisonOperators<ulong, ulong, bool>.operator <(ulong left, ulong right) => left < right;
        static bool IComparisonOperators<ulong, ulong, bool>.operator <=(ulong left, ulong right) => left <= right;
        static bool IComparisonOperators<ulong, ulong, bool>.operator >(ulong left, ulong right) => left > right;
        static bool IComparisonOperators<ulong, ulong, bool>.operator >=(ulong left, ulong right) => left >= right;
        static ulong IIncrementOperators<ulong>.operator ++(ulong value) => ++value;
        static ulong IIncrementOperators<ulong>.operator checked ++(ulong value) => checked(++value);
        static ulong IDecrementOperators<ulong>.operator --(ulong value) => --value;
        static ulong IDecrementOperators<ulong>.operator checked --(ulong value) => checked(--value);
        static ulong IUnaryNegationOperators<ulong, ulong>.operator -(ulong value) => (ulong)(0 - value);
        static ulong IUnaryNegationOperators<ulong, ulong>.operator checked -(ulong value) => checked((ulong)(0 - value));
        static ulong IUnaryPlusOperators<ulong, ulong>.operator +(ulong value) => value;
        static ulong IAdditiveIdentity<ulong, ulong>.AdditiveIdentity => 0;
        static ulong IMultiplicativeIdentity<ulong, ulong>.MultiplicativeIdentity => 1;
        static ulong IMinMaxValue<ulong>.MinValue => MinValue;
        static ulong IMinMaxValue<ulong>.MaxValue => MaxValue;
        static ulong INumberBase<ulong>.One => 1;
        static int INumberBase<ulong>.Radix => 2;
        static ulong INumberBase<ulong>.Zero => 0;
        static bool INumberBase<ulong>.IsCanonical(ulong value) => true;
        static bool INumberBase<ulong>.IsComplexNumber(ulong value) => false;
        static bool INumberBase<ulong>.IsFinite(ulong value) => true;
        static bool INumberBase<ulong>.IsImaginaryNumber(ulong value) => false;
        static bool INumberBase<ulong>.IsInfinity(ulong value) => false;
        static bool INumberBase<ulong>.IsInteger(ulong value) => true;
        static bool INumberBase<ulong>.IsNaN(ulong value) => false;
        static bool INumberBase<ulong>.IsNegativeInfinity(ulong value) => false;
        static bool INumberBase<ulong>.IsNormal(ulong value) => value != 0;
        static bool INumberBase<ulong>.IsPositiveInfinity(ulong value) => false;
        static bool INumberBase<ulong>.IsRealNumber(ulong value) => true;
        static bool INumberBase<ulong>.IsSubnormal(ulong value) => false;
        static bool INumberBase<ulong>.IsZero(ulong value) => value == 0;
        static ulong INumberBase<ulong>.MaxMagnitudeNumber(ulong x, ulong y) => Max(x, y);
        static ulong INumberBase<ulong>.MinMagnitudeNumber(ulong x, ulong y) => Min(x, y);
        static ulong INumber<ulong>.MaxNumber(ulong x, ulong y) => Max(x, y);
        static ulong INumber<ulong>.MinNumber(ulong x, ulong y) => Min(x, y);

        static bool INumberBase<ulong>.TryConvertFromChecked<TOther>(TOther value, out ulong result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Checked, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (ulong)bits;
            return converted;
        }

        static bool INumberBase<ulong>.TryConvertFromSaturating<TOther>(TOther value, out ulong result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Saturating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (ulong)bits;
            return converted;
        }

        static bool INumberBase<ulong>.TryConvertFromTruncating<TOther>(TOther value, out ulong result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Truncating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (ulong)bits;
            return converted;
        }

        static bool INumberBase<ulong>.TryConvertToChecked<TOther>(ulong value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<ulong>.TryConvertToSaturating<TOther>(ulong value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<ulong>.TryConvertToTruncating<TOther>(ulong value, out TOther result)
        {
            result = default!;
            return false;
        }

        int IBinaryInteger<ulong>.GetByteCount() => 8;

        int IBinaryInteger<ulong>.GetShortestBitLength() => 64 - (int)LeadingZeroCount(m_value);

        static bool IBinaryInteger<ulong>.TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out ulong value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: true, isUnsigned, 8, false, out ulong bits);
            value = read ? (ulong)bits : (ulong)0;
            return read;
        }

        static bool IBinaryInteger<ulong>.TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out ulong value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: false, isUnsigned, 8, false, out ulong bits);
            value = read ? (ulong)bits : (ulong)0;
            return read;
        }

        bool IBinaryInteger<ulong>.TryWriteBigEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)m_value, 8, bigEndian: true, destination, out bytesWritten);

        bool IBinaryInteger<ulong>.TryWriteLittleEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)m_value, 8, bigEndian: false, destination, out bytesWritten);

        static ulong INumberBase<ulong>.Abs(ulong value) => value;
        static ulong INumber<ulong>.CopySign(ulong value, ulong sign) => value;
        static int INumber<ulong>.Sign(ulong value) => value == 0 ? 0 : 1;
        static bool INumberBase<ulong>.IsNegative(ulong value) => false;
        static bool INumberBase<ulong>.IsPositive(ulong value) => true;
        static ulong INumberBase<ulong>.MaxMagnitude(ulong x, ulong y) => Max(x, y);
        static ulong INumberBase<ulong>.MinMagnitude(ulong x, ulong y) => Min(x, y);
    }
    public struct Single
        : IComparable, ISpanFormattable, IComparable<float>, IEquatable<float>, IBinaryFloatingPointIeee754<float>, IMinMaxValue<float>
    {
        private readonly float m_value;
        public const float MinValue = (float)-3.40282346638528859e+38;
        public const float MaxValue = (float)3.40282346638528859e+38;

        public const float Epsilon = (float)1.4e-45;
        public const float NegativeInfinity = (float)-1.0 / (float)0.0;
        public const float PositiveInfinity = (float)1.0 / (float)0.0;
        public const float NaN = (float)0.0 / (float)0.0;

        internal const float AdditiveIdentity = 0.0f;
        internal const float MultiplicativeIdentity = 1.0f;
        internal const float One = 1.0f;
        internal const float Zero = 0.0f;
        internal const float NegativeOne = -1.0f;
        public const float NegativeZero = -0.0f;


        internal const uint SignMask = 0x8000_0000;
        internal const int SignShift = 31;
        internal const byte ShiftedSignMask = (byte)(SignMask >> SignShift);

        internal const uint BiasedExponentMask = 0x7F80_0000;
        internal const int BiasedExponentShift = 23;
        internal const int BiasedExponentLength = 8;
        internal const byte ShiftedBiasedExponentMask = (byte)(BiasedExponentMask >> BiasedExponentShift);

        internal const uint TrailingSignificandMask = 0x007F_FFFF;

        internal const byte MinSign = 0;
        internal const byte MaxSign = 1;

        internal const byte MinBiasedExponent = 0x00;
        internal const byte MaxBiasedExponent = 0xFF;

        internal const byte ExponentBias = 127;

        internal const sbyte MinExponent = -126;
        internal const sbyte MaxExponent = +127;

        internal const uint MinTrailingSignificand = 0x0000_0000;
        internal const uint MaxTrailingSignificand = 0x007F_FFFF;

        internal const int TrailingSignificandLength = 23;
        internal const int SignificandLength = TrailingSignificandLength + 1;

        // Constants representing the private bit-representation for various default values

        internal const uint PositiveZeroBits = 0x0000_0000;
        internal const uint NegativeZeroBits = 0x8000_0000;

        internal const uint EpsilonBits = 0x0000_0001;

        internal const uint PositiveInfinityBits = 0x7F80_0000;
        internal const uint NegativeInfinityBits = 0xFF80_0000;

        internal const uint SmallestNormalBits = 0x0080_0000;

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return (obj is float other) && Equals(other);
        }
        public bool Equals(float obj)
        {
            if (obj == m_value)
            {
                return true;
            }
            return IsNaN(obj) && IsNaN(m_value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            uint bits = BitConverter.SingleToUInt32Bits(m_value);

            if (IsNaNOrZero(m_value))
            {
                // Ensure that all NaNs and both zeros have the same hash code
                bits &= PositiveInfinityBits;
            }

            return (int)bits;
        }

        public override string ToString()
        {
            return System.Number.FormatFloat(m_value, null, null);
        }
        public string ToString(IFormatProvider? provider)
        {
            return System.Number.FormatFloat(m_value, null, null);
        }
        public string ToString(string? format)
        {
            return System.Number.FormatFloat(m_value, format, null);
        }
        public static float Abs(float value) => MathF.Abs(value);
        public static float Sqrt(float x) => MathF.Sqrt(x);
        public static float Sin(float x) => MathF.Sin(x);
        public static float Cos(float x) => MathF.Cos(x);
        public static (float Sin, float Cos) SinCos(float x) => MathF.SinCos(x);
        public static float Exp(float x) => MathF.Exp(x);
        public static float FusedMultiplyAdd(float left, float right, float addend) => MathF.FusedMultiplyAdd(left, right, addend);
        public static float MultiplyAddEstimate(float left, float right, float addend) => (left * right) + addend;
        public static TInteger ConvertToInteger<TInteger>(float value) where TInteger : IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);
        public static TInteger ConvertToIntegerNative<TInteger>(float value) where TInteger : IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite(float f)
        {
            uint bits = BitConverter.SingleToUInt32Bits(f);
            return (~bits & PositiveInfinityBits) != 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInfinity(float f)
        {
            uint bits = BitConverter.SingleToUInt32Bits(Abs(f));
            return bits == PositiveInfinityBits;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNaN(float f)
        {
            return f != f;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsNaNOrZero(float f)
        {
            uint bits = BitConverter.SingleToUInt32Bits(f);
            return ((bits - 1) & ~SignMask) >= PositiveInfinityBits;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNegative(float f)
        {
            return BitConverter.SingleToInt32Bits(f) < 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNegativeInfinity(float f)
        {
            return f == NegativeInfinity;
        }
    

        public const float E = 2.71828183f;

        public const float Pi = 3.14159265f;

        public const float Tau = 6.283185307f;

        public int CompareTo(object? value)
        {
            if (value is null)
                return 1;
            if (value is float other)
                return CompareTo(other);
            throw new ArgumentException(SR.Arg_MustBeSingle);
        }

        public int CompareTo(float value)
        {
            if (m_value < value)
                return -1;
            if (m_value > value)
                return 1;
            if (m_value == value)
                return 0;
            if (IsNaN(m_value))
                return IsNaN(value) ? 0 : -1;
            return 1;
        }

        public string ToString(string? format, IFormatProvider? provider) => Number.FormatFloat(m_value, format, null);

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
            => Number.TryCopyFormatted(ToString(format.IsEmpty ? null : new string(format), provider), destination, out charsWritten);

        public static bool IsPositive(float f) => BitConverter.SingleToInt32Bits(f) >= 0;

        public static bool IsPositiveInfinity(float f) => f == PositiveInfinity;

        public static bool IsNormal(float f)
        {
            float abs = MathF.Abs(f);
            return abs < PositiveInfinity && abs >= 1.17549435E-38f && abs != 0;
        }

        public static bool IsSubnormal(float f)
        {
            float abs = MathF.Abs(f);
            return abs < 1.17549435E-38f && abs != 0;
        }

        public static bool IsInteger(float value) => IsFinite(value) && value == Truncate(value);

        public static bool IsEvenInteger(float value) => IsInteger(value) && MathF.Abs(value % 2) == 0;

        public static bool IsOddInteger(float value) => IsInteger(value) && MathF.Abs(value % 2) == 1;

        public static bool IsRealNumber(float value) => !IsNaN(value);

        public static float Truncate(float x) => MathF.Truncate(x);

        public static float Floor(float x) => MathF.Floor(x);

        public static float Ceiling(float x) => MathF.Ceiling(x);

        public static float Round(float x) => MathF.Round(x);

        public static float Round(float x, int digits) => MathF.Round(x, digits, MidpointRounding.ToEven);

        public static float Round(float x, MidpointRounding mode) => MathF.Round(x, 0, mode);

        public static float Round(float x, int digits, MidpointRounding mode) => MathF.Round(x, digits, mode);

        public static float Clamp(float value, float min, float max)
        {
            if (min > max)
                Math.ThrowMinMaxException(min, max);
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        public static float CopySign(float value, float sign) => MathF.CopySign(value, sign);

        public static float CreateChecked<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Checked);

        public static float CreateSaturating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Saturating);

        public static float CreateTruncating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Truncating);

        private static float Create<TOther>(TOther value, NumericConversion.Mode mode) where TOther : INumberBase<TOther>
        {
            if (NumericConversion.TryConvertToSingle(value, out float result))
                return result;
            bool converted = mode switch
            {
                NumericConversion.Mode.Checked => TOther.TryConvertToChecked(value, out result),
                NumericConversion.Mode.Saturating => TOther.TryConvertToSaturating(value, out result),
                _ => TOther.TryConvertToTruncating(value, out result),
            };
            if (!converted)
                ThrowHelper.ThrowNotSupportedException();
            return result;
        }

        public static int Sign(float value)
        {
            if (value < 0)
                return -1;
            if (value > 0)
                return 1;
            if (value == 0)
                return 0;
            throw new ArithmeticException(SR.Arithmetic_NaN);
        }

        public static float Max(float x, float y)
        {
            if (x != y)
                return IsNaN(x) ? x : (y < x ? x : y);
            return IsNegative(y) ? x : y;
        }

        public static float Min(float x, float y)
        {
            if (x != y)
                return IsNaN(x) ? x : (x < y ? x : y);
            return IsNegative(x) ? x : y;
        }

        public static float MaxNumber(float x, float y)
        {
            if (x != y)
                return IsNaN(y) ? x : (y < x ? x : y);
            return IsNegative(y) ? x : y;
        }

        public static float MinNumber(float x, float y)
        {
            if (x != y)
                return IsNaN(y) ? x : (x < y ? x : y);
            return IsNegative(x) ? x : y;
        }

        public static float MaxMagnitude(float x, float y)
        {
            float ax = MathF.Abs(x);
            float ay = MathF.Abs(y);
            if (ax > ay || IsNaN(ax))
                return x;
            if (ax == ay)
                return IsNegative(x) ? y : x;
            return y;
        }

        public static float MinMagnitude(float x, float y)
        {
            float ax = MathF.Abs(x);
            float ay = MathF.Abs(y);
            if (ax < ay || IsNaN(ax))
                return x;
            if (ax == ay)
                return IsNegative(x) ? x : y;
            return y;
        }

        public static float MaxMagnitudeNumber(float x, float y)
        {
            float ax = MathF.Abs(x);
            float ay = MathF.Abs(y);
            if (ax > ay || IsNaN(ay))
                return x;
            if (ax == ay)
                return IsNegative(x) ? y : x;
            return y;
        }

        public static float MinMagnitudeNumber(float x, float y)
        {
            float ax = MathF.Abs(x);
            float ay = MathF.Abs(y);
            if (ax < ay || IsNaN(ay))
                return x;
            if (ax == ay)
                return IsNegative(x) ? x : y;
            return y;
        }

        static float IAdditionOperators<float, float, float>.operator +(float left, float right) => left + right;
        static float ISubtractionOperators<float, float, float>.operator -(float left, float right) => left - right;
        static float IMultiplyOperators<float, float, float>.operator *(float left, float right) => left * right;
        static float IDivisionOperators<float, float, float>.operator /(float left, float right) => left / right;
        static float IModulusOperators<float, float, float>.operator %(float left, float right) => left % right;
        static bool IEqualityOperators<float, float, bool>.operator ==(float left, float right) => left == right;
        static bool IEqualityOperators<float, float, bool>.operator !=(float left, float right) => left != right;
        static bool IComparisonOperators<float, float, bool>.operator <(float left, float right) => left < right;
        static bool IComparisonOperators<float, float, bool>.operator <=(float left, float right) => left <= right;
        static bool IComparisonOperators<float, float, bool>.operator >(float left, float right) => left > right;
        static bool IComparisonOperators<float, float, bool>.operator >=(float left, float right) => left >= right;
        static float IIncrementOperators<float>.operator ++(float value) => ++value;
        static float IDecrementOperators<float>.operator --(float value) => --value;
        static float IUnaryNegationOperators<float, float>.operator -(float value) => -value;
        static float IUnaryPlusOperators<float, float>.operator +(float value) => value;
        static float IAdditiveIdentity<float, float>.AdditiveIdentity => 0;
        static float IMultiplicativeIdentity<float, float>.MultiplicativeIdentity => 1;
        static float IMinMaxValue<float>.MinValue => MinValue;
        static float IMinMaxValue<float>.MaxValue => MaxValue;
        static float IFloatingPointConstants<float>.E => E;
        static float IFloatingPointConstants<float>.Pi => Pi;
        static float IFloatingPointConstants<float>.Tau => Tau;
        static float ISignedNumber<float>.NegativeOne => -1;
        static float INumberBase<float>.One => 1;
        static int INumberBase<float>.Radix => 2;
        static float INumberBase<float>.Zero => 0;
        static bool INumberBase<float>.IsCanonical(float value) => true;
        static bool INumberBase<float>.IsComplexNumber(float value) => false;
        static bool INumberBase<float>.IsImaginaryNumber(float value) => false;
        static bool INumberBase<float>.IsZero(float value) => value == 0;

        static bool INumberBase<float>.TryConvertFromChecked<TOther>(TOther value, out float result) => NumericConversion.TryConvertToSingle(value, out result);

        static bool INumberBase<float>.TryConvertFromSaturating<TOther>(TOther value, out float result) => NumericConversion.TryConvertToSingle(value, out result);

        static bool INumberBase<float>.TryConvertFromTruncating<TOther>(TOther value, out float result) => NumericConversion.TryConvertToSingle(value, out result);

        static bool INumberBase<float>.TryConvertToChecked<TOther>(float value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<float>.TryConvertToSaturating<TOther>(float value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<float>.TryConvertToTruncating<TOther>(float value, out TOther result)
        {
            result = default!;
            return false;
        }

        // Correctly rounded parsing of binary floating point is not implemented yet.
        static float INumberBase<float>.Parse(string s, NumberStyles style, IFormatProvider? provider) => throw new NotSupportedException();

        static float INumberBase<float>.Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider) => throw new NotSupportedException();

        static bool INumberBase<float>.TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, out float result) => throw new NotSupportedException();

        static bool INumberBase<float>.TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out float result) => throw new NotSupportedException();

        static float IParsable<float>.Parse(string s, IFormatProvider? provider) => throw new NotSupportedException();

        static bool IParsable<float>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out float result) => throw new NotSupportedException();

        static float ISpanParsable<float>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => throw new NotSupportedException();

        static bool ISpanParsable<float>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out float result) => throw new NotSupportedException();

        int IFloatingPoint<float>.GetExponentByteCount() => 1;

        int IFloatingPoint<float>.GetExponentShortestBitLength()
        {
            sbyte exponent = (sbyte)(((uint)(BitConverter.SingleToUInt32Bits(m_value) >> 23) & 0xFF) - 127);
            return exponent >= 0
                ? 8 - BitOperations.LeadingZeroCount((uint)exponent) + -24
                : 8 + 1 - BitOperations.LeadingZeroCount((uint)(byte)~exponent) + -24;
        }

        int IFloatingPoint<float>.GetSignificandBitLength() => 23 + 1;

        int IFloatingPoint<float>.GetSignificandByteCount() => 4;

        bool IFloatingPoint<float>.TryWriteExponentBigEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)(long)(sbyte)(((uint)(BitConverter.SingleToUInt32Bits(m_value) >> 23) & 0xFF) - 127), 1, bigEndian: true, destination, out bytesWritten);

        bool IFloatingPoint<float>.TryWriteExponentLittleEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)(long)(sbyte)(((uint)(BitConverter.SingleToUInt32Bits(m_value) >> 23) & 0xFF) - 127), 1, bigEndian: false, destination, out bytesWritten);

        bool IFloatingPoint<float>.TryWriteSignificandBigEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger(Significand(m_value), 4, bigEndian: true, destination, out bytesWritten);

        bool IFloatingPoint<float>.TryWriteSignificandLittleEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger(Significand(m_value), 4, bigEndian: false, destination, out bytesWritten);

        private static ulong Significand(float value)
        {
            uint bits = BitConverter.SingleToUInt32Bits(value);
            ulong trailing = bits & 0x007F_FFFFu;
            return (bits & 0x7F80_0000u) != 0 ? trailing | (1UL << 23) : trailing;
        }

        public static float Acos(float x) => MathF.Acos(x);

        public static float AcosPi(float x) => Acos(x) / MathF.PI;

        public static float Asin(float x) => MathF.Asin(x);

        public static float AsinPi(float x) => Asin(x) / MathF.PI;

        public static float Atan(float x) => MathF.Atan(x);

        public static float AtanPi(float x) => Atan(x) / MathF.PI;

        public static float Atan2(float y, float x) => MathF.Atan2(y, x);

        public static float Atan2Pi(float y, float x) => Atan2(y, x) / MathF.PI;

        public static float Tan(float x) => MathF.Tan(x);

        public static float SinPi(float x)
        {
            if (!IsFinite(x))
                return NaN;
            float r = MathF.IEEERemainder(x, 2);
            if (r == 0 || MathF.Abs(r) == 1)
                return IsNegative(x) ? -(float)0.0 : (float)0.0;
            if (MathF.Abs(r) == 0.5)
                return r > 0 ? 1 : -1;
            return MathF.Sin(r * MathF.PI);
        }

        public static float CosPi(float x)
        {
            if (!IsFinite(x))
                return NaN;
            float r = MathF.Abs(MathF.IEEERemainder(x, 2));
            if (r == 0.5)
                return 0;
            if (r == 0)
                return 1;
            if (r == 1)
                return -1;
            return MathF.Cos(r * MathF.PI);
        }

        public static float TanPi(float x)
        {
            if (!IsFinite(x))
                return NaN;
            float r = MathF.IEEERemainder(x, 1);
            if (r == 0)
                return IsNegative(x) ? -(float)0.0 : (float)0.0;
            if (MathF.Abs(r) == 0.5)
                return r > 0 ? PositiveInfinity : NegativeInfinity;
            return MathF.Tan(r * MathF.PI);
        }

        public static (float SinPi, float CosPi) SinCosPi(float x) => (SinPi(x), CosPi(x));

        public static float Sinh(float x) => MathF.Sinh(x);

        public static float Cosh(float x) => MathF.Cosh(x);

        public static float Tanh(float x) => MathF.Tanh(x);

        public static float Asinh(float x) => MathF.Asinh(x);

        public static float Acosh(float x) => MathF.Acosh(x);

        public static float Atanh(float x) => MathF.Atanh(x);

        public static float Log(float x) => MathF.Log(x);

        public static float Log(float x, float newBase) => MathF.Log(x, newBase);

        public static float Log2(float value) => MathF.Log2(value);

        public static float Log10(float x) => MathF.Log10(x);

        public static float Exp2(float x) => MathF.Pow(2, x);

        public static float Exp10(float x) => MathF.Pow(10, x);

        public static float Pow(float x, float y) => MathF.Pow(x, y);

        public static float Cbrt(float x) => MathF.Cbrt(x);

        public static float Hypot(float x, float y) => (float)Math.Hypot(x, y);

        public static float RootN(float x, int n)
        {
            if (n == 0 || (x < 0 && (n & 1) == 0))
                return NaN;
            float result = MathF.Pow(MathF.Abs(x), (float)1.0 / n);
            return IsNegative(x) && (n & 1) != 0 ? -result : result;
        }

        public static float ScaleB(float x, int n) => MathF.ScaleB(x, n);

        public static int ILogB(float x) => MathF.ILogB(x);

        public static float Ieee754Remainder(float left, float right) => MathF.IEEERemainder(left, right);

        public static float BitIncrement(float x) => MathF.BitIncrement(x);

        public static float BitDecrement(float x) => MathF.BitDecrement(x);

        public static bool IsPow2(float value)
        {
            uint bits = BitConverter.SingleToUInt32Bits(value);
            uint exponent = bits & 0x7F80_0000u;
            uint significand = bits & 0x007F_FFFFu;
            if (value <= 0 || exponent == 0x7F80_0000u)
                return false;
            return exponent == 0 ? BitOperations.PopCount(significand) == 1 : significand == 0;
        }

        static float IFloatingPointIeee754<float>.Epsilon => Epsilon;

        static float IFloatingPointIeee754<float>.NaN => NaN;

        static float IFloatingPointIeee754<float>.NegativeInfinity => NegativeInfinity;

        static float IFloatingPointIeee754<float>.NegativeZero => NegativeZero;

        static float IFloatingPointIeee754<float>.PositiveInfinity => PositiveInfinity;

        static float IBitwiseOperators<float, float, float>.operator &(float left, float right)
            => BitConverter.UInt32BitsToSingle(BitConverter.SingleToUInt32Bits(left) & BitConverter.SingleToUInt32Bits(right));

        static float IBitwiseOperators<float, float, float>.operator |(float left, float right)
            => BitConverter.UInt32BitsToSingle(BitConverter.SingleToUInt32Bits(left) | BitConverter.SingleToUInt32Bits(right));

        static float IBitwiseOperators<float, float, float>.operator ^(float left, float right)
            => BitConverter.UInt32BitsToSingle(BitConverter.SingleToUInt32Bits(left) ^ BitConverter.SingleToUInt32Bits(right));

        static float IBitwiseOperators<float, float, float>.operator ~(float value)
            => BitConverter.UInt32BitsToSingle(~BitConverter.SingleToUInt32Bits(value));
    }
    public struct Double
        : IComparable, ISpanFormattable, IComparable<double>, IEquatable<double>, IBinaryFloatingPointIeee754<double>, IMinMaxValue<double>
    {
        private readonly double m_value;
        public const double MinValue = -1.7976931348623157E+308;
        public const double MaxValue = 1.7976931348623157E+308;

        public const double Epsilon = 4.9406564584124654E-324;
        public const double NegativeInfinity = (double)-1.0 / (double)(0.0);
        public const double PositiveInfinity = (double)1.0 / (double)(0.0);
        public const double NaN = (double)0.0 / (double)0.0;

        internal const ulong SignMask = 0x8000_0000_0000_0000;
        internal const int SignShift = 63;
        internal const byte ShiftedSignMask = (byte)(SignMask >> SignShift);

        internal const ulong BiasedExponentMask = 0x7FF0_0000_0000_0000;
        internal const int BiasedExponentShift = 52;
        internal const int BiasedExponentLength = 11;
        internal const ushort ShiftedBiasedExponentMask = (ushort)(BiasedExponentMask >> BiasedExponentShift);

        internal const ulong TrailingSignificandMask = 0x000F_FFFF_FFFF_FFFF;

        internal const byte MinSign = 0;
        internal const byte MaxSign = 1;

        internal const ushort MinBiasedExponent = 0x0000;
        internal const ushort MaxBiasedExponent = 0x07FF;

        internal const ushort ExponentBias = 1023;

        internal const short MinExponent = -1022;
        internal const short MaxExponent = +1023;

        internal const ulong MinTrailingSignificand = 0x0000_0000_0000_0000;
        internal const ulong MaxTrailingSignificand = 0x000F_FFFF_FFFF_FFFF;

        internal const int TrailingSignificandLength = 52;
        internal const int SignificandLength = TrailingSignificandLength + 1;


        internal const ulong PositiveZeroBits = 0x0000_0000_0000_0000;
        internal const ulong NegativeZeroBits = 0x8000_0000_0000_0000;

        internal const ulong EpsilonBits = 0x0000_0000_0000_0001;

        internal const ulong PositiveInfinityBits = 0x7FF0_0000_0000_0000;
        internal const ulong NegativeInfinityBits = 0xFFF0_0000_0000_0000;

        internal const ulong SmallestNormalBits = 0x0010_0000_0000_0000;


        public override string ToString()
        {
            return System.Number.FormatDouble(m_value, null, null);
        }
        public string ToString(string? format)
        {
            return System.Number.FormatDouble(m_value, format, null);
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return (obj is double other) && Equals(other);
        }
        public bool Equals(double obj)
        {
            if (obj == m_value)
            {
                return true;
            }
            return IsNaN(obj) && IsNaN(m_value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(m_value);

            if (IsNaNOrZero(m_value))
            {
                // Ensure that all NaNs and both zeros have the same hash code
                bits &= PositiveInfinityBits;
            }

            return unchecked((int)bits) ^ ((int)(bits >> 32));
        }

        public static double Abs(double value) => Math.Abs(value);
        public static double Sqrt(double x) => Math.Sqrt(x);
        public static double Truncate(double x) => Math.Truncate(x);
        public static double Sin(double x) => Math.Sin(x);
        public static double Cos(double x) => Math.Cos(x);
        public static (double Sin, double Cos) SinCos(double x) => Math.SinCos(x);
        public static double Exp(double x) => Math.Exp(x);
        public static double FusedMultiplyAdd(double left, double right, double addend) => Math.FusedMultiplyAdd(left, right, addend);
        public static double MultiplyAddEstimate(double left, double right, double addend) => (left * right) + addend;
        public static TInteger ConvertToInteger<TInteger>(double value) where TInteger : IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);
        public static TInteger ConvertToIntegerNative<TInteger>(double value) where TInteger : IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite(double d)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(d);
            return (~bits & PositiveInfinityBits) != 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInfinity(double d)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(Abs(d));
            return bits == PositiveInfinityBits;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNaN(double d) => d != d;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPositiveInfinity(double d)
        {
            return d == PositiveInfinity;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNegativeInfinity(double d)
        {
            return d == NegativeInfinity;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNegative(double d)
        {
            return BitConverter.DoubleToInt64Bits(d) < 0;
        }
        public static bool IsPositive(double value) => BitConverter.DoubleToInt64Bits(value) >= 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNormal(double d)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(Abs(d));
            return (bits - SmallestNormalBits) < (PositiveInfinityBits - SmallestNormalBits);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSubnormal(double d)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(Abs(d));
            return (bits - 1) < MaxTrailingSignificand;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsZero(double d)
        {
            return d == 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsNaNOrZero(double d)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(d);
            return ((bits - 1) & ~SignMask) >= PositiveInfinityBits;
        }
        public static bool IsInteger(double value) => IsFinite(value) && (value == Truncate(value));
        public static bool IsEvenInteger(double value) => IsInteger(value) && (Abs(value % 2) == 0);
        public static bool IsOddInteger(double value) => IsInteger(value) && (Abs(value % 2) == 1);

    

        public const double E = 2.7182818284590452354;

        public const double Pi = 3.14159265358979323846;

        public const double Tau = 6.283185307179586476925;

        public int CompareTo(object? value)
        {
            if (value is null)
                return 1;
            if (value is double other)
                return CompareTo(other);
            throw new ArgumentException(SR.Arg_MustBeDouble);
        }

        public int CompareTo(double value)
        {
            if (m_value < value)
                return -1;
            if (m_value > value)
                return 1;
            if (m_value == value)
                return 0;
            if (IsNaN(m_value))
                return IsNaN(value) ? 0 : -1;
            return 1;
        }

        public string ToString(IFormatProvider? provider) => ToString(null, provider);

        public string ToString(string? format, IFormatProvider? provider) => Number.FormatDouble(m_value, format, null);

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
            => Number.TryCopyFormatted(ToString(format.IsEmpty ? null : new string(format), provider), destination, out charsWritten);

        public static bool IsRealNumber(double value) => !IsNaN(value);

        public static double Floor(double x) => Math.Floor(x);

        public static double Ceiling(double x) => Math.Ceiling(x);

        public static double Round(double x) => Math.Round(x);

        public static double Round(double x, int digits) => Math.Round(x, digits, MidpointRounding.ToEven);

        public static double Round(double x, MidpointRounding mode) => Math.Round(x, 0, mode);

        public static double Round(double x, int digits, MidpointRounding mode) => Math.Round(x, digits, mode);

        public static double Clamp(double value, double min, double max)
        {
            if (min > max)
                Math.ThrowMinMaxException(min, max);
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        public static double CopySign(double value, double sign) => Math.CopySign(value, sign);

        public static double CreateChecked<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Checked);

        public static double CreateSaturating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Saturating);

        public static double CreateTruncating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Truncating);

        private static double Create<TOther>(TOther value, NumericConversion.Mode mode) where TOther : INumberBase<TOther>
        {
            if (NumericConversion.TryConvertToDouble(value, out double result))
                return result;
            bool converted = mode switch
            {
                NumericConversion.Mode.Checked => TOther.TryConvertToChecked(value, out result),
                NumericConversion.Mode.Saturating => TOther.TryConvertToSaturating(value, out result),
                _ => TOther.TryConvertToTruncating(value, out result),
            };
            if (!converted)
                ThrowHelper.ThrowNotSupportedException();
            return result;
        }

        public static int Sign(double value)
        {
            if (value < 0)
                return -1;
            if (value > 0)
                return 1;
            if (value == 0)
                return 0;
            throw new ArithmeticException(SR.Arithmetic_NaN);
        }

        public static double Max(double x, double y)
        {
            if (x != y)
                return IsNaN(x) ? x : (y < x ? x : y);
            return IsNegative(y) ? x : y;
        }

        public static double Min(double x, double y)
        {
            if (x != y)
                return IsNaN(x) ? x : (x < y ? x : y);
            return IsNegative(x) ? x : y;
        }

        public static double MaxNumber(double x, double y)
        {
            if (x != y)
                return IsNaN(y) ? x : (y < x ? x : y);
            return IsNegative(y) ? x : y;
        }

        public static double MinNumber(double x, double y)
        {
            if (x != y)
                return IsNaN(y) ? x : (x < y ? x : y);
            return IsNegative(x) ? x : y;
        }

        public static double MaxMagnitude(double x, double y)
        {
            double ax = Math.Abs(x);
            double ay = Math.Abs(y);
            if (ax > ay || IsNaN(ax))
                return x;
            if (ax == ay)
                return IsNegative(x) ? y : x;
            return y;
        }

        public static double MinMagnitude(double x, double y)
        {
            double ax = Math.Abs(x);
            double ay = Math.Abs(y);
            if (ax < ay || IsNaN(ax))
                return x;
            if (ax == ay)
                return IsNegative(x) ? x : y;
            return y;
        }

        public static double MaxMagnitudeNumber(double x, double y)
        {
            double ax = Math.Abs(x);
            double ay = Math.Abs(y);
            if (ax > ay || IsNaN(ay))
                return x;
            if (ax == ay)
                return IsNegative(x) ? y : x;
            return y;
        }

        public static double MinMagnitudeNumber(double x, double y)
        {
            double ax = Math.Abs(x);
            double ay = Math.Abs(y);
            if (ax < ay || IsNaN(ay))
                return x;
            if (ax == ay)
                return IsNegative(x) ? x : y;
            return y;
        }

        static double IAdditionOperators<double, double, double>.operator +(double left, double right) => left + right;
        static double ISubtractionOperators<double, double, double>.operator -(double left, double right) => left - right;
        static double IMultiplyOperators<double, double, double>.operator *(double left, double right) => left * right;
        static double IDivisionOperators<double, double, double>.operator /(double left, double right) => left / right;
        static double IModulusOperators<double, double, double>.operator %(double left, double right) => left % right;
        static bool IEqualityOperators<double, double, bool>.operator ==(double left, double right) => left == right;
        static bool IEqualityOperators<double, double, bool>.operator !=(double left, double right) => left != right;
        static bool IComparisonOperators<double, double, bool>.operator <(double left, double right) => left < right;
        static bool IComparisonOperators<double, double, bool>.operator <=(double left, double right) => left <= right;
        static bool IComparisonOperators<double, double, bool>.operator >(double left, double right) => left > right;
        static bool IComparisonOperators<double, double, bool>.operator >=(double left, double right) => left >= right;
        static double IIncrementOperators<double>.operator ++(double value) => ++value;
        static double IDecrementOperators<double>.operator --(double value) => --value;
        static double IUnaryNegationOperators<double, double>.operator -(double value) => -value;
        static double IUnaryPlusOperators<double, double>.operator +(double value) => value;
        static double IAdditiveIdentity<double, double>.AdditiveIdentity => 0;
        static double IMultiplicativeIdentity<double, double>.MultiplicativeIdentity => 1;
        static double IMinMaxValue<double>.MinValue => MinValue;
        static double IMinMaxValue<double>.MaxValue => MaxValue;
        static double IFloatingPointConstants<double>.E => E;
        static double IFloatingPointConstants<double>.Pi => Pi;
        static double IFloatingPointConstants<double>.Tau => Tau;
        static double ISignedNumber<double>.NegativeOne => -1;
        static double INumberBase<double>.One => 1;
        static int INumberBase<double>.Radix => 2;
        static double INumberBase<double>.Zero => 0;
        static bool INumberBase<double>.IsCanonical(double value) => true;
        static bool INumberBase<double>.IsComplexNumber(double value) => false;
        static bool INumberBase<double>.IsImaginaryNumber(double value) => false;
        static bool INumberBase<double>.IsZero(double value) => value == 0;

        static bool INumberBase<double>.TryConvertFromChecked<TOther>(TOther value, out double result) => NumericConversion.TryConvertToDouble(value, out result);

        static bool INumberBase<double>.TryConvertFromSaturating<TOther>(TOther value, out double result) => NumericConversion.TryConvertToDouble(value, out result);

        static bool INumberBase<double>.TryConvertFromTruncating<TOther>(TOther value, out double result) => NumericConversion.TryConvertToDouble(value, out result);

        static bool INumberBase<double>.TryConvertToChecked<TOther>(double value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<double>.TryConvertToSaturating<TOther>(double value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<double>.TryConvertToTruncating<TOther>(double value, out TOther result)
        {
            result = default!;
            return false;
        }

        // Correctly rounded parsing of binary floating point is not implemented yet.
        static double INumberBase<double>.Parse(string s, NumberStyles style, IFormatProvider? provider) => throw new NotSupportedException();

        static double INumberBase<double>.Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider) => throw new NotSupportedException();

        static bool INumberBase<double>.TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, out double result) => throw new NotSupportedException();

        static bool INumberBase<double>.TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out double result) => throw new NotSupportedException();

        static double IParsable<double>.Parse(string s, IFormatProvider? provider) => throw new NotSupportedException();

        static bool IParsable<double>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out double result) => throw new NotSupportedException();

        static double ISpanParsable<double>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => throw new NotSupportedException();

        static bool ISpanParsable<double>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out double result) => throw new NotSupportedException();

        int IFloatingPoint<double>.GetExponentByteCount() => 2;

        int IFloatingPoint<double>.GetExponentShortestBitLength()
        {
            short exponent = (short)(((ulong)(BitConverter.DoubleToUInt64Bits(m_value) >> 52) & 0x7FF) - 1023);
            return exponent >= 0
                ? 16 - BitOperations.LeadingZeroCount((uint)exponent) + -16
                : 16 + 1 - BitOperations.LeadingZeroCount((uint)(ushort)~exponent) + -16;
        }

        int IFloatingPoint<double>.GetSignificandBitLength() => 52 + 1;

        int IFloatingPoint<double>.GetSignificandByteCount() => 8;

        bool IFloatingPoint<double>.TryWriteExponentBigEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)(long)(short)(((ulong)(BitConverter.DoubleToUInt64Bits(m_value) >> 52) & 0x7FF) - 1023), 2, bigEndian: true, destination, out bytesWritten);

        bool IFloatingPoint<double>.TryWriteExponentLittleEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)(long)(short)(((ulong)(BitConverter.DoubleToUInt64Bits(m_value) >> 52) & 0x7FF) - 1023), 2, bigEndian: false, destination, out bytesWritten);

        bool IFloatingPoint<double>.TryWriteSignificandBigEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger(Significand(m_value), 8, bigEndian: true, destination, out bytesWritten);

        bool IFloatingPoint<double>.TryWriteSignificandLittleEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger(Significand(m_value), 8, bigEndian: false, destination, out bytesWritten);

        private static ulong Significand(double value)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(value);
            ulong trailing = bits & 0x000F_FFFF_FFFF_FFFFUL;
            return (bits & 0x7FF0_0000_0000_0000UL) != 0 ? trailing | (1UL << 52) : trailing;
        }

        public const double NegativeZero = -0.0;

        public static double Acos(double x) => Math.Acos(x);

        public static double AcosPi(double x) => Acos(x) / Math.PI;

        public static double Asin(double x) => Math.Asin(x);

        public static double AsinPi(double x) => Asin(x) / Math.PI;

        public static double Atan(double x) => Math.Atan(x);

        public static double AtanPi(double x) => Atan(x) / Math.PI;

        public static double Atan2(double y, double x) => Math.Atan2(y, x);

        public static double Atan2Pi(double y, double x) => Atan2(y, x) / Math.PI;

        public static double Tan(double x) => Math.Tan(x);

        public static double SinPi(double x)
        {
            if (!IsFinite(x))
                return NaN;
            double r = Math.IEEERemainder(x, 2);
            if (r == 0 || Math.Abs(r) == 1)
                return IsNegative(x) ? -(double)0.0 : (double)0.0;
            if (Math.Abs(r) == 0.5)
                return r > 0 ? 1 : -1;
            return Math.Sin(r * Math.PI);
        }

        public static double CosPi(double x)
        {
            if (!IsFinite(x))
                return NaN;
            double r = Math.Abs(Math.IEEERemainder(x, 2));
            if (r == 0.5)
                return 0;
            if (r == 0)
                return 1;
            if (r == 1)
                return -1;
            return Math.Cos(r * Math.PI);
        }

        public static double TanPi(double x)
        {
            if (!IsFinite(x))
                return NaN;
            double r = Math.IEEERemainder(x, 1);
            if (r == 0)
                return IsNegative(x) ? -(double)0.0 : (double)0.0;
            if (Math.Abs(r) == 0.5)
                return r > 0 ? PositiveInfinity : NegativeInfinity;
            return Math.Tan(r * Math.PI);
        }

        public static (double SinPi, double CosPi) SinCosPi(double x) => (SinPi(x), CosPi(x));

        public static double Sinh(double x) => Math.Sinh(x);

        public static double Cosh(double x) => Math.Cosh(x);

        public static double Tanh(double x) => Math.Tanh(x);

        public static double Asinh(double x) => Math.Asinh(x);

        public static double Acosh(double x) => Math.Acosh(x);

        public static double Atanh(double x) => Math.Atanh(x);

        public static double Log(double x) => Math.Log(x);

        public static double Log(double x, double newBase) => Math.Log(x, newBase);

        public static double Log2(double value) => Math.Log2(value);

        public static double Log10(double x) => Math.Log10(x);

        public static double Exp2(double x) => Math.Pow(2, x);

        public static double Exp10(double x) => Math.Pow(10, x);

        public static double Pow(double x, double y) => Math.Pow(x, y);

        public static double Cbrt(double x) => Math.Cbrt(x);

        public static double Hypot(double x, double y) => (double)Math.Hypot(x, y);

        public static double RootN(double x, int n)
        {
            if (n == 0 || (x < 0 && (n & 1) == 0))
                return NaN;
            double result = Math.Pow(Math.Abs(x), (double)1.0 / n);
            return IsNegative(x) && (n & 1) != 0 ? -result : result;
        }

        public static double ScaleB(double x, int n) => Math.ScaleB(x, n);

        public static int ILogB(double x) => Math.ILogB(x);

        public static double Ieee754Remainder(double left, double right) => Math.IEEERemainder(left, right);

        public static double BitIncrement(double x) => Math.BitIncrement(x);

        public static double BitDecrement(double x) => Math.BitDecrement(x);

        public static bool IsPow2(double value)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(value);
            ulong exponent = bits & 0x7FF0_0000_0000_0000UL;
            ulong significand = bits & 0x000F_FFFF_FFFF_FFFFUL;
            if (value <= 0 || exponent == 0x7FF0_0000_0000_0000UL)
                return false;
            return exponent == 0 ? BitOperations.PopCount(significand) == 1 : significand == 0;
        }

        static double IFloatingPointIeee754<double>.Epsilon => Epsilon;

        static double IFloatingPointIeee754<double>.NaN => NaN;

        static double IFloatingPointIeee754<double>.NegativeInfinity => NegativeInfinity;

        static double IFloatingPointIeee754<double>.NegativeZero => NegativeZero;

        static double IFloatingPointIeee754<double>.PositiveInfinity => PositiveInfinity;

        static double IBitwiseOperators<double, double, double>.operator &(double left, double right)
            => BitConverter.UInt64BitsToDouble(BitConverter.DoubleToUInt64Bits(left) & BitConverter.DoubleToUInt64Bits(right));

        static double IBitwiseOperators<double, double, double>.operator |(double left, double right)
            => BitConverter.UInt64BitsToDouble(BitConverter.DoubleToUInt64Bits(left) | BitConverter.DoubleToUInt64Bits(right));

        static double IBitwiseOperators<double, double, double>.operator ^(double left, double right)
            => BitConverter.UInt64BitsToDouble(BitConverter.DoubleToUInt64Bits(left) ^ BitConverter.DoubleToUInt64Bits(right));

        static double IBitwiseOperators<double, double, double>.operator ~(double value)
            => BitConverter.UInt64BitsToDouble(~BitConverter.DoubleToUInt64Bits(value));
    }
    public struct Decimal
    {
        private readonly decimal m_value;
        public const decimal MaxValue = 79228162514264337593543950335m;
        public const decimal MinValue = -79228162514264337593543950335m;

        public override string ToString()
        {
            return System.Number.FormatDouble((double)m_value, null, null);
        }
    }

    public readonly struct Half
    {
        internal const ushort SignMask = 0x8000;
        internal const int SignShift = 15;
        internal const byte ShiftedSignMask = SignMask >> SignShift;

        internal const ushort BiasedExponentMask = 0x7C00;
        internal const int BiasedExponentShift = 10;
        internal const int BiasedExponentLength = 5;
        internal const byte ShiftedBiasedExponentMask = BiasedExponentMask >> BiasedExponentShift;

        internal const ushort TrailingSignificandMask = 0x03FF;

        internal const byte MinSign = 0;
        internal const byte MaxSign = 1;

        internal const byte MinBiasedExponent = 0x00;
        internal const byte MaxBiasedExponent = 0x1F;

        internal const byte ExponentBias = 15;

        internal const sbyte MinExponent = -14;
        internal const sbyte MaxExponent = +15;

        internal const ushort MinTrailingSignificand = 0x0000;
        internal const ushort MaxTrailingSignificand = 0x03FF;

        internal const int TrailingSignificandLength = 10;
        internal const int SignificandLength = TrailingSignificandLength + 1;

        // Constants representing the private bit-representation for various default values

        private const ushort PositiveZeroBits = 0x0000;
        private const ushort NegativeZeroBits = 0x8000;

        private const ushort EpsilonBits = 0x0001;

        private const ushort PositiveInfinityBits = 0x7C00;
        private const ushort NegativeInfinityBits = 0xFC00;

        private const ushort PositiveQNaNBits = 0x7E00;
        private const ushort NegativeQNaNBits = 0xFE00;

        private const ushort MinValueBits = 0xFBFF;
        private const ushort MaxValueBits = 0x7BFF;

        private const ushort PositiveOneBits = 0x3C00;
        private const ushort NegativeOneBits = 0xBC00;

        private const ushort SmallestNormalBits = 0x0400;

        private const ushort EBits = 0x4170;
        private const ushort PiBits = 0x4248;
        private const ushort TauBits = 0x4648;

        // Well-defined and commonly used values

        public static Half Epsilon => new Half(EpsilonBits);                        //  5.9604645E-08

        public static Half PositiveInfinity => new Half(PositiveInfinityBits);      //  1.0 / 0.0;

        public static Half NegativeInfinity => new Half(NegativeInfinityBits);      // -1.0 / 0.0

        public static Half NaN => new Half(NegativeQNaNBits);                       //  0.0 / 0.0

        public static Half MinValue => new Half(MinValueBits);                      // -65504

        public static Half MaxValue => new Half(MaxValueBits);                      //  65504

        internal readonly ushort _value;

        internal Half(ushort value)
        {
            _value = value;
        }

        private Half(bool sign, ushort exp, ushort sig) => _value = (ushort)(((sign ? 1 : 0) << SignShift) + (exp << BiasedExponentShift) + sig);
    }
    public readonly struct Int128
    {
        private readonly ulong _lower;
        private readonly ulong _upper;

        public Int128(ulong upper, ulong lower)
        {
            _lower = lower;
            _upper = upper;
        }

        internal ulong Lower => _lower;

        internal ulong Upper => _upper;

        public int CompareTo(Int128 value)
        {
            if (this < value)
            {
                return -1;
            }
            else if (this > value)
            {
                return 1;
            }
            else
            {
                return 0;
            }
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return (obj is Int128 other) && Equals(other);
        }

        public bool Equals(Int128 other)
        {
            return this == other;
        }

        public static bool operator ==(Int128 left, Int128 right) => (left._lower == right._lower) && (left._upper == right._upper);

        public static bool operator !=(Int128 left, Int128 right) => (left._lower != right._lower) || (left._upper != right._upper);

        public static Int128 operator &(Int128 left, Int128 right) => new Int128(left._upper & right._upper, left._lower & right._lower);

        public static Int128 operator |(Int128 left, Int128 right) => new Int128(left._upper | right._upper, left._lower | right._lower);

        public static Int128 operator ^(Int128 left, Int128 right) => new Int128(left._upper ^ right._upper, left._lower ^ right._lower);

        public static Int128 operator ~(Int128 value) => new Int128(~value._upper, ~value._lower);

        public static bool operator <(Int128 left, Int128 right)
        {
            // If left and right have different signs: Signed comparison of _upper gives result since it is stored as two's complement
            // If signs are equal and left._upper < right._upper: left < right for negative and positive values,
            //                                                    since _upper is upper 64 bits in two's complement.
            // If signs are equal and left._upper > right._upper: left > right for negative and positive values,
            //                                                    since _upper is upper 64 bits in two's complement.
            // If left._upper == right._upper: unsigned comparison of _lower gives the result for both negative and positive values since
            //                                 lower values are lower 64 bits in two's complement.
            return ((long)left._upper < (long)right._upper)
                || ((left._upper == right._upper) && (left._lower < right._lower));
        }

        public static bool operator <=(Int128 left, Int128 right)
        {
            return ((long)left._upper < (long)right._upper)
                || ((left._upper == right._upper) && (left._lower <= right._lower));
        }

        public static bool operator >(Int128 left, Int128 right)
        {
            return ((long)left._upper > (long)right._upper)
                || ((left._upper == right._upper) && (left._lower > right._lower));
        }

        public static bool operator >=(Int128 left, Int128 right)
        {
            return ((long)left._upper > (long)right._upper)
                || ((left._upper == right._upper) && (left._lower >= right._lower));
        }
    }
    public readonly struct UInt128
    {
        internal const int Size = 16;

        private readonly ulong _lower;
        private readonly ulong _upper;

        public UInt128(ulong upper, ulong lower)
        {
            _lower = lower;
            _upper = upper;
        }

        internal ulong Lower => _lower;

        internal ulong Upper => _upper;

        public int CompareTo(UInt128 value)
        {
            if (this < value)
            {
                return -1;
            }
            else if (this > value)
            {
                return 1;
            }
            else
            {
                return 0;
            }
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return (obj is UInt128 other) && Equals(other);
        }

        public bool Equals(UInt128 other)
        {
            return this == other;
        }

        public static explicit operator char(UInt128 value) => (char)value._lower;

        public static bool operator ==(UInt128 left, UInt128 right) => (left._lower == right._lower) && (left._upper == right._upper);

        public static bool operator !=(UInt128 left, UInt128 right) => (left._lower != right._lower) || (left._upper != right._upper);

        public static UInt128 operator &(UInt128 left, UInt128 right) => new UInt128(left._upper & right._upper, left._lower & right._lower);

        public static UInt128 operator |(UInt128 left, UInt128 right) => new UInt128(left._upper | right._upper, left._lower | right._lower);

        public static UInt128 operator ^(UInt128 left, UInt128 right) => new UInt128(left._upper ^ right._upper, left._lower ^ right._lower);

        public static UInt128 operator ~(UInt128 value) => new UInt128(~value._upper, ~value._lower);

        public static bool operator <(UInt128 left, UInt128 right)
        {
            return (left._upper < right._upper)
                || (left._upper == right._upper) && (left._lower < right._lower);
        }

        public static bool operator <=(UInt128 left, UInt128 right)
        {
            return (left._upper < right._upper)
                || (left._upper == right._upper) && (left._lower <= right._lower);
        }

        public static bool operator >(UInt128 left, UInt128 right)
        {
            return (left._upper > right._upper)
                || (left._upper == right._upper) && (left._lower > right._lower);
        }

        public static bool operator >=(UInt128 left, UInt128 right)
        {
            return (left._upper > right._upper)
                || (left._upper == right._upper) && (left._lower >= right._lower);
        }
    }
    internal static class FormattingHelpers
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CountDigits(ulong value)
        {
            // Map the log2(value) to a power of 10.
            ReadOnlySpan<byte> log2ToPow10 =
            [
                1,  1,  1,  2,  2,  2,  3,  3,  3,  4,  4,  4,  4,  5,  5,  5,
                6,  6,  6,  7,  7,  7,  7,  8,  8,  8,  9,  9,  9,  10, 10, 10,
                10, 11, 11, 11, 12, 12, 12, 13, 13, 13, 13, 14, 14, 14, 15, 15,
                15, 16, 16, 16, 16, 17, 17, 17, 18, 18, 18, 19, 19, 19, 19, 20
            ];

            nint elementOffset = log2ToPow10[(int)ulong.Log2(value)];

            // Read the associated power of 10.
            ReadOnlySpan<ulong> powersOf10 =
            [
                0, // unused entry to avoid needing to subtract
                0,
                10,
                100,
                1000,
                10000,
                100000,
                1000000,
                10000000,
                100000000,
                1000000000,
                10000000000,
                100000000000,
                1000000000000,
                10000000000000,
                100000000000000,
                1000000000000000,
                10000000000000000,
                100000000000000000,
                1000000000000000000,
                10000000000000000000,
            ];

            ulong powerOf10 = Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetReference<ulong>(powersOf10), elementOffset);

            // Return the number of digits based on the power of 10, shifted by 1
            // if it falls below the threshold.
            int index = (int)elementOffset;
            return index - (value < powerOf10 ? 1 : 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CountDigits(uint value)
        {
            ReadOnlySpan<long> table =
            [
                4294967296,
                8589934582,
                8589934582,
                8589934582,
                12884901788,
                12884901788,
                12884901788,
                17179868184,
                17179868184,
                17179868184,
                21474826480,
                21474826480,
                21474826480,
                21474826480,
                25769703776,
                25769703776,
                25769703776,
                30063771072,
                30063771072,
                30063771072,
                34349738368,
                34349738368,
                34349738368,
                34349738368,
                38554705664,
                38554705664,
                38554705664,
                41949672960,
                41949672960,
                41949672960,
                42949672960,
                42949672960,
            ];

            long tableValue = table[(int)uint.Log2(value)];
            return (int)((value + tableValue) >> 32);
        }

        // Counts the number of trailing '0' digits in a decimal number.
        // e.g., value =      0 => retVal = 0, valueWithoutTrailingZeros = 0
        //       value =   1234 => retVal = 0, valueWithoutTrailingZeros = 1234
        //       value = 320900 => retVal = 2, valueWithoutTrailingZeros = 3209
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CountDecimalTrailingZeros(uint value, out uint valueWithoutTrailingZeros)
        {
            int zeroCount = 0;

            if (value != 0)
            {
                while (true)
                {
                    uint temp = value / 10;
                    if (value != (temp * 10))
                    {
                        break;
                    }

                    value = temp;
                    zeroCount++;
                }
            }

            valueWithoutTrailingZeros = value;
            return zeroCount;
        }
    }
    internal static unsafe class Number
    {
        // We need 1 additional byte, per length, for the terminating null
        internal const int DecimalNumberBufferLength = 29 + 1 + 1;  // 29 for the longest input + 1 for rounding
        internal const int DoubleNumberBufferLength = 767 + 1 + 1;  // 767 for the longest input + 1 for rounding: 4.9406564584124654E-324
        internal const int Int32NumberBufferLength = 10 + 1;    // 10 for the longest input: 2,147,483,647
        internal const int Int64NumberBufferLength = 19 + 1;    // 19 for the longest input: 9,223,372,036,854,775,807
        internal const int Int128NumberBufferLength = 39 + 1;    // 39 for the longest input: 170,141,183,460,469,231,731,687,303,715,884,105,727
        internal const int SingleNumberBufferLength = 112 + 1 + 1;  // 112 for the longest input + 1 for rounding: 1.40129846E-45
        internal const int HalfNumberBufferLength = 21 + 1 + 1; // 21 for the longest input + 1 for rounding: 0.000122010707855224609375
        internal const int UInt32NumberBufferLength = 10 + 1;   // 10 for the longest input: 4,294,967,295
        internal const int UInt64NumberBufferLength = 20 + 1;   // 20 for the longest input: 18,446,744,073,709,551,615
        internal const int UInt128NumberBufferLength = 39 + 1; // 39 for the longest input: 340,282,366,920,938,463,463,374,607,431,768,211,455
        internal const int Decimal32NumberBufferLength = 7 + 1 + 1; // 7 for the longest input + 1 for rounding
        internal const int Decimal64NumberBufferLength = 16 + 1 + 1; // 16 for the longest input + 1 for rounding
        internal const int Decimal128NumberBufferLength = 34 + 1 + 1; // 34 for the longest input + 1 for rounding

        internal unsafe ref struct NumberBuffer
        {
            public int DigitsCount;
            public int Scale;
            public bool IsNegative;
            public bool HasNonZeroTail;
            public NumberBufferKind Kind;
            public Span<byte> Digits;
            /// <safety>Converts the ref to Digits into a pointer value via Unsafe.AsPointer and returns it without dereferencing; 
            /// the result is not GC-tracked, so any use must be in an unsafe context that establishes Digits still refers to unmovable memory.</safety>
            public readonly byte* DigitsPtr => 
                (byte*)Unsafe.AsPointer(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(Digits)); // safe since constructor expects Digits to refer to unmovable memory

            public NumberBuffer(NumberBufferKind kind, byte* digits, int digitsLength) : this(kind, new Span<byte>(digits, digitsLength))
            {

            }

            public NumberBuffer(NumberBufferKind kind, Span<byte> digits)
            {
                DigitsCount = 0;
                Scale = 0;
                IsNegative = false;
                HasNonZeroTail = false;
                Kind = kind;
                Digits = digits;
                Digits[0] = (byte)'\0';
            }
        }
        internal enum NumberBufferKind : byte
        {
            Unknown = 0,
            Integer = 1,
            Decimal = 2,
            FloatingPoint = 3,

            /// <summary>
            /// An IEEE 754 decimal interchange format. Unlike <see cref="NumberBufferKind.FloatingPoint"/> the buffer
            /// holds the exact coefficient rather than a pre-rounded shortest representation, so formatting must round
            /// it; unlike <see cref="NumberBufferKind.Decimal"/> that rounding is ties-to-even and a signed zero must
            /// survive it.
            /// </summary>
            DecimalIeee754 = 4,
        }

        internal const int DecimalPrecision = 29;
        private const int SmallNumberCacheLength = 300;

        private static ReadOnlySpan<byte> TwoDigitsCharsAsBytes =>
            System.Runtime.InteropServices.MemoryMarshal.AsBytes<char>("00010203040506070809" +
                                        "10111213141516171819" +
                                        "20212223242526272829" +
                                        "30313233343536373839" +
                                        "40414243444546474849" +
                                        "50515253545556575859" +
                                        "60616263646566676869" +
                                        "70717273747576777879" +
                                        "80818283848586878889" +
                                        "90919293949596979899");
        private static ReadOnlySpan<byte> TwoDigitsBytes =>
                                        "00010203040506070809"u8 +
                                        "10111213141516171819"u8 +
                                        "20212223242526272829"u8 +
                                        "30313233343536373839"u8 +
                                        "40414243444546474849"u8 +
                                        "50515253545556575859"u8 +
                                        "60616263646566676869"u8 +
                                        "70717273747576777879"u8 +
                                        "80818283848586878889"u8 +
                                        "90919293949596979899"u8;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ref byte GetTwoDigitsBytesRef(bool useChars) =>
            ref System.Runtime.InteropServices.MemoryMarshal.GetReference(useChars ? TwoDigitsCharsAsBytes : TwoDigitsBytes);


        internal enum ParseStatus : byte
        {
            OK = 0,
            Format = 1,
            Overflow = 2,
        }
        internal static string CharToString(char c)
        {
            string s = String.FastAllocateString(1);
            ref char dst = ref s.GetRawStringData();
            dst = c;
            return s;
        }
        private static bool TryParseHexFormat(string? format, out bool upperCase, out int precision)
        {
            upperCase = true;
            precision = 0;

            if (string.IsNullOrEmpty(format))
                return false;

            char specifier = format[0];
            if (specifier != 'X' && specifier != 'x')
                return false;

            upperCase = specifier == 'X';

            for (int i = 1; i < format.Length; i++)
            {
                char c = format[i];
                if (c < '0' || c > '9')
                    return false;

                int digit = c - '0';
                if (precision > (0x7fffffff - digit) / 10)
                    throw new FormatException("Format specifier precision is too large.");

                precision = precision * 10 + digit;
            }

            return true;
        }
        private static bool TryParseDecimalFormat(string? format, out int precision)
        {
            precision = 0;
            if (string.IsNullOrEmpty(format) || (format[0] != 'D' && format[0] != 'd'))
                return false;

            for (int i = 1; i < format.Length; i++)
            {
                char c = format[i];
                if (c < '0' || c > '9')
                    return false;

                int digit = c - '0';
                if (precision > (0x7fffffff - digit) / 10)
                    throw new FormatException("Format specifier precision is too large.");

                precision = precision * 10 + digit;
            }

            return true;
        }
        // "D<precision>" zero-extends the default digits to the precision, after any sign.
        private static string ZeroExtendDecimal(string digits, int precision)
        {
            int sign = digits.Length != 0 && digits[0] == '-' ? 1 : 0;
            int padding = precision - (digits.Length - sign);
            if (padding <= 0)
                return digits;
            return string.Concat(digits.Substring(0, sign), new string('0', padding), digits.Substring(sign));
        }
        private static int CountHexDigits64(ulong value)
        {
            int digits = 1;
            while ((value >>= 4) != 0ul)
                digits++;

            return digits;
        }
        public static string FormatInt32(int value, int hexMask, string? format, IFormatProvider? provider)
        {
            // Fast path for default format
            if (string.IsNullOrEmpty(format))
            {
                return Int32ToString(value);
            }
            if (TryParseHexFormat(format, out bool upperCase, out int precision))
            {
                uint hexValue = hexMask == 0 ? (uint)value : ((uint)value & (uint)hexMask);
                return UInt32ToHexString(hexValue, precision, upperCase);
            }
            if (TryParseDecimalFormat(format, out int decimalPrecision))
                return ZeroExtendDecimal(Int32ToString(value), decimalPrecision);
            throw new NotSupportedException($"format {format} not supported");
        }
        internal static unsafe string Int32ToString(int value)
        {
            if (value == unchecked((int)0x80000000))
                return "-2147483648";
            char* buffer = stackalloc char[12]; // sign + 10 digits + terminator
            char* p = buffer + 12;

            bool neg = value < 0;
            uint v = (uint)(neg ? -value : value);

            do
            {
                uint digit = v % 10u;
                v /= 10u;
                *--p = (char)('0' + digit);
            } while (v != 0u);

            if (neg) *--p = '-';

            int len = (int)((buffer + 12) - p);
            string s = String.FastAllocateString(len);
            ref char dst = ref s.GetRawStringData();

            for (int i = 0; i < len; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = p[i];

            return s;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // expose to caller's likely-const format to trim away slow path
        public static bool TryFormatUInt32<TChar>(
            uint value, ReadOnlySpan<char> format, IFormatProvider? provider, Span<TChar> destination, out int charsWritten) 
            where TChar : unmanaged, IUtfChar<TChar>
        {
            // Fast path for default format
            if (format.Length == 0)
            {
                return TryUInt32ToDecStr(value, destination, out charsWritten);
            }

            //return TryFormatUInt32Slow(value, format, provider, destination, out charsWritten);
            throw new NotSupportedException();
        }
        internal static unsafe bool TryUInt32ToDecStr<TChar>(uint value, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            int bufferLength = FormattingHelpers.CountDigits(value);
            if (bufferLength <= destination.Length)
            {
                charsWritten = bufferLength;
                fixed (TChar* buffer = &System.Runtime.InteropServices.MemoryMarshal.GetReference(destination))
                {
                    TChar* p = UInt32ToDecChars(buffer + bufferLength, value);
                }
                return true;
            }

            charsWritten = 0;
            return false;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe TChar* UInt32ToDecChars<TChar>(TChar* bufferEnd, uint value) where TChar : unmanaged, IUtfChar<TChar>
        {
            if (value >= 10)
            {
                // Handle all values >= 100 two-digits at a time so as to avoid expensive integer division operations.
                while (value >= 100)
                {
                    bufferEnd -= 2;
                    (value, uint remainder) = Math.DivRem(value, 100);
                    WriteTwoDigits(remainder, bufferEnd);
                }

                // If there are two digits remaining, store them.
                if (value >= 10)
                {
                    bufferEnd -= 2;
                    WriteTwoDigits(value, bufferEnd);
                    return bufferEnd;
                }
            }
            // Otherwise, store the single digit remaining.
            *(--bufferEnd) = TChar.CastFrom(value + '0');
            return bufferEnd;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void WriteTwoDigits<TChar>(uint value, TChar* ptr) where TChar : unmanaged, IUtfChar<TChar>
        {
            Unsafe.CopyBlockUnaligned(
                ref *(byte*)ptr,
                ref Unsafe.Add(ref GetTwoDigitsBytesRef(typeof(TChar) == typeof(char)), (uint)sizeof(TChar) * 2 * value),
                (uint)sizeof(TChar) * 2);
        }
        public static string FormatUInt32(uint value, string? format, IFormatProvider? provider)
        {
            // Fast path for default format
            if (string.IsNullOrEmpty(format))
            {
                return UInt32ToString(value);
            }
            if (TryParseHexFormat(format, out bool upperCase, out int precision))
            {
                return UInt32ToHexString(value, precision, upperCase);
            }
            if (TryParseDecimalFormat(format, out int decimalPrecision))
                return ZeroExtendDecimal(UInt32ToString(value), decimalPrecision);
            throw new NotSupportedException($"format {format} not supported");
        }
        internal static unsafe string UInt32ToString(uint value)
        {
            char* buffer = stackalloc char[11]; // 10 digits + terminator
            char* p = buffer + 11;

            uint v = value;
            do
            {
                uint digit = v % 10u;
                v /= 10u;
                *--p = (char)('0' + digit);
            } while (v != 0u);

            int len = (int)((buffer + 11) - p);
            string s = String.FastAllocateString(len);
            ref char dst = ref s.GetRawStringData();

            for (int i = 0; i < len; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = p[i];

            return s;
        }
        internal static string FormatInt64(long value, string? format, IFormatProvider? provider)
        {
            // Fast path for default format
            if (string.IsNullOrEmpty(format))
            {
                return Int64ToString(value);
            }
            if (TryParseHexFormat(format, out bool upperCase, out int precision))
            {
                return UInt64ToHexString((ulong)value, precision, upperCase);
            }
            if (TryParseDecimalFormat(format, out int decimalPrecision))
                return ZeroExtendDecimal(Int64ToString(value), decimalPrecision);
            throw new NotSupportedException($"format {format} not supported");
        }
        private static unsafe string Int64ToString(long value)
        {
            if (value == unchecked((long)0x8000000000000000)) // long.MinValue
                return "-9223372036854775808";

            char* buffer = stackalloc char[21]; // sign + 19 digits + terminator
            char* p = buffer + 21;

            bool neg = value < 0;
            ulong v = (ulong)(neg ? -value : value);

            do
            {
                ulong digit = v % 10ul;
                v /= 10ul;
                *--p = (char)('0' + digit);
            } while (v != 0ul);

            if (neg) *--p = '-';

            int len = (int)((buffer + 21) - p);
            string s = String.FastAllocateString(len);
            ref char dst = ref s.GetRawStringData();

            for (int i = 0; i < len; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = p[i];

            return s;
        }
        public static string FormatUInt64(ulong value, string? format, IFormatProvider? provider)
        {
            // Fast path for default format
            if (string.IsNullOrEmpty(format))
            {
                return UInt64ToString(value);
            }
            if (TryParseHexFormat(format, out bool upperCase, out int precision))
            {
                return UInt64ToHexString(value, precision, upperCase);
            }
            if (TryParseDecimalFormat(format, out int decimalPrecision))
                return ZeroExtendDecimal(UInt64ToString(value), decimalPrecision);
            throw new NotSupportedException($"format {format} not supported");
        }

        internal static unsafe string UInt64ToString(ulong value)
        {
            char* buffer = stackalloc char[21]; // 20 digits + terminator
            char* p = buffer + 21;

            do
            {
                ulong digit = value % 10ul;
                value /= 10ul;
                *--p = (char)('0' + digit);
            } while (value != 0ul);

            int len = (int)((buffer + 21) - p);
            string s = String.FastAllocateString(len);
            ref char dst = ref s.GetRawStringData();

            for (int i = 0; i < len; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = p[i];

            return s;
        }

        private static string UInt64ToHexString(ulong value, int precision, bool upperCase)
        {
            int digitCount = CountHexDigits64(value);
            int len = precision > digitCount ? precision : digitCount;

            string s = String.FastAllocateString(len);
            ref char dst = ref s.GetRawStringData();

            int i = len;
            char alphaBase = upperCase ? 'A' : 'a';
            ulong v = value;

            do
            {
                int digit = (int)(v & 0xFul);
                v >>= 4;

                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, --i) =
                    (char)(digit < 10 ? '0' + digit : alphaBase + (digit - 10));
            } while (v != 0ul);

            while (i > 0)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, --i) = '0';

            return s;
        }
        private static string UInt32ToHexString(uint value, int precision, bool upperCase)
        {
            return UInt64ToHexString((ulong)value, precision, upperCase);
        }
        internal const int FloatFormatBufferCharCount = 32;
        internal const int DoubleFormatBufferCharCount = 32;
        private static unsafe string StringFromCharBuffer(char* source, int length)
        {
            string s = String.FastAllocateString(length);
            ref char dst = ref s.GetRawStringData();
            for (int i = 0; i < length; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = source[i];
            return s;
        }

        private static unsafe int FormatUnsignedIntegerToBuffer(ulong value, bool negative, char* destination, int destinationLength)
        {
            char* buffer = stackalloc char[20];
            char* p = buffer + 20;

            do
            {
                ulong digit = value % 10UL;
                value /= 10UL;
                *--p = (char)('0' + digit);
            } while (value != 0UL);

            int digitCount = (int)((buffer + 20) - p);
            int length = digitCount + (negative ? 1 : 0);

            int pos = 0;
            if (negative)
                destination[pos++] = '-';

            for (int i = 0; i < digitCount; i++)
                destination[pos++] = p[i];

            return length;
        }
        internal static unsafe string FormatFloat(float value, string? format, System.Globalization.NumberFormatInfo? info)
        {
            char* buffer = stackalloc char[FloatFormatBufferCharCount];
            int length = FormatFloatToBuffer(value, format, info, buffer, FloatFormatBufferCharCount);
            return StringFromCharBuffer(buffer, length);
        }
        internal static unsafe int FormatFloatToBuffer(float value, string? format, System.Globalization.NumberFormatInfo? info, char* destination, int destinationLength)
        {
            uint bits = BitConverter.SingleToUInt32Bits(value);
            bool negative = (bits & 0x8000_0000U) != 0;
            uint absBits = bits & 0x7FFF_FFFFU;

            if ((absBits & 0x7F80_0000U) == 0x7F80_0000U)
            {
                if ((absBits & 0x007F_FFFFU) != 0)
                {
                    destination[0] = 'N';
                    destination[1] = 'a';
                    destination[2] = 'N';
                    return 3;
                }

                if (negative) //-Infinity
                {
                    destination[0] = '-';
                    destination[1] = 'I';
                    destination[2] = 'n';
                    destination[3] = 'f';
                    destination[4] = 'i';
                    destination[5] = 'n';
                    destination[6] = 'i';
                    destination[7] = 't';
                    destination[8] = 'y';
                    return 9;
                }
                else //Infinity
                {
                    destination[0] = 'I';
                    destination[1] = 'n';
                    destination[2] = 'f';
                    destination[3] = 'i';
                    destination[4] = 'n';
                    destination[5] = 'i';
                    destination[6] = 't';
                    destination[7] = 'y';
                    return 8;
                }
            }

            if (absBits == 0)
            {
                if (negative)
                {
                    destination[0] = '-';
                    destination[1] = '0';
                    return 2;
                }
                destination[0] = '0';
                return 1;
            }

            // Up to 2^24 every integer is a float and its own shortest round-trip digits.
            if (value >= -16777216.0f && value <= 16777216.0f)
            {
                int integerValue = (int)value;
                if ((float)integerValue == value)
                {
                    uint magnitude = (uint)(value < 0 ? -value : value);
                    return FormatUnsignedIntegerToBuffer(magnitude, value < 0, destination, destinationLength);
                }
            }

            return FormatShortestBinaryToBuffer(negative, absBits & 0x007F_FFFFU, (int)(absBits >> 23), mantissaBits: 23, exponentBias: 127, maxDigits: 9, destination, destinationLength);
        }

        private const int DoubleFormatBigUIntMaxWords = 48;

        private unsafe struct BigUIntScratch
        {
            public uint* Words;
            public int Length;
            public int Capacity;
        }
        internal static unsafe string FormatDouble(double value, string? format, System.Globalization.NumberFormatInfo? info)
        {
            char* buffer = stackalloc char[DoubleFormatBufferCharCount];
            int length = FormatDoubleToBuffer(value, format, info, buffer, DoubleFormatBufferCharCount);
            return StringFromCharBuffer(buffer, length);
        }
        internal static unsafe int FormatDoubleToBuffer(double value, string? format, System.Globalization.NumberFormatInfo? info, char* destination, int destinationLength)
        {
            const ulong SignMask = 0x8000_0000_0000_0000UL;
            const ulong MantissaMask = 0x000F_FFFF_FFFF_FFFFUL;
            const ulong ExponentMask = 0x7FF0_0000_0000_0000UL;
            const int MantissaBits = 52;
            const int ExponentBias = 1023;

            ulong bits = BitConverter.DoubleToUInt64Bits(value);
            bool negative = (bits & SignMask) != 0;
            ulong absBits = bits & ~SignMask;

            if ((absBits & ExponentMask) == ExponentMask)
            {
                if ((absBits & MantissaMask) != 0)
                {
                    destination[0] = 'N';
                    destination[1] = 'a';
                    destination[2] = 'N';
                    return 3;
                }

                if (negative) //-Infinity
                {
                    destination[0] = '-';
                    destination[1] = 'I';
                    destination[2] = 'n';
                    destination[3] = 'f';
                    destination[4] = 'i';
                    destination[5] = 'n';
                    destination[6] = 'i';
                    destination[7] = 't';
                    destination[8] = 'y';
                    return 9;
                }
                else //Infinity
                {
                    destination[0] = 'I';
                    destination[1] = 'n';
                    destination[2] = 'f';
                    destination[3] = 'i';
                    destination[4] = 'n';
                    destination[5] = 'i';
                    destination[6] = 't';
                    destination[7] = 'y';
                    return 8;
                }
            }

            if (absBits == 0)
            {
                if (negative)
                {
                    destination[0] = '-';
                    destination[1] = '0';
                    return 2;
                }
                destination[0] = '0';
                return 1;
            }

            // Up to 2^53 every integer is a double and its own shortest round-trip digits.
            if (value >= -9007199254740992.0 && value <= 9007199254740992.0)
            {
                long integerValue = (long)value;
                if ((double)integerValue == value)
                {
                    ulong magnitude = (ulong)(value < 0 ? -value : value);
                    return FormatUnsignedIntegerToBuffer(magnitude, value < 0, destination, destinationLength);
                }
            }

            ulong ieeeMantissa = bits & MantissaMask;
            int ieeeExponent = (int)((bits >> MantissaBits) & 0x7FFUL);
            return FormatShortestBinaryToBuffer(negative, ieeeMantissa, ieeeExponent, MantissaBits, ExponentBias, maxDigits: 17, destination, destinationLength);
        }

        // The shortest digits that round-trip at the format's precision: maxDigits correctly rounded digits always do,
        // then digits are dropped while a candidate stays inside the value's rounding interval.
        private static unsafe int FormatShortestBinaryToBuffer(
            bool negative, ulong ieeeMantissa, int ieeeExponent, int mantissaBits, int exponentBias, int maxDigits, char* destination, int destinationLength)
        {
            ulong mantissa;
            int binaryExponent;
            if (ieeeExponent == 0)
            {
                mantissa = ieeeMantissa;
                binaryExponent = 1 - exponentBias - mantissaBits;
            }
            else
            {
                mantissa = (1UL << mantissaBits) | ieeeMantissa;
                binaryExponent = ieeeExponent - exponentBias - mantissaBits;
            }

            int decimalExponent = ComputeDecimalExponent(mantissa, binaryExponent);
            int decimalScale = decimalExponent - (maxDigits - 1);

            ulong digitsLimit = 1UL;
            for (int i = 0; i < maxDigits; i++)
                digitsLimit *= 10UL;

            ulong digits = ComputeRoundedScaledDigits(mantissa, binaryExponent, decimalScale);
            if (digits >= digitsLimit)
            {
                digits /= 10UL;
                decimalScale++;
            }

            ulong boundaryValue = mantissa << 2;
            ulong upperBoundary = boundaryValue + 2UL;
            int lowerBoundaryShift = (ieeeMantissa != 0 || ieeeExponent <= 1) ? 1 : 0;
            ulong lowerBoundary = boundaryValue - 1UL - (ulong)lowerBoundaryShift;
            int boundaryExponent = binaryExponent - 2;
            bool acceptBoundary = (mantissa & 1UL) == 0;

            while (digits >= 10UL)
            {
                ulong q = digits / 10UL;
                int nextScale = decimalScale + 1;

                bool lowerCandidate = IsDecimalInRoundInterval(q, nextScale, lowerBoundary, upperBoundary, boundaryExponent, acceptBoundary);
                bool upperCandidate = IsDecimalInRoundInterval(q + 1UL, nextScale, lowerBoundary, upperBoundary, boundaryExponent, acceptBoundary);

                if (!lowerCandidate && !upperCandidate)
                    break;

                if (lowerCandidate && upperCandidate)
                {
                    ulong midpointDigits = checked(q * 2UL + 1UL);
                    int midpointCompare = -CompareDecimalToBinary(midpointDigits, nextScale, boundaryValue, boundaryExponent + 1);

                    if (midpointCompare < 0)
                    {
                        digits = q;
                    }
                    else if (midpointCompare > 0)
                    {
                        digits = q + 1UL;
                    }
                    else
                    {
                        digits = ((q & 1UL) == 0) ? q : q + 1UL;
                    }
                }
                else
                {
                    digits = lowerCandidate ? q : q + 1UL;
                }

                decimalScale = nextScale;
            }

            return FormatShortestDoubleToBuffer(negative, digits, decimalScale, maxDigits, destination, destinationLength);
        }
        private static int ComputeDecimalExponent(ulong mantissa, int binaryExponent)
        {
            int binaryFloorExponent = binaryExponent + BitLength(mantissa) - 1;
            int decimalExponent = FloorLog10Pow2(binaryFloorExponent);

            while (ComparePositiveBinaryFloatToPowerOf10(mantissa, binaryExponent, decimalExponent + 1) >= 0)
                decimalExponent++;

            while (ComparePositiveBinaryFloatToPowerOf10(mantissa, binaryExponent, decimalExponent) < 0)
                decimalExponent--;

            return decimalExponent;
        }
        private static int FloorLog10Pow2(int exponent)
        {
            return (int)(((long)exponent * 78913L) >> 18);
        }
        private static int BitLength(ulong value)
        {
            int length = 0;
            while (value != 0)
            {
                length++;
                value >>= 1;
            }
            return length;
        }
        private static unsafe ulong ComputeRoundedScaledDigits(ulong mantissa, int binaryExponent, int decimalScale)
        {
            uint* numeratorStorage = stackalloc uint[DoubleFormatBigUIntMaxWords * 4];

            BigUIntScratch numerator = CreateBigUIntScratch(numeratorStorage);
            BigUIntScratch denominator = CreateBigUIntScratch(numeratorStorage + DoubleFormatBigUIntMaxWords);
            BigUIntScratch remainder = CreateBigUIntScratch(numeratorStorage + DoubleFormatBigUIntMaxWords * 2);
            BigUIntScratch temp = CreateBigUIntScratch(numeratorStorage + DoubleFormatBigUIntMaxWords * 3);

            BigUIntSetUInt64(ref numerator, mantissa);
            if (binaryExponent >= 0)
            {
                BigUIntShiftLeft(ref numerator, binaryExponent);
            }
            else if (decimalScale < 0)
            {
                BigUIntMultiplyPow10(ref numerator, -decimalScale);
                int denominatorShift = -binaryExponent;
                ulong pow2Result = BigUIntDivRemPow2ToUInt64(ref numerator, denominatorShift, ref remainder);
                int pow2Cmp = BigUIntCompareTwiceToPowerOfTwo(ref remainder, denominatorShift);
                if (pow2Cmp > 0 || (pow2Cmp == 0 && (pow2Result & 1UL) != 0))
                    pow2Result++;
                return pow2Result;
            }
            BigUIntSetUInt64(ref denominator, 1UL);

            if (binaryExponent < 0)
                BigUIntShiftLeft(ref denominator, -binaryExponent);

            if (decimalScale >= 0)
                BigUIntMultiplyPow10(ref denominator, decimalScale);
            else
                BigUIntMultiplyPow10(ref numerator, -decimalScale);

            ulong result = BigUIntDivRemToUInt64(ref numerator, ref denominator, ref remainder, ref temp);
            BigUIntShiftLeft(ref remainder, 1);

            int cmp = BigUIntCompare(ref remainder, ref denominator);
            if (cmp > 0 || (cmp == 0 && (result & 1UL) != 0))
                result++;

            return result;
        }
        private static bool IsDecimalInRoundInterval(
            ulong decimalDigits,
            int decimalScale,
            ulong lowerBoundary,
            ulong upperBoundary,
            int binaryBoundaryExponent,
            bool acceptBoundary)
        {
            int lowerCmp = CompareDecimalToBinary(decimalDigits, decimalScale, lowerBoundary, binaryBoundaryExponent);
            if (acceptBoundary)
            {
                if (lowerCmp < 0)
                    return false;
            }
            else
            {
                if (lowerCmp <= 0)
                    return false;
            }

            int upperCmp = CompareDecimalToBinary(decimalDigits, decimalScale, upperBoundary, binaryBoundaryExponent);
            if (acceptBoundary)
                return upperCmp <= 0;

            return upperCmp < 0;
        }
        private static unsafe int ComparePositiveBinaryFloatToPowerOf10(ulong mantissa, int binaryExponent, int decimalExponent)
        {
            uint* storage = stackalloc uint[DoubleFormatBigUIntMaxWords * 2];

            BigUIntScratch left = CreateBigUIntScratch(storage);
            BigUIntScratch right = CreateBigUIntScratch(storage + DoubleFormatBigUIntMaxWords);

            BigUIntSetUInt64(ref left, mantissa);
            BigUIntSetUInt64(ref right, 1UL);

            if (decimalExponent >= 0)
            {
                if (binaryExponent >= 0)
                {
                    BigUIntShiftLeft(ref left, binaryExponent);
                    BigUIntMultiplyPow10(ref right, decimalExponent);
                }
                else
                {
                    BigUIntMultiplyPow10(ref right, decimalExponent);
                    BigUIntShiftLeft(ref right, -binaryExponent);
                }
            }
            else
            {
                BigUIntMultiplyPow10(ref left, -decimalExponent);
                if (binaryExponent >= 0)
                    BigUIntShiftLeft(ref left, binaryExponent);
                else
                    BigUIntShiftLeft(ref right, -binaryExponent);
            }

            return BigUIntCompare(ref left, ref right);
        }
        private static unsafe int CompareDecimalToBinary(ulong decimalDigits, int decimalScale, ulong binaryMantissa, int binaryExponent)
        {
            uint* storage = stackalloc uint[DoubleFormatBigUIntMaxWords * 2];

            BigUIntScratch left = CreateBigUIntScratch(storage);
            BigUIntScratch right = CreateBigUIntScratch(storage + DoubleFormatBigUIntMaxWords);

            BigUIntSetUInt64(ref left, decimalDigits);
            BigUIntSetUInt64(ref right, binaryMantissa);

            if (decimalScale >= 0)
                BigUIntMultiplyPow10(ref left, decimalScale);
            else
                BigUIntMultiplyPow10(ref right, -decimalScale);

            if (binaryExponent >= 0)
                BigUIntShiftLeft(ref right, binaryExponent);
            else
                BigUIntShiftLeft(ref left, -binaryExponent);

            return BigUIntCompare(ref left, ref right);
        }
        private static unsafe BigUIntScratch CreateBigUIntScratch(uint* storage)
            => new BigUIntScratch
            {
                Words = storage,
                Length = 0,
                Capacity = DoubleFormatBigUIntMaxWords,
            };
        private static unsafe void BigUIntSetUInt64(ref BigUIntScratch value, ulong source)
        {
            BigUIntClear(ref value);

            if (source == 0UL)
                return;

            value.Words[0] = (uint)source;
            uint hi = (uint)(source >> 32);
            if (hi != 0U)
            {
                BigUIntEnsureCapacity(ref value, 2);
                value.Words[1] = hi;
                value.Length = 2;
            }
            else
            {
                value.Length = 1;
            }
        }
        private static unsafe void BigUIntClear(ref BigUIntScratch value)
        {
            for (int i = 0; i < value.Length; i++)
                value.Words[i] = 0U;

            value.Length = 0;
        }
        private static unsafe void BigUIntCopy(ref BigUIntScratch destination, ref BigUIntScratch source)
        {
            BigUIntClear(ref destination);
            BigUIntEnsureCapacity(ref destination, source.Length);

            for (int i = 0; i < source.Length; i++)
                destination.Words[i] = source.Words[i];

            destination.Length = source.Length;
        }
        private static unsafe void BigUIntEnsureCapacity(ref BigUIntScratch value, int required)
        {
            if (required > value.Capacity)
                throw new OverflowException();
        }
        private static unsafe void BigUIntNormalize(ref BigUIntScratch value)
        {
            while (value.Length > 0 && value.Words[value.Length - 1] == 0U)
                value.Length--;
        }
        private static unsafe int BigUIntBitLength(ref BigUIntScratch value)
        {
            BigUIntNormalize(ref value);
            if (value.Length == 0)
                return 0;

            uint top = value.Words[value.Length - 1];
            int bits = 32;
            while ((top & 0x8000_0000U) == 0U)
            {
                bits--;
                top <<= 1;
            }

            return (value.Length - 1) * 32 + bits;
        }
        private static unsafe int BigUIntCompare(ref BigUIntScratch left, ref BigUIntScratch right)
        {
            BigUIntNormalize(ref left);
            BigUIntNormalize(ref right);

            if (left.Length != right.Length)
                return left.Length < right.Length ? -1 : 1;

            for (int i = left.Length - 1; i >= 0; i--)
            {
                uint l = left.Words[i];
                uint r = right.Words[i];
                if (l != r)
                    return l < r ? -1 : 1;
            }

            return 0;
        }
        private static unsafe void BigUIntShiftLeft(ref BigUIntScratch value, int shift)
        {
            if (shift == 0 || value.Length == 0)
                return;

            if (shift < 0)
                throw new ArgumentOutOfRangeException("shift");

            int wordShift = shift >> 5;
            int bitShift = shift & 31;

            if (bitShift != 0)
            {
                BigUIntEnsureCapacity(ref value, value.Length + 1);

                int carryShift = 32 - bitShift;
                uint carry = 0U;
                for (int i = 0; i < value.Length; i++)
                {
                    uint current = value.Words[i];
                    value.Words[i] = (current << bitShift) | carry;
                    carry = current >> carryShift;
                }

                if (carry != 0U)
                    value.Words[value.Length++] = carry;
            }

            if (wordShift != 0)
            {
                BigUIntEnsureCapacity(ref value, value.Length + wordShift);

                for (int i = value.Length - 1; i >= 0; i--)
                    value.Words[i + wordShift] = value.Words[i];

                for (int i = 0; i < wordShift; i++)
                    value.Words[i] = 0U;

                value.Length += wordShift;
            }
        }
        private static unsafe void BigUIntTruncateToLowBits(ref BigUIntScratch value, int bitCount)
        {
            if (bitCount <= 0)
            {
                BigUIntClear(ref value);
                return;
            }

            int keepLength = (bitCount + 31) >> 5;
            if (keepLength < value.Length)
            {
                for (int i = keepLength; i < value.Length; i++)
                    value.Words[i] = 0U;

                value.Length = keepLength;
            }

            int usedBitsInTopWord = bitCount & 31;
            if (usedBitsInTopWord != 0 && value.Length != 0)
            {
                uint mask = (1U << usedBitsInTopWord) - 1U;
                value.Words[value.Length - 1] &= mask;
            }

            BigUIntNormalize(ref value);
        }
        private static unsafe void BigUIntMultiplyByUInt32(ref BigUIntScratch value, uint multiplier)
        {
            BigUIntNormalize(ref value);

            if (value.Length == 0 || multiplier == 1U)
                return;

            if (multiplier == 0U)
            {
                BigUIntClear(ref value);
                return;
            }

            BigUIntEnsureCapacity(ref value, value.Length + 1);

            ulong carry = 0UL;
            for (int i = 0; i < value.Length; i++)
            {
                ulong product = (ulong)value.Words[i] * multiplier + carry;
                value.Words[i] = (uint)product;
                carry = product >> 32;
            }

            if (carry != 0UL)
                value.Words[value.Length++] = (uint)carry;
        }
        private static void BigUIntMultiplyPow10(ref BigUIntScratch value, int exponent)
        {
            if (exponent < 0)
                throw new ArgumentOutOfRangeException("exponent");

            while (exponent >= 9)
            {
                BigUIntMultiplyByUInt32(ref value, 1000000000U);
                exponent -= 9;
            }

            if (exponent != 0)
                BigUIntMultiplyByUInt32(ref value, Pow10UInt32(exponent));
        }
        private static uint Pow10UInt32(int exponent)
        {
            switch (exponent)
            {
                case 0: return 1U;
                case 1: return 10U;
                case 2: return 100U;
                case 3: return 1000U;
                case 4: return 10000U;
                case 5: return 100000U;
                case 6: return 1000000U;
                case 7: return 10000000U;
                case 8: return 100000000U;
                case 9: return 1000000000U;
                default: throw new ArgumentOutOfRangeException("exponent");
            }
        }
        private static unsafe void BigUIntSubtract(ref BigUIntScratch left, ref BigUIntScratch right)
        {
            ulong borrow = 0UL;
            int rightLength = right.Length;

            for (int i = 0; i < left.Length; i++)
            {
                ulong subtrahend = (i < rightLength ? right.Words[i] : 0UL) + borrow;
                ulong minuend = left.Words[i];
                left.Words[i] = (uint)(minuend - subtrahend);
                borrow = minuend < subtrahend ? 1UL : 0UL;
            }

            if (borrow != 0UL)
                throw new InvalidOperationException();

            BigUIntNormalize(ref left);
        }
        private static unsafe ulong BigUIntDivRemPow2ToUInt64(ref BigUIntScratch numerator, int denominatorShift, ref BigUIntScratch remainder)
        {
            if (denominatorShift < 0)
                throw new ArgumentOutOfRangeException("denominatorShift");

            BigUIntCopy(ref remainder, ref numerator);
            BigUIntTruncateToLowBits(ref remainder, denominatorShift);

            int wordShift = denominatorShift >> 5;
            int bitShift = denominatorShift & 31;
            if (wordShift >= numerator.Length)
                return 0UL;

            ulong quotient = 0UL;
            int quotientWords = numerator.Length - wordShift;
            for (int i = 0; i < quotientWords; i++)
            {
                int sourceIndex = wordShift + i;
                uint word = bitShift == 0
                    ? numerator.Words[sourceIndex]
                    : numerator.Words[sourceIndex] >> bitShift;

                if (bitShift != 0 && sourceIndex + 1 < numerator.Length)
                    word |= numerator.Words[sourceIndex + 1] << (32 - bitShift);

                if (i < 2)
                {
                    quotient |= (ulong)word << (i * 32);
                }
                else if (word != 0U)
                {
                    throw new OverflowException();
                }
            }

            return quotient;
        }
        private static unsafe int BigUIntCompareTwiceToPowerOfTwo(ref BigUIntScratch value, int powerOfTwoExponent)
        {
            if (powerOfTwoExponent < 0)
                throw new ArgumentOutOfRangeException("powerOfTwoExponent");

            BigUIntNormalize(ref value);
            if (value.Length == 0)
                return -1;

            if (powerOfTwoExponent == 0)
                return 1;
            int bitLength = BigUIntBitLength(ref value);
            if (bitLength < powerOfTwoExponent)
                return -1;
            if (bitLength > powerOfTwoExponent)
                return 1;

            return BigUIntIsSingleBitSet(ref value, powerOfTwoExponent - 1) ? 0 : 1;
        }
        private static unsafe bool BigUIntIsSingleBitSet(ref BigUIntScratch value, int bitIndex)
        {
            if (bitIndex < 0)
                return false;

            int wordIndex = bitIndex >> 5;
            int bitInWord = bitIndex & 31;
            if (value.Length != wordIndex + 1)
                return false;

            if (value.Words[wordIndex] != (1U << bitInWord))
                return false;

            for (int i = 0; i < wordIndex; i++)
            {
                if (value.Words[i] != 0U)
                    return false;
            }

            return true;
        }
        private static unsafe ulong BigUIntDivRemToUInt64(
            ref BigUIntScratch numerator,
            ref BigUIntScratch denominator,
            ref BigUIntScratch remainder,
            ref BigUIntScratch shiftedDenominator)
        {
            if (denominator.Length == 0)
                throw new DivideByZeroException();

            BigUIntCopy(ref remainder, ref numerator);

            if (BigUIntCompare(ref remainder, ref denominator) < 0)
                return 0UL;

            int maxShift = BigUIntBitLength(ref remainder) - BigUIntBitLength(ref denominator);
            if (maxShift >= 64)
                throw new OverflowException();

            BigUIntCopy(ref shiftedDenominator, ref denominator);
            BigUIntShiftLeft(ref shiftedDenominator, maxShift);

            ulong quotient = 0UL;
            for (int shift = maxShift; shift >= 0; shift--)
            {
                if (BigUIntCompare(ref remainder, ref shiftedDenominator) >= 0)
                {
                    BigUIntSubtract(ref remainder, ref shiftedDenominator);
                    quotient |= 1UL << shift;
                }
                if (shift != 0 && shiftedDenominator.Length != 0)
                {
                    uint carry = 0U;
                    for (int i = shiftedDenominator.Length - 1; i >= 0; i--)
                    {
                        uint current = shiftedDenominator.Words[i];
                        shiftedDenominator.Words[i] = (current >> 1) | (carry << 31);
                        carry = current & 1U;
                    }
                    BigUIntNormalize(ref shiftedDenominator);
                }
            }

            return quotient;
        }
        private static unsafe int FormatShortestDoubleToBuffer(bool negative, ulong digits, int decimalScale, int maxDigits, char* destination, int destinationLength)
        {
            char* digitBuffer = stackalloc char[24];
            int digitCount = UInt64ToDecimalDigits(digits, digitBuffer + 24);
            char* digitStart = digitBuffer + 24 - digitCount;

            // Fixed notation up to the round-trip digit count, as runtime's "R" formatting does.
            int scientificExponent = digitCount + decimalScale - 1;
            if (scientificExponent >= -4 && scientificExponent < maxDigits)
                return FormatFixedDecimalToBuffer(negative, digitStart, digitCount, decimalScale, destination, destinationLength);

            return FormatScientificDecimalToBuffer(negative, digitStart, digitCount, scientificExponent, destination, destinationLength);
        }
        private static unsafe int UInt64ToDecimalDigits(ulong value, char* end)
        {
            char* p = end;
            ulong v = value;
            do
            {
                ulong digit = v % 10UL;
                v /= 10UL;
                *--p = (char)('0' + digit);
            } while (v != 0UL);

            return (int)(end - p);
        }
        private static unsafe int FormatFixedDecimalToBuffer(bool negative, char* digits, int digitCount, int decimalScale, char* destination, int destinationLength)
        {
            int decimalPoint = digitCount + decimalScale;
            int signLength = negative ? 1 : 0;
            int length;

            if (decimalPoint <= 0)
                length = signLength + 2 + (-decimalPoint) + digitCount;
            else if (decimalPoint >= digitCount)
                length = signLength + decimalPoint;
            else
                length = signLength + digitCount + 1;

            int pos = 0;

            if (negative)
                destination[pos++] = '-';

            if (decimalPoint <= 0)
            {
                destination[pos++] = '0';
                destination[pos++] = '.';

                int zeroCount = -decimalPoint;
                for (int i = 0; i < zeroCount; i++)
                    destination[pos++] = '0';

                for (int i = 0; i < digitCount; i++)
                    destination[pos++] = digits[i];

                return length;
            }

            if (decimalPoint >= digitCount)
            {
                for (int i = 0; i < digitCount; i++)
                    destination[pos++] = digits[i];

                for (int i = digitCount; i < decimalPoint; i++)
                    destination[pos++] = '0';

                return length;
            }

            for (int i = 0; i < decimalPoint; i++)
                destination[pos++] = digits[i];

            destination[pos++] = '.';

            for (int i = decimalPoint; i < digitCount; i++)
                destination[pos++] = digits[i];

            return length;
        }
        private static unsafe int FormatScientificDecimalToBuffer(bool negative, char* digits, int digitCount, int scientificExponent, char* destination, int destinationLength)
        {
            char* exponentBuffer = stackalloc char[8];
            uint exponentMagnitude = scientificExponent < 0 ? (uint)(-scientificExponent) : (uint)scientificExponent;
            int exponentMagnitudeDigitCount = UInt32ToDecimalDigits(exponentMagnitude, exponentBuffer + 8);
            int exponentDigitCount = exponentMagnitudeDigitCount < 2 ? 2 : exponentMagnitudeDigitCount;

            int signLength = negative ? 1 : 0;
            int significandLength = digitCount == 1 ? 1 : digitCount + 1;
            int length = signLength + significandLength + 2 + exponentDigitCount;

            int pos = 0;

            if (negative)
                destination[pos++] = '-';

            destination[pos++] = digits[0];
            if (digitCount != 1)
            {
                destination[pos++] = '.';
                for (int i = 1; i < digitCount; i++)
                    destination[pos++] = digits[i];
            }

            destination[pos++] = 'E';
            destination[pos++] = scientificExponent < 0 ? '-' : '+';

            int leadingZeroCount = exponentDigitCount - exponentMagnitudeDigitCount;
            for (int i = 0; i < leadingZeroCount; i++)
                destination[pos++] = '0';

            char* exponentStart = exponentBuffer + 8 - exponentMagnitudeDigitCount;
            for (int i = 0; i < exponentMagnitudeDigitCount; i++)
                destination[pos++] = exponentStart[i];

            return length;
        }
        private static unsafe int UInt32ToDecimalDigits(uint value, char* end)
        {
            char* p = end;
            uint v = value;
            do
            {
                uint digit = v % 10U;
                v /= 10U;
                *--p = (char)('0' + digit);
            } while (v != 0U);

            return (int)(end - p);
        }
        private static string FormatExponent(int exponent)
        {
            bool negative = exponent < 0;
            uint magnitude = negative ? (uint)(-exponent) : (uint)exponent;
            string digits = UInt32ToString(magnitude);

            if (magnitude < 10U)
                digits = "0" + digits;

            return negative ? ("E-" + digits) : ("E+" + digits);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SkipWhiteSpace(ref char p, int i, int len)
        {
            while (i < len && Char.IsWhiteSpace(System.Runtime.CompilerServices.Unsafe.Add<char>(ref p, i)))
                i++;
            return i;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SkipWhiteSpace(ReadOnlySpan<char> s, int i)
        {
            int len = s.Length;
            while (i < len && Char.IsWhiteSpace(s[i]))
                i++;
            return i;
        }

        internal static ParseStatus TryParseInt32(string s, out int result)
        {
            result = 0;
            if ((object)s == null) return ParseStatus.Format;
            return TryParseInt32(s.AsSpan(), out result);
        }

        internal static ParseStatus TryParseUInt32(string s, out uint result)
        {
            result = 0;
            if ((object)s == null) return ParseStatus.Format;
            return TryParseUInt32(s.AsSpan(), out result);
        }

        internal static ParseStatus TryParseInt64(string s, out long result)
        {
            result = 0;
            if ((object)s == null) return ParseStatus.Format;
            return TryParseInt64(s.AsSpan(), out result);
        }

        internal static ParseStatus TryParseUInt64(string s, out ulong result)
        {
            result = 0;
            if ((object)s == null) return ParseStatus.Format;
            return TryParseUInt64(s.AsSpan(), out result);
        }

        internal static ParseStatus TryParseInt16(string s, out short result)
        {
            result = 0;
            if ((object)s == null) return ParseStatus.Format;
            return TryParseInt16(s.AsSpan(), out result);
        }

        internal static ParseStatus TryParseUInt16(string s, out ushort result)
        {
            result = 0;
            if ((object)s == null) return ParseStatus.Format;
            return TryParseUInt16(s.AsSpan(), out result);
        }

        internal static ParseStatus TryParseSByte(string s, out sbyte result)
        {
            result = 0;
            if ((object)s == null) return ParseStatus.Format;
            return TryParseSByte(s.AsSpan(), out result);
        }

        internal static ParseStatus TryParseByte(string s, out byte result)
        {
            result = 0;
            if ((object)s == null) return ParseStatus.Format;
            return TryParseByte(s.AsSpan(), out result);
        }

        internal static ParseStatus TryParseInt32(ReadOnlySpan<char> s, out int result)
        {
            result = 0;

            int len = s.Length;
            if (len == 0) return ParseStatus.Format;

            int i = SkipWhiteSpace(s, 0);
            if (i >= len) return ParseStatus.Format;

            bool neg = false;
            char c = s[i];
            if (c == '+' || c == '-')
            {
                neg = (c == '-');
                i++;
                if (i >= len) return ParseStatus.Format;
            }

            uint limit = neg ? 2147483648u : 2147483647u;
            uint acc = 0;
            bool any = false;

            while (i < len)
            {
                c = s[i];
                uint digit = (uint)(c - '0');
                if (digit > 9u) break;

                any = true;

                if (acc > (limit - digit) / 10u)
                    return ParseStatus.Overflow;

                acc = acc * 10u + digit;
                i++;
            }

            if (!any) return ParseStatus.Format;

            i = SkipWhiteSpace(s, i);
            if (i != len) return ParseStatus.Format;

            if (neg)
            {
                if (acc == 2147483648u)
                    result = unchecked((int)0x80000000);
                else
                    result = -(int)acc;
            }
            else
            {
                result = (int)acc;
            }

            return ParseStatus.OK;
        }

        internal static ParseStatus TryParseUInt32(ReadOnlySpan<char> s, out uint result)
        {
            result = 0;

            int len = s.Length;
            if (len == 0) return ParseStatus.Format;

            int i = SkipWhiteSpace(s, 0);
            if (i >= len) return ParseStatus.Format;

            char c = s[i];
            if (c == '+')
            {
                i++;
                if (i >= len) return ParseStatus.Format;
            }
            else if (c == '-')
            {
                return ParseStatus.Format;
            }

            uint acc = 0;
            bool any = false;

            while (i < len)
            {
                c = s[i];
                uint digit = (uint)(c - '0');
                if (digit > 9u) break;

                any = true;

                if (acc > (uint.MaxValue - digit) / 10u)
                    return ParseStatus.Overflow;

                acc = acc * 10u + digit;
                i++;
            }

            if (!any) return ParseStatus.Format;

            i = SkipWhiteSpace(s, i);
            if (i != len) return ParseStatus.Format;

            result = acc;
            return ParseStatus.OK;
        }

        internal static ParseStatus TryParseInt64(ReadOnlySpan<char> s, out long result)
        {
            result = 0;

            int len = s.Length;
            if (len == 0) return ParseStatus.Format;

            int i = SkipWhiteSpace(s, 0);
            if (i >= len) return ParseStatus.Format;

            bool neg = false;
            char c = s[i];
            if (c == '+' || c == '-')
            {
                neg = (c == '-');
                i++;
                if (i >= len) return ParseStatus.Format;
            }

            ulong limit = neg ? 9223372036854775808UL : 9223372036854775807UL;
            ulong acc = 0;
            bool any = false;

            while (i < len)
            {
                c = s[i];
                ulong digit = (ulong)(c - '0');
                if (digit > 9UL) break;

                any = true;

                if (acc > (limit - digit) / 10UL)
                    return ParseStatus.Overflow;

                acc = acc * 10UL + digit;
                i++;
            }

            if (!any) return ParseStatus.Format;

            i = SkipWhiteSpace(s, i);
            if (i != len) return ParseStatus.Format;

            if (neg)
            {
                if (acc == 9223372036854775808UL)
                    result = unchecked((long)0x8000000000000000L);
                else
                    result = -(long)acc;
            }
            else
            {
                result = (long)acc;
            }

            return ParseStatus.OK;
        }

        internal static ParseStatus TryParseUInt64(ReadOnlySpan<char> s, out ulong result)
        {
            result = 0;

            int len = s.Length;
            if (len == 0) return ParseStatus.Format;

            int i = SkipWhiteSpace(s, 0);
            if (i >= len) return ParseStatus.Format;

            char c = s[i];
            if (c == '+')
            {
                i++;
                if (i >= len) return ParseStatus.Format;
            }
            else if (c == '-')
            {
                return ParseStatus.Format;
            }

            ulong acc = 0;
            bool any = false;

            while (i < len)
            {
                c = s[i];
                ulong digit = (ulong)(c - '0');
                if (digit > 9UL) break;

                any = true;

                if (acc > (ulong.MaxValue - digit) / 10UL)
                    return ParseStatus.Overflow;

                acc = acc * 10UL + digit;
                i++;
            }

            if (!any) return ParseStatus.Format;

            i = SkipWhiteSpace(s, i);
            if (i != len) return ParseStatus.Format;

            result = acc;
            return ParseStatus.OK;
        }

        internal static ParseStatus TryParseInt16(ReadOnlySpan<char> s, out short result)
        {
            result = 0;
            int tmp;
            var st = TryParseInt32(s, out tmp);
            if (st != ParseStatus.OK) return st;
            if (tmp < -32768 || tmp > 32767) return ParseStatus.Overflow;
            result = (short)tmp;
            return ParseStatus.OK;
        }

        internal static ParseStatus TryParseUInt16(ReadOnlySpan<char> s, out ushort result)
        {
            result = 0;
            uint tmp;
            var st = TryParseUInt32(s, out tmp);
            if (st != ParseStatus.OK) return st;
            if (tmp > 65535u) return ParseStatus.Overflow;
            result = (ushort)tmp;
            return ParseStatus.OK;
        }

        internal static ParseStatus TryParseSByte(ReadOnlySpan<char> s, out sbyte result)
        {
            result = 0;
            int tmp;
            var st = TryParseInt32(s, out tmp);
            if (st != ParseStatus.OK) return st;
            if (tmp < -128 || tmp > 127) return ParseStatus.Overflow;
            result = (sbyte)tmp;
            return ParseStatus.OK;
        }

        internal static ParseStatus TryParseByte(ReadOnlySpan<char> s, out byte result)
        {
            result = 0;
            uint tmp;
            var st = TryParseUInt32(s, out tmp);
            if (st != ParseStatus.OK) return st;
            if (tmp > 255u) return ParseStatus.Overflow;
            result = (byte)tmp;
            return ParseStatus.OK;
        }
    

        internal static bool TryCopyFormatted(string value, Span<char> destination, out int charsWritten)
        {
            if (value.TryCopyTo(destination))
            {
                charsWritten = value.Length;
                return true;
            }
            charsWritten = 0;
            return false;
        }

        [DoesNotReturn]
        internal static void ThrowParseException(ParseStatus status)
        {
            if (status == ParseStatus.Overflow)
                throw new OverflowException();
            throw new FormatException();
        }

        // Styles beyond white space, a leading sign and the hex/binary specifiers are not implemented.
        private const NumberStyles UnsupportedIntegerStyles = NumberStyles.AllowTrailingSign | NumberStyles.AllowParentheses | NumberStyles.AllowDecimalPoint
            | NumberStyles.AllowThousands | NumberStyles.AllowExponent | NumberStyles.AllowCurrencySymbol;

        internal static ParseStatus TryParseBinaryInteger<T>(ReadOnlySpan<char> s, NumberStyles style, out T result)
            where T : IBinaryInteger<T>, IMinMaxValue<T>
        {
            const NumberStyles Specifiers = NumberStyles.AllowHexSpecifier | NumberStyles.AllowBinarySpecifier;
            if ((style & ~(NumberStyles.Any | Specifiers)) != 0)
                throw new ArgumentException(SR.Argument_InvalidNumberStyles, nameof(style));
            if ((style & Specifiers) != 0 && (style & ~(NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | Specifiers)) != 0)
                throw new ArgumentException(SR.Arg_InvalidHexBinaryStyle, nameof(style));
            if ((style & UnsupportedIntegerStyles) != 0)
                throw new NotSupportedException();

            result = T.Zero;
            int i = 0;
            int end = s.Length;
            if ((style & NumberStyles.AllowLeadingWhite) != 0)
            {
                while (i < end && IsWhite(s[i]))
                    i++;
            }
            if ((style & NumberStyles.AllowTrailingWhite) != 0)
            {
                while (end > i && IsWhite(s[end - 1]))
                    end--;
            }
            if (i == end)
                return ParseStatus.Format;

            T value = T.Zero;
            if ((style & Specifiers) != 0)
            {
                int bitsPerDigit = (style & NumberStyles.AllowHexSpecifier) != 0 ? 4 : 1;
                int width = value.GetByteCount() * 8;
                for (; i < end; i++)
                {
                    int digit = bitsPerDigit == 4 ? HexDigit(s[i]) : s[i] - '0';
                    if ((uint)digit >= (1u << bitsPerDigit))
                        return ParseStatus.Format;
                    if ((value >>> (width - bitsPerDigit)) != T.Zero)
                        return ParseStatus.Overflow;
                    value = (value << bitsPerDigit) | T.CreateTruncating(digit);
                }
                result = value;
                return ParseStatus.OK;
            }

            bool negative = false;
            if ((style & NumberStyles.AllowLeadingSign) != 0 && (s[i] == '-' || s[i] == '+'))
            {
                negative = s[i] == '-';
                if (++i == end)
                    return ParseStatus.Format;
            }
            bool unsigned = T.IsZero(T.MinValue);
            T ten = T.CreateTruncating(10);
            for (; i < end; i++)
            {
                uint d = (uint)(s[i] - '0');
                if (d > 9)
                    return ParseStatus.Format;
                T digit = T.CreateTruncating(d);
                if (negative)
                {
                    if (unsigned ? digit != T.Zero : value < (T.MinValue + digit) / ten)
                        return ParseStatus.Overflow;
                    value = value * ten - digit;
                }
                else
                {
                    if (value > (T.MaxValue - digit) / ten)
                        return ParseStatus.Overflow;
                    value = value * ten + digit;
                }
            }
            result = value;
            return ParseStatus.OK;

            static bool IsWhite(char ch) => ch == 0x20 || (uint)(ch - 0x09) <= (0x0D - 0x09);

            static int HexDigit(char c)
            {
                if ((uint)(c - '0') <= 9)
                    return c - '0';
                int lower = c | 0x20;
                return (uint)(lower - 'a') <= 5 ? lower - 'a' + 10 : -1;
            }
        }
    }

    // Because we have special type system support that says a boxed Nullable<T>
    // can be used where a boxed T is used, Nullable<T> can not implement any interfaces
    // at all (since T may not).
    //
    // Do NOT add any interfaces to Nullable!

    [NonVersionable] // This only applies to field layout
    public partial struct Nullable<T> where T : struct
    {
        private readonly bool hasValue; // Do not rename (binary serialization)
        internal T value; // Do not rename (binary serialization) or make readonly (can be mutated in ToString, etc.)

        [NonVersionable]
        public Nullable(T value)
        {
            this.value = value;
            hasValue = true;
        }

        public readonly bool HasValue
        {
            [NonVersionable]
            get => hasValue;
        }

        public readonly T Value
        {
            get
            {
                if (!hasValue)
                {
                    throw new InvalidOperationException("no value");
                }
                return value;
            }
        }

        [NonVersionable]
        public readonly T GetValueOrDefault() => value;

        [NonVersionable]
        public readonly T GetValueOrDefault(T defaultValue) =>
            hasValue ? value : defaultValue;

        public override bool Equals(object? other)
        {
            if (!hasValue) return other == null;
            if (other == null) return false;
            return value.Equals(other);
        }

        public override int GetHashCode() => hasValue ? value.GetHashCode() : 0;

        public override string? ToString() => hasValue ? value.ToString() : "";

        [NonVersionable]
        public static implicit operator T?(T value) =>
            new T?(value);

        [NonVersionable]
        public static explicit operator T(T? value) => value!.Value;
    }

    public static class Nullable
    {
        public static bool Equals<T>(T? n1, T? n2) where T : struct
        {
            if (n1.HasValue)
            {
                if (n2.HasValue) return EqualityComparer<T>.Default.Equals(n1.value, n2.value);
                return false;
            }
            if (n2.HasValue) return false;
            return true;
        }

        /// <summary>
        /// Retrieves a readonly reference to the location in the <see cref="Nullable{T}"/> instance where the value is stored.
        /// </summary>
        /// <typeparam name="T">The underlying value type of the <see cref="Nullable{T}"/> generic type.</typeparam>
        /// <param name="nullable">The readonly reference to the input <see cref="Nullable{T}"/> value.</param>
        /// <returns>A readonly reference to the location where the instance's <typeparamref name="T"/> value is stored. If the instance's <see cref="Nullable{T}.HasValue"/> is false, the current value at that location may be the default value.</returns>
        /// <remarks>
        /// As the returned readonly reference refers to data that is stored in the input <paramref name="nullable"/> value, this method should only ever be
        /// called when the input reference points to a value with an actual location and not an "rvalue" (an expression that may appear on the right side but not left side of an assignment). That is, if this API is called and the input reference
        /// points to a value that is produced by the compiler as a defensive copy or a temporary copy, the behavior might not match the desired one.
        /// </remarks>
        public static ref readonly T GetValueRefOrDefaultRef<T>(ref readonly T? nullable)
            where T : struct
        {
            return ref nullable.value;
        }
    }

    public struct ValueTuple : ITuple
    {
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return obj is ValueTuple;
        }
        public bool Equals(ValueTuple other)
        {
            return true;
        }
        public int CompareTo(ValueTuple other)
        {
            return 0;
        }
        public override int GetHashCode()
        {
            return 0;
        }
        public override string ToString()
        {
            return "()";
        }
        int ITuple.Length => 0;
        object? ITuple.this[int index] => throw new IndexOutOfRangeException();
    }
    public struct ValueTuple<T1> : ITuple
    {
        public T1 Item1;

        public ValueTuple(T1 item1)
        {
            Item1 = item1;
        }
        int ITuple.Length => 1;
        object? ITuple.this[int index]
        {
            get
            {
                if (index != 0)
                {
                    throw new IndexOutOfRangeException();
                }
                return Item1;
            }
        }

        public override int GetHashCode()
        {
            return Item1?.GetHashCode() ?? 0;
        }
        public override string ToString()
        {
            return "(" + Item1?.ToString() + ")";
        }
    }
    public struct ValueTuple<T1, T2> : ITuple
    {
        public T1 Item1;
        public T2 Item2;

        public ValueTuple(T1 item1, T2 item2)
        {
            Item1 = item1;
            Item2 = item2;
        }
        int ITuple.Length => 2;
        object? ITuple.this[int index] =>
            index switch
            {
                0 => Item1,
                1 => Item2,
                _ => throw new IndexOutOfRangeException(),
            };

        public override int GetHashCode()
        {
            return HashCode.Combine(Item1?.GetHashCode() ?? 0,
                                    Item2?.GetHashCode() ?? 0);
        }
        public override string ToString()
        {
            return "(" + Item1?.ToString() + ", " + Item2?.ToString() + ")";
        }
    }
    public struct ValueTuple<T1, T2, T3> : ITuple
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;

        public ValueTuple(T1 item1, T2 item2, T3 item3)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
        }
        int ITuple.Length => 3;
        object? ITuple.this[int index] =>
            index switch
            {
                0 => Item1,
                1 => Item2,
                2 => Item3,
                _ => throw new IndexOutOfRangeException(),
            };

        public override int GetHashCode()
        {
            return HashCode.Combine(Item1?.GetHashCode() ?? 0,
                                    Item2?.GetHashCode() ?? 0,
                                    Item3?.GetHashCode() ?? 0);
        }
        public override string ToString()
        {
            return "(" + Item1?.ToString() + ", " + Item2?.ToString() + ", " + Item3?.ToString() + ")";
        }
    }
    public struct ValueTuple<T1, T2, T3, T4> : ITuple
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
        }
        int ITuple.Length => 4;
        object? ITuple.this[int index] =>
            index switch
            {
                0 => Item1,
                1 => Item2,
                2 => Item3,
                3 => Item4,
                _ => throw new IndexOutOfRangeException(),
            };

        public override int GetHashCode()
        {
            return HashCode.Combine(Item1?.GetHashCode() ?? 0,
                                    Item2?.GetHashCode() ?? 0,
                                    Item3?.GetHashCode() ?? 0,
                                    Item4?.GetHashCode() ?? 0);
        }
        public override string ToString()
        {
            return "(" + Item1?.ToString() + ", " + Item2?.ToString() + ", " + Item3?.ToString() + ", " + Item4?.ToString() + ")";
        }
    }
    public struct ValueTuple<T1, T2, T3, T4, T5> : ITuple
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
        }
        int ITuple.Length => 5;
        object? ITuple.this[int index] =>
            index switch
            {
                0 => Item1,
                1 => Item2,
                2 => Item3,
                3 => Item4,
                4 => Item5,
                _ => throw new IndexOutOfRangeException(),
            };

        public override int GetHashCode()
        {
            return HashCode.Combine(Item1?.GetHashCode() ?? 0,
                                    Item2?.GetHashCode() ?? 0,
                                    Item3?.GetHashCode() ?? 0,
                                    Item4?.GetHashCode() ?? 0,
                                    Item5?.GetHashCode() ?? 0);
        }
        public override string ToString()
        {
            return "(" + Item1?.ToString() + ", " + Item2?.ToString() + ", " + Item3?.ToString() +
                ", " + Item4?.ToString() + ", " + Item5?.ToString() + ")";
        }
    }
    public struct ValueTuple<T1, T2, T3, T4, T5, T6> : ITuple
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;
        public T6 Item6;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
            Item6 = item6;
        }
        int ITuple.Length => 6;
        object? ITuple.this[int index] =>
            index switch
            {
                0 => Item1,
                1 => Item2,
                2 => Item3,
                3 => Item4,
                4 => Item5,
                5 => Item6,
                _ => throw new IndexOutOfRangeException(),
            };

        public override int GetHashCode()
        {
            return HashCode.Combine(Item1?.GetHashCode() ?? 0,
                                    Item2?.GetHashCode() ?? 0,
                                    Item3?.GetHashCode() ?? 0,
                                    Item4?.GetHashCode() ?? 0,
                                    Item5?.GetHashCode() ?? 0,
                                    Item6?.GetHashCode() ?? 0);
        }
        public override string ToString()
        {
            return "(" + Item1?.ToString() + ", " + Item2?.ToString() + ", " + Item3?.ToString() +
                ", " + Item4?.ToString() + ", " + Item5?.ToString() + ", " + Item6?.ToString() + ")";
        }
    }
    public struct ValueTuple<T1, T2, T3, T4, T5, T6, T7> : ITuple
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;
        public T6 Item6;
        public T7 Item7;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
            Item6 = item6;
            Item7 = item7;
        }
        int ITuple.Length => 7;
        object? ITuple.this[int index] =>
            index switch
            {
                0 => Item1,
                1 => Item2,
                2 => Item3,
                3 => Item4,
                4 => Item5,
                5 => Item6,
                6 => Item7,
                _ => throw new IndexOutOfRangeException(),
            };

        public override int GetHashCode()
        {
            return HashCode.Combine(Item1?.GetHashCode() ?? 0,
                                    Item2?.GetHashCode() ?? 0,
                                    Item3?.GetHashCode() ?? 0,
                                    Item4?.GetHashCode() ?? 0,
                                    Item5?.GetHashCode() ?? 0,
                                    Item6?.GetHashCode() ?? 0,
                                    Item7?.GetHashCode() ?? 0);
        }
        public override string ToString()
        {
            return "(" + Item1?.ToString() + ", " + Item2?.ToString() + ", " + Item3?.ToString() +
                ", " + Item4?.ToString() + ", " + Item5?.ToString() + ", " + Item6?.ToString() + ", " + Item7?.ToString() + ")";
        }
    }
    public struct ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest> : ITuple
        where TRest : struct
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;
        public T6 Item6;
        public T7 Item7;
        public TRest Rest;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7, TRest rest)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
            Item6 = item6;
            Item7 = item7;
            Rest = rest;
        }
        int ITuple.Length => 8;
        object? ITuple.this[int index] =>
            index switch
            {
                0 => Item1,
                1 => Item2,
                2 => Item3,
                3 => Item4,
                4 => Item5,
                5 => Item6,
                6 => Item7,
                7 => Rest,
                _ => throw new IndexOutOfRangeException(),
            };

        public override int GetHashCode()
        {
            return HashCode.Combine(Item1?.GetHashCode() ?? 0,
                                    Item2?.GetHashCode() ?? 0,
                                    Item3?.GetHashCode() ?? 0,
                                    Item4?.GetHashCode() ?? 0,
                                    Item5?.GetHashCode() ?? 0,
                                    Item6?.GetHashCode() ?? 0,
                                    Item7?.GetHashCode() ?? 0);
        }

        public override string ToString()
        {
            return "(" + Item1?.ToString() + ", " + Item2?.ToString() + ", " + Item3?.ToString() +
                ", " + Item4?.ToString() + ", " + Item5?.ToString() + ", " + Item6?.ToString() + ", " + Item7?.ToString() + ", " + Rest.ToString() + ")";
        }
    }

    public readonly struct IntPtr
        : IComparable, ISpanFormattable, IComparable<nint>, IEquatable<nint>, IBinaryInteger<nint>, IMinMaxValue<nint>, ISignedNumber<nint>
    {
        private readonly nint _value;

        public static readonly nint Zero = 0;

        public IntPtr(int value)
        {
            _value = value;
        }

        public IntPtr(long value)
        {
#if TARGET_64BIT
            _value = (nint)value;
#else
            _value = checked((nint)value);
#endif                
        }

        public unsafe IntPtr(void* value)
        {
            _value = (nint)value;
        }

        public long ToInt64() => _value;


        public static int Size
        {
#if TARGET_64BIT
            get => 8;
#else
            get => 4;
#endif

        }

        public static nint MaxValue
        {
#if TARGET_64BIT
            get => unchecked((nint)0x7fffffffffffffffL);
#else
            get => unchecked((nint)0x7fffffff);
#endif
        }

        public static nint MinValue
        {
#if TARGET_64BIT
get => unchecked((nint)(unchecked((long)0x8000000000000000L)));
#else
            get => unchecked((nint)(unchecked((int)0x80000000)));
#endif
        }

        public override bool Equals([NotNullWhen(true)] object? obj) => (obj is nint other) && Equals(other);
        public override int GetHashCode()
        {
#if TARGET_64BIT
            long value = _value;
            return value.GetHashCode();
#else
            return (int)_value;
#endif
        }
    

        public int CompareTo(object? value)
        {
            if (value is null)
                return 1;
            if (value is nint other)
                return CompareTo(other);
            throw new ArgumentException(SR.Arg_MustBeIntPtr);
        }

        public int CompareTo(nint value) => _value < value ? -1 : _value > value ? 1 : 0;

        public bool Equals(nint obj) => _value == obj;

        public string ToString(string? format) => ToString(format, null);

        public string ToString(IFormatProvider? provider) => ToString(null, provider);

        public string ToString(string? format, IFormatProvider? provider) => IntPtr.Size == 8 ? Number.FormatInt64((long)_value, format, provider) : Number.FormatInt32((int)_value, 0, format, provider);

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
            => Number.TryCopyFormatted(ToString(format.IsEmpty ? null : new string(format), provider), destination, out charsWritten);

        public static nint Parse(string s) => Parse(s, NumberStyles.Integer, null);

        public static nint Parse(string s, NumberStyles style) => Parse(s, style, null);

        public static nint Parse(string s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static nint Parse(string s, NumberStyles style, IFormatProvider? provider)
        {
            if (s is null)
                throw new ArgumentNullException(nameof(s));
            return Parse(s.AsSpan(), style, provider);
        }

        public static nint Parse(ReadOnlySpan<char> s, NumberStyles style = NumberStyles.Integer, IFormatProvider? provider = null)
        {
            Number.ParseStatus status = Number.TryParseBinaryInteger(s, style, out nint result);
            if (status != Number.ParseStatus.OK)
                Number.ThrowParseException(status);
            return result;
        }

        public static nint Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static bool TryParse([NotNullWhen(true)] string? s, out nint result) => TryParse(s, NumberStyles.Integer, null, out result);

        public static bool TryParse(ReadOnlySpan<char> s, out nint result) => TryParse(s, NumberStyles.Integer, null, out result);

        public static bool TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, out nint result)
        {
            if (s is null)
            {
                result = 0;
                return false;
            }
            return TryParse(s.AsSpan(), style, provider, out result);
        }

        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out nint result)
            => Number.TryParseBinaryInteger(s, style, out result) == Number.ParseStatus.OK;

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out nint result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out nint result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static (nint Quotient, nint Remainder) DivRem(nint left, nint right)
        {
            nint quotient = (nint)(left / right);
            return (quotient, (nint)(left - quotient * right));
        }

        public static nint LeadingZeroCount(nint value) => (nint)BitOperations.LeadingZeroCount((nuint)(nuint)value);

        public static nint PopCount(nint value) => (nint)BitOperations.PopCount((nuint)(nuint)value);

        public static nint RotateLeft(nint value, int rotateAmount) => (nint)BitOperations.RotateLeft((nuint)(nuint)value, rotateAmount);

        public static nint RotateRight(nint value, int rotateAmount) => (nint)BitOperations.RotateRight((nuint)(nuint)value, rotateAmount);

        public static nint TrailingZeroCount(nint value) => (nint)BitOperations.TrailingZeroCount((nuint)(nuint)value);

        public static bool IsPow2(nint value) => BitOperations.IsPow2(value);

        public static nint Log2(nint value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), SR.ArgumentOutOfRange_NeedNonNegNum);
            return (nint)BitOperations.Log2((nuint)(nuint)value);
        }

        public static nint Clamp(nint value, nint min, nint max) => Math.Clamp(value, min, max);

        public static nint Max(nint x, nint y) => Math.Max(x, y);

        public static nint Min(nint x, nint y) => Math.Min(x, y);

        public static bool IsEvenInteger(nint value) => (value & 1) == 0;

        public static bool IsOddInteger(nint value) => (value & 1) != 0;

        public static nint CreateChecked<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Checked);

        public static nint CreateSaturating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Saturating);

        public static nint CreateTruncating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Truncating);

        private static nint Create<TOther>(TOther value, NumericConversion.Mode mode) where TOther : INumberBase<TOther>
        {
            if (NumericConversion.TryConvertToInteger(value, mode, (long)MinValue, (ulong)MaxValue, out ulong bits))
                return (nint)bits;
            nint result;
            bool converted = mode switch
            {
                NumericConversion.Mode.Checked => TOther.TryConvertToChecked(value, out result),
                NumericConversion.Mode.Saturating => TOther.TryConvertToSaturating(value, out result),
                _ => TOther.TryConvertToTruncating(value, out result),
            };
            if (!converted)
                ThrowHelper.ThrowNotSupportedException();
            return result;
        }

        public override string ToString() => ToString(null, null);

        public static nint Abs(nint value) => Math.Abs(value);

        public static nint CopySign(nint value, nint sign)
        {
            nint absValue = value;
            if (absValue < 0)
                absValue = (nint)(-absValue);
            if (sign >= 0)
            {
                if (absValue < 0)
                    Math.ThrowNegateTwosCompOverflow();
                return absValue;
            }
            return (nint)(-absValue);
        }

        public static int Sign(nint value) => value < 0 ? -1 : value > 0 ? 1 : 0;

        public static bool IsNegative(nint value) => value < 0;

        public static bool IsPositive(nint value) => value >= 0;

        public static nint MaxMagnitude(nint x, nint y)
        {
            nint absX = x;
            if (absX < 0)
            {
                absX = (nint)(-absX);
                if (absX < 0)
                    return x;
            }
            nint absY = y;
            if (absY < 0)
            {
                absY = (nint)(-absY);
                if (absY < 0)
                    return y;
            }
            if (absX > absY)
                return x;
            if (absX == absY)
                return IsNegative(x) ? y : x;
            return y;
        }

        public static nint MinMagnitude(nint x, nint y)
        {
            nint absX = x;
            if (absX < 0)
            {
                absX = (nint)(-absX);
                if (absX < 0)
                    return y;
            }
            nint absY = y;
            if (absY < 0)
            {
                absY = (nint)(-absY);
                if (absY < 0)
                    return x;
            }
            if (absX < absY)
                return x;
            if (absX == absY)
                return IsNegative(x) ? x : y;
            return y;
        }

        static nint IAdditionOperators<nint, nint, nint>.operator +(nint left, nint right) => (nint)(left + right);
        static nint IAdditionOperators<nint, nint, nint>.operator checked +(nint left, nint right) => checked((nint)(left + right));
        static nint ISubtractionOperators<nint, nint, nint>.operator -(nint left, nint right) => (nint)(left - right);
        static nint ISubtractionOperators<nint, nint, nint>.operator checked -(nint left, nint right) => checked((nint)(left - right));
        static nint IMultiplyOperators<nint, nint, nint>.operator *(nint left, nint right) => (nint)(left * right);
        static nint IMultiplyOperators<nint, nint, nint>.operator checked *(nint left, nint right) => checked((nint)(left * right));
        static nint IDivisionOperators<nint, nint, nint>.operator /(nint left, nint right) => (nint)(left / right);
        static nint IModulusOperators<nint, nint, nint>.operator %(nint left, nint right) => (nint)(left % right);
        static nint IBitwiseOperators<nint, nint, nint>.operator &(nint left, nint right) => (nint)(left & right);
        static nint IBitwiseOperators<nint, nint, nint>.operator |(nint left, nint right) => (nint)(left | right);
        static nint IBitwiseOperators<nint, nint, nint>.operator ^(nint left, nint right) => (nint)(left ^ right);
        static nint IBitwiseOperators<nint, nint, nint>.operator ~(nint value) => (nint)(~value);
        static nint IShiftOperators<nint, int, nint>.operator <<(nint value, int shiftAmount) => (nint)(value << shiftAmount);
        static nint IShiftOperators<nint, int, nint>.operator >>(nint value, int shiftAmount) => (nint)(value >> shiftAmount);
        static nint IShiftOperators<nint, int, nint>.operator >>>(nint value, int shiftAmount) => (nint)((nuint)value >>> shiftAmount);
        static bool IEqualityOperators<nint, nint, bool>.operator ==(nint left, nint right) => left == right;
        static bool IEqualityOperators<nint, nint, bool>.operator !=(nint left, nint right) => left != right;
        static bool IComparisonOperators<nint, nint, bool>.operator <(nint left, nint right) => left < right;
        static bool IComparisonOperators<nint, nint, bool>.operator <=(nint left, nint right) => left <= right;
        static bool IComparisonOperators<nint, nint, bool>.operator >(nint left, nint right) => left > right;
        static bool IComparisonOperators<nint, nint, bool>.operator >=(nint left, nint right) => left >= right;
        static nint IIncrementOperators<nint>.operator ++(nint value) => ++value;
        static nint IIncrementOperators<nint>.operator checked ++(nint value) => checked(++value);
        static nint IDecrementOperators<nint>.operator --(nint value) => --value;
        static nint IDecrementOperators<nint>.operator checked --(nint value) => checked(--value);
        static nint IUnaryNegationOperators<nint, nint>.operator -(nint value) => (nint)(-value);
        static nint IUnaryNegationOperators<nint, nint>.operator checked -(nint value) => checked((nint)(-value));
        static nint IUnaryPlusOperators<nint, nint>.operator +(nint value) => value;
        static nint IAdditiveIdentity<nint, nint>.AdditiveIdentity => 0;
        static nint IMultiplicativeIdentity<nint, nint>.MultiplicativeIdentity => 1;
        static nint IMinMaxValue<nint>.MinValue => MinValue;
        static nint IMinMaxValue<nint>.MaxValue => MaxValue;
        static nint INumberBase<nint>.One => 1;
        static int INumberBase<nint>.Radix => 2;
        static nint INumberBase<nint>.Zero => 0;
        static bool INumberBase<nint>.IsCanonical(nint value) => true;
        static bool INumberBase<nint>.IsComplexNumber(nint value) => false;
        static bool INumberBase<nint>.IsFinite(nint value) => true;
        static bool INumberBase<nint>.IsImaginaryNumber(nint value) => false;
        static bool INumberBase<nint>.IsInfinity(nint value) => false;
        static bool INumberBase<nint>.IsInteger(nint value) => true;
        static bool INumberBase<nint>.IsNaN(nint value) => false;
        static bool INumberBase<nint>.IsNegativeInfinity(nint value) => false;
        static bool INumberBase<nint>.IsNormal(nint value) => value != 0;
        static bool INumberBase<nint>.IsPositiveInfinity(nint value) => false;
        static bool INumberBase<nint>.IsRealNumber(nint value) => true;
        static bool INumberBase<nint>.IsSubnormal(nint value) => false;
        static bool INumberBase<nint>.IsZero(nint value) => value == 0;
        static nint INumberBase<nint>.MaxMagnitudeNumber(nint x, nint y) => MaxMagnitude(x, y);
        static nint INumberBase<nint>.MinMagnitudeNumber(nint x, nint y) => MinMagnitude(x, y);
        static nint INumber<nint>.MaxNumber(nint x, nint y) => Max(x, y);
        static nint INumber<nint>.MinNumber(nint x, nint y) => Min(x, y);

        static bool INumberBase<nint>.TryConvertFromChecked<TOther>(TOther value, out nint result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Checked, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (nint)bits;
            return converted;
        }

        static bool INumberBase<nint>.TryConvertFromSaturating<TOther>(TOther value, out nint result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Saturating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (nint)bits;
            return converted;
        }

        static bool INumberBase<nint>.TryConvertFromTruncating<TOther>(TOther value, out nint result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Truncating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (nint)bits;
            return converted;
        }

        static bool INumberBase<nint>.TryConvertToChecked<TOther>(nint value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<nint>.TryConvertToSaturating<TOther>(nint value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<nint>.TryConvertToTruncating<TOther>(nint value, out TOther result)
        {
            result = default!;
            return false;
        }

        int IBinaryInteger<nint>.GetByteCount() => IntPtr.Size;

        int IBinaryInteger<nint>.GetShortestBitLength() => _value >= 0 ? (IntPtr.Size * 8) - (int)LeadingZeroCount(_value) : (IntPtr.Size * 8) + 1 - (int)LeadingZeroCount((nint)~_value);

        static bool IBinaryInteger<nint>.TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out nint value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: true, isUnsigned, IntPtr.Size, true, out ulong bits);
            value = read ? (nint)bits : (nint)0;
            return read;
        }

        static bool IBinaryInteger<nint>.TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out nint value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: false, isUnsigned, IntPtr.Size, true, out ulong bits);
            value = read ? (nint)bits : (nint)0;
            return read;
        }

        bool IBinaryInteger<nint>.TryWriteBigEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)_value, IntPtr.Size, bigEndian: true, destination, out bytesWritten);

        bool IBinaryInteger<nint>.TryWriteLittleEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)_value, IntPtr.Size, bigEndian: false, destination, out bytesWritten);

        static nint ISignedNumber<nint>.NegativeOne => -1;
    }
    public readonly struct UIntPtr
        : IComparable, ISpanFormattable, IComparable<nuint>, IEquatable<nuint>, IBinaryInteger<nuint>, IMinMaxValue<nuint>, IUnsignedNumber<nuint>
    {
        private readonly nuint _value;

        public static readonly nuint Zero = 0;

        public UIntPtr(uint value)
        {
            _value = value;
        }

        public UIntPtr(ulong value)
        {
#if TARGET_64BIT
            _value = (nuint)value;
#else
            _value = checked((nuint)value);
#endif
        }

        public unsafe UIntPtr(void* value)
        {
            _value = (nuint)value;
        }

        public static int Size
        {
#if TARGET_64BIT
            get => 8;
#else
            get => 4;
#endif
        }

        public override bool Equals([NotNullWhen(true)] object? obj) => (obj is nuint other) && Equals(other);
        public override int GetHashCode()
        {
#if TARGET_64BIT
            ulong value = _value;
            return value.GetHashCode();
#else
            return (int)_value;
#endif
        }
    

        public int CompareTo(object? value)
        {
            if (value is null)
                return 1;
            if (value is nuint other)
                return CompareTo(other);
            throw new ArgumentException(SR.Arg_MustBeUIntPtr);
        }

        public int CompareTo(nuint value) => _value < value ? -1 : _value > value ? 1 : 0;

        public bool Equals(nuint obj) => _value == obj;

        public string ToString(string? format) => ToString(format, null);

        public string ToString(IFormatProvider? provider) => ToString(null, provider);

        public string ToString(string? format, IFormatProvider? provider) => IntPtr.Size == 8 ? Number.FormatUInt64((ulong)_value, format, provider) : Number.FormatUInt32((uint)_value, format, provider);

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
            => Number.TryCopyFormatted(ToString(format.IsEmpty ? null : new string(format), provider), destination, out charsWritten);

        public static nuint Parse(string s) => Parse(s, NumberStyles.Integer, null);

        public static nuint Parse(string s, NumberStyles style) => Parse(s, style, null);

        public static nuint Parse(string s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static nuint Parse(string s, NumberStyles style, IFormatProvider? provider)
        {
            if (s is null)
                throw new ArgumentNullException(nameof(s));
            return Parse(s.AsSpan(), style, provider);
        }

        public static nuint Parse(ReadOnlySpan<char> s, NumberStyles style = NumberStyles.Integer, IFormatProvider? provider = null)
        {
            Number.ParseStatus status = Number.TryParseBinaryInteger(s, style, out nuint result);
            if (status != Number.ParseStatus.OK)
                Number.ThrowParseException(status);
            return result;
        }

        public static nuint Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static bool TryParse([NotNullWhen(true)] string? s, out nuint result) => TryParse(s, NumberStyles.Integer, null, out result);

        public static bool TryParse(ReadOnlySpan<char> s, out nuint result) => TryParse(s, NumberStyles.Integer, null, out result);

        public static bool TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, out nuint result)
        {
            if (s is null)
            {
                result = 0;
                return false;
            }
            return TryParse(s.AsSpan(), style, provider, out result);
        }

        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out nuint result)
            => Number.TryParseBinaryInteger(s, style, out result) == Number.ParseStatus.OK;

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out nuint result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out nuint result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static (nuint Quotient, nuint Remainder) DivRem(nuint left, nuint right)
        {
            nuint quotient = (nuint)(left / right);
            return (quotient, (nuint)(left - quotient * right));
        }

        public static nuint LeadingZeroCount(nuint value) => (nuint)BitOperations.LeadingZeroCount((nuint)(nuint)value);

        public static nuint PopCount(nuint value) => (nuint)BitOperations.PopCount((nuint)(nuint)value);

        public static nuint RotateLeft(nuint value, int rotateAmount) => (nuint)BitOperations.RotateLeft((nuint)(nuint)value, rotateAmount);

        public static nuint RotateRight(nuint value, int rotateAmount) => (nuint)BitOperations.RotateRight((nuint)(nuint)value, rotateAmount);

        public static nuint TrailingZeroCount(nuint value) => (nuint)BitOperations.TrailingZeroCount((nuint)(nuint)value);

        public static bool IsPow2(nuint value) => BitOperations.IsPow2(value);

        public static nuint Log2(nuint value)
        {
            return (nuint)BitOperations.Log2((nuint)(nuint)value);
        }

        public static nuint Clamp(nuint value, nuint min, nuint max) => Math.Clamp(value, min, max);

        public static nuint Max(nuint x, nuint y) => Math.Max(x, y);

        public static nuint Min(nuint x, nuint y) => Math.Min(x, y);

        public static bool IsEvenInteger(nuint value) => (value & 1) == 0;

        public static bool IsOddInteger(nuint value) => (value & 1) != 0;

        public static nuint CreateChecked<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Checked);

        public static nuint CreateSaturating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Saturating);

        public static nuint CreateTruncating<TOther>(TOther value) where TOther : INumberBase<TOther> => Create(value, NumericConversion.Mode.Truncating);

        private static nuint Create<TOther>(TOther value, NumericConversion.Mode mode) where TOther : INumberBase<TOther>
        {
            if (NumericConversion.TryConvertToInteger(value, mode, (long)MinValue, (ulong)MaxValue, out ulong bits))
                return (nuint)bits;
            nuint result;
            bool converted = mode switch
            {
                NumericConversion.Mode.Checked => TOther.TryConvertToChecked(value, out result),
                NumericConversion.Mode.Saturating => TOther.TryConvertToSaturating(value, out result),
                _ => TOther.TryConvertToTruncating(value, out result),
            };
            if (!converted)
                ThrowHelper.ThrowNotSupportedException();
            return result;
        }

        public override string ToString() => ToString(null, null);

        static nuint IAdditionOperators<nuint, nuint, nuint>.operator +(nuint left, nuint right) => (nuint)(left + right);
        static nuint IAdditionOperators<nuint, nuint, nuint>.operator checked +(nuint left, nuint right) => checked((nuint)(left + right));
        static nuint ISubtractionOperators<nuint, nuint, nuint>.operator -(nuint left, nuint right) => (nuint)(left - right);
        static nuint ISubtractionOperators<nuint, nuint, nuint>.operator checked -(nuint left, nuint right) => checked((nuint)(left - right));
        static nuint IMultiplyOperators<nuint, nuint, nuint>.operator *(nuint left, nuint right) => (nuint)(left * right);
        static nuint IMultiplyOperators<nuint, nuint, nuint>.operator checked *(nuint left, nuint right) => checked((nuint)(left * right));
        static nuint IDivisionOperators<nuint, nuint, nuint>.operator /(nuint left, nuint right) => (nuint)(left / right);
        static nuint IModulusOperators<nuint, nuint, nuint>.operator %(nuint left, nuint right) => (nuint)(left % right);
        static nuint IBitwiseOperators<nuint, nuint, nuint>.operator &(nuint left, nuint right) => (nuint)(left & right);
        static nuint IBitwiseOperators<nuint, nuint, nuint>.operator |(nuint left, nuint right) => (nuint)(left | right);
        static nuint IBitwiseOperators<nuint, nuint, nuint>.operator ^(nuint left, nuint right) => (nuint)(left ^ right);
        static nuint IBitwiseOperators<nuint, nuint, nuint>.operator ~(nuint value) => (nuint)(~value);
        static nuint IShiftOperators<nuint, int, nuint>.operator <<(nuint value, int shiftAmount) => (nuint)(value << shiftAmount);
        static nuint IShiftOperators<nuint, int, nuint>.operator >>(nuint value, int shiftAmount) => (nuint)(value >> shiftAmount);
        static nuint IShiftOperators<nuint, int, nuint>.operator >>>(nuint value, int shiftAmount) => (nuint)((nuint)value >>> shiftAmount);
        static bool IEqualityOperators<nuint, nuint, bool>.operator ==(nuint left, nuint right) => left == right;
        static bool IEqualityOperators<nuint, nuint, bool>.operator !=(nuint left, nuint right) => left != right;
        static bool IComparisonOperators<nuint, nuint, bool>.operator <(nuint left, nuint right) => left < right;
        static bool IComparisonOperators<nuint, nuint, bool>.operator <=(nuint left, nuint right) => left <= right;
        static bool IComparisonOperators<nuint, nuint, bool>.operator >(nuint left, nuint right) => left > right;
        static bool IComparisonOperators<nuint, nuint, bool>.operator >=(nuint left, nuint right) => left >= right;
        static nuint IIncrementOperators<nuint>.operator ++(nuint value) => ++value;
        static nuint IIncrementOperators<nuint>.operator checked ++(nuint value) => checked(++value);
        static nuint IDecrementOperators<nuint>.operator --(nuint value) => --value;
        static nuint IDecrementOperators<nuint>.operator checked --(nuint value) => checked(--value);
        static nuint IUnaryNegationOperators<nuint, nuint>.operator -(nuint value) => (nuint)(0 - value);
        static nuint IUnaryNegationOperators<nuint, nuint>.operator checked -(nuint value) => checked((nuint)(0 - value));
        static nuint IUnaryPlusOperators<nuint, nuint>.operator +(nuint value) => value;
        static nuint IAdditiveIdentity<nuint, nuint>.AdditiveIdentity => 0;
        static nuint IMultiplicativeIdentity<nuint, nuint>.MultiplicativeIdentity => 1;
        static nuint IMinMaxValue<nuint>.MinValue => MinValue;
        static nuint IMinMaxValue<nuint>.MaxValue => MaxValue;
        static nuint INumberBase<nuint>.One => 1;
        static int INumberBase<nuint>.Radix => 2;
        static nuint INumberBase<nuint>.Zero => 0;
        static bool INumberBase<nuint>.IsCanonical(nuint value) => true;
        static bool INumberBase<nuint>.IsComplexNumber(nuint value) => false;
        static bool INumberBase<nuint>.IsFinite(nuint value) => true;
        static bool INumberBase<nuint>.IsImaginaryNumber(nuint value) => false;
        static bool INumberBase<nuint>.IsInfinity(nuint value) => false;
        static bool INumberBase<nuint>.IsInteger(nuint value) => true;
        static bool INumberBase<nuint>.IsNaN(nuint value) => false;
        static bool INumberBase<nuint>.IsNegativeInfinity(nuint value) => false;
        static bool INumberBase<nuint>.IsNormal(nuint value) => value != 0;
        static bool INumberBase<nuint>.IsPositiveInfinity(nuint value) => false;
        static bool INumberBase<nuint>.IsRealNumber(nuint value) => true;
        static bool INumberBase<nuint>.IsSubnormal(nuint value) => false;
        static bool INumberBase<nuint>.IsZero(nuint value) => value == 0;
        static nuint INumberBase<nuint>.MaxMagnitudeNumber(nuint x, nuint y) => Max(x, y);
        static nuint INumberBase<nuint>.MinMagnitudeNumber(nuint x, nuint y) => Min(x, y);
        static nuint INumber<nuint>.MaxNumber(nuint x, nuint y) => Max(x, y);
        static nuint INumber<nuint>.MinNumber(nuint x, nuint y) => Min(x, y);

        static bool INumberBase<nuint>.TryConvertFromChecked<TOther>(TOther value, out nuint result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Checked, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (nuint)bits;
            return converted;
        }

        static bool INumberBase<nuint>.TryConvertFromSaturating<TOther>(TOther value, out nuint result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Saturating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (nuint)bits;
            return converted;
        }

        static bool INumberBase<nuint>.TryConvertFromTruncating<TOther>(TOther value, out nuint result)
        {
            bool converted = NumericConversion.TryConvertToInteger(value, NumericConversion.Mode.Truncating, (long)MinValue, (ulong)MaxValue, out ulong bits);
            result = (nuint)bits;
            return converted;
        }

        static bool INumberBase<nuint>.TryConvertToChecked<TOther>(nuint value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<nuint>.TryConvertToSaturating<TOther>(nuint value, out TOther result)
        {
            result = default!;
            return false;
        }

        static bool INumberBase<nuint>.TryConvertToTruncating<TOther>(nuint value, out TOther result)
        {
            result = default!;
            return false;
        }

        int IBinaryInteger<nuint>.GetByteCount() => IntPtr.Size;

        int IBinaryInteger<nuint>.GetShortestBitLength() => (IntPtr.Size * 8) - (int)LeadingZeroCount(_value);

        static bool IBinaryInteger<nuint>.TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out nuint value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: true, isUnsigned, IntPtr.Size, false, out ulong bits);
            value = read ? (nuint)bits : (nuint)0;
            return read;
        }

        static bool IBinaryInteger<nuint>.TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out nuint value)
        {
            bool read = NumericConversion.TryReadInteger(source, bigEndian: false, isUnsigned, IntPtr.Size, false, out ulong bits);
            value = read ? (nuint)bits : (nuint)0;
            return read;
        }

        bool IBinaryInteger<nuint>.TryWriteBigEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)_value, IntPtr.Size, bigEndian: true, destination, out bytesWritten);

        bool IBinaryInteger<nuint>.TryWriteLittleEndian(Span<byte> destination, out int bytesWritten)
            => NumericConversion.TryWriteInteger((ulong)_value, IntPtr.Size, bigEndian: false, destination, out bytesWritten);

        static nuint INumberBase<nuint>.Abs(nuint value) => value;
        static nuint INumber<nuint>.CopySign(nuint value, nuint sign) => value;
        static int INumber<nuint>.Sign(nuint value) => value == 0 ? 0 : 1;
        static bool INumberBase<nuint>.IsNegative(nuint value) => false;
        static bool INumberBase<nuint>.IsPositive(nuint value) => true;
        static nuint INumberBase<nuint>.MaxMagnitude(nuint x, nuint y) => Max(x, y);
        static nuint INumberBase<nuint>.MinMagnitude(nuint x, nuint y) => Min(x, y);

        public static nuint MaxValue => ~(nuint)0;

        public static nuint MinValue => 0;
    }

    public readonly ref struct Span<T>
    {
        internal readonly ref T _reference;
        internal readonly int _length;
        public int Length => _length;
        public bool IsEmpty => _length == 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe Span(void* pointer, int length)
        {
            _reference = ref *(T*)pointer;
            _length = length;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span(T[] array)
        {
            if (array == null)
            {
                this = default;
                return;
            }
            if (!typeof(T).IsValueType && array.GetType() != typeof(T[]))
                throw new ArrayTypeMismatchException();

            _reference = ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference<T>(array);
            _length = array.Length;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span(T[]? array, int start, int length)
        {
            if (array == null)
            {
                if (start != 0 || length != 0)
                    throw new ArgumentOutOfRangeException();
                this = default;
                return; // returns default
            }
#if TARGET_64BIT
            if ((ulong)(uint)start + (ulong)(uint)length > (ulong)(uint)array.Length)
                throw new ArgumentOutOfRangeException();
#else
            if ((uint)start > (uint)array.Length || (uint)length > (uint)(array.Length - start))
                throw new ArgumentOutOfRangeException();
#endif
            _reference = ref Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(array), (nint)(uint)start /* force zero-extension */);
            _length = length;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span(ref T reference)
        {
            _reference = ref reference;
            _length = 1;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Span(ref T reference, int length)
        {
            _reference = ref reference;
            _length = length;
        }

        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if ((uint)index >= (uint)_length)
                {
                    throw new IndexOutOfRangeException();
                }
                return ref System.Runtime.CompilerServices.Unsafe.Add<T>(ref _reference, (nint)(uint)index /* force zero-extension */);
            }
        }

        /// <summary>
        /// Clears the contents of this span.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void Clear()
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                SpanHelpers.ClearWithReferences(ref Unsafe.As<T, IntPtr>(ref _reference), (uint)_length * (nuint)(sizeof(T) / sizeof(nuint)));
            }
            else
            {
                SpanHelpers.ClearWithoutReferences(ref Unsafe.As<T, byte>(ref _reference), (uint)_length * (nuint)sizeof(T));
            }
        }

        /// <summary>
        /// Fills the contents of this span with the given value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Fill(T value)
        {
            SpanHelpers.Fill(ref _reference, (uint)_length, value);
        }

        /// <summary>
        /// Copies the contents of this span into destination span. If the source
        /// and destinations overlap, this method behaves as if the original values in
        /// a temporary location before the destination is overwritten.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when the destination Span is shorter than the source Span.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(Span<T> destination)
        {
            if ((uint)_length <= (uint)destination.Length)
            {
                Buffer.Memmove(ref destination._reference, ref _reference, (uint)_length);
            }
            else
            {
                throw new ArgumentException();
            }
        }

        /// <summary>
        /// Copies the contents of this span into destination span. If the source
        /// and destinations overlap, this method behaves as if the original values in
        /// a temporary location before the destination is overwritten.
        /// </summary>
        /// <returns>If the destination span is shorter than the source span, this method
        /// return false and no data is written to the destination.</returns>
        public bool TryCopyTo(Span<T> destination)
        {
            bool retVal = false;
            if ((uint)_length <= (uint)destination.Length)
            {
                Buffer.Memmove(ref destination._reference, ref _reference, (uint)_length);
                retVal = true;
            }
            return retVal;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Span<T>(T[] array) => new Span<T>(array);
        public static bool operator ==(Span<T> left, Span<T> right) =>
            left._length == right._length &&
            System.Runtime.CompilerServices.Unsafe.AreSame<T>(ref left._reference, ref right._reference);
        public static bool operator !=(Span<T> left, Span<T> right) => !(left == right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> Slice(int start)
        {
            if ((uint)start > (uint)_length)
            {
                throw new ArgumentOutOfRangeException();
            }
            return new Span<T>(ref System.Runtime.CompilerServices.Unsafe.Add<T>(ref _reference, (nint)(uint)start /* force zero-extension */), _length - start);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> Slice(int start, int length)
        {
#if TARGET_64BIT
            if ((ulong)(uint)start + (ulong)(uint)length > (ulong)(uint)_length)
            {
                throw new ArgumentOutOfRangeException();
            }
#else
            if ((uint)start > (uint)_length || (uint)length > (uint)(_length - start))
            {
                throw new ArgumentOutOfRangeException();
            }
#endif
            return new Span<T>(ref System.Runtime.CompilerServices.Unsafe.Add<T>(ref _reference, (nint)(uint)start /* force zero-extension */), length);
        }


    

        /// <summary>
        /// Copies the contents of this span into a new array.  This heap
        /// allocates, so should generally be avoided, however it is sometimes
        /// necessary to bridge the gap with APIs written in terms of arrays.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T[] ToArray()
        {
            if (_length == 0)
                return Array.Empty<T>();

            var destination = new T[_length];
            Buffer.Memmove(ref MemoryMarshal.GetArrayDataReference(destination), ref _reference, (uint)_length);
            return destination;
        }

        public override string ToString()
        {
            if (typeof(T) == typeof(char))
            {
                return new string(new ReadOnlySpan<char>(ref Unsafe.As<T, char>(ref _reference), _length));
            }
            return $"System.Span<{typeof(T).Name}>[{_length}]";
        }
    }
    public readonly ref struct ReadOnlySpan<T>
    {
        internal readonly ref T _reference;
        private readonly int _length;
        public int Length => _length;
        public bool IsEmpty => _length == 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan(T[] array)
        {
            if (array == null)
                return;
            _reference = ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference<T>(array);
            _length = array.Length;
        }

        public unsafe ReadOnlySpan(void* pointer, int length)
        {
            _reference = ref *(T*)pointer;
            _length = length;
        }
        public ReadOnlySpan(ref readonly T reference)
        {
            _reference = ref System.Runtime.CompilerServices.Unsafe.AsRef<T>(in reference);
            _length = 1;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan(ref T reference, int length)
        {
            _reference = ref reference;
            _length = length;
        }
        public ref readonly T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if ((uint)index >= (uint)_length)
                    throw new IndexOutOfRangeException();
                return ref System.Runtime.CompilerServices.Unsafe.Add<T>(ref _reference, (nint)(uint)index /* force zero-extension */);
            }
        }

        /// <summary>
        /// Copies the contents of this read-only span into destination span. If the source
        /// and destinations overlap, this method behaves as if the original values in
        /// a temporary location before the destination is overwritten.
        /// </summary>
        /// <param name="destination">The span to copy items into.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when the destination Span is shorter than the source Span.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(Span<T> destination)
        {
            if ((uint)_length <= (uint)destination.Length)
            {
                Buffer.Memmove(ref destination._reference, ref _reference, (uint)_length);
            }
            else
            {
                throw new ArgumentException();
            }
        }

        /// <summary>
        /// Copies the contents of this read-only span into destination span. If the source
        /// and destinations overlap, this method behaves as if the original values in
        /// a temporary location before the destination is overwritten.
        /// </summary>
        /// <returns>If the destination span is shorter than the source span, this method
        /// return false and no data is written to the destination.</returns>
        /// <param name="destination">The span to copy items into.</param>
        public bool TryCopyTo(Span<T> destination)
        {
            bool retVal = false;
            if ((uint)_length <= (uint)destination.Length)
            {
                Buffer.Memmove(ref destination._reference, ref _reference, (uint)_length);
                retVal = true;
            }
            return retVal;
        }

        /// <summary>
        /// Returns false if left and right point at the same memory and have the same length.  Note that
        /// this does *not* check to see if the *contents* are equal.
        /// </summary>
        public static bool operator !=(ReadOnlySpan<T> left, ReadOnlySpan<T> right) => !(left == right);

        public static bool operator ==(ReadOnlySpan<T> left, ReadOnlySpan<T> right) =>
            left._length == right._length &&
            Unsafe.AreSame(ref left._reference, ref right._reference);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator ReadOnlySpan<T>(T[] array) => new ReadOnlySpan<T>(array);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator ReadOnlySpan<T>(Span<T> span) => new ReadOnlySpan<T>(ref span._reference, span.Length);

        /// <summary>
        /// Returns a 0-length read-only span whose base is the null pointer.
        /// </summary>
        public static ReadOnlySpan<T> Empty => default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> Slice(int start)
        {
            if ((uint)start > (uint)_length)
            {
                throw new IndexOutOfRangeException();
            }

            return new ReadOnlySpan<T>(ref System.Runtime.CompilerServices.Unsafe.Add<T>(ref _reference, (nint)(uint)start), _length - start);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> Slice(int start, int length)
        {
#if TARGET_64BIT
            if ((ulong)(uint)start + (ulong)(uint)length > (ulong)(uint)_length)
            {
                throw new ArgumentOutOfRangeException();
            }
#else
            if ((uint)start > (uint)_length || (uint)length > (uint)(_length - start))
            {
                throw new ArgumentOutOfRangeException();
            }
#endif
            return new ReadOnlySpan<T>(ref System.Runtime.CompilerServices.Unsafe.Add<T>(ref _reference, (nint)(uint)start), length);
        }
    

        /// <summary>
        /// Copies the contents of this read-only span into a new array.  This heap
        /// allocates, so should generally be avoided, however it is sometimes
        /// necessary to bridge the gap with APIs written in terms of arrays.
        /// </summary>
        public T[] ToArray()
        {
            if (_length == 0)
                return Array.Empty<T>();

            var destination = new T[_length];
            Buffer.Memmove(ref MemoryMarshal.GetArrayDataReference(destination), ref _reference, (uint)_length);
            return destination;
        }

        public override string ToString()
        {
            if (typeof(T) == typeof(char))
            {
                return new string(new ReadOnlySpan<char>(ref Unsafe.As<T, char>(ref _reference), _length));
            }
            return $"System.ReadOnlySpan<{typeof(T).Name}>[{_length}]";
        }
    }
    public readonly struct ReadOnlyMemory<T>
    {
        internal readonly object _object;
        internal readonly int _index;
        internal readonly int _length;

        internal const int RemoveFlagsBitMask = 0x7FFFFFFF;

        public ReadOnlyMemory(T[] array)
        {
            if (array == null)
            {
                //this = default;
                return; // returns default
            }

            _object = array;
            _index = 0;
            _length = array.Length;
        }
        internal ReadOnlyMemory(object obj, int start, int length)
        {
            _object = obj;
            _index = start;
            _length = length;
        }
    }

    internal static class SpanHelpers
    {
        public static int IndexOfAny<T>(ref T searchSpace, T value0, T value1, int length) where T : IEquatable<T>?
        {
            T lookUp;
            int index = 0;
            if (default(T) != null || ((object?)value0 != null && (object?)value1 != null))
            {
                while ((length - index) >= 8)
                {
                    lookUp = Unsafe.Add(ref searchSpace, index);
                    if (value0.Equals(lookUp) || value1.Equals(lookUp))
                        goto Found;
                    lookUp = Unsafe.Add(ref searchSpace, index + 1);
                    if (value0.Equals(lookUp) || value1.Equals(lookUp))
                        goto Found1;
                    lookUp = Unsafe.Add(ref searchSpace, index + 2);
                    if (value0.Equals(lookUp) || value1.Equals(lookUp))
                        goto Found2;
                    lookUp = Unsafe.Add(ref searchSpace, index + 3);
                    if (value0.Equals(lookUp) || value1.Equals(lookUp))
                        goto Found3;
                    lookUp = Unsafe.Add(ref searchSpace, index + 4);
                    if (value0.Equals(lookUp) || value1.Equals(lookUp))
                        goto Found4;
                    lookUp = Unsafe.Add(ref searchSpace, index + 5);
                    if (value0.Equals(lookUp) || value1.Equals(lookUp))
                        goto Found5;
                    lookUp = Unsafe.Add(ref searchSpace, index + 6);
                    if (value0.Equals(lookUp) || value1.Equals(lookUp))
                        goto Found6;
                    lookUp = Unsafe.Add(ref searchSpace, index + 7);
                    if (value0.Equals(lookUp) || value1.Equals(lookUp))
                        goto Found7;

                    index += 8;
                }

                if ((length - index) >= 4)
                {
                    lookUp = Unsafe.Add(ref searchSpace, index);
                    if (value0.Equals(lookUp) || value1.Equals(lookUp))
                        goto Found;
                    lookUp = Unsafe.Add(ref searchSpace, index + 1);
                    if (value0.Equals(lookUp) || value1.Equals(lookUp))
                        goto Found1;
                    lookUp = Unsafe.Add(ref searchSpace, index + 2);
                    if (value0.Equals(lookUp) || value1.Equals(lookUp))
                        goto Found2;
                    lookUp = Unsafe.Add(ref searchSpace, index + 3);
                    if (value0.Equals(lookUp) || value1.Equals(lookUp))
                        goto Found3;

                    index += 4;
                }

                while (index < length)
                {
                    lookUp = Unsafe.Add(ref searchSpace, index);
                    if (value0.Equals(lookUp) || value1.Equals(lookUp))
                        goto Found;

                    index++;
                }
            }
            else
            {
                for (index = 0; index < length; index++)
                {
                    lookUp = Unsafe.Add(ref searchSpace, index);
                    if ((object?)lookUp is null)
                    {
                        if ((object?)value0 is null || (object?)value1 is null)
                        {
                            goto Found;
                        }
                    }
                    else if (lookUp.Equals(value0) || lookUp.Equals(value1))
                    {
                        goto Found;
                    }
                }
            }

            return -1;

        Found: // Workaround for https://github.com/dotnet/runtime/issues/8795
            return index;
        Found1:
            return index + 1;
        Found2:
            return index + 2;
        Found3:
            return index + 3;
        Found4:
            return index + 4;
        Found5:
            return index + 5;
        Found6:
            return index + 6;
        Found7:
            return index + 7;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int IndexOfAnyValueType<T>(ref T searchSpace, T value0, T value1, int length) where T : struct, INumber<T>
            => IndexOfAnyValueType<T, DontNegate<T>>(ref searchSpace, value0, value1, length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int IndexOfAnyValueType<TValue, TNegator>(ref TValue searchSpace, TValue value0, TValue value1, int length)
            where TValue : struct, INumber<TValue>
            where TNegator : struct, INegator<TValue>
        {
            if (PackedSpanHelpers.PackedIndexOfIsSupported && typeof(TValue) == typeof(short) && PackedSpanHelpers.CanUsePackedIndexOf(value0) && PackedSpanHelpers.CanUsePackedIndexOf(value1))
            {
                char char0 = Unsafe.BitCast<TValue, char>(value0);
                char char1 = Unsafe.BitCast<TValue, char>(value1);

                if (RuntimeHelpers.IsKnownConstant(value0) && RuntimeHelpers.IsKnownConstant(value1))
                {
                    // If the values differ only in the 0x20 bit, we can optimize the search by reducing the number of comparisons.
                    // This optimization only applies to a small subset of values and the throughput difference is not too significant.
                    // We avoid introducing per-call overhead for non-constant values by guarding this optimization behind RuntimeHelpers.IsKnownConstant.
                    if ((char0 ^ char1) == 0x20)
                    {
                        char lowerCase = (char)Math.Max(char0, char1);

                        return typeof(TNegator) == typeof(DontNegate<short>)
                            ? PackedSpanHelpers.IndexOfAnyIgnoreCase(ref Unsafe.As<TValue, char>(ref searchSpace), lowerCase, length)
                            : PackedSpanHelpers.IndexOfAnyExceptIgnoreCase(ref Unsafe.As<TValue, char>(ref searchSpace), lowerCase, length);
                    }
                }

                return typeof(TNegator) == typeof(DontNegate<short>)
                    ? PackedSpanHelpers.IndexOfAny(ref Unsafe.As<TValue, char>(ref searchSpace), char0, char1, length)
                    : PackedSpanHelpers.IndexOfAnyExcept(ref Unsafe.As<TValue, char>(ref searchSpace), char0, char1, length);
            }

            return NonPackedIndexOfAnyValueType<TValue, TNegator>(ref searchSpace, value0, value1, length);
        }

        internal static int NonPackedIndexOfAnyValueType<TValue, TNegator>(ref TValue searchSpace, TValue value0, TValue value1, int length)
            where TValue : struct, INumber<TValue>
            where TNegator : struct, INegator<TValue>
        {
            if (!Vector128.IsHardwareAccelerated || length < Vector128<TValue>.Count)
            {
                nuint offset = 0;
                TValue lookUp;

                if (typeof(TValue) == typeof(byte)) // this optimization is beneficial only to byte
                {
                    while (length >= 8)
                    {
                        length -= 8;

                        ref TValue current = ref Unsafe.Add(ref searchSpace, offset);
                        lookUp = current;
                        if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1)) goto Found;
                        lookUp = Unsafe.Add(ref current, 1);
                        if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1)) goto Found1;
                        lookUp = Unsafe.Add(ref current, 2);
                        if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1)) goto Found2;
                        lookUp = Unsafe.Add(ref current, 3);
                        if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1)) goto Found3;
                        lookUp = Unsafe.Add(ref current, 4);
                        if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1)) goto Found4;
                        lookUp = Unsafe.Add(ref current, 5);
                        if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1)) goto Found5;
                        lookUp = Unsafe.Add(ref current, 6);
                        if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1)) goto Found6;
                        lookUp = Unsafe.Add(ref current, 7);
                        if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1)) goto Found7;

                        offset += 8;
                    }
                }

                while (length >= 4)
                {
                    length -= 4;

                    ref TValue current = ref Unsafe.Add(ref searchSpace, offset);
                    lookUp = current;
                    if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1)) goto Found;
                    lookUp = Unsafe.Add(ref current, 1);
                    if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1)) goto Found1;
                    lookUp = Unsafe.Add(ref current, 2);
                    if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1)) goto Found2;
                    lookUp = Unsafe.Add(ref current, 3);
                    if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1)) goto Found3;

                    offset += 4;
                }

                while (length > 0)
                {
                    length -= 1;

                    lookUp = Unsafe.Add(ref searchSpace, offset);
                    if (TNegator.NegateIfNeeded(lookUp == value0 || lookUp == value1)) goto Found;

                    offset += 1;
                }
                return -1;
            Found7:
                return (int)(offset + 7);
            Found6:
                return (int)(offset + 6);
            Found5:
                return (int)(offset + 5);
            Found4:
                return (int)(offset + 4);
            Found3:
                return (int)(offset + 3);
            Found2:
                return (int)(offset + 2);
            Found1:
                return (int)(offset + 1);
            Found:
                return (int)(offset);
            }
            else if (Vector512.IsHardwareAccelerated && length >= Vector512<TValue>.Count)
            {
                Vector512<TValue> equals, current, values0 = Vector512.Create(value0), values1 = Vector512.Create(value1);
                ref TValue currentSearchSpace = ref searchSpace;
                ref TValue oneVectorAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - Vector512<TValue>.Count);

                // Loop until either we've finished all elements or there's less than a vector's-worth remaining.
                do
                {
                    current = Vector512.LoadUnsafe(ref currentSearchSpace);
                    equals = TNegator.NegateIfNeeded(Vector512.Equals(values0, current) | Vector512.Equals(values1, current));
                    if (equals == Vector512<TValue>.Zero)
                    {
                        currentSearchSpace = ref Unsafe.Add(ref currentSearchSpace, Vector512<TValue>.Count);
                        continue;
                    }

                    return ComputeFirstIndex(ref searchSpace, ref currentSearchSpace, equals);
                }
                while (Unsafe.IsAddressLessThanOrEqualTo(ref currentSearchSpace, ref oneVectorAwayFromEnd));

                // If any elements remain, process the last vector in the search space.
                if ((uint)length % Vector512<TValue>.Count != 0)
                {
                    current = Vector512.LoadUnsafe(ref oneVectorAwayFromEnd);
                    equals = TNegator.NegateIfNeeded(Vector512.Equals(values0, current) | Vector512.Equals(values1, current));
                    if (equals != Vector512<TValue>.Zero)
                    {
                        return ComputeFirstIndex(ref searchSpace, ref oneVectorAwayFromEnd, equals);
                    }
                }
            }
            else if (Vector256.IsHardwareAccelerated && length >= Vector256<TValue>.Count)
            {
                Vector256<TValue> equals, current, values0 = Vector256.Create(value0), values1 = Vector256.Create(value1);
                ref TValue currentSearchSpace = ref searchSpace;
                ref TValue oneVectorAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - Vector256<TValue>.Count);

                // Loop until either we've finished all elements or there's less than a vector's-worth remaining.
                do
                {
                    current = Vector256.LoadUnsafe(ref currentSearchSpace);
                    equals = TNegator.NegateIfNeeded(Vector256.Equals(values0, current) | Vector256.Equals(values1, current));
                    if (equals == Vector256<TValue>.Zero)
                    {
                        currentSearchSpace = ref Unsafe.Add(ref currentSearchSpace, Vector256<TValue>.Count);
                        continue;
                    }

                    return ComputeFirstIndex(ref searchSpace, ref currentSearchSpace, equals);
                }
                while (Unsafe.IsAddressLessThanOrEqualTo(ref currentSearchSpace, ref oneVectorAwayFromEnd));

                // If any elements remain, process the last vector in the search space.
                if ((uint)length % Vector256<TValue>.Count != 0)
                {
                    current = Vector256.LoadUnsafe(ref oneVectorAwayFromEnd);
                    equals = TNegator.NegateIfNeeded(Vector256.Equals(values0, current) | Vector256.Equals(values1, current));
                    if (equals != Vector256<TValue>.Zero)
                    {
                        return ComputeFirstIndex(ref searchSpace, ref oneVectorAwayFromEnd, equals);
                    }
                }
            }
            else
            {
                Vector128<TValue> equals, current, values0 = Vector128.Create(value0), values1 = Vector128.Create(value1);
                ref TValue currentSearchSpace = ref searchSpace;
                ref TValue oneVectorAwayFromEnd = ref Unsafe.Add(ref searchSpace, length - Vector128<TValue>.Count);

                // Loop until either we've finished all elements or there's less than a vector's-worth remaining.
                do
                {
                    current = Vector128.LoadUnsafe(ref currentSearchSpace);
                    equals = TNegator.NegateIfNeeded(Vector128.Equals(values0, current) | Vector128.Equals(values1, current));
                    if (equals == Vector128<TValue>.Zero)
                    {
                        currentSearchSpace = ref Unsafe.Add(ref currentSearchSpace, Vector128<TValue>.Count);
                        continue;
                    }

                    return ComputeFirstIndex(ref searchSpace, ref currentSearchSpace, equals);
                }
                while (Unsafe.IsAddressLessThanOrEqualTo(ref currentSearchSpace, ref oneVectorAwayFromEnd));

                // If any elements remain, process the first vector in the search space.
                if ((uint)length % Vector128<TValue>.Count != 0)
                {
                    current = Vector128.LoadUnsafe(ref oneVectorAwayFromEnd);
                    equals = TNegator.NegateIfNeeded(Vector128.Equals(values0, current) | Vector128.Equals(values1, current));
                    if (equals != Vector128<TValue>.Zero)
                    {
                        return ComputeFirstIndex(ref searchSpace, ref oneVectorAwayFromEnd, equals);
                    }
                }
            }

            return -1;
        }

        internal interface INegator<T> where T : struct
        {
            static abstract bool NegateIfNeeded(bool equals);
            static abstract Vector128<T> NegateIfNeeded(Vector128<T> equals);
            static abstract Vector256<T> NegateIfNeeded(Vector256<T> equals);
            static abstract Vector512<T> NegateIfNeeded(Vector512<T> equals);

            // The generic vector APIs assume use for IndexOf where `DontNegate` is
            // for `IndexOfAny` and `Negate` is for `IndexOfAnyExcept`

            static abstract bool HasMatch<TVector>(TVector left, TVector right)
                where TVector : struct, ISimdVector<TVector, T>;

            static abstract TVector GetMatchMask<TVector>(TVector left, TVector right)
                where TVector : struct, ISimdVector<TVector, T>;
        }

        internal readonly struct DontNegate<T> : INegator<T>
            where T : struct
        {
            public static bool NegateIfNeeded(bool equals) => equals;
            public static Vector128<T> NegateIfNeeded(Vector128<T> equals) => equals;
            public static Vector256<T> NegateIfNeeded(Vector256<T> equals) => equals;
            public static Vector512<T> NegateIfNeeded(Vector512<T> equals) => equals;

            // The generic vector APIs assume use for `IndexOfAny` where we
            // want "HasMatch" to mean any of the two elements match.

            public static bool HasMatch<TVector>(TVector left, TVector right)
                where TVector : struct, ISimdVector<TVector, T>
            {
                return TVector.EqualsAny(left, right);
            }

            public static TVector GetMatchMask<TVector>(TVector left, TVector right)
                where TVector : struct, ISimdVector<TVector, T>
            {
                return TVector.Equals(left, right);
            }
        }

        internal readonly struct Negate<T> : INegator<T>
            where T : struct
        {
            public static bool NegateIfNeeded(bool equals) => !equals;
            public static Vector128<T> NegateIfNeeded(Vector128<T> equals) => ~equals;
            public static Vector256<T> NegateIfNeeded(Vector256<T> equals) => ~equals;
            public static Vector512<T> NegateIfNeeded(Vector512<T> equals) => ~equals;

            // The generic vector APIs assume use for `IndexOfAnyExcept` where we
            // want "HasMatch" to mean any of the two elements don't match

            public static bool HasMatch<TVector>(TVector left, TVector right)
                where TVector : struct, ISimdVector<TVector, T>
            {
                return !TVector.EqualsAll(left, right);
            }

            public static TVector GetMatchMask<TVector>(TVector left, TVector right)
                where TVector : struct, ISimdVector<TVector, T>
            {
                return ~TVector.Equals(left, right);
            }
        }


#if TARGET_ARM64 || TARGET_LOONGARCH64
        private const ulong MemmoveNativeThreshold = ulong.MaxValue;
#elif TARGET_ARM
        private const nuint MemmoveNativeThreshold = 512;
#else
        private const nuint MemmoveNativeThreshold = 2048;
#endif
        private const nuint ZeroMemoryNativeThreshold = 1024;

#if HAS_CUSTOM_BLOCKS
        [StructLayout(LayoutKind.Sequential, Size = 16)]
        private struct Block16 {}

        [StructLayout(LayoutKind.Sequential, Size = 64)]
        private struct Block64 {}
#endif // HAS_CUSTOM_BLOCKS

        public static unsafe void ClearWithReferences(ref IntPtr ip, nuint pointerSizeLength)
        {
            // First write backward 8 natural words at a time.
            // Writing backward allows us to get away with only simple modifications to the
            // mov instruction's base and index registers between loop iterations.

            for (; pointerSizeLength >= 8; pointerSizeLength -= 8)
            {
                Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -1) = default;
                Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -2) = default;
                Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -3) = default;
                Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -4) = default;
                Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -5) = default;
                Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -6) = default;
                Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -7) = default;
                Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -8) = default;
            }

            // The logic below works by trying to minimize the number of branches taken for any
            // given range of lengths. For example, the lengths [ 4 .. 7 ] are handled by a single
            // branch, [ 2 .. 3 ] are handled by a single branch, and [ 1 ] is handled by a single
            // branch.
            //
            // We can write both forward and backward as a perf improvement. For example,
            // the lengths [ 4 .. 7 ] can be handled by zeroing out the first four natural
            // words and the last 3 natural words. In the best case (length = 7), there are
            // no overlapping writes. In the worst case (length = 4), there are three
            // overlapping writes near the middle of the buffer. In perf testing, the
            // penalty for performing duplicate writes is less expensive than the penalty
            // for complex branching.

            if (pointerSizeLength >= 4)
            {
                goto Write4To7;
            }
            else if (pointerSizeLength >= 2)
            {
                goto Write2To3;
            }
            else if (pointerSizeLength > 0)
            {
                goto Write1;
            }
            else
            {
                return; // nothing to write
            }

        Write4To7:
            // Write first four and last three.
            Unsafe.Add(ref ip, 2) = default;
            Unsafe.Add(ref ip, 3) = default;
            Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -3) = default;
            Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -2) = default;

        Write2To3:
            // Write first two and last one.
            Unsafe.Add(ref ip, 1) = default;
            Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -1) = default;

        Write1:
            // Write only element.
            ip = default;
        }

        public static void ClearWithoutReferences(ref byte dest, nuint len)
        {
            if (len == 0)
                return;

            ref byte destEnd = ref Unsafe.Add(ref dest, len);

            if (len <= 16)
                goto MZER02;
            if (len > 64)
                goto MZER05;

        MZER00:
            // Clear bytes which are multiples of 16 and leave the remainder for MZER01 to handle.
#if HAS_CUSTOM_BLOCKS
            Unsafe.WriteUnaligned<Block16>(ref dest, default);
#elif TARGET_64BIT
            Unsafe.WriteUnaligned<long>(ref dest, 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 8), 0);
#else
            Unsafe.WriteUnaligned<int>(ref dest, 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 4), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 8), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 12), 0);
#endif
            if (len <= 32)
                goto MZER01;
#if HAS_CUSTOM_BLOCKS
            Unsafe.WriteUnaligned<Block16>(ref Unsafe.Add(ref dest, 16), default);
#elif TARGET_64BIT
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 16), 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 24), 0);
#else
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 16), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 20), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 24), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 28), 0);
#endif
            if (len <= 48)
                goto MZER01;
#if HAS_CUSTOM_BLOCKS
            Unsafe.WriteUnaligned<Block16>(ref Unsafe.Add(ref dest, 32), default);
#elif TARGET_64BIT
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 32), 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 40), 0);
#else
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 32), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 36), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 40), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 44), 0);
#endif

        MZER01:
            // Unconditionally clear the last 16 bytes using destEnd and return.
#if HAS_CUSTOM_BLOCKS
            Unsafe.WriteUnaligned<Block16>(ref Unsafe.Add(ref destEnd, -16), default);
#elif TARGET_64BIT
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref destEnd, -16), 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref destEnd, -8), 0);
#else
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -16), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -12), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -8), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -4), 0);
#endif
            return;

        MZER02:
            // Clear the first 8 bytes and then unconditionally clear the last 8 bytes and return.
            if ((len & 24) == 0)
                goto MZER03;
#if TARGET_64BIT
            Unsafe.WriteUnaligned<long>(ref dest, 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref destEnd, -8), 0);
#else
            Unsafe.WriteUnaligned<int>(ref dest, 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 4), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -8), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -4), 0);
#endif
            return;

        MZER03:
            // Clear the first 4 bytes and then unconditionally clear the last 4 bytes and return.
            if ((len & 4) == 0)
                goto MZER04;
            Unsafe.WriteUnaligned<int>(ref dest, 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -4), 0);
            return;

        MZER04:
            // Clear the first byte. For pending bytes, do an unconditionally clear of the last 2 bytes and return.
            if (len == 0)
                return;
            dest = 0;
            if ((len & 2) == 0)
                return;
            Unsafe.WriteUnaligned<short>(ref Unsafe.Add(ref destEnd, -2), 0);
            return;

        MZER05:
            // PInvoke to the native version when the clear length exceeds the threshold.
            if (len > ZeroMemoryNativeThreshold)
            {
                goto PInvoke;
            }

#if HAS_CUSTOM_BLOCKS
            if (len >= 256)
            {
                // Try to opportunistically align the destination below. The input isn't pinned, so the GC
                // is free to move the references. We're therefore assuming that reads may still be unaligned.
                nuint misalignedElements = 64 - Unsafe.OpportunisticMisalignment(ref dest, 64);
                Unsafe.WriteUnaligned<Block64>(ref dest, default);
                dest = ref Unsafe.Add(ref dest, misalignedElements);
                len -= misalignedElements;
            }
#endif
            // Clear 64-bytes at a time until the remainder is less than 64.
            // If remainder is greater than 16 bytes, then jump to MZER00. Otherwise, unconditionally clear the last 16 bytes and return.
            nuint n = len >> 6;

        MZER06:
#if HAS_CUSTOM_BLOCKS
            Unsafe.WriteUnaligned<Block64>(ref dest, default);
#elif TARGET_64BIT
            Unsafe.WriteUnaligned<long>(ref dest, 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 8), 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 16), 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 24), 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 32), 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 40), 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 48), 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 56), 0);
#else
            Unsafe.WriteUnaligned<int>(ref dest, 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 4), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 8), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 12), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 16), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 20), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 24), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 28), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 32), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 36), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 40), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 44), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 48), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 52), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 56), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 60), 0);
#endif
            dest = ref Unsafe.Add(ref dest, 64);
            n--;
            if (n != 0)
                goto MZER06;

            len %= 64;
            if (len > 16)
                goto MZER00;
#if HAS_CUSTOM_BLOCKS
            Unsafe.WriteUnaligned<Block16>(ref Unsafe.Add(ref destEnd, -16), default);
#elif TARGET_64BIT
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref destEnd, -16), 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref destEnd, -8), 0);
#else
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -16), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -12), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -8), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -4), 0);
#endif
            return;

        PInvoke:
            // Implicit nullchecks
            _ = Unsafe.ReadUnaligned<byte>(ref dest);
            ZeroMemoryNative(ref dest, len);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static unsafe void ZeroMemoryNative(ref byte b, nuint byteLength)
        {
            fixed (byte* ptr = &b)
            {
                byte* adjustedPtr = ptr;
#if TARGET_X86 || TARGET_AMD64
                if (byteLength > 0x100)
                {
                    // memset ends up calling rep stosb if the hardware claims to support it efficiently. rep stosb is up to 2x slower
                    // on misaligned blocks. Workaround this issue by aligning the blocks passed to memset upfront.
                    Unsafe.WriteUnaligned<Block16>(ptr, default);
                    Unsafe.WriteUnaligned<Block16>(ptr + byteLength - 16, default);

                    byte* alignedEnd = (byte*)((nuint)(ptr + byteLength - 1) & ~(nuint)(16 - 1));

                    adjustedPtr = (byte*)(((nuint)ptr + 16) & ~(nuint)(16 - 1));
                    byteLength = (nuint)(alignedEnd - adjustedPtr);
                }
#endif
                memset(adjustedPtr, 0, byteLength);
            }
        }

        public static unsafe void Fill<T>(ref T refData, nuint numElements, T value)
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                goto CannotVectorize;
            }


        CannotVectorize:

            // If we reached this point, we cannot vectorize this T, or there are too few
            // elements for us to vectorize. Fall back to an unrolled loop.

            nuint i = 0;

            // Write 8 elements at a time

            if (numElements >= 8)
            {
                nuint stopLoopAtOffset = numElements & ~(nuint)7;
                do
                {
                    Unsafe.Add(ref refData, (nint)i + 0) = value;
                    Unsafe.Add(ref refData, (nint)i + 1) = value;
                    Unsafe.Add(ref refData, (nint)i + 2) = value;
                    Unsafe.Add(ref refData, (nint)i + 3) = value;
                    Unsafe.Add(ref refData, (nint)i + 4) = value;
                    Unsafe.Add(ref refData, (nint)i + 5) = value;
                    Unsafe.Add(ref refData, (nint)i + 6) = value;
                    Unsafe.Add(ref refData, (nint)i + 7) = value;
                } while ((i += 8) < stopLoopAtOffset);
            }

            // Write next 4 elements if needed

            if ((numElements & 4) != 0)
            {
                Unsafe.Add(ref refData, (nint)i + 0) = value;
                Unsafe.Add(ref refData, (nint)i + 1) = value;
                Unsafe.Add(ref refData, (nint)i + 2) = value;
                Unsafe.Add(ref refData, (nint)i + 3) = value;
                i += 4;
            }

            // Write next 2 elements if needed

            if ((numElements & 2) != 0)
            {
                Unsafe.Add(ref refData, (nint)i + 0) = value;
                Unsafe.Add(ref refData, (nint)i + 1) = value;
                i += 2;
            }

            // Write final element if needed

            if ((numElements & 1) != 0)
            {
                Unsafe.Add(ref refData, (nint)i) = value;
            }
        }

        public static bool SequenceEqual<T>(ref T first, ref T second, int length) where T : IEquatable<T>?
        {
            if (Unsafe.AreSame(ref first, ref second))
            {
                return true;
            }

            nint index = 0; // Use nint for arithmetic to avoid unnecessary 64->32->64 truncations
            T lookUp0;
            T lookUp1;
            while (length >= 8)
            {
                length -= 8;

                lookUp0 = Unsafe.Add(ref first, index);
                lookUp1 = Unsafe.Add(ref second, index);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                lookUp0 = Unsafe.Add(ref first, index + 1);
                lookUp1 = Unsafe.Add(ref second, index + 1);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                lookUp0 = Unsafe.Add(ref first, index + 2);
                lookUp1 = Unsafe.Add(ref second, index + 2);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                lookUp0 = Unsafe.Add(ref first, index + 3);
                lookUp1 = Unsafe.Add(ref second, index + 3);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                lookUp0 = Unsafe.Add(ref first, index + 4);
                lookUp1 = Unsafe.Add(ref second, index + 4);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                lookUp0 = Unsafe.Add(ref first, index + 5);
                lookUp1 = Unsafe.Add(ref second, index + 5);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                lookUp0 = Unsafe.Add(ref first, index + 6);
                lookUp1 = Unsafe.Add(ref second, index + 6);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                lookUp0 = Unsafe.Add(ref first, index + 7);
                lookUp1 = Unsafe.Add(ref second, index + 7);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                index += 8;
            }

            if (length >= 4)
            {
                length -= 4;

                lookUp0 = Unsafe.Add(ref first, index);
                lookUp1 = Unsafe.Add(ref second, index);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                lookUp0 = Unsafe.Add(ref first, index + 1);
                lookUp1 = Unsafe.Add(ref second, index + 1);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                lookUp0 = Unsafe.Add(ref first, index + 2);
                lookUp1 = Unsafe.Add(ref second, index + 2);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                lookUp0 = Unsafe.Add(ref first, index + 3);
                lookUp1 = Unsafe.Add(ref second, index + 3);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                index += 4;
            }

            while (length > 0)
            {
                lookUp0 = Unsafe.Add(ref first, index);
                lookUp1 = Unsafe.Add(ref second, index);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                index += 1;
                length--;
            }

            return true;
        }

        public static unsafe bool SequenceEqual(ref byte first, ref byte second, nuint length)
        {
            bool result;
            // Use nint for arithmetic to avoid unnecessary 64->32->64 truncations
            if (length >= (nuint)sizeof(nuint))
            {
                // Conditional jmp forward to favor shorter lengths. (See comment at "Equal:" label)
                // The longer lengths can make back the time due to branch misprediction
                // better than shorter lengths.
                goto Longer;
            }

#if TARGET_64BIT
            // On 32-bit, this will always be true since sizeof(nuint) == 4
            if (length < sizeof(uint))
#endif
            {
                uint differentBits = 0;
                nuint offset = (length & 2);
                if (offset != 0)
                {
                    differentBits = LoadUShort(ref first);
                    differentBits -= LoadUShort(ref second);
                }
                if ((length & 1) != 0)
                {
                    differentBits |= (uint)Unsafe.AddByteOffset(ref first, offset) - (uint)Unsafe.AddByteOffset(ref second, offset);
                }
                result = (differentBits == 0);
                goto Result;
            }
#if TARGET_64BIT
            else
            {
                nuint offset = length - sizeof(uint);
                uint differentBits = LoadUInt(ref first) - LoadUInt(ref second);
                differentBits |= LoadUInt(ref first, offset) - LoadUInt(ref second, offset);
                result = (differentBits == 0);
                goto Result;
            }
#endif
        Longer:
            // Only check that the ref is the same if buffers are large,
            // and hence its worth avoiding doing unnecessary comparisons
            if (!Unsafe.AreSame(ref first, ref second))
            {
                goto Vector;
            }

            // This becomes a conditional jmp forward to not favor it.
            goto Equal;

        Result:
            return result;
            // When the sequence is equal; which is the longest execution, we want it to determine that
            // as fast as possible so we do not want the early outs to be "predicted not taken" branches.
        Equal:
            return true;

        Vector:

            {
                {
                    nuint offset = 0;
                    nuint lengthToExamine = length - (nuint)sizeof(nuint);
                    // Unsigned, so it shouldn't have overflowed larger than length (rather than negative)

                    if (lengthToExamine > 0)
                    {
                        do
                        {
                            // Compare unsigned so not do a sign extend mov on 64 bit
                            if (LoadNUInt(ref first, offset) != LoadNUInt(ref second, offset))
                            {
                                goto NotEqual;
                            }
                            offset += (nuint)sizeof(nuint);
                        } while (lengthToExamine > offset);
                    }

                    // Do final compare as sizeof(nuint) from end rather than start
                    result = (LoadNUInt(ref first, lengthToExamine) == LoadNUInt(ref second, lengthToExamine));
                    goto Result;
                }
            }

        NotEqual:
            return false;
        }

        public static int SequenceCompareTo<T>(ref T first, int firstLength, ref T second, int secondLength)
            where T : IComparable<T>?
        {
            int minLength = firstLength;
            if (minLength > secondLength)
                minLength = secondLength;
            for (int i = 0; i < minLength; i++)
            {
                T lookUp = Unsafe.Add(ref second, i);
                int result = (Unsafe.Add(ref first, i)?.CompareTo(lookUp) ?? (((object?)lookUp is null) ? 0 : -1));
                if (result != 0)
                {
                    return result;
                }
            }

            return firstLength.CompareTo(secondLength);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort LoadUShort(ref byte start)
            => Unsafe.ReadUnaligned<ushort>(ref start);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint LoadUInt(ref byte start)
            => Unsafe.ReadUnaligned<uint>(ref start);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint LoadUInt(ref byte start, nuint offset)
            => Unsafe.ReadUnaligned<uint>(ref Unsafe.AddByteOffset(ref start, offset));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint LoadNUInt(ref byte start)
            => Unsafe.ReadUnaligned<nuint>(ref start);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint LoadNUInt(ref byte start, nuint offset)
            => Unsafe.ReadUnaligned<nuint>(ref Unsafe.AddByteOffset(ref start, offset));

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern unsafe void memset(void* dest, int value, nuint len);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int ComputeFirstIndex<T>(ref T searchSpace, ref T current, Vector128<T> equals) where T : struct
        {
            uint notEqualsElements = equals.ExtractMostSignificantBits();
            int index = BitOperations.TrailingZeroCount(notEqualsElements);
            return index + (int)((nuint)Unsafe.ByteOffset(ref searchSpace, ref current) / (nuint)sizeof(T));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int ComputeFirstIndex<T>(ref T searchSpace, ref T current, Vector256<T> equals) where T : struct
        {
            uint notEqualsElements = equals.ExtractMostSignificantBits();
            int index = BitOperations.TrailingZeroCount(notEqualsElements);
            return index + (int)((nuint)Unsafe.ByteOffset(ref searchSpace, ref current) / (nuint)sizeof(T));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int ComputeFirstIndex<T>(ref T searchSpace, ref T current, Vector512<T> equals) where T : struct
        {
            ulong notEqualsElements = equals.ExtractMostSignificantBits();
            int index = BitOperations.TrailingZeroCount(notEqualsElements);
            return index + (int)((nuint)Unsafe.ByteOffset(ref searchSpace, ref current) / (nuint)sizeof(T));
        }
    }

    public static class MemoryExtensions
    {
        /// <summary>
        /// Searches for the first index of any of the specified values similar to calling IndexOf several times with the logical OR operator. If not found, returns -1.
        /// </summary>
        /// <param name="span">The span to search.</param>
        /// <param name="value0">One of the values to search for.</param>
        /// <param name="value1">One of the values to search for.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int IndexOfAny<T>(this ReadOnlySpan<T> span, T value0, T value1) where T : IEquatable<T>?
        {
            if (RuntimeHelpers.IsBitwiseEquatable<T>())
            {
                if (sizeof(T) == sizeof(byte))
                {
                    return SpanHelpers.IndexOfAnyValueType(
                        ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(span)),
                        Unsafe.BitCast<T, byte>(value0),
                        Unsafe.BitCast<T, byte>(value1),
                        span.Length);
                }
                else if (sizeof(T) == sizeof(short))
                {
                    return SpanHelpers.IndexOfAnyValueType(
                        ref Unsafe.As<T, short>(ref MemoryMarshal.GetReference(span)),
                        Unsafe.BitCast<T, short>(value0),
                        Unsafe.BitCast<T, short>(value1),
                        span.Length);
                }
            }

            return SpanHelpers.IndexOfAny(ref MemoryMarshal.GetReference(span), value0, value1, span.Length);
        }

        internal static bool EqualsOrdinalIgnoreCase(this ReadOnlySpan<char> span, ReadOnlySpan<char> value)
        {
            if (span.Length != value.Length)
                return false;
            if (value.Length == 0)
                return true;
            return Globalization.Ordinal.EqualsIgnoreCase(
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span),
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(value),
                span.Length);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<char> Trim(this ReadOnlySpan<char> span)
        {
            // Assume that in most cases input doesn't need trimming
            if (span.Length == 0 ||
                (!char.IsWhiteSpace(span[0]) && !char.IsWhiteSpace(span[^1])))
            {
                return span;
            }
            return TrimFallback(span);

            [MethodImpl(MethodImplOptions.NoInlining)]
            static ReadOnlySpan<char> TrimFallback(ReadOnlySpan<char> span)
            {
                int start = 0;
                for (; start < span.Length; start++)
                {
                    if (!char.IsWhiteSpace(span[start]))
                    {
                        break;
                    }
                }

                int end = span.Length - 1;
                for (; end > start; end--)
                {
                    if (!char.IsWhiteSpace(span[end]))
                    {
                        break;
                    }
                }
                return span.Slice(start, end - start + 1);
            }
        }

        /// <summary>
        /// Creates a new span over the target array.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<T> AsSpan<T>(this T[]? array)
        {
            return new Span<T>(array);
        }

        /// <summary>
        /// Creates a new span over the portion of the target array.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<T> AsSpan<T>(this T[]? array, int start)
        {
            if (array == null)
            {
                if (start != 0)
                    ThrowHelper.ThrowArgumentOutOfRangeException();
                return default;
            }
            if (!typeof(T).IsValueType && array.GetType() != typeof(T[]))
                ThrowHelper.ThrowArrayTypeMismatchException();
            if ((uint)start > (uint)array.Length)
                ThrowHelper.ThrowArgumentOutOfRangeException();

            return new Span<T>(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), (nint)(uint)start /* force zero-extension */), array.Length - start);
        }

        /// <summary>
        /// Creates a new Span over the portion of the target array beginning
        /// at 'start' index and ending at 'end' index (exclusive).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<T> AsSpan<T>(this T[]? array, int start, int length)
        {
            return new Span<T>(array, start, length);
        }


        /// <summary>
        /// Determines whether two sequences overlap in memory.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [OverloadResolutionPriority(-1)]
        public static bool Overlaps<T>(this Span<T> span, ReadOnlySpan<T> other) =>
            Overlaps((ReadOnlySpan<T>)span, other);

        /// <summary>
        /// Determines whether two sequences overlap in memory and outputs the element offset.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [OverloadResolutionPriority(-1)]
        public static bool Overlaps<T>(this Span<T> span, ReadOnlySpan<T> other, out int elementOffset) =>
            Overlaps((ReadOnlySpan<T>)span, other, out elementOffset);

        /// <summary>
        /// Determines whether two sequences overlap in memory.
        /// </summary>
        public static unsafe bool Overlaps<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> other)
        {
            if (span.IsEmpty || other.IsEmpty)
            {
                return false;
            }

            nint byteOffset = Unsafe.ByteOffset(
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span),
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(other));

            return (nuint)byteOffset < (nuint)((nint)span.Length * sizeof(T)) ||
                    (nuint)byteOffset > (nuint)(-((nint)other.Length * sizeof(T)));
        }

        /// <summary>
        /// Determines whether two sequences overlap in memory and outputs the element offset.
        /// </summary>
        public static unsafe bool Overlaps<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> other, out int elementOffset)
        {
            if (span.IsEmpty || other.IsEmpty)
            {
                elementOffset = 0;
                return false;
            }

            nint byteOffset = Unsafe.ByteOffset(
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span),
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(other));

            if ((nuint)byteOffset < (nuint)((nint)span.Length * sizeof(T)) ||
                (nuint)byteOffset > (nuint)(-((nint)other.Length * sizeof(T))))
            {
                if (byteOffset % sizeof(T) != 0)
                    throw new ArgumentException();

                elementOffset = (int)(byteOffset / sizeof(T));
                return true;
            }
            else
            {
                elementOffset = 0;
                return false;
            }
        }

        /// <summary>
        /// Determines whether two sequences are equal by comparing the elements using IEquatable{T}.Equals(T).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool SequenceEqual<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> other) where T : IEquatable<T>?
        {
            int length = span.Length;
            int otherLength = other.Length;

            //if (RuntimeHelpers.IsBitwiseEquatable<T>())
            {

            }

            return length == otherLength && SpanHelpers.SequenceEqual(
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span), 
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(other), length);
        }
        /// <summary>
        /// Determines the relative order of the sequences being compared by comparing the elements using IComparable{T}.CompareTo(T).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SequenceCompareTo<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> other) where T : IComparable<T>?
        {
            // Can't use IsBitwiseEquatable<T>() below because that only tells us about
            // equality checks, not about CompareTo checks.

            if (typeof(T) == typeof(byte))
                return SpanHelpers.SequenceCompareTo(
                    ref Unsafe.As<T, byte>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span)),
                    span.Length,
                    ref Unsafe.As<T, byte>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(other)),
                    other.Length);

            if (typeof(T) == typeof(char))
                return SpanHelpers.SequenceCompareTo(
                    ref Unsafe.As<T, char>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span)),
                    span.Length,
                    ref Unsafe.As<T, char>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(other)),
                    other.Length);

            return SpanHelpers.SequenceCompareTo(
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span), span.Length, 
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(other), other.Length);
        }
    }

    public struct HashCode
    {
        private static readonly uint s_seed = GenerateGlobalSeed();

        private const uint Prime1 = 2654435761U;
        private const uint Prime2 = 2246822519U;
        private const uint Prime3 = 3266489917U;
        private const uint Prime4 = 668265263U;
        private const uint Prime5 = 374761393U;

        private uint _v1, _v2, _v3, _v4;
        private uint _queue1, _queue2, _queue3;
        private uint _length;

        private static unsafe uint GenerateGlobalSeed()
        {
            uint result = 0u;
            //Interop.GetRandomBytes((byte*)&result, sizeof(uint));
            return result;
        }

        public static int Combine<T1>(T1 value1)
        {
            uint hc1 = (uint)(value1?.GetHashCode() ?? 0);

            uint hash = MixEmptyState();
            hash += 4;

            hash = QueueRound(hash, hc1);

            hash = MixFinal(hash);
            return (int)hash;
        }

        public static int Combine<T1, T2>(T1 value1, T2 value2)
        {
            uint hc1 = (uint)(value1?.GetHashCode() ?? 0);
            uint hc2 = (uint)(value2?.GetHashCode() ?? 0);

            uint hash = MixEmptyState();
            hash += 8;

            hash = QueueRound(hash, hc1);
            hash = QueueRound(hash, hc2);

            hash = MixFinal(hash);
            return (int)hash;
        }

        public static int Combine<T1, T2, T3>(T1 value1, T2 value2, T3 value3)
        {
            uint hc1 = (uint)(value1?.GetHashCode() ?? 0);
            uint hc2 = (uint)(value2?.GetHashCode() ?? 0);
            uint hc3 = (uint)(value3?.GetHashCode() ?? 0);

            uint hash = MixEmptyState();
            hash += 12;

            hash = QueueRound(hash, hc1);
            hash = QueueRound(hash, hc2);
            hash = QueueRound(hash, hc3);

            hash = MixFinal(hash);
            return (int)hash;
        }

        public static int Combine<T1, T2, T3, T4>(T1 value1, T2 value2, T3 value3, T4 value4)
        {
            uint hc1 = (uint)(value1?.GetHashCode() ?? 0);
            uint hc2 = (uint)(value2?.GetHashCode() ?? 0);
            uint hc3 = (uint)(value3?.GetHashCode() ?? 0);
            uint hc4 = (uint)(value4?.GetHashCode() ?? 0);

            Initialize(out uint v1, out uint v2, out uint v3, out uint v4);

            v1 = Round(v1, hc1);
            v2 = Round(v2, hc2);
            v3 = Round(v3, hc3);
            v4 = Round(v4, hc4);

            uint hash = MixState(v1, v2, v3, v4);
            hash += 16;

            hash = MixFinal(hash);
            return (int)hash;
        }

        public static int Combine<T1, T2, T3, T4, T5>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5)
        {
            uint hc1 = (uint)(value1?.GetHashCode() ?? 0);
            uint hc2 = (uint)(value2?.GetHashCode() ?? 0);
            uint hc3 = (uint)(value3?.GetHashCode() ?? 0);
            uint hc4 = (uint)(value4?.GetHashCode() ?? 0);
            uint hc5 = (uint)(value5?.GetHashCode() ?? 0);

            Initialize(out uint v1, out uint v2, out uint v3, out uint v4);

            v1 = Round(v1, hc1);
            v2 = Round(v2, hc2);
            v3 = Round(v3, hc3);
            v4 = Round(v4, hc4);

            uint hash = MixState(v1, v2, v3, v4);
            hash += 20;

            hash = QueueRound(hash, hc5);

            hash = MixFinal(hash);
            return (int)hash;
        }

        public static int Combine<T1, T2, T3, T4, T5, T6>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6)
        {
            uint hc1 = (uint)(value1?.GetHashCode() ?? 0);
            uint hc2 = (uint)(value2?.GetHashCode() ?? 0);
            uint hc3 = (uint)(value3?.GetHashCode() ?? 0);
            uint hc4 = (uint)(value4?.GetHashCode() ?? 0);
            uint hc5 = (uint)(value5?.GetHashCode() ?? 0);
            uint hc6 = (uint)(value6?.GetHashCode() ?? 0);

            Initialize(out uint v1, out uint v2, out uint v3, out uint v4);

            v1 = Round(v1, hc1);
            v2 = Round(v2, hc2);
            v3 = Round(v3, hc3);
            v4 = Round(v4, hc4);

            uint hash = MixState(v1, v2, v3, v4);
            hash += 24;

            hash = QueueRound(hash, hc5);
            hash = QueueRound(hash, hc6);

            hash = MixFinal(hash);
            return (int)hash;
        }

        public static int Combine<T1, T2, T3, T4, T5, T6, T7>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7)
        {
            uint hc1 = (uint)(value1?.GetHashCode() ?? 0);
            uint hc2 = (uint)(value2?.GetHashCode() ?? 0);
            uint hc3 = (uint)(value3?.GetHashCode() ?? 0);
            uint hc4 = (uint)(value4?.GetHashCode() ?? 0);
            uint hc5 = (uint)(value5?.GetHashCode() ?? 0);
            uint hc6 = (uint)(value6?.GetHashCode() ?? 0);
            uint hc7 = (uint)(value7?.GetHashCode() ?? 0);

            Initialize(out uint v1, out uint v2, out uint v3, out uint v4);

            v1 = Round(v1, hc1);
            v2 = Round(v2, hc2);
            v3 = Round(v3, hc3);
            v4 = Round(v4, hc4);

            uint hash = MixState(v1, v2, v3, v4);
            hash += 28;

            hash = QueueRound(hash, hc5);
            hash = QueueRound(hash, hc6);
            hash = QueueRound(hash, hc7);

            hash = MixFinal(hash);
            return (int)hash;
        }

        public static int Combine<T1, T2, T3, T4, T5, T6, T7, T8>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8)
        {
            uint hc1 = (uint)(value1?.GetHashCode() ?? 0);
            uint hc2 = (uint)(value2?.GetHashCode() ?? 0);
            uint hc3 = (uint)(value3?.GetHashCode() ?? 0);
            uint hc4 = (uint)(value4?.GetHashCode() ?? 0);
            uint hc5 = (uint)(value5?.GetHashCode() ?? 0);
            uint hc6 = (uint)(value6?.GetHashCode() ?? 0);
            uint hc7 = (uint)(value7?.GetHashCode() ?? 0);
            uint hc8 = (uint)(value8?.GetHashCode() ?? 0);

            Initialize(out uint v1, out uint v2, out uint v3, out uint v4);

            v1 = Round(v1, hc1);
            v2 = Round(v2, hc2);
            v3 = Round(v3, hc3);
            v4 = Round(v4, hc4);

            v1 = Round(v1, hc5);
            v2 = Round(v2, hc6);
            v3 = Round(v3, hc7);
            v4 = Round(v4, hc8);

            uint hash = MixState(v1, v2, v3, v4);
            hash += 32;

            hash = MixFinal(hash);
            return (int)hash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Initialize(out uint v1, out uint v2, out uint v3, out uint v4)
        {
            v1 = s_seed + Prime1 + Prime2;
            v2 = s_seed + Prime2;
            v3 = s_seed;
            v4 = s_seed - Prime1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Round(uint hash, uint input)
        {
            return System.Numerics.BitOperations.RotateLeft(hash + input * Prime2, 13) * Prime1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint QueueRound(uint hash, uint queuedValue)
        {
            return System.Numerics.BitOperations.RotateLeft(hash + queuedValue * Prime3, 17) * Prime4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint MixState(uint v1, uint v2, uint v3, uint v4)
        {
            return System.Numerics.BitOperations.RotateLeft(v1, 1)
                 + System.Numerics.BitOperations.RotateLeft(v2, 7)
                 + System.Numerics.BitOperations.RotateLeft(v3, 12)
                 + System.Numerics.BitOperations.RotateLeft(v4, 18);
        }

        private static uint MixEmptyState()
        {
            return s_seed + Prime5;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint MixFinal(uint hash)
        {
            hash ^= hash >> 15;
            hash *= Prime2;
            hash ^= hash >> 13;
            hash *= Prime3;
            hash ^= hash >> 16;
            return hash;
        }

        public void Add<T>(T value)
        {
            Add(value?.GetHashCode() ?? 0);
        }

        public void Add<T>(T value, IEqualityComparer<T>? comparer)
        {
            Add(value is null ? 0 : (comparer?.GetHashCode(value) ?? value.GetHashCode()));
        }

        private void Add(int value)
        {
            uint val = (uint)value;

            uint previousLength = _length++;
            uint position = previousLength % 4;

            if (position == 0)
                _queue1 = val;
            else if (position == 1)
                _queue2 = val;
            else if (position == 2)
                _queue3 = val;
            else // position == 3
            {
                if (previousLength == 3)
                    Initialize(out _v1, out _v2, out _v3, out _v4);

                _v1 = Round(_v1, _queue1);
                _v2 = Round(_v2, _queue2);
                _v3 = Round(_v3, _queue3);
                _v4 = Round(_v4, val);
            }
        }

        public int ToHashCode()
        {
            uint length = _length;

            uint position = length % 4;

            uint hash = length < 4 ? MixEmptyState() : MixState(_v1, _v2, _v3, _v4);

            hash += length * 4;

            if (position > 0)
            {
                hash = QueueRound(hash, _queue1);
                if (position > 1)
                {
                    hash = QueueRound(hash, _queue2);
                    if (position > 2)
                        hash = QueueRound(hash, _queue3);
                }
            }

            hash = MixFinal(hash);
            return (int)hash;
        }
    }

    public enum DateTimeKind
    {
        Unspecified = 0,
        Utc = 1,
        Local = 2,
    }
    public enum DayOfWeek
    {
        Sunday = 0,
        Monday = 1,
        Tuesday = 2,
        Wednesday = 3,
        Thursday = 4,
        Friday = 5,
        Saturday = 6,
    }
    public readonly struct TimeOnly
    {
        // represent the number of ticks map to the time of the day. 1 ticks = 100-nanosecond in time measurements.
        private readonly ulong _ticks;

        // MinTimeTicks is the ticks for the midnight time 00:00:00.000 AM
        private const long MinTimeTicks = 0;

        // MaxTimeTicks is the max tick value for the time in the day.
        private const long MaxTimeTicks = TimeSpan.TicksPerDay - 1;

        public static TimeOnly MinValue => new TimeOnly((ulong)MinTimeTicks);

        public static TimeOnly MaxValue => new TimeOnly((ulong)MaxTimeTicks);

        public TimeOnly(int hour, int minute) : this(DateTime.TimeToTicks(hour, minute, 0, 0)) { }

        public TimeOnly(int hour, int minute, int second) : this(DateTime.TimeToTicks(hour, minute, second, 0)) { }

        public TimeOnly(int hour, int minute, int second, int millisecond) : this(DateTime.TimeToTicks(hour, minute, second, millisecond)) { }

        public TimeOnly(int hour, int minute, int second, int millisecond, int microsecond) : this(DateTime.TimeToTicks(hour, minute, second, millisecond, microsecond)) { }

        public TimeOnly(long ticks)
        {
            if ((ulong)ticks > MaxTimeTicks)
            {
                throw new ArgumentOutOfRangeException();
            }

            _ticks = (ulong)ticks;
        }

        // exist to bypass the check in the public constructor.
        internal TimeOnly(ulong ticks) => _ticks = ticks;

        public int Hour => (int)(_ticks / TimeSpan.TicksPerHour);

        public int Minute => (int)((uint)(_ticks / TimeSpan.TicksPerMinute) % (uint)TimeSpan.MinutesPerHour);

        public int Second => (int)((uint)(_ticks / TimeSpan.TicksPerSecond) % (uint)TimeSpan.SecondsPerMinute);

        public int Millisecond => (int)((uint)(_ticks / TimeSpan.TicksPerMillisecond) % (uint)TimeSpan.MillisecondsPerSecond);

        public int Microsecond => (int)(_ticks / TimeSpan.TicksPerMicrosecond % (uint)TimeSpan.MicrosecondsPerMillisecond);

        public int Nanosecond => (int)(_ticks % TimeSpan.TicksPerMicrosecond * TimeSpan.NanosecondsPerTick);

        public long Ticks => (long)_ticks;

        private TimeOnly AddTicks(long ticks)
            => new TimeOnly((_ticks + TimeSpan.TicksPerDay + (ulong)(ticks % TimeSpan.TicksPerDay)) % TimeSpan.TicksPerDay);

        private TimeOnly AddTicks(long ticks, out int wrappedDays)
        {
            (long days, long newTicks) = Math.DivRem(ticks, TimeSpan.TicksPerDay);
            newTicks += (long)_ticks;
            if (newTicks < 0)
            {
                days--;
                newTicks += TimeSpan.TicksPerDay;
            }
            else if (newTicks >= TimeSpan.TicksPerDay)
            {
                days++;
                newTicks -= TimeSpan.TicksPerDay;
            }

            wrappedDays = (int)days;
            return new TimeOnly((ulong)newTicks);
        }

        public TimeOnly Add(TimeSpan value) => AddTicks(value.Ticks);

        public TimeOnly Add(TimeSpan value, out int wrappedDays) => AddTicks(value.Ticks, out wrappedDays);

        public TimeOnly AddHours(double value) => AddTicks((long)(value * TimeSpan.TicksPerHour));

        public TimeOnly AddHours(double value, out int wrappedDays) => AddTicks((long)(value * TimeSpan.TicksPerHour), out wrappedDays);

        public TimeOnly AddMinutes(double value) => AddTicks((long)(value * TimeSpan.TicksPerMinute));

        public TimeOnly AddMinutes(double value, out int wrappedDays) => AddTicks((long)(value * TimeSpan.TicksPerMinute), out wrappedDays);

        public bool IsBetween(TimeOnly start, TimeOnly end)
        {
            ulong time = _ticks;
            ulong startTicks = start._ticks;
            ulong endTicks = end._ticks;

            return startTicks <= endTicks
                ? (time - startTicks < endTicks - startTicks)
                : (time - endTicks >= startTicks - endTicks);
        }

        public static bool operator ==(TimeOnly left, TimeOnly right) => left._ticks == right._ticks;

        public static bool operator !=(TimeOnly left, TimeOnly right) => left._ticks != right._ticks;

        public static bool operator >(TimeOnly left, TimeOnly right) => left._ticks > right._ticks;

        public static bool operator >=(TimeOnly left, TimeOnly right) => left._ticks >= right._ticks;

        public static bool operator <(TimeOnly left, TimeOnly right) => left._ticks < right._ticks;

        public static bool operator <=(TimeOnly left, TimeOnly right) => left._ticks <= right._ticks;

        public static TimeSpan operator -(TimeOnly t1, TimeOnly t2)
        {
            long diff = (long)(t1._ticks - t2._ticks);
            // If the result is negative, add 24h to make it positive again using the sign bit.
            return new TimeSpan(diff + ((diff >> 63) & TimeSpan.TicksPerDay));
        }

        public void Deconstruct(out int hour, out int minute)
        {
            hour = Hour;
            minute = Minute;
        }

        public void Deconstruct(out int hour, out int minute, out int second)
        {
            ToDateTime().GetTime(out hour, out minute, out second);
        }

        public void Deconstruct(out int hour, out int minute, out int second, out int millisecond)
        {
            ToDateTime().GetTime(out hour, out minute, out second, out millisecond);
        }


        public static TimeOnly FromTimeSpan(TimeSpan timeSpan) => new TimeOnly(timeSpan._ticks);

        public static TimeOnly FromDateTime(DateTime dateTime) => new TimeOnly((ulong)dateTime.TimeOfDay.Ticks);

        public TimeSpan ToTimeSpan() => new TimeSpan((long)_ticks);

        internal DateTime ToDateTime() => DateTime.CreateUnchecked((long)_ticks);

        public int CompareTo(TimeOnly value) => _ticks.CompareTo(value._ticks);

        public int CompareTo(object? value)
        {
            if (value == null) return 1;
            if (value is not TimeOnly timeOnly)
            {
                throw new ArgumentException();
            }

            return CompareTo(timeOnly);
        }

        public bool Equals(TimeOnly value) => _ticks == value._ticks;

        public override bool Equals([NotNullWhen(true)] object? value) => value is TimeOnly timeOnly && _ticks == timeOnly._ticks;

        public override int GetHashCode()
        {
            ulong ticks = _ticks;
            return unchecked((int)ticks) ^ (int)(ticks >> 32);
        }
    }
    public readonly struct DateOnly
    {
        private readonly uint _dayNumber;

        // Maps to Jan 1st year 1
        private const int MinDayNumber = 0;

        // Maps to December 31 year 9999.
        private const int MaxDayNumber = DateTime.DaysTo10000 - 1;

        private static uint DayNumberFromDateTime(DateTime dt) => (uint)((ulong)dt.Ticks / TimeSpan.TicksPerDay);

        internal DateTime GetEquivalentDateTime() => DateTime.CreateUnchecked(_dayNumber * TimeSpan.TicksPerDay);

        private DateOnly(uint dayNumber)
        {
            _dayNumber = dayNumber;
        }

        public static DateOnly MinValue => new DateOnly(MinDayNumber);

        public static DateOnly MaxValue => new DateOnly(MaxDayNumber);

        public DateOnly(int year, int month, int day) => _dayNumber = DayNumberFromDateTime(new DateTime(year, month, day));
        public DateOnly(int year, int month, int day, System.Globalization.Calendar calendar)
            => _dayNumber = DayNumberFromDateTime(new DateTime(year, month, day, calendar));
        public static DateOnly FromDayNumber(int dayNumber)
        {
            if ((uint)dayNumber > MaxDayNumber)
            {
                throw new ArgumentOutOfRangeException();
            }

            return new DateOnly((uint)dayNumber);
        }

        public int Year => GetEquivalentDateTime().Year;

        public int Month => GetEquivalentDateTime().Month;

        public int Day => GetEquivalentDateTime().Day;

        public DayOfWeek DayOfWeek => (DayOfWeek)((_dayNumber + 1) % 7);

        public int DayOfYear => GetEquivalentDateTime().DayOfYear;

        public int DayNumber => (int)_dayNumber;

        public DateOnly AddDays(int value)
        {
            uint newDayNumber = _dayNumber + (uint)value;
            if (newDayNumber > MaxDayNumber)
            {
                throw new ArgumentOutOfRangeException();
            }

            return new DateOnly(newDayNumber);
        }

        public DateOnly AddMonths(int value) => new DateOnly(DayNumberFromDateTime(GetEquivalentDateTime().AddMonths(value)));

        public DateOnly AddYears(int value) => new DateOnly(DayNumberFromDateTime(GetEquivalentDateTime().AddYears(value)));

        public static bool operator ==(DateOnly left, DateOnly right) => left._dayNumber == right._dayNumber;
        public static bool operator !=(DateOnly left, DateOnly right) => left._dayNumber != right._dayNumber;
        public static bool operator >(DateOnly left, DateOnly right) => left._dayNumber > right._dayNumber;
        public static bool operator >=(DateOnly left, DateOnly right) => left._dayNumber >= right._dayNumber;
        public static bool operator <(DateOnly left, DateOnly right) => left._dayNumber < right._dayNumber;
        public static bool operator <=(DateOnly left, DateOnly right) => left._dayNumber <= right._dayNumber;

        public void Deconstruct(out int year, out int month, out int day)
            => GetEquivalentDateTime().GetDate(out year, out month, out day);

        public DateTime ToDateTime(TimeOnly time) => DateTime.CreateUnchecked(_dayNumber * TimeSpan.TicksPerDay + time.Ticks);
        public DateTime ToDateTime(TimeOnly time, DateTimeKind kind) => DateTime.SpecifyKind(ToDateTime(time), kind);
        public static DateOnly FromDateTime(DateTime dateTime) => new DateOnly(DayNumberFromDateTime(dateTime));
        public int CompareTo(DateOnly value) => _dayNumber.CompareTo(value._dayNumber);
        public int CompareTo(object? value)
        {
            if (value == null) return 1;
            if (value is not DateOnly dateOnly)
            {
                throw new ArgumentException();
            }

            return CompareTo(dateOnly);
        }

        public bool Equals(DateOnly value) => _dayNumber == value._dayNumber;

        public override bool Equals([NotNullWhen(true)] object? value) => value is DateOnly dateOnly && _dayNumber == dateOnly._dayNumber;

        public override int GetHashCode() => (int)_dayNumber;
    }
    public readonly partial struct DateTime
    {
        internal static bool SystemSupportsLeapSeconds => true;
        private static unsafe bool IsValidTimeWithLeapSeconds(DateTime value) => true;

        // Number of days in a non-leap year
        private const int DaysPerYear = 365;
        // Number of days in 4 years
        private const int DaysPer4Years = DaysPerYear * 4 + 1;       // 1461
        // Number of days in 100 years
        private const int DaysPer100Years = DaysPer4Years * 25 - 1;  // 36524
        // Number of days in 400 years
        private const int DaysPer400Years = DaysPer100Years * 4 + 1; // 146097

        // Number of days from 1/1/0001 to 12/31/1600
        private const int DaysTo1601 = DaysPer400Years * 4;          // 584388
        // Number of days from 1/1/0001 to 12/30/1899
        private const int DaysTo1899 = DaysPer400Years * 4 + DaysPer100Years * 3 - 367;
        // Number of days from 1/1/0001 to 12/31/1969
        internal const int DaysTo1970 = DaysPer400Years * 4 + DaysPer100Years * 3 + DaysPer4Years * 17 + DaysPerYear; // 719,162
        // Number of days from 1/1/0001 to 12/31/9999
        internal const int DaysTo10000 = DaysPer400Years * 25 - 366;  // 3652059

        internal const long MinTicks = 0;
        internal const long MaxTicks = DaysTo10000 * TimeSpan.TicksPerDay - 1;
        private const long MaxMicroseconds = MaxTicks / TimeSpan.TicksPerMicrosecond;
        private const long MaxMillis = MaxTicks / TimeSpan.TicksPerMillisecond;
        private const long MaxSeconds = MaxTicks / TimeSpan.TicksPerSecond;
        private const long MaxMinutes = MaxTicks / TimeSpan.TicksPerMinute;
        private const long MaxHours = MaxTicks / TimeSpan.TicksPerHour;
        private const long MaxDays = (long)DaysTo10000 - 1;

        internal const long UnixEpochTicks = DaysTo1970 * TimeSpan.TicksPerDay;
        private const long FileTimeOffset = DaysTo1601 * TimeSpan.TicksPerDay;
        private const long DoubleDateOffset = DaysTo1899 * TimeSpan.TicksPerDay;
        // The minimum OA date is 0100/01/01 (Note it's year 100).
        // The maximum OA date is 9999/12/31
        private const long OADateMinAsTicks = (DaysPer100Years - DaysPerYear) * TimeSpan.TicksPerDay;
        // All OA dates must be greater than (not >=) OADateMinAsDouble
        private const double OADateMinAsDouble = -657435.0;
        // All OA dates must be less than (not <=) OADateMaxAsDouble
        private const double OADateMaxAsDouble = 2958466.0;

        // Euclidean Affine Functions Algorithm (EAF) constants

        // Constants used for fast calculation of following subexpressions
        //      x / DaysPer4Years
        //      x % DaysPer4Years / 4
        private const uint EafMultiplier = (uint)(((1UL << 32) + DaysPer4Years - 1) / DaysPer4Years);   // 2,939,745
        private const uint EafDivider = EafMultiplier * 4;                                              // 11,758,980

        private const ulong TicksPer6Hours = TimeSpan.TicksPerHour * 6;
        private const int March1BasedDayOfNewYear = 306;              // Days between March 1 and January 1

        internal static ReadOnlySpan<uint> DaysToMonth365 => [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334, 365];
        internal static ReadOnlySpan<uint> DaysToMonth366 => [0, 31, 60, 91, 121, 152, 182, 213, 244, 274, 305, 335, 366];

        private static ReadOnlySpan<byte> DaysInMonth365 => [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
        private static ReadOnlySpan<byte> DaysInMonth366 => [31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

        public static readonly DateTime MinValue;
        public static readonly DateTime MaxValue = new DateTime(MaxTicks, DateTimeKind.Unspecified);
        public static readonly DateTime UnixEpoch = new DateTime(UnixEpochTicks, DateTimeKind.Utc);

        private const ulong TicksMask = 0x3FFFFFFFFFFFFFFF;
        private const ulong FlagsMask = 0xC000000000000000;
        private const long TicksCeiling = 0x4000000000000000;
        internal const ulong KindUtc = 0x4000000000000000;
        private const ulong KindLocal = 0x8000000000000000;
        private const ulong KindLocalAmbiguousDst = 0xC000000000000000;
        private const int KindShift = 62;

        private const string TicksField = "ticks"; // Do not rename (binary serialization)
        private const string DateDataField = "dateData"; // Do not rename (binary serialization)

        internal readonly ulong _dateData;

        public DateTime(long ticks)
        {
            if ((ulong)ticks > MaxTicks) ThrowTicksOutOfRange();
            _dateData = (ulong)ticks;
        }

        private DateTime(ulong dateData)
        {
            _dateData = dateData;
        }

        internal static DateTime CreateUnchecked(long ticks) => new DateTime((ulong)ticks);

        public DateTime(long ticks, DateTimeKind kind)
        {
            if ((ulong)ticks > MaxTicks) ThrowTicksOutOfRange();
            if ((uint)kind > (uint)DateTimeKind.Local) ThrowInvalidKind();
            _dateData = (ulong)ticks | ((ulong)(uint)kind << KindShift);
        }
        public DateTime(DateOnly date, TimeOnly time)
        {
            _dateData = (ulong)(date.DayNumber * TimeSpan.TicksPerDay + time.Ticks);
        }
        public DateTime(DateOnly date, TimeOnly time, DateTimeKind kind)
        {
            if ((uint)kind > (uint)DateTimeKind.Local) ThrowInvalidKind();
            _dateData = (ulong)(date.DayNumber * TimeSpan.TicksPerDay + time.Ticks) | ((ulong)(uint)kind << KindShift);
        }

        internal DateTime(long ticks, DateTimeKind kind, bool isAmbiguousDst)
        {
            if ((ulong)ticks > MaxTicks) ThrowTicksOutOfRange();
            _dateData = ((ulong)ticks | (isAmbiguousDst ? KindLocalAmbiguousDst : KindLocal));
        }

        private static void ThrowTicksOutOfRange() => throw new ArgumentOutOfRangeException("ticks");
        private static void ThrowInvalidKind() => throw new ArgumentException("kind");
        internal static void ThrowMillisecondOutOfRange() => throw new ArgumentOutOfRangeException("millisecond");
        internal static void ThrowMicrosecondOutOfRange() => throw new ArgumentOutOfRangeException("microsecond");
        private static void ThrowDateArithmetic(int param) => throw new ArgumentOutOfRangeException();
        private static void ThrowAddOutOfRange() => throw new ArgumentOutOfRangeException("value");

        public DateTime(int year, int month, int day)
        {
            _dateData = DateToTicks(year, month, day);
        }

        public DateTime(int year, int month, int day, System.Globalization.Calendar calendar)
            : this(year, month, day, 0, 0, 0, calendar)
        {
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second, int millisecond, System.Globalization.Calendar calendar, DateTimeKind kind)
        {
            if (calendar == null) throw new ArgumentNullException();

            if ((uint)millisecond >= TimeSpan.MillisecondsPerSecond) ThrowMillisecondOutOfRange();
            if ((uint)kind > (uint)DateTimeKind.Local) ThrowInvalidKind();

            if (second != 60 || !SystemSupportsLeapSeconds)
            {
                ulong ticks = calendar.ToDateTime(year, month, day, hour, minute, second, millisecond).UTicks;
                _dateData = ticks | ((ulong)(uint)kind << KindShift);
            }
            else
            {
                _dateData = WithLeapSecond(calendar, year, month, day, hour, minute, millisecond, kind);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ulong WithLeapSecond(
            System.Globalization.Calendar calendar, int year, int month, int day, int hour, int minute, int millisecond, DateTimeKind kind)
        {
            // if we have a leap second, then we adjust it to 59 so that DateTime will consider it the last in the specified minute.
            return ValidateLeapSecond(new DateTime(year, month, day, hour, minute, 59, millisecond, calendar, kind));
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second)
        {
            ulong ticks = DateToTicks(year, month, day);
            if (second != 60 || !SystemSupportsLeapSeconds)
            {
                _dateData = ticks + TimeToTicks(hour, minute, second);
            }
            else
            {
                _dateData = WithLeapSecond(ticks, hour, minute);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ulong WithLeapSecond(ulong ticks, int hour, int minute)
        {
            // if we have a leap second, then we adjust it to 59 so that DateTime will consider it the last in the specified minute.
            return ValidateLeapSecond(new DateTime(ticks + TimeToTicks(hour, minute, 59)));
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second, DateTimeKind kind)
        {
            if ((uint)kind > (uint)DateTimeKind.Local) ThrowInvalidKind();

            ulong ticks = DateToTicks(year, month, day) | ((ulong)(uint)kind << KindShift);
            if (second != 60 || !SystemSupportsLeapSeconds)
            {
                _dateData = ticks + TimeToTicks(hour, minute, second);
            }
            else
            {
                _dateData = WithLeapSecond(ticks, hour, minute);
            }
        }

        // Constructs a DateTime from a given year, month, day, hour,
        // minute, and second for the specified calendar.
        //
        public DateTime(int year, int month, int day, int hour, int minute, int second, System.Globalization.Calendar calendar)
        {
            if (calendar == null) throw new ArgumentNullException();

            if (second != 60 || !SystemSupportsLeapSeconds)
            {
                _dateData = calendar.ToDateTime(year, month, day, hour, minute, second, 0).UTicks;
            }
            else
            {
                _dateData = WithLeapSecond(calendar, year, month, day, hour, minute);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ulong WithLeapSecond(System.Globalization.Calendar calendar, int year, int month, int day, int hour, int minute)
        {
            // if we have a leap second, then we adjust it to 59 so that DateTime will consider it the last in the specified minute.
            return ValidateLeapSecond(new DateTime(year, month, day, hour, minute, 59, calendar));
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second, int millisecond)
            : this(year, month, day, hour, minute, second)
        {
            if ((uint)millisecond >= TimeSpan.MillisecondsPerSecond) ThrowMillisecondOutOfRange();
            _dateData += (uint)millisecond * (uint)TimeSpan.TicksPerMillisecond;
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second, int millisecond, DateTimeKind kind)
            : this(year, month, day, hour, minute, second, kind)
        {
            if ((uint)millisecond >= TimeSpan.MillisecondsPerSecond) ThrowMillisecondOutOfRange();
            _dateData += (uint)millisecond * (uint)TimeSpan.TicksPerMillisecond;
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second, int millisecond, System.Globalization.Calendar calendar)
        {
            if (calendar == null) throw new ArgumentNullException();

            if (second != 60 || !SystemSupportsLeapSeconds)
            {
                _dateData = calendar.ToDateTime(year, month, day, hour, minute, second, millisecond).UTicks;
            }
            else
            {
                _dateData = WithLeapSecond(calendar, year, month, day, hour, minute, millisecond);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ulong WithLeapSecond(System.Globalization.Calendar calendar,
            int year, int month, int day, int hour, int minute, int millisecond)
        {
            // if we have a leap second, then we adjust it to 59 so that DateTime will consider it the last in the specified minute.
            return ValidateLeapSecond(new DateTime(year, month, day, hour, minute, 59, millisecond, calendar));
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second, int millisecond, int microsecond)
            : this(year, month, day, hour, minute, second, millisecond, microsecond, DateTimeKind.Unspecified)
        {
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second, int millisecond, int microsecond, DateTimeKind kind)
            : this(year, month, day, hour, minute, second, millisecond, kind)
        {
            if ((uint)microsecond >= TimeSpan.MicrosecondsPerMillisecond) ThrowMicrosecondOutOfRange();
            _dateData += (uint)microsecond * (uint)TimeSpan.TicksPerMicrosecond;
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second,
            int millisecond, int microsecond, System.Globalization.Calendar calendar)
           : this(year, month, day, hour, minute, second, millisecond, microsecond, calendar, DateTimeKind.Unspecified)
        {
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second,
            int millisecond, int microsecond, System.Globalization.Calendar calendar, DateTimeKind kind)
            : this(year, month, day, hour, minute, second, millisecond, calendar, kind)
        {
            if ((uint)microsecond >= TimeSpan.MicrosecondsPerMillisecond) ThrowMicrosecondOutOfRange();
            _dateData += (uint)microsecond * (uint)TimeSpan.TicksPerMicrosecond;
        }

        internal static ulong ValidateLeapSecond(DateTime value)
        {
            if (!IsValidTimeWithLeapSeconds(value))
            {
                throw new ArgumentOutOfRangeException();
            }
            return value._dateData;
        }

        private ulong UTicks => _dateData & TicksMask;

        private ulong InternalKind => _dateData & FlagsMask;

        // Returns the DateTime resulting from adding the given
        // TimeSpan to this DateTime.
        //
        public DateTime Add(TimeSpan value)
        {
            return AddTicks(value._ticks);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private DateTime AddUnits(double value, long maxUnitCount, long ticksPerUnit)
        {
            if (Math.Abs(value) > maxUnitCount)
            {
                ThrowAddOutOfRange();
            }

            double integralPart = Math.Truncate(value);
            double fractionalPart = value - integralPart;
            long ticks = (long)(integralPart) * ticksPerUnit;
            ticks += (long)(fractionalPart * ticksPerUnit);

            return AddTicks(ticks);
        }

        public DateTime AddDays(double value) => AddUnits(value, MaxDays, TimeSpan.TicksPerDay);

        public DateTime AddHours(double value) => AddUnits(value, MaxHours, TimeSpan.TicksPerHour);

        public DateTime AddMilliseconds(double value) => AddUnits(value, MaxMillis, TimeSpan.TicksPerMillisecond);

        public DateTime AddMicroseconds(double value) => AddUnits(value, MaxMicroseconds, TimeSpan.TicksPerMicrosecond);

        public DateTime AddMinutes(double value) => AddUnits(value, MaxMinutes, TimeSpan.TicksPerMinute);

        public DateTime AddMonths(int months) => AddMonths(this, months);
        private static DateTime AddMonths(DateTime date, int months)
        {
            if (months < -120000 || months > 120000) throw new ArgumentOutOfRangeException();
            date.GetDate(out int year, out int month, out int day);
            int y = year, d = day;
            int m = month + months;
            int q = m > 0 ? (int)((uint)(m - 1) / 12) : m / 12 - 1;
            y += q;
            m -= q * 12;
            if (y < 1 || y > 9999) ThrowDateArithmetic(2);
            ReadOnlySpan<uint> daysTo = IsLeapYear(y) ? DaysToMonth366 : DaysToMonth365;
            uint daysToMonth = daysTo[m - 1];
            int days = (int)(daysTo[m] - daysToMonth);
            if (d > days) d = days;
            uint n = DaysToYear((uint)y) + daysToMonth + (uint)d - 1;
            return new DateTime(n * (ulong)TimeSpan.TicksPerDay + date.UTicks % TimeSpan.TicksPerDay | date.InternalKind);
        }

        public DateTime AddSeconds(double value) => AddUnits(value, MaxSeconds, TimeSpan.TicksPerSecond);

        // Returns the DateTime resulting from adding the given number of
        // 100-nanosecond ticks to this DateTime. The value argument
        // is permitted to be negative.
        //
        public DateTime AddTicks(long value)
        {
            ulong ticks = (ulong)(Ticks + value);
            if (ticks > MaxTicks) ThrowDateArithmetic(0);
            return new DateTime(ticks | InternalKind);
        }

        internal bool TryAddTicks(long value, out DateTime result)
        {
            ulong ticks = (ulong)(Ticks + value);
            if (ticks > MaxTicks)
            {
                result = default;
                return false;
            }
            result = new DateTime(ticks | InternalKind);
            return true;
        }

        public DateTime AddYears(int value) => AddYears(this, value);
        private static DateTime AddYears(DateTime date, int value)
        {
            if (value < -10000 || value > 10000)
            {
                throw new ArgumentOutOfRangeException();
            }
            date.GetDate(out int year, out int month, out int day);
            int y = year + value;
            if (y < 1 || y > 9999) ThrowDateArithmetic(0);
            uint n = DaysToYear((uint)y);

            int m = month - 1, d = day - 1;
            if (IsLeapYear(y))
            {
                n += DaysToMonth366[m];
            }
            else
            {
                if (d == 28 && m == 1) d--;
                n += DaysToMonth365[m];
            }
            n += (uint)d;
            return new DateTime(n * (ulong)TimeSpan.TicksPerDay + date.UTicks % TimeSpan.TicksPerDay | date.InternalKind);
        }

        public static int Compare(DateTime t1, DateTime t2)
        {
            long ticks1 = t1.Ticks;
            long ticks2 = t2.Ticks;
            if (ticks1 > ticks2) return 1;
            if (ticks1 < ticks2) return -1;
            return 0;
        }
        public int CompareTo(object? value)
        {
            if (value == null) return 1;
            if (!(value is DateTime))
            {
                throw new ArgumentException();
            }

            return Compare(this, (DateTime)value);
        }

        public int CompareTo(DateTime value)
        {
            return Compare(this, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong DateToTicks(int year, int month, int day)
        {
            if (year < 1 || year > 9999 || month < 1 || month > 12 || day < 1)
            {
                throw new ArgumentOutOfRangeException();
            }

            ReadOnlySpan<uint> days = RuntimeHelpers.IsKnownConstant(month) && month == 1 || IsLeapYear(year) ? DaysToMonth366 : DaysToMonth365;
            if ((uint)day > days[month] - days[month - 1])
            {
                throw new ArgumentOutOfRangeException();
            }

            uint n = DaysToYear((uint)year) + days[month - 1] + (uint)day - 1;
            return n * (ulong)TimeSpan.TicksPerDay;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint DaysToYear(uint year)
        {
            uint y = year - 1;
            uint cent = y / 100;
            return y * (365 * 4 + 1) / 4 - cent + cent / 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong TimeToTicks(int hour, int minute, int second)
        {
            if ((uint)hour >= 24 || (uint)minute >= 60 || (uint)second >= 60)
            {
                throw new ArgumentOutOfRangeException();
            }

            int totalSeconds = hour * 3600 + minute * 60 + second;
            return (uint)totalSeconds * (ulong)TimeSpan.TicksPerSecond;
        }

        internal static ulong TimeToTicks(int hour, int minute, int second, int millisecond)
        {
            ulong ticks = TimeToTicks(hour, minute, second);

            if ((uint)millisecond >= TimeSpan.MillisecondsPerSecond) ThrowMillisecondOutOfRange();

            ticks += (uint)millisecond * (uint)TimeSpan.TicksPerMillisecond;

            return ticks;
        }

        internal static ulong TimeToTicks(int hour, int minute, int second, int millisecond, int microsecond)
        {
            ulong ticks = TimeToTicks(hour, minute, second, millisecond);

            if ((uint)microsecond >= TimeSpan.MicrosecondsPerMillisecond) ThrowMicrosecondOutOfRange();

            ticks += (uint)microsecond * (uint)TimeSpan.TicksPerMicrosecond;

            return ticks;
        }

        public static DateTime SpecifyKind(DateTime value, DateTimeKind kind)
        {
            if ((uint)kind > (uint)DateTimeKind.Local) ThrowInvalidKind();
            return new DateTime(value.UTicks | ((ulong)(uint)kind << KindShift));
        }

        public DateTime Date => new((UTicks / TimeSpan.TicksPerDay * TimeSpan.TicksPerDay) | InternalKind);

        internal void GetDate(out int year, out int month, out int day) => GetDate(_dateData, out year, out month, out day);
        private static void GetDate(ulong dateData, out int year, out int month, out int day)
        {
            // y100 = number of whole 100-year periods since 3/1/0000
            // r1 = (day number within 100-year period) * 4
            (uint y100, uint r1) = Math.DivRem(((uint)((dateData & TicksMask) / TicksPer6Hours) | 3U) + 1224, DaysPer400Years);
            ulong u2 = Math.BigMul(EafMultiplier, r1 | 3U);
            uint daySinceMarch1 = (uint)u2 / EafDivider;
            uint n3 = 2141 * daySinceMarch1 + 197913;
            year = (int)(100 * y100 + (uint)(u2 >> 32));
            // compute month and day
            month = (int)(n3 >> 16);
            day = (ushort)n3 / 2141 + 1;

            // rollover December 31
            if (daySinceMarch1 >= March1BasedDayOfNewYear)
            {
                ++year;
                month -= 12;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void GetTime(out int hour, out int minute, out int second)
        {
            ulong seconds = UTicks / TimeSpan.TicksPerSecond;
            ulong minutes = seconds / 60;
            second = (int)(seconds - (minutes * 60));
            ulong hours = minutes / 60;
            minute = (int)(minutes - (hours * 60));
            hour = (int)((uint)hours % 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void GetTime(out int hour, out int minute, out int second, out int millisecond)
        {
            ulong milliseconds = UTicks / TimeSpan.TicksPerMillisecond;
            ulong seconds = milliseconds / 1000;
            millisecond = (int)(milliseconds - (seconds * 1000));
            ulong minutes = seconds / 60;
            second = (int)(seconds - (minutes * 60));
            ulong hours = minutes / 60;
            minute = (int)(minutes - (hours * 60));
            hour = (int)((uint)hours % 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void GetTimePrecise(out int hour, out int minute, out int second, out int tick)
        {
            ulong ticks = UTicks;
            ulong seconds = ticks / TimeSpan.TicksPerSecond;
            tick = (int)(ticks - (seconds * TimeSpan.TicksPerSecond));
            ulong minutes = seconds / 60;
            second = (int)(seconds - (minutes * 60));
            ulong hours = minutes / 60;
            minute = (int)(minutes - (hours * 60));
            hour = (int)((uint)hours % 24);
        }

        public int Day
        {
            get
            {
                // r1 = (day number within 100-year period) * 4
                uint r1 = (((uint)(UTicks / TicksPer6Hours) | 3U) + 1224) % DaysPer400Years;
                ulong u2 = Math.BigMul(EafMultiplier, r1 | 3U);
                ushort daySinceMarch1 = (ushort)((uint)u2 / EafDivider);
                int n3 = 2141 * daySinceMarch1 + 197913;
                // Return 1-based day-of-month
                return (ushort)n3 / 2141 + 1;
            }
        }

        public DayOfWeek DayOfWeek => (DayOfWeek)(((uint)(UTicks / TimeSpan.TicksPerDay) + 1) % 7);

        // Returns the day-of-year part of this DateTime. The returned value
        // is an integer between 1 and 366.
        //
        public int DayOfYear =>
            1 + (int)(((((uint)(UTicks / TicksPer6Hours) | 3U) % (uint)DaysPer400Years) | 3U) * EafMultiplier / EafDivider);

        // Returns the hash code for this DateTime.
        //
        public override int GetHashCode()
        {
            long ticks = Ticks;
            return unchecked((int)ticks) ^ (int)(ticks >> 32);
        }

        // Returns the hour part of this DateTime. The returned value is an
        // integer between 0 and 23.
        //
        public int Hour => (int)((uint)(UTicks / TimeSpan.TicksPerHour) % 24);

        internal bool IsAmbiguousDaylightSavingTime() => _dateData >= KindLocalAmbiguousDst;

        public DateTimeKind Kind
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                uint kind = (uint)(_dateData >> KindShift);
                // values 0-2 map directly to DateTimeKind, 3 (LocalAmbiguousDst) needs to be mapped to 2 (Local) using bit0 NAND bit1
                return (DateTimeKind)(kind & ~(kind >> 1));
            }
        }

        // Returns the millisecond part of this DateTime. The returned value
        // is an integer between 0 and 999.
        //
        public int Millisecond => (int)((UTicks / TimeSpan.TicksPerMillisecond) % 1000);

        /// <summary>
        /// The microseconds component, expressed as a value between 0 and 999.
        /// </summary>
        public int Microsecond => (int)((UTicks / TimeSpan.TicksPerMicrosecond) % 1000);

        /// <summary>
        /// The nanoseconds component, expressed as a value between 0 and 900 (in increments of 100 nanoseconds).
        /// </summary>
        public int Nanosecond => (int)(UTicks % TimeSpan.TicksPerMicrosecond) * 100;

        // Returns the minute part of this DateTime. The returned value is
        // an integer between 0 and 59.
        //
        public int Minute => (int)((UTicks / TimeSpan.TicksPerMinute) % 60);

        // Returns the month part of this DateTime. The returned value is an
        // integer between 1 and 12.
        //
        public int Month
        {
            get
            {
                // r1 = (day number within 100-year period) * 4
                uint r1 = (((uint)(UTicks / TicksPer6Hours) | 3U) + 1224) % DaysPer400Years;
                ulong u2 = Math.BigMul(EafMultiplier, r1 | 3U);
                ushort daySinceMarch1 = (ushort)((uint)u2 / EafDivider);
                int n3 = 2141 * daySinceMarch1 + 197913;
                return (ushort)(n3 >> 16) - (daySinceMarch1 >= March1BasedDayOfNewYear ? 12 : 0);
            }
        }

        // Returns the second part of this DateTime. The returned value is
        // an integer between 0 and 59.
        //
        public int Second => (int)((UTicks / TimeSpan.TicksPerSecond) % 60);

        // Returns the tick count for this DateTime. The returned value is
        // the number of 100-nanosecond intervals that have elapsed since 1/1/0001
        // 12:00am.
        //
        public long Ticks => (long)(_dateData & TicksMask);

        // Returns the time-of-day part of this DateTime. The returned value
        // is a TimeSpan that indicates the time elapsed since midnight.
        //
        public TimeSpan TimeOfDay => new TimeSpan((long)(UTicks % TimeSpan.TicksPerDay));

        public int Year => GetYear(_dateData);
        private static int GetYear(ulong dateData)
        {
            // y100 = number of whole 100-year periods since 1/1/0001
            // r1 = (day number within 100-year period) * 4
            (uint y100, uint r1) = Math.DivRem(((uint)((dateData & TicksMask) / TicksPer6Hours) | 3U), DaysPer400Years);
            return 1 + (int)(100 * y100 + (r1 | 3) / DaysPer4Years);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLeapYear(int year)
        {
            if (year < 1 || year > 9999)
            {
                throw new ArgumentOutOfRangeException();
            }
            if ((year & 3) != 0) return false;
            if ((year & 15) == 0) return true;
            return (uint)year % 25 != 0;
        }

        public TimeSpan Subtract(DateTime value)
        {
            return new TimeSpan(Ticks - value.Ticks);
        }

        public DateTime Subtract(TimeSpan value)
        {
            ulong ticks = (ulong)(Ticks - value._ticks);
            if (ticks > MaxTicks) ThrowDateArithmetic(0);
            return new DateTime(ticks | InternalKind);
        }

        private static double TicksToOADate(long value)
        {
            if (value == 0)
                return 0.0;  // Returns OleAut's zero'ed date value.
            if (value < TimeSpan.TicksPerDay) // This is a fix for VB. They want the default day to be 1/1/0001 rather than 12/30/1899.
                value += DoubleDateOffset; // We could have moved this fix down but we would like to keep the bounds check.
            if (value < OADateMinAsTicks)
                throw new OverflowException();
            // Currently, our max date == OA's max date (12/31/9999), so we don't
            // need an overflow check in that direction.
            long millis = (value - DoubleDateOffset) / TimeSpan.TicksPerMillisecond;
            if (millis < 0)
            {
                long frac = millis % TimeSpan.MillisecondsPerDay;
                if (frac != 0) millis -= (TimeSpan.MillisecondsPerDay + frac) * 2;
            }
            return (double)millis / TimeSpan.MillisecondsPerDay;
        }

        // Converts the DateTime instance into an OLE Automation compatible
        // double date.
        public double ToOADate()
        {
            return TicksToOADate(Ticks);
        }

        public static DateTime operator +(DateTime d, TimeSpan t)
        {
            ulong ticks = (ulong)(d.Ticks + t._ticks);
            if (ticks > MaxTicks) ThrowDateArithmetic(1);
            return new DateTime(ticks | d.InternalKind);
        }

        public static DateTime operator -(DateTime d, TimeSpan t)
        {
            ulong ticks = (ulong)(d.Ticks - t._ticks);
            if (ticks > MaxTicks) ThrowDateArithmetic(1);
            return new DateTime(ticks | d.InternalKind);
        }

        public static TimeSpan operator -(DateTime d1, DateTime d2) => new TimeSpan(d1.Ticks - d2.Ticks);

        public static bool operator ==(DateTime d1, DateTime d2) => ((d1._dateData ^ d2._dateData) << 2) == 0;

        public static bool operator !=(DateTime d1, DateTime d2) => !(d1 == d2);

        public static bool operator <(DateTime t1, DateTime t2) => t1.Ticks < t2.Ticks;

        public static bool operator <=(DateTime t1, DateTime t2) => t1.Ticks <= t2.Ticks;

        public static bool operator >(DateTime t1, DateTime t2) => t1.Ticks > t2.Ticks;

        public static bool operator >=(DateTime t1, DateTime t2) => t1.Ticks >= t2.Ticks;

        public void Deconstruct(out DateOnly date, out TimeOnly time)
        {
            date = DateOnly.FromDateTime(this);
            time = TimeOnly.FromDateTime(this);
        }

        public void Deconstruct(out int year, out int month, out int day)
        {
            GetDate(out year, out month, out day);
        }

        public TypeCode GetTypeCode() => TypeCode.DateTime;
    }
    public readonly struct TimeSpan
    {
        public const long NanosecondsPerTick = 100;                                                 //             100

        public const long TicksPerMicrosecond = 10;                                                 //              10

        public const long TicksPerMillisecond = TicksPerMicrosecond * 1000;                         //          10,000

        public const long TicksPerSecond = TicksPerMillisecond * 1000;                              //      10,000,000

        public const long TicksPerMinute = TicksPerSecond * 60;                                     //     600,000,000

        public const long TicksPerHour = TicksPerMinute * 60;                                       //  36,000,000,000

        public const long TicksPerDay = TicksPerHour * 24;                                          // 864,000,000,000

        public const long MicrosecondsPerMillisecond = TicksPerMillisecond / TicksPerMicrosecond;   //           1,000

        public const long MicrosecondsPerSecond = TicksPerSecond / TicksPerMicrosecond;             //       1,000,000

        public const long MicrosecondsPerMinute = TicksPerMinute / TicksPerMicrosecond;             //      60,000,000

        public const long MicrosecondsPerHour = TicksPerHour / TicksPerMicrosecond;                 //   3,600,000,000

        public const long MicrosecondsPerDay = TicksPerDay / TicksPerMicrosecond;                   //  86,400,000,000

        public const long MillisecondsPerSecond = TicksPerSecond / TicksPerMillisecond;             //           1,000

        public const long MillisecondsPerMinute = TicksPerMinute / TicksPerMillisecond;             //          60,000

        public const long MillisecondsPerHour = TicksPerHour / TicksPerMillisecond;                 //       3,600,000

        public const long MillisecondsPerDay = TicksPerDay / TicksPerMillisecond;                   //      86,400,000

        public const long SecondsPerMinute = TicksPerMinute / TicksPerSecond;                       //              60

        public const long SecondsPerHour = TicksPerHour / TicksPerSecond;                           //           3,600

        public const long SecondsPerDay = TicksPerDay / TicksPerSecond;                             //          86,400

        public const long MinutesPerHour = TicksPerHour / TicksPerMinute;                           //              60

        public const long MinutesPerDay = TicksPerDay / TicksPerMinute;                             //           1,440

        public const int HoursPerDay = (int)(TicksPerDay / TicksPerHour);                           //              24

        internal const long MinTicks = long.MinValue;                                               // -9,223,372,036,854,775,808
        internal const long MaxTicks = long.MaxValue;                                               // +9,223,372,036,854,775,807

        internal const long MinMicroseconds = MinTicks / TicksPerMicrosecond;                       // -  922,337,203,685,477,580
        internal const long MaxMicroseconds = MaxTicks / TicksPerMicrosecond;                       // +  922,337,203,685,477,580

        internal const long MinMilliseconds = MinTicks / TicksPerMillisecond;                       // -      922,337,203,685,477
        internal const long MaxMilliseconds = MaxTicks / TicksPerMillisecond;                       // +      922,337,203,685,477

        internal const long MinSeconds = MinTicks / TicksPerSecond;                                 // -          922,337,203,685
        internal const long MaxSeconds = MaxTicks / TicksPerSecond;                                 // +          922,337,203,685

        internal const long MinMinutes = MinTicks / TicksPerMinute;                                 // -           15,372,286,728
        internal const long MaxMinutes = MaxTicks / TicksPerMinute;                                 // +           15,372,286,728

        internal const long MinHours = MinTicks / TicksPerHour;                                     // -              256,204,778
        internal const long MaxHours = MaxTicks / TicksPerHour;                                     // +              256,204,778

        internal const long MinDays = MinTicks / TicksPerDay;                                       // -               10,675,199
        internal const long MaxDays = MaxTicks / TicksPerDay;                                       // +               10,675,199

        internal const long TicksPerTenthSecond = TicksPerMillisecond * 100;

        public static readonly TimeSpan Zero = new TimeSpan(0);

        public static readonly TimeSpan MaxValue = new TimeSpan(MaxTicks);
        public static readonly TimeSpan MinValue = new TimeSpan(MinTicks);

        internal readonly long _ticks; // Do not rename

        public TimeSpan(long ticks)
        {
            _ticks = ticks;
        }
        public TimeSpan(int hours, int minutes, int seconds)
        {
            _ticks = TimeToTicks(hours, minutes, seconds);
        }

        public TimeSpan(int days, int hours, int minutes, int seconds)
            : this(days, hours, minutes, seconds, 0)
        {
        }
        public TimeSpan(int days, int hours, int minutes, int seconds, int milliseconds) :
            this(days, hours, minutes, seconds, milliseconds, 0)
        {
        }
        public TimeSpan(int days, int hours, int minutes, int seconds, int milliseconds, int microseconds)
        {
            long totalMicroseconds = (days * MicrosecondsPerDay)
                                   + (hours * MicrosecondsPerHour)
                                   + (minutes * MicrosecondsPerMinute)
                                   + (seconds * MicrosecondsPerSecond)
                                   + (milliseconds * MicrosecondsPerMillisecond)
                                   + microseconds;

            if ((totalMicroseconds > MaxMicroseconds) || (totalMicroseconds < MinMicroseconds))
            {
                throw new ArgumentOutOfRangeException();
            }
            _ticks = totalMicroseconds * TicksPerMicrosecond;
        }

        public long Ticks => _ticks;

        public int Days => (int)(_ticks / TicksPerDay);

        public int Hours => (int)(_ticks / TicksPerHour % HoursPerDay);

        public int Milliseconds => (int)(_ticks / TicksPerMillisecond % MillisecondsPerSecond);

        public int Microseconds => (int)(_ticks / TicksPerMicrosecond % MicrosecondsPerMillisecond);

        public int Nanoseconds => (int)(_ticks % TicksPerMicrosecond * NanosecondsPerTick);

        public int Minutes => (int)(_ticks / TicksPerMinute % MinutesPerHour);

        public int Seconds => (int)(_ticks / TicksPerSecond % SecondsPerMinute);

        public double TotalDays => (double)_ticks / TicksPerDay;

        public double TotalHours => (double)_ticks / TicksPerHour;

        public double TotalMilliseconds
        {
            get
            {
                double temp = (double)_ticks / TicksPerMillisecond;

                if (temp > MaxMilliseconds)
                {
                    return MaxMilliseconds;
                }

                if (temp < MinMilliseconds)
                {
                    return MinMilliseconds;
                }
                return temp;
            }
        }
        public double TotalMicroseconds => (double)_ticks / TicksPerMicrosecond;

        public double TotalNanoseconds => (double)_ticks * NanosecondsPerTick;

        public double TotalMinutes => (double)_ticks / TicksPerMinute;

        public double TotalSeconds => (double)_ticks / TicksPerSecond;

        public TimeSpan Add(TimeSpan ts) => this + ts;
        public static int Compare(TimeSpan t1, TimeSpan t2) => t1._ticks.CompareTo(t2._ticks);
        public int CompareTo(object? value)
        {
            if (value is null)
            {
                return 1;
            }

            if (value is TimeSpan other)
            {
                return CompareTo(other);
            }

            throw new ArgumentException();
        }
        public int CompareTo(TimeSpan value) => Compare(this, value);

        public static TimeSpan FromDays(double value) => Interval(value, TicksPerDay);

        public TimeSpan Duration()
        {
            if (_ticks == MinTicks)
            {
                throw new OverflowException();
            }
            return new TimeSpan(_ticks >= 0 ? _ticks : -_ticks);
        }

        public override bool Equals([NotNullWhen(true)] object? value) => (value is TimeSpan other) && Equals(other);

        public bool Equals(TimeSpan obj) => Equals(this, obj);

        public static bool Equals(TimeSpan t1, TimeSpan t2) => t1 == t2;

        public override int GetHashCode() => _ticks.GetHashCode();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TimeSpan FromUnits(long units, long ticksPerUnit, long minUnits, long maxUnits)
        {
            if (units > maxUnits || units < minUnits)
            {
                throw new ArgumentOutOfRangeException();
            }
            return TimeSpan.FromTicks(units * ticksPerUnit);
        }

        public static TimeSpan FromDays(int days) => FromUnits(days, TicksPerDay, MinDays, MaxDays);

        public static TimeSpan FromHours(int hours) => FromUnits(hours, TicksPerHour, MinHours, MaxHours);

        public static TimeSpan FromMinutes(long minutes) => FromUnits(minutes, TicksPerMinute, MinMinutes, MaxMinutes);

        public static TimeSpan FromSeconds(long seconds) => FromUnits(seconds, TicksPerSecond, MinSeconds, MaxSeconds);

        public static TimeSpan FromMilliseconds(long milliseconds)
            => FromUnits(milliseconds, TicksPerMillisecond, MinMilliseconds, MaxMilliseconds);

        public static TimeSpan FromMicroseconds(long microseconds) => FromUnits(microseconds, TicksPerMicrosecond, MinMicroseconds, MaxMicroseconds);

        public static TimeSpan FromHours(double value) => Interval(value, TicksPerHour);

        private static TimeSpan Interval(double value, double scale)
        {
            if (double.IsNaN(value))
            {
                throw new ArgumentException();
            }
            return IntervalFromDoubleTicks(value * scale);
        }

        private static TimeSpan IntervalFromDoubleTicks(double ticks)
        {
            if ((ticks > MaxTicks) || (ticks < MinTicks) || double.IsNaN(ticks))
            {
                throw new OverflowException();
            }
            if (ticks == MaxTicks)
            {
                return MaxValue;
            }
            return new TimeSpan((long)ticks);
        }
        public static TimeSpan FromMilliseconds(double value) => Interval(value, TicksPerMillisecond);

        public static TimeSpan FromMicroseconds(double value) => Interval(value, TicksPerMicrosecond);

        public static TimeSpan FromMinutes(double value) => Interval(value, TicksPerMinute);

        public TimeSpan Negate() => -this;

        public static TimeSpan FromSeconds(double value) => Interval(value, TicksPerSecond);

        public TimeSpan Subtract(TimeSpan ts) => this - ts;

        public TimeSpan Multiply(double factor) => this * factor;

        public TimeSpan Divide(double divisor) => this / divisor;

        public double Divide(TimeSpan ts) => this / ts;

        public static TimeSpan FromTicks(long value) => new TimeSpan(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long TimeToTicks(int hour, int minute, int second)
        {
            // totalSeconds is bounded by 2^31 * 2^12 + 2^31 * 2^8 + 2^31,
            // which is less than 2^44, meaning we won't overflow totalSeconds.
            long totalSeconds = (hour * SecondsPerHour)
                              + (minute * SecondsPerMinute)
                              + second;

            if ((totalSeconds > MaxSeconds) || (totalSeconds < MinSeconds))
            {
                throw new ArgumentOutOfRangeException();
            }
            return totalSeconds * TicksPerSecond;
        }

        public static TimeSpan operator -(TimeSpan t)
        {
            if (t._ticks == MinTicks)
            {
                throw new OverflowException();
            }
            return new TimeSpan(-t._ticks);
        }

        public static TimeSpan operator -(TimeSpan t1, TimeSpan t2)
        {
            long result = t1._ticks - t2._ticks;
            long t1Sign = t1._ticks >> 63;

            if ((t1Sign != (t2._ticks >> 63)) && (t1Sign != (result >> 63)))
            {
                // Overflow if signs of operands was different and result's sign was opposite.
                // >> 63 gives the sign bit (either 64 1's or 64 0's).
                throw new OverflowException();
            }
            return new TimeSpan(result);
        }

        public static TimeSpan operator +(TimeSpan t) => t;

        public static TimeSpan operator +(TimeSpan t1, TimeSpan t2)
        {
            long result = t1._ticks + t2._ticks;
            long t1Sign = t1._ticks >> 63;

            if ((t1Sign == (t2._ticks >> 63)) && (t1Sign != (result >> 63)))
            {
                // Overflow if signs of operands was identical and result's sign was opposite.
                // >> 63 gives the sign bit (either 64 1's or 64 0's).
                throw new OverflowException();
            }
            return new TimeSpan(result);
        }

        public static TimeSpan operator *(TimeSpan timeSpan, double factor)
        {
            if (double.IsNaN(factor))
            {
                throw new ArgumentException();
            }

            // Rounding to the nearest tick is as close to the result we would have with unlimited
            // precision as possible, and so likely to have the least potential to surprise.
            double ticks = Math.Round(timeSpan.Ticks * factor);
            return IntervalFromDoubleTicks(ticks);
        }

        public static TimeSpan operator *(double factor, TimeSpan timeSpan) => timeSpan * factor;

        public static TimeSpan operator /(TimeSpan timeSpan, double divisor)
        {
            if (double.IsNaN(divisor))
            {
                throw new ArgumentException();
            }

            double ticks = Math.Round(timeSpan.Ticks / divisor);
            return IntervalFromDoubleTicks(ticks);
        }

        public static double operator /(TimeSpan t1, TimeSpan t2) => t1.Ticks / (double)t2.Ticks;

        public static bool operator ==(TimeSpan t1, TimeSpan t2) => t1._ticks == t2._ticks;

        public static bool operator !=(TimeSpan t1, TimeSpan t2) => t1._ticks != t2._ticks;

        public static bool operator <(TimeSpan t1, TimeSpan t2) => t1._ticks < t2._ticks;

        public static bool operator <=(TimeSpan t1, TimeSpan t2) => t1._ticks <= t2._ticks;

        public static bool operator >(TimeSpan t1, TimeSpan t2) => t1._ticks > t2._ticks;

        public static bool operator >=(TimeSpan t1, TimeSpan t2) => t1._ticks >= t2._ticks;
    }

    internal static class Marvin
    {
        /// <summary>
        /// Compute a Marvin hash and collapse it into a 32-bit hash.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ComputeHash32(ReadOnlySpan<byte> data, ulong seed)
            => ComputeHash32(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(data), (uint)data.Length, (uint)seed, (uint)(seed >> 32));

        /// <summary>
        /// Compute a Marvin hash and collapse it into a 32-bit hash.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ComputeHash32(ref byte data, uint count, uint p0, uint p1)
        {
            // Control flow of this method generally flows top-to-bottom, trying to
            // minimize the number of branches taken for large (>= 8 bytes, 4 chars) inputs.
            // If small inputs (< 8 bytes, 4 chars) are given, this jumps to a "small inputs"
            // handler at the end of the method.

            if (count < 8)
            {
                // We can't run the main loop, but we might still have 4 or more bytes available to us.
                // If so, jump to the 4 .. 7 bytes logic immediately after the main loop.

                if (count >= 4)
                {
                    goto Between4And7BytesRemain;
                }
                else
                {
                    goto InputTooSmallToEnterMainLoop;
                }
            }

            // Main loop - read 8 bytes at a time.
            // The block function is unrolled 2x in this loop.

            uint loopCount = count / 8;

            do
            {
                p0 += Unsafe.ReadUnaligned<uint>(ref data);
                uint nextUInt32 = Unsafe.ReadUnaligned<uint>(ref Unsafe.AddByteOffset(ref data, 4));

                // One block round for each of the 32-bit integers we just read, 2x rounds total.

                Block(ref p0, ref p1);
                p0 += nextUInt32;
                Block(ref p0, ref p1);

                // Bump the data reference pointer and decrement the loop count.

                data = ref Unsafe.AddByteOffset(ref data, 8);
            } while (--loopCount > 0);

            // n.b. We've not been updating the original 'count' parameter, so its actual value is
            // still the original data length. However, we can still rely on its least significant
            // 3 bits to tell us how much data remains (0 .. 7 bytes) after the loop above is
            // completed.

            if ((count & 0b_0100) == 0)
            {
                goto DoFinalPartialRead;
            }

        Between4And7BytesRemain:

            // If after finishing the main loop we still have 4 or more leftover bytes, or if we had
            // 4 .. 7 bytes to begin with and couldn't enter the loop in the first place, we need to
            // consume 4 bytes immediately and send them through one round of the block function.

            p0 += Unsafe.ReadUnaligned<uint>(ref data);
            Block(ref p0, ref p1);

        DoFinalPartialRead:

            // Finally, we have 0 .. 3 bytes leftover. Since we know the original data length was at
            // least 4 bytes (smaller lengths are handled at the end of this routine), we can safely
            // read the 4 bytes at the end of the buffer without reading past the beginning of the
            // original buffer. This necessarily means the data we're about to read will overlap with
            // some data we've already processed, but we can handle that below.

            uint partialResult = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref Unsafe.AddByteOffset(ref data, (nuint)count & 7), -4));

            // The 'partialResult' local above contains any data we have yet to read, plus some number
            // of bytes which we've already read from the buffer. An example of this is given below
            // for little-endian architectures. In this table, AA BB CC are the bytes which we still
            // need to consume, and ## are bytes which we want to throw away since we've already
            // consumed them as part of a previous read.
            //
            //                                                    (partialResult contains)   (we want it to contain)
            // count mod 4 = 0 -> [ ## ## ## ## |             ] -> 0x####_####             -> 0x0000_0080
            // count mod 4 = 1 -> [ ## ## ## ## | AA          ] -> 0xAA##_####             -> 0x0000_80AA
            // count mod 4 = 2 -> [ ## ## ## ## | AA BB       ] -> 0xBBAA_####             -> 0x0080_BBAA
            // count mod 4 = 3 -> [ ## ## ## ## | AA BB CC    ] -> 0xCCBB_AA##             -> 0x80CC_BBAA

            count = ~count << 3;

            if (BitConverter.IsLittleEndian)
            {
                partialResult >>= 8; // make some room for the 0x80 byte
                partialResult |= 0x8000_0000u; // put the 0x80 byte at the beginning
                partialResult >>= (int)count & 0x1F; // shift out all previously consumed bytes
            }
            else
            {
                partialResult <<= 8; // make some room for the 0x80 byte
                partialResult |= 0x80u; // put the 0x80 byte at the end
                partialResult <<= (int)count & 0x1F; // shift out all previously consumed bytes
            }

        DoFinalRoundsAndReturn:

            // Now that we've computed the final partial result, merge it in and run two rounds of
            // the block function to finish out the Marvin algorithm.

            p0 += partialResult;
            Block(ref p0, ref p1);
            Block(ref p0, ref p1);

            return (int)(p1 ^ p0);

        InputTooSmallToEnterMainLoop:

            // We had only 0 .. 3 bytes to begin with, so we can't perform any 32-bit reads.
            // This means that we're going to be building up the final result right away and
            // will only ever run two rounds total of the block function. Let's initialize
            // the partial result to "no data".

            if (BitConverter.IsLittleEndian)
            {
                partialResult = 0x80u;
            }
            else
            {
                partialResult = 0x80000000u;
            }

            if ((count & 0b_0001) != 0)
            {
                // If the buffer is 1 or 3 bytes in length, let's read a single byte now
                // and merge it into our partial result. This will result in partialResult
                // having one of the two values below, where AA BB CC are the buffer bytes.
                //
                //                  (little-endian / big-endian)
                // [ AA          ]  -> 0x0000_80AA / 0xAA80_0000
                // [ AA BB CC    ]  -> 0x0000_80CC / 0xCC80_0000

                partialResult = Unsafe.AddByteOffset(ref data, (nuint)count & 2);

                if (BitConverter.IsLittleEndian)
                {
                    partialResult |= 0x8000;
                }
                else
                {
                    partialResult <<= 24;
                    partialResult |= 0x800000u;
                }
            }

            if ((count & 0b_0010) != 0)
            {
                // If the buffer is 2 or 3 bytes in length, let's read a single ushort now
                // and merge it into the partial result. This will result in partialResult
                // having one of the two values below, where AA BB CC are the buffer bytes.
                //
                //                  (little-endian / big-endian)
                // [ AA BB       ]  -> 0x0080_BBAA / 0xAABB_8000
                // [ AA BB CC    ]  -> 0x80CC_BBAA / 0xAABB_CC80 (carried over from above)

                if (BitConverter.IsLittleEndian)
                {
                    partialResult <<= 16;
                    partialResult |= (uint)Unsafe.ReadUnaligned<ushort>(ref data);
                }
                else
                {
                    partialResult |= (uint)Unsafe.ReadUnaligned<ushort>(ref data);
                    partialResult = System.Numerics.BitOperations.RotateLeft(partialResult, 16);
                }
            }

            // Everything is consumed! Go perform the final rounds and return.

            goto DoFinalRoundsAndReturn;
        }

        /// <summary>
        /// Compute a Marvin OrdinalIgnoreCase hash and collapse it into a 32-bit hash.
        /// n.b. <paramref name="count"/> is specified as char count, not byte count.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ComputeHash32OrdinalIgnoreCase(ref char data, int count, uint p0, uint p1)
        {
            uint ucount = (uint)count; // in chars
            nuint byteOffset = 0; // in bytes
            uint tempValue;

            // We operate on 32-bit integers (two chars) at a time.

            while (ucount >= 2)
            {
                tempValue = Unsafe.ReadUnaligned<uint>(ref Unsafe.As<char, byte>(ref Unsafe.AddByteOffset(ref data, byteOffset)));
                if (!System.Text.Unicode.Utf16Utility.AllCharsInUInt32AreAscii(tempValue))
                {
                    goto NotAscii;
                }
                p0 += System.Text.Unicode.Utf16Utility.ConvertAllAsciiCharsInUInt32ToUppercase(tempValue);
                Block(ref p0, ref p1);

                byteOffset += 4;
                ucount -= 2;
            }

            // We have either one char (16 bits) or zero chars left over.

            if (ucount > 0)
            {
                tempValue = Unsafe.AddByteOffset(ref data, byteOffset);
                if (tempValue > 0x7Fu)
                {
                    goto NotAscii;
                }

                if (BitConverter.IsLittleEndian)
                {
                    // addition is written with -0x80u to allow fall-through to next statement rather than jmp past it
                    p0 += System.Text.Unicode.Utf16Utility.ConvertAllAsciiCharsInUInt32ToUppercase(tempValue) + (0x800000u - 0x80u);
                }
                else
                {
                    // as above, addition is modified to allow fall-through to next statement rather than jmp past it
                    p0 += (System.Text.Unicode.Utf16Utility.ConvertAllAsciiCharsInUInt32ToUppercase(tempValue) << 16) + 0x8000u - 0x80000000u;
                }
            }
            if (BitConverter.IsLittleEndian)
            {
                p0 += 0x80u;
            }
            else
            {
                p0 += 0x80000000u;
            }

            Block(ref p0, ref p1);
            Block(ref p0, ref p1);

            return (int)(p1 ^ p0);

        NotAscii:
            return ComputeHash32OrdinalIgnoreCaseSlow(ref Unsafe.AddByteOffset(ref data, byteOffset), (int)ucount, p0, p1);
        }

        private static unsafe int ComputeHash32OrdinalIgnoreCaseSlow(ref char data, int count, uint p0, uint p1)
        {
            char[]? borrowedArr = null;
            Span<char> scratch = (uint)count <= 64 ? stackalloc char[64] : (borrowedArr = System.Buffers.ArrayPool<char>.Shared.Rent(count));

            int charsWritten = Globalization.Ordinal.ToUpperOrdinal(new ReadOnlySpan<char>(ref data, count), scratch);

            // Slice the array to the size returned by ToUpperInvariant.
            // Multiplication below will not overflow since going from positive Int32 to UInt32.
            int hash = ComputeHash32(ref Unsafe.As<char, byte>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(scratch)), (uint)charsWritten * 2, p0, p1);

            // Return the borrowed array if necessary.
            if (borrowedArr != null)
            {
                System.Buffers.ArrayPool<char>.Shared.Return(borrowedArr);
            }

            return hash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Block(ref uint rp0, ref uint rp1)
        {
            uint p0 = rp0;
            uint p1 = rp1;

            p1 ^= p0;
            p0 = System.Numerics.BitOperations.RotateLeft(p0, 20);

            p0 += p1;
            p1 = System.Numerics.BitOperations.RotateLeft(p1, 9);

            p1 ^= p0;
            p0 = System.Numerics.BitOperations.RotateLeft(p0, 27);

            p0 += p1;
            p1 = System.Numerics.BitOperations.RotateLeft(p1, 19);

            rp0 = p0;
            rp1 = p1;
        }

        public static ulong DefaultSeed { get; } = GenerateSeed();

        private static unsafe ulong GenerateSeed()
        {
            ulong seed = 0;

            return seed;
        }
    }

    public static class Convert
    {
        public static bool ToBoolean(bool value)
        {
            return value;
        }

        public static bool ToBoolean(sbyte value)
        {
            return value != 0;
        }

        public static bool ToBoolean(byte value)
        {
            return value != 0;
        }

        public static bool ToBoolean(short value)
        {
            return value != 0;
        }

        public static bool ToBoolean(ushort value)
        {
            return value != 0;
        }

        public static bool ToBoolean(int value)
        {
            return value != 0;
        }

        public static bool ToBoolean(uint value)
        {
            return value != 0;
        }

        public static bool ToBoolean(long value)
        {
            return value != 0;
        }

        public static bool ToBoolean(ulong value)
        {
            return value != 0;
        }

        public static bool ToBoolean(float value)
        {
            return value != 0;
        }

        public static bool ToBoolean(double value)
        {
            return value != 0;
        }

        public static char ToChar(char value)
        {
            return value;
        }

        public static char ToChar(sbyte value)
        {
            if (value < 0) ThrowCharOverflowException();
            return (char)value;
        }

        public static char ToChar(byte value)
        {
            return (char)value;
        }

        public static char ToChar(short value)
        {
            if (value < 0) ThrowCharOverflowException();
            return (char)value;
        }

        public static char ToChar(ushort value)
        {
            return (char)value;
        }

        public static char ToChar(int value) => ToChar((uint)value);

        public static char ToChar(uint value)
        {
            if (value > char.MaxValue) ThrowCharOverflowException();
            return (char)value;
        }

        public static char ToChar(long value) => ToChar((ulong)value);

        public static char ToChar(ulong value)
        {
            if (value > char.MaxValue) ThrowCharOverflowException();
            return (char)value;
        }

        public static sbyte ToSByte(bool value)
        {
            return value ? (sbyte)bool.True : (sbyte)bool.False;
        }

        public static sbyte ToSByte(sbyte value)
        {
            return value;
        }

        public static sbyte ToSByte(char value)
        {
            if (value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(byte value)
        {
            if (value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(short value)
        {
            if (value < sbyte.MinValue || value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(ushort value)
        {
            if (value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(int value)
        {
            if (value < sbyte.MinValue || value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(uint value)
        {
            if (value > (uint)sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(long value)
        {
            if (value < sbyte.MinValue || value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(ulong value)
        {
            if (value > (ulong)sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(float value)
        {
            return ToSByte((double)value);
        }

        public static sbyte ToSByte(double value)
        {
            return ToSByte(ToInt32(value));
        }

        public static byte ToByte(bool value)
        {
            return value ? (byte)bool.True : (byte)bool.False;
        }

        public static byte ToByte(byte value)
        {
            return value;
        }

        public static byte ToByte(char value)
        {
            if (value > byte.MaxValue) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(sbyte value)
        {
            if (value < 0) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(short value)
        {
            if ((uint)value > byte.MaxValue) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(ushort value)
        {
            if (value > byte.MaxValue) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(int value) => ToByte((uint)value);

        public static byte ToByte(uint value)
        {
            if (value > byte.MaxValue) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(long value) => ToByte((ulong)value);

        public static byte ToByte(ulong value)
        {
            if (value > byte.MaxValue) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(float value)
        {
            return ToByte((double)value);
        }

        public static byte ToByte(double value)
        {
            return ToByte(ToInt32(value));
        }

        public static short ToInt16(bool value)
        {
            return value ? (short)bool.True : (short)bool.False;
        }

        public static short ToInt16(char value)
        {
            if (value > short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(sbyte value)
        {
            return value;
        }

        public static short ToInt16(byte value)
        {
            return value;
        }

        public static short ToInt16(ushort value)
        {
            if (value > short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(int value)
        {
            if (value < short.MinValue || value > short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(uint value)
        {
            if (value > (uint)short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(short value)
        {
            return value;
        }

        public static short ToInt16(long value)
        {
            if (value < short.MinValue || value > short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(ulong value)
        {
            if (value > (ulong)short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(float value)
        {
            return ToInt16((double)value);
        }

        public static short ToInt16(double value)
        {
            return ToInt16(ToInt32(value));
        }

        public static ushort ToUInt16(bool value)
        {
            return value ? (ushort)bool.True : (ushort)bool.False;
        }

        public static ushort ToUInt16(char value)
        {
            return value;
        }

        public static ushort ToUInt16(sbyte value)
        {
            if (value < 0) ThrowUInt16OverflowException();
            return (ushort)value;
        }

        public static ushort ToUInt16(byte value)
        {
            return value;
        }

        public static ushort ToUInt16(short value)
        {
            if (value < 0) ThrowUInt16OverflowException();
            return (ushort)value;
        }

        public static ushort ToUInt16(int value) => ToUInt16((uint)value);

        public static ushort ToUInt16(ushort value)
        {
            return value;
        }

        public static ushort ToUInt16(uint value)
        {
            if (value > ushort.MaxValue) ThrowUInt16OverflowException();
            return (ushort)value;
        }

        public static ushort ToUInt16(long value) => ToUInt16((ulong)value);

        public static ushort ToUInt16(ulong value)
        {
            if (value > ushort.MaxValue) ThrowUInt16OverflowException();
            return (ushort)value;
        }

        public static ushort ToUInt16(float value)
        {
            return ToUInt16((double)value);
        }

        public static ushort ToUInt16(double value)
        {
            return ToUInt16(ToInt32(value));
        }

        public static int ToInt32(bool value)
        {
            return value ? bool.True : bool.False;
        }

        public static int ToInt32(char value)
        {
            return value;
        }

        public static int ToInt32(sbyte value)
        {
            return value;
        }

        public static int ToInt32(byte value)
        {
            return value;
        }

        public static int ToInt32(short value)
        {
            return value;
        }

        public static int ToInt32(ushort value)
        {
            return value;
        }

        public static int ToInt32(uint value)
        {
            if ((int)value < 0) ThrowInt32OverflowException();
            return (int)value;
        }

        public static int ToInt32(int value)
        {
            return value;
        }

        public static int ToInt32(long value)
        {
            if (value < int.MinValue || value > int.MaxValue) ThrowInt32OverflowException();
            return (int)value;
        }

        public static int ToInt32(ulong value)
        {
            if (value > int.MaxValue) ThrowInt32OverflowException();
            return (int)value;
        }

        public static int ToInt32(float value)
        {
            return ToInt32((double)value);
        }

        public static int ToInt32(double value)
        {
            if (value >= 0)
            {
                if (value < 2147483647.5)
                {
                    int result = (int)value;
                    double dif = value - result;
                    if (dif > 0.5 || dif == 0.5 && (result & 1) != 0) result++;
                    return result;
                }
            }
            else
            {
                if (value >= -2147483648.5)
                {
                    int result = (int)value;
                    double dif = value - result;
                    if (dif < -0.5 || dif == -0.5 && (result & 1) != 0) result--;
                    return result;
                }
            }
            throw new OverflowException();
        }


        public static uint ToUInt32(bool value)
        {
            return value ? (uint)bool.True : (uint)bool.False;
        }

        public static uint ToUInt32(char value)
        {
            return value;
        }

        public static uint ToUInt32(sbyte value)
        {
            if (value < 0) ThrowUInt32OverflowException();
            return (uint)value;
        }

        public static uint ToUInt32(byte value)
        {
            return value;
        }

        public static uint ToUInt32(short value)
        {
            if (value < 0) ThrowUInt32OverflowException();
            return (uint)value;
        }

        public static uint ToUInt32(ushort value)
        {
            return value;
        }

        public static uint ToUInt32(int value)
        {
            if (value < 0) ThrowUInt32OverflowException();
            return (uint)value;
        }

        public static uint ToUInt32(uint value)
        {
            return value;
        }

        public static uint ToUInt32(long value) => ToUInt32((ulong)value);

        public static uint ToUInt32(ulong value)
        {
            if (value > uint.MaxValue) ThrowUInt32OverflowException();
            return (uint)value;
        }

        public static uint ToUInt32(float value)
        {
            return ToUInt32((double)value);
        }

        public static uint ToUInt32(double value)
        {
            if (value >= -0.5 && value < 4294967295.5)
            {
                uint result = (uint)value;
                double dif = value - result;
                if (dif > 0.5 || dif == 0.5 && (result & 1) != 0) result++;
                return result;
            }
            throw new OverflowException();
        }


        public static long ToInt64(bool value)
        {
            return value ? bool.True : bool.False;
        }

        public static long ToInt64(char value)
        {
            return value;
        }

        public static long ToInt64(sbyte value)
        {
            return value;
        }

        public static long ToInt64(byte value)
        {
            return value;
        }

        public static long ToInt64(short value)
        {
            return value;
        }

        public static long ToInt64(ushort value)
        {
            return value;
        }

        public static long ToInt64(int value)
        {
            return value;
        }

        public static long ToInt64(uint value)
        {
            return value;
        }

        public static long ToInt64(ulong value)
        {
            if ((long)value < 0) ThrowInt64OverflowException();
            return (long)value;
        }

        public static long ToInt64(long value)
        {
            return value;
        }

        public static long ToInt64(float value)
        {
            return ToInt64((double)value);
        }

        public static long ToInt64(double value)
        {
            return checked((long)Math.Round(value));
        }


        public static ulong ToUInt64(bool value)
        {
            return value ? (ulong)bool.True : (ulong)bool.False;
        }

        public static ulong ToUInt64(char value)
        {
            return value;
        }

        public static ulong ToUInt64(sbyte value)
        {
            if (value < 0) ThrowUInt64OverflowException();
            return (ulong)value;
        }

        public static ulong ToUInt64(byte value)
        {
            return value;
        }

        public static ulong ToUInt64(short value)
        {
            if (value < 0) ThrowUInt64OverflowException();
            return (ulong)value;
        }

        public static ulong ToUInt64(ushort value)
        {
            return value;
        }

        public static ulong ToUInt64(int value)
        {
            if (value < 0) ThrowUInt64OverflowException();
            return (ulong)value;
        }

        public static ulong ToUInt64(uint value)
        {
            return value;
        }

        public static ulong ToUInt64(long value)
        {
            if (value < 0) ThrowUInt64OverflowException();
            return (ulong)value;
        }

        public static ulong ToUInt64(ulong value)
        {
            return value;
        }

        public static ulong ToUInt64(float value)
        {
            return ToUInt64((double)value);
        }

        public static ulong ToUInt64(double value)
        {
            return checked((ulong)Math.Round(value));
        }

        private static void ThrowCharOverflowException() { throw new OverflowException(); }

        private static void ThrowByteOverflowException() { throw new OverflowException(); }

        private static void ThrowSByteOverflowException() { throw new OverflowException(); }

        private static void ThrowInt16OverflowException() { throw new OverflowException(); }

        private static void ThrowUInt16OverflowException() { throw new OverflowException(); }

        private static void ThrowInt32OverflowException() { throw new OverflowException(); }

        private static void ThrowUInt32OverflowException() { throw new OverflowException(); }

        private static void ThrowInt64OverflowException() { throw new OverflowException(); }

        private static void ThrowUInt64OverflowException() { throw new OverflowException(); }
    }
    public abstract class StringComparer : IComparer
    {
        public static StringComparer InvariantCulture => CultureAwareComparer.InvariantCaseSensitiveInstance;

        public static StringComparer InvariantCultureIgnoreCase => CultureAwareComparer.InvariantIgnoreCaseInstance;

        public static StringComparer CurrentCulture =>
            new CultureAwareComparer(System.Globalization.CultureInfo.CurrentCulture, System.Globalization.CompareOptions.None);

        public static StringComparer CurrentCultureIgnoreCase =>
            new CultureAwareComparer(System.Globalization.CultureInfo.CurrentCulture, System.Globalization.CompareOptions.IgnoreCase);

        public static StringComparer Ordinal => OrdinalCaseSensitiveComparer.Instance;

        public static StringComparer OrdinalIgnoreCase => OrdinalIgnoreCaseComparer.Instance;

        // Convert a StringComparison to a StringComparer
        public static StringComparer FromComparison(StringComparison comparisonType)
        {
            return comparisonType switch
            {
                StringComparison.CurrentCulture => CurrentCulture,
                StringComparison.CurrentCultureIgnoreCase => CurrentCultureIgnoreCase,
                StringComparison.InvariantCulture => InvariantCulture,
                StringComparison.InvariantCultureIgnoreCase => InvariantCultureIgnoreCase,
                StringComparison.Ordinal => Ordinal,
                StringComparison.OrdinalIgnoreCase => OrdinalIgnoreCase,
                _ => throw new ArgumentException(),
            };
        }

        private protected virtual bool IsWellKnownOrdinalComparerCore(out bool ignoreCase)
        {
            // unless specialized comparer overrides this, we're not a well-known ordinal comparer
            ignoreCase = default;
            return false;
        }
        private protected virtual bool IsWellKnownCultureAwareComparerCore(
            [NotNullWhen(true)] out System.Globalization.CompareInfo? compareInfo, out System.Globalization.CompareOptions compareOptions)
        {
            // unless specialized comparer overrides this, we're not a well-known culture-aware comparer
            compareInfo = default;
            compareOptions = default;
            return false;
        }

        public int GetHashCode(object obj)
        {
            if (obj is null) throw new ArgumentNullException();

            if (obj is string s)
            {
                return GetHashCode(s);
            }
            return obj.GetHashCode();
        }
        public int Compare(object? x, object? y)
        {
            if (x == y) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            if (x is string sa && y is string sb)
            {
                return Compare(sa, sb);
            }
            if (x is IComparable ia)
            {
                return ia.CompareTo(y);
            }
            throw new ArgumentException();
        }
        public abstract int Compare(string? x, string? y);
        public abstract bool Equals(string? x, string? y);
        public abstract int GetHashCode(string obj);
    }
    public class OrdinalComparer : StringComparer, IAlternateEqualityComparer<ReadOnlySpan<char>, string?>
    {
        private readonly bool _ignoreCase; // Do not rename

        internal OrdinalComparer(bool ignoreCase)
        {
            _ignoreCase = ignoreCase;
        }

        // Equals method for the comparer itself.
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (obj is not OrdinalComparer comparer)
            {
                return false;
            }
            return this._ignoreCase == comparer._ignoreCase;
        }

        public override int GetHashCode()
        {
            int hashCode = nameof(OrdinalComparer).GetHashCode();
            return _ignoreCase ? (~hashCode) : hashCode;
        }

        public override int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x == null)
            {
                return -1;
            }

            if (y == null)
            {
                return 1;
            }

            if (_ignoreCase)
            {
                return Globalization.Ordinal.CompareStringIgnoreCase(ref x.GetRawStringData(), x.Length, ref y.GetRawStringData(), y.Length);
            }

            return string.CompareOrdinal(x, y);
        }

        public override bool Equals(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            if (_ignoreCase)
            {
                return x.Length == y.Length && Globalization.Ordinal.EqualsIgnoreCase(ref x.GetRawStringData(), ref y.GetRawStringData(), x.Length);
            }

            return x.Equals(y);
        }

        public override int GetHashCode(string obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException();
            }

            if (_ignoreCase)
            {
                return obj.GetHashCodeOrdinalIgnoreCase();
            }

            return obj.GetHashCode();
        }

        string IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.Create(ReadOnlySpan<char> span) =>
            span.ToString();

        int IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.GetHashCode(ReadOnlySpan<char> span) =>
            _ignoreCase ? string.GetHashCodeOrdinalIgnoreCase(span) : string.GetHashCode(span);

        bool IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.Equals(ReadOnlySpan<char> span, string? target)
        {
            if (span.IsEmpty && target is null)
            {
                return false;
            }

            return _ignoreCase ? span.EqualsOrdinalIgnoreCase(target) : span.SequenceEqual(target);
        }
    }
    internal sealed class OrdinalCaseSensitiveComparer : OrdinalComparer, IAlternateEqualityComparer<ReadOnlySpan<char>, string?>
    {
        internal static readonly OrdinalCaseSensitiveComparer Instance = new OrdinalCaseSensitiveComparer();

        private OrdinalCaseSensitiveComparer() : base(false)
        {
        }

        public override int Compare(string? x, string? y) => string.CompareOrdinal(x, y);

        public override bool Equals(string? x, string? y) => string.Equals(x, y);

        public override int GetHashCode(string obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }
            return obj.GetHashCode();
        }
    }
    internal sealed class OrdinalIgnoreCaseComparer : OrdinalComparer, IAlternateEqualityComparer<ReadOnlySpan<char>, string?>
    {
        internal static readonly OrdinalIgnoreCaseComparer Instance = new OrdinalIgnoreCaseComparer();

        private OrdinalIgnoreCaseComparer() : base(true)
        {
        }

        public override int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x == null)
            {
                return -1;
            }

            if (y == null)
            {
                return 1;
            }

            return Globalization.Ordinal.CompareStringIgnoreCase(ref x.GetRawStringData(), x.Length, ref y.GetRawStringData(), y.Length);
        }

        public override bool Equals(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            if (x.Length != y.Length)
            {
                return false;
            }

            return Globalization.Ordinal.EqualsIgnoreCase(ref x.GetRawStringData(), ref y.GetRawStringData(), x.Length);
        }

        public override int GetHashCode(string obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException();
            }
            return obj.GetHashCodeOrdinalIgnoreCase();
        }
    }
    public sealed class CultureAwareComparer : StringComparer
    {
        internal static readonly CultureAwareComparer InvariantCaseSensitiveInstance =
            new CultureAwareComparer(System.Globalization.CompareInfo.Invariant, System.Globalization.CompareOptions.None);
        internal static readonly CultureAwareComparer InvariantIgnoreCaseInstance =
            new CultureAwareComparer(System.Globalization.CompareInfo.Invariant, System.Globalization.CompareOptions.IgnoreCase);

        private const System.Globalization.CompareOptions ValidCompareMaskOffFlags =
            ~(System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreSymbols |
            System.Globalization.CompareOptions.IgnoreNonSpace | System.Globalization.CompareOptions.IgnoreKanaType |
              System.Globalization.CompareOptions.IgnoreWidth | System.Globalization.CompareOptions.NumericOrdering | System.Globalization.CompareOptions.StringSort);

        private readonly System.Globalization.CompareInfo _compareInfo; // Do not rename
        private readonly System.Globalization.CompareOptions _options;

        internal CultureAwareComparer(System.Globalization.CultureInfo culture, System.Globalization.CompareOptions options) : this(culture.CompareInfo, options) { }

        internal CultureAwareComparer(System.Globalization.CompareInfo compareInfo, System.Globalization.CompareOptions options)
        {
            _compareInfo = compareInfo;

            if ((options & ValidCompareMaskOffFlags) != 0)
            {
                throw new ArgumentException();
            }
            _options = options;
        }

        public override int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;
            return _compareInfo.Compare(x, y, _options);
        }

        public override bool Equals(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null) return false;
            return _compareInfo.Compare(x, y, _options) == 0;
        }

        public override int GetHashCode(string obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }
            return (_options & System.Globalization.CompareOptions.IgnoreCase) != 0 ? obj.GetHashCodeOrdinalIgnoreCase() : obj.GetHashCode();
        }
    }
    internal interface IUtfChar<TSelf> :
        IBinaryInteger<TSelf>
        where TSelf : unmanaged, IUtfChar<TSelf>
    {
        /// <summary>Casts the specified value to this type.</summary>
        public static abstract TSelf CastFrom(byte value);

        /// <summary>Casts the specified value to this type.</summary>
        public static abstract TSelf CastFrom(char value);

        /// <summary>Casts the specified value to this type.</summary>
        public static abstract TSelf CastFrom(int value);

        /// <summary>Casts the specified value to this type.</summary>
        public static abstract TSelf CastFrom(uint value);

        /// <summary>Casts the specified value to this type.</summary>
        public static abstract TSelf CastFrom(ulong value);
    }
    public interface ITuple
    {
        int Length { get; }

        object this[int index] { get; }
    }
    public interface IDisposable
    {
        void Dispose();
    }
    public interface IComparable
    {
        int CompareTo(object? obj);
    }
    public interface IComparable<in T> where T : allows ref struct
    {
        int CompareTo(T? other);
    }
    public interface IConvertible
    {
        TypeCode GetTypeCode();

        bool ToBoolean(IFormatProvider? provider);
        char ToChar(IFormatProvider? provider);
        sbyte ToSByte(IFormatProvider? provider);
        byte ToByte(IFormatProvider? provider);
        short ToInt16(IFormatProvider? provider);
        ushort ToUInt16(IFormatProvider? provider);
        int ToInt32(IFormatProvider? provider);
        uint ToUInt32(IFormatProvider? provider);
        long ToInt64(IFormatProvider? provider);
        ulong ToUInt64(IFormatProvider? provider);
        float ToSingle(IFormatProvider? provider);
        double ToDouble(IFormatProvider? provider);
        decimal ToDecimal(IFormatProvider? provider);
        DateTime ToDateTime(IFormatProvider? provider);
        string ToString(IFormatProvider? provider);
        object ToType(Type conversionType, IFormatProvider? provider);
    }
    public interface IFormattable
    {
        string ToString(string? format, IFormatProvider? formatProvider);
    }
    /// <summary>Defines a mechanism for parsing a string to a value.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    public interface IParsable<TSelf>
        where TSelf : IParsable<TSelf>?
    {
        /// <summary>Parses a string into a value.</summary>
        /// <param name="s">The string to parse.</param>
        /// <param name="provider">An object that provides culture-specific formatting information about <paramref name="s" />.</param>
        /// <returns>The result of parsing <paramref name="s" />.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="s" /> is <c>null</c>.</exception>
        /// <exception cref="FormatException"><paramref name="s" /> is not in the correct format.</exception>
        /// <exception cref="OverflowException"><paramref name="s" /> is not representable by <typeparamref name="TSelf" />.</exception>
        static abstract TSelf Parse(string s, IFormatProvider? provider);

        /// <summary>Tries to parse a string into a value.</summary>
        /// <param name="s">The string to parse.</param>
        /// <param name="provider">An object that provides culture-specific formatting information about <paramref name="s" />.</param>
        /// <param name="result">On return, contains the result of successfully parsing <paramref name="s" /> or an undefined value on failure.</param>
        /// <returns><c>true</c> if <paramref name="s" /> was successfully parsed; otherwise, <c>false</c>.</returns>
        static abstract bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(returnValue: false)] out TSelf result);
    }
    /// <summary>Defines a mechanism for parsing a span of characters to a value.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    public interface ISpanParsable<TSelf> : IParsable<TSelf>
        where TSelf : ISpanParsable<TSelf>?
    {
        /// <summary>Parses a span of characters into a value.</summary>
        /// <param name="s">The span of characters to parse.</param>
        /// <param name="provider">An object that provides culture-specific formatting information about <paramref name="s" />.</param>
        /// <returns>The result of parsing <paramref name="s" />.</returns>
        /// <exception cref="FormatException"><paramref name="s" /> is not in the correct format.</exception>
        /// <exception cref="OverflowException"><paramref name="s" /> is not representable by <typeparamref name="TSelf" />.</exception>
        static abstract TSelf Parse(ReadOnlySpan<char> s, IFormatProvider? provider);

        /// <summary>Tries to parse a span of characters into a value.</summary>
        /// <param name="s">The span of characters to parse.</param>
        /// <param name="provider">An object that provides culture-specific formatting information about <paramref name="s" />.</param>
        /// <param name="result">On return, contains the result of successfully parsing <paramref name="s" /> or an undefined value on failure.</param>
        /// <returns><c>true</c> if <paramref name="s" /> was successfully parsed; otherwise, <c>false</c>.</returns>
        static abstract bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(returnValue: false)] out TSelf result);
    }
    /// <summary>Defines a mechanism for parsing a span of UTF-8 characters to a value.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    public interface IUtf8SpanParsable<TSelf>
        where TSelf : IUtf8SpanParsable<TSelf>?
    {
        /// <summary>Parses a span of UTF-8 characters into a value.</summary>
        /// <param name="utf8Text">The span of UTF-8 characters to parse.</param>
        /// <param name="provider">An object that provides culture-specific formatting information about <paramref name="utf8Text" />.</param>
        /// <returns>The result of parsing <paramref name="utf8Text" />.</returns>
        /// <exception cref="FormatException"><paramref name="utf8Text" /> is not in the correct format.</exception>
        /// <exception cref="OverflowException"><paramref name="utf8Text" /> is not representable by <typeparamref name="TSelf" />.</exception>
        static abstract TSelf Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider);

        /// <summary>Tries to parse a span of UTF-8 characters into a value.</summary>
        /// <param name="utf8Text">The span of UTF-8 characters to parse.</param>
        /// <param name="provider">An object that provides culture-specific formatting information about <paramref name="utf8Text" />.</param>
        /// <param name="result">On return, contains the result of successfully parsing <paramref name="utf8Text" /> or an undefined value on failure.</param>
        /// <returns><c>true</c> if <paramref name="utf8Text" /> was successfully parsed; otherwise, <c>false</c>.</returns>
        static abstract bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, [MaybeNullWhen(returnValue: false)] out TSelf result);
    }
    /// <summary>Provides functionality to format the string representation of an object into a span.</summary>
    public interface ISpanFormattable : IFormattable
    {
        /// <summary>Tries to format the value of the current instance into the provided span of characters.</summary>
        /// <param name="destination">When this method returns, this instance's value formatted as a span of characters.</param>
        /// <param name="charsWritten">When this method returns, the number of characters that were written in <paramref name="destination"/>.</param>
        /// <param name="format">A span containing the characters that represent a standard or custom format string that defines the acceptable format for <paramref name="destination"/>.</param>
        /// <param name="provider">An optional object that supplies culture-specific formatting information for <paramref name="destination"/>.</param>
        /// <returns><see langword="true"/> if the formatting was successful; otherwise, <see langword="false"/>.</returns>
        /// <remarks>
        /// An implementation of this interface should produce the same string of characters as an implementation of <see cref="IFormattable.ToString(string?, IFormatProvider?)"/>
        /// on the same type.
        /// TryFormat should return false only if there is not enough space in the destination buffer. Any other failures should throw an exception.
        /// </remarks>
        bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider);
    }
    /// <summary>Provides functionality to format the string representation of an object into a span as UTF-8.</summary>
    public interface IUtf8SpanFormattable
    {
        /// <summary>Tries to format the value of the current instance as UTF-8 into the provided span of bytes.</summary>
        /// <param name="utf8Destination">When this method returns, this instance's value formatted as a span of bytes.</param>
        /// <param name="bytesWritten">When this method returns, the number of bytes that were written in <paramref name="utf8Destination"/>.</param>
        /// <param name="format">A span containing the characters that represent a standard or custom format string that defines the acceptable format for <paramref name="utf8Destination"/>.</param>
        /// <param name="provider">An optional object that supplies culture-specific formatting information for <paramref name="utf8Destination"/>.</param>
        /// <returns><see langword="true"/> if the formatting was successful; otherwise, <see langword="false"/>.</returns>
        /// <remarks>
        /// An implementation of this interface should produce the same string of characters as an implementation of <see cref="IFormattable.ToString"/> or <see cref="ISpanFormattable.TryFormat"/>
        /// on the same type. TryFormat should return false only if there is not enough space in the destination buffer; any other failures should throw an exception.
        /// </remarks>
        bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider);
    }
    /// <summary>
    /// Provides a mechanism for retrieving an object to control formatting.
    /// </summary>
    public interface IFormatProvider
    {
        object? GetFormat(Type? formatType);
    }
    /// <summary>
    /// Defines a method that supports custom formatting of the value of an object.
    /// </summary>
    public interface ICustomFormatter
    {
        string Format(string? format, object? arg, IFormatProvider? formatProvider);
    }
    public interface IEquatable<T> where T : allows ref struct // invariant due to questionable semantics around equality and inheritance
    {
        /// <summary>Indicates whether the current object is equal to another object of the same type.</summary>
        bool Equals(T? other);
    }
    // math
    public enum MidpointRounding
    {
        ToEven = 0,
        AwayFromZero = 1,
        ToZero = 2,
        ToNegativeInfinity = 3,
        ToPositiveInfinity = 4
    }
    public static class BitConverter
    {
        public static readonly bool IsLittleEndian = true;

        public static byte[] GetBytes(bool value) => new byte[] { (value ? (byte)1 : (byte)0) };

        public static byte[] GetBytes(char value)
        {
            byte[] bytes = new byte[sizeof(char)];
            bool success = TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, char value)
        {
            if (destination.Length < sizeof(char))
                return false;

            Unsafe.WriteUnaligned(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static byte[] GetBytes(short value)
        {
            byte[] bytes = new byte[sizeof(short)];
            bool success = TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, short value)
        {
            if (destination.Length < sizeof(short))
                return false;

            Unsafe.WriteUnaligned(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static byte[] GetBytes(int value)
        {
            byte[] bytes = new byte[sizeof(int)];
            bool success = TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, int value)
        {
            if (destination.Length < sizeof(int))
                return false;

            Unsafe.WriteUnaligned(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static byte[] GetBytes(long value)
        {
            byte[] bytes = new byte[sizeof(long)];
            bool success = TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, long value)
        {
            if (destination.Length < sizeof(long))
                return false;

            Unsafe.WriteUnaligned(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static byte[] GetBytes(ushort value)
        {
            byte[] bytes = new byte[sizeof(ushort)];
            bool success = TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, ushort value)
        {
            if (destination.Length < sizeof(ushort))
                return false;

            Unsafe.WriteUnaligned(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static byte[] GetBytes(uint value)
        {
            byte[] bytes = new byte[sizeof(uint)];
            bool success = TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, uint value)
        {
            if (destination.Length < sizeof(uint))
                return false;

            Unsafe.WriteUnaligned(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static byte[] GetBytes(ulong value)
        {
            byte[] bytes = new byte[sizeof(ulong)];
            bool success = TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, ulong value)
        {
            if (destination.Length < sizeof(ulong))
                return false;

            Unsafe.WriteUnaligned(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static byte[] GetBytes(float value)
        {
            byte[] bytes = new byte[sizeof(float)];
            bool success = TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, float value)
        {
            if (destination.Length < sizeof(float))
                return false;

            Unsafe.WriteUnaligned(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static byte[] GetBytes(double value)
        {
            byte[] bytes = new byte[sizeof(double)];
            bool success = TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, double value)
        {
            if (destination.Length < sizeof(double))
                return false;

            Unsafe.WriteUnaligned(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static char ToChar(byte[] value, int startIndex) => unchecked((char)ToInt16(value, startIndex));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static char ToChar(ReadOnlySpan<byte> value)
        {
            if (value.Length < sizeof(char))
                throw new ArgumentOutOfRangeException();
            return Unsafe.ReadUnaligned<char>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(value));
        }

        public static short ToInt16(byte[] value, int startIndex)
        {
            if (value == null)
                throw new ArgumentNullException();
            if (unchecked((uint)startIndex) >= unchecked((uint)value.Length))
                throw new ArgumentOutOfRangeException();
            if (startIndex > value.Length - sizeof(short))
                throw new ArgumentException();

            return Unsafe.ReadUnaligned<short>(ref value[startIndex]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short ToInt16(ReadOnlySpan<byte> value)
        {
            if (value.Length < sizeof(short))
                throw new ArgumentOutOfRangeException();
            return Unsafe.ReadUnaligned<short>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(value));
        }

        public static int ToInt32(byte[] value, int startIndex)
        {
            if (value == null)
                throw new ArgumentNullException();
            if (unchecked((uint)startIndex) >= unchecked((uint)value.Length))
                throw new ArgumentOutOfRangeException();
            if (startIndex > value.Length - sizeof(int))
                throw new ArgumentException();

            return Unsafe.ReadUnaligned<int>(ref value[startIndex]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToInt32(ReadOnlySpan<byte> value)
        {
            if (value.Length < sizeof(int))
                throw new ArgumentOutOfRangeException();
            return Unsafe.ReadUnaligned<int>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(value));
        }

        public static long ToInt64(byte[] value, int startIndex)
        {
            if (value == null)
                throw new ArgumentNullException();
            if (unchecked((uint)startIndex) >= unchecked((uint)value.Length))
                throw new ArgumentOutOfRangeException();
            if (startIndex > value.Length - sizeof(long))
                throw new ArgumentException();

            return Unsafe.ReadUnaligned<long>(ref value[startIndex]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long ToInt64(ReadOnlySpan<byte> value)
        {
            if (value.Length < sizeof(long))
                throw new ArgumentOutOfRangeException();
            return Unsafe.ReadUnaligned<long>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(value));
        }

        public static long DoubleToInt64Bits(double value) => Unsafe.BitCast<double, long>(value);
        public static double Int64BitsToDouble(long value) => Unsafe.BitCast<long, double>(value);


        public static int SingleToInt32Bits(float value) => Unsafe.BitCast<float, int>(value);
        public static float Int32BitsToSingle(int value) => Unsafe.BitCast<int, float>(value);

        public static ulong DoubleToUInt64Bits(double value) => Unsafe.BitCast<double, ulong>(value);
        public static double UInt64BitsToDouble(ulong value) => Unsafe.BitCast<ulong, double>(value);

        public static uint SingleToUInt32Bits(float value) => Unsafe.BitCast<float, uint>(value);
        public static float UInt32BitsToSingle(uint value) => Unsafe.BitCast<uint, float>(value);
    }
    public static class Activator
    {
        [Intrinsic]
        public static T CreateInstance<T>() => throw new PlatformNotSupportedException();
    }
    public static class MathF
    {
        public const float E = 2.71828183f;

        public const float PI = 3.14159265f;

        public const float Tau = 6.283185307f;

        private const int maxRoundingDigits = 6;

        private const float singleRoundLimit = 1e8f;

        private const float SCALEB_C1 = 1.7014118E+38f; // 0x1p127f

        private const float SCALEB_C2 = 1.1754944E-38f; // 0x1p-126f

        private const float SCALEB_C3 = 16777216f; // 0x1p24f

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Abs(float x)
        {
            return Math.Abs(x);
        }

        public static float Sqrt(float x)
        {
            const uint SignMask = 0x8000_0000u;
            const uint ExponentMask = 0x7F80_0000u;
            const uint SignificandMask = 0x007F_FFFFu;
            const uint HiddenBit = 0x0080_0000u;
            const uint PositiveInfinityBits = 0x7F80_0000u;

            uint ix = BitConverter.SingleToUInt32Bits(x);

            // NaN / infinities
            if ((ix & ExponentMask) == ExponentMask)
            {
                // NaN
                if ((ix & SignificandMask) != 0)
                    return x + x;

                // +Infinity to +Infinity, -Infinity to NaN
                return (ix & SignMask) == 0 ? x : float.NaN;
            }

            // Negative values
            if ((ix & SignMask) != 0)
            {
                // sqrt(-0.0f) == -0.0f
                if ((ix & ~SignMask) == 0)
                    return x;

                return float.NaN;
            }

            // +0.0f
            if (ix == 0)
                return x;

            int m = (int)(ix >> 23);

            // Normalize subnormal input
            if (m == 0)
            {
                int i = 0;

                while ((ix & HiddenBit) == 0)
                {
                    i++;
                    ix <<= 1;
                }

                m -= i - 1;
            }

            m -= 127;
            ix = (ix & SignificandMask) | HiddenBit;

            if ((m & 1) != 0)
                ix += ix;

            m >>= 1;

            ix += ix;

            uint q = 0;
            uint s = 0;
            uint r = 0x0100_0000u;

            while (r != 0)
            {
                uint t = s + r;

                if (t <= ix)
                {
                    s = t + r;
                    ix -= t;
                    q += r;
                }

                ix += ix;
                r >>= 1;
            }

            // Round to nearest even
            if (ix != 0)
                q += q & 1u;

            ix = (q >> 1) + 0x3F00_0000u;
            ix += unchecked((uint)(m << 23));

            return BitConverter.UInt32BitsToSingle(ix);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe (float Sin, float Cos) SinCos(float x)
        {
            float sin, cos;
            SinCos(x, &sin, &cos);
            return (sin, cos);
        }
        public static unsafe float Sin(float x)
        {
            float sin, cos;
            SinCos(x, &sin, &cos);
            return sin;
        }

        public static unsafe float Cos(float x)
        {
            float sin, cos;
            SinCos(x, &sin, &cos);
            return cos;
        }

        private static unsafe void SinCos(float x, float* sin, float* cos)
        {
            const uint ExponentMask = 0x7F80_0000u;

            uint ux = BitConverter.SingleToUInt32Bits(x);
            // NaN and infinities
            if ((ux & ExponentMask) == ExponentMask)
            {
                *sin = float.NaN;
                *cos = float.NaN;
                return;
            }
            // zero
            if ((ux & 0x7FFF_FFFFu) == 0)
            {
                *sin = x;
                *cos = 1.0f;
                return;
            }

            const double PiOver2 = 1.57079632679489661923132169163975144;
            const double TwoPi = 6.28318530717958647692528676655900576;
            const double TwoOverPi = 0.636619772367581343075535053490057448;

            double y = (double)x % TwoPi;
            int q = (int)(y * TwoOverPi + (y >= 0.0 ? 0.5 : -0.5));
            double r = y - q * PiOver2;
            double z = r * r;
            double sr = r + r * z *
            (
                -1.66666666666666657415e-1 + z *
                (
                    8.33333333333333321769e-3 + z *
                    (
                        -1.98412698412698412535e-4 + z *
                        (
                            2.75573192239858906526e-6 + z * -2.50521083854417187750e-8
                        )
                    )
                )
            );

            double cr = 1.0 + z *
            (
                -5.00000000000000000000e-1 + z *
                (
                    4.16666666666666643537e-2 + z *
                    (
                        -1.38888888888888894189e-3 + z *
                        (
                            2.48015873015873015688e-5 + z * -2.75573192239858925110e-7
                        )
                    )
                )
            );

            switch (q & 3)
            {
                case 0:
                    *sin = (float)sr;
                    *cos = (float)cr;
                    break;

                case 1:
                    *sin = (float)cr;
                    *cos = (float)-sr;
                    break;

                case 2:
                    *sin = (float)-sr;
                    *cos = (float)-cr;
                    break;

                default:
                    *sin = (float)-cr;
                    *cos = (float)sr;
                    break;
            }
        }
    

        public static float Truncate(float x) => Math.Truncate(x);

        public static float Floor(float x)
        {
            float t = Math.Truncate(x);
            return t > x ? t - 1.0f : t;
        }

        public static float Ceiling(float x)
        {
            float t = Math.Truncate(x);
            return t < x ? t + 1.0f : t;
        }

        public static float CopySign(float x, float y)
            => BitConverter.UInt32BitsToSingle((BitConverter.SingleToUInt32Bits(x) & 0x7FFF_FFFFu) | (BitConverter.SingleToUInt32Bits(y) & 0x8000_0000u));

        public static float Round(float x)
        {
            const float IntegerBoundary = 8388608.0f; // 2^23
            if (Abs(x) >= IntegerBoundary)
                return x;
            float temp = CopySign(IntegerBoundary, x);
            return CopySign((x + temp) - temp, x);
        }

        private static ReadOnlySpan<float> RoundPower10Single => [1e0f, 1e1f, 1e2f, 1e3f, 1e4f, 1e5f, 1e6f];

        public static float Round(float x, int digits, MidpointRounding mode)
        {
            if ((uint)digits > maxRoundingDigits)
                throw new ArgumentOutOfRangeException(nameof(digits), SR.ArgumentOutOfRange_RoundingDigits_MathF);
            if (mode < MidpointRounding.ToEven || mode > MidpointRounding.ToPositiveInfinity)
                throw new ArgumentOutOfRangeException(nameof(mode));
            if (Abs(x) < singleRoundLimit)
            {
                float power10 = RoundPower10Single[digits];
                x *= power10;
                x = mode switch
                {
                    MidpointRounding.ToEven => Round(x),
                    MidpointRounding.AwayFromZero => Truncate(x + CopySign(0.49999997f, x)),
                    MidpointRounding.ToZero => Truncate(x),
                    MidpointRounding.ToNegativeInfinity => Floor(x),
                    _ => Ceiling(x),
                };
                x /= power10;
            }
            return x;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Round(float x, int digits)
        {
            return Round(x, digits, MidpointRounding.ToEven);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Round(float x, MidpointRounding mode)
        {
            switch (mode)
            {
                // Rounds to the nearest value; if the number falls midway,
                // it is rounded to the nearest value above (for positive numbers) or below (for negative numbers)
                case MidpointRounding.AwayFromZero:
                    // For ARM/ARM64 we can lower it down to a single instruction FRINTA
                    if (AdvSimd.IsSupported)
                        return AdvSimd.RoundAwayFromZeroScalar(Vector64.CreateScalarUnsafe(x)).ToScalar();
                    // For other platforms we use a fast managed implementation
                    // manually fold BitDecrement(0.5)
                    return Truncate(x + CopySign(0.49999997f, x));

                // Rounds to the nearest value; if the number falls midway,
                // it is rounded to the nearest value with an even least significant digit
                case MidpointRounding.ToEven:
                    return Round(x);
                // Directed rounding: Round to the nearest value, toward to zero
                case MidpointRounding.ToZero:
                    return Truncate(x);
                // Directed Rounding: Round down to the next value, toward negative infinity
                case MidpointRounding.ToNegativeInfinity:
                    return Floor(x);
                // Directed rounding: Round up to the next value, toward positive infinity
                case MidpointRounding.ToPositiveInfinity:
                    return Ceiling(x);

                default:
                    ThrowHelper.ThrowArgumentException_InvalidEnumValue(mode);
                    return default;
            }
        }

        public static float FusedMultiplyAdd(float x, float y, float z)
        {
            if (!float.IsFinite(x) || !float.IsFinite(y))
                return x * y + z;
            if (!float.IsFinite(z))
                return z;
            if (x == 0 || y == 0)
                return x * y + z;
            if (z == 0)
                return (float)((double)x * y);
            return (float)Math.FusedMultiplyAddCore(x, y, z, mantissaBits: 23, minExponent: -126, maxExponent: 127);
        }

        public static float Exp(float x) => (float)Math.Exp(x);

        public static float Tan(float x) => (float)Math.Tan(x);

        public static float Asin(float x) => (float)Math.Asin(x);

        public static float Acos(float x) => (float)Math.Acos(x);

        public static float Atan(float x) => (float)Math.Atan(x);

        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);

        public static float Sinh(float x) => (float)Math.Sinh(x);

        public static float Cosh(float x) => (float)Math.Cosh(x);

        public static float Tanh(float x) => (float)Math.Tanh(x);

        public static float Asinh(float x) => (float)Math.Asinh(x);

        public static float Acosh(float x) => (float)Math.Acosh(x);

        public static float Atanh(float x) => (float)Math.Atanh(x);

        public static float Log(float x) => (float)Math.Log(x);

        public static float Log(float x, float y) => (float)Math.Log(x, y);

        public static float Log2(float x) => (float)Math.Log2(x);

        public static float Log10(float x) => (float)Math.Log10(x);

        public static float Pow(float x, float y) => (float)Math.Pow(x, y);

        public static float Cbrt(float x) => (float)Math.Cbrt(x);

        public static int ILogB(float x) => Math.ILogB(x);

        public static float ScaleB(float x, int n) => (float)Math.ScaleB(x, n);

        public static float IEEERemainder(float x, float y) => (float)Math.IEEERemainder(x, y);

        public static float BitIncrement(float x)
        {
            uint bits = BitConverter.SingleToUInt32Bits(x);
            if ((bits & 0x7F80_0000) >= 0x7F80_0000)
                return bits == 0xFF80_0000 ? float.MinValue : x;
            if (bits == 0x8000_0000)
                return float.Epsilon;
            return BitConverter.UInt32BitsToSingle((int)bits < 0 ? bits - 1 : bits + 1);
        }

        public static float BitDecrement(float x)
        {
            uint bits = BitConverter.SingleToUInt32Bits(x);
            if ((bits & 0x7F80_0000) >= 0x7F80_0000)
                return bits == 0x7F80_0000 ? float.MaxValue : x;
            if (bits == 0x0000_0000)
                return -float.Epsilon;
            return BitConverter.UInt32BitsToSingle((int)bits < 0 ? bits + 1 : bits - 1);
        }
    }
    // Conversions between the primitive numeric types behind INumberBase's TryConvert members.
    internal static class NumericConversion
    {
        internal enum Mode
        {
            Checked,
            Saturating,
            Truncating,
        }

        // A primitive integer as its sign and magnitude, which covers long.MinValue through ulong.MaxValue.
        internal static bool TryGetInteger<T>(T value, out bool negative, out ulong magnitude)
        {
            long signed;
            if (typeof(T) == typeof(sbyte))
                signed = (sbyte)(object)value!;
            else if (typeof(T) == typeof(short))
                signed = (short)(object)value!;
            else if (typeof(T) == typeof(int))
                signed = (int)(object)value!;
            else if (typeof(T) == typeof(long))
                signed = (long)(object)value!;
            else if (typeof(T) == typeof(nint))
                signed = (nint)(object)value!;
            else
            {
                negative = false;
                if (typeof(T) == typeof(byte))
                    magnitude = (byte)(object)value!;
                else if (typeof(T) == typeof(ushort))
                    magnitude = (ushort)(object)value!;
                else if (typeof(T) == typeof(char))
                    magnitude = (char)(object)value!;
                else if (typeof(T) == typeof(uint))
                    magnitude = (uint)(object)value!;
                else if (typeof(T) == typeof(ulong))
                    magnitude = (ulong)(object)value!;
                else if (typeof(T) == typeof(nuint))
                    magnitude = (nuint)(object)value!;
                else
                {
                    magnitude = 0;
                    return false;
                }
                return true;
            }
            negative = signed < 0;
            magnitude = negative ? 0 - (ulong)signed : (ulong)signed;
            return true;
        }

        // A primitive binary float as a double, which holds every float exactly.
        internal static bool TryGetFloat<T>(T value, out double result)
        {
            if (typeof(T) == typeof(double))
            {
                result = (double)(object)value!;
                return true;
            }
            if (typeof(T) == typeof(float))
            {
                result = (float)(object)value!;
                return true;
            }
            result = 0;
            return false;
        }

        // Two's complement bits of the converted value; the caller truncates them to its width. Floats saturate unless checked.
        internal static bool TryConvertToInteger<T>(T value, Mode mode, long min, ulong max, out ulong bits)
        {
            if (TryGetInteger(value, out bool negative, out ulong magnitude))
            {
                ulong minMagnitude = min < 0 ? (ulong)(-(min + 1)) + 1 : 0;
                bool below = negative && magnitude > minMagnitude;
                bool above = !negative && magnitude > max;
                if ((below || above) && mode != Mode.Truncating)
                {
                    if (mode == Mode.Checked)
                        ThrowHelper.ThrowOverflowException();
                    bits = below ? (ulong)min : max;
                    return true;
                }
                bits = negative ? 0 - magnitude : magnitude;
                return true;
            }
            if (TryGetFloat(value, out double d))
            {
                double t = Math.Truncate(d);
                if (t >= min && t < (double)max + 1.0)
                {
                    bits = t < 0 ? (ulong)(long)t : (ulong)t;
                    return true;
                }
                if (mode == Mode.Checked)
                    ThrowHelper.ThrowOverflowException();
                bits = double.IsNaN(t) ? 0 : t < min ? (ulong)min : max;
                return true;
            }
            bits = 0;
            return false;
        }

        internal static bool TryConvertToDouble<T>(T value, out double result)
        {
            if (TryGetInteger(value, out bool negative, out ulong magnitude))
            {
                result = negative ? -(double)magnitude : magnitude;
                return true;
            }
            return TryGetFloat(value, out result);
        }

        internal static bool TryConvertToSingle<T>(T value, out float result)
        {
            if (TryGetInteger(value, out bool negative, out ulong magnitude))
            {
                result = negative ? -(float)magnitude : magnitude;
                return true;
            }
            if (TryGetFloat(value, out double d))
            {
                result = (float)d;
                return true;
            }
            result = 0;
            return false;
        }

        // Reads a two's complement integer into `size` bytes, failing when it does not fit the target.
        internal static bool TryReadInteger(ReadOnlySpan<byte> source, bool bigEndian, bool isUnsigned, int size, bool targetSigned, out ulong bits)
        {
            bits = 0;
            int length = source.Length;
            if (length == 0)
                return true;
            byte top = bigEndian ? source[0] : source[length - 1];
            bool sourceNegative = !isUnsigned && (sbyte)top < 0;
            if (sourceNegative && !targetSigned)
                return false;
            byte fill = sourceNegative ? (byte)0xFF : (byte)0;
            for (int i = 0; i < length; i++)
            {
                byte b = bigEndian ? source[length - 1 - i] : source[i];
                if (i < size)
                    bits |= (ulong)b << (8 * i);
                else if (b != fill)
                    return false;
            }
            if (length < size)
            {
                if (sourceNegative)
                    bits |= ulong.MaxValue << (8 * length);
                return true;
            }
            bool keptNegative = ((bits >> (8 * size - 1)) & 1) != 0;
            return !targetSigned || keptNegative == sourceNegative;
        }

        internal static bool TryWriteInteger(ulong value, int size, bool bigEndian, Span<byte> destination, out int bytesWritten)
        {
            if (destination.Length < size)
            {
                bytesWritten = 0;
                return false;
            }
            for (int i = 0; i < size; i++)
                destination[bigEndian ? size - 1 - i : i] = (byte)(value >> (8 * i));
            bytesWritten = size;
            return true;
        }
    }
    public static class Math
    {
        public const double E = 2.7182818284590452354;
        public const double PI = 3.14159265358979323846;
        public const double Tau = 6.283185307179586476925;

        private const int maxRoundingDigits = 15;
        private const double doubleRoundLimit = 1e16d;

        private static ReadOnlySpan<double> RoundPower10Double => new double[]
        {
            1E0, 1E1, 1E2, 1E3, 1E4, 1E5, 1E6, 1E7, 1E8,
            1E9, 1E10, 1E11, 1E12, 1E13, 1E14, 1E15
        };

        private const double SCALEB_C1 = 8.98846567431158E+307; // 0x1p1023

        private const double SCALEB_C2 = 2.2250738585072014E-308; // 0x1p-1022

        private const double SCALEB_C3 = 9007199254740992; // 0x1p53

        private const double Ln2 = 0.693147180559945309417232121458176568;
        private const double Ln10 = 2.302585092994045684017991454684364208;
        private const double Log2E = 1.442695040888963407359924681001892137; // 1/ln2

        internal static void ThrowNegateTwosCompOverflow()
        {
            throw new OverflowException();
        }

        public static byte Min(byte val1, byte val2)
        {
            return (val1 <= val2) ? val1 : val2;
        }

        public static short Min(short val1, short val2)
        {
            return (val1 <= val2) ? val1 : val2;
        }

        public static int Min(int val1, int val2)
        {
            return (val1 <= val2) ? val1 : val2;
        }

        public static long Min(long val1, long val2)
        {
            return (val1 <= val2) ? val1 : val2;
        }

        public static nint Min(nint val1, nint val2)
        {
            return (val1 <= val2) ? val1 : val2;
        }

        public static sbyte Min(sbyte val1, sbyte val2)
        {
            return (val1 <= val2) ? val1 : val2;
        }

        public static ushort Min(ushort val1, ushort val2)
        {
            return (val1 <= val2) ? val1 : val2;
        }

        public static uint Min(uint val1, uint val2)
        {
            return (val1 <= val2) ? val1 : val2;
        }

        public static ulong Min(ulong val1, ulong val2)
        {
            return (val1 <= val2) ? val1 : val2;
        }

        public static nuint Min(nuint val1, nuint val2)
        {
            return (val1 <= val2) ? val1 : val2;
        }

        public static byte Max(byte val1, byte val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }
        public static short Max(short val1, short val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        public static int Max(int val1, int val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        public static long Max(long val1, long val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        public static nint Max(nint val1, nint val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        public static sbyte Max(sbyte val1, sbyte val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        public static ushort Max(ushort val1, ushort val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        public static uint Max(uint val1, uint val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        public static ulong Max(ulong val1, ulong val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        public static nuint Max(nuint val1, nuint val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Clamp(byte value, byte min, byte max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Clamp(double value, double min, double max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short Clamp(short value, short min, short max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Clamp(int value, int min, int max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Clamp(long value, long min, long max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nint Clamp(nint value, nint min, nint max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte Clamp(sbyte value, sbyte min, sbyte max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp(float value, float min, float max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort Clamp(ushort value, ushort min, ushort max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Clamp(uint value, uint min, uint max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Clamp(ulong value, ulong min, ulong max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint Clamp(nuint value, nuint min, nuint max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        internal static void ThrowMinMaxException<T>(T min, T max)
        {
            throw new ArgumentException("minimum malue must be lower then maximum value.");
        }

        public static double BitDecrement(double x)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(x);

            if (!double.IsFinite(x))
            {
                // NaN returns NaN
                // -Infinity returns -Infinity
                // +Infinity returns MaxValue
                return (bits == double.PositiveInfinityBits) ? double.MaxValue : x;
            }

            if (bits == double.PositiveZeroBits)
            {
                // +0.0 returns -double.Epsilon
                return -double.Epsilon;
            }

            // Negative values need to be incremented
            // Positive values need to be decremented

            if (double.IsNegative(x))
            {
                bits += 1;
            }
            else
            {
                bits -= 1;
            }
            return BitConverter.UInt64BitsToDouble(bits);
        }

        public static double BitIncrement(double x)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(x);

            if (!double.IsFinite(x))
            {
                // NaN returns NaN
                // -Infinity returns MinValue
                // +Infinity returns +Infinity
                return (bits == double.NegativeInfinityBits) ? double.MinValue : x;
            }

            if (bits == double.NegativeZeroBits)
            {
                // -0.0 returns Epsilon
                return double.Epsilon;
            }

            // Negative values need to be decremented
            // Positive values need to be incremented

            if (double.IsNegative(x))
            {
                bits -= 1;
            }
            else
            {
                bits += 1;
            }
            return BitConverter.UInt64BitsToDouble(bits);
        }

        public static double CopySign(double x, double y)
        {
            // This method is required to work for all inputs,
            // including NaN, so we operate on the raw bits.
            ulong xbits = BitConverter.DoubleToUInt64Bits(x);
            ulong ybits = BitConverter.DoubleToUInt64Bits(y);

            // Remove the sign from x, and remove everything but the sign from y
            // Then, simply OR them to get the correct sign
            return BitConverter.UInt64BitsToDouble((xbits & ~double.SignMask) | (ybits & double.SignMask));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (sbyte Quotient, sbyte Remainder) DivRem(sbyte left, sbyte right)
        {
            sbyte quotient = (sbyte)(left / right);
            return (quotient, (sbyte)(left - (quotient * right)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (byte Quotient, byte Remainder) DivRem(byte left, byte right)
        {
            byte quotient = (byte)(left / right);
            return (quotient, (byte)(left - (quotient * right)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (short Quotient, short Remainder) DivRem(short left, short right)
        {
            short quotient = (short)(left / right);
            return (quotient, (short)(left - (quotient * right)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (ushort Quotient, ushort Remainder) DivRem(ushort left, ushort right)
        {
            ushort quotient = (ushort)(left / right);
            return (quotient, (ushort)(left - (quotient * right)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (int Quotient, int Remainder) DivRem(int left, int right)
        {
            int quotient = left / right;
            return (quotient, left - (quotient * right));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (uint Quotient, uint Remainder) DivRem(uint left, uint right)
        {
            uint quotient = left / right;
            return (quotient, left - (quotient * right));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (long Quotient, long Remainder) DivRem(long left, long right)
        {
            long quotient = left / right;
            return (quotient, left - (quotient * right));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (ulong Quotient, ulong Remainder) DivRem(ulong left, ulong right)
        {
            ulong quotient = left / right;
            return (quotient, left - (quotient * right));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong BigMul(uint a, uint b)
        {
            return ((ulong)a) * b;
        }

        public static long BigMul(int a, int b)
        {
            return ((long)a) * b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong BigMul(ulong a, uint b, out ulong low)
        {
#if TARGET_64BIT
            return Math.BigMul((ulong)a, (ulong)b, out low);
#else
            ulong prodL = ((ulong)(uint)a) * b;
            ulong prodH = (prodL >> 32) + (((ulong)(uint)(a >> 32)) * b);

            low = ((prodH << 32) | (uint)prodL);
            return (prodH >> 32);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong BigMul(uint a, ulong b, out ulong low)
            => BigMul(b, a, out low);

        public static unsafe ulong BigMul(ulong a, ulong b, out ulong low)
        {
            uint al = (uint)a;
            uint ah = (uint)(a >> 32);
            uint bl = (uint)b;
            uint bh = (uint)(b >> 32);

            ulong mull = ((ulong)al) * bl;
            ulong t = ((ulong)ah) * bl + (mull >> 32);
            ulong tl = ((ulong)al) * bh + (uint)t;

            low = tl << 32 | (uint)mull;

            return ((ulong)ah) * bh + (t >> 32) + (tl >> 32);
        }

        public static long BigMul(long a, long b, out long low)
        {
            ulong high = BigMul((ulong)a, (ulong)b, out ulong ulow);
            low = (long)ulow;
            return (long)high - ((a >> 63) & b) - ((b >> 63) & a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UInt128 BigMul(ulong a, ulong b)
        {
            ulong high = BigMul(a, b, out ulong low);
            return new UInt128(high, low);
        }

        public static Int128 BigMul(long a, long b)
        {
            long high = BigMul(a, b, out long low);
            return new Int128((ulong)high, (ulong)low);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short Abs(short value)
        {
            if (value < 0)
            {
                value = (short)-value;
                if (value < 0)
                {
                    ThrowNegateTwosCompOverflow();
                }
            }
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Abs(int value)
        {
            if (value < 0)
            {
                value = -value;
                if (value < 0)
                {
                    ThrowNegateTwosCompOverflow();
                }
            }
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Abs(long value)
        {
            if (value < 0)
            {
                value = -value;
                if (value < 0)
                {
                    ThrowNegateTwosCompOverflow();
                }
            }
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nint Abs(nint value)
        {
            if (value < 0)
            {
                value = -value;
                if (value < 0)
                {
                    ThrowNegateTwosCompOverflow();
                }
            }
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte Abs(sbyte value)
        {
            if (value < 0)
            {
                value = (sbyte)-value;
                if (value < 0)
                {
                    ThrowNegateTwosCompOverflow();
                }
            }
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Abs(double value)
        {
            const ulong mask = 0x7FFFFFFFFFFFFFFF;
            ulong raw = BitConverter.DoubleToUInt64Bits(value);

            return BitConverter.UInt64BitsToDouble(raw & mask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Abs(float value)
        {
            const uint mask = 0x7FFFFFFF;
            uint raw = BitConverter.SingleToUInt32Bits(value);

            return BitConverter.UInt32BitsToSingle(raw & mask);
        }

        public static double Truncate(double value)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(value);

            int biasedExp = (int)((bits >> 52) & 0x7FF);

            if (biasedExp == 0x7FF)
                return value;

            int exp = biasedExp - 1023;

            if (exp < 0)
                return BitConverter.UInt64BitsToDouble(bits & 0x8000_0000_0000_0000UL);

            if (exp >= 52)
                return value;

            int fracBits = 52 - exp;
            ulong mask = (1UL << fracBits) - 1UL;
            bits &= ~mask;

            return BitConverter.UInt64BitsToDouble(bits);
        }

        public static float Truncate(float value)
        {
            uint bits = BitConverter.SingleToUInt32Bits(value);

            int biasedExp = (int)((bits >> 23) & 0xFF);

            if (biasedExp == 0xFF)
                return value;

            int exp = biasedExp - 127;

            if (exp < 0)
                return BitConverter.UInt32BitsToSingle(bits & 0x8000_0000u);

            if (exp >= 23)
                return value;

            int fracBits = 23 - exp;
            uint mask = (1u << fracBits) - 1u;
            bits &= ~mask;

            return BitConverter.UInt32BitsToSingle(bits);
        }

        public static double Round(double a)
        {
            const double IntegerBoundary = 4503599627370496.0; // 2^52
            if (Abs(a) >= IntegerBoundary)
            {
                // Values above this boundary don't have a fractional
                // portion and so we can simply return them as-is.
                return a;
            }

            double temp = CopySign(IntegerBoundary, a);
            return CopySign((a + temp) - temp, a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe (double Sin, double Cos) SinCos(double x)
        {
            double sin, cos;
            SinCos(x, &sin, &cos);
            return (sin, cos);
        }
        public static unsafe double Sin(double x)
        {
            double sin, cos;
            SinCos(x, &sin, &cos);
            return sin;
        }

        public static unsafe double Cos(double x)
        {
            double sin, cos;
            SinCos(x, &sin, &cos);
            return cos;
        }

        private static unsafe void SinCos(double x, double* sin, double* cos)
        {
            const ulong ExponentMask = 0x7FF0_0000_0000_0000UL;
            const ulong AbsMask = 0x7FFF_FFFF_FFFF_FFFFUL;

            ulong ux = BitConverter.DoubleToUInt64Bits(x);
            // NaN and infinities
            if ((ux & ExponentMask) == ExponentMask)
            {
                *sin = double.NaN;
                *cos = double.NaN;
                return;
            }
            // zero
            if ((ux & AbsMask) == 0)
            {
                *sin = x;
                *cos = 1.0;
                return;
            }

            const double PiOver2 = 1.57079632679489661923132169163975144;
            const double TwoPi = 6.28318530717958647692528676655900576;
            const double TwoOverPi = 0.636619772367581343075535053490057448;

            double y = x % TwoPi;
            int q = (int)(y * TwoOverPi + (y >= 0.0 ? 0.5 : -0.5));
            double r = y - (double)q * PiOver2;
            double z = r * r;

            double sr = r + r * z *
            (
                -1.66666666666666666666e-1 + z *
                (
                    8.33333333333333333322e-3 + z *
                    (
                        -1.98412698412698412550e-4 + z *
                        (
                            2.75573192239858906526e-6 + z *
                            (
                                -2.50521083854417187750e-8 + z *
                                (
                                    1.60590438368216145994e-10 + z *
                                    (
                                        -7.64716373181981647590e-13 + z * 2.81145725434552076320e-15
                                    )
                                )
                            )
                        )
                    )
                )
            );

            double cr = 1.0 + z *
            (
                -5.00000000000000000000e-1 + z *
                (
                    4.16666666666666666667e-2 + z *
                    (
                        -1.38888888888888888889e-3 + z *
                        (
                            2.48015873015873015873e-5 + z *
                            (
                                -2.75573192239858906526e-7 + z *
                                (
                                    2.08767569878680989792e-9 + z *
                                    (
                                        -1.14707455977297247139e-11 + z * 4.77947733238738525335e-14
                                    )
                                )
                            )
                        )
                    )
                )
            );

            switch (q & 3)
            {
                case 0:
                    *sin = sr;
                    *cos = cr;
                    break;

                case 1:
                    *sin = cr;
                    *cos = -sr;
                    break;

                case 2:
                    *sin = -sr;
                    *cos = -cr;
                    break;

                default:
                    *sin = -cr;
                    *cos = sr;
                    break;
            }
        }

        public static double Sqrt(double d)
        {
            const uint Sign32 = 0x8000_0000u;

            ulong bits = BitConverter.DoubleToUInt64Bits(d);
            uint hx = (uint)(bits >> 32);
            uint lx = (uint)bits;

            // NaN / infinities
            if ((hx & 0x7FF0_0000u) == 0x7FF0_0000u)
            {
                // NaN
                if ((hx & 0x000F_FFFFu) != 0 || lx != 0)
                    return d + d;

                // +Infinity to +Infinity, -Infinity to NaN.
                return (hx & Sign32) == 0 ? d : double.NaN;
            }

            // Negative values
            if ((hx & Sign32) != 0)
            {
                // sqrt(-0.0) == -0.0
                if (((hx & 0x7FFF_FFFFu) | lx) == 0)
                    return d;

                return double.NaN;
            }

            // +0.0
            if (((hx & 0x7FFF_FFFFu) | lx) == 0)
                return d;

            int m = (int)(hx >> 20);
            // Normalize subnormal input
            if (m == 0)
            {
                while (hx == 0)
                {
                    m -= 21;
                    hx |= lx >> 11;
                    lx <<= 21;
                }

                int i = 0;
                while ((hx & 0x0010_0000u) == 0)
                {
                    i++;
                    hx <<= 1;
                }

                m -= i - 1;
                hx |= lx >> (32 - i);
                lx <<= i;
            }

            // Unbias exponent and restore the hidden significand bit
            m -= 1023;
            hx = (hx & 0x000F_FFFFu) | 0x0010_0000u;

            // Make exponent even
            if ((m & 1) != 0)
            {
                hx = hx + hx + (lx >> 31);
                lx += lx;
            }

            m >>= 1;

            hx = hx + hx + (lx >> 31);
            lx += lx;

            uint q = 0;
            uint q1 = 0;
            uint s0 = 0;
            uint s1 = 0;

            uint r = 0x0020_0000u;

            while (r != 0)
            {
                uint t = s0 + r;

                if (t <= hx)
                {
                    s0 = t + r;
                    hx -= t;
                    q += r;
                }

                hx = hx + hx + (lx >> 31);
                lx += lx;
                r >>= 1;
            }

            r = Sign32;
            while (r != 0)
            {
                uint t1 = s1 + r;
                uint t = s0;

                if ((t < hx) || ((t == hx) && (t1 <= lx)))
                {
                    s1 = t1 + r;

                    if (((t1 & Sign32) != 0) && ((s1 & Sign32) == 0))
                        s0++;

                    hx -= t;

                    if (lx < t1)
                        hx--;

                    lx -= t1;
                    q1 += r;
                }

                hx = hx + hx + (lx >> 31);
                lx += lx;
                r >>= 1;
            }

            // Round to nearest even
            if ((hx | lx) != 0)
            {
                const double One = 1.0;
                const double Tiny = 1.0e-300;

                double z = One - Tiny;

                if (z >= One)
                {
                    z = One + Tiny;

                    if (q1 == 0xFFFF_FFFFu)
                    {
                        q1 = 0;
                        q++;
                    }
                    else if (z > One)
                    {
                        if (q1 == 0xFFFF_FFFEu)
                            q++;

                        q1 += 2;
                    }
                    else
                    {
                        q1 += q1 & 1u;
                    }
                }
            }

            hx = (q >> 1) + 0x3FE0_0000u;
            lx = q1 >> 1;

            if ((q & 1) != 0)
                lx |= Sign32;

            hx += (uint)(m << 20);

            return BitConverter.UInt64BitsToDouble(((ulong)hx << 32) | lx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Pow(double x, double y)
        {
            if (y == 0.0)
                return 1.0;
            if (x == 1.0)
                return 1.0;
            if (Double.IsNaN(x) || Double.IsNaN(y))
                return Double.NaN;
            if (Double.IsInfinity(y))
            {
                double ax = Abs(x);

                if (ax == 1.0)
                    return 1.0;

                if (y > 0.0)
                    return (ax > 1.0) ? Double.PositiveInfinity : 0.0;
                else
                    return (ax > 1.0) ? 0.0 : Double.PositiveInfinity;
            }
            if (x == 0.0)
            {
                bool xNeg = Double.IsNegative(x);
                bool odd = Double.IsOddInteger(y);

                if (y > 0.0)
                {
                    return (odd && xNeg) ? -0.0 : 0.0;
                }
                else
                {
                    if (odd)
                        return xNeg ? Double.NegativeInfinity : Double.PositiveInfinity;
                    return Double.PositiveInfinity;
                }
            }

            if (Double.IsInfinity(x))
            {
                if (!Double.IsInteger(y))
                {
                    return (x > 0.0)
                        ? ((y > 0.0) ? Double.PositiveInfinity : 0.0)
                        : Double.NaN;
                }

                if (TryGetInt64FromIntegralDouble(y, out long yn))
                    return PowInteger(x, yn);

                return (y > 0.0) ? Double.PositiveInfinity : 0.0;
            }

            if (TryGetInt64FromIntegralDouble(y, out long n))
            {
                return PowInteger(x, n);
            }
            else if (Double.IsInteger(y))
            {
                double ax = (x < 0.0) ? -x : x;
                return Exp(y * Log(ax));
            }

            if (x < 0.0)
                return Double.NaN;

            // General case
            return Exp(y * Log(x));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double PowInteger(double x, long n)
        {
            if (n == 0)
                return 1.0;

            bool negExp = n < 0;

            ulong e = negExp ? (ulong)(-(n + 1)) + 1UL : (ulong)n;

            double result = 1.0;
            double baseVal = x;

            while (e != 0)
            {
                if ((e & 1UL) != 0)
                    result *= baseVal;

                baseVal *= baseVal;
                e >>= 1;
            }

            return negExp ? (1.0 / result) : result;
        }

        private static bool TryGetInt64FromIntegralDouble(double value, out long result)
        {
            result = 0;

            if (!Double.IsFinite(value))
                return false;

            if (value == 0.0)
                return true;

            ulong bits = BitConverter.DoubleToUInt64Bits(value);

            bool neg = (bits & 0x8000_0000_0000_0000UL) != 0;
            ulong absBits = bits & 0x7FFF_FFFF_FFFF_FFFFUL;

            int bexp = (int)((absBits >> 52) & 0x7FF);
            if (bexp == 0)
                return false;

            int exp = bexp - 1023;
            if (exp < 0)
                return false;

            // Check fractional part
            ulong mantOnly = absBits & 0x000F_FFFF_FFFF_FFFFUL;
            if (exp < 52)
            {
                ulong fracMask = (1UL << (52 - exp)) - 1UL;
                if ((mantOnly & fracMask) != 0)
                    return false;
            }

            if (exp > 63)
                return false;

            ulong mant = mantOnly | (1UL << 52); // implicit leading 1

            ulong intVal;
            if (exp >= 52)
                intVal = mant << (exp - 52);
            else
                intVal = mant >> (52 - exp);

            if (!neg)
            {
                if (intVal > 0x7FFF_FFFF_FFFF_FFFFUL)
                    return false;
                result = (long)intVal;
                return true;
            }
            else
            {
                // allow exactly 2^63 => long.MinValue
                if (intVal == 0x8000_0000_0000_0000UL)
                {
                    result = unchecked((long)0x8000_0000_0000_0000UL);
                    return true;
                }
                if (intVal > 0x7FFF_FFFF_FFFF_FFFFUL)
                    return false;

                result = -(long)intVal;
                return true;
            }
        }

        // Ported from musl libm, which takes these from FreeBSD msun under this notice:
        // Copyright (C) 1993 by Sun Microsystems, Inc. All rights reserved. Developed at SunPro, a Sun Microsystems, Inc. business.
        // Permission to use, copy, modify, and distribute this software is freely granted, provided that this notice is preserved.
        private const double PiOver2Hi = 1.57079632679489655800e+00;
        private const double PiOver2Lo = 6.12323399573676603587e-17;
        private const double Ln2Hi = 6.93147180369123816490e-01;
        private const double Ln2Lo = 1.90821492927058770002e-10;
        private const double Lg1 = 6.666666666666735130e-01;
        private const double Lg2 = 3.999999999940941908e-01;
        private const double Lg3 = 2.857142874366239149e-01;
        private const double Lg4 = 2.222219843214978396e-01;
        private const double Lg5 = 1.818357216161805012e-01;
        private const double Lg6 = 1.531383769920937332e-01;
        private const double Lg7 = 1.479819860511658591e-01;

        private static uint HighWord(double x) => (uint)(BitConverter.DoubleToUInt64Bits(x) >> 32);

        private static double ClearLowWord(double x) => BitConverter.UInt64BitsToDouble(BitConverter.DoubleToUInt64Bits(x) & 0xFFFF_FFFF_0000_0000UL);

        public static double Atan(double x)
        {
            uint ix = HighWord(x);
            bool negative = (ix >> 31) != 0;
            ix &= 0x7fffffff;
            int id;
            if (ix >= 0x44100000)
            {
                if (double.IsNaN(x))
                    return x;
                return negative ? -1.57079632679489655800e+00 : 1.57079632679489655800e+00;
            }
            if (ix < 0x3fdc0000)
            {
                if (ix < 0x3e400000)
                    return x;
                id = -1;
            }
            else
            {
                x = Abs(x);
                if (ix < 0x3ff30000)
                {
                    if (ix < 0x3fe60000)
                    {
                        id = 0;
                        x = (2.0 * x - 1.0) / (2.0 + x);
                    }
                    else
                    {
                        id = 1;
                        x = (x - 1.0) / (x + 1.0);
                    }
                }
                else if (ix < 0x40038000)
                {
                    id = 2;
                    x = (x - 1.5) / (1.0 + 1.5 * x);
                }
                else
                {
                    id = 3;
                    x = -1.0 / x;
                }
            }

            double z = x * x;
            double w = z * z;
            double s1 = z * (3.33333333333329318027e-01 + w * (1.42857142725034663711e-01 + w * (9.09088713343650656196e-02 +
                w * (6.66107313738753120669e-02 + w * (4.97687799461593236017e-02 + w * 1.62858201153657823623e-02)))));
            double s2 = w * (-1.99999999998764832476e-01 + w * (-1.11111104054623557880e-01 + w * (-7.69187620504482999495e-02 +
                w * (-5.83357013379057348645e-02 + w * -3.65315727442169155270e-02))));
            if (id < 0)
                return x - x * (s1 + s2);

            (double hi, double lo) = id switch
            {
                0 => (4.63647609000806093515e-01, 2.26987774529616870924e-17),
                1 => (7.85398163397448278999e-01, 3.06161699786838301793e-17),
                2 => (9.82793723247329054082e-01, 1.39033110312309984516e-17),
                _ => (1.57079632679489655800e+00, 6.12323399573676603587e-17),
            };
            z = hi - (x * (s1 + s2) - lo - x);
            return negative ? -z : z;
        }

        public static double Atan2(double y, double x)
        {
            const double Pi = 3.1415926535897931160E+00;
            const double PiLo = 1.2246467991473531772E-16;
            if (double.IsNaN(x) || double.IsNaN(y))
                return x + y;

            ulong bx = BitConverter.DoubleToUInt64Bits(x);
            ulong by = BitConverter.DoubleToUInt64Bits(y);
            uint ix = (uint)(bx >> 32), lx = (uint)bx, iy = (uint)(by >> 32), ly = (uint)by;
            if (((ix - 0x3ff00000) | lx) == 0)
                return Atan(y);

            uint m = ((iy >> 31) & 1) | ((ix >> 30) & 2);
            ix &= 0x7fffffff;
            iy &= 0x7fffffff;
            if ((iy | ly) == 0)
                return m switch { 0 or 1 => y, 2 => Pi, _ => -Pi };
            if ((ix | lx) == 0)
                return (m & 1) != 0 ? -Pi / 2 : Pi / 2;
            if (ix == 0x7ff00000)
            {
                if (iy == 0x7ff00000)
                    return m switch { 0 => Pi / 4, 1 => -Pi / 4, 2 => 3 * Pi / 4, _ => -3 * Pi / 4 };
                return m switch { 0 => 0.0, 1 => -0.0, 2 => Pi, _ => -Pi };
            }
            if (ix + (64 << 20) < iy || iy == 0x7ff00000)
                return (m & 1) != 0 ? -Pi / 2 : Pi / 2;

            double z = (m & 2) != 0 && iy + (64 << 20) < ix ? 0.0 : Atan(Abs(y / x));
            return m switch { 0 => z, 1 => -z, 2 => Pi - (z - PiLo), _ => (z - PiLo) - Pi };
        }

        private static double AsinRatio(double z)
        {
            double p = z * (1.66666666666666657415e-01 + z * (-3.25565818622400915405e-01 + z * (2.01212532134862925881e-01 +
                z * (-4.00555345006794114027e-02 + z * (7.91534994289814532176e-04 + z * 3.47933107596021167570e-05)))));
            double q = 1.0 + z * (-2.40339491173441421878e+00 + z * (2.02094576023350569471e+00 +
                z * (-6.88283971605453293030e-01 + z * 7.70381505559019352791e-02)));
            return p / q;
        }

        public static double Asin(double x)
        {
            uint hx = HighWord(x);
            uint ix = hx & 0x7fffffff;
            if (ix >= 0x3ff00000)
            {
                if (((ix - 0x3ff00000) | (uint)BitConverter.DoubleToUInt64Bits(x)) == 0)
                    return x * PiOver2Hi;
                return double.NaN;
            }
            if (ix < 0x3fe00000)
            {
                if (ix < 0x3e500000 && ix >= 0x00100000)
                    return x;
                return x + x * AsinRatio(x * x);
            }

            double z = (1 - Abs(x)) * 0.5;
            double s = Sqrt(z);
            double r = AsinRatio(z);
            double result;
            if (ix >= 0x3fef3333)
            {
                result = PiOver2Hi - (2 * (s + s * r) - PiOver2Lo);
            }
            else
            {
                double f = ClearLowWord(s);
                double c = (z - f * f) / (s + f);
                result = 0.5 * PiOver2Hi - (2 * s * r - (PiOver2Lo - 2 * c) - (0.5 * PiOver2Hi - 2 * f));
            }
            return (hx >> 31) != 0 ? -result : result;
        }

        public static double Acos(double x)
        {
            uint hx = HighWord(x);
            uint ix = hx & 0x7fffffff;
            if (ix >= 0x3ff00000)
            {
                if (((ix - 0x3ff00000) | (uint)BitConverter.DoubleToUInt64Bits(x)) == 0)
                    return (hx >> 31) != 0 ? 2 * PiOver2Hi : 0.0;
                return double.NaN;
            }
            if (ix < 0x3fe00000)
            {
                if (ix <= 0x3c600000)
                    return PiOver2Hi;
                return PiOver2Hi - (x - (PiOver2Lo - x * AsinRatio(x * x)));
            }
            if ((hx >> 31) != 0)
            {
                double zn = (1.0 + x) * 0.5;
                double sn = Sqrt(zn);
                return 2 * (PiOver2Hi - (sn + (AsinRatio(zn) * sn - PiOver2Lo)));
            }

            double z = (1.0 - x) * 0.5;
            double s = Sqrt(z);
            double df = ClearLowWord(s);
            double c = (z - df * df) / (s + df);
            return 2 * (df + (AsinRatio(z) * s + c));
        }

        public static double Tan(double x)
        {
            (double sin, double cos) = SinCos(x);
            return sin / cos;
        }

        // exp(x)/2 for x >= log(double.MaxValue), without overflowing on the way
        private static double ExpOverTwo(double x, double sign)
        {
            double scale = BitConverter.UInt64BitsToDouble((ulong)(0x3ff + 2043 / 2) << 52);
            double kLn2 = BitConverter.UInt64BitsToDouble(0x40962066151ADD8BUL);
            return Exp(x - kLn2) * (sign * scale) * scale;
        }

        private static double ExpM1(double x)
        {
            const double InvLn2 = 1.44269504088896338700e+00;
            const double Q1 = -3.33333333333331316428e-02;
            const double Q2 = 1.58730158725481460165e-03;
            const double Q3 = -7.93650757867487942473e-05;
            const double Q4 = 4.00821782732936239552e-06;
            const double Q5 = -2.01099218183624371326e-07;
            ulong bits = BitConverter.DoubleToUInt64Bits(x);
            uint hx = (uint)(bits >> 32) & 0x7fffffff;
            bool negative = (bits >> 63) != 0;
            int k;
            double c = 0;
            if (hx >= 0x4043687A)
            {
                if (double.IsNaN(x))
                    return x;
                if (negative)
                    return -1;
                if (x > 7.09782712893383973096e+02)
                    return double.PositiveInfinity;
            }
            if (hx > 0x3fd62e42)
            {
                double hi, lo;
                if (hx < 0x3FF0A2B2)
                {
                    k = negative ? -1 : 1;
                    hi = negative ? x + Ln2Hi : x - Ln2Hi;
                    lo = negative ? -Ln2Lo : Ln2Lo;
                }
                else
                {
                    k = (int)(InvLn2 * x + (negative ? -0.5 : 0.5));
                    hi = x - k * Ln2Hi;
                    lo = k * Ln2Lo;
                }
                x = hi - lo;
                c = (hi - x) - lo;
            }
            else if (hx < 0x3c900000)
            {
                return x;
            }
            else
            {
                k = 0;
            }

            double hfx = 0.5 * x;
            double hxs = x * hfx;
            double r1 = 1.0 + hxs * (Q1 + hxs * (Q2 + hxs * (Q3 + hxs * (Q4 + hxs * Q5))));
            double t = 3.0 - r1 * hfx;
            double e = hxs * ((r1 - t) / (6.0 - x * t));
            if (k == 0)
                return x - (x * e - hxs);
            e = x * (e - c) - c;
            e -= hxs;
            if (k == -1)
                return 0.5 * (x - e) - 0.5;
            if (k == 1)
                return x < -0.25 ? -2.0 * (e - (x + 0.5)) : 1.0 + 2.0 * (x - e);

            double twoPowK = BitConverter.UInt64BitsToDouble((ulong)(0x3ff + k) << 52);
            if (k < 0 || k > 56)
            {
                double y = x - e + 1.0;
                y = k == 1024 ? y * 2.0 * BitConverter.UInt64BitsToDouble(0x7FE0_0000_0000_0000UL) : y * twoPowK;
                return y - 1.0;
            }
            double twoPowMinusK = BitConverter.UInt64BitsToDouble((ulong)(0x3ff - k) << 52);
            return k < 20 ? (x - e + (1 - twoPowMinusK)) * twoPowK : (x - (e + twoPowMinusK) + 1) * twoPowK;
        }

        private static double LogP1(double x)
        {
            uint hx = HighWord(x);
            int k = 1;
            double c = 0, f = 0;
            if (hx < 0x3fda827a || (hx >> 31) != 0)
            {
                if (hx >= 0xbff00000)
                    return x == -1 ? double.NegativeInfinity : double.NaN;
                if (hx << 1 < 0x3ca00000u << 1)
                    return x;
                if (hx <= 0xbfd2bec4)
                {
                    k = 0;
                    f = x;
                }
            }
            else if (hx >= 0x7ff00000)
            {
                return x;
            }
            if (k != 0)
            {
                double u = 1 + x;
                uint hu = HighWord(u) + (0x3ff00000 - 0x3fe6a09e);
                k = (int)(hu >> 20) - 0x3ff;
                if (k < 54)
                    c = (k >= 2 ? 1 - (u - x) : x - (u - 1)) / u;
                hu = (hu & 0x000fffff) + 0x3fe6a09e;
                u = BitConverter.UInt64BitsToDouble((ulong)hu << 32 | (BitConverter.DoubleToUInt64Bits(u) & 0xffffffff));
                f = u - 1;
            }

            double hfsq = 0.5 * f * f;
            double s = f / (2.0 + f);
            double z = s * s;
            double w = z * z;
            double r = z * (Lg1 + w * (Lg3 + w * (Lg5 + w * Lg7))) + w * (Lg2 + w * (Lg4 + w * Lg6));
            double dk = k;
            return s * (hfsq + r) + (dk * Ln2Lo + c) - hfsq + f + dk * Ln2Hi;
        }

        // Splits log(x) into k*ln2 + hi + lo for the base 2 and base 10 kernels, or returns false with the special-case result
        private static bool TrySplitLog(double x, out int k, out double hi, out double lo, out double special)
        {
            ulong u = BitConverter.DoubleToUInt64Bits(x);
            uint hx = (uint)(u >> 32);
            k = 0;
            hi = lo = special = 0;
            if (hx < 0x00100000 || (hx >> 31) != 0)
            {
                if (u << 1 == 0)
                {
                    special = double.NegativeInfinity;
                    return false;
                }
                if ((hx >> 31) != 0)
                {
                    special = double.NaN;
                    return false;
                }
                k -= 54;
                u = BitConverter.DoubleToUInt64Bits(x * 18014398509481984.0);
                hx = (uint)(u >> 32);
            }
            else if (hx >= 0x7ff00000)
            {
                special = x;
                return false;
            }
            else if (hx == 0x3ff00000 && u << 32 == 0)
            {
                return false;
            }

            hx += 0x3ff00000 - 0x3fe6a09e;
            k += (int)(hx >> 20) - 0x3ff;
            hx = (hx & 0x000fffff) + 0x3fe6a09e;
            double f = BitConverter.UInt64BitsToDouble((ulong)hx << 32 | (u & 0xffffffff)) - 1.0;
            double hfsq = 0.5 * f * f;
            double s = f / (2.0 + f);
            double z = s * s;
            double w = z * z;
            double r = z * (Lg1 + w * (Lg3 + w * (Lg5 + w * Lg7))) + w * (Lg2 + w * (Lg4 + w * Lg6));
            hi = ClearLowWord(f - hfsq);
            lo = f - hi - hfsq + s * (hfsq + r);
            return true;
        }

        public static double Log2(double x)
        {
            const double InvLn2Hi = 1.44269504072144627571e+00;
            const double InvLn2Lo = 1.67517131648865118353e-10;
            if (!TrySplitLog(x, out int k, out double hi, out double lo, out double special))
                return special;

            double valHi = hi * InvLn2Hi;
            double valLo = (lo + hi) * InvLn2Lo + lo * InvLn2Hi;
            double y = k;
            double w = y + valHi;
            valLo += (y - w) + valHi;
            return valLo + w;
        }

        public static double Log10(double x)
        {
            const double InvLn10Hi = 4.34294481878168880939e-01;
            const double InvLn10Lo = 2.50829467116452752298e-11;
            const double Log10Of2Hi = 3.01029995663611771306e-01;
            const double Log10Of2Lo = 3.69423907715893078616e-13;
            if (!TrySplitLog(x, out int k, out double hi, out double lo, out double special))
                return special;

            double dk = k;
            double y = dk * Log10Of2Hi;
            double valHi = hi * InvLn10Hi;
            double valLo = dk * Log10Of2Lo + (lo + hi) * InvLn10Lo + lo * InvLn10Hi;
            double w = y + valHi;
            valLo += (y - w) + valHi;
            return valLo + w;
        }

        public static double Log(double a, double newBase)
        {
            if (double.IsNaN(a))
                return a;
            if (double.IsNaN(newBase))
                return newBase;
            if (newBase == 1 || (a != 1 && (newBase == 0 || double.IsPositiveInfinity(newBase))))
                return double.NaN;
            return Log(a) / Log(newBase);
        }

        public static double Sinh(double x)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(x);
            double h = (bits >> 63) != 0 ? -0.5 : 0.5;
            double absX = BitConverter.UInt64BitsToDouble(bits & 0x7FFF_FFFF_FFFF_FFFFUL);
            uint w = HighWord(absX);
            if (w < 0x40862e42)
            {
                double t = ExpM1(absX);
                if (w < 0x3ff00000)
                {
                    if (w < 0x3ff00000 - (26 << 20))
                        return x;
                    return h * (2 * t - t * t / (t + 1));
                }
                return h * (t + t / (t + 1));
            }
            return ExpOverTwo(absX, 2 * h);
        }

        public static double Cosh(double x)
        {
            x = Abs(x);
            uint w = HighWord(x);
            if (w < 0x3fe62e42)
            {
                if (w < 0x3ff00000 - (26 << 20))
                    return 1;
                double t = ExpM1(x);
                return 1 + t * t / (2 * (1 + t));
            }
            if (w < 0x40862e42)
            {
                double t = Exp(x);
                return 0.5 * (t + 1 / t);
            }
            return ExpOverTwo(x, 1.0);
        }

        public static double Tanh(double x)
        {
            bool negative = double.IsNegative(x);
            x = Abs(x);
            uint w = HighWord(x);
            double t;
            if (w > 0x3fe193ea)
            {
                if (w > 0x40340000)
                {
                    t = double.IsNaN(x) ? x : 1;
                }
                else
                {
                    t = ExpM1(2 * x);
                    t = 1 - 2 / (t + 2);
                }
            }
            else if (w > 0x3fd058ae)
            {
                t = ExpM1(2 * x);
                t = t / (t + 2);
            }
            else if (w >= 0x00100000)
            {
                t = ExpM1(-2 * x);
                t = -t / (t + 2);
            }
            else
            {
                t = x;
            }
            return negative ? -t : t;
        }

        public static double Asinh(double x)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(x);
            uint e = (uint)(bits >> 52) & 0x7ff;
            x = Abs(x);
            if (e >= 0x3ff + 26)
                x = Log(x) + Ln2;
            else if (e >= 0x3ff + 1)
                x = Log(2 * x + 1 / (Sqrt(x * x + 1) + x));
            else if (e >= 0x3ff - 26)
                x = LogP1(x + x * x / (Sqrt(x * x + 1) + 1));
            return (bits >> 63) != 0 ? -x : x;
        }

        public static double Acosh(double x)
        {
            uint e = (uint)(BitConverter.DoubleToUInt64Bits(x) >> 52);
            if (e < 0x3ff + 1)
                return LogP1(x - 1 + Sqrt((x - 1) * (x - 1) + 2 * (x - 1)));
            if (e < 0x3ff + 26)
                return Log(2 * x - 1 / (x + Sqrt(x * x - 1)));
            if ((e & 0x800) != 0)
                return double.NaN;
            return Log(x) + Ln2;
        }

        public static double Atanh(double x)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(x);
            uint e = (uint)(bits >> 52) & 0x7ff;
            double y = Abs(x);
            if (e < 0x3ff - 1)
            {
                if (e >= 0x3ff - 32)
                    y = 0.5 * LogP1(2 * y + 2 * y * y / (1 - y));
            }
            else
            {
                y = 0.5 * LogP1(2 * (y / (1 - y)));
            }
            return (bits >> 63) != 0 ? -y : y;
        }

        public static double Cbrt(double x)
        {
            const uint B1 = 715094163;
            const uint B2 = 696219795;
            const double P0 = 1.87595182427177009643;
            const double P1 = -1.88497979543377169875;
            const double P2 = 1.621429720105354466140;
            const double P3 = -0.758397934778766047437;
            const double P4 = 0.145996192886612446982;
            ulong u = BitConverter.DoubleToUInt64Bits(x);
            uint hx = (uint)(u >> 32) & 0x7fffffff;
            if (hx >= 0x7ff00000)
                return x + x;

            if (hx < 0x00100000)
            {
                u = BitConverter.DoubleToUInt64Bits(x * 18014398509481984.0);
                hx = (uint)(u >> 32) & 0x7fffffff;
                if (hx == 0)
                    return x;
                hx = hx / 3 + B2;
            }
            else
            {
                hx = hx / 3 + B1;
            }
            double t = BitConverter.UInt64BitsToDouble((u & (1UL << 63)) | (ulong)hx << 32);

            double r = (t * t) * (t / x);
            t = t * ((P0 + r * (P1 + r * P2)) + ((r * r) * r) * (P3 + r * P4));
            t = BitConverter.UInt64BitsToDouble((BitConverter.DoubleToUInt64Bits(t) + 0x80000000) & 0xffffffffc0000000UL);

            double s = t * t;
            r = x / s;
            double w = t + t;
            r = (r - t) / (w + r);
            return t + t * r;
        }

        internal static double Hypot(double x, double y)
        {
            ulong ux = BitConverter.DoubleToUInt64Bits(x) & 0x7FFF_FFFF_FFFF_FFFFUL;
            ulong uy = BitConverter.DoubleToUInt64Bits(y) & 0x7FFF_FFFF_FFFF_FFFFUL;
            if (ux < uy)
                (ux, uy) = (uy, ux);

            int ex = (int)(ux >> 52);
            int ey = (int)(uy >> 52);
            x = BitConverter.UInt64BitsToDouble(ux);
            y = BitConverter.UInt64BitsToDouble(uy);
            if (ey == 0x7ff)
                return y;
            if (ex == 0x7ff || uy == 0)
                return x;
            if (ex - ey > 64)
                return x + y;

            double z = 1;
            if (ex > 0x3ff + 510)
            {
                z = BitConverter.UInt64BitsToDouble((ulong)(0x3ff + 700) << 52);
                x *= BitConverter.UInt64BitsToDouble((ulong)(0x3ff - 700) << 52);
                y *= BitConverter.UInt64BitsToDouble((ulong)(0x3ff - 700) << 52);
            }
            else if (ey < 0x3ff - 450)
            {
                z = BitConverter.UInt64BitsToDouble((ulong)(0x3ff - 700) << 52);
                x *= BitConverter.UInt64BitsToDouble((ulong)(0x3ff + 700) << 52);
                y *= BitConverter.UInt64BitsToDouble((ulong)(0x3ff + 700) << 52);
            }
            (double hx, double lx) = Square(x);
            (double hy, double ly) = Square(y);
            return z * Sqrt(ly + lx + hy + hx);

            static (double Hi, double Lo) Square(double v)
            {
                double c = v * 134217729.0;
                double vh = v - c + c;
                double vl = v - vh;
                double hi = v * v;
                return (hi, vh * vh - hi + 2 * vh * vl + vl * vl);
            }
        }

        public static int ILogB(double x)
        {
            ulong i = BitConverter.DoubleToUInt64Bits(x);
            int e = (int)(i >> 52) & 0x7ff;
            if (e == 0)
            {
                i <<= 12;
                if (i == 0)
                    return int.MinValue;
                for (e = -0x3ff; i >> 63 == 0; e--, i <<= 1)
                {
                }
                return e;
            }
            if (e == 0x7ff)
                return int.MaxValue;
            return e - 0x3ff;
        }

        public static double ScaleB(double x, int n)
        {
            double y = x;
            if (n > 1023)
            {
                y *= BitConverter.UInt64BitsToDouble(0x7FE0_0000_0000_0000UL);
                n -= 1023;
                if (n > 1023)
                {
                    y *= BitConverter.UInt64BitsToDouble(0x7FE0_0000_0000_0000UL);
                    n -= 1023;
                    if (n > 1023)
                        n = 1023;
                }
            }
            else if (n < -1022)
            {
                // Keep the final n below -53 so the subnormal result is rounded once
                double down = BitConverter.UInt64BitsToDouble((ulong)(0x3ff - 1022 + 53) << 52);
                y *= down;
                n += 1022 - 53;
                if (n < -1022)
                {
                    y *= down;
                    n += 1022 - 53;
                    if (n < -1022)
                        n = -1022;
                }
            }
            return y * BitConverter.UInt64BitsToDouble((ulong)(0x3ff + n) << 52);
        }

        public static double IEEERemainder(double x, double y)
        {
            ulong uxBits = BitConverter.DoubleToUInt64Bits(x);
            ulong uy = BitConverter.DoubleToUInt64Bits(y);
            int ex = (int)(uxBits >> 52) & 0x7ff;
            int ey = (int)(uy >> 52) & 0x7ff;
            bool sx = (uxBits >> 63) != 0;
            bool sy = (uy >> 63) != 0;
            ulong uxi = uxBits;
            ulong i;

            if (uy << 1 == 0 || double.IsNaN(y) || ex == 0x7ff)
                return double.NaN;
            if (uxBits << 1 == 0)
                return x;

            if (ex == 0)
            {
                for (i = uxi << 12; i >> 63 == 0; ex--, i <<= 1)
                {
                }
                uxi <<= -ex + 1;
            }
            else
            {
                uxi &= ulong.MaxValue >> 12;
                uxi |= 1UL << 52;
            }
            if (ey == 0)
            {
                for (i = uy << 12; i >> 63 == 0; ey--, i <<= 1)
                {
                }
                uy <<= -ey + 1;
            }
            else
            {
                uy &= ulong.MaxValue >> 12;
                uy |= 1UL << 52;
            }

            uint q = 0;
            if (ex < ey)
            {
                if (ex + 1 != ey)
                    return x;
            }
            else
            {
                for (; ex > ey; ex--)
                {
                    i = uxi - uy;
                    if (i >> 63 == 0)
                    {
                        uxi = i;
                        q++;
                    }
                    uxi <<= 1;
                    q <<= 1;
                }
                i = uxi - uy;
                if (i >> 63 == 0)
                {
                    uxi = i;
                    q++;
                }
                if (uxi == 0)
                {
                    ex = -60;
                }
                else
                {
                    for (; uxi >> 52 == 0; uxi <<= 1, ex--)
                    {
                    }
                }
            }

            if (ex > 0)
            {
                uxi -= 1UL << 52;
                uxi |= (ulong)ex << 52;
            }
            else
            {
                uxi >>= -ex + 1;
            }
            x = BitConverter.UInt64BitsToDouble(uxi);
            if (sy)
                y = -y;
            if (ex == ey || (ex + 1 == ey && (2 * x > y || (2 * x == y && (q & 1) != 0))))
                x -= y;
            return sx ? -x : x;
        }

        private const double ExpOverflowThreshold = 709.782712893384;   // ~ln(Double.MaxValue)
        private const double ExpUnderflowThreshold = -745.133219101941; // ~ln(Double.MinSubnormal)

        public static double Exp(double x)
        {
            if (Double.IsNaN(x))
                return Double.NaN;

            if (x == Double.PositiveInfinity)
                return Double.PositiveInfinity;
            if (x == Double.NegativeInfinity)
                return 0.0;

            if (x >= ExpOverflowThreshold)
                return Double.PositiveInfinity;
            if (x <= ExpUnderflowThreshold)
                return 0.0;

            // x = k*ln2 + r, r in ~[-ln2/2, ln2/2]
            double kReal = x / Ln2;

            int k = (int)kReal;
            double frac = kReal - (double)k;
            if (kReal >= 0.0)
            {
                if (frac > 0.5) k++;
            }
            else
            {
                if (-frac > 0.5) k--;
            }

            double r = x - (double)k * Ln2;

            double r2 = r * r;

            // 1 + r + r^2/2 + r^3/6 + ... + r^10/10!
            double p =
                1.0 +
                r * (1.0 +
                r * (0.5 +
                r * (0.16666666666666666 +
                r * (0.041666666666666664 +
                r * (0.008333333333333333 +
                r * (0.001388888888888889 +
                r * (0.0001984126984126984 +
                r * (0.0000248015873015873 +
                r * (0.0000027557319223985893 +
                r * (0.0000002755731922398589))))))))));

            return Pow2(k) * p;
        }

        private static double Pow2(int k)
        {
            if (k > 1023)
                return Double.PositiveInfinity;
            if (k < -1074)
                return 0.0;

            if (k >= -1022)
            {
                ulong bits = (ulong)(k + 1023) << 52;
                return BitConverter.UInt64BitsToDouble(bits);
            }
            else
            {
                // subnormal
                int shift = k + 1074; // 0..51
                ulong mant = 1UL << shift;
                return BitConverter.UInt64BitsToDouble(mant);
            }
        }

        public static double Log(double x)
        {
            if (Double.IsNaN(x))
                return Double.NaN;

            if (x == 0.0)
                return Double.NegativeInfinity;

            if (x < 0.0)
                return Double.NaN;

            if (x == Double.PositiveInfinity)
                return Double.PositiveInfinity;

            // Decompose x = m * 2^e with m in [1,2)
            ulong bits = BitConverter.DoubleToUInt64Bits(x);
            int bexp = (int)((bits >> 52) & 0x7FF);
            ulong mant = bits & 0x000F_FFFF_FFFF_FFFFUL;

            int e;
            if (bexp == 0)
            {
                const double TwoPow52 = 4503599627370496.0; // 2^52
                x *= TwoPow52;

                bits = BitConverter.DoubleToUInt64Bits(x);
                bexp = (int)((bits >> 52) & 0x7FF);
                mant = bits & 0x000F_FFFF_FFFF_FFFFUL;

                e = (bexp - 1023) - 52;
            }
            else
            {
                e = bexp - 1023;
            }

            // normalize mantissa to [1,2)
            double m = BitConverter.UInt64BitsToDouble(mant | 0x3FF0_0000_0000_0000UL);

            // ln(m) = 2 * (t + t^3/3 + t^5/5 + ...), t = (m-1)/(m+1)
            double t = (m - 1.0) / (m + 1.0);
            double t2 = t * t;

            double s = t;

            double term = t;
            term *= t2; s += term / 3.0;
            term *= t2; s += term / 5.0;
            term *= t2; s += term / 7.0;
            term *= t2; s += term / 9.0;
            term *= t2; s += term / 11.0;

            double ln_m = 2.0 * s;

            return (double)e * Ln2 + ln_m;
        }
    

        public static double Floor(double d)
        {
            double t = Truncate(d);
            return t > d ? t - 1.0 : t;
        }

        public static double Ceiling(double a)
        {
            double t = Truncate(a);
            return t < a ? t + 1.0 : t;
        }

        public static double Round(double value, int digits, MidpointRounding mode)
        {
            if ((uint)digits > maxRoundingDigits)
                throw new ArgumentOutOfRangeException(nameof(digits), SR.ArgumentOutOfRange_RoundingDigits);
            if (mode < MidpointRounding.ToEven || mode > MidpointRounding.ToPositiveInfinity)
                throw new ArgumentOutOfRangeException(nameof(mode));
            if (Abs(value) < doubleRoundLimit)
            {
                double power10 = RoundPower10Double[digits];
                value *= power10;
                value = mode switch
                {
                    MidpointRounding.ToEven => Round(value),
                    MidpointRounding.AwayFromZero => Truncate(value + CopySign(BitDecrement(0.5), value)),
                    MidpointRounding.ToZero => Truncate(value),
                    MidpointRounding.ToNegativeInfinity => Floor(value),
                    _ => Ceiling(value),
                };
                value /= power10;
            }
            return value;
        }


        public static double FusedMultiplyAdd(double x, double y, double z)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y))
                return x * y + z;
            if (!double.IsFinite(z))
                return z;
            if (x == 0 || y == 0)
                return x * y + z;
            if (z == 0)
                return x * y;
            return FusedMultiplyAddCore(x, y, z, mantissaBits: 52, minExponent: -1022, maxExponent: 1023);
        }

        // Finite, nonzero operands; rounds x * y + z once, to nearest even, at the given binary format's precision.
        internal static double FusedMultiplyAddCore(double x, double y, double z, int mantissaBits, int minExponent, int maxExponent)
        {
            ulong mx = Decompose(x, out int ex, out bool sx);
            ulong my = Decompose(y, out int ey, out bool sy);
            ulong mz = Decompose(z, out int ez, out bool sz);

            // The product's 106 bits sit at the bottom of a 128-bit frame; z's 53 bits start aligned with its top.
            ulong phi = Math.BigMul(mx, my, out ulong plo);
            int pe = ex + ey;
            ulong zhi = mz >> 12, zlo = mz << 52;
            int ze = ez - 52;
            bool productSign = sx != sy;

            // Align to the smaller exponent while the larger operand still fits; past that the smaller one only rounds.
            int d = ze - pe;
            int frame;
            if (d >= 0)
            {
                int up = Math.Min(d, 22);
                ShiftLeft(ref zhi, ref zlo, up);
                ShiftRightSticky(ref phi, ref plo, d - up);
                frame = ze - up;
            }
            else
            {
                ShiftRightSticky(ref zhi, ref zlo, -d);
                frame = pe;
            }

            ulong rhi, rlo;
            bool negative = productSign;
            if (productSign == sz)
            {
                rlo = plo + zlo;
                rhi = phi + zhi + (rlo < plo ? 1UL : 0UL);
            }
            else if (phi > zhi || (phi == zhi && plo >= zlo))
            {
                rlo = plo - zlo;
                rhi = phi - zhi - (plo < zlo ? 1UL : 0UL);
            }
            else
            {
                rlo = zlo - plo;
                rhi = zhi - phi - (zlo < plo ? 1UL : 0UL);
                negative = sz;
            }
            if (rhi == 0 && rlo == 0)
                return 0.0;

            int top = rhi != 0 ? 127 - System.Numerics.BitOperations.LeadingZeroCount(rhi) : 63 - System.Numerics.BitOperations.LeadingZeroCount(rlo);
            int exponent = top + frame;
            // Bits below the result's last place: the precision's, or fewer once the exponent reaches the subnormal range.
            int drop = top - mantissaBits;
            int minDrop = minExponent - mantissaBits - frame;
            if (drop < minDrop)
                drop = minDrop;

            ulong mantissa;
            if (drop <= 0)
            {
                mantissa = rlo << -drop;
            }
            else
            {
                ulong qhi = rhi, qlo = rlo;
                TakeLowBits(ref qhi, ref qlo, drop, out bool halfway, out bool aboveHalf);
                mantissa = qlo;
                if (aboveHalf || (halfway && (mantissa & 1) != 0))
                    mantissa++;
            }

            int lsbExponent = frame + drop;
            if (mantissa >> (mantissaBits + 1) != 0)
            {
                mantissa >>= 1;
                lsbExponent++;
            }
            if (mantissa == 0)
                return negative ? -0.0 : 0.0;
            if (lsbExponent + mantissaBits > maxExponent)
                return negative ? double.NegativeInfinity : double.PositiveInfinity;

            int length = 64 - System.Numerics.BitOperations.LeadingZeroCount(mantissa);
            int topExponent = lsbExponent + length - 1;
            ulong bits = topExponent >= -1022
                ? ((ulong)(topExponent + 1023) << 52) | ((mantissa << (53 - length)) & 0xF_FFFF_FFFF_FFFFUL)
                : mantissa << (lsbExponent + 1074);
            return BitConverter.UInt64BitsToDouble(negative ? bits | (1UL << 63) : bits);
        }

        private static ulong Decompose(double value, out int exponent, out bool negative)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(value);
            negative = (long)bits < 0;
            int biased = (int)(bits >> 52) & 0x7FF;
            ulong mantissa = bits & 0xF_FFFF_FFFF_FFFFUL;
            if (biased == 0)
            {
                int shift = System.Numerics.BitOperations.LeadingZeroCount(mantissa) - 11;
                mantissa <<= shift;
                exponent = -1074 - shift;
            }
            else
            {
                mantissa |= 1UL << 52;
                exponent = biased - 1075;
            }
            return mantissa;
        }

        private static void ShiftLeft(ref ulong hi, ref ulong lo, int count)
        {
            if (count == 0)
                return;
            hi = (hi << count) | (lo >> (64 - count));
            lo <<= count;
        }

        // Shifted-out bits collapse into bit 0, which stays below every rounding position.
        private static void ShiftRightSticky(ref ulong hi, ref ulong lo, int count)
        {
            if (count == 0)
                return;
            bool sticky;
            if (count >= 128)
            {
                sticky = (hi | lo) != 0;
                hi = 0;
                lo = 0;
            }
            else if (count >= 64)
            {
                sticky = lo != 0 || (count > 64 && (hi << (128 - count)) != 0);
                lo = count == 64 ? hi : hi >> (count - 64);
                hi = 0;
            }
            else
            {
                sticky = (lo << (64 - count)) != 0;
                lo = (lo >> count) | (hi << (64 - count));
                hi >>= count;
            }
            if (sticky)
                lo |= 1;
        }

        private static void TakeLowBits(ref ulong hi, ref ulong lo, int count, out bool halfway, out bool aboveHalf)
        {
            // count is in 1..127; the low part compares against the half of its range
            ulong remHi, remLo, halfHi, halfLo;
            if (count >= 64)
            {
                remLo = lo;
                remHi = count == 64 ? 0 : hi & ((1UL << (count - 64)) - 1);
                halfHi = count == 64 ? 0 : 1UL << (count - 65);
                halfLo = count == 64 ? 1UL << 63 : 0;
                lo = count == 64 ? hi : hi >> (count - 64);
                hi = 0;
            }
            else
            {
                remHi = 0;
                remLo = lo & ((1UL << count) - 1);
                halfHi = 0;
                halfLo = 1UL << (count - 1);
                lo = (lo >> count) | (hi << (64 - count));
                hi >>= count;
            }
            halfway = remHi == halfHi && remLo == halfLo;
            aboveHalf = remHi > halfHi || (remHi == halfHi && remLo > halfLo);
        }
    }

    // delegates

    public delegate bool Predicate<in T>(T obj)
        where T : allows ref struct;

    public delegate TResult Func<out TResult>()
        where TResult : allows ref struct;

    public delegate TResult Func<in T, out TResult>(T arg)
        where T : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, out TResult>(T1 arg1, T2 arg2)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, out TResult>(T1 arg1, T2 arg2, T3 arg3)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct
        where T15 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15, in T16, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct
        where T15 : allows ref struct
        where T16 : allows ref struct
        where TResult : allows ref struct;

    public delegate void Action();

    public delegate void Action<in T>(T obj)
        where T : allows ref struct;

    public delegate void Action<in T1, in T2>(T1 arg1, T2 arg2)
        where T1 : allows ref struct
        where T2 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3>(T1 arg1, T2 arg2, T3 arg3)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4>(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct
        where T15 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15, in T16>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct
        where T15 : allows ref struct
        where T16 : allows ref struct;


    public static class GC
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void KeepAlive(object? obj) { }

        // Finalizers never run yet, so these only validate their argument
        public static void SuppressFinalize(object obj) => ArgumentNullException.ThrowIfNull(obj);

        public static void ReRegisterForFinalize(object obj) => ArgumentNullException.ThrowIfNull(obj);

        private enum StartNoGCRegionStatus
        {
            Succeeded,
            NotEnoughMemory,
            AmountTooLarge,
            AlreadyInProgress
        }

        private enum EndNoGCRegionStatus
        {
            Succeeded,
            NotInProgress,
            GCInduced,
            AllocationExceeded
        }


        /// <summary>
        /// Allocate an array while skipping zero-initialization if possible.
        /// </summary>
        /// <typeparam name="T">Specifies the type of the array element.</typeparam>
        /// <param name="length">Specifies the length of the array.</param>
        /// <param name="pinned">Specifies whether the allocated array must be pinned.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // forced to ensure no perf drop for small memory buffers (hot path)
        public static unsafe T[] AllocateUninitializedArray<T>(int length, bool pinned = false)
        {
            if (!pinned)
            {
                if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                {
                    return new T[length];
                }
            }

            return AllocateNewUninitializedArray(length, pinned);

            static T[] AllocateNewUninitializedArray(int length, bool pinned)
            {
                Internal.Runtime.GC_ALLOC_FLAGS flags = Internal.Runtime.GC_ALLOC_FLAGS.GC_ALLOC_ZEROING_OPTIONAL;
                if (pinned)
                    flags |= Internal.Runtime.GC_ALLOC_FLAGS.GC_ALLOC_PINNED_OBJECT_HEAP;
                if (length < 0)
                    throw new OverflowException();

                T[]? array = System.Runtime.RuntimeImports.RhAllocateNewArray<T>(length, (uint)flags);
                if (array == null)
                    throw new OutOfMemoryException();

                return array;
            }
        }
    }


    public enum PlatformID
    {
        Win32S = 0,
        Win32Windows = 1,
        Win32NT = 2,
        WinCE = 3,
        Unix = 4,
        Xbox = 5,
        MacOSX = 6,
        Other = 7
    }

    public sealed class Version
    {
        private readonly int _Major; // Do not rename
        private readonly int _Minor; // Do not rename
        private readonly int _Build; // Do not rename
        private readonly int _Revision; // Do not rename

        public Version(int major, int minor, int build, int revision)
        {
            if (major < 0 || minor < 0 || build < 0 || revision < 0) throw new ArgumentOutOfRangeException();
            _Major = major;
            _Minor = minor;
            _Build = build;
            _Revision = revision;
        }

        public Version(int major, int minor, int build)
        {
            if (major < 0 || minor < 0 || build < 0) throw new ArgumentOutOfRangeException();
            _Major = major;
            _Minor = minor;
            _Build = build;
            _Revision = -1;
        }

        public Version(int major, int minor)
        {
            if (major < 0 || minor < 0) throw new ArgumentOutOfRangeException();
            _Major = major;
            _Minor = minor;
            _Build = -1;
            _Revision = -1;
        }

        public Version()
        {
            //_Major = 0;
            //_Minor = 0;
            _Build = -1;
            _Revision = -1;
        }

        private Version(Version version)
        {
            _Major = version._Major;
            _Minor = version._Minor;
            _Build = version._Build;
            _Revision = version._Revision;
        }

        public int Major => _Major;

        public int Minor => _Minor;

        public int Build => _Build;

        public int Revision => _Revision;

        public short MajorRevision => (short)(_Revision >> 16);

        public short MinorRevision => (short)(_Revision & 0xFFFF);

        public int CompareTo(object? version)
        {
            if (version == null)
            {
                return 1;
            }

            if (version is Version v)
            {
                return CompareTo(v);
            }

            throw new ArgumentException();
        }

        public int CompareTo(Version? value)
        {
            return
                ReferenceEquals(value, this) ? 0 :
                value is null ? 1 :
                _Major != value._Major ? (_Major > value._Major ? 1 : -1) :
                _Minor != value._Minor ? (_Minor > value._Minor ? 1 : -1) :
                _Build != value._Build ? (_Build > value._Build ? 1 : -1) :
                _Revision != value._Revision ? (_Revision > value._Revision ? 1 : -1) :
                0;
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return Equals(obj as Version);
        }

        public bool Equals([NotNullWhen(true)] Version? obj)
        {
            return ReferenceEquals(obj, this) ||
                (obj is not null &&
                _Major == obj._Major &&
                _Minor == obj._Minor &&
                _Build == obj._Build &&
                _Revision == obj._Revision);
        }

        public override int GetHashCode()
        {
            // Let's assume that most version numbers will be pretty small and just
            // OR some lower order bits together.

            int accumulator = 0;

            accumulator |= (_Major & 0x0000000F) << 28;
            accumulator |= (_Minor & 0x000000FF) << 20;
            accumulator |= (_Build & 0x000000FF) << 12;
            accumulator |= (_Revision & 0x00000FFF);

            return accumulator;
        }

        public bool TryFormat(Span<char> destination, int fieldCount, out int charsWritten) =>
            TryFormatCore(destination, fieldCount, out charsWritten);

        private bool TryFormatCore<TChar>(Span<TChar> destination, int fieldCount, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            switch ((uint)fieldCount)
            {
                case > 4:
                    ThrowArgumentException("4");
                    break;

                case >= 3 when _Build == -1:
                    ThrowArgumentException("2");
                    break;

                case 4 when _Revision == -1:
                    ThrowArgumentException("3");
                    break;

                    static void ThrowArgumentException(string failureUpperBound) =>
                        throw new ArgumentException(failureUpperBound, nameof(fieldCount));
            }

            int totalCharsWritten = 0;

            for (int i = 0; i < fieldCount; i++)
            {
                if (i != 0)
                {
                    if (destination.IsEmpty)
                    {
                        charsWritten = 0;
                        return false;
                    }

                    destination[0] = TChar.CastFrom('.');
                    destination = destination.Slice(1);
                    totalCharsWritten++;
                }
                int value = i switch
                {
                    0 => _Major,
                    1 => _Minor,
                    2 => _Build,
                    _ => _Revision
                };
                int valueCharsWritten;
                bool formatted = typeof(TChar) == typeof(char) ?
                    ((uint)value).TryFormat(Unsafe.BitCast<Span<TChar>, Span<char>>(destination), out valueCharsWritten) :
                    ((uint)value).TryFormat(Unsafe.BitCast<Span<TChar>, Span<byte>>(destination), out valueCharsWritten, 
                        default, System.Globalization.CultureInfo.InvariantCulture);

                totalCharsWritten += valueCharsWritten;
                destination = destination.Slice(valueCharsWritten);
            }

            charsWritten = totalCharsWritten;
            return true;
        }

        public override string ToString() =>
            ToString(DefaultFormatFieldCount);

        public unsafe string ToString(int fieldCount)
        {
            Span<char> dest = stackalloc char[(4 * Number.Int32NumberBufferLength) + 3]; // at most 4 Int32s and 3 periods
            bool success = TryFormat(dest, fieldCount, out int charsWritten);
            return dest.Slice(0, charsWritten).ToString();
        }

        private int DefaultFormatFieldCount =>
            _Build == -1 ? 2 :
            _Revision == -1 ? 3 :
            4;
    }

    public sealed class OperatingSystem
    {
        private readonly Version _version;
        private readonly string? _servicePack;
        private readonly PlatformID _platform;
        private string? _versionString;

        public PlatformID Platform => _platform;

        public string ServicePack => _servicePack ?? string.Empty;

        public Version Version => _version;

        public unsafe string VersionString
        {
            get
            {
                if (_versionString == null)
                {
                    string os;
                    switch (_platform)
                    {
                        case PlatformID.Win32S: os = "Microsoft Win32S "; break;
                        case PlatformID.Win32Windows:
                            os = (_version.Major > 4 || (_version.Major == 4 && _version.Minor > 0))
                                ? "Microsoft Windows 98 " : "Microsoft Windows 95 "; break;
                        case PlatformID.Win32NT: os = "Microsoft Windows NT "; break;
                        case PlatformID.WinCE: os = "Microsoft Windows CE "; break;
                        case PlatformID.Unix: os = "Unix "; break;
                        case PlatformID.Xbox: os = "Xbox "; break;
                        case PlatformID.MacOSX: os = "Mac OS X "; break;
                        case PlatformID.Other: os = "Other "; break;
                        default:
                            os = "<unknown> "; break;
                    }

                    Span<char> stackBuffer = stackalloc char[128];
                    _versionString = string.IsNullOrEmpty(_servicePack) ?
                        string.Create(null, stackBuffer, $"{os}{_version}") :
                        string.Create(null, stackBuffer, $"{os}{_version.ToString(3)} {_servicePack}");
                }

                return _versionString;
            }
        }


        private static bool IsOSVersionAtLeast(int major, int minor, int build, int revision)
        {
            Version current = Environment.OSVersion.Version;

            if (current.Major != major)
            {
                return current.Major > major;
            }
            if (current.Minor != minor)
            {
                return current.Minor > minor;
            }
            // Unspecified build component is to be treated as zero
            int currentBuild = current.Build < 0 ? 0 : current.Build;
            build = build < 0 ? 0 : build;
            if (currentBuild != build)
            {
                return currentBuild > build;
            }

            // Unspecified revision component is to be treated as zero
            int currentRevision = current.Revision < 0 ? 0 : current.Revision;
            revision = revision < 0 ? 0 : revision;

            return currentRevision >= revision;
        }
    }

    public enum TypeCode
    {
        Empty = 0,          // Null reference
        Object = 1,         // Instance that isn't a value
        DBNull = 2,         // Database null value
        Boolean = 3,        // Boolean
        Char = 4,           // Unicode character
        SByte = 5,          // Signed 8-bit integer
        Byte = 6,           // Unsigned 8-bit integer
        Int16 = 7,          // Signed 16-bit integer
        UInt16 = 8,         // Unsigned 16-bit integer
        Int32 = 9,          // Signed 32-bit integer
        UInt32 = 10,        // Unsigned 32-bit integer
        Int64 = 11,         // Signed 64-bit integer
        UInt64 = 12,        // Unsigned 64-bit integer
        Single = 13,        // IEEE 32-bit float
        Double = 14,        // IEEE 64-bit double
        Decimal = 15,       // Decimal
        DateTime = 16,      // DateTime
        String = 18,        // Unicode character string
    }

    public abstract class Type : System.Reflection.MemberInfo, System.Reflection.IReflect
    {
        protected Type() { }

        [Intrinsic]
        public static Type GetTypeFromHandle(RuntimeTypeHandle handle) => RuntimeType.FromHandle(handle._value);

        public abstract string? Namespace { get; }
        public abstract string? AssemblyQualifiedName { get; }
        public abstract string? FullName { get; }

        public abstract Type UnderlyingSystemType { get; }

        public virtual bool IsEnum { [Intrinsic] get => throw new NotImplementedException(); }
        public bool IsPrimitive
        {
            [Intrinsic]
            get => IsPrimitiveImpl();
        }
        protected abstract bool IsPrimitiveImpl();
        public bool IsValueType
        {
            [Intrinsic]
            get => IsValueTypeImpl();
        }
        protected virtual bool IsValueTypeImpl() => throw new NotImplementedException();

        public bool IsArray => IsArrayImpl();
        protected abstract bool IsArrayImpl();
        public bool IsByRef => IsByRefImpl();
        protected abstract bool IsByRefImpl();
        public bool IsPointer => IsPointerImpl();
        protected abstract bool IsPointerImpl();
    }
    // One instance per type, so Type equality is reference equality, as in runtime.
    internal sealed class RuntimeType : Type
    {
        // RuntimeTypeInfoFlags of the compiler
        private const int ValueTypeFlag = 1 << 0;
        private const int EnumFlag = 1 << 1;
        private const int PrimitiveFlag = 1 << 2;
        private const int ArrayFlag = 1 << 3;
        private const int PointerFlag = 1 << 4;
        private const int ByRefFlag = 1 << 5;

        private static readonly object s_lock = new object();
        private static RuntimeType?[] s_types = new RuntimeType?[64];
        private static int s_count;

        private readonly IntPtr _handle;
        private string? _name;
        private string? _fullName;

        private RuntimeType(IntPtr handle) => _handle = handle;

        internal static RuntimeType FromHandle(IntPtr handle)
        {
            lock (s_lock)
            {
                RuntimeType?[] types = s_types;
                int index = Slot(handle, types.Length);
                for (RuntimeType? type; (type = types[index]) is not null; index = (index + 1) & (types.Length - 1))
                {
                    if (type._handle == handle)
                        return type;
                }

                var created = new RuntimeType(handle);
                types[index] = created;
                if (++s_count * 2 > types.Length)
                    s_types = Grow(types);
                return created;
            }
        }

        private static int Slot(IntPtr handle, int length) => (int)((ulong)(nuint)handle * 0x9E3779B97F4A7C15UL >> 40) & (length - 1);

        private static RuntimeType?[] Grow(RuntimeType?[] types)
        {
            var grown = new RuntimeType?[types.Length * 2];
            foreach (RuntimeType? type in types)
            {
                if (type is null)
                    continue;
                int index = Slot(type._handle, grown.Length);
                while (grown[index] is not null)
                    index = (index + 1) & (grown.Length - 1);
                grown[index] = type;
            }
            return grown;
        }

        public override string Name => _name ??= System.Runtime.RuntimeImports.RhGetTypeName(_handle);
        public override string? Namespace => System.Runtime.RuntimeImports.RhGetTypeNamespace(_handle);
        public override string? FullName => _fullName ??= System.Runtime.RuntimeImports.RhGetTypeFullName(_handle);
        public override string? AssemblyQualifiedName
            => FullName is string fullName ? fullName + ", " + System.Runtime.RuntimeImports.RhGetTypeAssemblyName(_handle) : null;
        public override Type UnderlyingSystemType => this;

        public override bool IsEnum => (Flags & EnumFlag) != 0;
        protected override bool IsPrimitiveImpl() => (Flags & PrimitiveFlag) != 0;
        protected override bool IsValueTypeImpl() => (Flags & ValueTypeFlag) != 0;
        protected override bool IsArrayImpl() => (Flags & ArrayFlag) != 0;
        protected override bool IsByRefImpl() => (Flags & ByRefFlag) != 0;
        protected override bool IsPointerImpl() => (Flags & PointerFlag) != 0;

        public override string ToString() => System.Runtime.RuntimeImports.RhGetTypeDisplayName(_handle);

        private int Flags => System.Runtime.RuntimeImports.RhGetTypeFlags(_handle);
    }

    public enum AttributeTargets
    {
        Assembly = 0x0001,
        Module = 0x0002,
        Class = 0x0004,
        Struct = 0x0008,
        Enum = 0x0010,
        Constructor = 0x0020,
        Method = 0x0040,
        Property = 0x0080,
        Field = 0x0100,
        Event = 0x0200,
        Interface = 0x0400,
        Parameter = 0x0800,
        Delegate = 0x1000,
        ReturnValue = 0x2000,
        GenericParameter = 0x4000,

        All = Assembly | Module | Class | Struct | Enum | Constructor |
                        Method | Property | Field | Event | Interface | Parameter |
                        Delegate | ReturnValue | GenericParameter
    }

    public class Exception
    {
        private string _message;
        public Exception()
        {
            _message = String.Empty;
        }
        public Exception(String message)
        {
            _message = message;
        }
        public Exception(String message, Exception innerException)
        {
            _message = message;
        }
        public virtual string Message => _message;
        public override string ToString()
        {
            return _message;
        }
    }
    public class ApplicationException : Exception
    {
        public ApplicationException()
            : base()
        { }
        public ApplicationException(string message)
            : base(message)
        { }
        public ApplicationException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
    public class SystemException : Exception
    {
        public SystemException()
            : base()
        { }

        public SystemException(string message)
            : base(message)
        { }

        public SystemException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
    public sealed class TypeInitializationException : SystemException
    {
        private readonly string? _typeName;

        private TypeInitializationException()
            : base()
        { }


        public TypeInitializationException(string? fullTypeName, Exception? innerException)
            : this(fullTypeName, string.Empty, innerException)
        { }

        internal TypeInitializationException(string? message) : base(message ?? string.Empty)
        { }

        internal TypeInitializationException(string? fullTypeName, string? message, Exception? innerException)
            : base(message ?? string.Empty, innerException)
        {
            _typeName = fullTypeName;
        }

        public string TypeName => _typeName ?? string.Empty;
    }
    public class SerializationException : SystemException
    {
        private static string _nullMessage = "Arg_SerializationException";
        public SerializationException()
        : base(_nullMessage)
        { }
        public SerializationException(string message)
        : base(message)
        { }
        public SerializationException(string message, Exception innerException)
       : base(message, innerException)
        { }
    }
    public class InvalidCastException : SystemException
    {
        public InvalidCastException() : base("Specified cast is not valid.") { }
        public InvalidCastException(string message) : base(message) { }
        public InvalidCastException(string message, Exception innerException) : base(message, innerException) { }
    }
    public class FormatException : SystemException
    {
        public FormatException()
            : base() { }
        public FormatException(string message)
            : base(message) { }
        public FormatException(string message, Exception innerException)
            : base(message, innerException) { }
    }
    public class ArrayTypeMismatchException : SystemException
    {
        public ArrayTypeMismatchException() : base() { }
        public ArrayTypeMismatchException(string message) : base(message) { }
        public ArrayTypeMismatchException(string message, Exception innerException) : base(message, innerException) { }
    }
    public class AccessViolationException : SystemException
    {
        public AccessViolationException()
            : base()
        { }

        public AccessViolationException(string message)
            : base(message)
        { }

        public AccessViolationException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
    public class OutOfMemoryException : SystemException
    {
        public OutOfMemoryException() : base(GetDefaultMessage())
        { }

        public OutOfMemoryException(string message)
            : base(message ?? GetDefaultMessage())
        { }

        public OutOfMemoryException(string message, Exception innerException)
            : base(message ?? GetDefaultMessage(), innerException)
        { }

        private static string GetDefaultMessage() => "Out of memory.";
    }
    public class NullReferenceException : SystemException
    {
        public NullReferenceException()
            : base()
        { }

        public NullReferenceException(string message)
            : base(message)
        { }

        public NullReferenceException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
    public class InvalidOperationException : SystemException
    {
        public InvalidOperationException()
            : base()
        { }

        public InvalidOperationException(string message)
            : base(message)
        { }

        public InvalidOperationException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
    public class NotImplementedException : SystemException
    {
        public NotImplementedException()
        : base("Arg_NotImplementedException")
        { }

        public NotImplementedException(string message)
        : base(message)
        { }

        public NotImplementedException(string message, Exception inner)
        : base(message, inner)
        { }
    }
    public class NotSupportedException : SystemException
    {
        public NotSupportedException()
            : base()
        { }

        public NotSupportedException(string message)
            : base(message)
        { }

        public NotSupportedException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
    public class PlatformNotSupportedException : NotSupportedException
    {
        public PlatformNotSupportedException()
            : base()
        { }

        public PlatformNotSupportedException(string message)
            : base(message)
        { }

        public PlatformNotSupportedException(string message, Exception inner)
            : base(message, inner)
        { }
    }
    public class ArithmeticException : SystemException
    {
        public ArithmeticException()
            : base()
        { }

        public ArithmeticException(string message)
            : base(message)
        { }

        public ArithmeticException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
    public class OverflowException : ArithmeticException
    {
        public OverflowException()
            : base("Arithmetic operation resulted in an overflow.")
        { }

        public OverflowException(string message)
            : base(message)
        { }

        public OverflowException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
    public class DivideByZeroException : ArithmeticException
    {
        public DivideByZeroException()
            : base("Attempted to divide by zero.")
        { }

        public DivideByZeroException(string message)
            : base(message)
        { }

        public DivideByZeroException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
    public class ArgumentException : SystemException
    {
        private readonly string _paramName;
        public virtual string ParamName => _paramName;

        public ArgumentException()
            : base()
        { }

        public ArgumentException(string message)
            : base(message)
        { }

        public ArgumentException(string message, Exception innerException)
            : base(message, innerException)
        { }

        public ArgumentException(string message, string paramName, Exception innerException)
            : base(message, innerException)
        {
            _paramName = paramName;
        }

        public ArgumentException(string message, string paramName)
            : base(message)
        {
            _paramName = paramName;
        }
    }
    public class CultureNotFoundException : ArgumentException
    {
        private readonly string _invalidCultureName; // unrecognized culture name
        private readonly int _invalidCultureId;     // unrecognized culture Lcid

        public CultureNotFoundException()
            : base(DefaultMessage)
        {
        }

        public CultureNotFoundException(string message)
            : base(message ?? DefaultMessage)
        {
        }

        public CultureNotFoundException(string paramName, string message)
            : base(message ?? DefaultMessage, paramName)
        {
        }

        public CultureNotFoundException(string message, Exception innerException)
            : base(message ?? DefaultMessage, innerException)
        {
        }

        public CultureNotFoundException(string paramName, string invalidCultureName, string message)
            : base(message ?? DefaultMessage, paramName)
        {
            _invalidCultureName = invalidCultureName;
        }

        public CultureNotFoundException(string message, string invalidCultureName, Exception innerException)
            : base(message ?? DefaultMessage, innerException)
        {
            _invalidCultureName = invalidCultureName;
        }

        public CultureNotFoundException(string message, int invalidCultureId, Exception innerException)
            : base(message ?? DefaultMessage, innerException)
        {
            _invalidCultureId = invalidCultureId;
        }

        public CultureNotFoundException(string paramName, int invalidCultureId, string message)
            : base(message ?? DefaultMessage, paramName)
        {
            _invalidCultureId = invalidCultureId;
        }

        public virtual int InvalidCultureId => _invalidCultureId;

        public virtual string InvalidCultureName => _invalidCultureName;

        private static string DefaultMessage => "Culture not supported.";
    }
    /// <summary>
    /// The exception that is thrown when a <see langword="null"/> reference (<see langword="Nothing"/> in Visual Basic) is passed to a method that does not accept it as a valid argument.
    /// </summary>
    public class ArgumentNullException : ArgumentException
    {
        // Creates a new ArgumentNullException with its message
        // string set to a default message explaining an argument was null.
        public ArgumentNullException()
             : base(SR.ArgumentNull_Generic)
        {
        }

        public ArgumentNullException(string? paramName)
            : base(SR.ArgumentNull_Generic, paramName)
        {
        }

        public ArgumentNullException(string? message, Exception? innerException)
            : base(message ?? SR.ArgumentNull_Generic, innerException)
        {
        }

        public ArgumentNullException(string? paramName, string? message)
            : base(message ?? SR.ArgumentNull_Generic, paramName)
        {
        }

        /// <summary>Throws an <see cref="ArgumentNullException"/> if <paramref name="argument"/> is null.</summary>
        /// <param name="argument">The reference type argument to validate as non-null.</param>
        /// <param name="paramName">The name of the parameter with which <paramref name="argument"/> corresponds.</param>
        [Intrinsic]
        public static void ThrowIfNull([NotNull] object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument is null)
            {
                Throw(paramName);
            }
        }

        /// <summary>Throws an <see cref="ArgumentNullException"/> if <paramref name="argument"/> is null.</summary>
        /// <param name="argument">The pointer argument to validate as non-null.</param>
        /// <param name="paramName">The name of the parameter with which <paramref name="argument"/> corresponds.</param>
        [CLSCompliant(false)]
        public static unsafe void ThrowIfNull([NotNull] void* argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument is null)
            {
                Throw(paramName);
            }
        }

        /// <summary>Throws an <see cref="ArgumentNullException"/> if <paramref name="argument"/> is null.</summary>
        /// <param name="argument">The pointer argument to validate as non-null.</param>
        /// <param name="paramName">The name of the parameter with which <paramref name="argument"/> corresponds.</param>
        internal static void ThrowIfNull(IntPtr argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument == IntPtr.Zero)
            {
                Throw(paramName);
            }
        }

        [DoesNotReturn]
        internal static void Throw(string? paramName) =>
            throw new ArgumentNullException(paramName);
    }

    /// <summary>
    /// The exception that is thrown when the value of an argument is outside the allowable range of values as defined by the invoked method.
    /// </summary>
    public class ArgumentOutOfRangeException : ArgumentException
    {
        private readonly object? _actualValue;

        // Creates a new ArgumentOutOfRangeException with its message
        // string set to a default message explaining an argument was out of range.
        public ArgumentOutOfRangeException()
            : base(SR.Arg_ArgumentOutOfRangeException)
        {
        }

        public ArgumentOutOfRangeException(string? paramName)
            : base(SR.Arg_ArgumentOutOfRangeException, paramName)
        {
        }

        public ArgumentOutOfRangeException(string? paramName, string? message)
            : base(message ?? SR.Arg_ArgumentOutOfRangeException, paramName)
        {
        }

        public ArgumentOutOfRangeException(string? message, Exception? innerException)
            : base(message ?? SR.Arg_ArgumentOutOfRangeException, innerException)
        {
        }

        public ArgumentOutOfRangeException(string? paramName, object? actualValue, string? message)
            : base(message ?? SR.Arg_ArgumentOutOfRangeException, paramName)
        {
            _actualValue = actualValue;
        }

        public override string Message
        {
            get
            {
                string s = base.Message;
                if (_actualValue != null)
                {
                    string valueMessage = SR.Format(SR.ArgumentOutOfRange_ActualValue, _actualValue);
                    if (s == null)
                        return valueMessage;
                    return s + Environment.NewLineConst + valueMessage;
                }
                return s;
            }
        }

        // Gets the value of the argument that caused the exception.
        public virtual object? ActualValue => _actualValue;

        [DoesNotReturn]
        private static void ThrowZero<T>(T value, string? paramName) =>
            throw new ArgumentOutOfRangeException(paramName, value, SR.Format(SR.ArgumentOutOfRange_Generic_MustBeNonZero, paramName, value));

        [DoesNotReturn]
        private static void ThrowNegative<T>(T value, string? paramName) =>
            throw new ArgumentOutOfRangeException(paramName, value, SR.Format(SR.ArgumentOutOfRange_Generic_MustBeNonNegative, paramName, value));

        [DoesNotReturn]
        private static void ThrowNegativeOrZero<T>(T value, string? paramName) =>
            throw new ArgumentOutOfRangeException(paramName, value, SR.Format(SR.ArgumentOutOfRange_Generic_MustBeNonNegativeNonZero, paramName, value));

        [DoesNotReturn]
        private static void ThrowGreater<T>(T value, T other, string? paramName) =>
            throw new ArgumentOutOfRangeException(paramName, value, SR.Format(SR.ArgumentOutOfRange_Generic_MustBeLessOrEqual, paramName, value, other));

        [DoesNotReturn]
        private static void ThrowGreaterEqual<T>(T value, T other, string? paramName) =>
            throw new ArgumentOutOfRangeException(paramName, value, SR.Format(SR.ArgumentOutOfRange_Generic_MustBeLess, paramName, value, other));

        [DoesNotReturn]
        private static void ThrowLess<T>(T value, T other, string? paramName) =>
            throw new ArgumentOutOfRangeException(paramName, value, SR.Format(SR.ArgumentOutOfRange_Generic_MustBeGreaterOrEqual, paramName, value, other));

        [DoesNotReturn]
        private static void ThrowLessEqual<T>(T value, T other, string? paramName) =>
            throw new ArgumentOutOfRangeException(paramName, value, SR.Format(SR.ArgumentOutOfRange_Generic_MustBeGreater, paramName, value, other));

        [DoesNotReturn]
        private static void ThrowEqual<T>(T value, T other, string? paramName) =>
            throw new ArgumentOutOfRangeException(paramName, value, SR.Format(SR.ArgumentOutOfRange_Generic_MustBeNotEqual, paramName, (object?)value ?? "null", (object?)other ?? "null"));

        [DoesNotReturn]
        private static void ThrowNotEqual<T>(T value, T other, string? paramName) =>
            throw new ArgumentOutOfRangeException(paramName, value, SR.Format(SR.ArgumentOutOfRange_Generic_MustBeEqual, paramName, (object?)value ?? "null", (object?)other ?? "null"));

        /// <summary>Throws an <see cref="ArgumentOutOfRangeException"/> if <paramref name="value"/> is zero.</summary>
        /// <param name="value">The argument to validate as non-zero.</param>
        /// <param name="paramName">The name of the parameter with which <paramref name="value"/> corresponds.</param>
        public static void ThrowIfZero<T>(T value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : INumberBase<T>
        {
            if (T.IsZero(value))
                ThrowZero(value, paramName);
        }

        /// <summary>Throws an <see cref="ArgumentOutOfRangeException"/> if <paramref name="value"/> is negative.</summary>
        /// <param name="value">The argument to validate as non-negative.</param>
        /// <param name="paramName">The name of the parameter with which <paramref name="value"/> corresponds.</param>
        public static void ThrowIfNegative<T>(T value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : INumberBase<T>
        {
            if (T.IsNegative(value))
                ThrowNegative(value, paramName);
        }

        /// <summary>Throws an <see cref="ArgumentOutOfRangeException"/> if <paramref name="value"/> is negative or zero.</summary>
        /// <param name="value">The argument to validate as non-zero or non-negative.</param>
        /// <param name="paramName">The name of the parameter with which <paramref name="value"/> corresponds.</param>
        public static void ThrowIfNegativeOrZero<T>(T value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : INumberBase<T>
        {
            if (T.IsNegative(value) || T.IsZero(value))
                ThrowNegativeOrZero(value, paramName);
        }

        /// <summary>Throws an <see cref="ArgumentOutOfRangeException"/> if <paramref name="value"/> is equal to <paramref name="other"/>.</summary>
        /// <param name="value">The argument to validate as not equal to <paramref name="other"/>.</param>
        /// <param name="other">The value to compare with <paramref name="value"/>.</param>
        /// <param name="paramName">The name of the parameter with which <paramref name="value"/> corresponds.</param>
        public static void ThrowIfEqual<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        {
            if (EqualityComparer<T>.Default.Equals(value, other))
                ThrowEqual(value, other, paramName);
        }

        /// <summary>Throws an <see cref="ArgumentOutOfRangeException"/> if <paramref name="value"/> is not equal to <paramref name="other"/>.</summary>
        /// <param name="value">The argument to validate as equal to <paramref name="other"/>.</param>
        /// <param name="other">The value to compare with <paramref name="value"/>.</param>
        /// <param name="paramName">The name of the parameter with which <paramref name="value"/> corresponds.</param>
        public static void ThrowIfNotEqual<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        {
            if (!EqualityComparer<T>.Default.Equals(value, other))
                ThrowNotEqual(value, other, paramName);
        }

        /// <summary>Throws an <see cref="ArgumentOutOfRangeException"/> if <paramref name="value"/> is greater than <paramref name="other"/>.</summary>
        /// <param name="value">The argument to validate as less or equal than <paramref name="other"/>.</param>
        /// <param name="other">The value to compare with <paramref name="value"/>.</param>
        /// <param name="paramName">The name of the parameter with which <paramref name="value"/> corresponds.</param>
        public static void ThrowIfGreaterThan<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : IComparable<T>
        {
            if (value.CompareTo(other) > 0)
                ThrowGreater(value, other, paramName);
        }

        /// <summary>Throws an <see cref="ArgumentOutOfRangeException"/> if <paramref name="value"/> is greater than or equal <paramref name="other"/>.</summary>
        /// <param name="value">The argument to validate as less than <paramref name="other"/>.</param>
        /// <param name="other">The value to compare with <paramref name="value"/>.</param>
        /// <param name="paramName">The name of the parameter with which <paramref name="value"/> corresponds.</param>
        public static void ThrowIfGreaterThanOrEqual<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : IComparable<T>
        {
            if (value.CompareTo(other) >= 0)
                ThrowGreaterEqual(value, other, paramName);
        }

        /// <summary>Throws an <see cref="ArgumentOutOfRangeException"/> if <paramref name="value"/> is less than <paramref name="other"/>.</summary>
        /// <param name="value">The argument to validate as greater than or equal than <paramref name="other"/>.</param>
        /// <param name="other">The value to compare with <paramref name="value"/>.</param>
        /// <param name="paramName">The name of the parameter with which <paramref name="value"/> corresponds.</param>
        public static void ThrowIfLessThan<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : IComparable<T>
        {
            if (value.CompareTo(other) < 0)
                ThrowLess(value, other, paramName);
        }

        /// <summary>Throws an <see cref="ArgumentOutOfRangeException"/> if <paramref name="value"/> is less than or equal <paramref name="other"/>.</summary>
        /// <param name="value">The argument to validate as greater than than <paramref name="other"/>.</param>
        /// <param name="other">The value to compare with <paramref name="value"/>.</param>
        /// <param name="paramName">The name of the parameter with which <paramref name="value"/> corresponds.</param>
        public static void ThrowIfLessThanOrEqual<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : IComparable<T>
        {
            if (value.CompareTo(other) <= 0)
                ThrowLessEqual(value, other, paramName);
        }
    }

    public class IndexOutOfRangeException : SystemException
    {
        public IndexOutOfRangeException() : base("Index was outside the bounds of the array.") { }
        public IndexOutOfRangeException(string message) : base(message) { }
        public IndexOutOfRangeException(string message, Exception inner) : base(message, inner) { }
    }

    public abstract class Attribute
    {

    }

    [System.AttributeUsage(System.AttributeTargets.Class, Inherited = true)]
    public sealed class AttributeUsageAttribute : Attribute
    {
        public AttributeUsageAttribute(AttributeTargets validOn)
        {
            ValidOn = validOn;
            Inherited = true;
        }

        public AttributeTargets ValidOn { get; }

        public bool AllowMultiple { get; set; }

        public bool Inherited { get; set; }
    }
    /// <summary>
    /// Indicates that the value of a static field is unique for each thread.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public class ThreadStaticAttribute : Attribute
    {
        public ThreadStaticAttribute()
        {
        }
    }
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class OverloadResolutionPriorityAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OverloadResolutionPriorityAttribute"/> class.
        /// </summary>
        public OverloadResolutionPriorityAttribute(int priority)
        {
            Priority = priority;
        }

        /// <summary>
        /// The priority of the member.
        /// </summary>
        public int Priority { get; }
    }
    /// <summary>
    /// Indicates that a method will allow a variable number of arguments in its invocation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = true, AllowMultiple = false)]
    public sealed class ParamArrayAttribute : Attribute
    {
        public ParamArrayAttribute() { }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = false)]
    public sealed class CLSCompliantAttribute : Attribute
    {
        private readonly bool _compliant;

        public CLSCompliantAttribute(bool isCompliant)
        {
            _compliant = isCompliant;
        }
        public bool IsCompliant => _compliant;
    }
    [AttributeUsage(AttributeTargets.Enum, Inherited = false)]
    public class FlagsAttribute : Attribute
    {
        public FlagsAttribute() { }
    }
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor,
                    AllowMultiple = false, Inherited = false)]
    public sealed class NonVersionableAttribute : Attribute
    {
        public NonVersionableAttribute() { }
    }
    public class Random
    {
        private int _seed;
        public static Random Shared { get; } = new ThreadSafeRandom();
        public Random() { }
        public Random(int seed) { _seed = seed; }


        public virtual int Next() => Next(0, 0x7fffffff);
        public virtual int Next(int maxValue) => Next(0, maxValue);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public virtual int Next(int minValue, int maxValue)
        {
            if (minValue > maxValue)
            {
                throw new Exception();
            }

            //int result = _impl.Next(minValue, maxValue);
            //AssertInRange(result, minValue, maxValue);
            return -1;
        }
    }
    public class ThreadSafeRandom : Random
    {
        public ThreadSafeRandom() { }
    }

    public static class Console
    {
        private static ReadOnlySpan<char> _trueString => ['T', 'r', 'u', 'e', '\0'];
        private static ReadOnlySpan<char> _trueWithNewLineString => ['T', 'r', 'u', 'e', '\n', '\0'];
        private static ReadOnlySpan<char> _falseString => ['F', 'a', 'l', 's', 'e', '\0'];
        private static ReadOnlySpan<char> _falseWithNewLineString => ['F', 'a', 'l', 's', 'e', '\n', '\0'];
        private static ReadOnlySpan<char> _intMinValueString => ['-', '2', '1', '4', '7', '4', '8', '3', '6', '4', '8', '\0'];
        private static ReadOnlySpan<char> _intMinValueWithNewLineString => ['-', '2', '1', '4', '7', '4', '8', '3', '6', '4', '8', '\n', '\0'];
        private static ReadOnlySpan<char> _longMinValueString =>
            ['-', '9', '2', '2', '3', '3', '7', '2', '0', '3', '6', '8', '5', '4', '7', '7', '5', '8', '0', '8', '\0'];
        public static void Write(sbyte value) { Write((int)value); }
        public static void Write(byte value) { Write((int)value); }
        public static void Write(short value) { Write((int)value); }
        public static void Write(ushort value) { Write((int)value); }
        public static unsafe void Write(int value)
        {
            char* p = stackalloc char[12] + 11;
            *p = '\0';
            if (value == unchecked((int)0x80000000)) //int.MinValue
            {
                //-2147483648
                _Write((char*)Unsafe.AsPointer(in _intMinValueString._reference));
                return;
            }
            bool negative = value < 0;
            if (negative)
                value = -value;

            do
            {
                int digit = value % 10;
                value /= 10;
                *--p = (char)('0' + digit);
            }
            while (value != 0);
            if (negative)
                *--p = '-';

            _Write(p);
        }
        public static void Write(uint value) { Write((long)value); }
        public static unsafe void Write(long value)
        {
            char* p = stackalloc char[21] + 20;
            *p = '\0';

            if (value == unchecked((long)0x8000000000000000)) // long.MinValue
            {
                //-9223372036854775808
                _Write((char*)Unsafe.AsPointer(in _longMinValueString._reference));
                return;
            }
            bool negative = value < 0;
            if (negative)
                value = -value;

            do
            {
                long digit = value % 10;
                value /= 10;
                *--p = (char)('0' + digit);
            }
            while (value != 0);

            if (negative)
                *--p = '-';

            _Write(p);
        }
        public static unsafe void Write(ulong value)
        {
            char* p = stackalloc char[21] + 20; // 20 digits + terminator
            *p = '\0';

            do
            {
                ulong digit = value % 10ul;
                value /= 10ul;
                *--p = (char)('0' + digit);
            } while (value != 0ul);

            _Write(p);
        }
        public static unsafe void Write(float value)
        {
            char* buffer = stackalloc char[System.Number.FloatFormatBufferCharCount + 1];
            int length = System.Number.FormatFloatToBuffer(value, null, null, buffer, System.Number.FloatFormatBufferCharCount);
            buffer[length] = '\0';
            _Write(buffer);
        }
        public static unsafe void Write(double value)
        {
            char* buffer = stackalloc char[System.Number.DoubleFormatBufferCharCount + 1];
            int length = System.Number.FormatDoubleToBuffer(value, null, null, buffer, System.Number.DoubleFormatBufferCharCount);
            buffer[length] = '\0';
            _Write(buffer);
        }
        public static unsafe void Write(char value) { uint terminated = value; _Write((char*)&terminated); }
        public static unsafe void Write(bool value)
        {
            _Write((char*)Unsafe.AsPointer(in (value ? ref _trueString._reference : ref _falseString._reference)));
        }
        public static unsafe void Write(char* value) { _Write(value); }
        public static void Write(ReadOnlySpan<char> value) { _Write(value); }
        public static void Write(string value) { _Write(value); }
        public static void Write(object value) { _Write(value.ToString()); }

        public unsafe static void WriteLine() { uint nl = '\n'; _Write((char*)&nl); }
        public unsafe static void WriteLine(sbyte value) { WriteLine((int)value); }
        public unsafe static void WriteLine(byte value) { WriteLine((int)value); }
        public unsafe static void WriteLine(short value) { WriteLine((int)value); }
        public unsafe static void WriteLine(ushort value) { WriteLine((int)value); }
        public unsafe static void WriteLine(int value)
        {
            char* p = stackalloc char[13] + 11;
            *(p + 1) = '\0';
            *p = '\n';
            if (value == unchecked((int)0x80000000)) //int.MinValue
            {
                //-2147483648
                _Write((char*)Unsafe.AsPointer(in _intMinValueWithNewLineString._reference));
                return;
            }
            bool negative = value < 0;
            if (negative)
                value = -value;

            do
            {
                int digit = value % 10;
                value /= 10;
                *--p = (char)('0' + digit);
            }
            while (value != 0);
            if (negative)
                *--p = '-';

            _Write(p);
        }
        public unsafe static void WriteLine(uint value) { Write(value); uint nl = '\n'; _Write((char*)&nl); }
        public unsafe static void WriteLine(long value) { Write(value); uint nl = '\n'; _Write((char*)&nl); }
        public unsafe static void WriteLine(ulong value) { Write(value); uint nl = '\n'; _Write((char*)&nl); }
        public unsafe static void WriteLine(char value) { ulong s = (ulong)value | ((ulong)'\n' << 16); _Write((char*)&s); }
        public unsafe static void WriteLine(bool value)
        {
            _Write((char*)Unsafe.AsPointer(in (value ? ref _trueWithNewLineString._reference : ref _falseWithNewLineString._reference)));
        }
        public unsafe static void WriteLine(float value) { Write(value); uint nl = '\n'; _Write((char*)&nl); }
        public unsafe static void WriteLine(double value) { Write(value); uint nl = '\n'; _Write((char*)&nl); }
        public unsafe static void WriteLine(string value) { _Write(value); uint nl = '\n'; _Write((char*)&nl); }
        public unsafe static void WriteLine(ReadOnlySpan<char> value) { _Write(value); uint nl = '\n'; _Write((char*)&nl); }
        public unsafe static unsafe void WriteLine(char* value) { _Write(value); uint nl = '\n'; _Write((char*)&nl); }
        public unsafe static void WriteLine(object value)
        {
            if (value != null)
            {
                _Write(value.ToString());
            }
            uint nl = '\n'; _Write((char*)&nl);
        }
        // intrinsics
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static unsafe void _Write(char* value) { }
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static void _Write(string value) { }
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static void _Write(ReadOnlySpan<char> value) { }
    }

    public unsafe class Buffer
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Memmove<T>(ref T dest, ref T src, nuint len)
        {
            if (len == (nuint)0)
                return;

            if (System.Runtime.CompilerServices.Unsafe.AreSame<T>(ref dest, ref src))
                return;

            int n = (int)len;
            if ((nuint)n != len)
                throw new ArgumentOutOfRangeException("len");

            nuint elemSize = (nuint)System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            nuint byteLen = elemSize * len;

            nint byteOffset = System.Runtime.CompilerServices.Unsafe.ByteOffset<T>(ref src, ref dest); // dest - src
            bool copyBackwards = (byteOffset > 0) && ((nuint)byteOffset < byteLen);

            if (!copyBackwards)
            {
                for (int i = 0; i < n; i++)
                {
                    System.Runtime.CompilerServices.Unsafe.Add<T>(ref dest, i) =
                        System.Runtime.CompilerServices.Unsafe.Add<T>(ref src, i);
                }
            }
            else
            {
                for (int i = n - 1; i >= 0; i--)
                {
                    System.Runtime.CompilerServices.Unsafe.Add<T>(ref dest, i) =
                        System.Runtime.CompilerServices.Unsafe.Add<T>(ref src, i);
                }
            }
        }
    }

    [InlineArray(2)]
    internal struct TwoObjects
    {
        private object? _arg0;

        public TwoObjects(object? arg0, object? arg1)
        {
            this[0] = arg0;
            this[1] = arg1;
        }
    }
    [InlineArray(3)]
    internal struct ThreeObjects
    {
        private object? _arg0;

        public ThreeObjects(object? arg0, object? arg1, object? arg2)
        {
            this[0] = arg0;
            this[1] = arg1;
            this[2] = arg2;
        }
    }
    internal static class ThrowHelper
    {
        [DoesNotReturn]
        internal static void ThrowArithmeticException(string message)
        {
            throw new ArithmeticException(message);
        }

        [DoesNotReturn]
        internal static void ThrowArrayTypeMismatchException()
        {
            throw new ArrayTypeMismatchException();
        }

        [DoesNotReturn]
        internal static void ThrowArgumentException_DestinationTooShort()
        {
            throw new ArgumentException(SR.Argument_DestinationTooShort, "destination");
        }

        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRangeException()
        {
            throw new ArgumentOutOfRangeException();
        }

        [DoesNotReturn]
        internal static void ThrowOverflowException()
        {
            throw new OverflowException();
        }

        [DoesNotReturn]
        internal static void ThrowInvalidOperationException()
        {
            throw new InvalidOperationException();
        }

        [DoesNotReturn]
        internal static void ThrowNotSupportedException()
        {
            throw new NotSupportedException();
        }

        [DoesNotReturn]
        internal static void ThrowInvalidOperationException_InvalidUtf8()
        {
            throw new InvalidOperationException(SR.InvalidOperation_InvalidUtf8);
        }

        [DoesNotReturn]
        internal static void ThrowFormatInvalidString()
        {
            throw new FormatException(SR.Format_InvalidString);
        }

        [DoesNotReturn]
        internal static void ThrowFormatInvalidString(int offset, ExceptionResource resource)
        {
            throw new FormatException(SR.Format(SR.Format_InvalidStringWithOffsetAndReason, offset, GetResourceString(resource)));
        }

        [DoesNotReturn]
        internal static void ThrowFormatIndexOutOfRange()
        {
            throw new FormatException(SR.Format_IndexOutOfRange);
        }

        [DoesNotReturn]
        internal static void ThrowFormatException_NeedSingleChar()
        {
            throw new FormatException(SR.Format_NeedSingleChar);
        }

        [DoesNotReturn]
        internal static void ThrowArgumentNullException(ExceptionArgument argument)
        {
            throw new ArgumentNullException(GetArgumentName(argument));
        }

        [DoesNotReturn]
        internal static void ThrowArgumentException(ExceptionResource resource)
        {
            throw GetArgumentException(resource);
        }

        private static ArgumentException GetArgumentException(ExceptionResource resource)
        {
            return new ArgumentException(GetResourceString(resource));
        }

        [DoesNotReturn]
        internal static void ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen()
        {
            throw new InvalidOperationException(SR.InvalidOperation_EnumOpCantHappen);
        }

        private static ArgumentOutOfRangeException GetArgumentOutOfRangeException(ExceptionArgument argument, ExceptionResource resource)
        {
            return new ArgumentOutOfRangeException(GetArgumentName(argument), GetResourceString(resource));
        }

        private static ArgumentOutOfRangeException GetArgumentOutOfRangeException(ExceptionArgument argument, int paramNumber, ExceptionResource resource)
        {
            return new ArgumentOutOfRangeException(GetArgumentName(argument) + "[" + paramNumber.ToString() + "]", GetResourceString(resource));
        }

        [DoesNotReturn]
        internal static void ThrowAccessViolationException()
        {
            throw new AccessViolationException();
        }

        [DoesNotReturn]
        internal static void ThrowArgumentException_InvalidEnumValue<TEnum>(TEnum value, [CallerArgumentExpression(nameof(value))] string argumentName = "")
        {
            throw new ArgumentException(SR.Format(SR.Argument_InvalidEnumValue, value, typeof(TEnum).Name), argumentName);
        }

        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRangeException(ExceptionArgument argument)
        {
            throw new ArgumentOutOfRangeException(GetArgumentName(argument));
        }

        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRangeException(ExceptionArgument argument, ExceptionResource resource)
        {
            throw GetArgumentOutOfRangeException(argument, resource);
        }

        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRangeException(ExceptionArgument argument, int paramNumber, ExceptionResource resource)
        {
            throw GetArgumentOutOfRangeException(argument, paramNumber, resource);
        }

        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRange_IndexMustBeLessOrEqualException()
        {
            throw GetArgumentOutOfRangeException(ExceptionArgument.index,
                                                    ExceptionResource.ArgumentOutOfRange_IndexMustBeLessOrEqual);
        }

        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRange_RoundingDigits_MathF(string name)
        {
            throw new ArgumentOutOfRangeException(name, SR.ArgumentOutOfRange_RoundingDigits_MathF);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ThrowForUnsupportedIntrinsicsVector128BaseType<T>()
        {
            if (!Vector128<T>.IsSupported)
            {
                ThrowNotSupportedException(ExceptionResource.Arg_TypeNotSupported);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ThrowForUnsupportedIntrinsicsVector256BaseType<T>()
        {
            if (!Vector256<T>.IsSupported)
            {
                ThrowNotSupportedException(ExceptionResource.Arg_TypeNotSupported);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ThrowForUnsupportedIntrinsicsVector512BaseType<T>()
        {
            if (!Vector512<T>.IsSupported)
            {
                ThrowNotSupportedException(ExceptionResource.Arg_TypeNotSupported);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ThrowForUnsupportedIntrinsicsVector64BaseType<T>()
        {
            if (!Vector64<T>.IsSupported)
            {
                ThrowNotSupportedException(ExceptionResource.Arg_TypeNotSupported);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ThrowForUnsupportedNumericsVectorBaseType<T>()
        {
            if (!Vector<T>.IsSupported)
            {
                ThrowNotSupportedException(ExceptionResource.Arg_TypeNotSupported);
            }
        }

        [DoesNotReturn]
        internal static void ThrowNotSupportedException(ExceptionResource resource)
        {
            throw new NotSupportedException(GetResourceString(resource));
        }

        [DoesNotReturn]
        internal static void ThrowStartIndexArgumentOutOfRange_ArgumentOutOfRange_IndexMustBeLess()
        {
            throw GetArgumentOutOfRangeException(ExceptionArgument.startIndex,
                                                    ExceptionResource.ArgumentOutOfRange_IndexMustBeLess);
        }

        [DoesNotReturn]
        internal static void ThrowUnreachableException()
        {
            throw new UnreachableException();
        }

        private static string GetResourceString(ExceptionResource resource)
        {
            switch (resource)
            {
                case ExceptionResource.ArgumentOutOfRange_IndexMustBeLessOrEqual:
                    return SR.ArgumentOutOfRange_IndexMustBeLessOrEqual;
                case ExceptionResource.ArgumentOutOfRange_IndexMustBeLess:
                    return SR.ArgumentOutOfRange_IndexMustBeLess;
                case ExceptionResource.Argument_AddingDuplicate:
                    return SR.Argument_AddingDuplicate;
                case ExceptionResource.Arg_TypeNotSupported:
                    return SR.Arg_TypeNotSupported;
                case ExceptionResource.Format_UnexpectedClosingBrace:
                    return SR.Format_UnexpectedClosingBrace;
                case ExceptionResource.Format_UnclosedFormatItem:
                    return SR.Format_UnclosedFormatItem;
                case ExceptionResource.Format_ExpectedAsciiDigit:
                    return SR.Format_ExpectedAsciiDigit;
                default:
                    return "";
            }
        }

        private static string GetArgumentName(ExceptionArgument argument)
        {
            switch (argument)
            {
                case ExceptionArgument.key:
                    return "key";
                case ExceptionArgument.values:
                    return "values";
                case ExceptionArgument.startIndex:
                    return "startIndex";
                case ExceptionArgument.s:
                    return "s";
                case ExceptionArgument.index:
                    return "index";
                default:
                    return "";
            }
        }
    }
    internal enum ExceptionArgument
    {
        key,
        values,
        startIndex,
        s,
        index,
    }
    internal enum ExceptionResource
    {
        ArgumentOutOfRange_IndexMustBeLessOrEqual,
        ArgumentOutOfRange_IndexMustBeLess,
        Argument_AddingDuplicate,
        Arg_TypeNotSupported,
        Format_UnexpectedClosingBrace,
        Format_UnclosedFormatItem,
        Format_ExpectedAsciiDigit,
    }
    internal static partial class SR
    {
        internal const string Arg_ArgumentOutOfRangeException = "Specified argument was out of the range of valid values.";
        internal const string Arg_InvalidHexBinaryStyle = "With the AllowHexSpecifier or AllowBinarySpecifier bit set in the enum bit field, the only other valid bits that can be combined into the enum value must be AllowLeadingWhite and AllowTrailingWhite.";
        internal const string Arg_MustBeByte = "Object must be of type Byte.";
        internal const string Arg_MustBeChar = "Object must be of type Char.";
        internal const string Arg_MustBeDouble = "Object must be of type Double.";
        internal const string Arg_MustBeInt16 = "Object must be of type Int16.";
        internal const string Arg_MustBeInt32 = "Object must be of type Int32.";
        internal const string Arg_MustBeInt64 = "Object must be of type Int64.";
        internal const string Arg_MustBeIntPtr = "Object must be of type IntPtr.";
        internal const string Arg_MustBeSByte = "Object must be of type SByte.";
        internal const string Arg_MustBeSingle = "Object must be of type Single.";
        internal const string Arg_MustBeUInt16 = "Object must be of type UInt16.";
        internal const string Arg_MustBeUInt32 = "Object must be of type UInt32.";
        internal const string Arg_MustBeUInt64 = "Object must be of type UInt64.";
        internal const string Arg_MustBeUIntPtr = "Object must be of type UIntPtr.";
        internal const string Arg_TypeNotSupported = "Specified type is not supported";
        internal const string Arg_UnreachableException = "The program executed an instruction that was thought to be unreachable.";
        internal const string ArgumentNull_Generic = "Value cannot be null.";
        internal const string ArgumentOutOfRange_ActualValue = "Actual value was {0}.";
        internal const string ArgumentOutOfRange_Generic_MustBeEqual = "{0} ('{1}') must be equal to '{2}'.";
        internal const string ArgumentOutOfRange_Generic_MustBeGreater = "{0} ('{1}') must be greater than '{2}'.";
        internal const string ArgumentOutOfRange_Generic_MustBeGreaterOrEqual = "{0} ('{1}') must be greater than or equal to '{2}'.";
        internal const string ArgumentOutOfRange_Generic_MustBeLess = "{0} ('{1}') must be less than '{2}'.";
        internal const string ArgumentOutOfRange_Generic_MustBeLessOrEqual = "{0} ('{1}') must be less than or equal to '{2}'.";
        internal const string ArgumentOutOfRange_Generic_MustBeNonNegative = "{0} ('{1}') must be a non-negative value.";
        internal const string ArgumentOutOfRange_Generic_MustBeNonNegativeNonZero = "{0} ('{1}') must be a non-negative and non-zero value.";
        internal const string ArgumentOutOfRange_Generic_MustBeNonZero = "{0} ('{1}') must be a non-zero value.";
        internal const string ArgumentOutOfRange_Generic_MustBeNotEqual = "{0} ('{1}') must not be equal to '{2}'.";
        internal const string ArgumentOutOfRange_GetByteCountOverflow = "Too many characters. The resulting number of bytes is larger than what can be returned as an int.";
        internal const string ArgumentOutOfRange_GetCharCountOverflow = "Too many bytes. The resulting number of chars is larger than what can be returned as an int.";
        internal const string ArgumentOutOfRange_IndexMustBeLess = "Index was out of range. Must be non-negative and less than the size of the collection.";
        internal const string ArgumentOutOfRange_IndexMustBeLessOrEqual = "Index was out of range. Must be non-negative and less than or equal to the size of the collection.";
        internal const string ArgumentOutOfRange_NeedNonNegNum = "Non-negative number required.";
        internal const string ArgumentOutOfRange_RoundingDigits = "Rounding digits must be between 0 and 15, inclusive.";
        internal const string ArgumentOutOfRange_RoundingDigits_MathF = "Rounding digits must be between 0 and 6, inclusive.";
        internal const string Argument_AddingDuplicate = "An item with the same key has already been added.";
        internal const string Argument_DestinationTooShort = "Destination is too short.";
        internal const string Argument_InvalidCharSequenceNoIndex = "String contains invalid Unicode code points.";
        internal const string Argument_InvalidEnumValue = "The value '{0}' is not valid for this usage of the type {1}.";
        internal const string Argument_InvalidNumberStyles = "An undefined NumberStyles value is being used.";
        internal const string Arithmetic_NaN = "Function does not accept floating point Not-a-Number values.";
        internal const string Format_ExpectedAsciiDigit = "Expected an ASCII digit.";
        internal const string Format_IndexOutOfRange = "Index (zero based) must be greater than or equal to zero and less than the size of the argument list.";
        internal const string Format_InvalidString = "Input string was not in a correct format.";
        internal const string Format_InvalidStringWithOffsetAndReason = "Input string was not in a correct format. Failure to parse near offset {0}. {1}";
        internal const string Format_NeedSingleChar = "String must be exactly one character long.";
        internal const string Format_UnclosedFormatItem = "Format item ends prematurely.";
        internal const string Format_UnexpectedClosingBrace = "Unexpected closing brace without a corresponding opening brace.";
        internal const string InvalidOperation_CollectionCorrupted = "A prior operation on this collection was interrupted by an exception. Collection's state is no longer trusted.";
        internal const string InvalidOperation_EnumOpCantHappen = "Enumeration has either not started or has already finished.";
        internal const string InvalidOperation_InvalidUtf8 = "Formatted string contains characters not representable as valid UTF-8.";
        internal const string InvalidOperation_ReadOnly = "Instance is read-only.";
        internal const string NotSupported_Type = "Type is not supported.";
    }
    internal static partial class SR
    {
        internal static string Format(string resourceFormat, object? p1)
        {
            return string.Format(resourceFormat, p1);
        }

        internal static string Format(string resourceFormat, object? p1, object? p2)
        {
            return string.Format(resourceFormat, p1, p2);
        }

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3)
        {
            return string.Format(resourceFormat, p1, p2, p3);
        }

        internal static string Format(string resourceFormat, params object?[]? args)
        {
            if (args != null)
            {
                return string.Format(resourceFormat, args);
            }

            return resourceFormat;
        }

        internal static string Format(IFormatProvider? provider, string resourceFormat, object? p1)
        {
            return string.Format(provider, resourceFormat, p1);
        }

        internal static string Format(IFormatProvider? provider, string resourceFormat, object? p1, object? p2)
        {
            return string.Format(provider, resourceFormat, p1, p2);
        }

        internal static string Format(IFormatProvider? provider, string resourceFormat, object? p1, object? p2, object? p3)
        {
            return string.Format(provider, resourceFormat, p1, p2, p3);
        }

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args)
        {
            if (args != null)
            {
                return string.Format(provider, resourceFormat, args);
            }

            return resourceFormat;
        }
    }
}
