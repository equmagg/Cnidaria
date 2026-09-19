int main(int argc, char** argv, char** envp)
{
    int index = 0;
    (void)argc;
    (void)argv;

    if (envp == (char**)NULL)
        return 0;
    while (envp[index] != (char*)NULL)
    {
        write_text(envp[index]);
        write_text("\n");
        index = index + 1;
    }
    return 0;
}
