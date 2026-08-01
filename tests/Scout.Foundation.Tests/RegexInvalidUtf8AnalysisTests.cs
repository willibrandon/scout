using System.Text;

namespace Scout;

/// <summary>
/// Verifies malformed UTF-8 analysis selects the compact scalar path only for consuming atoms.
/// </summary>
public sealed class RegexInvalidUtf8AnalysisTests
{
    private const string Issue58Pattern =
        @"(?u:\b|\B)(?-u:\b)ey[a-zA-Z0-9]{17,}\.ey[a-zA-Z0-9/\\_-]{17,}\.(?:[a-zA-Z0-9/\\_-]{10,}={0,2})?";

    /// <summary>
    /// Verifies every Unicode word predicate uses the byte-native malformed-context policy.
    /// </summary>
    [Theory]
    [InlineData(@"\b")]
    [InlineData(@"\B")]
    [InlineData(@"\b{start}")]
    [InlineData(@"\b{end}")]
    [InlineData(@"\b{start-half}")]
    [InlineData(@"\b{end-half}")]
    [InlineData(@"(?u:\b|\B)")]
    public void BoundaryPredicatesDoNotRequireReplacementScalarConsumption(string pattern)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern));

        Assert.False(RegexInvalidUtf8Analysis.RequiresReplacementScalarConsumption(
            tree.Root,
            CreateOptions()));
    }

    /// <summary>
    /// Verifies consuming atoms that accept U+FFFD continue to select replacement-scalar matching.
    /// </summary>
    [Theory]
    [InlineData(@"\u{FFFD}")]
    [InlineData(@"[\u{FFFD}]")]
    [InlineData(@"\p{So}")]
    [InlineData("[^a]")]
    [InlineData(".")]
    public void ConsumingReplacementScalarSyntaxRequiresCompactPath(string pattern)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern));

        Assert.True(RegexInvalidUtf8Analysis.RequiresReplacementScalarConsumption(
            tree.Root,
            CreateOptions()));
    }

    /// <summary>
    /// Verifies raw-byte scopes remain insensitive while later Unicode consumers remain sensitive.
    /// </summary>
    [Fact]
    public void ScopedRawByteAtomsRespectReplacementScalarConsumption()
    {
        RegexCompileOptions options = CreateOptions();
        RegexSyntaxTree raw = RegexSyntaxParser.Parse(@"(?-u:.)"u8);
        RegexSyntaxTree mixed = RegexSyntaxParser.Parse(@"(?-u:.)\u{FFFD}"u8);

        Assert.False(RegexInvalidUtf8Analysis.RequiresReplacementScalarConsumption(raw.Root, options));
        Assert.True(RegexInvalidUtf8Analysis.RequiresReplacementScalarConsumption(mixed.Root, options));
    }

    /// <summary>
    /// Verifies the reported pattern retains its required-literal prefilter with malformed matching enabled.
    /// </summary>
    [Fact]
    public void Issue58PatternKeepsRequiredLiteralPrefilter()
    {
        RegexCompileOptions options = CreateOptions();
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(Issue58Pattern));

        var automaton = RegexAutomaton.CompileParsed(tree, options);

        Assert.False(RegexInvalidUtf8Analysis.RequiresReplacementScalarConsumption(tree.Root, options));
        Assert.Equal(RegexPrefilterKind.RequiredLiteral, automaton.PrefilterKind);
    }

    /// <summary>
    /// Verifies disabled malformed matching never requests the replacement-scalar path.
    /// </summary>
    [Fact]
    public void DisabledOptionNeverRequiresReplacementScalarConsumption()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"\u{FFFD}"u8);
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false);

        Assert.False(RegexInvalidUtf8Analysis.RequiresReplacementScalarConsumption(tree.Root, options));
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
