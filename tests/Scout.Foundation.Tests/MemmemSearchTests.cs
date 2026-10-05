
namespace Scout;

/// <summary>
/// Verifies byte-substring behavior for the memmem port surface.
/// </summary>
[TestClass]
public sealed class MemmemSearchTests
{
    /// <summary>
    /// Verifies searches remain bounded for oversized needles and unaligned vector tails.
    /// </summary>
    [TestMethod]
    public void ReleaseBoundaryRegressionsStayWithinTheHaystack()
    {
        foreach (int length in new[] { 0, 1, 15, 16, 17, 22, 31, 32, 33, 63, 64, 65 })
        {
            byte[] storage = new byte[length + 3];
            Array.Fill(storage, (byte)'.');
            Span<byte> haystack = storage.AsSpan(1, length);
            byte[] oversized = new byte[length + 100];
            Array.Fill(oversized, (byte)'X');
            Assert.AreEqual(-1, MemmemSearch.Find(haystack, oversized));
            Assert.AreEqual(-1, MemmemSearch.FindReverse(haystack, oversized));
            Assert.AreEqual(-1, new MemmemFinder(oversized).Find(haystack));
            Assert.AreEqual(-1, new MemmemReverseFinder(oversized).FindReverse(haystack));
            if (length >= 2)
            {
                "ab"u8.CopyTo(haystack[^2..]);
                Assert.AreEqual(length - 2, MemmemSearch.Find(haystack, "ab"u8));
                Assert.AreEqual(length - 2, MemmemSearch.FindReverse(haystack, "ab"u8));
            }
        }
    }

    /// <summary>
    /// Verifies forward substring search preserves arbitrary bytes.
    /// </summary>
    [TestMethod]
    public void FindLocatesByteSequence()
    {
        ReadOnlySpan<byte> haystack = [0x61, 0xff, 0x00, 0x62, 0xff, 0x00];

        Assert.AreEqual(1, MemmemSearch.Find(haystack, [0xff, 0x00]));
        Assert.AreEqual(3, MemmemSearch.Find(haystack, [0x62]));
        Assert.AreEqual(-1, MemmemSearch.Find(haystack, [0x00, 0xff, 0x01]));
    }

    /// <summary>
    /// Verifies forward iterator-style substring search reports non-overlapping occurrences.
    /// </summary>
    [TestMethod]
    public void FindAllLocatesByteSequenceOccurrences()
    {
        ReadOnlySpan<byte> haystack = [0x66, 0x6f, 0x6f, 0x20, 0x66, 0x6f, 0x6f, 0x20, 0x66, 0x6f, 0x6f];

        Assert.AreSequenceEqual<int>([0, 4, 8], MemmemSearch.FindAll(haystack, "foo"u8));
        Assert.AreSequenceEqual<int>([0, 4, 8], Collect(MemmemSearch.Enumerate(haystack, "foo"u8)));
        Assert.AreSequenceEqual<int>([0, 4, 8], MemmemSearch.FindAll(haystack, [0x66]));
        Assert.IsEmpty(MemmemSearch.FindAll(haystack, "bar"u8));
        Assert.IsEmpty(Collect(MemmemSearch.Enumerate(haystack, "bar"u8)));
    }

    /// <summary>
    /// Verifies reusable forward finders own and reuse their needle bytes.
    /// </summary>
    [TestMethod]
    public void FinderReusesNeedleAcrossHaystacks()
    {
        byte[] mutableNeedle = "foo"u8.ToArray();
        var finder = new MemmemFinder(mutableNeedle);
        mutableNeedle[0] = (byte)'z';

        Assert.AreSequenceEqual("foo"u8.ToArray(), finder.Needle.ToArray());
        Assert.AreEqual(4, finder.Find("bar foo"u8));
        Assert.AreEqual(-1, finder.Find("bar baz"u8));
        Assert.AreSequenceEqual<int>([0, 8, 16], finder.FindAll("foo bar foo baz foo"u8));
        Assert.AreSequenceEqual<int>([0, 8, 16], Collect(finder.Enumerate("foo bar foo baz foo"u8)));
    }

    /// <summary>
    /// Verifies reverse search returns the last matching start offset.
    /// </summary>
    [TestMethod]
    public void FindReverseLocatesLastByteSequence()
    {
        ReadOnlySpan<byte> haystack = [0x61, 0xff, 0x00, 0x62, 0xff, 0x00];

        Assert.AreEqual(4, MemmemSearch.FindReverse(haystack, [0xff, 0x00]));
        Assert.AreEqual(3, MemmemSearch.FindReverse(haystack, [0x62]));
        Assert.AreEqual(-1, MemmemSearch.FindReverse(haystack, [0x00, 0xff, 0x01]));
    }

    /// <summary>
    /// Verifies reverse iterator-style substring search reports non-overlapping occurrences.
    /// </summary>
    [TestMethod]
    public void FindAllReverseLocatesByteSequenceOccurrences()
    {
        ReadOnlySpan<byte> haystack = [0x66, 0x6f, 0x6f, 0x20, 0x66, 0x6f, 0x6f, 0x20, 0x66, 0x6f, 0x6f];

        Assert.AreSequenceEqual<int>([8, 4, 0], MemmemSearch.FindAllReverse(haystack, "foo"u8));
        Assert.AreSequenceEqual<int>([8, 4, 0], Collect(MemmemSearch.EnumerateReverse(haystack, "foo"u8)));
        Assert.AreSequenceEqual<int>([8, 4, 0], MemmemSearch.FindAllReverse(haystack, [0x66]));
        Assert.IsEmpty(MemmemSearch.FindAllReverse(haystack, "bar"u8));
        Assert.IsEmpty(Collect(MemmemSearch.EnumerateReverse(haystack, "bar"u8)));
    }

    /// <summary>
    /// Verifies reusable reverse finders own and reuse their needle bytes.
    /// </summary>
    [TestMethod]
    public void ReverseFinderReusesNeedleAcrossHaystacks()
    {
        byte[] mutableNeedle = "foo"u8.ToArray();
        var finder = new MemmemReverseFinder(mutableNeedle);
        mutableNeedle[0] = (byte)'z';

        Assert.AreSequenceEqual("foo"u8.ToArray(), finder.Needle.ToArray());
        Assert.AreEqual(4, finder.FindReverse("bar foo"u8));
        Assert.AreEqual(-1, finder.FindReverse("bar baz"u8));
        Assert.AreSequenceEqual<int>([16, 8, 0], finder.FindAllReverse("foo bar foo baz foo"u8));
        Assert.AreSequenceEqual<int>([16, 8, 0], Collect(finder.EnumerateReverse("foo bar foo baz foo"u8)));
    }

    /// <summary>
    /// Verifies overlapping candidates are searched by start offset.
    /// </summary>
    [TestMethod]
    public void FindHandlesOverlappingNeedles()
    {
        ReadOnlySpan<byte> haystack = [0x61, 0x61, 0x61, 0x61];

        Assert.AreEqual(0, MemmemSearch.Find(haystack, [0x61, 0x61, 0x61]));
        Assert.AreEqual(1, MemmemSearch.FindReverse(haystack, [0x61, 0x61, 0x61]));
        Assert.AreSequenceEqual<int>([0], MemmemSearch.FindAll(haystack, [0x61, 0x61, 0x61]));
        Assert.AreSequenceEqual<int>([0], Collect(MemmemSearch.Enumerate(haystack, [0x61, 0x61, 0x61])));
        Assert.AreSequenceEqual<int>([1], MemmemSearch.FindAllReverse(haystack, [0x61, 0x61, 0x61]));
        Assert.AreSequenceEqual<int>([1], Collect(MemmemSearch.EnumerateReverse(haystack, [0x61, 0x61, 0x61])));
    }

    /// <summary>
    /// Verifies packed-pair substring search confirms candidates and preserves tail matches.
    /// </summary>
    [TestMethod]
    public void FindHandlesPackedPairFalsePositivesAndTailMatch()
    {
        byte[] haystack = new byte[360];
        haystack.AsSpan().Fill((byte)'x');
        "aeaeaeaeaf"u8.CopyTo(haystack.AsSpan(260));
        "aeaeaeaeae"u8.CopyTo(haystack.AsSpan(345));

        Assert.AreEqual(345, MemmemSearch.Find(haystack, "aeaeaeaeae"u8));
        Assert.AreEqual(345, new MemmemFinder("aeaeaeaeae"u8).Find(haystack));
    }

    /// <summary>
    /// Verifies packed-pair search rejects dense repeated-byte false positives.
    /// </summary>
    [TestMethod]
    public void FindHandlesRepeatedRarestByteFalsePositives()
    {
        byte[] haystack = new byte[512];
        haystack.AsSpan().Fill((byte)'x');
        for (int index = 0; index < haystack.Length - 4; index += 4)
        {
            "eeee"u8.CopyTo(haystack.AsSpan(index, 4));
        }

        Assert.AreEqual(-1, MemmemSearch.Find(haystack, "aeaeaeaeae"u8));
        Assert.AreEqual(-1, new MemmemFinder("aeaeaeaeae"u8).Find(haystack));
    }

    /// <summary>
    /// Verifies three-byte packed-pair search handles dense non-adjacent false positives.
    /// </summary>
    [TestMethod]
    public void FindHandlesThreeBytePackedPairFalsePositives()
    {
        byte[] haystack = new byte[512];
        haystack.AsSpan().Fill((byte)'x');
        for (int index = 0; index < 480; index += 4)
        {
            "axi"u8.CopyTo(haystack.AsSpan(index, 3));
        }

        "aei"u8.CopyTo(haystack.AsSpan(500, 3));

        Assert.AreEqual(500, MemmemSearch.Find(haystack, "aei"u8));
        Assert.AreEqual(500, new MemmemFinder("aei"u8).Find(haystack));
    }

    /// <summary>
    /// Verifies fused packed-pair search observes NUL bytes across vector and scalar boundaries.
    /// </summary>
    [TestMethod]
    public void FindAndDetectNulHandlesPackedPairBoundaries()
    {
        byte[] needle = "aeaeaeaeae"u8.ToArray();
        int[] nulOffsets = [0, 15, 16, 31, 32, 254, 255, 256, 510, 511];
        foreach (int nulOffset in nulOffsets)
        {
            byte[] haystack = new byte[512];
            haystack.AsSpan().Fill((byte)'x');
            haystack[nulOffset] = 0;
            needle.CopyTo(haystack, 345);
            var finder = new MemmemFinder(needle);
            bool containsNul = false;

            int match = finder.FindAndDetectNul(haystack, ref containsNul);

            Assert.AreEqual(345, match);
            if (nulOffset < match)
            {
                Assert.IsTrue(containsNul);
            }

            containsNul = false;
            Assert.AreEqual(-1, finder.FindAndDetectNul(haystack.AsSpan(match + 1), ref containsNul));
            Assert.AreEqual(nulOffset > match, containsNul);
        }
    }

    /// <summary>
    /// Verifies empty-needle semantics match Rust substring search behavior.
    /// </summary>
    [TestMethod]
    public void EmptyNeedleMatchesBoundary()
    {
        ReadOnlySpan<byte> haystack = [0x61, 0x62, 0x63];

        Assert.AreEqual(0, MemmemSearch.Find(haystack, []));
        Assert.AreEqual(3, MemmemSearch.FindReverse(haystack, []));
        Assert.AreSequenceEqual<int>([0, 1, 2, 3], MemmemSearch.FindAll(haystack, []));
        Assert.AreSequenceEqual<int>([0, 1, 2, 3], Collect(MemmemSearch.Enumerate(haystack, [])));
        Assert.AreSequenceEqual<int>([3, 2, 1, 0], MemmemSearch.FindAllReverse(haystack, []));
        Assert.AreSequenceEqual<int>([3, 2, 1, 0], Collect(MemmemSearch.EnumerateReverse(haystack, [])));
        Assert.AreSequenceEqual<int>([0, 1, 2, 3], new MemmemFinder([]).FindAll(haystack));
        Assert.AreSequenceEqual<int>([0, 1, 2, 3], Collect(new MemmemFinder([]).Enumerate(haystack)));
        Assert.AreSequenceEqual<int>([3, 2, 1, 0], new MemmemReverseFinder([]).FindAllReverse(haystack));
        Assert.AreSequenceEqual<int>([3, 2, 1, 0], Collect(new MemmemReverseFinder([]).EnumerateReverse(haystack)));
    }

    private static int[] Collect(MemmemEnumerator enumerator)
    {
        var matches = new List<int>();
        while (enumerator.MoveNext())
        {
            matches.Add(enumerator.Current);
        }

        return matches.ToArray();
    }

    private static int[] Collect(MemmemReverseEnumerator enumerator)
    {
        var matches = new List<int>();
        while (enumerator.MoveNext())
        {
            matches.Add(enumerator.Current);
        }

        return matches.ToArray();
    }
}
