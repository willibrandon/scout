using System.Text;

namespace Scout;

/// <summary>
/// Verifies malformed UTF-8 analysis selects the compact scalar path only for consuming atoms.
/// </summary>
[TestClass]
public sealed class RegexInvalidUtf8AnalysisTests
{
    private const string Issue58Pattern =
        @"(?u:\b|\B)(?-u:\b)ey[a-zA-Z0-9]{17,}\.ey[a-zA-Z0-9/\\_-]{17,}\.(?:[a-zA-Z0-9/\\_-]{10,}={0,2})?";

    /// <summary>
    /// Verifies every Unicode word predicate uses the byte-native malformed-context policy.
    /// </summary>
    [TestMethod]
    [DataRow(@"\b")]
    [DataRow(@"\B")]
    [DataRow(@"\b{start}")]
    [DataRow(@"\b{end}")]
    [DataRow(@"\b{start-half}")]
    [DataRow(@"\b{end-half}")]
    [DataRow(@"(?u:\b|\B)")]
    public void BoundaryPredicatesDoNotRequireReplacementScalarConsumption(string pattern)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern));

        Assert.IsFalse(RegexInvalidUtf8Analysis.RequiresReplacementScalarConsumption(
            tree.Root,
            CreateOptions()));
    }

    /// <summary>
    /// Verifies consuming atoms that accept U+FFFD continue to select replacement-scalar matching.
    /// </summary>
    [TestMethod]
    [DataRow(@"\u{FFFD}")]
    [DataRow(@"[\u{FFFD}]")]
    [DataRow(@"\p{So}")]
    [DataRow("[^a]")]
    [DataRow(".")]
    public void ConsumingReplacementScalarSyntaxRequiresCompactPath(string pattern)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern));

        Assert.IsTrue(RegexInvalidUtf8Analysis.RequiresReplacementScalarConsumption(
            tree.Root,
            CreateOptions()));
    }

    /// <summary>
    /// Verifies raw-byte scopes remain insensitive while later Unicode consumers remain sensitive.
    /// </summary>
    [TestMethod]
    public void ScopedRawByteAtomsRespectReplacementScalarConsumption()
    {
        RegexCompileOptions options = CreateOptions();
        RegexSyntaxTree raw = RegexSyntaxParser.Parse(@"(?-u:.)"u8);
        RegexSyntaxTree mixed = RegexSyntaxParser.Parse(@"(?-u:.)\u{FFFD}"u8);

        Assert.IsFalse(RegexInvalidUtf8Analysis.RequiresReplacementScalarConsumption(raw.Root, options));
        Assert.IsTrue(RegexInvalidUtf8Analysis.RequiresReplacementScalarConsumption(mixed.Root, options));
    }

    /// <summary>
    /// Verifies the reported pattern retains its required-literal prefilter with malformed matching enabled.
    /// </summary>
    [TestMethod]
    public void Issue58PatternKeepsRequiredLiteralPrefilter()
    {
        RegexCompileOptions options = CreateOptions();
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(Issue58Pattern));

        var automaton = RegexAutomaton.CompileParsed(tree, options);

        Assert.IsFalse(RegexInvalidUtf8Analysis.RequiresReplacementScalarConsumption(tree.Root, options));
        Assert.AreEqual(RegexPrefilterKind.RequiredLiteral, automaton.PrefilterKind);
    }

    /// <summary>
    /// Verifies disabled malformed matching never requests the replacement-scalar path.
    /// </summary>
    [TestMethod]
    public void DisabledOptionNeverRequiresReplacementScalarConsumption()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"\u{FFFD}"u8);
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false);

        Assert.IsFalse(RegexInvalidUtf8Analysis.RequiresReplacementScalarConsumption(tree.Root, options));
    }

    private static RegexCompileOptions CreateOptions()
    {
        return new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false,
            matchInvalidUtf8: true);
    }
}
