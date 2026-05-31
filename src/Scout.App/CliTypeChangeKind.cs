namespace Scout;

/// <summary>
/// Identifies the kind of file type change requested on the command line.
/// </summary>
public enum CliTypeChangeKind
{
    /// <summary>
    /// Selects a file type for whitelist filtering.
    /// </summary>
    Select,

    /// <summary>
    /// Negates a file type for blacklist filtering.
    /// </summary>
    Negate,

    /// <summary>
    /// Adds a file type definition.
    /// </summary>
    Add,

    /// <summary>
    /// Clears a file type definition.
    /// </summary>
    Clear,
}
