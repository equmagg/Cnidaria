#include <ctype.h>
#include <errno.h>
#include <fcntl.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/ioctl.h>
#include <termios.h>
#include <unistd.h>

// A small editor that keeps nano's screen and keys: title bar, text, status line and two lines of shortcuts
#define TAB_WIDTH 8
#define KEY_BASE 1000
#define KEY_UP (KEY_BASE + 1)
#define KEY_DOWN (KEY_BASE + 2)
#define KEY_LEFT (KEY_BASE + 3)
#define KEY_RIGHT (KEY_BASE + 4)
#define KEY_HOME (KEY_BASE + 5)
#define KEY_END (KEY_BASE + 6)
#define KEY_PAGE_UP (KEY_BASE + 7)
#define KEY_PAGE_DOWN (KEY_BASE + 8)
#define KEY_DELETE (KEY_BASE + 9)
#define KEY_INSERT (KEY_BASE + 10)
#define KEY_WORD_LEFT (KEY_BASE + 11)
#define KEY_WORD_RIGHT (KEY_BASE + 12)
#define KEY_TOP (KEY_BASE + 13)
#define KEY_BOTTOM (KEY_BASE + 14)
#define KEY_META 2000
#define CONTROL(letter) ((letter) & 0x1f)

struct line
{
    char* text;
    int length;
    int capacity;
};

static struct line* lines;
static int line_count;
static int line_capacity;
static int cursor_line;
static int cursor_byte;
static int wanted_column;
static int top_line;
static int rows = 24;
static int columns = 80;
static int modified;
static char file_name[256];
static char message[256];
static char last_search[128];
static char** cut_lines;
static int cut_count;
static int cut_capacity;
static int last_key_was_cut;
static struct termios saved_terminal;

static char* output;
static int output_length;
static int output_capacity;
static char** shown_rows;
static int shown_count;

static unsigned char input[256];
static int input_position;
static int input_length;

static void* allocate(void* pointer, size_t size)
{
    void* result = realloc(pointer, size);
    if (result == NULL)
    {
        write(2, "nano: out of memory\n", 20);
        exit(1);
    }
    return result;
}

static void line_reserve(struct line* line, int capacity)
{
    if (capacity <= line->capacity)
        return;
    if (capacity < line->capacity * 2)
        capacity = line->capacity * 2;
    if (capacity < 16)
        capacity = 16;
    line->text = (char*)allocate(line->text, (size_t)capacity);
    line->capacity = capacity;
}

static void line_set(struct line* line, const char* text, int length)
{
    line->text = NULL;
    line->length = 0;
    line->capacity = 0;
    line_reserve(line, length + 1);
    memcpy(line->text, text, (size_t)length);
    line->length = length;
}

// Room for one more line at index, with everything after it moved down
static struct line* insert_line(int index, const char* text, int length)
{
    if (line_count == line_capacity)
    {
        line_capacity = line_capacity < 64 ? 64 : line_capacity * 2;
        lines = (struct line*)allocate(lines, (size_t)line_capacity * sizeof(struct line));
    }
    memmove(lines + index + 1, lines + index, (size_t)(line_count - index) * sizeof(struct line));
    line_count++;
    line_set(&lines[index], text, length);
    return &lines[index];
}

static void delete_line(int index)
{
    free(lines[index].text);
    memmove(lines + index, lines + index + 1, (size_t)(line_count - index - 1) * sizeof(struct line));
    line_count--;
    if (line_count == 0)
        insert_line(0, "", 0);
}

static void emit(const char* text, int length)
{
    if (output_length + length > output_capacity)
    {
        output_capacity = (output_length + length) * 2;
        output = (char*)allocate(output, (size_t)output_capacity);
    }
    memcpy(output + output_length, text, (size_t)length);
    output_length += length;
}

static void emit_text(const char* text)
{
    emit(text, (int)strlen(text));
}

static void flush_output(void)
{
    int done = 0;
    while (done < output_length)
    {
        ssize_t written = write(1, output + done, (size_t)(output_length - done));
        if (written <= 0)
            break;
        done += (int)written;
    }
    output_length = 0;
}

static void set_message(const char* text)
{
    snprintf(message, sizeof(message), "%s", text);
}

static int display_width(const char* text, int length)
{
    int column = 0;
    int index;
    for (index = 0; index < length; index++)
    {
        unsigned char ch = (unsigned char)text[index];
        if (ch == '\t')
            column += TAB_WIDTH - column % TAB_WIDTH;
        else if (ch < 32 || ch == 127)
            column += 2;
        else
            column++;
    }
    return column;
}

static int byte_for_column(const struct line* line, int wanted)
{
    int column = 0;
    int index = 0;
    while (index < line->length)
    {
        int next = column + display_width(line->text + index, 1);
        if (line->text[index] == '\t')
            next = column + TAB_WIDTH - column % TAB_WIDTH;
        if (next > wanted)
            break;
        column = next;
        index++;
    }
    return index;
}

static int text_rows(void)
{
    int count = rows - 4;
    return count < 1 ? 1 : count;
}

static void query_size(void)
{
    struct winsize size;
    if (ioctl(1, TIOCGWINSZ, &size) == 0 && size.ws_row >= 6 && size.ws_col >= 20)
    {
        rows = size.ws_row;
        columns = size.ws_col;
    }
}

static void forget_screen(void)
{
    int index;
    for (index = 0; index < shown_count; index++)
        free(shown_rows[index]);
    free(shown_rows);
    shown_rows = NULL;
    shown_count = 0;
}

// A row is sent only when what it shows changed since the last frame
static void put_row(int row, const char* text)
{
    char position[24];
    if (shown_count != rows)
    {
        forget_screen();
        shown_rows = (char**)allocate(NULL, (size_t)rows * sizeof(char*));
        memset(shown_rows, 0, (size_t)rows * sizeof(char*));
        shown_count = rows;
        emit_text("\x1b[0m\x1b[2J");
    }
    if (shown_rows[row] != NULL && strcmp(shown_rows[row], text) == 0)
        return;
    free(shown_rows[row]);
    shown_rows[row] = strdup(text);
    snprintf(position, sizeof(position), "\x1b[%d;1H", row + 1);
    emit_text(position);
    emit_text(text);
    emit_text("\x1b[0m\x1b[K");
}

// The visible part of a line from display column start, with a $ where it runs on past either edge
static void render_line(const struct line* line, int start, char* row, int capacity)
{
    int column = 0;
    int used = 0;
    int index;
    int last_column = columns - 1;
    for (index = 0; index < line->length && used < capacity - 4; index++)
    {
        unsigned char ch = (unsigned char)line->text[index];
        int width = ch == '\t' ? TAB_WIDTH - column % TAB_WIDTH : (ch < 32 || ch == 127) ? 2 : 1;
        int part;
        for (part = 0; part < width; part++)
        {
            int at = column + part;
            if (at < start)
                continue;
            if (at - start >= last_column)
            {
                row[used++] = '$';
                row[used] = 0;
                return;
            }
            if (at == start && start != 0)
                row[used++] = '$';
            else if (ch == '\t')
                row[used++] = ' ';
            else if (ch < 32 || ch == 127)
                row[used++] = part == 0 ? '^' : (char)(ch == 127 ? '?' : ch + 64);
            else
                row[used++] = (char)ch;
        }
        column += width;
    }
    row[used] = 0;
}

static void render_shortcuts(char* row, int capacity, const char* const* keys, const char* const* names, int count)
{
    int used = 0;
    int index;
    int cell = columns / (count < 1 ? 1 : count);
    row[0] = 0;
    for (index = 0; index < count; index++)
    {
        int start = used;
        int written = snprintf(row + used, (size_t)(capacity - used), "\x1b[7m%s\x1b[0m %s", keys[index], names[index]);
        int visible = (int)strlen(keys[index]) + 1 + (int)strlen(names[index]);
        if (written < 0 || used + written >= capacity - 1)
            break;
        used += written;
        while (visible < cell && used < capacity - 1)
        {
            row[used++] = ' ';
            visible++;
        }
        row[used] = 0;
        (void)start;
    }
}

static void render(const char* prompt, const char* answer)
{
    char row[1024];
    int row_index;
    int visible = text_rows();
    int cursor_column;
    int start = 0;
    char position[32];
    static const char* const keys_top[6] = { "^G", "^O", "^W", "^K", "^C", "^_" };
    static const char* const names_top[6] = { "Keys", "Save", "Find", "Cut Line", "Position", "Go To" };
    static const char* const keys_bottom[6] = { "^X", "^R", "^\\", "^U", "^A", "^E" };
    static const char* const names_bottom[6] = { "Close", "Insert", "Replace", "Paste", "Home", "End" };

    query_size();
    visible = text_rows();
    if (cursor_line < top_line)
        top_line = cursor_line;
    if (cursor_line >= top_line + visible)
        top_line = cursor_line - visible + 1;

    snprintf(row, sizeof(row), "\x1b[7m  Cnidaria nano%*s%-*s%s", 4, "", columns - 31 > 0 ? columns - 31 : 0,
        file_name[0] != 0 ? file_name : "(new file)", modified ? "Changed " : "        ");
    {
        int length = (int)strlen(row);
        int visible_length = length - 4;
        while (visible_length < columns && length < (int)sizeof(row) - 1)
        {
            row[length++] = ' ';
            visible_length++;
        }
        row[length] = 0;
    }
    put_row(0, row);

    cursor_column = display_width(lines[cursor_line].text, cursor_byte);
    if (cursor_column >= columns - 1)
        start = cursor_column - cursor_column % (columns - 8 > 1 ? columns - 8 : 1);
    for (row_index = 0; row_index < visible; row_index++)
    {
        int line_index = top_line + row_index;
        if (line_index < line_count)
            render_line(&lines[line_index], line_index == cursor_line ? start : 0, row, (int)sizeof(row));
        else
            row[0] = 0;
        put_row(row_index + 1, row);
    }

    if (prompt != NULL)
        snprintf(row, sizeof(row), "\x1b[7m%s%s", prompt, answer);
    else if (message[0] != 0)
        snprintf(row, sizeof(row), "%*s\x1b[7m[ %s ]", (columns - (int)strlen(message) - 4) / 2 > 0 ? (columns - (int)strlen(message) - 4) / 2 : 0, "", message);
    else
        row[0] = 0;
    put_row(rows - 3, row);
    render_shortcuts(row, (int)sizeof(row), keys_top, names_top, 6);
    put_row(rows - 2, row);
    render_shortcuts(row, (int)sizeof(row), keys_bottom, names_bottom, 6);
    put_row(rows - 1, row);

    if (prompt != NULL)
        snprintf(position, sizeof(position), "\x1b[%d;%dH", rows - 2, (int)(strlen(prompt) + strlen(answer)) + 1);
    else
        snprintf(position, sizeof(position), "\x1b[%d;%dH", cursor_line - top_line + 2, cursor_column - start + 1);
    emit_text(position);
    flush_output();
}

static int next_byte(int wait)
{
    if (input_position == input_length)
    {
        ssize_t count;
        if (!wait)
            return -1;
        count = read(0, input, sizeof(input));
        if (count <= 0)
            return -1;
        input_position = 0;
        input_length = (int)count;
    }
    return input[input_position++];
}

// Keys arrive as the terminal sends them: a byte, or an escape sequence the keyboard driver wrote in one go
static int read_key(void)
{
    int ch = next_byte(1);
    int second;
    if (ch != 27)
        return ch;
    second = next_byte(0);
    if (second < 0)
        return 27;
    if (second == 'O')
    {
        int final = next_byte(0);
        if (final == 'H')
            return KEY_HOME;
        if (final == 'F')
            return KEY_END;
        if (final >= 'A' && final <= 'D')
            return KEY_UP + (final - 'A');
        return 0;
    }
    if (second == '[')
    {
        int parameters[4] = { 0, 0, 0, 0 };
        int count = 0;
        int final;
        for (;;)
        {
            final = next_byte(0);
            if (final < 0)
                return 0;
            if (final >= '0' && final <= '9')
            {
                if (count == 0)
                    count = 1;
                parameters[count - 1] = parameters[count - 1] * 10 + (final - '0');
            }
            else if (final == ';')
            {
                if (count < 4)
                    count++;
            }
            else
                break;
        }
        if (final == '~')
        {
            switch (parameters[0])
            {
            case 1:
            case 7:
                return KEY_HOME;
            case 4:
            case 8:
                return KEY_END;
            case 2:
                return KEY_INSERT;
            case 3:
                return KEY_DELETE;
            case 5:
                return KEY_PAGE_UP;
            case 6:
                return KEY_PAGE_DOWN;
            default:
                return 0;
            }
        }
        // A second parameter of 5 means Ctrl was held
        if (count >= 2 && parameters[1] == 5)
        {
            if (final == 'C')
                return KEY_WORD_RIGHT;
            if (final == 'D')
                return KEY_WORD_LEFT;
            if (final == 'H')
                return KEY_TOP;
            if (final == 'F')
                return KEY_BOTTOM;
        }
        if (final >= 'A' && final <= 'D')
            return KEY_UP + (final - 'A');
        if (final == 'H')
            return KEY_HOME;
        if (final == 'F')
            return KEY_END;
        return 0;
    }
    return KEY_META + tolower(second);
}

static void mark_modified(void)
{
    modified = 1;
}

static void insert_character(char ch)
{
    struct line* line = &lines[cursor_line];
    line_reserve(line, line->length + 2);
    memmove(line->text + cursor_byte + 1, line->text + cursor_byte, (size_t)(line->length - cursor_byte));
    line->text[cursor_byte] = ch;
    line->length++;
    cursor_byte++;
    mark_modified();
}

static void split_line(void)
{
    struct line* line = &lines[cursor_line];
    int tail = line->length - cursor_byte;
    insert_line(cursor_line + 1, lines[cursor_line].text + cursor_byte, tail);
    lines[cursor_line].length = cursor_byte;
    cursor_line++;
    cursor_byte = 0;
    mark_modified();
}

static void join_with_next(void)
{
    struct line* line;
    struct line* next;
    if (cursor_line + 1 >= line_count)
        return;
    line = &lines[cursor_line];
    next = &lines[cursor_line + 1];
    line_reserve(line, line->length + next->length + 1);
    memcpy(line->text + line->length, next->text, (size_t)next->length);
    line->length += next->length;
    delete_line(cursor_line + 1);
    mark_modified();
}

static void delete_forward(void)
{
    struct line* line = &lines[cursor_line];
    if (cursor_byte < line->length)
    {
        memmove(line->text + cursor_byte, line->text + cursor_byte + 1, (size_t)(line->length - cursor_byte - 1));
        line->length--;
        mark_modified();
    }
    else
        join_with_next();
}

static void delete_backward(void)
{
    if (cursor_byte > 0)
    {
        cursor_byte--;
        delete_forward();
    }
    else if (cursor_line > 0)
    {
        cursor_line--;
        cursor_byte = lines[cursor_line].length;
        join_with_next();
    }
}

static void cut_line(void)
{
    if (!last_key_was_cut)
    {
        while (cut_count > 0)
            free(cut_lines[--cut_count]);
    }
    if (cut_count == cut_capacity)
    {
        cut_capacity = cut_capacity < 16 ? 16 : cut_capacity * 2;
        cut_lines = (char**)allocate(cut_lines, (size_t)cut_capacity * sizeof(char*));
    }
    cut_lines[cut_count] = (char*)allocate(NULL, (size_t)lines[cursor_line].length + 1);
    memcpy(cut_lines[cut_count], lines[cursor_line].text, (size_t)lines[cursor_line].length);
    cut_lines[cut_count][lines[cursor_line].length] = 0;
    cut_count++;
    if (line_count == 1)
        lines[0].length = 0;
    else
        delete_line(cursor_line);
    if (cursor_line >= line_count)
        cursor_line = line_count - 1;
    cursor_byte = 0;
    mark_modified();
}

static void paste_lines(void)
{
    int index;
    if (cut_count == 0)
    {
        set_message("Nothing to paste");
        return;
    }
    for (index = 0; index < cut_count; index++)
        insert_line(cursor_line + index, cut_lines[index], (int)strlen(cut_lines[index]));
    cursor_line += cut_count;
    cursor_byte = 0;
    mark_modified();
}

static void free_lines(void)
{
    int index;
    for (index = 0; index < line_count; index++)
        free(lines[index].text);
    line_count = 0;
}

// Reads a file's lines in at index, returning how many there were, or -1 when the file cannot be read
static int read_file_at(const char* path, int index)
{
    int fd = open(path, O_RDONLY);
    char buffer[4096];
    char* pending = NULL;
    int pending_length = 0;
    int pending_capacity = 0;
    int added = 0;
    if (fd < 0)
        return -1;
    for (;;)
    {
        ssize_t count = read(fd, buffer, sizeof(buffer));
        int position;
        if (count < 0)
        {
            close(fd);
            free(pending);
            return -1;
        }
        if (count == 0)
            break;
        for (position = 0; position < (int)count; position++)
        {
            char ch = buffer[position];
            if (ch == '\n')
            {
                if (pending_length > 0 && pending[pending_length - 1] == '\r')
                    pending_length--;
                insert_line(index + added, pending != NULL ? pending : "", pending_length);
                added++;
                pending_length = 0;
                continue;
            }
            if (pending_length == pending_capacity)
            {
                pending_capacity = pending_capacity < 64 ? 64 : pending_capacity * 2;
                pending = (char*)allocate(pending, (size_t)pending_capacity);
            }
            pending[pending_length++] = ch;
        }
    }
    if (pending_length > 0)
    {
        insert_line(index + added, pending, pending_length);
        added++;
    }
    free(pending);
    close(fd);
    return added;
}

static int write_file(const char* path)
{
    int fd = open(path, O_WRONLY | O_CREAT | O_TRUNC, 0644);
    int index;
    char text[160];
    if (fd < 0)
    {
        snprintf(text, sizeof(text), "Error writing %s: %s", path, strerror(errno));
        set_message(text);
        return 0;
    }
    for (index = 0; index < line_count; index++)
    {
        // Like nano, the file always ends with a newline, and an empty last line is the one it makes
        if (index == line_count - 1 && lines[index].length == 0 && line_count > 1)
            break;
        if (write(fd, lines[index].text, (size_t)lines[index].length) != lines[index].length || write(fd, "\n", 1) != 1)
        {
            close(fd);
            snprintf(text, sizeof(text), "Error writing %s: %s", path, strerror(errno));
            set_message(text);
            return 0;
        }
    }
    close(fd);
    snprintf(text, sizeof(text), "Saved %d line%s", index, index == 1 ? "" : "s");
    set_message(text);
    modified = 0;
    return 1;
}

// A question on the status line: typing edits the answer, Enter takes it and ^C gives up
static int prompt_for(const char* question, char* answer, int capacity)
{
    int length = (int)strlen(answer);
    message[0] = 0;
    for (;;)
    {
        int key;
        render(question, answer);
        key = read_key();
        if (key == '\r' || key == '\n')
            return 1;
        if (key == CONTROL('c') || key == 27)
        {
            set_message("Cancelled");
            return 0;
        }
        if ((key == 127 || key == CONTROL('h')) && length > 0)
            answer[--length] = 0;
        else if (key >= 32 && key < 127 && length < capacity - 1)
        {
            answer[length++] = (char)key;
            answer[length] = 0;
        }
    }
}

static int ask_yes_no(const char* question)
{
    for (;;)
    {
        int key;
        render(question, "");
        key = read_key();
        if (key == 'y' || key == 'Y')
            return 1;
        if (key == 'n' || key == 'N')
            return 0;
        if (key == CONTROL('c'))
            return -1;
    }
}

static int save_with_prompt(void)
{
    char name[256];
    snprintf(name, sizeof(name), "%s", file_name);
    if (!prompt_for("Save as: ", name, (int)sizeof(name)) || name[0] == 0)
        return 0;
    if (!write_file(name))
        return 0;
    snprintf(file_name, sizeof(file_name), "%s", name);
    return 1;
}

static int find_in_line(const struct line* line, int from, const char* needle)
{
    int needle_length = (int)strlen(needle);
    int index;
    for (index = from; index + needle_length <= line->length; index++)
    {
        if (memcmp(line->text + index, needle, (size_t)needle_length) == 0)
            return index;
    }
    return -1;
}

static void search(void)
{
    char answer[128];
    char question[160];
    int step;
    answer[0] = 0;
    snprintf(question, sizeof(question), last_search[0] != 0 ? "Find [%s]: " : "Find: %s", last_search);
    if (!prompt_for(question, answer, (int)sizeof(answer)))
        return;
    if (answer[0] != 0)
        snprintf(last_search, sizeof(last_search), "%s", answer);
    if (last_search[0] == 0)
        return;
    for (step = 0; step <= line_count; step++)
    {
        int index = (cursor_line + step) % line_count;
        int from = step == 0 ? cursor_byte + 1 : 0;
        int found = from <= lines[index].length ? find_in_line(&lines[index], from, last_search) : -1;
        if (found >= 0)
        {
            if (index < cursor_line || (index == cursor_line && step != 0))
                set_message("Continued from the top");
            cursor_line = index;
            cursor_byte = found;
            return;
        }
    }
    snprintf(question, sizeof(question), "%s: no match", last_search);
    set_message(question);
}

static void replace(void)
{
    char needle[128];
    char replacement[128];
    char text[64];
    int index;
    int replaced = 0;
    int needle_length;
    int replacement_length;
    needle[0] = 0;
    replacement[0] = 0;
    if (!prompt_for("Replace what: ", needle, (int)sizeof(needle)) || needle[0] == 0)
        return;
    if (!prompt_for("Replace it with: ", replacement, (int)sizeof(replacement)))
        return;
    needle_length = (int)strlen(needle);
    replacement_length = (int)strlen(replacement);
    for (index = 0; index < line_count; index++)
    {
        struct line* line = &lines[index];
        int found = find_in_line(line, 0, needle);
        while (found >= 0)
        {
            line_reserve(line, line->length - needle_length + replacement_length + 1);
            memmove(line->text + found + replacement_length, line->text + found + needle_length, (size_t)(line->length - found - needle_length));
            memcpy(line->text + found, replacement, (size_t)replacement_length);
            line->length += replacement_length - needle_length;
            replaced++;
            found = find_in_line(line, found + replacement_length, needle);
        }
    }
    if (replaced != 0)
        mark_modified();
    if (cursor_byte > lines[cursor_line].length)
        cursor_byte = lines[cursor_line].length;
    snprintf(text, sizeof(text), "Replaced %d occurrence%s", replaced, replaced == 1 ? "" : "s");
    set_message(text);
}

static void go_to_line(void)
{
    char answer[32];
    int line_number;
    int column_number = 1;
    char* comma;
    answer[0] = 0;
    if (!prompt_for("Go to line[,column]: ", answer, (int)sizeof(answer)) || answer[0] == 0)
        return;
    line_number = atoi(answer);
    comma = strchr(answer, ',');
    if (comma != NULL)
        column_number = atoi(comma + 1);
    if (line_number < 1)
        line_number = 1;
    if (line_number > line_count)
        line_number = line_count;
    cursor_line = line_number - 1;
    cursor_byte = column_number - 1;
    if (cursor_byte < 0)
        cursor_byte = 0;
    if (cursor_byte > lines[cursor_line].length)
        cursor_byte = lines[cursor_line].length;
}

static void insert_file(void)
{
    char name[256];
    char text[300];
    int added;
    name[0] = 0;
    if (!prompt_for("Insert file: ", name, (int)sizeof(name)) || name[0] == 0)
        return;
    added = read_file_at(name, cursor_line);
    if (added < 0)
    {
        snprintf(text, sizeof(text), "Error reading %s: %s", name, strerror(errno));
        set_message(text);
        return;
    }
    if (added > 0)
        mark_modified();
    snprintf(text, sizeof(text), "Read %d line%s", added, added == 1 ? "" : "s");
    set_message(text);
}

static void report_location(void)
{
    char text[128];
    int total_bytes = 0;
    int before = 0;
    int index;
    for (index = 0; index < line_count; index++)
    {
        if (index == cursor_line)
            before = total_bytes + cursor_byte;
        total_bytes += lines[index].length + 1;
    }
    snprintf(text, sizeof(text), "Line %d of %d (%d%%), column %d of %d (%d%%), byte %d of %d (%d%%)",
        cursor_line + 1, line_count, (cursor_line + 1) * 100 / line_count,
        cursor_byte + 1, lines[cursor_line].length + 1, (cursor_byte + 1) * 100 / (lines[cursor_line].length + 1),
        before, total_bytes, total_bytes == 0 ? 0 : before * 100 / total_bytes);
    set_message(text);
}

static void show_help(void)
{
    static const char* const help[] = {
        "Keys",
        "",
        "Typing inserts text. ^ is Ctrl, and M- is Alt or Esc followed by the key.",
        "",
        "^G  These keys                    ^X  Close, asking to save changes",
        "^O  Save, asking for a file name   ^R  Insert a file at the cursor",
        "^W  Find the next match            ^\\  Replace every match",
        "^K  Cut the line (again: add more) ^U  Paste the cut lines",
        "^C  Show the cursor position       ^_  Go to a line (also M-G)",
        "^A  Start of the line              ^E  End of the line",
        "^Y  Page up                        ^V  Page down",
        "^P  Line up                        ^N  Line down",
        "^B  Character left                 ^F  Character right",
        "^D  Delete under the cursor        ^H  Delete before the cursor",
        "^L  Redraw the screen              M-\\ / M-/  First / last line",
        "Ctrl+Left / Ctrl+Right  Word left / right",
        "",
        "Any key goes back to the text.",
    };
    int count = (int)(sizeof(help) / sizeof(help[0]));
    int row;
    forget_screen();
    emit_text("\x1b[0m\x1b[2J\x1b[H");
    for (row = 0; row < count && row < rows; row++)
    {
        emit_text(help[row]);
        emit_text("\r\n");
    }
    flush_output();
    read_key();
    forget_screen();
}

static int is_word_byte(char ch)
{
    return isalnum((unsigned char)ch) || ch == '_';
}

static void move_word(int forward)
{
    if (forward)
    {
        struct line* line = &lines[cursor_line];
        if (cursor_byte >= line->length && cursor_line + 1 < line_count)
        {
            cursor_line++;
            cursor_byte = 0;
            line = &lines[cursor_line];
        }
        while (cursor_byte < line->length && is_word_byte(line->text[cursor_byte]))
            cursor_byte++;
        while (cursor_byte < line->length && !is_word_byte(line->text[cursor_byte]))
            cursor_byte++;
    }
    else
    {
        struct line* line = &lines[cursor_line];
        if (cursor_byte == 0 && cursor_line > 0)
        {
            cursor_line--;
            cursor_byte = lines[cursor_line].length;
            line = &lines[cursor_line];
        }
        while (cursor_byte > 0 && !is_word_byte(line->text[cursor_byte - 1]))
            cursor_byte--;
        while (cursor_byte > 0 && is_word_byte(line->text[cursor_byte - 1]))
            cursor_byte--;
    }
}

static void move_vertically(int delta)
{
    int column = wanted_column;
    cursor_line += delta;
    if (cursor_line < 0)
        cursor_line = 0;
    if (cursor_line >= line_count)
        cursor_line = line_count - 1;
    cursor_byte = byte_for_column(&lines[cursor_line], column);
}

static void restore_terminal(void)
{
    emit_text("\x1b[0m\x1b[2J\x1b[H\x1b[?1049l");
    flush_output();
    tcsetattr(0, TCSAFLUSH, &saved_terminal);
}

// Returns once the buffer is closed
static void edit(void)
{
    for (;;)
    {
        int key;
        int vertical = 0;
        int cut = 0;
        render(NULL, "");
        key = read_key();
        message[0] = 0;
        switch (key)
        {
        case CONTROL('x'):
            if (modified)
            {
                int answer = ask_yes_no("Save changes before closing? (y = save, n = discard, ^C = keep editing) ");
                if (answer < 0)
                    break;
                if (answer > 0 && !save_with_prompt())
                    break;
            }
            return;
        case CONTROL('o'):
            save_with_prompt();
            break;
        case CONTROL('w'):
            search();
            break;
        case CONTROL('\\'):
            replace();
            break;
        case CONTROL('k'):
            cut_line();
            cut = 1;
            break;
        case CONTROL('u'):
            paste_lines();
            break;
        case CONTROL('c'):
            report_location();
            break;
        case CONTROL('_'):
        case KEY_META + 'g':
            go_to_line();
            break;
        case CONTROL('r'):
            insert_file();
            break;
        case CONTROL('g'):
            show_help();
            break;
        case CONTROL('l'):
            forget_screen();
            break;
        case CONTROL('a'):
        case KEY_HOME:
            cursor_byte = 0;
            break;
        case CONTROL('e'):
        case KEY_END:
            cursor_byte = lines[cursor_line].length;
            break;
        case CONTROL('p'):
        case KEY_UP:
            move_vertically(-1);
            vertical = 1;
            break;
        case CONTROL('n'):
        case KEY_DOWN:
            move_vertically(1);
            vertical = 1;
            break;
        case CONTROL('y'):
        case KEY_PAGE_UP:
            move_vertically(-(text_rows() - 2 > 1 ? text_rows() - 2 : 1));
            vertical = 1;
            break;
        case CONTROL('v'):
        case KEY_PAGE_DOWN:
            move_vertically(text_rows() - 2 > 1 ? text_rows() - 2 : 1);
            vertical = 1;
            break;
        case CONTROL('b'):
        case KEY_LEFT:
            if (cursor_byte > 0)
                cursor_byte--;
            else if (cursor_line > 0)
            {
                cursor_line--;
                cursor_byte = lines[cursor_line].length;
            }
            break;
        case CONTROL('f'):
        case KEY_RIGHT:
            if (cursor_byte < lines[cursor_line].length)
                cursor_byte++;
            else if (cursor_line + 1 < line_count)
            {
                cursor_line++;
                cursor_byte = 0;
            }
            break;
        case KEY_WORD_LEFT:
            move_word(0);
            break;
        case KEY_WORD_RIGHT:
            move_word(1);
            break;
        case KEY_TOP:
        case KEY_META + '\\':
            cursor_line = 0;
            cursor_byte = 0;
            break;
        case KEY_BOTTOM:
        case KEY_META + '/':
            cursor_line = line_count - 1;
            cursor_byte = lines[cursor_line].length;
            break;
        case CONTROL('d'):
        case KEY_DELETE:
            delete_forward();
            break;
        case 127:
        case CONTROL('h'):
            delete_backward();
            break;
        case '\r':
        case '\n':
            split_line();
            break;
        case '\t':
            insert_character('\t');
            break;
        default:
            if (key >= 32 && key < 256 && key != 127)
                insert_character((char)key);
            else if (key != 0)
                set_message("No such key");
            break;
        }
        last_key_was_cut = cut;
        if (!vertical)
            wanted_column = display_width(lines[cursor_line].text, cursor_byte);
    }
}

int main(int argc, char** argv)
{
    struct termios raw;
    char text[300];
    if (!isatty(0) || tcgetattr(0, &saved_terminal) != 0)
    {
        fprintf(stderr, "nano: standard input is not a terminal\n");
        return 1;
    }
    if (argc > 1)
    {
        int added;
        snprintf(file_name, sizeof(file_name), "%s", argv[1]);
        added = read_file_at(file_name, 0);
        if (added < 0 && errno != ENOENT)
        {
            fprintf(stderr, "nano: %s: %s\n", file_name, strerror(errno));
            return 1;
        }
        if (added < 0)
            set_message("New file");
        else
        {
            snprintf(text, sizeof(text), "Read %d line%s", added, added == 1 ? "" : "s");
            set_message(text);
        }
    }
    if (line_count == 0)
        insert_line(0, "", 0);

    // Every key comes straight to the editor: no line editing, no echo, and ^C, ^Z, ^S and ^Q are commands here
    raw = saved_terminal;
    raw.c_iflag &= ~(tcflag_t)(ICRNL | IXON | INLCR | IGNCR);
    raw.c_lflag &= ~(tcflag_t)(ICANON | ECHO | ISIG | IEXTEN);
    raw.c_cc[VMIN] = 1;
    raw.c_cc[VTIME] = 0;
    tcsetattr(0, TCSAFLUSH, &raw);
    emit_text("\x1b[?1049h");

    edit();

    restore_terminal();
    free_lines();
    return 0;
}
