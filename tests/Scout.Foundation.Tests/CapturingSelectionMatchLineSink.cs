namespace Scout;

/// <summary>
/// Captures selected-line and reportable-match counts from match-line traversal.
/// </summary>
internal struct CapturingSelectionMatchLineSink : IMatchLineSink
{
    private long _lastSelectedLineNumber;

    /// <summary>
    /// Gets the number of reportable matches received.
    /// </summary>
    public ulong Matches { get; private set; }

    /// <summary>
    /// Gets the number of distinct selected lines received.
    /// </summary>
    public ulong SelectedLines { get; private set; }

    /// <summary>
    /// Gets the line number of the most recent reportable match.
    /// </summary>
    public long LastMatchedLineNumber { get; private set; }

    /// <summary>
    /// Gets the most recent selected line number.
    /// </summary>
    public long LastSelectedLineNumber => _lastSelectedLineNumber;

    /// <summary>
    /// Captures one reportable match and its containing-line selection.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="lineByteOffset">The zero-based line byte offset.</param>
    /// <param name="matchByteOffset">The zero-based match byte offset.</param>
    /// <param name="matchColumn">The one-based match column.</param>
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
        _ = lineByteOffset;
        _ = matchByteOffset;
        _ = matchColumn;
        _ = line;
        _ = match;
        Matches++;
        LastMatchedLineNumber = lineNumber;
        SelectLine(lineNumber);
    }

    /// <summary>
    /// Captures a completed selected line.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="lineByteOffset">The zero-based line byte offset.</param>
    /// <param name="line">The selected line bytes.</param>
    public void FinishLine(
        long lineNumber,
        long lineByteOffset,
        ReadOnlySpan<byte> line)
    {
        _ = lineByteOffset;
        _ = line;
        SelectLine(lineNumber);
    }

    private void SelectLine(long lineNumber)
    {
        if (_lastSelectedLineNumber == lineNumber)
        {
            return;
        }

        _lastSelectedLineNumber = lineNumber;
        SelectedLines++;
    }
}
