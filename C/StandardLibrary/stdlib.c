#include <ctype.h>
#include <errno.h>
#include <limits.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

// Arena allocator: system regions carved into chunks with boundary tags, binned by size

#if defined(_WIN32) && defined(__x86_64__)

#define __HEAP_GRANULARITY 65536

static void* __heap_system_map(size_t size)
{
    void* result;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_VirtualAlloc]\n"
        "add rsp, 32"
        : "={rax}"(result)
        : [address] "{rcx}"((void*)0), [size] "{rdx}"(size), [type] "{r8}"(12288), [protection] "{r9}"(4)
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
}

#elif defined(_WIN32) && defined(__i386__)

#define __HEAP_GRANULARITY 65536

static void* __heap_system_map(size_t size)
{
    void* result;
    __asm__ volatile(
        "push 4\n"
        "push 12288\n"
        "push %[size]\n"
        "push 0\n"
        "call dword ptr[__imp_VirtualAlloc]"
        : "={eax}"(result)
        : [size] "r"(size)
        : "ecx", "edx", "memory");
    return result;
}

#elif defined(__linux__)

#define __HEAP_GRANULARITY 4096

static void* __heap_system_map(size_t size)
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
        : [length] "{rsi}"(size), [protection] "{rdx}"(3), [address] "{rdi}"((void*)0)
        : "rcx", "r10", "r8", "r9", "r11", "memory");
#elif defined(__i386__)
    unsigned long arguments[6];
    arguments[0] = 0;
    arguments[1] = size;
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
        : [length] "{x1}"(size), [protection] "{x2}"(3), [flags] "{x3}"(34), [descriptor] "{x4}"(-1), [offset] "{x5}"(0)
        : "x8", "memory");
#elif defined(__arm__)
    result = 0;
    __asm__ volatile(
        "mov r7, #192\n"
        "svc #0"
        : "+{r0}"(result)
        : [length] "{r1}"(size), [protection] "{r2}"(3), [flags] "{r3}"(34), [descriptor] "{r4}"(-1), [offset] "{r5}"(0)
        : "r7", "memory");
#elif defined(__riscv)
    result = 0;
    __asm__ volatile(
        "addi a7, zero, 222\n"
        "ecall"
        : "+{a0}"(result)
        : [length] "{a1}"(size), [protection] "{a2}"(3), [flags] "{a3}"(34), [descriptor] "{a4}"(-1), [offset] "{a5}"(0)
        : "a7", "memory");
#else
    result = -1;
#endif

    if (result < 0 && result >= -4095)
        return (void*)0;
    return (void*)result;
}

#else

#define __HEAP_GRANULARITY 16
#define __HEAP_STATIC_BYTES 1048576

// No system allocator here: the arena is one static block
typedef union __heap_static_block
{
    void* pointer;
    size_t size;
    long double alignment;
    unsigned char bytes[__HEAP_STATIC_BYTES];
} __heap_static_block;

static __heap_static_block __heap_static_storage;
static int __heap_static_taken;

static void* __heap_system_map(size_t size)
{
    if (__heap_static_taken != 0 || size > sizeof(__heap_static_storage.bytes))
        return (void*)0;
    __heap_static_taken = 1;
    return (void*)__heap_static_storage.bytes;
}

#endif

#define __HEAP_IN_USE ((size_t)1)
#define __HEAP_PREVIOUS_IN_USE ((size_t)2)
#define __HEAP_FLAGS ((size_t)3)
#define __HEAP_ALIGNMENT (sizeof(size_t) * 2)
#define __HEAP_SENTINEL (sizeof(size_t) * 2)
#define __HEAP_SMALL_BINS 32
#define __HEAP_BIN_COUNT 56
#define __HEAP_REGION_MINIMUM 1048576

// While free, a chunk repeats its size in the next header's previous_size, for backward merging
typedef struct __heap_chunk
{
    size_t previous_size;
    size_t size;
    struct __heap_chunk* next_free;
    struct __heap_chunk* previous_free;
} __heap_chunk;

typedef struct __heap_region
{
    struct __heap_region* next;
    size_t size;
} __heap_region;

static __heap_chunk* __heap_bins[__HEAP_BIN_COUNT];
static __heap_region* __heap_regions;

static size_t __heap_align(size_t value, size_t alignment)
{
    size_t remainder = value % alignment;
    if (remainder == 0)
        return value;
    if (value > (size_t)-1 - (alignment - remainder))
        return 0;
    return value + alignment - remainder;
}

static size_t __heap_size(const __heap_chunk* chunk)
{
    return chunk->size & ~__HEAP_FLAGS;
}

static __heap_chunk* __heap_next(__heap_chunk* chunk)
{
    return (__heap_chunk*)((unsigned char*)chunk + __heap_size(chunk));
}

static void* __heap_payload(__heap_chunk* chunk)
{
    return (void*)((unsigned char*)chunk + sizeof(size_t) * 2);
}

static __heap_chunk* __heap_chunk_of(void* pointer)
{
    return (__heap_chunk*)((unsigned char*)pointer - sizeof(size_t) * 2);
}

// Exact bins below the small threshold, one bin per doubling above it
static size_t __heap_bin_index(size_t size)
{
    size_t index = size / __HEAP_ALIGNMENT;
    if (index < __HEAP_SMALL_BINS)
        return index;

    index = __HEAP_SMALL_BINS;
    size = size / (__HEAP_SMALL_BINS * __HEAP_ALIGNMENT);
    while (size > 1 && index + 1 < __HEAP_BIN_COUNT)
    {
        size = size >> 1;
        index = index + 1;
    }
    return index;
}

static void __heap_link(__heap_chunk* chunk)
{
    size_t index = __heap_bin_index(__heap_size(chunk));
    chunk->previous_free = (__heap_chunk*)0;
    chunk->next_free = __heap_bins[index];
    if (chunk->next_free != (__heap_chunk*)0)
        chunk->next_free->previous_free = chunk;
    __heap_bins[index] = chunk;
}

static void __heap_unlink(__heap_chunk* chunk)
{
    if (chunk->previous_free != (__heap_chunk*)0)
        chunk->previous_free->next_free = chunk->next_free;
    else
        __heap_bins[__heap_bin_index(__heap_size(chunk))] = chunk->next_free;
    if (chunk->next_free != (__heap_chunk*)0)
        chunk->next_free->previous_free = chunk->previous_free;
}

// Marks a chunk free and writes the footer the next header carries
static void __heap_release(__heap_chunk* chunk, size_t size)
{
    __heap_chunk* next;

    chunk->size = size | __HEAP_PREVIOUS_IN_USE;
    next = __heap_next(chunk);
    next->size = next->size & ~__HEAP_PREVIOUS_IN_USE;
    next->previous_size = size;
    __heap_link(chunk);
}

static void __heap_use(__heap_chunk* chunk, size_t needed)
{
    size_t size = __heap_size(chunk);
    size_t previous = chunk->size & __HEAP_PREVIOUS_IN_USE;
    __heap_chunk* next;

    if (size - needed >= sizeof(__heap_chunk))
    {
        chunk->size = needed | previous | __HEAP_IN_USE;
        __heap_release(__heap_next(chunk), size - needed);
        return;
    }

    chunk->size = size | previous | __HEAP_IN_USE;
    next = __heap_next(chunk);
    next->size = next->size | __HEAP_PREVIOUS_IN_USE;
}

static __heap_chunk* __heap_take(size_t needed)
{
    size_t index = __heap_bin_index(needed);

    while (index < __HEAP_BIN_COUNT)
    {
        __heap_chunk* chunk = __heap_bins[index];
        while (chunk != (__heap_chunk*)0)
        {
            if (__heap_size(chunk) >= needed)
            {
                __heap_unlink(chunk);
                __heap_use(chunk, needed);
                return chunk;
            }
            chunk = chunk->next_free;
        }
        index = index + 1;
    }

    return (__heap_chunk*)0;
}

// A sentinel header marked in use stops merges at the region end
static int __heap_grow(size_t needed)
{
    size_t header = __heap_align(sizeof(__heap_region), __HEAP_ALIGNMENT);
    size_t total;
    size_t usable;
    size_t offset;
    unsigned char* memory;
    unsigned char* start;
    __heap_region* region;
    __heap_chunk* sentinel;

    if (needed > (size_t)-1 - header - __HEAP_SENTINEL - __HEAP_ALIGNMENT - __HEAP_GRANULARITY)
        return 0;

    total = needed + header + __HEAP_SENTINEL + __HEAP_ALIGNMENT;
    if (total < __HEAP_REGION_MINIMUM)
        total = __HEAP_REGION_MINIMUM;
    total = __heap_align(total, __HEAP_GRANULARITY);
    if (total == 0)
        return 0;

    memory = (unsigned char*)__heap_system_map(total);
    if (memory == (unsigned char*)0)
        return 0;

    region = (__heap_region*)memory;
    region->size = total;
    region->next = __heap_regions;
    __heap_regions = region;

    start = memory + header;
    offset = (size_t)((uintptr_t)start % __HEAP_ALIGNMENT);
    if (offset != 0)
        start = start + (__HEAP_ALIGNMENT - offset);

    usable = total - (size_t)(start - memory) - __HEAP_SENTINEL;
    usable = usable - usable % __HEAP_ALIGNMENT;
    if (usable < sizeof(__heap_chunk))
        return 0;

    sentinel = (__heap_chunk*)(start + usable);
    sentinel->size = __HEAP_IN_USE;
    __heap_release((__heap_chunk*)start, usable);
    return 1;
}

static size_t __heap_request(size_t size)
{
    size_t needed;

    if (size > (size_t)-1 - sizeof(size_t) * 2 - __HEAP_ALIGNMENT)
        return 0;

    needed = __heap_align(size + sizeof(size_t) * 2, __HEAP_ALIGNMENT);
    if (needed < sizeof(__heap_chunk))
        needed = sizeof(__heap_chunk);
    return needed;
}

void* malloc(size_t size)
{
    size_t needed;
    __heap_chunk* chunk;

    if (size == 0)
        size = 1;

    needed = __heap_request(size);
    if (needed == 0)
        return (void*)0;

    chunk = __heap_take(needed);
    if (chunk == (__heap_chunk*)0)
    {
        if (__heap_grow(needed) == 0)
            return (void*)0;
        chunk = __heap_take(needed);
        if (chunk == (__heap_chunk*)0)
            return (void*)0;
    }

    return __heap_payload(chunk);
}

void free(void* pointer)
{
    __heap_chunk* chunk;
    __heap_chunk* neighbour;
    size_t size;

    if (pointer == (void*)0)
        return;

    chunk = __heap_chunk_of(pointer);
    size = __heap_size(chunk);

    neighbour = __heap_next(chunk);
    if ((neighbour->size & __HEAP_IN_USE) == 0)
    {
        __heap_unlink(neighbour);
        size = size + __heap_size(neighbour);
    }

    if ((chunk->size & __HEAP_PREVIOUS_IN_USE) == 0)
    {
        neighbour = (__heap_chunk*)((unsigned char*)chunk - chunk->previous_size);
        __heap_unlink(neighbour);
        size = size + __heap_size(neighbour);
        chunk = neighbour;
    }

    __heap_release(chunk, size);
}

size_t malloc_usable_size(void* pointer)
{
    if (pointer == (void*)0)
        return 0;
    return __heap_size(__heap_chunk_of(pointer)) - sizeof(size_t) * 2;
}

void* realloc(void* pointer, size_t size)
{
    __heap_chunk* chunk;
    __heap_chunk* next;
    size_t needed;
    size_t current;
    size_t capacity;
    void* replacement;

    if (pointer == (void*)0)
        return malloc(size);
    if (size == 0)
    {
        free(pointer);
        return (void*)0;
    }

    needed = __heap_request(size);
    if (needed == 0)
        return (void*)0;

    chunk = __heap_chunk_of(pointer);
    current = __heap_size(chunk);

    if (current >= needed)
    {
        __heap_use(chunk, needed);
        return pointer;
    }

    // Grow in place into a free neighbour
    next = __heap_next(chunk);
    if ((next->size & __HEAP_IN_USE) == 0 && current + __heap_size(next) >= needed)
    {
        __heap_unlink(next);
        chunk->size = (current + __heap_size(next)) | (chunk->size & __HEAP_PREVIOUS_IN_USE) | __HEAP_IN_USE;
        __heap_use(chunk, needed);
        return pointer;
    }

    replacement = malloc(size);
    if (replacement == (void*)0)
        return (void*)0;

    capacity = current - sizeof(size_t) * 2;
    if (capacity > size)
        capacity = size;

    memcpy(replacement, pointer, capacity);
    free(pointer);
    return replacement;
}

void* calloc(size_t count, size_t size)
{
    size_t total;
    void* pointer;

    if (size != 0 && count > (size_t)-1 / size)
        return (void*)0;

    total = count * size;
    pointer = malloc(total);
    if (pointer == (void*)0)
        return (void*)0;

    memset(pointer, 0, total);
    return pointer;
}

void* aligned_alloc(size_t alignment, size_t size)
{
    unsigned char* raw;
    unsigned char* aligned;
    __heap_chunk* chunk;
    __heap_chunk* head;
    size_t offset;
    size_t total;

    if (alignment == 0 || (alignment & (alignment - 1)) != 0)
        return (void*)0;
    if (alignment <= __HEAP_ALIGNMENT)
        return malloc(size);
    if (size > (size_t)-1 - alignment - sizeof(__heap_chunk))
        return (void*)0;

    total = size + alignment + sizeof(__heap_chunk);
    raw = (unsigned char*)malloc(total);
    if (raw == (unsigned char*)0)
        return (void*)0;

    offset = (size_t)((uintptr_t)raw % alignment);
    if (offset == 0)
        return (void*)raw;

    // Front slack becomes its own chunk and returns to the arena
    aligned = raw + (alignment - offset);
    while ((size_t)(aligned - raw) < sizeof(__heap_chunk))
        aligned = aligned + alignment;

    chunk = __heap_chunk_of((void*)raw);
    head = __heap_chunk_of((void*)aligned);
    offset = (size_t)((unsigned char*)head - (unsigned char*)chunk);
    head->size = (__heap_size(chunk) - offset) | __HEAP_IN_USE;
    chunk->size = offset | (chunk->size & __HEAP_PREVIOUS_IN_USE) | __HEAP_IN_USE;
    free(__heap_payload(chunk));
    return (void*)aligned;
}


static int __strto_digit(int character, int base)
{
    int value;

    if (character >= '0' && character <= '9')
        value = character - '0';
    else if (character >= 'a' && character <= 'z')
        value = character - 'a' + 10;
    else if (character >= 'A' && character <= 'Z')
        value = character - 'A' + 10;
    else
        return -1;

    return value < base ? value : -1;
}

// Shared by every strto* entry point; only the limit differs
static unsigned long long __strto_scan(
    const char* text,
    char** end,
    int base,
    unsigned long long limit,
    int* negative,
    int* overflow)
{
    const char* cursor = text;
    const char* digits;
    unsigned long long value = 0;
    unsigned long long cutoff;
    int cutlimit;

    *negative = 0;
    *overflow = 0;

    while (isspace((int)(unsigned char)*cursor))
        cursor = cursor + 1;

    if (*cursor == '+' || *cursor == '-')
    {
        *negative = *cursor == '-';
        cursor = cursor + 1;
    }

    if ((base == 0 || base == 16) && cursor[0] == '0' && (cursor[1] == 'x' || cursor[1] == 'X') &&
        __strto_digit((int)(unsigned char)cursor[2], 16) >= 0)
    {
        cursor = cursor + 2;
        base = 16;
    }
    else if (base == 0)
    {
        base = cursor[0] == '0' ? 8 : 10;
    }

    cutoff = limit / (unsigned long long)base;
    cutlimit = (int)(limit % (unsigned long long)base);

    digits = cursor;
    for (;;)
    {
        int digit = __strto_digit((int)(unsigned char)*cursor, base);
        if (digit < 0)
            break;

        if (value > cutoff || (value == cutoff && digit > cutlimit))
            *overflow = 1;
        else
            value = value * (unsigned long long)base + (unsigned long long)digit;

        cursor = cursor + 1;
    }

    if (end != (char**)0)
        *end = (char*)(cursor == digits ? text : cursor);

    if (*overflow != 0)
        return limit;
    return value;
}

unsigned long long strtoull(const char* restrict text, char** restrict end, int base)
{
    int negative;
    int overflow;
    unsigned long long value = __strto_scan(text, end, base, ULLONG_MAX, &negative, &overflow);

    if (overflow != 0)
    {
        errno = ERANGE;
        return ULLONG_MAX;
    }

    return negative ? 0ull - value : value;
}

unsigned long strtoul(const char* restrict text, char** restrict end, int base)
{
    int negative;
    int overflow;
    unsigned long long value = __strto_scan(text, end, base, (unsigned long long)ULONG_MAX, &negative, &overflow);

    if (overflow != 0)
    {
        errno = ERANGE;
        return ULONG_MAX;
    }

    return negative ? (unsigned long)(0ull - value) : (unsigned long)value;
}

long long strtoll(const char* restrict text, char** restrict end, int base)
{
    int negative;
    int overflow;
    unsigned long long limit;
    unsigned long long value;
    char* stop = (char*)0;

    value = __strto_scan(text, &stop, base, (unsigned long long)LLONG_MAX + 1ull, &negative, &overflow);
    if (end != (char**)0)
        *end = stop;

    limit = negative ? (unsigned long long)LLONG_MAX + 1ull : (unsigned long long)LLONG_MAX;
    if (overflow != 0 || value > limit)
    {
        errno = ERANGE;
        return negative ? LLONG_MIN : LLONG_MAX;
    }

    return negative ? -(long long)value : (long long)value;
}

long strtol(const char* restrict text, char** restrict end, int base)
{
    int negative;
    int overflow;
    unsigned long long limit;
    unsigned long long value;
    char* stop = (char*)0;

    value = __strto_scan(text, &stop, base, (unsigned long long)LONG_MAX + 1ull, &negative, &overflow);
    if (end != (char**)0)
        *end = stop;

    limit = negative ? (unsigned long long)LONG_MAX + 1ull : (unsigned long long)LONG_MAX;
    if (overflow != 0 || value > limit)
    {
        errno = ERANGE;
        return negative ? LONG_MIN : LONG_MAX;
    }

    return negative ? -(long)value : (long)value;
}

static double __strtod_scale(int exponent)
{
    double factor = 10.0;
    double result = 1.0;
    int count = exponent < 0 ? -exponent : exponent;

    while (count != 0)
    {
        if ((count & 1) != 0)
            result = result * factor;
        factor = factor * factor;
        count = count >> 1;
    }

    return exponent < 0 ? 1.0 / result : result;
}

double strtod(const char* restrict text, char** restrict end)
{
    const char* cursor = text;
    const char* digits;
    double value = 0.0;
    int negative = 0;
    int exponent = 0;
    int seen = 0;

    while (isspace((int)(unsigned char)*cursor))
        cursor = cursor + 1;

    if (*cursor == '+' || *cursor == '-')
    {
        negative = *cursor == '-';
        cursor = cursor + 1;
    }

    digits = cursor;
    while (*cursor >= '0' && *cursor <= '9')
    {
        value = value * 10.0 + (double)(*cursor - '0');
        cursor = cursor + 1;
        seen = 1;
    }

    if (*cursor == '.')
    {
        cursor = cursor + 1;
        while (*cursor >= '0' && *cursor <= '9')
        {
            value = value * 10.0 + (double)(*cursor - '0');
            exponent = exponent - 1;
            cursor = cursor + 1;
            seen = 1;
        }
    }

    if (seen != 0 && (*cursor == 'e' || *cursor == 'E'))
    {
        const char* mark = cursor;
        int exponent_negative = 0;
        int written = 0;
        int magnitude = 0;

        cursor = cursor + 1;
        if (*cursor == '+' || *cursor == '-')
        {
            exponent_negative = *cursor == '-';
            cursor = cursor + 1;
        }

        while (*cursor >= '0' && *cursor <= '9')
        {
            if (magnitude < 100000)
                magnitude = magnitude * 10 + (*cursor - '0');
            cursor = cursor + 1;
            written = 1;
        }

        if (written == 0)
            cursor = mark;
        else
            exponent = exponent + (exponent_negative ? -magnitude : magnitude);
    }

    if (end != (char**)0)
        *end = (char*)(seen ? cursor : text);
    if (seen == 0)
        return 0.0;

    if (exponent != 0)
        value = value * __strtod_scale(exponent);
    return negative ? -value : value;
}

float strtof(const char* restrict text, char** restrict end)
{
    return (float)strtod(text, end);
}

int atoi(const char* text)
{
    return (int)strtol(text, (char**)0, 10);
}

long atol(const char* text)
{
    return strtol(text, (char**)0, 10);
}

long long atoll(const char* text)
{
    return strtoll(text, (char**)0, 10);
}

double atof(const char* text)
{
    return strtod(text, (char**)0);
}

int abs(int value)
{
    return value < 0 ? -value : value;
}

long labs(long value)
{
    return value < 0 ? -value : value;
}

long long llabs(long long value)
{
    return value < 0 ? -value : value;
}

div_t div(int numerator, int denominator)
{
    div_t result;
    result.quot = numerator / denominator;
    result.rem = numerator % denominator;
    return result;
}

ldiv_t ldiv(long numerator, long denominator)
{
    ldiv_t result;
    result.quot = numerator / denominator;
    result.rem = numerator % denominator;
    return result;
}

lldiv_t lldiv(long long numerator, long long denominator)
{
    lldiv_t result;
    result.quot = numerator / denominator;
    result.rem = numerator % denominator;
    return result;
}

static unsigned long long __rand_state = 1;

int rand(void)
{
    __rand_state = __rand_state * 6364136223846793005ull + 1442695040888963407ull;
    return (int)((__rand_state >> 33) & 0x7FFFFFFFull);
}

void srand(unsigned seed)
{
    __rand_state = (unsigned long long)seed;
}

static void __sort_swap(unsigned char* left, unsigned char* right, size_t size)
{
    while (size != 0)
    {
        unsigned char value = *left;
        *left = *right;
        *right = value;
        left = left + 1;
        right = right + 1;
        size = size - 1;
    }
}

static void __sort_insertion(unsigned char* base, size_t count, size_t size, int (*compare)(const void*, const void*))
{
    size_t index;

    for (index = 1; index < count; index++)
    {
        size_t back = index;
        while (back != 0 && compare(base + back * size, base + (back - 1) * size) < 0)
        {
            __sort_swap(base + back * size, base + (back - 1) * size, size);
            back = back - 1;
        }
    }
}

// Recurse on the smaller half and loop on the larger, bounding the depth to log n
void qsort(void* base, size_t count, size_t size, int (*compare)(const void*, const void*))
{
    unsigned char* bytes = (unsigned char*)base;

    if (size == 0)
        return;

    while (count > 12)
    {
        size_t middle = count / 2;
        size_t last = count - 1;
        size_t store = 0;
        size_t index;

        if (compare(bytes + middle * size, bytes) < 0)
            __sort_swap(bytes + middle * size, bytes, size);
        if (compare(bytes + last * size, bytes + middle * size) < 0)
        {
            __sort_swap(bytes + last * size, bytes + middle * size, size);
            if (compare(bytes + middle * size, bytes) < 0)
                __sort_swap(bytes + middle * size, bytes, size);
        }
        __sort_swap(bytes, bytes + middle * size, size);

        for (index = 1; index < count; index++)
        {
            if (compare(bytes + index * size, bytes) < 0)
            {
                store = store + 1;
                __sort_swap(bytes + store * size, bytes + index * size, size);
            }
        }
        __sort_swap(bytes, bytes + store * size, size);

        if (store < count - store - 1)
        {
            qsort((void*)bytes, store, size, compare);
            bytes = bytes + (store + 1) * size;
            count = count - store - 1;
        }
        else
        {
            qsort((void*)(bytes + (store + 1) * size), count - store - 1, size, compare);
            count = store;
        }
    }

    __sort_insertion(bytes, count, size, compare);
}

void* bsearch(const void* key, const void* base, size_t count, size_t size, int (*compare)(const void*, const void*))
{
    const unsigned char* bytes = (const unsigned char*)base;

    while (count != 0)
    {
        size_t middle = count / 2;
        const unsigned char* candidate = bytes + middle * size;
        int order = compare(key, (const void*)candidate);

        if (order == 0)
            return (void*)candidate;
        if (order > 0)
        {
            bytes = candidate + size;
            count = count - middle - 1;
        }
        else
        {
            count = middle;
        }
    }

    return (void*)0;
}

extern char** environ;

char* getenv(const char* name)
{
    char** entry = environ;
    size_t length;

    if (entry == (char**)0 || name == (const char*)0)
        return (char*)0;

    length = strlen(name);
    while (*entry != (char*)0)
    {
        if (strncmp(*entry, name, length) == 0 && (*entry)[length] == '=')
            return *entry + length + 1;
        entry = entry + 1;
    }

    return (char*)0;
}

#define __ATEXIT_SLOTS 32

void __stdio_shutdown(void);

static void (*__atexit_handlers[__ATEXIT_SLOTS])(void);
static int __atexit_count;

int atexit(void (*handler)(void))
{
    if (handler == (void (*)(void))0 || __atexit_count >= __ATEXIT_SLOTS)
        return -1;

    __atexit_handlers[__atexit_count] = handler;
    __atexit_count = __atexit_count + 1;
    return 0;
}

#if defined(_WIN32) && defined(__x86_64__)

void _Exit(int status)
{
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_ExitProcess]"
        :
        : [status] "{rcx}"(status)
        : "rax", "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
}

#elif defined(_WIN32) && defined(__i386__)

void _Exit(int status)
{
    __asm__ volatile(
        "push %[status]\n"
        "call dword ptr[__imp_ExitProcess]"
        :
        : [status] "r"(status)
        : "eax", "ecx", "edx", "memory");
}

#elif defined(__linux__) && defined(__x86_64__)

void _Exit(int status)
{
    __asm__ volatile(
        "mov eax, 231\n"
        "syscall"
        :
        : [status] "{rdi}"(status)
        : "rax", "rcx", "r11", "memory");
}

#elif defined(__linux__) && defined(__i386__)

void _Exit(int status)
{
    __asm__ volatile(
        "push ebx\n"
        "mov ebx, %[status]\n"
        "mov eax, 252\n"
        ".byte 0xcd, 0x80\n"
        "pop ebx"
        :
        : [status] "r"(status)
        : "eax", "ecx", "edx", "memory");
}

#elif defined(__linux__) && defined(__aarch64__)

void _Exit(int status)
{
    __asm__ volatile(
        "mov x8, #94\n"
        "svc #0"
        :
        : [status] "{x0}"(status)
        : "x8", "memory");
}

#elif defined(__linux__) && defined(__arm__)

void _Exit(int status)
{
    __asm__ volatile(
        "mov r7, #248\n"
        "svc #0"
        :
        : [status] "{r0}"(status)
        : "r7", "memory");
}

#elif defined(__linux__) && defined(__riscv)

void _Exit(int status)
{
    __asm__ volatile(
        "addi a7, zero, 94\n"
        "ecall"
        :
        : [status] "{a0}"(status)
        : "a7", "memory");
}

#else

// Freestanding: no process to leave, so a fault is the only halt this target has
void _Exit(int status)
{
    volatile int* halt = (volatile int*)0;
    *halt = status;
}

#endif

void exit(int status)
{
    while (__atexit_count > 0)
    {
        __atexit_count = __atexit_count - 1;
        __atexit_handlers[__atexit_count]();
    }

#if defined(__CNIDARIA_FILES)
    __stdio_shutdown();
#endif

    _Exit(status);
}

void abort(void)
{
    _Exit(134);
}
