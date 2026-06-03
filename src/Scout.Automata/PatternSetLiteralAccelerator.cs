
namespace Scout;

internal sealed class PatternSetLiteralAccelerator
{
    private readonly AhoCorasickAutomaton automaton;
    private readonly int[] patternIds;

    public PatternSetLiteralAccelerator(IReadOnlyList<byte[]> patterns, IReadOnlyList<int> patternIds)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(patternIds);
        if (patterns.Count != patternIds.Count)
        {
            throw new ArgumentException("literal pattern and identifier counts must match", nameof(patternIds));
        }

        this.patternIds = new int[patternIds.Count];
        for (int index = 0; index < patternIds.Count; index++)
        {
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
