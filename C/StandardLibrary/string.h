#ifndef __STRING_H
#define __STRING_H

#include <stddef.h>

void* memcpy(void* restrict destination, const void* restrict source, size_t count);
void* memset(void* destination, int value, size_t count);

#endif
