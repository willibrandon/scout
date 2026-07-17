namespace Scout;

/// <summary>
/// Tracks whether a conservative regex prefilter remains effective for one thread-confined search operation.
/// </summary>
internal struct RegexPrefilterState
{
    /// <summary>
    /// The number of prefilter calls permitted before effectiveness is evaluated.
    /// </summary>
    internal const int MinimumSkipCount = 40;

    /// <summary>
    /// The minimum cumulative average number of bytes a prefilter must skip per call.
    /// </summary>
    internal const int MinimumAverageSkippedBytes = 16;

    private long _skipCount;
    private long _skippedByteCount;
    private bool _inert;

    /// <summary>
    /// Gets a value indicating whether the prefilter should be used for its next scan.
    /// </summary>
    internal bool IsEffective
    {
        get
        {
            if (_inert)
            {
                return false;
            }

            if (_skipCount < MinimumSkipCount)
            {
                return true;
            }

            if (_skippedByteCount >= _skipCount * MinimumAverageSkippedBytes)
            {
                return true;
            }

            _inert = true;
            return false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the prefilter is permanently disabled for this operation.
    /// </summary>
    internal readonly bool IsInert => _inert;

    /// <summary>
    /// Gets the number of completed prefilter scans observed by this operation.
    /// </summary>
    internal readonly long SkipCount => _skipCount;

    /// <summary>
    /// Gets the cumulative number of bytes skipped by completed prefilter scans.
    /// </summary>
    internal readonly long SkippedByteCount => _skippedByteCount;

    /// <summary>
    /// Records the distance from one prefilter scan position to its reported candidate.
    /// </summary>
    /// <param name="skippedByteCount">The nonnegative number of bytes skipped by the scan.</param>
    internal void RecordSkip(int skippedByteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skippedByteCount);
        if (IsInert)
        {
            return;
        }

        _skipCount++;
        _skippedByteCount += skippedByteCount;
    }
}
