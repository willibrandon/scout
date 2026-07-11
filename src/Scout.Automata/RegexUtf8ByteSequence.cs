namespace Scout;

/// <summary>
/// Represents one ordered sequence of one to four inclusive UTF-8 byte ranges.
/// </summary>
/// <param name="first">The first byte range.</param>
/// <param name="second">The second byte range, when present.</param>
/// <param name="third">The third byte range, when present.</param>
/// <param name="fourth">The fourth byte range, when present.</param>
/// <param name="length">The number of ranges in the sequence.</param>
internal readonly struct RegexUtf8ByteSequence(
    RegexUtf8ByteRange first,
    RegexUtf8ByteRange second,
    RegexUtf8ByteRange third,
    RegexUtf8ByteRange fourth,
    int length)
{
    private readonly RegexUtf8ByteRange _first = first;
    private readonly RegexUtf8ByteRange _second = second;
    private readonly RegexUtf8ByteRange _third = third;
    private readonly RegexUtf8ByteRange _fourth = fourth;

    /// <summary>
    /// Gets the number of ranges in the sequence.
    /// </summary>
    public int Length { get; } = length;

    /// <summary>
    /// Gets a byte range by its position in the sequence.
    /// </summary>
    /// <param name="index">The zero-based range index.</param>
    /// <returns>The requested byte range.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index" /> is outside the sequence.
    /// </exception>
    public RegexUtf8ByteRange this[int index] => index switch
    {
        0 when Length > 0 => _first,
        1 when Length > 1 => _second,
        2 when Length > 2 => _third,
        3 when Length > 3 => _fourth,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    /// <summary>
    /// Creates a one-range UTF-8 sequence.
    /// </summary>
    /// <param name="range">The byte range.</param>
    /// <returns>The created sequence.</returns>
    public static RegexUtf8ByteSequence Create(RegexUtf8ByteRange range)
    {
        return new RegexUtf8ByteSequence(range, default, default, default, 1);
    }

    /// <summary>
    /// Creates a UTF-8 sequence from equally sized encoded scalar boundaries.
    /// </summary>
    /// <param name="start">The encoded lower scalar boundary.</param>
    /// <param name="end">The encoded upper scalar boundary.</param>
    /// <returns>The created sequence.</returns>
    /// <exception cref="ArgumentException">
    /// The boundaries have different lengths or are not two to four bytes long.
    /// </exception>
    public static RegexUtf8ByteSequence Create(ReadOnlySpan<byte> start, ReadOnlySpan<byte> end)
    {
        if (start.Length != end.Length || start.Length is < 2 or > 4)
        {
            throw new ArgumentException("UTF-8 sequence boundaries must have equal lengths from two through four bytes.");
        }

        RegexUtf8ByteRange first = new(start[0], end[0]);
        RegexUtf8ByteRange second = new(start[1], end[1]);
        RegexUtf8ByteRange third = start.Length > 2
            ? new RegexUtf8ByteRange(start[2], end[2])
            : default;
        RegexUtf8ByteRange fourth = start.Length > 3
            ? new RegexUtf8ByteRange(start[3], end[3])
            : default;
        return new RegexUtf8ByteSequence(first, second, third, fourth, start.Length);
    }
}
