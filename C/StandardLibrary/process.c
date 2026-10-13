#include <dirent.h>
#include <errno.h>
#include <fcntl.h>
#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <sys/types.h>
#include <sys/utsname.h>
#include <sys/wait.h>
#include <unistd.h>

#if defined(__linux__)

#if defined(__x86_64__)
#define __NR_mmap 9
#define __NR_mprotect 10
#define __NR_munmap 11
#define __NR_fork 57
#define __NR_execve 59
#define __NR_exit 60
#define __NR_wait4 61
#define __NR_uname 63
#define __NR_getdents64 217
#elif defined(__i386__) || defined(__arm__)
#define __NR_exit 1
#define __NR_fork 2
#define __NR_execve 11
#define __NR_munmap 91
#define __NR_wait4 114
#define __NR_uname 122
#define __NR_mprotect 125
#define __NR_mmap2 192
#if defined(__i386__)
#define __NR_getdents64 220
#else
#define __NR_getdents64 217
#endif
#else
#define __NR_getdents64 61
#define __NR_exit 93
#define __NR_uname 160
#define __NR_munmap 215
#define __NR_clone 220
#define __NR_execve 221
#define __NR_mmap 222
#define __NR_mprotect 226
#define __NR_wait4 260
#endif

#define __CLONE_CHILD_SIGNAL 17

long __syscall(long number, long a, long b, long c, long d, long e, long f);
extern char** environ;

static long __process_result(long value)
{
    if (value < 0 && value >= -4095)
    {
        errno = (int)-value;
        return -1;
    }
    return value;
}

pid_t fork(void)
{
#if defined(__NR_fork)
    return (pid_t)__process_result(__syscall(__NR_fork, 0, 0, 0, 0, 0, 0));
#else
    // Without a fork call of its own, a kernel forks as a clone that shares nothing and reports with SIGCHLD
    return (pid_t)__process_result(__syscall(__NR_clone, __CLONE_CHILD_SIGNAL, 0, 0, 0, 0, 0));
#endif
}

int execve(const char* path, char* const argv[], char* const envp[])
{
    return (int)__process_result(__syscall(__NR_execve, (long)(uintptr_t)path, (long)(uintptr_t)argv, (long)(uintptr_t)envp, 0, 0, 0));
}

int execv(const char* path, char* const argv[])
{
    return execve(path, argv, environ);
}

// A name without a slash is looked for along PATH, and the root directory when PATH is not set
int execvp(const char* file, char* const argv[])
{
    const char* path;
    char candidate[512];
    int last_error = ENOENT;
    if (file == NULL || *file == 0)
    {
        errno = ENOENT;
        return -1;
    }
    if (strchr(file, '/') != NULL)
        return execve(file, argv, environ);
    path = getenv("PATH");
    if (path == NULL)
        path = "/";
    while (*path != 0)
    {
        const char* end = strchr(path, ':');
        size_t length = end != NULL ? (size_t)(end - path) : strlen(path);
        size_t name_length = strlen(file);
        if (length + 1 + name_length < sizeof(candidate))
        {
            memcpy(candidate, path, length);
            if (length == 0 || candidate[length - 1] != '/')
                candidate[length++] = '/';
            memcpy(candidate + length, file, name_length + 1);
            execve(candidate, argv, environ);
            if (errno != ENOENT && errno != ENOTDIR)
                last_error = errno;
        }
        if (end == NULL)
            break;
        path = end + 1;
    }
    errno = last_error;
    return -1;
}

void _exit(int status)
{
    for (;;)
        __syscall(__NR_exit, status, 0, 0, 0, 0, 0);
}

pid_t waitpid(pid_t pid, int* status, int options)
{
    return (pid_t)__process_result(__syscall(__NR_wait4, pid, (long)(uintptr_t)status, options, 0, 0, 0));
}

pid_t wait(int* status)
{
    return waitpid(-1, status, 0);
}

int uname(struct utsname* name)
{
    return (int)__process_result(__syscall(__NR_uname, (long)(uintptr_t)name, 0, 0, 0, 0, 0));
}

void* mmap(void* address, size_t length, int protection, int flags, int descriptor, off_t offset)
{
    long result;
#if defined(__NR_mmap2)
    // The 32-bit call takes the offset in pages, which is how it reaches past 4 GiB
    if ((offset & 4095) != 0)
    {
        errno = EINVAL;
        return MAP_FAILED;
    }
    result = __syscall(__NR_mmap2, (long)(uintptr_t)address, (long)length, protection, flags, descriptor, (long)(offset >> 12));
#else
    result = __syscall(__NR_mmap, (long)(uintptr_t)address, (long)length, protection, flags, descriptor, (long)offset);
#endif
    if (result < 0 && result >= -4095)
    {
        errno = (int)-result;
        return MAP_FAILED;
    }
    return (void*)(uintptr_t)result;
}

int munmap(void* address, size_t length)
{
    return (int)__process_result(__syscall(__NR_munmap, (long)(uintptr_t)address, (long)length, 0, 0, 0, 0));
}

int mprotect(void* address, size_t length, int protection)
{
    return (int)__process_result(__syscall(__NR_mprotect, (long)(uintptr_t)address, (long)length, protection, 0, 0, 0));
}

struct __dirstream
{
    int descriptor;
    int position;
    int size;
    struct dirent entry;
    char buffer[2048];
};

DIR* fdopendir(int descriptor)
{
    DIR* directory = (DIR*)malloc(sizeof(DIR));
    if (directory == NULL)
        return NULL;
    directory->descriptor = descriptor;
    directory->position = 0;
    directory->size = 0;
    return directory;
}

DIR* opendir(const char* path)
{
    DIR* directory;
    int descriptor = open(path, O_RDONLY | O_DIRECTORY);
    if (descriptor < 0)
        return NULL;
    directory = fdopendir(descriptor);
    if (directory == NULL)
        close(descriptor);
    return directory;
}

// Entries come in the kernel's linux_dirent64 layout: inode, offset, record length, type, then the name
struct dirent* readdir(DIR* directory)
{
    const unsigned char* record;
    unsigned long long inode;
    long long offset;
    unsigned short length;
    size_t name_length;
    if (directory->position >= directory->size)
    {
        long count = __process_result(__syscall(__NR_getdents64, directory->descriptor, (long)(uintptr_t)directory->buffer, (long)sizeof(directory->buffer), 0, 0, 0));
        if (count <= 0)
            return NULL;
        directory->size = (int)count;
        directory->position = 0;
    }
    record = (const unsigned char*)directory->buffer + directory->position;
    memcpy(&inode, record, 8);
    memcpy(&offset, record + 8, 8);
    directory->entry.d_ino = (ino_t)inode;
    directory->entry.d_off = (off_t)offset;
    memcpy(&length, record + 16, 2);
    directory->entry.d_reclen = length;
    directory->entry.d_type = record[18];
    name_length = strnlen((const char*)record + 19, sizeof(directory->entry.d_name) - 1);
    memcpy(directory->entry.d_name, record + 19, name_length);
    directory->entry.d_name[name_length] = 0;
    directory->position += length;
    return &directory->entry;
}

void rewinddir(DIR* directory)
{
    lseek(directory->descriptor, 0, SEEK_SET);
    directory->position = 0;
    directory->size = 0;
}

int dirfd(DIR* directory)
{
    return directory->descriptor;
}

int closedir(DIR* directory)
{
    int result = close(directory->descriptor);
    free(directory);
    return result;
}

#endif
