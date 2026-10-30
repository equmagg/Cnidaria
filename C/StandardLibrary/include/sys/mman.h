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
#define MAP_DENYWRITE 0x800
#define MAP_EXECUTABLE 0x1000
#define MAP_NORESERVE 0x4000
#define MAP_STACK 0x20000
#define MAP_FIXED_NOREPLACE 0x100000

#define MS_ASYNC 1
#define MS_INVALIDATE 2
#define MS_SYNC 4

#define MAP_FAILED ((void*)-1)

#if defined(__linux__)
void* mmap(void* address, size_t length, int protection, int flags, int descriptor, off_t offset);
int munmap(void* address, size_t length);
int mprotect(void* address, size_t length, int protection);
int msync(void* address, size_t length, int flags);
#endif

#endif
