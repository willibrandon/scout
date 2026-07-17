using System.Text;

namespace Scout;

/// <summary>
/// Verifies conservative prefilters cannot change authoritative regex results.
/// </summary>
public sealed class RegexAuthoritativePrefilterEquivalenceTests
{
    /// <summary>
    /// Verifies enabling a syntax-derived prefilter preserves ordered spans, counts, and captures.
    /// </summary>
    [Fact]
    public void SyntaxDerivedPrefilterPreservesAuthoritativeResults()
    {
        AssertEquivalent(
            @"(?<word>GeneratedRecord)(?<suffix>[0-9]+)",
            "GeneratedRecords GeneratedRecord12 GeneratedRecordX GeneratedRecord34");
        AssertEquivalent(
            "CollectExtensionSuffixCandidates|extensionSuffixPatterns|CreateAhoCorasick|GlobSet[.]cs",
            "CreateButNotAhoCorasick CreateAhoCorasick GlobSet-cs GlobSet.cs");
        AssertEquivalent(
            @"prefix[a-z]+suffix|prefix[0-9]+suffix",
            "prefix---suffix prefixabcX prefixabcsuffix prefix123suffix");
    }

    private static void AssertEquivalent(string pattern, string haystack)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern));
        RegexCompileOptions options = CreateOptions();
        var prefiltered = RegexAutomaton.CompileParsedAuthoritative(
            tree,
            options,
            compilePrefilter: true);
        var unfiltered = RegexAutomaton.CompileParsedAuthoritative(
            tree,
            options,
            compilePrefilter: false);
        byte[] bytes = Encoding.UTF8.GetBytes(haystack);

        Assert.NotEqual(RegexPrefilterKind.None, prefiltered.PrefilterKind);
        Assert.Equal(RegexPrefilterKind.None, unfiltered.PrefilterKind);
        Assert.Equal(FindAll(unfiltered, bytes), FindAll(prefiltered, bytes));
        Assert.Equal(unfiltered.CountMatches(bytes), prefiltered.CountMatches(bytes));
        Assert.Equal(unfiltered.SumMatchSpans(bytes), prefiltered.SumMatchSpans(bytes));
        AssertCapturesEqual(
            unfiltered.FindCaptures(bytes),
            prefiltered.FindCaptures(bytes),
            tree.CaptureCount);
    }

    private static RegexCompileOptions CreateOptions()
    {
        return new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: true,
            dotMatchesNewline: false,
            crlf: false,
            lineTerminator: (byte)'\n',
            utf8: false,
            unicodeClasses: true,
            specializationMode: RegexSpecializationMode.General);
    }

    private static RegexMatch[] FindAll(
        RegexAutomaton automaton,
        ReadOnlySpan<byte> haystack)
    {
        var matches = new List<RegexMatch>();
        int startAt = 0;
        while (startAt <= haystack.Length)
        {
            RegexMatch? match = automaton.Find(haystack, startAt);
            if (!match.HasValue)
            {
                break;
            }

            matches.Add(match.Value);
            startAt = match.Value.Length == 0
                ? match.Value.Start + 1
                : match.Value.End;
        }

        return matches.ToArray();
    }

    private static void AssertCapturesEqual(
        RegexCaptures? expected,
        RegexCaptures? actual,
        int captureCount)
    {
        Assert.Equal(expected?.Match, actual?.Match);
        for (int index = 0; index <= captureCount; index++)
        {
            Assert.Equal(expected?.GetGroup(index), actual?.GetGroup(index));
        }
    }
}
