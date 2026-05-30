namespace Scout;

/// <summary>
/// Identifies a ripgrep sort criterion.
/// </summary>
public enum CliSortKind
{
    /// <summary>
    /// Sorts by file path.
    /// </summary>
    Path,

    /// <summary>
    /// Sorts by last modified time.
    /// </summary>
    LastModified,

    /// <summary>
    /// Sorts by last accessed time.
    /// </summary>
    LastAccessed,

    /// <summary>
    /// Sorts by creation time.
    /// </summary>
    Created,
}
