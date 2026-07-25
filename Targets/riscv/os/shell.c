#define SHELL_LINE_CAPACITY 128
#define SHELL_MAX_ARGUMENTS 16

static int read_line(char* line, int capacity)
{
    int length = 0;
    for (;;)
    {
        char ch;
        s64 result = sys_read(0, &ch, 1ul);
        if (result <= 0l)
            return -1;
        if (ch == '\r' || ch == '\n')
        {
            write_text("\n");
            line[length] = 0;
            return length;
        }
        if (ch == 8 || ch == 127)
        {
            if (length != 0)
            {
                length = length - 1;
                write_text("\b \b");
            }
        }
        else if (ch >= 32 && ch < 127 && length + 1 < capacity)
        {
            line[length] = ch;
            length = length + 1;
            sys_write(1, &ch, 1ul);
        }
    }
}

static int split_arguments(char* line, char** arguments, int capacity)
{
    int count = 0;
    char* cursor = line;
    while (*cursor != 0)
    {
        while (*cursor == ' ' || *cursor == '\t')
            cursor = cursor + 1;
        if (*cursor == 0)
            break;
        if (count + 1 >= capacity)
            break;
        arguments[count] = cursor;
        count = count + 1;
        while (*cursor != 0 && *cursor != ' ' && *cursor != '\t')
            cursor = cursor + 1;
        if (*cursor != 0)
        {
            *cursor = 0;
            cursor = cursor + 1;
        }
    }
    arguments[count] = (char*)NULL;
    return count;
}

static int contains_dot(const char* text)
{
    while (*text != 0)
    {
        if (*text == '.')
            return 1;
        text = text + 1;
    }
    return 0;
}

static int build_program_path(const char* command, char* path, int capacity)
{
    int index = 0;
    int has_extension = contains_dot(command);
    if (*command != '/')
    {
        if (capacity < 2)
            return 0;
        path[index] = '/';
        index = index + 1;
    }
    while (*command != 0)
    {
        if (index + 1 >= capacity)
            return 0;
        path[index] = *command;
        index = index + 1;
        command = command + 1;
    }
    if (!has_extension)
    {
        if (index + 4 >= capacity)
            return 0;
        path[index] = '.';
        path[index + 1] = 'e';
        path[index + 2] = 'l';
        path[index + 3] = 'f';
        index = index + 4;
    }
    path[index] = 0;
    return 1;
}

static u16 load_u16(const u8* bytes)
{
    return (u16)((u16)bytes[0] | ((u16)bytes[1] << 8));
}

static void list_root(void)
{
    u8 buffer[512];
    s64 fd = sys_openat((s64)AT_FDCWD, "/", O_RDONLY, 0ul);
    if (fd < 0l)
    {
        write_text("ls: cannot open root directory\n");
        return;
    }
    for (;;)
    {
        s64 count = sys_getdents64((int)fd, buffer, 512ul);
        usize offset = 0ul;
        if (count <= 0l)
            break;
        while (offset < (usize)count)
        {
            u16 record_length = load_u16(buffer + offset + 16ul);
            const char* name;
            if (record_length < 20u || offset + (usize)record_length >(usize)count)
                break;
            name = (const char*)(buffer + offset + 19ul);
            write_text(name);
            write_text("\n");
            offset = offset + (usize)record_length;
        }
    }
    sys_close((int)fd);
}

int main(int argc, char** argv, char** envp)
{
    char line[SHELL_LINE_CAPACITY];
    char path[SHELL_LINE_CAPACITY];
    char* arguments[SHELL_MAX_ARGUMENTS];
    (void)argc;
    (void)argv;
    (void)envp;

    write_text("Cnidaria shell\n");
    for (;;)
    {
        int argument_count;
        int result;
        write_text("cnidaria$ ");
        if (read_line(line, SHELL_LINE_CAPACITY) < 0)
            return 0;
        argument_count = split_arguments(line, arguments, SHELL_MAX_ARGUMENTS);
        if (argument_count == 0)
            continue;
        if (string_equal(arguments[0], "exit"))
            return 0;
        if (string_equal(arguments[0], "ls"))
        {
            list_root();
            continue;
        }
        if (!build_program_path(arguments[0], path, SHELL_LINE_CAPACITY))
        {
            write_text("shell: command name is too long\n");
            continue;
        }
        result = run_program(path, arguments);
        if (result != 0)
            write_text("shell: program exited with a non-zero status\n");
    }
}
