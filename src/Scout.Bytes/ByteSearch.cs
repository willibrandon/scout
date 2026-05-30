using System;

namespace Scout;

/// <summary>
/// Provides span-based byte search helpers.
/// </summary>
public static class ByteSearch
{
    /// <summary>
    /// Finds the first occurrence of a byte in a byte span.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The byte to find.</param>
    /// <returns>The zero-based index, or <c>-1</c> when not found.</returns>
    public static int IndexOf(ReadOnlySpan<byte> haystack, byte needle)
    {
        return haystack.IndexOf(needle);
    }

    /// <summary>
    /// Finds the first occurrence of a byte sequence in a byte span.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The byte sequence to find.</param>
    /// <returns>The zero-based index, or <c>-1</c> when not found.</returns>
    public static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        return haystack.IndexOf(needle);
    }
}
