using System.Text;

namespace Scout;

/// <summary>
/// Verifies authoritative combined-pattern regex search plans.
/// </summary>
[TestClass]
public sealed class RegexSearchPlanTests
{
    /// <summary>
    /// Verifies an empty pattern set produces a non-null authoritative plan that can never match.
    /// </summary>
    [TestMethod]
    public void EmptyPatternSetCreatesAnExplicitEmptyLanguagePlan()
    {
        var options = new RegexSearchPlanOptions(
            asciiCaseInsensitive: true,
            crlf: true,
            multiline: true,
            multilineDotall: true);

        var plan = RegexSearchPlan.Create([], options);

        Assert.IsTrue(plan.IsEmptyLanguage);
        Assert.AreEqual(0, plan.PatternCount);
        Assert.IsTrue(plan.Pattern.IsEmpty);
        Assert.AreEqual(0, plan.CaptureCount);
        Assert.IsEmpty(plan.CaptureNames);
        Assert.IsFalse(plan.CanMatchEmpty);
        Assert.IsFalse(plan.EmptyMatchRequiresEndAssertion);
        Assert.IsTrue(plan.IsCompatible(
            asciiCaseInsensitive: true,
            lineRegexp: false,
            wordRegexp: false,
            crlf: true,
            nullData: false,
            multiline: true,
            multilineDotall: true));
        Assert.IsNull(plan.Matcher.Find(ReadOnlySpan<byte>.Empty));
        Assert.IsNull(plan.Matcher.Find("anything"u8));
        Assert.AreEqual(0, plan.Matcher.CountMatches("anything"u8));
    }

    /// <summary>
    /// Verifies parsed ordinary line-search plans select the exact literal-set engine for one or many patterns.
    /// </summary>
    [TestMethod]
    public void UsesLiteralSetForOrdinaryLiteralPatterns()
    {
        var single = RegexSearchPlan.Create(
            ["needle"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));
        var multiple = RegexSearchPlan.Create(
            ["needle"u8.ToArray(), "other"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));

        Assert.IsNotNull(single);
        Assert.AreEqual(RegexEngineKind.LiteralSet, single.Matcher.EngineKind);
        Assert.IsNotNull(multiple);
        Assert.AreEqual(RegexEngineKind.LiteralSet, multiple.Matcher.EngineKind);
    }

    /// <summary>
    /// Verifies a combined literal set preserves source order at equal starts and advances non-overlapping matches by the selected branch.
    /// </summary>
    [TestMethod]
    public void LiteralSetPreservesSourceOrderAndNonOverlappingCounts()
    {
        var longerFirst = RegexSearchPlan.Create(
            ["aa"u8.ToArray(), "a"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));
        var shorterFirst = RegexSearchPlan.Create(
            ["a"u8.ToArray(), "aa"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));

        Assert.IsNotNull(longerFirst);
        Assert.AreEqual(new RegexMatch(0, 2), longerFirst.Matcher.Find("aaa"u8));
        Assert.AreEqual(2, longerFirst.Matcher.CountMatches("aaa"u8));
        Assert.IsNotNull(shorterFirst);
        Assert.AreEqual(new RegexMatch(0, 1), shorterFirst.Matcher.Find("aaa"u8));
        Assert.AreEqual(3, shorterFirst.Matcher.CountMatches("aaa"u8));
    }

    /// <summary>
    /// Verifies ASCII case-insensitive ordinary plans retain exact literal-set matching.
    /// </summary>
    [TestMethod]
    public void UsesLiteralSetForAsciiCaseInsensitivePatterns()
    {
        var plan = RegexSearchPlan.Create(
            ["Needle"u8.ToArray(), "OTHER"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: true));

        Assert.IsNotNull(plan);
        Assert.AreEqual(RegexEngineKind.LiteralSet, plan.Matcher.EngineKind);
        Assert.AreEqual(new RegexMatch(1, 6), plan.Matcher.Find(" needle "u8));
    }

    /// <summary>
    /// Verifies mixed syntax and line or word policy remain on the authoritative general matcher.
    /// </summary>
    [TestMethod]
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

        Assert.IsNotNull(mixed);
        Assert.AreNotEqual(RegexEngineKind.LiteralSet, mixed.Matcher.EngineKind);
        Assert.IsNotNull(line);
        Assert.AreNotEqual(RegexEngineKind.LiteralSet, line.Matcher.EngineKind);
        Assert.IsNotNull(word);
        Assert.AreNotEqual(RegexEngineKind.LiteralSet, word.Matcher.EngineKind);
    }

    /// <summary>
    /// Verifies fallback mode continues to bypass the literal-set specialization.
    /// </summary>
    [TestMethod]
    public void FallbackModeBypassesLiteralSet()
    {
        using RegexSpecializationModeScope scope =
            RegexSpecializationModeDefaults.Use(RegexSpecializationMode.Fallback);
        var plan = RegexSearchPlan.Create(
            ["needle"u8.ToArray(), "other"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));

        Assert.IsNotNull(plan);
        Assert.AreNotEqual(RegexEngineKind.LiteralSet, plan.Matcher.EngineKind);
        Assert.AreEqual(new RegexMatch(1, 6), plan.Matcher.Find(" needle "u8));
    }

    /// <summary>
    /// Verifies one ordered expression and one matcher represent every source pattern.
    /// </summary>
    [TestMethod]
    public void CompilesOneOrderedMatcher()
    {
        var plan = RegexSearchPlan.Create(
        [
            "ab"u8.ToArray(),
            "a"u8.ToArray(),
        ],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));

        Assert.IsNotNull(plan);
        Assert.IsFalse(plan.IsEmptyLanguage);
        Assert.AreEqual("(?:ab)|(?:a)", Encoding.UTF8.GetString(plan.Pattern.Span));
        Assert.AreEqual(2, plan.PatternCount);
        Assert.AreEqual(new RegexMatch(0, 2), plan.Matcher.Find("ab"u8));
    }

    /// <summary>
    /// Verifies authoritative pattern sets preserve ordered overlap resolution and global captures
    /// without selecting the raw alternation-set engine.
    /// </summary>
    /// <param name="patternCount">The number of ordered source patterns.</param>
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(4)]
    [DataRow(8)]
    [DataRow(16)]
    [DataRow(32)]
    [DataRow(64)]
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

        Assert.IsFalse(plan.IsEmptyLanguage);
        Assert.AreEqual(patternCount, plan.PatternCount);
        Assert.AreEqual(patternCount, plan.CaptureCount);
        Assert.AreNotEqual(RegexEngineKind.AlternationSet, plan.Matcher.EngineKind);
        Assert.IsFalse(plan.Matcher.UsesSyntheticCaptureAlternationSet);
        Assert.IsNotNull(first);
        Assert.AreEqual(new RegexMatch(0, 2), first.Match);
        Assert.AreEqual(new RegexMatch(0, 2), first.GetGroup(1));
        Assert.AreEqual(2, plan.Matcher.CountMatches("aba"u8));
        Assert.IsNotNull(last);
        Assert.AreEqual(new RegexMatch(0, lastToken.Length), last.Match);
        Assert.AreEqual(new RegexMatch(0, lastToken.Length), last.GetGroup(patternCount));
        Assert.AreEqual(patternCount, plan.CaptureNames[$"capture{lastPattern}"]);
    }

    /// <summary>
    /// Verifies a large exact-literal plan retains the literal-set engine and searches the complete
    /// haystack through its syntax-derived common-prefix scanner.
    /// </summary>
    [TestMethod]
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

        Assert.AreEqual(RegexEngineKind.LiteralSet, plan.Matcher.EngineKind);
        Assert.IsFalse(plan.Matcher.UsesParsedPatternSet);
        Assert.IsTrue(plan.Matcher.CanSearchWholeHaystackWithFullMatches);
        Assert.IsTrue(plan.Matcher.UsesCommonPrefixLiteralScanner);
        Assert.IsFalse(plan.Matcher.UsesSyntheticCaptureAlternationSet);
        Assert.AreEqual(new RegexMatch(7, 26), plan.Matcher.Find(haystack));
        Assert.AreEqual(1, count);
    }

    /// <summary>
    /// Verifies the exact common-prefix engine counts matches and observes a late NUL through one
    /// authoritative candidate scan.
    /// </summary>
    [TestMethod]
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

        Assert.IsTrue(plan.Matcher.TryCountMatchesAndDetectNul(
            haystack,
            out long count,
            out bool containsNul));
        Assert.AreEqual(plan.Matcher.CountMatches(haystack), count);
        Assert.AreEqual(1, count);
        Assert.IsTrue(containsNul);
    }

    /// <summary>
    /// Verifies parsed exact-literal pattern sets fuse source-ordered counting and complete NUL
    /// detection without returning to raw-pattern recognition.
    /// </summary>
    [TestMethod]
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
    [TestMethod]
    public void ScopeDependentPlanningUsesParsedLiteralPath()
    {
        var plan = RegexSearchPlan.CreateScoped(
            ["literal"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false),
            RegexSearchScopePolicy.StandardMultiline);

        Assert.AreEqual(RegexEngineKind.LiteralSet, plan.Matcher.EngineKind);
        Assert.AreEqual(new RegexMatch(1, 7), plan.Matcher.Find(" literal "u8));
    }

    /// <summary>
    /// Verifies the literal-set common-prefix route preserves source order for overlapping exact literals.
    /// </summary>
    [TestMethod]
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

        Assert.AreEqual(RegexEngineKind.LiteralSet, longerPlan.Matcher.EngineKind);
        Assert.IsFalse(longerPlan.Matcher.UsesParsedPatternSet);
        Assert.IsTrue(longerPlan.Matcher.UsesCommonPrefixLiteralScanner);
        Assert.AreEqual(new RegexMatch(0, longerFirst[0].Length), longerPlan.Matcher.Find(haystack));
        Assert.AreEqual(2, longerPlan.Matcher.CountMatches(haystack));
        Assert.AreEqual(
            longerFirst[0].Length + longerFirst[1].Length,
            longerPlan.Matcher.SumMatchSpans(haystack));
        Assert.AreEqual(RegexEngineKind.LiteralSet, shorterPlan.Matcher.EngineKind);
        Assert.IsFalse(shorterPlan.Matcher.UsesParsedPatternSet);
        Assert.IsTrue(shorterPlan.Matcher.UsesCommonPrefixLiteralScanner);
        Assert.AreEqual(new RegexMatch(0, shorterFirst[0].Length), shorterPlan.Matcher.Find(haystack));
        Assert.AreEqual(2, shorterPlan.Matcher.CountMatches(haystack));
        Assert.AreEqual(
            shorterFirst[0].Length * 2,
            shorterPlan.Matcher.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies a rejected common-prefix occurrence does not hide a match beginning at an
    /// overlapping occurrence.
    /// </summary>
    [TestMethod]
    public void CommonPrefixLiteralSetRetriesOverlappingPrefixCandidates()
    {
        byte[][] patterns = Enumerable.Range(0, 16)
            .Select(static index => Encoding.ASCII.GetBytes($"aaaaaaaaX{index:X2}"))
            .ToArray();
        var plan = RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));
        byte[] haystack = "aaaaaaaaaX00"u8.ToArray();

        Assert.IsTrue(plan.Matcher.UsesCommonPrefixLiteralScanner);
        Assert.AreEqual(new RegexMatch(1, patterns[0].Length), plan.Matcher.Find(haystack));
        Assert.IsNull(plan.Matcher.Find(haystack, startAt: 2));
        Assert.AreEqual(1, plan.Matcher.CountMatches(haystack));
        Assert.AreEqual(patterns[0].Length, plan.Matcher.SumMatchSpans(haystack));
    }

    private static void AssertParsedFusedCount(
        RegexSearchPlan plan,
        byte[] haystack,
        long expectedCount)
    {
        Assert.AreEqual(RegexEngineKind.AlternationSet, plan.Matcher.EngineKind);
        Assert.IsTrue(plan.Matcher.UsesParsedPatternSet);
        Assert.IsTrue(plan.Matcher.UsesCommonPrefixLiteralScanner);
        Assert.IsTrue(plan.Matcher.TryCountMatchesAndDetectNul(
            haystack,
            out long count,
            out bool containsNul));
        Assert.AreEqual(plan.Matcher.CountMatches(haystack), count);
        Assert.AreEqual(expectedCount, count);
        Assert.IsTrue(containsNul);
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
    [TestMethod]
    public void BoundsUnicodeLinePlanConstructionAllocations()
    {
        const long AllocationLimit = 256 * 1024;
        byte[][] patterns = [@"\w{5}\s+\w{5}\s+\w{5}"u8.ToArray()];
        _ = RegexSearchPlan.Create(patterns, asciiCaseInsensitive: false);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var plan = RegexSearchPlan.Create(patterns, asciiCaseInsensitive: false);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.IsNotNull(plan);
        Assert.IsInRange(0, AllocationLimit, allocated);
    }

    /// <summary>
    /// Verifies captures are numbered globally across the ordered combined expression.
    /// </summary>
    [TestMethod]
    public void ExposesGlobalCaptureMetadata()
    {
        var plan = RegexSearchPlan.Create(
        [
            "(a)"u8.ToArray(),
            "(?P<right>b)"u8.ToArray(),
        ],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));

        Assert.IsNotNull(plan);
        Assert.AreEqual(2, plan.CaptureCount);
        Assert.AreEqual(2, plan.CaptureNames["right"]);
        RegexCaptures? captures = plan.Matcher.FindCaptures("b"u8);
        Assert.IsNotNull(captures);
        Assert.IsNull(captures.GetGroup(1));
        Assert.AreEqual(new RegexMatch(0, 1), captures.GetGroup(2));
    }

    /// <summary>
    /// Verifies absolute and effectively absolute anchors are derived from parsed syntax.
    /// </summary>
    [TestMethod]
    public void ExposesParsedAnchorAndEmptyMatchMetadata()
    {
        var absolutePlan = RegexSearchPlan.Create(
            [@"\Afoo\z"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));
        var scopedPlan = RegexSearchPlan.Create(
            ["(?-m:^)$"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));

        Assert.IsNotNull(absolutePlan);
        Assert.IsTrue(absolutePlan.HasAbsoluteAnchors);
        Assert.IsFalse(absolutePlan.HasLineAnchors);
        Assert.IsTrue(absolutePlan.HasHaystackAnchors);
        Assert.IsFalse(absolutePlan.CanMatchEmpty);
        Assert.IsNotNull(scopedPlan);
        Assert.IsFalse(scopedPlan.HasAbsoluteAnchors);
        Assert.IsTrue(scopedPlan.HasLineAnchors);
        Assert.IsTrue(scopedPlan.HasHaystackAnchors);
        Assert.IsTrue(scopedPlan.CanMatchEmpty);
    }

    /// <summary>
    /// Verifies empty paths that require an end assertion are distinguished from generally nullable paths.
    /// </summary>
    /// <param name="pattern">The regex pattern.</param>
    /// <param name="canMatchEmpty">Whether the pattern can match an empty span.</param>
    /// <param name="requiresEndAssertion">Whether every empty path requires an end assertion.</param>
    [TestMethod]
    [DataRow(@"\z", true, true)]
    [DataRow("$", true, true)]
    [DataRow(@"foo|\z", true, true)]
    [DataRow(@"(?:bar)?\z", true, true)]
    [DataRow(@"(?:\z)?", true, false)]
    [DataRow(@"\z|", true, false)]
    [DataRow(@"(?s:.*?)", true, false)]
    [DataRow(@"\b", true, false)]
    [DataRow("foo", false, false)]
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

        Assert.IsNotNull(plan);
        Assert.AreEqual(canMatchEmpty, plan.CanMatchEmpty);
        Assert.AreEqual(requiresEndAssertion, plan.EmptyMatchRequiresEndAssertion);
    }

    /// <summary>
    /// Verifies whole-line and whole-word policy is applied around the combined alternation.
    /// </summary>
    [TestMethod]
    public void AppliesCombinedLineAndWordPolicy()
    {
        var linePlan = RegexSearchPlan.Create(
            ["foo"u8.ToArray(), "bar"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false, lineRegexp: true));
        var wordPlan = RegexSearchPlan.Create(
            ["foo"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false, wordRegexp: true));

        Assert.IsNotNull(linePlan);
        Assert.AreEqual(new RegexMatch(0, 3), linePlan.Matcher.Find("foo\nbar"u8));
        Assert.IsNull(linePlan.Matcher.Find("xfoo\nbarx"u8));
        Assert.IsNotNull(wordPlan);
        Assert.AreEqual(new RegexMatch(1, 3), wordPlan.Matcher.Find(" foo "u8));
        Assert.IsNull(wordPlan.Matcher.Find("xfoo"u8));
    }

    /// <summary>
    /// Verifies the authoritative matcher owns the required-literal prefilter.
    /// </summary>
    [TestMethod]
    public void CompilesRequiredLiteralInsideAuthoritativeMatcher()
    {
        var plan = RegexSearchPlan.Create(
            [@"\w+GeneratedRecord"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));

        Assert.IsNotNull(plan);
        Assert.AreEqual(RegexPrefilterKind.RequiredLiteral, plan.Matcher.PrefilterKind);
        Assert.AreEqual(new RegexMatch(0, 18), plan.Matcher.Find("abcGeneratedRecord"u8));
    }

    /// <summary>
    /// Verifies plans can be reused only when every semantic option agrees.
    /// </summary>
    [TestMethod]
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

        Assert.IsNotNull(plan);
        Assert.IsTrue(plan.IsCompatible(
            asciiCaseInsensitive: true,
            lineRegexp: false,
            wordRegexp: true,
            crlf: true,
            nullData: false,
            multiline: false,
            multilineDotall: false));
        Assert.IsFalse(plan.IsCompatible(
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
    [TestMethod]
    public void PreservesOnlyTheCrlfCarriageReturnForLineSelection()
    {
        var plan = RegexSearchPlan.Create(
            [@"\r"u8.ToArray()],
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                crlf: true,
                preserveCrlfCarriageReturn: true));

        Assert.IsNotNull(plan);
        Assert.IsTrue(plan.Options.PreserveCrlfCarriageReturn);
        Assert.AreEqual(new RegexMatch(1, 1), plan.Matcher.Find("a\r\n"u8));
        Assert.IsNull(plan.Matcher.Find("a\n"u8));

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

        Assert.IsNotNull(dotPlan);
        Assert.IsNull(dotPlan.Matcher.Find("\r\n"u8));
        Assert.IsNotNull(whitespacePlan);
        Assert.AreEqual(new RegexMatch(0, 1), whitespacePlan.Matcher.Find("\r\n"u8));
    }

    /// <summary>
    /// Verifies line-feed exclusion removes only the excluded class member.
    /// </summary>
    [TestMethod]
    public void LineFeedExclusionPreservesTheRemainingClassRange()
    {
        var plan = RegexSearchPlan.Create(
            ["[a\n]+"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));

        Assert.IsNotNull(plan);
        Assert.AreEqual(new RegexMatch(0, 1), plan.Matcher.Find("a\nb"u8));
        Assert.IsNull(plan.Matcher.Find("b"u8));
    }

    /// <summary>
    /// Verifies a NUL-delimited record can contain and match across line feeds without a
    /// line-end candidate treating the first line feed as an uncrossable record boundary.
    /// </summary>
    [TestMethod]
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

        Assert.IsTrue(matched);
        Assert.AreEqual(new RegexMatch(0, 80), plan.Matcher.Find(haystack.AsSpan(0, 80)));
        Assert.AreEqual(1UL, sink.MatchedLines);
        Assert.AreEqual(1, sink.LineNumber);
        Assert.AreSequenceEqual(haystack, sink.Line);
    }

    /// <summary>
    /// Verifies Unicode atoms consume complete scalars while byte-oriented empty matches may begin within them.
    /// </summary>
    [TestMethod]
    public void PreservesUnicodeAtomsWithoutRestrictingEmptyMatchesToScalarBoundaries()
    {
        var dotPlan = RegexSearchPlan.Create(
            ["."u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));
        var emptyPlan = RegexSearchPlan.Create(
            ["(?:)"u8.ToArray()],
            new RegexSearchPlanOptions(asciiCaseInsensitive: false));
        ReadOnlySpan<byte> haystack = "\u00E9"u8;

        Assert.IsNotNull(dotPlan);
        Assert.IsNotNull(emptyPlan);
        Assert.AreEqual(new RegexMatch(0, 2), dotPlan.Matcher.Find(haystack));
        Assert.AreEqual(new RegexMatch(1, 0), emptyPlan.Matcher.Find(haystack, startAt: 1));
    }

    /// <summary>
    /// Verifies flat capture replay preserves named, optional, zero-width, and CRLF-aware context.
    /// </summary>
    [TestMethod]
    public void ReplaysOptionalNamedCapturesWithOriginalCrlfContext()
    {
        var plan = RegexSearchPlan.Create(
            [@"(^)(?P<word>foo)(?:-(?P<suffix>bar))?($)"u8.ToArray()],
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                crlf: true));
        byte[] haystack = "x\r\nfoo\r\nz"u8.ToArray();

        Assert.IsNotNull(plan);
        int[] captureSlots = new int[plan.CaptureSlotCount];
        Array.Fill(captureSlots, 42);

        Assert.IsTrue(plan.TryReplayCaptures(haystack, 3, 6, captureSlots));
        Assert.AreEqual(10, plan.CaptureSlotCount);
        Assert.AreEqual(2, plan.CaptureNames["word"]);
        Assert.AreEqual(3, plan.CaptureNames["suffix"]);
        Assert.AreSequenceEqual<int>([3, 6, 3, 3, 3, 6, -1, -1, 6, 6], captureSlots);
    }
}
