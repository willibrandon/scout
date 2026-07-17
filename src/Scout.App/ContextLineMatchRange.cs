namespace Scout;

/// <summary>
/// Identifies a contiguous range of retained context matches.
/// </summary>
/// <param name="start">The zero-based start in the shared match collection.</param>
/// <param name="count">The number of matches in the range.</param>
internal readonly struct ContextLineMatchRange(int start, int count)
{
    /// <summary>
    /// Gets the zero-based start in the shared match collection.
    /// </summary>
    public int Start { get; } = start;

    /// <summary>
    /// Gets the number of matches in the range.
    /// </summary>
    public int Count { get; } = count;
}
