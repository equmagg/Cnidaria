#include <errno.h>
#include <stdio.h>
#include <sys/stat.h>

int main(int argc, char** argv)
{
    int index;
    int status = 0;

    if (argc < 2)
    {
        fputs("mkdir: usage: mkdir name...\n", stderr);
        return 2;
    }

    for (index = 1; index < argc; index++)
    {
        if (mkdir(argv[index], 0755) < 0)
        {
            fprintf(stderr, "mkdir: %s: %s\n", argv[index], errno == EEXIST ? "already exists" : errno == ENOENT ? "has no parent directory" : "cannot be created");
            status = 1;
        }
    }
    return status;
}
