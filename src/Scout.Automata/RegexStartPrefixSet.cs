namespace Scout;

internal readonly struct RegexStartPrefixSet
{
    public RegexStartPrefixSet(byte[][] prefixes, bool caseInsensitive, bool unicodeCaseInsensitive)
    {
        Prefixes = prefixes;
        CaseInsensitive = caseInsensitive;
        UnicodeCaseInsensitive = unicodeCaseInsensitive;
    }

    public byte[][] Prefixes { get; }

    public bool CaseInsensitive { get; }

    public bool UnicodeCaseInsensitive { get; }
}
