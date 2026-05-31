namespace Scout;

internal struct RegexDfaBudget
{
    public const ulong SparseTransitionBytes = 16;

    private const ulong StateOverheadBytes = 64;
    private const ulong DenseTransitionBytes = 256 * sizeof(int);
    private const ulong NfaStateIndexBytes = sizeof(int);

    private readonly ulong limit;
    private ulong used;

    public RegexDfaBudget(ulong limit)
    {
        this.limit = limit;
        used = 0;
    }

    public static ulong EstimateStateBytes(int nfaStateCount, bool denseTransitions)
    {
        return StateOverheadBytes +
            checked((ulong)nfaStateCount * NfaStateIndexBytes) +
            (denseTransitions ? DenseTransitionBytes : 0);
    }

    public bool TryReserve(ulong bytes)
    {
        if (limit - used < bytes)
        {
            return false;
        }

        used += bytes;
        return true;
    }
}
