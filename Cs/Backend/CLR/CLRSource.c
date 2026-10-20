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

#ifdef __riscv_vector
typedef __rvv_uint8m8_t rh_bytes;
usize __riscv_vsetvl_e8m8(usize avl);
usize __riscv_vsetvlmax_e8m8(void);
rh_bytes __riscv_vle8_v_u8m8(const u8* rs1, usize vl);
void __riscv_vse8_v_u8m8(u8* rs1, rh_bytes vs3, usize vl);
rh_bytes __riscv_vmv_v_x_u8m8(u8 rs1, usize vl);
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
#define RH_EH_CATCH 1ul
#define RH_EH_CATCH_ALL 2ul
#define RH_EH_FINALLY 3ul
#define RH_EH_FAULT 4ul
#define RH_EH_FILTER 5ul
#define RH_REGION_FRAME 0ul
#define RH_REGION_FRAMELESS 1ul
#define RH_REGION_FUNCLET 2ul
#define RH_REGION_FILTER 3ul
#define RH_UNWIND_REGISTER_COUNT 64ul
#define RH_UNWIND_DEPTH_LIMIT (1ul << 20)

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

typedef struct RhSavedRegister
{
    usize index;
    isize offset;
    usize size;
} RhSavedRegister;

// A filter clause keeps the address of its filter in catch_type
typedef struct RhEhClause
{
    usize kind;
    const void* try_start;
    const void* try_end;
    const void* handler;
    const RhMethodTable* catch_type;
    isize source_try_start;
    isize source_try_end;
    isize source_handler_start;
    isize source_handler_end;
    isize source_handler_index;
} RhEhClause;

typedef struct RhEhInfo
{
    usize clause_count;
    const RhEhClause* clauses;
} RhEhInfo;

// A funclet keeps its parent's frame pointer; its own slots are addressed from its body stack pointer
typedef struct RhCodeRegion
{
    const void* start;
    const void* end;
    usize kind;
    isize frame_size;
    isize fp_offset;
    isize ra_offset;
    usize saved_count;
    const RhSavedRegister* saved;
    const RhEhInfo* eh;
    isize clause;
} RhCodeRegion;

typedef struct RhUnwindContext
{
    const void* ip;
    usize sp;
    usize fp;
    usize registers[RH_UNWIND_REGISTER_COUNT];
} RhUnwindContext;

// The stack pointer at a safepoint's call sits stack_adjustment bytes below the body stack pointer of its frame
typedef struct RhSafePoint
{
    const void* return_address;
    const RhCodeRegion* region;
    usize root_count;
    const RhRoot* roots;
    isize stack_adjustment;
} RhSafePoint;

typedef struct RhStaticRoot
{
    void* address;
    usize kind;
} RhStaticRoot;

typedef struct RhObject RhObject;

typedef struct RhDispatchRecord
{
    RhObject* exception;
    const RhUnwindContext* filtering;
    struct RhDispatchRecord* next;
} RhDispatchRecord;

#if (defined(__riscv_flen) && __riscv_flen >= 32) || defined(__i386__) || defined(__x86_64__) || defined(__aarch64__)
typedef union RhFloatBits
{
    float value;
    u32 bits;
} RhFloatBits;
#endif

#if (defined(__riscv_flen) && __riscv_flen >= 64 || defined(__x86_64__) || defined(__aarch64__)) && __SIZEOF_POINTER__ == 8
typedef union RhDoubleBits
{
    double value;
    usize bits;
} RhDoubleBits;
#endif

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
static const RhTypeInfo** rh_type_index;
static usize rh_type_index_mask;
static const RhStaticRoot* rh_static_roots;
static usize rh_static_root_count;
static u8* rh_heap_base;
static u8* rh_heap_used;
static u8* rh_heap_committed;
static u8* rh_heap_limit;
static usize rh_allocation_debt;
#define RH_GC_POLL_DEBT_LIMIT 262144ul
static RhBlock* rh_mark_stack;
#define RH_FREE_BUCKET_COUNT 24ul
#define RH_INTERIOR_BATCH_SIZE 512ul
static RhBlock* rh_free_buckets[RH_FREE_BUCKET_COUNT];
static void* rh_interior_batch[RH_INTERIOR_BATCH_SIZE];
static usize rh_interior_batch_count;
static int rh_gc_running;
static RhObject* rh_delegate_temporary_root;
static RhDispatchRecord* rh_dispatches;
static const RhCodeRegion* rh_regions;
static usize rh_region_count;

const void* RhpCurrentReturnAddress;
usize RhpCurrentStackPointer;
void* RhpCurrentFramePointer;
volatile u32 RhpGcPollRequested;

#if (defined(__riscv_flen) && __riscv_flen >= 32) || defined(__i386__) || defined(__x86_64__) || defined(__aarch64__)
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

#if (defined(__riscv_flen) && __riscv_flen >= 64 || defined(__x86_64__) || defined(__aarch64__)) && __SIZEOF_POINTER__ == 8
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
usize RhpCallFunclet(const void* handler, const RhUnwindContext* context, RhObject* exception);
void RhpCallFuncletReturn(void);
void RhpCallFuncletReturnFalse(void);
void RhpResume(const RhUnwindContext* context, const void* target);

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

static const RhCodeRegion* rh_find_region(const void* ip)
{
    usize key = (usize)ip - 1ul;
    usize low = 0ul;
    usize high = rh_region_count;
    const RhCodeRegion* region;
    while (low < high)
    {
        usize middle = low + (high - low) / 2ul;
        if ((usize)rh_regions[middle].start <= key)
            low = middle + 1ul;
        else
            high = middle;
    }
    if (low == 0ul)
        return (const RhCodeRegion*)0;
    region = &rh_regions[low - 1ul];
    if (key >= (usize)region->end)
        return (const RhCodeRegion*)0;
    return region;
}

static const RhSafePoint* rh_find_safe_point(const void* return_address);

static void rh_unwind_step(RhUnwindContext* context, const RhCodeRegion* region)
{
    usize base = region->kind == RH_REGION_FRAME ? context->fp : context->sp;
    usize i = 0ul;
    if (region->kind >= RH_REGION_FUNCLET)
    {
        const RhSafePoint* safe_point = rh_find_safe_point(context->ip);
        if (safe_point == (const RhSafePoint*)0)
            RhpFallbackFailFast(151);
        base = base + (usize)safe_point->stack_adjustment;
    }
    while (i < region->saved_count)
    {
        const RhSavedRegister* saved = &region->saved[i];
        const usize* source = (const usize*)(base + (usize)saved->offset);
        if (saved->index + (saved->size > sizeof(usize) ? 1ul : 0ul) >= RH_UNWIND_REGISTER_COUNT)
            RhpFallbackFailFast(151);
        context->registers[saved->index] = source[0];
        if (saved->size > sizeof(usize))
            context->registers[saved->index + 1ul] = source[1];
        i = i + 1ul;
    }
    context->ip = *(const void* const*)(base + (usize)region->ra_offset);
    if (region->kind == RH_REGION_FRAME)
        context->fp = *(const usize*)(base + (usize)region->fp_offset);
    context->sp = base + (usize)region->frame_size;
}

static int rh_unwind_frame(RhUnwindContext* context, const RhCodeRegion* region)
{
    rh_unwind_step(context, region);
    if (context->ip == (const void*)RhpCallFuncletReturn)
    {
        *context = **(const RhUnwindContext* const*)context->sp;
        return 1;
    }
    return region->kind >= RH_REGION_FUNCLET;
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

static int rh_eh_funclet_in_try(const RhCodeRegion* region, const RhEhClause* clause)
{
    const RhEhClause* owner;
    if (region->kind != RH_REGION_FUNCLET || region->clause < 0)
        return 0;
    owner = &region->eh->clauses[(usize)region->clause];
    return owner->source_handler_start >= clause->source_try_start &&
        owner->source_handler_end <= clause->source_try_end;
}

// Code inside a funclet is protected by every try that encloses the funclet's handler, besides the trys nested in it
static int rh_eh_protects(const RhCodeRegion* region, usize clause_index, usize ip)
{
    const RhEhClause* clause = &region->eh->clauses[clause_index];
    return (ip >= (usize)clause->try_start && ip < (usize)clause->try_end) || rh_eh_funclet_in_try(region, clause);
}

static isize rh_eh_next_clause(const RhCodeRegion* region, usize ip, isize previous)
{
    const RhEhInfo* eh = region->eh;
    isize best = -1;
    usize i = 0ul;
    while (i < eh->clause_count)
    {
        const RhEhClause* clause = &eh->clauses[i];
        if (rh_eh_protects(region, i, ip) &&
            (previous < 0 || rh_eh_precedes(&eh->clauses[(usize)previous], clause)) &&
            (best < 0 || rh_eh_precedes(clause, &eh->clauses[(usize)best])))
        {
            best = (isize)i;
        }
        i = i + 1ul;
    }
    return best;
}

static int rh_eh_accepts(const RhCodeRegion* region, const RhEhClause* clause, const RhUnwindContext* context, RhObject* exception)
{
    RhUnwindContext frame;
    if (clause->kind != RH_EH_FILTER)
        return rh_eh_matches_catch(clause, exception);
    frame = *context;
    while (rh_eh_funclet_in_try(region, clause))
    {
        (void)rh_unwind_frame(&frame, region);
        region = rh_find_region(frame.ip);
    }
    return RhpCallFunclet((const void*)clause->catch_type, &frame, exception) != 0ul;
}

static isize rh_eh_find_catch(const RhCodeRegion* region, const RhUnwindContext* context, RhObject* exception)
{
    usize ip = (usize)context->ip - 1ul;
    isize clause = rh_eh_next_clause(region, ip, -1);
    while (clause >= 0)
    {
        if (rh_eh_accepts(region, &region->eh->clauses[(usize)clause], context, exception))
            return clause;
        clause = rh_eh_next_clause(region, ip, clause);
    }
    return -1;
}

static void rh_eh_run_terminal_handlers(const RhCodeRegion* region, const RhUnwindContext* context, isize stop)
{
    usize ip = (usize)context->ip - 1ul;
    isize clause = rh_eh_next_clause(region, ip, -1);
    while (clause >= 0 && clause != stop)
    {
        const RhEhClause* current = &region->eh->clauses[(usize)clause];
        if (current->kind == RH_EH_FINALLY || current->kind == RH_EH_FAULT)
            (void)RhpCallFunclet(current->handler, context, (RhObject*)0);
        clause = rh_eh_next_clause(region, ip, clause);
    }
}

// Filter exceptions stop at the filter frame; other unhandled exceptions fail fast before unwinding.
static void rh_eh_dispatch(RhObject* exception, const RhUnwindContext* thrower)
{
    RhUnwindContext context = *thrower;
    const RhCodeRegion* region;
    usize depth = 0ul;
    usize target_depth;
    isize target_clause = -1;
    int skip = 0;
    RhDispatchRecord record;
    record.exception = exception;
    record.filtering = thrower;
    record.next = rh_dispatches;
    rh_dispatches = &record;

    for (;;)
    {
        region = rh_find_region(context.ip);
        if (region == (const RhCodeRegion*)0 || depth >= RH_UNWIND_DEPTH_LIMIT)
            RhpFallbackFailFast(134);
        if (region->kind == RH_REGION_FILTER)
            break;
        if (!skip && region->eh != (const RhEhInfo*)0)
        {
            target_clause = rh_eh_find_catch(region, &context, exception);
            if (target_clause >= 0)
                break;
        }
        skip = rh_unwind_frame(&context, region);
        depth = depth + 1ul;
    }

    record.filtering = (const RhUnwindContext*)0;
    target_depth = depth;
    context = *thrower;
    depth = 0ul;
    skip = 0;
    for (;;)
    {
        region = rh_find_region(context.ip);
        if (depth == target_depth && target_clause < 0)
            break;
        if (!skip && region->eh != (const RhEhInfo*)0)
            rh_eh_run_terminal_handlers(region, &context, depth == target_depth ? target_clause : -1);
        if (depth == target_depth)
            break;
        skip = rh_unwind_frame(&context, region);
        depth = depth + 1ul;
    }

    if (target_clause < 0)
    {
        rh_unwind_step(&context, region);
        while (rh_dispatches != (RhDispatchRecord*)0 && (usize)rh_dispatches < context.sp)
            rh_dispatches = rh_dispatches->next;
        RhpResume(&context, (const void*)RhpCallFuncletReturnFalse);
    }

    {
        const RhEhClause* target = &region->eh->clauses[(usize)target_clause];
        const void* continuation;
        // A catch runs and resumes in the frame that holds its try, past the funclets nested in that try
        while (rh_eh_funclet_in_try(region, target))
        {
            (void)rh_unwind_frame(&context, region);
            region = rh_find_region(context.ip);
        }
        continuation = (const void*)RhpCallFunclet(target->handler, &context, exception);
        // Dispatches abandoned by an exception escaping their finally live below the resumed frame
        while (rh_dispatches != (RhDispatchRecord*)0 && (usize)rh_dispatches < context.sp)
            rh_dispatches = rh_dispatches->next;
        RhpResume(&context, continuation);
    }
    RhpFallbackFailFast(150);
}

void RhpDispatchException(RhObject* exception, const RhUnwindContext* thrower)
{
    if (exception == (RhObject*)0 || thrower == (const RhUnwindContext*)0)
        RhpFallbackFailFast(134);
    rh_eh_dispatch(exception, thrower);
}

static int rh_type_info_is_valid(const RhTypeInfo* info)
{
    const RhMethodTable* type = info->type;
    usize component_size;
    if (type == (const RhMethodTable*)0 ||
        info->runtime_kind > RH_TYPE_PARAMETERIZED ||
        (info->gc_field_count != 0ul && info->gc_fields == (const RhGcField*)0) ||
        (info->component_gc_field_count != 0ul && info->component_gc_fields == (const RhGcField*)0) ||
        (((type->flags & RH_EETYPE_HAS_POINTERS) != 0u) !=
            (info->gc_field_count != 0ul || info->component_gc_field_count != 0ul)))
    {
        return 0;
    }

    component_size = rh_component_size(type);
    if (info->runtime_kind == RH_TYPE_FIXED)
    {
        if (rh_has_component_size(type) ||
            rh_method_table_kind(type) != 0ul ||
            type->base_size < RH_MINIMUM_GC_OBJECT_SIZE ||
            info->component_gc_field_count != 0ul)
        {
            return 0;
        }
        return 1;
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
            return 0;
        }
        return 1;
    }

    if (!rh_has_component_size(type) || component_size == 0ul)
        return 0;

    if (info->runtime_kind == RH_TYPE_STRING)
    {
        if (component_size != 2ul ||
            rh_method_table_kind(type) != 0ul ||
            rh_element_type(type) != RH_EETYPE_ELEMENT_TYPE_CLASS ||
            type->base_size < SYNC_BLOCK_SIZE + STRING_FIRST_CHAR_OFFSET + 2ul ||
            info->component_gc_field_count != 0ul)
        {
            return 0;
        }
        return 1;
    }

    if (rh_method_table_kind(type) != RH_EETYPE_PARAMETERIZED_KIND ||
        type->related_type == (const void*)0 ||
        type->base_size < SYNC_BLOCK_SIZE + ARRAY_DATA_OFFSET)
    {
        return 0;
    }

    if (info->runtime_kind == RH_TYPE_SZARRAY)
    {
        if (rh_element_type(type) != RH_EETYPE_ELEMENT_TYPE_SZARRAY ||
            type->base_size != SYNC_BLOCK_SIZE + ARRAY_DATA_OFFSET)
        {
            return 0;
        }
        return 1;
    }

    if (rh_element_type(type) != RH_EETYPE_ELEMENT_TYPE_ARRAY ||
        type->base_size <= SYNC_BLOCK_SIZE + ARRAY_DATA_OFFSET ||
        ((type->base_size - (SYNC_BLOCK_SIZE + ARRAY_DATA_OFFSET)) & 7u) != 0u)
    {
        return 0;
    }
    return 1;
}

static const RhTypeInfo* rh_require_method_table(const RhMethodTable* type, int code)
{
    usize slot;
    if (type == (const RhMethodTable*)0)
        RhpFallbackFailFast(code);
    slot = ((usize)type >> 3) & rh_type_index_mask;
    while (rh_type_index[slot] != (const RhTypeInfo*)0)
    {
        if (rh_type_index[slot]->type == type)
            return rh_type_index[slot];
        slot = (slot + 1ul) & rh_type_index_mask;
    }
    RhpFallbackFailFast(code);
    return (const RhTypeInfo*)0;
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
        rh_bytes zeros = __riscv_vmv_v_x_u8m8(0u, __riscv_vsetvlmax_e8m8());
        while (size != 0ul)
        {
            usize step = __riscv_vsetvl_e8m8(size);
            __riscv_vse8_v_u8m8(bytes, zeros, step);
            bytes = bytes + step;
            size = size - step;
        }
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
    int forward;
    if (destination_bytes == source_bytes || size == 0ul)
        return;
    forward = (usize)destination_bytes < (usize)source_bytes ||
        (usize)destination_bytes >= (usize)source_bytes + size;

#ifdef __riscv_vector
    if (forward)
    {
        while (size != 0ul)
        {
            usize step = __riscv_vsetvl_e8m8(size);
            __riscv_vse8_v_u8m8(destination_bytes, __riscv_vle8_v_u8m8(source_bytes, step), step);
            destination_bytes = destination_bytes + step;
            source_bytes = source_bytes + step;
            size = size - step;
        }
        return;
    }

    destination_bytes = destination_bytes + size;
    source_bytes = source_bytes + size;
    while (size != 0ul)
    {
        usize step = __riscv_vsetvl_e8m8(size);
        destination_bytes = destination_bytes - step;
        source_bytes = source_bytes - step;
        __riscv_vse8_v_u8m8(destination_bytes, __riscv_vle8_v_u8m8(source_bytes, step), step);
        size = size - step;
    }
#else
#if defined(__x86_64__)
    if (forward)
    {
        usize destination_arg = (usize)destination_bytes;
        usize source_arg = (usize)source_bytes;
        usize length_arg = size;
        __asm__ volatile(
            ".byte 0xf3, 0xa4"
            : [destination] "+{rdi}"(destination_arg), [source] "+{rsi}"(source_arg), [length] "+{rcx}"(length_arg)
            :
            : "memory");
        return;
    }
#elif defined(__i386__)
    if (forward)
    {
        usize destination_arg = (usize)destination_bytes;
        usize source_arg = (usize)source_bytes;
        usize length_arg = size;
        __asm__ volatile(
            ".byte 0xf3, 0xa4"
            : [destination] "+{edi}"(destination_arg), [source] "+{esi}"(source_arg), [length] "+{ecx}"(length_arg)
            :
            : "memory");
        return;
    }
#endif
    {
        // Word moves need both pointers equally misaligned
        int words = (((usize)destination_bytes ^ (usize)source_bytes) & (__SIZEOF_POINTER__ - 1ul)) == 0ul;
        if (forward)
        {
            while (words && size != 0ul && ((usize)destination_bytes & (__SIZEOF_POINTER__ - 1ul)) != 0ul)
            {
                *destination_bytes = *source_bytes;
                destination_bytes = destination_bytes + 1;
                source_bytes = source_bytes + 1;
                size = size - 1ul;
            }
            while (words && size >= __SIZEOF_POINTER__)
            {
                *(usize*)destination_bytes = *(const usize*)source_bytes;
                destination_bytes = destination_bytes + __SIZEOF_POINTER__;
                source_bytes = source_bytes + __SIZEOF_POINTER__;
                size = size - __SIZEOF_POINTER__;
            }
            while (size != 0ul)
            {
                *destination_bytes = *source_bytes;
                destination_bytes = destination_bytes + 1;
                source_bytes = source_bytes + 1;
                size = size - 1ul;
            }
            return;
        }

        destination_bytes = destination_bytes + size;
        source_bytes = source_bytes + size;
        while (words && size != 0ul && ((usize)destination_bytes & (__SIZEOF_POINTER__ - 1ul)) != 0ul)
        {
            destination_bytes = destination_bytes - 1;
            source_bytes = source_bytes - 1;
            *destination_bytes = *source_bytes;
            size = size - 1ul;
        }
        while (words && size >= __SIZEOF_POINTER__)
        {
            destination_bytes = destination_bytes - __SIZEOF_POINTER__;
            source_bytes = source_bytes - __SIZEOF_POINTER__;
            *(usize*)destination_bytes = *(const usize*)source_bytes;
            size = size - __SIZEOF_POINTER__;
        }
        while (size != 0ul)
        {
            destination_bytes = destination_bytes - 1;
            source_bytes = source_bytes - 1;
            *destination_bytes = *source_bytes;
            size = size - 1ul;
        }
    }
#endif
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

#define RH_CONSOLE_BUFFER_SIZE 1024ul

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

// Write barriers are unnecessary for the current GC
// No collection can occur during this copy; callers poll between large chunks
void RhpBulkMoveWithWriteBarrier(void* destination, const void* source, usize length)
{
    rh_memmove(destination, source, length);
}

void RhpMemmove(void* destination, const void* source, usize length)
{
    rh_memmove(destination, source, length);
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
    {
        rh_bytes pattern = __riscv_vmv_v_x_u8m8(fill, __riscv_vsetvlmax_e8m8());
        while (length != 0ul)
        {
            usize step = __riscv_vsetvl_e8m8(length);
            __riscv_vse8_v_u8m8(bytes, pattern, step);
            bytes = bytes + step;
            length = length - step;
        }
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
#elif defined(_WIN32) && defined(__aarch64__)
    usize result;
    __asm__ volatile(
        "sub sp, sp, #16\n"
        "str x30, [sp]\n"
        "ldr x16, __imp_GetCurrentThreadId\n"
        "blr x16\n"
        "ldr x30, [sp]\n"
        "add sp, sp, #16"
        : "={x0}"(result)
        :
        : "x1", "x2", "x3", "x4", "x5", "x6", "x7", "x8", "x9", "x10", "x11", "x12", "x13", "x14", "x15",
        "x16", "x17", "d0", "d1", "d2", "d3", "d4", "d5", "d6", "d7", "memory");
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
#elif defined(__aarch64__)
    usize original;
    usize status;
    __asm__ volatile(
        ".Lrh_cas_retry_%=:\n"
        "ldaxr %[original], [%[address]]\n"
        "cmp %[original], %[comparand]\n"
        "b.ne .Lrh_cas_done_%=\n"
        "stlxr %[status], %[value], [%[address]]\n"
        "cbnz %[status], .Lrh_cas_retry_%=\n"
        ".Lrh_cas_done_%=:"
        : [original] "=&r"(original), [status] "=&r"(status)
        : [address] "r"(address), [comparand] "r"(comparand), [value] "r"(value)
        : "cc", "memory");
    return original;
#endif
}

static void rh_cpu_relax(void)
{
#if defined(__aarch64__)
    __asm__ volatile("yield" : : : "memory");
#elif defined(__x86_64__) || defined(__i386__) || defined(__riscv)
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

// Allocation and marking look a type up per object, so the table is indexed once and only valid types enter it
static void rh_build_type_index(void)
{
    usize capacity = 16ul;
    usize bytes;
    usize i = 0ul;
    while (capacity < rh_type_info_count * 2ul)
        capacity = capacity * 2ul;
    bytes = rh_align_up(capacity * sizeof(const RhTypeInfo*), RH_PAGE_SIZE);
    rh_type_index = (const RhTypeInfo**)rh_os_reserve(bytes);
    if (rh_type_index == (const RhTypeInfo**)0 || !rh_os_commit((void*)rh_type_index, bytes))
        RhpFallbackFailFast(144);
    rh_type_index_mask = capacity - 1ul;

    while (i < rh_type_info_count)
    {
        const RhTypeInfo* info = &rh_type_infos[i];
        usize slot;
        i = i + 1ul;
        if (!rh_type_info_is_valid(info))
            continue;
        slot = ((usize)info->type >> 3) & rh_type_index_mask;
        while (rh_type_index[slot] != (const RhTypeInfo*)0 && rh_type_index[slot]->type != info->type)
            slot = (slot + 1ul) & rh_type_index_mask;
        if (rh_type_index[slot] == (const RhTypeInfo*)0)
            rh_type_index[slot] = info;
    }
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
    usize static_root_count,
    const RhCodeRegion* regions,
    usize region_count)
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
        sizeof(RhSavedRegister) != __SIZEOF_POINTER__ * 3ul ||
        sizeof(RhEhClause) != __SIZEOF_POINTER__ * 10ul ||
        sizeof(RhEhInfo) != __SIZEOF_POINTER__ * 2ul ||
        sizeof(RhCodeRegion) != __SIZEOF_POINTER__ * 10ul ||
        sizeof(RhUnwindContext) != __SIZEOF_POINTER__ * (3ul + RH_UNWIND_REGISTER_COUNT) ||
        stack_base == (void*)0 ||
        (safe_point_count != 0ul && safe_points == (const RhSafePoint*)0) ||
        (type_info_count != 0ul && type_infos == (const RhTypeInfo*)0) ||
        (static_root_count != 0ul && static_roots == (const RhStaticRoot*)0) ||
        (region_count != 0ul && regions == (const RhCodeRegion*)0))
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
    rh_regions = regions;
    rh_region_count = region_count;
    RhpCurrentReturnAddress = (const void*)0;
    RhpCurrentStackPointer = 0ul;
    RhpCurrentFramePointer = (void*)0;
    rh_dispatches = (RhDispatchRecord*)0;
    rh_build_type_index();
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

static void rh_resolve_interior_batch(void);

// An interior pointer finds its object by walking the heap, so they are gathered and resolved in one sorted walk
static void rh_mark_interior(void* interior)
{
    u8* target = (u8*)interior;
    if (target < rh_heap_base + RH_BLOCK_HEADER_SIZE + SYNC_BLOCK_SIZE || target >= rh_heap_used)
        return;
    if (rh_interior_batch_count == RH_INTERIOR_BATCH_SIZE)
        rh_resolve_interior_batch();
    rh_interior_batch[rh_interior_batch_count] = interior;
    rh_interior_batch_count = rh_interior_batch_count + 1ul;
}

static void rh_resolve_interior_batch(void)
{
    usize count = rh_interior_batch_count;
    usize gap = count / 2ul;
    usize i;
    u8* scan = rh_heap_base;
    rh_interior_batch_count = 0ul;

    while (gap != 0ul)
    {
        for (i = gap; i < count; i = i + 1ul)
        {
            void* value = rh_interior_batch[i];
            usize j = i;
            while (j >= gap && (usize)rh_interior_batch[j - gap] > (usize)value)
            {
                rh_interior_batch[j] = rh_interior_batch[j - gap];
                j = j - gap;
            }
            rh_interior_batch[j] = value;
        }
        gap = gap / 2ul;
    }

    i = 0ul;
    while (i < count && scan < rh_heap_used)
    {
        RhBlock* block = (RhBlock*)scan;
        u8* block_end;
        rh_validate_block(scan, block, 138);
        block_end = scan + block->size;
        while (i < count && (u8*)rh_interior_batch[i] < block_end)
        {
            if (block->kind == RH_BLOCK_OBJECT)
            {
                RhObject* object = rh_object_from_block(block);
                u8* object_end = (u8*)block + RH_BLOCK_HEADER_SIZE + rh_gc_object_size(object, block, 138);
                u8* target = (u8*)rh_interior_batch[i];
                if (target >= (u8*)object && target < object_end)
                    rh_mark_object((void*)object);
            }
            i = i + 1ul;
        }
        scan = block_end;
    }
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

static void rh_mark_frame_roots(RhUnwindContext* context)
{
    usize depth = 0ul;
    while (depth < RH_UNWIND_DEPTH_LIMIT)
    {
        const RhSafePoint* safe_point = rh_find_safe_point(context->ip);
        usize fp = context->fp;
        usize i = 0ul;
        if (safe_point == (const RhSafePoint*)0 || fp == 0ul || fp > rh_stack_base)
            RhpFallbackFailFast(143);
        if (safe_point->root_count != 0ul && safe_point->roots == (const RhRoot*)0)
            RhpFallbackFailFast(144);
        while (i < safe_point->root_count)
        {
            const RhRoot* root = &safe_point->roots[i];
            void* value = *(void**)((u8*)fp + root->frame_offset);
            if (root->kind == RH_ROOT_INTERIOR)
                rh_mark_interior(value);
            else if (root->kind == RH_ROOT_OBJECT)
                rh_mark_object(value);
            else
                RhpFallbackFailFast(144);
            i = i + 1ul;
        }

        if (safe_point->region == (const RhCodeRegion*)0 || safe_point->region->kind == RH_REGION_FRAMELESS)
            RhpFallbackFailFast(143);
        (void)rh_unwind_frame(context, safe_point->region);
        if (context->fp == 0ul)
            return;
        if (context->fp < fp || context->fp > rh_stack_base)
            RhpFallbackFailFast(142);
        depth = depth + 1ul;
    }
    RhpFallbackFailFast(144);
}

// A throw site outside any try records no roots; the frames above it are at calls
static void rh_mark_thrower_roots(const RhUnwindContext* thrower)
{
    RhUnwindContext context = *thrower;
    const RhCodeRegion* region;
    if (rh_find_safe_point(context.ip) == (const RhSafePoint*)0)
    {
        region = rh_find_region(context.ip);
        if (region == (const RhCodeRegion*)0)
            RhpFallbackFailFast(143);
        (void)rh_unwind_frame(&context, region);
        if (context.fp == 0ul)
            return;
    }
    rh_mark_frame_roots(&context);
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

static usize rh_free_bucket(usize size)
{
    usize units = size / RH_HEAP_ALIGNMENT;
    usize bucket = 0ul;
    while (units > 1ul && bucket + 1ul < RH_FREE_BUCKET_COUNT)
    {
        units = units >> 1;
        bucket = bucket + 1ul;
    }
    return bucket;
}

static void rh_free_list_insert(RhBlock* block)
{
    usize bucket = rh_free_bucket(block->size);
    block->kind = RH_BLOCK_FREE;
    block->mark_next = (RhBlock*)0;
    block->flags = 0ul;
    rh_set_free_next(block, rh_free_buckets[bucket]);
    rh_free_buckets[bucket] = block;
}

// One walk sweeps, coalesces and refills the free lists; the run after the last live object returns to the bump region
static void rh_sweep(void)
{
    u8* scan = rh_heap_base;
    u8* last_live_end = rh_heap_base;
    RhBlock* free_run = (RhBlock*)0;
    usize bucket = 0ul;
    while (bucket < RH_FREE_BUCKET_COUNT)
    {
        rh_free_buckets[bucket] = (RhBlock*)0;
        bucket = bucket + 1ul;
    }

    while (scan < rh_heap_used)
    {
        RhBlock* block = (RhBlock*)scan;
        usize size;
        rh_validate_block(scan, block, 145);
        size = block->size;
        if (block->kind == RH_BLOCK_OBJECT && (block->flags & RH_BLOCK_MARK) != 0ul)
        {
            block->mark_next = (RhBlock*)0;
            block->flags = 0ul;
            if (free_run != (RhBlock*)0)
            {
                rh_free_list_insert(free_run);
                free_run = (RhBlock*)0;
            }
            last_live_end = scan + size;
        }
        else
        {
            if (block->kind == RH_BLOCK_OBJECT)
                rh_monitor_destroy_for_object(rh_object_from_block(block));
            if (free_run == (RhBlock*)0)
                free_run = block;
            else
                free_run->size = free_run->size + size;
        }
        scan = scan + size;
    }

    rh_heap_used = last_live_end;
    rh_decommit_unused_tail();
    rh_allocation_debt = 0ul;
    RhpGcPollRequested = 0u;
}

static void rh_collect(RhUnwindContext* context)
{
    if (rh_gc_running != 0)
        return;
    rh_gc_running = 1;
    rh_mark_stack = (RhBlock*)0;
    rh_mark_frame_roots(context);
    rh_mark_static_roots();
    rh_mark_object(rh_delegate_temporary_root);
    {
        const RhDispatchRecord* dispatch = rh_dispatches;
        while (dispatch != (const RhDispatchRecord*)0)
        {
            rh_mark_object(dispatch->exception);
            if (dispatch->filtering != (const RhUnwindContext*)0)
                rh_mark_thrower_roots(dispatch->filtering);
            dispatch = dispatch->next;
        }
    }
    do
    {
        rh_resolve_interior_batch();
        rh_drain_mark_stack();
    } while (rh_interior_batch_count != 0ul);
    rh_sweep();
    rh_gc_running = 0;
}

static void rh_collect_current(void)
{
    RhUnwindContext context;
    usize i = 0ul;
    if (RhpCurrentReturnAddress == (const void*)0 || RhpCurrentFramePointer == (void*)0)
        RhpFallbackFailFast(144);
    context.ip = RhpCurrentReturnAddress;
    context.sp = RhpCurrentStackPointer;
    context.fp = (usize)RhpCurrentFramePointer;
    while (i < RH_UNWIND_REGISTER_COUNT)
    {
        context.registers[i] = 0ul;
        i = i + 1ul;
    }
    rh_collect(&context);
}

// A block in a larger bucket always fits; the request's own bucket is searched only a few blocks deep
static void* rh_try_allocate_from_free_list(usize total)
{
    usize bucket = rh_free_bucket(total);
    usize probes = 0ul;
    RhBlock* previous = (RhBlock*)0;
    RhBlock* block = rh_free_buckets[bucket];
    RhBlock* next;
    u8* address;

    while (block != (RhBlock*)0 && block->size < total && probes < 16ul)
    {
        previous = block;
        block = rh_free_next(block);
        probes = probes + 1ul;
    }
    if (block == (RhBlock*)0 || block->size < total)
    {
        previous = (RhBlock*)0;
        bucket = bucket + 1ul;
        while (bucket < RH_FREE_BUCKET_COUNT && rh_free_buckets[bucket] == (RhBlock*)0)
            bucket = bucket + 1ul;
        if (bucket == RH_FREE_BUCKET_COUNT)
            return (void*)0;
        block = rh_free_buckets[bucket];
    }

    address = (u8*)block;
    rh_validate_block(address, block, 146);
    if (block->kind != RH_BLOCK_FREE)
        RhpFallbackFailFast(146);
    next = rh_free_next(block);
    if (previous == (RhBlock*)0)
        rh_free_buckets[bucket] = next;
    else
        rh_set_free_next(previous, next);

    if (block->size - total >= rh_minimum_block_size())
    {
        RhBlock* tail = (RhBlock*)(address + total);
        tail->size = block->size - total;
        rh_free_list_insert(tail);
        block->size = total;
    }
    block->kind = RH_BLOCK_OBJECT;
    block->mark_next = (RhBlock*)0;
    block->flags = 0ul;
    return (void*)(address + RH_BLOCK_HEADER_SIZE);
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

void* RhpMemberwiseClone(RhObject* source)
{
    const RhMethodTable* type = source->type;
    const RhTypeInfo* info = rh_require_method_table(type, 140);
    usize gc_size;
    void* clone;
    if (info->runtime_kind == RH_TYPE_FIXED)
    {
        gc_size = (usize)type->base_size;
    }
    else
    {
        usize length_offset;
        if (info->runtime_kind != RH_TYPE_SZARRAY && info->runtime_kind != RH_TYPE_STRING)
            RhpFallbackFailFast(140);
        length_offset = info->runtime_kind == RH_TYPE_STRING ? STRING_LENGTH_OFFSET : ARRAY_LENGTH_OFFSET;
        gc_size = rh_variable_gc_size(type, *(int*)((u8*)source + length_offset));
    }
    if (gc_size < SYNC_BLOCK_SIZE + MANAGED_OBJECT_HEADER_SIZE || rh_total_block_size(gc_size) == 0ul)
        RhpFallbackFailFast(140);
    clone = rh_allocate_object(type, gc_size);
    rh_memmove((u8*)clone + MANAGED_OBJECT_HEADER_SIZE,
        (u8*)source + MANAGED_OBJECT_HEADER_SIZE,
        gc_size - SYNC_BLOCK_SIZE - MANAGED_OBJECT_HEADER_SIZE);
    return clone;
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
    rh_memmove(destination, source, (usize)length * 2ul);
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

// The span arrives as a struct whose second register segment leaves the bits above _length undefined.
void* RhpNewStringFromReadOnlySpan(
    const RhMethodTable* type,
    const u16* source,
    usize length_segment)
{
    int length = (int)length_segment;
    void* object;
    if (length < 0 || (source == (const u16*)0 && length != 0))
        RhpFallbackFailFast(147);
    object = RhpNewArray(type, length);
    if (length != 0)
        rh_copy_utf16((u16*)((u8*)object + STRING_FIRST_CHAR_OFFSET), source, length);
    return object;
}

// The native GC never moves objects, so an address mix is a stable identity hash for the object's lifetime
int RhpGetObjectHashCode(const RhObject* object)
{
    usize bits = (usize)object;
    u32 hash = (u32)(bits >> 3) ^ (u32)((bits >> 3) >> 16);
    hash = hash * 0x9E3779B1u;
    hash = (hash ^ (hash >> 15)) & 0x7fffffffu;
    return hash == 0u ? 1 : (int)hash;
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

// Each call reaches the stream as one write, or one per buffer when the text does not fit
void RhpConsoleWriteUtf16(const u16* text, int length)
{
    u8 buffer[RH_CONSOLE_BUFFER_SIZE];
    usize used = 0ul;
    int index = 0;
    if (text == (const u16*)0 || length <= 0)
        return;

    while (index < length)
    {
        u32 scalar;
        if (used > RH_CONSOLE_BUFFER_SIZE - 4ul)
        {
            rh_write_all(buffer, used);
            used = 0ul;
        }

        scalar = (u32)text[index];
        index = index + 1;
        if (scalar < 0x80u)
        {
            buffer[used] = (u8)scalar;
            used = used + 1ul;
            continue;
        }

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

        used = used + rh_encode_utf8(scalar, buffer + used);
    }

    rh_write_all(buffer, used);
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
