#define CAT_BUFFER 512

static int cat_line;
static int cat_at_start = 1;

static void cat_write_numbered(const char* text, usize count)
{
    usize index = 0ul;
    while (index < count)
    {
        if (cat_at_start)
        {
            cat_line = cat_line + 1;
            write_unsigned_to(1, (u64)cat_line, 6, ' ');
            write_text("  ");
            cat_at_start = 0;
        }
        write_all(1, text + index, 1ul);
        cat_at_start = text[index] == '\n';
        index = index + 1ul;
    }
}

static int cat_stream(int fd, int numbered)
{
    char buffer[CAT_BUFFER];
    for (;;)
    {
        s64 count = sys_read(fd, buffer, (usize)CAT_BUFFER);
        if (count < 0l)
            return 1;
        if (count == 0l)
            return 0;
        if (numbered)
            cat_write_numbered(buffer, (usize)count);
        else
            write_all(1, buffer, (usize)count);
    }
}

int main(int argc, char** argv, char** envp)
{
    int numbered = 0;
    int index = 1;
    int status = 0;
    (void)envp;

    while (index < argc && argv[index][0] == '-' && argv[index][1] != 0)
    {
        if (string_equal(argv[index], "-n"))
            numbered = 1;
        else
        {
            report("cat", (const char*)NULL, "usage: cat [-n] [file...]");
            return 2;
        }
        index = index + 1;
    }

    /* With nothing named, a filter reads what it is given */
    if (index == argc)
        return cat_stream(0, numbered);

    while (index < argc)
    {
        int fd;
        if (string_equal(argv[index], "-"))
        {
            if (cat_stream(0, numbered) != 0)
                status = 1;
            index = index + 1;
            continue;
        }
        fd = open_for_reading("cat", argv[index]);
        if (fd < 0)
        {
            status = 1;
            index = index + 1;
            continue;
        }
        if (cat_stream(fd, numbered) != 0)
            status = 1;
        sys_close(fd);
        index = index + 1;
    }
    return status;
}
