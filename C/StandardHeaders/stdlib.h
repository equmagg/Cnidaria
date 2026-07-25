#ifndef __STDLIB_H
#define __STDLIB_H

#include <stddef.h>
#include <string.h>

#if defined(_WIN32) && defined(__x86_64__)

static void* __stdlib_windows_heap(void)
{
    void* heap;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_GetProcessHeap]\n"
        "add rsp, 32"
        : "={rax}"(heap)
        :
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return heap;
}

static void* malloc(size_t size)
{
    void* result;
    void* heap = __stdlib_windows_heap();

    if (size == 0)
        size = 1;

    __asm__ volatile(
        "xor edx, edx\n"
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_HeapAlloc]\n"
        "add rsp, 32"
        : "={rax}"(result)
        : [heap] "{rcx}"(heap), [size] "{r8}"(size)
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
}

static void free(void* pointer)
{
    void* heap;

    if (pointer == (void*)0)
        return;

    heap = __stdlib_windows_heap();
    __asm__ volatile(
        "xor edx, edx\n"
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_HeapFree]\n"
        "add rsp, 32"
        :
    : [heap] "{rcx}"(heap), [pointer] "{r8}"(pointer)
        : "rax", "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
}

static void* realloc(void* pointer, size_t size)
{
    void* result;
    void* heap;

    if (pointer == (void*)0)
        return malloc(size);
    if (size == 0)
    {
        free(pointer);
        return (void*)0;
    }

    heap = __stdlib_windows_heap();
    __asm__ volatile(
        "xor edx, edx\n"
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_HeapReAlloc]\n"
        "add rsp, 32"
        : "={rax}"(result)
        : [heap] "{rcx}"(heap), [pointer] "{r8}"(pointer), [size] "{r9}"(size)
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
}

#elif defined(_WIN32) && defined(__i386__)

static void* __stdlib_windows_heap(void)
{
    void* heap;
    __asm__ volatile(
        "call dword ptr[__imp_GetProcessHeap]"
        : "={eax}"(heap)
        :
        : "ecx", "edx", "memory");
    return heap;
}

static void* malloc(size_t size)
{
    void* result;
    void* heap = __stdlib_windows_heap();

    if (size == 0)
        size = 1;

    __asm__ volatile(
        "push %[size]\n"
        "push 0\n"
        "push %[heap]\n"
        "call dword ptr[__imp_HeapAlloc]"
        : "={eax}"(result)
        : [heap] "r"(heap), [size] "r"(size)
        : "ecx", "edx", "memory");
    return result;
}

static void free(void* pointer)
{
    void* heap;

    if (pointer == (void*)0)
        return;

    heap = __stdlib_windows_heap();
    __asm__ volatile(
        "push %[pointer]\n"
        "push 0\n"
        "push %[heap]\n"
        "call dword ptr[__imp_HeapFree]"
        :
    : [heap] "r"(heap), [pointer] "r"(pointer)
        : "eax", "ecx", "edx", "memory");
}

static void* realloc(void* pointer, size_t size)
{
    void* result;
    void* heap;

    if (pointer == (void*)0)
        return malloc(size);
    if (size == 0)
    {
        free(pointer);
        return (void*)0;
    }

    heap = __stdlib_windows_heap();
    __asm__ volatile(
        "push %[size]\n"
        "push %[pointer]\n"
        "push 0\n"
        "push %[heap]\n"
        "call dword ptr[__imp_HeapReAlloc]"
        : "={eax}"(result)
        : [heap] "r"(heap), [pointer] "r"(pointer), [size] "r"(size)
        : "ecx", "edx", "memory");
    return result;
}

#elif defined(__linux__)

typedef struct __stdlib_linux_block
{
    size_t mapping_size;
    size_t requested_size;
    long double alignment;
} __stdlib_linux_block;

static void* __stdlib_linux_map(size_t length)
{
    long result;

#if defined(__x86_64__)
    __asm__ volatile(
        "mov eax, 9\n"
        "mov r10d, 34\n"
        "mov r8, -1\n"
        "xor r9d, r9d\n"
        "syscall"
        : "={rax}"(result)
        : [length] "{rsi}"(length), [protection] "{rdx}"(3), [address] "{rdi}"((void*)0)
        : "rcx", "r10", "r8", "r9", "r11", "memory");
#elif defined(__i386__)
    unsigned long arguments[6];
    arguments[0] = 0;
    arguments[1] = length;
    arguments[2] = 3;
    arguments[3] = 34;
    arguments[4] = (unsigned long)-1;
    arguments[5] = 0;
    __asm__ volatile(
        "push ebx\n"
        "mov ebx, %[arguments]\n"
        "mov eax, 90\n"
        ".byte 0xcd, 0x80\n"
        "pop ebx"
        : "={eax}"(result)
        : [arguments] "r"(arguments)
        : "ecx", "edx", "esi", "edi", "memory");
#elif defined(__aarch64__)
    result = 0;
    __asm__ volatile(
        "mov x8, #222\n"
        "svc #0"
        : "+{x0}"(result)
        : [length] "{x1}"(length), [protection] "{x2}"(3), [flags] "{x3}"(34), [descriptor] "{x4}"(-1), [offset] "{x5}"(0)
        : "x8", "memory");
#elif defined(__arm__)
    result = 0;
    __asm__ volatile(
        "mov r7, #192\n"
        "svc #0"
        : "+{r0}"(result)
        : [length] "{r1}"(length), [protection] "{r2}"(3), [flags] "{r3}"(34), [descriptor] "{r4}"(-1), [offset] "{r5}"(0)
        : "r7", "memory");
#elif defined(__riscv)
    result = 0;
    __asm__ volatile(
        "addi a7, zero, 222\n"
        "ecall"
        : "+{a0}"(result)
        : [length] "{a1}"(length), [protection] "{a2}"(3), [flags] "{a3}"(34), [descriptor] "{a4}"(-1), [offset] "{a5}"(0)
        : "a7", "memory");
#else
    result = -1;
#endif

    if (result < 0 && result >= -4095)
        return (void*)0;
    return (void*)result;
}

static void __stdlib_linux_unmap(void* address, size_t length)
{
#if defined(__x86_64__)
    long result;
    __asm__ volatile(
        "mov eax, 11\n"
        "syscall"
        : "={rax}"(result)
        : [address] "{rdi}"(address), [length] "{rsi}"(length)
        : "rcx", "r11", "memory");
#elif defined(__i386__)
    long result;
    __asm__ volatile(
        "push ebx\n"
        "mov eax, 91\n"
        ".byte 0xcd, 0x80\n"
        "pop ebx"
        : "={eax}"(result)
        : [address] "{ebx}"(address), [length] "{ecx}"(length)
        : "edx", "memory");
#elif defined(__aarch64__)
    long result = (long)address;
    __asm__ volatile(
        "mov x8, #215\n"
        "svc #0"
        : "+{x0}"(result)
        : [length] "{x1}"(length)
        : "x8", "memory");
#elif defined(__arm__)
    long result = (long)address;
    __asm__ volatile(
        "mov r7, #91\n"
        "svc #0"
        : "+{r0}"(result)
        : [length] "{r1}"(length)
        : "r7", "memory");
#elif defined(__riscv)
    long result = (long)address;
    __asm__ volatile(
        "addi a7, zero, 215\n"
        "ecall"
        : "+{a0}"(result)
        : [length] "{a1}"(length)
        : "a7", "memory");
#endif
}

static void* malloc(size_t size)
{
    size_t total_size;
    __stdlib_linux_block* block;

    if (size == 0)
        size = 1;
    if (size > (size_t)-1 - sizeof(__stdlib_linux_block))
        return (void*)0;

    total_size = size + sizeof(__stdlib_linux_block);
    block = (__stdlib_linux_block*)__stdlib_linux_map(total_size);
    if (block == (__stdlib_linux_block*)0)
        return (void*)0;

    block->mapping_size = total_size;
    block->requested_size = size;
    return (void*)(block + 1);
}

static void free(void* pointer)
{
    __stdlib_linux_block* block;

    if (pointer == (void*)0)
        return;

    block = ((__stdlib_linux_block*)pointer) - 1;
    __stdlib_linux_unmap((void*)block, block->mapping_size);
}

static void* realloc(void* pointer, size_t size)
{
    __stdlib_linux_block* block;
    size_t copy_size;
    void* replacement;

    if (pointer == (void*)0)
        return malloc(size);
    if (size == 0)
    {
        free(pointer);
        return (void*)0;
    }

    block = ((__stdlib_linux_block*)pointer) - 1;
    copy_size = block->requested_size;
    if (copy_size > size)
        copy_size = size;

    replacement = malloc(size);
    if (replacement == (void*)0)
        return (void*)0;

    memcpy(replacement, pointer, copy_size);
    free(pointer);
    return replacement;
}

#else

typedef struct __stdlib_arena_block
{
    size_t size;
    int free;
    struct __stdlib_arena_block* next;
    long double alignment;
} __stdlib_arena_block;

typedef union __stdlib_arena_storage
{
    long double alignment;
    unsigned char bytes[1048576];
} __stdlib_arena_storage;

static __stdlib_arena_storage __stdlib_arena;
static __stdlib_arena_block* __stdlib_arena_first;

static size_t __stdlib_arena_align(size_t value)
{
    size_t alignment = 16;
    size_t remainder = value % alignment;
    if (remainder == 0)
        return value;
    if (value > (size_t)-1 - (alignment - remainder))
        return 0;
    return value + alignment - remainder;
}

static void __stdlib_arena_initialize(void)
{
    if (__stdlib_arena_first != (__stdlib_arena_block*)0)
        return;

    __stdlib_arena_first = (__stdlib_arena_block*)__stdlib_arena.bytes;
    __stdlib_arena_first->size = sizeof(__stdlib_arena.bytes) - sizeof(__stdlib_arena_block);
    __stdlib_arena_first->free = 1;
    __stdlib_arena_first->next = (__stdlib_arena_block*)0;
}

static void __stdlib_arena_split(__stdlib_arena_block* block, size_t size)
{
    size_t remaining;
    __stdlib_arena_block* next;

    if (block->size < size + sizeof(__stdlib_arena_block) + sizeof(void*))
        return;

    remaining = block->size - size - sizeof(__stdlib_arena_block);
    next = (__stdlib_arena_block*)((unsigned char*)(block + 1) + size);
    next->size = remaining;
    next->free = 1;
    next->next = block->next;
    block->size = size;
    block->next = next;
}

static void* malloc(size_t size)
{
    __stdlib_arena_block* block;

    if (size == 0)
        size = 1;
    size = __stdlib_arena_align(size);
    if (size == 0)
        return (void*)0;

    __stdlib_arena_initialize();
    block = __stdlib_arena_first;
    while (block != (__stdlib_arena_block*)0)
    {
        if (block->free && block->size >= size)
        {
            __stdlib_arena_split(block, size);
            block->free = 0;
            return (void*)(block + 1);
        }
        block = block->next;
    }

    return (void*)0;
}

static void __stdlib_arena_coalesce(void)
{
    __stdlib_arena_block* block = __stdlib_arena_first;
    while (block != (__stdlib_arena_block*)0 && block->next != (__stdlib_arena_block*)0)
    {
        if (block->free && block->next->free)
        {
            block->size = block->size + sizeof(__stdlib_arena_block) + block->next->size;
            block->next = block->next->next;
        }
        else
        {
            block = block->next;
        }
    }
}

static void free(void* pointer)
{
    __stdlib_arena_block* block;

    if (pointer == (void*)0)
        return;

    block = ((__stdlib_arena_block*)pointer) - 1;
    block->free = 1;
    __stdlib_arena_coalesce();
}

static void* realloc(void* pointer, size_t size)
{
    __stdlib_arena_block* block;
    size_t aligned_size;
    size_t copy_size;
    void* replacement;

    if (pointer == (void*)0)
        return malloc(size);
    if (size == 0)
    {
        free(pointer);
        return (void*)0;
    }

    aligned_size = __stdlib_arena_align(size);
    if (aligned_size == 0)
        return (void*)0;

    block = ((__stdlib_arena_block*)pointer) - 1;
    if (block->size >= aligned_size)
    {
        __stdlib_arena_split(block, aligned_size);
        return pointer;
    }

    if (block->next != (__stdlib_arena_block*)0 && block->next->free &&
        block->size + sizeof(__stdlib_arena_block) + block->next->size >= aligned_size)
    {
        block->size = block->size + sizeof(__stdlib_arena_block) + block->next->size;
        block->next = block->next->next;
        __stdlib_arena_split(block, aligned_size);
        return pointer;
    }

    replacement = malloc(size);
    if (replacement == (void*)0)
        return (void*)0;

    copy_size = block->size;
    if (copy_size > size)
        copy_size = size;
    memcpy(replacement, pointer, copy_size);
    free(pointer);
    return replacement;
}

#endif

static void* calloc(size_t count, size_t size)
{
    size_t total_size;
    void* pointer;

    if (size != 0 && count > (size_t)-1 / size)
        return (void*)0;

    total_size = count * size;
    pointer = malloc(total_size);
    if (pointer == (void*)0)
        return (void*)0;

    memset(pointer, 0, total_size);
    return pointer;
}

#endif
