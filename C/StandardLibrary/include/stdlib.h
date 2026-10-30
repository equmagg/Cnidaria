#ifndef __STDLIB_H
#define __STDLIB_H

#include <stddef.h>

#define EXIT_SUCCESS 0
#define EXIT_FAILURE 1
#define RAND_MAX 2147483647
#define MB_CUR_MAX 1

typedef struct { int quot; int rem; } div_t;
typedef struct { long quot; long rem; } ldiv_t;
typedef struct { long long quot; long long rem; } lldiv_t;

void* malloc(size_t size);
void free(void* pointer);
void* realloc(void* pointer, size_t size);
void* calloc(size_t count, size_t size);
void* aligned_alloc(size_t alignment, size_t size);
size_t malloc_usable_size(void* pointer);

int atoi(const char* text);
long atol(const char* text);
long long atoll(const char* text);
double atof(const char* text);

long strtol(const char* restrict text, char** restrict end, int base);
long long strtoll(const char* restrict text, char** restrict end, int base);
unsigned long strtoul(const char* restrict text, char** restrict end, int base);
unsigned long long strtoull(const char* restrict text, char** restrict end, int base);
double strtod(const char* restrict text, char** restrict end);
float strtof(const char* restrict text, char** restrict end);

int abs(int value);
long labs(long value);
long long llabs(long long value);
div_t div(int numerator, int denominator);
ldiv_t ldiv(long numerator, long denominator);
lldiv_t lldiv(long long numerator, long long denominator);

int rand(void);
void srand(unsigned seed);

void qsort(void* base, size_t count, size_t size, int (*compare)(const void*, const void*));
void* bsearch(const void* key, const void* base, size_t count, size_t size, int (*compare)(const void*, const void*));

char* getenv(const char* name);

void exit(int status);
void _Exit(int status);
void abort(void);
int atexit(void (*handler)(void));

#endif
