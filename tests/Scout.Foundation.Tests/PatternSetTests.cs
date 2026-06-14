namespace Scout;

/// <summary>
/// Verifies the multi-regex pattern set surface.
/// </summary>
public sealed class PatternSetTests
{
    /// <summary>
    /// Verifies literal-only patterns use one multi-regex Aho-Corasick accelerator.
    /// </summary>
    [Fact]
    public void UsesLiteralMultiRegexAccelerator()
    {
        var set = PatternSet.Compile(
        [
            "abcd"u8.ToArray(),
            "ab"u8.ToArray(),
            @"[[:alpha:]]+\d+"u8.ToArray(),
        ]);

        Assert.True(set.UsesLiteralAccelerator);
        Assert.True(set.IsMatch("zzab"u8));
        Assert.Equal(new PatternSetMatch(0, new RegexMatch(2, 4)), set.Find("zzabcd abc123"u8));
        Assert.Equal([0, 1, 2], set.MatchingPatternIds("zzabcd abc123"u8));
    }

    /// <summary>
    /// Verifies plain ASCII literal patterns can skip syntax-tree planning.
    /// </summary>
    [Fact]
    public void UsesRawLiteralPlanForPlainAsciiPatterns()
    {
        var literalSet = PatternSet.Compile(
        [
            "absentmindedness"u8.ToArray(),
            "Zubeneschamali's"u8.ToArray(),
        ]);
        var regexSet = PatternSet.Compile(["a.c"u8.ToArray()]);

        Assert.True(literalSet.UsesLiteralAccelerator);
        Assert.True(literalSet.CanAccelerateEveryPattern);
        Assert.Equal(2, literalSet.CountMatches("absentmindedness Zubeneschamali's"u8));
        Assert.True(regexSet.IsMatch("abc"u8));
    }

    /// <summary>
    /// Verifies ASCII word-boundary literals use exact boundary-aware acceleration.
    /// </summary>
    [Fact]
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

        Assert.True(set.UsesBoundaryLiteralAccelerator);
        Assert.Equal(new PatternSetMatch(0, new RegexMatch(0, 2)), set.Find("if"u8));
        Assert.Equal(new PatternSetMatch(1, new RegexMatch(0, 8)), set.Find("if_reset"u8));
        Assert.Equal(11, set.SumMatchSpans("if if_reset"u8));

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

        Assert.Equal(2, mixedLiteralSet.CountMatches("if else"u8));
    }

    /// <summary>
    /// Verifies Unicode word-boundary literals keep Unicode boundary semantics while using acceleration.
    /// </summary>
    [Fact]
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

        Assert.True(set.UsesBoundaryLiteralAccelerator);
        Assert.Equal(new PatternSetMatch(0, new RegexMatch(expectedStart, 2)), set.Find(haystack));
    }

    /// <summary>
    /// Verifies multi-regex required-literal acceleration supports Unicode case folding.
    /// </summary>
    [Fact]
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

        Assert.True(set.UsesRequiredLiteralAccelerator);
        Assert.Equal(new PatternSetMatch(1, new RegexMatch(2, System.Text.Encoding.UTF8.GetByteCount("джон уотсон"))), set.Find(haystack));
    }

    /// <summary>
    /// Verifies literal-only patterns use the multi-regex accelerator in case-insensitive mode.
    /// </summary>
    [Fact]
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

        Assert.True(set.UsesLiteralAccelerator);
        Assert.Equal(new PatternSetMatch(0, new RegexMatch(2, 8)), set.Find("xxsherlock yy"u8));
        Assert.Equal(new PatternSetMatch(1, new RegexMatch(2, 6)), set.Find("xxWATSON yy"u8));
        Assert.Equal(new PatternSetMatch(2, new RegexMatch(2, 3)), set.Find(kelvin));
    }

    /// <summary>
    /// Verifies matching pattern identifiers are returned in insertion order.
    /// </summary>
    [Fact]
    public void ReturnsMatchingPatternIdsInInsertionOrder()
    {
        var set = PatternSet.Compile(
        [
            "foo"u8.ToArray(),
            @"[[:alpha:]]+\d+"u8.ToArray(),
            "bar"u8.ToArray(),
        ]);

        IReadOnlyList<int> matches = set.MatchingPatternIds("xxfoo bar abc123"u8);

        Assert.Equal([0, 1, 2], matches);
        Assert.True(set.IsMatch("abc123"u8));
        Assert.False(set.IsMatch("123"u8));
    }

    /// <summary>
    /// Verifies the selected match is the leftmost match across all patterns.
    /// </summary>
    [Fact]
    public void FindsLeftmostPatternMatch()
    {
        var set = PatternSet.Compile(
        [
            "bar"u8.ToArray(),
            "foo"u8.ToArray(),
        ]);

        PatternSetMatch? match = set.Find("xxfoo bar"u8);

        Assert.True(match.HasValue);
        Assert.Equal(new PatternSetMatch(1, new RegexMatch(2, 3)), match.Value);
    }

    /// <summary>
    /// Verifies pattern order breaks ties at the same match offset.
    /// </summary>
    [Fact]
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

        Assert.Equal(new PatternSetMatch(0, new RegexMatch(1, 2)), first.Find("zab"u8));
        Assert.Equal(new PatternSetMatch(0, new RegexMatch(1, 1)), second.Find("zab"u8));
    }

    /// <summary>
    /// Verifies exact-start matches use pattern order before scanning later offsets.
    /// </summary>
    [Fact]
    public void UsesPatternOrderForExactStartMatches()
    {
        var set = PatternSet.Compile(
        [
            "abc"u8.ToArray(),
            "."u8.ToArray(),
        ]);

        Assert.Equal(new PatternSetMatch(0, new RegexMatch(1, 3)), set.Find("zabc"u8, startAt: 1));
    }

    /// <summary>
    /// Verifies count helpers use the same non-overlapping iteration semantics as repeated find.
    /// </summary>
    [Fact]
    public void CountsNonOverlappingMatchesAndSpans()
    {
        var set = PatternSet.Compile(
        [
            "ab"u8.ToArray(),
            "cd"u8.ToArray(),
        ]);

        Assert.Equal(3, set.CountMatches("zabcd ab"u8));
        Assert.Equal(6, set.SumMatchSpans("zabcd ab"u8));
        Assert.Equal(2, set.CountMatches("zabcd ab"u8, startAt: 3));
        Assert.Equal(4, set.SumMatchSpans("zabcd ab"u8, startAt: 3));
    }

    /// <summary>
    /// Verifies byte-covering positive-width lexer sets can sum spans without resolving each token.
    /// </summary>
    [Fact]
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

        Assert.True(set.CoversEveryByteWithPositiveWidth);
        Assert.True(set.UsesAnchoredMatcherAccelerator);
        Assert.Equal(haystack.Length, set.SumMatchSpans(haystack));
        Assert.Equal(haystack.Length - 4, set.SumMatchSpans(haystack, startAt: 4));
        Assert.NotEqual(haystack.Length, set.CountMatches(haystack));
    }

    /// <summary>
    /// Verifies the byte-covering anchored matcher preserves lexer pattern ordering.
    /// </summary>
    [Fact]
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

        Assert.True(set.UsesAnchoredMatcherAccelerator);
        Assert.Equal(new PatternSetMatch(2, new RegexMatch(0, 6)), set.Find("assign x"u8));
        Assert.Equal(new PatternSetMatch(4, new RegexMatch(0, 8)), set.Find("assign_x"u8));
        Assert.Equal(new PatternSetMatch(3, new RegexMatch(0, 6)), set.Find("12_345+"u8));
        Assert.Equal(new PatternSetMatch(5, new RegexMatch(0, 3)), set.Find("<<<x"u8));
        Assert.Equal(new PatternSetMatch(0, new RegexMatch(0, 2)), set.Find("\r\nx"u8));
    }

    /// <summary>
    /// Verifies Unicode mode still uses anchored lexer matchers for byte-local token branches.
    /// </summary>
    [Fact]
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

        Assert.True(set.UsesAnchoredMatcherAccelerator);
        Assert.Equal(new PatternSetMatch(2, new RegexMatch(0, 6)), set.Find("assign x"u8));
        Assert.Equal(new PatternSetMatch(4, new RegexMatch(0, 8)), set.Find("assign_x"u8));
        Assert.Equal(new PatternSetMatch(3, new RegexMatch(0, 6)), set.Find("12_345+"u8));
        Assert.Equal(new PatternSetMatch(5, new RegexMatch(0, 3)), set.Find("<<<x"u8));
        Assert.Equal(new PatternSetMatch(0, new RegexMatch(0, 2)), set.Find("\r\nx"u8));
    }

    /// <summary>
    /// Verifies pattern sets whose patterns are whole-pattern captures can synthesize captures from the selected match.
    /// </summary>
    [Fact]
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

        Assert.True(set.CanSynthesizeWholePatternCaptures);
        Assert.NotNull(first);
        Assert.Equal(2, first.GroupCount);
        Assert.Equal(2, first.ParticipatingCount());
        Assert.Equal(new RegexMatch(0, 2), first.Match);
        Assert.Equal(new RegexMatch(0, 2), first.GetGroup(0));
        Assert.Equal(new RegexMatch(0, 2), first.GetGroup(1));
        Assert.NotNull(second);
        Assert.Equal(new RegexMatch(3, 6), second.Match);
        Assert.Equal(8, set.CountCaptures(haystack));
        Assert.Equal(6, set.CountCaptures(haystack, first.Match.End));
    }

    /// <summary>
    /// Verifies pattern-set whole-pattern capture synthesis is withheld for nested captures.
    /// </summary>
    [Fact]
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

        Assert.False(set.CanSynthesizeWholePatternCaptures);
        Assert.Null(set.FindCaptures("abc"u8));
        Assert.Throws<InvalidOperationException>(() => set.CountCaptures("abc"u8));
    }

    /// <summary>
    /// Verifies the byte-coverage shortcut is withheld for gaps and zero-width patterns.
    /// </summary>
    [Fact]
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

        Assert.False(missingNewline.CoversEveryByteWithPositiveWidth);
        Assert.False(missingNewline.UsesAnchoredMatcherAccelerator);
        Assert.Equal(2, missingNewline.SumMatchSpans("a\nb"u8));
        Assert.False(emptyFirst.CoversEveryByteWithPositiveWidth);
        Assert.False(emptyFirst.UsesAnchoredMatcherAccelerator);
        Assert.Equal(0, emptyFirst.SumMatchSpans("abc"u8));
    }

    /// <summary>
    /// Verifies an explicit dot-all single-byte fallback covers line terminators.
    /// </summary>
    [Fact]
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

        Assert.True(set.CoversEveryByteWithPositiveWidth);
        Assert.True(set.UsesAnchoredMatcherAccelerator);
        Assert.Equal(haystack.Length, set.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies count helpers can use the required-literal accelerator for fully covered regex sets.
    /// </summary>
    [Fact]
    public void CountsThroughRequiredLiteralAccelerator()
    {
        var set = PatternSet.Compile(
        [
            "foo[0-9]+"u8.ToArray(),
            "bar[0-9]+"u8.ToArray(),
        ]);

        Assert.True(set.UsesRequiredLiteralAccelerator);
        Assert.Equal(3, set.CountMatches("xxfoo1 bar22 foo333"u8));
    }

    /// <summary>
    /// Verifies the alternation preflight accepts only sets that can avoid per-branch fallback search.
    /// </summary>
    [Fact]
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

        Assert.True(PatternSet.CanPreflightAccelerateEveryPattern(
        [
            "literal"u8.ToArray(),
            "token[0-9]+"u8.ToArray(),
        ], options));
        Assert.False(PatternSet.CanPreflightAccelerateEveryPattern(
        [
            "token[0-9]+"u8.ToArray(),
            @"\d+"u8.ToArray(),
        ], options));
    }

    /// <summary>
    /// Verifies bounded required-literal windows still include the furthest valid match start.
    /// </summary>
    [Fact]
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

        Assert.True(set.UsesRequiredLiteralAccelerator);
        Assert.Equal(new PatternSetMatch(0, new RegexMatch(2, 11)), set.Find("xxabcsecret42"u8));
        Assert.Equal(2, set.CountMatches("abcsecret1 zztoken22"u8));
    }

    /// <summary>
    /// Verifies required-literal windows can skip starts that violate known first-byte predicates.
    /// </summary>
    [Fact]
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

        Assert.True(set.UsesRequiredLiteralAccelerator);
        Assert.Equal(new PatternSetMatch(0, new RegexMatch(18, 10)), set.Find("xxxxxxxxpassword1 Apassword2 zzz B---password333"u8));
        Assert.Equal(2, set.CountMatches("xxxxxxxxpassword1 Apassword2 zzz B---password333"u8));
        Assert.Equal(25, set.SumMatchSpans("xxxxxxxxpassword1 Apassword2 zzz B---password333"u8));
    }

    /// <summary>
    /// Verifies an empty set never matches.
    /// </summary>
    [Fact]
    public void EmptySetDoesNotMatch()
    {
        var set = PatternSet.Compile([]);

        Assert.Equal(0, set.Count);
        Assert.False(set.IsMatch("anything"u8));
        Assert.Null(set.Find("anything"u8));
        Assert.Empty(set.MatchingPatternIds("anything"u8));
    }
}
