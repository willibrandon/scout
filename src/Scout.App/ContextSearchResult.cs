namespace Scout;

/// <summary>
/// Holds context line state and the authoritative spans retained for output replay.
/// </summary>
/// <param name="lines">The physical input lines and their selection state.</param>
/// <param name="lineMatchRanges">The retained-match ranges aligned with physical lines.</param>
/// <param name="matches">The flat ordered collection of retained matches.</param>
internal sealed class ContextSearchResult(
    List<ContextLineInfo> lines,
    ContextLineMatchRange[] lineMatchRanges,
    ContextLineMatch[] matches)
{
    private readonly ContextLineMatchRange[] _lineMatchRanges =
        lineMatchRanges ?? throw new ArgumentNullException(nameof(lineMatchRanges));
    private readonly ContextLineMatch[] _matches =
        matches ?? throw new ArgumentNullException(nameof(matches));

    /// <summary>
    /// Gets the physical input lines and their selection state.
    /// </summary>
    public List<ContextLineInfo> Lines { get; } =
        lines ?? throw new ArgumentNullException(nameof(lines));

    /// <summary>
    /// Gets the ordered authoritative spans retained for one line.
    /// </summary>
    /// <param name="line">The line whose spans should be returned.</param>
    /// <returns>The ordered spans for <paramref name="line" />, or an empty collection.</returns>
    public ReadOnlySpan<ContextLineMatch> GetMatches(ContextLineInfo line)
    {
        int lineIndex = checked((int)line.LineNumber - 1);
        if ((uint)lineIndex >= (uint)_lineMatchRanges.Length ||
            Lines[lineIndex].Start != line.Start)
        {
            return [];
        }

        ContextLineMatchRange range = _lineMatchRanges[lineIndex];
        return _matches.AsSpan(range.Start, range.Count);
    }

    /// <summary>
    /// Gets the ordered authoritative spans retained for one physical line.
    /// </summary>
    /// <param name="lineIndex">The zero-based physical line index.</param>
    /// <returns>The ordered spans for the requested line, or an empty collection.</returns>
    public ReadOnlySpan<ContextLineMatch> GetMatches(int lineIndex)
    {
        if ((uint)lineIndex >= (uint)_lineMatchRanges.Length)
        {
            return [];
        }

        ContextLineMatchRange range = _lineMatchRanges[lineIndex];
        return _matches.AsSpan(range.Start, range.Count);
    }
}
