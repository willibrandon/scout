
namespace Scout;

/// <summary>
/// Reusable reverse byte-substring finder for one fixed needle.
/// </summary>
public sealed class MemmemReverseFinder
{
    private readonly byte[] needle;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemmemReverseFinder" /> class.
    /// </summary>
    /// <param name="needle">The byte sequence to search for.</param>
    public MemmemReverseFinder(ReadOnlySpan<byte> needle)
    {
        this.needle = needle.ToArray();
    }

    /// <summary>
    /// Gets the needle bytes searched by this finder.
    /// </summary>
    public ReadOnlyMemory<byte> Needle => needle;

    /// <summary>
    /// Finds the last occurrence of this finder's needle.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <returns>The zero-based index, or <c>-1</c> when the sequence is absent.</returns>
    public int FindReverse(ReadOnlySpan<byte> haystack)
    {
        return MemmemSearch.FindReverse(haystack, needle);
    }

    /// <summary>
    /// Finds every non-overlapping occurrence of this finder's needle in reverse order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <returns>The zero-based start indexes of every occurrence in descending order.</returns>
    public IReadOnlyList<int> FindAllReverse(ReadOnlySpan<byte> haystack)
    {
        return MemmemSearch.FindAllReverse(haystack, needle);
    }

    /// <summary>
    /// Enumerates every non-overlapping occurrence of this finder's needle in reverse order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <returns>An allocation-free byte sequence start offset enumerator.</returns>
    public MemmemReverseEnumerator EnumerateReverse(ReadOnlySpan<byte> haystack)
    {
        return MemmemSearch.EnumerateReverse(haystack, needle);
    }
}
