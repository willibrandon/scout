namespace Scout;

/// <summary>
/// Controls parallel recursive traversal after a directory entry is visited.
/// </summary>
public enum WalkState
{
    /// <summary>
    /// Continue traversal normally.
    /// </summary>
    Continue,

    /// <summary>
    /// Skip descending into the visited entry when it is a directory.
    /// </summary>
    Skip,

    /// <summary>
    /// Quit traversal as soon as workers observe the request.
    /// </summary>
    Quit,
}
