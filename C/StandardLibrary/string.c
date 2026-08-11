#include <stddef.h>

void* memcpy(void* restrict destination, const void* restrict source, size_t count)
{
    unsigned char* destination_bytes = (unsigned char*)destination;
    const unsigned char* source_bytes = (const unsigned char*)source;

    if (destination_bytes == source_bytes || count == 0)
        return destination;

#if defined(__riscv_vector)
    while (count != 0)
    {
        size_t vector_length;
        __asm__ volatile(
            "vsetvli %[vector_length], %[count], e8, m1, ta, ma"
            : [vector_length] "=r"(vector_length)
            : [count] "r"(count));
        __asm__ volatile(
            "vle8.v v0, 0(%[source])\n"
            "vse8.v v0, 0(%[destination])"
            :
        : [destination] "r"(destination_bytes), [source] "r"(source_bytes)
            : "v0", "memory");
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

void* memset(void* destination, int value, size_t count)
{
    unsigned char* destination_bytes = (unsigned char*)destination;
    unsigned char byte_value = (unsigned char)value;

    if (count == 0)
        return destination;

#if defined(__riscv_vector)
    while (count != 0)
    {
        size_t vector_length;
        __asm__ volatile(
            "vsetvli %[vector_length], %[count], e8, m1, ta, ma"
            : [vector_length] "=r"(vector_length)
            : [count] "r"(count));
        __asm__ volatile(
            "vxor.vv v0, v0, v0\n"
            "vadd.vx v0, v0, %[value]\n"
            "vse8.v v0, 0(%[destination])"
            :
        : [destination] "r"(destination_bytes), [value] "r"((size_t)byte_value)
            : "v0", "memory");
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
