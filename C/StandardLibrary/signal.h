#ifndef __SIGNAL_H
#define __SIGNAL_H

#include <stddef.h>
#include <sys/types.h>

typedef int sig_atomic_t;
typedef struct
{
    unsigned long __bits[64 / (8 * sizeof(unsigned long))];
} sigset_t;
typedef void (*__sighandler_t)(int);

#define SIG_ERR ((__sighandler_t)-1)
#define SIG_DFL ((__sighandler_t)0)
#define SIG_IGN ((__sighandler_t)1)

#define SIGHUP 1
#define SIGINT 2
#define SIGQUIT 3
#define SIGILL 4
#define SIGTRAP 5
#define SIGABRT 6
#define SIGIOT 6
#define SIGBUS 7
#define SIGFPE 8
#define SIGKILL 9
#define SIGUSR1 10
#define SIGSEGV 11
#define SIGUSR2 12
#define SIGPIPE 13
#define SIGALRM 14
#define SIGTERM 15
#define SIGSTKFLT 16
#define SIGCHLD 17
#define SIGCONT 18
#define SIGSTOP 19
#define SIGTSTP 20
#define SIGTTIN 21
#define SIGTTOU 22
#define SIGURG 23
#define SIGXCPU 24
#define SIGXFSZ 25
#define SIGVTALRM 26
#define SIGPROF 27
#define SIGWINCH 28
#define SIGIO 29
#define SIGPOLL SIGIO
#define SIGPWR 30
#define SIGSYS 31
#define SIGRTMIN 32
#define SIGRTMAX 64
#define NSIG 65
#define _NSIG 65

int raise(int sig);
__sighandler_t signal(int sig, __sighandler_t handler);

int sigemptyset(sigset_t* set);
int sigfillset(sigset_t* set);
int sigaddset(sigset_t* set, int sig);
int sigdelset(sigset_t* set, int sig);
int sigismember(const sigset_t* set, int sig);

#if defined(__linux__)

#define SA_NOCLDSTOP 0x00000001
#define SA_NOCLDWAIT 0x00000002
#define SA_SIGINFO 0x00000004
#define SA_RESTORER 0x04000000
#define SA_ONSTACK 0x08000000
#define SA_RESTART 0x10000000
#define SA_NODEFER 0x40000000
#define SA_RESETHAND 0x80000000
#define SA_NOMASK SA_NODEFER
#define SA_ONESHOT SA_RESETHAND

#define SIG_BLOCK 0
#define SIG_UNBLOCK 1
#define SIG_SETMASK 2

#define SS_ONSTACK 1
#define SS_DISABLE 2
#define SS_AUTODISARM (1u << 31)
#define MINSIGSTKSZ 2048
#define SIGSTKSZ 8192

#define SI_USER 0
#define SI_KERNEL 0x80
#define SI_QUEUE (-1)
#define SI_TIMER (-2)
#define SI_MESGQ (-3)
#define SI_ASYNCIO (-4)
#define SI_SIGIO (-5)
#define SI_TKILL (-6)

#define ILL_ILLOPC 1
#define ILL_ILLOPN 2
#define ILL_ILLADR 3
#define ILL_ILLTRP 4
#define ILL_PRVOPC 5
#define ILL_PRVREG 6
#define ILL_COPROC 7
#define ILL_BADSTK 8
#define FPE_INTDIV 1
#define FPE_INTOVF 2
#define FPE_FLTDIV 3
#define FPE_FLTOVF 4
#define FPE_FLTUND 5
#define FPE_FLTRES 6
#define FPE_FLTINV 7
#define FPE_FLTSUB 8
#define SEGV_MAPERR 1
#define SEGV_ACCERR 2
#define BUS_ADRALN 1
#define BUS_ADRERR 2
#define BUS_OBJERR 3
#define TRAP_BRKPT 1
#define TRAP_TRACE 2
#define CLD_EXITED 1
#define CLD_KILLED 2
#define CLD_DUMPED 3
#define CLD_TRAPPED 4
#define CLD_STOPPED 5
#define CLD_CONTINUED 6

#ifndef __STRUCT_TIMESPEC
#define __STRUCT_TIMESPEC
struct timespec
{
    time_t tv_sec;
    long tv_nsec;
};
#endif

union sigval
{
    int sival_int;
    void* sival_ptr;
};

typedef struct
{
    int si_signo;
    int si_errno;
    int si_code;
    union
    {
        char __pad[128 - 2 * sizeof(int) - sizeof(long)];
        struct
        {
            pid_t __pid;
            uid_t __uid;
            union
            {
                union sigval __value;
                struct
                {
                    int __status;
                    long __utime;
                    long __stime;
                } __child;
            } __extra;
        } __process;
        struct
        {
            void* __addr;
        } __fault;
    } __fields;
} siginfo_t;

#define si_pid __fields.__process.__pid
#define si_uid __fields.__process.__uid
#define si_value __fields.__process.__extra.__value
#define si_int __fields.__process.__extra.__value.sival_int
#define si_ptr __fields.__process.__extra.__value.sival_ptr
#define si_status __fields.__process.__extra.__child.__status
#define si_utime __fields.__process.__extra.__child.__utime
#define si_stime __fields.__process.__extra.__child.__stime
#define si_addr __fields.__fault.__addr

typedef struct
{
    void* ss_sp;
    int ss_flags;
    size_t ss_size;
} stack_t;

struct sigaction
{
    union
    {
        void (*__handler)(int);
        void (*__action)(int, siginfo_t*, void*);
    } __function;
    sigset_t sa_mask;
    int sa_flags;
    void (*sa_restorer)(void);
};

#define sa_handler __function.__handler
#define sa_sigaction __function.__action

int kill(pid_t pid, int sig);
int killpg(pid_t group, int sig);
int sigaction(int sig, const struct sigaction* restrict action, struct sigaction* restrict old);
int sigprocmask(int how, const sigset_t* restrict set, sigset_t* restrict old);
int sigpending(sigset_t* set);
int sigsuspend(const sigset_t* mask);
int sigwait(const sigset_t* restrict set, int* restrict sig);
int sigwaitinfo(const sigset_t* restrict set, siginfo_t* restrict info);
int sigtimedwait(const sigset_t* restrict set, siginfo_t* restrict info, const struct timespec* restrict timeout);
int sigqueue(pid_t pid, int sig, const union sigval value);
int sigaltstack(const stack_t* restrict stack, stack_t* restrict old);
int siginterrupt(int sig, int flag);
void psignal(int sig, const char* message);

#endif

#endif
