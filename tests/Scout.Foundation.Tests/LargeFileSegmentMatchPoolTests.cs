namespace Scout;

/// <summary>
/// Verifies bounded reuse of large-file segment match buffers.
/// </summary>
public sealed class LargeFileSegmentMatchPoolTests
{
    /// <summary>
    /// Verifies that returned buffers are cleared before they are reused.
    /// </summary>
    [Fact]
    public void ReturnClearsAndReusesMatchBuffer()
    {
        var pool = new LargeFileSegmentMatchPool(maximumRetainedCount: 1);
        List<LargeFileSegmentMatch> first = pool.Rent();
        first.Add(new LargeFileSegmentMatch(7, 11, 13, 17));

        pool.Return(first);
        List<LargeFileSegmentMatch> second = pool.Rent();

        Assert.Same(first, second);
        Assert.Empty(second);
    }

    /// <summary>
    /// Verifies that the pool does not retain more buffers than its configured bound.
    /// </summary>
    [Fact]
    public void ReturnRetainsAtMostConfiguredBufferCount()
    {
        var pool = new LargeFileSegmentMatchPool(maximumRetainedCount: 1);
        List<LargeFileSegmentMatch> first = pool.Rent();
        List<LargeFileSegmentMatch> second = pool.Rent();

        pool.Return(first);
        pool.Return(second);

        List<LargeFileSegmentMatch> retained = pool.Rent();
        List<LargeFileSegmentMatch> replacement = pool.Rent();
        Assert.Same(first, retained);
        Assert.NotSame(first, replacement);
        Assert.NotSame(second, replacement);
    }
}
