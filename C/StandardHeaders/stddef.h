#ifndef __STDDEF_H
#define __STDDEF_H

#if defined(_WIN64)
typedef unsigned long long size_t;
typedef long long ptrdiff_t;
#else
typedef unsigned long size_t;
typedef long ptrdiff_t;
#endif

#ifndef NULL
#define NULL ((void*)0)
#endif

#endif