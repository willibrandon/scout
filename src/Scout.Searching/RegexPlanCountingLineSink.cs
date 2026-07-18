namespace Scout;

/// <summary>
/// Counts selected lines while forwarding line notifications.
/// </summary>
/// <typeparam name="TSink">The wrapped line sink type.</typeparam>
/// <param name="inner">The wrapped line sink.</param>
internal struct RegexPlanCountingLineSink<TSink>(TSink inner) : ILineSink
    where TSink : struct, ILineSink
{
    private TSink _inner = inner;

    /// <summary>
    /// Gets the wrapped line sink.
    /// </summary>
    public readonly TSink Inner => _inner;

    /// <summary>
    /// Gets the number of selected lines.
    /// </summary>
    public ulong MatchedLines { get; private set; }

    /// <summary>
    /// Forwards and counts one selected line.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="byteOffset">The zero-based line byte offset.</param>
    /// <param name="matchColumn">The one-based match column.</param>
    /// <param name="line">The selected line.</param>
    public void MatchedLine(
        long lineNumber,
        long byteOffset,
        long matchColumn,
        ReadOnlySpan<byte> line)
    {
        _inner.MatchedLine(lineNumber, byteOffset, matchColumn, line);
        MatchedLines++;
    }
}
