#ifndef __SYS_MMAN_H
#define __SYS_MMAN_H

#include <stddef.h>
#include <sys/types.h>

#define PROT_NONE 0
#define PROT_READ 1
#define PROT_WRITE 2
#define PROT_EXEC 4

#define MAP_SHARED 0x01
#define MAP_PRIVATE 0x02
#define MAP_FIXED 0x10
#define MAP_ANONYMOUS 0x20
#define MAP_ANON MAP_ANONYMOUS

#define MAP_FAILED ((void*)-1)

#if defined(__linux__)
void* mmap(void* address, size_t length, int protection, int flags, int descriptor, off_t offset);
int munmap(void* address, size_t length);
int mprotect(void* address, size_t length, int protection);
#endif

#endif
