namespace Scout;

/// <summary>
/// Identifies the requested memory-map search mode.
/// </summary>
public enum CliMmapMode
{
    /// <summary>
    /// Let Scout choose whether to use memory maps.
    /// </summary>
    Auto,

    /// <summary>
    /// Try to use memory maps whenever possible.
    /// </summary>
    AlwaysTryMmap,

    /// <summary>
    /// Never use memory maps.
    /// </summary>
    Never,
}
