#ifndef __UNISTD_H
#define __UNISTD_H

#include <stddef.h>
#include <sys/types.h>

#define STDIN_FILENO 0
#define STDOUT_FILENO 1
#define STDERR_FILENO 2

#ifndef SEEK_SET
#define SEEK_SET 0
#define SEEK_CUR 1
#define SEEK_END 2
#endif

#define F_OK 0
#define X_OK 1
#define W_OK 2
#define R_OK 4

ssize_t read(int descriptor, void* buffer, size_t count);
ssize_t write(int descriptor, const void* buffer, size_t count);
int close(int descriptor);
off_t lseek(int descriptor, off_t offset, int whence);
int ftruncate(int descriptor, off_t length);
int fsync(int descriptor);
int dup(int descriptor);
int dup2(int descriptor, int replacement);
int isatty(int descriptor);

int unlink(const char* path);
int rmdir(const char* path);
int access(const char* path, int mode);
int chdir(const char* path);
char* getcwd(char* buffer, size_t size);

#if defined(__linux__)
pid_t getpid(void);
pid_t getppid(void);
pid_t getpgrp(void);
pid_t getpgid(pid_t pid);
int setpgid(pid_t pid, pid_t group);
pid_t getsid(pid_t pid);
pid_t setsid(void);
int pause(void);
unsigned int alarm(unsigned int seconds);
unsigned int sleep(unsigned int seconds);
pid_t tcgetpgrp(int descriptor);
int tcsetpgrp(int descriptor, pid_t group);
#endif

#endif
