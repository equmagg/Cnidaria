#include <errno.h>
#include <signal.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/time.h>
#include <unistd.h>

#define __SIGNAL_WORD_BITS (8 * sizeof(unsigned long))
#define __SIGNAL_WORDS (64 / __SIGNAL_WORD_BITS)
#define __SIGNAL_WORD(sig) (((unsigned int)(sig) - 1) / __SIGNAL_WORD_BITS)
#define __SIGNAL_BIT(sig) (1ul << (((unsigned int)(sig) - 1) % __SIGNAL_WORD_BITS))

static int __signal_invalid(int sig)
{
    if (sig > 0 && sig < _NSIG)
        return 0;
    errno = EINVAL;
    return 1;
}

int sigemptyset(sigset_t* set)
{
    unsigned int index;
    for (index = 0; index < __SIGNAL_WORDS; index++)
        set->__bits[index] = 0;
    return 0;
}

int sigfillset(sigset_t* set)
{
    unsigned int index;
    for (index = 0; index < __SIGNAL_WORDS; index++)
        set->__bits[index] = ~0ul;
    return 0;
}

int sigaddset(sigset_t* set, int sig)
{
    if (__signal_invalid(sig))
        return -1;
    set->__bits[__SIGNAL_WORD(sig)] = set->__bits[__SIGNAL_WORD(sig)] | __SIGNAL_BIT(sig);
    return 0;
}

int sigdelset(sigset_t* set, int sig)
{
    if (__signal_invalid(sig))
        return -1;
    set->__bits[__SIGNAL_WORD(sig)] = set->__bits[__SIGNAL_WORD(sig)] & ~__SIGNAL_BIT(sig);
    return 0;
}

int sigismember(const sigset_t* set, int sig)
{
    if (__signal_invalid(sig))
        return -1;
    return (set->__bits[__SIGNAL_WORD(sig)] & __SIGNAL_BIT(sig)) != 0;
}

static const char* const __signal_names[32] = {
    "Unknown signal 0", "Hangup", "Interrupt", "Quit", "Illegal instruction", "Trace/breakpoint trap", "Aborted",
    "Bus error", "Floating point exception", "Killed", "User defined signal 1", "Segmentation fault",
    "User defined signal 2", "Broken pipe", "Alarm clock", "Terminated", "Stack fault", "Child exited", "Continued",
    "Stopped (signal)", "Stopped", "Stopped (tty input)", "Stopped (tty output)", "Urgent I/O condition",
    "CPU time limit exceeded", "File size limit exceeded", "Virtual timer expired", "Profiling timer expired",
    "Window changed", "I/O possible", "Power failure", "Bad system call"
};

char* strsignal(int sig)
{
    static char text[32];
    if (sig > 0 && sig < SIGRTMIN)
        return (char*)__signal_names[sig];
    if (sig >= SIGRTMIN && sig <= SIGRTMAX)
        snprintf(text, sizeof(text), "Real-time signal %d", sig - SIGRTMIN);
    else
        snprintf(text, sizeof(text), "Unknown signal %d", sig);
    return text;
}

#if defined(__linux__)

#if defined(__x86_64__)
#define __NR_rt_sigaction 13
#define __NR_rt_sigprocmask 14
#define __NR_nanosleep 35
#define __NR_getitimer 36
#define __NR_setitimer 38
#define __NR_getpid 39
#define __NR_kill 62
#define __NR_setpgid 109
#define __NR_getppid 110
#define __NR_setsid 112
#define __NR_getpgid 121
#define __NR_getsid 124
#define __NR_rt_sigpending 127
#define __NR_rt_sigtimedwait 128
#define __NR_rt_sigqueueinfo 129
#define __NR_rt_sigsuspend 130
#define __NR_sigaltstack 131
#define __NR_gettid 186
#define __NR_tkill 200
#elif defined(__i386__) || defined(__arm__)
#define __NR_getpid 20
#define __NR_kill 37
#define __NR_setpgid 57
#define __NR_getppid 64
#define __NR_setsid 66
#define __NR_setitimer 104
#define __NR_getitimer 105
#define __NR_getpgid 132
#define __NR_getsid 147
#define __NR_rt_sigaction 174
#define __NR_rt_sigprocmask 175
#define __NR_rt_sigpending 176
#define __NR_rt_sigqueueinfo 178
#define __NR_rt_sigsuspend 179
#define __NR_sigaltstack 186
#define __NR_gettid 224
#define __NR_tkill 238
#else
#define __NR_nanosleep 101
#define __NR_getitimer 102
#define __NR_setitimer 103
#define __NR_kill 129
#define __NR_tkill 130
#define __NR_sigaltstack 132
#define __NR_rt_sigsuspend 133
#define __NR_rt_sigaction 134
#define __NR_rt_sigprocmask 135
#define __NR_rt_sigpending 136
#define __NR_rt_sigtimedwait 137
#define __NR_rt_sigqueueinfo 138
#define __NR_setpgid 154
#define __NR_getpgid 155
#define __NR_getsid 156
#define __NR_setsid 157
#define __NR_getpid 172
#define __NR_getppid 173
#define __NR_gettid 178
#endif

// A 32-bit kernel takes 64-bit time only through the calls that were added for it
#if __SIZEOF_POINTER__ == 4
#define __NR_clock_nanosleep_time64 407
#define __NR_rt_sigtimedwait_time64 421
#endif

long __syscall(long number, long a, long b, long c, long d, long e);

static long __signal_result(long value)
{
    if (value < 0 && value >= -4095)
    {
        errno = (int)-value;
        return -1;
    }
    return value;
}

struct __kernel_sigaction
{
    void (*handler)(int);
    unsigned long flags;
#if !defined(__riscv)
    void (*restorer)(void);
#endif
    unsigned char mask[8];
};

#if defined(__x86_64__)
typedef void (*__signal_code_t)(void);

// The kernel returns from a handler only through code the process names, and this is that code
static __signal_code_t __signal_restorer(void)
{
    union
    {
        void* data;
        void (*code)(void);
    } address;
    __asm__ volatile(
        "lea %[address], [rip + restore]\n"
        "jmp done\n"
        "restore:\n"
        "mov eax, 15\n"
        "syscall\n"
        "done:"
        : [address] "=r"(address.data));
    return address.code;
}
#endif

int sigaction(int sig, const struct sigaction* restrict action, struct sigaction* restrict old)
{
    struct __kernel_sigaction next;
    struct __kernel_sigaction previous;

    if (__signal_invalid(sig))
        return -1;
    if (action != (const struct sigaction*)0)
    {
        memset(&next, 0, sizeof(next));
        next.handler = action->sa_handler;
        next.flags = (unsigned long)(unsigned int)action->sa_flags;
#if defined(__x86_64__)
        next.flags = next.flags | SA_RESTORER;
        next.restorer = __signal_restorer();
#elif !defined(__riscv)
        next.restorer = action->sa_restorer;
#endif
        memcpy(next.mask, &action->sa_mask, sizeof(next.mask));
    }
    if (__signal_result(__syscall(
            __NR_rt_sigaction,
            sig,
            action != (const struct sigaction*)0 ? (long)(uintptr_t)&next : 0,
            old != (struct sigaction*)0 ? (long)(uintptr_t)&previous : 0,
            8,
            0)) < 0)
        return -1;
    if (old != (struct sigaction*)0)
    {
        memset(old, 0, sizeof(*old));
        old->sa_handler = previous.handler;
        old->sa_flags = (int)previous.flags;
#if !defined(__riscv)
        old->sa_restorer = previous.restorer;
#endif
        memcpy(&old->sa_mask, previous.mask, sizeof(previous.mask));
    }
    return 0;
}

__sighandler_t signal(int sig, __sighandler_t handler)
{
    struct sigaction action;
    struct sigaction old;

    memset(&action, 0, sizeof(action));
    action.sa_handler = handler;
    action.sa_flags = SA_RESTART;
    if (sigaction(sig, &action, &old) < 0)
        return SIG_ERR;
    return old.sa_handler;
}

int siginterrupt(int sig, int flag)
{
    struct sigaction action;

    if (sigaction(sig, (const struct sigaction*)0, &action) < 0)
        return -1;
    if (flag != 0)
        action.sa_flags = action.sa_flags & ~SA_RESTART;
    else
        action.sa_flags = action.sa_flags | SA_RESTART;
    return sigaction(sig, &action, (struct sigaction*)0);
}

int raise(int sig)
{
    return (int)__signal_result(__syscall(__NR_tkill, __syscall(__NR_gettid, 0, 0, 0, 0, 0), sig, 0, 0, 0));
}

int kill(pid_t pid, int sig)
{
    return (int)__signal_result(__syscall(__NR_kill, pid, sig, 0, 0, 0));
}

int killpg(pid_t group, int sig)
{
    if (group < 0)
    {
        errno = EINVAL;
        return -1;
    }
    return kill(-group, sig);
}

int sigprocmask(int how, const sigset_t* restrict set, sigset_t* restrict old)
{
    return (int)__signal_result(__syscall(__NR_rt_sigprocmask, how, (long)(uintptr_t)set, (long)(uintptr_t)old, 8, 0));
}

int sigpending(sigset_t* set)
{
    return (int)__signal_result(__syscall(__NR_rt_sigpending, (long)(uintptr_t)set, 8, 0, 0, 0));
}

int sigsuspend(const sigset_t* mask)
{
    return (int)__signal_result(__syscall(__NR_rt_sigsuspend, (long)(uintptr_t)mask, 8, 0, 0, 0));
}

int sigtimedwait(const sigset_t* restrict set, siginfo_t* restrict info, const struct timespec* restrict timeout)
{
#if __SIZEOF_POINTER__ == 4
    long long span[2];

    if (timeout != (const struct timespec*)0)
    {
        span[0] = timeout->tv_sec;
        span[1] = timeout->tv_nsec;
    }
    return (int)__signal_result(__syscall(
        __NR_rt_sigtimedwait_time64,
        (long)(uintptr_t)set,
        (long)(uintptr_t)info,
        timeout != (const struct timespec*)0 ? (long)(uintptr_t)span : 0,
        8,
        0));
#else
    return (int)__signal_result(__syscall(__NR_rt_sigtimedwait, (long)(uintptr_t)set, (long)(uintptr_t)info, (long)(uintptr_t)timeout, 8, 0));
#endif
}

int sigwaitinfo(const sigset_t* restrict set, siginfo_t* restrict info)
{
    return sigtimedwait(set, info, (const struct timespec*)0);
}

int sigwait(const sigset_t* restrict set, int* restrict sig)
{
    int result;

    do
        result = sigtimedwait(set, (siginfo_t*)0, (const struct timespec*)0);
    while (result < 0 && errno == EINTR);
    if (result < 0)
        return errno;
    *sig = result;
    return 0;
}

int sigqueue(pid_t pid, int sig, const union sigval value)
{
    siginfo_t info;

    memset(&info, 0, sizeof(info));
    info.si_signo = sig;
    info.si_code = SI_QUEUE;
    info.si_pid = getpid();
    info.si_value = value;
    return (int)__signal_result(__syscall(__NR_rt_sigqueueinfo, pid, sig, (long)(uintptr_t)&info, 0, 0));
}

int sigaltstack(const stack_t* restrict stack, stack_t* restrict old)
{
    return (int)__signal_result(__syscall(__NR_sigaltstack, (long)(uintptr_t)stack, (long)(uintptr_t)old, 0, 0, 0));
}

void psignal(int sig, const char* message)
{
    if (message != (const char*)0 && message[0] != 0)
    {
        fputs(message, stderr);
        fputs(": ", stderr);
    }
    fputs(strsignal(sig), stderr);
    fputc('\n', stderr);
    fflush(stderr);
}

pid_t getpid(void)
{
    return (pid_t)__syscall(__NR_getpid, 0, 0, 0, 0, 0);
}

pid_t getppid(void)
{
    return (pid_t)__syscall(__NR_getppid, 0, 0, 0, 0, 0);
}

pid_t getpgid(pid_t pid)
{
    return (pid_t)__signal_result(__syscall(__NR_getpgid, pid, 0, 0, 0, 0));
}

pid_t getpgrp(void)
{
    return getpgid(0);
}

int setpgid(pid_t pid, pid_t group)
{
    return (int)__signal_result(__syscall(__NR_setpgid, pid, group, 0, 0, 0));
}

pid_t getsid(pid_t pid)
{
    return (pid_t)__signal_result(__syscall(__NR_getsid, pid, 0, 0, 0, 0));
}

pid_t setsid(void)
{
    return (pid_t)__signal_result(__syscall(__NR_setsid, 0, 0, 0, 0, 0));
}

int pause(void)
{
    sigset_t mask;

    if (sigprocmask(SIG_BLOCK, (const sigset_t*)0, &mask) < 0)
        return -1;
    return sigsuspend(&mask);
}

// The kernel counts time in the width of a long, whatever width the program's time_t has
int setitimer(int which, const struct itimerval* restrict value, struct itimerval* restrict old)
{
    long next[4];
    long previous[4];

    if (value != (const struct itimerval*)0)
    {
        next[0] = (long)value->it_interval.tv_sec;
        next[1] = value->it_interval.tv_usec;
        next[2] = (long)value->it_value.tv_sec;
        next[3] = value->it_value.tv_usec;
    }
    if (__signal_result(__syscall(
            __NR_setitimer,
            which,
            value != (const struct itimerval*)0 ? (long)(uintptr_t)next : 0,
            old != (struct itimerval*)0 ? (long)(uintptr_t)previous : 0,
            0,
            0)) < 0)
        return -1;
    if (old != (struct itimerval*)0)
    {
        old->it_interval.tv_sec = previous[0];
        old->it_interval.tv_usec = previous[1];
        old->it_value.tv_sec = previous[2];
        old->it_value.tv_usec = previous[3];
    }
    return 0;
}

int getitimer(int which, struct itimerval* value)
{
    long current[4];

    if (__signal_result(__syscall(__NR_getitimer, which, (long)(uintptr_t)current, 0, 0, 0)) < 0)
        return -1;
    value->it_interval.tv_sec = current[0];
    value->it_interval.tv_usec = current[1];
    value->it_value.tv_sec = current[2];
    value->it_value.tv_usec = current[3];
    return 0;
}

unsigned int alarm(unsigned int seconds)
{
    struct itimerval next;
    struct itimerval old;
    unsigned int remaining;

    memset(&next, 0, sizeof(next));
    next.it_value.tv_sec = seconds;
    if (setitimer(ITIMER_REAL, &next, &old) < 0)
        return 0;
    remaining = (unsigned int)old.it_value.tv_sec;
    if (old.it_value.tv_usec >= 500000 || (remaining == 0 && old.it_value.tv_usec > 0))
        remaining = remaining + 1;
    return remaining;
}

unsigned int sleep(unsigned int seconds)
{
#if __SIZEOF_POINTER__ == 4
    long long request[2];
    long long remaining[2];

    request[0] = seconds;
    request[1] = 0;
    if (__syscall(__NR_clock_nanosleep_time64, 0, 0, (long)(uintptr_t)request, (long)(uintptr_t)remaining, 0) == -EINTR)
        return (unsigned int)remaining[0];
#else
    long request[2];
    long remaining[2];

    request[0] = (long)seconds;
    request[1] = 0;
    if (__syscall(__NR_nanosleep, (long)(uintptr_t)request, (long)(uintptr_t)remaining, 0, 0, 0) == -EINTR)
        return (unsigned int)remaining[0];
#endif
    return 0;
}

#else

// Without a kernel to deliver them, signals are only what raise makes of them
static __sighandler_t __signal_handlers[_NSIG];

__sighandler_t signal(int sig, __sighandler_t handler)
{
    __sighandler_t old;

    if (__signal_invalid(sig))
        return SIG_ERR;
    if (sig == SIGKILL || sig == SIGSTOP)
    {
        errno = EINVAL;
        return SIG_ERR;
    }
    old = __signal_handlers[sig];
    __signal_handlers[sig] = handler;
    return old;
}

int raise(int sig)
{
    __sighandler_t handler;

    if (__signal_invalid(sig))
        return -1;
    handler = __signal_handlers[sig];
    if (handler == SIG_IGN)
        return 0;
    if (handler == SIG_DFL)
    {
        if (sig == SIGCHLD || sig == SIGCONT || sig == SIGURG || sig == SIGWINCH)
            return 0;
        _Exit(128 + sig);
    }
    __signal_handlers[sig] = SIG_DFL;
    handler(sig);
    return 0;
}

#endif
