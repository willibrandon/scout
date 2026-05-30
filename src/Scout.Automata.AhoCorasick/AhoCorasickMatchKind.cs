namespace Scout;

/// <summary>
/// Defines Aho-Corasick non-overlapping match semantics.
/// </summary>
public enum AhoCorasickMatchKind
{
    /// <summary>
    /// Reports matches as soon as they are found and supports overlapping search.
    /// </summary>
    Standard,

    /// <summary>
    /// Reports the leftmost match, breaking ties by earliest pattern identifier.
    /// </summary>
    LeftmostFirst,

    /// <summary>
    /// Reports the leftmost match, breaking ties by longest match length.
    /// </summary>
    LeftmostLongest,
}
