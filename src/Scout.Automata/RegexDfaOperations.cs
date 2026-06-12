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

    public static int[] ClosureLeftmost(RegexNfa nfa, int stateIndex)
    {
        var threads = new List<int>();
        AddThreadLeftmost(nfa, stateIndex, threads, new bool[nfa.States.Count], new bool[nfa.States.Count]);
        return threads.ToArray();
    }

    public static int[] Move(RegexNfa nfa, int[] nfaStates, byte value)
    {
        var next = new List<int>();
        bool[] visited = new bool[nfa.States.Count];
        bool[] closedSplits = new bool[nfa.States.Count];
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
                AddThread(nfa, nfaState.Next, next, visited, closedSplits);
            }
            else if (nfaState.Kind == RegexNfaStateKind.Sparse &&
                nfaState.TryGetSparseTarget(value, out int sparseNext))
            {
                AddThread(nfa, sparseNext, next, visited, closedSplits);
            }
        }

        return next.ToArray();
    }

    public static int[] MoveLeftmost(RegexNfa nfa, int[] nfaStates, byte value)
    {
        var next = new List<int>();
        bool[] visited = new bool[nfa.States.Count];
        bool[] closedSplits = new bool[nfa.States.Count];
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
                    nfaState.LineTerminator) &&
                AddThreadLeftmost(nfa, nfaState.Next, next, visited, closedSplits))
            {
                break;
            }

            if (nfaState.Kind == RegexNfaStateKind.Sparse &&
                nfaState.TryGetSparseTarget(value, out int sparseNext) &&
                AddThreadLeftmost(nfa, sparseNext, next, visited, closedSplits))
            {
                break;
            }
        }

        return next.ToArray();
    }

    public static bool HasEarlierConsumer(
        RegexNfa nfa,
        int[] threads,
        int acceptIndex,
        ReadOnlySpan<byte> haystack,
        int position,
        Dictionary<(int State, int Position), bool>? reachabilityCache = null)
    {
        if (position >= haystack.Length)
        {
            return false;
        }

        for (int index = 0; index < acceptIndex; index++)
        {
            RegexNfaState state = nfa.States[threads[index]];
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

            if (consume == 1 && CanReachAcceptWithoutConsuming(nfa, state.Next, new bool[nfa.States.Count]))
            {
                return true;
            }

            if (CanReachAccept(nfa, state.Next, haystack, position + consume, reachabilityCache))
            {
                return true;
            }
        }

        for (int index = 0; index < acceptIndex; index++)
        {
            RegexNfaState state = nfa.States[threads[index]];
            if (state.Kind == RegexNfaStateKind.Sparse &&
                state.TryGetSparseTarget(haystack[position], out int sparseNext) &&
                CanReachAccept(nfa, sparseNext, haystack, position + 1, reachabilityCache))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanReachAcceptWithoutConsuming(RegexNfa nfa, int stateIndex, bool[] visited)
    {
        if (stateIndex < 0 || visited[stateIndex])
        {
            return false;
        }

        visited[stateIndex] = true;
        RegexNfaState state = nfa.States[stateIndex];
        return state.Kind switch
        {
            RegexNfaStateKind.Accept => true,
            RegexNfaStateKind.Split
                or RegexNfaStateKind.GreedyLoopSplit
                or RegexNfaStateKind.LazyLoopSplit =>
                CanReachAcceptWithoutConsuming(nfa, state.Next, visited) ||
                CanReachAcceptWithoutConsuming(nfa, state.Alternative, visited),
            RegexNfaStateKind.CaptureStart or RegexNfaStateKind.CaptureEnd =>
                CanReachAcceptWithoutConsuming(nfa, state.Next, visited),
            _ => false,
        };
    }

    public static bool CanReachAccept(
        RegexNfa nfa,
        int stateIndex,
        ReadOnlySpan<byte> haystack,
        int position,
        Dictionary<(int State, int Position), bool>? cache = null)
    {
        cache ??= [];
        return CanReachAcceptCore(nfa, stateIndex, haystack, position, cache, []);
    }

    private static bool CanReachAcceptCore(
        RegexNfa nfa,
        int stateIndex,
        ReadOnlySpan<byte> haystack,
        int position,
        Dictionary<(int State, int Position), bool> cache,
        HashSet<(int State, int Position)> visiting)
    {
        if (stateIndex < 0)
        {
            return false;
        }

        (int State, int Position) key = (stateIndex, position);
        if (cache.TryGetValue(key, out bool cached))
        {
            return cached;
        }

        if (!visiting.Add(key))
        {
            return false;
        }

        RegexNfaState state = nfa.States[stateIndex];
        bool result = state.Kind switch
        {
            RegexNfaStateKind.Accept => true,
            RegexNfaStateKind.Split
                or RegexNfaStateKind.GreedyLoopSplit
                or RegexNfaStateKind.LazyLoopSplit =>
                CanReachAcceptCore(nfa, state.Next, haystack, position, cache, visiting) ||
                CanReachAcceptCore(nfa, state.Alternative, haystack, position, cache, visiting),
            RegexNfaStateKind.Predicate =>
                RegexByteClass.PredicateMatches(
                    haystack,
                    position,
                    state.AtomKind,
                    state.MultiLine,
                    state.Crlf,
                    state.LineTerminator,
                    state.Utf8,
                    state.UnicodeClasses) &&
                CanReachAcceptCore(nfa, state.Next, haystack, position, cache, visiting),
            RegexNfaStateKind.CaptureStart or RegexNfaStateKind.CaptureEnd =>
                CanReachAcceptCore(nfa, state.Next, haystack, position, cache, visiting),
            RegexNfaStateKind.Atom =>
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
                CanReachAcceptCore(nfa, state.Next, haystack, position + consume, cache, visiting),
            RegexNfaStateKind.Sparse =>
                position < haystack.Length &&
                state.TryGetSparseTarget(haystack[position], out int sparseNext) &&
                CanReachAcceptCore(nfa, sparseNext, haystack, position + 1, cache, visiting),
            _ => false,
        };

        visiting.Remove(key);
        cache[key] = result;
        return result;
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

    private static bool AddThreadLeftmost(
        RegexNfa nfa,
        int stateIndex,
        List<int> threads,
        bool[] visited,
        bool[] closedSplits)
    {
        if (stateIndex < 0)
        {
            return false;
        }

        if (visited[stateIndex])
        {
            return AddClosedSplitExitLeftmost(nfa, stateIndex, threads, visited, closedSplits);
        }

        visited[stateIndex] = true;
        RegexNfaState state = nfa.States[stateIndex];
        switch (state.Kind)
        {
            case RegexNfaStateKind.Accept:
                threads.Add(stateIndex);
                return true;
            case RegexNfaStateKind.Split:
            case RegexNfaStateKind.GreedyLoopSplit:
            case RegexNfaStateKind.LazyLoopSplit:
                return AddThreadLeftmost(nfa, state.Next, threads, visited, closedSplits) ||
                    AddThreadLeftmost(nfa, state.Alternative, threads, visited, closedSplits);
            default:
                threads.Add(stateIndex);
                return false;
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

    private static bool AddClosedSplitExitLeftmost(
        RegexNfa nfa,
        int stateIndex,
        List<int> threads,
        bool[] visited,
        bool[] closedSplits)
    {
        RegexNfaState state = nfa.States[stateIndex];
        if (closedSplits[stateIndex])
        {
            return false;
        }

        closedSplits[stateIndex] = true;
        return state.Kind switch
        {
            RegexNfaStateKind.GreedyLoopSplit => AddThreadLeftmost(nfa, state.Alternative, threads, visited, closedSplits),
            RegexNfaStateKind.LazyLoopSplit => AddThreadLeftmost(nfa, state.Next, threads, visited, closedSplits),
            _ => false,
        };
    }
}
