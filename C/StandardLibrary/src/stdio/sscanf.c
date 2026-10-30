#include "scanf-internal.h"

#if defined(__CNIDARIA_FILES)
int sscanf(const char* text, const char* format, ...)
{
    va_list ap;
    int result;
    va_start(ap, format);
    result = __vfscanf_internal((FILE*)0, text, format, ap);
    va_end(ap);
    return result;
}
#endif
