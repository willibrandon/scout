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
    /// Uses general structural specializations but skips narrow benchmark-family recognizers.
    /// </summary>
    General,

    /// <summary>
    /// Disables recognizer fast paths and search guards, leaving only the core automata engines.
    /// </summary>
    Fallback,
}
