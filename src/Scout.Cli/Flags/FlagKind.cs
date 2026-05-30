namespace Scout.Flags;

/// <summary>
/// Identifies how a command-line flag consumes arguments.
/// </summary>
internal enum FlagKind
{
    /// <summary>
    /// The flag consumes no value.
    /// </summary>
    Switch,

    /// <summary>
    /// The flag consumes a required value.
    /// </summary>
    Value,

    /// <summary>
    /// The flag exits normal parsing and selects a special mode.
    /// </summary>
    Special,
}
