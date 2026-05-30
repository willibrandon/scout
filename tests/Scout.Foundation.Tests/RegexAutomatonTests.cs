namespace Scout;

/// <summary>
/// Verifies the Scout byte-oriented regex automaton.
/// </summary>
public sealed class RegexAutomatonTests
{
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
    /// Verifies the regex-crate any-byte Unicode class matches across line terminators.
    /// </summary>
    [Fact]
    public void MatchesAnyUnicodeClass()
    {
        Assert.Equal(new RegexMatch(0, 3), RegexAutomaton.Compile(@"a\p{Any}b"u8).Find("a\nb"u8));
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
}
