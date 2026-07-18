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
    private readonly ulong[] _asciiAtomMatches = CreateAsciiAtomMatches(nfa);
    private List<int> _current = [];
    private List<int> _next = [];
    private readonly int[] _visited = new int[nfa.States.Count];
    private readonly int[] _closedSplits = new int[nfa.States.Count];
    private int _threadScratchGeneration;
    private long _runnerLeaseVersion;
    private long _activeRunnerLeaseVersion;

    /// <summary>
    /// Begins an exclusive operation-scoped lease for this mutable runner.
    /// </summary>
    /// <returns>The generation token that owns the lease.</returns>
    internal long BeginRunnerLease()
    {
        long leaseVersion = System.Threading.Interlocked.Increment(ref _runnerLeaseVersion);
        System.Threading.Volatile.Write(ref _activeRunnerLeaseVersion, leaseVersion);
        return leaseVersion;
    }

    /// <summary>
    /// Determines whether a generation token still owns this mutable runner.
    /// </summary>
    /// <param name="leaseVersion">The generation token to verify.</param>
    /// <returns><see langword="true" /> when the lease remains active.</returns>
    internal bool IsRunnerLeaseActive(long leaseVersion)
    {
        return leaseVersion != 0 &&
            System.Threading.Volatile.Read(ref _activeRunnerLeaseVersion) == leaseVersion;
    }

    /// <summary>
    /// Attempts to end an exclusive operation-scoped lease exactly once.
    /// </summary>
    /// <param name="leaseVersion">The generation token returned when the lease began.</param>
    /// <returns>
    /// <see langword="true" /> when this call ended the current lease; otherwise,
    /// <see langword="false" /> for a copied, stale, or already returned lease.
    /// </returns>
    internal bool TryEndRunnerLease(long leaseVersion)
    {
        return leaseVersion != 0 &&
            System.Threading.Interlocked.CompareExchange(
                ref _activeRunnerLeaseVersion,
                value: 0,
                comparand: leaseVersion) == leaseVersion;
    }

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
                if (!HasEarlierMatchingConsumer(_current, acceptIndex, haystack, position))
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
                TryGetAtomMatchLength(
                    stateIndex,
                    state,
                    haystack,
                    position,
                    out candidateConsume))
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

    private bool HasEarlierMatchingConsumer(
        List<int> threads,
        int acceptIndex,
        ReadOnlySpan<byte> haystack,
        int position)
    {
        if (position >= haystack.Length)
        {
            return false;
        }

        for (int index = 0; index < acceptIndex; index++)
        {
            int stateIndex = threads[index];
            RegexNfaState state = _nfa.States[stateIndex];
            if (state.Kind == RegexNfaStateKind.Atom &&
                TryGetAtomMatchLength(
                    stateIndex,
                    state,
                    haystack,
                    position,
                    out _))
            {
                return true;
            }

            if (state.Kind == RegexNfaStateKind.Sparse &&
                state.TryGetSparseTarget(haystack[position], out _))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetAtomMatchLength(
        int stateIndex,
        RegexNfaState state,
        ReadOnlySpan<byte> haystack,
        int position,
        out int length)
    {
        if ((uint)position < (uint)haystack.Length && haystack[position] <= 0x7F)
        {
            byte value = haystack[position];
            int word = (stateIndex * 2) + (value >> 6);
            if ((_asciiAtomMatches[word] & (1UL << (value & 0x3F))) != 0)
            {
                length = 1;
                return true;
            }

            length = 0;
            return false;
        }

        return state.TryGetAtomMatchLength(haystack, position, out length);
    }

    private static ulong[] CreateAsciiAtomMatches(RegexNfa nfa)
    {
        ulong[] matches = new ulong[checked(nfa.States.Count * 2)];
        byte[] haystack = new byte[1];
        for (int stateIndex = 0; stateIndex < nfa.States.Count; stateIndex++)
        {
            RegexNfaState state = nfa.States[stateIndex];
            if (state.Kind != RegexNfaStateKind.Atom)
            {
                continue;
            }

            for (int value = 0; value <= 0x7F; value++)
            {
                haystack[0] = (byte)value;
                if (state.TryGetAtomMatchLength(
                        haystack,
                        position: 0,
                        out int length) &&
                    length == 1)
                {
                    int word = (stateIndex * 2) + (value >> 6);
                    matches[word] |= 1UL << (value & 0x3F);
                }
            }
        }

        return matches;
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
