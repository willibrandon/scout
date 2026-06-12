namespace Scout;

internal readonly record struct RegexNfaAtomCacheKey(
    RegexNfaStateKind StateKind,
    RegexSyntaxKind AtomKind,
    string Value,
    bool CaseInsensitive,
    bool MultiLine,
    bool DotMatchesNewline,
    bool Crlf,
    byte LineTerminator,
    bool Utf8,
    bool UnicodeClasses,
    int Next,
    int Alternative)
{
    public static RegexNfaAtomCacheKey Create(
        RegexNfaStateKind stateKind,
        RegexSyntaxKind atomKind,
        ReadOnlySpan<byte> value,
        bool caseInsensitive,
        bool multiLine,
        bool dotMatchesNewline,
        bool crlf,
        byte lineTerminator,
        bool utf8,
        bool unicodeClasses,
        int next,
        int alternative)
    {
        return new RegexNfaAtomCacheKey(
            stateKind,
            atomKind,
            value.IsEmpty ? string.Empty : Convert.ToHexString(value),
            caseInsensitive,
            multiLine,
            dotMatchesNewline,
            crlf,
            lineTerminator,
            utf8,
            unicodeClasses,
            next,
            alternative);
    }
}
