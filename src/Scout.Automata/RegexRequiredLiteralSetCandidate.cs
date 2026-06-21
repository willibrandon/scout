namespace Scout;

internal readonly struct RegexRequiredLiteralSetCandidate
{
    public RegexRequiredLiteralSetCandidate(byte[][] literals, int maxLookBehind)
        : this(literals, maxLookBehind, caseInsensitive: false, unicodeClasses: false)
    {
    }

    public RegexRequiredLiteralSetCandidate(
        byte[][] literals,
        int maxLookBehind,
        bool caseInsensitive,
        bool unicodeClasses)
    {
        Literals = literals;
        MaxLookBehind = maxLookBehind;
        CaseInsensitive = caseInsensitive;
        UnicodeClasses = unicodeClasses;
    }

    public byte[][] Literals { get; }

    public int MaxLookBehind { get; }

    public bool CaseInsensitive { get; }

    public bool UnicodeClasses { get; }
}
