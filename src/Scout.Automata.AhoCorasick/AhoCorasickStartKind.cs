namespace Scout;

/// <summary>
/// Defines which anchored modes an Aho-Corasick automaton supports.
/// </summary>
public enum AhoCorasickStartKind
{
    /// <summary>
    /// Supports both unanchored and anchored searches.
    /// </summary>
    Both,

    /// <summary>
    /// Supports only unanchored searches.
    /// </summary>
    Unanchored,

    /// <summary>
    /// Supports only anchored searches.
    /// </summary>
    Anchored,
}
