namespace Scout;

/// <summary>
/// Accumulates statistics from one standard-search matcher traversal.
/// </summary>
internal sealed class StandardSearchMetrics()
{
    /// <summary>
    /// Gets the number of authoritative matcher traversals.
    /// </summary>
    public int AuthoritativeTraversalCount { get; private set; }

    /// <summary>
    /// Gets the number of selected lines.
    /// </summary>
    public ulong MatchedLines { get; private set; }

    /// <summary>
    /// Gets the number of exact non-overlapping matches.
    /// </summary>
    public ulong Matches { get; private set; }

    /// <summary>
    /// Gets the number of input bytes searched.
    /// </summary>
    public ulong BytesSearched { get; private set; }

    /// <summary>
    /// Records the beginning of one authoritative matcher traversal.
    /// </summary>
    public void BeginTraversal()
    {
        AuthoritativeTraversalCount++;
    }

    /// <summary>
    /// Verifies that the completed operation entered exactly one authoritative traversal.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The operation entered zero or multiple authoritative traversals.
    /// </exception>
    public void ValidateCompletedTraversal()
    {
        if (AuthoritativeTraversalCount != 1)
        {
            throw new InvalidOperationException(
                $"Expected one authoritative search traversal but observed {AuthoritativeTraversalCount}.");
        }
    }

    /// <summary>
    /// Records the statistics produced by a traversal.
    /// </summary>
    /// <param name="matchedLines">The selected-line count.</param>
    /// <param name="matches">The exact non-overlapping match count.</param>
    /// <param name="bytesSearched">The searched-byte count.</param>
    public void Record(ulong matchedLines, ulong matches, ulong bytesSearched)
    {
        MatchedLines += matchedLines;
        Matches += matches;
        BytesSearched += bytesSearched;
    }
}
