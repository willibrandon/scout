namespace Scout;

/// <summary>
/// Identifies the search output mode selected by low-level command-line flags.
/// </summary>
public enum CliSearchMode
{
    /// <summary>
    /// Prints each matching line.
    /// </summary>
    Standard,

    /// <summary>
    /// Prints files that would be searched.
    /// </summary>
    Files,

    /// <summary>
    /// Prints the number of matching lines.
    /// </summary>
    Count,

    /// <summary>
    /// Prints the number of non-overlapping matches.
    /// </summary>
    CountMatches,

    /// <summary>
    /// Prints paths with at least one match.
    /// </summary>
    FilesWithMatches,

    /// <summary>
    /// Prints paths with no matches.
    /// </summary>
    FilesWithoutMatch,

    /// <summary>
    /// Prints search results as JSON Lines.
    /// </summary>
    Json,
}
