using System.Text;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Exercises release dependency regressions through the exported regex API on every supported framework.
/// </summary>
public sealed class UpstreamRegexRegressionTests
{
    /// <summary>
    /// Verifies comparison negation combines with the Unicode escape and enclosing character classes.
    /// </summary>
    /// <param name="pattern">The Unicode property expression.</param>
    /// <param name="matchesLetter">Whether the expression matches a letter.</param>
    [Theory]
    [InlineData(@"\p{gc!=Separator}", true)]
    [InlineData(@"\P{gc!=separator}", false)]
    [InlineData(@"\p{General_Category!=Z}", true)]
    [InlineData(@"\P{General_Category!=Z}", false)]
    [InlineData(@"[\p{gc!=Separator}]", true)]
    [InlineData(@"[^\p{gc!=Separator}]", false)]
    [InlineData(@"[\P{gc!=Separator}]", false)]
    [InlineData(@"[^\P{gc!=Separator}]", true)]
    public void UnicodeComparisonNegationMatchesUpstream(string pattern, bool matchesLetter)
    {
        foreach (ByteRegexEngineMode mode in Enum.GetValues<ByteRegexEngineMode>())
        {
            var regex = ByteRegex.Compile(pattern, new ByteRegexOptions { EngineMode = mode });
            Assert.Equal(matchesLetter, regex.IsMatch("A"u8));
            Assert.Equal(!matchesLetter, regex.IsMatch(" "u8));
            Assert.Equal(!matchesLetter, regex.IsMatch(Encoding.UTF8.GetBytes("\u2028")));
            Assert.Equal(matchesLetter, regex.IsMatch(Encoding.UTF8.GetBytes("λ")));
            Assert.False(regex.IsMatch([0xFF]));
        }
    }

    /// <summary>
    /// Verifies zero repetitions retain absent capture groups during repeated searches.
    /// </summary>
    [Fact]
    public void ZeroRepetitionCapturesRemainAbsent()
    {
        foreach (ByteRegexEngineMode mode in Enum.GetValues<ByteRegexEngineMode>())
        {
            var regex = ByteRegex.Compile("(abc)(ABC){0}", new ByteRegexOptions { EngineMode = mode });
            for (int iteration = 0; iteration < 8; iteration++)
            {
                ByteRegexCaptures captures = Assert.IsType<ByteRegexCaptures>(regex.FindCaptures("abcABC"u8));
                Assert.Equal(0, captures.Match.Start);
                Assert.Equal(3, captures.Match.Length);
                Assert.Equal(3, captures.GroupCount);
                Assert.Equal(captures.Match, captures.GetGroup(1));
                Assert.Null(captures.GetGroup(2));
                Assert.Null(regex.FindCaptures("def"u8));
            }
        }
    }

    /// <summary>
    /// Verifies comparison negation works for named script properties and extended syntax.
    /// </summary>
    [Fact]
    public void ScriptComparisonNegationUsesSharedSyntax()
    {
        foreach (ByteRegexEngineMode mode in Enum.GetValues<ByteRegexEngineMode>())
        {
            var regex = ByteRegex.Compile(@"(?x)\P{Script ! = Greek}", new ByteRegexOptions { EngineMode = mode });
            Assert.True(regex.IsMatch(Encoding.UTF8.GetBytes("λ")));
            Assert.False(regex.IsMatch("A"u8));
        }
    }

    /// <summary>
    /// Verifies property comparison negation composes with class algebra and case folding.
    /// </summary>
    [Fact]
    public void UnicodeComparisonsComposeWithClassAlgebra()
    {
        foreach (ByteRegexOptions options in Enum.GetValues<ByteRegexEngineMode>()
            .Select(mode => new ByteRegexOptions { EngineMode = mode }))
        {
            var regex = ByteRegex.Compile(@"(?i)[[\P{gc!=Letter}]&&[a-z]]", options);
            Assert.True(regex.IsMatch("A"u8));
            Assert.True(regex.IsMatch(Encoding.UTF8.GetBytes("ſK")));
            Assert.False(regex.IsMatch(" "u8));
            Assert.False(regex.IsMatch(Encoding.UTF8.GetBytes("λ")));
            regex = ByteRegex.Compile(@"[[\p{Script!=Greek}]--[a-zA-Z]]", options);
            Assert.True(regex.IsMatch("1"u8));
            Assert.False(regex.IsMatch("A"u8));
            Assert.False(regex.IsMatch(Encoding.UTF8.GetBytes("λ")));
        }
    }
}
