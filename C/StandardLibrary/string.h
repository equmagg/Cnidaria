#ifndef __STRING_H
#define __STRING_H

#include <stddef.h>

void* memcpy(void* restrict destination, const void* restrict source, size_t count);
void* memmove(void* destination, const void* source, size_t count);
void* memset(void* destination, int value, size_t count);
int memcmp(const void* left, const void* right, size_t count);
void* memchr(const void* source, int value, size_t count);
void* memrchr(const void* source, int value, size_t count);
void* memccpy(void* restrict destination, const void* restrict source, int value, size_t count);

size_t strlen(const char* text);
size_t strnlen(const char* text, size_t limit);

char* strcpy(char* restrict destination, const char* restrict source);
char* strncpy(char* restrict destination, const char* restrict source, size_t count);
char* stpcpy(char* restrict destination, const char* restrict source);
char* stpncpy(char* restrict destination, const char* restrict source, size_t count);
char* strcat(char* restrict destination, const char* restrict source);
char* strncat(char* restrict destination, const char* restrict source, size_t count);

int strcmp(const char* left, const char* right);
int strncmp(const char* left, const char* right, size_t count);
int strcoll(const char* left, const char* right);
size_t strxfrm(char* restrict destination, const char* restrict source, size_t count);

char* strchr(const char* text, int value);
char* strrchr(const char* text, int value);
char* strchrnul(const char* text, int value);
char* strstr(const char* haystack, const char* needle);
char* strpbrk(const char* text, const char* accept);
size_t strspn(const char* text, const char* accept);
size_t strcspn(const char* text, const char* reject);
char* strtok(char* restrict text, const char* restrict separators);
char* strtok_r(char* restrict text, const char* restrict separators, char** restrict state);

char* strdup(const char* text);
char* strndup(const char* text, size_t limit);
char* strerror(int number);

#endif
