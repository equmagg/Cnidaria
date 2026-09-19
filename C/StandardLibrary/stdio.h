#ifndef __STDIO_H
#define __STDIO_H

#include <stddef.h>
#include <stdarg.h>

#define EOF (-1)
#define BUFSIZ 1024
#define FOPEN_MAX 16
#define FILENAME_MAX 260

#define _IOFBF 0
#define _IOLBF 1
#define _IONBF 2

#ifndef SEEK_SET
#define SEEK_SET 0
#define SEEK_CUR 1
#define SEEK_END 2
#endif

// A platform with no files gets the formatting and nothing that would pretend to reach a disk
#if defined(__linux__) || (defined(_WIN32) && defined(__x86_64__))
#define __CNIDARIA_FILES 1
#endif

int printf(const char* format, ...);
int vprintf(const char* format, va_list arguments);
int snprintf(char* buffer, size_t size, const char* format, ...);
int vsnprintf(char* buffer, size_t size, const char* format, va_list arguments);
int sprintf(char* buffer, const char* format, ...);
int vsprintf(char* buffer, const char* format, va_list arguments);
int puts(const char* text);
int putchar(int character);

#if defined(__CNIDARIA_FILES)

typedef struct __file FILE;

FILE* __stdio_stream(int index);

#define stdin (__stdio_stream(0))
#define stdout (__stdio_stream(1))
#define stderr (__stdio_stream(2))

FILE* fopen(const char* path, const char* mode);
FILE* fdopen(int descriptor, const char* mode);
FILE* freopen(const char* path, const char* mode, FILE* stream);
int fclose(FILE* stream);
int fflush(FILE* stream);
int fileno(FILE* stream);
int setvbuf(FILE* stream, char* buffer, int mode, size_t size);
void setbuf(FILE* stream, char* buffer);

size_t fread(void* destination, size_t size, size_t count, FILE* stream);
size_t fwrite(const void* source, size_t size, size_t count, FILE* stream);

int fgetc(FILE* stream);
int getc(FILE* stream);
int getchar(void);
int ungetc(int value, FILE* stream);
char* fgets(char* text, int size, FILE* stream);

int fputc(int value, FILE* stream);
int putc(int value, FILE* stream);
int fputs(const char* text, FILE* stream);

int fseek(FILE* stream, long offset, int whence);
long ftell(FILE* stream);
void rewind(FILE* stream);

int feof(FILE* stream);
int ferror(FILE* stream);
void clearerr(FILE* stream);

int fprintf(FILE* stream, const char* format, ...);
int vfprintf(FILE* stream, const char* format, va_list arguments);

int remove(const char* path);
int rename(const char* from, const char* to);
void perror(const char* text);

#endif

#if defined(__linux__)
void shutdown(void);
#endif

#endif
