using System;

namespace Scout;

/// <summary>
/// Stores the selected ripgrep result sorting mode.
/// </summary>
public readonly struct CliSortMode : IEquatable<CliSortMode>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CliSortMode" /> struct.
    /// </summary>
    /// <param name="reverse">Whether the sort should be descending.</param>
    /// <param name="kind">The sort criterion.</param>
    public CliSortMode(bool reverse, CliSortKind kind)
    {
        Reverse = reverse;
        Kind = kind;
    }

    /// <summary>
    /// Gets a value indicating whether sorting should be descending.
    /// </summary>
    public bool Reverse { get; }

    /// <summary>
    /// Gets the sort criterion.
    /// </summary>
    public CliSortKind Kind { get; }

    /// <summary>
    /// Returns a value indicating whether this value equals another sort mode.
    /// </summary>
    /// <param name="other">The sort mode to compare with this value.</param>
    /// <returns><see langword="true" /> if both values are equal.</returns>
    public bool Equals(CliSortMode other)
    {
        return Reverse == other.Reverse && Kind == other.Kind;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is CliSortMode other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Reverse, Kind);
    }

    /// <summary>
    /// Compares two sort modes for equality.
    /// </summary>
    /// <param name="left">The left sort mode.</param>
    /// <param name="right">The right sort mode.</param>
    /// <returns><see langword="true" /> when both sort modes are equal.</returns>
    public static bool operator ==(CliSortMode left, CliSortMode right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Compares two sort modes for inequality.
    /// </summary>
    /// <param name="left">The left sort mode.</param>
    /// <param name="right">The right sort mode.</param>
    /// <returns><see langword="true" /> when the sort modes differ.</returns>
    public static bool operator !=(CliSortMode left, CliSortMode right)
    {
        return !left.Equals(right);
    }
}
