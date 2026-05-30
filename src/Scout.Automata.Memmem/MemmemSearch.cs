using System;
using System.Collections.Generic;

namespace Scout;

/// <summary>
/// Provides byte-substring search helpers corresponding to Rust's
/// <c>memchr::memmem</c> surface.
/// </summary>
public static class MemmemSearch
{
    /// <summary>
    /// Finds the first occurrence of a byte sequence.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The byte sequence to find.</param>
    /// <returns>The zero-based index, or <c>-1</c> when the sequence is absent.</returns>
    public static int Find(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.IsEmpty)
        {
            return 0;
        }

        return haystack.IndexOf(needle);
    }

    /// <summary>
    /// Finds every non-overlapping occurrence of a byte sequence in forward order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The byte sequence to find.</param>
    /// <returns>The zero-based start indexes of every occurrence.</returns>
    public static IReadOnlyList<int> FindAll(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        var matches = new List<int>();
        MemmemEnumerator enumerator = Enumerate(haystack, needle);
        while (enumerator.MoveNext())
        {
            matches.Add(enumerator.Current);
        }

        return matches;
    }

    /// <summary>
    /// Enumerates every non-overlapping occurrence of a byte sequence in forward order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The byte sequence to find.</param>
    /// <returns>An allocation-free byte sequence start offset enumerator.</returns>
    public static MemmemEnumerator Enumerate(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        return new MemmemEnumerator(haystack, needle);
    }

    /// <summary>
    /// Finds the last occurrence of a byte sequence.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The byte sequence to find.</param>
    /// <returns>The zero-based index, or <c>-1</c> when the sequence is absent.</returns>
    public static int FindReverse(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.IsEmpty)
        {
            return haystack.Length;
        }

        if (needle.Length > haystack.Length)
        {
            return -1;
        }

        int lastStart = haystack.Length - needle.Length;
        for (int index = lastStart; index >= 0; index--)
        {
            if (haystack.Slice(index, needle.Length).SequenceEqual(needle))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Finds every non-overlapping occurrence of a byte sequence in reverse order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The byte sequence to find.</param>
    /// <returns>The zero-based start indexes of every occurrence in descending order.</returns>
    public static IReadOnlyList<int> FindAllReverse(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        var matches = new List<int>();
        MemmemReverseEnumerator enumerator = EnumerateReverse(haystack, needle);
        while (enumerator.MoveNext())
        {
            matches.Add(enumerator.Current);
        }

        return matches;
    }

    /// <summary>
    /// Enumerates every non-overlapping occurrence of a byte sequence in reverse order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="needle">The byte sequence to find.</param>
    /// <returns>An allocation-free byte sequence start offset enumerator.</returns>
    public static MemmemReverseEnumerator EnumerateReverse(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        return new MemmemReverseEnumerator(haystack, needle);
    }
}
