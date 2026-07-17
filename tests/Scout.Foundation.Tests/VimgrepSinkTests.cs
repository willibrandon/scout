namespace Scout;

/// <summary>
/// Verifies vimgrep output from authoritative match-line events.
/// </summary>
public sealed class VimgrepSinkTests
{
    /// <summary>
    /// Verifies short-line records stream as each match arrives.
    /// </summary>
    [Fact]
    public void StreamsShortLineRecords()
    {
        using MemoryStream output = new();
        VimgrepSink sink = CreateSink(
            output,
            lineNumber: true,
            column: true,
            byteOffset: true);
        ReadOnlySpan<byte> line = "zero one one\n"u8;

        sink.MatchedLine(3, 20, 25, 6, line, line.Slice(5, 3));
        Assert.NotEmpty(output.ToArray());
        sink.MatchedLine(3, 20, 29, 10, line, line.Slice(9, 3));
        sink.FinishLine(3, 20, line);

        Assert.Equal(
            "3:6:25:zero one one\n3:10:29:zero one one\n",
            System.Text.Encoding.UTF8.GetString(output.ToArray()));
    }

    /// <summary>
    /// Verifies long-line records use the count supplied by authoritative events.
    /// </summary>
    [Fact]
    public void DefersLongOmittedLineUntilItsMatchCountIsKnown()
    {
        using MemoryStream output = new();
        VimgrepSink sink = CreateSink(
            output,
            maxColumns: 4);
        ReadOnlySpan<byte> line = "aa bb cc\n"u8;

        sink.MatchedLine(1, 0, 0, 1, line, line.Slice(0, 2));
        sink.MatchedLine(1, 0, 3, 4, line, line.Slice(3, 2));
        sink.MatchedLine(1, 0, 6, 7, line, line.Slice(6, 2));
        Assert.Empty(output.ToArray());
        sink.FinishLine(1, 0, line);

        Assert.Equal(
            "[Omitted long line with 3 matches]\n" +
            "[Omitted long line with 3 matches]\n" +
            "[Omitted long line with 3 matches]\n",
            System.Text.Encoding.UTF8.GetString(output.ToArray()));
    }

    /// <summary>
    /// Verifies retained context matches preserve their adjusted line and byte offsets.
    /// </summary>
    [Fact]
    public void AppliesContextOffsetsToDeferredRecords()
    {
        using MemoryStream output = new();
        VimgrepSink sink = CreateSink(
            output,
            lineNumber: true,
            column: true,
            byteOffset: true,
            maxColumns: 3,
            lineNumberOffset: 6,
            byteOffsetOffset: 100);
        ReadOnlySpan<byte> line = "abc match\n"u8;

        sink.MatchedLine(1, 0, 4, 5, line, line.Slice(4, 5));
        sink.FinishLine(1, 0, line);

        Assert.Equal(
            "7:5:104:[Omitted long line with 1 matches]\n",
            System.Text.Encoding.UTF8.GetString(output.ToArray()));
    }

    /// <summary>
    /// Verifies only-matching output applies the line limit to each authoritative span.
    /// </summary>
    [Fact]
    public void AppliesLineLimitToOnlyMatchingSpans()
    {
        using MemoryStream output = new();
        VimgrepSink sink = CreateSink(
            output,
            onlyMatching: true,
            lineNumber: true,
            column: true,
            byteOffset: true,
            maxColumns: 3);
        ReadOnlySpan<byte> line = "zero match tail\n"u8;

        sink.MatchedLine(2, 30, 35, 6, line, line.Slice(5, 5));
        Assert.Empty(output.ToArray());
        sink.FinishLine(2, 30, line);

        Assert.Equal(
            "2:6:35:[Omitted long matching line]\n",
            System.Text.Encoding.UTF8.GetString(output.ToArray()));
    }

    private static VimgrepSink CreateSink(
        MemoryStream output,
        bool lineNumber = false,
        bool column = false,
        bool byteOffset = false,
        bool onlyMatching = false,
        bool trim = false,
        ulong? maxColumns = null,
        bool preview = false,
        long lineNumberOffset = 0,
        long byteOffsetOffset = 0)
    {
        return new VimgrepSink(
            new RawByteWriter(output),
            prefix: null,
            ":"u8.ToArray(),
            lineNumber,
            column,
            byteOffset,
            onlyMatching,
            trim,
            nullPathTerminator: false,
            new OutputLineLimit(maxColumns, preview),
            new OutputColor(enabled: false),
            "\n"u8.ToArray(),
            lineNumberOffset,
            byteOffsetOffset);
    }
}
