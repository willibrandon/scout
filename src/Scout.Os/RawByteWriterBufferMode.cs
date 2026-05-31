namespace Scout;

/// <summary>
/// Identifies the buffering behavior used by <see cref="RawByteWriter" />.
/// </summary>
public enum RawByteWriterBufferMode
{
    /// <summary>
    /// Write bytes directly to the underlying stream.
    /// </summary>
    None,

    /// <summary>
    /// Buffer bytes until a line feed is written or the buffer is explicitly flushed.
    /// </summary>
    Line,

    /// <summary>
    /// Buffer bytes until the block buffer fills or the buffer is explicitly flushed.
    /// </summary>
    Block,
}
