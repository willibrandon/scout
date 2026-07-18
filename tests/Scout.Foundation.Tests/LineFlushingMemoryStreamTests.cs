namespace Scout;

/// <summary>
/// Verifies parallel direct-output line flushing behavior.
/// </summary>
public sealed class LineFlushingMemoryStreamTests
{
    /// <summary>
    /// Verifies threshold flushing writes only complete records.
    /// </summary>
    [Fact]
    public void ThresholdFlushesCompleteLinesOnly()
    {
        using MemoryStream output = new();
        RawByteWriter writer = new(output);
        object outputLock = new();
        using LineFlushingMemoryStream buffer = new(writer, outputLock, (byte)'\n', lineFlushThreshold: 4);

        buffer.Write("aa\nbb"u8);

        Assert.Equal("aa\n"u8.ToArray(), output.ToArray());
        Assert.Equal(2, buffer.Length);

        buffer.Flush();

        Assert.Equal("aa\nbb"u8.ToArray(), output.ToArray());
        Assert.Equal(0, buffer.Length);
    }

    /// <summary>
    /// Verifies threshold flushing keeps the worker buffer reusable for output-heavy parallel searches.
    /// </summary>
    [Fact]
    public void ThresholdFlushKeepsReusableCapacity()
    {
        using MemoryStream output = new();
        RawByteWriter writer = new(output);
        object outputLock = new();
        using LineFlushingMemoryStream buffer = new(writer, outputLock, (byte)'\n', lineFlushThreshold: 4);

        buffer.Write("aa\nbb\n"u8);

        Assert.Equal("aa\nbb\n"u8.ToArray(), output.ToArray());
        Assert.Equal(0, buffer.Length);
        Assert.True(buffer.Capacity > 0);
    }

    /// <summary>
    /// Verifies a partial record cannot force the reusable buffer above its configured threshold.
    /// </summary>
    [Fact]
    public void ThresholdCrossingFlushesBeforeGrowingReusableCapacity()
    {
        using MemoryStream output = new();
        RawByteWriter writer = new(output);
        object outputLock = new();
        using LineFlushingMemoryStream buffer = new(
            writer,
            outputLock,
            (byte)'\n',
            lineFlushThreshold: 256);

        byte[] first = CreateRecordChunk((byte)'a', (byte)'b');
        byte[] second = CreateRecordChunk((byte)'c', (byte)'x');
        byte[] third = CreateRecordChunk((byte)'d', (byte)'q');
        byte[] fourth = CreateRecordChunk((byte)'e', (byte)'r');

        buffer.Write(first);
        buffer.Write(second);
        buffer.Write(third);
        buffer.Write(fourth);

        Assert.InRange(buffer.Capacity, 1, 256);

        buffer.Flush();

        Assert.Equal(
            first.Concat(second).Concat(third).Concat(fourth).ToArray(),
            output.ToArray());
    }

    private static byte[] CreateRecordChunk(byte fill, byte tail)
    {
        byte[] chunk = new byte[128];
        chunk.AsSpan(0, 126).Fill(fill);
        chunk[126] = (byte)'\n';
        chunk[127] = tail;
        return chunk;
    }
}
