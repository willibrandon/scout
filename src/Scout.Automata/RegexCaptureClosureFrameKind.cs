namespace Scout;

/// <summary>
/// Identifies the operation represented by a capture epsilon-closure frame.
/// </summary>
internal enum RegexCaptureClosureFrameKind
{
    /// <summary>
    /// Explores an NFA state.
    /// </summary>
    ExploreState,

    /// <summary>
    /// Restores a capture slot after a prioritized branch has been explored.
    /// </summary>
    RestoreSlot,
}
