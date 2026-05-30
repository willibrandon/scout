using System;

namespace Scout;

/// <summary>
/// Enumerates the start offsets of one byte sequence in a haystack in reverse order.
/// </summary>
public ref struct MemmemReverseEnumerator
{
    private readonly ReadOnlySpan<byte> haystack;
    private readonly ReadOnlySpan<byte> needle;
    private int cursor;
    private int current;
    private bool hasCurrent;

    internal MemmemReverseEnumerator(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        this.haystack = haystack;
        this.needle = needle;
        cursor = haystack.Length;
        current = -1;
        hasCurrent = false;
    }

    /// <summary>
    /// Gets the current matched byte sequence start offset.
    /// </summary>
    public int Current => hasCurrent ? current : throw new InvalidOperationException("enumeration has no current value");

    /// <summary>
    /// Advances to the next matched byte sequence start offset.
    /// </summary>
    /// <returns><see langword="true" /> when another match was found.</returns>
    public bool MoveNext()
    {
        int found = needle.IsEmpty
            ? FindEmpty()
            : FindReverse();
        if (found < 0)
        {
            hasCurrent = false;
            return false;
        }

        current = found;
        cursor = needle.IsEmpty ? found - 1 : found;
        hasCurrent = true;
        return true;
    }

    private int FindEmpty()
    {
        return cursor < 0 ? -1 : cursor;
    }

    private int FindReverse()
    {
        return cursor < needle.Length ? -1 : MemmemSearch.FindReverse(haystack[..cursor], needle);
    }
}
