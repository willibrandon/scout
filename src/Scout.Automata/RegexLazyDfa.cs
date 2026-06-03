
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
        if (!TryIntern(states, ref budget, startNfaStates, out RegexLazyDfaState? startState))
        {
            dfa = null;
            return false;
        }

        dfa = new RegexLazyDfa(nfa, states, startState!, budget);
        return true;
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        RegexLazyDfaState current = startState;
        int deferredAcceptLength = -1;
        for (int position = start; position <= haystack.Length; position++)
        {
            int acceptIndex = RegexDfaOperations.IndexOfAccept(nfa, current.NfaStates);
            if (acceptIndex >= 0)
            {
                deferredAcceptLength = position - start;
                if (!RegexDfaOperations.HasEarlierConsumer(nfa, current.NfaStates, acceptIndex, haystack, position))
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

            if (current.NfaStates.Length == 0 && deferredAcceptLength >= 0)
            {
                length = deferredAcceptLength;
                return true;
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
            !TryIntern(states, ref budget, RegexDfaOperations.Move(nfa, state.NfaStates, value), out RegexLazyDfaState? created))
        {
            nextState = state;
            return false;
        }

        nextState = created!;
        state.Transitions.Add(value, nextState);
        return true;
    }

    private static bool TryIntern(
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

        state = new RegexLazyDfaState(nfaStates);
        states.Add(key, state);
        return true;
    }
}
