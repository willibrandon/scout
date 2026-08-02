namespace Scout;

/// <summary>
/// Executes one direction of a paired unanchored lazy-DFA search.
/// </summary>
internal interface IRegexLazyDfaDirection
{
    /// <summary>
    /// Finds the end of the first leftmost match.
    /// </summary>
    bool TryFindEnd(
        ReadOnlySpan<byte> haystack,
        int start,
        Dictionary<(int State, int Position), bool>? reachabilityCache,
        out int end,
        out bool gaveUp);

    /// <summary>
    /// Reconstructs a match start by searching the reversed NFA.
    /// </summary>
    bool TryFindStartReverse(
        ReadOnlySpan<byte> haystack,
        int start,
        int end,
        Dictionary<(int State, int Position), bool>? reachabilityCache,
        out int matchStart,
        out bool gaveUp);
}
