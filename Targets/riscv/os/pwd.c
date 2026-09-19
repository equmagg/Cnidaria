int main(int argc, char** argv, char** envp)
{
    char path[256];
    (void)argc;
    (void)argv;
    (void)envp;

    if (sys_getcwd(path, 256ul) < 0l)
    {
        report("pwd", (const char*)NULL, "the working directory cannot be read");
        return 1;
    }
    write_text(path);
    write_text("\n");
    return 0;
}
