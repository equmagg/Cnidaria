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

        private const string StddefH = @"#ifndef __STDDEF_H
#define __STDDEF_H

typedef unsigned long size_t;
typedef long ptrdiff_t;

#ifndef NULL
#define NULL ((void*)0)
#endif

#endif
";

        private const string StdioH = @"#ifndef __STDIO_H
#define __STDIO_H

#include <stddef.h>
#include <stdarg.h>

void __printf(const char* text);

static int __print_char(int ch)
{
    char text[2];
    text[0] = (char)ch;
    text[1] = 0;
    __printf(text);
    return 1;
}

static int __print_int(int value)
{
    char digits[12];
    unsigned int current;
    int length = 0;
    int count = 0;

    if (value < 0)
    {
        count = count + __print_char('-');
        current = (unsigned int)(0 - value);
    }
    else
    {
        current = (unsigned int)value;
    }

    if (current == 0)
        return count + __print_char('0');

    while (current != 0)
    {
        digits[length] = (char)('0' + (current % 10));
        length = length + 1;
        current = current / 10;
    }

    while (length != 0)
    {
        length = length - 1;
        count = count + __print_char(digits[length]);
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
        if (format[index] != '%')
        {
            count = count + __print_char(format[index]);
            index = index + 1;
            continue;
        }
        index = index + 1;
        
        if (format[index] == 0)
        {
            count = count + __print_char('%');
            break;
        }

        if (format[index] == '%')
        {
            count = count + __print_char('%');
            index = index + 1;
            continue;
        }

        if (format[index] == 'd' || format[index] == 'i')
        {
            count = count + __print_int(va_arg(ap, int));
            index = index + 1;
            continue;
        }

        count = count + __print_char('%');
        count = count + __print_char(format[index]);
        index = index + 1;
    }

    va_end(ap);
    return count;
}

#endif
";

        private const string StdargH = @"#ifndef __STDARG_H
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
