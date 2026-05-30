namespace Scout;

/// <summary>
/// Identifies the diagnostic logging verbosity requested by the command line.
/// </summary>
public enum CliLoggingMode
{
    /// <summary>
    /// Emit ripgrep debug diagnostics.
    /// </summary>
    Debug,

    /// <summary>
    /// Emit ripgrep trace diagnostics.
    /// </summary>
    Trace,
}
