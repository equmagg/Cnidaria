#include <stdlib.h>

int __libc_argc;
char** __libc_argv;
char** environ;
int __libc_initialized;
int errno;

void __libc_init(int argc, char** argv, char** envp)
{
    if (__libc_initialized != 0)
        return;
    __libc_argc = argc;
    __libc_argv = argv;
    environ = envp;
    __libc_initialized = 1;
}

#if defined(__linux__)
// What a C program's _start calls, as with glibc: returning from main is calling exit with its value
int __libc_start_main(int (*main)(int, char**, char**), int argc, char** argv, void (*init)(void), void (*fini)(void),
    void (*rtld_fini)(void), void* stack_end)
{
    char** envp = argv + argc + 1;
    int status;
    (void)init;
    (void)fini;
    (void)rtld_fini;
    (void)stack_end;
    __libc_init(argc, argv, envp);
    status = main(argc, argv, envp);
    exit(status);
    return status;
}
#endif
