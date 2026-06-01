namespace Scout;

/// <summary>
/// Verifies literal line search behavior.
/// </summary>
public sealed class LiteralLineSearcherTests
{
    /// <summary>
    /// Verifies plain regex patterns use the literal search path through the multi-pattern API.
    /// </summary>
    [Fact]
    public void SearchUsesLiteralRegexFastPathForPlainPatterns()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["Sherlock Holmes"u8.ToArray()];

        bool matched = LiteralLineSearcher.Search(
            "alpha\nSherlock Holmes\nomega\n"u8,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal("Sherlock Holmes\n"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies the literal regex path reports each matching line once.
    /// </summary>
    [Fact]
    public void SearchUsesLiteralRegexFastPathOncePerLine()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["needle"u8.ToArray()];

        bool matched = LiteralLineSearcher.Search(
            "needle needle\nhay\nneedle\n"u8,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(2UL, sink.MatchedLines);
        Assert.Equal(3, sink.LineNumber);
        Assert.Equal("needle\n"u8.ToArray(), sink.Line.ToArray());
    }
}
