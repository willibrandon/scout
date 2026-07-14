
namespace Scout;

/// <summary>
/// Counts lines selected by a search.
/// </summary>
internal struct CountingLineSink : ILineSink
{
    /// <summary>
    /// Gets the number of selected lines received by the sink.
    /// </summary>
    public ulong MatchedLines { get; private set; }

    /// <summary>
    /// Counts one selected line.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="byteOffset">The zero-based byte offset of the line.</param>
    /// <param name="matchColumn">The one-based byte column of the first match.</param>
    /// <param name="line">The selected line bytes.</param>
    public void MatchedLine(long lineNumber, long byteOffset, long matchColumn, ReadOnlySpan<byte> line)
    {
        MatchedLines++;
    }
}
