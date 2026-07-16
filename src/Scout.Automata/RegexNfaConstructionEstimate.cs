namespace Scout;

/// <summary>
/// Describes a conservative upper bound for the immutable NFAs retained by an unanchored
/// forward-search and reverse-start pair.
/// </summary>
/// <param name="forwardStateCount">
/// The maximum number of states in the forward NFA, including its unanchored wrapper.
/// </param>
/// <param name="reverseStateCount">The maximum number of states in the reverse NFA.</param>
internal readonly struct RegexNfaConstructionEstimate(
    ulong forwardStateCount,
    ulong reverseStateCount)
{
    /// <summary>
    /// Gets the conservative forward-state upper bound.
    /// </summary>
    internal ulong ForwardStateCount { get; } = forwardStateCount;

    /// <summary>
    /// Gets the conservative reverse-state upper bound.
    /// </summary>
    internal ulong ReverseStateCount { get; } = reverseStateCount;

    /// <summary>
    /// Gets the saturated combined state upper bound.
    /// </summary>
    internal ulong TotalStateCount => RegexNfaConstructionBudget.SaturatingAdd(
        ForwardStateCount,
        ReverseStateCount);

    /// <summary>
    /// Determines whether the conservative forward-state estimate fits a retained-NFA byte budget.
    /// </summary>
    /// <param name="sizeLimit">The maximum retained-NFA estimate in bytes.</param>
    /// <returns><see langword="true" /> when the forward estimate fits the budget.</returns>
    internal bool ForwardFits(ulong sizeLimit)
    {
        return RegexNfaConstructionBudget.CanFitStateCounts(
            ForwardStateCount,
            reverseStateCount: 0,
            sizeLimit);
    }

    /// <summary>
    /// Determines whether the conservative state estimate fits a retained-NFA byte budget.
    /// </summary>
    /// <param name="sizeLimit">The maximum retained-NFA estimate in bytes.</param>
    /// <returns><see langword="true" /> when the estimate fits the budget.</returns>
    internal bool Fits(ulong sizeLimit)
    {
        return RegexNfaConstructionBudget.CanFitStateCounts(
            ForwardStateCount,
            ReverseStateCount,
            sizeLimit);
    }
}
