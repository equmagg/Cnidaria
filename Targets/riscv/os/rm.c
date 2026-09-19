int main(int argc, char** argv, char** envp)
{
    int index = 1;
    int quiet = 0;
    int status = 0;
    (void)envp;

    while (index < argc && argv[index][0] == '-' && argv[index][1] != 0)
    {
        if (string_equal(argv[index], "-f"))
            quiet = 1;
        else
        {
            report("rm", (const char*)NULL, "usage: rm [-f] file...");
            return 2;
        }
        index = index + 1;
    }

    if (index == argc)
    {
        if (quiet)
            return 0;
        report("rm", (const char*)NULL, "usage: rm [-f] file...");
        return 2;
    }

    while (index < argc)
    {
        s64 result = sys_unlinkat((s64)AT_FDCWD, argv[index], 0ul);
        if (result < 0l && !quiet)
        {
            report("rm", argv[index], result == -2l ? "no such file" : result == -1l ? "is not a file" : "cannot be removed");
            status = 1;
        }
        index = index + 1;
    }
    return status;
}
