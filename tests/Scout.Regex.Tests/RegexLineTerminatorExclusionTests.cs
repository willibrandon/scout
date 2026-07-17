namespace Scout;

/// <summary>
/// Verifies parsed line-oriented regex compilation excludes configured record terminators.
/// </summary>
public sealed class RegexLineTerminatorExclusionTests
{
    /// <summary>
    /// Verifies line-oriented dot-all matching cannot consume a line feed.
    /// </summary>
    [Fact]
    public void DotAllExcludesLineFeed()
    {
        RegexAutomaton automaton = Compile("(?s:.)+"u8);

        Assert.Equal(new RegexMatch(0, 1), automaton.Find("a\nb"u8));
        Assert.Equal(2, automaton.CountMatches("a\nb"u8));
    }

    /// <summary>
    /// Verifies scoped dot-all flags cannot override structural record-terminator exclusion.
    /// </summary>
    [Fact]
    public void ScopedDotAllCannotConsumeLineFeed()
    {
        RegexAutomaton automaton = Compile("(?s:a.b)(?-s:c.d)"u8);

        Assert.Equal(new RegexMatch(0, 6), automaton.MatchAt("aXbcYd"u8, 0));
        Assert.Null(automaton.MatchAt("a\nbcYd"u8, 0));
        Assert.Null(automaton.MatchAt("aXbc\nd"u8, 0));
    }

    /// <summary>
    /// Verifies every class kind excludes a line feed according to the structural compile option.
    /// </summary>
    [Theory]
    [InlineData("[a\\n]+")]
    [InlineData("[\\s\\S]+")]
    [InlineData("\\s+")]
    public void CharacterClassesExcludeLineFeed(string pattern)
    {
        RegexAutomaton automaton = Compile(System.Text.Encoding.UTF8.GetBytes(pattern));

        Assert.Null(automaton.MatchAt("\n"u8, 0));
    }

    /// <summary>
    /// Verifies ASCII projection preserves exclusion for literals, dots, and character classes.
    /// </summary>
    /// <param name="pattern">The pattern projected to ASCII byte semantics.</param>
    [Theory]
    [InlineData("a\\nb")]
    [InlineData("a.b")]
    [InlineData("a[\\s\\S]b")]
    public void AsciiProjectionPreservesLineTerminatorExclusion(string pattern)
    {
        byte[] patternBytes = System.Text.Encoding.UTF8.GetBytes(pattern);
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(patternBytes);
        RegexCompileOptions options = CreateOptions();

        Assert.True(RegexAsciiFastPath.TryCompileNfa(patternBytes, tree.Root, options, out RegexNfa? nfa));
        Assert.NotNull(nfa);
        Assert.False(new PikeVm(nfa).TryMatchAt("a\nb"u8, start: 0, out _));
    }

    /// <summary>
    /// Verifies an unanchored ASCII projection cannot consume across an excluded record boundary.
    /// </summary>
    [Fact]
    public void UnanchoredAsciiProjectionPreservesLineTerminatorExclusion()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("(?s:a(?:.+)+b)"u8);
        byte[] haystack = Enumerable.Repeat((byte)'x', 8_192).ToArray();
        "a\nb"u8.CopyTo(haystack.AsSpan(4_096));
        var automaton = RegexAutomaton.CompileParsed(
            tree,
            CreateOptions(),
            compilePrefilter: false);

        Assert.Null(automaton.Find(haystack));
    }

    /// <summary>
    /// Verifies NUL-data compilation excludes NUL from otherwise dot-all atoms.
    /// </summary>
    [Fact]
    public void DotAllExcludesNulTerminator()
    {
        RegexAutomaton automaton = Compile("(?s:.)+"u8, excludedLineTerminator: 0);

        Assert.Equal(new RegexMatch(0, 1), automaton.Find(new byte[] { (byte)'a', 0, (byte)'b' }));
        Assert.Equal(2, automaton.CountMatches(new byte[] { (byte)'a', 0, (byte)'b' }));
    }

    /// <summary>
    /// Verifies CRLF compilation excludes both bytes in the record-terminator family.
    /// </summary>
    [Fact]
    public void DotAllExcludesCrLfTerminatorFamily()
    {
        RegexAutomaton automaton = Compile("(?s:.)+"u8, crlf: true);

        Assert.Equal(new RegexMatch(0, 1), automaton.Find("a\r\nb"u8));
        Assert.Equal(2, automaton.CountMatches("a\r\nb"u8));
    }

    /// <summary>
    /// Verifies the generic capture VM observes line-terminator exclusion.
    /// </summary>
    [Fact]
    public void CaptureMatchingExcludesLineFeed()
    {
        RegexAutomaton automaton = Compile("((?s:.)+)"u8);

        RegexCaptures? captures = automaton.FindCaptures("a\nb"u8);

        Assert.NotNull(captures);
        Assert.Equal(new RegexMatch(0, 1), captures.Match);
        Assert.Equal(new RegexMatch(0, 1), captures.GetGroup(1));
    }

    /// <summary>
    /// Verifies an explicit literal record terminator is identified before NFA compilation.
    /// </summary>
    [Fact]
    public void AnalysisIdentifiesExplicitLiteralTerminator()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("foo\\nbar"u8);
        RegexCompileOptions options = CreateOptions();

        RegexLineTerminatorAnalysisResult result = RegexLineTerminatorAnalysis.Analyze(
            tree.Root,
            options,
            out int position);

        Assert.Equal(RegexLineTerminatorAnalysisResult.ExplicitLiteral, result);
        Assert.Equal(3, position);
        Assert.Throws<RegexLineTerminatorException>(() => RegexAutomaton.CompileParsed(tree, options));
    }

    /// <summary>
    /// Verifies escaped spellings of configured record terminators are rejected after parsing.
    /// </summary>
    /// <param name="pattern">The escaped literal pattern.</param>
    /// <param name="crlf">Whether CRLF exclusion is enabled.</param>
    /// <param name="excludedLineTerminator">The configured record byte.</param>
    [Theory]
    [InlineData("\\x0A", false, 10)]
    [InlineData("\\u{A}", false, 10)]
    [InlineData("\\x00", false, 0)]
    [InlineData("\\x0D", true, 10)]
    [InlineData("\\x0A", true, 10)]
    public void AnalysisRejectsEscapedLiteralTerminator(
        string pattern,
        bool crlf,
        int excludedLineTerminator)
    {
        byte[] patternBytes = System.Text.Encoding.UTF8.GetBytes(pattern);
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(patternBytes);
        RegexCompileOptions options = CreateOptions(crlf, (byte)excludedLineTerminator);

        Assert.Equal(
            RegexLineTerminatorAnalysisResult.ExplicitLiteral,
            RegexLineTerminatorAnalysis.Analyze(tree.Root, options, out _));
        Assert.Throws<RegexLineTerminatorException>(() => RegexAutomaton.CompileParsed(tree, options));
    }

    /// <summary>
    /// Verifies inline CRLF flags do not change the record-terminator family selected by the caller.
    /// </summary>
    [Fact]
    public void InlineCrlfFlagsDoNotChangeRecordTerminatorExclusion()
    {
        RegexSyntaxTree crlfTree = RegexSyntaxParser.Parse("(?-R:\\r)"u8);
        RegexCompileOptions crlfOptions = CreateOptions(crlf: true);
        RegexSyntaxTree lineFeedTree = RegexSyntaxParser.Parse("(?R:\\r)"u8);
        RegexCompileOptions lineFeedOptions = CreateOptions();

        Assert.Equal(
            RegexLineTerminatorAnalysisResult.ExplicitLiteral,
            RegexLineTerminatorAnalysis.Analyze(crlfTree.Root, crlfOptions, out _));
        Assert.Equal(
            RegexLineTerminatorAnalysisResult.None,
            RegexLineTerminatorAnalysis.Analyze(lineFeedTree.Root, lineFeedOptions, out _));

        var automaton = RegexAutomaton.CompileParsed(lineFeedTree, lineFeedOptions);
        Assert.Equal(new RegexMatch(0, 1), automaton.MatchAt("\r"u8, 0));
    }

    /// <summary>
    /// Verifies a class emptied by record-terminator exclusion is rejected.
    /// </summary>
    [Fact]
    public void AnalysisIdentifiesClassEmptiedByExclusion()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("[\\n]"u8);
        RegexCompileOptions options = CreateOptions();

        RegexLineTerminatorAnalysisResult result = RegexLineTerminatorAnalysis.Analyze(
            tree.Root,
            options,
            out int position);

        Assert.Equal(RegexLineTerminatorAnalysisResult.EmptyAtom, result);
        Assert.Equal(0, position);
        Assert.Throws<RegexLineTerminatorException>(() => RegexAutomaton.CompileParsed(tree, options));
    }

    /// <summary>
    /// Verifies a class retaining another member remains valid after exclusion.
    /// </summary>
    [Fact]
    public void AnalysisAllowsClassRetainingNonTerminatorMember()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("[a\\n]+"u8);
        RegexCompileOptions options = CreateOptions();

        Assert.Equal(
            RegexLineTerminatorAnalysisResult.None,
            RegexLineTerminatorAnalysis.Analyze(tree.Root, options, out int position));
        Assert.Equal(-1, position);

        var automaton = RegexAutomaton.CompileParsed(tree, options);
        Assert.Equal(new RegexMatch(0, 1), automaton.Find("a\na"u8));
    }

    /// <summary>
    /// Verifies a scalar class without ASCII members remains valid after record-terminator exclusion.
    /// </summary>
    [Fact]
    public void AnalysisAllowsUnicodeOnlyClass()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"\p{Greek}"u8);
        RegexCompileOptions options = CreateOptions();

        Assert.Equal(
            RegexLineTerminatorAnalysisResult.None,
            RegexLineTerminatorAnalysis.Analyze(tree.Root, options, out int position));
        Assert.Equal(-1, position);

        var automaton = RegexAutomaton.CompileParsed(tree, options);
        Assert.Equal(new RegexMatch(0, 2), automaton.Find("π\n"u8));
    }

    /// <summary>
    /// Verifies line-oriented compilation retains the exact pure-literal specialization after
    /// record-terminator validation.
    /// </summary>
    [Fact]
    public void LineOrientedPureLiteralUsesLiteralSet()
    {
        RegexAutomaton automaton = Compile("literal"u8);

        Assert.Equal(RegexEngineKind.LiteralSet, automaton.EngineKind);
        Assert.Equal(new RegexMatch(2, 7), automaton.Find("--literal--"u8));
    }

    /// <summary>
    /// Verifies the public compile surface retains its existing dot-all behavior.
    /// </summary>
    [Fact]
    public void PublicCompileBehaviorIsUnchanged()
    {
        var automaton = RegexAutomaton.Compile(
            "."u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: true);

        Assert.Equal(new RegexMatch(0, 1), automaton.Find("\n"u8));
    }

    private static RegexAutomaton Compile(
        ReadOnlySpan<byte> pattern,
        bool crlf = false,
        byte excludedLineTerminator = (byte)'\n')
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(pattern);
        return RegexAutomaton.CompileParsed(tree, CreateOptions(crlf, excludedLineTerminator));
    }

    private static RegexCompileOptions CreateOptions(
        bool crlf = false,
        byte excludedLineTerminator = (byte)'\n')
    {
        return new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: true,
            dotMatchesNewline: false,
            crlf,
            lineTerminator: (byte)'\n',
            utf8: true,
            unicodeClasses: true,
            specializationMode: RegexSpecializationMode.Default,
            excludeLineTerminators: true,
            excludedLineTerminator: excludedLineTerminator);
    }
}
