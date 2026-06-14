namespace Scout;

internal sealed class RegexLargeLiteralTrieScanner
{
    private const int MinimumLiteralCount = 128;

    private readonly int[] rootTransitions;
    private readonly byte[][] transitionBytes;
    private readonly int[][] transitionTargets;
    private readonly int[] bestLiteralIds;
    private readonly int[] bestLiteralLengths;

    private RegexLargeLiteralTrieScanner(
        int[] rootTransitions,
        byte[][] transitionBytes,
        int[][] transitionTargets,
        int[] bestLiteralIds,
        int[] bestLiteralLengths)
    {
        this.rootTransitions = rootTransitions;
        this.transitionBytes = transitionBytes;
        this.transitionTargets = transitionTargets;
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

        bool hasSingleByteLiteral = false;
        var nodes = new List<RegexLargeLiteralTrieBuilderNode> { new() };
        for (int literalId = 0; literalId < literals.Count; literalId++)
        {
            byte[] literal = literals[literalId];
            if (literal.Length == 0)
            {
                return false;
            }

            hasSingleByteLiteral |= literal.Length == 1;
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

        if (!hasSingleByteLiteral)
        {
            return false;
        }

        int[] rootTransitions = new int[byte.MaxValue + 1];
        Array.Fill(rootTransitions, -1);
        byte[][] transitionBytes = new byte[nodes.Count][];
        int[][] transitionTargets = new int[nodes.Count][];
        int[] bestLiteralIds = new int[nodes.Count];
        int[] bestLiteralLengths = new int[nodes.Count];
        for (int state = 0; state < nodes.Count; state++)
        {
            RegexLargeLiteralTrieBuilderNode node = nodes[state];
            bestLiteralIds[state] = node.BestLiteralId;
            bestLiteralLengths[state] = node.BestLiteralLength;
            transitionBytes[state] = new byte[node.Transitions.Count];
            transitionTargets[state] = new int[node.Transitions.Count];

            int index = 0;
            foreach (KeyValuePair<byte, int> transition in node.Transitions)
            {
                transitionBytes[state][index] = transition.Key;
                transitionTargets[state][index] = transition.Value;
                if (state == 0)
                {
                    rootTransitions[transition.Key] = transition.Value;
                }

                index++;
            }
        }

        scanner = new RegexLargeLiteralTrieScanner(
            rootTransitions,
            transitionBytes,
            transitionTargets,
            bestLiteralIds,
            bestLiteralLengths);
        return true;
    }

    public RegexLiteralSetCandidate? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
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
        long total = 0;
        int start = Math.Clamp(startAt, 0, haystack.Length);
        while (start < haystack.Length)
        {
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

    private bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int literalId, out int length)
    {
        literalId = -1;
        length = 0;
        int state = rootTransitions[haystack[start]];
        if (state < 0)
        {
            return false;
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

    private int FindTransition(int state, byte value)
    {
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
