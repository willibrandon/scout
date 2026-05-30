using System;

namespace Scout;

/// <summary>
/// Receives matches from Scout's allocation-free internal matcher iterator.
/// </summary>
public interface IMatcherSink
{
    /// <summary>
    /// Receives one match.
    /// </summary>
    /// <param name="haystack">The haystack being searched.</param>
    /// <param name="match">The match span.</param>
    /// <returns><see langword="true" /> to continue iteration; otherwise, <see langword="false" />.</returns>
    bool Matched(ReadOnlySpan<byte> haystack, MatcherMatch match);
}
