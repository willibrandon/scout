using System;

namespace Scout;

/// <summary>
/// Enumerates the byte offsets of one byte in a haystack.
/// </summary>
public ref struct MemchrEnumerator
{
    private readonly ReadOnlySpan<byte> haystack;
    private readonly byte needle;
    private readonly bool reverse;
    private int cursor;
    private int current;
    private bool hasCurrent;

    internal MemchrEnumerator(ReadOnlySpan<byte> haystack, byte needle, bool reverse)
    {
        this.haystack = haystack;
        this.needle = needle;
        this.reverse = reverse;
        cursor = reverse ? haystack.Length : 0;
        current = -1;
        hasCurrent = false;
    }

    /// <summary>
    /// Gets the current matched byte offset.
    /// </summary>
    public int Current => hasCurrent ? current : throw new InvalidOperationException("enumeration has no current value");

    /// <summary>
    /// Advances to the next matched byte offset.
    /// </summary>
    /// <returns><see langword="true" /> when another match was found.</returns>
    public bool MoveNext()
    {
        int found = reverse
            ? FindReverse()
            : FindForward();
        if (found < 0)
        {
            hasCurrent = false;
            return false;
        }

        current = found;
        cursor = reverse ? found : found + 1;
        hasCurrent = true;
        return true;
    }

    private int FindForward()
    {
        if (cursor >= haystack.Length)
        {
            return -1;
        }

        int found = MemchrSearch.Find(haystack[cursor..], needle);
        return found < 0 ? -1 : cursor + found;
    }

    private int FindReverse()
    {
        return cursor <= 0 ? -1 : MemchrSearch.FindReverse(haystack[..cursor], needle);
    }
}
