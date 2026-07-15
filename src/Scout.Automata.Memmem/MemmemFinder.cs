
namespace Scout;

/// <summary>
/// Reusable forward byte-substring finder for one fixed needle.
/// </summary>
public sealed class MemmemFinder
{
    private readonly byte[] _needle;
    private readonly MemmemPackedPairFinder? _packedPairFinder;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemmemFinder" /> class.
    /// </summary>
    /// <param name="needle">The byte sequence to search for.</param>
    public MemmemFinder(ReadOnlySpan<byte> needle)
    {
        _needle = needle.ToArray();
        _packedPairFinder = MemmemPackedPairFinder.TryCreate(_needle, out MemmemPackedPairFinder finder)
            ? finder
            : null;
    }

    /// <summary>
    /// Gets the needle bytes searched by this finder.
    /// </summary>
    public ReadOnlyMemory<byte> Needle => _needle;

    /// <summary>
    /// Finds the first occurrence of this finder's needle.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <returns>The zero-based index, or <c>-1</c> when the sequence is absent.</returns>
    public int Find(ReadOnlySpan<byte> haystack)
    {
        return _packedPairFinder is MemmemPackedPairFinder finder
            ? finder.Find(haystack, _needle)
            : MemmemSearch.FindWithoutPackedPair(haystack, _needle);
    }

    /// <summary>
    /// Finds the first needle occurrence while detecting NUL bytes in the inspected prefix.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="containsNul">Set to <see langword="true" /> when an inspected byte is NUL.</param>
    /// <returns>The zero-based index, or <c>-1</c> when the sequence is absent.</returns>
    internal int FindAndDetectNul(ReadOnlySpan<byte> haystack, ref bool containsNul)
    {
        if (_needle.Length == 0)
        {
            containsNul |= haystack.Contains((byte)0);
            return 0;
        }

        if (_needle.Length > haystack.Length)
        {
            containsNul |= haystack.Contains((byte)0);
            return -1;
        }

        if (_needle.Length == 1)
        {
            return FindSingleByteAndDetectNul(haystack, _needle[0], ref containsNul);
        }

        return _packedPairFinder is MemmemPackedPairFinder finder
            ? finder.FindAndDetectNul(haystack, _needle, ref containsNul)
            : FindShortNeedleAndDetectNul(haystack, _needle, ref containsNul);
    }

    /// <summary>
    /// Finds every non-overlapping occurrence of this finder's needle.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <returns>The zero-based start indexes of every occurrence.</returns>
    public IReadOnlyList<int> FindAll(ReadOnlySpan<byte> haystack)
    {
        return MemmemSearch.FindAll(haystack, _needle);
    }

    /// <summary>
    /// Enumerates every non-overlapping occurrence of this finder's needle.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <returns>An allocation-free byte sequence start offset enumerator.</returns>
    public MemmemEnumerator Enumerate(ReadOnlySpan<byte> haystack)
    {
        return MemmemSearch.Enumerate(haystack, _needle);
    }

    private static int FindSingleByteAndDetectNul(
        ReadOnlySpan<byte> haystack,
        byte needle,
        ref bool containsNul)
    {
        int offset = 0;
        while (offset < haystack.Length)
        {
            int relative = haystack[offset..].IndexOfAny(needle, (byte)0);
            if (relative < 0)
            {
                return -1;
            }

            int found = offset + relative;
            if (haystack[found] == 0)
            {
                containsNul = true;
            }

            if (haystack[found] == needle)
            {
                return found;
            }

            offset = found + 1;
        }

        return -1;
    }

    private static int FindShortNeedleAndDetectNul(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle,
        ref bool containsNul)
    {
        int offset = 0;
        int lastStart = haystack.Length - needle.Length;
        while (offset < haystack.Length)
        {
            int relative = haystack[offset..].IndexOfAny(needle[0], (byte)0);
            if (relative < 0)
            {
                return -1;
            }

            int candidate = offset + relative;
            if (haystack[candidate] == 0)
            {
                containsNul = true;
            }

            if (candidate <= lastStart &&
                haystack[candidate] == needle[0] &&
                haystack.Slice(candidate, needle.Length).SequenceEqual(needle))
            {
                return candidate;
            }

            offset = candidate + 1;
        }

        return -1;
    }
}
