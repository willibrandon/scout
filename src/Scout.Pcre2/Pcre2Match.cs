
namespace Scout;

/// <summary>
/// Represents a PCRE2 match span.
/// </summary>
public readonly struct Pcre2Match : IEquatable<Pcre2Match>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Pcre2Match" /> struct.
    /// </summary>
    /// <param name="start">The zero-based start offset.</param>
    /// <param name="length">The match length.</param>
    public Pcre2Match(int start, int length)
    {
        Start = start;
        Length = length;
    }

    /// <summary>
    /// Gets the zero-based start offset.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the match length.
    /// </summary>
    public int Length { get; }

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
