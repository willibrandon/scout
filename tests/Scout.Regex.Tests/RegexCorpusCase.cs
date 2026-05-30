using System.Collections.Generic;

namespace Scout;

internal sealed class RegexCorpusCase
{
    public RegexCorpusCase(
        string name,
        IReadOnlyList<string> patterns,
        string haystack,
        IReadOnlyList<RegexMatch> expectedMatches,
        int? matchLimit)
    {
        Name = name;
        Patterns = patterns;
        Haystack = haystack;
        ExpectedMatches = expectedMatches;
        MatchLimit = matchLimit;
    }

    public string Name { get; }

    public IReadOnlyList<string> Patterns { get; }

    public string Haystack { get; }

    public IReadOnlyList<RegexMatch> ExpectedMatches { get; }

    public int? MatchLimit { get; }
}
