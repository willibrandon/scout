namespace Scout;

internal sealed class RegexLazyDfa
{
    private readonly RegexNfa nfa;
    private readonly PikeVm fallback;
    private readonly Dictionary<RegexDfaStateKey, RegexLazyDfaState> states;
    private RegexDfaBudget budget;
    private readonly RegexLazyDfaState startState;

    private RegexLazyDfa(
        RegexNfa nfa,
        Dictionary<RegexDfaStateKey, RegexLazyDfaState> states,
        RegexLazyDfaState startState,
        RegexDfaBudget budget)
    {
        this.nfa = nfa;
        fallback = new PikeVm(nfa);
        this.states = states;
        this.startState = startState;
        this.budget = budget;
    }

    public static bool TryCreate(RegexNfa nfa, ulong dfaSizeLimit, out RegexLazyDfa? dfa)
    {
        var budget = new RegexDfaBudget(dfaSizeLimit);
        var states = new Dictionary<RegexDfaStateKey, RegexLazyDfaState>();
        int[] startNfaStates = RegexDfaOperations.Closure(nfa, nfa.StartState);
        if (!TryIntern(nfa, states, ref budget, startNfaStates, out RegexLazyDfaState? startState))
        {
            dfa = null;
            return false;
        }

        dfa = new RegexLazyDfa(nfa, states, startState!, budget);
        return true;
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        return TryMatchAt(haystack, start, reachabilityCache: null, out length);
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
        if (state.Transitions.TryGetValue(value, out RegexLazyDfaState? existing))
        {
            nextState = existing;
            return true;
        }

        if (!budget.TryReserve(RegexDfaBudget.SparseTransitionBytes) ||
            !TryIntern(nfa, states, ref budget, RegexDfaOperations.Move(nfa, state.NfaStates, value), out RegexLazyDfaState? created))
        {
            nextState = state;
            return false;
        }

        nextState = created!;
        state.Transitions.Add(value, nextState);
        return true;
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
