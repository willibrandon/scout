namespace Scout;

/// <summary>
/// Holds the ordered authoritative matches produced by one multiline traversal.
/// </summary>
/// <param name="matches">The ordered non-overlapping matches.</param>
internal sealed class MultilineSearchResult(List<RegexMatch> matches)
{
    /// <summary>
    /// Gets the ordered non-overlapping matches.
    /// </summary>
    public List<RegexMatch> Matches { get; } =
        matches ?? throw new ArgumentNullException(nameof(matches));
}
