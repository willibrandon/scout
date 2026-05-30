using System;

namespace Scout;

/// <summary>
/// Receives individual matches together with their containing line.
/// </summary>
public interface IMatchLineSink
{
    /// <summary>
    /// Receives one match and the line that contains it.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="lineByteOffset">The zero-based byte offset of the line within the searched bytes.</param>
    /// <param name="matchByteOffset">The zero-based byte offset of the match within the searched bytes.</param>
    /// <param name="matchColumn">The one-based byte column of the match.</param>
    /// <param name="line">The containing line bytes, including a trailing line-feed byte when present.</param>
    /// <param name="match">The matching bytes.</param>
    void MatchedLine(
        long lineNumber,
        long lineByteOffset,
        long matchByteOffset,
        long matchColumn,
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> match);
}
