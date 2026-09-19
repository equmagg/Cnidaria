#define HEAD_BUFFER 512

static int head_parse(const char* text, u64* value)
{
    u64 result = 0ul;
    if (*text == 0)
        return 0;
    while (*text != 0)
    {
        if (*text < '0' || *text > '9')
            return 0;
        result = result * 10ul + (u64)(*text - '0');
        text = text + 1;
    }
    *value = result;
    return 1;
}

static int head_stream(int fd, u64 wanted)
{
    char buffer[HEAD_BUFFER];
    u64 seen = 0ul;
    while (seen < wanted)
    {
        usize index = 0ul;
        s64 count = sys_read(fd, buffer, (usize)HEAD_BUFFER);
        if (count < 0l)
            return 1;
        if (count == 0l)
            return 0;
        while (index < (usize)count && seen < wanted)
        {
            write_all(1, buffer + index, 1ul);
            if (buffer[index] == '\n')
                seen = seen + 1ul;
            index = index + 1ul;
        }
    }
    return 0;
}

int main(int argc, char** argv, char** envp)
{
    u64 wanted = 10ul;
    int index = 1;
    int status = 0;
    int files;
    (void)envp;

    while (index < argc && argv[index][0] == '-' && argv[index][1] != 0)
    {
        if (string_equal(argv[index], "-n") && index + 1 < argc)
        {
            if (!head_parse(argv[index + 1], &wanted))
            {
                report("head", argv[index + 1], "is not a count of lines");
                return 2;
            }
            index = index + 2;
            continue;
        }
        report("head", (const char*)NULL, "usage: head [-n count] [file...]");
        return 2;
    }

    if (index == argc)
        return head_stream(0, wanted);

    files = argc - index;
    while (index < argc)
    {
        int fd = open_for_reading("head", argv[index]);
        if (fd < 0)
        {
            status = 1;
            index = index + 1;
            continue;
        }
        /* Several files each say which one they are, the way head has always done it */
        if (files > 1)
        {
            write_text("==> ");
            write_text(argv[index]);
            write_text(" <==\n");
        }
        if (head_stream(fd, wanted) != 0)
            status = 1;
        sys_close(fd);
        index = index + 1;
        if (files > 1 && index < argc)
            write_text("\n");
    }
    return status;
}
