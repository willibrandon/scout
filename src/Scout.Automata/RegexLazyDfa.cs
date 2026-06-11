namespace Scout;

internal sealed class RegexLazyDfa
{
    private readonly RegexNfa nfa;
    private readonly PikeVm fallback;
    private readonly Dictionary<RegexDfaStateKey, RegexLazyDfaState> states;
    private RegexDfaBudget budget;
    private readonly RegexLazyDfaState startState;
    private readonly bool leftmostPrune;

    private RegexLazyDfa(
        RegexNfa nfa,
        Dictionary<RegexDfaStateKey, RegexLazyDfaState> states,
        RegexLazyDfaState startState,
        RegexDfaBudget budget,
        bool leftmostPrune)
    {
        this.nfa = nfa;
        fallback = new PikeVm(nfa);
        this.states = states;
        this.startState = startState;
        this.budget = budget;
        this.leftmostPrune = leftmostPrune;
    }

    public static bool TryCreate(RegexNfa nfa, ulong dfaSizeLimit, out RegexLazyDfa? dfa)
    {
        return TryCreate(nfa, dfaSizeLimit, leftmostPrune: false, out dfa);
    }

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

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        return TryMatchAt(haystack, start, reachabilityCache: null, out length);
    }

    public bool TryFindEnd(ReadOnlySpan<byte> haystack, int start, out int end)
    {
        return TryFindEnd(haystack, start, reachabilityCache: null, out end);
    }

    public bool TryFindEnd(
        ReadOnlySpan<byte> haystack,
        int start,
        Dictionary<(int State, int Position), bool>? reachabilityCache,
        out int end)
    {
        if (leftmostPrune)
        {
            return TryFindEndLeftmost(haystack, start, out end);
        }

        if (TryMatchAt(haystack, start, reachabilityCache, out int length))
        {
            end = start + length;
            return true;
        }

        end = 0;
        return false;
    }

    public bool TryFindStartReverse(ReadOnlySpan<byte> haystack, int start, int end, out int matchStart)
    {
        return TryFindStartReverse(haystack, start, end, reachabilityCache: null, out matchStart);
    }

    public bool TryFindStartReverse(
        ReadOnlySpan<byte> haystack,
        int start,
        int end,
        Dictionary<(int State, int Position), bool>? reachabilityCache,
        out int matchStart)
    {
        _ = reachabilityCache;
        if (leftmostPrune)
        {
            return TryFindStartReverseLeftmost(haystack, start, end, out matchStart);
        }

        matchStart = 0;
        return false;
    }

    private bool TryFindEndLeftmost(ReadOnlySpan<byte> haystack, int start, out int end)
    {
        RegexLazyDfaState current = startState;
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

            if (!TryTransition(current, haystack[position], out current))
            {
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

    private bool TryFindStartReverseLeftmost(ReadOnlySpan<byte> haystack, int start, int end, out int matchStart)
    {
        RegexLazyDfaState current = startState;
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

            if (!TryTransition(current, haystack[position - 1], out current))
            {
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

    public bool TryMatchAt(
        ReadOnlySpan<byte> haystack,
        int start,
        Dictionary<(int State, int Position), bool>? reachabilityCache,
        out int length)
    {
        RegexLazyDfaState current = startState;
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
                if (!RegexDfaOperations.HasEarlierConsumer(nfa, current.NfaStates, acceptIndex, haystack, position, reachabilityCache))
                {
                    length = deferredAcceptLength;
                    return true;
                }
            }

            if (position == haystack.Length)
            {
                break;
            }

            if (!TryTransition(current, haystack[position], out current))
            {
                return fallback.TryMatchAt(haystack, start, out length);
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

    public bool TryMatchAsciiAt(ReadOnlySpan<byte> haystack, int start, out int length, out bool aborted)
    {
        aborted = false;
        RegexLazyDfaState current = startState;
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
                if (!RegexDfaOperations.HasEarlierConsumer(nfa, current.NfaStates, acceptIndex, haystack, position, reachabilityCache))
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

        if (!budget.TryReserve(RegexDfaBudget.SparseTransitionBytes) ||
            !TryIntern(nfa, states, ref budget, Move(state.NfaStates, value), out RegexLazyDfaState? created))
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
        return leftmostPrune
            ? RegexDfaOperations.MoveLeftmost(nfa, nfaStates, value)
            : RegexDfaOperations.Move(nfa, nfaStates, value);
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
