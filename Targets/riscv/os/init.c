// Every program init starts leads a session of its own, with the console as the terminal it controls
static int run_session(char** argv, char** envp)
{
    s64 pid = sys_clone(SIGCHLD, 0ul);
    int status = 0;
    if (pid < 0l)
        return (int)pid;
    if (pid == 0l)
    {
        s64 result;
        sys_setsid();
        sys_ioctl(0, TIOCSCTTY, 0ul);
        result = sys_execve(argv[0], argv, envp);
        write_text("exec failed: ");
        write_text(argv[0]);
        write_text("\n");
        sys_exit(result == -2l ? 127 : 126);
    }
    // Orphans end up here, so whatever else ends is reaped until the one waited for does
    for (;;)
    {
        s64 done = sys_wait4(-1l, &status, 0ul);
        if (done < 0l)
            return -1;
        if (done == pid)
            return (status >> 8) & 255;
    }
}

int main(int argc, char** argv, char** envp)
{
    char* autorun_argv[2];
    char* shell_argv[2];
    int result;
    (void)argc;
    (void)argv;

    autorun_argv[0] = "/autorun";
    autorun_argv[1] = (char*)NULL;
    shell_argv[0] = "/shell";
    shell_argv[1] = (char*)NULL;

    result = run_session(autorun_argv, envp);
    if (result < 0)
        write_text("init: autorun could not be started\n");

    for (;;)
    {
        result = run_session(shell_argv, envp);
        if (result < 0)
            write_text("init: shell could not be started\n");
        else
            write_text("init: shell exited, restarting\n");
    }
}
