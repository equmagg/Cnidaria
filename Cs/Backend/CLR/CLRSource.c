typedef unsigned char u8;
typedef unsigned short u16;
typedef unsigned int u32;

#if __SIZEOF_POINTER__ == 8
typedef unsigned long long usize;
typedef signed long long isize;
#else
typedef unsigned int usize;
typedef signed int isize;
#endif

#define SYNC_BLOCK_SIZE __SIZEOF_POINTER__
#define MANAGED_OBJECT_HEADER_SIZE __SIZEOF_POINTER__
#define MINIMUM_MANAGED_OBJECT_SIZE __SIZEOF_POINTER__ * 2
#define MINIMUM_GC_OBJECT_SIZE SYNC_BLOCK_SIZE + MINIMUM_MANAGED_OBJECT_SIZE
#define STRING_LENGTH_OFFSET __SIZEOF_POINTER__
#define STRING_FIRST_CHAR_OFFSET __SIZEOF_POINTER__ + 4
#define ARRAY_LENGTH_OFFSET __SIZEOF_POINTER__
#define ARRAY_DATA_OFFSET __SIZEOF_POINTER__ * 2

#if defined(__linux__) && (defined(__riscv) || defined(__aarch64__))
#define RH_SYS_WRITE 64ul
#define RH_SYS_EXIT 93ul
#define RH_SYS_MUNMAP 215ul
#define RH_SYS_MMAP 222ul
#define RH_SYS_MPROTECT 226ul
#define RH_SYS_GETTID 178ul
#define RH_SYS_GETCPU 168ul
#elif defined(__linux__) && defined(__x86_64__)
#define RH_SYS_WRITE 1ul
#define RH_SYS_EXIT 60ul
#define RH_SYS_MUNMAP 11ul
#define RH_SYS_MMAP 9ul
#define RH_SYS_MPROTECT 10ul
#define RH_SYS_GETTID 186ul
#define RH_SYS_GETCPU 309ul
#elif defined(__linux__) && defined(__i386__)
#define RH_SYS_WRITE 4ul
#define RH_SYS_EXIT 1ul
#define RH_SYS_MUNMAP 91ul
#define RH_SYS_MMAP 90ul
#define RH_SYS_MPROTECT 125ul
#define RH_SYS_GETTID 224ul
#define RH_SYS_GETCPU 318ul
#elif defined(_WIN32)
#define RH_WIN_MEM_COMMIT 0x00001000u
#define RH_WIN_MEM_RESERVE 0x00002000u
#define RH_WIN_MEM_DECOMMIT 0x00004000u
#define RH_WIN_MEM_RELEASE 0x00008000u
#define RH_WIN_PAGE_NOACCESS 0x01u
#define RH_WIN_PAGE_READWRITE 0x04u
#else
#error Unsupported CLR runtime target
#endif

#define RH_PROT_NONE 0ul
#define RH_PROT_READ 1ul
#define RH_PROT_WRITE 2ul
#define RH_MAP_PRIVATE 2ul
#define RH_MAP_FIXED 16ul
#define RH_MAP_ANONYMOUS 32ul
#define RH_HEAP_RESERVE (64ul * 1024ul * 1024ul)
#define RH_HEAP_INITIAL_COMMIT (64ul * 1024ul)
#define RH_HEAP_COMMIT_GRANULARITY (64ul * 1024ul)
#define RH_PAGE_SIZE 4096ul
#define RH_HEAP_ALIGNMENT 16ul
#define RH_HGLOBAL_HEADER_SIZE RH_HEAP_ALIGNMENT
#define RH_BLOCK_HEADER_SIZE (__SIZEOF_POINTER__ * 4ul)
#define RH_BLOCK_FREE 0ul
#define RH_BLOCK_OBJECT 1ul
#define RH_BLOCK_MARK 1ul
#define RH_BLOCK_SCANNED 2ul
#define RH_ROOT_OBJECT 0ul
#define RH_ROOT_INTERIOR 1ul
#define RH_EETYPE_KIND_MASK 0x00030000u
#define RH_EETYPE_PARAMETERIZED_KIND 0x00020000u
#define RH_EETYPE_HAS_POINTERS 0x01000000u
#define RH_EETYPE_ELEMENT_TYPE_MASK 0x7c000000u
#define RH_EETYPE_ELEMENT_TYPE_SHIFT 26u
#define RH_EETYPE_ELEMENT_TYPE_CLASS 0x14u
#define RH_EETYPE_ELEMENT_TYPE_INTERFACE 0x15u
#define RH_EETYPE_ELEMENT_TYPE_SYSTEM_ARRAY 0x16u
#define RH_EETYPE_ELEMENT_TYPE_ARRAY 0x17u
#define RH_EETYPE_ELEMENT_TYPE_SZARRAY 0x18u
#define RH_EETYPE_ELEMENT_TYPE_BYREF 0x19u
#define RH_EETYPE_ELEMENT_TYPE_POINTER 0x1au
#define RH_EETYPE_HAS_COMPONENT_SIZE 0x80000000u
#define RH_EETYPE_COMPONENT_SIZE_MASK 0xffffu
#define RH_TYPE_FIXED 0ul
#define RH_TYPE_STRING 1ul
#define RH_TYPE_SZARRAY 2ul
#define RH_TYPE_MDARRAY 3ul
#define RH_TYPE_PARAMETERIZED 4ul
#define RH_MINIMUM_GC_OBJECT_SIZE MINIMUM_GC_OBJECT_SIZE
#define RH_EH_MAX_FRAMES 4096ul
#define RH_EH_MAX_CONTINUATIONS 256ul
#define RH_EH_MAX_CATCH_CONTEXTS 256ul
#define RH_EH_CATCH 1ul
#define RH_EH_CATCH_ALL 2ul
#define RH_EH_FINALLY 3ul
#define RH_EH_FAULT 4ul
#define RH_EH_CONTINUATION_LEAVE 1ul
#define RH_EH_CONTINUATION_THROW 2ul

typedef struct RhGcField
{
    usize offset;
    usize kind;
} RhGcField;

typedef struct RhMethodTable
{
    u32 flags;
    u32 base_size;
    const void* related_type;
    u16 vtable_slot_count;
    u16 interface_count;
    u32 hash_code;
} RhMethodTable;

typedef struct RhTypeInfo
{
    const RhMethodTable* type;
    usize gc_field_count;
    const RhGcField* gc_fields;
    usize component_gc_field_count;
    const RhGcField* component_gc_fields;
    usize runtime_kind;
} RhTypeInfo;

typedef struct RhRoot
{
    isize frame_offset;
    usize kind;
} RhRoot;

typedef struct RhSafePoint
{
    const void* return_address;
    isize saved_frame_pointer_offset;
    isize saved_return_address_offset;
    usize root_count;
    const RhRoot* roots;
} RhSafePoint;

typedef struct RhStaticRoot
{
    void* address;
    usize kind;
} RhStaticRoot;

typedef struct RhObject RhObject;

typedef struct RhEhClause
{
    usize kind;
    const void* try_start;
    const void* try_end;
    const void* handler_start;
    const void* handler_end;
    const RhMethodTable* catch_type;
    isize parent_index;
    isize source_try_start;
    isize source_try_end;
    isize source_handler_start;
    isize source_handler_end;
    isize source_handler_index;
} RhEhClause;

typedef struct RhEhMethodInfo
{
    usize clause_count;
    const RhEhClause* clauses;
} RhEhMethodInfo;

typedef struct RhEhFrame
{
    const RhEhMethodInfo* method;
    void* frame_pointer;
    const void* current_ip;
} RhEhFrame;

typedef union RhEhRegisterContext
{
    u8 data[512];
    usize alignment;
} RhEhRegisterContext;

#if defined(__riscv_flen) && __riscv_flen >= 32
typedef union RhFloatBits
{
    float value;
    u32 bits;
} RhFloatBits;
#endif

#if defined(__riscv_flen) && __riscv_flen >= 64 && __SIZEOF_POINTER__ == 8
typedef union RhDoubleBits
{
    double value;
    usize bits;
} RhDoubleBits;
#endif

typedef struct RhEhContinuation
{
    usize kind;
    const void* target;
    usize frame_index;
    const void* source_ip;
    isize clause_index;
    RhObject* exception;
} RhEhContinuation;

typedef struct RhCatchContext
{
    RhObject* exception;
    usize frame_index;
    const RhEhMethodInfo* method;
    isize clause_index;
} RhCatchContext;

typedef struct RhBlock
{
    usize size;
    usize kind;
    struct RhBlock* mark_next;
    usize flags;
} RhBlock;

struct RhObject
{
    const RhMethodTable* type;
};

static volatile usize rh_stack_base;
static const RhSafePoint* rh_safe_points;
static usize rh_safe_point_count;
static const RhTypeInfo* rh_type_infos;
static usize rh_type_info_count;
static const RhStaticRoot* rh_static_roots;
static usize rh_static_root_count;
static u8* rh_heap_base;
static u8* rh_heap_used;
static u8* rh_heap_committed;
static u8* rh_heap_limit;
static usize rh_allocation_debt;
#define RH_GC_POLL_DEBT_LIMIT 262144ul
static RhBlock* rh_mark_stack;
static RhBlock* rh_free_list;
static int rh_gc_running;
static RhObject* rh_delegate_temporary_root;
static RhObject* rh_active_exception;
static RhEhContinuation rh_eh_continuations[RH_EH_MAX_CONTINUATIONS];
static RhEhRegisterContext rh_eh_continuation_registers[RH_EH_MAX_CONTINUATIONS];
static usize rh_eh_continuation_count;
static RhCatchContext rh_catch_contexts[RH_EH_MAX_CATCH_CONTEXTS];
static usize rh_catch_context_count;

const RhSafePoint* RhpCurrentSafePoint;
void* RhpCurrentFramePointer;
volatile u32 RhpGcPollRequested;
usize RhpEhFrameCount;
RhEhFrame RhpEhFrames[RH_EH_MAX_FRAMES];
RhEhRegisterContext RhpEhRegisterContexts[RH_EH_MAX_FRAMES];
RhObject* RhpCurrentException;

#if defined(__riscv_flen) && __riscv_flen >= 32
float RhpFmodF(float x, float y)
{
    RhFloatBits ux;
    RhFloatBits uy;
    int ex;
    int ey;
    u32 sx;
    u32 difference;

    ux.value = x;
    uy.value = y;
    ex = (int)((ux.bits >> 23) & 0xffu);
    ey = (int)((uy.bits >> 23) & 0xffu);
    sx = ux.bits & 0x80000000u;

    if ((uy.bits << 1) == 0u ||
        (uy.bits & 0x7fffffffu) > 0x7f800000u ||
        ex == 0xff)
    {
        float invalid = x * y;
        return invalid / invalid;
    }

    if ((ux.bits << 1) <= (uy.bits << 1))
    {
        if ((ux.bits << 1) == (uy.bits << 1))
            return 0.0f * x;
        return x;
    }

    if (ex == 0)
    {
        difference = ux.bits << 9;
        while ((difference >> 31) == 0u)
        {
            ex = ex - 1;
            difference = difference << 1;
        }
        ux.bits = ux.bits << (-ex + 1);
    }
    else
    {
        ux.bits = (ux.bits & 0x007fffffu) | 0x00800000u;
    }

    if (ey == 0)
    {
        difference = uy.bits << 9;
        while ((difference >> 31) == 0u)
        {
            ey = ey - 1;
            difference = difference << 1;
        }
        uy.bits = uy.bits << (-ey + 1);
    }
    else
    {
        uy.bits = (uy.bits & 0x007fffffu) | 0x00800000u;
    }

    while (ex > ey)
    {
        difference = ux.bits - uy.bits;
        if ((difference >> 31) == 0u)
        {
            if (difference == 0u)
                return 0.0f * x;
            ux.bits = difference;
        }
        ux.bits = ux.bits << 1;
        ex = ex - 1;
    }

    difference = ux.bits - uy.bits;
    if ((difference >> 31) == 0u)
    {
        if (difference == 0u)
            return 0.0f * x;
        ux.bits = difference;
    }

    while ((ux.bits >> 23) == 0u)
    {
        ux.bits = ux.bits << 1;
        ex = ex - 1;
    }

    if (ex > 0)
    {
        ux.bits = ux.bits - 0x00800000u;
        ux.bits = ux.bits | ((u32)ex << 23);
    }
    else
    {
        ux.bits = ux.bits >> (-ex + 1);
    }

    ux.bits = ux.bits | sx;
    return ux.value;
}
#endif

#if defined(__riscv_flen) && __riscv_flen >= 64 && __SIZEOF_POINTER__ == 8
double RhpFmod(double x, double y)
{
    RhDoubleBits ux;
    RhDoubleBits uy;
    int ex;
    int ey;
    usize sign_mask;
    usize fraction_mask;
    usize infinity_bits;
    usize sx;
    usize difference;

    ux.value = x;
    uy.value = y;
    sign_mask = ((usize)1u) << 63;
    fraction_mask = (sign_mask >> 11) - 1u;
    infinity_bits = ((usize)0x7ffu) << 52;
    ex = (int)((ux.bits >> 52) & 0x7ffu);
    ey = (int)((uy.bits >> 52) & 0x7ffu);
    sx = ux.bits & sign_mask;

    if ((uy.bits << 1) == 0u ||
        (uy.bits & (sign_mask - 1u)) > infinity_bits ||
        ex == 0x7ff)
    {
        double invalid = x * y;
        return invalid / invalid;
    }

    if ((ux.bits << 1) <= (uy.bits << 1))
    {
        if ((ux.bits << 1) == (uy.bits << 1))
            return 0.0 * x;
        return x;
    }

    if (ex == 0)
    {
        difference = ux.bits << 12;
        while ((difference >> 63) == 0u)
        {
            ex = ex - 1;
            difference = difference << 1;
        }
        ux.bits = ux.bits << (-ex + 1);
    }
    else
    {
        ux.bits = (ux.bits & fraction_mask) | (((usize)1u) << 52);
    }

    if (ey == 0)
    {
        difference = uy.bits << 12;
        while ((difference >> 63) == 0u)
        {
            ey = ey - 1;
            difference = difference << 1;
        }
        uy.bits = uy.bits << (-ey + 1);
    }
    else
    {
        uy.bits = (uy.bits & fraction_mask) | (((usize)1u) << 52);
    }

    while (ex > ey)
    {
        difference = ux.bits - uy.bits;
        if ((difference >> 63) == 0u)
        {
            if (difference == 0u)
                return 0.0 * x;
            ux.bits = difference;
        }
        ux.bits = ux.bits << 1;
        ex = ex - 1;
    }

    difference = ux.bits - uy.bits;
    if ((difference >> 63) == 0u)
    {
        if (difference == 0u)
            return 0.0 * x;
        ux.bits = difference;
    }

    while ((ux.bits >> 52) == 0u)
    {
        ux.bits = ux.bits << 1;
        ex = ex - 1;
    }

    if (ex > 0)
    {
        ux.bits = ux.bits - (((usize)1u) << 52);
        ux.bits = ux.bits | ((usize)ex << 52);
    }
    else
    {
        ux.bits = ux.bits >> (-ex + 1);
    }

    ux.bits = ux.bits | sx;
    return ux.value;
}
#endif

void RhpFallbackFailFast(int code);
void RhpEhTransfer(void* frame_pointer, const void* target, const void* register_context);

#if defined(__linux__) && defined(__riscv)
static isize rh_syscall1(usize number, usize arg0)
{
    usize result = arg0;
    __asm__ volatile("ecall" : [result] "+{a0}"(result) : [number] "{a7}"(number) : "memory");
    return (isize)result;
}

static isize rh_syscall2(usize number, usize arg0, usize arg1)
{
    usize result = arg0;
    __asm__ volatile("ecall" : [result] "+{a0}"(result) : [arg1] "{a1}"(arg1), [number] "{a7}"(number) : "memory");
    return (isize)result;
}

static isize rh_syscall3(usize number, usize arg0, usize arg1, usize arg2)
{
    usize result = arg0;
    __asm__ volatile("ecall" : [result] "+{a0}"(result) : [arg1] "{a1}"(arg1), [arg2] "{a2}"(arg2), [number] "{a7}"(number) : "memory");
    return (isize)result;
}

static isize rh_syscall6(usize number, usize arg0, usize arg1, usize arg2, usize arg3, usize arg4, usize arg5)
{
    usize result = arg0;
    __asm__ volatile("ecall" : [result] "+{a0}"(result) : [arg1] "{a1}"(arg1), [arg2] "{a2}"(arg2), [arg3] "{a3}"(arg3), [arg4] "{a4}"(arg4), [arg5] "{a5}"(arg5), [number] "{a7}"(number) : "memory");
    return (isize)result;
}
#elif defined(__linux__) && defined(__aarch64__)
static isize rh_syscall1(usize number, usize arg0)
{
    usize result = arg0;
    __asm__ volatile("svc #0" : [result] "+{x0}"(result) : [number] "{x8}"(number) : "memory");
    return (isize)result;
}

static isize rh_syscall2(usize number, usize arg0, usize arg1)
{
    usize result = arg0;
    __asm__ volatile("svc #0" : [result] "+{x0}"(result) : [arg1] "{x1}"(arg1), [number] "{x8}"(number) : "memory");
    return (isize)result;
}

static isize rh_syscall3(usize number, usize arg0, usize arg1, usize arg2)
{
    usize result = arg0;
    __asm__ volatile("svc #0" : [result] "+{x0}"(result) : [arg1] "{x1}"(arg1), [arg2] "{x2}"(arg2), [number] "{x8}"(number) : "memory");
    return (isize)result;
}

static isize rh_syscall6(usize number, usize arg0, usize arg1, usize arg2, usize arg3, usize arg4, usize arg5)
{
    usize result = arg0;
    __asm__ volatile(
        "svc #0"
        : [result] "+{x0}"(result)
        : [arg1] "{x1}"(arg1), [arg2] "{x2}"(arg2), [arg3] "{x3}"(arg3), [arg4] "{x4}"(arg4), [arg5] "{x5}"(arg5), [number] "{x8}"(number)
        : "memory");
    return (isize)result;
}
#elif defined(__linux__) && defined(__x86_64__)
static isize rh_syscall1(usize number, usize arg0)
{
    usize result = number;
    __asm__ volatile("syscall" : [result] "+{rax}"(result) : [arg0] "{rdi}"(arg0) : "rcx", "r11", "memory");
    return (isize)result;
}

static isize rh_syscall2(usize number, usize arg0, usize arg1)
{
    usize result = number;
    __asm__ volatile("syscall" : [result] "+{rax}"(result) : [arg0] "{rdi}"(arg0), [arg1] "{rsi}"(arg1) : "rcx", "r11", "memory");
    return (isize)result;
}

static isize rh_syscall3(usize number, usize arg0, usize arg1, usize arg2)
{
    usize result = number;
    __asm__ volatile("syscall" : [result] "+{rax}"(result) : [arg0] "{rdi}"(arg0), [arg1] "{rsi}"(arg1), [arg2] "{rdx}"(arg2) : "rcx", "r11", "memory");
    return (isize)result;
}

static isize rh_syscall6(usize number, usize arg0, usize arg1, usize arg2, usize arg3, usize arg4, usize arg5)
{
    usize result = number;
    __asm__ volatile(
        "syscall"
        : [result] "+{rax}"(result)
        : [arg0] "{rdi}"(arg0), [arg1] "{rsi}"(arg1), [arg2] "{rdx}"(arg2), [arg3] "{r10}"(arg3), [arg4] "{r8}"(arg4), [arg5] "{r9}"(arg5)
        : "rcx", "r11", "memory");
    return (isize)result;
}
#elif defined(__linux__) && defined(__i386__)
static isize rh_syscall1(usize number, usize arg0)
{
    usize result = number;
    __asm__ volatile(
        "push ebx\n"
        ".byte 0xcd, 0x80\n"
        "pop ebx"
        : [result] "+{eax}"(result)
        : [arg0] "{ebx}"(arg0)
        : "ecx", "edx", "cc", "memory");
    return (isize)result;
}

static isize rh_syscall2(usize number, usize arg0, usize arg1)
{
    usize result = number;
    __asm__ volatile(
        "push ebx\n"
        ".byte 0xcd, 0x80\n"
        "pop ebx"
        : [result] "+{eax}"(result)
        : [arg0] "{ebx}"(arg0), [arg1] "{ecx}"(arg1)
        : "edx", "cc", "memory");
    return (isize)result;
}

static isize rh_syscall3(usize number, usize arg0, usize arg1, usize arg2)
{
    usize result = number;
    __asm__ volatile(
        "push ebx\n"
        ".byte 0xcd, 0x80\n"
        "pop ebx"
        : [result] "+{eax}"(result)
        : [arg0] "{ebx}"(arg0), [arg1] "{ecx}"(arg1), [arg2] "{edx}"(arg2)
        : "cc", "memory");
    return (isize)result;
}

static isize rh_syscall6(usize number, usize arg0, usize arg1, usize arg2, usize arg3, usize arg4, usize arg5)
{
    usize arguments[6];
    usize result = number;
    arguments[0] = arg0;
    arguments[1] = arg1;
    arguments[2] = arg2;
    arguments[3] = arg3;
    arguments[4] = arg4;
    arguments[5] = arg5;
    __asm__ volatile(
        "push ebx\n"
        ".byte 0xcd, 0x80\n"
        "pop ebx"
        : [result] "+{eax}"(result)
        : [arguments] "{ebx}"(arguments)
        : "ecx", "edx", "esi", "edi", "cc", "memory");
    return (isize)result;
}
#elif defined(_WIN32) && defined(__x86_64__)
static void* rh_windows_virtual_alloc(void* address, usize size, u32 allocation_type, u32 protection)
{
    void* result;
    void* address_arg = address;
    usize size_arg = size;
    usize allocation_type_arg = (usize)allocation_type;
    usize protection_arg = (usize)protection;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_VirtualAlloc]\n"
        "add rsp, 32"
        : "={rax}"(result), [address] "+{rcx}"(address_arg), [size] "+{rdx}"(size_arg),
        [allocation_type] "+{r8}"(allocation_type_arg), [protection] "+{r9}"(protection_arg)
        :
        : "r10", "r11", "xmm0", "xmm1", "xmm2", "xmm3", "xmm4", "xmm5", "cc", "memory");
    return result;
}

static int rh_windows_virtual_free(void* address, usize size, u32 free_type)
{
    usize result;
    void* address_arg = address;
    usize size_arg = size;
    usize free_type_arg = (usize)free_type;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_VirtualFree]\n"
        "add rsp, 32"
        : "={rax}"(result), [address] "+{rcx}"(address_arg), [size] "+{rdx}"(size_arg), [free_type] "+{r8}"(free_type_arg)
        :
        : "r9", "r10", "r11", "xmm0", "xmm1", "xmm2", "xmm3", "xmm4", "xmm5", "cc", "memory");
    return result != 0ul;
}

static void* rh_windows_stdout(void)
{
    void* result;
    __asm__ volatile(
        "sub rsp, 32\n"
        "mov ecx, -11\n"
        "call qword ptr[rip + __imp_GetStdHandle]\n"
        "add rsp, 32"
        : "={rax}"(result)
        :
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "xmm0", "xmm1", "xmm2", "xmm3", "xmm4", "xmm5", "cc", "memory");
    return result;
}

static usize rh_windows_write(void* handle, const void* data, u32 length)
{
    usize result;
    void* handle_arg = handle;
    const void* data_arg = data;
    usize length_arg = (usize)length;
    u32 written = 0u;
    __asm__ volatile(
        "lea r9, %[written]\n"
        "sub rsp, 48\n"
        "mov qword ptr[rsp + 32], 0\n"
        "call qword ptr[rip + __imp_WriteFile]\n"
        "add rsp, 48"
        : "={rax}"(result), [handle] "+{rcx}"(handle_arg), [data] "+{rdx}"(data_arg),
        [length] "+{r8}"(length_arg), [written] "=m"(written)
        :
        : "r9", "r10", "r11", "xmm0", "xmm1", "xmm2", "xmm3", "xmm4", "xmm5", "cc", "memory");
    if (result == 0ul)
        return 0ul;
    return (usize)written;
}

static void rh_windows_exit(u32 code)
{
    usize code_arg = (usize)code;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_ExitProcess]\n"
        "add rsp, 32"
        : [code] "+{rcx}"(code_arg)
        :
        : "rax", "rdx", "r8", "r9", "r10", "r11", "xmm0", "xmm1", "xmm2", "xmm3", "xmm4", "xmm5", "cc", "memory");
}

#elif defined(_WIN32) && defined(__i386__)
static void* rh_windows_virtual_alloc(void* address, usize size, u32 allocation_type, u32 protection)
{
    usize result = (usize)address;
    usize size_arg = size;
    usize allocation_type_arg = (usize)allocation_type;
    usize protection_arg = (usize)protection;
    __asm__ volatile(
        "push esi\n"
        "push edx\n"
        "push ecx\n"
        "push eax\n"
        "call dword ptr[__imp_VirtualAlloc]"
        : [result] "+{eax}"(result), [size] "+{ecx}"(size_arg),
        [allocation_type] "+{edx}"(allocation_type_arg), [protection] "+{esi}"(protection_arg)
        :
        : "cc", "memory");
    return (void*)result;
}

static int rh_windows_virtual_free(void* address, usize size, u32 free_type)
{
    usize result = (usize)address;
    usize size_arg = size;
    usize free_type_arg = (usize)free_type;
    __asm__ volatile(
        "push edx\n"
        "push ecx\n"
        "push eax\n"
        "call dword ptr[__imp_VirtualFree]"
        : [result] "+{eax}"(result), [size] "+{ecx}"(size_arg), [free_type] "+{edx}"(free_type_arg)
        :
        : "cc", "memory");
    return result != 0ul;
}

static void* rh_windows_stdout(void)
{
    usize result = (usize)-11;
    __asm__ volatile(
        "push eax\n"
        "call dword ptr[__imp_GetStdHandle]"
        : [result] "+{eax}"(result)
        :
        : "cc", "memory");
    return (void*)result;
}

static usize rh_windows_write(void* handle, const void* data, u32 length)
{
    usize result = (usize)handle;
    usize data_arg = (usize)data;
    usize length_arg = (usize)length;
    u32 written = 0u;
    __asm__ volatile(
        "push ebx\n"
        "lea ebx, %[written]\n"
        "add ebx, 4\n"
        "push 0\n"
        "push ebx\n"
        "push edx\n"
        "push ecx\n"
        "push eax\n"
        "call dword ptr[__imp_WriteFile]\n"
        "pop ebx"
        : [result] "+{eax}"(result), [data] "+{ecx}"(data_arg), [length] "+{edx}"(length_arg), [written] "=m"(written)
        :
        : "cc", "memory");
    if (result == 0ul)
        return 0ul;
    return (usize)written;
}

static void rh_windows_exit(u32 code)
{
    usize code_arg = (usize)code;
    __asm__ volatile(
        "push eax\n"
        "call dword ptr[__imp_ExitProcess]"
        : [code] "+{eax}"(code_arg)
        :
        : "cc", "memory");
}
#elif defined(_WIN32) && defined(__aarch64__)
static void* rh_windows_virtual_alloc(void* address, usize size, u32 allocation_type, u32 protection)
{
    void* result = address;
    __asm__ volatile(
        "sub sp, sp, #16\n"
        "str x30, [sp]\n"
        "ldr x16, __imp_VirtualAlloc\n"
        "blr x16\n"
        "ldr x30, [sp]\n"
        "add sp, sp, #16"
        : "+{x0}"(result)
        : [size] "{x1}"(size), [allocation_type] "{x2}"((usize)allocation_type), [protection] "{x3}"((usize)protection)
        : "x4", "x5", "x6", "x7", "x8", "x9", "x10", "x11", "x12", "x13", "x14", "x15", "x16", "x17",
        "d0", "d1", "d2", "d3", "d4", "d5", "d6", "d7", "memory");
    return result;
}

static int rh_windows_virtual_free(void* address, usize size, u32 free_type)
{
    usize result = (usize)address;
    __asm__ volatile(
        "sub sp, sp, #16\n"
        "str x30, [sp]\n"
        "ldr x16, __imp_VirtualFree\n"
        "blr x16\n"
        "ldr x30, [sp]\n"
        "add sp, sp, #16"
        : "+{x0}"(result)
        : [size] "{x1}"(size), [free_type] "{x2}"((usize)free_type)
        : "x3", "x4", "x5", "x6", "x7", "x8", "x9", "x10", "x11", "x12", "x13", "x14", "x15", "x16", "x17",
        "d0", "d1", "d2", "d3", "d4", "d5", "d6", "d7", "memory");
    return result != 0ul;
}

static void* rh_windows_stdout(void)
{
    usize result = (usize)(isize)-11;
    __asm__ volatile(
        "sub sp, sp, #16\n"
        "str x30, [sp]\n"
        "ldr x16, __imp_GetStdHandle\n"
        "blr x16\n"
        "ldr x30, [sp]\n"
        "add sp, sp, #16"
        : "+{x0}"(result)
        :
        : "x1", "x2", "x3", "x4", "x5", "x6", "x7", "x8", "x9", "x10", "x11", "x12", "x13", "x14", "x15", "x16", "x17",
        "d0", "d1", "d2", "d3", "d4", "d5", "d6", "d7", "memory");
    return (void*)result;
}

static usize rh_windows_write(void* handle, const void* data, u32 length)
{
    usize result = (usize)handle;
    u32 written = 0u;
    __asm__ volatile(
        "sub sp, sp, #16\n"
        "str x30, [sp]\n"
        "mov x4, #0\n"
        "ldr x16, __imp_WriteFile\n"
        "blr x16\n"
        "ldr x30, [sp]\n"
        "add sp, sp, #16"
        : "+{x0}"(result)
        : [data] "{x1}"(data), [length] "{x2}"((usize)length), [written] "{x3}"(&written)
        : "x4", "x5", "x6", "x7", "x8", "x9", "x10", "x11", "x12", "x13", "x14", "x15", "x16", "x17",
        "d0", "d1", "d2", "d3", "d4", "d5", "d6", "d7", "memory");
    if (result == 0ul)
        return 0ul;
    return (usize)written;
}

static void rh_windows_exit(u32 code)
{
    usize code_arg = (usize)code;
    __asm__ volatile(
        "sub sp, sp, #16\n"
        "str x30, [sp]\n"
        "ldr x16, __imp_ExitProcess\n"
        "blr x16\n"
        "ldr x30, [sp]\n"
        "add sp, sp, #16"
        : [code] "+{x0}"(code_arg)
        :
        : "x1", "x2", "x3", "x4", "x5", "x6", "x7", "x8", "x9", "x10", "x11", "x12", "x13", "x14", "x15", "x16", "x17",
        "d0", "d1", "d2", "d3", "d4", "d5", "d6", "d7", "memory");
}
#endif

static usize rh_align_up(usize value, usize alignment)
{
    return (value + alignment - 1ul) & ~(alignment - 1ul);
}

static usize rh_total_block_size(usize gc_size)
{
    usize maximum = (usize)-1;
    if (gc_size > maximum - RH_BLOCK_HEADER_SIZE - (RH_HEAP_ALIGNMENT - 1ul))
        return 0ul;
    return rh_align_up(RH_BLOCK_HEADER_SIZE + gc_size, RH_HEAP_ALIGNMENT);
}

static usize rh_component_size(const RhMethodTable* type)
{
    return (usize)(type->flags & RH_EETYPE_COMPONENT_SIZE_MASK);
}

static int rh_has_component_size(const RhMethodTable* type)
{
    return (type->flags & RH_EETYPE_HAS_COMPONENT_SIZE) != 0u;
}

static usize rh_method_table_kind(const RhMethodTable* type)
{
    return (usize)(type->flags & RH_EETYPE_KIND_MASK);
}

static usize rh_element_type(const RhMethodTable* type)
{
    return (usize)((type->flags & RH_EETYPE_ELEMENT_TYPE_MASK) >> RH_EETYPE_ELEMENT_TYPE_SHIFT);
}

static const RhMethodTable** rh_interface_map(const RhMethodTable* type)
{
    const void* map = *(const void**)((const u8*)type + 16ul + __SIZEOF_POINTER__);
    return (const RhMethodTable**)map;
}

static int rh_is_reference_type(const RhMethodTable* type)
{
    usize element_type = rh_element_type(type);
    return element_type >= RH_EETYPE_ELEMENT_TYPE_CLASS &&
        element_type <= RH_EETYPE_ELEMENT_TYPE_SZARRAY;
}

static int rh_is_assignable(const RhMethodTable* source, const RhMethodTable* target)
{
    while (1)
    {
        usize target_element_type;
        if (source == target)
            return 1;

        target_element_type = rh_element_type(target);
        if (target_element_type == RH_EETYPE_ELEMENT_TYPE_CLASS)
        {
            if (target->related_type == (const void*)0)
                return 1;
            if (rh_element_type(source) == RH_EETYPE_ELEMENT_TYPE_ARRAY ||
                rh_element_type(source) == RH_EETYPE_ELEMENT_TYPE_SZARRAY)
            {
                return 0;
            }
            source = (const RhMethodTable*)source->related_type;
            if (source == (const RhMethodTable*)0)
                return 0;
            continue;
        }

        if (target_element_type == RH_EETYPE_ELEMENT_TYPE_INTERFACE)
        {
            const RhMethodTable** interfaces = rh_interface_map(source);
            usize i = 0ul;
            if (interfaces == (const RhMethodTable**)0)
                return 0;
            while (i < (usize)source->interface_count)
            {
                if (interfaces[i] == target)
                    return 1;
                i = i + 1ul;
            }
            return 0;
        }

        if (target_element_type == RH_EETYPE_ELEMENT_TYPE_SYSTEM_ARRAY)
        {
            usize source_element_type = rh_element_type(source);
            return source_element_type == RH_EETYPE_ELEMENT_TYPE_ARRAY ||
                source_element_type == RH_EETYPE_ELEMENT_TYPE_SZARRAY;
        }

        if (target_element_type == RH_EETYPE_ELEMENT_TYPE_SZARRAY)
        {
            const RhMethodTable* source_element;
            const RhMethodTable* target_element;
            if (rh_element_type(source) != RH_EETYPE_ELEMENT_TYPE_SZARRAY)
                return 0;
            source_element = (const RhMethodTable*)source->related_type;
            target_element = (const RhMethodTable*)target->related_type;
            if (source_element == target_element)
                return 1;
            if (source_element == (const RhMethodTable*)0 ||
                target_element == (const RhMethodTable*)0 ||
                !rh_is_reference_type(source_element) ||
                !rh_is_reference_type(target_element))
            {
                return 0;
            }
            source = source_element;
            target = target_element;
            continue;
        }

        return 0;
    }
}

static int rh_eh_ip_in_range(const void* ip, const void* start, const void* end)
{
    usize value = (usize)ip;
    return value >= (usize)start && value < (usize)end;
}

static isize rh_eh_find_innermost_handler(const RhEhMethodInfo* method, const void* ip)
{
    isize best = -1;
    usize best_span = (usize)-1;
    usize i = 0ul;
    while (i < method->clause_count)
    {
        const RhEhClause* clause = &method->clauses[i];
        if (rh_eh_ip_in_range(ip, clause->handler_start, clause->handler_end))
        {
            usize span = (usize)clause->handler_end - (usize)clause->handler_start;
            if (span < best_span || (span == best_span && (isize)i > best))
            {
                best = (isize)i;
                best_span = span;
            }
        }
        i = i + 1ul;
    }
    return best;
}

static int rh_eh_source_range_contains(isize outer_start, isize outer_end, isize inner_start, isize inner_end)
{
    return inner_start >= outer_start && inner_end <= outer_end;
}

static int rh_eh_is_protected(const RhEhMethodInfo* method, const void* ip, usize clause_index)
{
    const RhEhClause* clause;
    isize current;
    if (clause_index >= method->clause_count)
        return 0;
    clause = &method->clauses[clause_index];
    if (rh_eh_ip_in_range(ip, clause->try_start, clause->try_end))
        return 1;
    current = rh_eh_find_innermost_handler(method, ip);
    if (current < 0)
        return 0;
    return rh_eh_source_range_contains(
        clause->source_try_start,
        clause->source_try_end,
        method->clauses[(usize)current].source_handler_start,
        method->clauses[(usize)current].source_handler_end);
}

static int rh_eh_handler_contains(const RhEhMethodInfo* method, isize clause_index, const void* ip)
{
    isize current;
    const RhEhClause* clause;
    if (clause_index < 0 || (usize)clause_index >= method->clause_count)
        return 0;
    current = rh_eh_find_innermost_handler(method, ip);
    if (current < 0)
        return 0;
    clause = &method->clauses[(usize)clause_index];
    return rh_eh_source_range_contains(
        clause->source_handler_start,
        clause->source_handler_end,
        method->clauses[(usize)current].source_handler_start,
        method->clauses[(usize)current].source_handler_end);
}

static int rh_eh_precedes(const RhEhClause* left, const RhEhClause* right)
{
    isize left_span = left->source_try_end - left->source_try_start;
    isize right_span = right->source_try_end - right->source_try_start;
    if (left_span != right_span)
        return left_span < right_span;
    if (left->source_try_start != right->source_try_start)
        return left->source_try_start > right->source_try_start;
    if (left->source_try_end != right->source_try_end)
        return left->source_try_end < right->source_try_end;
    if (left->source_handler_index != right->source_handler_index)
        return left->source_handler_index < right->source_handler_index;
    return left->source_handler_start < right->source_handler_start;
}

static int rh_eh_matches_catch(const RhEhClause* clause, const RhObject* exception)
{
    if (clause->kind == RH_EH_CATCH_ALL)
        return 1;
    if (clause->kind != RH_EH_CATCH || exception == (const RhObject*)0 || clause->catch_type == (const RhMethodTable*)0)
        return 0;
    return rh_is_assignable(exception->type, clause->catch_type);
}

static isize rh_eh_find_throw_handler(
    const RhEhFrame* frame,
    const RhObject* exception,
    const void* source_ip,
    isize after_clause_index)
{
    const RhEhMethodInfo* method = frame->method;
    const RhEhClause* after_clause = (const RhEhClause*)0;
    isize best = -1;
    usize i = 0ul;
    if (after_clause_index >= 0 && (usize)after_clause_index < method->clause_count)
        after_clause = &method->clauses[(usize)after_clause_index];
    while (i < method->clause_count)
    {
        const RhEhClause* clause = &method->clauses[i];
        if (rh_eh_is_protected(method, source_ip, i) &&
            (after_clause == (const RhEhClause*)0 || rh_eh_precedes(after_clause, clause)) &&
            (clause->kind == RH_EH_FINALLY || clause->kind == RH_EH_FAULT || rh_eh_matches_catch(clause, exception)) &&
            (best < 0 || rh_eh_precedes(clause, &method->clauses[(usize)best])))
        {
            best = (isize)i;
        }
        i = i + 1ul;
    }
    return best;
}

static isize rh_eh_find_leave_finally(
    const RhEhFrame* frame,
    const void* source_ip,
    const void* target,
    isize after_clause_index)
{
    const RhEhMethodInfo* method = frame->method;
    const RhEhClause* after_clause = (const RhEhClause*)0;
    isize best = -1;
    usize i = 0ul;
    if (after_clause_index >= 0 && (usize)after_clause_index < method->clause_count)
        after_clause = &method->clauses[(usize)after_clause_index];
    while (i < method->clause_count)
    {
        const RhEhClause* clause = &method->clauses[i];
        if (clause->kind == RH_EH_FINALLY &&
            rh_eh_is_protected(method, source_ip, i) &&
            !rh_eh_is_protected(method, target, i) &&
            (after_clause == (const RhEhClause*)0 || rh_eh_precedes(after_clause, clause)) &&
            (best < 0 || rh_eh_precedes(clause, &method->clauses[(usize)best])))
        {
            best = (isize)i;
        }
        i = i + 1ul;
    }
    return best;
}

static void rh_eh_copy_register_context(RhEhRegisterContext* destination, const RhEhRegisterContext* source)
{
    usize i = 0ul;
    while (i < 512ul)
    {
        destination->data[i] = source->data[i];
        i = i + 1ul;
    }
}

static void rh_eh_push_continuation(
    usize kind,
    const void* target,
    usize frame_index,
    const void* source_ip,
    isize clause_index,
    RhObject* exception)
{
    RhEhContinuation* continuation;
    if (rh_eh_continuation_count >= RH_EH_MAX_CONTINUATIONS)
        RhpFallbackFailFast(150);
    continuation = &rh_eh_continuations[rh_eh_continuation_count];
    continuation->kind = kind;
    continuation->target = target;
    continuation->frame_index = frame_index;
    continuation->source_ip = source_ip;
    continuation->clause_index = clause_index;
    continuation->exception = exception;
    rh_eh_copy_register_context(
        &rh_eh_continuation_registers[rh_eh_continuation_count],
        &RhpEhRegisterContexts[frame_index]);
    rh_eh_continuation_count = rh_eh_continuation_count + 1ul;
}

static void rh_eh_prune_continuations(usize frame_index, const void* target)
{
    usize source = 0ul;
    usize destination = 0ul;
    while (source < rh_eh_continuation_count)
    {
        RhEhContinuation continuation = rh_eh_continuations[source];
        int keep = continuation.frame_index < frame_index ||
            (continuation.frame_index == frame_index &&
                target != (const void*)0 &&
                rh_eh_handler_contains(
                    RhpEhFrames[frame_index].method,
                    continuation.clause_index,
                    target));
        if (keep)
        {
            if (destination != source)
            {
                rh_eh_continuations[destination] = continuation;
                rh_eh_copy_register_context(
                    &rh_eh_continuation_registers[destination],
                    &rh_eh_continuation_registers[source]);
            }
            destination = destination + 1ul;
        }
        source = source + 1ul;
    }
    rh_eh_continuation_count = destination;
}

static void rh_eh_refresh_current_exception(int preserve_active)
{
    if (rh_catch_context_count != 0ul)
        RhpCurrentException = rh_catch_contexts[rh_catch_context_count - 1ul].exception;
    else if (preserve_active)
        RhpCurrentException = rh_active_exception;
    else
        RhpCurrentException = (RhObject*)0;
}

static void rh_eh_prune_catches(usize frame_index, const void* target, int preserve_active)
{
    usize source = 0ul;
    usize destination = 0ul;
    while (source < rh_catch_context_count)
    {
        RhCatchContext context = rh_catch_contexts[source];
        int keep = context.frame_index < frame_index ||
            (context.frame_index == frame_index && rh_eh_handler_contains(context.method, context.clause_index, target));
        if (keep)
        {
            if (destination != source)
                rh_catch_contexts[destination] = context;
            destination = destination + 1ul;
        }
        source = source + 1ul;
    }
    rh_catch_context_count = destination;
    rh_eh_refresh_current_exception(preserve_active);
}

static void rh_eh_push_catch(RhObject* exception, usize frame_index, const RhEhMethodInfo* method, isize clause_index)
{
    RhCatchContext* context;
    if (rh_catch_context_count >= RH_EH_MAX_CATCH_CONTEXTS)
        RhpFallbackFailFast(150);
    context = &rh_catch_contexts[rh_catch_context_count];
    context->exception = exception;
    context->frame_index = frame_index;
    context->method = method;
    context->clause_index = clause_index;
    rh_catch_context_count = rh_catch_context_count + 1ul;
    RhpCurrentException = exception;
}

static void rh_eh_transfer(usize frame_index, const void* target)
{
    RhEhFrame* frame;
    if (frame_index >= RhpEhFrameCount || target == (const void*)0)
        RhpFallbackFailFast(150);
    rh_eh_prune_continuations(frame_index, target);
    RhpEhFrameCount = frame_index + 1ul;
    frame = &RhpEhFrames[frame_index];
    frame->current_ip = target;
    RhpEhTransfer(frame->frame_pointer, target, &RhpEhRegisterContexts[frame_index]);
    for (;;)
    {
    }
}

static void rh_eh_dispatch_from(
    RhObject* exception,
    usize frame_count,
    const void* source_ip,
    isize after_clause_index)
{
    while (frame_count != 0ul)
    {
        usize frame_index = frame_count - 1ul;
        RhEhFrame* frame = &RhpEhFrames[frame_index];
        const void* frame_source_ip = source_ip == (const void*)0 ? frame->current_ip : source_ip;
        isize clause_index = rh_eh_find_throw_handler(frame, exception, frame_source_ip, after_clause_index);
        if (clause_index >= 0)
        {
            const RhEhClause* clause = &frame->method->clauses[(usize)clause_index];
            rh_eh_prune_catches(frame_index, clause->handler_start, 1);
            if (clause->kind == RH_EH_FINALLY || clause->kind == RH_EH_FAULT)
            {
                rh_eh_push_continuation(
                    RH_EH_CONTINUATION_THROW,
                    (const void*)0,
                    frame_index,
                    frame_source_ip,
                    clause_index,
                    exception);
                rh_eh_transfer(frame_index, clause->handler_start);
            }
            rh_eh_push_catch(exception, frame_index, frame->method, clause_index);
            rh_eh_transfer(frame_index, clause->handler_start);
        }
        frame_count = frame_index;
        source_ip = (const void*)0;
        after_clause_index = -1;
    }
    RhpEhFrameCount = 0ul;
    rh_eh_continuation_count = 0ul;
    RhpCurrentException = exception;
    RhpFallbackFailFast(134);
}

static void rh_eh_dispatch(RhObject* exception)
{
    rh_eh_dispatch_from(exception, RhpEhFrameCount, (const void*)0, -1);
}

static void rh_eh_continue_leave(
    const void* target,
    const void* source_ip,
    isize after_clause_index)
{
    usize frame_index;
    RhEhFrame* frame;
    isize clause_index;
    if (RhpEhFrameCount == 0ul)
        RhpFallbackFailFast(150);
    frame_index = RhpEhFrameCount - 1ul;
    frame = &RhpEhFrames[frame_index];
    if (source_ip == (const void*)0)
        source_ip = frame->current_ip;
    clause_index = rh_eh_find_leave_finally(frame, source_ip, target, after_clause_index);
    if (clause_index >= 0)
    {
        const RhEhClause* clause = &frame->method->clauses[(usize)clause_index];
        rh_eh_prune_catches(frame_index, clause->handler_start, 0);
        rh_eh_push_continuation(
            RH_EH_CONTINUATION_LEAVE,
            target,
            frame_index,
            source_ip,
            clause_index,
            (RhObject*)0);
        rh_eh_transfer(frame_index, clause->handler_start);
    }
    rh_eh_prune_catches(frame_index, target, 0);
    rh_eh_transfer(frame_index, target);
}

void RhpThrowEx(RhObject* exception)
{
    if (exception == (RhObject*)0)
        RhpFallbackFailFast(134);
    rh_active_exception = exception;
    RhpCurrentException = exception;
    rh_eh_dispatch(exception);
}

void RhpRethrow(void)
{
    usize frame_index;
    isize i;
    RhObject* exception = (RhObject*)0;
    if (RhpEhFrameCount == 0ul)
        RhpFallbackFailFast(150);
    frame_index = RhpEhFrameCount - 1ul;
    i = (isize)rh_catch_context_count - 1;
    while (i >= 0)
    {
        RhCatchContext* context = &rh_catch_contexts[(usize)i];
        if (context->frame_index == frame_index &&
            rh_eh_handler_contains(context->method, context->clause_index, RhpEhFrames[frame_index].current_ip))
        {
            exception = context->exception;
            break;
        }
        i = i - 1;
    }
    if (exception == (RhObject*)0)
        RhpFallbackFailFast(150);
    rh_active_exception = exception;
    RhpCurrentException = exception;
    rh_eh_dispatch(exception);
}

void RhpLeave(const void* target, usize kind)
{
    if (kind == 0ul)
        RhpFallbackFailFast(150);
    rh_eh_continue_leave(target, (const void*)0, -1);
}

void RhpEndFinally(void)
{
    RhEhContinuation continuation;
    usize continuation_index;
    if (rh_eh_continuation_count == 0ul)
        RhpFallbackFailFast(150);
    rh_eh_continuation_count = rh_eh_continuation_count - 1ul;
    continuation_index = rh_eh_continuation_count;
    continuation = rh_eh_continuations[continuation_index];
    if (continuation.frame_index >= RhpEhFrameCount)
        RhpFallbackFailFast(150);
    rh_eh_copy_register_context(
        &RhpEhRegisterContexts[continuation.frame_index],
        &rh_eh_continuation_registers[continuation_index]);
    RhpEhFrames[continuation.frame_index].current_ip = continuation.source_ip;
    if (continuation.kind == RH_EH_CONTINUATION_THROW)
    {
        if (continuation.exception == (RhObject*)0)
            RhpFallbackFailFast(150);
        rh_active_exception = continuation.exception;
        RhpCurrentException = continuation.exception;
        rh_eh_dispatch_from(
            continuation.exception,
            continuation.frame_index + 1ul,
            continuation.source_ip,
            continuation.clause_index);
    }
    if (continuation.kind != RH_EH_CONTINUATION_LEAVE)
        RhpFallbackFailFast(150);
    rh_eh_continue_leave(
        continuation.target,
        continuation.source_ip,
        continuation.clause_index);
}

static const RhTypeInfo* rh_find_type_info(const RhMethodTable* type)
{
    usize i = 0ul;
    while (i < rh_type_info_count)
    {
        if (rh_type_infos[i].type == type)
            return &rh_type_infos[i];
        i = i + 1ul;
    }
    return (const RhTypeInfo*)0;
}

static const RhTypeInfo* rh_require_method_table(const RhMethodTable* type, int code)
{
    const RhTypeInfo* info;
    usize component_size;
    if (type == (const RhMethodTable*)0)
        RhpFallbackFailFast(code);
    info = rh_find_type_info(type);
    if (info == (const RhTypeInfo*)0 ||
        info->runtime_kind > RH_TYPE_PARAMETERIZED ||
        (info->gc_field_count != 0ul && info->gc_fields == (const RhGcField*)0) ||
        (info->component_gc_field_count != 0ul && info->component_gc_fields == (const RhGcField*)0) ||
        (((type->flags & RH_EETYPE_HAS_POINTERS) != 0u) !=
            (info->gc_field_count != 0ul || info->component_gc_field_count != 0ul)))
    {
        RhpFallbackFailFast(code);
    }

    component_size = rh_component_size(type);
    if (info->runtime_kind == RH_TYPE_FIXED)
    {
        if (rh_has_component_size(type) ||
            rh_method_table_kind(type) != 0ul ||
            type->base_size < RH_MINIMUM_GC_OBJECT_SIZE ||
            info->component_gc_field_count != 0ul)
        {
            RhpFallbackFailFast(code);
        }
        return info;
    }

    if (info->runtime_kind == RH_TYPE_PARAMETERIZED)
    {
        usize element_type = rh_element_type(type);
        if (rh_has_component_size(type) ||
            rh_method_table_kind(type) != RH_EETYPE_PARAMETERIZED_KIND ||
            type->related_type == (const void*)0 ||
            (element_type == RH_EETYPE_ELEMENT_TYPE_POINTER && type->base_size != 0u) ||
            (element_type == RH_EETYPE_ELEMENT_TYPE_BYREF && type->base_size != 1u) ||
            (element_type != RH_EETYPE_ELEMENT_TYPE_POINTER && element_type != RH_EETYPE_ELEMENT_TYPE_BYREF) ||
            info->gc_field_count != 0ul ||
            info->component_gc_field_count != 0ul)
        {
            RhpFallbackFailFast(code);
        }
        return info;
    }

    if (!rh_has_component_size(type) || component_size == 0ul)
        RhpFallbackFailFast(code);

    if (info->runtime_kind == RH_TYPE_STRING)
    {
        if (component_size != 2ul ||
            rh_method_table_kind(type) != 0ul ||
            rh_element_type(type) != RH_EETYPE_ELEMENT_TYPE_CLASS ||
            type->base_size < SYNC_BLOCK_SIZE + STRING_FIRST_CHAR_OFFSET + 2ul ||
            info->component_gc_field_count != 0ul)
        {
            RhpFallbackFailFast(code);
        }
        return info;
    }

    if (rh_method_table_kind(type) != RH_EETYPE_PARAMETERIZED_KIND ||
        type->related_type == (const void*)0 ||
        type->base_size < SYNC_BLOCK_SIZE + ARRAY_DATA_OFFSET)
    {
        RhpFallbackFailFast(code);
    }

    if (info->runtime_kind == RH_TYPE_SZARRAY)
    {
        if (rh_element_type(type) != RH_EETYPE_ELEMENT_TYPE_SZARRAY ||
            type->base_size != SYNC_BLOCK_SIZE + ARRAY_DATA_OFFSET)
        {
            RhpFallbackFailFast(code);
        }
        return info;
    }

    if (rh_element_type(type) != RH_EETYPE_ELEMENT_TYPE_ARRAY ||
        type->base_size <= SYNC_BLOCK_SIZE + ARRAY_DATA_OFFSET ||
        ((type->base_size - (SYNC_BLOCK_SIZE + ARRAY_DATA_OFFSET)) & 7u) != 0u)
    {
        RhpFallbackFailFast(code);
    }
    return info;
}

static usize rh_variable_gc_size(const RhMethodTable* type, int length)
{
    usize maximum = (usize)-1;
    usize count;
    usize component_size;
    usize base_size;
    if (length < 0)
        return 0ul;
    count = (usize)length;
    component_size = rh_component_size(type);
    base_size = (usize)type->base_size;
    if (component_size == 0ul || count > (maximum - base_size) / component_size)
        return 0ul;
    return base_size + count * component_size;
}

static usize rh_minimum_block_size(void)
{
    usize object_block = rh_total_block_size(RH_MINIMUM_GC_OBJECT_SIZE);
    usize free_block = rh_align_up(RH_BLOCK_HEADER_SIZE + sizeof(RhBlock*), RH_HEAP_ALIGNMENT);
    return object_block > free_block ? object_block : free_block;
}

static void rh_zero(void* address, usize size)
{
    u8* bytes = (u8*)address;
    usize word_size = (usize)__SIZEOF_POINTER__;
    usize block_size = word_size * 8ul;

#ifdef __riscv_vector
    if (size >= 64ul)
    {
        __asm__ volatile(
            "vsetvli a2, zero, e8, m8, ta, ma\n"
            "vxor.vv v8, v8, v8\n"
            ".Lrh_zero_loop_%=:\n"
            "vsetvli a2, %[count], e8, m8, ta, ma\n"
            "vse8.v v8, (%[destination])\n"
            "add %[destination], %[destination], a2\n"
            "sub %[count], %[count], a2\n"
            "bne %[count], zero, .Lrh_zero_loop_%="
            :
        : [destination] "{a0}"(bytes), [count] "{a1}"(size)
            : "memory");
        return;
    }
#endif

    while (size != 0ul && ((usize)bytes & (word_size - 1ul)) != 0ul)
    {
        *bytes = 0u;
        bytes = bytes + 1;
        size = size - 1ul;
    }

    if (size >= word_size)
    {
        usize* words = (usize*)bytes;

        while (size >= block_size)
        {
            words[0] = 0ul;
            words[1] = 0ul;
            words[2] = 0ul;
            words[3] = 0ul;
            words[4] = 0ul;
            words[5] = 0ul;
            words[6] = 0ul;
            words[7] = 0ul;
            words = words + 8;
            size = size - block_size;
        }

        while (size >= word_size)
        {
            *words = 0ul;
            words = words + 1;
            size = size - word_size;
        }

        bytes = (u8*)words;
    }

    while (size != 0ul)
    {
        *bytes = 0u;
        bytes = bytes + 1;
        size = size - 1ul;
    }
}

static void rh_memmove(void* destination, const void* source, usize size)
{
    u8* destination_bytes = (u8*)destination;
    const u8* source_bytes = (const u8*)source;
    if (destination_bytes == source_bytes || size == 0ul)
        return;
    if ((usize)destination_bytes < (usize)source_bytes ||
        (usize)destination_bytes >= (usize)source_bytes + size)
    {
        usize i = 0ul;
        while (i < size)
        {
            destination_bytes[i] = source_bytes[i];
            i = i + 1ul;
        }
        return;
    }
    while (size != 0ul)
    {
        size = size - 1ul;
        destination_bytes[size] = source_bytes[size];
    }
}

static void rh_write_all(const u8* data, usize length)
{
    usize offset = 0ul;
#if defined(__linux__)
    while (offset < length)
    {
        isize result = rh_syscall3(RH_SYS_WRITE, 1ul, (usize)(data + offset), length - offset);
        if (result <= 0l)
            return;
        offset = offset + (usize)result;
    }
#elif defined(_WIN32)
    void* handle = rh_windows_stdout();
    while (offset < length)
    {
        usize remaining = length - offset;
        u32 chunk = remaining > 0xfffffffful ? 0xffffffffu : (u32)remaining;
        usize written = rh_windows_write(handle, data + offset, chunk);
        if (written == 0ul)
            return;
        offset = offset + written;
    }
#endif
}

static usize rh_encode_utf8(u32 scalar, u8* buffer)
{
    if (scalar <= 0x7fu)
    {
        buffer[0] = (u8)scalar;
        return 1ul;
    }
    if (scalar <= 0x7ffu)
    {
        buffer[0] = (u8)(0xc0u | (scalar >> 6));
        buffer[1] = (u8)(0x80u | (scalar & 0x3fu));
        return 2ul;
    }
    if (scalar <= 0xffffu)
    {
        buffer[0] = (u8)(0xe0u | (scalar >> 12));
        buffer[1] = (u8)(0x80u | ((scalar >> 6) & 0x3fu));
        buffer[2] = (u8)(0x80u | (scalar & 0x3fu));
        return 3ul;
    }
    buffer[0] = (u8)(0xf0u | (scalar >> 18));
    buffer[1] = (u8)(0x80u | ((scalar >> 12) & 0x3fu));
    buffer[2] = (u8)(0x80u | ((scalar >> 6) & 0x3fu));
    buffer[3] = (u8)(0x80u | (scalar & 0x3fu));
    return 4ul;
}

void RhpFallbackFailFast(int code)
{
#if defined(__linux__)
    rh_syscall1(RH_SYS_EXIT, (usize)code);
#elif defined(_WIN32)
    rh_windows_exit((u32)code);
#endif
    for (;;)
    {
    }
}

static void* rh_os_reserve(usize size)
{
#if defined(__linux__)
    isize result = rh_syscall6(
        RH_SYS_MMAP,
        0ul,
        size,
        RH_PROT_NONE,
        RH_MAP_PRIVATE | RH_MAP_ANONYMOUS,
        (usize)-1,
        0ul);
    if (result < 0l)
        return (void*)0;
    return (void*)(usize)result;
#elif defined(_WIN32)
    return rh_windows_virtual_alloc((void*)0, size, RH_WIN_MEM_RESERVE, RH_WIN_PAGE_NOACCESS);
#endif
}

static int rh_os_commit(void* address, usize size)
{
#if defined(__linux__)
    return rh_syscall3(
        RH_SYS_MPROTECT,
        (usize)address,
        size,
        RH_PROT_READ | RH_PROT_WRITE) == 0l;
#elif defined(_WIN32)
    return rh_windows_virtual_alloc(address, size, RH_WIN_MEM_COMMIT, RH_WIN_PAGE_READWRITE) == address;
#endif
}

static int rh_os_decommit(void* address, usize size)
{
    if (size == 0ul)
        return 1;
#if defined(__linux__)
    {
        isize result = rh_syscall6(
            RH_SYS_MMAP,
            (usize)address,
            size,
            RH_PROT_NONE,
            RH_MAP_PRIVATE | RH_MAP_ANONYMOUS | RH_MAP_FIXED,
            (usize)-1,
            0ul);
        return result == (isize)(usize)address;
    }
#elif defined(_WIN32)
    return rh_windows_virtual_free(address, size, RH_WIN_MEM_DECOMMIT);
#endif
}

static int rh_os_release(void* address, usize size)
{
#if defined(__linux__)
    return rh_syscall2(RH_SYS_MUNMAP, (usize)address, size) == 0l;
#elif defined(_WIN32)
    (void)size;
    return rh_windows_virtual_free(address, 0ul, RH_WIN_MEM_RELEASE);
#endif
}

void* RhpAllocHGlobal(usize size)
{
    usize maximum = (usize)-1;
    usize payload_size = size == 0ul ? 1ul : size;
    usize total_size;
    usize mapping_size;
    u8* base;

    if (payload_size > maximum - RH_HGLOBAL_HEADER_SIZE)
        RhpFallbackFailFast(141);
    total_size = RH_HGLOBAL_HEADER_SIZE + payload_size;
    if (total_size > maximum - (RH_PAGE_SIZE - 1ul))
        RhpFallbackFailFast(141);
    mapping_size = rh_align_up(total_size, RH_PAGE_SIZE);
    base = (u8*)rh_os_reserve(mapping_size);
    if (base == (u8*)0)
        RhpFallbackFailFast(141);
    if (!rh_os_commit(base, mapping_size))
    {
        rh_os_release(base, mapping_size);
        RhpFallbackFailFast(141);
    }

    *(usize*)base = mapping_size;
    return (void*)(base + RH_HGLOBAL_HEADER_SIZE);
}

void RhpFreeHGlobal(void* pointer)
{
    u8* base;
    usize mapping_size;

    if (pointer == (void*)0)
        return;

    base = (u8*)pointer - RH_HGLOBAL_HEADER_SIZE;
    mapping_size = *(usize*)base;
    if (!rh_os_release(base, mapping_size))
        RhpFallbackFailFast(153);
}

void RhpMemset(void* destination, int value, usize length)
{
#if defined(__x86_64__)
    usize destination_arg = (usize)destination;
    usize length_arg = length;
    usize value_arg = (usize)(u8)value;
    __asm__ volatile(
        ".byte 0xf3, 0xaa"
        : [destination] "+{rdi}"(destination_arg), [length] "+{rcx}"(length_arg)
        : [value] "{rax}"(value_arg)
        : "memory");
    return;
#elif defined(__i386__)
    usize destination_arg = (usize)destination;
    usize length_arg = length;
    usize value_arg = (usize)(u8)value;
    __asm__ volatile(
        ".byte 0xf3, 0xaa"
        : [destination] "+{edi}"(destination_arg), [length] "+{ecx}"(length_arg)
        : [value] "{eax}"(value_arg)
        : "memory");
    return;
#else
    u8* bytes = (u8*)destination;
    u8 fill = (u8)value;
#if defined(__riscv_vector)
    while (length != 0ul)
    {
        usize vector_length;
        __asm__ volatile(
            "vsetvli %[vector_length], %[length], e8, m1, ta, ma"
            : [vector_length] "=r"(vector_length)
            : [length] "r"(length));
        __asm__ volatile(
            "vxor.vv v0, v0, v0\n"
            "vadd.vx v0, v0, %[value]\n"
            "vse8.v v0, 0(%[destination])"
            :
        : [destination] "r"(bytes), [value] "r"((usize)fill)
            : "v0", "memory");
        bytes = bytes + vector_length;
        length = length - vector_length;
    }
#else
    usize word = (usize)fill;
    word = word | (word << 8);
    word = word | (word << 16);
#if __SIZEOF_POINTER__ == 8
    word = word | (word << 32);
#endif

    while (length != 0ul && (((usize)bytes) & (__SIZEOF_POINTER__ - 1ul)) != 0ul)
    {
        *bytes = fill;
        bytes = bytes + 1ul;
        length = length - 1ul;
    }

    usize* words = (usize*)bytes;
    while (length >= (__SIZEOF_POINTER__ * 4ul))
    {
        words[0] = word;
        words[1] = word;
        words[2] = word;
        words[3] = word;
        words = words + 4ul;
        length = length - (__SIZEOF_POINTER__ * 4ul);
    }

    while (length >= __SIZEOF_POINTER__)
    {
        *words = word;
        words = words + 1ul;
        length = length - __SIZEOF_POINTER__;
    }

    bytes = (u8*)words;
    while (length != 0ul)
    {
        *bytes = fill;
        bytes = bytes + 1ul;
        length = length - 1ul;
    }
#endif
#endif
}

int RhpGetCurrentProcessorNumber(void)
{
#if defined(__linux__)
    u32 cpu = 0u;
    isize result = rh_syscall3(RH_SYS_GETCPU, (usize) & cpu, 0ul, 0ul);
    if (result != 0l)
        return 0;
    return (int)cpu;
#elif defined(_WIN32) && defined(__x86_64__)
    usize result;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_GetCurrentProcessorNumber]\n"
        "add rsp, 32"
        : "={rax}"(result)
        :
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return (int)(u32)result;
#elif defined(_WIN32) && defined(__i386__)
    usize result;
    __asm__ volatile(
        "call dword ptr[__imp_GetCurrentProcessorNumber]"
        : "={eax}"(result)
        :
        : "ecx", "edx", "memory");
    return (int)(u32)result;
#else
    return 0;
#endif
}

static usize rh_current_thread_id(void)
{
#if defined(__linux__)
    return (usize)rh_syscall1(RH_SYS_GETTID, 0ul);
#elif defined(_WIN32) && defined(__x86_64__)
    usize result;
    __asm__ volatile(
        "sub rsp, 32\n"
        "call qword ptr[rip + __imp_GetCurrentThreadId]\n"
        "add rsp, 32"
        : "={rax}"(result)
        :
        : "rcx", "rdx", "r8", "r9", "r10", "r11", "memory");
    return result;
#elif defined(_WIN32) && defined(__i386__)
    usize result;
    __asm__ volatile(
        "call dword ptr[__imp_GetCurrentThreadId]"
        : "={eax}"(result)
        :
        : "ecx", "edx", "memory");
    return result;
#endif
}

static usize rh_atomic_compare_exchange(volatile usize* address, usize comparand, usize value)
{
#if defined(__riscv_zacas)
    usize original = comparand;
#if __SIZEOF_POINTER__ == 8
    __asm__ volatile(
        "amocas.d.aqrl %[original], %[value], (%[address])"
        : [original] "+&r"(original)
        : [address] "r"(address), [value] "r"(value)
        : "memory");
#else
    __asm__ volatile(
        "amocas.w.aqrl %[original], %[value], (%[address])"
        : [original] "+&r"(original)
        : [address] "r"(address), [value] "r"(value)
        : "memory");
#endif
    return original;
#elif defined(__riscv_zalrsc)
    usize original;
    usize status;
#if __SIZEOF_POINTER__ == 8
    __asm__ volatile(
        ".Lrh_cas_retry_%=:\n"
        "lr.d.aq %[original], (%[address])\n"
        "bne %[original], %[comparand], .Lrh_cas_done_%=\n"
        "sc.d.rl %[status], %[value], (%[address])\n"
        "bne %[status], zero, .Lrh_cas_retry_%=\n"
        ".Lrh_cas_done_%=:"
        : [original] "=&r"(original), [status] "=&r"(status)
        : [address] "r"(address), [comparand] "r"(comparand), [value] "r"(value)
        : "memory");
#else
    __asm__ volatile(
        ".Lrh_cas_retry_%=:\n"
        "lr.w.aq %[original], (%[address])\n"
        "bne %[original], %[comparand], .Lrh_cas_done_%=\n"
        "sc.w.rl %[status], %[value], (%[address])\n"
        "bne %[status], zero, .Lrh_cas_retry_%=\n"
        ".Lrh_cas_done_%=:"
        : [original] "=&r"(original), [status] "=&r"(status)
        : [address] "r"(address), [comparand] "r"(comparand), [value] "r"(value)
        : "memory");
#endif
    return original;
#elif defined(__riscv)
    (void)address;
    (void)comparand;
    (void)value;
    RhpFallbackFailFast(154);
    return 0ul;
#elif defined(__x86_64__)
    usize original = comparand;
    __asm__ volatile(
        ".byte 0xf0\n"
        "cmpxchg qword ptr[%[address]], %[value]"
        : [original] "+&{rax}"(original)
        : [address] "r"(address), [value] "r"(value)
        : "cc", "memory");
    return original;
#elif defined(__i386__)
    usize original = comparand;
    __asm__ volatile(
        ".byte 0xf0\n"
        "cmpxchg dword ptr[%[address]], %[value]"
        : [original] "+&{eax}"(original)
        : [address] "r"(address), [value] "r"(value)
        : "cc", "memory");
    return original;
#endif
}

static void rh_cpu_relax(void)
{
#if defined(__x86_64__) || defined(__i386__) || defined(__riscv)
    __asm__ volatile("nop" : : : "memory");
#endif
}

typedef struct RhMonitor
{
    volatile usize gate;
    volatile usize owner;
    usize recursion;
} RhMonitor;

static RhMonitor* rh_monitor_for_object(void* object)
{
    volatile usize* slot = (volatile usize*)((u8*)object - SYNC_BLOCK_SIZE);
    usize current = *slot;
    RhMonitor* candidate;
    usize observed;

    if (current != 0ul)
        return (RhMonitor*)current;

    candidate = (RhMonitor*)RhpAllocHGlobal(sizeof(RhMonitor));
    candidate->gate = 0ul;
    candidate->owner = 0ul;
    candidate->recursion = 0ul;
    observed = rh_atomic_compare_exchange(slot, 0ul, (usize)candidate);
    if (observed == 0ul)
        return candidate;

    RhpFreeHGlobal(candidate);
    return (RhMonitor*)observed;
}

void RhpMonitorEnter(void* object)
{
    RhMonitor* monitor;
    usize thread_id;

    if (object == (void*)0)
        RhpFallbackFailFast(154);

    monitor = rh_monitor_for_object(object);
    thread_id = rh_current_thread_id();
    if (thread_id == 0ul)
        RhpFallbackFailFast(154);

    if (monitor->owner == thread_id)
    {
        if (monitor->recursion == (usize)-1)
            RhpFallbackFailFast(154);
        monitor->recursion = monitor->recursion + 1ul;
        return;
    }

    while (rh_atomic_compare_exchange(&monitor->gate, 0ul, 1ul) != 0ul)
        rh_cpu_relax();

    monitor->owner = thread_id;
    monitor->recursion = 1ul;
}

void RhpMonitorExit(void* object)
{
    volatile usize* slot;
    RhMonitor* monitor;
    usize thread_id;

    if (object == (void*)0)
        RhpFallbackFailFast(154);

    slot = (volatile usize*)((u8*)object - SYNC_BLOCK_SIZE);
    monitor = (RhMonitor*)(*slot);
    if (monitor == (RhMonitor*)0)
        RhpFallbackFailFast(154);

    thread_id = rh_current_thread_id();
    if (thread_id == 0ul || monitor->owner != thread_id || monitor->recursion == 0ul)
        RhpFallbackFailFast(154);

    if (monitor->recursion > 1ul)
    {
        monitor->recursion = monitor->recursion - 1ul;
        return;
    }

    monitor->recursion = 0ul;
    monitor->owner = 0ul;
    if (rh_atomic_compare_exchange(&monitor->gate, 1ul, 0ul) != 1ul)
        RhpFallbackFailFast(154);
}

static void rh_monitor_destroy_for_object(void* object)
{
    volatile usize* slot = (volatile usize*)((u8*)object - SYNC_BLOCK_SIZE);
    RhMonitor* monitor = (RhMonitor*)(*slot);
    if (monitor == (RhMonitor*)0)
        return;
    *slot = 0ul;
    RhpFreeHGlobal(monitor);
}

static int rh_ensure_committed(u8* required)
{
    usize required_offset;
    usize committed_offset;
    usize target_offset;
    if (required <= rh_heap_committed)
        return 1;
    if (required > rh_heap_limit)
        return 0;
    required_offset = (usize)(required - rh_heap_base);
    committed_offset = (usize)(rh_heap_committed - rh_heap_base);
    target_offset = rh_align_up(required_offset, RH_HEAP_COMMIT_GRANULARITY);
    if (target_offset > RH_HEAP_RESERVE)
        target_offset = RH_HEAP_RESERVE;
    if (!rh_os_commit(rh_heap_base + committed_offset, target_offset - committed_offset))
        return 0;
    rh_heap_committed = rh_heap_base + target_offset;
    return 1;
}

static void rh_decommit_unused_tail(void)
{
    usize used_offset = (usize)(rh_heap_used - rh_heap_base);
    usize committed_offset = (usize)(rh_heap_committed - rh_heap_base);
    usize keep_offset = rh_align_up(used_offset, RH_PAGE_SIZE);
    if (keep_offset >= committed_offset)
        return;
    if (!rh_os_decommit(rh_heap_base + keep_offset, committed_offset - keep_offset))
        RhpFallbackFailFast(137);
    rh_heap_committed = rh_heap_base + keep_offset;
}

static void rh_initialize_heap(void)
{
    u8* base = (u8*)rh_os_reserve(RH_HEAP_RESERVE);
    if (base == (u8*)0 || (((usize)base) & (RH_HEAP_ALIGNMENT - 1ul)) != 0ul)
        RhpFallbackFailFast(137);
    if (!rh_os_commit(base, RH_HEAP_INITIAL_COMMIT))
    {
        rh_os_release(base, RH_HEAP_RESERVE);
        RhpFallbackFailFast(137);
    }
    rh_heap_base = base;
    rh_heap_used = base;
    rh_heap_committed = base + RH_HEAP_INITIAL_COMMIT;
    rh_heap_limit = base + RH_HEAP_RESERVE;
    rh_mark_stack = (RhBlock*)0;
    rh_free_list = (RhBlock*)0;
    rh_allocation_debt = 0ul;
    RhpGcPollRequested = 0u;
}

void RhpInitialize(
    void* stack_base,
    const RhSafePoint* safe_points,
    usize safe_point_count,
    const RhTypeInfo* type_infos,
    usize type_info_count,
    const RhStaticRoot* static_roots,
    usize static_root_count)
{
    if (sizeof(usize) != __SIZEOF_POINTER__ ||
        RH_PAGE_SIZE == 0ul ||
        (RH_PAGE_SIZE & (RH_PAGE_SIZE - 1ul)) != 0ul ||
        RH_HEAP_COMMIT_GRANULARITY < RH_PAGE_SIZE ||
        (RH_HEAP_COMMIT_GRANULARITY & (RH_HEAP_COMMIT_GRANULARITY - 1ul)) != 0ul ||
        RH_HEAP_INITIAL_COMMIT == 0ul ||
        RH_HEAP_INITIAL_COMMIT > RH_HEAP_RESERVE ||
        (RH_HEAP_INITIAL_COMMIT & (RH_PAGE_SIZE - 1ul)) != 0ul ||
        (RH_HEAP_RESERVE & (RH_PAGE_SIZE - 1ul)) != 0ul ||
        sizeof(int) != 4ul ||
        sizeof(u16) != 2ul ||
        SYNC_BLOCK_SIZE != __SIZEOF_POINTER__ ||
        MANAGED_OBJECT_HEADER_SIZE < __SIZEOF_POINTER__ ||
        (MANAGED_OBJECT_HEADER_SIZE & (__SIZEOF_POINTER__ - 1ul)) != 0ul ||
        MINIMUM_GC_OBJECT_SIZE < SYNC_BLOCK_SIZE + MANAGED_OBJECT_HEADER_SIZE ||
        STRING_LENGTH_OFFSET != MANAGED_OBJECT_HEADER_SIZE ||
        STRING_FIRST_CHAR_OFFSET != STRING_LENGTH_OFFSET + 4ul ||
        ARRAY_LENGTH_OFFSET != MANAGED_OBJECT_HEADER_SIZE ||
        ARRAY_DATA_OFFSET < ARRAY_LENGTH_OFFSET + 4ul ||
        (ARRAY_DATA_OFFSET & (__SIZEOF_POINTER__ - 1ul)) != 0ul ||
        sizeof(RhBlock) != RH_BLOCK_HEADER_SIZE ||
        sizeof(RhObject) != __SIZEOF_POINTER__ ||
        sizeof(RhGcField) != __SIZEOF_POINTER__ * 2ul ||
        sizeof(RhMethodTable) != 16ul + __SIZEOF_POINTER__ ||
        sizeof(RhTypeInfo) != __SIZEOF_POINTER__ * 6ul ||
        sizeof(RhRoot) != __SIZEOF_POINTER__ * 2ul ||
        sizeof(RhSafePoint) != __SIZEOF_POINTER__ * 5ul ||
        sizeof(RhStaticRoot) != __SIZEOF_POINTER__ * 2ul ||
        sizeof(RhEhClause) != __SIZEOF_POINTER__ * 12ul ||
        sizeof(RhEhMethodInfo) != __SIZEOF_POINTER__ * 2ul ||
        sizeof(RhEhFrame) != __SIZEOF_POINTER__ * 3ul ||
        sizeof(RhEhRegisterContext) != 512ul ||
        sizeof(RhEhContinuation) != __SIZEOF_POINTER__ * 6ul ||
        sizeof(RhCatchContext) != __SIZEOF_POINTER__ * 4ul ||
        stack_base == (void*)0 ||
        (safe_point_count != 0ul && safe_points == (const RhSafePoint*)0) ||
        (type_info_count != 0ul && type_infos == (const RhTypeInfo*)0) ||
        (static_root_count != 0ul && static_roots == (const RhStaticRoot*)0))
    {
        RhpFallbackFailFast(144);
    }
    rh_stack_base = (usize)stack_base;
    rh_safe_points = safe_points;
    rh_safe_point_count = safe_point_count;
    rh_type_infos = type_infos;
    rh_type_info_count = type_info_count;
    rh_static_roots = static_roots;
    rh_static_root_count = static_root_count;
    RhpCurrentSafePoint = (const RhSafePoint*)0;
    RhpCurrentFramePointer = (void*)0;
    RhpEhFrameCount = 0ul;
    RhpCurrentException = (RhObject*)0;
    rh_active_exception = (RhObject*)0;
    rh_eh_continuation_count = 0ul;
    rh_catch_context_count = 0ul;
    rh_initialize_heap();
}

static void rh_validate_block(u8* address, const RhBlock* block, int code)
{
    usize remaining;
    if (address < rh_heap_base || address >= rh_heap_used)
        RhpFallbackFailFast(code);
    remaining = (usize)(rh_heap_used - address);
    if ((((usize)address - (usize)rh_heap_base) & (RH_HEAP_ALIGNMENT - 1ul)) != 0ul ||
        block->size < rh_minimum_block_size() ||
        (block->size & (RH_HEAP_ALIGNMENT - 1ul)) != 0ul ||
        block->size > remaining ||
        (block->kind != RH_BLOCK_FREE && block->kind != RH_BLOCK_OBJECT))
    {
        RhpFallbackFailFast(code);
    }
}

static RhBlock* rh_free_next(RhBlock* block)
{
    return *(RhBlock**)((u8*)block + RH_BLOCK_HEADER_SIZE);
}

static void rh_set_free_next(RhBlock* block, RhBlock* next)
{
    *(RhBlock**)((u8*)block + RH_BLOCK_HEADER_SIZE) = next;
}

static RhObject* rh_object_from_block(RhBlock* block)
{
    return (RhObject*)((u8*)block + RH_BLOCK_HEADER_SIZE + SYNC_BLOCK_SIZE);
}

static usize rh_gc_object_size(const RhObject* object, const RhBlock* block, int code)
{
    const RhMethodTable* type = object->type;
    const RhTypeInfo* info;
    usize gc_size;
    info = rh_require_method_table(type, code);

    if (info->runtime_kind == RH_TYPE_FIXED)
    {
        gc_size = (usize)type->base_size;
    }
    else
    {
        int length;
        usize length_offset;
        if (info->runtime_kind == RH_TYPE_MDARRAY || info->runtime_kind == RH_TYPE_PARAMETERIZED)
            RhpFallbackFailFast(code);
        length_offset = info->runtime_kind == RH_TYPE_STRING
            ? STRING_LENGTH_OFFSET
            : ARRAY_LENGTH_OFFSET;
        if (block->size < RH_BLOCK_HEADER_SIZE + SYNC_BLOCK_SIZE + length_offset + 4ul)
            RhpFallbackFailFast(code);
        length = *(const int*)((const u8*)object + length_offset);
        gc_size = rh_variable_gc_size(type, length);
        if (gc_size == 0ul)
            RhpFallbackFailFast(code);
    }

    if (gc_size < SYNC_BLOCK_SIZE + MANAGED_OBJECT_HEADER_SIZE ||
        rh_total_block_size(gc_size) == 0ul ||
        rh_total_block_size(gc_size) > block->size)
    {
        RhpFallbackFailFast(code);
    }
    return gc_size;
}

static RhBlock* rh_block_for_exact_object(void* object)
{
    u8* p;
    u8* block_address;
    RhBlock* block;
    RhObject* value;
    if (object == (void*)0)
        return (RhBlock*)0;
    p = (u8*)object;
    if (p < rh_heap_base + RH_BLOCK_HEADER_SIZE + SYNC_BLOCK_SIZE || p >= rh_heap_used)
        return (RhBlock*)0;
    block_address = p - RH_BLOCK_HEADER_SIZE - SYNC_BLOCK_SIZE;
    if ((((usize)block_address - (usize)rh_heap_base) & (RH_HEAP_ALIGNMENT - 1ul)) != 0ul)
        return (RhBlock*)0;
    block = (RhBlock*)block_address;
    if (block->kind != RH_BLOCK_OBJECT || block->size > (usize)(rh_heap_used - block_address))
        return (RhBlock*)0;
    value = rh_object_from_block(block);
    if ((void*)value != object)
        return (RhBlock*)0;
    rh_gc_object_size(value, block, 138);
    return block;
}

static int rh_mark_object(void* object)
{
    RhBlock* block = rh_block_for_exact_object(object);
    if (block == (RhBlock*)0)
        return 0;
    if ((block->flags & RH_BLOCK_MARK) != 0ul)
        return 0;
    block->flags = RH_BLOCK_MARK;
    block->mark_next = rh_mark_stack;
    rh_mark_stack = block;
    return 1;
}

static int rh_mark_interior(void* interior)
{
    u8* target;
    u8* scan;
    if (interior == (void*)0)
        return 0;

    target = (u8*)interior;
    if (target < rh_heap_base + RH_BLOCK_HEADER_SIZE + SYNC_BLOCK_SIZE || target >= rh_heap_used)
        return 0;

    scan = rh_heap_base;
    while (scan < rh_heap_used)
    {
        RhBlock* block = (RhBlock*)scan;
        rh_validate_block(scan, block, 138);
        if (block->kind == RH_BLOCK_OBJECT)
        {
            RhObject* object = rh_object_from_block(block);
            usize gc_size = rh_gc_object_size(object, block, 138);
            u8* object_start = (u8*)object;
            u8* object_end = (u8*)block + RH_BLOCK_HEADER_SIZE + gc_size;
            if (target >= object_start && target < object_end)
                return rh_mark_object((void*)object_start);
            if (target < object_start)
                return 0;
        }
        scan = scan + block->size;
    }
    return 0;
}

static const RhSafePoint* rh_find_safe_point(const void* return_address)
{
    usize low = 0ul;
    usize high = rh_safe_point_count;
    usize key = (usize)return_address;
    while (low < high)
    {
        usize middle = low + (high - low) / 2ul;
        usize candidate = (usize)rh_safe_points[middle].return_address;
        if (candidate < key)
            low = middle + 1ul;
        else
            high = middle;
    }
    if (low < rh_safe_point_count && rh_safe_points[low].return_address == return_address)
        return &rh_safe_points[low];
    return (const RhSafePoint*)0;
}

static void rh_mark_frame_roots(const RhSafePoint* safe_point, usize frame_pointer)
{
    const RhSafePoint* current = safe_point;
    usize fp = frame_pointer;
    usize depth = 0ul;
    if (current == (const RhSafePoint*)0 || fp == 0ul || fp > rh_stack_base)
        RhpFallbackFailFast(144);

    while (depth < 4096ul)
    {
        usize i = 0ul;
        if (current->root_count != 0ul && current->roots == (const RhRoot*)0)
            RhpFallbackFailFast(144);
        while (i < current->root_count)
        {
            const RhRoot* root = &current->roots[i];
            void* value = *(void**)((u8*)fp + root->frame_offset);
            if (root->kind == RH_ROOT_INTERIOR)
                rh_mark_interior(value);
            else if (root->kind == RH_ROOT_OBJECT)
                rh_mark_object(value);
            else
                RhpFallbackFailFast(144);
            i = i + 1ul;
        }

        {
            usize caller_fp = *(usize*)((u8*)fp + current->saved_frame_pointer_offset);
            const void* caller_ra;
            if (caller_fp == 0ul)
                return;
            if (caller_fp <= fp || caller_fp > rh_stack_base)
                RhpFallbackFailFast(142);
            caller_ra = *(const void**)((u8*)fp + current->saved_return_address_offset);
            current = rh_find_safe_point(caller_ra);
            if (current == (const RhSafePoint*)0)
                RhpFallbackFailFast(143);
            fp = caller_fp;
        }
        depth = depth + 1ul;
    }
    RhpFallbackFailFast(144);
}

static void rh_mark_field_value(void* value, usize kind)
{
    if (kind == RH_ROOT_INTERIOR)
        rh_mark_interior(value);
    else if (kind == RH_ROOT_OBJECT)
        rh_mark_object(value);
    else
        RhpFallbackFailFast(139);
}

static void rh_mark_eh_register_context_roots(const RhEhRegisterContext* context)
{
    usize offset = 0ul;
    while (offset < 256ul)
    {
        void* value = *(void* const*)(context->data + offset);
        rh_mark_interior(value);
        offset = offset + 8ul;
    }
}

static void rh_mark_static_roots(void)
{
    usize i = 0ul;
    if (rh_static_root_count != 0ul && rh_static_roots == (const RhStaticRoot*)0)
        RhpFallbackFailFast(144);
    while (i < rh_static_root_count)
    {
        const RhStaticRoot* root = &rh_static_roots[i];
        if (root->address == (void*)0)
            RhpFallbackFailFast(144);
        rh_mark_field_value(*(void**)root->address, root->kind);
        i = i + 1ul;
    }
}

static void rh_drain_mark_stack(void)
{
    while (rh_mark_stack != (RhBlock*)0)
    {
        RhBlock* block = rh_mark_stack;
        RhObject* object;
        const RhMethodTable* type;
        const RhTypeInfo* info;
        usize gc_size;
        usize object_size;
        usize i = 0ul;
        rh_mark_stack = block->mark_next;
        block->mark_next = (RhBlock*)0;
        block->flags = RH_BLOCK_MARK | RH_BLOCK_SCANNED;
        if (block->kind != RH_BLOCK_OBJECT)
            RhpFallbackFailFast(139);

        object = rh_object_from_block(block);
        type = object->type;
        info = rh_require_method_table(type, 139);
        gc_size = rh_gc_object_size(object, block, 139);
        object_size = gc_size - SYNC_BLOCK_SIZE;

        while (i < info->gc_field_count)
        {
            const RhGcField* field = &info->gc_fields[i];
            void* value;
            if (object_size < __SIZEOF_POINTER__ || field->offset > object_size - __SIZEOF_POINTER__)
                RhpFallbackFailFast(139);
            value = *(void**)((u8*)object + field->offset);
            rh_mark_field_value(value, field->kind);
            i = i + 1ul;
        }

        if (info->runtime_kind == RH_TYPE_SZARRAY && info->component_gc_field_count != 0ul)
        {
            int length = *(const int*)((const u8*)object + ARRAY_LENGTH_OFFSET);
            usize component_size = rh_component_size(type);
            usize element_index = 0ul;
            if (length < 0 || component_size < __SIZEOF_POINTER__)
                RhpFallbackFailFast(139);
            while (element_index < (usize)length)
            {
                u8* element = (u8*)object + ARRAY_DATA_OFFSET + element_index * component_size;
                usize field_index = 0ul;
                while (field_index < info->component_gc_field_count)
                {
                    const RhGcField* field = &info->component_gc_fields[field_index];
                    void* value;
                    if (field->offset > component_size - __SIZEOF_POINTER__)
                        RhpFallbackFailFast(139);
                    value = *(void**)(element + field->offset);
                    rh_mark_field_value(value, field->kind);
                    field_index = field_index + 1ul;
                }
                element_index = element_index + 1ul;
            }
        }
    }
}

static void rh_rebuild_free_list(void)
{
    u8* scan = rh_heap_base;
    RhBlock* tail = (RhBlock*)0;
    rh_free_list = (RhBlock*)0;
    while (scan < rh_heap_used)
    {
        RhBlock* block = (RhBlock*)scan;
        rh_validate_block(scan, block, 145);
        if (block->kind == RH_BLOCK_FREE)
        {
            block->mark_next = (RhBlock*)0;
            block->flags = 0ul;
            rh_set_free_next(block, (RhBlock*)0);
            if (tail == (RhBlock*)0)
                rh_free_list = block;
            else
                rh_set_free_next(tail, block);
            tail = block;
        }
        scan = scan + block->size;
    }
}

static void rh_sweep(void)
{
    u8* scan = rh_heap_base;
    u8* last_live_end = rh_heap_base;
    while (scan < rh_heap_used)
    {
        RhBlock* block = (RhBlock*)scan;
        rh_validate_block(scan, block, 145);
        if (block->kind == RH_BLOCK_OBJECT)
        {
            if ((block->flags & RH_BLOCK_MARK) == 0ul)
            {
                rh_monitor_destroy_for_object(rh_object_from_block(block));
                block->kind = RH_BLOCK_FREE;
                block->mark_next = (RhBlock*)0;
                block->flags = 0ul;
            }
            else
            {
                block->mark_next = (RhBlock*)0;
                block->flags = 0ul;
                last_live_end = scan + block->size;
            }
        }
        scan = scan + block->size;
    }

    scan = rh_heap_base;
    while (scan < rh_heap_used)
    {
        RhBlock* block = (RhBlock*)scan;
        rh_validate_block(scan, block, 145);
        if (block->kind == RH_BLOCK_FREE)
        {
            u8* next = scan + block->size;
            while (next < rh_heap_used)
            {
                RhBlock* next_block = (RhBlock*)next;
                rh_validate_block(next, next_block, 145);
                if (next_block->kind != RH_BLOCK_FREE)
                    break;
                block->size = block->size + next_block->size;
                next = scan + block->size;
            }
        }
        scan = scan + block->size;
    }

    rh_heap_used = last_live_end;
    rh_decommit_unused_tail();
    rh_rebuild_free_list();
    rh_allocation_debt = 0ul;
    RhpGcPollRequested = 0u;
}

static void rh_collect(const RhSafePoint* safe_point, void* frame_pointer)
{
    if (rh_gc_running != 0)
        return;
    rh_gc_running = 1;
    rh_mark_stack = (RhBlock*)0;
    rh_mark_frame_roots(safe_point, (usize)frame_pointer);
    rh_mark_static_roots();
    rh_mark_object(rh_delegate_temporary_root);
    rh_mark_object(rh_active_exception);
    {
        usize i = 0ul;
        while (i < RhpEhFrameCount)
        {
            rh_mark_eh_register_context_roots(&RhpEhRegisterContexts[i]);
            i = i + 1ul;
        }
        i = 0ul;
        while (i < rh_eh_continuation_count)
        {
            rh_mark_object(rh_eh_continuations[i].exception);
            rh_mark_eh_register_context_roots(&rh_eh_continuation_registers[i]);
            i = i + 1ul;
        }
        i = 0ul;
        while (i < rh_catch_context_count)
        {
            rh_mark_object(rh_catch_contexts[i].exception);
            i = i + 1ul;
        }
    }
    rh_drain_mark_stack();
    rh_sweep();
    rh_gc_running = 0;
}

static void rh_collect_current(void)
{
    const RhSafePoint* safe_point = RhpCurrentSafePoint;
    void* frame_pointer = RhpCurrentFramePointer;
    if (safe_point == (const RhSafePoint*)0 || frame_pointer == (void*)0)
        RhpFallbackFailFast(144);
    rh_collect(safe_point, frame_pointer);
}

static void* rh_try_allocate_from_free_list(usize total)
{
    RhBlock* previous = (RhBlock*)0;
    RhBlock* block = rh_free_list;
    while (block != (RhBlock*)0)
    {
        RhBlock* next;
        u8* address = (u8*)block;
        rh_validate_block(address, block, 146);
        if (block->kind != RH_BLOCK_FREE)
            RhpFallbackFailFast(146);
        next = rh_free_next(block);
        if (block->size >= total)
        {
            usize remainder = block->size - total;
            if (remainder >= rh_minimum_block_size())
            {
                RhBlock* tail = (RhBlock*)(address + total);
                tail->size = remainder;
                tail->kind = RH_BLOCK_FREE;
                tail->mark_next = (RhBlock*)0;
                tail->flags = 0ul;
                rh_set_free_next(tail, next);
                if (previous == (RhBlock*)0)
                    rh_free_list = tail;
                else
                    rh_set_free_next(previous, tail);
                block->size = total;
            }
            else
            {
                if (previous == (RhBlock*)0)
                    rh_free_list = next;
                else
                    rh_set_free_next(previous, next);
            }
            block->kind = RH_BLOCK_OBJECT;
            block->mark_next = (RhBlock*)0;
            block->flags = 0ul;
            return (void*)(address + RH_BLOCK_HEADER_SIZE);
        }
        previous = block;
        block = next;
    }
    return (void*)0;
}

static void* rh_try_bump_allocate(usize total)
{
    RhBlock* block;
    u8* required;
    if (total > (usize)(rh_heap_limit - rh_heap_used))
        return (void*)0;
    required = rh_heap_used + total;
    if (!rh_ensure_committed(required))
        return (void*)0;
    block = (RhBlock*)rh_heap_used;
    block->size = total;
    block->kind = RH_BLOCK_OBJECT;
    block->mark_next = (RhBlock*)0;
    block->flags = 0ul;
    rh_heap_used = rh_heap_used + total;
    return (void*)((u8*)block + RH_BLOCK_HEADER_SIZE);
}

static void* rh_try_allocate(usize gc_size)
{
    usize total = rh_total_block_size(gc_size);
    void* storage;
    if (total == 0ul || total > RH_HEAP_RESERVE)
        return (void*)0;
    storage = rh_try_allocate_from_free_list(total);
    if (storage != (void*)0)
        return storage;
    return rh_try_bump_allocate(total);
}

static void* rh_allocate_object_with_zeroing(const RhMethodTable* type, usize gc_size, int zeroing_optional)
{
    void* storage;
    void* object;
    usize maximum = (usize)-1;

    if (rh_allocation_debt >= RH_GC_POLL_DEBT_LIMIT)
        rh_collect_current();

    storage = rh_try_allocate(gc_size);
    if (storage == (void*)0)
    {
        rh_collect_current();
        storage = rh_try_allocate(gc_size);
        if (storage == (void*)0)
            RhpFallbackFailFast(141);
    }

    if (zeroing_optional != 0 && (type->flags & RH_EETYPE_HAS_POINTERS) == 0u)
    {
        usize prefix_size = (usize)type->base_size;
        if (prefix_size > gc_size)
            RhpFallbackFailFast(140);
        rh_zero(storage, prefix_size);
    }
    else
    {
        rh_zero(storage, gc_size);
    }

    object = (void*)((u8*)storage + SYNC_BLOCK_SIZE);
    ((RhObject*)object)->type = type;
    if (rh_allocation_debt > maximum - gc_size)
        rh_allocation_debt = maximum;
    else
        rh_allocation_debt = rh_allocation_debt + gc_size;
    if (rh_allocation_debt >= RH_GC_POLL_DEBT_LIMIT)
        RhpGcPollRequested = 1u;
    return object;
}

static void* rh_allocate_object(const RhMethodTable* type, usize gc_size)
{
    return rh_allocate_object_with_zeroing(type, gc_size, 0);
}

void* RhpNewFast(const RhMethodTable* type)
{
    const RhTypeInfo* info;
    usize gc_size;
    info = rh_require_method_table(type, 140);
    if (info->runtime_kind != RH_TYPE_FIXED)
        RhpFallbackFailFast(140);
    gc_size = (usize)type->base_size;
    if (rh_total_block_size(gc_size) == 0ul)
        RhpFallbackFailFast(140);
    return rh_allocate_object(type, gc_size);
}

void* RhpNewArray(const RhMethodTable* type, int length)
{
    const RhTypeInfo* info;
    usize gc_size;
    void* object;
    info = rh_require_method_table(type, 140);
    if (info->runtime_kind != RH_TYPE_SZARRAY && info->runtime_kind != RH_TYPE_STRING)
        RhpFallbackFailFast(140);
    gc_size = rh_variable_gc_size(type, length);
    if (gc_size == 0ul || rh_total_block_size(gc_size) == 0ul)
        RhpFallbackFailFast(140);

    object = rh_allocate_object(type, gc_size);
    if (info->runtime_kind == RH_TYPE_STRING)
        *(int*)((u8*)object + STRING_LENGTH_OFFSET) = length;
    else
        *(int*)((u8*)object + ARRAY_LENGTH_OFFSET) = length;
    return object;
}

void* RhpNewArrayUninitialized(const RhMethodTable* type, int length)
{
    const RhTypeInfo* info;
    usize gc_size;
    void* object;
    info = rh_require_method_table(type, 140);
    if (info->runtime_kind != RH_TYPE_SZARRAY)
        RhpFallbackFailFast(140);
    gc_size = rh_variable_gc_size(type, length);
    if (gc_size == 0ul || rh_total_block_size(gc_size) == 0ul)
        RhpFallbackFailFast(140);

    object = rh_allocate_object_with_zeroing(type, gc_size, 1);
    *(int*)((u8*)object + ARRAY_LENGTH_OFFSET) = length;
    return object;
}

static void* rh_delegate_read_slot(void* delegate_ref, usize offset)
{
    return *(void**)((u8*)delegate_ref + offset);
}

static void rh_delegate_write_slot(void* delegate_ref, usize offset, void* value)
{
    *(void**)((u8*)delegate_ref + offset) = value;
}

static usize rh_delegate_leaf_count(
    void* delegate_ref,
    const RhMethodTable* delegate_type,
    const RhMethodTable* array_type,
    usize invocation_list_offset,
    usize invocation_count_offset)
{
    void* list;
    usize count;
    int length;
    if (delegate_ref == (void*)0 || ((RhObject*)delegate_ref)->type != delegate_type)
        RhpFallbackFailFast(152);
    list = rh_delegate_read_slot(delegate_ref, invocation_list_offset);
    count = (usize)rh_delegate_read_slot(delegate_ref, invocation_count_offset);
    if (list == (void*)0)
    {
        if (count != 1ul)
            RhpFallbackFailFast(152);
        return 1ul;
    }
    if (count <= 1ul || ((RhObject*)list)->type != array_type || count > 2147483647ul)
        RhpFallbackFailFast(152);
    length = *(int*)((u8*)list + ARRAY_LENGTH_OFFSET);
    if (length < 0 || count >(usize)length)
        RhpFallbackFailFast(152);
    return count;
}

static void* rh_delegate_leaf_at(
    void* delegate_ref,
    usize index,
    const RhMethodTable* delegate_type,
    const RhMethodTable* array_type,
    usize invocation_list_offset,
    usize invocation_count_offset)
{
    void* list;
    usize count = rh_delegate_leaf_count(
        delegate_ref,
        delegate_type,
        array_type,
        invocation_list_offset,
        invocation_count_offset);
    if (index >= count)
        RhpFallbackFailFast(152);
    list = rh_delegate_read_slot(delegate_ref, invocation_list_offset);
    if (list == (void*)0 || count == 1ul)
        return delegate_ref;
    delegate_ref = *(void**)((u8*)list + ARRAY_DATA_OFFSET + index * __SIZEOF_POINTER__);
    if (delegate_ref == (void*)0 || ((RhObject*)delegate_ref)->type != delegate_type)
        RhpFallbackFailFast(152);
    return delegate_ref;
}

static int rh_delegate_same_leaf(
    void* left,
    void* right,
    const RhMethodTable* delegate_type,
    usize target_offset,
    usize method_ptr_offset)
{
    if (left == right)
        return 1;
    if (left == (void*)0 || right == (void*)0)
        return 0;
    if (((RhObject*)left)->type != delegate_type || ((RhObject*)right)->type != delegate_type)
        return 0;
    return rh_delegate_read_slot(left, target_offset) == rh_delegate_read_slot(right, target_offset) &&
        rh_delegate_read_slot(left, method_ptr_offset) == rh_delegate_read_slot(right, method_ptr_offset);
}

static void* rh_delegate_allocate_multicast(
    const RhMethodTable* delegate_type,
    const RhMethodTable* array_type,
    usize count,
    usize target_offset,
    usize method_ptr_offset,
    usize invocation_list_offset,
    usize invocation_count_offset)
{
    void* result;
    void* list;
    if (count < 2ul || count > 2147483647ul)
        RhpFallbackFailFast(152);
    result = RhpNewFast(delegate_type);
    rh_delegate_temporary_root = (RhObject*)result;
    list = RhpNewArray(array_type, (int)count);
    rh_delegate_write_slot(result, target_offset, (void*)0);
    rh_delegate_write_slot(result, method_ptr_offset, (void*)0);
    rh_delegate_write_slot(result, invocation_list_offset, list);
    rh_delegate_write_slot(result, invocation_count_offset, (void*)count);
    return result;
}

void* RhpDelegateCombine(
    void* left,
    void* right,
    const RhMethodTable* delegate_type,
    const RhMethodTable* array_type,
    usize target_offset,
    usize method_ptr_offset,
    usize invocation_list_offset,
    usize invocation_count_offset)
{
    usize left_count;
    usize right_count;
    usize total;
    usize index;
    void* result;
    void* list;
    if (left == (void*)0)
        return right;
    if (right == (void*)0)
        return left;
    if (((RhObject*)left)->type != delegate_type || ((RhObject*)right)->type != delegate_type)
        RhpFallbackFailFast(152);
    left_count = rh_delegate_leaf_count(left, delegate_type, array_type, invocation_list_offset, invocation_count_offset);
    right_count = rh_delegate_leaf_count(right, delegate_type, array_type, invocation_list_offset, invocation_count_offset);
    if (left_count > 2147483647ul - right_count)
        RhpFallbackFailFast(152);
    total = left_count + right_count;
    result = rh_delegate_allocate_multicast(
        delegate_type,
        array_type,
        total,
        target_offset,
        method_ptr_offset,
        invocation_list_offset,
        invocation_count_offset);
    list = rh_delegate_read_slot(result, invocation_list_offset);
    index = 0ul;
    while (index < left_count)
    {
        *(void**)((u8*)list + ARRAY_DATA_OFFSET + index * __SIZEOF_POINTER__) = rh_delegate_leaf_at(
            left,
            index,
            delegate_type,
            array_type,
            invocation_list_offset,
            invocation_count_offset);
        index = index + 1ul;
    }
    index = 0ul;
    while (index < right_count)
    {
        *(void**)((u8*)list + ARRAY_DATA_OFFSET + (left_count + index) * __SIZEOF_POINTER__) = rh_delegate_leaf_at(
            right,
            index,
            delegate_type,
            array_type,
            invocation_list_offset,
            invocation_count_offset);
        index = index + 1ul;
    }
    rh_delegate_temporary_root = (RhObject*)0;
    return result;
}

void* RhpDelegateRemove(
    void* source,
    void* value,
    const RhMethodTable* delegate_type,
    const RhMethodTable* array_type,
    usize target_offset,
    usize method_ptr_offset,
    usize invocation_list_offset,
    usize invocation_count_offset)
{
    usize source_count;
    usize value_count;
    usize start;
    usize compare_index;
    usize remove_at = (usize)-1;
    usize new_count;
    usize source_index;
    usize destination_index;
    void* result;
    void* list;
    if (source == (void*)0 || value == (void*)0)
        return source;
    if (((RhObject*)source)->type != delegate_type || ((RhObject*)value)->type != delegate_type)
        return source;
    source_count = rh_delegate_leaf_count(source, delegate_type, array_type, invocation_list_offset, invocation_count_offset);
    value_count = rh_delegate_leaf_count(value, delegate_type, array_type, invocation_list_offset, invocation_count_offset);
    if (value_count == 0ul || source_count < value_count)
        return source;
    start = source_count - value_count;
    for (;;)
    {
        compare_index = 0ul;
        while (compare_index < value_count && rh_delegate_same_leaf(
            rh_delegate_leaf_at(source, start + compare_index, delegate_type, array_type, invocation_list_offset, invocation_count_offset),
            rh_delegate_leaf_at(value, compare_index, delegate_type, array_type, invocation_list_offset, invocation_count_offset),
            delegate_type,
            target_offset,
            method_ptr_offset))
        {
            compare_index = compare_index + 1ul;
        }
        if (compare_index == value_count)
        {
            remove_at = start;
            break;
        }
        if (start == 0ul)
            break;
        start = start - 1ul;
    }
    if (remove_at == (usize)-1)
        return source;
    new_count = source_count - value_count;
    if (new_count == 0ul)
        return (void*)0;
    if (new_count == 1ul)
    {
        source_index = remove_at == 0ul ? value_count : 0ul;
        return rh_delegate_leaf_at(source, source_index, delegate_type, array_type, invocation_list_offset, invocation_count_offset);
    }
    result = rh_delegate_allocate_multicast(
        delegate_type,
        array_type,
        new_count,
        target_offset,
        method_ptr_offset,
        invocation_list_offset,
        invocation_count_offset);
    list = rh_delegate_read_slot(result, invocation_list_offset);
    source_index = 0ul;
    destination_index = 0ul;
    while (source_index < source_count)
    {
        if (source_index == remove_at)
        {
            source_index = source_index + value_count;
        }
        else
        {
            *(void**)((u8*)list + ARRAY_DATA_OFFSET + destination_index * __SIZEOF_POINTER__) = rh_delegate_leaf_at(
                source,
                source_index,
                delegate_type,
                array_type,
                invocation_list_offset,
                invocation_count_offset);
            source_index = source_index + 1ul;
            destination_index = destination_index + 1ul;
        }
    }
    rh_delegate_temporary_root = (RhObject*)0;
    return result;
}

static void rh_copy_utf16(u16* destination, const u16* source, int length)
{
    int i = 0;
    while (i < length)
    {
        destination[i] = source[i];
        i = i + 1;
    }
}

void* RhpNewStringFromChar(
    const RhMethodTable* type,
    u16 value,
    int length)
{
    void* object = RhpNewArray(type, length);
    u16* destination = (u16*)((u8*)object + STRING_FIRST_CHAR_OFFSET);
    int i = 0;
    while (i < length)
    {
        destination[i] = value;
        i = i + 1;
    }
    return object;
}

void* RhpNewStringFromUtf16(
    const RhMethodTable* type,
    const u16* source)
{
    int length = 0;
    void* object;
    if (source == (const u16*)0)
        RhpFallbackFailFast(147);
    while (source[length] != 0u)
    {
        if (length == 0x7fffffff)
            RhpFallbackFailFast(140);
        length = length + 1;
    }
    object = RhpNewArray(type, length);
    rh_copy_utf16((u16*)((u8*)object + STRING_FIRST_CHAR_OFFSET), source, length);
    return object;
}

static const RhMethodTable* rh_require_array(const void* array)
{
    const RhObject* object;
    const RhMethodTable* type;
    if (array == (const void*)0)
        RhpFallbackFailFast(147);
    object = (const RhObject*)array;
    type = object->type;
    if (rh_require_method_table(type, 147)->runtime_kind != RH_TYPE_SZARRAY ||
        *(const int*)((const u8*)array + ARRAY_LENGTH_OFFSET) < 0)
    {
        RhpFallbackFailFast(147);
    }
    return type;
}

static int rh_array_length(const void* array)
{
    rh_require_array(array);
    return *(const int*)((const u8*)array + ARRAY_LENGTH_OFFSET);
}

int RhpArrayGetLength(const void* array)
{
    return rh_array_length(array);
}

void RhpArrayClear(void* array, int index, int length)
{
    const RhMethodTable* type = rh_require_array(array);
    int array_length = rh_array_length(array);
    usize component_size = rh_component_size(type);
    usize byte_offset;
    usize byte_count;
    u8* destination;
    if (index < 0 || length < 0 || index > array_length || length > array_length - index)
        RhpFallbackFailFast(148);
    byte_offset = (usize)index * component_size;
    byte_count = (usize)length * component_size;
    destination = (u8*)array + ARRAY_DATA_OFFSET + byte_offset;
#ifdef __riscv_vector
    if (byte_count >= 16ul)
    {
        __asm__ volatile(
            "vsetvli a2, zero, e8, m8, ta, ma\n"
            "vxor.vv v8, v8, v8\n"
            ".Lrhp_array_clear_loop_%=:\n"
            "vsetvli a2, %[count], e8, m8, ta, ma\n"
            "vse8.v v8, (%[destination])\n"
            "add %[destination], %[destination], a2\n"
            "sub %[count], %[count], a2\n"
            "bne %[count], zero, .Lrhp_array_clear_loop_%="
            :
        : [destination] "{a0}"(destination), [count] "{a1}"(byte_count)
            : "memory");
        return;
    }
#endif
    rh_zero(destination, byte_count);
}

int RhpArrayCopy(
    const void* source_array,
    int source_index,
    void* destination_array,
    int destination_index,
    int length)
{
    const RhMethodTable* source_type = rh_require_array(source_array);
    const RhMethodTable* destination_type = rh_require_array(destination_array);
    int source_length = rh_array_length(source_array);
    int destination_length = rh_array_length(destination_array);
    const RhMethodTable* source_element;
    const RhMethodTable* destination_element;
    usize source_component_size;
    usize destination_component_size;
    usize byte_count;
    const u8* source;
    u8* destination;

    if (source_index < 0 || destination_index < 0 || length < 0 ||
        source_index > source_length || destination_index > destination_length ||
        length > source_length - source_index || length > destination_length - destination_index)
    {
        RhpFallbackFailFast(148);
    }

    source_element = (const RhMethodTable*)source_type->related_type;
    destination_element = (const RhMethodTable*)destination_type->related_type;
    source_component_size = rh_component_size(source_type);
    destination_component_size = rh_component_size(destination_type);

    if (source_element == destination_element)
    {
        if (source_component_size != destination_component_size)
            RhpFallbackFailFast(149);
        byte_count = (usize)length * source_component_size;
        source = (const u8*)source_array + ARRAY_DATA_OFFSET + (usize)source_index * source_component_size;
        destination = (u8*)destination_array + ARRAY_DATA_OFFSET + (usize)destination_index * destination_component_size;
    }
    else
    {
        if (source_component_size != __SIZEOF_POINTER__ || destination_component_size != __SIZEOF_POINTER__ ||
            source_element == (const RhMethodTable*)0 || destination_element == (const RhMethodTable*)0 ||
            !rh_is_reference_type(source_element) || !rh_is_reference_type(destination_element))
        {
            return 0;
        }

        source = (const u8*)source_array + ARRAY_DATA_OFFSET + (usize)source_index * __SIZEOF_POINTER__;
        destination = (u8*)destination_array + ARRAY_DATA_OFFSET + (usize)destination_index * __SIZEOF_POINTER__;
        if (!rh_is_assignable(source_element, destination_element))
        {
            usize i = 0ul;
            while (i < (usize)length)
            {
                const RhObject* value = *(const RhObject**)(source + i * __SIZEOF_POINTER__);
                if (value != (const RhObject*)0 && !rh_is_assignable(value->type, destination_element))
                    return 0;
                i = i + 1ul;
            }
        }
        byte_count = (usize)length * __SIZEOF_POINTER__;
    }

#ifdef __riscv_vector
    if (byte_count >= 16ul)
    {
        if (destination == source)
            return 1;
        if ((usize)destination < (usize)source ||
            (usize)destination >= (usize)source + byte_count)
        {
            __asm__ volatile(
                ".Lrhp_array_copy_forward_loop_%=:\n"
                "vsetvli a3, %[count], e8, m8, ta, ma\n"
                "vle8.v v8, (%[source])\n"
                "vse8.v v8, (%[destination])\n"
                "add %[source], %[source], a3\n"
                "add %[destination], %[destination], a3\n"
                "sub %[count], %[count], a3\n"
                "bne %[count], zero, .Lrhp_array_copy_forward_loop_%="
                :
            : [destination] "{a0}"(destination), [source] "{a1}"(source), [count] "{a2}"(byte_count)
                : "memory");
            return 1;
        }

        source = source + byte_count;
        destination = destination + byte_count;
        __asm__ volatile(
            ".Lrhp_array_copy_backward_loop_%=:\n"
            "vsetvli a3, %[count], e8, m8, ta, ma\n"
            "sub %[source], %[source], a3\n"
            "sub %[destination], %[destination], a3\n"
            "vle8.v v8, (%[source])\n"
            "vse8.v v8, (%[destination])\n"
            "sub %[count], %[count], a3\n"
            "bne %[count], zero, .Lrhp_array_copy_backward_loop_%="
            :
        : [destination] "{a0}"(destination), [source] "{a1}"(source), [count] "{a2}"(byte_count)
            : "memory");
        return 1;
    }
#endif

    rh_memmove(destination, source, byte_count);
    return 1;
}

void* RhpNewStringFromCharArray(
    const RhMethodTable* type,
    const void* array)
{
    const RhMethodTable* array_type = rh_require_array(array);
    int length;
    const u16* source;
    void* object;
    if (rh_component_size(array_type) != 2ul)
        RhpFallbackFailFast(147);
    length = rh_array_length(array);
    source = (const u16*)((const u8*)array + ARRAY_DATA_OFFSET);
    object = RhpNewArray(type, length);
    rh_copy_utf16((u16*)((u8*)object + STRING_FIRST_CHAR_OFFSET), source, length);
    return object;
}

void* RhpNewStringFromCharArrayRange(
    const RhMethodTable* type,
    const void* array,
    int start_index,
    int length)
{
    const RhMethodTable* array_type = rh_require_array(array);
    int array_length;
    const u16* source;
    void* object;
    if (rh_component_size(array_type) != 2ul)
        RhpFallbackFailFast(147);
    array_length = rh_array_length(array);
    if (start_index < 0 || length < 0 || start_index > array_length || length > array_length - start_index)
        RhpFallbackFailFast(147);
    source = (const u16*)((const u8*)array + ARRAY_DATA_OFFSET) + start_index;
    object = RhpNewArray(type, length);
    rh_copy_utf16((u16*)((u8*)object + STRING_FIRST_CHAR_OFFSET), source, length);
    return object;
}

void* RhpNewStringFromReadOnlySpan(
    const RhMethodTable* type,
    const u16* source,
    int length)
{
    void* object;
    if (length < 0 || (source == (const u16*)0 && length != 0))
        RhpFallbackFailFast(147);
    object = RhpNewArray(type, length);
    if (length != 0)
        rh_copy_utf16((u16*)((u8*)object + STRING_FIRST_CHAR_OFFSET), source, length);
    return object;
}

void RhpGcPoll(void)
{
    if (RhpGcPollRequested != 0u)
        rh_collect_current();
}

static const RhMethodTable* rh_require_string(const void* value)
{
    const RhObject* object;
    const RhMethodTable* type;
    if (value == (const void*)0)
        RhpFallbackFailFast(147);
    object = (const RhObject*)value;
    type = object->type;
    if (rh_require_method_table(type, 147)->runtime_kind != RH_TYPE_STRING ||
        *(const int*)((const u8*)value + STRING_LENGTH_OFFSET) < 0)
    {
        RhpFallbackFailFast(147);
    }
    return type;
}

void RhpConsoleWriteUtf16(const u16* text, int length)
{
    int index = 0;
    if (text == (const u16*)0 || length <= 0)
        return;

    while (index < length)
    {
        u32 scalar = (u32)text[index];
        u8 encoded[4];
        usize encoded_length;
        index = index + 1;

        if (scalar >= 0xd800u && scalar <= 0xdbffu)
        {
            if (index < length)
            {
                u32 low = (u32)text[index];
                if (low >= 0xdc00u && low <= 0xdfffu)
                {
                    scalar = 0x10000u + ((scalar - 0xd800u) << 10) + (low - 0xdc00u);
                    index = index + 1;
                }
                else
                {
                    scalar = 0xfffdu;
                }
            }
            else
            {
                scalar = 0xfffdu;
            }
        }
        else if (scalar >= 0xdc00u && scalar <= 0xdfffu)
        {
            scalar = 0xfffdu;
        }

        encoded_length = rh_encode_utf8(scalar, encoded);
        rh_write_all(encoded, encoded_length);
    }
}

void RhpConsoleWriteUtf16Z(const u16* text)
{
    int length = 0;
    if (text == (const u16*)0)
        return;
    while (text[length] != 0u)
        length = length + 1;
    RhpConsoleWriteUtf16(text, length);
}

void RhpConsoleWriteString(const void* value)
{
    const u8* object;
    int length;
    const u16* chars;
    if (value == (const void*)0)
        return;
    object = (const u8*)value;
    rh_require_string(value);
    length = *(const int*)(object + STRING_LENGTH_OFFSET);
    chars = (const u16*)(object + STRING_FIRST_CHAR_OFFSET);
    RhpConsoleWriteUtf16(chars, length);
}
