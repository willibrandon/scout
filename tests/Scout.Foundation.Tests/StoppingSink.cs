
namespace Scout;

/// <summary>
/// Stops matcher callback iteration after the first match.
/// </summary>
internal struct StoppingSink : IMatcherSink
{
    /// <summary>
    /// Gets the match observed before stopping.
    /// </summary>
    public MatcherMatch Match { get; private set; }

    /// <inheritdoc />
    public bool Matched(ReadOnlySpan<byte> haystack, MatcherMatch match)
    {
        Match = match;
        return false;
    }
}
