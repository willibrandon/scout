namespace Scout;

/// <summary>
/// Identifies one authoritative scalar-atom lowering under its effective semantic options.
/// </summary>
/// <param name="Atom">The parsed atom whose scalar semantics are lowered.</param>
/// <param name="CaseInsensitive">Whether matching ignores case.</param>
/// <param name="DotMatchesNewline">Whether dot normally consumes line terminators.</param>
/// <param name="Crlf">Whether CR and LF form the line-terminator family.</param>
/// <param name="LineTerminator">The configured non-CRLF line terminator.</param>
/// <param name="Utf8">Whether matching observes UTF-8 scalar boundaries.</param>
/// <param name="UnicodeClasses">Whether character classes use Unicode semantics.</param>
/// <param name="ExcludeLineTerminators">Whether consuming atoms exclude record terminators.</param>
/// <param name="ExcludeCrLf">Whether exclusion treats CR and LF as one family.</param>
/// <param name="ExcludedLineTerminator">The record byte excluded from consuming atoms.</param>
internal readonly record struct RegexScalarAtomPlanCacheKey(
    RegexAtomNode Atom,
    bool CaseInsensitive,
    bool DotMatchesNewline,
    bool Crlf,
    byte LineTerminator,
    bool Utf8,
    bool UnicodeClasses,
    bool ExcludeLineTerminators,
    bool ExcludeCrLf,
    byte ExcludedLineTerminator)
{
    /// <summary>
    /// Creates a key from one parsed atom and its effective options.
    /// </summary>
    /// <param name="atom">The parsed atom.</param>
    /// <param name="options">The effective compile options.</param>
    /// <returns>The semantic plan key.</returns>
    internal static RegexScalarAtomPlanCacheKey Create(
        RegexAtomNode atom,
        RegexCompileOptions options)
    {
        return new RegexScalarAtomPlanCacheKey(
            atom,
            options.CaseInsensitive,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            options.Utf8,
            options.UnicodeClasses,
            options.ExcludeLineTerminators,
            options.ExcludeCrLf,
            options.ExcludedLineTerminator);
    }
}
