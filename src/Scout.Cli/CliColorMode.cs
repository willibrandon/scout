namespace Scout;

/// <summary>
/// Defines when colored output should be emitted.
/// </summary>
public enum CliColorMode
{
    /// <summary>
    /// Colors are emitted when stdout is connected to a terminal.
    /// </summary>
    Auto,

    /// <summary>
    /// ANSI colors are always emitted.
    /// </summary>
    Always,

    /// <summary>
    /// ANSI colors are always emitted.
    /// </summary>
    Ansi,

    /// <summary>
    /// Colors are never emitted.
    /// </summary>
    Never,
}
