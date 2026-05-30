namespace Scout.Flags;

/// <summary>
/// Identifies the help and completion category for a command-line flag.
/// </summary>
internal enum FlagCategory
{
    /// <summary>
    /// Search mode and traversal behavior.
    /// </summary>
    Search,

    /// <summary>
    /// Pattern matching behavior.
    /// </summary>
    Matching,

    /// <summary>
    /// Regex engine behavior.
    /// </summary>
    Regex,

    /// <summary>
    /// Output formatting behavior.
    /// </summary>
    Output,

    /// <summary>
    /// Binary file handling behavior.
    /// </summary>
    Binary,

    /// <summary>
    /// Diagnostic and reporting behavior.
    /// </summary>
    Diagnostics,
}
