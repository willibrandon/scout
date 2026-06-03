
namespace Scout;

/// <summary>
/// Writes bytes directly to a stream without text encoding.
/// </summary>
public sealed class RawByteWriter
{
    private const int BufferSize = 64 * 1024;

    private static readonly byte[] NewLine = [(byte)'\n'];

    private readonly Stream stream;
    private byte[]? buffer;
    private int bufferedLength;
    private RawByteWriterBufferMode bufferMode;

    /// <summary>
    /// Initializes a new instance of the <see cref="RawByteWriter" /> class.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    public RawByteWriter(Stream stream)
        : this(stream, RawByteWriterBufferMode.None)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RawByteWriter" /> class.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="bufferMode">The buffering mode to use.</param>
    public RawByteWriter(Stream stream, RawByteWriterBufferMode bufferMode)
    {
        ArgumentNullException.ThrowIfNull(stream);
        this.stream = stream;
        SetBufferMode(bufferMode);
    }

    /// <summary>
    /// Changes buffering mode after flushing any pending buffered bytes.
    /// </summary>
    /// <param name="mode">The buffering mode to use for future writes.</param>
    public void SetBufferMode(RawByteWriterBufferMode mode)
    {
        if (bufferedLength > 0)
        {
            FlushBuffer(flushStream: true);
        }

        bufferMode = mode;
        if (mode == RawByteWriterBufferMode.None)
        {
            buffer = null;
            return;
        }

        buffer ??= new byte[BufferSize];
    }

    /// <summary>
    /// Writes bytes directly to the underlying stream.
    /// </summary>
    /// <param name="bytes">The bytes to write.</param>
    public void Write(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        if (bufferMode == RawByteWriterBufferMode.None)
        {
            stream.Write(bytes);
            return;
        }

        WriteBuffered(bytes);
    }

    /// <summary>
    /// Writes bytes followed by a line-feed byte.
    /// </summary>
    /// <param name="bytes">The bytes to write before the line feed.</param>
    public void WriteLine(ReadOnlySpan<byte> bytes)
    {
        Write(bytes);
        Write(NewLine);
    }

    /// <summary>
    /// Flushes the underlying stream.
    /// </summary>
    public void Flush()
    {
        FlushBuffer(flushStream: false);
        stream.Flush();
    }

    private void WriteBuffered(ReadOnlySpan<byte> bytes)
    {
        while (bytes.Length != 0)
        {
            int newlineOffset = bufferMode == RawByteWriterBufferMode.Line
                ? bytes.IndexOf((byte)'\n')
                : -1;
            int chunkLength = newlineOffset < 0 ? bytes.Length : newlineOffset + 1;
            WriteBufferedChunk(bytes[..chunkLength]);
            if (newlineOffset >= 0)
            {
                FlushBuffer(flushStream: true);
            }

            bytes = bytes[chunkLength..];
        }
    }

    private void WriteBufferedChunk(ReadOnlySpan<byte> bytes)
    {
        byte[] activeBuffer = buffer!;
        while (bytes.Length != 0)
        {
            if (bufferedLength == 0 && bytes.Length >= activeBuffer.Length)
            {
                stream.Write(bytes);
                return;
            }

            int writable = Math.Min(bytes.Length, activeBuffer.Length - bufferedLength);
            bytes[..writable].CopyTo(activeBuffer.AsSpan(bufferedLength));
            bufferedLength += writable;
            bytes = bytes[writable..];

            if (bufferedLength == activeBuffer.Length)
            {
                FlushBuffer(flushStream: false);
            }
        }
    }

    private void FlushBuffer(bool flushStream)
    {
        if (bufferedLength > 0)
        {
            stream.Write(buffer.AsSpan(0, bufferedLength));
            bufferedLength = 0;
        }

        if (flushStream)
        {
            stream.Flush();
        }
    }
}
