
namespace Scout;

/// <summary>
/// Reusable forward byte-substring finder for one fixed needle.
/// </summary>
public sealed class MemmemFinder
{
    private readonly byte[] needle;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemmemFinder" /> class.
    /// </summary>
    /// <param name="needle">The byte sequence to search for.</param>
    public MemmemFinder(ReadOnlySpan<byte> needle)
    {
        this.needle = needle.ToArray();
    }

    /// <summary>
    /// Gets the needle bytes searched by this finder.
    /// </summary>
    public ReadOnlyMemory<byte> Needle => needle;

    /// <summary>
    /// Finds the first occurrence of this finder's needle.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <returns>The zero-based index, or <c>-1</c> when the sequence is absent.</returns>
    public int Find(ReadOnlySpan<byte> haystack)
    {
        return MemmemSearch.Find(haystack, needle);
    }

    /// <summary>
    /// Finds every non-overlapping occurrence of this finder's needle.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <returns>The zero-based start indexes of every occurrence.</returns>
    public IReadOnlyList<int> FindAll(ReadOnlySpan<byte> haystack)
    {
        return MemmemSearch.FindAll(haystack, needle);
    }

    /// <summary>
    /// Enumerates every non-overlapping occurrence of this finder's needle.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <returns>An allocation-free byte sequence start offset enumerator.</returns>
    public MemmemEnumerator Enumerate(ReadOnlySpan<byte> haystack)
    {
        return MemmemSearch.Enumerate(haystack, needle);
    }
}
