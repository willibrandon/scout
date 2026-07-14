using System.Text;

namespace Scout;

/// <summary>
/// Verifies context-search matching and planning behavior.
/// </summary>
public sealed class ContextSearchOperationsTests
{
    /// <summary>
    /// Verifies whole-buffer context matching does not treat the line terminator as match content.
    /// </summary>
    [Fact]
    public void BuildLinesExcludesLineTerminatorFromRegexMatching()
    {
        List<ContextLineInfo> lines = ContextSearchOperations.BuildLines(
            "foo\n"u8.ToArray(),
            ["foo[^x]"u8.ToArray()],
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            crlf: false,
            nullData: false);

        ContextLineInfo line = Assert.Single(lines);
        Assert.False(line.SelectedMatch);
        Assert.False(line.OriginalMatch);
    }

    /// <summary>
    /// Verifies whole-buffer context matching selects a zero-width match on an unterminated final line.
    /// </summary>
    [Fact]
    public void BuildLinesPreservesZeroWidthMatchOnUnterminatedFinalLine()
    {
        List<ContextLineInfo> lines = ContextSearchOperations.BuildLines(
            "value"u8.ToArray(),
            ["$"u8.ToArray()],
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            crlf: false,
            nullData: false);

        ContextLineInfo line = Assert.Single(lines);
        Assert.True(line.SelectedMatch);
        Assert.True(line.OriginalMatch);
        Assert.Equal(6, line.MatchColumn);
        Assert.Equal(6, line.ContextColumn);
    }

    /// <summary>
    /// Verifies mixed alternations scan a large context buffer through conservative whole-buffer candidates.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void BuildLinesUsesWholeBufferMixedAlternationCandidates()
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

        List<ContextLineInfo> lines = ContextSearchOperations.BuildLines(
            haystack,
            patterns,
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            crlf: false,
            nullData: false);

        Assert.Equal(350_001, lines.Count);
        Assert.DoesNotContain(lines.Take(350_000), line => line.SelectedMatch);
        Assert.True(lines[^1].SelectedMatch);
        Assert.Equal(1, lines[^1].MatchColumn);
    }
}
