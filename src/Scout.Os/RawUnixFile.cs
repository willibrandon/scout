using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Scout;

/// <summary>
/// Opens Unix file paths from raw bytes without decoding them as UTF-8.
/// </summary>
public static unsafe partial class RawUnixFile
{
    private const int OpenReadOnly = 0;
    private const int OpenWriteOnly = 1;
    private const uint FileMode = 0x1B6;

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
        if (OperatingSystem.IsWindows() || (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()))
        {
            throw new PlatformNotSupportedException("Raw Unix byte paths are not available on Windows.");
        }

        ValidatePath(path);
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

    /// <summary>
    /// Writes bytes to a raw Unix path, creating or truncating the file.
    /// </summary>
    /// <param name="path">The non-NUL-terminated raw file path bytes.</param>
    /// <param name="bytes">The bytes to write.</param>
    /// <exception cref="ArgumentException">The path is empty or contains a NUL byte.</exception>
    /// <exception cref="IOException">The path cannot be opened or written.</exception>
    /// <exception cref="PlatformNotSupportedException">The current platform is Windows.</exception>
    public static void WriteAllBytes(ReadOnlySpan<byte> path, ReadOnlySpan<byte> bytes)
    {
        if (OperatingSystem.IsWindows() || (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()))
        {
            throw new PlatformNotSupportedException("Raw Unix byte paths are not available on Windows.");
        }

        ValidatePath(path);
        byte[] terminatedPath = new byte[path.Length + 1];
        path.CopyTo(terminatedPath);
        fixed (byte* pathPointer = terminatedPath)
        {
            int flags = OpenWriteOnly | OpenCreate | OpenTruncate;
            int fileDescriptor = Open(pathPointer, flags, FileMode);
            if (fileDescriptor < 0)
            {
                int error = Marshal.GetLastPInvokeError();
                throw new IOException(new Win32Exception(error).Message);
            }

            using var handle = new SafeFileHandle((IntPtr)fileDescriptor, ownsHandle: true);
            using var stream = new FileStream(handle, FileAccess.Write);
            stream.Write(bytes);
        }
    }

    private static int OpenCreate => OperatingSystem.IsMacOS() ? 0x0200 : 0x0040;

    private static int OpenTruncate => OperatingSystem.IsMacOS() ? 0x0400 : 0x0200;

    private static void ValidatePath(ReadOnlySpan<byte> path)
    {
        if (path.IsEmpty)
        {
            throw new ArgumentException("Path cannot be empty.", nameof(path));
        }

        if (path.Contains((byte)0))
        {
            throw new ArgumentException("Unix paths cannot contain a NUL byte.", nameof(path));
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "open", SetLastError = true)]
    private static partial int Open(byte* path, int flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "open", SetLastError = true)]
    private static partial int Open(byte* path, int flags, uint mode);
}
