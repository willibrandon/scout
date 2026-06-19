
namespace Scout;

/// <summary>
/// Searches for multiple byte patterns with Aho-Corasick semantics.
/// </summary>
public sealed class AhoCorasickAutomaton
{
    private const int MaxEagerDenseTransitionStates = 512;
    private const int MaxPromotedDenseTransitionStates = 32768;
    private const int LazyDensePromotionRowThreshold = 128;

    private readonly AhoCorasickState[] states;
    private int[]? denseTransitions;
    private ushort[]? compactDenseTransitions;
    private readonly int[]?[]? lazyDenseTransitionRows;
    private readonly int[] emptyPatternIds;
    private readonly int[] outputCounts;
    private readonly byte[][] patterns;
    private readonly AhoCorasickMatchKind matchKind;
    private readonly AhoCorasickStartKind startKind;
    private readonly bool asciiCaseInsensitive;
    private int lazyDenseRowsBuilt;

    private AhoCorasickAutomaton(
        AhoCorasickState[] states,
        int[]? denseTransitions,
        ushort[]? compactDenseTransitions,
        int[] emptyPatternIds,
        int[] outputCounts,
        byte[][] patterns,
        AhoCorasickMatchKind matchKind,
        AhoCorasickStartKind startKind,
        bool asciiCaseInsensitive)
    {
        this.states = states;
        this.denseTransitions = denseTransitions;
        this.compactDenseTransitions = compactDenseTransitions;
        lazyDenseTransitionRows = denseTransitions is null && compactDenseTransitions is null ? new int[states.Length][] : null;
        this.emptyPatternIds = emptyPatternIds;
        this.outputCounts = outputCounts;
        this.patterns = patterns;
        this.matchKind = matchKind;
        this.startKind = startKind;
        this.asciiCaseInsensitive = asciiCaseInsensitive;
    }

    /// <summary>
    /// Gets the number of patterns in this automaton.
    /// </summary>
    public int PatternCount => patterns.Length;

    /// <summary>
    /// Gets the match semantics used by this automaton.
    /// </summary>
    public AhoCorasickMatchKind MatchKind => matchKind;

    /// <summary>
    /// Gets the anchored-mode support used by this automaton.
    /// </summary>
    public AhoCorasickStartKind StartKind => startKind;

    /// <summary>
    /// Gets a value indicating whether ASCII case-insensitive matching is enabled.
    /// </summary>
    public bool AsciiCaseInsensitive => asciiCaseInsensitive;

    /// <summary>
    /// Creates a builder for configuring an Aho-Corasick automaton.
    /// </summary>
    /// <returns>A new builder with upstream default configuration.</returns>
    public static AhoCorasickBuilder Builder()
    {
        return new AhoCorasickBuilder();
    }

    /// <summary>
    /// Builds an automaton from byte patterns.
    /// </summary>
    /// <param name="patterns">The ordered patterns to search for.</param>
    /// <returns>An Aho-Corasick automaton.</returns>
    public static AhoCorasickAutomaton Create(IReadOnlyList<byte[]> patterns)
    {
        return Create(patterns, AhoCorasickMatchKind.Standard);
    }

    /// <summary>
    /// Builds an automaton from byte patterns.
    /// </summary>
    /// <param name="patterns">The ordered patterns to search for.</param>
    /// <param name="matchKind">The match semantics to use for non-overlapping search.</param>
    /// <returns>An Aho-Corasick automaton.</returns>
    public static AhoCorasickAutomaton Create(IReadOnlyList<byte[]> patterns, AhoCorasickMatchKind matchKind)
    {
        return Create(patterns, matchKind, asciiCaseInsensitive: false);
    }

    /// <summary>
    /// Builds an automaton from byte patterns.
    /// </summary>
    /// <param name="patterns">The ordered patterns to search for.</param>
    /// <param name="matchKind">The match semantics to use for non-overlapping search.</param>
    /// <param name="asciiCaseInsensitive">Whether to ignore ASCII byte case.</param>
    /// <returns>An Aho-Corasick automaton.</returns>
    public static AhoCorasickAutomaton Create(
        IReadOnlyList<byte[]> patterns,
        AhoCorasickMatchKind matchKind,
        bool asciiCaseInsensitive)
    {
        return Create(patterns, matchKind, asciiCaseInsensitive, AhoCorasickStartKind.Unanchored);
    }

    /// <summary>
    /// Builds an automaton from byte patterns.
    /// </summary>
    /// <param name="patterns">The ordered patterns to search for.</param>
    /// <param name="matchKind">The match semantics to use for non-overlapping search.</param>
    /// <param name="asciiCaseInsensitive">Whether to ignore ASCII byte case.</param>
    /// <param name="startKind">The anchored-mode support to use.</param>
    /// <returns>An Aho-Corasick automaton.</returns>
    public static AhoCorasickAutomaton Create(
        IReadOnlyList<byte[]> patterns,
        AhoCorasickMatchKind matchKind,
        bool asciiCaseInsensitive,
        AhoCorasickStartKind startKind)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ValidateMatchKind(matchKind);
        ValidateStartKind(startKind);

        var states = new List<AhoCorasickState> { new() };
        var emptyPatternIds = new List<int>();
        byte[][] ownedPatterns = new byte[patterns.Count][];

        for (int patternId = 0; patternId < patterns.Count; patternId++)
        {
            byte[] pattern = patterns[patternId];
            ArgumentNullException.ThrowIfNull(pattern);
            ownedPatterns[patternId] = CopyPattern(pattern, asciiCaseInsensitive);

            if (pattern.Length == 0)
            {
                emptyPatternIds.Add(patternId);
                continue;
            }

            AddPattern(states, patternId, ownedPatterns[patternId]);
        }

        BuildFailureLinks(states);
        AhoCorasickState[] builtStates = states.ToArray();
        SortOutputs(builtStates);
        return new AhoCorasickAutomaton(
            builtStates,
            TryBuildDenseTransitions(builtStates, asciiCaseInsensitive),
            TryBuildCompactDenseTransitions(builtStates, asciiCaseInsensitive),
            emptyPatternIds.ToArray(),
            BuildOutputCounts(builtStates),
            ownedPatterns,
            matchKind,
            startKind,
            asciiCaseInsensitive);
    }

    /// <summary>
    /// Finds the first standard non-overlapping match.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <returns>The first match, or <see langword="null" /> when no pattern matches.</returns>
    public AhoCorasickMatch? Find(ReadOnlySpan<byte> haystack)
    {
        AhoCorasickEnumerator enumerator = Enumerate(haystack);
        return enumerator.MoveNext() ? enumerator.Current : null;
    }

    /// <summary>
    /// Finds all standard non-overlapping matches.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <returns>The matches in search order.</returns>
    public IReadOnlyList<AhoCorasickMatch> FindAll(ReadOnlySpan<byte> haystack)
    {
        var matches = new List<AhoCorasickMatch>();
        AhoCorasickEnumerator enumerator = Enumerate(haystack);
        while (enumerator.MoveNext())
        {
            matches.Add(enumerator.Current);
        }

        return matches;
    }

    /// <summary>
    /// Enumerates all non-overlapping matches in forward order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <returns>An allocation-free non-overlapping match enumerator.</returns>
    public AhoCorasickEnumerator Enumerate(ReadOnlySpan<byte> haystack)
    {
        EnsureUnanchoredSupported();
        return new AhoCorasickEnumerator(this, haystack, anchored: false);
    }

    /// <summary>
    /// Finds the first anchored non-overlapping match.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <returns>The first anchored match, or <see langword="null" /> when no pattern matches.</returns>
    public AhoCorasickMatch? FindAnchored(ReadOnlySpan<byte> haystack)
    {
        AhoCorasickEnumerator enumerator = EnumerateAnchored(haystack);
        return enumerator.MoveNext() ? enumerator.Current : null;
    }

    /// <summary>
    /// Finds all anchored non-overlapping matches.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <returns>The matches in search order.</returns>
    public IReadOnlyList<AhoCorasickMatch> FindAllAnchored(ReadOnlySpan<byte> haystack)
    {
        var matches = new List<AhoCorasickMatch>();
        AhoCorasickEnumerator enumerator = EnumerateAnchored(haystack);
        while (enumerator.MoveNext())
        {
            matches.Add(enumerator.Current);
        }

        return matches;
    }

    /// <summary>
    /// Enumerates all anchored non-overlapping matches in forward order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <returns>An allocation-free non-overlapping match enumerator.</returns>
    public AhoCorasickEnumerator EnumerateAnchored(ReadOnlySpan<byte> haystack)
    {
        EnsureAnchoredSupported();
        return new AhoCorasickEnumerator(this, haystack, anchored: true);
    }

    /// <summary>
    /// Finds all standard overlapping matches.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <returns>The matches in search order.</returns>
    public IReadOnlyList<AhoCorasickMatch> FindOverlapping(ReadOnlySpan<byte> haystack)
    {
        var matches = new List<AhoCorasickMatch>();
        AhoCorasickOverlappingEnumerator enumerator = EnumerateOverlapping(haystack);
        while (enumerator.MoveNext())
        {
            matches.Add(enumerator.Current);
        }

        return matches;
    }

    /// <summary>
    /// Enumerates all overlapping matches in forward order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <returns>An allocation-free overlapping match enumerator.</returns>
    public AhoCorasickOverlappingEnumerator EnumerateOverlapping(ReadOnlySpan<byte> haystack)
    {
        EnsureUnanchoredSupported();
        if (matchKind != AhoCorasickMatchKind.Standard)
        {
            throw new InvalidOperationException("overlapping search requires standard match semantics");
        }

        return new AhoCorasickOverlappingEnumerator(this, haystack);
    }

    internal int EmptyPatternCount => emptyPatternIds.Length;

    internal AhoCorasickMatch? FindNextNonOverlapping(
        ReadOnlySpan<byte> haystack,
        int offset,
        int skipEmptyAt,
        bool anchored)
    {
        if (matchKind != AhoCorasickMatchKind.Standard)
        {
            return anchored
                ? FindBestAt(haystack, offset, skipEmptyAt)
                : FindNextLeftmost(haystack, offset, skipEmptyAt);
        }

        if (emptyPatternIds.Length != 0)
        {
            return offset <= haystack.Length
                ? new AhoCorasickMatch(emptyPatternIds[0], offset, offset)
                : null;
        }

        return anchored
            ? FindStandardAnchoredAt(haystack, offset)
            : FindStandardAtOrAfter(haystack, offset);
    }

    internal AhoCorasickMatch GetEmptyPatternMatch(int emptyIndex, int offset)
    {
        return new AhoCorasickMatch(emptyPatternIds[emptyIndex], offset, offset);
    }

    internal int GetOutputCount(int state)
    {
        return outputCounts[state];
    }

    internal int[] GetOutputCountsForEnumerator()
    {
        return outputCounts;
    }

    internal AhoCorasickMatch GetOutputMatch(int state, int outputIndex, int end)
    {
        AhoCorasickOutput output = states[state].Outputs[outputIndex];
        return new AhoCorasickMatch(output.PatternId, end - output.Length, end);
    }

    internal int NextStateForEnumerator(int state, byte value)
    {
        return NextState(state, value);
    }

    internal int[]? GetDenseTransitionsForEnumerator()
    {
        return System.Threading.Volatile.Read(ref denseTransitions);
    }

    internal ushort[]? GetCompactDenseTransitionsForEnumerator()
    {
        return System.Threading.Volatile.Read(ref compactDenseTransitions);
    }

    private static void ValidateMatchKind(AhoCorasickMatchKind matchKind)
    {
        if (matchKind is not (
            AhoCorasickMatchKind.Standard or
            AhoCorasickMatchKind.LeftmostFirst or
            AhoCorasickMatchKind.LeftmostLongest))
        {
            throw new ArgumentOutOfRangeException(nameof(matchKind));
        }
    }

    private static void ValidateStartKind(AhoCorasickStartKind startKind)
    {
        if (startKind is not (
            AhoCorasickStartKind.Both or
            AhoCorasickStartKind.Unanchored or
            AhoCorasickStartKind.Anchored))
        {
            throw new ArgumentOutOfRangeException(nameof(startKind));
        }
    }

    private void EnsureUnanchoredSupported()
    {
        if (startKind == AhoCorasickStartKind.Anchored)
        {
            throw new InvalidOperationException(
                "unanchored search requested but automaton only supports anchored searches");
        }
    }

    private void EnsureAnchoredSupported()
    {
        if (startKind == AhoCorasickStartKind.Unanchored)
        {
            throw new InvalidOperationException(
                "anchored search requested but automaton only supports unanchored searches");
        }
    }

    private static void AddPattern(List<AhoCorasickState> states, int patternId, ReadOnlySpan<byte> pattern)
    {
        int state = 0;
        foreach (byte value in pattern)
        {
            Dictionary<byte, int> transitions = states[state].Transitions;
            if (!transitions.TryGetValue(value, out int next))
            {
                next = states.Count;
                transitions.Add(value, next);
                states.Add(new AhoCorasickState());
            }

            state = next;
        }

        states[state].Outputs.Add(new AhoCorasickOutput(patternId, pattern.Length));
    }

    private static byte[] CopyPattern(ReadOnlySpan<byte> pattern, bool asciiCaseInsensitive)
    {
        byte[] copy = pattern.ToArray();
        if (!asciiCaseInsensitive)
        {
            return copy;
        }

        for (int index = 0; index < copy.Length; index++)
        {
            copy[index] = FoldAscii(copy[index]);
        }

        return copy;
    }

    private static void BuildFailureLinks(List<AhoCorasickState> states)
    {
        var queue = new Queue<int>();
        foreach (KeyValuePair<byte, int> transition in states[0].Transitions)
        {
            states[transition.Value].Failure = 0;
            queue.Enqueue(transition.Value);
        }

        while (queue.Count != 0)
        {
            int current = queue.Dequeue();
            foreach (KeyValuePair<byte, int> transition in states[current].Transitions)
            {
                byte value = transition.Key;
                int target = transition.Value;
                queue.Enqueue(target);

                int failure = states[current].Failure;
                while (failure != 0 && !states[failure].Transitions.ContainsKey(value))
                {
                    failure = states[failure].Failure;
                }

                if (states[failure].Transitions.TryGetValue(value, out int failureTarget))
                {
                    states[target].Failure = failureTarget;
                    states[target].Outputs.AddRange(states[failureTarget].Outputs);
                }
            }
        }
    }

    private static void SortOutputs(AhoCorasickState[] states)
    {
        foreach (AhoCorasickState state in states)
        {
            state.Outputs.Sort(CompareOutputs);
        }
    }

    private static int[] BuildOutputCounts(AhoCorasickState[] states)
    {
        int[] counts = new int[states.Length];
        for (int state = 0; state < states.Length; state++)
        {
            counts[state] = states[state].Outputs.Count;
        }

        return counts;
    }

    private static int CompareOutputs(AhoCorasickOutput left, AhoCorasickOutput right)
    {
        int lengthComparison = right.Length.CompareTo(left.Length);
        return lengthComparison != 0
            ? lengthComparison
            : left.PatternId.CompareTo(right.PatternId);
    }

    private AhoCorasickMatch? FindNextLeftmost(ReadOnlySpan<byte> haystack, int offset, int skipEmptyAt)
    {
        for (int start = offset; start <= haystack.Length; start++)
        {
            AhoCorasickMatch? best = FindBestAt(haystack, start, skipEmptyAt);
            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    private AhoCorasickMatch? FindBestAt(ReadOnlySpan<byte> haystack, int start, int skipEmptyAt)
    {
        AhoCorasickMatch? best = null;
        for (int patternId = 0; patternId < patterns.Length; patternId++)
        {
            ReadOnlySpan<byte> pattern = patterns[patternId];
            if (pattern.IsEmpty && start == skipEmptyAt)
            {
                continue;
            }

            if (!MatchesAt(haystack, start, pattern))
            {
                continue;
            }

            var candidate = new AhoCorasickMatch(patternId, start, start + pattern.Length);
            if (IsBetterLeftmost(candidate, best))
            {
                best = candidate;
            }
        }

        return best;
    }

    private bool IsBetterLeftmost(AhoCorasickMatch candidate, AhoCorasickMatch? best)
    {
        if (best is null)
        {
            return true;
        }

        AhoCorasickMatch current = best.Value;
        if (matchKind == AhoCorasickMatchKind.LeftmostFirst)
        {
            return candidate.PatternId < current.PatternId;
        }

        int lengthComparison = candidate.Length.CompareTo(current.Length);
        return lengthComparison > 0 || (lengthComparison == 0 && candidate.PatternId < current.PatternId);
    }

    private bool MatchesAt(ReadOnlySpan<byte> haystack, int start, ReadOnlySpan<byte> pattern)
    {
        if (start > haystack.Length || pattern.Length > haystack.Length - start)
        {
            return false;
        }

        if (!asciiCaseInsensitive)
        {
            return haystack.Slice(start, pattern.Length).SequenceEqual(pattern);
        }

        for (int index = 0; index < pattern.Length; index++)
        {
            if (FoldAscii(haystack[start + index]) != pattern[index])
            {
                return false;
            }
        }

        return true;
    }

    private AhoCorasickMatch? FindStandardAtOrAfter(ReadOnlySpan<byte> haystack, int offset)
    {
        int state = 0;
        for (int index = offset; index < haystack.Length; index++)
        {
            state = NextState(state, haystack[index]);
            List<AhoCorasickOutput> outputs = states[state].Outputs;
            if (outputs.Count != 0)
            {
                AhoCorasickOutput output = outputs[0];
                int end = index + 1;
                return new AhoCorasickMatch(output.PatternId, end - output.Length, end);
            }
        }

        return null;
    }

    private AhoCorasickMatch? FindStandardAnchoredAt(ReadOnlySpan<byte> haystack, int offset)
    {
        int state = 0;
        for (int index = offset; index < haystack.Length; index++)
        {
            state = NextState(state, haystack[index]);
            int end = index + 1;
            foreach (AhoCorasickOutput output in states[state].Outputs)
            {
                int start = end - output.Length;
                if (start == offset)
                {
                    return new AhoCorasickMatch(output.PatternId, start, end);
                }
            }
        }

        return null;
    }

    private int NextState(int state, byte value)
    {
        ushort[]? compactTransitions = System.Threading.Volatile.Read(ref compactDenseTransitions);
        if (compactTransitions is not null)
        {
            return compactTransitions[(state * 256) + value];
        }

        int[]? transitions = System.Threading.Volatile.Read(ref denseTransitions);
        if (transitions is not null)
        {
            return transitions[(state * 256) + value];
        }

        int[]?[]? denseRows = lazyDenseTransitionRows;
        if (denseRows is not null)
        {
            int[]? row = denseRows[state];
            if (row is null)
            {
                row = BuildDenseTransitionRow(state);
                denseRows[state] = row;
                if (System.Threading.Interlocked.Increment(ref lazyDenseRowsBuilt) == LazyDensePromotionRowThreshold)
                {
                    TryPromoteDenseTransitions();
                }
            }

            return row[value];
        }

        return NextSparseState(states, state, value);
    }

    private int[] BuildDenseTransitionRow(int state)
    {
        int[] transitions = new int[256];
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            byte lookup = asciiCaseInsensitive ? FoldAscii((byte)value) : (byte)value;
            transitions[value] = NextSparseState(states, state, lookup);
        }

        return transitions;
    }

    private void TryPromoteDenseTransitions()
    {
        if (states.Length > MaxPromotedDenseTransitionStates ||
            System.Threading.Volatile.Read(ref compactDenseTransitions) is not null ||
            System.Threading.Volatile.Read(ref denseTransitions) is not null)
        {
            return;
        }

        if (CanUseCompactDenseTransitions(states))
        {
            ushort[] transitions = BuildCompactDenseTransitions(states, asciiCaseInsensitive);
            System.Threading.Volatile.Write(ref compactDenseTransitions, transitions);
        }
        else
        {
            int[] transitions = BuildDenseTransitions(states, asciiCaseInsensitive);
            System.Threading.Volatile.Write(ref denseTransitions, transitions);
        }
    }

    private static int[]? TryBuildDenseTransitions(AhoCorasickState[] states, bool asciiCaseInsensitive)
    {
        if (states.Length > MaxEagerDenseTransitionStates ||
            CanUseCompactDenseTransitions(states))
        {
            return null;
        }

        return BuildDenseTransitions(states, asciiCaseInsensitive);
    }

    private static ushort[]? TryBuildCompactDenseTransitions(AhoCorasickState[] states, bool asciiCaseInsensitive)
    {
        if (states.Length > MaxEagerDenseTransitionStates ||
            !CanUseCompactDenseTransitions(states))
        {
            return null;
        }

        return BuildCompactDenseTransitions(states, asciiCaseInsensitive);
    }

    private static bool CanUseCompactDenseTransitions(AhoCorasickState[] states)
    {
        return states.Length <= ushort.MaxValue;
    }

    private static int[] BuildDenseTransitions(AhoCorasickState[] states, bool asciiCaseInsensitive)
    {
        int[] transitions = new int[states.Length * 256];
        for (int state = 0; state < states.Length; state++)
        {
            int baseIndex = state * 256;
            for (int value = 0; value <= byte.MaxValue; value++)
            {
                byte lookup = asciiCaseInsensitive ? FoldAscii((byte)value) : (byte)value;
                transitions[baseIndex + value] = NextSparseState(states, state, lookup);
            }
        }

        return transitions;
    }

    private static ushort[] BuildCompactDenseTransitions(AhoCorasickState[] states, bool asciiCaseInsensitive)
    {
        ushort[] transitions = new ushort[states.Length * 256];
        for (int state = 0; state < states.Length; state++)
        {
            int baseIndex = state * 256;
            for (int value = 0; value <= byte.MaxValue; value++)
            {
                byte lookup = asciiCaseInsensitive ? FoldAscii((byte)value) : (byte)value;
                transitions[baseIndex + value] = (ushort)NextSparseState(states, state, lookup);
            }
        }

        return transitions;
    }

    private static int NextSparseState(AhoCorasickState[] states, int state, byte value)
    {
        while (true)
        {
            if (states[state].Transitions.TryGetValue(value, out int next))
            {
                return next;
            }

            if (state == 0)
            {
                return 0;
            }

            state = states[state].Failure;
        }
    }

    private static byte FoldAscii(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 0x20) : value;
    }
}
