namespace Scout.Text.Regex;

/// <summary>
/// Describes a match over a byte span.
/// </summary>
public readonly struct ByteRegexMatch : IEquatable<ByteRegexMatch>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ByteRegexMatch" /> struct.
    /// </summary>
    /// <param name="start">The zero-based start offset.</param>
    /// <param name="length">The match length in bytes.</param>
    public ByteRegexMatch(int start, int length)
    {
        Start = start;
        Length = length;
    }

    /// <summary>
    /// Gets the zero-based start offset.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the match length in bytes.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Gets the exclusive end offset.
    /// </summary>
    public int End => Start + Length;

    /// <summary>
    /// Gets the matched byte span from a source input.
    /// </summary>
    /// <param name="input">The input that produced this match.</param>
    /// <returns>The matched bytes.</returns>
    public ReadOnlySpan<byte> Value(ReadOnlySpan<byte> input)
    {
        return input.Slice(Start, Length);
    }

    /// <summary>
    /// Returns a value indicating whether two matches are equal.
    /// </summary>
    /// <param name="left">The left match.</param>
    /// <param name="right">The right match.</param>
    /// <returns><see langword="true" /> when both matches have the same start and length.</returns>
    public static bool operator ==(ByteRegexMatch left, ByteRegexMatch right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Returns a value indicating whether two matches are not equal.
    /// </summary>
    /// <param name="left">The left match.</param>
    /// <param name="right">The right match.</param>
    /// <returns><see langword="true" /> when the matches differ.</returns>
    public static bool operator !=(ByteRegexMatch left, ByteRegexMatch right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(ByteRegexMatch other)
    {
        return Start == other.Start && Length == other.Length;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is ByteRegexMatch match && Equals(match);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Start, Length);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"[{Start}..{End})";
    }

    internal static ByteRegexMatch FromRegexMatch(RegexMatch match)
    {
        return new ByteRegexMatch(match.Start, match.Length);
    }
}
