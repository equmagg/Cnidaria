#ifndef __SYS_WAIT_H
#define __SYS_WAIT_H

#include <sys/types.h>

#define WNOHANG 1
#define WUNTRACED 2
#define WSTOPPED 2
#define WCONTINUED 8

#define WEXITSTATUS(status) (((status) >> 8) & 0xff)
#define WTERMSIG(status) ((status) & 0x7f)
#define WSTOPSIG(status) WEXITSTATUS(status)
#define WIFEXITED(status) (WTERMSIG(status) == 0)
#define WIFSTOPPED(status) (((status) & 0xff) == 0x7f)
#define WIFSIGNALED(status) (WTERMSIG(status) != 0 && WTERMSIG(status) != 0x7f)
#define WIFCONTINUED(status) ((status) == 0xffff)
#define WCOREDUMP(status) (((status) & 0x80) != 0)

#if defined(__linux__)
pid_t wait(int* status);
pid_t waitpid(pid_t pid, int* status, int options);
#endif

#endif
