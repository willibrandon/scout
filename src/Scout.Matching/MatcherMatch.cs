using System;

namespace Scout;

/// <summary>
/// Describes a byte match passed through Scout's matcher callback ABI.
/// </summary>
public readonly struct MatcherMatch : IEquatable<MatcherMatch>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MatcherMatch" /> struct.
    /// </summary>
    /// <param name="start">The zero-based match start offset.</param>
    /// <param name="length">The match length in bytes.</param>
    public MatcherMatch(int start, int length)
    {
        Start = start;
        Length = length;
    }

    /// <summary>
    /// Gets the zero-based match start offset.
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
    /// Returns a value indicating whether two matches are equal.
    /// </summary>
    /// <param name="left">The left match.</param>
    /// <param name="right">The right match.</param>
    /// <returns><see langword="true" /> when both matches have the same start and length.</returns>
    public static bool operator ==(MatcherMatch left, MatcherMatch right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Returns a value indicating whether two matches are not equal.
    /// </summary>
    /// <param name="left">The left match.</param>
    /// <param name="right">The right match.</param>
    /// <returns><see langword="true" /> when the matches differ.</returns>
    public static bool operator !=(MatcherMatch left, MatcherMatch right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is MatcherMatch match && Equals(match);
    }

    /// <inheritdoc />
    public bool Equals(MatcherMatch other)
    {
        return Start == other.Start && Length == other.Length;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Start, Length);
    }
}
