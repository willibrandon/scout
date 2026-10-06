
namespace Scout;

/// <summary>
/// Verifies the initial byte-oriented Aho-Corasick port surface.
/// </summary>
[TestClass]
public sealed class AhoCorasickTests
{
    /// <summary>
    /// Verifies standard non-overlapping search reports matches when first seen.
    /// </summary>
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
    public void FindAnchoredReturnsFirstAnchoredMatch()
    {
        AhoCorasickAutomaton automaton = BuildBoth("abcd"u8.ToArray(), "ab"u8.ToArray());

        Assert.AreEqual(new AhoCorasickMatch(1, 0, 2), automaton.FindAnchored("abcd"u8));
    }

    /// <summary>
    /// Verifies standard anchored empty patterns match every boundary.
    /// </summary>
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
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
    [TestMethod]
    public void DefaultStartKindRejectsAnchoredSearch()
    {
        AhoCorasickAutomaton automaton = Build("foo"u8.ToArray());

        InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => automaton.FindAnchored("foo"u8));
        Assert.AreEqual(
            "anchored search requested but automaton only supports unanchored searches",
            exception.Message);
        Assert.ThrowsExactly<InvalidOperationException>(() => automaton.EnumerateAnchored("foo"u8));
    }

    /// <summary>
    /// Verifies anchored-only automatons reject unanchored searches.
    /// </summary>
    [TestMethod]
    public void AnchoredStartKindRejectsUnanchoredSearch()
    {
        AhoCorasickAutomaton automaton = AhoCorasickAutomaton.Builder()
            .WithStartKind(AhoCorasickStartKind.Anchored)
            .Build(["foo"u8.ToArray()]);

        InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => automaton.Find("foo"u8));
        Assert.AreEqual(
            "unanchored search requested but automaton only supports anchored searches",
            exception.Message);
        Assert.ThrowsExactly<InvalidOperationException>(() => automaton.Enumerate("foo"u8));
    }

    /// <summary>
    /// Verifies builder options are reflected in the built automaton.
    /// </summary>
    [TestMethod]
    public void BuilderAppliesSupportedOptions()
    {
        AhoCorasickAutomaton automaton = AhoCorasickAutomaton.Builder()
            .WithMatchKind(AhoCorasickMatchKind.LeftmostLongest)
            .WithStartKind(AhoCorasickStartKind.Both)
            .WithAsciiCaseInsensitive(true)
            .Build(["ab"u8.ToArray(), "ABCD"u8.ToArray()]);

        Assert.AreEqual(AhoCorasickMatchKind.LeftmostLongest, automaton.MatchKind);
        Assert.AreEqual(AhoCorasickStartKind.Both, automaton.StartKind);
        Assert.IsTrue(automaton.AsciiCaseInsensitive);
        AssertMatches(
            automaton.FindAllAnchored("aBcD"u8),
            [(1, 0, 4)]);
    }

    /// <summary>
    /// Verifies small automatons still use eager dense transition tables.
    /// </summary>
    [TestMethod]
    public void SmallAutomatonsUseEagerDenseTransitions()
    {
        byte[][] patterns = Enumerable
            .Range(0, 16)
            .Select(index => System.Text.Encoding.ASCII.GetBytes($"needle-{index:D2}"))
            .ToArray();
        AhoCorasickAutomaton automaton = Build(patterns);

        Assert.IsNotNull(GetAnyDenseTransitions(automaton));
        AssertMatches(
            automaton.FindAll("xx needle-15 yy needle-07"u8),
            [(15, 3, 12), (7, 16, 25)]);
    }

    /// <summary>
    /// Verifies larger automatons use lazy dense transition rows and still search correctly.
    /// </summary>
    [TestMethod]
    public void SearchesWithLazyDenseTransitionRows()
    {
        byte[][] patterns = Enumerable
            .Range(0, 700)
            .Select(index => System.Text.Encoding.ASCII.GetBytes($"needle-{index:D4}"))
            .ToArray();
        AhoCorasickAutomaton automaton = Build(patterns);

        Assert.IsNull(GetAnyDenseTransitions(automaton));
        AssertMatches(
            automaton.FindAll("xx needle-0699 yy needle-0007"u8),
            [(699, 3, 14), (7, 18, 29)]);
    }

    /// <summary>
    /// Verifies hot lazy automatons promote to contiguous dense transitions.
    /// </summary>
    [TestMethod]
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

        Assert.IsNull(GetAnyDenseTransitions(automaton));
        Assert.HasCount(700, automaton.FindAll(haystack));
        Assert.IsNotNull(GetAnyDenseTransitions(automaton));
    }

    /// <summary>
    /// Verifies larger automatons can be promoted before a long scan starts.
    /// </summary>
    [TestMethod]
    public void CanEagerlyPromoteLargeAutomatonToDenseTransitions()
    {
        byte[][] patterns = Enumerable
            .Range(0, 700)
            .Select(index => System.Text.Encoding.ASCII.GetBytes($"needle-{index:D4}"))
            .ToArray();
        AhoCorasickAutomaton automaton = Build(patterns);

        Assert.IsNull(GetAnyDenseTransitions(automaton));
        automaton.EnsureDenseTransitions(maxStates: 4096);

        Assert.IsNotNull(GetAnyDenseTransitions(automaton));
        AssertMatches(
            automaton.FindAll("xx needle-0699 yy needle-0007"u8),
            [(699, 3, 14), (7, 18, 29)]);
    }

    private static object? GetAnyDenseTransitions(AhoCorasickAutomaton automaton)
    {
        Type type = typeof(AhoCorasickAutomaton);
        return type
            .GetField("denseTransitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(automaton) ??
            type
                .GetField("compactDenseTransitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
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
        Assert.HasCount(expected.Count, actual);
        for (int index = 0; index < expected.Count; index++)
        {
            (int patternId, int start, int end) = expected[index];
            Assert.AreEqual(patternId, actual[index].PatternId);
            Assert.AreEqual(start, actual[index].Start);
            Assert.AreEqual(end, actual[index].End);
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
