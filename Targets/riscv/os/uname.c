#define UNAME_FIELD 65ul

int main(int argc, char** argv, char** envp)
{
    char uts[390];
    int all = 0;
    int wanted = 0;
    int index = 1;
    int written = 0;
    (void)envp;

    while (index < argc && argv[index][0] == '-' && argv[index][1] != 0)
    {
        const char* flags = argv[index] + 1;
        while (*flags != 0)
        {
            if (*flags == 'a')
                all = 1;
            else if (*flags == 's')
                wanted = wanted | 1;
            else if (*flags == 'n')
                wanted = wanted | 2;
            else if (*flags == 'r')
                wanted = wanted | 4;
            else if (*flags == 'v')
                wanted = wanted | 8;
            else if (*flags == 'm')
                wanted = wanted | 16;
            else
            {
                report("uname", (const char*)NULL, "usage: uname [-asnrvm]");
                return 2;
            }
            flags = flags + 1;
        }
        index = index + 1;
    }

    if (sys_uname(uts) < 0l)
    {
        report("uname", (const char*)NULL, "the system name cannot be read");
        return 1;
    }
    if (all)
        wanted = 31;
    if (wanted == 0)
        wanted = 1;

    /* The fields sit one after another, each as wide as the last */
    index = 0;
    while (index < 5)
    {
        if ((wanted & (1 << index)) != 0)
        {
            if (written)
                write_text(" ");
            write_text(uts + (usize)index * UNAME_FIELD);
            written = 1;
        }
        index = index + 1;
    }
    write_text("\n");
    return 0;
}
