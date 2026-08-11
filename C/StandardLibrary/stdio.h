#ifndef __STDIO_H
#define __STDIO_H

#include <stddef.h>
#include <stdarg.h>

int printf(const char* format, ...);

#if defined(__linux__)
void shutdown(void);
#endif

#endif
