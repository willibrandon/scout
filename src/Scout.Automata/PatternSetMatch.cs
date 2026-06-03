
namespace Scout;

/// <summary>
/// Describes the first match found by a pattern set.
/// </summary>
public readonly struct PatternSetMatch : IEquatable<PatternSetMatch>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PatternSetMatch" /> struct.
    /// </summary>
    /// <param name="patternId">The zero-based pattern identifier.</param>
    /// <param name="match">The byte match.</param>
    public PatternSetMatch(int patternId, RegexMatch match)
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
    public RegexMatch Match { get; }

    /// <summary>
    /// Returns a value indicating whether two values are equal.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><see langword="true" /> when both values have the same pattern and match.</returns>
    public static bool operator ==(PatternSetMatch left, PatternSetMatch right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Returns a value indicating whether two values are not equal.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><see langword="true" /> when the values differ.</returns>
    public static bool operator !=(PatternSetMatch left, PatternSetMatch right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(PatternSetMatch other)
    {
        return PatternId == other.PatternId && Match == other.Match;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is PatternSetMatch match && Equals(match);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(PatternId, Match);
    }
}
