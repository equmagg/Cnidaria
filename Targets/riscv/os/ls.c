#define LS_MAX_ENTRIES 128
#define LS_NAME_CAPACITY 32
#define LS_TERMINAL_COLUMNS 80

static char ls_names[LS_MAX_ENTRIES][LS_NAME_CAPACITY];
static u32 ls_kinds[LS_MAX_ENTRIES];
static int ls_count;

static void ls_remember(const char* name, u32 kind)
{
    int index = 0;
    if (ls_count == LS_MAX_ENTRIES)
        return;
    while (index < LS_NAME_CAPACITY - 1 && name[index] != 0)
    {
        ls_names[ls_count][index] = name[index];
        index = index + 1;
    }
    ls_names[ls_count][index] = 0;
    ls_kinds[ls_count] = kind;
    ls_count = ls_count + 1;
}

static int ls_before(const char* left, const char* right)
{
    usize index = 0ul;
    while (left[index] != 0 && right[index] != 0)
    {
        if (left[index] != right[index])
            return (u8)left[index] < (u8)right[index];
        index = index + 1ul;
    }
    return right[index] != 0;
}

/* The names come out of the directory in the order it stores them, and a listing is sorted */
static void ls_sort(void)
{
    int outer = 1;
    while (outer < ls_count)
    {
        char name[LS_NAME_CAPACITY];
        u32 kind = ls_kinds[outer];
        int inner = outer - 1;
        int index = 0;
        while (index < LS_NAME_CAPACITY)
        {
            name[index] = ls_names[outer][index];
            index = index + 1;
        }
        while (inner >= 0 && ls_before(name, ls_names[inner]))
        {
            index = 0;
            while (index < LS_NAME_CAPACITY)
            {
                ls_names[inner + 1][index] = ls_names[inner][index];
                index = index + 1;
            }
            ls_kinds[inner + 1] = ls_kinds[inner];
            inner = inner - 1;
        }
        index = 0;
        while (index < LS_NAME_CAPACITY)
        {
            ls_names[inner + 1][index] = name[index];
            index = index + 1;
        }
        ls_kinds[inner + 1] = kind;
        outer = outer + 1;
    }
}

static u16 ls_load_u16(const u8* bytes)
{
    return (u16)((u16)bytes[0] | ((u16)bytes[1] << 8));
}

/* A flat volume holds every name at the root, so a name is a path with a slash in front */
static void ls_build_path(const char* directory, const char* name, char* path)
{
    int index = 0;
    /* A name in the working directory stands on its own; anywhere else it hangs off its directory */
    if (!(directory[0] == '.' && directory[1] == 0))
    {
        if (!(directory[0] == '/' && directory[1] == 0))
        {
            while (directory[index] != 0 && index < LS_NAME_CAPACITY)
            {
                path[index] = directory[index];
                index = index + 1;
            }
        }
        path[index] = '/';
        index = index + 1;
    }
    while (*name != 0 && index < LS_NAME_CAPACITY * 2 - 1)
    {
        path[index] = *name;
        index = index + 1;
        name = name + 1;
    }
    path[index] = 0;
}

/* The nine bits every listing spells out as three groups of three */
static void ls_write_permissions(u32 mode)
{
    int bit = 8;
    while (bit >= 0)
    {
        int set = (mode & (1u << bit)) != 0u;
        write_text(!set ? "-" : bit % 3 == 2 ? "r" : bit % 3 == 1 ? "w" : "x");
        bit = bit - 1;
    }
}

static void ls_write_long(const char* directory, const char* name, u32 kind)
{
    char path[LS_NAME_CAPACITY * 2];
    u32 mode;
    ls_build_path(directory, name, path);
    mode = path_mode(path);
    if (mode == 0u)
        mode = kind == DT_DIR ? S_IFDIR : kind == DT_CHR ? S_IFCHR : S_IFREG;

    write_text((mode & S_IFMT) == S_IFDIR ? "d" : (mode & S_IFMT) == S_IFCHR ? "c" : "-");
    ls_write_permissions(mode);
    write_text(" ");
    write_unsigned_to(1, (mode & S_IFMT) == S_IFDIR ? 0ul : path_size(path), 8, ' ');
    write_text(" ");
    write_text(name);
    write_text("\n");
}

/* Names go across the screen the way a terminal shows them, as many to a line as fit */
static void ls_write_columns(void)
{
    int widest = 0;
    int width;
    int columns;
    int index = 0;
    while (index < ls_count)
    {
        int length = (int)string_length(ls_names[index]);
        if (length > widest)
            widest = length;
        index = index + 1;
    }
    width = widest + 2;
    columns = LS_TERMINAL_COLUMNS / width;
    if (columns < 1)
        columns = 1;

    index = 0;
    while (index < ls_count)
    {
        int column = index % columns;
        int padding = width - (int)string_length(ls_names[index]);
        write_text(ls_names[index]);
        index = index + 1;
        if (column == columns - 1 || index == ls_count)
        {
            write_text("\n");
            continue;
        }
        while (padding > 0)
        {
            write_text(" ");
            padding = padding - 1;
        }
    }
}

static int ls_directory(const char* path, int all, int longFormat, int onePerLine)
{
    u8 buffer[512];
    s64 fd = sys_openat((s64)AT_FDCWD, path, O_RDONLY, 0ul);
    int index;
    if (fd < 0l)
    {
        report("ls", path, fd == -2l ? "no such file or directory" : "cannot be opened");
        return 1;
    }

    ls_count = 0;
    for (;;)
    {
        s64 count = sys_getdents64((int)fd, buffer, 512ul);
        usize offset = 0ul;
        if (count <= 0l)
            break;
        while (offset < (usize)count)
        {
            u16 record = ls_load_u16(buffer + offset + 16ul);
            const char* name;
            if (record < 20u || offset + (usize)record > (usize)count)
                break;
            name = (const char*)(buffer + offset + 19ul);
            if (all || name[0] != '.')
                ls_remember(name, (u32)buffer[offset + 18ul]);
            offset = offset + (usize)record;
        }
    }
    sys_close((int)fd);
    ls_sort();

    index = 0;
    while (index < ls_count)
    {
        if (longFormat)
            ls_write_long(path, ls_names[index], ls_kinds[index]);
        else if (onePerLine)
        {
            write_text(ls_names[index]);
            write_text("\n");
        }
        index = index + 1;
    }
    if (!longFormat && !onePerLine)
        ls_write_columns();
    return 0;
}

int main(int argc, char** argv, char** envp)
{
    int all = 0;
    int longFormat = 0;
    int onePerLine = 0;
    int index = 1;
    int paths = 0;
    int status = 0;
    (void)envp;

    while (index < argc && argv[index][0] == '-' && argv[index][1] != 0)
    {
        const char* flags = argv[index] + 1;
        while (*flags != 0)
        {
            if (*flags == 'a')
                all = 1;
            else if (*flags == 'l')
                longFormat = 1;
            else if (*flags == '1')
                onePerLine = 1;
            else
            {
                report("ls", (const char*)NULL, "usage: ls [-a] [-l] [-1] [path...]");
                return 2;
            }
            flags = flags + 1;
        }
        index = index + 1;
    }

    paths = argc - index;
    if (paths == 0)
        return ls_directory(".", all, longFormat, onePerLine);

    while (index < argc)
    {
        u32 mode = path_mode(argv[index]);
        if (mode != 0u && (mode & S_IFMT) != S_IFDIR)
        {
            if (longFormat)
                ls_write_long("", argv[index], DT_REG);
            else
            {
                write_text(argv[index]);
                write_text("\n");
            }
        }
        else
        {
            if (paths > 1)
            {
                write_text(argv[index]);
                write_text(":\n");
            }
            if (ls_directory(argv[index], all, longFormat, onePerLine) != 0)
                status = 1;
            if (paths > 1 && index + 1 < argc)
                write_text("\n");
        }
        index = index + 1;
    }
    return status;
}
