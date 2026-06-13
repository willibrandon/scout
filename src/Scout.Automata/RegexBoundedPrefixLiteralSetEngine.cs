namespace Scout;

internal sealed class RegexBoundedPrefixLiteralSetEngine
{
    private const int MaxPrefix = 32;

    private readonly byte[][] literals;
    private readonly RegexPackedLiteralSetScanner? packedScanner;
    private readonly RegexCaseSensitiveLiteralSetScanner? scanner;
    private readonly MemmemFinder? singleLiteralFinder;
    private readonly int minimum;
    private readonly int maximum;
    private readonly byte lineTerminator;

    private RegexBoundedPrefixLiteralSetEngine(
        byte[][] literals,
        RegexPackedLiteralSetScanner? packedScanner,
        RegexCaseSensitiveLiteralSetScanner? scanner,
        int minimum,
        int maximum,
        byte lineTerminator)
    {
        this.literals = literals;
        this.packedScanner = packedScanner;
        this.scanner = scanner;
        this.minimum = minimum;
        this.maximum = maximum;
        this.lineTerminator = lineTerminator;
        if (literals.Length == 1)
        {
            singleLiteralFinder = new MemmemFinder(literals[0]);
        }
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexBoundedPrefixLiteralSetEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive ||
            options.Utf8 ||
            options.UnicodeClasses ||
            options.DotMatchesNewline ||
            options.Crlf ||
            options.SwapGreed)
        {
            return false;
        }

        root = UnwrapTransparentGroups(root);
        if (root is not RegexSequenceNode { Nodes.Count: 2 } sequence ||
            !TryGetBoundedDotRepetition(sequence.Nodes[0], out int minimum, out int maximum) ||
            maximum > MaxPrefix ||
            !TryGetLiteralAlternatives(sequence.Nodes[1], out byte[][] literals))
        {
            return false;
        }

        RegexPackedLiteralSetScanner? packedScanner = null;
        RegexCaseSensitiveLiteralSetScanner? scanner = null;
        if (literals.Length > 1 &&
            !RegexPackedLiteralSetScanner.TryCreate(literals, out packedScanner) &&
            !RegexCaseSensitiveLiteralSetScanner.TryCreate(literals, out scanner))
        {
            return false;
        }

        engine = new RegexBoundedPrefixLiteralSetEngine(
            literals,
            packedScanner,
            scanner,
            minimum,
            maximum,
            options.LineTerminator);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int lowerBound = Math.Clamp(startAt, 0, haystack.Length);
        int nextStartToTry = lowerBound;
        int literalSearchAt = Math.Min(haystack.Length, lowerBound + minimum);
        while (TryFindLiteral(haystack, literalSearchAt, out RegexLiteralSetCandidate candidate))
        {
            int literalStart = candidate.Match.Start;
            int firstStart = Math.Max(nextStartToTry, literalStart - maximum);
            int lastStart = Math.Min(literalStart - minimum, haystack.Length);
            for (int start = firstStart; start <= lastStart; start++)
            {
                if (TryMatchAt(haystack, start, out int length))
                {
                    return new RegexMatch(start, length);
                }
            }

            nextStartToTry = Math.Max(nextStartToTry, lastStart + 1);
            literalSearchAt = literalStart + 1;
        }

        return null;
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        return TryMatchAt(haystack, start, out int length)
            ? new RegexMatch(start, length)
            : null;
    }

    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSum(haystack, startAt, sumSpans: false);
    }

    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSum(haystack, startAt, sumSpans: true);
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        if ((uint)start > (uint)haystack.Length)
        {
            return false;
        }

        int maxGap = Math.Min(maximum, haystack.Length - start);
        int lineTerminatorOffset = haystack.Slice(start, maxGap).IndexOf(lineTerminator);
        if (lineTerminatorOffset >= 0)
        {
            maxGap = lineTerminatorOffset;
        }

        if (maxGap < minimum)
        {
            return false;
        }

        for (int gap = maxGap; gap >= minimum; gap--)
        {
            int literalStart = start + gap;
            for (int index = 0; index < literals.Length; index++)
            {
                byte[] literal = literals[index];
                if (LiteralMatchesAt(haystack, literalStart, literal))
                {
                    length = gap + literal.Length;
                    return true;
                }
            }
        }

        return false;
    }

    private long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (Find(haystack, offset) is RegexMatch match)
        {
            total += sumSpans ? match.Length : 1;
            offset = match.End;
        }

        return total;
    }

    private bool TryFindLiteral(
        ReadOnlySpan<byte> haystack,
        int startAt,
        out RegexLiteralSetCandidate candidate)
    {
        if (scanner is not null)
        {
            RegexLiteralSetCandidate? found = scanner.Find(haystack, startAt);
            candidate = found.GetValueOrDefault();
            return found.HasValue;
        }

        if (packedScanner is not null)
        {
            RegexLiteralSetCandidate? found = packedScanner.Find(haystack, startAt);
            candidate = found.GetValueOrDefault();
            return found.HasValue;
        }

        int start = Math.Clamp(startAt, 0, haystack.Length);
        int offset = singleLiteralFinder!.Find(haystack[start..]);
        if (offset < 0)
        {
            candidate = default;
            return false;
        }

        candidate = new RegexLiteralSetCandidate(0, new RegexMatch(start + offset, literals[0].Length));
        return true;
    }

    private static bool TryGetBoundedDotRepetition(RegexSyntaxNode node, out int minimum, out int maximum)
    {
        minimum = 0;
        maximum = 0;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexRepetitionNode
            {
                Minimum: >= 0,
                Maximum: { } candidateMaximum,
                Lazy: false,
            } repetition ||
            candidateMaximum < repetition.Minimum ||
            UnwrapTransparentGroups(repetition.Child) is not RegexAtomNode { Kind: RegexSyntaxKind.Dot })
        {
            return false;
        }

        minimum = repetition.Minimum;
        maximum = candidateMaximum;
        return true;
    }

    private static bool TryGetLiteralAlternatives(RegexSyntaxNode node, out byte[][] literals)
    {
        node = UnwrapTransparentGroups(node);
        var collected = new List<byte[]>();
        if (node is RegexAlternationNode alternation)
        {
            for (int index = 0; index < alternation.Alternatives.Count; index++)
            {
                if (!TryGetLiteral(alternation.Alternatives[index], out byte[] literal))
                {
                    literals = [];
                    return false;
                }

                AddDistinctLiteral(collected, literal);
            }
        }
        else if (TryGetLiteral(node, out byte[] literal))
        {
            AddDistinctLiteral(collected, literal);
        }
        else
        {
            literals = [];
            return false;
        }

        literals = collected.ToArray();
        return literals.Length > 0;
    }

    private static bool TryGetLiteral(RegexSyntaxNode node, out byte[] literal)
    {
        var bytes = new List<byte>();
        if (!TryAppendLiteral(node, bytes) || bytes.Count == 0)
        {
            literal = [];
            return false;
        }

        literal = bytes.ToArray();
        return true;
    }

    private static bool TryAppendLiteral(RegexSyntaxNode node, List<byte> literal)
    {
        node = UnwrapTransparentGroups(node);
        if (node is RegexSequenceNode sequence)
        {
            for (int index = 0; index < sequence.Nodes.Count; index++)
            {
                if (!TryAppendLiteral(sequence.Nodes[index], literal))
                {
                    return false;
                }
            }

            return true;
        }

        if (node is not RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom ||
            atom.Value.Length == 0)
        {
            return false;
        }

        ReadOnlySpan<byte> value = atom.Value.Span;
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] > 0x7F)
            {
                return false;
            }

            literal.Add(value[index]);
        }

        return true;
    }

    private static bool LiteralMatchesAt(ReadOnlySpan<byte> haystack, int start, byte[] literal)
    {
        return (uint)start <= (uint)haystack.Length &&
            literal.Length <= haystack.Length - start &&
            haystack.Slice(start, literal.Length).SequenceEqual(literal);
    }

    private static void AddDistinctLiteral(List<byte[]> literals, byte[] literal)
    {
        for (int index = 0; index < literals.Count; index++)
        {
            if (literals[index].AsSpan().SequenceEqual(literal))
            {
                return;
            }
        }

        literals.Add(literal);
    }

    private static RegexSyntaxNode UnwrapTransparentGroups(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode group &&
            string.IsNullOrEmpty(group.EnabledFlags) &&
            string.IsNullOrEmpty(group.DisabledFlags))
        {
            node = group.Child;
        }

        return node;
    }
}
