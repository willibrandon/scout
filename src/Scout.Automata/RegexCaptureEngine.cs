namespace Scout;

/// <summary>
/// Executes ordered capture-aware NFA searches with reusable runner-local state.
/// </summary>
/// <param name="nfa">The capture-aware NFA to execute.</param>
/// <param name="prefilter">The optional prefilter used to enumerate candidate starts.</param>
internal sealed class RegexCaptureEngine(RegexNfa nfa, RegexPrefilter? prefilter)
{
    private readonly RegexNfa _nfa = nfa;
    private readonly RegexPrefilter? _prefilter = prefilter;
    private readonly List<RegexCaptureClosureFrame> _closureStack = new(Math.Clamp(nfa.States.Count, 1, 16));
    private readonly int[] _initialSlots = new int[checked(2 * (nfa.CaptureCount + 1))];
    private readonly int[] _deferredAcceptSlots = new int[checked(2 * (nfa.CaptureCount + 1))];
    private readonly Dictionary<(int State, int Position), bool> _reachabilityCache = [];
    private readonly HashSet<(int State, int Position)> _reachabilityVisited = [];
    private readonly Stack<(int State, int Position)> _reachabilityPending = [];
    private RegexCaptureActiveStates _current = new(
        nfa.States.Count,
        checked(2 * (nfa.CaptureCount + 1)));
    private RegexCaptureActiveStates _next = new(
        nfa.States.Count,
        checked(2 * (nfa.CaptureCount + 1)));

    /// <summary>
    /// Finds the first match and its participating capture groups at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <param name="startAt">The first byte offset to consider.</param>
    /// <returns>The first capture result, or <see langword="null" /> when no match exists.</returns>
    public RegexCaptures? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        _reachabilityCache.Clear();
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (_prefilter?.UsesRequiredLiteralWindow == true)
        {
            Span<long> requiredRangeBuffer =
                stackalloc long[RegexCandidateStartEnumerator.RequiredLiteralRangeBufferLength];
            var candidates = RegexCandidateStartEnumerator.RequiredLiteralRanges(
                haystack,
                startOffset,
                haystack.Length,
                _nfa.Utf8,
                _prefilter,
                requiredRangeBuffer);
            while (candidates.MoveNext(out int start))
            {
                if (TryMatchAt(haystack, start, requiredEnd: -1, _deferredAcceptSlots))
                {
                    return ToCaptures(_deferredAcceptSlots);
                }
            }

            return null;
        }

        if (_prefilter is not null)
        {
            for (int start = _prefilter.FindCandidate(haystack, startOffset);
                 start >= 0;
                 start = _prefilter.FindCandidate(haystack, start + 1))
            {
                if (_nfa.Utf8 && !RegexByteClass.IsUtf8Boundary(haystack, start))
                {
                    continue;
                }

                if (TryMatchAt(haystack, start, requiredEnd: -1, _deferredAcceptSlots))
                {
                    return ToCaptures(_deferredAcceptSlots);
                }
            }

            return null;
        }

        for (int start = startOffset; start <= haystack.Length; start++)
        {
            if (_nfa.Utf8 && !RegexByteClass.IsUtf8Boundary(haystack, start))
            {
                continue;
            }

            if (TryMatchAt(haystack, start, requiredEnd: -1, _deferredAcceptSlots))
            {
                return ToCaptures(_deferredAcceptSlots);
            }
        }

        return null;
    }

    /// <summary>
    /// Replays captures for an anchored match with a known exclusive end offset.
    /// </summary>
    /// <param name="haystack">The complete haystack bytes used to evaluate predicates.</param>
    /// <param name="startAt">The anchored match start.</param>
    /// <param name="endAt">The required exclusive match end.</param>
    /// <returns>The capture result for the exact span, or <see langword="null" /> when it cannot be replayed.</returns>
    internal RegexCaptures? MatchAt(ReadOnlySpan<byte> haystack, int startAt, int endAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        int endOffset = Math.Clamp(endAt, startOffset, haystack.Length);
        if (_nfa.Utf8 && !RegexByteClass.IsUtf8Boundary(haystack, startOffset))
        {
            return null;
        }

        return TryMatchAt(haystack, startOffset, endOffset, _deferredAcceptSlots)
            ? ToCaptures(_deferredAcceptSlots)
            : null;
    }

    /// <summary>
    /// Replays captures for an anchored match into caller-owned flattened start and exclusive-end slots.
    /// </summary>
    /// <param name="haystack">The complete haystack bytes used to evaluate predicates.</param>
    /// <param name="startAt">The anchored match start.</param>
    /// <param name="endAt">The required exclusive match end.</param>
    /// <param name="captureSlots">Receives absolute start and exclusive-end offsets for every capture.</param>
    /// <returns><see langword="true" /> when the exact span can be replayed.</returns>
    internal bool TryReplayCaptures(
        ReadOnlySpan<byte> haystack,
        int startAt,
        int endAt,
        Span<int> captureSlots)
    {
        if (captureSlots.Length < _initialSlots.Length)
        {
            throw new ArgumentException("The capture slot buffer is too small.", nameof(captureSlots));
        }

        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        int endOffset = Math.Clamp(endAt, startOffset, haystack.Length);
        if (_nfa.Utf8 && !RegexByteClass.IsUtf8Boundary(haystack, startOffset))
        {
            captureSlots.Fill(-1);
            return false;
        }

        return TryMatchAt(haystack, startOffset, endOffset, captureSlots);
    }

    private bool TryMatchAt(
        ReadOnlySpan<byte> haystack,
        int start,
        int requiredEnd,
        Span<int> captureSlots)
    {
        captureSlots.Fill(-1);
        bool exactEnd = requiredEnd >= 0;
        Array.Fill(_initialSlots, -1);
        _initialSlots[0] = start;

        _current.Clear();
        _next.Clear();
        AddThread(
            _nfa.StartState,
            haystack,
            start,
            _initialSlots,
            _current);

        bool hasDeferredAccept = false;
        int deferredAcceptEnd = -1;
        while (_current.Count > 0)
        {
            int position = MinPosition(_current);
            int acceptIndex = IndexOfAccept(_current, position);
            if (acceptIndex >= 0)
            {
                if (exactEnd)
                {
                    if (position == requiredEnd)
                    {
                        CopyCaptureSlots(
                            _current.GetSlots(acceptIndex),
                            start,
                            position,
                            captureSlots);
                        return true;
                    }
                }
                else
                {
                    _current.GetSlots(acceptIndex).CopyTo(_deferredAcceptSlots);
                    hasDeferredAccept = true;
                    deferredAcceptEnd = position;
                    if (!HasEarlierConsumer(_current, acceptIndex, haystack, position))
                    {
                        CopyCaptureSlots(
                            _deferredAcceptSlots,
                            start,
                            position,
                            captureSlots);
                        return true;
                    }
                }
            }

            _next.Clear();
            for (int index = 0; index < _current.Count; index++)
            {
                CaptureThread thread = _current[index];
                Span<int> threadSlots = _current.GetSlots(index);
                if (thread.Position != position)
                {
                    if (!exactEnd || thread.Position <= requiredEnd)
                    {
                        _next.TryAddThread(
                            thread.State,
                            thread.Position,
                            threadSlots);
                    }

                    continue;
                }

                RegexNfaState state = _nfa.States[thread.State];
                if (state.Kind == RegexNfaStateKind.Atom &&
                    state.TryGetAtomMatchLength(haystack, position, out int consume) &&
                    (!exactEnd || consume <= requiredEnd - position))
                {
                    AddThread(
                        state.Next,
                        haystack,
                        position + consume,
                        threadSlots,
                        _next);
                }
                else if (position < haystack.Length &&
                    (!exactEnd || position < requiredEnd) &&
                    state.Kind == RegexNfaStateKind.Sparse &&
                    state.TryGetSparseTarget(haystack[position], out int sparseNext))
                {
                    AddThread(
                        sparseNext,
                        haystack,
                        position + 1,
                        threadSlots,
                        _next);
                }
            }

            (_current, _next) = (_next, _current);
        }

        if (hasDeferredAccept)
        {
            CopyCaptureSlots(
                _deferredAcceptSlots,
                start,
                deferredAcceptEnd,
                captureSlots);
            return true;
        }

        return false;
    }

    private void AddThread(
        int stateIndex,
        ReadOnlySpan<byte> haystack,
        int position,
        Span<int> slots,
        RegexCaptureActiveStates threads)
    {
        _closureStack.Clear();
        if (stateIndex >= 0)
        {
            _closureStack.Add(RegexCaptureClosureFrame.Explore(stateIndex));
        }

        while (_closureStack.Count > 0)
        {
            int frameIndex = _closureStack.Count - 1;
            RegexCaptureClosureFrame frame = _closureStack[frameIndex];
            _closureStack.RemoveAt(frameIndex);
            if (frame.Kind == RegexCaptureClosureFrameKind.RestoreSlot)
            {
                slots[frame.Slot] = frame.PreviousValue;
                continue;
            }

            stateIndex = frame.State;
            RegexNfaState state = _nfa.States[stateIndex];
            if (!threads.TryVisit(stateIndex, position))
            {
                continue;
            }

            switch (state.Kind)
            {
                case RegexNfaStateKind.Split:
                case RegexNfaStateKind.GreedyLoopSplit:
                case RegexNfaStateKind.LazyLoopSplit:
                    if (state.Alternative >= 0)
                    {
                        _closureStack.Add(RegexCaptureClosureFrame.Explore(state.Alternative));
                    }

                    if (state.Next >= 0)
                    {
                        _closureStack.Add(RegexCaptureClosureFrame.Explore(state.Next));
                    }

                    break;
                case RegexNfaStateKind.CaptureStart:
                    int startSlot = checked(2 * state.CaptureIndex);
                    int endSlot = startSlot + 1;
                    _closureStack.Add(RegexCaptureClosureFrame.Restore(startSlot, slots[startSlot]));
                    slots[startSlot] = position;
                    _closureStack.Add(RegexCaptureClosureFrame.Restore(endSlot, slots[endSlot]));
                    slots[endSlot] = -1;
                    if (state.Next >= 0)
                    {
                        _closureStack.Add(RegexCaptureClosureFrame.Explore(state.Next));
                    }

                    break;
                case RegexNfaStateKind.CaptureEnd:
                    int slot = checked((2 * state.CaptureIndex) + 1);
                    _closureStack.Add(RegexCaptureClosureFrame.Restore(slot, slots[slot]));
                    slots[slot] = position;
                    if (state.Next >= 0)
                    {
                        _closureStack.Add(RegexCaptureClosureFrame.Explore(state.Next));
                    }

                    break;
                case RegexNfaStateKind.Predicate:
                    if (RegexByteClass.PredicateMatches(
                        haystack,
                        position,
                        state.AtomKind,
                        state.MultiLine,
                        state.Crlf,
                        state.LineTerminator,
                        state.Utf8,
                        state.UnicodeClasses) &&
                        state.Next >= 0)
                    {
                        _closureStack.Add(RegexCaptureClosureFrame.Explore(state.Next));
                    }

                    break;
                case RegexNfaStateKind.Atom:
                case RegexNfaStateKind.Sparse:
                case RegexNfaStateKind.Accept:
                    threads.AddVisitedThread(stateIndex, position, slots);
                    break;
            }
        }
    }

    private bool HasEarlierConsumer(
        RegexCaptureActiveStates threads,
        int acceptIndex,
        ReadOnlySpan<byte> haystack,
        int position)
    {
        if (position >= haystack.Length)
        {
            return false;
        }

        for (int index = 0; index < acceptIndex; index++)
        {
            CaptureThread thread = threads[index];
            if (thread.Position != position)
            {
                continue;
            }

            RegexNfaState state = _nfa.States[thread.State];
            if (state.Kind == RegexNfaStateKind.Atom &&
                state.TryGetAtomMatchLength(haystack, position, out int consume) &&
                RegexDfaOperations.CanReachAccept(
                    _nfa,
                    state.Next,
                    haystack,
                    position + consume,
                    _reachabilityCache,
                    _reachabilityVisited,
                    _reachabilityPending))
            {
                return true;
            }

            if (state.Kind == RegexNfaStateKind.Sparse &&
                state.TryGetSparseTarget(haystack[position], out int sparseNext) &&
                RegexDfaOperations.CanReachAccept(
                    _nfa,
                    sparseNext,
                    haystack,
                    position + 1,
                    _reachabilityCache,
                    _reachabilityVisited,
                    _reachabilityPending))
            {
                return true;
            }
        }

        return false;
    }

    private int IndexOfAccept(RegexCaptureActiveStates threads, int position)
    {
        for (int index = 0; index < threads.Count; index++)
        {
            CaptureThread thread = threads[index];
            if (thread.Position == position &&
                _nfa.States[thread.State].Kind == RegexNfaStateKind.Accept)
            {
                return index;
            }
        }

        return -1;
    }

    private static int MinPosition(RegexCaptureActiveStates threads)
    {
        int position = threads[0].Position;
        for (int index = 1; index < threads.Count; index++)
        {
            position = Math.Min(position, threads[index].Position);
        }

        return position;
    }

    private static void CopyCaptureSlots(
        ReadOnlySpan<int> source,
        int start,
        int end,
        Span<int> destination)
    {
        source.CopyTo(destination);
        destination[0] = start;
        destination[1] = end;
    }

    private static RegexCaptures ToCaptures(ReadOnlySpan<int> slots)
    {
        var match = new RegexMatch(slots[0], slots[1] - slots[0]);
        var groups = new RegexMatch?[slots.Length / 2];
        groups[0] = match;
        for (int index = 1; index < groups.Length; index++)
        {
            int groupStart = slots[2 * index];
            int groupEnd = slots[(2 * index) + 1];
            if (groupStart >= 0 && groupEnd >= groupStart)
            {
                groups[index] = new RegexMatch(groupStart, groupEnd - groupStart);
            }
        }

        return new RegexCaptures(match, groups);
    }
}
