namespace Scout;

/// <summary>
/// Accelerates an ordered set of exact literal patterns while preserving pattern identity.
/// </summary>
internal sealed class PatternSetLiteralAccelerator
{
    private readonly AhoCorasickAutomaton automaton;
    private readonly RegexCommonPrefixLiteralSetScanner? _commonPrefixScanner;
    private readonly RegexLargeLiteralSetScanner? largeLiteralScanner;
    private readonly byte[][] patterns;
    private readonly int[] patternIds;
    private readonly int[] allPatternIndexes;
    private readonly int[][]? patternsByFirstByte;

    /// <summary>
    /// Initializes an accelerator for ordered literal patterns and their source identifiers.
    /// </summary>
    /// <param name="patterns">The ordered literal patterns.</param>
    /// <param name="patternIds">The source identifier for each pattern.</param>
    /// <param name="asciiCaseInsensitive">Whether matching folds ASCII case.</param>
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
        if (!asciiCaseInsensitive)
        {
            RegexCommonPrefixLiteralSetScanner.TryCreate(
                this.patterns,
                out _commonPrefixScanner,
                takeLiteralOwnership: true);
            if (_commonPrefixScanner is null)
            {
                RegexLargeLiteralSetScanner.TryCreate(this.patterns, out largeLiteralScanner);
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether searches use the compiler-proven common-prefix scanner.
    /// </summary>
    internal bool UsesCommonPrefixScanner => _commonPrefixScanner is not null;

    /// <summary>
    /// Finds the leftmost match, breaking equal-start ties by source pattern order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <returns>The selected pattern match, or <see langword="null" />.</returns>
    public PatternSetMatch? Find(ReadOnlySpan<byte> haystack)
    {
        if (_commonPrefixScanner is not null)
        {
            RegexLiteralSetCandidate? candidate = _commonPrefixScanner.Find(haystack, startAt: 0);
            return candidate.HasValue
                ? new PatternSetMatch(patternIds[candidate.Value.LiteralId], candidate.Value.Match)
                : null;
        }

        if (largeLiteralScanner is not null)
        {
            RegexLiteralSetCandidate? candidate = largeLiteralScanner.Find(haystack, startAt: 0);
            return candidate.HasValue
                ? new PatternSetMatch(patternIds[candidate.Value.LiteralId], candidate.Value.Match)
                : null;
        }

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

    /// <summary>
    /// Finds the first ordered literal that matches at an exact byte offset.
    /// </summary>
    /// <param name="haystack">The bytes to inspect.</param>
    /// <param name="startAt">The exact match start.</param>
    /// <returns>The selected pattern match, or <see langword="null" />.</returns>
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

    /// <summary>
    /// Determines whether any literal matches the supplied bytes.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <returns><see langword="true" /> when a literal matches.</returns>
    public bool IsMatch(ReadOnlySpan<byte> haystack)
    {
        if (_commonPrefixScanner is not null)
        {
            return _commonPrefixScanner.Find(haystack, startAt: 0).HasValue;
        }

        if (largeLiteralScanner is not null)
        {
            return largeLiteralScanner.Find(haystack, startAt: 0).HasValue;
        }

        return automaton.Find(haystack).HasValue;
    }

    /// <summary>
    /// Counts ordered non-overlapping matches at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <returns>The number of matches.</returns>
    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (_commonPrefixScanner is not null)
        {
            return _commonPrefixScanner.CountMatches(haystack, startAt);
        }

        if (largeLiteralScanner is not null)
        {
            return largeLiteralScanner.CountMatches(haystack, startAt);
        }

        return CountOrSumMatches(haystack, startAt, sumSpans: false);
    }

    /// <summary>
    /// Attempts to count ordered non-overlapping matches while the common-prefix scan detects NUL bytes.
    /// </summary>
    /// <param name="haystack">The complete bytes to search.</param>
    /// <param name="count">Receives the non-overlapping match count.</param>
    /// <param name="containsNul">Receives whether the complete haystack contains a NUL byte.</param>
    /// <returns><see langword="true" /> when one common-prefix scan produced both results.</returns>
    internal bool TryCountMatchesAndDetectNul(
        ReadOnlySpan<byte> haystack,
        out long count,
        out bool containsNul)
    {
        if (_commonPrefixScanner is not null)
        {
            return _commonPrefixScanner.TryCountMatchesAndDetectNul(
                haystack,
                out count,
                out containsNul);
        }

        count = 0;
        containsNul = false;
        return false;
    }

    /// <summary>
    /// Sums ordered non-overlapping match lengths at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <returns>The sum of match lengths.</returns>
    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (_commonPrefixScanner is not null)
        {
            return _commonPrefixScanner.SumMatchSpans(haystack, startAt);
        }

        if (largeLiteralScanner is not null)
        {
            return largeLiteralScanner.SumMatchSpans(haystack, startAt);
        }

        return CountOrSumMatches(haystack, startAt, sumSpans: true);
    }

    /// <summary>
    /// Marks every source pattern identifier that occurs in the supplied bytes.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="matches">The source-pattern flags to update.</param>
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
