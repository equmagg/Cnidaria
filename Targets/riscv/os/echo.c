#include <stdio.h>
#include <string.h>

int main(int argc, char** argv)
{
    int index = 1;
    int newline = 1;

    if (index < argc && strcmp(argv[index], "-n") == 0)
    {
        newline = 0;
        index++;
    }

    while (index < argc)
    {
        fputs(argv[index], stdout);
        index++;
        if (index < argc)
            fputc(' ', stdout);
    }

    if (newline)
        fputc('\n', stdout);
    return 0;
}
