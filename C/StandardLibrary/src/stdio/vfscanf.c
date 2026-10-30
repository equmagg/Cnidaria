#include "scanf-internal.h"

#if defined(__CNIDARIA_FILES)
int vfscanf(FILE* stream, const char* format, va_list ap)
{
    return __vfscanf_internal(stream, (const char*)0, format, ap);
}
#endif
