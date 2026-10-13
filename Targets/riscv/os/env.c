#include <stdio.h>
#include <unistd.h>

int main(void)
{
    char** entry = environ;
    while (entry != NULL && *entry != NULL)
    {
        puts(*entry);
        entry++;
    }
    return 0;
}
