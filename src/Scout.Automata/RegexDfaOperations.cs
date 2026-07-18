namespace Scout;

/// <summary>
/// Provides shared closure, transition, and reachability operations for DFA-style runners.
/// </summary>
internal static class RegexDfaOperations
{
    /// <summary>
    /// Determines whether an NFA can use byte-oriented DFA operations.
    /// </summary>
    /// <param name="nfa">The NFA to inspect.</param>
    /// <returns><see langword="true" /> when every state is supported.</returns>
    public static bool CanCompile(RegexNfa nfa)
    {
        for (int index = 0; index < nfa.States.Count; index++)
        {
            RegexNfaState state = nfa.States[index];
            if (state.Kind == RegexNfaStateKind.Predicate ||
                state.RequiresUtf8ScalarMatch)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Computes the ordered epsilon closure for a state.
    /// </summary>
    /// <param name="nfa">The source NFA.</param>
    /// <param name="stateIndex">The starting state index.</param>
    /// <returns>The ordered closure state indexes.</returns>
    public static int[] Closure(RegexNfa nfa, int stateIndex)
    {
        var threads = new List<int>();
        AddThread(nfa, stateIndex, threads, new bool[nfa.States.Count], new bool[nfa.States.Count]);
        return threads.ToArray();
    }

    /// <summary>
    /// Computes an epsilon closure that stops after the first accepting path.
    /// </summary>
    /// <param name="nfa">The source NFA.</param>
    /// <param name="stateIndex">The starting state index.</param>
    /// <returns>The leftmost-first closure state indexes.</returns>
    public static int[] ClosureLeftmost(RegexNfa nfa, int stateIndex)
    {
        var threads = new List<int>();
        AddThreadLeftmost(nfa, stateIndex, threads, new bool[nfa.States.Count], new bool[nfa.States.Count]);
        return threads.ToArray();
    }

    /// <summary>
    /// Moves an ordered NFA state set through one byte and closes the resulting states.
    /// </summary>
    /// <param name="nfa">The source NFA.</param>
    /// <param name="nfaStates">The current ordered state indexes.</param>
    /// <param name="value">The byte to consume.</param>
    /// <returns>The next ordered state indexes.</returns>
    public static int[] Move(RegexNfa nfa, int[] nfaStates, byte value)
    {
        var next = new List<int>();
        bool[] visited = new bool[nfa.States.Count];
        bool[] closedSplits = new bool[nfa.States.Count];
        for (int index = 0; index < nfaStates.Length; index++)
        {
            RegexNfaState nfaState = nfa.States[nfaStates[index]];
            if (nfaState.Kind == RegexNfaStateKind.Atom &&
                nfaState.AtomMatches(value))
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

    /// <summary>
    /// Moves a leftmost-first NFA state set through one byte.
    /// </summary>
    /// <param name="nfa">The source NFA.</param>
    /// <param name="nfaStates">The current ordered state indexes.</param>
    /// <param name="value">The byte to consume.</param>
    /// <returns>The next leftmost-first state indexes.</returns>
    public static int[] MoveLeftmost(RegexNfa nfa, int[] nfaStates, byte value)
    {
        var next = new List<int>();
        bool[] visited = new bool[nfa.States.Count];
        bool[] closedSplits = new bool[nfa.States.Count];
        for (int index = 0; index < nfaStates.Length; index++)
        {
            RegexNfaState nfaState = nfa.States[nfaStates[index]];
            if (nfaState.Kind == RegexNfaStateKind.Atom &&
                nfaState.AtomMatches(value) &&
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

    /// <summary>
    /// Determines whether a consumer ordered before an accepting state can still match.
    /// </summary>
    /// <param name="nfa">The source NFA.</param>
    /// <param name="threads">The current ordered state indexes.</param>
    /// <param name="acceptIndex">The index of the accepting state in <paramref name="threads" />.</param>
    /// <param name="haystack">The complete haystack bytes.</param>
    /// <param name="position">The current byte position.</param>
    /// <param name="reachabilityCache">An optional reusable reachability cache.</param>
    /// <returns><see langword="true" /> when an earlier consumer can reach acceptance.</returns>
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
                !state.TryGetAtomMatchLength(haystack, position, out int consume))
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

    /// <summary>
    /// Determines whether a state can reach acceptance from a haystack position.
    /// </summary>
    /// <param name="nfa">The source NFA.</param>
    /// <param name="stateIndex">The starting state index.</param>
    /// <param name="haystack">The complete haystack bytes.</param>
    /// <param name="position">The starting byte position.</param>
    /// <param name="cache">An optional reusable result cache.</param>
    /// <param name="visited">An optional reusable visited set.</param>
    /// <param name="pending">An optional reusable traversal stack.</param>
    /// <returns><see langword="true" /> when an accepting state is reachable.</returns>
    public static bool CanReachAccept(
        RegexNfa nfa,
        int stateIndex,
        ReadOnlySpan<byte> haystack,
        int position,
        Dictionary<(int State, int Position), bool>? cache = null,
        HashSet<(int State, int Position)>? visited = null,
        Stack<(int State, int Position)>? pending = null)
    {
        cache ??= [];
        if (stateIndex < 0)
        {
            return false;
        }

        (int State, int Position) start = (stateIndex, position);
        if (cache.TryGetValue(start, out bool cached))
        {
            return cached;
        }

        visited ??= [];
        pending ??= [];
        visited.Clear();
        pending.Clear();
        pending.Push(start);

        while (pending.Count > 0)
        {
            (int State, int Position) current = pending.Pop();
            if (current.State < 0 ||
                !visited.Add(current))
            {
                continue;
            }

            if (cache.TryGetValue(current, out cached))
            {
                if (cached)
                {
                    cache[start] = true;
                    return true;
                }

                continue;
            }

            RegexNfaState state = nfa.States[current.State];
            switch (state.Kind)
            {
                case RegexNfaStateKind.Accept:
                    cache[start] = true;
                    cache[current] = true;
                    return true;

                case RegexNfaStateKind.Split:
                case RegexNfaStateKind.GreedyLoopSplit:
                case RegexNfaStateKind.LazyLoopSplit:
                    pending.Push((state.Alternative, current.Position));
                    pending.Push((state.Next, current.Position));
                    break;

                case RegexNfaStateKind.Predicate:
                    if (RegexByteClass.PredicateMatches(
                            haystack,
                            current.Position,
                            state.AtomKind,
                            state.MultiLine,
                            state.Crlf,
                            state.LineTerminator,
                            state.Utf8,
                            state.UnicodeClasses))
                    {
                        pending.Push((state.Next, current.Position));
                    }

                    break;

                case RegexNfaStateKind.CaptureStart:
                case RegexNfaStateKind.CaptureEnd:
                    pending.Push((state.Next, current.Position));
                    break;

                case RegexNfaStateKind.Atom:
                    if (state.TryGetAtomMatchLength(haystack, current.Position, out int consume))
                    {
                        pending.Push((state.Next, current.Position + consume));
                    }

                    break;

                case RegexNfaStateKind.Sparse:
                    if (current.Position < haystack.Length &&
                        state.TryGetSparseTarget(haystack[current.Position], out int sparseNext))
                    {
                        pending.Push((sparseNext, current.Position + 1));
                    }

                    break;
            }
        }

        foreach ((int State, int Position) current in visited)
        {
            cache[current] = false;
        }

        return false;
    }

    /// <summary>
    /// Finds the first accepting state in an ordered state set.
    /// </summary>
    /// <param name="nfa">The source NFA.</param>
    /// <param name="threads">The ordered state indexes.</param>
    /// <returns>The accepting state position, or <c>-1</c> when none is present.</returns>
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
