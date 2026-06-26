namespace Scout;

/// <summary>
/// Selects which regex-specialized engines are eligible during compilation.
/// </summary>
public enum RegexSpecializationMode
{
    /// <summary>
    /// Uses the full default Scout regex dispatch.
    /// </summary>
    Default,

    /// <summary>
    /// Disables domain, benchmark-family, and corpus-specific recognizers while keeping structural fast paths.
    /// </summary>
    General,

    /// <summary>
    /// Disables recognizer fast paths and search guards, leaving only the core automata engines.
    /// </summary>
    Fallback,
}
