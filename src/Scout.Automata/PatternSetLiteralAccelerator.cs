namespace Scout;

internal sealed class PatternSetLiteralAccelerator
{
    private readonly AhoCorasickAutomaton automaton;
    private readonly byte[][] patterns;
    private readonly int[] patternIds;

    public PatternSetLiteralAccelerator(IReadOnlyList<byte[]> patterns, IReadOnlyList<int> patternIds)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(patternIds);
        if (patterns.Count != patternIds.Count)
        {
            throw new ArgumentException("literal pattern and identifier counts must match", nameof(patternIds));
        }

        this.patterns = new byte[patterns.Count][];
        this.patternIds = new int[patternIds.Count];
        for (int index = 0; index < patternIds.Count; index++)
        {
            this.patterns[index] = patterns[index].ToArray();
            this.patternIds[index] = patternIds[index];
        }

        automaton = AhoCorasickAutomaton.Create(patterns);
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
        for (int index = 0; index < patterns.Length; index++)
        {
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

    public bool IsMatch(ReadOnlySpan<byte> haystack)
    {
        return automaton.Find(haystack).HasValue;
    }

    public void MarkMatchingPatternIds(ReadOnlySpan<byte> haystack, bool[] matches)
    {
        AhoCorasickOverlappingEnumerator literals = automaton.EnumerateOverlapping(haystack);
        while (literals.MoveNext())
        {
            matches[patternIds[literals.Current.PatternId]] = true;
        }
    }
}
