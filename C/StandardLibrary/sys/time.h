#ifndef __SYS_TIME_H
#define __SYS_TIME_H

#include <sys/types.h>

typedef long suseconds_t;

struct timeval
{
    time_t tv_sec;
    suseconds_t tv_usec;
};

#if defined(__linux__)

#define ITIMER_REAL 0
#define ITIMER_VIRTUAL 1
#define ITIMER_PROF 2

struct itimerval
{
    struct timeval it_interval;
    struct timeval it_value;
};

int getitimer(int which, struct itimerval* value);
int setitimer(int which, const struct itimerval* restrict value, struct itimerval* restrict old);

#endif

#endif
