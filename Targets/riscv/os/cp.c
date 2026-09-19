#define CP_BUFFER 512

int main(int argc, char** argv, char** envp)
{
    char buffer[CP_BUFFER];
    int source;
    int target;
    (void)envp;

    if (argc != 3)
    {
        report("cp", (const char*)NULL, "usage: cp source target");
        return 2;
    }

    source = open_for_reading("cp", argv[1]);
    if (source < 0)
        return 1;
    target = open_for_writing("cp", argv[2], 0);
    if (target < 0)
    {
        sys_close(source);
        return 1;
    }

    for (;;)
    {
        s64 count = sys_read(source, buffer, (usize)CP_BUFFER);
        if (count < 0l)
        {
            report("cp", argv[1], "cannot be read");
            sys_close(source);
            sys_close(target);
            return 1;
        }
        if (count == 0l)
            break;
        write_all(target, buffer, (usize)count);
    }

    sys_close(source);
    sys_close(target);
    return 0;
}
