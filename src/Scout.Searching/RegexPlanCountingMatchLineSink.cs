namespace Scout;

/// <summary>
/// Counts selected lines and reportable matches while forwarding line-match notifications.
/// </summary>
/// <typeparam name="TSink">The wrapped line-match sink type.</typeparam>
/// <param name="inner">The wrapped line-match sink.</param>
internal struct RegexPlanCountingMatchLineSink<TSink>(TSink inner) : IMatchLineSink
    where TSink : struct, IMatchLineSink
{
    private TSink _inner = inner;
    private long _lastMatchedLineNumber;

    /// <summary>
    /// Gets the wrapped line-match sink.
    /// </summary>
    public readonly TSink Inner => _inner;

    /// <summary>
    /// Gets the number of selected lines.
    /// </summary>
    public ulong MatchedLines { get; private set; }

    /// <summary>
    /// Gets the number of reportable matches.
    /// </summary>
    public long Matches { get; private set; }

    /// <summary>
    /// Gets the exclusive end of the last selected line.
    /// </summary>
    public ulong LastMatchedLineEnd { get; private set; }

    /// <summary>
    /// Forwards and counts one reportable match.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="lineByteOffset">The zero-based byte offset of the line.</param>
    /// <param name="matchByteOffset">The zero-based byte offset of the match.</param>
    /// <param name="matchColumn">The one-based byte column of the match.</param>
    /// <param name="line">The containing line.</param>
    /// <param name="match">The matching bytes.</param>
    public void MatchedLine(
        long lineNumber,
        long lineByteOffset,
        long matchByteOffset,
        long matchColumn,
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> match)
    {
        _inner.MatchedLine(lineNumber, lineByteOffset, matchByteOffset, matchColumn, line, match);
        Matches++;
        LastMatchedLineEnd = checked((ulong)(lineByteOffset + line.Length));
        CountLine(lineNumber);
    }

    /// <summary>
    /// Forwards notification that a selected line is complete.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="lineByteOffset">The zero-based byte offset of the line.</param>
    /// <param name="line">The completed line.</param>
    public void FinishLine(long lineNumber, long lineByteOffset, ReadOnlySpan<byte> line)
    {
        LastMatchedLineEnd = checked((ulong)(lineByteOffset + line.Length));
        CountLine(lineNumber);
        _inner.FinishLine(lineNumber, lineByteOffset, line);
    }

    private void CountLine(long lineNumber)
    {
        if (lineNumber == _lastMatchedLineNumber)
        {
            return;
        }

        MatchedLines++;
        _lastMatchedLineNumber = lineNumber;
    }
}
