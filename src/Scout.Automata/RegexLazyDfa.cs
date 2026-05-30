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
        startState = Intern(Closure(nfa.StartState));
    }

    public static bool CanCompile(RegexNfa nfa)
    {
        for (int index = 0; index < nfa.States.Count; index++)
        {
            if (nfa.States[index].Kind == RegexNfaStateKind.Predicate)
            {
                return false;
            }
        }

        return true;
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        RegexLazyDfaState current = startState;
        int deferredAcceptLength = -1;
        for (int position = start; position <= haystack.Length; position++)
        {
            int acceptIndex = IndexOfAccept(current.NfaStates);
            if (acceptIndex >= 0)
            {
                deferredAcceptLength = position - start;
                if (!HasEarlierConsumer(current.NfaStates, acceptIndex, haystack, position))
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

        var next = new List<int>();
        for (int index = 0; index < state.NfaStates.Length; index++)
        {
            RegexNfaState nfaState = nfa.States[state.NfaStates[index]];
            if (nfaState.Kind == RegexNfaStateKind.Atom &&
                RegexByteClass.AtomMatches(
                    value,
                    nfaState.AtomKind,
                    nfaState.Value.Span,
                    nfaState.CaseInsensitive,
                    nfaState.MultiLine,
                    nfaState.DotMatchesNewline,
                    nfaState.Crlf,
                    nfaState.LineTerminator))
            {
                AddThread(nfaState.Next, next, new bool[nfa.States.Count], new bool[nfa.States.Count]);
            }
        }

        nextState = Intern(next.ToArray());
        state.Transitions.Add(value, nextState);
        return nextState;
    }

    private int[] Closure(int stateIndex)
    {
        var threads = new List<int>();
        AddThread(stateIndex, threads, new bool[nfa.States.Count], new bool[nfa.States.Count]);
        return threads.ToArray();
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

    private void AddThread(
        int stateIndex,
        List<int> threads,
        bool[] visited,
        bool[] closedSplits)
    {
        if (stateIndex < 0)
        {
            return;
        }

        if (visited[stateIndex])
        {
            AddClosedSplitExit(stateIndex, threads, visited, closedSplits);
            return;
        }

        visited[stateIndex] = true;
        RegexNfaState state = nfa.States[stateIndex];
        switch (state.Kind)
        {
            case RegexNfaStateKind.Split:
            case RegexNfaStateKind.GreedyLoopSplit:
            case RegexNfaStateKind.LazyLoopSplit:
                AddThread(state.Next, threads, visited, closedSplits);
                AddThread(state.Alternative, threads, visited, closedSplits);
                break;
            default:
                threads.Add(stateIndex);
                break;
        }
    }

    private void AddClosedSplitExit(
        int stateIndex,
        List<int> threads,
        bool[] visited,
        bool[] closedSplits)
    {
        RegexNfaState state = nfa.States[stateIndex];
        if (closedSplits[stateIndex])
        {
            return;
        }

        closedSplits[stateIndex] = true;
        switch (state.Kind)
        {
            case RegexNfaStateKind.GreedyLoopSplit:
                AddThread(state.Alternative, threads, visited, closedSplits);
                break;
            case RegexNfaStateKind.LazyLoopSplit:
                AddThread(state.Next, threads, visited, closedSplits);
                break;
        }
    }

    private bool HasEarlierConsumer(int[] threads, int acceptIndex, ReadOnlySpan<byte> haystack, int position)
    {
        if (position >= haystack.Length)
        {
            return false;
        }

        for (int index = 0; index < acceptIndex; index++)
        {
            RegexNfaState state = nfa.States[threads[index]];
            if (state.Kind == RegexNfaStateKind.Atom &&
                RegexByteClass.AtomMatches(
                    haystack[position],
                    state.AtomKind,
                    state.Value.Span,
                    state.CaseInsensitive,
                    state.MultiLine,
                    state.DotMatchesNewline,
                    state.Crlf,
                    state.LineTerminator))
            {
                return true;
            }
        }

        return false;
    }

    private int IndexOfAccept(int[] threads)
    {
        for (int index = 0; index < threads.Length; index++)
        {
            if (nfa.States[threads[index]].Kind == RegexNfaStateKind.Accept)
            {
                return index;
            }
        }

        return -1;
    }
}
