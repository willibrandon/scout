namespace Scout;

/// <summary>
/// Reuses one mutable exact-capture engine for an operation-scoped sequence of replays.
/// </summary>
/// <param name="automaton">The automaton that owns the runner pool.</param>
/// <param name="captureEngine">The rented exact-capture engine, or <see langword="null" />.</param>
/// <param name="leaseVersion">The exclusive capture-engine lease generation.</param>
internal struct RegexCaptureRunner(
    RegexAutomaton automaton,
    RegexCaptureEngine? captureEngine,
    long leaseVersion) : IDisposable
{
    private RegexAutomaton? _automaton = automaton;
    private RegexCaptureEngine? _captureEngine = captureEngine;
    private long _leaseVersion = leaseVersion;

    /// <summary>
    /// Gets a value indicating whether this runner still owns its mutable state.
    /// </summary>
    internal readonly bool IsInitialized =>
        _automaton is not null &&
        (_captureEngine is null || _captureEngine.IsRunnerLeaseActive(_leaseVersion));

    /// <summary>
    /// Gets the active capture-engine lease generation, or zero when no engine was required.
    /// </summary>
    internal readonly long LeaseVersion => _leaseVersion;

    /// <summary>
    /// Determines whether another lease refers to the same mutable capture engine.
    /// </summary>
    /// <param name="other">The other operation-scoped capture lease.</param>
    /// <returns><see langword="true" /> when both values reference the same engine.</returns>
    internal readonly bool SharesPooledStateWith(in RegexCaptureRunner other)
    {
        return _captureEngine is not null &&
            ReferenceEquals(_captureEngine, other._captureEngine);
    }

    /// <summary>
    /// Replays capture groups for one authoritative match span.
    /// </summary>
    /// <param name="haystack">The complete haystack used to evaluate predicates.</param>
    /// <param name="startAt">The exact match start.</param>
    /// <param name="endAt">The exact exclusive match end.</param>
    /// <param name="captureSlots">Receives flattened capture start and end offsets.</param>
    /// <returns><see langword="true" /> when the exact span was replayed.</returns>
    internal readonly bool TryReplayCaptures(
        ReadOnlySpan<byte> haystack,
        int startAt,
        int endAt,
        Span<int> captureSlots)
    {
        RegexAutomaton? automaton = _automaton;
        if (automaton is null ||
            _captureEngine is not null && !_captureEngine.IsRunnerLeaseActive(_leaseVersion))
        {
            throw new ObjectDisposedException(nameof(RegexCaptureRunner));
        }

        return automaton.TryReplayCapturesWithRunner(
            haystack,
            startAt,
            endAt,
            captureSlots,
            _captureEngine);
    }

    /// <summary>
    /// Returns the rented capture engine to its owning automaton exactly once.
    /// </summary>
    public void Dispose()
    {
        RegexAutomaton? automaton = _automaton;
        RegexCaptureEngine? captureEngine = _captureEngine;
        long leaseVersion = _leaseVersion;
        _automaton = null;
        _captureEngine = null;
        _leaseVersion = 0;
        automaton?.ReturnCaptureRunner(captureEngine, leaseVersion);
    }
}
