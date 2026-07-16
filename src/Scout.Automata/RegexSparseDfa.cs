namespace Scout;

/// <summary>
/// Executes an eagerly compiled sparse DFA while preserving leftmost-first match priority.
/// </summary>
/// <param name="nfa">The ordered NFA represented by the DFA states.</param>
/// <param name="states">The compiled sparse DFA states.</param>
internal sealed class RegexSparseDfa(RegexNfa nfa, RegexSparseDfaState[] states)
{
    private const int AlphabetSize = 256;

    private readonly RegexNfa _nfa = nfa;
    private readonly RegexSparseDfaState[] _states = states;

    /// <summary>
    /// Attempts to compile an ordered NFA into a sparse DFA within the supplied limits.
    /// </summary>
    /// <param name="nfa">The NFA to compile.</param>
    /// <param name="stateLimit">The maximum number of DFA states.</param>
    /// <param name="dfaSizeLimit">The maximum DFA storage budget in bytes.</param>
    /// <param name="dfa">Receives the compiled DFA when successful.</param>
    /// <returns><see langword="true" /> when the DFA was compiled.</returns>
    public static bool TryCompile(RegexNfa nfa, int stateLimit, ulong dfaSizeLimit, out RegexSparseDfa? dfa)
    {
        if (TryBuild(nfa, stateLimit, dfaSizeLimit, out RegexSparseDfaState[]? states))
        {
            dfa = new RegexSparseDfa(nfa, states!);
            return true;
        }

        dfa = null;
        return false;
    }

    /// <summary>
    /// Attempts a leftmost-first match anchored at a byte offset.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="start">The byte offset at which matching begins.</param>
    /// <param name="length">Receives the accepted match length.</param>
    /// <returns><see langword="true" /> when a match is accepted.</returns>
    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        int current = 0;
        int deferredAcceptLength = -1;
        Dictionary<(int State, int Position), bool>? reachabilityCache = null;
        for (int position = start; position <= haystack.Length; position++)
        {
            RegexSparseDfaState state = _states[current];
            int acceptIndex = state.AcceptIndex;
            if (acceptIndex >= 0)
            {
                deferredAcceptLength = position - start;
                if (acceptIndex == 0)
                {
                    length = deferredAcceptLength;
                    return true;
                }

                reachabilityCache ??= [];
                if (!RegexDfaOperations.HasEarlierConsumer(
                        _nfa,
                        state.NfaStates,
                        acceptIndex,
                        haystack,
                        position,
                        reachabilityCache))
                {
                    length = deferredAcceptLength;
                    return true;
                }
            }

            if (position == haystack.Length)
            {
                break;
            }

            if (!state.Transitions.TryGetValue(haystack[position], out current))
            {
                if (deferredAcceptLength >= 0)
                {
                    length = deferredAcceptLength;
                    return true;
                }

                length = 0;
                return false;
            }
        }

        if (deferredAcceptLength >= 0)
        {
            length = deferredAcceptLength;
            return true;
        }

        length = 0;
        return false;
    }

    private static bool TryBuild(RegexNfa nfa, int stateLimit, ulong dfaSizeLimit, out RegexSparseDfaState[]? states)
    {
        var stateSets = new List<int[]>();
        var transitionRows = new List<Dictionary<byte, int>?>();
        var indexes = new Dictionary<RegexDfaStateKey, int>();
        var budget = new RegexDfaBudget(dfaSizeLimit);
        if (Intern(RegexDfaOperations.Closure(nfa, nfa.StartState)) < 0)
        {
            states = null;
            return false;
        }

        for (int stateIndex = 0; stateIndex < stateSets.Count; stateIndex++)
        {
            var transitions = new Dictionary<byte, int>();
            for (int value = 0; value < AlphabetSize; value++)
            {
                int[] next = RegexDfaOperations.Move(nfa, stateSets[stateIndex], (byte)value);
                if (next.Length > 0)
                {
                    if (!budget.TryReserve(RegexDfaBudget.SparseTransitionBytes))
                    {
                        states = null;
                        return false;
                    }

                    int nextIndex = Intern(next);
                    if (nextIndex < 0)
                    {
                        states = null;
                        return false;
                    }

                    transitions.Add((byte)value, nextIndex);
                }
            }

            transitionRows[stateIndex] = transitions;
        }

        states = new RegexSparseDfaState[stateSets.Count];
        for (int stateIndex = 0; stateIndex < states.Length; stateIndex++)
        {
            states[stateIndex] = new RegexSparseDfaState(
                stateSets[stateIndex],
                RegexDfaOperations.IndexOfAccept(nfa, stateSets[stateIndex]),
                transitionRows[stateIndex]!);
        }

        return true;

        int Intern(int[] nfaStates)
        {
            var key = new RegexDfaStateKey(nfaStates);
            if (indexes.TryGetValue(key, out int existing))
            {
                return existing;
            }

            if (stateSets.Count >= stateLimit)
            {
                return -1;
            }

            if (!budget.TryReserve(RegexDfaBudget.EstimateStateBytes(nfaStates.Length, denseTransitions: false)))
            {
                return -1;
            }

            int index = stateSets.Count;
            indexes.Add(key, index);
            stateSets.Add(nfaStates);
            transitionRows.Add(null);
            return index;
        }
    }
}
