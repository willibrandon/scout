using System.Text;

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

    /// <summary>
    /// Verifies class-sequence regex acceleration reports line metadata for ASCII matches.
    /// </summary>
    [Fact]
    public void SearchUsesClassSequenceAcceleratorForAsciiWords()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["\\w{5}\\s+\\w{5}\\s+\\w{5}"u8.ToArray()];

        bool matched = LiteralLineSearcher.Search(
            "tiny\nabcde fghij klmno\nomega\n"u8,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal(5, sink.ByteOffset);
        Assert.Equal(1, sink.MatchColumn);
        Assert.Equal("abcde fghij klmno\n"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies class-sequence regex acceleration falls back to Unicode-aware matching when needed.
    /// </summary>
    [Fact]
    public void SearchUsesClassSequenceAcceleratorForUnicodeWords()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["\\w{5}\\s+\\w{5}\\s+\\w{5}"u8.ToArray()];
        byte[] haystack = Encoding.UTF8.GetBytes("tiny\nabcde caf\u00e9x klmno\nomega\n");

        bool matched = LiteralLineSearcher.Search(
            haystack,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal(5, sink.ByteOffset);
        Assert.Equal(1, sink.MatchColumn);
        Assert.Equal(Encoding.UTF8.GetBytes("abcde caf\u00e9x klmno\n"), sink.Line);
    }

    /// <summary>
    /// Verifies Unicode class-sequence backtracking stays on UTF-8 scalar boundaries.
    /// </summary>
    [Fact]
    public void SearchClassSequenceAcceleratorBacktracksUnicodeScalars()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["\\w+\\w\\s"u8.ToArray()];
        byte[] haystack = Encoding.UTF8.GetBytes("caf\u00e9 \n");

        bool matched = LiteralLineSearcher.Search(
            haystack,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(1, sink.LineNumber);
        Assert.Equal(1, sink.MatchColumn);
        Assert.Equal(haystack, sink.Line);
    }

    /// <summary>
    /// Verifies class-sequence regex acceleration honors max-count limiting.
    /// </summary>
    [Fact]
    public void SearchClassSequenceAcceleratorHonorsMaxMatchingLines()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["\\w{5}\\s+\\w{5}\\s+\\w{5}"u8.ToArray()];

        bool matched = LiteralLineSearcher.Search(
            "abcde fghij klmno\npqrst uvwxy zabcd\n"u8,
            patterns,
            ref sink,
            maxMatchingLines: 1);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(1, sink.LineNumber);
        Assert.Equal("abcde fghij klmno\n"u8.ToArray(), sink.Line.ToArray());
    }
}
