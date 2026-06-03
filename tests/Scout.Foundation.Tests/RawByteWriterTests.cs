
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

    /// <summary>
    /// Verifies block buffering holds bytes until the buffer is flushed.
    /// </summary>
    [Fact]
    public void BlockBufferingWritesOnlyWhenFlushed()
    {
        using MemoryStream stream = new();
        var writer = new RawByteWriter(stream, RawByteWriterBufferMode.Block);

        writer.Write([0x61, 0x62]);
        Assert.Empty(stream.ToArray());

        writer.Flush();
        Assert.Equal([0x61, 0x62], stream.ToArray());
    }

    /// <summary>
    /// Verifies line buffering flushes when a line-feed byte is written.
    /// </summary>
    [Fact]
    public void LineBufferingFlushesAfterLineFeed()
    {
        using var stream = new RecordingWriteStream();
        var writer = new RawByteWriter(stream, RawByteWriterBufferMode.Line);

        writer.Write([0x61, 0x62]);
        Assert.Empty(stream.ToArray());

        writer.Write([0x0a]);
        Assert.Equal([0x61, 0x62, 0x0a], stream.ToArray());
        Assert.Equal(1, stream.FlushCount);
    }

    /// <summary>
    /// Verifies changing buffering mode first flushes bytes held by the prior mode.
    /// </summary>
    [Fact]
    public void SetBufferModeFlushesPendingBytes()
    {
        using MemoryStream stream = new();
        var writer = new RawByteWriter(stream, RawByteWriterBufferMode.Block);

        writer.Write([0x61]);
        writer.SetBufferMode(RawByteWriterBufferMode.None);
        writer.Write([0x62]);

        Assert.Equal([0x61, 0x62], stream.ToArray());
    }
}
