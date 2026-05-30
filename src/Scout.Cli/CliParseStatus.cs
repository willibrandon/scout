namespace Scout;

/// <summary>
/// Identifies the result category of low-level CLI parsing.
/// </summary>
public enum CliParseStatus
{
    /// <summary>
    /// Parsing completed with low-level arguments.
    /// </summary>
    Ok,

    /// <summary>
    /// Parsing found a special mode that should short-circuit normal execution.
    /// </summary>
    Special,

    /// <summary>
    /// Parsing failed with a user-facing error.
    /// </summary>
    Error,
}
