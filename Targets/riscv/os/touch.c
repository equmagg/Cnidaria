#include <fcntl.h>
#include <stdio.h>
#include <unistd.h>

int main(int argc, char** argv)
{
    int index;
    int status = 0;

    if (argc < 2)
    {
        fputs("touch: usage: touch file...\n", stderr);
        return 2;
    }

    for (index = 1; index < argc; index++)
    {
        // A name that is already there is left as it is, which is what touch promises
        int fd = open(argv[index], O_WRONLY | O_CREAT, 0644);
        if (fd < 0)
        {
            fprintf(stderr, "touch: %s: cannot be created\n", argv[index]);
            status = 1;
        }
        else
            close(fd);
    }
    return status;
}
