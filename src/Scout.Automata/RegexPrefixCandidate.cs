namespace Scout;

internal readonly struct RegexPrefixCandidate
{
    public RegexPrefixCandidate(byte[] bytes, bool sealedPrefix, int preferredPrefixBytes)
        : this(bytes, sealedPrefix, preferredPrefixBytes, caseInsensitive: false, unicodeClasses: false)
    {
    }

    public RegexPrefixCandidate(
        byte[] bytes,
        bool sealedPrefix,
        int preferredPrefixBytes,
        bool caseInsensitive,
        bool unicodeClasses)
    {
        Bytes = bytes;
        Sealed = sealedPrefix || bytes.Length >= preferredPrefixBytes;
        CaseInsensitive = caseInsensitive;
        UnicodeClasses = unicodeClasses;
    }

    public byte[] Bytes { get; }

    public bool Sealed { get; }

    public bool CaseInsensitive { get; }

    public bool UnicodeClasses { get; }

    public RegexPrefixCandidate Merge(RegexPrefixCandidate other)
    {
        return new RegexPrefixCandidate(
            Bytes,
            Sealed || other.Sealed,
            int.MaxValue,
            CaseInsensitive || other.CaseInsensitive,
            UnicodeClasses || other.UnicodeClasses);
    }
}
