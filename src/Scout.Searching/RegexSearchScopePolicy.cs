namespace Scout;

/// <summary>
/// Selects how parsed regex syntax determines the effective search scope for one operation.
/// </summary>
internal enum RegexSearchScopePolicy
{
    /// <summary>
    /// Searches one record at a time.
    /// </summary>
    Records,

    /// <summary>
    /// Searches the whole input when standard multiline semantics require record terminators or the original haystack.
    /// </summary>
    StandardMultiline,

    /// <summary>
    /// Searches the whole input when JSON multiline reporting requires stable match positions across records.
    /// </summary>
    JsonMultiline,
}
