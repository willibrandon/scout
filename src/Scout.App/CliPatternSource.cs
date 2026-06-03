
namespace Scout;

/// <summary>
/// Represents one explicit command-line pattern source.
/// </summary>
public readonly struct CliPatternSource : IEquatable<CliPatternSource>
{
    private CliPatternSource(OsString value, bool file)
    {
        Value = value;
        IsFile = file;
    }

    /// <summary>
    /// Gets the pattern text or pattern-file path.
    /// </summary>
    public OsString Value { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Value" /> is a pattern-file path.
    /// </summary>
    public bool IsFile { get; }

    /// <summary>
    /// Creates an inline pattern source.
    /// </summary>
    /// <param name="pattern">The inline pattern.</param>
    /// <returns>The pattern source.</returns>
    public static CliPatternSource Pattern(OsString pattern)
    {
        return new CliPatternSource(pattern, file: false);
    }

    /// <summary>
    /// Creates a pattern-file source.
    /// </summary>
    /// <param name="path">The pattern-file path.</param>
    /// <returns>The pattern source.</returns>
    public static CliPatternSource File(OsString path)
    {
        return new CliPatternSource(path, file: true);
    }

    /// <inheritdoc />
    public bool Equals(CliPatternSource other)
    {
        return Value == other.Value && IsFile == other.IsFile;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is CliPatternSource other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Value, IsFile);
    }

    /// <summary>
    /// Compares two pattern sources for equality.
    /// </summary>
    /// <param name="left">The left pattern source.</param>
    /// <param name="right">The right pattern source.</param>
    /// <returns><see langword="true" /> when the sources are equal.</returns>
    public static bool operator ==(CliPatternSource left, CliPatternSource right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Compares two pattern sources for inequality.
    /// </summary>
    /// <param name="left">The left pattern source.</param>
    /// <param name="right">The right pattern source.</param>
    /// <returns><see langword="true" /> when the sources are different.</returns>
    public static bool operator !=(CliPatternSource left, CliPatternSource right)
    {
        return !left.Equals(right);
    }
}
