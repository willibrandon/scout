using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Scout;

/// <summary>
/// Wraps a process standard stream without initializing text-oriented console state.
/// </summary>
internal sealed unsafe partial class RawStandardStream : Stream
{
    private readonly IntPtr windowsHandle;
    private readonly int unixFileDescriptor;
    private readonly bool canRead;
    private readonly bool canWrite;

    public RawStandardStream(IntPtr windowsHandle, int unixFileDescriptor, FileAccess access)
    {
        this.windowsHandle = windowsHandle;
        this.unixFileDescriptor = unixFileDescriptor;
        canRead = access == FileAccess.Read || access == FileAccess.ReadWrite;
        canWrite = access == FileAccess.Write || access == FileAccess.ReadWrite;
    }

    public override bool CanRead => canRead;

    public override bool CanSeek => false;

    public override bool CanWrite => canWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

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

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

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
        nint result = Read(unixFileDescriptor, buffer, (nuint)length);
        if (result < 0)
        {
            ThrowLastIoException();
        }

        return checked((int)result);
    }

    private int WriteUnix(byte* buffer, int length)
    {
        nint result = Write(unixFileDescriptor, buffer, (nuint)length);
        if (result < 0)
        {
            ThrowLastIoException();
        }

        return checked((int)result);
    }

    private int ReadWindows(byte* buffer, int length)
    {
        if (ReadFile(windowsHandle, buffer, length, out int bytesRead, IntPtr.Zero) == 0)
        {
            ThrowLastIoException();
        }

        return bytesRead;
    }

    private int WriteWindows(byte* buffer, int length)
    {
        if (WriteFile(windowsHandle, buffer, length, out int bytesWritten, IntPtr.Zero) == 0)
        {
            ThrowLastIoException();
        }

        return bytesWritten;
    }

    private static void ThrowLastIoException()
    {
        int error = Marshal.GetLastPInvokeError();
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
