#include <errno.h>
#include <stdio.h>
#include <sys/ioctl.h>
#include <sys/wait.h>
#include <unistd.h>

// Every program init starts leads a session of its own, with the console as the terminal it controls
static int run_session(char* const* argv)
{
    pid_t pid = fork();
    int status = 0;
    if (pid < 0)
        return -1;
    if (pid == 0)
    {
        setsid();
        ioctl(0, TIOCSCTTY, 0);
        execve(argv[0], argv, environ);
        printf("exec failed: %s\n", argv[0]);
        _exit(errno == ENOENT ? 127 : 126);
    }
    // Orphans end up here, so whatever else ends is reaped until the one waited for does
    for (;;)
    {
        pid_t done = waitpid(-1, &status, 0);
        if (done < 0)
            return -1;
        if (done == pid)
            return WEXITSTATUS(status);
    }
}

int main(void)
{
    char* autorun[] = { "/autorun", NULL };
    char* shell[] = { "/shell", NULL };

    if (run_session(autorun) < 0)
        puts("init: autorun could not be started");

    for (;;)
    {
        if (run_session(shell) < 0)
            puts("init: shell could not be started");
        else
            puts("init: shell exited, restarting");
    }
}
