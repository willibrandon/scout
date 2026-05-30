using System;
using System.Text;

namespace Scout;

/// <summary>
/// Represents a platform operating-system string without forcing UTF-8 validity.
/// </summary>
public readonly struct OsString : IEquatable<OsString>
{
    private readonly byte[]? unixBytes;
    private readonly string? windowsText;

    private OsString(byte[]? unixBytes, string? windowsText)
    {
        this.unixBytes = unixBytes;
        this.windowsText = windowsText;
    }

    /// <summary>
    /// Gets an empty operating-system string for the current platform.
    /// </summary>
    public static OsString Empty => OperatingSystem.IsWindows()
        ? FromWindowsString(string.Empty)
        : FromUnixBytes([]);

    /// <summary>
    /// Gets a value indicating whether the value is stored as Unix bytes.
    /// </summary>
    public bool IsUnixBytes => windowsText is null;

    /// <summary>
    /// Gets a value indicating whether the value is stored as Windows UTF-16 text.
    /// </summary>
    public bool IsWindowsText => windowsText is not null;

    /// <summary>
    /// Creates an operating-system string from Unix bytes.
    /// </summary>
    /// <param name="bytes">The raw Unix bytes to copy.</param>
    /// <returns>An operating-system string preserving the supplied bytes.</returns>
    public static OsString FromUnixBytes(ReadOnlySpan<byte> bytes)
    {
        return new OsString(bytes.ToArray(), windowsText: null);
    }

    /// <summary>
    /// Creates an operating-system string from Windows UTF-16 text.
    /// </summary>
    /// <param name="text">The Windows text to copy.</param>
    /// <returns>An operating-system string preserving the supplied text.</returns>
    public static OsString FromWindowsString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new OsString(unixBytes: null, windowsText: text);
    }

    /// <summary>
    /// Creates an operating-system string from semantic text for the current platform.
    /// </summary>
    /// <param name="text">The text to encode for the current platform.</param>
    /// <returns>A platform operating-system string.</returns>
    public static OsString FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (OperatingSystem.IsWindows())
        {
            return FromWindowsString(text);
        }

        return FromUnixBytes(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// Gets the raw Unix bytes.
    /// </summary>
    /// <returns>The raw Unix bytes.</returns>
    /// <exception cref="InvalidOperationException">The value is a Windows UTF-16 string.</exception>
    public ReadOnlySpan<byte> AsUnixBytes()
    {
        if (windowsText is not null)
        {
            throw new InvalidOperationException("The OS string is stored as Windows UTF-16 text.");
        }

        return unixBytes.AsSpan();
    }

    /// <summary>
    /// Gets the Windows UTF-16 text.
    /// </summary>
    /// <returns>The Windows UTF-16 text.</returns>
    /// <exception cref="InvalidOperationException">The value is a Unix byte string.</exception>
    public string AsWindowsString()
    {
        if (windowsText is null)
        {
            throw new InvalidOperationException("The OS string is stored as Unix bytes.");
        }

        return windowsText;
    }

    /// <summary>
    /// Compares this value to raw Unix bytes.
    /// </summary>
    /// <param name="bytes">The bytes to compare with this value.</param>
    /// <returns><see langword="true" /> when this value is Unix bytes equal to <paramref name="bytes" />.</returns>
    public bool EqualsUnixBytes(ReadOnlySpan<byte> bytes)
    {
        return IsUnixBytes && AsUnixBytes().SequenceEqual(bytes);
    }

    /// <summary>
    /// Determines whether this value starts with raw Unix bytes.
    /// </summary>
    /// <param name="prefix">The byte prefix to compare with this value.</param>
    /// <returns><see langword="true" /> when this value is Unix bytes and starts with <paramref name="prefix" />.</returns>
    public bool StartsWithUnixBytes(ReadOnlySpan<byte> prefix)
    {
        return IsUnixBytes && AsUnixBytes().StartsWith(prefix);
    }

    /// <summary>
    /// Attempts to decode this value as valid UTF-8 text on Unix or returns Windows text directly.
    /// </summary>
    /// <param name="text">The decoded text when decoding succeeds.</param>
    /// <returns><see langword="true" /> when a valid text representation is available.</returns>
    public bool TryGetText(out string text)
    {
        if (windowsText is not null)
        {
            text = windowsText;
            return true;
        }

        try
        {
            text = Utf8Strict.GetString(AsUnixBytes());
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    /// <inheritdoc />
    public bool Equals(OsString other)
    {
        if (IsUnixBytes != other.IsUnixBytes)
        {
            return false;
        }

        return IsUnixBytes
            ? AsUnixBytes().SequenceEqual(other.AsUnixBytes())
            : string.Equals(windowsText, other.windowsText, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is OsString other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hashCode = new();
        hashCode.Add(IsUnixBytes);

        if (IsUnixBytes)
        {
            foreach (byte value in AsUnixBytes())
            {
                hashCode.Add(value);
            }
        }
        else
        {
            hashCode.Add(windowsText, StringComparer.Ordinal);
        }

        return hashCode.ToHashCode();
    }

    /// <summary>
    /// Compares two operating-system strings for platform-unit equality.
    /// </summary>
    /// <param name="left">The left operating-system string.</param>
    /// <param name="right">The right operating-system string.</param>
    /// <returns><see langword="true" /> when the values are equal.</returns>
    public static bool operator ==(OsString left, OsString right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Compares two operating-system strings for platform-unit inequality.
    /// </summary>
    /// <param name="left">The left operating-system string.</param>
    /// <param name="right">The right operating-system string.</param>
    /// <returns><see langword="true" /> when the values are different.</returns>
    public static bool operator !=(OsString left, OsString right)
    {
        return !left.Equals(right);
    }

    private static readonly Encoding Utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
}
