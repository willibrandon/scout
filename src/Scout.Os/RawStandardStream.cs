using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Scout;

/// <summary>
/// Wraps a process standard stream without initializing text-oriented console state.
/// </summary>
/// <param name="windowsHandle">The Windows standard-stream handle.</param>
/// <param name="unixFileDescriptor">The Unix standard-stream file descriptor.</param>
/// <param name="access">The permitted stream access.</param>
internal sealed unsafe partial class RawStandardStream(
    IntPtr windowsHandle,
    int unixFileDescriptor,
    FileAccess access) : Stream
{
    private readonly IntPtr _windowsHandle = windowsHandle;
    private readonly int _unixFileDescriptor = unixFileDescriptor;
    private readonly bool _canRead = access == FileAccess.Read || access == FileAccess.ReadWrite;
    private readonly bool _canWrite = access == FileAccess.Write || access == FileAccess.ReadWrite;

    /// <inheritdoc />
    public override bool CanRead => _canRead;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => _canWrite;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length || count > buffer.Length - offset)
        {
            throw new ArgumentException("Offset and count exceed the buffer bounds.");
        }

        return Read(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        if (!CanRead)
        {
            throw new NotSupportedException();
        }

        if (buffer.IsEmpty)
        {
            return 0;
        }

        fixed (byte* pointer = buffer)
        {
            return OperatingSystem.IsWindows()
                ? ReadWindows(pointer, buffer.Length)
                : ReadUnix(pointer, buffer.Length);
        }
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length || count > buffer.Length - offset)
        {
            throw new ArgumentException("Offset and count exceed the buffer bounds.");
        }

        Write(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (!CanWrite)
        {
            throw new NotSupportedException();
        }

        if (buffer.IsEmpty)
        {
            return;
        }

        fixed (byte* pointer = buffer)
        {
            byte* cursor = pointer;
            int remaining = buffer.Length;
            while (remaining > 0)
            {
                int written = OperatingSystem.IsWindows()
                    ? WriteWindows(cursor, remaining)
                    : WriteUnix(cursor, remaining);
                if (written <= 0)
                {
                    throw new IOException("standard stream write failed");
                }

                cursor += written;
                remaining -= written;
            }
        }
    }

    private int ReadUnix(byte* buffer, int length)
    {
        nint result = Read(_unixFileDescriptor, buffer, (nuint)length);
        if (result < 0)
        {
            int error = Marshal.GetLastPInvokeError();
            ThrowIoException(error);
        }

        return checked((int)result);
    }

    private int WriteUnix(byte* buffer, int length)
    {
        nint result = Write(_unixFileDescriptor, buffer, (nuint)length);
        if (result < 0)
        {
            int error = Marshal.GetLastPInvokeError();
            ThrowIoException(error);
        }

        return checked((int)result);
    }

    private int ReadWindows(byte* buffer, int length)
    {
        if (ReadFile(_windowsHandle, buffer, length, out int bytesRead, IntPtr.Zero) == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            if (RawStandardStreams.IsWindowsStandardInputEndOfFile(error))
            {
                return 0;
            }

            ThrowIoException(error);
        }

        return bytesRead;
    }

    private int WriteWindows(byte* buffer, int length)
    {
        if (WriteFile(_windowsHandle, buffer, length, out int bytesWritten, IntPtr.Zero) == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            ThrowIoException(error);
        }

        return bytesWritten;
    }

    [DoesNotReturn]
    private static void ThrowIoException(int error)
    {
        throw new IOException(new Win32Exception(error).Message, RawStandardStreams.GetIoErrorHResult(error));
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    private static partial nint Read(int fileDescriptor, byte* buffer, nuint count);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    private static partial nint Write(int fileDescriptor, byte* buffer, nuint count);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int ReadFile(IntPtr file, byte* buffer, int bytesToRead, out int bytesRead, IntPtr overlapped);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int WriteFile(IntPtr file, byte* buffer, int bytesToWrite, out int bytesWritten, IntPtr overlapped);
}
