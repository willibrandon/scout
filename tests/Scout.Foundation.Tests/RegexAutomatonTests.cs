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
        RegexMatch? shorterFirst = shorterFirstAutomaton.Find("xxfoobar"u8);
        RegexMatch? longerFirst = longerFirstAutomaton.Find("xxfoobar"u8);
        RegexMatch? caseInsensitive = caseInsensitiveAutomaton.Find("xxjohn watson"u8);

        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(shorterFirstAutomaton));
        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(longerFirstAutomaton));
        Assert.Equal(RegexEngineKind.LiteralSet, GetEngineKind(caseInsensitiveAutomaton));
        Assert.Equal(new RegexMatch(2, 3), shorterFirst);
        Assert.Equal(new RegexMatch(2, 6), longerFirst);
        Assert.Equal(new RegexMatch(2, 11), caseInsensitive);
        Assert.Equal(2, shorterFirstAutomaton.CountMatches("foo foobar"u8));
        Assert.Equal(6, shorterFirstAutomaton.SumMatchSpans("foo foobar"u8));
        Assert.Equal(1, shorterFirstAutomaton.CountMatches("foo foobar"u8, startAt: 4));
        Assert.Equal(3, shorterFirstAutomaton.SumMatchSpans("foo foobar"u8, startAt: 4));
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
    /// Verifies anchored delimiter-separated capture patterns report field captures.
    /// </summary>
    [Fact]
    public void DelimitedCaptureEngineReportsFieldCaptures()
    {
        var automaton = RegexAutomaton.Compile("^([A-Z0-9]+);([^;]*);([YN])$"u8);

        RegexCaptures? captures = automaton.FindCaptures("0041;;Y"u8);

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

        Assert.True(automaton.UsesStructuredLogCaptureEngine);
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
    /// Verifies Unicode-aware case-insensitive literals can use a required-literal prefilter.
    /// </summary>
    [Fact]
    public void UsesRequiredLiteralPrefilterForUnicodeCaseInsensitiveLiteral()
    {
        byte[] pattern = System.Text.Encoding.UTF8.GetBytes("Шерлок Холмс");
        byte[] haystack = System.Text.Encoding.UTF8.GetBytes("xxшерлок холмс yy");
        var automaton = RegexAutomaton.Compile(
            pattern,
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false,
            unicodeClasses: true);

        Assert.Equal(RegexPrefilterKind.RequiredLiteral, automaton.PrefilterKind);
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

        Assert.Equal([new RegexMatch(0, 1), new RegexMatch(0, 2), new RegexMatch(0, 3)], matches);
        Assert.Equal([new RegexMatch(2, 0), new RegexMatch(2, 1)], emptyMatches);
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
        Assert.Equal(new RegexMatch(0, 2), RegexAutomaton.Compile(@"\P{N}+"u8).Find("abⅠ"u8));
        Assert.Null(RegexAutomaton.Compile(@"\p{Lu}"u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, unicodeClasses: false).Find("Λ"u8));
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
    /// Verifies Unicode PikeVM patterns can use an ASCII DFA mirror without losing Unicode fallback semantics.
    /// </summary>
    [Fact]
    public void UnicodePikeVmAsciiFastPathPreservesFallbackSemantics()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("[a-z]+0"u8);
        var options = new RegexCompileOptions(
            caseInsensitive: true,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false);
        RegexNfa unicodeNfa = RegexNfaCompiler.Compile(tree.Root, options);

        Assert.False(RegexDfaOperations.CanCompile(unicodeNfa));
        Assert.True(RegexAsciiFastPath.TryCompileNfa("[a-z]+0"u8, tree.Root, options, out RegexNfa? asciiNfa));
        Assert.NotNull(asciiNfa);
        Assert.True(RegexDfaOperations.CanCompile(asciiNfa));

        var automaton = RegexAutomaton.Compile(
            "[a-z]+0"u8,
            caseInsensitive: true,
            multiLine: false,
            dotMatchesNewline: false);
        byte[] kelvinHaystack = System.Text.Encoding.UTF8.GetBytes("11\u212A0");

        Assert.Equal(new RegexMatch(2, 2), automaton.Find("11K0"u8));
        Assert.Equal(new RegexMatch(2, 4), automaton.Find(kelvinHaystack));
        Assert.Null(automaton.Find("1120"u8));
    }

    /// <summary>
    /// Verifies the ASCII mirror can accept ASCII-only matches without losing Unicode extensions at non-ASCII boundaries.
    /// </summary>
    [Fact]
    public void UnicodePikeVmAsciiFastPathFallsBackAtNonAsciiBoundary()
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

        Assert.Equal(new RegexMatch(0, 4), RegexAutomaton.Compile("."u8).Find(poop));
        Assert.Equal(new RegexMatch(0, 4), RegexAutomaton.Compile("[^a]"u8).Find(poop));
        Assert.Equal(new RegexMatch(4, 0), RegexAutomaton.Compile(""u8).Find(poop, startAt: 1));
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

        Assert.Equal(new RegexMatch(0, 1), RegexAutomaton.Compile("."u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, utf8: false, unicodeClasses: false).Find(poop));
        Assert.Equal(new RegexMatch(1, 0), RegexAutomaton.Compile(""u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, utf8: false).Find(poop, startAt: 1));
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

    private static byte[] RebarUnstructuredLogPattern()
    {
        return @"^([^ ]+ [^ ]+) ([DIWEF])[1234]: ((?:(?:\[[^\]]*?\]|\([^\)]*?\)): )*)(.*?) \{([^\}]*)\}$"u8.ToArray();
    }

    private static void AssertGroupText(RegexCaptures captures, byte[] haystack, int groupIndex, string expected)
    {
        RegexMatch? group = captures.GetGroup(groupIndex);

        Assert.NotNull(group);
        Assert.Equal(
            expected,
            System.Text.Encoding.ASCII.GetString(haystack.AsSpan(group.Value.Start, group.Value.Length)));
    }

    private static RegexNfa CompileNfa(ReadOnlySpan<byte> pattern)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(pattern);
        return RegexNfaCompiler.Compile(
            tree.Root,
            new RegexCompileOptions(caseInsensitive: false, swapGreed: false, multiLine: false, dotMatchesNewline: false));
    }
}
