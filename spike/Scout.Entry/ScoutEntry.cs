using System.Runtime.InteropServices;

namespace Scout.Entry;

/// <summary>
/// Native entry point exported to the C driver for platform argument round-trips.
/// </summary>
internal static unsafe partial class ScoutEntry
{
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "write")]
    private static partial nint WriteUnix(int fd, byte* buffer, nuint count);

    /// <summary>
    /// Echoes each argument exactly as captured at the platform boundary.
    /// </summary>
    /// <param name="argc">Argument count from the C runtime.</param>
    /// <param name="argv">Raw, NUL-terminated Unix argument byte pointers.</param>
    /// <param name="envp">Raw, NUL-terminated environment byte pointers.</param>
    /// <returns>The process exit code.</returns>
    [UnmanagedCallersOnly(EntryPoint = "scout_entry")]
    public static int Run(int argc, byte** argv, byte** envp)
    {
        _ = envp;

        if (OperatingSystem.IsWindows())
        {
            return RunWindows();
        }

        for (int index = 1; index < argc; index++)
        {
            byte* pointer = argv[index];
            nuint length = 0;
            while (pointer[length] != 0)
            {
                length++;
            }

            _ = WriteUnix(1, pointer, length);
            byte newline = (byte)'\n';
            _ = WriteUnix(1, &newline, 1);
        }

        return 0;
    }

    private static int RunWindows()
    {
        IntPtr stdout = GetStdHandle(StandardOutputHandle);
        if (stdout == IntPtr.Zero || stdout == InvalidHandleValue)
        {
            return 1;
        }

        IntPtr commandLine = GetCommandLineW();
        if (commandLine == IntPtr.Zero)
        {
            return 1;
        }

        IntPtr argvPointer = CommandLineToArgvW(commandLine, out int argc);
        if (argvPointer == IntPtr.Zero)
        {
            return 1;
        }

        try
        {
            char** argv = (char**)argvPointer;
            for (int index = 1; index < argc; index++)
            {
                char* pointer = argv[index];
                uint byteLength = checked((uint)(MeasureNullTerminated(pointer) * sizeof(char)));
                if (!WriteFile(stdout, (byte*)pointer, byteLength, out _, IntPtr.Zero))
                {
                    return 1;
                }

                char newline = '\n';
                if (!WriteFile(stdout, (byte*)&newline, sizeof(char), out _, IntPtr.Zero))
                {
                    return 1;
                }
            }
        }
        finally
        {
            _ = LocalFree(argvPointer);
        }

        return 0;
    }

    private static int MeasureNullTerminated(char* pointer)
    {
        int length = 0;
        while (pointer[length] != '\0')
        {
            length++;
        }

        return length;
    }

    private const int StandardOutputHandle = -11;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "GetCommandLineW")]
    private static partial IntPtr GetCommandLineW();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "GetStdHandle", SetLastError = true)]
    private static partial IntPtr GetStdHandle(int standardHandle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "WriteFile", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteFile(
        IntPtr file,
        byte* buffer,
        uint bytesToWrite,
        out uint bytesWritten,
        IntPtr overlapped);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("shell32.dll", EntryPoint = "CommandLineToArgvW", SetLastError = true)]
    private static partial IntPtr CommandLineToArgvW(IntPtr commandLine, out int argumentCount);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "LocalFree", SetLastError = true)]
    private static partial IntPtr LocalFree(IntPtr memory);
}
