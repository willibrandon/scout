using System.Runtime.InteropServices;

namespace Scout;

/// <summary>
/// Native entry point exported to the C driver for raw argv byte round-trips.
/// </summary>
internal static unsafe class ScoutEntry
{
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("libc", EntryPoint = "write", ExactSpelling = true)]
    private static extern nint Write(int fd, byte* buffer, nuint count);

    /// <summary>
    /// Echoes each raw argument byte string to file descriptor 1.
    /// </summary>
    /// <param name="argc">Argument count from the C runtime.</param>
    /// <param name="argv">Raw, NUL-terminated argument byte pointers.</param>
    /// <param name="envp">Raw, NUL-terminated environment byte pointers.</param>
    /// <returns>The process exit code.</returns>
    [UnmanagedCallersOnly(EntryPoint = "scout_entry")]
    public static int Run(int argc, byte** argv, byte** envp)
    {
        _ = envp;

        for (int index = 1; index < argc; index++)
        {
            byte* pointer = argv[index];
            nuint length = 0;
            while (pointer[length] != 0)
            {
                length++;
            }

            _ = Write(1, pointer, length);
            byte newline = (byte)'\n';
            _ = Write(1, &newline, 1);
        }

        return 0;
    }
}
