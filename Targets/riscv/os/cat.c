#include <errno.h>
#include <fcntl.h>
#include <stdio.h>
#include <string.h>
#include <unistd.h>

static int cat_line;
static int cat_at_start = 1;

static void cat_write(const char* text, size_t count, int numbered)
{
    size_t index;
    if (!numbered)
    {
        fwrite(text, 1, count, stdout);
        return;
    }
    for (index = 0; index < count; index++)
    {
        if (cat_at_start)
            printf("%6d  ", ++cat_line);
        fputc(text[index], stdout);
        cat_at_start = text[index] == '\n';
    }
}

static int cat_stream(int fd, int numbered)
{
    char buffer[512];
    for (;;)
    {
        ssize_t count = read(fd, buffer, sizeof(buffer));
        if (count < 0)
            return 1;
        if (count == 0)
            return 0;
        cat_write(buffer, (size_t)count, numbered);
    }
}

int main(int argc, char** argv)
{
    int numbered = 0;
    int index = 1;
    int status = 0;

    while (index < argc && argv[index][0] == '-' && argv[index][1] != 0)
    {
        if (strcmp(argv[index], "-n") != 0)
        {
            fputs("cat: usage: cat [-n] [file...]\n", stderr);
            return 2;
        }
        numbered = 1;
        index++;
    }

    // With nothing named, a filter reads what it is given
    if (index == argc)
        return cat_stream(0, numbered);

    for (; index < argc; index++)
    {
        int fd;
        if (strcmp(argv[index], "-") == 0)
        {
            status |= cat_stream(0, numbered);
            continue;
        }
        fd = open(argv[index], O_RDONLY);
        if (fd < 0)
        {
            fprintf(stderr, "cat: %s: %s\n", argv[index], errno == ENOENT ? "no such file" : errno == EISDIR ? "is a directory" : "cannot be opened");
            status = 1;
            continue;
        }
        status |= cat_stream(fd, numbered);
        close(fd);
    }
    return status;
}
