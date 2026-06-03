using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Scout;

/// <summary>
/// Reads the Unix current working directory as raw bytes.
/// </summary>
public static unsafe partial class RawUnixCurrentDirectory
{
    private const int InitialBufferLength = 256;
    private const int MaximumBufferLength = 1 << 20;
    private const int RangeError = 34;

    /// <summary>
    /// Gets the current working directory from <c>getcwd</c> without decoding it as UTF-8.
    /// </summary>
    /// <returns>The current working directory bytes without the trailing NUL terminator.</returns>
    /// <exception cref="IOException">The current directory cannot be read.</exception>
    /// <exception cref="PlatformNotSupportedException">The current platform is Windows.</exception>
    public static byte[] Get()
    {
        if (OperatingSystem.IsWindows() || (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()))
        {
            throw new PlatformNotSupportedException("Raw Unix current directory access is only available on Linux and macOS.");
        }

        int bufferLength = InitialBufferLength;
        while (bufferLength <= MaximumBufferLength)
        {
            byte[] buffer = new byte[bufferLength];
            fixed (byte* bufferPointer = buffer)
            {
                nint result = GetCurrentWorkingDirectory(bufferPointer, (nuint)buffer.Length);
                if (result != 0)
                {
                    int length = MeasureNullTerminated(bufferPointer, buffer.Length);
                    return buffer.AsSpan(0, length).ToArray();
                }

                int error = Marshal.GetLastPInvokeError();
                if (error != RangeError)
                {
                    throw new IOException(new Win32Exception(error).Message);
                }
            }

            bufferLength *= 2;
        }

        throw new IOException("Current directory path exceeds the maximum raw Unix buffer length.");
    }

    private static int MeasureNullTerminated(byte* buffer, int bufferLength)
    {
        int length = 0;
        while (length < bufferLength && buffer[length] != 0)
        {
            length++;
        }

        return length;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "getcwd", SetLastError = true)]
    private static partial nint GetCurrentWorkingDirectory(byte* buffer, nuint size);
}
