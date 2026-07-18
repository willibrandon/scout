namespace Scout;

/// <summary>
/// Counts authoritative matches while forwarding each selected line once to a line sink.
/// </summary>
/// <typeparam name="TSink">The wrapped line sink type.</typeparam>
/// <param name="inner">The wrapped line sink.</param>
internal struct RegexPlanLineOutputMatchSink<TSink>(TSink inner) : IMatchLineSink
    where TSink : struct, ILineSink
{
    private TSink _inner = inner;
    private long _lastMatchedLineNumber;

    /// <summary>
    /// Gets the wrapped line sink.
    /// </summary>
    public readonly TSink Inner => _inner;

    /// <summary>
    /// Gets the number of selected lines.
    /// </summary>
    public ulong MatchedLines { get; private set; }

    /// <summary>
    /// Gets the number of exact non-overlapping matches.
    /// </summary>
    public ulong Matches { get; private set; }

    /// <summary>
    /// Gets the exclusive end of the last selected line.
    /// </summary>
    public ulong LastMatchedLineEnd { get; private set; }

    /// <summary>
    /// Counts one match and forwards its containing line when first observed.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="lineByteOffset">The zero-based line byte offset.</param>
    /// <param name="matchByteOffset">The zero-based match byte offset.</param>
    /// <param name="matchColumn">The one-based match column.</param>
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
        _ = matchByteOffset;
        _ = match;
        Matches++;
        LastMatchedLineEnd = checked((ulong)(lineByteOffset + line.Length));
        if (lineNumber == _lastMatchedLineNumber)
        {
            return;
        }

        _inner.MatchedLine(lineNumber, lineByteOffset, matchColumn, line);
        MatchedLines++;
        _lastMatchedLineNumber = lineNumber;
    }

    /// <summary>
    /// Completes a selected line that has already been forwarded.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="lineByteOffset">The zero-based line byte offset.</param>
    /// <param name="line">The selected line.</param>
    public void FinishLine(
        long lineNumber,
        long lineByteOffset,
        ReadOnlySpan<byte> line)
    {
        LastMatchedLineEnd = checked((ulong)(lineByteOffset + line.Length));
        if (lineNumber != _lastMatchedLineNumber)
        {
            _inner.MatchedLine(lineNumber, lineByteOffset, matchColumn: 0, line);
            MatchedLines++;
            _lastMatchedLineNumber = lineNumber;
        }
    }
}
