#include <stdio.h>
#include <sys/utsname.h>

int main(int argc, char** argv)
{
    struct utsname name;
    const char* fields[5];
    int wanted = 0;
    int index = 1;
    int written = 0;

    while (index < argc && argv[index][0] == '-' && argv[index][1] != 0)
    {
        const char* flags = argv[index] + 1;
        for (; *flags != 0; flags++)
        {
            switch (*flags)
            {
            case 'a':
                wanted = 31;
                break;
            case 's':
                wanted |= 1;
                break;
            case 'n':
                wanted |= 2;
                break;
            case 'r':
                wanted |= 4;
                break;
            case 'v':
                wanted |= 8;
                break;
            case 'm':
                wanted |= 16;
                break;
            default:
                fputs("uname: usage: uname [-asnrvm]\n", stderr);
                return 2;
            }
        }
        index++;
    }

    if (uname(&name) < 0)
    {
        fputs("uname: the system name cannot be read\n", stderr);
        return 1;
    }
    if (wanted == 0)
        wanted = 1;

    fields[0] = name.sysname;
    fields[1] = name.nodename;
    fields[2] = name.release;
    fields[3] = name.version;
    fields[4] = name.machine;
    for (index = 0; index < 5; index++)
    {
        if ((wanted & (1 << index)) == 0)
            continue;
        if (written)
            fputc(' ', stdout);
        fputs(fields[index], stdout);
        written = 1;
    }
    fputc('\n', stdout);
    return 0;
}
