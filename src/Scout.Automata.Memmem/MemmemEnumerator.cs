
namespace Scout;

/// <summary>
/// Enumerates the start offsets of one byte sequence in a haystack.
/// </summary>
public ref struct MemmemEnumerator
{
    private readonly ReadOnlySpan<byte> haystack;
    private readonly ReadOnlySpan<byte> needle;
    private int cursor;
    private int current;
    private bool hasCurrent;

    internal MemmemEnumerator(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        this.haystack = haystack;
        this.needle = needle;
        cursor = 0;
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
            : FindForward();
        if (found < 0)
        {
            hasCurrent = false;
            return false;
        }

        current = found;
        cursor = needle.IsEmpty ? found + 1 : found + needle.Length;
        hasCurrent = true;
        return true;
    }

    private int FindEmpty()
    {
        return cursor > haystack.Length ? -1 : cursor;
    }

    private int FindForward()
    {
        if (cursor > haystack.Length - needle.Length)
        {
            return -1;
        }

        int found = MemmemSearch.Find(haystack[cursor..], needle);
        return found < 0 ? -1 : cursor + found;
    }
}
