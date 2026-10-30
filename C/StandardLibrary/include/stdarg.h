#ifndef __STDARG_H
#define __STDARG_H

#define __VA_KIND_GP 0u
#define __VA_KIND_FP 1u
#define __VA_KIND_OVERFLOW 2u
#define __VA_SLOT_SIZE(type) ((sizeof(type) + sizeof(void*) - 1u) & ~(sizeof(void*) - 1u))
#define __VA_ALIGN(type) _Alignof(type)

#if defined(__x86_64__) && !defined(_WIN64)
typedef struct __va_list_tag
{
    unsigned int gp_offset;
    unsigned int fp_offset;
    void* overflow_arg_area;
    void* reg_save_area;
} va_list[1];
void __builtin_va_start(va_list ap);
void* __builtin_va_arg(va_list ap, unsigned int kind, unsigned int size, unsigned int align);
#define __VA_KIND(type) _Generic(*(type*)0, float: __VA_KIND_FP, double: __VA_KIND_FP, long double: __VA_KIND_OVERFLOW, default: __VA_KIND_GP)
#define va_start(ap, last) __builtin_va_start(ap)
#define va_arg(ap, type) (*(type*)__builtin_va_arg((ap), __VA_KIND(type), sizeof(type), __VA_ALIGN(type)))
#define va_copy(dst, src) ((dst)[0] = (src)[0])
#elif defined(__aarch64__) && !defined(_WIN32) && !defined(__APPLE__)
typedef struct __va_list_tag
{
    void* stack;
    void* gr_top;
    void* vr_top;
    int gr_offset;
    int vr_offset;
} va_list;
void __builtin_va_start(va_list* ap);
void* __builtin_va_arg(va_list* ap, unsigned int kind, unsigned int size, unsigned int align);
#define __VA_KIND(type) _Generic(*(type*)0, float: __VA_KIND_FP, double: __VA_KIND_FP, long double: __VA_KIND_FP, default: __VA_KIND_GP)
#define va_start(ap, last) __builtin_va_start(&(ap))
#define va_arg(ap, type) (*(type*)__builtin_va_arg(&(ap), __VA_KIND(type), sizeof(type), __VA_ALIGN(type)))
#define va_copy(dst, src) ((dst) = (src))
#elif defined(__arm__) && !defined(_WIN32) && !defined(__APPLE__)
typedef struct __va_list_tag
{
    void* stack;
} va_list;
void __builtin_va_start(va_list* ap);
void* __builtin_va_arg(va_list* ap, unsigned int kind, unsigned int size, unsigned int align);
#define va_start(ap, last) __builtin_va_start(&(ap))
#define va_arg(ap, type) (*(type*)__builtin_va_arg(&(ap), __VA_KIND_GP, sizeof(type), __VA_ALIGN(type)))
#define va_copy(dst, src) ((dst) = (src))
#else
typedef char* va_list;
void __builtin_va_start(va_list* ap);
void* __builtin_va_arg(va_list* ap, unsigned int kind, unsigned int size, unsigned int align);
#define va_start(ap, last) __builtin_va_start(&(ap))
#define va_arg(ap, type) (*(type*)__builtin_va_arg(&(ap), __VA_KIND_GP, sizeof(type), __VA_ALIGN(type)))
#define va_copy(dst, src) ((dst) = (src))
#endif

#define va_end(ap) ((void)0)

#endif