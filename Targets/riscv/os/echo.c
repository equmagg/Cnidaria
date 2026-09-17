int main(int argc, char** argv, char** envp)
{
    int index = 1;
    int newline = 1;
    (void)envp;

    if (index < argc && string_equal(argv[index], "-n"))
    {
        newline = 0;
        index = index + 1;
    }

    while (index < argc)
    {
        write_text(argv[index]);
        index = index + 1;
        if (index < argc)
            write_text(" ");
    }

    if (newline)
        write_text("\n");
    return 0;
}
