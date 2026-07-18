using System.Text;

namespace Scout;

/// <summary>
/// Verifies mixed-alternation matching remains authoritative without sacrificing throughput.
/// </summary>
[Collection(MixedAlternationThroughputTestGroup.Name)]
public sealed class MixedAlternationThroughputTests()
{
    /// <summary>
    /// Verifies context construction scans a large buffer through conservative whole-buffer candidates.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void BuildSearchResultUsesWholeBufferMixedAlternationCandidates()
    {
        const string Pattern =
            "CollectExtensionSuffixCandidates|extensionSuffixPatterns|CreateAhoCorasick|GlobSet.cs";
        byte[][] patterns = [Encoding.UTF8.GetBytes(Pattern)];
        byte[] unrelatedLine =
            "private readonly byte[][] unrelatedPatterns;\n"u8.ToArray();
        byte[] matchingLine = "CreateAhoCorasick(copy);\n"u8.ToArray();
        byte[] haystack =
            new byte[(unrelatedLine.Length * 350_000) + matchingLine.Length];
        for (int offset = 0;
            offset < haystack.Length - matchingLine.Length;
            offset += unrelatedLine.Length)
        {
            unrelatedLine.CopyTo(haystack, offset);
        }

        matchingLine.CopyTo(haystack, haystack.Length - matchingLine.Length);
        var regexPlan = RegexSearchPlan.Create(
            patterns,
            asciiCaseInsensitive: false);

        ContextSearchResult result = ContextSearchOperations.BuildSearchResult(
            haystack,
            patterns,
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            crlf: false,
            nullData: false,
            stopOnNonmatch: false,
            regexPlan);

        Assert.Equal(350_001, result.Lines.Count);
        Assert.DoesNotContain(
            result.Lines.Take(350_000),
            line => line.SelectedMatch);
        Assert.True(result.Lines[^1].SelectedMatch);
        Assert.Equal(1, result.Lines[^1].MatchColumn);
    }

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
        RegexSearchPlan plan = LiteralLineSearcher.CreateRegexSearchPlan(
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
