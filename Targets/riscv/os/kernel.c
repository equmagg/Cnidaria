typedef unsigned char u8;
typedef unsigned short u16;
typedef unsigned int u32;
typedef unsigned long long u64;
typedef signed long long s64;
typedef unsigned long usize;

#if __riscv_vector
typedef __rvv_uint8m8_t vuint8m8_t;
u64 __riscv_vsetvl_e8m8(u64 avl);
vuint8m8_t __riscv_vle8_v_u8m8(const u8* rs1, u64 vl);
void __riscv_vse8_v_u8m8(u8* rs1, vuint8m8_t vs3, u64 vl);
vuint8m8_t __riscv_vmv_v_i_u8m8(int simm5, u64 vl);
u64 __riscv_vsetvlmax_e8m8(void);
#endif

#define NULL ((void*)0)
#define RAM_BASE 0x80000000ul
#define RAM_LIMIT 0x88000000ul
#define PAGE_SIZE 4096ul
#define PAGE_MASK 4095ul
#define KERNEL_LOAD_BASE 0x80400000ul
#define KERNEL_RESERVED_END 0x80800000ul
#define KERNEL_STACK_TOP 0x88000000ul
#define KERNEL_STACK_RESERVE_SIZE 0x00400000ul
#define USER_ELF_BUFFER 0x82000000ul
#define USER_ELF_BUFFER_SIZE 0x01000000ul
#define USER_INTERP_BUFFER 0x83000000ul
#define USER_INTERP_BUFFER_SIZE 0x00400000ul
#define USER_MMAP_BASE 0x3000000000ul
#define USER_HOST_BRIDGE_BASE 0x2F00000000ul
#define USER_FRAMEBUFFER_BASE 0x2E00000000ul
#define HOST_BRIDGE_WINDOW_SIZE 0x1000ul
#define USER_STACK_TOP 0x4000000000ul
#define USER_STACK_SIZE 0x00100000ul
#define USER_VA_LIMIT 0x4000000000ul
#define VIRTIO_MMIO_DEFAULT_BASE 0x10001000ul
#define UART_MMIO_BASE 0x10000000ul
#define CLINT_MMIO_BASE 0x02000000ul
#define PLIC_MMIO_BASE 0x0c000000ul
#define PTE_V 0x001ul
#define PTE_R 0x002ul
#define PTE_W 0x004ul
#define PTE_X 0x008ul
#define PTE_U 0x010ul
#define PTE_A 0x040ul
#define PTE_D 0x080ul
#define PTE_SOFT_NOACCESS 0x100ul
#define SATP_MODE_SV39 0x8000000000000000ul
#define ELF_PT_LOAD 1u
#define ELF_PT_INTERP 3u
#define ELF_PT_PHDR 6u
#define ELF_PF_X 1u
#define ELF_PF_W 2u
#define ELF_PF_R 4u
#define VIRTIO_QUEUE_SIZE 8u
#define SECTOR_SIZE 512u
#define FAT_EOC 0x0ffffff8u
#define FAT_END 0x0fffffffu
#define FAT_READ_ERROR 0xffffffffu
#define SYS_GETCWD 17ul
#define SYS_IOCTL 29ul
#define SYS_OPENAT 56ul
#define SYS_CLOSE 57ul
#define SYS_GETDENTS64 61ul
#define SYS_LSEEK 62ul
#define SYS_READ 63ul
#define SYS_WRITE 64ul
#define SYS_READLINKAT 78ul
#define SYS_NEWFSTATAT 79ul
#define SYS_FSTAT 80ul
#define SYS_EXIT 93ul
#define SYS_EXIT_GROUP 94ul
#define SYS_SET_TID_ADDRESS 96ul
#define SYS_SET_ROBUST_LIST 99ul
#define SYS_NANOSLEEP 101ul
#define SYS_SCHED_YIELD 124ul
#define SYS_RT_SIGACTION 134ul
#define SYS_RT_SIGPROCMASK 135ul
#define SYS_REBOOT 142ul
#define SYS_UNAME 160ul
#define LINUX_REBOOT_MAGIC1 0xfee1deadul
#define LINUX_REBOOT_MAGIC2 0x28121969ul
#define LINUX_REBOOT_MAGIC2A 0x05121996ul
#define LINUX_REBOOT_MAGIC2B 0x16041998ul
#define LINUX_REBOOT_MAGIC2C 0x20112000ul
#define LINUX_REBOOT_CMD_RESTART 0x01234567ul
#define LINUX_REBOOT_CMD_HALT 0xcdef0123ul
#define LINUX_REBOOT_CMD_CAD_ON 0x89abcdeful
#define LINUX_REBOOT_CMD_CAD_OFF 0x00000000ul
#define LINUX_REBOOT_CMD_POWER_OFF 0x4321fedcul
#define LINUX_REBOOT_CMD_RESTART2 0xa1b2c3d4ul
#define SYS_GETPID 172ul
#define SYS_GETPPID 173ul
#define SYS_GETUID 174ul
#define SYS_GETEUID 175ul
#define SYS_GETGID 176ul
#define SYS_GETEGID 177ul
#define SYS_GETTID 178ul
#define SYS_BRK 214ul
#define SYS_MUNMAP 215ul
#define SYS_CLONE 220ul
#define SYS_EXECVE 221ul
#define SYS_MMAP 222ul
#define SYS_MPROTECT 226ul
#define SYS_WAIT4 260ul
#define SYS_PRLIMIT64 261ul
#define AT_NULL 0ul
#define AT_PAGESZ 6ul
#define AT_ENTRY 9ul
#define AT_PHENT 4ul
#define AT_PHNUM 5ul
#define AT_PHDR 3ul
#define AT_BASE 7ul
#define AT_HOST_BRIDGE 0x1000ul
#define AT_FRAMEBUFFER 0x1001ul
#define MAX_OPEN_FILES 32u
#define PATH_BUFFER_SIZE 128u
#define VFS_NODE_NONE 0u
#define VFS_NODE_CONSOLE 1u
#define VFS_NODE_NULL 2u
#define VFS_NODE_ZERO 3u
#define VFS_NODE_FAT_FILE 4u
#define VFS_NODE_ROOT_DIR 5u
#define VFS_NODE_DEV_DIR 6u
#define VFS_NODE_FAT_DIR 7u
#define FAT_ATTRIBUTE_DIRECTORY 16u
#define O_ACCMODE 3ul
#define O_RDONLY 0ul
#define O_WRONLY 1ul
#define O_CREAT 64ul
#define O_EXCL 128ul
#define O_TRUNC 512ul
#define O_APPEND 1024ul
#define SYS_UNLINKAT 35ul
#define SYS_MKDIRAT 34ul
#define SYS_CHDIR 49ul
#define AT_REMOVEDIR 0x200ul
#define O_RDWR 2ul
#define O_NONBLOCK 2048ul
#define AT_FDCWD ((u64)-100l)
#define AT_EMPTY_PATH 0x1000ul
#define SEEK_SET 0ul
#define SEEK_CUR 1ul
#define SEEK_END 2ul
#define S_IFCHR 8192u
#define S_IFDIR 16384u
#define S_IFREG 32768u
#define DT_DIR 4u
#define DT_CHR 2u
#define DT_REG 8u
#define PROT_READ 1ul
#define PROT_WRITE 2ul
#define PROT_EXEC 4ul
#define MAP_PRIVATE 2ul
#define MAP_FIXED 16ul
#define MAP_ANONYMOUS 32ul
#define TIOCGWINSZ 0x5413ul
#define TCGETS 0x5401ul
#define TCSETS 0x5402ul
#define TCSETSW 0x5403ul
#define TCSETSF 0x5404ul
#define MAX_PROCESSES 16u
#define TRAP_FRAME_VECTOR_WORDS 256u
#define MAX_VECTOR_REGISTER_BYTES 64u
#define MAX_VM_REGIONS 64u
#define MAX_EXEC_ARGS 16u
#define MAX_EXEC_ARG_BYTES 512u
#define PROC_UNUSED 0u
#define PROC_RUNNABLE 1u
#define PROC_RUNNING 2u
#define PROC_WAITING 3u
#define PROC_ZOMBIE 4u
#define PROC_VFORK 5u
#define DEFAULT_TIME_SLICE 4u
#define TIMER_INTERVAL 50000ul
#define CLINT_MTIME_OFFSET 0xbff8ul
#define SIE_STIE 32ul
#define SSTATUS_SIE 2ul
#define SSTATUS_SPIE 32ul
#define SSTATUS_SPP 256ul
#define SSTATUS_VS 0x600ul
#define WNOHANG 1ul
#define SIGCHLD 17ul
#define SIGILL 4ul
#define SIGTRAP 5ul
#define SIGBUS 7ul
#define SIGSEGV 11ul
#define CLONE_VM 0x00000100ul
#define CLONE_FS 0x00000200ul
#define CLONE_FILES 0x00000400ul
#define CLONE_SIGHAND 0x00000800ul
#define CLONE_VFORK 0x00004000ul
#define CLONE_THREAD 0x00010000ul
#define CLONE_SETTLS 0x00080000ul
#define CLONE_PARENT_SETTID 0x00100000ul
#define CLONE_CHILD_CLEARTID 0x00200000ul
#define CLONE_CHILD_SETTID 0x01000000ul

struct trap_frame
{
    u64 x[32];
    u64 sepc;
    u64 sstatus;
    u64 scause;
    u64 stval;
    u64 vtype;
    u64 vl;
    u64 vstart;
    u64 vreserved;
    u64 v[TRAP_FRAME_VECTOR_WORDS];
};

struct boot_device
{
    u64 virtio_blk_base;
    u64 uart_base;
    u64 ram_base;
    u64 ram_size;
    u64 host_bridge_base;
    u64 framebuffer_base;
    u64 framebuffer_size;
    u64 framebuffer_window;
};

struct fat32_volume
{
    u32 partition_lba;
    u32 fat_lba;
    u32 data_lba;
    u32 root_cluster;
    u32 sectors_per_cluster;
    u32 fat_sectors;
    u32 fat_count;
    u32 cluster_count;
};

struct elf_image
{
    u64 entry;
    u64 start_entry;
    u64 interp_base;
    u64 phdr;
    u64 phent;
    u64 phnum;
    u64 brk_start;
    char interp[PATH_BUFFER_SIZE];
};

struct exec_arguments
{
    u32 count;
    u32 environment_count;
    u32 bytes_used;
    u32 offsets[MAX_EXEC_ARGS];
    u32 environment_offsets[MAX_EXEC_ARGS];
    char bytes[MAX_EXEC_ARG_BYTES];
};

struct block_device
{
    u64 base;
    u64 sector_count;
    int present;
};

struct virtq_descriptor
{
    u64 address;
    u32 length;
    u16 flags;
    u16 next;
};

struct virtio_block_request
{
    u32 type;
    u32 reserved;
    u64 sector;
    u8 status;
};

struct vfs_node
{
    u32 type;
    u32 first_cluster;
    u32 size;
    u32 mode;
    u32 entry_lba;
    u32 entry_offset;
};

struct file_descriptor
{
    u32 used;
    u32 flags;
    u64 offset;
    struct vfs_node node;
};

struct path_result
{
    struct vfs_node parent;
    struct vfs_node node;
    char leaf[11];
    u32 has_parent;
    u32 has_node;
    u32 leaf_valid;
};

struct vm_region
{
    u64 start;
    u64 end;
    u64 prot;
    u64 flags;
    u32 used;
};

struct process
{
    u32 used;
    u32 state;
    u32 pid;
    u32 ppid;
    u32 exit_status;
    u32 exit_signal;
    u32 time_slice;
    u32 vfork_parent_pid;
    struct vfs_node cwd;
    char cwd_path[PATH_BUFFER_SIZE];
    u64 root_page_table;
    u64 brk;
    u64 brk_min;
    u64 mmap_cursor;
    u64 wait_pid;
    u64 wait_status_pointer;
    struct trap_frame frame;
    struct file_descriptor files[MAX_OPEN_FILES];
    struct vm_region vm_regions[MAX_VM_REGIONS];
};

extern void kernel_enter_user(u64 entry, u64 stack);

static struct boot_device boot_device;
static struct fat32_volume boot_volume;
static u64 kernel_root_page_table;
static u64 device_root_page_table;
static u64 current_user_root_page_table;
static u64 free_page_cursor;
static u64 free_page_end;
static u64 free_page_list;
static u64 process_brk;
static u64 process_brk_min;
static u64 user_mmap_cursor;
static u64 virtio_avail_index;
static struct virtq_descriptor virtq_desc[VIRTIO_QUEUE_SIZE];
static u16 virtq_avail[2 + VIRTIO_QUEUE_SIZE];
static u16 virtq_used_raw[2 + VIRTIO_QUEUE_SIZE * 4];
static struct virtio_block_request virtio_request;
static u8 sector_buffer[SECTOR_SIZE];
static u8 fat_buffer[SECTOR_SIZE];
static u8 dir_buffer[SECTOR_SIZE];

static u8 dirent_buffer[64];
static u32 fat_buffer_lba;
static u32 dir_buffer_lba;
static u64 fat_buffer_valid;
static u64 dir_buffer_valid;
static struct block_device root_block_device;
static struct process processes[MAX_PROCESSES];
static struct process* current_task;
static struct file_descriptor* open_files;
static u32 current_task_slot;
static u32 next_pid;
static u64 scheduler_ticks;
static int console_ready;
static const char init_path[] = "/init";

static int user_copy_to_writable(u64 root, u64 destination, const void* source, u64 count);
static int copy_user_string(u64 source, char* destination, u32 capacity);
static int vfs_lookup(const char* path, struct vfs_node* node);
static int fat_read_path_to_memory(const char* path, void* destination, u32 max_size, u32* out_size);
static int fat_read_at(struct vfs_node* node, u64 offset, void* destination, u32 count, u32* read_count);
static int load_elf64(const u8* image, u32 image_size, u64 root, struct elf_image* loaded);
static int load_process_image(u64 root, const u8* image, u32 image_size, struct elf_image* loaded);
static s64 capture_exec_arguments(u64 argv, u64 envp, const char* fallback, struct exec_arguments* arguments);
static int make_kernel_arguments(const char* path, struct exec_arguments* arguments);
static int build_user_stack(u64 root, struct elf_image* image, struct exec_arguments* arguments, u64* out_stack);

static u64 align_down(u64 value, u64 alignment)
{
    __asm__ volatile(
        "addi %[alignment], %[alignment], -1\n"
        "andn %[value], %[value], %[alignment]"
        : [value] "+{a0}"(value), [alignment] "+{a1}"(alignment)
        :
        : );
    return value;
}

static u64 align_up(u64 value, u64 alignment)
{
    __asm__ volatile(
        "addi %[alignment], %[alignment], -1\n"
        "add %[value], %[value], %[alignment]\n"
        "andn %[value], %[value], %[alignment]"
        : [value] "+{a0}"(value), [alignment] "+{a1}"(alignment)
        :
        : );
    return value;
}

static void fence_rw(void)
{
    __asm__ volatile("fence rw, rw" : : : "memory");
}

static u8 mmio_read8(u64 address)
{
    __asm__ volatile(
        "lbu %[address], 0(%[address])"
        : [address] "+{a0}"(address)
        :
        : "memory");
    return (u8)address;
}

static void mmio_write8(u64 address, u8 value)
{
    __asm__ volatile(
        "sb %[value], 0(%[address])"
        :
    : [address] "{a0}"(address), [value] "{a1}"((u64)value)
        : "memory");
}

static void sbi_putchar(int ch)
{
    u64 arg0 = (u64)(u8)ch;
    u64 eid = 1ul;
    __asm__ volatile("ecall" : : [arg0] "{a0}"(arg0), [eid] "{a7}"(eid) : "memory");
}

static void sbi_system_reset(u64 reset_type, u64 reset_reason)
{
    u64 fid = 0ul;
    u64 eid = 0x53525354ul;
    __asm__ volatile("ecall" : : [arg0] "{a0}"(reset_type), [arg1] "{a1}"(reset_reason), [fid] "{a6}"(fid), [eid] "{a7}"(eid) : "memory");
}

static void fbcon_putchar(int ch);

static int uart_can_read(void)
{
    u64 value = boot_device.uart_base;
    __asm__ volatile(
        "lbu a1, 5(%[value])\n"
        "andi %[value], a1, 1"
        : [value] "+{a0}"(value)
        :
        : "memory");
    return (int)value;
}

static int uart_try_read(void)
{
    u64 value = boot_device.uart_base;
    __asm__ volatile(
        "lbu a1, 5(%[value])\n"
        "andi a1, a1, 1\n"
        "beq a1, zero, .Luart_try_read_empty_%=\n"
        "lbu %[value], 0(%[value])\n"
        "jal zero, .Luart_try_read_done_%=\n"
        ".Luart_try_read_empty_%=:\n"
        "addi %[value], zero, -1\n"
        ".Luart_try_read_done_%=:"
        : [value] "+{a0}"(value)
        :
        : "memory");
    return (int)value;
}

static int uart_read_blocking(void)
{
    u64 value = boot_device.uart_base;
    __asm__ volatile(
        ".Luart_read_wait_%=:\n"
        "lbu a1, 5(%[value])\n"
        "andi a1, a1, 1\n"
        "beq a1, zero, .Luart_read_wait_%=\n"
        "lbu %[value], 0(%[value])"
        : [value] "+{a0}"(value)
        :
        : "memory");
    return (int)value;
}

static void uart_putchar(int ch)
{
    u64 base = boot_device.uart_base;
    u64 value = (u64)(u8)ch;
    __asm__ volatile(
        ".Luart_putchar_wait_%=:\n"
        "lbu a2, 5(%[base])\n"
        "andi a2, a2, 32\n"
        "beq a2, zero, .Luart_putchar_wait_%=\n"
        "sb %[value], 0(%[base])"
        :
    : [base] "{a0}"(base), [value] "{a1}"(value)
        : "memory");
}

// What a program writes reaches the screen as well; what the kernel says stays on the serial line
static void uart_write_buffer(const u8* data, u64 count)
{
    u64 base = boot_device.uart_base;
    u64 drawn = 0ul;
    while (drawn < count)
    {
        fbcon_putchar((int)data[drawn]);
        drawn = drawn + 1ul;
    }
    __asm__ volatile(
        "beq %[count], zero, .Luart_write_done_%=\n"
        ".Luart_write_next_%=:\n"
        "lbu a3, 0(%[data])\n"
        "addi %[data], %[data], 1\n"
        "addi %[count], %[count], -1\n"
        "addi a4, zero, 10\n"
        "bne a3, a4, .Luart_write_char_%=\n"
        ".Luart_write_cr_wait_%=:\n"
        "lbu a4, 5(%[base])\n"
        "andi a4, a4, 32\n"
        "beq a4, zero, .Luart_write_cr_wait_%=\n"
        "addi a4, zero, 13\n"
        "sb a4, 0(%[base])\n"
        ".Luart_write_char_%=:\n"
        ".Luart_write_char_wait_%=:\n"
        "lbu a4, 5(%[base])\n"
        "andi a4, a4, 32\n"
        "beq a4, zero, .Luart_write_char_wait_%=\n"
        "sb a3, 0(%[base])\n"
        "bne %[count], zero, .Luart_write_next_%=\n"
        ".Luart_write_done_%=:"
        :
    : [data] "{a0}"(data), [count] "{a1}"(count), [base] "{a2}"(base)
        : "memory");
}

static void console_putchar_raw(int ch)
{
    if (console_ready)
        uart_putchar(ch);
    else
        sbi_putchar(ch);
}

static void console_putchar(int ch)
{
    if (ch == '\n')
        console_putchar_raw('\r');
    console_putchar_raw(ch);
}

static void console_init(void)
{
    mmio_write8(boot_device.uart_base + 1ul, 0u);
    mmio_write8(boot_device.uart_base + 3ul, 3u);
    mmio_write8(boot_device.uart_base + 2ul, 7u);
    console_ready = 1;
}

static void puts(const char* text)
{
    while (*text != 0)
    {
        console_putchar((int)*text);
        text = text + 1;
    }
}

static void put_hex_nibble(u64 value)
{
    value = value & 15ul;
    if (value < 10ul)
        console_putchar((int)('0' + value));
    else
        console_putchar((int)('a' + value - 10ul));
}

static void put_hex64(u64 value)
{
    int shift = 60;
    puts("0x");
    while (shift >= 0)
    {
        put_hex_nibble(value >> (u64)shift);
        shift = shift - 4;
    }
}

static void put_dec(u64 value)
{
    char buffer[21];
    int index = 20;
    buffer[index] = 0;
    if (value == 0)
    {
        console_putchar('0');
        return;
    }
    while (value != 0 && index > 0)
    {
        u64 digit = value % 10ul;
        index = index - 1;
        buffer[index] = (char)('0' + digit);
        value = value / 10ul;
    }
    puts(buffer + index);
}

static void halt(void)
{
    sbi_system_reset(0ul, 1ul);
    for (;;)
        __asm__ volatile("wfi" : : : "memory");
}

static void panic(const char* text)
{
    puts("kernel: panic: ");
    puts(text);
    puts("\n");
    halt();
}

static void mem_copy(void* dst, const void* src, u64 count)
{
    u8* d = (u8*)dst;
    const u8* s = (const u8*)src;

#if __riscv_vector
    while (count != 0ul)
    {
        u64 vl = __riscv_vsetvl_e8m8(count);
        __riscv_vse8_v_u8m8(d, __riscv_vle8_v_u8m8(s, vl), vl);
        d = d + vl;
        s = s + vl;
        count = count - vl;
    }
#else
    while (count != 0ul && ((((u64)d | (u64)s) & 7ul) != 0ul))
    {
        *d = *s;
        d = d + 1;
        s = s + 1;
        count = count - 1ul;
    }

    u64* dwords = (u64*)d;
    const u64* swords = (const u64*)s;

    while (count >= 64ul)
    {
        dwords[0] = swords[0];
        dwords[1] = swords[1];
        dwords[2] = swords[2];
        dwords[3] = swords[3];
        dwords[4] = swords[4];
        dwords[5] = swords[5];
        dwords[6] = swords[6];
        dwords[7] = swords[7];
        dwords = dwords + 8;
        swords = swords + 8;
        count = count - 64ul;
    }

    while (count >= 8ul)
    {
        *dwords = *swords;
        dwords = dwords + 1;
        swords = swords + 1;
        count = count - 8ul;
    }

    d = (u8*)dwords;
    s = (const u8*)swords;

    while (count != 0ul)
    {
        *d = *s;
        d = d + 1;
        s = s + 1;
        count = count - 1ul;
    }
#endif
}

static void mem_zero(void* dst, u64 count)
{
    u8* bytes = (u8*)dst;

#if __riscv_vector
    vuint8m8_t zero = __riscv_vmv_v_i_u8m8(0, __riscv_vsetvlmax_e8m8());
    while (count != 0ul)
    {
        u64 vl = __riscv_vsetvl_e8m8(count);
        __riscv_vse8_v_u8m8(bytes, zero, vl);
        bytes = bytes + vl;
        count = count - vl;
    }
#else
    while (count != 0ul && (((u64)bytes & 7ul) != 0ul))
    {
        *bytes = 0;
        bytes = bytes + 1;
        count = count - 1ul;
    }

    {
        u64* words = (u64*)bytes;
        while (count >= 8ul)
        {
            *words = 0ul;
            words = words + 1;
            count = count - 8ul;
        }
        bytes = (u8*)words;
    }

    while (count != 0)
    {
        *bytes = 0;
        bytes = bytes + 1;
        count = count - 1ul;
    }
#endif
}

// The console the guest can see
#define FBCON_GLYPH_WIDTH 8ul
#define FBCON_GLYPH_HEIGHT 8ul
#define FBCON_FIRST_GLYPH 32u
#define FBCON_LAST_GLYPH 126u
#define FBCON_MAGIC 0x465542454D415246ul
#define FBCON_FOREGROUND 0x00C8D0D8u

static const u8 fbcon_font[(FBCON_LAST_GLYPH - FBCON_FIRST_GLYPH + 1u) * FBCON_GLYPH_HEIGHT] = {
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, //  
    0x10, 0x10, 0x10, 0x10, 0x10, 0x00, 0x10, 0x00, // !
    0x28, 0x28, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // "
    0x28, 0x28, 0x7C, 0x28, 0x7C, 0x28, 0x28, 0x00, // #
    0x10, 0x3C, 0x50, 0x38, 0x14, 0x78, 0x10, 0x00, // $
    0x60, 0x64, 0x08, 0x10, 0x20, 0x4C, 0x0C, 0x00, // %
    0x30, 0x48, 0x50, 0x20, 0x54, 0x48, 0x34, 0x00, // &
    0x10, 0x10, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, // '
    0x08, 0x10, 0x20, 0x20, 0x20, 0x10, 0x08, 0x00, // (
    0x20, 0x10, 0x08, 0x08, 0x08, 0x10, 0x20, 0x00, // )
    0x00, 0x10, 0x54, 0x38, 0x54, 0x10, 0x00, 0x00, // *
    0x00, 0x10, 0x10, 0x7C, 0x10, 0x10, 0x00, 0x00, // +
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x20, // ,
    0x00, 0x00, 0x00, 0x7C, 0x00, 0x00, 0x00, 0x00, // -
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, // .
    0x04, 0x08, 0x08, 0x10, 0x20, 0x20, 0x40, 0x00, // /
    0x38, 0x44, 0x4C, 0x54, 0x64, 0x44, 0x38, 0x00, // 0
    0x10, 0x30, 0x10, 0x10, 0x10, 0x10, 0x38, 0x00, // 1
    0x38, 0x44, 0x04, 0x08, 0x10, 0x20, 0x7C, 0x00, // 2
    0x7C, 0x08, 0x10, 0x08, 0x04, 0x44, 0x38, 0x00, // 3
    0x08, 0x18, 0x28, 0x48, 0x7C, 0x08, 0x08, 0x00, // 4
    0x7C, 0x40, 0x78, 0x04, 0x04, 0x44, 0x38, 0x00, // 5
    0x18, 0x20, 0x40, 0x78, 0x44, 0x44, 0x38, 0x00, // 6
    0x7C, 0x04, 0x08, 0x10, 0x20, 0x20, 0x20, 0x00, // 7
    0x38, 0x44, 0x44, 0x38, 0x44, 0x44, 0x38, 0x00, // 8
    0x38, 0x44, 0x44, 0x3C, 0x04, 0x08, 0x30, 0x00, // 9
    0x00, 0x00, 0x10, 0x00, 0x00, 0x10, 0x00, 0x00, // :
    0x00, 0x00, 0x10, 0x00, 0x00, 0x10, 0x10, 0x20, // ;
    0x08, 0x10, 0x20, 0x40, 0x20, 0x10, 0x08, 0x00, // <
    0x00, 0x00, 0x7C, 0x00, 0x7C, 0x00, 0x00, 0x00, // =
    0x20, 0x10, 0x08, 0x04, 0x08, 0x10, 0x20, 0x00, // >
    0x38, 0x44, 0x04, 0x08, 0x10, 0x00, 0x10, 0x00, // ?
    0x38, 0x44, 0x5C, 0x54, 0x5C, 0x40, 0x38, 0x00, // @
    0x38, 0x44, 0x44, 0x7C, 0x44, 0x44, 0x44, 0x00, // A
    0x78, 0x44, 0x44, 0x78, 0x44, 0x44, 0x78, 0x00, // B
    0x38, 0x44, 0x40, 0x40, 0x40, 0x44, 0x38, 0x00, // C
    0x70, 0x48, 0x44, 0x44, 0x44, 0x48, 0x70, 0x00, // D
    0x7C, 0x40, 0x40, 0x78, 0x40, 0x40, 0x7C, 0x00, // E
    0x7C, 0x40, 0x40, 0x78, 0x40, 0x40, 0x40, 0x00, // F
    0x38, 0x44, 0x40, 0x5C, 0x44, 0x44, 0x3C, 0x00, // G
    0x44, 0x44, 0x44, 0x7C, 0x44, 0x44, 0x44, 0x00, // H
    0x38, 0x10, 0x10, 0x10, 0x10, 0x10, 0x38, 0x00, // I
    0x1C, 0x08, 0x08, 0x08, 0x08, 0x48, 0x30, 0x00, // J
    0x44, 0x48, 0x50, 0x60, 0x50, 0x48, 0x44, 0x00, // K
    0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x7C, 0x00, // L
    0x44, 0x6C, 0x54, 0x54, 0x44, 0x44, 0x44, 0x00, // M
    0x44, 0x64, 0x54, 0x4C, 0x44, 0x44, 0x44, 0x00, // N
    0x38, 0x44, 0x44, 0x44, 0x44, 0x44, 0x38, 0x00, // O
    0x78, 0x44, 0x44, 0x78, 0x40, 0x40, 0x40, 0x00, // P
    0x38, 0x44, 0x44, 0x44, 0x54, 0x48, 0x34, 0x00, // Q
    0x78, 0x44, 0x44, 0x78, 0x50, 0x48, 0x44, 0x00, // R
    0x3C, 0x40, 0x40, 0x38, 0x04, 0x04, 0x78, 0x00, // S
    0x7C, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x00, // T
    0x44, 0x44, 0x44, 0x44, 0x44, 0x44, 0x38, 0x00, // U
    0x44, 0x44, 0x44, 0x44, 0x44, 0x28, 0x10, 0x00, // V
    0x44, 0x44, 0x44, 0x54, 0x54, 0x6C, 0x44, 0x00, // W
    0x44, 0x44, 0x28, 0x10, 0x28, 0x44, 0x44, 0x00, // X
    0x44, 0x44, 0x28, 0x10, 0x10, 0x10, 0x10, 0x00, // Y
    0x7C, 0x04, 0x08, 0x10, 0x20, 0x40, 0x7C, 0x00, // Z
    0x38, 0x20, 0x20, 0x20, 0x20, 0x20, 0x38, 0x00, // [
    0x40, 0x20, 0x20, 0x10, 0x08, 0x08, 0x04, 0x00, // \/
    0x38, 0x08, 0x08, 0x08, 0x08, 0x08, 0x38, 0x00, // ]
    0x10, 0x28, 0x44, 0x00, 0x00, 0x00, 0x00, 0x00, // ^
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x7C, // _
    0x20, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // `
    0x00, 0x00, 0x38, 0x04, 0x3C, 0x44, 0x3C, 0x00, // a
    0x40, 0x40, 0x78, 0x44, 0x44, 0x44, 0x78, 0x00, // b
    0x00, 0x00, 0x38, 0x40, 0x40, 0x44, 0x38, 0x00, // c
    0x04, 0x04, 0x3C, 0x44, 0x44, 0x44, 0x3C, 0x00, // d
    0x00, 0x00, 0x38, 0x44, 0x7C, 0x40, 0x38, 0x00, // e
    0x18, 0x24, 0x20, 0x70, 0x20, 0x20, 0x20, 0x00, // f
    0x00, 0x00, 0x3C, 0x44, 0x44, 0x3C, 0x04, 0x38, // g
    0x40, 0x40, 0x78, 0x44, 0x44, 0x44, 0x44, 0x00, // h
    0x10, 0x00, 0x30, 0x10, 0x10, 0x10, 0x38, 0x00, // i
    0x08, 0x00, 0x18, 0x08, 0x08, 0x08, 0x48, 0x30, // j
    0x40, 0x40, 0x48, 0x50, 0x60, 0x50, 0x48, 0x00, // k
    0x30, 0x10, 0x10, 0x10, 0x10, 0x10, 0x38, 0x00, // l
    0x00, 0x00, 0x68, 0x54, 0x54, 0x54, 0x54, 0x00, // m
    0x00, 0x00, 0x78, 0x44, 0x44, 0x44, 0x44, 0x00, // n
    0x00, 0x00, 0x38, 0x44, 0x44, 0x44, 0x38, 0x00, // o
    0x00, 0x00, 0x78, 0x44, 0x44, 0x78, 0x40, 0x40, // p
    0x00, 0x00, 0x3C, 0x44, 0x44, 0x3C, 0x04, 0x04, // q
    0x00, 0x00, 0x58, 0x64, 0x40, 0x40, 0x40, 0x00, // r
    0x00, 0x00, 0x3C, 0x40, 0x38, 0x04, 0x78, 0x00, // s
    0x20, 0x20, 0x70, 0x20, 0x20, 0x24, 0x18, 0x00, // t
    0x00, 0x00, 0x44, 0x44, 0x44, 0x44, 0x3C, 0x00, // u
    0x00, 0x00, 0x44, 0x44, 0x44, 0x28, 0x10, 0x00, // v
    0x00, 0x00, 0x44, 0x54, 0x54, 0x54, 0x28, 0x00, // w
    0x00, 0x00, 0x44, 0x28, 0x10, 0x28, 0x44, 0x00, // x
    0x00, 0x00, 0x44, 0x44, 0x44, 0x3C, 0x04, 0x38, // y
    0x00, 0x00, 0x7C, 0x08, 0x10, 0x20, 0x7C, 0x00, // z
    0x18, 0x20, 0x20, 0x60, 0x20, 0x20, 0x18, 0x00, // {
    0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x00, // |
    0x30, 0x08, 0x08, 0x0C, 0x08, 0x08, 0x30, 0x00, // }
    0x00, 0x00, 0x34, 0x4C, 0x00, 0x00, 0x00, 0x00, // ~
};

static volatile u64* fbcon_registers;
static u32* fbcon_pixels;
static u64 fbcon_row_pixels;
static u64 fbcon_columns;
static u64 fbcon_rows;
static u64 fbcon_column;
static u64 fbcon_row;
static int fbcon_cursor_shown;

static void fbcon_init(void)
{
    volatile u64* registers;
    if (boot_device.framebuffer_base == 0ul)
        return;
    registers = (volatile u64*)boot_device.framebuffer_base;
    if (registers[0] != FBCON_MAGIC)
        return;

    fbcon_pixels = (u32*)(boot_device.framebuffer_base + registers[5]);
    fbcon_row_pixels = registers[4] / 4ul;
    fbcon_columns = registers[2] / FBCON_GLYPH_WIDTH;
    fbcon_rows = registers[3] / FBCON_GLYPH_HEIGHT;
    if (fbcon_columns == 0ul || fbcon_rows == 0ul)
        return;

    mem_zero(fbcon_pixels, registers[6]);
    fbcon_registers = registers;
    fbcon_draw_cursor(1);
    fbcon_registers[8] = 1ul;
}

static void fbcon_draw_glyph(u64 column, u64 row, u32 code)
{
    const u8* glyph;
    u32* target;
    u64 line = 0ul;

    if (code < FBCON_FIRST_GLYPH || code > FBCON_LAST_GLYPH)
        code = (u32)'?';
    glyph = fbcon_font + (u64)(code - FBCON_FIRST_GLYPH) * FBCON_GLYPH_HEIGHT;
    target = fbcon_pixels + row * FBCON_GLYPH_HEIGHT * fbcon_row_pixels + column * FBCON_GLYPH_WIDTH;

    while (line < FBCON_GLYPH_HEIGHT)
    {
        u32 bits = (u32)glyph[line];
        u64 pixel = 0ul;
        while (pixel < FBCON_GLYPH_WIDTH)
        {
            target[pixel] = (bits & (0x80u >> pixel)) != 0u ? FBCON_FOREGROUND : 0u;
            pixel = pixel + 1ul;
        }
        target = target + fbcon_row_pixels;
        line = line + 1ul;
    }
}

// The mark that says where the next character will land
static void fbcon_draw_cursor(int shown)
{
    u32* target;
    u64 line = FBCON_GLYPH_HEIGHT - 2ul;
    if (fbcon_column >= fbcon_columns || fbcon_row >= fbcon_rows)
        return;
    target = fbcon_pixels + fbcon_row * FBCON_GLYPH_HEIGHT * fbcon_row_pixels + fbcon_column * FBCON_GLYPH_WIDTH;
    while (line < FBCON_GLYPH_HEIGHT)
    {
        u64 pixel = 0ul;
        while (pixel < FBCON_GLYPH_WIDTH)
        {
            target[line * fbcon_row_pixels + pixel] = shown ? FBCON_FOREGROUND : 0u;
            pixel = pixel + 1ul;
        }
        line = line + 1ul;
    }
    fbcon_cursor_shown = shown;
}

static void fbcon_newline(void)
{
    u64 kept;
    u64 shift;
    fbcon_column = 0ul;
    fbcon_row = fbcon_row + 1ul;
    if (fbcon_row < fbcon_rows)
        return;

    // The screen is full, so everything moves up by one row of glyphs
    fbcon_row = fbcon_rows - 1ul;
    kept = (fbcon_rows - 1ul) * FBCON_GLYPH_HEIGHT * fbcon_row_pixels;
    shift = FBCON_GLYPH_HEIGHT * fbcon_row_pixels;
    mem_copy(fbcon_pixels, fbcon_pixels + shift, kept * 4ul);
    mem_zero(fbcon_pixels + kept, shift * 4ul);
}

static void fbcon_putchar(int ch)
{
    if (fbcon_registers == (volatile u64*)0)
        return;
    if (fbcon_cursor_shown)
        fbcon_draw_cursor(0);

    if (ch == '\n')
    {
        fbcon_newline();
    }
    else if (ch == '\r')
    {
        fbcon_column = 0ul;
    }
    else if (ch == '\b')
    {
        if (fbcon_column != 0ul)
            fbcon_column = fbcon_column - 1ul;
    }
    else if (ch == '\t')
    {
        u64 stop = (fbcon_column + 8ul) & ~7ul;
        while (fbcon_column < stop && fbcon_column < fbcon_columns)
        {
            fbcon_draw_glyph(fbcon_column, fbcon_row, (u32)' ');
            fbcon_column = fbcon_column + 1ul;
        }
        if (fbcon_column >= fbcon_columns)
            fbcon_newline();
    }
    else if (ch >= 32 && ch < 127)
    {
        if (fbcon_column >= fbcon_columns)
            fbcon_newline();
        fbcon_draw_glyph(fbcon_column, fbcon_row, (u32)ch);
        fbcon_column = fbcon_column + 1ul;
    }
    else
    {
        fbcon_draw_cursor(1);
        return;
    }

    fbcon_draw_cursor(1);
    fbcon_registers[8] = 1ul;
}

static u16 le16(const u8* p)
{
    u64 value = (u64)p;
    __asm__ volatile(
        "lbu a1, 0(%[value])\n"
        "lbu a2, 1(%[value])\n"
        "slli a2, a2, 8\n"
        "or %[value], a1, a2"
        : [value] "+{a0}"(value)
        :
        : "memory");
    return (u16)value;
}

static u32 le32(const u8* p)
{
    u64 value = (u64)p;
    __asm__ volatile(
        "lbu a1, 0(%[value])\n"
        "lbu a2, 1(%[value])\n"
        "lbu a3, 2(%[value])\n"
        "lbu a4, 3(%[value])\n"
        "slli a2, a2, 8\n"
        "slli a3, a3, 16\n"
        "slli a4, a4, 24\n"
        "or a1, a1, a2\n"
        "or a3, a3, a4\n"
        "or %[value], a1, a3"
        : [value] "+{a0}"(value)
        :
        : "memory");
    return (u32)value;
}

static u64 le64(const u8* p)
{
    u64 value = (u64)p;
    __asm__ volatile(
        "lbu a1, 0(%[value])\n"
        "lbu a2, 1(%[value])\n"
        "lbu a3, 2(%[value])\n"
        "lbu a4, 3(%[value])\n"
        "lbu a5, 4(%[value])\n"
        "lbu a6, 5(%[value])\n"
        "lbu t0, 6(%[value])\n"
        "lbu t1, 7(%[value])\n"
        "slli a2, a2, 8\n"
        "slli a3, a3, 16\n"
        "slli a4, a4, 24\n"
        "slli a5, a5, 32\n"
        "slli a6, a6, 40\n"
        "slli t0, t0, 48\n"
        "slli t1, t1, 56\n"
        "or a1, a1, a2\n"
        "or a3, a3, a4\n"
        "or a5, a5, a6\n"
        "or t0, t0, t1\n"
        "or a1, a1, a3\n"
        "or a5, a5, t0\n"
        "or %[value], a1, a5"
        : [value] "+{a0}"(value)
        :
        : "memory");
    return value;
}

static u32 be32(const u8* p)
{
    u64 value = (u64)p;
    __asm__ volatile(
        "lbu a1, 0(%[value])\n"
        "lbu a2, 1(%[value])\n"
        "lbu a3, 2(%[value])\n"
        "lbu a4, 3(%[value])\n"
        "slli a1, a1, 24\n"
        "slli a2, a2, 16\n"
        "slli a3, a3, 8\n"
        "or a1, a1, a2\n"
        "or a3, a3, a4\n"
        "or %[value], a1, a3"
        : [value] "+{a0}"(value)
        :
        : "memory");
    return (u32)value;
}

static void store_le16(u8* p, u16 value)
{
    __asm__ volatile(
        "sb %[value], 0(%[p])\n"
        "srli %[value], %[value], 8\n"
        "sb %[value], 1(%[p])"
        :
    : [p] "{a0}"(p), [value] "{a1}"((u64)value)
        : "memory");
}

static void store_le32(u8* p, u32 value)
{
    __asm__ volatile(
        "sb %[value], 0(%[p])\n"
        "srli %[value], %[value], 8\n"
        "sb %[value], 1(%[p])\n"
        "srli %[value], %[value], 8\n"
        "sb %[value], 2(%[p])\n"
        "srli %[value], %[value], 8\n"
        "sb %[value], 3(%[p])"
        :
    : [p] "{a0}"(p), [value] "{a1}"((u64)value)
        : "memory");
}

static void store_le64(u8* p, u64 value)
{
    __asm__ volatile(
        "sb %[value], 0(%[p])\n"
        "srli %[value], %[value], 8\n"
        "sb %[value], 1(%[p])\n"
        "srli %[value], %[value], 8\n"
        "sb %[value], 2(%[p])\n"
        "srli %[value], %[value], 8\n"
        "sb %[value], 3(%[p])\n"
        "srli %[value], %[value], 8\n"
        "sb %[value], 4(%[p])\n"
        "srli %[value], %[value], 8\n"
        "sb %[value], 5(%[p])\n"
        "srli %[value], %[value], 8\n"
        "sb %[value], 6(%[p])\n"
        "srli %[value], %[value], 8\n"
        "sb %[value], 7(%[p])"
        :
    : [p] "{a0}"(p), [value] "{a1}"(value)
        : "memory");
}

static u64 be_cell(const u8* p, u32 cells)
{
    u64 value = 0;
    while (cells != 0)
    {
        value = (value << 32) | (u64)be32(p);
        p = p + 4;
        cells = cells - 1;
    }
    return value;
}

static u32 mmio_read32(u64 base, u32 offset)
{
    u64 address = base + (u64)offset;
    __asm__ volatile(
        "lwu %[address], 0(%[address])"
        : [address] "+{a0}"(address)
        :
        : "memory");
    return (u32)address;
}

static void mmio_write32(u64 base, u32 offset, u32 value)
{
    u64 address = base + (u64)offset;
    __asm__ volatile(
        "sw %[value], 0(%[address])"
        :
    : [address] "{a0}"(address), [value] "{a1}"((u64)value)
        : "memory");
}

static u64 mmio_read64(u64 address)
{
    __asm__ volatile(
        "ld %[address], 0(%[address])"
        : [address] "+{a0}"(address)
        :
        : "memory");
    return address;
}

static u64 ram_end(void)
{
    u64 end = boot_device.ram_base + boot_device.ram_size;
    if (end < boot_device.ram_base)
        return RAM_LIMIT;
    if (end <= RAM_BASE)
        return RAM_BASE;
    if (end > RAM_LIMIT)
        return RAM_LIMIT;
    return end;
}

static int ranges_overlap(u64 a, u64 a_size, u64 b, u64 b_size)
{
    u64 a_end;
    u64 b_end;
    if (a_size == 0ul || b_size == 0ul)
        return 0;
    a_end = a + a_size;
    b_end = b + b_size;
    if (a_end < a || b_end < b)
        return 1;
    return a < b_end && b < a_end;
}

static u64 physical_reserved_limit(u64 address, u64 size)
{
    if (ranges_overlap(address, size, KERNEL_LOAD_BASE, KERNEL_RESERVED_END - KERNEL_LOAD_BASE))
        return KERNEL_RESERVED_END;
    if (ranges_overlap(address, size, USER_ELF_BUFFER, USER_ELF_BUFFER_SIZE))
        return USER_ELF_BUFFER + USER_ELF_BUFFER_SIZE;
    if (ranges_overlap(address, size, USER_INTERP_BUFFER, USER_INTERP_BUFFER_SIZE))
        return USER_INTERP_BUFFER + USER_INTERP_BUFFER_SIZE;
    if (ranges_overlap(address, size, KERNEL_STACK_TOP - KERNEL_STACK_RESERVE_SIZE, KERNEL_STACK_RESERVE_SIZE))
        return KERNEL_STACK_TOP;
    return address;
}

static void memory_manager_init(void)
{
    free_page_cursor = align_up(KERNEL_RESERVED_END, PAGE_SIZE);
    free_page_end = align_down(ram_end(), PAGE_SIZE);
    free_page_list = 0ul;
    if (free_page_cursor >= free_page_end)
        panic("no usable physical memory");
}

static u64 alloc_page_raw(void)
{
    u64 page;
    u64 reserved_limit;
    if (free_page_list != 0ul)
    {
        page = free_page_list;
        free_page_list = *((u64*)page);
        return page;
    }
    for (;;)
    {
        if (free_page_cursor + PAGE_SIZE < free_page_cursor || free_page_cursor + PAGE_SIZE > free_page_end)
            panic("out of physical pages");
        reserved_limit = physical_reserved_limit(free_page_cursor, PAGE_SIZE);
        if (reserved_limit != free_page_cursor)
        {
            free_page_cursor = align_up(reserved_limit, PAGE_SIZE);
            continue;
        }
        page = free_page_cursor;
        free_page_cursor = free_page_cursor + PAGE_SIZE;
        return page;
    }
}

static void free_page_raw(u64 page)
{
    *((u64*)page) = free_page_list;
    free_page_list = page;
}

static u64 alloc_page(void)
{
    u64 page = alloc_page_raw();
    mem_zero((void*)page, PAGE_SIZE);
    return page;
}

static u64 pte_make(u64 physical, u64 flags)
{
    __asm__ volatile(
        "srli %[physical], %[physical], 12\n"
        "slli %[physical], %[physical], 10\n"
        "or %[physical], %[physical], %[flags]\n"
        "ori %[physical], %[physical], 1"
        : [physical] "+{a0}"(physical)
        : [flags] "{a1}"(flags)
        : );
    return physical;
}

static u64 pte_make_noaccess(u64 physical)
{
    return pte_make(physical, PTE_SOFT_NOACCESS) & ~PTE_V;
}

static u64 pte_physical(u64 pte)
{
    __asm__ volatile(
        "srli %[pte], %[pte], 10\n"
        "slli %[pte], %[pte], 12"
        : [pte] "+{a0}"(pte)
        :
        : );
    return pte;
}

static u32 sv39_index(u64 virtual_address, int level)
{
    u64 shift = (u64)level;
    __asm__ volatile(
        "slli a2, %[shift], 3\n"
        "add %[shift], %[shift], a2\n"
        "addi %[shift], %[shift], 12\n"
        "srl %[address], %[address], %[shift]\n"
        "andi %[address], %[address], 511"
        : [address] "+{a0}"(virtual_address), [shift] "+{a1}"(shift)
        :
        : );
    return (u32)virtual_address;
}

static void map_leaf(u64 root, u64 virtual_address, u64 physical_address, u64 flags, int leaf_level)
{
    u64 table = root;
    int level = 2;
    while (level > leaf_level)
    {
        u32 index = sv39_index(virtual_address, level);
        u64* entries = (u64*)table;
        u64 pte = entries[index];
        if ((pte & PTE_V) == 0ul)
        {
            u64 next = alloc_page();
            entries[index] = pte_make(next, 0ul);
            table = next;
        }
        else
        {
            if ((pte & (PTE_R | PTE_W | PTE_X)) != 0ul)
                panic("page table leaf collision");
            table = pte_physical(pte);
        }
        level = level - 1;
    }
    ((u64*)table)[sv39_index(virtual_address, leaf_level)] = pte_make(physical_address, flags);
}

static void map_page(u64 root, u64 virtual_address, u64 physical_address, u64 flags)
{
    map_leaf(root, virtual_address, physical_address, flags, 0);
}

static void map_noaccess_page(u64 root, u64 virtual_address, u64 physical_address)
{
    u64 table = root;
    int level = 2;
    while (level > 0)
    {
        u32 index = sv39_index(virtual_address, level);
        u64* entries = (u64*)table;
        u64 pte = entries[index];
        if ((pte & PTE_V) == 0ul)
        {
            u64 next = alloc_page();
            entries[index] = pte_make(next, 0ul);
            table = next;
        }
        else
        {
            if ((pte & (PTE_R | PTE_W | PTE_X)) != 0ul)
                panic("page table leaf collision");
            table = pte_physical(pte);
        }
        level = level - 1;
    }
    ((u64*)table)[sv39_index(virtual_address, 0)] = pte_make_noaccess(physical_address);
}

static void map_range_2m(u64 root, u64 virtual_address, u64 physical_address, u64 size, u64 flags)
{
    u64 offset = 0ul;
    while (offset < size)
    {
        map_leaf(root, virtual_address + offset, physical_address + offset, flags, 1);
        offset = offset + 0x200000ul;
    }
}

static void map_range_4k(u64 root, u64 virtual_address, u64 physical_address, u64 size, u64 flags)
{
    u64 offset = 0ul;
    while (offset < size)
    {
        map_page(root, virtual_address + offset, physical_address + offset, flags);
        offset = offset + PAGE_SIZE;
    }
}

static int unmap_user_page(u64 root, u64 virtual_address)
{
    u64 table = root;
    int level = 2;
    while (level > 0)
    {
        u64 pte = ((u64*)table)[sv39_index(virtual_address, level)];
        if ((pte & PTE_V) == 0ul)
            return 1;
        if ((pte & (PTE_R | PTE_W | PTE_X)) != 0ul)
            return 0;
        table = pte_physical(pte);
        level = level - 1;
    }
    {
        u64* entries = (u64*)table;
        u32 index = sv39_index(virtual_address, 0);
        u64 pte = entries[index];
        u64 physical;
        if ((pte & PTE_V) == 0ul)
        {
            if ((pte & PTE_SOFT_NOACCESS) == 0ul)
                return 1;
            physical = pte_physical(pte);
            entries[index] = 0ul;
            free_page_raw(physical);
            return 1;
        }
        if ((pte & PTE_U) == 0ul || (pte & (PTE_R | PTE_W | PTE_X)) == 0ul)
            return 0;
        physical = pte_physical(pte);
        entries[index] = 0ul;
        free_page_raw(physical);
    }
    return 1;
}

static int unmap_user_pages(u64 root, u64 start, u64 end)
{
    u64 page = start;
    while (page < end)
    {
        if (!unmap_user_page(root, page))
            return 0;
        page = page + PAGE_SIZE;
    }
    __asm__ volatile("sfence.vma zero, zero" : : : "memory");
    return 1;
}

static void write_satp(u64 value)
{
    __asm__ volatile("csrrw zero, satp, t0\nsfence.vma zero, zero" : : [value] "{t0}"(value) : "memory");
}

static void activate_page_table(u64 root)
{
    write_satp(SATP_MODE_SV39 | (root >> 12));
}

static int user_address_range_valid(u64 address, u64 size)
{
    u64 end;
    if (size == 0ul)
        return 1;
    end = address + size;
    if (end < address)
        return 0;
    if (address < PAGE_SIZE || end > USER_VA_LIMIT)
        return 0;
    if (ranges_overlap(address, size, RAM_BASE, ram_end() - RAM_BASE))
        return 0;
    if (ranges_overlap(address, size, UART_MMIO_BASE, 0x00010000ul))
        return 0;
    if (ranges_overlap(address, size, PLIC_MMIO_BASE, 0x00400000ul))
        return 0;
    return 1;
}

static int user_device_window(u64 address, u64 size)
{
    if (ranges_overlap(address, size, USER_HOST_BRIDGE_BASE, HOST_BRIDGE_WINDOW_SIZE))
        return boot_device.host_bridge_base != 0ul;
    if (ranges_overlap(address, size, USER_FRAMEBUFFER_BASE, boot_device.framebuffer_window))
        return 1;
    return 0;
}

static int user_mapping_range_valid(u64 address, u64 size)
{
    if (!user_address_range_valid(address, size))
        return 0;
    if (ranges_overlap(address, size, USER_STACK_TOP - USER_STACK_SIZE, USER_STACK_SIZE))
        return 0;
    if (user_device_window(address, size))
        return 0;
    return 1;
}

static u64 user_translate(u64 root, u64 virtual_address, u64 required, u64* physical)
{
    __asm__ volatile(
        "srli t0, a1, 38\n"
        "bne t0, zero, .Luser_translate_fail_%=\n"
        "srli t0, a1, 30\n"
        "andi t0, t0, 511\n"
        "sh3add t0, t0, a0\n"
        "ld t0, 0(t0)\n"
        "andi t1, t0, 1\n"
        "beq t1, zero, .Luser_translate_fail_%=\n"
        "andi t1, t0, 6\n"
        "addi t2, zero, 4\n"
        "beq t1, t2, .Luser_translate_fail_%=\n"
        "andi t1, t0, 10\n"
        "bne t1, zero, .Luser_translate_leaf2_%=\n"
        "srli a0, t0, 10\n"
        "slli a0, a0, 12\n"
        "srli t0, a1, 21\n"
        "andi t0, t0, 511\n"
        "sh3add t0, t0, a0\n"
        "ld t0, 0(t0)\n"
        "andi t1, t0, 1\n"
        "beq t1, zero, .Luser_translate_fail_%=\n"
        "andi t1, t0, 6\n"
        "beq t1, t2, .Luser_translate_fail_%=\n"
        "andi t1, t0, 10\n"
        "bne t1, zero, .Luser_translate_leaf1_%=\n"
        "srli a0, t0, 10\n"
        "slli a0, a0, 12\n"
        "srli t0, a1, 12\n"
        "andi t0, t0, 511\n"
        "sh3add t0, t0, a0\n"
        "ld t0, 0(t0)\n"
        "andi t1, t0, 1\n"
        "beq t1, zero, .Luser_translate_fail_%=\n"
        "andi t1, t0, 6\n"
        "beq t1, t2, .Luser_translate_fail_%=\n"
        "andi t1, t0, 10\n"
        "beq t1, zero, .Luser_translate_fail_%=\n"
        "addi t4, zero, 12\n"
        "jal zero, .Luser_translate_leaf_%=\n"
        ".Luser_translate_leaf1_%=:\n"
        "addi t4, zero, 21\n"
        "jal zero, .Luser_translate_leaf_%=\n"
        ".Luser_translate_leaf2_%=:\n"
        "addi t4, zero, 30\n"
        ".Luser_translate_leaf_%=:\n"
        "andi t1, t0, 16\n"
        "beq t1, zero, .Luser_translate_fail_%=\n"
        "andn t1, a2, t0\n"
        "bne t1, zero, .Luser_translate_fail_%=\n"
        "addi t1, t4, -2\n"
        "srl t0, t0, t1\n"
        "sll t0, t0, t4\n"
        "addi t1, zero, 64\n"
        "sub t1, t1, t4\n"
        "sll t3, a1, t1\n"
        "srl t3, t3, t1\n"
        "or t0, t0, t3\n"
        "sd t0, 0(a3)\n"
        "addi a0, zero, 1\n"
        "jal zero, .Luser_translate_done_%=\n"
        ".Luser_translate_fail_%=:\n"
        "addi a0, zero, 0\n"
        ".Luser_translate_done_%=:"
        : [root] "+{a0}"(root)
        : [virtual_address] "{a1}"(virtual_address), [required] "{a2}"(required), [physical] "{a3}"(physical)
        : "memory");
    return root;
}

static int user_copy_to(u64 root, u64 destination, const void* source, u64 count)
{
    const u8* src = (const u8*)source;
    u64 done = 0ul;
    while (done < count)
    {
        u64 physical;
        u64 page_offset;
        u64 chunk;
        if (!user_translate(root, destination + done, 0ul, &physical))
            return 0;
        page_offset = physical & PAGE_MASK;
        chunk = PAGE_SIZE - page_offset;
        if (chunk > count - done)
            chunk = count - done;
        mem_copy((void*)physical, src + done, chunk);
        done = done + chunk;
    }
    return 1;
}

static int user_store_u64(u64 root, u64 destination, u64 value)
{
    return user_copy_to(root, destination, &value, 8ul);
}

static int user_load_u8(u64 root, u64 source, u8* value)
{
    u64 physical;
    if (!user_translate(root, source, PTE_R, &physical))
        return 0;
    *value = *((u8*)physical);
    return 1;
}

static int map_user_page(u64 root, u64 virtual_address, u64 flags)
{
    u64 physical;
    if (!user_mapping_range_valid(virtual_address, PAGE_SIZE))
        return 0;
    physical = alloc_page();
    map_page(root, virtual_address, physical, flags | PTE_U | PTE_A | PTE_D);
    return 1;
}

static int map_user_range(u64 root, u64 start, u64 end, u64 flags)
{
    u64 page = align_down(start, PAGE_SIZE);
    u64 limit = align_up(end, PAGE_SIZE);
    while (page < limit)
    {
        if (!map_user_page(root, page, flags))
            return 0;
        page = page + PAGE_SIZE;
    }
    return 1;
}

static int map_user_stack(u64 root, u64 start)
{
    u64 page = align_down(start, PAGE_SIZE);
    u64 limit = USER_STACK_TOP - USER_STACK_SIZE;
    if (page < limit || page >= USER_STACK_TOP)
        return 0;
    while (page < USER_STACK_TOP)
    {
        u64 physical = alloc_page();
        map_page(root, page, physical, PTE_R | PTE_W | PTE_U | PTE_A | PTE_D);
        page = page + PAGE_SIZE;
    }
    return 1;
}

static int map_user_stack_fault(u64 root, u64 address)
{
    u64 physical;
    u64 existing;
    u64 page;
    if (address < USER_STACK_TOP - USER_STACK_SIZE || address >= USER_STACK_TOP)
        return 0;
    page = align_down(address, PAGE_SIZE);
    if (user_translate(root, page, 0ul, &existing))
        return 0;
    physical = alloc_page();
    map_page(root, page, physical, PTE_R | PTE_W | PTE_U | PTE_A | PTE_D);
    __asm__ volatile("sfence.vma %[page], zero" : : [page] "{t0}"(page) : "memory");
    return 1;
}

static int map_user_brk_fault(u64 root, u64 address)
{
    u64 existing;
    u64 page;
    if (address < process_brk_min || address >= process_brk)
        return 0;
    page = align_down(address, PAGE_SIZE);
    if (user_translate(root, page, 0ul, &existing))
        return 0;
    if (!map_user_page(root, page, PTE_R | PTE_W))
        return 0;
    __asm__ volatile("sfence.vma %[page], zero" : : [page] "{t0}"(page) : "memory");
    return 1;
}

static int user_translate_with_brk_fault(u64 root, u64 address, u64 required, u64* physical)
{
    if (user_translate(root, address, required, physical))
        return 1;
    if (root != current_user_root_page_table || (required & PTE_X) != 0ul)
        return 0;
    if (!map_user_brk_fault(root, address))
        return 0;
    return user_translate(root, address, required, physical);
}

static void map_kernel_address_space(u64 root)
{
    u64 ram_size = align_down(ram_end() - RAM_BASE, 0x200000ul);
    map_range_2m(root, RAM_BASE, RAM_BASE, ram_size, PTE_R | PTE_W | PTE_X | PTE_A | PTE_D);
    map_range_4k(root, boot_device.uart_base, boot_device.uart_base, 0x00010000ul, PTE_R | PTE_W | PTE_A | PTE_D);
    map_range_4k(root, boot_device.virtio_blk_base, boot_device.virtio_blk_base, 0x00001000ul, PTE_R | PTE_W | PTE_A | PTE_D);
    map_range_4k(root, CLINT_MMIO_BASE, CLINT_MMIO_BASE, 0x00010000ul, PTE_R | PTE_W | PTE_A | PTE_D);
    map_range_4k(root, PLIC_MMIO_BASE, PLIC_MMIO_BASE, 0x00400000ul, PTE_R | PTE_W | PTE_A | PTE_D);
    if (boot_device.framebuffer_base != 0ul)
    {
        map_range_2m(root, boot_device.framebuffer_base, boot_device.framebuffer_base,
            boot_device.framebuffer_window, PTE_R | PTE_W | PTE_A | PTE_D);
    }
}

static void copy_4k_mapping_table(u64 destination_root, u64 source_root, u64 virtual_address)
{
    u32 root_index = sv39_index(virtual_address, 2);
    u32 middle_index = sv39_index(virtual_address, 1);
    u64 source_middle_pte = ((u64*)source_root)[root_index];
    u64 source_middle;
    u64 source_leaf_pte;
    u64 destination_middle_pte = ((u64*)destination_root)[root_index];
    u64 destination_middle;
    u64 destination_leaf;
    if ((source_middle_pte & PTE_V) == 0ul || (source_middle_pte & (PTE_R | PTE_W | PTE_X)) != 0ul)
        panic("invalid source mapping root");
    source_middle = pte_physical(source_middle_pte);
    source_leaf_pte = ((u64*)source_middle)[middle_index];
    if ((source_leaf_pte & PTE_V) == 0ul || (source_leaf_pte & (PTE_R | PTE_W | PTE_X)) != 0ul)
        panic("invalid source mapping table");
    if ((destination_middle_pte & PTE_V) == 0ul)
    {
        destination_middle = alloc_page();
        ((u64*)destination_root)[root_index] = pte_make(destination_middle, 0ul);
    }
    else
    {
        if ((destination_middle_pte & (PTE_R | PTE_W | PTE_X)) != 0ul)
            panic("destination mapping root collision");
        destination_middle = pte_physical(destination_middle_pte);
    }
    destination_leaf = alloc_page();
    mem_copy((void*)destination_leaf, (const void*)pte_physical(source_leaf_pte), PAGE_SIZE);
    ((u64*)destination_middle)[middle_index] = pte_make(destination_leaf, 0ul);
}

static void share_device_window(u64 root, u64 virtual_address)
{
    u32 index = sv39_index(virtual_address, 2);
    ((u64*)root)[index] = ((u64*)device_root_page_table)[index];
}

static void device_mappings_init(void)
{
    device_root_page_table = alloc_page();
    if (boot_device.host_bridge_base != 0ul)
    {
        map_page(device_root_page_table, USER_HOST_BRIDGE_BASE, boot_device.host_bridge_base,
            PTE_R | PTE_W | PTE_U | PTE_A | PTE_D);
    }
    if (boot_device.framebuffer_base != 0ul)
    {
        map_range_2m(device_root_page_table, USER_FRAMEBUFFER_BASE, boot_device.framebuffer_base,
            boot_device.framebuffer_window, PTE_R | PTE_W | PTE_U | PTE_A | PTE_D);
    }
}

static u64 create_user_address_space(void)
{
    u64 root = alloc_page();
    u64 ram_size = align_down(ram_end() - RAM_BASE, 0x200000ul);
    map_range_2m(root, RAM_BASE, RAM_BASE, ram_size, PTE_R | PTE_W | PTE_X | PTE_A | PTE_D);
    map_range_4k(root, boot_device.uart_base, boot_device.uart_base, 0x00010000ul, PTE_R | PTE_W | PTE_A | PTE_D);
    map_range_4k(root, boot_device.virtio_blk_base, boot_device.virtio_blk_base, 0x00001000ul, PTE_R | PTE_W | PTE_A | PTE_D);
    if (boot_device.framebuffer_base != 0ul)
    {
        map_range_2m(root, boot_device.framebuffer_base, boot_device.framebuffer_base,
            boot_device.framebuffer_window, PTE_R | PTE_W | PTE_A | PTE_D);
    }
    copy_4k_mapping_table(root, kernel_root_page_table, PLIC_MMIO_BASE);
    copy_4k_mapping_table(root, kernel_root_page_table, PLIC_MMIO_BASE + 0x00200000ul);
    if (boot_device.host_bridge_base != 0ul)
        share_device_window(root, USER_HOST_BRIDGE_BASE);
    if (boot_device.framebuffer_base != 0ul)
        share_device_window(root, USER_FRAMEBUFFER_BASE);
    return root;
}

static void kernel_mmu_init(void)
{
    kernel_root_page_table = alloc_page();
    map_kernel_address_space(kernel_root_page_table);
    activate_page_table(kernel_root_page_table);
}

static u64 timer_now(void)
{
    u64 value;
    __asm__ volatile("csrrs %[value], time, zero" : [value] "=r"(value) : : );
    return value;
}

static void sbi_set_timer(u64 next_time)
{
    u64 fid = 0ul;
    u64 eid = 0x54494d45ul;
    __asm__ volatile("ecall" : : [arg0] "{a0}"(next_time), [fid] "{a6}"(fid), [eid] "{a7}"(eid) : "memory");
}

static void timer_program_next(void)
{
    sbi_set_timer(timer_now() + TIMER_INTERVAL);
}

static void timer_enable(void)
{
    timer_program_next();
    __asm__ volatile("csrrs zero, sie, t0" : : [stie] "{t0}"(SIE_STIE) : "memory");
}

static u64 vector_register_bytes(void)
{
    u64 value;
    __asm__ volatile("csrrs %[value], vlenb, zero" : [value] "=r"(value) : : );
    return value;
}

static void process_clear(struct process* process)
{
    mem_zero(process, sizeof(struct process));
}

static void process_table_init(void)
{
    u32 index = 0u;
    while (index < MAX_PROCESSES)
    {
        process_clear(&processes[index]);
        index = index + 1u;
    }
    next_pid = 1u;
    current_task_slot = 0u;
    current_task = &processes[0];
    process_clear(current_task);
    current_task->used = 1u;
    current_task->state = PROC_RUNNING;
    current_task->pid = next_pid;
    current_task->ppid = 0u;
    current_task->time_slice = DEFAULT_TIME_SLICE;
    // The first process stands at the root, and every later one inherits where its parent stood
    vfs_root_node(&current_task->cwd);
    current_task->cwd_path[0] = '/';
    current_task->cwd_path[1] = 0;
    next_pid = next_pid + 1u;
    open_files = current_task->files;
}

static void process_save_active(struct process* process, struct trap_frame* frame)
{
    if (process == NULL || process->used == 0u)
        return;
    if (frame != NULL)
        mem_copy(&process->frame, frame, sizeof(struct trap_frame));
    process->root_page_table = current_user_root_page_table;
    process->brk = process_brk;
    process->brk_min = process_brk_min;
    process->mmap_cursor = user_mmap_cursor;
}

static void process_load_active(struct process* process, struct trap_frame* frame)
{
    current_task = process;
    current_user_root_page_table = process->root_page_table;
    process_brk = process->brk;
    process_brk_min = process->brk_min;
    user_mmap_cursor = process->mmap_cursor;
    open_files = process->files;
    if (frame != NULL)
        mem_copy(frame, &process->frame, sizeof(struct trap_frame));
    activate_page_table(process->root_page_table);
}

static void process_commit_active(void)
{
    process_save_active(current_task, NULL);
}

static int process_alloc_slot(void)
{
    u32 index = 0u;
    while (index < MAX_PROCESSES)
    {
        if (processes[index].used == 0u)
            return (int)index;
        index = index + 1u;
    }
    return -1;
}

static int scheduler_pick_next(void)
{
    u32 count = 0u;
    u32 index = current_task_slot + 1u;
    if (index >= MAX_PROCESSES)
        index = 0u;
    while (count < MAX_PROCESSES)
    {
        if (processes[index].used != 0u && processes[index].state == PROC_RUNNABLE)
            return (int)index;
        index = index + 1u;
        if (index >= MAX_PROCESSES)
            index = 0u;
        count = count + 1u;
    }
    return -1;
}

static void scheduler_switch(struct trap_frame* frame)
{
    int next;
    if (current_task != NULL)
        process_save_active(current_task, frame);
    next = scheduler_pick_next();
    if (next < 0)
    {
        if (current_task != NULL && current_task->used != 0u && current_task->state == PROC_RUNNABLE)
            next = (int)current_task_slot;
        else
            halt();
    }
    current_task_slot = (u32)next;
    processes[current_task_slot].state = PROC_RUNNING;
    if (processes[current_task_slot].time_slice == 0u)
        processes[current_task_slot].time_slice = DEFAULT_TIME_SLICE;
    process_load_active(&processes[current_task_slot], frame);
}

static void scheduler_yield(struct trap_frame* frame)
{
    if (current_task == NULL)
        return;
    if (current_task->state == PROC_RUNNING)
        current_task->state = PROC_RUNNABLE;
    scheduler_switch(frame);
}

static int copy_user_leaf_pages(u64 dst_root, u64 virtual_address, u64 pte, int level)
{
    u64 flags = pte & (PTE_R | PTE_W | PTE_X | PTE_U | PTE_A | PTE_D);
    u64 leaf_size = 1ul << (12ul + (u64)level * 9ul);
    u64 offset_mask = leaf_size - 1ul;
    u64 source_base = pte_physical(pte) & ~offset_mask;
    u64 offset = 0ul;
    while (offset < leaf_size)
    {
        u64 page_va = virtual_address + offset;
        if (!user_address_range_valid(page_va, PAGE_SIZE))
            return 0;
        u64 page = alloc_page_raw();
        mem_copy((void*)page, (const void*)(source_base + offset), PAGE_SIZE);
        map_page(dst_root, page_va, page, flags);
        offset = offset + PAGE_SIZE;
    }
    return 1;
}

static int copy_user_page_table_level(u64 dst_root, u64 src_table, int level, u64 virtual_prefix)
{
    u32 index = 0u;
    while (index < 512u)
    {
        u64 pte = ((u64*)src_table)[index];
        if ((pte & PTE_V) != 0ul || (level == 0 && (pte & PTE_SOFT_NOACCESS) != 0ul))
        {
            u64 virtual_address = virtual_prefix | ((u64)index << (12ul + (u64)level * 9ul));
            if (virtual_address < USER_VA_LIMIT)
            {
                if (level == 0 && (pte & PTE_SOFT_NOACCESS) != 0ul)
                {
                    u64 page = alloc_page_raw();
                    mem_copy((void*)page, (const void*)pte_physical(pte), PAGE_SIZE);
                    map_noaccess_page(dst_root, virtual_address, page);
                }
                else if ((pte & (PTE_R | PTE_X)) != 0ul)
                {
                    if ((pte & PTE_U) != 0ul && !user_device_window(virtual_address, PAGE_SIZE))
                    {
                        if (!copy_user_leaf_pages(dst_root, virtual_address, pte, level))
                            return 0;
                    }
                }
                else if (level > 0 && !user_device_window(virtual_address, PAGE_SIZE))
                {
                    if (!copy_user_page_table_level(dst_root, pte_physical(pte), level - 1, virtual_address))
                        return 0;
                }
            }
        }
        index = index + 1u;
    }
    return 1;
}

static u64 copy_user_address_space(u64 source_root)
{
    u64 root = create_user_address_space();
    if (!copy_user_page_table_level(root, source_root, 2, 0ul))
        return 0ul;
    return root;
}

static int process_clone(struct trap_frame* frame, u64 flags, u64 child_stack)
{
    int slot;
    struct process* child;
    u64 child_root;
    u64 unsupported = CLONE_VM | CLONE_FS | CLONE_FILES | CLONE_SIGHAND | CLONE_THREAD | CLONE_SETTLS | CLONE_PARENT_SETTID | CLONE_CHILD_CLEARTID | CLONE_CHILD_SETTID;
    if ((flags & unsupported) != 0ul)
        return -22;
    if ((flags & 255ul) != 0ul && (flags & 255ul) != SIGCHLD)
        return -22;
    slot = process_alloc_slot();
    if (slot < 0)
        return -11;
    if ((flags & CLONE_VFORK) != 0ul)
        child_root = current_user_root_page_table;
    else
        child_root = copy_user_address_space(current_user_root_page_table);
    if (child_root == 0ul)
        return -12;
    child = &processes[(u32)slot];
    process_clear(child);
    child->used = 1u;
    child->state = PROC_RUNNABLE;
    child->pid = next_pid;
    child->ppid = current_task->pid;
    child->root_page_table = child_root;
    child->brk = process_brk;
    child->brk_min = process_brk_min;
    child->mmap_cursor = user_mmap_cursor;
    child->time_slice = DEFAULT_TIME_SLICE;
    if ((flags & CLONE_VFORK) != 0ul)
        child->vfork_parent_pid = current_task->pid;
    child->cwd = current_task->cwd;
    mem_copy(child->cwd_path, current_task->cwd_path, (u64)PATH_BUFFER_SIZE);
    mem_copy(child->files, open_files, sizeof(struct file_descriptor) * (u64)MAX_OPEN_FILES);
    mem_copy(child->vm_regions, current_task->vm_regions, sizeof(struct vm_region) * (u64)MAX_VM_REGIONS);
    mem_copy(&child->frame, frame, sizeof(struct trap_frame));
    child->frame.x[10] = 0ul;
    if (child_stack != 0ul)
        child->frame.x[2] = child_stack;
    next_pid = next_pid + 1u;
    return (int)child->pid;
}

static void process_reap(struct process* process)
{
    process_clear(process);
}

static int process_match_wait_pid(struct process* child, u64 wait_pid)
{
    if (wait_pid == 0ul || wait_pid == (u64)-1l)
        return 1;
    if (wait_pid == (u64)child->pid)
        return 1;
    return 0;
}

static int process_store_wait_status(struct process* parent, u64 status_pointer, u32 exit_status, u32 exit_signal)
{
    u32 wait_status = exit_signal != 0u ? exit_signal : exit_status << 8;
    if (status_pointer == 0ul)
        return 1;
    return user_copy_to_writable(parent->root_page_table, status_pointer, &wait_status, 4ul);
}

static int process_try_wait(struct process* parent, u64 pid, u64 status_pointer, s64* result)
{
    u32 index = 0u;
    int has_child = 0;
    while (index < MAX_PROCESSES)
    {
        struct process* child = &processes[index];
        if (child->used != 0u && child->ppid == parent->pid && process_match_wait_pid(child, pid))
        {
            has_child = 1;
            if (child->state == PROC_ZOMBIE)
            {
                u32 child_pid = child->pid;
                u32 exit_status = child->exit_status;
                if (!process_store_wait_status(parent, status_pointer, exit_status, child->exit_signal))
                {
                    *result = -14l;
                    return 1;
                }
                process_reap(child);
                *result = (s64)child_pid;
                return 1;
            }
        }
        index = index + 1u;
    }
    if (!has_child)
    {
        *result = -10l;
        return 1;
    }
    return 0;
}

static void sys_wait4_dispatch(struct trap_frame* frame, u64 pid, u64 status_pointer, u64 options)
{
    s64 result;
    if ((options & ~WNOHANG) != 0ul)
    {
        frame->x[10] = (u64)-22l;
        return;
    }
    if (process_try_wait(current_task, pid, status_pointer, &result))
    {
        frame->x[10] = (u64)result;
        return;
    }
    if ((options & WNOHANG) != 0ul)
    {
        frame->x[10] = 0ul;
        return;
    }
    current_task->wait_pid = pid;
    current_task->wait_status_pointer = status_pointer;
    current_task->state = PROC_WAITING;
    frame->x[10] = (u64)-4l;
    scheduler_switch(frame);
}

static void process_wake_vfork_parent(struct process* child)
{
    u32 index = 0u;
    u32 parent_pid = child->vfork_parent_pid;
    if (parent_pid == 0u)
        return;
    while (index < MAX_PROCESSES)
    {
        struct process* parent = &processes[index];
        if (parent->used != 0u && parent->pid == parent_pid && parent->state == PROC_VFORK)
        {
            parent->state = PROC_RUNNABLE;
            child->vfork_parent_pid = 0u;
            return;
        }
        index = index + 1u;
    }
    child->vfork_parent_pid = 0u;
}

static void process_wake_waiter(struct process* child)
{
    u32 index = 0u;
    while (index < MAX_PROCESSES)
    {
        struct process* parent = &processes[index];
        if (parent->used != 0u && parent->state == PROC_WAITING && parent->pid == child->ppid && process_match_wait_pid(child, parent->wait_pid))
        {
            if (process_store_wait_status(parent, parent->wait_status_pointer, child->exit_status, child->exit_signal))
                parent->frame.x[10] = (u64)child->pid;
            else
                parent->frame.x[10] = (u64)-14l;
            parent->wait_pid = 0ul;
            parent->wait_status_pointer = 0ul;
            parent->state = PROC_RUNNABLE;
            process_reap(child);
            return;
        }
        index = index + 1u;
    }
}

static void process_reparent_children(u32 parent_pid)
{
    u32 index = 0u;
    u32 new_parent = parent_pid == 1u ? 0u : 1u;
    while (index < MAX_PROCESSES)
    {
        if (processes[index].used != 0u && processes[index].ppid == parent_pid)
            processes[index].ppid = new_parent;
        index = index + 1u;
    }
}

static void process_exit_current(struct trap_frame* frame, u64 status)
{
    u32 pid = current_task->pid;
    u64 code = status & 255ul;
    process_reparent_children(pid);
    current_task->exit_status = (u32)code;
    current_task->state = PROC_ZOMBIE;
    // A process that ended the way it meant to says nothing: the console belongs to the terminal
    if (code != 0ul)
    {
        puts("kernel: process ");
        put_dec(pid);
        puts(" exited with status ");
        put_dec(code);
        puts("\n");
    }
    process_wake_vfork_parent(current_task);
    process_wake_waiter(current_task);
    scheduler_switch(frame);
}

// The signal a fault would carry, so a program ends the way it would on the kernel we follow
static u64 fault_signal(u64 cause)
{
    if (cause == 2ul)
        return SIGILL;
    if (cause == 3ul)
        return SIGTRAP;
    if (cause == 0ul || cause == 4ul || cause == 6ul || cause == 1ul || cause == 5ul || cause == 7ul)
        return SIGBUS;
    return SIGSEGV;
}

static void scheduler_timer_interrupt(struct trap_frame* frame)
{
    scheduler_ticks = scheduler_ticks + 1ul;
    timer_program_next();
    if (current_task == NULL || current_task->used == 0u)
        return;
    if (current_task->time_slice > 0u)
        current_task->time_slice = current_task->time_slice - 1u;
    if (current_task->time_slice == 0u)
    {
        current_task->time_slice = DEFAULT_TIME_SLICE;
        scheduler_yield(frame);
    }
}

static s64 sys_execve_impl(struct trap_frame* frame, u64 path_pointer, u64 argv, u64 envp)
{
    char path[PATH_BUFFER_SIZE];
    struct elf_image image;
    struct exec_arguments arguments;
    u32 image_size;
    u64 new_root;
    u64 stack;
    u64 old_root = current_user_root_page_table;
    u64 old_brk = process_brk;
    u64 old_brk_min = process_brk_min;
    u64 old_mmap_cursor = user_mmap_cursor;
    if (!copy_user_string(path_pointer, path, PATH_BUFFER_SIZE))
        return -14l;
    {
        s64 argument_result = capture_exec_arguments(argv, envp, path, &arguments);
        if (argument_result != 0l)
            return argument_result;
    }
    if (!fat_read_path_to_memory(path, (void*)USER_ELF_BUFFER, (u32)USER_ELF_BUFFER_SIZE, &image_size))
        return -2l;
    new_root = create_user_address_space();
    if (!load_process_image(new_root, (const u8*)USER_ELF_BUFFER, image_size, &image))
    {
        current_user_root_page_table = old_root;
        process_brk = old_brk;
        process_brk_min = old_brk_min;
        user_mmap_cursor = old_mmap_cursor;
        return -8l;
    }
    if (!build_user_stack(new_root, &image, &arguments, &stack))
    {
        current_user_root_page_table = old_root;
        process_brk = old_brk;
        process_brk_min = old_brk_min;
        user_mmap_cursor = old_mmap_cursor;
        return -12l;
    }
    current_user_root_page_table = new_root;
    mem_zero(current_task->vm_regions, sizeof(struct vm_region) * (u64)MAX_VM_REGIONS);
    frame->sepc = image.start_entry;
    frame->sstatus = (frame->sstatus & ~SSTATUS_SPP) | SSTATUS_SPIE;
    frame->x[2] = stack;
    frame->x[10] = 0ul;
    current_task->root_page_table = current_user_root_page_table;
    current_task->brk = process_brk;
    current_task->brk_min = process_brk_min;
    current_task->mmap_cursor = user_mmap_cursor;
    process_wake_vfork_parent(current_task);
    activate_page_table(current_user_root_page_table);
    return 0l;
}

static int string_equals(const char* a, const char* b)
{
    while (*a != 0 && *b != 0)
    {
        if (*a != *b)
            return 0;
        a = a + 1;
        b = b + 1;
    }
    return *a == *b;
}

static int prop_contains_string(const u8* data, u32 length, const char* text)
{
    u32 index = 0;
    while (index < length)
    {
        const char* current = (const char*)(data + index);
        u32 len = 0;
        while (index + len < length && current[len] != 0)
            len = len + 1;
        if (string_equals(current, text))
            return 1;
        index = index + len + 1;
    }
    return 0;
}

static const char* fdt_node_name(const char* name)
{
    const char* last = name;
    while (*name != 0)
    {
        if (*name == '/')
            last = name + 1;
        name = name + 1;
    }
    return last;
}

static int fdt_node_unit_name_equals(const char* name, const char* text)
{
    const char* base = fdt_node_name(name);
    while (*text != 0)
    {
        if (*base != *text)
            return 0;
        base = base + 1;
        text = text + 1;
    }
    return *base == 0 || *base == '@';
}

static void parse_fdt(void* fdt)
{
    const u8* base = (const u8*)fdt;
    u32 magic = be32(base);
    u32 off_struct;
    u32 off_strings;
    const u8* structp;
    const char* strings;
    char current_node[64];
    u32 address_cells[16];
    u32 size_cells[16];
    int memory_node[16];
    int virtio_node[16];
    int serial_node[16];
    int host_bridge_node[16];
    int framebuffer_node[16];
    int depth = -1;

    boot_device.virtio_blk_base = VIRTIO_MMIO_DEFAULT_BASE;
    boot_device.uart_base = UART_MMIO_BASE;
    boot_device.ram_base = RAM_BASE;
    boot_device.ram_size = RAM_LIMIT - RAM_BASE;
    boot_device.host_bridge_base = 0ul;
    boot_device.framebuffer_base = 0ul;
    boot_device.framebuffer_size = 0ul;
    boot_device.framebuffer_window = 0ul;

    if (magic != 0xd00dfeedu)
        return;

    off_struct = be32(base + 8);
    off_strings = be32(base + 12);
    structp = base + off_struct;
    strings = (const char*)(base + off_strings);
    current_node[0] = 0;

    for (;;)
    {
        u32 token = be32(structp);
        structp = structp + 4;
        if (token == 1u)
        {
            const char* name = (const char*)structp;
            u32 len = 0;
            u32 copy = 0;
            while (name[len] != 0)
                len = len + 1;
            while (copy < len && copy + 1 < 64u)
            {
                current_node[copy] = name[copy];
                copy = copy + 1;
            }
            current_node[copy] = 0;
            if (depth == 15)
                return;
            depth = depth + 1;
            if (depth == 0)
            {
                address_cells[depth] = 2u;
                size_cells[depth] = 2u;
            }
            else
            {
                address_cells[depth] = address_cells[depth - 1];
                size_cells[depth] = size_cells[depth - 1];
            }
            memory_node[depth] = fdt_node_unit_name_equals(current_node, "memory");
            virtio_node[depth] = 0;
            serial_node[depth] = 0;
            host_bridge_node[depth] = 0;
            framebuffer_node[depth] = 0;
            structp = structp + len + 1;
            structp = (const u8*)align_up((u64)structp, 4ul);
        }
        else if (token == 2u)
        {
            if (depth >= 0)
                depth = depth - 1;
            current_node[0] = 0;
        }
        else if (token == 3u)
        {
            u32 length = be32(structp);
            u32 nameoff = be32(structp + 4);
            const char* prop = strings + nameoff;
            const u8* data = structp + 8;
            if (depth >= 0)
            {
                if (string_equals(prop, "#address-cells") && length >= 4u)
                {
                    u32 cells = be32(data);
                    if (cells <= 2u)
                        address_cells[depth] = cells;
                }
                else if (string_equals(prop, "#size-cells") && length >= 4u)
                {
                    u32 cells = be32(data);
                    if (cells <= 2u)
                        size_cells[depth] = cells;
                }
                else if (string_equals(prop, "compatible") && prop_contains_string(data, length, "virtio,mmio"))
                    virtio_node[depth] = 1;
                else if (string_equals(prop, "compatible") && prop_contains_string(data, length, "ns16550a"))
                    serial_node[depth] = 1;
                else if (string_equals(prop, "compatible") && prop_contains_string(data, length, "cnidaria,host-bridge"))
                    host_bridge_node[depth] = 1;
                else if (string_equals(prop, "compatible") && prop_contains_string(data, length, "cnidaria,framebuffer"))
                    framebuffer_node[depth] = 1;
                else if (string_equals(prop, "device_type") && prop_contains_string(data, length, "memory"))
                    memory_node[depth] = 1;
                else if (string_equals(prop, "reg"))
                {
                    u32 parent_address_cells = depth == 0 ? address_cells[depth] : address_cells[depth - 1];
                    u32 parent_size_cells = depth == 0 ? size_cells[depth] : size_cells[depth - 1];
                    u32 reg_stride = (parent_address_cells + parent_size_cells) * 4u;
                    if (reg_stride != 0u && length >= reg_stride)
                    {
                        u64 reg_base = be_cell(data, parent_address_cells);
                        u64 reg_size = be_cell(data + parent_address_cells * 4u, parent_size_cells);
                        if (virtio_node[depth])
                            boot_device.virtio_blk_base = reg_base;
                        else if (serial_node[depth])
                            boot_device.uart_base = reg_base;
                        else if (host_bridge_node[depth])
                            boot_device.host_bridge_base = reg_base;
                        else if (framebuffer_node[depth])
                        {
                            boot_device.framebuffer_base = reg_base;
                            boot_device.framebuffer_size = reg_size;
                            boot_device.framebuffer_window = align_up(reg_size, 0x200000ul);
                        }
                        else if (memory_node[depth])
                        {
                            boot_device.ram_base = reg_base;
                            boot_device.ram_size = reg_size;
                        }
                    }
                }
            }
            structp = data + length;
            structp = (const u8*)align_up((u64)structp, 4ul);
        }
        else if (token == 4u)
        {
        }
        else if (token == 9u)
        {
            return;
        }
        else
        {
            return;
        }
    }
}

static int virtio_blk_init(void)
{
    u64 base = boot_device.virtio_blk_base;
    u32 queue_max;

    if (mmio_read32(base, 0x000u) != 0x74726976u)
        return 0;
    if (mmio_read32(base, 0x004u) != 2u)
        return 0;
    if (mmio_read32(base, 0x008u) != 2u)
        return 0;

    mmio_write32(base, 0x070u, 0u);
    mmio_write32(base, 0x070u, 1u);
    mmio_write32(base, 0x070u, 3u);
    mmio_write32(base, 0x024u, 0u);
    mmio_write32(base, 0x020u, 0u);
    mmio_write32(base, 0x024u, 1u);
    mmio_write32(base, 0x020u, 0u);
    mmio_write32(base, 0x070u, 11u);
    if ((mmio_read32(base, 0x070u) & 8u) == 0u)
        return 0;

    mmio_write32(base, 0x030u, 0u);
    queue_max = mmio_read32(base, 0x034u);
    if (queue_max < VIRTIO_QUEUE_SIZE)
        return 0;
    mmio_write32(base, 0x038u, VIRTIO_QUEUE_SIZE);
    mmio_write32(base, 0x044u, 0u);

    mem_zero(virtq_desc, sizeof(virtq_desc));
    mem_zero(virtq_avail, sizeof(virtq_avail));
    mem_zero(virtq_used_raw, sizeof(virtq_used_raw));
    mem_zero(&virtio_request, sizeof(virtio_request));
    virtio_avail_index = 0ul;
    virtq_avail[0] = 1u;
    virtq_desc[0].address = (u64)&virtio_request;
    virtq_desc[0].length = 16u;
    virtq_desc[0].flags = 1u;
    virtq_desc[0].next = 1u;
    virtq_desc[1].next = 2u;
    virtq_desc[2].address = (u64)&virtio_request.status;
    virtq_desc[2].length = 1u;
    virtq_desc[2].flags = 2u;
    virtq_desc[2].next = 0u;

    mmio_write32(base, 0x080u, (u32)(u64)virtq_desc);
    mmio_write32(base, 0x084u, (u32)((u64)virtq_desc >> 32));
    mmio_write32(base, 0x090u, (u32)(u64)virtq_avail);
    mmio_write32(base, 0x094u, (u32)((u64)virtq_avail >> 32));
    mmio_write32(base, 0x0a0u, (u32)(u64)virtq_used_raw);
    mmio_write32(base, 0x0a4u, (u32)((u64)virtq_used_raw >> 32));
    mmio_write32(base, 0x044u, 1u);
    mmio_write32(base, 0x070u, 15u);
    return 1;
}

static int virtio_blk_transfer(u64 sector, void* buffer, u32 bytes, u32 type)
{
    u64 base = boot_device.virtio_blk_base;
    u32 sectors;
    u32 transfer_bytes;
    u64 index;

    if (bytes == 0u)
        return 1;

    sectors = (bytes + SECTOR_SIZE - 1u) / SECTOR_SIZE;
    transfer_bytes = sectors * SECTOR_SIZE;
    virtio_request.type = type;
    virtio_request.sector = sector;
    virtio_request.status = 255u;
    virtq_desc[1].address = (u64)buffer;
    virtq_desc[1].length = transfer_bytes;
    virtq_desc[1].flags = type == 0u ? 3u : 1u;

    index = virtio_avail_index;
    virtq_avail[2 + (index & (VIRTIO_QUEUE_SIZE - 1u))] = 0u;
    index = index + 1ul;
    virtio_avail_index = index;
    virtq_avail[1] = (u16)index;

    __asm__ volatile(
        "fence rw, rw\n"
        "sw zero, 80(a0)\n"
        "addi t0, zero, 255\n"
        ".Lvirtio_blk_wait_%=:\n"
        "lbu t1, 0(a1)\n"
        "beq t1, t0, .Lvirtio_blk_wait_%=\n"
        "lwu t1, 96(a0)\n"
        "sw t1, 100(a0)\n"
        "lbu a0, 0(a1)"
        : [base] "+{a0}"(base)
        : [status] "{a1}"(&virtio_request.status)
        : "memory");
    return base == 0ul;
}

static int virtio_blk_read(u64 sector, void* buffer, u32 bytes)
{
    return virtio_blk_transfer(sector, buffer, bytes, 0u);
}

static int virtio_blk_write(u64 sector, void* buffer, u32 bytes)
{
    return virtio_blk_transfer(sector, buffer, bytes, 1u);
}

static u64 virtio_blk_capacity_sectors(void)
{
    u64 low = (u64)mmio_read32(boot_device.virtio_blk_base, 0x100u);
    u64 high = (u64)mmio_read32(boot_device.virtio_blk_base, 0x104u);
    return low | (high << 32);
}

static int block_subsystem_init(void)
{
    mem_zero(&root_block_device, sizeof(root_block_device));
    if (!virtio_blk_init())
        return 0;
    root_block_device.base = boot_device.virtio_blk_base;
    root_block_device.sector_count = virtio_blk_capacity_sectors();
    root_block_device.present = 1;
    return 1;
}

static int block_read(u64 lba, void* buffer, u32 bytes)
{
    u64 sectors;
    if (bytes == 0u)
        return 1;
    if (!root_block_device.present)
        return 0;
    sectors = ((u64)bytes + (u64)SECTOR_SIZE - 1ul) / (u64)SECTOR_SIZE;
    if (lba >= root_block_device.sector_count || sectors > root_block_device.sector_count - lba)
        return 0;
    return virtio_blk_read(lba, buffer, bytes);
}

static int block_read_sector(u64 lba, void* buffer)
{
    return block_read(lba, buffer, SECTOR_SIZE);
}

static int block_write_sector(u64 lba, void* buffer)
{
    if (!root_block_device.present)
        return 0;
    if (lba >= root_block_device.sector_count)
        return 0;
    return virtio_blk_write(lba, buffer, SECTOR_SIZE);
}

static int disk_read_sector(u32 lba, void* buffer)
{
    return block_read_sector((u64)lba, buffer);
}

static int fat_mount(void)
{
    u8* mbr = sector_buffer;
    u8* bpb = sector_buffer;
    int part;
    fat_walk_forget();
    fat_buffer_valid = 0ul;
    dir_buffer_valid = 0ul;
    if (!disk_read_sector(0u, mbr))
        return 0;
    if (le16(mbr + 510) != 0xaa55u)
        return 0;
    part = 0;
    while (part < 4)
    {
        u8* entry = mbr + 446 + part * 16;
        u8 type = entry[4];
        if (type == 0x0bu || type == 0x0cu)
        {
            boot_volume.partition_lba = le32(entry + 8);
            break;
        }
        part = part + 1;
    }
    if (part == 4 || boot_volume.partition_lba == 0u)
        return 0;
    if (!disk_read_sector(boot_volume.partition_lba, bpb))
        return 0;
    if (le16(bpb + 510) != 0xaa55u)
        return 0;
    if (le16(bpb + 11) != SECTOR_SIZE)
        return 0;
    if (le16(bpb + 17) != 0u)
        return 0;
    if (le16(bpb + 22) != 0u)
        return 0;
    boot_volume.sectors_per_cluster = bpb[13];
    boot_volume.fat_sectors = le32(bpb + 36);
    boot_volume.root_cluster = le32(bpb + 44);
    boot_volume.fat_lba = boot_volume.partition_lba + (u32)le16(bpb + 14);
    boot_volume.fat_count = (u32)bpb[16];
    boot_volume.data_lba = boot_volume.fat_lba + boot_volume.fat_sectors * boot_volume.fat_count;
    {
        u32 total = le32(bpb + 32);
        if (total == 0u)
            total = (u32)le16(bpb + 19);
        boot_volume.cluster_count = total > boot_volume.data_lba - boot_volume.partition_lba
            ? (total - (boot_volume.data_lba - boot_volume.partition_lba)) / boot_volume.sectors_per_cluster + 2u
            : 2u;
    }
    if (boot_volume.sectors_per_cluster == 0u || boot_volume.fat_sectors == 0u || boot_volume.root_cluster < 2u)
        return 0;
    return 1;
}

// Every reader and writer of a cached sector comes through here, so the tag never lies
static int fat_sector_load(u32 lba)
{
    if (fat_buffer_valid != 0ul && fat_buffer_lba == lba)
        return 1;
    if (!disk_read_sector(lba, fat_buffer))
        return 0;
    fat_buffer_lba = lba;
    fat_buffer_valid = 1ul;
    return 1;
}

static int dir_sector_load(u32 lba)
{
    if (dir_buffer_valid != 0ul && dir_buffer_lba == lba)
        return 1;
    if (!disk_read_sector(lba, dir_buffer))
        return 0;
    dir_buffer_lba = lba;
    dir_buffer_valid = 1ul;
    return 1;
}

static u32 fat_cluster_lba(u32 cluster)
{
    return boot_volume.data_lba + (cluster - 2u) * boot_volume.sectors_per_cluster;
}

static u32 fat_next_cluster(u32 cluster)
{
    u64 fat_offset = (u64)cluster << 2ul;
    u32 lba = boot_volume.fat_lba + (u32)(fat_offset >> 9ul);
    u32 sector_offset = (u32)(fat_offset & (u64)(SECTOR_SIZE - 1u));
    if (!fat_sector_load(lba))
        return FAT_READ_ERROR;
    return le32(fat_buffer + sector_offset) & 0x0fffffffu;
}

// Writing a sector makes any cached copy of it stale
static int disk_write_sector(u32 lba, void* buffer)
{
    if (fat_buffer_valid != 0ul && fat_buffer_lba == lba)
        fat_buffer_valid = 0ul;
    if (dir_buffer_valid != 0ul && dir_buffer_lba == lba)
        dir_buffer_valid = 0ul;
    return block_write_sector((u64)lba, buffer);
}

// Where the last read of a file left off in its cluster chain
static u32 fat_walk_first_cluster;
static u32 fat_walk_cluster;
static u64 fat_walk_index;

static void fat_walk_forget(void)
{
    fat_walk_first_cluster = 0u;
    fat_walk_cluster = 0u;
    fat_walk_index = 0ul;
}

// A volume keeps more than one copy of its table, and they are kept in step
static int fat_set_cluster(u32 cluster, u32 value)
{
    fat_walk_forget();
    u64 fat_offset = (u64)cluster << 2ul;
    u32 sector = (u32)(fat_offset >> 9ul);
    u32 offset = (u32)(fat_offset & (u64)(SECTOR_SIZE - 1u));
    u32 copy = 0u;
    u32 previous;

    if (sector >= boot_volume.fat_sectors)
        return 0;
    if (!fat_sector_load(boot_volume.fat_lba + sector))
        return 0;
    previous = le32(fat_buffer + offset);
    store_le32(fat_buffer + offset, (previous & 0xf0000000u) | (value & 0x0fffffffu));
    while (copy < boot_volume.fat_count)
    {
        if (!disk_write_sector(boot_volume.fat_lba + copy * boot_volume.fat_sectors + sector, fat_buffer))
            return 0;
        copy = copy + 1u;
    }
    return 1;
}

static u32 fat_allocation_hint = 2u;

static u32 fat_allocate_cluster(void)
{
    u32 limit = boot_volume.cluster_count;
    u32 scanned = 0u;
    u32 cluster = fat_allocation_hint < 2u ? 2u : fat_allocation_hint;

    if (limit < 3u)
        return 0u;
    while (scanned < limit)
    {
        u32 value;
        if (cluster >= limit)
            cluster = 2u;
        value = fat_next_cluster(cluster);
        if (value == FAT_READ_ERROR)
            return 0u;
        if (value == 0u)
        {
            if (!fat_set_cluster(cluster, FAT_END))
                return 0u;
            fat_allocation_hint = cluster + 1u;
            return cluster;
        }
        cluster = cluster + 1u;
        scanned = scanned + 1u;
    }
    return 0u;
}

static int fat_clear_cluster(u32 cluster)
{
    u32 sector_index = 0u;
    mem_zero(sector_buffer, SECTOR_SIZE);
    while (sector_index < boot_volume.sectors_per_cluster)
    {
        if (!disk_write_sector(fat_cluster_lba(cluster) + sector_index, sector_buffer))
            return 0;
        sector_index = sector_index + 1u;
    }
    return 1;
}

static int fat_free_chain(u32 cluster)
{
    while (cluster >= 2u && cluster < FAT_EOC)
    {
        u32 next = fat_next_cluster(cluster);
        if (next == FAT_READ_ERROR)
            return 0;
        if (!fat_set_cluster(cluster, 0u))
            return 0;
        if (cluster < fat_allocation_hint)
            fat_allocation_hint = cluster;
        cluster = next;
    }
    return 1;
}

// Walks a chain to the cluster holding an offset, growing it when it does not reach that far
static int fat_cluster_for_offset(struct vfs_node* node, u64 offset, int grow, u32* out_cluster)
{
    u64 bytes_per_cluster = (u64)boot_volume.sectors_per_cluster * (u64)SECTOR_SIZE;
    u64 wanted = offset / bytes_per_cluster;
    u32 cluster = node->first_cluster;
    u64 index = 0ul;

    if (cluster < 2u || cluster >= FAT_EOC)
    {
        if (!grow)
            return 0;
        cluster = fat_allocate_cluster();
        if (cluster == 0u || !fat_clear_cluster(cluster))
            return 0;
        node->first_cluster = cluster;
    }

    while (index < wanted)
    {
        u32 next = fat_next_cluster(cluster);
        if (next == FAT_READ_ERROR)
            return 0;
        if (next < 2u || next >= FAT_EOC)
        {
            if (!grow)
                return 0;
            next = fat_allocate_cluster();
            if (next == 0u || !fat_clear_cluster(next) || !fat_set_cluster(cluster, next))
                return 0;
        }
        cluster = next;
        index = index + 1ul;
    }
    *out_cluster = cluster;
    return 1;
}

static int fat_write_entry(struct vfs_node* node)
{
    if (node->entry_lba == 0u)
        return 1;
    if (!dir_sector_load(node->entry_lba))
        return 0;
    store_le16(dir_buffer + node->entry_offset + 20u, (u16)(node->first_cluster >> 16));
    store_le16(dir_buffer + node->entry_offset + 26u, (u16)node->first_cluster);
    store_le32(dir_buffer + node->entry_offset + 28u, node->size);
    return disk_write_sector(node->entry_lba, dir_buffer);
}

// Puts bytes into a file, reaching for more clusters when it runs past the end
static int fat_write_at(struct vfs_node* node, u64 offset, const u8* source, u32 count, u32* written)
{
    u64 bytes_per_cluster = (u64)boot_volume.sectors_per_cluster * (u64)SECTOR_SIZE;
    u32 done = 0u;
    *written = 0u;

    while (done < count)
    {
        u64 position = offset + (u64)done;
        u64 within = position % bytes_per_cluster;
        u32 cluster;
        u32 sector_index = (u32)(within / (u64)SECTOR_SIZE);
        u32 sector_offset = (u32)(within % (u64)SECTOR_SIZE);
        u32 chunk = SECTOR_SIZE - sector_offset;
        u32 lba;

        if (chunk > count - done)
            chunk = count - done;
        if (!fat_cluster_for_offset(node, position, 1, &cluster))
            return 0;
        lba = fat_cluster_lba(cluster) + sector_index;

        // A partial sector keeps what it already held
        if (chunk != SECTOR_SIZE)
        {
            if (!disk_read_sector(lba, sector_buffer))
                return 0;
        }
        mem_copy(sector_buffer + sector_offset, source + done, (u64)chunk);
        if (!disk_write_sector(lba, sector_buffer))
            return 0;
        done = done + chunk;
    }

    if (offset + (u64)count > (u64)node->size)
        node->size = (u32)(offset + (u64)count);
    *written = done;
    return fat_write_entry(node);
}

// Looks a name up inside the directory a cluster chain holds
static int fat_find_in(u32 directory, const char* short_name, struct vfs_node* out)
{
    u32 cluster = directory;
    while (cluster >= 2u && cluster < FAT_EOC)
    {
        u32 sector_index = 0u;
        while (sector_index < boot_volume.sectors_per_cluster)
        {
            u32 lba = fat_cluster_lba(cluster) + sector_index;
            u32 offset = 0u;
            if (!dir_sector_load(lba))
                return 0;
            while (offset < SECTOR_SIZE)
            {
                const u8* entry = dir_buffer + offset;
                u8 first = entry[0];
                u8 attributes = entry[11];
                u32 name_index = 0u;
                if (first == 0u)
                    return 0;
                if (first == 0xe5u || (attributes & 15u) == 15u || (attributes & 8u) != 0u)
                {
                    offset = offset + 32u;
                    continue;
                }
                while (name_index < 11u && entry[name_index] == (u8)short_name[name_index])
                    name_index = name_index + 1u;
                if (name_index == 11u)
                {
                    u32 found = ((u32)le16(entry + 20) << 16) | (u32)le16(entry + 26);
                    int directory_entry = (attributes & FAT_ATTRIBUTE_DIRECTORY) != 0u;
                    out->type = directory_entry ? VFS_NODE_FAT_DIR : VFS_NODE_FAT_FILE;
                    // A directory whose entry says cluster zero is the root
                    out->first_cluster = directory_entry && found == 0u ? boot_volume.root_cluster : found;
                    out->size = directory_entry ? 0u : le32(entry + 28);
                    out->mode = directory_entry ? (S_IFDIR | 493u) : (S_IFREG | 438u);
                    out->entry_lba = lba;
                    out->entry_offset = offset;
                    return 1;
                }
                offset = offset + 32u;
            }
            sector_index = sector_index + 1u;
        }
        cluster = fat_next_cluster(cluster);
        if (cluster == FAT_READ_ERROR)
            return 0;
    }
    return 0;
}


// Puts a name in a directory, taking a free slot or making one
static int fat_create_in(u32 directory, const char* short_name, u32 attributes, u32* out_lba, u32* out_offset)
{
    u32 cluster = directory;
    u32 previous = 0u;

    while (cluster >= 2u && cluster < FAT_EOC)
    {
        u32 sector_index = 0u;
        while (sector_index < boot_volume.sectors_per_cluster)
        {
            u32 lba = fat_cluster_lba(cluster) + sector_index;
            u32 offset = 0u;
            if (!dir_sector_load(lba))
                return 0;
            while (offset < SECTOR_SIZE)
            {
                u8 first = dir_buffer[offset];
                if (first == 0u || first == 0xe5u)
                {
                    u32 index = 0u;
                    mem_zero(dir_buffer + offset, 32ul);
                    while (index < 11u)
                    {
                        dir_buffer[offset + index] = (u8)short_name[index];
                        index = index + 1u;
                    }
                    dir_buffer[offset + 11u] = (u8)(attributes != 0u ? attributes : 32u);
                    // A fresh name owns nothing yet, so it has no cluster and no length
                    if (!disk_write_sector(lba, dir_buffer))
                        return 0;
                    *out_lba = lba;
                    *out_offset = offset;
                    return 1;
                }
                offset = offset + 32u;
            }
            sector_index = sector_index + 1u;
        }
        previous = cluster;
        cluster = fat_next_cluster(cluster);
        if (cluster == FAT_READ_ERROR)
            return 0;
    }

    // The directory is full, so it grows by a cluster
    if (previous == 0u)
        return 0;
    cluster = fat_allocate_cluster();
    if (cluster == 0u || !fat_clear_cluster(cluster) || !fat_set_cluster(previous, cluster))
        return 0;
    return fat_create_in(directory, short_name, attributes, out_lba, out_offset);
}

static int fat_remove_in(u32 directory, const char* short_name)
{
    struct vfs_node found;
    if (!fat_find_in(directory, short_name, &found))
        return 0;
    if (!dir_sector_load(found.entry_lba))
        return 0;
    dir_buffer[found.entry_offset] = 0xe5u;
    if (!disk_write_sector(found.entry_lba, dir_buffer))
        return 0;
    return fat_free_chain(found.first_cluster);
}

// A directory holds nothing once its own two names are taken out of the count
static int fat_directory_is_empty(u32 directory)
{
    u32 cluster = directory;
    while (cluster >= 2u && cluster < FAT_EOC)
    {
        u32 sector_index = 0u;
        while (sector_index < boot_volume.sectors_per_cluster)
        {
            u32 lba = fat_cluster_lba(cluster) + sector_index;
            u32 offset = 0u;
            if (!dir_sector_load(lba))
                return 0;
            while (offset < SECTOR_SIZE)
            {
                const u8* entry = dir_buffer + offset;
                u8 first = entry[0];
                u8 attributes = entry[11];
                if (first == 0u)
                    return 1;
                if (first != 0xe5u && (attributes & 15u) != 15u && (attributes & 8u) == 0u)
                {
                    if (!(entry[0] == (u8)'.' && (entry[1] == (u8)' ' || (entry[1] == (u8)'.' && entry[2] == (u8)' '))))
                        return 0;
                }
                offset = offset + 32u;
            }
            sector_index = sector_index + 1u;
        }
        cluster = fat_next_cluster(cluster);
        if (cluster == FAT_READ_ERROR)
            return 0;
    }
    return 1;
}

// A fresh directory knows itself and its parent before it knows anything else
static int fat_make_directory(u32 parent, const char* short_name, struct vfs_node* out)
{
    u32 cluster = fat_allocate_cluster();
    u32 entry_lba;
    u32 entry_offset;
    u32 index;

    if (cluster == 0u || !fat_clear_cluster(cluster))
        return 0;
    if (!fat_create_in(parent, short_name, FAT_ATTRIBUTE_DIRECTORY, &entry_lba, &entry_offset))
        return 0;
    if (!dir_sector_load(entry_lba))
        return 0;
    store_le16(dir_buffer + entry_offset + 20u, (u16)(cluster >> 16));
    store_le16(dir_buffer + entry_offset + 26u, (u16)cluster);
    store_le32(dir_buffer + entry_offset + 28u, 0u);
    if (!disk_write_sector(entry_lba, dir_buffer))
        return 0;

    if (!dir_sector_load(fat_cluster_lba(cluster)))
        return 0;
    mem_zero(dir_buffer, 64ul);
    index = 0u;
    while (index < 11u)
    {
        dir_buffer[index] = (u8)' ';
        dir_buffer[32u + index] = (u8)' ';
        index = index + 1u;
    }
    dir_buffer[0] = (u8)'.';
    dir_buffer[11] = FAT_ATTRIBUTE_DIRECTORY;
    store_le16(dir_buffer + 20, (u16)(cluster >> 16));
    store_le16(dir_buffer + 26, (u16)cluster);
    dir_buffer[32] = (u8)'.';
    dir_buffer[33] = (u8)'.';
    dir_buffer[43] = FAT_ATTRIBUTE_DIRECTORY;
    // The root is written as cluster zero, which is what every volume expects
    store_le16(dir_buffer + 52, (u16)(parent == boot_volume.root_cluster ? 0u : parent >> 16));
    store_le16(dir_buffer + 58, (u16)(parent == boot_volume.root_cluster ? 0u : parent));
    if (!disk_write_sector(fat_cluster_lba(cluster), dir_buffer))
        return 0;

    out->type = VFS_NODE_FAT_DIR;
    out->first_cluster = cluster;
    out->size = 0u;
    out->mode = S_IFDIR | 493u;
    out->entry_lba = entry_lba;
    out->entry_offset = entry_offset;
    return 1;
}

static int user_copy_to_writable(u64 root, u64 destination, const void* source, u64 count)
{
    const u8* src = (const u8*)source;
    u64 done = 0ul;
    while (done < count)
    {
        u64 physical;
        u64 page_offset;
        u64 chunk;
        if (!user_translate_with_brk_fault(root, destination + done, PTE_W, &physical))
            return 0;
        page_offset = physical & PAGE_MASK;
        chunk = PAGE_SIZE - page_offset;
        if (chunk > count - done)
            chunk = count - done;
        mem_copy((void*)physical, src + done, chunk);
        done = done + chunk;
    }
    return 1;
}

static int user_copy_from_readable(u64 root, void* destination, u64 source, u64 count)
{
    u8* dst = (u8*)destination;
    u64 done = 0ul;
    while (done < count)
    {
        u64 physical;
        u64 page_offset;
        u64 chunk;
        if (!user_translate_with_brk_fault(root, source + done, PTE_R, &physical))
            return 0;
        page_offset = physical & PAGE_MASK;
        chunk = PAGE_SIZE - page_offset;
        if (chunk > count - done)
            chunk = count - done;
        mem_copy(dst + done, (const void*)physical, chunk);
        done = done + chunk;
    }
    return 1;
}

static int copy_user_string(u64 source, char* destination, u32 capacity)
{
    u32 index = 0;
    if (capacity == 0u)
        return 0;
    while (index + 1u < capacity)
    {
        u64 physical;
        u64 chunk;
        u64 remaining = (u64)capacity - 1ul - (u64)index;
        u64 offset = 0ul;
        if (!user_translate(current_user_root_page_table, source + (u64)index, PTE_R, &physical))
            return 0;
        chunk = PAGE_SIZE - (physical & PAGE_MASK);
        if (chunk > remaining)
            chunk = remaining;
        while (offset < chunk)
        {
            u8 ch = ((const u8*)physical)[offset];
            destination[index] = (char)ch;
            if (ch == 0u)
                return 1;
            index = index + 1u;
            offset = offset + 1ul;
        }
    }
    destination[capacity - 1u] = 0;
    return 0;
}

static int add_kernel_argument(struct exec_arguments* arguments, const char* text)
{
    u32 offset;
    u32 index = 0u;
    if (arguments->count >= MAX_EXEC_ARGS)
        return 0;
    offset = arguments->bytes_used;
    while (text[index] != 0)
    {
        if (arguments->bytes_used + 1u >= MAX_EXEC_ARG_BYTES)
            return 0;
        arguments->bytes[arguments->bytes_used] = text[index];
        arguments->bytes_used = arguments->bytes_used + 1u;
        index = index + 1u;
    }
    if (arguments->bytes_used >= MAX_EXEC_ARG_BYTES)
        return 0;
    arguments->bytes[arguments->bytes_used] = 0;
    arguments->bytes_used = arguments->bytes_used + 1u;
    arguments->offsets[arguments->count] = offset;
    arguments->count = arguments->count + 1u;
    return 1;
}

static int add_kernel_environment(struct exec_arguments* arguments, const char* text)
{
    u32 offset;
    u32 index = 0u;
    if (arguments->environment_count >= MAX_EXEC_ARGS)
        return 0;
    offset = arguments->bytes_used;
    while (text[index] != 0)
    {
        if (arguments->bytes_used + 1u >= MAX_EXEC_ARG_BYTES)
            return 0;
        arguments->bytes[arguments->bytes_used] = text[index];
        arguments->bytes_used = arguments->bytes_used + 1u;
        index = index + 1u;
    }
    if (arguments->bytes_used >= MAX_EXEC_ARG_BYTES)
        return 0;
    arguments->bytes[arguments->bytes_used] = 0;
    arguments->bytes_used = arguments->bytes_used + 1u;
    arguments->environment_offsets[arguments->environment_count] = offset;
    arguments->environment_count = arguments->environment_count + 1u;
    return 1;
}

// The first process is handed the environment every later one inherits
static int make_kernel_arguments(const char* path, struct exec_arguments* arguments)
{
    mem_zero(arguments, sizeof(struct exec_arguments));
    if (!add_kernel_argument(arguments, path))
        return 0;
    if (!add_kernel_environment(arguments, "PATH=/bin:/"))
        return 0;
    if (!add_kernel_environment(arguments, "HOME=/"))
        return 0;
    if (!add_kernel_environment(arguments, "SHELL=/shell"))
        return 0;
    return add_kernel_environment(arguments, "TERM=cnidaria");
}

// Copies one null terminated vector out of the caller, which is what argv and envp both are
static s64 capture_exec_vector(u64 vector, struct exec_arguments* arguments, u32* offsets, u32* count)
{
    u32 index = 0u;
    *count = 0u;
    if (vector == 0ul)
        return 0l;
    while (index < MAX_EXEC_ARGS)
    {
        u64 source;
        u32 offset;
        u32 string_index = 0u;
        if (!user_copy_from_readable(current_user_root_page_table, &source, vector + (u64)index * 8ul, 8ul))
            return -14l;
        if (source == 0ul)
            return 0l;
        offset = arguments->bytes_used;
        for (;;)
        {
            u8 ch;
            if (arguments->bytes_used >= MAX_EXEC_ARG_BYTES)
                return -7l;
            if (!user_load_u8(current_user_root_page_table, source + (u64)string_index, &ch))
                return -14l;
            arguments->bytes[arguments->bytes_used] = (char)ch;
            arguments->bytes_used = arguments->bytes_used + 1u;
            string_index = string_index + 1u;
            if (ch == 0u)
                break;
        }
        offsets[index] = offset;
        *count = *count + 1u;
        index = index + 1u;
    }
    {
        u64 next;
        if (!user_copy_from_readable(current_user_root_page_table, &next, vector + (u64)index * 8ul, 8ul))
            return -14l;
        if (next != 0ul)
            return -7l;
    }
    return 0l;
}

static s64 capture_exec_arguments(u64 argv, u64 envp, const char* fallback, struct exec_arguments* arguments)
{
    s64 result;
    mem_zero(arguments, sizeof(struct exec_arguments));
    result = capture_exec_vector(argv, arguments, arguments->offsets, &arguments->count);
    if (result < 0l)
        return result;
    if (arguments->count == 0u && !add_kernel_argument(arguments, fallback))
        return -7l;
    return capture_exec_vector(envp, arguments, arguments->environment_offsets, &arguments->environment_count);
}

static int ascii_to_upper(int ch)
{
    if (ch >= 'a' && ch <= 'z')
        return ch - 'a' + 'A';
    return ch;
}

static int fat_name_char_valid(int ch)
{
    if (ch >= 'A' && ch <= 'Z')
        return 1;
    if (ch >= '0' && ch <= '9')
        return 1;
    if (ch == '_' || ch == '$' || ch == '~' || ch == '!' || ch == '#' || ch == '%' || ch == '&')
        return 1;
    if (ch == '-' || ch == '@' || ch == '^' || ch == '`' || ch == '{' || ch == '}' || ch == '(' || ch == ')')
        return 1;
    return 0;
}

static const char* skip_root_slashes(const char* path)
{
    while (*path == '/')
        path = path + 1;
    return path;
}

static int path_is_root(const char* path)
{
    path = skip_root_slashes(path);
    return *path == 0;
}

static int path_equal_literal(const char* path, const char* literal)
{
    while (*path != 0 && *literal != 0)
    {
        if (*path != *literal)
            return 0;
        path = path + 1;
        literal = literal + 1;
    }
    return *path == 0 && *literal == 0;
}

static int path_equal_literal_skip_root(const char* path, const char* literal)
{
    return path_equal_literal(skip_root_slashes(path), literal);
}

static int path_to_fat_short_name(const char* path, char* short_name)
{
    const char* p = skip_root_slashes(path);
    u32 i = 0;
    u32 ext = 0;
    while (i < 11u)
    {
        short_name[i] = ' ';
        i = i + 1u;
    }
    i = 0;
    while (*p != 0 && *p != '.' && *p != '/')
    {
        int ch = ascii_to_upper((int)*p);
        if (i >= 8u || !fat_name_char_valid(ch))
            return 0;
        short_name[i] = (char)ch;
        i = i + 1u;
        p = p + 1;
    }
    if (i == 0u)
        return 0;
    if (*p == '.')
    {
        p = p + 1;
        while (*p != 0 && *p != '/')
        {
            int ch = ascii_to_upper((int)*p);
            if (ext >= 3u || !fat_name_char_valid(ch))
                return 0;
            short_name[8u + ext] = (char)ch;
            ext = ext + 1u;
            p = p + 1;
        }
        if (ext == 0u)
            return 0;
    }
    return *p == 0;
}

static void vfs_root_node(struct vfs_node* node)
{
    node->type = VFS_NODE_ROOT_DIR;
    node->first_cluster = boot_volume.root_cluster;
    node->size = 0u;
    node->mode = S_IFDIR | 493u;
    node->entry_lba = 0u;
    node->entry_offset = 0u;
}

static void vfs_device_node(struct vfs_node* node, u32 type)
{
    node->type = type;
    node->first_cluster = 0u;
    node->size = 0u;
    node->mode = type == VFS_NODE_DEV_DIR ? (S_IFDIR | 365u) : (S_IFCHR | 438u);
    node->entry_lba = 0u;
    node->entry_offset = 0u;
}

static int component_equals(const char* name, u32 length, const char* literal)
{
    u32 index = 0u;
    while (index < length && literal[index] != 0)
    {
        if (name[index] != literal[index])
            return 0;
        index = index + 1u;
    }
    return index == length && literal[index] == 0;
}

// One name of a path, in the eight and three a volume can hold
static int component_to_short_name(const char* name, u32 length, char* short_name)
{
    u32 index = 0u;
    u32 written = 0u;
    u32 extension = 0u;
    while (index < 11u)
    {
        short_name[index] = ' ';
        index = index + 1u;
    }
    index = 0u;
    while (index < length && name[index] != '.')
    {
        int ch = ascii_to_upper((int)name[index]);
        if (written >= 8u || !fat_name_char_valid(ch))
            return 0;
        short_name[written] = (char)ch;
        written = written + 1u;
        index = index + 1u;
    }
    if (written == 0u)
        return 0;
    if (index < length)
    {
        index = index + 1u;
        while (index < length)
        {
            int ch = ascii_to_upper((int)name[index]);
            if (extension >= 3u || !fat_name_char_valid(ch))
                return 0;
            short_name[8u + extension] = (char)ch;
            extension = extension + 1u;
            index = index + 1u;
        }
    }
    return 1;
}

// The cluster a directory says its parent lives in, which is zero for the root
static int fat_parent_cluster(u32 directory, u32* out)
{
    if (!dir_sector_load(fat_cluster_lba(directory)))
        return 0;
    if (dir_buffer[32] != (u8)'.' || dir_buffer[33] != (u8)'.')
        return 0;
    *out = ((u32)le16(dir_buffer + 52) << 16) | (u32)le16(dir_buffer + 58);
    if (*out == 0u)
        *out = boot_volume.root_cluster;
    return 1;
}

static int vfs_component_lookup(struct vfs_node* directory, const char* name, u32 length, struct vfs_node* out)
{
    char short_name[11];

    if (component_equals(name, length, "."))
    {
        *out = *directory;
        return 1;
    }

    if (directory->type == VFS_NODE_DEV_DIR)
    {
        if (component_equals(name, length, ".."))
        {
            vfs_root_node(out);
            return 1;
        }
        if (component_equals(name, length, "console") || component_equals(name, length, "tty") ||
            component_equals(name, length, "ttyS0"))
        {
            vfs_device_node(out, VFS_NODE_CONSOLE);
            return 1;
        }
        if (component_equals(name, length, "null"))
        {
            vfs_device_node(out, VFS_NODE_NULL);
            return 1;
        }
        if (component_equals(name, length, "zero"))
        {
            vfs_device_node(out, VFS_NODE_ZERO);
            return 1;
        }
        return 0;
    }

    if (directory->type != VFS_NODE_ROOT_DIR && directory->type != VFS_NODE_FAT_DIR)
        return 0;

    if (component_equals(name, length, ".."))
    {
        u32 parent;
        if (directory->type == VFS_NODE_ROOT_DIR)
        {
            vfs_root_node(out);
            return 1;
        }
        if (!fat_parent_cluster(directory->first_cluster, &parent))
            return 0;
        if (parent == boot_volume.root_cluster)
            vfs_root_node(out);
        else
        {
            out->type = VFS_NODE_FAT_DIR;
            out->first_cluster = parent;
            out->size = 0u;
            out->mode = S_IFDIR | 493u;
            out->entry_lba = 0u;
            out->entry_offset = 0u;
        }
        return 1;
    }

    // The device directory hangs off the root and lives nowhere on the volume
    if (directory->type == VFS_NODE_ROOT_DIR && component_equals(name, length, "dev"))
    {
        vfs_device_node(out, VFS_NODE_DEV_DIR);
        return 1;
    }

    if (!component_to_short_name(name, length, short_name))
        return 0;
    return fat_find_in(directory->first_cluster, short_name, out);
}

static int vfs_walk(struct vfs_node* start, const char* path, struct path_result* result)
{
    struct vfs_node current;
    const char* cursor = path;

    if (*cursor == '/')
    {
        vfs_root_node(&current);
        while (*cursor == '/')
            cursor = cursor + 1;
    }
    else
    {
        current = *start;
    }

    result->has_parent = 0u;
    result->has_node = 1u;
    result->leaf_valid = 0u;
    result->node = current;

    for (;;)
    {
        const char* begin = cursor;
        u32 length = 0u;
        while (*cursor != 0 && *cursor != '/')
        {
            cursor = cursor + 1;
            length = length + 1u;
        }
        if (length != 0u)
        {
            struct vfs_node next;
            result->parent = current;
            result->has_parent = 1u;
            result->leaf_valid = (u32)component_to_short_name(begin, length, result->leaf);
            if (!vfs_component_lookup(&current, begin, length, &next))
            {
                result->has_node = 0u;
                while (*cursor == '/')
                    cursor = cursor + 1;
                return *cursor == 0 ? 1 : 0;
            }
            current = next;
            result->node = current;
            result->has_node = 1u;
        }
        while (*cursor == '/')
            cursor = cursor + 1;
        if (*cursor == 0)
            return 1;
    }
}

static struct vfs_node* vfs_working_directory(void);

static int vfs_lookup(const char* path, struct vfs_node* node)
{
    struct path_result walked;
    if (!vfs_walk(vfs_working_directory(), path, &walked) || !walked.has_node)
        return 0;
    *node = walked.node;
    return 1;
}

static int fat_find_short(const char* short_name, u32* first_cluster, u32* size)
{
    struct vfs_node found;
    if (!fat_find_in(boot_volume.root_cluster, short_name, &found))
        return 0;
    *first_cluster = found.first_cluster;
    *size = found.size;
    return 1;
}

static u32 fat_read_short_to_memory(const char* short_name, void* destination, u32 max_size, u32* out_size)
{
    u32 cluster;
    u32 file_size;
    u32 remaining;
    u32 cluster_bytes;
    u8* dst = (u8*)destination;

    *out_size = 0u;
    if (!fat_find_short(short_name, &cluster, &file_size))
        return 1u;
    if (file_size > max_size)
        return 2u;

    cluster_bytes = boot_volume.sectors_per_cluster * SECTOR_SIZE;
    remaining = file_size;
    while (remaining >= cluster_bytes)
    {
        u32 run_start = cluster;
        u32 run_last = cluster;
        u32 run_clusters = 1u;
        u32 full_clusters = remaining / cluster_bytes;
        u32 next_cluster = 0u;
        u32 bytes;

        while (run_clusters < full_clusters)
        {
            next_cluster = fat_next_cluster(run_last);
            if (next_cluster == FAT_READ_ERROR || next_cluster < 2u || next_cluster >= FAT_EOC)
                return 3u;
            if (next_cluster != run_last + 1u)
                break;
            run_last = next_cluster;
            run_clusters = run_clusters + 1u;
            next_cluster = 0u;
        }

        bytes = run_clusters * cluster_bytes;
        if (!block_read((u64)fat_cluster_lba(run_start), dst, bytes))
            return 3u;
        dst = dst + bytes;
        remaining = remaining - bytes;
        if (remaining == 0u)
            break;

        if (next_cluster == 0u)
            next_cluster = fat_next_cluster(run_last);
        if (next_cluster == FAT_READ_ERROR || next_cluster < 2u || next_cluster >= FAT_EOC)
            return 4u;
        cluster = next_cluster;
    }

    if (remaining != 0u)
    {
        u32 full_bytes = remaining & ~(SECTOR_SIZE - 1u);
        u32 sector_index = 0u;
        if (full_bytes != 0u)
        {
            if (!block_read((u64)fat_cluster_lba(cluster), dst, full_bytes))
                return 3u;
            dst = dst + full_bytes;
            remaining = remaining - full_bytes;
            sector_index = full_bytes / SECTOR_SIZE;
        }
        if (remaining != 0u)
        {
            if (!disk_read_sector(fat_cluster_lba(cluster) + sector_index, sector_buffer))
                return 3u;
            mem_copy(dst, sector_buffer, remaining);
        }
    }

    *out_size = file_size;
    return 0u;
}

static int fat_read_path_to_memory(const char* path, void* destination, u32 max_size, u32* out_size)
{
    char short_name[11];
    if (!path_to_fat_short_name(path, short_name))
        return 0;
    return fat_read_short_to_memory(short_name, destination, max_size, out_size) == 0u;
}

static int fat_read_at(struct vfs_node* node, u64 offset, void* destination, u32 count, u32* read_count)
{
    u32 cluster = node->first_cluster;
    u64 cluster_size = (u64)boot_volume.sectors_per_cluster * (u64)SECTOR_SIZE;
    u64 cluster_index;
    u64 skip_clusters;
    u64 inner_offset;
    u8* dst = (u8*)destination;
    u32 done = 0u;

    *read_count = 0u;
    if (node->type != VFS_NODE_FAT_FILE)
        return 0;
    if (offset >= (u64)node->size)
        return 1;
    if ((u64)count > (u64)node->size - offset)
        count = (u32)((u64)node->size - offset);
    if (count == 0u)
        return 1;

    cluster_index = offset / cluster_size;
    inner_offset = offset - cluster_index * cluster_size;
    skip_clusters = cluster_index;

    // Reading a file in order otherwise walks its chain again from the start on every call
    if (fat_walk_first_cluster == node->first_cluster && fat_walk_index <= cluster_index &&
        fat_walk_cluster >= 2u && fat_walk_cluster < FAT_EOC)
    {
        cluster = fat_walk_cluster;
        skip_clusters = cluster_index - fat_walk_index;
    }

    while (skip_clusters != 0ul)
    {
        cluster = fat_next_cluster(cluster);
        if (cluster == FAT_READ_ERROR)
            return 0;
        if (cluster < 2u || cluster >= FAT_EOC)
            return 0;
        skip_clusters = skip_clusters - 1ul;
    }

    fat_walk_first_cluster = node->first_cluster;
    fat_walk_cluster = cluster;
    fat_walk_index = cluster_index;

    while (done < count)
    {
        u32 sector_index = (u32)(inner_offset / (u64)SECTOR_SIZE);
        u32 sector_offset = (u32)(inner_offset & (u64)(SECTOR_SIZE - 1u));
        u32 chunk = SECTOR_SIZE - sector_offset;
        if (chunk > count - done)
            chunk = count - done;
        if (!disk_read_sector(fat_cluster_lba(cluster) + sector_index, sector_buffer))
            return 0;
        mem_copy(dst + done, sector_buffer + sector_offset, chunk);
        done = done + chunk;
        inner_offset = inner_offset + (u64)chunk;
        if (inner_offset >= cluster_size && done < count)
        {
            inner_offset = 0ul;
            cluster = fat_next_cluster(cluster);
            if (cluster == FAT_READ_ERROR || cluster < 2u || cluster >= FAT_EOC)
                return 0;
            cluster_index = cluster_index + 1ul;
            fat_walk_cluster = cluster;
            fat_walk_index = cluster_index;
        }
    }

    *read_count = done;
    return 1;
}

static void fd_clear(u32 fd)
{
    open_files[fd].used = 0u;
    open_files[fd].flags = 0u;
    open_files[fd].offset = 0ul;
    open_files[fd].node.type = VFS_NODE_NONE;
    open_files[fd].node.first_cluster = 0u;
    open_files[fd].node.size = 0u;
    open_files[fd].node.mode = 0u;
}

static int fd_valid(u64 fd)
{
    if (fd >= (u64)MAX_OPEN_FILES)
        return 0;
    return open_files[(u32)fd].used != 0u;
}

static int fd_alloc(void)
{
    u32 fd = 0u;
    while (fd < MAX_OPEN_FILES)
    {
        if (open_files[fd].used == 0u)
            return (int)fd;
        fd = fd + 1u;
    }
    return -1;
}

static void fd_install(u32 fd, struct vfs_node* node, u64 flags)
{
    open_files[fd].used = 1u;
    open_files[fd].flags = (u32)flags;
    open_files[fd].offset = 0ul;
    open_files[fd].node.type = node->type;
    open_files[fd].node.first_cluster = node->first_cluster;
    open_files[fd].node.size = node->size;
    open_files[fd].node.mode = node->mode;
    open_files[fd].node.entry_lba = node->entry_lba;
    open_files[fd].node.entry_offset = node->entry_offset;
}

static void vfs_init(void)
{
    u32 fd = 0u;
    struct vfs_node console;
    while (fd < MAX_OPEN_FILES)
    {
        fd_clear(fd);
        fd = fd + 1u;
    }
    console.type = VFS_NODE_CONSOLE;
    console.first_cluster = 0u;
    console.size = 0u;
    console.mode = S_IFCHR | 438u;
    fd_install(0u, &console, O_RDWR);
    fd_install(1u, &console, O_RDWR);
    fd_install(2u, &console, O_RDWR);
}

static int fd_read_allowed(struct file_descriptor* file)
{
    return ((u64)file->flags & O_ACCMODE) != O_WRONLY;
}

static int fd_write_allowed(struct file_descriptor* file)
{
    u64 mode = (u64)file->flags & O_ACCMODE;
    return mode == O_WRONLY || mode == O_RDWR;
}

static s64 console_read_to_user(u64 buffer, u64 count, u32 flags)
{
    u64 done = 0ul;
    if (count == 0ul)
        return 0l;
    if ((flags & (u32)O_NONBLOCK) != 0u && !uart_can_read())
        return -11l;
    while (done < count)
    {
        u64 physical;
        u64 chunk;
        u64 offset = 0ul;
        if (!user_translate(current_user_root_page_table, buffer + done, PTE_W, &physical))
            return -14l;
        chunk = PAGE_SIZE - (physical & PAGE_MASK);
        if (chunk > count - done)
            chunk = count - done;
        while (offset < chunk)
        {
            ((u8*)physical)[offset] = (u8)uart_read_blocking();
            offset = offset + 1ul;
            done = done + 1ul;
            if ((flags & (u32)O_NONBLOCK) != 0u && !uart_can_read())
                return (s64)done;
        }
    }
    return (s64)done;
}

static s64 zero_read_to_user(u64 buffer, u64 count)
{
    u64 done = 0ul;
    mem_zero(sector_buffer, SECTOR_SIZE);
    while (done < count)
    {
        u64 chunk = count - done;
        if (chunk > SECTOR_SIZE)
            chunk = SECTOR_SIZE;
        if (!user_copy_to_writable(current_user_root_page_table, buffer + done, sector_buffer, chunk))
            return -14l;
        done = done + chunk;
    }
    return (s64)done;
}

static s64 fat_file_read_to_user(struct file_descriptor* file, u64 buffer, u64 count)
{
    u64 done = 0ul;
    while (done < count)
    {
        u32 requested = (u32)(count - done > SECTOR_SIZE ? SECTOR_SIZE : count - done);
        u32 actual = 0u;
        if (!fat_read_at(&file->node, file->offset + done, sector_buffer, requested, &actual))
            return done == 0ul ? -5l : (s64)done;
        if (actual == 0u)
            break;
        if (!user_copy_to_writable(current_user_root_page_table, buffer + done, sector_buffer, (u64)actual))
            return -14l;
        done = done + (u64)actual;
    }
    file->offset = file->offset + done;
    return (s64)done;
}

static s64 vfs_read(u64 fd_value, u64 buffer, u64 count)
{
    struct file_descriptor* file;
    u32 type;
    if (!fd_valid(fd_value))
        return -9l;
    file = &open_files[(u32)fd_value];
    if (!fd_read_allowed(file))
        return -9l;
    type = file->node.type;
    if (type == VFS_NODE_CONSOLE)
        return console_read_to_user(buffer, count, file->flags);
    if (type == VFS_NODE_NULL)
        return 0l;
    if (type == VFS_NODE_ZERO)
        return zero_read_to_user(buffer, count);
    if (type == VFS_NODE_FAT_FILE)
        return fat_file_read_to_user(file, buffer, count);
    if (type == VFS_NODE_ROOT_DIR || type == VFS_NODE_DEV_DIR)
        return -21l;
    return -9l;
}

static s64 console_write_from_user(u64 buffer, u64 count)
{
    u64 done = 0ul;
    while (done < count)
    {
        u64 physical;
        u64 chunk;
        if (!user_translate(current_user_root_page_table, buffer + done, PTE_R, &physical))
            return -14l;
        chunk = PAGE_SIZE - (physical & PAGE_MASK);
        if (chunk > count - done)
            chunk = count - done;
        uart_write_buffer((const u8*)physical, chunk);
        done = done + chunk;
    }
    return (s64)count;
}

// Carries what a program wrote into the file it named, a page of its memory at a time
static s64 fat_write_from_user(struct file_descriptor* file, u64 buffer, u64 count)
{
    u64 done = 0ul;
    if ((file->flags & O_APPEND) != 0ul)
        file->offset = (u64)file->node.size;
    while (done < count)
    {
        u64 physical;
        u64 chunk;
        u32 written;
        if (!user_translate(current_user_root_page_table, buffer + done, PTE_R, &physical))
            return -14l;
        chunk = PAGE_SIZE - (physical & PAGE_MASK);
        if (chunk > count - done)
            chunk = count - done;
        if (!fat_write_at(&file->node, file->offset, (const u8*)physical, (u32)chunk, &written))
            return done != 0ul ? (s64)done : -5l;
        file->offset = file->offset + (u64)written;
        done = done + (u64)written;
        if (written == 0u)
            break;
    }
    return (s64)done;
}

static s64 vfs_write(u64 fd_value, u64 buffer, u64 count)
{
    struct file_descriptor* file;
    u32 type;
    if (!fd_valid(fd_value))
        return -9l;
    file = &open_files[(u32)fd_value];
    if (!fd_write_allowed(file))
        return -9l;
    type = file->node.type;
    if (type == VFS_NODE_CONSOLE)
        return console_write_from_user(buffer, count);
    if (type == VFS_NODE_NULL || type == VFS_NODE_ZERO)
        return (s64)count;
    if (type == VFS_NODE_FAT_FILE)
        return fat_write_from_user(file, buffer, count);
    if (type == VFS_NODE_ROOT_DIR || type == VFS_NODE_DEV_DIR)
        return -30l;
    return -9l;
}

static s64 vfs_openat(u64 dirfd, u64 path_pointer, u64 flags, u64 mode)
{
    char path[PATH_BUFFER_SIZE];
    struct path_result walked;
    struct vfs_node node;
    int fd;
    (void)mode;
    if (dirfd != AT_FDCWD && !fd_valid(dirfd))
        return -9l;
    if (!copy_user_string(path_pointer, path, PATH_BUFFER_SIZE))
        return -14l;
    if (!vfs_walk_at(dirfd, path, &walked))
        return -2l;
    if (!walked.has_node)
    {
        u32 entry_lba;
        u32 entry_offset;
        if ((flags & O_CREAT) == 0ul)
            return -2l;
        if (!walked.has_parent || !walked.leaf_valid)
            return -22l;
        if (walked.parent.type != VFS_NODE_ROOT_DIR && walked.parent.type != VFS_NODE_FAT_DIR)
            return -30l;
        if (!fat_create_in(walked.parent.first_cluster, walked.leaf, 0u, &entry_lba, &entry_offset))
            return -28l;
        node.type = VFS_NODE_FAT_FILE;
        node.first_cluster = 0u;
        node.size = 0u;
        node.mode = S_IFREG | 438u;
        node.entry_lba = entry_lba;
        node.entry_offset = entry_offset;
    }
    else
    {
        node = walked.node;
        if (node.type == VFS_NODE_FAT_FILE && (flags & (O_CREAT | O_EXCL)) == (O_CREAT | O_EXCL))
            return -17l;
    }
    if ((node.type == VFS_NODE_ROOT_DIR || node.type == VFS_NODE_DEV_DIR || node.type == VFS_NODE_FAT_DIR) &&
        ((flags & O_ACCMODE) != 0ul))
    {
        return -21l;
    }
    // Opening for writing with a truncation gives back everything the file held
    if (node.type == VFS_NODE_FAT_FILE && (flags & O_TRUNC) != 0ul && (flags & O_ACCMODE) != O_RDONLY)
    {
        if (!fat_free_chain(node.first_cluster))
            return -5l;
        node.first_cluster = 0u;
        node.size = 0u;
        if (!fat_write_entry(&node))
            return -5l;
    }
    fd = fd_alloc();
    if (fd < 0)
        return -24l;
    fd_install((u32)fd, &node, flags);
    return (s64)fd;
}

static struct vfs_node vfs_fallback_directory;

// Before there is a process to ask, a walk starts where the volume does
static struct vfs_node* vfs_working_directory(void)
{
    if (current_task == (struct process*)NULL || current_task->cwd.type == VFS_NODE_NONE)
    {
        vfs_root_node(&vfs_fallback_directory);
        return &vfs_fallback_directory;
    }
    return &current_task->cwd;
}

// A walk may start at a descriptor rather than the working directory, which is what the at calls are for
static int vfs_walk_at(u64 dirfd, const char* path, struct path_result* result)
{
    if (dirfd == AT_FDCWD || path[0] == '/')
        return vfs_walk(vfs_working_directory(), path, result);
    if (!fd_valid(dirfd))
        return 0;
    return vfs_walk(&open_files[(u32)dirfd].node, path, result);
}

// Builds the name a working directory will answer with, resolving what the path says about itself
static int path_canonical(const char* base, const char* path, char* out, u32 capacity)
{
    u32 length = 0u;
    const char* cursor = path;

    if (*cursor == '/')
    {
        out[0] = '/';
        length = 1u;
        while (*cursor == '/')
            cursor = cursor + 1;
    }
    else
    {
        while (base[length] != 0 && length + 1u < capacity)
        {
            out[length] = base[length];
            length = length + 1u;
        }
        if (length == 0u)
        {
            out[0] = '/';
            length = 1u;
        }
    }

    for (;;)
    {
        const char* begin = cursor;
        u32 size = 0u;
        while (*cursor != 0 && *cursor != '/')
        {
            cursor = cursor + 1;
            size = size + 1u;
        }
        if (size != 0u && !component_equals(begin, size, "."))
        {
            if (component_equals(begin, size, ".."))
            {
                while (length > 1u && out[length - 1u] != '/')
                    length = length - 1u;
                if (length > 1u)
                    length = length - 1u;
            }
            else
            {
                u32 index = 0u;
                if (length != 1u)
                {
                    if (length + 1u >= capacity)
                        return 0;
                    out[length] = '/';
                    length = length + 1u;
                }
                while (index < size)
                {
                    if (length + 1u >= capacity)
                        return 0;
                    out[length] = begin[index];
                    length = length + 1u;
                    index = index + 1u;
                }
            }
        }
        while (*cursor == '/')
            cursor = cursor + 1;
        if (*cursor == 0)
            break;
    }

    out[length] = 0;
    return 1;
}

static s64 vfs_chdir(u64 path_pointer)
{
    char path[PATH_BUFFER_SIZE];
    char canonical[PATH_BUFFER_SIZE];
    struct path_result walked;
    if (!copy_user_string(path_pointer, path, PATH_BUFFER_SIZE))
        return -14l;
    if (!vfs_walk(vfs_working_directory(), path, &walked) || !walked.has_node)
        return -2l;
    if (walked.node.type != VFS_NODE_ROOT_DIR && walked.node.type != VFS_NODE_FAT_DIR &&
        walked.node.type != VFS_NODE_DEV_DIR)
        return -20l;
    if (!path_canonical(current_task->cwd_path, path, canonical, PATH_BUFFER_SIZE))
        return -36l;
    current_task->cwd = walked.node;
    {
        u32 index = 0u;
        while (canonical[index] != 0 && index + 1u < PATH_BUFFER_SIZE)
        {
            current_task->cwd_path[index] = canonical[index];
            index = index + 1u;
        }
        current_task->cwd_path[index] = 0;
    }
    return 0l;
}

static s64 vfs_mkdirat(u64 dirfd, u64 path_pointer, u64 mode)
{
    char path[PATH_BUFFER_SIZE];
    struct path_result walked;
    struct vfs_node made;
    (void)mode;
    if (!copy_user_string(path_pointer, path, PATH_BUFFER_SIZE))
        return -14l;
    if (!vfs_walk_at(dirfd, path, &walked))
        return -2l;
    if (walked.has_node)
        return -17l;
    if (!walked.has_parent || !walked.leaf_valid)
        return -22l;
    if (walked.parent.type != VFS_NODE_ROOT_DIR && walked.parent.type != VFS_NODE_FAT_DIR)
        return -30l;
    return fat_make_directory(walked.parent.first_cluster, walked.leaf, &made) ? 0l : -28l;
}

static s64 vfs_unlinkat(u64 dirfd, u64 path_pointer, u64 flags)
{
    char path[PATH_BUFFER_SIZE];
    struct path_result walked;
    int removing_directory = (flags & AT_REMOVEDIR) != 0ul;

    if (!copy_user_string(path_pointer, path, PATH_BUFFER_SIZE))
        return -14l;
    if (!vfs_walk_at(dirfd, path, &walked) || !walked.has_node)
        return -2l;
    if (!walked.has_parent || !walked.leaf_valid)
        return -22l;
    if (removing_directory)
    {
        if (walked.node.type != VFS_NODE_FAT_DIR)
            return walked.node.type == VFS_NODE_ROOT_DIR ? -16l : -20l;
        if (!fat_directory_is_empty(walked.node.first_cluster))
            return -39l;
    }
    else if (walked.node.type != VFS_NODE_FAT_FILE)
    {
        return walked.node.type == VFS_NODE_FAT_DIR ? -21l : -1l;
    }
    if (walked.parent.type != VFS_NODE_ROOT_DIR && walked.parent.type != VFS_NODE_FAT_DIR)
        return -30l;
    return fat_remove_in(walked.parent.first_cluster, walked.leaf) ? 0l : -5l;
}

static s64 vfs_close(u64 fd)
{
    if (!fd_valid(fd))
        return -9l;
    fd_clear((u32)fd);
    return 0l;
}

static const char* root_dir_entry_name(u64 index)
{
    if (index == 0ul)
        return ".";
    if (index == 1ul)
        return "..";
    if (index == 2ul)
        return "dev";
    return NULL;
}

static u32 root_dir_entry_type(u64 index)
{
    if (index <= 2ul)
        return DT_DIR;
    return 0u;
}

static char fat_display_char(u8 ch)
{
    if (ch >= (u8)'A' && ch <= (u8)'Z')
        return (char)(ch - (u8)'A' + (u8)'a');
    return (char)ch;
}

static void fat_format_short_name(const u8* entry, char* name)
{
    u32 stem_length = 8u;
    u32 extension_length = 3u;
    u32 output = 0u;
    u32 index = 0u;
    while (stem_length != 0u && entry[stem_length - 1u] == (u8)' ')
        stem_length = stem_length - 1u;
    while (extension_length != 0u && entry[8u + extension_length - 1u] == (u8)' ')
        extension_length = extension_length - 1u;
    while (index < stem_length)
    {
        name[output] = fat_display_char(entry[index]);
        output = output + 1u;
        index = index + 1u;
    }
    if (extension_length != 0u)
    {
        name[output] = '.';
        output = output + 1u;
        index = 0u;
        while (index < extension_length)
        {
            name[output] = fat_display_char(entry[8u + index]);
            output = output + 1u;
            index = index + 1u;
        }
    }
    name[output] = 0;
}

static int fat_entry_at(u32 directory, u64 requested, char* name, u32* type, u64* inode)
{
    u32 cluster = directory;
    u64 current = 0ul;
    while (cluster >= 2u && cluster < FAT_EOC)
    {
        u32 sector_index = 0u;
        while (sector_index < boot_volume.sectors_per_cluster)
        {
            u32 lba = fat_cluster_lba(cluster) + sector_index;
            u32 offset = 0u;
            if (!dir_sector_load(lba))
                return 0;
            while (offset < SECTOR_SIZE)
            {
                u8* entry = dir_buffer + offset;
                u8 first = entry[0];
                u8 attributes = entry[11];
                if (first == 0u)
                    return 0;
                if (first != 0xe5u && (attributes & 15u) != 15u && (attributes & 8u) == 0u)
                {
                    if (current == requested)
                    {
                        u32 first_cluster = ((u32)le16(entry + 20) << 16) | (u32)le16(entry + 26);
                        fat_format_short_name(entry, name);
                        *type = (attributes & 16u) != 0u ? DT_DIR : DT_REG;
                        *inode = ((u64)first_cluster << 32) | current;
                        return 1;
                    }
                    current = current + 1ul;
                }
                offset = offset + 32u;
            }
            sector_index = sector_index + 1u;
        }
        cluster = fat_next_cluster(cluster);
        if (cluster == FAT_READ_ERROR)
            return 0;
    }
    return 0;
}

static const char* dev_dir_entry_name(u64 index)
{
    if (index == 0ul)
        return ".";
    if (index == 1ul)
        return "..";
    if (index == 2ul)
        return "console";
    if (index == 3ul)
        return "tty";
    if (index == 4ul)
        return "ttyS0";
    if (index == 5ul)
        return "null";
    if (index == 6ul)
        return "zero";
    return NULL;
}

static u32 dev_dir_entry_type(u64 index)
{
    if (index == 0ul || index == 1ul)
        return DT_DIR;
    if (index >= 2ul && index <= 6ul)
        return DT_CHR;
    return 0u;
}

static u32 string_length(const char* text)
{
    u32 length = 0u;
    while (text[length] != 0)
        length = length + 1u;
    return length;
}

static void build_dirent64(u8* buffer, u64 inode, u64 next_offset, u32 type, const char* name, u32 record_length)
{
    u32 name_length = string_length(name);
    u32 i = 0u;
    mem_zero(buffer, (u64)record_length);
    store_le64(buffer + 0, inode);
    store_le64(buffer + 8, next_offset);
    store_le16(buffer + 16, (u16)record_length);
    buffer[18] = (u8)type;
    while (i < name_length)
    {
        buffer[19u + i] = (u8)name[i];
        i = i + 1u;
    }
    buffer[19u + name_length] = 0u;
}

static s64 vfs_getdents64(u64 fd_value, u64 user_buffer, u64 count)
{
    struct file_descriptor* file;
    u64 done = 0ul;
    if (!fd_valid(fd_value))
        return -9l;
    file = &open_files[(u32)fd_value];
    if (file->node.type != VFS_NODE_ROOT_DIR && file->node.type != VFS_NODE_DEV_DIR &&
        file->node.type != VFS_NODE_FAT_DIR)
    {
        return -20l;
    }
    while (done < count)
    {
        const char* name;
        char fat_name[13];
        u32 type;
        u32 name_length;
        u32 record_length;
        u64 inode = ((u64)file->node.type << 32) | file->offset;
        if (file->node.type == VFS_NODE_ROOT_DIR)
        {
            name = root_dir_entry_name(file->offset);
            type = root_dir_entry_type(file->offset);
            if (name == NULL)
            {
                if (file->offset < 3ul || !fat_entry_at(file->node.first_cluster, file->offset - 3ul, fat_name, &type, &inode))
                    break;
                name = fat_name;
            }
        }
        else if (file->node.type == VFS_NODE_FAT_DIR)
        {
            if (!fat_entry_at(file->node.first_cluster, file->offset, fat_name, &type, &inode))
                break;
            name = fat_name;
        }
        else
        {
            name = dev_dir_entry_name(file->offset);
            type = dev_dir_entry_type(file->offset);
        }
        if (name == NULL)
            break;
        name_length = string_length(name);
        record_length = (u32)align_up((u64)(19u + name_length + 1u), 8ul);
        if ((u64)record_length > count - done)
        {
            if (done == 0ul)
                return -22l;
            break;
        }
        if ((u64)record_length > sizeof(dirent_buffer))
            return -22l;
        build_dirent64(dirent_buffer, inode, file->offset + 1ul, type, name, record_length);
        if (!user_copy_to_writable(current_user_root_page_table, user_buffer + done, dirent_buffer, (u64)record_length))
            return -14l;
        done = done + (u64)record_length;
        file->offset = file->offset + 1ul;
    }
    return (s64)done;
}

static s64 vfs_lseek(u64 fd_value, u64 offset, u64 whence)
{
    struct file_descriptor* file;
    s64 base;
    s64 requested;
    if (!fd_valid(fd_value))
        return -9l;
    file = &open_files[(u32)fd_value];
    if (file->node.type != VFS_NODE_FAT_FILE)
        return -29l;
    if (whence == SEEK_SET)
        base = 0l;
    else if (whence == SEEK_CUR)
        base = (s64)file->offset;
    else if (whence == SEEK_END)
        base = (s64)file->node.size;
    else
        return -22l;
    requested = base + (s64)offset;
    if (requested < 0l)
        return -22l;
    file->offset = (u64)requested;
    return requested;
}

static void stat_store_node(u8* stat_buffer, struct vfs_node* node)
{
    u64 blocks = ((u64)node->size + 511ul) / 512ul;
    mem_zero(stat_buffer, 128ul);
    store_le64(stat_buffer + 0, 1ul);
    store_le64(stat_buffer + 8, ((u64)node->type << 32) | (u64)node->first_cluster);
    store_le32(stat_buffer + 16, node->mode);
    store_le32(stat_buffer + 20, node->type == VFS_NODE_ROOT_DIR || node->type == VFS_NODE_DEV_DIR ||
        node->type == VFS_NODE_FAT_DIR ? 2u : 1u);
    store_le32(stat_buffer + 24, 0u);
    store_le32(stat_buffer + 28, 0u);
    store_le64(stat_buffer + 32, node->type == VFS_NODE_CONSOLE ? 1ul : 0ul);
    store_le64(stat_buffer + 48, (u64)node->size);
    store_le32(stat_buffer + 56, SECTOR_SIZE);
    store_le64(stat_buffer + 64, blocks);
}

static s64 vfs_fstat(u64 fd_value, u64 stat_pointer)
{
    u8 stat_buffer[128];
    if (!fd_valid(fd_value))
        return -9l;
    stat_store_node(stat_buffer, &open_files[(u32)fd_value].node);
    if (!user_copy_to_writable(current_user_root_page_table, stat_pointer, stat_buffer, 128ul))
        return -14l;
    return 0l;
}

static s64 vfs_newfstatat(u64 dirfd, u64 path_pointer, u64 stat_pointer, u64 flags)
{
    char path[PATH_BUFFER_SIZE];
    struct vfs_node node;
    if ((flags & AT_EMPTY_PATH) != 0ul)
    {
        if (!copy_user_string(path_pointer, path, PATH_BUFFER_SIZE))
            return -14l;
        if (path[0] == 0)
        {
            if (!fd_valid(dirfd))
                return -9l;
            stat_store_node(sector_buffer, &open_files[(u32)dirfd].node);
            if (!user_copy_to_writable(current_user_root_page_table, stat_pointer, sector_buffer, 128ul))
                return -14l;
            return 0l;
        }
    }
    if (dirfd != AT_FDCWD && !fd_valid(dirfd))
        return -9l;
    if (!copy_user_string(path_pointer, path, PATH_BUFFER_SIZE))
        return -14l;
    if (!vfs_lookup(path, &node))
        return -2l;
    stat_store_node(sector_buffer, &node);
    if (!user_copy_to_writable(current_user_root_page_table, stat_pointer, sector_buffer, 128ul))
        return -14l;
    return 0l;
}

static void store_string_field(u8* buffer, u32 offset, const char* text)
{
    u32 i = 0u;
    while (i < 65u)
    {
        buffer[offset + i] = 0u;
        i = i + 1u;
    }
    i = 0u;
    while (text[i] != 0 && i < 64u)
    {
        buffer[offset + i] = (u8)text[i];
        i = i + 1u;
    }
}

static s64 sys_uname_impl(u64 user_pointer)
{
    u8 uts[390];
    mem_zero(uts, 390ul);
    store_string_field(uts, 0u, "Linux");
    store_string_field(uts, 65u, "cnidaria-riscv");
    store_string_field(uts, 130u, "6.1.0-cnidaria");
    store_string_field(uts, 195u, "#1 SMP PREEMPT");
    store_string_field(uts, 260u, "riscv64");
    store_string_field(uts, 325u, "cnidaria");
    if (!user_copy_to_writable(current_user_root_page_table, user_pointer, uts, 390ul))
        return -14l;
    return 0l;
}

static s64 sys_reboot_impl(u64 magic1, u64 magic2, u64 cmd)
{
    u32 magic2_32 = (u32)magic2;
    u32 cmd_32 = (u32)cmd;

    if ((u32)magic1 != (u32)LINUX_REBOOT_MAGIC1)
        return -22l;
    if (magic2_32 != (u32)LINUX_REBOOT_MAGIC2 && magic2_32 != (u32)LINUX_REBOOT_MAGIC2A &&
        magic2_32 != (u32)LINUX_REBOOT_MAGIC2B && magic2_32 != (u32)LINUX_REBOOT_MAGIC2C)
        return -22l;

    if (cmd_32 == (u32)LINUX_REBOOT_CMD_CAD_ON || cmd_32 == (u32)LINUX_REBOOT_CMD_CAD_OFF)
        return 0l;

    if (cmd_32 == (u32)LINUX_REBOOT_CMD_POWER_OFF)
    {
        halt();
    }
    if (cmd_32 == (u32)LINUX_REBOOT_CMD_RESTART || cmd_32 == (u32)LINUX_REBOOT_CMD_RESTART2)
    {
        halt();
    }
    if (cmd_32 == (u32)LINUX_REBOOT_CMD_HALT)
    {
        halt();
    }
    return -22l;
}

static s64 sys_getcwd_impl(u64 user_buffer, u64 size)
{
    const char* cwd = current_task->cwd_path[0] != 0 ? current_task->cwd_path : "/";
    u64 length = (u64)string_length(cwd) + 1ul;
    if (size < length)
        return -34l;
    if (!user_copy_to_writable(current_user_root_page_table, user_buffer, cwd, length))
        return -14l;
    return (s64)user_buffer;
}

static s64 sys_ioctl_impl(u64 fd_value, u64 request, u64 argument)
{
    u8 data[64];
    if (!fd_valid(fd_value))
        return -9l;
    if (open_files[(u32)fd_value].node.type != VFS_NODE_CONSOLE)
        return -25l;
    if (request == TIOCGWINSZ)
    {
        mem_zero(data, 8ul);
        store_le16(data + 0, 25u);
        store_le16(data + 2, 80u);
        if (!user_copy_to_writable(current_user_root_page_table, argument, data, 8ul))
            return -14l;
        return 0l;
    }
    if (request == TCGETS)
    {
        mem_zero(data, 64ul);
        if (!user_copy_to_writable(current_user_root_page_table, argument, data, 64ul))
            return -14l;
        return 0l;
    }
    if (request == TCSETS || request == TCSETSW || request == TCSETSF)
        return 0l;
    return -25l;
}

static s64 sys_readlinkat_impl(u64 dirfd, u64 path_pointer, u64 buffer, u64 size)
{
    char path[PATH_BUFFER_SIZE];
    const char* target = "/init.elf";
    u64 len = 0ul;
    (void)dirfd;
    if (!copy_user_string(path_pointer, path, PATH_BUFFER_SIZE))
        return -14l;
    if (!path_equal_literal(path, "/proc/self/exe") && !path_equal_literal(path, "/proc/thread-self/exe"))
        return -2l;
    while (target[len] != 0)
        len = len + 1ul;
    if (len > size)
        len = size;
    if (!user_copy_to_writable(current_user_root_page_table, buffer, target, len))
        return -14l;
    return (s64)len;
}

static u64 vm_pte_flags(u64 prot)
{
    u64 flags = 0ul;
    if ((prot & PROT_READ) != 0ul)
        flags = flags | PTE_R;
    if ((prot & PROT_WRITE) != 0ul)
        flags = flags | PTE_R | PTE_W;
    if ((prot & PROT_EXEC) != 0ul)
        flags = flags | PTE_X;
    return flags;
}

static struct vm_region* vm_find_region(u64 address)
{
    u32 index = 0u;
    while (index < MAX_VM_REGIONS)
    {
        struct vm_region* region = &current_task->vm_regions[index];
        if (region->used != 0u && address >= region->start && address < region->end)
            return region;
        index = index + 1u;
    }
    return NULL;
}

static int vm_range_covered(u64 start, u64 end)
{
    u64 cursor = start;
    while (cursor < end)
    {
        struct vm_region* region = vm_find_region(cursor);
        if (region == NULL)
            return 0;
        if (region->end <= cursor)
            return 0;
        cursor = region->end < end ? region->end : end;
    }
    return 1;
}

static int vm_range_overlaps(u64 start, u64 end)
{
    u32 index = 0u;
    while (index < MAX_VM_REGIONS)
    {
        struct vm_region* region = &current_task->vm_regions[index];
        if (region->used != 0u && start < region->end && end > region->start)
            return 1;
        index = index + 1u;
    }
    return 0;
}

static int vm_free_slot_count(void)
{
    u32 index = 0u;
    int count = 0;
    while (index < MAX_VM_REGIONS)
    {
        if (current_task->vm_regions[index].used == 0u)
            count = count + 1;
        index = index + 1u;
    }
    return count;
}

static struct vm_region* vm_allocate_region(void)
{
    u32 index = 0u;
    while (index < MAX_VM_REGIONS)
    {
        struct vm_region* region = &current_task->vm_regions[index];
        if (region->used == 0u)
        {
            region->used = 1u;
            return region;
        }
        index = index + 1u;
    }
    return NULL;
}

static int vm_add_region(u64 start, u64 end, u64 prot, u64 flags)
{
    struct vm_region* region;
    u32 index = 0u;
    while (index < MAX_VM_REGIONS)
    {
        region = &current_task->vm_regions[index];
        if (region->used != 0u && region->end == start && region->prot == prot && region->flags == flags)
        {
            region->end = end;
            return 1;
        }
        if (region->used != 0u && region->start == end && region->prot == prot && region->flags == flags)
        {
            region->start = start;
            return 1;
        }
        index = index + 1u;
    }
    region = vm_allocate_region();
    if (region == NULL)
        return 0;
    region->start = start;
    region->end = end;
    region->prot = prot;
    region->flags = flags;
    return 1;
}

static int vm_remove_range(u64 start, u64 end)
{
    u32 index = 0u;
    int splits = 0;
    while (index < MAX_VM_REGIONS)
    {
        struct vm_region* region = &current_task->vm_regions[index];
        if (region->used != 0u && start > region->start && end < region->end)
            splits = splits + 1;
        index = index + 1u;
    }
    if (vm_free_slot_count() < splits)
        return 0;
    index = 0u;
    while (index < MAX_VM_REGIONS)
    {
        struct vm_region* region = &current_task->vm_regions[index];
        if (region->used == 0u || start >= region->end || end <= region->start)
        {
            index = index + 1u;
            continue;
        }
        if (start <= region->start && end >= region->end)
        {
            region->used = 0u;
        }
        else if (start <= region->start)
        {
            region->start = end;
        }
        else if (end >= region->end)
        {
            region->end = start;
        }
        else
        {
            struct vm_region* right = vm_allocate_region();
            if (right == NULL)
                return 0;
            right->start = end;
            right->end = region->end;
            right->prot = region->prot;
            right->flags = region->flags;
            region->end = start;
        }
        index = index + 1u;
    }
    return 1;
}

static u64* user_leaf_pte(u64 root, u64 virtual_address)
{
    u64 table = root;
    int level = 2;
    while (level > 0)
    {
        u64 pte = ((u64*)table)[sv39_index(virtual_address, level)];
        if ((pte & PTE_V) == 0ul || (pte & (PTE_R | PTE_W | PTE_X)) != 0ul)
            return NULL;
        table = pte_physical(pte);
        level = level - 1;
    }
    return &((u64*)table)[sv39_index(virtual_address, 0)];
}

static int vm_apply_protection(u64 start, u64 end, u64 prot)
{
    u64 page = start;
    u64 pte_flags = vm_pte_flags(prot);
    while (page < end)
    {
        u64* entry = user_leaf_pte(current_user_root_page_table, page);
        if (pte_flags == 0ul)
        {
            if (entry != NULL && (*entry & PTE_V) != 0ul)
            {
                u64 physical = pte_physical(*entry);
                *entry = pte_make_noaccess(physical);
            }
        }
        else if (entry == NULL || ((*entry & PTE_V) == 0ul && (*entry & PTE_SOFT_NOACCESS) == 0ul))
        {
            if (!map_user_page(current_user_root_page_table, page, pte_flags))
                return 0;
        }
        else
        {
            u64 physical = pte_physical(*entry);
            *entry = pte_make(physical, pte_flags | PTE_U | PTE_A | PTE_D);
        }
        page = page + PAGE_SIZE;
    }
    __asm__ volatile("sfence.vma zero, zero" : : : "memory");
    return 1;
}

static s64 sys_munmap_impl(u64 address, u64 length)
{
    u64 start;
    u64 end;
    if ((address & PAGE_MASK) != 0ul || length == 0ul)
        return -22l;
    start = address;
    length = align_up(length, PAGE_SIZE);
    end = start + length;
    if (end < start || !user_mapping_range_valid(start, length))
        return -22l;
    if (!unmap_user_pages(current_user_root_page_table, start, end))
        return -22l;
    if (!vm_remove_range(start, end))
        return -12l;
    return 0l;
}

static s64 sys_mprotect_impl(u64 address, u64 length, u64 prot)
{
    u64 start;
    u64 end;
    if ((address & PAGE_MASK) != 0ul || length == 0ul || (prot & ~(PROT_READ | PROT_WRITE | PROT_EXEC)) != 0ul)
        return -22l;
    start = address;
    length = align_up(length, PAGE_SIZE);
    end = start + length;
    if (end < start || !vm_range_covered(start, end))
        return -12l;
    if (!vm_apply_protection(start, end, prot))
        return -12l;
    if (!vm_remove_range(start, end) || !vm_add_region(start, end, prot, MAP_PRIVATE | MAP_ANONYMOUS))
        return -12l;
    return 0l;
}

static s64 sys_mmap_impl(u64 address, u64 length, u64 prot, u64 flags, u64 fd, u64 offset)
{
    u64 start;
    u64 end;
    u64 pte_flags;
    (void)offset;
    if (length == 0ul || (prot & ~(PROT_READ | PROT_WRITE | PROT_EXEC)) != 0ul)
        return -22l;
    if ((flags & (MAP_PRIVATE | MAP_ANONYMOUS)) != (MAP_PRIVATE | MAP_ANONYMOUS))
        return -19l;
    if (fd != (u64)-1l)
        return -22l;
    length = align_up(length, PAGE_SIZE);
    if ((flags & MAP_FIXED) != 0ul)
    {
        if ((address & PAGE_MASK) != 0ul)
            return -22l;
        start = address;
    }
    else
    {
        start = align_up(user_mmap_cursor, PAGE_SIZE);
        while (vm_range_overlaps(start, start + length))
            start = start + length;
    }
    end = start + length;
    if (end < start || !user_mapping_range_valid(start, length))
        return -22l;
    if ((flags & MAP_FIXED) != 0ul)
    {
        if (!unmap_user_pages(current_user_root_page_table, start, end) || !vm_remove_range(start, end))
            return -12l;
    }
    else if (vm_range_overlaps(start, end))
    {
        return -12l;
    }
    pte_flags = vm_pte_flags(prot);
    if (pte_flags != 0ul && !map_user_range(current_user_root_page_table, start, end, pte_flags))
    {
        unmap_user_pages(current_user_root_page_table, start, end);
        return -12l;
    }
    if (!vm_add_region(start, end, prot, flags & (MAP_PRIVATE | MAP_ANONYMOUS)))
    {
        unmap_user_pages(current_user_root_page_table, start, end);
        return -12l;
    }
    if ((flags & MAP_FIXED) == 0ul)
        user_mmap_cursor = end;
    return (s64)start;
}

static u64 elf_segment_flags(u32 elf_flags)
{
    u64 flags = 0ul;
    if ((elf_flags & ELF_PF_R) != 0u)
        flags = flags | PTE_R;
    if ((elf_flags & ELF_PF_W) != 0u)
        flags = flags | PTE_W | PTE_R;
    if ((elf_flags & ELF_PF_X) != 0u)
        flags = flags | PTE_X;
    if (flags == 0ul)
        flags = PTE_R;
    return flags;
}

static int load_elf64(const u8* image, u32 image_size, u64 root, struct elf_image* loaded)
{
    u64 phoff;
    u16 phentsize;
    u16 phnum;
    u16 index;
    u64 high = 0ul;
    u64 entry_physical;
    if (image_size < 64u)
        return 0;
    if (image[0] != 0x7fu || image[1] != 'E' || image[2] != 'L' || image[3] != 'F')
        return 0;
    if (image[4] != 2u || image[5] != 1u || image[6] != 1u)
        return 0;
    if (le16(image + 18) != 243u)
        return 0;
    if (le32(image + 20) != 1u)
        return 0;
    phoff = le64(image + 32);
    phentsize = le16(image + 54);
    phnum = le16(image + 56);
    if (phentsize < 56u)
        return 0;
    if (phoff + (u64)phentsize * (u64)phnum > (u64)image_size)
        return 0;
    loaded->phdr = 0ul;
    loaded->interp[0] = 0;
    index = 0;
    while (index < phnum)
    {
        const u8* ph = image + phoff + (u64)index * (u64)phentsize;
        u32 type = le32(ph + 0);
        if (type == ELF_PT_PHDR)
            loaded->phdr = le64(ph + 16);
        if (type == ELF_PT_INTERP)
        {
            u64 interp_offset = le64(ph + 8);
            u64 interp_size = le64(ph + 32);
            u64 cursor = 0ul;
            if (interp_size == 0ul || interp_size > (u64)PATH_BUFFER_SIZE)
                return 0;
            if (interp_offset + interp_size > (u64)image_size)
                return 0;
            while (cursor < interp_size)
            {
                loaded->interp[cursor] = (char)image[interp_offset + cursor];
                cursor = cursor + 1ul;
            }
            loaded->interp[interp_size - 1ul] = 0;
        }
        if (type == ELF_PT_LOAD)
        {
            u32 flags = le32(ph + 4);
            u64 offset = le64(ph + 8);
            u64 vaddr = le64(ph + 16);
            u64 filesz = le64(ph + 32);
            u64 memsz = le64(ph + 40);
            u64 end = vaddr + memsz;
            u64 mapped_start = align_down(vaddr, PAGE_SIZE);
            u64 mapped_end = align_up(end, PAGE_SIZE);
            if (memsz < filesz)
                return 0;
            if (offset + filesz > (u64)image_size)
                return 0;
            if (end < vaddr)
                return 0;
            if (memsz != 0ul)
            {
                if (!user_mapping_range_valid(vaddr, memsz))
                    return 0;
                if (!map_user_range(root, mapped_start, mapped_end, elf_segment_flags(flags)))
                    return 0;
                if (filesz != 0ul && !user_copy_to(root, vaddr, image + offset, filesz))
                    return 0;
                if (end > high)
                    high = end;
            }
        }
        index = index + 1;
    }
    loaded->entry = le64(image + 24);
    loaded->start_entry = loaded->entry;
    loaded->interp_base = 0ul;
    loaded->phent = phentsize;
    loaded->phnum = phnum;
    loaded->brk_start = align_up(high, PAGE_SIZE);
    if (loaded->entry >= USER_VA_LIMIT)
        return 0;
    if (!user_translate(root, loaded->entry, PTE_X, &entry_physical))
        return 0;
    return 1;
}

static int load_process_image(u64 root, const u8* image, u32 image_size, struct elf_image* loaded)
{
    struct elf_image interpreter;
    u32 interpreter_size;
    if (!load_elf64(image, image_size, root, loaded))
        return 0;
    process_brk = loaded->brk_start;
    process_brk_min = loaded->brk_start;
    user_mmap_cursor = USER_MMAP_BASE;
    if (loaded->interp[0] == 0)
        return 1;
    if (!fat_read_path_to_memory(loaded->interp, (void*)USER_INTERP_BUFFER, (u32)USER_INTERP_BUFFER_SIZE, &interpreter_size))
        return 0;
    if (!load_elf64((const u8*)USER_INTERP_BUFFER, interpreter_size, root, &interpreter))
        return 0;
    if (interpreter.interp[0] != 0)
        return 0;
    loaded->interp_base = 0ul;
    loaded->start_entry = interpreter.entry;
    return 1;
}

static int build_user_stack(u64 root, struct elf_image* image, struct exec_arguments* arguments, u64* out_stack)
{
    u64 strings = USER_STACK_TOP - (u64)arguments->bytes_used;
    u64 sp;
    u64 stack[MAX_EXEC_ARGS * 2u + 24u];
    u32 word = 0u;
    u32 index = 0u;
    if (arguments->count == 0u)
        return 0;
    stack[word] = (u64)arguments->count;
    word = word + 1u;
    while (index < arguments->count)
    {
        stack[word] = strings + (u64)arguments->offsets[index];
        word = word + 1u;
        index = index + 1u;
    }
    stack[word] = 0ul;
    word = word + 1u;
    index = 0u;
    while (index < arguments->environment_count)
    {
        stack[word] = strings + (u64)arguments->environment_offsets[index];
        word = word + 1u;
        index = index + 1u;
    }
    stack[word] = 0ul;
    word = word + 1u;
    stack[word] = AT_PAGESZ;
    stack[word + 1u] = PAGE_SIZE;
    word = word + 2u;
    stack[word] = AT_ENTRY;
    stack[word + 1u] = image->entry;
    word = word + 2u;
    stack[word] = AT_PHENT;
    stack[word + 1u] = image->phent;
    word = word + 2u;
    stack[word] = AT_PHNUM;
    stack[word + 1u] = image->phnum;
    word = word + 2u;
    stack[word] = AT_PHDR;
    stack[word + 1u] = image->phdr;
    word = word + 2u;
    stack[word] = AT_BASE;
    stack[word + 1u] = image->interp_base;
    word = word + 2u;
    stack[word] = AT_HOST_BRIDGE;
    stack[word + 1u] = boot_device.host_bridge_base == 0ul ? 0ul : USER_HOST_BRIDGE_BASE;
    word = word + 2u;
    stack[word] = AT_FRAMEBUFFER;
    stack[word + 1u] = boot_device.framebuffer_base == 0ul ? 0ul : USER_FRAMEBUFFER_BASE;
    word = word + 2u;
    stack[word] = AT_NULL;
    stack[word + 1u] = 0ul;
    word = word + 2u;
    sp = align_down(align_down(strings, 16ul) - (u64)word * 8ul, 16ul);
    if (!map_user_stack(root, sp))
        return 0;
    if (!user_copy_to(root, strings, arguments->bytes, (u64)arguments->bytes_used))
        return 0;
    if (!user_copy_to(root, sp, stack, (u64)word * 8ul))
        return 0;
    *out_stack = sp;
    return 1;
}

static int grow_user_brk(u64 requested)
{
    u64 old_limit;
    u64 new_limit;
    if (requested < process_brk_min || requested >= USER_STACK_TOP - USER_STACK_SIZE)
        return 0;
    old_limit = align_up(process_brk, PAGE_SIZE);
    new_limit = align_up(requested, PAGE_SIZE);
    if (requested > process_brk)
    {
        if (!user_mapping_range_valid(process_brk_min, requested - process_brk_min))
            return 0;
    }
    else if (new_limit < old_limit)
    {
        if (!unmap_user_pages(current_user_root_page_table, new_limit, old_limit))
            return 0;
    }
    process_brk = requested;
    return 1;
}

void kernel_trap_dispatch(struct trap_frame* frame)
{
    u64 cause = frame->scause;
    if (cause == 8ul)
    {
        u64 nr = frame->x[17];
        frame->sepc = frame->sepc + 4ul;
        if (nr == SYS_READ)
        {
            frame->x[10] = (u64)vfs_read(frame->x[10], frame->x[11], frame->x[12]);
            return;
        }
        if (nr == SYS_WRITE)
        {
            frame->x[10] = (u64)vfs_write(frame->x[10], frame->x[11], frame->x[12]);
            return;
        }
        if (nr == SYS_OPENAT)
        {
            frame->x[10] = (u64)vfs_openat(frame->x[10], frame->x[11], frame->x[12], frame->x[13]);
            return;
        }
        if (nr == SYS_CLOSE)
        {
            frame->x[10] = (u64)vfs_close(frame->x[10]);
            return;
        }
        if (nr == SYS_GETDENTS64)
        {
            frame->x[10] = (u64)vfs_getdents64(frame->x[10], frame->x[11], frame->x[12]);
            return;
        }
        if (nr == SYS_LSEEK)
        {
            frame->x[10] = (u64)vfs_lseek(frame->x[10], frame->x[11], frame->x[12]);
            return;
        }
        if (nr == SYS_FSTAT)
        {
            frame->x[10] = (u64)vfs_fstat(frame->x[10], frame->x[11]);
            return;
        }
        if (nr == SYS_NEWFSTATAT)
        {
            frame->x[10] = (u64)vfs_newfstatat(frame->x[10], frame->x[11], frame->x[12], frame->x[13]);
            return;
        }
        if (nr == SYS_GETCWD)
        {
            frame->x[10] = (u64)sys_getcwd_impl(frame->x[10], frame->x[11]);
            return;
        }
        if (nr == SYS_IOCTL)
        {
            frame->x[10] = (u64)sys_ioctl_impl(frame->x[10], frame->x[11], frame->x[12]);
            return;
        }
        if (nr == SYS_CHDIR)
        {
            frame->x[10] = (u64)vfs_chdir(frame->x[10]);
            return;
        }
        if (nr == SYS_MKDIRAT)
        {
            frame->x[10] = (u64)vfs_mkdirat(frame->x[10], frame->x[11], frame->x[12]);
            return;
        }
        if (nr == SYS_UNLINKAT)
        {
            frame->x[10] = (u64)vfs_unlinkat(frame->x[10], frame->x[11], frame->x[12]);
            return;
        }
        if (nr == SYS_READLINKAT)
        {
            frame->x[10] = (u64)sys_readlinkat_impl(frame->x[10], frame->x[11], frame->x[12], frame->x[13]);
            return;
        }
        if (nr == SYS_UNAME)
        {
            frame->x[10] = (u64)sys_uname_impl(frame->x[10]);
            return;
        }
        if (nr == SYS_GETPID || nr == SYS_GETTID || nr == SYS_SET_TID_ADDRESS)
        {
            frame->x[10] = current_task == NULL ? 1ul : (u64)current_task->pid;
            return;
        }
        if (nr == SYS_GETPPID)
        {
            frame->x[10] = current_task == NULL ? 0ul : (u64)current_task->ppid;
            return;
        }
        if (nr == SYS_GETUID || nr == SYS_GETEUID || nr == SYS_GETGID || nr == SYS_GETEGID)
        {
            frame->x[10] = 0ul;
            return;
        }
        if (nr == SYS_SET_ROBUST_LIST || nr == SYS_RT_SIGACTION || nr == SYS_RT_SIGPROCMASK || nr == SYS_PRLIMIT64)
        {
            frame->x[10] = 0ul;
            return;
        }
        if (nr == SYS_SCHED_YIELD)
        {
            frame->x[10] = 0ul;
            scheduler_yield(frame);
            return;
        }
        if (nr == SYS_CLONE)
        {
            u64 flags = frame->x[10];
            s64 result = (s64)process_clone(frame, flags, frame->x[11]);
            frame->x[10] = (u64)result;
            if (result > 0l && (flags & CLONE_VFORK) != 0ul)
            {
                current_task->state = PROC_VFORK;
                scheduler_switch(frame);
            }
            return;
        }
        if (nr == SYS_EXECVE)
        {
            s64 result = sys_execve_impl(frame, frame->x[10], frame->x[11], frame->x[12]);
            if (result != 0l)
                frame->x[10] = (u64)result;
            return;
        }
        if (nr == SYS_WAIT4)
        {
            sys_wait4_dispatch(frame, frame->x[10], frame->x[11], frame->x[12]);
            return;
        }
        if (nr == SYS_MMAP)
        {
            frame->x[10] = (u64)sys_mmap_impl(frame->x[10], frame->x[11], frame->x[12], frame->x[13], frame->x[14], frame->x[15]);
            return;
        }
        if (nr == SYS_MUNMAP)
        {
            frame->x[10] = (u64)sys_munmap_impl(frame->x[10], frame->x[11]);
            return;
        }
        if (nr == SYS_MPROTECT)
        {
            frame->x[10] = (u64)sys_mprotect_impl(frame->x[10], frame->x[11], frame->x[12]);
            return;
        }
        if (nr == SYS_BRK)
        {
            u64 requested = frame->x[10];
            if (requested == 0ul)
                frame->x[10] = process_brk;
            else if (grow_user_brk(requested))
                frame->x[10] = process_brk;
            else
                frame->x[10] = process_brk;
            return;
        }
        if (nr == SYS_REBOOT)
        {
            frame->x[10] = (u64)sys_reboot_impl(frame->x[10], frame->x[11], frame->x[12]);
            return;
        }
        if (nr == SYS_EXIT || nr == SYS_EXIT_GROUP)
        {
            process_exit_current(frame, frame->x[10]);
            return;
        }
        frame->x[10] = (u64)-38l;
        return;
    }
    if (cause == 0x8000000000000005ul)
    {
        scheduler_timer_interrupt(frame);
        return;
    }
    if ((cause == 13ul || cause == 15ul) && (frame->sstatus & SSTATUS_SPP) == 0ul)
    {
        if (map_user_stack_fault(current_user_root_page_table, frame->stval))
            return;
        if (map_user_brk_fault(current_user_root_page_table, frame->stval))
            return;
    }

    // A fault a program took is the program's to die of; only the kernel's own are fatal
    if ((frame->sstatus & SSTATUS_SPP) == 0ul && current_task != (struct process*)NULL && current_task->used != 0u)
    {
        puts("kernel: process ");
        put_dec(current_task->pid);
        puts(" faulted, cause=");
        put_hex64(frame->scause);
        puts(" sepc=");
        put_hex64(frame->sepc);
        puts(" stval=");
        put_hex64(frame->stval);
        puts("\n");
        current_task->exit_signal = (u32)fault_signal(cause);
        process_exit_current(frame, 0ul);
        return;
    }

    puts("kernel: trap cause=");
    put_hex64(frame->scause);
    puts(" sepc=");
    put_hex64(frame->sepc);
    puts(" stval=");
    put_hex64(frame->stval);
    puts("\n");
    halt();
}

void kernel_main(u64 hartid, void* fdt)
{
    u32 init_size;
    u32 init_read_result;
    struct elf_image image;
    struct exec_arguments arguments;
    u64 stack;

    parse_fdt(fdt);
#if __riscv_vector
    __asm__ volatile("csrrs zero, sstatus, %[vs]" : : [vs] "{t0}"(SSTATUS_VS) : "memory");
#endif
    console_init();
    fbcon_init();
    memory_manager_init();
    kernel_mmu_init();
    device_mappings_init();

    if (vector_register_bytes() > MAX_VECTOR_REGISTER_BYTES)
        panic("vector registers exceed the saved context");
    if (!block_subsystem_init())
        panic("block subsystem init failed");
    if (!fat_mount())
        panic("boot FAT32 mount failed");
    init_read_result = fat_read_short_to_memory("INIT       ", (void*)USER_ELF_BUFFER, (u32)USER_ELF_BUFFER_SIZE, &init_size);
    if (init_read_result == 1u)
        panic("INIT.ELF directory entry not found");
    if (init_read_result == 2u)
        panic("INIT.ELF exceeds staging buffer");
    if (init_read_result == 3u)
        panic("INIT.ELF data read failed");
    if (init_read_result == 4u)
        panic("INIT.ELF cluster chain is truncated");
    process_table_init();
    vfs_init();

    current_user_root_page_table = create_user_address_space();
    current_task->root_page_table = current_user_root_page_table;
    if (!load_process_image(current_user_root_page_table, (const u8*)USER_ELF_BUFFER, init_size, &image))
        panic("invalid INIT.ELF");
    if (!make_kernel_arguments(init_path, &arguments))
        panic("init arguments setup failed");
    if (!build_user_stack(current_user_root_page_table, &image, &arguments, &stack))
        panic("user stack setup failed");

    current_task->root_page_table = current_user_root_page_table;
    current_task->brk = process_brk;
    current_task->brk_min = process_brk_min;
    current_task->mmap_cursor = user_mmap_cursor;
    activate_page_table(current_user_root_page_table);
    timer_enable();
    kernel_enter_user(image.start_entry, stack);
    halt();
}
