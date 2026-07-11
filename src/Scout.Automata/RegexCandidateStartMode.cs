namespace Scout;

/// <summary>
/// Identifies how candidate match starts are discovered.
/// </summary>
internal enum RegexCandidateStartMode
{
    /// <summary>
    /// Considers every legal start in the requested range.
    /// </summary>
    Every,

    /// <summary>
    /// Considers only exact prefix prefilter hits.
    /// </summary>
    ExactPrefix,

    /// <summary>
    /// Considers starts in merged required-literal lookbehind ranges.
    /// </summary>
    RequiredLiteralRanges,
}
