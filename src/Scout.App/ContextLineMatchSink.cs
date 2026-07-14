namespace Scout;

/// <summary>
/// Collects lines selected by a context search.
/// </summary>
/// <param name="lines">The collection that receives selected-line state.</param>
internal readonly struct ContextLineMatchSink(List<ContextLineInfo> lines) : ILineSink
{
    private readonly List<ContextLineInfo> _lines = lines;

    /// <summary>
    /// Records one selected line and its first match column.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="byteOffset">The zero-based byte offset of the line.</param>
    /// <param name="matchColumn">The one-based byte column of the first match.</param>
    /// <param name="line">The containing line bytes.</param>
    public void MatchedLine(
        long lineNumber,
        long byteOffset,
        long matchColumn,
        ReadOnlySpan<byte> line)
    {
        _lines.Add(new ContextLineInfo(
            checked((int)byteOffset),
            line.Length,
            lineNumber,
            selectedMatch: true,
            matchColumn,
            originalMatch: true,
            matchColumn));
    }
}
