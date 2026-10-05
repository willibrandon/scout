
namespace Scout;

/// <summary>
/// Verifies byte-search behavior for the memchr port surface.
/// </summary>
[TestClass]
public sealed class MemchrSearchTests
{
    /// <summary>
    /// Verifies first and reverse single-byte searches.
    /// </summary>
    [TestMethod]
    public void FindLocatesSingleByte()
    {
        ReadOnlySpan<byte> haystack = [0x00, 0xff, 0x20, 0xff, 0x40];

        Assert.AreEqual(1, MemchrSearch.Find(haystack, 0xff));
        Assert.AreEqual(3, MemchrSearch.FindReverse(haystack, 0xff));
        Assert.AreEqual(-1, MemchrSearch.Find(haystack, 0x99));
        Assert.AreEqual(-1, MemchrSearch.FindReverse(haystack, 0x99));
    }

    /// <summary>
    /// Verifies single-byte search handles vector-sized haystacks and scalar tails.
    /// </summary>
    [TestMethod]
    public void FindLocatesSingleByteAcrossVectorBoundaries()
    {
        byte[] haystack = new byte[150];
        Array.Fill(haystack, (byte)0x10);
        haystack[64] = 0x20;
        haystack[149] = 0x30;

        Assert.AreEqual(64, MemchrSearch.Find(haystack, 0x20));
        Assert.AreEqual(149, MemchrSearch.Find(haystack, 0x30));
        Assert.AreEqual(149, MemchrSearch.FindReverse(haystack, 0x30));
        Assert.AreEqual(64, MemchrSearch.FindReverse(haystack, 0x20));
        Assert.AreEqual(-1, MemchrSearch.Find(haystack, 0x40));
        Assert.AreEqual(-1, MemchrSearch.FindReverse(haystack, 0x40));
    }

    /// <summary>
    /// Verifies single-byte iterator-style searches report every match.
    /// </summary>
    [TestMethod]
    public void FindAllLocatesSingleByteOccurrences()
    {
        ReadOnlySpan<byte> haystack = [0x00, 0xff, 0x20, 0xff, 0x40];

        Assert.AreSequenceEqual<int>([1, 3], MemchrSearch.FindAll(haystack, 0xff));
        Assert.AreSequenceEqual<int>([3, 1], MemchrSearch.FindAllReverse(haystack, 0xff));
        Assert.AreSequenceEqual<int>([1, 3], Collect(MemchrSearch.Enumerate(haystack, 0xff)));
        Assert.AreSequenceEqual<int>([3, 1], Collect(MemchrSearch.EnumerateReverse(haystack, 0xff)));
        Assert.IsEmpty(MemchrSearch.FindAll(haystack, 0x99));
        Assert.IsEmpty(MemchrSearch.FindAllReverse(haystack, 0x99));
    }

    /// <summary>
    /// Verifies two-byte search variants match the earliest or latest candidate.
    /// </summary>
    [TestMethod]
    public void Find2LocatesEitherByte()
    {
        ReadOnlySpan<byte> haystack = [0x10, 0x20, 0x30, 0x40, 0x20];

        Assert.AreEqual(1, MemchrSearch.Find2(haystack, 0x20, 0x40));
        Assert.AreEqual(1, MemchrSearch.Find2(haystack, 0x20, 0x20));
        Assert.AreEqual(4, MemchrSearch.Find2Reverse(haystack, 0x20, 0x40));
        Assert.AreEqual(-1, MemchrSearch.Find2(haystack, 0x55, 0x66));
        Assert.AreEqual(-1, MemchrSearch.Find2Reverse(haystack, 0x55, 0x66));
    }

    /// <summary>
    /// Verifies two-byte search handles vector-sized haystacks and scalar tails.
    /// </summary>
    [TestMethod]
    public void Find2LocatesEitherByteAcrossVectorBoundaries()
    {
        byte[] haystack = new byte[150];
        Array.Fill(haystack, (byte)0x10);
        haystack[65] = 0x20;
        haystack[149] = 0x30;

        Assert.AreEqual(65, MemchrSearch.Find2(haystack, 0x20, 0x30));
        Assert.AreEqual(149, MemchrSearch.Find2(haystack, 0x30, 0x40));
        Assert.AreEqual(149, MemchrSearch.Find2Reverse(haystack, 0x20, 0x30));
        Assert.AreEqual(65, MemchrSearch.Find2Reverse(haystack, 0x20, 0x40));
        Assert.AreEqual(-1, MemchrSearch.Find2(haystack, 0x40, 0x50));
        Assert.AreEqual(-1, MemchrSearch.Find2Reverse(haystack, 0x40, 0x50));
    }

    /// <summary>
    /// Verifies two-byte iterator-style searches report every candidate.
    /// </summary>
    [TestMethod]
    public void Find2AllLocatesEitherByteOccurrences()
    {
        ReadOnlySpan<byte> haystack = [0x10, 0x20, 0x30, 0x40, 0x20];

        Assert.AreSequenceEqual<int>([1, 3, 4], MemchrSearch.Find2All(haystack, 0x20, 0x40));
        Assert.AreSequenceEqual<int>([4, 3, 1], MemchrSearch.Find2AllReverse(haystack, 0x20, 0x40));
        Assert.AreSequenceEqual<int>([1, 3, 4], Collect(MemchrSearch.Enumerate2(haystack, 0x20, 0x40)));
        Assert.AreSequenceEqual<int>([4, 3, 1], Collect(MemchrSearch.Enumerate2Reverse(haystack, 0x20, 0x40)));
        Assert.IsEmpty(MemchrSearch.Find2All(haystack, 0x55, 0x66));
        Assert.IsEmpty(MemchrSearch.Find2AllReverse(haystack, 0x55, 0x66));
    }

    /// <summary>
    /// Verifies three-byte search variants match the earliest or latest candidate.
    /// </summary>
    [TestMethod]
    public void Find3LocatesAnyByte()
    {
        ReadOnlySpan<byte> haystack = [0x10, 0x20, 0x30, 0x40, 0x20];

        Assert.AreEqual(1, MemchrSearch.Find3(haystack, 0x55, 0x20, 0x40));
        Assert.AreEqual(1, MemchrSearch.Find3(haystack, 0x55, 0x20, 0x20));
        Assert.AreEqual(4, MemchrSearch.Find3Reverse(haystack, 0x55, 0x20, 0x40));
        Assert.AreEqual(-1, MemchrSearch.Find3(haystack, 0x55, 0x66, 0x77));
        Assert.AreEqual(-1, MemchrSearch.Find3Reverse(haystack, 0x55, 0x66, 0x77));
    }

    /// <summary>
    /// Verifies three-byte search handles vector-sized haystacks and scalar tails.
    /// </summary>
    [TestMethod]
    public void Find3LocatesAnyByteAcrossVectorBoundaries()
    {
        byte[] haystack = new byte[150];
        Array.Fill(haystack, (byte)0x10);
        haystack[66] = 0x20;
        haystack[149] = 0x30;

        Assert.AreEqual(66, MemchrSearch.Find3(haystack, 0x20, 0x30, 0x40));
        Assert.AreEqual(149, MemchrSearch.Find3(haystack, 0x30, 0x40, 0x50));
        Assert.AreEqual(149, MemchrSearch.Find3Reverse(haystack, 0x20, 0x30, 0x40));
        Assert.AreEqual(66, MemchrSearch.Find3Reverse(haystack, 0x20, 0x40, 0x50));
        Assert.AreEqual(-1, MemchrSearch.Find3(haystack, 0x40, 0x50, 0x60));
        Assert.AreEqual(-1, MemchrSearch.Find3Reverse(haystack, 0x40, 0x50, 0x60));
    }

    /// <summary>
    /// Verifies three-byte iterator-style searches report every candidate.
    /// </summary>
    [TestMethod]
    public void Find3AllLocatesAnyByteOccurrences()
    {
        ReadOnlySpan<byte> haystack = [0x10, 0x20, 0x30, 0x40, 0x20];

        Assert.AreSequenceEqual<int>([0, 1, 3, 4], MemchrSearch.Find3All(haystack, 0x10, 0x20, 0x40));
        Assert.AreSequenceEqual<int>([4, 3, 1, 0], MemchrSearch.Find3AllReverse(haystack, 0x10, 0x20, 0x40));
        Assert.AreSequenceEqual<int>([0, 1, 3, 4], Collect(MemchrSearch.Enumerate3(haystack, 0x10, 0x20, 0x40)));
        Assert.AreSequenceEqual<int>([4, 3, 1, 0], Collect(MemchrSearch.Enumerate3Reverse(haystack, 0x10, 0x20, 0x40)));
        Assert.IsEmpty(MemchrSearch.Find3All(haystack, 0x55, 0x66, 0x77));
        Assert.IsEmpty(MemchrSearch.Find3AllReverse(haystack, 0x55, 0x66, 0x77));
    }

    private static int[] Collect(MemchrEnumerator enumerator)
    {
        var matches = new List<int>();
        while (enumerator.MoveNext())
        {
            matches.Add(enumerator.Current);
        }

        return matches.ToArray();
    }

    private static int[] Collect(Memchr2Enumerator enumerator)
    {
        var matches = new List<int>();
        while (enumerator.MoveNext())
        {
            matches.Add(enumerator.Current);
        }

        return matches.ToArray();
    }

    private static int[] Collect(Memchr3Enumerator enumerator)
    {
        var matches = new List<int>();
        while (enumerator.MoveNext())
        {
            matches.Add(enumerator.Current);
        }

        return matches.ToArray();
    }
}
