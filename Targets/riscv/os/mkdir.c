int main(int argc, char** argv, char** envp)
{
    int index = 1;
    int status = 0;
    (void)envp;

    if (argc < 2)
    {
        report("mkdir", (const char*)NULL, "usage: mkdir name...");
        return 2;
    }

    while (index < argc)
    {
        s64 result = sys_mkdirat((s64)AT_FDCWD, argv[index], 493ul);
        if (result < 0l)
        {
            report("mkdir", argv[index],
                result == -17l ? "already exists" : result == -2l ? "has no parent directory" : "cannot be created");
            status = 1;
        }
        index = index + 1;
    }
    return status;
}
