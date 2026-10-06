namespace Scout;

/// <summary>
/// Verifies parsed line-oriented regex compilation excludes configured record terminators.
/// </summary>
[TestClass]
public sealed class RegexLineTerminatorExclusionTests
{
    /// <summary>
    /// Verifies line-oriented dot-all matching cannot consume a line feed.
    /// </summary>
    [TestMethod]
    public void DotAllExcludesLineFeed()
    {
        RegexAutomaton automaton = Compile("(?s:.)+"u8);

        Assert.AreEqual(new RegexMatch(0, 1), automaton.Find("a\nb"u8));
        Assert.AreEqual(2, automaton.CountMatches("a\nb"u8));
    }

    /// <summary>
    /// Verifies scoped dot-all flags cannot override structural record-terminator exclusion.
    /// </summary>
    [TestMethod]
    public void ScopedDotAllCannotConsumeLineFeed()
    {
        RegexAutomaton automaton = Compile("(?s:a.b)(?-s:c.d)"u8);

        Assert.AreEqual(new RegexMatch(0, 6), automaton.MatchAt("aXbcYd"u8, 0));
        Assert.IsNull(automaton.MatchAt("a\nbcYd"u8, 0));
        Assert.IsNull(automaton.MatchAt("aXbc\nd"u8, 0));
    }

    /// <summary>
    /// Verifies every class kind excludes a line feed according to the structural compile option.
    /// </summary>
    [TestMethod]
    [DataRow("[a\\n]+")]
    [DataRow("[\\s\\S]+")]
    [DataRow("\\s+")]
    public void CharacterClassesExcludeLineFeed(string pattern)
    {
        RegexAutomaton automaton = Compile(System.Text.Encoding.UTF8.GetBytes(pattern));

        Assert.IsNull(automaton.MatchAt("\n"u8, 0));
    }

    /// <summary>
    /// Verifies ASCII projection preserves exclusion for literals, dots, and character classes.
    /// </summary>
    /// <param name="pattern">The pattern projected to ASCII byte semantics.</param>
    [TestMethod]
    [DataRow("a\\nb")]
    [DataRow("a.b")]
    [DataRow("a[\\s\\S]b")]
    public void AsciiProjectionPreservesLineTerminatorExclusion(string pattern)
    {
        byte[] patternBytes = System.Text.Encoding.UTF8.GetBytes(pattern);
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(patternBytes);
        RegexCompileOptions options = CreateOptions();

        Assert.IsTrue(RegexAsciiFastPath.TryCompileNfa(patternBytes, tree.Root, options, out RegexNfa? nfa));
        Assert.IsNotNull(nfa);
        Assert.IsFalse(new PikeVm(nfa).TryMatchAt("a\nb"u8, start: 0, out _));
    }

    /// <summary>
    /// Verifies an unanchored ASCII projection cannot consume across an excluded record boundary.
    /// </summary>
    [TestMethod]
    public void UnanchoredAsciiProjectionPreservesLineTerminatorExclusion()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("(?s:a(?:.+)+b)"u8);
        byte[] haystack = Enumerable.Repeat((byte)'x', 8_192).ToArray();
        "a\nb"u8.CopyTo(haystack.AsSpan(4_096));
        var automaton = RegexAutomaton.CompileParsed(
            tree,
            CreateOptions(),
            compilePrefilter: false);

        Assert.IsNull(automaton.Find(haystack));
    }

    /// <summary>
    /// Verifies NUL-data compilation excludes NUL from otherwise dot-all atoms.
    /// </summary>
    [TestMethod]
    public void DotAllExcludesNulTerminator()
    {
        RegexAutomaton automaton = Compile("(?s:.)+"u8, excludedLineTerminator: 0);

        Assert.AreEqual(new RegexMatch(0, 1), automaton.Find(new byte[] { (byte)'a', 0, (byte)'b' }));
        Assert.AreEqual(2, automaton.CountMatches(new byte[] { (byte)'a', 0, (byte)'b' }));
    }

    /// <summary>
    /// Verifies CRLF compilation excludes both bytes in the record-terminator family.
    /// </summary>
    [TestMethod]
    public void DotAllExcludesCrLfTerminatorFamily()
    {
        RegexAutomaton automaton = Compile("(?s:.)+"u8, crlf: true);

        Assert.AreEqual(new RegexMatch(0, 1), automaton.Find("a\r\nb"u8));
        Assert.AreEqual(2, automaton.CountMatches("a\r\nb"u8));
    }

    /// <summary>
    /// Verifies the generic capture VM observes line-terminator exclusion.
    /// </summary>
    [TestMethod]
    public void CaptureMatchingExcludesLineFeed()
    {
        RegexAutomaton automaton = Compile("((?s:.)+)"u8);

        RegexCaptures? captures = automaton.FindCaptures("a\nb"u8);

        Assert.IsNotNull(captures);
        Assert.AreEqual(new RegexMatch(0, 1), captures.Match);
        Assert.AreEqual(new RegexMatch(0, 1), captures.GetGroup(1));
    }

    /// <summary>
    /// Verifies an explicit literal record terminator is identified before NFA compilation.
    /// </summary>
    [TestMethod]
    public void AnalysisIdentifiesExplicitLiteralTerminator()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("foo\\nbar"u8);
        RegexCompileOptions options = CreateOptions();

        RegexLineTerminatorAnalysisResult result = RegexLineTerminatorAnalysis.Analyze(
            tree.Root,
            options,
            out int position);

        Assert.AreEqual(RegexLineTerminatorAnalysisResult.ExplicitLiteral, result);
        Assert.AreEqual(3, position);
        Assert.ThrowsExactly<RegexLineTerminatorException>(() => RegexAutomaton.CompileParsed(tree, options));
    }

    /// <summary>
    /// Verifies escaped spellings of configured record terminators are rejected after parsing.
    /// </summary>
    /// <param name="pattern">The escaped literal pattern.</param>
    /// <param name="crlf">Whether CRLF exclusion is enabled.</param>
    /// <param name="excludedLineTerminator">The configured record byte.</param>
    [TestMethod]
    [DataRow("\\x0A", false, 10)]
    [DataRow("\\u{A}", false, 10)]
    [DataRow("\\x00", false, 0)]
    [DataRow("\\x0D", true, 10)]
    [DataRow("\\x0A", true, 10)]
    public void AnalysisRejectsEscapedLiteralTerminator(
        string pattern,
        bool crlf,
        int excludedLineTerminator)
    {
        byte[] patternBytes = System.Text.Encoding.UTF8.GetBytes(pattern);
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(patternBytes);
        RegexCompileOptions options = CreateOptions(crlf, (byte)excludedLineTerminator);

        Assert.AreEqual(
            RegexLineTerminatorAnalysisResult.ExplicitLiteral,
            RegexLineTerminatorAnalysis.Analyze(tree.Root, options, out _));
        Assert.ThrowsExactly<RegexLineTerminatorException>(() => RegexAutomaton.CompileParsed(tree, options));
    }

    /// <summary>
    /// Verifies inline CRLF flags do not change the record-terminator family selected by the caller.
    /// </summary>
    [TestMethod]
    public void InlineCrlfFlagsDoNotChangeRecordTerminatorExclusion()
    {
        RegexSyntaxTree crlfTree = RegexSyntaxParser.Parse("(?-R:\\r)"u8);
        RegexCompileOptions crlfOptions = CreateOptions(crlf: true);
        RegexSyntaxTree lineFeedTree = RegexSyntaxParser.Parse("(?R:\\r)"u8);
        RegexCompileOptions lineFeedOptions = CreateOptions();

        Assert.AreEqual(
            RegexLineTerminatorAnalysisResult.ExplicitLiteral,
            RegexLineTerminatorAnalysis.Analyze(crlfTree.Root, crlfOptions, out _));
        Assert.AreEqual(
            RegexLineTerminatorAnalysisResult.None,
            RegexLineTerminatorAnalysis.Analyze(lineFeedTree.Root, lineFeedOptions, out _));

        var automaton = RegexAutomaton.CompileParsed(lineFeedTree, lineFeedOptions);
        Assert.AreEqual(new RegexMatch(0, 1), automaton.MatchAt("\r"u8, 0));
    }

    /// <summary>
    /// Verifies a class emptied by record-terminator exclusion is rejected.
    /// </summary>
    [TestMethod]
    public void AnalysisIdentifiesClassEmptiedByExclusion()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("[\\n]"u8);
        RegexCompileOptions options = CreateOptions();

        RegexLineTerminatorAnalysisResult result = RegexLineTerminatorAnalysis.Analyze(
            tree.Root,
            options,
            out int position);

        Assert.AreEqual(RegexLineTerminatorAnalysisResult.EmptyAtom, result);
        Assert.AreEqual(0, position);
        Assert.ThrowsExactly<RegexLineTerminatorException>(() => RegexAutomaton.CompileParsed(tree, options));
    }

    /// <summary>
    /// Verifies a class retaining another member remains valid after exclusion.
    /// </summary>
    [TestMethod]
    public void AnalysisAllowsClassRetainingNonTerminatorMember()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("[a\\n]+"u8);
        RegexCompileOptions options = CreateOptions();

        Assert.AreEqual(
            RegexLineTerminatorAnalysisResult.None,
            RegexLineTerminatorAnalysis.Analyze(tree.Root, options, out int position));
        Assert.AreEqual(-1, position);

        var automaton = RegexAutomaton.CompileParsed(tree, options);
        Assert.AreEqual(new RegexMatch(0, 1), automaton.Find("a\na"u8));
    }

    /// <summary>
    /// Verifies a scalar class without ASCII members remains valid after record-terminator exclusion.
    /// </summary>
    [TestMethod]
    public void AnalysisAllowsUnicodeOnlyClass()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"\p{Greek}"u8);
        RegexCompileOptions options = CreateOptions();

        Assert.AreEqual(
            RegexLineTerminatorAnalysisResult.None,
            RegexLineTerminatorAnalysis.Analyze(tree.Root, options, out int position));
        Assert.AreEqual(-1, position);

        var automaton = RegexAutomaton.CompileParsed(tree, options);
        Assert.AreEqual(new RegexMatch(0, 2), automaton.Find("π\n"u8));
    }

    /// <summary>
    /// Verifies line-oriented compilation retains the exact pure-literal specialization after
    /// record-terminator validation.
    /// </summary>
    [TestMethod]
    public void LineOrientedPureLiteralUsesLiteralSet()
    {
        RegexAutomaton automaton = Compile("literal"u8);

        Assert.AreEqual(RegexEngineKind.LiteralSet, automaton.EngineKind);
        Assert.AreEqual(new RegexMatch(2, 7), automaton.Find("--literal--"u8));
    }

    /// <summary>
    /// Verifies the public compile surface retains its existing dot-all behavior.
    /// </summary>
    [TestMethod]
    public void PublicCompileBehaviorIsUnchanged()
    {
        var automaton = RegexAutomaton.Compile(
            "."u8,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: true);

        Assert.AreEqual(new RegexMatch(0, 1), automaton.Find("\n"u8));
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
