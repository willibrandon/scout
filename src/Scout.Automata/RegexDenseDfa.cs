namespace Scout;

/// <summary>
/// Executes an eagerly compiled dense DFA while preserving leftmost-first match priority.
/// </summary>
/// <param name="nfa">The ordered NFA represented by the DFA states.</param>
/// <param name="states">The compiled dense DFA states.</param>
internal sealed class RegexDenseDfa(RegexNfa nfa, RegexDenseDfaState[] states)
{
    private const int AlphabetSize = 256;

    private readonly RegexNfa _nfa = nfa;
    private readonly RegexDenseDfaState[] _states = states;

    /// <summary>
    /// Attempts to compile an ordered NFA into a dense DFA within the supplied limits.
    /// </summary>
    /// <param name="nfa">The NFA to compile.</param>
    /// <param name="stateLimit">The maximum number of DFA states.</param>
    /// <param name="dfaSizeLimit">The maximum DFA storage budget in bytes.</param>
    /// <param name="dfa">Receives the compiled DFA when successful.</param>
    /// <returns><see langword="true" /> when the DFA was compiled.</returns>
    public static bool TryCompile(RegexNfa nfa, int stateLimit, ulong dfaSizeLimit, out RegexDenseDfa? dfa)
    {
        if (TryBuild(nfa, stateLimit, dfaSizeLimit, out RegexDenseDfaState[]? states))
        {
            dfa = new RegexDenseDfa(nfa, states!);
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
            RegexDenseDfaState state = _states[current];
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

            current = state.Transitions[haystack[position]];
            if (_states[current].NfaStates.Length == 0)
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

    private static bool TryBuild(RegexNfa nfa, int stateLimit, ulong dfaSizeLimit, out RegexDenseDfaState[]? states)
    {
        var stateSets = new List<int[]>();
        var transitionRows = new List<int[]?>();
        var indexes = new Dictionary<RegexDfaStateKey, int>();
        var budget = new RegexDfaBudget(dfaSizeLimit);
        if (Intern(RegexDfaOperations.Closure(nfa, nfa.StartState)) < 0)
        {
            states = null;
            return false;
        }

        for (int stateIndex = 0; stateIndex < stateSets.Count; stateIndex++)
        {
            int[] transitions = new int[AlphabetSize];
            for (int value = 0; value < AlphabetSize; value++)
            {
                int next = Intern(RegexDfaOperations.Move(nfa, stateSets[stateIndex], (byte)value));
                if (next < 0)
                {
                    states = null;
                    return false;
                }

                transitions[value] = next;
            }

            transitionRows[stateIndex] = transitions;
        }

        states = new RegexDenseDfaState[stateSets.Count];
        for (int stateIndex = 0; stateIndex < states.Length; stateIndex++)
        {
            states[stateIndex] = new RegexDenseDfaState(
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

            if (!budget.TryReserve(RegexDfaBudget.EstimateStateBytes(nfaStates.Length, denseTransitions: true)))
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
