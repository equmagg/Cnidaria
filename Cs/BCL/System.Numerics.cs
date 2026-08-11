namespace System.Numerics
{
    public static class BitOperations
    {
        private static ReadOnlySpan<byte> TrailingZeroCountDeBruijn => // 32
        [
            00, 01, 28, 02, 29, 14, 24, 03,
            30, 22, 20, 15, 25, 17, 04, 08,
            31, 27, 13, 23, 21, 19, 16, 07,
            26, 12, 18, 06, 11, 05, 10, 09
        ];

        private static ReadOnlySpan<byte> Log2DeBruijn => // 32
        [
            00, 09, 01, 10, 13, 21, 02, 29,
            11, 14, 16, 18, 22, 25, 03, 30,
            08, 12, 20, 28, 15, 17, 24, 07,
            19, 27, 23, 06, 26, 05, 04, 31
        ];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint RotateLeft(uint value, int offset)
            => (value << offset) | (value >> (32 - offset));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong RotateLeft(ulong value, int offset)
            => (value << offset) | (value >> (64 - offset));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint RotateRight(uint value, int offset)
            => (value >> offset) | (value << (32 - offset));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong RotateRight(ulong value, int offset)
            => (value >> offset) | (value << (64 - offset));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPow2(int value) => (value & (value - 1)) == 0 && value > 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPow2(uint value) => (value & (value - 1)) == 0 && value != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPow2(long value) => (value & (value - 1)) == 0 && value > 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPow2(ulong value) => (value & (value - 1)) == 0 && value != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPow2(nint value) => (value & (value - 1)) == 0 && value > 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPow2(nuint value) => (value & (value - 1)) == 0 && value != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2(nuint value)
        {
#if TARGET_64BIT
            return Log2((ulong)value);
#else
            return Log2((uint)value);
#endif
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2(ulong value)
        {
            value |= 1;

            uint hi = (uint)(value >> 32);

            if (hi == 0)
            {
                return Log2((uint)value);
            }

            return 32 + Log2(hi);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2(uint value)
        {
            // The 0->0 contract is fulfilled by setting the LSB to 1.
            // Log(1) is 0, and setting the LSB for values > 1 does not change the log2 result.
            value |= 1;

            // Fill trailing zeros with ones, eg 00010010 becomes 00011111
            value |= value >> 01;
            value |= value >> 02;
            value |= value >> 04;
            value |= value >> 08;
            value |= value >> 16;

            // Using deBruijn sequence, k=2, n=5 (2^5=32) : 0b_0000_0111_1100_0100_1010_1100_1101_1101u
            return Log2DeBruijn[(int)((value * 0x07C4ACDDu) >> 27)];
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Log2Ceiling(uint value)
        {
            int result = Log2(value);
            if (PopCount(value) != 1)
            {
                result++;
            }
            return result;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Log2Ceiling(ulong value)
        {
            int result = Log2(value);
            if (PopCount(value) != 1)
            {
                result++;
            }
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PopCount(uint value)
        {
            const uint c1 = 0x_55555555u;
            const uint c2 = 0x_33333333u;
            const uint c3 = 0x_0F0F0F0Fu;
            const uint c4 = 0x_01010101u;

            value -= (value >> 1) & c1;
            value = (value & c2) + ((value >> 2) & c2);
            value = (((value + (value >> 4)) & c3) * c4) >> 24;

            return (int)value;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PopCount(ulong value)
        {
#if TARGET_64BIT
            const ulong c1 = 0x_55555555_55555555ul;
            const ulong c2 = 0x_33333333_33333333ul;
            const ulong c3 = 0x_0F0F0F0F_0F0F0F0Ful;
            const ulong c4 = 0x_01010101_01010101ul;

            value -= (value >> 1) & c1;
            value = (value & c2) + ((value >> 2) & c2);
            value = (((value + (value >> 4)) & c3) * c4) >> 56;

            return (int)value;
#else
            return PopCount((uint)value) // lo
                + PopCount((uint)(value >> 32)); // hi
#endif
        }
        public static int PopCount(nuint value)
        {
#if TARGET_64BIT
            return PopCount((ulong)value);
#else
            return PopCount((uint)value);
#endif
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint ResetLowestSetBit(uint value)
        {
            // It's lowered to BLSR on x86
            return value & (value - 1);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong ResetLowestSetBit(ulong value)
        {
            return value & (value - 1);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint FlipBit(uint value, int index)
        {
            return value ^ (1u << index);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong FlipBit(ulong value, int index)
        {
            return value ^ (1ul << index);
        }
    }

    public readonly struct BigInteger
    {
        internal const uint kuMaskHighBit = unchecked((uint)int.MinValue);
        internal const int kcbitUint = 32;
        internal const int kcbitUlong = 64;
        internal const int DecimalScaleFactorMask = 0x00FF0000;

        internal static int MaxLength => Array.MaxLength / kcbitUint;

        internal readonly int _sign; // Do not rename
        internal readonly uint[]? _bits; // Do not rename

        private static readonly BigInteger s_bnMinInt = new BigInteger(-1, new uint[] { kuMaskHighBit });
        private static readonly BigInteger s_bnOneInt = new BigInteger(1);
        private static readonly BigInteger s_bnZeroInt = new BigInteger(0);
        private static readonly BigInteger s_bnMinusOneInt = new BigInteger(-1);

        public BigInteger(int value)
        {
            if (value == int.MinValue)
                this = s_bnMinInt;
            else
            {
                _sign = value;
                _bits = null;
            }

        }

        public BigInteger(uint value)
        {
            if (value <= int.MaxValue)
            {
                _sign = (int)value;
                _bits = null;
            }
            else
            {
                _sign = +1;
                _bits = new uint[1];
                _bits[0] = value;
            }

        }

        public BigInteger(long value)
        {
            if (int.MinValue < value && value <= int.MaxValue)
            {
                _sign = (int)value;
                _bits = null;
            }
            else if (value == int.MinValue)
            {
                this = s_bnMinInt;
            }
            else
            {
                ulong x;
                if (value < 0)
                {
                    x = unchecked((ulong)-value);
                    _sign = -1;
                }
                else
                {
                    x = (ulong)value;
                    _sign = +1;
                }

                if (x <= uint.MaxValue)
                {
                    _bits = new uint[1];
                    _bits[0] = (uint)x;
                }
                else
                {
                    _bits = new uint[2];
                    _bits[0] = unchecked((uint)x);
                    _bits[1] = (uint)(x >> kcbitUint);
                }
            }

        }

        public BigInteger(ulong value)
        {
            if (value <= int.MaxValue)
            {
                _sign = (int)value;
                _bits = null;
            }
            else if (value <= uint.MaxValue)
            {
                _sign = +1;
                _bits = new uint[1];
                _bits[0] = (uint)value;
            }
            else
            {
                _sign = +1;
                _bits = new uint[2];
                _bits[0] = unchecked((uint)value);
                _bits[1] = (uint)(value >> kcbitUint);
            }

        }

        internal BigInteger(int n, uint[]? rgu)
        {
            if ((rgu is not null) && (rgu.Length > MaxLength))
            {
                throw new OverflowException();
            }

            _sign = n;
            _bits = rgu;

        }

        public static BigInteger Zero { get { return s_bnZeroInt; } }

        public static BigInteger One { get { return s_bnOneInt; } }

        public static BigInteger MinusOne { get { return s_bnMinusOneInt; } }


        public bool IsZero { get { return _sign == 0; } }

        public bool IsOne { get { return _sign == 1 && _bits == null; } }

        public bool IsEven { get { return _bits == null ? (_sign & 1) == 0 : (_bits[0] & 1) == 0; } }

        public int Sign
        {
            get { return (_sign >> (kcbitUint - 1)) - (-_sign >> (kcbitUint - 1)); }
        }

        public static int Compare(BigInteger left, BigInteger right)
        {
            return left.CompareTo(right);
        }

        public static BigInteger Abs(BigInteger value)
        {
            return (value >= Zero) ? value : -value;
        }

        public static BigInteger Add(BigInteger left, BigInteger right)
        {
            return left + right;
        }

        public static BigInteger Subtract(BigInteger left, BigInteger right)
        {
            return left - right;
        }

        public static (BigInteger Quotient, BigInteger Remainder) DivRem(BigInteger left, BigInteger right)
        {
            BigInteger quotient = DivRem(left, right, out BigInteger remainder);
            return (quotient, remainder);
        }
        public static BigInteger DivRem(BigInteger dividend, BigInteger divisor, out BigInteger remainder)
        {
            if (divisor.IsZero)
                throw new DivideByZeroException();

            if (dividend.IsZero)
            {
                remainder = Zero;
                return Zero;
            }

            bool quotientNegative = (dividend._sign < 0) ^ (divisor._sign < 0);
            bool remainderNegative = dividend._sign < 0;

            uint[] dividendMagnitude = GetMagnitudeArray(dividend);
            uint[] divisorMagnitude = GetMagnitudeArray(divisor);

            uint[] quotientMagnitude = BigIntegerCalculator.Divide(
                dividendMagnitude,
                divisorMagnitude,
                out uint[] remainderMagnitude);

            remainder = CreateFromMagnitude(remainderMagnitude, remainderNegative);
            return CreateFromMagnitude(quotientMagnitude, quotientNegative);
        }

        public static BigInteger Negate(BigInteger value)
        {
            return -value;
        }

        public static BigInteger Max(BigInteger left, BigInteger right)
        {
            if (left.CompareTo(right) < 0)
                return right;
            return left;
        }

        public static BigInteger Min(BigInteger left, BigInteger right)
        {
            if (left.CompareTo(right) <= 0)
                return left;
            return right;
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return obj is BigInteger other && Equals(other);
        }
        public bool Equals(BigInteger other)
        {
            if (_sign != other._sign)
                return false;

            if (_bits == other._bits)
                return true;

            if (_bits == null || other._bits == null)
                return false;

            int length = BigIntegerCalculator.GetLength(_bits);
            if (length != BigIntegerCalculator.GetLength(other._bits))
                return false;

            for (int i = 0; i < length; i++)
            {
                if (_bits[i] != other._bits[i])
                    return false;
            }

            return true;
        }

        public override int GetHashCode()
        {
            int hash = _sign;

            if (_bits != null)
            {
                int length = BigIntegerCalculator.GetLength(_bits);
                for (int i = 0; i < length; i++)
                    hash = unchecked(hash * 31 + (int)_bits[i]);
            }

            return hash;
        }

        public int CompareTo(long other)
        {
            if (_bits == null)
                return ((long)_sign).CompareTo(other);
            int cu;
            if ((_sign ^ other) < 0 || (cu = _bits.Length) > 2)
                return _sign;
            ulong uu = other < 0 ? (ulong)-other : (ulong)other;
            ulong uuTmp = cu == 2 ? NumericsHelpers.MakeUInt64(_bits[1], _bits[0]) : _bits[0];
            return _sign * uuTmp.CompareTo(uu);
        }
        public int CompareTo(ulong other)
        {
            if (_sign < 0)
                return -1;
            if (_bits == null)
                return ((ulong)_sign).CompareTo(other);
            int cu = _bits.Length;
            if (cu > 2)
                return +1;
            ulong uuTmp = cu == 2 ? NumericsHelpers.MakeUInt64(_bits[1], _bits[0]) : _bits[0];
            return uuTmp.CompareTo(other);
        }
        public int CompareTo(BigInteger other)
        {
            if ((_sign ^ other._sign) < 0)
            {
                // Different signs, so the comparison is easy.
                return _sign < 0 ? -1 : +1;
            }

            // Same signs
            if (_bits == null)
            {
                if (other._bits == null)
                    return _sign < other._sign ? -1 : _sign > other._sign ? +1 : 0;
                return -other._sign;
            }

            if (other._bits == null)
                return _sign;

            int bitsResult = BigIntegerCalculator.Compare(_bits, other._bits);
            return _sign < 0 ? -bitsResult : bitsResult;
        }



        public int CompareTo(object? obj)
        {
            if (obj == null)
                return 1;
            if (obj is not BigInteger bigInt)
                throw new ArgumentException();
            return CompareTo(bigInt);
        }

        public static BigInteger operator <<(BigInteger value, int shift)
        {
            if (shift == 0 || value.IsZero)
                return value;

            if (shift == int.MinValue)
                return (value >> int.MaxValue) >> 1;

            if (shift < 0)
                return value >> -shift;

            uint[] magnitude = GetMagnitudeArray(value);
            uint[] shifted = BigIntegerCalculator.ShiftLeft(magnitude, shift);

            return CreateFromMagnitude(shifted, value._sign < 0);
        }

        public static BigInteger operator >>(BigInteger value, int shift)
        {
            if (shift == 0 || value.IsZero)
                return value;

            if (shift == int.MinValue)
                return (value << int.MaxValue) << 1;

            if (shift < 0)
                return value << -shift;

            uint[] magnitude = GetMagnitudeArray(value);

            if (value._sign >= 0)
            {
                uint[] shifted = BigIntegerCalculator.ShiftRight(magnitude, shift);
                return CreateFromMagnitude(shifted, false);
            }
            else
            {
                // -m >> shift == -ceil(m / 2^shift)
                uint[] shifted = BigIntegerCalculator.ShiftRight(magnitude, shift);

                if (BigIntegerCalculator.HasNonZeroLowerBits(magnitude, shift))
                    shifted = BigIntegerCalculator.Add(shifted, 1u);

                return CreateFromMagnitude(shifted, true);
            }
        }

        private static uint[] GetMagnitudeArray(BigInteger value)
        {
            if (value._bits != null)
                return value._bits;

            if (value._sign == 0)
                return Array.Empty<uint>();

            return new uint[] { AbsAsUInt(value._sign) };
        }

        public static BigInteger operator ~(BigInteger value)
        {
            return -(value + One);
        }

        public static BigInteger operator -(BigInteger value)
        {
            return new BigInteger(-value._sign, value._bits);
        }

        public static BigInteger operator +(BigInteger value)
        {
            return value;
        }

        public static BigInteger operator ++(BigInteger value)
        {
            return value + One;
        }

        public static BigInteger operator --(BigInteger value)
        {
            return value - One;
        }

        public static BigInteger operator +(BigInteger left, BigInteger right)
        {
            if (left._bits == null && right._bits == null)
                return new BigInteger((long)left._sign + (long)right._sign);

            if (left._sign < 0 != right._sign < 0)
                return Subtract(left._bits, left._sign, right._bits, -1 * right._sign);
            return Add(left._bits, left._sign, right._bits, right._sign);
        }

        public static BigInteger operator -(BigInteger left, BigInteger right)
        {
            if (left._bits == null && right._bits == null)
                return new BigInteger((long)left._sign - (long)right._sign);

            if (left._sign < 0 != right._sign < 0)
                return Add(left._bits, left._sign, right._bits, -1 * right._sign);
            return Subtract(left._bits, left._sign, right._bits, right._sign);
        }

        public static BigInteger operator *(BigInteger left, BigInteger right)
        {
            if (left._bits == null && right._bits == null)
                return (long)left._sign * right._sign;

            return Multiply(left._bits, left._sign, right._bits, right._sign);
        }

        public static BigInteger operator /(BigInteger dividend, BigInteger divisor)
        {
            if (divisor.IsZero)
                throw new DivideByZeroException();

            if (dividend.IsZero)
                return Zero;

            bool negative = (dividend._sign < 0) ^ (divisor._sign < 0);

            uint[] quotient = BigIntegerCalculator.Divide(
                GetMagnitudeArray(dividend),
                GetMagnitudeArray(divisor));

            return CreateFromMagnitude(quotient, negative);
        }

        public static BigInteger operator %(BigInteger dividend, BigInteger divisor)
        {
            if (divisor.IsZero)
                throw new DivideByZeroException();

            if (dividend.IsZero)
                return Zero;

            uint[] remainder = BigIntegerCalculator.Remainder(
                GetMagnitudeArray(dividend),
                GetMagnitudeArray(divisor));

            return CreateFromMagnitude(remainder, dividend._sign < 0);
        }

        private static BigInteger Subtract(ReadOnlySpan<uint> leftBits, int leftSign, ReadOnlySpan<uint> rightBits, int rightSign)
        {
            bool trivialLeft = leftBits.IsEmpty;
            bool trivialRight = rightBits.IsEmpty;

            if (trivialLeft && trivialRight)
            {
                return new BigInteger((long)leftSign - (long)rightSign);
            }

            uint[] resultBits;

            if (trivialLeft)
            {
                resultBits = BigIntegerCalculator.Subtract(rightBits, AbsAsUInt(leftSign));
                return CreateFromMagnitude(resultBits, leftSign >= 0);
            }

            if (trivialRight)
            {
                resultBits = BigIntegerCalculator.Subtract(leftBits, AbsAsUInt(rightSign));
                return CreateFromMagnitude(resultBits, leftSign < 0);
            }

            int cmp = BigIntegerCalculator.Compare(leftBits, rightBits);

            if (cmp < 0)
            {
                resultBits = BigIntegerCalculator.Subtract(rightBits, leftBits);
                return CreateFromMagnitude(resultBits, leftSign >= 0);
            }

            resultBits = BigIntegerCalculator.Subtract(leftBits, rightBits);
            return CreateFromMagnitude(resultBits, leftSign < 0);
        }

        private static BigInteger Add(ReadOnlySpan<uint> leftBits, int leftSign, ReadOnlySpan<uint> rightBits, int rightSign)
        {
            bool trivialLeft = leftBits.IsEmpty;
            bool trivialRight = rightBits.IsEmpty;

            if (trivialLeft && trivialRight)
            {
                return new BigInteger((long)leftSign + (long)rightSign);
            }

            uint[] resultBits;

            if (trivialLeft)
            {
                resultBits = BigIntegerCalculator.Add(rightBits, AbsAsUInt(leftSign));
                return CreateFromMagnitude(resultBits, leftSign < 0);
            }

            if (trivialRight)
            {
                resultBits = BigIntegerCalculator.Add(leftBits, AbsAsUInt(rightSign));
                return CreateFromMagnitude(resultBits, leftSign < 0);
            }

            resultBits = BigIntegerCalculator.Add(leftBits, rightBits);
            return CreateFromMagnitude(resultBits, leftSign < 0);
        }

        private static BigInteger Multiply(ReadOnlySpan<uint> left, int leftSign, ReadOnlySpan<uint> right, int rightSign)
        {
            if (leftSign == 0 || rightSign == 0)
                return Zero;

            bool negative = (leftSign < 0) ^ (rightSign < 0);

            if (left.IsEmpty)
            {
                uint small = AbsAsUInt(leftSign);
                uint[] bits = BigIntegerCalculator.Multiply(right, small);
                return CreateFromMagnitude(bits, negative);
            }

            if (right.IsEmpty)
            {
                uint small = AbsAsUInt(rightSign);
                uint[] bits = BigIntegerCalculator.Multiply(left, small);
                return CreateFromMagnitude(bits, negative);
            }

            uint[] result = BigIntegerCalculator.Multiply(left, right);
            return CreateFromMagnitude(result, negative);
        }

        private static uint AbsAsUInt(int value)
        {
            if (value >= 0)
                return (uint)value;

            return (uint)(-((long)value));
        }
        private static BigInteger CreateFromMagnitude(uint[] bits, bool negative)
        {
            int length = BigIntegerCalculator.GetLength(bits);

            if (length == 0)
                return Zero;

            if (length == 1)
            {
                uint value = bits[0];

                if (!negative && value <= int.MaxValue)
                    return new BigInteger((int)value);

                if (negative && value <= 0x80000000u)
                {
                    if (value == 0x80000000u)
                        return new BigInteger(int.MinValue);

                    return new BigInteger(-(int)value);
                }
            }

            uint[] normalized = new uint[length];

            for (int i = 0; i < length; i++)
                normalized[i] = bits[i];

            return new BigInteger(negative ? -1 : 1, normalized);
        }

        public static bool operator <(BigInteger left, BigInteger right)
        {
            return left.CompareTo(right) < 0;
        }

        public static bool operator <=(BigInteger left, BigInteger right)
        {
            return left.CompareTo(right) <= 0;
        }

        public static bool operator >(BigInteger left, BigInteger right)
        {
            return left.CompareTo(right) > 0;
        }
        public static bool operator >=(BigInteger left, BigInteger right)
        {
            return left.CompareTo(right) >= 0;
        }

        public static bool operator ==(BigInteger left, BigInteger right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(BigInteger left, BigInteger right)
        {
            return !left.Equals(right);
        }

        public static bool operator <(BigInteger left, long right)
        {
            return left.CompareTo(right) < 0;
        }

        public static bool operator <=(BigInteger left, long right)
        {
            return left.CompareTo(right) <= 0;
        }

        public static bool operator >(BigInteger left, long right)
        {
            return left.CompareTo(right) > 0;
        }

        public static bool operator >=(BigInteger left, long right)
        {
            return left.CompareTo(right) >= 0;
        }

        public static bool operator ==(BigInteger left, long right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(BigInteger left, long right)
        {
            return !left.Equals(right);
        }

        public static bool operator <(long left, BigInteger right)
        {
            return right.CompareTo(left) > 0;
        }

        public static bool operator <=(long left, BigInteger right)
        {
            return right.CompareTo(left) >= 0;
        }

        public static bool operator >(long left, BigInteger right)
        {
            return right.CompareTo(left) < 0;
        }

        public static bool operator >=(long left, BigInteger right)
        {
            return right.CompareTo(left) <= 0;
        }

        public static bool operator ==(long left, BigInteger right)
        {
            return right.Equals(left);
        }

        public static bool operator !=(long left, BigInteger right)
        {
            return !right.Equals(left);
        }

        public static bool operator <(BigInteger left, ulong right)
        {
            return left.CompareTo(right) < 0;
        }

        public static bool operator <=(BigInteger left, ulong right)
        {
            return left.CompareTo(right) <= 0;
        }

        public static bool operator >(BigInteger left, ulong right)
        {
            return left.CompareTo(right) > 0;
        }

        public static bool operator >=(BigInteger left, ulong right)
        {
            return left.CompareTo(right) >= 0;
        }

        public static bool operator ==(BigInteger left, ulong right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(BigInteger left, ulong right)
        {
            return !left.Equals(right);
        }

        public static bool operator <(ulong left, BigInteger right)
        {
            return right.CompareTo(left) > 0;
        }

        public static bool operator <=(ulong left, BigInteger right)
        {
            return right.CompareTo(left) >= 0;
        }

        public static bool operator >(ulong left, BigInteger right)
        {
            return right.CompareTo(left) < 0;
        }

        public static bool operator >=(ulong left, BigInteger right)
        {
            return right.CompareTo(left) <= 0;
        }

        public static bool operator ==(ulong left, BigInteger right)
        {
            return right.Equals(left);
        }

        public static bool operator !=(ulong left, BigInteger right)
        {
            return !right.Equals(left);
        }


        public static implicit operator BigInteger(int value)
        {
            return new BigInteger(value);
        }

        public static implicit operator BigInteger(long value)
        {
            return new BigInteger(value);
        }

        public static explicit operator byte(BigInteger value)
        {
            return checked((byte)((int)value));
        }

        public static explicit operator char(BigInteger value)
        {
            return checked((char)((int)value));
        }

        public static explicit operator short(BigInteger value)
        {
            return checked((short)((int)value));
        }

        public static explicit operator int(BigInteger value)
        {
            if (value._bits == null)
            {
                return value._sign;  // Value packed into int32 sign
            }
            if (value._bits.Length > 1)
            {
                // More than 32 bits
                throw new OverflowException();
            }
            if (value._sign > 0)
            {
                return checked((int)value._bits[0]);
            }
            if (value._bits[0] > kuMaskHighBit)
            {
                // Value > Int32.MinValue
                throw new OverflowException();
            }
            return unchecked(-(int)value._bits[0]);
        }

        public static explicit operator long(BigInteger value)
        {
            if (value._bits == null)
            {
                return value._sign;
            }

            int len = value._bits.Length;
            if (len > 2)
            {
                throw new OverflowException();
            }

            ulong uu;
            if (len > 1)
            {
                uu = NumericsHelpers.MakeUInt64(value._bits[1], value._bits[0]);
            }
            else
            {
                uu = value._bits[0];
            }

            long ll = value._sign > 0 ? unchecked((long)uu) : unchecked(-(long)uu);
            if ((ll > 0 && value._sign > 0) || (ll < 0 && value._sign < 0))
            {
                // Signs match, no overflow
                return ll;
            }
            throw new OverflowException();
        }
    }
    internal static class BigIntegerCalculator
    {

        internal static int GetLength(uint[] bits)
        {
            int length = bits.Length;

            while (length > 0 && bits[length - 1] == 0)
                length--;

            return length;
        }
        private static int GetLength(ReadOnlySpan<uint> bits)
        {
            int length = bits.Length;

            while (length > 0 && bits[length - 1] == 0)
                length--;

            return length;
        }
        internal static int Compare(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
        {
            int leftLength = GetLength(left);
            int rightLength = GetLength(right);

            if (leftLength < rightLength)
                return -1;

            if (leftLength > rightLength)
                return 1;

            for (int i = leftLength - 1; i >= 0; i--)
            {
                uint l = left[i];
                uint r = right[i];

                if (l < r)
                    return -1;

                if (l > r)
                    return 1;
            }

            return 0;

        }
        internal static uint[] ShiftLeft(ReadOnlySpan<uint> value, int shift)
        {
            int length = GetLength(value);
            if (length == 0)
                return Array.Empty<uint>();

            int digitShift = shift / 32;
            int smallShift = shift & 31;

            long resultLengthLong = (long)length + digitShift + 1;
            if (resultLengthLong > BigInteger.MaxLength)
                throw new OverflowException();

            int resultLength = (int)resultLengthLong;
            uint[] result = new uint[resultLength];

            if (smallShift == 0)
            {
                for (int i = 0; i < length; i++)
                    result[i + digitShift] = value[i];
            }
            else
            {
                int carryShift = 32 - smallShift;
                uint carry = 0;

                for (int i = 0; i < length; i++)
                {
                    uint current = value[i];
                    result[i + digitShift] = (current << smallShift) | carry;
                    carry = current >> carryShift;
                }

                result[length + digitShift] = carry;
            }

            return result;
        }
        internal static uint[] ShiftRight(ReadOnlySpan<uint> value, int shift)
        {
            int length = GetLength(value);
            if (length == 0)
                return Array.Empty<uint>();

            int digitShift = shift / 32;
            int smallShift = shift & 31;

            if (digitShift >= length)
                return Array.Empty<uint>();

            int resultLength = length - digitShift;
            uint[] result = new uint[resultLength];

            if (smallShift == 0)
            {
                for (int i = 0; i < resultLength; i++)
                    result[i] = value[i + digitShift];
            }
            else
            {
                int carryShift = 32 - smallShift;
                uint carry = 0;

                for (int i = length - 1; i >= digitShift; i--)
                {
                    uint current = value[i];
                    result[i - digitShift] = (current >> smallShift) | carry;
                    carry = current << carryShift;
                }
            }

            return result;
        }

        internal static bool HasNonZeroLowerBits(ReadOnlySpan<uint> value, int bitCount)
        {
            int length = GetLength(value);
            if (length == 0 || bitCount <= 0)
                return false;

            int fullWords = bitCount / 32;
            int partialBits = bitCount & 31;

            int wordsToCheck = fullWords < length ? fullWords : length;

            for (int i = 0; i < wordsToCheck; i++)
            {
                if (value[i] != 0)
                    return true;
            }

            if (partialBits != 0 && fullWords < length)
            {
                uint mask = (1u << partialBits) - 1u;
                if ((value[fullWords] & mask) != 0)
                    return true;
            }

            return false;
        }

        internal static uint[] Add(ReadOnlySpan<uint> left, uint right)
        {
            uint[] result = new uint[left.Length + 1];

            ulong carry = right;
            int i = 0;

            for (; i < left.Length; i++)
            {
                ulong sum = (ulong)left[i] + carry;
                result[i] = (uint)sum;
                carry = sum >> 32;

                if (carry == 0)
                {
                    i++;
                    break;
                }
            }

            for (; i < left.Length; i++)
                result[i] = left[i];

            result[left.Length] = (uint)carry;
            return result;
        }
        internal static uint[] Add(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
        {
            if (left.Length < right.Length)
            {
                ReadOnlySpan<uint> tmp = left;
                left = right;
                right = tmp;
            }

            uint[] result = new uint[left.Length + 1];

            ulong carry = 0;
            int i = 0;

            for (; i < right.Length; i++)
            {
                ulong sum = (ulong)left[i] + right[i] + carry;
                result[i] = (uint)sum;
                carry = sum >> 32;
            }

            for (; i < left.Length; i++)
            {
                ulong sum = (ulong)left[i] + carry;
                result[i] = (uint)sum;
                carry = sum >> 32;

                if (carry == 0)
                {
                    i++;
                    break;
                }
            }

            for (; i < left.Length; i++)
                result[i] = left[i];

            result[left.Length] = (uint)carry;
            return result;
        }

        internal static uint[] Subtract(ReadOnlySpan<uint> left, uint right)
        {
            uint[] result = new uint[left.Length];

            ulong borrow = right;
            int i = 0;

            for (; i < left.Length; i++)
            {
                ulong current = left[i];
                ulong diff = current - borrow;

                result[i] = (uint)diff;

                borrow = current < borrow ? 1UL : 0UL;

                if (borrow == 0)
                {
                    i++;
                    break;
                }
            }

            for (; i < left.Length; i++)
                result[i] = left[i];

            return result;
        }
        internal static uint[] Subtract(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
        {
            uint[] result = new uint[left.Length];

            ulong borrow = 0;
            int i = 0;

            for (; i < right.Length; i++)
            {
                ulong subtrahend = (ulong)right[i] + borrow;
                ulong minuend = left[i];
                ulong diff = minuend - subtrahend;

                result[i] = (uint)diff;

                borrow = minuend < subtrahend ? 1UL : 0UL;
            }

            for (; i < left.Length; i++)
            {
                ulong minuend = left[i];
                ulong diff = minuend - borrow;

                result[i] = (uint)diff;

                borrow = minuend < borrow ? 1UL : 0UL;

                if (borrow == 0)
                {
                    i++;
                    break;
                }
            }

            for (; i < left.Length; i++)
                result[i] = left[i];

            return result;
        }

        internal static uint[] Multiply(ReadOnlySpan<uint> left, uint right)
        {
            int leftLength = GetLength(left);

            if (leftLength == 0 || right == 0)
                return Array.Empty<uint>();

            if (leftLength + 1 > BigInteger.MaxLength)
                throw new OverflowException();

            uint[] result = new uint[leftLength + 1];

            ulong carry = 0;

            for (int i = 0; i < leftLength; i++)
            {
                ulong product = ((ulong)left[i] * right) + carry;
                result[i] = (uint)product;
                carry = product >> 32;
            }

            result[leftLength] = (uint)carry;
            return result;
        }
        internal static uint[] Multiply(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
        {
            int leftLength = GetLength(left);
            int rightLength = GetLength(right);

            if (leftLength == 0 || rightLength == 0)
                return Array.Empty<uint>();

            if (leftLength < rightLength)
            {
                ReadOnlySpan<uint> tmp = left;
                left = right;
                right = tmp;

                int tmpLength = leftLength;
                leftLength = rightLength;
                rightLength = tmpLength;
            }

            long resultLengthLong = (long)leftLength + rightLength;
            if (resultLengthLong > BigInteger.MaxLength)
                throw new OverflowException();

            int resultLength = (int)resultLengthLong;
            uint[] result = new uint[resultLength];

            for (int i = 0; i < rightLength; i++)
            {
                ulong r = right[i];
                if (r == 0)
                    continue;

                ulong carry = 0;
                int resultIndex = i;

                for (int j = 0; j < leftLength; j++, resultIndex++)
                {
                    ulong product =
                        ((ulong)left[j] * r) +
                        result[resultIndex] +
                        carry;

                    result[resultIndex] = (uint)product;
                    carry = product >> 32;
                }

                result[i + leftLength] = (uint)carry;
            }

            return result;
        }

        internal static uint[] Divide(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
        {
            return Divide(left, right, out _);
        }
        internal static uint[] Divide(ReadOnlySpan<uint> left, uint right)
        {
            return Divide(left, right, out _);
        }
        internal static uint[] Remainder(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
        {
            Divide(left, right, out uint[] remainder);
            return remainder;
        }
        internal static uint Remainder(ReadOnlySpan<uint> left, uint right)
        {
            Divide(left, right, out uint remainder);
            return remainder;
        }

        internal static uint[] Divide(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right, out uint[] remainder)
        {
            int leftLength = GetLength(left);
            int rightLength = GetLength(right);

            if (rightLength == 0)
                throw new DivideByZeroException();

            if (leftLength == 0)
            {
                remainder = Array.Empty<uint>();
                return Array.Empty<uint>();
            }

            int cmp = Compare(left, right);
            if (cmp < 0)
            {
                remainder = Copy(left, leftLength);
                return Array.Empty<uint>();
            }

            if (cmp == 0)
            {
                remainder = Array.Empty<uint>();
                return new uint[] { 1u };
            }

            if (rightLength == 1)
            {
                uint rest;
                uint[] quotient = Divide(left, right[0], out rest);
                remainder = rest == 0 ? Array.Empty<uint>() : new uint[] { rest };
                return quotient;
            }

            return DivideMultiWord(left, leftLength, right, rightLength, out remainder);
        }

        internal static uint[] Divide(ReadOnlySpan<uint> left, uint right, out uint remainder)
        {
            if (right == 0)
                throw new DivideByZeroException();

            int leftLength = GetLength(left);
            if (leftLength == 0)
            {
                remainder = 0;
                return Array.Empty<uint>();
            }

            uint[] quotient = new uint[leftLength];
            ulong rem = 0;

            for (int i = leftLength - 1; i >= 0; i--)
            {
                ulong value = (rem << 32) | left[i];
                quotient[i] = (uint)(value / right);
                rem = value % right;
            }

            remainder = (uint)rem;
            return quotient;
        }
        private static uint[] DivideMultiWord(
            ReadOnlySpan<uint> dividend,
            int dividendLength,
            ReadOnlySpan<uint> divisor,
            int divisorLength,
            out uint[] remainder)
        {
            int shift = LeadingZeroCount(divisor[divisorLength - 1]);

            uint[] u = new uint[dividendLength + 1];
            uint[] v = new uint[divisorLength + 1];

            LeftShift(dividend, dividendLength, shift, u);
            LeftShift(divisor, divisorLength, shift, v);

            int quotientLength = dividendLength - divisorLength + 1;
            uint[] quotient = new uint[quotientLength];

            const ulong Base = 0x1_0000_0000UL;

            for (int j = quotientLength - 1; j >= 0; j--)
            {
                ulong qhat;
                ulong rhat;

                ulong high = u[j + divisorLength];
                ulong low = u[j + divisorLength - 1];
                ulong divisorHigh = v[divisorLength - 1];

                if (high == divisorHigh)
                {
                    qhat = uint.MaxValue;
                    rhat = low + divisorHigh;
                }
                else
                {
                    ulong value = (high << 32) | low;
                    qhat = value / divisorHigh;
                    rhat = value % divisorHigh;
                }

                if (divisorLength > 1)
                {
                    ulong divisorNext = v[divisorLength - 2];
                    ulong dividendNext = u[j + divisorLength - 2];

                    while (rhat < Base && qhat * divisorNext > ((rhat << 32) | dividendNext))
                    {
                        qhat--;
                        rhat += divisorHigh;
                    }
                }

                bool underflow = SubtractProduct(u, j, v, divisorLength, qhat);

                if (underflow)
                {
                    qhat--;
                    AddBack(u, j, v, divisorLength);
                }

                quotient[j] = (uint)qhat;
            }

            remainder = RightShift(u, divisorLength, shift);
            return quotient;
        }

        private static uint[] Copy(ReadOnlySpan<uint> source, int length)
        {
            if (length == 0)
                return Array.Empty<uint>();

            uint[] result = new uint[length];

            for (int i = 0; i < length; i++)
                result[i] = source[i];

            return result;
        }

        private static bool SubtractProduct(uint[] left, int leftOffset, uint[] right, int rightLength, ulong multiplier)
        {
            ulong carry = 0;
            ulong borrow = 0;

            for (int i = 0; i < rightLength; i++)
            {
                ulong product = (ulong)right[i] * multiplier + carry;
                carry = product >> 32;

                ulong subtrahend = (uint)product + borrow;
                ulong minuend = left[leftOffset + i];

                left[leftOffset + i] = (uint)(minuend - subtrahend);
                borrow = minuend < subtrahend ? 1UL : 0UL;
            }

            ulong high = left[leftOffset + rightLength];
            ulong finalSubtrahend = carry + borrow;

            left[leftOffset + rightLength] = (uint)(high - finalSubtrahend);
            return high < finalSubtrahend;
        }

        private static void AddBack(uint[] left, int leftOffset, uint[] right, int rightLength)
        {
            ulong carry = 0;

            for (int i = 0; i < rightLength; i++)
            {
                ulong sum = (ulong)left[leftOffset + i] + right[i] + carry;
                left[leftOffset + i] = (uint)sum;
                carry = sum >> 32;
            }

            left[leftOffset + rightLength] = (uint)((ulong)left[leftOffset + rightLength] + carry);
        }

        private static void LeftShift(ReadOnlySpan<uint> source, int sourceLength, int shift, uint[] destination)
        {
            if (destination.Length < sourceLength)
                throw new ArgumentException("Destination is too small.");

            if (shift == 0)
            {
                for (int i = 0; i < sourceLength; i++)
                    destination[i] = source[i];

                if (destination.Length > sourceLength)
                    destination[sourceLength] = 0;

                return;
            }

            int inverseShift = 32 - shift;
            uint carry = 0;

            for (int i = 0; i < sourceLength; i++)
            {
                uint value = source[i];
                destination[i] = (value << shift) | carry;
                carry = value >> inverseShift;
            }

            if (destination.Length > sourceLength)
                destination[sourceLength] = carry;
            else if (carry != 0)
                throw new OverflowException();
        }
        private static uint[] RightShift(uint[] source, int sourceLength, int shift)
        {
            uint[] result = new uint[sourceLength];

            if (shift == 0)
            {
                for (int i = 0; i < sourceLength; i++)
                    result[i] = source[i];

                return result;
            }

            int inverseShift = 32 - shift;
            uint carry = 0;

            for (int i = sourceLength - 1; i >= 0; i--)
            {
                uint value = source[i];
                result[i] = (value >> shift) | carry;
                carry = value << inverseShift;
            }

            return result;
        }

        private static int LeadingZeroCount(uint value)
        {
            if (value == 0)
                return 32;

            int count = 0;

            if ((value & 0xFFFF0000u) == 0)
            {
                count += 16;
                value <<= 16;
            }

            if ((value & 0xFF000000u) == 0)
            {
                count += 8;
                value <<= 8;
            }

            if ((value & 0xF0000000u) == 0)
            {
                count += 4;
                value <<= 4;
            }

            if ((value & 0xC0000000u) == 0)
            {
                count += 2;
                value <<= 2;
            }

            if ((value & 0x80000000u) == 0)
                count++;

            return count;
        }
    }
    internal static class NumericsHelpers
    {
        internal static ulong MakeUInt64(uint uHi, uint uLo)
        {
            return ((ulong)uHi << 32) | (ulong)uLo;
        }
        internal static uint Abs(int value)
        {
            if (value >= 0)
                return (uint)value;

            return (uint)(-((long)value));
        }
    }

    public struct Vector2
    {
        internal const int Alignment = 8;
        internal const int ElementCount = 2;

        public float X;
        public float Y;

        public Vector2(float value)
        {
            X = value; Y = value;
        }

        public Vector2(float x, float y)
        {
            X = x; Y = y;
        }
        public static Vector2 One
        {
            get => new Vector2(1.0f);
        }
        public static Vector2 Zero
        {
            get => new Vector2(0.0f);
        }
        public static Vector2 UnitX
        {
            get => new Vector2(1.0f, 0.0f);
        }
        public static Vector2 UnitY
        {
            get => new Vector2(0.0f, 1.0f);
        }
        public override string ToString()
        {
            return $"<{X.ToString()}, {Y.ToString()}>";
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator +(Vector2 left, Vector2 right)
            => new Vector2(left.X + right.X, left.Y + right.Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector2 value)
        {
            this.X += value.X;
            this.Y += value.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator /(Vector2 left, Vector2 right)
        {
            var result = new Vector2(left.X, left.Y);
            result.X /= right.X;
            result.Y /= right.Y;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator /(Vector2 value1, float value2)
        {
            var result = new Vector2(value1.X, value1.Y);
            result.X /= value2;
            result.Y /= value2;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector2 left, Vector2 right) => left.X == right.X && left.Y == right.Y;

        public static bool operator !=(Vector2 left, Vector2 right) => !(left == right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator *(Vector2 left, Vector2 right)
        {
            var result = new Vector2(left.X, left.Y);
            result.X *= right.X;
            result.Y *= right.Y;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(Vector2 value)
        {
            this.X *= value.X;
            this.Y *= value.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator *(Vector2 left, float right)
        {
            var result = new Vector2(left.X, left.Y);
            result.X *= right;
            result.Y *= right;
            return result;
        }

        public static Vector2 operator *(float left, Vector2 right)
        {
            var result = new Vector2(right.X, right.Y);
            result.X *= left;
            result.Y *= left;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(float value)
        {
            this.X *= value;
            this.Y *= value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator -(Vector2 left, Vector2 right)
        {
            var result = new Vector2(left.X, left.Y);
            result.X -= right.X;
            result.Y -= right.Y;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector2 value)
        {
            this.X -= value.X;
            this.Y -= value.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator -(Vector2 value) => new Vector2(-(value.X), -(value.Y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator &(Vector2 left, Vector2 right)
            => new Vector2(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.X) & Unsafe.BitCast<float, int>(right.X)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Y) & Unsafe.BitCast<float, int>(right.Y)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator |(Vector2 left, Vector2 right)
            => new Vector2(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.X) | Unsafe.BitCast<float, int>(right.X)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Y) | Unsafe.BitCast<float, int>(right.Y)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator ^(Vector2 left, Vector2 right)
            => new Vector2(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.X) ^ Unsafe.BitCast<float, int>(right.X)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Y) ^ Unsafe.BitCast<float, int>(right.Y)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator <<(Vector2 value, int shiftAmount)
            => new Vector2(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.X) << shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Y) << shiftAmount));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator ~(Vector2 value)
            => new Vector2(
                Unsafe.BitCast<int, float>(~Unsafe.BitCast<float, int>(value.X)),
                Unsafe.BitCast<int, float>(~Unsafe.BitCast<float, int>(value.Y)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator >>(Vector2 value, int shiftAmount)
            => new Vector2(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.X) >> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Y) >> shiftAmount));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator +(Vector2 value) => value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator >>>(Vector2 value, int shiftAmount)
            => new Vector2(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.X) >>> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Y) >>> shiftAmount));

        public static float Distance(Vector2 value1, Vector2 value2) => float.Sqrt(DistanceSquared(value1, value2));
        public static float DistanceSquared(Vector2 value1, Vector2 value2) => (value1 - value2).LengthSquared();

        public static Vector2 Divide(Vector2 left, Vector2 right) => left / right;
        public static Vector2 Divide(Vector2 left, float divisor) => left / divisor;

        public static Vector2 Multiply(Vector2 left, Vector2 right) => left * right;
        public static Vector2 Multiply(Vector2 left, float right) => left * right;
        public static Vector2 Multiply(float left, Vector2 right) => left * right;

        public static Vector2 Negate(Vector2 value) => -value;

        public static Vector2 Subtract(Vector2 left, Vector2 right) => left - right;
        public static Vector2 Add(Vector2 left, Vector2 right) => left + right;

        public static Vector2 Xor(Vector2 left, Vector2 right) => left ^ right;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Vector2 vector1, Vector2 vector2)
            => vector1.X * vector2.X + vector1.Y * vector2.Y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Cross(Vector2 value1, Vector2 value2)
            => value1.X * value2.Y - value1.Y * value2.X;

        public static (Vector2 Sin, Vector2 Cos) SinCos(Vector2 vector)
        {
            var (sinX, cosX) = MathF.SinCos(vector.X);
            var (sinY, cosY) = MathF.SinCos(vector.Y);

            return (
                new Vector2(sinX, sinY),
                new Vector2(cosX, cosY)
            );
        }

        public override readonly int GetHashCode() => HashCode.Combine(X, Y);

        public readonly float Length() => float.Sqrt(LengthSquared());
        public readonly float LengthSquared() => Dot(this, this);

        public static Vector2 Normalize(Vector2 value) => value / value.Length();
    }
    public struct Vector3
    {
        internal const int Alignment = 8;
        internal const int ElementCount = 3;

        public float X = 0;
        public float Y = 0;
        public float Z = 0;

        public Vector3(float value)
        {
            X = value; Y = value; Z = value;
        }
        public Vector3(float x, float y, float z)
        {
            X = x; Y = y; Z = z;
        }
        public static Vector3 One
        {
            get => new Vector3(1.0f);
        }
        public static Vector3 Zero
        {
            get => new Vector3(0f);
        }
        public static Vector3 UnitX
        {
            get => new Vector3(1.0f, 0f, 0f);
        }
        public static Vector3 UnitY
        {
            get => new Vector3(0.0f, 1.0f, 0f);
        }
        public static Vector3 UnitZ
        {
            get => new Vector3(0f, 0f, 1.0f);
        }
        public override string ToString()
        {
            return $"<{X.ToString()}, {Y.ToString()}, {Z.ToString()}>";
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator +(Vector3 left, Vector3 right)
            => new Vector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector3 value)
        {
            this.X += value.X;
            this.Y += value.Y;
            this.Z += value.Z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator /(Vector3 left, Vector3 right)
        {
            Vector3 result = new Vector3(left.X, left.Y, left.Z);
            result.X /= right.X;
            result.Y /= right.Y;
            result.Z /= right.Z;
            return result;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator /(Vector3 value1, float value2)
        {
            Vector3 result = new Vector3(value1.X, value1.Y, value1.Z);
            value1.X /= value2;
            value1.Y /= value2;
            value1.Z /= value2;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector3 left, Vector3 right)
            => left.X == right.X && left.Y == right.Y && left.Z == right.Z;

        public static bool operator !=(Vector3 left, Vector3 right) => !(left == right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator *(Vector3 left, Vector3 right)
        {
            Vector3 result = new Vector3(left.X, left.Y, left.Z);
            result.X *= right.X;
            result.Y *= right.Y;
            result.Z *= right.Z;
            return result;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(Vector3 value)
        {
            this.X *= value.X;
            this.Y *= value.Y;
            this.Z *= value.Z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator *(Vector3 left, float right)
        {
            Vector3 result = new Vector3(left.X, left.Y, left.Z);
            result.X *= right;
            result.Y *= right;
            result.Z *= right;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator *(float left, Vector3 right)
        {
            Vector3 result = new Vector3(right.X, right.Y, right.Z);
            result.X *= left;
            result.Y *= left;
            result.Z *= left;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(float value)
        {
            this.X *= value;
            this.Y *= value;
            this.Z *= value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator -(Vector3 left, Vector3 right)
        {
            Vector3 result = new Vector3(left.X, left.Y, left.Z);
            result.X -= right.X;
            result.Y -= right.Y;
            result.Z -= right.Z;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector3 value)
        {
            this.X -= value.X;
            this.Y -= value.Y;
            this.Z -= value.Z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator -(Vector3 value) => new Vector3(-(value.X), -(value.Y), -(value.Z));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator &(Vector3 left, Vector3 right)
             => new Vector3(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.X) & Unsafe.BitCast<float, int>(right.X)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Y) & Unsafe.BitCast<float, int>(right.Y)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Z) & Unsafe.BitCast<float, int>(right.Z)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator |(Vector3 left, Vector3 right)
             => new Vector3(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.X) | Unsafe.BitCast<float, int>(right.X)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Y) | Unsafe.BitCast<float, int>(right.Y)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Z) | Unsafe.BitCast<float, int>(right.Z)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator ^(Vector3 left, Vector3 right)
             => new Vector3(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.X) ^ Unsafe.BitCast<float, int>(right.X)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Y) ^ Unsafe.BitCast<float, int>(right.Y)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Z) ^ Unsafe.BitCast<float, int>(right.Z)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator <<(Vector3 value, int shiftAmount)
            => new Vector3(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.X) << shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Y) << shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Z) << shiftAmount));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator ~(Vector3 value)
            => new Vector3(
                Unsafe.BitCast<int, float>(~Unsafe.BitCast<float, int>(value.X)),
                Unsafe.BitCast<int, float>(~Unsafe.BitCast<float, int>(value.Y)),
                Unsafe.BitCast<int, float>(~Unsafe.BitCast<float, int>(value.Z)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator >>(Vector3 value, int shiftAmount)
            => new Vector3(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.X) >> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Y) >> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Z) >> shiftAmount));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator +(Vector3 value) => value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator >>>(Vector3 value, int shiftAmount)
            => new Vector3(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.X) >>> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Y) >>> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Z) >>> shiftAmount));

        public static float Distance(Vector3 value1, Vector3 value2) => float.Sqrt(DistanceSquared(value1, value2));
        public static float DistanceSquared(Vector3 value1, Vector3 value2) => (value1 - value2).LengthSquared();

        public static Vector3 Divide(Vector3 left, Vector3 right) => left / right;
        public static Vector3 Divide(Vector3 left, float divisor) => left / divisor;

        public static Vector3 Multiply(Vector3 left, Vector3 right) => left * right;
        public static Vector3 Multiply(Vector3 left, float right) => left * right;
        public static Vector3 Multiply(float left, Vector3 right) => left * right;

        public static Vector3 Negate(Vector3 value) => -value;

        public static Vector3 Subtract(Vector3 left, Vector3 right) => left - right;
        public static Vector3 Add(Vector3 left, Vector3 right) => left + right;

        public static Vector3 Xor(Vector3 left, Vector3 right) => left ^ right;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Vector3 vector1, Vector3 vector2)
            => vector1.X * vector2.X + vector1.Y * vector2.Y + vector1.Z * vector2.Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Cross(Vector3 vector1, Vector3 vector2)
            => new Vector3(vector1.Y * vector2.Z - vector1.Z * vector2.Y,
                           vector1.Z * vector2.X - vector1.X * vector2.Z,
                           vector1.X * vector2.Y - vector1.Y * vector2.X);

        public static (Vector3 Sin, Vector3 Cos) SinCos(Vector3 vector)
        {
            var (sinX, cosX) = MathF.SinCos(vector.X);
            var (sinY, cosY) = MathF.SinCos(vector.Y);
            var (sinZ, cosZ) = MathF.SinCos(vector.Z);

            return (
                new Vector3(sinX, sinY, sinZ),
                new Vector3(cosX, cosY, cosZ)
            );
        }

        public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z);

        public readonly float Length() => float.Sqrt(LengthSquared());
        public readonly float LengthSquared() => Dot(this, this);

        public static Vector3 Normalize(Vector3 value) => value / value.Length();
    }
    public struct Vector4
    {
        internal const int Alignment = 16;
        internal const int ElementCount = 4;

        public float X;
        public float Y;
        public float Z;
        public float W;


        public Vector4(float value)
        {
            X = value; Y = value; Z = value; W = value;
        }

        public Vector4(Vector2 value, float z, float w)
        {
            X = value.X; Y = value.Y; Z = z; W = w;
        }

        public Vector4(Vector3 value, float w)
        {
            X = value.X; Y = value.Y; Z = value.Z; W = w;
        }

        public Vector4(float x, float y, float z, float w)
        {
            X = x; Y = y; Z = z; W = w;
        }
        public static Vector4 One
        {
            get => new Vector4(1.0f);
        }
        public static Vector4 Zero
        {
            get => new Vector4(0.0f);
        }
        public static Vector4 UnitX
        {
            get => new Vector4(1.0f, 0f, 0f, 0f);
        }
        public static Vector4 UnitY
        {
            get => new Vector4(0.0f, 1.0f, 0f, 0f);
        }
        public static Vector4 UnitZ
        {
            get => new Vector4(0f, 0f, 1.0f, 0f);
        }
        public static Vector4 UnitW
        {
            get => new Vector4(0f, 0f, 0f, 1.0f);
        }
        public override string ToString()
        {
            return $"<{X.ToString()}, {Y.ToString()}, {Z.ToString()}, {W.ToString()}>";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator +(Vector4 left, Vector4 right)
            => new Vector4(left.X + right.X, left.Y + right.Y, left.Z + right.Z, left.W + right.W);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector4 value)
        {
            this.X += value.X;
            this.Y += value.Y;
            this.Z += value.Z;
            this.W += value.W;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator /(Vector4 left, Vector4 right)
        {
            Vector4 result = new Vector4(left.X, left.Y, left.Z, left.W);
            result.X /= right.X;
            result.Y /= right.Y;
            result.Z /= right.Z;
            result.W /= right.W;
            return result;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator /(Vector4 value1, float value2)
        {
            Vector4 result = new Vector4(value1.X, value1.Y, value1.Z, value1.W);
            value1.X /= value2;
            value1.Y /= value2;
            value1.Z /= value2;
            value1.W /= value2;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector4 left, Vector4 right)
            => left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W;

        public static bool operator !=(Vector4 left, Vector4 right) => !(left == right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator *(Vector4 left, Vector4 right)
        {
            Vector4 result = new Vector4(left.X, left.Y, left.Z, left.W);
            result.X *= right.X;
            result.Y *= right.Y;
            result.Z *= right.Z;
            result.W *= right.W;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(Vector4 value)
        {
            this.X *= value.X;
            this.Y *= value.Y;
            this.Z *= value.Z;
            this.W *= value.W;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator *(Vector4 left, float right)
        {
            Vector4 result = new Vector4(left.X, left.Y, left.Z, left.W);
            result.X *= right;
            result.Y *= right;
            result.Z *= right;
            result.W *= right;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator *(float left, Vector4 right)
        {
            Vector4 result = new Vector4(right.X, right.Y, right.Z, right.W);
            result.X *= left;
            result.Y *= left;
            result.Z *= left;
            result.W *= left;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(float value)
        {
            this.X *= value;
            this.Y *= value;
            this.Z *= value;
            this.W *= value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator -(Vector4 left, Vector4 right)
        {
            Vector4 result = new Vector4(left.X, left.Y, left.Z, left.W);
            result.X -= right.X;
            result.Y -= right.Y;
            result.Z -= right.Z;
            result.W -= right.W;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator -=(Vector4 value)
        {
            this.X -= value.X;
            this.Y -= value.Y;
            this.Z -= value.Z;
            this.W -= value.W;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator -(Vector4 value) => new Vector4(-(value.X), -(value.Y), -(value.Z), -(value.W));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator &(Vector4 left, Vector4 right)
            => new Vector4(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.X) & Unsafe.BitCast<float, int>(right.X)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Y) & Unsafe.BitCast<float, int>(right.Y)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Z) & Unsafe.BitCast<float, int>(right.Z)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.W) & Unsafe.BitCast<float, int>(right.W)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator |(Vector4 left, Vector4 right)
            => new Vector4(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.X) | Unsafe.BitCast<float, int>(right.X)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Y) | Unsafe.BitCast<float, int>(right.Y)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Z) | Unsafe.BitCast<float, int>(right.Z)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.W) | Unsafe.BitCast<float, int>(right.W)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator ^(Vector4 left, Vector4 right)
            => new Vector4(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.X) ^ Unsafe.BitCast<float, int>(right.X)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Y) ^ Unsafe.BitCast<float, int>(right.Y)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Z) ^ Unsafe.BitCast<float, int>(right.Z)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.W) ^ Unsafe.BitCast<float, int>(right.W)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator <<(Vector4 value, int shiftAmount)
            => new Vector4(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.X) << shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Y) << shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Z) << shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.W) << shiftAmount));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator ~(Vector4 value)
            => new Vector4(
                Unsafe.BitCast<int, float>(~Unsafe.BitCast<float, int>(value.X)),
                Unsafe.BitCast<int, float>(~Unsafe.BitCast<float, int>(value.Y)),
                Unsafe.BitCast<int, float>(~Unsafe.BitCast<float, int>(value.Z)),
                Unsafe.BitCast<int, float>(~Unsafe.BitCast<float, int>(value.W)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator >>(Vector4 value, int shiftAmount)
            => new Vector4(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.X) >> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Y) >> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Z) >> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.W) >> shiftAmount));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator +(Vector4 value) => value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator >>>(Vector4 value, int shiftAmount)
            => new Vector4(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.X) >>> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Y) >>> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Z) >>> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.W) >>> shiftAmount));

        public static float Distance(Vector4 value1, Vector4 value2) => float.Sqrt(DistanceSquared(value1, value2));
        public static float DistanceSquared(Vector4 value1, Vector4 value2) => (value1 - value2).LengthSquared();

        public static Vector4 Divide(Vector4 left, Vector4 right) => left / right;
        public static Vector4 Divide(Vector4 left, float divisor) => left / divisor;

        public static Vector4 Multiply(Vector4 left, Vector4 right) => left * right;
        public static Vector4 Multiply(Vector4 left, float right) => left * right;
        public static Vector4 Multiply(float left, Vector4 right) => left * right;

        public static Vector4 Negate(Vector4 value) => -value;

        public static Vector4 Subtract(Vector4 left, Vector4 right) => left - right;
        public static Vector4 Add(Vector4 left, Vector4 right) => left + right;

        public static Vector4 Xor(Vector4 left, Vector4 right) => left ^ right;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Vector4 vector1, Vector4 vector2)
            => vector1.X * vector2.X + vector1.Y * vector2.Y + vector1.Z * vector2.Z + vector1.W * vector2.W;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Cross(Vector4 vector1, Vector4 vector2)
            => new Vector4(vector1.Y * vector2.Z - vector1.Z * vector2.Y,
                           vector1.Z * vector2.X - vector1.X * vector2.Z,
                           vector1.X * vector2.Y - vector1.Y * vector2.X,
                           vector1.W * vector2.W);

        public static (Vector4 Sin, Vector4 Cos) SinCos(Vector4 vector)
        {
            var (sinX, cosX) = MathF.SinCos(vector.X);
            var (sinY, cosY) = MathF.SinCos(vector.Y);
            var (sinZ, cosZ) = MathF.SinCos(vector.Z);
            var (sinW, cosW) = MathF.SinCos(vector.W);

            return (
                new Vector4(sinX, sinY, sinZ, sinW),
                new Vector4(cosX, cosY, cosZ, cosW)
            );
        }

        public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z, W);

        public readonly float Length() => float.Sqrt(LengthSquared());
        public readonly float LengthSquared() => Dot(this, this);

        public static Vector4 Normalize(Vector4 value) => value / value.Length();
    }

    public struct Quaternion : IEquatable<Quaternion>
    {
        /// <summary>The X value of the vector component of the quaternion.</summary>
        public float X;

        /// <summary>The Y value of the vector component of the quaternion.</summary>
        public float Y;

        /// <summary>The Z value of the vector component of the quaternion.</summary>
        public float Z;

        /// <summary>The rotation component of the quaternion.</summary>
        public float W;

        internal const int Count = 4;

        public Quaternion(float x, float y, float z, float w)
        {
            X = x; Y = y; Z = z; W = w;
        }
        public Quaternion(Vector3 vectorPart, float scalarPart)
        {
            this = Create(vectorPart, scalarPart);
        }

        public static Quaternion Zero
        {
            get => default;
        }
        public static Quaternion Identity
        {
            get => Create(0.0f, 0.0f, 0.0f, 1.0f);
        }

        public static Quaternion Create(float x, float y, float z, float w) => new Quaternion(x, y, z, w);
        public static Quaternion Create(Vector3 vectorPart, float scalarPart) => new Quaternion(vectorPart.X, vectorPart.Y, vectorPart.Z, scalarPart);

        public static Quaternion CreateFromRotationMatrix(Matrix4x4 matrix)
        {
            float trace = matrix.M11 + matrix.M22 + matrix.M33;

            Quaternion q = default;

            if (trace > 0.0f)
            {
                float s = float.Sqrt(trace + 1.0f);
                q.W = s * 0.5f;
                s = 0.5f / s;
                q.X = (matrix.M23 - matrix.M32) * s;
                q.Y = (matrix.M31 - matrix.M13) * s;
                q.Z = (matrix.M12 - matrix.M21) * s;
            }
            else
            {
                if (matrix.M11 >= matrix.M22 && matrix.M11 >= matrix.M33)
                {
                    float s = float.Sqrt(1.0f + matrix.M11 - matrix.M22 - matrix.M33);
                    float invS = 0.5f / s;
                    q.X = 0.5f * s;
                    q.Y = (matrix.M12 + matrix.M21) * invS;
                    q.Z = (matrix.M13 + matrix.M31) * invS;
                    q.W = (matrix.M23 - matrix.M32) * invS;
                }
                else if (matrix.M22 > matrix.M33)
                {
                    float s = float.Sqrt(1.0f + matrix.M22 - matrix.M11 - matrix.M33);
                    float invS = 0.5f / s;
                    q.X = (matrix.M21 + matrix.M12) * invS;
                    q.Y = 0.5f * s;
                    q.Z = (matrix.M32 + matrix.M23) * invS;
                    q.W = (matrix.M31 - matrix.M13) * invS;
                }
                else
                {
                    float s = float.Sqrt(1.0f + matrix.M33 - matrix.M11 - matrix.M22);
                    float invS = 0.5f / s;
                    q.X = (matrix.M31 + matrix.M13) * invS;
                    q.Y = (matrix.M32 + matrix.M23) * invS;
                    q.Z = 0.5f * s;
                    q.W = (matrix.M12 - matrix.M21) * invS;
                }
            }

            return q;
        }
        public static Quaternion CreateFromYawPitchRoll(float yaw, float pitch, float roll)
        {
            (Vector3 sin, Vector3 cos) = Vector3.SinCos((new Vector3(roll, pitch, yaw)) * 0.5f);

            (float sr, float cr) = (sin.X, cos.X);
            (float sp, float cp) = (sin.Y, cos.Y);
            (float sy, float cy) = (sin.Z, cos.Z);

            Quaternion result;

            result.X = cy * sp * cr + sy * cp * sr;
            result.Y = sy * cp * cr - cy * sp * sr;
            result.Z = cy * cp * sr - sy * sp * cr;
            result.W = cy * cp * cr + sy * sp * sr;

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Quaternion quaternion1, Quaternion quaternion2)
            => quaternion1.X * quaternion2.X +
               quaternion1.Y * quaternion2.Y +
               quaternion1.Z * quaternion2.Z +
               quaternion1.W * quaternion2.W;

        public static bool operator ==(Quaternion value1, Quaternion value2)
            => value1.X == value2.X && value1.Y == value2.Y && value1.Z == value2.Z && value1.W == value2.W;
        public static bool operator !=(Quaternion value1, Quaternion value2) => !(value1 == value2);

        public readonly float Length() => float.Sqrt(LengthSquared());
        public readonly float LengthSquared() => Dot(this, this);

        public override readonly bool Equals([NotNullWhen(true)] object? obj) => (obj is Quaternion other) && Equals(other);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Equals(Quaternion other) => this == other;
        public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z, W);
        public override readonly string ToString() => $"{{X:{X} Y:{Y} Z:{Z} W:{W}}}";
    }

    public struct Matrix3x2
    {
        private const int RowCount = 3;
        private const int ColumnCount = 2;

        public float M11;
        public float M12;
        public float M21;
        public float M22;
        public float M31;
        public float M32;

        public Matrix3x2(float m11, float m12,
                         float m21, float m22,
                         float m31, float m32)
        {
            M11 = m11; M12 = m12;
            M21 = m21; M22 = m22;
            M31 = m31; M32 = m32;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix3x2 Create(float m11, float m12,
                                       float m21, float m22,
                                       float m31, float m32)
            => new Matrix3x2(m11, m12, m21, m22, m31, m32);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix3x2 Create(Vector2 x, Vector2 y, Vector2 z)
            => new Matrix3x2(x.X, x.Y, y.X, y.Y, z.X, z.Y);

        public Vector2 X
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector2(M11, M12);
        }
        public Vector2 Y
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector2(M21, M22);
        }
        public Vector2 Z
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector2(M31, M32);
        }
    }
    public struct Matrix4x4
    {
        internal const int RowCount = 4;
        internal const int ColumnCount = 4;

        public float M11;
        public float M12;
        public float M13;
        public float M14;

        public float M21;
        public float M22;
        public float M23;
        public float M24;

        public float M31;
        public float M32;
        public float M33;
        public float M34;

        public float M41;
        public float M42;
        public float M43;
        public float M44;

        public Matrix4x4(float m11, float m12, float m13, float m14,
                         float m21, float m22, float m23, float m24,
                         float m31, float m32, float m33, float m34,
                         float m41, float m42, float m43, float m44)
        {
            M11 = m11; M12 = m12; M13 = m13; M14 = m14;
            M21 = m21; M22 = m22; M23 = m23; M24 = m24;
            M31 = m31; M32 = m32; M33 = m33; M34 = m34;
            M41 = m41; M42 = m42; M43 = m43; M44 = m44;
        }
        public static Matrix4x4 Identity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Create(Vector4.UnitX, Vector4.UnitY, Vector4.UnitZ, Vector4.UnitW);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 Create(Vector4 x, Vector4 y, Vector4 z, Vector4 w)
            => new Matrix4x4(x.X, x.Y, x.Z, x.W,
                             y.X, y.Y, y.Z, y.W,
                             z.X, z.Y, z.Z, z.W,
                             w.X, w.Y, w.Z, w.W);
        public Vector4 X
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector4(M11, M12, M13, M14);
        }
        public Vector4 Y
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector4(M21, M22, M23, M24);
        }
        public Vector4 Z
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector4(M31, M32, M33, M34);
        }
        public Vector4 W
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector4(M41, M42, M43, M44);
        }
    }
}