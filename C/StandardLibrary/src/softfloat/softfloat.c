#include <stdint.h>

// libgcc's routines for the formats SoftFloat.IsSoftware names, IEEE 754 with ties to even and without exception flags

#if defined(__ARM_32BIT_STATE) || defined(__riscv) && (!defined(__riscv_flen) || __riscv_flen < 64)
#define SOFT_DOUBLE 1
#endif
#if defined(__ARM_32BIT_STATE) || defined(__riscv) && !defined(__riscv_flen)
#define SOFT_FLOAT 1
#endif
#if defined(__riscv) && __riscv_xlen == 32
#define WIDE_CONVERSIONS 1
#endif

#if defined(SOFT_DOUBLE) || defined(WIDE_CONVERSIONS)

#define DOUBLE_SIGN 0x8000000000000000ull
#define DOUBLE_INFINITY 0x7FF0000000000000ull
#define DOUBLE_NAN 0x7FF8000000000000ull
#define DOUBLE_FRACTION 0x000FFFFFFFFFFFFFull
#define DOUBLE_IMPLICIT 0x0010000000000000ull
#define FLOAT_SIGN 0x80000000u
#define FLOAT_INFINITY 0x7F800000u
#define FLOAT_NAN 0x7FC00000u

typedef union
{
    double value;
    uint64_t bits;
} double_cell;

typedef union
{
    float value;
    uint32_t bits;
} float_cell;

static uint64_t double_bits(double value)
{
    double_cell cell;
    cell.value = value;
    return cell.bits;
}

static double double_of(uint64_t bits)
{
    double_cell cell;
    cell.bits = bits;
    return cell.value;
}

static uint32_t float_bits(float value)
{
    float_cell cell;
    cell.value = value;
    return cell.bits;
}

static float float_of(uint32_t bits)
{
    float_cell cell;
    cell.bits = bits;
    return cell.value;
}

static int exponent_of(uint64_t bits)
{
    return (int)(bits >> 52) & 0x7FF;
}

static int leading_zeros(uint64_t value)
{
    int count = 0;
    if ((value >> 32) == 0)
    {
        count += 32;
        value <<= 32;
    }
    if ((value >> 48) == 0)
    {
        count += 16;
        value <<= 16;
    }
    if ((value >> 56) == 0)
    {
        count += 8;
        value <<= 8;
    }
    if ((value >> 60) == 0)
    {
        count += 4;
        value <<= 4;
    }
    if ((value >> 62) == 0)
    {
        count += 2;
        value <<= 2;
    }
    return count + (int)(1 - (value >> 63));
}

// Shifts right by at least one place and keeps whether any lost bit was set in bit zero
static uint64_t shift_right_jam(uint64_t value, int distance)
{
    if (distance >= 63)
        return value != 0;
    return value >> distance | (uint64_t)((value << (64 - distance)) != 0);
}

// The significand's leading bit is bit 62, and adding it into the exponent field raises the biased exponent given by one
static uint64_t round_pack(uint64_t sign, int exponent, uint64_t significand)
{
    if ((unsigned)exponent >= 0x7FD)
    {
        if (exponent < 0)
        {
            significand = shift_right_jam(significand, -exponent);
            exponent = 0;
        }
        else if (exponent > 0x7FD || significand + 0x200 >= DOUBLE_SIGN)
            return sign | DOUBLE_INFINITY;
    }

    uint64_t rest = significand & 0x3FF;
    significand = (significand + 0x200) >> 10;
    if (rest == 0x200)
        significand &= ~1ull;
    return sign + ((uint64_t)exponent << 52) + significand;
}

static uint64_t normalize_round_pack(uint64_t sign, int exponent, uint64_t significand)
{
    int shift = leading_zeros(significand) - 1;
    exponent -= shift;
    if (shift >= 10 && (unsigned)exponent < 0x7FD)
        return sign + ((uint64_t)exponent << 52) + (significand << (shift - 10));
    return round_pack(sign, exponent, significand << shift);
}

static uint64_t double_from_magnitude(uint64_t sign, uint64_t magnitude)
{
    if (magnitude == 0)
        return 0;
    if (magnitude >> 63)
        return round_pack(sign, 0x43D, shift_right_jam(magnitude, 1));
    return normalize_round_pack(sign, 0x43C, magnitude);
}

static uint32_t round_pack_float(uint32_t sign, int exponent, uint32_t significand)
{
    if ((unsigned)exponent >= 0xFD)
    {
        if (exponent < 0)
        {
            significand = (uint32_t)shift_right_jam(significand, -exponent);
            exponent = 0;
        }
        else if (exponent > 0xFD || significand + 0x40 >= FLOAT_SIGN)
            return sign | FLOAT_INFINITY;
    }

    uint32_t rest = significand & 0x7F;
    significand = (significand + 0x40) >> 7;
    if (rest == 0x40)
        significand &= ~1u;
    return sign + ((uint32_t)exponent << 23) + significand;
}

static uint32_t float_from_magnitude(uint32_t sign, uint64_t magnitude)
{
    if (magnitude == 0)
        return 0;
    int shift = leading_zeros(magnitude) - 40;
    if (shift >= 0)
        return sign + ((uint32_t)(0x95 - shift) << 23) + ((uint32_t)magnitude << shift);
    shift += 7;
    uint32_t significand = shift < 0 ? (uint32_t)shift_right_jam(magnitude, -shift) : (uint32_t)magnitude << shift;
    return round_pack_float(sign, 0x9C - shift, significand);
}

static uint64_t widen_float(uint32_t bits)
{
    uint64_t sign = (uint64_t)(bits & FLOAT_SIGN) << 32;
    int exponent = (int)(bits >> 23) & 0xFF;
    uint64_t fraction = bits & 0x7FFFFFu;
    if (exponent == 0xFF)
        return fraction != 0 ? DOUBLE_NAN : sign | DOUBLE_INFINITY;
    if (exponent == 0)
    {
        if (fraction == 0)
            return sign;
        int shift = leading_zeros(fraction) - 40;
        fraction = (fraction << shift) & 0x7FFFFFu;
        exponent = 1 - shift;
    }
    return sign | (uint64_t)(exponent + 0x380) << 52 | fraction << 29;
}

static uint64_t truncated_magnitude(uint64_t bits, int width, uint64_t limit)
{
    int exponent = exponent_of(bits);
    if (exponent < 0x3FF)
        return 0;
    if (exponent >= 0x3FF + width)
        return limit;
    uint64_t significand = (bits & DOUBLE_FRACTION) | DOUBLE_IMPLICIT;
    int shift = exponent - 0x433;
    return shift >= 0 ? significand << shift : significand >> -shift;
}

static long long signed_from_bits(uint64_t bits)
{
    if (bits & DOUBLE_SIGN)
        return (long long)(0 - truncated_magnitude(bits, 63, DOUBLE_SIGN));
    return (long long)truncated_magnitude(bits, 63, DOUBLE_SIGN - 1);
}

static unsigned long long unsigned_from_bits(uint64_t bits)
{
    return bits & DOUBLE_SIGN ? 0 : truncated_magnitude(bits, 64, ~0ull);
}

static uint64_t magnitude_of(long long value)
{
    return value < 0 ? 0 - (uint64_t)value : (uint64_t)value;
}

double __floatdidf(long long value)
{
    return double_of(double_from_magnitude(value < 0 ? DOUBLE_SIGN : 0, magnitude_of(value)));
}

double __floatundidf(unsigned long long value)
{
    return double_of(double_from_magnitude(0, value));
}

long long __fixdfdi(double value)
{
    return signed_from_bits(double_bits(value));
}

unsigned long long __fixunsdfdi(double value)
{
    return unsigned_from_bits(double_bits(value));
}

float __floatdisf(long long value)
{
    return float_of(float_from_magnitude(value < 0 ? FLOAT_SIGN : 0, magnitude_of(value)));
}

float __floatundisf(unsigned long long value)
{
    return float_of(float_from_magnitude(0, value));
}

long long __fixsfdi(float value)
{
    return signed_from_bits(widen_float(float_bits(value)));
}

unsigned long long __fixunssfdi(float value)
{
    return unsigned_from_bits(widen_float(float_bits(value)));
}

#endif

#if defined(SOFT_DOUBLE)

static uint64_t add_magnitudes(uint64_t x, uint64_t y, uint64_t sign)
{
    int xe = exponent_of(x);
    int ye = exponent_of(y);
    uint64_t xs = x & DOUBLE_FRACTION;
    uint64_t ys = y & DOUBLE_FRACTION;
    int difference = xe - ye;
    if (difference == 0)
    {
        if (xe == 0)
            return x + ys;
        if (xe == 0x7FF)
            return (xs | ys) != 0 ? DOUBLE_NAN : x;
        return round_pack(sign, xe, (2 * DOUBLE_IMPLICIT + xs + ys) << 9);
    }

    int exponent;
    xs <<= 9;
    ys <<= 9;
    if (difference < 0)
    {
        if (ye == 0x7FF)
            return ys != 0 ? DOUBLE_NAN : sign | DOUBLE_INFINITY;
        exponent = ye;
        xs = xe != 0 ? xs + 0x2000000000000000ull : xs << 1;
        xs = shift_right_jam(xs, -difference);
    }
    else
    {
        if (xe == 0x7FF)
            return xs != 0 ? DOUBLE_NAN : x;
        exponent = xe;
        ys = ye != 0 ? ys + 0x2000000000000000ull : ys << 1;
        ys = shift_right_jam(ys, difference);
    }

    uint64_t sum = 0x2000000000000000ull + xs + ys;
    if (sum < 0x4000000000000000ull)
    {
        exponent--;
        sum <<= 1;
    }
    return round_pack(sign, exponent, sum);
}

static uint64_t subtract_magnitudes(uint64_t x, uint64_t y, uint64_t sign)
{
    int xe = exponent_of(x);
    int ye = exponent_of(y);
    uint64_t xs = x & DOUBLE_FRACTION;
    uint64_t ys = y & DOUBLE_FRACTION;
    int difference = xe - ye;
    if (difference == 0)
    {
        if (xe == 0x7FF)
            return DOUBLE_NAN;
        int64_t delta = (int64_t)(xs - ys);
        if (delta == 0)
            return 0;
        if (xe != 0)
            xe--;
        if (delta < 0)
        {
            sign ^= DOUBLE_SIGN;
            delta = -delta;
        }
        int shift = leading_zeros((uint64_t)delta) - 11;
        int exponent = xe - shift;
        if (exponent < 0)
        {
            shift = xe;
            exponent = 0;
        }
        return sign + ((uint64_t)exponent << 52) + ((uint64_t)delta << shift);
    }

    int exponent;
    uint64_t result;
    xs <<= 10;
    ys <<= 10;
    if (difference < 0)
    {
        sign ^= DOUBLE_SIGN;
        if (ye == 0x7FF)
            return ys != 0 ? DOUBLE_NAN : sign | DOUBLE_INFINITY;
        xs += xe != 0 ? 0x4000000000000000ull : xs;
        xs = shift_right_jam(xs, -difference);
        exponent = ye;
        result = (ys | 0x4000000000000000ull) - xs;
    }
    else
    {
        if (xe == 0x7FF)
            return xs != 0 ? DOUBLE_NAN : x;
        ys += ye != 0 ? 0x4000000000000000ull : ys;
        ys = shift_right_jam(ys, difference);
        exponent = xe;
        result = (xs | 0x4000000000000000ull) - ys;
    }
    return normalize_round_pack(sign, exponent - 1, result);
}

static uint64_t add_bits(uint64_t x, uint64_t y)
{
    uint64_t sign = x & DOUBLE_SIGN;
    return ((x ^ y) & DOUBLE_SIGN) != 0 ? subtract_magnitudes(x, y, sign) : add_magnitudes(x, y, sign);
}

double __adddf3(double a, double b)
{
    return double_of(add_bits(double_bits(a), double_bits(b)));
}

double __subdf3(double a, double b)
{
    return double_of(add_bits(double_bits(a), double_bits(b) ^ DOUBLE_SIGN));
}

static int is_nan(uint64_t bits)
{
    return (bits & ~DOUBLE_SIGN) > DOUBLE_INFINITY;
}

static int normalize_subnormal(uint64_t *fraction)
{
    int shift = leading_zeros(*fraction) - 11;
    *fraction <<= shift;
    return 1 - shift;
}

static uint64_t multiply_wide(uint64_t a, uint64_t b, uint64_t *low)
{
    uint64_t a0 = (uint32_t)a;
    uint64_t a1 = a >> 32;
    uint64_t b0 = (uint32_t)b;
    uint64_t b1 = b >> 32;
    uint64_t bottom = a0 * b0;
    uint64_t middle = a1 * b0;
    uint64_t cross = middle + a0 * b1;
    uint64_t top = a1 * b1 + ((uint64_t)(cross < middle) << 32) + (cross >> 32);
    cross <<= 32;
    bottom += cross;
    *low = bottom;
    return top + (bottom < cross);
}

double __muldf3(double a, double b)
{
    uint64_t x = double_bits(a);
    uint64_t y = double_bits(b);
    uint64_t sign = (x ^ y) & DOUBLE_SIGN;
    int xe = exponent_of(x);
    int ye = exponent_of(y);
    uint64_t xs = x & DOUBLE_FRACTION;
    uint64_t ys = y & DOUBLE_FRACTION;
    if (xe == 0x7FF || ye == 0x7FF)
    {
        if (is_nan(x) || is_nan(y) || (x & ~DOUBLE_SIGN) == 0 || (y & ~DOUBLE_SIGN) == 0)
            return double_of(DOUBLE_NAN);
        return double_of(sign | DOUBLE_INFINITY);
    }
    if (xe == 0)
    {
        if (xs == 0)
            return double_of(sign);
        xe = normalize_subnormal(&xs);
    }
    if (ye == 0)
    {
        if (ys == 0)
            return double_of(sign);
        ye = normalize_subnormal(&ys);
    }

    int exponent = xe + ye - 0x3FF;
    uint64_t low;
    uint64_t product = multiply_wide((xs | DOUBLE_IMPLICIT) << 10, (ys | DOUBLE_IMPLICIT) << 11, &low);
    product |= low != 0;
    if (product < 0x4000000000000000ull)
    {
        exponent--;
        product <<= 1;
    }
    return double_of(round_pack(sign, exponent, product));
}

double __divdf3(double a, double b)
{
    uint64_t x = double_bits(a);
    uint64_t y = double_bits(b);
    uint64_t sign = (x ^ y) & DOUBLE_SIGN;
    int xe = exponent_of(x);
    int ye = exponent_of(y);
    uint64_t xs = x & DOUBLE_FRACTION;
    uint64_t ys = y & DOUBLE_FRACTION;
    if (xe == 0x7FF)
        return double_of(xs != 0 || ye == 0x7FF ? DOUBLE_NAN : sign | DOUBLE_INFINITY);
    if (ye == 0x7FF)
        return double_of(ys != 0 ? DOUBLE_NAN : sign);
    if (ye == 0)
    {
        if (ys == 0)
            return double_of((x & ~DOUBLE_SIGN) == 0 ? DOUBLE_NAN : sign | DOUBLE_INFINITY);
        ye = normalize_subnormal(&ys);
    }
    if (xe == 0)
    {
        if (xs == 0)
            return double_of(sign);
        xe = normalize_subnormal(&xs);
    }

    int exponent = xe - ye + 0x3FE;
    xs |= DOUBLE_IMPLICIT;
    ys |= DOUBLE_IMPLICIT;
    if (xs < ys)
    {
        exponent--;
        xs <<= 1;
    }

    // 55 quotient bits are the 53 kept, the rounding bit and one more, and the remainder gives the sticky bit
    uint64_t quotient = 0;
    for (int i = 0; i < 55; i++)
    {
        quotient <<= 1;
        if (xs >= ys)
        {
            xs -= ys;
            quotient |= 1;
        }
        xs <<= 1;
    }
    return double_of(round_pack(sign, exponent, quotient << 8 | (xs != 0)));
}

static int compare(uint64_t x, uint64_t y, uint64_t sign, uint64_t infinity, int unordered)
{
    uint64_t xm = x & ~sign;
    uint64_t ym = y & ~sign;
    if (xm > infinity || ym > infinity)
        return unordered;
    if ((xm | ym) == 0 || x == y)
        return 0;
    if (((x ^ y) & sign) != 0)
        return (x & sign) != 0 ? -1 : 1;
    return (x < y) != ((x & sign) != 0) ? -1 : 1;
}

int __eqdf2(double a, double b)
{
    return compare(double_bits(a), double_bits(b), DOUBLE_SIGN, DOUBLE_INFINITY, 1);
}

int __nedf2(double a, double b)
{
    return compare(double_bits(a), double_bits(b), DOUBLE_SIGN, DOUBLE_INFINITY, 1);
}

int __ltdf2(double a, double b)
{
    return compare(double_bits(a), double_bits(b), DOUBLE_SIGN, DOUBLE_INFINITY, 1);
}

int __ledf2(double a, double b)
{
    return compare(double_bits(a), double_bits(b), DOUBLE_SIGN, DOUBLE_INFINITY, 1);
}

int __gtdf2(double a, double b)
{
    return compare(double_bits(a), double_bits(b), DOUBLE_SIGN, DOUBLE_INFINITY, -1);
}

int __gedf2(double a, double b)
{
    return compare(double_bits(a), double_bits(b), DOUBLE_SIGN, DOUBLE_INFINITY, -1);
}

double __floatsidf(int value)
{
    return __floatdidf(value);
}

double __floatunsidf(unsigned value)
{
    return __floatundidf(value);
}

int __fixdfsi(double value)
{
    uint64_t bits = double_bits(value);
    if (bits & DOUBLE_SIGN)
        return (int)(0u - (uint32_t)truncated_magnitude(bits, 31, FLOAT_SIGN));
    return (int)truncated_magnitude(bits, 31, FLOAT_SIGN - 1);
}

unsigned __fixunsdfsi(double value)
{
    uint64_t bits = double_bits(value);
    return bits & DOUBLE_SIGN ? 0 : (unsigned)truncated_magnitude(bits, 32, 0xFFFFFFFFu);
}

double __extendsfdf2(float value)
{
    return double_of(widen_float(float_bits(value)));
}

float __truncdfsf2(double value)
{
    uint64_t bits = double_bits(value);
    uint32_t sign = (uint32_t)(bits >> 32) & FLOAT_SIGN;
    int exponent = exponent_of(bits);
    uint64_t fraction = bits & DOUBLE_FRACTION;
    if (exponent == 0x7FF)
        return float_of(fraction != 0 ? FLOAT_NAN : sign | FLOAT_INFINITY);
    uint32_t significand = (uint32_t)shift_right_jam(fraction, 22);
    if (exponent == 0 && significand == 0)
        return float_of(sign);
    return float_of(round_pack_float(sign, exponent - 0x381, significand | 0x40000000u));
}

#endif

#if defined(SOFT_FLOAT)

// Rounding the double result again to float is innocuous for these operations since 53 >= 2 * 24 + 2
float __addsf3(float a, float b)
{
    return __truncdfsf2(__adddf3(__extendsfdf2(a), __extendsfdf2(b)));
}

float __subsf3(float a, float b)
{
    return __truncdfsf2(__subdf3(__extendsfdf2(a), __extendsfdf2(b)));
}

float __mulsf3(float a, float b)
{
    return __truncdfsf2(__muldf3(__extendsfdf2(a), __extendsfdf2(b)));
}

float __divsf3(float a, float b)
{
    return __truncdfsf2(__divdf3(__extendsfdf2(a), __extendsfdf2(b)));
}

int __eqsf2(float a, float b)
{
    return compare(float_bits(a), float_bits(b), FLOAT_SIGN, FLOAT_INFINITY, 1);
}

int __nesf2(float a, float b)
{
    return compare(float_bits(a), float_bits(b), FLOAT_SIGN, FLOAT_INFINITY, 1);
}

int __ltsf2(float a, float b)
{
    return compare(float_bits(a), float_bits(b), FLOAT_SIGN, FLOAT_INFINITY, 1);
}

int __lesf2(float a, float b)
{
    return compare(float_bits(a), float_bits(b), FLOAT_SIGN, FLOAT_INFINITY, 1);
}

int __gtsf2(float a, float b)
{
    return compare(float_bits(a), float_bits(b), FLOAT_SIGN, FLOAT_INFINITY, -1);
}

int __gesf2(float a, float b)
{
    return compare(float_bits(a), float_bits(b), FLOAT_SIGN, FLOAT_INFINITY, -1);
}

float __floatsisf(int value)
{
    return __floatdisf(value);
}

float __floatunsisf(unsigned value)
{
    return __floatundisf(value);
}

int __fixsfsi(float value)
{
    return __fixdfsi(__extendsfdf2(value));
}

unsigned __fixunssfsi(float value)
{
    return __fixunsdfsi(__extendsfdf2(value));
}

#endif
