typedef unsigned char u8;
typedef unsigned short u16;
typedef unsigned int u32;
typedef unsigned long long u64;
typedef signed long long s64;
typedef unsigned long usize;

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
#define SATP_MODE_SV39 0x8000000000000000ul
#define ELF_PF_X 1u
#define ELF_PF_W 2u
#define ELF_PF_R 4u
#define VIRTIO_QUEUE_SIZE 8u
#define SECTOR_SIZE 512u
#define FAT_EOC 0x0ffffff8u
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
#define SYS_UNAME 160ul
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
#define MAX_OPEN_FILES 32u
#define PATH_BUFFER_SIZE 128u
#define VFS_NODE_NONE 0u
#define VFS_NODE_CONSOLE 1u
#define VFS_NODE_NULL 2u
#define VFS_NODE_ZERO 3u
#define VFS_NODE_FAT_FILE 4u
#define VFS_NODE_ROOT_DIR 5u
#define VFS_NODE_DEV_DIR 6u
#define O_ACCMODE 3ul
#define O_WRONLY 1ul
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
#define MAX_EXEC_ARGS 16u
#define MAX_EXEC_ARG_BYTES 512u
#define PROC_UNUSED 0u
#define PROC_RUNNABLE 1u
#define PROC_RUNNING 2u
#define PROC_WAITING 3u
#define PROC_ZOMBIE 4u
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
};

struct boot_device
{
    u64 virtio_blk_base;
    u64 uart_base;
    u64 ram_base;
    u64 ram_size;
};

struct fat32_volume
{
    u32 partition_lba;
    u32 fat_lba;
    u32 data_lba;
    u32 root_cluster;
    u32 sectors_per_cluster;
    u32 fat_sectors;
};

struct elf_image
{
    u64 entry;
    u64 phdr;
    u64 phent;
    u64 phnum;
    u64 brk_start;
};

struct exec_arguments
{
    u32 count;
    u32 bytes_used;
    u32 offsets[MAX_EXEC_ARGS];
    char bytes[MAX_EXEC_ARG_BYTES];
};

struct block_device
{
    u64 base;
    u64 sector_count;
    int present;
};

struct vfs_node
{
    u32 type;
    u32 first_cluster;
    u32 size;
    u32 mode;
};

struct file_descriptor
{
    u32 used;
    u32 flags;
    u64 offset;
    struct vfs_node node;
};

struct process
{
    u32 used;
    u32 state;
    u32 pid;
    u32 ppid;
    u32 exit_status;
    u32 time_slice;
    u64 root_page_table;
    u64 brk;
    u64 brk_min;
    u64 mmap_cursor;
    u64 wait_pid;
    u64 wait_status_pointer;
    struct trap_frame frame;
    struct file_descriptor files[MAX_OPEN_FILES];
};

extern void kernel_enter_user(u64 entry, u64 stack);

static struct boot_device boot_device;
static struct fat32_volume boot_volume;
static u64 kernel_root_page_table;
static u64 current_user_root_page_table;
static u64 free_page_cursor;
static u64 free_page_end;
static u64 process_brk;
static u64 process_brk_min;
static u64 user_mmap_cursor;
static u32 virtio_avail_index;
static u32 virtio_used_index;
static u64 virtq_desc[16];
static u16 virtq_avail[2 + VIRTIO_QUEUE_SIZE];
static u16 virtq_used_raw[2 + VIRTIO_QUEUE_SIZE * 4];
static u8 virtio_request[17];
static u8 sector_buffer[SECTOR_SIZE];
static u8 fat_buffer[SECTOR_SIZE];
static u8 dir_buffer[SECTOR_SIZE];
static struct block_device root_block_device;
static struct process processes[MAX_PROCESSES];
static struct process* current_task;
static struct file_descriptor* open_files;
static u32 current_task_slot;
static u32 next_pid;
static u64 scheduler_ticks;
static int console_ready;
static const char init_path[] = "/init.elf";

static int user_copy_to_writable(u64 root, u64 destination, const void* source, u64 count);
static int copy_user_string(u64 source, char* destination, u32 capacity);
static int vfs_lookup(const char* path, struct vfs_node* node);
static int fat_read_path_to_memory(const char* path, void* destination, u32 max_size, u32* out_size);
static int fat_read_at(struct vfs_node* node, u64 offset, void* destination, u32 count, u32* read_count);
static int load_elf64(const u8* image, u32 image_size, u64 root, struct elf_image* loaded);
static s64 capture_exec_arguments(u64 argv, const char* fallback, struct exec_arguments* arguments);
static int make_kernel_arguments(const char* path, struct exec_arguments* arguments);
static int build_user_stack(u64 root, struct elf_image* image, struct exec_arguments* arguments, u64* out_stack);

static u64 align_down(u64 value, u64 alignment)
{
    return value & ~(alignment - 1ul);
}

static u64 align_up(u64 value, u64 alignment)
{
    return (value + alignment - 1ul) & ~(alignment - 1ul);
}

static void fence_rw(void)
{
    __asm__ volatile("fence rw, rw" : : : "memory");
}

static volatile u8* mmio8(u64 address)
{
    return (volatile u8*)address;
}

static u8 mmio_read8(u64 address)
{
    return *mmio8(address);
}

static void mmio_write8(u64 address, u8 value)
{
    *mmio8(address) = value;
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

static int uart_can_read(void)
{
    return (mmio_read8(boot_device.uart_base + 5ul) & 1u) != 0u;
}

static int uart_try_read(void)
{
    if (!uart_can_read())
        return -1;
    return (int)mmio_read8(boot_device.uart_base);
}

static int uart_read_blocking(void)
{
    while (!uart_can_read())
    {
    }
    return (int)mmio_read8(boot_device.uart_base);
}

static void uart_putchar(int ch)
{
    while ((mmio_read8(boot_device.uart_base + 5ul) & 32u) == 0u)
    {
    }
    mmio_write8(boot_device.uart_base, (u8)ch);
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
        u64 vl;
        __asm__ volatile(
            "vsetvli %[vl], %[count], e8, m8, ta, ma\n"
            "vle8.v v8, (%[src])\n"
            "vse8.v v8, (%[dst])"
            : [vl] "={a3}"(vl)
            : [count] "{a0}"(count), [src] "{a1}"(s), [dst] "{a2}"(d)
            : "memory");
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

    {
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
    }

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
    while (count != 0ul)
    {
        u64 vl;
        __asm__ volatile(
            "vsetvli %[vl], %[count], e8, m8, ta, ma\n"
            "vxor.vv v8, v8, v8\n"
            "vse8.v v8, (%[dst])"
            : [vl] "={a2}"(vl)
            : [count] "{a0}"(count), [dst] "{a1}"(bytes)
            : "memory");
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

static int mem_equal(const void* a, const void* b, u64 count)
{
    const u8* x = (const u8*)a;
    const u8* y = (const u8*)b;
    while (count != 0)
    {
        if (*x != *y)
            return 0;
        x = x + 1;
        y = y + 1;
        count = count - 1;
    }
    return 1;
}

static u16 le16(const u8* p)
{
    return (u16)((u16)p[0] | ((u16)p[1] << 8));
}

static u32 le32(const u8* p)
{
    return (u32)p[0] | ((u32)p[1] << 8) | ((u32)p[2] << 16) | ((u32)p[3] << 24);
}

static u64 le64(const u8* p)
{
    return (u64)le32(p) | ((u64)le32(p + 4) << 32);
}

static u32 be32(const u8* p)
{
    return ((u32)p[0] << 24) | ((u32)p[1] << 16) | ((u32)p[2] << 8) | (u32)p[3];
}

static void store_le16(u8* p, u16 value)
{
    p[0] = (u8)value;
    p[1] = (u8)(value >> 8);
}

static void store_le32(u8* p, u32 value)
{
    p[0] = (u8)value;
    p[1] = (u8)(value >> 8);
    p[2] = (u8)(value >> 16);
    p[3] = (u8)(value >> 24);
}

static void store_le64(u8* p, u64 value)
{
    store_le32(p, (u32)value);
    store_le32(p + 4, (u32)(value >> 32));
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

static volatile u32* mmio32(u64 address)
{
    return (volatile u32*)address;
}

static u32 mmio_read32(u64 base, u32 offset)
{
    return *mmio32(base + (u64)offset);
}

static void mmio_write32(u64 base, u32 offset, u32 value)
{
    *mmio32(base + (u64)offset) = value;
}

static volatile u64* mmio64(u64 address)
{
    return (volatile u64*)address;
}

static u64 mmio_read64(u64 address)
{
    return *mmio64(address);
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
    if (ranges_overlap(address, size, KERNEL_STACK_TOP - KERNEL_STACK_RESERVE_SIZE, KERNEL_STACK_RESERVE_SIZE))
        return KERNEL_STACK_TOP;
    return address;
}

static void memory_manager_init(void)
{
    free_page_cursor = align_up(KERNEL_RESERVED_END, PAGE_SIZE);
    free_page_end = align_down(ram_end(), PAGE_SIZE);
    if (free_page_cursor >= free_page_end)
        panic("no usable physical memory");
}

static u64 alloc_page_raw(void)
{
    u64 page;
    u64 reserved_limit;
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

static u64 alloc_page(void) 
{
    u64 page = alloc_page_raw();
    mem_zero((void*)page, PAGE_SIZE);
    return page;
}

static u64 pte_make(u64 physical, u64 flags)
{
    return ((physical >> 12) << 10) | flags | PTE_V;
}

static u64 pte_physical(u64 pte)
{
    return (pte >> 10) << 12;
}

static u32 sv39_index(u64 virtual_address, int level)
{
    return (u32)((virtual_address >> (12ul + (u64)level * 9ul)) & 511ul);
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
    if (ranges_overlap(address, size, CLINT_MMIO_BASE, 0x00010000ul))
        return 0;
    if (ranges_overlap(address, size, PLIC_MMIO_BASE, 0x00400000ul))
        return 0;
    return 1;
}

static int user_mapping_range_valid(u64 address, u64 size)
{
    if (!user_address_range_valid(address, size))
        return 0;
    if (ranges_overlap(address, size, USER_STACK_TOP - USER_STACK_SIZE, USER_STACK_SIZE))
        return 0;
    return 1;
}

static int user_va_canonical(u64 address)
{
    return address < USER_VA_LIMIT;
}

static int user_translate(u64 root, u64 virtual_address, u64 required, u64* physical)
{
    u64 table = root;
    int level = 2;
    if (!user_va_canonical(virtual_address))
        return 0;
    for (;;)
    {
        u64 pte = ((u64*)table)[sv39_index(virtual_address, level)];
        if ((pte & PTE_V) == 0ul || ((pte & PTE_W) != 0ul && (pte & PTE_R) == 0ul))
            return 0;
        if ((pte & (PTE_R | PTE_X)) != 0ul)
        {
            u64 offset_mask;
            if ((pte & PTE_U) == 0ul)
                return 0;
            if ((required & PTE_R) != 0ul && (pte & PTE_R) == 0ul)
                return 0;
            if ((required & PTE_W) != 0ul && (pte & PTE_W) == 0ul)
                return 0;
            if ((required & PTE_X) != 0ul && (pte & PTE_X) == 0ul)
                return 0;
            offset_mask = (1ul << (12ul + (u64)level * 9ul)) - 1ul;
            *physical = (pte_physical(pte) & ~offset_mask) | (virtual_address & offset_mask);
            return 1;
        }
        if (level == 0)
            return 0;
        table = pte_physical(pte);
        level = level - 1;
    }
}

static int user_physical(u64 root, u64 virtual_address, u64* physical)
{
    u64 table = root;
    int level = 2;
    if (!user_va_canonical(virtual_address))
        return 0;
    for (;;)
    {
        u64 pte = ((u64*)table)[sv39_index(virtual_address, level)];
        if ((pte & PTE_V) == 0ul || ((pte & PTE_W) != 0ul && (pte & PTE_R) == 0ul))
            return 0;
        if ((pte & (PTE_R | PTE_X)) != 0ul)
        {
            u64 offset_mask;
            if ((pte & PTE_U) == 0ul)
                return 0;
            offset_mask = (1ul << (12ul + (u64)level * 9ul)) - 1ul;
            *physical = (pte_physical(pte) & ~offset_mask) | (virtual_address & offset_mask);
            return 1;
        }
        if (level == 0)
            return 0;
        table = pte_physical(pte);
        level = level - 1;
    }
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
        if (!user_physical(root, destination + done, &physical))
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

static int map_user_stack(u64 root)
{
    u64 page = USER_STACK_TOP - USER_STACK_SIZE;
    while (page < USER_STACK_TOP)
    {
        u64 physical = alloc_page();
        map_page(root, page, physical, PTE_R | PTE_W | PTE_U | PTE_A | PTE_D);
        page = page + PAGE_SIZE;
    }
    return 1;
}

static void map_kernel_address_space(u64 root)
{
    u64 ram_size = align_down(ram_end() - RAM_BASE, 0x200000ul);
    map_range_2m(root, RAM_BASE, RAM_BASE, ram_size, PTE_R | PTE_W | PTE_X | PTE_A | PTE_D);
    map_range_4k(root, boot_device.uart_base, boot_device.uart_base, 0x00010000ul, PTE_R | PTE_W | PTE_A | PTE_D);
    map_range_4k(root, boot_device.virtio_blk_base, boot_device.virtio_blk_base, 0x00001000ul, PTE_R | PTE_W | PTE_A | PTE_D);
    map_range_4k(root, CLINT_MMIO_BASE, CLINT_MMIO_BASE, 0x00010000ul, PTE_R | PTE_W | PTE_A | PTE_D);
    map_range_4k(root, PLIC_MMIO_BASE, PLIC_MMIO_BASE, 0x00400000ul, PTE_R | PTE_W | PTE_A | PTE_D);
}

static u64 create_user_address_space(void)
{
    u64 root = alloc_page();
    map_kernel_address_space(root);
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
    return mmio_read64(CLINT_MMIO_BASE + CLINT_MTIME_OFFSET);
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
        if ((pte & PTE_V) != 0ul)
        {
            u64 virtual_address = virtual_prefix | ((u64)index << (12ul + (u64)level * 9ul));
            if (virtual_address < USER_VA_LIMIT)
            {
                if ((pte & (PTE_R | PTE_X)) != 0ul)
                {
                    if ((pte & PTE_U) != 0ul)
                    {
                        if (!copy_user_leaf_pages(dst_root, virtual_address, pte, level))
                            return 0;
                    }
                }
                else if (level > 0)
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
    u64 unsupported = CLONE_VM | CLONE_FS | CLONE_FILES | CLONE_SIGHAND | CLONE_VFORK | CLONE_THREAD | CLONE_SETTLS | CLONE_PARENT_SETTID | CLONE_CHILD_CLEARTID | CLONE_CHILD_SETTID;
    if ((flags & unsupported) != 0ul)
        return -22;
    if ((flags & 255ul) != 0ul && (flags & 255ul) != SIGCHLD)
        return -22;
    slot = process_alloc_slot();
    if (slot < 0)
        return -11;
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
    mem_copy(child->files, open_files, sizeof(struct file_descriptor) * (u64)MAX_OPEN_FILES);
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

static int process_store_wait_status(struct process* parent, u64 status_pointer, u32 exit_status)
{
    u32 wait_status = exit_status << 8;
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
                if (!process_store_wait_status(parent, status_pointer, exit_status))
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

static void process_wake_waiter(struct process* child)
{
    u32 index = 0u;
    while (index < MAX_PROCESSES)
    {
        struct process* parent = &processes[index];
        if (parent->used != 0u && parent->state == PROC_WAITING && parent->pid == child->ppid && process_match_wait_pid(child, parent->wait_pid))
        {
            if (process_store_wait_status(parent, parent->wait_status_pointer, child->exit_status))
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
    puts("kernel: process ");
    put_dec(pid);
    puts(" exited with status ");
    put_dec(code);
    puts("\n");
    process_wake_waiter(current_task);
    scheduler_switch(frame);
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
    (void)envp;
    if (!copy_user_string(path_pointer, path, PATH_BUFFER_SIZE))
        return -14l;
    {
        s64 argument_result = capture_exec_arguments(argv, path, &arguments);
        if (argument_result != 0l)
            return argument_result;
    }
    if (!fat_read_path_to_memory(path, (void*)USER_ELF_BUFFER, (u32)USER_ELF_BUFFER_SIZE, &image_size))
        return -2l;
    new_root = create_user_address_space();
    if (!load_elf64((const u8*)USER_ELF_BUFFER, image_size, new_root, &image))
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
    frame->sepc = image.entry;
    frame->sstatus = (frame->sstatus & ~SSTATUS_SPP) | SSTATUS_SPIE;
    frame->x[2] = stack;
    frame->x[10] = 0ul;
    current_task->root_page_table = current_user_root_page_table;
    current_task->brk = process_brk;
    current_task->brk_min = process_brk_min;
    current_task->mmap_cursor = user_mmap_cursor;
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
    int depth = -1;

    boot_device.virtio_blk_base = VIRTIO_MMIO_DEFAULT_BASE;
    boot_device.uart_base = UART_MMIO_BASE;
    boot_device.ram_base = RAM_BASE;
    boot_device.ram_size = RAM_LIMIT - RAM_BASE;

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

static void virtq_set_desc(u32 index, u64 address, u32 length, u16 flags, u16 next)
{
    u8* p = (u8*)virtq_desc;
    p = p + (u64)index * 16ul;
    ((u64*)p)[0] = address;
    ((u32*)(p + 8))[0] = length;
    ((u16*)(p + 12))[0] = flags;
    ((u16*)(p + 14))[0] = next;
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
    virtio_avail_index = 0;
    virtio_used_index = 0;

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
    u32 sectors = (bytes + SECTOR_SIZE - 1u) / SECTOR_SIZE;
    u32 transfer_bytes = sectors * SECTOR_SIZE;
    u16 data_flags = 1u;

    if (type == 0u)
        data_flags = 3u;

    mem_zero(virtio_request, sizeof(virtio_request));
    store_le32(virtio_request + 0, type);
    store_le32(virtio_request + 4, 0u);
    store_le64(virtio_request + 8, sector);
    virtio_request[16] = 255u;

    virtq_set_desc(0, (u64)virtio_request, 16u, 1u, 1u);
    virtq_set_desc(1, (u64)buffer, transfer_bytes, data_flags, 2u);
    virtq_set_desc(2, (u64)(virtio_request + 16), 1u, 2u, 0u);
    virtq_avail[0] = 1u;
    virtq_avail[2 + (virtio_avail_index & (VIRTIO_QUEUE_SIZE - 1u))] = 0u;
    virtio_avail_index = virtio_avail_index + 1u;
    virtq_avail[1] = (u16)virtio_avail_index;

    fence_rw();
    mmio_write32(base, 0x050u, 0u);
    while (virtio_request[16] == 255u)
    {
    }
    mmio_write32(base, 0x064u, mmio_read32(base, 0x060u));
    virtio_used_index = virtio_used_index + 1u;
    return virtio_request[16] == 0u;
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

static int block_read_sector(u64 lba, void* buffer)
{
    if (!root_block_device.present)
        return 0;
    if (lba >= root_block_device.sector_count)
        return 0;
    return virtio_blk_read(lba, buffer, SECTOR_SIZE);
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
    boot_volume.data_lba = boot_volume.fat_lba + boot_volume.fat_sectors * (u32)bpb[16];
    if (boot_volume.sectors_per_cluster == 0u || boot_volume.fat_sectors == 0u || boot_volume.root_cluster < 2u)
        return 0;
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
    if (!disk_read_sector(lba, fat_buffer))
        return FAT_READ_ERROR;
    return le32(fat_buffer + sector_offset) & 0x0fffffffu;
}

static int fat_find_short(const char* short_name, u32* first_cluster, u32* size)
{
    u32 cluster = boot_volume.root_cluster;
    while (cluster >= 2u && cluster < FAT_EOC)
    {
        u32 sector_index = 0u;
        while (sector_index < boot_volume.sectors_per_cluster)
        {
            u32 lba = fat_cluster_lba(cluster) + sector_index;
            u32 offset = 0u;
            if (!disk_read_sector(lba, dir_buffer))
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
                    *first_cluster = ((u32)le16(entry + 20) << 16) | (u32)le16(entry + 26);
                    *size = le32(entry + 28);
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


static int user_copy_to_writable(u64 root, u64 destination, const void* source, u64 count)
{
    const u8* src = (const u8*)source;
    u64 done = 0ul;
    while (done < count)
    {
        u64 physical;
        u64 page_offset;
        u64 chunk;
        if (!user_translate(root, destination + done, PTE_W, &physical))
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
        if (!user_translate(root, source + done, PTE_R, &physical))
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
        u8 ch;
        if (!user_load_u8(current_user_root_page_table, source + (u64)index, &ch))
            return 0;
        destination[index] = (char)ch;
        if (ch == 0u)
            return 1;
        index = index + 1u;
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

static int make_kernel_arguments(const char* path, struct exec_arguments* arguments)
{
    mem_zero(arguments, sizeof(struct exec_arguments));
    return add_kernel_argument(arguments, path);
}

static s64 capture_exec_arguments(u64 argv, const char* fallback, struct exec_arguments* arguments)
{
    u32 argument_index = 0u;
    mem_zero(arguments, sizeof(struct exec_arguments));
    if (argv != 0ul)
    {
        while (argument_index < MAX_EXEC_ARGS)
        {
            u64 source;
            u32 offset;
            u32 string_index = 0u;
            if (!user_copy_from_readable(current_user_root_page_table, &source, argv + (u64)argument_index * 8ul, 8ul))
                return -14l;
            if (source == 0ul)
                break;
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
            arguments->offsets[argument_index] = offset;
            arguments->count = arguments->count + 1u;
            argument_index = argument_index + 1u;
        }
        if (argument_index == MAX_EXEC_ARGS)
        {
            u64 next;
            if (!user_copy_from_readable(current_user_root_page_table, &next, argv + (u64)argument_index * 8ul, 8ul))
                return -14l;
            if (next != 0ul)
                return -7l;
        }
    }
    if (arguments->count == 0u && !add_kernel_argument(arguments, fallback))
        return -7l;
    return 0l;
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

static int vfs_lookup(const char* path, struct vfs_node* node)
{
    char short_name[11];
    u32 first_cluster;
    u32 size;
    if (path_is_root(path))
    {
        node->type = VFS_NODE_ROOT_DIR;
        node->first_cluster = boot_volume.root_cluster;
        node->size = 0u;
        node->mode = S_IFDIR | 365u;
        return 1;
    }
    if (path_equal_literal_skip_root(path, "dev"))
    {
        node->type = VFS_NODE_DEV_DIR;
        node->first_cluster = 0u;
        node->size = 0u;
        node->mode = S_IFDIR | 365u;
        return 1;
    }
    if (path_equal_literal_skip_root(path, "dev/console") || path_equal_literal_skip_root(path, "dev/tty") || path_equal_literal_skip_root(path, "dev/ttyS0"))
    {
        node->type = VFS_NODE_CONSOLE;
        node->first_cluster = 0u;
        node->size = 0u;
        node->mode = S_IFCHR | 438u;
        return 1;
    }
    if (path_equal_literal_skip_root(path, "dev/null"))
    {
        node->type = VFS_NODE_NULL;
        node->first_cluster = 0u;
        node->size = 0u;
        node->mode = S_IFCHR | 438u;
        return 1;
    }
    if (path_equal_literal_skip_root(path, "dev/zero"))
    {
        node->type = VFS_NODE_ZERO;
        node->first_cluster = 0u;
        node->size = 0u;
        node->mode = S_IFCHR | 438u;
        return 1;
    }
    if (!path_to_fat_short_name(path, short_name))
        return 0;
    if (!fat_find_short(short_name, &first_cluster, &size))
        return 0;
    node->type = VFS_NODE_FAT_FILE;
    node->first_cluster = first_cluster;
    node->size = size;
    node->mode = S_IFREG | 365u;
    return 1;
}

static u32 fat_read_short_to_memory(const char* short_name, void* destination, u32 max_size, u32* out_size)
{
    u32 cluster;
    u32 file_size;
    u32 remaining;
    u8* dst = (u8*)destination;

    *out_size = 0u;
    if (!fat_find_short(short_name, &cluster, &file_size))
        return 1u;
    if (file_size > max_size)
        return 2u;

    remaining = file_size;
    while (remaining != 0u)
    {
        u32 sector_index = 0u;
        while (sector_index < boot_volume.sectors_per_cluster && remaining != 0u)
        {
            u32 copy = remaining < SECTOR_SIZE ? remaining : SECTOR_SIZE;
            if (!disk_read_sector(fat_cluster_lba(cluster) + sector_index, sector_buffer))
                return 3u;
            mem_copy(dst, sector_buffer, copy);
            dst = dst + copy;
            remaining = remaining - copy;
            sector_index = sector_index + 1u;
        }
        if (remaining != 0u)
        {
            cluster = fat_next_cluster(cluster);
            if (cluster == FAT_READ_ERROR)
                return 3u;
            if (cluster < 2u || cluster >= FAT_EOC)
                return 4u;
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

    skip_clusters = offset / cluster_size;
    inner_offset = offset - skip_clusters * cluster_size;
    while (skip_clusters != 0ul)
    {
        cluster = fat_next_cluster(cluster);
        if (cluster == FAT_READ_ERROR)
            return 0;
        if (cluster < 2u || cluster >= FAT_EOC)
            return 0;
        skip_clusters = skip_clusters - 1ul;
    }

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
        u8 ch = (u8)uart_read_blocking();
        if (!user_copy_to_writable(current_user_root_page_table, buffer + done, &ch, 1ul))
            return -14l;
        done = done + 1ul;
        if ((flags & (u32)O_NONBLOCK) != 0u && !uart_can_read())
            break;
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
    u64 i = 0ul;
    while (i < count)
    {
        u8 ch;
        if (!user_load_u8(current_user_root_page_table, buffer + i, &ch))
            return -14l;
        console_putchar((int)ch);
        i = i + 1ul;
    }
    return (s64)count;
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
    if (type == VFS_NODE_FAT_FILE || type == VFS_NODE_ROOT_DIR || type == VFS_NODE_DEV_DIR)
        return -30l;
    return -9l;
}

static s64 vfs_openat(u64 dirfd, u64 path_pointer, u64 flags, u64 mode)
{
    char path[PATH_BUFFER_SIZE];
    struct vfs_node node;
    int fd;
    (void)mode;
    if (dirfd != AT_FDCWD && !fd_valid(dirfd))
        return -9l;
    if (!copy_user_string(path_pointer, path, PATH_BUFFER_SIZE))
        return -14l;
    if (!vfs_lookup(path, &node))
        return -2l;
    if ((node.type == VFS_NODE_ROOT_DIR || node.type == VFS_NODE_DEV_DIR) && ((flags & O_ACCMODE) != 0ul))
        return -21l;
    if (node.type == VFS_NODE_FAT_FILE && ((flags & O_ACCMODE) != 0ul))
        return -30l;
    fd = fd_alloc();
    if (fd < 0)
        return -24l;
    fd_install((u32)fd, &node, flags);
    return (s64)fd;
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

static int fat_root_entry_at(u64 requested, char* name, u32* type, u64* inode)
{
    u32 cluster = boot_volume.root_cluster;
    u64 current = 0ul;
    while (cluster >= 2u && cluster < FAT_EOC)
    {
        u32 sector_index = 0u;
        while (sector_index < boot_volume.sectors_per_cluster)
        {
            u32 offset = 0u;
            if (!disk_read_sector(fat_cluster_lba(cluster) + sector_index, dir_buffer))
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
    if (file->node.type != VFS_NODE_ROOT_DIR && file->node.type != VFS_NODE_DEV_DIR)
        return -20l;
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
                if (file->offset < 3ul || !fat_root_entry_at(file->offset - 3ul, fat_name, &type, &inode))
                    break;
                name = fat_name;
            }
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
        build_dirent64(dir_buffer, inode, file->offset + 1ul, type, name, record_length);
        if (!user_copy_to_writable(current_user_root_page_table, user_buffer + done, dir_buffer, (u64)record_length))
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
    store_le32(stat_buffer + 20, node->type == VFS_NODE_ROOT_DIR || node->type == VFS_NODE_DEV_DIR ? 2u : 1u);
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

static s64 sys_getcwd_impl(u64 user_buffer, u64 size)
{
    char cwd[2];
    if (size < 2ul)
        return -34l;
    cwd[0] = '/';
    cwd[1] = 0;
    if (!user_copy_to_writable(current_user_root_page_table, user_buffer, cwd, 2ul))
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

static s64 sys_mmap_impl(u64 address, u64 length, u64 prot, u64 flags, u64 fd, u64 offset)
{
    u64 start;
    u64 end;
    u64 pte_flags = 0ul;
    (void)offset;
    if (length == 0ul)
        return -22l;
    if ((flags & MAP_ANONYMOUS) == 0ul)
        return -19l;
    if (fd != (u64)-1l)
        fd = fd;
    length = align_up(length, PAGE_SIZE);
    if ((flags & MAP_FIXED) != 0ul)
        start = align_down(address, PAGE_SIZE);
    else
    {
        start = align_up(user_mmap_cursor, PAGE_SIZE);
        user_mmap_cursor = start + length;
    }
    end = start + length;
    if (end < start)
        return -22l;
    if ((prot & PROT_READ) != 0ul)
        pte_flags = pte_flags | PTE_R;
    if ((prot & PROT_WRITE) != 0ul)
        pte_flags = pte_flags | PTE_R | PTE_W;
    if ((prot & PROT_EXEC) != 0ul)
        pte_flags = pte_flags | PTE_X;
    if (pte_flags == 0ul)
        pte_flags = PTE_R;
    if (!map_user_range(current_user_root_page_table, start, end, pte_flags))
        return -12l;
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
    index = 0;
    while (index < phnum)
    {
        const u8* ph = image + phoff + (u64)index * (u64)phentsize;
        u32 type = le32(ph + 0);
        if (type == 1u)
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
    loaded->phdr = 0ul;
    loaded->phent = phentsize;
    loaded->phnum = phnum;
    loaded->brk_start = align_up(high, PAGE_SIZE);
    if (!user_va_canonical(loaded->entry))
        return 0;
    if (!user_translate(root, loaded->entry, PTE_X, &entry_physical))
        return 0;
    process_brk = loaded->brk_start;
    process_brk_min = loaded->brk_start;
    user_mmap_cursor = 0x3000000000ul;
    return 1;
}

static int build_user_stack(u64 root, struct elf_image* image, struct exec_arguments* arguments, u64* out_stack)
{
    u64 strings = USER_STACK_TOP - (u64)arguments->bytes_used;
    u64 sp;
    u64 stack[MAX_EXEC_ARGS + 15u];
    u32 word = 0u;
    u32 index = 0u;
    if (arguments->count == 0u)
        return 0;
    if (!map_user_stack(root))
        return 0;
    if (!user_copy_to(root, strings, arguments->bytes, (u64)arguments->bytes_used))
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
    stack[word] = AT_NULL;
    stack[word + 1u] = 0ul;
    word = word + 2u;
    sp = align_down(align_down(strings, 16ul) - (u64)word * 8ul, 16ul);
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
    if (new_limit > old_limit)
    {
        if (!map_user_range(current_user_root_page_table, old_limit, new_limit, PTE_R | PTE_W))
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
            frame->x[10] = (u64)(s64)process_clone(frame, frame->x[10], frame->x[11]);
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
        if (nr == SYS_MUNMAP || nr == SYS_MPROTECT)
        {
            frame->x[10] = 0ul;
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

    puts("Cnidaria kernel\n");
    puts("kernel: hart ");
    put_dec(hartid);
    puts(" fdt ");
    put_hex64((u64)fdt);
    puts("\n");

    parse_fdt(fdt);
#if __riscv_vector
    __asm__ volatile("csrrs zero, sstatus, %[vs]" : : [vs] "{t0}"(SSTATUS_VS) : "memory");
#endif
    console_init();
    memory_manager_init();
    kernel_mmu_init();
    puts("kernel: sv39 root ");
    put_hex64(kernel_root_page_table);
    puts("\n");
    puts("kernel: virtio-blk ");
    put_hex64(boot_device.virtio_blk_base);
    puts(" uart=");
    put_hex64(boot_device.uart_base);
    puts("\n");

    if (!block_subsystem_init())
        panic("block subsystem init failed");
    if (!fat_mount())
        panic("boot FAT32 mount failed");
    init_read_result = fat_read_short_to_memory("INIT    ELF", (void*)USER_ELF_BUFFER, (u32)USER_ELF_BUFFER_SIZE, &init_size);
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
    if (!load_elf64((const u8*)USER_ELF_BUFFER, init_size, current_user_root_page_table, &image))
        panic("invalid INIT.ELF");
    if (!make_kernel_arguments(init_path, &arguments))
        panic("init arguments setup failed");
    if (!build_user_stack(current_user_root_page_table, &image, &arguments, &stack))
        panic("user stack setup failed");

    puts("kernel: entering user root=");
    put_hex64(current_user_root_page_table);
    puts(" entry=");
    put_hex64(image.entry);
    puts(" sp=");
    put_hex64(stack);
    puts("\n");
    current_task->root_page_table = current_user_root_page_table;
    current_task->brk = process_brk;
    current_task->brk_min = process_brk_min;
    current_task->mmap_cursor = user_mmap_cursor;
    activate_page_table(current_user_root_page_table);
    timer_enable();
    kernel_enter_user(image.entry, stack);
    halt();
}
