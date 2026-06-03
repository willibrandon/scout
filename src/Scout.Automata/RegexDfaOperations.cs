
namespace Scout;

internal static class RegexDfaOperations
{
    public static bool CanCompile(RegexNfa nfa)
    {
        for (int index = 0; index < nfa.States.Count; index++)
        {
            RegexNfaState state = nfa.States[index];
            if (state.Kind == RegexNfaStateKind.Predicate ||
                RegexByteClass.RequiresUtf8ScalarMatch(state.AtomKind, state.Value.Span, state.Utf8, state.CaseInsensitive, state.UnicodeClasses))
            {
                return false;
            }
        }

        return true;
    }

    public static int[] Closure(RegexNfa nfa, int stateIndex)
    {
        var threads = new List<int>();
        AddThread(nfa, stateIndex, threads, new bool[nfa.States.Count], new bool[nfa.States.Count]);
        return threads.ToArray();
    }

    public static int[] Move(RegexNfa nfa, int[] nfaStates, byte value)
    {
        var next = new List<int>();
        for (int index = 0; index < nfaStates.Length; index++)
        {
            RegexNfaState nfaState = nfa.States[nfaStates[index]];
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
                AddThread(nfa, nfaState.Next, next, new bool[nfa.States.Count], new bool[nfa.States.Count]);
            }
        }

        return next.ToArray();
    }

    public static bool HasEarlierConsumer(RegexNfa nfa, int[] threads, int acceptIndex, ReadOnlySpan<byte> haystack, int position)
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

    public static int IndexOfAccept(RegexNfa nfa, int[] threads)
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

    private static void AddThread(
        RegexNfa nfa,
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
            AddClosedSplitExit(nfa, stateIndex, threads, visited, closedSplits);
            return;
        }

        visited[stateIndex] = true;
        RegexNfaState state = nfa.States[stateIndex];
        switch (state.Kind)
        {
            case RegexNfaStateKind.Split:
            case RegexNfaStateKind.GreedyLoopSplit:
            case RegexNfaStateKind.LazyLoopSplit:
                AddThread(nfa, state.Next, threads, visited, closedSplits);
                AddThread(nfa, state.Alternative, threads, visited, closedSplits);
                break;
            default:
                threads.Add(stateIndex);
                break;
        }
    }

    private static void AddClosedSplitExit(
        RegexNfa nfa,
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
                AddThread(nfa, state.Alternative, threads, visited, closedSplits);
                break;
            case RegexNfaStateKind.LazyLoopSplit:
                AddThread(nfa, state.Next, threads, visited, closedSplits);
                break;
        }
    }
}
