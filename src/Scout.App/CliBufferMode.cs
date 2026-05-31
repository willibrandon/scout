namespace Scout;

/// <summary>
/// Identifies the requested stdout buffering mode.
/// </summary>
public enum CliBufferMode
{
    /// <summary>
    /// Use ripgrep's automatic tty-sensitive buffering choice.
    /// </summary>
    Auto,

    /// <summary>
    /// Force line buffering.
    /// </summary>
    Line,

    /// <summary>
    /// Force block buffering.
    /// </summary>
    Block,
}
