
namespace Scout;

/// <summary>
/// Stores one ordered traversal override glob parsed from the command line.
/// </summary>
public readonly struct CliGlobPattern : IEquatable<CliGlobPattern>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CliGlobPattern" /> struct.
    /// </summary>
    /// <param name="value">The glob pattern text.</param>
    /// <param name="caseInsensitive">Whether this glob is intrinsically case-insensitive.</param>
    public CliGlobPattern(string value, bool caseInsensitive)
    {
        ArgumentNullException.ThrowIfNull(value);

        Value = value;
        CaseInsensitive = caseInsensitive;
    }

    /// <summary>
    /// Gets the glob pattern text.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets a value indicating whether this glob is intrinsically case-insensitive.
    /// </summary>
    public bool CaseInsensitive { get; }

    /// <summary>
    /// Returns a value indicating whether this value equals another glob pattern.
    /// </summary>
    /// <param name="other">The glob pattern to compare with this value.</param>
    /// <returns><see langword="true" /> if both values are equal.</returns>
    public bool Equals(CliGlobPattern other)
    {
        return CaseInsensitive == other.CaseInsensitive && StringComparer.Ordinal.Equals(Value, other.Value);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is CliGlobPattern other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(StringComparer.Ordinal.GetHashCode(Value), CaseInsensitive);
    }

    /// <summary>
    /// Compares two glob patterns for equality.
    /// </summary>
    /// <param name="left">The left glob pattern.</param>
    /// <param name="right">The right glob pattern.</param>
    /// <returns><see langword="true" /> when both values are equal.</returns>
    public static bool operator ==(CliGlobPattern left, CliGlobPattern right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Compares two glob patterns for inequality.
    /// </summary>
    /// <param name="left">The left glob pattern.</param>
    /// <param name="right">The right glob pattern.</param>
    /// <returns><see langword="true" /> when the values differ.</returns>
    public static bool operator !=(CliGlobPattern left, CliGlobPattern right)
    {
        return !left.Equals(right);
    }
}
