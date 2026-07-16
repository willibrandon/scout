using System.Text;

namespace Scout;

/// <summary>
/// Verifies authoritative combined-pattern regex search plans.
/// </summary>
public sealed class RegexSearchPlanTests
{
    /// <summary>
    /// Verifies ordinary line-search plans retain the exact literal-set engine for one or many patterns.
    /// </summary>
    [Fact]
    public void UsesLiteralSetForOrdinaryLiteralPatterns()
    {
        var single = RegexSearchPlan.Create(
            ["needle"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));
        var multiple = RegexSearchPlan.Create(
            ["needle"u8.ToArray(), "other"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));

        Assert.NotNull(single);
        Assert.Equal(RegexEngineKind.LiteralSet, single.Matcher.EngineKind);
        Assert.NotNull(multiple);
        Assert.Equal(RegexEngineKind.LiteralSet, multiple.Matcher.EngineKind);
    }

    /// <summary>
    /// Verifies a combined literal set preserves source order at equal starts and advances non-overlapping matches by the selected branch.
    /// </summary>
    [Fact]
    public void LiteralSetPreservesSourceOrderAndNonOverlappingCounts()
    {
        var longerFirst = RegexSearchPlan.Create(
            ["aa"u8.ToArray(), "a"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));
        var shorterFirst = RegexSearchPlan.Create(
            ["a"u8.ToArray(), "aa"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));

        Assert.NotNull(longerFirst);
        Assert.Equal(new RegexMatch(0, 2), longerFirst.Matcher.Find("aaa"u8));
        Assert.Equal(2, longerFirst.Matcher.CountMatches("aaa"u8));
        Assert.NotNull(shorterFirst);
        Assert.Equal(new RegexMatch(0, 1), shorterFirst.Matcher.Find("aaa"u8));
        Assert.Equal(3, shorterFirst.Matcher.CountMatches("aaa"u8));
    }

    /// <summary>
    /// Verifies ASCII case-insensitive ordinary plans retain exact literal-set matching.
    /// </summary>
    [Fact]
    public void UsesLiteralSetForAsciiCaseInsensitivePatterns()
    {
        var plan = RegexSearchPlan.Create(
            ["Needle"u8.ToArray(), "OTHER"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: true));

        Assert.NotNull(plan);
        Assert.Equal(RegexEngineKind.LiteralSet, plan.Matcher.EngineKind);
        Assert.Equal(new RegexMatch(1, 6), plan.Matcher.Find(" needle "u8));
    }

    /// <summary>
    /// Verifies mixed syntax and line or word policy remain on the authoritative general matcher.
    /// </summary>
    [Fact]
    public void KeepsNonLiteralPoliciesOnTheGeneralMatcher()
    {
        var mixed = RegexSearchPlan.Create(
            ["literal"u8.ToArray(), "a+"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));
        var line = RegexSearchPlan.Create(
            ["literal"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false, lineRegexp: true));
        var word = RegexSearchPlan.Create(
            ["literal"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false, wordRegexp: true));

        Assert.NotNull(mixed);
        Assert.NotEqual(RegexEngineKind.LiteralSet, mixed.Matcher.EngineKind);
        Assert.NotNull(line);
        Assert.NotEqual(RegexEngineKind.LiteralSet, line.Matcher.EngineKind);
        Assert.NotNull(word);
        Assert.NotEqual(RegexEngineKind.LiteralSet, word.Matcher.EngineKind);
    }

    /// <summary>
    /// Verifies fallback mode continues to bypass the literal-set specialization.
    /// </summary>
    [Fact]
    public void FallbackModeBypassesLiteralSet()
    {
        using RegexSpecializationModeScope scope =
            RegexSpecializationModeDefaults.Use(RegexSpecializationMode.Fallback);
        var plan = RegexSearchPlan.Create(
            ["needle"u8.ToArray(), "other"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));

        Assert.NotNull(plan);
        Assert.NotEqual(RegexEngineKind.LiteralSet, plan.Matcher.EngineKind);
        Assert.Equal(new RegexMatch(1, 6), plan.Matcher.Find(" needle "u8));
    }

    /// <summary>
    /// Verifies one ordered expression and one matcher represent every source pattern.
    /// </summary>
    [Fact]
    public void CompilesOneOrderedMatcher()
    {
        var plan = RegexSearchPlan.Create(
        [
            "ab"u8.ToArray(),
            "a"u8.ToArray(),
        ],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));

        Assert.NotNull(plan);
        Assert.Equal("(?:ab)|(?:a)", Encoding.UTF8.GetString(plan.Pattern.Span));
        Assert.Equal(2, plan.PatternCount);
        Assert.Equal(new RegexMatch(0, 2), plan.Matcher.Find("ab"u8));
    }

    /// <summary>
    /// Verifies large Unicode line plans are compiled without materializing temporary UTF-8 tries.
    /// </summary>
    [Fact]
    public void BoundsUnicodeLinePlanConstructionAllocations()
    {
        const long AllocationLimit = 256 * 1024;
        byte[][] patterns = [@"\w{5}\s+\w{5}\s+\w{5}"u8.ToArray()];
        _ = RegexSearchPlan.Create(patterns, asciiCaseInsensitive: false);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var plan = RegexSearchPlan.Create(patterns, asciiCaseInsensitive: false);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.NotNull(plan);
        Assert.InRange(allocated, 0, AllocationLimit);
    }

    /// <summary>
    /// Verifies captures are numbered globally across the ordered combined expression.
    /// </summary>
    [Fact]
    public void ExposesGlobalCaptureMetadata()
    {
        var plan = RegexSearchPlan.Create(
        [
            "(a)"u8.ToArray(),
            "(?P<right>b)"u8.ToArray(),
        ],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));

        Assert.NotNull(plan);
        Assert.Equal(2, plan.CaptureCount);
        Assert.Equal(2, plan.CaptureNames["right"]);
        RegexCaptures? captures = plan.Matcher.FindCaptures("b"u8);
        Assert.NotNull(captures);
        Assert.Null(captures.GetGroup(1));
        Assert.Equal(new RegexMatch(0, 1), captures.GetGroup(2));
    }

    /// <summary>
    /// Verifies absolute and effectively absolute anchors are derived from parsed syntax.
    /// </summary>
    [Fact]
    public void ExposesParsedAnchorAndEmptyMatchMetadata()
    {
        var absolutePlan = RegexSearchPlan.Create(
            [@"\Afoo\z"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));
        var scopedPlan = RegexSearchPlan.Create(
            ["(?-m:^)$"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));

        Assert.NotNull(absolutePlan);
        Assert.True(absolutePlan.HasAbsoluteAnchors);
        Assert.False(absolutePlan.HasLineAnchors);
        Assert.True(absolutePlan.HasHaystackAnchors);
        Assert.False(absolutePlan.CanMatchEmpty);
        Assert.NotNull(scopedPlan);
        Assert.False(scopedPlan.HasAbsoluteAnchors);
        Assert.True(scopedPlan.HasLineAnchors);
        Assert.True(scopedPlan.HasHaystackAnchors);
        Assert.True(scopedPlan.CanMatchEmpty);
    }

    /// <summary>
    /// Verifies empty paths that require an end assertion are distinguished from generally nullable paths.
    /// </summary>
    /// <param name="pattern">The regex pattern.</param>
    /// <param name="canMatchEmpty">Whether the pattern can match an empty span.</param>
    /// <param name="requiresEndAssertion">Whether every empty path requires an end assertion.</param>
    [Theory]
    [InlineData(@"\z", true, true)]
    [InlineData("$", true, true)]
    [InlineData(@"foo|\z", true, true)]
    [InlineData(@"(?:bar)?\z", true, true)]
    [InlineData(@"(?:\z)?", true, false)]
    [InlineData(@"\z|", true, false)]
    [InlineData(@"(?s:.*?)", true, false)]
    [InlineData(@"\b", true, false)]
    [InlineData("foo", false, false)]
    public void ClassifiesEndRequiredEmptyPaths(
        string pattern,
        bool canMatchEmpty,
        bool requiresEndAssertion)
    {
        var plan = RegexSearchPlan.Create(
            [Encoding.UTF8.GetBytes(pattern)],
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                multiline: true));

        Assert.NotNull(plan);
        Assert.Equal(canMatchEmpty, plan.CanMatchEmpty);
        Assert.Equal(requiresEndAssertion, plan.EmptyMatchRequiresEndAssertion);
    }

    /// <summary>
    /// Verifies whole-line and whole-word policy is applied around the combined alternation.
    /// </summary>
    [Fact]
    public void AppliesCombinedLineAndWordPolicy()
    {
        var linePlan = RegexSearchPlan.Create(
            ["foo"u8.ToArray(), "bar"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false, lineRegexp: true));
        var wordPlan = RegexSearchPlan.Create(
            ["foo"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false, wordRegexp: true));

        Assert.NotNull(linePlan);
        Assert.Equal(new RegexMatch(0, 3), linePlan.Matcher.Find("foo\nbar"u8));
        Assert.Null(linePlan.Matcher.Find("xfoo\nbarx"u8));
        Assert.NotNull(wordPlan);
        Assert.Equal(new RegexMatch(1, 3), wordPlan.Matcher.Find(" foo "u8));
        Assert.Null(wordPlan.Matcher.Find("xfoo"u8));
    }

    /// <summary>
    /// Verifies the authoritative matcher owns the required-literal prefilter.
    /// </summary>
    [Fact]
    public void CompilesRequiredLiteralInsideAuthoritativeMatcher()
    {
        var plan = RegexSearchPlan.Create(
            [@"\w+GeneratedRecord"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));

        Assert.NotNull(plan);
        Assert.Equal(RegexPrefilterKind.RequiredLiteral, plan.Matcher.PrefilterKind);
        Assert.Equal(new RegexMatch(0, 18), plan.Matcher.Find("abcGeneratedRecord"u8));
    }

    /// <summary>
    /// Verifies plans can be reused only when every semantic option agrees.
    /// </summary>
    [Fact]
    public void ChecksAllCompatibilityOptions()
    {
        var plan = RegexSearchPlan.Create(
            ["foo"u8.ToArray()],
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: true,
                lineRegexp: false,
                wordRegexp: true,
                crlf: true,
                nullData: false,
                multiline: false,
                multilineDotall: false));

        Assert.NotNull(plan);
        Assert.True(plan.IsCompatible(
            asciiCaseInsensitive: true,
            lineRegexp: false,
            wordRegexp: true,
            crlf: true,
            nullData: false,
            multiline: false,
            multilineDotall: false));
        Assert.False(plan.IsCompatible(
            asciiCaseInsensitive: true,
            lineRegexp: false,
            wordRegexp: true,
            crlf: false,
            nullData: false,
            multiline: false,
            multilineDotall: false));
    }

    /// <summary>
    /// Verifies the CRLF line-selection policy permits carriage returns without permitting line feeds.
    /// </summary>
    [Fact]
    public void PreservesOnlyTheCrlfCarriageReturnForLineSelection()
    {
        var plan = RegexSearchPlan.Create(
            [@"\r"u8.ToArray()],
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                crlf: true,
                preserveCrlfCarriageReturn: true));

        Assert.NotNull(plan);
        Assert.True(plan.Options.PreserveCrlfCarriageReturn);
        Assert.Equal(new RegexMatch(1, 1), plan.Matcher.Find("a\r\n"u8));
        Assert.Null(plan.Matcher.Find("a\n"u8));

        var dotPlan = RegexSearchPlan.Create(
            ["."u8.ToArray()],
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                crlf: true,
                preserveCrlfCarriageReturn: true));
        var whitespacePlan = RegexSearchPlan.Create(
            [@"\s"u8.ToArray()],
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                crlf: true,
                preserveCrlfCarriageReturn: true));

        Assert.NotNull(dotPlan);
        Assert.Null(dotPlan.Matcher.Find("\r\n"u8));
        Assert.NotNull(whitespacePlan);
        Assert.Equal(new RegexMatch(0, 1), whitespacePlan.Matcher.Find("\r\n"u8));
    }

    /// <summary>
    /// Verifies line-feed exclusion removes only the excluded class member.
    /// </summary>
    [Fact]
    public void LineFeedExclusionPreservesTheRemainingClassRange()
    {
        var plan = RegexSearchPlan.Create(
            ["[a\n]+"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));

        Assert.NotNull(plan);
        Assert.Equal(new RegexMatch(0, 1), plan.Matcher.Find("a\nb"u8));
        Assert.Null(plan.Matcher.Find("b"u8));
    }

    /// <summary>
    /// Verifies Unicode atoms consume complete scalars while byte-oriented empty matches may begin within them.
    /// </summary>
    [Fact]
    public void PreservesUnicodeAtomsWithoutRestrictingEmptyMatchesToScalarBoundaries()
    {
        var dotPlan = RegexSearchPlan.Create(
            ["."u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));
        var emptyPlan = RegexSearchPlan.Create(
            ["(?:)"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));
        ReadOnlySpan<byte> haystack = "\u00E9"u8;

        Assert.NotNull(dotPlan);
        Assert.NotNull(emptyPlan);
        Assert.Equal(new RegexMatch(0, 2), dotPlan.Matcher.Find(haystack));
        Assert.Equal(new RegexMatch(1, 0), emptyPlan.Matcher.Find(haystack, startAt: 1));
    }

    /// <summary>
    /// Verifies flat capture replay preserves named, optional, zero-width, and CRLF-aware context.
    /// </summary>
    [Fact]
    public void ReplaysOptionalNamedCapturesWithOriginalCrlfContext()
    {
        var plan = RegexSearchPlan.Create(
            [@"(^)(?P<word>foo)(?:-(?P<suffix>bar))?($)"u8.ToArray()],
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                crlf: true));
        byte[] haystack = "x\r\nfoo\r\nz"u8.ToArray();

        Assert.NotNull(plan);
        int[] captureSlots = new int[plan.CaptureSlotCount];
        Array.Fill(captureSlots, 42);

        Assert.True(plan.TryReplayCaptures(haystack, 3, 6, captureSlots));
        Assert.Equal(10, plan.CaptureSlotCount);
        Assert.Equal(2, plan.CaptureNames["word"]);
        Assert.Equal(3, plan.CaptureNames["suffix"]);
        Assert.Equal([3, 6, 3, 3, 3, 6, -1, -1, 6, 6], captureSlots);
    }
}
