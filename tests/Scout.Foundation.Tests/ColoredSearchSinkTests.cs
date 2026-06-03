
namespace Scout;

/// <summary>
/// Verifies colored search output behavior.
/// </summary>
public sealed class ColoredSearchSinkTests
{
    /// <summary>
    /// Verifies match byte offsets are interpreted relative to their containing lines.
    /// </summary>
    [Fact]
    public void HighlightsMatchByteOffsetsAcrossLines()
    {
        using MemoryStream output = new();
        var writer = new RawByteWriter(output);
        var color = new OutputColor(enabled: true);
        var sink = new ColoredSearchSink(
            writer,
            prefix: null,
            ":"u8.ToArray(),
            lineNumber: true,
            column: false,
            byteOffset: false,
            trim: false,
            nullPathTerminator: false,
            new OutputLineLimit(null, preview: false),
            color,
            "\n"u8.ToArray());

        ReadOnlySpan<byte> first = "public sealed class Pcre2Regex\n"u8;
        ReadOnlySpan<byte> second = "    /// Initializes a new instance of the <see cref=\"Pcre2Regex\" /> class.\n"u8;

        sink.MatchedLine(1, 0, 14, 15, first, "class"u8);
        sink.MatchedLine(2, first.Length, first.Length + 68, 69, second, "class"u8);
        sink.Flush();

        Assert.Equal(
            "\u001b[0m\u001b[32m1\u001b[0m:public sealed \u001b[0m\u001b[1m\u001b[31mclass\u001b[0m Pcre2Regex\n" +
            "\u001b[0m\u001b[32m2\u001b[0m:    /// Initializes a new instance of the <see cref=\"Pcre2Regex\" /> \u001b[0m\u001b[1m\u001b[31mclass\u001b[0m.\n",
            System.Text.Encoding.UTF8.GetString(output.ToArray()));
    }
}
