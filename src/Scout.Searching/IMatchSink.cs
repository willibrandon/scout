using System;

namespace Scout;

/// <summary>
/// Receives individual matches from a searcher.
/// </summary>
public interface IMatchSink
{
    /// <summary>
    /// Receives one match.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="byteOffset">The zero-based byte offset of the match within the searched bytes.</param>
    /// <param name="matchColumn">The one-based byte column of the match.</param>
    /// <param name="match">The matching bytes.</param>
    void Matched(long lineNumber, long byteOffset, long matchColumn, ReadOnlySpan<byte> match);
}
