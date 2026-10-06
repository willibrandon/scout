using System.Text;

namespace Scout;

/// <summary>
/// Verifies exact finite-language extraction from authoritative regex syntax.
/// </summary>
[TestClass]
public sealed class RegexFiniteLiteralExtractorTests
{
    /// <summary>
    /// Verifies concatenation distributes over nested alternations in stable preference order.
    /// </summary>
    /// <param name="pattern">The finite regex expression.</param>
    /// <param name="expected">The expected comma-separated literal preference order.</param>
    [TestMethod]
    [DataRow(
        "(?:Generated|Paladin(?:Record|Value))",
        "Generated,PaladinRecord,PaladinValue")]
    [DataRow("(?:ab|cd)(?:ef|gh)", "abef,abgh,cdef,cdgh")]
    [DataRow("(?:x|y){2}", "xx,xy,yx,yy")]
    [DataRow("foo(?:bar|baz)?", "foobar,foobaz,foo")]
    [DataRow("foo(?:bar|baz)??", "foo,foobar,foobaz")]
    [DataRow("(?U:foo(?:bar|baz)?)", "foo,foobar,foobaz")]
    [DataRow("(?U:foo(?:bar|baz)??)", "foobar,foobaz,foo")]
    [DataRow("[ab](?:c|d)", "ac,ad,bc,bd")]
    [DataRow("[a-c&&b-d]x", "bx,cx")]
    [DataRow("[a-f--aeiou]", "b,c,d,f")]
    [DataRow("[a-c~~b-d]", "a,d")]
    [DataRow("[a-c[0-2]]", "0,1,2,a,b,c")]
    [DataRow("[0-9]", "0,1,2,3,4,5,6,7,8,9")]
    [DataRow("[δλ](?:x|y)", "δx,δy,λx,λy")]
    public void ExtractsFiniteLanguagesInRegexPreferenceOrder(
        string pattern,
        string expected)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(expected);
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern));

        bool extracted = RegexFiniteLiteralExtractor.TryExtract(
            tree.Root,
            CreateOptions(),
            out List<byte[]> literals,
            out bool? caseInsensitive,
            out bool? unicodeCaseFolding,
            out _);

        Assert.IsTrue(extracted);
        Assert.AreSequenceEqual(expected.Split(','), literals.Select(Encoding.UTF8.GetString));
        Assert.IsFalse(caseInsensitive);
        Assert.IsNull(unicodeCaseFolding);
    }

    /// <summary>
    /// Verifies the issue #46 nested expressions use the exact literal-set engine.
    /// </summary>
    /// <param name="pattern">The nested finite regex expression.</param>
    /// <param name="haystack">The input text.</param>
    /// <param name="expectedMatches">The expected non-overlapping match count.</param>
    [TestMethod]
    [DataRow(
        "(?:Generated|Paladin(?:Record|Value))",
        "Generated PaladinRecord PaladinValue Paladin Missing",
        3)]
    [DataRow(
        "(?:Absent|Missing(?:Two|Three))",
        "Absent MissingTwo MissingThree Missing Four",
        3)]
    public void NestedLiteralAlternationsUseLiteralSetSpecialization(
        string pattern,
        string haystack,
        int expectedMatches)
    {
        var automaton = RegexAutomaton.Compile(Encoding.UTF8.GetBytes(pattern));

        Assert.AreEqual(RegexEngineKind.LiteralSet, automaton.EngineKind);
        Assert.AreEqual(expectedMatches, automaton.CountMatches(Encoding.UTF8.GetBytes(haystack)));
    }

    /// <summary>
    /// Verifies finite class algebra and bounded products execute through the exact literal-set engine.
    /// </summary>
    /// <param name="pattern">The finite regex expression.</param>
    /// <param name="haystack">The input text.</param>
    /// <param name="expectedMatches">The expected non-overlapping match count.</param>
    [TestMethod]
    [DataRow("[a-c&&b-d]x", "ax bx cx dx", 2)]
    [DataRow("[a-f--aeiou]", "abcdef", 4)]
    [DataRow("[a-c~~b-d]", "abcd", 2)]
    [DataRow("[a-c[0-2]]", "0a1b2c3d", 6)]
    [DataRow("(?:x|y){2}", "xx xy yx yy xz", 4)]
    public void FiniteProductsUseLiteralSetSpecialization(
        string pattern,
        string haystack,
        int expectedMatches)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(haystack);
        var automaton = RegexAutomaton.Compile(Encoding.UTF8.GetBytes(pattern));

        Assert.AreEqual(RegexEngineKind.LiteralSet, automaton.EngineKind);
        Assert.AreEqual(expectedMatches, automaton.CountMatches(Encoding.UTF8.GetBytes(haystack)));
    }

    /// <summary>
    /// Verifies a finite Unicode class product preserves exact search semantics when a structural engine takes precedence.
    /// </summary>
    [TestMethod]
    public void UnicodeClassProductPreservesExactSearchSemantics()
    {
        var automaton = RegexAutomaton.Compile("[δλ](?:x|y)"u8);

        Assert.AreEqual(4, automaton.CountMatches("δx δy λx λy μx"u8));
    }

    /// <summary>
    /// Verifies swap-greed mode reverses bounded repetition preference without changing its finite language.
    /// </summary>
    /// <param name="pattern">The swap-greed finite expression.</param>
    /// <param name="expectedLength">The expected preferred match length.</param>
    [TestMethod]
    [DataRow("(?U:foo(?:bar|baz)?)", 3)]
    [DataRow("(?U:foo(?:bar|baz)??)", 6)]
    public void SwapGreedPreservesFiniteLanguagePreference(string pattern, int expectedLength)
    {
        var automaton = RegexAutomaton.Compile(Encoding.UTF8.GetBytes(pattern));

        Assert.AreEqual(RegexEngineKind.LiteralSet, automaton.EngineKind);
        Assert.AreEqual(new RegexMatch(0, expectedLength), automaton.Find("foobar"u8));
    }

    /// <summary>
    /// Verifies extraction declines languages that are infinite, over-limit, or mix case modes.
    /// </summary>
    /// <param name="pattern">The expression that cannot use exact finite-language specialization.</param>
    [TestMethod]
    [DataRow("(?:a|b)+")]
    [DataRow("a{11}")]
    [DataRow("[a-k]")]
    [DataRow("(?i:a)(?-i:b)")]
    [DataRow("^literal$")]
    public void DeclinesUnsupportedExactLanguages(string pattern)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern));

        Assert.IsFalse(RegexFiniteLiteralExtractor.TryExtract(
            tree.Root,
            CreateOptions(),
            out _,
            out _,
            out _,
            out _));
    }

    /// <summary>
    /// Verifies the extractor bounds total language size and individual literal length.
    /// </summary>
    [TestMethod]
    public void EnforcesFiniteLanguageExpansionLimits()
    {
        string manyAlternatives = string.Join(
            '|',
            Enumerable.Range(0, 251).Select(static index => $"token{index:D3}"));
        string longLiteral = new('a', 101);

        AssertExtractionDeclined(manyAlternatives);
        AssertExtractionDeclined(longLiteral);
    }

    /// <summary>
    /// Verifies the inclusive repetition, literal-length, and language-size limits.
    /// </summary>
    [TestMethod]
    public void AcceptsFiniteLanguagesAtInclusiveExpansionLimits()
    {
        AssertExtractedLanguage("a{10}", expectedCount: 1, expectedFirst: new string('a', 10), expectedLast: new string('a', 10));
        AssertExtractionDeclined("a{11}");

        string length100 = "a{10}" + new string('b', 90);
        string length101 = "a{10}" + new string('b', 91);
        AssertExtractedLanguage(length100, expectedCount: 1, expectedFirst: new string('a', 10) + new string('b', 90), expectedLast: new string('a', 10) + new string('b', 90));
        AssertExtractionDeclined(length101);

        string alternatives250 = CreateAlternation(250);
        AssertExtractedLanguage(alternatives250, expectedCount: 250, expectedFirst: "token000", expectedLast: "token249");
        AssertExtractionDeclined(CreateAlternation(251));
    }

    /// <summary>
    /// Verifies incompatible scoped ASCII and Unicode folding modes retain authoritative fallback semantics.
    /// </summary>
    [TestMethod]
    public void MixedUnicodeCaseFoldingUsesAuthoritativeAutomaton()
    {
        const string pattern = "(?i-u:sx)|(?i:sy)";
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern));

        Assert.IsFalse(RegexFiniteLiteralExtractor.TryExtract(
            tree.Root,
            CreateOptions(),
            out _,
            out _,
            out _,
            out _));

        var automaton = RegexAutomaton.Compile(Encoding.UTF8.GetBytes(pattern));
        Assert.AreNotEqual(RegexEngineKind.LiteralSet, automaton.EngineKind);
        Assert.IsNull(automaton.Find("ſx"u8));
        Assert.AreEqual(new RegexMatch(4, 3), automaton.Find("ſx ſy"u8));
    }

    /// <summary>
    /// Verifies an exact language containing the empty string stays on the authoritative automaton path.
    /// </summary>
    [TestMethod]
    public void LiteralSetSpecializationRejectsEmptyLanguageMembers()
    {
        var automaton = RegexAutomaton.Compile("(?:foo|)"u8);

        Assert.AreNotEqual(RegexEngineKind.LiteralSet, automaton.EngineKind);
        Assert.AreEqual(new RegexMatch(0, 0), automaton.Find("bar"u8));
    }

    /// <summary>
    /// Verifies an exact language above the extraction limits retains authoritative automaton semantics.
    /// </summary>
    [TestMethod]
    public void OverLimitFiniteLanguageUsesAuthoritativeAutomaton()
    {
        var automaton = RegexAutomaton.Compile("[a-k]"u8);

        Assert.AreNotEqual(RegexEngineKind.LiteralSet, automaton.EngineKind);
        Assert.AreEqual(11, automaton.CountMatches("abcdefghijk"u8));
    }

    /// <summary>
    /// Verifies word-boundary specialization rejects scoped case folding and empty language members.
    /// </summary>
    [TestMethod]
    public void WordBoundaryFiniteLanguageFallbackPreservesSemantics()
    {
        var folded = RegexAutomaton.Compile(@"\b(?i:foo|bar)\b"u8);
        var empty = RegexAutomaton.Compile(@"\b(?:foo|)\b"u8);

        Assert.AreNotEqual(RegexEngineKind.WordBoundaryLiteralSet, folded.EngineKind);
        Assert.AreEqual(new RegexMatch(1, 3), folded.Find(" FOO "u8));
        Assert.AreNotEqual(RegexEngineKind.WordBoundaryLiteralSet, empty.EngineKind);
        Assert.AreEqual(new RegexMatch(0, 0), empty.Find("x "u8));
    }

    /// <summary>
    /// Verifies the word-boundary literal-set aggregate limit is inclusive and never truncates fallback matches.
    /// </summary>
    [TestMethod]
    public void WordBoundaryLiteralSetHonorsAggregateLanguageLimit()
    {
        string acceptedPattern = CreateWordBoundaryAlternation(250);
        var accepted = RegexAutomaton.Compile(Encoding.UTF8.GetBytes(acceptedPattern));
        Assert.AreEqual(RegexEngineKind.WordBoundaryLiteralSet, accepted.EngineKind);
        Assert.AreEqual(new RegexMatch(1, 10), accepted.Find(" keyword249 "u8));

        string fallbackPattern = CreateWordBoundaryAlternation(251);
        var fallback = RegexAutomaton.Compile(Encoding.UTF8.GetBytes(fallbackPattern));
        Assert.AreNotEqual(RegexEngineKind.WordBoundaryLiteralSet, fallback.EngineKind);
        Assert.AreEqual(new RegexMatch(1, 10), fallback.Find(" keyword250 "u8));
    }

    private static RegexCompileOptions CreateOptions()
    {
        return new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false);
    }

    private static void AssertExtractionDeclined(string pattern)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern));
        Assert.IsFalse(RegexFiniteLiteralExtractor.TryExtract(
            tree.Root,
            CreateOptions(),
            out _,
            out _,
            out _,
            out _));
    }

    private static void AssertExtractedLanguage(
        string pattern,
        int expectedCount,
        string expectedFirst,
        string expectedLast)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern));
        Assert.IsTrue(RegexFiniteLiteralExtractor.TryExtract(
            tree.Root,
            CreateOptions(),
            out List<byte[]> literals,
            out _,
            out _,
            out _));
        Assert.HasCount(expectedCount, literals);
        Assert.AreEqual(expectedFirst, Encoding.UTF8.GetString(literals[0]));
        Assert.AreEqual(expectedLast, Encoding.UTF8.GetString(literals[^1]));
    }

    private static string CreateAlternation(int count)
    {
        return string.Join('|', Enumerable.Range(0, count).Select(static index => $"token{index:D3}"));
    }

    private static string CreateWordBoundaryAlternation(int count)
    {
        return string.Join(
            '|',
            Enumerable.Range(0, count).Select(static index => $@"(?:\b(keyword{index:D3})\b)"));
    }
}
