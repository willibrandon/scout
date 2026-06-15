namespace Scout;

internal sealed class PatternSetLiteralAccelerator
{
    private readonly AhoCorasickAutomaton automaton;
    private readonly byte[][] patterns;
    private readonly int[] patternIds;
    private readonly int[] allPatternIndexes;
    private readonly int[][]? patternsByFirstByte;

    public PatternSetLiteralAccelerator(IReadOnlyList<byte[]> patterns, IReadOnlyList<int> patternIds, bool asciiCaseInsensitive = false)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(patternIds);
        if (patterns.Count != patternIds.Count)
        {
            throw new ArgumentException("literal pattern and identifier counts must match", nameof(patternIds));
        }

        this.patterns = new byte[patterns.Count][];
        this.patternIds = new int[patternIds.Count];
        allPatternIndexes = new int[patternIds.Count];
        for (int index = 0; index < patternIds.Count; index++)
        {
            this.patterns[index] = patterns[index].ToArray();
            this.patternIds[index] = patternIds[index];
            allPatternIndexes[index] = index;
        }

        patternsByFirstByte = asciiCaseInsensitive ? null : BuildPatternsByFirstByte(this.patterns);
        automaton = AhoCorasickAutomaton.Create(patterns, AhoCorasickMatchKind.Standard, asciiCaseInsensitive);
    }

    public PatternSetMatch? Find(ReadOnlySpan<byte> haystack)
    {
        PatternSetMatch? best = null;
        AhoCorasickOverlappingEnumerator matches = automaton.EnumerateOverlapping(haystack);
        while (matches.MoveNext())
        {
            AhoCorasickMatch match = matches.Current;
            var candidate = new PatternSetMatch(
                patternIds[match.PatternId],
                new RegexMatch(match.Start, match.Length));
            if (PatternSet.IsBetter(candidate, best))
            {
                best = candidate;
            }
        }

        return best;
    }

    public PatternSetMatch? FindAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        ReadOnlySpan<int> candidates = GetExactCandidates(haystack, startAt);
        for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
        {
            int index = candidates[candidateIndex];
            byte[] pattern = patterns[index];
            if (pattern.Length <= haystack.Length - startAt &&
                haystack.Slice(startAt, pattern.Length).SequenceEqual(pattern))
            {
                return new PatternSetMatch(
                    patternIds[index],
                    new RegexMatch(startAt, pattern.Length));
            }
        }

        return null;
    }

    private ReadOnlySpan<int> GetExactCandidates(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (patternsByFirstByte is null)
        {
            return allPatternIndexes;
        }

        return startAt < haystack.Length
            ? patternsByFirstByte[haystack[startAt]]
            : [];
    }

    public bool IsMatch(ReadOnlySpan<byte> haystack)
    {
        return automaton.Find(haystack).HasValue;
    }

    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSumMatches(haystack, startAt, sumSpans: false);
    }

    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSumMatches(haystack, startAt, sumSpans: true);
    }

    public void MarkMatchingPatternIds(ReadOnlySpan<byte> haystack, bool[] matches)
    {
        AhoCorasickOverlappingEnumerator literals = automaton.EnumerateOverlapping(haystack);
        while (literals.MoveNext())
        {
            matches[patternIds[literals.Current.PatternId]] = true;
        }
    }

    private long CountOrSumMatches(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        var candidates = new List<PatternSetMatch>();
        AhoCorasickOverlappingEnumerator matches = automaton.EnumerateOverlapping(haystack[startAt..]);
        while (matches.MoveNext())
        {
            AhoCorasickMatch match = matches.Current;
            AddCandidate(
                candidates,
                new PatternSetMatch(
                    patternIds[match.PatternId],
                    new RegexMatch(startAt + match.Start, match.Length)));
        }

        candidates.Sort(static (left, right) =>
        {
            int startComparison = left.Match.Start.CompareTo(right.Match.Start);
            return startComparison != 0
                ? startComparison
                : left.PatternId.CompareTo(right.PatternId);
        });

        long total = 0;
        int nextAllowedStart = startAt;
        for (int index = 0; index < candidates.Count; index++)
        {
            RegexMatch match = candidates[index].Match;
            if (match.Start < nextAllowedStart)
            {
                continue;
            }

            total += sumSpans ? match.Length : 1;
            nextAllowedStart = match.End;
        }

        return total;
    }

    private static void AddCandidate(List<PatternSetMatch> candidates, PatternSetMatch candidate)
    {
        for (int index = 0; index < candidates.Count; index++)
        {
            PatternSetMatch existing = candidates[index];
            if (existing.PatternId == candidate.PatternId &&
                existing.Match.Equals(candidate.Match))
            {
                return;
            }
        }

        candidates.Add(candidate);
    }

    private static int[][] BuildPatternsByFirstByte(byte[][] patterns)
    {
        var buckets = new List<int>[256];
        for (int index = 0; index < buckets.Length; index++)
        {
            buckets[index] = [];
        }

        for (int index = 0; index < patterns.Length; index++)
        {
            buckets[patterns[index][0]].Add(index);
        }

        int[][] indexed = new int[256][];
        for (int index = 0; index < indexed.Length; index++)
        {
            indexed[index] = buckets[index].ToArray();
        }

        return indexed;
    }
}
