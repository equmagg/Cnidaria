#include <errno.h>
#include <fcntl.h>
#include <stdio.h>
#include <unistd.h>

struct counts
{
    unsigned long lines;
    unsigned long words;
    unsigned long bytes;
};

static void wc_report(const struct counts* counts, int wantLines, int wantWords, int wantBytes, const char* name)
{
    if (wantLines)
        printf("%8lu", counts->lines);
    if (wantWords)
        printf("%8lu", counts->words);
    if (wantBytes)
        printf("%8lu", counts->bytes);
    if (name != NULL)
        printf(" %s", name);
    fputc('\n', stdout);
}

static int wc_count(int fd, struct counts* counts)
{
    char buffer[512];
    int inWord = 0;
    counts->lines = 0;
    counts->words = 0;
    counts->bytes = 0;
    for (;;)
    {
        ssize_t index;
        ssize_t count = read(fd, buffer, sizeof(buffer));
        if (count < 0)
            return 1;
        if (count == 0)
            return 0;
        counts->bytes += (unsigned long)count;
        for (index = 0; index < count; index++)
        {
            char ch = buffer[index];
            if (ch == '\n')
                counts->lines++;
            if (ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r')
                inWord = 0;
            else if (!inWord)
            {
                inWord = 1;
                counts->words++;
            }
        }
    }
}

int main(int argc, char** argv)
{
    struct counts counts;
    struct counts total = { 0, 0, 0 };
    int wantLines = 0;
    int wantWords = 0;
    int wantBytes = 0;
    int index = 1;
    int status = 0;
    int files = 0;

    while (index < argc && argv[index][0] == '-' && argv[index][1] != 0)
    {
        const char* flags = argv[index] + 1;
        for (; *flags != 0; flags++)
        {
            if (*flags == 'l')
                wantLines = 1;
            else if (*flags == 'w')
                wantWords = 1;
            else if (*flags == 'c')
                wantBytes = 1;
            else
            {
                fputs("wc: usage: wc [-l] [-w] [-c] [file...]\n", stderr);
                return 2;
            }
        }
        index++;
    }
    if (!wantLines && !wantWords && !wantBytes)
    {
        wantLines = 1;
        wantWords = 1;
        wantBytes = 1;
    }

    // With nothing named, a filter counts what it is given
    if (index == argc)
    {
        if (wc_count(0, &counts) != 0)
            return 1;
        wc_report(&counts, wantLines, wantWords, wantBytes, NULL);
        return 0;
    }

    for (; index < argc; index++)
    {
        int fd = open(argv[index], O_RDONLY);
        if (fd < 0)
        {
            fprintf(stderr, "wc: %s: %s\n", argv[index], errno == ENOENT ? "no such file" : errno == EISDIR ? "is a directory" : "cannot be opened");
            status = 1;
            continue;
        }
        status |= wc_count(fd, &counts);
        close(fd);
        wc_report(&counts, wantLines, wantWords, wantBytes, argv[index]);
        total.lines += counts.lines;
        total.words += counts.words;
        total.bytes += counts.bytes;
        files++;
    }

    if (files > 1)
        wc_report(&total, wantLines, wantWords, wantBytes, "total");
    return status;
}
