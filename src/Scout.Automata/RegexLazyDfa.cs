namespace Scout;

/// <summary>
/// Executes a lazily materialized DFA with bounded transition storage and safe fallback.
/// </summary>
/// <param name="nfa">The NFA represented by the lazy DFA.</param>
/// <param name="states">The interned DFA states.</param>
/// <param name="startState">The DFA start state.</param>
/// <param name="budget">The remaining transition-storage budget.</param>
/// <param name="leftmostPrune">Whether closures retain only leftmost-priority states.</param>
internal sealed class RegexLazyDfa(
    RegexNfa nfa,
    Dictionary<RegexDfaStateKey, RegexLazyDfaState> states,
    RegexLazyDfaState startState,
    RegexDfaBudget budget,
    bool leftmostPrune)
{
    private const int MaxAcceleratorNeedles = 3;

    private readonly RegexNfa _nfa = nfa;
    private PikeVm? _fallback;
    private readonly Dictionary<RegexDfaStateKey, RegexLazyDfaState> _states = states;
    private RegexDfaBudget _budget = budget;
    private readonly RegexLazyDfaState _startState = startState;
    private readonly bool _leftmostPrune = leftmostPrune;

    /// <summary>
    /// Attempts to create a general lazy DFA within a byte budget.
    /// </summary>
    /// <param name="nfa">The NFA to execute.</param>
    /// <param name="dfaSizeLimit">The maximum estimated DFA storage in bytes.</param>
    /// <param name="dfa">Receives the lazy DFA when successful.</param>
    /// <returns><see langword="true"/> when the start state fits within the budget.</returns>
    public static bool TryCreate(RegexNfa nfa, ulong dfaSizeLimit, out RegexLazyDfa? dfa)
    {
        return TryCreate(nfa, dfaSizeLimit, leftmostPrune: false, out dfa);
    }

    /// <summary>
    /// Attempts to create a lazy DFA with optional leftmost-priority pruning.
    /// </summary>
    /// <param name="nfa">The NFA to execute.</param>
    /// <param name="dfaSizeLimit">The maximum estimated DFA storage in bytes.</param>
    /// <param name="leftmostPrune">Whether closures retain only leftmost-priority states.</param>
    /// <param name="dfa">Receives the lazy DFA when successful.</param>
    /// <returns><see langword="true"/> when the start state fits within the budget.</returns>
    public static bool TryCreate(RegexNfa nfa, ulong dfaSizeLimit, bool leftmostPrune, out RegexLazyDfa? dfa)
    {
        var budget = new RegexDfaBudget(dfaSizeLimit);
        var states = new Dictionary<RegexDfaStateKey, RegexLazyDfaState>();
        int[] startNfaStates = leftmostPrune
            ? RegexDfaOperations.ClosureLeftmost(nfa, nfa.StartState)
            : RegexDfaOperations.Closure(nfa, nfa.StartState);
        if (!TryIntern(nfa, states, ref budget, startNfaStates, out RegexLazyDfaState? startState))
        {
            dfa = null;
            return false;
        }

        dfa = new RegexLazyDfa(nfa, states, startState!, budget, leftmostPrune);
        return true;
    }

    /// <summary>
    /// Attempts to match at one byte offset.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="start">The byte offset at which matching begins.</param>
    /// <param name="length">Receives the accepted match length.</param>
    /// <returns><see langword="true"/> when a match is accepted.</returns>
    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        return TryMatchAt(haystack, start, reachabilityCache: null, out length);
    }

    /// <summary>
    /// Attempts to find a match end at or after one byte offset.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="start">The byte offset at which matching begins.</param>
    /// <param name="end">Receives the accepted end offset.</param>
    /// <returns><see langword="true"/> when a match is accepted.</returns>
    public bool TryFindEnd(ReadOnlySpan<byte> haystack, int start, out int end)
    {
        return TryFindEnd(haystack, start, reachabilityCache: null, out end);
    }

    /// <summary>
    /// Attempts to find a match end and reports transition-budget exhaustion.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="start">The byte offset at which matching begins.</param>
    /// <param name="end">Receives the accepted end offset.</param>
    /// <param name="gaveUp">Receives whether the DFA exhausted its transition budget.</param>
    /// <returns><see langword="true"/> when a match is accepted.</returns>
    public bool TryFindEnd(ReadOnlySpan<byte> haystack, int start, out int end, out bool gaveUp)
    {
        return TryFindEnd(haystack, start, reachabilityCache: null, out end, out gaveUp);
    }

    /// <summary>
    /// Attempts to find a match end with a reusable reachability cache.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="start">The byte offset at which matching begins.</param>
    /// <param name="reachabilityCache">The optional reusable NFA reachability cache.</param>
    /// <param name="end">Receives the accepted end offset.</param>
    /// <returns><see langword="true"/> when a match is accepted.</returns>
    public bool TryFindEnd(
        ReadOnlySpan<byte> haystack,
        int start,
        Dictionary<(int State, int Position), bool>? reachabilityCache,
        out int end)
    {
        return TryFindEnd(haystack, start, reachabilityCache, out end, out _);
    }

    /// <summary>
    /// Attempts to find a match end with reusable reachability and budget reporting.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="start">The byte offset at which matching begins.</param>
    /// <param name="reachabilityCache">The optional reusable NFA reachability cache.</param>
    /// <param name="end">Receives the accepted end offset.</param>
    /// <param name="gaveUp">Receives whether the DFA exhausted its transition budget.</param>
    /// <returns><see langword="true"/> when a match is accepted.</returns>
    public bool TryFindEnd(
        ReadOnlySpan<byte> haystack,
        int start,
        Dictionary<(int State, int Position), bool>? reachabilityCache,
        out int end,
        out bool gaveUp)
    {
        if (_leftmostPrune)
        {
            return TryFindEndLeftmost(haystack, start, out end, out gaveUp);
        }

        gaveUp = false;
        if (TryMatchAt(haystack, start, reachabilityCache, out int length))
        {
            end = start + length;
            return true;
        }

        end = 0;
        return false;
    }

    /// <summary>
    /// Attempts to find a match start by running a leftmost-pruned DFA in reverse.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="start">The earliest permitted start.</param>
    /// <param name="end">The exclusive reverse-search end.</param>
    /// <param name="matchStart">Receives the accepted start offset.</param>
    /// <returns><see langword="true"/> when a start is accepted.</returns>
    public bool TryFindStartReverse(ReadOnlySpan<byte> haystack, int start, int end, out int matchStart)
    {
        return TryFindStartReverse(haystack, start, end, reachabilityCache: null, out matchStart);
    }

    /// <summary>
    /// Attempts to find a reverse match start and reports transition-budget exhaustion.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="start">The earliest permitted start.</param>
    /// <param name="end">The exclusive reverse-search end.</param>
    /// <param name="matchStart">Receives the accepted start offset.</param>
    /// <param name="gaveUp">Receives whether the DFA exhausted its transition budget.</param>
    /// <returns><see langword="true"/> when a start is accepted.</returns>
    public bool TryFindStartReverse(ReadOnlySpan<byte> haystack, int start, int end, out int matchStart, out bool gaveUp)
    {
        return TryFindStartReverse(haystack, start, end, reachabilityCache: null, out matchStart, out gaveUp);
    }

    /// <summary>
    /// Attempts to find a reverse match start with a reusable reachability cache.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="start">The earliest permitted start.</param>
    /// <param name="end">The exclusive reverse-search end.</param>
    /// <param name="reachabilityCache">The optional reusable NFA reachability cache.</param>
    /// <param name="matchStart">Receives the accepted start offset.</param>
    /// <returns><see langword="true"/> when a start is accepted.</returns>
    public bool TryFindStartReverse(
        ReadOnlySpan<byte> haystack,
        int start,
        int end,
        Dictionary<(int State, int Position), bool>? reachabilityCache,
        out int matchStart)
    {
        return TryFindStartReverse(haystack, start, end, reachabilityCache, out matchStart, out _);
    }

    /// <summary>
    /// Attempts to find a reverse match start with reusable reachability and budget reporting.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="start">The earliest permitted start.</param>
    /// <param name="end">The exclusive reverse-search end.</param>
    /// <param name="reachabilityCache">The optional reusable NFA reachability cache.</param>
    /// <param name="matchStart">Receives the accepted start offset.</param>
    /// <param name="gaveUp">Receives whether the DFA exhausted its transition budget.</param>
    /// <returns><see langword="true"/> when a start is accepted.</returns>
    public bool TryFindStartReverse(
        ReadOnlySpan<byte> haystack,
        int start,
        int end,
        Dictionary<(int State, int Position), bool>? reachabilityCache,
        out int matchStart,
        out bool gaveUp)
    {
        _ = reachabilityCache;
        if (_leftmostPrune)
        {
            return TryFindStartReverseLeftmost(haystack, start, end, out matchStart, out gaveUp);
        }

        gaveUp = false;
        matchStart = 0;
        return false;
    }

    /// <summary>
    /// Finds the earliest and latest starts accepted while running the DFA in reverse.
    /// </summary>
    /// <param name="haystack">The bytes containing the reverse search window.</param>
    /// <param name="start">The earliest permitted start.</param>
    /// <param name="end">The exclusive end from which reverse matching begins.</param>
    /// <param name="earliest">Receives the earliest accepted start.</param>
    /// <param name="latest">Receives the latest accepted start.</param>
    /// <param name="gaveUp">Receives whether the DFA exhausted its transition budget.</param>
    /// <returns><see langword="true"/> when at least one start is accepted.</returns>
    internal bool TryFindStartBoundsReverse(
        ReadOnlySpan<byte> haystack,
        int start,
        int end,
        out int earliest,
        out int latest,
        out bool gaveUp)
    {
        RegexLazyDfaState current = _startState;
        earliest = -1;
        latest = -1;
        gaveUp = false;
        int position = end;
        while (position >= start)
        {
            if (current.AcceptIndex >= 0)
            {
                earliest = position;
                if (latest < 0)
                {
                    latest = position;
                }
            }

            if (position == start)
            {
                break;
            }

            if (!TryTransition(current, haystack[position - 1], out current))
            {
                gaveUp = true;
                return earliest >= 0;
            }

            position--;
            if (current.NfaStates.Length == 0)
            {
                break;
            }
        }

        return earliest >= 0;
    }

    private bool TryFindEndLeftmost(ReadOnlySpan<byte> haystack, int start, out int end, out bool gaveUp)
    {
        gaveUp = false;
        RegexLazyDfaState current = _startState;
        int lastAcceptEnd = -1;
        for (int position = start; position <= haystack.Length; position++)
        {
            if (current.AcceptIndex >= 0)
            {
                lastAcceptEnd = position;
            }

            if (position == haystack.Length)
            {
                break;
            }

            if (TryAccelerate(current, haystack, position, allowLeftmost: true, out int acceleratedPosition))
            {
                if (current.AcceptIndex >= 0)
                {
                    lastAcceptEnd = acceleratedPosition;
                }

                position = acceleratedPosition - 1;
                continue;
            }

            if (!TryTransition(current, haystack[position], out current))
            {
                gaveUp = true;
                end = lastAcceptEnd;
                return lastAcceptEnd >= 0;
            }

            if (current.NfaStates.Length == 0)
            {
                end = lastAcceptEnd;
                return lastAcceptEnd >= 0;
            }
        }

        end = lastAcceptEnd;
        return lastAcceptEnd >= 0;
    }

    private bool TryFindStartReverseLeftmost(
        ReadOnlySpan<byte> haystack,
        int start,
        int end,
        out int matchStart,
        out bool gaveUp)
    {
        gaveUp = false;
        RegexLazyDfaState current = _startState;
        int lastAcceptStart = -1;
        int position = end;
        while (position >= start)
        {
            if (current.AcceptIndex >= 0)
            {
                lastAcceptStart = position;
            }

            if (position == start)
            {
                break;
            }

            if (TryAccelerateReverse(current, haystack, start, position, out int acceleratedPosition))
            {
                if (current.AcceptIndex >= 0)
                {
                    lastAcceptStart = acceleratedPosition;
                }

                position = acceleratedPosition;
                continue;
            }

            if (!TryTransition(current, haystack[position - 1], out current))
            {
                gaveUp = true;
                matchStart = lastAcceptStart;
                return lastAcceptStart >= 0;
            }

            position--;
            if (current.NfaStates.Length == 0)
            {
                matchStart = lastAcceptStart;
                return lastAcceptStart >= 0;
            }
        }

        matchStart = lastAcceptStart;
        return lastAcceptStart >= 0;
    }

    /// <summary>
    /// Attempts to match at one byte offset with a reusable reachability cache.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="start">The byte offset at which matching begins.</param>
    /// <param name="reachabilityCache">The optional reusable NFA reachability cache.</param>
    /// <param name="length">Receives the accepted match length.</param>
    /// <returns><see langword="true"/> when a match is accepted.</returns>
    public bool TryMatchAt(
        ReadOnlySpan<byte> haystack,
        int start,
        Dictionary<(int State, int Position), bool>? reachabilityCache,
        out int length)
    {
        RegexLazyDfaState current = _startState;
        int deferredAcceptLength = -1;
        bool hasReusableReachabilityCache = reachabilityCache is not null;
        if (hasReusableReachabilityCache)
        {
            reachabilityCache!.Clear();
        }

        for (int position = start; position <= haystack.Length; position++)
        {
            int acceptIndex = current.AcceptIndex;
            if (acceptIndex >= 0)
            {
                deferredAcceptLength = position - start;
                if (acceptIndex == 0)
                {
                    length = deferredAcceptLength;
                    return true;
                }

                reachabilityCache ??= [];
                if (!RegexDfaOperations.HasEarlierConsumer(_nfa, current.NfaStates, acceptIndex, haystack, position, reachabilityCache))
                {
                    length = deferredAcceptLength;
                    return true;
                }

                if (TryAccelerate(current, haystack, position, out int acceleratedPosition))
                {
                    position = acceleratedPosition - 1;
                    continue;
                }
            }

            if (position == haystack.Length)
            {
                break;
            }

            if (!TryTransition(current, haystack[position], out current))
            {
                return GetOrCreateFallback().TryMatchAt(haystack, start, out length);
            }

            if (current.NfaStates.Length == 0)
            {
                if (deferredAcceptLength >= 0)
                {
                    length = deferredAcceptLength;
                    return true;
                }

                length = 0;
                return false;
            }
        }

        if (deferredAcceptLength >= 0)
        {
            length = deferredAcceptLength;
            return true;
        }

        length = 0;
        return false;
    }

    private PikeVm GetOrCreateFallback()
    {
        return _fallback ??= new PikeVm(_nfa);
    }

    /// <summary>
    /// Attempts to match an ASCII haystack and reports when non-ASCII input aborts the fast path.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="start">The byte offset at which matching begins.</param>
    /// <param name="length">Receives the accepted match length.</param>
    /// <param name="aborted">Receives whether non-ASCII input or budget exhaustion aborted the path.</param>
    /// <returns><see langword="true"/> when a match is accepted.</returns>
    public bool TryMatchAsciiAt(ReadOnlySpan<byte> haystack, int start, out int length, out bool aborted)
    {
        aborted = false;
        RegexLazyDfaState current = _startState;
        int deferredAcceptLength = -1;
        Dictionary<(int State, int Position), bool>? reachabilityCache = null;
        for (int position = start; position <= haystack.Length; position++)
        {
            int acceptIndex = current.AcceptIndex;
            if (acceptIndex >= 0)
            {
                deferredAcceptLength = position - start;
                if (acceptIndex == 0)
                {
                    length = deferredAcceptLength;
                    return true;
                }

                reachabilityCache ??= [];
                if (!RegexDfaOperations.HasEarlierConsumer(_nfa, current.NfaStates, acceptIndex, haystack, position, reachabilityCache))
                {
                    length = deferredAcceptLength;
                    return true;
                }
            }

            if (position == haystack.Length)
            {
                break;
            }

            if (haystack[position] > 0x7F)
            {
                aborted = true;
                length = 0;
                return false;
            }

            if (!TryTransition(current, haystack[position], out current))
            {
                aborted = true;
                length = 0;
                return false;
            }

            if (current.NfaStates.Length == 0)
            {
                if (deferredAcceptLength >= 0)
                {
                    length = deferredAcceptLength;
                    return true;
                }

                length = 0;
                return false;
            }
        }

        if (deferredAcceptLength >= 0)
        {
            length = deferredAcceptLength;
            return true;
        }

        length = 0;
        return false;
    }

    private bool TryTransition(RegexLazyDfaState state, byte value, out RegexLazyDfaState nextState)
    {
        if (state.TryGetTransition(value, out RegexLazyDfaState? existing))
        {
            nextState = existing!;
            return true;
        }

        if (!_budget.TryReserve(RegexDfaBudget.SparseTransitionBytes) ||
            !TryIntern(_nfa, _states, ref _budget, Move(state.NfaStates, value), out RegexLazyDfaState? created))
        {
            nextState = state;
            return false;
        }

        nextState = created!;
        state.AddTransition(value, nextState);
        return true;
    }

    private int[] Move(int[] nfaStates, byte value)
    {
        return _leftmostPrune
            ? RegexDfaOperations.MoveLeftmost(_nfa, nfaStates, value)
            : RegexDfaOperations.Move(_nfa, nfaStates, value);
    }

    private bool TryAccelerate(
        RegexLazyDfaState state,
        ReadOnlySpan<byte> haystack,
        int position,
        out int acceleratedPosition)
    {
        return TryAccelerate(state, haystack, position, allowLeftmost: false, out acceleratedPosition);
    }

    private bool TryAccelerate(
        RegexLazyDfaState state,
        ReadOnlySpan<byte> haystack,
        int position,
        bool allowLeftmost,
        out int acceleratedPosition)
    {
        acceleratedPosition = position;
        if (!allowLeftmost && _leftmostPrune ||
            position >= haystack.Length ||
            !TryGetOrCreateAccelerator(state, out byte[] needles))
        {
            return false;
        }

        int offset = IndexOfAcceleratorNeedle(haystack[position..], needles);
        acceleratedPosition = offset < 0 ? haystack.Length : position + offset;
        return acceleratedPosition > position;
    }

    private bool TryAccelerateReverse(
        RegexLazyDfaState state,
        ReadOnlySpan<byte> haystack,
        int start,
        int position,
        out int acceleratedPosition)
    {
        acceleratedPosition = position;
        if (position <= start ||
            !TryGetOrCreateAccelerator(state, out byte[] needles))
        {
            return false;
        }

        int offset = LastIndexOfAcceleratorNeedle(haystack[start..position], needles);
        acceleratedPosition = offset < 0 ? start : start + offset + 1;
        return acceleratedPosition < position;
    }

    private bool TryGetOrCreateAccelerator(RegexLazyDfaState state, out byte[] needles)
    {
        if (state.AcceleratorComputed)
        {
            return state.TryGetAccelerator(out needles);
        }

        var acceleratorNeedles = new List<byte>(MaxAcceleratorNeedles);
        bool sawSelfLoop = false;
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            int[] next = Move(state.NfaStates, (byte)value);
            if (next.AsSpan().SequenceEqual(state.NfaStates))
            {
                sawSelfLoop = true;
                continue;
            }

            if (acceleratorNeedles.Count == MaxAcceleratorNeedles)
            {
                needles = [];
                state.SetAccelerator(null);
                return false;
            }

            acceleratorNeedles.Add((byte)value);
        }

        if (!sawSelfLoop)
        {
            needles = [];
            state.SetAccelerator(null);
            return false;
        }

        needles = acceleratorNeedles.ToArray();
        state.SetAccelerator(needles);
        return true;
    }

    private static int IndexOfAcceleratorNeedle(ReadOnlySpan<byte> haystack, byte[] needles)
    {
        return needles.Length switch
        {
            0 => -1,
            1 => haystack.IndexOf(needles[0]),
            2 => haystack.IndexOfAny(needles[0], needles[1]),
            3 => haystack.IndexOfAny(needles[0], needles[1], needles[2]),
            _ => -1,
        };
    }

    private static int LastIndexOfAcceleratorNeedle(ReadOnlySpan<byte> haystack, byte[] needles)
    {
        return needles.Length switch
        {
            0 => -1,
            1 => haystack.LastIndexOf(needles[0]),
            2 => haystack.LastIndexOfAny(needles[0], needles[1]),
            3 => haystack.LastIndexOfAny(needles[0], needles[1], needles[2]),
            _ => -1,
        };
    }

    private static bool TryIntern(
        RegexNfa nfa,
        Dictionary<RegexDfaStateKey, RegexLazyDfaState> states,
        ref RegexDfaBudget budget,
        int[] nfaStates,
        out RegexLazyDfaState? state)
    {
        var key = new RegexDfaStateKey(nfaStates);
        if (states.TryGetValue(key, out RegexLazyDfaState? existing))
        {
            state = existing;
            return true;
        }

        if (!budget.TryReserve(RegexDfaBudget.EstimateStateBytes(nfaStates.Length, denseTransitions: false)))
        {
            state = null;
            return false;
        }

        state = new RegexLazyDfaState(nfaStates, RegexDfaOperations.IndexOfAccept(nfa, nfaStates));
        states.Add(key, state);
        return true;
    }
}
