using System;
using System.Collections.Generic;

namespace Scout;

internal sealed class PikeVm
{
    private readonly RegexNfa nfa;
    private List<(int State, int Position)> current = [];
    private List<(int State, int Position)> next = [];

    public PikeVm(RegexNfa nfa)
    {
        this.nfa = nfa;
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        current.Clear();
        AddThread(nfa.StartState, haystack, start, current, new bool[nfa.States.Count], new bool[nfa.States.Count]);
        int deferredAcceptLength = -1;
        while (current.Count > 0)
        {
            int position = MinPosition(current);
            int acceptIndex = IndexOfAccept(current, position);
            if (acceptIndex >= 0)
            {
                deferredAcceptLength = position - start;
                if (!HasEarlierConsumer(current, acceptIndex, haystack, position))
                {
                    length = deferredAcceptLength;
                    return true;
                }
            }

            next.Clear();
            for (int index = 0; index < current.Count; index++)
            {
                (int stateIndex, int threadPosition) = current[index];
                if (threadPosition != position)
                {
                    next.Add(current[index]);
                    continue;
                }

                RegexNfaState state = nfa.States[stateIndex];
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
                        state.Utf8,
                        out int consume))
                {
                    AddThread(state.Next, haystack, position + consume, next, new bool[nfa.States.Count], new bool[nfa.States.Count]);
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
        List<(int State, int Position)> threads,
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
                if (RegexByteClass.PredicateMatches(haystack, position, state.AtomKind, state.MultiLine, state.Crlf, state.LineTerminator, state.Utf8))
                {
                    AddThread(state.Next, haystack, position, threads, visited, closedSplits);
                }

                break;
            default:
                threads.Add((stateIndex, position));
                break;
        }
    }

    private void AddClosedSplitExit(
        int stateIndex,
        ReadOnlySpan<byte> haystack,
        int position,
        List<(int State, int Position)> threads,
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

    private bool HasEarlierConsumer(List<(int State, int Position)> threads, int acceptIndex, ReadOnlySpan<byte> haystack, int position)
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

            RegexNfaState state = nfa.States[threads[index].State];
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
                    state.Utf8,
                    out _))
            {
                return true;
            }
        }

        return false;
    }

    private int IndexOfAccept(List<(int State, int Position)> threads, int position)
    {
        for (int index = 0; index < threads.Count; index++)
        {
            (int stateIndex, int threadPosition) = threads[index];
            if (threadPosition == position && nfa.States[stateIndex].Kind == RegexNfaStateKind.Accept)
            {
                return index;
            }
        }

        return -1;
    }

    private static int MinPosition(List<(int State, int Position)> threads)
    {
        int position = threads[0].Position;
        for (int index = 1; index < threads.Count; index++)
        {
            position = Math.Min(position, threads[index].Position);
        }

        return position;
    }
}
