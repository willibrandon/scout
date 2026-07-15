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
/// <param name="requiredRangeBuffer">Scratch storage for ordered required-literal ranges.</param>
/// <param name="nulDetection">Optional one-element storage that records observed NUL bytes.</param>
internal ref struct RegexCandidateStartEnumerator(
    ReadOnlySpan<byte> haystack,
    RegexCandidateStartMode mode,
    int startAt,
    int maxStart,
    bool utf8,
    RegexPrefilter? prefilter,
    RegexStartPredicate? startPredicate,
    Span<long> requiredRangeBuffer,
    Span<bool> nulDetection)
{
    /// <summary>
    /// The stack scratch length required by the ordered required-literal range stream.
    /// </summary>
    internal const int RequiredLiteralRangeBufferLength = 32;

    private readonly ReadOnlySpan<byte> _haystack = haystack;
    private readonly RegexCandidateStartMode _mode = mode;
    private readonly RegexPrefilter? _prefilter = prefilter;
    private readonly RegexStartPredicate? _startPredicate = startPredicate;
    private readonly bool _hasRequiredStart = startPredicate?.HasRequiredStart ?? false;
    private readonly bool _utf8 = utf8;
    private readonly int _startAt = Math.Clamp(startAt, 0, haystack.Length);
    private readonly int _maxStart = Math.Clamp(maxStart, 0, haystack.Length);
    private int _nextStart = Math.Clamp(startAt, 0, haystack.Length);
    private int _rangeEnd = Math.Clamp(startAt, 0, haystack.Length) - 1;
    private int _requiredSearchAt = Math.Clamp(startAt, 0, haystack.Length);
    private int _pendingRequiredAt = -1;
    private Span<long> _requiredRangeBuffer = requiredRangeBuffer;
    private Span<bool> _nulDetection = nulDetection;
    private int _requiredRangeCount;
    private bool _requiredGateDisabled;

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
            startPredicate,
            requiredRangeBuffer: default,
            nulDetection: default);
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
            startPredicate: null,
            requiredRangeBuffer: default,
            nulDetection: default);
    }

    /// <summary>
    /// Creates an enumerator that streams merged required-literal lookbehind ranges.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="maxStart">The last permitted match start.</param>
    /// <param name="utf8">Whether candidates must begin on UTF-8 boundaries.</param>
    /// <param name="prefilter">The required-literal prefilter used to form ranges.</param>
    /// <param name="requiredRangeBuffer">Stack scratch used to order narrowed ranges.</param>
    /// <returns>A required-literal-range candidate enumerator.</returns>
    public static RegexCandidateStartEnumerator RequiredLiteralRanges(
        ReadOnlySpan<byte> haystack,
        int startAt,
        int maxStart,
        bool utf8,
        RegexPrefilter prefilter,
        Span<long> requiredRangeBuffer)
    {
        return new RegexCandidateStartEnumerator(
            haystack,
            RegexCandidateStartMode.RequiredLiteralRanges,
            startAt,
            maxStart,
            utf8,
            prefilter,
            startPredicate: null,
            requiredRangeBuffer,
            nulDetection: default);
    }

    /// <summary>
    /// Creates a required-literal range enumerator that detects NUL bytes in the same scan.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="maxStart">The last permitted match start.</param>
    /// <param name="utf8">Whether candidates must begin on UTF-8 boundaries.</param>
    /// <param name="prefilter">The required-literal prefilter used to form ranges.</param>
    /// <param name="requiredRangeBuffer">Stack scratch used to order narrowed ranges.</param>
    /// <param name="nulDetection">One-element storage that records observed NUL bytes.</param>
    /// <returns>A required-literal-range candidate enumerator.</returns>
    public static RegexCandidateStartEnumerator RequiredLiteralRangesAndDetectNul(
        ReadOnlySpan<byte> haystack,
        int startAt,
        int maxStart,
        bool utf8,
        RegexPrefilter prefilter,
        Span<long> requiredRangeBuffer,
        Span<bool> nulDetection)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nulDetection.Length, 1);
        return new RegexCandidateStartEnumerator(
            haystack,
            RegexCandidateStartMode.RequiredLiteralRanges,
            startAt,
            maxStart,
            utf8,
            prefilter,
            startPredicate: null,
            requiredRangeBuffer,
            nulDetection);
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

    /// <summary>
    /// Gets a value indicating whether this enumerator has active required-literal ranges
    /// in its caller-provided scratch buffer.
    /// </summary>
    internal readonly bool HasBufferedRequiredRanges => _requiredRangeCount > 0;

    private bool MoveNextEvery(out int start)
    {
        while (_nextStart <= _maxStart)
        {
            int candidate = _hasRequiredStart
                ? _startPredicate!.FindNextRequiredStart(_haystack, _nextStart, _maxStart)
                : _nextStart;
            if (candidate < 0)
            {
                break;
            }

            _nextStart = candidate + 1;
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
        if (_requiredGateDisabled || !_prefilter!.UsesRequiredLiteralPrefixGate)
        {
            return TryLoadFallbackRequiredLiteralRange();
        }

        while (true)
        {
            if (_requiredRangeCount == 0)
            {
                int requiredAt = TakeNextRequiredLiteral();
                if (requiredAt < 0)
                {
                    return false;
                }

                if (!_prefilter.TryGetRequiredLiteralRange(
                        _haystack,
                        requiredAt,
                        _startAt,
                        _maxStart,
                        out int rangeStart,
                        out int rangeEnd))
                {
                    continue;
                }

                if (!TryInsertRequiredLiteralRange(rangeStart, rangeEnd))
                {
                    return TryFallbackFromBufferedRanges(requiredAt);
                }
            }

            int earliestEnd = UnpackRangeEnd(_requiredRangeBuffer[0]);
            int nextRequiredAt = TakeNextRequiredLiteral();
            if (nextRequiredAt < 0)
            {
                return TryPopRequiredLiteralRange();
            }

            int conservativeStart = Math.Max(
                _startAt,
                nextRequiredAt - _prefilter.RequiredLiteralWindow);
            // A later hit can reverse-match to an earlier start. The earliest buffered range is
            // ordered only after every future conservative range begins beyond its end.
            if (conservativeStart > earliestEnd)
            {
                _pendingRequiredAt = nextRequiredAt;
                return TryPopRequiredLiteralRange();
            }

            if (!_prefilter.TryGetRequiredLiteralRange(
                    _haystack,
                    nextRequiredAt,
                    _startAt,
                    _maxStart,
                    out int nextRangeStart,
                    out int nextRangeEnd))
            {
                continue;
            }

            if (!TryInsertRequiredLiteralRange(nextRangeStart, nextRangeEnd))
            {
                return TryFallbackFromBufferedRanges(nextRequiredAt);
            }
        }
    }

    private bool TryLoadFallbackRequiredLiteralRange()
    {
        while (true)
        {
            int requiredAt = TakeNextRequiredLiteral();
            if (requiredAt < 0)
            {
                return false;
            }

            int rangeStart = Math.Max(
                _nextStart,
                Math.Max(_startAt, requiredAt - _prefilter!.RequiredLiteralWindow));
            int rangeEnd = Math.Min(_maxStart, requiredAt);
            if (rangeStart > rangeEnd)
            {
                if (rangeStart > _maxStart)
                {
                    return false;
                }

                continue;
            }

            return CompleteFallbackRequiredLiteralRange(rangeStart, rangeEnd);
        }
    }

    private bool TryFallbackFromBufferedRanges(int requiredAt)
    {
        // A broad range preserves ordering and completeness if pathological disjoint ranges
        // exceed the bounded stack scratch.
        _requiredGateDisabled = true;
        _requiredRangeCount = 0;
        int rangeStart = Math.Max(_startAt, _nextStart);
        int rangeEnd = Math.Min(_maxStart, requiredAt);
        if (rangeStart > rangeEnd)
        {
            return TryLoadFallbackRequiredLiteralRange();
        }

        return CompleteFallbackRequiredLiteralRange(rangeStart, rangeEnd);
    }

    private bool CompleteFallbackRequiredLiteralRange(int rangeStart, int rangeEnd)
    {
        while (true)
        {
            int nextRequiredAt = FindNextRequiredLiteral();
            if (nextRequiredAt < 0)
            {
                break;
            }

            int nextRangeStart = Math.Max(
                _startAt,
                nextRequiredAt - _prefilter!.RequiredLiteralWindow);
            if ((long)nextRangeStart > (long)rangeEnd + 1)
            {
                _pendingRequiredAt = nextRequiredAt;
                break;
            }

            rangeEnd = Math.Max(rangeEnd, Math.Min(_maxStart, nextRequiredAt));
        }

        _nextStart = rangeStart;
        _rangeEnd = rangeEnd;
        return true;
    }

    private bool TryInsertRequiredLiteralRange(int rangeStart, int rangeEnd)
    {
        int index = 0;
        while (index < _requiredRangeCount &&
            (long)UnpackRangeEnd(_requiredRangeBuffer[index]) + 1 < rangeStart)
        {
            index++;
        }

        int mergeEnd = index;
        while (mergeEnd < _requiredRangeCount &&
            UnpackRangeStart(_requiredRangeBuffer[mergeEnd]) <= (long)rangeEnd + 1)
        {
            rangeStart = Math.Min(rangeStart, UnpackRangeStart(_requiredRangeBuffer[mergeEnd]));
            rangeEnd = Math.Max(rangeEnd, UnpackRangeEnd(_requiredRangeBuffer[mergeEnd]));
            mergeEnd++;
        }

        int removed = mergeEnd - index;
        if (removed == 0 && _requiredRangeCount == _requiredRangeBuffer.Length)
        {
            return false;
        }

        if (removed == 0)
        {
            for (int source = _requiredRangeCount - 1; source >= index; source--)
            {
                _requiredRangeBuffer[source + 1] = _requiredRangeBuffer[source];
            }
        }
        else
        {
            int destination = index + 1;
            for (int source = mergeEnd; source < _requiredRangeCount; source++, destination++)
            {
                _requiredRangeBuffer[destination] = _requiredRangeBuffer[source];
            }
        }

        _requiredRangeBuffer[index] = PackRange(rangeStart, rangeEnd);
        _requiredRangeCount = _requiredRangeCount - removed + 1;
        return true;
    }

    private bool TryPopRequiredLiteralRange()
    {
        long range = _requiredRangeBuffer[0];
        for (int index = 1; index < _requiredRangeCount; index++)
        {
            _requiredRangeBuffer[index - 1] = _requiredRangeBuffer[index];
        }

        _requiredRangeCount--;
        _nextStart = Math.Max(_nextStart, UnpackRangeStart(range));
        _rangeEnd = UnpackRangeEnd(range);
        if (_nextStart <= _rangeEnd)
        {
            return true;
        }

        return TryLoadNextRequiredLiteralRange();
    }

    private int TakeNextRequiredLiteral()
    {
        int requiredAt = _pendingRequiredAt;
        _pendingRequiredAt = -1;
        return requiredAt >= 0 ? requiredAt : FindNextRequiredLiteral();
    }

    private static long PackRange(int start, int end)
    {
        return (long)(uint)start << 32 | (uint)end;
    }

    private static int UnpackRangeStart(long range)
    {
        return (int)(range >> 32);
    }

    private static int UnpackRangeEnd(long range)
    {
        return (int)range;
    }

    private int FindNextRequiredLiteral()
    {
        if (_requiredSearchAt >= _haystack.Length)
        {
            return -1;
        }

        int requiredAt = _nulDetection.IsEmpty
            ? _prefilter!.FindRequiredLiteral(_haystack, _requiredSearchAt)
            : _prefilter!.FindRequiredLiteralAndDetectNul(
                _haystack,
                _requiredSearchAt,
                ref _nulDetection[0]);
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
