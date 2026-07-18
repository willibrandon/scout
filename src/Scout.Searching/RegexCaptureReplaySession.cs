namespace Scout;

/// <summary>
/// Supplies replacement captures through one operation-scoped exact-capture runner.
/// </summary>
/// <param name="searchPlan">The authoritative regex plan whose captures are replayed.</param>
internal sealed class RegexCaptureReplaySession(RegexSearchPlan searchPlan) :
    IReplacementCaptureProvider,
    IDisposable
{
    private readonly RegexSearchPlan _searchPlan = searchPlan;
    private RegexCaptureRunner _runner = searchPlan.Matcher.RentCaptureRunner();

    /// <inheritdoc />
    public int CaptureCount => _searchPlan.CaptureCount;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, int> CaptureNames => _searchPlan.CaptureNames;

    /// <summary>
    /// Gets a value indicating whether the operation-scoped runner remains active.
    /// </summary>
    internal bool IsInitialized => _runner.IsInitialized;

    /// <summary>
    /// Gets the active capture-engine lease generation.
    /// </summary>
    internal long LeaseVersion => _runner.LeaseVersion;

    /// <inheritdoc />
    public bool TryCollectCaptureSlots(
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength,
        int searchStart,
        Span<int> captureSlots)
    {
        _ = searchStart;
        return _searchPlan.TryCollectCaptureSlots(
            haystack,
            matchStart,
            matchLength,
            captureSlots,
            in _runner);
    }

    /// <summary>
    /// Returns the operation-scoped capture runner to its owning automaton.
    /// </summary>
    public void Dispose()
    {
        _runner.Dispose();
    }
}
