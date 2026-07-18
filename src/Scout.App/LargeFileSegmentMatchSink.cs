namespace Scout;

/// <summary>
/// Retains ordered matches reported for one large-file segment.
/// </summary>
/// <param name="matchPool">The pool that supplies reusable match buffers.</param>
internal struct LargeFileSegmentMatchSink(LargeFileSegmentMatchPool matchPool) : ILineSink
{
    private readonly LargeFileSegmentMatchPool _matchPool =
        matchPool ?? throw new ArgumentNullException(nameof(matchPool));
    private List<LargeFileSegmentMatch>? _matches;

    /// <summary>
    /// Gets the number of matched lines reported to the sink.
    /// </summary>
    public ulong MatchedLines { get; private set; }

    /// <summary>
    /// Gets the lazily rented match buffer, or <see langword="null" /> when no match was reported.
    /// </summary>
    public List<LargeFileSegmentMatch>? Matches => _matches;

    /// <summary>
    /// Retains one matched line in segment-relative coordinates.
    /// </summary>
    /// <param name="lineNumber">The one-based line number within the segment.</param>
    /// <param name="byteOffset">The zero-based line byte offset within the segment.</param>
    /// <param name="matchColumn">The one-based match byte column.</param>
    /// <param name="line">The matched line bytes.</param>
    public void MatchedLine(long lineNumber, long byteOffset, long matchColumn, ReadOnlySpan<byte> line)
    {
        MatchedLines++;
        _matches ??= _matchPool.Rent();
        _matches.Add(new LargeFileSegmentMatch(lineNumber, checked((int)byteOffset), line.Length, matchColumn));
    }

    /// <summary>
    /// Transfers ownership of the retained match buffer to the caller.
    /// </summary>
    /// <returns>The retained match buffer, or <see langword="null" /> when no match was reported.</returns>
    public List<LargeFileSegmentMatch>? DetachMatches()
    {
        List<LargeFileSegmentMatch>? matches = _matches;
        _matches = null;
        return matches;
    }

    /// <summary>
    /// Returns the retained match buffer to its pool, if one was rented.
    /// </summary>
    public void ReturnMatches()
    {
        List<LargeFileSegmentMatch>? matches = DetachMatches();
        if (matches is not null)
        {
            _matchPool.Return(matches);
        }
    }
}
