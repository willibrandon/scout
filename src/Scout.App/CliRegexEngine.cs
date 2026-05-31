namespace Scout;

/// <summary>
/// Identifies the requested regex engine.
/// </summary>
public enum CliRegexEngine
{
    /// <summary>
    /// Use the default finite-automata regex engine.
    /// </summary>
    Default,

    /// <summary>
    /// Automatically select between the default engine and PCRE2.
    /// </summary>
    Auto,

    /// <summary>
    /// Force PCRE2.
    /// </summary>
    Pcre2,
}
