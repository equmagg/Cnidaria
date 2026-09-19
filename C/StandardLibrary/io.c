#include <errno.h>
#include <fcntl.h>
#include <stddef.h>
#include <stdint.h>
#include <string.h>
#include <sys/stat.h>
#include <sys/types.h>
#include <unistd.h>

#if defined(__linux__)

#if defined(__x86_64__)
#define __NR_read 0
#define __NR_write 1
#define __NR_close 3
#define __NR_fstat 5
#define __NR_lseek 8
#define __NR_ioctl 16
#define __NR_dup 32
#define __NR_dup2 33
#define __NR_ftruncate 77
#define __NR_getcwd 79
#define __NR_chdir 80
#define __NR_fsync 74
#define __NR_openat 257
#define __NR_mkdirat 258
#define __NR_newfstatat 262
#define __NR_unlinkat 263
#define __NR_renameat 264
#define __NR_faccessat 269
#elif defined(__i386__)
#define __NR_read 3
#define __NR_write 4
#define __NR_close 6
#define __NR_chdir 12
#define __NR_lseek 19
#define __NR_dup 41
#define __NR_ioctl 54
#define __NR_dup2 63
#define __NR_fsync 118
#define __NR_ftruncate 93
#define __NR_getcwd 183
#define __NR_fstat 197
#define __NR_openat 295
#define __NR_mkdirat 296
#define __NR_newfstatat 300
#define __NR_unlinkat 301
#define __NR_renameat 302
#define __NR_faccessat 307
#elif defined(__arm__)
#define __NR_read 3
#define __NR_write 4
#define __NR_close 6
#define __NR_chdir 12
#define __NR_lseek 19
#define __NR_dup 41
#define __NR_ioctl 54
#define __NR_dup2 63
#define __NR_ftruncate 93
#define __NR_fsync 118
#define __NR_getcwd 183
#define __NR_fstat 197
#define __NR_openat 322
#define __NR_mkdirat 323
#define __NR_newfstatat 327
#define __NR_unlinkat 328
#define __NR_renameat 329
#define __NR_faccessat 334
#else
#define __NR_getcwd 17
#define __NR_dup 23
#define __NR_dup3 24
#define __NR_ioctl 29
#define __NR_mkdirat 34
#define __NR_unlinkat 35
#define __NR_renameat 38
#define __NR_ftruncate 46
#define __NR_faccessat 48
#define __NR_chdir 49
#define __NR_openat 56
#define __NR_close 57
#define __NR_lseek 62
#define __NR_read 63
#define __NR_write 64
#define __NR_fsync 82
#define __NR_newfstatat 79
#define __NR_fstat 80
#endif

// One entry point for every call into the kernel; a register the call ignores costs nothing
static long __sys(long number, long a, long b, long c, long d, long e)
{
    long result;

#if defined(__x86_64__)
    result = number;
    __asm__ volatile(
        "syscall"
        : "+{rax}"(result)
        : [a] "{rdi}"(a), [b] "{rsi}"(b), [c] "{rdx}"(c), [d] "{r10}"(d), [e] "{r8}"(e)
        : "rcx", "r11", "memory");
#elif defined(__i386__)
    result = number;
    __asm__ volatile(
        "push ebx\n"
        "mov ebx, %[a]\n"
        ".byte 0xcd, 0x80\n"
        "pop ebx"
        : "+{eax}"(result)
        : [a] "r"(a), [b] "{ecx}"(b), [c] "{edx}"(c), [d] "{esi}"(d), [e] "{edi}"(e)
        : "memory");
#elif defined(__aarch64__)
    result = a;
    __asm__ volatile(
        "svc #0"
        : "+{x0}"(result)
        : [number] "{x8}"(number), [b] "{x1}"(b), [c] "{x2}"(c), [d] "{x3}"(d), [e] "{x4}"(e)
        : "memory");
#elif defined(__arm__)
    result = a;
    __asm__ volatile(
        "svc #0"
        : "+{r0}"(result)
        : [number] "{r7}"(number), [b] "{r1}"(b), [c] "{r2}"(c), [d] "{r3}"(d), [e] "{r4}"(e)
        : "memory");
#elif defined(__riscv)
    result = a;
    __asm__ volatile(
        "ecall"
        : "+{a0}"(result)
        : [number] "{a7}"(number), [b] "{a1}"(b), [c] "{a2}"(c), [d] "{a3}"(d), [e] "{a4}"(e)
        : "memory");
#else
    result = -38;
#endif

    return result;
}

// A failed call comes back as the negated error, which is where errno gets its value
static long __sys_result(long value)
{
    if (value < 0 && value >= -4095)
    {
        errno = (int)-value;
        return -1;
    }
    return value;
}

ssize_t read(int descriptor, void* buffer, size_t count)
{
    return (ssize_t)__sys_result(__sys(__NR_read, (long)descriptor, (long)(uintptr_t)buffer, (long)count, 0, 0));
}

ssize_t write(int descriptor, const void* buffer, size_t count)
{
    return (ssize_t)__sys_result(__sys(__NR_write, (long)descriptor, (long)(uintptr_t)buffer, (long)count, 0, 0));
}

int close(int descriptor)
{
    return (int)__sys_result(__sys(__NR_close, (long)descriptor, 0, 0, 0, 0));
}

off_t lseek(int descriptor, off_t offset, int whence)
{
    return (off_t)__sys_result(__sys(__NR_lseek, (long)descriptor, (long)offset, (long)whence, 0, 0));
}

int ftruncate(int descriptor, off_t length)
{
    return (int)__sys_result(__sys(__NR_ftruncate, (long)descriptor, (long)length, 0, 0, 0));
}

int fsync(int descriptor)
{
    return (int)__sys_result(__sys(__NR_fsync, (long)descriptor, 0, 0, 0, 0));
}

int dup(int descriptor)
{
    return (int)__sys_result(__sys(__NR_dup, (long)descriptor, 0, 0, 0, 0));
}

int dup2(int descriptor, int replacement)
{
#if defined(__NR_dup2)
    return (int)__sys_result(__sys(__NR_dup2, (long)descriptor, (long)replacement, 0, 0, 0));
#else
    if (descriptor == replacement)
        return descriptor;
    return (int)__sys_result(__sys(__NR_dup3, (long)descriptor, (long)replacement, 0, 0, 0));
#endif
}

int openat(int directory, const char* path, int flags, ...)
{
    return (int)__sys_result(__sys(__NR_openat, (long)directory, (long)(uintptr_t)path, (long)flags, 0666, 0));
}

int open(const char* path, int flags, ...)
{
    return openat(AT_FDCWD, path, flags);
}

int unlink(const char* path)
{
    return (int)__sys_result(__sys(__NR_unlinkat, AT_FDCWD, (long)(uintptr_t)path, 0, 0, 0));
}

int rmdir(const char* path)
{
    return (int)__sys_result(__sys(__NR_unlinkat, AT_FDCWD, (long)(uintptr_t)path, AT_REMOVEDIR, 0, 0));
}

int mkdir(const char* path, mode_t mode)
{
    return (int)__sys_result(__sys(__NR_mkdirat, AT_FDCWD, (long)(uintptr_t)path, (long)mode, 0, 0));
}

int access(const char* path, int mode)
{
    return (int)__sys_result(__sys(__NR_faccessat, AT_FDCWD, (long)(uintptr_t)path, (long)mode, 0, 0));
}

int chdir(const char* path)
{
    return (int)__sys_result(__sys(__NR_chdir, (long)(uintptr_t)path, 0, 0, 0, 0));
}

char* getcwd(char* buffer, size_t size)
{
    if (__sys_result(__sys(__NR_getcwd, (long)(uintptr_t)buffer, (long)size, 0, 0, 0)) < 0)
        return (char*)0;
    return buffer;
}

int __io_rename(const char* from, const char* to)
{
    return (int)__sys_result(__sys(__NR_renameat, AT_FDCWD, (long)(uintptr_t)from, AT_FDCWD, (long)(uintptr_t)to, 0));
}

int isatty(int descriptor)
{
    unsigned char state[64];
    if (__sys(__NR_ioctl, (long)descriptor, 0x5401, (long)(uintptr_t)state, 0, 0) < 0)
    {
        errno = ENOTTY;
        return 0;
    }
    return 1;
}

// The kernel reports more than a program asks for, and where each field sits is the platform's business
static void __stat_unpack(const unsigned char* raw, struct stat* out)
{
    const unsigned int* words = (const unsigned int*)raw;
    const unsigned long long* wide = (const unsigned long long*)raw;

#if defined(__x86_64__)
    out->st_dev = wide[0];
    out->st_ino = wide[1];
    out->st_nlink = (nlink_t)wide[2];
    out->st_mode = words[6];
    out->st_uid = words[7];
    out->st_gid = words[8];
    out->st_rdev = wide[5];
    out->st_size = (off_t)wide[6];
    out->st_blksize = (blksize_t)wide[7];
    out->st_blocks = (blkcnt_t)wide[8];
    out->st_atime = (time_t)wide[9];
    out->st_mtime = (time_t)wide[11];
    out->st_ctime = (time_t)wide[13];
#else
    out->st_dev = wide[0];
    out->st_ino = wide[1];
    out->st_mode = words[4];
    out->st_nlink = words[5];
    out->st_uid = words[6];
    out->st_gid = words[7];
    out->st_rdev = wide[4];
    out->st_size = (off_t)wide[6];
    out->st_blksize = (blksize_t)words[14];
    out->st_blocks = (blkcnt_t)wide[8];
    out->st_atime = (time_t)wide[9];
    out->st_mtime = (time_t)wide[11];
    out->st_ctime = (time_t)wide[13];
#endif
}

int fstat(int descriptor, struct stat* out)
{
    unsigned char raw[256];
    memset(raw, 0, sizeof(raw));
    if (__sys_result(__sys(__NR_fstat, (long)descriptor, (long)(uintptr_t)raw, 0, 0, 0)) < 0)
        return -1;
    __stat_unpack(raw, out);
    return 0;
}

int stat(const char* path, struct stat* out)
{
    unsigned char raw[256];
    memset(raw, 0, sizeof(raw));
    if (__sys_result(__sys(__NR_newfstatat, AT_FDCWD, (long)(uintptr_t)path, (long)(uintptr_t)raw, 0, 0)) < 0)
        return -1;
    __stat_unpack(raw, out);
    return 0;
}

#elif defined(_WIN32) && defined(__x86_64__)

#define __IO_MAX_FILES 32
#define __WIN_INVALID ((void*)(long long)-1)

static void* __io_handles[__IO_MAX_FILES];
static int __io_ready;

static void* __win_std_handle(unsigned long long which)
{
    void* result;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_GetStdHandle]\n"
        "add rsp, 32"
        : "={rax}"(result)
        : [which] "{rcx}"(which)
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
}

static void __io_prepare(void)
{
    if (__io_ready != 0)
        return;
    __io_ready = 1;
    __io_handles[0] = __win_std_handle((unsigned long long)-10);
    __io_handles[1] = __win_std_handle((unsigned long long)-11);
    __io_handles[2] = __win_std_handle((unsigned long long)-12);
}

static void* __io_handle(int descriptor)
{
    __io_prepare();
    if (descriptor < 0 || descriptor >= __IO_MAX_FILES)
        return __WIN_INVALID;
    return __io_handles[descriptor] == (void*)0 ? __WIN_INVALID : __io_handles[descriptor];
}

static int __io_slot(void)
{
    int index = 3;
    __io_prepare();
    while (index < __IO_MAX_FILES)
    {
        if (__io_handles[index] == (void*)0)
            return index;
        index = index + 1;
    }
    return -1;
}

static void* __win_create_file(
    const char* path,
    unsigned long long access,
    unsigned long long share,
    unsigned long long disposition,
    unsigned long long attributes)
{
    void* result;
    __asm__ volatile(
        "mov r10, %[disposition]\n"
        "mov r11, %[attributes]\n"
        "sub rsp, 64\n"
        "mov qword ptr[rsp + 32], r10\n"
        "mov qword ptr[rsp + 40], r11\n"
        "mov qword ptr[rsp + 48], 0\n"
        "call qword ptr[rip + __imp_CreateFileA]\n"
        "add rsp, 64"
        : "={rax}"(result)
        : [path] "{rcx}"(path), [access] "{rdx}"(access), [share] "{r8}"(share), [security] "{r9}"((void*)0),
          [disposition] "r"(disposition), [attributes] "r"(attributes)
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
}

static int __win_read_file(void* handle, void* buffer, unsigned long long count, unsigned int* done)
{
    int result;
    __asm__ volatile(
        "sub rsp, 48\n"
        "mov qword ptr[rsp + 32], 0\n"
        "call qword ptr[rip + __imp_ReadFile]\n"
        "add rsp, 48"
        : "={rax}"(result)
        : [handle] "{rcx}"(handle), [buffer] "{rdx}"(buffer), [count] "{r8}"(count), [done] "{r9}"(done)
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
}

static int __win_write_file(void* handle, const void* buffer, unsigned long long count, unsigned int* done)
{
    int result;
    __asm__ volatile(
        "sub rsp, 48\n"
        "mov qword ptr[rsp + 32], 0\n"
        "call qword ptr[rip + __imp_WriteFile]\n"
        "add rsp, 48"
        : "={rax}"(result)
        : [handle] "{rcx}"(handle), [buffer] "{rdx}"(buffer), [count] "{r8}"(count), [done] "{r9}"(done)
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
}

static int __win_close_handle(void* handle)
{
    int result;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_CloseHandle]\n"
        "add rsp, 32"
        : "={rax}"(result)
        : [handle] "{rcx}"(handle)
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
}

static int __win_seek(void* handle, long long distance, long long* position, unsigned long long method)
{
    int result;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_SetFilePointerEx]\n"
        "add rsp, 32"
        : "={rax}"(result)
        : [handle] "{rcx}"(handle), [distance] "{rdx}"(distance), [position] "{r8}"(position), [method] "{r9}"(method)
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
}

static int __win_set_end(void* handle)
{
    int result;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_SetEndOfFile]\n"
        "add rsp, 32"
        : "={rax}"(result)
        : [handle] "{rcx}"(handle)
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
}

static int __win_flush(void* handle)
{
    int result;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_FlushFileBuffers]\n"
        "add rsp, 32"
        : "={rax}"(result)
        : [handle] "{rcx}"(handle)
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
}

static int __win_file_size(void* handle, long long* size)
{
    int result;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_GetFileSizeEx]\n"
        "add rsp, 32"
        : "={rax}"(result)
        : [handle] "{rcx}"(handle), [size] "{rdx}"(size)
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
}

static unsigned int __win_file_type(void* handle)
{
    unsigned int result;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_GetFileType]\n"
        "add rsp, 32"
        : "={rax}"(result)
        : [handle] "{rcx}"(handle)
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
}

static unsigned int __win_attributes(const char* path)
{
    unsigned int result;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_GetFileAttributesA]\n"
        "add rsp, 32"
        : "={rax}"(result)
        : [path] "{rcx}"(path)
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
}

static int __win_delete(const char* path)
{
    int result;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_DeleteFileA]\n"
        "add rsp, 32"
        : "={rax}"(result)
        : [path] "{rcx}"(path)
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
}

static int __win_move(const char* from, const char* to, unsigned long long flags)
{
    int result;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_MoveFileExA]\n"
        "add rsp, 32"
        : "={rax}"(result)
        : [from] "{rcx}"(from), [to] "{rdx}"(to), [flags] "{r8}"(flags)
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
}

static int __win_make_directory(const char* path)
{
    int result;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_CreateDirectoryA]\n"
        "add rsp, 32"
        : "={rax}"(result)
        : [path] "{rcx}"(path), [security] "{rdx}"((void*)0)
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
}

static int __win_remove_directory(const char* path)
{
    int result;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_RemoveDirectoryA]\n"
        "add rsp, 32"
        : "={rax}"(result)
        : [path] "{rcx}"(path)
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
}

static unsigned int __win_current_directory(unsigned long long size, char* buffer)
{
    unsigned int result;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_GetCurrentDirectoryA]\n"
        "add rsp, 32"
        : "={rax}"(result)
        : [size] "{rcx}"(size), [buffer] "{rdx}"(buffer)
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
}

static int __win_set_current_directory(const char* path)
{
    int result;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_SetCurrentDirectoryA]\n"
        "add rsp, 32"
        : "={rax}"(result)
        : [path] "{rcx}"(path)
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
}

ssize_t read(int descriptor, void* buffer, size_t count)
{
    unsigned int done = 0;
    void* handle = __io_handle(descriptor);

    if (handle == __WIN_INVALID)
    {
        errno = EBADF;
        return -1;
    }
    if (__win_read_file(handle, buffer, (unsigned long long)count, &done) == 0)
    {
        errno = EIO;
        return -1;
    }
    return (ssize_t)done;
}

ssize_t write(int descriptor, const void* buffer, size_t count)
{
    unsigned int done = 0;
    void* handle = __io_handle(descriptor);

    if (handle == __WIN_INVALID)
    {
        errno = EBADF;
        return -1;
    }
    if (__win_write_file(handle, buffer, (unsigned long long)count, &done) == 0)
    {
        errno = EIO;
        return -1;
    }
    return (ssize_t)done;
}

int close(int descriptor)
{
    void* handle = __io_handle(descriptor);

    if (handle == __WIN_INVALID || descriptor < 3)
    {
        errno = EBADF;
        return -1;
    }
    __win_close_handle(handle);
    __io_handles[descriptor] = (void*)0;
    return 0;
}

int open(const char* path, int flags, ...)
{
    unsigned long long access = 0x80000000ull;
    unsigned long long disposition;
    int slot;
    void* handle;

    if ((flags & O_ACCMODE) == O_WRONLY)
        access = 0x40000000ull;
    else if ((flags & O_ACCMODE) == O_RDWR)
        access = 0xc0000000ull;

    if ((flags & O_CREAT) != 0 && (flags & O_EXCL) != 0)
        disposition = 1ull;
    else if ((flags & O_CREAT) != 0 && (flags & O_TRUNC) != 0)
        disposition = 2ull;
    else if ((flags & O_CREAT) != 0)
        disposition = 4ull;
    else if ((flags & O_TRUNC) != 0)
        disposition = 5ull;
    else
        disposition = 3ull;

    slot = __io_slot();
    if (slot < 0)
    {
        errno = EMFILE;
        return -1;
    }

    handle = __win_create_file(path, access, 3ull, disposition, 0x80ull);
    if (handle == __WIN_INVALID)
    {
        errno = ENOENT;
        return -1;
    }

    __io_handles[slot] = handle;
    if ((flags & O_APPEND) != 0)
    {
        long long position = 0;
        __win_seek(handle, 0, &position, 2ull);
    }
    return slot;
}

int openat(int directory, const char* path, int flags, ...)
{
    (void)directory;
    return open(path, flags);
}

off_t lseek(int descriptor, off_t offset, int whence)
{
    long long position = 0;
    void* handle = __io_handle(descriptor);

    if (handle == __WIN_INVALID)
    {
        errno = EBADF;
        return -1;
    }
    if (__win_seek(handle, (long long)offset, &position, (unsigned long long)whence) == 0)
    {
        errno = EINVAL;
        return -1;
    }
    return (off_t)position;
}

int ftruncate(int descriptor, off_t length)
{
    long long position = 0;
    void* handle = __io_handle(descriptor);

    if (handle == __WIN_INVALID)
    {
        errno = EBADF;
        return -1;
    }
    if (__win_seek(handle, (long long)length, &position, 0ull) == 0 || __win_set_end(handle) == 0)
    {
        errno = EIO;
        return -1;
    }
    return 0;
}

int fsync(int descriptor)
{
    void* handle = __io_handle(descriptor);

    if (handle == __WIN_INVALID)
    {
        errno = EBADF;
        return -1;
    }
    return __win_flush(handle) == 0 ? -1 : 0;
}

int dup(int descriptor)
{
    errno = ENOSYS;
    (void)descriptor;
    return -1;
}

int dup2(int descriptor, int replacement)
{
    errno = ENOSYS;
    (void)descriptor;
    (void)replacement;
    return -1;
}

int isatty(int descriptor)
{
    void* handle = __io_handle(descriptor);

    if (handle == __WIN_INVALID)
    {
        errno = EBADF;
        return 0;
    }
    if (__win_file_type(handle) != 2u)
    {
        errno = ENOTTY;
        return 0;
    }
    return 1;
}

int unlink(const char* path)
{
    if (__win_delete(path) == 0)
    {
        errno = ENOENT;
        return -1;
    }
    return 0;
}

int rmdir(const char* path)
{
    if (__win_remove_directory(path) == 0)
    {
        errno = ENOTEMPTY;
        return -1;
    }
    return 0;
}

int mkdir(const char* path, mode_t mode)
{
    (void)mode;
    if (__win_make_directory(path) == 0)
    {
        errno = EEXIST;
        return -1;
    }
    return 0;
}

int access(const char* path, int mode)
{
    unsigned int attributes = __win_attributes(path);

    if (attributes == 0xffffffffu)
    {
        errno = ENOENT;
        return -1;
    }
    if ((mode & W_OK) != 0 && (attributes & 1u) != 0)
    {
        errno = EACCES;
        return -1;
    }
    return 0;
}

int chdir(const char* path)
{
    if (__win_set_current_directory(path) == 0)
    {
        errno = ENOENT;
        return -1;
    }
    return 0;
}

char* getcwd(char* buffer, size_t size)
{
    if (__win_current_directory((unsigned long long)size, buffer) == 0u)
    {
        errno = ERANGE;
        return (char*)0;
    }
    return buffer;
}

int __io_rename(const char* from, const char* to)
{
    if (__win_move(from, to, 3ull) == 0)
    {
        errno = ENOENT;
        return -1;
    }
    return 0;
}

static int __win_fill_stat(void* handle, unsigned int attributes, struct stat* out)
{
    long long size = 0;

    memset(out, 0, sizeof(struct stat));
    if (handle != __WIN_INVALID && __win_file_size(handle, &size) != 0)
        out->st_size = (off_t)size;
    out->st_nlink = 1;
    out->st_mode = (attributes & 0x10u) != 0 ? (mode_t)(S_IFDIR | 0755) : (mode_t)(S_IFREG | 0644);
    out->st_blksize = 4096;
    out->st_blocks = (blkcnt_t)((out->st_size + 511) / 512);
    return 0;
}

int fstat(int descriptor, struct stat* out)
{
    void* handle = __io_handle(descriptor);

    if (handle == __WIN_INVALID)
    {
        errno = EBADF;
        return -1;
    }
    return __win_fill_stat(handle, __win_file_type(handle) == 2u ? 0u : 0u, out);
}

int stat(const char* path, struct stat* out)
{
    unsigned int attributes = __win_attributes(path);
    int descriptor;
    int result;

    if (attributes == 0xffffffffu)
    {
        errno = ENOENT;
        return -1;
    }
    if ((attributes & 0x10u) != 0)
        return __win_fill_stat(__WIN_INVALID, attributes, out);

    descriptor = open(path, O_RDONLY);
    if (descriptor < 0)
        return -1;
    result = __win_fill_stat(__io_handle(descriptor), attributes, out);
    close(descriptor);
    return result;
}

int creat(const char* path, mode_t mode)
{
    (void)mode;
    return open(path, O_WRONLY | O_CREAT | O_TRUNC);
}

#endif
