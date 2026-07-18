namespace Scout;

/// <summary>
/// Owns adaptive prefilter state and any lazily rented DFA shared by copies of one find runner.
/// </summary>
/// <param name="automaton">The automaton that owns the runner pool and search guards.</param>
/// <param name="hasPrefilter">Whether the runner needs adaptive prefilter state.</param>
internal sealed class RegexFindRunnerState(RegexAutomaton automaton, bool hasPrefilter)
{
    private readonly RegexAutomaton _automaton = automaton;
    private readonly bool _hasPrefilter = hasPrefilter;
    private RegexPrefilterState _prefilterState;
    private RegexLazyDfa? _anchoredDfa;
    private long _anchoredDfaLeaseVersion;
    private RegexUnanchoredLazyDfa? _unanchoredDfa;
    private long _unanchoredDfaLeaseVersion;
    private bool _usesAsciiProjection;
    private int _disposed;

    /// <summary>
    /// Gets a value indicating whether the shared operation-scoped state remains active.
    /// </summary>
    internal bool IsActive => System.Threading.Volatile.Read(ref _disposed) == 0;

    /// <summary>
    /// Gets the current anchored-DFA lease generation, or zero before lazy rental.
    /// </summary>
    internal long AnchoredDfaLeaseVersion => _anchoredDfaLeaseVersion;

    /// <summary>
    /// Gets the currently rented anchored DFA, or <see langword="null" /> before activation.
    /// </summary>
    internal RegexLazyDfa? AnchoredDfa => _anchoredDfa;

    /// <summary>
    /// Gets the current unanchored-DFA lease generation, or zero before lazy rental.
    /// </summary>
    internal long UnanchoredDfaLeaseVersion => _unanchoredDfaLeaseVersion;

    /// <summary>
    /// Gets the currently rented unanchored DFA, or <see langword="null" /> before activation.
    /// </summary>
    internal RegexUnanchoredLazyDfa? UnanchoredDfa => _unanchoredDfa;

    /// <summary>
    /// Gets a value indicating whether the current DFA executes an ASCII projection.
    /// </summary>
    internal bool UsesAsciiProjection => _usesAsciiProjection;

    /// <summary>
    /// Gets the adaptive prefilter-effectiveness state for this search operation.
    /// </summary>
    internal Span<RegexPrefilterState> PrefilterState => _hasPrefilter
        ? System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _prefilterState, 1)
        : default;

    /// <summary>
    /// Gets a value indicating whether this operation has permanently disabled its prefilter.
    /// </summary>
    internal bool IsPrefilterInert => _hasPrefilter && _prefilterState.IsInert;

    /// <summary>
    /// Gets the number of prefilter scans observed by this operation.
    /// </summary>
    internal long PrefilterSkipCount => _hasPrefilter ? _prefilterState.SkipCount : 0;

    /// <summary>
    /// Rents and retains the eligible anchored or unanchored DFA for the operation.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="hasRequiredStart">
    /// Whether every match must begin at an anchored text or line boundary.
    /// </param>
    internal void EnsureDfa(ReadOnlySpan<byte> haystack, bool hasRequiredStart)
    {
        ThrowIfDisposed();
        if (_anchoredDfa is not null || _unanchoredDfa is not null)
        {
            return;
        }

        RegexLazyDfa? anchoredDfa = _automaton.RentFindAnchoredDfa(haystack);
        if (anchoredDfa is not null)
        {
            _anchoredDfa = anchoredDfa;
            _anchoredDfaLeaseVersion = anchoredDfa.BeginRunnerLease();
            return;
        }

        if (hasRequiredStart)
        {
            return;
        }

        RegexUnanchoredLazyDfa? unanchoredDfa =
            _automaton.RentFindUnanchoredDfa(haystack, out bool usesAsciiProjection);
        if (unanchoredDfa is null)
        {
            return;
        }

        _unanchoredDfa = unanchoredDfa;
        _unanchoredDfaLeaseVersion = unanchoredDfa.BeginRunnerLease();
        _usesAsciiProjection = usesAsciiProjection;
    }

    /// <summary>
    /// Returns the lazily rented DFA to its owning pool exactly once.
    /// </summary>
    internal void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        RegexLazyDfa? anchoredDfa = _anchoredDfa;
        long anchoredDfaLeaseVersion = _anchoredDfaLeaseVersion;
        RegexUnanchoredLazyDfa? unanchoredDfa = _unanchoredDfa;
        long unanchoredDfaLeaseVersion = _unanchoredDfaLeaseVersion;
        bool usesAsciiProjection = _usesAsciiProjection;
        _anchoredDfa = null;
        _anchoredDfaLeaseVersion = 0;
        _unanchoredDfa = null;
        _unanchoredDfaLeaseVersion = 0;
        _usesAsciiProjection = false;
        _automaton.ReturnFindAnchoredDfa(
            anchoredDfa,
            anchoredDfaLeaseVersion);
        _automaton.ReturnFindUnanchoredDfa(
            unanchoredDfa,
            unanchoredDfaLeaseVersion,
            usesAsciiProjection);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(!IsActive, nameof(RegexFindRunner));
    }
}
