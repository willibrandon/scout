using System;
using System.Collections.Generic;

namespace Scout;

internal sealed class RegexLazyDfa
{
    private readonly RegexNfa nfa;
    private readonly Dictionary<RegexDfaStateKey, RegexLazyDfaState> states = [];
    private readonly RegexLazyDfaState startState;

    public RegexLazyDfa(RegexNfa nfa)
    {
        this.nfa = nfa;
        startState = Intern(RegexDfaOperations.Closure(nfa, nfa.StartState));
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

            current = Transition(current, haystack[position]);
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

    private RegexLazyDfaState Transition(RegexLazyDfaState state, byte value)
    {
        if (state.Transitions.TryGetValue(value, out RegexLazyDfaState? nextState))
        {
            return nextState;
        }

        nextState = Intern(RegexDfaOperations.Move(nfa, state.NfaStates, value));
        state.Transitions.Add(value, nextState);
        return nextState;
    }

    private RegexLazyDfaState Intern(int[] nfaStates)
    {
        var key = new RegexDfaStateKey(nfaStates);
        if (states.TryGetValue(key, out RegexLazyDfaState? existing))
        {
            return existing;
        }

        var state = new RegexLazyDfaState(nfaStates);
        states.Add(key, state);
        return state;
    }
}
