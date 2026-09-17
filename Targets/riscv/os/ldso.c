typedef unsigned char u8;
typedef unsigned short u16;
typedef unsigned int u32;
typedef unsigned long u64;
typedef signed long s64;
typedef unsigned long usize;

#define SYS_OPENAT 56ul
#define SYS_CLOSE 57ul
#define SYS_LSEEK 62ul
#define SYS_READ 63ul
#define SYS_WRITE 64ul
#define SYS_EXIT 93ul
#define SYS_MMAP 222ul
#define SYS_MPROTECT 226ul

#define AT_FDCWD ((u64)-100l)
#define O_RDONLY 0ul
#define SEEK_SET 0ul
#define PAGE_SIZE 4096ul
#define PROT_READ 1ul
#define PROT_WRITE 2ul
#define PROT_EXEC 4ul
#define MAP_PRIVATE 2ul
#define MAP_ANONYMOUS 32ul

#define AT_NULL 0ul
#define AT_PHDR 3ul
#define AT_PHNUM 5ul
#define AT_ENTRY 9ul

#define PT_LOAD 1u
#define PT_DYNAMIC 2u
#define PF_W 2u
#define PF_X 1u

#define DT_NULL 0ul
#define DT_NEEDED 1ul
#define DT_PLTRELSZ 2ul
#define DT_HASH 4ul
#define DT_STRTAB 5ul
#define DT_SYMTAB 6ul
#define DT_RELA 7ul
#define DT_RELASZ 8ul
#define DT_INIT 12ul
#define DT_JMPREL 23ul

#define R_RISCV_32 1ul
#define R_RISCV_64 2ul
#define R_RISCV_RELATIVE 3ul
#define R_RISCV_JUMP_SLOT 5ul

#define MAX_OBJECTS 8
#define MAX_PHDRS 16
#define PATH_LIMIT 64
#define NAME_LIMIT 32

struct elf_header
{
    u8 ident[16];
    u16 type;
    u16 machine;
    u32 version;
    u64 entry;
    u64 phoff;
    u64 shoff;
    u32 flags;
    u16 ehsize;
    u16 phentsize;
    u16 phnum;
    u16 shentsize;
    u16 shnum;
    u16 shstrndx;
};

struct elf_phdr
{
    u32 type;
    u32 flags;
    u64 offset;
    u64 vaddr;
    u64 paddr;
    u64 filesz;
    u64 memsz;
    u64 align;
};

struct elf_sym
{
    u32 name;
    u8 info;
    u8 other;
    u16 shndx;
    u64 value;
    u64 size;
};

struct elf_rela
{
    u64 offset;
    u64 info;
    s64 addend;
};

struct elf_dyn
{
    u64 tag;
    u64 value;
};

struct shared_object
{
    char name[NAME_LIMIT];
    u64 base;
    u64 dynamic;
    u64 symbols;
    u64 strings;
    u64 hash;
    u64 rela;
    u64 rela_size;
    u64 jmprel;
    u64 jmprel_size;
    u64 init;
    u64 needed[MAX_OBJECTS];
    struct elf_phdr segments[MAX_PHDRS];
    u32 segment_count;
    u32 needed_count;
    u32 loaded;
};

typedef void (*init_function)(int, char**, char**);

static struct shared_object objects[MAX_OBJECTS];
static u32 object_count;
static struct elf_phdr phdr_buffer[MAX_PHDRS];

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

static s64 syscall6(u64 number, u64 arg0, u64 arg1, u64 arg2, u64 arg3, u64 arg4, u64 arg5)
{
    u64 result;
    __asm__ volatile("ecall\nmv %[result], a0" : [result] "=r"(result) : [arg0] "{a0}"(arg0), [arg1] "{a1}"(arg1), [arg2] "{a2}"(arg2), [arg3] "{a3}"(arg3), [arg4] "{a4}"(arg4), [arg5] "{a5}"(arg5), [number] "{a7}"(number) : "memory");
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

static void report(const char* text)
{
    syscall3(SYS_WRITE, 2ul, (u64)text, string_length(text));
}

static void fail(const char* text)
{
    report("ld.so: ");
    report(text);
    report("\n");
    syscall1(SYS_EXIT, 127ul);
    for (;;)
    {
    }
}

static u64 align_down(u64 value, u64 alignment)
{
    return value - (value % alignment);
}

static u64 align_up(u64 value, u64 alignment)
{
    return align_down(value + alignment - 1ul, alignment);
}

static u32 elf_hash(const char* name)
{
    u32 hash = 0u;
    u32 high;
    usize index = 0ul;
    while (name[index] != 0)
    {
        hash = (hash << 4) + (u32)(u8)name[index];
        high = hash & 0xf0000000u;
        if (high != 0u)
            hash = hash ^ (high >> 24);
        hash = hash & ~high;
        index = index + 1ul;
    }
    return hash;
}

static void read_dynamic(struct shared_object* object)
{
    struct elf_dyn* entry = (struct elf_dyn*)object->dynamic;
    while (entry->tag != DT_NULL)
    {
        if (entry->tag == DT_SYMTAB)
            object->symbols = object->base + entry->value;
        else if (entry->tag == DT_STRTAB)
            object->strings = object->base + entry->value;
        else if (entry->tag == DT_HASH)
            object->hash = object->base + entry->value;
        else if (entry->tag == DT_RELA)
            object->rela = object->base + entry->value;
        else if (entry->tag == DT_RELASZ)
            object->rela_size = entry->value;
        else if (entry->tag == DT_JMPREL)
            object->jmprel = object->base + entry->value;
        else if (entry->tag == DT_PLTRELSZ)
            object->jmprel_size = entry->value;
        else if (entry->tag == DT_INIT)
            object->init = object->base + entry->value;
        else if (entry->tag == DT_NEEDED && object->needed_count < (u32)MAX_OBJECTS)
        {
            object->needed[object->needed_count] = entry->value;
            object->needed_count = object->needed_count + 1u;
        }
        entry = entry + 1;
    }
}

static u64 lookup_symbol(const char* name)
{
    u32 index = 0u;
    u32 hash = elf_hash(name);
    while (index < object_count)
    {
        struct shared_object* object = &objects[index];
        index = index + 1u;
        if (object->hash == 0ul || object->symbols == 0ul || object->strings == 0ul)
            continue;
        {
            u32* table = (u32*)object->hash;
            u32 buckets = table[0];
            u32 slot;
            if (buckets == 0u)
                continue;
            slot = table[2u + (hash % buckets)];
            while (slot != 0u)
            {
                struct elf_sym* symbol = ((struct elf_sym*)object->symbols) + slot;
                if (symbol->shndx != 0u && string_equal((const char*)(object->strings + (u64)symbol->name), name))
                    return object->base + symbol->value;
                slot = table[2u + buckets + slot];
            }
        }
    }
    return 0ul;
}

static void apply_relocations(struct shared_object* object, u64 table, u64 size)
{
    u64 offset = 0ul;
    while (offset < size)
    {
        struct elf_rela* relocation = (struct elf_rela*)(table + offset);
        u64 type = relocation->info & 0xfffffffful;
        u64 index = relocation->info >> 32;
        u64* target = (u64*)(object->base + relocation->offset);
        offset = offset + (u64)sizeof(struct elf_rela);

        if (type == R_RISCV_RELATIVE)
        {
            *target = object->base + (u64)relocation->addend;
            continue;
        }
        if (type == R_RISCV_JUMP_SLOT || type == R_RISCV_64 || type == R_RISCV_32)
        {
            struct elf_sym* symbol = ((struct elf_sym*)object->symbols) + index;
            const char* name = (const char*)(object->strings + (u64)symbol->name);
            u64 value = lookup_symbol(name);
            if (value == 0ul)
            {
                report("ld.so: unresolved symbol ");
                report(name);
                report("\n");
                syscall1(SYS_EXIT, 127ul);
            }
            if (type == R_RISCV_JUMP_SLOT)
                *target = value;
            else if (type == R_RISCV_64)
                *target = value + (u64)relocation->addend;
            else
                *((u32*)target) = (u32)(value + (u64)relocation->addend);
            continue;
        }
        fail("unsupported relocation");
    }
}

static int read_exact(int descriptor, u64 file_offset, void* destination, u64 count)
{
    u64 done = 0ul;
    if (syscall3(SYS_LSEEK, (u64)descriptor, file_offset, SEEK_SET) < 0l)
        return 0;
    while (done < count)
    {
        s64 result = syscall3(SYS_READ, (u64)descriptor, (u64)destination + done, count - done);
        if (result <= 0l)
            return 0;
        done = done + (u64)result;
    }
    return 1;
}

static int already_loaded(const char* name)
{
    u32 index = 0u;
    while (index < object_count)
    {
        if (string_equal(objects[index].name, name))
            return 1;
        index = index + 1u;
    }
    return 0;
}

static void load_library(const char* name)
{
    char path[PATH_LIMIT];
    struct elf_header header;
    struct shared_object* object;
    int descriptor;
    u32 index;
    u64 lowest = ~0ul;
    u64 highest = 0ul;
    u64 span;
    s64 mapping;

    usize length = string_length(name);
    if (already_loaded(name))
        return;
    if (length + 2ul > (usize)PATH_LIMIT || length + 1ul > (usize)NAME_LIMIT)
        fail("library path is too long");
    path[0] = '/';
    index = 0u;
    while ((usize)index < length)
    {
        path[index + 1u] = name[index];
        index = index + 1u;
    }
    path[length + 1ul] = 0;

    descriptor = (int)syscall4(SYS_OPENAT, AT_FDCWD, (u64)path, O_RDONLY, 0ul);
    if (descriptor < 0)
    {
        report("ld.so: cannot open ");
        report(path);
        report("\n");
        syscall1(SYS_EXIT, 127ul);
    }
    if (!read_exact(descriptor, 0ul, &header, (u64)sizeof(struct elf_header)))
        fail("truncated library header");
    if (header.ident[0] != 0x7fu || header.ident[1] != 'E' || header.ident[2] != 'L' || header.ident[3] != 'F')
        fail("library is not an ELF image");
    if (header.phnum == 0u || (u32)header.phnum > (u32)MAX_PHDRS)
        fail("unsupported library program header count");
    if (!read_exact(descriptor, header.phoff, phdr_buffer, (u64)header.phnum * (u64)sizeof(struct elf_phdr)))
        fail("truncated library program headers");

    index = 0u;
    while (index < (u32)header.phnum)
    {
        struct elf_phdr* segment = &phdr_buffer[index];
        index = index + 1u;
        u64 start;
        u64 end;
        if (segment->type != PT_LOAD || segment->memsz == 0ul)
            continue;
        start = align_down(segment->vaddr, PAGE_SIZE);
        end = align_up(segment->vaddr + segment->memsz, PAGE_SIZE);
        if (start < lowest)
            lowest = start;
        if (end > highest)
            highest = end;
    }
    if (highest <= lowest)
        fail("library has no loadable segments");

    span = highest - lowest;
    mapping = syscall6(SYS_MMAP, 0ul, span, PROT_READ | PROT_WRITE | PROT_EXEC,
        MAP_PRIVATE | MAP_ANONYMOUS, (u64)-1l, 0ul);
    if (mapping <= 0l)
        fail("library mapping failed");

    object = &objects[object_count];
    object_count = object_count + 1u;
    object->base = (u64)mapping - lowest;
    object->loaded = 1u;
    index = 0u;
    while ((usize)index < length)
    {
        object->name[index] = name[index];
        index = index + 1u;
    }
    object->name[length] = 0;

    index = 0u;
    while (index < (u32)header.phnum)
    {
        struct elf_phdr* segment = &phdr_buffer[index];
        index = index + 1u;
        if (segment->type == PT_DYNAMIC)
            object->dynamic = object->base + segment->vaddr;
        if (segment->type != PT_LOAD || segment->filesz == 0ul)
            continue;
        if (!read_exact(descriptor, segment->offset, (void*)(object->base + segment->vaddr), segment->filesz))
            fail("truncated library segment");
    }
    syscall1(SYS_CLOSE, (u64)descriptor);

    if (object->dynamic == 0ul)
        fail("library has no dynamic section");
    read_dynamic(object);
    object->segment_count = (u32)header.phnum;
    index = 0u;
    while (index < object->segment_count)
    {
        object->segments[index] = phdr_buffer[index];
        index = index + 1u;
    }
}

static void protect_object(struct shared_object* object)
{
    u32 index = 0u;
    while (index < object->segment_count)
    {
        struct elf_phdr* segment = &object->segments[index];
        u64 start;
        u64 end;
        u64 protection;
        index = index + 1u;
        if (segment->type != PT_LOAD || segment->memsz == 0ul)
            continue;
        start = align_down(object->base + segment->vaddr, PAGE_SIZE);
        end = align_up(object->base + segment->vaddr + segment->memsz, PAGE_SIZE);
        protection = PROT_READ;
        if ((segment->flags & PF_W) != 0u)
            protection = protection | PROT_WRITE;
        if ((segment->flags & PF_X) != 0u)
            protection = protection | PROT_EXEC;
        if (protection == (PROT_READ | PROT_WRITE | PROT_EXEC))
            continue;
        if (syscall3(SYS_MPROTECT, start, end - start, protection) < 0l)
            fail("library protection failed");
    }
}

static u64 auxiliary_value(u64* auxv, u64 key)
{
    while (auxv[0] != AT_NULL)
    {
        if (auxv[0] == key)
            return auxv[1];
        auxv = auxv + 2;
    }
    return 0ul;
}

static void enter_program(u64 entry, u64 stack)
{
    __asm__ volatile(
        "mv sp, t1\n"
        "mv a0, zero\n"
        "jr t0"
        :
        : [entry] "{t0}"(entry), [stack] "{t1}"(stack)
        : "memory");
}

int main(int argc, char** argv, char** envp)
{
    u64* auxv;
    u64 phdr;
    u64 phnum;
    u64 entry;
    struct shared_object* program;
    u32 index;
    u32 pending;

    auxv = (u64*)envp;
    while (*auxv != 0ul)
        auxv = auxv + 1;
    auxv = auxv + 1;

    phdr = auxiliary_value(auxv, AT_PHDR);
    phnum = auxiliary_value(auxv, AT_PHNUM);
    entry = auxiliary_value(auxv, AT_ENTRY);
    if (phdr == 0ul || phnum == 0ul || entry == 0ul)
        fail("the kernel did not describe the program image");

    program = &objects[0];
    object_count = 1u;
    program->base = 0ul;
    program->loaded = 1u;
    program->name[0] = 0;
    index = 0u;
    while ((u64)index < phnum)
    {
        struct elf_phdr* segment = ((struct elf_phdr*)phdr) + index;
        index = index + 1u;
        if (segment->type == PT_DYNAMIC)
            program->dynamic = segment->vaddr;
    }
    if (program->dynamic == 0ul)
        fail("the program is not dynamically linked");
    read_dynamic(program);

    pending = 0u;
    while (pending < object_count)
    {
        struct shared_object* object = &objects[pending];
        u32 needed = 0u;
        pending = pending + 1u;
        while (needed < object->needed_count)
        {
            const char* name = (const char*)(object->strings + object->needed[needed]);
            needed = needed + 1u;
            load_library(name);
        }
    }

    index = 0u;
    while (index < object_count)
    {
        struct shared_object* object = &objects[index];
        index = index + 1u;
        if (object->rela_size != 0ul)
            apply_relocations(object, object->rela, object->rela_size);
        if (object->jmprel_size != 0ul)
            apply_relocations(object, object->jmprel, object->jmprel_size);
    }

    index = 1u;
    while (index < object_count)
    {
        protect_object(&objects[index]);
        index = index + 1u;
    }

    index = object_count;
    while (index > 1u)
    {
        struct shared_object* object = &objects[index - 1u];
        index = index - 1u;
        if (object->init != 0ul)
        {
            init_function initialize = (init_function)object->init;
            initialize(argc, argv, envp);
        }
    }

    enter_program(entry, (u64)argv - 8ul);
    return 0;
}
