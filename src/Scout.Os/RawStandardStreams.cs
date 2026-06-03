using System.Runtime.InteropServices;

namespace Scout;

/// <summary>
/// Provides raw byte writers for the process standard streams.
/// </summary>
public static partial class RawStandardStreams
{
    private const int StandardInputFileDescriptor = 0;
    private const int StandardOutputFileDescriptor = 1;
    private const int StandardErrorFileDescriptor = 2;
    private const int StandardInputHandle = -10;
    private const int StandardOutputHandle = -11;
    private const int StandardErrorHandle = -12;

    /// <summary>
    /// Opens a raw stream for stdin.
    /// </summary>
    /// <returns>A stream over stdin.</returns>
    public static Stream OpenInput()
    {
        return Open(StandardInputFileDescriptor, StandardInputHandle, FileAccess.Read);
    }

    /// <summary>
    /// Opens a raw byte writer for stdout.
    /// </summary>
    /// <returns>A raw byte writer over stdout.</returns>
    public static RawByteWriter OpenOutput()
    {
        return new RawByteWriter(Open(StandardOutputFileDescriptor, StandardOutputHandle, FileAccess.Write));
    }

    /// <summary>
    /// Opens a raw byte writer for stderr.
    /// </summary>
    /// <returns>A raw byte writer over stderr.</returns>
    public static RawByteWriter OpenError()
    {
        return new RawByteWriter(Open(StandardErrorFileDescriptor, StandardErrorHandle, FileAccess.Write));
    }

    private static RawStandardStream Open(int unixFileDescriptor, int windowsHandle, FileAccess access)
    {
        IntPtr handle = OperatingSystem.IsWindows()
            ? GetStdHandle(windowsHandle)
            : new IntPtr(unixFileDescriptor);
        return new RawStandardStream(handle, unixFileDescriptor, access);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GetStdHandle(int handle);
}
