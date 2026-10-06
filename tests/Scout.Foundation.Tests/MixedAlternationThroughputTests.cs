using System.Text;

namespace Scout;

/// <summary>
/// Verifies mixed-alternation matching remains authoritative without sacrificing throughput.
/// </summary>
/// <param name="testContext">The context for the current test.</param>
[DoNotParallelize]
[TestClass]
public sealed class MixedAlternationThroughputTests(TestContext testContext)
{
    /// <summary>
    /// Verifies context construction scans a large buffer through conservative whole-buffer candidates.
    /// </summary>
    [TestMethod]
    [Timeout(5000, CooperativeCancellation = true)]
    public void BuildSearchResultUsesWholeBufferMixedAlternationCandidates()
    {
        CancellationToken cancellationToken = testContext.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
            unrelatedLine.CopyTo(haystack, offset);
        }

        cancellationToken.ThrowIfCancellationRequested();
        matchingLine.CopyTo(haystack, haystack.Length - matchingLine.Length);
        var regexPlan = RegexSearchPlan.Create(
            patterns,
            asciiCaseInsensitive: false);

        cancellationToken.ThrowIfCancellationRequested();
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

        cancellationToken.ThrowIfCancellationRequested();
        Assert.HasCount(350_001, result.Lines);
        Assert.DoesNotContain(
            line => line.SelectedMatch,
            result.Lines.Take(350_000));
        Assert.IsTrue(result.Lines[^1].SelectedMatch);
        Assert.AreEqual(1, result.Lines[^1].MatchColumn);
    }

    /// <summary>
    /// Verifies mixed literal and regex alternatives use a conservative prefilter and authoritative verification.
    /// </summary>
    [TestMethod]
    [Timeout(5000, CooperativeCancellation = true)]
    public void SearchMixedAlternationUsesConservativeCandidatesWithAuthoritativeVerification()
    {
        CancellationToken cancellationToken = testContext.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        const string pattern =
            "CollectExtensionSuffixCandidates|extensionSuffixPatterns|CreateAhoCorasick|GlobSet.cs";
        byte[][] patterns = [Encoding.UTF8.GetBytes(pattern)];
        byte[] unrelatedLine = "private readonly byte[][] unrelatedPatterns;\n"u8.ToArray();
        byte[] matchingLine = "CreateAhoCorasick(copy);\n"u8.ToArray();
        byte[] haystack = new byte[(unrelatedLine.Length * 350_000) + matchingLine.Length];
        for (int offset = 0; offset < haystack.Length - matchingLine.Length; offset += unrelatedLine.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            unrelatedLine.CopyTo(haystack, offset);
        }

        cancellationToken.ThrowIfCancellationRequested();
        matchingLine.CopyTo(haystack, haystack.Length - matchingLine.Length);
        RegexSearchPlan plan = LiteralLineSearcher.CreateRegexSearchPlan(
            patterns,
            asciiCaseInsensitive: false);
        var sink = new CapturingMatchLineSink();

        cancellationToken.ThrowIfCancellationRequested();
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

        cancellationToken.ThrowIfCancellationRequested();
        Assert.AreNotEqual(RegexPrefilterKind.None, plan.Matcher.PrefilterKind);
        Assert.IsTrue(matched);
        Assert.AreEqual(1UL, sink.Matches);
        Assert.AreEqual(350_001, sink.LineNumber);
        Assert.AreSequenceEqual("CreateAhoCorasick"u8.ToArray(), sink.Match);
        Assert.AreEqual(1, matchingLines);
        Assert.AreEqual(1, matches);

        cancellationToken.ThrowIfCancellationRequested();
        var rejectedSink = new CapturingMatchLineSink();
        Assert.IsFalse(LiteralLineSearcher.SearchMatchLinesWithRegexPlan(
            "CreateButNotAhoCorasick\n"u8,
            patterns,
            plan,
            ref rejectedSink));
        Assert.AreEqual(0UL, rejectedSink.Matches);
    }
}
