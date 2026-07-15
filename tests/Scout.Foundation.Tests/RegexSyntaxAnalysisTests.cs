using System.Text;

namespace Scout;

/// <summary>
/// Verifies parsed regex execution-mode analysis.
/// </summary>
public sealed class RegexSyntaxAnalysisTests
{
    /// <summary>
    /// Verifies line-feed reachability follows consuming atoms and effective dotall scopes.
    /// </summary>
    /// <param name="pattern">The regex pattern.</param>
    /// <param name="dotMatchesNewline">Whether dotall is enabled before inline flags are applied.</param>
    /// <param name="expected">Whether the pattern can consume a line feed.</param>
    [Theory]
    [InlineData("a", false, false)]
    [InlineData("(?s:a)", false, false)]
    [InlineData("[a]", false, false)]
    [InlineData(@"\S", false, false)]
    [InlineData(@"\p{Letter}", false, false)]
    [InlineData(".", false, false)]
    [InlineData(".", true, true)]
    [InlineData("(?s:.)", false, true)]
    [InlineData("(?s)(?-s:.)", false, false)]
    [InlineData("\n", false, true)]
    [InlineData(@"\n", false, true)]
    [InlineData(@"[\n]", false, true)]
    [InlineData("[^a]", false, true)]
    [InlineData(@"\s", false, true)]
    [InlineData(@"\W", false, true)]
    [InlineData(@"\p{Control}", false, true)]
    public void DetectsActualLineFeedConsumption(
        string pattern,
        bool dotMatchesNewline,
        bool expected)
    {
        byte[][] patterns = [Encoding.UTF8.GetBytes(pattern)];

        Assert.Equal(
            expected,
            RegexSyntaxAnalysis.CanMatchLineFeed(patterns, dotMatchesNewline));
    }
}
