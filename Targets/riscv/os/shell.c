#define SHELL_LINE_CAPACITY 512
#define SHELL_MAX_WORDS 32
#define SHELL_WORD_CAPACITY 256
#define SHELL_PATH_CAPACITY 128

/* One word of a command, and what the operator after it was */
#define SHELL_END 0
#define SHELL_SEQUENCE 1
#define SHELL_AND 2
#define SHELL_OR 3

#define SHELL_MAX_VARIABLES 32

static char shell_words[SHELL_MAX_WORDS][SHELL_WORD_CAPACITY];
static char* shell_argv[SHELL_MAX_WORDS + 1];
static int shell_status;

/* The environment the shell keeps and hands to everything it runs */
static char shell_variables[SHELL_MAX_VARIABLES][SHELL_WORD_CAPACITY];
static char* shell_environment[SHELL_MAX_VARIABLES + 1];
static int shell_variable_count;

static int shell_name_length(const char* entry)
{
    int length = 0;
    while (entry[length] != 0 && entry[length] != '=')
        length = length + 1;
    return length;
}

static int shell_find_variable(const char* name, int length)
{
    int index = 0;
    while (index < shell_variable_count)
    {
        if (shell_name_length(shell_variables[index]) == length)
        {
            int position = 0;
            while (position < length && shell_variables[index][position] == name[position])
                position = position + 1;
            if (position == length)
                return index;
        }
        index = index + 1;
    }
    return -1;
}

static const char* shell_value(const char* name, int length)
{
    int index = shell_find_variable(name, length);
    if (index < 0)
        return (const char*)NULL;
    return shell_variables[index] + length + 1;
}

static void shell_set(const char* entry)
{
    int length = shell_name_length(entry);
    int index = shell_find_variable(entry, length);
    int position = 0;
    if (index < 0)
    {
        if (shell_variable_count == SHELL_MAX_VARIABLES)
            return;
        index = shell_variable_count;
        shell_variable_count = shell_variable_count + 1;
        shell_environment[index] = shell_variables[index];
        shell_environment[shell_variable_count] = (char*)NULL;
    }
    while (entry[position] != 0 && position + 1 < SHELL_WORD_CAPACITY)
    {
        shell_variables[index][position] = entry[position];
        position = position + 1;
    }
    shell_variables[index][position] = 0;
}

/* PWD follows the shell around, the way it does in a real one */
static void shell_track_directory(void)
{
    char entry[SHELL_WORD_CAPACITY];
    int index = 0;
    const char* prefix = "PWD=";
    while (prefix[index] != 0)
    {
        entry[index] = prefix[index];
        index = index + 1;
    }
    if (sys_getcwd(entry + index, (usize)(SHELL_WORD_CAPACITY - index)) < 0l)
        return;
    shell_set(entry);
}

static int read_line(char* line, int capacity)
{
    int length = 0;
    for (;;)
    {
        char ch;
        s64 result = sys_read(0, &ch, 1ul);
        if (result <= 0l)
            return -1;
        if (ch == '\r' || ch == '\n')
        {
            write_text("\n");
            line[length] = 0;
            return length;
        }
        if (ch == 8 || ch == 127)
        {
            if (length != 0)
            {
                length = length - 1;
                write_text("\b \b");
            }
        }
        else if (ch >= 32 && ch < 127 && length + 1 < capacity)
        {
            line[length] = ch;
            length = length + 1;
            write_all(1, &ch, 1ul);
        }
    }
}

static int shell_is_space(char ch)
{
    return ch == ' ' || ch == '\t';
}

/* The status of the last command, which is what $? is worth */
static int shell_append_status(char* word, int* length)
{
    int value = shell_status;
    char digits[8];
    int count = 0;
    if (value == 0)
    {
        digits[count] = '0';
        count = count + 1;
    }
    while (value != 0 && count < 8)
    {
        digits[count] = (char)('0' + (value % 10));
        value = value / 10;
        count = count + 1;
    }
    while (count != 0 && *length + 1 < SHELL_WORD_CAPACITY)
    {
        count = count - 1;
        word[*length] = digits[count];
        *length = *length + 1;
    }
    return 1;
}

static int shell_name_start(char ch)
{
    return (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || ch == '_';
}

static int shell_name_char(char ch)
{
    return shell_name_start(ch) || (ch >= '0' && ch <= '9');
}

/* Puts what a name is worth into the word being built, and steps past the name */
static void shell_append_value(char* word, int* length, const char** cursor)
{
    const char* begin = *cursor;
    int size = 0;
    const char* value;
    while (shell_name_char(begin[size]))
        size = size + 1;
    *cursor = begin + size;
    value = shell_value(begin, size);
    if (value == (const char*)NULL)
        return;
    while (*value != 0 && *length + 1 < SHELL_WORD_CAPACITY)
    {
        word[*length] = *value;
        *length = *length + 1;
        value = value + 1;
    }
}

/*
 * Splits a line the way a shell does: single quotes take everything literally, double quotes
 * take everything but an escape, a backslash outside quotes takes the next character, and a
 * hash starts a comment. The word the cursor stops on is the operator that ends the command.
 */
static int shell_split(const char** cursor, int* count, int* separator, const char** error)
{
    const char* text = *cursor;
    *count = 0;
    *separator = SHELL_END;
    *error = (const char*)NULL;

    for (;;)
    {
        int length = 0;
        int quoted = 0;
        char* word;

        while (shell_is_space(*text))
            text = text + 1;
        if (*text == '#' || *text == 0)
        {
            *cursor = *text == 0 ? text : text + string_length(text);
            return 1;
        }
        if (*text == ';')
        {
            *separator = SHELL_SEQUENCE;
            *cursor = text + 1;
            return 1;
        }
        if (*text == '&' && text[1] == '&')
        {
            *separator = SHELL_AND;
            *cursor = text + 2;
            return 1;
        }
        if (*text == '|' && text[1] == '|')
        {
            *separator = SHELL_OR;
            *cursor = text + 2;
            return 1;
        }
        if (*text == '&' || *text == '|')
        {
            *error = "a background command and a pipe are not supported";
            return 0;
        }
        if (*count == SHELL_MAX_WORDS)
        {
            *error = "too many words in one command";
            return 0;
        }

        word = shell_words[*count];
        while (*text != 0 && !shell_is_space(*text))
        {
            char ch = *text;
            if (ch == ';' || ch == '&' || ch == '|' || ch == '#')
                break;
            if (ch == '\'')
            {
                text = text + 1;
                while (*text != 0 && *text != '\'')
                {
                    if (length + 1 < SHELL_WORD_CAPACITY)
                    {
                        word[length] = *text;
                        length = length + 1;
                    }
                    text = text + 1;
                }
                if (*text != '\'')
                {
                    *error = "a quote was left open";
                    return 0;
                }
                text = text + 1;
                quoted = 1;
                continue;
            }
            if (ch == '"')
            {
                text = text + 1;
                while (*text != 0 && *text != '"')
                {
                    char inner = *text;
                    if (inner == '\\' && (text[1] == '"' || text[1] == '\\' || text[1] == '$'))
                    {
                        text = text + 1;
                        inner = *text;
                    }
                    else if (inner == '$' && text[1] == '?')
                    {
                        text = text + 2;
                        shell_append_status(word, &length);
                        continue;
                    }
                    else if (inner == '$' && shell_name_start(text[1]))
                    {
                        text = text + 1;
                        shell_append_value(word, &length, &text);
                        continue;
                    }
                    if (length + 1 < SHELL_WORD_CAPACITY)
                    {
                        word[length] = inner;
                        length = length + 1;
                    }
                    text = text + 1;
                }
                if (*text != '"')
                {
                    *error = "a quote was left open";
                    return 0;
                }
                text = text + 1;
                quoted = 1;
                continue;
            }
            if (ch == '\\')
            {
                text = text + 1;
                if (*text == 0)
                {
                    *error = "a line may not end in a backslash";
                    return 0;
                }
                ch = *text;
            }
            else if (ch == '$' && text[1] == '?')
            {
                text = text + 2;
                shell_append_status(word, &length);
                continue;
            }
            else if (ch == '$' && shell_name_start(text[1]))
            {
                text = text + 1;
                shell_append_value(word, &length, &text);
                continue;
            }
            if (length + 1 < SHELL_WORD_CAPACITY)
            {
                word[length] = ch;
                length = length + 1;
            }
            text = text + 1;
        }

        if (length != 0 || quoted)
        {
            word[length] = 0;
            *count = *count + 1;
        }
    }
}

/* Looks for a command the way a shell does: along the path, unless the name says where it is */
static int shell_locate(const char* command, char* path, int capacity)
{
    const char* search;
    int index;

    if (command[0] == '/' || (command[0] == '.' && (command[1] == '/' || (command[1] == '.' && command[2] == '/'))))
    {
        index = 0;
        while (command[index] != 0 && index + 1 < capacity)
        {
            path[index] = command[index];
            index = index + 1;
        }
        path[index] = 0;
        return path_mode(path) != 0u;
    }

    search = shell_value("PATH", 4);
    if (search == (const char*)NULL)
        search = "/";
    for (;;)
    {
        const char* element = search;
        int length = 0;
        while (*search != 0 && *search != ':')
        {
            search = search + 1;
            length = length + 1;
        }
        index = 0;
        while (index < length && index + 1 < capacity)
        {
            path[index] = element[index];
            index = index + 1;
        }
        if (index == 0 || path[index - 1] != '/')
        {
            if (index + 1 < capacity)
            {
                path[index] = '/';
                index = index + 1;
            }
        }
        {
            int position = 0;
            while (command[position] != 0 && index + 1 < capacity)
            {
                path[index] = command[position];
                index = index + 1;
                position = position + 1;
            }
        }
        path[index] = 0;
        if (path_mode(path) != 0u)
            return 1;
        if (*search == 0)
            return 0;
        search = search + 1;
    }
}

static const char* shell_signal_name(int signal)
{
    if (signal == 4)
        return "Illegal instruction";
    if (signal == 5)
        return "Trace/breakpoint trap";
    if (signal == 7)
        return "Bus error";
    if (signal == 8)
        return "Floating point exception";
    if (signal == 11)
        return "Segmentation fault";
    return "Killed";
}

/* A redirection is taken out of the words before the command sees them */
static int shell_take_redirections(int* count, const char** input, const char** output, int* append, const char** error)
{
    int read = 0;
    int write = 0;
    *input = (const char*)NULL;
    *output = (const char*)NULL;
    *append = 0;
    *error = (const char*)NULL;

    while (read < *count)
    {
        char* word = shell_words[read];
        const char** target = (const char**)NULL;
        const char* rest = (const char*)NULL;

        if (word[0] == '<')
        {
            target = input;
            rest = word + 1;
        }
        else if (word[0] == '>' && word[1] == '>')
        {
            target = output;
            *append = 1;
            rest = word + 2;
        }
        else if (word[0] == '>')
        {
            target = output;
            rest = word + 1;
        }

        if (target == (const char**)NULL)
        {
            shell_argv[write] = word;
            write = write + 1;
            read = read + 1;
            continue;
        }

        if (*rest != 0)
        {
            *target = rest;
            read = read + 1;
            continue;
        }
        read = read + 1;
        if (read == *count)
        {
            *error = "a redirection needs a file";
            return 0;
        }
        *target = shell_words[read];
        read = read + 1;
    }

    *count = write;
    shell_argv[write] = (char*)NULL;
    return 1;
}

/* The child moves its own descriptors, since the lowest free one is what an open returns */
static int shell_redirect(const char* path, int fd, u64 flags)
{
    s64 opened;
    sys_close(fd);
    opened = sys_openat((s64)AT_FDCWD, path, flags, 0ul);
    if (opened == (s64)fd)
        return 1;
    if (opened >= 0l)
        sys_close((int)opened);
    return 0;
}

static int shell_run(int count, const char* input, const char* output, int append)
{
    char path[SHELL_PATH_CAPACITY];
    s64 pid;
    int status = 0;

    if (!shell_locate(shell_argv[0], path, SHELL_PATH_CAPACITY))
    {
        report("shell", shell_argv[0], "command not found");
        return 127;
    }
    (void)count;

    pid = sys_clone(SIGCHLD, 0ul);
    if (pid < 0l)
    {
        write_text("shell: a process could not be started\n");
        return 1;
    }
    if (pid == 0l)
    {
        if (input != (const char*)NULL && !shell_redirect(input, 0, O_RDONLY))
        {
            report("shell", input, "cannot be read");
            sys_exit(1);
        }
        if (output != (const char*)NULL &&
            !shell_redirect(output, 1, O_WRONLY | O_CREAT | (append ? O_APPEND : O_TRUNC)))
        {
            report("shell", output, "cannot be written");
            sys_exit(1);
        }
        {
            s64 result = sys_execve(path, shell_argv, shell_environment);
            report("shell", shell_argv[0], result == -2l ? "command not found" : "cannot be run");
            sys_exit(result == -2l ? 127 : 126);
        }
    }
    if (sys_wait4(pid, &status, 0ul) < 0l)
        return 1;
    if ((status & 127) != 0)
    {
        int signal = status & 127;
        write_text(shell_signal_name(signal));
        write_text("\n");
        return 128 + signal;
    }
    return (status >> 8) & 255;
}

static int shell_builtin(int count, int* exiting)
{
    *exiting = 0;
    if (count == 0)
        return -1;
    if (string_equal(shell_argv[0], "exit"))
    {
        *exiting = 1;
        return count > 1 ? (int)shell_argv[1][0] - '0' : 0;
    }
    if (string_equal(shell_argv[0], "cd"))
    {
        const char* home = shell_value("HOME", 4);
        const char* target = count > 1 ? shell_argv[1] : home != (const char*)NULL ? home : "/";
        s64 result = sys_chdir(target);
        if (result >= 0l)
        {
            shell_track_directory();
            return 0;
        }
        report("cd", target, result == -20l ? "is not a directory" : "no such directory");
        return 1;
    }
    if (string_equal(shell_argv[0], "pwd") && count == 1)
    {
        char here[SHELL_LINE_CAPACITY];
        if (sys_getcwd(here, SHELL_LINE_CAPACITY) < 0l)
            return 1;
        write_text(here);
        write_text("\n");
        return 0;
    }
    if (string_equal(shell_argv[0], "export"))
    {
        int index = 1;
        while (index < count)
        {
            shell_set(shell_argv[index]);
            index = index + 1;
        }
        return 0;
    }
    if (string_equal(shell_argv[0], "help"))
    {
        write_text("cnidaria shell\n");
        write_text("  builtins: cd [path], pwd, export NAME=value, exit [status], help\n");
        write_text("  programs: ls cat cp rm mkdir rmdir touch wc head echo uname\n");
        write_text("  quoting:  'literal' \"escaped\" \\c, $NAME and $? expand\n");
        write_text("  operators: ; && || < > >>\n");
        return 0;
    }
    return -1;
}

int main(int argc, char** argv, char** envp)
{
    char line[SHELL_LINE_CAPACITY];
    (void)argc;
    (void)argv;

    /* Whatever was handed down is what the shell starts with */
    if (envp != (char**)NULL)
    {
        int index = 0;
        while (envp[index] != (char*)NULL)
        {
            shell_set(envp[index]);
            index = index + 1;
        }
    }
    if (shell_value("PATH", 4) == (const char*)NULL)
        shell_set("PATH=/bin:/");
    shell_track_directory();

    write_text("Cnidaria shell\n");
    for (;;)
    {
        const char* cursor;
        const char* here;
        int running = 1;

        here = shell_value("PWD", 3);
        write_text("cnidaria:");
        write_text(here != (const char*)NULL ? here : "?");
        write_text("$ ");
        if (read_line(line, SHELL_LINE_CAPACITY) < 0)
            return 0;

        cursor = line;
        while (running)
        {
            const char* input;
            const char* output;
            const char* error;
            int count;
            int separator;
            int append;
            int exiting;
            int builtin;

            if (!shell_split(&cursor, &count, &separator, &error))
            {
                write_text("shell: ");
                write_text(error);
                write_text("\n");
                shell_status = 2;
                break;
            }
            if (count == 0 && separator == SHELL_END)
                break;
            if (count != 0)
            {
                if (!shell_take_redirections(&count, &input, &output, &append, &error))
                {
                    write_text("shell: ");
                    write_text(error);
                    write_text("\n");
                    shell_status = 2;
                    break;
                }
                if (count == 0)
                {
                    write_text("shell: a redirection needs a command\n");
                    shell_status = 2;
                    break;
                }

                builtin = shell_builtin(count, &exiting);
                if (exiting)
                    return builtin;
                shell_status = builtin >= 0 ? builtin : shell_run(count, input, output, append);
            }

            /* What comes next depends on how the last command went */
            if (separator == SHELL_END)
                break;
            if (separator == SHELL_AND && shell_status != 0)
                running = 0;
            if (separator == SHELL_OR && shell_status == 0)
                running = 0;
            if (!running)
            {
                /* The rest of the line is skipped, but a semicolon starts it again */
                int skipped;
                int nextSeparator;
                while (shell_split(&cursor, &skipped, &nextSeparator, &error))
                {
                    if (nextSeparator == SHELL_SEQUENCE)
                    {
                        running = 1;
                        break;
                    }
                    if (nextSeparator == SHELL_END)
                        break;
                }
            }
        }
    }
}
