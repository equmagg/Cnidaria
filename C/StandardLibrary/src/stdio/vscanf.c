#include "scanf-internal.h"

#if defined(__CNIDARIA_FILES)
int vscanf(const char* format, va_list ap)
{
    return __vfscanf_internal(stdin, (const char*)0, format, ap);
}
#endif
