using System.Collections;

namespace Scout;

/// <summary>
/// Counts record-candidate checks made through an ordered pattern list.
/// </summary>
/// <param name="patterns">The ordered patterns.</param>
internal sealed class CountingPatternList(byte[][] patterns) : IReadOnlyList<byte[]>
{
    private readonly byte[][] _patterns = patterns;

    /// <summary>
    /// Gets the number of candidate checks observed since the last reset.
    /// </summary>
    public int CandidateChecks { get; private set; }

    /// <inheritdoc />
    public int Count
    {
        get
        {
            CandidateChecks++;
            return _patterns.Length;
        }
    }

    /// <inheritdoc />
    public byte[] this[int index] => _patterns[index];

    /// <summary>
    /// Resets the observed candidate-check count.
    /// </summary>
    public void ResetCandidateChecks()
    {
        CandidateChecks = 0;
    }

    /// <inheritdoc />
    public IEnumerator<byte[]> GetEnumerator()
    {
        return ((IEnumerable<byte[]>)_patterns).GetEnumerator();
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
