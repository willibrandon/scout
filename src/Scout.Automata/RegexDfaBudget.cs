namespace Scout;

/// <summary>
/// Tracks the estimated retained storage of one bounded DFA construction or cache.
/// </summary>
/// <param name="limit">The maximum estimated retained storage in bytes.</param>
internal struct RegexDfaBudget(ulong limit)
{
    private const ulong StateOverheadBytes = 64;
    private const ulong NfaStateIndexBytes = sizeof(int);

    private readonly ulong _limit = limit;
    private ulong _used;

    /// <summary>
    /// Gets the estimated storage of one sparse transition entry.
    /// </summary>
    public const ulong SparseTransitionBytes = 16;

    /// <summary>
    /// Gets the estimated storage of one 256-entry lazy-DFA reference table.
    /// </summary>
    public static ulong DenseReferenceTransitionTableBytes =>
        (3UL + 256UL) * (ulong)IntPtr.Size;

    /// <summary>
    /// Estimates the retained storage of one DFA state.
    /// </summary>
    /// <param name="nfaStateCount">The number of NFA-state indexes retained by the state.</param>
    /// <param name="denseTransitions">Whether the state retains a 256-entry integer transition row.</param>
    /// <returns>The estimated retained storage in bytes.</returns>
    public static ulong EstimateStateBytes(int nfaStateCount, bool denseTransitions)
    {
        return StateOverheadBytes +
            checked((ulong)nfaStateCount * NfaStateIndexBytes) +
            (denseTransitions
                ? 3UL * (ulong)IntPtr.Size + 256UL * sizeof(int)
                : 0);
    }

    /// <summary>
    /// Attempts to reserve one lazy transition and any dense reference table it will allocate.
    /// </summary>
    /// <param name="allocatesDenseReferenceTable">
    /// Whether the transition promotes its state to a 256-entry reference table.
    /// </param>
    /// <returns><see langword="true" /> when the complete reservation fits within the limit.</returns>
    public bool TryReserveLazyTransition(bool allocatesDenseReferenceTable)
    {
        ulong bytes = SparseTransitionBytes +
            (allocatesDenseReferenceTable ? DenseReferenceTransitionTableBytes : 0);
        return TryReserve(bytes);
    }

    /// <summary>
    /// Attempts to reserve estimated retained storage from the remaining budget.
    /// </summary>
    /// <param name="bytes">The number of bytes to reserve.</param>
    /// <returns><see langword="true" /> when the reservation fits within the limit.</returns>
    public bool TryReserve(ulong bytes)
    {
        if (_limit - _used < bytes)
        {
            return false;
        }

        _used += bytes;
        return true;
    }
}
