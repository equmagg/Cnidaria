#include <errno.h>
#include <fcntl.h>
#include <stdio.h>
#include <unistd.h>

int main(int argc, char** argv)
{
    char buffer[512];
    int source;
    int target;

    if (argc != 3)
    {
        fputs("cp: usage: cp source target\n", stderr);
        return 2;
    }

    source = open(argv[1], O_RDONLY);
    if (source < 0)
    {
        fprintf(stderr, "cp: %s: %s\n", argv[1], errno == ENOENT ? "no such file" : errno == EISDIR ? "is a directory" : "cannot be opened");
        return 1;
    }
    target = open(argv[2], O_WRONLY | O_CREAT | O_TRUNC, 0644);
    if (target < 0)
    {
        fprintf(stderr, "cp: %s: %s\n", argv[2], errno == EISDIR ? "is a directory" : errno == ENOSPC ? "leaves no room" : "cannot be written");
        close(source);
        return 1;
    }

    for (;;)
    {
        ssize_t count = read(source, buffer, sizeof(buffer));
        ssize_t written = 0;
        if (count < 0)
        {
            fprintf(stderr, "cp: %s: cannot be read\n", argv[1]);
            close(source);
            close(target);
            return 1;
        }
        if (count == 0)
            break;
        while (written < count)
        {
            ssize_t step = write(target, buffer + written, (size_t)(count - written));
            if (step <= 0)
            {
                fprintf(stderr, "cp: %s: cannot be written\n", argv[2]);
                close(source);
                close(target);
                return 1;
            }
            written += step;
        }
    }

    close(source);
    close(target);
    return 0;
}
