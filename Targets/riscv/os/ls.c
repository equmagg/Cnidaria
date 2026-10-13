#include <dirent.h>
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>

#define LS_TERMINAL_COLUMNS 80

struct entry
{
    char* name;
    unsigned char type;
};

static struct entry* entries;
static int entry_count;
static int entry_capacity;

static void remember(const char* name, unsigned char type)
{
    if (entry_count == entry_capacity)
    {
        entry_capacity = entry_capacity < 32 ? 32 : entry_capacity * 2;
        entries = (struct entry*)realloc(entries, (size_t)entry_capacity * sizeof(struct entry));
        if (entries == NULL)
        {
            fputs("ls: out of memory\n", stderr);
            exit(1);
        }
    }
    entries[entry_count].name = strdup(name);
    entries[entry_count].type = type;
    entry_count++;
}

static void forget_all(void)
{
    while (entry_count > 0)
        free(entries[--entry_count].name);
}

static int by_name(const void* left, const void* right)
{
    return strcmp(((const struct entry*)left)->name, ((const struct entry*)right)->name);
}

// A name in the working directory stands on its own; anywhere else it hangs off its directory
static void build_path(const char* directory, const char* name, char* path, size_t capacity)
{
    if (strcmp(directory, ".") == 0 || directory[0] == 0)
        snprintf(path, capacity, "%s", name);
    else if (strcmp(directory, "/") == 0)
        snprintf(path, capacity, "/%s", name);
    else
        snprintf(path, capacity, "%s/%s", directory, name);
}

static void write_long(const char* directory, const char* name, unsigned char type)
{
    char path[1024];
    struct stat info;
    unsigned int mode;
    unsigned long size = 0;
    int bit;
    build_path(directory, name, path, sizeof(path));
    if (stat(path, &info) == 0)
    {
        mode = (unsigned int)info.st_mode;
        size = (unsigned long)info.st_size;
    }
    else
        mode = type == DT_DIR ? S_IFDIR : type == DT_CHR ? S_IFCHR : S_IFREG;

    // The nine bits every listing spells out as three groups of three
    fputc(S_ISDIR(mode) ? 'd' : S_ISCHR(mode) ? 'c' : '-', stdout);
    for (bit = 8; bit >= 0; bit--)
        fputc((mode & (1u << bit)) == 0 ? '-' : bit % 3 == 2 ? 'r' : bit % 3 == 1 ? 'w' : 'x', stdout);
    printf(" %8lu %s\n", S_ISDIR(mode) ? 0ul : size, name);
}

// Names go across the screen the way a terminal shows them, as many to a line as fit
static void write_columns(void)
{
    int widest = 0;
    int width;
    int columns;
    int index;
    for (index = 0; index < entry_count; index++)
    {
        int length = (int)strlen(entries[index].name);
        if (length > widest)
            widest = length;
    }
    width = widest + 2;
    columns = LS_TERMINAL_COLUMNS / width;
    if (columns < 1)
        columns = 1;

    for (index = 0; index < entry_count; index++)
    {
        int last = index % columns == columns - 1 || index + 1 == entry_count;
        if (last)
            printf("%s\n", entries[index].name);
        else
            printf("%-*s", width, entries[index].name);
    }
}

static int list_directory(const char* path, int all, int longFormat, int onePerLine)
{
    struct dirent* item;
    DIR* directory = opendir(path);
    int index;
    if (directory == NULL)
    {
        fprintf(stderr, "ls: %s: %s\n", path, errno == ENOENT ? "no such file or directory" : "cannot be opened");
        return 1;
    }
    while ((item = readdir(directory)) != NULL)
    {
        if (all || item->d_name[0] != '.')
            remember(item->d_name, item->d_type);
    }
    closedir(directory);
    qsort(entries, (size_t)entry_count, sizeof(struct entry), by_name);

    if (longFormat || onePerLine)
    {
        for (index = 0; index < entry_count; index++)
        {
            if (longFormat)
                write_long(path, entries[index].name, entries[index].type);
            else
                puts(entries[index].name);
        }
    }
    else
        write_columns();
    forget_all();
    return 0;
}

int main(int argc, char** argv)
{
    int all = 0;
    int longFormat = 0;
    int onePerLine = 0;
    int index = 1;
    int paths;
    int status = 0;

    while (index < argc && argv[index][0] == '-' && argv[index][1] != 0)
    {
        const char* flags = argv[index] + 1;
        for (; *flags != 0; flags++)
        {
            if (*flags == 'a')
                all = 1;
            else if (*flags == 'l')
                longFormat = 1;
            else if (*flags == '1')
                onePerLine = 1;
            else
            {
                fputs("ls: usage: ls [-a] [-l] [-1] [path...]\n", stderr);
                return 2;
            }
        }
        index++;
    }

    paths = argc - index;
    if (paths == 0)
        return list_directory(".", all, longFormat, onePerLine);

    for (; index < argc; index++)
    {
        struct stat info;
        if (stat(argv[index], &info) == 0 && !S_ISDIR(info.st_mode))
        {
            if (longFormat)
                write_long("", argv[index], DT_REG);
            else
                puts(argv[index]);
            continue;
        }
        if (paths > 1)
            printf("%s:\n", argv[index]);
        status |= list_directory(argv[index], all, longFormat, onePerLine);
        if (paths > 1 && index + 1 < argc)
            fputc('\n', stdout);
    }
    return status;
}
