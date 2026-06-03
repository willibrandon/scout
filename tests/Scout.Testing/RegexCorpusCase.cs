
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
        bool earliest,
        bool overlapping,
        bool matchKindAll,
        bool caseInsensitive,
        bool utf8,
        bool unicodeClasses,
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
        Earliest = earliest;
        Overlapping = overlapping;
        MatchKindAll = matchKindAll;
        CaseInsensitive = caseInsensitive;
        Utf8 = utf8;
        UnicodeClasses = unicodeClasses;
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

    public bool Earliest { get; }

    public bool Overlapping { get; }

    public bool MatchKindAll { get; }

    public bool CaseInsensitive { get; }

    public bool Utf8 { get; }

    public bool UnicodeClasses { get; }

    public bool Compiles { get; }
}
