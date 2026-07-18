namespace Scout;

/// <summary>
/// Verifies replacement-line buffering behavior.
/// </summary>
public sealed class ReplacementLineSinkTests
{
    /// <summary>
    /// Verifies direct replacement streaming does not initialize buffered-line accumulators.
    /// </summary>
    [Fact]
    public void DirectStreamingDoesNotInitializeBufferedLineAccumulator()
    {
        using MemoryStream output = new();
        RawByteWriter writer = new(output);
        ReplacementLineSink sink = new(
            writer,
            prefix: null,
            fieldSeparator: ":"u8.ToArray(),
            replacement: "X"u8.ToArray(),
            lineNumber: false,
            column: false,
            byteOffset: false,
            trim: false,
            nullPathTerminator: false,
            vimgrep: false,
            lineLimit: default,
            lineTerminator: "\n"u8.ToArray(),
            streamPlainBodyDirectly: true);

        try
        {
            sink.MatchedLineWithSearchStart(
                lineNumber: 1,
                lineByteOffset: 0,
                matchByteOffset: 1,
                matchColumn: 2,
                line: "afoo\n"u8,
                match: "foo"u8,
                searchStart: 1);
            sink.FinishLine(1, 0, "afoo\n"u8);

            Assert.False(sink.IsAccumulatorInitialized);
            Assert.Equal("aX\n"u8.ToArray(), output.ToArray());
        }
        finally
        {
            sink.Dispose();
        }
    }
}
