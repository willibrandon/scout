using System;
using System.Collections.Generic;

namespace Scout;

/// <summary>
/// Represents an ordered multi-regex set.
/// </summary>
public sealed class PatternSet
{
    private readonly RegexAutomaton[] automata;

    private PatternSet(RegexAutomaton[] automata)
    {
        this.automata = automata;
    }

    /// <summary>
    /// Gets the number of patterns in the set.
    /// </summary>
    public int Count => automata.Length;

    /// <summary>
    /// Compiles an ordered set of byte regex patterns.
    /// </summary>
    /// <param name="patterns">The ordered pattern bytes.</param>
    /// <returns>The compiled set.</returns>
    public static PatternSet Compile(IReadOnlyList<byte[]> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        var automata = new RegexAutomaton[patterns.Count];
        for (int index = 0; index < patterns.Count; index++)
        {
            byte[] pattern = patterns[index] ?? throw new ArgumentNullException(nameof(patterns));
            automata[index] = RegexAutomaton.Compile(pattern);
        }

        return new PatternSet(automata);
    }

    /// <summary>
    /// Returns a value indicating whether any pattern matches a haystack.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns><see langword="true" /> when at least one pattern matches.</returns>
    public bool IsMatch(ReadOnlySpan<byte> haystack)
    {
        for (int index = 0; index < automata.Length; index++)
        {
            if (automata[index].IsMatch(haystack))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the leftmost match across all patterns, using pattern order to break ties.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns>The selected match, or <see langword="null" /> when no pattern matches.</returns>
    public PatternSetMatch? Find(ReadOnlySpan<byte> haystack)
    {
        PatternSetMatch? best = null;
        for (int index = 0; index < automata.Length; index++)
        {
            RegexMatch? match = automata[index].Find(haystack);
            if (!match.HasValue)
            {
                continue;
            }

            var candidate = new PatternSetMatch(index, match.Value);
            if (IsBetter(candidate, best))
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Returns pattern identifiers whose regex matches the haystack.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns>Matching pattern identifiers in insertion order.</returns>
    public IReadOnlyList<int> MatchingPatternIds(ReadOnlySpan<byte> haystack)
    {
        var patternIds = new List<int>();
        for (int index = 0; index < automata.Length; index++)
        {
            if (automata[index].IsMatch(haystack))
            {
                patternIds.Add(index);
            }
        }

        return patternIds;
    }

    private static bool IsBetter(PatternSetMatch candidate, PatternSetMatch? best)
    {
        if (!best.HasValue)
        {
            return true;
        }

        PatternSetMatch current = best.Value;
        if (candidate.Match.Start != current.Match.Start)
        {
            return candidate.Match.Start < current.Match.Start;
        }

        return candidate.PatternId < current.PatternId;
    }
}
