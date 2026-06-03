
namespace Scout;

/// <summary>
/// Collects matcher callback spans for regex adapter tests.
/// </summary>
internal struct CollectingSink : IMatcherSink
{
    /// <summary>
    /// Gets the number of collected matches.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Gets collected match start offsets.
    /// </summary>
    public int[] Starts { get; private set; }

    /// <summary>
    /// Gets collected match lengths.
    /// </summary>
    public int[] Lengths { get; private set; }

    /// <inheritdoc />
    public bool Matched(ReadOnlySpan<byte> haystack, MatcherMatch match)
    {
        Starts ??= new int[4];
        Lengths ??= new int[4];
        Starts[Count] = match.Start;
        Lengths[Count] = match.Length;
        Count++;
        return true;
    }
}
