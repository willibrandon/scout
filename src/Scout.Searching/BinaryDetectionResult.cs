
namespace Scout;

/// <summary>
/// Describes binary detection results for a search input.
/// </summary>
public readonly struct BinaryDetectionResult : IEquatable<BinaryDetectionResult>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BinaryDetectionResult" /> struct.
    /// </summary>
    /// <param name="kind">The selected binary handling mode.</param>
    /// <param name="offset">The first binary NUL offset, or <c>-1</c> when none was found.</param>
    public BinaryDetectionResult(BinaryDetectionKind kind, int offset)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(offset, -1);

        Kind = kind;
        Offset = offset;
    }

    /// <summary>
    /// Gets the selected binary handling mode.
    /// </summary>
    public BinaryDetectionKind Kind { get; }

    /// <summary>
    /// Gets the first binary NUL offset, or <c>-1</c> when none was found.
    /// </summary>
    public int Offset { get; }

    /// <summary>
    /// Gets a value indicating whether a binary NUL byte was found.
    /// </summary>
    public bool IsBinary => Offset >= 0;

    /// <inheritdoc />
    public bool Equals(BinaryDetectionResult other)
    {
        return Kind == other.Kind && Offset == other.Offset;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is BinaryDetectionResult other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Kind, Offset);
    }

    /// <summary>
    /// Compares two results for equality.
    /// </summary>
    /// <param name="left">The left result.</param>
    /// <param name="right">The right result.</param>
    /// <returns><see langword="true" /> when the results are equal.</returns>
    public static bool operator ==(BinaryDetectionResult left, BinaryDetectionResult right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Compares two results for inequality.
    /// </summary>
    /// <param name="left">The left result.</param>
    /// <param name="right">The right result.</param>
    /// <returns><see langword="true" /> when the results are different.</returns>
    public static bool operator !=(BinaryDetectionResult left, BinaryDetectionResult right)
    {
        return !left.Equals(right);
    }
}
