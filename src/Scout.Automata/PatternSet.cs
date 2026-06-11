namespace Scout;

/// <summary>
/// Represents an ordered multi-regex set.
/// </summary>
public sealed class PatternSet
{
    private readonly RegexAutomaton[] automata;
    private readonly int[] automataPatternIds;
    private readonly int[] allAutomataIndexes;
    private readonly int[][]? exactAutomataByFirstByte;
    private readonly PatternSetLiteralAccelerator? literalAccelerator;
    private readonly PatternSetRequiredLiteralAccelerator? requiredLiteralAccelerator;
    private readonly int count;

    private PatternSet(
        int count,
        RegexAutomaton[] automata,
        int[] automataPatternIds,
        int[] allAutomataIndexes,
        int[][]? exactAutomataByFirstByte,
        PatternSetLiteralAccelerator? literalAccelerator,
        PatternSetRequiredLiteralAccelerator? requiredLiteralAccelerator)
    {
        this.count = count;
        this.automata = automata;
        this.automataPatternIds = automataPatternIds;
        this.allAutomataIndexes = allAutomataIndexes;
        this.exactAutomataByFirstByte = exactAutomataByFirstByte;
        this.literalAccelerator = literalAccelerator;
        this.requiredLiteralAccelerator = requiredLiteralAccelerator;
    }

    /// <summary>
    /// Gets the number of patterns in the set.
    /// </summary>
    public int Count => count;

    internal bool UsesLiteralAccelerator => literalAccelerator is not null;

    internal bool UsesRequiredLiteralAccelerator => requiredLiteralAccelerator is not null;

    internal bool RequiredLiteralAcceleratorCoversAll => requiredLiteralAccelerator?.CoversAllAutomata == true;

    internal bool CanAccelerateEveryPattern => automata.Length == 0 || RequiredLiteralAcceleratorCoversAll;

    internal static bool CanPreflightAccelerateEveryPattern(IReadOnlyList<byte[]> patterns, RegexCompileOptions options)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        for (int index = 0; index < patterns.Count; index++)
        {
            byte[] pattern = patterns[index] ?? throw new ArgumentNullException(nameof(patterns));
            if (TryGetLiteralPattern(pattern, out byte[] literal) &&
                literal.Length != 0 &&
                TryPrepareLiteralPatterns(literal, options, out _))
            {
                continue;
            }

            RegexSyntaxTree tree = RegexSyntaxParser.Parse(pattern);
            if (RegexPrefilter.TryCollectRequiredLiteralSet(tree.Root, options, out byte[][] requiredLiterals) &&
                requiredLiterals.Length > 0 &&
                RegexPrefilter.TryPrepareRequiredLiteralSet(requiredLiterals, options, out _))
            {
                continue;
            }

            if (RegexPrefilter.TryFindRequiredLiteral(tree.Root, options, out byte[] requiredLiteral) &&
                requiredLiteral.Length >= 2 &&
                RegexPrefilter.TryPrepareRequiredLiteralSet([requiredLiteral], options, out _))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Compiles an ordered set of byte regex patterns.
    /// </summary>
    /// <param name="patterns">The ordered pattern bytes.</param>
    /// <returns>The compiled set.</returns>
    public static PatternSet Compile(IReadOnlyList<byte[]> patterns)
    {
        return Compile(
            patterns,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false);
    }

    /// <summary>
    /// Compiles an ordered set of byte regex patterns with root regex options.
    /// </summary>
    /// <param name="patterns">The ordered pattern bytes.</param>
    /// <param name="caseInsensitive">Whether literal and class atoms match ASCII case-insensitively.</param>
    /// <param name="multiLine">Whether <c>^</c> and <c>$</c> match adjacent to line feeds.</param>
    /// <param name="dotMatchesNewline">Whether <c>.</c> matches line feeds.</param>
    /// <param name="crlf">Whether CRLF mode treats carriage returns and line feeds as line terminators.</param>
    /// <param name="lineTerminator">The line terminator byte used when CRLF mode is disabled.</param>
    /// <param name="utf8">Whether empty and scalar-consuming matches must respect UTF-8 code point boundaries.</param>
    /// <param name="unicodeClasses">Whether Perl classes and word-boundary assertions use Unicode word definitions.</param>
    /// <returns>The compiled set.</returns>
    public static PatternSet Compile(
        IReadOnlyList<byte[]> patterns,
        bool caseInsensitive,
        bool multiLine,
        bool dotMatchesNewline,
        bool crlf = false,
        byte lineTerminator = (byte)'\n',
        bool utf8 = true,
        bool unicodeClasses = true)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        var automata = new List<RegexAutomaton>();
        var automataPatternIds = new List<int>();
        var literalPatterns = new List<byte[]>();
        var literalPatternIds = new List<int>();
        var requiredLiteralEntries = new List<PatternSetRequiredLiteralEntry>();
        var options = new RegexCompileOptions(caseInsensitive, swapGreed: false, multiLine, dotMatchesNewline, crlf, lineTerminator, utf8, unicodeClasses);
        for (int index = 0; index < patterns.Count; index++)
        {
            byte[] pattern = patterns[index] ?? throw new ArgumentNullException(nameof(patterns));
            if (TryGetLiteralPattern(pattern, out byte[] literal) &&
                literal.Length != 0 &&
                TryPrepareLiteralPatterns(literal, options, out byte[][] preparedLiteralPatterns))
            {
                for (int literalIndex = 0; literalIndex < preparedLiteralPatterns.Length; literalIndex++)
                {
                    literalPatterns.Add(preparedLiteralPatterns[literalIndex]);
                    literalPatternIds.Add(index);
                }

                continue;
            }

            RegexSyntaxTree tree = RegexSyntaxParser.Parse(pattern);
            if (RegexPrefilter.TryCollectRequiredLiteralSet(tree.Root, options, out byte[][] requiredLiterals) &&
                requiredLiterals.Length > 0 &&
                RegexPrefilter.TryPrepareRequiredLiteralSet(requiredLiterals, options, out byte[][] preparedLiterals))
            {
                int maxLookBehind = TryGetRequiredLiteralLookBehind(tree.Root, options, requiredLiterals);
                requiredLiteralEntries.Add(new PatternSetRequiredLiteralEntry(automata.Count, preparedLiterals, maxLookBehind));
            }
            else if (RegexPrefilter.TryFindRequiredLiteral(tree.Root, options, out byte[] requiredLiteral) &&
                requiredLiteral.Length >= 2 &&
                RegexPrefilter.TryPrepareRequiredLiteralSet([requiredLiteral], options, out preparedLiterals))
            {
                requiredLiteralEntries.Add(new PatternSetRequiredLiteralEntry(automata.Count, preparedLiterals));
            }

            automata.Add(RegexAutomaton.Compile(pattern, caseInsensitive, multiLine, dotMatchesNewline, crlf, lineTerminator, utf8, unicodeClasses));
            automataPatternIds.Add(index);
        }

        PatternSetLiteralAccelerator? literalAccelerator = literalPatterns.Count == 0
            ? null
            : new PatternSetLiteralAccelerator(literalPatterns, literalPatternIds, caseInsensitive);
        RegexAutomaton[] compiledAutomata = automata.ToArray();
        PatternSetRequiredLiteralAccelerator? requiredLiteralAccelerator = requiredLiteralEntries.Count == 0
            ? null
            : new PatternSetRequiredLiteralAccelerator(requiredLiteralEntries, compiledAutomata.Length, compiledAutomata);
        return new PatternSet(
            patterns.Count,
            compiledAutomata,
            automataPatternIds.ToArray(),
            BuildAllAutomataIndexes(compiledAutomata.Length),
            BuildExactAutomataByFirstByte(compiledAutomata),
            literalAccelerator,
            requiredLiteralAccelerator);
    }

    /// <summary>
    /// Returns a value indicating whether any pattern matches a haystack.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns><see langword="true" /> when at least one pattern matches.</returns>
    public bool IsMatch(ReadOnlySpan<byte> haystack)
    {
        if (literalAccelerator is not null && literalAccelerator.IsMatch(haystack))
        {
            return true;
        }

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
    /// Counts all non-overlapping matches in a haystack.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns>The number of non-overlapping matches.</returns>
    public long CountMatches(ReadOnlySpan<byte> haystack)
    {
        return CountMatches(haystack, startAt: 0);
    }

    /// <summary>
    /// Counts all non-overlapping matches in a haystack at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <param name="startAt">The first byte offset to consider.</param>
    /// <returns>The number of non-overlapping matches.</returns>
    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (literalAccelerator is not null && automata.Length == 0)
        {
            return literalAccelerator.CountMatches(haystack, startOffset);
        }

        if (literalAccelerator is null &&
            requiredLiteralAccelerator is not null &&
            requiredLiteralAccelerator.CoversAllAutomata)
        {
            return requiredLiteralAccelerator.CountMatches(haystack, startOffset, automata, automataPatternIds);
        }

        return IterateNonOverlapping(haystack, startAt, sumSpans: false);
    }

    /// <summary>
    /// Sums the byte lengths of all non-overlapping matches in a haystack.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns>The sum of non-overlapping match lengths.</returns>
    public long SumMatchSpans(ReadOnlySpan<byte> haystack)
    {
        return SumMatchSpans(haystack, startAt: 0);
    }

    /// <summary>
    /// Sums the byte lengths of all non-overlapping matches in a haystack at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <param name="startAt">The first byte offset to consider.</param>
    /// <returns>The sum of non-overlapping match lengths.</returns>
    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (literalAccelerator is not null && automata.Length == 0)
        {
            return literalAccelerator.SumMatchSpans(haystack, startOffset);
        }

        return IterateNonOverlapping(haystack, startAt, sumSpans: true);
    }

    /// <summary>
    /// Finds the leftmost match across all patterns, using pattern order to break ties.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns>The selected match, or <see langword="null" /> when no pattern matches.</returns>
    public PatternSetMatch? Find(ReadOnlySpan<byte> haystack)
    {
        return Find(haystack, startAt: 0);
    }

    /// <summary>
    /// Finds the leftmost match across all patterns at or after a byte offset, using pattern order to break ties.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <param name="startAt">The first byte offset to consider.</param>
    /// <returns>The selected match, or <see langword="null" /> when no pattern matches.</returns>
    public PatternSetMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        PatternSetMatch? exact = literalAccelerator?.FindAt(haystack, startOffset);
        ReadOnlySpan<int> exactAutomata = GetExactAutomataForStart(haystack, startOffset);
        for (int candidateIndex = 0; candidateIndex < exactAutomata.Length; candidateIndex++)
        {
            int index = exactAutomata[candidateIndex];
            if (exact.HasValue && automataPatternIds[index] >= exact.Value.PatternId)
            {
                break;
            }

            RegexMatch? match = automata[index].MatchAt(haystack, startOffset);
            if (!match.HasValue)
            {
                continue;
            }

            return new PatternSetMatch(automataPatternIds[index], match.Value);
        }

        if (exact.HasValue)
        {
            return exact;
        }

        PatternSetMatch? best = null;
        if (literalAccelerator is not null)
        {
            PatternSetMatch? literalMatch = literalAccelerator.Find(haystack[startOffset..]);
            if (literalMatch.HasValue)
            {
                best = new PatternSetMatch(
                    literalMatch.Value.PatternId,
                    new RegexMatch(literalMatch.Value.Match.Start + startOffset, literalMatch.Value.Match.Length));
            }
        }

        if (requiredLiteralAccelerator is not null)
        {
            best = requiredLiteralAccelerator.Find(haystack, startOffset, automata, automataPatternIds, best);
        }

        for (int index = 0; index < automata.Length; index++)
        {
            if (requiredLiteralAccelerator?.CoversAutomaton(index) == true)
            {
                continue;
            }

            RegexMatch? match = automata[index].Find(haystack, startOffset);
            if (!match.HasValue)
            {
                continue;
            }

            var candidate = new PatternSetMatch(automataPatternIds[index], match.Value);
            if (IsBetter(candidate, best))
            {
                best = candidate;
            }
        }

        return best;
    }

    private ReadOnlySpan<int> GetExactAutomataForStart(ReadOnlySpan<byte> haystack, int startOffset)
    {
        if (exactAutomataByFirstByte is null || startOffset >= haystack.Length)
        {
            return allAutomataIndexes;
        }

        return exactAutomataByFirstByte[haystack[startOffset]];
    }

    private static int[][]? BuildExactAutomataByFirstByte(RegexAutomaton[] automata)
    {
        if (automata.Length == 0)
        {
            return null;
        }

        var buckets = new List<int>[256];
        for (int index = 0; index < buckets.Length; index++)
        {
            buckets[index] = [];
        }

        for (int automatonIndex = 0; automatonIndex < automata.Length; automatonIndex++)
        {
            bool[] firstBytes = new bool[256];
            if (!automata[automatonIndex].TryAddStartBytes(firstBytes))
            {
                for (int value = 0; value <= byte.MaxValue; value++)
                {
                    buckets[value].Add(automatonIndex);
                }

                continue;
            }

            for (int value = 0; value <= byte.MaxValue; value++)
            {
                if (firstBytes[value])
                {
                    buckets[value].Add(automatonIndex);
                }
            }
        }

        int[][] indexed = new int[256][];
        for (int index = 0; index < indexed.Length; index++)
        {
            indexed[index] = buckets[index].ToArray();
        }

        return indexed;
    }

    private static int[] BuildAllAutomataIndexes(int count)
    {
        int[] indexes = new int[count];
        for (int index = 0; index < indexes.Length; index++)
        {
            indexes[index] = index;
        }

        return indexes;
    }

    private long IterateNonOverlapping(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        int suppressedEmptyStart = -1;
        while (offset <= haystack.Length)
        {
            PatternSetMatch? match = Find(haystack, offset);
            if (!match.HasValue)
            {
                return total;
            }

            RegexMatch span = match.Value.Match;
            if (span.Length == 0 && span.Start == suppressedEmptyStart)
            {
                offset = Math.Min(span.Start + 1, haystack.Length + 1);
                suppressedEmptyStart = -1;
                continue;
            }

            total += sumSpans ? span.Length : 1;
            if (span.Length == 0)
            {
                suppressedEmptyStart = -1;
                offset = Math.Min(span.End + 1, haystack.Length + 1);
            }
            else
            {
                suppressedEmptyStart = Math.Min(span.End, haystack.Length + 1);
                offset = suppressedEmptyStart;
            }
        }

        return total;
    }

    /// <summary>
    /// Returns pattern identifiers whose regex matches the haystack.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns>Matching pattern identifiers in insertion order.</returns>
    public IReadOnlyList<int> MatchingPatternIds(ReadOnlySpan<byte> haystack)
    {
        bool[] matched = new bool[count];
        literalAccelerator?.MarkMatchingPatternIds(haystack, matched);
        for (int index = 0; index < automata.Length; index++)
        {
            if (automata[index].IsMatch(haystack))
            {
                matched[automataPatternIds[index]] = true;
            }
        }

        var patternIds = new List<int>();
        for (int index = 0; index < matched.Length; index++)
        {
            if (matched[index])
            {
                patternIds.Add(index);
            }
        }

        return patternIds;
    }

    internal static bool IsBetter(PatternSetMatch candidate, PatternSetMatch? best)
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

    private static bool TryGetLiteralPattern(byte[] pattern, out byte[] literal)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(pattern);
        var bytes = new List<byte>();
        if (TryAppendLiteralPattern(tree.Root, bytes))
        {
            literal = bytes.ToArray();
            return true;
        }

        literal = [];
        return false;
    }

    private static bool TryAppendLiteralPattern(RegexSyntaxNode node, List<byte> bytes)
    {
        switch (node.Kind)
        {
            case RegexSyntaxKind.Empty:
                return true;
            case RegexSyntaxKind.Literal:
                bytes.AddRange(((RegexAtomNode)node).Value.ToArray());
                return true;
            case RegexSyntaxKind.Sequence:
                var sequence = (RegexSequenceNode)node;
                for (int index = 0; index < sequence.Nodes.Count; index++)
                {
                    if (!TryAppendLiteralPattern(sequence.Nodes[index], bytes))
                    {
                        return false;
                    }
                }

                return true;
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return string.IsNullOrEmpty(group.EnabledFlags) &&
                    string.IsNullOrEmpty(group.DisabledFlags) &&
                    TryAppendLiteralPattern(group.Child, bytes);
            default:
                return false;
        }
    }

    private static bool TryPrepareLiteralPatterns(byte[] literal, RegexCompileOptions options, out byte[][] prepared)
    {
        if (!options.CaseInsensitive)
        {
            prepared = [literal];
            return true;
        }

        return RegexPrefilter.TryPreparePrefixLiteralSet([literal], options, out prepared);
    }

    private static int TryGetRequiredLiteralLookBehind(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        byte[][] requiredLiterals)
    {
        if (options.UnicodeClasses ||
            !RegexPrefilter.TryCollectRequiredLiteralSetWithLookBehind(root, options, out byte[][] boundedLiterals, out int maxLookBehind) ||
            !LiteralSetsEqual(requiredLiterals, boundedLiterals))
        {
            return RegexPrefilter.RequiredLiteralLookBehind;
        }

        return Math.Min(maxLookBehind, RegexPrefilter.RequiredLiteralLookBehind);
    }

    private static bool LiteralSetsEqual(byte[][] left, byte[][] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int index = 0; index < left.Length; index++)
        {
            if (!left[index].AsSpan().SequenceEqual(right[index]))
            {
                return false;
            }
        }

        return true;
    }
}
