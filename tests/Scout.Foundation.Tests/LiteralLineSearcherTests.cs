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
    /// Verifies the literal regex path keeps exact line metadata after skipping many nonmatching lines.
    /// </summary>
    [Fact]
    public void SearchUsesLiteralRegexFastPathCountsSkippedLines()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["needle"u8.ToArray()];

        bool matched = LiteralLineSearcher.Search(
            "alpha\nbeta\ngamma\nneedle here\n"u8,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(4, sink.LineNumber);
        Assert.Equal(17, sink.ByteOffset);
        Assert.Equal(1, sink.MatchColumn);
        Assert.Equal("needle here\n"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies the literal regex path keeps line metadata with NUL-terminated records.
    /// </summary>
    [Fact]
    public void SearchUsesLiteralRegexFastPathCountsSkippedNullDataLines()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["needle"u8.ToArray()];

        bool matched = LiteralLineSearcher.Search(
            "alpha\0beta\0needle here\0"u8,
            patterns,
            ref sink,
            nullData: true);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(3, sink.LineNumber);
        Assert.Equal(11, sink.ByteOffset);
        Assert.Equal(1, sink.MatchColumn);
        Assert.Equal("needle here\0"u8.ToArray(), sink.Line.ToArray());
    }

    /// <summary>
    /// Verifies multi-pattern match-line search reports literal regex match offsets late in a line.
    /// </summary>
    [Fact]
    public void SearchMatchLinesReportsLiteralRegexOffsets()
    {
        var sink = new CapturingMatchLineSink();
        AssertMatchLineReportsLiteralRegexOffset(["class"u8.ToArray()], ref sink);
    }

    /// <summary>
    /// Verifies multi-pattern match-line search reports global match offsets across multiple lines.
    /// </summary>
    [Fact]
    public void SearchMatchLinesReportsLiteralRegexOffsetsAcrossLines()
    {
        var sink = new CapturingMatchLineSink();

        bool matched = LiteralLineSearcher.SearchMatchLines(
            "public sealed class Pcre2Regex\n    /// Initializes a new instance of the <see cref=\"Pcre2Regex\" /> class.\n"u8,
            ["class"u8.ToArray()],
            ref sink);

        Assert.True(matched);
        Assert.Equal(2UL, sink.Matches);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal(31, sink.LineByteOffset);
        Assert.Equal(99, sink.MatchByteOffset);
        Assert.Equal(69, sink.MatchColumn);
        Assert.Equal("class"u8.ToArray(), sink.Match.ToArray());
    }

    /// <summary>
    /// Verifies scoped group match-line search reports inner match offsets.
    /// </summary>
    [Fact]
    public void SearchMatchLinesReportsScopedRegexInnerOffsets()
    {
        var sink = new CapturingMatchLineSink();

        AssertMatchLineReportsLiteralRegexOffset(["(?:class)"u8.ToArray()], ref sink);
        sink = new CapturingMatchLineSink();
        AssertMatchLineReportsLiteralRegexOffset(["(?-u:class)"u8.ToArray()], ref sink);
    }

    private static void AssertMatchLineReportsLiteralRegexOffset(byte[][] patterns, ref CapturingMatchLineSink sink)
    {
        bool matched = LiteralLineSearcher.SearchMatchLines(
            "    /// Initializes a new instance of the <see cref=\"Pcre2Regex\" /> class.\n"u8,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(1UL, sink.Matches);
        Assert.Equal(1, sink.LineNumber);
        Assert.Equal(68, sink.MatchByteOffset);
        Assert.Equal(69, sink.MatchColumn);
        Assert.Equal("class"u8.ToArray(), sink.Match.ToArray());
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
    /// Verifies class-sequence regex acceleration recognizes Unicode whitespace separators.
    /// </summary>
    [Fact]
    public void SearchUsesClassSequenceAcceleratorForUnicodeWhitespace()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["\\w{5}\\s+\\w{5}\\s+\\w{5}"u8.ToArray()];
        byte[] haystack = Encoding.UTF8.GetBytes("tiny\nabcde\u00a0fghij klmno\nomega\n");

        bool matched = LiteralLineSearcher.Search(
            haystack,
            patterns,
            ref sink);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal(5, sink.ByteOffset);
        Assert.Equal(1, sink.MatchColumn);
        Assert.Equal(Encoding.UTF8.GetBytes("abcde\u00a0fghij klmno\n"), sink.Line);
    }

    /// <summary>
    /// Verifies class-sequence regex acceleration can skip earliest-column Unicode work when only line selection is needed.
    /// </summary>
    [Fact]
    public void SearchClassSequenceAcceleratorCanSkipUnicodeColumnFallback()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["\\w{5}\\s+\\w{5}\\s+\\w{5}"u8.ToArray()];
        byte[] haystack = Encoding.UTF8.GetBytes("\u00e9\u00e9\u00e9\u00e9\u00e9 fghij klmno abcde fghij klmno\n");

        bool matched = LiteralLineSearcher.Search(
            haystack,
            patterns,
            ref sink,
            requireMatchColumn: false);

        Assert.True(matched);
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.True(sink.MatchColumn > 1);
        Assert.Equal(haystack, sink.Line);
    }

    /// <summary>
    /// Verifies no-column class-sequence acceleration still checks earlier Unicode-only matching lines.
    /// </summary>
    [Fact]
    public void SearchClassSequenceAcceleratorChecksEarlierUnicodeLineWhenColumnSkipped()
    {
        var sink = new CapturingLineSink();
        byte[][] patterns = ["\\w{5}\\s+\\w{5}\\s+\\w{5}"u8.ToArray()];
        byte[] haystack = Encoding.UTF8.GetBytes("\u00e9\u00e9\u00e9\u00e9\u00e9 fghij klmno\nabcde fghij klmno\n");

        bool matched = LiteralLineSearcher.Search(
            haystack,
            patterns,
            ref sink,
            requireMatchColumn: false);

        Assert.True(matched);
        Assert.Equal(2UL, sink.MatchedLines);
        Assert.Equal(2, sink.LineNumber);
        Assert.Equal(Encoding.UTF8.GetBytes("abcde fghij klmno\n"), sink.Line);
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
