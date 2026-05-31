using System;
using System.Collections.Generic;

namespace Scout;

internal sealed class RegexOnePassDfa
{
    private readonly RegexNfa nfa;
    private readonly PikeVm fallback;
    private List<int> current = [];
    private List<int> next = [];

    public RegexOnePassDfa(RegexNfa nfa)
    {
        this.nfa = nfa;
        fallback = new PikeVm(nfa);
    }

    public static bool CanCompile(RegexNfa nfa)
    {
        bool sawPredicate = false;
        bool sawBranch = false;
        for (int index = 0; index < nfa.States.Count; index++)
        {
            RegexNfaState state = nfa.States[index];
            RegexNfaStateKind kind = state.Kind;
            if (state.Utf8 && RegexByteClass.RequiresUtf8ScalarMatch(state.AtomKind, state.Value.Span, state.CaseInsensitive, state.UnicodeClasses))
            {
                return false;
            }

            sawPredicate |= kind == RegexNfaStateKind.Predicate;
            sawBranch |= kind is RegexNfaStateKind.Split
                or RegexNfaStateKind.GreedyLoopSplit
                or RegexNfaStateKind.LazyLoopSplit;
        }

        return sawPredicate && sawBranch;
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        current.Clear();
        AddThread(nfa.StartState, haystack, start, current, new bool[nfa.States.Count], new bool[nfa.States.Count]);
        int deferredAcceptLength = -1;
        for (int position = start; position <= haystack.Length; position++)
        {
            int acceptIndex = IndexOfAccept(current);
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
            if (!TryStep(haystack, position, next))
            {
                return fallback.TryMatchAt(haystack, start, out length);
            }

            if (next.Count == 0 && deferredAcceptLength >= 0)
            {
                length = deferredAcceptLength;
                return true;
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

    private bool TryStep(ReadOnlySpan<byte> haystack, int position, List<int> destination)
    {
        bool matched = false;
        for (int index = 0; index < current.Count; index++)
        {
            RegexNfaState state = nfa.States[current[index]];
            if (state.Kind != RegexNfaStateKind.Atom ||
                !RegexByteClass.TryGetAtomMatchLength(
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
                    state.UnicodeClasses,
                    out int consume))
            {
                continue;
            }

            if (matched)
            {
                return false;
            }

            matched = true;
            AddThread(
                state.Next,
                haystack,
                position + consume,
                destination,
                new bool[nfa.States.Count],
                new bool[nfa.States.Count]);
        }

        return true;
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
                if (RegexByteClass.PredicateMatches(haystack, position, state.AtomKind, state.MultiLine, state.Crlf, state.LineTerminator, state.Utf8, state.UnicodeClasses))
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
                    state.UnicodeClasses,
                    out _))
            {
                return true;
            }
        }

        return false;
    }

    private int IndexOfAccept(List<int> threads)
    {
        for (int index = 0; index < threads.Count; index++)
        {
            if (nfa.States[threads[index]].Kind == RegexNfaStateKind.Accept)
            {
                return index;
            }
        }

        return -1;
    }
}
