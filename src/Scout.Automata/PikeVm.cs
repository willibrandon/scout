using System;
using System.Collections.Generic;

namespace Scout;

internal sealed class PikeVm
{
    private readonly RegexNfa nfa;
    private List<int> current = [];
    private List<int> next = [];

    public PikeVm(RegexNfa nfa)
    {
        this.nfa = nfa;
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        current.Clear();
        AddThread(nfa.StartState, haystack, start, current, new bool[nfa.States.Count], new bool[nfa.States.Count]);
        int deferredAcceptLength = -1;
        for (int position = start; position <= haystack.Length; position++)
        {
            int acceptIndex = current.IndexOf(0);
            if (acceptIndex >= 0)
            {
                deferredAcceptLength = position - start;
                if (!HasEarlierConsumer(current, acceptIndex, haystack, position))
                {
                    length = deferredAcceptLength;
                    return true;
                }
            }

            if (position == haystack.Length)
            {
                break;
            }

            next.Clear();
            for (int index = 0; index < current.Count; index++)
            {
                RegexNfaState state = nfa.States[current[index]];
                if (state.Kind == RegexNfaStateKind.Atom &&
                    RegexByteClass.AtomMatches(
                        haystack[position],
                        state.AtomKind,
                        state.Value.Span,
                        state.CaseInsensitive,
                        state.DotMatchesNewline,
                        state.Crlf))
                {
                    AddThread(state.Next, haystack, position + 1, next, new bool[nfa.States.Count], new bool[nfa.States.Count]);
                }
            }

            (current, next) = (next, current);
        }

        if (deferredAcceptLength >= 0)
        {
            length = deferredAcceptLength;
            return true;
        }

        length = 0;
        return false;
    }

    private void AddThread(
        int stateIndex,
        ReadOnlySpan<byte> haystack,
        int position,
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
            AddClosedSplitExit(stateIndex, haystack, position, threads, visited, closedSplits);
            return;
        }

        visited[stateIndex] = true;
        RegexNfaState state = nfa.States[stateIndex];
        switch (state.Kind)
        {
            case RegexNfaStateKind.Split:
            case RegexNfaStateKind.GreedyLoopSplit:
            case RegexNfaStateKind.LazyLoopSplit:
                AddThread(state.Next, haystack, position, threads, visited, closedSplits);
                AddThread(state.Alternative, haystack, position, threads, visited, closedSplits);
                break;
            case RegexNfaStateKind.Predicate:
                if (RegexByteClass.PredicateMatches(haystack, position, state.AtomKind, state.MultiLine, state.Crlf))
                {
                    AddThread(state.Next, haystack, position, threads, visited, closedSplits);
                }

                break;
            default:
                threads.Add(stateIndex);
                break;
        }
    }

    private void AddClosedSplitExit(
        int stateIndex,
        ReadOnlySpan<byte> haystack,
        int position,
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
                AddThread(state.Alternative, haystack, position, threads, visited, closedSplits);
                break;
            case RegexNfaStateKind.LazyLoopSplit:
                AddThread(state.Next, haystack, position, threads, visited, closedSplits);
                break;
        }
    }

    private bool HasEarlierConsumer(List<int> threads, int acceptIndex, ReadOnlySpan<byte> haystack, int position)
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
                    state.DotMatchesNewline,
                    state.Crlf))
            {
                return true;
            }
        }

        return false;
    }
}
