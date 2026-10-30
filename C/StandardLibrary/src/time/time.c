#include <errno.h>
#include <limits.h>
#include <stdint.h>
#include <sys/time.h>
#include <time.h>
#include <unistd.h>

#if defined(__linux__)

#if __SIZEOF_POINTER__ == 4
#define __NR_clock_gettime 403
#define __NR_clock_getres 406
#define __NR_clock_nanosleep 407
#elif defined(__x86_64__)
#define __NR_clock_gettime 228
#define __NR_clock_getres 229
#define __NR_clock_nanosleep 230
#else
#define __NR_clock_gettime 113
#define __NR_clock_getres 114
#define __NR_clock_nanosleep 115
#endif

long __syscall(long number, long a, long b, long c, long d, long e, long f);

static int __clock_read(long number, clockid_t clock, struct timespec* value)
{
    long result;
#if __SIZEOF_POINTER__ == 4
    long long wide[2];
    result = __syscall(number, clock, value != NULL ? (long)(uintptr_t)wide : 0, 0, 0, 0, 0);
    if (result == 0 && value != NULL)
    {
        value->tv_sec = wide[0];
        value->tv_nsec = (long)wide[1];
    }
#else
    result = __syscall(number, clock, (long)(uintptr_t)value, 0, 0, 0, 0);
#endif
    if (result < 0)
    {
        errno = (int)-result;
        return -1;
    }
    return 0;
}

int clock_gettime(clockid_t clock, struct timespec* value)
{
    return __clock_read(__NR_clock_gettime, clock, value);
}

int clock_getres(clockid_t clock, struct timespec* resolution)
{
    return __clock_read(__NR_clock_getres, clock, resolution);
}

int clock_nanosleep(clockid_t clock, int flags, const struct timespec* request, struct timespec* remaining)
{
    long result;
#if __SIZEOF_POINTER__ == 4
    long long span[2];
    long long rest[2];
    if (request == NULL)
        return EFAULT;
    span[0] = request->tv_sec;
    span[1] = request->tv_nsec;
    result = __syscall(__NR_clock_nanosleep, clock, flags, (long)(uintptr_t)span, remaining != NULL ? (long)(uintptr_t)rest : 0, 0, 0);
    if (result == -EINTR && remaining != NULL && (flags & TIMER_ABSTIME) == 0)
    {
        remaining->tv_sec = rest[0];
        remaining->tv_nsec = (long)rest[1];
    }
#else
    result = __syscall(__NR_clock_nanosleep, clock, flags, (long)(uintptr_t)request, (long)(uintptr_t)remaining, 0, 0);
#endif
    return (int)-result;
}

int nanosleep(const struct timespec* request, struct timespec* remaining)
{
    int result = clock_nanosleep(CLOCK_MONOTONIC, 0, request, remaining);
    if (result != 0)
    {
        errno = result;
        return -1;
    }
    return 0;
}

int gettimeofday(struct timeval* restrict value, void* restrict timezone)
{
    struct timespec current;
    (void)timezone;
    if (value == NULL)
        return 0;
    if (clock_gettime(CLOCK_REALTIME, &current) != 0)
        return -1;
    value->tv_sec = current.tv_sec;
    value->tv_usec = current.tv_nsec / 1000;
    return 0;
}

time_t time(time_t* result)
{
    struct timespec current;
    if (clock_gettime(CLOCK_REALTIME, &current) != 0)
        return (time_t)-1;
    if (result != NULL)
        *result = current.tv_sec;
    return current.tv_sec;
}

double difftime(time_t end, time_t beginning)
{
    return end >= beginning ? (double)((unsigned long long)end - (unsigned long long)beginning) : -(double)((unsigned long long)beginning - (unsigned long long)end);
}

clock_t clock(void)
{
    struct timespec current;
    clock_t fraction;
    if (clock_gettime(CLOCK_PROCESS_CPUTIME_ID, &current) != 0)
        return (clock_t)-1;
    fraction = current.tv_nsec / (1000000000l / CLOCKS_PER_SEC);
    if (current.tv_sec > (LONG_MAX - fraction) / CLOCKS_PER_SEC)
        return (clock_t)-1;
    return (clock_t)current.tv_sec * CLOCKS_PER_SEC + fraction;
}

int timespec_get(struct timespec* value, int base)
{
    return base == TIME_UTC && clock_gettime(CLOCK_REALTIME, value) == 0 ? TIME_UTC : 0;
}

int timespec_getres(struct timespec* value, int base)
{
    return base == TIME_UTC && clock_getres(CLOCK_REALTIME, value) == 0 ? TIME_UTC : 0;
}

unsigned int sleep(unsigned int seconds)
{
    struct timespec request;
    struct timespec remaining;
    unsigned int result = 0;
    int saved_errno = errno;
    request.tv_sec = seconds;
    request.tv_nsec = 0;
    if (nanosleep(&request, &remaining) != 0 && errno == EINTR)
        result = (unsigned int)remaining.tv_sec + (remaining.tv_nsec != 0);
    errno = saved_errno;
    return result;
}

int usleep(useconds_t microseconds)
{
    struct timespec request;
    request.tv_sec = microseconds / 1000000u;
    request.tv_nsec = (long)(microseconds % 1000000u) * 1000l;
    return nanosleep(&request, NULL);
}

#endif
