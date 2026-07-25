int main(int argc, char** argv, char** envp)
{
    char* autorun_argv[2];
    char* shell_argv[2];
    int result;
    (void)argc;
    (void)argv;
    (void)envp;

    autorun_argv[0] = "/autorun.elf";
    autorun_argv[1] = (char*)NULL;
    shell_argv[0] = "/shell.elf";
    shell_argv[1] = (char*)NULL;

    result = run_program(autorun_argv[0], autorun_argv);
    if (result < 0)
        write_text("init: autorun could not be started\n");

    for (;;)
    {
        result = run_program(shell_argv[0], shell_argv);
        if (result < 0)
            write_text("init: shell could not be started\n");
        else
            write_text("init: shell exited, restarting\n");
    }
}
