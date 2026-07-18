using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Scout;

/// <summary>
/// Executes a fully materialized leftmost-first unanchored DFA through one shared transition table.
/// </summary>
/// <param name="transitions">The row-major transition table containing premultiplied state offsets.</param>
/// <param name="startStates">The premultiplied start-state offsets indexed by preceding-byte context.</param>
/// <param name="byteContexts">The preceding-byte context for every byte value.</param>
/// <param name="maximumSpecialState">
/// The greatest premultiplied state offset reserved for dead and accepting states.
/// </param>
internal sealed class RegexUnanchoredDenseDfa(
    int[] transitions,
    int[] startStates,
    byte[] byteContexts,
    int maximumSpecialState)
{
    private const int ByteCount = 256;
    private const int EndOfInput = ByteCount;
    private const int AlphabetSize = ByteCount + 1;
    private const byte StartContext = 0;

    private readonly int[] _transitions = transitions;
    private readonly int[] _startStates = startStates;
    private readonly byte[] _byteContexts = byteContexts;
    private readonly int _maximumSpecialState = maximumSpecialState;

    /// <summary>
    /// Determines whether every NFA state is supported by delayed-match byte determinization.
    /// </summary>
    /// <param name="nfa">The NFA to inspect.</param>
    /// <returns><see langword="true" /> when the NFA can be determinized.</returns>
    internal static bool CanCompile(RegexNfa nfa)
    {
        ArgumentNullException.ThrowIfNull(nfa);
        for (int index = 0; index < nfa.States.Count; index++)
        {
            RegexNfaState state = nfa.States[index];
            if (state.RequiresUtf8ScalarMatch ||
                state.Kind == RegexNfaStateKind.Predicate &&
                (state.Utf8 || state.UnicodeClasses || !IsSupportedPredicate(state.AtomKind)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Attempts to compile a byte-oriented NFA into a bounded leftmost-first transition table.
    /// </summary>
    /// <param name="nfa">The unanchored forward NFA.</param>
    /// <param name="stateLimit">The maximum number of materialized DFA states.</param>
    /// <param name="dfaSizeLimit">The maximum estimated DFA storage in bytes.</param>
    /// <param name="dfa">Receives the compiled DFA when successful.</param>
    /// <returns><see langword="true" /> when every reachable state fits within both limits.</returns>
    internal static bool TryCompile(
        RegexNfa nfa,
        int stateLimit,
        ulong dfaSizeLimit,
        out RegexUnanchoredDenseDfa? dfa)
    {
        ArgumentNullException.ThrowIfNull(nfa);
        if (stateLimit <= 0 || !CanCompile(nfa))
        {
            dfa = null;
            return false;
        }

        byte[] byteContexts = new byte[ByteCount];
        byte[] contextRepresentatives = new byte[ByteCount + 1];
        int contextCount = BuildPreviousContexts(nfa, byteContexts, contextRepresentatives);
        byte[] byteClasses = new byte[ByteCount];
        byte[] byteRepresentatives = new byte[ByteCount];
        int byteClassCount = BuildByteClasses(
            nfa,
            byteContexts,
            byteClasses,
            byteRepresentatives);
        int[] initialStates = [nfa.StartState];

        for (byte previousContext = 0; previousContext < contextCount; previousContext++)
        {
            for (int byteClass = 0; byteClass < byteClassCount; byteClass++)
            {
                ResolveContextualClosure(
                    nfa,
                    initialStates,
                    previousContext,
                    byteRepresentatives[byteClass],
                    contextRepresentatives,
                    out _,
                    out bool accepting);
                if (accepting)
                {
                    dfa = null;
                    return false;
                }
            }

            ResolveContextualClosure(
                nfa,
                initialStates,
                previousContext,
                EndOfInput,
                contextRepresentatives,
                out _,
                out bool acceptsAtEnd);
            if (acceptsAtEnd)
            {
                dfa = null;
                return false;
            }
        }

        var stateSets = new List<int[]>();
        var previousContexts = new List<byte>();
        var acceptingStates = new List<bool>();
        var transitionRows = new List<int[]?>();
        var indexes = new Dictionary<RegexLookaroundDfaStateKey, int>();
        var budget = new RegexDfaBudget(dfaSizeLimit);
        int[] startStateIndexes = new int[contextCount];
        for (byte previousContext = 0; previousContext < contextCount; previousContext++)
        {
            int startState = Intern(initialStates, previousContext, accepting: false);
            if (startState < 0)
            {
                dfa = null;
                return false;
            }

            startStateIndexes[previousContext] = startState;
        }

        int deadState = Intern(Array.Empty<int>(), StartContext, accepting: false);
        if (deadState < 0)
        {
            dfa = null;
            return false;
        }

        for (int stateIndex = 0; stateIndex < stateSets.Count; stateIndex++)
        {
            int[] transitions = new int[AlphabetSize];
            if (stateSets[stateIndex].Length == 0)
            {
                Array.Fill(transitions, deadState);
                transitionRows[stateIndex] = transitions;
                continue;
            }

            for (int byteClass = 0; byteClass < byteClassCount; byteClass++)
            {
                byte value = byteRepresentatives[byteClass];
                ResolveContextualClosure(
                    nfa,
                    stateSets[stateIndex],
                    previousContexts[stateIndex],
                    value,
                    contextRepresentatives,
                    out int[] consumers,
                    out bool accepting);
                int[] nextStates = MoveWithoutClosure(nfa, consumers, value);
                int next = Intern(nextStates, byteContexts[value], accepting);
                if (next < 0)
                {
                    dfa = null;
                    return false;
                }

                for (int candidate = 0; candidate < ByteCount; candidate++)
                {
                    if (byteClasses[candidate] == byteClass)
                    {
                        transitions[candidate] = next;
                    }
                }
            }

            ResolveContextualClosure(
                nfa,
                stateSets[stateIndex],
                previousContexts[stateIndex],
                EndOfInput,
                contextRepresentatives,
                out _,
                out bool acceptsAtEnd);
            transitions[EndOfInput] = Intern(
                Array.Empty<int>(),
                StartContext,
                acceptsAtEnd);
            if (transitions[EndOfInput] < 0)
            {
                dfa = null;
                return false;
            }

            transitionRows[stateIndex] = transitions;
        }

        int[] remappedStates = new int[stateSets.Count];
        remappedStates[deadState] = 0;
        int nextState = 1;
        for (int stateIndex = 0; stateIndex < stateSets.Count; stateIndex++)
        {
            if (acceptingStates[stateIndex])
            {
                remappedStates[stateIndex] = nextState++;
            }
        }

        int maximumSpecialState = checked((nextState - 1) * AlphabetSize);
        for (int stateIndex = 0; stateIndex < stateSets.Count; stateIndex++)
        {
            if (stateIndex != deadState && !acceptingStates[stateIndex])
            {
                remappedStates[stateIndex] = nextState++;
            }
        }

        System.Diagnostics.Debug.Assert(nextState == stateSets.Count);
        int[] flattenedTransitions = GC.AllocateUninitializedArray<int>(
            checked(stateSets.Count * AlphabetSize));
        for (int stateIndex = 0; stateIndex < stateSets.Count; stateIndex++)
        {
            int targetOffset = checked(remappedStates[stateIndex] * AlphabetSize);
            int[] row = transitionRows[stateIndex]!;
            for (int value = 0; value < AlphabetSize; value++)
            {
                flattenedTransitions[targetOffset + value] = checked(
                    remappedStates[row[value]] * AlphabetSize);
            }
        }

        int[] remappedStartStates = new int[contextCount];
        for (int context = 0; context < contextCount; context++)
        {
            remappedStartStates[context] = checked(
                remappedStates[startStateIndexes[context]] * AlphabetSize);
        }

        dfa = new RegexUnanchoredDenseDfa(
            flattenedTransitions,
            remappedStartStates,
            byteContexts,
            maximumSpecialState);
        return true;

        int Intern(int[] nfaStates, byte previousContext, bool accepting)
        {
            if (nfaStates.Length == 0)
            {
                previousContext = StartContext;
            }

            var key = new RegexLookaroundDfaStateKey(nfaStates, previousContext, accepting);
            if (indexes.TryGetValue(key, out int existing))
            {
                return existing;
            }

            ulong estimatedBytes = RegexDfaBudget.EstimateStateBytes(
                nfaStates.Length,
                denseTransitions: true);
            if (estimatedBytes != ulong.MaxValue)
            {
                estimatedBytes = estimatedBytes > ulong.MaxValue - sizeof(int)
                    ? ulong.MaxValue
                    : estimatedBytes + sizeof(int);
            }

            if (stateSets.Count >= stateLimit || !budget.TryReserve(estimatedBytes))
            {
                return -1;
            }

            int index = stateSets.Count;
            indexes.Add(key, index);
            stateSets.Add(nfaStates);
            previousContexts.Add(previousContext);
            acceptingStates.Add(accepting);
            transitionRows.Add(null);
            return index;
        }
    }

    private static int BuildPreviousContexts(
        RegexNfa nfa,
        byte[] byteContexts,
        byte[] representatives)
    {
        bool hasPredicates = false;
        for (int index = 0; index < nfa.States.Count; index++)
        {
            hasPredicates |= nfa.States[index].Kind == RegexNfaStateKind.Predicate;
        }

        int contextCount = hasPredicates ? 1 : 0;
        for (int value = 0; value < ByteCount; value++)
        {
            int context = hasPredicates ? 1 : 0;
            while (context < contextCount &&
                !ArePreviousBytesEquivalent(nfa, (byte)value, representatives[context]))
            {
                context++;
            }

            if (context == contextCount)
            {
                representatives[contextCount++] = (byte)value;
            }

            byteContexts[value] = (byte)context;
        }

        return contextCount;
    }

    private static bool ArePreviousBytesEquivalent(RegexNfa nfa, byte left, byte right)
    {
        for (int index = 0; index < nfa.States.Count; index++)
        {
            RegexNfaState state = nfa.States[index];
            if (state.Kind != RegexNfaStateKind.Predicate)
            {
                continue;
            }

            switch (state.AtomKind)
            {
                case RegexSyntaxKind.WordBoundary:
                case RegexSyntaxKind.NotWordBoundary:
                case RegexSyntaxKind.WordStartBoundary:
                case RegexSyntaxKind.WordEndBoundary:
                case RegexSyntaxKind.WordStartHalfBoundary:
                case RegexSyntaxKind.WordEndHalfBoundary:
                    if (IsAsciiWordByte(left) != IsAsciiWordByte(right))
                    {
                        return false;
                    }

                    break;
                case RegexSyntaxKind.StartAnchor when state.MultiLine && state.Crlf:
                    if ((left == (byte)'\r') != (right == (byte)'\r') ||
                        (left == (byte)'\n') != (right == (byte)'\n'))
                    {
                        return false;
                    }

                    break;
                case RegexSyntaxKind.StartAnchor when state.MultiLine:
                    if ((left == state.LineTerminator) != (right == state.LineTerminator))
                    {
                        return false;
                    }

                    break;
                case RegexSyntaxKind.EndAnchor when state.MultiLine && state.Crlf:
                    if ((left == (byte)'\r') != (right == (byte)'\r') ||
                        (left == (byte)'\n') != (right == (byte)'\n'))
                    {
                        return false;
                    }

                    break;
                case RegexSyntaxKind.EndAnchor when state.MultiLine:
                    if ((left == state.LineTerminator) != (right == state.LineTerminator))
                    {
                        return false;
                    }

                    break;
            }
        }

        return true;
    }

    private static int BuildByteClasses(
        RegexNfa nfa,
        byte[] previousContexts,
        byte[] byteClasses,
        byte[] representatives)
    {
        int classCount = 0;
        for (int value = 0; value < ByteCount; value++)
        {
            int byteClass = 0;
            while (byteClass < classCount &&
                (previousContexts[value] != previousContexts[representatives[byteClass]] ||
                    !AreConsumersEquivalent(nfa, (byte)value, representatives[byteClass])))
            {
                byteClass++;
            }

            if (byteClass == classCount)
            {
                representatives[classCount++] = (byte)value;
            }

            byteClasses[value] = (byte)byteClass;
        }

        return classCount;
    }

    private static bool AreConsumersEquivalent(RegexNfa nfa, byte left, byte right)
    {
        for (int stateIndex = 0; stateIndex < nfa.States.Count; stateIndex++)
        {
            RegexNfaState state = nfa.States[stateIndex];
            if (state.Kind == RegexNfaStateKind.Atom)
            {
                if (state.AtomMatches(left) != state.AtomMatches(right))
                {
                    return false;
                }

                continue;
            }

            if (state.Kind != RegexNfaStateKind.Sparse)
            {
                continue;
            }

            bool leftMatched = state.TryGetSparseTarget(left, out int leftTarget);
            bool rightMatched = state.TryGetSparseTarget(right, out int rightTarget);
            if (leftMatched != rightMatched || leftMatched && leftTarget != rightTarget)
            {
                return false;
            }
        }

        return true;
    }

    private static void ResolveContextualClosure(
        RegexNfa nfa,
        int[] roots,
        byte previousContext,
        int current,
        byte[] contextRepresentatives,
        out int[] consumers,
        out bool accepting)
    {
        var threads = new List<int>();
        bool[] visited = new bool[nfa.States.Count];
        bool[] closedSplits = new bool[nfa.States.Count];
        accepting = false;
        for (int index = 0; index < roots.Length; index++)
        {
            if (AddContextualThreadLeftmost(
                nfa,
                roots[index],
                previousContext,
                current,
                contextRepresentatives,
                threads,
                visited,
                closedSplits))
            {
                accepting = true;
                break;
            }
        }

        consumers = threads.ToArray();
    }

    private static bool AddContextualThreadLeftmost(
        RegexNfa nfa,
        int stateIndex,
        byte previousContext,
        int current,
        byte[] contextRepresentatives,
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
            return AddClosedSplitExitLeftmost(
                nfa,
                stateIndex,
                previousContext,
                current,
                contextRepresentatives,
                threads,
                visited,
                closedSplits);
        }

        visited[stateIndex] = true;
        RegexNfaState state = nfa.States[stateIndex];
        switch (state.Kind)
        {
            case RegexNfaStateKind.Accept:
                return true;
            case RegexNfaStateKind.Split:
            case RegexNfaStateKind.GreedyLoopSplit:
            case RegexNfaStateKind.LazyLoopSplit:
                return AddContextualThreadLeftmost(
                        nfa,
                        state.Next,
                        previousContext,
                        current,
                        contextRepresentatives,
                        threads,
                        visited,
                        closedSplits) ||
                    AddContextualThreadLeftmost(
                        nfa,
                        state.Alternative,
                        previousContext,
                        current,
                        contextRepresentatives,
                        threads,
                        visited,
                        closedSplits);
            case RegexNfaStateKind.Predicate:
                return PredicateMatches(
                        state,
                        previousContext,
                        current,
                        contextRepresentatives) &&
                    AddContextualThreadLeftmost(
                        nfa,
                        state.Next,
                        previousContext,
                        current,
                        contextRepresentatives,
                        threads,
                        visited,
                        closedSplits);
            case RegexNfaStateKind.CaptureStart:
            case RegexNfaStateKind.CaptureEnd:
                return AddContextualThreadLeftmost(
                    nfa,
                    state.Next,
                    previousContext,
                    current,
                    contextRepresentatives,
                    threads,
                    visited,
                    closedSplits);
            default:
                threads.Add(stateIndex);
                return false;
        }
    }

    private static bool AddClosedSplitExitLeftmost(
        RegexNfa nfa,
        int stateIndex,
        byte previousContext,
        int current,
        byte[] contextRepresentatives,
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
            RegexNfaStateKind.GreedyLoopSplit => AddContextualThreadLeftmost(
                nfa,
                state.Alternative,
                previousContext,
                current,
                contextRepresentatives,
                threads,
                visited,
                closedSplits),
            RegexNfaStateKind.LazyLoopSplit => AddContextualThreadLeftmost(
                nfa,
                state.Next,
                previousContext,
                current,
                contextRepresentatives,
                threads,
                visited,
                closedSplits),
            _ => false,
        };
    }

    private static bool PredicateMatches(
        RegexNfaState state,
        byte previousContext,
        int current,
        byte[] contextRepresentatives)
    {
        Span<byte> context = stackalloc byte[2];
        if (previousContext == StartContext)
        {
            if (current == EndOfInput)
            {
                return PredicateMatches(state, ReadOnlySpan<byte>.Empty, position: 0);
            }

            context[0] = (byte)current;
            return PredicateMatches(state, context[..1], position: 0);
        }

        context[0] = contextRepresentatives[previousContext];
        if (current == EndOfInput)
        {
            return PredicateMatches(state, context[..1], position: 1);
        }

        context[1] = (byte)current;
        return PredicateMatches(state, context, position: 1);
    }

    private static bool PredicateMatches(
        RegexNfaState state,
        ReadOnlySpan<byte> haystack,
        int position)
    {
        return RegexByteClass.PredicateMatches(
            haystack,
            position,
            state.AtomKind,
            state.MultiLine,
            state.Crlf,
            state.LineTerminator,
            utf8: false,
            unicodeClasses: false);
    }

    private static int[] MoveWithoutClosure(RegexNfa nfa, int[] consumers, byte value)
    {
        var next = new List<int>();
        bool[] visited = new bool[nfa.States.Count];
        for (int index = 0; index < consumers.Length; index++)
        {
            RegexNfaState state = nfa.States[consumers[index]];
            int successor = -1;
            if (state.Kind == RegexNfaStateKind.Atom && state.AtomMatches(value))
            {
                successor = state.Next;
            }
            else if (state.Kind == RegexNfaStateKind.Sparse &&
                state.TryGetSparseTarget(value, out int sparseNext))
            {
                successor = sparseNext;
            }

            if (successor >= 0 && !visited[successor])
            {
                visited[successor] = true;
                next.Add(successor);
            }
        }

        return next.ToArray();
    }

    private static bool IsSupportedPredicate(RegexSyntaxKind kind)
    {
        return kind is RegexSyntaxKind.StartAnchor
            or RegexSyntaxKind.EndAnchor
            or RegexSyntaxKind.AbsoluteStartAnchor
            or RegexSyntaxKind.AbsoluteEndAnchor
            or RegexSyntaxKind.WordBoundary
            or RegexSyntaxKind.NotWordBoundary
            or RegexSyntaxKind.WordStartBoundary
            or RegexSyntaxKind.WordEndBoundary
            or RegexSyntaxKind.WordStartHalfBoundary
            or RegexSyntaxKind.WordEndHalfBoundary;
    }

    private static bool IsAsciiWordByte(byte value)
    {
        return value == (byte)'_' ||
            value is >= (byte)'0' and <= (byte)'9'
                or >= (byte)'A' and <= (byte)'Z'
                or >= (byte)'a' and <= (byte)'z';
    }

    /// <summary>
    /// Attempts to find the end of the next leftmost match without reconstructing its start.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <param name="end">Receives the exclusive match end.</param>
    /// <returns><see langword="true" /> when a match end is found.</returns>
    internal bool TryFindEnd(ReadOnlySpan<byte> haystack, int startAt, out int end)
    {
        int position = Math.Clamp(startAt, 0, haystack.Length);
        byte previousContext = position == 0
            ? StartContext
            : _byteContexts[haystack[position - 1]];
        int current = _startStates[previousContext];
        int maximumSpecialState = _maximumSpecialState;
        int lastAcceptEnd = -1;
        ref int transitions = ref MemoryMarshal.GetArrayDataReference(_transitions);
        ref byte input = ref MemoryMarshal.GetReference(haystack);
        int unrolledEnd = haystack.Length - 4;
        while (position <= unrolledEnd)
        {
            current = Unsafe.Add(ref transitions, current + Unsafe.Add(ref input, position));
            position++;
            if ((uint)current <= (uint)maximumSpecialState)
            {
                if (current == 0)
                {
                    goto Complete;
                }

                lastAcceptEnd = position - 1;
                continue;
            }

            current = Unsafe.Add(ref transitions, current + Unsafe.Add(ref input, position));
            position++;
            if ((uint)current <= (uint)maximumSpecialState)
            {
                if (current == 0)
                {
                    goto Complete;
                }

                lastAcceptEnd = position - 1;
                continue;
            }

            current = Unsafe.Add(ref transitions, current + Unsafe.Add(ref input, position));
            position++;
            if ((uint)current <= (uint)maximumSpecialState)
            {
                if (current == 0)
                {
                    goto Complete;
                }

                lastAcceptEnd = position - 1;
                continue;
            }

            current = Unsafe.Add(ref transitions, current + Unsafe.Add(ref input, position));
            position++;
            if ((uint)current <= (uint)maximumSpecialState)
            {
                if (current == 0)
                {
                    goto Complete;
                }

                lastAcceptEnd = position - 1;
            }
        }

        while (position < haystack.Length)
        {
            current = Unsafe.Add(ref transitions, current + Unsafe.Add(ref input, position));
            position++;
            if ((uint)current <= (uint)maximumSpecialState)
            {
                if (current == 0)
                {
                    goto Complete;
                }

                lastAcceptEnd = position - 1;
            }
        }

        current = Unsafe.Add(ref transitions, current + EndOfInput);
        if (current != 0 && (uint)current <= (uint)maximumSpecialState)
        {
            lastAcceptEnd = haystack.Length;
        }

    Complete:
        end = lastAcceptEnd;
        return lastAcceptEnd >= 0;
    }

    /// <summary>
    /// Counts non-overlapping matches without reconstructing their starts.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <returns>The number of non-overlapping matches.</returns>
    internal long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        long count = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (offset < haystack.Length && TryFindEnd(haystack, offset, out int end))
        {
            System.Diagnostics.Debug.Assert(end > offset);
            count++;
            offset = end;
        }

        return count;
    }
}
