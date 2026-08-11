#ifndef __STDDEF_H
#define __STDDEF_H

#if defined(_WIN64)
typedef unsigned long long size_t;
typedef long long ptrdiff_t;
#elif defined(__SIZEOF_POINTER__) && __SIZEOF_POINTER__ == 4
typedef unsigned int size_t;
typedef int ptrdiff_t;
#else
typedef unsigned long size_t;
typedef long ptrdiff_t;
#endif

#ifndef NULL
#define NULL ((void*)0)
#endif

#endif
