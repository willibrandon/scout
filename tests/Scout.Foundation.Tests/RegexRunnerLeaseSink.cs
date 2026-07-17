namespace Scout;

/// <summary>
/// Observes nested runner leases from the struct-sink matcher callback.
/// </summary>
/// <param name="observer">The lease observer.</param>
internal struct RegexRunnerLeaseSink(RegexRunnerLeaseObserver observer) : IMatcherSink
{
    private readonly RegexRunnerLeaseObserver _observer = observer;

    /// <inheritdoc />
    public bool Matched(ReadOnlySpan<byte> haystack, MatcherMatch match)
    {
        _ = haystack;
        _ = match;
        _observer.Observe();
        return true;
    }
}
