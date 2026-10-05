namespace Scout;

/// <summary>
/// Verifies bounded reuse of large-file segment match buffers.
/// </summary>
[TestClass]
public sealed class LargeFileSegmentMatchPoolTests
{
    /// <summary>
    /// Verifies that returned buffers are cleared before they are reused.
    /// </summary>
    [TestMethod]
    public void ReturnClearsAndReusesMatchBuffer()
    {
        var pool = new LargeFileSegmentMatchPool(maximumRetainedCount: 1);
        List<LargeFileSegmentMatch> first = pool.Rent();
        first.Add(new LargeFileSegmentMatch(7, 11, 13, 17));

        pool.Return(first);
        List<LargeFileSegmentMatch> second = pool.Rent();

        Assert.AreSame(first, second);
        Assert.IsEmpty(second);
    }

    /// <summary>
    /// Verifies that the pool does not retain more buffers than its configured bound.
    /// </summary>
    [TestMethod]
    public void ReturnRetainsAtMostConfiguredBufferCount()
    {
        var pool = new LargeFileSegmentMatchPool(maximumRetainedCount: 1);
        List<LargeFileSegmentMatch> first = pool.Rent();
        List<LargeFileSegmentMatch> second = pool.Rent();

        pool.Return(first);
        pool.Return(second);

        List<LargeFileSegmentMatch> retained = pool.Rent();
        List<LargeFileSegmentMatch> replacement = pool.Rent();
        Assert.AreSame(first, retained);
        Assert.AreNotSame(first, replacement);
        Assert.AreNotSame(second, replacement);
    }
}
