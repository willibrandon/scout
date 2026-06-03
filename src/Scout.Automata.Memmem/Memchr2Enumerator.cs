
namespace Scout;

/// <summary>
/// Enumerates the byte offsets of either of two bytes in a haystack.
/// </summary>
public ref struct Memchr2Enumerator
{
    private readonly ReadOnlySpan<byte> haystack;
    private readonly byte first;
    private readonly byte second;
    private readonly bool reverse;
    private int cursor;
    private int current;
    private bool hasCurrent;

    internal Memchr2Enumerator(ReadOnlySpan<byte> haystack, byte first, byte second, bool reverse)
    {
        this.haystack = haystack;
        this.first = first;
        this.second = second;
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

        int found = MemchrSearch.Find2(haystack[cursor..], first, second);
        return found < 0 ? -1 : cursor + found;
    }

    private int FindReverse()
    {
        return cursor <= 0 ? -1 : MemchrSearch.Find2Reverse(haystack[..cursor], first, second);
    }
}
