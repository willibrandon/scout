namespace Scout;

internal sealed class RegexRunLiteralDotStarEngine
{
    private const int MinimumLiteralLength = 3;

    private readonly RegexSimpleSequenceSegment leadingRun;
    private readonly byte[] literal;
    private readonly int literalAnchorIndex;
    private readonly byte literalAnchorByte;
    private readonly bool dotMatchesNewline;
    private readonly byte lineTerminator;

    private RegexRunLiteralDotStarEngine(
        RegexSimpleSequenceSegment leadingRun,
        byte[] literal,
        RegexCompileOptions options)
    {
        this.leadingRun = leadingRun;
        this.literal = literal;
        literalAnchorIndex = literal.Length - 1;
        literalAnchorByte = literal[literalAnchorIndex];
        dotMatchesNewline = options.DotMatchesNewline;
        lineTerminator = options.LineTerminator;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexRunLiteralDotStarEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive ||
            options.Crlf ||
            options.Utf8 ||
            options.UnicodeClasses)
        {
            return false;
        }

        root = UnwrapTransparentGroups(root);
        if (root is not RegexSequenceNode sequence ||
            sequence.Nodes.Count < 3 ||
            !TryCreateLeadingRun(sequence.Nodes[0], options, out RegexSimpleSequenceSegment leadingRun) ||
            !IsGreedyDotStar(sequence.Nodes[^1]) ||
            !TryCollectLiteral(sequence, out byte[] literal) ||
            literal.Length < MinimumLiteralLength)
        {
            return false;
        }

        engine = new RegexRunLiteralDotStarEngine(leadingRun, literal, options);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int lowerBound = Math.Clamp(startAt, 0, haystack.Length);
        int literalStart = FindLiteral(haystack, lowerBound, haystack.Length);
        if (literalStart < 0)
        {
            return null;
        }

        int runStart = FindRunStart(haystack, literalStart, lowerBound);
        int matchStart = Math.Max(runStart, lowerBound);
        int matchEnd = FindLineEnd(haystack, literalStart + literal.Length);
        return new RegexMatch(matchStart, matchEnd - matchStart);
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

        int runEnd = start;
        while (runEnd < haystack.Length && leadingRun.AtomMatches(haystack[runEnd]))
        {
            runEnd++;
        }

        int literalStart = FindLiteral(haystack, start, runEnd);
        if (literalStart < 0)
        {
            return false;
        }

        int matchEnd = FindLineEnd(haystack, literalStart + literal.Length);
        length = matchEnd - start;
        return true;
    }

    private long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long count = 0;
        long spanSum = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (Find(haystack, offset) is RegexMatch match)
        {
            count++;
            if (sumSpans)
            {
                spanSum += match.Length;
            }

            offset = match.Length == 0 ? Math.Min(match.End + 1, haystack.Length + 1) : match.End;
        }

        return sumSpans ? spanSum : count;
    }

    private int FindLiteral(ReadOnlySpan<byte> haystack, int start, int end)
    {
        int searchAt = start + literalAnchorIndex;
        while (searchAt < end)
        {
            int offset = haystack[searchAt..end].IndexOf(literalAnchorByte);
            if (offset < 0)
            {
                return -1;
            }

            int anchor = searchAt + offset;
            int literalStart = anchor - literalAnchorIndex;
            if (literalStart >= start &&
                literal.Length <= end - literalStart &&
                haystack.Slice(literalStart, literal.Length).SequenceEqual(literal))
            {
                return literalStart;
            }

            searchAt = anchor + 1;
        }

        return -1;
    }

    private int FindRunStart(ReadOnlySpan<byte> haystack, int literalStart, int lowerBound)
    {
        int runStart = literalStart;
        while (runStart > lowerBound && leadingRun.AtomMatches(haystack[runStart - 1]))
        {
            runStart--;
        }

        return runStart;
    }

    private int FindLineEnd(ReadOnlySpan<byte> haystack, int start)
    {
        if (dotMatchesNewline)
        {
            return haystack.Length;
        }

        int offset = haystack[start..].IndexOf(lineTerminator);
        return offset < 0 ? haystack.Length : start + offset;
    }

    private static bool TryCreateLeadingRun(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexSimpleSequenceSegment segment)
    {
        segment = default;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexRepetitionNode { Minimum: 0, Maximum: null, Lazy: false } repetition ||
            !TryCreateSegment(repetition.Child, options, out segment))
        {
            return false;
        }

        return segment.MatcherKind != RegexSimpleSequenceByteMatcherKind.Any;
    }

    private static bool TryCreateSegment(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexSimpleSequenceSegment segment)
    {
        segment = default;
        node = UnwrapTransparentGroups(node);
        if (node is not RegexAtomNode atom ||
            IsPredicate(atom.Kind) ||
            atom.Kind == RegexSyntaxKind.Literal && atom.Value.Length != 1 ||
            RegexByteClass.RequiresUtf8ScalarMatch(
                atom.Kind,
                atom.Value.Span,
                options.Utf8,
                options.CaseInsensitive,
                options.UnicodeClasses))
        {
            return false;
        }

        segment = new RegexSimpleSequenceSegment(
            atom.Kind,
            atom.Value.ToArray(),
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            minimum: 1,
            maximum: 1,
            lazy: false);
        return true;
    }

    private static bool TryCollectLiteral(RegexSequenceNode sequence, out byte[] literal)
    {
        literal = [];
        var bytes = new List<byte>();
        for (int index = 1; index < sequence.Nodes.Count - 1; index++)
        {
            if (UnwrapTransparentGroups(sequence.Nodes[index]) is not RegexAtomNode
                {
                    Kind: RegexSyntaxKind.Literal,
                } atom)
            {
                return false;
            }

            bytes.AddRange(atom.Value.ToArray());
        }

        literal = bytes.ToArray();
        return literal.Length > 0;
    }

    private static bool IsGreedyDotStar(RegexSyntaxNode node)
    {
        return UnwrapTransparentGroups(node) is RegexRepetitionNode
        {
            Child.Kind: RegexSyntaxKind.Dot,
            Minimum: 0,
            Maximum: null,
            Lazy: false,
        };
    }

    private static bool IsPredicate(RegexSyntaxKind kind)
    {
        return kind is RegexSyntaxKind.StartAnchor
            or RegexSyntaxKind.EndAnchor
            or RegexSyntaxKind.AbsoluteStartAnchor
            or RegexSyntaxKind.AbsoluteEndAnchor
            or RegexSyntaxKind.WordBoundary
            or RegexSyntaxKind.NotWordBoundary
            or RegexSyntaxKind.WordStartBoundary
            or RegexSyntaxKind.WordEndBoundary
            or RegexSyntaxKind.WordStartHalfBoundary
            or RegexSyntaxKind.WordEndHalfBoundary;
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
