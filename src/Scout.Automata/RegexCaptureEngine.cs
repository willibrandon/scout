namespace Scout;

internal sealed class RegexCaptureEngine
{
    private readonly RegexNfa nfa;
    private readonly RegexPrefilter? prefilter;
    private List<CaptureThread> current = [];
    private List<CaptureThread> next = [];
    private HashSet<(int State, int Position)> currentSeen = [];
    private HashSet<(int State, int Position)> nextSeen = [];

    public RegexCaptureEngine(RegexNfa nfa, RegexPrefilter? prefilter)
    {
        this.nfa = nfa;
        this.prefilter = prefilter;
    }

    public RegexCaptures? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (prefilter?.UsesRequiredLiteralWindow == true)
        {
            int nextStartToTry = startOffset;
            for (int requiredAt = prefilter.FindRequiredLiteral(haystack, startOffset);
                 requiredAt >= 0;
                 requiredAt = prefilter.FindRequiredLiteral(haystack, requiredAt + 1))
            {
                int firstStart = Math.Max(startOffset, requiredAt - prefilter.RequiredLiteralWindow);
                firstStart = Math.Max(firstStart, nextStartToTry);
                for (int start = firstStart; start <= requiredAt; start++)
                {
                    if (nfa.Utf8 && !RegexByteClass.IsUtf8Boundary(haystack, start))
                    {
                        continue;
                    }

                    if (TryMatchAt(haystack, start, out RegexCaptures? captures))
                    {
                        return captures;
                    }
                }

                nextStartToTry = Math.Max(nextStartToTry, requiredAt + 1);
            }

            return null;
        }

        if (prefilter is not null)
        {
            for (int start = prefilter.FindCandidate(haystack, startOffset);
                 start >= 0;
                 start = prefilter.FindCandidate(haystack, start + 1))
            {
                if (nfa.Utf8 && !RegexByteClass.IsUtf8Boundary(haystack, start))
                {
                    continue;
                }

                if (TryMatchAt(haystack, start, out RegexCaptures? captures))
                {
                    return captures;
                }
            }

            return null;
        }

        for (int start = startOffset; start <= haystack.Length; start++)
        {
            if (nfa.Utf8 && !RegexByteClass.IsUtf8Boundary(haystack, start))
            {
                continue;
            }

            if (TryMatchAt(haystack, start, out RegexCaptures? captures))
            {
                return captures;
            }
        }

        return null;
    }

    internal RegexCaptures? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (nfa.Utf8 && !RegexByteClass.IsUtf8Boundary(haystack, startOffset))
        {
            return null;
        }

        return TryMatchAt(haystack, startOffset, out RegexCaptures? captures)
            ? captures
            : null;
    }

    private bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out RegexCaptures? captures)
    {
        int[] starts = CreateCaptureSlots(nfa.CaptureCount + 1);
        int[] ends = CreateCaptureSlots(nfa.CaptureCount + 1);
        starts[0] = start;

        current.Clear();
        currentSeen.Clear();
        AddThread(
            nfa.StartState,
            haystack,
            start,
            starts,
            ends,
            current,
            currentSeen,
            new bool[nfa.States.Count],
            new bool[nfa.States.Count]);

        CaptureThread? deferredAccept = null;
        Dictionary<(int State, int Position), bool> reachabilityCache = [];
        while (current.Count > 0)
        {
            int position = MinPosition(current);
            int acceptIndex = IndexOfAccept(current, position);
            if (acceptIndex >= 0)
            {
                CaptureThread accepted = current[acceptIndex].WithGroupEnd(0, position);
                deferredAccept = accepted;
                if (!HasEarlierConsumer(current, acceptIndex, haystack, position, reachabilityCache))
                {
                    captures = ToCaptures(accepted, start, position);
                    return true;
                }
            }

            next.Clear();
            nextSeen.Clear();
            for (int index = 0; index < current.Count; index++)
            {
                CaptureThread thread = current[index];
                if (thread.Position != position)
                {
                    AddThreadEntry(thread, next, nextSeen);
                    continue;
                }

                RegexNfaState state = nfa.States[thread.State];
                if (state.Kind == RegexNfaStateKind.Atom &&
                    RegexByteClass.TryGetAtomMatchLength(
                        haystack,
                        position,
                        state.AtomKind,
                        state.Value.Span,
                        state.CaseInsensitive,
                        state.MultiLine,
                        state.DotMatchesNewline,
                        state.Crlf,
                        state.LineTerminator,
                        state.Utf8,
                        state.UnicodeClasses,
                        out int consume))
                {
                    AddThread(
                        state.Next,
                        haystack,
                        position + consume,
                        CloneSlots(thread.Starts),
                        CloneSlots(thread.Ends),
                        next,
                        nextSeen,
                        new bool[nfa.States.Count],
                        new bool[nfa.States.Count]);
                }
                else if (position < haystack.Length &&
                    state.Kind == RegexNfaStateKind.Sparse &&
                    state.TryGetSparseTarget(haystack[position], out int sparseNext))
                {
                    AddThread(
                        sparseNext,
                        haystack,
                        position + 1,
                        CloneSlots(thread.Starts),
                        CloneSlots(thread.Ends),
                        next,
                        nextSeen,
                        new bool[nfa.States.Count],
                        new bool[nfa.States.Count]);
                }
            }

            (current, next) = (next, current);
            (currentSeen, nextSeen) = (nextSeen, currentSeen);
        }

        if (deferredAccept.HasValue)
        {
            CaptureThread accepted = deferredAccept.Value;
            captures = ToCaptures(accepted, start, accepted.Position);
            return true;
        }

        captures = null;
        return false;
    }

    private void AddThread(
        int stateIndex,
        ReadOnlySpan<byte> haystack,
        int position,
        int[] starts,
        int[] ends,
        List<CaptureThread> threads,
        HashSet<(int State, int Position)> seen,
        bool[] visited,
        bool[] closedSplits)
    {
        if (stateIndex < 0)
        {
            return;
        }

        if (visited[stateIndex])
        {
            AddClosedSplitExit(stateIndex, haystack, position, starts, ends, threads, seen, visited, closedSplits);
            return;
        }

        visited[stateIndex] = true;
        RegexNfaState state = nfa.States[stateIndex];
        switch (state.Kind)
        {
            case RegexNfaStateKind.Split:
            case RegexNfaStateKind.GreedyLoopSplit:
            case RegexNfaStateKind.LazyLoopSplit:
                AddThread(state.Next, haystack, position, CloneSlots(starts), CloneSlots(ends), threads, seen, visited, closedSplits);
                AddThread(state.Alternative, haystack, position, CloneSlots(starts), CloneSlots(ends), threads, seen, visited, closedSplits);
                break;
            case RegexNfaStateKind.CaptureStart:
                starts = CloneSlots(starts);
                starts[state.CaptureIndex] = position;
                ends = CloneSlots(ends);
                ends[state.CaptureIndex] = -1;
                AddThread(state.Next, haystack, position, starts, ends, threads, seen, visited, closedSplits);
                break;
            case RegexNfaStateKind.CaptureEnd:
                ends = CloneSlots(ends);
                ends[state.CaptureIndex] = position;
                AddThread(state.Next, haystack, position, starts, ends, threads, seen, visited, closedSplits);
                break;
            case RegexNfaStateKind.Predicate:
                if (RegexByteClass.PredicateMatches(haystack, position, state.AtomKind, state.MultiLine, state.Crlf, state.LineTerminator, state.Utf8, state.UnicodeClasses))
                {
                    AddThread(state.Next, haystack, position, starts, ends, threads, seen, visited, closedSplits);
                }

                break;
            default:
                AddThreadEntry(new CaptureThread(stateIndex, position, starts, ends), threads, seen);
                break;
        }
    }

    private void AddClosedSplitExit(
        int stateIndex,
        ReadOnlySpan<byte> haystack,
        int position,
        int[] starts,
        int[] ends,
        List<CaptureThread> threads,
        HashSet<(int State, int Position)> seen,
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
                AddThread(state.Alternative, haystack, position, starts, ends, threads, seen, visited, closedSplits);
                break;
            case RegexNfaStateKind.LazyLoopSplit:
                AddThread(state.Next, haystack, position, starts, ends, threads, seen, visited, closedSplits);
                break;
        }
    }

    private static void AddThreadEntry(
        CaptureThread thread,
        List<CaptureThread> threads,
        HashSet<(int State, int Position)> seen)
    {
        if (seen.Add((thread.State, thread.Position)))
        {
            threads.Add(thread);
        }
    }

    private bool HasEarlierConsumer(
        List<CaptureThread> threads,
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
            CaptureThread thread = threads[index];
            if (thread.Position != position)
            {
                continue;
            }

            RegexNfaState state = nfa.States[thread.State];
            if (state.Kind == RegexNfaStateKind.Atom &&
                RegexByteClass.TryGetAtomMatchLength(
                    haystack,
                    position,
                    state.AtomKind,
                    state.Value.Span,
                    state.CaseInsensitive,
                    state.MultiLine,
                    state.DotMatchesNewline,
                    state.Crlf,
                    state.LineTerminator,
                    state.Utf8,
                    state.UnicodeClasses,
                    out int consume) &&
                RegexDfaOperations.CanReachAccept(nfa, state.Next, haystack, position + consume, reachabilityCache))
            {
                return true;
            }

            if (state.Kind == RegexNfaStateKind.Sparse &&
                state.TryGetSparseTarget(haystack[position], out int sparseNext) &&
                RegexDfaOperations.CanReachAccept(nfa, sparseNext, haystack, position + 1, reachabilityCache))
            {
                return true;
            }
        }

        return false;
    }

    private int IndexOfAccept(List<CaptureThread> threads, int position)
    {
        for (int index = 0; index < threads.Count; index++)
        {
            CaptureThread thread = threads[index];
            if (thread.Position == position && nfa.States[thread.State].Kind == RegexNfaStateKind.Accept)
            {
                return index;
            }
        }

        return -1;
    }

    private static int MinPosition(List<CaptureThread> threads)
    {
        int position = threads[0].Position;
        for (int index = 1; index < threads.Count; index++)
        {
            position = Math.Min(position, threads[index].Position);
        }

        return position;
    }

    private static RegexCaptures ToCaptures(CaptureThread accepted, int start, int end)
    {
        var match = new RegexMatch(start, end - start);
        var groups = new RegexMatch?[accepted.Starts.Length];
        groups[0] = match;
        for (int index = 1; index < groups.Length; index++)
        {
            int groupStart = accepted.Starts[index];
            int groupEnd = accepted.Ends[index];
            if (groupStart >= 0 && groupEnd >= groupStart)
            {
                groups[index] = new RegexMatch(groupStart, groupEnd - groupStart);
            }
        }

        return new RegexCaptures(match, groups);
    }

    private static int[] CreateCaptureSlots(int length)
    {
        int[] slots = new int[length];
        Array.Fill(slots, -1);
        return slots;
    }

    private static int[] CloneSlots(int[] slots)
    {
        return (int[])slots.Clone();
    }

}
