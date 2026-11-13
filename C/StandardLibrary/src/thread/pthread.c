#include <errno.h>
#include <limits.h>
#include <pthread.h>
#include <sched.h>
#include <semaphore.h>
#include <signal.h>
#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <time.h>
#include <unistd.h>

#if defined(__linux__)

#if defined(__x86_64__)
#define __NR_munmap 11
#define __NR_rt_sigprocmask 14
#define __NR_getpid 39
#define __NR_clone 56
#define __NR_exit 60
#define __NR_arch_prctl 158
#define __NR_futex 202
#define __NR_set_tid_address 218
#define __NR_tgkill 234
#elif defined(__i386__)
#define __NR_exit 1
#define __NR_getpid 20
#define __NR_munmap 91
#define __NR_clone 120
#define __NR_rt_sigprocmask 175
#define __NR_set_thread_area 243
#define __NR_set_tid_address 258
#define __NR_tgkill 270
#elif defined(__arm__)
#define __NR_exit 1
#define __NR_getpid 20
#define __NR_munmap 91
#define __NR_clone 120
#define __NR_rt_sigprocmask 175
#define __NR_set_tid_address 256
#define __NR_tgkill 268
#define __NR_set_tls 0xf0005
#elif defined(__aarch64__) || defined(__riscv)
#define __NR_futex 98
#define __NR_exit 93
#define __NR_set_tid_address 96
#define __NR_tgkill 131
#define __NR_rt_sigprocmask 135
#define __NR_getpid 172
#define __NR_munmap 215
#define __NR_clone 220
#endif

// A 32-bit kernel takes 64-bit time only through the calls added for it
#if __SIZEOF_POINTER__ == 4
#undef __NR_futex
#define __NR_futex 422
#endif

#define __FUTEX_WAIT 0
#define __FUTEX_WAKE 1
#define __FUTEX_WAIT_BITSET 9
#define __FUTEX_PRIVATE 128
#define __FUTEX_CLOCK_REALTIME 256
#define __FUTEX_ANY (-1)

#define __CLONE_VM 0x00000100l
#define __CLONE_FS 0x00000200l
#define __CLONE_FILES 0x00000400l
#define __CLONE_SIGHAND 0x00000800l
#define __CLONE_THREAD 0x00010000l
#define __CLONE_SYSVSEM 0x00040000l
#define __CLONE_SETTLS 0x00080000l
#define __CLONE_PARENT_SETTID 0x00100000l
#define __CLONE_CHILD_CLEARTID 0x00200000l
#define __CLONE_CHILD_SETTID 0x01000000l

#define __THREAD_JOINABLE 0
#define __THREAD_DETACHED 1
#define __THREAD_EXITING 2

#define __THREAD_PAGE 4096ul
#define __THREAD_STACK_DEFAULT 131072ul
#define __THREAD_GUARD_DEFAULT 4096ul
#define __THREAD_ATFORK_SLOTS 32

#define __MUTEX_TYPE 3
#define __MUTEX_SHARED 4

#define __RWLOCK_WRITER 0x7fffffff
#define __RWLOCK_WAITERS ((int)0x80000000)

long __syscall(long number, long a, long b, long c, long d, long e, long f);
void __stdlib_lock(void);
void __stdlib_unlock(void);

struct __pthread
{
    struct __pthread* self;
    struct __pthread* next;
    struct __pthread* previous;
    volatile int tid;
    volatile int detach_state;
    int errno_value;
    void* (*start)(void*);
    void* argument;
    void* result;
    char* map_base;
    size_t map_size;
    char* stack;
    size_t stack_size;
    size_t guard_size;
    unsigned long start_mask;
    void* specific[PTHREAD_KEYS_MAX];
};

#define __THREAD_BLOCK ((sizeof(struct __pthread) + 15ul) & ~15ul)

// With TLS above the thread pointer the block ends at it, with TLS below the block starts at it and opens with its own address
#if defined(__x86_64__) || defined(__i386__)
#define __THREAD_POINTER_OFFSET 0ul
#else
#define __THREAD_POINTER_OFFSET __THREAD_BLOCK
#endif

static struct __pthread __pthread_main;
static volatile int __pthread_list_lock;
static int __pthread_count;
static void (*__pthread_destructors[PTHREAD_KEYS_MAX])(void*);
static unsigned char __pthread_keys_used[PTHREAD_KEYS_MAX];
static void (*__atfork_prepare[__THREAD_ATFORK_SLOTS])(void);
static void (*__atfork_parent[__THREAD_ATFORK_SLOTS])(void);
static void (*__atfork_child[__THREAD_ATFORK_SLOTS])(void);
static int __atfork_count;

#if defined(__i386__)

struct __user_desc
{
    unsigned int entry_number;
    unsigned int base_addr;
    unsigned int limit;
    unsigned int flags;
};

// A 32-bit data segment of 4 GiB, its limit in pages, that a program may load
#define __USER_DESC_FLAGS 0x51u

static unsigned int __thread_area_entry;

#endif

#if defined(__arm__)
// Helpers the kernel maps at fixed addresses into every ARM process, working on cores without exclusive loads
#define __KUSER_MEMORY_BARRIER ((void (*)(void))0xffff0fa0ul)
#define __KUSER_CMPXCHG ((int (*)(int, int, volatile int*))0xffff0fc0ul)
#define __KUSER_GET_TLS ((char* (*)(void))0xffff0fe0ul)
#endif

static int __atomic_cas(volatile int* address, int expected, int desired)
{
#if defined(__x86_64__) || defined(__i386__)
    int old = expected;
    __asm__ volatile(
        ".byte 0xf0\n"
        "cmpxchg dword ptr[%[address]], %[desired]"
        : [old] "+&{eax}"(old)
        : [address] "r"(address), [desired] "r"(desired)
        : "cc", "memory");
    return old;
#elif defined(__aarch64__)
    int old;
    int status;
    __asm__ volatile(
        ".L__atomic_cas_retry_%=:\n"
        "ldaxr %w[old], [%[address]]\n"
        "cmp %w[old], %w[expected]\n"
        "b.ne .L__atomic_cas_done_%=\n"
        "stlxr %w[status], %w[desired], [%[address]]\n"
        "cbnz %w[status], .L__atomic_cas_retry_%=\n"
        ".L__atomic_cas_done_%=:"
        : [old] "=&r"(old), [status] "=&r"(status)
        : [address] "r"(address), [expected] "r"(expected), [desired] "r"(desired)
        : "cc", "memory");
    return old;
#elif defined(__arm__)
    for (;;)
    {
        int old = *address;
        if (old != expected)
        {
            __KUSER_MEMORY_BARRIER();
            return old;
        }
        if (__KUSER_CMPXCHG(expected, desired, address) == 0)
            return expected;
    }
#elif defined(__riscv_zacas)
    long old = (long)expected;
    __asm__ volatile(
        "amocas.w.aqrl %[old], %[desired], (%[address])"
        : [old] "+&r"(old)
        : [address] "r"(address), [desired] "r"((long)desired)
        : "memory");
    return (int)old;
#elif defined(__riscv)
    long old;
    long failed;
    __asm__ volatile(
        ".L__atomic_cas_retry_%=:\n"
        "lr.w.aqrl %[old], (%[address])\n"
        "bne %[old], %[expected], .L__atomic_cas_done_%=\n"
        "sc.w.aqrl %[failed], %[desired], (%[address])\n"
        "bnez %[failed], .L__atomic_cas_retry_%=\n"
        ".L__atomic_cas_done_%=:"
        : [old] "=&r"(old), [failed] "=&r"(failed)
        : [address] "r"(address), [expected] "r"((long)expected), [desired] "r"((long)desired)
        : "memory");
    return (int)old;
#endif
}

static int __atomic_swap(volatile int* address, int value)
{
#if defined(__x86_64__) || defined(__i386__)
    __asm__ volatile(
        "xchg dword ptr[%[address]], %[value]"
        : [value] "+&r"(value)
        : [address] "r"(address)
        : "memory");
    return value;
#elif defined(__riscv)
    long old;
    __asm__ volatile(
        "amoswap.w.aqrl %[old], %[value], (%[address])"
        : [old] "=&r"(old)
        : [address] "r"(address), [value] "r"((long)value)
        : "memory");
    return (int)old;
#elif defined(__aarch64__) || defined(__arm__)
    for (;;)
    {
        int old = *address;
        if (__atomic_cas(address, old, value) == old)
            return old;
    }
#endif
}

static int __atomic_add(volatile int* address, int value)
{
#if defined(__x86_64__) || defined(__i386__)
    __asm__ volatile(
        ".byte 0xf0\n"
        "xadd dword ptr[%[address]], %[value]"
        : [value] "+&r"(value)
        : [address] "r"(address)
        : "cc", "memory");
    return value;
#elif defined(__riscv)
    long old;
    __asm__ volatile(
        "amoadd.w.aqrl %[old], %[value], (%[address])"
        : [old] "=&r"(old)
        : [address] "r"(address), [value] "r"((long)value)
        : "memory");
    return (int)old;
#elif defined(__aarch64__) || defined(__arm__)
    for (;;)
    {
        int old = *address;
        if (__atomic_cas(address, old, old + value) == old)
            return old;
    }
#endif
}

static int __atomic_load(volatile int* address)
{
    int value = *address;
#if defined(__aarch64__)
    __asm__ volatile("dmb ish" : : : "memory");
#elif defined(__arm__)
    __KUSER_MEMORY_BARRIER();
#elif defined(__riscv)
    __asm__ volatile("fence r, rw" : : : "memory");
#else
    __asm__ volatile("" : : : "memory");
#endif
    return value;
}

static char* __thread_pointer(void)
{
    char* value;
#if defined(__x86_64__)
    __asm__ volatile(".byte 0x64, 0x48, 0x8b, 0x04, 0x25, 0x00, 0x00, 0x00, 0x00" : "={rax}"(value) : : "memory");
#elif defined(__i386__)
    __asm__ volatile(".byte 0x65, 0xa1, 0x00, 0x00, 0x00, 0x00" : "={eax}"(value) : : "memory");
#elif defined(__aarch64__)
    __asm__ volatile("mrs %[value], tpidr_el0" : [value] "=r"(value) : : "memory");
#elif defined(__arm__)
    value = __KUSER_GET_TLS();
#elif defined(__riscv)
    __asm__ volatile("mv %[value], tp" : [value] "=r"(value) : : "memory");
#endif
    return value;
}

static void __thread_pointer_set(char* pointer)
{
#if defined(__x86_64__)
    __syscall(__NR_arch_prctl, 0x1002, (long)pointer, 0, 0, 0, 0);
#elif defined(__i386__)
    struct __user_desc descriptor;
    descriptor.entry_number = 0xffffffffu;
    descriptor.base_addr = (unsigned int)pointer;
    descriptor.limit = 0xfffffu;
    descriptor.flags = __USER_DESC_FLAGS;
    __syscall(__NR_set_thread_area, (long)&descriptor, 0, 0, 0, 0, 0);
    __thread_area_entry = descriptor.entry_number;
    __asm__ volatile(".byte 0x8e, 0xe8" : : "{eax}"(descriptor.entry_number * 8u + 3u) : "memory");
#elif defined(__aarch64__)
    __asm__ volatile("msr tpidr_el0, %[pointer]" : : [pointer] "r"(pointer) : "memory");
#elif defined(__arm__)
    __syscall(__NR_set_tls, (long)pointer, 0, 0, 0, 0, 0);
#elif defined(__riscv)
    __asm__ volatile("mv tp, %[pointer]" : : [pointer] "r"(pointer) : "memory");
#endif
}

// The kernel reads a deadline as two 64-bit words, whatever this libc's timespec is
static long __futex(volatile int* address, int op, int value, const struct timespec* deadline, int value3)
{
    long long kernel[2];
    if (deadline == (const struct timespec*)0)
        return __syscall(__NR_futex, (long)address, op, value, 0, 0, value3);
    kernel[0] = (long long)deadline->tv_sec;
    kernel[1] = (long long)deadline->tv_nsec;
    return __syscall(__NR_futex, (long)address, op, value, (long)kernel, 0, value3);
}

static long __futex_wait(volatile int* address, int value, int shared, const struct timespec* deadline, clockid_t clock)
{
    int flags = shared ? 0 : __FUTEX_PRIVATE;
    if (deadline == (const struct timespec*)0)
        return __futex(address, __FUTEX_WAIT | flags, value, deadline, 0);
    if (clock == CLOCK_REALTIME)
        flags = flags | __FUTEX_CLOCK_REALTIME;
    return __futex(address, __FUTEX_WAIT_BITSET | flags, value, deadline, __FUTEX_ANY);
}

static void __futex_wake(volatile int* address, int count, int shared)
{
    __futex(address, __FUTEX_WAKE | (shared ? 0 : __FUTEX_PRIVATE), count, (const struct timespec*)0, 0);
}

static int __deadline_invalid(const struct timespec* deadline)
{
    return deadline != (const struct timespec*)0 && (deadline->tv_nsec < 0 || deadline->tv_nsec >= 1000000000l);
}

static unsigned long __signal_mask(int how, unsigned long mask)
{
    unsigned long old = 0;
    __syscall(__NR_rt_sigprocmask, how, (long)&mask, (long)&old, 8, 0, 0);
    return old;
}

void __pthread_main_init(void)
{
    struct __pthread* self = &__pthread_main;
    if (self->self == self)
        return;
    self->self = self;
    self->next = self;
    self->previous = self;
    self->tid = (int)__syscall(__NR_set_tid_address, (long)&self->tid, 0, 0, 0, 0, 0);
    __pthread_count = 1;
    __thread_pointer_set((char*)self + __THREAD_POINTER_OFFSET);
}

// A library initializer may run before libc's, so the main thread is set up on first use
static struct __pthread* __pthread_current(void)
{
    if (__pthread_main.self == (struct __pthread*)0)
        __pthread_main_init();
    return (struct __pthread*)(__thread_pointer() - __THREAD_POINTER_OFFSET);
}

int* __errno_location(void)
{
    return &__pthread_current()->errno_value;
}

void* __libc_thread_self(void)
{
    return (void*)__pthread_current();
}

// Zero is free, one held, two held with someone waiting
void __libc_lock(volatile int* word)
{
    if (__atomic_cas(word, 0, 1) == 0)
        return;
    while (__atomic_swap(word, 2) != 0)
        __futex(word, __FUTEX_WAIT | __FUTEX_PRIVATE, 2, (const struct timespec*)0, 0);
}

int __libc_trylock(volatile int* word)
{
    return __atomic_cas(word, 0, 1) == 0 ? 0 : EBUSY;
}

void __libc_unlock(volatile int* word)
{
    if (__atomic_swap(word, 0) == 2)
        __futex(word, __FUTEX_WAKE | __FUTEX_PRIVATE, 1, (const struct timespec*)0, 0);
}

static void __pthread_unlink(struct __pthread* thread)
{
    thread->previous->next = thread->next;
    thread->next->previous = thread->previous;
    thread->next = thread;
    thread->previous = thread;
    __pthread_count = __pthread_count - 1;
}

static void __pthread_start(struct __pthread* self)
{
    __syscall(__NR_rt_sigprocmask, SIG_SETMASK, (long)&self->start_mask, 0, 8, 0, 0);
    pthread_exit(self->start(self->argument));
}

static long __pthread_clone(long flags, char* stack, struct __pthread* thread)
{
    char* pointer = (char*)thread + __THREAD_POINTER_OFFSET;
#if defined(__x86_64__)
    unsigned long* frame = (unsigned long*)stack - 2;
    long result = __NR_clone;
    frame[0] = (unsigned long)__pthread_start;
    frame[1] = (unsigned long)thread;
    __asm__ volatile(
        "syscall\n"
        "test rax, rax\n"
        "jnz .L__pthread_clone_parent_%=\n"
        "mov rdi, qword ptr[rsp + 8]\n"
        "call qword ptr[rsp]\n"
        ".byte 0x0f, 0x0b\n"
        ".L__pthread_clone_parent_%=:"
        : "+{rax}"(result)
        : [flags] "{rdi}"(flags), [stack] "{rsi}"(frame), [parent_tid] "{rdx}"(&thread->tid), [child_tid] "{r10}"(&thread->tid),
          [tls] "{r8}"(pointer)
        : "rcx", "r11", "memory");
    return result;
#elif defined(__i386__)
    unsigned long* frame = (unsigned long*)(stack - 20);
    struct __user_desc descriptor;
    long result = flags;
    descriptor.entry_number = __thread_area_entry;
    descriptor.base_addr = (unsigned int)pointer;
    descriptor.limit = 0xfffffu;
    descriptor.flags = __USER_DESC_FLAGS;
    frame[0] = (unsigned long)__pthread_start;
    frame[1] = (unsigned long)thread;
    __asm__ volatile(
        "push ebx\n"
        "mov ebx, eax\n"
        "mov eax, 120\n"
        ".byte 0xcd, 0x80\n"
        "test eax, eax\n"
        "jnz .L__pthread_clone_parent_%=\n"
        "pop eax\n"
        "call eax\n"
        ".byte 0x0f, 0x0b\n"
        ".L__pthread_clone_parent_%=:\n"
        "pop ebx"
        : "+{eax}"(result)
        : [stack] "{ecx}"(frame), [parent_tid] "{edx}"(&thread->tid), [tls] "{esi}"(&descriptor), [child_tid] "{edi}"(&thread->tid)
        : "memory");
    return result;
#elif defined(__aarch64__)
    unsigned long* frame = (unsigned long*)stack - 2;
    long result = flags;
    frame[0] = (unsigned long)__pthread_start;
    frame[1] = (unsigned long)thread;
    __asm__ volatile(
        "svc #0\n"
        "cbnz x0, .L__pthread_clone_parent_%=\n"
        "ldr x16, [sp]\n"
        "ldr x0, [sp, #8]\n"
        "add sp, sp, #16\n"
        "blr x16\n"
        "brk #0\n"
        ".L__pthread_clone_parent_%=:"
        : "+{x0}"(result)
        : [stack] "{x1}"(frame), [parent_tid] "{x2}"(&thread->tid), [tls] "{x3}"(pointer), [child_tid] "{x4}"(&thread->tid),
          [number] "{x8}"((long)__NR_clone)
        : "memory");
    return result;
#elif defined(__arm__)
    unsigned long* frame = (unsigned long*)stack - 2;
    long result = flags;
    frame[0] = (unsigned long)__pthread_start;
    frame[1] = (unsigned long)thread;
    __asm__ volatile(
        "svc #0\n"
        "cmp r0, #0\n"
        "bne .L__pthread_clone_parent_%=\n"
        "ldr r12, [sp]\n"
        "ldr r0, [sp, #4]\n"
        "add sp, sp, #8\n"
        "blx r12\n"
        ".word 0xe7f000f0\n"
        ".L__pthread_clone_parent_%=:"
        : "+{r0}"(result)
        : [stack] "{r1}"(frame), [parent_tid] "{r2}"(&thread->tid), [tls] "{r3}"(pointer), [child_tid] "{r4}"(&thread->tid),
          [number] "{r7}"((long)__NR_clone)
        : "r12", "memory");
    return result;
#elif defined(__riscv)
    unsigned long* frame = (unsigned long*)stack - 2;
    long result = flags;
    frame[0] = (unsigned long)__pthread_start;
    frame[1] = (unsigned long)thread;
    __asm__ volatile(
        "ecall\n"
        "bnez a0, .L__pthread_clone_parent_%=\n"
#if __riscv_xlen == 64
        "ld t0, 0(sp)\n"
        "ld a0, 8(sp)\n"
        "addi sp, sp, 16\n"
#else
        "lw t0, 0(sp)\n"
        "lw a0, 4(sp)\n"
        "addi sp, sp, 8\n"
#endif
        "jalr ra, t0, 0\n"
        "ebreak\n"
        ".L__pthread_clone_parent_%=:"
        : "+{a0}"(result)
        : [stack] "{a1}"(frame), [parent_tid] "{a2}"(&thread->tid), [tls] "{a3}"(pointer), [child_tid] "{a4}"(&thread->tid),
          [number] "{a7}"((long)__NR_clone)
        : "memory");
    return result;
#endif
}

int pthread_create(pthread_t* restrict thread, const pthread_attr_t* restrict attr, void* (*start)(void*), void* restrict argument)
{
    pthread_attr_t defaults;
    struct __pthread* self = __pthread_current();
    struct __pthread* created;
    char* map = (char*)0;
    size_t map_size = 0;
    char* stack;
    char* top;
    unsigned long mask;
    long result;
    if (attr == (const pthread_attr_t*)0)
    {
        pthread_attr_init(&defaults);
        attr = &defaults;
    }
    if (attr->__stack != (void*)0)
    {
        stack = (char*)attr->__stack;
        top = stack + attr->__stack_size;
    }
    else
    {
        size_t guard = (attr->__guard_size + __THREAD_PAGE - 1ul) & ~(__THREAD_PAGE - 1ul);
        map_size = guard + ((attr->__stack_size + __THREAD_BLOCK + __THREAD_PAGE - 1ul) & ~(__THREAD_PAGE - 1ul));
        map = (char*)mmap((void*)0, map_size, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANONYMOUS | MAP_STACK, -1, 0);
        if (map == (char*)MAP_FAILED)
            return EAGAIN;
        if (guard != 0 && mprotect(map, guard, PROT_NONE) != 0)
        {
            munmap(map, map_size);
            return EAGAIN;
        }
        stack = map + guard;
        top = map + map_size;
    }
    created = (struct __pthread*)(((uintptr_t)top - __THREAD_BLOCK) & ~(uintptr_t)15);
    memset(created, 0, sizeof(struct __pthread));
    created->self = created;
    created->detach_state = attr->__detach_state == PTHREAD_CREATE_DETACHED ? __THREAD_DETACHED : __THREAD_JOINABLE;
    created->start = start;
    created->argument = argument;
    created->map_base = map;
    created->map_size = map_size;
    created->stack = stack;
    created->stack_size = (size_t)((char*)created - stack);
    created->guard_size = (size_t)(stack - map);
    mask = __signal_mask(SIG_SETMASK, ~0ul);
    created->start_mask = mask;
    __libc_lock(&__pthread_list_lock);
    created->next = self->next;
    created->previous = self;
    self->next->previous = created;
    self->next = created;
    __pthread_count = __pthread_count + 1;
    result = __pthread_clone(__CLONE_VM | __CLONE_FS | __CLONE_FILES | __CLONE_SIGHAND | __CLONE_THREAD | __CLONE_SYSVSEM |
        __CLONE_SETTLS | __CLONE_PARENT_SETTID | __CLONE_CHILD_CLEARTID, (char*)created, created);
    if (result < 0)
        __pthread_unlink(created);
    __libc_unlock(&__pthread_list_lock);
    __signal_mask(SIG_SETMASK, mask);
    if (result < 0)
    {
        if (map != (char*)0)
            munmap(map, map_size);
        return EAGAIN;
    }
    *thread = created;
    return 0;
}

static void __pthread_run_destructors(struct __pthread* self)
{
    int round;
    for (round = 0; round < PTHREAD_DESTRUCTOR_ITERATIONS; round = round + 1)
    {
        int ran = 0;
        unsigned int key;
        for (key = 0; key < PTHREAD_KEYS_MAX; key = key + 1)
        {
            void* value = self->specific[key];
            void (*destructor)(void*) = __pthread_destructors[key];
            if (value == (void*)0 || destructor == (void (*)(void*))0)
                continue;
            self->specific[key] = (void*)0;
            destructor(value);
            ran = 1;
        }
        if (!ran)
            break;
    }
}

// No memory access between the two calls, the stack being gone after the first
static void __pthread_unmap_self(char* base, size_t size)
{
#if defined(__x86_64__)
    __asm__ volatile(
        "mov eax, 11\n"
        "syscall\n"
        "xor edi, edi\n"
        "mov eax, 60\n"
        "syscall"
        :
        : [base] "{rdi}"(base), [size] "{rsi}"(size)
        : "rax", "rcx", "r11", "memory");
#elif defined(__i386__)
    __asm__ volatile(
        "mov ebx, eax\n"
        "mov eax, 91\n"
        ".byte 0xcd, 0x80\n"
        "xor ebx, ebx\n"
        "mov eax, 1\n"
        ".byte 0xcd, 0x80"
        :
        : [base] "{eax}"(base), [size] "{ecx}"(size)
        : "memory");
#elif defined(__aarch64__)
    __asm__ volatile(
        "mov x8, #215\n"
        "svc #0\n"
        "mov x0, #0\n"
        "mov x8, #93\n"
        "svc #0"
        :
        : [base] "{x0}"(base), [size] "{x1}"(size)
        : "x8", "memory");
#elif defined(__arm__)
    __asm__ volatile(
        "mov r7, #91\n"
        "svc #0\n"
        "mov r0, #0\n"
        "mov r7, #1\n"
        "svc #0"
        :
        : [base] "{r0}"(base), [size] "{r1}"(size)
        : "r7", "memory");
#elif defined(__riscv)
    __asm__ volatile(
        "addi a7, zero, 215\n"
        "ecall\n"
        "addi a0, zero, 0\n"
        "addi a7, zero, 93\n"
        "ecall"
        :
        : [base] "{a0}"(base), [size] "{a1}"(size)
        : "a7", "memory");
#endif
}

void pthread_exit(void* result)
{
    struct __pthread* self = __pthread_current();
    int last;
    __pthread_run_destructors(self);
    self->result = result;
    __libc_lock(&__pthread_list_lock);
    last = __pthread_count == 1;
    if (!last)
        __pthread_unlink(self);
    __libc_unlock(&__pthread_list_lock);
    if (last)
        exit(0);
    // Whichever of the exit and a detach comes second frees the thread
    if (__atomic_cas(&self->detach_state, __THREAD_JOINABLE, __THREAD_EXITING) == __THREAD_DETACHED && self->map_base != (char*)0)
    {
        __signal_mask(SIG_SETMASK, ~0ul);
        __syscall(__NR_set_tid_address, 0, 0, 0, 0, 0, 0);
        __pthread_unmap_self(self->map_base, self->map_size);
    }
    for (;;)
        __syscall(__NR_exit, 0, 0, 0, 0, 0, 0);
}

int pthread_join(pthread_t thread, void** result)
{
    if (thread == __pthread_current())
        return EDEADLK;
    if (thread->detach_state == __THREAD_DETACHED)
        return EINVAL;
    for (;;)
    {
        int tid = __atomic_load(&thread->tid);
        if (tid == 0)
            break;
        __futex(&thread->tid, __FUTEX_WAIT, tid, (const struct timespec*)0, 0);
    }
    if (result != (void**)0)
        *result = thread->result;
    if (thread->map_base != (char*)0)
        munmap(thread->map_base, thread->map_size);
    return 0;
}

int pthread_detach(pthread_t thread)
{
    int state = __atomic_cas(&thread->detach_state, __THREAD_JOINABLE, __THREAD_DETACHED);
    if (state == __THREAD_EXITING)
        return pthread_join(thread, (void**)0);
    return state == __THREAD_JOINABLE ? 0 : EINVAL;
}

pthread_t pthread_self(void)
{
    return __pthread_current();
}

int pthread_equal(pthread_t left, pthread_t right)
{
    return left == right;
}

int pthread_kill(pthread_t thread, int sig)
{
    int tid = thread->tid;
    long result;
    if (tid == 0)
        return ESRCH;
    result = __syscall(__NR_tgkill, __syscall(__NR_getpid, 0, 0, 0, 0, 0, 0), tid, sig, 0, 0, 0);
    return result < 0 ? (int)-result : 0;
}

int pthread_sigmask(int how, const sigset_t* restrict set, sigset_t* restrict old)
{
    long result = __syscall(__NR_rt_sigprocmask, how, (long)set, (long)old, 8, 0, 0);
    return result < 0 ? (int)-result : 0;
}

// Zero has not run, one runs now, two is done
int pthread_once(pthread_once_t* once, void (*routine)(void))
{
    volatile int* state = (volatile int*)once;
    if (__atomic_load(state) == 2)
        return 0;
    if (__atomic_cas(state, 0, 1) == 0)
    {
        routine();
        __atomic_swap(state, 2);
        __futex_wake(state, INT_MAX, 0);
        return 0;
    }
    while (__atomic_load(state) == 1)
        __futex_wait(state, 1, 0, (const struct timespec*)0, CLOCK_REALTIME);
    return 0;
}

int pthread_atfork(void (*prepare)(void), void (*parent)(void), void (*child)(void))
{
    int result = 0;
    __libc_lock(&__pthread_list_lock);
    if (__atfork_count == __THREAD_ATFORK_SLOTS)
        result = ENOMEM;
    else
    {
        __atfork_prepare[__atfork_count] = prepare;
        __atfork_parent[__atfork_count] = parent;
        __atfork_child[__atfork_count] = child;
        __atfork_count = __atfork_count + 1;
    }
    __libc_unlock(&__pthread_list_lock);
    return result;
}

void __libc_stdio_fork_lock(void);
void __libc_stdio_fork_unlock(int child);

pid_t __pthread_fork(void)
{
    struct __pthread* self = __pthread_current();
    int count = __atfork_count;
    int index;
    long result;
    for (index = count; index > 0; index = index - 1)
    {
        if (__atfork_prepare[index - 1] != (void (*)(void))0)
            __atfork_prepare[index - 1]();
    }
    __libc_lock(&__pthread_list_lock);
    __libc_stdio_fork_lock();
    __stdlib_lock();
#if defined(__x86_64__)
    result = __syscall(__NR_clone, SIGCHLD | __CLONE_CHILD_SETTID | __CLONE_CHILD_CLEARTID, 0, 0, (long)&self->tid, 0, 0);
#else
    result = __syscall(__NR_clone, SIGCHLD | __CLONE_CHILD_SETTID | __CLONE_CHILD_CLEARTID, 0, 0, 0, (long)&self->tid, 0);
#endif
    if (result == 0)
    {
        self->next = self;
        self->previous = self;
        __pthread_count = 1;
    }
    __stdlib_unlock();
    __libc_stdio_fork_unlock(result == 0);
    __libc_unlock(&__pthread_list_lock);
    for (index = 0; index < count; index = index + 1)
    {
        void (*handler)(void) = result == 0 ? __atfork_child[index] : __atfork_parent[index];
        if (handler != (void (*)(void))0)
            handler();
    }
    if (result < 0)
    {
        errno = (int)-result;
        return -1;
    }
    return (pid_t)result;
}

int pthread_attr_init(pthread_attr_t* attr)
{
    attr->__detach_state = PTHREAD_CREATE_JOINABLE;
    attr->__guard_size = __THREAD_GUARD_DEFAULT;
    attr->__stack_size = __THREAD_STACK_DEFAULT;
    attr->__stack = (void*)0;
    return 0;
}

int pthread_attr_destroy(pthread_attr_t* attr)
{
    (void)attr;
    return 0;
}

int pthread_attr_getdetachstate(const pthread_attr_t* attr, int* state)
{
    *state = attr->__detach_state;
    return 0;
}

int pthread_attr_setdetachstate(pthread_attr_t* attr, int state)
{
    if (state != PTHREAD_CREATE_JOINABLE && state != PTHREAD_CREATE_DETACHED)
        return EINVAL;
    attr->__detach_state = state;
    return 0;
}

int pthread_attr_getguardsize(const pthread_attr_t* restrict attr, size_t* restrict size)
{
    *size = attr->__guard_size;
    return 0;
}

int pthread_attr_setguardsize(pthread_attr_t* attr, size_t size)
{
    attr->__guard_size = size;
    return 0;
}

int pthread_attr_getstacksize(const pthread_attr_t* restrict attr, size_t* restrict size)
{
    *size = attr->__stack_size;
    return 0;
}

int pthread_attr_setstacksize(pthread_attr_t* attr, size_t size)
{
    if (size < PTHREAD_STACK_MIN)
        return EINVAL;
    attr->__stack_size = size;
    return 0;
}

int pthread_attr_getstack(const pthread_attr_t* restrict attr, void** restrict stack, size_t* restrict size)
{
    *stack = attr->__stack;
    *size = attr->__stack_size;
    return 0;
}

int pthread_attr_setstack(pthread_attr_t* attr, void* stack, size_t size)
{
    if (size < PTHREAD_STACK_MIN)
        return EINVAL;
    attr->__stack = stack;
    attr->__stack_size = size;
    return 0;
}

static int __mutex_acquire(pthread_mutex_t* mutex, const struct timespec* deadline)
{
    int shared = (mutex->__kind & __MUTEX_SHARED) != 0;
    if (__atomic_cas(&mutex->__lock, 0, 1) == 0)
        return 0;
    while (__atomic_swap(&mutex->__lock, 2) != 0)
    {
        long result = __futex_wait(&mutex->__lock, 2, shared, deadline, CLOCK_REALTIME);
        if (result == -ETIMEDOUT || result == -EINVAL)
            return (int)-result;
    }
    return 0;
}

static int __mutex_lock(pthread_mutex_t* mutex, const struct timespec* deadline, int wait)
{
    int type = mutex->__kind & __MUTEX_TYPE;
    int tid = 0;
    int result;
    if (type != PTHREAD_MUTEX_NORMAL)
    {
        tid = __pthread_current()->tid;
        if (mutex->__owner == tid)
        {
            if (type != PTHREAD_MUTEX_RECURSIVE)
                return EDEADLK;
            if (mutex->__count == INT_MAX)
                return EAGAIN;
            mutex->__count = mutex->__count + 1;
            return 0;
        }
    }
    if (!wait)
        result = __atomic_cas(&mutex->__lock, 0, 1) == 0 ? 0 : EBUSY;
    else
        result = __mutex_acquire(mutex, deadline);
    if (result == 0 && type != PTHREAD_MUTEX_NORMAL)
    {
        mutex->__owner = tid;
        mutex->__count = 0;
    }
    return result;
}

int pthread_mutex_init(pthread_mutex_t* restrict mutex, const pthread_mutexattr_t* restrict attr)
{
    mutex->__lock = 0;
    mutex->__kind = attr != (const pthread_mutexattr_t*)0 ? attr->__kind : 0;
    mutex->__owner = 0;
    mutex->__count = 0;
    return 0;
}

int pthread_mutex_destroy(pthread_mutex_t* mutex)
{
    (void)mutex;
    return 0;
}

int pthread_mutex_lock(pthread_mutex_t* mutex)
{
    return __mutex_lock(mutex, (const struct timespec*)0, 1);
}

int pthread_mutex_trylock(pthread_mutex_t* mutex)
{
    return __mutex_lock(mutex, (const struct timespec*)0, 0);
}

int pthread_mutex_timedlock(pthread_mutex_t* restrict mutex, const struct timespec* restrict deadline)
{
    return __mutex_lock(mutex, deadline, 1);
}

int pthread_mutex_unlock(pthread_mutex_t* mutex)
{
    int type = mutex->__kind & __MUTEX_TYPE;
    if (type != PTHREAD_MUTEX_NORMAL)
    {
        if (mutex->__owner != __pthread_current()->tid)
            return EPERM;
        if (mutex->__count != 0)
        {
            mutex->__count = mutex->__count - 1;
            return 0;
        }
        mutex->__owner = 0;
    }
    if (__atomic_swap(&mutex->__lock, 0) == 2)
        __futex_wake(&mutex->__lock, 1, (mutex->__kind & __MUTEX_SHARED) != 0);
    return 0;
}

int pthread_mutexattr_init(pthread_mutexattr_t* attr)
{
    attr->__kind = 0;
    return 0;
}

int pthread_mutexattr_destroy(pthread_mutexattr_t* attr)
{
    (void)attr;
    return 0;
}

int pthread_mutexattr_gettype(const pthread_mutexattr_t* restrict attr, int* restrict type)
{
    *type = attr->__kind & __MUTEX_TYPE;
    return 0;
}

int pthread_mutexattr_settype(pthread_mutexattr_t* attr, int type)
{
    if (type != PTHREAD_MUTEX_NORMAL && type != PTHREAD_MUTEX_RECURSIVE && type != PTHREAD_MUTEX_ERRORCHECK)
        return EINVAL;
    attr->__kind = (attr->__kind & ~__MUTEX_TYPE) | type;
    return 0;
}

int pthread_mutexattr_getpshared(const pthread_mutexattr_t* restrict attr, int* restrict shared)
{
    *shared = (attr->__kind & __MUTEX_SHARED) != 0 ? PTHREAD_PROCESS_SHARED : PTHREAD_PROCESS_PRIVATE;
    return 0;
}

int pthread_mutexattr_setpshared(pthread_mutexattr_t* attr, int shared)
{
    if (shared != PTHREAD_PROCESS_PRIVATE && shared != PTHREAD_PROCESS_SHARED)
        return EINVAL;
    attr->__kind = shared == PTHREAD_PROCESS_SHARED ? attr->__kind | __MUTEX_SHARED : attr->__kind & ~__MUTEX_SHARED;
    return 0;
}

int pthread_cond_init(pthread_cond_t* restrict cond, const pthread_condattr_t* restrict attr)
{
    cond->__sequence = 0;
    cond->__waiters = 0;
    cond->__clock = attr != (const pthread_condattr_t*)0 ? attr->__clock : CLOCK_REALTIME;
    cond->__shared = attr != (const pthread_condattr_t*)0 ? attr->__shared : PTHREAD_PROCESS_PRIVATE;
    return 0;
}

int pthread_cond_destroy(pthread_cond_t* cond)
{
    (void)cond;
    return 0;
}

// A signal bumps the sequence before reading the waiter count, and a waiter counts itself before reading the sequence
static int __cond_wait(pthread_cond_t* cond, pthread_mutex_t* mutex, const struct timespec* deadline)
{
    int sequence;
    int result;
    long waited;
    if (__deadline_invalid(deadline))
        return EINVAL;
    __atomic_add(&cond->__waiters, 1);
    sequence = cond->__sequence;
    result = pthread_mutex_unlock(mutex);
    if (result != 0)
    {
        __atomic_add(&cond->__waiters, -1);
        return result;
    }
    waited = __futex_wait(&cond->__sequence, sequence, cond->__shared, deadline, cond->__clock);
    __atomic_add(&cond->__waiters, -1);
    __mutex_lock(mutex, (const struct timespec*)0, 1);
    return waited == -ETIMEDOUT ? ETIMEDOUT : 0;
}

int pthread_cond_wait(pthread_cond_t* restrict cond, pthread_mutex_t* restrict mutex)
{
    return __cond_wait(cond, mutex, (const struct timespec*)0);
}

int pthread_cond_timedwait(pthread_cond_t* restrict cond, pthread_mutex_t* restrict mutex, const struct timespec* restrict deadline)
{
    return __cond_wait(cond, mutex, deadline);
}

int pthread_cond_signal(pthread_cond_t* cond)
{
    __atomic_add(&cond->__sequence, 1);
    if (__atomic_load(&cond->__waiters) != 0)
        __futex_wake(&cond->__sequence, 1, cond->__shared);
    return 0;
}

int pthread_cond_broadcast(pthread_cond_t* cond)
{
    __atomic_add(&cond->__sequence, 1);
    if (__atomic_load(&cond->__waiters) != 0)
        __futex_wake(&cond->__sequence, INT_MAX, cond->__shared);
    return 0;
}

int pthread_condattr_init(pthread_condattr_t* attr)
{
    attr->__clock = CLOCK_REALTIME;
    attr->__shared = PTHREAD_PROCESS_PRIVATE;
    return 0;
}

int pthread_condattr_destroy(pthread_condattr_t* attr)
{
    (void)attr;
    return 0;
}

int pthread_condattr_getclock(const pthread_condattr_t* restrict attr, clockid_t* restrict clock)
{
    *clock = attr->__clock;
    return 0;
}

int pthread_condattr_setclock(pthread_condattr_t* attr, clockid_t clock)
{
    if (clock != CLOCK_REALTIME && clock != CLOCK_MONOTONIC)
        return EINVAL;
    attr->__clock = clock;
    return 0;
}

int pthread_condattr_getpshared(const pthread_condattr_t* restrict attr, int* restrict shared)
{
    *shared = attr->__shared;
    return 0;
}

int pthread_condattr_setpshared(pthread_condattr_t* attr, int shared)
{
    if (shared != PTHREAD_PROCESS_PRIVATE && shared != PTHREAD_PROCESS_SHARED)
        return EINVAL;
    attr->__shared = shared;
    return 0;
}

int pthread_rwlock_init(pthread_rwlock_t* restrict rwlock, const pthread_rwlockattr_t* restrict attr)
{
    rwlock->__lock = 0;
    rwlock->__waiters = 0;
    rwlock->__shared = attr != (const pthread_rwlockattr_t*)0 ? attr->__shared : PTHREAD_PROCESS_PRIVATE;
    return 0;
}

int pthread_rwlock_destroy(pthread_rwlock_t* rwlock)
{
    (void)rwlock;
    return 0;
}

// The low bits count readers, all of them set is a writer, and the top bit says someone sleeps on the word
int pthread_rwlock_tryrdlock(pthread_rwlock_t* rwlock)
{
    for (;;)
    {
        int value = rwlock->__lock;
        int readers = value & __RWLOCK_WRITER;
        if (readers == __RWLOCK_WRITER)
            return EBUSY;
        if (readers == __RWLOCK_WRITER - 1)
            return EAGAIN;
        if (__atomic_cas(&rwlock->__lock, value, value + 1) == value)
            return 0;
    }
}

int pthread_rwlock_trywrlock(pthread_rwlock_t* rwlock)
{
    return __atomic_cas(&rwlock->__lock, 0, __RWLOCK_WRITER) == 0 ? 0 : EBUSY;
}

static int __rwlock_wait(pthread_rwlock_t* rwlock, int writer, const struct timespec* deadline)
{
    if (__deadline_invalid(deadline))
        return EINVAL;
    for (;;)
    {
        int result = writer ? pthread_rwlock_trywrlock(rwlock) : pthread_rwlock_tryrdlock(rwlock);
        int value;
        long waited;
        if (result != EBUSY)
            return result;
        value = rwlock->__lock;
        if ((value & __RWLOCK_WRITER) == 0 || (!writer && (value & __RWLOCK_WRITER) != __RWLOCK_WRITER))
            continue;
        __atomic_add(&rwlock->__waiters, 1);
        __atomic_cas(&rwlock->__lock, value, value | __RWLOCK_WAITERS);
        waited = __futex_wait(&rwlock->__lock, value | __RWLOCK_WAITERS, rwlock->__shared, deadline, CLOCK_REALTIME);
        __atomic_add(&rwlock->__waiters, -1);
        if (waited == -ETIMEDOUT)
            return ETIMEDOUT;
    }
}

int pthread_rwlock_rdlock(pthread_rwlock_t* rwlock)
{
    return __rwlock_wait(rwlock, 0, (const struct timespec*)0);
}

int pthread_rwlock_timedrdlock(pthread_rwlock_t* restrict rwlock, const struct timespec* restrict deadline)
{
    return __rwlock_wait(rwlock, 0, deadline);
}

int pthread_rwlock_wrlock(pthread_rwlock_t* rwlock)
{
    return __rwlock_wait(rwlock, 1, (const struct timespec*)0);
}

int pthread_rwlock_timedwrlock(pthread_rwlock_t* restrict rwlock, const struct timespec* restrict deadline)
{
    return __rwlock_wait(rwlock, 1, deadline);
}

int pthread_rwlock_unlock(pthread_rwlock_t* rwlock)
{
    int value;
    int readers;
    int next;
    int waiters;
    do
    {
        value = rwlock->__lock;
        readers = value & __RWLOCK_WRITER;
        waiters = rwlock->__waiters;
        next = readers == __RWLOCK_WRITER || readers == 1 ? 0 : value - 1;
    } while (__atomic_cas(&rwlock->__lock, value, next) != value);
    if (next == 0 && (waiters != 0 || value < 0))
        __futex_wake(&rwlock->__lock, readers == __RWLOCK_WRITER ? INT_MAX : 1, rwlock->__shared);
    return 0;
}

int pthread_rwlockattr_init(pthread_rwlockattr_t* attr)
{
    attr->__shared = PTHREAD_PROCESS_PRIVATE;
    return 0;
}

int pthread_rwlockattr_destroy(pthread_rwlockattr_t* attr)
{
    (void)attr;
    return 0;
}

int pthread_rwlockattr_getpshared(const pthread_rwlockattr_t* restrict attr, int* restrict shared)
{
    *shared = attr->__shared;
    return 0;
}

int pthread_rwlockattr_setpshared(pthread_rwlockattr_t* attr, int shared)
{
    if (shared != PTHREAD_PROCESS_PRIVATE && shared != PTHREAD_PROCESS_SHARED)
        return EINVAL;
    attr->__shared = shared;
    return 0;
}

int pthread_spin_init(pthread_spinlock_t* lock, int shared)
{
    (void)shared;
    *lock = 0;
    return 0;
}

int pthread_spin_destroy(pthread_spinlock_t* lock)
{
    (void)lock;
    return 0;
}

// On a single hart the holder runs only once the spinner yields
int pthread_spin_lock(pthread_spinlock_t* lock)
{
    while (__atomic_swap(lock, 1) != 0)
        sched_yield();
    return 0;
}

int pthread_spin_trylock(pthread_spinlock_t* lock)
{
    return __atomic_swap(lock, 1) == 0 ? 0 : EBUSY;
}

int pthread_spin_unlock(pthread_spinlock_t* lock)
{
    __atomic_swap(lock, 0);
    return 0;
}

int pthread_barrier_init(pthread_barrier_t* restrict barrier, const pthread_barrierattr_t* restrict attr, unsigned int count)
{
    if (count == 0 || count > INT_MAX)
        return EINVAL;
    barrier->__sequence = 0;
    barrier->__count = 0;
    barrier->__limit = count;
    barrier->__shared = attr != (const pthread_barrierattr_t*)0 ? attr->__shared : PTHREAD_PROCESS_PRIVATE;
    return 0;
}

int pthread_barrier_destroy(pthread_barrier_t* barrier)
{
    (void)barrier;
    return 0;
}

int pthread_barrier_wait(pthread_barrier_t* barrier)
{
    int sequence = __atomic_load(&barrier->__sequence);
    if ((unsigned int)__atomic_add(&barrier->__count, 1) + 1u == barrier->__limit)
    {
        barrier->__count = 0;
        __atomic_add(&barrier->__sequence, 1);
        __futex_wake(&barrier->__sequence, INT_MAX, barrier->__shared);
        return PTHREAD_BARRIER_SERIAL_THREAD;
    }
    while (__atomic_load(&barrier->__sequence) == sequence)
        __futex_wait(&barrier->__sequence, sequence, barrier->__shared, (const struct timespec*)0, CLOCK_REALTIME);
    return 0;
}

int pthread_barrierattr_init(pthread_barrierattr_t* attr)
{
    attr->__shared = PTHREAD_PROCESS_PRIVATE;
    return 0;
}

int pthread_barrierattr_destroy(pthread_barrierattr_t* attr)
{
    (void)attr;
    return 0;
}

int pthread_barrierattr_getpshared(const pthread_barrierattr_t* restrict attr, int* restrict shared)
{
    *shared = attr->__shared;
    return 0;
}

int pthread_barrierattr_setpshared(pthread_barrierattr_t* attr, int shared)
{
    if (shared != PTHREAD_PROCESS_PRIVATE && shared != PTHREAD_PROCESS_SHARED)
        return EINVAL;
    attr->__shared = shared;
    return 0;
}

int pthread_key_create(pthread_key_t* key, void (*destructor)(void*))
{
    unsigned int index;
    int result = EAGAIN;
    __libc_lock(&__pthread_list_lock);
    for (index = 0; index < PTHREAD_KEYS_MAX; index = index + 1)
    {
        if (__pthread_keys_used[index] == 0)
        {
            __pthread_keys_used[index] = 1;
            __pthread_destructors[index] = destructor;
            *key = index;
            result = 0;
            break;
        }
    }
    __libc_unlock(&__pthread_list_lock);
    return result;
}

int pthread_key_delete(pthread_key_t key)
{
    struct __pthread* thread;
    int result = 0;
    if (key >= PTHREAD_KEYS_MAX)
        return EINVAL;
    __libc_lock(&__pthread_list_lock);
    if (__pthread_keys_used[key] == 0)
        result = EINVAL;
    else
    {
        struct __pthread* self = __pthread_current();
        __pthread_keys_used[key] = 0;
        __pthread_destructors[key] = (void (*)(void*))0;
        thread = self;
        do
        {
            thread->specific[key] = (void*)0;
            thread = thread->next;
        } while (thread != self);
    }
    __libc_unlock(&__pthread_list_lock);
    return result;
}

void* pthread_getspecific(pthread_key_t key)
{
    if (key >= PTHREAD_KEYS_MAX)
        return (void*)0;
    return __pthread_current()->specific[key];
}

int pthread_setspecific(pthread_key_t key, const void* value)
{
    if (key >= PTHREAD_KEYS_MAX || __pthread_keys_used[key] == 0)
        return EINVAL;
    __pthread_current()->specific[key] = (void*)value;
    return 0;
}

int sem_init(sem_t* semaphore, int shared, unsigned int value)
{
    if (value > SEM_VALUE_MAX)
    {
        errno = EINVAL;
        return -1;
    }
    semaphore->__value = (int)value;
    semaphore->__waiters = 0;
    semaphore->__shared = shared;
    return 0;
}

int sem_destroy(sem_t* semaphore)
{
    (void)semaphore;
    return 0;
}

int sem_trywait(sem_t* semaphore)
{
    for (;;)
    {
        int value = semaphore->__value;
        if (value <= 0)
        {
            errno = EAGAIN;
            return -1;
        }
        if (__atomic_cas(&semaphore->__value, value, value - 1) == value)
            return 0;
    }
}

int sem_timedwait(sem_t* restrict semaphore, const struct timespec* restrict deadline)
{
    for (;;)
    {
        long waited;
        int value = semaphore->__value;
        if (value > 0)
        {
            if (__atomic_cas(&semaphore->__value, value, value - 1) == value)
                return 0;
            continue;
        }
        if (__deadline_invalid(deadline))
        {
            errno = EINVAL;
            return -1;
        }
        __atomic_add(&semaphore->__waiters, 1);
        waited = __futex_wait(&semaphore->__value, 0, semaphore->__shared, deadline, CLOCK_REALTIME);
        __atomic_add(&semaphore->__waiters, -1);
        if (waited == -ETIMEDOUT || waited == -EINTR)
        {
            errno = (int)-waited;
            return -1;
        }
    }
}

int sem_wait(sem_t* semaphore)
{
    return sem_timedwait(semaphore, (const struct timespec*)0);
}

int sem_post(sem_t* semaphore)
{
    for (;;)
    {
        int value = semaphore->__value;
        if (value == SEM_VALUE_MAX)
        {
            errno = EOVERFLOW;
            return -1;
        }
        if (__atomic_cas(&semaphore->__value, value, value + 1) == value)
            break;
    }
    if (__atomic_load(&semaphore->__waiters) != 0)
        __futex_wake(&semaphore->__value, 1, semaphore->__shared);
    return 0;
}

int sem_getvalue(sem_t* restrict semaphore, int* restrict value)
{
    *value = semaphore->__value;
    return 0;
}

#endif
