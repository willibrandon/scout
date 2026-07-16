namespace Scout;

/// <summary>
/// Describes a contiguous run of complete records classified by whether every byte is ASCII.
/// </summary>
/// <param name="offset">The zero-based byte offset of the run in the source span.</param>
/// <param name="length">The length of the run in bytes.</param>
/// <param name="isAscii">Whether every record in the run contains only ASCII bytes.</param>
internal readonly struct AsciiRecordRun(int offset, int length, bool isAscii)
{
    /// <summary>
    /// Gets the zero-based byte offset of the run in the source span.
    /// </summary>
    public int Offset { get; } = offset;

    /// <summary>
    /// Gets the length of the run in bytes.
    /// </summary>
    public int Length { get; } = length;

    /// <summary>
    /// Gets a value indicating whether every record in the run contains only ASCII bytes.
    /// </summary>
    public bool IsAscii { get; } = isAscii;
}
