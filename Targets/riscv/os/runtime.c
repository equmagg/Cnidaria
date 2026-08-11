typedef unsigned char u8;
typedef unsigned short u16;
typedef unsigned int u32;
typedef unsigned long u64;
typedef signed long s64;
typedef unsigned long usize;

#define NULL ((void*)0)
#define SYS_OPENAT 56ul
#define SYS_CLOSE 57ul
#define SYS_GETDENTS64 61ul
#define SYS_READ 63ul
#define SYS_WRITE 64ul
#define SYS_EXIT 93ul
#define SYS_CLONE 220ul
#define SYS_EXECVE 221ul
#define SYS_WAIT4 260ul
#define AT_FDCWD ((u64)-100l)
#define O_RDONLY 0ul
#define SIGCHLD 17ul

static s64 syscall1(u64 number, u64 arg0)
{
    u64 result;
    __asm__ volatile("ecall\nmv %[result], a0" : [result] "=r"(result) : [arg0] "{a0}"(arg0), [number] "{a7}"(number) : "memory");
    return (s64)result;
}

static s64 syscall3(u64 number, u64 arg0, u64 arg1, u64 arg2)
{
    u64 result;
    __asm__ volatile("ecall\nmv %[result], a0" : [result] "=r"(result) : [arg0] "{a0}"(arg0), [arg1] "{a1}"(arg1), [arg2] "{a2}"(arg2), [number] "{a7}"(number) : "memory");
    return (s64)result;
}

static s64 syscall4(u64 number, u64 arg0, u64 arg1, u64 arg2, u64 arg3)
{
    u64 result;
    __asm__ volatile("ecall\nmv %[result], a0" : [result] "=r"(result) : [arg0] "{a0}"(arg0), [arg1] "{a1}"(arg1), [arg2] "{a2}"(arg2), [arg3] "{a3}"(arg3), [number] "{a7}"(number) : "memory");
    return (s64)result;
}

static s64 syscall5(u64 number, u64 arg0, u64 arg1, u64 arg2, u64 arg3, u64 arg4)
{
    u64 result;
    __asm__ volatile("ecall\nmv %[result], a0" : [result] "=r"(result) : [arg0] "{a0}"(arg0), [arg1] "{a1}"(arg1), [arg2] "{a2}"(arg2), [arg3] "{a3}"(arg3), [arg4] "{a4}"(arg4), [number] "{a7}"(number) : "memory");
    return (s64)result;
}

static usize string_length(const char* text)
{
    usize length = 0ul;
    while (text[length] != 0)
        length = length + 1ul;
    return length;
}

static int string_equal(const char* left, const char* right)
{
    usize index = 0ul;
    while (left[index] != 0 && right[index] != 0)
    {
        if (left[index] != right[index])
            return 0;
        index = index + 1ul;
    }
    return left[index] == right[index];
}

static s64 sys_read(int fd, void* buffer, usize count)
{
    return syscall3(SYS_READ, (u64)fd, (u64)buffer, count);
}

static s64 sys_write(int fd, const void* buffer, usize count)
{
    return syscall3(SYS_WRITE, (u64)fd, (u64)buffer, count);
}

static s64 sys_openat(s64 dirfd, const char* path, u64 flags, u64 mode)
{
    return syscall4(SYS_OPENAT, (u64)dirfd, (u64)path, flags, mode);
}

static s64 sys_close(int fd)
{
    return syscall1(SYS_CLOSE, (u64)fd);
}

static s64 sys_getdents64(int fd, void* buffer, usize count)
{
    return syscall3(SYS_GETDENTS64, (u64)fd, (u64)buffer, count);
}

static s64 sys_clone(u64 flags, u64 child_stack)
{
    return syscall5(SYS_CLONE, flags, child_stack, 0ul, 0ul, 0ul);
}

static s64 sys_execve(const char* path, char** argv, char** envp)
{
    return syscall3(SYS_EXECVE, (u64)path, (u64)argv, (u64)envp);
}

static s64 sys_wait4(s64 pid, int* status, u64 options)
{
    return syscall4(SYS_WAIT4, (u64)pid, (u64)status, options, 0ul);
}

static void sys_exit(int status)
{
    syscall1(SYS_EXIT, (u64)status);
    for (;;)
    {
    }
}

static void write_all(int fd, const char* text, usize length)
{
    usize written = 0ul;
    while (written < length)
    {
        s64 result = sys_write(fd, text + written, length - written);
        if (result <= 0l)
            return;
        written = written + (usize)result;
    }
}

static void write_text(const char* text)
{
    write_all(1, text, string_length(text));
}

static int run_program(const char* path, char** argv)
{
    s64 pid = sys_clone(SIGCHLD, 0ul);
    int status = 0;
    if (pid < 0l)
        return (int)pid;
    if (pid == 0l)
    {
        s64 result = sys_execve(path, argv, (char**)NULL);
        write_text("exec failed: ");
        write_text(path);
        write_text("\n");
        sys_exit(result == -2l ? 127 : 126);
    }
    if (sys_wait4(pid, &status, 0ul) < 0l)
        return -1;
    return (status >> 8) & 255;
}
