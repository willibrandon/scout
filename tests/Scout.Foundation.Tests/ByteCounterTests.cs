namespace Scout;

/// <summary>
/// Verifies SIMD-gated byte counting behavior.
/// </summary>
public sealed class ByteCounterTests
{
    /// <summary>
    /// Counts bytes across scalar and vector-sized boundaries.
    /// </summary>
    [Fact]
    public void CountScansAcrossVectorBoundaries()
    {
        byte[] haystack = new byte[173];
        haystack[0] = 0x2a;
        haystack[15] = 0x2a;
        haystack[16] = 0x2a;
        haystack[31] = 0x2a;
        haystack[32] = 0x2a;
        haystack[63] = 0x2a;
        haystack[64] = 0x2a;
        haystack[127] = 0x2a;
        haystack[128] = 0x2a;
        haystack[172] = 0x2a;

        Assert.Equal(10, ByteCounter.Count(haystack, 0x2a));
        Assert.Equal(0, ByteCounter.Count(haystack, 0x7f));
    }

    /// <summary>
    /// Counts empty and short inputs through the scalar fallback.
    /// </summary>
    [Fact]
    public void CountHandlesEmptyAndShortInputs()
    {
        Assert.Equal(0, ByteCounter.Count([], 0x00));
        Assert.Equal(2, ByteCounter.Count([0x00, 0xff, 0x00], 0x00));
    }

    /// <summary>
    /// Counts large all-match inputs without overflowing vector accumulators.
    /// </summary>
    [Fact]
    public void CountHandlesLargeAllMatchInput()
    {
        byte[] haystack = new byte[(1024 * 1024) + 123];
        Array.Fill(haystack, (byte)'\n');

        Assert.Equal(haystack.Length, ByteCounter.Count(haystack, (byte)'\n'));
    }

    /// <summary>
    /// Counts one byte while returning the first position of another byte.
    /// </summary>
    [Fact]
    public void CountAndFindFirstScansAcrossVectorBoundaries()
    {
        byte[] haystack = new byte[257];
        Array.Fill(haystack, (byte)'a');
        haystack[0] = (byte)'\n';
        haystack[31] = (byte)'\n';
        haystack[32] = 0;
        haystack[64] = (byte)'\n';
        haystack[128] = 0;
        haystack[256] = (byte)'\n';

        long count = ByteCounter.CountAndFindFirst(haystack, (byte)'\n', 0, out int firstFound);

        Assert.Equal(4, count);
        Assert.Equal(32, firstFound);
        Assert.Equal(4, ByteCounter.CountAndFindFirst(haystack, (byte)'\n', (byte)'x', out int missing));
        Assert.Equal(-1, missing);
    }
}
