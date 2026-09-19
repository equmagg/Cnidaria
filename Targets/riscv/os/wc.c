#define WC_BUFFER 512

static u64 wc_total_lines;
static u64 wc_total_words;
static u64 wc_total_bytes;

static void wc_report(u64 lines, u64 words, u64 bytes, int wantLines, int wantWords, int wantBytes, const char* name)
{
    if (wantLines)
        write_unsigned_to(1, lines, 8, ' ');
    if (wantWords)
        write_unsigned_to(1, words, 8, ' ');
    if (wantBytes)
        write_unsigned_to(1, bytes, 8, ' ');
    if (name != (const char*)NULL)
    {
        write_text(" ");
        write_text(name);
    }
    write_text("\n");
}

static int wc_count(int fd, u64* lines, u64* words, u64* bytes)
{
    char buffer[WC_BUFFER];
    int inWord = 0;
    *lines = 0ul;
    *words = 0ul;
    *bytes = 0ul;
    for (;;)
    {
        usize index = 0ul;
        s64 count = sys_read(fd, buffer, (usize)WC_BUFFER);
        if (count < 0l)
            return 1;
        if (count == 0l)
            return 0;
        *bytes = *bytes + (u64)count;
        while (index < (usize)count)
        {
            char ch = buffer[index];
            if (ch == '\n')
                *lines = *lines + 1ul;
            if (ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r')
                inWord = 0;
            else if (!inWord)
            {
                inWord = 1;
                *words = *words + 1ul;
            }
            index = index + 1ul;
        }
    }
}

int main(int argc, char** argv, char** envp)
{
    int wantLines = 0;
    int wantWords = 0;
    int wantBytes = 0;
    int index = 1;
    int status = 0;
    int files = 0;
    (void)envp;

    while (index < argc && argv[index][0] == '-' && argv[index][1] != 0)
    {
        const char* flags = argv[index] + 1;
        while (*flags != 0)
        {
            if (*flags == 'l')
                wantLines = 1;
            else if (*flags == 'w')
                wantWords = 1;
            else if (*flags == 'c')
                wantBytes = 1;
            else
            {
                report("wc", (const char*)NULL, "usage: wc [-l] [-w] [-c] [file...]");
                return 2;
            }
            flags = flags + 1;
        }
        index = index + 1;
    }
    if (!wantLines && !wantWords && !wantBytes)
    {
        wantLines = 1;
        wantWords = 1;
        wantBytes = 1;
    }

    /* With nothing named, a filter counts what it is given */
    if (index == argc)
    {
        u64 lines;
        u64 words;
        u64 bytes;
        if (wc_count(0, &lines, &words, &bytes) != 0)
            return 1;
        wc_report(lines, words, bytes, wantLines, wantWords, wantBytes, (const char*)NULL);
        return 0;
    }

    while (index < argc)
    {
        u64 lines;
        u64 words;
        u64 bytes;
        int fd = open_for_reading("wc", argv[index]);
        if (fd < 0)
        {
            status = 1;
            index = index + 1;
            continue;
        }
        if (wc_count(fd, &lines, &words, &bytes) != 0)
            status = 1;
        sys_close(fd);
        wc_report(lines, words, bytes, wantLines, wantWords, wantBytes, argv[index]);
        wc_total_lines = wc_total_lines + lines;
        wc_total_words = wc_total_words + words;
        wc_total_bytes = wc_total_bytes + bytes;
        files = files + 1;
        index = index + 1;
    }

    if (files > 1)
        wc_report(wc_total_lines, wc_total_words, wc_total_bytes, wantLines, wantWords, wantBytes, "total");
    return status;
}
