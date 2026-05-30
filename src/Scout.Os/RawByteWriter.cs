using System;
using System.IO;

namespace Scout;

/// <summary>
/// Writes bytes directly to a stream without text encoding.
/// </summary>
public sealed class RawByteWriter
{
    private static readonly byte[] NewLine = [(byte)'\n'];

    private readonly Stream stream;

    /// <summary>
    /// Initializes a new instance of the <see cref="RawByteWriter" /> class.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    public RawByteWriter(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        this.stream = stream;
    }

    /// <summary>
    /// Writes bytes directly to the underlying stream.
    /// </summary>
    /// <param name="bytes">The bytes to write.</param>
    public void Write(ReadOnlySpan<byte> bytes)
    {
        stream.Write(bytes);
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
        stream.Flush();
    }
}
