#include <stdio.h>
#include <unistd.h>

int main(void)
{
    char path[256];
    if (getcwd(path, sizeof(path)) == NULL)
    {
        fputs("pwd: the working directory cannot be read\n", stderr);
        return 1;
    }
    puts(path);
    return 0;
}
