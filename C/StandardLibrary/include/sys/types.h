#ifndef __SYS_TYPES_H
#define __SYS_TYPES_H

#include <stddef.h>

typedef long long off_t;
typedef unsigned int mode_t;
typedef unsigned long long dev_t;
typedef unsigned long long ino_t;
typedef unsigned int nlink_t;
typedef unsigned int uid_t;
typedef unsigned int gid_t;
typedef long long blksize_t;
typedef long long blkcnt_t;
typedef long long time_t;
typedef int pid_t;
typedef unsigned int useconds_t;

typedef struct __pthread* pthread_t;
typedef unsigned int pthread_key_t;
typedef int pthread_once_t;
typedef volatile int pthread_spinlock_t;

typedef struct
{
    int __detach_state;
    size_t __guard_size;
    size_t __stack_size;
    void* __stack;
} pthread_attr_t;

typedef struct
{
    volatile int __lock;
    int __kind;
    volatile int __owner;
    int __count;
} pthread_mutex_t;

typedef struct
{
    int __kind;
} pthread_mutexattr_t;

typedef struct
{
    volatile int __sequence;
    volatile int __waiters;
    int __clock;
    int __shared;
} pthread_cond_t;

typedef struct
{
    int __clock;
    int __shared;
} pthread_condattr_t;

typedef struct
{
    volatile int __lock;
    volatile int __waiters;
    int __shared;
} pthread_rwlock_t;

typedef struct
{
    int __shared;
} pthread_rwlockattr_t;

typedef struct
{
    volatile int __sequence;
    volatile int __count;
    unsigned int __limit;
    int __shared;
} pthread_barrier_t;

typedef struct
{
    int __shared;
} pthread_barrierattr_t;

#if defined(_WIN64) || (defined(__SIZEOF_POINTER__) && __SIZEOF_POINTER__ == 8)
typedef long long ssize_t;
#else
typedef int ssize_t;
#endif

#endif
