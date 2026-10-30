#ifndef __SYS_UIO_H
#define __SYS_UIO_H

#include <stddef.h>
#include <sys/types.h>

#define IOV_MAX 1024

struct iovec
{
    void* iov_base;
    size_t iov_len;
};

#if defined(__linux__)
ssize_t readv(int descriptor, const struct iovec* vectors, int count);
ssize_t writev(int descriptor, const struct iovec* vectors, int count);
#endif

#endif
