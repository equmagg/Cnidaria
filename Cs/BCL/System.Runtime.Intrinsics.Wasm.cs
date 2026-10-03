using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace System.Runtime.Intrinsics.Wasm
{
    internal abstract class WasmBase
    {
        /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
        /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
        /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
        public static bool IsSupported => false;
        /// <summary>  i32.clz</summary>
        public static int LeadingZeroCount(int value) { throw new PlatformNotSupportedException(); }

        /// <summary>  i32.clz</summary>
        public static int LeadingZeroCount(uint value) { throw new PlatformNotSupportedException(); }

        /// <summary>  i64.clz</summary>
        public static int LeadingZeroCount(long value) { throw new PlatformNotSupportedException(); }

        /// <summary>  i64.clz</summary>
        public static int LeadingZeroCount(ulong value) { throw new PlatformNotSupportedException(); }

        /// <summary>  i32.ctz</summary>
        public static int TrailingZeroCount(int value) { throw new PlatformNotSupportedException(); }

        /// <summary>  i32.ctz</summary>
        public static int TrailingZeroCount(uint value) { throw new PlatformNotSupportedException(); }

        /// <summary>  i64.ctz</summary>
        public static int TrailingZeroCount(long value) { throw new PlatformNotSupportedException(); }

        /// <summary>  i64.ctz</summary>
        public static int TrailingZeroCount(ulong value) { throw new PlatformNotSupportedException(); }
    }
    [CLSCompliant(false)]
    public abstract class PackedSimd
    {
        /// <summary>Gets a value that indicates whether the APIs in this class are supported.</summary>
        /// <value><see langword="true" /> if the APIs are supported; otherwise, <see langword="false" />.</value>
        /// <remarks>A value of <see langword="false" /> indicates that the APIs will throw <see cref="PlatformNotSupportedException" />.</remarks>
        public static bool IsSupported { [Intrinsic] get { return false; } }
        public static Vector128<byte>   ConvertNarrowingSaturateUnsigned(Vector128<short> lower, Vector128<short> upper) { throw new PlatformNotSupportedException(); }

        public static Vector128<ushort> ConvertNarrowingSaturateUnsigned(Vector128<int>   lower, Vector128<int>   upper) { throw new PlatformNotSupportedException(); }
    }
}
