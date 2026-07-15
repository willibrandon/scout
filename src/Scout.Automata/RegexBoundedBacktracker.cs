namespace Scout;

/// <summary>
/// Executes bounded backtracking for linear NFAs and finite acyclic branches.
/// </summary>
/// <param name="nfa">The NFA to execute.</param>
internal sealed class RegexBoundedBacktracker(RegexNfa nfa)
{
    private const int AcyclicWorkLimitPerState = 16;

    private readonly RegexNfa _nfa = nfa;
    private readonly RegexNfaState[] _states = nfa.States as RegexNfaState[] ?? [.. nfa.States];
    private readonly RegexLiteralRunTable _literalRuns = RegexLiteralRunTable.Create(nfa);
    private readonly bool _hasBranchingStates = HasBranchingStates(nfa);
    private readonly int[] _backtrackStates = new int[nfa.States.Count];
    private readonly int[] _backtrackPositions = new int[nfa.States.Count];
    private readonly ulong[] _asciiAtomMatches = CreateAsciiAtomMatches(nfa);
    private readonly PikeVm? _fallbackPikeVm = HasBranchingStates(nfa) ? new PikeVm(nfa) : null;
    private readonly int _acyclicWorkLimit = Math.Max(256, nfa.States.Count * AcyclicWorkLimitPerState);
    private readonly int[] _visitedGenerations = new int[nfa.States.Count];
    private readonly int[] _visitedPositions = new int[nfa.States.Count];
    private int _generation;

    /// <summary>
    /// Determines whether an NFA can use the bounded backtracking engine.
    /// </summary>
    /// <param name="nfa">The NFA to inspect.</param>
    /// <returns>
    /// <see langword="true" /> when the NFA is linear or all of its branches form a finite
    /// acyclic graph.
    /// </returns>
    public static bool CanCompile(RegexNfa nfa)
    {
        bool hasBranchingStates = false;
        for (int index = 0; index < nfa.States.Count; index++)
        {
            RegexNfaStateKind kind = nfa.States[index].Kind;
            if (kind is RegexNfaStateKind.CaptureStart or RegexNfaStateKind.CaptureEnd)
            {
                return false;
            }

            hasBranchingStates |= IsBranchingState(kind);
        }

        return !hasBranchingStates || IsAcyclic(nfa);
    }

    /// <summary>
    /// Attempts a match anchored at a byte offset.
    /// </summary>
    /// <param name="haystack">The bytes to match.</param>
    /// <param name="start">The anchored start offset.</param>
    /// <param name="length">The matched byte length when successful.</param>
    /// <returns><see langword="true" /> when the NFA matches at <paramref name="start" />.</returns>
    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        if ((uint)start > (uint)haystack.Length)
        {
            length = 0;
            return false;
        }

        return _hasBranchingStates
            ? TryMatchAcyclicBranchesAt(haystack, start, out length)
            : TryMatchLinearAt(haystack, start, out length);
    }

    private bool TryMatchLinearAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        int generation = NextGeneration();
        int stateIndex = _nfa.StartState;
        int position = start;
        while (stateIndex >= 0)
        {
            if (_visitedGenerations[stateIndex] == generation &&
                _visitedPositions[stateIndex] == position)
            {
                length = 0;
                return false;
            }

            _visitedGenerations[stateIndex] = generation;
            _visitedPositions[stateIndex] = position;
            RegexNfaState state = _states[stateIndex];
            switch (state.Kind)
            {
                case RegexNfaStateKind.Accept:
                    length = position - start;
                    return true;
                case RegexNfaStateKind.Atom:
                    if (_literalRuns.TryGet(
                        stateIndex,
                        out ReadOnlySpan<byte> literalBytes,
                        out int literalSuccessor))
                    {
                        if (literalBytes.Length > haystack.Length - position ||
                            !haystack.Slice(position, literalBytes.Length).SequenceEqual(literalBytes))
                        {
                            length = 0;
                            return false;
                        }

                        position += literalBytes.Length;
                        stateIndex = literalSuccessor;
                        break;
                    }

                    if (!TryGetAtomMatchLength(stateIndex, state, haystack, position, out int consume))
                    {
                        length = 0;
                        return false;
                    }

                    position += consume;
                    stateIndex = state.Next;
                    break;
                case RegexNfaStateKind.Sparse:
                    if (position >= haystack.Length ||
                        !state.TryGetSparseTarget(haystack[position], out int sparseNext))
                    {
                        length = 0;
                        return false;
                    }

                    position++;
                    stateIndex = sparseNext;
                    break;
                case RegexNfaStateKind.Predicate:
                    if (!RegexByteClass.PredicateMatches(
                        haystack,
                        position,
                        state.AtomKind,
                        state.MultiLine,
                        state.Crlf,
                        state.LineTerminator,
                        state.Utf8,
                        state.UnicodeClasses))
                    {
                        length = 0;
                        return false;
                    }

                    stateIndex = state.Next;
                    break;
                default:
                    length = 0;
                    return false;
            }
        }

        length = 0;
        return false;
    }

    private bool TryMatchAcyclicBranchesAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        int stateIndex = _nfa.StartState;
        int position = start;
        int backtrackCount = 0;
        int work = 0;
        while (true)
        {
            while (stateIndex >= 0)
            {
                if (++work > _acyclicWorkLimit)
                {
                    return _fallbackPikeVm!.TryMatchAt(haystack, start, out length);
                }

                RegexNfaState state = _states[stateIndex];
                switch (state.Kind)
                {
                    case RegexNfaStateKind.Accept:
                        length = position - start;
                        return true;
                    case RegexNfaStateKind.Atom:
                        if (_literalRuns.TryGet(
                            stateIndex,
                            out ReadOnlySpan<byte> literalBytes,
                            out int literalSuccessor))
                        {
                            if (literalBytes.Length > haystack.Length - position ||
                                !haystack.Slice(position, literalBytes.Length).SequenceEqual(literalBytes))
                            {
                                stateIndex = -1;
                                break;
                            }

                            position += literalBytes.Length;
                            stateIndex = literalSuccessor;
                            break;
                        }

                        if (!TryGetAtomMatchLength(stateIndex, state, haystack, position, out int consume))
                        {
                            stateIndex = -1;
                            break;
                        }

                        position += consume;
                        stateIndex = state.Next;
                        break;
                    case RegexNfaStateKind.Sparse:
                        if (position >= haystack.Length ||
                            !state.TryGetSparseTarget(haystack[position], out int sparseNext))
                        {
                            stateIndex = -1;
                            break;
                        }

                        position++;
                        stateIndex = sparseNext;
                        break;
                    case RegexNfaStateKind.Predicate:
                        if (!RegexByteClass.PredicateMatches(
                            haystack,
                            position,
                            state.AtomKind,
                            state.MultiLine,
                            state.Crlf,
                            state.LineTerminator,
                            state.Utf8,
                            state.UnicodeClasses))
                        {
                            stateIndex = -1;
                            break;
                        }

                        stateIndex = state.Next;
                        break;
                    case RegexNfaStateKind.Split:
                    case RegexNfaStateKind.GreedyLoopSplit:
                    case RegexNfaStateKind.LazyLoopSplit:
                        if (state.Alternative >= 0)
                        {
                            _backtrackStates[backtrackCount] = state.Alternative;
                            _backtrackPositions[backtrackCount] = position;
                            backtrackCount++;
                        }

                        stateIndex = state.Next;
                        break;
                    default:
                        stateIndex = -1;
                        break;
                }
            }

            if (backtrackCount == 0)
            {
                length = 0;
                return false;
            }

            backtrackCount--;
            stateIndex = _backtrackStates[backtrackCount];
            position = _backtrackPositions[backtrackCount];
        }
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
                if (state.TryGetAtomMatchLength(haystack, position: 0, out int length) &&
                    length == 1)
                {
                    int word = (stateIndex * 2) + (value >> 6);
                    matches[word] |= 1UL << (value & 0x3F);
                }
            }
        }

        return matches;
    }

    private static bool HasBranchingStates(RegexNfa nfa)
    {
        for (int index = 0; index < nfa.States.Count; index++)
        {
            if (IsBranchingState(nfa.States[index].Kind))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBranchingState(RegexNfaStateKind kind)
    {
        return kind is RegexNfaStateKind.Split
            or RegexNfaStateKind.GreedyLoopSplit
            or RegexNfaStateKind.LazyLoopSplit;
    }

    private static bool IsAcyclic(RegexNfa nfa)
    {
        int[] indegrees = new int[nfa.States.Count];
        for (int stateIndex = 0; stateIndex < nfa.States.Count; stateIndex++)
        {
            VisitSuccessors(nfa.States[stateIndex], successor => indegrees[successor]++);
        }

        int[] queue = new int[nfa.States.Count];
        int queueStart = 0;
        int queueEnd = 0;
        for (int stateIndex = 0; stateIndex < indegrees.Length; stateIndex++)
        {
            if (indegrees[stateIndex] == 0)
            {
                queue[queueEnd++] = stateIndex;
            }
        }

        int visitedCount = 0;
        while (queueStart < queueEnd)
        {
            int stateIndex = queue[queueStart++];
            visitedCount++;
            VisitSuccessors(
                nfa.States[stateIndex],
                successor =>
                {
                    if (--indegrees[successor] == 0)
                    {
                        queue[queueEnd++] = successor;
                    }
                });
        }

        return visitedCount == nfa.States.Count;
    }

    private static void VisitSuccessors(RegexNfaState state, Action<int> visit)
    {
        switch (state.Kind)
        {
            case RegexNfaStateKind.Split:
            case RegexNfaStateKind.GreedyLoopSplit:
            case RegexNfaStateKind.LazyLoopSplit:
                Visit(state.Next);
                Visit(state.Alternative);
                break;
            case RegexNfaStateKind.Sparse:
                for (int index = 0; index < state.SparseTransitions.Length; index++)
                {
                    Visit(state.SparseTransitions[index].Next);
                }

                break;
            case RegexNfaStateKind.Atom:
            case RegexNfaStateKind.Predicate:
                Visit(state.Next);
                break;
        }

        void Visit(int successor)
        {
            if (successor >= 0)
            {
                visit(successor);
            }
        }
    }

    private int NextGeneration()
    {
        if (_generation == int.MaxValue)
        {
            Array.Clear(_visitedGenerations);
            _generation = 0;
        }

        return ++_generation;
    }
}
