namespace Scout;

/// <summary>
/// Identifies a reusable atom state within one NFA compilation.
/// </summary>
/// <param name="StateKind">The NFA state operation.</param>
/// <param name="AtomKind">The syntax atom kind.</param>
/// <param name="Value">The normalized atom payload.</param>
/// <param name="CaseInsensitive">Whether the atom ignores case.</param>
/// <param name="MultiLine">Whether anchors use multiline semantics.</param>
/// <param name="DotMatchesNewline">Whether dot normally consumes line terminators.</param>
/// <param name="Crlf">Whether CR and LF form the line-terminator family.</param>
/// <param name="LineTerminator">The non-CRLF line terminator.</param>
/// <param name="Utf8">Whether the state observes UTF-8 scalar boundaries.</param>
/// <param name="UnicodeClasses">Whether classes use Unicode semantics.</param>
/// <param name="Next">The primary successor state.</param>
/// <param name="Alternative">The alternative successor state.</param>
/// <param name="ExcludeLineTerminators">Whether consuming atoms exclude configured record terminators.</param>
/// <param name="ExcludeCrLf">Whether exclusion treats CR and LF as one record-terminator family.</param>
/// <param name="ExcludedLineTerminator">The record byte excluded from consuming atoms.</param>
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
    int Alternative,
    bool ExcludeLineTerminators,
    bool ExcludeCrLf,
    byte ExcludedLineTerminator)
{
    /// <summary>
    /// Creates an atom-state cache key from a byte payload.
    /// </summary>
    /// <param name="stateKind">The NFA state operation.</param>
    /// <param name="atomKind">The syntax atom kind.</param>
    /// <param name="value">The atom payload.</param>
    /// <param name="caseInsensitive">Whether the atom ignores case.</param>
    /// <param name="multiLine">Whether anchors use multiline semantics.</param>
    /// <param name="dotMatchesNewline">Whether dot normally consumes line terminators.</param>
    /// <param name="crlf">Whether CR and LF form the line-terminator family.</param>
    /// <param name="lineTerminator">The non-CRLF line terminator.</param>
    /// <param name="utf8">Whether the state observes UTF-8 scalar boundaries.</param>
    /// <param name="unicodeClasses">Whether classes use Unicode semantics.</param>
    /// <param name="next">The primary successor state.</param>
    /// <param name="alternative">The alternative successor state.</param>
    /// <param name="excludeLineTerminators">Whether consuming atoms exclude configured record terminators.</param>
    /// <param name="excludeCrLf">Whether exclusion treats CR and LF as one record-terminator family.</param>
    /// <param name="excludedLineTerminator">The record byte excluded from consuming atoms.</param>
    /// <returns>The cache key.</returns>
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
        int alternative,
        bool excludeLineTerminators,
        bool excludeCrLf,
        byte excludedLineTerminator)
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
            alternative,
            excludeLineTerminators,
            excludeCrLf,
            excludedLineTerminator);
    }
}
