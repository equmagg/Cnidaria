using System.Numerics;

namespace Cnidaria.Cs;

// Robison's N-bit unsigned (N-Bit Unsigned Division Via N-Bit Multiply-Add) and Hacker's Delight 10-1 signed division magic
internal static class MagicDivide
{
    public static ulong GetUnsignedMagic(ulong divisor, int wordBits, int numeratorBits, out bool increment, out int preShift, out int postShift)
    {
        ulong mask = wordBits == 64 ? ulong.MaxValue : (1UL << wordBits) - 1;
        int extraShift = wordBits - numeratorBits;
        ulong initialPowerOfTwo = 1UL << (wordBits - 1);
        ulong quotient = initialPowerOfTwo / divisor;
        ulong remainder = initialPowerOfTwo % divisor;
        int ceilLog2 = 64 - BitOperations.LeadingZeroCount(divisor);

        ulong downMultiplier = 0;
        int downExponent = 0;
        bool hasMagicDown = false;

        int exponent;
        for (exponent = 0; ; exponent++)
        {
            if (remainder >= divisor - remainder)
            {
                quotient = unchecked(quotient * 2 + 1) & mask;
                remainder = unchecked(remainder * 2 - divisor);
            }
            else
            {
                quotient = unchecked(quotient * 2) & mask;
                remainder = unchecked(remainder * 2);
            }

            if (exponent + extraShift >= ceilLog2 || divisor - remainder <= 1UL << (exponent + extraShift))
                break;

            if (!hasMagicDown && remainder <= 1UL << (exponent + extraShift))
            {
                hasMagicDown = true;
                downMultiplier = quotient;
                downExponent = exponent;
            }
        }

        if (exponent < ceilLog2)
        {
            increment = false;
            preShift = 0;
            postShift = exponent;
            return (quotient + 1) & mask;
        }

        if ((divisor & 1) != 0)
        {
            increment = true;
            preShift = 0;
            postShift = downExponent;
            return downMultiplier;
        }

        int shift = BitOperations.TrailingZeroCount(divisor);
        ulong result = GetUnsignedMagic(divisor >> shift, wordBits, numeratorBits - shift, out increment, out _, out postShift);
        preShift = shift;
        return result;
    }

    public static long GetSignedMagic(long divisor, int wordBits, out int shift)
    {
        ulong mask = wordBits == 64 ? ulong.MaxValue : (1UL << wordBits) - 1;
        int bitsMinus1 = wordBits - 1;
        ulong twoNMinus1 = 1UL << bitsMinus1;
        ulong absDivisor = unchecked((ulong)(divisor < 0 ? -divisor : divisor)) & mask;
        ulong t = twoNMinus1 + ((unchecked((ulong)divisor) & mask) >> bitsMinus1);
        ulong absNc = t - 1 - t % absDivisor;
        int p = bitsMinus1;
        ulong q1 = twoNMinus1 / absNc;
        ulong r1 = twoNMinus1 - q1 * absNc;
        ulong q2 = twoNMinus1 / absDivisor;
        ulong r2 = twoNMinus1 - q2 * absDivisor;
        ulong delta;

        do
        {
            p++;
            q1 = unchecked(q1 * 2) & mask;
            r1 = unchecked(r1 * 2) & mask;
            if (r1 >= absNc)
            {
                q1 = (q1 + 1) & mask;
                r1 -= absNc;
            }

            q2 = unchecked(q2 * 2) & mask;
            r2 = unchecked(r2 * 2) & mask;
            if (r2 >= absDivisor)
            {
                q2 = (q2 + 1) & mask;
                r2 -= absDivisor;
            }

            delta = absDivisor - r2;
        } while (q1 < delta || (q1 == delta && r1 == 0));

        long magic = SignExtend((q2 + 1) & mask, wordBits);
        if (divisor < 0)
            magic = SignExtend(unchecked((ulong)-magic) & mask, wordBits);
        shift = p - wordBits;
        return magic;
    }

    private static long SignExtend(ulong value, int bits)
        => bits == 64 ? unchecked((long)value) : unchecked((long)(value << (64 - bits))) >> (64 - bits);
}
