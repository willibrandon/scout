namespace Scout;

/// <summary>
/// Reuses mutable authoritative regex engine state for one sequence of searches.
/// </summary>
/// <param name="automaton">The automaton that owns the runner pool and search guards.</param>
/// <param name="pikeVm">The rented Pike VM, or <see langword="null" /> for another engine kind.</param>
/// <param name="pikeVmLeaseVersion">The exclusive Pike VM lease generation.</param>
/// <param name="state">The optional shared lazy-DFA state for this operation.</param>
/// <param name="allowUnanchoredDfa">Whether the operation may activate an unanchored DFA.</param>
internal ref struct RegexFindRunner(
    RegexAutomaton automaton,
    PikeVm? pikeVm,
    long pikeVmLeaseVersion,
    RegexFindRunnerState? state,
    bool allowUnanchoredDfa)
{
    private RegexAutomaton? _automaton = automaton;
    private PikeVm? _pikeVm = pikeVm;
    private long _pikeVmLeaseVersion = pikeVmLeaseVersion;
    private RegexFindRunnerState? _state = state;
    private readonly bool _allowUnanchoredDfa = allowUnanchoredDfa;

    /// <summary>
    /// Gets a value indicating whether this runner has an owning automaton.
    /// </summary>
    internal readonly bool IsInitialized =>
        _automaton is not null &&
        (_pikeVm is null || _pikeVm.IsRunnerLeaseActive(_pikeVmLeaseVersion)) &&
        (_state is null || _state.IsActive);

    /// <summary>
    /// Gets the current anchored-DFA lease generation, or zero before lazy rental.
    /// </summary>
    internal readonly long AnchoredDfaLeaseVersion =>
        _state?.AnchoredDfaLeaseVersion ?? 0;

    /// <summary>
    /// Gets the current unanchored-DFA lease generation, or zero before lazy rental.
    /// </summary>
    internal readonly long UnanchoredDfaLeaseVersion =>
        _state?.UnanchoredDfaLeaseVersion ?? 0;

    /// <summary>
    /// Gets a value indicating whether the current DFA executes an ASCII projection.
    /// </summary>
    internal readonly bool UsesAsciiProjection => _state?.UsesAsciiProjection == true;

    /// <summary>
    /// Determines whether another lease refers to the same mutable engine state.
    /// </summary>
    /// <param name="other">The other operation-scoped lease.</param>
    /// <returns>
    /// <see langword="true" /> when both values refer to the same Pike VM or lazy-DFA state.
    /// </returns>
    internal readonly bool SharesPooledStateWith(in RegexFindRunner other)
    {
        return _pikeVm is not null && ReferenceEquals(_pikeVm, other._pikeVm) ||
            _state is not null && ReferenceEquals(_state, other._state);
    }

    /// <summary>
    /// Finds the first leftmost match at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <returns>The first match, or <see langword="null" /> when no match exists.</returns>
    public readonly RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        RegexAutomaton? automaton = _automaton;
        if (automaton is null ||
            _pikeVm is not null && !_pikeVm.IsRunnerLeaseActive(_pikeVmLeaseVersion) ||
            _state is not null && !_state.IsActive)
        {
            throw new ObjectDisposedException(nameof(RegexFindRunner));
        }

        return automaton.FindWithRunner(
            haystack,
            startAt,
            _pikeVm,
            _state,
            _allowUnanchoredDfa);
    }

    /// <summary>
    /// Returns any rented mutable engine state to its owning automaton.
    /// </summary>
    public void Dispose()
    {
        RegexAutomaton? automaton = _automaton;
        PikeVm? pikeVm = _pikeVm;
        long pikeVmLeaseVersion = _pikeVmLeaseVersion;
        RegexFindRunnerState? state = _state;
        _automaton = null;
        _pikeVm = null;
        _pikeVmLeaseVersion = 0;
        _state = null;
        automaton?.ReturnFindPikeVm(pikeVm, pikeVmLeaseVersion);
        state?.Dispose();
    }
}
