namespace Scout;

/// <summary>
/// Represents one operation in the iterative capture epsilon-closure traversal.
/// </summary>
/// <param name="kind">The frame operation.</param>
/// <param name="state">The NFA state to explore.</param>
/// <param name="slot">The capture slot to restore.</param>
/// <param name="previousValue">The capture slot value to restore.</param>
internal readonly struct RegexCaptureClosureFrame(
    RegexCaptureClosureFrameKind kind,
    int state,
    int slot,
    int previousValue)
{
    /// <summary>
    /// Gets the frame operation.
    /// </summary>
    public RegexCaptureClosureFrameKind Kind { get; } = kind;

    /// <summary>
    /// Gets the NFA state to explore.
    /// </summary>
    public int State { get; } = state;

    /// <summary>
    /// Gets the capture slot to restore.
    /// </summary>
    public int Slot { get; } = slot;

    /// <summary>
    /// Gets the capture slot value to restore.
    /// </summary>
    public int PreviousValue { get; } = previousValue;

    /// <summary>
    /// Creates a frame that explores an NFA state.
    /// </summary>
    /// <param name="state">The NFA state to explore.</param>
    /// <returns>The new exploration frame.</returns>
    public static RegexCaptureClosureFrame Explore(int state)
    {
        return new RegexCaptureClosureFrame(
            RegexCaptureClosureFrameKind.ExploreState,
            state,
            slot: -1,
            previousValue: -1);
    }

    /// <summary>
    /// Creates a frame that restores a capture slot.
    /// </summary>
    /// <param name="slot">The capture slot to restore.</param>
    /// <param name="previousValue">The previous capture slot value.</param>
    /// <returns>The new restoration frame.</returns>
    public static RegexCaptureClosureFrame Restore(int slot, int previousValue)
    {
        return new RegexCaptureClosureFrame(
            RegexCaptureClosureFrameKind.RestoreSlot,
            state: -1,
            slot,
            previousValue);
    }
}
