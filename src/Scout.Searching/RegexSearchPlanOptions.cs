namespace Scout;

/// <summary>
/// Describes the semantic options used to build an authoritative regex search plan.
/// </summary>
/// <param name="asciiCaseInsensitive">Whether literals and classes use ASCII case-insensitive matching.</param>
/// <param name="lineRegexp">Whether the expression must match a complete record.</param>
/// <param name="wordRegexp">Whether matches must have word boundaries.</param>
/// <param name="crlf">Whether CRLF-aware matching is enabled.</param>
/// <param name="nullData">Whether NUL terminates records.</param>
/// <param name="multiline">Whether matches may span records.</param>
/// <param name="multilineDotall">Whether dot matches record terminators in multiline mode.</param>
/// <param name="preserveCrlfCarriageReturn">Whether line selection preserves CR while still excluding LF.</param>
internal readonly struct RegexSearchPlanOptions(
    bool asciiCaseInsensitive,
    bool lineRegexp = false,
    bool wordRegexp = false,
    bool crlf = false,
    bool nullData = false,
    bool multiline = false,
    bool multilineDotall = false,
    bool preserveCrlfCarriageReturn = false)
{
    /// <summary>
    /// Gets a value indicating whether literals and classes use ASCII case-insensitive matching.
    /// </summary>
    internal bool AsciiCaseInsensitive { get; } = asciiCaseInsensitive;

    /// <summary>
    /// Gets a value indicating whether the expression must match a complete record.
    /// </summary>
    internal bool LineRegexp { get; } = lineRegexp;

    /// <summary>
    /// Gets a value indicating whether matches must have word boundaries.
    /// </summary>
    internal bool WordRegexp { get; } = wordRegexp;

    /// <summary>
    /// Gets a value indicating whether CRLF-aware matching is enabled.
    /// </summary>
    internal bool Crlf { get; } = crlf;

    /// <summary>
    /// Gets a value indicating whether NUL terminates records.
    /// </summary>
    internal bool NullData { get; } = nullData;

    /// <summary>
    /// Gets a value indicating whether matches may span records.
    /// </summary>
    internal bool Multiline { get; } = multiline;

    /// <summary>
    /// Gets a value indicating whether dot matches record terminators in multiline mode.
    /// </summary>
    internal bool MultilineDotall { get; } = multilineDotall;

    /// <summary>
    /// Gets a value indicating whether line selection preserves CR while still excluding LF.
    /// </summary>
    internal bool PreserveCrlfCarriageReturn { get; } = preserveCrlfCarriageReturn;

    /// <summary>
    /// Determines whether these options are identical to supplied search semantics.
    /// </summary>
    /// <param name="candidateAsciiCaseInsensitive">Whether matching is ASCII case-insensitive.</param>
    /// <param name="candidateLineRegexp">Whether the expression must match a complete record.</param>
    /// <param name="candidateWordRegexp">Whether matches must have word boundaries.</param>
    /// <param name="candidateCrlf">Whether CRLF-aware matching is enabled.</param>
    /// <param name="candidateNullData">Whether NUL terminates records.</param>
    /// <param name="candidateMultiline">Whether matches may span records.</param>
    /// <param name="candidateMultilineDotall">Whether dot matches record terminators in multiline mode.</param>
    /// <returns><see langword="true" /> when all semantic options are equal.</returns>
    internal bool IsCompatible(
        bool candidateAsciiCaseInsensitive,
        bool candidateLineRegexp,
        bool candidateWordRegexp,
        bool candidateCrlf,
        bool candidateNullData,
        bool candidateMultiline,
        bool candidateMultilineDotall)
    {
        return AsciiCaseInsensitive == candidateAsciiCaseInsensitive &&
            LineRegexp == candidateLineRegexp &&
            WordRegexp == candidateWordRegexp &&
            Crlf == candidateCrlf &&
            NullData == candidateNullData &&
            Multiline == candidateMultiline &&
            MultilineDotall == candidateMultilineDotall;
    }
}
