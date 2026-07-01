namespace Scout.Text.Regex;

/// <summary>
/// Selects the regex engine family used when compiling a byte regex.
/// </summary>
public enum ByteRegexEngineMode
{
    /// <summary>
    /// Uses Scout's full optimized regex dispatch.
    /// </summary>
    Optimized,

    /// <summary>
    /// Uses general-purpose structural fast paths without corpus-specific recognizers.
    /// </summary>
    General,

    /// <summary>
    /// Uses the core automata engines without recognizer fast paths or search guards.
    /// </summary>
    AutomataOnly,
}
