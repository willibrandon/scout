using System.Runtime.InteropServices;

namespace Scout;

/// <summary>
/// Detects stdout terminal capabilities without using text-oriented console APIs.
/// </summary>
public static partial class StandardOutputProbe
{
    private const int StandardOutputFileDescriptor = 1;
    private const int StandardOutputHandle = -11;
    private const uint WindowsFileTypeChar = 0x0002;
    private const uint WindowsEnableVirtualTerminalProcessing = 0x0004;

    /// <summary>
    /// Returns true when stdout is connected to a terminal/console.
    /// </summary>
    /// <returns><see langword="true" /> when stdout should be treated as terminal output.</returns>
    public static bool IsTerminal()
    {
        if (OperatingSystem.IsWindows())
        {
            return IsTerminalWindows();
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return IsATty(StandardOutputFileDescriptor) == 1;
        }

        return false;
    }

    /// <summary>
    /// Enables ANSI escape processing on Windows consoles when available.
    /// </summary>
    public static void TryEnableVirtualTerminalProcessing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        IntPtr handle = GetStdHandle(StandardOutputHandle);
        if (!IsValidHandle(handle) || GetConsoleMode(handle, out uint mode) == 0)
        {
            return;
        }

        _ = SetConsoleMode(handle, mode | WindowsEnableVirtualTerminalProcessing);
    }

    private static bool IsTerminalWindows()
    {
        IntPtr handle = GetStdHandle(StandardOutputHandle);
        if (!IsValidHandle(handle) || GetFileType(handle) != WindowsFileTypeChar)
        {
            return false;
        }

        return GetConsoleMode(handle, out _) != 0;
    }

    private static bool IsValidHandle(IntPtr handle)
    {
        return handle != IntPtr.Zero && handle != new IntPtr(-1);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "isatty", SetLastError = true)]
    private static partial int IsATty(int fileDescriptor);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GetStdHandle(int handle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint GetFileType(IntPtr handle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int GetConsoleMode(IntPtr handle, out uint mode);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int SetConsoleMode(IntPtr handle, uint mode);
}
