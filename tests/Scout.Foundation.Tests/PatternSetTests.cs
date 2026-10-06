using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Verifies the multi-regex pattern set surface.
/// </summary>
[TestClass]
public sealed class PatternSetTests
{
    /// <summary>
    /// Verifies literal-only patterns use one multi-regex Aho-Corasick accelerator.
    /// </summary>
    [TestMethod]
    public void UsesLiteralMultiRegexAccelerator()
    {
        var set = PatternSet.Compile(
        [
            "abcd"u8.ToArray(),
            "ab"u8.ToArray(),
            @"[[:alpha:]]+\d+"u8.ToArray(),
        ]);

        Assert.IsTrue(set.UsesLiteralAccelerator);
        Assert.IsTrue(set.IsMatch("zzab"u8));
        Assert.AreEqual(new PatternSetMatch(0, new RegexMatch(2, 4)), set.Find("zzabcd abc123"u8));
        Assert.AreSequenceEqual<int>([0, 1, 2], set.MatchingPatternIds("zzabcd abc123"u8));
    }

    /// <summary>
    /// Verifies plain ASCII literal patterns can skip syntax-tree planning.
    /// </summary>
    [TestMethod]
    public void UsesRawLiteralPlanForPlainAsciiPatterns()
    {
        var literalSet = PatternSet.Compile(
        [
            "absentmindedness"u8.ToArray(),
            "Zubeneschamali's"u8.ToArray(),
        ]);
        var regexSet = PatternSet.Compile(["a.c"u8.ToArray()]);

        Assert.IsTrue(literalSet.UsesLiteralAccelerator);
        Assert.IsTrue(literalSet.CanAccelerateEveryPattern);
        Assert.AreEqual(2, literalSet.CountMatches("absentmindedness Zubeneschamali's"u8));
        Assert.IsTrue(regexSet.IsMatch("abc"u8));
    }

    /// <summary>
    /// Verifies large literal-only sets preserve ordered pattern-set matching semantics.
    /// </summary>
    [TestMethod]
    public void LargeLiteralSetCountsNonOverlappingMatches()
    {
        byte[][] patterns = new byte[130][];
        for (int index = 0; index < patterns.Length; index++)
        {
            patterns[index] = System.Text.Encoding.ASCII.GetBytes($"dictionary{index:D3}literal");
        }

        var set = PatternSet.Compile(patterns);
        byte[] haystack = System.Text.Encoding.ASCII.GetBytes(
            "xx dictionary005literal yy dictionary129literal dictionary010literal");

        Assert.IsTrue(set.UsesLiteralAccelerator);
        Assert.AreEqual(new PatternSetMatch(5, new RegexMatch(3, patterns[5].Length)), set.Find(haystack));
        Assert.AreEqual(3, set.CountMatches(haystack));
        Assert.AreEqual(patterns[5].Length + patterns[129].Length + patterns[10].Length, set.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies ASCII word-boundary literals use exact boundary-aware acceleration.
    /// </summary>
    [TestMethod]
    public void UsesBoundaryLiteralAcceleratorForAsciiKeywordPatterns()
    {
        var set = PatternSet.Compile(
        [
            @"(\bif\b)"u8.ToArray(),
            @"([a-zA-Z_][0-9a-zA-Z_]*)"u8.ToArray(),
            "(.)"u8.ToArray(),
        ],
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.IsTrue(set.UsesBoundaryLiteralAccelerator);
        Assert.AreEqual(new PatternSetMatch(0, new RegexMatch(0, 2)), set.Find("if"u8));
        Assert.AreEqual(new PatternSetMatch(1, new RegexMatch(0, 8)), set.Find("if_reset"u8));
        Assert.AreEqual(11, set.SumMatchSpans("if if_reset"u8));

        var mixedLiteralSet = PatternSet.Compile(
        [
            @"(\bif\b)"u8.ToArray(),
            "else"u8.ToArray(),
        ],
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.AreEqual(2, mixedLiteralSet.CountMatches("if else"u8));
    }

    /// <summary>
    /// Verifies Unicode word-boundary literals keep Unicode boundary semantics while using acceleration.
    /// </summary>
    [TestMethod]
    public void UsesBoundaryLiteralAcceleratorForUnicodeKeywordPatterns()
    {
        var set = PatternSet.Compile(
        [
            @"(\bif\b)"u8.ToArray(),
        ],
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("αif if");
        int expectedStart = System.Text.Encoding.UTF8.GetByteCount("αif ");

        Assert.IsTrue(set.UsesBoundaryLiteralAccelerator);
        Assert.AreEqual(new PatternSetMatch(0, new RegexMatch(expectedStart, 2)), set.Find(haystack));
    }

    /// <summary>
    /// Verifies multi-regex required-literal acceleration supports Unicode case folding.
    /// </summary>
    [TestMethod]
    public void UsesRequiredLiteralAcceleratorForUnicodeCaseInsensitivePatterns()
    {
        var set = PatternSet.Compile(
        [
            System.Text.Encoding.UTF8.GetBytes("Шерлок Холмс"),
            System.Text.Encoding.UTF8.GetBytes("Джон Уотсон"),
        ],
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false,
            unicodeClasses: true);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("xxджон уотсон yy");

        Assert.IsTrue(set.UsesRequiredLiteralAccelerator);
        Assert.AreEqual(new PatternSetMatch(1, new RegexMatch(2, System.Text.Encoding.UTF8.GetByteCount("джон уотсон"))), set.Find(haystack));
    }

    /// <summary>
    /// Verifies literal-only patterns use the multi-regex accelerator in case-insensitive mode.
    /// </summary>
    [TestMethod]
    public void UsesLiteralAcceleratorForCaseInsensitivePatterns()
    {
        var set = PatternSet.Compile(
        [
            "Sherlock"u8.ToArray(),
            "Watson"u8.ToArray(),
            "k"u8.ToArray(),
        ],
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false,
            unicodeClasses: true);
        byte[] kelvin = System.Text.Encoding.UTF8.GetBytes("xx\u212A yy");

        Assert.IsTrue(set.UsesLiteralAccelerator);
        Assert.AreEqual(new PatternSetMatch(0, new RegexMatch(2, 8)), set.Find("xxsherlock yy"u8));
        Assert.AreEqual(new PatternSetMatch(1, new RegexMatch(2, 6)), set.Find("xxWATSON yy"u8));
        Assert.AreEqual(new PatternSetMatch(2, new RegexMatch(2, 3)), set.Find(kelvin));
    }

    /// <summary>
    /// Verifies matching pattern identifiers are returned in insertion order.
    /// </summary>
    [TestMethod]
    public void ReturnsMatchingPatternIdsInInsertionOrder()
    {
        var set = PatternSet.Compile(
        [
            "foo"u8.ToArray(),
            @"[[:alpha:]]+\d+"u8.ToArray(),
            "bar"u8.ToArray(),
        ]);

        IReadOnlyList<int> matches = set.MatchingPatternIds("xxfoo bar abc123"u8);

        Assert.AreSequenceEqual<int>([0, 1, 2], matches);
        Assert.IsTrue(set.IsMatch("abc123"u8));
        Assert.IsFalse(set.IsMatch("123"u8));
    }

    /// <summary>
    /// Verifies mixed pattern-set membership agrees with independently compiled regexes.
    /// </summary>
    [TestMethod]
    public void MatchingPatternIdsAgreeWithIndependentRegexesForMixedPatternSet()
    {
        string[] patternTexts =
        [
            "needle",
            "prefix-[a-z]+-suffix",
            @"[0-9]{2}",
            "missing-[a-z]+",
            "prefix",
            "prefix-(?:value|other)-suffix",
        ];
        byte[][] patterns = [.. patternTexts.Select(System.Text.Encoding.UTF8.GetBytes)];
        byte[] haystack = "xxprefix-value-suffix yy needle zz 42"u8.ToArray();
        var set = PatternSet.Compile(patterns);
        int[] expectedPatternIds = patternTexts
            .Select((pattern, patternId) => (Regex: ByteRegex.Compile(pattern), PatternId: patternId))
            .Where(candidate => candidate.Regex.IsMatch(haystack))
            .Select(candidate => candidate.PatternId)
            .ToArray();

        IReadOnlyList<int> actualPatternIds = set.MatchingPatternIds(haystack);

        Assert.IsTrue(set.UsesLiteralAccelerator);
        Assert.IsTrue(set.UsesRequiredLiteralAccelerator);
        Assert.AreSequenceEqual<int>([0, 1, 2, 4, 5], expectedPatternIds);
        Assert.AreSequenceEqual<int>([0, 1, 2, 4, 5], actualPatternIds);
        Assert.AreSequenceEqual(expectedPatternIds, actualPatternIds);
    }

    /// <summary>
    /// Verifies the selected match is the leftmost match across all patterns.
    /// </summary>
    [TestMethod]
    public void FindsLeftmostPatternMatch()
    {
        var set = PatternSet.Compile(
        [
            "bar"u8.ToArray(),
            "foo"u8.ToArray(),
        ]);

        PatternSetMatch? match = set.Find("xxfoo bar"u8);

        Assert.IsTrue(match.HasValue);
        Assert.AreEqual(new PatternSetMatch(1, new RegexMatch(2, 3)), match.Value);
    }

    /// <summary>
    /// Verifies pattern order breaks ties at the same match offset.
    /// </summary>
    [TestMethod]
    public void UsesPatternOrderToBreakTies()
    {
        var first = PatternSet.Compile(
        [
            "ab"u8.ToArray(),
            "a"u8.ToArray(),
        ]);
        var second = PatternSet.Compile(
        [
            "a"u8.ToArray(),
            "ab"u8.ToArray(),
        ]);

        Assert.AreEqual(new PatternSetMatch(0, new RegexMatch(1, 2)), first.Find("zab"u8));
        Assert.AreEqual(new PatternSetMatch(0, new RegexMatch(1, 1)), second.Find("zab"u8));
    }

    /// <summary>
    /// Verifies exact-start matches use pattern order before scanning later offsets.
    /// </summary>
    [TestMethod]
    public void UsesPatternOrderForExactStartMatches()
    {
        var set = PatternSet.Compile(
        [
            "abc"u8.ToArray(),
            "."u8.ToArray(),
        ]);

        Assert.AreEqual(new PatternSetMatch(0, new RegexMatch(1, 3)), set.Find("zabc"u8, startAt: 1));
    }

    /// <summary>
    /// Verifies a failed exact-start candidate does not discard later accelerated matches.
    /// </summary>
    [TestMethod]
    public void FindsLaterLiteralAndBoundaryCandidatesAfterExactStartMiss()
    {
        var literals = PatternSet.Compile(["needle"u8.ToArray(), "other"u8.ToArray()]);
        Assert.IsTrue(literals.UsesLiteralAccelerator);
        Assert.AreEqual(new PatternSetMatch(0, new RegexMatch(2, 6)), literals.Find("xxneedle"u8, startAt: 1));

        var boundaries = PatternSet.Compile(["\\bif\\b"u8.ToArray(), "\\belse\\b"u8.ToArray()]);
        Assert.IsTrue(boundaries.UsesBoundaryLiteralAccelerator);
        Assert.AreEqual(new PatternSetMatch(0, new RegexMatch(3, 2)), boundaries.Find("xx if"u8, startAt: 1));
    }

    /// <summary>
    /// Verifies count helpers use the same non-overlapping iteration semantics as repeated find.
    /// </summary>
    [TestMethod]
    public void CountsNonOverlappingMatchesAndSpans()
    {
        var set = PatternSet.Compile(
        [
            "ab"u8.ToArray(),
            "cd"u8.ToArray(),
        ]);

        Assert.AreEqual(3, set.CountMatches("zabcd ab"u8));
        Assert.AreEqual(6, set.SumMatchSpans("zabcd ab"u8));
        Assert.AreEqual(2, set.CountMatches("zabcd ab"u8, startAt: 3));
        Assert.AreEqual(4, set.SumMatchSpans("zabcd ab"u8, startAt: 3));
    }

    /// <summary>
    /// Verifies byte-covering positive-width lexer sets can sum spans without resolving each token.
    /// </summary>
    [TestMethod]
    public void SumsRemainingBytesForPositiveWidthCoveringSets()
    {
        var set = PatternSet.Compile(
        [
            @"(\r\n|\r|\n)"u8.ToArray(),
            @"([\t\v\f ]+)"u8.ToArray(),
            @"([a-zA-Z_][0-9a-zA-Z_]*)"u8.ToArray(),
            "(.)"u8.ToArray(),
        ],
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        ReadOnlySpan<byte> haystack = "abc \n+\r\nzz"u8;

        Assert.IsTrue(set.CoversEveryByteWithPositiveWidth);
        Assert.IsTrue(set.UsesAnchoredMatcherAccelerator);
        Assert.AreEqual(haystack.Length, set.SumMatchSpans(haystack));
        Assert.AreEqual(haystack.Length - 4, set.SumMatchSpans(haystack, startAt: 4));
        Assert.AreNotEqual(haystack.Length, set.CountMatches(haystack));
    }

    /// <summary>
    /// Verifies the byte-covering anchored matcher preserves lexer pattern ordering.
    /// </summary>
    [TestMethod]
    public void AnchoredMatcherFindsLexerTokensInPatternOrder()
    {
        var set = PatternSet.Compile(
        [
            @"(\r\n|\r|\n)"u8.ToArray(),
            @"([\t\v\f ]+)"u8.ToArray(),
            @"(\bassign\b)"u8.ToArray(),
            @"([0-9]+(?:_[0-9]+)*)"u8.ToArray(),
            @"([a-zA-Z_][0-9a-zA-Z_]*)"u8.ToArray(),
            @"(<<<|>>>|<<|>>)"u8.ToArray(),
            "(.)"u8.ToArray(),
        ],
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.IsTrue(set.UsesAnchoredMatcherAccelerator);
        Assert.AreEqual(new PatternSetMatch(2, new RegexMatch(0, 6)), set.Find("assign x"u8));
        Assert.AreEqual(new PatternSetMatch(4, new RegexMatch(0, 8)), set.Find("assign_x"u8));
        Assert.AreEqual(new PatternSetMatch(3, new RegexMatch(0, 6)), set.Find("12_345+"u8));
        Assert.AreEqual(new PatternSetMatch(5, new RegexMatch(0, 3)), set.Find("<<<x"u8));
        Assert.AreEqual(new PatternSetMatch(0, new RegexMatch(0, 2)), set.Find("\r\nx"u8));
    }

    /// <summary>
    /// Verifies Unicode mode still uses anchored lexer matchers for byte-local token branches.
    /// </summary>
    [TestMethod]
    public void AnchoredMatcherFindsAsciiLexerTokensInUnicodeMode()
    {
        var set = PatternSet.Compile(
        [
            @"(\r\n|\r|\n)"u8.ToArray(),
            @"([\t\v\f ]+)"u8.ToArray(),
            @"(\bassign\b)"u8.ToArray(),
            @"([0-9]+(?:_[0-9]+)*)"u8.ToArray(),
            @"([a-zA-Z_][0-9a-zA-Z_]*)"u8.ToArray(),
            @"(<<<|>>>|<<|>>)"u8.ToArray(),
            "(.)"u8.ToArray(),
        ],
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);

        Assert.IsTrue(set.UsesAnchoredMatcherAccelerator);
        Assert.AreEqual(new PatternSetMatch(2, new RegexMatch(0, 6)), set.Find("assign x"u8));
        Assert.AreEqual(new PatternSetMatch(4, new RegexMatch(0, 8)), set.Find("assign_x"u8));
        Assert.AreEqual(new PatternSetMatch(3, new RegexMatch(0, 6)), set.Find("12_345+"u8));
        Assert.AreEqual(new PatternSetMatch(5, new RegexMatch(0, 3)), set.Find("<<<x"u8));
        Assert.AreEqual(new PatternSetMatch(0, new RegexMatch(0, 2)), set.Find("\r\nx"u8));
    }

    /// <summary>
    /// Verifies pattern sets whose patterns are whole-pattern captures can synthesize captures from the selected match.
    /// </summary>
    [TestMethod]
    public void SynthesizesWholePatternCaptures()
    {
        var set = PatternSet.Compile(
        [
            @"(\bassign\b)"u8.ToArray(),
            @"([0-9]+(?:_[0-9]+)*)"u8.ToArray(),
            @"([a-zA-Z_][0-9a-zA-Z_]*)"u8.ToArray(),
        ],
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "zz assign 123_45 if_reset"u8.ToArray();

        RegexCaptures? first = set.FindCaptures(haystack);
        RegexCaptures? second = set.FindCaptures(haystack, first!.Match.End);

        Assert.IsTrue(set.CanSynthesizeWholePatternCaptures);
        Assert.IsNotNull(first);
        Assert.AreEqual(2, first.GroupCount);
        Assert.AreEqual(2, first.ParticipatingCount());
        Assert.AreEqual(new RegexMatch(0, 2), first.Match);
        Assert.AreEqual(new RegexMatch(0, 2), first.GetGroup(0));
        Assert.AreEqual(new RegexMatch(0, 2), first.GetGroup(1));
        Assert.IsNotNull(second);
        Assert.AreEqual(new RegexMatch(3, 6), second.Match);
        Assert.AreEqual(8, set.CountCaptures(haystack));
        Assert.AreEqual(6, set.CountCaptures(haystack, first.Match.End));
    }

    /// <summary>
    /// Verifies pattern-set whole-pattern capture synthesis is withheld for nested captures.
    /// </summary>
    [TestMethod]
    public void SkipsWholePatternCaptureSynthesisForNestedCaptures()
    {
        var set = PatternSet.Compile(
        [
            @"(([a-z]+))"u8.ToArray(),
            @"([0-9]+)"u8.ToArray(),
        ],
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.IsFalse(set.CanSynthesizeWholePatternCaptures);
        Assert.IsNull(set.FindCaptures("abc"u8));
        Assert.ThrowsExactly<InvalidOperationException>(() => set.CountCaptures("abc"u8));
    }

    /// <summary>
    /// Verifies the byte-coverage shortcut is withheld for gaps and zero-width patterns.
    /// </summary>
    [TestMethod]
    public void DoesNotUseByteCoverageShortcutForGapsOrEmptyPatterns()
    {
        var missingNewline = PatternSet.Compile(
        [
            "(.)"u8.ToArray(),
        ],
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        var emptyFirst = PatternSet.Compile(
        [
            ""u8.ToArray(),
            "(?s:.)"u8.ToArray(),
        ],
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.IsFalse(missingNewline.CoversEveryByteWithPositiveWidth);
        Assert.IsFalse(missingNewline.UsesAnchoredMatcherAccelerator);
        Assert.AreEqual(2, missingNewline.SumMatchSpans("a\nb"u8));
        Assert.IsFalse(emptyFirst.CoversEveryByteWithPositiveWidth);
        Assert.IsFalse(emptyFirst.UsesAnchoredMatcherAccelerator);
        Assert.AreEqual(0, emptyFirst.SumMatchSpans("abc"u8));
    }

    /// <summary>
    /// Verifies an explicit dot-all single-byte fallback covers line terminators.
    /// </summary>
    [TestMethod]
    public void DotAllFallbackCoversEveryByte()
    {
        var set = PatternSet.Compile(
        [
            "(?s:.)"u8.ToArray(),
        ],
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        ReadOnlySpan<byte> haystack = "a\nb\r\nc"u8;

        Assert.IsTrue(set.CoversEveryByteWithPositiveWidth);
        Assert.IsTrue(set.UsesAnchoredMatcherAccelerator);
        Assert.AreEqual(haystack.Length, set.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies count helpers can use the required-literal accelerator for fully covered regex sets.
    /// </summary>
    [TestMethod]
    public void CountsThroughRequiredLiteralAccelerator()
    {
        var set = PatternSet.Compile(
        [
            "foo[0-9]+"u8.ToArray(),
            "bar[0-9]+"u8.ToArray(),
        ]);

        Assert.IsTrue(set.UsesRequiredLiteralAccelerator);
        Assert.AreEqual(3, set.CountMatches("xxfoo1 bar22 foo333"u8));
    }

    /// <summary>
    /// Verifies malformed matching keeps required-literal acceleration for zero-width boundaries.
    /// </summary>
    [TestMethod]
    public void InvalidUtf8BoundaryPatternUsesRequiredLiteralAccelerator()
    {
        const string pattern =
            @"(?u:\b|\B)(?-u:\b)ey[a-zA-Z0-9]{17,}\.ey[a-zA-Z0-9/\\_-]{17,}\.(?:[a-zA-Z0-9/\\_-]{10,}={0,2})?";
        byte[] patternBytes = System.Text.Encoding.UTF8.GetBytes(pattern);
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false,
            matchInvalidUtf8: true);

        Assert.IsTrue(PatternSet.CanPreflightAccelerateEveryPattern([patternBytes], options));

        var set = PatternSet.Compile([patternBytes], options, dfaSizeLimit: null);
        string token = $"ey{new string('A', 17)}.ey{new string('B', 17)}.{new string('C', 10)}";
        byte[] tokenBytes = System.Text.Encoding.ASCII.GetBytes(token);
        byte[] input = GC.AllocateUninitializedArray<byte>(tokenBytes.Length + 1);
        input[0] = 0xFF;
        tokenBytes.CopyTo(input, 1);

        Assert.IsTrue(set.UsesRequiredLiteralAccelerator);
        Assert.IsTrue(set.RequiredLiteralAcceleratorCoversAll);
        Assert.AreEqual(
            new PatternSetMatch(0, new RegexMatch(1, tokenBytes.Length)),
            set.Find(input));
    }

    /// <summary>
    /// Verifies the alternation preflight accepts only sets that can avoid per-branch fallback search.
    /// </summary>
    [TestMethod]
    public void PreflightRequiresEveryPatternToHaveAnAccelerator()
    {
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false,
            crlf: false,
            lineTerminator: (byte)'\n',
            utf8: false,
            unicodeClasses: false);

        Assert.IsTrue(PatternSet.CanPreflightAccelerateEveryPattern(
        [
            "literal"u8.ToArray(),
            "token[0-9]+"u8.ToArray(),
        ], options));
        Assert.IsFalse(PatternSet.CanPreflightAccelerateEveryPattern(
        [
            "token[0-9]+"u8.ToArray(),
            @"\d+"u8.ToArray(),
        ], options));
    }

    /// <summary>
    /// Verifies bounded required-literal windows still include the furthest valid match start.
    /// </summary>
    [TestMethod]
    public void FindsThroughBoundedRequiredLiteralLookBehind()
    {
        var set = PatternSet.Compile(
        [
            ".{0,3}(?:secret|token)[0-9]+"u8.ToArray(),
        ],
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.IsTrue(set.UsesRequiredLiteralAccelerator);
        Assert.AreEqual(new PatternSetMatch(0, new RegexMatch(2, 11)), set.Find("xxabcsecret42"u8));
        Assert.AreEqual(2, set.CountMatches("abcsecret1 zztoken22"u8));
    }

    /// <summary>
    /// Verifies Unicode-enabled required-literal windows include case-folded literal byte expansion.
    /// </summary>
    [TestMethod]
    public void FindsThroughUnicodeCaseFoldedRequiredLiteralLookBehind()
    {
        var set = PatternSet.Compile(
        [
            "(?i)K.{0,2}secret[0-9]+"u8.ToArray(),
        ],
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);

        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("xx\u212A\U0001F600\U0001F600secret7 Kxsecret8");

        Assert.IsTrue(set.UsesRequiredLiteralAccelerator);
        Assert.AreEqual(2, set.CountMatches(haystack));
    }

    /// <summary>
    /// Verifies required-literal verification preserves Unicode scalar class semantics.
    /// </summary>
    [TestMethod]
    public void RequiredLiteralAcceleratorVerifiesUnicodeScalarAtoms()
    {
        var set = PatternSet.Compile(
        [
            @"foo\s+bar"u8.ToArray(),
        ],
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("xxfoo\u2003bar fooXbar");
        int expectedLength = System.Text.Encoding.UTF8.GetByteCount("foo\u2003bar");

        Assert.IsTrue(set.UsesRequiredLiteralAccelerator);
        Assert.AreEqual(new PatternSetMatch(0, new RegexMatch(2, expectedLength)), set.Find(haystack));
        Assert.AreEqual(1, set.CountMatches(haystack));
    }

    /// <summary>
    /// Verifies large pattern sets still use required-literal verification when bounded look-behind analysis is skipped.
    /// </summary>
    [TestMethod]
    public void LargeRequiredLiteralAcceleratorVerifiesUnicodeScalarAtoms()
    {
        byte[][] patterns = Enumerable.Range(0, 32)
            .Select(index => System.Text.Encoding.UTF8.GetBytes($@"p{index:00}foo\s+bar"))
            .ToArray();
        var set = PatternSet.Compile(
            patterns,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("xxp31foo\u2003bar p31fooXbar");
        int expectedLength = System.Text.Encoding.UTF8.GetByteCount("p31foo\u2003bar");

        Assert.IsTrue(set.UsesRequiredLiteralAccelerator);
        Assert.AreEqual(new PatternSetMatch(31, new RegexMatch(2, expectedLength)), set.Find(haystack));
        Assert.AreEqual(1, set.CountMatches(haystack));
    }

    /// <summary>
    /// Verifies required-literal windows can skip starts that violate known first-byte predicates.
    /// </summary>
    [TestMethod]
    public void RequiredLiteralLookBehindHonorsStartBytes()
    {
        var set = PatternSet.Compile(
        [
            "[A-Z].{0,8}password[0-9]+"u8.ToArray(),
        ],
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.IsTrue(set.UsesRequiredLiteralAccelerator);
        Assert.AreEqual(new PatternSetMatch(0, new RegexMatch(18, 10)), set.Find("xxxxxxxxpassword1 Apassword2 zzz B---password333"u8));
        Assert.AreEqual(2, set.CountMatches("xxxxxxxxpassword1 Apassword2 zzz B---password333"u8));
        Assert.AreEqual(25, set.SumMatchSpans("xxxxxxxxpassword1 Apassword2 zzz B---password333"u8));
    }

    /// <summary>
    /// Verifies required-literal candidates can be rejected by anchored byte-pattern guards before automaton validation.
    /// </summary>
    [TestMethod]
    public void RequiredLiteralAcceleratorUsesAnchoredGuards()
    {
        var set = PatternSet.Compile(
        [
            @"\bcio[a-zA-Z0-9]{32}\b"u8.ToArray(),
            @"(?i)client.?secret.{0,10}\b([a-z0-9_-]{24})(?:[^a-z0-9_-]|$)"u8.ToArray(),
            @"(?:(machine\s+[^\s]+)|default)\s+login\s+([^\s]+)\s+password\s+([^\s]+)"u8.ToArray(),
        ],
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.IsTrue(set.UsesRequiredLiteralAccelerator);
        Assert.IsTrue(set.UsesRequiredLiteralGuards);
        Assert.AreEqual(3, set.CountMatches(
            "noise cio0123456789abcdefghijklmnopqrstuv CLIENT-secret = abcdefghijklmnopqrstuvwx; default login alice password sesame"u8));
        Assert.AreEqual(0, set.CountMatches(
            "scio0123456789abcdefghijklmnopqrstuv client nosecret default login alice pass sesame"u8));
    }

    /// <summary>
    /// Verifies anchored guards are built for moderately long required literals that are common enough to need cheap rejection.
    /// </summary>
    [TestMethod]
    public void RequiredLiteralAcceleratorGuardsModeratelyLongLiterals()
    {
        var set = PatternSet.Compile(
        [
            @"(?i)client.?secret.{0,10}\b([a-z0-9_-]{24})(?:[^a-z0-9_-]|$)"u8.ToArray(),
        ],
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.IsTrue(set.UsesRequiredLiteralAccelerator);
        Assert.IsTrue(set.UsesRequiredLiteralGuards);
        Assert.AreEqual(1, set.CountMatches("CLIENT-secret abcdefghijklmnopqrstuvwx;"u8));
        Assert.AreEqual(0, set.CountMatches("client profile without a secret token"u8));
    }

    /// <summary>
    /// Verifies anchored required-literal guards can keep a mandatory prefix when a later optional sequence is too broad to model.
    /// </summary>
    [TestMethod]
    public void RequiredLiteralGuardKeepsSafePrefixBeforeUnsupportedTail()
    {
        var set = PatternSet.Compile(
        [
            @"(?:username|USERNAME|user|USER)[ \t]*=[ \t]*[""']([a-zA-Z0-9.@_\-+]{3,30})[""']\s*[,;]?\s*(?:\s*(?:\#|//)[^\n\r]*[\n\r])*(?:password|pass|PASSWORD|PASS)[ \t]*=[ \t]*[""']([^""']{5,30})[""']"u8.ToArray(),
        ],
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.IsTrue(set.UsesRequiredLiteralAccelerator);
        Assert.IsTrue(set.UsesRequiredLiteralGuards);
        Assert.AreEqual(1, set.CountMatches("user = \"alice\" // keep this assignment\npassword = \"sesame\""u8));
        Assert.AreEqual(0, set.CountMatches("user profile password hint"u8));
    }

    /// <summary>
    /// Verifies an empty set never matches.
    /// </summary>
    [TestMethod]
    public void EmptySetDoesNotMatch()
    {
        var set = PatternSet.Compile([]);

        Assert.AreEqual(0, set.Count);
        Assert.IsFalse(set.IsMatch("anything"u8));
        Assert.IsNull(set.Find("anything"u8));
        Assert.IsEmpty(set.MatchingPatternIds("anything"u8));
    }
}
