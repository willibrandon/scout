namespace Scout;

internal sealed class PatternSetBoundaryLiteralAccelerator
{
    private readonly AhoCorasickAutomaton automaton;
    private readonly byte[][] patterns;
    private readonly int[] patternIds;
    private readonly int[][] patternsByFirstByte;
    private readonly RegexCompileOptions options;

    public PatternSetBoundaryLiteralAccelerator(
        IReadOnlyList<byte[]> patterns,
        IReadOnlyList<int> patternIds,
        RegexCompileOptions options)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(patternIds);
        if (patterns.Count != patternIds.Count)
        {
            throw new ArgumentException("boundary literal pattern and identifier counts must match", nameof(patternIds));
        }

        this.patterns = new byte[patterns.Count][];
        this.patternIds = new int[patternIds.Count];
        for (int index = 0; index < patternIds.Count; index++)
        {
            this.patterns[index] = patterns[index].ToArray();
            this.patternIds[index] = patternIds[index];
        }

        this.options = options;
        patternsByFirstByte = BuildPatternsByFirstByte(this.patterns);
        automaton = AhoCorasickAutomaton.Create(patterns, AhoCorasickMatchKind.Standard, asciiCaseInsensitive: false);
    }

    public PatternSetMatch? Find(ReadOnlySpan<byte> haystack)
    {
        PatternSetMatch? best = null;
        AhoCorasickOverlappingEnumerator matches = automaton.EnumerateOverlapping(haystack);
        while (matches.MoveNext())
        {
            AhoCorasickMatch match = matches.Current;
            if (!HasWordBoundaries(haystack, match.Start, match.Length))
            {
                continue;
            }

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
        if (startAt >= haystack.Length)
        {
            return null;
        }

        ReadOnlySpan<int> candidates = patternsByFirstByte[haystack[startAt]];
        for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
        {
            int index = candidates[candidateIndex];
            byte[] pattern = patterns[index];
            if (pattern.Length <= haystack.Length - startAt &&
                haystack.Slice(startAt, pattern.Length).SequenceEqual(pattern) &&
                HasWordBoundaries(haystack, startAt, pattern.Length))
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
        return Find(haystack).HasValue;
    }

    public void MarkMatchingPatternIds(ReadOnlySpan<byte> haystack, bool[] matches)
    {
        AhoCorasickOverlappingEnumerator literals = automaton.EnumerateOverlapping(haystack);
        while (literals.MoveNext())
        {
            AhoCorasickMatch literal = literals.Current;
            if (HasWordBoundaries(haystack, literal.Start, literal.Length))
            {
                matches[patternIds[literal.PatternId]] = true;
            }
        }
    }

    private bool HasWordBoundaries(ReadOnlySpan<byte> haystack, int start, int length)
    {
        int end = start + length;
        if (length <= 0 ||
            start < 0 ||
            end > haystack.Length ||
            !IsAsciiWord(haystack[start]) ||
            !IsAsciiWord(haystack[end - 1]))
        {
            return false;
        }

        if (!options.Utf8 && !options.UnicodeClasses)
        {
            return (start == 0 || !IsAsciiWord(haystack[start - 1])) &&
                (end == haystack.Length || !IsAsciiWord(haystack[end]));
        }

        if (start > 0 &&
            haystack[start - 1] <= 0x7F &&
            IsAsciiWord(haystack[start - 1]))
        {
            return false;
        }

        if (end < haystack.Length &&
            haystack[end] <= 0x7F &&
            IsAsciiWord(haystack[end]))
        {
            return false;
        }

        return RegexByteClass.PredicateMatches(
                haystack,
                start,
                RegexSyntaxKind.WordBoundary,
                options.MultiLine,
                options.Crlf,
                options.LineTerminator,
                options.Utf8,
                options.UnicodeClasses) &&
            RegexByteClass.PredicateMatches(
                haystack,
                end,
                RegexSyntaxKind.WordBoundary,
                options.MultiLine,
                options.Crlf,
                options.LineTerminator,
                options.Utf8,
                options.UnicodeClasses);
    }

    private static bool IsAsciiWord(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z'
            or >= (byte)'0' and <= (byte)'9'
            or (byte)'_';
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
