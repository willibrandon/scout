using System.Collections.Generic;

namespace Scout;

internal sealed class RegexCorpusCase
{
    public RegexCorpusCase(
        string name,
        IReadOnlyList<byte[]> patterns,
        byte[] haystack,
        IReadOnlyList<RegexMatch> expectedMatches,
        int? matchLimit,
        byte lineTerminator,
        int boundsStart,
        int boundsEnd,
        bool anchored,
        bool caseInsensitive,
        bool utf8,
        bool compiles)
    {
        Name = name;
        Patterns = patterns;
        Haystack = haystack;
        ExpectedMatches = expectedMatches;
        MatchLimit = matchLimit;
        LineTerminator = lineTerminator;
        BoundsStart = boundsStart;
        BoundsEnd = boundsEnd;
        Anchored = anchored;
        CaseInsensitive = caseInsensitive;
        Utf8 = utf8;
        Compiles = compiles;
    }

    public string Name { get; }

    public IReadOnlyList<byte[]> Patterns { get; }

    public byte[] Haystack { get; }

    public IReadOnlyList<RegexMatch> ExpectedMatches { get; }

    public int? MatchLimit { get; }

    public byte LineTerminator { get; }

    public int BoundsStart { get; }

    public int BoundsEnd { get; }

    public bool Anchored { get; }

    public bool CaseInsensitive { get; }

    public bool Utf8 { get; }

    public bool Compiles { get; }
}
