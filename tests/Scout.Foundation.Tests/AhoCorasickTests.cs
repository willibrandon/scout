
namespace Scout;

/// <summary>
/// Verifies the initial byte-oriented Aho-Corasick port surface.
/// </summary>
public sealed class AhoCorasickTests
{
    /// <summary>
    /// Verifies standard non-overlapping search reports matches when first seen.
    /// </summary>
    [Fact]
    public void FindAllUsesStandardNonOverlappingSemantics()
    {
        AhoCorasickAutomaton automaton = Build("abcd"u8.ToArray(), "ab"u8.ToArray(), "abc"u8.ToArray());

        AssertMatches(
            automaton.FindAll("abcd"u8),
            [(1, 0, 2)]);
        AssertMatches(
            Collect(automaton.Enumerate("abcd"u8)),
            [(1, 0, 2)]);
    }

    /// <summary>
    /// Verifies standard search resumes after the reported match end.
    /// </summary>
    [Fact]
    public void FindAllResumesAfterMatchEnd()
    {
        AhoCorasickAutomaton automaton = Build(
            "abcd"u8.ToArray(),
            "bcd"u8.ToArray(),
            "cd"u8.ToArray(),
            "b"u8.ToArray());

        AssertMatches(
            automaton.FindAll("abcd"u8),
            [(3, 1, 2), (2, 2, 4)]);
    }

    /// <summary>
    /// Verifies overlapping search reports every pattern ending at a byte offset.
    /// </summary>
    [Fact]
    public void FindOverlappingReportsAllMatchesInUpstreamOrder()
    {
        AhoCorasickAutomaton automaton = Build(
            "abcd"u8.ToArray(),
            "bcd"u8.ToArray(),
            "cd"u8.ToArray(),
            "b"u8.ToArray());

        AssertMatches(
            automaton.FindOverlapping("abcd"u8),
            [(3, 1, 2), (0, 0, 4), (1, 1, 4), (2, 2, 4)]);
        AssertMatches(
            Collect(automaton.EnumerateOverlapping("abcd"u8)),
            [(3, 1, 2), (0, 0, 4), (1, 1, 4), (2, 2, 4)]);
    }

    /// <summary>
    /// Verifies duplicate byte patterns keep their distinct pattern identifiers.
    /// </summary>
    [Fact]
    public void FindOverlappingRetainsDuplicatePatternIds()
    {
        AhoCorasickAutomaton automaton = Build("foo"u8.ToArray(), "foo"u8.ToArray());

        AssertMatches(
            automaton.FindOverlapping("foobarfoo"u8),
            [(0, 0, 3), (1, 0, 3), (0, 6, 9), (1, 6, 9)]);
    }

    /// <summary>
    /// Verifies empty patterns use upstream standard non-overlapping boundary behavior.
    /// </summary>
    [Fact]
    public void FindAllUsesFirstEmptyPatternAtEveryBoundary()
    {
        AhoCorasickAutomaton automaton = Build("a"u8.ToArray(), []);

        AssertMatches(
            automaton.FindAll("a"u8),
            [(1, 0, 0), (1, 1, 1)]);
    }

    /// <summary>
    /// Verifies overlapping empty patterns are emitted at byte boundaries.
    /// </summary>
    [Fact]
    public void FindOverlappingEmitsEmptyPatternBoundaries()
    {
        AhoCorasickAutomaton automaton = Build([], "a"u8.ToArray(), []);

        AssertMatches(
            automaton.FindOverlapping("a"u8),
            [(0, 0, 0), (2, 0, 0), (1, 0, 1), (0, 1, 1), (2, 1, 1)]);
    }

    /// <summary>
    /// Verifies arbitrary non-UTF-8 bytes are matched without decoding.
    /// </summary>
    [Fact]
    public void FindOverlappingPreservesArbitraryBytes()
    {
        AhoCorasickAutomaton automaton = Build([0xff, 0x00], [0x00]);
        ReadOnlySpan<byte> haystack = [0xaa, 0xff, 0x00];

        AssertMatches(
            automaton.FindOverlapping(haystack),
            [(0, 1, 3), (1, 2, 3)]);
    }

    /// <summary>
    /// Verifies standard anchored search only reports matches starting at the current offset.
    /// </summary>
    [Fact]
    public void FindAllAnchoredRequiresMatchAtCurrentOffset()
    {
        AhoCorasickAutomaton automaton = BuildBoth(
            "abcd"u8.ToArray(),
            "bcd"u8.ToArray(),
            "cd"u8.ToArray(),
            "b"u8.ToArray());

        AssertMatches(
            automaton.FindAllAnchored("abcd"u8),
            [(0, 0, 4)]);
        AssertMatches(
            Collect(automaton.EnumerateAnchored("abcd"u8)),
            [(0, 0, 4)]);
    }

    /// <summary>
    /// Verifies standard anchored search stops when the next offset has no match.
    /// </summary>
    [Fact]
    public void FindAllAnchoredStopsWhenNextOffsetDoesNotMatch()
    {
        AhoCorasickAutomaton automaton = BuildBoth("abcd"u8.ToArray(), "ab"u8.ToArray(), "abc"u8.ToArray());

        AssertMatches(
            automaton.FindAllAnchored("abcd"u8),
            [(1, 0, 2)]);
    }

    /// <summary>
    /// Verifies anchored search exposes the first anchored match.
    /// </summary>
    [Fact]
    public void FindAnchoredReturnsFirstAnchoredMatch()
    {
        AhoCorasickAutomaton automaton = BuildBoth("abcd"u8.ToArray(), "ab"u8.ToArray());

        Assert.Equal(new AhoCorasickMatch(1, 0, 2), automaton.FindAnchored("abcd"u8));
    }

    /// <summary>
    /// Verifies standard anchored empty patterns match every boundary.
    /// </summary>
    [Fact]
    public void FindAllAnchoredUsesFirstEmptyPatternAtEveryBoundary()
    {
        AhoCorasickAutomaton automaton = BuildBoth([], "a"u8.ToArray());

        AssertMatches(
            automaton.FindAllAnchored("aa"u8),
            [(0, 0, 0), (0, 1, 1), (0, 2, 2)]);
    }

    /// <summary>
    /// Verifies leftmost-first keeps the earliest pattern among same-start matches.
    /// </summary>
    [Fact]
    public void LeftmostFirstPrefersEarliestPattern()
    {
        AhoCorasickAutomaton automaton = Build(
            AhoCorasickMatchKind.LeftmostFirst,
            "abcd"u8.ToArray(),
            "ab"u8.ToArray());

        AssertMatches(
            automaton.FindAll("abcd"u8),
            [(0, 0, 4)]);
        AssertMatches(
            Collect(automaton.Enumerate("abcd"u8)),
            [(0, 0, 4)]);
    }

    /// <summary>
    /// Verifies leftmost-first can prefer a shorter pattern by pattern order.
    /// </summary>
    [Fact]
    public void LeftmostFirstUsesPatternOrderBeforeLength()
    {
        AhoCorasickAutomaton automaton = Build(
            AhoCorasickMatchKind.LeftmostFirst,
            "a"u8.ToArray(),
            "ab"u8.ToArray());

        AssertMatches(
            automaton.FindAll("xayabbbz"u8),
            [(0, 1, 2), (0, 3, 4)]);
    }

    /// <summary>
    /// Verifies leftmost-longest chooses the longest match among same-start matches.
    /// </summary>
    [Fact]
    public void LeftmostLongestPrefersLongestPattern()
    {
        AhoCorasickAutomaton automaton = Build(
            AhoCorasickMatchKind.LeftmostLongest,
            "ab"u8.ToArray(),
            "abcd"u8.ToArray());

        AssertMatches(
            automaton.FindAll("abcd"u8),
            [(1, 0, 4)]);
    }

    /// <summary>
    /// Verifies leftmost-longest breaks equal-length ties by pattern order.
    /// </summary>
    [Fact]
    public void LeftmostLongestBreaksLengthTiesByPatternOrder()
    {
        AhoCorasickAutomaton automaton = Build(
            AhoCorasickMatchKind.LeftmostLongest,
            "abcdefg"u8.ToArray(),
            "bcdef"u8.ToArray(),
            "bcde"u8.ToArray());

        AssertMatches(
            automaton.FindAll("abcdef"u8),
            [(1, 1, 6)]);
    }

    /// <summary>
    /// Verifies leftmost modes skip an empty match immediately after a non-empty match.
    /// </summary>
    [Fact]
    public void LeftmostSkipsEmptyMatchImmediatelyAfterNonEmptyMatch()
    {
        AhoCorasickAutomaton automaton = Build(
            AhoCorasickMatchKind.LeftmostLongest,
            [],
            "a"u8.ToArray());

        AssertMatches(
            automaton.FindAll("ab"u8),
            [(1, 0, 1), (0, 2, 2)]);
    }

    /// <summary>
    /// Verifies empty-only leftmost matching advances by one boundary.
    /// </summary>
    [Fact]
    public void LeftmostEmptyOnlyMatchesEveryBoundary()
    {
        AhoCorasickAutomaton automaton = Build(AhoCorasickMatchKind.LeftmostFirst, [], []);

        AssertMatches(
            automaton.FindAll("a"u8),
            [(0, 0, 0), (0, 1, 1)]);
    }

    /// <summary>
    /// Verifies leftmost anchored search does not scan forward to a later start.
    /// </summary>
    [Fact]
    public void LeftmostAnchoredDoesNotScanForward()
    {
        AhoCorasickAutomaton automaton = BuildBoth(
            AhoCorasickMatchKind.LeftmostFirst,
            "ab"u8.ToArray(),
            "a"u8.ToArray());

        AssertMatches(
            automaton.FindAllAnchored("xayabbbz"u8),
            []);
    }

    /// <summary>
    /// Verifies leftmost anchored search continues exactly at each match end.
    /// </summary>
    [Fact]
    public void LeftmostAnchoredContinuesAtMatchEnd()
    {
        AhoCorasickAutomaton automaton = BuildBoth(
            AhoCorasickMatchKind.LeftmostLongest,
            "z"u8.ToArray(),
            "abcdefghi"u8.ToArray(),
            "hz"u8.ToArray(),
            "abcdefgh"u8.ToArray());

        AssertMatches(
            automaton.FindAllAnchored("abcdefghzyz"u8),
            [(3, 0, 8), (0, 8, 9)]);
    }

    /// <summary>
    /// Verifies leftmost-longest anchored search still prefers the longest current match.
    /// </summary>
    [Fact]
    public void LeftmostLongestAnchoredPrefersLongestPattern()
    {
        AhoCorasickAutomaton automaton = BuildBoth(
            AhoCorasickMatchKind.LeftmostLongest,
            "ab"u8.ToArray(),
            "abcd"u8.ToArray());

        AssertMatches(
            automaton.FindAllAnchored("abcd"u8),
            [(1, 0, 4)]);
        AssertMatches(
            Collect(automaton.EnumerateAnchored("abcd"u8)),
            [(1, 0, 4)]);
    }

    /// <summary>
    /// Verifies ASCII case-insensitive search folds pattern and haystack bytes.
    /// </summary>
    [Fact]
    public void AsciiCaseInsensitiveFindsFoldedMatch()
    {
        AhoCorasickAutomaton automaton = BuildAsciiCaseInsensitive(
            AhoCorasickMatchKind.Standard,
            "fOoBaR"u8.ToArray());

        AssertMatches(
            automaton.FindAll("quux foobar baz"u8),
            [(0, 5, 11)]);
    }

    /// <summary>
    /// Verifies ASCII case-insensitive non-overlapping search keeps first duplicate pattern.
    /// </summary>
    [Fact]
    public void AsciiCaseInsensitiveNonOverlappingKeepsFirstDuplicate()
    {
        AhoCorasickAutomaton automaton = BuildAsciiCaseInsensitive(
            AhoCorasickMatchKind.Standard,
            "foo"u8.ToArray(),
            "FOO"u8.ToArray());

        AssertMatches(
            automaton.FindAll("fOo"u8),
            [(0, 0, 3)]);
    }

    /// <summary>
    /// Verifies ASCII case-insensitive overlapping search reports duplicate patterns.
    /// </summary>
    [Fact]
    public void AsciiCaseInsensitiveOverlappingReportsDuplicates()
    {
        AhoCorasickAutomaton automaton = BuildAsciiCaseInsensitive(
            AhoCorasickMatchKind.Standard,
            "FOO"u8.ToArray(),
            "foo"u8.ToArray());

        AssertMatches(
            automaton.FindOverlapping("fOo"u8),
            [(0, 0, 3), (1, 0, 3)]);
    }

    /// <summary>
    /// Verifies ASCII case-insensitive leftmost-first preserves pattern-order semantics.
    /// </summary>
    [Fact]
    public void AsciiCaseInsensitiveLeftmostFirstUsesPatternOrder()
    {
        AhoCorasickAutomaton automaton = BuildAsciiCaseInsensitive(
            AhoCorasickMatchKind.LeftmostFirst,
            "A"u8.ToArray(),
            "ab"u8.ToArray());

        AssertMatches(
            automaton.FindAll("xAyABbbz"u8),
            [(0, 1, 2), (0, 3, 4)]);
    }

    /// <summary>
    /// Verifies ASCII case-insensitive leftmost-longest still prefers longest match.
    /// </summary>
    [Fact]
    public void AsciiCaseInsensitiveLeftmostLongestPrefersLongest()
    {
        AhoCorasickAutomaton automaton = BuildAsciiCaseInsensitive(
            AhoCorasickMatchKind.LeftmostLongest,
            "ab"u8.ToArray(),
            "ABCD"u8.ToArray());

        AssertMatches(
            automaton.FindAll("aBcD"u8),
            [(1, 0, 4)]);
    }

    /// <summary>
    /// Verifies ASCII case-insensitive matching does not fold non-ASCII bytes.
    /// </summary>
    [Fact]
    public void AsciiCaseInsensitiveDoesNotFoldNonAsciiBytes()
    {
        AhoCorasickAutomaton automaton = BuildAsciiCaseInsensitive(
            AhoCorasickMatchKind.Standard,
            [0xc0]);

        AssertMatches(
            automaton.FindAll([0xe0]),
            []);
    }

    /// <summary>
    /// Verifies upstream default start-kind support rejects anchored searches.
    /// </summary>
    [Fact]
    public void DefaultStartKindRejectsAnchoredSearch()
    {
        AhoCorasickAutomaton automaton = Build("foo"u8.ToArray());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => automaton.FindAnchored("foo"u8));
        Assert.Equal(
            "anchored search requested but automaton only supports unanchored searches",
            exception.Message);
        Assert.Throws<InvalidOperationException>(() => automaton.EnumerateAnchored("foo"u8));
    }

    /// <summary>
    /// Verifies anchored-only automatons reject unanchored searches.
    /// </summary>
    [Fact]
    public void AnchoredStartKindRejectsUnanchoredSearch()
    {
        AhoCorasickAutomaton automaton = AhoCorasickAutomaton.Builder()
            .WithStartKind(AhoCorasickStartKind.Anchored)
            .Build(["foo"u8.ToArray()]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => automaton.Find("foo"u8));
        Assert.Equal(
            "unanchored search requested but automaton only supports anchored searches",
            exception.Message);
        Assert.Throws<InvalidOperationException>(() => automaton.Enumerate("foo"u8));
    }

    /// <summary>
    /// Verifies builder options are reflected in the built automaton.
    /// </summary>
    [Fact]
    public void BuilderAppliesSupportedOptions()
    {
        AhoCorasickAutomaton automaton = AhoCorasickAutomaton.Builder()
            .WithMatchKind(AhoCorasickMatchKind.LeftmostLongest)
            .WithStartKind(AhoCorasickStartKind.Both)
            .WithAsciiCaseInsensitive(true)
            .Build(["ab"u8.ToArray(), "ABCD"u8.ToArray()]);

        Assert.Equal(AhoCorasickMatchKind.LeftmostLongest, automaton.MatchKind);
        Assert.Equal(AhoCorasickStartKind.Both, automaton.StartKind);
        Assert.True(automaton.AsciiCaseInsensitive);
        AssertMatches(
            automaton.FindAllAnchored("aBcD"u8),
            [(1, 0, 4)]);
    }

    /// <summary>
    /// Verifies larger automatons use lazy dense transition rows and still search correctly.
    /// </summary>
    [Fact]
    public void SearchesWithLazyDenseTransitionRows()
    {
        byte[][] patterns = Enumerable
            .Range(0, 700)
            .Select(index => System.Text.Encoding.ASCII.GetBytes($"needle-{index:D4}"))
            .ToArray();
        AhoCorasickAutomaton automaton = Build(patterns);

        Assert.Null(GetDenseTransitions(automaton));
        AssertMatches(
            automaton.FindAll("xx needle-0699 yy needle-0007"u8),
            [(699, 3, 14), (7, 18, 29)]);
    }

    /// <summary>
    /// Verifies hot lazy automatons promote to contiguous dense transitions.
    /// </summary>
    [Fact]
    public void PromotesHotLazyRowsToDenseTransitions()
    {
        byte[][] patterns = Enumerable
            .Range(0, 700)
            .Select(index => new[] { (byte)(index >> 8), (byte)index, (byte)'x' })
            .ToArray();
        AhoCorasickAutomaton automaton = Build(patterns);
        byte[] haystack = new byte[patterns.Length * 4];
        for (int index = 0; index < patterns.Length; index++)
        {
            patterns[index].CopyTo(haystack.AsSpan(index * 4, 3));
            haystack[(index * 4) + 3] = (byte)' ';
        }

        Assert.Null(GetDenseTransitions(automaton));
        Assert.Equal(700, automaton.FindAll(haystack).Count);
        Assert.NotNull(GetDenseTransitions(automaton));
    }

    private static object? GetDenseTransitions(AhoCorasickAutomaton automaton)
    {
        return typeof(AhoCorasickAutomaton)
            .GetField("denseTransitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(automaton);
    }

    private static AhoCorasickAutomaton Build(params byte[][] patterns)
    {
        return AhoCorasickAutomaton.Create(patterns);
    }

    private static AhoCorasickAutomaton Build(AhoCorasickMatchKind matchKind, params byte[][] patterns)
    {
        return AhoCorasickAutomaton.Create(patterns, matchKind);
    }

    private static AhoCorasickAutomaton BuildBoth(params byte[][] patterns)
    {
        return AhoCorasickAutomaton.Create(
            patterns,
            AhoCorasickMatchKind.Standard,
            asciiCaseInsensitive: false,
            AhoCorasickStartKind.Both);
    }

    private static AhoCorasickAutomaton BuildBoth(AhoCorasickMatchKind matchKind, params byte[][] patterns)
    {
        return AhoCorasickAutomaton.Create(
            patterns,
            matchKind,
            asciiCaseInsensitive: false,
            AhoCorasickStartKind.Both);
    }

    private static AhoCorasickAutomaton BuildAsciiCaseInsensitive(
        AhoCorasickMatchKind matchKind,
        params byte[][] patterns)
    {
        return AhoCorasickAutomaton.Create(patterns, matchKind, asciiCaseInsensitive: true);
    }

    private static void AssertMatches(
        IReadOnlyList<AhoCorasickMatch> actual,
        IReadOnlyList<(int PatternId, int Start, int End)> expected)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int index = 0; index < expected.Count; index++)
        {
            (int patternId, int start, int end) = expected[index];
            Assert.Equal(patternId, actual[index].PatternId);
            Assert.Equal(start, actual[index].Start);
            Assert.Equal(end, actual[index].End);
        }
    }

    private static List<AhoCorasickMatch> Collect(AhoCorasickEnumerator enumerator)
    {
        var matches = new List<AhoCorasickMatch>();
        while (enumerator.MoveNext())
        {
            matches.Add(enumerator.Current);
        }

        return matches;
    }

    private static List<AhoCorasickMatch> Collect(AhoCorasickOverlappingEnumerator enumerator)
    {
        var matches = new List<AhoCorasickMatch>();
        while (enumerator.MoveNext())
        {
            matches.Add(enumerator.Current);
        }

        return matches;
    }
}
