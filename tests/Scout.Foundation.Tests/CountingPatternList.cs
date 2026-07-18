using System.Collections;

namespace Scout;

/// <summary>
/// Counts accesses made through an ordered raw-pattern list.
/// </summary>
/// <param name="patterns">The ordered patterns.</param>
internal sealed class CountingPatternList(byte[][] patterns) : IReadOnlyList<byte[]>
{
    private readonly byte[][] _patterns = patterns;

    /// <summary>
    /// Gets the number of list accesses observed since the last reset.
    /// </summary>
    public int AccessCount { get; private set; }

    /// <inheritdoc />
    public int Count
    {
        get
        {
            AccessCount++;
            return _patterns.Length;
        }
    }

    /// <inheritdoc />
    public byte[] this[int index]
    {
        get
        {
            AccessCount++;
            return _patterns[index];
        }
    }

    /// <summary>
    /// Resets the observed raw-pattern accesses.
    /// </summary>
    public void ResetAccessCount()
    {
        AccessCount = 0;
    }

    /// <inheritdoc />
    public IEnumerator<byte[]> GetEnumerator()
    {
        AccessCount++;
        return ((IEnumerable<byte[]>)_patterns).GetEnumerator();
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
