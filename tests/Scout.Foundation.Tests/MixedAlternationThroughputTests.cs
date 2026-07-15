using System.Text;

namespace Scout;

/// <summary>
/// Verifies mixed-alternation matching remains authoritative without sacrificing throughput.
/// </summary>
[Collection(MixedAlternationThroughputTestGroup.Name)]
public sealed class MixedAlternationThroughputTests()
{
    /// <summary>
    /// Verifies mixed literal and regex alternatives use a conservative prefilter and authoritative verification.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void SearchMixedAlternationUsesConservativeCandidatesWithAuthoritativeVerification()
    {
        const string pattern =
            "CollectExtensionSuffixCandidates|extensionSuffixPatterns|CreateAhoCorasick|GlobSet.cs";
        byte[][] patterns = [Encoding.UTF8.GetBytes(pattern)];
        byte[] unrelatedLine = "private readonly byte[][] unrelatedPatterns;\n"u8.ToArray();
        byte[] matchingLine = "CreateAhoCorasick(copy);\n"u8.ToArray();
        byte[] haystack = new byte[(unrelatedLine.Length * 350_000) + matchingLine.Length];
        for (int offset = 0; offset < haystack.Length - matchingLine.Length; offset += unrelatedLine.Length)
        {
            unrelatedLine.CopyTo(haystack, offset);
        }

        matchingLine.CopyTo(haystack, haystack.Length - matchingLine.Length);
        RegexSearchPlan? plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);
        var sink = new CapturingMatchLineSink();

        bool matched = LiteralLineSearcher.SearchMatchLinesWithRegexPlan(
            haystack,
            patterns,
            plan,
            ref sink);
        LiteralLineSearcher.CountMatchesAndMatchingLinesWithRegexPlan(
            haystack,
            patterns,
            plan,
            asciiCaseInsensitive: false,
            lineRegexp: false,
            wordRegexp: false,
            maxMatchingLines: null,
            crlf: false,
            nullData: false,
            out long matchingLines,
            out long matches);

        Assert.NotNull(plan);
        Assert.NotEqual(RegexPrefilterKind.None, plan.Matcher.PrefilterKind);
        Assert.True(matched);
        Assert.Equal(1UL, sink.Matches);
        Assert.Equal(350_001, sink.LineNumber);
        Assert.Equal("CreateAhoCorasick"u8.ToArray(), sink.Match);
        Assert.Equal(1, matchingLines);
        Assert.Equal(1, matches);

        var rejectedSink = new CapturingMatchLineSink();
        Assert.False(LiteralLineSearcher.SearchMatchLinesWithRegexPlan(
            "CreateButNotAhoCorasick\n"u8,
            patterns,
            plan,
            ref rejectedSink));
        Assert.Equal(0UL, rejectedSink.Matches);
    }
}
