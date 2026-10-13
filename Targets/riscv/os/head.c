#include <errno.h>
#include <fcntl.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

static int head_stream(int fd, unsigned long wanted)
{
    char buffer[512];
    unsigned long seen = 0;
    while (seen < wanted)
    {
        ssize_t index;
        ssize_t count = read(fd, buffer, sizeof(buffer));
        if (count < 0)
            return 1;
        if (count == 0)
            return 0;
        for (index = 0; index < count && seen < wanted; index++)
        {
            fputc(buffer[index], stdout);
            if (buffer[index] == '\n')
                seen++;
        }
    }
    return 0;
}

int main(int argc, char** argv)
{
    unsigned long wanted = 10;
    int index = 1;
    int status = 0;
    int files;

    while (index < argc && argv[index][0] == '-' && argv[index][1] != 0)
    {
        char* end;
        if (strcmp(argv[index], "-n") != 0 || index + 1 >= argc)
        {
            fputs("head: usage: head [-n count] [file...]\n", stderr);
            return 2;
        }
        wanted = strtoul(argv[index + 1], &end, 10);
        if (argv[index + 1][0] < '0' || argv[index + 1][0] > '9' || *end != 0)
        {
            fprintf(stderr, "head: %s: is not a count of lines\n", argv[index + 1]);
            return 2;
        }
        index += 2;
    }

    if (index == argc)
        return head_stream(0, wanted);

    files = argc - index;
    for (; index < argc; index++)
    {
        int fd = open(argv[index], O_RDONLY);
        if (fd < 0)
        {
            fprintf(stderr, "head: %s: %s\n", argv[index], errno == ENOENT ? "no such file" : errno == EISDIR ? "is a directory" : "cannot be opened");
            status = 1;
            continue;
        }
        // Several files each say which one they are, the way head has always done it
        if (files > 1)
            printf("==> %s <==\n", argv[index]);
        status |= head_stream(fd, wanted);
        close(fd);
        if (files > 1 && index + 1 < argc)
            fputc('\n', stdout);
    }
    return status;
}
