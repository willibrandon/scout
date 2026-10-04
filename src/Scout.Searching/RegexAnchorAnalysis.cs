namespace Scout;

/// <summary>
/// Accumulates the anchor scopes present in a syntax tree.
/// </summary>
internal struct RegexAnchorAnalysis
{
    internal bool HasAbsoluteAnchors;

    internal bool HasLineAnchors;

    internal bool HasHaystackAnchors;
}
