using System;
using System.Collections.Generic;

namespace Scout;

internal sealed class RegexSparseDfa
{
    private const int AlphabetSize = 256;

    private readonly RegexNfa nfa;
    private readonly RegexSparseDfaState[] states;

    private RegexSparseDfa(RegexNfa nfa, RegexSparseDfaState[] states)
    {
        this.nfa = nfa;
        this.states = states;
    }

    public static bool TryCompile(RegexNfa nfa, int stateLimit, ulong dfaSizeLimit, out RegexSparseDfa? dfa)
    {
        if (TryBuild(nfa, stateLimit, dfaSizeLimit, out RegexSparseDfaState[]? states))
        {
            dfa = new RegexSparseDfa(nfa, states!);
            return true;
        }

        dfa = null;
        return false;
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        int current = 0;
        int deferredAcceptLength = -1;
        for (int position = start; position <= haystack.Length; position++)
        {
            RegexSparseDfaState state = states[current];
            if (state.AcceptIndex >= 0)
            {
                deferredAcceptLength = position - start;
                if (!RegexDfaOperations.HasEarlierConsumer(nfa, state.NfaStates, state.AcceptIndex, haystack, position))
                {
                    length = deferredAcceptLength;
                    return true;
                }
            }

            if (position == haystack.Length)
            {
                break;
            }

            if (!state.Transitions.TryGetValue(haystack[position], out current))
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

    private static bool TryBuild(RegexNfa nfa, int stateLimit, ulong dfaSizeLimit, out RegexSparseDfaState[]? states)
    {
        var stateSets = new List<int[]>();
        var transitionRows = new List<Dictionary<byte, int>?>();
        var indexes = new Dictionary<RegexDfaStateKey, int>();
        var budget = new RegexDfaBudget(dfaSizeLimit);
        if (Intern(RegexDfaOperations.Closure(nfa, nfa.StartState)) < 0)
        {
            states = null;
            return false;
        }

        for (int stateIndex = 0; stateIndex < stateSets.Count; stateIndex++)
        {
            var transitions = new Dictionary<byte, int>();
            for (int value = 0; value < AlphabetSize; value++)
            {
                int[] next = RegexDfaOperations.Move(nfa, stateSets[stateIndex], (byte)value);
                if (next.Length > 0)
                {
                    if (!budget.TryReserve(RegexDfaBudget.SparseTransitionBytes))
                    {
                        states = null;
                        return false;
                    }

                    int nextIndex = Intern(next);
                    if (nextIndex < 0)
                    {
                        states = null;
                        return false;
                    }

                    transitions.Add((byte)value, nextIndex);
                }
            }

            transitionRows[stateIndex] = transitions;
        }

        states = new RegexSparseDfaState[stateSets.Count];
        for (int stateIndex = 0; stateIndex < states.Length; stateIndex++)
        {
            states[stateIndex] = new RegexSparseDfaState(
                stateSets[stateIndex],
                RegexDfaOperations.IndexOfAccept(nfa, stateSets[stateIndex]),
                transitionRows[stateIndex]!);
        }

        return true;

        int Intern(int[] nfaStates)
        {
            var key = new RegexDfaStateKey(nfaStates);
            if (indexes.TryGetValue(key, out int existing))
            {
                return existing;
            }

            if (stateSets.Count >= stateLimit)
            {
                return -1;
            }

            if (!budget.TryReserve(RegexDfaBudget.EstimateStateBytes(nfaStates.Length, denseTransitions: false)))
            {
                return -1;
            }

            int index = stateSets.Count;
            indexes.Add(key, index);
            stateSets.Add(nfaStates);
            transitionRows.Add(null);
            return index;
        }
    }
}
