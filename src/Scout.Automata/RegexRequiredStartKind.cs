namespace Scout;

/// <summary>
/// Identifies a position constraint shared by every possible regex match start.
/// </summary>
internal enum RegexRequiredStartKind
{
    /// <summary>
    /// Allows a match to start at any legal byte boundary.
    /// </summary>
    None,

    /// <summary>
    /// Requires a match to start at the beginning of the haystack.
    /// </summary>
    Text,

    /// <summary>
    /// Requires a match to start at the beginning of the haystack or immediately after a line terminator.
    /// </summary>
    Line,
}
