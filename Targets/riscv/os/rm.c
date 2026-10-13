#include <errno.h>
#include <stdio.h>
#include <string.h>
#include <unistd.h>

int main(int argc, char** argv)
{
    int index = 1;
    int quiet = 0;
    int status = 0;

    while (index < argc && argv[index][0] == '-' && argv[index][1] != 0)
    {
        if (strcmp(argv[index], "-f") != 0)
        {
            fputs("rm: usage: rm [-f] file...\n", stderr);
            return 2;
        }
        quiet = 1;
        index++;
    }

    if (index == argc)
    {
        if (quiet)
            return 0;
        fputs("rm: usage: rm [-f] file...\n", stderr);
        return 2;
    }

    for (; index < argc; index++)
    {
        if (unlink(argv[index]) < 0 && !quiet)
        {
            fprintf(stderr, "rm: %s: %s\n", argv[index], errno == ENOENT ? "no such file" : errno == EPERM || errno == EISDIR ? "is not a file" : "cannot be removed");
            status = 1;
        }
    }
    return status;
}
