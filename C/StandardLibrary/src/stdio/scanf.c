#include "scanf-internal.h"

#if defined(__CNIDARIA_FILES)
int scanf(const char* format, ...)
{
    va_list ap;
    int result;
    va_start(ap, format);
    result = __vfscanf_internal(stdin, (const char*)0, format, ap);
    va_end(ap);
    return result;
}
#endif
