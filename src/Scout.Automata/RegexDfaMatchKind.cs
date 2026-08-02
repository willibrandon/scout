namespace Scout;

/// <summary>
/// Selects whether a DFA retains every accepting path or applies leftmost-first priority.
/// </summary>
internal enum RegexDfaMatchKind
{
    /// <summary>
    /// Retains only paths that can still participate in the first prioritized match.
    /// </summary>
    LeftmostFirst,

    /// <summary>
    /// Retains every viable path so callers can inspect all accepted bounds.
    /// </summary>
    All,
}
