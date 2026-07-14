namespace Scout;

/// <summary>
/// Identifies a capture-aware NFA thread by state and byte position.
/// </summary>
/// <param name="state">The NFA state index.</param>
/// <param name="position">The byte position associated with the state.</param>
internal readonly struct CaptureThread(int state, int position)
{
    /// <summary>
    /// Gets the NFA state index.
    /// </summary>
    public int State { get; } = state;

    /// <summary>
    /// Gets the byte position associated with the state.
    /// </summary>
    public int Position { get; } = position;
}
