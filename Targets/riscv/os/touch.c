int main(int argc, char** argv, char** envp)
{
    int index = 1;
    int status = 0;
    (void)envp;

    if (argc < 2)
    {
        report("touch", (const char*)NULL, "usage: touch file...");
        return 2;
    }

    while (index < argc)
    {
        /* A name that is already there is left as it is, which is what touch promises */
        s64 fd = sys_openat((s64)AT_FDCWD, argv[index], O_WRONLY | O_CREAT, 420ul);
        if (fd < 0l)
        {
            report("touch", argv[index], "cannot be created");
            status = 1;
        }
        else
        {
            sys_close((int)fd);
        }
        index = index + 1;
    }
    return status;
}
