
namespace Scout;

/// <summary>
/// Represents a PCRE2 match span.
/// </summary>
/// <param name="start">The zero-based start offset.</param>
/// <param name="length">The match length.</param>
public readonly struct Pcre2Match(int start, int length) : IEquatable<Pcre2Match>
{
    /// <summary>
    /// Initializes a match span with the pattern start retained before any <c>\K</c> adjustment.
    /// </summary>
    /// <param name="start">The zero-based reported match start.</param>
    /// <param name="length">The reported match length.</param>
    /// <param name="patternStart">The zero-based start of the successful pattern match.</param>
    internal Pcre2Match(int start, int length, int patternStart)
        : this(start, length)
    {
        PatternStart = patternStart;
    }

    /// <summary>
    /// Gets the zero-based start offset.
    /// </summary>
    public int Start { get; } = start;

    /// <summary>
    /// Gets the match length.
    /// </summary>
    public int Length { get; } = length;

    /// <summary>
    /// Gets the start of the successful PCRE2 pattern match before any <c>\K</c> adjustment.
    /// </summary>
    internal int PatternStart { get; } = start;

    /// <inheritdoc />
    public bool Equals(Pcre2Match other)
    {
        return Start == other.Start && Length == other.Length;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Pcre2Match other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Start, Length);
    }

    /// <summary>
    /// Tests whether two PCRE2 match spans are equal.
    /// </summary>
    /// <param name="left">The left match span.</param>
    /// <param name="right">The right match span.</param>
    /// <returns><see langword="true" /> when the spans are equal.</returns>
    public static bool operator ==(Pcre2Match left, Pcre2Match right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Tests whether two PCRE2 match spans are not equal.
    /// </summary>
    /// <param name="left">The left match span.</param>
    /// <param name="right">The right match span.</param>
    /// <returns><see langword="true" /> when the spans are not equal.</returns>
    public static bool operator !=(Pcre2Match left, Pcre2Match right)
    {
        return !left.Equals(right);
    }
}
