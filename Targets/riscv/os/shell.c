#define SHELL_LINE_CAPACITY 512
#define SHELL_MAX_WORDS 32
#define SHELL_WORD_CAPACITY 256
#define SHELL_PATH_CAPACITY 128

// One word of a command, and what the operator after it was
#define SHELL_END 0
#define SHELL_SEQUENCE 1
#define SHELL_AND 2
#define SHELL_OR 3
#define SHELL_BACKGROUND 4

#define SHELL_MAX_VARIABLES 32

#define SHELL_MAX_JOBS 16
#define SHELL_JOB_TEXT 128
#define SHELL_JOB_RUNNING 1
#define SHELL_JOB_STOPPED 2

static char shell_words[SHELL_MAX_WORDS][SHELL_WORD_CAPACITY];
static char* shell_argv[SHELL_MAX_WORDS + 1];
static int shell_status;

// The environment the shell keeps and hands to everything it runs
static char shell_variables[SHELL_MAX_VARIABLES][SHELL_WORD_CAPACITY];
static char* shell_environment[SHELL_MAX_VARIABLES + 1];
static int shell_variable_count;

// A job is one process leading a group of its own, which is what the terminal hands itself to
static s64 shell_job_pid[SHELL_MAX_JOBS];
static int shell_job_state[SHELL_MAX_JOBS];
static int shell_job_signal[SHELL_MAX_JOBS];
static char shell_job_text[SHELL_MAX_JOBS][SHELL_JOB_TEXT];
static int shell_current_job = -1;
static int shell_interactive;
static s64 shell_pgid;
static int shell_warned_stopped;
static volatile int shell_interrupted;

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

// PWD follows the shell around, the way it does in a real one
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

static void shell_on_interrupt(int signal)
{
    (void)signal;
    shell_interrupted = 1;
}

// The terminal edits the line, so what arrives is a whole line, and an interrupt throws it away
static int read_line(char* line, int capacity)
{
    int length = 0;
    for (;;)
    {
        s64 result = sys_read(0, line + length, (usize)(capacity - 1 - length));
        if (result == -4l)
        {
            if (shell_interrupted)
            {
                shell_interrupted = 0;
                write_text("\n");
                return -2;
            }
            continue;
        }
        if (result < 0l)
            return -1;
        if (result == 0l)
        {
            if (length == 0)
                return -1;
            continue;
        }
        length = length + (int)result;
        if (line[length - 1] == '\n' || length >= capacity - 1)
        {
            if (line[length - 1] == '\n')
                length = length - 1;
            line[length] = 0;
            return length;
        }
    }
}

static int shell_is_space(char ch)
{
    return ch == ' ' || ch == '\t';
}

// The status of the last command, which is what $? is worth
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

// Puts what a name is worth into the word being built, and steps past the name
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

// Splits a line the way a shell does: single quotes take everything literally, double quotes
// take everything but an escape, a backslash outside quotes takes the next character, and a
// hash starts a comment. The word the cursor stops on is the operator that ends the command.
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
        if (*text == '&')
        {
            *separator = SHELL_BACKGROUND;
            *cursor = text + 1;
            return 1;
        }
        if (*text == '|')
        {
            *error = "a pipe is not supported";
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

// Looks for a command the way a shell does: along the path, unless the name says where it is
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
    static const char* const names[] = {
        "Hangup", "Interrupt", "Quit", "Illegal instruction", "Trace/breakpoint trap", "Aborted", "Bus error",
        "Floating point exception", "Killed", "User defined signal 1", "Segmentation fault",
        "User defined signal 2", "Broken pipe", "Alarm clock", "Terminated"
    };
    if (signal >= 1 && signal <= 15)
        return names[signal - 1];
    if (signal == SIGSTOP)
        return "Stopped (signal)";
    if (signal == SIGTTIN)
        return "Stopped (tty input)";
    if (signal == SIGTTOU)
        return "Stopped (tty output)";
    if (signal == SIGTSTP)
        return "Stopped";
    return "Killed";
}

static void shell_take_terminal(void)
{
    int group = (int)shell_pgid;
    if (shell_interactive)
        sys_ioctl(0, TIOCSPGRP, (u64)&group);
}

static void shell_write_padded(const char* text, int width)
{
    write_text(text);
    width = width - (int)string_length(text);
    while (width > 0)
    {
        write_text(" ");
        width = width - 1;
    }
}

// Jobs are reported the way the shell everyone knows reports them
static void shell_report_job(int job, const char* state, int running)
{
    write_text("[");
    write_unsigned((u64)(job + 1));
    write_text(job == shell_current_job ? "]+  " : "]   ");
    shell_write_padded(state, 24);
    write_text(shell_job_text[job]);
    write_text(running ? " &\n" : "\n");
}

static void shell_forget_job(int job)
{
    int index = SHELL_MAX_JOBS;
    shell_job_pid[job] = 0l;
    if (shell_current_job != job)
        return;
    shell_current_job = -1;
    while (index != 0)
    {
        index = index - 1;
        if (shell_job_pid[index] != 0l)
        {
            shell_current_job = index;
            return;
        }
    }
}

static int shell_job_of(s64 pid)
{
    int index = 0;
    while (index < SHELL_MAX_JOBS)
    {
        if (shell_job_pid[index] == pid)
            return index;
        index = index + 1;
    }
    return -1;
}

static int shell_add_job(s64 pid, int count)
{
    int job = shell_job_of(0l);
    int length = 0;
    int word = 0;
    if (job < 0)
        return -1;
    shell_job_pid[job] = pid;
    shell_job_state[job] = SHELL_JOB_RUNNING;
    shell_job_signal[job] = 0;
    while (word < count)
    {
        const char* text = shell_argv[word];
        if (word != 0 && length + 1 < SHELL_JOB_TEXT)
            shell_job_text[job][length++] = ' ';
        while (*text != 0 && length + 1 < SHELL_JOB_TEXT)
            shell_job_text[job][length++] = *text++;
        word = word + 1;
    }
    shell_job_text[job][length] = 0;
    shell_current_job = job;
    return job;
}

// What a job ended or stopped with, told once and then forgotten or kept
static void shell_job_changed(int job, int status)
{
    if ((status & 255) == 127)
    {
        shell_job_state[job] = SHELL_JOB_STOPPED;
        shell_job_signal[job] = (status >> 8) & 255;
        shell_current_job = job;
        shell_report_job(job, shell_signal_name(shell_job_signal[job]), 0);
        return;
    }
    if (status == 0xffff)
    {
        shell_job_state[job] = SHELL_JOB_RUNNING;
        return;
    }
    if ((status & 127) != 0)
        shell_report_job(job, shell_signal_name(status & 127), 0);
    else if (((status >> 8) & 255) != 0)
    {
        char state[16] = "Exit ";
        int value = (status >> 8) & 255;
        int length = 5;
        if (value >= 100)
            state[length++] = (char)('0' + value / 100);
        if (value >= 10)
            state[length++] = (char)('0' + value / 10 % 10);
        state[length++] = (char)('0' + value % 10);
        state[length] = 0;
        shell_report_job(job, state, 0);
    }
    else
        shell_report_job(job, "Done", 0);
    shell_forget_job(job);
}

static void shell_reap_jobs(void)
{
    for (;;)
    {
        int status = 0;
        s64 pid = sys_wait4(-1l, &status, WNOHANG | WUNTRACED | WCONTINUED);
        int job;
        if (pid <= 0l)
            return;
        job = shell_job_of(pid);
        if (job >= 0)
            shell_job_changed(job, status);
    }
}

// The foreground job has the terminal until it ends or stops, and then the shell takes it back
static int shell_wait_foreground(int job)
{
    s64 pid = shell_job_pid[job];
    int status = 0;
    s64 result;
    do
        result = sys_wait4(pid, &status, WUNTRACED);
    while (result == -4l);
    shell_take_terminal();
    if (result < 0l)
    {
        shell_forget_job(job);
        return 1;
    }
    if ((status & 255) == 127)
    {
        write_text("\n");
        shell_job_changed(job, status);
        return 128 + ((status >> 8) & 255);
    }
    shell_forget_job(job);
    if ((status & 127) != 0)
    {
        int signal = status & 127;
        if (signal != SIGINT)
            write_text(shell_signal_name(signal));
        write_text("\n");
        return 128 + signal;
    }
    return (status >> 8) & 255;
}

// A redirection is taken out of the words before the command sees them
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

// The child moves its own descriptors, since the lowest free one is what an open returns
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

static int shell_run(int count, const char* input, const char* output, int append, int background)
{
    char path[SHELL_PATH_CAPACITY];
    s64 pid;
    int status = 0;

    if (!shell_locate(shell_argv[0], path, SHELL_PATH_CAPACITY))
    {
        report("shell", shell_argv[0], "command not found");
        return 127;
    }
    pid = sys_clone(SIGCHLD, 0ul);
    if (pid < 0l)
    {
        write_text("shell: a process could not be started\n");
        return 1;
    }
    if (pid == 0l)
    {
        // The child joins its own group before either side can hand it the terminal
        if (shell_interactive)
        {
            int group = (int)sys_getpid();
            sys_setpgid(0l, 0l);
            if (!background)
                sys_ioctl(0, TIOCSPGRP, (u64)&group);
            sys_signal(SIGINT, SIG_DFL);
            sys_signal(SIGQUIT, SIG_DFL);
            sys_signal(SIGTSTP, SIG_DFL);
            sys_signal(SIGTTIN, SIG_DFL);
            sys_signal(SIGTTOU, SIG_DFL);
        }
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
    if (shell_interactive)
    {
        int group = (int)pid;
        sys_setpgid(pid, pid);
        if (!background)
            sys_ioctl(0, TIOCSPGRP, (u64)&group);
    }
    {
        int job = shell_add_job(pid, count);
        if (job < 0)
        {
            sys_wait4(pid, &status, 0ul);
            shell_take_terminal();
            return (status >> 8) & 255;
        }
        if (!background)
            return shell_wait_foreground(job);
        write_text("[");
        write_unsigned((u64)(job + 1));
        write_text("] ");
        write_unsigned((u64)pid);
        write_text("\n");
    }
    (void)status;
    return 0;
}

// A job is named by %n, %+ or %%, or by its number alone; nothing names the current one
static int shell_job_argument(int count, const char* name)
{
    const char* text;
    int value = 0;
    if (count < 2)
    {
        if (shell_current_job < 0)
            report(name, (const char*)NULL, "no current job");
        return shell_current_job;
    }
    text = shell_argv[1];
    if (*text == '%')
        text = text + 1;
    if (*text == '+' || *text == '%')
        return shell_current_job;
    while (*text >= '0' && *text <= '9')
    {
        value = value * 10 + (*text - '0');
        text = text + 1;
    }
    if (*text != 0 || value < 1 || value > SHELL_MAX_JOBS || shell_job_pid[value - 1] == 0l)
    {
        report(name, shell_argv[1], "no such job");
        return -1;
    }
    return value - 1;
}

static int shell_signal_number(const char* text)
{
    static const char* const names[] = {
        "HUP", "INT", "QUIT", "ILL", "TRAP", "ABRT", "BUS", "FPE", "KILL", "USR1", "SEGV", "USR2", "PIPE",
        "ALRM", "TERM", "STKFLT", "CHLD", "CONT", "STOP", "TSTP", "TTIN", "TTOU", "URG", "XCPU", "XFSZ",
        "VTALRM", "PROF", "WINCH", "IO", "PWR", "SYS"
    };
    int value = 0;
    int index = 0;
    if (text[0] == 'S' && text[1] == 'I' && text[2] == 'G')
        text = text + 3;
    if (*text >= '0' && *text <= '9')
    {
        while (*text >= '0' && *text <= '9')
        {
            value = value * 10 + (*text - '0');
            text = text + 1;
        }
        return *text == 0 && value <= 64 ? value : -1;
    }
    while (index < 31)
    {
        if (string_equal(text, names[index]))
            return index + 1;
        index = index + 1;
    }
    return -1;
}

static int shell_kill(int count)
{
    int signal = SIGTERM;
    int index = 1;
    int status = 0;
    if (count > 1 && shell_argv[1][0] == '-')
    {
        const char* name = shell_argv[1] + 1;
        if (string_equal(name, "s") && count > 2)
        {
            name = shell_argv[2];
            index = index + 1;
        }
        signal = shell_signal_number(name);
        index = index + 1;
        if (signal < 0)
        {
            report("kill", shell_argv[index - 1], "invalid signal");
            return 1;
        }
    }
    if (index == count)
    {
        report("kill", (const char*)NULL, "usage: kill [-s signal | -signal] pid | %job ...");
        return 2;
    }
    while (index < count)
    {
        const char* text = shell_argv[index];
        s64 target = 0l;
        int negative = 0;
        if (*text == '%')
        {
            int value = 0;
            text = text + 1;
            while (*text >= '0' && *text <= '9')
            {
                value = value * 10 + (*text - '0');
                text = text + 1;
            }
            if (*text != 0 || value < 1 || value > SHELL_MAX_JOBS || shell_job_pid[value - 1] == 0l)
            {
                report("kill", shell_argv[index], "no such job");
                status = 1;
                index = index + 1;
                continue;
            }
            target = -shell_job_pid[value - 1];
        }
        else
        {
            if (*text == '-')
            {
                negative = 1;
                text = text + 1;
            }
            while (*text >= '0' && *text <= '9')
            {
                target = target * 10l + (s64)(*text - '0');
                text = text + 1;
            }
            if (negative)
                target = -target;
        }
        if (*text != 0 || sys_kill(target, signal) < 0l)
        {
            report("kill", shell_argv[index], *text != 0 ? "arguments must be process or job IDs" : "no such process");
            status = 1;
        }
        index = index + 1;
    }
    return status;
}

static int shell_builtin(int count, int* exiting)
{
    *exiting = 0;
    if (count == 0)
        return -1;
    if (string_equal(shell_argv[0], "exit"))
    {
        int index = 0;
        while (index < SHELL_MAX_JOBS && !shell_warned_stopped)
        {
            if (shell_job_pid[index] != 0l && shell_job_state[index] == SHELL_JOB_STOPPED)
            {
                write_text("There are stopped jobs.\n");
                shell_warned_stopped = 1;
                return 1;
            }
            index = index + 1;
        }
        *exiting = 1;
        return count > 1 ? (int)shell_argv[1][0] - '0' : 0;
    }
    shell_warned_stopped = 0;
    if (string_equal(shell_argv[0], "jobs"))
    {
        int index = 0;
        while (index < SHELL_MAX_JOBS)
        {
            if (shell_job_pid[index] != 0l)
            {
                int stopped = shell_job_state[index] == SHELL_JOB_STOPPED;
                shell_report_job(index, stopped ? shell_signal_name(shell_job_signal[index]) : "Running", !stopped);
            }
            index = index + 1;
        }
        return 0;
    }
    if (string_equal(shell_argv[0], "fg"))
    {
        int job = shell_job_argument(count, "fg");
        int group;
        if (job < 0)
            return 1;
        group = (int)shell_job_pid[job];
        write_text(shell_job_text[job]);
        write_text("\n");
        if (shell_interactive)
            sys_ioctl(0, TIOCSPGRP, (u64)&group);
        shell_job_state[job] = SHELL_JOB_RUNNING;
        sys_kill(-shell_job_pid[job], SIGCONT);
        return shell_wait_foreground(job);
    }
    if (string_equal(shell_argv[0], "bg"))
    {
        int job = shell_job_argument(count, "bg");
        if (job < 0)
            return 1;
        shell_job_state[job] = SHELL_JOB_RUNNING;
        shell_current_job = job;
        write_text("[");
        write_unsigned((u64)(job + 1));
        write_text("]+ ");
        write_text(shell_job_text[job]);
        write_text(" &\n");
        sys_kill(-shell_job_pid[job], SIGCONT);
        return 0;
    }
    if (string_equal(shell_argv[0], "kill"))
        return shell_kill(count);
    if (string_equal(shell_argv[0], "wait"))
    {
        int index = 0;
        int status = 0;
        while (index < SHELL_MAX_JOBS)
        {
            if (shell_job_pid[index] != 0l && shell_job_state[index] == SHELL_JOB_RUNNING)
            {
                sys_wait4(shell_job_pid[index], &status, 0ul);
                shell_forget_job(index);
            }
            index = index + 1;
        }
        return (status >> 8) & 255;
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
        write_text("  jobs:     jobs, fg [%n], bg [%n], kill [-signal] pid|%n, wait; Ctrl-Z stops, Ctrl-C interrupts\n");
        write_text("  programs: ls cat cp rm mkdir rmdir touch wc head echo uname\n");
        write_text("  quoting:  'literal' \"escaped\" \\c, $NAME and $? expand\n");
        write_text("  operators: ; && || & < > >>\n");
        return 0;
    }
    return -1;
}

int main(int argc, char** argv, char** envp)
{
    char line[SHELL_LINE_CAPACITY];
    (void)argc;
    (void)argv;

    // Whatever was handed down is what the shell starts with
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

    // A shell on a terminal keeps a group of its own in the foreground and lets its jobs take the stops
    {
        int group = 0;
        shell_pgid = sys_getpid();
        shell_interactive = sys_ioctl(0, TIOCGPGRP, (u64)&group) >= 0l;
        if (shell_interactive)
        {
            if (group != (int)shell_pgid)
                sys_setpgid(0l, 0l);
            shell_take_terminal();
            sys_signal(SIGINT, (u64)shell_on_interrupt);
            sys_signal(SIGQUIT, SIG_IGN);
            sys_signal(SIGTSTP, SIG_IGN);
            sys_signal(SIGTTIN, SIG_IGN);
            sys_signal(SIGTTOU, SIG_IGN);
        }
    }

    write_text("Cnidaria shell\n");
    for (;;)
    {
        const char* cursor;
        const char* here;
        int running = 1;

        shell_reap_jobs();
        here = shell_value("PWD", 3);
        write_text("cnidaria:");
        write_text(here != (const char*)NULL ? here : "?");
        write_text("$ ");
        {
            int length = read_line(line, SHELL_LINE_CAPACITY);
            if (length == -2)
            {
                shell_status = 130;
                continue;
            }
            if (length < 0)
            {
                write_text("exit\n");
                return 0;
            }
        }

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
                shell_status = builtin >= 0 ? builtin : shell_run(count, input, output, append, separator == SHELL_BACKGROUND);
            }

            // What comes next depends on how the last command went
            if (separator == SHELL_END)
                break;
            if (separator == SHELL_AND && shell_status != 0)
                running = 0;
            if (separator == SHELL_OR && shell_status == 0)
                running = 0;
            if (!running)
            {
                // The rest of the line is skipped, but a semicolon starts it again
                int skipped;
                int nextSeparator;
                while (shell_split(&cursor, &skipped, &nextSeparator, &error))
                {
                    if (nextSeparator == SHELL_SEQUENCE || nextSeparator == SHELL_BACKGROUND)
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
