namespace Scout;

/// <summary>
/// Verifies bounded lazy determinization of byte-safe look-around assertions.
/// </summary>
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
    [Theory]
    [InlineData("foo$", "foo\nfoo\r\nfoo\rbar\nfoo xfoo", true, false)]
    [InlineData("foo$", "foo\nfoo\r\nfoo\rbar\nfoo xfoo", true, true)]
    [InlineData("^foo", "xfoo\nfoo\r\nfoo", true, true)]
    [InlineData("\\Afoo", "foo foo", false, false)]
    [InlineData("foo\\z", "foo foo", false, false)]
    [InlineData("\\bfoo\\b", "xfoo foo foo!", false, false)]
    [InlineData("\\Bfoo\\B", "xfoox foo", false, false)]
    [InlineData("\\<foo\\>", "xfoo foo foo!", false, false)]
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
        Assert.True(RegexUnanchoredLazyDfa.TryCreate(
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

            Assert.False(gaveUp);
            Assert.Equal(expected.HasValue, found);
            Assert.Equal(expected ?? default, actual);
        }
    }

    /// <summary>
    /// Verifies cache exhaustion is reported so callers can retain authoritative fallback behavior.
    /// </summary>
    [Fact]
    public void TransitionBudgetExhaustionReportsGiveUp()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("foo$"u8);
        RegexCompileOptions options = CreateOptions(multiLine: false, crlf: false);
        RegexNfa nfa = RegexNfaCompiler.CompileUnanchored(tree.Root, options);
        Assert.True(RegexLookaroundLazyDfa.TryCreate(
            nfa,
            dfaSizeLimit: 96,
            out RegexLookaroundLazyDfa? dfa));

        Assert.False(dfa!.TryFindEnd(
            "xxfoo"u8,
            start: 0,
            reachabilityCache: null,
            out int end,
            out bool gaveUp));
        Assert.Equal(-1, end);
        Assert.True(gaveUp);
    }

    /// <summary>
    /// Verifies expanded UTF-8 scalar transitions and anchors agree with PikeVM even when the
    /// requested start offset falls inside a multibyte scalar.
    /// </summary>
    [Fact]
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
        Assert.True(RegexUnanchoredLazyDfa.TryCreate(
            tree.Root,
            options,
            GenerousDfaSizeLimit,
            out RegexUnanchoredLazyDfa? dfa));
        byte[] haystack = "xαα\nαα"u8.ToArray();

        for (int startAt = 0; startAt <= haystack.Length; startAt++)
        {
            RegexMatch? expected = fallback.Find(haystack, startAt);
            bool found = dfa!.TryFind(haystack, startAt, out RegexMatch actual, out bool gaveUp);

            Assert.False(gaveUp);
            Assert.Equal(expected.HasValue, found);
            Assert.Equal(expected ?? default, actual);
        }
    }

    /// <summary>
    /// Verifies Unicode-sensitive word assertions remain on the authoritative engine.
    /// </summary>
    [Fact]
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

        Assert.False(RegexLookaroundDfaOperations.CanCompile(nfa));
        Assert.False(RegexLookaroundLazyDfa.TryCreate(
            nfa,
            GenerousDfaSizeLimit,
            out RegexLookaroundLazyDfa? dfa));
        Assert.Null(dfa);
    }

    /// <summary>
    /// Verifies a scoped byte-mode word assertion remains eligible under Unicode root options.
    /// </summary>
    [Fact]
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

        Assert.True(RegexUnanchoredLazyDfa.CanCompileSyntax(tree.Root, options));
        Assert.True(RegexUnanchoredLazyDfa.TryCreate(
            tree.Root,
            options,
            GenerousDfaSizeLimit,
            out RegexUnanchoredLazyDfa? dfa));
        Assert.True(dfa!.TryFind("!word!"u8, startAt: 0, out RegexMatch match, out bool gaveUp));
        Assert.False(gaveUp);
        Assert.Equal(new RegexMatch(1, 4), match);
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
