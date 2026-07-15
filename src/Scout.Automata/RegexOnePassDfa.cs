namespace Scout;

/// <summary>
/// Executes one-pass NFA searches and falls back when a step becomes ambiguous.
/// </summary>
/// <param name="nfa">The NFA to execute.</param>
internal sealed class RegexOnePassDfa(RegexNfa nfa)
{
    private readonly RegexNfa _nfa = nfa;
    private readonly PikeVm _fallback = new(nfa);
    private readonly RegexLiteralRunTable _literalRuns = RegexLiteralRunTable.Create(nfa);
    private List<int> _current = [];
    private List<int> _next = [];
    private readonly int[] _visited = new int[nfa.States.Count];
    private readonly int[] _closedSplits = new int[nfa.States.Count];
    private readonly Dictionary<(int State, int Position), bool> _reachabilityCache = [];
    private readonly HashSet<(int State, int Position)> _reachabilityVisited = [];
    private readonly Stack<(int State, int Position)> _reachabilityPending = [];
    private int _threadScratchGeneration;

    /// <summary>
    /// Determines whether an NFA can benefit from the one-pass engine.
    /// </summary>
    /// <param name="nfa">The NFA to inspect.</param>
    /// <returns><see langword="true" /> when the NFA contains supported predicates and branches.</returns>
    public static bool CanCompile(RegexNfa nfa)
    {
        bool sawPredicate = false;
        bool sawBranch = false;
        for (int index = 0; index < nfa.States.Count; index++)
        {
            RegexNfaState state = nfa.States[index];
            RegexNfaStateKind kind = state.Kind;
            if (state.RequiresUtf8ScalarMatch)
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

    /// <summary>
    /// Attempts a leftmost-first match anchored at a byte offset.
    /// </summary>
    /// <param name="haystack">The bytes to match.</param>
    /// <param name="start">The anchored start offset.</param>
    /// <param name="length">The matched byte length when successful.</param>
    /// <returns><see langword="true" /> when the NFA matches at <paramref name="start" />.</returns>
    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        _current.Clear();
        _reachabilityCache.Clear();
        int threadScratchGeneration = NextThreadScratchGeneration();
        AddThread(
            _nfa.StartState,
            haystack,
            start,
            _current,
            _visited,
            _closedSplits,
            threadScratchGeneration);
        int deferredAcceptLength = -1;
        int position = start;
        while (position <= haystack.Length)
        {
            int acceptIndex = IndexOfAccept(_current);
            if (acceptIndex >= 0)
            {
                deferredAcceptLength = position - start;
                if (!HasEarlierConsumer(_current, acceptIndex, haystack, position, _reachabilityCache))
                {
                    length = deferredAcceptLength;
                    return true;
                }
            }

            if (position == haystack.Length)
            {
                break;
            }

            _next.Clear();
            if (!TryStep(haystack, position, _next, out int consume))
            {
                return _fallback.TryMatchAt(haystack, start, out length);
            }

            if (_next.Count == 0)
            {
                if (deferredAcceptLength >= 0)
                {
                    length = deferredAcceptLength;
                    return true;
                }

                length = 0;
                return false;
            }

            position += consume;
            (_current, _next) = (_next, _current);
        }

        if (deferredAcceptLength >= 0)
        {
            length = deferredAcceptLength;
            return true;
        }

        length = 0;
        return false;
    }

    private bool TryStep(
        ReadOnlySpan<byte> haystack,
        int position,
        List<int> destination,
        out int consume)
    {
        consume = 0;
        bool matched = false;
        int matchedStateIndex = -1;
        int target = -1;
        for (int index = 0; index < _current.Count; index++)
        {
            int stateIndex = _current[index];
            RegexNfaState state = _nfa.States[stateIndex];
            int candidateTarget;
            int candidateConsume;
            if (state.Kind == RegexNfaStateKind.Atom &&
                state.TryGetAtomMatchLength(haystack, position, out candidateConsume))
            {
                candidateTarget = state.Next;
            }
            else if (state.Kind == RegexNfaStateKind.Sparse &&
                state.TryGetSparseTarget(haystack[position], out candidateTarget))
            {
                candidateConsume = 1;
            }
            else
            {
                continue;
            }

            if (matched)
            {
                return false;
            }

            matched = true;
            matchedStateIndex = stateIndex;
            target = candidateTarget;
            consume = candidateConsume;
        }

        if (!matched)
        {
            return true;
        }

        if (_literalRuns.TryGet(
            matchedStateIndex,
            out ReadOnlySpan<byte> literalBytes,
            out int literalSuccessor))
        {
            if (literalBytes.Length > haystack.Length - position ||
                !haystack.Slice(position, literalBytes.Length).SequenceEqual(literalBytes))
            {
                consume = 0;
                return true;
            }

            target = literalSuccessor;
            consume = literalBytes.Length;
        }

        int threadScratchGeneration = NextThreadScratchGeneration();
        AddThread(
            target,
            haystack,
            position + consume,
            destination,
            _visited,
            _closedSplits,
            threadScratchGeneration);
        return true;
    }

    private int NextThreadScratchGeneration()
    {
        if (_threadScratchGeneration == int.MaxValue)
        {
            Array.Clear(_visited);
            Array.Clear(_closedSplits);
            _threadScratchGeneration = 0;
        }

        return ++_threadScratchGeneration;
    }

    private void AddThread(
        int stateIndex,
        ReadOnlySpan<byte> haystack,
        int position,
        List<int> threads,
        int[] visited,
        int[] closedSplits,
        int threadScratchGeneration)
    {
        if (stateIndex < 0)
        {
            return;
        }

        if (visited[stateIndex] == threadScratchGeneration)
        {
            AddClosedSplitExit(
                stateIndex,
                haystack,
                position,
                threads,
                visited,
                closedSplits,
                threadScratchGeneration);
            return;
        }

        visited[stateIndex] = threadScratchGeneration;
        RegexNfaState state = _nfa.States[stateIndex];
        switch (state.Kind)
        {
            case RegexNfaStateKind.Split:
            case RegexNfaStateKind.GreedyLoopSplit:
            case RegexNfaStateKind.LazyLoopSplit:
                AddThread(
                    state.Next,
                    haystack,
                    position,
                    threads,
                    visited,
                    closedSplits,
                    threadScratchGeneration);
                AddThread(
                    state.Alternative,
                    haystack,
                    position,
                    threads,
                    visited,
                    closedSplits,
                    threadScratchGeneration);
                break;
            case RegexNfaStateKind.Predicate:
                if (RegexByteClass.PredicateMatches(haystack, position, state.AtomKind, state.MultiLine, state.Crlf, state.LineTerminator, state.Utf8, state.UnicodeClasses))
                {
                    AddThread(
                        state.Next,
                        haystack,
                        position,
                        threads,
                        visited,
                        closedSplits,
                        threadScratchGeneration);
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
        int[] visited,
        int[] closedSplits,
        int threadScratchGeneration)
    {
        RegexNfaState state = _nfa.States[stateIndex];
        if (closedSplits[stateIndex] == threadScratchGeneration)
        {
            return;
        }

        closedSplits[stateIndex] = threadScratchGeneration;
        switch (state.Kind)
        {
            case RegexNfaStateKind.GreedyLoopSplit:
                AddThread(
                    state.Alternative,
                    haystack,
                    position,
                    threads,
                    visited,
                    closedSplits,
                    threadScratchGeneration);
                break;
            case RegexNfaStateKind.LazyLoopSplit:
                AddThread(
                    state.Next,
                    haystack,
                    position,
                    threads,
                    visited,
                    closedSplits,
                    threadScratchGeneration);
                break;
        }
    }

    private bool HasEarlierConsumer(
        List<int> threads,
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
            RegexNfaState state = _nfa.States[threads[index]];
            if (state.Kind == RegexNfaStateKind.Atom &&
                state.TryGetAtomMatchLength(haystack, position, out int consume) &&
                RegexDfaOperations.CanReachAccept(
                    _nfa,
                    state.Next,
                    haystack,
                    position + consume,
                    reachabilityCache,
                    _reachabilityVisited,
                    _reachabilityPending))
            {
                return true;
            }

            if (state.Kind == RegexNfaStateKind.Sparse &&
                state.TryGetSparseTarget(haystack[position], out int sparseNext) &&
                RegexDfaOperations.CanReachAccept(
                    _nfa,
                    sparseNext,
                    haystack,
                    position + 1,
                    reachabilityCache,
                    _reachabilityVisited,
                    _reachabilityPending))
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
            if (_nfa.States[threads[index]].Kind == RegexNfaStateKind.Accept)
            {
                return index;
            }
        }

        return -1;
    }
}
