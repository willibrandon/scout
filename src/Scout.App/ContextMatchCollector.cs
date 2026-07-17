namespace Scout;

/// <summary>
/// Retains authoritative line selections and ordered match spans for context output.
/// </summary>
/// <param name="selectedLineStarts">The ordered byte offsets of selected lines.</param>
/// <param name="selectedLineMatchRanges">The retained-match ranges aligned with selected lines.</param>
/// <param name="matches">The flat ordered collection of retained matches.</param>
internal struct ContextMatchCollector(
    List<int> selectedLineStarts,
    List<ContextLineMatchRange> selectedLineMatchRanges,
    List<ContextLineMatch> matches) : IMatchLineSink
{
    private readonly List<int> _selectedLineStarts =
        selectedLineStarts ?? throw new ArgumentNullException(nameof(selectedLineStarts));
    private readonly List<ContextLineMatchRange> _selectedLineMatchRanges =
        selectedLineMatchRanges ?? throw new ArgumentNullException(nameof(selectedLineMatchRanges));
    private readonly List<ContextLineMatch> _matches =
        matches ?? throw new ArgumentNullException(nameof(matches));

    /// <summary>
    /// Retains one authoritative match and its containing-line association.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="lineByteOffset">The zero-based containing-line byte offset.</param>
    /// <param name="matchByteOffset">The zero-based match byte offset.</param>
    /// <param name="matchColumn">The one-based match byte column.</param>
    /// <param name="line">The containing line bytes.</param>
    /// <param name="match">The matching bytes.</param>
    public void MatchedLine(
        long lineNumber,
        long lineByteOffset,
        long matchByteOffset,
        long matchColumn,
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> match)
    {
        _ = lineNumber;
        _ = line;
        int checkedLineByteOffset = checked((int)lineByteOffset);
        if (_selectedLineStarts.Count == 0 ||
            _selectedLineStarts[^1] != checkedLineByteOffset)
        {
            _selectedLineStarts.Add(checkedLineByteOffset);
            _selectedLineMatchRanges.Add(new ContextLineMatchRange(
                _matches.Count,
                count: 0));
        }

        ContextLineMatchRange range = _selectedLineMatchRanges[^1];
        _matches.Add(new ContextLineMatch(
            checked((int)(matchByteOffset - lineByteOffset)),
            matchColumn,
            match.Length));
        _selectedLineMatchRanges[^1] = new ContextLineMatchRange(
            range.Start,
            range.Count + 1);
    }

    /// <summary>
    /// Retains a selection-only line when its authoritative match has no reportable span.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="lineByteOffset">The zero-based containing-line byte offset.</param>
    /// <param name="line">The containing line bytes.</param>
    public void FinishLine(
        long lineNumber,
        long lineByteOffset,
        ReadOnlySpan<byte> line)
    {
        _ = lineNumber;
        _ = line;
        int checkedLineByteOffset = checked((int)lineByteOffset);
        if (_selectedLineStarts.Count == 0 ||
            _selectedLineStarts[^1] != checkedLineByteOffset)
        {
            _selectedLineStarts.Add(checkedLineByteOffset);
            _selectedLineMatchRanges.Add(new ContextLineMatchRange(
                _matches.Count,
                count: 0));
        }
    }
}
