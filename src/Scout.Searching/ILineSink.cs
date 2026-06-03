
namespace Scout;

/// <summary>
/// Receives matching lines from a searcher.
/// </summary>
public interface ILineSink
{
    /// <summary>
    /// Receives one line that matched.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="byteOffset">The zero-based byte offset of the line within the searched bytes.</param>
    /// <param name="matchColumn">The one-based byte column of the first match, or zero when no text matched.</param>
    /// <param name="line">The matching line bytes, including a trailing line-feed byte when present.</param>
    void MatchedLine(long lineNumber, long byteOffset, long matchColumn, ReadOnlySpan<byte> line);
}
