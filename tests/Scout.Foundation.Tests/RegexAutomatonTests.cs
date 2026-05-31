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

        Assert.True(longFirst.HasValue);
        Assert.True(shortFirst.HasValue);
        Assert.Equal(new RegexMatch(1, 2), longFirst.Value);
        Assert.Equal(new RegexMatch(1, 1), shortFirst.Value);
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
    /// Verifies Perl and bracket classes honor ripgrep's multiline line-terminator policy.
    /// </summary>
    [Fact]
    public void ClassesHonorMultilineLineTerminatorPolicy()
    {
        Assert.Null(RegexAutomaton.Compile(@"\s"u8).Find("\n"u8));
        Assert.Null(RegexAutomaton.Compile(@"[\s]"u8).Find("\n"u8));
        Assert.Null(RegexAutomaton.Compile(@"\W"u8).Find("\n"u8));

        Assert.Equal(new RegexMatch(0, 1), RegexAutomaton.Compile(@"\s"u8, multiLine: true, dotMatchesNewline: false).Find("\n"u8));
        Assert.Equal(new RegexMatch(0, 1), RegexAutomaton.Compile(@"[\s]"u8, multiLine: true, dotMatchesNewline: false).Find("\n"u8));
        Assert.Equal(new RegexMatch(0, 1), RegexAutomaton.Compile(@"\W"u8, multiLine: true, dotMatchesNewline: false).Find("\n"u8));
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
        Assert.Equal(new RegexMatch(2, 0), RegexAutomaton.Compile(@"\B"u8).Find(delta, startAt: 1));
    }

    /// <summary>
    /// Verifies byte mode preserves byte-by-byte matching.
    /// </summary>
    [Fact]
    public void ByteModeCanSplitCodepoints()
    {
        byte[] poop = [0xF0, 0x9F, 0x92, 0xA9];

        Assert.Equal(new RegexMatch(0, 1), RegexAutomaton.Compile("."u8, caseInsensitive: false, multiLine: false, dotMatchesNewline: false, utf8: false).Find(poop));
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

    private static RegexNfa CompileNfa(ReadOnlySpan<byte> pattern)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(pattern);
        return RegexNfaCompiler.Compile(
            tree.Root,
            new RegexCompileOptions(caseInsensitive: false, swapGreed: false, multiLine: false, dotMatchesNewline: false));
    }
}
