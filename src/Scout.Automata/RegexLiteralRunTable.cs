namespace Scout;

/// <summary>
/// Stores maximal deterministic runs of case-sensitive single-byte literal NFA states.
/// </summary>
/// <param name="literalBytesByState">The literal run beginning at each eligible entry state.</param>
/// <param name="successorsByState">The successor reached after consuming each literal run.</param>
internal sealed class RegexLiteralRunTable(
    ReadOnlyMemory<byte>[] literalBytesByState,
    int[] successorsByState)
{
    private const int MinimumRunLength = 2;

    private readonly ReadOnlyMemory<byte>[] _literalBytesByState = literalBytesByState;
    private readonly int[] _successorsByState = successorsByState;

    /// <summary>
    /// Builds the deterministic literal runs for an NFA.
    /// </summary>
    /// <param name="nfa">The NFA to inspect.</param>
    /// <returns>The precomputed literal-run table.</returns>
    public static RegexLiteralRunTable Create(RegexNfa nfa)
    {
        int stateCount = nfa.States.Count;
        int[] incomingCounts = new int[stateCount];
        int[] incomingSources = new int[stateCount];
        Array.Fill(incomingSources, -1);

        for (int stateIndex = 0; stateIndex < stateCount; stateIndex++)
        {
            RegexNfaState state = nfa.States[stateIndex];
            AddIncoming(stateIndex, state.Next, incomingCounts, incomingSources);
            AddIncoming(stateIndex, state.Alternative, incomingCounts, incomingSources);
            for (int transitionIndex = 0; transitionIndex < state.SparseTransitions.Length; transitionIndex++)
            {
                AddIncoming(
                    stateIndex,
                    state.SparseTransitions[transitionIndex].Next,
                    incomingCounts,
                    incomingSources);
            }
        }

        var literalBytesByState = new ReadOnlyMemory<byte>[stateCount];
        int[] successorsByState = new int[stateCount];
        Array.Fill(successorsByState, -1);
        int[] visitedGenerations = new int[stateCount];
        int generation = 0;
        for (int stateIndex = 0; stateIndex < stateCount; stateIndex++)
        {
            if (!IsLiteralRunEntry(
                nfa,
                stateIndex,
                incomingCounts,
                incomingSources))
            {
                continue;
            }

            generation++;
            List<byte> bytes = [];
            int current = stateIndex;
            int successor = -1;
            while (TryGetLiteralByte(nfa.States[current], out byte value))
            {
                visitedGenerations[current] = generation;
                bytes.Add(value);
                successor = nfa.States[current].Next;
                if ((uint)successor >= (uint)stateCount ||
                    successor == nfa.StartState ||
                    incomingCounts[successor] != 1 ||
                    incomingSources[successor] != current ||
                    visitedGenerations[successor] == generation ||
                    !TryGetLiteralByte(nfa.States[successor], out _))
                {
                    break;
                }

                current = successor;
            }

            if (bytes.Count >= MinimumRunLength)
            {
                literalBytesByState[stateIndex] = bytes.ToArray();
                successorsByState[stateIndex] = successor;
            }
        }

        return new RegexLiteralRunTable(literalBytesByState, successorsByState);
    }

    /// <summary>
    /// Attempts to get the literal run beginning at an NFA state.
    /// </summary>
    /// <param name="stateIndex">The candidate NFA state.</param>
    /// <param name="literalBytes">Receives the bytes consumed by the run.</param>
    /// <param name="successor">Receives the state reached after the run.</param>
    /// <returns><see langword="true" /> when a literal run begins at the state.</returns>
    public bool TryGet(
        int stateIndex,
        out ReadOnlySpan<byte> literalBytes,
        out int successor)
    {
        ReadOnlyMemory<byte> bytes = _literalBytesByState[stateIndex];
        literalBytes = bytes.Span;
        successor = _successorsByState[stateIndex];
        return !bytes.IsEmpty;
    }

    private static void AddIncoming(
        int source,
        int target,
        int[] incomingCounts,
        int[] incomingSources)
    {
        if ((uint)target >= (uint)incomingCounts.Length)
        {
            return;
        }

        incomingCounts[target]++;
        incomingSources[target] = source;
    }

    private static bool IsLiteralRunEntry(
        RegexNfa nfa,
        int stateIndex,
        int[] incomingCounts,
        int[] incomingSources)
    {
        if (!TryGetLiteralByte(nfa.States[stateIndex], out _))
        {
            return false;
        }

        if (stateIndex == nfa.StartState || incomingCounts[stateIndex] != 1)
        {
            return true;
        }

        int predecessor = incomingSources[stateIndex];
        return predecessor < 0 ||
            nfa.States[predecessor].Next != stateIndex ||
            !TryGetLiteralByte(nfa.States[predecessor], out _);
    }

    private static bool TryGetLiteralByte(RegexNfaState state, out byte value)
    {
        value = 0;
        if (state.Kind != RegexNfaStateKind.Atom ||
            state.AtomKind != RegexSyntaxKind.Literal ||
            state.CaseInsensitive ||
            state.RequiresUtf8ScalarMatch ||
            state.Value.Length != 1)
        {
            return false;
        }

        value = state.Value.Span[0];
        return state.AtomMatches(value);
    }
}
