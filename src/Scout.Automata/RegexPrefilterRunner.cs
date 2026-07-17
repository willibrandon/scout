namespace Scout;

/// <summary>
/// Tracks one adaptive syntax-derived prefilter scan across independent candidate records.
/// </summary>
/// <param name="prefilter">The compiled prefilter, or <see langword="null" /> when unavailable.</param>
internal ref struct RegexPrefilterRunner(RegexPrefilter? prefilter)
{
    private readonly RegexPrefilter? _prefilter = prefilter;
    private RegexPrefilterState _state;

    /// <summary>
    /// Gets a value indicating whether this runner has a compiled prefilter.
    /// </summary>
    internal readonly bool IsAvailable => _prefilter is not null;

    /// <summary>
    /// Gets a value indicating whether each reported candidate is an exact match start.
    /// </summary>
    internal readonly bool UsesExactStartCandidates =>
        _prefilter?.UsesExactStartCandidates == true;

    /// <summary>
    /// Gets a value indicating whether the prefilter has become ineffective for this operation.
    /// </summary>
    internal readonly bool IsInert => _state.IsInert;

    /// <summary>
    /// Gets the number of completed prefilter scans observed by this operation.
    /// </summary>
    internal readonly long SkipCount => _state.SkipCount;

    /// <summary>
    /// Attempts to locate the next conservative candidate within a search window.
    /// </summary>
    /// <param name="haystack">The complete search window.</param>
    /// <param name="startAt">The first byte to scan.</param>
    /// <param name="candidate">Receives the candidate byte offset, or <c>-1</c>.</param>
    /// <returns>
    /// <see langword="true" /> when a candidate was found. A false result is authoritative
    /// exhaustion unless <see cref="IsInert" /> is <see langword="true" />, in which case the
    /// caller must continue with an unfiltered authoritative search at <paramref name="startAt" />.
    /// </returns>
    internal bool TryFindCandidate(
        ReadOnlySpan<byte> haystack,
        int startAt,
        out int candidate)
    {
        RegexPrefilter? candidatePrefilter = _prefilter;
        if (candidatePrefilter is null)
        {
            candidate = -1;
            return false;
        }

        int boundedStart = Math.Clamp(startAt, 0, haystack.Length);
        if (!_state.IsEffective)
        {
            candidate = -1;
            return false;
        }

        candidate = candidatePrefilter.UsesRequiredLiteralWindow
            ? candidatePrefilter.FindRequiredLiteral(haystack, boundedStart)
            : candidatePrefilter.FindCandidate(haystack, boundedStart);
        _state.RecordSkip(candidate < 0
            ? haystack.Length - boundedStart
            : Math.Max(0, candidate - boundedStart));
        return candidate >= 0;
    }
}
