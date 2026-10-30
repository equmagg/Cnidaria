#ifndef __TIME_H
#define __TIME_H

#include <stddef.h>
#include <sys/types.h>

typedef long clock_t;
typedef int clockid_t;

#define CLOCKS_PER_SEC 1000000l
#define TIME_UTC 1

#ifndef __STRUCT_TIMESPEC
#define __STRUCT_TIMESPEC
struct timespec
{
    time_t tv_sec;
    long tv_nsec;
};
#endif

#if defined(__linux__)
#define CLOCK_REALTIME 0
#define CLOCK_MONOTONIC 1
#define CLOCK_PROCESS_CPUTIME_ID 2
#define CLOCK_THREAD_CPUTIME_ID 3
#define CLOCK_MONOTONIC_RAW 4
#define CLOCK_REALTIME_COARSE 5
#define CLOCK_MONOTONIC_COARSE 6
#define CLOCK_BOOTTIME 7
#define TIMER_ABSTIME 1

time_t time(time_t* result);
double difftime(time_t end, time_t beginning);
clock_t clock(void);
int timespec_get(struct timespec* value, int base);
int timespec_getres(struct timespec* value, int base);
int clock_gettime(clockid_t clock, struct timespec* value);
int clock_getres(clockid_t clock, struct timespec* resolution);
int clock_nanosleep(clockid_t clock, int flags, const struct timespec* request, struct timespec* remaining);
int nanosleep(const struct timespec* request, struct timespec* remaining);
#endif

#endif
