#ifndef __SCANF_INTERNAL_H
#define __SCANF_INTERNAL_H

#include <stdio.h>

#if defined(__CNIDARIA_FILES)
int __vfscanf_internal(FILE* stream, const char* text, const char* format, va_list arguments);
#endif

#endif
