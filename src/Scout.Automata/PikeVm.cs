using System.Runtime.CompilerServices;

namespace Scout;

/// <summary>
/// Executes reusable Thompson NFA searches with leftmost-first PikeVM semantics.
/// </summary>
/// <param name="nfa">The compiled NFA executed by this runner.</param>
internal sealed class PikeVm(RegexNfa nfa)
{
    // A transition consumes either one byte or one UTF-8 scalar, so a frontier
    // can contain at most four consecutive positions while its minimum advances.
    private const int ActivePositionSlotCount = 4;

    private readonly RegexNfa _nfa = nfa;
    private readonly RegexNfaState[] _states = nfa.States as RegexNfaState[] ?? [.. nfa.States];
    private readonly RegexNfaStateKind[] _stateKinds = CreateStateKinds(nfa);
    private readonly int[] _nextStates = CreateNextStates(nfa);
    private readonly int[] _alternativeStates = CreateAlternativeStates(nfa);
    private readonly int _startState = nfa.StartState;
    private readonly bool _utf8 = nfa.Utf8;
    private readonly bool _hasCyclicZeroWidthGraph = HasCyclicZeroWidthGraph(nfa);
    private readonly int[]? _asciiStartClosure = CreateAsciiStartClosure(nfa);
    private readonly ulong[] _asciiAtomMatches = CreateAsciiAtomMatches(nfa);
    private readonly int[] _closureVisited = new int[nfa.States.Count];
    private readonly int[] _closureClosedSplits = new int[nfa.States.Count];
    private readonly Dictionary<(int State, int Position), bool> _reachabilityCache = [];
    private readonly HashSet<(int State, int Position)> _reachabilityVisited = [];
    private readonly Stack<(int State, int Position)> _reachabilityPending = [];
    private int[] _asciiCurrentStates = new int[nfa.States.Count];
    private int[] _asciiCurrentStarts = new int[nfa.States.Count];
    private int[] _asciiNextStates = new int[nfa.States.Count];
    private int[] _asciiNextStarts = new int[nfa.States.Count];
    private int[] _asciiCurrentSeen = new int[nfa.States.Count];
    private int[] _asciiNextSeen = new int[nfa.States.Count];
    private List<PikeVmThread> _current = [];
    private List<PikeVmThread> _next = [];
    // A closure pushes at most two edges per first state visit and one closed-loop
    // exit per state, so four entries per NFA state leave a strict reusable bound.
    private readonly int[] _closureStack = new int[checked(Math.Max(1, nfa.States.Count * 4))];
    private long[] _currentSeen = new long[checked(nfa.States.Count * ActivePositionSlotCount)];
    private long[] _nextSeen = new long[checked(nfa.States.Count * ActivePositionSlotCount)];
    private int _closureGeneration;
    private int _threadSetGeneration;
    private int _currentSeenGeneration;
    private int _nextSeenGeneration;
    private int _currentMinimumPosition;
    private int _currentMaximumPosition;
    private int _nextMinimumPosition;
    private int _nextMaximumPosition;
    private int _asciiThreadSetGeneration;
    private int _asciiCurrentSeenGeneration;
    private int _asciiNextSeenGeneration;
    private int _asciiCurrentCount;
    private int _asciiNextCount;

    /// <summary>
    /// Attempts a leftmost-first match anchored at a byte offset.
    /// </summary>
    /// <param name="haystack">The bytes to match.</param>
    /// <param name="start">The anchored start offset.</param>
    /// <param name="length">The matched byte length when successful.</param>
    /// <returns><see langword="true" /> when the pattern matches at <paramref name="start" />.</returns>
    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        return TryMatchAt(haystack, start, earliest: false, out length);
    }

    /// <summary>
    /// Attempts an earliest-ending match anchored at a byte offset.
    /// </summary>
    /// <param name="haystack">The bytes to match.</param>
    /// <param name="start">The anchored start offset.</param>
    /// <param name="length">The earliest matched byte length when successful.</param>
    /// <returns><see langword="true" /> when the pattern matches at <paramref name="start" />.</returns>
    public bool TryMatchEarliestAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        return TryMatchAt(haystack, start, earliest: true, out length);
    }

    /// <summary>
    /// Attempts a longest match anchored at a byte offset.
    /// </summary>
    /// <param name="haystack">The bytes to match.</param>
    /// <param name="start">The anchored start offset.</param>
    /// <param name="length">The longest matched byte length when successful.</param>
    /// <returns><see langword="true" /> when the pattern matches at <paramref name="start" />.</returns>
    public bool TryMatchLongestAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        ResetCurrentThreadSet();
        AddThread(
            _startState,
            haystack,
            start,
            start,
            _current,
            _currentSeen,
            _currentSeenGeneration,
            ref _currentMinimumPosition,
            ref _currentMaximumPosition);
        int longestAcceptLength = -1;
        while (_current.Count > 0)
        {
            int position = _currentMinimumPosition;
            if (IndexOfAccept(_current, position) >= 0)
            {
                longestAcceptLength = Math.Max(longestAcceptLength, position - start);
            }

            ResetNextThreadSet();
            for (int index = 0; index < _current.Count; index++)
            {
                (int stateIndex, int threadPosition, int threadStart) = _current[index];
                if (threadPosition != position)
                {
                    AddThreadEntry(
                        stateIndex,
                        threadPosition,
                        threadStart,
                        _next,
                        _nextSeen,
                        _nextSeenGeneration,
                        ref _nextMinimumPosition,
                        ref _nextMaximumPosition);
                    continue;
                }

                AddConsumerThread(
                    stateIndex,
                    haystack,
                    position,
                    threadStart,
                    _next,
                    _nextSeen,
                    _nextSeenGeneration,
                    ref _nextMinimumPosition,
                    ref _nextMaximumPosition);
            }

            SwapThreadSets();
        }

        if (longestAcceptLength >= 0)
        {
            length = longestAcceptLength;
            return true;
        }

        length = 0;
        return false;
    }

    /// <summary>
    /// Adds every distinct match length found at an anchored byte offset.
    /// </summary>
    /// <param name="haystack">The bytes to match.</param>
    /// <param name="start">The anchored start offset.</param>
    /// <param name="lengths">The collection that receives distinct match lengths.</param>
    public void AddMatchLengthsAt(ReadOnlySpan<byte> haystack, int start, List<int> lengths)
    {
        ResetCurrentThreadSet();
        AddThread(
            _startState,
            haystack,
            start,
            start,
            _current,
            _currentSeen,
            _currentSeenGeneration,
            ref _currentMinimumPosition,
            ref _currentMaximumPosition);
        while (_current.Count > 0)
        {
            int position = _currentMinimumPosition;
            if (IndexOfAccept(_current, position) >= 0)
            {
                int length = position - start;
                if (!lengths.Contains(length))
                {
                    lengths.Add(length);
                }
            }

            ResetNextThreadSet();
            for (int index = 0; index < _current.Count; index++)
            {
                (int stateIndex, int threadPosition, int threadStart) = _current[index];
                if (threadPosition != position)
                {
                    AddThreadEntry(
                        stateIndex,
                        threadPosition,
                        threadStart,
                        _next,
                        _nextSeen,
                        _nextSeenGeneration,
                        ref _nextMinimumPosition,
                        ref _nextMaximumPosition);
                    continue;
                }

                AddConsumerThread(
                    stateIndex,
                    haystack,
                    position,
                    threadStart,
                    _next,
                    _nextSeen,
                    _nextSeenGeneration,
                    ref _nextMinimumPosition,
                    ref _nextMaximumPosition);
            }

            SwapThreadSets();
        }
    }

    /// <summary>
    /// Finds the first leftmost match among a monotonically increasing stream of candidate starts.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="candidates">The candidate match starts to examine.</param>
    /// <returns>The leftmost-first match, or <see langword="null" /> when no candidate matches.</returns>
    public RegexMatch? Find(ReadOnlySpan<byte> haystack, ref RegexCandidateStartEnumerator candidates)
    {
        return haystack.IndexOfAnyExceptInRange((byte)0x00, (byte)0x7F) < 0
            ? FindAscii(haystack, ref candidates)
            : FindVariableWidth(haystack, ref candidates);
    }

    private RegexMatch? FindAscii(
        ReadOnlySpan<byte> haystack,
        ref RegexCandidateStartEnumerator candidates)
    {
        ResetAsciiCurrentThreadSet();

        bool hasCandidate = candidates.MoveNext(out int candidate);
        RegexMatch? deferredMatch = null;
        int position = 0;
        while (_asciiCurrentCount > 0 || hasCandidate)
        {
            if (_asciiCurrentCount == 0)
            {
                if (deferredMatch.HasValue)
                {
                    return deferredMatch;
                }

                ResetAsciiCurrentThreadSet();
                position = candidate;
                AddAsciiStartThread(
                    haystack,
                    position,
                    candidate,
                    _asciiCurrentStates,
                    _asciiCurrentStarts,
                    _asciiCurrentSeen,
                    _asciiCurrentSeenGeneration,
                    ref _asciiCurrentCount);
                hasCandidate = candidates.MoveNext(out candidate);
                if (_asciiCurrentCount == 0)
                {
                    continue;
                }
            }

            while (!deferredMatch.HasValue && hasCandidate && candidate <= position)
            {
                AddAsciiStartThread(
                    haystack,
                    position,
                    candidate,
                    _asciiCurrentStates,
                    _asciiCurrentStarts,
                    _asciiCurrentSeen,
                    _asciiCurrentSeenGeneration,
                    ref _asciiCurrentCount);
                hasCandidate = candidates.MoveNext(out candidate);
            }

            ResetAsciiNextThreadSet();
            bool hasByte = position < haystack.Length;
            byte value = hasByte ? haystack[position] : (byte)0;
            for (int index = 0; index < _asciiCurrentCount; index++)
            {
                int stateIndex = _asciiCurrentStates[index];
                int threadStart = _asciiCurrentStarts[index];
                RegexNfaStateKind stateKind = _stateKinds[stateIndex];
                if (stateKind == RegexNfaStateKind.Accept)
                {
                    deferredMatch = new RegexMatch(threadStart, position - threadStart);
                    break;
                }

                if (stateKind == RegexNfaStateKind.Atom)
                {
                    if (hasByte && AsciiAtomMatches(stateIndex, value))
                    {
                        AddAsciiThread(
                            _nextStates[stateIndex],
                            haystack,
                            position + 1,
                            threadStart,
                            _asciiNextStates,
                            _asciiNextStarts,
                            _asciiNextSeen,
                            _asciiNextSeenGeneration,
                            ref _asciiNextCount);
                    }

                    continue;
                }

                if (hasByte &&
                    stateKind == RegexNfaStateKind.Sparse &&
                    _states[stateIndex].TryGetSparseTarget(value, out int sparseNext))
                {
                    AddAsciiThread(
                        sparseNext,
                        haystack,
                        position + 1,
                        threadStart,
                        _asciiNextStates,
                        _asciiNextStarts,
                        _asciiNextSeen,
                        _asciiNextSeenGeneration,
                        ref _asciiNextCount);
                }
            }

            SwapAsciiThreadSets();
            if (_asciiCurrentCount > 0)
            {
                position++;
            }
        }

        return deferredMatch;
    }

    private RegexMatch? FindVariableWidth(
        ReadOnlySpan<byte> haystack,
        ref RegexCandidateStartEnumerator candidates)
    {
        ResetCurrentThreadSet();

        bool hasCandidate = TryMoveNextCandidate(haystack, ref candidates, out int candidate);
        RegexMatch? deferredMatch = null;
        while (_current.Count > 0 || hasCandidate)
        {
            if (_current.Count == 0)
            {
                if (deferredMatch.HasValue)
                {
                    return deferredMatch;
                }

                AddThread(
                    _startState,
                    haystack,
                    candidate,
                    candidate,
                    _current,
                    _currentSeen,
                    _currentSeenGeneration,
                    ref _currentMinimumPosition,
                    ref _currentMaximumPosition);
                hasCandidate = TryMoveNextCandidate(haystack, ref candidates, out candidate);
                if (_current.Count == 0)
                {
                    continue;
                }
            }

            int position = _currentMinimumPosition;
            while (!deferredMatch.HasValue && hasCandidate && candidate <= position)
            {
                AddThread(
                    _startState,
                    haystack,
                    candidate,
                    candidate,
                    _current,
                    _currentSeen,
                    _currentSeenGeneration,
                    ref _currentMinimumPosition,
                    ref _currentMaximumPosition);
                hasCandidate = TryMoveNextCandidate(haystack, ref candidates, out candidate);
                position = _currentMinimumPosition;
            }

            ResetNextThreadSet();
            if (_currentMaximumPosition == position)
            {
                for (int index = 0; index < _current.Count; index++)
                {
                    (int stateIndex, _, int threadStart) = _current[index];
                    RegexNfaState state = _states[stateIndex];
                    if (state.Kind == RegexNfaStateKind.Accept)
                    {
                        deferredMatch = new RegexMatch(threadStart, position - threadStart);
                        break;
                    }

                    AddConsumerThread(
                        stateIndex,
                        haystack,
                        position,
                        threadStart,
                        _next,
                        _nextSeen,
                        _nextSeenGeneration,
                        ref _nextMinimumPosition,
                        ref _nextMaximumPosition);
                }
            }
            else
            {
                for (int index = 0; index < _current.Count; index++)
                {
                    (int stateIndex, int threadPosition, int threadStart) = _current[index];
                    if (threadPosition != position)
                    {
                        AddThreadEntry(
                            stateIndex,
                            threadPosition,
                            threadStart,
                            _next,
                            _nextSeen,
                            _nextSeenGeneration,
                            ref _nextMinimumPosition,
                            ref _nextMaximumPosition);
                        continue;
                    }

                    RegexNfaState state = _states[stateIndex];
                    if (state.Kind == RegexNfaStateKind.Accept)
                    {
                        deferredMatch = new RegexMatch(threadStart, position - threadStart);
                        break;
                    }

                    AddConsumerThread(
                        stateIndex,
                        haystack,
                        position,
                        threadStart,
                        _next,
                        _nextSeen,
                        _nextSeenGeneration,
                        ref _nextMinimumPosition,
                        ref _nextMaximumPosition);
                }
            }

            SwapThreadSets();
        }

        return deferredMatch;
    }

    private bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, bool earliest, out int length)
    {
        ResetCurrentThreadSet();
        _reachabilityCache.Clear();
        AddThread(
            _startState,
            haystack,
            start,
            start,
            _current,
            _currentSeen,
            _currentSeenGeneration,
            ref _currentMinimumPosition,
            ref _currentMaximumPosition);
        int deferredAcceptLength = -1;
        while (_current.Count > 0)
        {
            int position = _currentMinimumPosition;
            int acceptIndex = IndexOfAccept(_current, position);
            if (acceptIndex >= 0)
            {
                deferredAcceptLength = position - start;
                if (earliest || !HasEarlierConsumer(_current, acceptIndex, haystack, position))
                {
                    length = deferredAcceptLength;
                    return true;
                }
            }

            ResetNextThreadSet();
            for (int index = 0; index < _current.Count; index++)
            {
                (int stateIndex, int threadPosition, int threadStart) = _current[index];
                if (threadPosition != position)
                {
                    AddThreadEntry(
                        stateIndex,
                        threadPosition,
                        threadStart,
                        _next,
                        _nextSeen,
                        _nextSeenGeneration,
                        ref _nextMinimumPosition,
                        ref _nextMaximumPosition);
                    continue;
                }

                AddConsumerThread(
                    stateIndex,
                    haystack,
                    position,
                    threadStart,
                    _next,
                    _nextSeen,
                    _nextSeenGeneration,
                    ref _nextMinimumPosition,
                    ref _nextMaximumPosition);
            }

            SwapThreadSets();
        }

        if (deferredAcceptLength >= 0)
        {
            length = deferredAcceptLength;
            return true;
        }

        length = 0;
        return false;
    }

    private void AddConsumerThread(
        int stateIndex,
        ReadOnlySpan<byte> haystack,
        int position,
        int start,
        List<PikeVmThread> threads,
        long[] seen,
        int seenGeneration,
        ref int minimumPosition,
        ref int maximumPosition)
    {
        RegexNfaState state = _states[stateIndex];
        if (state.Kind == RegexNfaStateKind.Atom &&
            position < haystack.Length &&
            haystack[position] <= 0x7F)
        {
            if (AsciiAtomMatches(stateIndex, haystack[position]))
            {
                AddThread(
                    state.Next,
                    haystack,
                    position + 1,
                    start,
                    threads,
                    seen,
                    seenGeneration,
                    ref minimumPosition,
                    ref maximumPosition);
            }
        }
        else if (state.Kind == RegexNfaStateKind.Atom &&
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
                start,
                threads,
                seen,
                seenGeneration,
                ref minimumPosition,
                ref maximumPosition);
        }
        else if (position < haystack.Length &&
            state.Kind == RegexNfaStateKind.Sparse &&
            state.TryGetSparseTarget(haystack[position], out int sparseNext))
        {
            AddThread(
                sparseNext,
                haystack,
                position + 1,
                start,
                threads,
                seen,
                seenGeneration,
                ref minimumPosition,
                ref maximumPosition);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool AsciiAtomMatches(int stateIndex, byte value)
    {
        int word = (stateIndex * 2) + (value >> 6);
        return (_asciiAtomMatches[word] & (1UL << (value & 0x3F))) != 0;
    }

    private void AddAsciiStartThread(
        ReadOnlySpan<byte> haystack,
        int position,
        int start,
        int[] states,
        int[] starts,
        int[] seen,
        int seenGeneration,
        ref int count)
    {
        if (_asciiStartClosure is null)
        {
            AddAsciiThread(
                _startState,
                haystack,
                position,
                start,
                states,
                starts,
                seen,
                seenGeneration,
                ref count);
            return;
        }

        for (int index = 0; index < _asciiStartClosure.Length; index++)
        {
            int stateIndex = _asciiStartClosure[index];
            if (seen[stateIndex] == seenGeneration)
            {
                continue;
            }

            seen[stateIndex] = seenGeneration;
            states[count] = stateIndex;
            starts[count] = start;
            count++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddAsciiThread(
        int stateIndex,
        ReadOnlySpan<byte> haystack,
        int position,
        int start,
        int[] states,
        int[] starts,
        int[] seen,
        int seenGeneration,
        ref int count)
    {
        if (!_hasCyclicZeroWidthGraph)
        {
            AddAsciiAcyclicThread(
                stateIndex,
                haystack,
                position,
                start,
                states,
                starts,
                seen,
                seenGeneration,
                ref count);
            return;
        }

        AddAsciiCyclicThread(
            stateIndex,
            haystack,
            position,
            start,
            states,
            starts,
            seen,
            seenGeneration,
            ref count);
    }

    private void AddAsciiCyclicThread(
        int stateIndex,
        ReadOnlySpan<byte> haystack,
        int position,
        int start,
        int[] states,
        int[] starts,
        int[] seen,
        int seenGeneration,
        ref int count)
    {
        int generation = NextClosureGeneration();
        int closureCount = 0;
        if (stateIndex >= 0)
        {
            _closureStack[closureCount++] = stateIndex;
        }

        while (closureCount > 0)
        {
            stateIndex = _closureStack[--closureCount];
            RegexNfaStateKind stateKind = _stateKinds[stateIndex];
            if (_closureVisited[stateIndex] == generation)
            {
                if (_closureClosedSplits[stateIndex] != generation)
                {
                    _closureClosedSplits[stateIndex] = generation;
                    int loopExit = stateKind switch
                    {
                        RegexNfaStateKind.GreedyLoopSplit => _alternativeStates[stateIndex],
                        RegexNfaStateKind.LazyLoopSplit => _nextStates[stateIndex],
                        _ => -1,
                    };
                    if (loopExit >= 0)
                    {
                        _closureStack[closureCount++] = loopExit;
                    }
                }

                continue;
            }

            if (seen[stateIndex] == seenGeneration)
            {
                continue;
            }

            seen[stateIndex] = seenGeneration;
            _closureVisited[stateIndex] = generation;
            switch (stateKind)
            {
                case RegexNfaStateKind.Split:
                case RegexNfaStateKind.GreedyLoopSplit:
                case RegexNfaStateKind.LazyLoopSplit:
                    int alternative = _alternativeStates[stateIndex];
                    if (alternative >= 0)
                    {
                        _closureStack[closureCount++] = alternative;
                    }

                    int next = _nextStates[stateIndex];
                    if (next >= 0)
                    {
                        _closureStack[closureCount++] = next;
                    }

                    break;
                case RegexNfaStateKind.Predicate:
                    RegexNfaState state = _states[stateIndex];
                    if (RegexByteClass.PredicateMatches(haystack, position, state.AtomKind, state.MultiLine, state.Crlf, state.LineTerminator, state.Utf8, state.UnicodeClasses))
                    {
                        int predicateNext = _nextStates[stateIndex];
                        if (predicateNext >= 0)
                        {
                            _closureStack[closureCount++] = predicateNext;
                        }
                    }

                    break;
                default:
                    states[count] = stateIndex;
                    starts[count] = start;
                    count++;
                    break;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddAsciiAcyclicThread(
        int stateIndex,
        ReadOnlySpan<byte> haystack,
        int position,
        int start,
        int[] states,
        int[] starts,
        int[] seen,
        int seenGeneration,
        ref int count)
    {
        int closureCount = 0;
        if (stateIndex >= 0)
        {
            _closureStack[closureCount++] = stateIndex;
        }

        while (closureCount > 0)
        {
            stateIndex = _closureStack[--closureCount];
            if (seen[stateIndex] == seenGeneration)
            {
                continue;
            }

            seen[stateIndex] = seenGeneration;
            RegexNfaStateKind stateKind = _stateKinds[stateIndex];
            switch (stateKind)
            {
                case RegexNfaStateKind.Split:
                case RegexNfaStateKind.GreedyLoopSplit:
                case RegexNfaStateKind.LazyLoopSplit:
                    int alternative = _alternativeStates[stateIndex];
                    if (alternative >= 0)
                    {
                        _closureStack[closureCount++] = alternative;
                    }

                    int next = _nextStates[stateIndex];
                    if (next >= 0)
                    {
                        _closureStack[closureCount++] = next;
                    }

                    break;
                case RegexNfaStateKind.Predicate:
                    RegexNfaState state = _states[stateIndex];
                    if (RegexByteClass.PredicateMatches(haystack, position, state.AtomKind, state.MultiLine, state.Crlf, state.LineTerminator, state.Utf8, state.UnicodeClasses))
                    {
                        int predicateNext = _nextStates[stateIndex];
                        if (predicateNext >= 0)
                        {
                            _closureStack[closureCount++] = predicateNext;
                        }
                    }

                    break;
                default:
                    states[count] = stateIndex;
                    starts[count] = start;
                    count++;
                    break;
            }
        }
    }

    private void AddThread(
        int stateIndex,
        ReadOnlySpan<byte> haystack,
        int position,
        int start,
        List<PikeVmThread> threads,
        long[] seen,
        int seenGeneration,
        ref int minimumPosition,
        ref int maximumPosition)
    {
        int generation = NextClosureGeneration();
        int closureCount = 0;
        PushClosureState(stateIndex);
        while (closureCount > 0)
        {
            stateIndex = _closureStack[--closureCount];
            RegexNfaState state = _states[stateIndex];
            if (_closureVisited[stateIndex] == generation)
            {
                PushClosedSplitExit(stateIndex, state);
                continue;
            }

            if (!TryAddThreadState(stateIndex, position, seen, seenGeneration))
            {
                continue;
            }

            _closureVisited[stateIndex] = generation;
            switch (state.Kind)
            {
                case RegexNfaStateKind.Split:
                case RegexNfaStateKind.GreedyLoopSplit:
                case RegexNfaStateKind.LazyLoopSplit:
                    PushClosureState(state.Alternative);
                    PushClosureState(state.Next);
                    break;
                case RegexNfaStateKind.Predicate:
                    if (RegexByteClass.PredicateMatches(haystack, position, state.AtomKind, state.MultiLine, state.Crlf, state.LineTerminator, state.Utf8, state.UnicodeClasses))
                    {
                        PushClosureState(state.Next);
                    }

                    break;
                default:
                    threads.Add(new PikeVmThread(stateIndex, position, start));
                    minimumPosition = Math.Min(minimumPosition, position);
                    maximumPosition = Math.Max(maximumPosition, position);
                    break;
            }
        }

        void PushClosureState(int nextState)
        {
            if (nextState >= 0)
            {
                _closureStack[closureCount++] = nextState;
            }
        }

        void PushClosedSplitExit(int loopStateIndex, RegexNfaState loopState)
        {
            if (_closureClosedSplits[loopStateIndex] == generation)
            {
                return;
            }

            _closureClosedSplits[loopStateIndex] = generation;
            switch (loopState.Kind)
            {
                case RegexNfaStateKind.GreedyLoopSplit:
                    PushClosureState(loopState.Alternative);
                    break;
                case RegexNfaStateKind.LazyLoopSplit:
                    PushClosureState(loopState.Next);
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

    private int NextThreadSetGeneration()
    {
        if (_threadSetGeneration == int.MaxValue)
        {
            Array.Clear(_currentSeen);
            Array.Clear(_nextSeen);
            _threadSetGeneration = 0;
        }

        return ++_threadSetGeneration;
    }

    private int NextAsciiThreadSetGeneration()
    {
        if (_asciiThreadSetGeneration == int.MaxValue)
        {
            Array.Clear(_asciiCurrentSeen);
            Array.Clear(_asciiNextSeen);
            _asciiThreadSetGeneration = 0;
        }

        return ++_asciiThreadSetGeneration;
    }

    private static void AddThreadEntry(
        int stateIndex,
        int position,
        int start,
        List<PikeVmThread> threads,
        long[] seen,
        int seenGeneration,
        ref int minimumPosition,
        ref int maximumPosition)
    {
        if (TryAddThreadState(stateIndex, position, seen, seenGeneration))
        {
            threads.Add(new PikeVmThread(stateIndex, position, start));
            minimumPosition = Math.Min(minimumPosition, position);
            maximumPosition = Math.Max(maximumPosition, position);
        }
    }

    private static bool TryAddThreadState(int stateIndex, int position, long[] seen, int seenGeneration)
    {
        int slot = (stateIndex * ActivePositionSlotCount) + (position & (ActivePositionSlotCount - 1));
        long key = ((long)seenGeneration << 32) | (uint)position;
        if (seen[slot] == key)
        {
            return false;
        }

        seen[slot] = key;
        return true;
    }

    private bool HasEarlierConsumer(
        List<PikeVmThread> threads,
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
            if (threads[index].Position != position)
            {
                continue;
            }

            RegexNfaState state = _states[threads[index].State];
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

    private int IndexOfAccept(List<PikeVmThread> threads, int position)
    {
        for (int index = 0; index < threads.Count; index++)
        {
            (int stateIndex, int threadPosition, _) = threads[index];
            if (threadPosition == position && _states[stateIndex].Kind == RegexNfaStateKind.Accept)
            {
                return index;
            }
        }

        return -1;
    }

    private void ResetCurrentThreadSet()
    {
        _current.Clear();
        _currentSeenGeneration = NextThreadSetGeneration();
        _currentMinimumPosition = int.MaxValue;
        _currentMaximumPosition = int.MinValue;
    }

    private void ResetNextThreadSet()
    {
        _next.Clear();
        _nextSeenGeneration = NextThreadSetGeneration();
        _nextMinimumPosition = int.MaxValue;
        _nextMaximumPosition = int.MinValue;
    }

    private void ResetAsciiCurrentThreadSet()
    {
        _asciiCurrentCount = 0;
        _asciiCurrentSeenGeneration = NextAsciiThreadSetGeneration();
    }

    private void ResetAsciiNextThreadSet()
    {
        _asciiNextCount = 0;
        _asciiNextSeenGeneration = NextAsciiThreadSetGeneration();
    }

    private static ulong[] CreateAsciiAtomMatches(RegexNfa nfa)
    {
        ulong[] matches = new ulong[checked(nfa.States.Count * 2)];
        byte[] haystack = new byte[1];
        for (int stateIndex = 0; stateIndex < nfa.States.Count; stateIndex++)
        {
            RegexNfaState state = nfa.States[stateIndex];
            if (state.Kind != RegexNfaStateKind.Atom)
            {
                continue;
            }

            for (int value = 0; value <= 0x7F; value++)
            {
                haystack[0] = (byte)value;
                if (RegexByteClass.TryGetAtomMatchLength(
                        haystack,
                        position: 0,
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
                        out int length) &&
                    length == 1)
                {
                    int word = (stateIndex * 2) + (value >> 6);
                    matches[word] |= 1UL << (value & 0x3F);
                }
            }
        }

        return matches;
    }

    private static RegexNfaStateKind[] CreateStateKinds(RegexNfa nfa)
    {
        var kinds = new RegexNfaStateKind[nfa.States.Count];
        for (int stateIndex = 0; stateIndex < kinds.Length; stateIndex++)
        {
            kinds[stateIndex] = nfa.States[stateIndex].Kind;
        }

        return kinds;
    }

    private static int[] CreateNextStates(RegexNfa nfa)
    {
        int[] nextStates = new int[nfa.States.Count];
        for (int stateIndex = 0; stateIndex < nextStates.Length; stateIndex++)
        {
            nextStates[stateIndex] = nfa.States[stateIndex].Next;
        }

        return nextStates;
    }

    private static int[] CreateAlternativeStates(RegexNfa nfa)
    {
        int[] alternativeStates = new int[nfa.States.Count];
        for (int stateIndex = 0; stateIndex < alternativeStates.Length; stateIndex++)
        {
            alternativeStates[stateIndex] = nfa.States[stateIndex].Alternative;
        }

        return alternativeStates;
    }

    private static bool HasCyclicZeroWidthGraph(RegexNfa nfa)
    {
        int stateCount = nfa.States.Count;
        int[] indegrees = new int[stateCount];
        for (int stateIndex = 0; stateIndex < stateCount; stateIndex++)
        {
            RegexNfaState state = nfa.States[stateIndex];
            switch (state.Kind)
            {
                case RegexNfaStateKind.Split:
                case RegexNfaStateKind.GreedyLoopSplit:
                case RegexNfaStateKind.LazyLoopSplit:
                    if (state.Next >= 0)
                    {
                        indegrees[state.Next]++;
                    }

                    if (state.Alternative >= 0)
                    {
                        indegrees[state.Alternative]++;
                    }

                    break;
                case RegexNfaStateKind.Predicate:
                    if (state.Next >= 0)
                    {
                        indegrees[state.Next]++;
                    }

                    break;
            }
        }

        int[] queue = new int[stateCount];
        int queueStart = 0;
        int queueEnd = 0;
        for (int stateIndex = 0; stateIndex < stateCount; stateIndex++)
        {
            if (indegrees[stateIndex] == 0)
            {
                queue[queueEnd++] = stateIndex;
            }
        }

        int visitedCount = 0;
        while (queueStart < queueEnd)
        {
            int stateIndex = queue[queueStart++];
            visitedCount++;
            RegexNfaState state = nfa.States[stateIndex];
            switch (state.Kind)
            {
                case RegexNfaStateKind.Split:
                case RegexNfaStateKind.GreedyLoopSplit:
                case RegexNfaStateKind.LazyLoopSplit:
                    RemoveEdge(state.Next);
                    RemoveEdge(state.Alternative);
                    break;
                case RegexNfaStateKind.Predicate:
                    RemoveEdge(state.Next);
                    break;
            }
        }

        return visitedCount != stateCount;

        void RemoveEdge(int target)
        {
            if (target >= 0 && --indegrees[target] == 0)
            {
                queue[queueEnd++] = target;
            }
        }
    }

    private static int[]? CreateAsciiStartClosure(RegexNfa nfa)
    {
        if (HasCyclicZeroWidthGraph(nfa))
        {
            return null;
        }

        bool[] seen = new bool[nfa.States.Count];
        int[] pending = new int[checked(Math.Max(1, (nfa.States.Count * 2) + 1))];
        int pendingCount = 0;
        if (nfa.StartState >= 0)
        {
            pending[pendingCount++] = nfa.StartState;
        }

        List<int> closure = [];
        while (pendingCount > 0)
        {
            int stateIndex = pending[--pendingCount];
            if (seen[stateIndex])
            {
                continue;
            }

            seen[stateIndex] = true;
            RegexNfaState state = nfa.States[stateIndex];
            switch (state.Kind)
            {
                case RegexNfaStateKind.Split:
                case RegexNfaStateKind.GreedyLoopSplit:
                case RegexNfaStateKind.LazyLoopSplit:
                    if (state.Alternative >= 0)
                    {
                        pending[pendingCount++] = state.Alternative;
                    }

                    if (state.Next >= 0)
                    {
                        pending[pendingCount++] = state.Next;
                    }

                    break;
                case RegexNfaStateKind.Predicate:
                    return null;
                default:
                    closure.Add(stateIndex);
                    break;
            }
        }

        return closure.ToArray();
    }

    private bool TryMoveNextCandidate(
        ReadOnlySpan<byte> haystack,
        ref RegexCandidateStartEnumerator candidates,
        out int candidate)
    {
        while (candidates.MoveNext(out candidate))
        {
            if (candidate >= 0 &&
                candidate <= haystack.Length &&
                (!_utf8 || RegexByteClass.IsUtf8Boundary(haystack, candidate)))
            {
                return true;
            }
        }

        candidate = -1;
        return false;
    }

    private void SwapThreadSets()
    {
        (_current, _next) = (_next, _current);
        (_currentSeen, _nextSeen) = (_nextSeen, _currentSeen);
        (_currentSeenGeneration, _nextSeenGeneration) = (_nextSeenGeneration, _currentSeenGeneration);
        (_currentMinimumPosition, _nextMinimumPosition) = (_nextMinimumPosition, _currentMinimumPosition);
        (_currentMaximumPosition, _nextMaximumPosition) = (_nextMaximumPosition, _currentMaximumPosition);
    }

    private void SwapAsciiThreadSets()
    {
        (_asciiCurrentStates, _asciiNextStates) = (_asciiNextStates, _asciiCurrentStates);
        (_asciiCurrentStarts, _asciiNextStarts) = (_asciiNextStarts, _asciiCurrentStarts);
        (_asciiCurrentSeen, _asciiNextSeen) = (_asciiNextSeen, _asciiCurrentSeen);
        (_asciiCurrentSeenGeneration, _asciiNextSeenGeneration) = (_asciiNextSeenGeneration, _asciiCurrentSeenGeneration);
        (_asciiCurrentCount, _asciiNextCount) = (_asciiNextCount, _asciiCurrentCount);
    }
}
