namespace Scout;

/// <summary>
/// Enforces a conservative byte budget across immutable forward and reverse NFA construction.
/// </summary>
/// <param name="sizeLimit">The maximum retained-NFA estimate in bytes.</param>
internal sealed class RegexNfaConstructionBudget(ulong sizeLimit)
{
    // This intentionally exceeds the current object, list-entry, and alignment costs. Exact
    // atom and sparse-transition payload bytes are charged in addition to this fixed allowance.
    private const ulong EstimatedStateBytes = 256;
    private const ulong EstimatedSparseTransitionBytes = 8;
    private readonly ulong _sizeLimit = sizeLimit;
    private ulong _usedBytes;

    /// <summary>
    /// Gets the configured retained-NFA estimate limit.
    /// </summary>
    internal ulong SizeLimit => _sizeLimit;

    /// <summary>
    /// Gets the currently reserved retained-NFA estimate.
    /// </summary>
    internal ulong UsedBytes => _usedBytes;

    /// <summary>
    /// Determines whether two state-count upper bounds fit a retained-NFA byte budget.
    /// </summary>
    /// <param name="forwardStateCount">The forward-state upper bound.</param>
    /// <param name="reverseStateCount">The reverse-state upper bound.</param>
    /// <param name="sizeLimit">The maximum retained-NFA estimate in bytes.</param>
    /// <returns><see langword="true" /> when the bounds fit the budget.</returns>
    internal static bool CanFitStateCounts(
        ulong forwardStateCount,
        ulong reverseStateCount,
        ulong sizeLimit)
    {
        ulong stateCount = SaturatingAdd(forwardStateCount, reverseStateCount);
        return SaturatingMultiply(stateCount, EstimatedStateBytes) <= sizeLimit;
    }

    /// <summary>
    /// Computes the conservative retained-byte estimate for a materialized NFA.
    /// </summary>
    /// <param name="nfa">The materialized NFA.</param>
    /// <returns>The saturated retained-byte estimate.</returns>
    internal static ulong EstimateRetainedBytes(RegexNfa nfa)
    {
        ulong estimatedBytes = 0;
        for (int index = 0; index < nfa.States.Count; index++)
        {
            RegexNfaState state = nfa.States[index];
            ulong payloadBytes = (ulong)state.Value.Length;
            payloadBytes = SaturatingAdd(
                payloadBytes,
                EstimateSparsePayloadBytes(state.SparseTransitions.Length));
            estimatedBytes = SaturatingAdd(
                estimatedBytes,
                SaturatingAdd(EstimatedStateBytes, payloadBytes));
        }

        return estimatedBytes;
    }

    /// <summary>
    /// Computes the conservative retained payload estimate for sparse transitions.
    /// </summary>
    /// <param name="transitionCount">The number of sparse transitions.</param>
    /// <returns>The saturated payload estimate in bytes.</returns>
    internal static ulong EstimateSparsePayloadBytes(int transitionCount)
    {
        return SaturatingMultiply(
            (ulong)Math.Max(transitionCount, 0),
            EstimatedSparseTransitionBytes);
    }

    /// <summary>
    /// Determines whether materialized forward and reverse NFAs fit the configured budget.
    /// </summary>
    /// <param name="forward">The materialized forward NFA.</param>
    /// <param name="reverse">The materialized reverse NFA.</param>
    /// <returns><see langword="true" /> when their retained estimate fits.</returns>
    internal bool CanRetain(RegexNfa forward, RegexNfa reverse)
    {
        ulong estimatedBytes = SaturatingAdd(
            EstimateRetainedBytes(forward),
            EstimateRetainedBytes(reverse));
        return estimatedBytes <= _sizeLimit;
    }

    /// <summary>
    /// Reserves one NFA state and its atom payload before either is allocated.
    /// </summary>
    /// <param name="payloadBytes">The atom payload bytes retained by the state.</param>
    /// <exception cref="InsufficientMemoryException">
    /// The reservation would exceed the configured budget.
    /// </exception>
    internal void ReserveState(ulong payloadBytes)
    {
        ReserveBytes(SaturatingAdd(EstimatedStateBytes, payloadBytes));
    }

    /// <summary>
    /// Reserves a materialized NFA that is shared with a newly created wrapper.
    /// </summary>
    /// <param name="nfa">The NFA whose retained graph must be covered.</param>
    /// <exception cref="InsufficientMemoryException">
    /// The reservation would exceed the configured budget.
    /// </exception>
    internal void ReserveRetainedNfa(RegexNfa nfa)
    {
        ReserveBytes(EstimateRetainedBytes(nfa));
    }

    /// <summary>
    /// Captures the current reservation for rollback after an abandoned construction attempt.
    /// </summary>
    /// <returns>The opaque reservation checkpoint.</returns>
    internal ulong CreateCheckpoint()
    {
        return _usedBytes;
    }

    /// <summary>
    /// Restores a reservation checkpoint after an abandoned construction attempt.
    /// </summary>
    /// <param name="checkpoint">The checkpoint returned by <see cref="CreateCheckpoint" />.</param>
    internal void Restore(ulong checkpoint)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(checkpoint, _usedBytes);

        _usedBytes = checkpoint;
    }

    /// <summary>
    /// Adds two values and saturates on overflow.
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>The sum, or <see cref="ulong.MaxValue" /> on overflow.</returns>
    internal static ulong SaturatingAdd(ulong left, ulong right)
    {
        return left > ulong.MaxValue - right ? ulong.MaxValue : left + right;
    }

    /// <summary>
    /// Multiplies two values and saturates on overflow.
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>The product, or <see cref="ulong.MaxValue" /> on overflow.</returns>
    internal static ulong SaturatingMultiply(ulong left, ulong right)
    {
        if (left == 0 || right == 0)
        {
            return 0;
        }

        return left > ulong.MaxValue / right ? ulong.MaxValue : left * right;
    }

    private void ReserveBytes(ulong bytes)
    {
        ulong next = SaturatingAdd(_usedBytes, bytes);
        if (next > _sizeLimit)
        {
            throw new InsufficientMemoryException("The regex NFA construction budget was exceeded.");
        }

        _usedBytes = next;
    }
}
