typedef unsigned char u8;
typedef unsigned short u16;
typedef unsigned int u32;
typedef unsigned long u64;
typedef signed long s64;
typedef unsigned long usize;

#define NULL ((void*)0)
#define SYS_GETCWD 17ul
#define SYS_MKDIRAT 34ul
#define SYS_UNLINKAT 35ul
#define SYS_CHDIR 49ul
#define AT_REMOVEDIR 0x200ul
#define SYS_OPENAT 56ul
#define SYS_CLOSE 57ul
#define SYS_GETDENTS64 61ul
#define SYS_READ 63ul
#define SYS_LSEEK 62ul
#define SYS_WRITE 64ul
#define SYS_NEWFSTATAT 79ul
#define SYS_FSTAT 80ul
#define SYS_UNAME 160ul
#define SYS_EXIT 93ul
#define SYS_CLONE 220ul
#define SYS_EXECVE 221ul
#define SYS_WAIT4 260ul
#define AT_FDCWD ((u64)-100l)
#define O_RDONLY 0ul
#define O_WRONLY 1ul
#define O_RDWR 2ul
#define O_CREAT 64ul
#define O_EXCL 128ul
#define O_TRUNC 512ul
#define O_APPEND 1024ul
#define SEEK_SET 0ul
#define SEEK_CUR 1ul
#define SEEK_END 2ul
#define DT_DIR 4u
#define DT_REG 8u
#define DT_CHR 2u
#define S_IFMT 61440u
#define S_IFCHR 8192u
#define S_IFDIR 16384u
#define S_IFREG 32768u
#define STAT_SIZE 128ul
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

static s64 sys_unlinkat(s64 dirfd, const char* path, u64 flags)
{
    return syscall3(SYS_UNLINKAT, (u64)dirfd, (u64)path, flags);
}

static s64 sys_mkdirat(s64 dirfd, const char* path, u64 mode)
{
    return syscall3(SYS_MKDIRAT, (u64)dirfd, (u64)path, mode);
}

static s64 sys_chdir(const char* path)
{
    return syscall1(SYS_CHDIR, (u64)path);
}

static s64 sys_close(int fd)
{
    return syscall1(SYS_CLOSE, (u64)fd);
}

static s64 sys_getdents64(int fd, void* buffer, usize count)
{
    return syscall3(SYS_GETDENTS64, (u64)fd, (u64)buffer, count);
}

static s64 sys_lseek(int fd, s64 offset, u64 whence)
{
    return syscall3(SYS_LSEEK, (u64)fd, (u64)offset, whence);
}

static s64 sys_fstat(int fd, void* buffer)
{
    return syscall3(SYS_FSTAT, (u64)fd, (u64)buffer, 0ul);
}

static s64 sys_newfstatat(s64 dirfd, const char* path, void* buffer, u64 flags)
{
    return syscall4(SYS_NEWFSTATAT, (u64)dirfd, (u64)path, (u64)buffer, flags);
}

static s64 sys_getcwd(char* buffer, usize size)
{
    return syscall3(SYS_GETCWD, (u64)buffer, size, 0ul);
}

static s64 sys_uname(void* buffer)
{
    return syscall1(SYS_UNAME, (u64)buffer);
}

static u32 load_le32(const u8* bytes)
{
    return (u32)bytes[0] | ((u32)bytes[1] << 8) | ((u32)bytes[2] << 16) | ((u32)bytes[3] << 24);
}

static u64 load_le64(const u8* bytes)
{
    return (u64)load_le32(bytes) | ((u64)load_le32(bytes + 4) << 32);
}

/// The mode a path carries, or zero when it has none
static u32 path_mode(const char* path)
{
    u8 buffer[STAT_SIZE];
    if (sys_newfstatat(AT_FDCWD, path, buffer, 0ul) < 0l)
        return 0u;
    return load_le32(buffer + 16);
}

static u64 path_size(const char* path)
{
    u8 buffer[STAT_SIZE];
    if (sys_newfstatat(AT_FDCWD, path, buffer, 0ul) < 0l)
        return 0ul;
    return load_le64(buffer + 48);
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

static void write_text_to(int fd, const char* text)
{
    write_all(fd, text, string_length(text));
}

/// Writes a number the way every program here wants to see one
static void write_unsigned_to(int fd, u64 value, int width, char padding)
{
    char digits[24];
    int length = 0;
    int index;
    if (value == 0ul)
    {
        digits[length] = '0';
        length = length + 1;
    }
    while (value != 0ul)
    {
        digits[length] = (char)('0' + (value % 10ul));
        value = value / 10ul;
        length = length + 1;
    }
    while (length < width)
    {
        write_all(fd, &padding, 1ul);
        width = width - 1;
    }
    index = length;
    while (index != 0)
    {
        index = index - 1;
        write_all(fd, digits + index, 1ul);
    }
}

static void write_unsigned(u64 value)
{
    write_unsigned_to(1, value, 0, ' ');
}

/// What a program says when it cannot do what it was asked
static void report(const char* program, const char* subject, const char* reason)
{
    write_text_to(2, program);
    write_text_to(2, ": ");
    if (subject != (const char*)NULL)
    {
        write_text_to(2, subject);
        write_text_to(2, ": ");
    }
    write_text_to(2, reason);
    write_text_to(2, "\n");
}

/// Opens a file for writing, making it when it is not there
static int open_for_writing(const char* program, const char* path, int append)
{
    u64 flags = O_WRONLY | O_CREAT | (append ? O_APPEND : O_TRUNC);
    s64 fd = sys_openat((s64)AT_FDCWD, path, flags, 420ul);
    if (fd >= 0l)
        return (int)fd;
    report(program, path, fd == -21l ? "is a directory" : fd == -28l ? "leaves no room" : "cannot be written");
    return -1;
}

/// Opens a file for reading, reporting why if it cannot
static int open_for_reading(const char* program, const char* path)
{
    s64 fd = sys_openat((s64)AT_FDCWD, path, O_RDONLY, 0ul);
    if (fd >= 0l)
        return (int)fd;
    report(program, path, fd == -2l ? "no such file" : fd == -21l ? "is a directory" : "cannot be opened");
    return -1;
}

static int run_program(const char* path, char** argv, char** envp)
{
    s64 pid = sys_clone(SIGCHLD, 0ul);
    int status = 0;
    if (pid < 0l)
        return (int)pid;
    if (pid == 0l)
    {
        s64 result = sys_execve(path, argv, envp);
        write_text("exec failed: ");
        write_text(path);
        write_text("\n");
        sys_exit(result == -2l ? 127 : 126);
    }
    if (sys_wait4(pid, &status, 0ul) < 0l)
        return -1;
    return (status >> 8) & 255;
}
