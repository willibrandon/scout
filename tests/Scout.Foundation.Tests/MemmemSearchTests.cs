
namespace Scout;

/// <summary>
/// Verifies byte-substring behavior for the memmem port surface.
/// </summary>
public sealed class MemmemSearchTests
{
    /// <summary>
    /// Verifies forward substring search preserves arbitrary bytes.
    /// </summary>
    [Fact]
    public void FindLocatesByteSequence()
    {
        ReadOnlySpan<byte> haystack = [0x61, 0xff, 0x00, 0x62, 0xff, 0x00];

        Assert.Equal(1, MemmemSearch.Find(haystack, [0xff, 0x00]));
        Assert.Equal(3, MemmemSearch.Find(haystack, [0x62]));
        Assert.Equal(-1, MemmemSearch.Find(haystack, [0x00, 0xff, 0x01]));
    }

    /// <summary>
    /// Verifies forward iterator-style substring search reports non-overlapping occurrences.
    /// </summary>
    [Fact]
    public void FindAllLocatesByteSequenceOccurrences()
    {
        ReadOnlySpan<byte> haystack = [0x66, 0x6f, 0x6f, 0x20, 0x66, 0x6f, 0x6f, 0x20, 0x66, 0x6f, 0x6f];

        Assert.Equal([0, 4, 8], MemmemSearch.FindAll(haystack, "foo"u8));
        Assert.Equal([0, 4, 8], Collect(MemmemSearch.Enumerate(haystack, "foo"u8)));
        Assert.Equal([0, 4, 8], MemmemSearch.FindAll(haystack, [0x66]));
        Assert.Empty(MemmemSearch.FindAll(haystack, "bar"u8));
        Assert.Empty(Collect(MemmemSearch.Enumerate(haystack, "bar"u8)));
    }

    /// <summary>
    /// Verifies reusable forward finders own and reuse their needle bytes.
    /// </summary>
    [Fact]
    public void FinderReusesNeedleAcrossHaystacks()
    {
        byte[] mutableNeedle = "foo"u8.ToArray();
        var finder = new MemmemFinder(mutableNeedle);
        mutableNeedle[0] = (byte)'z';

        Assert.Equal("foo"u8.ToArray(), finder.Needle.ToArray());
        Assert.Equal(4, finder.Find("bar foo"u8));
        Assert.Equal(-1, finder.Find("bar baz"u8));
        Assert.Equal([0, 8, 16], finder.FindAll("foo bar foo baz foo"u8));
        Assert.Equal([0, 8, 16], Collect(finder.Enumerate("foo bar foo baz foo"u8)));
    }

    /// <summary>
    /// Verifies reverse search returns the last matching start offset.
    /// </summary>
    [Fact]
    public void FindReverseLocatesLastByteSequence()
    {
        ReadOnlySpan<byte> haystack = [0x61, 0xff, 0x00, 0x62, 0xff, 0x00];

        Assert.Equal(4, MemmemSearch.FindReverse(haystack, [0xff, 0x00]));
        Assert.Equal(3, MemmemSearch.FindReverse(haystack, [0x62]));
        Assert.Equal(-1, MemmemSearch.FindReverse(haystack, [0x00, 0xff, 0x01]));
    }

    /// <summary>
    /// Verifies reverse iterator-style substring search reports non-overlapping occurrences.
    /// </summary>
    [Fact]
    public void FindAllReverseLocatesByteSequenceOccurrences()
    {
        ReadOnlySpan<byte> haystack = [0x66, 0x6f, 0x6f, 0x20, 0x66, 0x6f, 0x6f, 0x20, 0x66, 0x6f, 0x6f];

        Assert.Equal([8, 4, 0], MemmemSearch.FindAllReverse(haystack, "foo"u8));
        Assert.Equal([8, 4, 0], Collect(MemmemSearch.EnumerateReverse(haystack, "foo"u8)));
        Assert.Equal([8, 4, 0], MemmemSearch.FindAllReverse(haystack, [0x66]));
        Assert.Empty(MemmemSearch.FindAllReverse(haystack, "bar"u8));
        Assert.Empty(Collect(MemmemSearch.EnumerateReverse(haystack, "bar"u8)));
    }

    /// <summary>
    /// Verifies reusable reverse finders own and reuse their needle bytes.
    /// </summary>
    [Fact]
    public void ReverseFinderReusesNeedleAcrossHaystacks()
    {
        byte[] mutableNeedle = "foo"u8.ToArray();
        var finder = new MemmemReverseFinder(mutableNeedle);
        mutableNeedle[0] = (byte)'z';

        Assert.Equal("foo"u8.ToArray(), finder.Needle.ToArray());
        Assert.Equal(4, finder.FindReverse("bar foo"u8));
        Assert.Equal(-1, finder.FindReverse("bar baz"u8));
        Assert.Equal([16, 8, 0], finder.FindAllReverse("foo bar foo baz foo"u8));
        Assert.Equal([16, 8, 0], Collect(finder.EnumerateReverse("foo bar foo baz foo"u8)));
    }

    /// <summary>
    /// Verifies overlapping candidates are searched by start offset.
    /// </summary>
    [Fact]
    public void FindHandlesOverlappingNeedles()
    {
        ReadOnlySpan<byte> haystack = [0x61, 0x61, 0x61, 0x61];

        Assert.Equal(0, MemmemSearch.Find(haystack, [0x61, 0x61, 0x61]));
        Assert.Equal(1, MemmemSearch.FindReverse(haystack, [0x61, 0x61, 0x61]));
        Assert.Equal([0], MemmemSearch.FindAll(haystack, [0x61, 0x61, 0x61]));
        Assert.Equal([0], Collect(MemmemSearch.Enumerate(haystack, [0x61, 0x61, 0x61])));
        Assert.Equal([1], MemmemSearch.FindAllReverse(haystack, [0x61, 0x61, 0x61]));
        Assert.Equal([1], Collect(MemmemSearch.EnumerateReverse(haystack, [0x61, 0x61, 0x61])));
    }

    /// <summary>
    /// Verifies packed-pair substring search confirms candidates and preserves tail matches.
    /// </summary>
    [Fact]
    public void FindHandlesPackedPairFalsePositivesAndTailMatch()
    {
        byte[] haystack = new byte[360];
        haystack.AsSpan().Fill((byte)'x');
        "aeaeaeaeaf"u8.CopyTo(haystack.AsSpan(260));
        "aeaeaeaeae"u8.CopyTo(haystack.AsSpan(345));

        Assert.Equal(345, MemmemSearch.Find(haystack, "aeaeaeaeae"u8));
        Assert.Equal(345, new MemmemFinder("aeaeaeaeae"u8).Find(haystack));
    }

    /// <summary>
    /// Verifies packed-pair search rejects dense repeated-byte false positives.
    /// </summary>
    [Fact]
    public void FindHandlesRepeatedRarestByteFalsePositives()
    {
        byte[] haystack = new byte[512];
        haystack.AsSpan().Fill((byte)'x');
        for (int index = 0; index < haystack.Length - 4; index += 4)
        {
            "eeee"u8.CopyTo(haystack.AsSpan(index, 4));
        }

        Assert.Equal(-1, MemmemSearch.Find(haystack, "aeaeaeaeae"u8));
        Assert.Equal(-1, new MemmemFinder("aeaeaeaeae"u8).Find(haystack));
    }

    /// <summary>
    /// Verifies three-byte packed-pair search handles dense non-adjacent false positives.
    /// </summary>
    [Fact]
    public void FindHandlesThreeBytePackedPairFalsePositives()
    {
        byte[] haystack = new byte[512];
        haystack.AsSpan().Fill((byte)'x');
        for (int index = 0; index < 480; index += 4)
        {
            "axi"u8.CopyTo(haystack.AsSpan(index, 3));
        }

        "aei"u8.CopyTo(haystack.AsSpan(500, 3));

        Assert.Equal(500, MemmemSearch.Find(haystack, "aei"u8));
        Assert.Equal(500, new MemmemFinder("aei"u8).Find(haystack));
    }

    /// <summary>
    /// Verifies empty-needle semantics match Rust substring search behavior.
    /// </summary>
    [Fact]
    public void EmptyNeedleMatchesBoundary()
    {
        ReadOnlySpan<byte> haystack = [0x61, 0x62, 0x63];

        Assert.Equal(0, MemmemSearch.Find(haystack, []));
        Assert.Equal(3, MemmemSearch.FindReverse(haystack, []));
        Assert.Equal([0, 1, 2, 3], MemmemSearch.FindAll(haystack, []));
        Assert.Equal([0, 1, 2, 3], Collect(MemmemSearch.Enumerate(haystack, [])));
        Assert.Equal([3, 2, 1, 0], MemmemSearch.FindAllReverse(haystack, []));
        Assert.Equal([3, 2, 1, 0], Collect(MemmemSearch.EnumerateReverse(haystack, [])));
        Assert.Equal([0, 1, 2, 3], new MemmemFinder([]).FindAll(haystack));
        Assert.Equal([0, 1, 2, 3], Collect(new MemmemFinder([]).Enumerate(haystack)));
        Assert.Equal([3, 2, 1, 0], new MemmemReverseFinder([]).FindAllReverse(haystack));
        Assert.Equal([3, 2, 1, 0], Collect(new MemmemReverseFinder([]).EnumerateReverse(haystack)));
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
