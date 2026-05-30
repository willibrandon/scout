namespace Scout;

/// <summary>
/// Identifies the case-matching mode selected by low-level command-line flags.
/// </summary>
public enum CliCaseMode
{
    /// <summary>
    /// Matches bytes exactly.
    /// </summary>
    Sensitive,

    /// <summary>
    /// Matches ASCII letters without regard to case.
    /// </summary>
    Insensitive,

    /// <summary>
    /// Uses case-insensitive matching unless the pattern contains an ASCII uppercase letter.
    /// </summary>
    Smart,
}
