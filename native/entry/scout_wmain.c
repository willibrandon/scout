#include <wchar.h>

extern int scout_entry(int argc, char **argv, char **envp);

int wmain(int argc, wchar_t **argv, wchar_t **envp)
{
    (void)argc;
    (void)argv;
    (void)envp;
    return scout_entry(0, (char **)0, (char **)0);
}
