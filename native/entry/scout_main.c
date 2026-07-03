#include <fcntl.h>
#include <stddef.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <sys/types.h>
#include <unistd.h>
#if defined(__APPLE__)
#include <mach-o/dyld.h>
#endif

#ifndef O_CLOEXEC
#define O_CLOEXEC 0
#endif

#define PCRE2_CONFIG_JIT 1u
#define PCRE2_CONFIG_VERSION 11u
#define SCOUT_FAST_SEARCH_MAX_BYTES 65536
#define SCOUT_EXECUTABLE_PATH_MAX 4096
#ifndef SCOUT_VERSION
#define SCOUT_VERSION "0.4.1"
#endif
#ifndef SCOUT_RIPGREP_VERSION
#define SCOUT_RIPGREP_VERSION "15.1.0"
#endif
#ifndef SCOUT_RIPGREP_REVISION_SHORT
#define SCOUT_RIPGREP_REVISION_SHORT "4857d6fa67"
#endif

#ifndef SCOUT_LAUNCHER
extern int scout_entry(int argc, char **argv, char **envp);
#endif
extern int pcre2_config_8(unsigned int what, void *where);

static const char scout_short_version[] =
    "scout " SCOUT_VERSION " (ripgrep " SCOUT_RIPGREP_VERSION " compatible, rev " SCOUT_RIPGREP_REVISION_SHORT ")\n";

static int write_all(const char *bytes, size_t length)
{
    size_t written = 0;
    while (written < length)
    {
        ssize_t result = write(STDOUT_FILENO, bytes + written, length - written);
        if (result <= 0)
        {
            return 0;
        }

        written += (size_t)result;
    }

    return 1;
}

static int write_text(const char *text)
{
    return write_all(text, strlen(text));
}

static int arg_eq(const char *actual, const char *expected)
{
    return actual != NULL && strcmp(actual, expected) == 0;
}

static int is_no_config(const char *argument)
{
    return arg_eq(argument, "--no-config");
}

static const char *single_special_argument(int argc, char **argv)
{
    if (argc == 2)
    {
        return argv[1];
    }

    if (argc == 3)
    {
        if (is_no_config(argv[1]))
        {
            return argv[2];
        }

        if (is_no_config(argv[2]))
        {
            return argv[1];
        }
    }

    return NULL;
}

static int write_pcre2_version_line(void)
{
    unsigned int jit = 0;
    char version[64];
    int version_length = pcre2_config_8(PCRE2_CONFIG_VERSION, version);
    int has_jit = pcre2_config_8(PCRE2_CONFIG_JIT, &jit) >= 0 && jit == 1;
    if (version_length <= 1 || version_length > (int)sizeof(version))
    {
        return write_text("PCRE2 unknown is available (JIT is unavailable)\n");
    }

    int token_length = version_length - 1;
    for (int index = 0; index < token_length; index++)
    {
        if (version[index] == ' ')
        {
            token_length = index;
            break;
        }
    }

    char line[128];
    int written = snprintf(
        line,
        sizeof(line),
        "PCRE2 %.*s is available (JIT is %s)\n",
        token_length,
        version,
        has_jit ? "available" : "unavailable");
    if (written <= 0 || written >= (int)sizeof(line))
    {
        return 0;
    }

    return write_all(line, (size_t)written);
}

static int write_long_version(void)
{
#if defined(__aarch64__) || defined(_M_ARM64)
    return
        write_text(scout_short_version) &&
        write_text("\nfeatures:+pcre2\nsimd(compile):+NEON\nsimd(runtime):+NEON\n\n") &&
        write_pcre2_version_line() &&
        write_text("\n");
#else
    return -1;
#endif
}

static int is_simple_regex_literal(const char *pattern)
{
    if (pattern == NULL || pattern[0] == '\0' || pattern[0] == '-')
    {
        return 0;
    }

    for (const unsigned char *cursor = (const unsigned char *)pattern; *cursor != '\0'; cursor++)
    {
        unsigned char value = *cursor;
        if (value < 0x20 || value >= 0x7f)
        {
            return 0;
        }

        switch (value)
        {
            case '\\':
            case '^':
            case '$':
            case '.':
            case '|':
            case '?':
            case '*':
            case '+':
            case '(':
            case ')':
            case '[':
            case ']':
            case '{':
            case '}':
                return 0;
            default:
                break;
        }
    }

    return 1;
}

static int span_contains(const char *haystack, size_t haystack_length, const char *needle, size_t needle_length)
{
    if (needle_length == 0 || needle_length > haystack_length)
    {
        return 0;
    }

    size_t last = haystack_length - needle_length;
    for (size_t index = 0; index <= last; index++)
    {
        if (haystack[index] == needle[0] && memcmp(haystack + index, needle, needle_length) == 0)
        {
            return 1;
        }
    }

    return 0;
}

static int file_contains_nul(const char *bytes, size_t length)
{
    for (size_t index = 0; index < length; index++)
    {
        if (bytes[index] == '\0')
        {
            return 1;
        }
    }

    return 0;
}

static int run_simple_tiny_search(char **argv)
{
    const char *pattern = argv[2];
    const char *path = argv[3];
    if (!is_simple_regex_literal(pattern))
    {
        return -1;
    }

    int fd = open(path, O_RDONLY | O_CLOEXEC);
    if (fd < 0)
    {
        return -1;
    }

    struct stat status;
    if (fstat(fd, &status) != 0 || !S_ISREG(status.st_mode) || status.st_size < 0 || status.st_size > SCOUT_FAST_SEARCH_MAX_BYTES)
    {
        close(fd);
        return -1;
    }

    size_t length = (size_t)status.st_size;
    char *bytes = length == 0 ? NULL : (char *)malloc(length);
    if (length != 0 && bytes == NULL)
    {
        close(fd);
        return -1;
    }

    size_t offset = 0;
    while (offset < length)
    {
        ssize_t count = read(fd, bytes + offset, length - offset);
        if (count <= 0)
        {
            free(bytes);
            close(fd);
            return -1;
        }

        offset += (size_t)count;
    }

    close(fd);
    if (length == 0)
    {
        return 1;
    }

    if (file_contains_nul(bytes, length))
    {
        free(bytes);
        return -1;
    }

    size_t pattern_length = strlen(pattern);
    size_t line_start = 0;
    int matched = 0;
    for (size_t index = 0; index <= length; index++)
    {
        if (index != length && bytes[index] != '\n')
        {
            continue;
        }

        size_t line_end = index;
        if (span_contains(bytes + line_start, line_end - line_start, pattern, pattern_length))
        {
            size_t output_length = line_end - line_start;
            if (index != length)
            {
                output_length++;
            }

            if (!write_all(bytes + line_start, output_length))
            {
                free(bytes);
                return 2;
            }

            if (index == length && !write_text("\n"))
            {
                free(bytes);
                return 2;
            }

            matched = 1;
        }

        line_start = index + 1;
    }

    free(bytes);
    return matched ? 0 : 1;
}

static int try_run_native_fast_path(int argc, char **argv)
{
    const char *special = single_special_argument(argc, argv);
    if (arg_eq(special, "-V"))
    {
        return write_text(scout_short_version) ? 0 : 2;
    }

    if (arg_eq(special, "--pcre2-version"))
    {
        return write_pcre2_version_line() ? 0 : 2;
    }

    if (arg_eq(special, "--version"))
    {
        int result = write_long_version();
        if (result >= 0)
        {
            return result ? 0 : 2;
        }
    }

    if (argc == 4 && is_no_config(argv[1]))
    {
        return run_simple_tiny_search(argv);
    }

    return -1;
}

#ifdef SCOUT_LAUNCHER
static int try_resolve_path(char *buffer, size_t capacity)
{
    char *resolved = realpath(buffer, NULL);
    if (resolved == NULL)
    {
        return 0;
    }

    size_t length = strlen(resolved);
    if (length >= capacity)
    {
        free(resolved);
        return 0;
    }

    memcpy(buffer, resolved, length + 1);
    free(resolved);
    return 1;
}

static int try_get_current_executable_path(char *buffer, size_t capacity)
{
#if defined(__APPLE__)
    uint32_t size = (uint32_t)capacity;
    if (_NSGetExecutablePath(buffer, &size) != 0)
    {
        return 0;
    }

    (void)try_resolve_path(buffer, capacity);
    return 1;
#elif defined(__linux__)
    ssize_t length = readlink("/proc/self/exe", buffer, capacity - 1);
    if (length < 0 || (size_t)length >= capacity)
    {
        return 0;
    }

    buffer[length] = '\0';
    (void)try_resolve_path(buffer, capacity);
    return 1;
#else
    (void)buffer;
    (void)capacity;
    return 0;
#endif
}

static int make_real_binary_path(const char *argv0, char *buffer, size_t capacity)
{
    if (!try_get_current_executable_path(buffer, capacity))
    {
        if (argv0 == NULL || strlen(argv0) >= capacity)
        {
            return 0;
        }

        strcpy(buffer, argv0);
        (void)try_resolve_path(buffer, capacity);
    }

    char *slash = strrchr(buffer, '/');
    if (slash == NULL)
    {
        return 0;
    }

    const char real_name[] = "scout-real";
    size_t prefix_length = (size_t)(slash - buffer) + 1;
    if (prefix_length + sizeof(real_name) > capacity)
    {
        return 0;
    }

    memcpy(slash + 1, real_name, sizeof(real_name));
    return 1;
}

static int run_real_binary(int argc, char **argv, char **envp)
{
    (void)argc;
    char real_path[SCOUT_EXECUTABLE_PATH_MAX];
    if (make_real_binary_path(argv[0], real_path, sizeof(real_path)))
    {
        execve(real_path, argv, envp);
    }

    execvp("scout-real", argv);
    return 127;
}
#endif

int main(int argc, char **argv, char **envp)
{
    int fast_path_exit_code = try_run_native_fast_path(argc, argv);
    if (fast_path_exit_code >= 0)
    {
        return fast_path_exit_code;
    }

#ifdef SCOUT_LAUNCHER
    return run_real_binary(argc, argv, envp);
#else
    return scout_entry(argc, argv, envp);
#endif
}
