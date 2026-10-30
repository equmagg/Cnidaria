#include "scanf-internal.h"

#if defined(__CNIDARIA_FILES)
int vsscanf(const char* text, const char* format, va_list ap)
{
    return __vfscanf_internal((FILE*)0, text, format, ap);
}
#endif
