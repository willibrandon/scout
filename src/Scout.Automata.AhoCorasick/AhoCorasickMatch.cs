
namespace Scout;

/// <summary>
/// Represents one Aho-Corasick match over byte offsets.
/// </summary>
public readonly struct AhoCorasickMatch : IEquatable<AhoCorasickMatch>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AhoCorasickMatch" /> struct.
    /// </summary>
    /// <param name="patternId">The matched pattern's zero-based identifier.</param>
    /// <param name="start">The inclusive start byte offset.</param>
    /// <param name="end">The exclusive end byte offset.</param>
    public AhoCorasickMatch(int patternId, int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(patternId);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);

        PatternId = patternId;
        Start = start;
        End = end;
    }

    /// <summary>
    /// Gets the matched pattern's zero-based identifier.
    /// </summary>
    public int PatternId { get; }

    /// <summary>
    /// Gets the inclusive start byte offset.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the exclusive end byte offset.
    /// </summary>
    public int End { get; }

    /// <summary>
    /// Gets the match length in bytes.
    /// </summary>
    public int Length => End - Start;

    /// <summary>
    /// Gets a value indicating whether this match has zero length.
    /// </summary>
    public bool IsEmpty => Length == 0;

    /// <inheritdoc />
    public bool Equals(AhoCorasickMatch other)
    {
        return PatternId == other.PatternId && Start == other.Start && End == other.End;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is AhoCorasickMatch other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(PatternId, Start, End);
    }

    /// <summary>
    /// Compares two matches for equality.
    /// </summary>
    /// <param name="left">The left match.</param>
    /// <param name="right">The right match.</param>
    /// <returns><see langword="true" /> when the matches are equal.</returns>
    public static bool operator ==(AhoCorasickMatch left, AhoCorasickMatch right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Compares two matches for inequality.
    /// </summary>
    /// <param name="left">The left match.</param>
    /// <param name="right">The right match.</param>
    /// <returns><see langword="true" /> when the matches are different.</returns>
    public static bool operator !=(AhoCorasickMatch left, AhoCorasickMatch right)
    {
        return !left.Equals(right);
    }
}
