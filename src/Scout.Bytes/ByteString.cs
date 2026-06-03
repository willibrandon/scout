
namespace Scout;

/// <summary>
/// Represents an immutable, owned byte string.
/// </summary>
public readonly struct ByteString : IEquatable<ByteString>
{
    private readonly byte[]? bytes;

    private ByteString(byte[] bytes)
    {
        this.bytes = bytes;
    }

    /// <summary>
    /// Gets the empty byte string.
    /// </summary>
    public static ByteString Empty { get; } = new(Array.Empty<byte>());

    /// <summary>
    /// Gets the number of bytes in the string.
    /// </summary>
    public int Length => AsSpan().Length;

    /// <summary>
    /// Gets a value indicating whether this byte string is empty.
    /// </summary>
    public bool IsEmpty => Length == 0;

    /// <summary>
    /// Copies bytes into a new immutable byte string.
    /// </summary>
    /// <param name="source">The source bytes to copy.</param>
    /// <returns>An immutable byte string containing the copied bytes.</returns>
    public static ByteString Copy(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
        {
            return Empty;
        }

        return new ByteString(source.ToArray());
    }

    /// <summary>
    /// Gets this byte string as a read-only span.
    /// </summary>
    /// <returns>The byte string contents.</returns>
    public ReadOnlySpan<byte> AsSpan()
    {
        return bytes.AsSpan();
    }

    /// <summary>
    /// Copies this byte string into a new array.
    /// </summary>
    /// <returns>A new byte array containing this byte string.</returns>
    public byte[] ToArray()
    {
        return AsSpan().ToArray();
    }

    /// <inheritdoc />
    public bool Equals(ByteString other)
    {
        return AsSpan().SequenceEqual(other.AsSpan());
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is ByteString other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hashCode = new();
        foreach (byte value in AsSpan())
        {
            hashCode.Add(value);
        }

        return hashCode.ToHashCode();
    }

    /// <summary>
    /// Compares two byte strings for byte-for-byte equality.
    /// </summary>
    /// <param name="left">The left byte string.</param>
    /// <param name="right">The right byte string.</param>
    /// <returns><see langword="true" /> when the byte strings are equal.</returns>
    public static bool operator ==(ByteString left, ByteString right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Compares two byte strings for byte-for-byte inequality.
    /// </summary>
    /// <param name="left">The left byte string.</param>
    /// <param name="right">The right byte string.</param>
    /// <returns><see langword="true" /> when the byte strings are different.</returns>
    public static bool operator !=(ByteString left, ByteString right)
    {
        return !left.Equals(right);
    }
}
