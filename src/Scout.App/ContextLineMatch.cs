namespace Scout;

/// <summary>
/// Describes one authoritative match retained for context output replay.
/// </summary>
/// <param name="start">The zero-based match start in its containing line.</param>
/// <param name="column">The one-based match byte column in its containing line.</param>
/// <param name="length">The match length in bytes.</param>
internal readonly struct ContextLineMatch(
    int start,
    long column,
    int length)
{
    /// <summary>
    /// Gets the zero-based match start in its containing line.
    /// </summary>
    public int Start { get; } = start;

    /// <summary>
    /// Gets the one-based match byte column in its containing line.
    /// </summary>
    public long Column { get; } = column;

    /// <summary>
    /// Gets the match length in bytes.
    /// </summary>
    public int Length { get; } = length;
}
