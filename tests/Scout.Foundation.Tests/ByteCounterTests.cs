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
}
