using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.Wasm;
using System.Text;

namespace System.Runtime.Intrinsics.X86
{
    [CLSCompliant(false)]
    public abstract class Lzcnt : X86Base
    {
        public new abstract class X64 : X86Base.X64
        {
            /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
            /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
            /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
            public static new bool IsSupported { [Intrinsic] get { return false; } }
            internal X64() { }
            /// <summary>
            ///   <para>unsigned __int64 _lzcnt_u64 (unsigned __int64 a)</para>
            ///   <para>  LZCNT r64, r/m64</para>
            ///   <para>This intrinsic is only available on 64-bit processes</para>
            /// </summary>
            public static ulong LeadingZeroCount(ulong value) { throw new PlatformNotSupportedException(); }
        }
        /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
        /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
        /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
        public static new bool IsSupported { [Intrinsic] get { return false; } }
        internal Lzcnt() { }
        /// <summary>
        ///   <para>unsigned int _lzcnt_u32 (unsigned int a)</para>
        ///   <para>  LZCNT r32, r/m32</para>
        /// </summary>
        public static uint LeadingZeroCount(uint value) { throw new PlatformNotSupportedException(); }
    }
    [CLSCompliant(false)]
    public abstract class Fma : Avx
    {
        public static new bool IsSupported { [Intrinsic] get { return false; } }
        internal Fma() { }
        /// <summary>
        ///   <para>__m128 _mm_fmadd_ps (__m128 a, __m128 b, __m128 c)</para>
        ///   <para>  VFMADDPS xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VFMADDPS xmm1 {k1}{z}, xmm2, xmm3/m128/m32bcst</para>
        /// </summary>
        public static Vector128<float> MultiplyAdd(Vector128<float> a, Vector128<float> b, Vector128<float> c) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128d _mm_fmadd_pd (__m128d a, __m128d b, __m128d c)</para>
        ///   <para>  VFMADDPD xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VFMADDPD xmm1 {k1}{z}, xmm2, xmm3/m128/m64bcst</para>
        /// </summary>
        public static Vector128<double> MultiplyAdd(Vector128<double> a, Vector128<double> b, Vector128<double> c) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256 _mm256_fmadd_ps (__m256 a, __m256 b, __m256 c)</para>
        ///   <para>  VFMADDPS ymm1,         ymm2, ymm3/m256</para>
        ///   <para>  VFMADDPS ymm1 {k1}{z}, ymm2, ymm3/m256/m32bcst</para>
        /// </summary>
        public static Vector256<float> MultiplyAdd(Vector256<float> a, Vector256<float> b, Vector256<float> c) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256d _mm256_fmadd_pd (__m256d a, __m256d b, __m256d c)</para>
        ///   <para>  VFMADDPD ymm1,         ymm2, ymm3/m256</para>
        ///   <para>  VFMADDPD ymm1 {k1}{z}, ymm2, ymm3/m256/m64bcst</para>
        /// </summary>
        public static Vector256<double> MultiplyAdd(Vector256<double> a, Vector256<double> b, Vector256<double> c) { throw new PlatformNotSupportedException(); }
    }
    [CLSCompliant(false)]
    public abstract class Bmi1 : X86Base
    {
        public new abstract class X64 : X86Base.X64
        {
            /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
            /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
            /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
            public static new bool IsSupported { [Intrinsic] get { return false; } }
            internal X64() { }
            /// <summary>
            ///   <para>__int64 _mm_tzcnt_64 (unsigned __int64 a)</para>
            ///   <para>  TZCNT r64, r/m64</para>
            ///   <para>This intrinsic is only available on 64-bit processes</para>
            /// </summary>
            public static ulong TrailingZeroCount(ulong value) { throw new PlatformNotSupportedException(); }
        }
        /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
        /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
        /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
        public static new bool IsSupported { [Intrinsic] get { return false; } }
        internal Bmi1() { }
        /// <summary>
        ///   <para>int _mm_tzcnt_32 (unsigned int a)</para>
        ///   <para>  TZCNT r32, r/m32</para>
        /// </summary>
        public static uint TrailingZeroCount(uint value) { throw new PlatformNotSupportedException(); }
    }
    [CLSCompliant(false)]
    public abstract class Avx512BW : Avx512F
    {
        /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
        /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
        /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
        public static new bool IsSupported { [Intrinsic] get { return false; } }
        internal Avx512BW() { }
        /// <summary>
        ///   <para>__m512i _mm512_packus_epi16 (__m512i a, __m512i b)</para>
        ///   <para>  VPACKUSWB zmm1 {k1}{z}, zmm2, zmm3/m512</para>
        /// </summary>
        public static Vector512<byte> PackUnsignedSaturate(Vector512<short> left, Vector512<short> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m512i _mm512_packus_epi32 (__m512i a, __m512i b)</para>
        ///   <para>  VPACKUSDW zmm1 {k1}{z}, zmm2, zmm3/m512/m32bcst</para>
        /// </summary>
        public static Vector512<ushort> PackUnsignedSaturate(Vector512<int> left, Vector512<int> right) { throw new PlatformNotSupportedException(); }
    }
    [CLSCompliant(false)]
    public abstract class Avx512F : Avx2
    {
        /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
        /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
        /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
        public static new bool IsSupported { [Intrinsic] get { return false; } }
        internal Avx512F() { }
        /// <summary>
        ///   <para>__m256 _mm512_cvtpd_ps (__m512d a)</para>
        ///   <para>  VCVTPD2PS ymm1,         zmm2/m512</para>
        ///   <para>  VCVTPD2PS ymm1 {k1}{z}, zmm2/m512/m64bcst{er}</para>
        /// </summary>
        public static Vector256<float> ConvertToVector256Single(Vector512<double> value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256 _mm512_cvt_roundpd_ps (__m512d a, int rounding)</para>
        ///   <para>  VCVTPD2PS ymm1, zmm2 {er}</para>
        /// </summary>
        public static Vector256<float> ConvertToVector256Single(Vector512<double> value, [ConstantExpected(Max = FloatRoundingMode.ToZero)] FloatRoundingMode mode) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m512d _mm512_cvtepi32_pd (__m256i a)</para>
        ///   <para>  VCVTDQ2PD zmm1 {k1}{z}, ymm2/m256/m32bcst</para>
        /// </summary>
        public static Vector512<double> ConvertToVector512Double(Vector256<int> value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m512d _mm512_cvtps_pd (__m256 a)</para>
        ///   <para>  VCVTPS2PD zmm1 {k1}{z}, ymm2/m256/m32bcst{sae}</para>
        /// </summary>
        public static Vector512<double> ConvertToVector512Double(Vector256<float> value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m512d _mm512_cvtepu32_pd (__m256i a)</para>
        ///   <para>  VCVTUDQ2PD zmm1 {k1}{z}, ymm2/m256/m32bcst</para>
        /// </summary>
        public static Vector512<double> ConvertToVector512Double(Vector256<uint> value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m512i _mm512_permutexvar_epi64 (__m512i idx, __m512i a)</para>
        ///   <para>  VPERMQ zmm1 {k1}{z}, zmm2, zmm3/m512/m64bcst</para>
        /// </summary>
        /// <remarks>The native and managed intrinsics have different order of parameters.</remarks>
        public static Vector512<long> PermuteVar8x64(Vector512<long> value, Vector512<long> control) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m512i _mm512_permutexvar_epi64 (__m512i idx, __m512i a)</para>
        ///   <para>  VPERMQ zmm1 {k1}{z}, zmm2, zmm3/m512/m64bcst</para>
        /// </summary>
        /// <remarks>The native and managed intrinsics have different order of parameters.</remarks>
        public static Vector512<ulong> PermuteVar8x64(Vector512<ulong> value, Vector512<ulong> control) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m512d _mm512_permutexvar_pd (__m512i idx, __m512d a)</para>
        ///   <para>  VPERMPD zmm1 {k1}{z}, zmm2, zmm3/m512/m64bcst</para>
        /// </summary>
        /// <remarks>The native and managed intrinsics have different order of parameters.</remarks>
        public static Vector512<double> PermuteVar8x64(Vector512<double> value, Vector512<long> control) { throw new PlatformNotSupportedException(); }
    }
    [CLSCompliant(false)]
    public abstract class Avx2 : Avx
    {
        /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
        /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
        /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
        public static new bool IsSupported { [Intrinsic] get { return false; } }
        internal Avx2() { }
        /// <summary>
        ///   <para>__m256i _mm256_and_si256 (__m256i a, __m256i b)</para>
        ///   <para>  VPAND ymm1, ymm2, ymm3/m256</para>
        /// </summary>
        public static Vector256<sbyte> And(Vector256<sbyte> left, Vector256<sbyte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_and_si256 (__m256i a, __m256i b)</para>
        ///   <para>  VPAND ymm1, ymm2, ymm3/m256</para>
        /// </summary>
        public static Vector256<byte> And(Vector256<byte> left, Vector256<byte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_and_si256 (__m256i a, __m256i b)</para>
        ///   <para>  VPAND ymm1, ymm2, ymm3/m256</para>
        /// </summary>
        public static Vector256<short> And(Vector256<short> left, Vector256<short> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_and_si256 (__m256i a, __m256i b)</para>
        ///   <para>  VPAND ymm1, ymm2, ymm3/m256</para>
        /// </summary>
        public static Vector256<ushort> And(Vector256<ushort> left, Vector256<ushort> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_and_si256 (__m256i a, __m256i b)</para>
        ///   <para>  VPAND  ymm1,         ymm2, ymm3/m256</para>
        ///   <para>  VPANDD ymm1 {k1}{z}, ymm2, ymm3/m256/m32bcst</para>
        /// </summary>
        public static Vector256<int> And(Vector256<int> left, Vector256<int> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_and_si256 (__m256i a, __m256i b)</para>
        ///   <para>  VPAND  ymm1,         ymm2, ymm3/m256</para>
        ///   <para>  VPANDD ymm1 {k1}{z}, ymm2, ymm3/m256/m32bcst</para>
        /// </summary>
        public static Vector256<uint> And(Vector256<uint> left, Vector256<uint> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_and_si256 (__m256i a, __m256i b)</para>
        ///   <para>  VPAND  ymm1,         ymm2, ymm3/m256</para>
        ///   <para>  VPANDQ ymm1 {k1}{z}, ymm2, ymm3/m256/m64bcst</para>
        /// </summary>
        public static Vector256<long> And(Vector256<long> left, Vector256<long> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_and_si256 (__m256i a, __m256i b)</para>
        ///   <para>  VPAND  ymm1,         ymm2, ymm3/m256</para>
        ///   <para>  VPANDQ ymm1 {k1}{z}, ymm2, ymm3/m256/m64bcst</para>
        /// </summary>
        public static Vector256<ulong> And(Vector256<ulong> left, Vector256<ulong> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_blend_epi32 (__m128i a, __m128i b, const int imm8)</para>
        ///   <para>  VPBLENDD xmm1, xmm2, xmm3/m128, imm8</para>
        /// </summary>
        public static Vector128<int> Blend(Vector128<int> left, Vector128<int> right, [ConstantExpected] byte control) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_blend_epi32 (__m128i a, __m128i b, const int imm8)</para>
        ///   <para>  VPBLENDD xmm1, xmm2, xmm3/m128, imm8</para>
        /// </summary>
        public static Vector128<uint> Blend(Vector128<uint> left, Vector128<uint> right, [ConstantExpected] byte control) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_blend_epi16 (__m256i a, __m256i b, const int imm8)</para>
        ///   <para>  VPBLENDW ymm1, ymm2, ymm3/m256 imm8</para>
        /// </summary>
        public static Vector256<short> Blend(Vector256<short> left, Vector256<short> right, [ConstantExpected] byte control) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_blend_epi16 (__m256i a, __m256i b, const int imm8)</para>
        ///   <para>  VPBLENDW ymm1, ymm2, ymm3/m256 imm8</para>
        /// </summary>
        public static Vector256<ushort> Blend(Vector256<ushort> left, Vector256<ushort> right, [ConstantExpected] byte control) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_blend_epi32 (__m256i a, __m256i b, const int imm8)</para>
        ///   <para>  VPBLENDD ymm1, ymm2, ymm3/m256, imm8</para>
        /// </summary>
        public static Vector256<int> Blend(Vector256<int> left, Vector256<int> right, [ConstantExpected] byte control) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_blend_epi32 (__m256i a, __m256i b, const int imm8)</para>
        ///   <para>  VPBLENDD ymm1, ymm2, ymm3/m256, imm8</para>
        /// </summary>
        public static Vector256<uint> Blend(Vector256<uint> left, Vector256<uint> right, [ConstantExpected] byte control) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_packus_epi16 (__m256i a, __m256i b)</para>
        ///   <para>  VPACKUSWB ymm1,         ymm2, ymm3/m256</para>
        ///   <para>  VPACKUSWB ymm1 {k1}{z}, ymm2, ymm3/m256</para>
        /// </summary>
        public static Vector256<byte> PackUnsignedSaturate(Vector256<short> left, Vector256<short> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_packus_epi32 (__m256i a, __m256i b)</para>
        ///   <para>  VPACKUSDW ymm1,         ymm2, ymm3/m256</para>
        ///   <para>  VPACKUSDW ymm1 {k1}{z}, ymm2, ymm3/m256/m32bcst</para>
        /// </summary>
        public static Vector256<ushort> PackUnsignedSaturate(Vector256<int> left, Vector256<int> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_permute4x64_epi64 (__m256i a, const int imm8)</para>
        ///   <para>  VPERMQ ymm1,         ymm2/m256,         imm8</para>
        ///   <para>  VPERMQ ymm1 {k1}{z}, ymm2/m256/m64bcst, imm8</para>
        /// </summary>
        public static Vector256<long> Permute4x64(Vector256<long> value, [ConstantExpected] byte control) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_permute4x64_epi64 (__m256i a, const int imm8)</para>
        ///   <para>  VPERMQ ymm1,         ymm2/m256,         imm8</para>
        ///   <para>  VPERMQ ymm1 {k1}{z}, ymm2/m256/m64bcst, imm8</para>
        /// </summary>
        public static Vector256<ulong> Permute4x64(Vector256<ulong> value, [ConstantExpected] byte control) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256d _mm256_permute4x64_pd (__m256d a, const int imm8)</para>
        ///   <para>  VPERMPD ymm1,         ymm2/m256,         imm8</para>
        ///   <para>  VPERMPD ymm1 {k1}{z}, ymm2/m256/m64bcst, imm8</para>
        /// </summary>
        public static Vector256<double> Permute4x64(Vector256<double> value, [ConstantExpected] byte control) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_srl_epi16 (__m256i a, __m128i count)</para>
        ///   <para>  VPSRLW ymm1,         ymm2, xmm3/m128</para>
        ///   <para>  VPSRLW ymm1 {k1}{z}, ymm2, xmm3/m128</para>
        /// </summary>
        public static Vector256<short> ShiftRightLogical(Vector256<short> value, Vector128<short> count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_srl_epi16 (__m256i a, __m128i count)</para>
        ///   <para>  VPSRLW ymm1,         ymm2, xmm3/m128</para>
        ///   <para>  VPSRLW ymm1 {k1}{z}, ymm2, xmm3/m128</para>
        /// </summary>
        public static Vector256<ushort> ShiftRightLogical(Vector256<ushort> value, Vector128<ushort> count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_srl_epi32 (__m256i a, __m128i count)</para>
        ///   <para>  VPSRLD ymm1,         ymm2, xmm3/m128</para>
        ///   <para>  VPSRLD ymm1 {k1}{z}, ymm2, xmm3/m128</para>
        /// </summary>
        public static Vector256<int> ShiftRightLogical(Vector256<int> value, Vector128<int> count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_srl_epi32 (__m256i a, __m128i count)</para>
        ///   <para>  VPSRLD ymm1,         ymm2, xmm3/m128</para>
        ///   <para>  VPSRLD ymm1 {k1}{z}, ymm2, xmm3/m128</para>
        /// </summary>
        public static Vector256<uint> ShiftRightLogical(Vector256<uint> value, Vector128<uint> count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_srl_epi64 (__m256i a, __m128i count)</para>
        ///   <para>  VPSRLQ ymm1,         ymm2, xmm3/m128</para>
        ///   <para>  VPSRLQ ymm1 {k1}{z}, ymm2, xmm3/m128</para>
        /// </summary>
        public static Vector256<long> ShiftRightLogical(Vector256<long> value, Vector128<long> count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_srl_epi64 (__m256i a, __m128i count)</para>
        ///   <para>  VPSRLQ ymm1,         ymm2, xmm3/m128</para>
        ///   <para>  VPSRLQ ymm1 {k1}{z}, ymm2, xmm3/m128</para>
        /// </summary>
        public static Vector256<ulong> ShiftRightLogical(Vector256<ulong> value, Vector128<ulong> count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_srli_epi16 (__m256i a, int imm8)</para>
        ///   <para>  VPSRLW ymm1,         ymm2, imm8</para>
        ///   <para>  VPSRLW ymm1 {k1}{z}, ymm2, imm8</para>
        /// </summary>
        public static Vector256<short> ShiftRightLogical(Vector256<short> value, [ConstantExpected] byte count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_srli_epi16 (__m256i a, int imm8)</para>
        ///   <para>  VPSRLW ymm1,         ymm2, imm8</para>
        ///   <para>  VPSRLW ymm1 {k1}{z}, ymm2, imm8</para>
        /// </summary>
        public static Vector256<ushort> ShiftRightLogical(Vector256<ushort> value, [ConstantExpected] byte count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_srli_epi32 (__m256i a, int imm8)</para>
        ///   <para>  VPSRLD ymm1,         ymm2, imm8</para>
        ///   <para>  VPSRLD ymm1 {k1}{z}, ymm2, imm8</para>
        /// </summary>
        public static Vector256<int> ShiftRightLogical(Vector256<int> value, [ConstantExpected] byte count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_srli_epi32 (__m256i a, int imm8)</para>
        ///   <para>  VPSRLD ymm1,         ymm2, imm8</para>
        ///   <para>  VPSRLD ymm1 {k1}{z}, ymm2, imm8</para>
        /// </summary>
        public static Vector256<uint> ShiftRightLogical(Vector256<uint> value, [ConstantExpected] byte count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_srli_epi64 (__m256i a, int imm8)</para>
        ///   <para>  VPSRLQ ymm1,         ymm2, imm8</para>
        ///   <para>  VPSRLQ ymm1 {k1}{z}, ymm2, imm8</para>
        /// </summary>
        public static Vector256<long> ShiftRightLogical(Vector256<long> value, [ConstantExpected] byte count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_srli_epi64 (__m256i a, int imm8)</para>
        ///   <para>  VPSRLQ ymm1,         ymm2, imm8</para>
        ///   <para>  VPSRLQ ymm1 {k1}{z}, ymm2, imm8</para>
        /// </summary>
        public static Vector256<ulong> ShiftRightLogical(Vector256<ulong> value, [ConstantExpected] byte count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_xor_si256 (__m256i a, __m256i b)</para>
        ///   <para>  VPXOR ymm1, ymm2, ymm3/m256</para>
        /// </summary>
        public static Vector256<sbyte> Xor(Vector256<sbyte> left, Vector256<sbyte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_xor_si256 (__m256i a, __m256i b)</para>
        ///   <para>  VPXOR ymm1, ymm2, ymm3/m256</para>
        /// </summary>
        public static Vector256<byte> Xor(Vector256<byte> left, Vector256<byte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_xor_si256 (__m256i a, __m256i b)</para>
        ///   <para>  VPXOR ymm1, ymm2, ymm3/m256</para>
        /// </summary>
        public static Vector256<short> Xor(Vector256<short> left, Vector256<short> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_xor_si256 (__m256i a, __m256i b)</para>
        ///   <para>  VPXOR ymm1, ymm2, ymm3/m256</para>
        /// </summary>
        public static Vector256<ushort> Xor(Vector256<ushort> left, Vector256<ushort> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_xor_si256 (__m256i a, __m256i b)</para>
        ///   <para>  VPXOR  ymm1,         ymm2, ymm3/m256</para>
        ///   <para>  VPXORD ymm1 {k1}{z}, ymm2, ymm3/m256/m32bcst</para>
        /// </summary>
        public static Vector256<int> Xor(Vector256<int> left, Vector256<int> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_xor_si256 (__m256i a, __m256i b)</para>
        ///   <para>  VPXOR  ymm1,         ymm2, ymm3/m256</para>
        ///   <para>  VPXORD ymm1 {k1}{z}, ymm2, ymm3/m256/m32bcst</para>
        /// </summary>
        public static Vector256<uint> Xor(Vector256<uint> left, Vector256<uint> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_xor_si256 (__m256i a, __m256i b)</para>
        ///   <para>  VPXOR  ymm1,         ymm2, ymm3/m256</para>
        ///   <para>  VPXORQ ymm1 {k1}{z}, ymm2, ymm3/m256/m64bcst</para>
        /// </summary>
        public static Vector256<long> Xor(Vector256<long> left, Vector256<long> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256i _mm256_xor_si256 (__m256i a, __m256i b)</para>
        ///   <para>  VPXOR  ymm1,         ymm2, ymm3/m256</para>
        ///   <para>  VPXORQ ymm1 {k1}{z}, ymm2, ymm3/m256/m64bcst</para>
        /// </summary>
        public static Vector256<ulong> Xor(Vector256<ulong> left, Vector256<ulong> right) { throw new PlatformNotSupportedException(); }
    }
    [CLSCompliant(false)]
    public abstract class Avx : Sse42
    {
        /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
        /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
        /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
        public static new bool IsSupported { [Intrinsic] get { return false; } }
        internal Avx() { }
        /// <summary>
        ///   <para>__m256 _mm256_add_ps (__m256 a, __m256 b)</para>
        ///   <para>  VADDPS ymm1,         ymm2, ymm3/m256</para>
        ///   <para>  VADDPS ymm1 {k1}{z}, ymm2, ymm3/m256/m32bcst</para>
        /// </summary>
        public static Vector256<float> Add(Vector256<float> left, Vector256<float> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256d _mm256_add_pd (__m256d a, __m256d b)</para>
        ///   <para>  VADDPD ymm1,         ymm2, ymm3/m256</para>
        ///   <para>  VADDPD ymm1 {k1}{z}, ymm2, ymm3/m256/m64bcst</para>
        /// </summary>
        public static Vector256<double> Add(Vector256<double> left, Vector256<double> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128 _mm256_cvtpd_ps (__m256d a)</para>
        ///   <para>  VCVTPD2PS xmm1,         ymm2/m256</para>
        ///   <para>  VCVTPD2PS xmm1 {k1}{z}, ymm2/m256/m64bcst</para>
        /// </summary>
        public static Vector128<float> ConvertToVector128Single(Vector256<double> value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256d _mm256_cvtepi32_pd (__m128i a)</para>
        ///   <para>  VCVTDQ2PD ymm1,         xmm2/m128</para>
        ///   <para>  VCVTDQ2PD ymm1 {k1}{z}, xmm2/m128/m32bcst</para>
        /// </summary>
        public static Vector256<double> ConvertToVector256Double(Vector128<int> value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256d _mm256_cvtps_pd (__m128 a)</para>
        ///   <para>  VCVTPS2PD ymm1,         xmm2/m128</para>
        ///   <para>  VCVTPS2PD ymm1 {k1}{z}, xmm2/m128/m32bcst</para>
        /// </summary>
        public static Vector256<double> ConvertToVector256Double(Vector128<float> value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256 _mm256_cvtepi32_ps (__m256i a)</para>
        ///   <para>  VCVTDQ2PS ymm1,         ymm2/m256</para>
        ///   <para>  VCVTDQ2PS ymm1 {k1}{z}, ymm2/m256/m32bcst</para>
        /// </summary>
        public static Vector256<float> ConvertToVector256Single(Vector256<int> value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256 _mm256_mul_ps (__m256 a, __m256 b)</para>
        ///   <para>  VMULPS ymm1,         ymm2, ymm3/m256</para>
        ///   <para>  VMULPS ymm1 {k1}{z}, ymm2, ymm3/m256/m32bcst</para>
        /// </summary>
        public static Vector256<float> Multiply(Vector256<float> left, Vector256<float> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256d _mm256_mul_pd (__m256d a, __m256d b)</para>
        ///   <para>  VMULPD ymm1,         ymm2, ymm3/m256</para>
        ///   <para>  VMULPD ymm1 {k1}{z}, ymm2, ymm3/m256/m64bcst</para>
        /// </summary>
        public static Vector256<double> Multiply(Vector256<double> left, Vector256<double> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256 _mm256_sub_ps (__m256 a, __m256 b)</para>
        ///   <para>  VSUBPS ymm1,         ymm2, ymm3/m256</para>
        ///   <para>  VSUBPS ymm1 {k1}{z}, ymm2, ymm3/m256/m32bcst</para>
        /// </summary>
        public static Vector256<float> Subtract(Vector256<float> left, Vector256<float> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m256d _mm256_sub_pd (__m256d a, __m256d b)</para>
        ///   <para>  VSUBPD ymm1,         ymm2, ymm3/m256</para>
        ///   <para>  VSUBPD ymm1 {k1}{z}, ymm2, ymm3/m256/m64bcst</para>
        /// </summary>
        public static Vector256<double> Subtract(Vector256<double> left, Vector256<double> right) { throw new PlatformNotSupportedException(); }
    }
    [CLSCompliant(false)]
    public abstract class Sse42 : Sse41
    {
        /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
        /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
        /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
        public static new bool IsSupported { [Intrinsic] get { return false; } }
        internal Sse42() { }
    }
    [CLSCompliant(false)]
    public abstract class Sse41 : Ssse3
    {
        public new abstract class X64 : Ssse3.X64
        {
            /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
            /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
            /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
            public static new bool IsSupported { [Intrinsic] get { return false; } }
            internal X64() { }
        }
        /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
        /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
        /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
        public static new bool IsSupported { [Intrinsic] get { return false; } }
        internal Sse41() { }
    }
    [CLSCompliant(false)]
    public abstract class Ssse3 : Sse3
    {
        public new abstract class X64 : Sse3.X64
        {
            /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
            /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
            /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
            public static new bool IsSupported { [Intrinsic] get { return false; } }
            internal X64() { }
        }
        /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
        /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
        /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
        public static new bool IsSupported { [Intrinsic] get { return false; } }
        internal Ssse3() { }
    }
    [CLSCompliant(false)]
    public abstract class Sse3 : Sse2
    {
        public new abstract class X64 : Sse2.X64
        {
            /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
            /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
            /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
            public static new bool IsSupported { [Intrinsic] get { return false; } }
            internal X64() { }
        }
        /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
        /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
        /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
        public static new bool IsSupported { [Intrinsic] get { return false; } }
        internal Sse3() { }
    }
    [CLSCompliant(false)]
    public abstract class Sse2 : Sse
    {
        public new abstract class X64 : Sse.X64
        {
            /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
            /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
            /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
            public static new bool IsSupported { [Intrinsic] get { return false; } }
            internal X64() { }
            /// <summary>
            ///   <para>__m128i _mm_cvtsi64_si128 (__int64 a)</para>
            ///   <para>   MOVQ xmm1, r/m64</para>
            ///   <para>  VMOVQ xmm1, r/m64</para>
            ///   <para>This intrinsic is only available on 64-bit processes</para>
            /// </summary>
            public static Vector128<ulong> ConvertScalarToVector128UInt64(ulong value) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>__int64 _mm_cvtsi128_si64 (__m128i a)</para>
            ///   <para>   MOVQ r/m64, xmm1</para>
            ///   <para>  VMOVQ r/m64, xmm1</para>
            ///   <para>This intrinsic is only available on 64-bit processes</para>
            /// </summary>
            public static ulong ConvertToUInt64(Vector128<ulong> value) { throw new PlatformNotSupportedException(); }
        }
        /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
        /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
        /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
        public static new bool IsSupported { [Intrinsic] get { return false; } }
        internal Sse2() { }
        /// <summary>
        ///   <para>__m128i _mm_add_epi8 (__m128i a,  __m128i b)</para>
        ///   <para>   PADDB xmm1,               xmm2/m128</para>
        ///   <para>  VPADDB xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPADDB xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<byte> Add(Vector128<byte> left, Vector128<byte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_add_epi8 (__m128i a,  __m128i b)</para>
        ///   <para>   PADDB xmm1,               xmm2/m128</para>
        ///   <para>  VPADDB xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPADDB xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<sbyte> Add(Vector128<sbyte> left, Vector128<sbyte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_add_epi16 (__m128i a,  __m128i b)</para>
        ///   <para>   PADDW xmm1,               xmm2/m128</para>
        ///   <para>  VPADDW xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPADDW xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<short> Add(Vector128<short> left, Vector128<short> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_add_epi16 (__m128i a,  __m128i b)</para>
        ///   <para>   PADDW xmm1,               xmm2/m128</para>
        ///   <para>  VPADDW xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPADDW xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<ushort> Add(Vector128<ushort> left, Vector128<ushort> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_add_epi32 (__m128i a,  __m128i b)</para>
        ///   <para>   PADDD xmm1,               xmm2/m128</para>
        ///   <para>  VPADDD xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPADDD xmm1 {k1}{z}, xmm2, xmm3/m128/m32bcst</para>
        /// </summary>
        public static Vector128<int> Add(Vector128<int> left, Vector128<int> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_add_epi32 (__m128i a,  __m128i b)</para>
        ///   <para>   PADDD xmm1,               xmm2/m128</para>
        ///   <para>  VPADDD xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPADDD xmm1 {k1}{z}, xmm2, xmm3/m128/m32bcst</para>
        /// </summary>
        public static Vector128<uint> Add(Vector128<uint> left, Vector128<uint> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_add_epi64 (__m128i a,  __m128i b)</para>
        ///   <para>   PADDQ xmm1,               xmm2/m128</para>
        ///   <para>  VPADDQ xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPADDQ xmm1 {k1}{z}, xmm2, xmm3/m128/m64bcst</para>
        /// </summary>
        public static Vector128<long> Add(Vector128<long> left, Vector128<long> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_add_epi64 (__m128i a,  __m128i b)</para>
        ///   <para>   PADDQ xmm1,               xmm2/m128</para>
        ///   <para>  VPADDQ xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPADDQ xmm1 {k1}{z}, xmm2, xmm3/m128/m64bcst</para>
        /// </summary>
        public static Vector128<ulong> Add(Vector128<ulong> left, Vector128<ulong> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128d _mm_add_pd (__m128d a,  __m128d b)</para>
        ///   <para>   ADDPD xmm1,               xmm2/m128</para>
        ///   <para>  VADDPD xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VADDPD xmm1 {k1}{z}, xmm2, xmm3/m128/m64bcst</para>
        /// </summary>
        public static Vector128<double> Add(Vector128<double> left, Vector128<double> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_adds_epi8 (__m128i a,  __m128i b)</para>
        ///   <para>   PADDSB xmm1,               xmm2/m128</para>
        ///   <para>  VPADDSB xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPADDSB xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<sbyte> AddSaturate(Vector128<sbyte> left, Vector128<sbyte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_adds_epu8 (__m128i a,  __m128i b)</para>
        ///   <para>   PADDUSB xmm1,               xmm2/m128</para>
        ///   <para>  VPADDUSB xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPADDUSB xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<byte> AddSaturate(Vector128<byte> left, Vector128<byte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_adds_epi16 (__m128i a,  __m128i b)</para>
        ///   <para>   PADDSW xmm1,               xmm2/m128</para>
        ///   <para>  VPADDSW xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPADDSW xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<short> AddSaturate(Vector128<short> left, Vector128<short> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_adds_epu16 (__m128i a,  __m128i b)</para>
        ///   <para>   PADDUSW xmm1,               xmm2/m128</para>
        ///   <para>  VPADDUSW xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPADDUSW xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<ushort> AddSaturate(Vector128<ushort> left, Vector128<ushort> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_and_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   PAND xmm1,       xmm2/m128</para>
        ///   <para>  VPAND xmm1, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<byte> And(Vector128<byte> left, Vector128<byte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_and_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   PAND xmm1,       xmm2/m128</para>
        ///   <para>  VPAND xmm1, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<sbyte> And(Vector128<sbyte> left, Vector128<sbyte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_and_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   PAND xmm1,       xmm2/m128</para>
        ///   <para>  VPAND xmm1, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<short> And(Vector128<short> left, Vector128<short> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_and_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   PAND xmm1,       xmm2/m128</para>
        ///   <para>  VPAND xmm1, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<ushort> And(Vector128<ushort> left, Vector128<ushort> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_and_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   PAND  xmm1,               xmm2/m128</para>
        ///   <para>  VPAND  xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPANDD xmm1 {k1}{z}, xmm2, xmm3/m128/m32bcst</para>
        /// </summary>
        public static Vector128<int> And(Vector128<int> left, Vector128<int> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_and_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   PAND  xmm1,               xmm2/m128</para>
        ///   <para>  VPAND  xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPANDD xmm1 {k1}{z}, xmm2, xmm3/m128/m32bcst</para>
        /// </summary>
        public static Vector128<uint> And(Vector128<uint> left, Vector128<uint> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_and_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   PAND  xmm1,               xmm2/m128</para>
        ///   <para>  VPAND  xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPANDQ xmm1 {k1}{z}, xmm2, xmm3/m128/m64bcst</para>
        /// </summary>
        public static Vector128<long> And(Vector128<long> left, Vector128<long> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_and_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   PAND  xmm1,               xmm2/m128</para>
        ///   <para>  VPAND  xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPANDQ xmm1 {k1}{z}, xmm2, xmm3/m128/m64bcst</para>
        /// </summary>
        public static Vector128<ulong> And(Vector128<ulong> left, Vector128<ulong> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128d _mm_and_pd (__m128d a, __m128d b)</para>
        ///   <para>   ANDPD xmm1,               xmm2/m128</para>
        ///   <para>  VANDPD xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VANDPD xmm1 {k1}{z}, xmm2, xmm3/m128/m64bcst</para>
        /// </summary>
        public static Vector128<double> And(Vector128<double> left, Vector128<double> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>int _mm_cvtsi128_si32 (__m128i a)</para>
        ///   <para>   MOVD r/m32, xmm1</para>
        ///   <para>  VMOVD r/m32, xmm1</para>
        /// </summary>
        public static uint ConvertToUInt32(Vector128<uint> value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128 _mm_cvtepi32_ps (__m128i a)</para>
        ///   <para>   CVTDQ2PS xmm1,         xmm2/m128</para>
        ///   <para>  VCVTDQ2PS xmm1,         xmm2/m128</para>
        ///   <para>  VCVTDQ2PS xmm1 {k1}{z}, xmm2/m128/m32bcst</para>
        /// </summary>
        public static Vector128<float> ConvertToVector128Single(Vector128<int> value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128 _mm_cvtpd_ps (__m128d a)</para>
        ///   <para>   CVTPD2PS xmm1,         xmm2/m128</para>
        ///   <para>  VCVTPD2PS xmm1,         xmm2/m128</para>
        ///   <para>  VCVTPD2PS xmm1 {k1}{z}, xmm2/m128/m64bcst</para>
        /// </summary>
        public static Vector128<float> ConvertToVector128Single(Vector128<double> value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>int _mm_movemask_epi8 (__m128i a)</para>
        ///   <para>   PMOVMSKB r32, xmm1</para>
        ///   <para>  VPMOVMSKB r32, xmm1</para>
        /// </summary>
        public static int MoveMask(Vector128<sbyte> value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>int _mm_movemask_epi8 (__m128i a)</para>
        ///   <para>   PMOVMSKB r32, xmm1</para>
        ///   <para>  VPMOVMSKB r32, xmm1</para>
        /// </summary>
        public static int MoveMask(Vector128<byte> value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>int _mm_movemask_pd (__m128d a)</para>
        ///   <para>   MOVMSKPD r32, xmm1</para>
        ///   <para>  VMOVMSKPD r32, xmm1</para>
        /// </summary>
        public static int MoveMask(Vector128<double> value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_or_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   POR xmm1,       xmm2/m128</para>
        ///   <para>  VPOR xmm1, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<byte> Or(Vector128<byte> left, Vector128<byte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_or_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   POR xmm1,       xmm2/m128</para>
        ///   <para>  VPOR xmm1, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<sbyte> Or(Vector128<sbyte> left, Vector128<sbyte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_or_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   POR xmm1,       xmm2/m128</para>
        ///   <para>  VPOR xmm1, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<short> Or(Vector128<short> left, Vector128<short> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_or_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   POR  xmm1,       xmm2/m128</para>
        ///   <para>  VPOR  xmm1, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<ushort> Or(Vector128<ushort> left, Vector128<ushort> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_or_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   POR  xmm1,               xmm2/m128</para>
        ///   <para>  VPOR  xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPORD xmm1 {k1}{z}, xmm2, xmm3/m128/m32bcst</para>
        /// </summary>
        public static Vector128<int> Or(Vector128<int> left, Vector128<int> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_or_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   POR  xmm1,               xmm2/m128</para>
        ///   <para>  VPOR  xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPORD xmm1 {k1}{z}, xmm2, xmm3/m128/m32bcst</para>
        /// </summary>
        public static Vector128<uint> Or(Vector128<uint> left, Vector128<uint> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_or_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   POR  xmm1,               xmm2/m128</para>
        ///   <para>  VPOR  xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPORQ xmm1 {k1}{z}, xmm2, xmm3/m128/m64bcst</para>
        /// </summary>
        public static Vector128<long> Or(Vector128<long> left, Vector128<long> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_or_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   POR  xmm1,               xmm2/m128</para>
        ///   <para>  VPOR  xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPORQ xmm1 {k1}{z}, xmm2, xmm3/m128/m64bcst</para>
        /// </summary>
        public static Vector128<ulong> Or(Vector128<ulong> left, Vector128<ulong> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128d _mm_or_pd (__m128d a,  __m128d b)</para>
        ///   <para>   ORPD xmm1,               xmm2/m128</para>
        ///   <para>  VORPD xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VORPD xmm1 {k1}{z}, xmm2, xmm3/m128/m64bcst</para>
        /// </summary>
        public static Vector128<double> Or(Vector128<double> left, Vector128<double> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_packus_epi16 (__m128i a,  __m128i b)</para>
        ///   <para>   PACKUSWB xmm1,               xmm2/m128</para>
        ///   <para>  VPACKUSWB xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPACKUSWB xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<byte> PackUnsignedSaturate(Vector128<short> left, Vector128<short> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_srl_epi16 (__m128i a, __m128i count)</para>
        ///   <para>   PSRLW xmm1,               xmm2/m128</para>
        ///   <para>  VPSRLW xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPSRLW xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<short> ShiftRightLogical(Vector128<short> value, Vector128<short> count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_srl_epi16 (__m128i a, __m128i count)</para>
        ///   <para>   PSRLW xmm1,               xmm2/m128</para>
        ///   <para>  VPSRLW xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPSRLW xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<ushort> ShiftRightLogical(Vector128<ushort> value, Vector128<ushort> count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_srl_epi32 (__m128i a, __m128i count)</para>
        ///   <para>   PSRLD xmm1,               xmm2/m128</para>
        ///   <para>  VPSRLD xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPSRLD xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<int> ShiftRightLogical(Vector128<int> value, Vector128<int> count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_srl_epi32 (__m128i a, __m128i count)</para>
        ///   <para>   PSRLD xmm1,               xmm2/m128</para>
        ///   <para>  VPSRLD xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPSRLD xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<uint> ShiftRightLogical(Vector128<uint> value, Vector128<uint> count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_srl_epi64 (__m128i a, __m128i count)</para>
        ///   <para>   PSRLQ xmm1,               xmm2/m128</para>
        ///   <para>  VPSRLQ xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPSRLQ xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<long> ShiftRightLogical(Vector128<long> value, Vector128<long> count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_srl_epi64 (__m128i a, __m128i count)</para>
        ///   <para>   PSRLQ xmm1,               xmm2/m128</para>
        ///   <para>  VPSRLQ xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPSRLQ xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<ulong> ShiftRightLogical(Vector128<ulong> value, Vector128<ulong> count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_srli_epi16 (__m128i a,  int immediate)</para>
        ///   <para>   PSRLW xmm1,               imm8</para>
        ///   <para>  VPSRLW xmm1,         xmm2, imm8</para>
        ///   <para>  VPSRLW xmm1 {k1}{z}, xmm2, imm8</para>
        /// </summary>
        public static Vector128<short> ShiftRightLogical(Vector128<short> value, [ConstantExpected] byte count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_srli_epi16 (__m128i a,  int immediate)</para>
        ///   <para>   PSRLW xmm1,               imm8</para>
        ///   <para>  VPSRLW xmm1,         xmm2, imm8</para>
        ///   <para>  VPSRLW xmm1 {k1}{z}, xmm2, imm8</para>
        /// </summary>
        public static Vector128<ushort> ShiftRightLogical(Vector128<ushort> value, [ConstantExpected] byte count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_srli_epi32 (__m128i a,  int immediate)</para>
        ///   <para>   PSRLD xmm1,               imm8</para>
        ///   <para>  VPSRLD xmm1,         xmm2, imm8</para>
        ///   <para>  VPSRLD xmm1 {k1}{z}, xmm2, imm8</para>
        /// </summary>
        public static Vector128<int> ShiftRightLogical(Vector128<int> value, [ConstantExpected] byte count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_srli_epi32 (__m128i a,  int immediate)</para>
        ///   <para>   PSRLD xmm1,               imm8</para>
        ///   <para>  VPSRLD xmm1,         xmm2, imm8</para>
        ///   <para>  VPSRLD xmm1 {k1}{z}, xmm2, imm8</para>
        /// </summary>
        public static Vector128<uint> ShiftRightLogical(Vector128<uint> value, [ConstantExpected] byte count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_srli_epi64 (__m128i a,  int immediate)</para>
        ///   <para>   PSRLQ xmm1,               imm8</para>
        ///   <para>  VPSRLQ xmm1,         xmm2, imm8</para>
        ///   <para>  VPSRLQ xmm1 {k1}{z}, xmm2, imm8</para>
        /// </summary>
        public static Vector128<long> ShiftRightLogical(Vector128<long> value, [ConstantExpected] byte count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_srli_epi64 (__m128i a,  int immediate)</para>
        ///   <para>   PSRLQ xmm1,               imm8</para>
        ///   <para>  VPSRLQ xmm1,         xmm2, imm8</para>
        ///   <para>  VPSRLQ xmm1 {k1}{z}, xmm2, imm8</para>
        /// </summary>
        public static Vector128<ulong> ShiftRightLogical(Vector128<ulong> value, [ConstantExpected] byte count) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void _mm_storeu_si32 (void* mem_addr, __m128i a)</para>
        ///   <para>   MOVD m32, xmm1</para>
        ///   <para>  VMOVD m32, xmm1</para>
        /// </summary>
        public static unsafe void StoreScalar(int* address, Vector128<int> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void _mm_storeu_si32 (void* mem_addr, __m128i a)</para>
        ///   <para>   MOVD m32, xmm1</para>
        ///   <para>  VMOVD m32, xmm1</para>
        /// </summary>
        public static unsafe void StoreScalar(uint* address, Vector128<uint> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void _mm_storel_epi64 (__m128i* mem_addr, __m128i a)</para>
        ///   <para>   MOVQ m64, xmm1</para>
        ///   <para>  VMOVQ m64, xmm1</para>
        /// </summary>
        public static unsafe void StoreScalar(long* address, Vector128<long> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void _mm_storel_epi64 (__m128i* mem_addr, __m128i a)</para>
        ///   <para>   MOVQ m64, xmm1</para>
        ///   <para>  VMOVQ m64, xmm1</para>
        /// </summary>
        public static unsafe void StoreScalar(ulong* address, Vector128<ulong> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void _mm_store_sd (double* mem_addr, __m128d a)</para>
        ///   <para>   MOVSD m64,      xmm1</para>
        ///   <para>  VMOVSD m64,      xmm1</para>
        ///   <para>  VMOVSD m64 {k1}, xmm1</para>
        /// </summary>
        public static unsafe void StoreScalar(double* address, Vector128<double> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_sub_epi8 (__m128i a,  __m128i b)</para>
        ///   <para>   PSUBB xmm1,               xmm2/m128</para>
        ///   <para>  VPSUBB xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPSUBB xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<byte> Subtract(Vector128<byte> left, Vector128<byte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_sub_epi8 (__m128i a,  __m128i b)</para>
        ///   <para>   PSUBB xmm1,               xmm2/m128</para>
        ///   <para>  VPSUBB xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPSUBB xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<sbyte> Subtract(Vector128<sbyte> left, Vector128<sbyte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_sub_epi16 (__m128i a,  __m128i b)</para>
        ///   <para>   PSUBW xmm1,               xmm2/m128</para>
        ///   <para>  VPSUBW xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPSUBW xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<short> Subtract(Vector128<short> left, Vector128<short> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_sub_epi16 (__m128i a,  __m128i b)</para>
        ///   <para>   PSUBW xmm1,               xmm2/m128</para>
        ///   <para>  VPSUBW xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPSUBW xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<ushort> Subtract(Vector128<ushort> left, Vector128<ushort> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_sub_epi32 (__m128i a,  __m128i b)</para>
        ///   <para>   PSUBD xmm1,               xmm2/m128</para>
        ///   <para>  VPSUBD xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPSUBD xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<int> Subtract(Vector128<int> left, Vector128<int> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_sub_epi32 (__m128i a,  __m128i b)</para>
        ///   <para>   PSUBD xmm1,               xmm2/m128</para>
        ///   <para>  VPSUBD xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPSUBD xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<uint> Subtract(Vector128<uint> left, Vector128<uint> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_sub_epi64 (__m128i a,  __m128i b)</para>
        ///   <para>   PSUBQ xmm1,               xmm2/m128</para>
        ///   <para>  VPSUBQ xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPSUBQ xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<long> Subtract(Vector128<long> left, Vector128<long> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_sub_epi64 (__m128i a,  __m128i b)</para>
        ///   <para>   PSUBQ xmm1,               xmm2/m128</para>
        ///   <para>  VPSUBQ xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPSUBQ xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<ulong> Subtract(Vector128<ulong> left, Vector128<ulong> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128d _mm_sub_pd (__m128d a, __m128d b)</para>
        ///   <para>   SUBPD xmm1,               xmm2/m128</para>
        ///   <para>  VSUBPD xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VSUBPD xmm1 {k1}{z}, xmm2, xmm3/m128/m64bcst</para>
        /// </summary>
        public static Vector128<double> Subtract(Vector128<double> left, Vector128<double> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_unpackhi_epi8 (__m128i a,  __m128i b)</para>
        ///   <para>   PUNPCKHBW xmm1,               xmm2/m128</para>
        ///   <para>  VPUNPCKHBW xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPUNPCKHBW xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<byte> UnpackHigh(Vector128<byte> left, Vector128<byte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_unpackhi_epi8 (__m128i a,  __m128i b)</para>
        ///   <para>   PUNPCKHBW xmm1,               xmm2/m128</para>
        ///   <para>  VPUNPCKHBW xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPUNPCKHBW xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<sbyte> UnpackHigh(Vector128<sbyte> left, Vector128<sbyte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_unpackhi_epi16 (__m128i a,  __m128i b)</para>
        ///   <para>   PUNPCKHWD xmm1,               xmm2/m128</para>
        ///   <para>  VPUNPCKHWD xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPUNPCKHWD xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<short> UnpackHigh(Vector128<short> left, Vector128<short> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_unpackhi_epi16 (__m128i a,  __m128i b)</para>
        ///   <para>   PUNPCKHWD xmm1,               xmm2/m128</para>
        ///   <para>  VPUNPCKHWD xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPUNPCKHWD xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<ushort> UnpackHigh(Vector128<ushort> left, Vector128<ushort> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_unpackhi_epi32 (__m128i a,  __m128i b)</para>
        ///   <para>   PUNPCKHDQ xmm1,               xmm2/m128</para>
        ///   <para>  VPUNPCKHDQ xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPUNPCKHDQ xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<int> UnpackHigh(Vector128<int> left, Vector128<int> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_unpackhi_epi32 (__m128i a,  __m128i b)</para>
        ///   <para>   PUNPCKHDQ xmm1,               xmm2/m128</para>
        ///   <para>  VPUNPCKHDQ xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPUNPCKHDQ xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<uint> UnpackHigh(Vector128<uint> left, Vector128<uint> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_unpackhi_epi64 (__m128i a,  __m128i b)</para>
        ///   <para>   PUNPCKHQDQ xmm1,               xmm2/m128</para>
        ///   <para>  VPUNPCKHQDQ xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPUNPCKHQDQ xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<long> UnpackHigh(Vector128<long> left, Vector128<long> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_unpackhi_epi64 (__m128i a,  __m128i b)</para>
        ///   <para>   PUNPCKHQDQ xmm1,               xmm2/m128</para>
        ///   <para>  VPUNPCKHQDQ xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPUNPCKHQDQ xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<ulong> UnpackHigh(Vector128<ulong> left, Vector128<ulong> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128d _mm_unpackhi_pd (__m128d a,  __m128d b)</para>
        ///   <para>   UNPCKHPD xmm1,               xmm2/m128</para>
        ///   <para>  VUNPCKHPD xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VUNPCKHPD xmm1 {k1}{z}, xmm2, xmm3/m128/m64bcst</para>
        /// </summary>
        public static Vector128<double> UnpackHigh(Vector128<double> left, Vector128<double> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_unpacklo_epi8 (__m128i a,  __m128i b)</para>
        ///   <para>   PUNPCKLBW xmm1,               xmm2/m128</para>
        ///   <para>  VPUNPCKLBW xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPUNPCKLBW xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<byte> UnpackLow(Vector128<byte> left, Vector128<byte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_unpacklo_epi8 (__m128i a,  __m128i b)</para>
        ///   <para>   PUNPCKLBW xmm1,               xmm2/m128</para>
        ///   <para>  VPUNPCKLBW xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPUNPCKLBW xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<sbyte> UnpackLow(Vector128<sbyte> left, Vector128<sbyte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_unpacklo_epi16 (__m128i a,  __m128i b)</para>
        ///   <para>   PUNPCKLWD xmm1,               xmm2/m128</para>
        ///   <para>  VPUNPCKLWD xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPUNPCKLWD xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<short> UnpackLow(Vector128<short> left, Vector128<short> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_unpacklo_epi16 (__m128i a,  __m128i b)</para>
        ///   <para>   PUNPCKLWD xmm1,               xmm2/m128</para>
        ///   <para>  VPUNPCKLWD xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPUNPCKLWD xmm1 {k1}{z}, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<ushort> UnpackLow(Vector128<ushort> left, Vector128<ushort> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_unpacklo_epi32 (__m128i a,  __m128i b)</para>
        ///   <para>   PUNPCKLDQ xmm1,               xmm2/m128</para>
        ///   <para>  VPUNPCKLDQ xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPUNPCKLDQ xmm1 {k1}{z}, xmm2, xmm3/m128/m32bcst</para>
        /// </summary>
        public static Vector128<int> UnpackLow(Vector128<int> left, Vector128<int> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_unpacklo_epi32 (__m128i a,  __m128i b)</para>
        ///   <para>   PUNPCKLDQ xmm1,               xmm2/m128</para>
        ///   <para>  VPUNPCKLDQ xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPUNPCKLDQ xmm1 {k1}{z}, xmm2, xmm3/m128/m32bcst</para>
        /// </summary>
        public static Vector128<uint> UnpackLow(Vector128<uint> left, Vector128<uint> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_unpacklo_epi64 (__m128i a,  __m128i b)</para>
        ///   <para>   PUNPCKLQDQ xmm1,               xmm2/m128</para>
        ///   <para>  VPUNPCKLQDQ xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPUNPCKLQDQ xmm1 {k1}{z}, xmm2, xmm3/m128/m64bcst</para>
        /// </summary>
        public static Vector128<long> UnpackLow(Vector128<long> left, Vector128<long> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_unpacklo_epi64 (__m128i a,  __m128i b)</para>
        ///   <para>   PUNPCKLQDQ xmm1,               xmm2/m128</para>
        ///   <para>  VPUNPCKLQDQ xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPUNPCKLQDQ xmm1 {k1}{z}, xmm2, xmm3/m128/m64bcst</para>
        /// </summary>
        public static Vector128<ulong> UnpackLow(Vector128<ulong> left, Vector128<ulong> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128d _mm_unpacklo_pd (__m128d a,  __m128d b)</para>
        ///   <para>   UNPCKLPD xmm1,               xmm2/m128</para>
        ///   <para>  VUNPCKLPD xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VUNPCKLPD xmm1 {k1}{z}, xmm2, xmm3/m128/m64bcst</para>
        /// </summary>
        public static Vector128<double> UnpackLow(Vector128<double> left, Vector128<double> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_xor_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   PXOR xmm1,       xmm2/m128</para>
        ///   <para>  VPXOR xmm1, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<byte> Xor(Vector128<byte> left, Vector128<byte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_xor_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   PXOR xmm1,       xmm2/m128</para>
        ///   <para>  VPXOR xmm1, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<sbyte> Xor(Vector128<sbyte> left, Vector128<sbyte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_xor_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   PXOR xmm1,       xmm2/m128</para>
        ///   <para>  VPXOR xmm1, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<short> Xor(Vector128<short> left, Vector128<short> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_xor_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   PXOR xmm1,       xmm2/m128</para>
        ///   <para>  VPXOR xmm1, xmm2, xmm3/m128</para>
        /// </summary>
        public static Vector128<ushort> Xor(Vector128<ushort> left, Vector128<ushort> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_xor_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   PXOR  xmm1,               xmm2/m128</para>
        ///   <para>  VPXOR  xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPXORD xmm1 {k1}{z}, xmm2, xmm3/m128/m32bcst</para>
        /// </summary>
        public static Vector128<int> Xor(Vector128<int> left, Vector128<int> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_xor_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   PXOR  xmm1,               xmm2/m128</para>
        ///   <para>  VPXOR  xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPXORD xmm1 {k1}{z}, xmm2, xmm3/m128/m32bcst</para>
        /// </summary>
        public static Vector128<uint> Xor(Vector128<uint> left, Vector128<uint> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_xor_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   PXOR  xmm1,               xmm2/m128</para>
        ///   <para>  VPXOR  xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPXORQ xmm1 {k1}{z}, xmm2, xmm3/m128/m64bcst</para>
        /// </summary>
        public static Vector128<long> Xor(Vector128<long> left, Vector128<long> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128i _mm_xor_si128 (__m128i a,  __m128i b)</para>
        ///   <para>   PXOR  xmm1,               xmm2/m128</para>
        ///   <para>  VPXOR  xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VPXORQ xmm1 {k1}{z}, xmm2, xmm3/m128/m64bcst</para>
        /// </summary>
        public static Vector128<ulong> Xor(Vector128<ulong> left, Vector128<ulong> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128d _mm_xor_pd (__m128d a,  __m128d b)</para>
        ///   <para>   XORPD xmm1,               xmm2/m128</para>
        ///   <para>  VXORPD xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VXORPD xmm1 {k1}{z}, xmm2, xmm3/m128/m64bcst</para>
        /// </summary>
        public static Vector128<double> Xor(Vector128<double> left, Vector128<double> right) { throw new PlatformNotSupportedException(); }
    }
    [CLSCompliant(false)]
    public abstract class Sse : X86Base
    {
        public new abstract class X64 : X86Base.X64
        {
            /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
            /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
            /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
            public static new bool IsSupported { [Intrinsic] get { return false; } }
            internal X64() { }
        }
        /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
        /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
        /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
        public static new bool IsSupported { [Intrinsic] get { return false; } }
        internal Sse() { }
        /// <summary>
        ///   <para>__m128 _mm_add_ps (__m128 a,  __m128 b)</para>
        ///   <para>   ADDPS xmm1,               xmm2/m128</para>
        ///   <para>  VADDPS xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VADDPS xmm1 {k1}{z}, xmm2, xmm3/m128/m32bcst</para>
        /// </summary>
        public static Vector128<float> Add(Vector128<float> left, Vector128<float> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>__m128 _mm_mul_ps (__m128 a, __m128 b)</para>
        ///   <para>   MULPS xmm1,               xmm2/m128</para>
        ///   <para>  VMULPS xmm1,         xmm2, xmm3/m128</para>
        ///   <para>  VMULPS xmm1 {k1}{z}, xmm2, xmm3/m128/m32bcst</para>
        /// </summary>
        public static Vector128<float> Multiply(Vector128<float> left, Vector128<float> right) { throw new PlatformNotSupportedException(); }
    }
    [CLSCompliant(false)]
    public abstract partial class X86Base
    {
        public abstract class X64
        {
            /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
            /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
            /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
            public static bool IsSupported { [Intrinsic] get => false; }
            internal X64() { }
            /// <summary>
            ///   <para>unsigned char _BitScanForward64 (unsigned __int32* index, unsigned __int64 a)</para>
            ///   <para>  BSF reg reg/m64</para>
            ///   <para>The above native signature does not directly correspond to the managed signature.</para>
            /// </summary>
            /// <remarks>
            ///   <para>This method is to remain internal.</para>
            ///   <para>Its functionality is exposed in the public <see cref="System.Numerics.BitOperations" /> class.</para>
            /// </remarks>
            internal static ulong BitScanForward(ulong value) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>unsigned char _BitScanReverse64 (unsigned __int32* index, unsigned __int64 a)</para>
            ///   <para>  BSR reg reg/m64</para>
            ///   <para>The above native signature does not directly correspond to the managed signature.</para>
            /// </summary>
            /// <remarks>
            ///   <para>This method is to remain internal.</para>
            ///   <para>Its functionality is exposed in the public <see cref="System.Numerics.BitOperations" /> class.</para>
            /// </remarks>
            internal static ulong BitScanReverse(ulong value) { throw new PlatformNotSupportedException(); }
        }
        /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
        /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
        /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
        public static bool IsSupported { [Intrinsic] get => false; }
        internal X86Base() { }
        /// <summary>
        ///   <para>unsigned char _BitScanForward (unsigned __int32* index, unsigned __int32 a)</para>
        ///   <para>  BSF reg reg/m32</para>
        ///   <para>The above native signature does not directly correspond to the managed signature.</para>
        /// </summary>
        /// <remarks>
        ///   <para>This method is to remain internal.</para>
        ///   <para>Its functionality is exposed in the public <see cref="System.Numerics.BitOperations" /> class.</para>
        /// </remarks>
        internal static uint BitScanForward(uint value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>unsigned char _BitScanReverse (unsigned __int32* index, unsigned __int32 a)</para>
        ///   <para>  BSR reg reg/m32</para>
        ///   <para>The above native signature does not directly correspond to the managed signature.</para>
        /// </summary>
        /// <remarks>
        ///   <para>This method is to remain internal.</para>
        ///   <para>Its functionality is exposed in the public <see cref="System.Numerics.BitOperations" /> class.</para>
        /// </remarks>
        internal static uint BitScanReverse(uint value) { throw new PlatformNotSupportedException(); }
    }
    public enum FloatComparisonMode : byte
    {
        /// <summary>_CMP_EQ_OQ</summary>
        OrderedEqualNonSignaling = 0,

        /// <summary>_CMP_LT_OS</summary>
        OrderedLessThanSignaling = 1,

        /// <summary>_CMP_LE_OS</summary>
        OrderedLessThanOrEqualSignaling = 2,

        /// <summary>_CMP_UNORD_Q</summary>
        UnorderedNonSignaling = 3,

        /// <summary>_CMP_NEQ_UQ</summary>
        UnorderedNotEqualNonSignaling = 4,

        /// <summary>_CMP_NLT_US</summary>
        UnorderedNotLessThanSignaling = 5,

        /// <summary>_CMP_NLE_US</summary>
        UnorderedNotLessThanOrEqualSignaling = 6,

        /// <summary>_CMP_ORD_Q</summary>
        OrderedNonSignaling = 7,

        /// <summary>_CMP_EQ_UQ</summary>
        UnorderedEqualNonSignaling = 8,

        /// <summary>_CMP_NGE_US</summary>
        UnorderedNotGreaterThanOrEqualSignaling = 9,

        /// <summary>_CMP_NGT_US</summary>
        UnorderedNotGreaterThanSignaling = 10,

        /// <summary>_CMP_FALSE_OQ</summary>
        OrderedFalseNonSignaling = 11,

        /// <summary>_CMP_NEQ_OQ</summary>
        OrderedNotEqualNonSignaling = 12,

        /// <summary>_CMP_GE_OS</summary>
        OrderedGreaterThanOrEqualSignaling = 13,

        /// <summary>_CMP_GT_OS</summary>
        OrderedGreaterThanSignaling = 14,

        /// <summary>_CMP_TRUE_UQ</summary>
        UnorderedTrueNonSignaling = 15,

        /// <summary>_CMP_EQ_OS</summary>
        OrderedEqualSignaling = 16,

        /// <summary>_CMP_LT_OQ</summary>
        OrderedLessThanNonSignaling = 17,

        /// <summary>_CMP_LE_OQ</summary>
        OrderedLessThanOrEqualNonSignaling = 18,

        /// <summary>_CMP_UNORD_S</summary>
        UnorderedSignaling = 19,

        /// <summary>_CMP_NEQ_US</summary>
        UnorderedNotEqualSignaling = 20,

        /// <summary>_CMP_NLT_UQ</summary>
        UnorderedNotLessThanNonSignaling = 21,

        /// <summary>_CMP_NLE_UQ</summary>
        UnorderedNotLessThanOrEqualNonSignaling = 22,

        /// <summary>_CMP_ORD_S</summary>
        OrderedSignaling = 23,

        /// <summary>_CMP_EQ_US</summary>
        UnorderedEqualSignaling = 24,

        /// <summary>_CMP_NGE_UQ</summary>
        UnorderedNotGreaterThanOrEqualNonSignaling = 25,

        /// <summary>_CMP_NGT_UQ</summary>
        UnorderedNotGreaterThanNonSignaling = 26,

        /// <summary>_CMP_FALSE_OS</summary>
        OrderedFalseSignaling = 27,

        /// <summary>_CMP_NEQ_OS</summary>
        OrderedNotEqualSignaling = 28,

        /// <summary>_CMP_GE_OQ</summary>
        OrderedGreaterThanOrEqualNonSignaling = 29,

        /// <summary>_CMP_GT_OQ</summary>
        OrderedGreaterThanNonSignaling = 30,

        /// <summary>_CMP_TRUE_US</summary>
        UnorderedTrueSignaling = 31,
    }

    public enum FloatRoundingMode : byte
    {
        /// <summary>_MM_FROUND_TO_NEAREST_INT | _MM_FROUND_NO_EXC</summary>
        ToEven = 0x08,
        /// <summary>_MM_FROUND_TO_NEG_INF | _MM_FROUND_NO_EXC</summary>
        ToNegativeInfinity = 0x09,
        /// <summary>_MM_FROUND_TO_POS_INF | _MM_FROUND_NO_EXC</summary>
        ToPositiveInfinity = 0x0A,
        /// <summary>_MM_FROUND_TO_ZERO | _MM_FROUND_NO_EXC</summary>
        ToZero = 0x0B,
    }
}
