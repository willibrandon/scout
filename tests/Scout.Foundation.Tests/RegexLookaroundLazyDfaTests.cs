namespace Scout;

/// <summary>
/// Verifies bounded lazy determinization of byte-safe look-around assertions.
/// </summary>
[TestClass]
public sealed class RegexLookaroundLazyDfaTests
{
    private const ulong GenerousDfaSizeLimit = 16UL * 1024UL * 1024UL;

    /// <summary>
    /// Verifies paired forward and reverse searches preserve PikeVM results at every start offset.
    /// </summary>
    /// <param name="pattern">The contextual pattern to compare.</param>
    /// <param name="haystackText">The input text to search.</param>
    /// <param name="multiLine">Whether line anchors are enabled.</param>
    /// <param name="crlf">Whether CRLF is treated as one line terminator.</param>
    [TestMethod]
    [DataRow("foo$", "foo\nfoo\r\nfoo\rbar\nfoo xfoo", true, false)]
    [DataRow("foo$", "foo\nfoo\r\nfoo\rbar\nfoo xfoo", true, true)]
    [DataRow("^foo", "xfoo\nfoo\r\nfoo", true, true)]
    [DataRow("\\Afoo", "foo foo", false, false)]
    [DataRow("foo\\z", "foo foo", false, false)]
    [DataRow("\\bfoo\\b", "xfoo foo foo!", false, false)]
    [DataRow("\\Bfoo\\B", "xfoox foo", false, false)]
    [DataRow("\\<foo\\>", "xfoo foo foo!", false, false)]
    [DataRow("(?i)[a-z]{0,50}?key[a-z]{0,20}=([a-z]{10,20})(?:\"|$)", "PublicKeyToken=abcdefghijklmnop\"", false, false)]
    [DataRow("(?i)[a-z]{0,50}?key[a-z]{0,20}=([a-z]{10,20})\"", "PublicKeyToken=abcdefghijklmnop\"", false, false)]
    [DataRow("(?:|Public)Key", "PublicKey Key", false, false)]
    [DataRow("(?i)[a-z]{0,50}key", "PublicKey Key", false, false)]
    public void PairedSearchMatchesPikeVmAtEveryStartOffset(
        string pattern,
        string haystackText,
        bool multiLine,
        bool crlf)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(System.Text.Encoding.ASCII.GetBytes(pattern));
        RegexCompileOptions options = CreateOptions(multiLine, crlf);
        RegexNfa nfa = RegexNfaCompiler.Compile(tree.Root, options);
        var fallback = RegexMetaEngine.Compile(
            nfa,
            prefilter: null,
            dfaSizeLimit: 0);
        Assert.IsTrue(RegexUnanchoredLazyDfa.TryCreate(
            nfa,
            tree.Root,
            options,
            GenerousDfaSizeLimit,
            out RegexUnanchoredLazyDfa? dfa));
        byte[] haystack = System.Text.Encoding.ASCII.GetBytes(haystackText);

        for (int startAt = 0; startAt <= haystack.Length; startAt++)
        {
            RegexMatch? expected = fallback.Find(haystack, startAt);
            bool found = dfa!.TryFind(haystack, startAt, out RegexMatch actual, out bool gaveUp);

            Assert.IsFalse(gaveUp);
            Assert.AreEqual(expected.HasValue, found);
            Assert.AreEqual(expected ?? default, actual);
        }
    }

    /// <summary>
    /// Verifies cache exhaustion is reported so callers can retain authoritative fallback behavior.
    /// </summary>
    [TestMethod]
    public void TransitionBudgetExhaustionReportsGiveUp()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("foo$"u8);
        RegexCompileOptions options = CreateOptions(multiLine: false, crlf: false);
        RegexNfa nfa = RegexNfaCompiler.CompileUnanchored(tree.Root, options);
        Assert.IsTrue(RegexLookaroundLazyDfa.TryCreate(
            nfa,
            dfaSizeLimit: 96,
            out RegexLookaroundLazyDfa? dfa));

        Assert.IsFalse(dfa!.TryFindEnd(
            "xxfoo"u8,
            start: 0,
            reachabilityCache: null,
            out int end,
            out bool gaveUp));
        Assert.AreEqual(-1, end);
        Assert.IsTrue(gaveUp);
    }

    /// <summary>
    /// Verifies an all-path contextual reverse search reports cache exhaustion instead of
    /// publishing a provisional match start.
    /// </summary>
    [TestMethod]
    public void ReverseAllTransitionBudgetExhaustionReportsGiveUp()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("foo$"u8);
        RegexCompileOptions options = CreateOptions(multiLine: false, crlf: false);
        RegexNfa reversed = RegexNfaCompiler.CompileReversed(tree.Root, options);
        Assert.IsTrue(RegexLookaroundLazyDfa.TryCreate(
            reversed,
            dfaSizeLimit: 96,
            RegexDfaMatchKind.All,
            out RegexLookaroundLazyDfa? dfa));

        Assert.IsFalse(dfa!.TryFindStartReverse(
            "foo"u8,
            start: 0,
            end: 3,
            reachabilityCache: null,
            out int matchStart,
            out bool gaveUp));
        Assert.AreEqual(-1, matchStart);
        Assert.IsTrue(gaveUp);
    }

    /// <summary>
    /// Verifies span aggregation uses complete leftmost matches reconstructed by the reverse DFA.
    /// </summary>
    [TestMethod]
    public void PairedSearchSumsCompleteLeftmostSpans()
    {
        const string Pattern = "(?i)[a-z]{0,50}?key[a-z]{0,20}=([a-z]{10,20})(?:\"|$)";
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(
            System.Text.Encoding.ASCII.GetBytes(Pattern));
        RegexCompileOptions options = CreateOptions(multiLine: false, crlf: false);
        RegexNfa nfa = RegexNfaCompiler.Compile(tree.Root, options);
        Assert.IsTrue(RegexUnanchoredLazyDfa.TryCreate(
            nfa,
            tree.Root,
            options,
            GenerousDfaSizeLimit,
            out RegexUnanchoredLazyDfa? dfa));
        byte[] haystack = "PublicKeyToken=abcdefghijklmnop\" PublicKeyToken=qrstuvwxyzabcdef\""u8.ToArray();

        Assert.IsTrue(dfa!.TrySumMatchSpans(haystack, startAt: 0, out long spanSum));
        Assert.AreEqual(64, spanSum);
    }

    /// <summary>
    /// Verifies expanded UTF-8 scalar transitions and anchors agree with PikeVM even when the
    /// requested start offset falls inside a multibyte scalar.
    /// </summary>
    [TestMethod]
    public void ExpandedUtf8AnchorSearchMatchesPikeVmAtEveryByteOffset()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("α+$"u8);
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: true,
            dotMatchesNewline: false,
            utf8: true,
            unicodeClasses: true,
            specializationMode: RegexSpecializationMode.General,
            matchInvalidUtf8: true);
        RegexNfa nfa = RegexNfaCompiler.Compile(tree.Root, options);
        var fallback = RegexMetaEngine.Compile(
            nfa,
            prefilter: null,
            dfaSizeLimit: 0);
        Assert.IsTrue(RegexUnanchoredLazyDfa.TryCreate(
            tree.Root,
            options,
            GenerousDfaSizeLimit,
            out RegexUnanchoredLazyDfa? dfa));
        byte[] haystack = "xαα\nαα"u8.ToArray();

        for (int startAt = 0; startAt <= haystack.Length; startAt++)
        {
            RegexMatch? expected = fallback.Find(haystack, startAt);
            bool found = dfa!.TryFind(haystack, startAt, out RegexMatch actual, out bool gaveUp);

            Assert.IsFalse(gaveUp);
            Assert.AreEqual(expected.HasValue, found);
            Assert.AreEqual(expected ?? default, actual);
        }
    }

    /// <summary>
    /// Verifies Unicode-sensitive word assertions remain on the authoritative engine.
    /// </summary>
    [TestMethod]
    public void UnicodeWordPredicateIsNotContextuallyDeterminized()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"\bword\b"u8);
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: true,
            unicodeClasses: true,
            specializationMode: RegexSpecializationMode.General);
        RegexNfa nfa = RegexNfaCompiler.CompileUnanchored(tree.Root, options);

        Assert.IsFalse(RegexLookaroundDfaOperations.CanCompile(nfa));
        Assert.IsFalse(RegexLookaroundLazyDfa.TryCreate(
            nfa,
            GenerousDfaSizeLimit,
            out RegexLookaroundLazyDfa? dfa));
        Assert.IsNull(dfa);
    }

    /// <summary>
    /// Verifies a scoped byte-mode word assertion remains eligible under Unicode root options.
    /// </summary>
    [TestMethod]
    public void ScopedByteWordPredicateIsContextuallyDeterminized()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"(?-u:\bword\b)"u8);
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: true,
            unicodeClasses: true,
            specializationMode: RegexSpecializationMode.General);

        Assert.IsTrue(RegexUnanchoredLazyDfa.CanCompileSyntax(tree.Root, options));
        Assert.IsTrue(RegexUnanchoredLazyDfa.TryCreate(
            tree.Root,
            options,
            GenerousDfaSizeLimit,
            out RegexUnanchoredLazyDfa? dfa));
        Assert.IsTrue(dfa!.TryFind("!word!"u8, startAt: 0, out RegexMatch match, out bool gaveUp));
        Assert.IsFalse(gaveUp);
        Assert.AreEqual(new RegexMatch(1, 4), match);
    }

    private static RegexCompileOptions CreateOptions(bool multiLine, bool crlf)
    {
        return new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine,
            dotMatchesNewline: false,
            crlf,
            lineTerminator: (byte)'\n',
            utf8: false,
            unicodeClasses: false,
            specializationMode: RegexSpecializationMode.General);
    }
}
