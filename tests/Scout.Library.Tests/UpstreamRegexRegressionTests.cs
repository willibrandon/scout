using System.Text;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Exercises release dependency regressions through the exported regex API on every supported framework.
/// </summary>
[TestClass]
public sealed class UpstreamRegexRegressionTests
{
    /// <summary>
    /// Verifies comparison negation combines with the Unicode escape and enclosing character classes.
    /// </summary>
    /// <param name="pattern">The Unicode property expression.</param>
    /// <param name="matchesLetter">Whether the expression matches a letter.</param>
    [TestMethod]
    [DataRow(@"\p{gc!=Separator}", true)]
    [DataRow(@"\P{gc!=separator}", false)]
    [DataRow(@"\p{General_Category!=Z}", true)]
    [DataRow(@"\P{General_Category!=Z}", false)]
    [DataRow(@"[\p{gc!=Separator}]", true)]
    [DataRow(@"[^\p{gc!=Separator}]", false)]
    [DataRow(@"[\P{gc!=Separator}]", false)]
    [DataRow(@"[^\P{gc!=Separator}]", true)]
    public void UnicodeComparisonNegationMatchesUpstream(string pattern, bool matchesLetter)
    {
        foreach (ByteRegexEngineMode mode in Enum.GetValues<ByteRegexEngineMode>())
        {
            var regex = ByteRegex.Compile(pattern, new ByteRegexOptions { EngineMode = mode });
            Assert.AreEqual(matchesLetter, regex.IsMatch("A"u8));
            Assert.AreEqual(!matchesLetter, regex.IsMatch(" "u8));
            Assert.AreEqual(!matchesLetter, regex.IsMatch(Encoding.UTF8.GetBytes("\u2028")));
            Assert.AreEqual(matchesLetter, regex.IsMatch(Encoding.UTF8.GetBytes("λ")));
            Assert.IsFalse(regex.IsMatch([0xFF]));
        }
    }

    /// <summary>
    /// Verifies zero repetitions retain absent capture groups during repeated searches.
    /// </summary>
    [TestMethod]
    public void ZeroRepetitionCapturesRemainAbsent()
    {
        foreach (ByteRegexEngineMode mode in Enum.GetValues<ByteRegexEngineMode>())
        {
            var regex = ByteRegex.Compile("(abc)(ABC){0}", new ByteRegexOptions { EngineMode = mode });
            for (int iteration = 0; iteration < 8; iteration++)
            {
                ByteRegexCaptures captures = Assert.IsExactInstanceOfType<ByteRegexCaptures>(regex.FindCaptures("abcABC"u8));
                Assert.AreEqual(0, captures.Match.Start);
                Assert.AreEqual(3, captures.Match.Length);
                Assert.AreEqual(3, captures.GroupCount);
                Assert.AreEqual(captures.Match, captures.GetGroup(1));
                Assert.IsNull(captures.GetGroup(2));
                Assert.IsNull(regex.FindCaptures("def"u8));
            }
        }
    }

    /// <summary>
    /// Verifies comparison negation works for named script properties and extended syntax.
    /// </summary>
    [TestMethod]
    public void ScriptComparisonNegationUsesSharedSyntax()
    {
        foreach (ByteRegexEngineMode mode in Enum.GetValues<ByteRegexEngineMode>())
        {
            var regex = ByteRegex.Compile(@"(?x)\P{Script ! = Greek}", new ByteRegexOptions { EngineMode = mode });
            Assert.IsTrue(regex.IsMatch(Encoding.UTF8.GetBytes("λ")));
            Assert.IsFalse(regex.IsMatch("A"u8));
        }
    }

    /// <summary>
    /// Verifies property comparison negation composes with class algebra and case folding.
    /// </summary>
    [TestMethod]
    public void UnicodeComparisonsComposeWithClassAlgebra()
    {
        foreach (ByteRegexOptions options in Enum.GetValues<ByteRegexEngineMode>()
            .Select(mode => new ByteRegexOptions { EngineMode = mode }))
        {
            var regex = ByteRegex.Compile(@"(?i)[[\P{gc!=Letter}]&&[a-z]]", options);
            Assert.IsTrue(regex.IsMatch("A"u8));
            Assert.IsTrue(regex.IsMatch(Encoding.UTF8.GetBytes("ſK")));
            Assert.IsFalse(regex.IsMatch(" "u8));
            Assert.IsFalse(regex.IsMatch(Encoding.UTF8.GetBytes("λ")));
            regex = ByteRegex.Compile(@"[[\p{Script!=Greek}]--[a-zA-Z]]", options);
            Assert.IsTrue(regex.IsMatch("1"u8));
            Assert.IsFalse(regex.IsMatch("A"u8));
            Assert.IsFalse(regex.IsMatch(Encoding.UTF8.GetBytes("λ")));
        }
    }
}
