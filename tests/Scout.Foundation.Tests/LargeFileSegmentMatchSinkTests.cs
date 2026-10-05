namespace Scout;

/// <summary>
/// Verifies pooled retention of large-file segment matches.
/// </summary>
[TestClass]
public sealed class LargeFileSegmentMatchSinkTests
{
    /// <summary>
    /// Verifies that the sink rents lazily and returns an empty reusable buffer.
    /// </summary>
    [TestMethod]
    public void MatchedLineLazilyRentsAndReturnsMatchBuffer()
    {
        var pool = new LargeFileSegmentMatchPool(maximumRetainedCount: 1);
        var sink = new LargeFileSegmentMatchSink(pool);
        Assert.IsNull(sink.Matches);

        sink.MatchedLine(7, 11, 17, "matching line\n"u8);

        List<LargeFileSegmentMatch> matches = Assert.IsExactInstanceOfType<List<LargeFileSegmentMatch>>(sink.Matches);
        LargeFileSegmentMatch match = Assert.ContainsSingle(matches);
        Assert.AreEqual(1UL, sink.MatchedLines);
        Assert.AreEqual(7, match.LineNumber);
        Assert.AreEqual(11, match.LineStart);
        Assert.AreEqual(14, match.LineLength);
        Assert.AreEqual(17, match.MatchColumn);

        sink.ReturnMatches();
        sink.ReturnMatches();
        Assert.IsNull(sink.Matches);

        var next = new LargeFileSegmentMatchSink(pool);
        next.MatchedLine(19, 23, 29, "next\n"u8);
        Assert.AreSame(matches, next.Matches);
        Assert.ContainsSingle(next.Matches!);
    }

    /// <summary>
    /// Verifies that detaching a match buffer transfers ownership away from the sink.
    /// </summary>
    [TestMethod]
    public void DetachMatchesTransfersMatchBufferOwnership()
    {
        var pool = new LargeFileSegmentMatchPool(maximumRetainedCount: 1);
        var sink = new LargeFileSegmentMatchSink(pool);
        sink.MatchedLine(3, 5, 7, "match\n"u8);
        List<LargeFileSegmentMatch> matches = Assert.IsExactInstanceOfType<List<LargeFileSegmentMatch>>(sink.Matches);

        List<LargeFileSegmentMatch> detached = Assert.IsExactInstanceOfType<List<LargeFileSegmentMatch>>(sink.DetachMatches());
        sink.ReturnMatches();

        Assert.AreSame(matches, detached);
        Assert.IsNull(sink.Matches);
        pool.Return(detached);
        Assert.AreSame(detached, pool.Rent());
    }
}
