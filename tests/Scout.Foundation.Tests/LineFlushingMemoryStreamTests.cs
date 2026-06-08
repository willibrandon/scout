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
}
