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

#if defined(_WIN64) || (defined(__SIZEOF_POINTER__) && __SIZEOF_POINTER__ == 8)
typedef long long ssize_t;
#else
typedef int ssize_t;
#endif

#endif
