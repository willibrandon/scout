using System.Collections.Generic;

namespace Scout;

internal sealed class RegexCorpusCase
{
    public RegexCorpusCase(string name, string pattern, string haystack, IReadOnlyList<RegexMatch> expectedMatches, int? matchLimit)
    {
        Name = name;
        Pattern = pattern;
        Haystack = haystack;
        ExpectedMatches = expectedMatches;
        MatchLimit = matchLimit;
    }

    public string Name { get; }

    public string Pattern { get; }

    public string Haystack { get; }

    public IReadOnlyList<RegexMatch> ExpectedMatches { get; }

    public int? MatchLimit { get; }
}
