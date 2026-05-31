using System;

namespace Scout;

/// <summary>
/// Stores one ordered file type change parsed from the command line.
/// </summary>
public readonly struct CliTypeChange : IEquatable<CliTypeChange>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CliTypeChange" /> struct.
    /// </summary>
    /// <param name="kind">The type change kind.</param>
    /// <param name="value">The type name or type definition.</param>
    public CliTypeChange(CliTypeChangeKind kind, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        Kind = kind;
        Value = value;
    }

    /// <summary>
    /// Gets the type change kind.
    /// </summary>
    public CliTypeChangeKind Kind { get; }

    /// <summary>
    /// Gets the type name or type definition text.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Returns a value indicating whether this value equals another type change.
    /// </summary>
    /// <param name="other">The type change to compare with this value.</param>
    /// <returns><see langword="true" /> if both values are equal.</returns>
    public bool Equals(CliTypeChange other)
    {
        return Kind == other.Kind && StringComparer.Ordinal.Equals(Value, other.Value);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is CliTypeChange other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Kind, StringComparer.Ordinal.GetHashCode(Value));
    }

    /// <summary>
    /// Compares two type changes for equality.
    /// </summary>
    /// <param name="left">The left type change.</param>
    /// <param name="right">The right type change.</param>
    /// <returns><see langword="true" /> when both values are equal.</returns>
    public static bool operator ==(CliTypeChange left, CliTypeChange right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Compares two type changes for inequality.
    /// </summary>
    /// <param name="left">The left type change.</param>
    /// <param name="right">The right type change.</param>
    /// <returns><see langword="true" /> when the values differ.</returns>
    public static bool operator !=(CliTypeChange left, CliTypeChange right)
    {
        return !left.Equals(right);
    }
}
