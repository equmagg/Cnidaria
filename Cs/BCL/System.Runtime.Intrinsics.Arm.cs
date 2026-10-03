using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Wasm;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace System.Runtime.Intrinsics.Arm
{
    [CLSCompliant(false)]
    public abstract class AdvSimd : ArmBase
    {
        public new abstract class Arm64 : ArmBase.Arm64
        {
            public static new bool IsSupported { [Intrinsic] get { return false; } }
            internal Arm64() { }
            /// <summary>
            ///   <para>float64x2_t vcvt_f64_f32 (float32x2_t a)</para>
            ///   <para>  A64: FCVTL Vd.2D, Vn.2S</para>
            /// </summary>
            public static Vector128<double> ConvertToDouble(Vector64<float> value) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>float64x2_t vcvtq_f64_s64 (int64x2_t a)</para>
            ///   <para>  A64: SCVTF Vd.2D, Vn.2D</para>
            /// </summary>
            public static Vector128<double> ConvertToDouble(Vector128<long> value) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>float64x2_t vcvtq_f64_u64 (uint64x2_t a)</para>
            ///   <para>  A64: UCVTF Vd.2D, Vn.2D</para>
            /// </summary>
            public static Vector128<double> ConvertToDouble(Vector128<ulong> value) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>float32x2_t vcvt_f32_f64 (float64x2_t a)</para>
            ///   <para>  A64: FCVTN Vd.2S, Vn.2D</para>
            /// </summary>
            public static Vector64<float> ConvertToSingleLower(Vector128<double> value) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint8x16_t vpmaxq_u8 (uint8x16_t a, uint8x16_t b)</para>
            ///   <para>  A64: UMAXP Vd.16B, Vn.16B, Vm.16B</para>
            /// </summary>
            public static Vector128<byte> MaxPairwise(Vector128<byte> left, Vector128<byte> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>float64x2_t vpmaxq_f64 (float64x2_t a, float64x2_t b)</para>
            ///   <para>  A64: FMAXP Vd.2D, Vn.2D, Vm.2D</para>
            /// </summary>
            public static Vector128<double> MaxPairwise(Vector128<double> left, Vector128<double> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int16x8_t vpmaxq_s16 (int16x8_t a, int16x8_t b)</para>
            ///   <para>  A64: SMAXP Vd.8H, Vn.8H, Vm.8H</para>
            /// </summary>
            public static Vector128<short> MaxPairwise(Vector128<short> left, Vector128<short> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int32x4_t vpmaxq_s32 (int32x4_t a, int32x4_t b)</para>
            ///   <para>  A64: SMAXP Vd.4S, Vn.4S, Vm.4S</para>
            /// </summary>
            public static Vector128<int> MaxPairwise(Vector128<int> left, Vector128<int> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int8x16_t vpmaxq_s8 (int8x16_t a, int8x16_t b)</para>
            ///   <para>  A64: SMAXP Vd.16B, Vn.16B, Vm.16B</para>
            /// </summary>
            public static Vector128<sbyte> MaxPairwise(Vector128<sbyte> left, Vector128<sbyte> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>float32x4_t vpmaxq_f32 (float32x4_t a, float32x4_t b)</para>
            ///   <para>  A64: FMAXP Vd.4S, Vn.4S, Vm.4S</para>
            /// </summary>
            public static Vector128<float> MaxPairwise(Vector128<float> left, Vector128<float> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint16x8_t vpmaxq_u16 (uint16x8_t a, uint16x8_t b)</para>
            ///   <para>  A64: UMAXP Vd.8H, Vn.8H, Vm.8H</para>
            /// </summary>
            public static Vector128<ushort> MaxPairwise(Vector128<ushort> left, Vector128<ushort> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint32x4_t vpmaxq_u32 (uint32x4_t a, uint32x4_t b)</para>
            ///   <para>  A64: UMAXP Vd.4S, Vn.4S, Vm.4S</para>
            /// </summary>
            public static Vector128<uint> MaxPairwise(Vector128<uint> left, Vector128<uint> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint8x16_t vpminq_u8 (uint8x16_t a, uint8x16_t b)</para>
            ///   <para>  A64: UMINP Vd.16B, Vn.16B, Vm.16B</para>
            /// </summary>
            public static Vector128<byte> MinPairwise(Vector128<byte> left, Vector128<byte> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>float64x2_t vpminq_f64 (float64x2_t a, float64x2_t b)</para>
            ///   <para>  A64: FMINP Vd.2D, Vn.2D, Vm.2D</para>
            /// </summary>
            public static Vector128<double> MinPairwise(Vector128<double> left, Vector128<double> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int16x8_t vpminq_s16 (int16x8_t a, int16x8_t b)</para>
            ///   <para>  A64: SMINP Vd.8H, Vn.8H, Vm.8H</para>
            /// </summary>
            public static Vector128<short> MinPairwise(Vector128<short> left, Vector128<short> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int32x4_t vpminq_s32 (int32x4_t a, int32x4_t b)</para>
            ///   <para>  A64: SMINP Vd.4S, Vn.4S, Vm.4S</para>
            /// </summary>
            public static Vector128<int> MinPairwise(Vector128<int> left, Vector128<int> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int8x16_t vpminq_s8 (int8x16_t a, int8x16_t b)</para>
            ///   <para>  A64: SMINP Vd.16B, Vn.16B, Vm.16B</para>
            /// </summary>
            public static Vector128<sbyte> MinPairwise(Vector128<sbyte> left, Vector128<sbyte> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>float32x4_t vpminq_f32 (float32x4_t a, float32x4_t b)</para>
            ///   <para>  A64: FMINP Vd.4S, Vn.4S, Vm.4S</para>
            /// </summary>
            public static Vector128<float> MinPairwise(Vector128<float> left, Vector128<float> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint16x8_t vpminq_u16 (uint16x8_t a, uint16x8_t b)</para>
            ///   <para>  A64: UMINP Vd.8H, Vn.8H, Vm.8H</para>
            /// </summary>
            public static Vector128<ushort> MinPairwise(Vector128<ushort> left, Vector128<ushort> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint32x4_t vpminq_u32 (uint32x4_t a, uint32x4_t b)</para>
            ///   <para>  A64: UMINP Vd.4S, Vn.4S, Vm.4S</para>
            /// </summary>
            public static Vector128<uint> MinPairwise(Vector128<uint> left, Vector128<uint> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint8x8_t vuzp1_u8(uint8x8_t a, uint8x8_t b)</para>
            ///   <para>  A64: UZP1 Vd.8B, Vn.8B, Vm.8B</para>
            /// </summary>
            public static Vector64<byte> UnzipEven(Vector64<byte> left, Vector64<byte> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int16x4_t vuzp1_s16(int16x4_t a, int16x4_t b)</para>
            ///   <para>  A64: UZP1 Vd.4H, Vn.4H, Vm.4H</para>
            /// </summary>
            public static Vector64<short> UnzipEven(Vector64<short> left, Vector64<short> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int32x2_t vuzp1_s32(int32x2_t a, int32x2_t b)</para>
            ///   <para>  A64: UZP1 Vd.2S, Vn.2S, Vm.2S</para>
            /// </summary>
            public static Vector64<int> UnzipEven(Vector64<int> left, Vector64<int> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int8x8_t vuzp1_s8(int8x8_t a, int8x8_t b)</para>
            ///   <para>  A64: UZP1 Vd.8B, Vn.8B, Vm.8B</para>
            /// </summary>
            public static Vector64<sbyte> UnzipEven(Vector64<sbyte> left, Vector64<sbyte> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>float32x2_t vuzp1_f32(float32x2_t a, float32x2_t b)</para>
            ///   <para>  A64: UZP1 Vd.2S, Vn.2S, Vm.2S</para>
            /// </summary>
            public static Vector64<float> UnzipEven(Vector64<float> left, Vector64<float> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint16x4_t vuzp1_u16(uint16x4_t a, uint16x4_t b)</para>
            ///   <para>  A64: UZP1 Vd.4H, Vn.4H, Vm.4H</para>
            /// </summary>
            public static Vector64<ushort> UnzipEven(Vector64<ushort> left, Vector64<ushort> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint32x2_t vuzp1_u32(uint32x2_t a, uint32x2_t b)</para>
            ///   <para>  A64: UZP1 Vd.2S, Vn.2S, Vm.2S</para>
            /// </summary>
            public static Vector64<uint> UnzipEven(Vector64<uint> left, Vector64<uint> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint8x16_t vuzp1q_u8(uint8x16_t a, uint8x16_t b)</para>
            ///   <para>  A64: UZP1 Vd.16B, Vn.16B, Vm.16B</para>
            /// </summary>
            public static Vector128<byte> UnzipEven(Vector128<byte> left, Vector128<byte> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>float64x2_t vuzp1q_f64(float64x2_t a, float64x2_t b)</para>
            ///   <para>  A64: UZP1 Vd.2D, Vn.2D, Vm.2D</para>
            /// </summary>
            public static Vector128<double> UnzipEven(Vector128<double> left, Vector128<double> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int16x8_t vuzp1q_s16(int16x8_t a, int16x8_t b)</para>
            ///   <para>  A64: UZP1 Vd.8H, Vn.8H, Vm.8H</para>
            /// </summary>
            public static Vector128<short> UnzipEven(Vector128<short> left, Vector128<short> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int32x4_t vuzp1q_s32(int32x4_t a, int32x4_t b)</para>
            ///   <para>  A64: UZP1 Vd.4S, Vn.4S, Vm.4S</para>
            /// </summary>
            public static Vector128<int> UnzipEven(Vector128<int> left, Vector128<int> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int64x2_t vuzp1q_s64(int64x2_t a, int64x2_t b)</para>
            ///   <para>  A64: UZP1 Vd.2D, Vn.2D, Vm.2D</para>
            /// </summary>
            public static Vector128<long> UnzipEven(Vector128<long> left, Vector128<long> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int8x16_t vuzp1q_u8(int8x16_t a, int8x16_t b)</para>
            ///   <para>  A64: UZP1 Vd.16B, Vn.16B, Vm.16B</para>
            /// </summary>
            public static Vector128<sbyte> UnzipEven(Vector128<sbyte> left, Vector128<sbyte> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>float32x4_t vuzp1q_f32(float32x4_t a, float32x4_t b)</para>
            ///   <para>  A64: UZP1 Vd.4S, Vn.4S, Vm.4S</para>
            /// </summary>
            public static Vector128<float> UnzipEven(Vector128<float> left, Vector128<float> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint16x8_t vuzp1q_u16(uint16x8_t a, uint16x8_t b)</para>
            ///   <para>  A64: UZP1 Vd.8H, Vn.8H, Vm.8H</para>
            /// </summary>
            public static Vector128<ushort> UnzipEven(Vector128<ushort> left, Vector128<ushort> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint32x4_t vuzp1q_u32(uint32x4_t a, uint32x4_t b)</para>
            ///   <para>  A64: UZP1 Vd.4S, Vn.4S, Vm.4S</para>
            /// </summary>
            public static Vector128<uint> UnzipEven(Vector128<uint> left, Vector128<uint> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint64x2_t vuzp1q_u64(uint64x2_t a, uint64x2_t b)</para>
            ///   <para>  A64: UZP1 Vd.2D, Vn.2D, Vm.2D</para>
            /// </summary>
            public static Vector128<ulong> UnzipEven(Vector128<ulong> left, Vector128<ulong> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint8x8_t vzip2_u8(uint8x8_t a, uint8x8_t b)</para>
            ///   <para>  A64: ZIP2 Vd.8B, Vn.8B, Vm.8B</para>
            /// </summary>
            public static Vector64<byte> ZipHigh(Vector64<byte> left, Vector64<byte> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int16x4_t vzip2_s16(int16x4_t a, int16x4_t b)</para>
            ///   <para>  A64: ZIP2 Vd.4H, Vn.4H, Vm.4H</para>
            /// </summary>
            public static Vector64<short> ZipHigh(Vector64<short> left, Vector64<short> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int32x2_t vzip2_s32(int32x2_t a, int32x2_t b)</para>
            ///   <para>  A64: ZIP2 Vd.2S, Vn.2S, Vm.2S</para>
            /// </summary>
            public static Vector64<int> ZipHigh(Vector64<int> left, Vector64<int> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int8x8_t vzip2_s8(int8x8_t a, int8x8_t b)</para>
            ///   <para>  A64: ZIP2 Vd.8B, Vn.8B, Vm.8B</para>
            /// </summary>
            public static Vector64<sbyte> ZipHigh(Vector64<sbyte> left, Vector64<sbyte> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>float32x2_t vzip2_f32(float32x2_t a, float32x2_t b)</para>
            ///   <para>  A64: ZIP2 Vd.2S, Vn.2S, Vm.2S</para>
            /// </summary>
            public static Vector64<float> ZipHigh(Vector64<float> left, Vector64<float> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint16x4_t vzip2_u16(uint16x4_t a, uint16x4_t b)</para>
            ///   <para>  A64: ZIP2 Vd.4H, Vn.4H, Vm.4H</para>
            /// </summary>
            public static Vector64<ushort> ZipHigh(Vector64<ushort> left, Vector64<ushort> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint32x2_t vzip2_u32(uint32x2_t a, uint32x2_t b)</para>
            ///   <para>  A64: ZIP2 Vd.2S, Vn.2S, Vm.2S</para>
            /// </summary>
            public static Vector64<uint> ZipHigh(Vector64<uint> left, Vector64<uint> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint8x16_t vzip2q_u8(uint8x16_t a, uint8x16_t b)</para>
            ///   <para>  A64: ZIP2 Vd.16B, Vn.16B, Vm.16B</para>
            /// </summary>
            public static Vector128<byte> ZipHigh(Vector128<byte> left, Vector128<byte> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>float64x2_t vzip2q_f64(float64x2_t a, float64x2_t b)</para>
            ///   <para>  A64: ZIP2 Vd.2D, Vn.2D, Vm.2D</para>
            /// </summary>
            public static Vector128<double> ZipHigh(Vector128<double> left, Vector128<double> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int16x8_t vzip2q_s16(int16x8_t a, int16x8_t b)</para>
            ///   <para>  A64: ZIP2 Vd.8H, Vn.8H, Vm.8H</para>
            /// </summary>
            public static Vector128<short> ZipHigh(Vector128<short> left, Vector128<short> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int32x4_t vzip2q_s32(int32x4_t a, int32x4_t b)</para>
            ///   <para>  A64: ZIP2 Vd.4S, Vn.4S, Vm.4S</para>
            /// </summary>
            public static Vector128<int> ZipHigh(Vector128<int> left, Vector128<int> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int64x2_t vzip2q_s64(int64x2_t a, int64x2_t b)</para>
            ///   <para>  A64: ZIP2 Vd.2D, Vn.2D, Vm.2D</para>
            /// </summary>
            public static Vector128<long> ZipHigh(Vector128<long> left, Vector128<long> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int8x16_t vzip2q_u8(int8x16_t a, int8x16_t b)</para>
            ///   <para>  A64: ZIP2 Vd.16B, Vn.16B, Vm.16B</para>
            /// </summary>
            public static Vector128<sbyte> ZipHigh(Vector128<sbyte> left, Vector128<sbyte> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>float32x4_t vzip2q_f32(float32x4_t a, float32x4_t b)</para>
            ///   <para>  A64: ZIP2 Vd.4S, Vn.4S, Vm.4S</para>
            /// </summary>
            public static Vector128<float> ZipHigh(Vector128<float> left, Vector128<float> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint16x8_t vzip2q_u16(uint16x8_t a, uint16x8_t b)</para>
            ///   <para>  A64: ZIP2 Vd.8H, Vn.8H, Vm.8H</para>
            /// </summary>
            public static Vector128<ushort> ZipHigh(Vector128<ushort> left, Vector128<ushort> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint32x4_t vzip2q_u32(uint32x4_t a, uint32x4_t b)</para>
            ///   <para>  A64: ZIP2 Vd.4S, Vn.4S, Vm.4S</para>
            /// </summary>
            public static Vector128<uint> ZipHigh(Vector128<uint> left, Vector128<uint> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint64x2_t vzip2q_u64(uint64x2_t a, uint64x2_t b)</para>
            ///   <para>  A64: ZIP2 Vd.2D, Vn.2D, Vm.2D</para>
            /// </summary>
            public static Vector128<ulong> ZipHigh(Vector128<ulong> left, Vector128<ulong> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint8x8_t vzip1_u8(uint8x8_t a, uint8x8_t b)</para>
            ///   <para>  A64: ZIP1 Vd.8B, Vn.8B, Vm.8B</para>
            /// </summary>
            public static Vector64<byte> ZipLow(Vector64<byte> left, Vector64<byte> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int16x4_t vzip1_s16(int16x4_t a, int16x4_t b)</para>
            ///   <para>  A64: ZIP1 Vd.4H, Vn.4H, Vm.4H</para>
            /// </summary>
            public static Vector64<short> ZipLow(Vector64<short> left, Vector64<short> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int32x2_t vzip1_s32(int32x2_t a, int32x2_t b)</para>
            ///   <para>  A64: ZIP1 Vd.2S, Vn.2S, Vm.2S</para>
            /// </summary>
            public static Vector64<int> ZipLow(Vector64<int> left, Vector64<int> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int8x8_t vzip1_s8(int8x8_t a, int8x8_t b)</para>
            ///   <para>  A64: ZIP1 Vd.8B, Vn.8B, Vm.8B</para>
            /// </summary>
            public static Vector64<sbyte> ZipLow(Vector64<sbyte> left, Vector64<sbyte> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>float32x2_t vzip1_f32(float32x2_t a, float32x2_t b)</para>
            ///   <para>  A64: ZIP1 Vd.2S, Vn.2S, Vm.2S</para>
            /// </summary>
            public static Vector64<float> ZipLow(Vector64<float> left, Vector64<float> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint16x4_t vzip1_u16(uint16x4_t a, uint16x4_t b)</para>
            ///   <para>  A64: ZIP1 Vd.4H, Vn.4H, Vm.4H</para>
            /// </summary>
            public static Vector64<ushort> ZipLow(Vector64<ushort> left, Vector64<ushort> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint32x2_t vzip1_u32(uint32x2_t a, uint32x2_t b)</para>
            ///   <para>  A64: ZIP1 Vd.2S, Vn.2S, Vm.2S</para>
            /// </summary>
            public static Vector64<uint> ZipLow(Vector64<uint> left, Vector64<uint> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint8x16_t vzip1q_u8(uint8x16_t a, uint8x16_t b)</para>
            ///   <para>  A64: ZIP1 Vd.16B, Vn.16B, Vm.16B</para>
            /// </summary>
            public static Vector128<byte> ZipLow(Vector128<byte> left, Vector128<byte> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>float64x2_t vzip1q_f64(float64x2_t a, float64x2_t b)</para>
            ///   <para>  A64: ZIP1 Vd.2D, Vn.2D, Vm.2D</para>
            /// </summary>
            public static Vector128<double> ZipLow(Vector128<double> left, Vector128<double> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int16x8_t vzip1q_s16(int16x8_t a, int16x8_t b)</para>
            ///   <para>  A64: ZIP1 Vd.8H, Vn.8H, Vm.8H</para>
            /// </summary>
            public static Vector128<short> ZipLow(Vector128<short> left, Vector128<short> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int32x4_t vzip1q_s32(int32x4_t a, int32x4_t b)</para>
            ///   <para>  A64: ZIP1 Vd.4S, Vn.4S, Vm.4S</para>
            /// </summary>
            public static Vector128<int> ZipLow(Vector128<int> left, Vector128<int> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int64x2_t vzip1q_s64(int64x2_t a, int64x2_t b)</para>
            ///   <para>  A64: ZIP1 Vd.2D, Vn.2D, Vm.2D</para>
            /// </summary>
            public static Vector128<long> ZipLow(Vector128<long> left, Vector128<long> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>int8x16_t vzip1q_u8(int8x16_t a, int8x16_t b)</para>
            ///   <para>  A64: ZIP1 Vd.16B, Vn.16B, Vm.16B</para>
            /// </summary>
            public static Vector128<sbyte> ZipLow(Vector128<sbyte> left, Vector128<sbyte> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>float32x4_t vzip1q_f32(float32x4_t a, float32x4_t b)</para>
            ///   <para>  A64: ZIP1 Vd.4S, Vn.4S, Vm.4S</para>
            /// </summary>
            public static Vector128<float> ZipLow(Vector128<float> left, Vector128<float> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint16x8_t vzip1q_u16(uint16x8_t a, uint16x8_t b)</para>
            ///   <para>  A64: ZIP1 Vd.8H, Vn.8H, Vm.8H</para>
            /// </summary>
            public static Vector128<ushort> ZipLow(Vector128<ushort> left, Vector128<ushort> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint32x4_t vzip1q_u32(uint32x4_t a, uint32x4_t b)</para>
            ///   <para>  A64: ZIP1 Vd.4S, Vn.4S, Vm.4S</para>
            /// </summary>
            public static Vector128<uint> ZipLow(Vector128<uint> left, Vector128<uint> right) { throw new PlatformNotSupportedException(); }

            /// <summary>
            ///   <para>uint64x2_t vzip1q_u64(uint64x2_t a, uint64x2_t b)</para>
            ///   <para>  A64: ZIP1 Vd.2D, Vn.2D, Vm.2D</para>
            /// </summary>
            public static Vector128<ulong> ZipLow(Vector128<ulong> left, Vector128<ulong> right) { throw new PlatformNotSupportedException(); }
        }
        public static new bool IsSupported { [Intrinsic] get { return false; } }
        internal AdvSimd() { }
        /// <summary>
        ///   <para>uint8x8_t vtst_u8 (uint8x8_t a, uint8x8_t b)</para>
        ///   <para>  A32: VTST.8 Dd, Dn, Dm</para>
        ///   <para>  A64: CMTST Vd.8B, Vn.8B, Vm.8B</para>
        /// </summary>
        public static Vector64<byte> CompareTest(Vector64<byte> left, Vector64<byte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>uint16x4_t vtst_s16 (int16x4_t a, int16x4_t b)</para>
        ///   <para>  A32: VTST.16 Dd, Dn, Dm</para>
        ///   <para>  A64: CMTST Vd.4H, Vn.4H, Vm.4H</para>
        /// </summary>
        public static Vector64<short> CompareTest(Vector64<short> left, Vector64<short> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>uint32x2_t vtst_s32 (int32x2_t a, int32x2_t b)</para>
        ///   <para>  A32: VTST.32 Dd, Dn, Dm</para>
        ///   <para>  A64: CMTST Vd.2S, Vn.2S, Vm.2S</para>
        /// </summary>
        public static Vector64<int> CompareTest(Vector64<int> left, Vector64<int> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>uint8x8_t vtst_s8 (int8x8_t a, int8x8_t b)</para>
        ///   <para>  A32: VTST.8 Dd, Dn, Dm</para>
        ///   <para>  A64: CMTST Vd.8B, Vn.8B, Vm.8B</para>
        /// </summary>
        public static Vector64<sbyte> CompareTest(Vector64<sbyte> left, Vector64<sbyte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>uint32x2_t vtst_f32 (float32x2_t a, float32x2_t b)</para>
        ///   <para>  A32: VTST.32 Dd, Dn, Dm</para>
        ///   <para>  A64: CMTST Vd.2S, Vn.2S, Vm.2S</para>
        ///   <para>The above native signature does not exist. We provide this additional overload for consistency with the other scalar APIs.</para>
        /// </summary>
        public static Vector64<float> CompareTest(Vector64<float> left, Vector64<float> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>uint16x4_t vtst_u16 (uint16x4_t a, uint16x4_t b)</para>
        ///   <para>  A32: VTST.16 Dd, Dn, Dm</para>
        ///   <para>  A64: CMTST Vd.4H, Vn.4H, Vm.4H</para>
        /// </summary>
        public static Vector64<ushort> CompareTest(Vector64<ushort> left, Vector64<ushort> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>uint32x2_t vtst_u32 (uint32x2_t a, uint32x2_t b)</para>
        ///   <para>  A32: VTST.32 Dd, Dn, Dm</para>
        ///   <para>  A64: CMTST Vd.2S, Vn.2S, Vm.2S</para>
        /// </summary>
        public static Vector64<uint> CompareTest(Vector64<uint> left, Vector64<uint> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>uint8x16_t vtstq_u8 (uint8x16_t a, uint8x16_t b)</para>
        ///   <para>  A32: VTST.8 Qd, Qn, Qm</para>
        ///   <para>  A64: CMTST Vd.16B, Vn.16B, Vm.16B</para>
        /// </summary>
        public static Vector128<byte> CompareTest(Vector128<byte> left, Vector128<byte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>uint16x8_t vtstq_s16 (int16x8_t a, int16x8_t b)</para>
        ///   <para>  A32: VTST.16 Qd, Qn, Qm</para>
        ///   <para>  A64: CMTST Vd.8H, Vn.8H, Vm.8H</para>
        /// </summary>
        public static Vector128<short> CompareTest(Vector128<short> left, Vector128<short> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>uint32x4_t vtstq_s32 (int32x4_t a, int32x4_t b)</para>
        ///   <para>  A32: VTST.32 Qd, Qn, Qm</para>
        ///   <para>  A64: CMTST Vd.4S, Vn.4S, Vm.4S</para>
        /// </summary>
        public static Vector128<int> CompareTest(Vector128<int> left, Vector128<int> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>uint8x16_t vtstq_s8 (int8x16_t a, int8x16_t b)</para>
        ///   <para>  A32: VTST.8 Qd, Qn, Qm</para>
        ///   <para>  A64: CMTST Vd.16B, Vn.16B, Vm.16B</para>
        /// </summary>
        public static Vector128<sbyte> CompareTest(Vector128<sbyte> left, Vector128<sbyte> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>uint32x4_t vtstq_f32 (float32x4_t a, float32x4_t b)</para>
        ///   <para>  A32: VTST.32 Qd, Qn, Qm</para>
        ///   <para>  A64: CMTST Vd.4S, Vn.4S, Vm.4S</para>
        ///   <para>The above native signature does not exist. We provide this additional overload for consistency with the other scalar APIs.</para>
        /// </summary>
        public static Vector128<float> CompareTest(Vector128<float> left, Vector128<float> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>uint16x8_t vtstq_u16 (uint16x8_t a, uint16x8_t b)</para>
        ///   <para>  A32: VTST.16 Qd, Qn, Qm</para>
        ///   <para>  A64: CMTST Vd.8H, Vn.8H, Vm.8H</para>
        /// </summary>
        public static Vector128<ushort> CompareTest(Vector128<ushort> left, Vector128<ushort> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>uint32x4_t vtstq_u32 (uint32x4_t a, uint32x4_t b)</para>
        ///   <para>  A32: VTST.32 Qd, Qn, Qm</para>
        ///   <para>  A64: CMTST Vd.4S, Vn.4S, Vm.4S</para>
        /// </summary>
        public static Vector128<uint> CompareTest(Vector128<uint> left, Vector128<uint> right) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>uint8x16_t vdupq_n_u8 (uint8_t value)</para>
        ///   <para>  A32: VDUP.8 Qd, Rt</para>
        ///   <para>  A64: DUP Vd.16B, Rn</para>
        /// </summary>
        public static Vector128<byte> DuplicateToVector128(byte value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>int16x8_t vdupq_n_s16 (int16_t value)</para>
        ///   <para>  A32: VDUP.16 Qd, Rt</para>
        ///   <para>  A64: DUP Vd.8H, Rn</para>
        /// </summary>
        public static Vector128<short> DuplicateToVector128(short value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>int32x4_t vdupq_n_s32 (int32_t value)</para>
        ///   <para>  A32: VDUP.32 Qd, Rt</para>
        ///   <para>  A64: DUP Vd.4S, Rn</para>
        /// </summary>
        public static Vector128<int> DuplicateToVector128(int value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>int8x16_t vdupq_n_s8 (int8_t value)</para>
        ///   <para>  A32: VDUP.8 Qd, Rt</para>
        ///   <para>  A64: DUP Vd.16B, Rn</para>
        /// </summary>
        public static Vector128<sbyte> DuplicateToVector128(sbyte value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>float32x4_t vdupq_n_f32 (float32_t value)</para>
        ///   <para>  A32: VDUP Qd, Dm[0]</para>
        ///   <para>  A64: DUP Vd.4S, Vn.S[0]</para>
        /// </summary>
        public static Vector128<float> DuplicateToVector128(float value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>uint16x8_t vdupq_n_u16 (uint16_t value)</para>
        ///   <para>  A32: VDUP.16 Qd, Rt</para>
        ///   <para>  A64: DUP Vd.8H, Rn</para>
        /// </summary>
        public static Vector128<ushort> DuplicateToVector128(ushort value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>uint32x4_t vdupq_n_u32 (uint32_t value)</para>
        ///   <para>  A32: VDUP.32 Qd, Rt</para>
        ///   <para>  A64: DUP Vd.4S, Rn</para>
        /// </summary>
        public static Vector128<uint> DuplicateToVector128(uint value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>uint8x8_t vqmovun_s16 (int16x8_t a)</para>
        ///   <para>  A32: VQMOVUN.S16 Dd, Qm</para>
        ///   <para>  A64: SQXTUN Vd.8B, Vn.8H</para>
        /// </summary>
        public static Vector64<byte> ExtractNarrowingSaturateUnsignedLower(Vector128<short> value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>uint16x4_t vqmovun_s32 (int32x4_t a)</para>
        ///   <para>  A32: VQMOVUN.S32 Dd, Qm</para>
        ///   <para>  A64: SQXTUN Vd.4H, Vn.4S</para>
        /// </summary>
        public static Vector64<ushort> ExtractNarrowingSaturateUnsignedLower(Vector128<int> value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>uint32x2_t vqmovun_s64 (int64x2_t a)</para>
        ///   <para>  A32: VQMOVUN.S64 Dd, Qm</para>
        ///   <para>  A64: SQXTUN Vd.2S, Vn.2D</para>
        /// </summary>
        public static Vector64<uint> ExtractNarrowingSaturateUnsignedLower(Vector128<long> value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>float64x1_t vrnda_f64 (float64x1_t a)</para>
        ///   <para>  A32: VRINTA.F64 Dd, Dm</para>
        ///   <para>  A64: FRINTA Dd, Dn</para>
        /// </summary>
        public static Vector64<double> RoundAwayFromZeroScalar(Vector64<double> value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>float32_t vrndas_f32 (float32_t a)</para>
        ///   <para>  A32: VRINTA.F32 Sd, Sm</para>
        ///   <para>  A64: FRINTA Sd, Sn</para>
        ///   <para>The above native signature does not exist. We provide this additional overload for consistency with the other scalar APIs.</para>
        /// </summary>
        public static Vector64<float> RoundAwayFromZeroScalar(Vector64<float> value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1_u8 (uint8_t * ptr, uint8x8_t val)</para>
        ///   <para>  A32: VST1.8 { Dd }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.8B }, [Xn]</para>
        /// </summary>
        public static unsafe void Store(byte* address, Vector64<byte> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1_f64 (float64_t * ptr, float64x1_t val)</para>
        ///   <para>  A32: VST1.64 { Dd }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.1D }, [Xn]</para>
        /// </summary>
        public static unsafe void Store(double* address, Vector64<double> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1_s16 (int16_t * ptr, int16x4_t val)</para>
        ///   <para>  A32: VST1.16 { Dd }, [Rn]</para>
        ///   <para>  A64: ST1 {Vt.4H }, [Xn]</para>
        /// </summary>
        public static unsafe void Store(short* address, Vector64<short> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1_s32 (int32_t * ptr, int32x2_t val)</para>
        ///   <para>  A32: VST1.32 { Dd }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.2S }, [Xn]</para>
        /// </summary>
        public static unsafe void Store(int* address, Vector64<int> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1_s64 (int64_t * ptr, int64x1_t val)</para>
        ///   <para>  A32: VST1.64 { Dd }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.1D }, [Xn]</para>
        /// </summary>
        public static unsafe void Store(long* address, Vector64<long> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1_s8 (int8_t * ptr, int8x8_t val)</para>
        ///   <para>  A32: VST1.8 { Dd }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.8B }, [Xn]</para>
        /// </summary>
        public static unsafe void Store(sbyte* address, Vector64<sbyte> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1_f32 (float32_t * ptr, float32x2_t val)</para>
        ///   <para>  A32: VST1.32 { Dd }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.2S }, [Xn]</para>
        /// </summary>
        public static unsafe void Store(float* address, Vector64<float> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1_u16 (uint16_t * ptr, uint16x4_t val)</para>
        ///   <para>  A32: VST1.16 { Dd }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.4H }, [Xn]</para>
        /// </summary>
        public static unsafe void Store(ushort* address, Vector64<ushort> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1_u32 (uint32_t * ptr, uint32x2_t val)</para>
        ///   <para>  A32: VST1.32 { Dd }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.2S }, [Xn]</para>
        /// </summary>
        public static unsafe void Store(uint* address, Vector64<uint> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1_u64 (uint64_t * ptr, uint64x1_t val)</para>
        ///   <para>  A32: VST1.64 { Dd }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.1D }, [Xn]</para>
        /// </summary>
        public static unsafe void Store(ulong* address, Vector64<ulong> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1q_u8 (uint8_t * ptr, uint8x16_t val)</para>
        ///   <para>  A32: VST1.8 { Dd, Dd+1 }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.16B }, [Xn]</para>
        /// </summary>
        public static unsafe void Store(byte* address, Vector128<byte> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1q_f64 (float64_t * ptr, float64x2_t val)</para>
        ///   <para>  A32: VST1.64 { Dd, Dd+1 }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.2D }, [Xn]</para>
        /// </summary>
        public static unsafe void Store(double* address, Vector128<double> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1q_s16 (int16_t * ptr, int16x8_t val)</para>
        ///   <para>  A32: VST1.16 { Dd, Dd+1 }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.8H }, [Xn]</para>
        /// </summary>
        public static unsafe void Store(short* address, Vector128<short> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1q_s32 (int32_t * ptr, int32x4_t val)</para>
        ///   <para>  A32: VST1.32 { Dd, Dd+1 }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.4S }, [Xn]</para>
        /// </summary>
        public static unsafe void Store(int* address, Vector128<int> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1q_s64 (int64_t * ptr, int64x2_t val)</para>
        ///   <para>  A32: VST1.64 { Dd, Dd+1 }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.2D }, [Xn]</para>
        /// </summary>
        public static unsafe void Store(long* address, Vector128<long> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1q_s8 (int8_t * ptr, int8x16_t val)</para>
        ///   <para>  A32: VST1.8 { Dd, Dd+1 }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.16B }, [Xn]</para>
        /// </summary>
        public static unsafe void Store(sbyte* address, Vector128<sbyte> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1q_f32 (float32_t * ptr, float32x4_t val)</para>
        ///   <para>  A32: VST1.32 { Dd, Dd+1 }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.4S }, [Xn]</para>
        /// </summary>
        public static unsafe void Store(float* address, Vector128<float> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1q_u16 (uint16_t * ptr, uint16x8_t val)</para>
        ///   <para>  A32: VST1.16 { Dd, Dd+1 }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.8H }, [Xn]</para>
        /// </summary>
        public static unsafe void Store(ushort* address, Vector128<ushort> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1q_u32 (uint32_t * ptr, uint32x4_t val)</para>
        ///   <para>  A32: VST1.32 { Dd, Dd+1 }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.4S }, [Xn]</para>
        /// </summary>
        public static unsafe void Store(uint* address, Vector128<uint> source) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1q_u64 (uint64_t * ptr, uint64x2_t val)</para>
        ///   <para>  A32: VST1.64 { Dd, Dd+1 }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.2D }, [Xn]</para>
        /// </summary>
        public static unsafe void Store(ulong* address, Vector128<ulong> source) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.8B, Vn+1.8B }, [Xn]</summary>
        public static unsafe void Store(byte* address, (Vector64<byte> Value1, Vector64<byte> Value2) value) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.8B, Vn+1.8B }, [Xn]</summary>
        public static unsafe void Store(sbyte* address, (Vector64<sbyte> Value1, Vector64<sbyte> Value2) value) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.4H, Vn+1.4H }, [Xn]</summary>
        public static unsafe void Store(short* address, (Vector64<short> Value1, Vector64<short> Value2) value) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.4H, Vn+1.4H }, [Xn]</summary>
        public static unsafe void Store(ushort* address, (Vector64<ushort> Value1, Vector64<ushort> Value2) value) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.2S, Vn+1.2S }, [Xn]</summary>
        public static unsafe void Store(int* address, (Vector64<int> Value1, Vector64<int> Value2) value) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.2S, Vn+1.2S }, [Xn]</summary>
        public static unsafe void Store(uint* address, (Vector64<uint> Value1, Vector64<uint> Value2) value) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.2S, Vn+1.2S }, [Xn]</summary>
        public static unsafe void Store(float* address, (Vector64<float> Value1, Vector64<float> Value2) value) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.8B, Vn+1.8B, Vn+2.8B }, [Xn]</summary>
        public static unsafe void Store(byte* address, (Vector64<byte> Value1, Vector64<byte> Value2, Vector64<byte> Value3) value) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.8B, Vn+1.8B, Vn+2.8B }, [Xn]</summary>
        public static unsafe void Store(sbyte* address, (Vector64<sbyte> Value1, Vector64<sbyte> Value2, Vector64<sbyte> Value3) value) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.4H, Vn+1.4H, Vn+2.4H }, [Xn]</summary>
        public static unsafe void Store(short* address, (Vector64<short> Value1, Vector64<short> Value2, Vector64<short> Value3) value) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.4H, Vn+1.4H, Vn+2.4H }, [Xn]</summary>
        public static unsafe void Store(ushort* address, (Vector64<ushort> Value1, Vector64<ushort> Value2, Vector64<ushort> Value3) value) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.2S, Vn+1.2S, Vn+2.2S }, [Xn]</summary>
        public static unsafe void Store(int* address, (Vector64<int> Value1, Vector64<int> Value2, Vector64<int> Value3) value) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.2S, Vn+1.2S, Vn+2.2S }, [Xn]</summary>
        public static unsafe void Store(uint* address, (Vector64<uint> Value1, Vector64<uint> Value2, Vector64<uint> Value3) value) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.2S, Vn+1.2S, Vn+2.2S }, [Xn]</summary>
        public static unsafe void Store(float* address, (Vector64<float> Value1, Vector64<float> Value2, Vector64<float> Value3) value) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.8B, Vn+1.8B, Vn+2.8B, Vn+3.8B }, [Xn]</summary>
        public static unsafe void Store(byte* address, (Vector64<byte> Value1, Vector64<byte> Value2, Vector64<byte> Value3, Vector64<byte> Value4) value) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.8B, Vn+1.8B, Vn+2.8B, Vn+3.8B }, [Xn]</summary>
        public static unsafe void Store(sbyte* address, (Vector64<sbyte> Value1, Vector64<sbyte> Value2, Vector64<sbyte> Value3, Vector64<sbyte> Value4) value) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.4H, Vn+1.4H, Vn+2.4H, Vn+3.4H }, [Xn]</summary>
        public static unsafe void Store(short* address, (Vector64<short> Value1, Vector64<short> Value2, Vector64<short> Value3, Vector64<short> Value4) value) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.4H, Vn+1.4H, Vn+2.4H, Vn+3.4H }, [Xn]</summary>
        public static unsafe void Store(ushort* address, (Vector64<ushort> Value1, Vector64<ushort> Value2, Vector64<ushort> Value3, Vector64<ushort> Value4) value) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.2S, Vn+1.2S, Vn+2.2S, Vn+3.2S }, [Xn]</summary>
        public static unsafe void Store(int* address, (Vector64<int> Value1, Vector64<int> Value2, Vector64<int> Value3, Vector64<int> Value4) value) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.2S, Vn+1.2S, Vn+2.2S, Vn+3.2S }, [Xn]</summary>
        public static unsafe void Store(uint* address, (Vector64<uint> Value1, Vector64<uint> Value2, Vector64<uint> Value3, Vector64<uint> Value4) value) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST1 { Vn.2S, Vn+1.2S, Vn+2.2S, Vn+3.2S }, [Xn]</summary>
        public static unsafe void Store(float* address, (Vector64<float> Value1, Vector64<float> Value2, Vector64<float> Value3, Vector64<float> Value4) value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1_lane_u8 (uint8_t * ptr, uint8x8_t val, const int lane)</para>
        ///   <para>  A32: VST1.8 { Dd[index] }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.B }[index], [Xn]</para>
        /// </summary>
        public static unsafe void StoreSelectedScalar(byte* address, Vector64<byte> value, [ConstantExpected(Max = (byte)(7))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1_lane_s16 (int16_t * ptr, int16x4_t val, const int lane)</para>
        ///   <para>  A32: VST1.16 { Dd[index] }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.H }[index], [Xn]</para>
        /// </summary>
        public static unsafe void StoreSelectedScalar(short* address, Vector64<short> value, [ConstantExpected(Max = (byte)(3))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1_lane_s32 (int32_t * ptr, int32x2_t val, const int lane)</para>
        ///   <para>  A32: VST1.32 { Dd[index] }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.S }[index], [Xn]</para>
        /// </summary>
        public static unsafe void StoreSelectedScalar(int* address, Vector64<int> value, [ConstantExpected(Max = (byte)(1))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1_lane_s8 (int8_t * ptr, int8x8_t val, const int lane)</para>
        ///   <para>  A32: VST1.8 { Dd[index] }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.B }[index], [Xn]</para>
        /// </summary>
        public static unsafe void StoreSelectedScalar(sbyte* address, Vector64<sbyte> value, [ConstantExpected(Max = (byte)(7))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1_lane_f32 (float32_t * ptr, float32x2_t val, const int lane)</para>
        ///   <para>  A32: VST1.32 { Dd[index] }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.S }[index], [Xn]</para>
        /// </summary>
        public static unsafe void StoreSelectedScalar(float* address, Vector64<float> value, [ConstantExpected(Max = (byte)(1))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1_lane_u16 (uint16_t * ptr, uint16x4_t val, const int lane)</para>
        ///   <para>  A32: VST1.16 { Dd[index] }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.H }[index], [Xn]</para>
        /// </summary>
        public static unsafe void StoreSelectedScalar(ushort* address, Vector64<ushort> value, [ConstantExpected(Max = (byte)(3))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1_lane_u32 (uint32_t * ptr, uint32x2_t val, const int lane)</para>
        ///   <para>  A32: VST1.32 { Dd[index] }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.S }[index], [Xn]</para>
        /// </summary>
        public static unsafe void StoreSelectedScalar(uint* address, Vector64<uint> value, [ConstantExpected(Max = (byte)(1))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1q_lane_u8 (uint8_t * ptr, uint8x16_t val, const int lane)</para>
        ///   <para>  A32: VST1.8 { Dd[index] }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.B }[index], [Xn]</para>
        /// </summary>
        public static unsafe void StoreSelectedScalar(byte* address, Vector128<byte> value, [ConstantExpected(Max = (byte)(15))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1q_lane_f64 (float64_t * ptr, float64x2_t val, const int lane)</para>
        ///   <para>  A32: VSTR.64 Dd, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.D }[index], [Xn]</para>
        /// </summary>
        public static unsafe void StoreSelectedScalar(double* address, Vector128<double> value, [ConstantExpected(Max = (byte)(1))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1q_lane_s16 (int16_t * ptr, int16x8_t val, const int lane)</para>
        ///   <para>  A32: VST1.16 { Dd[index] }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.H }[index], [Xn]</para>
        /// </summary>
        public static unsafe void StoreSelectedScalar(short* address, Vector128<short> value, [ConstantExpected(Max = (byte)(7))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1q_lane_s32 (int32_t * ptr, int32x4_t val, const int lane)</para>
        ///   <para>  A32: VST1.32 { Dd[index] }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.S }[index], [Xn]</para>
        /// </summary>
        public static unsafe void StoreSelectedScalar(int* address, Vector128<int> value, [ConstantExpected(Max = (byte)(3))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1q_lane_s64 (int64_t * ptr, int64x2_t val, const int lane)</para>
        ///   <para>  A32: VSTR.64 Dd, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.D }[index], [Xn]</para>
        /// </summary>
        public static unsafe void StoreSelectedScalar(long* address, Vector128<long> value, [ConstantExpected(Max = (byte)(1))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1q_lane_s8 (int8_t * ptr, int8x16_t val, const int lane)</para>
        ///   <para>  A32: VST1.8 { Dd[index] }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.B }[index], [Xn]</para>
        /// </summary>
        public static unsafe void StoreSelectedScalar(sbyte* address, Vector128<sbyte> value, [ConstantExpected(Max = (byte)(15))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1q_lane_f32 (float32_t * ptr, float32x4_t val, const int lane)</para>
        ///   <para>  A32: VST1.32 { Dd[index] }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.S }[index], [Xn]</para>
        /// </summary>
        public static unsafe void StoreSelectedScalar(float* address, Vector128<float> value, [ConstantExpected(Max = (byte)(3))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1q_lane_u16 (uint16_t * ptr, uint16x8_t val, const int lane)</para>
        ///   <para>  A32: VST1.16 { Dd[index] }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.H }[index], [Xn]</para>
        /// </summary>
        public static unsafe void StoreSelectedScalar(ushort* address, Vector128<ushort> value, [ConstantExpected(Max = (byte)(7))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1q_lane_u32 (uint32_t * ptr, uint32x4_t val, const int lane)</para>
        ///   <para>  A32: VST1.32 { Dd[index] }, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.S }[index], [Xn]</para>
        /// </summary>
        public static unsafe void StoreSelectedScalar(uint* address, Vector128<uint> value, [ConstantExpected(Max = (byte)(3))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>void vst1q_lane_u64 (uint64_t * ptr, uint64x2_t val, const int lane)</para>
        ///   <para>  A32: VSTR.64 Dd, [Rn]</para>
        ///   <para>  A64: ST1 { Vt.D }[index], [Xn]</para>
        /// </summary>
        public static unsafe void StoreSelectedScalar(ulong* address, Vector128<ulong> value, [ConstantExpected(Max = (byte)(1))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST2 { Vt.8B, Vt+1.8B }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(byte* address, (Vector64<byte> value1, Vector64<byte> value2) value, [ConstantExpected(Max = (byte)(7))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST2 { Vt.8B, Vt+1.8B }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(sbyte* address, (Vector64<sbyte> value1, Vector64<sbyte> value2) value, [ConstantExpected(Max = (byte)(7))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST2 { Vt.4H, Vt+1.4H }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(short* address, (Vector64<short> value1, Vector64<short> value2) value, [ConstantExpected(Max = (byte)(3))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST2 { Vt.4H, Vt+1.4H }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(ushort* address, (Vector64<ushort> value1, Vector64<ushort> value2) value, [ConstantExpected(Max = (byte)(3))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST2 { Vt.2S, Vt+1.2S }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(int* address, (Vector64<int> value1, Vector64<int> value2) value, [ConstantExpected(Max = (byte)(1))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST2 { Vt.2S, Vt+1.2S }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(uint* address, (Vector64<uint> value1, Vector64<uint> value2) value, [ConstantExpected(Max = (byte)(1))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST2 { Vt.2S, Vt+1.2S }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(float* address, (Vector64<float> value1, Vector64<float> value2) value, [ConstantExpected(Max = (byte)(1))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST3 { Vt.8B, Vt+1.8B, Vt+2.8B }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(byte* address, (Vector64<byte> value1, Vector64<byte> value2, Vector64<byte> value3) value, [ConstantExpected(Max = (byte)(7))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST3 { Vt.8B, Vt+1.8B, Vt+2.8B }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(sbyte* address, (Vector64<sbyte> value1, Vector64<sbyte> value2, Vector64<sbyte> value3) value, [ConstantExpected(Max = (byte)(7))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST3 { Vt.4H, Vt+1.4H, Vt+2.4H }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(short* address, (Vector64<short> value1, Vector64<short> value2, Vector64<short> value3) value, [ConstantExpected(Max = (byte)(3))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST3 { Vt.4H, Vt+1.4H, Vt+2.4H }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(ushort* address, (Vector64<ushort> value1, Vector64<ushort> value2,  Vector64<ushort> value3) value, [ConstantExpected(Max = (byte)(3))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST3 { Vt.2S, Vt+1.2S, Vt+2.2S }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(int* address, (Vector64<int> value1, Vector64<int> value2, Vector64<int> value3) value, [ConstantExpected(Max = (byte)(1))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST3 { Vt.2S, Vt+1.2S, Vt+2.2S }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(uint* address, (Vector64<uint> value1, Vector64<uint> value2, Vector64<uint> value3) value, [ConstantExpected(Max = (byte)(1))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST2 { Vt.2S, Vt+1.2S, Vt+2.2S }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(float* address, (Vector64<float> value1, Vector64<float> value2, Vector64<float> value3) value, [ConstantExpected(Max = (byte)(1))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST4 { Vt.8B, Vt+1.8B, Vt+2.8B, Vt+3.8B }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(byte* address, (Vector64<byte> value1, Vector64<byte> value2, Vector64<byte> value3, Vector64<byte> value4) value, [ConstantExpected(Max = (byte)(7))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST4 { Vt.8B, Vt+1.8B, Vt+2.8B, Vt+3.8B }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(sbyte* address, (Vector64<sbyte> value1, Vector64<sbyte> value2, Vector64<sbyte> value3, Vector64<sbyte> value4) value, [ConstantExpected(Max = (byte)(7))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST4 { Vt.4H, Vt+1.4H, Vt+2.4H, Vt+3.4H }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(short* address, (Vector64<short> value1, Vector64<short> value2, Vector64<short> value3, Vector64<short> value4) value, [ConstantExpected(Max = (byte)(3))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST4 { Vt.4H, Vt+1.4H, Vt+2.4H, Vt+3.4H }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(ushort* address, (Vector64<ushort> value1, Vector64<ushort> value2, Vector64<ushort> value3, Vector64<ushort> value4) value, [ConstantExpected(Max = (byte)(3))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST4 { Vt.2S, Vt+1.2S, Vt+2.2S, Vt+3.2S }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(int* address, (Vector64<int> value1, Vector64<int> value2, Vector64<int> value3, Vector64<int> value4) value, [ConstantExpected(Max = (byte)(1))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST4 { Vt.2S, Vt+1.2S, Vt+2.2S, Vt+3.2S }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(uint* address, (Vector64<uint> value1, Vector64<uint> value2, Vector64<uint> value3, Vector64<uint> value4) value, [ConstantExpected(Max = (byte)(1))] byte index) { throw new PlatformNotSupportedException(); }

        /// <summary>  A64: ST4 { Vt.2S, Vt+1.2S, Vt+2.2S, Vt+3.2S }[index], [Xn]</summary>
        public static unsafe void StoreSelectedScalar(float* address, (Vector64<float> value1, Vector64<float> value2, Vector64<float> value3, Vector64<float> value4) value, [ConstantExpected(Max = (byte)(1))] byte index) { throw new PlatformNotSupportedException(); }
    }
    [CLSCompliant(false)]
    public abstract class ArmBase
    {
        public abstract class Arm64
        {
            /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
            /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
            /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
            public static bool IsSupported { [Intrinsic] get => false; }
            internal Arm64() { }
            /// <summary>  A64: CLZ Xd, Xn</summary>
            public static int LeadingZeroCount(long value) { throw new PlatformNotSupportedException(); }

            /// <summary>  A64: CLZ Xd, Xn</summary>
            public static int LeadingZeroCount(ulong value) { throw new PlatformNotSupportedException(); }

            /// <summary>  A64: RBIT Xd, Xn</summary>
            public static long ReverseElementBits(long value) { throw new PlatformNotSupportedException(); }

            /// <summary>  A64: RBIT Xd, Xn</summary>
            public static ulong ReverseElementBits(ulong value) { throw new PlatformNotSupportedException(); }
        }
        /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
        /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
        /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
        public static bool IsSupported { [Intrinsic] get => false; }
        internal ArmBase() { }
        /// <summary>
        ///   <para>  A32: CLZ Rd, Rm</para>
        ///   <para>  A64: CLZ Wd, Wn</para>
        /// </summary>
        public static int LeadingZeroCount(int value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>  A32: CLZ Rd, Rm</para>
        ///   <para>  A64: CLZ Wd, Wn</para>
        /// </summary>
        public static int LeadingZeroCount(uint value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>  A32: RBIT Rd, Rm</para>
        ///   <para>  A64: RBIT Wd, Wn</para>
        /// </summary>
        public static int ReverseElementBits(int value) { throw new PlatformNotSupportedException(); }

        /// <summary>
        ///   <para>  A32: RBIT Rd, Rm</para>
        ///   <para>  A64: RBIT Wd, Wn</para>
        /// </summary>
        public static uint ReverseElementBits(uint value) { throw new PlatformNotSupportedException(); }
    }
}
