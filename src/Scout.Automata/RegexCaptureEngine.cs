namespace Scout;

/// <summary>
/// Executes capture-aware NFA searches with reusable runner-local scratch state.
/// </summary>
/// <param name="nfa">The capture-aware NFA to execute.</param>
/// <param name="prefilter">The optional prefilter used to enumerate candidate starts.</param>
internal sealed class RegexCaptureEngine(RegexNfa nfa, RegexPrefilter? prefilter)
{
    private readonly RegexNfa _nfa = nfa;
    private readonly RegexPrefilter? _prefilter = prefilter;
    private readonly List<(int StateIndex, int[] Starts, int[] Ends)> _closureStack = [];
    private readonly int[] _closureVisited = new int[nfa.States.Count];
    private readonly int[] _closureClosedSplits = new int[nfa.States.Count];
    private readonly Dictionary<(int State, int Position), bool> _reachabilityCache = [];
    private readonly HashSet<(int State, int Position)> _reachabilityVisited = [];
    private readonly Stack<(int State, int Position)> _reachabilityPending = [];
    private List<CaptureThread> _current = [];
    private List<CaptureThread> _next = [];
    private HashSet<(int State, int Position)> _currentSeen = [];
    private HashSet<(int State, int Position)> _nextSeen = [];
    private int _closureGeneration;

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
                if (TryMatchAt(haystack, start, out RegexCaptures? captures))
                {
                    return captures;
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

                if (TryMatchAt(haystack, start, out RegexCaptures? captures))
                {
                    return captures;
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

            if (TryMatchAt(haystack, start, out RegexCaptures? captures))
            {
                return captures;
            }
        }

        return null;
    }

    internal RegexCaptures? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        _reachabilityCache.Clear();
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (_nfa.Utf8 && !RegexByteClass.IsUtf8Boundary(haystack, startOffset))
        {
            return null;
        }

        return TryMatchAt(haystack, startOffset, out RegexCaptures? captures)
            ? captures
            : null;
    }

    private bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out RegexCaptures? captures)
    {
        int[] starts = CreateCaptureSlots(_nfa.CaptureCount + 1);
        int[] ends = CreateCaptureSlots(_nfa.CaptureCount + 1);
        starts[0] = start;

        _current.Clear();
        _currentSeen.Clear();
        AddThread(
            _nfa.StartState,
            haystack,
            start,
            starts,
            ends,
            _current,
            _currentSeen);

        CaptureThread? deferredAccept = null;
        while (_current.Count > 0)
        {
            int position = MinPosition(_current);
            int acceptIndex = IndexOfAccept(_current, position);
            if (acceptIndex >= 0)
            {
                CaptureThread accepted = _current[acceptIndex].WithGroupEnd(0, position);
                deferredAccept = accepted;
                if (!HasEarlierConsumer(_current, acceptIndex, haystack, position))
                {
                    captures = ToCaptures(accepted, start, position);
                    return true;
                }
            }

            _next.Clear();
            _nextSeen.Clear();
            for (int index = 0; index < _current.Count; index++)
            {
                CaptureThread thread = _current[index];
                if (thread.Position != position)
                {
                    AddThreadEntry(thread, _next, _nextSeen);
                    continue;
                }

                RegexNfaState state = _nfa.States[thread.State];
                if (state.Kind == RegexNfaStateKind.Atom &&
                    RegexByteClass.TryGetAtomMatchLength(
                        haystack,
                        position,
                        state.AtomKind,
                        state.Value.Span,
                        state.CaseInsensitive,
                        state.MultiLine,
                        state.DotMatchesNewline,
                        state.Crlf,
                        state.LineTerminator,
                        state.UnicodeClasses,
                        state.RequiresUtf8ScalarMatch,
                        state.CanUseAsciiScalarFastPath,
                        out int consume))
                {
                    AddThread(
                        state.Next,
                        haystack,
                        position + consume,
                        CloneSlots(thread.Starts),
                        CloneSlots(thread.Ends),
                        _next,
                        _nextSeen);
                }
                else if (position < haystack.Length &&
                    state.Kind == RegexNfaStateKind.Sparse &&
                    state.TryGetSparseTarget(haystack[position], out int sparseNext))
                {
                    AddThread(
                        sparseNext,
                        haystack,
                        position + 1,
                        CloneSlots(thread.Starts),
                        CloneSlots(thread.Ends),
                        _next,
                        _nextSeen);
                }
            }

            (_current, _next) = (_next, _current);
            (_currentSeen, _nextSeen) = (_nextSeen, _currentSeen);
        }

        if (deferredAccept.HasValue)
        {
            CaptureThread accepted = deferredAccept.Value;
            captures = ToCaptures(accepted, start, accepted.Position);
            return true;
        }

        captures = null;
        return false;
    }

    private void AddThread(
        int stateIndex,
        ReadOnlySpan<byte> haystack,
        int position,
        int[] starts,
        int[] ends,
        List<CaptureThread> threads,
        HashSet<(int State, int Position)> seen)
    {
        int generation = NextClosureGeneration();
        _closureStack.Clear();
        PushClosureFrame(stateIndex, starts, ends);
        while (_closureStack.Count > 0)
        {
            int frameIndex = _closureStack.Count - 1;
            (int frameStateIndex, int[] frameStarts, int[] frameEnds) = _closureStack[frameIndex];
            _closureStack.RemoveAt(frameIndex);
            stateIndex = frameStateIndex;
            starts = frameStarts;
            ends = frameEnds;
            if (stateIndex < 0)
            {
                continue;
            }

            RegexNfaState state = _nfa.States[stateIndex];
            if (_closureVisited[stateIndex] == generation)
            {
                PushClosedSplitExit(stateIndex, state, starts, ends);
                continue;
            }

            _closureVisited[stateIndex] = generation;
            switch (state.Kind)
            {
                case RegexNfaStateKind.Split:
                case RegexNfaStateKind.GreedyLoopSplit:
                case RegexNfaStateKind.LazyLoopSplit:
                    PushClosureFrame(state.Alternative, CloneSlots(starts), CloneSlots(ends));
                    PushClosureFrame(state.Next, CloneSlots(starts), CloneSlots(ends));
                    break;
                case RegexNfaStateKind.CaptureStart:
                    starts = CloneSlots(starts);
                    starts[state.CaptureIndex] = position;
                    ends = CloneSlots(ends);
                    ends[state.CaptureIndex] = -1;
                    PushClosureFrame(state.Next, starts, ends);
                    break;
                case RegexNfaStateKind.CaptureEnd:
                    ends = CloneSlots(ends);
                    ends[state.CaptureIndex] = position;
                    PushClosureFrame(state.Next, starts, ends);
                    break;
                case RegexNfaStateKind.Predicate:
                    if (RegexByteClass.PredicateMatches(haystack, position, state.AtomKind, state.MultiLine, state.Crlf, state.LineTerminator, state.Utf8, state.UnicodeClasses))
                    {
                        PushClosureFrame(state.Next, starts, ends);
                    }

                    break;
                default:
                    AddThreadEntry(new CaptureThread(stateIndex, position, starts, ends), threads, seen);
                    break;
            }
        }

        void PushClosureFrame(int nextState, int[] frameStarts, int[] frameEnds)
        {
            if (nextState >= 0)
            {
                _closureStack.Add((nextState, frameStarts, frameEnds));
            }
        }

        void PushClosedSplitExit(
            int loopStateIndex,
            RegexNfaState loopState,
            int[] frameStarts,
            int[] frameEnds)
        {
            if (_closureClosedSplits[loopStateIndex] == generation)
            {
                return;
            }

            _closureClosedSplits[loopStateIndex] = generation;
            switch (loopState.Kind)
            {
                case RegexNfaStateKind.GreedyLoopSplit:
                    PushClosureFrame(loopState.Alternative, frameStarts, frameEnds);
                    break;
                case RegexNfaStateKind.LazyLoopSplit:
                    PushClosureFrame(loopState.Next, frameStarts, frameEnds);
                    break;
            }
        }
    }

    private int NextClosureGeneration()
    {
        if (_closureGeneration == int.MaxValue)
        {
            Array.Clear(_closureVisited);
            Array.Clear(_closureClosedSplits);
            _closureGeneration = 0;
        }

        return ++_closureGeneration;
    }

    private static void AddThreadEntry(
        CaptureThread thread,
        List<CaptureThread> threads,
        HashSet<(int State, int Position)> seen)
    {
        if (seen.Add((thread.State, thread.Position)))
        {
            threads.Add(thread);
            return;
        }

        for (int index = 0; index < threads.Count; index++)
        {
            CaptureThread existing = threads[index];
            if (existing.State == thread.State &&
                existing.Position == thread.Position &&
                IsStrictCaptureExtension(thread, existing))
            {
                threads[index] = thread;
                return;
            }
        }
    }

    private static bool IsStrictCaptureExtension(CaptureThread candidate, CaptureThread existing)
    {
        int candidateCount = 0;
        int existingCount = 0;
        for (int index = 0; index < existing.Starts.Length; index++)
        {
            bool candidateParticipates = CaptureParticipates(candidate, index);
            bool existingParticipates = CaptureParticipates(existing, index);
            if (candidateParticipates)
            {
                candidateCount++;
            }

            if (!existingParticipates)
            {
                continue;
            }

            existingCount++;
            if (!candidateParticipates ||
                candidate.Starts[index] != existing.Starts[index] ||
                candidate.Ends[index] != existing.Ends[index])
            {
                return false;
            }
        }

        return candidateCount > existingCount;
    }

    private static bool CaptureParticipates(CaptureThread thread, int index)
    {
        return thread.Starts[index] >= 0 && thread.Ends[index] >= thread.Starts[index];
    }

    private static int ParticipatingCaptureCount(CaptureThread thread)
    {
        int count = 0;
        for (int index = 0; index < thread.Starts.Length; index++)
        {
            if (CaptureParticipates(thread, index))
            {
                count++;
            }
        }

        return count;
    }

    private bool HasEarlierConsumer(
        List<CaptureThread> threads,
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
                RegexByteClass.TryGetAtomMatchLength(
                    haystack,
                    position,
                    state.AtomKind,
                    state.Value.Span,
                    state.CaseInsensitive,
                    state.MultiLine,
                    state.DotMatchesNewline,
                    state.Crlf,
                    state.LineTerminator,
                    state.UnicodeClasses,
                    state.RequiresUtf8ScalarMatch,
                    state.CanUseAsciiScalarFastPath,
                out int consume) &&
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

    private int IndexOfAccept(List<CaptureThread> threads, int position)
    {
        for (int index = 0; index < threads.Count; index++)
        {
            CaptureThread thread = threads[index];
            if (thread.Position == position && _nfa.States[thread.State].Kind == RegexNfaStateKind.Accept)
            {
                return index;
            }
        }

        return -1;
    }

    private static int MinPosition(List<CaptureThread> threads)
    {
        int position = threads[0].Position;
        for (int index = 1; index < threads.Count; index++)
        {
            position = Math.Min(position, threads[index].Position);
        }

        return position;
    }

    private static RegexCaptures ToCaptures(CaptureThread accepted, int start, int end)
    {
        var match = new RegexMatch(start, end - start);
        var groups = new RegexMatch?[accepted.Starts.Length];
        groups[0] = match;
        for (int index = 1; index < groups.Length; index++)
        {
            int groupStart = accepted.Starts[index];
            int groupEnd = accepted.Ends[index];
            if (groupStart >= 0 && groupEnd >= groupStart)
            {
                groups[index] = new RegexMatch(groupStart, groupEnd - groupStart);
            }
        }

        return new RegexCaptures(match, groups);
    }

    private static int[] CreateCaptureSlots(int length)
    {
        int[] slots = new int[length];
        Array.Fill(slots, -1);
        return slots;
    }

    private static int[] CloneSlots(int[] slots)
    {
        return (int[])slots.Clone();
    }

}
