namespace Scout;

/// <summary>
/// Summarizes authoritative matches reported for one streamed line.
/// </summary>
internal struct LargeFileLineMatchSummarySink : IMatchLineSink
{
    /// <summary>
    /// Gets the one-based column of the first reported match.
    /// </summary>
    public long FirstMatchColumn { get; private set; }

    /// <summary>
    /// Gets the number of reported matches.
    /// </summary>
    public long MatchCount { get; private set; }

    /// <summary>
    /// Records one authoritative match.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="lineByteOffset">The zero-based byte offset of the line.</param>
    /// <param name="matchByteOffset">The zero-based byte offset of the match.</param>
    /// <param name="matchColumn">The one-based byte column of the match.</param>
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
        _ = lineByteOffset;
        _ = matchByteOffset;
        _ = line;
        _ = match;
        if (MatchCount == 0)
        {
            FirstMatchColumn = matchColumn;
        }

        MatchCount++;
    }
}
