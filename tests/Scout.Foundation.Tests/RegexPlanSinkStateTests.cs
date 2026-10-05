namespace Scout;

/// <summary>
/// Verifies search adapters retain mutations made by their wrapped value-type sinks.
/// </summary>
public sealed class RegexPlanSinkStateTests
{
    /// <summary>
    /// Verifies the counting line adapter retains both callbacks in its inner sink.
    /// </summary>
    [Fact]
    public void CountingLineSinkRetainsInnerState()
    {
        var sink = new RegexPlanCountingLineSink<CapturingLineSink>(default);
        sink.MatchedLine(1, 0, 1, "first\n"u8);
        sink.MatchedLine(2, 6, 2, "second\n"u8);

        Assert.Equal(2UL, sink.MatchedLines);
        Assert.Equal(2UL, sink.Inner.MatchedLines);
        Assert.Equal(2, sink.Inner.LineNumber);
        Assert.Equal("second\n"u8.ToArray(), sink.Inner.Line);
    }

    /// <summary>
    /// Verifies the counting match adapter preserves all inner match notifications.
    /// </summary>
    [Fact]
    public void CountingMatchSinkRetainsInnerState()
    {
        var sink = new RegexPlanCountingMatchLineSink<CapturingMatchLineSink>(default);
        sink.MatchedLine(1, 0, 0, 1, "aa\n"u8, "a"u8);
        sink.MatchedLine(1, 0, 1, 2, "aa\n"u8, "a"u8);

        Assert.Equal(2, sink.Matches);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(2UL, sink.Inner.Matches);
        Assert.Equal(1, sink.Inner.MatchByteOffset);
    }

    /// <summary>
    /// Verifies the line-output adapter preserves inner state while deduplicating matches per line.
    /// </summary>
    [Fact]
    public void LineOutputSinkRetainsInnerState()
    {
        var sink = new RegexPlanLineOutputMatchSink<CapturingLineSink>(default);
        sink.MatchedLine(1, 0, 0, 1, "aa\n"u8, "a"u8);
        sink.MatchedLine(1, 0, 1, 2, "aa\n"u8, "a"u8);
        sink.MatchedLine(2, 3, 3, 1, "a\n"u8, "a"u8);

        Assert.Equal(2UL, sink.MatchedLines);
        Assert.Equal(2UL, sink.Inner.MatchedLines);
        Assert.Equal(2, sink.Inner.LineNumber);
    }
}
