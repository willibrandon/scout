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
        AddThread(nfa.StartState, haystack, start, current, new bool[nfa.States.Count]);
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
                        state.DotMatchesNewline))
                {
                    AddThread(state.Next, haystack, position + 1, next, new bool[nfa.States.Count]);
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

    private void AddThread(int stateIndex, ReadOnlySpan<byte> haystack, int position, List<int> threads, bool[] visited)
    {
        if (stateIndex < 0 || visited[stateIndex])
        {
            return;
        }

        visited[stateIndex] = true;
        RegexNfaState state = nfa.States[stateIndex];
        switch (state.Kind)
        {
            case RegexNfaStateKind.Split:
                AddThread(state.Next, haystack, position, threads, visited);
                AddThread(state.Alternative, haystack, position, threads, visited);
                break;
            case RegexNfaStateKind.Predicate:
                if (RegexByteClass.PredicateMatches(haystack, position, state.AtomKind, state.MultiLine))
                {
                    AddThread(state.Next, haystack, position, threads, visited);
                }

                break;
            default:
                threads.Add(stateIndex);
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
                    state.DotMatchesNewline))
            {
                return true;
            }
        }

        return false;
    }
}
