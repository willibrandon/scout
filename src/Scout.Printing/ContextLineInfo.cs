namespace Scout;

/// <summary>
/// Describes one input line and its selected and original match state.
/// </summary>
internal readonly struct ContextLineInfo(
    int start,
    int length,
    long lineNumber,
    bool selectedMatch,
    long matchColumn,
    bool originalMatch,
    long contextColumn)
{
    /// <summary>
    /// Gets the zero-based byte offset of the line.
    /// </summary>
    public int Start { get; } = start;

    /// <summary>
    /// Gets the line length in bytes, including its terminator when present.
    /// </summary>
    public int Length { get; } = length;

    /// <summary>
    /// Gets the one-based line number.
    /// </summary>
    public long LineNumber { get; } = lineNumber;

    /// <summary>
    /// Gets a value indicating whether the line is selected after inversion is applied.
    /// </summary>
    public bool SelectedMatch { get; } = selectedMatch;

    /// <summary>
    /// Gets the one-based selected-match column, or zero when none applies.
    /// </summary>
    public long MatchColumn { get; } = matchColumn;

    /// <summary>
    /// Gets a value indicating whether the line matched before inversion was applied.
    /// </summary>
    public bool OriginalMatch { get; } = originalMatch;

    /// <summary>
    /// Gets the one-based original-match column, or zero when the line did not match.
    /// </summary>
    public long ContextColumn { get; } = contextColumn;
}
