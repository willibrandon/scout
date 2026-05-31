namespace Scout;

/// <summary>
/// Identifies command-line modes that short-circuit normal search execution.
/// </summary>
public enum CliSpecialMode
{
    /// <summary>
    /// Print condensed help text from <c>-h</c>.
    /// </summary>
    HelpShort,

    /// <summary>
    /// Print full help text from <c>--help</c>.
    /// </summary>
    HelpLong,

    /// <summary>
    /// Print short version text from <c>-V</c>.
    /// </summary>
    VersionShort,

    /// <summary>
    /// Print long version text from <c>--version</c>.
    /// </summary>
    VersionLong,

    /// <summary>
    /// Print PCRE2 version text from <c>--pcre2-version</c>.
    /// </summary>
    Pcre2Version,
}
