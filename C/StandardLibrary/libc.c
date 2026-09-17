int __libc_argc;
char** __libc_argv;
char** environ;
int __libc_initialized;

void __libc_init(int argc, char** argv, char** envp)
{
    if (__libc_initialized != 0)
        return;
    __libc_argc = argc;
    __libc_argv = argv;
    environ = envp;
    __libc_initialized = 1;
}
