using System.Runtime.InteropServices;

namespace Scout;

internal static unsafe partial class NativeArgumentReader
{
    internal static OsString[] CaptureUnix(int argc, byte** argv)
    {
        if (argc < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(argc), argc, "Argument count cannot be negative.");
        }

        if (argv is null && argc != 0)
        {
            throw new ArgumentNullException(nameof(argv));
        }

        var arguments = new OsString[argc];
        for (int index = 0; index < argc; index++)
        {
            byte* pointer = argv[index];
            if (pointer is null)
            {
                throw new ArgumentException("Argument pointers cannot contain null entries.", nameof(argv));
            }

            int length = MeasureNullTerminated(pointer);
            arguments[index] = OsString.FromUnixBytes(new ReadOnlySpan<byte>(pointer, length));
        }

        return arguments;
    }

    internal static OsString[] CaptureWindowsCommandLine()
    {
        IntPtr commandLine = GetCommandLineW();
        if (commandLine == IntPtr.Zero)
        {
            throw new InvalidOperationException("GetCommandLineW returned a null command line.");
        }

        IntPtr argvPointer = CommandLineToArgvW(commandLine, out int argc);
        if (argvPointer == IntPtr.Zero)
        {
            throw new InvalidOperationException("CommandLineToArgvW failed.");
        }

        try
        {
            return CaptureWindowsWide(argc, (char**)argvPointer);
        }
        finally
        {
            _ = LocalFree(argvPointer);
        }
    }

    internal static OsString[] CaptureWindowsWide(int argc, char** argv)
    {
        if (argc < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(argc), argc, "Argument count cannot be negative.");
        }

        if (argv is null && argc != 0)
        {
            throw new ArgumentNullException(nameof(argv));
        }

        var arguments = new OsString[argc];
        for (int index = 0; index < argc; index++)
        {
            char* pointer = argv[index];
            if (pointer is null)
            {
                throw new ArgumentException("Argument pointers cannot contain null entries.", nameof(argv));
            }

            arguments[index] = OsString.FromWindowsString(new string(pointer));
        }

        return arguments;
    }

    private static int MeasureNullTerminated(byte* pointer)
    {
        nuint length = 0;
        while (pointer[length] != 0)
        {
            length++;
            if (length > int.MaxValue)
            {
                throw new ArgumentException("Argument is too large to address as a managed span.", nameof(pointer));
            }
        }

        return checked((int)length);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "GetCommandLineW")]
    private static partial IntPtr GetCommandLineW();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("shell32.dll", EntryPoint = "CommandLineToArgvW", SetLastError = true)]
    private static partial IntPtr CommandLineToArgvW(IntPtr commandLine, out int argumentCount);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "LocalFree", SetLastError = true)]
    private static partial IntPtr LocalFree(IntPtr memory);
}
