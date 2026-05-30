using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Scout;

/// <summary>
/// Opens Unix file paths from raw bytes without decoding them as UTF-8.
/// </summary>
public static unsafe class RawUnixFile
{
    private const int OpenReadOnly = 0;

    /// <summary>
    /// Opens a raw Unix path for reading.
    /// </summary>
    /// <param name="path">The non-NUL-terminated raw path bytes.</param>
    /// <returns>A safe handle owning the opened file descriptor.</returns>
    /// <exception cref="ArgumentException">The path is empty or contains a NUL byte.</exception>
    /// <exception cref="IOException">The path cannot be opened.</exception>
    /// <exception cref="PlatformNotSupportedException">The current platform is Windows.</exception>
    public static SafeFileHandle OpenRead(ReadOnlySpan<byte> path)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Raw Unix byte paths are not available on Windows.");
        }

        if (path.IsEmpty)
        {
            throw new ArgumentException("Path cannot be empty.", nameof(path));
        }

        if (path.Contains((byte)0))
        {
            throw new ArgumentException("Unix paths cannot contain a NUL byte.", nameof(path));
        }

        byte[] terminatedPath = new byte[path.Length + 1];
        path.CopyTo(terminatedPath);
        fixed (byte* pathPointer = terminatedPath)
        {
            int fileDescriptor = Open(pathPointer, OpenReadOnly);
            if (fileDescriptor < 0)
            {
                int error = Marshal.GetLastPInvokeError();
                throw new IOException(new Win32Exception(error).Message);
            }

            return new SafeFileHandle((IntPtr)fileDescriptor, ownsHandle: true);
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open(byte* path, int flags);
}
