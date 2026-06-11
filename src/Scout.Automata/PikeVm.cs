namespace Scout;

internal sealed class PikeVm
{
    private readonly RegexNfa nfa;
    private List<(int State, int Position)> current = [];
    private List<(int State, int Position)> next = [];
    private HashSet<(int State, int Position)> currentSeen = [];
    private HashSet<(int State, int Position)> nextSeen = [];

    public PikeVm(RegexNfa nfa)
    {
        this.nfa = nfa;
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        return TryMatchAt(haystack, start, earliest: false, out length);
    }

    public bool TryMatchEarliestAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        return TryMatchAt(haystack, start, earliest: true, out length);
    }

    public bool TryMatchLongestAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        current.Clear();
        currentSeen.Clear();
        AddThread(nfa.StartState, haystack, start, current, currentSeen, new bool[nfa.States.Count], new bool[nfa.States.Count]);
        int longestAcceptLength = -1;
        while (current.Count > 0)
        {
            int position = MinPosition(current);
            if (IndexOfAccept(current, position) >= 0)
            {
                longestAcceptLength = Math.Max(longestAcceptLength, position - start);
            }

            next.Clear();
            nextSeen.Clear();
            for (int index = 0; index < current.Count; index++)
            {
                (int stateIndex, int threadPosition) = current[index];
                if (threadPosition != position)
                {
                    AddThreadEntry(stateIndex, threadPosition, next, nextSeen);
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
                        state.UnicodeClasses,
                        out int consume))
                {
                    AddThread(state.Next, haystack, position + consume, next, nextSeen, new bool[nfa.States.Count], new bool[nfa.States.Count]);
                }
            }

            (current, next) = (next, current);
            (currentSeen, nextSeen) = (nextSeen, currentSeen);
        }

        if (longestAcceptLength >= 0)
        {
            length = longestAcceptLength;
            return true;
        }

        length = 0;
        return false;
    }

    public void AddMatchLengthsAt(ReadOnlySpan<byte> haystack, int start, List<int> lengths)
    {
        current.Clear();
        currentSeen.Clear();
        AddThread(nfa.StartState, haystack, start, current, currentSeen, new bool[nfa.States.Count], new bool[nfa.States.Count]);
        while (current.Count > 0)
        {
            int position = MinPosition(current);
            if (IndexOfAccept(current, position) >= 0)
            {
                int length = position - start;
                if (!lengths.Contains(length))
                {
                    lengths.Add(length);
                }
            }

            next.Clear();
            nextSeen.Clear();
            for (int index = 0; index < current.Count; index++)
            {
                (int stateIndex, int threadPosition) = current[index];
                if (threadPosition != position)
                {
                    AddThreadEntry(stateIndex, threadPosition, next, nextSeen);
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
                        state.UnicodeClasses,
                        out int consume))
                {
                    AddThread(state.Next, haystack, position + consume, next, nextSeen, new bool[nfa.States.Count], new bool[nfa.States.Count]);
                }
            }

            (current, next) = (next, current);
            (currentSeen, nextSeen) = (nextSeen, currentSeen);
        }
    }

    private bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, bool earliest, out int length)
    {
        current.Clear();
        currentSeen.Clear();
        AddThread(nfa.StartState, haystack, start, current, currentSeen, new bool[nfa.States.Count], new bool[nfa.States.Count]);
        int deferredAcceptLength = -1;
        Dictionary<(int State, int Position), bool> reachabilityCache = [];
        while (current.Count > 0)
        {
            int position = MinPosition(current);
            int acceptIndex = IndexOfAccept(current, position);
            if (acceptIndex >= 0)
            {
                deferredAcceptLength = position - start;
                if (earliest || !HasEarlierConsumer(current, acceptIndex, haystack, position, reachabilityCache))
                {
                    length = deferredAcceptLength;
                    return true;
                }
            }

            next.Clear();
            nextSeen.Clear();
            for (int index = 0; index < current.Count; index++)
            {
                (int stateIndex, int threadPosition) = current[index];
                if (threadPosition != position)
                {
                    AddThreadEntry(stateIndex, threadPosition, next, nextSeen);
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
                        state.UnicodeClasses,
                        out int consume))
                {
                    AddThread(state.Next, haystack, position + consume, next, nextSeen, new bool[nfa.States.Count], new bool[nfa.States.Count]);
                }
            }

            (current, next) = (next, current);
            (currentSeen, nextSeen) = (nextSeen, currentSeen);
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
        HashSet<(int State, int Position)> seen,
        bool[] visited,
        bool[] closedSplits)
    {
        if (stateIndex < 0)
        {
            return;
        }

        if (visited[stateIndex])
        {
            AddClosedSplitExit(stateIndex, haystack, position, threads, seen, visited, closedSplits);
            return;
        }

        visited[stateIndex] = true;
        RegexNfaState state = nfa.States[stateIndex];
        switch (state.Kind)
        {
            case RegexNfaStateKind.Split:
            case RegexNfaStateKind.GreedyLoopSplit:
            case RegexNfaStateKind.LazyLoopSplit:
                AddThread(state.Next, haystack, position, threads, seen, visited, closedSplits);
                AddThread(state.Alternative, haystack, position, threads, seen, visited, closedSplits);
                break;
            case RegexNfaStateKind.Predicate:
                if (RegexByteClass.PredicateMatches(haystack, position, state.AtomKind, state.MultiLine, state.Crlf, state.LineTerminator, state.Utf8, state.UnicodeClasses))
                {
                    AddThread(state.Next, haystack, position, threads, seen, visited, closedSplits);
                }

                break;
            default:
                AddThreadEntry(stateIndex, position, threads, seen);
                break;
        }
    }

    private static void AddThreadEntry(
        int stateIndex,
        int position,
        List<(int State, int Position)> threads,
        HashSet<(int State, int Position)> seen)
    {
        if (seen.Add((stateIndex, position)))
        {
            threads.Add((stateIndex, position));
        }
    }

    private void AddClosedSplitExit(
        int stateIndex,
        ReadOnlySpan<byte> haystack,
        int position,
        List<(int State, int Position)> threads,
        HashSet<(int State, int Position)> seen,
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
                AddThread(state.Alternative, haystack, position, threads, seen, visited, closedSplits);
                break;
            case RegexNfaStateKind.LazyLoopSplit:
                AddThread(state.Next, haystack, position, threads, seen, visited, closedSplits);
                break;
        }
    }

    private bool HasEarlierConsumer(
        List<(int State, int Position)> threads,
        int acceptIndex,
        ReadOnlySpan<byte> haystack,
        int position,
        Dictionary<(int State, int Position), bool> reachabilityCache)
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
                    state.UnicodeClasses,
                    out int consume) &&
                RegexDfaOperations.CanReachAccept(nfa, state.Next, haystack, position + consume, reachabilityCache))
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
