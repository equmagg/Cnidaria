#include <errno.h>
#include <stdio.h>
#include <unistd.h>

int main(int argc, char** argv)
{
    int index;
    int status = 0;

    if (argc < 2)
    {
        fputs("rmdir: usage: rmdir name...\n", stderr);
        return 2;
    }

    for (index = 1; index < argc; index++)
    {
        if (rmdir(argv[index]) < 0)
        {
            fprintf(stderr, "rmdir: %s: %s\n", argv[index],
                errno == ENOTEMPTY ? "is not empty" : errno == ENOTDIR ? "is not a directory" : errno == ENOENT ? "no such directory" : "cannot be removed");
            status = 1;
        }
    }
    return status;
}
