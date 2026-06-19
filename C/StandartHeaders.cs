using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.C
{
    public static class StandardHeaders
    {
        public const string BuiltinVaStartName = "__builtin_va_start";
        public const string PrintfIntrinsicName = "__printf";
        public const string MallocIntrinsicName = "malloc";
        public const string FreeIntrinsicName = "free";

        private const string StddefH = @"
#ifndef __STDDEF_H
#define __STDDEF_H

typedef unsigned long size_t;
typedef long ptrdiff_t;

#ifndef NULL
#define NULL ((void*)0)
#endif

#endif
";

        private const string StdioH = @"
#ifndef __STDIO_H
#define __STDIO_H

#include <stddef.h>
#include <stdarg.h>

void __printf(const char* text);

#define __PRINTF_LEN_NONE 0
#define __PRINTF_LEN_HH 1
#define __PRINTF_LEN_H 2
#define __PRINTF_LEN_L 3
#define __PRINTF_LEN_LL 4
#define __PRINTF_LEN_J 5
#define __PRINTF_LEN_Z 6
#define __PRINTF_LEN_T 7
#define __PRINTF_LEN_CAPITAL_L 8
#define __PRINTF_PRECISION_LIMIT 18

static int __printf_putchar(int ch)
{
    char text[2];
    text[0] = (char)ch;
    text[1] = 0;
    __printf(text);
    return 1;
}

static int __printf_repeat(int ch, int count)
{
    int written = 0;
    while (count > 0)
    {
        __printf_putchar(ch);
        written = written + 1;
        count = count - 1;
    }
    return written;
}

static int __printf_write(const char* text, int length)
{
    int index = 0;
    while (index < length)
    {
        __printf_putchar(text[index]);
        index = index + 1;
    }
    return length;
}

static int __printf_strlen(const char* text, int precision)
{
    int length = 0;
    if (text == (const char*)0)
        text = ""(null)"";
    while (text[length] != 0 && (precision < 0 || length < precision))
        length = length + 1;
    return length;
}

static int __printf_pad_write(const char* text, int length, int left, int zero, int width, char sign, const char* prefix, int prefix_len)
{
    int total = length + prefix_len;
    int written = 0;
    if (sign != 0)
        total = total + 1;
    if (!left && !zero && width > total)
        written = written + __printf_repeat(' ', width - total);
    if (sign != 0)
        written = written + __printf_putchar(sign);
    if (prefix_len != 0)
        written = written + __printf_write(prefix, prefix_len);
    if (!left && zero && width > total)
        written = written + __printf_repeat('0', width - total);
    written = written + __printf_write(text, length);
    if (left && width > total)
        written = written + __printf_repeat(' ', width - total);
    return written;
}

static int __printf_emit_string(const char* text, int left, int width, int precision)
{
    int length;
    if (text == (const char*)0)
        text = ""(null)"";
    length = __printf_strlen(text, precision);
    return __printf_pad_write(text, length, left, 0, width, 0, """", 0);
}

static int __printf_emit_char(int ch, int left, int width)
{
    char text[1];
    text[0] = (char)ch;
    return __printf_pad_write(text, 1, left, 0, width, 0, """", 0);
}

static char* __printf_utoa(unsigned long long value, unsigned int base, int upper, char* end, int* length)
{
    char* p = end;
    int count = 0;
    do
    {
        unsigned int digit = (unsigned int)(value % (unsigned long long)base);
        p = p - 1;
        if (digit < 10)
            *p = (char)('0' + digit);
        else if (upper)
            *p = (char)('A' + digit - 10);
        else
            *p = (char)('a' + digit - 10);
        value = value / (unsigned long long)base;
        count = count + 1;
    }
    while (value != 0);
    *length = count;
    return p;
}

static unsigned long long __printf_read_unsigned(va_list* ap, int length)
{
    if (length == __PRINTF_LEN_LL || length == __PRINTF_LEN_J)
        return va_arg(*ap, unsigned long long);
    if (length == __PRINTF_LEN_L || length == __PRINTF_LEN_Z || length == __PRINTF_LEN_T)
        return (unsigned long long)va_arg(*ap, unsigned long);
    if (length == __PRINTF_LEN_H)
        return (unsigned long long)(unsigned short)va_arg(*ap, unsigned int);
    if (length == __PRINTF_LEN_HH)
        return (unsigned long long)(unsigned char)va_arg(*ap, unsigned int);
    return (unsigned long long)va_arg(*ap, unsigned int);
}

static unsigned long long __printf_read_signed(va_list* ap, int length, int* negative)
{
    long long value;
    if (length == __PRINTF_LEN_LL || length == __PRINTF_LEN_J)
        value = va_arg(*ap, long long);
    else if (length == __PRINTF_LEN_L || length == __PRINTF_LEN_Z || length == __PRINTF_LEN_T)
        value = (long long)va_arg(*ap, long);
    else if (length == __PRINTF_LEN_H)
        value = (long long)(short)va_arg(*ap, int);
    else if (length == __PRINTF_LEN_HH)
        value = (long long)(signed char)va_arg(*ap, int);
    else
        value = (long long)va_arg(*ap, int);

    if (value < 0)
    {
        *negative = 1;
        return 0ull - (unsigned long long)value;
    }

    *negative = 0;
    return (unsigned long long)value;
}

static int __printf_emit_integer(unsigned long long value, int negative, int is_signed, unsigned int base, int upper, int alt, int zero, int left, int plus, int space, int width, int precision, int pointer_form)
{
    char buffer[65];
    char prefix[2];
    char sign = 0;
    char* digits = buffer + 65;
    int digit_len = 0;
    int zeroes = 0;
    int prefix_len = 0;
    int total;
    int written = 0;

    if (!(precision == 0 && value == 0 && !pointer_form))
        digits = __printf_utoa(value, base, upper, buffer + 65, &digit_len);

    if (is_signed)
    {
        if (negative)
            sign = '-';
        else if (plus)
            sign = '+';
        else if (space)
            sign = ' ';
    }

    if (pointer_form || (alt && base == 16 && value != 0))
    {
        prefix[0] = '0';
        if (upper)
            prefix[1] = 'X';
        else
            prefix[1] = 'x';
        prefix_len = 2;
    }
    else if (alt && base == 8 && (digit_len == 0 || *digits != '0'))
    {
        prefix[0] = '0';
        prefix_len = 1;
    }

    if (precision > digit_len)
        zeroes = precision - digit_len;
    if (precision >= 0)
        zero = 0;

    total = digit_len + zeroes + prefix_len;
    if (sign != 0)
        total = total + 1;

    if (!left && !zero && width > total)
        written = written + __printf_repeat(' ', width - total);
    if (sign != 0)
        written = written + __printf_putchar(sign);
    if (prefix_len != 0)
        written = written + __printf_write(prefix, prefix_len);
    if (!left && zero && width > total)
        written = written + __printf_repeat('0', width - total);
    written = written + __printf_repeat('0', zeroes);
    written = written + __printf_write(digits, digit_len);
    if (left && width > total)
        written = written + __printf_repeat(' ', width - total);
    return written;
}

static int __printf_trim_float(char* buffer, int length)
{
    int dot = -1;
    int index = 0;
    while (index < length)
    {
        if (buffer[index] == '.')
            dot = index;
        index = index + 1;
    }
    if (dot < 0)
        return length;
    while (length > dot + 1 && buffer[length - 1] == '0')
        length = length - 1;
    if (length > dot && buffer[length - 1] == '.')
        length = length - 1;
    return length;
}

static double __printf_pow10(int precision)
{
    double value = 1.0;
    while (precision > 0)
    {
        value = value * 10.0;
        precision = precision - 1;
    }
    return value;
}

static int __printf_fixed_abs(double value, int precision, int alt, char* buffer)
{
    double scale;
    double place = 1.0;
    int length = 0;
    int digit;
    int index;

    if (precision < 0)
        precision = 6;
    if (precision > __PRINTF_PRECISION_LIMIT)
        precision = __PRINTF_PRECISION_LIMIT;

    scale = __printf_pow10(precision);
    value = value + 0.5 / scale;

    while (place <= value / 10.0 && place < 1000000000000000000.0)
        place = place * 10.0;

    while (place >= 1.0)
    {
        digit = (int)(value / place);
        if (digit > 9)
            digit = 9;
        buffer[length] = (char)('0' + digit);
        length = length + 1;
        value = value - (double)digit * place;
        place = place / 10.0;
    }

    if (length == 0)
    {
        buffer[length] = '0';
        length = length + 1;
    }

    if (precision > 0 || alt)
    {
        buffer[length] = '.';
        length = length + 1;
    }

    index = 0;
    while (index < precision)
    {
        value = value * 10.0;
        digit = (int)value;
        if (digit > 9)
            digit = 9;
        buffer[length] = (char)('0' + digit);
        length = length + 1;
        value = value - (double)digit;
        index = index + 1;
    }
    return length;
}

static int __printf_decimal_exp(double value)
{
    int exponent = 0;
    if (value == 0.0)
        return 0;
    while (value >= 10.0 && exponent < 308)
    {
        value = value / 10.0;
        exponent = exponent + 1;
    }
    while (value < 1.0 && exponent > -308)
    {
        value = value * 10.0;
        exponent = exponent - 1;
    }
    return exponent;
}

static int __printf_append_dec_exp(int exponent, int upper, char* buffer, int length)
{
    char temp[8];
    int temp_len = 0;
    int value;
    if (upper)
        buffer[length] = 'E';
    else
        buffer[length] = 'e';
    length = length + 1;
    if (exponent < 0)
    {
        buffer[length] = '-';
        value = 0 - exponent;
    }
    else
    {
        buffer[length] = '+';
        value = exponent;
    }
    length = length + 1;
    do
    {
        temp[temp_len] = (char)('0' + (value % 10));
        temp_len = temp_len + 1;
        value = value / 10;
    }
    while (value != 0);
    while (temp_len < 2)
    {
        temp[temp_len] = '0';
        temp_len = temp_len + 1;
    }
    while (temp_len > 0)
    {
        temp_len = temp_len - 1;
        buffer[length] = temp[temp_len];
        length = length + 1;
    }
    return length;
}

static int __printf_exp_abs(double value, int precision, int alt, int upper, char* buffer)
{
    int exponent = __printf_decimal_exp(value);
    int length;
    while (value >= 10.0)
        value = value / 10.0;
    while (value != 0.0 && value < 1.0)
        value = value * 10.0;
    length = __printf_fixed_abs(value, precision, alt, buffer);
    return __printf_append_dec_exp(exponent, upper, buffer, length);
}

static int __printf_float_abs(double value, int spec, int precision, int alt, int upper, char* buffer)
{
    int exponent;
    int length;
    if (precision < 0)
        precision = 6;
    if (precision > __PRINTF_PRECISION_LIMIT)
        precision = __PRINTF_PRECISION_LIMIT;

    if (spec == 'e' || spec == 'E')
        return __printf_exp_abs(value, precision, alt, upper, buffer);

    if (spec == 'g' || spec == 'G')
    {
        if (precision == 0)
            precision = 1;
        exponent = __printf_decimal_exp(value);
        if (exponent < -4 || exponent >= precision)
        {
            length = __printf_exp_abs(value, precision - 1, alt, upper, buffer);
            if (!alt)
                length = __printf_trim_float(buffer, length);
            return length;
        }
        precision = precision - exponent - 1;
        if (precision < 0)
            precision = 0;
        length = __printf_fixed_abs(value, precision, alt, buffer);
        if (!alt)
            length = __printf_trim_float(buffer, length);
        return length;
    }

    return __printf_fixed_abs(value, precision, alt, buffer);
}

static int __printf_emit_float(double value, int spec, int upper, int alt, int zero, int left, int plus, int space, int width, int precision)
{
    char buffer[96];
    char sign = 0;
    int length;

    if (value != value)
    {
        if (upper)
            return __printf_pad_write(""NAN"", 3, left, 0, width, 0, """", 0);
        return __printf_pad_write(""nan"", 3, left, 0, width, 0, """", 0);
    }

    if (value < 0.0)
    {
        sign = '-';
        value = 0.0 - value;
    }
    else if (plus)
        sign = '+';
    else if (space)
        sign = ' ';

    if (value != 0.0 && value / 2.0 == value)
    {
        if (upper)
            return __printf_pad_write(""INF"", 3, left, 0, width, sign, """", 0);
        return __printf_pad_write(""inf"", 3, left, 0, width, sign, """", 0);
    }

    if (spec == 'a' || spec == 'A')
    {
        int hex_precision = precision;
        if (hex_precision < 0)
            hex_precision = 6;
        length = __printf_exp_abs(value, hex_precision, alt, upper, buffer);
    }
    else
    {
        length = __printf_float_abs(value, spec, precision, alt, upper, buffer);
    }
    return __printf_pad_write(buffer, length, left, zero, width, sign, """", 0);
}

static void __printf_store_count(va_list* ap, int length, int count)
{
    if (length == __PRINTF_LEN_HH)
    {
        signed char* target = va_arg(*ap, signed char*);
        if (target != (signed char*)0)
            *target = (signed char)count;
    }
    else if (length == __PRINTF_LEN_H)
    {
        short* target = va_arg(*ap, short*);
        if (target != (short*)0)
            *target = (short)count;
    }
    else if (length == __PRINTF_LEN_L || length == __PRINTF_LEN_Z || length == __PRINTF_LEN_T)
    {
        long* target = va_arg(*ap, long*);
        if (target != (long*)0)
            *target = (long)count;
    }
    else if (length == __PRINTF_LEN_LL || length == __PRINTF_LEN_J)
    {
        long long* target = va_arg(*ap, long long*);
        if (target != (long long*)0)
            *target = (long long)count;
    }
    else
    {
        int* target = va_arg(*ap, int*);
        if (target != (int*)0)
            *target = count;
    }
}

static int __printf_bad_format(const char* format, int start, int end)
{
    int count = 0;
    while (start < end && format[start] != 0)
    {
        count = count + __printf_putchar(format[start]);
        start = start + 1;
    }
    return count;
}

int printf(const char* format, ...)
{
    va_list ap;
    int index = 0;
    int count = 0;
    va_start(ap, format);

    while (format[index] != 0)
    {
        int start;
        int left = 0;
        int plus = 0;
        int space = 0;
        int alt = 0;
        int zero = 0;
        int width = 0;
        int precision = -1;
        int length = __PRINTF_LEN_NONE;
        int spec;

        if (format[index] != '%')
        {
            count = count + __printf_putchar(format[index]);
            index = index + 1;
            continue;
        }

        start = index;
        index = index + 1;

        while (format[index] == '-' || format[index] == '+' || format[index] == ' ' || format[index] == '#' || format[index] == '0')
        {
            if (format[index] == '-')
                left = 1;
            else if (format[index] == '+')
                plus = 1;
            else if (format[index] == ' ')
                space = 1;
            else if (format[index] == '#')
                alt = 1;
            else
                zero = 1;
            index = index + 1;
        }

        if (format[index] == '*')
        {
            width = va_arg(ap, int);
            if (width < 0)
            {
                left = 1;
                width = 0 - width;
            }
            index = index + 1;
        }
        else
        {
            while (format[index] >= '0' && format[index] <= '9')
            {
                width = width * 10 + format[index] - '0';
                index = index + 1;
            }
        }

        if (format[index] == '.')
        {
            index = index + 1;
            precision = 0;
            if (format[index] == '*')
            {
                precision = va_arg(ap, int);
                if (precision < 0)
                    precision = -1;
                index = index + 1;
            }
            else
            {
                while (format[index] >= '0' && format[index] <= '9')
                {
                    precision = precision * 10 + format[index] - '0';
                    index = index + 1;
                }
            }
        }

        if (format[index] == 'h')
        {
            index = index + 1;
            if (format[index] == 'h')
            {
                length = __PRINTF_LEN_HH;
                index = index + 1;
            }
            else
                length = __PRINTF_LEN_H;
        }
        else if (format[index] == 'l')
        {
            index = index + 1;
            if (format[index] == 'l')
            {
                length = __PRINTF_LEN_LL;
                index = index + 1;
            }
            else
                length = __PRINTF_LEN_L;
        }
        else if (format[index] == 'j')
        {
            length = __PRINTF_LEN_J;
            index = index + 1;
        }
        else if (format[index] == 'z')
        {
            length = __PRINTF_LEN_Z;
            index = index + 1;
        }
        else if (format[index] == 't')
        {
            length = __PRINTF_LEN_T;
            index = index + 1;
        }
        else if (format[index] == 'L')
        {
            length = __PRINTF_LEN_CAPITAL_L;
            index = index + 1;
        }

        spec = format[index];
        if (spec == 0)
        {
            count = count + __printf_bad_format(format, start, index);
            break;
        }

        if (left)
            zero = 0;

        if (spec == '%')
            count = count + __printf_emit_char('%', left, width);
        else if (spec == 'c')
            count = count + __printf_emit_char(va_arg(ap, int), left, width);
        else if (spec == 's')
            count = count + __printf_emit_string(va_arg(ap, char*), left, width, precision);
        else if (spec == 'd' || spec == 'i')
        {
            int negative = 0;
            unsigned long long value = __printf_read_signed(&ap, length, &negative);
            count = count + __printf_emit_integer(value, negative, 1, 10, 0, 0, zero, left, plus, space, width, precision, 0);
        }
        else if (spec == 'u' || spec == 'o' || spec == 'x' || spec == 'X')
        {
            unsigned long long value = __printf_read_unsigned(&ap, length);
            unsigned int base = 10;
            int upper = 0;
            if (spec == 'o')
                base = 8;
            else if (spec == 'x' || spec == 'X')
                base = 16;
            if (spec == 'X')
                upper = 1;
            count = count + __printf_emit_integer(value, 0, 0, base, upper, alt, zero, left, 0, 0, width, precision, 0);
        }
        else if (spec == 'p')
        {
            void* ptr = va_arg(ap, void*);
            count = count + __printf_emit_integer((unsigned long long)(unsigned long)ptr, 0, 0, 16, 0, 0, zero, left, 0, 0, width, precision, 1);
        }
        else if (spec == 'f' || spec == 'F' || spec == 'e' || spec == 'E' || spec == 'g' || spec == 'G' || spec == 'a' || spec == 'A')
        {
            int upper = 0;
            if (spec == 'F' || spec == 'E' || spec == 'G' || spec == 'A')
                upper = 1;
            count = count + __printf_emit_float(va_arg(ap, double), spec, upper, alt, zero, left, plus, space, width, precision);
        }
        else if (spec == 'n')
            __printf_store_count(&ap, length, count);
        else
            count = count + __printf_bad_format(format, start, index + 1);

        index = index + 1;
    }

    va_end(ap);
    return count;
}

#endif

";
        private const string StdargH = @"
#ifndef __STDARG_H
#define __STDARG_H

typedef char* va_list;
char *__builtin_va_start(void);
#define va_start(ap, last) ((ap) = __builtin_va_start())
#define va_arg(ap, type) (*(type *)(((ap) = (ap) + 8) - 8))
#define va_copy(dst, src) ((dst) = (src))
#define va_end(ap) ((void)0)

#endif
";

        public static ImmutableArray<IncludeFile> CreateFiles()
        {
            return ImmutableArray.Create(
                new IncludeFile("stddef.h", StddefH),
                new IncludeFile("stdio.h", StdioH),
                new IncludeFile("stdarg.h", StdargH));
        }

        public static IIncludeResolver CreateResolver()
            => new InMemoryIncludeResolver(CreateFiles());

        public static IReadOnlyDictionary<string, string> CreateFileMap()
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in CreateFiles())
                files[file.Path] = file.Text;
            return files;
        }
    }
}
