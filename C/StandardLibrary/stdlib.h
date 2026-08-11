#ifndef __STDLIB_H
#define __STDLIB_H

#include <stddef.h>

void* malloc(size_t size);
void free(void* pointer);
void* realloc(void* pointer, size_t size);
void* calloc(size_t count, size_t size);

#endif
