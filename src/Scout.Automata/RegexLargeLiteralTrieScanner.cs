namespace Scout;

internal sealed class RegexLargeLiteralTrieScanner
{
    private const int MinimumLiteralCount = 128;
    private const int DenseTransitionFanoutThreshold = 4;

    private readonly int[] rootTransitions;
    private readonly int[] rootImmediateLiteralIds;
    private readonly bool rootMatchesOnlyImmediateLiterals;
    private readonly byte[][] transitionBytes;
    private readonly int[][] transitionTargets;
    private readonly int[]?[] denseTransitionTargets;
    private readonly int[] bestLiteralIds;
    private readonly int[] bestLiteralLengths;

    private RegexLargeLiteralTrieScanner(
        int[] rootTransitions,
        int[] rootImmediateLiteralIds,
        byte[][] transitionBytes,
        int[][] transitionTargets,
        int[]?[] denseTransitionTargets,
        int[] bestLiteralIds,
        int[] bestLiteralLengths)
    {
        this.rootTransitions = rootTransitions;
        this.rootImmediateLiteralIds = rootImmediateLiteralIds;
        rootMatchesOnlyImmediateLiterals = RootMatchesOnlyImmediateLiterals(rootTransitions, rootImmediateLiteralIds);
        this.transitionBytes = transitionBytes;
        this.transitionTargets = transitionTargets;
        this.denseTransitionTargets = denseTransitionTargets;
        this.bestLiteralIds = bestLiteralIds;
        this.bestLiteralLengths = bestLiteralLengths;
    }

    public static bool TryCreate(IReadOnlyList<byte[]> literals, out RegexLargeLiteralTrieScanner? scanner)
    {
        scanner = null;
        if (literals.Count < MinimumLiteralCount)
        {
            return false;
        }

        for (int literalId = 0; literalId < literals.Count; literalId++)
        {
            byte[] literal = literals[literalId];
            if (literal.Length == 0)
            {
                return false;
            }

            if (literal.Length == 1)
            {
                return TryCreateTrie(literals, out scanner);
            }
        }

        return false;
    }

    private static bool TryCreateTrie(IReadOnlyList<byte[]> literals, out RegexLargeLiteralTrieScanner? scanner)
    {
        var nodes = new List<RegexLargeLiteralTrieBuilderNode> { new() };
        for (int literalId = 0; literalId < literals.Count; literalId++)
        {
            byte[] literal = literals[literalId];
            int state = 0;
            for (int index = 0; index < literal.Length; index++)
            {
                Dictionary<byte, int> transitions = nodes[state].Transitions;
                byte value = literal[index];
                if (!transitions.TryGetValue(value, out int next))
                {
                    next = nodes.Count;
                    transitions.Add(value, next);
                    nodes.Add(new RegexLargeLiteralTrieBuilderNode());
                }

                state = next;
            }

            RegexLargeLiteralTrieBuilderNode terminal = nodes[state];
            if (terminal.BestLiteralId < 0 || literalId < terminal.BestLiteralId)
            {
                terminal.BestLiteralId = literalId;
                terminal.BestLiteralLength = literal.Length;
            }
        }

        int[] rootTransitions = new int[byte.MaxValue + 1];
        Array.Fill(rootTransitions, -1);
        int[] rootImmediateLiteralIds = BuildRootImmediateLiteralIds(nodes);
        byte[][] transitionBytes = new byte[nodes.Count][];
        int[][] transitionTargets = new int[nodes.Count][];
        int[]?[] denseTransitionTargets = new int[nodes.Count][];
        int[] bestLiteralIds = new int[nodes.Count];
        int[] bestLiteralLengths = new int[nodes.Count];
        for (int state = 0; state < nodes.Count; state++)
        {
            RegexLargeLiteralTrieBuilderNode node = nodes[state];
            bestLiteralIds[state] = node.BestLiteralId;
            bestLiteralLengths[state] = node.BestLiteralLength;
            transitionBytes[state] = new byte[node.Transitions.Count];
            transitionTargets[state] = new int[node.Transitions.Count];
            int[]? denseTransitions = null;
            if (state != 0 && node.Transitions.Count >= DenseTransitionFanoutThreshold)
            {
                denseTransitions = new int[byte.MaxValue + 1];
                Array.Fill(denseTransitions, -1);
                denseTransitionTargets[state] = denseTransitions;
            }

            int index = 0;
            foreach (KeyValuePair<byte, int> transition in node.Transitions)
            {
                transitionBytes[state][index] = transition.Key;
                transitionTargets[state][index] = transition.Value;
                if (denseTransitions is not null)
                {
                    denseTransitions[transition.Key] = transition.Value;
                }

                if (state == 0)
                {
                    rootTransitions[transition.Key] = transition.Value;
                }

                index++;
            }
        }

        scanner = new RegexLargeLiteralTrieScanner(
            rootTransitions,
            rootImmediateLiteralIds,
            transitionBytes,
            transitionTargets,
            denseTransitionTargets,
            bestLiteralIds,
            bestLiteralLengths);
        return true;
    }

    public RegexLiteralSetCandidate? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (rootMatchesOnlyImmediateLiterals)
        {
            return FindRootImmediateLiteral(haystack, startAt);
        }

        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        for (int start = startOffset; start < haystack.Length; start++)
        {
            if (TryMatchAt(haystack, start, out int literalId, out int length))
            {
                return new RegexLiteralSetCandidate(literalId, new RegexMatch(start, length));
            }
        }

        return null;
    }

    public long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        if (rootMatchesOnlyImmediateLiterals)
        {
            return CountRootImmediateLiterals(haystack, startAt);
        }

        long total = 0;
        int start = Math.Clamp(startAt, 0, haystack.Length);
        while (start < haystack.Length)
        {
            byte value = haystack[start];
            if (rootImmediateLiteralIds[value] >= 0)
            {
                total++;
                start++;
                continue;
            }

            if (rootTransitions[value] < 0)
            {
                start++;
                continue;
            }

            if (!TryMatchAt(haystack, start, out _, out int length))
            {
                start++;
                continue;
            }

            total += sumSpans ? length : 1;
            start += length;
        }

        return total;
    }

    private RegexLiteralSetCandidate? FindRootImmediateLiteral(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        for (int start = startOffset; start < haystack.Length; start++)
        {
            byte value = haystack[start];
            int literalId = rootImmediateLiteralIds[value];
            if (literalId >= 0)
            {
                return new RegexLiteralSetCandidate(literalId, new RegexMatch(start, 1));
            }
        }

        return null;
    }

    private long CountRootImmediateLiterals(ReadOnlySpan<byte> haystack, int startAt)
    {
        long total = 0;
        int start = Math.Clamp(startAt, 0, haystack.Length);
        for (int index = start; index < haystack.Length; index++)
        {
            if (rootImmediateLiteralIds[haystack[index]] >= 0)
            {
                total++;
            }
        }

        return total;
    }

    private bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int literalId, out int length)
    {
        literalId = -1;
        length = 0;
        int state = rootTransitions[haystack[start]];
        if (state < 0)
        {
            return false;
        }

        int immediateLiteralId = rootImmediateLiteralIds[haystack[start]];
        if (immediateLiteralId >= 0)
        {
            literalId = immediateLiteralId;
            length = bestLiteralLengths[state];
            return true;
        }

        int index = start;
        while (state >= 0)
        {
            int candidateId = bestLiteralIds[state];
            if (candidateId >= 0 && (literalId < 0 || candidateId < literalId))
            {
                literalId = candidateId;
                length = bestLiteralLengths[state];
            }

            index++;
            if (index >= haystack.Length)
            {
                break;
            }

            state = FindTransition(state, haystack[index]);
        }

        return literalId >= 0;
    }

    private static bool RootMatchesOnlyImmediateLiterals(int[] rootTransitions, int[] rootImmediateLiteralIds)
    {
        for (int value = 0; value < rootTransitions.Length; value++)
        {
            if (rootTransitions[value] >= 0 && rootImmediateLiteralIds[value] < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static int[] BuildRootImmediateLiteralIds(List<RegexLargeLiteralTrieBuilderNode> nodes)
    {
        int[] subtreeBestLiteralIds = new int[nodes.Count];
        Array.Fill(subtreeBestLiteralIds, -2);
        int[] rootImmediateLiteralIds = new int[byte.MaxValue + 1];
        Array.Fill(rootImmediateLiteralIds, -1);

        foreach (KeyValuePair<byte, int> transition in nodes[0].Transitions)
        {
            int state = transition.Value;
            int terminalLiteralId = nodes[state].BestLiteralId;
            if (terminalLiteralId >= 0 && terminalLiteralId == SubtreeBestLiteralId(nodes, subtreeBestLiteralIds, state))
            {
                rootImmediateLiteralIds[transition.Key] = terminalLiteralId;
            }
        }

        return rootImmediateLiteralIds;
    }

    private static int SubtreeBestLiteralId(
        List<RegexLargeLiteralTrieBuilderNode> nodes,
        int[] subtreeBestLiteralIds,
        int state)
    {
        int cached = subtreeBestLiteralIds[state];
        if (cached != -2)
        {
            return cached;
        }

        int best = nodes[state].BestLiteralId;
        foreach (int target in nodes[state].Transitions.Values)
        {
            int childBest = SubtreeBestLiteralId(nodes, subtreeBestLiteralIds, target);
            if (childBest >= 0 && (best < 0 || childBest < best))
            {
                best = childBest;
            }
        }

        subtreeBestLiteralIds[state] = best;
        return best;
    }

    private int FindTransition(int state, byte value)
    {
        int[]? denseTransitions = denseTransitionTargets[state];
        if (denseTransitions is not null)
        {
            return denseTransitions[value];
        }

        byte[] bytes = transitionBytes[state];
        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == value)
            {
                return transitionTargets[state][index];
            }
        }

        return -1;
    }

}
