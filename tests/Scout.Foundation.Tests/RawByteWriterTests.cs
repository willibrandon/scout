using System.IO;

namespace Scout;

/// <summary>
/// Verifies raw byte writer behavior.
/// </summary>
public sealed class RawByteWriterTests
{
    /// <summary>
    /// Verifies raw writes preserve arbitrary bytes without text transcoding.
    /// </summary>
    [Fact]
    public void WritePreservesBytes()
    {
        using MemoryStream stream = new();
        var writer = new RawByteWriter(stream);

        writer.Write([0xff, 0x0a, 0x80]);
        writer.Flush();

        Assert.Equal([0xff, 0x0a, 0x80], stream.ToArray());
    }

    /// <summary>
    /// Verifies line writes append a single line-feed byte.
    /// </summary>
    [Fact]
    public void WriteLineAppendsLineFeedByte()
    {
        using MemoryStream stream = new();
        var writer = new RawByteWriter(stream);

        writer.WriteLine([0x61, 0xff]);
        writer.Flush();

        Assert.Equal([0x61, 0xff, 0x0a], stream.ToArray());
    }
}
