namespace Scout;

/// <summary>
/// Verifies pooled retention of large-file segment matches.
/// </summary>
public sealed class LargeFileSegmentMatchSinkTests
{
    /// <summary>
    /// Verifies that the sink rents lazily and returns an empty reusable buffer.
    /// </summary>
    [Fact]
    public void MatchedLineLazilyRentsAndReturnsMatchBuffer()
    {
        var pool = new LargeFileSegmentMatchPool(maximumRetainedCount: 1);
        var sink = new LargeFileSegmentMatchSink(pool);
        Assert.Null(sink.Matches);

        sink.MatchedLine(7, 11, 17, "matching line\n"u8);

        List<LargeFileSegmentMatch> matches = Assert.IsType<List<LargeFileSegmentMatch>>(sink.Matches);
        LargeFileSegmentMatch match = Assert.Single(matches);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(7, match.LineNumber);
        Assert.Equal(11, match.LineStart);
        Assert.Equal(14, match.LineLength);
        Assert.Equal(17, match.MatchColumn);

        sink.ReturnMatches();
        sink.ReturnMatches();
        Assert.Null(sink.Matches);

        var next = new LargeFileSegmentMatchSink(pool);
        next.MatchedLine(19, 23, 29, "next\n"u8);
        Assert.Same(matches, next.Matches);
        Assert.Single(next.Matches!);
    }

    /// <summary>
    /// Verifies that detaching a match buffer transfers ownership away from the sink.
    /// </summary>
    [Fact]
    public void DetachMatchesTransfersMatchBufferOwnership()
    {
        var pool = new LargeFileSegmentMatchPool(maximumRetainedCount: 1);
        var sink = new LargeFileSegmentMatchSink(pool);
        sink.MatchedLine(3, 5, 7, "match\n"u8);
        List<LargeFileSegmentMatch> matches = Assert.IsType<List<LargeFileSegmentMatch>>(sink.Matches);

        List<LargeFileSegmentMatch> detached = Assert.IsType<List<LargeFileSegmentMatch>>(sink.DetachMatches());
        sink.ReturnMatches();

        Assert.Same(matches, detached);
        Assert.Null(sink.Matches);
        pool.Return(detached);
        Assert.Same(detached, pool.Rent());
    }
}
