namespace Scout.IO.Ignore;

/// <summary>
/// Carries the evaluated state needed to visit or descend into one entry.
/// </summary>
/// <param name="entry">The evaluated directory entry.</param>
/// <param name="ignoreStack">The parent ignore stack used to evaluate the entry.</param>
/// <param name="shouldYield">Whether the entry should be yielded to the visitor.</param>
/// <param name="shouldRecurse">Whether traversal should descend into the entry.</param>
internal readonly struct WalkEntryState(
    DirEntry entry,
    IgnoreStack ignoreStack,
    bool shouldYield,
    bool shouldRecurse)
{
    /// <summary>
    /// Gets the evaluated directory entry.
    /// </summary>
    public DirEntry Entry { get; } = entry;

    /// <summary>
    /// Gets the parent ignore stack used to evaluate the entry.
    /// </summary>
    public IgnoreStack IgnoreStack { get; } = ignoreStack;

    /// <summary>
    /// Gets a value indicating whether the entry should be yielded to the visitor.
    /// </summary>
    public bool ShouldYield { get; } = shouldYield;

    /// <summary>
    /// Gets a value indicating whether traversal should descend into the entry.
    /// </summary>
    public bool ShouldRecurse { get; } = shouldRecurse;
}
