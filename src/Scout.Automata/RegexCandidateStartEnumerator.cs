namespace Scout;

/// <summary>
/// Streams legal match starts without materializing candidate collections.
/// </summary>
/// <param name="haystack">The bytes being searched.</param>
/// <param name="mode">The strategy used to discover candidate starts.</param>
/// <param name="startAt">The first permitted match start.</param>
/// <param name="maxStart">The last permitted match start.</param>
/// <param name="utf8">Whether candidates must begin on UTF-8 boundaries.</param>
/// <param name="prefilter">The prefilter used by prefix and required-literal modes.</param>
/// <param name="startPredicate">The predicate used by the every-start mode.</param>
internal ref struct RegexCandidateStartEnumerator(
    ReadOnlySpan<byte> haystack,
    RegexCandidateStartMode mode,
    int startAt,
    int maxStart,
    bool utf8,
    RegexPrefilter? prefilter,
    RegexStartPredicate? startPredicate)
{
    private readonly ReadOnlySpan<byte> _haystack = haystack;
    private readonly RegexCandidateStartMode _mode = mode;
    private readonly RegexPrefilter? _prefilter = prefilter;
    private readonly RegexStartPredicate? _startPredicate = startPredicate;
    private readonly bool _utf8 = utf8;
    private readonly int _startAt = Math.Clamp(startAt, 0, haystack.Length);
    private readonly int _maxStart = Math.Clamp(maxStart, 0, haystack.Length);
    private int _nextStart = Math.Clamp(startAt, 0, haystack.Length);
    private int _rangeEnd = Math.Clamp(startAt, 0, haystack.Length) - 1;
    private int _requiredSearchAt = Math.Clamp(startAt, 0, haystack.Length);
    private int _pendingRequiredAt = -1;

    /// <summary>
    /// Creates an enumerator that considers every legal match start.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="maxStart">The last permitted match start.</param>
    /// <param name="utf8">Whether candidates must begin on UTF-8 boundaries.</param>
    /// <param name="startPredicate">An optional predicate that filters match starts.</param>
    /// <returns>An every-start candidate enumerator.</returns>
    public static RegexCandidateStartEnumerator Every(
        ReadOnlySpan<byte> haystack,
        int startAt,
        int maxStart,
        bool utf8,
        RegexStartPredicate? startPredicate)
    {
        return new RegexCandidateStartEnumerator(
            haystack,
            RegexCandidateStartMode.Every,
            startAt,
            maxStart,
            utf8,
            prefilter: null,
            startPredicate);
    }

    /// <summary>
    /// Creates an enumerator that returns exact prefix candidates.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="maxStart">The last permitted match start.</param>
    /// <param name="utf8">Whether candidates must begin on UTF-8 boundaries.</param>
    /// <param name="prefilter">The prefix prefilter used to locate candidates.</param>
    /// <returns>An exact-prefix candidate enumerator.</returns>
    public static RegexCandidateStartEnumerator ExactPrefix(
        ReadOnlySpan<byte> haystack,
        int startAt,
        int maxStart,
        bool utf8,
        RegexPrefilter prefilter)
    {
        return new RegexCandidateStartEnumerator(
            haystack,
            RegexCandidateStartMode.ExactPrefix,
            startAt,
            maxStart,
            utf8,
            prefilter,
            startPredicate: null);
    }

    /// <summary>
    /// Creates an enumerator that streams merged required-literal lookbehind ranges.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="maxStart">The last permitted match start.</param>
    /// <param name="utf8">Whether candidates must begin on UTF-8 boundaries.</param>
    /// <param name="prefilter">The required-literal prefilter used to form ranges.</param>
    /// <returns>A required-literal-range candidate enumerator.</returns>
    public static RegexCandidateStartEnumerator RequiredLiteralRanges(
        ReadOnlySpan<byte> haystack,
        int startAt,
        int maxStart,
        bool utf8,
        RegexPrefilter prefilter)
    {
        return new RegexCandidateStartEnumerator(
            haystack,
            RegexCandidateStartMode.RequiredLiteralRanges,
            startAt,
            maxStart,
            utf8,
            prefilter,
            startPredicate: null);
    }

    /// <summary>
    /// Advances to the next legal candidate start.
    /// </summary>
    /// <param name="start">Receives the next candidate start, or <c>-1</c> when exhausted.</param>
    /// <returns><see langword="true"/> when a candidate was produced.</returns>
    public bool MoveNext(out int start)
    {
        return _mode switch
        {
            RegexCandidateStartMode.Every => MoveNextEvery(out start),
            RegexCandidateStartMode.ExactPrefix => MoveNextExactPrefix(out start),
            RegexCandidateStartMode.RequiredLiteralRanges => MoveNextRequiredLiteralRange(out start),
            _ => throw new InvalidOperationException($"Unsupported candidate-start mode: {_mode}."),
        };
    }

    private bool MoveNextEvery(out int start)
    {
        while (_nextStart <= _maxStart)
        {
            int candidate = _nextStart++;
            if (IsUtf8Boundary(candidate) &&
                (_startPredicate is null || _startPredicate.CanStartAt(_haystack, candidate)))
            {
                start = candidate;
                return true;
            }
        }

        start = -1;
        return false;
    }

    private bool MoveNextExactPrefix(out int start)
    {
        while (_nextStart <= _maxStart)
        {
            int candidate = _prefilter!.FindCandidate(_haystack, _nextStart);
            if (candidate < 0 || candidate > _maxStart)
            {
                break;
            }

            _nextStart = candidate + 1;
            if (IsUtf8Boundary(candidate))
            {
                start = candidate;
                return true;
            }
        }

        start = -1;
        return false;
    }

    private bool MoveNextRequiredLiteralRange(out int start)
    {
        while (true)
        {
            while (_nextStart <= _rangeEnd)
            {
                int candidate = _nextStart++;
                if (IsUtf8Boundary(candidate) && _prefilter!.CanStartAt(_haystack, candidate))
                {
                    start = candidate;
                    return true;
                }
            }

            if (!TryLoadNextRequiredLiteralRange())
            {
                start = -1;
                return false;
            }
        }
    }

    private bool TryLoadNextRequiredLiteralRange()
    {
        int requiredAt = _pendingRequiredAt;
        _pendingRequiredAt = -1;
        if (requiredAt < 0)
        {
            requiredAt = FindNextRequiredLiteral();
            if (requiredAt < 0)
            {
                return false;
            }
        }

        int mergedStart = Math.Max(_startAt, requiredAt - _prefilter!.RequiredLiteralWindow);
        int mergedEnd = Math.Min(_maxStart, requiredAt);
        while (true)
        {
            int nextRequiredAt = FindNextRequiredLiteral();
            if (nextRequiredAt < 0)
            {
                break;
            }

            int nextRangeStart = Math.Max(_startAt, nextRequiredAt - _prefilter.RequiredLiteralWindow);
            if (nextRangeStart > mergedEnd + 1)
            {
                _pendingRequiredAt = nextRequiredAt;
                break;
            }

            mergedEnd = Math.Min(_maxStart, nextRequiredAt);
        }

        _nextStart = mergedStart;
        _rangeEnd = mergedEnd;
        return _nextStart <= _rangeEnd;
    }

    private int FindNextRequiredLiteral()
    {
        if (_requiredSearchAt >= _haystack.Length)
        {
            return -1;
        }

        int requiredAt = _prefilter!.FindRequiredLiteral(_haystack, _requiredSearchAt);
        if (requiredAt < 0)
        {
            _requiredSearchAt = _haystack.Length;
            return -1;
        }

        _requiredSearchAt = requiredAt + 1;
        return requiredAt;
    }

    private bool IsUtf8Boundary(int position)
    {
        return !_utf8 || RegexByteClass.IsUtf8Boundary(_haystack, position);
    }
}
