namespace Scout;

/// <summary>
/// Represents one inclusive byte range in an ordered UTF-8 sequence.
/// </summary>
/// <param name="start">The inclusive lower byte.</param>
/// <param name="end">The inclusive upper byte.</param>
internal readonly struct RegexUtf8ByteRange(byte start, byte end) : IEquatable<RegexUtf8ByteRange>
{
    /// <summary>
    /// Gets the inclusive lower byte.
    /// </summary>
    public byte Start { get; } = start;

    /// <summary>
    /// Gets the inclusive upper byte.
    /// </summary>
    public byte End { get; } = end;

    /// <summary>
    /// Determines whether two ranges have the same bounds.
    /// </summary>
    /// <param name="other">The other range.</param>
    /// <returns><see langword="true" /> when both bounds are equal.</returns>
    public bool Equals(RegexUtf8ByteRange other)
    {
        return Start == other.Start && End == other.End;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is RegexUtf8ByteRange other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Start, End);
    }
}
