using System.Text;

namespace Scout;

/// <summary>
/// Verifies authoritative combined-pattern regex search plans.
/// </summary>
public sealed class RegexSearchPlanTests
{
    /// <summary>
    /// Verifies an empty pattern set produces a non-null authoritative plan that can never match.
    /// </summary>
    [Fact]
    public void EmptyPatternSetCreatesAnExplicitEmptyLanguagePlan()
    {
        var options = new RegexSearchPlanOptions(
            asciiCaseInsensitive: true,
            crlf: true,
            multiline: true,
            multilineDotall: true);

        var plan = RegexSearchPlan.Create([], options);

        Assert.True(plan.IsEmptyLanguage);
        Assert.Equal(0, plan.PatternCount);
        Assert.True(plan.Pattern.IsEmpty);
        Assert.Equal(0, plan.CaptureCount);
        Assert.Empty(plan.CaptureNames);
        Assert.False(plan.CanMatchEmpty);
        Assert.False(plan.EmptyMatchRequiresEndAssertion);
        Assert.True(plan.IsCompatible(
            asciiCaseInsensitive: true,
            lineRegexp: false,
            wordRegexp: false,
            crlf: true,
            nullData: false,
            multiline: true,
            multilineDotall: true));
        Assert.Null(plan.Matcher.Find(ReadOnlySpan<byte>.Empty));
        Assert.Null(plan.Matcher.Find("anything"u8));
        Assert.Equal(0, plan.Matcher.CountMatches("anything"u8));
    }

    /// <summary>
    /// Verifies parsed ordinary line-search plans select the exact literal-set engine for one or many patterns.
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
        Assert.False(plan.IsEmptyLanguage);
        Assert.Equal("(?:ab)|(?:a)", Encoding.UTF8.GetString(plan.Pattern.Span));
        Assert.Equal(2, plan.PatternCount);
        Assert.Equal(new RegexMatch(0, 2), plan.Matcher.Find("ab"u8));
    }

    /// <summary>
    /// Verifies authoritative pattern sets preserve ordered overlap resolution and global captures
    /// without selecting the raw alternation-set engine.
    /// </summary>
    /// <param name="patternCount">The number of ordered source patterns.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void AuthoritativePatternSetsPreserveOrderingOverlapsAndGlobalCaptures(int patternCount)
    {
        byte[][] patterns = new byte[patternCount][];
        for (int index = 0; index < patterns.Length; index++)
        {
            string overlap = index == 0 ? "ab|a" : "a";
            patterns[index] = Encoding.ASCII.GetBytes(
                $"(?<capture{index}>{overlap}|token_{index:D2})");
        }

        var plan = RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                multiline: true));
        RegexCaptures? first = plan.Matcher.FindCaptures("aba"u8);
        int lastPattern = patternCount - 1;
        byte[] lastToken = Encoding.ASCII.GetBytes($"token_{lastPattern:D2}");
        RegexCaptures? last = plan.Matcher.FindCaptures(lastToken);

        Assert.False(plan.IsEmptyLanguage);
        Assert.Equal(patternCount, plan.PatternCount);
        Assert.Equal(patternCount, plan.CaptureCount);
        Assert.NotEqual(RegexEngineKind.AlternationSet, plan.Matcher.EngineKind);
        Assert.False(plan.Matcher.UsesSyntheticCaptureAlternationSet);
        Assert.NotNull(first);
        Assert.Equal(new RegexMatch(0, 2), first.Match);
        Assert.Equal(new RegexMatch(0, 2), first.GetGroup(1));
        Assert.Equal(2, plan.Matcher.CountMatches("aba"u8));
        Assert.NotNull(last);
        Assert.Equal(new RegexMatch(0, lastToken.Length), last.Match);
        Assert.Equal(new RegexMatch(0, lastToken.Length), last.GetGroup(patternCount));
        Assert.Equal(patternCount, plan.CaptureNames[$"capture{lastPattern}"]);
    }

    /// <summary>
    /// Verifies a large exact-literal plan retains the literal-set engine and searches the complete
    /// haystack through its syntax-derived common-prefix scanner.
    /// </summary>
    [Fact]
    public void LargeExactLiteralPlanUsesCommonPrefixLiteralSetWholeHaystackRoute()
    {
        byte[][] patterns = Enumerable.Range(0, 64)
            .Select(static index => Encoding.ASCII.GetBytes($"issue44_absent_pattern_{index:D3}"))
            .ToArray();
        var plan = RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));
        byte[] haystack = "prefix issue44_absent_pattern_063 suffix\n"u8.ToArray();

        long count = LiteralLineSearcher.CountMatchesWithRegexPlan(
            haystack,
            patterns,
            plan,
            asciiCaseInsensitive: false);

        Assert.Equal(RegexEngineKind.LiteralSet, plan.Matcher.EngineKind);
        Assert.False(plan.Matcher.UsesParsedPatternSet);
        Assert.True(plan.Matcher.CanSearchWholeHaystackWithFullMatches);
        Assert.True(plan.Matcher.UsesCommonPrefixLiteralScanner);
        Assert.False(plan.Matcher.UsesSyntheticCaptureAlternationSet);
        Assert.Equal(new RegexMatch(7, 26), plan.Matcher.Find(haystack));
        Assert.Equal(1, count);
    }

    /// <summary>
    /// Verifies the exact common-prefix engine counts matches and observes a late NUL through one
    /// authoritative candidate scan.
    /// </summary>
    [Fact]
    public void LargeExactLiteralPlanFusesMatchCountingAndNulDetection()
    {
        byte[][] patterns = Enumerable.Range(0, 64)
            .Select(static index =>
                Encoding.ASCII.GetBytes($"issue44_absent_pattern_{index:D3}"))
            .ToArray();
        var plan = RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));
        byte[] haystack =
            "issue44_absent_pattern_099 issue44_absent_pattern_063 trailing\0"u8
                .ToArray();

        Assert.True(plan.Matcher.TryCountMatchesAndDetectNul(
            haystack,
            out long count,
            out bool containsNul));
        Assert.Equal(plan.Matcher.CountMatches(haystack), count);
        Assert.Equal(1, count);
        Assert.True(containsNul);
    }

    /// <summary>
    /// Verifies parsed exact-literal pattern sets fuse source-ordered counting and complete NUL
    /// detection without returning to raw-pattern recognition.
    /// </summary>
    [Fact]
    public void ParsedCommonPrefixPatternSetFusesOrderedCountingAndNulDetection()
    {
        byte[][] longerFirstPatterns = CreateCapturedCommonPrefixPatterns(shorterFirst: false);
        byte[][] shorterFirstPatterns = CreateCapturedCommonPrefixPatterns(shorterFirst: true);
        var longerFirst = RegexSearchPlan.Create(
            longerFirstPatterns,
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));
        var shorterFirst = RegexSearchPlan.Create(
            shorterFirstPatterns,
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));
        byte[] haystack = "aaaaaaaaaaaaaaaa\0"u8.ToArray();

        AssertParsedFusedCount(longerFirst, haystack, expectedCount: 1);
        AssertParsedFusedCount(shorterFirst, haystack, expectedCount: 2);
    }

    /// <summary>
    /// Verifies a scope whose execution depends on syntax analysis retains parsed planning.
    /// </summary>
    [Fact]
    public void ScopeDependentPlanningUsesParsedLiteralPath()
    {
        var plan = RegexSearchPlan.CreateScoped(
            ["literal"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false),
            RegexSearchScopePolicy.StandardMultiline);

        Assert.Equal(RegexEngineKind.LiteralSet, plan.Matcher.EngineKind);
        Assert.Equal(new RegexMatch(1, 7), plan.Matcher.Find(" literal "u8));
    }

    /// <summary>
    /// Verifies the literal-set common-prefix route preserves source order for overlapping exact literals.
    /// </summary>
    [Fact]
    public void CommonPrefixLiteralSetPreservesOrderedOverlaps()
    {
        byte[][] longerFirst = Enumerable.Range(0, 64)
            .Select(static index => Encoding.ASCII.GetBytes($"issue44_common_token_{index:D3}"))
            .ToArray();
        longerFirst[0] = "issue44_common_long"u8.ToArray();
        longerFirst[1] = "issue44_common"u8.ToArray();
        byte[][] shorterFirst = longerFirst.Select(static pattern => pattern.ToArray()).ToArray();
        (shorterFirst[0], shorterFirst[1]) = (shorterFirst[1], shorterFirst[0]);
        byte[] haystack = "issue44_common_long issue44_common"u8.ToArray();

        var longerPlan = RegexSearchPlan.Create(
            longerFirst,
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));
        var shorterPlan = RegexSearchPlan.Create(
            shorterFirst,
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));

        Assert.Equal(RegexEngineKind.LiteralSet, longerPlan.Matcher.EngineKind);
        Assert.False(longerPlan.Matcher.UsesParsedPatternSet);
        Assert.True(longerPlan.Matcher.UsesCommonPrefixLiteralScanner);
        Assert.Equal(new RegexMatch(0, longerFirst[0].Length), longerPlan.Matcher.Find(haystack));
        Assert.Equal(2, longerPlan.Matcher.CountMatches(haystack));
        Assert.Equal(
            longerFirst[0].Length + longerFirst[1].Length,
            longerPlan.Matcher.SumMatchSpans(haystack));
        Assert.Equal(RegexEngineKind.LiteralSet, shorterPlan.Matcher.EngineKind);
        Assert.False(shorterPlan.Matcher.UsesParsedPatternSet);
        Assert.True(shorterPlan.Matcher.UsesCommonPrefixLiteralScanner);
        Assert.Equal(new RegexMatch(0, shorterFirst[0].Length), shorterPlan.Matcher.Find(haystack));
        Assert.Equal(2, shorterPlan.Matcher.CountMatches(haystack));
        Assert.Equal(
            shorterFirst[0].Length * 2,
            shorterPlan.Matcher.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies a rejected common-prefix occurrence does not hide a match beginning at an
    /// overlapping occurrence.
    /// </summary>
    [Fact]
    public void CommonPrefixLiteralSetRetriesOverlappingPrefixCandidates()
    {
        byte[][] patterns = Enumerable.Range(0, 16)
            .Select(static index => Encoding.ASCII.GetBytes($"aaaaaaaaX{index:X2}"))
            .ToArray();
        var plan = RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));
        byte[] haystack = "aaaaaaaaaX00"u8.ToArray();

        Assert.True(plan.Matcher.UsesCommonPrefixLiteralScanner);
        Assert.Equal(new RegexMatch(1, patterns[0].Length), plan.Matcher.Find(haystack));
        Assert.Null(plan.Matcher.Find(haystack, startAt: 2));
        Assert.Equal(1, plan.Matcher.CountMatches(haystack));
        Assert.Equal(patterns[0].Length, plan.Matcher.SumMatchSpans(haystack));
    }

    private static void AssertParsedFusedCount(
        RegexSearchPlan plan,
        byte[] haystack,
        long expectedCount)
    {
        Assert.Equal(RegexEngineKind.AlternationSet, plan.Matcher.EngineKind);
        Assert.True(plan.Matcher.UsesParsedPatternSet);
        Assert.True(plan.Matcher.UsesCommonPrefixLiteralScanner);
        Assert.True(plan.Matcher.TryCountMatchesAndDetectNul(
            haystack,
            out long count,
            out bool containsNul));
        Assert.Equal(plan.Matcher.CountMatches(haystack), count);
        Assert.Equal(expectedCount, count);
        Assert.True(containsNul);
    }

    private static byte[][] CreateCapturedCommonPrefixPatterns(bool shorterFirst)
    {
        string[] literals = Enumerable.Range(0, 16)
            .Select(static index => $"aaaaaaaaZ{index:X2}")
            .ToArray();
        literals[0] = shorterFirst ? "aaaaaaaa" : "aaaaaaaaa";
        literals[1] = shorterFirst ? "aaaaaaaaa" : "aaaaaaaa";
        return literals
            .Select(static (literal, index) =>
                Encoding.ASCII.GetBytes($"(?<capture{index}>{literal})"))
            .ToArray();
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
    /// Verifies a NUL-delimited record can contain and match across line feeds without a
    /// line-end candidate treating the first line feed as an uncrossable record boundary.
    /// </summary>
    [Fact]
    public void NullDataAnchoredPatternCanCrossLineFeedsWithinARecord()
    {
        byte[][] patterns =
        [
            @"^[A-Z\n]{79}A$"u8.ToArray(),
            "^Z$"u8.ToArray(),
        ];
        var plan = RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                nullData: true));
        byte[] haystack = new byte[81];
        haystack.AsSpan(0, 80).Fill((byte)'B');
        haystack[40] = (byte)'\n';
        haystack[79] = (byte)'A';
        haystack[80] = 0;
        var sink = new CapturingLineSink();

        bool matched = LiteralLineSearcher.SearchWithRegexPlan(
            haystack,
            patterns,
            plan,
            ref sink,
            nullData: true);

        Assert.True(matched);
        Assert.Equal(new RegexMatch(0, 80), plan.Matcher.Find(haystack.AsSpan(0, 80)));
        Assert.Equal(1UL, sink.MatchedLines);
        Assert.Equal(1, sink.LineNumber);
        Assert.Equal(haystack, sink.Line);
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
