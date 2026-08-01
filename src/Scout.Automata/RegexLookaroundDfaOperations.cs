namespace Scout;

/// <summary>
/// Provides shared delayed-match determinization operations for byte-safe look-around.
/// </summary>
internal static class RegexLookaroundDfaOperations
{
    internal const int EndOfInput = 256;
    internal const byte StartContext = 0;

    /// <summary>
    /// Determines whether every NFA state supports contextual byte determinization.
    /// </summary>
    internal static bool CanCompile(RegexNfa nfa)
    {
        ArgumentNullException.ThrowIfNull(nfa);
        for (int index = 0; index < nfa.States.Count; index++)
        {
            RegexNfaState state = nfa.States[index];
            if (state.Kind == RegexNfaStateKind.Predicate)
            {
                if (!IsSupportedPredicate(state.AtomKind) ||
                    IsWordPredicate(state.AtomKind) && (state.Utf8 || state.UnicodeClasses))
                {
                    return false;
                }

                continue;
            }

            if (state.RequiresUtf8ScalarMatch)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reports whether an NFA contains a byte-oriented word assertion.
    /// </summary>
    internal static bool ContainsWordPredicate(RegexNfa nfa)
    {
        ArgumentNullException.ThrowIfNull(nfa);
        for (int index = 0; index < nfa.States.Count; index++)
        {
            RegexNfaState state = nfa.States[index];
            if (state.Kind == RegexNfaStateKind.Predicate && IsWordPredicate(state.AtomKind))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds equivalence classes for the byte preceding the next transition.
    /// </summary>
    internal static int BuildPreviousContexts(
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
        for (int value = 0; value <= byte.MaxValue; value++)
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

    /// <summary>
    /// Builds byte equivalence classes that preserve consumers and look-around context.
    /// </summary>
    internal static int BuildByteClasses(
        RegexNfa nfa,
        byte[] previousContexts,
        byte[] byteClasses,
        byte[] representatives)
    {
        int classCount = 0;
        for (int value = 0; value <= byte.MaxValue; value++)
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

    /// <summary>
    /// Resolves an ordered epsilon closure against the current boundary context.
    /// </summary>
    internal static void ResolveContextualClosure(
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

    /// <summary>
    /// Moves contextual consumers through one byte without resolving the next closure.
    /// </summary>
    internal static int[] MoveWithoutClosure(RegexNfa nfa, int[] consumers, byte value)
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
                case RegexSyntaxKind.EndAnchor when state.MultiLine && state.Crlf:
                    if ((left == (byte)'\r') != (right == (byte)'\r') ||
                        (left == (byte)'\n') != (right == (byte)'\n'))
                    {
                        return false;
                    }

                    break;
                case RegexSyntaxKind.StartAnchor when state.MultiLine:
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
            unicodeClasses: false,
            matchInvalidUtf8: state.MatchInvalidUtf8);
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

    private static bool IsWordPredicate(RegexSyntaxKind kind)
    {
        return kind is RegexSyntaxKind.WordBoundary
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
}
