int main(int argc, char** argv, char** envp)
{
    int index = 1;
    int status = 0;
    (void)envp;

    if (argc < 2)
    {
        report("rmdir", (const char*)NULL, "usage: rmdir name...");
        return 2;
    }

    while (index < argc)
    {
        s64 result = sys_unlinkat((s64)AT_FDCWD, argv[index], AT_REMOVEDIR);
        if (result < 0l)
        {
            report("rmdir", argv[index],
                result == -39l ? "is not empty" : result == -20l ? "is not a directory" : result == -2l ? "no such directory" : "cannot be removed");
            status = 1;
        }
        index = index + 1;
    }
    return status;
}
