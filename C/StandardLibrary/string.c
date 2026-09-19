#include <stddef.h>
#include <stdint.h>
#include <string.h>
#include <stdlib.h>

#if defined(__riscv_vector)
#include <riscv_vector.h>

// Fault-only-first reads a whole vector past an unmeasured string end without faulting
static size_t __string_scan_zero(const unsigned char* bytes)
{
    size_t width = __riscv_vsetvlmax_e8m8();
    size_t scanned = 0;

    for (;;)
    {
        size_t taken = 0;
        vuint8m8_t chunk = __riscv_vle8ff_v_u8m8(bytes + scanned, &taken, width);
        long first = __riscv_vfirst_m_b1(__riscv_vmseq_vx_u8m8_b1(chunk, 0, taken), taken);
        if (first >= 0)
            return scanned + (size_t)first;
        scanned = scanned + taken;
    }
}
#endif

void* memcpy(void* restrict destination, const void* restrict source, size_t count)
{
    unsigned char* destination_bytes = (unsigned char*)destination;
    const unsigned char* source_bytes = (const unsigned char*)source;

    if (destination_bytes == source_bytes || count == 0)
        return destination;

#if defined(__riscv_vector)
    while (count != 0)
    {
        size_t vector_length = __riscv_vsetvl_e8m8(count);
        __riscv_vse8_v_u8m8(destination_bytes, __riscv_vle8_v_u8m8(source_bytes, vector_length), vector_length);
        destination_bytes = destination_bytes + vector_length;
        source_bytes = source_bytes + vector_length;
        count = count - vector_length;
    }
#elif defined(__SSE2__) && (defined(__x86_64__) || defined(__i386__))
    while (count >= 64)
    {
        __asm__ volatile(
            "movdqu xmm0, xmmword ptr[%[source]]\n"
            "movdqu xmm1, xmmword ptr[%[source] + 16]\n"
            "movdqu xmm2, xmmword ptr[%[source] + 32]\n"
            "movdqu xmm3, xmmword ptr[%[source] + 48]\n"
            "movdqu xmmword ptr[%[destination]], xmm0\n"
            "movdqu xmmword ptr[%[destination] + 16], xmm1\n"
            "movdqu xmmword ptr[%[destination] + 32], xmm2\n"
            "movdqu xmmword ptr[%[destination] + 48], xmm3"
            :
        : [destination] "r"(destination_bytes), [source] "r"(source_bytes)
            : "xmm0", "xmm1", "xmm2", "xmm3", "memory");
        source_bytes = source_bytes + 64;
        destination_bytes = destination_bytes + 64;
        count = count - 64;
    }

    while (count >= 16)
    {
        __asm__ volatile(
            "movdqu xmm0, xmmword ptr[%[source]]\n"
            "movdqu xmmword ptr[%[destination]], xmm0"
            :
        : [destination] "r"(destination_bytes), [source] "r"(source_bytes)
            : "xmm0", "memory");
        source_bytes = source_bytes + 16;
        destination_bytes = destination_bytes + 16;
        count = count - 16;
    }
#endif

    while (count != 0)
    {
        *destination_bytes = *source_bytes;
        destination_bytes = destination_bytes + 1;
        source_bytes = source_bytes + 1;
        count = count - 1;
    }

    return destination;
}

void* memmove(void* destination, const void* source, size_t count)
{
    unsigned char* destination_bytes = (unsigned char*)destination;
    const unsigned char* source_bytes = (const unsigned char*)source;

    if (destination_bytes == source_bytes || count == 0)
        return destination;

    // Backwards only when the destination overlaps ahead of the source
    if (destination_bytes < source_bytes || destination_bytes >= source_bytes + count)
        return memcpy(destination, source, count);

#if defined(__riscv_vector)
    while (count != 0)
    {
        size_t vector_length = __riscv_vsetvl_e8m8(count);
        size_t tail = count - vector_length;
        __riscv_vse8_v_u8m8(destination_bytes + tail, __riscv_vle8_v_u8m8(source_bytes + tail, vector_length), vector_length);
        count = tail;
    }
#else
    while (count != 0)
    {
        count = count - 1;
        destination_bytes[count] = source_bytes[count];
    }
#endif

    return destination;
}

void* memset(void* destination, int value, size_t count)
{
    unsigned char* destination_bytes = (unsigned char*)destination;
    unsigned char byte_value = (unsigned char)value;

    if (count == 0)
        return destination;

#if defined(__riscv_vector)
    vuint8m8_t fill = __riscv_vmv_v_x_u8m8(byte_value, __riscv_vsetvlmax_e8m8());
    while (count != 0)
    {
        size_t vector_length = __riscv_vsetvl_e8m8(count);
        __riscv_vse8_v_u8m8(destination_bytes, fill, vector_length);
        destination_bytes = destination_bytes + vector_length;
        count = count - vector_length;
    }
#elif defined(__SSE2__) && (defined(__x86_64__) || defined(__i386__))
    if (count >= 16)
    {
        unsigned char vector_bytes[16];
        size_t vector_index = 0;
        while (vector_index < 16)
        {
            vector_bytes[vector_index] = byte_value;
            vector_index = vector_index + 1;
        }

        while (count >= 64)
        {
            __asm__ volatile(
                "movdqu xmm0, xmmword ptr[%[pattern]]\n"
                "movdqu xmmword ptr[%[destination]], xmm0\n"
                "movdqu xmmword ptr[%[destination] + 16], xmm0\n"
                "movdqu xmmword ptr[%[destination] + 32], xmm0\n"
                "movdqu xmmword ptr[%[destination] + 48], xmm0"
                :
            : [destination] "r"(destination_bytes), [pattern] "r"(vector_bytes)
                : "xmm0", "memory");
            destination_bytes = destination_bytes + 64;
            count = count - 64;
        }

        while (count >= 16)
        {
            __asm__ volatile(
                "movdqu xmm0, xmmword ptr[%[pattern]]\n"
                "movdqu xmmword ptr[%[destination]], xmm0"
                :
            : [destination] "r"(destination_bytes), [pattern] "r"(vector_bytes)
                : "xmm0", "memory");
            destination_bytes = destination_bytes + 16;
            count = count - 16;
        }
    }
#endif

    while (count != 0)
    {
        *destination_bytes = byte_value;
        destination_bytes = destination_bytes + 1;
        count = count - 1;
    }

    return destination;
}

int memcmp(const void* left, const void* right, size_t count)
{
    const unsigned char* left_bytes = (const unsigned char*)left;
    const unsigned char* right_bytes = (const unsigned char*)right;

    if (left_bytes == right_bytes)
        return 0;

#if defined(__riscv_vector)
    while (count != 0)
    {
        size_t vector_length = __riscv_vsetvl_e8m8(count);
        vuint8m8_t left_vector = __riscv_vle8_v_u8m8(left_bytes, vector_length);
        vuint8m8_t right_vector = __riscv_vle8_v_u8m8(right_bytes, vector_length);
        long first = __riscv_vfirst_m_b1(__riscv_vmsne_vv_u8m8_b1(left_vector, right_vector, vector_length), vector_length);
        if (first >= 0)
            return (int)left_bytes[first] - (int)right_bytes[first];
        left_bytes = left_bytes + vector_length;
        right_bytes = right_bytes + vector_length;
        count = count - vector_length;
    }
#else
    while (count != 0)
    {
        if (*left_bytes != *right_bytes)
            return (int)*left_bytes - (int)*right_bytes;
        left_bytes = left_bytes + 1;
        right_bytes = right_bytes + 1;
        count = count - 1;
    }
#endif

    return 0;
}

void* memchr(const void* source, int value, size_t count)
{
    const unsigned char* bytes = (const unsigned char*)source;
    unsigned char wanted = (unsigned char)value;

#if defined(__riscv_vector)
    while (count != 0)
    {
        size_t vector_length = __riscv_vsetvl_e8m8(count);
        vuint8m8_t chunk = __riscv_vle8_v_u8m8(bytes, vector_length);
        long first = __riscv_vfirst_m_b1(__riscv_vmseq_vx_u8m8_b1(chunk, wanted, vector_length), vector_length);
        if (first >= 0)
            return (void*)(bytes + first);
        bytes = bytes + vector_length;
        count = count - vector_length;
    }
#else
    while (count != 0)
    {
        if (*bytes == wanted)
            return (void*)bytes;
        bytes = bytes + 1;
        count = count - 1;
    }
#endif

    return (void*)0;
}

void* memrchr(const void* source, int value, size_t count)
{
    const unsigned char* bytes = (const unsigned char*)source;
    unsigned char wanted = (unsigned char)value;

    while (count != 0)
    {
        count = count - 1;
        if (bytes[count] == wanted)
            return (void*)(bytes + count);
    }

    return (void*)0;
}

void* memccpy(void* restrict destination, const void* restrict source, int value, size_t count)
{
    unsigned char* destination_bytes = (unsigned char*)destination;
    const unsigned char* source_bytes = (const unsigned char*)source;
    unsigned char wanted = (unsigned char)value;

#if defined(__riscv_vector)
    while (count != 0)
    {
        size_t vector_length = __riscv_vsetvl_e8m8(count);
        vuint8m8_t chunk = __riscv_vle8_v_u8m8(source_bytes, vector_length);
        long first = __riscv_vfirst_m_b1(__riscv_vmseq_vx_u8m8_b1(chunk, wanted, vector_length), vector_length);
        size_t taken = first >= 0 ? (size_t)first + 1 : vector_length;

        __riscv_vse8_v_u8m8(destination_bytes, chunk, taken);
        destination_bytes = destination_bytes + taken;
        if (first >= 0)
            return (void*)destination_bytes;

        source_bytes = source_bytes + taken;
        count = count - taken;
    }
#else
    while (count != 0)
    {
        unsigned char current = *source_bytes;
        *destination_bytes = current;
        destination_bytes = destination_bytes + 1;
        source_bytes = source_bytes + 1;
        count = count - 1;
        if (current == wanted)
            return (void*)destination_bytes;
    }
#endif

    return (void*)0;
}

size_t strlen(const char* text)
{
#if defined(__riscv_vector)
    return __string_scan_zero((const unsigned char*)text);
#else
    const unsigned char* bytes = (const unsigned char*)text;
    const unsigned char* cursor = bytes;
    while (*cursor != 0)
        cursor = cursor + 1;
    return (size_t)(cursor - bytes);
#endif
}

size_t strnlen(const char* text, size_t limit)
{
    const unsigned char* bytes = (const unsigned char*)text;
    size_t scanned = 0;

#if defined(__riscv_vector)
    while (scanned < limit)
    {
        size_t vector_length = __riscv_vsetvl_e8m8(limit - scanned);
        vuint8m8_t chunk = __riscv_vle8_v_u8m8(bytes + scanned, vector_length);
        long first = __riscv_vfirst_m_b1(__riscv_vmseq_vx_u8m8_b1(chunk, 0, vector_length), vector_length);
        if (first >= 0)
            return scanned + (size_t)first;
        scanned = scanned + vector_length;
    }
#else
    while (scanned < limit && bytes[scanned] != 0)
        scanned = scanned + 1;
#endif

    return scanned;
}

char* stpcpy(char* restrict destination, const char* restrict source)
{
#if defined(__riscv_vector)
    const unsigned char* bytes = (const unsigned char*)source;
    unsigned char* output = (unsigned char*)destination;
    size_t width = __riscv_vsetvlmax_e8m8();
    size_t scanned = 0;

    // One pass: the scan and the copy share it
    for (;;)
    {
        size_t taken = 0;
        vuint8m8_t chunk = __riscv_vle8ff_v_u8m8(bytes + scanned, &taken, width);
        long first = __riscv_vfirst_m_b1(__riscv_vmseq_vx_u8m8_b1(chunk, 0, taken), taken);
        if (first >= 0)
        {
            __riscv_vse8_v_u8m8(output + scanned, chunk, (size_t)first + 1);
            return (char*)(output + scanned + (size_t)first);
        }

        __riscv_vse8_v_u8m8(output + scanned, chunk, taken);
        scanned = scanned + taken;
    }
#else
    size_t length = strlen(source);
    memcpy(destination, source, length + 1);
    return destination + length;
#endif
}

char* strcpy(char* restrict destination, const char* restrict source)
{
    stpcpy(destination, source);
    return destination;
}

char* stpncpy(char* restrict destination, const char* restrict source, size_t count)
{
    size_t length = strnlen(source, count);

    memcpy(destination, source, length);
    if (length < count)
        memset(destination + length, 0, count - length);

    return destination + length;
}

char* strncpy(char* restrict destination, const char* restrict source, size_t count)
{
    stpncpy(destination, source, count);
    return destination;
}

char* strcat(char* restrict destination, const char* restrict source)
{
    stpcpy(destination + strlen(destination), source);
    return destination;
}

char* strncat(char* restrict destination, const char* restrict source, size_t count)
{
    size_t end = strlen(destination);
    size_t length = strnlen(source, count);

    memcpy(destination + end, source, length);
    destination[end + length] = 0;
    return destination;
}

int strcmp(const char* left, const char* right)
{
    const unsigned char* left_bytes = (const unsigned char*)left;
    const unsigned char* right_bytes = (const unsigned char*)right;

#if defined(__riscv_vector)
    size_t width = __riscv_vsetvlmax_e8m8();
    size_t scanned = 0;

    for (;;)
    {
        size_t left_taken = 0;
        size_t right_taken = 0;
        vuint8m8_t left_chunk = __riscv_vle8ff_v_u8m8(left_bytes + scanned, &left_taken, width);
        vuint8m8_t right_chunk = __riscv_vle8ff_v_u8m8(right_bytes + scanned, &right_taken, width);
        size_t taken = left_taken < right_taken ? left_taken : right_taken;
        vbool1_t stop = __riscv_vmor_mm_b1(
            __riscv_vmsne_vv_u8m8_b1(left_chunk, right_chunk, taken),
            __riscv_vmseq_vx_u8m8_b1(left_chunk, 0, taken),
            taken);
        long first = __riscv_vfirst_m_b1(stop, taken);
        if (first >= 0)
        {
            size_t at = scanned + (size_t)first;
            return (int)left_bytes[at] - (int)right_bytes[at];
        }
        scanned = scanned + taken;
    }
#else
    while (*left_bytes != 0 && *left_bytes == *right_bytes)
    {
        left_bytes = left_bytes + 1;
        right_bytes = right_bytes + 1;
    }

    return (int)*left_bytes - (int)*right_bytes;
#endif
}

int strncmp(const char* left, const char* right, size_t count)
{
    const unsigned char* left_bytes = (const unsigned char*)left;
    const unsigned char* right_bytes = (const unsigned char*)right;

#if defined(__riscv_vector)
    size_t scanned = 0;

    while (scanned < count)
    {
        size_t taken = __riscv_vsetvl_e8m8(count - scanned);
        vuint8m8_t left_chunk = __riscv_vle8_v_u8m8(left_bytes + scanned, taken);
        vuint8m8_t right_chunk = __riscv_vle8_v_u8m8(right_bytes + scanned, taken);
        vbool1_t stop = __riscv_vmor_mm_b1(
            __riscv_vmsne_vv_u8m8_b1(left_chunk, right_chunk, taken),
            __riscv_vmseq_vx_u8m8_b1(left_chunk, 0, taken),
            taken);
        long first = __riscv_vfirst_m_b1(stop, taken);
        if (first >= 0)
        {
            size_t at = scanned + (size_t)first;
            return (int)left_bytes[at] - (int)right_bytes[at];
        }
        scanned = scanned + taken;
    }

    return 0;
#else
    while (count != 0)
    {
        if (*left_bytes != *right_bytes || *left_bytes == 0)
            return (int)*left_bytes - (int)*right_bytes;
        left_bytes = left_bytes + 1;
        right_bytes = right_bytes + 1;
        count = count - 1;
    }

    return 0;
#endif
}

int strcoll(const char* left, const char* right)
{
    return strcmp(left, right);
}

size_t strxfrm(char* restrict destination, const char* restrict source, size_t count)
{
    size_t length = strlen(source);

    if (count != 0)
    {
        size_t copied = length < count - 1 ? length : count - 1;
        memcpy(destination, source, copied);
        destination[copied] = 0;
    }

    return length;
}

char* strchrnul(const char* text, int value)
{
    const unsigned char* bytes = (const unsigned char*)text;
    unsigned char wanted = (unsigned char)value;

#if defined(__riscv_vector)
    size_t width = __riscv_vsetvlmax_e8m8();
    size_t scanned = 0;

    for (;;)
    {
        size_t taken = 0;
        vuint8m8_t chunk = __riscv_vle8ff_v_u8m8(bytes + scanned, &taken, width);
        vbool1_t hit = __riscv_vmor_mm_b1(
            __riscv_vmseq_vx_u8m8_b1(chunk, wanted, taken),
            __riscv_vmseq_vx_u8m8_b1(chunk, 0, taken),
            taken);
        long first = __riscv_vfirst_m_b1(hit, taken);
        if (first >= 0)
            return (char*)(bytes + scanned + (size_t)first);
        scanned = scanned + taken;
    }
#else
    while (*bytes != 0 && *bytes != wanted)
        bytes = bytes + 1;
    return (char*)bytes;
#endif
}

char* strchr(const char* text, int value)
{
    char* found = strchrnul(text, value);
    return *(const unsigned char*)found == (unsigned char)value ? found : (char*)0;
}

char* strrchr(const char* text, int value)
{
    unsigned char wanted = (unsigned char)value;
    const unsigned char* bytes = (const unsigned char*)text;
    size_t length = strlen(text);

    if (wanted == 0)
        return (char*)(bytes + length);

#if defined(__riscv_vector)
    while (length != 0)
    {
        size_t vector_length = __riscv_vsetvl_e8m8(length);
        size_t tail = length - vector_length;
        vuint8m8_t chunk = __riscv_vle8_v_u8m8(bytes + tail, vector_length);
        vbool1_t hit = __riscv_vmseq_vx_u8m8_b1(chunk, wanted, vector_length);
        if (__riscv_vcpop_m_b1(hit, vector_length) != 0)
        {
            size_t index = vector_length;
            while (index != 0)
            {
                index = index - 1;
                if (bytes[tail + index] == wanted)
                    return (char*)(bytes + tail + index);
            }
        }
        length = tail;
    }
#else
    while (length != 0)
    {
        length = length - 1;
        if (bytes[length] == wanted)
            return (char*)(bytes + length);
    }
#endif

    return (char*)0;
}

char* strstr(const char* haystack, const char* needle)
{
    size_t needle_length = strlen(needle);
    const char* cursor = haystack;

    if (needle_length == 0)
        return (char*)haystack;

    for (;;)
    {
        cursor = strchr(cursor, (unsigned char)needle[0]);
        if (cursor == (char*)0)
            return (char*)0;
        if (strncmp(cursor, needle, needle_length) == 0)
            return (char*)cursor;
        cursor = cursor + 1;
    }
}

static void __string_fill_set(unsigned char* table, const char* characters)
{
    const unsigned char* bytes = (const unsigned char*)characters;

    memset(table, 0, 256);
    while (*bytes != 0)
    {
        table[*bytes] = 1;
        bytes = bytes + 1;
    }
}

size_t strspn(const char* text, const char* accept)
{
    unsigned char table[256];
    const unsigned char* bytes = (const unsigned char*)text;
    size_t length = 0;

    __string_fill_set(table, accept);
    while (bytes[length] != 0 && table[bytes[length]] != 0)
        length = length + 1;

    return length;
}

size_t strcspn(const char* text, const char* reject)
{
    unsigned char table[256];
    const unsigned char* bytes = (const unsigned char*)text;
    size_t length = 0;

    __string_fill_set(table, reject);
    while (bytes[length] != 0 && table[bytes[length]] == 0)
        length = length + 1;

    return length;
}

char* strpbrk(const char* text, const char* accept)
{
    size_t offset = strcspn(text, accept);
    return text[offset] != 0 ? (char*)(text + offset) : (char*)0;
}

char* strtok_r(char* restrict text, const char* restrict separators, char** restrict state)
{
    char* cursor = text != (char*)0 ? text : *state;
    char* token;

    if (cursor == (char*)0)
        return (char*)0;

    cursor = cursor + strspn(cursor, separators);
    if (*cursor == 0)
    {
        *state = cursor;
        return (char*)0;
    }

    token = cursor;
    cursor = cursor + strcspn(cursor, separators);
    if (*cursor != 0)
    {
        *cursor = 0;
        cursor = cursor + 1;
    }

    *state = cursor;
    return token;
}

static char* __string_token_state;

char* strtok(char* restrict text, const char* restrict separators)
{
    return strtok_r(text, separators, &__string_token_state);
}

char* strndup(const char* text, size_t limit)
{
    size_t length = strnlen(text, limit);
    char* copy = (char*)malloc(length + 1);

    if (copy == (char*)0)
        return (char*)0;

    memcpy(copy, text, length);
    copy[length] = 0;
    return copy;
}

char* strdup(const char* text)
{
    return strndup(text, (size_t)-1);
}

char* strerror(int number)
{
    if (number == 0)
        return "Success";
    return "Unknown error";
}
