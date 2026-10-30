#ifndef __POLL_H
#define __POLL_H

#include <signal.h>

#define POLLIN 0x001
#define POLLPRI 0x002
#define POLLOUT 0x004
#define POLLERR 0x008
#define POLLHUP 0x010
#define POLLNVAL 0x020
#define POLLRDNORM 0x040
#define POLLRDBAND 0x080
#define POLLWRNORM 0x100
#define POLLWRBAND 0x200

typedef unsigned long nfds_t;

struct pollfd
{
    int fd;
    short events;
    short revents;
};

#if defined(__linux__)
int poll(struct pollfd* fds, nfds_t count, int timeout);
int ppoll(struct pollfd* fds, nfds_t count, const struct timespec* timeout, const sigset_t* mask);
#endif

#endif
