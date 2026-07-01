namespace Scout.Text.Regex;

/// <summary>
/// Describes the first match found by a byte regex set.
/// </summary>
public readonly struct ByteRegexSetMatch : IEquatable<ByteRegexSetMatch>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ByteRegexSetMatch" /> struct.
    /// </summary>
    /// <param name="patternId">The zero-based pattern identifier.</param>
    /// <param name="match">The byte match.</param>
    public ByteRegexSetMatch(int patternId, ByteRegexMatch match)
    {
        PatternId = patternId;
        Match = match;
    }

    /// <summary>
    /// Gets the zero-based pattern identifier.
    /// </summary>
    public int PatternId { get; }

    /// <summary>
    /// Gets the byte match.
    /// </summary>
    public ByteRegexMatch Match { get; }

    /// <summary>
    /// Returns a value indicating whether two values are equal.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><see langword="true" /> when both values have the same pattern and match.</returns>
    public static bool operator ==(ByteRegexSetMatch left, ByteRegexSetMatch right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Returns a value indicating whether two values are not equal.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><see langword="true" /> when the values differ.</returns>
    public static bool operator !=(ByteRegexSetMatch left, ByteRegexSetMatch right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(ByteRegexSetMatch other)
    {
        return PatternId == other.PatternId && Match == other.Match;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is ByteRegexSetMatch match && Equals(match);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(PatternId, Match);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{PatternId}: {Match}";
    }
}
