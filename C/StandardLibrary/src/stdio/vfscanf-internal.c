#include <ctype.h>
#include <errno.h>
#include <limits.h>
#include <stddef.h>
#include <stdint.h>
#include "scanf-internal.h"

#if defined(__CNIDARIA_FILES)

typedef struct
{
    FILE* stream;
    const unsigned char* text;
    size_t consumed;
    int look;
    int has_look;
} __scan_input;

enum
{
    __SCAN_DEFAULT,
    __SCAN_HH,
    __SCAN_H,
    __SCAN_L,
    __SCAN_LL,
    __SCAN_J,
    __SCAN_Z,
    __SCAN_T,
    __SCAN_BIG_L
};

static int __scan_peek(__scan_input* in)
{
    if (!in->has_look)
    {
        if (in->stream != (FILE*)0)
            in->look = fgetc(in->stream);
        else if (*in->text != 0)
        {
            in->look = (int)*in->text;
            in->text++;
        }
        else
            in->look = EOF;
        in->has_look = 1;
    }
    return in->look;
}

static int __scan_take(__scan_input* in)
{
    int ch = __scan_peek(in);
    if (ch != EOF)
    {
        in->consumed++;
        in->has_look = 0;
    }
    return ch;
}

static void __scan_finish(__scan_input* in)
{
    if (in->stream != (FILE*)0 && in->has_look && in->look != EOF)
        ungetc(in->look, in->stream);
}

static void __scan_space(__scan_input* in)
{
    int ch;
    while ((ch = __scan_peek(in)) != EOF && isspace(ch))
        __scan_take(in);
}

static int __scan_digit(int ch)
{
    if (ch >= '0' && ch <= '9') return ch - '0';
    if (ch >= 'a' && ch <= 'f') return ch - 'a' + 10;
    if (ch >= 'A' && ch <= 'F') return ch - 'A' + 10;
    return -1;
}

static int __scan_integer(__scan_input* in, int width, char conversion,
    unsigned long long* result, int* negative, int* at_eof)
{
    int base = 10;
    int seen = 0;
    int ch;
    unsigned long long value = 0;

    *negative = 0;
    *at_eof = __scan_peek(in) == EOF;
    if (*at_eof) return 0;

    if (conversion == 'o') base = 8;
    if (conversion == 'x' || conversion == 'X' || conversion == 'p') base = 16;
    if (conversion == 'i') base = 0;

    ch = __scan_peek(in);
    if (width != 0 && conversion != 'p' && (ch == '+' || ch == '-'))
    {
        *negative = ch == '-';
        __scan_take(in);
        width--;
    }

    if (width != 0 && __scan_peek(in) == '0')
    {
        __scan_take(in);
        width--;
        seen = 1;
        if (base == 0) base = 8;
        if (width != 0 && (conversion == 'i' || base == 16) &&
            (__scan_peek(in) == 'x' || __scan_peek(in) == 'X'))
        {
            __scan_take(in);
            width--;
            base = 16;
            seen = 0;
        }
    }

    if (base == 0) base = 10;
    while (width != 0)
    {
        int digit = __scan_digit(__scan_peek(in));
        if (digit < 0 || digit >= base) break;
        seen = 1;
        __scan_take(in);
        width--;
        if (value > (ULLONG_MAX - (unsigned long long)digit) / (unsigned long long)base)
        {
            value = ULLONG_MAX;
            errno = ERANGE;
        }
        else
            value = value * (unsigned long long)base + (unsigned long long)digit;
    }

    *result = value;
    return seen;
}

static unsigned long long __scan_unsigned_limit(int length)
{
    if (length == __SCAN_HH) return UCHAR_MAX;
    if (length == __SCAN_H) return USHRT_MAX;
    if (length == __SCAN_DEFAULT) return UINT_MAX;
    if (length == __SCAN_L) return ULONG_MAX;
    if (length == __SCAN_Z || length == __SCAN_T) return (size_t)-1;
    return ULLONG_MAX;
}

static unsigned long long __scan_signed_limit(int length, int negative)
{
    unsigned long long max;
    if (length == __SCAN_HH) max = SCHAR_MAX;
    else if (length == __SCAN_H) max = SHRT_MAX;
    else if (length == __SCAN_DEFAULT) max = INT_MAX;
    else if (length == __SCAN_L) max = LONG_MAX;
    else if (length == __SCAN_Z || length == __SCAN_T)
        max = sizeof(ptrdiff_t) == sizeof(int) ? INT_MAX : LLONG_MAX;
    else max = LLONG_MAX;
    return max + (unsigned long long)(negative != 0);
}

static void __scan_store_integer(va_list* ap, int length, int signed_conv,
    unsigned long long value, int negative)
{
    if (signed_conv)
    {
        unsigned long long limit = __scan_signed_limit(length, negative);
        long long signed_value;
        if (value > limit)
        {
            value = limit;
            errno = ERANGE;
        }
        if (negative && value == (unsigned long long)LLONG_MAX + 1ull)
            signed_value = LLONG_MIN;
        else if (negative)
            signed_value = -(long long)value;
        else
            signed_value = (long long)value;

        if (length == __SCAN_HH) *va_arg(*ap, signed char*) = (signed char)signed_value;
        else if (length == __SCAN_H) *va_arg(*ap, short*) = (short)signed_value;
        else if (length == __SCAN_L) *va_arg(*ap, long*) = (long)signed_value;
        else if (length == __SCAN_LL) *va_arg(*ap, long long*) = signed_value;
        else if (length == __SCAN_J) *va_arg(*ap, intmax_t*) = (intmax_t)signed_value;
        else if (length == __SCAN_Z || length == __SCAN_T)
            *va_arg(*ap, ptrdiff_t*) = (ptrdiff_t)signed_value;
        else *va_arg(*ap, int*) = (int)signed_value;
    }
    else
    {
        unsigned long long limit = __scan_unsigned_limit(length);
        if (value > limit)
        {
            value = limit;
            errno = ERANGE;
        }
        if (negative) value = 0ull - value;

        if (length == __SCAN_HH) *va_arg(*ap, unsigned char*) = (unsigned char)value;
        else if (length == __SCAN_H) *va_arg(*ap, unsigned short*) = (unsigned short)value;
        else if (length == __SCAN_L) *va_arg(*ap, unsigned long*) = (unsigned long)value;
        else if (length == __SCAN_LL) *va_arg(*ap, unsigned long long*) = value;
        else if (length == __SCAN_J) *va_arg(*ap, uintmax_t*) = (uintmax_t)value;
        else if (length == __SCAN_Z || length == __SCAN_T)
            *va_arg(*ap, size_t*) = (size_t)value;
        else *va_arg(*ap, unsigned int*) = (unsigned int)value;
    }
}

static double __scan_scale(double value, int exponent, int base)
{
    unsigned int steps;
    double factor;

    if (value == 0.0 || exponent == 0) return value;
    factor = exponent < 0 ? 1.0 / (double)base : (double)base;
    steps = (unsigned int)(exponent < 0 ? -exponent : exponent);
    while (steps != 0)
    {
        if ((steps & 1u) != 0) value *= factor;
        steps >>= 1;
        if (steps != 0) factor *= factor;
    }
    return value;
}

static int __scan_exponent(__scan_input* in, int* width, int* exponent)
{
    int negative = 0;
    int seen = 0;
    int result = 0;
    int ch;

    ch = __scan_peek(in);
    if (*width != 0 && (ch == '+' || ch == '-'))
    {
        negative = ch == '-';
        __scan_take(in);
        (*width)--;
    }
    while (*width != 0 && (ch = __scan_peek(in)) >= '0' && ch <= '9')
    {
        if (result < 4096) result = result * 10 + ch - '0';
        if (result > 4096) result = 4096;
        seen = 1;
        __scan_take(in);
        (*width)--;
    }
    *exponent = negative ? -result : result;
    return seen;
}

static int __scan_floating(__scan_input* in, int width, double* result, int* at_eof)
{
    int ch;
    int negative = 0;
    int hex = 0;
    int base = 10;
    int seen = 0;
    int fractional = 0;
    int kept_digits = 0;
    int exponent = 0;
    double value = 0.0;

    *at_eof = __scan_peek(in) == EOF;
    if (*at_eof) return 0;

    ch = __scan_peek(in);
    if (width != 0 && (ch == '+' || ch == '-'))
    {
        negative = ch == '-';
        __scan_take(in);
        width--;
    }

    if (width != 0 && __scan_peek(in) == '0')
    {
        __scan_take(in);
        width--;
        seen = 1;
        if (width != 0 && (__scan_peek(in) == 'x' || __scan_peek(in) == 'X'))
        {
            __scan_take(in);
            width--;
            hex = 1;
            base = 16;
            seen = 0;
            kept_digits = 0;
        }
    }

    while (width != 0)
    {
        int digit;
        ch = __scan_peek(in);
        if (ch == '.' && !fractional)
        {
            fractional = 1;
            __scan_take(in);
            width--;
            continue;
        }
        digit = __scan_digit(ch);
        if (digit < 0 || digit >= base) break;
        seen = 1;
        __scan_take(in);
        width--;
        if (kept_digits == 0 && digit == 0)
        {
            // Leading zeros do not consume the significand's precision
            if (fractional && exponent > -4096) exponent -= hex ? 4 : 1;
        }
        else if (kept_digits < (hex ? 13 : 17))
        {
            value = value * (double)base + (double)digit;
            kept_digits++;
            if (fractional) exponent -= hex ? 4 : 1;
        }
        else if (!fractional && exponent < 4096)
            exponent += hex ? 4 : 1;
    }
    if (!seen) return 0;

    ch = __scan_peek(in);
    if (width != 0 && (hex ? (ch == 'p' || ch == 'P') : (ch == 'e' || ch == 'E')))
    {
        int explicit_exponent;
        __scan_take(in);
        width--;
        if (!__scan_exponent(in, &width, &explicit_exponent)) return 0;
        exponent += explicit_exponent;
    }
    if (exponent > 4096) exponent = 4096;
    if (exponent < -4096) exponent = -4096;
    value = __scan_scale(value, exponent, hex ? 2 : 10);
    *result = negative ? -value : value;
    return 1;
}

static const char* __scan_set_end(const char* start)
{
    const char* p = start;
    if (*p == '^') p++;
    if (*p == ']') p++;
    while (*p != 0 && *p != ']') p++;
    return *p == ']' ? p : (const char*)0;
}

static int __scan_set_contains(const char* start, const char* end, int ch)
{
    const char* p = start;
    int inverted = 0;
    int matched = 0;
    int previous = -1;
    if (*p == '^') { inverted = 1; p++; }

    while (p < end)
    {
        int current = (unsigned char)*p;
        if (current == '-' && previous >= 0 && p + 1 < end)
        {
            int high = (unsigned char)p[1];
            if (previous <= ch && ch <= high) matched = 1;
            previous = high;
            p += 2;
        }
        else
        {
            if (current == ch) matched = 1;
            previous = current;
            p++;
        }
    }
    return inverted ? !matched : matched;
}

static void __scan_store_count(va_list* ap, int length, size_t count)
{
    if (length == __SCAN_HH) *va_arg(*ap, signed char*) = (signed char)count;
    else if (length == __SCAN_H) *va_arg(*ap, short*) = (short)count;
    else if (length == __SCAN_L) *va_arg(*ap, long*) = (long)count;
    else if (length == __SCAN_LL) *va_arg(*ap, long long*) = (long long)count;
    else if (length == __SCAN_J) *va_arg(*ap, intmax_t*) = (intmax_t)count;
    else if (length == __SCAN_Z || length == __SCAN_T)
        *va_arg(*ap, ptrdiff_t*) = (ptrdiff_t)count;
    else *va_arg(*ap, int*) = (int)count;
}

int __vfscanf_internal(FILE* stream, const char* text, const char* format, va_list arguments)
{
    __scan_input in;
    va_list ap;
    int assigned = 0;
    int failed_eof = 0;

    in.stream = stream;
    in.text = (const unsigned char*)text;
    in.consumed = 0;
    in.look = EOF;
    in.has_look = 0;
    va_copy(ap, arguments);

    while (*format != 0)
    {
        int ch;
        int width = INT_MAX;
        int length = __SCAN_DEFAULT;
        int suppress = 0;
        char conversion;
        const char* set_start = (const char*)0;
        const char* set_end = (const char*)0;

        if (isspace((unsigned char)*format))
        {
            do { format++; } while (isspace((unsigned char)*format));
            __scan_space(&in);
            continue;
        }
        if (*format != '%')
        {
            ch = __scan_peek(&in);
            if (ch != (unsigned char)*format)
            {
                failed_eof = ch == EOF;
                break;
            }
            __scan_take(&in);
            format++;
            continue;
        }

        format++;
        if (*format == '%')
        {
            ch = __scan_peek(&in);
            if (ch != '%')
            {
                failed_eof = ch == EOF;
                break;
            }
            __scan_take(&in);
            format++;
            continue;
        }

        if (*format == '*') { suppress = 1; format++; }
        if (*format >= '0' && *format <= '9')
        {
            width = 0;
            while (*format >= '0' && *format <= '9')
            {
                if (width < INT_MAX / 10 - 10)
                    width = width * 10 + *format - '0';
                else width = INT_MAX;
                format++;
            }
            if (width == 0) break;
        }

        if (*format == 'h')
        {
            format++;
            length = __SCAN_H;
            if (*format == 'h') { format++; length = __SCAN_HH; }
        }
        else if (*format == 'l')
        {
            format++;
            length = __SCAN_L;
            if (*format == 'l') { format++; length = __SCAN_LL; }
        }
        else if (*format == 'j') { length = __SCAN_J; format++; }
        else if (*format == 'z') { length = __SCAN_Z; format++; }
        else if (*format == 't') { length = __SCAN_T; format++; }
        else if (*format == 'L') { length = __SCAN_BIG_L; format++; }

        conversion = *format;
        if (conversion == 0) break;
        format++;

        if (conversion == 'n')
        {
            if (!suppress) __scan_store_count(&ap, length, in.consumed);
            continue;
        }

        if (conversion == '[')
        {
            set_start = format;
            set_end = __scan_set_end(set_start);
            if (set_end == (const char*)0) break;
            format = set_end + 1;
        }


        if ((conversion == 'c' || conversion == 's' || conversion == '[') &&
            length != __SCAN_DEFAULT) break;

        if (conversion != 'c' && conversion != '[')
            __scan_space(&in);

        if (conversion == 'c' || conversion == 's' || conversion == '[')
        {
            int read = 0;
            char* destination = suppress ? (char*)0 : va_arg(ap, char*);
            if (conversion == 'c' && width == INT_MAX) width = 1;
            while (width != 0)
            {
                ch = __scan_peek(&in);
                if (ch == EOF) break;
                if (conversion == 's' && isspace(ch)) break;
                if (conversion == '[' && !__scan_set_contains(set_start, set_end, ch)) break;
                if (!suppress) destination[read] = (char)ch;
                __scan_take(&in);
                read++;
                width--;
            }
            if (read == 0 || (conversion == 'c' && width != 0))
            {
                failed_eof = __scan_peek(&in) == EOF;
                break;
            }
            if (!suppress)
            {
                if (conversion != 'c') destination[read] = 0;
                assigned++;
            }
            continue;
        }

        if (conversion == 'd' || conversion == 'i' || conversion == 'u' ||
            conversion == 'x' || conversion == 'X' || conversion == 'o' || conversion == 'p')
        {
            unsigned long long number;
            int negative;
            int at_eof;
            if (!__scan_integer(&in, width, conversion, &number, &negative, &at_eof))
            {
                failed_eof = at_eof;
                break;
            }
            if (!suppress)
            {
                if (conversion == 'p')
                    *va_arg(ap, void**) = (void*)(uintptr_t)number;
                else
                    __scan_store_integer(&ap, length, conversion == 'd' || conversion == 'i',
                        number, negative);
                assigned++;
            }
            continue;
        }

        if (conversion == 'f' || conversion == 'F' || conversion == 'e' ||
            conversion == 'E' || conversion == 'g' || conversion == 'G' ||
            conversion == 'a' || conversion == 'A')
        {
            double number;
            int at_eof;
            if (length != __SCAN_DEFAULT && length != __SCAN_L) break;
            if (!__scan_floating(&in, width, &number, &at_eof))
            {
                failed_eof = at_eof;
                break;
            }
            if (!suppress)
            {
                if (length == __SCAN_L) *va_arg(ap, double*) = number;
                else *va_arg(ap, float*) = (float)number;
                assigned++;
            }
            continue;
        }

        break;
    }

    va_end(ap);
    __scan_finish(&in);
    return assigned == 0 && failed_eof ? EOF : assigned;
}

#endif
