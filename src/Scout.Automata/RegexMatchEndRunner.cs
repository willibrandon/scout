namespace Scout;

/// <summary>
/// Holds one forward unanchored DFA execution path across successive match-end searches.
/// </summary>
/// <param name="pool">The pool that owns the runner.</param>
/// <param name="dfa">The optional exclusively rented mutable runner.</param>
/// <param name="dfaLeaseToken">The exclusive mutable-runner lease generation.</param>
/// <param name="denseDfa">The optional shared immutable dense runner.</param>
/// <param name="usesAsciiProjection">Whether the runner executes an authoritative ASCII projection.</param>
internal ref struct RegexMatchEndRunner(
    RegexRunnerPool<RegexUnanchoredLazyDfa>? pool,
    RegexUnanchoredLazyDfa? dfa,
    long dfaLeaseToken,
    RegexUnanchoredDenseDfa? denseDfa,
    bool usesAsciiProjection)
{
    private RegexRunnerPool<RegexUnanchoredLazyDfa>? _pool = pool;
    private RegexUnanchoredLazyDfa? _dfa = dfa;
    private long _dfaLeaseToken = dfaLeaseToken;
    private RegexUnanchoredDenseDfa? _denseDfa = denseDfa;
    private readonly bool _usesAsciiProjection = usesAsciiProjection;

    /// <summary>
    /// Gets a value indicating whether a forward runner is available.
    /// </summary>
    internal readonly bool IsAvailable =>
        _denseDfa is not null ||
        _dfa is not null && _dfa.IsRunnerLeaseActive(_dfaLeaseToken);

    /// <summary>
    /// Gets a value indicating whether the runner uses ASCII-projected semantics.
    /// </summary>
    internal readonly bool UsesAsciiProjection => _usesAsciiProjection;

    /// <summary>
    /// Determines whether another lease refers to the same mutable DFA runner.
    /// </summary>
    /// <param name="other">The other operation-scoped lease.</param>
    /// <returns><see langword="true" /> when both values refer to the same mutable DFA.</returns>
    internal readonly bool SharesPooledStateWith(in RegexMatchEndRunner other)
    {
        return _dfa is not null && ReferenceEquals(_dfa, other._dfa);
    }

    /// <summary>
    /// Attempts to find the next match end without reconstructing its start.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="end">Receives the exclusive match end.</param>
    /// <param name="completed">
    /// Receives whether the forward DFA completed authoritatively, including a definitive no-match result.
    /// </param>
    /// <returns><see langword="true" /> when a match end is found.</returns>
    internal readonly bool TryFindEnd(
        ReadOnlySpan<byte> haystack,
        int startAt,
        out int end,
        out bool completed)
    {
        if (_denseDfa is not null)
        {
            completed = true;
            return _denseDfa.TryFindEnd(
                haystack,
                Math.Clamp(startAt, 0, haystack.Length),
                out end);
        }

        RegexUnanchoredLazyDfa? dfa = _dfa;
        if (dfa is null)
        {
            end = 0;
            completed = false;
            return false;
        }

        if (!dfa.IsRunnerLeaseActive(_dfaLeaseToken))
        {
            throw new ObjectDisposedException(nameof(RegexMatchEndRunner));
        }

        bool found = dfa.TryFindEnd(
            haystack,
            Math.Clamp(startAt, 0, haystack.Length),
            out end,
            out bool gaveUp);
        completed = !gaveUp;
        return found && !gaveUp;
    }

    /// <summary>
    /// Attempts to count every non-overlapping match without reconstructing match starts.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="count">Receives the complete count, or zero when the DFA gives up.</param>
    /// <returns><see langword="true" /> when the forward DFA completes authoritatively.</returns>
    internal readonly bool TryCountMatches(
        ReadOnlySpan<byte> haystack,
        int startAt,
        out long count)
    {
        if (_denseDfa is not null)
        {
            count = _denseDfa.CountMatches(
                haystack,
                Math.Clamp(startAt, 0, haystack.Length));
            return true;
        }

        count = 0;
        RegexUnanchoredLazyDfa? dfa = _dfa;
        if (dfa is null)
        {
            return false;
        }

        if (!dfa.IsRunnerLeaseActive(_dfaLeaseToken))
        {
            throw new ObjectDisposedException(nameof(RegexMatchEndRunner));
        }

        if (!dfa.TryCountMatches(
                haystack,
                Math.Clamp(startAt, 0, haystack.Length),
                out long completedCount))
        {
            return false;
        }

        count = completedCount;
        return true;
    }

    /// <summary>
    /// Returns the exclusively rented runner to its pool.
    /// </summary>
    internal void Dispose()
    {
        RegexRunnerPool<RegexUnanchoredLazyDfa>? pool = _pool;
        RegexUnanchoredLazyDfa? dfa = _dfa;
        long dfaLeaseToken = _dfaLeaseToken;
        _pool = null;
        _dfa = null;
        _dfaLeaseToken = 0;
        _denseDfa = null;
        if (pool is not null &&
            dfa is not null &&
            dfa.TryEndRunnerLease(dfaLeaseToken))
        {
            pool.Return(dfa);
        }
    }
}
