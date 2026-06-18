namespace Scout;

/// <summary>
/// Verifies the Scout byte-oriented regex automaton.
/// </summary>
public sealed class RegexAutomatonTests
{
    /// <summary>
    /// Verifies leading required literals use the Memmem regex prefilter.
    /// </summary>
    [Fact]
    public void UsesMemmemPrefilterForLeadingRequiredLiteral()
    {
        var automaton = RegexAutomaton.Compile("needle[0-9]+"u8);

        Assert.Equal(RegexPrefilterKind.Memmem, automaton.PrefilterKind);
        Assert.Equal(new RegexMatch(2, 8), automaton.Find("xxneedle42 yy"u8));
        Assert.Null(automaton.Find("xxhaystack42 yy"u8));
    }

    /// <summary>
    /// Verifies fixed-length impossibility checks reject searches before engine execution.
    /// </summary>
    [Fact]
    public void RejectsImpossibleFixedLengthSearches()
    {
        var tooSmallAscii = RegexAutomaton.Compile(
            "\\w{10,}"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            unicodeClasses: false);
        var tooBigAscii = RegexAutomaton.Compile(
            "^\\w{30}$"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            unicodeClasses: false);
        var tooBigUnicode = RegexAutomaton.Compile(
            "^\\w{10}$"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            unicodeClasses: true);
        var goIssue = RegexAutomaton.Compile("^a{2,5}$"u8);
        var unicodeDot = RegexAutomaton.Compile("^.{249}$"u8);
        ReadOnlySpan<byte> tooSmallUnicodePattern = "[\\p{math}&&\\u{10000}-\\u{10FFFF}]{10,}"u8;
        var tooSmallUnicode = RegexAutomaton.Compile(
            tooSmallUnicodePattern,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        RegexSyntaxTree tooSmallUnicodeTree = RegexSyntaxParser.Parse(tooSmallUnicodePattern);
        RegexRepetitionNode tooSmallUnicodeRepeat = Assert.IsType<RegexRepetitionNode>(tooSmallUnicodeTree.Root);
        RegexAtomNode tooSmallUnicodeAtom = Assert.IsType<RegexAtomNode>(tooSmallUnicodeRepeat.Child);
        var unicodeOptions = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        Assert.True(RegexByteClass.RequiresUtf8ScalarMatch(
            tooSmallUnicodeAtom.Kind,
            tooSmallUnicodeAtom.Value.Span,
            utf8: false,
            caseInsensitive: false,
            unicodeClasses: true));
        Assert.True(RegexUtf8ByteCompiler.TryGetUtf8ByteLengthRange(
            tooSmallUnicodeAtom.Kind,
            tooSmallUnicodeAtom.Value.Span,
            unicodeOptions,
            out int unicodeMinimumBytes,
            out int unicodeMaximumBytes));
        Assert.Equal(4, unicodeMinimumBytes);
        Assert.Equal(4, unicodeMaximumBytes);
        byte[] unicodeHaystack = System.Text.Encoding.UTF8.GetBytes("𝛃𝛃𝛃𝛃𝛃𝛃𝛃𝛃𝛃𝛃𝛃");
        byte[] shortUnicodeHaystack = System.Text.Encoding.UTF8.GetBytes("𝛃𝛃𝛃𝛃𝛃𝛃𝛃𝛃𝛃");
        byte[] longAsciiHaystack = new byte[1_000];
        Array.Fill(longAsciiHaystack, (byte)'a');

        Assert.Null(tooSmallAscii.Find("abcdef"u8));
        Assert.Equal(0, tooSmallAscii.CountMatches("abcdef"u8));
        Assert.Equal(0, tooSmallAscii.SumMatchSpans("abcdef"u8));
        Assert.Null(tooBigAscii.Find("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz"u8));
        Assert.Equal(0, tooBigUnicode.CountMatches(unicodeHaystack));
        Assert.Equal(0, goIssue.CountMatches(longAsciiHaystack));
        Assert.Null(goIssue.Find(longAsciiHaystack));
        Assert.Equal(0, unicodeDot.CountMatches(longAsciiHaystack));
        Assert.True(HasLengthGuard(tooSmallUnicode));
        Assert.Equal(0, tooSmallUnicode.CountMatches(shortUnicodeHaystack));
        Assert.Equal(new RegexMatch(0, 3), goIssue.Find("aaa"u8));
        Assert.Equal(1, goIssue.CountMatches("aaa"u8));
        Assert.Equal(0, goIssue.CountMatches("aaa"u8, startAt: 1));
    }

    /// <summary>
    /// Verifies empty patterns use a dedicated zero-length match engine.
    /// </summary>
    [Fact]
    public void UsesEmptyEngineForEmptyPattern()
    {
        var automaton = RegexAutomaton.Compile(""u8);

        Assert.Equal(RegexEngineKind.Empty, GetEngineKind(automaton));
        Assert.True(automaton.IsMatch("abc"u8));
        Assert.Equal(new RegexMatch(0, 0), automaton.Find("abc"u8));
        Assert.Equal(new RegexMatch(2, 0), automaton.Find("abc"u8, startAt: 2));
        Assert.Equal(new RegexMatch(3, 0), automaton.Find("abc"u8, startAt: 99));
        Assert.Equal(new RegexMatch(2, 0), automaton.MatchAt("abc"u8, startAt: 2));
        Assert.Equal(new RegexMatch(3, 0), automaton.FindEarliest("abc"u8, startAt: 3));
        Assert.Equal(new RegexMatch(1, 0), automaton.FindAllKindAt("abc"u8, startAt: 1));
        Assert.Equal([new RegexMatch(1, 0)], automaton.FindOverlappingAt("abc"u8, startAt: 1));
        Assert.Equal(4, automaton.CountMatches("abc"u8));
        Assert.Equal(2, automaton.CountMatches("abc"u8, startAt: 2));
        Assert.Equal(1, automaton.CountMatches("abc"u8, startAt: 99));
        Assert.Equal(4, automaton.CountCaptures("abc"u8));
        Assert.Equal(2, automaton.CountCaptures("abc"u8, startAt: 2));
        Assert.Equal(1, automaton.CountCaptures("abc"u8, startAt: 99));
        Assert.Equal(0, automaton.SumMatchSpans("abc"u8));
    }

    /// <summary>
    /// Verifies missing required byte classes reject searches before engine execution.
    /// </summary>
    [Fact]
    public void RejectsMissingRequiredByteSet()
    {
        var automaton = RegexAutomaton.Compile(@".efghijklmnopq[a-z]+[A-Z]"u8);
        byte[] noUppercase = System.Text.Encoding.ASCII.GetBytes(
            string.Concat(Enumerable.Repeat("bcdefghijklmnopq", 8)));
        byte[] matching = "xefghijklmnopqzA"u8.ToArray();

        Assert.Null(automaton.Find(noUppercase));
        Assert.Null(automaton.FindEarliest(noUppercase, startAt: 0));
        Assert.Equal(0, automaton.CountMatches(noUppercase));
        Assert.Equal(0, automaton.SumMatchSpans(noUppercase));
        Assert.Equal(new RegexMatch(0, 16), automaton.Find(matching));
        Assert.Equal(1, automaton.CountMatches(matching));
        Assert.Equal(16, automaton.SumMatchSpans(matching));
    }

    /// <summary>
    /// Verifies branch-specific required literals reject impossible top-level alternation searches.
    /// </summary>
    [Fact]
    public void RejectsMissingBranchRequiredLiteralSet()
    {
        var automaton = RegexAutomaton.Compile(
            "([a-zA-Z][a-zA-Z0-9]*)://([^ /]+)(/[^ ]*)?|([^ @]+)@([^ @]+)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(RegexPrefilterKind.None, automaton.PrefilterKind);
        Assert.True(automaton.UsesRequiredLiteralAnySetGuard);
        Assert.Null(automaton.Find("plain text without either marker"u8));
        Assert.Equal(0, automaton.CountMatches("plain text without either marker"u8));
        Assert.Equal(0, automaton.SumMatchSpans("plain text without either marker"u8));
        Assert.Equal(new RegexMatch(4, 5), automaton.Find("see a://b now"u8));
        Assert.Equal(new RegexMatch(3, 3), automaton.Find("xx a@b yy"u8));
    }

    /// <summary>
    /// Verifies branch-required literal guards are skipped when any alternation branch has no required literal.
    /// </summary>
    [Fact]
    public void SkipsBranchRequiredLiteralSetWhenAlternativeHasNoLiteral()
    {
        var automaton = RegexAutomaton.Compile(
            "([a-z]+)://x|[0-9]+"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.False(automaton.UsesRequiredLiteralAnySetGuard);
        Assert.Equal(new RegexMatch(0, 3), automaton.Find("123"u8));
    }

    /// <summary>
    /// Verifies top-level literal alternatives use the Aho-Corasick regex prefilter.
    /// </summary>
    [Fact]
    public void UsesAhoCorasickPrefilterForTopLevelLiteralAlternatives()
    {
        var automaton = RegexAutomaton.Compile("foo[0-9]+|bar[a-z]+"u8);

        Assert.Equal(RegexPrefilterKind.AhoCorasick, automaton.PrefilterKind);
        Assert.Equal(new RegexMatch(3, 5), automaton.Find("xx barzz foo42"u8));
        Assert.Equal(new RegexMatch(9, 5), automaton.Find("xx bazzz foo42"u8));
        Assert.Null(automaton.Find("xx bazzz quux"u8));
    }

    /// <summary>
    /// Verifies small literal alternative sets use the Teddy regex prefilter.
    /// </summary>
    [Fact]
    public void UsesTeddyPrefilterForSmallLiteralAlternativeSets()
    {
        var automaton = RegexAutomaton.Compile("cat[0-9]+|dog[a-z]+|emu[A-Z]+"u8);

        Assert.Equal(RegexPrefilterKind.Teddy, automaton.PrefilterKind);
        Assert.Equal(new RegexMatch(9, 5), automaton.Find("xx dog12 emuZZ"u8));
        Assert.Equal(new RegexMatch(3, 5), automaton.Find("xx cat42 emuZZ"u8));
        Assert.Null(automaton.Find("xx cow42 elkZZ"u8));
    }

    /// <summary>
    /// Verifies Teddy keeps ASCII case-insensitive matches when the first byte changes case.
    /// </summary>
    [Fact]
    public void TeddyPrefilterMatchesCaseInsensitiveFirstByteVariants()
    {
        Assert.True(RegexTeddyPrefilter.TryCreate(
            [
                "asia0"u8.ToArray(),
                "akia0"u8.ToArray(),
                "aroa0"u8.ToArray(),
            ],
            asciiCaseInsensitive: true,
            out RegexTeddyPrefilter? prefilter));

        Assert.Equal(2, prefilter!.FindCandidate("xxASIA0123"u8, 0));
        Assert.Equal(2, prefilter.FindCandidate("xxAKIA0123"u8, 0));
        Assert.Equal(-1, prefilter.FindCandidate("xxASIA0123"u8, 3));
    }

    /// <summary>
    /// Verifies pure literal alternations use exact leftmost-first literal-set execution.
    /// </summary>
    [Fact]
    public void UsesLiteralSetEngineForPureLiteralAlternations()
    {
        var shorterFirstAutomaton = RegexAutomaton.Compile("foo|foobar"u8);
        var longerFirstAutomaton = RegexAutomaton.Compile("foobar|foo"u8);
        var caseInsensitiveAutomaton = RegexAutomaton.Compile(
            "Sherlock Holmes|John Watson"u8,
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false,
            unicodeClasses: false);
        var sharedFirstByteAutomaton = RegexAutomaton.Compile("Sherlock|Street"u8);
        RegexMatch? shorterFirst = shorterFirstAutomaton.Find("xxfoobar"u8);
        RegexMatch? longerFirst = longerFirstAutomaton.Find("xxfoobar"u8);
        RegexMatch? caseInsensitive = caseInsensitiveAutomaton.Find("xxjohn watson"u8);

        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(shorterFirstAutomaton));
        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(longerFirstAutomaton));
        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(caseInsensitiveAutomaton));
        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(sharedFirstByteAutomaton));
        Assert.Equal(new RegexMatch(2, 3), shorterFirst);
        Assert.Equal(new RegexMatch(2, 6), longerFirst);
        Assert.Equal(new RegexMatch(2, 11), caseInsensitive);
        Assert.Equal(new RegexMatch(3, 6), sharedFirstByteAutomaton.Find("xx Street Sherlock"u8));
        Assert.Equal(2, shorterFirstAutomaton.CountMatches("foo foobar"u8));
        Assert.Equal(6, shorterFirstAutomaton.SumMatchSpans("foo foobar"u8));
        Assert.Equal(1, shorterFirstAutomaton.CountMatches("foo foobar"u8, startAt: 4));
        Assert.Equal(3, shorterFirstAutomaton.SumMatchSpans("foo foobar"u8, startAt: 4));
        Assert.Equal(2, caseInsensitiveAutomaton.CountMatches("sherlock holmes JOHN WATSON"u8));
        Assert.Equal(26, caseInsensitiveAutomaton.SumMatchSpans("sherlock holmes JOHN WATSON"u8));
        Assert.Equal(2, sharedFirstByteAutomaton.CountMatches("Street Sherlock"u8));
        Assert.Equal(14, sharedFirstByteAutomaton.SumMatchSpans("Street Sherlock"u8));

        RegexCaptures? captures = shorterFirstAutomaton.FindCaptures("xxfoo"u8);
        Assert.NotNull(captures);
        Assert.Equal(1, captures.GroupCount);
        Assert.Equal(new RegexMatch(2, 3), captures.Match);
        Assert.Equal(new RegexMatch(2, 3), captures.GetGroup(0));
    }

    /// <summary>
    /// Verifies grouped pure literal alternations omit the group delimiters from literal-set matches.
    /// </summary>
    [Fact]
    public void LiteralSetEngineCountsGroupedPureLiteralAlternations()
    {
        var automaton = RegexAutomaton.Compile("(?:ab)|(?:cd)"u8);

        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(2, 2), automaton.Find("xxab cd"u8));
        Assert.Equal(new RegexMatch(0, 2), automaton.MatchAt("cd"u8, 0));
        Assert.Equal(2, automaton.CountMatches("ab cd"u8));
        Assert.Equal(4, automaton.SumMatchSpans("ab cd"u8));
    }

    /// <summary>
    /// Verifies small non-ASCII literal alternations preserve byte-oriented leftmost-first matching.
    /// </summary>
    [Fact]
    public void CountsSmallNonAsciiLiteralAlternations()
    {
        var shorterFirstAutomaton = RegexAutomaton.Compile("β|ββ"u8);
        var longerFirstAutomaton = RegexAutomaton.Compile("ββ|β|δε"u8);

        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(shorterFirstAutomaton));
        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(longerFirstAutomaton));
        Assert.Equal(new RegexMatch(1, 2), shorterFirstAutomaton.Find("xββ"u8));
        Assert.Equal(new RegexMatch(1, 4), longerFirstAutomaton.Find("xββδεβ"u8));
        Assert.Equal(3, longerFirstAutomaton.CountMatches("xββδεβ"u8));
        Assert.Equal(10, longerFirstAutomaton.SumMatchSpans("xββδεβ"u8));
    }

    /// <summary>
    /// Verifies small packed literal alternations preserve leftmost-first and non-overlapping count semantics.
    /// </summary>
    [Fact]
    public void CountsPackedLiteralAlternations()
    {
        var longerFirstAutomaton = RegexAutomaton.Compile("abcdzz|abcd|wxyz"u8);
        var shorterFirstAutomaton = RegexAutomaton.Compile("abcd|abcdzz|wxyz"u8);

        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(longerFirstAutomaton));
        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(shorterFirstAutomaton));
        Assert.Equal(new RegexMatch(2, 6), longerFirstAutomaton.Find("xxabcdzz abcd wxyz"u8));
        Assert.Equal(new RegexMatch(2, 4), shorterFirstAutomaton.Find("xxabcdzz abcd wxyz"u8));
        Assert.Equal(3, longerFirstAutomaton.CountMatches("xxabcdzz abcd wxyz"u8));
        Assert.Equal(14, longerFirstAutomaton.SumMatchSpans("xxabcdzz abcd wxyz"u8));
        Assert.Equal(2, longerFirstAutomaton.CountMatches("xxabcdzz abcd wxyz"u8, startAt: 7));
        Assert.Equal(8, longerFirstAutomaton.SumMatchSpans("xxabcdzz abcd wxyz"u8, startAt: 7));
    }

    /// <summary>
    /// Verifies packed literal alternations preserve byte-oriented matching for non-ASCII literals.
    /// </summary>
    [Fact]
    public void CountsPackedNonAsciiLiteralAlternations()
    {
        var automaton = RegexAutomaton.Compile("Шерлок|Джон|Ирен"u8);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("xxШерлок Джон Ирен Шерлок");

        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(2, System.Text.Encoding.UTF8.GetByteCount("Шерлок")), automaton.Find(haystack));
        Assert.Equal(4, automaton.CountMatches(haystack));
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount("ШерлокДжонИренШерлок"), automaton.SumMatchSpans(haystack));
        Assert.Equal(3, automaton.CountMatches(haystack, startAt: System.Text.Encoding.UTF8.GetByteCount("xxШерлок ")));
    }

    /// <summary>
    /// Verifies large literal alternations preserve byte-oriented leftmost-first matching.
    /// </summary>
    [Fact]
    public void CountsLargeLiteralAlternations()
    {
        byte[] longerFirstPattern = BuildLargeLiteralAlternation("abcdzz", "abcd");
        byte[] shorterFirstPattern = BuildLargeLiteralAlternation("abcd", "abcdzz");
        var longerFirstAutomaton = RegexAutomaton.Compile(longerFirstPattern);
        var shorterFirstAutomaton = RegexAutomaton.Compile(shorterFirstPattern);

        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(longerFirstAutomaton));
        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(shorterFirstAutomaton));
        Assert.Equal(new RegexMatch(2, 6), longerFirstAutomaton.Find("xxabcdzz yy w042token"u8));
        Assert.Equal(new RegexMatch(2, 4), shorterFirstAutomaton.Find("xxabcdzz yy w042token"u8));
        Assert.Equal(2, longerFirstAutomaton.CountMatches("xxabcdzz yy w042token"u8));
        Assert.Equal(15, longerFirstAutomaton.SumMatchSpans("xxabcdzz yy w042token"u8));
    }

    /// <summary>
    /// Verifies large Aho literal alternations preserve leftmost-first non-overlapping counts.
    /// </summary>
    [Fact]
    public void CountsLargeAhoLiteralAlternations()
    {
        byte[] longerFirstPattern = BuildLargeAhoLiteralAlternation("abcdzz", "a", "abcd");
        byte[] shorterFirstPattern = BuildLargeAhoLiteralAlternation("a", "abcdzz", "abcd");
        var longerFirstAutomaton = RegexAutomaton.Compile(longerFirstPattern);
        var shorterFirstAutomaton = RegexAutomaton.Compile(shorterFirstPattern);
        byte[] haystack = "xxabcdzz abcdzz"u8.ToArray();

        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(longerFirstAutomaton));
        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(shorterFirstAutomaton));
        Assert.Equal(new RegexMatch(2, 6), longerFirstAutomaton.Find(haystack));
        Assert.Equal(new RegexMatch(2, 1), shorterFirstAutomaton.Find(haystack));
        Assert.Equal(2, longerFirstAutomaton.CountMatches(haystack));
        Assert.Equal(12, longerFirstAutomaton.SumMatchSpans(haystack));
        Assert.Equal(2, shorterFirstAutomaton.CountMatches(haystack));
        Assert.Equal(2, shorterFirstAutomaton.SumMatchSpans(haystack));

        byte[] separated = "xxa yy abcdzz"u8.ToArray();
        Assert.Equal(new RegexMatch(2, 1), longerFirstAutomaton.Find(separated));
        Assert.Equal(2, longerFirstAutomaton.CountMatches(separated));
        Assert.Equal(7, longerFirstAutomaton.SumMatchSpans(separated));
    }

    /// <summary>
    /// Verifies single ASCII case-insensitive literals use literal-set count and span semantics.
    /// </summary>
    [Fact]
    public void LiteralSetCountsSingleAsciiCaseInsensitiveLiteral()
    {
        var automaton = RegexAutomaton.Compile(
            "Sherlock Holmes"u8,
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false,
            unicodeClasses: false);

        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(2, 15), automaton.Find("xxsherlock holmes yy"u8));
        Assert.Equal(2, automaton.CountMatches("sherlock holmes SHERLOCK HOLMES"u8));
        Assert.Equal(30, automaton.SumMatchSpans("sherlock holmes SHERLOCK HOLMES"u8));
        Assert.Equal(1, automaton.CountMatches("sherlock holmes SHERLOCK HOLMES"u8, startAt: 16));
        Assert.Equal(15, automaton.SumMatchSpans("sherlock holmes SHERLOCK HOLMES"u8, startAt: 16));
    }

    /// <summary>
    /// Verifies sparse three-byte literals keep exact literal-set semantics.
    /// </summary>
    [Fact]
    public void LiteralSetCountsSparseThreeByteLiterals()
    {
        var firstByteAnchor = RegexAutomaton.Compile(
            "zqj"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        var middleByteAnchor = RegexAutomaton.Compile(
            "aqj"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "aaaa aqx zqj aqj zaqj"u8.ToArray();

        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(firstByteAnchor));
        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(middleByteAnchor));
        Assert.Equal(new RegexMatch(9, 3), firstByteAnchor.Find(haystack));
        Assert.Equal(new RegexMatch(13, 3), middleByteAnchor.Find(haystack));
        Assert.Equal(1, firstByteAnchor.CountMatches(haystack));
        Assert.Equal(3, firstByteAnchor.SumMatchSpans(haystack));
        Assert.Equal(2, middleByteAnchor.CountMatches(haystack));
        Assert.Equal(6, middleByteAnchor.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies long sparse ASCII literals keep exact literal-set count and span semantics.
    /// </summary>
    [Fact]
    public void LiteralSetCountsLongSparseAsciiLiteral()
    {
        var automaton = RegexAutomaton.Compile(
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xxxxABCDEFGHIJKLMNOPQRSTUVWXYx yyyy ABCDEFGHIJKLMNOPQRSTUVWXYZ zz ABCDEFGHIJKLMNOPQRSTUVWXYZ"u8.ToArray();
        int firstStart = haystack.AsSpan().IndexOf("ABCDEFGHIJKLMNOPQRSTUVWXYZ"u8);
        int secondStart = haystack.AsSpan((firstStart + 1)..).IndexOf("ABCDEFGHIJKLMNOPQRSTUVWXYZ"u8) + firstStart + 1;

        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(firstStart, 26), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(secondStart, 26), automaton.Find(haystack, firstStart + 1));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(52, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies single non-ASCII literals keep exact byte-oriented count and span semantics.
    /// </summary>
    [Fact]
    public void LiteralSetCountsSingleNonAsciiLiteral()
    {
        byte[] literal = System.Text.Encoding.UTF8.GetBytes("Шерлок Холмс");
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("xxШерлок Холмс yy Шерлок Холмс");

        var automaton = RegexAutomaton.Compile(literal);

        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(2, literal.Length), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(2 + literal.Length + 4, literal.Length), automaton.Find(haystack, startAt: 3));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(1, automaton.CountMatches(haystack, startAt: 3));
        Assert.Equal(literal.Length * 2, automaton.SumMatchSpans(haystack));
        Assert.Equal(literal.Length, automaton.SumMatchSpans(haystack, startAt: 3));
    }

    /// <summary>
    /// Verifies Unicode-aware literal-set execution uses simple case folding while preserving haystack span lengths.
    /// </summary>
    [Fact]
    public void LiteralSetCountsUnicodeCaseInsensitiveLiterals()
    {
        var automaton = RegexAutomaton.Compile(
            "k|Шерлок Холмс"u8,
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false,
            unicodeClasses: true);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("xx\u212A yy шерлок холмс");

        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(2, 3), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(9, 23), automaton.Find(haystack, startAt: 5));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(1, automaton.CountMatches(haystack, startAt: 5));
        Assert.Equal(26, automaton.SumMatchSpans(haystack));
        Assert.Equal(23, automaton.SumMatchSpans(haystack, startAt: 5));
    }

    /// <summary>
    /// Verifies common Cyrillic Unicode case folds stay on the literal-set fast path.
    /// </summary>
    [Fact]
    public void LiteralSetCountsCommonCyrillicCaseInsensitiveAlternation()
    {
        byte[] pattern = System.Text.Encoding.UTF8.GetBytes(
            "Шерлок Холмс|Джон Уотсон|Ирен Адлер|инспектор Лестрейд|профессор Мориарти");
        string[] matches =
        [
            "шерлок холмс",
            "ДЖОН УОТСОН",
            "ирен адлер",
            "ИНСПЕКТОР ЛЕСТРЕЙД",
            "ПРОФЕССОР МОРИАРТИ",
        ];
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes(string.Join(" / ", matches));
        var automaton = RegexAutomaton.Compile(
            pattern,
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false,
            unicodeClasses: true);

        long expectedSpanSum = 0;
        for (int index = 0; index < matches.Length; index++)
        {
            expectedSpanSum += System.Text.Encoding.UTF8.GetByteCount(matches[index]);
        }

        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, System.Text.Encoding.UTF8.GetByteCount(matches[0])), automaton.Find(haystack));
        Assert.Equal(matches.Length, automaton.CountMatches(haystack));
        Assert.Equal(expectedSpanSum, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies the common Cyrillic literal-set scalar prefilter honors prefix case folds.
    /// </summary>
    [Fact]
    public void LiteralSetFindsCommonCyrillicCaseInsensitiveAlternationWithScalarPrefilter()
    {
        byte[] pattern = System.Text.Encoding.UTF8.GetBytes("Шер|Джо|Ире");
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("шер");
        var automaton = RegexAutomaton.Compile(
            pattern,
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false,
            unicodeClasses: true);

        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, haystack.Length), automaton.Find(haystack));
        Assert.Equal(1, automaton.CountMatches(haystack));
        Assert.Equal(haystack.Length, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies inline no-Unicode mode keeps literal-set case folding ASCII-only.
    /// </summary>
    [Fact]
    public void LiteralSetHonorsInlineNoUnicodeCaseMode()
    {
        var longS = RegexAutomaton.Compile(
            "(?-u:s)"u8,
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: true,
            unicodeClasses: true);
        var delta = RegexAutomaton.Compile(
            System.Text.Encoding.UTF8.GetBytes("(?-u:Δ)"),
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: true,
            unicodeClasses: true);

        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(longS));
        Assert.Null(longS.Find(System.Text.Encoding.UTF8.GetBytes("ſ")));
        Assert.Null(delta.Find(System.Text.Encoding.UTF8.GetBytes("δ")));
        Assert.Equal(new RegexMatch(0, 2), delta.Find(System.Text.Encoding.UTF8.GetBytes("Δ")));
    }

    /// <summary>
    /// Verifies regexes without explicit captures can synthesize the whole-match capture result.
    /// </summary>
    [Fact]
    public void FindCapturesSynthesizesWholeMatchForNoCaptureRegex()
    {
        var automaton = RegexAutomaton.Compile("[a-z]+[0-9]+"u8);

        RegexCaptures? captures = automaton.FindCaptures("--abc123"u8);

        Assert.NotNull(captures);
        Assert.Equal(1, captures.GroupCount);
        Assert.Equal(new RegexMatch(2, 6), captures.Match);
        Assert.Equal(new RegexMatch(2, 6), captures.GetGroup(0));
        Assert.Equal(1, automaton.CountCaptures("--abc123"u8));
    }

    /// <summary>
    /// Verifies regexes that are one whole-pattern capture can synthesize that capture from the match span.
    /// </summary>
    [Fact]
    public void FindCapturesSynthesizesWholePatternCapture()
    {
        var automaton = RegexAutomaton.Compile("([a-z]+[0-9]+)"u8);

        RegexCaptures? captures = automaton.FindCaptures("--abc123"u8);

        Assert.True(automaton.UsesWholePatternCaptureSynthesis);
        Assert.NotNull(captures);
        Assert.Equal(2, captures.GroupCount);
        Assert.Equal(2, captures.ParticipatingCount());
        Assert.Equal(new RegexMatch(2, 6), captures.Match);
        Assert.Equal(new RegexMatch(2, 6), captures.GetGroup(0));
        Assert.Equal(new RegexMatch(2, 6), captures.GetGroup(1));
        Assert.Equal(2, automaton.CountCaptures("--abc123"u8));
        Assert.Equal(0, automaton.CountCaptures("--abc123"u8, captures.Match.End));
    }

    /// <summary>
    /// Verifies nested captures still use the regular capture engine.
    /// </summary>
    [Fact]
    public void WholePatternCaptureSynthesisSkipsNestedCaptures()
    {
        var automaton = RegexAutomaton.Compile("(([a-z]+)[0-9]+)"u8);

        RegexCaptures? captures = automaton.FindCaptures("--abc123"u8);

        Assert.False(automaton.UsesWholePatternCaptureSynthesis);
        Assert.NotNull(captures);
        Assert.Equal(3, captures.GroupCount);
        Assert.Equal(new RegexMatch(2, 6), captures.GetGroup(1));
        Assert.Equal(new RegexMatch(2, 3), captures.GetGroup(2));
    }

    /// <summary>
    /// Verifies word-whitespace-literal suffix patterns use literal-driven matching.
    /// </summary>
    [Fact]
    public void WordWhitespaceLiteralEngineReportsSuffixMatches()
    {
        var automaton = RegexAutomaton.Compile(
            @"\w+\s+Holmes"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "Sherlock Holmes and Mycroft Holmesian"u8.ToArray();

        RegexMatch? first = automaton.Find(haystack);
        RegexMatch? second = automaton.Find(haystack, first!.Value.End);

        Assert.Equal(RegexEngineKind.WordWhitespaceLiteral, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 15), first);
        Assert.Equal(new RegexMatch(20, 14), second);
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(29, automaton.SumMatchSpans(haystack));
        Assert.Equal(new RegexMatch(3, 12), automaton.Find(haystack, 3));
    }

    /// <summary>
    /// Verifies word-whitespace-literal inner patterns honor explicit word boundaries.
    /// </summary>
    [Fact]
    public void WordWhitespaceLiteralEngineReportsBoundaryInnerMatches()
    {
        var automaton = RegexAutomaton.Compile(
            @"\b\w+\s+Holmes\s+\w+\b"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "Sherlock Holmes said and notHolmes value and Mycroft Holmesian value"u8.ToArray();

        RegexMatch? match = automaton.Find(haystack);

        Assert.Equal(RegexEngineKind.WordWhitespaceLiteral, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 20), match);
        Assert.Equal(1, automaton.CountMatches(haystack));
        Assert.Equal(20, automaton.SumMatchSpans(haystack));
        Assert.Null(automaton.Find("notHolmes value"u8));
        Assert.Null(automaton.Find("Sherlock Holmium said"u8));

        byte[] prefixCollisionHaystack = "Sherlock Holmium said Sherlock Holmes said"u8.ToArray();
        int expectedStart = "Sherlock Holmium said "u8.Length;
        Assert.Equal(new RegexMatch(expectedStart, 20), automaton.Find(prefixCollisionHaystack));
    }

    /// <summary>
    /// Verifies Unicode word-whitespace-literal inner patterns use literal-driven matching.
    /// </summary>
    [Fact]
    public void WordWhitespaceLiteralEngineReportsUnicodeBoundaryInnerMatches()
    {
        var automaton = RegexAutomaton.Compile(
            @"\b\w+\s+Холмс\s+\w+\b"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = "Шерлок Холмс сказал и неХолмс значение и Майкрофт Холмс пришел"u8.ToArray();

        RegexMatch? first = automaton.Find(haystack);
        RegexMatch? second = automaton.Find(haystack, first!.Value.End);

        Assert.Equal(RegexEngineKind.WordWhitespaceLiteral, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 36), first);
        Assert.Equal(new RegexMatch(75, 40), second);
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(76, automaton.SumMatchSpans(haystack));
        Assert.Equal(new RegexMatch(75, 40), automaton.Find(haystack, 4));
        Assert.Null(automaton.Find("неХолмс значение"u8));
    }

    /// <summary>
    /// Verifies bounded letter suffixes between whitespace use suffix-driven matching.
    /// </summary>
    [Fact]
    public void BoundedLetterSuffixWhitespaceEngineCountsNonOverlappingWords()
    {
        var automaton = RegexAutomaton.Compile(
            @"\s[a-zA-Z]{0,12}ing\s"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xx walking  running  tooling  abcdefghijkling abcdefghijklmnoing wing\t"u8.ToArray();

        RegexMatch? first = automaton.Find(haystack);
        RegexMatch? second = automaton.Find(haystack, first!.Value.End);
        RegexMatch? third = automaton.Find(haystack, second!.Value.End);

        Assert.Equal(RegexEngineKind.BoundedLetterSuffixWhitespace, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(2, 9), first);
        Assert.Equal(new RegexMatch(11, 9), second);
        Assert.Equal(new RegexMatch(20, 9), third);
        Assert.Equal(new RegexMatch(11, 9), automaton.Find(haystack, startAt: 3));
        Assert.Equal(new RegexMatch(0, 9), automaton.MatchAt(" walking "u8, 0));
        Assert.Equal(new RegexMatch(0, 6), automaton.MatchAt(" xing\n"u8, 0));
        Assert.Null(automaton.MatchAt(" abcdefghijklmnoing "u8, 0));
        Assert.Equal(5, automaton.CountMatches(haystack));
        Assert.Equal(50, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies equivalent capture threads keep nested captures reached through optional repeated suffixes.
    /// </summary>
    [Fact]
    public void FindCapturesKeepsNestedCapturesInOptionalRepeatedSuffix()
    {
        var automaton = RegexAutomaton.Compile(
            @"(?P<spaces>\s*)(?P<note>(?i:# note)(?::\s?(?P<codes>([A-Z]+[0-9]+(?:[,\s]+)?)+))?)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] line = "    value  # note: E501"u8.ToArray();

        RegexCaptures? captures = automaton.FindCaptures(line);

        Assert.False(automaton.UsesNoqaCaptureEngine);
        Assert.NotNull(captures);
        Assert.Equal(5, captures.ParticipatingCount());
        Assert.Equal(5, automaton.CountCaptures(line));
        AssertGroupText(captures, line, 3, "E501");
        AssertGroupText(captures, line, 4, "E501");
    }

    /// <summary>
    /// Verifies Ruff noqa captures use the reverse literal capture engine.
    /// </summary>
    [Fact]
    public void NoqaCaptureEngineReportsInlineFlagCaptures()
    {
        var automaton = RegexAutomaton.Compile(
            @"(?P<spaces>\s*)(?P<noqa>(?i:# noqa)(?::\s?(?P<codes>([A-Z]+[0-9]+(?:[,\s]+)?)+))?)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] line = "value  # NoQa: E501,W291x"u8.ToArray();

        RegexCaptures? captures = automaton.FindCaptures(line);

        Assert.True(automaton.UsesNoqaCaptureEngine);
        Assert.NotNull(captures);
        Assert.Equal(5, captures.ParticipatingCount());
        Assert.Equal(5, automaton.CountCaptures(line));
        AssertGroupText(captures, line, 1, "  ");
        AssertGroupText(captures, line, 2, "# NoQa: E501,W291");
        AssertGroupText(captures, line, 3, "E501,W291");
        AssertGroupText(captures, line, 4, "W291");

        byte[] withoutCodes = "  # noqa:"u8.ToArray();
        RegexCaptures? shortCaptures = automaton.FindCaptures(withoutCodes);

        Assert.NotNull(shortCaptures);
        Assert.Equal(3, shortCaptures.ParticipatingCount());
        Assert.Equal(3, automaton.CountCaptures(withoutCodes));
        AssertGroupText(shortCaptures, withoutCodes, 1, "  ");
        AssertGroupText(shortCaptures, withoutCodes, 2, "# noqa");
        Assert.Null(shortCaptures.GetGroup(3));
        Assert.Null(shortCaptures.GetGroup(4));
    }

    /// <summary>
    /// Verifies the expanded Rebar noqa form uses the same capture engine.
    /// </summary>
    [Fact]
    public void NoqaCaptureEngineReportsExpandedClassCaptures()
    {
        var automaton = RegexAutomaton.Compile(
            @"(\s*)((?:# [Nn][Oo][Qq][Aa])(?::\s?(([A-Z]+[0-9]+(?:[,\s]+)?)+))?)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] line = System.Text.Encoding.UTF8.GetBytes("\u00A0# nOqA: C901");

        RegexCaptures? captures = automaton.FindCaptures(line);

        Assert.True(automaton.UsesNoqaCaptureEngine);
        Assert.NotNull(captures);
        Assert.Equal(5, captures.ParticipatingCount());
        Assert.Equal(5, automaton.CountCaptures(line));
        AssertGroupUtf8Text(captures, line, 1, "\u00A0");
        AssertGroupText(captures, line, 2, "# nOqA: C901");
        AssertGroupText(captures, line, 3, "C901");
        AssertGroupText(captures, line, 4, "C901");
    }

    /// <summary>
    /// Verifies repeated alternation captures do not keep stale groups from abandoned parses.
    /// </summary>
    [Fact]
    public void FindCapturesDropsStaleGroupsFromRepeatedAlternatives()
    {
        var automaton = RegexAutomaton.Compile(
            BibleReferencePattern(),
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = "Gen 1:1, 2\n3 King 1:3-4\nII Ki. 3:12-14, 25\n"u8.ToArray();

        Assert.True(automaton.UsesBibleReferenceCaptureEngine);

        long total = 0;
        int matches = 0;
        int offset = 0;
        RegexCaptures? last = null;
        while (offset <= haystack.Length)
        {
            RegexCaptures? captures = automaton.FindCaptures(haystack, offset);
            if (captures is null)
            {
                break;
            }

            matches++;
            total += captures.ParticipatingCount();
            last = captures;
            RegexMatch match = captures.Match;
            offset = match.Length == 0 ? Math.Min(match.End + 1, haystack.Length + 1) : match.End;
        }

        Assert.Equal(3, matches);
        Assert.Equal(30, total);
        Assert.Equal(30, automaton.CountCaptures(haystack));
        Assert.NotNull(last);
        Assert.Null(last.GetGroup(7));
        Assert.Null(last.GetGroup(8));
        AssertGroupText(last, haystack, 11, "12");
        AssertGroupText(last, haystack, 13, "14");
        AssertGroupText(last, haystack, 14, "25");
    }

    /// <summary>
    /// Verifies the bible-reference capture fast path preserves repeated numeric location captures.
    /// </summary>
    [Fact]
    public void BibleReferenceCaptureEngineReportsRepeatedNumericLocations()
    {
        var automaton = RegexAutomaton.Compile(
            BibleReferencePattern(),
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = "I LM 1957\n"u8.ToArray();

        RegexCaptures? captures = automaton.FindCaptures(haystack);

        Assert.True(automaton.UsesBibleReferenceCaptureEngine);
        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, haystack.Length), captures.Match);
        Assert.Equal(15, captures.GroupCount);
        Assert.Equal(7, captures.ParticipatingCount());
        AssertGroupText(captures, haystack, 1, "I LM");
        AssertGroupText(captures, haystack, 2, "I ");
        AssertGroupText(captures, haystack, 3, "I");
        AssertGroupText(captures, haystack, 4, "1957\n");
        AssertGroupText(captures, haystack, 5, "7\n");
        AssertGroupText(captures, haystack, 6, "7");
    }

    /// <summary>
    /// Verifies the bible-reference capture fast path stays limited to Unicode-class mode.
    /// </summary>
    [Fact]
    public void BibleReferenceCaptureEngineSkipsNonUnicodeClasses()
    {
        var automaton = RegexAutomaton.Compile(
            BibleReferencePattern(),
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.False(automaton.UsesBibleReferenceCaptureEngine);
    }

    /// <summary>
    /// Verifies whole-branch captured scalar-run alternatives synthesize capture groups.
    /// </summary>
    [Fact]
    public void FindCapturesSynthesizesScalarRunAlternationCaptures()
    {
        var automaton = RegexAutomaton.Compile(
            @"(\p{L}{4})|(\p{L}{3})|(\p{L}{2})"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("абвг деж xy");

        RegexCaptures? first = automaton.FindCaptures(haystack);
        RegexCaptures? second = automaton.FindCaptures(haystack, first!.Match.End);
        RegexCaptures? third = automaton.FindCaptures(haystack, second!.Match.End);

        Assert.NotNull(first);
        Assert.Equal(2, first.ParticipatingCount());
        Assert.Equal(first.Match, first.GetGroup(1));
        Assert.Null(first.GetGroup(2));
        Assert.Null(first.GetGroup(3));
        Assert.NotNull(second);
        Assert.Equal(2, second.ParticipatingCount());
        Assert.Null(second.GetGroup(1));
        Assert.Equal(second.Match, second.GetGroup(2));
        Assert.Null(second.GetGroup(3));
        Assert.NotNull(third);
        Assert.Equal(2, third.ParticipatingCount());
        Assert.Null(third.GetGroup(1));
        Assert.Null(third.GetGroup(2));
        Assert.Equal(third.Match, third.GetGroup(3));
    }

    /// <summary>
    /// Verifies lowercase/uppercase Unicode property scalar runs count non-overlapping matches directly.
    /// </summary>
    [Fact]
    public void ScalarRunCountsLowerOrUpperUnicodeProperties()
    {
        var automaton = RegexAutomaton.Compile(
            @"(?:\p{Lowercase}|\p{Uppercase}){3}"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("abC xyz αβΓ Δεζ 123");

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 3), automaton.Find(haystack));
        Assert.Equal(4, automaton.CountMatches(haystack));
    }

    /// <summary>
    /// Verifies bounded Unicode-letter runs count non-overlapping matches directly.
    /// </summary>
    [Fact]
    public void ScalarRunCountsBoundedUnicodeLetters()
    {
        var automaton = RegexAutomaton.Compile(
            @"\p{L}{8,13}"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("абвгдеж абвгдежз абвгдежзийклм абвгдежзийклмнопрстуф");
        int firstStart = System.Text.Encoding.UTF8.GetByteCount("абвгдеж ");

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(firstStart, 16), automaton.Find(haystack));
        Assert.Equal(4, automaton.CountMatches(haystack));
    }

    /// <summary>
    /// Verifies unbounded Unicode property scalar runs count non-overlapping spans directly.
    /// </summary>
    [Fact]
    public void ScalarRunCountsUnboundedUnicodePropertyRuns()
    {
        var automaton = RegexAutomaton.Compile(
            @"\p{Greek}+"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("abc αβγ 123 δε z ηθι");
        int firstStart = System.Text.Encoding.UTF8.GetByteCount("abc ");
        int secondStart = System.Text.Encoding.UTF8.GetByteCount("abc αβγ 123 ");

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(firstStart, 6), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(secondStart, 4), automaton.Find(haystack, firstStart + 6));
        Assert.Equal(3, automaton.CountMatches(haystack));
        Assert.Equal(16, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies a single Unicode dot-all scalar atom uses the scalar run engine and skips invalid UTF-8.
    /// </summary>
    [Fact]
    public void ScalarRunCountsUnicodeDotAllScalars()
    {
        var automaton = RegexAutomaton.Compile(
            "(?s:.)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = [(byte)'a', 0xC3, 0xA9, (byte)'\n', 0xD0, 0x96, 0xFF, (byte)'b'];

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 1), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(1, 2), automaton.Find(haystack, startAt: 1));
        Assert.Equal(new RegexMatch(3, 1), automaton.Find(haystack, startAt: 3));
        Assert.Equal(new RegexMatch(4, 2), automaton.MatchAt(haystack, startAt: 4));
        Assert.Null(automaton.MatchAt(haystack, startAt: 6));
        Assert.Equal(new RegexMatch(7, 1), automaton.Find(haystack, startAt: 6));
        Assert.Equal(5, automaton.CountMatches(haystack));
        Assert.Equal(7, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies literal-prefix word captures synthesize branch captures directly.
    /// </summary>
    [Fact]
    public void LiteralWordCaptureEngineReportsAlternationCaptures()
    {
        var automaton = RegexAutomaton.Compile(
            @"fn is_(\w+)|fn as_(\w+)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "fn no_ignored fn is_ready fn as_ref fn is_ fn as_42x!"u8.ToArray();

        RegexCaptures? first = automaton.FindCaptures(haystack);
        RegexCaptures? second = automaton.FindCaptures(haystack, first!.Match.End);
        RegexCaptures? third = automaton.FindCaptures(haystack, second!.Match.End);

        Assert.True(automaton.UsesLiteralWordCaptureEngine);
        Assert.NotNull(first);
        Assert.Equal(2, first.ParticipatingCount());
        AssertGroupText(first, haystack, 1, "ready");
        Assert.Null(first.GetGroup(2));
        Assert.NotNull(second);
        Assert.Equal(2, second.ParticipatingCount());
        Assert.Null(second.GetGroup(1));
        AssertGroupText(second, haystack, 2, "ref");
        Assert.NotNull(third);
        Assert.Equal(2, third.ParticipatingCount());
        Assert.Null(third.GetGroup(1));
        AssertGroupText(third, haystack, 2, "42x");
        Assert.Null(automaton.FindCaptures(haystack, third.Match.End));
    }

    /// <summary>
    /// Verifies literal-run branch captures synthesize the matching branch group directly.
    /// </summary>
    [Fact]
    public void LiteralRunAlternationCaptureEngineReportsBranchCaptures()
    {
        var automaton = RegexAutomaton.Compile(
            @"(?:(a+)|(b+)|(c+))"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xx aaabcc dd ccc"u8.ToArray();

        RegexCaptures? first = automaton.FindCaptures(haystack);
        RegexCaptures? second = automaton.FindCaptures(haystack, first!.Match.End);
        RegexCaptures? third = automaton.FindCaptures(haystack, second!.Match.End);
        RegexCaptures? suffix = automaton.FindCaptures("aaaa"u8, startAt: 2);

        Assert.True(automaton.UsesLiteralRunAlternationCaptureEngine);
        Assert.NotNull(first);
        Assert.Equal(2, first.ParticipatingCount());
        AssertGroupText(first, haystack, 1, "aaa");
        Assert.Null(first.GetGroup(2));
        Assert.Null(first.GetGroup(3));
        Assert.NotNull(second);
        Assert.Equal(2, second.ParticipatingCount());
        Assert.Null(second.GetGroup(1));
        AssertGroupText(second, haystack, 2, "b");
        Assert.Null(second.GetGroup(3));
        Assert.NotNull(third);
        Assert.Equal(2, third.ParticipatingCount());
        Assert.Null(third.GetGroup(1));
        Assert.Null(third.GetGroup(2));
        AssertGroupText(third, haystack, 3, "cc");
        Assert.NotNull(suffix);
        Assert.Equal(new RegexMatch(2, 2), suffix.Match);
        Assert.Null(automaton.FindCaptures(haystack, haystack.Length));
    }

    /// <summary>
    /// Verifies literal path semver captures synthesize branch-specific captures directly.
    /// </summary>
    [Fact]
    public void PathSemverCaptureEngineReportsAlternationCaptures()
    {
        var automaton = RegexAutomaton.Compile(
            @"cargo/registry/src/[^/]+/([0-9A-Za-z_-]+)-([0-9]+\.[0-9]+\.[0-9]+[0-9A-Za-z+.-]*)/|cargo\\registry\\src\\[^\\]+\\([0-9A-Za-z_-]+)-([0-9]+\.[0-9]+\.[0-9]+[0-9A-Za-z+.-]*)\\"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack =
            @"bad cargo/registry/src/github.com-hash/broken-1.2/ cargo/registry/src/github.com-hash/serde-1.0.197/ cargo\registry\src\github.com-hash\memchr-2.7.1\"u8.ToArray();

        RegexCaptures? first = automaton.FindCaptures(haystack);
        RegexCaptures? second = automaton.FindCaptures(haystack, first!.Match.End);

        Assert.True(automaton.UsesPathSemverCaptureEngine);
        Assert.NotNull(first);
        Assert.Equal(3, first.ParticipatingCount());
        AssertGroupText(first, haystack, 1, "serde");
        AssertGroupText(first, haystack, 2, "1.0.197");
        Assert.Null(first.GetGroup(3));
        Assert.Null(first.GetGroup(4));
        Assert.NotNull(second);
        Assert.Equal(3, second.ParticipatingCount());
        Assert.Null(second.GetGroup(1));
        Assert.Null(second.GetGroup(2));
        AssertGroupText(second, haystack, 3, "memchr");
        AssertGroupText(second, haystack, 4, "2.7.1");
        Assert.Null(automaton.FindCaptures(haystack, second.Match.End));
    }

    /// <summary>
    /// Verifies path semver captures support slash character classes.
    /// </summary>
    [Fact]
    public void PathSemverCaptureEngineReportsSeparatorClassCaptures()
    {
        var automaton = RegexAutomaton.Compile(
            @"cargo[\\/]registry[\\/]src[\\/][^\\/]+[\\/]([0-9A-Za-z_-]+)-([0-9]+\.[0-9]+\.[0-9]+[0-9A-Za-z+.-]*)[\\/]"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack =
            @"cargo\registry\src\github.com-hash\winapi-0.3.9\ cargo/registry/src/github.com-hash/aho-corasick-1.1.3/"u8.ToArray();

        RegexCaptures? first = automaton.FindCaptures(haystack);
        RegexCaptures? second = automaton.FindCaptures(haystack, first!.Match.End);

        Assert.True(automaton.UsesPathSemverCaptureEngine);
        Assert.NotNull(first);
        Assert.Equal(3, first.ParticipatingCount());
        AssertGroupText(first, haystack, 1, "winapi");
        AssertGroupText(first, haystack, 2, "0.3.9");
        Assert.NotNull(second);
        Assert.Equal(3, second.ParticipatingCount());
        AssertGroupText(second, haystack, 1, "aho-corasick");
        AssertGroupText(second, haystack, 2, "1.1.3");
        Assert.Null(automaton.FindCaptures(haystack, second.Match.End));
    }

    /// <summary>
    /// Verifies fixed ASCII letter length alternations synthesize captures.
    /// </summary>
    [Fact]
    public void AsciiLetterLengthAlternationCaptureEngineReportsCaptures()
    {
        var automaton = RegexAutomaton.Compile(
            @"([A-Za-z]{7})|([A-Za-z]{6})|([A-Za-z]{5})"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xx abcdefgh yz abcd ABCDE z abcdef"u8.ToArray();

        Assert.True(automaton.UsesAsciiLetterLengthAlternationCaptureEngine);
        RegexCaptures? first = automaton.FindCaptures(haystack);
        Assert.NotNull(first);
        Assert.Equal(new RegexMatch(3, 7), first.Match);
        Assert.Equal(new RegexMatch(3, 7), first.GetGroup(0));
        Assert.Equal(new RegexMatch(3, 7), first.GetGroup(1));
        Assert.Null(first.GetGroup(2));
        Assert.Null(first.GetGroup(3));

        RegexCaptures? second = automaton.FindCaptures(haystack, first.Match.End);
        Assert.NotNull(second);
        Assert.Equal(new RegexMatch(20, 5), second.Match);
        Assert.Equal(new RegexMatch(20, 5), second.GetGroup(3));

        RegexCaptures? third = automaton.FindCaptures(haystack, second.Match.End);
        Assert.NotNull(third);
        Assert.Equal(new RegexMatch(28, 6), third.Match);
        Assert.Equal(new RegexMatch(28, 6), third.GetGroup(2));
    }

    /// <summary>
    /// Verifies required-literal prefilters do not reject scoped case-insensitive literals.
    /// </summary>
    [Fact]
    public void ScopedCaseInsensitiveLiteralAfterNullablePrefixMatchesMixedCase()
    {
        var automaton = RegexAutomaton.Compile(
            @"\s*(?i:# noqa)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);

        Assert.Equal(new RegexMatch(0, 8), automaton.Find("  # NoQA"u8));
        Assert.Equal(new RegexMatch(1, 8), automaton.Find("x  # NoQA"u8));
    }

    /// <summary>
    /// Verifies line-wide dot-star contains patterns use a linear count/span path.
    /// </summary>
    [Fact]
    public void LineContainsEngineCountsLineSpans()
    {
        var automaton = RegexAutomaton.Compile(".*.*=.*"u8);
        byte[] haystack = "abc\nx=123\nnope\nz=9"u8.ToArray();

        Assert.Equal(RegexEngineKind.LineContains, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 2), automaton.Find("=x"u8, startAt: 0));
        Assert.Equal(new RegexMatch(4, 5), automaton.Find(haystack, startAt: 0));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(8, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies byte-run literal dot-star patterns scan from the required literal.
    /// </summary>
    [Fact]
    public void RunLiteralDotStarEngineCountsLineSpans()
    {
        var automaton = RegexAutomaton.Compile(
            @"[ -~]*ABCDEFGHIJKLMNOPQRSTUVWXYZ.*"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = System.Text.Encoding.ASCII.GetBytes(
            "bad\n" +
            "xABCDEFGHIJKLMNOPQRSTUVWXYZ tail\n" +
            "next ABCDEFGHIJKLMNOPQRSTUVWXYZ!");

        Assert.Equal(RegexEngineKind.RunLiteralDotStar, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(4, 32), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(5, 31), automaton.Find(haystack, startAt: 5));
        Assert.Equal(new RegexMatch(37, 32), automaton.Find(haystack, startAt: 36));
        Assert.Null(automaton.MatchAt(haystack, 0));
        Assert.Equal(new RegexMatch(4, 32), automaton.MatchAt(haystack, 4));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(64, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies Unicode letter-run literal alternations scan from inner literals.
    /// </summary>
    [Fact]
    public void UnicodeLetterLiteralRunEngineCountsWordSpans()
    {
        var automaton = RegexAutomaton.Compile(
            @"\pL+herloc\pL+|\pL+olme\pL+"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = System.Text.Encoding.ASCII.GetBytes("Sherlock Holmes herlocX XolmeY");
        byte[] unicodeHaystack = System.Text.Encoding.UTF8.GetBytes("βherlocδ");

        Assert.Equal(RegexEngineKind.UnicodeLetterLiteralRun, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 8), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(9, 6), automaton.Find(haystack, startAt: 1));
        Assert.Equal(new RegexMatch(24, 6), automaton.Find(haystack, startAt: 16));
        Assert.Equal(new RegexMatch(0, 8), automaton.MatchAt(haystack, 0));
        Assert.Null(automaton.MatchAt(haystack, 1));
        Assert.Equal(new RegexMatch(9, 6), automaton.MatchAt(haystack, 9));
        Assert.Equal(3, automaton.CountMatches(haystack));
        Assert.Equal(20, automaton.SumMatchSpans(haystack));
        Assert.Equal(new RegexMatch(0, 10), automaton.MatchAt(unicodeHaystack, 0));
    }

    /// <summary>
    /// Verifies literal-prefix ASCII letter runs scan from prefix candidates.
    /// </summary>
    [Fact]
    public void LiteralPrefixRunEngineCountsAlternationSpans()
    {
        var lowercase = RegexAutomaton.Compile(
            @"Sher[a-z]+|Hol[a-z]+"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        var caseInsensitive = RegexAutomaton.Compile(
            @"Sher[a-z]+|Hol[a-z]+"u8,
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        var letter = RegexAutomaton.Compile(
            @"Huck[a-zA-Z]+|Saw[a-zA-Z]+"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(RegexEngineKind.LiteralPrefixRun, GetEngineKind(lowercase));
        Assert.Equal(RegexEngineKind.LiteralPrefixRun, GetEngineKind(caseInsensitive));
        Assert.Equal(RegexEngineKind.LiteralPrefixRun, GetEngineKind(letter));

        byte[] lowercaseHaystack = "xx Sherlock Holmes Sher9 Hol_ Sherman Holdup"u8.ToArray();
        Assert.Equal(new RegexMatch(3, 8), lowercase.Find(lowercaseHaystack));
        Assert.Equal(new RegexMatch(12, 6), lowercase.Find(lowercaseHaystack, startAt: 4));
        Assert.Equal(new RegexMatch(3, 8), lowercase.MatchAt(lowercaseHaystack, 3));
        Assert.Null(lowercase.MatchAt(lowercaseHaystack, 4));
        Assert.Equal(4, lowercase.CountMatches(lowercaseHaystack));
        Assert.Equal(27, lowercase.SumMatchSpans(lowercaseHaystack));

        byte[] caseInsensitiveHaystack = "sherLOCK HOLMES Sher9 holdup"u8.ToArray();
        Assert.Equal(new RegexMatch(0, 8), caseInsensitive.Find(caseInsensitiveHaystack));
        Assert.Equal(new RegexMatch(9, 6), caseInsensitive.Find(caseInsensitiveHaystack, startAt: 1));
        Assert.Equal(3, caseInsensitive.CountMatches(caseInsensitiveHaystack));
        Assert.Equal(20, caseInsensitive.SumMatchSpans(caseInsensitiveHaystack));

        byte[] letterHaystack = "HuckFinn SawX sawlower Huck9 Saw_"u8.ToArray();
        Assert.Equal(new RegexMatch(0, 8), letter.Find(letterHaystack));
        Assert.Equal(new RegexMatch(9, 4), letter.Find(letterHaystack, startAt: 1));
        Assert.Equal(2, letter.CountMatches(letterHaystack));
        Assert.Equal(12, letter.SumMatchSpans(letterHaystack));
    }

    /// <summary>
    /// Verifies literal-prefix run candidates preserve branch fallback when prefixes overlap.
    /// </summary>
    [Fact]
    public void LiteralPrefixRunEngineHandlesOverlappingPrefixes()
    {
        var automaton = RegexAutomaton.Compile(
            @"Hu[A-Z]+|Huck[A-Za-z]+"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xx HuckFinn yy HuABC"u8.ToArray();
        int firstStart = haystack.AsSpan().IndexOf("HuckFinn"u8);
        int secondStart = haystack.AsSpan().IndexOf("HuABC"u8);

        Assert.Equal(RegexEngineKind.LiteralPrefixRun, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(firstStart, 8), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(secondStart, 5), automaton.Find(haystack, firstStart + 1));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(13, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies bounded literal gaps scan from prefix candidates and honor greedy repeats.
    /// </summary>
    [Fact]
    public void BoundedLiteralGapEngineCountsAlternationSpans()
    {
        var automaton = RegexAutomaton.Compile(
            @"Holmes.{0,5}Watson|Watson.{0,5}Holmes"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xx Holmes--Watson HolmesabcdefWatson WatsonHolmes WatsonabcHolmes Holmes\nWatson"u8.ToArray();
        byte[] greedyHaystack = "HolmesWatsonxxWatson"u8.ToArray();

        Assert.Equal(RegexEngineKind.BoundedLiteralGap, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(3, 14), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(11, 13), automaton.Find(haystack, startAt: 4));
        Assert.Equal(new RegexMatch(37, 12), automaton.Find(haystack, startAt: 17));
        Assert.Equal(new RegexMatch(43, 13), automaton.Find(haystack, startAt: 40));
        Assert.Equal(new RegexMatch(3, 14), automaton.MatchAt(haystack, 3));
        Assert.Null(automaton.MatchAt(haystack, 18));
        Assert.Null(automaton.MatchAt(haystack, 68));
        Assert.Equal(3, automaton.CountMatches(haystack));
        Assert.Equal(41, automaton.SumMatchSpans(haystack));
        Assert.Equal(new RegexMatch(0, 20), RegexAutomaton.Compile(
            @"Holmes.{0,10}Watson"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false).Find(greedyHaystack));

        var shortPrefix = RegexAutomaton.Compile(
            @"Tom.{10,25}river|river.{10,25}Tom"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] shortPrefixHaystack = "Tom----------river______________________________river--------------Tom"u8.ToArray();
        Assert.Equal(RegexEngineKind.BoundedLiteralGap, GetEngineKind(shortPrefix));
        Assert.Equal(2, shortPrefix.CountMatches(shortPrefixHaystack));
        Assert.Equal(40, shortPrefix.SumMatchSpans(shortPrefixHaystack));
    }

    /// <summary>
    /// Verifies bounded whitespace-dot line gaps count non-empty non-newline runs.
    /// </summary>
    [Fact]
    public void BoundedLineLiteralGapEngineCountsAlternationSpans()
    {
        var automaton = RegexAutomaton.Compile(
            @"Holmes(?:\s*.+\s*){0,2}Watson|Watson(?:\s*.+\s*){0,2}Holmes"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        byte[] haystack = "xx Holmes a\n b\nWatson yy Watson z Holmes"u8.ToArray();
        byte[] immediateHaystack = "HolmesWatson"u8.ToArray();
        byte[] crlfGap = "Holmes\r\nWatson"u8.ToArray();
        byte[] lineOnlyGap = "Holmes\nWatson"u8.ToArray();
        byte[] tooManyRuns = "Holmes a\n b\n cWatson"u8.ToArray();
        byte[] greedyHaystack = "Holmes a\nWatson xx\nWatson"u8.ToArray();

        Assert.Equal(RegexEngineKind.BoundedLineLiteralGap, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(3, 18), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(25, 15), automaton.Find(haystack, startAt: 22));
        Assert.Equal(new RegexMatch(0, 12), automaton.Find(immediateHaystack));
        Assert.Equal(new RegexMatch(0, 14), automaton.Find(crlfGap));
        Assert.Equal(new RegexMatch(3, 18), automaton.MatchAt(haystack, 3));
        Assert.Null(automaton.MatchAt(lineOnlyGap, 0));
        Assert.Null(automaton.MatchAt(tooManyRuns, 0));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(33, automaton.SumMatchSpans(haystack));
        Assert.Equal(new RegexMatch(0, greedyHaystack.Length), RegexAutomaton.Compile(
            @"Holmes(?:\s*.+\s*){0,2}Watson"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false).Find(greedyHaystack));
    }

    /// <summary>
    /// Verifies anchored line literal gaps scan from the required tail literal while preserving lazy dot-star spans.
    /// </summary>
    [Fact]
    public void AnchoredLineLiteralGapEngineMatchesCodingComments()
    {
        var automaton = RegexAutomaton.Compile(
            @"^[ \t\f]*#.*?coding[:=][ \t]*utf-?8"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);

        Assert.Equal(RegexEngineKind.AnchoredLineLiteralGap, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 21), automaton.Find("  # -*- coding: utf-8 -*-"u8));
        Assert.Equal(new RegexMatch(0, 13), automaton.Find("# coding=utf8"u8));
        Assert.Equal(new RegexMatch(0, 19), automaton.Find(" \t\f# coding:   utf8"u8));
        Assert.True(automaton.IsMatch("# other coding:    utf8"u8));
        Assert.False(automaton.IsMatch("x # coding: utf-8"u8));
        Assert.False(automaton.IsMatch("# no coding here"u8));
        Assert.False(automaton.IsMatch("# coding: ascii"u8));
    }

    /// <summary>
    /// Verifies anchored line literal gaps keep non-multiline start-anchor and dot line-boundary semantics.
    /// </summary>
    [Fact]
    public void AnchoredLineLiteralGapEnginePreservesAnchorAndLineBoundarySemantics()
    {
        var automaton = RegexAutomaton.Compile(
            @"^[ \t\f]*#.*?coding[:=][ \t]*utf-?8"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);

        byte[] twoLines = "# coding: utf-8\n# coding: utf-8"u8.ToArray();

        Assert.Equal(new RegexMatch(0, 15), automaton.Find(twoLines));
        Assert.Null(automaton.Find(twoLines, startAt: 1));
        Assert.Equal(new RegexMatch(0, 15), automaton.MatchAt(twoLines, 0));
        Assert.Null(automaton.MatchAt(twoLines, 1));
        Assert.Equal(1, automaton.CountMatches(twoLines));
        Assert.Equal(15, automaton.SumMatchSpans(twoLines));
        Assert.False(automaton.IsMatch("# comment\ncoding: utf-8"u8));
    }

    /// <summary>
    /// Verifies bounded dot prefixes before literal alternatives scan from literals while preserving greediness.
    /// </summary>
    [Fact]
    public void BoundedPrefixLiteralSetEngineCountsLiteralAlternatives()
    {
        var shortPrefix = RegexAutomaton.Compile(
            @".{0,2}(Tom|Sawyer|Huckleberry|Finn)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        var longPrefix = RegexAutomaton.Compile(
            @".{2,4}(Tom|Sawyer|Huckleberry|Finn)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xxTom abSawyer\nxFinn abcdFinn yyHuckleberry"u8.ToArray();
        byte[] captureHaystack = "xxTom"u8.ToArray();

        Assert.Equal(RegexEngineKind.BoundedPrefixLiteralSet, GetEngineKind(shortPrefix));
        Assert.Equal(RegexEngineKind.BoundedPrefixLiteralSet, GetEngineKind(longPrefix));
        Assert.Equal(new RegexMatch(0, 5), shortPrefix.Find(captureHaystack));
        Assert.Equal(new RegexMatch(1, 4), shortPrefix.Find(captureHaystack, startAt: 1));
        Assert.Equal(new RegexMatch(2, 3), shortPrefix.Find("x\nTom"u8));
        Assert.Equal(new RegexMatch(0, 5), shortPrefix.MatchAt(captureHaystack, 0));
        Assert.Equal(5, shortPrefix.CountMatches(haystack));
        Assert.Equal(37, shortPrefix.SumMatchSpans(haystack));

        RegexCaptures? captures = shortPrefix.FindCaptures(captureHaystack);
        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, 5), captures.Match);
        AssertGroupText(captures, captureHaystack, 1, "Tom");

        Assert.Equal(new RegexMatch(0, 7), longPrefix.Find("abcdTom"u8));
        Assert.Equal(new RegexMatch(0, 7), longPrefix.MatchAt("abcdTom"u8, 0));
        Assert.Null(longPrefix.Find("xTom"u8));
        Assert.Equal(4, longPrefix.CountMatches(haystack));
        Assert.Equal(36, longPrefix.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies short literal alternatives keep leftmost-first tie semantics.
    /// </summary>
    [Fact]
    public void LiteralSetEngineCountsShortLiteralAlternatives()
    {
        var automaton = RegexAutomaton.Compile(
            @"Tom|Sawyer|Huckleberry|Finn"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xTom SawyerFinn Huckleberry"u8.ToArray();

        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(1, 3), automaton.Find(haystack));
        Assert.Equal(4, automaton.CountMatches(haystack));
        Assert.Equal(24, automaton.SumMatchSpans(haystack));

        var longFirst = RegexAutomaton.Compile(
            @"foobar|foo"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        var shortFirst = RegexAutomaton.Compile(
            @"foo|foobar"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(new RegexMatch(0, 6), longFirst.Find("foobar"u8));
        Assert.Equal(new RegexMatch(0, 3), shortFirst.Find("foobar"u8));
    }

    /// <summary>
    /// Verifies the Unicode grapheme cluster engine counts extended cluster-shaped regex matches.
    /// </summary>
    [Fact]
    public void UnicodeGraphemeClusterEngineCountsExtendedClusters()
    {
        var automaton = RegexAutomaton.Compile(
            UnicodeGraphemeClusterPattern(),
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] firstCluster = System.Text.Encoding.UTF8.GetBytes("a\u0301");
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes(
            "a\u0301\r\n\rX\U0001F469\u200D\U0001F469\u1100\u1161\u11A8\u11A8\U0001F1FA\U0001F1F8");
        byte[] invalidThenAscii = [0xFF, (byte)'a'];

        Assert.Equal(RegexEngineKind.UnicodeGraphemeCluster, GetEngineKind(automaton));
        Assert.Equal(7, automaton.CountMatches(haystack));
        Assert.Equal(haystack.Length, automaton.SumMatchSpans(haystack));
        Assert.Equal(new RegexMatch(0, firstCluster.Length), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(1, firstCluster.Length - 1), automaton.Find(haystack, startAt: 1));
        Assert.Equal(new RegexMatch(firstCluster.Length, 2), automaton.Find(haystack, startAt: 2));
        Assert.Null(automaton.MatchAt(haystack, startAt: 2));
        Assert.Equal(new RegexMatch(firstCluster.Length, 2), automaton.MatchAt(haystack, firstCluster.Length));
        Assert.Equal(new RegexMatch(1, 1), automaton.Find(invalidThenAscii));
        Assert.Equal(1, automaton.CountMatches(invalidThenAscii));
    }

    /// <summary>
    /// Verifies bounded byte-class sequences scan from selective starting bytes and preserve repeat greediness.
    /// </summary>
    [Fact]
    public void BoundedByteClassSequenceEngineCountsQuoteSpans()
    {
        var automaton = RegexAutomaton.Compile(
            """["'][^"']{0,30}[?!\.]["']"""u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        var greedy = RegexAutomaton.Compile(
            "@[ab]{0,3}[ab]b"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        var lazy = RegexAutomaton.Compile(
            "@[ab]{0,3}?[ab]b"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        var suffixClass = RegexAutomaton.Compile(
            @"[a-q][^u-z]{3}[x\xE0-\xFF]"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        byte[] haystack = System.Text.Encoding.ASCII.GetBytes("xx \"yes?\" 'ok.' \"no\" \"a?.\"");
        byte[] tooLong = System.Text.Encoding.ASCII.GetBytes("\"" + new string('a', 31) + "!\"");
        byte[] classHaystack =
        [
            (byte)'z', (byte)' ', (byte)'a', (byte)'b', (byte)'c', (byte)'d', (byte)'x', (byte)' ',
            (byte)'b', (byte)'c', (byte)'d', (byte)'e', 0xE0, (byte)' ',
            (byte)'c', (byte)'u', (byte)'d', (byte)'e', (byte)'x',
        ];

        Assert.Equal(RegexEngineKind.BoundedByteClassSequence, GetEngineKind(automaton));
        Assert.Equal(RegexEngineKind.BoundedByteClassSequence, GetEngineKind(greedy));
        Assert.Equal(RegexEngineKind.BoundedByteClassSequence, GetEngineKind(lazy));
        Assert.Equal(RegexEngineKind.BoundedByteClassSequence, GetEngineKind(suffixClass));
        Assert.Equal(new RegexMatch(3, 6), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(10, 5), automaton.Find(haystack, startAt: 4));
        Assert.Equal(new RegexMatch(21, 5), automaton.Find(haystack, startAt: 15));
        Assert.Equal(new RegexMatch(3, 6), automaton.MatchAt(haystack, 3));
        Assert.Null(automaton.MatchAt(haystack, 16));
        Assert.Null(automaton.MatchAt(tooLong, 0));
        Assert.Equal(new RegexMatch(0, 3), automaton.MatchAt("\"!\""u8, 0));
        Assert.Equal(new RegexMatch(0, 3), automaton.FindEarliest("\"!\""u8, startAt: 0));
        Assert.Equal(new RegexMatch(0, 3), automaton.FindAllKindAt("\"!\""u8, startAt: 0));
        Assert.Equal([new RegexMatch(0, 3)], automaton.FindOverlappingAt("\"!\""u8, startAt: 0));
        Assert.Equal(3, automaton.CountMatches(haystack));
        Assert.Equal(16, automaton.SumMatchSpans(haystack));
        Assert.Equal(new RegexMatch(0, 5), greedy.Find("@aabb"u8));
        Assert.Equal(new RegexMatch(0, 4), lazy.Find("@aabb"u8));
        Assert.Equal(new RegexMatch(2, 5), suffixClass.Find(classHaystack));
        Assert.Equal(new RegexMatch(8, 5), suffixClass.Find(classHaystack, startAt: 3));
        Assert.Equal(new RegexMatch(8, 5), suffixClass.MatchAt(classHaystack, 8));
        Assert.Null(suffixClass.MatchAt(classHaystack, 14));
        Assert.Equal(2, suffixClass.CountMatches(classHaystack));
        Assert.Equal(10, suffixClass.SumMatchSpans(classHaystack));
    }

    /// <summary>
    /// Verifies bounded scalar-class sequences count UTF-8 scalars while keeping ASCII fast paths.
    /// </summary>
    [Fact]
    public void BoundedScalarClassSequenceEngineCountsUnicodeSpans()
    {
        var automaton = RegexAutomaton.Compile(
            "[a-q][^u-z]{3}x"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        var suffixClass = RegexAutomaton.Compile(
            @"[a-q][^u-z]{3}[x\xE0-\xFF]"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("zz aabcx b¢cdx c¢∞dx qabcx rbcdx aabux");
        byte[] classHaystack = System.Text.Encoding.UTF8.GetBytes("zz aabcx bcdeà cdefÿ dabu x");

        Assert.Equal(RegexEngineKind.BoundedScalarClassSequence, GetEngineKind(automaton));
        Assert.Equal(RegexEngineKind.BoundedScalarClassSequence, GetEngineKind(suffixClass));
        Assert.Equal(new RegexMatch(3, 5), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(9, 6), automaton.Find(haystack, startAt: 4));
        Assert.Equal(new RegexMatch(16, 8), automaton.Find(haystack, startAt: 10));
        Assert.Equal(new RegexMatch(25, 5), automaton.Find(haystack, startAt: 18));
        Assert.Equal(new RegexMatch(9, 6), automaton.MatchAt(haystack, 9));
        Assert.Equal(new RegexMatch(16, 8), automaton.MatchAt(haystack, 16));
        Assert.Equal(new RegexMatch(0, 5), automaton.FindEarliest("aabcx"u8, startAt: 0));
        Assert.Equal(new RegexMatch(0, 5), automaton.FindAllKindAt("aabcx"u8, startAt: 0));
        Assert.Equal([new RegexMatch(0, 5)], automaton.FindOverlappingAt("aabcx"u8, startAt: 0));
        Assert.Null(automaton.MatchAt(haystack, 31));
        Assert.Null(automaton.MatchAt(haystack, 37));
        Assert.Equal(4, automaton.CountMatches(haystack));
        Assert.Equal(24, automaton.SumMatchSpans(haystack));
        Assert.Equal(new RegexMatch(3, 5), suffixClass.Find(classHaystack));
        Assert.Equal(new RegexMatch(9, 6), suffixClass.Find(classHaystack, startAt: 4));
        Assert.Equal(new RegexMatch(16, 6), suffixClass.Find(classHaystack, startAt: 10));
        Assert.Equal(new RegexMatch(9, 6), suffixClass.MatchAt(classHaystack, 9));
        Assert.Null(suffixClass.MatchAt(classHaystack, 23));
        Assert.Equal(new RegexMatch(0, 6), suffixClass.FindEarliest(System.Text.Encoding.UTF8.GetBytes("abcdà"), startAt: 0));
        Assert.Equal(new RegexMatch(0, 6), suffixClass.FindAllKindAt(System.Text.Encoding.UTF8.GetBytes("abcdà"), startAt: 0));
        Assert.Equal([new RegexMatch(0, 6)], suffixClass.FindOverlappingAt(System.Text.Encoding.UTF8.GetBytes("abcdà"), startAt: 0));
        Assert.Equal(3, suffixClass.CountMatches(classHaystack));
        Assert.Equal(17, suffixClass.SumMatchSpans(classHaystack));
    }

    /// <summary>
    /// Verifies repeated lazy dot-star delimiter runs scan suffix candidates within one line.
    /// </summary>
    [Fact]
    public void RepeatedLazyDotStarLiteralEngineCountsDelimiterRuns()
    {
        var automaton = RegexAutomaton.Compile(
            "(.*?,){2}z"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "aa,bb,z tail a,b,xz\ncc,dd,z"u8.ToArray();
        byte[] largeHaystack = new byte[256];
        Array.Fill(largeHaystack, (byte)'x');
        largeHaystack[80] = (byte)'\n';
        "aa,bb,z"u8.CopyTo(largeHaystack.AsSpan(81));
        byte[] largeNoMatch = new byte[256];
        Array.Fill(largeNoMatch, (byte)'x');
        for (int index = 16; index < largeNoMatch.Length; index += 31)
        {
            largeNoMatch[index] = (byte)'z';
        }

        Assert.Equal(RegexEngineKind.RepeatedLazyDotStarLiteral, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 7), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(1, 6), automaton.Find(haystack, startAt: 1));
        Assert.Equal(new RegexMatch(20, 7), automaton.Find(haystack, startAt: 16));
        Assert.Equal(new RegexMatch(81, 7), automaton.Find(largeHaystack));
        Assert.Equal(new RegexMatch(0, 7), automaton.MatchAt(haystack, 0));
        Assert.Null(automaton.MatchAt(haystack, 3));
        Assert.Null(automaton.MatchAt("aa,\nbb,z"u8, 0));
        Assert.Null(automaton.Find(largeNoMatch));
        Assert.Equal(new RegexMatch(0, 3), automaton.Find(",,z"u8));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(14, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies delimiter spans scan from delimiters directly.
    /// </summary>
    [Fact]
    public void DelimitedSpanEngineCountsDelimitedSpans()
    {
        var angle = RegexAutomaton.Compile(
            @"<[^>]*>"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        var pipe = RegexAutomaton.Compile(
            @"\|[^|][^|]*\|"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(RegexEngineKind.DelimitedSpan, GetEngineKind(angle));
        Assert.Equal(RegexEngineKind.DelimitedSpan, GetEngineKind(pipe));
        Assert.Equal(new RegexMatch(2, 3), angle.Find("xx<a> <bc>"u8));
        Assert.Equal(new RegexMatch(6, 4), angle.Find("xx<a> <bc>"u8, startAt: 3));
        Assert.Equal(new RegexMatch(0, 4), angle.MatchAt("<bc>"u8, 0));
        Assert.Null(angle.MatchAt("x<bc>"u8, 0));
        Assert.Equal(2, angle.CountMatches("xx<a> <bc>"u8));
        Assert.Equal(7, angle.SumMatchSpans("xx<a> <bc>"u8));
        Assert.Equal(new RegexMatch(3, 3), pipe.Find("xx |a| |bc|"u8));
        Assert.Null(pipe.Find("||"u8));
        Assert.Equal(new RegexMatch(1, 3), pipe.Find("||a|"u8));
        Assert.Equal(2, pipe.CountMatches("xx |a| |bc|"u8));
        Assert.Equal(7, pipe.SumMatchSpans("xx |a| |bc|"u8));
    }

    /// <summary>
    /// Verifies delimiter spans can include a standalone terminator alternative.
    /// </summary>
    [Fact]
    public void DelimitedSpanEngineCountsStandaloneTerminators()
    {
        var automaton = RegexAutomaton.Compile(
            @">[^\n]*\n|\n"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "abc\n>header\nx>mid\nno final"u8.ToArray();
        int headerStart = haystack.AsSpan().IndexOf(">header\n"u8);

        Assert.Equal(RegexEngineKind.DelimitedSpan, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(3, 1), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(headerStart, 8), automaton.Find(haystack, startAt: 4));
        Assert.Equal(new RegexMatch(headerStart + 7, 1), automaton.Find(haystack, startAt: headerStart + 1));
        Assert.Equal(new RegexMatch(0, 4), automaton.MatchAt(">xx\n"u8, 0));
        Assert.Equal(new RegexMatch(0, 1), automaton.MatchAt("\n"u8, 0));
        Assert.Equal(3, automaton.CountMatches(haystack));
        Assert.Equal(14, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies fixed-width byte alternations scan from their most selective byte position.
    /// </summary>
    [Fact]
    public void FixedWidthAlternationEngineCountsRegexReduxVariants()
    {
        var automaton = RegexAutomaton.Compile(
            @"[cgt]gggtaaa|tttaccc[acg]"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xx cgggtaaa yy tttaccca zz tttaccct tgggtaaa"u8.ToArray();
        int firstStart = haystack.AsSpan().IndexOf("cgggtaaa"u8);
        int secondStart = haystack.AsSpan().IndexOf("tttaccca"u8);
        int thirdStart = haystack.AsSpan().IndexOf("tgggtaaa"u8);

        Assert.Equal(RegexEngineKind.FixedWidthAlternation, GetEngineKind(automaton));
        Assert.True(automaton.IsMatch(haystack));
        Assert.Equal(new RegexMatch(firstStart, 8), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(secondStart, 8), automaton.Find(haystack, firstStart + 1));
        Assert.Equal(new RegexMatch(thirdStart, 8), automaton.MatchAt(haystack, thirdStart));
        Assert.Null(automaton.MatchAt(haystack, thirdStart + 1));
        Assert.Equal(3, automaton.CountMatches(haystack));
        Assert.Equal(24, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies fixed-width captured alternations still return capture groups through the generic capture engine.
    /// </summary>
    [Fact]
    public void FixedWidthAlternationEngineKeepsGenericCaptures()
    {
        var automaton = RegexAutomaton.Compile(
            @"([cgt]gggtaaa)|tttaccc[acg]"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xx cgggtaaa yy"u8.ToArray();

        RegexCaptures? captures = automaton.FindCaptures(haystack);

        Assert.Equal(RegexEngineKind.FixedWidthAlternation, GetEngineKind(automaton));
        Assert.NotNull(captures);
        AssertGroupText(captures, haystack, 1, "cgggtaaa");
    }

    /// <summary>
    /// Verifies nested fixed-width alternations with exact repetitions use the fixed-width scanner.
    /// </summary>
    [Fact]
    public void FixedWidthAlternationEngineCountsCapturedAsciiKeyPattern()
    {
        var automaton = RegexAutomaton.Compile(
            @"((?:ASIA|AKIA|AROA|AIDA)([A-Z0-7]{16}))"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = "xx ASIAABCDEFGHIJKLMNOP yy AIDA0000000000000000 zz ASIAABCDEFGH12345678"u8.ToArray();
        int firstStart = haystack.AsSpan().IndexOf("ASIAABCDEFGHIJKLMNOP"u8);
        int secondStart = haystack.AsSpan().IndexOf("AIDA0000000000000000"u8);

        Assert.Equal(RegexEngineKind.FixedWidthAlternation, GetEngineKind(automaton));
        Assert.True(automaton.IsMatch(haystack));
        Assert.False(automaton.IsMatch("xx ASIAABCDEFGH12345678 yy"u8));
        Assert.Equal(new RegexMatch(firstStart, 20), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(secondStart, 20), automaton.MatchAt(haystack, secondStart));
        Assert.Equal(new RegexMatch(firstStart, 20), automaton.FindEarliest(haystack, startAt: 0));
        Assert.Equal(new RegexMatch(firstStart, 20), automaton.FindAllKindAt(haystack, firstStart));
        Assert.Equal([new RegexMatch(firstStart, 20)], automaton.FindOverlappingAt(haystack, firstStart));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(40, automaton.SumMatchSpans(haystack));

        RegexCaptures? captures = automaton.FindCaptures(haystack);

        Assert.NotNull(captures);
        AssertGroupText(captures, haystack, 1, "ASIAABCDEFGHIJKLMNOP");
        AssertGroupText(captures, haystack, 2, "ABCDEFGHIJKLMNOP");
    }

    /// <summary>
    /// Verifies small fixed-width byte alternations with classes keep exact matching semantics.
    /// </summary>
    [Fact]
    public void FixedWidthAlternationEngineCountsShortClassBranches()
    {
        var automaton = RegexAutomaton.Compile(
            @"a[NSt]|BY"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xx aN BY aQ aS BYY at"u8.ToArray();
        int firstStart = haystack.AsSpan().IndexOf("aN"u8);
        int secondStart = haystack.AsSpan().IndexOf("BY"u8);
        int thirdStart = haystack.AsSpan().IndexOf("aS"u8);

        Assert.Equal(RegexEngineKind.FixedWidthAlternation, GetEngineKind(automaton));
        Assert.True(automaton.IsMatch(haystack));
        Assert.Equal(new RegexMatch(firstStart, 2), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(secondStart, 2), automaton.Find(haystack, firstStart + 1));
        Assert.Equal(new RegexMatch(thirdStart, 2), automaton.MatchAt(haystack, thirdStart));
        Assert.Null(automaton.MatchAt(haystack, thirdStart + 1));
        Assert.Equal(5, automaton.CountMatches(haystack));
        Assert.Equal(10, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies small fixed-width three-byte alternations preserve exact branch pairing.
    /// </summary>
    [Fact]
    public void FixedWidthAlternationEngineCountsShortTripleBranches()
    {
        var automaton = RegexAutomaton.Compile(
            @"aND|caN|Ha[DS]|WaS"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xx HaD yy caN zz HaN aa WaS bb aND cc HaS"u8.ToArray();
        int firstStart = haystack.AsSpan().IndexOf("HaD"u8);
        int secondStart = haystack.AsSpan().IndexOf("caN"u8);
        int rejectedStart = haystack.AsSpan().IndexOf("HaN"u8);
        int lastStart = haystack.AsSpan().IndexOf("HaS"u8);

        Assert.Equal(RegexEngineKind.FixedWidthAlternation, GetEngineKind(automaton));
        Assert.True(automaton.IsMatch(haystack));
        Assert.Equal(new RegexMatch(firstStart, 3), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(secondStart, 3), automaton.Find(haystack, firstStart + 1));
        Assert.Equal(new RegexMatch(lastStart, 3), automaton.MatchAt(haystack, lastStart));
        Assert.Null(automaton.MatchAt(haystack, rejectedStart));
        Assert.Equal(5, automaton.CountMatches(haystack));
        Assert.Equal(15, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies fixed-width exact-set matching preserves branch pairings instead of accepting a per-position cross product.
    /// </summary>
    [Fact]
    public void FixedWidthAlternationEngineExactSetPreservesBranchPairings()
    {
        var automaton = RegexAutomaton.Compile(
            @"a[bc]|c[de]"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xx ad cb ab ce"u8.ToArray();
        int firstStart = haystack.AsSpan().IndexOf("ab"u8);
        int secondStart = haystack.AsSpan().IndexOf("ce"u8);

        Assert.Equal(RegexEngineKind.FixedWidthAlternation, GetEngineKind(automaton));
        Assert.Null(automaton.MatchAt(haystack, haystack.AsSpan().IndexOf("ad"u8)));
        Assert.Null(automaton.MatchAt(haystack, haystack.AsSpan().IndexOf("cb"u8)));
        Assert.Equal(new RegexMatch(firstStart, 2), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(secondStart, 2), automaton.Find(haystack, firstStart + 1));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(4, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies a single fixed-width sequence with a literal seed uses direct fixed-width scanning.
    /// </summary>
    [Fact]
    public void FixedWidthAlternationEngineCountsSingleClassSequence()
    {
        var automaton = RegexAutomaton.Compile(
            @"tHa[Nt]"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xx tHaN yy tHat zz tHaa"u8.ToArray();
        int firstStart = haystack.AsSpan().IndexOf("tHaN"u8);
        int secondStart = haystack.AsSpan().IndexOf("tHat"u8);
        int rejectedStart = haystack.AsSpan().IndexOf("tHaa"u8);

        Assert.Equal(RegexEngineKind.FixedWidthAlternation, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(firstStart, 4), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(secondStart, 4), automaton.Find(haystack, firstStart + 1));
        Assert.Equal(new RegexMatch(secondStart, 4), automaton.MatchAt(haystack, secondStart));
        Assert.Null(automaton.MatchAt(haystack, rejectedStart));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(8, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies a single fixed-width byte-class sequence without a literal seed uses direct fixed-width scanning.
    /// </summary>
    [Fact]
    public void FixedWidthAlternationEngineCountsSingleClassSequenceWithoutLiteralSeed()
    {
        var automaton = RegexAutomaton.Compile(
            @"[a-z][a-z][a-z][a-z][a-z]"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xx abcde yy ab12e zz pqrst"u8.ToArray();
        int firstStart = haystack.AsSpan().IndexOf("abcde"u8);
        int secondStart = haystack.AsSpan().IndexOf("pqrst"u8);
        int rejectedStart = haystack.AsSpan().IndexOf("ab12e"u8);

        Assert.Equal(RegexEngineKind.FixedWidthAlternation, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(firstStart, 5), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(secondStart, 5), automaton.Find(haystack, firstStart + 1));
        Assert.Equal(new RegexMatch(secondStart, 5), automaton.MatchAt(haystack, secondStart));
        Assert.Null(automaton.MatchAt(haystack, rejectedStart));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(10, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies simple start-anchored fixed-width patterns use the direct fixed-width matcher.
    /// </summary>
    [Fact]
    public void FixedWidthAlternationEngineHandlesStartAnchoredSequence()
    {
        var automaton = RegexAutomaton.Compile(
            @"^.bc(d|e)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        var absolute = RegexAutomaton.Compile(
            @"\A.bc(d|e)"u8,
            caseInsensitive: false,
            multiLine: true,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        var multiline = RegexAutomaton.Compile(
            @"^.bc(d|e)"u8,
            caseInsensitive: false,
            multiLine: true,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(RegexEngineKind.FixedWidthAlternation, GetEngineKind(automaton));
        Assert.Equal(RegexEngineKind.FixedWidthAlternation, GetEngineKind(absolute));
        Assert.NotEqual(RegexEngineKind.FixedWidthAlternation, GetEngineKind(multiline));

        Assert.Equal(new RegexMatch(0, 4), automaton.Find("abcdefghijklmnopqrstuvwxyz"u8));
        Assert.Equal(new RegexMatch(0, 4), automaton.MatchAt("abcdefghijklmnopqrstuvwxyz"u8, 0));
        Assert.True(automaton.IsMatch("abcdefghijklmnopqrstuvwxyz"u8));
        Assert.Equal(1, automaton.CountMatches("abcdefghijklmnopqrstuvwxyz"u8));
        Assert.Equal(4, automaton.SumMatchSpans("abcdefghijklmnopqrstuvwxyz"u8));

        Assert.Null(automaton.Find("xabcdefghijklmnopqrstuvwxyz"u8));
        Assert.Null(automaton.Find("abcdefghijklmnopqrstuvwxyz"u8, startAt: 1));
        Assert.Null(automaton.MatchAt("abcdefghijklmnopqrstuvwxyz"u8, startAt: 1));
        Assert.Equal(0, automaton.CountMatches("abcdefghijklmnopqrstuvwxyz"u8, startAt: 1));
        Assert.Equal(0, automaton.SumMatchSpans("abcdefghijklmnopqrstuvwxyz"u8, startAt: 1));
    }

    /// <summary>
    /// Verifies leading ASCII class plus literal suffix patterns scan from suffix candidates.
    /// </summary>
    [Fact]
    public void LeadingClassLiteralEngineCountsSuffixSpans()
    {
        var alternation = RegexAutomaton.Compile(
            @"([A-Za-z]awyer|[A-Za-z]inn)\s"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        var single = RegexAutomaton.Compile(
            @"[a-z]shing"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        var trailingClass = RegexAutomaton.Compile(
            @"([A-Z]awyer|[A-Z]inn)[0-9\s]"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(RegexEngineKind.LeadingClassLiteral, GetEngineKind(alternation));
        Assert.Equal(RegexEngineKind.LeadingClassLiteral, GetEngineKind(single));
        Assert.Equal(RegexEngineKind.LeadingClassLiteral, GetEngineKind(trailingClass));

        byte[] alternationHaystack = "Sawyer Finn xawyer! Zinn\tbinn xyer "u8.ToArray();
        Assert.Equal(new RegexMatch(0, 7), alternation.Find(alternationHaystack));
        Assert.Equal(new RegexMatch(7, 5), alternation.Find(alternationHaystack, startAt: 1));
        Assert.Equal(new RegexMatch(20, 5), alternation.Find(alternationHaystack, startAt: 13));
        Assert.Equal(new RegexMatch(0, 7), alternation.MatchAt(alternationHaystack, 0));
        Assert.Null(alternation.MatchAt(alternationHaystack, 12));
        Assert.Equal(4, alternation.CountMatches(alternationHaystack));
        Assert.Equal(22, alternation.SumMatchSpans(alternationHaystack));

        Assert.Equal(2, single.CountMatches("ashing Zshing bshing"u8));
        Assert.Equal(12, single.SumMatchSpans("ashing Zshing bshing"u8));
        Assert.Equal(2, trailingClass.CountMatches("Sawyer7 Finn\t sawyer "u8));
        Assert.Equal(12, trailingClass.SumMatchSpans("Sawyer7 Finn\t sawyer "u8));
    }

    /// <summary>
    /// Verifies leading-class literal candidates preserve branch fallback when literals overlap.
    /// </summary>
    [Fact]
    public void LeadingClassLiteralEngineHandlesOverlappingLiterals()
    {
        var automaton = RegexAutomaton.Compile(
            @"[A-Za-z]in|[A-Za-z]inn"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xx xinn zinn"u8.ToArray();
        int firstStart = haystack.AsSpan().IndexOf("xin"u8);
        int secondStart = haystack.AsSpan().IndexOf("zin"u8);

        Assert.Equal(RegexEngineKind.LeadingClassLiteral, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(firstStart, 3), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(secondStart, 3), automaton.Find(haystack, firstStart + 1));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(6, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies word-boundary literal alternations scan from literal candidates.
    /// </summary>
    [Fact]
    public void WordBoundaryLiteralSetEngineCountsFlatAlternationSpans()
    {
        var ascii = RegexAutomaton.Compile(
            @"\b(foo|foobar|bar)\b"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        var unicode = RegexAutomaton.Compile(
            @"\b(foo|foobar|bar)\b"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: true,
            unicodeClasses: true);
        byte[] asciiHaystack = "!!foobar foo xfoo bar_ bar"u8.ToArray();
        byte[] unicodeHaystack = System.Text.Encoding.UTF8.GetBytes("δfoo foo βbar bar!");

        Assert.Equal(RegexEngineKind.WordBoundaryLiteralSet, GetEngineKind(ascii));
        Assert.Equal(RegexEngineKind.WordBoundaryLiteralSet, GetEngineKind(unicode));
        Assert.Equal(new RegexMatch(2, 6), ascii.Find(asciiHaystack));
        Assert.Equal(new RegexMatch(9, 3), ascii.Find(asciiHaystack, startAt: 3));
        Assert.Equal(new RegexMatch(2, 6), ascii.MatchAt(asciiHaystack, 2));
        Assert.Null(ascii.MatchAt(asciiHaystack, 4));
        Assert.Equal(3, ascii.CountMatches(asciiHaystack));
        Assert.Equal(12, ascii.SumMatchSpans(asciiHaystack));
        Assert.Equal(new RegexMatch(6, 3), unicode.Find(unicodeHaystack));
        Assert.Equal(2, unicode.CountMatches(unicodeHaystack));
        Assert.Equal(6, unicode.SumMatchSpans(unicodeHaystack));
    }

    /// <summary>
    /// Verifies word-boundary literal branches generated from per-line alternation use the same engine.
    /// </summary>
    [Fact]
    public void WordBoundaryLiteralSetEngineCountsGeneratedBoundaryBranches()
    {
        var automaton = RegexAutomaton.Compile(
            @"(?:\b(foo)\b)|(?:\b(foobar)\b)|(?:\b(bar)\b)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "!!foobar foo xfoo bar_ bar"u8.ToArray();

        Assert.Equal(RegexEngineKind.WordBoundaryLiteralSet, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(2, 6), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(9, 3), automaton.Find(haystack, startAt: 3));
        Assert.Equal(3, automaton.CountMatches(haystack));
        Assert.Equal(12, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies large generated word-boundary branch alternations bypass the generic alternation set.
    /// </summary>
    [Fact]
    public void WordBoundaryLiteralSetEnginePrecedesLargeAlternationSet()
    {
        string pattern = string.Join(
            "|",
            Enumerable.Range(0, 64).Select(static index => $@"(?:\b(kw{index})\b)"));
        var automaton = RegexAutomaton.Compile(
            System.Text.Encoding.ASCII.GetBytes(pattern),
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(RegexEngineKind.WordBoundaryLiteralSet, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(3, 4), automaton.Find("xx kw42 kw7"u8));
        Assert.Equal(2, automaton.CountMatches("xx kw42 kw7"u8));
        Assert.Equal(7, automaton.SumMatchSpans("xx kw42 kw7"u8));
    }

    /// <summary>
    /// Verifies finite factored word-boundary literal languages expand in regex order.
    /// </summary>
    [Fact]
    public void WordBoundaryLiteralSetEngineExpandsFactoredKeywordTrie()
    {
        var automaton = RegexAutomaton.Compile(
            @"\b(i(?:1(?:28|6)|32|64|mpl|size|[8fn])|t(?:r(?:ait|ue|y)|ype(?:(?:of)?))|u(?:ns(?:afe|ized)|s(?:(?:(?:iz)?)e)))\b"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "type typeof typex use usize unsized unsafe if in i128 impl try true trait"u8.ToArray();

        Assert.Equal(RegexEngineKind.WordBoundaryLiteralSet, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 4), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(5, 6), automaton.MatchAt(haystack, 5));
        Assert.Null(automaton.MatchAt(haystack, 12));
        Assert.Equal(13, automaton.CountMatches(haystack));
        Assert.Equal(55, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies byte-mode greedy dot-star uses direct line span execution.
    /// </summary>
    [Fact]
    public void DotStarEngineCountsLineSpans()
    {
        var automaton = RegexAutomaton.Compile(
            ".*"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "ab\nc\n"u8.ToArray();

        Assert.Equal(RegexEngineKind.DotStar, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 2), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(2, 0), automaton.Find(haystack, startAt: 2));
        Assert.Equal(3, automaton.CountMatches(haystack));
        Assert.Equal(3, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies byte-mode dot-all greedy dot-star consumes the full suffix directly.
    /// </summary>
    [Fact]
    public void DotStarEngineCountsDotAllSpan()
    {
        var automaton = RegexAutomaton.Compile(
            "(?s).*"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "ab\nc"u8.ToArray();

        Assert.Equal(RegexEngineKind.DotStar, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 4), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(2, 2), automaton.Find(haystack, startAt: 2));
        Assert.Equal(new RegexMatch(4, 0), automaton.Find(haystack, startAt: 4));
        Assert.Equal(1, automaton.CountMatches(haystack));
        Assert.Equal(4, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies Unicode scalar dot-all plus consumes contiguous valid UTF-8 scalar spans directly.
    /// </summary>
    [Fact]
    public void DotStarEngineCountsUnicodeDotAllPlusSpan()
    {
        var automaton = RegexAutomaton.Compile(
            "(?s:.)+"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = [(byte)'a', 0xC3, 0xA9, (byte)'\n', 0xD0, 0x96];

        Assert.Equal(RegexEngineKind.DotStar, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, haystack.Length), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(3, 3), automaton.MatchAt(haystack, startAt: 3));
        Assert.Null(automaton.Find(ReadOnlySpan<byte>.Empty));
        Assert.Equal(1, automaton.CountMatches(haystack));
        Assert.Equal(haystack.Length, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies Unicode scalar dot-all plus splits around invalid UTF-8.
    /// </summary>
    [Fact]
    public void DotStarEngineSplitsUnicodeDotAllPlusAtInvalidUtf8()
    {
        var automaton = RegexAutomaton.Compile(
            "(?s:.)+"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = [0xFF, (byte)'a', 0xC3, 0xA9, 0xFF, (byte)'b'];

        Assert.Equal(RegexEngineKind.DotStar, GetEngineKind(automaton));
        Assert.Null(automaton.MatchAt(haystack, startAt: 0));
        Assert.Equal(new RegexMatch(1, 3), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(5, 1), automaton.Find(haystack, startAt: 4));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(4, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies Unicode scalar dot-star preserves empty matches around invalid UTF-8.
    /// </summary>
    [Fact]
    public void DotStarEngineCountsUnicodeDotAllStarAroundInvalidUtf8()
    {
        var automaton = RegexAutomaton.Compile(
            "(?s).*"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = [0xFF, (byte)'a'];

        Assert.Equal(RegexEngineKind.DotStar, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 0), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(1, 1), automaton.Find(haystack, startAt: 1));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(1, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies byte-mode multiline whole-line patterns use direct line scanning.
    /// </summary>
    [Fact]
    public void WholeLineEngineCountsMultilineDotStarAnchors()
    {
        var automaton = RegexAutomaton.Compile(
            "(?m)^.*$"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "ab\n\nc\n"u8.ToArray();

        Assert.Equal(RegexEngineKind.WholeLine, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 2), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(3, 0), automaton.Find(haystack, startAt: 1));
        Assert.Equal(new RegexMatch(3, 0), automaton.Find(haystack, startAt: 2));
        Assert.Equal(new RegexMatch(4, 1), automaton.Find(haystack, startAt: 4));
        Assert.Equal(new RegexMatch(6, 0), automaton.Find(haystack, startAt: 5));
        Assert.Equal(new RegexMatch(0, 2), automaton.MatchAt(haystack, 0));
        Assert.Null(automaton.MatchAt(haystack, 1));
        Assert.Equal(new RegexMatch(3, 0), automaton.MatchAt(haystack, 3));
        Assert.Equal(new RegexMatch(6, 0), automaton.MatchAt(haystack, 6));
        Assert.Equal(4, automaton.CountMatches(haystack));
        Assert.Equal(3, automaton.SumMatchSpans(haystack));
        Assert.Equal(3, automaton.CountMatches(haystack, startAt: 1));
        Assert.Equal(1, automaton.SumMatchSpans(haystack, startAt: 1));
        Assert.Equal(1, automaton.CountMatches(ReadOnlySpan<byte>.Empty));
        Assert.Equal(0, automaton.SumMatchSpans(ReadOnlySpan<byte>.Empty));
    }

    /// <summary>
    /// Verifies multiline whole-line specialization is skipped when UTF-8 scalar boundaries matter.
    /// </summary>
    [Fact]
    public void WholeLineEngineSkipsUtf8Mode()
    {
        var automaton = RegexAutomaton.Compile("(?m)^.*$"u8);

        Assert.NotEqual(RegexEngineKind.WholeLine, GetEngineKind(automaton));
    }

    /// <summary>
    /// Verifies greedy dot-star specialization preserves UTF-8 scalar boundaries.
    /// </summary>
    [Fact]
    public void DotStarEngineCountsUtf8DotAllSpan()
    {
        var automaton = RegexAutomaton.Compile("(?s).*"u8);
        var inlineUtf8 = RegexAutomaton.Compile(
            "(?u:.*)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = [(byte)'a', 0xC3, 0xA9];

        Assert.Equal(RegexEngineKind.DotStar, GetEngineKind(automaton));
        Assert.Equal(RegexEngineKind.DotStar, GetEngineKind(inlineUtf8));
        Assert.Equal(new RegexMatch(0, haystack.Length), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(haystack.Length, 0), automaton.Find(haystack, startAt: 2));
        Assert.Null(automaton.MatchAt(haystack, startAt: 2));
        Assert.Equal(1, automaton.CountMatches(haystack));
        Assert.Equal(haystack.Length, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies byte-mode IPv4 address patterns use direct dotted-octet scanning.
    /// </summary>
    [Fact]
    public void Ipv4AddressEngineCountsDottedOctets()
    {
        var automaton = RegexAutomaton.Compile(
            @"(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = "x192.168.00.01 y25.25.25.25 z1.2.3.4"u8.ToArray();

        Assert.Equal(RegexEngineKind.Ipv4Address, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(1, 13), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(16, 11), automaton.MatchAt(haystack, startAt: 16));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(24, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies IPv4 address scanning preserves unanchored starts inside larger digit runs.
    /// </summary>
    [Fact]
    public void Ipv4AddressEngineFindsLaterValidDigitStart()
    {
        var automaton = RegexAutomaton.Compile(
            @"(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);

        Assert.Equal(new RegexMatch(2, 11), automaton.Find("x256.00.00.00"u8));
    }

    /// <summary>
    /// Verifies IPv4 address specialization is skipped when UTF-8 scalar boundaries matter.
    /// </summary>
    [Fact]
    public void Ipv4AddressEngineSkipsUtf8Mode()
    {
        var automaton = RegexAutomaton.Compile(
            @"(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])"u8);

        Assert.NotEqual(RegexEngineKind.Ipv4Address, GetEngineKind(automaton));
    }

    /// <summary>
    /// Verifies byte-mode email address patterns scan around the at-sign delimiter.
    /// </summary>
    [Fact]
    public void EmailAddressEngineCountsDottedDomains()
    {
        var automaton = RegexAutomaton.Compile(
            @"[\w\.+-]+@[\w\.-]+\.[\w\.-]+"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "x a.b+c-d@example.test nope@nodot good@sub.domain.tld!"u8.ToArray();

        Assert.Equal(RegexEngineKind.EmailAddress, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(2, 20), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(34, 19), automaton.Find(haystack, startAt: 22));
        Assert.Equal(new RegexMatch(2, 20), automaton.MatchAt(haystack, startAt: 2));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(39, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies email address scanning can start inside a longer local token.
    /// </summary>
    [Fact]
    public void EmailAddressEngineHonorsStartAtInsideLocalPart()
    {
        var automaton = RegexAutomaton.Compile(
            @"[\w\.+-]+@[\w\.-]+\.[\w\.-]+"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(new RegexMatch(4, 7), automaton.Find("xx abc@d.ef"u8, startAt: 4));
        Assert.Equal(1, automaton.CountMatches("xx abc@d.ef"u8, startAt: 4));
        Assert.Equal(7, automaton.SumMatchSpans("xx abc@d.ef"u8, startAt: 4));
        Assert.Null(automaton.Find("name@nodot"u8));
    }

    /// <summary>
    /// Verifies email address specialization is skipped when UTF-8 or Unicode class semantics matter.
    /// </summary>
    [Fact]
    public void EmailAddressEngineSkipsUtf8UnicodeMode()
    {
        var automaton = RegexAutomaton.Compile(@"[\w\.+-]+@[\w\.-]+\.[\w\.-]+"u8);

        Assert.NotEqual(RegexEngineKind.EmailAddress, GetEngineKind(automaton));
    }

    /// <summary>
    /// Verifies the lh3 email shape scans around the at-sign delimiter directly.
    /// </summary>
    [Fact]
    public void EmailAddressEngineCountsLh3CapturedEmails()
    {
        var automaton = RegexAutomaton.Compile(
            @"([^ @]+)@([^ @]+)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "x a@b no@ name@domain next@test"u8.ToArray();
        int firstStart = haystack.AsSpan().IndexOf("a@b"u8);
        int secondStart = haystack.AsSpan().IndexOf("name@domain"u8);
        int thirdStart = haystack.AsSpan().IndexOf("next@test"u8);

        RegexCaptures? captures = automaton.FindCaptures(haystack);

        Assert.Equal(RegexEngineKind.EmailAddress, GetEngineKind(automaton));
        Assert.True(automaton.IsMatch(haystack));
        Assert.False(automaton.IsMatch("no at sign here"u8));
        Assert.False(automaton.IsMatch("@domain"u8));
        Assert.False(automaton.IsMatch("name@"u8));
        Assert.False(automaton.IsMatch("name@ domain"u8));
        Assert.False(automaton.IsMatch("name @domain"u8));
        Assert.Equal(new RegexMatch(firstStart, 3), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(secondStart, 11), automaton.Find(haystack, firstStart + 3));
        Assert.Equal(new RegexMatch(thirdStart, 9), automaton.MatchAt(haystack, thirdStart));
        Assert.Equal(3, automaton.CountMatches(haystack));
        Assert.Equal(23, automaton.SumMatchSpans(haystack));
        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(firstStart, 3), captures.Match);
        AssertGroupText(captures, haystack, 1, "a");
        AssertGroupText(captures, haystack, 2, "b");
    }

    /// <summary>
    /// Verifies byte-mode URI patterns scan around the scheme delimiter.
    /// </summary>
    [Fact]
    public void UriEngineCountsDelimitedUris()
    {
        var automaton = RegexAutomaton.Compile(
            @"[\w]+://[^/\s?#]+[^\s?#]+(?:\?[^\s#]*)?(?:#[^\s]*)?"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "x http://example.com/path?q=1#frag ftp://a/b no://x? bad ://missing"u8.ToArray();

        Assert.Equal(RegexEngineKind.Uri, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(2, 32), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(35, 9), automaton.Find(haystack, startAt: 34));
        Assert.Equal(new RegexMatch(2, 32), automaton.MatchAt(haystack, startAt: 2));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(41, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies the lh3 URI shape scans around the scheme delimiter directly.
    /// </summary>
    [Fact]
    public void UriEngineCountsLh3CapturedUris()
    {
        var automaton = RegexAutomaton.Compile(
            @"([a-zA-Z][a-zA-Z0-9]*)://([^ /]+)(/[^ ]*)?"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "x 1http://ab http://example.com/path?q#frag ftp://a/b no://x? bad ://missing"u8.ToArray();
        ReadOnlySpan<byte> first = "http://ab"u8;
        ReadOnlySpan<byte> second = "http://example.com/path?q#frag"u8;
        ReadOnlySpan<byte> third = "ftp://a/b"u8;
        ReadOnlySpan<byte> fourth = "no://x?"u8;
        int firstStart = haystack.AsSpan().IndexOf(first);
        int secondStart = haystack.AsSpan().IndexOf(second);
        int thirdStart = haystack.AsSpan().IndexOf(third);
        int fourthStart = haystack.AsSpan().IndexOf(fourth);

        RegexCaptures? captures = automaton.FindCaptures(haystack);

        Assert.Equal(RegexEngineKind.Uri, GetEngineKind(automaton));
        Assert.True(automaton.IsMatch(haystack));
        Assert.False(automaton.IsMatch("bad ://missing"u8));
        Assert.Equal(new RegexMatch(firstStart, first.Length), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(secondStart, second.Length), automaton.Find(haystack, firstStart + first.Length));
        Assert.Equal(new RegexMatch(thirdStart, third.Length), automaton.MatchAt(haystack, thirdStart));
        Assert.Equal(4, automaton.CountMatches(haystack));
        Assert.Equal(first.Length + second.Length + third.Length + fourth.Length, automaton.SumMatchSpans(haystack));
        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(firstStart, first.Length), captures.Match);
        AssertGroupText(captures, haystack, 1, "http");
        AssertGroupText(captures, haystack, 2, "ab");
        Assert.Null(captures.GetGroup(3));
    }

    /// <summary>
    /// Verifies the lh3 URI-or-email alternation scans both delimiters directly.
    /// </summary>
    [Fact]
    public void UriOrEmailEngineCountsLh3Alternation()
    {
        var automaton = RegexAutomaton.Compile(
            @"([a-zA-Z][a-zA-Z0-9]*)://([^ /]+)(/[^ ]*)?|([^ @]+)@([^ @]+)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "x a@b http://example.com/path?q#frag ftp://a/b z@y http://a@b"u8.ToArray();
        ReadOnlySpan<byte> first = "a@b"u8;
        ReadOnlySpan<byte> second = "http://example.com/path?q#frag"u8;
        ReadOnlySpan<byte> third = "ftp://a/b"u8;
        ReadOnlySpan<byte> fourth = "z@y"u8;
        ReadOnlySpan<byte> fifth = "http://a@b"u8;
        int firstStart = haystack.AsSpan().IndexOf(first);
        int secondStart = haystack.AsSpan().IndexOf(second);
        int thirdStart = haystack.AsSpan().IndexOf(third);
        int fourthStart = haystack.AsSpan().IndexOf(fourth);
        int fifthStart = haystack.AsSpan().IndexOf(fifth);

        RegexCaptures? emailCaptures = automaton.FindCaptures(haystack);
        RegexCaptures? uriCaptures = automaton.FindCaptures(haystack, firstStart + first.Length);

        Assert.Equal(RegexEngineKind.UriOrEmail, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(firstStart, first.Length), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(secondStart, second.Length), automaton.Find(haystack, firstStart + first.Length));
        Assert.Equal(new RegexMatch(thirdStart, third.Length), automaton.MatchAt(haystack, thirdStart));
        Assert.Equal(new RegexMatch(fifthStart, fifth.Length), automaton.Find(haystack, fourthStart + fourth.Length));
        Assert.Equal(5, automaton.CountMatches(haystack));
        Assert.Equal(first.Length + second.Length + third.Length + fourth.Length + fifth.Length, automaton.SumMatchSpans(haystack));
        Assert.NotNull(emailCaptures);
        Assert.Equal(new RegexMatch(firstStart, first.Length), emailCaptures.Match);
        AssertGroupText(emailCaptures, haystack, 4, "a");
        AssertGroupText(emailCaptures, haystack, 5, "b");
        Assert.Null(emailCaptures.GetGroup(1));
        Assert.NotNull(uriCaptures);
        Assert.Equal(new RegexMatch(secondStart, second.Length), uriCaptures.Match);
        AssertGroupText(uriCaptures, haystack, 1, "http");
        AssertGroupText(uriCaptures, haystack, 2, "example.com");
        AssertGroupText(uriCaptures, haystack, 3, "/path?q#frag");
    }

    /// <summary>
    /// Verifies URI scanning can start inside a longer scheme token.
    /// </summary>
    [Fact]
    public void UriEngineHonorsStartAtInsideScheme()
    {
        var automaton = RegexAutomaton.Compile(
            @"[\w]+://[^/\s?#]+[^\s?#]+(?:\?[^\s#]*)?(?:#[^\s]*)?"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(new RegexMatch(4, 8), automaton.Find("xx http://ab"u8, startAt: 4));
        Assert.Equal(1, automaton.CountMatches("xx http://ab"u8, startAt: 4));
        Assert.Equal(8, automaton.SumMatchSpans("xx http://ab"u8, startAt: 4));
        Assert.Null(automaton.Find("http://a?"u8));
    }

    /// <summary>
    /// Verifies the lh3 date shape scans around slash delimiters directly.
    /// </summary>
    [Fact]
    public void DateEngineCountsLh3CapturedDates()
    {
        var automaton = RegexAutomaton.Compile(
            @"([0-9][0-9]?)/([0-9][0-9]?)/([0-9][0-9]([0-9][0-9])?)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "x 6/7/98 12/31/1999 1/2/345 123x/45/67"u8.ToArray();
        ReadOnlySpan<byte> first = "6/7/98"u8;
        ReadOnlySpan<byte> second = "12/31/1999"u8;
        ReadOnlySpan<byte> third = "1/2/34"u8;
        int firstStart = haystack.AsSpan().IndexOf(first);
        int secondStart = haystack.AsSpan().IndexOf(second);
        int thirdStart = haystack.AsSpan().IndexOf(third);

        RegexCaptures? captures = automaton.FindCaptures(haystack);

        Assert.Equal(RegexEngineKind.Date, GetEngineKind(automaton));
        Assert.True(automaton.IsMatch(haystack));
        Assert.False(automaton.IsMatch("no date here / x"u8));
        Assert.Equal(new RegexMatch(firstStart, first.Length), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(secondStart, second.Length), automaton.Find(haystack, firstStart + 1));
        Assert.Equal(new RegexMatch(thirdStart, third.Length), automaton.MatchAt(haystack, thirdStart));
        Assert.Equal(3, automaton.CountMatches(haystack));
        Assert.Equal(first.Length + second.Length + third.Length, automaton.SumMatchSpans(haystack));
        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(firstStart, first.Length), captures.Match);
        AssertGroupText(captures, haystack, 1, "6");
        AssertGroupText(captures, haystack, 2, "7");
        AssertGroupText(captures, haystack, 3, "98");
        Assert.Null(captures.GetGroup(4));
    }

    /// <summary>
    /// Verifies date specialization is skipped when UTF-8 or Unicode class semantics matter.
    /// </summary>
    [Fact]
    public void DateEngineSkipsUtf8UnicodeMode()
    {
        var automaton = RegexAutomaton.Compile(
            @"([0-9][0-9]?)/([0-9][0-9]?)/([0-9][0-9]([0-9][0-9])?)"u8);

        Assert.NotEqual(RegexEngineKind.Date, GetEngineKind(automaton));
    }

    /// <summary>
    /// Verifies URI specialization is skipped when UTF-8 or Unicode class semantics matter.
    /// </summary>
    [Fact]
    public void UriEngineSkipsUtf8UnicodeMode()
    {
        var automaton = RegexAutomaton.Compile(@"[\w]+://[^/\s?#]+[^\s?#]+(?:\?[^\s#]*)?(?:#[^\s]*)?"u8);

        Assert.NotEqual(RegexEngineKind.Uri, GetEngineKind(automaton));
    }

    /// <summary>
    /// Verifies prefixed greedy dot-star tails preserve leftmost spans in lazy DFA execution.
    /// </summary>
    [Fact]
    public void LazyDfaCountsPrefixedDotStarTailSpans()
    {
        byte[] pattern = System.Text.Encoding.ASCII.GetBytes(
            """(?:(?:"|'|\]|\}|\\|\d|(?:nan|infinity|true|false|null|undefined|symbol|math)|`|-|\+)+[)]*;?((?:\s|-|~|!|\{\}|\|\||\+)*.*(?:.*=.*)))""");
        byte[] haystack = "math x=xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"u8.ToArray();
        var automaton = RegexAutomaton.Compile(
            pattern,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(RegexEngineKind.LazyDfa, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 107), automaton.Find(haystack));
        Assert.Equal(1, automaton.CountMatches(haystack));
        Assert.Equal(107, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies unanchored lazy DFA search can reverse-match branches containing multi-byte literals.
    /// </summary>
    [Fact]
    public void UnanchoredLazyDfaFindsMultiByteLiteralBranches()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"(?:abc|ab)\d+"u8);
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.True(RegexUnanchoredLazyDfa.TryCreate(tree.Root, options, dfaSizeLimit: 1024 * 1024, out RegexUnanchoredLazyDfa? dfa));
        Assert.NotNull(dfa);

        byte[] haystack = "zz abc123 xx ab45"u8.ToArray();

        Assert.True(dfa.TryFind(haystack, startAt: 0, out RegexMatch match, out bool gaveUp));
        Assert.False(gaveUp);
        Assert.Equal(new RegexMatch(3, 6), match);
        Assert.True(dfa.TrySumMatchSpans(haystack, startAt: 0, out long spanSum));
        Assert.Equal(10, spanSum);

        RegexSyntaxTree priorityTree = RegexSyntaxParser.Parse(@"(?:ab|b)"u8);
        Assert.True(RegexUnanchoredLazyDfa.TryCreate(priorityTree.Root, options, dfaSizeLimit: 1024 * 1024, out RegexUnanchoredLazyDfa? priorityDfa));
        Assert.NotNull(priorityDfa);
        Assert.True(priorityDfa.TryFind("zab"u8, startAt: 0, out RegexMatch priorityMatch, out gaveUp));
        Assert.False(gaveUp);
        Assert.Equal(new RegexMatch(1, 2), priorityMatch);
    }

    /// <summary>
    /// Verifies dot-star/class fallback alternations avoid quadratic all-match iteration.
    /// </summary>
    [Fact]
    public void DotStarClassFallbackCountsWithoutSuffixRescans()
    {
        var automaton = RegexAutomaton.Compile(".*[^A-Z]|[A-Z]"u8);

        Assert.Equal(RegexEngineKind.DotStarClassFallback, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 1), automaton.Find("AAAA"u8));
        Assert.Equal(4, automaton.CountMatches("AAAA"u8));
        Assert.Equal(4, automaton.SumMatchSpans("AAAA"u8));
        Assert.Equal(new RegexMatch(0, 3), automaton.Find("AA-A"u8));
        Assert.Equal(2, automaton.CountMatches("AA-A"u8));
        Assert.Equal(4, automaton.SumMatchSpans("AA-A"u8));
    }

    /// <summary>
    /// Verifies large whole-branch capture alternations can report captures from the winning branch.
    /// </summary>
    [Fact]
    public void AlternationSetSynthesizesWholeBranchCaptures()
    {
        string pattern = string.Join(
            "|",
            Enumerable.Range(0, 16).Select(static index => $"(?:(tok{index}[xy]))"));
        var automaton = RegexAutomaton.Compile(System.Text.Encoding.ASCII.GetBytes(pattern));

        RegexCaptures? captures = automaton.FindCaptures("zz tok7x tok3y"u8);

        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(3, 5), captures.Match);
        Assert.Equal(17, captures.GroupCount);
        Assert.Equal(2, captures.ParticipatingCount());
        Assert.Equal(new RegexMatch(3, 5), captures.GetGroup(8));
    }

    /// <summary>
    /// Verifies nested branch flattening for search keeps top-level branch captures intact.
    /// </summary>
    [Fact]
    public void AlternationSetKeepsSyntheticCapturesWhenSearchSetFlattens()
    {
        string pattern = string.Join(
            "|",
            Enumerable.Range(0, 16).Select(static index => $"(?:((?:tok{index}[xy]|tok{index}z[0-9])))"));
        var automaton = RegexAutomaton.Compile(System.Text.Encoding.ASCII.GetBytes(pattern));

        RegexCaptures? captures = automaton.FindCaptures("zz tok7y tok3x"u8);

        Assert.Equal(RegexEngineKind.AlternationSet, GetEngineKind(automaton));
        Assert.True(automaton.UsesSyntheticCaptureAlternationSet);
        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(3, 5), captures.Match);
        Assert.Equal(17, captures.GroupCount);
        Assert.Equal(2, captures.ParticipatingCount());
        Assert.Equal(new RegexMatch(3, 5), captures.GetGroup(8));
    }

    /// <summary>
    /// Verifies a whole-pattern enclosing group does not hide large top-level alternations from the set engine.
    /// </summary>
    [Fact]
    public void AlternationSetUnwrapsWholePatternGroup()
    {
        string pattern = "(" + string.Join(
            "|",
            Enumerable.Range(0, 16).Select(static index => $"tok{index}[0-9]")) + ")";
        var automaton = RegexAutomaton.Compile(System.Text.Encoding.ASCII.GetBytes(pattern));

        Assert.Equal(RegexEngineKind.AlternationSet, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(3, 5), automaton.Find("xx tok73"u8));
    }

    /// <summary>
    /// Verifies case-insensitive literal alternatives can still use set execution.
    /// </summary>
    [Fact]
    public void AlternationSetSupportsCaseInsensitiveLiteralAlternatives()
    {
        string pattern = "(" + string.Join(
            "|",
            Enumerable.Range(0, 16).Select(static index => $"token{index}").Append("item[0-9]")) + ")";
        var automaton = RegexAutomaton.Compile(
            System.Text.Encoding.ASCII.GetBytes(pattern),
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false);

        Assert.Equal(RegexEngineKind.AlternationSet, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(3, 6), automaton.Find("xx TOKEN7 token3"u8));
    }

    /// <summary>
    /// Verifies large alternations with an unaccelerated branch do not use per-branch set execution.
    /// </summary>
    [Fact]
    public void AlternationSetRejectsUnacceleratedBranches()
    {
        string pattern = "(" + string.Join(
            "|",
            Enumerable.Range(0, 16).Select(static index => $"token{index}[0-9]").Append(@"\d+")) + ")";
        var automaton = RegexAutomaton.Compile(System.Text.Encoding.ASCII.GetBytes(pattern));

        Assert.NotEqual(RegexEngineKind.AlternationSet, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(3, 3), automaton.Find("xx 123 token3"u8));
    }

    /// <summary>
    /// Verifies grouped alternatives inside top-level alternatives are flattened for set execution.
    /// </summary>
    [Fact]
    public void AlternationSetFlattensNestedGroupedAlternatives()
    {
        string left = "(" + string.Join(
            "|",
            Enumerable.Range(0, 8).Select(static index => $"left{index}[0-9]")) + ")";
        string right = "(" + string.Join(
            "|",
            Enumerable.Range(0, 8).Select(static index => $"right{index}[0-9]")) + "){1,1}";
        var automaton = RegexAutomaton.Compile(System.Text.Encoding.ASCII.GetBytes(left + "|" + right));

        Assert.Equal(RegexEngineKind.AlternationSet, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(3, 6), automaton.Find("xx left42 right73"u8));
        Assert.Equal(new RegexMatch(10, 7), automaton.Find("xx left42 right73"u8, startAt: 9));
    }

    /// <summary>
    /// Verifies fallback NFA APIs work for alternation-set engines after lazy initialization.
    /// </summary>
    [Fact]
    public void AlternationSetLazilyInitializesFallbackNfa()
    {
        string pattern = "(" + string.Join(
            "|",
            Enumerable.Range(0, 16)
                .Select(static index => $"tok{index}[0-9]")
                .Append("tok7[0-9][a-z]")) + ")";
        var automaton = RegexAutomaton.Compile(System.Text.Encoding.ASCII.GetBytes(pattern));

        RegexMatch? earliest = automaton.FindEarliest("zz tok73a"u8, startAt: 0);
        RegexMatch? allKind = automaton.FindAllKindAt("tok73a"u8, startAt: 0);
        IReadOnlyList<RegexMatch> overlapping = automaton.FindOverlappingAt("tok73a"u8, startAt: 0);

        Assert.Equal(RegexEngineKind.AlternationSet, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(3, 5), earliest);
        Assert.Equal(new RegexMatch(0, 6), allKind);
        Assert.Contains(new RegexMatch(0, 5), overlapping);
        Assert.Contains(new RegexMatch(0, 6), overlapping);
    }

    /// <summary>
    /// Verifies anchored delimiter-separated capture patterns report field captures.
    /// </summary>
    [Fact]
    public void DelimitedCaptureEngineReportsFieldCaptures()
    {
        var automaton = RegexAutomaton.Compile(
            "^([A-Z0-9]+);([^;]*);([YN])$"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        RegexCaptures? captures = automaton.FindCaptures("0041;;Y"u8);

        Assert.Equal(RegexEngineKind.DelimitedCapture, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 7), automaton.Find("0041;;Y"u8));
        Assert.Equal(1, automaton.CountMatches("0041;;Y"u8));
        Assert.Equal(7, automaton.SumMatchSpans("0041;;Y"u8));
        Assert.Equal(4, automaton.CountCaptures("0041;;Y"u8));
        Assert.Equal(0, automaton.CountMatches("x0041;;Y"u8));
        Assert.Equal(0, automaton.CountCaptures("x0041;;Y"u8));
        Assert.Equal(0, automaton.CountMatches("0041;;Y"u8, startAt: 1));
        Assert.Equal(0, automaton.CountCaptures("0041;;Y"u8, startAt: 1));
        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, 7), captures.Match);
        Assert.Equal(4, captures.GroupCount);
        Assert.Equal(4, captures.ParticipatingCount());
        Assert.Equal(new RegexMatch(0, 4), captures.GetGroup(1));
        Assert.Equal(new RegexMatch(5, 0), captures.GetGroup(2));
        Assert.Equal(new RegexMatch(6, 1), captures.GetGroup(3));
        Assert.Null(automaton.FindCaptures("0041;BAD;Z"u8));
    }

    /// <summary>
    /// Verifies anchored word-prefix capture patterns use direct capture extraction.
    /// </summary>
    [Fact]
    public void AnchoredWordCaptureEngineReportsPrefixWords()
    {
        var automaton = RegexAutomaton.Compile(
            "^ *(\\w+) +(\\w+) +(\\w+)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] line = System.Text.Encoding.UTF8.GetBytes("  Привет мир test!");

        RegexCaptures? captures = automaton.FindCaptures(line);

        Assert.True(automaton.UsesAnchoredWordCaptureEngine);
        Assert.NotNull(captures);
        Assert.Equal(4, captures.ParticipatingCount());
        Assert.Equal(new RegexMatch(0, System.Text.Encoding.UTF8.GetByteCount("  Привет мир test")), captures.Match);
        AssertGroupUtf8Text(captures, line, 1, "Привет");
        AssertGroupUtf8Text(captures, line, 2, "мир");
        AssertGroupUtf8Text(captures, line, 3, "test");
        Assert.Null(automaton.FindCaptures(line, captures.Match.End));
    }

    /// <summary>
    /// Verifies anchored fixed-run word-boundary captures use direct extraction.
    /// </summary>
    [Fact]
    public void AnchoredRunBoundaryCaptureEngineReportsAsciiCaptures()
    {
        var automaton = RegexAutomaton.Compile(
            @"^(\S{8})(\S)\b"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        RegexCaptures? captures = automaton.FindCaptures("abcdefghi!"u8);

        Assert.True(automaton.UsesAnchoredRunBoundaryCaptureEngine);
        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, 9), captures.Match);
        Assert.Equal(3, captures.ParticipatingCount());
        AssertGroupText(captures, "abcdefghi!"u8.ToArray(), 1, "abcdefgh");
        AssertGroupText(captures, "abcdefghi!"u8.ToArray(), 2, "i");
        Assert.Null(automaton.FindCaptures("abcdefghij"u8));
        Assert.Null(automaton.FindCaptures("abcdefgh!"u8));
    }

    /// <summary>
    /// Verifies anchored fixed-run word-boundary captures count Unicode scalars.
    /// </summary>
    [Fact]
    public void AnchoredRunBoundaryCaptureEngineReportsUnicodeCaptures()
    {
        var automaton = RegexAutomaton.Compile(
            @"^(\S{8})(\S)\b"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] line = System.Text.Encoding.UTF8.GetBytes("абвгдежзи ");
        byte[] noBoundary = System.Text.Encoding.UTF8.GetBytes("абвгдежзий");

        RegexCaptures? captures = automaton.FindCaptures(line);

        Assert.True(automaton.UsesAnchoredRunBoundaryCaptureEngine);
        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, System.Text.Encoding.UTF8.GetByteCount("абвгдежзи")), captures.Match);
        Assert.Equal(3, captures.ParticipatingCount());
        AssertGroupUtf8Text(captures, line, 1, "абвгдежз");
        AssertGroupUtf8Text(captures, line, 2, "и");
        Assert.Null(automaton.FindCaptures(noBoundary));
    }

    /// <summary>
    /// Verifies keyword-boundary whitespace captures use direct extraction.
    /// </summary>
    [Fact]
    public void KeywordWhitespaceCaptureEngineReportsRuffKeywordCaptures()
    {
        var automaton = RegexAutomaton.Compile(
            @"(\s*)\b(?:False|None|True|and|as|assert|async|await|break|class|continue|def|del|elif|else|except|finally|for|from|global|if|import|in|is|lambda|nonlocal|not|or|pass|raise|return|try|while|with|yield)\b(\s*)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] line = System.Text.Encoding.UTF8.GetBytes("xx\u00A0if  value and\tmore");

        RegexCaptures? first = automaton.FindCaptures(line);

        Assert.True(automaton.UsesKeywordWhitespaceCaptureEngine);
        Assert.NotNull(first);
        Assert.Equal(3, first.ParticipatingCount());
        Assert.Equal(new RegexMatch(2, System.Text.Encoding.UTF8.GetByteCount("\u00A0if  ")), first.Match);
        AssertGroupUtf8Text(first, line, 1, "\u00A0");
        AssertGroupUtf8Text(first, line, 2, "  ");

        RegexCaptures? second = automaton.FindCaptures(line, first.Match.End);

        Assert.NotNull(second);
        Assert.Equal(3, second.ParticipatingCount());
        AssertGroupUtf8Text(second, line, 1, " ");
        AssertGroupUtf8Text(second, line, 2, "\t");
        Assert.Equal(6, automaton.CountCaptures(line));
        Assert.Equal(3, automaton.CountCaptures(line, first.Match.End));
        Assert.Equal(3, automaton.CountCaptures("diff if"u8));
        Assert.Equal(0, automaton.CountCaptures(System.Text.Encoding.UTF8.GetBytes("αif")));
        Assert.Equal(0, automaton.CountCaptures(System.Text.Encoding.UTF8.GetBytes("ifα")));
        Assert.Equal(0, automaton.CountCaptures("diff"u8));
        Assert.Null(automaton.FindCaptures(System.Text.Encoding.UTF8.GetBytes("αif")));
        Assert.Null(automaton.FindCaptures(System.Text.Encoding.UTF8.GetBytes("ifα")));
        Assert.Null(automaton.FindCaptures("diff"u8));
    }

    /// <summary>
    /// Verifies operator-spacing whitespace captures use direct extraction.
    /// </summary>
    [Fact]
    public void OperatorSpacingCaptureEngineReportsRuffOperatorCaptures()
    {
        var automaton = RegexAutomaton.Compile(
            @"[^,\s](\s*)(?:[-+*/|!<=>%&^]+|:=)(\s*)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] line = System.Text.Encoding.UTF8.GetBytes("π  +=  b c := d");

        RegexCaptures? first = automaton.FindCaptures(line);

        Assert.True(automaton.UsesOperatorSpacingCaptureEngine);
        Assert.NotNull(first);
        Assert.Equal(3, first.ParticipatingCount());
        Assert.Equal(new RegexMatch(0, System.Text.Encoding.UTF8.GetByteCount("π  +=  ")), first.Match);
        AssertGroupUtf8Text(first, line, 1, "  ");
        AssertGroupUtf8Text(first, line, 2, "  ");
        Assert.Equal(6, automaton.CountCaptures(line));
        Assert.Equal(3, automaton.CountCaptures(line, first.Match.End));
        Assert.Equal(3, automaton.CountCaptures("a+b"u8));
        Assert.Equal(3, automaton.CountCaptures("a +  + b"u8));

        RegexCaptures? second = automaton.FindCaptures(line, first.Match.End);

        Assert.NotNull(second);
        Assert.Equal(3, second.ParticipatingCount());
        AssertGroupUtf8Text(second, line, 1, " ");
        AssertGroupUtf8Text(second, line, 2, " ");
        Assert.Null(automaton.FindCaptures(", + "u8));
        Assert.Equal(0, automaton.CountCaptures(", + "u8));
    }

    /// <summary>
    /// Verifies anchored dot-all captures are synthesized from haystack length.
    /// </summary>
    [Fact]
    public void AnchoredDotStarCaptureEngineReportsDeterministicCaptures()
    {
        var automaton = RegexAutomaton.Compile(
            @"(?s)^((.*)()()($))"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "abc\nxyz"u8.ToArray();

        RegexCaptures? captures = automaton.FindCaptures(haystack);

        Assert.True(automaton.UsesAnchoredDotStarCaptureEngine);
        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, haystack.Length), captures.Match);
        Assert.Equal(6, captures.GroupCount);
        Assert.Equal(6, captures.ParticipatingCount());
        Assert.Equal(new RegexMatch(0, haystack.Length), captures.GetGroup(1));
        Assert.Equal(new RegexMatch(0, haystack.Length), captures.GetGroup(2));
        Assert.Equal(new RegexMatch(haystack.Length, 0), captures.GetGroup(3));
        Assert.Equal(new RegexMatch(haystack.Length, 0), captures.GetGroup(4));
        Assert.Equal(new RegexMatch(haystack.Length, 0), captures.GetGroup(5));
        Assert.Null(automaton.FindCaptures(haystack, 1));
    }

    /// <summary>
    /// Verifies anchored dot-all capture synthesis stays byte-mode only.
    /// </summary>
    [Fact]
    public void AnchoredDotStarCaptureEngineSkipsUtf8Mode()
    {
        var automaton = RegexAutomaton.Compile(
            @"(?s)^((.*)()()($))"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: true,
            unicodeClasses: true);

        Assert.False(automaton.UsesAnchoredDotStarCaptureEngine);
        Assert.NotNull(automaton.FindCaptures("abc"u8));
    }

    /// <summary>
    /// Verifies anchored quoted-string captures use direct extraction.
    /// </summary>
    [Fact]
    public void AnchoredQuotedStringCaptureEngineReportsRawGroup()
    {
        var automaton = RegexAutomaton.Compile(
            "^(?i)[urb]*['\"](?P<raw>.*)['\"]$"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);

        RegexCaptures? first = automaton.FindCaptures("Br\"hello\""u8);
        RegexCaptures? second = automaton.FindCaptures("'raw bytes'"u8);

        Assert.True(automaton.UsesAnchoredQuotedStringCaptureEngine);
        Assert.NotNull(first);
        Assert.Equal(new RegexMatch(0, 9), first.Match);
        Assert.Equal(2, first.ParticipatingCount());
        Assert.Equal(new RegexMatch(3, 5), first.GetGroup(1));
        Assert.NotNull(second);
        Assert.Equal(new RegexMatch(1, 9), second.GetGroup(1));
        Assert.Null(automaton.FindCaptures("x\"nope\""u8));
        Assert.Null(automaton.FindCaptures("b\"unterminated"u8));
        Assert.Null(automaton.FindCaptures("b\"multi\nline\""u8));
        Assert.Null(automaton.FindCaptures("Br\"hello\""u8, startAt: 1));
    }

    /// <summary>
    /// Verifies fixed Unicode scalar captures wrapped in word boundaries use direct extraction.
    /// </summary>
    [Fact]
    public void ScalarRunCaptureEngineReportsBoundaryWrappedUnicodeWords()
    {
        var automaton = RegexAutomaton.Compile(
            @"\b(?:([\w&&\p{Cyrillic}]{6})|([\w&&\p{Cyrillic}]{5}))\b"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("xx привет мир слово test");

        RegexCaptures? first = automaton.FindCaptures(haystack);
        RegexCaptures? second = automaton.FindCaptures(haystack, first!.Match.End);

        Assert.True(automaton.UsesScalarRunCaptureEngine);
        Assert.NotNull(first);
        Assert.Equal(2, first.ParticipatingCount());
        AssertGroupUtf8Text(first, haystack, 1, "привет");
        Assert.Null(first.GetGroup(2));
        Assert.NotNull(second);
        AssertGroupUtf8Text(second, haystack, 2, "слово");
        Assert.Null(second.GetGroup(1));
        Assert.Null(automaton.FindCaptures(System.Text.Encoding.UTF8.GetBytes("приветы")));
        Assert.Null(automaton.FindCaptures(System.Text.Encoding.UTF8.GetBytes("приветx")));
    }

    /// <summary>
    /// Verifies ASCII word-length alternation captures use direct word-run scanning.
    /// </summary>
    [Fact]
    public void AsciiWordLengthAlternationCaptureEngineReportsGroups()
    {
        var automaton = RegexAutomaton.Compile(
            @"\b(?:(\w{6})|(\w{5}))\b"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "abcd abcde abcdef abcdefg 12345 __123"u8.ToArray();

        RegexCaptures? first = automaton.FindCaptures(haystack);
        RegexCaptures? second = automaton.FindCaptures(haystack, first!.Match.End);
        RegexCaptures? third = automaton.FindCaptures(haystack, second!.Match.End);
        RegexCaptures? fourth = automaton.FindCaptures(haystack, third!.Match.End);

        Assert.True(automaton.UsesAsciiWordLengthAlternationCaptureEngine);
        Assert.NotNull(first);
        Assert.Equal(new RegexMatch(5, 5), first.Match);
        Assert.Null(first.GetGroup(1));
        Assert.Equal(new RegexMatch(5, 5), first.GetGroup(2));
        Assert.NotNull(second);
        Assert.Equal(new RegexMatch(11, 6), second.Match);
        Assert.Equal(new RegexMatch(11, 6), second.GetGroup(1));
        Assert.Null(second.GetGroup(2));
        Assert.NotNull(third);
        Assert.Equal(new RegexMatch(26, 5), third.Match);
        Assert.Null(third.GetGroup(1));
        Assert.Equal(new RegexMatch(26, 5), third.GetGroup(2));
        Assert.NotNull(fourth);
        Assert.Equal(new RegexMatch(32, 5), fourth.Match);
        Assert.Null(fourth.GetGroup(1));
        Assert.Equal(new RegexMatch(32, 5), fourth.GetGroup(2));
        Assert.Null(automaton.FindCaptures(haystack, fourth.Match.End));
    }

    /// <summary>
    /// Verifies ASCII word-length alternation captures skip partial word starts.
    /// </summary>
    [Fact]
    public void AsciiWordLengthAlternationCaptureEngineSkipsPartialWords()
    {
        var automaton = RegexAutomaton.Compile(
            @"\b(?:(\w{6})|(\w{5}))\b"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        RegexCaptures? captures = automaton.FindCaptures("abcdef 12345"u8, startAt: 2);

        Assert.True(automaton.UsesAsciiWordLengthAlternationCaptureEngine);
        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(7, 5), captures.Match);
        Assert.Equal(new RegexMatch(7, 5), captures.GetGroup(2));
    }

    /// <summary>
    /// Verifies the rebar unstructured log pattern uses direct structural capture extraction.
    /// </summary>
    [Fact]
    public void StructuredLogCaptureEngineReportsRebarLogCaptures()
    {
        var automaton = RegexAutomaton.Compile(
            RebarUnstructuredLogPattern(),
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] line = "2022/06/17 06:25:22 I4: [17936:140245395805952:(17998)]: (8fb074fc-c766-498b-b224-8b660126b2c0): Searching for query 'dummy query' {/src/master/mastersearchattrs.cc:MasterSearchAttributes():40}"u8.ToArray();

        RegexCaptures? captures = automaton.FindCaptures(line);

        Assert.Equal(RegexEngineKind.StructuredLogCapture, GetEngineKind(automaton));
        Assert.True(automaton.UsesStructuredLogCaptureEngine);
        Assert.Equal(1, automaton.CountMatches(line));
        Assert.Equal(line.Length, automaton.SumMatchSpans(line));
        Assert.Equal(6, automaton.CountCaptures(line));
        Assert.Equal(0, automaton.CountMatches(line, startAt: 1));
        Assert.Equal(0, automaton.CountCaptures(line, startAt: 1));
        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, line.Length), captures.Match);
        Assert.Equal(6, captures.GroupCount);
        Assert.Equal(6, captures.ParticipatingCount());
        AssertGroupText(captures, line, 1, "2022/06/17 06:25:22");
        AssertGroupText(captures, line, 2, "I");
        AssertGroupText(captures, line, 3, "[17936:140245395805952:(17998)]: (8fb074fc-c766-498b-b224-8b660126b2c0): ");
        AssertGroupText(captures, line, 4, "Searching for query 'dummy query'");
        AssertGroupText(captures, line, 5, "/src/master/mastersearchattrs.cc:MasterSearchAttributes():40");
    }

    /// <summary>
    /// Verifies the structured log capture path keeps earlier braces in the message body.
    /// </summary>
    [Fact]
    public void StructuredLogCaptureEngineUsesFinalLocationBlock()
    {
        var automaton = RegexAutomaton.Compile(
            RebarUnstructuredLogPattern(),
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] line = "2022/06/17 06:25:22 I4: msg {bad} tail {/loc}"u8.ToArray();

        RegexCaptures? captures = automaton.FindCaptures(line);

        Assert.True(automaton.UsesStructuredLogCaptureEngine);
        Assert.NotNull(captures);
        Assert.Equal(6, captures.ParticipatingCount());
        AssertGroupText(captures, line, 1, "2022/06/17 06:25:22");
        AssertGroupText(captures, line, 2, "I");
        AssertGroupText(captures, line, 3, "");
        AssertGroupText(captures, line, 4, "msg {bad} tail");
        AssertGroupText(captures, line, 5, "/loc");
    }

    /// <summary>
    /// Verifies the structured log capture path preserves dot newline semantics in the body capture.
    /// </summary>
    [Fact]
    public void StructuredLogCaptureEngineRejectsBodyLineTerminator()
    {
        var automaton = RegexAutomaton.Compile(
            RebarUnstructuredLogPattern(),
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] line = "2022/06/17 06:25:22 I4: first\nsecond {/loc}"u8.ToArray();

        Assert.True(automaton.UsesStructuredLogCaptureEngine);
        Assert.Null(automaton.FindCaptures(line));
        Assert.Equal(0, automaton.CountMatches(line));
        Assert.Equal(0, automaton.CountCaptures(line));
    }

    /// <summary>
    /// Verifies anchored tab-delimited log captures use direct structural extraction.
    /// </summary>
    [Fact]
    public void TabbedLogCaptureEngineReportsCaddyLogCaptures()
    {
        var automaton = RegexAutomaton.Compile(
            @"^([0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z)\t(\w+)\t(\w+)\t([^\t]+)(?:\t(.+))?$"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] line = "2022-07-27T00:18:48Z\tinfo\ttls\tcleaning storage unit\t{\"description\": \"FileStorage:/root/.local/share/caddy\"}"u8.ToArray();
        byte[] noDetails = "2022-07-27T00:18:48Z\tinfo\ttls\tcleaning storage unit"u8.ToArray();

        RegexCaptures? captures = automaton.FindCaptures(line);
        RegexCaptures? noDetailsCaptures = automaton.FindCaptures(noDetails);

        Assert.True(automaton.UsesTabbedLogCaptureEngine);
        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, line.Length), captures.Match);
        Assert.Equal(6, captures.GroupCount);
        Assert.Equal(6, captures.ParticipatingCount());
        Assert.Equal(6, automaton.CountCaptures(line));
        AssertGroupText(captures, line, 1, "2022-07-27T00:18:48Z");
        AssertGroupText(captures, line, 2, "info");
        AssertGroupText(captures, line, 3, "tls");
        AssertGroupText(captures, line, 4, "cleaning storage unit");
        AssertGroupText(captures, line, 5, "{\"description\": \"FileStorage:/root/.local/share/caddy\"}");

        Assert.NotNull(noDetailsCaptures);
        Assert.Equal(5, noDetailsCaptures.ParticipatingCount());
        Assert.Equal(5, automaton.CountCaptures(noDetails));
        Assert.Null(noDetailsCaptures.GetGroup(5));
        Assert.Null(automaton.FindCaptures("2022-07-27T00:18:48Z\tinfo\ttls\t"u8));
        Assert.Equal(0, automaton.CountCaptures("2022-07-27T00:18:48Z\tinfo\ttls\tmessage\t"u8));

        var differentNegatedClass = RegexAutomaton.Compile(
            @"^([0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z)\t(\w+)\t(\w+)\t([^\ta]+)(?:\t(.+))?$"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        Assert.False(differentNegatedClass.UsesTabbedLogCaptureEngine);
    }

    /// <summary>
    /// Verifies anchored function predicate signatures synthesize captures without a second generic capture pass.
    /// </summary>
    [Fact]
    public void FnPredicateCaptureEngineReportsSignatureCaptures()
    {
        var automaton = RegexAutomaton.Compile(
            @"^\s*fn\s+(is_([^\(]+))\(([^)]+)\) -> bool \{$"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] line = "    fn is_ascii_word(byte: u8) -> bool {"u8.ToArray();

        RegexCaptures? captures = automaton.FindCaptures(line);

        Assert.True(automaton.UsesFnPredicateCaptureEngine);
        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, line.Length), captures.Match);
        AssertGroupText(captures, line, 1, "is_ascii_word");
        AssertGroupText(captures, line, 2, "ascii_word");
        AssertGroupText(captures, line, 3, "byte: u8");
        Assert.Null(automaton.FindCaptures("fn is_empty() -> bool {"u8));
        Assert.Null(automaton.FindCaptures("fn is_ascii_word(byte: u8) -> bool { trailing"u8));
        Assert.Null(automaton.FindCaptures(line, 1));
    }

    /// <summary>
    /// Verifies anchored line-prefix captures use direct extraction for shebang-like patterns.
    /// </summary>
    [Fact]
    public void LinePrefixCaptureEngineReportsShebangCaptures()
    {
        var automaton = RegexAutomaton.Compile(
            @"^(\s*)#!(.*)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] line = System.Text.Encoding.UTF8.GetBytes("\u00A0\t#!/usr/bin/env python");

        RegexCaptures? captures = automaton.FindCaptures(line);

        Assert.True(automaton.UsesLinePrefixCaptureEngine);
        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, line.Length), captures.Match);
        Assert.Equal(3, captures.ParticipatingCount());
        Assert.Equal(3, automaton.CountCaptures(line));
        AssertGroupUtf8Text(captures, line, 1, "\u00A0\t");
        AssertGroupUtf8Text(captures, line, 2, "/usr/bin/env python");
        Assert.Equal(0, automaton.CountCaptures("  print()"u8));
        Assert.Null(automaton.FindCaptures(line, 1));
    }

    /// <summary>
    /// Verifies the line-prefix capture path preserves dot newline and empty-capture semantics.
    /// </summary>
    [Fact]
    public void LinePrefixCaptureEngineStopsDotAtLineTerminator()
    {
        var automaton = RegexAutomaton.Compile(
            @"^(\s*)#!(.*)"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = "#!first\nsecond"u8.ToArray();

        RegexCaptures? captures = automaton.FindCaptures(haystack);

        Assert.True(automaton.UsesLinePrefixCaptureEngine);
        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, "#!first"u8.Length), captures.Match);
        AssertGroupText(captures, haystack, 1, "");
        AssertGroupText(captures, haystack, 2, "first");
        Assert.Equal(3, automaton.CountCaptures("#!"u8));
    }

    /// <summary>
    /// Verifies nullable leading expressions do not use a literal prefilter.
    /// </summary>
    [Fact]
    public void SkipsMemmemPrefilterForNullableLeadingExpressions()
    {
        var automaton = RegexAutomaton.Compile("a*"u8);

        Assert.Equal(RegexPrefilterKind.None, automaton.PrefilterKind);
        Assert.Equal(new RegexMatch(0, 0), automaton.Find("bbb"u8));
    }

    /// <summary>
    /// Verifies Unicode-aware case-insensitive literals use literal-set execution.
    /// </summary>
    [Fact]
    public void UsesLiteralSetForUnicodeCaseInsensitiveLiteral()
    {
        byte[] pattern = System.Text.Encoding.UTF8.GetBytes("Шерлок Холмс");
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("xxшерлок холмс yy");
        var automaton = RegexAutomaton.Compile(
            pattern,
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false,
            unicodeClasses: true);

        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(automaton));
        Assert.Equal(RegexPrefilterKind.None, automaton.PrefilterKind);
        Assert.Equal(new RegexMatch(2, pattern.Length), automaton.Find(haystack));
    }

    /// <summary>
    /// Verifies required-literal prefilters keep the same selectivity floor as single literal prefixes.
    /// </summary>
    [Fact]
    public void RequiredLiteralSetRejectsShortLiterals()
    {
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false);

        Assert.False(RegexPrefilter.TryPrepareRequiredLiteralSet([[(byte)'\n']], options, out _));
        Assert.True(RegexPrefilter.TryPrepareRequiredLiteralSet(["$2"u8.ToArray()], options, out _));
        Assert.True(RegexPrefilter.TryPrepareRequiredLiteralSet(["abc"u8.ToArray()], options, out byte[][] prepared));
        byte[] only = Assert.Single(prepared);
        Assert.Equal("abc"u8.ToArray(), only);
    }

    /// <summary>
    /// Verifies case-sensitive required-literal prefilters keep exact literal bytes.
    /// </summary>
    [Fact]
    public void RequiredLiteralSetPreservesCaseSensitiveLiterals()
    {
        var caseSensitive = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false);
        var caseInsensitive = new RegexCompileOptions(
            caseInsensitive: true,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.True(RegexPrefilter.TryPrepareRequiredLiteralSet(["AbC"u8.ToArray()], caseSensitive, out byte[][] exact));
        Assert.Equal("AbC"u8.ToArray(), Assert.Single(exact));

        Assert.True(RegexPrefilter.TryPrepareRequiredLiteralSet(["AbC"u8.ToArray()], caseInsensitive, out byte[][] folded));
        Assert.Equal("abc"u8.ToArray(), Assert.Single(folded));
    }

    /// <summary>
    /// Verifies the single required-literal finder preserves ASCII case-insensitive matching.
    /// </summary>
    [Fact]
    public void SingleRequiredLiteralFinderMatchesAsciiCaseInsensitive()
    {
        var finder = new RegexAsciiCaseInsensitiveFinder("# noqa"u8);

        Assert.Equal(3, finder.Find("xx # NOQA"u8));
        Assert.Equal(3, finder.Find("xx # noqa"u8));
        Assert.Equal(-1, finder.Find("xx # node"u8));
    }

    /// <summary>
    /// Verifies the single required-literal finder keeps leftmost ASCII case-insensitive block matches.
    /// </summary>
    [Fact]
    public void SingleRequiredLiteralFinderMatchesAsciiCaseInsensitiveBlockAnchor()
    {
        var finder = new RegexAsciiCaseInsensitiveFinder("Twain"u8);

        Assert.Equal(3, finder.Find("xx tWain yy"u8));
        Assert.Equal(0, finder.Find("twAIN Twain"u8));
        Assert.Equal(8, finder.Find("xx wain tWaIn"u8));
        Assert.Equal(-1, finder.Find("xx tWai"u8));
        Assert.Equal(-1, finder.Find("xx wain"u8));
    }

    /// <summary>
    /// Verifies required-literal sets retain a proven maximum distance from the match start.
    /// </summary>
    [Fact]
    public void RequiredLiteralSetComputesBoundedLookBehind()
    {
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(".{0,2}(?:secret|token)[0-9]"u8);

        Assert.True(RegexPrefilter.TryCollectRequiredLiteralSetWithLookBehind(
            tree.Root,
            options,
            out byte[][] literals,
            out int maxLookBehind));

        Assert.Equal(2, maxLookBehind);
        Assert.Contains(literals, literal => literal.AsSpan().SequenceEqual("secret"u8));
        Assert.Contains(literals, literal => literal.AsSpan().SequenceEqual("token"u8));
    }

    /// <summary>
    /// Verifies Unicode case-fold expansion is included in required-literal lookbehind bounds.
    /// </summary>
    [Fact]
    public void RequiredLiteralSetComputesUnicodeCaseFoldedLookBehind()
    {
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("(?i)K.{0,2}secret[0-9]"u8);

        Assert.True(RegexPrefilter.TryCollectRequiredLiteralSetWithLookBehind(
            tree.Root,
            options,
            out byte[][] literals,
            out int maxLookBehind));

        Assert.Equal(11, maxLookBehind);
        Assert.Contains(literals, literal => literal.AsSpan().SequenceEqual("secret"u8));
    }

    /// <summary>
    /// Verifies compiled required-literal prefilters use the proven maximum lookbehind.
    /// </summary>
    [Fact]
    public void RequiredLiteralPrefilterUsesBoundedLookBehind()
    {
        var automaton = RegexAutomaton.Compile(
            ".{0,2}(?:secret|token)[0-9]"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(RegexPrefilterKind.RequiredLiteral, automaton.PrefilterKind);
        Assert.Equal(2, automaton.RequiredLiteralWindow);
        Assert.Equal(new RegexMatch(2, 9), automaton.Find("zzxysecret7"u8));
    }

    /// <summary>
    /// Verifies compiled case-sensitive required-literal prefilters do not fold candidates.
    /// </summary>
    [Fact]
    public void RequiredLiteralPrefilterPreservesCaseSensitiveMatches()
    {
        var automaton = RegexAutomaton.Compile(
            ".{0,2}ABC[0-9]"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(RegexPrefilterKind.RequiredLiteral, automaton.PrefilterKind);
        Assert.Equal(new RegexMatch(2, 6), automaton.Find("zzxyABC7"u8));
        Assert.Null(automaton.Find("zzxyabc7"u8));
    }

    /// <summary>
    /// Verifies the meta engine selects the dense DFA for small position-independent NFAs.
    /// </summary>
    [Fact]
    public void MetaEngineSelectsDenseDfaForSmallPredicateFreePatterns()
    {
        RegexNfa nfa = CompileNfa("needle"u8);

        var engine = RegexMetaEngine.Compile(nfa);

        Assert.Equal(RegexEngineKind.DenseDfa, engine.Kind);
        Assert.Equal(new RegexMatch(2, 6), engine.Find("xxneedle yy"u8, startAt: 0));
    }

    /// <summary>
    /// Verifies the DFA size limit can force fallback to the PikeVM.
    /// </summary>
    [Fact]
    public void MetaEngineFallsBackToPikeVmWhenDfaSizeLimitCannotFitStartState()
    {
        RegexNfa nfa = CompileNfa("needle"u8);

        var engine = RegexMetaEngine.Compile(nfa, prefilter: null, dfaSizeLimit: 0);

        Assert.Equal(RegexEngineKind.PikeVm, engine.Kind);
        Assert.Equal(new RegexMatch(2, 6), engine.Find("xxneedle yy"u8, startAt: 0));
    }

    /// <summary>
    /// Verifies the meta engine selects the sparse DFA for medium position-independent NFAs.
    /// </summary>
    [Fact]
    public void MetaEngineSelectsSparseDfaForMediumPredicateFreePatterns()
    {
        RegexNfa nfa = CompileNfa("abcdefghijk"u8);

        var engine = RegexMetaEngine.Compile(nfa);

        Assert.Equal(RegexEngineKind.SparseDfa, engine.Kind);
        Assert.Equal(new RegexMatch(2, 11), engine.Find("xxabcdefghijk yy"u8, startAt: 0));
    }

    /// <summary>
    /// Verifies the meta engine keeps the lazy DFA for large position-independent NFAs.
    /// </summary>
    [Fact]
    public void MetaEngineSelectsLazyDfaForLargePredicateFreePatterns()
    {
        RegexNfa nfa = CompileNfa("abcdefghijklmnopqrstuvwxyz0123456789"u8);

        var engine = RegexMetaEngine.Compile(nfa);

        Assert.Equal(RegexEngineKind.LazyDfa, engine.Kind);
        Assert.Equal(new RegexMatch(2, 36), engine.Find("xxabcdefghijklmnopqrstuvwxyz0123456789"u8, startAt: 0));
    }

    /// <summary>
    /// Verifies the meta engine selects the bounded backtracker for small position-sensitive predicates.
    /// </summary>
    [Fact]
    public void MetaEngineSelectsBoundedBacktrackerForSmallPositionSensitivePredicates()
    {
        RegexNfa nfa = CompileNfa("(?m)^abc$"u8);

        var engine = RegexMetaEngine.Compile(nfa);

        Assert.Equal(RegexEngineKind.BoundedBacktracker, engine.Kind);
        Assert.Equal(new RegexMatch(4, 3), engine.Find("zzz\nabc\n"u8, startAt: 0));
    }

    /// <summary>
    /// Verifies the meta engine selects the one-pass DFA for deterministic position-sensitive loops.
    /// </summary>
    [Fact]
    public void MetaEngineSelectsOnePassDfaForDeterministicPositionSensitiveLoops()
    {
        RegexNfa nfa = CompileNfa("(?m)^a+$"u8);

        var engine = RegexMetaEngine.Compile(nfa);

        Assert.Equal(RegexEngineKind.OnePassDfa, engine.Kind);
        Assert.Equal(new RegexMatch(3, 3), engine.Find("xx\naaa\n"u8, startAt: 0));
    }

    /// <summary>
    /// Verifies the meta engine keeps larger position-sensitive predicates on the PikeVM path.
    /// </summary>
    [Fact]
    public void MetaEngineSelectsPikeVmForLargePositionSensitivePredicates()
    {
        RegexNfa nfa = CompileNfa("(?m)^abcdefghijklmnopqrstuvwxyz0123456789$"u8);

        var engine = RegexMetaEngine.Compile(nfa);

        Assert.Equal(RegexEngineKind.PikeVm, engine.Kind);
        Assert.Equal(
            new RegexMatch(3, 36),
            engine.Find("xx\nabcdefghijklmnopqrstuvwxyz0123456789\n"u8, startAt: 0));
    }

    /// <summary>
    /// Verifies literal matching returns the leftmost start and byte length.
    /// </summary>
    [Fact]
    public void FindsLiteralMatch()
    {
        var automaton = RegexAutomaton.Compile("needle"u8);

        RegexMatch? match = automaton.Find("xxneedle yy"u8);

        Assert.True(match.HasValue);
        Assert.Equal(new RegexMatch(2, 6), match.Value);
    }

    /// <summary>
    /// Verifies alternation preserves leftmost-first branch priority.
    /// </summary>
    [Fact]
    public void PreservesAlternationPriority()
    {
        RegexMatch? longFirst = RegexAutomaton.Compile("ab|a"u8).Find("zab"u8);
        RegexMatch? shortFirst = RegexAutomaton.Compile("a|ab"u8).Find("zab"u8);
        RegexMatch? deadEarlierBranch = RegexAutomaton.Compile("torsdag|tor|tors"u8).Find("tors"u8);

        Assert.True(longFirst.HasValue);
        Assert.True(shortFirst.HasValue);
        Assert.True(deadEarlierBranch.HasValue);
        Assert.Equal(new RegexMatch(1, 2), longFirst.Value);
        Assert.Equal(new RegexMatch(1, 1), shortFirst.Value);
        Assert.Equal(new RegexMatch(0, 3), deadEarlierBranch.Value);
    }

    /// <summary>
    /// Verifies repetition honors greedy, lazy, and ungreedy flag priority.
    /// </summary>
    [Fact]
    public void HonorsRepetitionPriority()
    {
        RegexMatch? greedy = RegexAutomaton.Compile("a+"u8).Find("zaaa"u8);
        RegexMatch? lazy = RegexAutomaton.Compile("a+?"u8).Find("zaaa"u8);
        RegexMatch? ungreedy = RegexAutomaton.Compile("(?U)a+"u8).Find("zaaa"u8);
        RegexMatch? emptyFirstStar = RegexAutomaton.Compile("(?:|a)*"u8).Find("aaa"u8);
        RegexMatch? consumingFirstStar = RegexAutomaton.Compile("(?:a|)*"u8).Find("aaa"u8);

        Assert.True(greedy.HasValue);
        Assert.True(lazy.HasValue);
        Assert.True(ungreedy.HasValue);
        Assert.Equal(new RegexMatch(1, 3), greedy.Value);
        Assert.Equal(new RegexMatch(1, 1), lazy.Value);
        Assert.Equal(new RegexMatch(1, 1), ungreedy.Value);
        Assert.Equal(new RegexMatch(0, 0), emptyFirstStar);
        Assert.Equal(new RegexMatch(0, 3), consumingFirstStar);
    }

    /// <summary>
    /// Verifies earliest search returns the first accepting span instead of leftmost-first greedy priority.
    /// </summary>
    [Fact]
    public void HonorsEarliestSearchKind()
    {
        RegexMatch? greedy = RegexAutomaton.Compile("a+"u8).FindEarliest("zaaa"u8, startAt: 0);
        RegexMatch? alternate = RegexAutomaton.Compile("abc|a"u8).FindEarliest("abc"u8, startAt: 0);
        RegexMatch? endAnchored = RegexAutomaton.Compile("(abc|a)$"u8).FindEarliest("abc"u8, startAt: 0);

        Assert.Equal(new RegexMatch(1, 1), greedy);
        Assert.Equal(new RegexMatch(0, 1), alternate);
        Assert.Equal(new RegexMatch(0, 3), endAnchored);
    }

    /// <summary>
    /// Verifies all-match-kind search keeps the longest accepting span from a fixed start.
    /// </summary>
    [Fact]
    public void HonorsAllMatchKindAtStart()
    {
        RegexMatch? alternate = RegexAutomaton.Compile("foo|foobar"u8).FindAllKindAt("foobar"u8, startAt: 0);
        RegexMatch? nongreedy = RegexAutomaton.Compile("(abc)+?"u8).FindAllKindAt("abcabcabc"u8, startAt: 0);
        RegexMatch? dot = RegexAutomaton.Compile("(?s:.)"u8).FindAllKindAt("foobar"u8, startAt: 5);

        Assert.Equal(new RegexMatch(0, 6), alternate);
        Assert.Equal(new RegexMatch(0, 9), nongreedy);
        Assert.Equal(new RegexMatch(5, 1), dot);
    }

    /// <summary>
    /// Verifies overlapping search reports every accepting span from a fixed start.
    /// </summary>
    [Fact]
    public void FindsOverlappingMatchesAtStart()
    {
        IReadOnlyList<RegexMatch> matches = RegexAutomaton.Compile("a+"u8).FindOverlappingAt("aaa"u8, startAt: 0);
        IReadOnlyList<RegexMatch> emptyMatches = RegexAutomaton.Compile("a*"u8).FindOverlappingAt("aaa"u8, startAt: 2);
        IReadOnlyList<RegexMatch> literalMatches = RegexAutomaton.Compile("a|aa|aaa"u8).FindOverlappingAt("aaa"u8, startAt: 0);

        Assert.Equal([new RegexMatch(0, 1), new RegexMatch(0, 2), new RegexMatch(0, 3)], matches);
        Assert.Equal([new RegexMatch(2, 0), new RegexMatch(2, 1)], emptyMatches);
        Assert.Equal([new RegexMatch(0, 1), new RegexMatch(0, 2), new RegexMatch(0, 3)], literalMatches);
    }

    /// <summary>
    /// Verifies adjacent optional groups retain ripgrep's leftmost-first greedy priority.
    /// </summary>
    [Fact]
    public void HonorsAdjacentOptionalGroupPriority()
    {
        var automaton = RegexAutomaton.Compile(@"(^|[^a-z])((([a-z]+)?)\s)?b(\s([a-z]+)?)($|[^a-z])"u8, multiLine: true, dotMatchesNewline: false);
        ReadOnlySpan<byte> haystack = " b b b b b b b b\nc\n"u8;

        RegexMatch? first = automaton.Find(haystack);
        Assert.True(first.HasValue);
        Assert.Equal(0, first.Value.Start);
        Assert.Equal(5, first.Value.Length);

        RegexMatch? second = automaton.Find(haystack, MatchIterator.AdvanceAfter(new MatcherMatch(first!.Value.Start, first.Value.Length), haystack.Length));
        Assert.True(second.HasValue);
        Assert.Equal(6, second.Value.Start);
        Assert.Equal(7, second.Value.Length);

        RegexMatch? third = automaton.Find(haystack, MatchIterator.AdvanceAfter(new MatcherMatch(second!.Value.Start, second.Value.Length), haystack.Length));
        Assert.True(third.HasValue);
        Assert.Equal(14, third.Value.Start);
        Assert.Equal(4, third.Value.Length);

        RegexMatch? fourth = automaton.Find(haystack, MatchIterator.AdvanceAfter(new MatcherMatch(third!.Value.Start, third.Value.Length), haystack.Length));
        Assert.Null(fourth);
    }

    /// <summary>
    /// Verifies repeated zero-width alternatives keep priority over later consuming branches.
    /// </summary>
    [Fact]
    public void RepetitionPreservesZeroWidthAlternativePriority()
    {
        var automaton = RegexAutomaton.Compile(@"(?m)(?:^|a)+"u8);

        RegexMatch? match = automaton.Find("a\naaa\n"u8);

        Assert.Equal(new RegexMatch(0, 0), match);
    }

    /// <summary>
    /// Verifies greedy repetition continues through multiline predicates before accepting.
    /// </summary>
    [Fact]
    public void GreedyMultilineRepetitionSpansLines()
    {
        var automaton = RegexAutomaton.Compile(@"(?m)(?:^\d+$\n?)+"u8);

        RegexMatch? match = automaton.Find("123\n456\n789"u8);

        Assert.Equal(new RegexMatch(0, 11), match);
    }

    /// <summary>
    /// Verifies scoped and unscoped case-insensitive flags affect subsequent atoms.
    /// </summary>
    [Fact]
    public void AppliesInlineCaseFlags()
    {
        var global = RegexAutomaton.Compile("(?i)abc"u8);
        var scoped = RegexAutomaton.Compile("(?i:abc)def"u8);
        var disabled = RegexAutomaton.Compile("(?i:abc)(?-i:def)"u8);

        Assert.Equal(new RegexMatch(0, 3), global.Find("ABC"u8));
        Assert.Equal(new RegexMatch(0, 6), scoped.Find("ABCdef"u8));
        Assert.Null(scoped.Find("ABCDEF"u8));
        Assert.Equal(new RegexMatch(0, 6), disabled.Find("ABCdef"u8));
        Assert.Null(disabled.Find("ABCDEF"u8));
    }

    /// <summary>
    /// Verifies dot-all and multiline flags affect runtime predicates.
    /// </summary>
    [Fact]
    public void AppliesDotAllAndMultilineFlags()
    {
        Assert.Null(RegexAutomaton.Compile("a.b"u8).Find("a\nb"u8));
        Assert.Equal(new RegexMatch(0, 3), RegexAutomaton.Compile("(?s)a.b"u8).Find("a\nb"u8));

        Assert.Null(RegexAutomaton.Compile("^foo$"u8).Find("bar\nfoo\nbaz"u8));
        Assert.Equal(new RegexMatch(4, 3), RegexAutomaton.Compile("(?m)^foo$"u8).Find("bar\nfoo\nbaz"u8));
    }

    /// <summary>
    /// Verifies Perl and bracket classes include line terminators according to class membership.
    /// </summary>
    [Fact]
    public void ClassesCanMatchLineTerminatorsWithoutMultiline()
    {
        Assert.Equal(new RegexMatch(0, 1), RegexAutomaton.Compile(@"\s"u8).Find("\n"u8));
        Assert.Equal(new RegexMatch(0, 1), RegexAutomaton.Compile(@"[\s]"u8).Find("\n"u8));
        Assert.Equal(new RegexMatch(0, 1), RegexAutomaton.Compile(@"\D"u8).Find("\n"u8));
        Assert.Equal(new RegexMatch(0, 1), RegexAutomaton.Compile(@"\W"u8).Find("\n"u8));
        Assert.Equal(new RegexMatch(0, 1), RegexAutomaton.Compile(@"[^a]"u8).Find("\n"u8));
        Assert.Null(RegexAutomaton.Compile(@"\S"u8).Find("\n"u8));
    }

    /// <summary>
    /// Verifies escaped newlines inside bracket classes are parsed as line feeds, not as the letter n.
    /// </summary>
    [Fact]
    public void CharacterClassesParseEscapedNewline()
    {
        Assert.Equal(new RegexMatch(3, 1), RegexAutomaton.Compile(@"[\n]"u8).Find("abc\nxyz"u8));
        Assert.Equal(new RegexMatch(0, 3), RegexAutomaton.Compile(@"[^\n]+"u8).Find("abc\nxyz"u8));
    }

    /// <summary>
    /// Verifies bracket class intersection uses both operands instead of treating ampersands as literals.
    /// </summary>
    [Fact]
    public void CharacterClassesSupportIntersection()
    {
        var ascii = RegexAutomaton.Compile(@"[ab&&bc]+"u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, unicodeClasses: false);
        Assert.Equal(new RegexMatch(1, 1), ascii.Find("abc"u8));
        Assert.Null(RegexAutomaton.Compile(@"[a&&b]"u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, unicodeClasses: false).Find("ab"u8));

        var cyrillicWord = RegexAutomaton.Compile(@"[\w&&\p{Cyrillic}]+"u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, unicodeClasses: true);
        Assert.Equal(new RegexMatch(4, 6), cyrillicWord.Find("abc фоо 123 _ &&"u8));
        Assert.Null(cyrillicWord.Find("abc123_&&"u8));
    }

    /// <summary>
    /// Verifies UTF-8 character classes support high scalar escapes and ranges.
    /// </summary>
    [Fact]
    public void CharacterClassesSupportHighScalarEscapes()
    {
        var scalarRange = RegexAutomaton.Compile(@"[\u{10000}-\u{10FFFF}]"u8);
        var mathIntersection = RegexAutomaton.Compile(@"[\p{math}&&\u{10000}-\u{10FFFF}]"u8);
        byte[] mathBeta = System.Text.Encoding.UTF8.GetBytes("𝛃");

        Assert.Equal(new RegexMatch(0, 4), scalarRange.Find(mathBeta));
        Assert.Equal(new RegexMatch(0, 4), mathIntersection.Find(mathBeta));
        Assert.Null(mathIntersection.Find("+"u8));
    }

    /// <summary>
    /// Verifies bracket class hexadecimal escapes decode to byte literals before range matching.
    /// </summary>
    [Fact]
    public void CharacterClassesDecodeHexByteEscapes()
    {
        var automaton = RegexAutomaton.Compile(@"(?-u:[\x00-\x29]+)"u8);

        Assert.Equal(new RegexMatch(1, 2), automaton.Find([(byte)'x', 0x00, 0x29, (byte)'*']));
        Assert.Null(automaton.Find("x029"u8));
    }

    /// <summary>
    /// Verifies CRLF mode treats carriage returns and line feeds as one line terminator family.
    /// </summary>
    [Fact]
    public void AppliesCrlfMode()
    {
        Assert.Equal(new RegexMatch(0, 1), RegexAutomaton.Compile("."u8).Find("\r"u8));
        Assert.Null(RegexAutomaton.Compile("(?R)."u8).Find("\r\n"u8));
        Assert.Equal(new RegexMatch(0, 1), RegexAutomaton.Compile("(?Rs)."u8).Find("\r"u8));

        Assert.Null(RegexAutomaton.Compile("(?m)^foo$"u8).Find("\r\nfoo\r\n"u8));
        Assert.Equal(new RegexMatch(2, 3), RegexAutomaton.Compile("(?Rm)^foo$"u8).Find("\r\nfoo\r\n"u8));
        Assert.Equal(new RegexMatch(2, 0), RegexAutomaton.Compile("(?Rm)^"u8).Find("\r\nx"u8, startAt: 1));
        Assert.Equal(new RegexMatch(2, 0), RegexAutomaton.Compile("(?Rm)$"u8).Find("\r\n"u8, startAt: 1));
    }

    /// <summary>
    /// Verifies custom line terminators affect dot and multiline anchors.
    /// </summary>
    [Fact]
    public void AppliesCustomLineTerminator()
    {
        var anchored = RegexAutomaton.Compile("(?m)^[a-z]+$"u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, crlf: false, lineTerminator: 0);
        var dot = RegexAutomaton.Compile("."u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, crlf: false, lineTerminator: 0);

        Assert.Equal(new RegexMatch(1, 3), anchored.Find("\0abc\0"u8));
        Assert.Equal(new RegexMatch(1, 1), dot.Find("\0\n"u8));
    }

    /// <summary>
    /// Verifies verbose mode ignores unescaped whitespace inside bracket classes.
    /// </summary>
    [Fact]
    public void ExtendedModeIgnoresWhitespaceInsideCharacterClasses()
    {
        Assert.Equal(new RegexMatch(0, 1), RegexAutomaton.Compile(@"(?x)[ a ]"u8).Find("a"u8));
        Assert.Null(RegexAutomaton.Compile(@"(?x)[ a ]"u8).Find(" "u8));
        Assert.Equal(new RegexMatch(0, 1), RegexAutomaton.Compile(@"(?x)[\ ]"u8).Find(" "u8));
    }

    /// <summary>
    /// Verifies the regex-crate any-byte Unicode class matches across line terminators.
    /// </summary>
    [Fact]
    public void MatchesAnyUnicodeClass()
    {
        Assert.Equal(new RegexMatch(0, 3), RegexAutomaton.Compile(@"a\p{Any}b"u8).Find("a\nb"u8));
    }

    /// <summary>
    /// Verifies Unicode general category classes use pinned regex-syntax tables.
    /// </summary>
    [Fact]
    public void UnicodePropertyClassesUsePinnedGeneralCategoryTables()
    {
        Assert.Equal(new RegexMatch(0, 8), RegexAutomaton.Compile(@"\p{Lu}+"u8).Find("ΛΘΓΔα"u8));
        Assert.Equal(new RegexMatch(0, 10), RegexAutomaton.Compile(@"\p{Lu}+"u8, caseInsensitive: true, multiLine: false, dotMatchesNewline: false).Find("ΛΘΓΔα"u8));
        Assert.Equal(new RegexMatch(0, 10), RegexAutomaton.Compile(@"\pL+"u8).Find("ΛΘΓΔα"u8));
        Assert.Equal(new RegexMatch(8, 2), RegexAutomaton.Compile(@"\p{Ll}+"u8).Find("ΛΘΓΔα"u8));
        Assert.Equal(new RegexMatch(0, 2), RegexAutomaton.Compile(@"\p{gc=Letter}"u8).Find("δ"u8));
        Assert.Equal(new RegexMatch(0, 2), RegexAutomaton.Compile(@"\p{gc:Letter}"u8).Find("δ"u8));
        Assert.Equal(new RegexMatch(0, 2), RegexAutomaton.Compile(@"\p{General_Category=Letter}"u8).Find("δ"u8));
        Assert.Equal(new RegexMatch(0, 2), RegexAutomaton.Compile(@"\p{Lowercase}"u8).Find("δ"u8));
        Assert.Equal(new RegexMatch(0, 2), RegexAutomaton.Compile(@"\p{Uppercase}"u8).Find("Λ"u8));
        Assert.Equal(new RegexMatch(0, 2), RegexAutomaton.Compile(@"\P{N}+"u8).Find("abⅠ"u8));
        Assert.Null(RegexAutomaton.Compile(@"\p{Lu}"u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, unicodeClasses: false).Find("Λ"u8));
    }

    /// <summary>
    /// Verifies Unicode script classes use pinned regex-syntax tables.
    /// </summary>
    [Fact]
    public void UnicodeScriptPropertyClassesUsePinnedTables()
    {
        Assert.Equal(new RegexMatch(4, 6), RegexAutomaton.Compile(@"\p{Cyrillic}+"u8).Find("abc фоо"u8));
        Assert.Equal(new RegexMatch(4, 6), RegexAutomaton.Compile(@"\p{sc=Cyrillic}+"u8).Find("abc фоо"u8));
        Assert.Equal(new RegexMatch(4, 6), RegexAutomaton.Compile(@"\p{Script:Cyrillic}+"u8).Find("abc фоо"u8));
        Assert.Equal(new RegexMatch(4, 6), RegexAutomaton.Compile(@"\p{Greek}+"u8).Find("abc δει"u8));
        Assert.Equal(new RegexMatch(4, 6), RegexAutomaton.Compile(@"\p{sc=Greek}+"u8).Find("abc δει"u8));
        Assert.Equal(new RegexMatch(4, 6), RegexAutomaton.Compile(@"\p{Script:Grek}+"u8).Find("abc δει"u8));
        Assert.Null(RegexAutomaton.Compile(@"\p{Cyrillic}"u8).Find("\u0301"u8));
        Assert.Equal(new RegexMatch(0, 2), RegexAutomaton.Compile(@"\p{scx=Cyrillic}"u8).Find("\u0301"u8));
    }

    /// <summary>
    /// Verifies bracket classes can contain regex-syntax Unicode property tokens.
    /// </summary>
    [Fact]
    public void UnicodePropertyTokensInBracketClassesUsePinnedTables()
    {
        Assert.Equal(new RegexMatch(0, 2), RegexAutomaton.Compile(@"[\PN]+"u8).Find("abⅠ"u8));
        Assert.Equal(new RegexMatch(2, 3), RegexAutomaton.Compile(@"[^\PN]+"u8).Find("abⅠ"u8));
        Assert.Equal(new RegexMatch(0, 3), RegexAutomaton.Compile(@"[\p{Emoji}]+"u8).Find("\u23E9x"u8));
    }

    /// <summary>
    /// Verifies Unicode binary property classes use pinned regex-syntax tables.
    /// </summary>
    [Fact]
    public void UnicodeBinaryPropertyClassesUsePinnedTables()
    {
        Assert.Equal(new RegexMatch(0, 3), RegexAutomaton.Compile(@"\p{Math}"u8).Find("⋿"u8));
        Assert.Equal(new RegexMatch(0, 3), RegexAutomaton.Compile(@"\p{Emoji}"u8).Find("\u23E9"u8));
        Assert.Equal(new RegexMatch(0, 4), RegexAutomaton.Compile(@"\p{emoji}"u8).Find("\U0001F21A"u8));
        Assert.Equal(new RegexMatch(0, 4), RegexAutomaton.Compile(@"\p{extendedpictographic}"u8).Find("\U0001FA6E"u8));
        Assert.Equal(new RegexMatch(0, 3), RegexAutomaton.Compile(@"\P{Emoji}+"u8).Find("abc\u23E9"u8));
    }

    /// <summary>
    /// Verifies Unicode grapheme-cluster-break classes use pinned regex-syntax tables.
    /// </summary>
    [Fact]
    public void UnicodeGraphemeClusterBreakClassesUsePinnedTables()
    {
        Assert.Equal(new RegexMatch(0, 1), RegexAutomaton.Compile(@"\p{gcb=CR}"u8).Find("\r"u8));
        Assert.Equal(new RegexMatch(0, 1), RegexAutomaton.Compile(@"\p{gcb=LF}"u8).Find("\n"u8));
        Assert.Equal(new RegexMatch(0, 2), RegexAutomaton.Compile(@"\p{gcb=Extend}"u8).Find("\u0300"u8));
        Assert.Equal(new RegexMatch(0, 3), RegexAutomaton.Compile(@"\p{gcb=SpacingMark}"u8).Find("\u0903"u8));
        Assert.Equal(new RegexMatch(0, 5), RegexAutomaton.Compile(@"[\p{gcb=Extend}\p{gcb=ZWJ}]+"u8).Find("\u0300\u200D"u8));
    }

    /// <summary>
    /// Verifies UTF-8 word classes consume Unicode word scalars.
    /// </summary>
    [Fact]
    public void Utf8WordClassMatchesUnicodeScalars()
    {
        Assert.Equal(new RegexMatch(0, 6), RegexAutomaton.Compile(@"\b\w+\b"u8).Find("βββ☃"u8));
        Assert.Equal(new RegexMatch(0, 2), RegexAutomaton.Compile(@"[\w]+"u8).Find("β☃"u8));
        Assert.Equal(new RegexMatch(0, 2), RegexAutomaton.Compile(@"\w"u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, utf8: false).Find("β"u8));
        Assert.Null(RegexAutomaton.Compile(@"\w"u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, unicodeClasses: false).Find("β"u8));
        Assert.Null(RegexAutomaton.Compile(@"\w"u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, utf8: false, unicodeClasses: false).Find("β"u8));
    }

    /// <summary>
    /// Verifies POSIX alpha classes use regex-syntax's pinned Alphabetic table, not runtime Unicode categories.
    /// </summary>
    [Fact]
    public void PosixAlphaUsesPinnedUnicodeAlphabeticTable()
    {
        Assert.Equal(new RegexMatch(0, 2), RegexAutomaton.Compile(@"[[:alpha:]]"u8).Find("\u0345"u8));
        Assert.Equal(new RegexMatch(0, 2), RegexAutomaton.Compile(@"[[:alnum:]]"u8).Find("\u0345"u8));
        Assert.Null(RegexAutomaton.Compile(@"[[:alpha:]]"u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, unicodeClasses: false).Find("\u0345"u8));
    }

    /// <summary>
    /// Verifies UTF-8 word boundaries inspect adjacent Unicode scalars.
    /// </summary>
    [Fact]
    public void Utf8WordBoundariesInspectUnicodeScalars()
    {
        var automaton = RegexAutomaton.Compile(@"\b[0-9]+\b"u8);

        Assert.Null(automaton.Find("β123"u8, startAt: 2));
        Assert.Null(automaton.Find("123β"u8));
        Assert.Equal(new RegexMatch(2, 3), RegexAutomaton.Compile(@"\b[0-9]+\b"u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, unicodeClasses: false).Find("β123"u8, startAt: 2));
        Assert.Equal(new RegexMatch(2, 3), RegexAutomaton.Compile(@"(?-u:\b[0-9]+\b)"u8).Find("β123"u8, startAt: 2));
    }

    /// <summary>
    /// Verifies Unicode case-insensitive matching applies simple folds that reach ASCII.
    /// </summary>
    [Fact]
    public void UnicodeCaseInsensitiveClassesFoldToAscii()
    {
        Assert.Equal(new RegexMatch(0, 7), RegexAutomaton.Compile("[a-z]+"u8, caseInsensitive: true, multiLine: false, dotMatchesNewline: false).Find("aA\u212AaA"u8));
        Assert.Equal(new RegexMatch(0, 7), RegexAutomaton.Compile("[a-z]+"u8, caseInsensitive: true, multiLine: false, dotMatchesNewline: false, utf8: false).Find("aA\u212AaA"u8));
        Assert.Equal(new RegexMatch(0, 3), RegexAutomaton.Compile("k"u8, caseInsensitive: true, multiLine: false, dotMatchesNewline: false).Find("\u212A"u8));
        Assert.Null(RegexAutomaton.Compile("[a-z]+"u8, caseInsensitive: true, multiLine: false, dotMatchesNewline: false, unicodeClasses: false).Find("\u212A"u8));
    }

    /// <summary>
    /// Verifies Unicode scalar-sensitive atoms lower to byte-DFA states without losing Unicode semantics.
    /// </summary>
    [Fact]
    public void UnicodeScalarAtomsCompileToByteDfa()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("[a-z]+0"u8);
        var options = new RegexCompileOptions(
            caseInsensitive: true,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        RegexNfa unicodeNfa = RegexNfaCompiler.Compile(tree.Root, options);

        Assert.True(RegexDfaOperations.CanCompile(unicodeNfa));

        var automaton = RegexAutomaton.Compile(
            "[a-z]+0"u8,
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] kelvinHaystack = System.Text.Encoding.UTF8.GetBytes("11\u212A0");

        Assert.NotEqual(RegexEngineKind.PikeVm, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(2, 2), automaton.Find("11K0"u8));
        Assert.Equal(new RegexMatch(2, 4), automaton.Find(kelvinHaystack));
        Assert.Null(automaton.Find("1120"u8));
    }

    /// <summary>
    /// Verifies Unicode byte-DFA lowering preserves Unicode digit, whitespace, negated class and dot semantics.
    /// </summary>
    [Fact]
    public void UnicodeByteDfaPreservesScalarClassSemantics()
    {
        var digitSpaceDot = RegexAutomaton.Compile(
            @"\d+\s."u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] digitHaystack = System.Text.Encoding.UTF8.GetBytes("xx\u06F1\u06F2\u00A0k\n");
        byte[] digitMatchText = System.Text.Encoding.UTF8.GetBytes("\u06F1\u06F2\u00A0k");

        Assert.NotEqual(RegexEngineKind.PikeVm, GetEngineKind(digitSpaceDot));
        Assert.Equal(new RegexMatch(2, digitMatchText.Length), digitSpaceDot.Find(digitHaystack));

        var notLetters = RegexAutomaton.Compile(
            "[^A-Za-z]+"u8,
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] notLetterHaystack = System.Text.Encoding.UTF8.GetBytes("abc\u212A \u06F1\u06F2");
        int notLetterStart = System.Text.Encoding.UTF8.GetByteCount("abc\u212A");
        int notLetterLength = System.Text.Encoding.UTF8.GetByteCount(" \u06F1\u06F2");

        Assert.NotEqual(RegexEngineKind.PikeVm, GetEngineKind(notLetters));
        Assert.Equal(new RegexMatch(notLetterStart, notLetterLength), notLetters.Find(notLetterHaystack));

        var dot = RegexAutomaton.Compile(
            "."u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] emojiThenNewline = System.Text.Encoding.UTF8.GetBytes("\U0001F4A9\n");

        Assert.NotEqual(RegexEngineKind.PikeVm, GetEngineKind(dot));
        Assert.Equal(new RegexMatch(0, 4), dot.Find(emojiThenNewline));
        Assert.Null(dot.Find(emojiThenNewline, startAt: 4));
    }

    /// <summary>
    /// Verifies Unicode aggregate scans match ASCII and Unicode digits.
    /// </summary>
    [Fact]
    public void UnicodeByteDfaMatchesAsciiAndUnicodeDigits()
    {
        var automaton = RegexAutomaton.Compile(
            @"\d+"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false);
        byte[] extendedArabicIndic = System.Text.Encoding.UTF8.GetBytes("123\u06F4x");

        Assert.Equal(new RegexMatch(0, 3), automaton.Find("123x"u8));
        Assert.Equal(new RegexMatch(0, 5), automaton.Find(extendedArabicIndic));
    }

    /// <summary>
    /// Verifies aggregate scans keep progress around non-ASCII digits.
    /// </summary>
    [Fact]
    public void UnicodeByteDfaAggregateCountsUnicodeDigits()
    {
        var automaton = RegexAutomaton.Compile(
            @"\d+"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("123 456 \u06F7 89 10");

        Assert.Equal(5, automaton.CountMatches(haystack));
        Assert.Equal(12, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies aggregate scans do not truncate ASCII matches that Unicode case folding can extend.
    /// </summary>
    [Fact]
    public void UnicodeByteDfaAggregateDoesNotTruncateCaseFold()
    {
        var automaton = RegexAutomaton.Compile(
            "[a-z]+"u8,
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("ab\u212A cd");

        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(7, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies Unicode-aware start predicates use first-byte fallbacks for case folds and alternations.
    /// </summary>
    [Fact]
    public void UnicodeStartPredicateCollectsCaseFoldedAlternationFirstBytes()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"(?:jan|\d+|k)"u8);
        var options = new RegexCompileOptions(
            caseInsensitive: true,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false);

        Assert.True(RegexStartPredicate.TryCreate(tree.Root, options, out RegexStartPredicate? predicate));
        Assert.NotNull(predicate);
        Assert.True(predicate.CanStartAt("Jan"u8, 0));
        Assert.True(predicate.CanStartAt("9"u8, 0));
        Assert.True(predicate.CanStartAt(System.Text.Encoding.UTF8.GetBytes("\u212A"), 0));
        Assert.False(predicate.CanStartAt("!"u8, 0));
    }

    /// <summary>
    /// Verifies start predicates merge common alternation positions beyond the first byte.
    /// </summary>
    [Fact]
    public void StartPredicateCollectsMultiByteAlternationPrefixes()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"cat|dog|cow"u8);
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.True(RegexStartPredicate.TryCreate(tree.Root, options, out RegexStartPredicate? predicate));
        Assert.NotNull(predicate);
        Assert.True(predicate.CanStartAt("cat"u8, 0));
        Assert.True(predicate.CanStartAt("dog"u8, 0));
        Assert.True(predicate.CanStartAt("cow"u8, 0));
        Assert.False(predicate.CanStartAt("can"u8, 0));
        Assert.False(predicate.CanStartAt("dig"u8, 0));
    }

    /// <summary>
    /// Verifies Unicode atoms still consume scalars when the search allows invalid UTF-8 haystacks.
    /// </summary>
    [Fact]
    public void UnicodeAtomsRespectScalarsWhenUtf8SearchIsDisabled()
    {
        byte[] invalid = [0xFF, (byte)'a', 0xFF];

        Assert.Null(RegexAutomaton.Compile("^(?s:.)*?[a-z]"u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, utf8: false).Find(invalid));
        Assert.Equal(new RegexMatch(1, 1), RegexAutomaton.Compile("[a-z]"u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, utf8: false).Find(invalid));
        Assert.Equal(new RegexMatch(0, 3), RegexAutomaton.Compile(@"\w+"u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, utf8: false).Find("aδ"u8));
        Assert.Equal(new RegexMatch(0, 2), RegexAutomaton.Compile("[^a]"u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, utf8: false).Find("δ"u8));
    }

    /// <summary>
    /// Verifies UTF-8 mode does not report matches inside a valid code point.
    /// </summary>
    [Fact]
    public void Utf8ModeDoesNotSplitCodepoints()
    {
        byte[] poop = [0xF0, 0x9F, 0x92, 0xA9];
        byte[] delta = [0xCE, 0xB4];
        var empty = RegexAutomaton.Compile(""u8);

        Assert.Equal(new RegexMatch(0, 4), RegexAutomaton.Compile("."u8).Find(poop));
        Assert.Equal(new RegexMatch(0, 4), RegexAutomaton.Compile("[^a]"u8).Find(poop));
        Assert.Equal(new RegexMatch(4, 0), empty.Find(poop, startAt: 1));
        Assert.Null(empty.MatchAt(poop, startAt: 1));
        Assert.Equal(new RegexMatch(4, 0), empty.FindEarliest(poop, startAt: 1));
        Assert.Null(empty.FindAllKindAt(poop, startAt: 1));
        Assert.Empty(empty.FindOverlappingAt(poop, startAt: 1));
        Assert.Equal(2, empty.CountMatches(poop));
        Assert.Equal(1, empty.CountMatches(poop, startAt: 1));
        Assert.Equal(new RegexMatch(4, 0), RegexAutomaton.Compile(@"(?:(?-u:\B)|(?su:.))+"u8).Find(poop, startAt: 1));
        Assert.Null(RegexAutomaton.Compile(@"\B"u8).Find(delta, startAt: 1));
    }

    /// <summary>
    /// Verifies byte mode preserves byte-by-byte matching.
    /// </summary>
    [Fact]
    public void ByteModeCanSplitCodepoints()
    {
        byte[] poop = [0xF0, 0x9F, 0x92, 0xA9];
        var empty = RegexAutomaton.Compile(""u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, utf8: false);

        Assert.Equal(new RegexMatch(0, 1), RegexAutomaton.Compile("."u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, utf8: false, unicodeClasses: false).Find(poop));
        Assert.Equal(new RegexMatch(1, 0), empty.Find(poop, startAt: 1));
        Assert.Equal(new RegexMatch(1, 0), empty.MatchAt(poop, startAt: 1));
        Assert.Equal(5, empty.CountMatches(poop));
        Assert.Equal(4, empty.CountMatches(poop, startAt: 1));
        Assert.Equal(new RegexMatch(1, 1), RegexAutomaton.Compile("(?-u:.)"u8).Find(poop, startAt: 1));
    }

    /// <summary>
    /// Verifies root compile options match multiline search configuration and remain overridable by inline flags.
    /// </summary>
    [Fact]
    public void AppliesRootRegexOptions()
    {
        Assert.Equal(new RegexMatch(0, 3), RegexAutomaton.Compile("a.b"u8, multiLine: false, dotMatchesNewline: true).Find("a\nb"u8));
        Assert.Equal(new RegexMatch(0, 7), RegexAutomaton.Compile("foo.*bar"u8, multiLine: false, dotMatchesNewline: true).Find("foo\nbar"u8));
        Assert.Equal(new RegexMatch(0, 7), RegexAutomaton.Compile("foo.*bar"u8, multiLine: true, dotMatchesNewline: true).Find("foo\nbar\n"u8));
        Assert.Equal(new RegexMatch(4, 3), RegexAutomaton.Compile("^foo$"u8, multiLine: true, dotMatchesNewline: false).Find("bar\nfoo\nbaz"u8));
        Assert.Equal(new RegexMatch(0, 3), RegexAutomaton.Compile("abc"u8, caseInsensitive: true, multiLine: false, dotMatchesNewline: false).Find("ABC"u8));
        Assert.Null(RegexAutomaton.Compile("(?-i:abc)"u8, caseInsensitive: true, multiLine: false, dotMatchesNewline: false).Find("ABC"u8));
        Assert.Null(RegexAutomaton.Compile("(?-s:a.b)"u8, multiLine: false, dotMatchesNewline: true).Find("a\nb"u8));
    }

    /// <summary>
    /// Verifies extended mode ignores pattern whitespace and comments outside character classes.
    /// </summary>
    [Fact]
    public void AppliesExtendedMode()
    {
        var compact = RegexAutomaton.Compile("(?x) a # comment\n b c"u8);
        var escapedSpace = RegexAutomaton.Compile(@"(?x)a\ b"u8);
        var disabled = RegexAutomaton.Compile("(?x:a(?-x: b )c)"u8);

        Assert.Equal(new RegexMatch(0, 3), compact.Find("abc"u8));
        Assert.Null(compact.Find("a b c"u8));
        Assert.Equal(new RegexMatch(0, 3), escapedSpace.Find("a b"u8));
        Assert.Equal(new RegexMatch(0, 5), disabled.Find("a b c"u8));
    }

    /// <summary>
    /// Verifies shorthand and POSIX classes combine with word-end assertions.
    /// </summary>
    [Fact]
    public void MatchesClassesAndWordBoundaries()
    {
        var automaton = RegexAutomaton.Compile(@"[[:alpha:]]+\d{2,3}?\b{end}"u8);

        RegexMatch? match = automaton.Find("11abc123 yy"u8);

        Assert.True(match.HasValue);
        Assert.Equal(new RegexMatch(2, 6), match.Value);
    }

    /// <summary>
    /// Verifies start and end anchors constrain matches.
    /// </summary>
    [Fact]
    public void MatchesAnchors()
    {
        var automaton = RegexAutomaton.Compile("^foo$"u8);

        Assert.Equal(new RegexMatch(0, 3), automaton.Find("foo"u8));
        Assert.Null(automaton.Find("xfoo"u8));
        Assert.Null(automaton.Find("foox"u8));
    }

    /// <summary>
    /// Verifies absolute anchors ignore multiline line boundaries.
    /// </summary>
    [Fact]
    public void MatchesAbsoluteAnchors()
    {
        var start = RegexAutomaton.Compile("\\Abar"u8, multiLine: true, dotMatchesNewline: false);
        var end = RegexAutomaton.Compile("bar\\z"u8, multiLine: true, dotMatchesNewline: false);

        Assert.Equal(new RegexMatch(0, 3), start.Find("bar\nfoo"u8));
        Assert.Null(start.Find("foo\nbar"u8));
        Assert.Equal(new RegexMatch(4, 3), end.Find("foo\nbar"u8));
        Assert.Null(end.Find("foo\nbar\n"u8));
    }

    /// <summary>
    /// Verifies a leading alternation inside a sequence uses a prefix-set prefilter.
    /// </summary>
    [Fact]
    public void BuildsPrefixPrefilterForSequenceLeadingAlternation()
    {
        var automaton = RegexAutomaton.Compile(
            @"(?:public|private|protected|internal)[^;={}]*\([^)]*(?:,[^)]*){8,}\)"u8,
            multiLine: true,
            dotMatchesNewline: false);

        Assert.NotEqual(RegexPrefilterKind.None, automaton.PrefilterKind);
        Assert.Equal(
            new RegexMatch(7, 41),
            automaton.Find("ignore\ninternal static void M(a,b,c,d,e,f,g,h,i)\n"u8));
    }

    /// <summary>
    /// Verifies hard start anchors skip unhelpful prefilter construction.
    /// </summary>
    [Fact]
    public void SkipsPrefilterForHardStartAnchoredPattern()
    {
        var automaton = RegexAutomaton.Compile(
            @"^([A-Z0-9]+);([^;]+);([YN])$"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(RegexPrefilterKind.None, automaton.PrefilterKind);
        Assert.Equal(new RegexMatch(0, 11), automaton.Find("0041;NAME;Y"u8));
        Assert.Null(automaton.Find("xx\n0041;NAME;Y"u8));
    }

    /// <summary>
    /// Verifies multiline start anchors still build useful prefilters.
    /// </summary>
    [Fact]
    public void BuildsPrefilterForMultilineStartAnchoredPattern()
    {
        var automaton = RegexAutomaton.Compile(
            @"(?m)^([A-Z0-9]+);needle$"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.NotEqual(RegexPrefilterKind.None, automaton.PrefilterKind);
        Assert.Equal(new RegexMatch(3, 9), automaton.Find("xx\nAB;needle\n"u8));
    }

    /// <summary>
    /// Verifies leading prefix-set prefilters support case-insensitive literals.
    /// </summary>
    [Fact]
    public void BuildsCaseInsensitivePrefixPrefilterForSequenceLeadingAlternation()
    {
        var automaton = RegexAutomaton.Compile(
            @"(?:public|private|protected|internal)[^;={}]*\([^)]*(?:,[^)]*){8,}\)"u8,
            caseInsensitive: true,
            multiLine: true,
            dotMatchesNewline: false);

        Assert.NotEqual(RegexPrefilterKind.None, automaton.PrefilterKind);
        Assert.Equal(
            new RegexMatch(7, 41),
            automaton.Find("ignore\nINTERNAL static void M(a,b,c,d,e,f,g,h,i)\n"u8));
    }

    /// <summary>
    /// Verifies Unicode case-fold variants remain visible to case-insensitive prefix prefilters.
    /// </summary>
    [Fact]
    public void CaseInsensitivePrefixPrefilterIncludesUnicodeFoldVariants()
    {
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("\u212Ailo7");
        var automaton = RegexAutomaton.Compile(
            "(?:kilo|mega)[0-9]+"u8,
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false);

        Assert.Equal(RegexPrefilterKind.AhoCorasick, automaton.PrefilterKind);
        Assert.Equal(new RegexMatch(0, haystack.Length), automaton.Find(haystack));
    }

    /// <summary>
    /// Verifies prefix-set prefilters can derive finite prefixes through leading digit classes.
    /// </summary>
    [Fact]
    public void BuildsPrefixPrefilterFromFiniteLeadingClasses()
    {
        var automaton = RegexAutomaton.Compile(
            "(?:19\\d\\d|20\\d\\d|due)\\n"u8,
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false);

        Assert.Equal(RegexPrefilterKind.AhoCorasick, automaton.PrefilterKind);
        Assert.Equal(new RegexMatch(4, 5), automaton.Find("xxx 2012\n"u8));
    }

    /// <summary>
    /// Verifies optional leading bytes preserve both optional and consumed prefixes.
    /// </summary>
    [Fact]
    public void BuildsPrefixPrefilterAcrossOptionalLeadingLiteral()
    {
        var automaton = RegexAutomaton.Compile(
            "(?:-?\\d{4}|due)\\n"u8,
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false);

        Assert.Equal(RegexPrefilterKind.AhoCorasick, automaton.PrefilterKind);
        Assert.Equal(new RegexMatch(2, 6), automaton.Find("xx-2024\n"u8));
        Assert.Equal(new RegexMatch(2, 5), automaton.Find("xx2024\n"u8));
    }

    /// <summary>
    /// Verifies finite character-class prefixes do not skip adjacent class tokens.
    /// </summary>
    [Fact]
    public void BuildsPrefixPrefilterFromAdjacentCharacterClassTokens()
    {
        var automaton = RegexAutomaton.Compile(
            "(?:[/:\\-,.\\s_+@]+|due)\\n"u8,
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false);

        Assert.Equal(RegexPrefilterKind.AhoCorasick, automaton.PrefilterKind);
        Assert.Equal(new RegexMatch(2, 2), automaton.Find("xx,\n"u8));
    }

    /// <summary>
    /// Verifies large ASCII case-insensitive patterns still keep a cheap required-prefix prefilter.
    /// </summary>
    [Fact]
    public void LargeCaseInsensitiveAsciiPrefilterKeepsRequiredPrefix()
    {
        var pattern = new System.Text.StringBuilder("prefix(?:");
        for (int index = 0; index < 600; index++)
        {
            if (index > 0)
            {
                pattern.Append('|');
            }

            pattern.Append(index.ToString("D3"));
        }

        pattern.Append(")[0-9]");
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(System.Text.Encoding.ASCII.GetBytes(pattern.ToString()));
        var options = new RegexCompileOptions(
            caseInsensitive: true,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        var prefilter = RegexPrefilter.Compile(tree.Root, options);

        Assert.NotNull(prefilter);
        Assert.Equal(RegexPrefilterKind.AhoCorasick, prefilter.Kind);
    }

    /// <summary>
    /// Verifies large ASCII case-insensitive prefix sets without cheap literals skip expensive prefilter construction.
    /// </summary>
    [Fact]
    public void LargeCaseInsensitiveAsciiPrefilterSkipsBroadPrefixSet()
    {
        var pattern = new System.Text.StringBuilder("(?:");
        for (int index = 0; index < 600; index++)
        {
            if (index > 0)
            {
                pattern.Append('|');
            }

            switch (index % 4)
            {
                case 0:
                    pattern.Append(@"\d{1,2}:\d{2}");
                    break;
                case 1:
                    pattern.Append(@"\s+[a-z]{3}");
                    break;
                case 2:
                    pattern.Append("jan");
                    pattern.Append(index.ToString("D3"));
                    break;
                default:
                    pattern.Append("[._-]foo");
                    pattern.Append(index.ToString("D3"));
                    break;
            }
        }

        pattern.Append(")z");
        byte[] patternBytes = System.Text.Encoding.ASCII.GetBytes(pattern.ToString());
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(patternBytes);
        var options = new RegexCompileOptions(
            caseInsensitive: true,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        var prefilter = RegexPrefilter.Compile(tree.Root, options);
        var automaton = RegexAutomaton.Compile(
            patternBytes,
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Null(prefilter);
        Assert.Equal(new RegexMatch(2, 7), automaton.Find("xxJAN002z"u8));
    }

    /// <summary>
    /// Verifies broad leading classes fall through to selective required-literal prefilters.
    /// </summary>
    [Fact]
    public void SkipsBroadClassPrefixPrefilterWhenRequiredLiteralExists()
    {
        byte[] pattern = System.Text.Encoding.ASCII.GetBytes(
            """(?:["'][A-Za-z0-9+/]{40}["'].*?(?:ASIA|AKIA))""");
        byte[] haystack = System.Text.Encoding.ASCII.GetBytes(
            "\"" + new string('a', 40) + "\" trailing AKIA");
        var automaton = RegexAutomaton.Compile(
            pattern,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(RegexPrefilterKind.RequiredLiteral, automaton.PrefilterKind);
        Assert.Equal(new RegexMatch(0, haystack.Length), automaton.Find(haystack));
    }

    /// <summary>
    /// Verifies Unicode class prefixes include non-ASCII UTF-8 start bytes.
    /// </summary>
    [Fact]
    public void BuildsPrefixPrefilterFromUnicodeWhitespaceClassToken()
    {
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("\u2028X");
        var automaton = RegexAutomaton.Compile(
            "(?:[\\s,]+|due)X"u8,
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false,
            unicodeClasses: true);

        Assert.Equal(RegexPrefilterKind.AhoCorasick, automaton.PrefilterKind);
        Assert.Equal(new RegexMatch(0, haystack.Length), automaton.Find(haystack));
    }

    /// <summary>
    /// Verifies impossible sequence-prefix candidates are skipped without hiding a later valid match.
    /// </summary>
    [Fact]
    public void SequenceAlternationPrefixGatePreservesLaterSignatureMatch()
    {
        var automaton = RegexAutomaton.Compile(
            @"(?:public|private|protected|internal)[^;={}]*\([^)]*(?:,[^)]*){8,}\)"u8,
            multiLine: true,
            dotMatchesNewline: false);

        const string prefix = "public nope;\nprivate nope;\n";
        const string match = "internal static void M(a,b,c,d,e,f,g,h,i)";
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes(prefix + match + "\n");

        Assert.Equal(
            new RegexMatch(prefix.Length, match.Length),
            automaton.Find(haystack));
    }

    /// <summary>
    /// Verifies prefix-gate skipping stays conservative when terminators can also start a prefix.
    /// </summary>
    [Fact]
    public void SequenceAlternationPrefixGatePreservesOverlappingTerminatorPrefix()
    {
        var automaton = RegexAutomaton.Compile(@"(?:x|y)[^x]*a"u8);

        Assert.Equal(new RegexMatch(1, 2), automaton.Find("xxa"u8));
    }

    /// <summary>
    /// Verifies prefix-gate skipping is not applied across inline flag changes.
    /// </summary>
    [Fact]
    public void SequenceAlternationPrefixGateIgnoresInlineFlagShapes()
    {
        var automaton = RegexAutomaton.Compile(@"(?:a|b)[^x]*(?i)z"u8);

        Assert.Equal(new RegexMatch(0, 2), automaton.Find("aZ"u8));
    }

    /// <summary>
    /// Verifies a large lazy-DFA state space fails over before pathological no-match verification stalls.
    /// </summary>
    [Fact(Timeout = 1000)]
    public void RejectsUnterminatedRepeatedRegexSignatureWithoutStalling()
    {
        var automaton = RegexAutomaton.Compile(
            @"(?:public|private|protected|internal)[^;={}]*\([^)]*(?:,[^)]*){8,}\)"u8,
            multiLine: true,
            dotMatchesNewline: false);
        var fallbackAutomaton = RegexAutomaton.Compile(
            @"(?:public|private|protected|internal)[^;={}]*\([^)]*(?:,[^)]*){8,}\)"u8,
            caseInsensitive: false,
            multiLine: true,
            dotMatchesNewline: false,
            dfaSizeLimit: 0);

        byte[] haystack = System.Text.Encoding.UTF8.GetBytes(
            "internal static bool Foo(" +
            string.Concat(Enumerable.Repeat("ReadOnlyMemory<byte>? replacement,", 20)));

        Assert.Null(automaton.Find(haystack));
        Assert.Null(fallbackAutomaton.Find(haystack));
    }

    /// <summary>
    /// Verifies failed lazy-DFA prefix candidates stop at the dead state instead of rescanning to EOF.
    /// </summary>
    [Fact(Timeout = 1000)]
    public void RejectsManyFailedSignaturePrefixesWithoutRescanningRemainder()
    {
        var automaton = RegexAutomaton.Compile(
            @"(?:public|private|protected|internal)[^;={}]*\([^)]*(?:,[^)]*){8,}\)"u8,
            multiLine: true,
            dotMatchesNewline: false);

        byte[] haystack = System.Text.Encoding.UTF8.GetBytes(
            string.Concat(Enumerable.Repeat("public nope;\n", 20_000)));

        Assert.Null(automaton.Find(haystack));
    }

    /// <summary>
    /// Verifies bounded literal context patterns avoid expanded lazy-DFA execution.
    /// </summary>
    [Fact]
    public void UsesSimpleSequenceEngineForBoundedLiteralContext()
    {
        var automaton = RegexAutomaton.Compile(
            @"[A-Za-z]{10}\s+[\s\S]{0,100}Result[\s\S]{0,100}\s+[A-Za-z]{10}"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        string match = "ABCDEFGHIJ " +
            new string('x', 24) +
            "Result" +
            new string('y', 20) +
            " ZYXWVUTSRQ";
        byte[] haystack = System.Text.Encoding.ASCII.GetBytes("zz " + match + " tail");

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.Equal(RegexPrefilterKind.RequiredLiteral, automaton.PrefilterKind);
        Assert.Equal(new RegexMatch(3, match.Length), automaton.Find(haystack));
    }

    /// <summary>
    /// Verifies fixed-width byte class sequences ending in a literal scan from the suffix.
    /// </summary>
    [Fact]
    public void CountsFixedWidthByteClassLiteralSuffixSequences()
    {
        var automaton = RegexAutomaton.Compile(
            @"[a-q][^u-z]{3}x"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "zzabcqx yy b123x c111x"u8.ToArray();

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(2, 5), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(11, 5), automaton.Find(haystack, startAt: 3));
        Assert.Equal(3, automaton.CountMatches(haystack));
        Assert.Equal(15, automaton.SumMatchSpans(haystack));
        Assert.Equal(2, automaton.CountMatches(haystack, startAt: 3));
        Assert.Equal(10, automaton.SumMatchSpans(haystack, startAt: 3));
    }

    /// <summary>
    /// Verifies literal/whitespace/literal sequences avoid the generic recursive matcher.
    /// </summary>
    [Fact]
    public void CountsLiteralWhitespaceLiteralSequences()
    {
        var automaton = RegexAutomaton.Compile(
            @"Sherlock\s+Holmes"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "aa Sherlock Holmes bb Sherlock\tHolmes cc Sherlock  Holmes dd SherlockHolmes ee"u8.ToArray();

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        RegexMatch? first = automaton.Find(haystack);
        Assert.Equal(new RegexMatch(3, 15), first);
        Assert.Equal(new RegexMatch(22, 15), automaton.Find(haystack, first!.Value.End));
        Assert.Equal(3, automaton.CountMatches(haystack));
        Assert.Equal(46, automaton.SumMatchSpans(haystack));
        Assert.Equal(2, automaton.CountMatches(haystack, startAt: first.Value.End));
        Assert.Equal(31, automaton.SumMatchSpans(haystack, startAt: first.Value.End));

        var bounded = RegexAutomaton.Compile(
            @"Sherlock\s{1,2}Holmes"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] boundedHaystack = "Sherlock Holmes Sherlock   Holmes Sherlock\t\nHolmes"u8.ToArray();

        Assert.Equal(2, bounded.CountMatches(boundedHaystack));
        Assert.Equal(31, bounded.SumMatchSpans(boundedHaystack));
    }

    /// <summary>
    /// Verifies fixed word/whitespace sequences scan from their whitespace anchor.
    /// </summary>
    [Fact]
    public void CountsFixedWordWhitespaceSequences()
    {
        var automaton = RegexAutomaton.Compile(
            @"\w{5}\s\w{6}\s\w{7}"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xx abcde foobar bazquux tail abcde foobar bazquuxxxx abcde foo_bar seven77"u8.ToArray();

        Assert.Equal(RegexEngineKind.FixedWordWhitespaceSequence, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(3, 20), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(29, 20), automaton.Find(haystack, startAt: 4));
        Assert.Equal(new RegexMatch(29, 20), automaton.FindEarliest(haystack, startAt: 4));
        Assert.Equal(new RegexMatch(3, 20), automaton.FindAllKindAt(haystack, startAt: 3));
        Assert.Equal([new RegexMatch(3, 20)], automaton.FindOverlappingAt(haystack, startAt: 3));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(40, automaton.SumMatchSpans(haystack));
        Assert.Equal(1, automaton.CountMatches(haystack, startAt: 4));
        Assert.Equal(20, automaton.SumMatchSpans(haystack, startAt: 4));
        Assert.Null(automaton.FindAllKindAt(haystack, startAt: 4));

        byte[] shortFirstWord = "zz abc defghi jklmnop"u8.ToArray();
        Assert.Null(automaton.Find(shortFirstWord));
        Assert.Equal(0, automaton.CountMatches(shortFirstWord));
        Assert.Equal(0, automaton.SumMatchSpans(shortFirstWord));
    }

    /// <summary>
    /// Verifies repeated literal run alternations with an empty branch count in linear time.
    /// </summary>
    [Fact]
    public void RepeatedLiteralRunOrEmptyCountsEmptyFallbacks()
    {
        var automaton = RegexAutomaton.Compile(
            @"(?:A+){4}|"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(RegexEngineKind.RepeatedLiteralRunOrEmpty, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 0), automaton.Find("AAA"u8));
        Assert.Equal(4, automaton.CountMatches("AAA"u8));
        Assert.Equal(0, automaton.SumMatchSpans("AAA"u8));
        Assert.Equal(new RegexMatch(0, 5), automaton.Find("AAAAA"u8));
        Assert.Equal(2, automaton.CountMatches("AAAAA"u8));
        Assert.Equal(5, automaton.SumMatchSpans("AAAAA"u8));
        Assert.Equal(new RegexMatch(1, 4), automaton.Find("xAAAA"u8, startAt: 1));
        Assert.Equal(2, automaton.CountMatches("xAAAA"u8, startAt: 1));
    }

    /// <summary>
    /// Verifies Unicode word classes keep scalar semantics on the fixed word/whitespace engine.
    /// </summary>
    [Fact]
    public void FixedWordWhitespaceSequencesPreserveUnicodeWordScalars()
    {
        var automaton = RegexAutomaton.Compile(
            @"\w{5}\s\w{6}\s\w{7}"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("xx αβγδε foobar bazquux tail");

        Assert.Equal(RegexEngineKind.FixedWordWhitespaceSequence, GetEngineKind(automaton));
        RegexMatch expected = new(3, System.Text.Encoding.UTF8.GetByteCount("αβγδε foobar bazquux"));
        Assert.Equal(expected, automaton.Find(haystack));
        Assert.Equal(1, automaton.CountMatches(haystack));
        Assert.Equal(expected.Length, automaton.SumMatchSpans(haystack));

        string mixed = "abcdé foobar bazquux xx abcde foobár bazquux yy abcde foobar bazquúx";
        byte[] mixedHaystack = System.Text.Encoding.UTF8.GetBytes(mixed);
        int firstLength = System.Text.Encoding.UTF8.GetByteCount("abcdé foobar bazquux");
        int secondStart = System.Text.Encoding.UTF8.GetByteCount("abcdé foobar bazquux xx ");
        int secondLength = System.Text.Encoding.UTF8.GetByteCount("abcde foobár bazquux");
        int thirdStart = System.Text.Encoding.UTF8.GetByteCount("abcdé foobar bazquux xx abcde foobár bazquux yy ");
        int thirdLength = System.Text.Encoding.UTF8.GetByteCount("abcde foobar bazquúx");

        Assert.Equal(new RegexMatch(0, firstLength), automaton.Find(mixedHaystack));
        Assert.Equal(new RegexMatch(secondStart, secondLength), automaton.Find(mixedHaystack, firstLength));
        Assert.Equal(new RegexMatch(thirdStart, thirdLength), automaton.Find(mixedHaystack, secondStart + secondLength));
        Assert.Equal(3, automaton.CountMatches(mixedHaystack));
        Assert.Equal(firstLength + secondLength + thirdLength, automaton.SumMatchSpans(mixedHaystack));

        var byteMode = RegexAutomaton.Compile(
            @"\w{5}\s\w{6}\s\w{7}"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        Assert.Null(byteMode.Find(haystack));
    }

    /// <summary>
    /// Verifies fixed-width suffix scanning preserves non-overlapping count semantics.
    /// </summary>
    [Fact]
    public void CountsFixedWidthLiteralSuffixSequencesNonOverlapping()
    {
        var automaton = RegexAutomaton.Compile(
            @"[a-q][^u-z]{3}x"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(2, automaton.CountMatches("a111xb222x"u8));
        Assert.Equal(10, automaton.SumMatchSpans("a111xb222x"u8));
    }

    /// <summary>
    /// Verifies ASCII letter runs with literal suffixes count by scanning each run once.
    /// </summary>
    [Fact]
    public void CountsAsciiLetterRunLiteralSuffixSequences()
    {
        var automaton = RegexAutomaton.Compile(
            @"[a-zA-Z]+ing"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xx singingx bringing thing iing ing"u8.ToArray();

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(3, 7), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(4, 6), automaton.Find(haystack, startAt: 4));
        Assert.Equal(4, automaton.CountMatches(haystack));
        Assert.Equal(24, automaton.SumMatchSpans(haystack));
        Assert.Equal(4, automaton.CountMatches(haystack, startAt: 4));
        Assert.Equal(23, automaton.SumMatchSpans(haystack, startAt: 4));
        Assert.Equal(0, automaton.CountMatches("ing"u8));
    }

    /// <summary>
    /// Verifies single bounded byte-class runs use direct simple-sequence scanning.
    /// </summary>
    [Fact]
    public void UsesSimpleSequenceEngineForSingleBoundedByteClassRun()
    {
        var automaton = RegexAutomaton.Compile(
            @"[A-Za-z]{2,3}"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 3), automaton.Find("abcdef 1 zz"u8));
        Assert.Equal(new RegexMatch(3, 3), automaton.Find("abcdef 1 zz"u8, startAt: 3));
        Assert.Equal(new RegexMatch(9, 2), automaton.Find("abcdef 1 zz"u8, startAt: 6));
        Assert.Equal(3, automaton.CountMatches("abcdef 1 zz"u8));
        Assert.Equal(8, automaton.SumMatchSpans("abcdef 1 zz"u8));
        Assert.Equal(2, automaton.CountMatches("abcdef 1 zz"u8, startAt: 3));
        Assert.Equal(5, automaton.SumMatchSpans("abcdef 1 zz"u8, startAt: 3));

        var fixedWidth = RegexAutomaton.Compile(
            @"[A-Za-z]{3}"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(fixedWidth));
        Assert.Equal(new RegexMatch(0, 3), fixedWidth.FindEarliest("abcd"u8, startAt: 0));
        Assert.Equal(new RegexMatch(0, 3), fixedWidth.FindAllKindAt("abcd"u8, startAt: 0));
        Assert.Equal([new RegexMatch(0, 3)], fixedWidth.FindOverlappingAt("abcd"u8, startAt: 0));
    }

    /// <summary>
    /// Verifies long ASCII byte-class runs count bounded chunks and trailing remainders correctly.
    /// </summary>
    [Fact]
    public void CountsLongBoundedAsciiLetterRunsByChunks()
    {
        var automaton = RegexAutomaton.Compile(
            @"[A-Za-z]{8,13}"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = System.Text.Encoding.ASCII.GetBytes(
            new string('a', 13) + " " +
            new string('b', 21) + " " +
            new string('c', 7) + " " +
            new string('d', 8));

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.Equal(4, automaton.CountMatches(haystack));
        Assert.Equal(42, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies byte-mode ASCII word-boundary runs use direct simple-sequence scanning.
    /// </summary>
    [Fact]
    public void UsesSimpleSequenceEngineForAsciiWordBoundaryRuns()
    {
        var automaton = RegexAutomaton.Compile(
            @"\b[0-9A-Za-z_]+\b"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 3), automaton.Find("abc 12_x -- long_word"u8));
        Assert.Equal(new RegexMatch(4, 3), automaton.Find("abc def"u8, startAt: 1));
        Assert.Equal(3, automaton.CountMatches("abc 12_x -- long_word"u8));
        Assert.Equal(16, automaton.SumMatchSpans("abc 12_x -- long_word"u8));
        Assert.Equal(2, automaton.CountMatches("abc 12_x -- long_word"u8, startAt: 1));
        Assert.Equal(13, automaton.SumMatchSpans("abc 12_x -- long_word"u8, startAt: 1));
    }

    /// <summary>
    /// Verifies byte-mode ASCII word-boundary runs honor minimum repeat lengths.
    /// </summary>
    [Fact]
    public void AsciiWordBoundaryRunsHonorMinimumLength()
    {
        var automaton = RegexAutomaton.Compile(
            @"\b[0-9A-Za-z_]{4,}\b"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.True(automaton.IsMatch("abc 12_x -- long_word"u8));
        Assert.False(automaton.IsMatch("abc 123 -- xyz"u8));
        Assert.Equal(new RegexMatch(4, 4), automaton.Find("abc 12_x -- long_word"u8));
        Assert.Equal(new RegexMatch(12, 9), automaton.Find("abc 12_x -- long_word"u8, startAt: 5));
        Assert.Equal(2, automaton.CountMatches("abc 12_x -- long_word"u8));
        Assert.Equal(13, automaton.SumMatchSpans("abc 12_x -- long_word"u8));
        Assert.Equal(1, automaton.CountMatches("abc 12_x -- long_word"u8, startAt: 5));
        Assert.Equal(9, automaton.SumMatchSpans("abc 12_x -- long_word"u8, startAt: 5));

        byte[] longHaystack = System.Text.Encoding.ASCII.GetBytes(
            "abc " + new string('a', 40) + " -- bbb " + new string('c', 12));
        Assert.Equal(2, automaton.CountMatches(longHaystack));
        Assert.Equal(52, automaton.SumMatchSpans(longHaystack));
        Assert.Equal(1, automaton.CountMatches(longHaystack, startAt: 5));
        Assert.Equal(12, automaton.SumMatchSpans(longHaystack, startAt: 5));
    }

    /// <summary>
    /// Verifies long ASCII word-boundary IsMatch scans carry vector suffix runs across fragmented masks.
    /// </summary>
    [Fact]
    public void AsciiWordBoundaryRunIsMatchHandlesFragmentedVectorMasks()
    {
        var automaton = RegexAutomaton.Compile(
            @"\b\w{25,}\b"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] fragmented = System.Text.Encoding.ASCII.GetBytes(
            "abc def ghi jkl mno pqr stu vwx yz " + new string('a', 24));
        byte[] crossing = System.Text.Encoding.ASCII.GetBytes(
            "abc def ghi jkl mno pqr stu " + new string('x', 30));

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.False(automaton.IsMatch(fragmented));
        Assert.True(automaton.IsMatch(crossing));
        Assert.Equal(new RegexMatch(28, 30), automaton.Find(crossing));
        Assert.Equal(1, automaton.CountMatches(crossing));
        Assert.Equal(30, automaton.SumMatchSpans(crossing));
    }

    /// <summary>
    /// Verifies disjoint repeated byte runs with a suffix byte and word boundary avoid repeated PikeVM rescans.
    /// </summary>
    [Fact]
    public void UsesSimpleSequenceEngineForDisjointByteRunSuffixBoundary()
    {
        var automaton = RegexAutomaton.Compile(
            @"[A-Z]{2,}(?-u:[0-9])\b"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false);

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(3, 3), automaton.Find("xx AB1 CD2 EFG3!"u8));
        Assert.Equal(new RegexMatch(7, 3), automaton.Find("xx AB1 CD2 EFG3!"u8, startAt: 4));
        Assert.Equal(3, automaton.CountMatches("xx AB1 CD2 EFG3!"u8));
        Assert.Equal(10, automaton.SumMatchSpans("xx AB1 CD2 EFG3!"u8));
    }

    /// <summary>
    /// Verifies sparse-class no-match runs are rejected with one forward scan instead of retrying each start.
    /// </summary>
    [Fact(Timeout = 1000)]
    public void RejectsDisjointByteRunSuffixBoundaryNoMatchWithoutRescanning()
    {
        var automaton = RegexAutomaton.Compile(
            @"[0-24-68-9A-CE-GI-KM-OQ-SU-WY-Za-ce-gi-km-oq-su-wy-z]{100,}(?-u:[\x00-\x29])\b"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes(
            "💩" +
            string.Concat(Enumerable.Repeat("01245689ABCEFGIJKMNOQRSUVWYZabcefgijkmnoqrsuvwyz", 2_000)));

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.Null(automaton.Find(haystack));
        Assert.Equal(0, automaton.CountMatches(haystack));
        Assert.Equal(0, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies Unicode word-boundary runs use direct scalar-run scanning.
    /// </summary>
    [Fact]
    public void UsesSimpleSequenceEngineForUnicodeWordBoundaryRuns()
    {
        var automaton = RegexAutomaton.Compile(
            @"\b\w{3,}\b"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.True(automaton.IsMatch("ββ βββ word_12 ☃abc"u8));
        Assert.False(automaton.IsMatch("ββ xx ☃ab"u8));
        Assert.Equal(new RegexMatch(5, 6), automaton.Find("ββ βββ word_12 ☃abc"u8));
        Assert.Equal(new RegexMatch(12, 7), automaton.Find("ββ βββ word_12 ☃abc"u8, startAt: 6));
        Assert.Equal(3, automaton.CountMatches("ββ βββ word_12 ☃abc"u8));
        Assert.Equal(16, automaton.SumMatchSpans("ββ βββ word_12 ☃abc"u8));
        Assert.Equal(2, automaton.CountMatches("ββ βββ word_12 ☃abc"u8, startAt: 6));
        Assert.Equal(10, automaton.SumMatchSpans("ββ βββ word_12 ☃abc"u8, startAt: 6));
    }

    /// <summary>
    /// Verifies word-run suffix literals scan whole word runs instead of falling back to the NFA.
    /// </summary>
    [Fact]
    public void UsesWordSuffixLiteralEngineForWordEndingLiterals()
    {
        var doubleSuffix = RegexAutomaton.Compile(
            @"\b\w+nn\b"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        var singleSuffix = RegexAutomaton.Compile(
            @"\b\w+n\b"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(RegexEngineKind.WordSuffixLiteral, GetEngineKind(doubleSuffix));
        Assert.Equal(new RegexMatch(3, 3), doubleSuffix.Find("nn ann annx xnn nnnn"u8));
        Assert.Equal(new RegexMatch(12, 3), doubleSuffix.Find("nn ann annx xnn nnnn"u8, startAt: 4));
        Assert.Equal(3, doubleSuffix.CountMatches("nn ann annx xnn nnnn"u8));
        Assert.Equal(10, doubleSuffix.SumMatchSpans("nn ann annx xnn nnnn"u8));
        Assert.Equal(new RegexMatch(3, 3), doubleSuffix.MatchAt("nn ann"u8, 3));
        Assert.Null(doubleSuffix.MatchAt("nn ann"u8, 4));

        Assert.Equal(RegexEngineKind.WordSuffixLiteral, GetEngineKind(singleSuffix));
        Assert.Equal(new RegexMatch(2, 2), singleSuffix.Find("n an ann annx fin_"u8));
        Assert.Equal(2, singleSuffix.CountMatches("n an ann annx fin_"u8));
        Assert.Equal(5, singleSuffix.SumMatchSpans("n an ann annx fin_"u8));
    }

    /// <summary>
    /// Verifies single atoms anchored at the end only test the final possible match.
    /// </summary>
    [Fact]
    public void UsesSimpleSequenceEngineForEndAnchoredAtom()
    {
        var ascii = RegexAutomaton.Compile(
            @"\w$"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        var unicode = RegexAutomaton.Compile(
            @"\w$"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] unicodeHaystack = System.Text.Encoding.UTF8.GetBytes("***Ж");

        Assert.Equal(RegexEngineKind.EndAnchoredAtom, GetEngineKind(ascii));
        Assert.Equal(new RegexMatch(3, 1), ascii.Find("***X"u8));
        Assert.Equal(1, ascii.CountMatches("***X"u8));
        Assert.Equal(1, ascii.SumMatchSpans("***X"u8));
        Assert.Equal(0, ascii.CountMatches("***X"u8, startAt: 4));
        Assert.Null(ascii.Find("***!"u8));
        Assert.Null(ascii.Find("X\n"u8));

        Assert.Equal(RegexEngineKind.EndAnchoredAtom, GetEngineKind(unicode));
        Assert.Equal(new RegexMatch(3, 2), unicode.Find(unicodeHaystack));
        Assert.Equal(2, unicode.SumMatchSpans(unicodeHaystack));
        Assert.Null(unicode.MatchAt(unicodeHaystack, 2));
    }

    /// <summary>
    /// Verifies fixed byte sequences anchored at the end scan from the suffix.
    /// </summary>
    [Fact]
    public void UsesEndAnchoredSequenceEngineForFixedByteSuffixes()
    {
        var automaton = RegexAutomaton.Compile(
            @"A[AB]B[BC]C[CD]D[DE]E[EF]F[FG]G[GH]H[HI]I[IJ]J$"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "prefix\nAABCCCDEEEFGGHHHIJJ"u8.ToArray();

        Assert.Equal(RegexEngineKind.EndAnchoredSequence, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(7, 19), automaton.Find(haystack));
        Assert.Equal(1, automaton.CountMatches(haystack));
        Assert.Equal(19, automaton.SumMatchSpans(haystack));
        Assert.Null(automaton.Find(haystack, startAt: 8));
        Assert.Equal(new RegexMatch(7, 19), automaton.MatchAt(haystack, 7));
        Assert.Null(automaton.MatchAt(haystack, 8));
    }

    /// <summary>
    /// Verifies start-and-end anchored byte sequences avoid the generic one-pass DFA.
    /// </summary>
    [Fact]
    public void UsesEndAnchoredSequenceEngineForWholeHaystackByteSequences()
    {
        var repeatedAlternation = RegexAutomaton.Compile(
            @"^.bc(d|e)*$"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        var literalPrefix = RegexAutomaton.Compile(
            @"^abcdefghijklmnopqrstuvwxyz.*$"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(RegexEngineKind.EndAnchoredSequence, GetEngineKind(repeatedAlternation));
        Assert.Equal(new RegexMatch(0, 17), repeatedAlternation.Find("abcddddddeeeededd"u8));
        Assert.Equal(new RegexMatch(0, 17), repeatedAlternation.MatchAt("abcddddddeeeededd"u8, 0));
        Assert.Equal(1, repeatedAlternation.CountMatches("abcddddddeeeededd"u8));
        Assert.Equal(17, repeatedAlternation.SumMatchSpans("abcddddddeeeededd"u8));
        Assert.Null(repeatedAlternation.Find("xabcddddddeeeededd"u8));
        Assert.Null(repeatedAlternation.Find("abcddddddeeeededd"u8, startAt: 1));
        Assert.Null(repeatedAlternation.MatchAt("abcddddddeeeededd"u8, 1));

        Assert.Equal(RegexEngineKind.EndAnchoredSequence, GetEngineKind(literalPrefix));
        Assert.Equal(new RegexMatch(0, 29), literalPrefix.Find("abcdefghijklmnopqrstuvwxyzXYZ"u8));
        Assert.Equal(29, literalPrefix.SumMatchSpans("abcdefghijklmnopqrstuvwxyzXYZ"u8));
        Assert.Null(literalPrefix.Find("xabcdefghijklmnopqrstuvwxyzXYZ"u8));
    }

    /// <summary>
    /// Verifies repeated single-byte alternations can participate in end-anchored sequence scans.
    /// </summary>
    [Fact]
    public void UsesEndAnchoredSequenceEngineForRepeatedByteAlternationSuffixes()
    {
        var automaton = RegexAutomaton.Compile(
            @".bc(d|e)*$"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xx abcddddddeeeededd"u8.ToArray();

        Assert.Equal(RegexEngineKind.EndAnchoredSequence, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(3, 17), automaton.Find(haystack));
        Assert.Equal(1, automaton.CountMatches(haystack));
        Assert.Equal(17, automaton.SumMatchSpans(haystack));
        Assert.Equal(new RegexMatch(3, 17), automaton.MatchAt(haystack, 3));
        Assert.Null(automaton.MatchAt(haystack, 4));
    }

    /// <summary>
    /// Verifies printable byte runs anchored at the end return the leftmost matching run.
    /// </summary>
    [Fact]
    public void UsesEndAnchoredSequenceEngineForPrintableRunSuffixes()
    {
        var automaton = RegexAutomaton.Compile(
            @"[ -~]*ABCDEFGHIJKLMNOPQRSTUVWXYZ$"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "noise\nxABCDEFGHIJKLMNOPQRSTUVWXYZ"u8.ToArray();

        Assert.Equal(RegexEngineKind.EndAnchoredSequence, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(6, 27), automaton.Find(haystack));
        Assert.Equal(1, automaton.CountMatches(haystack));
        Assert.Equal(27, automaton.SumMatchSpans(haystack));
        Assert.Equal(new RegexMatch(7, 26), automaton.Find(haystack, startAt: 7));
        Assert.Equal(new RegexMatch(7, 26), automaton.MatchAt(haystack, 7));
        Assert.Null(automaton.Find(haystack, startAt: haystack.Length));
    }

    /// <summary>
    /// Verifies delimiter-separated byte runs scan from the delimiter.
    /// </summary>
    [Fact]
    public void UsesSimpleSequenceEngineForDelimitedByteRuns()
    {
        var automaton = RegexAutomaton.Compile(
            @"\w+@\w+"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = "xx a@b no abc@def! z@9"u8.ToArray();

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(3, 3), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(3, 3), automaton.Find(haystack, startAt: 3));
        Assert.Equal(new RegexMatch(10, 7), automaton.Find(haystack, startAt: 4));
        Assert.Equal(new RegexMatch(10, 7), automaton.Find(haystack, startAt: 6));
        Assert.Equal(3, automaton.CountMatches(haystack));
        Assert.Equal(13, automaton.SumMatchSpans(haystack));
        Assert.Equal(2, automaton.CountMatches(haystack, startAt: 6));
        Assert.Equal(10, automaton.SumMatchSpans(haystack, startAt: 6));
        Assert.Null(automaton.Find("abc@"u8));
        Assert.Null(automaton.Find("@abc"u8));
    }

    /// <summary>
    /// Verifies single bounded Unicode scalar runs use direct scalar scanning.
    /// </summary>
    [Fact]
    public void UsesSimpleSequenceEngineForSingleBoundedUnicodePropertyRun()
    {
        var automaton = RegexAutomaton.Compile(
            @"\p{L}{2,3}"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("абвг 12 де");

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 6), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(12, 4), automaton.Find(haystack, startAt: 6));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(10, automaton.SumMatchSpans(haystack));
        Assert.Equal(1, automaton.CountMatches(haystack, startAt: 6));
        Assert.Equal(4, automaton.SumMatchSpans(haystack, startAt: 6));

        byte[] mixedScript = System.Text.Encoding.UTF8.GetBytes("Λδǅ abc");
        Assert.Equal(new RegexMatch(0, 6), automaton.Find(mixedScript));
        Assert.Equal(2, automaton.CountMatches(mixedScript));
        Assert.Equal(9, automaton.SumMatchSpans(mixedScript));

        byte[] longCyrillic = System.Text.Encoding.UTF8.GetBytes(
            new string('ж', 11) + " 12 " + new string('я', 2));
        Assert.Equal(5, automaton.CountMatches(longCyrillic));
        Assert.Equal(26, automaton.SumMatchSpans(longCyrillic));

        var fixedWidth = RegexAutomaton.Compile(
            @"\p{L}{3}"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(fixedWidth));
        Assert.Equal(new RegexMatch(0, 6), fixedWidth.FindEarliest("абвг"u8, startAt: 0));
        Assert.Equal(new RegexMatch(0, 6), fixedWidth.FindAllKindAt("абвг"u8, startAt: 0));
        Assert.Equal([new RegexMatch(0, 6)], fixedWidth.FindOverlappingAt("абвг"u8, startAt: 0));
    }

    /// <summary>
    /// Verifies a single Unicode property atom uses direct scalar scanning.
    /// </summary>
    [Fact]
    public void UsesSimpleSequenceEngineForSingleUnicodePropertyAtom()
    {
        var automaton = RegexAutomaton.Compile(
            @"\p{Sm}"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("a+b ∞ c");

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(1, 1), automaton.Find(haystack));
        Assert.Equal(new RegexMatch(4, 3), automaton.Find(haystack, startAt: 2));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(4, automaton.SumMatchSpans(haystack));
        Assert.Equal(1, automaton.CountMatches(haystack, startAt: 2));
        Assert.Equal(3, automaton.SumMatchSpans(haystack, startAt: 2));
    }

    /// <summary>
    /// Verifies bounded alternations of Unicode scalar classes use direct scalar scanning.
    /// </summary>
    [Fact]
    public void UsesSimpleSequenceEngineForBoundedUnicodePropertyAlternationRun()
    {
        var automaton = RegexAutomaton.Compile(
            @"(?x)(?:\p{Lowercase}|\p{Uppercase}|\p{Titlecase_Letter}|\p{Modifier_Letter}|\p{Other_Letter}){2,3}"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: true);
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("Λδǅ abc");

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 6), automaton.Find(haystack));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(9, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies the simple sequence engine rejects bounded gaps that exceed their maximum.
    /// </summary>
    [Fact]
    public void SimpleSequenceEngineHonorsBoundedRepeatMaximum()
    {
        var automaton = RegexAutomaton.Compile(
            @"A[\s\S]{0,3}Result"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.Equal(new RegexMatch(0, 9), automaton.Find("A12Result"u8));
        Assert.Null(automaton.Find("A1234Result"u8));
    }

    /// <summary>
    /// Verifies repeated simple sub-sequences use direct execution instead of expanded lazy-DFA states.
    /// </summary>
    [Fact]
    public void UsesSimpleSequenceEngineForRepeatedCapitalizedWords()
    {
        var automaton = RegexAutomaton.Compile(
            @"(?:[A-Z][a-z]+\s*){3,5}"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        const string match = "One Two Three Four Five ";
        byte[] haystack = System.Text.Encoding.ASCII.GetBytes("xx " + match + "Six");

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.Equal(RegexPrefilterKind.None, automaton.PrefilterKind);
        Assert.Equal(new RegexMatch(3, match.Length), automaton.Find(haystack));
        Assert.Equal(1, automaton.CountMatches(haystack));
        Assert.Equal(match.Length, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies repeated capitalized-word runs preserve non-overlapping count semantics when they exceed the repeat maximum.
    /// </summary>
    [Fact]
    public void CountsRepeatedCapitalizedWordsByBoundedChunks()
    {
        var automaton = RegexAutomaton.Compile(
            @"(?:[A-Z][a-z]+\s*){2,3}"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        byte[] haystack = System.Text.Encoding.ASCII.GetBytes("One Two Three Four Five ");

        Assert.Equal(RegexEngineKind.SimpleSequence, GetEngineKind(automaton));
        Assert.Equal(2, automaton.CountMatches(haystack));
        Assert.Equal(haystack.Length, automaton.SumMatchSpans(haystack));
    }

    /// <summary>
    /// Verifies a zero DFA cache limit preserves match semantics through fallback.
    /// </summary>
    [Fact]
    public void DfaSizeLimitFallbackPreservesMatches()
    {
        var automaton = RegexAutomaton.Compile(
            "needle"u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            dfaSizeLimit: 0);

        Assert.Equal(new RegexMatch(2, 6), automaton.Find("xxneedle yy"u8));
        Assert.Null(automaton.Find("xxhaystack yy"u8));
    }

    private static RegexEngineKind GetEngineKind(RegexAutomaton automaton)
    {
        return ((RegexMetaEngine)typeof(RegexAutomaton)
            .GetField("engine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(automaton)!)
            .Kind;
    }

    private static bool HasLengthGuard(RegexAutomaton automaton)
    {
        return typeof(RegexAutomaton)
            .GetField("lengthGuard", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(automaton) is not null;
    }

    private static byte[] RebarUnstructuredLogPattern()
    {
        return @"^([^ ]+ [^ ]+) ([DIWEF])[1234]: ((?:(?:\[[^\]]*?\]|\([^\)]*?\)): )*)(.*?) \{([^\}]*)\}$"u8.ToArray();
    }

    private static byte[] BibleReferencePattern()
    {
        return @"(?P<Book>(([1234]|I{1,4})[\t\f\pZ]*)?\pL+\.?)[\t\f\pZ]+(?P<Locations>((?P<Chapter>1?[0-9]?[0-9])(-(?P<ChapterEnd>\d+)|,\s*(?P<ChapterNext>\\d+))*(:\s*(?P<Verse>\d+))?(-(?P<VerseEnd>\d+)|,\s*(?P<VerseNext>\d+))*\s?)+)"u8.ToArray();
    }

    private static byte[] UnicodeGraphemeClusterPattern()
    {
        return System.Text.Encoding.ASCII.GetBytes(
            """
            (?x)
            \p{gcb=CR} \p{gcb=LF}
            |
            \p{gcb=Control}
            |
            \p{gcb=Prepend}*
            (
              (
                (\p{gcb=L}* (\p{gcb=V}+ | \p{gcb=LV} \p{gcb=V}* | \p{gcb=LVT}) \p{gcb=T}*)
                |
                \p{gcb=L}+
                |
                \p{gcb=T}+
              )
              |
              \p{gcb=RI} \p{gcb=RI}
              |
              \p{Extended_Pictographic} (\p{gcb=Extend}* \p{gcb=ZWJ} \p{Extended_Pictographic})*
              |
              [^\p{gcb=Control} \p{gcb=CR} \p{gcb=LF}]
            )
            [\p{gcb=Extend} \p{gcb=ZWJ} \p{gcb=SpacingMark}]*
            |
            \p{Any}
            """);
    }

    private static byte[] BuildLargeLiteralAlternation(string first, string second)
    {
        string[] literals = new string[130];
        literals[0] = first;
        literals[1] = second;
        for (int index = 2; index < literals.Length; index++)
        {
            literals[index] = $"w{index:D3}token";
        }

        return System.Text.Encoding.ASCII.GetBytes(string.Join('|', literals));
    }

    private static byte[] BuildLargeAhoLiteralAlternation(params string[] orderedPrefixes)
    {
        string[] literals = new string[130];
        for (int index = 0; index < orderedPrefixes.Length; index++)
        {
            literals[index] = orderedPrefixes[index];
        }

        for (int index = orderedPrefixes.Length; index < literals.Length; index++)
        {
            literals[index] = $"z{index:D3}token";
        }

        return System.Text.Encoding.ASCII.GetBytes(string.Join('|', literals));
    }

    private static void AssertGroupText(RegexCaptures captures, byte[] haystack, int groupIndex, string expected)
    {
        RegexMatch? group = captures.GetGroup(groupIndex);

        Assert.NotNull(group);
        Assert.Equal(
            expected,
            System.Text.Encoding.ASCII.GetString(haystack.AsSpan(group.Value.Start, group.Value.Length)));
    }

    private static void AssertGroupUtf8Text(RegexCaptures captures, byte[] haystack, int groupIndex, string expected)
    {
        RegexMatch? group = captures.GetGroup(groupIndex);

        Assert.NotNull(group);
        Assert.Equal(
            expected,
            System.Text.Encoding.UTF8.GetString(haystack.AsSpan(group.Value.Start, group.Value.Length)));
    }

    private static RegexNfa CompileNfa(ReadOnlySpan<byte> pattern)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(pattern);
        return RegexNfaCompiler.Compile(
            tree.Root,
            new RegexCompileOptions(caseInsensitive: false, swapGreed: false, multiLine: false, dotMatchesNewline: false));
    }
}
