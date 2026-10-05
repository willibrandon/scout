
namespace Scout;

/// <summary>
/// Verifies streaming search-input transcoding.
/// </summary>
[TestClass]
public sealed class SearchEncodingReaderTests
{
    /// <summary>
    /// Verifies stream reads apply BOM sniffing and transcoding.
    /// </summary>
    [TestMethod]
    public void ReadToEndAppliesSearchEncoding()
    {
        using var stream = new SegmentedReadStream(
            [0xFF],
            [0xFE, (byte)'n'],
            [0, (byte)'e', 0, (byte)'e', 0],
            [(byte)'d', 0, (byte)'l', 0, (byte)'e', 0, (byte)'\n', 0]);

        byte[] bytes = SearchEncodingReader.ReadToEnd(stream, SearchEncodingKind.Auto);

        Assert.AreSequenceEqual("needle\n"u8.ToArray(), bytes);
    }

    /// <summary>
    /// Verifies raw stream reads preserve bytes after buffering.
    /// </summary>
    [TestMethod]
    public void ReadToEndPreservesRawBytes()
    {
        using var stream = new MemoryStream([0xEF, 0xBB, 0xBF, (byte)'n']);

        byte[] bytes = SearchEncodingReader.ReadToEnd(stream, SearchEncodingKind.None);

        Assert.AreSequenceEqual<byte>([0xEF, 0xBB, 0xBF, (byte)'n'], bytes);
    }

    /// <summary>
    /// Verifies null streams are rejected.
    /// </summary>
    [TestMethod]
    public void ReadToEndRejectsNullStream()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => SearchEncodingReader.ReadToEnd(null!, SearchEncodingKind.Auto));
    }

    /// <summary>
    /// Verifies null destination streams are rejected.
    /// </summary>
    [TestMethod]
    public void TranscodeToRejectsNullDestination()
    {
        using var stream = new MemoryStream();

        Assert.ThrowsExactly<ArgumentNullException>(() => SearchEncodingReader.TranscodeTo(stream, null!, SearchEncodingKind.Auto));
    }

    /// <summary>
    /// Verifies UTF-8 and UTF-16 sequences can span stream reads.
    /// </summary>
    [TestMethod]
    public void ReadToEndPreservesSplitUnicodeSequences()
    {
        Assert.AreSequenceEqual(
            "\uD83D\uDE00"u8.ToArray(),
            ReadSegmented(SearchEncodingKind.Utf8, [0xF0], [0x9F, 0x98], [0x80]));
        Assert.AreSequenceEqual(
            "\uD83D\uDE00"u8.ToArray(),
            ReadSegmented(SearchEncodingKind.Utf16Le, [0x3D], [0xD8, 0x00], [0xDE]));
    }

    /// <summary>
    /// Verifies multibyte legacy encodings can span stream reads.
    /// </summary>
    [TestMethod]
    public void ReadToEndPreservesSplitLegacySequences()
    {
        Assert.AreSequenceEqual(
            "\u4E2D\u6587"u8.ToArray(),
            ReadSegmented(SearchEncodingKind.Big5, [0xA4], [0xA4, 0xA4], [0xE5]));
        Assert.AreSequenceEqual(
            "\uD83D\uDE00"u8.ToArray(),
            ReadSegmented(SearchEncodingKind.Gb18030, [0x94, 0x39], [0xFC], [0x36]));
        Assert.AreSequenceEqual(
            "\uAC00"u8.ToArray(),
            ReadSegmented(SearchEncodingKind.EucKr, [0xB0], [0xA1]));
        Assert.AreSequenceEqual(
            "\u3042"u8.ToArray(),
            ReadSegmented(SearchEncodingKind.EucJp, [0xA4], [0xA2]));
        Assert.AreSequenceEqual(
            "\u3042"u8.ToArray(),
            ReadSegmented(SearchEncodingKind.ShiftJis, [0x82], [0xA0]));
    }

    /// <summary>
    /// Verifies split malformed GB18030 prefixes preserve WHATWG byte consumption.
    /// </summary>
    [TestMethod]
    public void ReadToEndPreservesSplitGb18030MalformedConsumption()
    {
        byte[] bytes = ReadSegmented(SearchEncodingKind.Gb18030, [0x81, 0x30], [(byte)'A']);

        Assert.AreSequenceEqual("\uFFFD0A"u8.ToArray(), bytes);
    }

    /// <summary>
    /// Verifies ISO-2022-JP decoder state survives stream read boundaries.
    /// </summary>
    [TestMethod]
    public void ReadToEndPreservesIso2022JpStateAcrossReads()
    {
        byte[] bytes = ReadSegmented(
            SearchEncodingKind.Iso2022Jp,
            [0x1B],
            [(byte)'$', (byte)'B', 0x46],
            [0x7C, 0x4B],
            [0x5C, 0x38, 0x6C]);

        Assert.AreSequenceEqual("\u65E5\u672C\u8A9E"u8.ToArray(), bytes);
    }

    /// <summary>
    /// Verifies BOM-like data after the initial sniff is decoded as content.
    /// </summary>
    [TestMethod]
    public void ReadToEndOnlySniffsBomAtStart()
    {
        byte[] bytes = ReadSegmented(
            SearchEncodingKind.Utf16Le,
            [(byte)'A', 0, 0xFF],
            [0xFE, (byte)'B', 0]);

        Assert.AreSequenceEqual("A\uFEFFB"u8.ToArray(), bytes);
    }

    private static byte[] ReadSegmented(SearchEncodingKind encodingKind, params byte[][] segments)
    {
        using var stream = new SegmentedReadStream(segments);
        return SearchEncodingReader.ReadToEnd(stream, encodingKind);
    }
}
