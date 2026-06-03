
namespace Scout;

/// <summary>
/// Configures and builds Aho-Corasick automatons.
/// </summary>
public sealed class AhoCorasickBuilder
{
    /// <summary>
    /// Gets the configured non-overlapping match semantics.
    /// </summary>
    public AhoCorasickMatchKind MatchKind { get; private set; } = AhoCorasickMatchKind.Standard;

    /// <summary>
    /// Gets the configured anchored-mode support.
    /// </summary>
    public AhoCorasickStartKind StartKind { get; private set; } = AhoCorasickStartKind.Unanchored;

    /// <summary>
    /// Gets a value indicating whether ASCII case-insensitive matching is enabled.
    /// </summary>
    public bool AsciiCaseInsensitive { get; private set; }

    /// <summary>
    /// Sets the non-overlapping match semantics.
    /// </summary>
    /// <param name="matchKind">The match semantics to use.</param>
    /// <returns>This builder.</returns>
    public AhoCorasickBuilder WithMatchKind(AhoCorasickMatchKind matchKind)
    {
        MatchKind = matchKind;
        return this;
    }

    /// <summary>
    /// Sets which anchored modes the automaton supports.
    /// </summary>
    /// <param name="startKind">The anchored-mode support to use.</param>
    /// <returns>This builder.</returns>
    public AhoCorasickBuilder WithStartKind(AhoCorasickStartKind startKind)
    {
        StartKind = startKind;
        return this;
    }

    /// <summary>
    /// Sets whether ASCII case-insensitive matching is enabled.
    /// </summary>
    /// <param name="enabled">Whether to ignore ASCII byte case.</param>
    /// <returns>This builder.</returns>
    public AhoCorasickBuilder WithAsciiCaseInsensitive(bool enabled)
    {
        AsciiCaseInsensitive = enabled;
        return this;
    }

    /// <summary>
    /// Builds an automaton from byte patterns.
    /// </summary>
    /// <param name="patterns">The ordered patterns to search for.</param>
    /// <returns>An Aho-Corasick automaton.</returns>
    public AhoCorasickAutomaton Build(IReadOnlyList<byte[]> patterns)
    {
        return AhoCorasickAutomaton.Create(patterns, MatchKind, AsciiCaseInsensitive, StartKind);
    }
}
