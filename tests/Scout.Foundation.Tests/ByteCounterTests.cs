namespace Scout;

/// <summary>
/// Verifies SIMD-gated byte counting behavior.
/// </summary>
[TestClass]
public sealed class ByteCounterTests
{
    /// <summary>
    /// Counts bytes across scalar and vector-sized boundaries.
    /// </summary>
    [TestMethod]
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

        Assert.AreEqual(10, ByteCounter.Count(haystack, 0x2a));
        Assert.AreEqual(0, ByteCounter.Count(haystack, 0x7f));
    }

    /// <summary>
    /// Counts empty and short inputs through the scalar fallback.
    /// </summary>
    [TestMethod]
    public void CountHandlesEmptyAndShortInputs()
    {
        Assert.AreEqual(0, ByteCounter.Count([], 0x00));
        Assert.AreEqual(2, ByteCounter.Count([0x00, 0xff, 0x00], 0x00));
    }

    /// <summary>
    /// Counts large all-match inputs without overflowing vector accumulators.
    /// </summary>
    [TestMethod]
    public void CountHandlesLargeAllMatchInput()
    {
        byte[] haystack = new byte[(1024 * 1024) + 123];
        Array.Fill(haystack, (byte)'\n');

        Assert.AreEqual(haystack.Length, ByteCounter.Count(haystack, (byte)'\n'));
    }

    /// <summary>
    /// Counts one byte while returning the first position of another byte.
    /// </summary>
    [TestMethod]
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

        Assert.AreEqual(4, count);
        Assert.AreEqual(32, firstFound);
        Assert.AreEqual(4, ByteCounter.CountAndFindFirst(haystack, (byte)'\n', (byte)'x', out int missing));
        Assert.AreEqual(-1, missing);
    }

    /// <summary>
    /// Counts a large input while locating a find byte in the scalar tail.
    /// </summary>
    [TestMethod]
    public void CountAndFindFirstHandlesLargeInputAndScalarTail()
    {
        byte[] haystack = new byte[(1024 * 1024) + 3];
        Array.Fill(haystack, (byte)'\n');
        haystack[^1] = 0;

        long count = ByteCounter.CountAndFindFirst(haystack, (byte)'\n', 0, out int firstFound);

        Assert.AreEqual((long)haystack.Length - 1, count);
        Assert.AreEqual(haystack.Length - 1, firstFound);
    }
}
