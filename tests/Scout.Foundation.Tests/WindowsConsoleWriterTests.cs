using System.Text;

namespace Scout;

/// <summary>
/// Verifies UTF-8 to UTF-16 transcoding for Windows console output.
/// </summary>
[TestClass]
public sealed class WindowsConsoleWriterTests
{
    private const string NonUtf8Message = "Windows stdio in console mode does not support writing non-UTF-8 byte sequences";

    /// <summary>
    /// Verifies multi-byte UTF-8 reaches the console as UTF-16 code units.
    /// </summary>
    [TestMethod]
    public void WriteTranscodesUtf8ToUtf16()
    {
        StringBuilder console = new();
        var writer = new WindowsConsoleWriter(units => Accept(console, units));
        byte[] data = "医技--科室07-01\\dist\\assets\\index.js\n"u8.ToArray();

        int consumed = writer.Write(data);

        Assert.AreEqual(data.Length, consumed);
        Assert.AreEqual("医技--科室07-01\\dist\\assets\\index.js\n", console.ToString());
    }

    /// <summary>
    /// Verifies a sequence split across writes is held and emitted once it completes.
    /// </summary>
    [TestMethod]
    public void WriteHoldsSequenceSplitAcrossWrites()
    {
        StringBuilder console = new();
        var writer = new WindowsConsoleWriter(units => Accept(console, units));

        Assert.AreEqual(1, writer.Write([0xe5]));
        Assert.AreEqual(1, writer.Write([0x8c]));
        Assert.AreEqual(0, console.Length);

        Assert.AreEqual(1, writer.Write([0xbb, 0x61]));
        Assert.AreEqual("医", console.ToString());

        Assert.AreEqual(1, writer.Write([0x61]));
        Assert.AreEqual("医a", console.ToString());
    }

    /// <summary>
    /// Verifies a write stops in front of an incomplete trailing sequence.
    /// </summary>
    [TestMethod]
    public void WriteStopsBeforeTrailingIncompleteSequence()
    {
        StringBuilder console = new();
        var writer = new WindowsConsoleWriter(units => Accept(console, units));

        Assert.AreEqual(2, writer.Write([0x61, 0x62, 0xe5, 0x8c]));
        Assert.AreEqual("ab", console.ToString());
    }

    /// <summary>
    /// Verifies valid bytes ahead of invalid ones are written before the invalid bytes are rejected.
    /// </summary>
    [TestMethod]
    public void WriteStopsBeforeInvalidBytesThenRejectsThem()
    {
        StringBuilder console = new();
        var writer = new WindowsConsoleWriter(units => Accept(console, units));

        Assert.AreEqual(1, writer.Write([0x61, 0xff, 0x62]));
        Assert.AreEqual("a", console.ToString());

        IOException exception = Assert.ThrowsExactly<IOException>(() => writer.Write([0xff, 0x62]));
        Assert.AreEqual(NonUtf8Message, exception.Message);
    }

    /// <summary>
    /// Verifies a byte that cannot continue a held sequence is rejected and the held bytes are dropped.
    /// </summary>
    [TestMethod]
    public void WriteRejectsNonContinuationAfterIncompleteSequence()
    {
        StringBuilder console = new();
        var writer = new WindowsConsoleWriter(units => Accept(console, units));

        Assert.AreEqual(1, writer.Write([0xe5]));
        IOException exception = Assert.ThrowsExactly<IOException>(() => writer.Write([0x61]));
        Assert.AreEqual(NonUtf8Message, exception.Message);

        Assert.AreEqual(1, writer.Write([0x61]));
        Assert.AreEqual("a", console.ToString());
    }

    /// <summary>
    /// Verifies a single write never exceeds the console chunk size.
    /// </summary>
    [TestMethod]
    public void WriteCapsConsoleChunkSize()
    {
        StringBuilder console = new();
        var writer = new WindowsConsoleWriter(units => Accept(console, units));
        byte[] data = new byte[5000];
        Array.Fill(data, (byte)'a');

        Assert.AreEqual(4096, writer.Write(data));
        Assert.AreEqual(4096, console.Length);
    }

    /// <summary>
    /// Verifies a partial console write reports the UTF-8 bytes covered by the accepted code units.
    /// </summary>
    [TestMethod]
    public void PartialConsoleWriteReportsBytesCovered()
    {
        StringBuilder console = new();
        var writer = new WindowsConsoleWriter(units => Accept(console, units[..Math.Min(2, units.Length)]));

        Assert.AreEqual(3, writer.Write("aé医"u8.ToArray()));
        Assert.AreEqual("aé", console.ToString());
    }

    /// <summary>
    /// Verifies a partial console write never leaves half of a surrogate pair behind.
    /// </summary>
    [TestMethod]
    public void PartialConsoleWriteCompletesSurrogatePair()
    {
        StringBuilder console = new();
        var writer = new WindowsConsoleWriter(units => Accept(console, units[..Math.Min(2, units.Length)]));

        Assert.AreEqual(5, writer.Write("a😀b"u8.ToArray()));
        Assert.AreEqual("a😀", console.ToString());
    }

    private static int Accept(StringBuilder console, ReadOnlySpan<char> units)
    {
        console.Append(units);
        return units.Length;
    }
}
